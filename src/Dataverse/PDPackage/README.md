# TALXIS.DevKit.Build.Dataverse.PdPackage

MSBuild integration for Power Platform Package Deployer (PD) packages. Wraps `Microsoft.PowerApps.MSBuild.PDPackage` and provides Configuration Migration Tool (CMT) package discovery, metadata merging, and zipping.

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

### ImportConfig auto-generation

`ImportConfig.xml` is not required to exist in the project. If `<AssetsSrcDirectory>ImportConfig.xml` is missing, a skeleton (`<configdatastorage>` with empty `<solutions>`, `<filestoimport>`, `<filesmapstoimport>`) is generated in `$(IntermediateOutputPath)<FolderName>/ImportConfig.xml` before MSBuild validation runs, and `@(PdImportConfig)` is rewired to point at it. `<FolderName>` is read from the `GetImportPackageDataFolderName` property of any `ImportExtension`-derived class found in the project's top-level `.cs` files (default `PkgAssets`).

When `AutoGeneratePdImportConfig` is `true` (default) and the source `ImportConfig.xml` contains no `<configsolutionfile>` entries, the `<solutions>` section is generated automatically from the project's solution references. Both `<ProjectReference>` items pointing at Solution projects and `<PackageReference>` items pointing at `pp-solution` NuGet packages are picked up, and each one produces a `<configsolutionfile>` entry with `requiredimportmode="async"`.

The generated entries appear in the **same order as the references are declared in the `.csproj` file**, walking `<PackageReference>` and `<ProjectReference>` items in document order (interleaved). A `PackageReference` is matched by its package id; a `ProjectReference` is matched by the csproj file name (without extension), which by convention equals the solution unique name. Any `<configsolutionfile>` element that can't be matched against a csproj reference is moved to the end while keeping its relative position.

If you ship a hand-written `PkgAssets/ImportConfig.xml` with explicit `<configsolutionfile>` entries, auto-generation is skipped entirely and your file is used as-is.

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
| `PdPackageMsBuildVersion` | `1.50.1` | Version of `Microsoft.PowerApps.MSBuild.PDPackage` imported by the package. Defined once in the package props, so set it in `Directory.Build.props` or via `-p:`; a value set in the csproj body is too late for the Microsoft props import. |
| `PdPackageTargetFileName` | `$(PackageId).pdpkg.zip` | Name of the produced `.pdpkg.zip`.`$(PackageId)` falls back PackageId → AssemblyName → `.csproj` name. |
| `GeneratePdPackageOnBuild` | `true` | Runs `GeneratePdPackage` after publish. |
| `GeneratePackageOnPublish` | `true` | Triggers NuGet pack after `dotnet publish` to produce a `.nupkg` containing the `.pdpkg.zip`. Set it to `false` in the csproj to skip packing. |

### Validation

| Property | Default | Description |
|----------|---------|-------------|
| `SkipPcfDependencyValidation` | _(unset)_ | When `true`, skips the `_ValidatePcfDependenciesAfterPackage` check after publish. |
| `IgnoredPcfPrefixes` | _(unset)_ | Semicolon-separated PCF control prefixes to exclude from dependency validation. |

### CMT packages

| Property | Default | Description |
|----------|---------|-------------|
| `CmtPackageSearchRoot` | _(none)_ | When set, only CMT packages located under this folder are used. When unset, all CMT packages among the project's `None`/`Content` items are used. |
| `CmtPackageOutputDir` | `$(TargetDir)\CmtPackages` | Output folder for zipped CMT packages. |
| `IncludedCmtPackages` | _(none)_ | Semicolon-separated package names to include (case-insensitive). |
| `ExcludedCmtPackages` | _(none)_ | Semicolon-separated package names to exclude (case-insensitive). |

### CMT metadata merge

| Property | Default | Description |
|----------|---------|-------------|
| `CmtPackageName` | _(none)_ | Name injected into merged metadata. |
| `CmtMetadataOutputDir` | `$(IntermediateOutputPath)\CmtMetadata\$(CmtMetadataZipName)` | Temp folder for merged metadata. |
| `CmtMetadataZipName` | `$(CmtPackageName)` or `MainCmtPackage` | Name of the merged metadata zip. |
| `CmtImportConfigPath` | _(none)_ | Path to ImportConfig.xml used for metadata injection. |
| `AutoGeneratePdImportConfig` | _(none)_ | When `true`, uses the generated ImportConfig instead of copying a project file. |
| `PdAssetsTargetFolder` | _(none)_ | Target folder under publish assets for the merged metadata zip. |

## Related Packages

- **Depends on**: `Microsoft.PowerApps.MSBuild.PDPackage`, `TALXIS.DevKit.Build.Dataverse.Tasks`
- **Typically references**: `TALXIS.DevKit.Build.Dataverse.Solution` projects


