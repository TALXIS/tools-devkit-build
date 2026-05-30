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
        var fileGuid = page.GetMetadata("FileGuid");
        var projectXml = page.GetMetadata("ProjectXmlPath");
        var fileXml = page.GetMetadata("FileXmlPath");
        var compiledJs = page.GetMetadata("CompiledJsPath");
        var entrySource = page.GetMetadata("EntrySourcePath");
        var config = page.GetMetadata("ConfigJsonPath");
        var firstPrompt = page.GetMetadata("FirstPromptJsonPath");

        RequireFile(projectXml, $"GenPage project XML for {pageName}");
        RequireFile(fileXml, $"GenPage file XML for {pageName}");
        RequireFile(compiledJs, $"compiled GenPage bundle for {pageName}");
        RequireFile(entrySource, $"GenPage source entry for {pageName}");
        if (!string.IsNullOrWhiteSpace(config)) RequireFile(config, $"GenPage config for {pageName}");
        if (!string.IsNullOrWhiteSpace(firstPrompt)) RequireFile(firstPrompt, $"GenPage firstPrompt for {pageName}");
        if (Log.HasLoggedErrors) return;

        var pageDir = Path.Combine(metadataRoot, "uxagentprojects", pageGuid);
        var fileDir = Path.Combine(pageDir, fileGuid);
        var fileContent = Path.Combine(fileDir, "filecontent");
        if (Directory.Exists(fileContent))
            Directory.Delete(fileContent, true);

        Directory.CreateDirectory(fileDir);
        Directory.CreateDirectory(Path.Combine(fileContent, "src", "pages"));

        File.Copy(projectXml, Path.Combine(pageDir, "uxagentproject.xml"), true);
        File.Copy(fileXml, Path.Combine(fileDir, "uxagentprojectfile.xml"), true);
        File.Copy(compiledJs, Path.Combine(fileContent, "src", "pages", "page.compiled"), true);
        File.Copy(entrySource, Path.Combine(fileContent, "src", "pages", "page.tsx"), true);
        if (!string.IsNullOrWhiteSpace(config)) File.Copy(config, Path.Combine(fileContent, "config.json"), true);
        if (!string.IsNullOrWhiteSpace(firstPrompt)) File.Copy(firstPrompt, Path.Combine(fileContent, "firstPrompt.json"), true);

        Log.LogMessage(MessageImportance.High, $"Projected GenPage '{pageName}' native tree to {pageDir}");
    }

    private void RequireFile(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            Log.LogError($"Missing {description}: {path}");
    }
}
