#!/bin/bash
# Bundle the current 1.7 build for the Steam Workshop.
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
mkdir -p Multiplayer/1.7
cp -r About Textures LoadFolders.xml Multiplayer/
cp -r 1.7/Assemblies 1.7/AssembliesCustom Defs Languages Multiplayer/1.7/
rm -f Multiplayer/1.7/Languages/.git Multiplayer/1.7/Languages/LICENSE Multiplayer/1.7/Languages/README.md

rm -f "Multiplayer-v$VERSION.zip"
zip -r -q "Multiplayer-v$VERSION.zip" Multiplayer

echo "Ok, $PWD/Multiplayer-v$VERSION.zip ready for uploading to Workshop"
