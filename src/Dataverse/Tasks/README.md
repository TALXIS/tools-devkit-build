# TALXIS.DevKit.Build.Dataverse.Tasks

Core MSBuild tasks and targets library shared by all `TALXIS.DevKit.Build.Dataverse.*` packages. Provides custom C# MSBuild tasks for Git-based version generation, solution XML patching, solution packaging via PAC CLI, schema validation, CMT data merging, and web resource management. Most users do not reference this package directly -- it is pulled in as a dependency of the higher-level packages (`Plugin`, `Solution`, `Pcf`, etc.).

## Installation

```xml
<PackageReference Include="TALXIS.DevKit.Build.Dataverse.Tasks" Version="0.0.0.1" PrivateAssets="All" />
```

Typically this package is referenced transitively through one of the component packages.

## How It Works

The package ships compiled task assemblies for `net472` and `net6.0`. At build time, the correct assembly is selected based on `MSBuildRuntimeType` (Core vs Full Framework).

### Registered MSBuild tasks

| Category | Tasks |
|----------|-------|
| Versioning | `GenerateGitVersion`, `ApplyVersionNumber`, `ApplyPcfVersionNumber`, `ApplyPluginVersionNumberInSolution` |
| Solution packaging | `InvokeSolutionPackager`, `PatchSolutionXml`, `EnsureCustomizationsNode` |
| Component metadata | `EnsurePluginAssemblyDataXml`, `EnsureWorkflowActivityAssemblyDataXml`, `EnsureWebResourceDataXml`, `AddRootComponentToSolution` |
| Validation | `ValidateSolutionComponentSchema`, `ValidateXmlFiles`, `ValidateJsonFiles` |
| CMT data | `MergeCmtDataXml`, `MergeCmtDataSchemaXml`, `AppendCmtDataFileToImportConfig` |
| Utilities | `RetrieveProjectReferences`, `ResolveWebResourceName` |

### Key targets

- **GenerateVersionNumber** -- requires the `Version` property. Runs `GenerateGitVersion` using the major/minor from `Version`, the current Git branch, and `ApplyToBranches` rules to produce a full four-part version number.
- **ApplyVersionNumber** -- patches the generated version into solution metadata folders (`SolutionXml`, `PluginAssemblies`, `Workflows`, `Controls`, `SdkMessageProcessingSteps`).
- **ApplyPcfVersionNumber** -- updates the version in `ControlManifest.xml` for PCF controls.
- **PackDataverseSolution** -- invokes the PAC solution packager to produce a `.zip` from the working directory.
- **ValidateSolutionComponentSchema** -- validates solution XML files against bundled XSD schemas and JSON files against JSON schemas.
- **InitializeSolutionPackagerWorkingDirectory** -- copies solution source files into the intermediate working directory for packaging.
- **CleanupWorkingDirectory** -- removes temporary localization and working directories after build.

## MSBuild Properties

### Versioning

| Property | Default | Description |
|----------|---------|-------------|
| `Version` | _(required)_ | Base version (`Major.Minor`); used by `GenerateGitVersion` to produce the full version. |
| `ApplyToBranches` | _(none)_ | Semicolon-separated branch rules (e.g. `master;hotfix;develop:1;pr:3;feature/*:2`). |
| `LocalBranchBuildVersionNumber` | `0.0.20000.0` | Fallback version used when the current branch does not match `ApplyToBranches`. |

### Solution packager paths

| Property | Default | Description |
|----------|---------|-------------|
| `SolutionRootPath` | `.` | Relative path to the solution source root. |
| `SolutionPackagerWorkingDirectory` | `$(IntermediateOutputPath)` | Working folder for pack/unpack operations. |
| `SolutionPackagerMetadataWorkingDirectory` | `$(SolutionPackagerWorkingDirectory)Metadata` | Metadata folder used by version update targets. |
| `SolutionPackagerLocalizationWorkingDirectory` | _(none)_ | Optional localization working folder (cleaned by `CleanupWorkingDirectory`). |
| `SolutionPackageLogFilePath` | `$(IntermediateOutputPath)SolutionPackager.log` | SolutionPackager log file path. |
| `SolutionPackageZipFilePath` | `$(OutputPath)$(MSBuildProjectName).zip` | Output path for the packed solution `.zip`. |

### PCF versioning

| Property | Default | Description |
|----------|---------|-------------|
| `PcfOutputPath` | _(none)_ | Output directory containing `ControlManifest.xml` (used by `ApplyPcfVersionNumber`). |

## Related Packages

This is the foundational package in the ecosystem. The following packages depend on it:

- `TALXIS.DevKit.Build.Dataverse.Pcf`
- `TALXIS.DevKit.Build.Dataverse.Plugin`
- `TALXIS.DevKit.Build.Dataverse.WorkflowActivity`
- `TALXIS.DevKit.Build.Dataverse.ScriptLibrary`
- `TALXIS.DevKit.Build.Dataverse.Solution`
- `TALXIS.DevKit.Build.Dataverse.PdPackage`


