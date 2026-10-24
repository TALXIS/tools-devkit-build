using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Linq;
using TALXIS.DevKit.Build.Dataverse.Tasks.Tests.Support;
using Xunit;

namespace TALXIS.DevKit.Build.Dataverse.Tasks.Tests
{
    /// <summary>
    /// A plugin project that references another plugin project ends up with two copies of the internal attribute types
    /// once ILRepack has merged them; it keeps one and renames the other to "&lt;guid&gt;Name". Classes from either
    /// assembly can carry either copy, and the generator must see both.
    /// </summary>
    public class MergedAttributeTypesTests : IDisposable
    {
        private const string AttributeNamespace = "TALXIS.DevKit.Build.Dataverse.Plugin";
        private const string UpdateId = "20bebb1b-ea3e-db11-86a7-000a3a5473e8";

        private readonly PluginFixture _fx = new PluginFixture();
        private readonly FakeBuildEngine _engine = new FakeBuildEngine();

        public void Dispose() => _fx.Dispose();

        [Theory]
        [InlineData("PluginStepAttribute")]
        [InlineData("<47f78ed5-d240-4cae-ae42-2a1229643906>PluginStepAttribute")]
        public void Step_is_generated_whichever_copy_of_the_attribute_type_the_class_carries(string attributeTypeName)
        {
            _fx.Emit(path => EmitPlugin(path, attributeTypeName), "Fixture.P");

            Assert.True(_fx.NewTask(_engine).Execute(), string.Join(Environment.NewLine, _engine.Errors));

            var step = _fx.OnlyStep();
            Assert.Equal(UpdateId, (string)step.Element("SdkMessageId"));
            Assert.Equal("account", (string)step.Element("PrimaryEntity"));
            Assert.Equal("40", (string)step.Element("Stage"));
        }

        [Fact]
        public void Generated_step_survives_when_a_merge_renames_the_attribute_type()
        {
            _fx.Emit(path => EmitPlugin(path, "PluginStepAttribute"), "Fixture.P");
            Assert.True(_fx.NewTask(_engine).Execute());
            string file = _fx.StepFiles.Single();
            string content = File.ReadAllText(file);

            _fx.Emit(path => EmitPlugin(path, "<47f78ed5-d240-4cae-ae42-2a1229643906>PluginStepAttribute"), "Fixture.P");
            Assert.True(_fx.NewTask(new FakeBuildEngine()).Execute());

            Assert.Equal(new[] { file }, _fx.StepFiles.ToArray());
            Assert.Equal(content, File.ReadAllText(file));
            Assert.Single(_fx.StepRootComponentIds());
        }

        // class Fixture.P : Microsoft.Xrm.Sdk.IPlugin carrying [<attributeTypeName>("Update", "account", 40)],
        // with the attribute type defined under that exact name (C# cannot spell a name like "<guid>PluginStepAttribute").
        private static void EmitPlugin(string path, string attributeTypeName)
        {
            var assembly = new PersistedAssemblyBuilder(new AssemblyName(PluginFixture.AssemblyName), typeof(object).Assembly);
            ModuleBuilder module = assembly.DefineDynamicModule(PluginFixture.AssemblyName);

            TypeBuilder plugin = module.DefineType("Microsoft.Xrm.Sdk.IPlugin", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
            plugin.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                typeof(void), new[] { typeof(IServiceProvider) });

            TypeBuilder attribute = module.DefineType(AttributeNamespace + "." + attributeTypeName,
                TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Class, typeof(Attribute));
            ConstructorBuilder attributeCtor = attribute.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard,
                new[] { typeof(string), typeof(string), typeof(int) });
            ILGenerator il = attributeCtor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(Attribute).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null));
            il.Emit(OpCodes.Ret);

            TypeBuilder pluginClass = module.DefineType("Fixture.P", TypeAttributes.Public | TypeAttributes.Class, typeof(object), new Type[] { plugin });
            ConstructorBuilder ctor = pluginClass.DefineDefaultConstructor(MethodAttributes.Public);
            MethodBuilder execute = pluginClass.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.Final | MethodAttributes.NewSlot,
                typeof(void), new[] { typeof(IServiceProvider) });
            execute.GetILGenerator().Emit(OpCodes.Ret);
            pluginClass.SetCustomAttribute(new CustomAttributeBuilder(attributeCtor, new object[] { "Update", "account", 40 }));

            plugin.CreateType();
            attribute.CreateType();
            pluginClass.CreateType();
            assembly.Save(path);
        }
    }
}
