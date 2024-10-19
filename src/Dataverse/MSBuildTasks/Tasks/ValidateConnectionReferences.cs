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
    public string SolutionPath { get; set; } = string.Empty;

    public string ConnectionReferencesSolutionPath { get; set; } = string.Empty;

    public override bool Execute()
    {

        if (string.IsNullOrEmpty(ConnectionReferencesSolutionPath))
        {
            ConnectionReferencesSolutionPath = SolutionPath;
            Log.LogMessage(MessageImportance.Normal, $"ConnectionReferencesPath not set. Using SolutionPath: {ConnectionReferencesSolutionPath}");
        }

        List<string> errorMessages;

        bool validationResult = ValidateAllFlowConnectionReferences(SolutionPath, ConnectionReferencesSolutionPath, out errorMessages);

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

    private bool ValidateAllFlowConnectionReferences(string solutionPath, string connectionReferencesPath, out List<string> errorMessages)
    {
        errorMessages = new List<string>();

        // 1. Check if the solution has any Power Automate flow
        var workflowsPath = Path.Combine(solutionPath, "Workflows");

        if (!Directory.Exists(workflowsPath))
        {
            Log.LogMessage(MessageImportance.Normal, $"No Workflows folder found at {workflowsPath}. Skipping connection reference validation.");
            return true; // No workflows to validate
        }

        // 2. Find Power Automate flows (Category = 5)
        var flowFiles = Directory.GetFiles(workflowsPath, "*.xml")
            .Where(file =>
            {
                var doc = XDocument.Load(file);
                var category = doc.Descendants("Category").FirstOrDefault()?.Value;
                return category == "5";
            })
            .ToList();

        if (flowFiles.Count == 0)
        {
            Log.LogMessage(MessageImportance.Normal,"No Power Automate flows found.");
            return true; // No flows to validate
        }

        // 3. Load connection references from Customizations.xml
        var customizationsPath = Path.Combine(connectionReferencesPath, "Other", "Customizations.xml");
        if (!File.Exists(customizationsPath))
        {
            errorMessages.Add($"Error: Customizations.xml not found at: {customizationsPath}");
            return false;
        }

        var definedConnectionReferences = ExtractDefinedConnectionReferences(customizationsPath);

        // 4. Validate connection references for each flow
        bool allValid = true;
        foreach (var flowFile in flowFiles)
        {
            var flowName = XDocument.Load(flowFile).Root?.Attribute("Name")?.Value ?? Path.GetFileNameWithoutExtension(flowFile);

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

    private bool ValidateFlowConnectionReferences(string flowXmlPath, HashSet<string> definedConnectionReferences, out List<string> errorMessages)
    {
        errorMessages = new List<string>();

        // Extract the base name without the '.json.data.xml' extension
        var baseName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(flowXmlPath)));
        var flowJsonPath = Path.Combine(Path.GetDirectoryName(flowXmlPath), $"{baseName}.json");

        if (!File.Exists(flowJsonPath))
        {
            errorMessages.Add($"Error: JSON definition not found for flow: {baseName}.json");
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
                                errorMessages.Add($"Error: Invalid connection reference '{logicalName}' in flow {baseName}.json");
                                allValid = false;
                            }
                        }
                    }
                    return allValid;
                }
            }
            Log.LogMessage(MessageImportance.High, $"No connection references found in flow: {baseName}.json");
            return true;
        }
        catch (JsonException ex)
        {
            errorMessages.Add($"Error: Failed to parse JSON for flow {baseName}.json: {ex.Message}");
            return false;
        }
    }
}
