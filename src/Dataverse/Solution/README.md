# TALXIS.DevKit.Build.Dataverse.Solution

MSBuild integration for building complete Dataverse solutions. Orchestrates the entire solution build pipeline: discovers and builds referenced Plugin, WorkflowActivity, ScriptLibrary, and PCF projects; patches solution XML with version, publisher, and managed state; runs the PAC solution packager to produce a `.zip` file; and supports `dotnet pack` to generate a NuGet package containing the solution zip.

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

`ProbePluginLibraries`, `ProbeScriptLibraries`, and `ProbeWorkflowActivityLibraries` call `GetProjectType` on all `ProjectReference` items to classify them by component type.

### 2. Component builds

`BuildPluginLibraries`, `BuildScriptLibraries`, and `BuildWorkflowActivityLibraries` compile each referenced component project before `CopyCdsSolutionContent`.

### 3. Component metadata generation

- **Plugin assemblies** -- `EnsurePluginAssemblyDataXml` generates `.data.xml` files under `PluginAssemblies/`.
- **Workflow activities** -- `EnsureWorkflowActivityAssemblyDataXml` generates `.data.xml` for workflow activity assemblies.
- **Script libraries** -- `CopyScriptLibrariesToWebResources` resolves web resource names with the publisher prefix, generates `.data.xml`, and registers root components in `Solution.xml`.

### 4. Solution XML patching

`PatchSolutionXml` writes `Version` (use `ApplyVersionNumber` instead, see below), `Managed`, `PublisherName`, and `PublisherPrefix` into `Solution.xml` (all optional).

### 5. PAC override and versioning

`ProcessCdsProjectReferencesOutputs` replaces the Microsoft default to filter ScriptLibrary and WorkflowActivity references from PAC processing. Then `GenerateVersionNumber` and `ApplyVersionNumber` patch the version across all solution metadata.

### 6. Solution packaging

`PackDataverseSolution` invokes the PAC solution packager to produce the output `.zip`.

### 7. NuGet packing

`dotnet pack` produces a `.nupkg` with the solution `.zip` under `content/solution/`.

## MSBuild Properties

### General

| Property | Default | Description |
|----------|---------|-------------|
| `ProjectType` | `Solution` | Marks the project as a solution for reference discovery. |
| `Version` | _(required)_ | Base version; used for Git versioning and applied to solution.xml and related metadata. |
| `ApplyToBranches` | _(none)_ | Semicolon-separated branch rules (e.g. `master;hotfix;develop:1;pr:3;feature/*:2`). |
| `LocalBranchBuildVersionNumber` | `0.0.0.1` | Fallback version when Git versioning is not applied. |

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
| `PcfForceUpdate` | _(none)_ | Forwarded to PAC `ProcessCdsProjectReferencesOutputs` to force PCF updates. |

## Related Packages

- **Depends on**: `TALXIS.DevKit.Build.Dataverse.Tasks`, `Microsoft.PowerApps.MSBuild.Solution`
- **Discovers and builds**: `Plugin`, `WorkflowActivity`, `ScriptLibrary`, and `Pcf` projects via `ProjectReference`


