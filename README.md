# Power Platform MSBuild Tasks

> [!CAUTION]
> This project is currently in a development phase and not ready for production use.
> While we actively use these tools internally, our aim is to share and collaborate with the broader community to refine and enhance their capabilities.
> We are in the process of gradually open-sourcing the code, removing internal dependencies to make it universally applicable.
> At this stage, it serves as a source of inspiration and a basis for collaboration.
> We welcome feedback, suggestions, and contributions through pull requests.

If wish to use this project for your team, please contact us at hello@networg.com for a personalized onboarding experience and customization to meet your specific needs.

## Goal

The primary objective of this NuGet package is to help Power Platform developers customize the MSBuild process for their Dataverse solution components. Customizations are done using a set of build tasks which make development workflows more productive and automated.

## Status

### Supported functionality

The package currently supports a MSBuild Tasks aimed at extending the build process for Power Platform solutions with useful steps:

#### Solution Component Schema Validation
Ensures XML and JSON artifacts comply with expected schemas. Supported components can be found in the ValidationSchema folder. If your build fails and you belive it should be successful create an issue in this repository or send a PR with corrected definitions.

#### Version Number Generator
Generates version numbers based on Git commit history, applying these versions across various solution components including Solution XML, Plugin Assembly Metadata Files, Workflow Activity Groups, Workflow Files, and SdkMessageProcessingStep Files.

### Work in progress

- **Build Number From Dependencies**: Project dependency folders are analyzed for Git changes to be reflected in generated version numbers.
- **Solution Packaging**: Facilitates the use of the PAC CLI for running the solution packager, simplifying the packaging process.

## Getting Started
> [!TIP]  
> You can find demo steps for creating a new solution using PAC CLI and this package [here](https://tntg.cz/repo-init-demo).

> [!WARNING]  
> You may have troubles building `.cdsproj` projects produced by PAC CLI.
> Also adding them to Visual Studio Solutins (.sln files) might not work.
> To work around this you can add the following property to your .cdsproj: `<DefaultProjectTypeGuid>FAE04EC0-301F-11D3-BF4B-00C04F79EFBC</DefaultProjectTypeGuid>`.
> Alternatively you can rename `.cdsproj` extension to `.csproj` and add `Sdk="Microsoft.NET.Sdk"` attribute to the `Project` element in your `.csproj`.

### Using tasks from the package
To integrate these custom MSBuild tasks into your dotnet project, add the following properties to the `.csproj` or `.cdsproj` file of your Dataverse solution project:
```xml
<PropertyGroup>
    <!-- Major and minor version of the solution -->
    <Version>2.3.20000.0</Version>
    <!-- Folder in the project where Dataverse solution is unpacked (PAC CLI users src folder in the init command) -->
    <SolutionRootPath>Declarations</SolutionRootPath>
</PropertyGroup>

```
Then, add a reference to this package to introduce build tasks to your project:
```xml
<ItemGroup>
    <PackageReference Include="TALXIS.DevKit.Build.Dataverse.Tasks" Version="0.*" />
</ItemGroup>
```
> [!WARNING]  
> The reference must be added after
> `PackageReference Include="Microsoft.PowerApps.MSBuild.Solution"`
> because some of Targets defined by the Microsoft package are overriden by this package.


Now you can extend the build process explicitly calling additional tasks during build:

```xml
<Target Name="BuildDataverseSolution" BeforeTargets="Build" Condition="Exists('$(ProjectDir)$(SolutionRootPath)\Other\Solution.xml')">
    <CallTarget Targets="ValidateSolutionComponentSchema"/>
    <CallTarget Targets="GenerateVersionNumber"/>
</Target>
<Target Name="BeforePackDataverseSolution" AfterTargets="ProcessCdsProjectReferencesOutputs" Condition="Exists('$(ProjectDir)$(SolutionRootPath)\Other\Solution.xml')">
    <CallTarget Targets="ApplyVersionNumber"/>
</Target>
```

### Example of a .csproj file

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net462</TargetFramework>
        <AssemblyName>Some.Solution</AssemblyName>
        <!-- Define major and minor version of the solution here -->
        <Version>2.3.20000.0</Version>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="TALXIS.SDK.BuildTargets.Dataverse.Tasks" Version="0.*" />
        <!-- Add other project references like plugins, PCFs and scripts here...
        <ProjectReference Include="..\something_else\dependency.csproj" />
         -->
    </ItemGroup>

    <PropertyGroup>
        <AssemblyName>Some.Solution</AssemblyName>
        <!-- Define major and minor version of the solution here -->
        <Version>2.3.20000.0</Version>
        <!-- Folder in the project where Dataverse solution is unpacked -->
        <SolutionRootPath>Declarations</SolutionRootPath>
    </PropertyGroup>

    <Target Name="BuildDataverseSolution" BeforeTargets="Build" Condition="Exists('$(ProjectDir)$(SolutionRootPath)\Other\Solution.xml')">
        <CallTarget Targets="ValidateSolutionComponentSchema"/>
        <CallTarget Targets="GenerateVersionNumber"/>
    </Target>
    <Target Name="BeforePackDataverseSolution" AfterTargets="ProcessCdsProjectReferencesOutputs" Condition="Exists('$(ProjectDir)$(SolutionRootPath)\Other\Solution.xml')">
        <CallTarget Targets="ApplyVersionNumber"/>
    </Target>
</Project>
```

## Collaboration

We are happy to collaborate with developers and contributors interested in enhancing Power Platform development processes. If you have feedback, suggestions, or would like to contribute, please feel free to submit issues or pull requests.

### Local building and debugging

#### Package project

Run the following terminal command in the folder `MSBuildTasks`:

```
dotnet pack --configuration Release
```

#### Consuming project

Add `nuget.config` file to your Dataverse solution project folder:

```xml
<configuration>
  <packageSources>
    <!-- package source is additive -->
    <add key="LocalBuildTasks" value="/{REPOSITORY PATH}/src/MSBuildTasks/bin/Release/" />
  </packageSources>
</configuration>
```

Clear all cached packages:

> Note that the command below is going to nuke your entire local package cache. It might be wiser to navigate to `.nuget\packages\talxis.devkit.build.dataverse.tasks` and delete only the contents of this folder. If you can't delete it because it is in use, execute `dotnet build-server shutdown` first.

```
dotnet nuget locals --clear all
```

Rebuild the project:

```
dotnet build --no-incremental --force
```

#### Debugging MSBuild logs

Build the target project with:

```
dotnet build -bl
```

The produced logs can be opened in [MSBuild Log Viewer](https://msbuildlog.com/).

## Contact us

For further information or to discuss potential use cases for your team, please reach out to us at hello@networg.com.
