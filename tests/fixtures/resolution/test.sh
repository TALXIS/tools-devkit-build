#!/usr/bin/env bash
# Toolchain resolution and property rules, checked by evaluation only (nothing is installed).
source "$TESTS_DIR/lib/assert.sh"
cd "$(dirname "$0")/src"
git clean -qfdX . 2>/dev/null || true
here=$PWD

# -getProperty together with -restore reports the evaluation from before package imports, so
# restore first and query afterwards.
prop() { dotnet restore "$1" -nologo >/dev/null 2>&1; dotnet msbuild "$1" -nologo -getProperty:"$2" 2>/dev/null | tail -n 1; }
toolchain() { local label=$1; shift; run_dotnet "$label" msbuild "$@" -restore -t:NodeToolchain -nologo; }

# TypeScriptDir is an undocumented alias of NodeRootPath, normalized to an absolute path
assert_eq "$(prop Legacy.TypeScriptDir/Legacy.TypeScriptDir.csproj NodeRootFullPath)" "$here/Legacy.TypeScriptDir/TS" "TypeScriptDir sets the Node root"
assert_eq "$(prop Legacy.TypeScriptDir/Legacy.TypeScriptDir.csproj TypeScriptDir)" "$here/Legacy.TypeScriptDir/TS" "TypeScriptDir reads back as the absolute Node root"
assert_eq "$(prop Both.Paths/Both.Paths.csproj NodeRootFullPath)" "$here/Both.Paths/web" "NodeRootPath wins over TypeScriptDir"
assert_eq "$(prop Plain.Solution/Plain.Solution.csproj NodeRootFullPath)" "" "non-Node project types get no Node root"

# Selection errors name their source
if toolchain dup Dup.Candidate/Dup.Candidate.csproj; then fail "duplicate candidate accepted"; else pass "duplicate candidate rejected"; fi
assert_grep "registered more than once" "$(last_console)" "duplicate error explains the problem"
assert_grep "Dup.Candidate.csproj" "$(last_console)" "duplicate error names the registering file"
if toolchain tie Tie.Candidate/Tie.Candidate.csproj; then fail "priority tie accepted"; else pass "priority tie rejected"; fi
assert_grep "Multiple node package manager candidates matched with priority 0" "$(last_console)" "tie error explains the problem"

# Rush: an unregistered project warns and prefers pnpm; NodePackageManager=rush is rejected
toolchain rush-unregistered RushRepo/libs/Unregistered/Unregistered.csproj -v:n
assert_grep "is not registered in it" "$(last_console)" "unregistered Rush project warns"
assert_grep "package manager pnpm at" "$(last_console)" "unregistered Rush project prefers pnpm"
if toolchain rush-as-pm RushRepo/libs/Unregistered/Unregistered.csproj -p:NodePackageManager=rush; then fail "NodePackageManager=rush accepted"; else pass "NodePackageManager=rush rejected"; fi
assert_grep "names an orchestrator, not a package manager" "$(last_console)" "NodePackageManager=rush error gives the fix"
toolchain rush-none RushRepo/libs/Unregistered/Unregistered.csproj -p:NodeOrchestrator=None -v:n
assert_not_grep "is not registered in it" "$(last_console)" "NodeOrchestrator=None silences Rush detection"

# NodeBuildCommand replaces the build step and still gets NODE_ENV
expect_success build-override build Build.Override/Build.Override.csproj -c Release
assert_grep "// production" Build.Override/build/main.js "NodeBuildCommand ran with NODE_ENV=production"

finish
