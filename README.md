# Power Platform MSBuild Tasks

> [!CAUTION]
> This project is currently in a development phase and not ready for production use.
> While we actively use these tools internally, our aim is to share and collaborate with the broader community to refine and enhance their capabilities.
> We are in the process of gradually open-sourcing the code, removing internal dependencies to make it universally applicable.
> At this stage, it serves as a source of inspiration and a basis for collaboration.
> We welcome feedback, suggestions, and contributions through pull requests.

If wish to use this project for your team, please contact us at hello@networg.com for a personalized onboarding experience and customization to meet your specific needs.

## Project Goal
The primary objective of this project is to empower Power Platform developers to customize the MSBuild process for their solution components. This customization includes adding useful tasks that streamline development workflows and improve efficiency.

## Supported Functionality
The project currently supports a range of tasks aimed at enhancing the development process for Power Platform solutions:

- **Solution Component Schema Validation**: Ensures XML and JSON artifacts comply with expected schemas.
- **Solution Versioning**: Generates version numbers based on Git commit history, applying these versions across various solution components including Solution XML, Plugin Assembly Metadata Files, Workflow Activity Groups, Workflow Files, and SdkMessageProcessingStep Files.
- **Solution Packaging**: Facilitates the use of the PAC CLI for running the solution packager, simplifying the packaging process.

## Getting Started
To integrate these custom MSBuild tasks into your project, add the following package reference to the `.csproj` file of your solution project:

```xml
<ItemGroup>
    <PackageReference Include="TALXIS.DevKit.Build.Dataverse.Tasks" Version="0.*" />
</ItemGroup>
```

Then, define a target for building your Dataverse solution, specifying the tasks to be executed:

```xml
<Target Name="BuildDataverseSolution" BeforeTargets="Build" Condition="Exists('$(ProjectDir)$(SolutionRootPath)\Other\Solution.xml')">
    <CallTarget Targets="ValidateSolutionComponentSchema"/>
    <CallTarget Targets="GenerateVersionNumber"/>
    <CallTarget Targets="ApplyVersionNumber"/>
</Target>
```
## Collaboration
We are happy to collaborate with developers and contributors interested in enhancing Power Platform development processes. If you have feedback, suggestions, or would like to contribute, please feel free to submit issues or pull requests.

## Contact Us
For further information or to discuss potential use cases for your team, please reach out to us at hello@networg.com.
