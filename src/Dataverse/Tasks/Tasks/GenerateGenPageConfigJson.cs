using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class GenerateGenPageConfigJson : Task
{
    public string DataSources { get; set; }

    [Required]
    public string OutputPath { get; set; }

    public override bool Execute()
    {
        try
        {
            var sources = new JArray();

            if (!string.IsNullOrWhiteSpace(DataSources))
            {
                var items = DataSources
                    .Split(',')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0);

                foreach (var item in items)
                {
                    sources.Add(item);
                }
            }

            var config = new JObject
            {
                ["dataSources"] = sources,
                ["model"] = ""
            };

            var directory = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(OutputPath, config.ToString(Formatting.Indented), new UTF8Encoding(false));

            Log.LogMessage(MessageImportance.High, $"Generated GenPage config.json: {OutputPath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
    }
}
