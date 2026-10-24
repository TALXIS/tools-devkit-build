# TALXIS.DevKit.Build.Dataverse.Pcf

MSBuild integration for Power Apps Component Framework (PCF) projects. Wraps `Microsoft.PowerApps.MSBuild.Pcf`, runs the control's JavaScript build through the SDK's shared Node targets, and applies Git-based version numbers to the control.

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

The package imports `Microsoft.PowerApps.MSBuild.Pcf` and replaces its `PcfBuild` target:

- **Node build** -- `PcfBuild` runs the shared `NodeRestore` and `NodeBuild`. Dependencies are installed through the detected package manager or Rush, and the control's `build` script (`pcf-scripts build`) gets `--build-mode development` for Debug and `--build-mode production` otherwise. Microsoft's own unconditional `npm install` is turned off (`PcfEnableAutoNpmInstall=false`). See [NodeDependencies.md](../../../docs/NodeDependencies.md).
- **Release safety** -- the bundle is rebuilt whenever the build mode changes, and every non-Debug build rebuilds it (`PcfAlwaysNpmRunBuild=true` unless set), so a development bundle (for example from `npm start watch`) is never packaged into a Release solution. Production bundles are checked for webpack development output and size (`NodeBuildMaxOutputSize`).
- **Versioning** -- `_ApplyPcfVersionAfterBuild` runs after `PcfBuild`: **GenerateVersionNumber** computes the four-part version from `Version` and the Git branch rules (`GitVersionNumberProductionBranches`, `GitVersionNumberBranchPrefixes`), and **ApplyPcfVersionNumber** writes it into the generated `ControlManifest.xml`.

Output goes to `out/controls/<control>/`, Microsoft's PCF layout, which Solution projects pick up.

## MSBuild Properties

| Property | Default | Description |
|----------|---------|-------------|
| `Version` | _(required)_ | Base version (`Major.Minor`); used by Git versioning to produce the full version. See [Versioning](/docs/Versioning.md). |
| `NodeRootPath` | `.` | Folder with `package.json`, relative to the project. |
| `PcfAlwaysNpmRunBuild` | `true` except for Debug | Microsoft's property; `false` lets an unchanged bundle be reused in non-Debug builds. |

The shared Node properties are described in [NodeDependencies.md](../../../docs/NodeDependencies.md#properties).

## Related Packages

- **Depends on**: `TALXIS.DevKit.Build.Dataverse.Tasks`, `Microsoft.PowerApps.MSBuild.Pcf`
- **Consumed by**: `TALXIS.DevKit.Build.Dataverse.Solution` projects via `ProjectReference`


