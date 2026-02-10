using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class EnsureWebResourceDataXml : Task
{
    [Required]
    public string DataXmlFile { get; set; }

    [Required]
    public string WebResourceName { get; set; }

    [Required]
    public string DisplayName { get; set; }
    public string WebResourceType { get; set; } = "3";
    public string IntroducedVersion { get; set; } = "1.0.0.0";

    public override bool Execute()
    {
        try
        {
            if (File.Exists(DataXmlFile))
            {
                return true;
            }

            var directory = Path.GetDirectoryName(DataXmlFile);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var guid = Guid.NewGuid();
            var guidLower = guid.ToString();
            var guidUpper = guid.ToString().ToUpperInvariant();

            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("WebResource",
                    new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                    new XElement("WebResourceId", $"{{{guidLower}}}"),
                    new XElement("Name", WebResourceName),
                    new XElement("DisplayName", DisplayName),
                    new XElement("WebResourceType", string.IsNullOrWhiteSpace(WebResourceType) ? "3" : WebResourceType),
                    new XElement("IntroducedVersion", string.IsNullOrWhiteSpace(IntroducedVersion) ? "1.0.0.0" : IntroducedVersion),
                    new XElement("IsEnabledForMobileClient", "0"),
                    new XElement("IsAvailableForMobileOffline", "0"),
                    new XElement("IsCustomizable", "1"),
                    new XElement("CanBeDeleted", "1"),
                    new XElement("IsHidden", "0"),
                    new XElement("FileName", $"/WebResources/{WebResourceName}{guidUpper}")
                )
            );

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                NewLineChars = Environment.NewLine,
                NewLineHandling = NewLineHandling.Replace
            };

            using (var writer = XmlWriter.Create(DataXmlFile, settings))
            {
                doc.Save(writer);
            }

            Log.LogMessage(MessageImportance.High, $"Generated webresource data.xml: {DataXmlFile}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
    }
}
