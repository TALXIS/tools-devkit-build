# Versioning

Git-based version number generation is enabled by default. The SDK automatically derives version numbers from Git history so that every build on a tracked branch produces a unique, monotonically increasing version.

## Version number format

```
<Major>.<Minor>.<BranchPrefix><YY><MM>.<DD><CommitCount>
```

| Part | Source | Example |
|------|--------|---------|
| `Major` | First number from `<Version>` in your project file | `1` |
| `Minor` | Second number from `<Version>` in your project file | `0` |
| `BranchPrefix` | Optional digit (0–5) from branch rules in `GitVersionNumberBranches` | `2` (for develop) |
| `YY` | Last two digits of the latest commit year | `26` |
| `MM` | Month of the latest commit (zero-padded) | `05` |
| `DD` | Day of the latest commit (zero-padded) | `15` |
| `CommitCount` | Number of commits on that day, zero-padded to 3 digits | `003` |

**Example:** `Version=1.0`, branch `develop` (prefix `2`), latest commit on 2026-05-15 with 3 commits that day → **`1.0.22605.15003`**

The branch prefix allows higher-priority branches (e.g. production) to always have a higher version than lower-priority branches, preventing accidental overwrites when deploying. Maximum prefix value is `5` due to the [build number limitation](https://learn.microsoft.com/en-us/archive/blogs/msbuild/why-are-build-numbers-limited-to-65535) in Windows.

Commit counts include commits from all referenced projects (resolved recursively via `ProjectReference`).

## MSBuild properties

| Property | Default | Description |
|----------|---------|-------------|
| `GitVersionNumber` | `true` (SDK) / _empty_ (Tasks) | Master switch. Set to `false` to disable Git-based versioning entirely — the project's `Version` property is used as-is. When using the Tasks package directly (without the SDK), this is not set by default. |
| `GitVersionNumberBranches` | `main:1;master:1;develop:2;` (SDK only) | Semicolon-separated branch rules. Each entry is `<branch-name>` or `<branch-name>:<prefix>`. Wildcard patterns are supported (e.g. `feature/*:0`). Defaults are only applied when using the SDK package; the Tasks package alone does not populate this. |
| `LocalBuildVersionNumber` | `0.0.20000.0` | Version used when the build is not running in CI, or when the current branch does not match any rule in `GitVersionNumberBranches`. |
| `IsRunningInCI` | _(auto-detect)_ | Override CI detection. Set to `true` or `false` to force the behaviour; omit to let the task auto-detect from common CI environment variables (`CI`, `TF_BUILD`, `GITHUB_ACTIONS`, `GITLAB_CI`, `JENKINS_URL`, `CIRCLECI`, `TEAMCITY_VERSION`). |

These properties can be set per project in your `.csproj` or shared via `Directory.Build.props`:

```xml
<Project>
   <PropertyGroup>
      <GitVersionNumberBranches>master:1;main:1;develop:2;release/*:3</GitVersionNumberBranches>
      <LocalBuildVersionNumber>0.0.12345.0</LocalBuildVersionNumber>
   </PropertyGroup>
</Project>
```

### Opting out of Git versioning

Set `GitVersionNumber` to `false` to disable automatic version generation:

```xml
<GitVersionNumber>false</GitVersionNumber>
```

When disabled, `GitVersionNumberBranches` is not populated with defaults and the `GenerateGitVersion` task uses the project's `Version` property as-is.

### Local builds

When building locally, the task auto-detects that it is not in CI and uses `LocalBuildVersionNumber` instead of generating a commit-derived version. No extra configuration is needed.

If you need to override this detection (e.g., your CI system is not recognised), set `IsRunningInCI` explicitly:

```xml
<IsRunningInCI>true</IsRunningInCI>
```

### Local builds on tracked branches (manual override)

If you want to force git versioning locally for testing, you can set `IsRunningInCI=true` at the command line:

```shell
dotnet build -p:IsRunningInCI=true
```

To pin a predictable local version regardless of CI detection, you can set `LocalBuildVersionNumber`:

```xml
<LocalBuildVersionNumber>0.0.12345.0</LocalBuildVersionNumber>
```

Or clear the branch rules to force the fallback even in CI:

```shell
dotnet build -p:GitVersionNumberBranches=""
```

## PCFs

Since PCFs [use semantic versioning](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/manifest-schema-reference/control), and there are [some nuances](https://dianabirkelbach.wordpress.com/2020/12/23/all-about-pcf-versioning/) with changing the major and minor numbers. The maximum value for each part is *2,147,483,647* (32-bit integer). With PCFs it is impossible to push a lower version of PCF (even with `ForceUpdate=TRUE`). We currently assemble the PCF version as following from the outputs generated above (this applies also when not using the generate version):

```
0.0.<SECONDS_FROM_2020-01-01_TILL_LAST_COMMIT_OR_NOW>
```

## Edge cases

There may obviously be some edge cases. If you find any, please [report them](https://github.com/TALXIS/tools-devkit-build/issues) or submit a PR to fix it!

### Removing a project reference results in a lower version number on the same day
If you make a change to a solution with a referenced project on the same day, and then remove a project reference on the same day, the second build version is going to be lower and the solution will fail to import. This is most likely to happen in non-production branches and the known workaround is to make a commit and rebuild the affected solution the next day. This is to change in future.

### Over 999 commits per day
Each number of version is limited by [`ushort`](https://learn.microsoft.com/en-us/dotnet/api/system.uint16?view=net-9.0)'s maximum size. If you do more than 999 commits per day across all referenced projects, you will end up with an error. A workaround is to bump projects with too many commits in that day to a single commit the next day. Alternatively, you can consider using [squashing commits](https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/incorporating-changes-from-a-pull-request/about-pull-request-merges#squash-and-merge-your-commits). If you hit this, please reach out to us.