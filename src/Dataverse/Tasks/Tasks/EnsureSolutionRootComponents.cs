using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class EnsureSolutionRootComponents : Task
{
    [Required]
    public ITaskItem SolutionXml { get; set; }

    [Required]
    public ITaskItem[] WebResources { get; set; }

    public string RootComponentType { get; set; } = "61";

    public string Behavior { get; set; } = "0";

    public override bool Execute()
    {
        try
        {
            var solutionPath = SolutionXml?.ItemSpec;
            if (string.IsNullOrWhiteSpace(solutionPath) || !File.Exists(solutionPath))
            {
                Log.LogError($"Solution.xml not found: {solutionPath}");
                return false;
            }

            var webResourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in WebResources ?? Array.Empty<ITaskItem>())
            {
                var name = item.GetMetadata("WebResourceName");
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = Path.GetFileName(item.ItemSpec);
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    webResourceNames.Add(name);
                }
            }

            if (webResourceNames.Count == 0)
            {
                Log.LogMessage(MessageImportance.Low, "No web resources to add to Solution.xml.");
                return true;
            }

            var document = XDocument.Load(solutionPath);
            var solutionManifest = document.Root?.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "SolutionManifest");
            if (solutionManifest == null)
            {
                Log.LogError($"SolutionManifest element not found in {solutionPath}");
                return false;
            }

            var ns = solutionManifest.Name.Namespace;
            var rootComponents = solutionManifest.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "RootComponents");
            if (rootComponents == null)
            {
                rootComponents = new XElement(ns + "RootComponents");
                var missingDependencies = solutionManifest.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "MissingDependencies");
                if (missingDependencies != null)
                {
                    missingDependencies.AddBeforeSelf(rootComponents);
                }
                else
                {
                    solutionManifest.Add(rootComponents);
                }
            }

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in rootComponents.Elements().Where(e => e.Name.LocalName == "RootComponent"))
            {
                var typeValue = element.Attribute("type")?.Value;
                var schemaName = element.Attribute("schemaName")?.Value;
                if (string.Equals(typeValue, RootComponentType, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(schemaName))
                {
                    existing.Add(schemaName);
                }
            }

            var changed = false;
            foreach (var name in webResourceNames)
            {
                if (existing.Contains(name))
                {
                    continue;
                }

                rootComponents.Add(new XElement(ns + "RootComponent",
                    new XAttribute("type", RootComponentType),
                    new XAttribute("schemaName", name),
                    new XAttribute("behavior", Behavior)));
                changed = true;
            }

            if (!changed)
            {
                Log.LogMessage(MessageImportance.Low, "Solution.xml already contains all web resource root components.");
                return true;
            }

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                NewLineChars = Environment.NewLine,
                NewLineHandling = NewLineHandling.Replace
            };

            using (var writer = XmlWriter.Create(solutionPath, settings))
            {
                document.Save(writer);
            }

            Log.LogMessage(MessageImportance.High, $"Updated Solution.xml root components: {solutionPath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
    }
}
