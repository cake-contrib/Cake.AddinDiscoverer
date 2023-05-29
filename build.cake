
// Install tools.
#tool nuget:?package=GitVersion.CommandLine&version=5.12.0


///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument<string>("target", "Default");
var configuration = Argument<string>("configuration", "Release");
var clearCache = Argument<bool>("clearcache", false);


///////////////////////////////////////////////////////////////////////////////
// GLOBAL VARIABLES
///////////////////////////////////////////////////////////////////////////////

var appName = "Cake.AddinDiscoverer";
var gitHubRepo = "Cake.AddinDiscoverer";

var gitHubToken = Argument<string>("GITHUB_TOKEN", EnvironmentVariable("GITHUB_TOKEN"));
var gitHubUserName = Argument<string>("GITHUB_USERNAME", EnvironmentVariable("GITHUB_USERNAME"));
var gitHubPassword = Argument<string>("GITHUB_PASSWORD", EnvironmentVariable("GITHUB_PASSWORD"));

var sourceFolder = "./Source/";
var outputDir = "./artifacts/";
var publishDir = $"{outputDir}Publish/";

var versionInfo = GitVersion(new GitVersionSettings() { OutputType = GitVersionOutput.Json });
var cakeVersion = typeof(ICakeContext).Assembly.GetName().Version.ToString();
var isLocalBuild = BuildSystem.IsLocalBuild;
var isMainBranch = StringComparer.OrdinalIgnoreCase.Equals("main", BuildSystem.AppVeyor.Environment.Repository.Branch);
var isMainRepo = StringComparer.OrdinalIgnoreCase.Equals($"{gitHubUserName}/{gitHubRepo}", BuildSystem.AppVeyor.Environment.Repository.Name);
var isPullRequest = BuildSystem.AppVeyor.Environment.PullRequest.IsPullRequest;
var isTagged = (
	BuildSystem.AppVeyor.Environment.Repository.Tag.IsTag &&
	!string.IsNullOrWhiteSpace(BuildSystem.AppVeyor.Environment.Repository.Tag.Name)
);


///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(context =>
{
	if (!isLocalBuild && (context.Log.Verbosity != Verbosity.Diagnostic))
	{
		Information("Increasing verbosity to diagnostic.");
		context.Log.Verbosity = Verbosity.Diagnostic;
	}

	Information("Building version {0} of {1} ({2}, {3}) using version {4} of Cake",
		versionInfo.LegacySemVerPadded,
		appName,
		configuration,
		target,
		cakeVersion
	);

	Information("Variables:\r\n\tLocalBuild: {0}\r\n\tIsMainBranch: {1}\r\n\tIsMainRepo: {2}\r\n\tIsPullRequest: {3}\r\n\tIsTagged: {4}",
		isLocalBuild,
		isMainBranch,
		isMainRepo,
		isPullRequest,
		isTagged
	);

	if (!string.IsNullOrEmpty(gitHubToken))
	{
		Information("GitHub Info:\r\n\tRepo: {0}\r\n\tUserName: {1}\r\n\tToken: {2}",
			gitHubRepo,
			gitHubUserName,
			new string('*', gitHubToken.Length)
		);
	}
	else
	{
		Information("GitHub Info:\r\n\tRepo: {0}\r\n\tUserName: {1}\r\n\tPassword: {2}",
			gitHubRepo,
			gitHubUserName,
			string.IsNullOrEmpty(gitHubPassword) ? "[NULL]" : new string('*', gitHubPassword.Length)
		);
	}
});

Teardown(context =>
{
	// Executed AFTER the last task.
	Information("Finished running tasks.");
});


///////////////////////////////////////////////////////////////////////////////
// TASK DEFINITIONS
///////////////////////////////////////////////////////////////////////////////

Task("AppVeyor-Build_Number")
	.WithCriteria(() => AppVeyor.IsRunningOnAppVeyor)
	.Does(() =>
{
	GitVersion(new GitVersionSettings()
	{
		UpdateAssemblyInfo = false,
		OutputType = GitVersionOutput.BuildServer
	});
});

Task("Clean")
	.IsDependentOn("AppVeyor-Build_Number")
	.Does(() =>
{
	// Clean solution directories.
	Information("Cleaning {0}", sourceFolder);
	CleanDirectories($"{sourceFolder}*/bin/{configuration}");
	CleanDirectories($"{sourceFolder}*/obj/{configuration}");

	// Clean previous artifacts
	Information("Cleaning {0}", outputDir);
	if (DirectoryExists(outputDir)) CleanDirectories(MakeAbsolute(Directory(outputDir)).FullPath);
	else CreateDirectory(outputDir);
});

Task("Restore-NuGet-Packages")
	.IsDependentOn("Clean")
	.Does(() =>
{
	DotNetRestore(sourceFolder, new DotNetRestoreSettings
	{
		Sources = new [] {
			"https://api.nuget.org/v3/index.json"
		}
	});
});

Task("Build")
	.IsDependentOn("Restore-NuGet-Packages")
	.Does(() =>
{
	DotNetBuild($"{sourceFolder}{appName}.sln", new DotNetBuildSettings
	{
		Configuration = configuration,
		NoRestore = true,
		ArgumentCustomization = args => args.Append($"/p:SemVer={versionInfo.LegacySemVerPadded}")
	});
});

Task("Publish")
	.IsDependentOn("Build")
	.Does(() =>
{
	DotNetPublish($"{sourceFolder}{appName}.sln", new DotNetPublishSettings
	{
		Configuration = configuration,
		NoBuild = true,
		NoRestore = true,
		OutputDirectory = publishDir
	});
});

Task("Run")
	.IsDependentOn("Publish")
	.WithCriteria(() => isLocalBuild || !isPullRequest)
	.Does(() =>
{
	var args = new ProcessArgumentBuilder()
		.AppendSwitchQuoted("-t", outputDir) // "Folder where temporary files (including reports) are saved."
		.AppendSwitchQuoted("-u", gitHubUserName) // "Github username."
		.AppendSwitchQuotedSecret("-p", gitHubPassword); // "Github password."

	if (clearCache) args.Append("-c");
	if (isMainBranch)
	{
		args.Append("-e"); // "Generate the Excel report and write to a file."
		args.Append("-k"); // "Update addin references in CakeRecipe."
		args.Append("-m"); // "Generate the Markdown report and write to a file."
		args.Append("-n"); // "Synchronize the list of contributors."
		args.Append("-r"); // "Commit reports and other files to cake-contrib repo."
		args.Append("-s"); // "Synchronize the yaml files on Cake's web site with the packages discovered on NuGet."
	}
	else
	{
		args.Append("-e"); // "Generate the Excel report and write to a file."
		args.Append("-m"); // "Generate the Markdown report and write to a file."
		args.Append("-w"); // Exclude slow steps so we fail fast while investigating the timeout issue on AppVeyor
	}

	// Execute the command
	using (DiagnosticVerbosity())
	{
		var processResult = StartProcess(
			new FilePath($"{publishDir}{appName}.exe"),
			new ProcessSettings()
			{
				Arguments = args
			});
		if (processResult != 0)
		{
			throw new Exception($"{appName} did not complete successfully. Result code: {processResult}");
		}
	}
});

Task("Upload-Artifacts")
	.IsDependentOn("Run")
	.WithCriteria(() => AppVeyor.IsRunningOnAppVeyor)
	.Does(() =>
{
	var markdownReport = $"{outputDir}{appName}/AddinDiscoveryReport.md";
	if (FileExists(markdownReport))
	{
		AppVeyor.UploadArtifact(markdownReport);
	}

	var excelReport = $"{outputDir}{appName}/AddinDiscoveryReport.xlsx";
	if (FileExists(excelReport))
	{
		AppVeyor.UploadArtifact(excelReport);
    }
});


///////////////////////////////////////////////////////////////////////////////
// TARGETS
///////////////////////////////////////////////////////////////////////////////

Task("AppVeyor")
	.IsDependentOn("Upload-Artifacts");

Task("Default")
	.IsDependentOn("Run");


///////////////////////////////////////////////////////////////////////////////
// EXECUTION
///////////////////////////////////////////////////////////////////////////////

RunTarget(target);
