# Node dependency restore

`Pcf`, `ScriptLibrary`, and `CodeApp` projects need `node_modules` hydrated before their JavaScript/TypeScript
build step runs. The SDK does this automatically via a shared `NodeRestore` MSBuild target - it detects the
package manager (npm, pnpm, Yarn, or Bun) and optional orchestrator (currently Rush) from the same marker
files those tools use, then runs the correct install command for local development or CI.

## Design principles

This mechanism exists to let developers of polyglot (.NET + Node) monorepos use one consistent verb set -
`dotnet restore` / `build` / `clean` / `publish` - without needing to know or care which project uses which
underlying Node tool:

- **The `dotnet` CLI is the only interaction surface.** CI/CD pipelines in consumer repos never run a wrapper
  script and never need to "run `rush`/`npm` first, then run `dotnet`" - `dotnet restore`/`build`/`clean`/
  `publish` alone must produce the right outcome, whether a project is pure .NET, pure Node, or both.
- **Incremental and full-repo adoption use the same mechanism, not separate code paths.** A single `.csproj`
  dropped into an otherwise-plain folder (own local `package.json`, no repo-wide orchestrator) and every project
  in a repo sharing one Rush/pnpm/npm installation root both fall out of the same marker walk-up
  (`GetDirectoryNameOfFileAbove`) - the incremental case simply resolves the package-manager root to the project's own
  directory because no marker is found above it.
- **Toolchain roles resolve independently.** `ResolveNodeToolchain` selects one package manager and, separately, an optional orchestrator. Built-in candidates cover npm, pnpm, Yarn, Bun, and Rush; external NuGet packages can add candidates without changing SDK core.
- **Repo-level exclusivity is enforced by the SDK, informed by the tool.** Rush has its own whole-repo,
  fail-fast lock for `update`/`install`/`build`, but several of its phases (the per-user pnpm bootstrap in
  `~/.rush`, the lockfile copies into `common/temp`) run before that lock and are not concurrency-safe - a
  parallel solution-scope restore corrupts them. `NodeRestore` therefore serializes all Rush invocations for a
  workspace behind one named system mutex, and keeps a bounded, exponential-backoff retry on Rush's own
  "already running" condition as a second line of defense for invocations that don't come from this SDK -
  narrow, tool-specific resilience for confirmed failure modes, not a general-purpose mechanism imposed on
  every tool.
- **MSBuild stays the top-level orchestrator.** It decides *when*/*whether* each project builds at all and in
  what order (via project references, `.slnx` build graph, `-m` parallelism). Rush is only ever the primitive
  that the *Node-specific* portion of that work is delegated to (installing dependencies, and - for Rush
  specifically - running the actual Node build step too, to get its content-hash incremental skip and build
  cache) - never the other way around.

## Verb parity

| Verb | Node behavior |
|---|---|
| `dotnet restore` (project **or solution/repo-root**) | Hydrates Node deps via `NodeRestore`, hooked on `AfterTargets="CollectPackageReferences"` - the one per-project target NuGet's solution-level restore reliably invokes for every project, unlike `AfterTargets="Restore"` which only fires for single-project restore. This is what makes a bare `dotnet restore` at the repo root hydrate Node dependencies for every project, not just NuGet ones. |
| `dotnet build` | Hydrates (implicit restore) + builds. Without an owning orchestrator, the selected package manager runs the build script. For Rush-owned projects (`Pcf`/`ScriptLibrary`/`CodeApp`), the build step itself delegates to Rush's own `build` command instead, so Rush's content-hash incremental skip and build cache apply - see "Build delegation to Rush" below. |
| `dotnet clean` | Removes this project's own JS build-output folder only (`dist` for CodeApp, ScriptLibrary's TypeScript output folder). Never touches `node_modules` or any shared workspace state - "clean" and "prune installed deps" are different operations, and removing `node_modules` is a far more expensive, disruptive step than a normal `dotnet clean` should trigger silently. `Pcf` has no new Clean target from this SDK - Microsoft's own `PcfClean` (`npm run clean`) already owns PCF's `out/controls` cleanup. |
| `dotnet publish` | Copies JS build output into the publish directory (existing, unaffected by any of the above). |

`dotnet build --no-restore` still triggers `NodeRestore` - it sits in each project type's Node build chain
(`BuildTypeScript`/`PcfBuild`/`BuildCodeApp`), not only behind NuGet's restore. This is deliberate, not a bug:
worst case it's a cheap no-op via the existing incremental gate (non-Rush) or the Rush up-to-date gate; it never
silently skips Node hydration just because NuGet's own restore step was skipped.

On a cold NuGet cache, the SDK re-evaluates each eligible Node project after package restore downloads the Tasks package, so the same `dotnet restore` invocation can run `NodeRestore`. The anchor computes the Node root locally in that cold-cache evaluation using the same `NodeRootPath` > `TypeScriptDir` > `.` precedence as `ProjectPaths.props`. Existing ScriptLibrary projects that set only `TypeScriptDir` continue to work unchanged.

## Build delegation to Rush

When `NodeOrchestrator` resolves to Rush for a `Pcf`/`ScriptLibrary`/`CodeApp` project, the *build* step (not just
dependency hydration) is delegated to Rush's own `install-run-rush.js build`, instead of calling `npm run build`
directly - this is what actually lets Rush's per-project content-hash incremental skip and build cache apply to
the Node build step. A direct `npm run build` every time has zero incrementality of its own.

The exact command is chosen by MSBuild's own solution-vs-project signal (`$(SolutionPath)`), not a single fixed
choice, because a fixed choice creates one of two different regressions:

- **Standalone build** (`dotnet build a.csproj`, `$(SolutionPath)` unset/`*Undefined*`): uses **scoped**
  `install-run-rush.js build --to .` - builds only this project plus its transitive Rush-graph upstream
  dependencies. This keeps "build the one project I'm working on after a fresh clone" fast on a large Rush repo -
  it does not build the whole registered graph.
- **Solution-scope build** (`dotnet build repo.slnx`, potentially N Rush-registered projects building in
  parallel via `-m`): uses plain **unscoped** `install-run-rush.js build` - whichever project's target reaches
  this branch first builds the entire registered graph via Rush's own internal parallelism; every other
  project's own call to the same command hits Rush's default incremental skip and returns as a fast no-op.
  Avoids serializing N real Node builds behind Rush's one exclusive whole-repo lock, which a per-project-scoped
  call would otherwise do (`rush build` acquires the exact same whole-repo lock as `rush update`/`install`).

A project directory under a Rush marker but not actually listed in `rush.json`'s `projects` array (legitimate
incremental adoption - not every project needs to join Rush's graph on day one) is detected proactively before
either restore or build routes through Rush, and falls back to the independently selected package manager for restore and build, with a visible warning instead.

### PCF-specific: forwarding the build mode as `--build-mode`

One consistent convention drives every project type in this SDK, PCF included: run `dotnet build` for an
unminified, source-mapped build, or `dotnet build --configuration Release` for a minified production build -
the developer never has to think about it or pass any Node-specific flag themselves. This mirrors the exact
Debug/Release convention Microsoft's own `Microsoft.PowerApps.MSBuild.Pcf.props` already established for
`$(PcfBuildMode)`, so PCF's Rush-delegated path reuses `$(PcfBuildMode)` directly rather than introducing a
second, parallel mapping.

Rush's generic `build` command has no built-in mechanism to forward arbitrary CLI arguments through to each
project's own script, so this package's Rush-branch override of `PcfBuild` forwards the mode via Rush's own
documented **custom commands and parameters** feature (`common/config/rush/command-line.json`), with the flag
spelled `--build-mode` (matching `pcf-scripts`' own `--buildMode`, camel-case-expanded by `yargs`).

The mode CLI flag is **opt-in per project type**, because an npm `build` script forwards extra arguments
verbatim to whatever tool it runs, and tools like `tsc` or `rollup` hard-fail on flags they don't recognize:

| Project type | Mode CLI flag | Why |
|---|---|---|
| `Pcf` | `--build-mode` | `pcf-scripts` parses it (`yargs`) |
| `CodeApp` | `--mode` | vite's own CLI flag |
| `ScriptLibrary` | _(none)_ | build script is commonly plain `tsc`/`rollup`; `NODE_ENV` is the only channel |

`NODE_ENV=development|production` is always set on the build invocation for every project type regardless of
the flag, so any build script (including ScriptLibrary's) can branch on it.

**This requires a one-time addition to your repo's `common/config/rush/command-line.json`** - add the following
to its `parameters` array (`associatedCommands` must include both `"build"` and `"rebuild"`):

```json
{
  "parameterKind": "choice",
  "longName": "--build-mode",
  "description": "PCF build mode",
  "associatedCommands": ["build", "rebuild"],
  "alternatives": [
    { "name": "production", "description": "Minified production bundle" },
    { "name": "development", "description": "Source-mapped development bundle" }
  ],
  "defaultValue": "development"
}
```

If a Rush-registered PCF project's `command-line.json` doesn't declare this yet, **the build fails immediately**
with an error naming the missing parameter - it does not silently fall back to Microsoft's un-cached,
un-delegated `<Exec>`. This is deliberate: once a project is Rush-registered, it has unambiguously opted into
Rush build delegation, so a missing declaration is a fixable configuration gap, not a legitimate "not opted in
yet" state (contrast with the `rush.json` registration check above, which *does* fall back gracefully, since a
project simply not yet added to `rush.json` at all is indistinguishable from valid incremental adoption).

Rush's own build cache already incorporates the command-line parameters used into its cache key (a `choice`
parameter with `defaultValue` is always appended, even on a plain `rush build`), so `--build-mode development`
(Debug) and `--build-mode production` (Release) builds of the same project get distinct, correct cache entries
automatically - no extra work needed here.

No `package.json` "build" script translation is needed, and `--out-dir`/`--build-source` are not forwarded at
all: `pcf-scripts`' own default output directory already matches this SDK's `$(PcfOutputPath)` default, and
`--build-source` only affects telemetry. Rush invokes each project's script directly (no `npm run --`
indirection, so no `--` argument terminator is ever involved) and constructs the command line as plain,
space-separated tokens - `pcf-scripts build --build-mode production` - which `yargs`' default camel-case
expansion reliably parses into `argv.buildMode`, exactly as if the flag had been spelled `--buildMode` in the
first place.

**For the build *cache* itself to activate** (as opposed to just delegation, which works either way via Rush's
default incremental "output preservation" skip), each PCF project also needs its own `rush-project.json`
declaring `projectOutputFolderNames` (typically `["out"]`) - this is a separate, recommended-but-not-required
consumer prerequisite; a project missing it still builds correctly through the forwarded parameter, just
without the archived-cache performance benefit.

`Pcf` projects have no new Clean target from this SDK, and this override does not affect `PcfClean`.

## How detection works

`NodeToolchain` walks upward from `$(NodeRootFullPath)` with MSBuild's built-in `GetDirectoryNameOfFileAbove`. Package-manager and orchestrator detection are separate:

| Role | Marker | Value | Priority |
|---|---|---|---|
| Package manager | `pnpm-lock.yaml` | `pnpm` | 200 |
| Package manager | `yarn.lock` | `yarn` | 190 |
| Package manager | `bun.lock` or `bun.lockb` | `bun` | 180 |
| Package manager | `package-lock.json`, or no stronger marker | `npm` | 0 |
| Orchestrator | `rush.json` | `rush` | 300 |

This means a Rush repository normally resolves both its underlying package manager (for example `pnpm`) and `rush`. Rush owns restore/build only for projects registered in `rush.json`; an unregistered project keeps the selected package-manager path.

## Configuration

| Property | Default | Description |
|---|---|---|
| `NodePackageManager` | _(auto)_ | Package manager: `npm`, `pnpm`, `yarn`, `bun`, or `None`. `None` skips dependency hydration. |
| `NodeOrchestrator` | _(auto)_ | Orchestrator: `rush` or `None`. `None` disables orchestrator ownership while retaining package-manager detection. |
| `NodeRestoreCommand` | _(empty)_ | Exact restore command override. It runs from `NodeRootFullPath` on every invocation and suppresses built-in restore providers. |
| `TypeScriptDir` | project directory | Fully supported ScriptLibrary Node-root setting. Existing projects do not need to rename it. Relative values resolve against the project directory, and the evaluated property remains the normalized absolute path as before. |
| `NodeRootPath` | `TypeScriptDir`, then `.` | Cross-project Node-root setting for Pcf, ScriptLibrary, and CodeApp. It takes precedence only when both properties are explicitly supplied. |
| `IsRunningInCI` | _(auto)_ | Selects frozen/reproducible install commands. |

External package-manager detection targets append to `NodePackageManagerDetectDependsOn` and add `NodePackageManagerCandidate` items. External orchestrators use `NodeOrchestratorDetectDependsOn` and `NodeOrchestratorCandidate`. Candidates provide `Priority`, `RootPath`, and `Source`; orchestrators may also set `OwnsRestore` and `OwnsBuild`. The winning items are exposed as `NodeSelectedPackageManager` and `NodeSelectedOrchestrator`, with custom metadata preserved. Providers hook the public `NodeRestore` or `NodeBuild` target with normal `BeforeTargets`/`AfterTargets`, gate on those selected items, and use explicit dependencies for their own internal ordering.

Selection is deterministic: duplicate identities, invalid priorities, equal winning priorities, missing roots, and unmatched explicit values fail with source information. `NodeRestoreCommand` suppresses built-in provider execution and runs only the supplied command.

## Frozen (CI-safe) installs

When `IsRunningInCI` resolves to `true` **and** a lockfile exists at the selected provider root, `NodeRestore`
uses the frozen/reproducible install variant instead of the mutable one:

| Tool | Local / mutable | CI / frozen |
|---|---|---|
| `rush` | `install-run-rush.js update` (scoped `install --to .` on a never-installed workspace - see [Rush specifics](#rush-specifics)) | `install-run-rush.js install` (same scoping rule) |
| `pnpm` | `pnpm install` | `pnpm install --frozen-lockfile` |
| `yarn` (Classic) | `yarn install` | `yarn install --frozen-lockfile` |
| `yarn` (Berry) | `yarn install` | `yarn install` (Berry enforces immutable installs automatically in CI) |
| `bun` | `bun install` | `bun install --frozen-lockfile` |
| `npm` | `npm install` | `npm ci` |

Both conditions matter: `npm ci` (and the other frozen variants) hard-fail when there is no lockfile, so CI
without a committed lockfile intentionally still falls back to the mutable variant with a build warning.
Locally, the frozen variant is never used - `npm ci` in
particular deletes `node_modules` and reinstalls from scratch on every invocation, and fails outright on the
transient `package.json`/lockfile mismatch that's normal mid-edit during local development.

Rush is always treated as "has a lockfile" for this purpose - `install-run-rush.js`'s own `install` vs.
`update` verbs already enforce the same frozen-vs-mutable distinction internally.

## Rush specifics

Rush is not a package manager - it's an installer-owning orchestrator that wraps npm/pnpm/Yarn internally and
must never be bypassed. `NodeRestore` always invokes it via the version-pinned bootstrap script
(`<rush.json directory>/common/scripts/install-run-rush.js`), never a global `rush` binary, so the exact Rush
version pinned in `rush.json` is always what runs.

### Serialization

Rush invocations are serialized behind **one named system mutex per workspace** (held inside the
`ExecWithRetry` task, across all concurrent MSBuild node processes). Rush's own repo lock is fail-fast and
covers only part of its work: the pnpm bootstrap in the per-user `~/.rush` cache and the lockfile copies into
`common/temp` run before that lock and corrupt each other when two invocations overlap - which a parallel
solution-scope restore or build otherwise guarantees. With the mutex, the first invocation does the real work
and every queued one hits Rush's own fast path. The bounded retry on Rush's "already running" message remains
as a second line of defense for invocations that don't come from this SDK.

### Up-to-date gate

Rush is only spawned when there is possibly something to do: when `common/temp/last-install.flag` exists, the
project's `node_modules` exist, and no registered project manifest, Rush configuration file, or pnpm patch is
newer than the flag, `NodeRestore` skips the invocation entirely. A fresh clone, deleted `node_modules`, or
edited dependency/configuration input invokes Rush, which remains authoritative for the detailed state check
and re-links missing project dependencies itself. Partially deleted Rush bootstrap or local-pnpm state fails
with a recovery command; the SDK does not delete shared Rush state.

### Scoped restore

With Rush subspaces enabled, project restore runs `update --to .` locally or `install --to .` in CI from the
project directory. Rush resolves the owning subspace and any required cross-subspace dependency closure.

In a conventional workspace that has never been installed, a standalone project restore uses
`install --to .`; a mutable local restore falls back to a full `update` if that scoped install cannot satisfy
the current lockfile. Installed conventional workspaces and solution-scope restores remain unscoped because
switching between filtered and full install state forces unnecessary reinstalls.

## Once-per-package-manager-root execution (non-Rush tools)

For npm/pnpm/Yarn/Bun, `NodeRestore` runs at the `RootPath` of `NodeSelectedPackageManager` and is gated by an MSBuild
Inputs/Outputs check (package.json + lockfile → a `.node-restore.stamp` file inside that root's
`node_modules`, so deleting `node_modules` re-triggers the install and the stamp can never be
committed; Yarn Berry PnP, which materializes no `node_modules`, keeps the stamp at the root), so the second,
third, ... project in the same build that shares a package-manager root sees the install as already up-to-date and
skips it - the same "once per installation root, not once per project" guarantee `dotnet restore` gives per solution.

Known limitation: concurrent multi-proc MSBuild builds (`dotnet build -m`) of independent projects sharing one
package-manager root can still race to invoke install simultaneously for these tools, since none of npm/pnpm/Yarn/Bun
ship a cross-process lock of their own (Rush does not have this problem - its invocations are serialized behind
the per-workspace mutex, see [Rush specifics](#rush-specifics)).

## No global side effects

`NodeRestore` never installs anything globally on the developer's machine: no `npm install -g`, no
`corepack enable`, no silently bootstrapping a missing tool. If the resolved tool isn't on `PATH`, the install
command itself fails with a normal "command not found"-style error - the same philosophy as `dotnet restore`
erroring on a missing SDK/tool rather than trying to fix the environment.

Rush's own `install-run-rush.js` downloads the `rush.json`-pinned Rush release into Rush's per-user cache
without installing a global npm package or modifying `PATH`.

## Interop with Microsoft's PCF build SDK (`Pcf` package only)

`TALXIS.DevKit.Build.Dataverse.Pcf` depends on Microsoft's official `Microsoft.PowerApps.MSBuild.Pcf` NuGet
package, which ships its own, independent, unconditional `npm install` hook
(`_PcfAutoNpmInstall`/`RestoreNPM`, controlled by the `PcfEnableAutoNpmInstall` MSBuild property - defaults to
`true` in Microsoft's targets). Left alone, that hook would run a second, plain `npm install` on every PCF
build regardless of anything `NodeRestore` does - no package-manager detection, no CI/lockfile awareness.

This package defaults `PcfEnableAutoNpmInstall` to `false` (in `TALXIS.DevKit.Build.Dataverse.Pcf.props`, which
NuGet imports before the consumer's project body and before Microsoft's own default check runs), since
`NodeRestore` already covers the same job with the detection/CI/frozen logic above. If you specifically want
Microsoft's original unconditional `npm install` behavior back instead, set
`<PcfEnableAutoNpmInstall>true</PcfEnableAutoNpmInstall>` in your own project - that value is set early enough
to still win over both defaults.
