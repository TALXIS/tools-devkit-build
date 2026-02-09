# TALXIS.DevKit.Build.Dataverse.ScriptLibrary

MSBuild integration for Dataverse web resource (JavaScript/TypeScript) projects. Automatically runs `npm install` and `npm run build` when a TypeScript project is detected, copies the compiled JS output to the build output directory, and exposes metadata targets that allow Solution projects to discover and integrate script libraries as web resources.

## Installation

```xml
<PackageReference Include="TALXIS.DevKit.Build.Dataverse.ScriptLibrary" Version="0.0.0.1" PrivateAssets="All" />
```

Or use the SDK approach:

```xml
<Project Sdk="TALXIS.DevKit.Build.Sdk/0.0.0.1">
  <PropertyGroup>
    <ProjectType>ScriptLibrary</ProjectType>
  </PropertyGroup>
</Project>
```

## Prerequisites

When `RunNodeBuild` is `true` (auto-detected from the presence of `package.json` in `TypeScriptDir`):

- **Node.js** must be available on `PATH`
- **npm** must be available on `PATH`

The build will fail with a descriptive error if either is missing.

## How It Works

The package sets `ProjectType` to `ScriptLibrary` and disables `GenerateAssemblyInfo` by default since this is not a traditional .NET assembly project.

### Build-time targets

1. **CheckScriptLibraryPrereqs** -- validates that `TypeScriptDir` exists, `package.json` is present, and `node`/`npm` are on `PATH`.
2. **BuildTypeScript** (runs before `Build`) -- executes `npm install` followed by `npm run build` in `TypeScriptDir`.
3. **CopyScriptLibraryMainToOutput** (runs after `Build`) -- copies the main JS file from `TypeScriptDir\build\` to the output directory.

### Integration targets

Called by `TALXIS.DevKit.Build.Dataverse.Solution` via `ProjectReference`:

- **GetProjectType** -- returns `ScriptLibrary`.
- **GetScriptLibraryOutputs** -- exposes the compiled JS file path for the solution to copy into `WebResources/`.

## MSBuild Properties

| Property | Default | Description |
|----------|---------|-------------|
| `ProjectType` | `ScriptLibrary` | Marks the project for reference discovery by Solution projects. |
| `RunNodeBuild` | Auto-detected | Set to `true` to run `npm install` and `npm run build`. Defaults to `true` if `package.json` exists in `TypeScriptDir`. |
| `TypeScriptDir` | `$(MSBuildProjectDirectory)\TS` | Folder containing the TypeScript project (`package.json`, sources). |
| `ScriptLibraryMainFile` | _(none)_ | Main script file path used by consuming targets. |
| `LangVersion` | `latest` | C# language version for the project. |
| `GenerateAssemblyInfo` | `false` | Disables auto-generated assembly info. |

## Related Packages

- **Depends on**: `TALXIS.DevKit.Build.Dataverse.Tasks`
- **Consumed by**: `TALXIS.DevKit.Build.Dataverse.Solution` projects via `ProjectReference`


