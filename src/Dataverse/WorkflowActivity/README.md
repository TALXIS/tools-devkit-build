# TALXIS.DevKit.Build.Dataverse.WorkflowActivity

MSBuild targets for Dataverse workflowActivity projects.

## MSBuild properties
- `ProjectType` (default `WorkflowActivity`): marks the project as a workflowActivity for reference discovery.
- `Version` (required): base version; major/minor are used for Git versioning and applied to assembly/package versions.
- `ApplyToBranches`: semicolon-separated branch rules for Git versioning (example: `master;hotfix;develop:1;pr:3;feature/*:2`).
- `LocalBranchBuildVersionNumber` (default `0.0.0.1`): fallback version when Git versioning is not applied.
- `WorkflowActivityTargetFramework` (default `$(TargetFramework)` when set, otherwise `net462`): target framework used to locate the compiled workflowActivity DLL.
- `WorkflowActivityPublishFolderName` (default `publish`): publish folder name under `bin\<Configuration>\<TFM>\`.
- `WorkflowActivityAssemblyId`: explicit GUID for the workflowActivity assembly metadata; if empty, a new GUID is generated.
