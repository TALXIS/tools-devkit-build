using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class ValidateDuplicateGuids : Task
{
    [Required]
    public ITaskItem[] FilesForValidation { get; set; }

    private static readonly Regex GuidPattern = new Regex(
        @"\{?[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}\}?",
        RegexOptions.Compiled);

    private static readonly (string ElementName, string FilePattern)[] IdentityRules = new[]
    {
        ("savedqueryid",                    "SavedQueries"),
        ("savedqueryvisualizationid",       "SavedQueries"),
        ("formid",                          "FormXml"),
        ("WebResourceId",                   ".data.xml"),
        ("WorkflowId",                      "Workflows"),
        ("SdkMessageProcessingStepId",      "SdkMessageProcessingSteps"),
        ("PluginTypeId",                    "PluginAssemblies"),
        ("connectionroleid",                "ConnectionRoles"),
        ("OptionSetId",                     "OptionSets"),
        ("environmentvariabledefinitionid", "environmentvariabledefinitions"),
        ("environmentvariablevalueid",      "environmentvariabledefinitions"),
        ("AppModuleId",                     "AppModules"),
        ("RoleId",                          "Roles"),
    };

    private static readonly Dictionary<string, List<string>> IdentityLookup;

    static ValidateDuplicateGuids()
    {
        IdentityLookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (elementName, filePattern) in IdentityRules)
        {
            if (!IdentityLookup.TryGetValue(elementName, out var patterns))
            {
                patterns = new List<string>();
                IdentityLookup[elementName] = patterns;
            }
            if (filePattern != null)
                patterns.Add(filePattern);
        }
    }

    private struct GuidLocation
    {
        public string FilePath;
        public string ElementName;
        public int Line;
        public int Column;
    }

    public override bool Execute()
    {
        try
        {
            if (FilesForValidation == null || FilesForValidation.Length == 0)
                return true;

            var guidMap = new Dictionary<string, List<GuidLocation>>(StringComparer.OrdinalIgnoreCase);

            foreach (var fileItem in FilesForValidation)
            {
                var filePath = fileItem.ItemSpec;
                if (!File.Exists(filePath))
                    continue;

                try
                {
                    ScanFile(filePath, guidMap);
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"ValidateDuplicateGuids: could not parse {filePath}: {ex.Message}");
                }
            }

            int duplicateCount = 0;
            foreach (var entry in guidMap)
            {
                var locations = entry.Value;
                if (locations.Count < 2)
                    continue;

                duplicateCount++;
                var guid = entry.Key;

                for (int i = 0; i < locations.Count; i++)
                {
                    var loc = locations[i];
                    var otherFiles = locations
                        .Where((_, idx) => idx != i)
                        .Select(l => Path.GetFileName(l.FilePath))
                        .ToArray();

                    Log.LogError(
                        subcategory: "guid",
                        errorCode: "TALXISGUID001",
                        helpKeyword: null,
                        file: loc.FilePath,
                        lineNumber: loc.Line,
                        columnNumber: loc.Column,
                        endLineNumber: 0,
                        endColumnNumber: 0,
                        message: $"Duplicate GUID {{{guid}}} in <{loc.ElementName}>. Also found in: {string.Join(", ", otherFiles)}");
                }
            }

            if (duplicateCount > 0)
            {
                Log.LogError($"ValidateDuplicateGuids: found {duplicateCount} duplicate GUID(s) across {FilesForValidation.Length} files.");
            }

            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    private void ScanFile(string filePath, Dictionary<string, List<GuidLocation>> guidMap)
    {
        var doc = XDocument.Load(filePath, LoadOptions.SetLineInfo);
        ScanElements(doc.Root, filePath, guidMap);
    }

    private void ScanElements(XElement element, string filePath, Dictionary<string, List<GuidLocation>> guidMap)
    {
        if (!element.HasElements)
        {
            var localName = element.Name.LocalName;

            if (!IsInsideParameters(element) && IsIdentityElement(localName, filePath))
            {
                var value = element.Value.Trim();
                if (GuidPattern.IsMatch(value))
                {
                    var normalized = NormalizeGuid(value);
                    if (normalized != null)
                    {
                        var lineInfo = (IXmlLineInfo)element;
                        AddGuid(guidMap, normalized, new GuidLocation
                        {
                            FilePath = filePath,
                            ElementName = localName,
                            Line = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0,
                            Column = lineInfo.HasLineInfo() ? lineInfo.LinePosition : 0
                        });
                    }
                }
            }
        }

        foreach (var child in element.Elements())
        {
            ScanElements(child, filePath, guidMap);
        }
    }

    private static bool IsInsideParameters(XElement element)
    {
        var parent = element.Parent;
        while (parent != null)
        {
            if (parent.Name.LocalName.Equals("parameters", StringComparison.OrdinalIgnoreCase))
                return true;
            parent = parent.Parent;
        }
        return false;
    }

    private static bool IsIdentityElement(string elementName, string filePath)
    {
        if (!IdentityLookup.TryGetValue(elementName, out var patterns))
            return false;

        if (patterns.Count == 0)
            return true;

        foreach (var pattern in patterns)
        {
            if (filePath.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static string NormalizeGuid(string raw)
    {
        var stripped = raw.Trim().TrimStart('{').TrimEnd('}').ToLowerInvariant();
        if (Guid.TryParse(stripped, out var parsed))
            return parsed.ToString("D");
        return null;
    }

    private static void AddGuid(Dictionary<string, List<GuidLocation>> map, string guid, GuidLocation location)
    {
        if (!map.TryGetValue(guid, out var list))
        {
            list = new List<GuidLocation>();
            map[guid] = list;
        }
        list.Add(location);
    }
}
