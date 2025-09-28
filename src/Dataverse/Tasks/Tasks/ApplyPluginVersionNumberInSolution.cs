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

public class ApplyPluginVersionNumberInSolution : Task
{
    [Required]
    public ITaskItem[] SourceFileItems { get; set; }
    [Required]
    public string WorkingDirectoryPath { get; set; }

    public override bool Execute()
    {
        foreach (var item in SourceFileItems)
        {
            var componentType = item.GetMetadata("PowerAppsComponentType");
            var sourceFilePath = item.ItemSpec;
            if(componentType == "Plugin")
            {
                var version = Assembly.LoadFrom(sourceFilePath).GetName().Version.ToString();
                Log.LogMessage(MessageImportance.High, $"Processing {sourceFilePath}, version: {version}");
                var pluginAssemblies = Directory.EnumerateFiles(WorkingDirectoryPath, "*.dll.data.xml", SearchOption.AllDirectories);
                foreach (var pluginAssemblyXmlPath in pluginAssemblies)
                {
                    if(pluginAssemblyXmlPath.IndexOf(Path.GetFileNameWithoutExtension(sourceFilePath), StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    Log.LogMessage(MessageImportance.High, $"Processing {pluginAssemblyXmlPath}");
                    UpdateVersionInPluginAssemblyMetadataFile(pluginAssemblyXmlPath, version);
                }
            }
        }

        return true;
    }

    private void UpdateVersionInPluginAssemblyMetadataFile(string path, string newVersion)
    {
        var pluginAssemblyDocument = XDocument.Load(path);
        var fullNameAttributeValue = pluginAssemblyDocument.Root.Attribute("FullName")?.Value;
        var assemblyName = fullNameAttributeValue?.Split(',')[0].Trim();
        var currentVersion = ExtractVersionFromFQDN(fullNameAttributeValue);

        Log.LogMessage(MessageImportance.High, $" > Updating references to {assemblyName} from version {currentVersion} to {newVersion}");

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

        Log.LogMessage(MessageImportance.High, $" > {elementName} references to {assemblyName} ({elementsToUpdate.Count()})");
        foreach (var element in elementsToUpdate)
        {
            Log.LogMessage(MessageImportance.Low, $"   - {element.Value}");
            if (element.Value.Contains(assemblyName))
            {
                element.Value = Regex.Replace(element.Value, pattern, replacement);
                Log.LogMessage(MessageImportance.High, $"   - Updated to {element.Value}");
            }
        }
    }

    private void ReplaceVersionInWorkflowActivityGroup(XDocument document, string assemblyName, string newVersion)
    {
        var elementsToUpdate = document.Descendants("WorkflowActivityGroupName");
        var pattern = @"\([\d.]*\)";
        var replacement = $"({newVersion})";

        Log.LogMessage(MessageImportance.High, $" > Workflow Activity Group for {assemblyName}");

        foreach (var element in elementsToUpdate)
        {
            if (element.Parent.Attribute("AssemblyQualifiedName").Value.Contains(assemblyName))
            {
                element.Value = Regex.Replace(element.Value, pattern, replacement);
            }
        }
    }

    private string ExtractVersionFromFQDN(string fullName)
    {
        var match = Regex.Match(fullName, @"Version=([\d.]*),");
        return match.Success ? match.Groups[1].Value : null;
    }
}
