# TALXIS.DevKit.Build.Dataverse.GenPage

MSBuild integration for Dataverse generative page (GenPage) projects. Transpiles TSX source files via TypeScript, patches in RuntimeTypes, generates config metadata, and exposes output targets for Solution projects to discover and integrate GenPages as `uxagentprojects`.

## Installation

```xml
<PackageReference Include="TALXIS.DevKit.Build.Dataverse.GenPage" Version="0.0.0.1" PrivateAssets="All" />
```

Or use the SDK approach:

```xml
<Project Sdk="TALXIS.DevKit.Build.Sdk/0.0.0.1">
  <PropertyGroup>
    <ProjectType>GenPage</ProjectType>
    <GenPageId>{your-genpage-guid}</GenPageId>
    <GenPageDataSources>datasource1,datasource2</GenPageDataSources>
  </PropertyGroup>
</Project>
```

## Prerequisites

- **Node.js** must be available on `PATH`
- **npx** must be available on `PATH`

The build will fail with a descriptive error if either is missing.

## How It Works

The package sets `ProjectType` to `GenPage` and disables `GenerateAssemblyInfo` by default since this is not a traditional .NET assembly project.

### Build-time targets

1. **CheckGenPagePrereqs** — validates that the main TSX file exists and `node`/`npx` are on `PATH`.
2. **TranspileGenPage** (runs before `Build`) — executes `tsc` via `npx` to transpile TSX to JS, then patches the output by stripping `RuntimeTypes` imports and prepending `RuntimeTypes.js` content if present.
3. **GenerateGenPageConfig** — generates `config.json` from project properties (`GenPageDataSources`).
4. **CopyGenPageOutputs** (runs after `Build`) — copies `page.tsx`, `page.compiled`, and `config.json` to the output directory.

### Integration targets

Called by `TALXIS.DevKit.Build.Dataverse.Solution` via `ProjectReference`:

- **GetProjectType** — returns `GenPage`.
- **GetGenPageOutputs** — exposes the compiled output folder and metadata for the solution to copy into `uxagentprojects/`.

## MSBuild Properties

| Property | Default | Description |
|----------|---------|-------------|
| `ProjectType` | `GenPage` | Marks the project for reference discovery by Solution projects. |
| `GenPageMainFile` | `page.tsx` | Main TSX source file to transpile. |
| `GenPageName` | `$(MSBuildProjectName)` | Name used for the output folder and metadata. |
| `GenPageId` | _(required)_ | GUID identifying this GenPage in Dataverse. |
| `GenPageDataSources` | _(empty)_ | Comma-separated list of data source identifiers. |
| `LangVersion` | `latest` | C# language version for the project. |
| `GenerateAssemblyInfo` | `false` | Disables auto-generated assembly info. |

## Related Packages

- **Depends on**: `TALXIS.DevKit.Build.Dataverse.Tasks`
- **Consumed by**: `TALXIS.DevKit.Build.Dataverse.Solution` projects via `ProjectReference`
