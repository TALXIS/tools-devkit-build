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
            foreach (var fileForValidation in FilesForValidation)
            {
                bool isValid = ValidateJsonAgainstSchema(fileForValidation.ItemSpec, SchemaFiles.Select(x => x.ItemSpec));

                if (!isValid)
                {
                    Log.LogError($"The JSON file {fileForValidation} is not valid against the JSON schema");
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

    private bool ValidateJsonAgainstSchema(string jsonFilePath, IEnumerable<string> schemaFilePaths)
    {
        bool isValid = true;
        JSchema schema = new JSchema();

        foreach (var schemaFilePath in schemaFilePaths)
        {
            var schemaContent = File.ReadAllText(schemaFilePath);
            schema = JSchema.Parse(schemaContent);
        }

        var jsonContent = File.ReadAllText(jsonFilePath);
        var jsonObject = JObject.Parse(jsonContent);

        IList<string> messages;
        if (!jsonObject.IsValid(schema, out messages))
        {
            foreach (var message in messages)
            {
                Log.LogError($"Schema validation error: {message}");
                isValid = false;
            }
        }
        return isValid;
    }
}
