using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using TALXIS.DevKit.Build.Dataverse.Tasks.Tests.Support;
using Xunit;

namespace TALXIS.DevKit.Build.Dataverse.Tasks.Tests
{
    public class EnsurePluginAssemblyDataXmlTests : IDisposable
    {
        private const string AssemblyName = "Fixture.Plugins";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "txc-plugin-assembly-" + Guid.NewGuid().ToString("N"));
        private readonly ImmutableArray<byte> _publicKey = CreatePublicKey();
        private FakeBuildEngine _engine = new FakeBuildEngine();

        public EnsurePluginAssemblyDataXmlTests()
        {
            Directory.CreateDirectory(PluginRoot);
            Directory.CreateDirectory(Path.Combine(SolutionRoot, "Other"));
            File.WriteAllText(Path.Combine(PluginRoot, AssemblyName + ".csproj"), "<Project><PropertyGroup><AssemblyName>" + AssemblyName + "</AssemblyName></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(SolutionRoot, "Other", "Solution.xml"), "<ImportExportXml><SolutionManifest><RootComponents /></SolutionManifest></ImportExportXml>");
        }

        private string PluginRoot => Path.Combine(_root, "Plugin");
        private string SolutionRoot => Path.Combine(_root, "Solution");
        private string DllPath => Path.Combine(PluginRoot, "bin", AssemblyName + ".dll");
        private string DataXmlPath => Path.Combine(SolutionRoot, "PluginAssemblies", AssemblyName + ".dll.data.xml");

        public void Dispose()
        {
            try { Directory.Delete(_root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        [Fact]
        public void No_warning_when_every_registered_type_is_built()
        {
            Build("A", "B");
            Build("A", "B");

            Assert.Empty(_engine.Warnings);
            Assert.Equal(new[] { "Fixture.A", "Fixture.B" }, RegisteredTypes());
        }

        [Fact]
        public void Deleted_class_is_reported_and_kept()
        {
            Build("A", "B");
            Build("A");

            string warning = Assert.Single(_engine.Warnings);
            Assert.Contains("Fixture.B", warning);
            Assert.Contains(DataXmlPath, warning);
            Assert.Equal(new[] { "Fixture.A", "Fixture.B" }, RegisteredTypes());
        }

        [Fact]
        public void Renamed_class_reports_the_old_name_and_registers_the_new_one()
        {
            Build("A", "B");
            Build("A", "C");

            string warning = Assert.Single(_engine.Warnings);
            Assert.Contains("Fixture.B", warning);
            Assert.Equal(new[] { "Fixture.A", "Fixture.B", "Fixture.C" }, RegisteredTypes());
        }

        [Fact]
        public void Each_missing_type_gets_its_own_warning()
        {
            Build("A", "B", "C");
            Build("A");

            Assert.Equal(2, _engine.Warnings.Count);
            Assert.Contains(_engine.Warnings, w => w.Contains("Fixture.B"));
            Assert.Contains(_engine.Warnings, w => w.Contains("Fixture.C"));
        }

        private void Build(params string[] pluginClasses)
        {
            Compile(pluginClasses);
            _engine = new FakeBuildEngine();

            var task = new EnsurePluginAssemblyDataXml
            {
                BuildEngine = _engine,
                PluginRootPath = PluginRoot,
                PluginAssemblyId = Guid.NewGuid().ToString("D"),
                RepositoryRoot = SolutionRoot,
                PluginDllPath = DllPath,
            };

            Assert.True(task.Execute(), string.Join(Environment.NewLine, _engine.Errors));
        }

        private void Compile(IEnumerable<string> pluginClasses)
        {
            string source = "using System; namespace Microsoft.Xrm.Sdk { public interface IPlugin { void Execute(IServiceProvider serviceProvider); } } namespace Fixture { " +
                string.Join(" ", pluginClasses.Select(name => "public class " + name + " : Microsoft.Xrm.Sdk.IPlugin { public void Execute(IServiceProvider serviceProvider) { } }")) + " }";

            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));

            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithCryptoPublicKey(_publicKey)
                .WithPublicSign(true);

            var compilation = CSharpCompilation.Create(AssemblyName, new[] { CSharpSyntaxTree.ParseText(source) }, references, options);

            Directory.CreateDirectory(Path.GetDirectoryName(DllPath));
            var result = compilation.Emit(DllPath);
            if (!result.Success)
                throw new InvalidOperationException("Fixture does not compile:" + Environment.NewLine +
                    string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        private List<string> RegisteredTypes() => XDocument.Load(DataXmlPath).Descendants("PluginType")
            .Select(e => (string)e.Attribute("Name"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Strong-name public key blob (SNK header + CAPI PUBLICKEYBLOB), built by hand so it works off Windows too.
        private static ImmutableArray<byte> CreatePublicKey()
        {
            RSAParameters key;
            using (var rsa = RSA.Create(1024))
                key = rsa.ExportParameters(false);

            byte[] modulus = key.Modulus.Reverse().ToArray();
            byte[] exponent = new byte[4];
            Array.Copy(key.Exponent.Reverse().ToArray(), exponent, key.Exponent.Length);

            var capi = new List<byte> { 0x06, 0x02, 0x00, 0x00 };
            capi.AddRange(BitConverter.GetBytes(0x2400));
            capi.AddRange(new byte[] { (byte)'R', (byte)'S', (byte)'A', (byte)'1' });
            capi.AddRange(BitConverter.GetBytes(modulus.Length * 8));
            capi.AddRange(exponent);
            capi.AddRange(modulus);

            var blob = new List<byte>();
            blob.AddRange(BitConverter.GetBytes(0x2400));
            blob.AddRange(BitConverter.GetBytes(0x8004));
            blob.AddRange(BitConverter.GetBytes(capi.Count));
            blob.AddRange(capi);
            return blob.ToImmutableArray();
        }
    }
}
