using DotMake.CommandLine;

try
{
    Cli.Run<RootCliCommand>(args);
}
catch (Exception e)
{
    Console.WriteLine(@"Exception in main: {0}", e.Message);
}

[CliCommand]
public class RootCliCommand
{
 
    public void Run(CliContext context)
    {
        if (context.IsEmptyCommand())
              context.ShowHelp();
          else
              context.ShowValues();
    }
}