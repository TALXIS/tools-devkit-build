using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Xml;
using System.Xml.Linq;

/// <summary>
/// Generates SdkMessageProcessingSteps/{id}.xml from [PluginStep] / [PluginStepImage] attributes into the solution source.
/// </summary>
public sealed class GeneratePluginSteps : Task
{
    private const int PreValidation = 10;
    private const int PreOperation = 20;
    private const int PostOperation = 40;
    private const int Asynchronous = 1;
    private const int PreImage = 0;
    private const int PostImage = 1;
    private const int BothImages = 2;

    // Same values the pp-plugin-assembly-step template uses.
    private static readonly Dictionary<string, string> SdkStepMessageIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Create"] = "9ebdbb1b-ea3e-db11-86a7-000a3a5473e8",
        ["Update"] = "20bebb1b-ea3e-db11-86a7-000a3a5473e8",
        ["Delete"] = "a1bdbb1b-ea3e-db11-86a7-000a3a5473e8",
        ["SetState"] = "1cbebb1b-ea3e-db11-86a7-000a3a5473e8",
        ["Assign"] = "8cbdbb1b-ea3e-db11-86a7-000a3a5473e8",
        ["Retrieve"] = "f2bdbb1b-ea3e-db11-86a7-000a3a5473e8",
        ["RetrieveMultiple"] = "03bebb1b-ea3e-db11-86a7-000a3a5473e8",
    };

    [Required]
    public string PluginDllPath { get; set; } = "";

    [Required]
    public string RepositoryRoot { get; set; } = "";

    public string PluginRootPath { get; set; } = "";
    public string Configuration { get; set; } = "Debug";
    public string TargetFramework { get; set; } = "net462";

    public override bool Execute()
    {
        try
        {
            if (!File.Exists(PluginDllPath))
                throw new FileNotFoundException("Plugin assembly not found", PluginDllPath);

            var errors = new List<string>();
            var warnings = new List<string>();
            string assemblyName = AssemblyName.GetAssemblyName(PluginDllPath).Name;

            List<StepInfo> steps = ReadSteps(errors);
            foreach (var step in steps)
                Validate(step, errors, warnings);

            if (steps.Count > 0 && errors.Count == 0)
            {
                Dictionary<string, string> pluginTypeIds = ReadPluginTypeIds(assemblyName, out string assemblyFullName, errors);
                if (errors.Count == 0)
                {
                    foreach (var step in steps)
                        ResolveIds(step, assemblyName, assemblyFullName, pluginTypeIds, errors);
                    ValidateUniqueIds(steps, errors);
                }
            }

            foreach (var warning in warnings) Log.LogWarning(warning);
            foreach (var error in errors) Log.LogError(error);
            if (errors.Count > 0) return false;

            // Runs even without attributes, so removing the last [PluginStep] also removes its step.
            List<string> removedIds = WriteSteps(steps, assemblyName);
            UpdateRootComponents(steps, removedIds);
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    private List<StepInfo> ReadSteps(List<string> errors)
    {
        var probeDirs = new List<string> { Path.GetDirectoryName(PluginDllPath) };
        if (!string.IsNullOrWhiteSpace(PluginRootPath))
        {
            probeDirs.Add(Path.Combine(PluginRootPath, "bin", Configuration, TargetFramework));
            probeDirs.Add(PluginRootPath);
        }

        var result = new List<StepInfo>();
        using (var scanner = new MetadataTypeScanner(PluginDllPath, probeDirs))
        {
            foreach (var type in scanner.GetPublicTypes())
            {
                if (type.IsInterface || type.IsAbstract) continue;
                if (!type.ImplementsInterface("Microsoft.Xrm.Sdk", "IPlugin")) continue;

                var steps = type.GetCustomAttributes("PluginStepAttribute").Select(a => ReadStep(type.FullName, a)).ToList();
                foreach (var image in type.GetCustomAttributes("PluginStepImageAttribute").Select(ReadImage))
                {
                    StepInfo target = FindImageStep(steps, image, out string error);
                    if (target == null) errors.Add(type.FullName + ": image '" + image.Name + "' " + error);
                    else target.Images.Add(image);
                }

                result.AddRange(steps);
            }
        }

        return result;
    }

    private static StepInfo ReadStep(string pluginTypeName, CustomAttributeValue<string> value)
    {
        var step = new StepInfo
        {
            PluginTypeName = pluginTypeName,
            Message = Fixed<string>(value, 0) ?? "",
            PrimaryEntity = Fixed<string>(value, 1) ?? "",
            Stage = Fixed<int>(value, 2),
        };

        foreach (var named in value.NamedArguments)
        {
            switch (named.Name)
            {
                case "Name": step.Name = named.Value as string; break;
                case "Id": step.Id = named.Value as string; break;
                case "MessageId": step.MessageId = named.Value as string; break;
                case "FilteringAttributes": step.FilteringAttributes = Strings(named.Value); break;
                case "Mode": step.Mode = (int)named.Value; break;
                case "Rank": step.Rank = (int)named.Value; break;
                case "AsyncAutoDelete": step.AsyncAutoDelete = (bool)named.Value; break;
                case "Configuration": step.Configuration = named.Value as string; break;
                case "Description": step.Description = named.Value as string; break;
            }
        }

        return step;
    }

    private static ImageInfo ReadImage(CustomAttributeValue<string> value)
    {
        var image = new ImageInfo
        {
            ImageType = Fixed<int>(value, 0),
            Name = Fixed<string>(value, 1) ?? "",
            Attributes = value.FixedArguments.Length > 2 ? Strings(value.FixedArguments[2].Value) : new List<string>(),
        };

        foreach (var named in value.NamedArguments)
        {
            switch (named.Name)
            {
                case "StepName": image.StepName = named.Value as string; break;
                case "Id": image.Id = named.Value as string; break;
                case "EntityAlias": image.EntityAlias = named.Value as string; break;
                case "MessagePropertyName": image.MessagePropertyName = named.Value as string; break;
            }
        }

        return image;
    }

    private static StepInfo FindImageStep(List<StepInfo> steps, ImageInfo image, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(image.StepName))
        {
            if (steps.Count == 1) return steps[0];
            error = steps.Count == 0
                ? "has no [PluginStep] on the class."
                : "is ambiguous, the class has several [PluginStep]s. Set StepName to the Name of the target step.";
            return null;
        }

        var match = steps.FirstOrDefault(s => string.Equals(s.Name, image.StepName, StringComparison.Ordinal));
        if (match == null)
            error = "references StepName '" + image.StepName + "', but no [PluginStep] on the class has that Name.";

        return match;
    }

    private static T Fixed<T>(CustomAttributeValue<string> value, int index)
        => value.FixedArguments.Length > index && value.FixedArguments[index].Value is T v ? v : default;

    private static List<string> Strings(object arrayValue)
    {
        if (arrayValue is not ImmutableArray<CustomAttributeTypedArgument<string>> items) return new List<string>();

        return items
            .Select(i => i.Value as string)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void Validate(StepInfo step, List<string> errors, List<string> warnings)
    {
        string where = step.PluginTypeName + " [" + step.Message + " of " + step.PrimaryEntity + "]";

        if (string.IsNullOrWhiteSpace(step.Message)) errors.Add(where + ": message is empty.");
        if (step.Stage != PreValidation && step.Stage != PreOperation && step.Stage != PostOperation)
            errors.Add(where + ": stage " + step.Stage + " is not PreValidation (10), PreOperation (20) or PostOperation (40).");
        if (step.Mode == Asynchronous && step.Stage != PostOperation)
            errors.Add(where + ": asynchronous steps must run in PostOperation.");
        if (step.AsyncAutoDelete && step.Mode != Asynchronous)
            errors.Add(where + ": AsyncAutoDelete is only valid for asynchronous steps.");
        if (!IsGuidOrEmpty(step.Id)) errors.Add(where + ": Id '" + step.Id + "' is not a GUID.");
        if (!IsGuidOrEmpty(step.MessageId)) errors.Add(where + ": MessageId '" + step.MessageId + "' is not a GUID.");
        if (string.IsNullOrWhiteSpace(step.MessageId) && !SdkStepMessageIds.ContainsKey(step.Message))
            errors.Add(where + ": '" + step.Message + "' has no built-in SdkMessageId (only " + string.Join(", ", SdkStepMessageIds.Keys) + " do), set MessageId.");
        if (step.FilteringAttributes.Count > 0 && !IsMessage(step, "Update"))
            warnings.Add(where + ": Dataverse applies filtering attributes only to Update, they have no effect here.");

        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in step.Images)
        {
            string imageWhere = where + " image '" + image.Name + "'";

            if (string.IsNullOrWhiteSpace(image.Name)) errors.Add(where + ": image name is empty.");
            if (image.ImageType < PreImage || image.ImageType > BothImages)
                errors.Add(imageWhere + ": unknown image type " + image.ImageType + ".");
            if (!IsGuidOrEmpty(image.Id)) errors.Add(imageWhere + ": Id '" + image.Id + "' is not a GUID.");
            if (image.ImageType != PreImage && step.Stage != PostOperation)
                errors.Add(imageWhere + ": post-images are only available in PostOperation.");
            if (image.ImageType != PostImage && IsMessage(step, "Create"))
                errors.Add(imageWhere + ": Create has no pre-image, the record does not exist yet.");
            if (image.ImageType != PreImage && IsMessage(step, "Delete"))
                errors.Add(imageWhere + ": Delete has no post-image, the record is gone.");

            string alias = image.EntityAlias ?? image.Name;
            if (image.ImageType != PostImage && !aliases.Add("pre|" + alias))
                errors.Add(imageWhere + ": alias '" + alias + "' is already used by another pre-image on this step.");
            if (image.ImageType != PreImage && !aliases.Add("post|" + alias))
                errors.Add(imageWhere + ": alias '" + alias + "' is already used by another post-image on this step.");
        }
    }

    private static void ValidateUniqueIds(List<StepInfo> steps, List<string> errors)
    {
        foreach (var group in steps.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add("Step id " + group.Key + " is used by several registrations (" + string.Join(", ", group.Select(s => s.PluginTypeName)) + "). Set a distinct Id on them.");

        foreach (var group in steps.SelectMany(s => s.Images).GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add("Image id " + group.Key + " is used by several images. Set a distinct Id on them.");
    }

    private Dictionary<string, string> ReadPluginTypeIds(string assemblyName, out string assemblyFullName, List<string> errors)
    {
        assemblyFullName = null;
        string pluginAssembliesRoot = Path.Combine(RepositoryRoot, "PluginAssemblies");
        string dataXmlPath = Directory.Exists(pluginAssembliesRoot)
            ? Directory.GetFiles(pluginAssembliesRoot, assemblyName + ".dll.data.xml", SearchOption.AllDirectories).FirstOrDefault()
            : null;

        if (dataXmlPath == null)
        {
            errors.Add("PluginAssemblies/**/" + assemblyName + ".dll.data.xml not found in the solution, it is needed to resolve PluginTypeIds for [PluginStep]. Keep GeneratePluginAssembly enabled or add the file.");
            return null;
        }

        var root = XDocument.Load(dataXmlPath).Root;
        assemblyFullName = root?.Attribute("FullName")?.Value;

        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pluginType in root?.Descendants("PluginType") ?? Enumerable.Empty<XElement>())
        {
            string aqn = pluginType.Attribute("AssemblyQualifiedName")?.Value ?? "";
            string className = aqn.Contains(',') ? aqn.Substring(0, aqn.IndexOf(',')).Trim() : pluginType.Attribute("Name")?.Value;
            string id = pluginType.Attribute("PluginTypeId")?.Value;
            if (!string.IsNullOrWhiteSpace(className) && !string.IsNullOrWhiteSpace(id))
                ids[className] = NormalizeGuid(id);
        }

        return ids;
    }

    private static void ResolveIds(StepInfo step, string assemblyName, string assemblyFullName, Dictionary<string, string> pluginTypeIds, List<string> errors)
    {
        if (pluginTypeIds.TryGetValue(step.PluginTypeName, out string pluginTypeId))
            step.PluginTypeId = pluginTypeId;
        else
            errors.Add(step.PluginTypeName + ": no PluginType in " + assemblyName + ".dll.data.xml, so its steps cannot be generated.");

        step.AssemblyQualifiedName = step.PluginTypeName + ", " + assemblyFullName;
        step.SdkMessageId = !string.IsNullOrWhiteSpace(step.MessageId) ? NormalizeGuid(step.MessageId) : SdkStepMessageIds[step.Message];
        step.Id = !string.IsNullOrWhiteSpace(step.Id)
            ? NormalizeGuid(step.Id)
            : GeneratedStepId(assemblyName, step.PluginTypeName, step.SdkMessageId, step.PrimaryEntity, step.Stage.ToString());

        foreach (var image in step.Images)
        {
            image.Id = !string.IsNullOrWhiteSpace(image.Id)
                ? NormalizeGuid(image.Id)
                : DeterministicGuid.Create(string.Join("|", "SdkMessageProcessingStepImage", step.Id, image.ImageType, (image.EntityAlias ?? image.Name).ToLowerInvariant()));
        }
    }

    // Built only from values stored in the step XML, so a generated file can be recognized later and removed with its attribute.
    private static string GeneratedStepId(string assemblyName, string pluginTypeName, string sdkMessageId, string primaryEntity, string stage)
        => DeterministicGuid.Create(string.Join("|", "SdkMessageProcessingStep", assemblyName, pluginTypeName,
            sdkMessageId.ToLowerInvariant(), primaryEntity.ToLowerInvariant(), stage));

    private List<string> WriteSteps(List<StepInfo> steps, string assemblyName)
    {
        string stepsDir = Path.Combine(RepositoryRoot, "SdkMessageProcessingSteps");
        var currentIds = new HashSet<string>(steps.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
        var removedIds = new List<string>();

        var existing = Directory.Exists(stepsDir)
            ? Directory.GetFiles(stepsDir, "*.xml").Select(ReadExistingStep).Where(s => s != null).ToList()
            : new List<ExistingStep>();

        foreach (var file in existing.Where(f => !currentIds.Contains(f.Id)))
        {
            if (file.AssemblyName == assemblyName && file.Id == GeneratedStepId(assemblyName, file.PluginTypeName, file.SdkMessageId, file.PrimaryEntity, file.Stage))
            {
                File.Delete(file.Path);
                removedIds.Add(file.Id);
                Log.LogMessage(MessageImportance.High, "Plugin step removed, its [PluginStep] no longer exists: " + file.Path);
                continue;
            }

            foreach (var step in steps.Where(file.Matches))
                Log.LogWarning(step.PluginTypeName + " has a hand-written step " + Path.GetFileName(file.Path) + " for " + step.Message + " of " + step.PrimaryEntity + " (stage " + step.Stage + ") besides [PluginStep], so it would run twice. Delete the XML or set Id on the attribute to its id.");
        }

        if (steps.Count > 0) Directory.CreateDirectory(stepsDir);
        foreach (var step in steps)
        {
            string path = Path.Combine(stepsDir, "{" + step.Id + "}.xml");
            if (WriteIfChanged(path, BuildStepXml(step)))
                Log.LogMessage(MessageImportance.High, "Plugin step generated: " + path);
        }

        return removedIds;
    }

    private static XDocument BuildStepXml(StepInfo step)
    {
        string name = string.IsNullOrWhiteSpace(step.Name)
            ? step.PluginTypeName + ": " + step.Message + (string.IsNullOrWhiteSpace(step.PrimaryEntity) ? "" : " of " + step.PrimaryEntity)
            : step.Name;

        var root = new XElement("SdkMessageProcessingStep",
            new XAttribute("Name", name),
            new XAttribute("SdkMessageProcessingStepId", "{" + step.Id + "}"),
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XElement("SdkMessageId", step.SdkMessageId),
            new XElement("PluginTypeName", step.AssemblyQualifiedName),
            new XElement("PluginTypeId", step.PluginTypeId));

        if (!string.IsNullOrWhiteSpace(step.PrimaryEntity)) root.Add(new XElement("PrimaryEntity", step.PrimaryEntity));
        root.Add(new XElement("AsyncAutoDelete", step.AsyncAutoDelete ? 1 : 0));
        if (!string.IsNullOrEmpty(step.Configuration)) root.Add(new XElement("Configuration", step.Configuration));

        root.Add(
            new XElement("Description", string.IsNullOrWhiteSpace(step.Description) ? name : step.Description),
            new XElement("FilteringAttributes", string.Join(",", step.FilteringAttributes)),
            new XElement("InvocationSource", 1),
            new XElement("Mode", step.Mode),
            new XElement("Rank", step.Rank),
            new XElement("EventHandlerTypeCode", 4602),
            new XElement("Stage", step.Stage),
            new XElement("IsCustomizable", 1),
            new XElement("IsHidden", 0),
            new XElement("SupportedDeployment", 0),
            new XElement("IntroducedVersion", "1.0"),
            new XElement("SdkMessageProcessingStepImages", step.Images.Select(image =>
                new XElement("SdkMessageProcessingStepImage",
                    new XAttribute("Name", image.Name),
                    new XElement("SdkMessageProcessingStepImageId", "{" + image.Id + "}"),
                    new XElement("Attributes", string.Join(",", image.Attributes)),
                    new XElement("EntityAlias", image.EntityAlias ?? image.Name),
                    new XElement("ImageType", image.ImageType),
                    new XElement("MessagePropertyName", image.MessagePropertyName ?? DefaultMessagePropertyName(step)),
                    new XElement("IsCustomizable", 1)))));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    // Same mapping the Plugin Registration Tool uses when it registers an image.
    private static string DefaultMessagePropertyName(StepInfo step)
    {
        switch (step.Message.ToLowerInvariant())
        {
            case "create": return "Id";
            case "setstate":
            case "setstatedynamicentity": return "EntityMoniker";
            case "deliverincoming":
            case "deliverpromote": return "EmailId";
            case "send":
                if (string.Equals(step.PrimaryEntity, "fax", StringComparison.OrdinalIgnoreCase)) return "FaxId";
                if (string.Equals(step.PrimaryEntity, "template", StringComparison.OrdinalIgnoreCase)) return "TemplateId";
                return "EmailId";
            default: return "Target";
        }
    }

    private void UpdateRootComponents(List<StepInfo> steps, List<string> removedIds)
    {
        if (steps.Count == 0 && removedIds.Count == 0) return;

        string solutionXmlPath = Path.Combine(RepositoryRoot, "Other", "Solution.xml");
        if (!File.Exists(solutionXmlPath))
            throw new FileNotFoundException("Solution.xml not found", solutionXmlPath);

        var doc = new XmlDocument();
        doc.Load(solutionXmlPath);

        var rootComponents = doc.SelectSingleNode("//RootComponents") as XmlElement;
        if (rootComponents == null)
            throw new Exception("Solution.xml has no RootComponents element");

        var stepComponents = rootComponents.ChildNodes.OfType<XmlElement>()
            .Where(e => e.Name == "RootComponent" && e.GetAttribute("type") == "92")
            .ToList();

        bool changed = false;
        foreach (var component in stepComponents.Where(e => removedIds.Contains(NormalizeGuid(e.GetAttribute("id")), StringComparer.OrdinalIgnoreCase)))
        {
            rootComponents.RemoveChild(component);
            changed = true;
        }

        var registered = new HashSet<string>(stepComponents.Select(e => NormalizeGuid(e.GetAttribute("id"))), StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps.Where(s => registered.Add(s.Id)))
        {
            XmlElement rc = doc.CreateElement("RootComponent");
            rc.SetAttribute("type", "92");
            rc.SetAttribute("id", "{" + step.Id + "}");
            rc.SetAttribute("behavior", "0");
            rootComponents.AppendChild(rc);
            changed = true;
        }

        if (changed) doc.Save(solutionXmlPath);
    }

    // Rewriting an unchanged file would show up as noise in git on every build.
    private static bool WriteIfChanged(string path, XDocument doc)
    {
        string content;
        using (var writer = new Utf8StringWriter())
        {
            doc.Save(writer);
            content = writer.ToString();
        }

        if (File.Exists(path) && File.ReadAllText(path) == content) return false;

        File.WriteAllText(path, content, new UTF8Encoding(true));
        return true;
    }

    private static ExistingStep ReadExistingStep(string path)
    {
        try
        {
            var root = XDocument.Load(path).Root;
            if (root == null) return null;

            string[] typeName = ((string)root.Element("PluginTypeName") ?? "").Split(',');
            return new ExistingStep
            {
                Path = path,
                Id = NormalizeGuid((string)root.Attribute("SdkMessageProcessingStepId")),
                PluginTypeName = typeName[0].Trim(),
                AssemblyName = typeName.Length > 1 ? typeName[1].Trim() : "",
                SdkMessageId = NormalizeGuid((string)root.Element("SdkMessageId")),
                PrimaryEntity = (string)root.Element("PrimaryEntity") ?? "",
                Stage = (string)root.Element("Stage") ?? "",
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsMessage(StepInfo step, string message)
        => string.Equals(step.Message, message, StringComparison.OrdinalIgnoreCase);

    private static bool IsGuidOrEmpty(string value)
        => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value.Trim().Trim('{', '}'), out _);

    private static string NormalizeGuid(string value) => Guid.Parse(value.Trim().Trim('{', '}')).ToString("D");

    private sealed class StepInfo
    {
        public string PluginTypeName { get; set; } = "";
        public string Message { get; set; } = "";
        public string PrimaryEntity { get; set; } = "";
        public int Stage { get; set; }
        public string Name { get; set; }
        public string Id { get; set; }
        public string MessageId { get; set; }
        public List<string> FilteringAttributes { get; set; } = new List<string>();
        public int Mode { get; set; }
        public int Rank { get; set; } = 1;
        public bool AsyncAutoDelete { get; set; }
        public string Configuration { get; set; }
        public string Description { get; set; }
        public List<ImageInfo> Images { get; } = new List<ImageInfo>();

        public string SdkMessageId { get; set; } = "";
        public string PluginTypeId { get; set; } = "";
        public string AssemblyQualifiedName { get; set; } = "";
    }

    private sealed class ImageInfo
    {
        public int ImageType { get; set; }
        public string Name { get; set; } = "";
        public List<string> Attributes { get; set; } = new List<string>();
        public string StepName { get; set; }
        public string Id { get; set; }
        public string EntityAlias { get; set; }
        public string MessagePropertyName { get; set; }
    }

    private sealed class ExistingStep
    {
        public string Path { get; set; } = "";
        public string Id { get; set; } = "";
        public string PluginTypeName { get; set; } = "";
        public string AssemblyName { get; set; } = "";
        public string SdkMessageId { get; set; } = "";
        public string PrimaryEntity { get; set; } = "";
        public string Stage { get; set; } = "";

        public bool Matches(StepInfo step)
            => PluginTypeName == step.PluginTypeName
               && string.Equals(SdkMessageId, step.SdkMessageId, StringComparison.OrdinalIgnoreCase)
               && string.Equals(PrimaryEntity, step.PrimaryEntity, StringComparison.OrdinalIgnoreCase)
               && Stage == step.Stage.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
