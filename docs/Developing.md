# Developing

We are happy to collaborate with developers and contributors interested in enhancing Power Platform development processes. If you have feedback, suggestions, or would like to contribute, please feel free to submit issues or pull requests.

## Local building and debugging

For developing and testing, you may use [this repo](https://github.com/TALXIS/tools-devkit-build-test) which has a basic setup done.

### Package project

Run the following terminal command in the folder `MSBuildTasks`:

```powershell
dotnet pack --configuration Debug
```

### Consuming project

Add `nuget.config` file to your Dataverse solution project folder:

```xml
<configuration>
  <packageSources>
    <!-- package source is additive -->
    <add key="LocalBuildTasks" value="/{REPOSITORY PATH}/src/MSBuildTasks/bin/Release/" />
  </packageSources>
</configuration>
```

Clear all cached packages:

> Note that the command below is going to **nuke** your entire local package cache. It might be wiser to navigate to `.nuget\packages\talxis.devkit.build.dataverse.tasks` and delete only the contents of this folder. If you can't delete it because it is in use, execute `dotnet build-server shutdown` first.

```powershell
dotnet nuget locals --clear all
```

Rebuild the project:

```powershell
dotnet build --no-incremental --force
```

### Attaching debugger

In folder where you want to run `dotnet build`:

```powershell
set MSBUILDDEBUGONSTART=1
dotnet build -bl /m:1
```

You will get a promp to attach debugger into a Visual Studio instance.

### MSBuild logs

Build the target project with:

```powershell
dotnet build -bl
```

The produced logs can be opened in [MSBuild Log Viewer](https://msbuildlog.com/).