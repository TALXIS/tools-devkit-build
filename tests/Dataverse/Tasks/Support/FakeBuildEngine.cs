using Microsoft.Build.Framework;
using System.Collections;
using System.Collections.Generic;

namespace TALXIS.DevKit.Build.Dataverse.Tasks.Tests.Support
{
    internal sealed class FakeBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Messages { get; } = new List<string>();

        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "test.proj";

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message);
        public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e.Message);
        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e.Message);
    }
}
