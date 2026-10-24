#!/usr/bin/env bash
# Static checks for the SDK's MSBuild files; run by tests/run.sh before any fixture.
#  1. Every .props/.targets file is well-formed XML. MSBuild refuses a whole package when one file
#     is not, and "--" inside an XML comment is the usual cause.
#  2. The Node core (Targets/Node/*.targets, not Providers/) names no tool: providers own that.
#  3. Private Node targets follow _Node<Module><Rest> with Module one of Toolchain, Restore,
#     Build, Exec, so a target in a log leads straight to its file.
set -euo pipefail
REPO_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$REPO_DIR"
status=0

while IFS= read -r f; do
  python3 - "$f" <<'PY' || status=1
import sys, xml.dom.minidom
try:
    xml.dom.minidom.parse(sys.argv[1])
except Exception as e:
    print(f"lint: {sys.argv[1]}: not well-formed XML: {e}")
    sys.exit(1)
PY
done < <(git ls-files 'src/**/*.props' 'src/**/*.targets' 'tests/**/*.targets' 'tests/**/*.csproj')

NODE=src/Dataverse/Tasks/msbuild/tasks/Targets/Node
for f in "$NODE"/*.targets; do
  hits=$(python3 - "$f" <<'PY'
import re, sys
s = re.sub(r'<!--.*?-->', '', open(sys.argv[1], encoding='utf-8').read(), flags=re.S)
found = sorted(set(m.group(0).lower() for m in re.finditer(r"(?i)\b(npm|pnpm|yarn|bun|rush)\b", s)))
print(" ".join(found))
PY
)
  if [ -n "$hits" ]; then echo "lint: $f names tools outside a comment: $hits"; status=1; fi
done

while IFS= read -r line; do
  name=${line#*Name=\"}; name=${name%%\"*}
  case "$name" in
    _Node*) [[ "$name" =~ ^_Node(Toolchain|Restore|Build|Exec)[A-Z] ]] || { echo "lint: private target '$name' does not start with _Node<Toolchain|Restore|Build|Exec>"; status=1; } ;;
  esac
done < <(grep -rhoE '<Target Name="_Node[A-Za-z]*"' "$NODE")

[ $status -eq 0 ] && echo "lint: ok"
exit $status
