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
