$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

$projects = @(
  "src\\Dataverse\\PDPackage\\TALXIS.DevKit.Build.Dataverse.PdPackage.csproj",
  "src\\Dataverse\\Plugin\\TALXIS.DevKit.Build.Dataverse.Plugin.csproj",
  "src\\Dataverse\\Solution\\TALXIS.DevKit.Build.Dataverse.Solution.csproj",
  "src\\Dataverse\\ScriptLibrary\\TALXIS.DevKit.Build.Dataverse.ScriptLibrary.csproj",
  "src\\Dataverse\\Tasks\\TALXIS.DevKit.Build.Dataverse.Tasks.csproj",
  "src\\Dataverse\\WorkflowActivity\\TALXIS.DevKit.Build.Dataverse.WorkflowActivity.csproj"
)

foreach ($project in $projects) {
  $projectPath = Join-Path $repoRoot $project
  if (-not (Test-Path -Path $projectPath)) {
    throw "Project not found: $projectPath"
  }

  dotnet build $projectPath -c Release && dotnet pack $projectPath -c Release
}
