#!/usr/bin/env bash
# Build and bundle the current branch for the Steam Workshop.
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

# Fold version.json into Version.cs and About/About.xml, including the commit hash.
bash build/apply-version.sh --hash
# Read the version with sed rather than grep -P, which needs a UTF-8 locale.
VERSION=$(sed -n 's/.*SimpleVersion = "\([^"]*\)".*/\1/p' Source/Common/Version.cs)

dotnet build Source --configuration Release

TMP="$(mktemp -d)"
trap 'rm -rf -- "$TMP"' EXIT

ZIP="$ROOT/Multiplayer-v$VERSION.zip"
bash build/package.sh --dest "$TMP/Multiplayer" --zip "$ZIP"
bash build/verify-package.sh --zip "$ZIP"

echo "Ok, $ZIP ready for uploading to Workshop"
