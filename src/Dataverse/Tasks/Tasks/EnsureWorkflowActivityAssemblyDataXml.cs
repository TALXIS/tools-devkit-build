using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Threading;
#if NET6_0_OR_GREATER
using System.Runtime.Loader;
#endif

public sealed class EnsureWorkflowActivityAssemblyDataXml : Task
{
    [Required]
    public string WorkflowActivityRootPath { get; set; } = "";

    [Required]
    public string WorkflowActivityAssemblyId { get; set; } = "";

    public string RepositoryRoot { get; set; } = "";

    public string Configuration { get; set; } = "Debug";
    public string TargetFramework { get; set; } = "net462";
    public string PublishFolderName { get; set; } = "publish";
    public string WorkflowActivityDllPath { get; set; } = "";
    public string DefaultWorkflowActivityGroupName { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            ValidateRootPath();
            string repoRoot = GetRepositoryRoot();

            string csprojPath = FindProjectFile(WorkflowActivityRootPath);
            string csprojFileName = Path.GetFileNameWithoutExtension(csprojPath);
            Tuple<string, string> meta = ReadProjectMetadata(csprojPath, csprojFileName);
            string assemblyName = meta.Item1;

            string existingId = FindWorkflowActivityAssemblyId(repoRoot, assemblyName);
            string effectiveId = !string.IsNullOrWhiteSpace(existingId) ? existingId : WorkflowActivityAssemblyId;
            if (string.IsNullOrWhiteSpace(effectiveId))
                effectiveId = Guid.NewGuid().ToString("D");

            string normalizedGuid = NormalizeGuid(effectiveId);
            WorkflowActivityAssemblyId = normalizedGuid;

            WorkflowActivityProjectInfo info = BuildProjectInfo(repoRoot, normalizedGuid);

            GenerateWorkflowActivityAssemblyData(info, normalizedGuid);

            Log.LogMessage(MessageImportance.High, "WorkflowActivityAssembly data xml generated: " + info.XmlPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    private void ValidateRootPath()
    {
        if (string.IsNullOrWhiteSpace(WorkflowActivityRootPath))
            throw new ArgumentException("WorkflowActivityRootPath is empty");

        if (!Directory.Exists(WorkflowActivityRootPath))
            throw new DirectoryNotFoundException("WorkflowActivityRootPath not found: " + WorkflowActivityRootPath);
    }

    private string GetRepositoryRoot()
    {
        return !string.IsNullOrWhiteSpace(RepositoryRoot)
            ? RepositoryRoot
            : Directory.GetCurrentDirectory();
    }

    private WorkflowActivityProjectInfo BuildProjectInfo(string repoRoot, string normalizedGuid)
    {
        string csprojPath = FindProjectFile(WorkflowActivityRootPath);
        string projectDirectory = GetProjectDirectory(csprojPath);
        string csprojFileName = Path.GetFileNameWithoutExtension(csprojPath);

        Tuple<string, string> meta = ReadProjectMetadata(csprojPath, csprojFileName);
        string assemblyName = meta.Item1;
        string fileVersion = meta.Item2;

        string dllPath = ResolveWorkflowActivityDllPath(assemblyName);
        string xmlPath = BuildWorkflowActivityDataXmlPath(repoRoot, assemblyName, normalizedGuid);

        return new WorkflowActivityProjectInfo
        {
            RepositoryRoot = repoRoot,
            ProjectDirectory = projectDirectory,
            CsprojFileName = csprojFileName,
            AssemblyName = assemblyName,
            FileVersion = fileVersion,
            XmlPath = xmlPath,
            DllPath = dllPath
        };
    }

    private void GenerateWorkflowActivityAssemblyData(WorkflowActivityProjectInfo info, string normalizedGuid)
    {
        if (!File.Exists(info.DllPath))
            throw new FileNotFoundException("Build not found", info.DllPath);

        string tempDllPath = CopyDllToTempFolder(info);

        HashSet<string> probeDirs = BuildProbeDirectories(tempDllPath, info.ProjectDirectory);
        ResolveEventHandler handler = CreateAssemblyResolveHandler(probeDirs);

        AppDomain.CurrentDomain.AssemblyResolve += handler;

        try
        {
            TryAddSdkAssemblyProbe(probeDirs);

            Assembly workflowActivityAssembly = LoadWorkflowActivityAssembly(tempDllPath, info.AssemblyName, probeDirs);
            string publicKeyToken = GetPublicKeyToken(workflowActivityAssembly);

            List<WorkflowActivityTypeInfo> classList = GetWorkflowActivityClassInfos(workflowActivityAssembly, info.FileVersion);
            if (!classList.Any())
                throw new Exception("WorkflowActivities not found in assembly " + info.AssemblyName);

            string xmlDir = EnsureDirectoryForFile(info.XmlPath);

            XmlDocument workflowActivityDoc = CreateWorkflowActivityAssemblyDocument(
                info.AssemblyName,
                info.FileVersion,
                publicKeyToken,
                normalizedGuid,
                classList,
                info.CsprojFileName,
                info.XmlPath,
                info.RepositoryRoot
            );

            workflowActivityDoc.Save(info.XmlPath);

            UpsertRootComponentIntoSolutionXml(
                info.RepositoryRoot,
                normalizedGuid,
                info.AssemblyName,
                info.FileVersion,
                publicKeyToken
            );
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= handler;
        }
    }

    private static string FindProjectFile(string rootPath)
    {
        string csprojPath = Directory.GetFiles(rootPath, "*.csproj").FirstOrDefault();
        if (csprojPath == null)
            throw new Exception("csproj not found");

        return csprojPath;
    }

    private static string GetProjectDirectory(string csprojPath)
    {
        string projectDirectory = Path.GetDirectoryName(csprojPath);
        if (string.IsNullOrEmpty(projectDirectory))
            throw new Exception("ProjectDirectory not resolved");

        return projectDirectory;
    }

    private static string FindExistingDataXml(string repoRoot, string assemblyName)
    {
        string pluginAssembliesRoot = Path.Combine(repoRoot, "PluginAssemblies");

        if (!Directory.Exists(pluginAssembliesRoot))
            return null;

        string dataXmlFileName = assemblyName + ".dll.data.xml";

        var files = Directory.GetFiles(pluginAssembliesRoot, dataXmlFileName, SearchOption.AllDirectories);

        return files.FirstOrDefault();
    }

    private static string BuildWorkflowActivityDataXmlPath(string repoRoot, string assemblyName, string normalizedGuid)
    {
        string existingXmlPath = FindExistingDataXml(repoRoot, assemblyName);

        if (existingXmlPath != null)
        {
            return existingXmlPath;
        }

        return Path.Combine(
            repoRoot,
            "PluginAssemblies",
            assemblyName + ".dll.data.xml"
        );
    }

    private string FindWorkflowActivityAssemblyId(string repoRoot, string assemblyName)
    {
        string existingXmlPath = FindExistingDataXml(repoRoot, assemblyName);

        if (existingXmlPath == null)
            return "";

        var doc = XDocument.Load(existingXmlPath);
        var root = doc.Root;
        if (root == null)
            return "";

        var idAttr = root.Attribute("PluginAssemblyId");
        return idAttr == null ? "" : idAttr.Value;
    }

    private string BuildWorkflowActivityDllPath(string assemblyName)
    {
        return Path.Combine(
            WorkflowActivityRootPath,
            "bin",
            Configuration,
            TargetFramework,
            PublishFolderName,
            assemblyName + ".dll"
        );
    }

    private string ResolveWorkflowActivityDllPath(string assemblyName)
    {
        if (!string.IsNullOrWhiteSpace(WorkflowActivityDllPath))
        {
            var candidate = WorkflowActivityDllPath;
            if (!Path.IsPathRooted(candidate))
                candidate = Path.Combine(WorkflowActivityRootPath, candidate);

            return Path.GetFullPath(candidate);
        }

        return BuildWorkflowActivityDllPath(assemblyName);
    }

    private HashSet<string> BuildProbeDirectories(string dllPath, string projectDirectory)
    {
        string dllDir = Path.GetDirectoryName(dllPath);
        if (string.IsNullOrEmpty(dllDir))
            throw new Exception("dll directory not resolved");

        var probeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        probeDirs.Add(dllDir);
        probeDirs.Add(Path.Combine(WorkflowActivityRootPath, "bin", Configuration, TargetFramework));
        probeDirs.Add(projectDirectory);

        return probeDirs;
    }

    private static ResolveEventHandler CreateAssemblyResolveHandler(HashSet<string> probeDirs)
    {
        return (sender, args) =>
        {
            string name = null;
            try
            {
                var an = new AssemblyName(args.Name);
                name = an.Name;
            }
            catch { /* ignore */ }

            if (string.IsNullOrWhiteSpace(name))
                return null;

            foreach (var dir in probeDirs)
            {
                var candidate = Path.Combine(dir, name + ".dll");
                if (File.Exists(candidate))
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(candidate);
                        return Assembly.Load(bytes);
                    }
                    catch { /* ignore */ }
                }
            }
            return null;
        };
    }

    private void TryAddSdkAssemblyProbe(HashSet<string> probeDirs)
    {
        string sdkPath = Path.Combine(WorkflowActivityRootPath, "bin", Configuration, TargetFramework, "Microsoft.Xrm.Sdk.dll");

        if (!File.Exists(sdkPath))
            return;

        TryLoadAssemblyNoThrow(sdkPath);

        string sdkDir = Path.GetDirectoryName(sdkPath);

        if (!string.IsNullOrEmpty(sdkDir))
            probeDirs.Add(sdkDir);

        // Add .NET Framework reference assemblies for System.Activities
        TryAddFrameworkAssemblyProbe(probeDirs);
    }

    private void TryAddFrameworkAssemblyProbe(HashSet<string> probeDirs)
    {
        // Try to find System.Activities in standard .NET Framework locations
        string[] possiblePaths = new[]
        {
            @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319",
            @"C:\Windows\Microsoft.NET\Framework\v4.0.30319",
            @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.6.2",
            @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8",
            @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2",
        };

        foreach (var path in possiblePaths)
        {
            if (Directory.Exists(path))
            {
                probeDirs.Add(path);
                string systemActivitiesPath = Path.Combine(path, "System.Activities.dll");
                if (File.Exists(systemActivitiesPath))
                {
                    TryLoadAssemblyNoThrow(systemActivitiesPath);
                }
            }
        }
    }

    private static string GetPublicKeyToken(Assembly assembly)
    {
        byte[] token = assembly.GetName().GetPublicKeyToken();

        if (token == null || token.Length == 0)
            throw new Exception("Build not signed");

        return BitConverter.ToString(token).Replace("-", "").ToLowerInvariant();
    }

    private List<WorkflowActivityTypeInfo> GetWorkflowActivityClassInfos(Assembly assembly, string fileVersion)
    {
        var result = new List<WorkflowActivityTypeInfo>();

        foreach (var type in GetTypesSafe(assembly))
        {
            if (!type.IsClass || !type.IsPublic || type.IsAbstract)
                continue;

            if (!InheritsFromByName(type, "System.Activities.CodeActivity"))
                continue;

            string fullName = type.FullName;
            if (string.IsNullOrWhiteSpace(fullName))
                continue;

            string groupName = GetWorkflowActivityGroupName(type, fileVersion);
            string displayName = GetWorkflowActivityDisplayName(type);

            result.Add(new WorkflowActivityTypeInfo
            {
                FullName = fullName,
                GroupName = groupName,
                DisplayName = displayName
            });
        }

        return result;
    }

    private string GetWorkflowActivityGroupName(Type type, string fileVersion)
    {
        // Try to get from CrmPluginRegistrationAttribute (Group parameter)
        foreach (var attr in type.GetCustomAttributesData())
        {
            if (attr.AttributeType.Name == "CrmPluginRegistrationAttribute")
            {
                // Look for Group named argument
                foreach (var namedArg in attr.NamedArguments)
                {
                    if (namedArg.MemberName == "Group" && namedArg.TypedValue.Value is string groupValue)
                    {
                        if (!string.IsNullOrWhiteSpace(groupValue))
                            return groupValue + " (" + fileVersion + ")";
                    }
                }
            }
        }

        // Fallback to DefaultWorkflowActivityGroupName or assembly name
        string baseName = !string.IsNullOrWhiteSpace(DefaultWorkflowActivityGroupName)
            ? DefaultWorkflowActivityGroupName
            : type.Assembly.GetName().Name;

        return baseName + " (" + fileVersion + ")";
    }

    private static string GetWorkflowActivityDisplayName(Type type)
    {
        // Try to get from CrmPluginRegistrationAttribute (Name parameter)
        foreach (var attr in type.GetCustomAttributesData())
        {
            if (attr.AttributeType.Name == "CrmPluginRegistrationAttribute")
            {
                // First constructor argument is usually the Name
                if (attr.ConstructorArguments.Count > 0)
                {
                    var nameValue = attr.ConstructorArguments[0].Value as string;
                    if (!string.IsNullOrWhiteSpace(nameValue))
                        return nameValue;
                }
            }
        }

        // Fallback to class name
        return type.Name;
    }

    private static bool InheritsFromByName(Type t, string baseClassName)
    {
        try
        {
            Type current = t.BaseType;
            while (current != null)
            {
                // Check by FullName
                if (string.Equals(current.FullName, baseClassName, StringComparison.Ordinal))
                    return true;

                // Also check by Name only (in case namespace differs)
                string simpleClassName = baseClassName;
                int lastDot = baseClassName.LastIndexOf('.');
                if (lastDot >= 0)
                    simpleClassName = baseClassName.Substring(lastDot + 1);

                if (string.Equals(current.Name, simpleClassName, StringComparison.Ordinal))
                    return true;

                current = current.BaseType;
            }
        }
        catch
        {
            // If we can't inspect the type hierarchy, return false
        }
        return false;
    }

    private static string EnsureDirectoryForFile(string filePath)
    {
        string dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir))
            throw new Exception("xml directory not resolved");

        Directory.CreateDirectory(dir);

        return dir;
    }

    private static XmlDocument CreateWorkflowActivityAssemblyDocument(
        string assemblyName,
        string fileVersion,
        string publicKeyToken,
        string normalizedGuid,
        IEnumerable<WorkflowActivityTypeInfo> classList,
        string csprojFileName,
        string existingXmlPath,
        string repoRoot)
    {
        var doc = new XmlDocument();
        var xmlDecl = doc.CreateXmlDeclaration("1.0", "utf-8", null);
        doc.AppendChild(xmlDecl);

        XmlElement root = doc.CreateElement("PluginAssembly");
        root.SetAttribute("FullName", BuildAssemblyFullName(assemblyName, fileVersion, publicKeyToken));
        root.SetAttribute("PluginAssemblyId", normalizedGuid);
        root.SetAttribute("CustomizationLevel", "1");
        root.SetAttribute("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
        doc.AppendChild(root);

        XmlElement isolationMode = doc.CreateElement("IsolationMode");
        isolationMode.InnerText = "2";
        root.AppendChild(isolationMode);

        XmlElement sourceType = doc.CreateElement("SourceType");
        sourceType.InnerText = "0";
        root.AppendChild(sourceType);

        XmlElement introducedVersion = doc.CreateElement("IntroducedVersion");
        introducedVersion.InnerText = "1.0";
        root.AppendChild(introducedVersion);

        XmlElement fileName = doc.CreateElement("FileName");
        fileName.InnerText = BuildRelativeDllPath(existingXmlPath, repoRoot, assemblyName);
        root.AppendChild(fileName);

        XmlElement pluginTypes = doc.CreateElement("PluginTypes");
        root.AppendChild(pluginTypes);

        var existingPluginTypes = LoadExistingPluginTypeMap(existingXmlPath, doc);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var classInfo in classList)
        {
            string className = classInfo.FullName;

            if (!seen.Add(className))
                continue;

            XmlElement pluginType;

            if (existingPluginTypes.TryGetValue(className, out var existingPluginType))
            {
                pluginType = existingPluginType;
            }
            else
            {
                pluginType = CreatePluginTypeElement(doc);
                pluginType.SetAttribute("PluginTypeId", Guid.NewGuid().ToString("D"));
                pluginType.SetAttribute("Name", classInfo.DisplayName);
            }

            pluginType.SetAttribute(
                "AssemblyQualifiedName",
                BuildAssemblyQualifiedTypeName(className, assemblyName, fileVersion, publicKeyToken)
            );

            // Ensure Name attribute is set
            if (string.IsNullOrWhiteSpace(pluginType.GetAttribute("Name")))
                pluginType.SetAttribute("Name", classInfo.DisplayName);

            // Ensure FriendlyName element exists
            var friendlyName = pluginType.SelectSingleNode("FriendlyName") as XmlElement;
            if (friendlyName == null)
            {
                friendlyName = doc.CreateElement("FriendlyName");
                friendlyName.InnerText = Guid.NewGuid().ToString("D");
                pluginType.AppendChild(friendlyName);
            }

            // Update or create WorkflowActivityGroupName element
            var workflowGroupName = pluginType.SelectSingleNode("WorkflowActivityGroupName") as XmlElement;
            if (workflowGroupName == null)
            {
                workflowGroupName = doc.CreateElement("WorkflowActivityGroupName");
                pluginType.AppendChild(workflowGroupName);
            }
            workflowGroupName.InnerText = classInfo.GroupName;

            if (string.IsNullOrWhiteSpace(pluginType.GetAttribute("PluginTypeId")))
                pluginType.SetAttribute("PluginTypeId", Guid.NewGuid().ToString("D"));

            pluginTypes.AppendChild(pluginType);
        }

        return doc;
    }

    private static void UpsertRootComponentIntoSolutionXml(
        string repoRoot,
        string normalizedGuid,
        string assemblyName,
        string fileVersion,
        string publicKeyToken)
    {
        var solutionPath = Path.Combine(repoRoot, "Other", "Solution.xml");
        if (!File.Exists(solutionPath))
            throw new FileNotFoundException("Solution.xml not found", solutionPath);

        var doc = new XmlDocument();

        doc.Load(solutionPath);

        XmlElement rootComponents = doc.SelectSingleNode("//RootComponents") as XmlElement;
        if (rootComponents == null)
        {
            if (doc.DocumentElement == null)
                throw new Exception("Solution.xml has no document element");

            rootComponents = doc.CreateElement("RootComponents");
            doc.DocumentElement.AppendChild(rootComponents);
        }

        var desiredIdBraced = "{" + normalizedGuid + "}";

        XmlElement existing = null;
        foreach (XmlNode n in rootComponents.ChildNodes)
        {
            var el = n as XmlElement;
            if (el == null) continue;
            if (!string.Equals(el.Name, "RootComponent", StringComparison.Ordinal)) continue;

            var typeAttr = el.GetAttribute("type");
            if (!string.Equals(typeAttr, "91", StringComparison.Ordinal)) continue;

            var idAttr = el.GetAttribute("id");
            if (IsSameGuidBraced(idAttr, desiredIdBraced))
            {
                existing = el;
                break;
            }
        }

        XmlElement rc = existing ?? doc.CreateElement("RootComponent");
        rc.SetAttribute("type", "91");
        rc.SetAttribute("id", desiredIdBraced);
        rc.SetAttribute("schemaName", BuildAssemblyFullName(assemblyName, fileVersion, publicKeyToken));
        rc.SetAttribute("behavior", "0");

        if (existing == null)
            rootComponents.AppendChild(rc);

        doc.Save(solutionPath);
    }

    private static bool IsSameGuidBraced(string a, string b)
    {
        string na = NormalizeGuidBraces(a);
        string nb = NormalizeGuidBraces(b);

        return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeGuidBraces(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";

        return s.Trim().Trim('{', '}');
    }

    private static string BuildRelativeDllPath(string xmlPath, string repoRoot, string assemblyName)
    {
        string xmlDir = Path.GetDirectoryName(xmlPath);
        if (string.IsNullOrEmpty(xmlDir))
            return "/PluginAssemblies/" + assemblyName + ".dll";

        string pluginAssembliesRoot = Path.Combine(repoRoot, "PluginAssemblies");
        string relativePath;

        if (xmlDir.Equals(pluginAssembliesRoot, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = "/PluginAssemblies/" + assemblyName + ".dll";
        }
        else
        {
            string subFolder = xmlDir.Substring(pluginAssembliesRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            relativePath = "/PluginAssemblies/" + subFolder.Replace(Path.DirectorySeparatorChar, '/') + "/" + assemblyName + ".dll";
        }

        return relativePath;
    }

    private static string BuildAssemblyFullName(string assemblyName, string fileVersion, string publicKeyToken)
    {
        return assemblyName + ", Version=" + fileVersion + ", Culture=neutral, PublicKeyToken=" + publicKeyToken;
    }

    private static string BuildAssemblyQualifiedTypeName(string className, string assemblyName, string fileVersion, string publicKeyToken)
    {
        return className + ", " + BuildAssemblyFullName(assemblyName, fileVersion, publicKeyToken);
    }

    private static void TryLoadAssemblyNoThrow(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            Assembly.Load(bytes);
        }
        catch { /* ignore */ }
    }

    private Assembly LoadWorkflowActivityAssembly(string dllPath, string assemblyName, HashSet<string> probeDirs)
    {
        var alreadyLoaded = FindLoadedAssembly(assemblyName);
        if (alreadyLoaded != null)
            return alreadyLoaded;

#if NET6_0_OR_GREATER
        try
        {
            var alc = new AssemblyLoadContext("WorkflowActivityAssembly-" + Guid.NewGuid().ToString("N"), isCollectible: true);
            alc.Resolving += (context, name) =>
            {
                foreach (var dir in probeDirs)
                {
                    var candidate = Path.Combine(dir, name.Name + ".dll");
                    if (File.Exists(candidate))
                        return context.LoadFromAssemblyPath(candidate);
                }
                return null;
            };

            var bytes = File.ReadAllBytes(dllPath);
            var asm = alc.LoadFromStream(new MemoryStream(bytes));
            return asm;
        }
        catch (FileLoadException)
        {
            var loaded = FindLoadedAssembly(assemblyName);
            if (loaded != null)
                return loaded;
            throw;
        }
#else
        try
        {
            var bytes = File.ReadAllBytes(dllPath);
            return Assembly.Load(bytes);
        }
        catch (FileLoadException)
        {
            var loaded = FindLoadedAssembly(assemblyName);
            if (loaded != null)
                return loaded;
            throw;
        }
#endif
    }

    private static Assembly FindLoadedAssembly(string assemblyName)
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a =>
            {
                var name = a.GetName();
                return name != null && string.Equals(name.Name, assemblyName, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static string NormalizeGuid(string guidText)
    {
        if (string.IsNullOrWhiteSpace(guidText))
            throw new ArgumentException("WorkflowActivityAssemblyId is empty");

        var trimmed = guidText.Trim().Trim('{', '}');

        Guid g;
        if (!Guid.TryParse(trimmed, out g))
            throw new ArgumentException("WorkflowActivityAssemblyId is not a valid GUID: " + guidText);

        return g.ToString("D");
    }

    private static Tuple<string, string> ReadProjectMetadata(string csprojPath, string fallbackAssemblyName)
    {
        var xdoc = XDocument.Load(csprojPath);

        string assemblyName = xdoc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "AssemblyName")
            ?.Value;

        string fileVersion = xdoc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "FileVersion")
            ?.Value;

        assemblyName = (assemblyName ?? "").Trim();
        fileVersion = (fileVersion ?? "").Trim();

        if (string.IsNullOrWhiteSpace(assemblyName))
            assemblyName = fallbackAssemblyName;

        if (string.IsNullOrWhiteSpace(fileVersion))
            fileVersion = "1.0.0.0";

        return Tuple.Create(assemblyName, fileVersion);
    }

    private static IEnumerable<Type> GetTypesSafe(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException rtle)
        {
            return rtle.Types.Where(t => t != null).Cast<Type>();
        }
    }

    private static Dictionary<string, XmlElement> LoadExistingPluginTypeMap(string xmlPath, XmlDocument targetDoc)
    {
        var result = new Dictionary<string, XmlElement>(StringComparer.Ordinal);

        if (!File.Exists(xmlPath))
            return result;

        var existingDoc = new XmlDocument();
        existingDoc.Load(xmlPath);

        var pluginTypesNode = existingDoc.SelectSingleNode("//PluginAssembly/PluginTypes") as XmlElement;
        if (pluginTypesNode == null)
            return result;

        foreach (var node in pluginTypesNode.ChildNodes)
        {
            var el = node as XmlElement;
            if (el == null)
                continue;

            if (!string.Equals(el.Name, "PluginType", StringComparison.Ordinal))
                continue;

            string className = GetPluginTypeClassName(el);
            if (string.IsNullOrWhiteSpace(className))
                continue;

            if (result.ContainsKey(className))
                continue;

            var imported = (XmlElement)targetDoc.ImportNode(el, true);
            result[className] = imported;
        }

        return result;
    }

    private static string GetPluginTypeClassName(XmlElement pluginTypeElement)
    {
        string aqn = pluginTypeElement.GetAttribute("AssemblyQualifiedName");
        if (string.IsNullOrWhiteSpace(aqn))
            return "";

        int commaIndex = aqn.IndexOf(',');
        if (commaIndex < 0)
            return aqn.Trim();

        return aqn.Substring(0, commaIndex).Trim();
    }

    private static XmlElement CreatePluginTypeElement(XmlDocument doc)
    {
        return doc.CreateElement("PluginType");
    }

    private string CopyDllToTempFolder(WorkflowActivityProjectInfo info)
    {
        string tempDir = Path.Combine(
            info.RepositoryRoot,
            "obj",
            Configuration,
            TargetFramework,
            "Temp"
        );

        Directory.CreateDirectory(tempDir);

        string tempDllPath = Path.Combine(tempDir, info.AssemblyName + ".dll");
        File.Copy(info.DllPath, tempDllPath, true);

        return tempDllPath;
    }

    private sealed class WorkflowActivityProjectInfo
    {
        public string RepositoryRoot { get; set; } = "";
        public string ProjectDirectory { get; set; } = "";
        public string CsprojFileName { get; set; } = "";
        public string AssemblyName { get; set; } = "";
        public string FileVersion { get; set; } = "";
        public string XmlPath { get; set; } = "";
        public string DllPath { get; set; } = "";
    }

    private sealed class WorkflowActivityTypeInfo
    {
        public string FullName { get; set; } = "";
        public string GroupName { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }
}
