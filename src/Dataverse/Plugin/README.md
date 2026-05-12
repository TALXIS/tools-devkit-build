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

- **TalxisBeforeBuild** (runs before `BeforeBuild`) -- executes `GenerateVersionNumber` followed by `ApplyPluginVersionNumber` to set `AssemblyVersion`, `FileVersion`, `Version`, and `PackageVersion` from Git.
- **TalxisMergePluginDependencies** (runs after `Build`) -- uses [ILRepack](https://github.com/gluck/il-repack) to merge every managed DLL that landed in `$(OutDir)` into the main plugin assembly, so the Dataverse sandbox (which loads a single assembly) can resolve all referenced types without sibling DLLs. Sandbox-provided assemblies are skipped: `Microsoft.Xrm.Sdk*`, `Microsoft.Crm.Sdk.Proxy`, `Newtonsoft.Json`, `System.*`, `mscorlib`, `netstandard`. Idempotent — always reads the raw compiler output from `$(IntermediateOutputPath)` so the target can safely re-run within the same Solution build. Merged types keep their original public names (`Internalize=false`) to preserve Dataverse's reflection-based plugin type detection. Disable per-project with `<TalxisMergePluginDependencies>false</TalxisMergePluginDependencies>`.

### Integration targets

These targets are called by `TALXIS.DevKit.Build.Dataverse.Solution` when it discovers this project via `ProjectReference`:

- **GetProjectType** -- returns `Plugin` so the Solution build knows how to handle this reference.
- **GetPluginAssemblyInfo** -- returns `PluginRootPath`, `PluginAssemblyId`, `TargetFramework`, `PublishFolderName`, and `AssemblyName` for automatic plugin assembly metadata generation in the solution.

## MSBuild Properties

| Property | Default | Description |
|----------|---------|-------------|
| `ProjectType` | `Plugin` | Marks the project as a plugin for reference discovery. |
| `Version` | _(required)_ | Base version; major/minor are used for Git versioning. |
| `ApplyToBranches` | _(none)_ | Semicolon-separated branch rules (e.g. `master;hotfix;develop:1;pr:3;feature/*:2`). |
| `LocalBranchBuildVersionNumber` | `0.0.0.1` | Fallback version when Git versioning is not applied. |
| `PluginTargetFramework` | `$(TargetFramework)` or `net462` | Target framework used to locate the compiled plugin DLL. |
| `PluginPublishFolderName` | `publish` | Publish folder name under `bin\<Configuration>\<TFM>\`. |
| `PluginAssemblyId` | _(auto-generated)_ | Explicit GUID for the plugin assembly metadata; a new GUID is generated if empty. |
| `TalxisMergePluginDependencies` | `true` | When `true`, runs `TalxisMergePluginDependencies` after `Build` to ILRepack referenced DLLs into the plugin assembly. Set to `false` to opt out. |

## Related Packages

- **Depends on**: `TALXIS.DevKit.Build.Dataverse.Tasks`, `Microsoft.PowerApps.MSBuild.Plugin`, `Microsoft.CrmSdk.CoreAssemblies`, `ILRepack.Lib.MSBuild.Task`
- **Consumed by**: `TALXIS.DevKit.Build.Dataverse.Solution` projects via `ProjectReference`


