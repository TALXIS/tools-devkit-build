# Node dependency restore

`Pcf`, `ScriptLibrary`, and `CodeApp` projects need `node_modules` hydrated before their JavaScript/TypeScript
build step runs. The SDK does this automatically via a shared `NodeRestore` MSBuild target - it detects the
right package manager for the project (npm, pnpm, Yarn, Bun, or Rush) from the same marker files those tools
themselves already use, and runs the correct install command for the situation (local dev vs. CI, mutable vs.
frozen/reproducible).

This replaces the previous behavior of unconditionally running `npm install`, which had three problems:

- **No opt-out** - broke any repo where dependencies are hydrated by a different tool, or by an orchestrator
  like Rush that must never be bypassed (running `pnpm install` directly in a Rush repo corrupts Rush's own
  state).
- **Mutated the lockfile mid-build** - `npm install` reconciles `package-lock.json` to `package.json` and can
  rewrite it (format migration, re-resolved ranges). This breaks any CI cache keyed on the lockfile's hash (the
  key changes between cache restore and cache save, so it can never hit again) and makes installs
  non-reproducible.
- **Always ran, every build** - no once-per-workspace guarantee for monorepos with multiple PCF/ScriptLibrary/CodeApp
  projects sharing one `node_modules`.

## Design principles

This mechanism exists to let developers of polyglot (.NET + Node) monorepos use one consistent verb set -
`dotnet restore` / `build` / `clean` / `publish` - without needing to know or care which project uses which
underlying Node tool:

- **The `dotnet` CLI is the only interaction surface.** CI/CD pipelines in consumer repos never run a wrapper
  script and never need to "run `rush`/`npm` first, then run `dotnet`" - `dotnet restore`/`build`/`clean`/
  `publish` alone must produce the right outcome, whether a project is pure .NET, pure Node, or both.
- **Incremental and full-repo adoption use the same mechanism, not separate code paths.** A single `.csproj`
  dropped into an otherwise-plain folder (own local `package.json`, no repo-wide orchestrator) and every project
  in a repo sharing one Rush/pnpm/npm workspace both fall out of the same marker walk-up
  (`GetDirectoryNameOfFileAbove`) - the incremental case simply resolves the workspace root to the project's own
  directory because no marker is found above it.
- **Rush is the out-of-box-supported orchestrator, not the only one.** Nothing Rush-specific is hardcoded into
  the shared detection/dispatch path; the two escape hatches (`NodePackageManager=<name>` for a tool already in
  the command table, `NodeRestoreCommand=<any command>` for anything else) are the intended extension surface for
  a tool this SDK doesn't ship day-one support for - no plugin/adapter abstraction is introduced.
- **Hand repo-level exclusivity arbitration to the tool that already owns it - don't reinvent it.** Rush already
  has its own whole-repo, fail-fast lock for `update`/`install`/`build`. Rather than building a new generic lock
  file to serialize MSBuild's parallel solution builds, `NodeRestore` retries the Rush invocation with a bounded,
  exponential backoff when it hits Rush's own "already running" condition - narrow, tool-specific resilience for
  one confirmed failure mode, not a general-purpose mechanism imposed on every tool.
- **MSBuild stays the top-level orchestrator.** It decides *when*/*whether* each project builds at all and in
  what order (via project references, `.slnx` build graph, `-m` parallelism). Rush is only ever the primitive
  that the *Node-specific* portion of that work is delegated to (installing dependencies, and - for Rush
  specifically - running the actual Node build step too, to get its content-hash incremental skip and build
  cache) - never the other way around.

## Verb parity

| Verb | Node behavior |
|---|---|
| `dotnet restore` (project **or solution/repo-root**) | Hydrates Node deps via `NodeRestore`, hooked on `AfterTargets="CollectPackageReferences"` - the one per-project target NuGet's solution-level restore reliably invokes for every project, unlike `AfterTargets="Restore"` which only fires for single-project restore. This is what makes a bare `dotnet restore` at the repo root hydrate Node dependencies for every project, not just NuGet ones. |
| `dotnet build` | Hydrates (implicit restore) + builds. For non-Rush tools, `npm run build` runs directly, unchanged. For Rush-resolved projects (`Pcf`/`ScriptLibrary`/`CodeApp`), the build step itself delegates to Rush's own `build` command instead, so Rush's content-hash incremental skip and build cache apply - see "Build delegation to Rush" below. |
| `dotnet clean` | Removes this project's own JS build-output folder only (`dist` for CodeApp, ScriptLibrary's TypeScript output folder). Never touches `node_modules` or any shared workspace state - "clean" and "prune installed deps" are different operations, and removing `node_modules` is a far more expensive, disruptive step than a normal `dotnet clean` should trigger silently. `Pcf` has no new Clean target from this SDK - Microsoft's own `PcfClean` (`npm run clean`) already owns PCF's `out/controls` cleanup. |
| `dotnet publish` | Copies JS build output into the publish directory (existing, unaffected by any of the above). |

`dotnet build --no-restore` still triggers `NodeRestore` - `CollectPackageReferences` is a build-time target, not
gated by NuGet's `--no-restore` flag. This is deliberate, not a bug: worst case it's a cheap no-op via the
existing incremental gate (non-Rush) or Rush's own state hash (Rush); it never silently skips Node hydration just
because NuGet's own restore step was skipped.

## Build delegation to Rush

When the resolved tool for a `Pcf`/`ScriptLibrary`/`CodeApp` project is Rush, the *build* step (not just
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
either restore or build routes through Rush, and falls back to this project's own direct `npm install`/
`npm run build` with a visible warning instead.

### PCF-specific: forwarding the build mode as `--build-mode`

One consistent convention drives every project type in this SDK, PCF included: run `dotnet build` for an
unminified, source-mapped build, or `dotnet build --configuration Release` for a minified production build -
the developer never has to think about it or pass any Node-specific flag themselves. This mirrors the exact
Debug/Release convention Microsoft's own `Microsoft.PowerApps.MSBuild.Pcf.props` already established for
`$(PcfBuildMode)`, so PCF's Rush-delegated path reuses `$(PcfBuildMode)` directly rather than introducing a
second, parallel mapping.

Rush's generic `build` command has no built-in mechanism to forward arbitrary CLI arguments through to each
project's own script, so this package's Rush-branch override of `PcfBuild` forwards the mode via Rush's own
documented **custom commands and parameters** feature (`common/config/rush/command-line.json`) - the same
mechanism used for every project type's mode flag (see "Build delegation to Rush" above), just with the
flag spelled `--build-mode` (matching `pcf-scripts`' own `--buildMode`, camel-case-expanded by `yargs`) instead
of the generic `--mode`.

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

Purely via MSBuild's built-in `GetDirectoryNameOfFileAbove`, walking up from the project directory (or
`$(TypeScriptDir)` for ScriptLibrary) looking for the first marker in this order:

| Precedence | Marker | Resolved tool |
|---|---|---|
| 1 | `rush.json` | `rush` |
| 2 | `pnpm-lock.yaml` | `pnpm` |
| 3 | `yarn.lock` | `yarn` (Classic or Berry, detected via `.yarnrc.yml`) |
| 4 | `bun.lockb` | `bun` |
| 5 | `package-lock.json` | `npm` |
| _(none found)_ | - | `npm`, run in the project directory itself (today's default behavior for a bare project) |

No bundled Node script, external npm dependency, or extra process is needed - these five filenames have been
stable across all of these tools for years, and the small number of install command variants below don't need
a library to track.

## Configuration

| Property | Default | Description |
|----------|---------|-------------|
| `NodePackageManager` | _(auto)_ | Leave empty to auto-detect via the table above. Set explicitly to `npm`, `pnpm`, `yarn`, `bun`, or `rush` to skip detection and force a tool (the workspace root is still resolved the same way). Set to `None` to skip Node restore entirely - use this when dependencies are hydrated by something external to the build (a separate CI step, a different orchestrator, etc.). |
| `NodeRestoreCommand` | _(empty)_ | Escape hatch: if set, this exact command line is run instead of anything auto-detected or resolved from `NodePackageManager` - for any tool this SDK doesn't know about, or any custom install invocation. Runs every build (no incremental caching, since an arbitrary command's staleness can't be inferred). |
| `NodeRestoreProjectDirectory` | `$(MSBuildProjectDirectory)` | Directory containing `package.json` that detection starts walking up from. ScriptLibrary sets this to `$(TypeScriptDir)` before calling `NodeRestore`; Pcf/CodeApp use the default. |
| `IsRunningInCI` | _(auto)_ | Reused as-is from [Versioning.md](Versioning.md) - leave empty to auto-detect CI from environment variables, or set `true`/`false` to override. Selects the frozen/reproducible install variant below. |

## Frozen (CI-safe) installs

When `IsRunningInCI` resolves to `true` **and** a lockfile exists at the resolved workspace root, `NodeRestore`
uses the frozen/reproducible install variant instead of the mutable one:

| Tool | Local / mutable | CI / frozen |
|---|---|---|
| `rush` | `install-run-rush.js update` | `install-run-rush.js install` |
| `pnpm` | `pnpm install` | `pnpm install --frozen-lockfile` |
| `yarn` (Classic) | `yarn install` | `yarn install --frozen-lockfile` |
| `yarn` (Berry) | `yarn install` | `yarn install --immutable` |
| `bun` | `bun install` | `bun install --frozen-lockfile` |
| `npm` | `npm install` | `npm ci` |

Both conditions matter: `npm ci` (and the other frozen variants) hard-fail when there is no lockfile, so CI
without a committed lockfile intentionally still falls back to the mutable variant with a build warning,
rather than breaking a build that previously worked. Locally, the frozen variant is never used - `npm ci` in
particular deletes `node_modules` and reinstalls from scratch on every invocation, and fails outright on the
transient `package.json`/lockfile mismatch that's normal mid-edit during local development.

This is a **behavior change for CI builds** compared to the SDK's previous unconditional `npm install`: CI
builds with a committed lockfile now get a frozen, non-mutating install. This directly fixes the lockfile
cache-invalidation and reproducibility problems described above.

Rush is always treated as "has a lockfile" for this purpose - `install-run-rush.js`'s own `install` vs.
`update` verbs already enforce the same frozen-vs-mutable distinction internally.

## Rush specifics

Rush is not a package manager - it's an installer-owning orchestrator that wraps npm/pnpm/Yarn internally and
must never be bypassed. `NodeRestore` always invokes it via the version-pinned bootstrap script
(`<rush.json directory>/common/scripts/install-run-rush.js`), never a global `rush` binary, so the exact Rush
version pinned in `rush.json` is always what runs.

Rush is invoked **unconditionally on every build** rather than being gated by a stamp file: Rush is already
self-idempotent (it compares a state hash against `common/temp/last-install.flag` and exits almost immediately
if nothing changed) and already self-serializing across concurrent invocations (its own
`common/temp/rush#<pid>.lock`). A second, custom incrementality mechanism layered on top would duplicate one
Rush already owns and would be a likely source of subtly-wrong "already restored" bugs.

## Once-per-workspace execution (non-Rush tools)

For npm/pnpm/Yarn/Bun, `NodeRestore` runs at the detected workspace root and is gated by an MSBuild
Inputs/Outputs check (lockfile → a `.node-restore-stamp` file under that root's `node_modules`), so the second,
third, ... project in the same build that shares a workspace root sees the install as already up-to-date and
skips it - the same "once per workspace, not once per project" guarantee `dotnet restore` gives per solution.

Known limitation: concurrent multi-proc MSBuild builds (`dotnet build -m`) of independent projects sharing one
workspace root can still race to invoke install simultaneously for these tools, since none of npm/pnpm/Yarn/Bun
ship a cross-process lock of their own (Rush does not have this problem - see above).

## No global side effects

`NodeRestore` never installs anything globally on the developer's machine: no `npm install -g`, no
`corepack enable`, no silently bootstrapping a missing tool. If the resolved tool isn't on `PATH`, the install
command itself fails with a normal "command not found"-style error - the same philosophy as `dotnet restore`
erroring on a missing SDK/tool rather than trying to fix the environment.

Rush's own `install-run-rush.js` downloads the `rush.json`-pinned Rush release into Rush's own per-user cache
(not a global npm package, not added to `PATH`). This is Rush's own pre-existing, documented mechanism -
identical to what already happens the moment a developer manually runs `rush update` in that repo today. It is
not a new side effect introduced by this SDK.

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

## Breaking change

The previous `NpmInstall` target (Pcf) and inline `npm install` steps (ScriptLibrary, CodeApp) are replaced
outright by the shared `NodeRestore` target - there is no back-compat alias for the old `NpmInstall` name. If
you had a workaround hooking `BeforeTargets`/`AfterTargets="NpmInstall"` (for example, redefining it as a
no-op to opt out, as described in [#90](https://github.com/TALXIS/tools-devkit-build/issues/90)), replace it
with `NodePackageManager=None` instead, and remove the old workaround target entirely.
