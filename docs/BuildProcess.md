# Build process

This document describes how the TALXIS build packages are layered, how their MSBuild files are distributed through NuGet, and what each project-type package actually wires into a consumer build. For the version-numbering rules themselves, see [Versioning.md](./Versioning.md).

## Overview

The repository is organized as three layers:

| Layer | Package(s) | Responsibility |
|---|---|---|
| Core tasks | `TALXIS.DevKit.Build.Dataverse.Tasks` | Ships the task assembly plus reusable MSBuild targets/tasks such as Git version generation, XML/JSON validation, solution packaging helpers, assembly merge, CMT data handling, and GenPage helpers. |
| Project-type packages | `...Solution`, `...PdPackage`, `...Plugin`, `...WorkflowActivity`, `...Pcf`, `...ScriptLibrary`, `...CodeApp`, `...GenPage` | Add the build hooks for one specific `ProjectType`. Most of them depend on the Tasks package and, where relevant, on Microsoft Power Apps MSBuild packages. |
| SDK | `TALXIS.DevKit.Build.Sdk` | Entry point for consumers. It resolves `TALXIS.DevKit.Build.Dataverse.$(ProjectType)` and adds it as a package reference automatically. |

## How package resolution works

A project using the SDK sets `ProjectType` and uses `TALXIS.DevKit.Build.Sdk` as its MSBuild SDK. `src/Sdk/Sdk/Sdk.targets` computes the package name as:

`TALXIS.DevKit.Build.Dataverse.$(ProjectType)`

and adds it as a `PackageReference` with the same version as the SDK package itself. The version is derived from the SDK package directory in the NuGet cache, so the SDK and the resolved project-type package stay aligned.

The SDK also sets a default `TargetFramework` (`net472` if the consumer does not set one) and enables `GitVersionNumber=true` by default.

## How NuGet makes the build logic load automatically

The automatic wiring relies on NuGet/MSBuild conventions:

- files under `build/` are imported automatically for a **direct** package reference
- files under `buildTransitive/` are imported automatically for **transitive** package references
- the imported `.props` and `.targets` files can then `Import` the real implementation from elsewhere in the package

That is exactly how these packages are built:

### Tasks package

`TALXIS.DevKit.Build.Dataverse.Tasks.csproj` packs:

- `msbuild/build/*.*` -> `build/`
- `msbuild/buildMultiTargeting/*.*` -> `buildMultiTargeting/`
- `msbuild/buildTransitive/*.*` -> `buildTransitive/`
- `msbuild/tasks/**/*` -> `tasks/`
- the compiled task assembly -> `tasks/net10.0/`

Its `build`, `buildMultiTargeting`, and `buildTransitive` files are thin forwarding imports to `tasks/TALXIS.DevKit.Build.Dataverse.Tasks.props` and `.targets`.

The real `tasks/...Tasks.targets` file then:

- imports `Props/*.props` and `Targets/*.targets`
- registers the task assembly with many `UsingTask` entries (`GenerateGitVersion`, `InvokeSolutionPackager`, `ValidateXmlFiles`, `MergeCmtDataXml`, `ValidatePcfDependencies`, `PatchGenPageCompiledCode`, etc.)

### Project-type packages

Each project-type `.nuspec` maps:

- `msbuild\tasks\*.*` -> `tasks/`
- `msbuild\build\*.*` -> `build/`

The `build/*.props` / `build/*.targets` files are again small forwarding imports to the real files in `tasks/`.

Because the project-type packages declare `TALXIS.DevKit.Build.Dataverse.Tasks` as a dependency, the Tasks package arrives transitively. Its `buildTransitive/` files then make the shared task registrations available automatically in the consumer build.

### Practical result

A consumer typically only needs one of:

- `Sdk="TALXIS.DevKit.Build.Sdk"` plus `<ProjectType>...`
- or a direct `PackageReference` to a project-type package

After restore, MSBuild imports the package's `build/*.props`/`build/*.targets`, those import the real `tasks/*.props`/`tasks/*.targets`, and the transitive Tasks package contributes the shared task assembly and common targets.

## The Tasks layer by itself

The Tasks package is mostly a library of callable targets and registered tasks, not a full build pipeline on its own.

The important point is that it does **not** automatically attach versioning/validation/packaging to the normal `Build` target. Targets such as `GenerateVersionNumber`, `ApplyVersionNumber`, `ApplyPluginVersionNumber`, `ApplyPcfVersionNumber`, `ValidateSolutionComponentSchema`, `InitializeSolutionPackagerWorkingDirectory`, and `PackDataverseSolution` are available to be called by higher-level packages or by a consumer project explicitly.

The one built-in hook in this package is `PackCanvasApps`, which runs `BeforeTargets="InitializeSolutionPackagerWorkingDirectory"` and cleans stale `CanvasApps/*.msapp` files when that helper target is used.

## Cross-project orchestration pattern

Several project types cooperate through helper targets rather than through normal .NET compilation alone:

- most project-type packages expose `GetProjectType`
- `Plugin` exposes `GetPluginAssemblyInfo`
- `WorkflowActivity` exposes `GetWorkflowActivityAssemblyInfo`
- `ScriptLibrary` exposes `GetScriptLibraryOutputs` and `GetSuppressedScriptLibraryReferences`
- `CodeApp` exposes `GetCodeAppOutputs`
- `GenPage` exposes `GetGenPageOutputs`

The `Solution` and `PdPackage` packages use these helper targets to classify `ProjectReference` entries and stage the correct outputs into solution/package metadata.

## What each project-type package wires into the build

### Solution

`TALXIS.DevKit.Build.Dataverse.Solution` depends on `Microsoft.PowerApps.MSBuild.Solution` and the shared Tasks package. Its build logic is split across several imported `.targets` files.

Main hooks added by the package:

| Target | Hook | Purpose |
|---|---|---|
| `PatchSolutionXmlWorkingDirectory` | `AfterTargets="CopyCdsSolutionContent"` / `BeforeTargets="ProcessCdsProjectReferencesOutputs"` | Patches copied `Other\Solution.xml` in the metadata working directory. |
| `_PrepareSolutionContentBeforeProcessCds` | `BeforeTargets="ProcessCdsProjectReferencesOutputs"` | Calls a placeholder `ApplyPluginVersionNumberInSolution` target after `CopyCdsSolutionContent`. |
| `_ApplySolutionVersionAfterProcessCds` | `AfterTargets="ProcessCdsProjectReferencesOutputs"` | Runs `GenerateVersionNumber` and `ApplyVersionNumber` for the solution content. |
| `_EnsureCustomizationNodesBeforePackage` | `AfterTargets="ProcessCdsProjectReferencesOutputs"` / `BeforeTargets="PowerAppsPackage"` | Ensures required nodes exist in `Customizations.xml`. |
| `_ValidateDuplicateGuidsBeforePackage` | `AfterTargets="ProcessCdsProjectReferencesOutputs"` / `BeforeTargets="PowerAppsPackage"` | Runs duplicate-GUID validation unless opted out. |
| `_ValidateQuickFindViewsBeforePackage` | `AfterTargets="ProcessCdsProjectReferencesOutputs"` / `BeforeTargets="PowerAppsPackage"` | Runs Quick Find validation unless opted out. |
| `ProcessCdsProjectReferencesOutputs` | `BeforeTargets="PowerAppsPackage"` | Overrides the PAC-stage handling of project-reference outputs. |
| `PowerAppsPackage` | `AfterTargets="AfterBuild"` | Packs the prepared metadata directory into the final solution zip with `InvokeSolutionPackager`. |

Specialized integration targets imported by the Solution package:

- **ScriptLibrary**
  - `ProbeScriptLibraries`
  - `BuildScriptLibraries`
  - `CopyScriptLibrariesToWebResources`
  - `CopyScriptLibrariesToMetadata`
  - detects referenced `ScriptLibrary` projects, removes them from normal reference handling, builds them, resolves Dataverse web-resource names, optionally generates missing `.data.xml`, adds root components to `Solution.xml`, and copies the JS outputs into solution metadata

- **CodeApp**
  - `ProbeCodeApps`
  - `BuildCodeApps`
  - `PrepareCodeAppsSources`
  - `CopyCodeAppsToMetadata`
  - detects referenced `CodeApp` projects, builds them with `RunNodeBuild=true`, adds a CanvasApp root component to `Solution.xml`, generates `.meta.xml`, ensures the `CanvasApps` node exists, and copies `dist` output into `CanvasApps/<publisher>_<AppName>_CodeAppPackages/`

- **GenPage**
  - `ProbeGenPages`
  - `BuildGenPages`
  - `CopyGenPagesToMetadata`
  - detects referenced `GenPage` projects, builds them, ensures the `uxagentprojects` node exists, and generates/copies `uxagentproject.xml`, `page.tsx`, `page.compiled`, and `config.json` metadata

- **Plugin**
  - `ProbePluginLibraries`
  - `BuildPluginLibraries`
  - `AlignPluginAssemblyDataVersions`
  - builds referenced plugin projects before solution packaging, gathers assembly metadata through `GetPluginAssemblyInfo`, creates/updates plugin assembly `.data.xml`, and aligns plugin assembly versions in the staged solution metadata

- **WorkflowActivity**
  - `ProbeWorkflowActivityLibraries`
  - `BuildWorkflowActivityLibraries`
  - `CopyWorkflowActivityDllToMetadata`
  - `AlignWorkflowActivityAssemblyDataVersions`
  - builds referenced workflow activity projects, creates/updates workflow assembly `.data.xml`, copies the workflow DLL into `PluginAssemblies`, and aligns versions in staged metadata

Two details are easy to miss:

1. `ValidateSolutionComponentSchema` exists in the shared Tasks package but is **not** auto-wired by the Solution package.
2. the Solution package overrides PAC-oriented handling by redefining `ProcessCdsProjectReferencesOutputs` and filtering out `ScriptLibrary`, `WorkflowActivity`, and `CodeApp` references for manual staging.

### PdPackage

`TALXIS.DevKit.Build.Dataverse.PdPackage` depends on `Microsoft.PowerApps.MSBuild.PDPackage` and the shared Tasks package. Its `build` files import Microsoft's PDPackage props/targets first, then TALXIS targets on top.

Main hooks:

| Target | Hook | Purpose |
|---|---|---|
| `_OverridePdPackageZipName` | `BeforeTargets="ComputePdPackageOutput"` | Sets the output file name to `$(PackageId)$(PdPackageTargetExt)` when not provided. |
| `_ApplyPdPackageVersionNumber` | `BeforeTargets="BeforeBuild;GenerateNuspec"` | Runs `GenerateVersionNumber` for PDPackage builds/packs. |
| `_DetectPdProjectReferenceTypes` | `BeforeTargets="ResolveProjectReferences"` | Calls `GetProjectType` on referenced projects and marks solution references as non-assembly references. |
| `_GetPdPackageItemsFromPpProjectReferences` | depends on `_DetectPdProjectReferenceTypes` | Calls `GetOutputsForPdPackage` on references and collects `PdSolution` inputs. |
| `_GeneratePdPackageAfterPublish` | `AfterTargets="Publish"` | Generates the `.pdpkg.zip` after publish. |
| `_ValidatePcfDependenciesAfterPackage` | `AfterTargets="_GeneratePdPackageAfterPublish"` | Validates PCF dependencies across solution zips in `PkgAssets`. |

Additional PDPackage-specific processing:

- `_EnsureImportConfigBeforeGenerate` creates a skeleton `ImportConfig.xml` when one is missing
- `_DetectCustomImportConfig` disables auto-generation when a user-supplied `PkgAssets/ImportConfig.xml` already defines `configsolutionfile`
- `_PostProcessImportConfig` post-processes generated import config with annotated solutions and CMT data
- `DiscoverCmtPackages` finds CMT packages by locating directories with `[Content_Types].xml`, `data.xml`, and `data_schema.xml`
- `_ZipCmtPackagesAfterBuild` zips discovered CMT packages after `Build`
- `_PrepareCmtMetadataBeforePublish` merges CMT `data.xml` / `data_schema.xml` into one metadata package and zips it
- `_PublishCmtMetadataAfterComputePublishList` adds that merged CMT zip to publish output

### Plugin

`TALXIS.DevKit.Build.Dataverse.Plugin` depends on `Microsoft.PowerApps.MSBuild.Plugin`, `Microsoft.CrmSdk.CoreAssemblies`, `Microsoft.NETFramework.ReferenceAssemblies`, `ILRepack.Lib.MSBuild.Task`, and the shared Tasks package.

Main hooks:

- imports `Microsoft.PowerApps.VisualStudio.Plugin.props` / `.targets`
- `_PublishPluginAfterBuild` runs `Publish` with `NoBuild=true` after `Build` when `PublishOnBuild=true`
- `_ApplyPluginVersionBeforeBuild` runs before `BeforeBuild`
- `_AssemblyMergePluginDependenciesAfterBuild` runs after `Build` and depends on `AssemblyMergeDependencies`
- `GetPluginAssemblyInfo` exposes plugin metadata to other packages
- sets `IsPackable=false` and hooks `$(BeforePack)` with `_ErrorOnPluginPack`, which raises a hard error before any nuspec/nupkg work starts

It also redirects `ILRepackTargetsFile` to a no-op file so TALXIS controls the merge step instead of ILRepack's default auto-hook.

### WorkflowActivity

`TALXIS.DevKit.Build.Dataverse.WorkflowActivity` is parallel to `Plugin`, but for workflow assemblies.

Main hooks:

- imports `Microsoft.PowerApps.VisualStudio.WorkflowActivity.props` / `.targets`
- `_ApplyWorkflowActivityVersionBeforeBuild` runs before `BeforeBuild`
- `_AssemblyMergeWorkflowActivityDependenciesAfterBuild` runs after `Build` and depends on `AssemblyMergeDependencies`
- `GetWorkflowActivityAssemblyInfo` exposes workflow metadata to other packages
- sets `IsPackable=false` and hooks `$(BeforePack)` with `_ErrorOnWorkflowActivityPack`, which raises a hard error before any nuspec/nupkg work starts

Like the Plugin package, it replaces ILRepack's default auto-hook with a no-op target file and lets TALXIS drive the assembly merge.

### Pcf

`TALXIS.DevKit.Build.Dataverse.Pcf` depends on `Microsoft.PowerApps.MSBuild.Pcf`, `Microsoft.NETFramework.ReferenceAssemblies`, and the shared Tasks package.

Main hooks:

- imports `Microsoft.PowerApps.VisualStudio.Pcf.props` / `.targets`
- `NpmInstall` runs `BeforeTargets="BeforeBuild"`
- `_ApplyPcfVersionBeforeBuild` runs `BeforeTargets="BeforeBuild"` and depends on `NpmInstall`
- `_EnsurePcfStubAssembly` runs before `Publish` / `GetCopyToPublishDirectoryItems` and creates a stub DLL if needed
- `PcfCopyToPublish` runs `AfterTargets="Publish"` and copies PCF output into `out\controls\publish`
- sets `IsPackable=false` and hooks `$(BeforePack)` with `_ErrorOnPcfPack`, which raises a hard error before any nuspec/nupkg work starts

This package also fixes the output layout by setting `AppendTargetFrameworkToOutputPath=false` and `OutputPath=$(ProjectDirectory)\out\controls`.

### ScriptLibrary

`TALXIS.DevKit.Build.Dataverse.ScriptLibrary` depends only on the shared Tasks package.

Main hooks:

- `CheckScriptLibraryPrereqs`
- `BuildTypeScript` (`BeforeTargets="Build"`)
- `CopyScriptLibraryMainToOutput` (`AfterTargets="Build"`)
- `GetScriptLibraryOutputs`
- `GetSuppressedScriptLibraryReferences`

The package expects TypeScript sources under `$(TypeScriptDir)` (default `TS`), runs `npm install` and `npm run build`, copies the selected main JS file to `$(TargetDir)`, and lets Solution builds query which referenced script libraries are `CompileOnly` and therefore should not be deployed as separate web resources. Standalone `npm` packaging of a ScriptLibrary is planned but not yet implemented, so it does not currently set `IsPackable=false`.

### CodeApp

`TALXIS.DevKit.Build.Dataverse.CodeApp` depends only on the shared Tasks package.

Main hooks:

- `CheckCodeAppPrereqs`
- `BuildCodeApp` (`BeforeTargets="Build"`)
- `CopyCodeAppDist` (`AfterTargets="Build"`)
- `GetCodeAppOutputs`
- `CopyCodeAppDistPublish` (`AfterTargets="Publish"`)

The package runs `npm install` and `npm run build`, expects output under `dist/`, copies it into `$(OutputPath)$(AppName)/` and `$(PublishDir)$(AppName)/`, and exposes the `dist` folder plus `power.config.json` to Solution packaging. CodeApp projects are not standalone components, so the package sets `IsPackable=false` and hooks `$(BeforePack)` with `_ErrorOnCodeAppPack`, which raises a hard error before any nuspec/nupkg work starts.

### GenPage

`TALXIS.DevKit.Build.Dataverse.GenPage` depends only on the shared Tasks package.

Main hooks:

- `CheckGenPagePrereqs`
- `TranspileGenPage` (`BeforeTargets="Build"`)
- `CopyGenPageOutputs` (`AfterTargets="Build"`)
- `GetGenPageOutputs`

The package validates `GenPageId`, transpiles `page.tsx` with `npx ... typescript@5.3.2 tsc`, patches the compiled output into `page.compiled`, then stages `page.tsx`, `page.compiled`, and optional `genpage.config.json` into `$(OutputPath)$(GenPageName)/` for later Solution integration. GenPage projects are not standalone components, so the package sets `IsPackable=false` and hooks `$(BeforePack)` with `_ErrorOnGenPagePack`, which raises a hard error before any nuspec/nupkg work starts.

## `dotnet pack` in this build family

Only **Solution** and **PdPackage** are packable and add custom pack file-inclusion targets. `Solution` uses `_IncludeSolutionZipInPack` with `BeforeTargets="_GetPackageFiles"` and `DependsOnTargets="Build"`, so packing a solution package explicitly reuses the build pipeline that produces the solution zip; it also adds `build/<PackageId>.props`, which declares a `PdSolution` item over the packaged `content/solution/*.zip` for downstream PDPackage consumption. `PdPackage` uses `_IncludePdPackageZipInPack` with `DependsOnTargets="_GeneratePdPackageAfterPublish"`, so its pack path is tied to publish/package generation rather than directly to `Build`. Both also hook `GenerateNuspec` for their version-apply targets, so the generated package version stays correct even under `dotnet pack --no-build`.

`Plugin`, `WorkflowActivity`, `Pcf`, `CodeApp`, and `GenPage` are consumed exclusively via `<ProjectReference>` from a Solution project - they are never published as standalone NuGet packages. Each of these sets `IsPackable=false` and appends its own target to the `$(BeforePack)` property, which MSBuild inserts as the very first step in the `Pack` target's dependency chain. That target raises a hard `<Error>` explaining that the project cannot be packed and should instead be referenced from a Solution project - the error fires before `GenerateNuspec`, `_IntermediatePack`, or any other pack-related work runs, so no `.nuspec`/`.nupkg` is ever produced.

`ScriptLibrary` does not yet set `IsPackable=false` - standalone `npm` packaging is planned for it, so it remains packable (with the default SDK behavior) until that support lands.
