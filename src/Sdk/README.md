# TALXIS.DevKit.Build.Sdk

An MSBuild SDK package that simplifies project setup by automatically resolving and referencing the correct `TALXIS.DevKit.Build.Dataverse.*` package based on the `ProjectType` property. Instead of manually adding `PackageReference` entries, projects declare this SDK and set `ProjectType` to have everything wired automatically.

## Installation

This is an MSBuild SDK, used differently from a regular NuGet package.

```xml
<Project Sdk="TALXIS.DevKit.Build.Sdk/0.0.0.1">
  <PropertyGroup>
    <ProjectType>Solution</ProjectType>
  </PropertyGroup>
</Project>
```

## How It Works

- **Sdk.props** imports `Microsoft.NET.Sdk` props, sets `TargetFramework` to `net472` by default (overridable), and defines default values for `TALXISDevKitDataversePackageBase` and `TALXISDevKitDataversePackageVersion`.
- **Sdk.targets** imports `Microsoft.NET.Sdk` targets, then constructs `TALXISDevKitDataversePackageName` from `$(TALXISDevKitDataversePackageBase).$(ProjectType)` when `ProjectType` is set. It adds a `PackageReference` for the resolved package with `PrivateAssets="All"`.

### Supported ProjectType values

`Solution`, `Plugin`, `Pcf`, `ScriptLibrary`, `CodeApp`, `PdPackage`, `WorkflowActivity`

The `TALXISDevKitDataversePackageName` property can be set explicitly to override the auto-resolution for advanced scenarios.

### Key targets

- **ResolveGitBranch** -- resolves the current Git branch name via `git rev-parse --abbrev-ref HEAD` and exposes it as the `$(GitBranch)` property. Does not run automatically; must be called explicitly via `DependsOnTargets="ResolveGitBranch"`.

## MSBuild Properties

| Property | Default | Description |
|----------|---------|-------------|
| `TargetFramework` | `net472` | Default target framework. Override by setting `<TargetFramework>` in your csproj. Not applied if `TargetFrameworks` (multi-targeting) is set. |
| `ProjectType` | _(none)_ | Selects the package to reference (e.g. `Solution`, `Plugin`, `Pcf`). |
| `TALXISDevKitDataversePackageBase` | `TALXIS.DevKit.Build.Dataverse` | Base package name combined with `ProjectType`. |
| `TALXISDevKitDataversePackageVersion` | `0.0.0.1` | Version used in the auto-generated package reference. |
| `TALXISDevKitDataversePackageName` | `$(Base).$(ProjectType)` | Explicit package name; overrides the base + ProjectType combination. |

## Related Packages

This is the entry point to the TALXIS.DevKit.Build ecosystem. Based on `ProjectType`, it references one of:

- `TALXIS.DevKit.Build.Dataverse.Solution`
- `TALXIS.DevKit.Build.Dataverse.Plugin`
- `TALXIS.DevKit.Build.Dataverse.Pcf`
- `TALXIS.DevKit.Build.Dataverse.ScriptLibrary`
- `TALXIS.DevKit.Build.Dataverse.CodeApp`
- `TALXIS.DevKit.Build.Dataverse.PdPackage`
- `TALXIS.DevKit.Build.Dataverse.WorkflowActivity`
