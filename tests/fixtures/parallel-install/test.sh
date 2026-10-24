#!/usr/bin/env bash
# Two projects sharing one npm workspace root, built in parallel (-m): exactly one npm install
# runs. The other project waits for the root's lock, then finds the install stamp current.
source "$TESTS_DIR/lib/assert.sh"
cd "$(dirname "$0")/src"
git clean -qfdX . 2>/dev/null || true

expect_success parallel build Parallel.slnx -m:2 -c Debug
log=$(ls -t "$LOG_DIR/${FIXTURE_NAME}"-*-parallel.log | head -n 1)
installs=$(grep -cE "Running in '[^']*': npm install" "$log")
assert_eq "$installs" "1" "one npm install for the shared root"
assert_file node_modules/.node-restore.stamp
assert_file LibA/bin/Debug/net472/fx_liba.js
assert_file LibB/bin/Debug/net472/fx_libb.js

# A rebuild installs nothing
expect_success rebuild build Parallel.slnx -m:2 -c Debug
log=$(ls -t "$LOG_DIR/${FIXTURE_NAME}"-*-rebuild.log | head -n 1)
assert_eq "$(grep -cE "Running in '[^']*': npm install" "$log")" "0" "rebuild runs no install"

finish
