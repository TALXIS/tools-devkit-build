using System;

namespace TALXIS.DevKit.Build.Dataverse.Plugin
{
    /// <summary>
    /// Registers the plugin class for a Dataverse message; the build generates the SdkMessageProcessingStep from it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    internal sealed class PluginStepAttribute : Attribute
    {
        public PluginStepAttribute(string message, string primaryEntity, PluginStage stage)
        {
            Message = message;
            PrimaryEntity = primaryEntity;
            Stage = stage;
        }

        /// <summary>
        /// For messages that are not tied to an entity, such as Associate, Disassociate or an unbound custom action.
        /// </summary>
        public PluginStepAttribute(string message, PluginStage stage)
        {
            Message = message;
            Stage = stage;
        }

        public string Message { get; }
        public string PrimaryEntity { get; }
        public PluginStage Stage { get; }

        public string Name { get; set; }
        public string Id { get; set; }
        public string MessageId { get; set; }
        public string[] FilteringAttributes { get; set; }
        public PluginMode Mode { get; set; }
        public int Rank { get; set; } = 1;
        public bool AsyncAutoDelete { get; set; }
        public string Configuration { get; set; }
        public string Description { get; set; }
        /// <summary>
        /// Not set = what Dataverse stores for the message: Child for Create, Update, Delete and RetrieveExchangeRate, Parent otherwise.
        /// </summary>
        public PluginInvocationSource InvocationSource { get; set; }
        public bool RunAsSystem { get; set; }
    }
}
