# TALXIS.DevKit.Build.Sdk

SDK that wires package references based on `ProjectType`.

## MSBuild properties
- `ProjectType`: selects the package to reference (for example `Solution`, `Plugin`, `Pcf`, `ScriptLibrary`, `PdPackage`, `Tasks`).
- `TALXISDevKitDataversePackageBase` (default `TALXIS.DevKit.Build.Dataverse`): base package name used with `ProjectType`.
- `TALXISDevKitDataversePackageVersion` (default `0.0.0.1`): version used in the package reference.
- `TALXISDevKitDataversePackageName`: explicit package name; overrides the base+ProjectType combination.
