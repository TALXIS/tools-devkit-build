using DotMake.CommandLine;

[CliCommand(Description = "Analyze a dotnet project", Parent = typeof(RootCliCommand))]
public class ProjectCommand
{
    [CliOption(Description = "Path of the project file (*.[cs]proj)")]
    public required string ProjectFilePath { get; set; }

    public int Run(CliContext context)
    {
        var analyzeTask = new AnalyzeProject();
        analyzeTask.ProjectPath = "/Users/tomasprokop/Desktop/Repos/tools-devkit-build/src/Tasks.MSBuild/TALXIS.DevKit.Build.Dataverse.Tasks.csproj";
        analyzeTask.Execute();
        return 0;
    }
}