# TALXIS.DevKit.Build.Dataverse.Solution

MSBuild integration for building complete Dataverse solutions. Orchestrates the entire solution build pipeline: discovers and builds referenced Plugin, WorkflowActivity, ScriptLibrary, CodeApp, and PCF projects; patches solution XML with version, publisher, and managed state; supports manual invocation of schema validation targets for solution metadata against XSD/JSON schemas; runs the shared tasks package's SolutionPackagerLib-based packager to produce a `.zip` file; and supports `dotnet pack` to generate a NuGet package containing the solution zip.

## Installation

```xml
<PackageReference Include="TALXIS.DevKit.Build.Dataverse.Solution" Version="0.0.0.1" PrivateAssets="All" />
```

Or use the SDK approach:

```xml
<Project Sdk="TALXIS.DevKit.Build.Sdk/0.0.0.1">
  <PropertyGroup>
    <ProjectType>Solution</ProjectType>
  </PropertyGroup>
</Project>
```

## How It Works

The package sets `ProjectType` to `Solution` and imports `Microsoft.PowerApps.MSBuild.Solution` targets. The build pipeline executes in the following order:

### 1. Component discovery

`ProbePluginLibraries`, `ProbeScriptLibraries`, `ProbeCodeApps`, and `ProbeWorkflowActivityLibraries` call `GetProjectType` on all `ProjectReference` items to classify them by component type. ScriptLibrary and CodeApp references are removed from `@(ProjectReference)` after discovery so the standard .NET `ResolveProjectReferences` pipeline does not build them a second time -- they are built explicitly in the next step.

### 2. Component builds

`BuildPluginLibraries`, `BuildScriptLibraries`, `BuildCodeApps`, and `BuildWorkflowActivityLibraries` compile each referenced component project before `CopyCdsSolutionContent`.

### 3. Component metadata generation

- **Plugin assemblies** -- `EnsurePluginAssemblyDataXml` generates `.data.xml` files under `PluginAssemblies/`.
- **Plugin steps** -- `GeneratePluginSteps` (right after `EnsurePluginAssemblyDataXml`) writes `SdkMessageProcessingSteps/{id}.xml` and Type 92 root components into the solution source from `[PluginStep]` / `[PluginStepImage]` attributes on plugin classes, so the steps are committed like the assembly `.data.xml`. Files are only rewritten when their content changes. Step and image ids are derived from plugin type, message, entity and stage, so they stay stable; a generated step is deleted together with its attribute, hand-written steps are never touched. Set `Id` on the attribute to adopt an existing step. The build fails on invalid registrations (pre-image on Create, post-image outside PostOperation, asynchronous step outside PostOperation, ambiguous image binding, unknown message without `MessageId`) and warns when a hand-written step duplicates a generated one.
- **Workflow activities** -- `EnsureWorkflowActivityAssemblyDataXml` generates `.data.xml` for workflow activity assemblies.
- **Script libraries** -- `CopyScriptLibrariesToWebResources` resolves web resource names with the publisher prefix, generates `.data.xml`, and registers root components in `Solution.xml`.
- **Code apps** -- `PrepareCodeAppsSources` generates `.meta.xml` via `GenerateCodeAppMetaXml`, adds root components (Type 300) to `Solution.xml`, and ensures the `CanvasApps` node exists in `Customizations.xml`.

### 4. Solution XML patching

`PatchSolutionXml` writes `Version` (use `ApplyVersionNumber` instead, see below), `Managed`, `PublisherName`, and `PublisherPrefix` into `Solution.xml` (all optional).

### 5. Solution packaging override and versioning

`ProcessCdsProjectReferencesOutputs` replaces the Microsoft default to filter ScriptLibrary, CodeApp, and WorkflowActivity references from solution packaging processing. Then `GenerateVersionNumber` and `ApplyVersionNumber` patch the version across all solution metadata.

### 6. Schema validation (manual)

`ValidateSolutionComponentSchema` validates all solution XML files against 22 bundled XSD schemas and JSON flows against a JSON schema. Validation runs in batch mode -- all errors are collected before failing the build, with MSBuild-canonical error format for IDE click-through.

> [!TIP]
> This validation is **not wired into the build pipeline automatically** -- it must be invoked manually, e.g. `dotnet build -t:ValidateSolutionComponentSchema`.

### 7. Solution packaging

`PowerAppsPackage` invokes the tasks package's SolutionPackagerLib-based packager to produce the output `.zip`.

### 8. NuGet packing

`dotnet pack` produces a `.nupkg` with the solution `.zip` under `content/solution/`.

## MSBuild Properties

### General

| Property | Default | Description |
|----------|---------|-------------|
| `ProjectType` | `Solution` | Marks the project as a solution for reference discovery. |
| `Version` | _(required)_ | Base version; used for Git versioning and applied to solution.xml and related metadata. See [Versioning](/docs/Versioning.md). |

### Solution metadata

| Property | Default | Description |
|----------|---------|-------------|
| `Managed` | _(none)_ | Value written to the `<Managed>` element in solution.xml. |
| `PublisherName` | _(none)_ | Value written to the publisher name fields in solution.xml. |
| `PublisherPrefix` | _(none)_ | Value written to solution.xml and used as the web resource name prefix. |

### Paths

| Property | Default | Description |
|----------|---------|-------------|
| `SolutionRootPath` | `.` | Relative path to the solution source root. |
| `SolutionPackagerWorkingDirectory` | `$(IntermediateOutputPath)` | Working folder for solution packager operations. |
| `SolutionPackagerMetadataWorkingDirectory` | `$(SolutionPackagerWorkingDirectory)Metadata` | Metadata folder used for version updates. |
| `SolutionPackagerLocalizationWorkingDirectory` | _(none)_ | Optional localization working folder (cleaned by `CleanupWorkingDirectory`). |
| `SolutionPackageLogFilePath` | `$(IntermediateOutputPath)SolutionPackager.log` | SolutionPackager log path. |
| `SolutionPackageZipFilePath` | `$(OutputPath)$(MSBuildProjectName).zip` | Output zip path for pack tasks. |

### Web resources and PCF

| Property | Default | Description |
|----------|---------|-------------|
| `WebResourcesDir` | `$(MSBuildProjectDirectory)\$(SolutionRootPath)\WebResources\` | Destination folder for script library web resources. |
| `GeneratePluginSteps` | `true` | When `false`, skips generating plugin steps from `[PluginStep]` attributes. |
| `PcfForceUpdate` | _(none)_ | Forwarded to `ProcessCdsProjectReferencesOutputs` to force PCF updates before solution packaging. |

### Validation

| Property | Default | Description |
|----------|---------|-------------|
| `SkipDuplicateGuidValidation` | _(unset)_ | When `true`, skips the `_ValidateDuplicateGuidsBeforePackage` check during build. |
| `SkipQuickFindValidation` | _(unset)_ | When `true`, skips the `_ValidateQuickFindViewsBeforePackage` check during build. |

Schema validation via `ValidateSolutionComponentSchema` is **not wired automatically** -- invoke it manually (e.g. `dotnet build -t:ValidateSolutionComponentSchema`). No skip property is needed.

## Related Packages

- **Depends on**: `TALXIS.DevKit.Build.Dataverse.Tasks`, `Microsoft.PowerApps.MSBuild.Solution`
- **Discovers and builds**: `Plugin`, `WorkflowActivity`, `ScriptLibrary`, `CodeApp`, and `Pcf` projects via `ProjectReference`
