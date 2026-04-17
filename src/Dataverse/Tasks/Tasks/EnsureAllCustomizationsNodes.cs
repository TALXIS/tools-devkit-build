using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class EnsureAllCustomizationsNodes : Task
{
    [Required]
    public string CustomizationsXmlFile { get; set; }

    [Required]
    public string MetadataWorkingDirectory { get; set; }

    private static readonly (string FolderOrFile, string NodeName)[] ComponentMap = new[]
    {
        // Folder-based components
        ("Entities",                          "Entities"),
        ("Roles",                             "Roles"),
        ("Workflows",                         "Workflows"),
        ("WebResources",                      "WebResources"),
        ("PluginAssemblies",                  "SolutionPluginAssemblies"),
        ("SdkMessageProcessingSteps",         "SdkMessageProcessingSteps"),
        ("OptionSets",                        "optionsets"),
        ("AppModules",                        "AppModules"),
        ("AppModuleSiteMaps",                 "AppModuleSiteMaps"),
        ("Dialogs",                           "Dialogs"),
        ("Dashboards",                        "Dashboards"),
        ("CanvasApps",                        "CanvasApps"),
        ("Controls",                          "CustomControls"),

        // Subfolder-based components (under Other/)
        ("Other\\Relationships",              "EntityRelationships"),

        // File-based components (single XML files)
        ("Other\\EntityMaps.xml",             "EntityMaps"),
        ("Other\\FieldSecurityProfiles.xml",  "FieldSecurityProfiles"),
    };

    public override bool Execute()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(CustomizationsXmlFile) || !File.Exists(CustomizationsXmlFile))
            {
                Log.LogMessage(MessageImportance.Low,
                    $"EnsureAllCustomizationsNodes: Customizations.xml not found at {CustomizationsXmlFile}, skipping.");
                return true;
            }

            if (string.IsNullOrWhiteSpace(MetadataWorkingDirectory) || !Directory.Exists(MetadataWorkingDirectory))
            {
                Log.LogMessage(MessageImportance.Low,
                    $"EnsureAllCustomizationsNodes: metadata directory not found at {MetadataWorkingDirectory}, skipping.");
                return true;
            }

            var doc = XDocument.Load(CustomizationsXmlFile);
            var root = doc.Root;
            if (root == null)
            {
                Log.LogError($"EnsureAllCustomizationsNodes: Customizations.xml has no root element: {CustomizationsXmlFile}");
                return false;
            }

            var addedNodes = new List<string>();

            foreach (var (folderOrFile, nodeName) in ComponentMap)
            {
                if (!ComponentExists(folderOrFile))
                    continue;

                var elementName = root.Name.Namespace + nodeName;
                if (root.Elements(elementName).Any())
                    continue;

                root.Add(new XElement(elementName));
                addedNodes.Add(nodeName);
            }

            if (addedNodes.Count == 0)
            {
                Log.LogMessage(MessageImportance.Low,
                    "EnsureAllCustomizationsNodes: all required nodes already present.");
                return true;
            }

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                NewLineChars = Environment.NewLine,
                NewLineHandling = NewLineHandling.Replace
            };

            using (var writer = XmlWriter.Create(CustomizationsXmlFile, settings))
            {
                doc.Save(writer);
            }

            Log.LogMessage(MessageImportance.High,
                $"EnsureAllCustomizationsNodes: added missing nodes to Customizations.xml: {string.Join(", ", addedNodes)}");

            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
    }

    private bool ComponentExists(string relativePath)
    {
        var fullPath = Path.Combine(MetadataWorkingDirectory, relativePath);

        if (relativePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return File.Exists(fullPath);

        if (!Directory.Exists(fullPath))
            return false;

        try
        {
            return Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }
}
