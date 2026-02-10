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

- **Sdk.props** imports `Microsoft.NET.Sdk` props and defines default values for `TALXISDevKitDataversePackageBase` and `TALXISDevKitDataversePackageVersion`.
- **Sdk.targets** imports `Microsoft.NET.Sdk` targets, then constructs `TALXISDevKitDataversePackageName` from `$(TALXISDevKitDataversePackageBase).$(ProjectType)` when `ProjectType` is set. It adds a `PackageReference` for the resolved package with `PrivateAssets="All"`.

### Supported ProjectType values

`Solution`, `Plugin`, `Pcf`, `ScriptLibrary`, `PdPackage`, `WorkflowActivity`

The `TALXISDevKitDataversePackageName` property can be set explicitly to override the auto-resolution for advanced scenarios.

## MSBuild Properties

| Property | Default | Description |
|----------|---------|-------------|
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
- `TALXIS.DevKit.Build.Dataverse.PdPackage`
- `TALXIS.DevKit.Build.Dataverse.WorkflowActivity`
