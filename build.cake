// Install tools.
#tool dotnet:?package=GitVersion.Tool&version=5.12.0


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

var buildBranch = Context.GetBuildBranch();
var repoName = Context.GetRepoName();

var versionInfo = (GitVersion)null; // Will be calculated in SETUP

var cakeVersion = typeof(ICakeContext).Assembly.GetName().Version.ToString();
var isLocalBuild = BuildSystem.IsLocalBuild;
var isMainBranch = StringComparer.OrdinalIgnoreCase.Equals("main", buildBranch);
var isMainRepo = StringComparer.OrdinalIgnoreCase.Equals($"{gitHubUserName}/{gitHubRepo}", repoName);
var isPullRequest = BuildSystem.AppVeyor.Environment.PullRequest.IsPullRequest;
var isTagged = BuildSystem.AppVeyor.Environment.Repository.Tag.IsTag && !string.IsNullOrWhiteSpace(BuildSystem.AppVeyor.Environment.Repository.Tag.Name);


///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(context =>
{
	if (!isLocalBuild && context.Log.Verbosity != Verbosity.Diagnostic)
	{
		Information("Increasing verbosity to diagnostic.");
		context.Log.Verbosity = Verbosity.Diagnostic;
	}

	Information("Calculating version info...");
	versionInfo = GitVersion(new GitVersionSettings() { OutputType = GitVersionOutput.Json });

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
	if (DirectoryExists(outputDir))
	{
		SafeCleanDirectory(outputDir, false);
	}
	else
	{
		CreateDirectory(outputDir);
	}
});

Task("Restore-NuGet-Packages")
	.IsDependentOn("Clean")
	.Does(() =>
{
	DotNetRestore(sourceFolder, new DotNetRestoreSettings
	{
		Runtime = "win-x64",
		Sources = new [] { "https://api.nuget.org/v3/index.json", }
	});
});

// Combining "build" and "publish" in a single task otherwise you get exeption from dotnet due to publishing to a single file.
// More details here: https://github.com/orgs/cake-build/discussions/4280
Task("Publish")
	.IsDependentOn("Restore-NuGet-Packages")
	.Does(() =>
{
	DotNetPublish($"{sourceFolder}{appName}.sln", new DotNetPublishSettings
	{
		Configuration = configuration,
		NoBuild = false,
		NoRestore = true,
		PublishSingleFile = true,
		SelfContained = true,
		ArgumentCustomization = args => args.Append($"-p:SemVer={versionInfo.LegacySemVerPadded}")
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
	var artifacts = new string[]
	{
		 "Audit.xlsx",
		 "Audit.md",
		 "Analysis_result.json"
	};

	foreach (var artifact in artifacts)
	{
		var path = $"{outputDir}{appName}/{artifact}";
		if (FileExists(path))
		{
			AppVeyor.UploadArtifact(path);
		}
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



///////////////////////////////////////////////////////////////////////////////
// PRIVATE METHODS
///////////////////////////////////////////////////////////////////////////////
private static string TrimStart(this string source, string value, StringComparison comparisonType)
{
	if (source == null)
	{
		throw new ArgumentNullException(nameof(source));
	}

	int valueLength = value.Length;
	int startIndex = 0;
	while (source.IndexOf(value, startIndex, comparisonType) == startIndex)
	{
		startIndex += valueLength;
	}

	return source.Substring(startIndex);
}

private static List<string> ExecuteCommand(this ICakeContext context, FilePath exe, string args)
{
    context.StartProcess(exe, new ProcessSettings { Arguments = args, RedirectStandardOutput = true }, out var redirectedOutput);

    return redirectedOutput.ToList();
}

private static List<string> ExecGitCmd(this ICakeContext context, string cmd)
{
    var gitExe = context.Tools.Resolve(context.IsRunningOnWindows() ? "git.exe" : "git");
    return context.ExecuteCommand(gitExe, cmd);
}

private static string GetBuildBranch(this ICakeContext context)
{
    var buildSystem = context.BuildSystem();
    string repositoryBranch = null;

    if (buildSystem.IsRunningOnAppVeyor) repositoryBranch = buildSystem.AppVeyor.Environment.Repository.Branch;
    else if (buildSystem.IsRunningOnAzurePipelines) repositoryBranch = buildSystem.AzurePipelines.Environment.Repository.SourceBranchName;
    else if (buildSystem.IsRunningOnBamboo) repositoryBranch = buildSystem.Bamboo.Environment.Repository.Branch;
    else if (buildSystem.IsRunningOnBitbucketPipelines) repositoryBranch = buildSystem.BitbucketPipelines.Environment.Repository.Branch;
    else if (buildSystem.IsRunningOnBitrise) repositoryBranch = buildSystem.Bitrise.Environment.Repository.GitBranch;
    else if (buildSystem.IsRunningOnGitHubActions) repositoryBranch = buildSystem.GitHubActions.Environment.Workflow.Ref.Replace("refs/heads/", "");
    else if (buildSystem.IsRunningOnGitLabCI) repositoryBranch = buildSystem.GitLabCI.Environment.Build.RefName;
    else if (buildSystem.IsRunningOnTeamCity) repositoryBranch = buildSystem.TeamCity.Environment.Build.BranchName;
    else if (buildSystem.IsRunningOnTravisCI) repositoryBranch = buildSystem.TravisCI.Environment.Build.Branch;
	else repositoryBranch = ExecGitCmd(context, "rev-parse --abbrev-ref HEAD").Single();

    return repositoryBranch;
}

public static string GetRepoName(this ICakeContext context)
{
    var buildSystem = context.BuildSystem();

    if (buildSystem.IsRunningOnAppVeyor) return buildSystem.AppVeyor.Environment.Repository.Name;
    else if (buildSystem.IsRunningOnAzurePipelines) return buildSystem.AzurePipelines.Environment.Repository.RepoName;
    else if (buildSystem.IsRunningOnTravisCI) return buildSystem.TravisCI.Environment.Repository.Slug;
    else if (buildSystem.IsRunningOnGitHubActions) return buildSystem.GitHubActions.Environment.Workflow.Repository;

	var originUrl = ExecGitCmd(context, "config --get remote.origin.url").Single();
	var parts = originUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);
	return $"{parts[parts.Length - 2]}/{parts[parts.Length - 1].Replace(".git", "")}";
}

// Clean previous artifacts and  make sure to preserve the content
// of folders that are used as caches (such as "packages", "analysis"
// and "archives"). Do not use Cake's "CleanDirectories" alias because
// there is no way to exclude a sub folder which prevents us from
// exluding the subfolders we want to preserve.
private void SafeCleanDirectory(string directoryPath, bool deleteFiles)
{
	foreach (var directory in System.IO.Directory.EnumerateDirectories(directoryPath))
	{
		// Check if this folder is used for caching purposes
		if (directory.EndsWith("packages") || directory.EndsWith("analysis") || directory.EndsWith("archives"))
		{
			Information(" -> Skipping {0}", directory);
		}
		else
		{
			Information(" -> Cleaning {0}", directory);

			// Delete files in this folder
			if (deleteFiles) 
			{
				DeleteFiles($"{directory}/*.*");
			}

			// Clean the subfolders
			SafeCleanDirectory(directory, true);

			// Delete this folder if it's empty
			if (!System.IO.Directory.EnumerateFileSystemEntries(directory).Any())
			{
				System.IO.Directory.Delete(directory, false);
			}
		}
	}
}