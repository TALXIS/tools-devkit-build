# TALXIS.DevKit.Build.Dataverse.Plugin

MSBuild integration for Dataverse plugin assembly projects. Configures Visual Studio project type GUIDs for CRM plugin development, brings in `Microsoft.CrmSdk.CoreAssemblies` and `Microsoft.PowerApps.MSBuild.Plugin`, applies automatic Git-based versioning, merges referenced managed dependencies into the output DLL via ILRepack so the Dataverse sandbox can load all required types from a single assembly, and exposes metadata targets that allow Solution projects to discover and integrate plugin assemblies during build.

## Installation

```xml
<PackageReference Include="TALXIS.DevKit.Build.Dataverse.Plugin" Version="0.0.0.1" PrivateAssets="All" />
```

Or use the SDK approach:

```xml
<Project Sdk="TALXIS.DevKit.Build.Sdk/0.0.0.1">
  <PropertyGroup>
    <ProjectType>Plugin</ProjectType>
  </PropertyGroup>
</Project>
```

## How It Works

The package sets `ProjectType` to `Plugin` and configures `ProjectTypeGuids` for CRM plugin recognition in Visual Studio.

### Build-time targets

- **_ApplyPluginVersionBeforeBuild** (runs before `BeforeBuild`) -- executes `GenerateVersionNumber` followed by `ApplyPluginVersionNumber` to set `AssemblyVersion`, `FileVersion`, `Version`, and `PackageVersion` from Git.
- **AssemblyMergeDependencies** (runs after `Build`) -- uses [ILRepack](https://github.com/gluck/il-repack) to merge every managed DLL that landed in `$(OutDir)` into the main plugin assembly, so the Dataverse sandbox (which loads a single assembly) can resolve all referenced types without sibling DLLs. By default, sandbox-provided assemblies are skipped: `Microsoft.Xrm.Sdk*`, `Microsoft.Crm.Sdk.Proxy`, `Newtonsoft.Json`, `System.*`, `mscorlib`, `netstandard` (configurable via `$(AssemblyMergeExcludes)`). Idempotent — always reads the raw compiler output from `$(IntermediateOutputPath)` so the target can safely re-run within the same Solution build. Merged types keep their original public names (`Internalize=false`) to preserve Dataverse's reflection-based plugin type detection. Disable per-project with `<AssemblyMergeSkip>true</AssemblyMergeSkip>`.

### Integration targets

These targets are called by `TALXIS.DevKit.Build.Dataverse.Solution` when it discovers this project via `ProjectReference`:

- **GetProjectType** -- returns `Plugin` so the Solution build knows how to handle this reference.
- **GetPluginAssemblyInfo** -- returns `PluginRootPath`, `PluginAssemblyId`, `TargetFramework`, `PublishFolderName`, and `AssemblyName` for automatic plugin assembly metadata generation in the solution.

### Plugin step attributes

Plugin steps can be declared on the plugin class instead of hand-written `SdkMessageProcessingSteps/*.xml`. The Solution build turns them into steps (see the Solution package README).

```csharp
using TALXIS.DevKit.Build.Dataverse.Plugin;

[PluginStep("Update", "account", PluginStage.PostOperation,
    Name = "NameChanged",
    FilteringAttributes = new[] { "name", "telephone1" })]
[PluginStepImage(PluginImageType.PreImage, "PreImage", "name", "telephone1", StepName = "NameChanged")]
public class AccountPlugin : PluginBase
```

The attributes ship as `internal` source files compiled into the plugin, so the plugin gets no extra runtime dependency. They stay within C# 7.3, which is what net462 projects default to.

`PluginStep` takes the message, the primary entity and the stage. Messages that are not tied to an entity (Associate, Disassociate, an unbound custom action) use `PluginStep(message, stage)`. Optional settings:

| Setting | Default | Notes |
|---------|---------|-------|
| `Name`, `Description` | derived from type, message and entity | |
| `Id` | derived, see the Solution package README | Set it to adopt an existing step. |
| `MessageId` | built-in id | Required for any message not listed below, such as custom actions. A GUID, with or without braces. |
| `FilteringAttributes` | none | Dataverse applies them to Update only. |
| `Mode` | `Synchronous` | `Asynchronous` is only valid in `PostOperation`. |
| `AsyncAutoDelete` | `false` | Only valid for asynchronous steps. |
| `Rank` | `1` | |
| `Configuration` | none | Unsecure configuration. Secure configuration can't be set from code. |
| `InvocationSource` | `Child` | `Parent` for steps registered that way. |
| `RunAsSystem` | `false` | Writes `ImpersonatingUserIdName` `SYSTEM`. |

Messages with a built-in id: Create, Update, Delete, SetState, SetStateDynamicEntity, Assign, Retrieve, RetrieveMultiple, Associate, Disassociate and RetrieveExchangeRate. These are the same in every environment. For a custom action, copy the `SdkMessageId` from its workflow definition into `MessageId`.

`PluginStepImage` takes the image type, name and attributes (none = all columns); `StepName` binds it to a step when the class declares more than one, `EntityAlias` and `MessagePropertyName` override the defaults, and `Id` pins the image id.

Not covered by attributes, keep these as hand-written step XML (they are left alone): steps for non-class plugin types such as Power Fx expressions, and steps that impersonate a specific user.

> [!WARNING]
> Generated steps need the plugin assembly's `.data.xml` from the Solution build, so keep `GeneratePluginAssembly` enabled. Do not scaffold the same assembly with the `pp-plugin-assembly` template in such a solution: it appends a second type-91 root component and breaks the next build ([tools-devkit-templates#179](https://github.com/TALXIS/tools-devkit-templates/issues/179)).

## MSBuild Properties

| Property | Default | Description |
|----------|---------|-------------|
| `ProjectType` | `Plugin` | Marks the project as a plugin for reference discovery. |
| `Version` | _(required)_ | Base version; major/minor are used for Git versioning. |
| `GitVersionNumberBranchPrefixes` | _(none)_ | Optional `<branch>:<prefix>` map for build ordering (e.g. `develop:1;feature/*:3`). Prefix `2` is reserved for local builds. |
| `LocalBuildVersionNumber` | `0.0.20000.0` | Version used for local / non-CI builds. |
| `PluginTargetFramework` | `$(TargetFramework)` or `net462` | Target framework used to locate the compiled plugin DLL. |
| `PluginPublishFolderName` | `publish` | Publish folder name under `bin\<Configuration>\<TFM>\`. |
| `PluginAssemblyId` | _(auto-generated)_ | Explicit GUID for the plugin assembly metadata; a new GUID is generated if empty. |
| `IncludePluginRegistrationAttributes` | `true` | When `false`, the `PluginStep` / `PluginStepImage` attribute sources are not compiled into the project. |
| `AssemblyMergeSkip` | _(unset)_ | When `true`, skips the post-build `AssemblyMergeDependencies` ILRepack step. |
| `AssemblyMergeExcludes` | `mscorlib;netstandard;Newtonsoft.Json;Microsoft.Xrm.Sdk;Microsoft.Crm.Sdk.Proxy` | Semicolon-separated assembly filenames (without `.dll`) to exclude from merging. Prefix patterns `Microsoft.Xrm.Sdk.*` and `System.*` are always excluded. |

## Related Packages

- **Depends on**: `TALXIS.DevKit.Build.Dataverse.Tasks`, `Microsoft.PowerApps.MSBuild.Plugin`, `Microsoft.CrmSdk.CoreAssemblies`, `ILRepack.Lib.MSBuild.Task`
- **Consumed by**: `TALXIS.DevKit.Build.Dataverse.Solution` projects via `ProjectReference`


