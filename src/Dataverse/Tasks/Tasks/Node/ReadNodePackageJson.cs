using System;
using System.IO;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>
/// Reads what the Node build needs from a package.json: whether it defines a "build" script.
/// A project without one is built with the SDK's default build for its project type.
/// </summary>
public sealed class ReadNodePackageJson : Task
{
    [Required]
    public string PackageJsonPath { get; set; } = string.Empty;

    [Output]
    public bool HasBuildScript { get; private set; }

    public override bool Execute()
    {
        if (!File.Exists(PackageJsonPath))
        {
            Log.LogError($"package.json not found at '{PackageJsonPath}'.");
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(PackageJsonPath), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            HasBuildScript = document.RootElement.ValueKind == JsonValueKind.Object &&
                             document.RootElement.TryGetProperty("scripts", out var scripts) &&
                             scripts.ValueKind == JsonValueKind.Object &&
                             scripts.TryGetProperty("build", out var build) &&
                             build.ValueKind == JsonValueKind.String &&
                             !string.IsNullOrWhiteSpace(build.GetString());
            return true;
        }
        catch (JsonException ex)
        {
            Log.LogError($"'{PackageJsonPath}' is not valid JSON: {ex.Message}");
            return false;
        }
    }
}
