using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public sealed class GenPageValidateBundle : Task
{
    private static readonly Regex DefaultExportRegex = new(@"\bexport\s+default\b", RegexOptions.Compiled);
    private static readonly Regex NamedDefaultExportRegex = new(@"\bexport\s*\{\s*[^}]*\bas\s+default\b[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex FromImportRegex = new(@"\b(?:import|export)\s+(?:[^'"";]+?\s+from\s+)?['""](?<module>[^'""]+)['""]", RegexOptions.Compiled);
    private static readonly Regex DynamicImportRegex = new(@"\bimport\s*\(\s*['""](?<module>[^'""]+)['""]\s*\)", RegexOptions.Compiled);

    [Required]
    public ITaskItem[] Bundles { get; set; } = Array.Empty<ITaskItem>();

    public string AllowedBareImports { get; set; } = "react;react-dom;react-dom/client;react/jsx-runtime;@fluentui/react-components;@fluentui/react-icons";

    public override bool Execute()
    {
        var allowed = AllowedBareImports
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Bundles)
        {
            var path = item.GetMetadata("CompiledJsPath");
            if (string.IsNullOrWhiteSpace(path))
                path = item.ItemSpec;

            var pageName = item.GetMetadata("PageName");
            if (!File.Exists(path))
            {
                Log.LogError($"GenPage bundle not found for '{pageName}': {path}");
                continue;
            }

            var content = StripComments(File.ReadAllText(path));
            if (!HasDefaultExport(content))
                Log.LogError($"GenPage bundle '{path}' must contain an ESM default export.");

            foreach (var module in FindModules(content))
            {
                if (IsBareImport(module) && !IsAllowed(module, allowed))
                    Log.LogError($"GenPage bundle '{path}' contains unsupported bare import '{module}'. Bundle dependencies or add a supported external.");
            }
        }

        return !Log.HasLoggedErrors;
    }

    private static bool HasDefaultExport(string content)
    {
        return DefaultExportRegex.IsMatch(content) || NamedDefaultExportRegex.IsMatch(content);
    }

    private static IEnumerable<string> FindModules(string content)
    {
        foreach (Match match in FromImportRegex.Matches(content))
            yield return match.Groups["module"].Value;
        foreach (Match match in DynamicImportRegex.Matches(content))
            yield return match.Groups["module"].Value;
    }

    private static bool IsBareImport(string module)
    {
        return !module.StartsWith(".", StringComparison.Ordinal)
            && !module.StartsWith("/", StringComparison.Ordinal)
            && !module.Contains("://", StringComparison.Ordinal);
    }

    private static bool IsAllowed(string module, HashSet<string> allowed)
    {
        return allowed.Contains(module) || allowed.Any(a => module.StartsWith(a + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static string StripComments(string content)
    {
        var result = new StringBuilder(content.Length);
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inTemplate = false;
        var inLineComment = false;
        var inBlockComment = false;
        var escaped = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            var next = i + 1 < content.Length ? content[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\r' || c == '\n')
                {
                    inLineComment = false;
                    result.Append(c);
                }
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                else if (c == '\r' || c == '\n')
                {
                    result.Append(c);
                }
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && !inTemplate && c == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && !inTemplate && c == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            result.Append(c);

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if ((inSingleQuote || inDoubleQuote || inTemplate) && c == '\\')
            {
                escaped = true;
                continue;
            }

            if (!inDoubleQuote && !inTemplate && c == '\'')
                inSingleQuote = !inSingleQuote;
            else if (!inSingleQuote && !inTemplate && c == '"')
                inDoubleQuote = !inDoubleQuote;
            else if (!inSingleQuote && !inDoubleQuote && c == '`')
                inTemplate = !inTemplate;
        }

        return result.ToString();
    }
}
