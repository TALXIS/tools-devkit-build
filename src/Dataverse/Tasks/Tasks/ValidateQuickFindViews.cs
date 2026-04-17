using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class ValidateQuickFindViews : Task
{
    [Required]
    public ITaskItem[] FilesForValidation { get; set; }

    public override bool Execute()
    {
        try
        {
            if (FilesForValidation == null || FilesForValidation.Length == 0)
                return true;

            int quickFindCount = 0;
            int failedCount = 0;

            foreach (var fileItem in FilesForValidation)
            {
                var filePath = fileItem.ItemSpec;
                if (!File.Exists(filePath))
                    continue;

                try
                {
                    var doc = XDocument.Load(filePath, LoadOptions.SetLineInfo);
                    var savedQueries = doc.Descendants("savedquery");

                    foreach (var sq in savedQueries)
                    {
                        if (!IsQuickFind(sq))
                            continue;

                        quickFindCount++;
                        if (!ValidateSingleQuickFind(sq, filePath))
                            failedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"ValidateQuickFindViews: could not parse {filePath}: {ex.Message}");
                }
            }

            if (failedCount > 0)
            {
                Log.LogWarning($"ValidateQuickFindViews: {failedCount} of {quickFindCount} Quick Find view(s) are missing search attributes.");
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    private static bool IsQuickFind(XElement savedQuery)
    {
        var el = savedQuery.Element("isquickfindquery");
        return el != null && el.Value.Trim() == "1";
    }

    private bool ValidateSingleQuickFind(XElement savedQuery, string filePath)
    {
        // Get the display name for a readable error message.
        var nameEl = savedQuery.Descendants("LocalizedName").FirstOrDefault();
        var displayName = nameEl?.Attribute("description")?.Value ?? "(unnamed)";

        // Navigate: <fetchxml> → <fetch> → <entity> → <filter isquickfindfields="1">
        var fetchXmlEl = savedQuery.Element("fetchxml");
        if (fetchXmlEl == null)
        {
            ReportWarning(savedQuery, filePath, displayName, "has no <fetchxml> element");
            return false;
        }

        var fetchEl = fetchXmlEl.Element("fetch");
        if (fetchEl == null)
        {
            ReportWarning(savedQuery, filePath, displayName, "has no <fetch> element inside <fetchxml>");
            return false;
        }

        var entityEl = fetchEl.Element("entity");
        if (entityEl == null)
        {
            ReportWarning(savedQuery, filePath, displayName, "has no <entity> element inside <fetch>");
            return false;
        }

        // Look for <filter isquickfindfields="1"> anywhere under <entity> (can be nested)
        var quickFindFilter = entityEl
            .Descendants("filter")
            .FirstOrDefault(f => f.Attribute("isquickfindfields")?.Value == "1");

        if (quickFindFilter == null)
        {
            ReportWarning(savedQuery, filePath, displayName,
                "is marked as Quick Find (isquickfindquery=1) but FetchXML has no <filter isquickfindfields=\"1\">. " +
                "Users will not be able to search by any attribute.");
            return false;
        }

        // The filter exists — check it has at least one <condition>
        var conditions = quickFindFilter.Elements("condition").ToList();
        if (conditions.Count == 0)
        {
            ReportWarning(savedQuery, filePath, displayName,
                "has <filter isquickfindfields=\"1\"> but it contains no <condition> elements. " +
                "Add at least one condition to define searchable attributes.");
            return false;
        }

        return true;
    }

    private void ReportWarning(XElement element, string filePath, string displayName, string detail)
    {
        var lineInfo = (IXmlLineInfo)element;
        int line = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;
        int col = lineInfo.HasLineInfo() ? lineInfo.LinePosition : 0;

        Log.LogWarning(
            subcategory: "quickfind",
            warningCode: "TALXISQF001",
            helpKeyword: null,
            file: filePath,
            lineNumber: line,
            columnNumber: col,
            endLineNumber: 0,
            endColumnNumber: 0,
            message: $"Quick Find view \"{displayName}\" {detail}");
    }
}
