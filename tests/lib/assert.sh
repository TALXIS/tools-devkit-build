#!/usr/bin/env bash
# Shared helpers for fixture tests. Source this file; call `run_dotnet` for every dotnet
# invocation so each one gets its own binlog under tests/.work/logs.
set -u

TESTS_DIR=${TESTS_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}
WORK_DIR=${WORK_DIR:-$TESTS_DIR/.work}
LOG_DIR=$WORK_DIR/logs
mkdir -p "$LOG_DIR"

FAILURES=0
STEP=0

log()  { printf '%s\n' "$*"; }
pass() { log "  ok    $*"; }
fail() { log "  FAIL  $*"; FAILURES=$((FAILURES + 1)); }

# run_dotnet <label> <dotnet args...>   (runs in the current directory)
run_dotnet() {
  local label=$1; shift
  STEP=$((STEP + 1))
  local base="$LOG_DIR/${FIXTURE_NAME:-fixture}-$(printf '%02d' "$STEP")-$label"
  log "  dotnet $* [$label]"
  if dotnet "$@" -bl:"$base.binlog" -flp:"LogFile=$base.log;Verbosity=detailed" > "$base.console" 2>&1; then
    return 0
  else
    local rc=$?
    log "  dotnet exited with $rc; last lines of $base.console:"
    tail -n 25 "$base.console" | sed 's/^/    | /'
    return $rc
  fi
}

# expect_success <label> <dotnet args...>
expect_success() { local label=$1; shift; if run_dotnet "$label" "$@"; then pass "$label succeeded"; else fail "$label failed"; fi; }
# expect_failure <label> <dotnet args...>
expect_failure() { local label=$1; shift; if run_dotnet "$label" "$@"; then fail "$label unexpectedly succeeded"; else pass "$label failed as expected"; fi; }

last_console() { ls -t "$LOG_DIR"/*.console | head -n 1; }

assert_file()      { [ -f "$1" ] && pass "exists: ${1#$PWD/}" || fail "missing: ${1#$PWD/}"; }
assert_no_file()   { [ ! -e "$1" ] && pass "absent: ${1#$PWD/}" || fail "unexpectedly present: ${1#$PWD/}"; }
assert_dir()       { [ -d "$1" ] && pass "dir exists: ${1#$PWD/}" || fail "dir missing: ${1#$PWD/}"; }
# assert_grep <pattern> <file> [message]
assert_grep()      { if grep -qE -- "$1" "$2" 2>/dev/null; then pass "${3:-found /$1/ in ${2#$PWD/}}"; else fail "${3:-missing /$1/ in ${2#$PWD/}}"; fi; }
assert_not_grep()  { if [ ! -f "$2" ]; then fail "${3:-no /$1/ in ${2#$PWD/}} (file missing)"; elif grep -qE -- "$1" "$2"; then fail "${3:-unexpected /$1/ in ${2#$PWD/}}"; else pass "${3:-no /$1/ in ${2#$PWD/}}"; fi; }
# assert_lt <a> <b> <message>
assert_lt()        { if [ -n "$1" ] && [ -n "$2" ] && [ "$1" -lt "$2" ]; then pass "$3 ($1 < $2)"; else fail "$3 ('$1' vs '$2')"; fi; }
assert_eq()        { if [ "$1" = "$2" ]; then pass "$3"; else fail "$3 ('$1' != '$2')"; fi; }
assert_ne()        { if [ "$1" != "$2" ]; then pass "$3"; else fail "$3 (both '$1')"; fi; }
file_size()        { stat -c %s "$1" 2>/dev/null || stat -f %z "$1"; }
file_hash()        { sha256sum "$1" | cut -d' ' -f1; }
# zip_grep <pattern> <zip> <entry> [message]
zip_grep()         { if unzip -p "$2" "$3" 2>/dev/null | grep -qE -- "$1"; then pass "${4:-found /$1/ in ${2#$PWD/}:$3}"; else fail "${4:-missing /$1/ in ${2#$PWD/}:$3}"; fi; }
zip_list()         { unzip -Z1 "$1" 2>/dev/null; }
# count_launches <shimlog> <regex>  (only meaningful when run.sh installed the shims)
count_launches()   { grep -c "^START.*$2" "$1" 2>/dev/null || echo 0; }

finish() {
  if [ "$FAILURES" -eq 0 ]; then log "PASS ${FIXTURE_NAME:-fixture}"; exit 0; else log "FAIL ${FIXTURE_NAME:-fixture}: $FAILURES assertion(s) failed"; exit 1; fi
}
