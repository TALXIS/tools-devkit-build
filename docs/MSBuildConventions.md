# MSBuild Conventions

This document describes the naming and structural conventions used throughout the
TALXIS DevKit Build SDK. Following these rules makes the codebase predictable for
anyone reading it — even without prior MSBuild or Power Platform experience.

## Naming

### Targets

| Kind | Pattern | Example |
|------|---------|---------|
| Public entry point | `<Module>` | `NodeRestore`, `PcfBuild` |
| Private implementation | `_<Module><Submodule><Verb>` | `_NodeRestoreRushDetect`, `_NodeBuildDirect` |

- **Module** matches the entry-point `.targets` file: `NodeRestore`, `NodeBuild`, `Pcf`, etc.
- **Submodule** identifies the tool or adapter: `Rush`, `Npm`, `Pnpm`, `Yarn`, `Bun`.
- **Verb** describes the action: `Detect`, `Resolve`, `Run`, `Select`, `Validate`.

### Properties

| Kind | Pattern | Example |
|------|---------|---------|
| Public (consumer-facing) | `<Module><Noun>` | `NodePackageManager`, `NodeBuildConfiguration` |
| Private (internal state) | `_<Module><Submodule><Noun>` | `_NodeRestoreRushScoped`, `_NodeRestoreHasLockfile` |
| Extension point | `<Module>Adapter<Phase>DependsOn` | `NodeRestoreAdapterDetectDependsOn` |

### Underscore prefix rule

`_` means "private implementation detail" — not for consumers to reference, hook into,
or depend on. No underscore means "public API" — stable for `DependsOnTargets`,
`BeforeTargets`, `AfterTargets` usage. This matches Microsoft's own MSBuild SDK convention.

### C# task classes

Task class names equal their `UsingTask TaskName`. The name uses `<Verb><Subject>` form:
`SelectToolAdapter`, `ResolveRushProject`, `ExecWithRetry`.

## File structure

```
Targets/
  NodeRestore.targets              → public target: NodeRestore (entry point)
  NodeRestore/
    Selection.targets              → _NodeRestoreSelect, _NodeRestoreValidateResolvedCommand
    Rush.targets                   → _NodeRestoreRushDetect, _NodeRestoreRushResolve, _NodeRestoreRushRun
    Npm.targets                    → _NodeRestoreNpmDetect, _NodeRestoreNpmResolve, _NodeRestoreNpmRun
    Pnpm.targets                   → _NodeRestorePnpmDetect, ...
    Yarn.targets                   → _NodeRestoreYarnDetect, ...
    Bun.targets                    → _NodeRestoreBunDetect, ...
    CustomCommand.targets          → _NodeRestoreRunCustom
    Retry.targets                  → _NodeExecWithRetry (shared by restore + build)
  NodeBuild.targets                → public target entry (imports below)
  NodeBuild/
    Direct.targets                 → _NodeBuildDirect
    RushDelegation.targets         → _NodeBuildDelegateToRush

Tasks/
  Node/SelectToolAdapter.cs        → generic priority-based adapter selection
  Node/ResolveRushProject.cs       → Rush workspace/subspace topology resolver
  ExecWithRetry.cs                 → command execution with mutex, retry, cancellation
```

### Cross-referencing rule

A contributor reading an MSBuild log should locate the source file from the target name:
1. **Module prefix** → folder (`_NodeRestore*` → `NodeRestore/`)
2. **Submodule** → file (`Rush` → `Rush.targets`)
3. **Verb** → which target inside that file

## Adapter extension pattern

Every package-manager adapter follows the same three-phase contract:

1. **Detect** — register `_NodeRestoreAdapterCandidate` items via `NodeRestoreAdapterDetectDependsOn`
2. **Resolve** — compute `_NodeRestoreResolvedCommand` via `AfterTargets="_NodeRestoreSelect"` + tool condition
3. **Run** — execute the command via `AfterTargets="_NodeRestoreSelect"` + tool condition (stamp-gated)

An adapter registers detection by appending to `NodeRestoreAdapterDetectDependsOn`.
Resolve and run targets hook in via standard MSBuild `AfterTargets` — no other
registration needed. External adapters (shipped as NuGet packages) follow the exact
same pattern as built-in ones.

## Adding a new ecosystem

When Python, Azure Functions, or another ecosystem is added:

```
Targets/
  PythonRestore.targets            → same entry-point structure as NodeRestore.targets
  PythonRestore/
    Selection.targets              → reuses SelectToolAdapter task
    Pip.targets                    → _PythonRestorePipDetect, _PythonRestorePipResolve, _PythonRestorePipRun
    Poetry.targets                 → ...
    Uv.targets                     → ...

Tasks/
  Python/ResolvePipProject.cs      → ecosystem-specific resolver
```

Shared infrastructure (`SelectToolAdapter`, `ExecWithRetry`, `ProjectPaths.props`) is
ecosystem-agnostic and reused as-is.
