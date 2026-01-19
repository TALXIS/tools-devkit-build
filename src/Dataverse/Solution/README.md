# TALXIS.DevKit.Build.Dataverse.Solution

MSBuild targets for Dataverse solution projects.

## MSBuild properties
- `ProjectType` (default `Solution`): marks the project as a solution for reference discovery.
- `Version` (required): base version; used for Git versioning and applied to solution.xml and related metadata.
- `ApplyToBranches`: semicolon-separated branch rules for Git versioning (example: `master;hotfix;develop:1;pr:3;feature/*:2`).
- `LocalBranchBuildVersionNumber` (default `0.0.0.1`): fallback version when Git versioning is not applied.
- `Managed`: value written to the `<Managed>` element in solution.xml.
- `PublisherName`: value written to the publisher name fields in solution.xml.
- `PublisherPrefix`: value written to solution.xml and used as the web resource name prefix.
- `SolutionRootPath` (default `.`): relative path to the solution source root.
- `SolutionPackagerWorkingDirectory` (default `$(IntermediateOutputPath)`): working folder for solution packager operations.
- `SolutionPackagerMetadataWorkingDirectory` (default `$(SolutionPackagerWorkingDirectory)Metadata`): metadata folder used for version updates.
- `SolutionPackagerLocalizationWorkingDirectory`: optional localization working folder (cleaned by CleanupWorkingDirectory).
- `SolutionPackageLogFilePath` (default `$(IntermediateOutputPath)SolutionPackager.log`): SolutionPackager log path.
- `SolutionPackageZipFilePath` (default `$(OutputPath)$(MSBuildProjectName).zip`): output zip path for pack tasks.
- `WebResourcesDir`: destination folder for script library web resources (default `$(MSBuildProjectDirectory)\$(SolutionRootPath)\WebResources\`).
- `PcfForceUpdate`: forwarded to PAC `ProcessCdsProjectReferencesOutputs` to force PCF updates.
