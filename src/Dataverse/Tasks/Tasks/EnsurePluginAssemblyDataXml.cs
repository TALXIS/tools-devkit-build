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

    public string? RepositoryRoot { get; set; }

    public string Configuration { get; set; } = "Debug";

    public string TargetFramework { get; set; } = "net462";

    public string PublishFolderName { get; set; } = "publish";

    public override bool Execute()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(PluginRootPath))
                throw new ArgumentException("PluginRootPath is empty");

            if (!Directory.Exists(PluginRootPath))
                throw new DirectoryNotFoundException($"PluginRootPath not found: {PluginRootPath}");

            var normalizedGuid = NormalizeGuid(PluginAssemblyId);
            var repoRoot = !string.IsNullOrWhiteSpace(RepositoryRoot)
                ? RepositoryRoot!
                : Directory.GetCurrentDirectory();

            string csprojPath = Directory.GetFiles(PluginRootPath, "*.csproj").FirstOrDefault()
                ?? throw new Exception("csproj not found");

            string projectDirectory = Path.GetDirectoryName(csprojPath)
                ?? throw new Exception("ProjectDirectory not resolved");

            string csprojFileName = Path.GetFileNameWithoutExtension(csprojPath);

            (string assemblyName, string fileVersion) = ReadProjectMetadata(csprojPath, csprojFileName);

            string xmlPath = Path.Combine(
                repoRoot,
                "SolutionDeclarationsRoot",
                "PluginAssemblies",
                $"{assemblyName}-{normalizedGuid.ToUpperInvariant()}",
                $"{assemblyName}.dll.data.xml"
            );

            string dllPath = Path.Combine(
                PluginRootPath,
                "bin",
                Configuration,
                TargetFramework,
                PublishFolderName,
                $"{assemblyName}.dll"
            );

            if (!File.Exists(dllPath))
                throw new FileNotFoundException("Build not found", dllPath);

            var probeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetDirectoryName(dllPath)!, // publish
                Path.Combine(PluginRootPath, "bin", Configuration, TargetFramework), // output
                projectDirectory,
            };

            ResolveEventHandler handler = (sender, args) =>
            {
                var name = new AssemblyName(args.Name).Name;
                if (string.IsNullOrWhiteSpace(name)) return null;

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

            AppDomain.CurrentDomain.AssemblyResolve += handler;

            try
            {
                string sdkPath = Path.Combine(PluginRootPath, "bin", Configuration, TargetFramework, "Microsoft.Xrm.Sdk.dll");
                if (File.Exists(sdkPath))
                {
                    TryLoadAssemblyNoThrow(sdkPath);
                    probeDirs.Add(Path.GetDirectoryName(sdkPath)!);
                }

                Assembly pluginAssembly = Assembly.LoadFrom(dllPath);

                byte[] token = pluginAssembly.GetName().GetPublicKeyToken();
                if (token == null || token.Length == 0)
                    throw new Exception("Build not signed");

                string publicKeyToken = BitConverter.ToString(token).Replace("-", "").ToLowerInvariant();

                var classList = GetPluginTypesSafe(pluginAssembly)
                    .Where(t => t.IsClass && t.IsPublic)
                    .Where(t => ImplementsInterfaceByName(t, "Microsoft.Xrm.Sdk.IPlugin"))
                    .Select(t => t.FullName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Cast<string>()
                    .ToList();

                if (!classList.Any())
                    throw new Exception("Plugins not found");

                Directory.CreateDirectory(Path.GetDirectoryName(xmlPath)!);

                var pluginDoc = new XmlDocument();
                var xmlDecl = pluginDoc.CreateXmlDeclaration("1.0", "utf-8", null);
                pluginDoc.AppendChild(xmlDecl);

                XmlElement root = pluginDoc.CreateElement("PluginAssembly");
                root.SetAttribute("FullName", $"{assemblyName}, Version={fileVersion}, Culture=neutral, PublicKeyToken={publicKeyToken}");
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
                fileName.InnerText = $"/PluginAssemblies/{assemblyName}-{normalizedGuid.ToUpperInvariant()}/{assemblyName}.dll";
                root.AppendChild(fileName);

                XmlElement pluginTypes = pluginDoc.CreateElement("PluginTypes");
                root.AppendChild(pluginTypes);

                foreach (var className in classList)
                {
                    if (className == $"{csprojFileName}.PluginBase")
                        continue;

                    XmlElement pluginType = pluginDoc.CreateElement("PluginType");
                    pluginType.SetAttribute("AssemblyQualifiedName",
                        $"{className}, {assemblyName}, Version={fileVersion}, Culture=neutral, PublicKeyToken={publicKeyToken}");
                    pluginType.SetAttribute("PluginTypeId", Guid.NewGuid().ToString("D"));
                    pluginType.SetAttribute("Name", className);

                    XmlElement friendlyName = pluginDoc.CreateElement("FriendlyName");
                    friendlyName.InnerText = Guid.NewGuid().ToString("D");
                    pluginType.AppendChild(friendlyName);

                    pluginTypes.AppendChild(pluginType);
                }

                pluginDoc.Save(xmlPath);

                string destDllPath = Path.Combine(Path.GetDirectoryName(xmlPath)!, $"{assemblyName}.dll");
                File.Copy(dllPath, destDllPath, overwrite: true);

                var solutionDoc = new XmlDocument();
                XmlElement solutionRoot = solutionDoc.CreateElement("RootComponent");
                solutionRoot.SetAttribute("type", "91");
                solutionRoot.SetAttribute("id", $"{{{normalizedGuid}}}");
                solutionRoot.SetAttribute("schemaName", $"{assemblyName}, Version={fileVersion}, Culture=neutral, PublicKeyToken={publicKeyToken}");
                solutionRoot.SetAttribute("behavior", "0");

                solutionDoc.AppendChild(solutionRoot);

                string tempDir = Path.Combine(repoRoot, ".template.temp");
                Directory.CreateDirectory(tempDir);

                solutionDoc.Save(Path.Combine(tempDir, "RootComponent.xml"));

                Log.LogMessage(MessageImportance.High, $"PluginAssembly data xml generated: {xmlPath}");
                return true;
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= handler;
            }
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: true, showDetail: true, file: null);
            return false;
        }
    }

    private static void TryLoadAssemblyNoThrow(string path)
    {
        try { Assembly.LoadFrom(path); } catch { /* ignore */ }
    }

    private static string NormalizeGuid(string guidText)
    {
        if (string.IsNullOrWhiteSpace(guidText))
            throw new ArgumentException("PluginAssemblyId is empty");

        var trimmed = guidText.Trim().Trim('{', '}');

        if (!Guid.TryParse(trimmed, out var g))
            throw new ArgumentException($"PluginAssemblyId is not a valid GUID: {guidText}");

        return g.ToString("D");
    }

    private static (string AssemblyName, string FileVersion) ReadProjectMetadata(string csprojPath, string fallbackAssemblyName)
    {
        var xdoc = XDocument.Load(csprojPath);

        string? assemblyName =
            xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "AssemblyName")?.Value?.Trim();

        string? fileVersion =
            xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "FileVersion")?.Value?.Trim();

        if (string.IsNullOrWhiteSpace(assemblyName))
            assemblyName = fallbackAssemblyName;

        if (string.IsNullOrWhiteSpace(fileVersion))
            fileVersion = "1.0.0.0";

        return (assemblyName, fileVersion);
    }

    private static IEnumerable<Type> GetPluginTypesSafe(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException rtle)
        {
            var loaderErrors = rtle.LoaderExceptions?
                .Where(e => e != null)
                .Select(e => e!.Message)
                .Distinct()
                .ToArray() ?? Array.Empty<string>();

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
}
