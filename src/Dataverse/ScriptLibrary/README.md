# TALXIS.DevKit.Build.Dataverse.ScriptLibrary

MSBuild targets for Dataverse ScriptLibrary projects.

## MSBuild properties
- `ProjectType` (default `ScriptLibrary`): marks the project for reference discovery.
- `RunNodeBuild` (default `false`): runs `npm install` and `npm run build` in `TypeScriptDir`.
- `TypeScriptDir` (default `$(MSBuildProjectDirectory)\TS`): folder containing the TypeScript project.
- `ScriptLibraryMainFile` (default `$(MSBuildProjectDirectory)\TS\build\main.js`): main script file used by consuming targets.
- `LangVersion` (default `latest`): C# language version for the project.
- `GenerateAssemblyInfo` (default `false`): disables auto-generated assembly info.
