# TALXIS.DevKit.Build.Dataverse.Plugin

MSBuild targets for Dataverse plugin projects.

## MSBuild properties
- `ProjectType` (default `Plugin`): marks the project as a plugin for reference discovery.
- `Version` (required): base version; major/minor are used for Git versioning and applied to assembly/package versions.
- `ApplyToBranches`: semicolon-separated branch rules for Git versioning (example: `master;hotfix;develop:1;pr:3;feature/*:2`).
- `LocalBranchBuildVersionNumber` (default `0.0.0.1`): fallback version when Git versioning is not applied.
- `PluginTargetFramework` (default `$(TargetFramework)` when set, otherwise `net462`): target framework used to locate the compiled plugin DLL.
- `PluginPublishFolderName` (default `publish`): publish folder name under `bin\<Configuration>\<TFM>\`.
- `PluginAssemblyId`: explicit GUID for the plugin assembly metadata; if empty, a new GUID is generated.
