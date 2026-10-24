# TALXIS.DevKit.Build.Dataverse.ScriptLibrary

MSBuild integration for Dataverse web resource (JavaScript/TypeScript) projects. The JavaScript build runs as part of `dotnet build`: Node dependencies are installed through the detected package manager or Rush, the package.json `build` script runs (or, without one, the SDK's default rollup build), and `$(ScriptLibraryName).js` lands in the build output, where Solution projects pick it up as a web resource. See [NodeDependencies.md](../../../docs/NodeDependencies.md) for tools, Debug/Release behaviour and troubleshooting.

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

Node.js on `PATH`, and the package manager the project uses (npm ships with Node). Projects without a `package.json` skip the Node build and ship a checked-in `$(ScriptLibraryName).js`.

## How It Works

The package sets `ProjectType` to `ScriptLibrary` and disables `GenerateAssemblyInfo` by default since this is not a traditional .NET assembly project.

### Build-time targets

1. **CheckScriptLibraryPrereqs** -- checks that the Node root and its `package.json` exist.
2. **BuildTypeScript** (before `Build`) -- runs the shared `NodeRestore` and `NodeBuild`. The build gets `NODE_ENV` (`development` for Debug, `production` otherwise) and `BUILD_OUTPUT_DIR`. Without a `build` script, the SDK's default rollup build bundles `src/index.ts` (or `src/index.js`) into a UMD bundle named `$(ScriptLibraryName)`.
3. **CopyScriptLibraryMainToOutput** (after `Build`) -- copies `$(ScriptLibraryName).js` from `BUILD_OUTPUT_DIR`, then `dist/`, then `build/` under the Node root to the output directory. A missing file is an error. In production builds the file is checked for inline source maps and size.

### Integration targets

Called by `TALXIS.DevKit.Build.Dataverse.Solution` via `ProjectReference`:

- **GetProjectType** -- returns `ScriptLibrary`.
- **GetScriptLibraryOutputs** -- exposes the compiled JS file path for the solution to copy into `WebResources/`.
- **GetSuppressedScriptLibraryReferences** -- returns the absolute paths of `<ProjectReference>` entries marked `CompileOnly`. Solution uses this to remove those projects from the standalone deployment list (so they don't land in the solution as their own web resources when the consumer only needs their types).

## Cross-ScriptLibrary references

When one ScriptLibrary project references another, the relationship is controlled by the `ScriptLibraryMode` metadata on the `<ProjectReference>`:

```xml
<ItemGroup>
  <ProjectReference Include="..\Shared\Shared.csproj">
    <ScriptLibraryMode>CompileOnly</ScriptLibraryMode>
  </ProjectReference>
</ItemGroup>
```

| Mode | Build-time effect | Runtime effect in Dataverse |
|------|---|---|
| `Separate` (default) | Both projects compile independently. | Each project is deployed as its own web resource. The consuming form must load both, with the referenced library first. |
| `CompileOnly` | The referenced project still builds (so its `.d.ts` is available for TypeScript-side `/// <reference>` resolution), but its `.js` is not deployed by this Solution. | The referenced project is **not** deployed by this Solution. The consumer assumes the library is already loaded in Dataverse via some other deployment path. |

CompileOnly removes the referenced project from the Solution's standalone-deployment list automatically.

> [!NOTE]
> `Separate` is not handled as an explicit value in the targets — the build logic only checks for `CompileOnly`. Anything else (including the literal string `Separate`, an empty value, a typo like `Compile`, or omitting the metadata entirely) falls through as the default behaviour: both projects compile independently and both deploy as their own web resources. The `Separate` keyword in this table is documentation only — there is no validation that would reject unknown values.

## MSBuild Properties

| Property | Default | Description |
|----------|---------|-------------|
| `ProjectType` | `ScriptLibrary` | Marks the project for reference discovery by Solution projects. |
| `ScriptLibraryName` | `main` | Name of the deliverable (`<name>.js`), the web resource and the default build's UMD global. |
| `NodeRootPath` | `.` | Folder with `package.json` and sources, relative to the project. |
| `RunNodeBuild` | `true` when `package.json` exists | `false` skips the Node restore and build. |
| `<ProjectReference>` metadata `ScriptLibraryMode` | `Separate` | Relationship to another referenced ScriptLibrary project: `Separate` or `CompileOnly`. See [Cross-ScriptLibrary references](#cross-scriptlibrary-references). |

The shared Node properties (`NodePackageManager`, `NodeOrchestrator`, `NodeRestoreCommand`, `NodeBuildCommand`, `NodeBuildConfiguration`, `NodeBuildOutputDir`, `NodeBuildMaxOutputSize`) are described in [NodeDependencies.md](../../../docs/NodeDependencies.md#properties).

## Related Packages

- **Depends on**: `TALXIS.DevKit.Build.Dataverse.Tasks`
- **Consumed by**: `TALXIS.DevKit.Build.Dataverse.Solution` projects via `ProjectReference`
