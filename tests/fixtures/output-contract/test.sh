#!/usr/bin/env bash
# Output contract: deliverables land in bin (TargetDir/OutDir), found first in BUILD_OUTPUT_DIR,
# then in the tool's own folder; a missing deliverable is an error; production deliverables are
# checked for inline source maps and size; code app deliverables never carry stale assets.
source "$TESTS_DIR/lib/assert.sh"
cd "$(dirname "$0")/src"
git clean -qfdX . 2>/dev/null || true
echo 'console.log("v1");' > App.Assets/src.js

# BUILD_OUTPUT_DIR is honoured and becomes the deliverable
expect_success outdir-release build Lib.OutputDir/Lib.OutputDir.csproj -c Release
assert_grep '"production"' Lib.OutputDir/bin/Release/net472/fx_outdir.js "BUILD_OUTPUT_DIR output became the deliverable (NODE_ENV=production)"
assert_no_file Lib.OutputDir/build/fx_outdir.js
assert_file Lib.OutputDir/obj/Release/net472/node/fx_outdir.js

# Wrong file name: clear error naming the searched folders
expect_failure wrong-name build Lib.WrongName/Lib.WrongName.csproj -c Debug
assert_grep 'did not produce fx_expected.js' "$(last_console)" "missing deliverable error names the file"

# Release check: inline source map fails Release, Debug is fine
expect_success inline-debug build Lib.InlineMap/Lib.InlineMap.csproj -c Debug
expect_failure inline-release build Lib.InlineMap/Lib.InlineMap.csproj -c Release
assert_grep 'contains an inline source map' "$(last_console)" "Release rejects an inline source map"

# Size limit
expect_failure size-release build Lib.OutputDir/Lib.OutputDir.csproj -c Release -p:NodeBuildMaxOutputSize=10
assert_grep 'over the 10-byte limit' "$(last_console)" "Release rejects a deliverable over NodeBuildMaxOutputSize"

# Code app: rebuild after a source change leaves exactly one hashed asset in the deliverable
expect_success app-1 build App.Assets/App.Assets.csproj -c Debug
echo 'console.log("v2");' > App.Assets/src.js
expect_success app-2 build App.Assets/App.Assets.csproj -c Debug
count=$(find App.Assets/bin/Debug/net472/fxassets/assets -name 'index-*.js' | wc -l)
assert_eq "$count" "1" "code app deliverable holds only the current hashed asset"
assert_grep 'v2' "$(find App.Assets/bin/Debug/net472/fxassets/assets -name 'index-*.js' | head -n 1)" "code app deliverable is the latest build"

finish
