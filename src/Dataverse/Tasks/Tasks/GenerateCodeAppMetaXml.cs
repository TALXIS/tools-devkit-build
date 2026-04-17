using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json.Linq;

/// <summary>
/// MSBuild task that generates a .meta.xml file for a Power Apps Code App
/// based on power.config.json and the build output (dist/) directory.
/// </summary>
public sealed class GenerateCodeAppMetaXml : Task
{
    /// <summary>
    /// Path to power.config.json.
    /// </summary>
    [Required]
    public string ConfigPath { get; set; } = "";

    /// <summary>
    /// Schema name of the app (e.g. "pub_myapp").
    /// Used for the Name element and CodeAppPackageUri paths.
    /// </summary>
    [Required]
    public string AppSchemaName { get; set; } = "";

    /// <summary>
    /// Path where the .meta.xml will be written.
    /// </summary>
    [Required]
    public string OutputPath { get; set; } = "";

    private static readonly Dictionary<string, string> MimeTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html",  "text/html" },
            { ".htm",   "text/html" },
            { ".js",    "application/javascript" },
            { ".mjs",   "application/javascript" },
            { ".css",   "text/css" },
            { ".svg",   "image/svg+xml" },
            { ".png",   "image/png" },
            { ".jpg",   "image/jpeg" },
            { ".jpeg",  "image/jpeg" },
            { ".gif",   "image/gif" },
            { ".webp",  "image/webp" },
            { ".ico",   "image/x-icon" },
            { ".json",  "application/json" },
            { ".woff",  "font/woff" },
            { ".woff2", "font/woff2" },
            { ".ttf",   "font/ttf" },
            { ".eot",   "application/vnd.ms-fontobject" },
            { ".map",   "application/json" },
            { ".txt",   "text/plain" },
            { ".xml",   "application/xml" },
            { ".wasm",  "application/wasm" },
        };

    public override bool Execute()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ConfigPath) || !File.Exists(ConfigPath))
            {
                Log.LogError($"power.config.json not found: {ConfigPath}");
                return false;
            }

            if (string.IsNullOrWhiteSpace(AppSchemaName))
            {
                Log.LogError("AppSchemaName is required.");
                return false;
            }

            var xml = Generate();

            var dir = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(OutputPath, xml, new UTF8Encoding(false));
            Log.LogMessage(MessageImportance.High, $"Generated CodeApp meta.xml: {OutputPath}");

            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    private string Generate()
    {
        // ── Load config ──
        var configFullPath = Path.GetFullPath(ConfigPath);
        var configDir = Path.GetDirectoryName(configFullPath);
        var config = JObject.Parse(File.ReadAllText(configFullPath));

        var buildPathRaw = (string)config["buildPath"] ?? "./dist";
        var buildPath = Path.IsPathRooted(buildPathRaw)
            ? buildPathRaw
            : Path.GetFullPath(Path.Combine(configDir, buildPathRaw));

        if (!Directory.Exists(buildPath))
            throw new DirectoryNotFoundException(
                $"Build path not found: {buildPath}. Ensure npm run build produces output.");

        var displayName = (string)config["appDisplayName"] ?? AppSchemaName;
        var description = (string)config["description"];
        var hasDescription = !string.IsNullOrWhiteSpace(description);

        // ── DatabaseReferences JSON ──
        var dbRefsOut = new JObject();
        var cdsDeps = new JArray();

        var databaseReferences = config["databaseReferences"] as JObject;
        if (databaseReferences != null)
        {
            foreach (var prop in databaseReferences.Properties())
            {
                var key = prop.Name;
                var dbRef = (JObject)prop.Value;
                var envVarName = (string)dbRef["environmentVariableName"] ?? "";

                var dataSources = new JObject();
                var dsNode = dbRef["dataSources"] as JObject;
                if (dsNode != null)
                {
                    foreach (var dsProp in dsNode.Properties())
                    {
                        var ds = (JObject)dsProp.Value;
                        var entitySetName = (string)ds["entitySetName"] ?? "";
                        var logicalName = (string)ds["logicalName"] ?? "";

                        dataSources[dsProp.Name] = new JObject
                        {
                            ["entitySetName"] = entitySetName,
                            ["logicalName"] = logicalName
                        };

                        cdsDeps.Add(new JObject
                        {
                            ["logicalname"] = logicalName,
                            ["componenttype"] = 1
                        });
                    }
                }

                dbRefsOut[key] = new JObject
                {
                    ["databaseDetails"] = new JObject
                    {
                        ["referenceType"] = "Environmental",
                        ["environmentName"] = key,
                        ["overrideValues"] = new JObject
                        {
                            ["status"] = "NotSpecified",
                            ["environmentVariableName"] = envVarName
                        }
                    },
                    ["dataSources"] = dataSources
                };
            }
        }

        var dbRefsJson = dbRefsOut.ToString(Newtonsoft.Json.Formatting.None);
        var cdsDepsJson = new JObject { ["cdsdependencies"] = cdsDeps }
            .ToString(Newtonsoft.Json.Formatting.None);

        // ── ConnectionReferences ──
        var connRefs = config["connectionReferences"];
        var connRefsJson = connRefs != null && connRefs.Type != JTokenType.Null
            ? connRefs.ToString(Newtonsoft.Json.Formatting.None)
            : "{}";
        if (string.IsNullOrWhiteSpace(connRefsJson) || connRefsJson == "null")
            connRefsJson = "{}";

        // ── Scan build output → CodeAppPackageUris ──
        var packageUriBase = $"/CanvasApps/{AppSchemaName}_CodeAppPackages";
        var allFiles = Directory.GetFiles(buildPath, "*", SearchOption.AllDirectories);
        var codeAppPackageUris = new List<string>();

        foreach (var file in allFiles)
        {
            var relativePath = MakeRelativePath(buildPath, file).Replace('\\', '/');
            var mime = GetMimeType(file);
            codeAppPackageUris.Add($"{packageUriBase}/{relativePath}_ContentType_{mime}");
        }

        // ── Build XML ──
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        const string xsiNs = "http://www.w3.org/2001/XMLSchema-instance";

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
                writer.WriteStartElement("CanvasApp");
                writer.WriteAttributeString("xmlns", "xsi", null, xsiNs);

                writer.WriteElementString("Name", AppSchemaName);
                writer.WriteElementString("AppVersion", timestamp);
                writer.WriteElementString("Status", "Ready");
                writer.WriteElementString("CreatedByClientVersion", "0.0.0.0");
                writer.WriteElementString("MinClientVersion", "0.0.0.0");
                writer.WriteElementString("Tags", "{}");
                writer.WriteElementString("IsCdsUpgraded", "0");

                WriteNilElement(writer, "GalleryItemId", xsiNs);

                writer.WriteElementString("BackgroundColor", "RGBA(255,255,255,1)");
                writer.WriteElementString("DisplayName", displayName);

                if (hasDescription)
                    writer.WriteElementString("Description", description);
                else
                    WriteNilElement(writer, "Description", xsiNs);

                WriteNilElement(writer, "CommitMessage", xsiNs);
                WriteNilElement(writer, "Publisher", xsiNs);

                writer.WriteElementString("AuthorizationReferences", "[]");
                writer.WriteElementString("ConnectionReferences", connRefsJson);
                writer.WriteElementString("DatabaseReferences", dbRefsJson);
                writer.WriteElementString("AppComponents", "[]");
                writer.WriteElementString("AppComponentDependencies", "[]");
                writer.WriteElementString("CanConsumeAppPass", "1");
                writer.WriteElementString("CanvasAppType", "4");
                writer.WriteElementString("BypassConsent", "0");
                writer.WriteElementString("AdminControlBypassConsent", "0");

                WriteNilElement(writer, "EmbeddedApp", xsiNs);

                writer.WriteElementString("IntroducedVersion", "1.0");
                writer.WriteElementString("CdsDependencies", cdsDepsJson);
                writer.WriteElementString("IsCustomizable", "1");

                // CodeAppPackageUris
                writer.WriteStartElement("CodeAppPackageUris");
                foreach (var uri in codeAppPackageUris)
                {
                    writer.WriteElementString("CodeAppPackageUri", uri);
                }
                writer.WriteEndElement();

                writer.WriteEndElement(); // CanvasApp
                writer.WriteEndDocument();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static void WriteNilElement(XmlWriter writer, string name, string xsiNs)
    {
        writer.WriteStartElement(name);
        writer.WriteAttributeString("xsi", "nil", xsiNs, "true");
        writer.WriteFullEndElement();
    }

    private static string GetMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return MimeTypes.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
    }

    /// <summary>
    /// Compatible replacement for Path.GetRelativePath (not available in net472).
    /// </summary>
    private static string MakeRelativePath(string basePath, string fullPath)
    {
        if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            basePath += Path.DirectorySeparatorChar;

        var baseUri = new Uri(basePath);
        var fullUri = new Uri(fullPath);
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString())
                   .Replace('/', Path.DirectorySeparatorChar);
    }
}
