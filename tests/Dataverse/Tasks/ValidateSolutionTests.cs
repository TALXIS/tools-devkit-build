using System;
using System.IO;
using TALXIS.DevKit.Build.Dataverse.Tasks.Tests.Support;
using Xunit;

namespace TALXIS.DevKit.Build.Dataverse.Tasks.Tests
{
    public class ValidateSolutionTests : IDisposable
    {
        private const string ControlName = "fx_Fx.SampleControl";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "txc-validate-solution-" + Guid.NewGuid().ToString("N"));
        private readonly FakeBuildEngine _engine = new FakeBuildEngine();

        public ValidateSolutionTests()
        {
            Write("Other/Solution.xml", @"<?xml version=""1.0"" encoding=""utf-8""?>
<ImportExportXml version=""9.2"" SolutionPackageVersion=""9.2"" languagecode=""1033"">
  <SolutionManifest>
    <UniqueName>Declarations</UniqueName>
    <LocalizedNames>
      <LocalizedName description=""Declarations"" languagecode=""1033"" />
    </LocalizedNames>
    <Descriptions></Descriptions>
    <Version>1.0.0.0</Version>
    <Managed>0</Managed>
    <Publisher>
      <UniqueName>Pub</UniqueName>
      <LocalizedNames>
        <LocalizedName description=""Pub"" languagecode=""1033"" />
      </LocalizedNames>
      <Descriptions>
        <Description description="""" languagecode=""1033"" />
      </Descriptions>
      <EMailAddress />
      <SupportingWebsiteUrl />
      <CustomizationPrefix>fx</CustomizationPrefix>
      <CustomizationOptionValuePrefix>10000</CustomizationOptionValuePrefix>
      <Addresses>
        <Address>
          <AddressNumber>1</AddressNumber>
          <AddressTypeCode>1</AddressTypeCode>
        </Address>
      </Addresses>
    </Publisher>
    <RootComponents>
      <RootComponent type=""66"" schemaName=""" + ControlName + @""" behavior=""0"" />
    </RootComponents>
    <MissingDependencies />
  </SolutionManifest>
</ImportExportXml>");

            Write("Controls/" + ControlName + "/ControlManifest.xml", @"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest>
  <control namespace=""Fx"" constructor=""SampleControl"" version=""0.0.1"" display-name-key=""SampleControl"" description-key=""SampleControl description"" control-type=""standard"">
    <property name=""sampleProperty"" display-name-key=""Property_Display_Key"" description-key=""Property_Desc_Key"" of-type=""SingleLine.Text"" usage=""bound"" required=""true"" />
    <resources>
      <code path=""bundle.js"" order=""1"" />
    </resources>
  </control>
</manifest>");
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        private void Write(string relativePath, string content)
        {
            string path = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        private bool Run() => new ValidateSolution { BuildEngine = _engine, ProjectDir = _root }.Execute();

        [Fact]
        public void Accepts_the_data_file_written_for_a_referenced_pcf_control()
        {
            // The shape Microsoft's ProcessCdsProjectReferencesOutputs writes for a ProjectReference to a Pcf project.
            Write("Controls/" + ControlName + "/ControlManifest.xml.data.xml", @"<?xml version=""1.0"" encoding=""utf-8""?>
<CustomControl>
  <Name>" + ControlName + @"</Name>
  <FileName>/Controls/" + ControlName + @"/ControlManifest.xml</FileName>
</CustomControl>");

            Assert.True(Run(), string.Join(Environment.NewLine, _engine.Errors));
            Assert.Empty(_engine.Errors);
        }

        [Fact]
        public void Rejects_a_pcf_data_file_without_a_name()
        {
            Write("Controls/" + ControlName + "/ControlManifest.xml.data.xml", @"<?xml version=""1.0"" encoding=""utf-8""?>
<CustomControl>
  <FileName>/Controls/" + ControlName + @"/ControlManifest.xml</FileName>
</CustomControl>");

            Assert.False(Run());
            Assert.NotEmpty(_engine.Errors);
        }
    }
}
