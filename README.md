# Power Platform MSBuild Tasks

> [!CAUTION]
> This project is currently in a development phase and not ready for production use.
> While we actively use these tools internally, our aim is to share and collaborate with the broader community to refine and enhance their capabilities.
> We are in the process of gradually open-sourcing the code, removing internal dependencies to make it universally applicable.
> At this stage, it serves as a source of inspiration and a basis for collaboration.
> We welcome feedback, suggestions, and contributions through pull requests.

If wish to use this project for your team, please contact us at hello@networg.com for a personalized onboarding experience and customization to meet your specific needs.

## Goal

The primary objective of this NuGet package is to empower Power Platform developers to customize the MSBuild process for their solution components. This customization includes adding useful tasks that streamline development workflows and improve efficiency.

## Supported Functionality

The package currently supports a range of tasks aimed at enhancing the development process for Power Platform solutions:

- **Solution Component Schema Validation**: Ensures XML and JSON artifacts comply with expected schemas.
- **Solution Versioning**: Generates version numbers based on Git commit history, applying these versions across various solution components including Solution XML, Plugin Assembly Metadata Files, Workflow Activity Groups, Workflow Files, and SdkMessageProcessingStep Files.
- **Solution Packaging**: Facilitates the use of the PAC CLI for running the solution packager, simplifying the packaging process.

## Getting Started

> [!WARNING]  
> You may have troubles building `.cdsproj` projects produced by PAC CLI. Also adding them to Visual Studio Solutins (.sln files) might not work. To work around this you can rename `.cdsproj` extension to `.csproj`.

### Using tasks from the package
To integrate these custom MSBuild tasks into your dotnet project, add the following package reference to the `.csproj` file of your Dataverse solution project:

```xml
<ItemGroup>
    <PackageReference Include="TALXIS.DevKit.Build.Dataverse.Tasks" Version="0.*" />
</ItemGroup>
```

Then, you can extend the build process by specifying additional tasks to be executed:

```xml
<Target Name="BuildDataverseSolution" BeforeTargets="Build" Condition="Exists('$(ProjectDir)$(SolutionRootPath)\Other\Solution.xml')">
    <CallTarget Targets="ValidateSolutionComponentSchema"/>
    <CallTarget Targets="GenerateVersionNumber"/>
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
        <PackageReference Include="TALXIS.SDK.BuildTargets.Dataverse.Tasks" Version="3.*" />
        <!-- Add other project references like plugins, PCFs and scripts here...
        <ProjectReference Include="..\something_else\dependency.csproj" />
         -->
    </ItemGroup>

    <PropertyGroup>
        <!-- Folder in the project where Dataverse solution is unpacked -->
        <SolutionRootPath>Declarations</SolutionRootPath>
    </PropertyGroup>

    <Target Name="BuildDataverseSolution" BeforeTargets="Build" Condition="Exists('$(ProjectDir)$(SolutionRootPath)\Other\Solution.xml')">
        <CallTarget Targets="ValidateSolutionComponentSchema"/>
        <CallTarget Targets="GenerateVersionNumber"/>
        <CallTarget Targets="ApplyVersionNumber"/>
    </Target>
</Project>
```

## Collaboration
We are happy to collaborate with developers and contributors interested in enhancing Power Platform development processes. If you have feedback, suggestions, or would like to contribute, please feel free to submit issues or pull requests.

## Contact Us
For further information or to discuss potential use cases for your team, please reach out to us at hello@networg.com.
