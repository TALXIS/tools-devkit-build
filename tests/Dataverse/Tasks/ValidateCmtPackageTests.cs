using System;
using System.IO;
using Microsoft.Build.Utilities;
using TALXIS.DevKit.Build.Dataverse.Tasks.Tests.Support;
using Xunit;

namespace TALXIS.DevKit.Build.Dataverse.Tasks.Tests
{
    /// <summary>
    /// CMT accepts only true|false|1|0 in boolean attributes, while the reader turns any other token into false.
    /// </summary>
    public class ValidateCmtPackageTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "cmtvalidate-" + Guid.NewGuid().ToString("N"));
        private readonly FakeBuildEngine _engine = new FakeBuildEngine();

        public void Dispose() => Directory.Delete(_root, true);

        [Fact]
        public void Valid_package_passes()
        {
            WritePackage(disablePlugins: "false", updateCompare: "true");

            Assert.True(NewTask().Execute(), string.Join(Environment.NewLine, _engine.Errors));
        }

        [Theory]
        [InlineData("yes")]
        [InlineData("True")]
        public void Disableplugins_value_that_is_not_a_CMT_boolean_fails(string value)
        {
            WritePackage(disablePlugins: value, updateCompare: "true");

            Assert.False(NewTask().Execute());

            var error = Assert.Single(_engine.Errors);
            Assert.Contains("'disableplugins'", error);
            Assert.Contains($"'{value}'", error);
        }

        [Fact]
        public void UpdateCompare_value_that_is_not_a_CMT_boolean_fails()
        {
            WritePackage(disablePlugins: "false", updateCompare: "yes");

            Assert.False(NewTask().Execute());

            Assert.Contains("'updateCompare'", Assert.Single(_engine.Errors));
        }

        [Fact]
        public void Malformed_xml_is_reported_once()
        {
            WritePackage(disablePlugins: "false", updateCompare: "true");
            File.WriteAllText(Path.Combine(_root, "data.xml"), "<entities><entity name=\"account\"></entitie>");

            Assert.False(NewTask().Execute());

            Assert.Single(_engine.Errors);
        }

        private ValidateCmtPackage NewTask() => new ValidateCmtPackage
        {
            BuildEngine = _engine,
            PackageDirectories = new[] { new TaskItem(_root) },
        };

        private void WritePackage(string disablePlugins, string updateCompare)
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, "data_schema.xml"),
$@"<entities dateMode=""absolute"">
  <entity name=""account"" displayname=""Account"" primaryidfield=""accountid"" primarynamefield=""name"" disableplugins=""{disablePlugins}"">
    <fields>
      <field displayname=""Account"" name=""accountid"" type=""guid"" primaryKey=""true"" />
      <field displayname=""Name"" name=""name"" type=""string"" updateCompare=""{updateCompare}"" />
    </fields>
  </entity>
</entities>");
            File.WriteAllText(Path.Combine(_root, "data.xml"),
@"<?xml version=""1.0"" encoding=""utf-8""?>
<entities timestamp=""2026-10-26T00:00:00Z"">
  <entity name=""account"" displayname=""Account"">
    <records>
      <record id=""c0000000-0000-4000-8000-000000000001"">
        <field name=""accountid"" value=""c0000000-0000-4000-8000-000000000001"" />
        <field name=""name"" value=""Contoso"" />
      </record>
    </records>
  </entity>
</entities>");
        }
    }
}
