using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

public class RetrieveProjectReferences : Task
{
    [Required]
    public string CurrentProjectFullPath { get; set; }

    [Output]
    public ITaskItem[] ReferencedProjects { get; private set; }

    public override bool Execute()
    {
        var projects = new List<ITaskItem>();
        try
        {
            RetrieveAllProjectReferences(CurrentProjectFullPath, projects);
            ReferencedProjects = projects.ToArray();
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
        return true;
    }

    private void RetrieveAllProjectReferences(string projectPath, List<ITaskItem> projects)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
            return;

        var projectDir = Path.GetDirectoryName(projectPath);
        var doc = XDocument.Load(projectPath);

        XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
        var descendants = doc.Descendants(ns + "ProjectReference");
        if (descendants == null || !descendants.Any())
        {
            ns = "";
            descendants = doc.Descendants(ns + "ProjectReference");
        }

        foreach (var reference in descendants)
        {
            var referencedProjectPath = Directory.GetParent(Path.Combine(projectDir, reference.Attribute("Include").Value)).FullName;
            if (!projects.Exists(p => string.Equals(p.ItemSpec, referencedProjectPath, StringComparison.OrdinalIgnoreCase)))
            {
                projects.Add(new TaskItem(referencedProjectPath));
                RetrieveAllProjectReferences(referencedProjectPath, projects);
            }
        }
    }
}
