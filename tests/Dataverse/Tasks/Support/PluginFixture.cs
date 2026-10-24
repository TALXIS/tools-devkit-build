using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TALXIS.DevKit.Build.Dataverse.Tasks.Tests.Support
{
    /// <summary>
    /// A solution source folder plus a compiled plugin assembly, the two inputs EnsureSdkMessageProcessingStepXml works on.
    /// The assembly is built from the attribute sources a Plugin project ships, at the C# version net462 projects default to.
    /// </summary>
    internal sealed class PluginFixture : IDisposable
    {
        public const string AssemblyName = "Fixture.Plugins";
        public const string AssemblyFullName = AssemblyName + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        private const string Prelude = "using System; using TALXIS.DevKit.Build.Dataverse.Plugin; namespace Microsoft.Xrm.Sdk { public interface IPlugin { void Execute(IServiceProvider serviceProvider); } }";

        private readonly string _binDirectory;

        public PluginFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "txc-plugin-steps-" + Guid.NewGuid().ToString("N"));
            _binDirectory = Path.Combine(Root, "bin");
            Directory.CreateDirectory(_binDirectory);
            Directory.CreateDirectory(Path.Combine(Root, "Other"));
            Directory.CreateDirectory(Path.Combine(Root, "PluginAssemblies"));
            File.WriteAllText(SolutionXmlPath, "<ImportExportXml><SolutionManifest><RootComponents /></SolutionManifest></ImportExportXml>");
        }

        public string Root { get; }
        public string PluginDllPath => Path.Combine(_binDirectory, AssemblyName + ".dll");
        public string SolutionXmlPath => Path.Combine(Root, "Other", "Solution.xml");
        public string StepsDirectory => Path.Combine(Root, "SdkMessageProcessingSteps");

        public IEnumerable<string> StepFiles => Directory.Exists(StepsDirectory) ? Directory.GetFiles(StepsDirectory, "*.xml") : new string[0];

        /// <summary>Compiles the given class declarations, replacing the previous build, and refreshes the assembly's .data.xml.</summary>
        public void Compile(params string[] classDeclarations)
        {
            string source = Prelude + " namespace Fixture { " + string.Join(" ", classDeclarations) + " }";
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
            var trees = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Registration"), "*.cs")
                .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path))
                .Concat(new[] { CSharpSyntaxTree.ParseText(source, parseOptions) });

            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));

            var compilation = CSharpCompilation.Create(AssemblyName, trees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var result = compilation.Emit(PluginDllPath);
            if (!result.Success)
                throw new InvalidOperationException("Fixture does not compile:" + Environment.NewLine +
                    string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

            WritePluginAssemblyDataXml(classDeclarations);
        }

        public static string TypeId(string fullTypeName) => DeterministicId("plugintype|" + fullTypeName);

        public EnsureSdkMessageProcessingStepXml NewTask(FakeBuildEngine engine) => new EnsureSdkMessageProcessingStepXml
        {
            BuildEngine = engine,
            PluginDllPath = PluginDllPath,
            RepositoryRoot = Root,
        };

        public XElement ReadStep(string id) => XDocument.Load(Path.Combine(StepsDirectory, "{" + id + "}.xml")).Root;

        public XElement OnlyStep()
        {
            string[] files = StepFiles.ToArray();
            if (files.Length != 1) throw new InvalidOperationException("Expected one step file, found " + files.Length);
            return XDocument.Load(files[0]).Root;
        }

        public List<string> StepRootComponentIds() => XDocument.Load(SolutionXmlPath).Descendants("RootComponent")
            .Where(e => (string)e.Attribute("type") == "92")
            .Select(e => ((string)e.Attribute("id")).Trim('{', '}'))
            .ToList();

        /// <summary>A step as pp-plugin-assembly-step writes it: random id, file named after it, type 92 root in Solution.xml.</summary>
        public string AddHandWrittenStep(string pluginTypeName, string sdkMessageId, string primaryEntity, int stage)
        {
            string id = Guid.NewGuid().ToString("D");
            Directory.CreateDirectory(StepsDirectory);
            File.WriteAllText(Path.Combine(StepsDirectory, "{" + id + "}.xml"),
                "<SdkMessageProcessingStep Name=\"hand\" SdkMessageProcessingStepId=\"{" + id + "}\">" +
                "<SdkMessageId>" + sdkMessageId + "</SdkMessageId>" +
                "<PluginTypeName>" + pluginTypeName + ", " + AssemblyFullName + "</PluginTypeName>" +
                "<PrimaryEntity>" + primaryEntity + "</PrimaryEntity>" +
                "<Stage>" + stage + "</Stage></SdkMessageProcessingStep>");

            var doc = XDocument.Load(SolutionXmlPath);
            doc.Descendants("RootComponents").Single().Add(new XElement("RootComponent",
                new XAttribute("type", "92"), new XAttribute("id", "{" + id + "}"), new XAttribute("behavior", "0")));
            doc.Save(SolutionXmlPath);
            return id;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void WritePluginAssemblyDataXml(string[] classDeclarations)
        {
            var types = classDeclarations
                .SelectMany(declaration => Regex.Matches(declaration, @"\bclass\s+(\w+)\s*:\s*Microsoft\.Xrm\.Sdk\.IPlugin").Cast<Match>())
                .Select(match => "Fixture." + match.Groups[1].Value);

            var root = new XElement("PluginAssembly", new XAttribute("FullName", AssemblyFullName),
                new XElement("PluginTypes", types.Select(type => new XElement("PluginType",
                    new XAttribute("PluginTypeId", TypeId(type)),
                    new XAttribute("AssemblyQualifiedName", type + ", " + AssemblyFullName),
                    new XAttribute("Name", type)))));

            root.Save(Path.Combine(Root, "PluginAssemblies", AssemblyName + ".dll.data.xml"));
        }

        private static string DeterministicId(string seed)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
                return new Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(seed))).ToString("D");
        }
    }
}
