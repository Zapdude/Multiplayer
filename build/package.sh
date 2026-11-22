#!/usr/bin/env bash
# Assemble the shipped mod folder from the repository root.
#
# Everything at the top level ships except the repository-only entries below, so
# adding a content folder (Sounds/, Patches/, a new version folder) does not
# require editing a workflow. Dot entries are never shipped.
#
# Usage:
#   build/package.sh --dest /abs/path/Multiplayer [--zip /abs/path/out.zip]
#
# --dest must be outside the repository: packaging inside the tree would copy
# the destination into itself. Both release workflows package into a temporary
# directory and then move or zip the result.
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
DEST=""
ZIP=""

usage() {
  echo "usage: $0 --dest <absolute path outside the repository> [--zip <zip path>]" >&2
  exit 2
}

while [ $# -gt 0 ]; do
  case "$1" in
    --dest) [ $# -ge 2 ] || usage; DEST="$2"; shift 2 ;;
    --zip)  [ $# -ge 2 ] || usage; ZIP="$2"; shift 2 ;;
    *) usage ;;
  esac
done
[ -n "$DEST" ] || usage

# Resolve the destination even when its parent does not exist yet.
mkdir -p -- "$(dirname -- "$DEST")"
DEST_ABS="$(cd -- "$(dirname -- "$DEST")" && pwd -P)/$(basename -- "$DEST")"
case "$DEST_ABS" in
  "$ROOT"/*) echo "package: --dest must be outside $ROOT" >&2; exit 2 ;;
esac

# Repository-only entries. Keep this list about the repository, not about mod
# content: a new content folder must ship without being added here. Generated
# directories (output/, Multiplayer/, zips) are listed so a dirty tree cannot
# leak them into a package.
DENY=" .github build Source docs output Multiplayer version.json global.json workshop_bundler.sh README.md LICENSE "

cd "$ROOT"
rm -rf -- "$DEST_ABS"
mkdir -p -- "$DEST_ABS"

shopt -s dotglob nullglob
for entry in *; do
  case "$entry" in
    .*) continue ;;
  esac
  case "$DENY" in
    *" $entry "*) continue ;;
  esac
  case "$entry" in
    Multiplayer*.zip) continue ;;
  esac
  cp -r -- "$entry" "$DEST_ABS/"
done
shopt -u dotglob nullglob

# Ship only the per-language folders. The licence, readme, the l10n maintenance
# script and VCS metadata belong to the repository, not to the mod.
if [ -d "$DEST_ABS/Languages" ]; then
  find "$DEST_ABS/Languages" -mindepth 1 -maxdepth 1 -type f -delete
fi

if [ -n "$ZIP" ]; then
  PYTHON="${PYTHON:-}"
  if [ -z "$PYTHON" ]; then
    if command -v python3 >/dev/null 2>&1; then
      PYTHON=python3
    elif command -v python >/dev/null 2>&1; then
      PYTHON=python
    else
      echo "package: python3/python is required to write the zip" >&2
      exit 1
    fi
  fi

  mkdir -p -- "$(dirname -- "$ZIP")"
  rm -f -- "$ZIP"
  # Python's zipfile is used instead of the `zip` command so the archive can be
  # built on any machine that can already run apply-version.sh.
  "$PYTHON" - "$ZIP" "$(dirname -- "$DEST_ABS")" "$(basename -- "$DEST_ABS")" <<'PY'
import os
import sys
import zipfile
from pathlib import Path

zip_path, parent, root_name = sys.argv[1], Path(sys.argv[2]), sys.argv[3]
with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as archive:
    for directory, dirnames, filenames in os.walk(parent / root_name):
        dirnames.sort()
        filenames.sort()
        for name in filenames:
            full = Path(directory) / name
            archive.write(full, full.relative_to(parent).as_posix())
PY
fi

echo "package: $DEST_ABS"
[ -z "$ZIP" ] || echo "package: $ZIP"
