# TALXIS.DevKit.Build.Dataverse.ScriptLibrary

MSBuild integration for Dataverse web resource (JavaScript/TypeScript) projects. Automatically restores Node dependencies (auto-detected package manager (npm, pnpm, Yarn, or Bun) plus optional Rush orchestration; see [NodeDependencies.md](../../../docs/NodeDependencies.md)) and runs the package build script through the selected toolchain when a TypeScript project is detected, copies the compiled JS output to the build output directory, and exposes metadata targets that allow Solution projects to discover and integrate script libraries as web resources.

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

When `RunNodeBuild` is `true` (auto-detected from the presence of `package.json` in the configured Node root):

- **Node.js** must be available on `PATH`

The build will fail with a descriptive error if Node.js is missing; package manager resolution/validation is handled by `NodeRestore` based on what it detects.

## How It Works

The package sets `ProjectType` to `ScriptLibrary` and disables `GenerateAssemblyInfo` by default since this is not a traditional .NET assembly project.

### Build-time targets

1. **CheckScriptLibraryPrereqs** -- validates that the configured Node root exists, `package.json` is present, and `node` is on `PATH` (package manager presence is checked by `NodeRestore` itself, since it depends on what's detected).
2. **BuildTypeScript** (runs before `Build`) -- calls the shared `NodeRestore` target (auto-detected package manager) followed by the shared `NodeBuild` target in the configured Node root.
3. **CopyScriptLibraryMainToOutput** (runs after `Build`) -- copies the main JS file from the configured Node root's `build/` directory to the output directory.

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
| `RunNodeBuild` | Auto-detected | Set to `true` to restore Node dependencies via `NodeRestore` and run `NodeBuild`. Defaults to `true` if `package.json` exists in the configured Node root. |
| `NodePackageManager` | Auto-detected | `npm`, `pnpm`, `yarn`, `bun`, or `None`. |
| `NodeOrchestrator` | Auto-detected | `rush` or `None`. |
| `TypeScriptDir` | project directory | Existing ScriptLibrary setting for the Node/TypeScript project root. It remains fully supported; no project migration is required. Relative values such as `<TypeScriptDir>TS</TypeScriptDir>` resolve against the project directory and evaluate to the normalized absolute path. |
| `NodeRootPath` | `TypeScriptDir`, then `.` | Equivalent cross-project Node-root setting. If both are explicitly set, `NodeRootPath` wins. |
| `ScriptLibraryMainFile` | _(none)_ | Main script file path used by consuming targets. |
| `<ProjectReference>` metadata `ScriptLibraryMode` | `Separate` | Controls the relationship to another referenced ScriptLibrary project: `Separate` or `CompileOnly`. See [Cross-ScriptLibrary references](#cross-scriptlibrary-references). |
| `LangVersion` | `latest` | C# language version for the project. |
| `GenerateAssemblyInfo` | `false` | Disables auto-generated assembly info. |

## Related Packages

- **Depends on**: `TALXIS.DevKit.Build.Dataverse.Tasks`
- **Consumed by**: `TALXIS.DevKit.Build.Dataverse.Solution` projects via `ProjectReference`
