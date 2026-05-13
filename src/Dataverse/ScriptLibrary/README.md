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
4. **BundleReferencedScriptLibraries** (runs after `CopyScriptLibraryMainToOutput`) -- concatenates the compiled `.js` of every `<ProjectReference>` marked `<ScriptLibraryMode>Bundle</ScriptLibraryMode>` in front of this project's main `.js`. The combined file replaces the main output, so the standard `GetScriptLibraryOutputs` path picks up the merged content.

### Integration targets

Called by `TALXIS.DevKit.Build.Dataverse.Solution` via `ProjectReference`:

- **GetProjectType** -- returns `ScriptLibrary`.
- **GetScriptLibraryOutputs** -- exposes the compiled JS file path for the solution to copy into `WebResources/`.
- **GetSuppressedScriptLibraryReferences** -- returns the absolute paths of `<ProjectReference>` entries marked `Bundle` or `CompileOnly`. Solution uses this to remove those projects from the standalone deployment list (otherwise they would land in the solution as their own web resources alongside the bundled copy).

## Cross-ScriptLibrary references

When one ScriptLibrary project references another, the relationship is controlled by the `ScriptLibraryMode` metadata on the `<ProjectReference>`:

```xml
<ItemGroup>
  <ProjectReference Include="..\Shared\Shared.csproj">
    <ScriptLibraryMode>Bundle</ScriptLibraryMode>
  </ProjectReference>
</ItemGroup>
```

| Mode | Build-time effect | Runtime effect in Dataverse |
|------|---|---|
| `Separate` (default) | Both projects compile independently. | Each project is deployed as its own web resource. The consuming form must load both, with the referenced library first. |
| `Bundle` | The referenced project builds first; its compiled `.js` is concatenated in front of the consumer's `.js` (see `BundleReferencedScriptLibraries`). | Only one web resource (the consumer) is deployed. The form loads a single script. |
| `CompileOnly` | The referenced project builds (so its `.d.ts` / output is available for TypeScript-side resolution) but its `.js` is not bundled into the consumer's output. | The referenced project is **not** deployed by this Solution. The consumer assumes the library is already loaded in Dataverse via some other deployment path. |

Bundle and CompileOnly remove the referenced project from the Solution's standalone-deployment list automatically.

> [!NOTE]
> `Separate` is not handled as an explicit value in the targets — the build logic only checks for `Bundle` and `CompileOnly`. Anything else (including the literal string `Separate`, an empty value, a typo like `Bundl`, or omitting the metadata entirely) falls through as the default behaviour: both projects compile independently and both deploy as their own web resources. The `Separate` keyword in this table is documentation only — there is no validation that would reject unknown values.

## MSBuild Properties

| Property | Default | Description |
|----------|---------|-------------|
| `ProjectType` | `ScriptLibrary` | Marks the project for reference discovery by Solution projects. |
| `RunNodeBuild` | Auto-detected | Set to `true` to run `npm install` and `npm run build`. Defaults to `true` if `package.json` exists in `TypeScriptDir`. |
| `TypeScriptDir` | `$(MSBuildProjectDirectory)\TS` | Folder containing the TypeScript project (`package.json`, sources). |
| `ScriptLibraryMainFile` | _(none)_ | Main script file path used by consuming targets. |
| `<ProjectReference>` metadata `ScriptLibraryMode` | `Separate` | Controls the relationship to another referenced ScriptLibrary project: `Separate`, `Bundle`, or `CompileOnly`. See [Cross-ScriptLibrary references](#cross-scriptlibrary-references). |
| `LangVersion` | `latest` | C# language version for the project. |
| `GenerateAssemblyInfo` | `false` | Disables auto-generated assembly info. |

## Related Packages

- **Depends on**: `TALXIS.DevKit.Build.Dataverse.Tasks`
- **Consumed by**: `TALXIS.DevKit.Build.Dataverse.Solution` projects via `ProjectReference`


