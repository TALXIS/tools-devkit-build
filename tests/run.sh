#!/usr/bin/env bash
# Fixture runner for the TALXIS DevKit Build SDK.
#
#   tests/run.sh                 pack the SDK, then run every fixture under tests/fixtures
#   tests/run.sh config-propagation ...   run only the named fixtures
#   SKIP_PACK=1 tests/run.sh     reuse the packages already in tests/.feed
#
# Each fixture is a folder with a test.sh; it runs in place (its Node and MSBuild outputs are
# ignored by tests/.gitignore). The SDK is packed as 0.0.0-local into tests/.feed and resolved
# through tests/fixtures/nuget.config; the NuGet package cache is isolated under tests/.work so a repack
# of the same version is always picked up.
set -euo pipefail

TESTS_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPO_DIR=$(cd "$TESTS_DIR/.." && pwd)
export TESTS_DIR
export WORK_DIR=$TESTS_DIR/.work
export SDK_VERSION=0.0.0-local
FEED_DIR=$TESTS_DIR/.feed

export NUGET_PACKAGES=$WORK_DIR/nuget-packages
export NUGET_HTTP_CACHE_PATH=$WORK_DIR/nuget-http-cache
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
# Fixtures must behave the same whatever CI system runs them; each test sets CI explicitly.
unset CI TF_BUILD GITHUB_ACTIONS GITLAB_CI CIRCLECI JENKINS_URL TEAMCITY_VERSION IsRunningInCI || true

mkdir -p "$WORK_DIR" "$FEED_DIR"

"$TESTS_DIR/lint.sh"

if [ "${SKIP_PACK:-0}" != "1" ]; then
  echo "== packing SDK $SDK_VERSION into ${FEED_DIR#$REPO_DIR/}"
  rm -f "$FEED_DIR"/*.nupkg
  dotnet pack "$REPO_DIR/TALXIS.DevKit.Build.slnx" -c Release -p:Version=$SDK_VERSION -p:PackageVersion=$SDK_VERSION -o "$FEED_DIR" \
    -bl:"$WORK_DIR/pack.binlog" > "$WORK_DIR/pack.console" 2>&1 || { tail -n 30 "$WORK_DIR/pack.console"; echo "pack failed, see $WORK_DIR/pack.console"; exit 1; }
  echo "   $(ls "$FEED_DIR"/*.nupkg | wc -l) packages"
fi
# Always drop the cached copy of the SDK under test so the freshly packed one is used.
rm -rf "$NUGET_PACKAGES"/talxis.devkit.build.*

if [ $# -gt 0 ]; then fixtures=("$@"); else fixtures=($(cd "$TESTS_DIR/fixtures" && ls -d */ | tr -d /)); fi

overall=0
for name in "${fixtures[@]}"; do
  dir=$TESTS_DIR/fixtures/$name
  [ -f "$dir/test.sh" ] || { echo "== $name: no test.sh, skipped"; continue; }
  echo "== $name"
  if ( cd "$dir" && FIXTURE_NAME=$name bash ./test.sh ); then :; else overall=1; fi
done
exit $overall
