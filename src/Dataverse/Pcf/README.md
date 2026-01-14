# TALXIS.DevKit.Build.Dataverse.Pcf

MSBuild targets for Dataverse PCF projects.

## MSBuild properties
- `Version` (required): base version; used for Git versioning and applied to assembly/package versions.
- `ApplyToBranches`: semicolon-separated branch rules for Git versioning (example: `master;hotfix;develop:1;pr:3;feature/*:2`).
- `LocalBranchBuildVersionNumber` (default `0.0.0.1`): fallback version when Git versioning is not applied.
