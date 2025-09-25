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
using System.Reflection;

public class ApplyVersionNumber : Task
{
    [Required]
    public string Version { get; set; }
    [Required]
    public ITaskItem SolutionXml { get; set; }
    [Required]
    public string WorkingDirectoryPath { get; set; }
    public ITaskItem PluginAssembliesFolder { get; set; }
    public ITaskItem SdkMessageProcessingStepsFolder { get; set; }
    public ITaskItem WorkflowsFolder { get; set; }
    public ITaskItem ControlsFolder { get; set; }

    private readonly IList<Assembly> _assemblies = new List<Assembly>();

    public override bool Execute()
    {
        UpdateVersionInSolutionXmlFile(SolutionXml.ItemSpec, Version);
        if (PluginAssembliesFolder != null && Directory.Exists(PluginAssembliesFolder.ItemSpec))
        {
            var pluginAssemblies = Directory.EnumerateFiles(PluginAssembliesFolder.ItemSpec, "*.dll.data.xml", SearchOption.AllDirectories);
            foreach (var pluginAssemblyXmlPath in pluginAssemblies)
            {
                var pluginAssemblyDocument = XDocument.Load(pluginAssemblyXmlPath);
                var fullNameAttributeValue = pluginAssemblyDocument.Root.Attribute("FullName")?.Value;
                var assemblyName = fullNameAttributeValue?.Split(',')[0].Trim();
                var assembly = Assembly.LoadFrom(pluginAssemblyXmlPath.Replace(".data.xml", ""));
                _assemblies.Add(assembly);

                Log.LogMessage(MessageImportance.High, $" > Discovered {assembly.FullName} at {pluginAssemblyXmlPath}");
            }
        }
        if (WorkflowsFolder != null && Directory.Exists(WorkflowsFolder.ItemSpec))
        {
            var workflows = Directory.EnumerateFiles(WorkflowsFolder.ItemSpec, "*.xml", SearchOption.AllDirectories);
            foreach (var workflowXmlPath in workflows)
            {
                Log.LogMessage(MessageImportance.High, $"Processing {workflowXmlPath}");
                UpdateVersionInWorkflowFiles(workflowXmlPath);
            }
        }
        if(SdkMessageProcessingStepsFolder != null && Directory.Exists(SdkMessageProcessingStepsFolder.ItemSpec))
        {
            var sdkMessageProcessingSteps = Directory.EnumerateFiles(SdkMessageProcessingStepsFolder.ItemSpec, "*.xml", SearchOption.AllDirectories);
            foreach (var sdkMessageProcessingStepXmlPath in sdkMessageProcessingSteps)
            {
                Log.LogMessage(MessageImportance.High, $"Processing {sdkMessageProcessingStepXmlPath}");
                UpdateVersionInSdkMessageProcessingStepFiles(sdkMessageProcessingStepXmlPath);
            }
        }

        return true;
    }

    private void UpdateVersionInSolutionXmlFile(string path, string newVersion)
    {
        var solutionXmlDocument = XDocument.Load(path);
        var solutionManifest = solutionXmlDocument.Root.Element("SolutionManifest");
        var versionElement = solutionManifest.Element("Version");

        if (versionElement.Value != newVersion)
        {
            versionElement.Value = newVersion;
            File.WriteAllText(path, solutionXmlDocument.ToString());
            Log.LogMessage(MessageImportance.High, $" > {path}");
        }
    }

    private void UpdateVersionInWorkflowFiles(string workflowXmlPath)
    {
        var workflowDocument = XDocument.Load(workflowXmlPath);
        var xamlFileName = workflowDocument.Root.Elements().Where(n => n.Name.LocalName == "XamlFileName").FirstOrDefault()?.Value;
        var workflowXamlPath = WorkingDirectoryPath + xamlFileName;
        Log.LogMessage(MessageImportance.High, $" > Processing workflow XAML file {workflowXamlPath}");
        var workflowXaml = XDocument.Load(workflowXamlPath);
        var elements = workflowXaml.Descendants().Where(n => n.Name.LocalName == "ActivityReference").Attributes("AssemblyQualifiedName");
        string pattern = @"Version=[\d.]*,";
        bool changesApplied = false;
        foreach (var attr in elements)
        {
            var currentVersion = ExtractVersionFromFQDN(attr.Value);
            var assemblyName = attr.Value.Split(',')[1]?.Trim();
            var assembly = _assemblies.Where(x => x.GetName().Name == assemblyName).FirstOrDefault();
            Log.LogMessage(MessageImportance.High, $" > Updating Workflow Activity Reference to {assemblyName} from version {currentVersion}, assembly in project {assembly != null}");
            if (assembly != null)
            {
                var newVersion = assembly.GetName().Version.ToString();
                if(currentVersion == newVersion)
                {
                    continue;
                }
                string replacement = $"Version={newVersion},";
                attr.Value = Regex.Replace(attr.Value, pattern, replacement);
                Log.LogMessage(MessageImportance.High, $" > Workflow Activity Reference to {assemblyName}, old version: {currentVersion}, new: {newVersion}");
                changesApplied = true;
            }
        }
        if (changesApplied) File.WriteAllText(workflowXmlPath, workflowDocument.ToString());
    }

    private void UpdateVersionInSdkMessageProcessingStepFiles(string sdkMessageProcessingStepXmlPath)
    {
        var sdkMessageProcessingStepDocument = XDocument.Load(sdkMessageProcessingStepXmlPath);
        var pluginTypeNameElement = sdkMessageProcessingStepDocument.Root.Element("PluginTypeName");
        var assemblyName = pluginTypeNameElement?.Value?.Split(',')[1].Trim();
        var currentVersion = ExtractVersionFromFQDN(pluginTypeNameElement?.Value);

        Log.LogMessage(MessageImportance.High, $" > Updating SdkMessageProcessingStep references to {assemblyName} from version {currentVersion}");

        if (pluginTypeNameElement?.Value != null)
        {
            var assembly = _assemblies.Where(x => x.GetName().Name == assemblyName).FirstOrDefault();
            if (assembly != null)
            {
                var newVersion = assembly.GetName().Version.ToString();
                Log.LogMessage(MessageImportance.High, $"Version found: {currentVersion}, updating to {newVersion}");
                if (currentVersion == newVersion)
                {
                    return;
                }
                string pattern = @"Version=[\d.]*,";
                string replacement = $"Version={newVersion},";
                pluginTypeNameElement.SetValue(Regex.Replace(pluginTypeNameElement.Value, pattern, replacement));
                Log.LogMessage(MessageImportance.High, $" > SdkMessageProcessingStep for {assemblyName}, old version: {currentVersion}, new: {newVersion}");
                File.WriteAllText(sdkMessageProcessingStepXmlPath, sdkMessageProcessingStepDocument.ToString());
            }
        }
    }

    private string ExtractVersionFromFQDN(string fullName)
    {
        var match = Regex.Match(fullName, @"Version=([\d.]*),");
        return match.Success ? match.Groups[1].Value : null;
    }
}
