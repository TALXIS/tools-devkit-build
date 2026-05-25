using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class GenerateGenPageProjectXml : Task
{
    [Required]
    public string ProjectId { get; set; }

    [Required]
    public string PageName { get; set; }

    [Required]
    public string OutputDir { get; set; }

    public override bool Execute()
    {
        try
        {
            Directory.CreateDirectory(OutputDir);

            var doc = new XDocument(
                new XElement("uxagentproject",
                    new XAttribute("uxagentprojectid", ProjectId),
                    new XElement("iscustomizable", "1"),
                    new XElement("name", PageName),
                    new XElement("statecode", "0"),
                    new XElement("statuscode", "1")
                )
            );

            var outputPath = Path.Combine(OutputDir, "uxagentproject.xml");

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineChars = Environment.NewLine,
                NewLineHandling = NewLineHandling.Replace
            };

            using (var writer = XmlWriter.Create(outputPath, settings))
            {
                doc.Save(writer);
            }

            Log.LogMessage(MessageImportance.High, $"Generated GenPage project XML: {outputPath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
    }
}
