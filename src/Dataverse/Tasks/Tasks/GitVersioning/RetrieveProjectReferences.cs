using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        foreach (var includeValue in ProjectReferenceHelper.GetProjectReferenceIncludes(doc))
        {
            var referencedProjectPath = ProjectReferenceHelper.ResolveReferencedProjectDirectory(projectDir, includeValue);
            var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!projects.Exists(p => string.Equals(p.ItemSpec, referencedProjectPath, comparison)))
            {
                projects.Add(new TaskItem(referencedProjectPath));
                // Find the project file in the referenced directory for recursive resolution
                var refProjectFile = ProjectReferenceHelper.FindProjectFile(referencedProjectPath);
                if (refProjectFile != null)
                    RetrieveAllProjectReferences(refProjectFile, projects);
            }
        }
    }
}
