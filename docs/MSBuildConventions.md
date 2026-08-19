# MSBuild Conventions

This document describes naming, layout, and extension conventions used by the TALXIS DevKit Build SDK.

## Naming

| Kind | Pattern | Example |
|---|---|---|
| Public entry target | `<Module>` | `NodeToolchain`, `NodeRestore`, `NodeBuild` |
| Private implementation target | `_<Module><Provider><Verb>` | `_NodeToolchainPnpmDetect`, `_NodeRestoreRushRun` |
| Public property | `<Module><Noun>` | `NodePackageManager`, `NodeOrchestrator` |
| Private property/item | `_<Module><Provider><Noun>` | `_NodeRestoreNpmRootPath`, `_NodeToolchainRushTempRoot` |
| Public extension item | `<Module><Role><Noun>` | `NodePackageManagerCandidate`, `NodeSelectedOrchestrator`, `NodeBuildArgument` |
| Extension dependency property | `<Role>DetectDependsOn` | `NodePackageManagerDetectDependsOn` |

An underscore marks an implementation detail. Consumers may rely on public targets, properties, and extension items, but must not call private targets or inspect private state.

C# task classes use `<Verb><Subject>` and match their `UsingTask` name, for example `ResolveNodeToolchain`, `ResolveRushProject`, and `ExecWithRetry`.

## Node file structure

```text
Targets/
  NodeToolchain.targets          package-manager/orchestrator selection
  NodeToolchain/
    Npm.targets                  npm candidate detection
    Pnpm.targets                 pnpm candidate detection
    Yarn.targets                 Yarn candidate detection
    Bun.targets                  Bun candidate detection
    Rush.targets                 Rush detection and project topology
  NodeRestore.targets            public dependency-hydration entry point
  NodeRestore/
    Npm.targets                  npm command and incremental execution
    Pnpm.targets                 pnpm command and incremental execution
    Yarn.targets                 Yarn command and incremental execution
    Bun.targets                  Bun command and incremental execution
    Rush.targets                 Rush install/update, gate, bootstrap checks
    CustomCommand.targets        NodeRestoreCommand override
    Retry.targets                shared Rush mutex/retry target
  NodeBuild.targets              public Node build entry point
  NodeBuild/
    Npm.targets                  npm build provider
    Pnpm.targets                 pnpm build provider
    Yarn.targets                 Yarn build provider
    Bun.targets                  Bun build provider
    Rush.targets                 build through Rush

Tasks/Node/
  ResolveNodeToolchain.cs        independent role selection
  ResolveRushProject.cs          Rush registration and subspace topology
```

## Node toolchain extension pattern

Package managers and orchestrators are independent roles. A Rush repository can therefore resolve `pnpm` as its package manager and `rush` as its orchestrator.

External NuGet packages extend detection by appending targets to:

- `NodePackageManagerDetectDependsOn`
- `NodeOrchestratorDetectDependsOn`

A detection target adds `NodePackageManagerCandidate` or `NodeOrchestratorCandidate` items. Each item uses its identity as the public value and supplies `Priority`, `RootPath`, and `Source` metadata. Orchestrators set `OwnsRestore` and `OwnsBuild` for the current project; a selected orchestrator with both values `false` is detected but does not own either lifecycle.

```xml
<PropertyGroup>
  <NodeOrchestratorDetectDependsOn>
    $(NodeOrchestratorDetectDependsOn);_ContosoDetect
  </NodeOrchestratorDetectDependsOn>
</PropertyGroup>
<Target Name="_ContosoDetect">
  <ItemGroup Condition="Exists('$(NodeRootFullPath)/contoso.json')">
    <NodeOrchestratorCandidate Include="contoso">
      <Priority>250</Priority>
      <RootPath>$(NodeRootFullPath)</RootPath>
      <OwnsRestore>true</OwnsRestore>
      <OwnsBuild>true</OwnsBuild>
      <Source>$(MSBuildThisFileFullPath)</Source>
    </NodeOrchestratorCandidate>
  </ItemGroup>
</Target>
```

Selection rejects duplicate identities, invalid priorities, equal winning priorities, missing roots, and explicit values that do not match a registered candidate.

The public `NodeToolchain` target performs resolution. The selected candidates are exposed as read-only `NodeSelectedPackageManager` and `NodeSelectedOrchestrator` items, with all candidate metadata preserved. Providers consume these items but do not add or remove them. Built-in providers use the same normal `BeforeTargets`/`AfterTargets` hooks as external packages.

`NodeBuildArgument` items carry project-type build arguments for every provider:

```xml
<ItemGroup>
  <NodeBuildArgument Include="--mode">
    <Value>$(NodeBuildConfiguration)</Value>
  </NodeBuildArgument>
</ItemGroup>
```

An argument forwarded through Rush additionally supplies `RushParameterName`, the exact custom parameter declared in Rush `command-line.json`. Other providers ignore that metadata.

```xml
<Target Name="_ContosoBuild"
        AfterTargets="NodeBuild"
        Condition="'@(NodeSelectedOrchestrator)' == 'contoso' and '@(NodeSelectedOrchestrator->'%(OwnsBuild)')' == 'true'">
  <Exec WorkingDirectory="@(NodeSelectedOrchestrator->'%(RootPath)')"
        Command="contoso build @(NodeBuildArgument->'%(Identity) %(Value)', ' ')" />
</Target>
```

Internal resolve/run ordering should use `DependsOnTargets`; do not create a second lifecycle abstraction.

## Cross-referencing rule

A target name should identify its source: module prefix selects the folder, provider selects the file, and verb identifies the target within that file. For example `_NodeRestoreRushRun` lives in `NodeRestore/Rush.targets`.
