using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Collections.Generic;

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

        string xmlPath = BuildPluginDataXmlPath(repoRoot, assemblyName, normalizedGuid);
        string dllPath = BuildPluginDllPath(assemblyName);

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

        HashSet<string> probeDirs = BuildProbeDirectories(info.DllPath, info.ProjectDirectory);
        ResolveEventHandler handler = CreateAssemblyResolveHandler(probeDirs);

        AppDomain.CurrentDomain.AssemblyResolve += handler;

        try
        {
            TryAddSdkAssemblyProbe(probeDirs);

            Assembly pluginAssembly = Assembly.LoadFrom(info.DllPath);
            string publicKeyToken = GetPublicKeyToken(pluginAssembly);

            List<string> classList = GetPluginClassNames(pluginAssembly);
            if (!classList.Any())
                throw new Exception("Plugins not found");

            string xmlDir = EnsureDirectoryForFile(info.XmlPath);

            XmlDocument pluginDoc = CreatePluginAssemblyDocument(
                info.AssemblyName,
                info.FileVersion,
                publicKeyToken,
                normalizedGuid,
                classList,
                info.CsprojFileName
            );

            pluginDoc.Save(info.XmlPath);

            string destDllPath = Path.Combine(xmlDir, info.AssemblyName + ".dll");
            File.Copy(info.DllPath, destDllPath, true);

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

    private static string BuildPluginDataXmlPath(string repoRoot, string assemblyName, string normalizedGuid)
    {
        return Path.Combine(
            repoRoot,
            "PluginAssemblies",
            assemblyName + "-" + normalizedGuid.ToUpperInvariant(),
            assemblyName + ".dll.data.xml"
        );
    }

    private string FindPluginAssemblyId(string repoRoot, string assemblyName)
    {
        string pluginAssembliesRoot = Path.Combine(repoRoot, "PluginAssemblies");

        if (!Directory.Exists(pluginAssembliesRoot)) return "";

        var matchDirs = Directory.GetDirectories(pluginAssembliesRoot, "*" + assemblyName + "*");

        if (matchDirs.Length == 0) return "";

        var xmlPath = matchDirs.FirstOrDefault() == null ? null : Directory.GetFiles(matchDirs.FirstOrDefault(), "*.xml").FirstOrDefault();

        if (xmlPath == null) return "";

        var doc = XDocument.Load(xmlPath);
        var root = doc.Root;
        if (root == null) return "";

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

    private HashSet<string> BuildProbeDirectories(string dllPath, string projectDirectory)
    {
        string dllDir = Path.GetDirectoryName(dllPath);
        if (string.IsNullOrEmpty(dllDir))
            throw new Exception("dll directory not resolved");

        var probeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        probeDirs.Add(dllDir);
        probeDirs.Add(Path.Combine(PluginRootPath, "bin", Configuration, TargetFramework));
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
                    try { return Assembly.LoadFrom(candidate); }
                    catch { /* ignore */ }
                }
            }
            return null;
        };
    }

    private void TryAddSdkAssemblyProbe(HashSet<string> probeDirs)
    {
        string sdkPath = Path.Combine(PluginRootPath, "bin", Configuration, TargetFramework, "Microsoft.Xrm.Sdk.dll");
        
        if (!File.Exists(sdkPath))
            return;

        TryLoadAssemblyNoThrow(sdkPath);

        string sdkDir = Path.GetDirectoryName(sdkPath);

        if (!string.IsNullOrEmpty(sdkDir))
            probeDirs.Add(sdkDir);
    }

    private static string GetPublicKeyToken(Assembly pluginAssembly)
    {
        byte[] token = pluginAssembly.GetName().GetPublicKeyToken();

        if (token == null || token.Length == 0)
            throw new Exception("Build not signed");

        return BitConverter.ToString(token).Replace("-", "").ToLowerInvariant();
    }

    private static List<string> GetPluginClassNames(Assembly pluginAssembly)
    {
        return GetPluginTypesSafe(pluginAssembly)
            .Where(t => t.IsClass && t.IsPublic)
            .Where(t => ImplementsInterfaceByName(t, "Microsoft.Xrm.Sdk.IPlugin"))
            .Select(t => t.FullName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();
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
        string csprojFileName)
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
        fileName.InnerText = "/PluginAssemblies/" + assemblyName + "-" + normalizedGuid.ToUpperInvariant() + "/" + assemblyName + ".dll";
        root.AppendChild(fileName);

        XmlElement pluginTypes = pluginDoc.CreateElement("PluginTypes");
        root.AppendChild(pluginTypes);

        string pluginBaseName = string.IsNullOrEmpty(csprojFileName) ? "" : csprojFileName + ".PluginBase";

        foreach (var className in classList)
        {
            if (className == pluginBaseName)
                continue;

            XmlElement pluginType = pluginDoc.CreateElement("PluginType");
            pluginType.SetAttribute(
                "AssemblyQualifiedName",
                className + ", " + assemblyName + ", Version=" + fileVersion + ", Culture=neutral, PublicKeyToken=" + publicKeyToken
            );
            pluginType.SetAttribute("PluginTypeId", Guid.NewGuid().ToString("D"));
            pluginType.SetAttribute("Name", className);

            XmlElement friendlyName = pluginDoc.CreateElement("FriendlyName");
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


    private static string BuildAssemblyFullName(string assemblyName, string fileVersion, string publicKeyToken)
    {
        return assemblyName + ", Version=" + fileVersion + ", Culture=neutral, PublicKeyToken=" + publicKeyToken;
    }

    private static void TryLoadAssemblyNoThrow(string path)
    {
        try { Assembly.LoadFrom(path); }
        catch { /* ignore */ }
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

    private static IEnumerable<Type> GetPluginTypesSafe(Assembly asm)
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

    private static bool ImplementsInterfaceByName(Type t, string interfaceFullName)
    {
        try
        {
            return t.GetInterfaces().Any(i => string.Equals(i.FullName, interfaceFullName, StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
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
