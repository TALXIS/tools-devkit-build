using System;
using System.IO;
using System.Xml;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public sealed class AddRootComponentToSolution : Task
{
    [Required]
    public string SolutionPath { get; set; } = "";

    [Required]
    public string Type { get; set; } = "";

    public string Id { get; set; } = "";

    public string SchemaName { get; set; } = "";

    public string Behavior { get; set; } = "0";

    public override bool Execute()
    {
        try
        {
            ValidateInputs();

            var fullPath = Path.GetFullPath(SolutionPath);
            var doc = new XmlDocument();
            doc.Load(fullPath);

            var rootComponents = doc.SelectSingleNode("//RootComponents") as XmlElement;
            if (rootComponents == null)
            {
                if (doc.DocumentElement == null)
                    throw new InvalidOperationException("Solution.xml is missing a document element.");

                rootComponents = doc.CreateElement("RootComponents");
                doc.DocumentElement.AppendChild(rootComponents);
            }

            if (!ExistsAlready(rootComponents))
            {
                var rc = doc.CreateElement("RootComponent");
                rc.SetAttribute("type", Type.Trim());

                if (!string.IsNullOrWhiteSpace(Id))
                    rc.SetAttribute("id", Normalize(Id));

                if (!string.IsNullOrWhiteSpace(SchemaName))
                    rc.SetAttribute("schemaName", SchemaName.Trim());

                if (!string.IsNullOrWhiteSpace(Behavior))
                    rc.SetAttribute("behavior", Behavior.Trim());

                rootComponents.AppendChild(rc);
                Log.LogMessage(MessageImportance.High, $"RootComponent added to {fullPath}");
            }
            else
            {
                Log.LogMessage(MessageImportance.Low, "RootComponent already present, no changes written.");
            }

            doc.Save(fullPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    private void ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(SolutionPath))
            throw new ArgumentException("SolutionPath is required.");

        if (!File.Exists(SolutionPath))
            throw new FileNotFoundException("Solution.xml not found", SolutionPath);

        if (string.IsNullOrWhiteSpace(Type))
            throw new ArgumentException("Type is required.");

        if (string.IsNullOrWhiteSpace(Id) && string.IsNullOrWhiteSpace(SchemaName))
            throw new ArgumentException("Either Id or SchemaName must be provided.");
    }

    private bool ExistsAlready(XmlElement rootComponents)
    {
        foreach (XmlNode node in rootComponents.ChildNodes)
        {
            if (node is not XmlElement el)
                continue;

            if (!string.Equals(el.Name, "RootComponent", StringComparison.Ordinal))
                continue;

            var typeAttr = el.GetAttribute("type");
            if (!string.Equals(typeAttr, Type.Trim(), StringComparison.Ordinal))
                continue;

            var idAttr = el.GetAttribute("id");
            var schemaAttr = el.GetAttribute("schemaName");

            bool idMatches = !string.IsNullOrWhiteSpace(Id) &&
                             string.Equals(Normalize(idAttr), Normalize(Id), StringComparison.OrdinalIgnoreCase);

            bool schemaMatches = !string.IsNullOrWhiteSpace(SchemaName) &&
                                 string.Equals(schemaAttr?.Trim(), SchemaName.Trim(), StringComparison.Ordinal);

            if ((idMatches && !string.IsNullOrWhiteSpace(Id)) ||
                (schemaMatches && !string.IsNullOrWhiteSpace(SchemaName)))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string guidLike)
    {
        if (string.IsNullOrWhiteSpace(guidLike))
            return "";

        return guidLike.Trim().Trim('{', '}');
    }
}
