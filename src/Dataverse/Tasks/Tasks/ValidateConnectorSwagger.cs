using System;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.OpenApi.Reader;

/// <summary>
/// Parses a connector's apiDefinition.swagger.json with Microsoft.OpenApi - the same library
/// Power Platform's own connector tooling uses to parse and validate OpenAPI definitions - and
/// fails the build on structural errors instead of deferring all validation to deploy time.
/// </summary>
public class ValidateConnectorSwagger : Task
{
    [Required]
    public string ApiDefinitionPath { get; set; } = "";

    public override bool Execute()
    {
        if (!File.Exists(ApiDefinitionPath))
        {
            // CheckConnectorPrereqs (which this target depends on) already errors on a missing
            // file with a clearer, connector-specific message - nothing more to add here.
            return true;
        }

        try
        {
            var json = File.ReadAllText(ApiDefinitionPath);
            var result = OpenApiModelFactory.Parse(json, "json", settings: null);

            foreach (var warning in result.Diagnostic.Warnings)
            {
                Log.LogWarning($"{ApiDefinitionPath}: {warning.Message} ({warning.Pointer})");
            }

            foreach (var error in result.Diagnostic.Errors)
            {
                Log.LogError($"{ApiDefinitionPath}: {error.Message} ({error.Pointer})");
            }

            return result.Diagnostic.Errors.Count == 0;
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to parse OpenAPI definition '{ApiDefinitionPath}': {ex.Message}");
            return false;
        }
    }
}
