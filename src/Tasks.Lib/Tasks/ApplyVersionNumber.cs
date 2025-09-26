using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Xml.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class ApplyVersionNumber : Task
{
    [Required]
    public string Version { get; set; }
    [Required]
    public ITaskItem SolutionXml { get; set; }
    public ITaskItem[] PluginAssemblies { get; set; }
    public ITaskItem[] SdkMessageProcessingSteps { get; set; }
    public ITaskItem[] Workflows { get; set; }
    public ITaskItem CustomControlsFolder { get; set; }

    private readonly HashSet<string> assemblyNames = new HashSet<string>();

    public override bool Execute()
    {
        UpdateVersionInSolutionXmlFile(SolutionXml.ItemSpec, Version);
        if (PluginAssemblies != null)
        {
            foreach (var pluginAssemblyXmlPath in PluginAssemblies.Select(x => x.ItemSpec))
            {
                UpdateVersionInPluginAssemblyMetadataFile(pluginAssemblyXmlPath, Version);
            }
            Workflows?.ToList().ForEach(workflowXmlPath => UpdateVersionInWorkflowFiles(workflowXmlPath.ItemSpec, Version));
            SdkMessageProcessingSteps?.ToList().ForEach(sdkMessageProcessingStepXmlPath => UpdateVersionInSdkMessageProcessingStepFiles(sdkMessageProcessingStepXmlPath.ItemSpec, Version));
        }
        
        if (CustomControlsFolder != null)
        {
            var customControls = Directory.EnumerateFiles(CustomControlsFolder.ItemSpec, "ControlManifest.xml", SearchOption.AllDirectories);

            var versionNumbers = Version.Split('.');
            var pcfVersion = $"0.0.{versionNumbers[0]}{versionNumbers[1]}{versionNumbers[2]}{versionNumbers[3]}";
            Log.LogMessage(MessageImportance.High, $" > Using {pcfVersion} for PCF version number in manifest");

            foreach (var manifest in customControls)
            {
                UpdateVersionInControlManifestXmlFile(manifest, pcfVersion);
            }
        }
        return true;
    }

    private void UpdateVersionInSolutionXmlFile(string path, string newVersion)
    {
        var solutionXmlDocument = XDocument.Load(SolutionXml.ItemSpec);
        var solutionManifest = solutionXmlDocument.Root.Element("SolutionManifest");
        var versionElement = solutionManifest.Element("Version");

        if (versionElement.Value != newVersion)
        {
            versionElement.Value = newVersion;
            File.WriteAllText(path, solutionXmlDocument.ToString());
            Log.LogMessage(MessageImportance.High, $" > Solution.xml");
        }
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

    private void UpdateVersionInPluginAssemblyMetadataFile(string path, string newVersion)
    {
        var pluginAssemblyDocument = XDocument.Load(path);
        var fullNameAttributeValue = pluginAssemblyDocument.Root.Attribute("FullName")?.Value;
        var assemblyName = fullNameAttributeValue?.Split(',')[0].Trim();
        assemblyNames.Add(assemblyName);
        var currentVersion = ExtractVersionFromFQDN(fullNameAttributeValue);

        if (currentVersion != newVersion)
        {
            ReplaceVersionInAssemblyNameAttribute(pluginAssemblyDocument, "PluginAssembly", "FullName", assemblyName, newVersion);
            ReplaceVersionInAssemblyNameAttribute(pluginAssemblyDocument, "PluginType", "AssemblyQualifiedName", assemblyName, newVersion);
            ReplaceVersionInWorkflowActivityGroup(pluginAssemblyDocument, assemblyName, newVersion);
            File.WriteAllText(path, pluginAssemblyDocument.ToString());
        }
    }

    private void ReplaceVersionInAssemblyNameAttribute(XDocument document, string elementName, string attributeName, string assemblyName, string newVersion)
    {
        var elementsToUpdate = document.Descendants(elementName).Attributes(attributeName);
        var pattern = @"Version=[\d.]*,";
        var replacement = $"Version={newVersion},";

        Log.LogMessage(MessageImportance.High, $" > {elementName} references to {assemblyName}");
        foreach (var element in elementsToUpdate)
        {
            if (element.Value.Contains(assemblyName))
            {
                element.Value = Regex.Replace(element.Value, pattern, replacement);
            }
        }
    }

    private void ReplaceVersionInWorkflowActivityGroup(XDocument document, string assemblyName, string newVersion)
    {
        var elementsToUpdate = document.Descendants("WorkflowActivityGroupName");
        var pattern = @"\([\d.]*\)";
        var replacement = $"({newVersion})";
        foreach (var element in elementsToUpdate)
        {
            if (element.Parent.Attribute("AssemblyQualifiedName").Value.Contains(assemblyName))
            {
                element.Value = Regex.Replace(element.Value, pattern, replacement);
            }
        }
        Log.LogMessage(MessageImportance.High, $" > Workflow Activity Group for {assemblyName}");
    }

    private void UpdateVersionInWorkflowFiles(string workflowXmlPath, string newVersion)
    {
        var workflowDocument = XDocument.Load(workflowXmlPath);
        var elements = workflowDocument.Descendants().Where(n => n.Name.LocalName == "ActivityReference").Attributes("AssemblyQualifiedName");
        string pattern = @"Version=[\d.]*,";
        string replacement = $"Version={newVersion},";
        bool changesApplied = false;
        foreach (var attr in elements)
        {
            var currentVersion = ExtractVersionFromFQDN(attr.Value);
            var assemblyName = attr.Value.Split(',')[1]?.Trim();
            if (assemblyNames.Contains(assemblyName) && currentVersion != newVersion)
            {
                attr.Value = Regex.Replace(attr.Value, pattern, replacement);
                Log.LogMessage(MessageImportance.High, $" > Workflow Activity Reference to {assemblyName}");
                changesApplied = true;
            }
        }
        if (changesApplied) File.WriteAllText(workflowXmlPath, workflowDocument.ToString());
    }

    private void UpdateVersionInSdkMessageProcessingStepFiles(string sdkMessageProcessingStepXmlPath, string newVersion)
    {
        var sdkMessageProcessingStepDocument = XDocument.Load(sdkMessageProcessingStepXmlPath);
        var pluginTypeNameElement = sdkMessageProcessingStepDocument.Root.Element("PluginTypeName");
        var assemblyName = pluginTypeNameElement?.Value?.Split(',')[1].Trim();
        var currentVersion = ExtractVersionFromFQDN(pluginTypeNameElement?.Value);

        if (pluginTypeNameElement?.Value != null && assemblyNames.Contains(assemblyName) && currentVersion != newVersion)
        {
            string pattern = @"Version=[\d.]*,";
            string replacement = $"Version={newVersion},";
            pluginTypeNameElement.SetValue(Regex.Replace(pluginTypeNameElement.Value, pattern, replacement));
            Log.LogMessage(MessageImportance.High, $" > SdkMessageProcessingStep for {assemblyName}");
            File.WriteAllText(sdkMessageProcessingStepXmlPath, sdkMessageProcessingStepDocument.ToString());
        }
    }

    private string ExtractVersionFromFQDN(string fullName)
    {
        var match = Regex.Match(fullName, @"Version=([\d.]*),");
        return match.Success ? match.Groups[1].Value : null;
    }
}
