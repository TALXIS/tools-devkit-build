using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Xml.Linq;
using System.Linq;
using System.Xml;
using System.Collections.Generic;

public class ApplyPcfVersionNumber : Task
{
    [Required]
    public string Version { get; set; }
    [Required]
    public ITaskItem PcfOutputPath { get; set; }

    public override bool Execute()
    {
        if (PcfOutputPath != null && Directory.Exists(PcfOutputPath.ItemSpec))
        {
            var customControls = Directory.EnumerateFiles(PcfOutputPath.ItemSpec, "ControlManifest.xml", SearchOption.AllDirectories);

            var versionNumbers = Version.Split('.');
            var pcfVersion = $"0.0.{versionNumbers[0]}{versionNumbers[1]}{versionNumbers[2]}{versionNumbers[3]}";
            Log.LogMessage(MessageImportance.High, $" > Using {pcfVersion} for PCF version number in manifest");

            foreach (var manifest in customControls)
            {
                Log.LogMessage(MessageImportance.High, $"Processing {manifest}");
                UpdateVersionInControlManifestXmlFile(manifest, pcfVersion);
            }
        }
        return true;
    }

    private void UpdateVersionInControlManifestXmlFile(string path, string newVersion)
    {
        var solutionXmlDocument = XDocument.Load(path);
        var solutionManifest = solutionXmlDocument.Root.Element("control");
        var currentVersion = solutionManifest.Attribute("version");

        if (currentVersion.Value != newVersion)
        {
            currentVersion.Value = newVersion;
            File.WriteAllText(path, solutionXmlDocument.ToString());
            Log.LogMessage(MessageImportance.High, $" > {path}");
        }
    }
}
