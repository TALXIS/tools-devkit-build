using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TALXIS.DevKit.Build.Dataverse.Tasks.Tests.Support;
using Xunit;

namespace TALXIS.DevKit.Build.Dataverse.Tasks.Tests
{
    public class EnsureAllCustomizationsNodesTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ensure-customizations-" + Guid.NewGuid().ToString("N"));
        private readonly FakeBuildEngine _engine = new FakeBuildEngine();

        public EnsureAllCustomizationsNodesTests()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Other"));
            File.WriteAllText(CustomizationsPath,
                "<ImportExportXml xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><Languages><Language>1033</Language></Languages></ImportExportXml>");
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private string CustomizationsPath => Path.Combine(_root, "Other", "Customizations.xml");

        private XElement Run()
        {
            var task = new EnsureAllCustomizationsNodes
            {
                BuildEngine = _engine,
                CustomizationsXmlFile = CustomizationsPath,
                MetadataWorkingDirectory = _root
            };

            Assert.True(task.Execute(), string.Join(Environment.NewLine, _engine.Errors));
            return XDocument.Load(CustomizationsPath).Root;
        }

        [Fact]
        public void Application_ribbon_file_gets_a_RibbonDiffXml_placeholder()
        {
            File.WriteAllText(Path.Combine(_root, "Other", "RibbonCustomization.xml"), "<RibbonDiffXml />");

            var root = Run();

            Assert.Single(root.Elements("RibbonDiffXml"));
        }

        [Fact]
        public void Existing_RibbonDiffXml_placeholder_is_not_duplicated()
        {
            File.WriteAllText(Path.Combine(_root, "Other", "RibbonCustomization.xml"), "<RibbonDiffXml />");
            var doc = XDocument.Load(CustomizationsPath);
            doc.Root.AddFirst(new XElement("RibbonDiffXml"));
            doc.Save(CustomizationsPath);

            var root = Run();

            Assert.Single(root.Elements("RibbonDiffXml"));
        }

        [Fact]
        public void No_application_ribbon_file_means_no_placeholder()
        {
            var root = Run();

            Assert.Empty(root.Elements("RibbonDiffXml"));
        }
    }
}
