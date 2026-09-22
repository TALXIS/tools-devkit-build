using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json.Linq;

/// <summary>
/// MSBuild task that generates the Connector.xml descriptor for a Power Platform custom
/// connector solution component. The connectorid is derived deterministically from the
/// connector's schema name (RFC 4122 version-3 name-based UUID) instead of being generated
/// fresh on every build, so repeated builds keep updating the same Dataverse Connector record
/// rather than creating a new one each time.
/// </summary>
public sealed class GenerateConnectorXml : Task
{
    // Fixed namespace GUID for TALXIS DevKit-generated connector ids. Arbitrary but constant -
    // changing this value would change the id generated for every existing connector.
    private static readonly Guid ConnectorIdNamespace = new Guid("8f14e45f-ceea-467e-bd36-6d0aa2b5a08c");

    [Required]
    public string OutputPath { get; set; } = "";

    /// <summary>Dataverse schema name, e.g. "talxis_connectorsopenfoodfacts". Also the name-based GUID seed.</summary>
    [Required]
    public string SchemaName { get; set; } = "";

    /// <summary>
    /// Optional explicit connectorid (GUID) for migrating an existing connector, so imports keep
    /// updating the same Dataverse record. Empty means the deterministic name-based id.
    /// </summary>
    public string ConnectorId { get; set; } = "";

    /// <summary>Fallback display name, used only when the swagger's own "info.title" is absent/unreadable.</summary>
    [Required]
    public string DisplayName { get; set; } = "";

    /// <summary>Fallback description, used only when the swagger's own "info.description" is absent/unreadable.</summary>
    public string Description { get; set; } = "";

    /// <summary>File name (not path) of the staged OpenAPI definition, referenced as "/Connector/&lt;name&gt;".</summary>
    [Required]
    public string OpenApiDefinitionFileName { get; set; } = "";

    [Required]
    public string ConnectionParametersFileName { get; set; } = "";

    /// <summary>Full path to write the extracted, bare "connectionParameters" JSON to.</summary>
    [Required]
    public string ConnectionParametersOutputPath { get; set; } = "";

    [Required]
    public string PolicyTemplateInstancesFileName { get; set; } = "";

    /// <summary>Full path to write the extracted, bare "policyTemplateInstances" JSON to.</summary>
    [Required]
    public string PolicyTemplateInstancesOutputPath { get; set; } = "";

    public string CustomCodeFileName { get; set; } = "";

    public string IconFileName { get; set; } = "";

    /// <summary>
    /// Path to apiProperties.json. This is a paconn/pac-CLI-shaped file
    /// ({"properties":{"connectionParameters":...,"iconBrandColor":...,"policyTemplateInstances":...}})
    /// - the live Dataverse Connector entity's own "connectionparameters"/"policytemplateinstances"
    /// attributes each expect just the bare inner value, not this wrapper, confirmed against
    /// INT0010-CustomConnectors' real, working production connectors (e.g. every one of their
    /// *_connectionparameters.json files is a bare "{...}"/"{}", never wrapped in "properties").
    /// Copying apiProperties.json verbatim into the connectionparameters attribute - what this task
    /// used to do - produces a shape ApiHubs' backend rejects outright on live import with
    /// "ApiHubsRequestFailed: Nullable object must have a value", confirmed via direct Web API
    /// isolation testing (TALXIS/tools-devkit-build#119).
    /// </summary>
    [Required]
    public string ApiPropertiesPath { get; set; } = "";

    /// <summary>Optional path to the source apiDefinition.swagger.json, read for "info.title"/"info.description".</summary>
    public string ApiDefinitionPath { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SchemaName))
            {
                Log.LogError("SchemaName is required.");
                return false;
            }

            Guid connectorId;
            if (string.IsNullOrWhiteSpace(ConnectorId))
            {
                connectorId = CreateDeterministicGuid(ConnectorIdNamespace, SchemaName.Trim());
            }
            else if (!Guid.TryParse(ConnectorId.Trim(), out connectorId))
            {
                Log.LogError($"ConnectorId '{ConnectorId}' is not a valid GUID.");
                return false;
            }

            var apiProperties = ReadApiProperties();
            if (Log.HasLoggedErrors) return false;

            // Every solution-exported connector carries an iconbrandcolor; default to Power Platform blue.
            var iconBrandColor = (string)apiProperties?["iconBrandColor"] ?? "#007ee5";

            var (swaggerTitle, swaggerDescription) = ReadSwaggerInfo();
            if (Log.HasLoggedErrors) return false;

            var displayName = !string.IsNullOrWhiteSpace(swaggerTitle) ? swaggerTitle : DisplayName;
            var description = !string.IsNullOrWhiteSpace(swaggerDescription) ? swaggerDescription : Description;

            WriteExtractedJson(ConnectionParametersOutputPath, apiProperties?["connectionParameters"], new JObject());
            WriteExtractedJson(PolicyTemplateInstancesOutputPath, apiProperties?["policyTemplateInstances"], new JArray());

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = new UTF8Encoding(false),
                OmitXmlDeclaration = false,
            };

            using (var stream = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(stream, settings))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("Connector");
                    writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");

                    writer.WriteElementString("connectorid", connectorId.ToString());
                    writer.WriteElementString("description", description ?? "");
                    writer.WriteElementString("displayname", displayName);
                    writer.WriteElementString("iconbrandcolor", iconBrandColor);
                    writer.WriteElementString("name", SchemaName.Trim());
                    writer.WriteElementString("connectortype", "1");
                    writer.WriteElementString("openapidefinition", "/Connector/" + OpenApiDefinitionFileName);
                    writer.WriteElementString("connectionparameters", "/Connector/" + ConnectionParametersFileName);
                    writer.WriteElementString("policytemplateinstances", "/Connector/" + PolicyTemplateInstancesFileName);

                    if (!string.IsNullOrWhiteSpace(CustomCodeFileName))
                        writer.WriteElementString("customcodeblobcontent", "/Connector/" + CustomCodeFileName);

                    if (!string.IsNullOrWhiteSpace(IconFileName))
                        writer.WriteElementString("iconblob", "/Connector/" + IconFileName);

                    writer.WriteEndElement(); // Connector
                    writer.WriteEndDocument();
                }

                var dir = Path.GetDirectoryName(OutputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllBytes(OutputPath, stream.ToArray());
            }

            Log.LogMessage(MessageImportance.High, $"Generated Connector.xml: {OutputPath} (id={connectorId})");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    /// <summary>
    /// Reads apiProperties.json's inner "properties" object (the paconn/pac-CLI wrapper's payload).
    /// Malformed JSON is a build error: this file feeds the import-critical connection parameters.
    /// </summary>
    private JObject ReadApiProperties()
    {
        if (string.IsNullOrWhiteSpace(ApiPropertiesPath) || !File.Exists(ApiPropertiesPath))
            return null;

        try
        {
            var root = JObject.Parse(File.ReadAllText(ApiPropertiesPath));
            return root["properties"] as JObject;
        }
        catch (Exception ex)
        {
            Log.LogError($"apiProperties.json at {ApiPropertiesPath} is not valid JSON: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Writes just the extracted inner value (not apiProperties.json's outer "properties" wrapper)
    /// as its own JSON file - the shape the live Dataverse Connector entity's own
    /// connectionparameters/policytemplateinstances attributes each expect.
    /// </summary>
    private void WriteExtractedJson(string outputPath, JToken value, JToken fallback)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(outputPath, (value ?? fallback).ToString(Newtonsoft.Json.Formatting.Indented));
    }

    /// <summary>
    /// Reads info.title/info.description and fails the build on the fields ApiHubs rejects at
    /// import time with an opaque "Invalid Api definition object" error (swagger 2.0, title, host).
    /// </summary>
    private (string Title, string Description) ReadSwaggerInfo()
    {
        if (string.IsNullOrWhiteSpace(ApiDefinitionPath) || !File.Exists(ApiDefinitionPath))
            return (null, null);

        JObject swagger;
        try
        {
            swagger = JObject.Parse(File.ReadAllText(ApiDefinitionPath));
        }
        catch (Exception ex)
        {
            Log.LogError($"OpenAPI definition at {ApiDefinitionPath} is not valid JSON: {ex.Message}");
            return (null, null);
        }

        var info = swagger["info"];
        var title = (string)info?["title"];

        if ((string)swagger["swagger"] != "2.0")
            Log.LogError($"OpenAPI definition at {ApiDefinitionPath} must declare \"swagger\": \"2.0\" - Power Platform custom connectors only accept Swagger 2.0.");
        if (string.IsNullOrWhiteSpace(title))
            Log.LogError($"OpenAPI definition at {ApiDefinitionPath} is missing the required info.title - Dataverse import rejects a connector without it.");
        if (string.IsNullOrWhiteSpace((string)swagger["host"]))
            Log.LogError($"OpenAPI definition at {ApiDefinitionPath} is missing the required top-level \"host\" - the connector's service URL is derived from it.");

        return (title, (string)info?["description"]);
    }

    /// <summary>
    /// RFC 4122 version-3 (MD5, name-based) UUID, so the same (namespace, name) pair always
    /// produces the same GUID.
    /// </summary>
    private static Guid CreateDeterministicGuid(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var data = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, data, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, data, namespaceBytes.Length, nameBytes.Length);

        byte[] hash;
        using (var md5 = MD5.Create())
        {
            hash = md5.ComputeHash(data);
        }

        var newGuid = new byte[16];
        Array.Copy(hash, 0, newGuid, 0, 16);

        newGuid[6] = (byte)((newGuid[6] & 0x0F) | (3 << 4)); // version 3
        newGuid[8] = (byte)((newGuid[8] & 0x3F) | 0x80);     // RFC 4122 variant

        SwapByteOrder(newGuid);
        return new Guid(newGuid);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        SwapBytes(guid, 0, 3);
        SwapBytes(guid, 1, 2);
        SwapBytes(guid, 4, 5);
        SwapBytes(guid, 6, 7);
    }

    private static void SwapBytes(byte[] guid, int left, int right)
    {
        var temp = guid[left];
        guid[left] = guid[right];
        guid[right] = temp;
    }
}
