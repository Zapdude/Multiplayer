#!/usr/bin/env bash
# Fail when a packaged mod folder or zip does not match version.json.
#
# Usage: build/verify-package.sh --dir <folder> | --zip <archive>
#
# Checks the things that have silently broken before: the tag in Manifest.xml,
# the modVersion/commit stamp, the assemblies, the language-folder cleanup, and
# that repository-only paths stayed out of the package. A gate that never fires
# is indistinguishable from a broken gate; see docs/handoffs/multiplayer-fork-handoff.md.
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
MODE=""
TARGET=""

usage() {
  echo "usage: $0 --dir <folder> | --zip <archive>" >&2
  exit 2
}

while [ $# -gt 0 ]; do
  case "$1" in
    --dir) [ $# -ge 2 ] || usage; MODE=dir; TARGET="$2"; shift 2 ;;
    --zip) [ $# -ge 2 ] || usage; MODE=zip; TARGET="$2"; shift 2 ;;
    *) usage ;;
  esac
done
[ -n "$MODE" ] || usage

PYTHON="${PYTHON:-}"
if [ -z "$PYTHON" ]; then
  if command -v python3 >/dev/null 2>&1; then
    PYTHON=python3
  elif command -v python >/dev/null 2>&1; then
    PYTHON=python
  else
    echo "verify-package: python3/python is required" >&2
    exit 1
  fi
fi

"$PYTHON" - "$MODE" "$TARGET" "$ROOT" <<'PY'
import json
import re
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

mode, target, root = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
meta = json.loads((root / "version.json").read_text(encoding="utf-8"))
version = meta["version"]
tag = f"v{version}{meta.get('releaseTagSuffix', '')}"
rimworld = meta["rimworld"]

errors = []
files = {}

if mode == "dir":
    if not target.is_dir():
        sys.exit(f"verify-package: not a directory: {target}")
    names = []
    for path in target.rglob("*"):
        rel = path.relative_to(target).as_posix()
        names.append(rel)
        if path.is_file() and rel in ("About/About.xml", "About/Manifest.xml", "LoadFolders.xml"):
            files[rel] = path.read_bytes()
else:
    if not target.is_file():
        sys.exit(f"verify-package: not a file: {target}")
    with zipfile.ZipFile(target) as archive:
        raw = archive.namelist()
        roots = {name.split("/", 1)[0] for name in raw if "/" in name}
        if len(roots) != 1:
            sys.exit(f"verify-package: expected exactly one root folder, found {sorted(roots)}")
        prefix = roots.pop() + "/"
        names = [name[len(prefix):].rstrip("/") for name in raw if name.startswith(prefix)]
        for rel in ("About/About.xml", "About/Manifest.xml", "LoadFolders.xml"):
            try:
                files[rel] = archive.read(prefix + rel)
            except KeyError:
                pass

names = [name.rstrip("/") for name in names if name.rstrip("/")]

def fail(message):
    errors.append(message)

def has_dir(name):
    return name in names or any(n.startswith(name + "/") for n in names)

for required in ("About/About.xml", "About/Manifest.xml", "Defs", "Textures", "Languages"):
    if not has_dir(required) and required not in files:
        fail(f"missing {required}")

if "About/About.xml" in files:
    about = ET.fromstring(files["About/About.xml"])
    package_id = about.findtext("packageId")
    if package_id != "zap.multiplayerbodged":
        fail(f"packageId is {package_id!r}, expected 'zap.multiplayerbodged'")
    mod_version = about.findtext("modVersion") or ""
    if not re.fullmatch(re.escape(version) + r"( \([0-9a-f]{7,40}\))?", mod_version):
        fail(f"modVersion is {mod_version!r}, expected {version!r} with an optional commit hash")
    supported = [node.text for node in about.findall("supportedVersions/li")]
    if rimworld not in supported:
        fail(f"supportedVersions {supported} does not include {rimworld!r}")

if "About/Manifest.xml" in files:
    manifest = ET.fromstring(files["About/Manifest.xml"])
    download = (manifest.findtext("downloadUri") or "").strip()
    update = (manifest.findtext("manifestUri") or "").strip()
    if not download.endswith(f"/releases/tag/{tag}"):
        fail(f"downloadUri {download!r} does not name the derived tag {tag!r}")
    if "/releases/" in update:
        fail(f"manifestUri {update!r} points at a tag; installed copies would stop seeing updates")
    if not update.endswith("/About/Manifest.xml"):
        fail(f"manifestUri {update!r} does not end with /About/Manifest.xml")

dlls = [name for name in names if name.lower().endswith(".dll")]
basenames = {name.rsplit("/", 1)[-1] for name in dlls}
for required in ("Multiplayer.dll", "MultiplayerCommon.dll"):
    if required not in basenames:
        fail(f"missing assembly {required}")
if not any("/Assemblies/" in f"/{name}" or "/AssembliesCustom/" in f"/{name}" for name in dlls):
    fail("no assembly under Assemblies/ or AssembliesCustom/")

language_files = [
    name for name in names
    if re.fullmatch(r"Languages/[^/]+", name)
    and not any(other.startswith(name + "/") for other in names)
]
if language_files:
    fail(f"repository files left in Languages/: {sorted(language_files)}")

if any(name == "1.7" or name.startswith("1.7/") for name in names) and "LoadFolders.xml" not in names:
    fail("package contains 1.7/ but no LoadFolders.xml")

repository_only = re.compile(
    r"^(Source|build|docs|\.github|output|version\.json|global\.json|"
    r"workshop_bundler\.sh|README\.md|LICENSE)(/|$)"
)
for first in sorted({name.split("/", 1)[0] for name in names}):
    if repository_only.match(first):
        fail(f"repository-only path shipped: {first}")

if errors:
    print(f"verify-package: {len(errors)} problem(s) in {target}")
    for error in errors:
        print(f"  - {error}")
    sys.exit(1)

print(
    f"verify-package: ok - {tag}, {len(dlls)} assemblies, "
    f"{len([n for n in names if n.startswith('Languages/')])} language entries"
)
PY
