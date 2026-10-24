#!/usr/bin/env bash
# Package managers that are not built into the SDK plug in from outside, through the same
# contract the built-in npm and pnpm providers use: one .targets file registering candidate
# metadata. No SDK core change is needed (Yarn and Bun ship only in this fixture).
source "$TESTS_DIR/lib/assert.sh"
cd "$(dirname "$0")/src"
git clean -qfdX . 2>/dev/null || true

log_of() { ls -t "$LOG_DIR/${FIXTURE_NAME}"-*"-$1.log" | head -n 1; }

# Yarn, detected from yarn.lock, local build
expect_success yarn-debug build Scripts.Yarn/Scripts.Yarn.csproj -c Debug
assert_file Scripts.Yarn/node_modules/.yarn-integrity
assert_grep 'yarn install' "$(log_of yarn-debug)" "yarn: installed through the external provider"
assert_grep 'yarn run build' "$(log_of yarn-debug)" "yarn: built through the external provider"
assert_file Scripts.Yarn/bin/Debug/net472/fx_yarn.js

# Bun, detected from bun.lock, in CI mode: the provider's frozen install command is used
CI=true expect_success bun-ci build Scripts.Bun/Scripts.Bun.csproj -c Release
assert_grep 'bun install --frozen-lockfile' "$(log_of bun-ci)" "bun: CI uses the provider's frozen install"
assert_grep 'bun run build' "$(log_of bun-ci)" "bun: built through the external provider"
assert_file Scripts.Bun/bin/Release/net472/fx_bun.js

finish
