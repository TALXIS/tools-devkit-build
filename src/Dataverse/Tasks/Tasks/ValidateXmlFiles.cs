using System;
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

    // Tracks the file currently being validated so the ValidationEventHandler
    // can emit MSBuild-canonical error messages (file(line,col): error CODE: ...).
    private string _currentFile;
    private bool _currentFileHasError;

    public override bool Execute()
    {
        try
        {
            if (SchemaFiles == null || SchemaFiles.Length == 0)
            {
                Log.LogError("ValidateXmlFiles: no XSD schema files were provided.");
                return false;
            }

            var schemas = new XmlSchemaSet();
            foreach (var xsdFilePath in SchemaFiles.Select(x => x.ItemSpec))
            {
                if (!File.Exists(xsdFilePath))
                {
                    Log.LogError($"ValidateXmlFiles: schema file not found: {xsdFilePath}");
                    return false;
                }
                schemas.Add(null, xsdFilePath);
            }

            try
            {
                schemas.Compile();
            }
            catch (XmlSchemaException ex)
            {
                Log.LogError($"ValidateXmlFiles: failed to compile schema set: {ex.Message}");
                return false;
            }

            var settings = new XmlReaderSettings
            {
                ValidationType  = ValidationType.Schema,
                ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings
                                | XmlSchemaValidationFlags.ProcessIdentityConstraints
                                | XmlSchemaValidationFlags.ProcessInlineSchema
                                | XmlSchemaValidationFlags.ProcessSchemaLocation,
                Schemas         = schemas,
                DtdProcessing   = DtdProcessing.Ignore
            };
            settings.ValidationEventHandler += OnValidationEvent;

            int total   = FilesForValidation?.Length ?? 0;
            int failed  = 0;

            for (int i = 0; i < total; i++)
            {
                _currentFile = FilesForValidation[i].ItemSpec;
                _currentFileHasError = false;
                ValidateSingleFile(_currentFile, settings);
                if (_currentFileHasError) failed++;
            }

            if (failed > 0)
            {
                Log.LogError($"ValidateXmlFiles: {failed} of {total} XML file(s) failed schema validation.");
                return false;
            }

            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    private void OnValidationEvent(object sender, ValidationEventArgs e)
    {
        int line = e.Exception?.LineNumber   ?? 0;
        int col  = e.Exception?.LinePosition ?? 0;

        if (e.Severity == XmlSeverityType.Error)
        {
            // MSBuild canonical format: IDEs (VS, Rider, VS Code) pick up file+line+col.
            Log.LogError(
                subcategory: "schema",
                errorCode:   "TALXISXSD001",
                helpKeyword: null,
                file:        _currentFile,
                lineNumber:  line,
                columnNumber: col,
                endLineNumber: 0,
                endColumnNumber: 0,
                message:     e.Message);
            _currentFileHasError = true;
        }
        else
        {
            Log.LogWarning(
                subcategory: "schema",
                warningCode: "TALXISXSD001",
                helpKeyword: null,
                file:        _currentFile,
                lineNumber:  line,
                columnNumber: col,
                endLineNumber: 0,
                endColumnNumber: 0,
                message:     e.Message);
        }
    }

    private void ValidateSingleFile(string xmlFilePath, XmlReaderSettings settings)
    {
        if (!File.Exists(xmlFilePath))
        {
            Log.LogError($"ValidateXmlFiles: file not found: {xmlFilePath}");
            _currentFileHasError = true;
            return;
        }

        try
        {
            using (var reader = XmlReader.Create(xmlFilePath, settings))
            {
                while (reader.Read()) { /* drives validation */ }
            }
        }
        catch (XmlException ex)
        {
            // Malformed XML (not a schema violation).
            Log.LogError(
                subcategory: "xml",
                errorCode:   "TALXISXML001",
                helpKeyword: null,
                file:        xmlFilePath,
                lineNumber:  ex.LineNumber,
                columnNumber: ex.LinePosition,
                endLineNumber: 0,
                endColumnNumber: 0,
                message:     ex.Message);
            _currentFileHasError = true;
        }
        catch (Exception ex)
        {
            Log.LogError($"{xmlFilePath}: {ex.Message}");
            _currentFileHasError = true;
        }
    }
}
