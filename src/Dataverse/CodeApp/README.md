# TALXIS.DevKit.Build.Dataverse.CodeApp

MSBuild integration for Power Apps code-first canvas app projects. The app's JavaScript build runs as part of `dotnet build` (the package.json `build` script, or the SDK's default Vite build without one), the result lands in `$(OutDir)$(AppName)/`, and metadata targets let Solution projects generate `.meta.xml` and package the app into the solution `.zip`. See [NodeDependencies.md](../../../docs/NodeDependencies.md) for tools, Debug/Release behaviour and troubleshooting.

## Installation

```xml
<PackageReference Include="TALXIS.DevKit.Build.Dataverse.CodeApp" Version="0.0.0.1" PrivateAssets="All" />
```

Or use the SDK approach:

```xml
<Project Sdk="TALXIS.DevKit.Build.Sdk/0.0.0.1">
  <PropertyGroup>
    <ProjectType>CodeApp</ProjectType>
    <AppName>myapp</AppName>
  </PropertyGroup>
</Project>
```

## Prerequisites

- Node.js on `PATH`, and the package manager the project uses.
- A `package.json` in the Node root.
- A `power.config.json` describing the app (read by `GenerateCodeAppMetaXml`).

## How It Works

### Build-time targets

1. **CheckCodeAppPrereqs** -- checks that `package.json` exists.
2. **BuildCodeApp** (before `Build`) -- runs the shared `NodeRestore` and `NodeBuild` with `--mode development|production`, `NODE_ENV` and `BUILD_OUTPUT_DIR`. Without a `build` script, the SDK's default Vite build runs (React, Tailwind and the Power Apps Vite plugin are used when installed).
3. **CopyCodeAppDist** (after `Build`) -- empties `$(OutDir)$(AppName)/` and fills it from `BUILD_OUTPUT_DIR`, or from `dist/` for builds that ignore it. Fails when no `index.html` was produced or `AppName` is not set. In production builds the JavaScript files are checked for inline source maps and size.
4. **CopyCodeAppDistPublish** (after `Publish`) -- copies the same folder to `$(PublishDir)$(AppName)/`.

### Integration targets

These targets are called by `TALXIS.DevKit.Build.Dataverse.Solution` when it discovers this project via `ProjectReference`:

- **GetProjectType** -- returns `CodeApp` so the Solution build knows how to handle this reference.
- **GetCodeAppOutputs** (depends on `Build`) -- returns `$(OutDir)$(AppName)` along with `AppName` and `ConfigPath` (location of `power.config.json`) metadata. The Solution project uses this to call `GenerateCodeAppMetaXml` and produce the `.meta.xml` file for PAC packaging.

### What happens in the Solution project

When a Solution project has a `ProjectReference` to a CodeApp project, the following happens automatically during solution build:

1. **ProbeCodeApps** discovers the CodeApp reference by calling `GetProjectType`.
2. **BuildCodeApps** calls `GetCodeAppOutputs`, which triggers the full CodeApp build (Node dependency restore + build).
3. **PrepareCodeAppsSources** generates `.meta.xml` via `GenerateCodeAppMetaXml`, adds a `RootComponent` entry (Type 300) to `Solution.xml`, and ensures the `CanvasApps` node exists in `Customizations.xml`.
4. **CopyCodeAppsToMetadata** copies the CodeApp dist output into the solution metadata `CanvasApps/` folder before PAC packages the solution.

The CodeApp reference is automatically filtered out of the standard `ResolveProjectReferences` pipeline to avoid unnecessary assembly resolution.

## MSBuild Properties

| Property | Default | Description |
|----------|---------|-------------|
| `ProjectType` | `CodeApp` | Marks the project as a code app for reference discovery. |
| `AppName` | _(required)_ | Application name; the output folder name and the schema name in `.meta.xml`. |
| `NodeRootPath` | `.` | Folder with `package.json` and sources, relative to the project. |
| `RunNodeBuild` | `true` when `package.json` exists | `false` skips the Node restore and build. |

The shared Node properties are described in [NodeDependencies.md](../../../docs/NodeDependencies.md#properties). Rush-registered code apps need `--mode` declared in Rush's `command-line.json`.

## power.config.json

The `GenerateCodeAppMetaXml` task reads this file to generate the `.meta.xml` required by PAC. Place it in the project root. The task scans `dist/` for all files, maps extensions to MIME types (50+ extensions supported), and produces `CodeAppPackageUri` entries.
