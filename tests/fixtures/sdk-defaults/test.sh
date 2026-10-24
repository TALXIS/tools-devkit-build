#!/usr/bin/env bash
# Convention over configuration: a ScriptLibrary and a CodeApp with no build script and no
# rollup/vite config build with the SDK's defaults, honour Debug/Release, and land in bin.
source "$TESTS_DIR/lib/assert.sh"
cd "$(dirname "$0")/src"
git clean -qfdX . 2>/dev/null || true
LIB=Lib.Default/bin/%s/net472/fx_default.js
APP=App.Default/bin/%s/net472/fxdefault

expect_success lib-debug build Lib.Default/Lib.Default.csproj -c Debug
assert_grep 'using the SDK default build for ScriptLibrary' "$(last_console)" "lib: SDK default build used"
assert_grep 'sourceMappingURL=data:' "$(printf $LIB Debug)" "lib Debug: inline source map"
assert_grep 'fx_default' "$(printf $LIB Debug)" "lib: UMD global named after ScriptLibraryName"
assert_grep 'TransactionForm' "$(printf $LIB Debug)" "lib: entry point bundled"

expect_success lib-release build Lib.Default/Lib.Default.csproj -c Release
assert_not_grep 'sourceMappingURL' "$(printf $LIB Release)" "lib Release: no source map"
assert_lt "$(file_size "$(printf $LIB Release)")" "$(file_size "$(printf $LIB Debug)")" "lib Release: minified"

expect_success app-debug build App.Default/App.Default.csproj -c Debug
assert_grep 'using the SDK default build for CodeApp' "$(last_console)" "app: SDK default build used"
assert_file "$(printf $APP Debug)/index.html"
expect_success app-release build App.Default/App.Default.csproj -c Release
assert_file "$(printf $APP Release)/index.html"
dbg=$(find "$(printf $APP Debug)/assets" -name '*.js' | head -n 1); rel=$(find "$(printf $APP Release)/assets" -name '*.js' | head -n 1)
assert_lt "$(file_size "$rel")" "$(file_size "$dbg")" "app Release: minified"
assert_not_grep 'sourceMappingURL' "$rel" "app Release: no source map"

finish
