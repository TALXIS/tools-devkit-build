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

### Project reference filtering

`_DetectPdProjectReferenceTypes` probes all `ProjectReference` items for `GetProjectType`. Solution-type references have `ReferenceOutputAssembly` set to `false` so their DLLs are not included in the package output.

### ILRepack

`DataverseILRepack` (runs after `Build`) merges all non-Microsoft DLLs (excluding reference assemblies and `Newtonsoft.Json`) into the main output assembly using ILRepack.exe. Can be disabled with `DataversePackageRunILRepack=false` or `SkipPackageILRepack=true`.

### CMT package discovery

`TalxisDiscoverCmtPackages` scans for folders containing `[Content_Types].xml` with sibling `data.xml` and `data_schema.xml`. Supports include/exclude filtering via `IncludedCmtPackages`/`ExcludedCmtPackages`.

### CMT package zipping

`TalxisZipCmtPackages` (runs after `Build`) zips each discovered CMT package directory into `CmtPackageOutputDir`.

### CMT metadata merging

`TalxisPrepareCmtPackageMetadata` merges `data.xml` and `data_schema.xml` from all CMT packages into a single combined package, generates `[Content_Types].xml`, zips it, and appends a reference to `ImportConfig.xml`.

### NuGet packing

`dotnet pack` produces a `.nupkg` with the `.pdpkg.zip`.

## MSBuild Properties

### PDPackage

| Property | Default | Description |
|----------|---------|-------------|
| `PdPackageMsBuildVersion` | `1.50.1` | Version of `Microsoft.PowerApps.MSBuild.PDPackage` imported by the package. |
| `GeneratePdPackageOnBuild` | `true` | Runs `GeneratePdPackage` after build/publish. |

### ILRepack

| Property | Default | Description |
|----------|---------|-------------|
| `DataversePackageRunILRepack` | `true` | Runs ILRepack after build. |
| `SkipPackageILRepack` | _(none)_ | Set to `true` to skip ILRepack. |
| `ILRepackVersion` | `2.0.18` | ILRepack NuGet package version. |
| `ILRepackExe` | `$(NuGetPackageRoot)ilrepack\$(ILRepackVersion)\tools\ILRepack.exe` | Path to ILRepack.exe. |
| `ReferencedAssembliesDir` | `$(TargetDir)` | Directory scanned for assemblies to merge. |
| `DataversePackageILRepackKeyFile` | _(none)_ | Strong-name key file passed to ILRepack `/keyfile`. |

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

- **Depends on**: `Microsoft.PowerApps.MSBuild.PDPackage`, `ilrepack`
- **Typically references**: `TALXIS.DevKit.Build.Dataverse.Solution` projects


