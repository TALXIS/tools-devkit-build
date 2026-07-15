using System;
using System.Globalization;
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
    public string LastCommitDateTime { get; set; }
    [Required]
    public ITaskItem PcfOutputPath { get; set; }

    public override bool Execute()
    {
        if (PcfOutputPath == null || !Directory.Exists(PcfOutputPath.ItemSpec))
        {
            Log.LogError($"PCF output directory not found at '{PcfOutputPath?.ItemSpec}'. Expected ControlManifest.xml to already exist by the time version patching runs.");
            return false;
        }

        var customControls = Directory.EnumerateFiles(PcfOutputPath.ItemSpec, "ControlManifest.xml", SearchOption.AllDirectories).ToList();
        if (customControls.Count == 0)
        {
            Log.LogError($"No ControlManifest.xml found under '{PcfOutputPath.ItemSpec}'. Expected the PCF build to have produced one by the time version patching runs.");
            return false;
        }

        if(string.IsNullOrEmpty(LastCommitDateTime))
        {
            Log.LogWarning("LastCommitDateTime is not set, using current date and time.");
            LastCommitDateTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        if (!DateTime.TryParseExact(LastCommitDateTime, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var lastCommitDateTime))
        {
            Log.LogError($"LastCommitDateTime '{LastCommitDateTime}' is not in the expected 'yyyy-MM-ddTHH:mm:ssZ' UTC format.");
            return false;
        }
        Log.LogMessage(MessageImportance.High, $"Last commit date and time: {lastCommitDateTime:yyyy-MM-ddTHH:mm:ssZ}");

        var secondsSince2020 = (long)(lastCommitDateTime - new DateTime(2020, 1, 1)).TotalSeconds;
        Log.LogMessage(MessageImportance.High, $"Seconds since 2020-01-01T00:00:00Z: {secondsSince2020}");

        // var versionNumbers = Version.Split('.');
        var pcfVersion = $"0.0.{secondsSince2020}";
        Log.LogMessage(MessageImportance.High, $" > Using {pcfVersion} for PCF version number in manifest");

        foreach (var manifest in customControls)
        {
            Log.LogMessage(MessageImportance.High, $"Processing {manifest}");
            UpdateVersionInControlManifestXmlFile(manifest, pcfVersion);
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
