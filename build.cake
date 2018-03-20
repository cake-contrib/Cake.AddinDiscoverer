
// Install tools.
#tool "nuget:?package=GitVersion.CommandLine&version=4.0.0-beta0012"


///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument<string>("target", "Default");
var configuration = Argument<string>("configuration", "Release");


///////////////////////////////////////////////////////////////////////////////
// GLOBAL VARIABLES
///////////////////////////////////////////////////////////////////////////////

var appName = "Cake.AddinDiscoverer";
var gitHubRepo = "Cake.AddinDiscoverer";

var gitHubUserName = EnvironmentVariable("GITHUB_USERNAME");
var gitHubPassword = EnvironmentVariable("GITHUB_PASSWORD");

var sourceFolder = "./Source/";
var outputDir = "./artifacts/";

var versionInfo = GitVersion(new GitVersionSettings() { OutputType = GitVersionOutput.Json });
var milestone = string.Concat("v", versionInfo.MajorMinorPatch);
var cakeVersion = typeof(ICakeContext).Assembly.GetName().Version.ToString();
var isLocalBuild = BuildSystem.IsLocalBuild;
var isMainBranch = StringComparer.OrdinalIgnoreCase.Equals("master", BuildSystem.AppVeyor.Environment.Repository.Branch);
var	isMainRepo = StringComparer.OrdinalIgnoreCase.Equals(gitHubUserName + "/" + gitHubRepo, BuildSystem.AppVeyor.Environment.Repository.Name);
var	isPullRequest = BuildSystem.AppVeyor.Environment.PullRequest.IsPullRequest;
var	isTagged = (
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
	CleanDirectories(sourceFolder + "*/bin/" + configuration);
	CleanDirectories(sourceFolder + "*/obj/" + configuration);

	// Clean previous artifacts
	Information("Cleaning {0}", outputDir);
	if (DirectoryExists(outputDir)) CleanDirectories(MakeAbsolute(Directory(outputDir)).FullPath);
	else CreateDirectory(outputDir);
});

Task("Restore-NuGet-Packages")
	.IsDependentOn("Clean")
	.Does(() =>
{
	DotNetCoreRestore("./Source/", new DotNetCoreRestoreSettings
	{
		Sources = new [] {
			"https://dotnet.myget.org/F/dotnet-core/api/v3/index.json",
			"https://dotnet.myget.org/F/cli-deps/api/v3/index.json",
			"https://api.nuget.org/v3/index.json",
		}
	});
});

Task("Build")
	.IsDependentOn("Restore-NuGet-Packages")
	.Does(() =>
{
	DotNetCoreBuild(sourceFolder + appName + ".sln", new DotNetCoreBuildSettings
	{
		Configuration = configuration,
		NoRestore = true
	});
});

Task("Execute")
	.IsDependentOn("Build")
	.Does(() =>
{
});


///////////////////////////////////////////////////////////////////////////////
// TARGETS
///////////////////////////////////////////////////////////////////////////////

Task("AppVeyor")
	.IsDependentOn("Execute");

Task("Default")
	.IsDependentOn("Build");


///////////////////////////////////////////////////////////////////////////////
// EXECUTION
///////////////////////////////////////////////////////////////////////////////

RunTarget(target);
