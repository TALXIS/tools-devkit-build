# Tasks package

The [TALXIS.DevKit.Build.Dataverse.Tasks](https://www.nuget.org/packages/TALXIS.DevKit.Build.Dataverse.Tasks) package offers raw targets which you can embed into any kind of project and call them when needed.

## Using tasks from the package
To integrate these custom MSBuild tasks into your dotnet project, add the following properties to the `.csproj` or `.cdsproj` file of your Dataverse solution project:
```xml
<PropertyGroup>
    <!-- Major and minor version of the solution -->
    <Version>2.3</Version>
    <!-- Folder in the project where Dataverse solution is unpacked (PAC CLI users src folder in the init command) -->
    <SolutionRootPath>Declarations</SolutionRootPath>
</PropertyGroup>
```
Then, add a reference to this package to introduce build tasks to your project:
```xml
<ItemGroup>
    <PackageReference Include="TALXIS.DevKit.Build.Dataverse.Tasks" Version="1.0.*" />
</ItemGroup>
```
> [!WARNING]  
> The reference must be added after
> `PackageReference Include="Microsoft.PowerApps.MSBuild.Solution"`
> because some of Targets defined by the Microsoft package are overriden by this package.

Now you can extend the build process explicitly calling additional tasks during build:

### Solution without plugin
```xml
<Target Name="TalxisAfterProcessCdsProjectReferencesOutputs" AfterTargets="ProcessCdsProjectReferencesOutputs" Condition="Exists('$(ProjectDir)$(SolutionRootPath)\Other\Solution.xml')">
    <CallTarget Targets="ValidateSolutionComponentSchema"/>
    <CallTarget Targets="GenerateVersionNumber"/>
    <CallTarget Targets="ApplyVersionNumber"/>
</Target>
```

### Solution with plugin reference
```xml
<!-- This needs to happen after CopyCdsSolutionContent and before ProcessCdsProjectReferencesOutputs, so that .dll mapping inside ProcessCdsProjectReferencesOutputs works -->
<Target Name="TalxisBeforeProcessCdsProjectReferencesOutputs" BeforeTargets="ProcessCdsProjectReferencesOutputs" DependsOnTargets="CopyCdsSolutionContent">
    <CallTarget Targets="ApplyPluginVersionNumberInSolution"/>
</Target>
<!-- Once .dll are copied and we have everything in place, we can update references and solution -->
<Target Name="TalxisAfterProcessCdsProjectReferencesOutputs" AfterTargets="ProcessCdsProjectReferencesOutputs">
    <CallTarget Targets="ValidateSolutionComponentSchema"/>
    <CallTarget Targets="GenerateVersionNumber"/>
    <CallTarget Targets="ApplyVersionNumber"/>
</Target>
```

### PCF project
```xml
<PropertyGroup>
    <Version>1.0</Version>
</PropertyGroup>
<Target Name="TalxisAfterPcfBuild" AfterTargets="PcfBuild">
    <CallTarget Targets="GenerateVersionNumber"/>
    <CallTarget Targets="ApplyPcfVersionNumber"/>
</Target>
```

### Plugin project
```xml
<Target Name="TalxisBeforeBuild" BeforeTargets="BeforeBuild">
    <CallTarget Targets="GenerateVersionNumber"/>
    <CallTarget Targets="ApplyPluginVersionNumber"/>
</Target>
```

## Example of a solution .csproj file

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net462</TargetFramework>
        <AssemblyName>Some.Solution</AssemblyName>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="Microsoft.PowerApps.MSBuild.Solution" Version="1.48.2" />
        <PackageReference Include="TALXIS.SDK.BuildTargets.Dataverse.Tasks" Version="1.0.*" />
        <!-- Add other project references like plugins, PCFs and scripts here...
        <ProjectReference Include="..\something_else\dependency.csproj" />
         -->
    </ItemGroup>

    <PropertyGroup>
        <AssemblyName>Some.Solution</AssemblyName>
        <!-- Define major and minor version of the solution here -->
        <Version>2.3</Version>
        <!-- Folder in the project where Dataverse solution is unpacked -->
        <SolutionRootPath>Declarations</SolutionRootPath>
    </PropertyGroup>

    <Target Name="TalxisAfterProcessCdsProjectReferencesOutputs" AfterTargets="ProcessCdsProjectReferencesOutputs" Condition="Exists('$(ProjectDir)$(SolutionRootPath)\Other\Solution.xml')">
        <CallTarget Targets="ValidateSolutionComponentSchema"/>
        <CallTarget Targets="GenerateVersionNumber"/>
        <CallTarget Targets="ApplyVersionNumber"/>
    </Target>
</Project>
```