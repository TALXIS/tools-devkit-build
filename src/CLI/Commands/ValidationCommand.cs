using DotMake.CommandLine;

[CliCommand(Description = "Validate file schema", Parent = typeof(RootCliCommand))]
public class ValidationCommand
{
    [CliOption(Description = "Path of the file to validate")]
    public string FilePath { get; set; }

    [CliOption(Description = "Path of the schema file")]
    public string SchemaPath { get; set; }

    public int Run(CliContext context)
    {
        return 0;
    }
}