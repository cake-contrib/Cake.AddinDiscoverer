
// Install tools.
#tool nuget:?package=GitVersion.CommandLine&version=5.0.0-beta2-97


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

var gitHubUserName = EnvironmentVariable("GITHUB_USERNAME");
var gitHubPassword = EnvironmentVariable("GITHUB_PASSWORD");

var sourceFolder = "./Source/";
var outputDir = "./artifacts/";
var publishDir = $"{outputDir}Publish/";

var versionInfo = GitVersion(new GitVersionSettings() { OutputType = GitVersionOutput.Json });
var cakeVersion = typeof(ICakeContext).Assembly.GetName().Version.ToString();
var isLocalBuild = BuildSystem.IsLocalBuild;
var isMainBranch = StringComparer.OrdinalIgnoreCase.Equals("master", BuildSystem.AppVeyor.Environment.Repository.Branch);
var isMainRepo = StringComparer.OrdinalIgnoreCase.Equals(gitHubUserName + "/" + gitHubRepo, BuildSystem.AppVeyor.Environment.Repository.Name);
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
	if (isMainBranch && (context.Log.Verbosity != Verbosity.Diagnostic))
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

	Information("GitHub Info:\r\n\tRepo: {0}\r\n\tUserName: {1}\r\n\tPassword: {2}",
		gitHubRepo,
		gitHubUserName,
		string.IsNullOrEmpty(gitHubPassword) ? "[NULL]" : new string('*', gitHubPassword.Length)
	);
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
	if (!DirectoryExists(outputDir)) CreateDirectory(outputDir);
	if (DirectoryExists(publishDir)) CleanDirectories(MakeAbsolute(Directory(publishDir)).FullPath);
});

Task("Restore-NuGet-Packages")
	.IsDependentOn("Clean")
	.Does(() =>
{
	DotNetCoreRestore(sourceFolder, new DotNetCoreRestoreSettings
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
	DotNetCoreBuild($"{sourceFolder}{appName}.sln", new DotNetCoreBuildSettings
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
	DotNetCorePublish($"{sourceFolder}{appName}.sln", new DotNetCorePublishSettings
	{
		Configuration = configuration,
		NoRestore = true,
		OutputDirectory = publishDir
	});
});

Task("Run")
	.IsDependentOn("Publish")
	.Does(() =>
{
	var args = new Dictionary<string, string>()
	{
		{ "-t", $"\"{outputDir}\"" },
		{ "-u", $"\"{gitHubUserName}\"" },
		{ "-p", $"\"{gitHubPassword}\"" },
	};
	if (clearCache) args.Add("-c", null);
	if (isMainBranch)
	{
		args.Add("-r", null);
		args.Add("-x", null);
		args.Add("-s", null);
		args.Add("-k", null);
	}
	else
	{
		args.Add("-m", null);
		args.Add("-e", null);
	}

	// Display the command we are about to execute (be careful to avoid displaying the password)
	var safeArgs = args.Where(arg => arg.Key != "-p").Union(new[] { new KeyValuePair<string, string>("-p", "\"<REDACTED>\"") });
	var displayArgs = string.Join(" ", safeArgs.Select(arg => $"{arg.Key} {arg.Value ?? string.Empty}".Trim()));
	Information($"{publishDir}{appName}.exe {displayArgs}");

	// Execute the command
	var processResult = StartProcess(
		new FilePath($"{publishDir}{appName}.exe"),
		new ProcessSettings()
		{
			Arguments = string.Join(" ", args.Select(arg => $"{arg.Key} {arg.Value ?? string.Empty}".Trim()))
		});
	if (processResult != 0)
	{
		throw new Exception($"{appName} did not complete successfully. Result code: {processResult}");
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
