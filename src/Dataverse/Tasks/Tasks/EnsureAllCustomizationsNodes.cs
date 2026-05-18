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
        (Path.Combine("Other", "Relationships"),             "EntityRelationships"),

        // File-based components (single XML files)
        (Path.Combine("Other", "EntityMaps.xml"),            "EntityMaps"),
        (Path.Combine("Other", "FieldSecurityProfiles.xml"), "FieldSecurityProfiles"),
    };

    public override bool Execute()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(MetadataWorkingDirectory) || !Directory.Exists(MetadataWorkingDirectory))
            {
                Log.LogMessage(MessageImportance.Low,
                    $"EnsureAllCustomizationsNodes: metadata directory not found at {MetadataWorkingDirectory}, skipping.");
                return true;
            }

            if (string.IsNullOrWhiteSpace(CustomizationsXmlFile))
            {
                Log.LogError("EnsureAllCustomizationsNodes: CustomizationsXmlFile is empty.");
                return false;
            }

            if (!File.Exists(CustomizationsXmlFile))
            {
                CreateCustomizationsSkeleton(CustomizationsXmlFile);
                Log.LogMessage(MessageImportance.High,
                    $"EnsureAllCustomizationsNodes: Customizations.xml was missing; created skeleton at {CustomizationsXmlFile}.");
            }

            var doc = XDocument.Load(CustomizationsXmlFile);
            var root = doc.Root;
            if (root == null)
            {
                Log.LogError($"EnsureAllCustomizationsNodes: Customizations.xml has no root element: {CustomizationsXmlFile}");
                return false;
            }

            var changed = false;

            // 1. Component nodes based on what's actually present in the metadata
            //    working directory.
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
                changed = true;
            }

            if (addedNodes.Count > 0)
            {
                Log.LogMessage(MessageImportance.High,
                    $"EnsureAllCustomizationsNodes: added missing component nodes: {string.Join(", ", addedNodes)}");
            }
            else
            {
                Log.LogMessage(MessageImportance.Low,
                    "EnsureAllCustomizationsNodes: all required component nodes already present.");
            }

            // 2. Languages — discover from Other/Solution.xml in the same metadata
            //    working directory and ensure each language code appears under
            //    <Languages><Language>...</Language></Languages>.
            var solutionLanguages = ExtractLanguageCodesFromSolution();
            if (solutionLanguages.Count > 0)
            {
                var addedLanguages = EnsureLanguageCodes(root, solutionLanguages);
                if (addedLanguages.Count > 0)
                {
                    Log.LogMessage(MessageImportance.High,
                        $"EnsureAllCustomizationsNodes: added missing languages: {string.Join(", ", addedLanguages)}");
                    changed = true;
                }
                else
                {
                    Log.LogMessage(MessageImportance.Low,
                        "EnsureAllCustomizationsNodes: all solution languages already declared.");
                }
            }

            if (!changed)
            {
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

    /// <summary>
    /// Writes a minimal Customizations.xml skeleton to <paramref name="path"/>.
    /// Matches the shape produced by the solution packager: root
    /// <c>ImportExportXml</c> with the standard xsi namespace declaration.
    /// </summary>
    private static void CreateCustomizationsSkeleton(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var skeleton = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("ImportExportXml",
                new XAttribute(XNamespace.Xmlns + "xsi", xsi.NamespaceName)));

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            NewLineChars = Environment.NewLine,
            NewLineHandling = NewLineHandling.Replace
        };

        using (var writer = XmlWriter.Create(path, settings))
        {
            skeleton.Save(writer);
        }
    }

    /// <summary>
    /// Reads <c>Other/Solution.xml</c> from the metadata working directory and
    /// collects every distinct language code referenced by the solution:
    ///   - <c>ImportExportXml/@languagecode</c> (the solution's base language)
    ///   - <c>//LocalizedName/@languagecode</c>
    ///   - <c>//Description/@languagecode</c>
    /// Returns an empty set if Solution.xml is absent or has no language info.
    /// </summary>
    private HashSet<string> ExtractLanguageCodesFromSolution()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var solutionPath = Path.Combine(MetadataWorkingDirectory, "Other", "Solution.xml");
        if (!File.Exists(solutionPath))
            return codes;

        XDocument solutionDoc;
        try
        {
            solutionDoc = XDocument.Load(solutionPath);
        }
        catch (Exception ex)
        {
            Log.LogWarning($"EnsureAllCustomizationsNodes: could not parse Solution.xml at {solutionPath}: {ex.Message}");
            return codes;
        }

        var solutionRoot = solutionDoc.Root;
        if (solutionRoot == null)
            return codes;

        // Base language on the root element.
        var rootLang = (string)solutionRoot.Attribute("languagecode");
        if (!string.IsNullOrWhiteSpace(rootLang))
            codes.Add(rootLang.Trim());

        // Localized names and descriptions anywhere under the document.
        foreach (var attr in solutionRoot
                     .Descendants()
                     .Where(e => e.Name.LocalName == "LocalizedName" || e.Name.LocalName == "Description")
                     .Attributes("languagecode"))
        {
            if (!string.IsNullOrWhiteSpace(attr.Value))
                codes.Add(attr.Value.Trim());
        }

        return codes;
    }

    /// <summary>
    /// Ensures <c>root/Languages/Language</c> contains every code from
    /// <paramref name="requiredCodes"/>. Creates the parent <c>Languages</c>
    /// element if it doesn't exist yet. Returns the codes that were actually
    /// added (empty if everything was already present).
    /// </summary>
    private static List<string> EnsureLanguageCodes(XElement root, HashSet<string> requiredCodes)
    {
        var added = new List<string>();
        var ns = root.Name.Namespace;
        var languagesName = ns + "Languages";
        var languageName = ns + "Language";

        var languagesEl = root.Element(languagesName);
        if (languagesEl == null)
        {
            languagesEl = new XElement(languagesName);
            root.Add(languagesEl);
        }

        var existing = new HashSet<string>(
            languagesEl.Elements(languageName)
                       .Select(e => (e.Value ?? string.Empty).Trim())
                       .Where(v => v.Length > 0),
            StringComparer.Ordinal);

        foreach (var code in requiredCodes)
        {
            if (existing.Contains(code))
                continue;
            languagesEl.Add(new XElement(languageName, code));
            existing.Add(code);
            added.Add(code);
        }

        return added;
    }
}
