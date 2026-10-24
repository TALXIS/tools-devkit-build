#!/usr/bin/env bash
# Rush workspace: a registered project restores and builds through the version-pinned Rush
# bootstrap; the up-to-date gate skips Rush on the next build; an unregistered project in the
# same repository warns and uses pnpm directly; CI uses the frozen "rush install".
source "$TESTS_DIR/lib/assert.sh"
cd "$(dirname "$0")/src"
git clean -qfdX . 2>/dev/null || true
log_of() { ls -t "$LOG_DIR/${FIXTURE_NAME}"-*"-$1.log" | head -n 1; }

REG=libs/Registered/Registered.csproj
UNREG=libs/Unregistered/Unregistered.csproj

# First build: never-installed workspace, scoped install, then rush build scoped to the project
expect_success first build "$REG" -c Debug
assert_grep 'install-run-rush.js" install --to \.' "$(log_of first)" "first build: scoped rush install"
assert_grep 'install-run-rush.js" build --to \.' "$(log_of first)" "first build: rush build scoped to the project"
assert_file libs/Registered/bin/Debug/net472/fx_registered.js

# Second build: the gate skips Rush's install, the build still goes through Rush
expect_success second build "$REG" -c Debug
assert_grep 'install state is up to date' "$(log_of second)" "second build: install skipped by the gate"
assert_not_grep 'Rush (install|update) finished' "$(log_of second)" "second build: Rush install did not run"

# Changing a registered package.json invalidates the gate
touch libs/Registered/package.json
expect_success after-edit build "$REG" -c Debug
assert_grep 'install-run-rush.js" update' "$(log_of after-edit)" "edited package.json: rush update runs again"

# CI: frozen install from the committed lockfile
rm -rf common/temp libs/Registered/node_modules
CI=true expect_success ci build "$REG" -c Release
assert_grep 'install-run-rush.js" install' "$(log_of ci)" "CI: rush install (frozen)"
assert_not_grep 'install-run-rush.js" update' "$(log_of ci)" "CI: never rush update"
assert_file libs/Registered/bin/Release/net472/fx_registered.js

# Unregistered project inside the Rush repo
expect_success unregistered build "$UNREG" -c Debug
assert_grep 'is not registered in it' "$(last_console)" "unregistered: warning"
assert_grep 'pnpm install' "$(log_of unregistered)" "unregistered: pnpm, the Rush workspace's package manager"
assert_file libs/Unregistered/bin/Debug/net472/fx_unregistered.js

finish
