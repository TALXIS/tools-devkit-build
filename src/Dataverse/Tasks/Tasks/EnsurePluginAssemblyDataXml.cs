using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

public sealed class EnsurePluginAssemblyDataXml : Task
{
    [Required]
    public string PluginRootPath { get; set; } = "";

    [Required]
    public string PluginAssemblyId { get; set; } = "";

    public string RepositoryRoot { get; set; } = "";

    public string Configuration { get; set; } = "Debug";
    public string TargetFramework { get; set; } = "net462";
    public string PublishFolderName { get; set; } = "publish";
    public string PluginDllPath { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            ValidatePluginRootPath();
            string repoRoot = GetRepositoryRoot();

            string csprojPath = FindProjectFile(PluginRootPath);
            string csprojFileName = Path.GetFileNameWithoutExtension(csprojPath);
            Tuple<string, string> meta = ReadProjectMetadata(csprojPath, csprojFileName);
            string assemblyName = meta.Item1;

            string existingId = FindPluginAssemblyId(repoRoot, assemblyName);
            string effectiveId = !string.IsNullOrWhiteSpace(existingId) ? existingId : PluginAssemblyId;
            if (string.IsNullOrWhiteSpace(effectiveId))
                effectiveId = Guid.NewGuid().ToString("D");

            string normalizedGuid = NormalizeGuid(effectiveId);
            PluginAssemblyId = normalizedGuid;

            PluginProjectInfo info = BuildProjectInfo(repoRoot, normalizedGuid);

            GeneratePluginAssemblyData(info, normalizedGuid);

            Log.LogMessage(MessageImportance.High, "PluginAssembly data xml generated: " + info.XmlPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    private void ValidatePluginRootPath()
    {
        if (string.IsNullOrWhiteSpace(PluginRootPath))
            throw new ArgumentException("PluginRootPath is empty");

        if (!Directory.Exists(PluginRootPath))
            throw new DirectoryNotFoundException("PluginRootPath not found: " + PluginRootPath);
    }

    private string GetRepositoryRoot()
    {
        return !string.IsNullOrWhiteSpace(RepositoryRoot)
            ? RepositoryRoot
            : Directory.GetCurrentDirectory();
    }

    private PluginProjectInfo BuildProjectInfo(string repoRoot, string normalizedGuid)
    {
        string csprojPath = FindProjectFile(PluginRootPath);
        string projectDirectory = GetProjectDirectory(csprojPath);
        string csprojFileName = Path.GetFileNameWithoutExtension(csprojPath);

        Tuple<string, string> meta = ReadProjectMetadata(csprojPath, csprojFileName);
        string assemblyName = meta.Item1;
        string fileVersion = meta.Item2;

        string dllPath = ResolvePluginDllPath(assemblyName);
        string xmlPath = BuildPluginDataXmlPath(repoRoot, assemblyName, normalizedGuid);

        return new PluginProjectInfo
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

    private void GeneratePluginAssemblyData(PluginProjectInfo info, string normalizedGuid)
    {
        if (!File.Exists(info.DllPath))
            throw new FileNotFoundException("Build not found", info.DllPath);

        string publicKeyToken = GetPublicKeyTokenFromPath(info.DllPath);

        var probeDirs = new List<string>
        {
            Path.GetDirectoryName(info.DllPath),
            Path.Combine(PluginRootPath, "bin", Configuration, TargetFramework),
            info.ProjectDirectory
        };

        List<string> classList = CollectPluginClasses(info.DllPath, probeDirs);
        if (!classList.Any())
            throw new Exception("Plugins not found");

        EnsureDirectoryForFile(info.XmlPath);

        XmlDocument pluginDoc = CreatePluginAssemblyDocument(
            info.AssemblyName,
            info.FileVersion,
            publicKeyToken,
            normalizedGuid,
            classList,
            info.CsprojFileName,
            info.XmlPath,
            info.RepositoryRoot
        );

        pluginDoc.Save(info.XmlPath);

        UpsertRootComponentIntoSolutionXml(
            info.RepositoryRoot,
            normalizedGuid,
            info.AssemblyName,
            info.FileVersion,
            publicKeyToken
        );
    }

    private static List<string> CollectPluginClasses(string dllPath, IEnumerable<string> probeDirs)
    {
        var result = new List<string>();

        using (var scanner = new MetadataTypeScanner(dllPath, probeDirs))
        {
            foreach (var type in scanner.GetPublicTypes())
            {
                if (type.IsInterface || type.IsAbstract)
                    continue;
                if (!type.ImplementsInterface("Microsoft.Xrm.Sdk", "IPlugin"))
                    continue;

                if (!string.IsNullOrWhiteSpace(type.FullName))
                    result.Add(type.FullName);
            }
        }

        return result;
    }

    private static string FindProjectFile(string pluginRootPath)
    {
        string csprojPath = Directory.GetFiles(pluginRootPath, "*.csproj").FirstOrDefault();
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

    private static string BuildPluginDataXmlPath(string repoRoot, string assemblyName, string normalizedGuid)
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

    private string FindPluginAssemblyId(string repoRoot, string assemblyName)
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

    private string BuildPluginDllPath(string assemblyName)
    {
        return Path.Combine(
            PluginRootPath,
            "bin",
            Configuration,
            TargetFramework,
            PublishFolderName,
            assemblyName + ".dll"
        );
    }

    private string ResolvePluginDllPath(string assemblyName)
    {
        if (!string.IsNullOrWhiteSpace(PluginDllPath))
        {
            var candidate = PluginDllPath;
            if (!Path.IsPathRooted(candidate))
                candidate = Path.Combine(PluginRootPath, candidate);

            return Path.GetFullPath(candidate);
        }

        return BuildPluginDllPath(assemblyName);
    }

    private static string GetPublicKeyTokenFromPath(string dllPath)
    {
        byte[] token = AssemblyName.GetAssemblyName(dllPath).GetPublicKeyToken();

        if (token == null || token.Length == 0)
            throw new Exception("Build not signed");

        return BitConverter.ToString(token).Replace("-", "").ToLowerInvariant();
    }

    private static string EnsureDirectoryForFile(string filePath)
    {
        string dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir))
            throw new Exception("xml directory not resolved");

        Directory.CreateDirectory(dir);

        return dir;
    }

    private static XmlDocument CreatePluginAssemblyDocument(
        string assemblyName,
        string fileVersion,
        string publicKeyToken,
        string normalizedGuid,
        IEnumerable<string> classList,
        string csprojFileName,
        string existingXmlPath,
        string repoRoot)
    {
        string pluginBaseName = string.IsNullOrEmpty(csprojFileName) ? "" : csprojFileName + ".PluginBase";

        if (File.Exists(existingXmlPath))
        {
            return UpdateExistingPluginAssemblyDocument(
                existingXmlPath, classList, pluginBaseName,
                assemblyName, fileVersion, publicKeyToken);
        }

        return CreateNewPluginAssemblyDocument(
            assemblyName, fileVersion, publicKeyToken, normalizedGuid,
            classList, pluginBaseName, existingXmlPath, repoRoot);
    }

    private static XmlDocument UpdateExistingPluginAssemblyDocument(
        string existingXmlPath,
        IEnumerable<string> classList,
        string pluginBaseName,
        string assemblyName,
        string fileVersion,
        string publicKeyToken)
    {
        var pluginDoc = new XmlDocument();
        pluginDoc.Load(existingXmlPath);

        var pluginTypesNode = pluginDoc.SelectSingleNode("//PluginAssembly/PluginTypes") as XmlElement;
        if (pluginTypesNode == null)
        {
            var root = pluginDoc.DocumentElement;
            if (root == null)
                throw new Exception("Existing XML has no document element");

            pluginTypesNode = pluginDoc.CreateElement("PluginTypes");
            root.AppendChild(pluginTypesNode);
        }

        var existingClassNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (XmlNode node in pluginTypesNode.ChildNodes)
        {
            var el = node as XmlElement;
            if (el == null || !string.Equals(el.Name, "PluginType", StringComparison.Ordinal))
                continue;

            string className = GetPluginTypeClassName(el);
            if (!string.IsNullOrWhiteSpace(className))
                existingClassNames.Add(className);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var className in classList)
        {
            if (className == pluginBaseName)
                continue;

            if (!seen.Add(className))
                continue;

            if (existingClassNames.Contains(className))
                continue;

            XmlElement pluginType = CreatePluginTypeElement(pluginDoc);
            pluginType.SetAttribute("PluginTypeId", Guid.NewGuid().ToString("D"));
            pluginType.SetAttribute("Name", className);
            pluginType.SetAttribute(
                "AssemblyQualifiedName",
                BuildAssemblyQualifiedTypeName(className, assemblyName, fileVersion, publicKeyToken)
            );

            var friendlyName = pluginDoc.CreateElement("FriendlyName");
            friendlyName.InnerText = Guid.NewGuid().ToString("D");
            pluginType.AppendChild(friendlyName);

            pluginTypesNode.AppendChild(pluginType);
        }

        return pluginDoc;
    }

    private static XmlDocument CreateNewPluginAssemblyDocument(
        string assemblyName,
        string fileVersion,
        string publicKeyToken,
        string normalizedGuid,
        IEnumerable<string> classList,
        string pluginBaseName,
        string xmlPath,
        string repoRoot)
    {
        var pluginDoc = new XmlDocument();
        var xmlDecl = pluginDoc.CreateXmlDeclaration("1.0", "utf-8", null);
        pluginDoc.AppendChild(xmlDecl);

        XmlElement root = pluginDoc.CreateElement("PluginAssembly");
        root.SetAttribute("FullName", BuildAssemblyFullName(assemblyName, fileVersion, publicKeyToken));
        root.SetAttribute("PluginAssemblyId", normalizedGuid);
        root.SetAttribute("CustomizationLevel", "1");
        root.SetAttribute("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
        pluginDoc.AppendChild(root);

        XmlElement isolationMode = pluginDoc.CreateElement("IsolationMode");
        isolationMode.InnerText = "2";
        root.AppendChild(isolationMode);

        XmlElement sourceType = pluginDoc.CreateElement("SourceType");
        sourceType.InnerText = "0";
        root.AppendChild(sourceType);

        XmlElement fileName = pluginDoc.CreateElement("FileName");
        fileName.InnerText = BuildRelativeDllPath(xmlPath, repoRoot, assemblyName);
        root.AppendChild(fileName);

        XmlElement pluginTypes = pluginDoc.CreateElement("PluginTypes");
        root.AppendChild(pluginTypes);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var className in classList)
        {
            if (className == pluginBaseName)
                continue;

            if (!seen.Add(className))
                continue;

            XmlElement pluginType = CreatePluginTypeElement(pluginDoc);
            pluginType.SetAttribute("PluginTypeId", Guid.NewGuid().ToString("D"));
            pluginType.SetAttribute("Name", className);
            pluginType.SetAttribute(
                "AssemblyQualifiedName",
                BuildAssemblyQualifiedTypeName(className, assemblyName, fileVersion, publicKeyToken)
            );

            var friendlyName = pluginDoc.CreateElement("FriendlyName");
            friendlyName.InnerText = Guid.NewGuid().ToString("D");
            pluginType.AppendChild(friendlyName);

            pluginTypes.AppendChild(pluginType);
        }

        return pluginDoc;
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

        if (existing != null)
        {
            // Assembly already registered in Solution.xml — do not modify
            return;
        }

        XmlElement rc = doc.CreateElement("RootComponent");
        rc.SetAttribute("type", "91");
        rc.SetAttribute("id", desiredIdBraced);
        rc.SetAttribute("schemaName", BuildAssemblyFullName(assemblyName, fileVersion, publicKeyToken));
        rc.SetAttribute("behavior", "0");
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

    private static string NormalizeGuid(string guidText)
    {
        if (string.IsNullOrWhiteSpace(guidText))
            throw new ArgumentException("PluginAssemblyId is empty");

        var trimmed = guidText.Trim().Trim('{', '}');

        Guid g;
        if (!Guid.TryParse(trimmed, out g))
            throw new ArgumentException("PluginAssemblyId is not a valid GUID: " + guidText);

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

    private static string GetPluginTypeClassName(XmlElement pluginTypeElement)
    {
        string aqn = pluginTypeElement.GetAttribute("AssemblyQualifiedName");
        if (!string.IsNullOrWhiteSpace(aqn))
        {
            int commaIndex = aqn.IndexOf(',');
            if (commaIndex < 0)
                return aqn.Trim();
            return aqn.Substring(0, commaIndex).Trim();
        }

        string nameAttr = pluginTypeElement.GetAttribute("Name");
        if (!string.IsNullOrWhiteSpace(nameAttr))
            return nameAttr.Trim();

        return "";
    }

    private static XmlElement CreatePluginTypeElement(XmlDocument doc)
    {
        return doc.CreateElement("PluginType");
    }

    private sealed class PluginProjectInfo
    {
        public string RepositoryRoot { get; set; } = "";
        public string ProjectDirectory { get; set; } = "";
        public string CsprojFileName { get; set; } = "";
        public string AssemblyName { get; set; } = "";
        public string FileVersion { get; set; } = "";
        public string XmlPath { get; set; } = "";
        public string DllPath { get; set; } = "";
    }
}
