using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Build.Construction;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

public class AnalyzeProject : Task
{
    [Required]
    public string ProjectPath { get; set; }

    public override bool Execute()
    {
#if DEBUG
        var debugEnvVariable = Environment.GetEnvironmentVariable("DEBUG_DEVKIT");
        if (!string.IsNullOrEmpty(debugEnvVariable) && debugEnvVariable.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
#if IS_CORECLR
                    Console.WriteLine("Waiting for debugger to attach.");
                    Console.WriteLine($"Process ID: {Process.GetCurrentProcess().Id}");

                    while (!Debugger.IsAttached)
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                    Debugger.Break();
#else
            Debugger.Launch();
#endif
        }
#endif
        try
        {
            var projectRootElement = ProjectRootElement.Open(ProjectPath);

            var projectReferences = projectRootElement.Items.Where(x => x.ItemType == "ProjectReference");
            var packageReferences = projectRootElement.Items.Where(x => x.ItemType == "PackageReference");

         

            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }
}
