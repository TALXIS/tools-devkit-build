using System;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public sealed class ProjectGenPageNativeTree : Task
{
    [Required]
    public string MetadataRoot { get; set; } = "";

    [Required]
    public ITaskItem[] Pages { get; set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        try
        {
            var metadataRoot = Path.GetFullPath(MetadataRoot);
            foreach (var page in Pages)
                ProjectPage(metadataRoot, page);
            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    private void ProjectPage(string metadataRoot, ITaskItem page)
    {
        var pageName = page.GetMetadata("PageName");
        var pageGuid = page.GetMetadata("PageGuid");
        var projectXml = page.GetMetadata("ProjectXmlPath");
        var compiledJs = page.GetMetadata("CompiledJsPath");
        var entrySource = page.GetMetadata("EntrySourcePath");
        var config = page.GetMetadata("ConfigJsonPath");
        var firstPrompt = page.GetMetadata("FirstPromptJsonPath");

        RequireFile(projectXml, $"GenPage project XML for {pageName}");
        RequireFile(page.GetMetadata("CompiledFileXmlPath"), $"compiled GenPage file XML for {pageName}");
        RequireFile(page.GetMetadata("SourceFileXmlPath"), $"source GenPage file XML for {pageName}");
        RequireFile(compiledJs, $"compiled GenPage bundle for {pageName}");
        RequireFile(entrySource, $"GenPage source entry for {pageName}");
        if (!string.IsNullOrWhiteSpace(config)) RequireFile(config, $"GenPage config for {pageName}");
        if (!string.IsNullOrWhiteSpace(firstPrompt)) RequireFile(firstPrompt, $"GenPage firstPrompt for {pageName}");
        if (Log.HasLoggedErrors) return;

        var pageDir = Path.Combine(metadataRoot, "uxagentprojects", pageGuid);
        if (Directory.Exists(pageDir))
            Directory.Delete(pageDir, true);

        Directory.CreateDirectory(pageDir);
        File.Copy(projectXml, Path.Combine(pageDir, "uxagentproject.xml"), true);

        ProjectFile(pageDir, page, "Compiled", compiledJs, "page.compiled", pageName, "");
        ProjectFile(pageDir, page, "Source", entrySource, "page.tsx", pageName, "");
        ProjectFile(pageDir, page, "Config", config, "config.json", pageName, "{\"dataSources\":[],\"model\":\"\"}");
        ProjectFile(pageDir, page, "FirstPrompt", firstPrompt, "firstPrompt.json", pageName, "{\"userMessage\":\"\",\"agentMessage\":\"\"}");

        Log.LogMessage(MessageImportance.High, $"Projected GenPage '{pageName}' native tree to {pageDir}");
    }

    private void ProjectFile(string pageDir, ITaskItem page, string prefix, string payloadPath, string payloadBaseName, string pageName, string defaultPayload)
    {
        var fileGuid = page.GetMetadata(prefix + "FileGuid");
        var fileXml = page.GetMetadata(prefix + "FileXmlPath");
        RequireFile(fileXml, $"{prefix} GenPage file XML for {pageName}");
        if (string.IsNullOrWhiteSpace(defaultPayload))
            RequireFile(payloadPath, $"{prefix} GenPage payload for {pageName}");
        else if (!string.IsNullOrWhiteSpace(payloadPath))
            RequireFile(payloadPath, $"{prefix} GenPage payload for {pageName}");
        if (Log.HasLoggedErrors) return;

        var fileDir = Path.Combine(pageDir, "uxagentprojectfiles", fileGuid);
        var fileContent = Path.Combine(fileDir, "filecontent");
        Directory.CreateDirectory(fileContent);
        File.Copy(fileXml, Path.Combine(fileDir, "uxagentprojectfile.xml"), true);

        var destination = Path.Combine(fileContent, payloadBaseName);
        if (!string.IsNullOrWhiteSpace(payloadPath))
            File.Copy(payloadPath, destination, true);
        else
            File.WriteAllText(destination, defaultPayload);
    }

    private void RequireFile(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            Log.LogError($"Missing {description}: {path}");
    }
}
