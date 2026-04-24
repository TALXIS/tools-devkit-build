using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

public class ValidateJsonFiles : Task
{
    [Required]
    public ITaskItem[] FilesForValidation { get; set; }

    [Required]
    public ITaskItem[] SchemaFiles { get; set; }

    public override bool Execute()
    {
        try
        {
            if (SchemaFiles == null || SchemaFiles.Length == 0)
            {
                Log.LogError("ValidateJsonFiles: no JSON schema files were provided.");
                return false;
            }

           var schemas = new List<KeyValuePair<string, JSchema>>();
            foreach (var schemaFilePath in SchemaFiles.Select(x => x.ItemSpec))
            {
                if (!File.Exists(schemaFilePath))
                {
                    Log.LogError($"ValidateJsonFiles: schema file not found: {schemaFilePath}");
                    return false;
                }
                try
                {
                    var parsed = JSchema.Parse(File.ReadAllText(schemaFilePath));
                    schemas.Add(new KeyValuePair<string, JSchema>(schemaFilePath, parsed));
                }
                catch (Exception ex)
                {
                    Log.LogError($"ValidateJsonFiles: failed to parse schema {schemaFilePath}: {ex.Message}");
                    return false;
                }
            }

            int total  = FilesForValidation?.Length ?? 0;
            int failed = 0;

            for (int i = 0; i < total; i++)
            {
                var filePath = FilesForValidation[i].ItemSpec;
                if (!ValidateSingleFile(filePath, schemas))
                {
                    failed++;
                }
            }

            if (failed > 0)
            {
                Log.LogError($"ValidateJsonFiles: {failed} of {total} JSON file(s) failed schema validation.");
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

    private bool ValidateSingleFile(string jsonFilePath, List<KeyValuePair<string, JSchema>> schemas)
    {
        if (!File.Exists(jsonFilePath))
        {
            Log.LogError($"ValidateJsonFiles: file not found: {jsonFilePath}");
            return false;
        }

        JToken jsonToken;
        try
        {
            // Parse as generic token so we accept both objects and arrays at the root.
            jsonToken = JToken.Parse(File.ReadAllText(jsonFilePath));
        }
        catch (Exception ex)
        {
            Log.LogError(
                subcategory: "json",
                errorCode:   "TALXISJSON001",
                helpKeyword: null,
                file:        jsonFilePath,
                lineNumber:  0,
                columnNumber: 0,
                endLineNumber: 0,
                endColumnNumber: 0,
                message:     $"invalid JSON - {ex.Message}");
            return false;
        }

      var perSchemaMessages = new List<KeyValuePair<string, IList<string>>>();

        foreach (var pair in schemas)
        {
            IList<string> messages;
            if (jsonToken.IsValid(pair.Value, out messages))
            {
                return true;
            }
            perSchemaMessages.Add(new KeyValuePair<string, IList<string>>(pair.Key, messages));
        }

        foreach (var entry in perSchemaMessages)
        {
            foreach (var msg in entry.Value)
            {
                Log.LogError(
                    subcategory: "schema",
                    errorCode:   "TALXISJSONSCHEMA001",
                    helpKeyword: null,
                    file:        jsonFilePath,
                    lineNumber:  0,
                    columnNumber: 0,
                    endLineNumber: 0,
                    endColumnNumber: 0,
                    message:     $"[{Path.GetFileName(entry.Key)}] {msg}");
            }
        }
        return false;
    }
}
