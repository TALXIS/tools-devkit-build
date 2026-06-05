# TALXIS DevKit Dataverse GenPage build support

MSBuild integration for Dataverse GenPage projects.

A GenPage project is a project-type marker only. It carries no Dataverse IDs or page metadata. Every `*.tsx` file outside excluded build folders is treated as a page, and the page name is the file name without extension.

## Project contract

```xml
<PropertyGroup>
  <ProjectType>GenPage</ProjectType>
</PropertyGroup>
```

Optional source files:

- `<PageName>.config.json`, otherwise shared `genpage.config.json`

Build output is normalized to `$(TargetDir)<PageName>.js` for each page.

## Solution integration

Solution projects discover referenced GenPage projects, call `GetGenPageOutputs`, ensure XML-only `uxagentprojects/<page-guid>/...` declarations exist in solution source, then project native `filecontent/` only into the SolutionPackager metadata working directory under `obj`.
