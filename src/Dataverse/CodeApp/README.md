# TALXIS.DevKit.Build.Dataverse.CodeApp

MSBuild integration for Power Apps code-first canvas app projects. Automates the `npm install` / `npm run build` lifecycle, copies the compiled `dist/` output into the correct location, and exposes metadata targets that allow Solution projects to discover, generate `.meta.xml`, and package canvas apps into the solution `.zip`.

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

- **Node.js** and **npm** must be available in `PATH`.
- A `package.json` must exist in the project root.
- The `npm run build` script must produce output in a `dist/` folder.
- A `power.config.json` file must exist, describing the app schema name and metadata used by `GenerateCodeAppMetaXml`.

## How It Works

### Build-time targets

1. **CheckNodePrereqs** / **NodeInstallPackagesWithLock** / **NodeInstallPackagesWithoutLock** -- shared Node.js targets from `TALXIS.DevKit.Build.Dataverse.Tasks` that validate `package.json`, check `node` / `npm`, and install packages in the project root when `RunNodeBuild` is enabled.
2. **BuildCodeApp** (runs before `Build`, depends on the shared Node.js targets) -- executes `npm run $(NodeBuildScript)` in the project root directory and passes `NODE_ENV` plus `BUILD_OUTPUT_DIR` to the npm script.
3. **CopyCodeAppDist** (runs after `Build`) -- copies the npm build output to `$(OutputPath)$(AppName)\`, preferring `$(NodeBuildOutputDir)` and falling back to `dist/`. Fails the build if no output folder is found or if `AppName` is not set.
4. **CopyCodeAppDistPublish** (runs after `Publish`) -- same as above, but copies to `$(PublishDir)` instead.

### Integration targets

These targets are called by `TALXIS.DevKit.Build.Dataverse.Solution` when it discovers this project via `ProjectReference`:

- **GetProjectType** -- returns `CodeApp` so the Solution build knows how to handle this reference.
- **GetCodeAppOutputs** (depends on `Build`) -- returns the path to the compiled npm output folder, preferring `$(NodeBuildOutputDir)` and falling back to `dist/`, along with `AppName` and `ConfigPath` (location of `power.config.json`) metadata. The Solution project uses this to call `GenerateCodeAppMetaXml` and produce the `.meta.xml` file for PAC packaging.

### What happens in the Solution project

When a Solution project has a `ProjectReference` to a CodeApp project, the following happens automatically during solution build:

1. **ProbeCodeApps** discovers the CodeApp reference by calling `GetProjectType`.
2. **BuildCodeApps** calls `GetCodeAppOutputs`, which triggers the full CodeApp build (npm install + build).
3. **PrepareCodeAppsSources** generates `.meta.xml` via `GenerateCodeAppMetaXml`, adds a `RootComponent` entry (Type 300) to `Solution.xml`, and ensures the `CanvasApps` node exists in `Customizations.xml`.
4. **CopyCodeAppsToMetadata** copies the resolved CodeApp build output into the solution metadata `CanvasApps/` folder before PAC packages the solution.

The CodeApp reference is automatically filtered out of the standard `ResolveProjectReferences` pipeline to avoid unnecessary assembly resolution.

## MSBuild Properties

| Property | Default | Description |
|----------|---------|-------------|
| `ProjectType` | `CodeApp` | Marks the project as a code app for reference discovery. |
| `AppName` | _(required)_ | Application name; used as the output folder name and in `.meta.xml` generation. |
| `RunNodeBuild` | Auto-detected | Set to `true` if `package.json` exists in project root; set explicitly to override. |

## power.config.json

The `GenerateCodeAppMetaXml` task reads this file to generate the `.meta.xml` required by PAC. Place it in the project root. The task scans `dist/` for all files, maps extensions to MIME types (50+ extensions supported), and produces `CodeAppPackageUri` entries.
