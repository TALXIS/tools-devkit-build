# TALXIS.DevKit.Build.Dataverse.Pcf

MSBuild integration for Power Apps Component Framework (PCF) projects. Wraps `Microsoft.PowerApps.MSBuild.Pcf` and adds automatic Git-based version number generation that is applied to the PCF control before build.

## Installation

```xml
<PackageReference Include="TALXIS.DevKit.Build.Dataverse.Pcf" Version="0.0.0.1" PrivateAssets="All" />
```

Or use the SDK approach:

```xml
<Project Sdk="TALXIS.DevKit.Build.Sdk/0.0.0.1">
  <PropertyGroup>
    <ProjectType>Pcf</ProjectType>
  </PropertyGroup>
</Project>
```

## How It Works

The package imports `Microsoft.PowerApps.MSBuild.Pcf` targets as a NuGet dependency and layers versioning on top.

The `TalxisBeforeBuild` target runs before `BeforeBuild` and executes two steps in sequence:

1. **GenerateVersionNumber** (from `Tasks`) -- reads the `Version` property, inspects the current Git branch against `ApplyToBranches` rules, and produces a full four-part version number.
2. **ApplyPluginVersionNumber** -- writes the generated version to `AssemblyVersion`, `FileVersion`, `Version`, and `PackageVersion`.

The Microsoft PCF targets version is controlled by `MicrosoftPowerAppsTargetsVersion` from `Directory.Build.props`.

## MSBuild Properties

| Property | Default | Description |
|----------|---------|-------------|
| `Version` | _(required)_ | Base version (`Major.Minor`); used by Git versioning to produce the full version. |
| `ApplyToBranches` | _(none)_ | Semicolon-separated branch rules (e.g. `master;hotfix;develop:1;pr:3;feature/*:2`). |
| `LocalBranchBuildVersionNumber` | `0.0.0.1` | Fallback version when the current branch does not match `ApplyToBranches`. |

## Related Packages

- **Depends on**: `TALXIS.DevKit.Build.Dataverse.Tasks`, `Microsoft.PowerApps.MSBuild.Pcf`
- **Consumed by**: `TALXIS.DevKit.Build.Dataverse.Solution` projects via `ProjectReference`


