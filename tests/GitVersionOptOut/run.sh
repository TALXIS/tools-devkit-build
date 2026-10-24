#!/usr/bin/env bash
# Verifies GitVersionNumber=false opts a project out of Git-based versioning:
# the csproj <Version> must be used as-is, in CI and outside CI.
#
# Usage: tests/GitVersionOptOut/run.sh
# Requires: dotnet (10.x SDK), git.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Lay the Tasks package out the way NuGet does: <pkg>/*.props|*.targets + <pkg>/net10.0/<assembly>.
pkg="$work/pkg"
mkdir -p "$pkg"
cp -R "$repo_root/src/Dataverse/Tasks/msbuild/tasks/." "$pkg/"
dotnet build "$repo_root/src/Dataverse/Tasks/TALXIS.DevKit.Build.Dataverse.Tasks.csproj" \
  -c Release -o "$pkg/net10.0" -v:q -nologo -clp:ErrorsOnly

# Consumer project: real SDK props/targets (which set the GitVersionNumber default and branch
# rules) plus the Tasks package targets, on a throwaway Git repository.
proj="$work/consumer"
mkdir -p "$proj"
cat > "$proj/Consumer.csproj" <<EOF
<Project>
  <Import Project="$repo_root/src/Sdk/Sdk/Sdk.props" />
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>3.4.0</Version>
  </PropertyGroup>
  <Import Project="$repo_root/src/Sdk/Sdk/Sdk.targets" />
  <Import Project="$pkg/TALXIS.DevKit.Build.Dataverse.Tasks.props" />
  <Import Project="$pkg/TALXIS.DevKit.Build.Dataverse.Tasks.targets" />
</Project>
EOF
(
  cd "$proj"
  git init -q
  git -c user.name=test -c user.email=test@example.com add Consumer.csproj
  git -c user.name=test -c user.email=test@example.com commit -q -m "initial"
)

# evaluate <msbuild args...>: prints "Version PackageVersion" after running GenerateVersionNumber.
evaluate() {
  local json
  json="$(dotnet msbuild "$proj/Consumer.csproj" -nologo -v:q -t:GenerateVersionNumber \
    -getProperty:Version -getProperty:PackageVersion "$@")"
  local v pv
  v="$(sed -n 's/.*"Version": *"\([^"]*\)".*/\1/p' <<<"$json")"
  pv="$(sed -n 's/.*"PackageVersion": *"\([^"]*\)".*/\1/p' <<<"$json")"
  echo "$v $pv"
}

failures=0
check() { # check <description> <actual> <expected-regex>
  if [[ "$2" =~ $3 ]]; then
    echo "PASS: $1 -> $2"
  else
    echo "FAIL: $1 -> $2 (expected to match $3)"
    failures=$((failures + 1))
  fi
}

ci=(-p:IsRunningInCI=true -p:GitVersionBranch=main)
local_build=(-p:IsRunningInCI=false)

check "GitVersionNumber=true, CI on main generates a production version" \
  "$(evaluate "${ci[@]}" -p:GitVersionNumber=true)" '^3\.4\.[0-9]{4}\.[0-9]+ 3\.4\.[0-9]{4}\.[0-9]+$'
check "GitVersionNumber=true, local build uses LocalBuildVersionNumber" \
  "$(evaluate "${local_build[@]}" -p:GitVersionNumber=true)" '^0\.0\.20000\.0 0\.0\.20000\.0$'
check "GitVersionNumber=false, CI on main keeps csproj Version" \
  "$(evaluate "${ci[@]}" -p:GitVersionNumber=false)" '^3\.4\.0 3\.4\.0$'
check "GitVersionNumber=false, local build keeps csproj Version" \
  "$(evaluate "${local_build[@]}" -p:GitVersionNumber=false)" '^3\.4\.0 3\.4\.0$'

if (( failures > 0 )); then
  echo "$failures check(s) failed."
  exit 1
fi
echo "All checks passed."
