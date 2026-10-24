#!/usr/bin/env bash
# A pnpm-managed PCF project must receive the build mode as a pcf-scripts flag. pnpm forwards
# script arguments without an npm-style "--" separator; with the separator pcf-scripts sees
# "--" as a sub-command and fails.
source "$TESTS_DIR/lib/assert.sh"
cd "$(dirname "$0")/src"
git clean -qfdX . 2>/dev/null || true
PROJ=Pcf.Sample/Pcf.Sample.csproj
BUNDLE=Pcf.Sample/out/controls/SampleControl/bundle.js

expect_success debug build "$PROJ" -c Debug
assert_file "$BUNDLE"
assert_grep 'eval\(' "$BUNDLE" "debug: development bundle"
assert_not_grep 'Not a valid sub-command' "$(last_console)" "debug: pcf-scripts accepted the arguments"

expect_success release build "$PROJ" -c Release
assert_not_grep 'eval\(' "$BUNDLE" "release: production bundle"

finish
