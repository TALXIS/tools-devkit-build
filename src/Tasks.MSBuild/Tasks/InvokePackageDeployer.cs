using System;
using System.Diagnostics;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
/* using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Xrm.Tooling.Connector;
using Microsoft.Xrm.Tooling.PackageDeployment.CrmPackageCore.Models;
using Microsoft.Xrm.Tooling.PackageDeployment.CrmPackageCore.ImportCode; */

public class InvokePackageDeployer : Task
{
    [Required]
    public string PackagePath { get; set; }

    public override bool Execute()
    {
        try
        {
            // TODO: Package Deployer WIP
            /*
            Log.LogMessage(MessageImportance.High, "Initializing package deployment");

            CoreObjects Data = new CoreObjects(sourcePackageAssemblyPath: PackagePath, allowPackageCodeExecution: true);

            CrmServiceClient crmServiceClient = new CrmServiceClient(useUniqueInstance: true);
            if (!crmServiceClient.IsReady)
            {
                Log.LogError("Service client is not in ready state");
                return false;
            }

            Log.LogMessage(MessageImportance.High, "Setup authentication");
            Data.CrmSvc = crmServiceClient;
            Log.LogMessage(MessageImportance.High, "Import configuration");

            PackageImportConfigurationParser packageImportConfigurationParser = new PackageImportConfigurationParser(Data);
            try
            {
                packageImportConfigurationParser.ReadConfig();
            }
            catch (Exception ex)
            {
                Log.LogError("Error in configuration parsing: " + ex.Message);
                return false;
            }

            Log.LogMessage(MessageImportance.High, "Starting import");
            BaseImportCustomizations import = new BaseImportCustomizations(Data);
            import.BeginSolutionImport();
            */
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }
}