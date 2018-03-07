# Cake.AddinDiscoverer
Tool to aid with discovering information about Cake Addins

## Steps
This console application performs the following steps:

1. Discovers the addins listed as YAML files in the `Addins` folder of the `website` repo under the `cake-contrib` organization (https://github.com/cake-build/website/tree/develop/addins)
2. Discovers the addins listed in the `Status.md` file in the `home` repo under the `cake-contrib` organization (https://raw.githubusercontent.com/cake-contrib/Home/master/Status.md'). **PLEASE NOTE**: this file contains several sections such as "Recipes", "Modules", "Websites", "Addins", "Work In Progress", "Needs Investigation" and "Deprecated". I am making the assumption that we only care about addins listed under 3 of those sections: "Recipes", "Modules" and "Addins".
3. If the URL for the discovered addin is not pointing to the GitHub repo, attempts to figure out the repo URL by searching for the `Project Site` link on package's nuget page. **PLEASE NOTE**: some packages omit this information unfortunately which means that these addins cannot be properly analyzed.
4. Searches the GitHub repo for a .SLN file. **PLEASE NOTE**: if more than one solution file is discovered, we pick one at random. **HINT**: Keep only one solution in your repo in order to allow us to predictably analyze your addin.
5. Parse the solution file and discover the project. **PLEASE NOTE**: we ignore project named `*.Tests.csproj` because we assume these are unit testing projects. **HINT**: if your solution references unit testing projects that don't follow this naming convention, our analysis will yield unexpected results.
6. Parse the `csproj` to discover that reference to `Cake.Core` and `Cake.Common`
7. Parse the `csproj` to discover the framework(s) targeted by your addin
8. Analyze the information discovered and determine if the plugins meet the agred upon "best practice".
9. Output this information in an Excel spreadsheet and/or a markdown file.

## Best practices

The best practice this tool inspects for are:

1. Your plugin references a given version of the Cake DLLs.
2. The references to the Cake DLLs are private
3. Your plugin does not target multiple .NET frameworks and only targets `netstandard2.0`

## Command Line arguments

You can invoke this tool with the following arguments:

```csharp
-c            (Default: false) Clear the list of addins that was previously cached.
-i            (Default: false) Create issue in Github repositories that do not meet recommendations.
-p            Github password.
-t            Folder where temporary files (including reports) are saved.
-u            Github username.
-v            Required. The recommended Cake version. e.g.: 0.26.0
--excel       (Default: false) Generate the Excel report.
--markdown    (Default: false) Generate the Excel report.
```

## Important note

This tool caches certain information such as the list of discovered addins, the content of `.sln` and `.csproj` file for performance reasons and also to ensure invoking this tool repeatedly does not cause you to exceed the number of GitHub API calls calls you are allowed to make in an hour. Make sure you add `-c` when invoking this tool when you want the previously cached information to be deleted and re-discover the list of plugins and re-download their solution and project files.

## Todo

I intend to add a feature to this tool which will create an issue in the addin repo to encourage the author to fix their addin.
