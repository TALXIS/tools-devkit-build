# TALXIS.DevKit.Build.Dataverse.PdPackage

MSBuild targets for Dataverse PDPackage projects.

## MSBuild properties
### PDPackage
- `PdPackageMsBuildVersion` (default `1.50.1`): version of `Microsoft.PowerApps.MSBuild.PDPackage` imported by the package.
- `GeneratePdPackageOnBuild` (default `true`): runs `GeneratePdPackage` after build/publish.

### ILRepack
- `DataversePackageRunILRepack` (default `true`): runs ILRepack after build.
- `SkipPackageILRepack`: set to `true` to skip ILRepack.
- `ILRepackVersion` (default `2.0.18`): ILRepack package version.
- `ILRepackExe` (default `$(NuGetPackageRoot)ilrepack\<ILRepackVersion>\tools\ILRepack.exe`): path to ILRepack.exe.
- `ReferencedAssembliesDir` (default `$(TargetDir)`): directory scanned for assemblies to merge.
- `DataversePackageILRepackKeyFile`: strong-name key file passed to ILRepack `/keyfile`.

### Cmt packages
- `CmtPackageSearchRoot` (default project directory): root folder scanned for Cmt packages.
- `CmtPackageOutputDir` (default `<TargetDir>\CmtPackages` or `<OutputPath>\CmtPackages`): output folder for zipped Cmt packages.
- `IncludedCmtPackages`: semicolon-separated package names to include (case-insensitive).
- `ExcludedCmtPackages`: semicolon-separated package names to exclude (case-insensitive).

### Cmt metadata merge
- `CmtPackageName`: name injected into merged metadata.
- `CmtMetadataOutputDir` (default `$(IntermediateOutputPath)\CmtMetadata\<CmtMetadataZipName>`): temp folder for merged metadata.
- `CmtMetadataZipName` (default `$(CmtPackageName)` or `MainCmtPackage`): name of the merged metadata zip.
- `CmtMetadataLcid`: LCID used when appending metadata to ImportConfig.
- `CmtMetadataUserMapFileName`: optional user map file name used in ImportConfig.
- `CmtImportConfigPath`: path to ImportConfig.xml used for metadata injection.
- `AutoGeneratePdImportConfig`: when `true`, uses the generated ImportConfig instead of copying a project file.
- `PdAssetsTargetFolder`: target folder under publish assets for the merged metadata zip.
