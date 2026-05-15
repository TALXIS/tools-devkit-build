# TALXIS.DevKit.Build.Dataverse.WorkflowActivity

MSBuild integration for Dynamics 365 custom workflow activity assembly projects. Mirrors the Plugin package pattern: configures Visual Studio project type GUIDs, applies automatic Git-based versioning, merges referenced managed dependencies into the output DLL via ILRepack so the Dataverse sandbox can load all required types from a single assembly, and exposes metadata targets that allow Solution projects to discover and integrate workflow activity assemblies during build.

## Installation

```xml
<PackageReference Include="TALXIS.DevKit.Build.Dataverse.WorkflowActivity" Version="0.0.0.1" PrivateAssets="All" />
```

Or use the SDK approach:

```xml
<Project Sdk="TALXIS.DevKit.Build.Sdk/0.0.0.1">
  <PropertyGroup>
    <ProjectType>WorkflowActivity</ProjectType>
  </PropertyGroup>
</Project>
```

## How It Works

The package sets `ProjectType` to `WorkflowActivity` and configures `ProjectTypeGuids` for workflow activity recognition in Visual Studio.

### Build-time targets

- **TalxisBeforeBuild** (runs before `BeforeBuild`) -- executes `GenerateVersionNumber` followed by `ApplyPluginVersionNumber` to set `AssemblyVersion`, `FileVersion`, `Version`, and `PackageVersion` from Git.
- **TalxisMergePluginDependencies** (runs after `Build`) -- uses [ILRepack](https://github.com/gluck/il-repack) to merge every managed DLL that landed in `$(OutDir)` into the main workflow activity assembly, so the Dataverse sandbox (which loads a single assembly) can resolve all referenced types without sibling DLLs. Sandbox-provided assemblies are skipped: `Microsoft.Xrm.Sdk*`, `Microsoft.Crm.Sdk.Proxy`, `Newtonsoft.Json`, `System.*`, `mscorlib`, `netstandard`. Idempotent — always reads the raw compiler output from `$(IntermediateOutputPath)` so the target can safely re-run within the same Solution build. Merged types keep their original public names (`Internalize=false`) to preserve Dataverse's reflection-based detection of `CodeActivity` subclasses. Disable per-project with `<TalxisMergePluginDependencies>false</TalxisMergePluginDependencies>`.

### Integration targets

These targets are called by `TALXIS.DevKit.Build.Dataverse.Solution` when it discovers this project via `ProjectReference`:

- **GetProjectType** -- returns `WorkflowActivity` so the Solution build knows how to handle this reference.
- **GetWorkflowActivityAssemblyInfo** -- returns `WorkflowActivityRootPath`, `WorkflowActivityAssemblyId`, `TargetFramework`, `PublishFolderName`, and `AssemblyName` for automatic workflow activity assembly metadata generation in the solution.

## MSBuild Properties

| Property | Default | Description |
|----------|---------|-------------|
| `ProjectType` | `WorkflowActivity` | Marks the project as a workflow activity for reference discovery. |
| `Version` | _(required)_ | Base version; major/minor are used for Git versioning. |
| `ApplyToBranches` | _(none)_ | Semicolon-separated branch rules (e.g. `master;hotfix;develop:1;pr:3;feature/*:2`). |
| `LocalBranchBuildVersionNumber` | `0.0.0.1` | Fallback version when Git versioning is not applied. |
| `WorkflowActivityTargetFramework` | `$(TargetFramework)` or `net462` | Target framework used to locate the compiled workflow activity DLL. |
| `WorkflowActivityPublishFolderName` | `publish` | Publish folder name under `bin\<Configuration>\<TFM>\`. |
| `WorkflowActivityAssemblyId` | _(auto-generated)_ | Explicit GUID for the workflow activity assembly metadata; a new GUID is generated if empty. |
| `TalxisMergePluginDependencies` | `true` | When `true`, runs `TalxisMergePluginDependencies` after `Build` to ILRepack referenced DLLs into the workflow activity assembly. Set to `false` to opt out. |

## Related Packages

- **Depends on**: `TALXIS.DevKit.Build.Dataverse.Tasks`, `Microsoft.PowerApps.MSBuild.Plugin`, `Microsoft.CrmSdk.CoreAssemblies`, `ILRepack.Lib.MSBuild.Task`
- **Consumed by**: `TALXIS.DevKit.Build.Dataverse.Solution` projects via `ProjectReference`


