$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Join-Path $repoRoot "src\\Dataverse"
$destination = "C:\\NuGetLocal"

if (-not (Test-Path -Path $destination)) {
  New-Item -Path $destination -ItemType Directory -Force | Out-Null
}

$packageCandidates = Get-ChildItem -Path $sourceRoot -Directory | ForEach-Object {
  $releaseDir = Join-Path $_.FullName "bin\\Release"
  if (Test-Path -Path $releaseDir) {
    Get-ChildItem -Path $releaseDir -Filter *.nupkg -File -ErrorAction SilentlyContinue
  }
}

$latestPackages = $packageCandidates | Group-Object -Property Name | ForEach-Object {
  $_.Group | Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

if (-not $latestPackages) {
  Write-Host "No .nupkg files found under $sourceRoot."
  exit 0
}

foreach ($pkg in $latestPackages) {
  Copy-Item -Path $pkg.FullName -Destination $destination -Force
}

Write-Host "Copied $($latestPackages.Count) package(s) to $destination."

# Stop all .NET Host processes before clearing NuGet cache
$dotnetProcesses = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue
if ($dotnetProcesses) {
  Write-Host "Stopping $($dotnetProcesses.Count) .NET Host process(es)..."
  $dotnetProcesses | Stop-Process -Force
  Start-Sleep -Seconds 1
}

dotnet nuget locals all --clear
