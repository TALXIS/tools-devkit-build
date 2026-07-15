# Versioning

Automatic Git-based version number generation is enabled by default. TALXIS SDK derives a version number from Git history so that every build on a tracked branch produces a unique, monotonically increasing version based on commits - with **zero configuration** for most teams.

Versioning is a hard problem: several constraints pull in different directions, and most "obvious" simplifications break one of them. Please read the rationale before changing the defaults - a change can introduce a class of problems.

---

## Problem statement

We have to satisfy multiple constraints at the same time. Together they rule out simple schemes.

### Constraint 1 - It must be a valid EXE/DLL assembly version

To ensure compatibility across all operating systems and platforms, each of the four version fields (Major.Minor.Build.Revision) must be limited to a maximum value of 65535 (16-bit). While modern frameworks (like .NET) and environments (Linux, macOS) support versions as arbitrary strings or larger integers, the Windows Portable Executable (PE) file format intentionally maintains strict backward compatibility with its 16-bit resource structure. This decision ensures that binary metadata can be reliably read by all runtimes, deployment and build systems. ([why build numbers are limited to 65535](https://learn.microsoft.com/en-us/archive/blogs/msbuild/why-are-build-numbers-limited-to-65535)).

It means:
- You **cannot** put a full timestamp in one part (`20250615` overflows).
- You **cannot** put a global, ever-growing commit count in one part (it eventually overflows, and it isn't even monotonic across branches).
- You have very little room: each part must encode something meaningful in **at most 5 digits**.

### Constraint 2 - It must also be expressible as SemVer

The same artifact set spans .NET assemblies **and** packages that use [Semantic Versioning](https://semver.org/) (npm, NuGet, PCFs). We need a **lossless round-trip** between the two so a single source of truth drives both, and so a package registry never sees a "lower" version than the assembly it came from.

SemVer's build-metadata suffix (`+...`) is the natural home for the 4th .NET part. That only works if the .NET version is structured so it maps cleanly: `A.B.C.D` ↔ `A.B.C+D`. A scheme that, say, packs unrelated data into Major or spreads one logical number across two parts cannot round-trip.

One constraint which we were not able to satisfy is that PCF controls don't accept build-metadata suffix. Because of that we use a specific strategy for PCF controls.

### Constraint 3 - The build number should be automatic, from Git history

Developers should not hand-edit a build counter - it's friction, error-prone and it isn't reproducible. The build/revision portion has to be **derived from Git** and be **unique and monotonically increasing** over time, while still fitting Constraint 1.

Our answer is a **date + same-day commit count**, which both increases over time and fits in 16 bits:

- `Build` carries `YYMM` (year + month) - e.g. June 2025 → `2506`.
- `Revision` carries `DD` + the same-day commit count - e.g. the 3rd commit on the 15th → `15003`.

`YYMM` ≤ `9912` and `DDcc` (day + up to 999 commits) ≤ `31999` - both safely under 65535. Time only moves forward, so versions only go up.

### Constraint 4 - Major/Minor must stay under manual control as a release decision

The build number answers *"which build is newer?"*. It must **not** answer *"is this a breaking change?"* - that's a people release decision. So `Major.Minor` is **never** auto-derived from dates; it comes from the project (`<Version>` in the csproj) and optionally from a release-branch name or a Git tag.

### Constraint 5 - It should reduce deployment mistakes

People and agents routinely produce deployable artifacts during development and testing. The dangerous mistake is **deploying the wrong artifact into the wrong environment** e.g. a unreleased and untested local build landing in UAT/PROD.

In an ideal world, developers hold no privileged accounts and can only deploy to their own devbox. In reality most teams don't have that discipline and coding agents often run under identities with access to multiple environments (instructions drift risk).

We try to lower the risk by adding a **version-ordering guardrail**: lower-trust origins produce lower version numbers, and **deployment tooling rejects deployment of an artifact whose version is lower than what's already installed**. A stray import is therefore *blocked loudly*.

> **Important:** This is enforced by **our deployment tooling**, not by the Dataverse platform (anymore). Dataverse APIs no longer enforces same or higher version.

> **This is a guardrail, not a security control.** It is trivially bypassable (anyone can set a version by hand) and is **no substitute for proper privileged identity management**.

### Why these collide

The build number (Constraints 1–3) tells you which artifact is *newer*. But "newer" is exactly the wrong signal for safety (Constraint 5): a local experiment built today is *newer in time* than last week's UAT release, yet must never be allowed to overwrite it. We cannot solve safety with the date-derived number alone, and we cannot solve "which build is newer" with Major/Minor alone. The design below resolves the tension by giving the two jobs to **two different parts of the version**.

---

## Version number strategy

| Part of the version | Job | Controlled by |
|---|---|---|
| **`Major.Minor`** | Communicates breaking changes and decides *which class of environment* an artifact is allowed into | People (release decision) |
| **`Build.Revision`** | Decides *which build is newer* within a tier | Git history (automatic) |

### Format

```
<Major>.<Minor>.[<BranchPrefix>]<YY><MM>.<DD><CommitCount>
└─ tier guard ─┘└────────── automatic ordering ──────────┘
```

| Part | Source | Example |
|------|--------|---------|
| `Major` | First number of `<Version>` in the project file (`0` for non-production) | `1` |
| `Minor` | Second number of `<Version>` in the project file (`0` for non-production) | `0` |
| `BranchPrefix` | **Optional** digit from the branch→prefix map `GitVersionNumberBranchPrefixes`; **absent by default for production** | `1` (develop) |
| `YY` | Last two digits of the latest commit year | `25` |
| `MM` | Month of the latest commit (zero-padded) | `06` |
| `DD` | Day of the latest commit (zero-padded) | `15` |
| `CommitCount` | Same-day commit count, zero-padded to 3 digits (counts all referenced projects, resolved recursively via `ProjectReference`) | `003` |

### Tiers

| Tier | `Major.Minor` | `Build` | Default branches |
|------|---------------|---------|------------------|
| **Production** | real, from csproj `<Version>` | `YYMM` → `2506` (prefix optional) | `main`, `master`, `hotfix/*`, `release/*` |
| **Non-production** | `0.0` | `<prefix>YYMM` → `12506` | `develop` (prefix `1`) + any you add |
| **Local / untracked** | `0.0` | `20000` (reserved prefix 2) | anything unmatched, or any non-CI build |

Because production is always `≥ 1.0.*` and non-production is always `0.0.*`, **any production artifact outranks any non-production artifact** - the guard cannot be crossed by accident. The build number still orders builds *within* a tier, but it can never lift a `0.0` artifact above a real release.

The two properties are **orthogonal**: `GitVersionNumberProductionBranches` decides `Major.Minor` (real vs `0.0`), and `GitVersionNumberBranchPrefixes` optionally assigns a build prefix to **any** branch. Production branches are unprefixed by default (clean `YYMM`), but you may give them a prefix too if you need to order several production environments.

---

## Safety rules

Two concrete rules fall directly out of Constraint 5.

### Rule 1 - Local builds are always `0.0.20000.0`

A build only gets a *production* number when it runs **in CI** from a production branch. **Outside CI, every build is `0.0.20000.0` regardless of branch** - so a local build on `master` can never produce `>1.0.x`, and can't therefore be imported over a production environment (via our tools).

CI is auto-detected from the environment variables set by common runners. If your runner isn't auto-detected, set the `IsRunningInCI` MSBuild property to `true` (or `false` to force local behavior).

This deliberately **does not** trust the branch name alone. Checking out `master` locally is normal; producing a `master`-grade artifact from an unreviewed working tree is not.

### Rule 2 - Daily-integration CI is the lowest; local builds own prefix 2

Developers must be able to import their **own local build over their devbox**, which is hydrated nightly by integration CI. If devbox builds outranked local builds, local development would be impractical. So daily-integration CI gets the **lowest** prefix (`1`), and **local builds are given the next band up - prefix `2` - which is reserved exclusively for them**.

```
0.0.1YYMM.*   daily integration CI (devbox hydration)   ≤ 0.0.19912.*   ← LOWEST
0.0.2xxxx.*   local builds (reserved prefix 2)                          ← always just above daily CI
```

Today every local build is exactly `0.0.20000.0` - the floor of the prefix-2 band, and greater than any `1YYMM` (max `19912`). Reserving the **whole** prefix-2 band for local builds (rather than a single value) leaves room to make local builds incremental in the future (see [Future](#future-incremental-local-builds)) without disturbing any other tier. Higher environments therefore start at prefix `3`.

---

## The non-production prefix ladder

Real projects have **many** non-production environments, and an artifact promoted up the chain must keep outranking the one below it. The optional prefix ranks environments *inside* the `0.0` tier - higher prefix = higher version = wins the guard:

```
prefix 1   daily integration CI (devbox hydration)   0.0.1YYMM.*   (10001–19912)  ← LOWEST
prefix 2   local builds (RESERVED - do not assign)   0.0.2xxxx.*                  ← Rule 2
prefix 3   feature devbox                             0.0.3YYMM.*   (30001–39912)
prefix 4   SIT                                        0.0.4YYMM.*   (40001–49912)
prefix 5   UAT                                        0.0.5YYMM.*   (50001–59912)
```

**Prefix `2` is reserved for local builds** and must not be assigned to a branch. `5YYMM` ≤ `59912` < 65535, so **the maximum usable prefix is `5`** - a direct consequence of Constraint 1. That leaves prefixes `3`–`5` for higher non-production environments. The default ships **only `develop:1`** (the one tier every devbox-hydrating team needs); the rest are a ready-made recipe (see [Workflow recipes](#workflow-recipes)) rather than clutter in the zero-config default.

---

## Major/Minor: where it comes from

`Major.Minor` is the release decision, kept under people control (Constraint 4). It is deliberately a **separate, overridable input**, so teams can pick whatever is common for them - the tooling does not lock you into one source:

- **In the project file** (default) - the first two parts of `<Version>` in the csproj.
- **From the pipeline** - set or override `Version` (or the major/minor properties) from CI variables at build time.
- **From a release branch name** - trunk-then-cut teams cut a `release/x` branch that carries its **own `Major.Minor`**; a common convention is to encode it in the branch name (e.g. `release/2.1`). The next release uses a higher `Major.Minor`. This is how one release line is distinguished from the next - by the guard, not the build number.
- **From a Git tag** - tagging a release (e.g. `v2.1.0`) and taking `Major.Minor` from the tag is a widespread practice.

Reading `Major.Minor` from the **branch name** or a **Git tag** are planned, opt-in features; today the value comes from `<Version>` (which a pipeline can set). The whole point of the design is that adding those sources later changes only *where the two numbers come from* - never the build/revision machinery or the safety guard.

### Monorepos

Versions are evaluated **per project**, and commit counts are resolved recursively through `ProjectReference`. Each package in a monorepo therefore gets its own independent version driven by the commits that actually affect it.

---

## Examples

```
CI on main,        2025-06-15, 3 commits   →  1.0.2506.15003     (production)
CI on release/2.0, 2025-06-15              →  2.0.2506.15001     (production, next release line)
CI on hotfix/x,    2025-06-15              →  1.0.2506.15001     (production, patches 1.0)
CI on develop,     2025-06-15, 3 commits   →  0.0.12506.15003    (non-production, prefix 1, lowest)
any local build (any branch)               →  0.0.20000.0        (always - Rule 1, reserved prefix 2)
CI on feature/*  (with feature/*:3)        →  0.0.32506.15003    (non-production, prefix 3)
```

Ordering that matters:

- Any production `≥ 1.0.*` **>** any non-production `0.0.*` → the tier guard.
- Within non-production: daily CI `0.0.1YYMM` (≤ `19912`) **<** local `0.0.2xxxx` (reserved prefix 2) **<** feature `0.0.3YYMM` … **<** UAT `0.0.5YYMM`.

---

## Configuration

| Property | Default | Description |
|----------|---------|-------------|
| `GitVersionNumber` | `true` (SDK) / _empty_ (Tasks) | Master switch. Set to `false` to disable Git-based versioning entirely - the project's `Version` is used as-is. The Tasks package alone does not set this. |
| `GitVersionNumberProductionBranches` | `main;master;hotfix/*;release/*;` | Branches that receive the **real `Major.Minor`** (when built in CI). Everything else is non-production (`0.0`). Wildcards supported. |
| `GitVersionNumberBranchPrefixes` | `develop:1;` | Optional branch→prefix map (`<branch>:<prefix>`) that assigns a build prefix for ordering. Applies to **any** branch, production or not. Prefix `2` is reserved for local builds; usable values are `1` and `3`–`5`. Wildcards supported (e.g. `feature/*:3`). SDK-only default. |
| `LocalBuildVersionNumber` | `0.0.20000.0` | Version for local / untracked / non-CI builds. The `0.0` marks it as never-deployable-to-a-real-environment (Rule 1); `20000` is the floor of the reserved prefix-2 band, just above daily-integration CI (Rule 2). |
| `IsRunningInCI` | _(auto)_ | Leave empty to auto-detect CI from environment variables. Set to `true`/`false` to override detection. |
| `SemVer` | _(output)_ | The SemVer projection of the generated version, `Major.Minor.Build+Revision` (see [SemVer round-trip](#semver--npm-round-trip)). |

Set these per project or share them via `Directory.Build.props`:

```xml
<Project>
   <PropertyGroup>
      <GitVersionNumberProductionBranches>main;master;hotfix/*;release/*;</GitVersionNumberProductionBranches>
      <GitVersionNumberBranchPrefixes>develop:1;</GitVersionNumberBranchPrefixes>
      <LocalBuildVersionNumber>0.0.20000.0</LocalBuildVersionNumber>
   </PropertyGroup>
</Project>
```

### Workflow recipes

The defaults assume Git Flow, but nothing forces a particular flow. A few starting points:

| Workflow | `GitVersionNumberProductionBranches` | `GitVersionNumberBranchPrefixes` |
|----------|--------------------------------------|----------------------------------|
| Simple trunk | `main;master;` | _(none)_ |
| Git Flow **(default)** | `main;master;hotfix/*;release/*;` | `develop:1;` |
| Trunk + release cuts (trunk = daily integration) | `release/*;hotfix/*;` | `main:1;master:1;` |
| Full non-production ladder | `main;master;hotfix/*;release/*;` | `develop:1;feature/*:3;sit/*:4;uat/*:5;` |

In the "trunk + release cuts" recipe, `main`/`master` are **not** production, so they build as non-production daily integration (`0.0`) with prefix `1` (lowest, devbox hydration); only the cut `release/*` branches are production with their own `Major.Minor`. Prefix `2` is skipped everywhere because it is reserved for local builds.

### Opting out

```xml
<GitVersionNumber>false</GitVersionNumber>
```

When disabled, `GitVersionNumberBranchPrefixes` is not populated with defaults and the `GenerateGitVersion` task uses the project's `Version` as-is.

---

## SemVer / npm round-trip

The mapping is lossless and contains **no branch logic** - it is a pure structural projection (Constraint 2):

```
.NET  A.B.C.D    ↔    SemVer  A.B.C+D
```

```
1.0.2506.15003    →  1.0.2506+15003    (a registry / npm sees 1.0.2506)
0.0.12506.15003   →  0.0.12506+15003
0.0.20000.0       →  0.0.20000+0
```

The `GenerateGitVersion` task exposes this via the `SemVer` MSBuild property, always formatted `{Major}.{Minor}.{Build}+{Revision}`.

---

## Future: incremental local builds

Today, **every** local build is exactly `0.0.20000.0`. That guarantees safety (Rule 1) but has a known cost: because consecutive local builds produce the *same* number, importing a fresh local build over your previous one requires a force-update - the guardrail can't tell them apart.

A natural future requirement is to make local builds **incremental** - derived from the build **time** and whether the working tree has **dirty (uncommitted) changes** - so each rebuild outranks the last and reimporting over your own devbox "just works". The design reserves room for exactly this:

- Prefix `2` (the whole `0.0.2xxxx.*` band) is **reserved for local builds** and assigned to no environment.
- That band is a large, collision-free space: build `20000`–`29912` × revision `0`–`65535`. A future implementation can map a timestamp (plus a dirty-tree marker) into it to produce a monotonically increasing local version.
- Every value in the band still **outranks daily-integration CI** (any `1YYMM` ≤ `19912`) and stays **below every higher environment** (prefix `3`+), so the safety tiers are untouched.

The invariant to preserve: incremental local builds must stay **inside the prefix-2 band** and keep `Major.Minor` at `0.0`, or they would break Rules 1 and 2. Carving out that band is the hard part, and it is already done - which is why `0.0.20000.0` is a load-bearing default, not an arbitrary placeholder.

---

## Where versioning is applied per project type

`<ProjectType>` wires up Git-based versioning automatically; each type applies the generated version to a different artifact:

| `ProjectType` | Version applied to |
|---|---|
| `Solution` | `Solution.xml` inside the solution zip and the `.nupkg` |
| `PDPackage` | `.pdpkg.zip` metadata and the `.nupkg` |
| `Plugin` | Plugin assembly version |
| `WorkflowActivity` | Workflow activity assembly version |
| `Pcf` | `ControlManifest.xml` (PCF-specific format, see [PCFs](#pcfs)) |
| `CodeApp` / `GenPage` / `ScriptLibrary` | Not versioned today; `package.json` version stamping is planned |

See [Build process](./BuildProcess.md) for how each project type wires this in and how it interacts with `dotnet pack`.

---

## PCFs

PCFs [use semantic versioning](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/manifest-schema-reference/control), with [some nuances](https://dianabirkelbach.wordpress.com/2020/12/23/all-about-pcf-versioning/) around changing major/minor. Each part may be up to *2,147,483,647* (32-bit), and it is impossible to push a *lower* PCF version (even with `ForceUpdate=TRUE`). We therefore assemble the PCF version from the outputs above as (this applies even when Git versioning is disabled):

```
0.0.<SECONDS_FROM_2020-01-01_TILL_LAST_COMMIT_OR_NOW>
```

---

## Edge cases

There may be edge cases. If you find one, please [report it](https://github.com/TALXIS/tools-devkit-build/issues) or submit a PR.

### CI builds from unlisted branches produce LocalBuildVersionNumber

Any branch not listed in `GitVersionNumberProductionBranches` or `GitVersionNumberBranchPrefixes` produces `LocalBuildVersionNumber` (`0.0.20000.0`) even in CI, with a build warning. Add the branch to `GitVersionNumberBranchPrefixes` to give it a real build number.

### Removing a project reference results in a lower version number on the same day

If you change a solution with a referenced project on a given day, then remove a project reference on the same day, the second build's commit count can be lower, producing a lower version that fails to import. This is most likely on non-production branches; the workaround is to make a commit and rebuild the next day. To be improved in future.

### Over 999 commits per day

Each version part is bounded by [`ushort`](https://learn.microsoft.com/en-us/dotnet/api/system.uint16) (65535). More than 999 commits in one day across all referenced projects overflows the revision. Workarounds: collapse to fewer commits the next day, or [squash on merge](https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/incorporating-changes-from-a-pull-request/about-pull-request-merges#squash-and-merge-your-commits). If you hit this, please reach out.
