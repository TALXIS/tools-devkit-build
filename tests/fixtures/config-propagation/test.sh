#!/usr/bin/env bash
# Debug/Release must propagate from a Solution build to every referenced Node project, with no
# source change between builds: Debug gives development bundles and an unmanaged solution,
# Release gives production bundles (minified, no inline source maps, no eval) and a managed one.
# Solution schema validation runs on every build, including the PCF control's data.xml.
source "$TESTS_DIR/lib/assert.sh"
cd "$(dirname "$0")/src"
git clean -qfdX . 2>/dev/null || true

SOL=Solution.Sample
PCF_BUNDLE=Pcf.Sample/out/controls/SampleControl/bundle.js
ROLLUP=Scripts.Rollup/bin/%s/net472/fx_rollup.js
TSC=Scripts.Tsc/bin/%s/net472/fx_tsc.js

zip_for() { ls "$SOL"/bin/"$1"/net472/*.zip 2>/dev/null | head -n 1; }
managed_flag() { unzip -p "$1" solution.xml | grep -oE '<Managed>[0-9]</Managed>' | head -n 1; }
code_app_js() { find App.Code/dist/assets -name '*.js' | head -n 1; }

build() {
  local cfg=$1 label=$2
  expect_success "$label" build "$SOL/$SOL.csproj" -c "$cfg"
  # Solution validation stays on: the PCF reference's ControlManifest.xml.data.xml must validate.
  assert_not_grep 'TXVAL[0-9]+' "$(last_console)" "$label: solution validation passes"
}

check_debug() {
  local tag=$1
  assert_file "$PCF_BUNDLE"
  assert_grep 'eval\(' "$PCF_BUNDLE" "$tag: PCF bundle is a development build (eval devtool)"
  assert_grep 'sourceMappingURL=data:' "$(printf $ROLLUP Debug)" "$tag: rollup library has an inline source map"
  local z; z=$(zip_for Debug); assert_file "$z"
  assert_eq "$(managed_flag "$z")" "<Managed>0</Managed>" "$tag: Debug solution is unmanaged"
}

# 1. Debug
build Debug debug-1
check_debug "debug-1"
PCF_DEBUG_SIZE=$(file_size "$PCF_BUNDLE")
ROLLUP_DEBUG_SIZE=$(file_size "$(printf $ROLLUP Debug)")
APP_DEBUG_SIZE=$(file_size "$(code_app_js)")

# 2. Release, no source change
build Release release
assert_not_grep 'eval\(' "$PCF_BUNDLE" "release: PCF bundle has no eval"
assert_lt "$(file_size "$PCF_BUNDLE")" "$PCF_DEBUG_SIZE" "release: PCF bundle smaller than Debug"
R=$(printf $ROLLUP Release)
assert_not_grep 'sourceMappingURL=data:' "$R" "release: rollup library has no inline source map"
assert_lt "$(file_size "$R")" "$ROLLUP_DEBUG_SIZE" "release: rollup library smaller than Debug"
assert_lt "$(file_size "$(code_app_js)")" "$APP_DEBUG_SIZE" "release: code app bundle smaller than Debug"
Z=$(zip_for Release); assert_file "$Z"
assert_eq "$(managed_flag "$Z")" "<Managed>1</Managed>" "release: Release solution is managed"
if zip_list "$Z" | grep -qE '\.map$'; then fail "release: solution zip contains .map files"; else pass "release: solution zip has no .map files"; fi
entry=$(zip_list "$Z" | grep -E 'WebResources/fx_rollup\.js' | head -n 1)
if [ -n "$entry" ] && [ "$(unzip -p "$Z" "$entry" | sha256sum | cut -d' ' -f1)" = "$(file_hash "$R")" ]; then
  pass "release: zip carries the Release rollup bundle"; else fail "release: zip rollup web resource ($entry) differs from the Release bundle"; fi
entry=$(zip_list "$Z" | grep -E 'Controls/.*SampleControl/bundle\.js$' | head -n 1)
if [ -n "$entry" ] && ! unzip -p "$Z" "$entry" | grep -q 'eval('; then
  pass "release: zip carries the production PCF bundle"; else fail "release: zip PCF bundle ($entry) missing or a development build"; fi

# 3. Back to Debug, no source change: the PCF bundle must be rebuilt in development mode
build Debug debug-2
check_debug "debug-2"

finish
