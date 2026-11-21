#!/bin/bash
# Bundle the current 1.6 build for the Steam Workshop.
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

# Folds version.json into Version.cs and About/About.xml, including the commit hash
bash build/apply-version.sh --hash
VERSION=$(grep -Po '(?<=SimpleVersion = ")[^"]+' Source/Common/Version.cs)

cd Source || exit
dotnet build --configuration Release || { echo 'dotnet build FAILED' ; exit 1; }
cd ..

rm -rf Multiplayer/
mkdir -p Multiplayer
cp -r About Textures Assemblies AssembliesCustom Defs Languages Multiplayer/
rm -f Multiplayer/Languages/.git Multiplayer/Languages/LICENSE Multiplayer/Languages/README.md

rm -f "Multiplayer-v$VERSION.zip"
zip -r -q "Multiplayer-v$VERSION.zip" Multiplayer

echo "Ok, $PWD/Multiplayer-v$VERSION.zip ready for uploading to Workshop"
