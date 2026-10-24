using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TALXIS.DevKit.Build.Dataverse.Tasks.Tests.Support;
using Xunit;

namespace TALXIS.DevKit.Build.Dataverse.Tasks.Tests
{
    public class GeneratePluginStepsTests : IDisposable
    {
        private const string CreateId = "9ebdbb1b-ea3e-db11-86a7-000a3a5473e8";
        private const string UpdateId = "20bebb1b-ea3e-db11-86a7-000a3a5473e8";
        private const string DeleteId = "a1bdbb1b-ea3e-db11-86a7-000a3a5473e8";

        private readonly PluginFixture _fx = new PluginFixture();
        private readonly FakeBuildEngine _engine = new FakeBuildEngine();

        public void Dispose() => _fx.Dispose();

        private static string Plugin(string attributes, string name = "P")
            => attributes + " public class " + name + " : Microsoft.Xrm.Sdk.IPlugin { public void Execute(IServiceProvider serviceProvider) { } }";

        private bool Run() => _fx.NewTask(_engine).Execute();

        private XElement Generate(params string[] classes)
        {
            _fx.Compile(classes);
            Assert.True(Run(), string.Join(Environment.NewLine, _engine.Errors));
            return _fx.OnlyStep();
        }

        private static string Value(XElement step, string element) => (string)step.Element(element);

        [Fact]
        public void Writes_declared_values_and_a_root_component()
        {
            var step = Generate(Plugin("[PluginStep(\"Update\", \"account\", PluginStage.PostOperation, Name = \"NameChanged\", FilteringAttributes = new[] { \"Name\", \"telephone1\" }, Rank = 3, Description = \"d\")]"));

            Assert.Equal("NameChanged", (string)step.Attribute("Name"));
            Assert.Equal(UpdateId, Value(step, "SdkMessageId"));
            Assert.Equal("account", Value(step, "PrimaryEntity"));
            Assert.Equal("40", Value(step, "Stage"));
            Assert.Equal("0", Value(step, "Mode"));
            Assert.Equal("3", Value(step, "Rank"));
            Assert.Equal("name,telephone1", Value(step, "FilteringAttributes"));
            Assert.Equal("d", Value(step, "Description"));
            Assert.Equal("Fixture.P, " + PluginFixture.AssemblyFullName, Value(step, "PluginTypeName"));
            Assert.Equal(PluginFixture.TypeId("Fixture.P"), Value(step, "PluginTypeId"));
            Assert.Equal("4602", Value(step, "EventHandlerTypeCode"));
            Assert.Equal("0", Value(step, "SupportedDeployment"));

            string id = ((string)step.Attribute("SdkMessageProcessingStepId")).Trim('{', '}');
            Assert.Equal(new[] { id }, _fx.StepRootComponentIds());
        }

        [Theory]
        [InlineData("Create", "9ebdbb1b-ea3e-db11-86a7-000a3a5473e8")]
        [InlineData("Update", "20bebb1b-ea3e-db11-86a7-000a3a5473e8")]
        [InlineData("Delete", "a1bdbb1b-ea3e-db11-86a7-000a3a5473e8")]
        [InlineData("SetState", "1cbebb1b-ea3e-db11-86a7-000a3a5473e8")]
        [InlineData("SetStateDynamicEntity", "1dbebb1b-ea3e-db11-86a7-000a3a5473e8")]
        [InlineData("Assign", "8cbdbb1b-ea3e-db11-86a7-000a3a5473e8")]
        [InlineData("Retrieve", "f2bdbb1b-ea3e-db11-86a7-000a3a5473e8")]
        [InlineData("RetrieveMultiple", "03bebb1b-ea3e-db11-86a7-000a3a5473e8")]
        [InlineData("Associate", "16e7c888-3b24-448b-b7da-ebfe9b681762")]
        [InlineData("Disassociate", "bdfc191d-fc56-493a-a6b1-cb0c47a22ecc")]
        [InlineData("RetrieveExchangeRate", "126075c5-c77f-44fd-bb7d-3457a9168cee")]
        [InlineData("update", "20bebb1b-ea3e-db11-86a7-000a3a5473e8")]
        public void Built_in_messages_resolve_to_their_system_ids(string message, string expectedId)
        {
            var step = Generate(Plugin("[PluginStep(\"" + message + "\", \"account\", PluginStage.PreOperation)]"));

            Assert.Equal(expectedId, Value(step, "SdkMessageId"));
        }

        [Fact]
        public void Message_without_primary_entity_keeps_its_stage_and_omits_the_element()
        {
            var step = Generate(Plugin("[PluginStep(\"Associate\", PluginStage.PreOperation)]"));

            Assert.Equal("16e7c888-3b24-448b-b7da-ebfe9b681762", Value(step, "SdkMessageId"));
            Assert.Equal("20", Value(step, "Stage"));
            Assert.Null(step.Element("PrimaryEntity"));
        }

        [Fact]
        public void Message_with_primary_entity_still_reads_entity_and_stage_by_position()
        {
            var step = Generate(Plugin("[PluginStep(\"Create\", \"contact\", PluginStage.PreValidation)]"));

            Assert.Equal("contact", Value(step, "PrimaryEntity"));
            Assert.Equal("10", Value(step, "Stage"));
        }

        [Fact]
        public void Invocation_source_defaults_to_child_and_can_be_parent()
        {
            var child = Generate(Plugin("[PluginStep(\"Create\", \"account\", PluginStage.PostOperation)]"));
            Assert.Equal("1", Value(child, "InvocationSource"));

            foreach (string file in _fx.StepFiles) File.Delete(file);
            var parent = Generate(Plugin("[PluginStep(\"Create\", \"account\", PluginStage.PostOperation, InvocationSource = PluginInvocationSource.Parent)]"));
            Assert.Equal("0", Value(parent, "InvocationSource"));
        }

        [Fact]
        public void Run_as_system_writes_the_impersonation_element_where_exports_put_it()
        {
            var step = Generate(Plugin("[PluginStep(\"Create\", \"account\", PluginStage.PostOperation, RunAsSystem = true)]"));

            Assert.Equal("SYSTEM", Value(step, "ImpersonatingUserIdName"));
            string[] order = step.Elements().Select(e => e.Name.LocalName).ToArray();
            Assert.Equal("FilteringAttributes", order[Array.IndexOf(order, "ImpersonatingUserIdName") - 1]);
            Assert.Equal("InvocationSource", order[Array.IndexOf(order, "ImpersonatingUserIdName") + 1]);
        }

        [Fact]
        public void Impersonation_element_is_absent_by_default()
        {
            var step = Generate(Plugin("[PluginStep(\"Create\", \"account\", PluginStage.PostOperation)]"));

            Assert.Null(step.Element("ImpersonatingUserIdName"));
        }

        [Fact]
        public void Asynchronous_step_with_auto_delete_and_configuration()
        {
            var step = Generate(Plugin("[PluginStep(\"Create\", \"account\", PluginStage.PostOperation, Mode = PluginMode.Asynchronous, AsyncAutoDelete = true, Configuration = \"{\\\"k\\\":1}\")]"));

            Assert.Equal("1", Value(step, "Mode"));
            Assert.Equal("1", Value(step, "AsyncAutoDelete"));
            Assert.Equal("{\"k\":1}", Value(step, "Configuration"));
        }

        [Theory]
        [InlineData("{624f8970-2162-ea11-a811-000d3ab54878}")]
        [InlineData("624F8970-2162-EA11-A811-000D3AB54878")]
        public void Custom_message_uses_the_supplied_id_in_any_guid_format(string messageId)
        {
            var step = Generate(Plugin("[PluginStep(\"talxis_winopportunity\", \"talxis_opportunityheader\", PluginStage.PostOperation, MessageId = \"" + messageId + "\", Configuration = \"talxis_signopportunity\")]"));

            Assert.Equal("624f8970-2162-ea11-a811-000d3ab54878", Value(step, "SdkMessageId"));
            Assert.Equal("talxis_signopportunity", Value(step, "Configuration"));
        }

        [Fact]
        public void Unbound_custom_action_needs_no_primary_entity()
        {
            var step = Generate(Plugin("[PluginStep(\"talxis_diffcheckdatafeed\", PluginStage.PostOperation, MessageId = \"fdd14d4e-239b-ed11-aad1-000d3aad8d08\")]"));

            Assert.Equal("fdd14d4e-239b-ed11-aad1-000d3aad8d08", Value(step, "SdkMessageId"));
            Assert.Null(step.Element("PrimaryEntity"));
        }

        [Fact]
        public void Explicit_id_adopts_an_existing_step()
        {
            var step = Generate(Plugin("[PluginStep(\"Update\", \"account\", PluginStage.PostOperation, Id = \"{11111111-2222-3333-4444-555555555555}\")]"));

            Assert.Equal("{11111111-2222-3333-4444-555555555555}", (string)step.Attribute("SdkMessageProcessingStepId"));
            Assert.Equal(new[] { "11111111-2222-3333-4444-555555555555" }, _fx.StepRootComponentIds());
            Assert.True(File.Exists(Path.Combine(_fx.StepsDirectory, "{11111111-2222-3333-4444-555555555555}.xml")));
        }

        [Fact]
        public void Adopting_a_step_whose_file_name_uses_an_uppercase_guid_leaves_one_file()
        {
            const string id = "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE";
            Directory.CreateDirectory(_fx.StepsDirectory);
            File.WriteAllText(Path.Combine(_fx.StepsDirectory, "{" + id + "}.xml"),
                "<SdkMessageProcessingStep SdkMessageProcessingStepId=\"{" + id + "}\"><SdkMessageId>" + UpdateId + "</SdkMessageId><PluginTypeName>Fixture.P, " + PluginFixture.AssemblyFullName + "</PluginTypeName><PrimaryEntity>account</PrimaryEntity><Stage>40</Stage></SdkMessageProcessingStep>");
            _fx.Compile(Plugin("[PluginStep(\"Update\", \"account\", PluginStage.PostOperation, Id = \"" + id + "\")]"));

            Assert.True(Run(), string.Join(Environment.NewLine, _engine.Errors));

            Assert.Single(_fx.StepFiles);
        }

        [Fact]
        public void Generated_ids_are_frozen()
        {
            // Changing how ids are derived re-keys every generated step in every customer solution.
            var step = Generate(Plugin("[PluginStep(\"Update\", \"account\", PluginStage.PostOperation, Name = \"n\")]\n[PluginStepImage(PluginImageType.PreImage, \"PreImage\", \"name\")]"));

            Assert.Equal("{" + GoldenStepId + "}", (string)step.Attribute("SdkMessageProcessingStepId"));
            Assert.Equal("{" + GoldenImageId + "}", (string)step.Descendants("SdkMessageProcessingStepImageId").Single());
        }

        private const string GoldenStepId = "82cfdb2e-0642-52bd-7240-e372026524ed";
        private const string GoldenImageId = "02750afe-c48f-dcab-9fc6-68c4ffad714d";

        [Fact]
        public void Images_cover_pre_post_both_and_all_columns()
        {
            var step = Generate(Plugin(
                "[PluginStep(\"Update\", \"account\", PluginStage.PostOperation, Name = \"u\")]\n" +
                "[PluginStepImage(PluginImageType.PreImage, \"Pre\", \"name\", \"telephone1\")]\n" +
                "[PluginStepImage(PluginImageType.PostImage, \"Post\")]\n" +
                "[PluginStepImage(PluginImageType.Both, \"Both\", EntityAlias = \"Alias\")]"));

            var images = step.Descendants("SdkMessageProcessingStepImage").ToDictionary(i => (string)i.Attribute("Name"));
            Assert.Equal("0", Value(images["Pre"], "ImageType"));
            Assert.Equal("name,telephone1", Value(images["Pre"], "Attributes"));
            Assert.Equal("1", Value(images["Post"], "ImageType"));
            Assert.Equal("", Value(images["Post"], "Attributes"));
            Assert.Equal("2", Value(images["Both"], "ImageType"));
            Assert.Equal("Alias", Value(images["Both"], "EntityAlias"));
            Assert.Equal("Pre", Value(images["Pre"], "EntityAlias"));
            Assert.Equal("Target", Value(images["Pre"], "MessagePropertyName"));
        }

        [Theory]
        [InlineData("Create", "PostOperation", "PostImage", "Id")]
        [InlineData("SetState", "PostOperation", "PreImage", "EntityMoniker")]
        [InlineData("Update", "PreOperation", "PreImage", "Target")]
        public void Image_message_property_follows_the_registration_tool_mapping(string message, string stage, string imageType, string expected)
        {
            var step = Generate(Plugin("[PluginStep(\"" + message + "\", \"account\", PluginStage." + stage + ")]\n[PluginStepImage(PluginImageType." + imageType + ", \"i\", \"name\")]"));

            Assert.Equal(expected, Value(step.Descendants("SdkMessageProcessingStepImage").Single(), "MessagePropertyName"));
        }

        [Fact]
        public void Image_binds_to_the_step_named_by_step_name()
        {
            _fx.Compile(Plugin(
                "[PluginStep(\"Update\", \"account\", PluginStage.PostOperation, Name = \"a\")]\n" +
                "[PluginStep(\"Update\", \"contact\", PluginStage.PostOperation, Name = \"b\")]\n" +
                "[PluginStepImage(PluginImageType.PreImage, \"i\", \"name\", StepName = \"b\")]"));

            Assert.True(Run(), string.Join(Environment.NewLine, _engine.Errors));

            var steps = _fx.StepFiles.Select(f => XDocument.Load(f).Root).ToDictionary(s => (string)s.Attribute("Name"));
            Assert.Empty(steps["a"].Descendants("SdkMessageProcessingStepImage"));
            Assert.Single(steps["b"].Descendants("SdkMessageProcessingStepImage"));
        }

        [Theory]
        [InlineData("[PluginStep(\"Create\", \"account\", PluginStage.PostOperation, InvocationSource = (PluginInvocationSource)5)]", "invocation source 5")]
        [InlineData("[PluginStep(\"Create\", \"account\", (PluginStage)15)]", "stage 15")]
        [InlineData("[PluginStep(\"Create\", \"account\", PluginStage.PreOperation, Mode = PluginMode.Asynchronous)]", "asynchronous steps must run in PostOperation")]
        [InlineData("[PluginStep(\"Create\", \"account\", PluginStage.PostOperation, AsyncAutoDelete = true)]", "AsyncAutoDelete is only valid for asynchronous steps")]
        [InlineData("[PluginStep(\"talxis_unknown\", PluginStage.PostOperation)]", "has no built-in SdkMessageId")]
        [InlineData("[PluginStep(\"custom\", PluginStage.PostOperation, MessageId = \"nope\")]", "MessageId 'nope' is not a GUID")]
        [InlineData("[PluginStep(\"Create\", \"account\", PluginStage.PostOperation, Id = \"nope\")]", "Id 'nope' is not a GUID")]
        [InlineData("[PluginStep(\"Create\", \"account\", PluginStage.PostOperation)][PluginStepImage(PluginImageType.PreImage, \"i\")]", "Create has no pre-image")]
        [InlineData("[PluginStep(\"Delete\", \"account\", PluginStage.PreOperation)][PluginStepImage(PluginImageType.PostImage, \"i\")]", "post-images are only available in PostOperation")]
        [InlineData("[PluginStep(\"Delete\", \"account\", PluginStage.PostOperation)][PluginStepImage(PluginImageType.PostImage, \"i\")]", "Delete has no post-image")]
        [InlineData("[PluginStep(\"Update\", \"account\", PluginStage.PostOperation)][PluginStepImage(PluginImageType.PreImage, \"i\"), PluginStepImage(PluginImageType.PreImage, \"j\", EntityAlias = \"i\")]", "alias 'i' is already used")]
        [InlineData("[PluginStep(\"Update\", \"account\", PluginStage.PostOperation)][PluginStep(\"Update\", \"contact\", PluginStage.PostOperation)][PluginStepImage(PluginImageType.PreImage, \"i\")]", "is ambiguous")]
        [InlineData("[PluginStep(\"Update\", \"account\", PluginStage.PostOperation, Name = \"a\")][PluginStepImage(PluginImageType.PreImage, \"i\", StepName = \"zzz\")]", "no [PluginStep] on the class has that Name")]
        [InlineData("[PluginStepImage(PluginImageType.PreImage, \"i\")]", "has no [PluginStep] on the class")]
        [InlineData("[PluginStep(\"Associate\", PluginStage.PreOperation, Rank = 1)][PluginStep(\"Associate\", PluginStage.PreOperation, Rank = 2)]", "is used by several registrations")]
        public void Invalid_registrations_fail_the_build_and_write_nothing(string attributes, string expectedError)
        {
            _fx.Compile(Plugin(attributes));

            Assert.False(Run());
            Assert.Contains(_engine.Errors, e => e.Contains(expectedError));
            Assert.Empty(_fx.StepFiles);
            Assert.Empty(_fx.StepRootComponentIds());
        }

        [Fact]
        public void Filtering_attributes_outside_update_warn_but_still_generate()
        {
            var step = Generate(Plugin("[PluginStep(\"Create\", \"account\", PluginStage.PostOperation, FilteringAttributes = new[] { \"name\" })]"));

            Assert.Equal("name", Value(step, "FilteringAttributes"));
            Assert.Contains(_engine.Warnings, w => w.Contains("only to Update"));
        }

        [Fact]
        public void Second_run_changes_nothing()
        {
            Generate(Plugin("[PluginStep(\"Update\", \"account\", PluginStage.PostOperation)]"));
            string path = _fx.StepFiles.Single();
            string solutionBefore = File.ReadAllText(_fx.SolutionXmlPath);
            DateTime written = File.GetLastWriteTimeUtc(path);
            System.Threading.Thread.Sleep(1100);

            Assert.True(Run());

            Assert.Equal(written, File.GetLastWriteTimeUtc(path));
            Assert.Equal(solutionBefore, File.ReadAllText(_fx.SolutionXmlPath));
            Assert.Single(_fx.StepRootComponentIds());
        }

        [Fact]
        public void Removing_the_attribute_removes_its_step_and_root_component_only()
        {
            string handWritten = _fx.AddHandWrittenStep("Fixture.P", CreateId, "account", 40);
            Generate2(Plugin("[PluginStep(\"Update\", \"account\", PluginStage.PostOperation)]"));
            Assert.Equal(2, _fx.StepFiles.Count());

            _fx.Compile(Plugin(""));
            Assert.True(Run(), string.Join(Environment.NewLine, _engine.Errors));

            Assert.Equal(new[] { "{" + handWritten + "}.xml" }, _fx.StepFiles.Select(Path.GetFileName).ToArray());
            Assert.Equal(new[] { handWritten }, _fx.StepRootComponentIds());
        }

        [Fact]
        public void Changing_the_stage_replaces_the_generated_step()
        {
            var before = Generate(Plugin("[PluginStep(\"Update\", \"account\", PluginStage.PostOperation)]"));
            var after = Generate2(Plugin("[PluginStep(\"Update\", \"account\", PluginStage.PreOperation)]"));

            Assert.NotEqual((string)before.Attribute("SdkMessageProcessingStepId"), (string)after.Attribute("SdkMessageProcessingStepId"));
            Assert.Single(_fx.StepFiles);
            Assert.Single(_fx.StepRootComponentIds());
        }

        [Fact]
        public void Hand_written_steps_survive_when_the_class_declares_no_steps()
        {
            string handWritten = _fx.AddHandWrittenStep("Fixture.P", UpdateId, "account", 40);
            _fx.Compile(Plugin(""));

            Assert.True(Run(), string.Join(Environment.NewLine, _engine.Errors));

            Assert.Single(_fx.StepFiles);
            Assert.Equal(new[] { handWritten }, _fx.StepRootComponentIds());
        }

        [Fact]
        public void Hand_written_step_that_duplicates_a_generated_one_warns_and_stays()
        {
            string handWritten = _fx.AddHandWrittenStep("Fixture.P", UpdateId, "account", 40);
            _fx.Compile(Plugin("[PluginStep(\"Update\", \"account\", PluginStage.PostOperation)]"));

            Assert.True(Run(), string.Join(Environment.NewLine, _engine.Errors));

            Assert.Equal(2, _fx.StepFiles.Count());
            Assert.Contains("{" + handWritten + "}.xml", _fx.StepFiles.Select(Path.GetFileName));
            Assert.Contains(_engine.Warnings, w => w.Contains("would run twice"));
        }

        [Fact]
        public void Hand_written_step_of_another_plugin_assembly_is_never_touched()
        {
            Directory.CreateDirectory(_fx.StepsDirectory);
            string foreignId = Guid.NewGuid().ToString("D");
            string foreignPath = Path.Combine(_fx.StepsDirectory, "{" + foreignId + "}.xml");
            File.WriteAllText(foreignPath, "<SdkMessageProcessingStep SdkMessageProcessingStepId=\"{" + foreignId + "}\"><SdkMessageId>" + UpdateId + "</SdkMessageId><PluginTypeName>Other.Type, Other.Assembly, Version=1.0.0.0</PluginTypeName><PrimaryEntity>account</PrimaryEntity><Stage>40</Stage></SdkMessageProcessingStep>");
            _fx.Compile(Plugin(""));

            Assert.True(Run());

            Assert.True(File.Exists(foreignPath));
        }

        [Fact]
        public void Classes_that_are_not_plugins_are_ignored()
        {
            _fx.Compile(
                Plugin("[PluginStep(\"Update\", \"account\", PluginStage.PostOperation)]", "Real"),
                "[PluginStep(\"Update\", \"account\", PluginStage.PostOperation)] public class NotAPlugin { }",
                "[PluginStep(\"Update\", \"account\", PluginStage.PostOperation)] public abstract class Base : Microsoft.Xrm.Sdk.IPlugin { public void Execute(IServiceProvider serviceProvider) { } }");

            Assert.True(Run(), string.Join(Environment.NewLine, _engine.Errors));

            Assert.Contains("Fixture.Real", (string)_fx.OnlyStep().Element("PluginTypeName"));
        }

        [Fact]
        public void Several_plugin_classes_each_get_their_own_steps()
        {
            _fx.Compile(
                Plugin("[PluginStep(\"Create\", \"account\", PluginStage.PreOperation)][PluginStep(\"Update\", \"account\", PluginStage.PostOperation)]", "A"),
                Plugin("[PluginStep(\"Create\", \"account\", PluginStage.PreOperation)]", "B"));

            Assert.True(Run(), string.Join(Environment.NewLine, _engine.Errors));

            Assert.Equal(3, _fx.StepFiles.Count());
            Assert.Equal(3, _fx.StepRootComponentIds().Distinct().Count());
        }

        [Fact]
        public void Missing_plugin_assembly_data_xml_is_reported()
        {
            _fx.Compile(Plugin("[PluginStep(\"Update\", \"account\", PluginStage.PostOperation)]"));
            File.Delete(Path.Combine(_fx.Root, "PluginAssemblies", PluginFixture.AssemblyName + ".dll.data.xml"));

            Assert.False(Run());
            Assert.Contains(_engine.Errors, e => e.Contains("data.xml not found"));
        }

        [Fact]
        public void Plugin_missing_from_the_data_xml_is_reported()
        {
            _fx.Compile(Plugin("[PluginStep(\"Update\", \"account\", PluginStage.PostOperation)]"));
            string dataXml = Path.Combine(_fx.Root, "PluginAssemblies", PluginFixture.AssemblyName + ".dll.data.xml");
            var doc = XDocument.Load(dataXml);
            doc.Descendants("PluginType").Remove();
            doc.Save(dataXml);

            Assert.False(Run());
            Assert.Contains(_engine.Errors, e => e.Contains("no PluginType in"));
        }

        [Fact]
        public void Missing_plugin_assembly_is_reported()
        {
            Assert.False(Run());
            Assert.Contains(_engine.Errors, e => e.Contains("Plugin assembly not found"));
        }

        // The same step, rebuilt after changing the source, in a fixture that already ran once.
        private XElement Generate2(string cls)
        {
            _fx.Compile(cls);
            Assert.True(Run(), string.Join(Environment.NewLine, _engine.Errors));
            return XDocument.Load(_fx.StepFiles.OrderByDescending(File.GetLastWriteTimeUtc).First()).Root;
        }
    }
}
