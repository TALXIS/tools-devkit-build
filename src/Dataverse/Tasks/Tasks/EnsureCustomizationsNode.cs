using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class EnsureCustomizationsNode : Task
{
    [Required]
    public string CustomizationsXmlFile { get; set; }

    [Required]
    public string NodeName { get; set; }

    public override bool Execute()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(CustomizationsXmlFile) || !File.Exists(CustomizationsXmlFile))
            {
                Log.LogError($"Customizations.xml not found: {CustomizationsXmlFile}");

                return false;
            }

            var nodeName = (NodeName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(nodeName))
            {
                Log.LogError("NodeName is empty.");

                return false;
            }

            try
            {
                XmlConvert.VerifyNCName(nodeName);
            }
            catch (Exception ex)
            {
                Log.LogError($"NodeName is not a valid XML name: {nodeName}. {ex.Message}");

                return false;
            }

            var document = XDocument.Load(CustomizationsXmlFile);
            var root = document.Root;

            if (root == null)
            {
                Log.LogError($"Customizations.xml has no document element: {CustomizationsXmlFile}");

                return false;
            }

            var elementName = root.Name.Namespace + nodeName;

            if (root.Elements(elementName).Any())
            {
                Log.LogMessage(MessageImportance.Low, $"Customizations.xml already contains node '{nodeName}'.");

                return true;
            }

            root.Add(new XElement(elementName));

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                NewLineChars = Environment.NewLine,
                NewLineHandling = NewLineHandling.Replace
            };

            using (var writer = XmlWriter.Create(CustomizationsXmlFile, settings))
            {
                document.Save(writer);
            }

            Log.LogMessage(MessageImportance.High, $"Added node '{nodeName}' to Customizations.xml: {CustomizationsXmlFile}");

            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            
            return false;
        }
    }
}
