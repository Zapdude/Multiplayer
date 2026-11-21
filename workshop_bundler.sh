#!/bin/bash
# Bundle the current 1.7 build for the Steam Workshop.
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

# Folds version.json into Version.cs and About/About.xml, including the commit hash
bash build/apply-version.sh --hash
# Read the version with sed rather than grep -P, which needs a UTF-8 locale.
VERSION=$(sed -n 's/.*SimpleVersion = "\([^"]*\)".*/\1/p' Source/Common/Version.cs)

cd Source || exit
dotnet build --configuration Release || { echo 'dotnet build FAILED' ; exit 1; }
cd ..

rm -rf Multiplayer/
mkdir -p Multiplayer/1.7
cp -r About Textures LoadFolders.xml Multiplayer/
cp -r 1.7/Assemblies 1.7/AssembliesCustom Defs Languages Multiplayer/1.7/
# Ship only the per-language folders. The licence, readme, the l10n maintenance script and any VCS
# metadata belong to the repository, not to the mod.
find Multiplayer/1.7/Languages -mindepth 1 -maxdepth 1 -type f -delete

rm -f "Multiplayer-v$VERSION.zip"
zip -r -q "Multiplayer-v$VERSION.zip" Multiplayer

echo "Ok, $PWD/Multiplayer-v$VERSION.zip ready for uploading to Workshop"
