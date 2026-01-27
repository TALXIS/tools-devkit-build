# TALXIS.DevKit.Build.Dataverse.Tasks

MSBuild tasks and targets shared by Dataverse packages.

## MSBuild properties
### Versioning

### Solution packager paths
- `SolutionRootPath` (default `.`): relative path to the solution source root.
- `SolutionPackagerWorkingDirectory` (default `$(IntermediateOutputPath)`): working folder for pack/unpack.
- `SolutionPackagerMetadataWorkingDirectory` (default `$(SolutionPackagerWorkingDirectory)Metadata`): metadata folder used by version update targets.
- `SolutionPackagerLocalizationWorkingDirectory`: optional localization working folder (cleaned by CleanupWorkingDirectory).
- `SolutionPackageLogFilePath` (default `$(IntermediateOutputPath)SolutionPackager.log`): SolutionPackager log path.
- `SolutionPackageZipFilePath` (default `$(OutputPath)$(MSBuildProjectName).zip`): output zip path used by PackDataverseSolution.

### PCF versioning
- `PcfOutputPath`: output directory containing `ControlManifest.xml` (used by ApplyPcfVersionNumber).
