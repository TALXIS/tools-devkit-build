# TALXIS.DevKit.Build.Dataverse.PdPackage

MSBuild integration for Power Platform Package Deployer (PD) packages. Wraps `Microsoft.PowerApps.MSBuild.PDPackage`, adds ILRepack-based assembly merging for the deployment package DLL, and provides Configuration Migration Tool (CMT) package discovery, metadata merging, and zipping.

## Installation

```xml
<PackageReference Include="TALXIS.DevKit.Build.Dataverse.PdPackage" Version="0.0.0.1" PrivateAssets="All" />
```

Or use the SDK approach:

```xml
<Project Sdk="TALXIS.DevKit.Build.Sdk/0.0.0.1">
  <PropertyGroup>
    <ProjectType>PdPackage</ProjectType>
  </PropertyGroup>
</Project>
```

## How It Works

### Microsoft PDPackage import

Props and targets from `Microsoft.PowerApps.MSBuild.PDPackage` are imported automatically. The version is controlled by `PdPackageMsBuildVersion`.

### Defaults for ProjectReference

All `ProjectReference` items default to `ReferenceOutputAssembly=false` via `ItemDefinitionGroup`. PDPackage projects reference other projects (e.g. Solution) purely for build ordering and packaging, not to consume their output assemblies as compile-time references. Override per-reference by explicitly setting `ReferenceOutputAssembly="true"`.

### .NET Framework references

`System.ComponentModel.Composition` is automatically referenced when targeting .NET Framework. This is required by `Microsoft.CrmSdk.XrmTooling.PackageDeployment` (MEF / `IImportPackageExtension`).

### Project reference filtering

`_DetectPdProjectReferenceTypes` probes all `ProjectReference` items for `GetProjectType`. Solution-type references have `ReferenceOutputAssembly` set to `false` so their DLLs are not included in the package output.

### Assembly merge

`AssemblyMergeDependencies` (runs after `Build` via `_AssemblyMergePackageDependenciesAfterBuild`) merges dependency DLLs into the main output assembly using the shared ILRepack engine from `TALXIS.DevKit.Build.Dataverse.Tasks`. PdPackage defaults to a different exclude list than Plugin/WorkflowActivity — `Newtonsoft.Json` is **not** excluded because the package-deployer runtime doesn't provide it. Can be disabled with `<AssemblyMergeSkip>true</AssemblyMergeSkip>`.

### CMT package discovery

`DiscoverCmtPackages` scans for folders containing `[Content_Types].xml` with sibling `data.xml` and `data_schema.xml`. Supports include/exclude filtering via `IncludedCmtPackages`/`ExcludedCmtPackages`.

### CMT package zipping

`_ZipCmtPackagesAfterBuild` (runs after `Build`) zips each discovered CMT package directory into `CmtPackageOutputDir`.

### CMT metadata merging

`_PrepareCmtMetadataBeforePublish` merges `data.xml` and `data_schema.xml` from all CMT packages into a single combined package, generates `[Content_Types].xml`, zips it, and appends a reference to `ImportConfig.xml`.

### Publishing and NuGet packing

`dotnet publish` is the primary build command. It publishes the project, generates the `.pdpkg.zip` via `GeneratePdPackage`, and then automatically runs `Pack` to produce a `.nupkg` containing the `.pdpkg.zip` (controlled by `GeneratePackageOnPublish`).

## MSBuild Properties

### PDPackage

| Property | Default | Description |
|----------|---------|-------------|
| `PdPackageMsBuildVersion` | `1.50.1` | Version of `Microsoft.PowerApps.MSBuild.PDPackage` imported by the package. |
| `PdPackageTargetFileName` | `$(MSBuildProjectName).pdpkg.zip` | Name of the produced `.pdpkg.zip`. Defaults to the `.csproj` file name (instead of Microsoft's `$(PackageId)`). Set it explicitly to override. |
| `GeneratePdPackageOnBuild` | `true` | Runs `GeneratePdPackage` after publish. |
| `GeneratePackageOnPublish` | `true` | Triggers NuGet pack after `dotnet publish` to produce a `.nupkg` containing the `.pdpkg.zip`. |

### Assembly merge

| Property | Default | Description |
|----------|---------|-------------|
| `AssemblyMergeSkip` | _(unset)_ | When `true`, skips the post-build `AssemblyMergeDependencies` ILRepack step. |
| `AssemblyMergeExcludes` | `mscorlib;netstandard;Microsoft.Xrm.Sdk;Microsoft.Crm.Sdk.Proxy` | Semicolon-separated assembly filenames (without `.dll`) to exclude from merging. Note: PdPackage does **not** exclude `Newtonsoft.Json` by default (unlike Plugin/WorkflowActivity) because the package-deployer runtime doesn't provide it. Prefix patterns `Microsoft.Xrm.Sdk.*` and `System.*` are always excluded. |

### Validation

| Property | Default | Description |
|----------|---------|-------------|
| `SkipPcfDependencyValidation` | _(unset)_ | When `true`, skips the `_ValidatePcfDependenciesAfterPackage` check after publish. |
| `IgnoredPcfPrefixes` | _(unset)_ | Semicolon-separated PCF control prefixes to exclude from dependency validation. |

### CMT packages

| Property | Default | Description |
|----------|---------|-------------|
| `CmtPackageSearchRoot` | Project directory | Root folder scanned for CMT packages. |
| `CmtPackageOutputDir` | `$(TargetDir)\CmtPackages` | Output folder for zipped CMT packages. |
| `IncludedCmtPackages` | _(none)_ | Semicolon-separated package names to include (case-insensitive). |
| `ExcludedCmtPackages` | _(none)_ | Semicolon-separated package names to exclude (case-insensitive). |

### CMT metadata merge

| Property | Default | Description |
|----------|---------|-------------|
| `CmtPackageName` | _(none)_ | Name injected into merged metadata. |
| `CmtMetadataOutputDir` | `$(IntermediateOutputPath)\CmtMetadata\$(CmtMetadataZipName)` | Temp folder for merged metadata. |
| `CmtMetadataZipName` | `$(CmtPackageName)` or `MainCmtPackage` | Name of the merged metadata zip. |
| `CmtMetadataLcid` | _(none)_ | LCID used when appending metadata to ImportConfig. |
| `CmtMetadataUserMapFileName` | _(none)_ | Optional user map file name used in ImportConfig. |
| `CmtImportConfigPath` | _(none)_ | Path to ImportConfig.xml used for metadata injection. |
| `AutoGeneratePdImportConfig` | _(none)_ | When `true`, uses the generated ImportConfig instead of copying a project file. |
| `PdAssetsTargetFolder` | _(none)_ | Target folder under publish assets for the merged metadata zip. |

## Related Packages

- **Depends on**: `Microsoft.PowerApps.MSBuild.PDPackage`, `ILRepack.Lib.MSBuild.Task`, `TALXIS.DevKit.Build.Dataverse.Tasks`
- **Typically references**: `TALXIS.DevKit.Build.Dataverse.Solution` projects


