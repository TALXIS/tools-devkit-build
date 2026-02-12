TODO: Version nuspec dependnecy in GitHub Actions

# Power Platform MSBuild Tasks

[![NuGet Version](https://img.shields.io/nuget/v/TALXIS.DevKit.Build.Dataverse.Tasks)](https://www.nuget.org/packages/TALXIS.DevKit.Build.Dataverse.Tasks)


> [!WARNING]
> This project is currently in a development phase and not ready for production use.
> While we actively use these tools internally, our aim is to share and collaborate with the broader community to refine and enhance their capabilities.
> We are in the process of gradually open-sourcing the code, removing internal dependencies to make it universally applicable.
> At this stage, it serves as a source of inspiration and a basis for collaboration.
> We welcome feedback, suggestions, and contributions through pull requests.

If wish to use this project for your team, please contact us at hello@networg.com for a personalized onboarding experience and customization to meet your specific needs.

> [!CAUTION]
> Only modify the source code if you use standard platform customization capabilities.
> Manual changes might make the source source code invalid but still importable to Dataverse.
> In some situations, this could cause your environment to become irreversibly corrupted.

## Goal
The primary objective of this NuGet package is to help Power Platform developers customize the MSBuild process (dotnet build) for their Dataverse solution components. Customizations are done using a set of build tasks which make development workflows more productive and automated.

## Status

### Supported functionality

The package currently supports a MSBuild Tasks aimed at extending the build process for Power Platform solutions with useful steps:

#### Solution Component Schema Validation
Ensures XML and JSON artifacts comply with expected schemas. Supported components can be found in the ValidationSchema folder. If your build fails and you belive it should be successful create an issue in this repository or send a PR with corrected definitions.

#### Version Number Generator
Generates version numbers based on Git commit history, applying these versions across various solution components including Solution XML, Plugin Assembly Metadata Files, Workflow Activity Groups, Workflow Files, and SdkMessageProcessingStep Files. See [here](/docs/Versioning.md) for more details.

#### Build Number From Dependencies
Project dependency folders are analyzed for Git changes to be reflected in generated version numbers. See [here](/docs/Versioning.md) for more details.

## Packages

| Package | Description |
|---------|-------------|
| [TALXIS.DevKit.Build.Sdk](src\Sdk\README.md) | MSBuild SDK that auto-resolves the correct package based on `ProjectType`. Entry point for new projects. |
| [TALXIS.DevKit.Build.Dataverse.Tasks](src/Dataverse/Tasks/README.md) | Core MSBuild tasks shared by all packages: Git versioning, schema validation, solution packaging, CMT data merging. |
| [TALXIS.DevKit.Build.Dataverse.Solution](src/Dataverse/Solution/README.md) | Orchestrates the full Dataverse solution build: component discovery, XML patching, PAC solution packager, NuGet packing. |
| [TALXIS.DevKit.Build.Dataverse.Plugin](src/Dataverse/Plugin/README.md) | MSBuild integration for Dataverse plugin assemblies with auto-versioning and metadata exposure for Solution projects. |
| [TALXIS.DevKit.Build.Dataverse.Pcf](src/Dataverse/Pcf/README.md) | MSBuild integration for PCF controls. Wraps `Microsoft.PowerApps.MSBuild.Pcf` with Git-based versioning. |
| [TALXIS.DevKit.Build.Dataverse.WorkflowActivity](src/Dataverse/WorkflowActivity/README.md) | MSBuild integration for custom workflow activity assemblies with auto-versioning and Solution project integration. |
| [TALXIS.DevKit.Build.Dataverse.ScriptLibrary](src/Dataverse/ScriptLibrary/README.md) | Builds TypeScript/JS web resource projects (`npm install` + `npm run build`) and integrates them into Solution builds. |
| [TALXIS.DevKit.Build.Dataverse.PdPackage](src/Dataverse/PDPackage/README.md) | Package Deployer integration with ILRepack assembly merging and CMT metadata merge/zip support. |

## Getting Started
> [!TIP]  
> You can find demo steps for creating a new solution using PAC CLI and this package [here](https://tntg.cz/repo-init-demo).

> [!WARNING]  
> You may have troubles building `.cdsproj` projects produced by PAC CLI.
> Also adding them to Visual Studio Solutins (.sln files) might not work.
> To work around this you can add the following property to your .cdsproj: `<DefaultProjectTypeGuid>FAE04EC0-301F-11D3-BF4B-00C04F79EFBC</DefaultProjectTypeGuid>`.
> Alternatively you can rename `.cdsproj` extension to `.csproj` and add `Sdk="Microsoft.NET.Sdk"` attribute to the `Project` element in your `.csproj`.

### Configure defaults
Defaults can be set per project or per folder via `Directory.Build.props` file. For more information see [here](/docs/Versioning.md)
```xml
<Project>
   <PropertyGroup>
      <ApplyToBranches>master:1;main:1;develop:2;pr/*:3</ApplyToBranches>
      <LocalBranchBuildVersionNumber>0.0.12345.0</LocalBranchBuildVersionNumber>
   </PropertyGroup>
</Project>
```

### Using ready-made packages

For fastest setup, use `TALXIS.DevKit.Build.Dataverse.Solution`, `TALXIS.DevKit.Build.Dataverse.Plugin` and `TALXIS.DevKit.Build.Dataverse.Pcf` packages to replace the `Microsoft.PowerApps.MSBuild.*` packages and have everything already wired in (the version is locked to the latest known compatible version).

### Using TALXIS.DevKit.Build.Dataverse.Tasks

You can use the raw targets to integrate with your existing setup. See [here](/docs/Tasks-Package.md).

### Using with CI/CD
This action should work in any cloud build environment, as long as you clone the entire Git repository with deep clone.

With GitHub actions:
```yml
- uses: actions/checkout@v2
  with:
    fetch-depth: 0
```

With Azure Pipelines:
```yml
- uses: actions/checkout@v2
  with:
    fetch-depth: 0
```

## Collaboration

See [Developing](/docs/Developing.md).

### Work in progress

- **Solution Packaging**: Facilitates the use of the PAC CLI for running the solution packager, simplifying the packaging process.
    * Currently provided by `Microsoft.PowerApps.MSBuild.Solution` package.

## Contact us

For further information or to discuss potential use cases for your team, please reach out to us at hello@networg.com.
