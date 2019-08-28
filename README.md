# Cake.AddinDiscoverer
Tool to aid with discovering information about Cake Addins

## Steps
This console application audits Cake addins discovered on NuGet.org and generates a report to indicate if they follow recommended guidelines. 
The AddinDiscoverer searches nuget.org for packages that follow the recommended naming convention which is `Cake.xxx` for addins and recipes and `Cake.xxx.Module` for modules

## Best practices

The best practice this tool inspects for are:

1. Your plugin references the appropriate version of the Cake DLLs.
2. The references to the Cake DLLs are private
3. Your plugin does not target multiple .NET frameworks and only targets `netstandard2.0`
4. Your plugin uses the "cake-contrib" icon
5. The project has been moved to the cake-contrib organisation

## Command Line arguments

You can invoke this tool with the following arguments:

```csharp
  -t, --tempfolder          Folder where temporary files (including reports) are saved.
  -a, --addinname           Name of the specific addin to be audited. If omitted, all addins are audited.
  -u, --user                Github username.
  -p, --password            Github password.
  -y, --proxy               The URL of your proxy. For example, to proxy request through Fiddler use: 'http://localhost:8888'.
  -c, --clearcache          (Default: false) Clear the list of addins that was previously cached.
  -i, --issue               (Default: false) Create issue in Github repositories that do not meet recommendations.
  -q, --pullrequest         (Default: false) Submit pull request in Github repositories to fix recommendations.
  -e, --exceltofile         (Default: false) Generate the Excel report and write to a file.
  -x, --exceltorepo         (Default: false) Generate the Excel report and commit to cake-contrib repo.
  -m, --markdowntofile      (Default: false) Generate the Markdown report and write to a file.
  -r, --markdowntorepo      (Default: false) Generate the Markdown report and commit to cake-contrib repo.
  -s, --syncyaml            (Default: false) Synchronize the yaml files on Cake's web site with the packages discovered on NuGet.
  -k, --updatecakerecipe    (Default: false) Update addin references in CakeRecipe.
  -w, --excludeslowsteps    (Default: false) Exclude step that take much time (such as GetGithubStats and CheckUsingCakeRecipe).
  --help                    Display this help screen.
  --version                 Display version information.
```

## Important note

This tool caches certain information such as the list of discovered addins, the content of `.sln` and `.csproj` file for performance reasons and also to ensure invoking this tool repeatedly does not cause you to exceed the number of GitHub API calls you are allowed to make in an hour. Make sure you add `-c` when invoking this tool when you want the previously cached information to be deleted and re-discover the list of plugins and re-download their solution and project files.

If you specify `-i` to create a new issue in the addin Github repo, this tool will attempt to detect if an opened issue already exist to avoid creating duplicate issues.

## What is automated

As of version 3.4.0 we have automated the following:
1. Addin report
	- Discover all existing addins on nuget
	- Exclude a few well-known packages (via a manually maintained "black list" file)
	- Generate a markdown report
	- Generate an Excel report
	- Generate graph showing progress over time
	- Commit the generated files to the `cake-contrib/home` repo
2. Synchronize YAML files
	- Create YAML file for addins that do not already one
	- Update existing YAML file when metadata for a given addin package has changed
	- Delete YAML file when addin package is removed from nuget
	- Create issue and submit PR in `cake-build/website` with all the deleted/modified/created YAML files
	- PR must be reviewed by Cake staff
	- Do not create any new issue until previous one is closed
	- Keep in mind that I am arbitrarily restricting the number of YAML file deleted, created or updated in order to avoid triggering github's abuse protection
3. Maintain Cake.Recipe references
	- Detect which version of Cake is used by Cake.Recipe (https://github.com/cake-contrib/Cake.Recipe/blob/develop/tools/packages.config)
	- Make sure Cake.Recipe references the latest version of all addins compatible with the previously determined version of Cake
	- Create issue and submit PR in `cake-contrib/recipe`
	- PR must be reviewed by Cake staff
4. Upgrade the version of Cake used by Cake.Recipe
	- Determine if there is a newer version of Cake with breaking changes than what Cake.Recipe is currently using
	- Determine if ALL referenced addins have been updated to support the newer version of Cake
	- Create issue and submit PR in `cake-contrib/recipe`
	- PR must be reviewed by Cake staff
