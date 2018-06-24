# Cake.AddinDiscoverer
Tool to aid with discovering information about Cake Addins

## Steps
This console application audits Cake addins discovered on Nuget.org and generates a report to indicate if they follow recommended guidelines. 
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
  -t, --tempfolder        Folder where temporary files (including reports) are saved.
  -a, --addinname         Name of the specific addin to be audited. If omitted, all addins are audited.
  -u, --user              Github username.
  -p, --password          Github password.
  -c, --clearcache        (Default: false) Clear the list of addins that was previously cached.
  -i, --issue             (Default: false) Create issue in Github repositories that do not meet recommendations.
  -e, --exceltofile       (Default: false) Generate the Excel report and write to a file.
  -x, --exceltorepo       (Default: false) Generate the Excel report and commit to cake-contrib repo.
  -m, --markdowntofile    (Default: false) Generate the Markdown report and write to a file.
  -r, --markdowntorepo    (Default: false) Generate the Markdown report and commit to cake-contrib repo.
  -s, --syncyaml          (Default: false) Synchronize the yaml files on Cake's web site with the packages discovered on Nuget.
  --help                  Display this help screen.
  --version               Display version information.
```

## Important note

This tool caches certain information such as the list of discovered addins, the content of `.sln` and `.csproj` file for performance reasons and also to ensure invoking this tool repeatedly does not cause you to exceed the number of GitHub API calls you are allowed to make in an hour. Make sure you add `-c` when invoking this tool when you want the previously cached information to be deleted and re-discover the list of plugins and re-download their solution and project files.

If you specify `-i` to create a new issue in the addin Github repo, this tool will attempt to detect if an opened issue already exist to avoid creating duplicate issues.
