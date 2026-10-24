# MSBuild conventions

Naming, layout and extension rules for the SDK's MSBuild files. `tests/lint.sh` checks the parts marked *checked*.

## Naming

| Kind | Pattern | Example |
|---|---|---|
| Public target | `<Module>` | `NodeToolchain`, `NodeRestore`, `NodeBuild` |
| Private target | `_<Module><Rest>` | `_NodeRestorePackageManagerRun`, `_NodeBuildRushRun` (*checked* for `Node*`) |
| Csproj property | `<ProjectType><Noun>` or `<Module><Noun>` | `ScriptLibraryName`, `NodeRootPath` |
| Private property or item | `_<Module><Noun>` | `_NodeRestoreRootPath` |
| Public extension item | `<Module><Role><Noun>` | `NodePackageManagerCandidate`, `NodeSelectedOrchestrator`, `NodeBuildArgument` |
| Extension dependency property | `<Role>DetectDependsOn` | `NodePackageManagerDetectDependsOn` |
| C# task | `<Verb><Subject>`, same as its `UsingTask` | `ResolveNodeToolchain`, `CheckNodeBuildOutput` |

An underscore marks an implementation detail. Consumers and extensions may use public targets, properties and items;
they must not call private targets or read private state. A csproj-facing property exists only when a real project
needs to override it, and always has a working default.

Every `.props` and `.targets` file must be well-formed XML (*checked*): MSBuild rejects a whole package otherwise. Two
hyphens in a row are not allowed inside an XML comment.

## Node layout

```text
Targets/Node/
  NodeToolchain.targets   resolves the package manager and orchestrator (ResolveNodeToolchain)
  NodeRestore.targets     NodeRestore + the generic package-manager install runner
  NodeBuild.targets       NodeBuild + the generic build runner, output folder and output check
  NodeExec.targets        serialized, retried, cancellable command execution (ExecWithRetry)
  Providers/
    Npm.targets           npm candidate (metadata only)
    Pnpm.targets          pnpm candidate (metadata only)
    Rush.targets          Rush detection, restore and build
  defaults/
    rollup.config.mjs     ScriptLibrary default build
    vite.config.mjs       CodeApp default build

Tasks/Node/
  ResolveNodeToolchain.cs  selects one candidate per role and decides ownership
  ResolveRushProject.cs    Rush registration, subspaces, workspaces (JSONC-tolerant)
  ReadNodePackageJson.cs   whether package.json has a build script
  CheckNodeBuildOutput.cs  production output check
```

The core files name no tool outside comments (*checked*); everything tool-specific is in a provider file. A target in
a build log leads to its file: `_NodeRestore...` is in `NodeRestore.targets` unless its name contains a provider
(`_NodeRestoreRushRun` is in `Providers/Rush.targets`).

## Extension pattern

Package managers and orchestrators are independent roles: a Rush repository has pnpm as its package manager and Rush
as its orchestrator. A provider appends a detection target to `NodePackageManagerDetectDependsOn` or
`NodeOrchestratorDetectDependsOn`; the target adds a candidate item when the tool applies. Built-in providers use
exactly the same mechanism as providers from other packages. [NodeDependencies.md](NodeDependencies.md) lists the
candidate metadata and has a complete example.

Use `DependsOnTargets` for ordering inside a provider. Call a target that must run several times per build with the
`MSBuild` task and explicit `Properties`, never with `CallTarget` (MSBuild runs a target once per property set), and
escape values with `$([MSBuild]::Escape(...))` so semicolons survive.

## Tests

`tests/run.sh` packs the working tree as `0.0.0-local` and runs every `tests/fixtures/<name>/test.sh` against it.
A change to the build logic comes with a fixture that fails before the change and passes after it.
