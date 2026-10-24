using System;

namespace TALXIS.DevKit.Build.Dataverse.Plugin
{
    /// <summary>
    /// Adds an entity image to a step declared by <see cref="PluginStepAttribute"/> on the same class.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    internal sealed class PluginStepImageAttribute : Attribute
    {
        public PluginStepImageAttribute(PluginImageType imageType, string name, params string[] attributes)
        {
            ImageType = imageType;
            ImageName = name;
            Attributes = attributes;
        }

        public PluginImageType ImageType { get; }
        public string ImageName { get; }
        public string[] Attributes { get; }

        public string StepName { get; set; }
        public string Id { get; set; }
        public string EntityAlias { get; set; }
        public string MessagePropertyName { get; set; }
    }
}
