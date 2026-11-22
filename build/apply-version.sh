#!/usr/bin/env bash
# Fold version.json into the files that must carry the version at build/package time.
# Run before building for a release; normal development builds can use checked-in values.
#
# The release tag is derived from the version here and nowhere else. Bumping version in
# version.json is what mints a new tag; until then every build refreshes the same one.
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

PYTHON="${PYTHON:-}"
if [ -z "$PYTHON" ]; then
  if command -v python3 >/dev/null 2>&1; then
    PYTHON=python3
  elif command -v python >/dev/null 2>&1; then
    PYTHON=python
  else
    echo "apply-version: python3/python is required" >&2
    exit 1
  fi
fi

HASH=""
case "${1:-}" in
  --hash)
    if command -v git >/dev/null 2>&1; then
      HASH="$(git rev-parse --short HEAD 2>/dev/null || true)"
    fi
    ;;
  "")
    ;;
  *)
    echo "usage: $0 [--hash]" >&2
    exit 2
    ;;
esac

"$PYTHON" - "$HASH" <<'PY'
import json
import re
import sys
from pathlib import Path

root = Path.cwd()
meta = json.loads((root / "version.json").read_text(encoding="utf-8"))
version = meta["version"]
assembly_version = meta["assemblyVersion"]
protocol = int(meta["protocol"])
tag_suffix = meta.get("releaseTagSuffix", "")
prerelease = bool(meta.get("prerelease", False))
release_tag = f"v{version}{tag_suffix}"
hash_suffix = f" ({sys.argv[1]})" if sys.argv[1] else ""

def replace_one(path, pattern, replacement):
    path = root / path
    with path.open("r", encoding="utf-8", newline="") as f:
        text = f.read()
    new, count = re.subn(pattern, replacement, text, count=1)
    if count != 1:
        raise SystemExit(f"apply-version: expected exactly one match for {pattern!r} in {path}")
    with path.open("w", encoding="utf-8", newline="") as f:
        f.write(new)

replace_one(
    "Source/Common/Version.cs",
    r'public const string SimpleVersion = "[^"]*";',
    f'public const string SimpleVersion = "{version}";',
)
replace_one(
    "Source/Common/Version.cs",
    r'public const string AssemblyVersion = "[^"]*";',
    f'public const string AssemblyVersion = "{assembly_version}";',
)
replace_one(
    "Source/Common/Version.cs",
    r'public const int Protocol = \d+;',
    f'public const int Protocol = {protocol};',
)
replace_one(
    "About/About.xml",
    r'<modVersion>.*?</modVersion>',
    f'<modVersion>{version}{hash_suffix}</modVersion>',
)
# The download always names the release tag for this version, so a version bump cannot
# leave the manifest pointing at the previous release.
replace_one(
    "About/Manifest.xml",
    r'<downloadUri>.*?</downloadUri>',
    f'<downloadUri>https://github.com/Zapdude/Multiplayer/releases/tag/{release_tag}</downloadUri>',
)

print(f"version={version}")
print(f"assemblyVersion={assembly_version}")
print(f"protocol={protocol}")
print(f"releaseTag={release_tag}")
print(f"prerelease={'true' if prerelease else 'false'}")
PY
