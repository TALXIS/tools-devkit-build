using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

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

        foreach (var reference in doc.Descendants(ns + "ProjectReference"))
        {
            var referencedProjectPath = Path.Combine(projectDir, reference.Attribute("Include").Value);
            if (!projects.Exists(p => string.Equals(p.ItemSpec, referencedProjectPath, StringComparison.OrdinalIgnoreCase)))
            {
                projects.Add(new TaskItem(referencedProjectPath));
                RetrieveAllProjectReferences(referencedProjectPath, projects);
            }
        }
    }
}
