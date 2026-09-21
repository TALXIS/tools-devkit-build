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
  apiDefinition.swagger.json   (required - the OpenAPI 2.0 definition)
  apiProperties.json           (required - connection parameters, capabilities, policy templates)
  icon.png                     (optional)
  script.csx                   (optional - custom code, a `class Script : ScriptBase`)
```

## Prerequisites

- `apiDefinition.swagger.json` and `apiProperties.json` must exist in the project root. The build fails with a clear error if either is missing.

## How It Works

### Build-time targets

1. **CheckConnectorPrereqs** -- validates that `apiDefinition.swagger.json` and `apiProperties.json` exist in the project root.
2. **ValidateConnectorSwagger** -- a no-op by default; overridden by a later-imported package to add real OpenAPI structural validation (see the swagger validation add-on).

### Integration targets

These targets are called by `TALXIS.DevKit.Build.Dataverse.Solution` when it discovers this project via `ProjectReference`:

- **GetProjectType** -- returns `Connector` so the Solution build knows how to handle this reference.
- **GetConnectorOutputs** (depends on `CheckConnectorPrereqs`, `ValidateConnectorSwagger`) -- returns the resolved paths to the OpenAPI definition, API properties file, icon (if present), and custom-code script (if present), along with the connector's name.

### What happens in the Solution project

When a Solution project has a `ProjectReference` to a Connector project, the following happens automatically during solution build:

1. **ProbeConnectors** discovers the Connector reference by calling `GetProjectType`.
2. **GetConnectorOutputs** resolves the connector's files.
3. The Solution build stages those files into the solution metadata `Connectors/` folder under the Dataverse-required naming, adds a `RootComponent` entry (Type `372`) to `Solution.xml`, and ensures the `Connectors` node exists in `Customizations.xml`.

## MSBuild Properties

| Property | Default | Description |
|----------|---------|--------------|
| `ProjectType` | `Connector` | Marks the project as a connector for reference discovery. |
| `ConnectorName` | Project name | Used to derive the Dataverse connector schema name when staged into a Solution. |
| `ConnectorApiDefinitionPath` | `apiDefinition.swagger.json` in the project root, if present | Override to point at a different OpenAPI definition file. |
| `ConnectorApiPropertiesPath` | `apiProperties.json` in the project root | Override to point at a different API properties file. |
| `ConnectorIconPath` | `icon.png` in the project root, if present | Override to point at a different icon file. |
| `ConnectorScriptPath` | `script.csx` in the project root, if present | Override to point at a different custom-code file. |
