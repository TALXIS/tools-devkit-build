using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Build.Utilities;
using TALXIS.DevKit.Build.Dataverse.Tasks.Tests.Support;
using Xunit;

namespace TALXIS.DevKit.Build.Dataverse.Tasks.Tests
{
    /// <summary>
    /// The import orders of all source packages end up in the merged data_schema.xml without breaking any of them.
    /// </summary>
    public class MergeCmtDataSchemaXmlTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "cmtschemamerge-" + Guid.NewGuid().ToString("N"));
        private readonly FakeBuildEngine _engine = new FakeBuildEngine();

        public void Dispose() => Directory.Delete(_root, true);

        [Fact]
        public void Merged_schema_keeps_the_import_order_of_every_package()
        {
            var a = WriteSchema("a", new[] { "contact", "annotation" });
            var b = WriteSchema("b", new[] { "account", "contact" });
            var task = NewTask(a, b);

            Assert.True(task.Execute(), string.Join(Environment.NewLine, _engine.Errors));

            Assert.Equal(new[] { "account", "contact", "annotation" }, ImportOrder(task));
        }

        [Fact]
        public void Import_order_left_open_by_the_packages_follows_the_lookups()
        {
            var products = WriteSchema("products", new[] { "product", "pricelistitem" }, ("product", "currency"));
            var main = WriteSchema("main", new[] { "currency", "pricelist" });
            var task = NewTask(products, main);

            Assert.True(task.Execute(), string.Join(Environment.NewLine, _engine.Errors));

            Assert.Equal(new[] { "currency", "product", "pricelistitem", "pricelist" }, ImportOrder(task));
            Assert.Empty(_engine.Warnings);
        }

        [Fact]
        public void Contradicting_import_orders_fail_the_merge()
        {
            var a = WriteSchema("a", new[] { "account", "contact" });
            var b = WriteSchema("b", new[] { "contact", "account" });
            var task = NewTask(a, b);

            Assert.False(task.Execute());

            var error = Assert.Single(_engine.Errors);
            Assert.Contains("'account' before 'contact'", error);
            Assert.Contains("'contact' before 'account'", error);
            Assert.False(File.Exists(Path.Combine(_root, "out", "data_schema.xml")));
        }

        private MergeCmtDataSchemaXml NewTask(params string[] files) => new MergeCmtDataSchemaXml
        {
            BuildEngine = _engine,
            DataSchemaFiles = files.Select(f => new TaskItem(f)).ToArray(),
            OutputDirectory = Path.Combine(_root, "out"),
        };

        private static string[] ImportOrder(MergeCmtDataSchemaXml task) =>
            XDocument.Load(task.OutputDataSchemaXml).Root.Element("entityImportOrder").Elements("entityName").Select(e => e.Value).ToArray();

        private string WriteSchema(string package, string[] order, params (string From, string To)[] lookups)
        {
            var schema = new XElement("entities",
                new XAttribute("dateMode", "absolute"),
                order.Select(name => new XElement("entity",
                    new XAttribute("name", name),
                    new XAttribute("displayname", name),
                    new XAttribute("primaryidfield", name + "id"),
                    new XAttribute("primarynamefield", "name"),
                    new XAttribute("disableplugins", "false"),
                    new XElement("fields",
                        new XElement("field", new XAttribute("displayname", "Id"), new XAttribute("name", name + "id"), new XAttribute("type", "guid"), new XAttribute("primaryKey", "true")),
                        new XElement("field", new XAttribute("displayname", "Name"), new XAttribute("name", "name"), new XAttribute("type", "string")),
                        lookups.Where(l => l.From == name).Select(l => new XElement("field",
                            new XAttribute("displayname", l.To), new XAttribute("name", l.To + "id"), new XAttribute("type", "entityreference"), new XAttribute("lookupType", l.To)))))),
                new XElement("entityImportOrder", order.Select(name => new XElement("entityName", name))));

            var path = Path.Combine(_root, package, "data_schema.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            schema.Save(path);
            return path;
        }
    }
}
