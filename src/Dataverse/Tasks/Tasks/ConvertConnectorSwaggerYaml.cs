using System;
using System.IO;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.OpenApi.YamlReader;
using SharpYaml.Serialization;

/// <summary>
/// Converts a connector's apiDefinition.swagger.yml to the JSON file GetConnectorOutputs and the
/// Dataverse solution staging step both expect, using Microsoft.OpenApi.YamlReader's own
/// YamlConverter - which, unlike a hand-rolled generic YAML-to-JSON conversion, correctly
/// preserves scalar types (booleans and numbers don't come back as strings).
/// </summary>
public class ConvertConnectorSwaggerYaml : Task
{
    [Required]
    public string YamlPath { get; set; } = "";

    [Required]
    public string JsonPath { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            var yamlStream = new YamlStream();
            using (var reader = new StreamReader(YamlPath))
            {
                yamlStream.Load(reader);
            }

            if (yamlStream.Documents.Count == 0)
            {
                Log.LogError($"'{YamlPath}' contains no YAML document.");
                return false;
            }

            var jsonNode = YamlConverter.ToJsonNode(yamlStream.Documents[0]);
            var json = jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            var dir = Path.GetDirectoryName(JsonPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(JsonPath, json);
            return true;
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to convert '{YamlPath}' to JSON: {ex.Message}");
            return false;
        }
    }
}
