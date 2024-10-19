using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Text.Json;

public class ValidateConnectionReferences : Task
{
    [Required]
    public string ConnectionReferencesSolutionPath { get; set; } = string.Empty;

    [Required]
    public ITaskItem[] WorkflowDefinitions { get; set; }

    [Required]
    public ITaskItem[] WorkflowMetadataFiles { get; set; }

    public override bool Execute()
    {
        List<string> errorMessages;

        bool validationResult = ValidateAllFlowConnectionReferences(ConnectionReferencesSolutionPath, out errorMessages);

        if (!validationResult)
        {
            foreach (var error in errorMessages)
            {
                Log.LogError(error);
            }

            Log.LogError("Connection reference validation failed. See output for details.");
        }

        return validationResult;
    }

    private bool ValidateAllFlowConnectionReferences(string connectionReferencesPath, out List<string> errorMessages)
    {
        errorMessages = new List<string>();

        var flowFiles = WorkflowDefinitions.Select(item => item.ItemSpec).ToList();

        if (flowFiles.Count == 0)
        {
            Log.LogMessage(MessageImportance.Normal, "No Power Automate flows found.");
            return true; // No flows to validate
        }

        // Load connection references from Customizations.xml
        var customizationsPath = Path.Combine(connectionReferencesPath, "Other", "Customizations.xml");
        if (!File.Exists(customizationsPath))
        {
            errorMessages.Add($"Error: Customizations.xml not found at: {customizationsPath}");
            return false;
        }

        var definedConnectionReferences = ExtractDefinedConnectionReferences(customizationsPath);

        // Validate connection references for each flow
        bool allValid = true;
        foreach (var flowFile in flowFiles)
        {
            if (!ValidateFlowConnectionReferences(flowFile, definedConnectionReferences, out var flowErrors))
            {
                allValid = false;
                errorMessages.AddRange(flowErrors); // Accumulate errors for this flow
            }
        }

        return allValid;
    }

    private HashSet<string> ExtractDefinedConnectionReferences(string customizationsPath)
    {
        var doc = XDocument.Load(customizationsPath);
        return new HashSet<string>(
            doc.Descendants("connectionreference")
               .Select(cr => cr.Attribute("connectionreferencelogicalname")?.Value)
               .Where(name => !string.IsNullOrEmpty(name))
        );
    }

    private bool ValidateFlowConnectionReferences(string flowJsonPath, HashSet<string> definedConnectionReferences, out List<string> errorMessages)
    {
        errorMessages = new List<string>();

        if (!File.Exists(flowJsonPath))
        {
            errorMessages.Add($"Error: JSON definition not found for flow: {flowJsonPath}");
            return false;
        }

        var jsonContent = File.ReadAllText(flowJsonPath);
        try
        {
            using (JsonDocument document = JsonDocument.Parse(jsonContent))
            {
                JsonElement root = document.RootElement;

                if (root.TryGetProperty("properties", out JsonElement propertiesElement) &&
                    propertiesElement.TryGetProperty("connectionReferences", out JsonElement connectionReferencesElement) &&
                    connectionReferencesElement.ValueKind == JsonValueKind.Object)
                {
                    bool allValid = true;
                    foreach (JsonProperty connectionReference in connectionReferencesElement.EnumerateObject())
                    {
                        if (connectionReference.Value.TryGetProperty("connection", out JsonElement connectionElement) &&
                            connectionElement.TryGetProperty("connectionReferenceLogicalName", out JsonElement logicalNameElement))
                        {
                            string logicalName = logicalNameElement.GetString();
                            if (string.IsNullOrEmpty(logicalName) || !definedConnectionReferences.Contains(logicalName))
                            {
                                errorMessages.Add($"Error: Invalid connection reference '{logicalName}' in flow {flowJsonPath}");
                                allValid = false;
                            }
                        }
                    }
                    return allValid;
                }
            }
            Log.LogMessage(MessageImportance.High, $"No connection references found in flow: {flowJsonPath}");
            return true;
        }
        catch (JsonException ex)
        {
            errorMessages.Add($"Error: Failed to parse JSON for flow {flowJsonPath}: {ex.Message}");
            return false;
        }
    }
}
