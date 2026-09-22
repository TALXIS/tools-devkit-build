# TALXIS.DevKit.Build.Dataverse.Connector

MSBuild integration for Power Platform custom connector projects. A connector project is just the flat set of files a custom connector is made of - an OpenAPI definition, an API properties file, an optional icon, and optional custom code - with no compile step of its own. This package exposes metadata targets that allow Solution projects to discover a referenced Connector project and stage its files directly into the packed solution as a `Connector` component (Dataverse root component type `372`).

## Installation

```xml
<PackageReference Include="TALXIS.DevKit.Build.Dataverse.Connector" Version="0.0.0.1" PrivateAssets="All" />
```

Or use the SDK approach:

```xml
<Project Sdk="TALXIS.DevKit.Build.Sdk/0.0.0.1">
  <PropertyGroup>
    <ProjectType>Connector</ProjectType>
  </PropertyGroup>
</Project>
```

## Project layout

A Connector project is flat, with files at the project root - not nested under a Dataverse-internal folder structure. The Dataverse-required naming/nesting is a staging detail the Solution project handles when it packs the referenced connector; it is never something the connector project itself needs to know about.

```
Connectors.MyConnector/
  Connectors.MyConnector.csproj
  apiDefinition.swagger.json   (required - the OpenAPI 2.0 definition; or apiDefinition.swagger.yml, see below)
  apiProperties.json           (required - connection parameters, icon brand color, capabilities)
  icon.png                     (optional)
  script.csx                   (optional - custom code, a `class Script : ScriptBase`)
```

`apiDefinition.swagger.yml` is also accepted in place of `apiDefinition.swagger.json` - it's converted to JSON at build time (see below). If both are present, the `.json` file wins.

## Prerequisites

- An OpenAPI definition (`apiDefinition.swagger.json` or `apiDefinition.swagger.yml`) and `apiProperties.json` must exist in the project root. The build fails with a clear error if neither definition file is present.

## How It Works

### Build-time targets

1. **ConvertConnectorSwaggerYaml** -- when only `apiDefinition.swagger.yml` is present (no `.json`), converts it to JSON at `$(IntermediateOutputPath)apiDefinition.swagger.json` using `Microsoft.OpenApi.YamlReader`.
2. **CheckConnectorPrereqs** -- validates that an OpenAPI definition and `apiProperties.json` exist in the project root.
3. **ValidateConnectorSwagger** -- parses the (possibly just-converted) JSON definition with `Microsoft.OpenApi` and fails the build with a clear, pointer-annotated error on structural problems.

### Integration targets

These targets are called by `TALXIS.DevKit.Build.Dataverse.Solution` when it discovers this project via `ProjectReference`:

- **GetProjectType** -- returns `Connector` so the Solution build knows how to handle this reference.
- **GetConnectorOutputs** (depends on `ConvertConnectorSwaggerYaml`, `CheckConnectorPrereqs`, `ValidateConnectorSwagger`) -- returns the resolved paths to the OpenAPI definition, API properties file, icon (if present), and custom-code script (if present), along with the connector's name.

### What happens in the Solution project

When a Solution project has a `ProjectReference` to a Connector project, the following happens automatically during solution build:

1. **ProbeConnectors** discovers the Connector reference by calling `GetProjectType`.
2. **GetConnectorOutputs** resolves the connector's files.
3. The Solution build stages those files into the solution metadata `Connectors/` folder under the Dataverse-required naming, adds a `RootComponent` entry (Type `372`) to `Solution.xml`, and ensures the `Connectors` node exists in `Customizations.xml`.

The connector's `displayname`/`description` are read from the swagger's own `info.title`/`info.description` when present, falling back to the connector's name/blank otherwise.

## MSBuild Properties

| Property | Default | Description |
|----------|---------|--------------|
| `ProjectType` | `Connector` | Marks the project as a connector for reference discovery. |
| `ConnectorName` | Last dot-segment of the project name (e.g. `Connectors.MyConnector` -> `MyConnector`) | Used to derive the Dataverse connector schema name when staged into a Solution. Must be a valid identifier (letters/digits, starting with a letter) - the build fails with a clear error otherwise. |
| `ConnectorId` | _(none - deterministic name-based GUID)_ | Explicit `connectorid` override. Set it when migrating an existing connector so imports keep updating the same Dataverse record instead of creating a duplicate. |
| `ConnectorApiDefinitionPath` | `apiDefinition.swagger.json` in the project root, if present | Override to point at a different OpenAPI definition file. |
| `ConnectorApiDefinitionYamlPath` | `apiDefinition.swagger.yml` in the project root, if present and no `.json` was found | Override to point at a different YAML definition to convert. |
| `ConnectorApiPropertiesPath` | `apiProperties.json` in the project root | Override to point at a different API properties file. |
| `ConnectorIconPath` | `icon.png` in the project root, if present | Override to point at a different icon file. |
| `ConnectorScriptPath` | `script.csx` in the project root, if present | Override to point at a different custom-code file. |
