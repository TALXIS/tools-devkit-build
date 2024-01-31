using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Schema;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class ValidateXmlFiles : Task
{
    [Required]
    public ITaskItem[] FilesForValidation { get; set; }

    [Required]
    public ITaskItem[] SchemaFiles { get; set; }


    public override bool Execute()
    {
        try
        {
            XmlSchemaSet schemas = new XmlSchemaSet();

            foreach (var xsdFilePath in SchemaFiles.Select(x => x.ItemSpec))
            {
                schemas.Add(null, xsdFilePath);
            }

            XmlReaderSettings settings = new XmlReaderSettings
            {
                ValidationType = ValidationType.Schema,
                Schemas = schemas,
                DtdProcessing = DtdProcessing.Ignore
            };

            settings.ValidationEventHandler += (object sender, ValidationEventArgs e) =>
            {
                switch (e.Severity)
                {
                    case XmlSeverityType.Error:
                        throw e.Exception;
                    case XmlSeverityType.Warning:
                        Log.LogWarning($"Schema validation warning: {e.Message}");
                        break;
                }
            };


            foreach (var fileForValidation in FilesForValidation)
            {
                bool isValid = ValidateXmlAgainstXsd(settings, fileForValidation.ItemSpec);

                if (!isValid)
                {
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    private bool ValidateXmlAgainstXsd(XmlReaderSettings readerSettings, string xmlFilePath)
    {
        bool isValid = true;
        XmlReader reader = XmlReader.Create(xmlFilePath, readerSettings);
        try
        {
            while (reader.Read()) { /* Empty loop to ensure all content is read and validated */ }
        }
        catch (Exception e)
        {
            Log.LogError($"File {xmlFilePath} is not valid against the XSD schema. Error detail: {e.Message}");
            isValid = false;
        }
        finally
        {
            reader.Dispose();
        }
        return isValid;
    }
}
