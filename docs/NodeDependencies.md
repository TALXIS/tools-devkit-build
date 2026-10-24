# Node builds

`Pcf`, `ScriptLibrary` and `CodeApp` projects contain a JavaScript or TypeScript build. The SDK runs it as part of
`dotnet restore`, `dotnet build` and `dotnet publish`, so a repository with .NET and Node projects needs no other
build command. The SDK has four jobs, whatever tools a project uses:

1. **Run inside the .NET build.** `dotnet restore` installs Node dependencies; `dotnet build` installs them if needed
   and runs the JavaScript build.
2. **Propagate the configuration.** `Debug` builds development bundles and an unmanaged solution; every other
   configuration builds production bundles and a managed solution.
3. **Pick up the outputs.** Each project type puts its deliverable in `bin`, where Solution projects find it.
4. **Stay safe in parallel builds.** Installs that share a folder never run at the same time.

## Quick start

A project needs only its project type, its identity property and a `package.json`.

```xml
<Project Sdk="TALXIS.DevKit.Build.Sdk/2.0.0">
  <PropertyGroup>
    <ProjectType>ScriptLibrary</ProjectType>
    <ScriptLibraryName>contoso_main</ScriptLibraryName>
  </PropertyGroup>
</Project>
```

When `package.json` has no `build` script, the SDK uses its default build for the project type:

| Project type | Default build | Needs in `devDependencies` | Writes |
|---|---|---|---|
| `ScriptLibrary` | rollup, entry `src/index.ts` (or `.js`), UMD bundle named `$(ScriptLibraryName)` | `rollup`, `@rollup/plugin-typescript`, `typescript`, `tslib`; optional `@rollup/plugin-node-resolve`, `@rollup/plugin-commonjs`, `@rollup/plugin-terser` (minification) | `$(ScriptLibraryName).js` |
| `CodeApp` | Vite; uses `@vitejs/plugin-react`, `@tailwindcss/vite` and `@microsoft/power-apps-vite` when installed; `@` aliases `./src` | `vite` | the app (`index.html` and assets) |
| `Pcf` | `pcf-scripts build` (the control's own `build` script) | `pcf-scripts` | `out/controls/<control>/bundle.js` |

The default configs live in the SDK package and load rollup, Vite and plugins from the project's `node_modules`, so
the project's own versions are used. A project's own `build` script always wins over the default.

## How a build runs

```
dotnet restore / build
  NodeToolchain   pick the package manager and the orchestrator (if any) for this project
  NodeRestore     install dependencies (skipped when already up to date)
  NodeBuild       run the build with NODE_ENV and, unless an orchestrator builds, BUILD_OUTPUT_DIR
  outputs         copy the deliverable to bin; in production builds, check it
```

## Debug and Release

| | `Debug` | Any other configuration (`Release`, `Staging`, ...) |
|---|---|---|
| `NodeBuildConfiguration` | `development` | `production` |
| `NODE_ENV` | `development` | `production` |
| PCF | `pcf-scripts --build-mode development` | `--build-mode production` |
| CodeApp | `--mode development` | `--mode production` |
| Solution package | unmanaged | managed |

`NodeBuildConfiguration` can be set explicitly to override the mapping. A Solution build passes its configuration
to every referenced project.

PCF projects rebuild their bundle whenever the mode changes, even with unchanged sources, and non-Debug builds always
rebuild it. A bundle left by `npm start watch` can therefore never end up in a Release solution. Set
`PcfAlwaysNpmRunBuild=false` to opt out of the second rule.

In production builds the deliverables are checked before packaging. The build fails when a file contains an inline
source map, is a webpack development bundle (`eval` devtool), or is larger than `NodeBuildMaxOutputSize` (default
5 MB, the Dataverse web-resource limit; a warning at 80%). A `sourceMappingURL` comment that points to a separate
`.map` file is allowed; the `.map` file is not packaged.

## Output contract

A build should write its result to the folder in `BUILD_OUTPUT_DIR` (default `obj/<configuration>/<tfm>/node/`,
emptied before each build). Builds that ignore it keep working: the SDK also looks in the tool's usual folder.

| Project type | Deliverable | Looked up in |
|---|---|---|
| `ScriptLibrary` | `bin/<configuration>/<tfm>/$(ScriptLibraryName).js` | `BUILD_OUTPUT_DIR`, then `dist/`, then `build/` under the Node root; a missing file is an error |
| `CodeApp` | `bin/<configuration>/<tfm>/$(AppName)/` | `BUILD_OUTPUT_DIR`, then `dist/` |
| `Pcf` | `out/controls/<control>/` | Microsoft's PCF layout, unchanged |

A ScriptLibrary without a `package.json` ships a checked-in `$(ScriptLibraryName).js` from anywhere under its Node root.
`dotnet clean` removes the build outputs but never `node_modules`.

## Bring your own tools

There are three layers. Each has one way to override it.

### 1. Build tool: rollup, webpack, Vite, tsc or anything else

The build tool is whatever the `package.json` `build` script runs. Switching from rollup to webpack, or dropping Vite,
is a `package.json` change. The script receives:

- `NODE_ENV`: `development` or `production`. Turn minification on and source maps off for `production`.
- `BUILD_OUTPUT_DIR`: where to write the deliverable.
- The project type's arguments: PCF `--build-mode <mode>`, CodeApp `--mode <mode>`, ScriptLibrary none.

`NodeBuildCommand` replaces the build step entirely; it runs from the Node root with the same environment variables.

Recipes:

- **rollup with your own config**: `"build": "rollup -c"`, and in `rollup.config.mjs`:

  ```js
  import typescript from '@rollup/plugin-typescript';
  import terser from '@rollup/plugin-terser';
  const production = process.env.NODE_ENV === 'production';
  export default {
    input: 'src/index.ts',
    output: {
      file: `${process.env.BUILD_OUTPUT_DIR ?? 'build/'}contoso_main.js`,
      format: 'umd',
      name: 'Contoso',
      sourcemap: production ? false : 'inline'
    },
    plugins: [typescript(), ...(production ? [terser()] : [])]
  };
  ```

- **tsc only**: `"build": "tsc -p ."` with `"outFile": "build/contoso_main.js"`. tsc does not minify; use rollup for
  production bundles of any size.
- **webpack**: `"build": "webpack --mode $NODE_ENV"` with `output.path` set from `BUILD_OUTPUT_DIR`.
- **CodeApp without Vite**: any build that writes `index.html` and its assets to `BUILD_OUTPUT_DIR` (or `dist/`).
- **PCF with a custom webpack config**: pcf-scripts merges `webpack.config.js` only when `featureconfig.json` contains
  `"pcfAllowCustomWebpack": "on"`. Read the mode from pcf-scripts' parsed `--build-mode` (yargs gives it as
  `buildMode`), not from `NODE_ENV`.

### 2. Package manager: npm and pnpm built in

The package manager is detected by walking up from the Node root:

| Found above the Node root | Package manager | Install runs in |
|---|---|---|
| `pnpm-lock.yaml` | pnpm | the lockfile folder |
| `package-lock.json`, or nothing | npm | the lockfile folder, or the Node root |

Inside a Rush repository, pnpm is preferred for projects Rush does not own.

The install runs once per install folder and is skipped while `node_modules/.node-restore.stamp` is newer than
`package.json` and the lockfile. Your own `.npmrc`, `pnpm-workspace.yaml` and lockfile location are respected; the SDK
only runs the install in the lockfile's folder.

Overrides: `NodePackageManager` (`npm`, `pnpm`, a provider's name, or `None` to skip the install while still
building), and `NodeRestoreCommand`, which replaces the install entirely and runs from the Node root on every build.

### 3. Orchestrator: Rush built in

When `rush.json` is found above the Node root and the project is registered in it, Rush owns restore and build:

- Restore runs the version-pinned `common/scripts/install-run-rush.js` (never a global `rush`): `rush install` in CI,
  `rush update` locally. A project built on its own in a never-installed workspace installs only what it needs
  (`install --to .`) when the repository uses pnpm workspaces (`useWorkspaces` in `pnpm-config.json`). When Rush
  reports that the lockfile needs `rush update`, a local build runs it. Rush is skipped while
  `common/temp/last-install.flag` is newer than every registered `package.json`, `rush.json` and
  `common/config/rush` file.
- Build runs `rush build` (scoped with `--to .` for a project built on its own), so Rush's incremental build and
  build cache apply. Every build argument must be declared in `common/config/rush/command-line.json`; otherwise the
  build fails with this fix:

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

  CodeApp projects need the same for `--mode`. A Rush project needs a `build` script; the SDK default builds apply
  only when the package manager builds.
- With subspaces enabled, restore is always scoped to the project (`--to .`) and the up-to-date check is off, because
  a project can depend on several subspaces.

An unregistered project below `rush.json` gets a warning and uses the package manager directly.
`NodeOrchestrator=None` turns Rush off for a project.

### Adding a package manager or orchestrator

A provider is one `.targets` file, shipped in a NuGet package's `buildTransitive` folder, that appends a detection
target and registers a candidate. Package managers need no targets of their own; the SDK's runners use the metadata:

```xml
<Project>
  <PropertyGroup>
    <NodePackageManagerDetectDependsOn>$(NodePackageManagerDetectDependsOn);_ContosoYarnDetect</NodePackageManagerDetectDependsOn>
  </PropertyGroup>
  <Target Name="_ContosoYarnDetect">
    <PropertyGroup>
      <_ContosoYarnRoot>$([MSBuild]::GetDirectoryNameOfFileAbove('$(NodeRootFullPath)', 'yarn.lock'))</_ContosoYarnRoot>
    </PropertyGroup>
    <ItemGroup Condition="'$(_ContosoYarnRoot)' != '' or '$(NodePackageManager)' == 'yarn'">
      <NodePackageManagerCandidate Include="yarn">
        <Priority>190</Priority>
        <RootPath Condition="'$(_ContosoYarnRoot)' != ''">$(_ContosoYarnRoot)</RootPath>
        <RootPath Condition="'$(_ContosoYarnRoot)' == ''">$(NodeRootFullPath)</RootPath>
        <Lockfile>yarn.lock</Lockfile>
        <InstallCommand>yarn install</InstallCommand>
        <FrozenInstallCommand>yarn install --frozen-lockfile</FrozenInstallCommand>
        <RunScriptCommand>yarn run</RunScriptCommand>
        <ExecCommand>yarn</ExecCommand>
        <Source>$(MSBuildThisFileFullPath)</Source>
      </NodePackageManagerCandidate>
    </ItemGroup>
  </Target>
</Project>
```

| Candidate metadata | Meaning |
|---|---|
| `Priority` | Higher wins when nothing is requested. npm is 0, pnpm with a lockfile 200, Rush 300. Ties are an error. |
| `RootPath` | Folder the install runs in. |
| `Lockfile` | Lockfile name in `RootPath`; selects `FrozenInstallCommand` in CI and joins the up-to-date check. |
| `InstallCommand`, `FrozenInstallCommand` | Install commands for local builds and for CI. |
| `RunScriptCommand`, `ArgsSeparator` | Runs the `build` script; the separator goes between the script name and its arguments (npm needs two dashes). |
| `ExecCommand` | Runs a tool installed in the project; used for the SDK default builds. |
| `StampPath` | Optional; for tools without `node_modules` (Yarn Plug'n'Play). |
| `Source` | The registering file, shown in errors. |

An orchestrator registers a `NodeOrchestratorCandidate` through `NodeOrchestratorDetectDependsOn`, with `Priority`,
`RootPath`, `OwnsRestore`, `OwnsBuild`, an optional preferred `PackageManager`, and hooks its own targets
`AfterTargets="NodeRestore"` and `AfterTargets="NodeBuild"`, conditioned on its selected item:

```xml
Condition="'@(NodeSelectedOrchestrator)' == 'contoso' and '@(NodeSelectedOrchestrator->'%(OwnsBuild)')' == 'true'"
```

The SDK resolves both roles once and publishes `NodeSelectedPackageManager` and `NodeSelectedOrchestrator`, with
`OwnsRestore` and `OwnsBuild` set so that exactly one provider runs each step. Duplicate names, priority ties, invalid
priorities, missing roots and unmatched explicit requests fail with the registering file. Providers from NuGet
packages are not imported during a bare `dotnet restore`; they apply from the first build on.

`tests/fixtures/external-provider` contains complete Yarn and Bun providers.

## Properties

| Property | Default | Meaning |
|---|---|---|
| `NodeRootPath` | `.` | Folder with `package.json`, relative to the project. |
| `NodeBuildConfiguration` | `development` for `Debug`, else `production` | Build mode passed to the Node build. |
| `NodePackageManager` | detected | `npm`, `pnpm`, a provider's name, or `None` (skip the install, still build). |
| `NodeOrchestrator` | detected | `rush`, a provider's name, or `None`. |
| `NodeRestoreCommand` | | Replaces the install. |
| `NodeBuildCommand` | | Replaces the build. |
| `NodeBuildOutputDir` | `obj/<configuration>/<tfm>/node/` | `BUILD_OUTPUT_DIR`. |
| `NodeBuildMaxOutputSize` | `5242880` | Largest deliverable in production builds, in bytes; `0` turns the check off. |
| `RunNodeBuild` | `true` when `package.json` exists | `false` skips Node restore and build for a ScriptLibrary or CodeApp. |
| `IsRunningInCI` | detected from `CI`, `TF_BUILD`, `GITHUB_ACTIONS`, `GITLAB_CI`, `CIRCLECI`, `JENKINS_URL`, `TEAMCITY_VERSION` | Selects frozen installs. Accepts `true`/`false`/`1`/`0`/`yes`/`no`. |

Project types add `NodeBuildArgument` items (identity = flag, `Value` metadata = value); a project can add its own.

## CI

CI installs are frozen (`npm ci`, `pnpm install --frozen-lockfile`, `rush install`) when the lockfile exists; without
one the build warns. Commit lockfiles. A typical Azure Pipelines job needs nothing beyond the .NET commands:

```yaml
- task: UseNode@1
  inputs:
    version: '22.x'
- task: UseDotNet@2
  inputs:
    version: '10.x'
- script: dotnet restore -c Release
- script: dotnet publish --no-restore -c Release src/Solutions.Main
```

`npmAuthenticate` (or another registry login) must run before `dotnet restore`, because `dotnet restore` installs
Node dependencies. A cold-cache restore of a whole solution cannot install them (NuGet runs no per-project target
there); the build installs them instead.

## Parallel builds

Installs that share an install folder, and all Rush commands of one repository, take a lock that spans MSBuild nodes
and processes. A build that waits for the lock says so once a minute and gives up after 30 minutes. After getting the
lock, a build re-checks the up-to-date stamp, so only the first of several projects sharing a folder installs. A Rush
build script must not call `dotnet build` on a project of the same repository: that build would wait for the lock its
caller holds.

## Troubleshooting

- **See what ran.** Build with `-v:n`: every Node command is logged with its working directory, and
  `NodeToolchain:` shows the selected package manager and orchestrator. Failed commands print their full output.
- **Start over.** Delete `node_modules` (and `common/temp` in a Rush repository) and build again; the stamps live
  inside those folders.
- **"did not produce <name>.js".** The build wrote a different file name or folder. Write `$(ScriptLibraryName).js`
  to `BUILD_OUTPUT_DIR`, or set `ScriptLibraryName` to the file the build writes.
- **"contains an inline source map" or "webpack development bundle" in Release.** The build ignores `NODE_ENV`;
  see the recipes above.
- **"does not declare the custom parameter(s)".** Add the parameter to Rush's `command-line.json` as shown above.
- **"Waiting for another build..."** Another build holds the install lock for the same folder. If none is running,
  stop leftover `dotnet` processes and retry.
- **Rush bootstrap errors** name the folder to delete and the command to run; the SDK never deletes shared Rush state.
- **Node dependencies are not installed by `dotnet restore`** when packages are restored to a custom folder
  (`RestorePackagesPath`); the restore warns and the build installs them.

## Interop with Microsoft's PCF targets

`TALXIS.DevKit.Build.Dataverse.Pcf` builds on `Microsoft.PowerApps.MSBuild.Pcf`. It turns off that package's own
unconditional `npm install` (`PcfEnableAutoNpmInstall=false`; set it to `true` to restore Microsoft's behavior) and
replaces its `PcfBuild` target with the SDK's restore and build, so package-manager detection, Rush and frozen CI
installs apply to PCF projects too.
