#!/usr/bin/env bash
# Assemble a runnable folder: package.sh <dest_dir>
# Includes the redrawn texture pack from build/texreplace when present.
set -euo pipefail
cd "$(dirname "$0")/.."
DEST=${1:-build/dist/XenaPC}
mkdir -p "$DEST"
cp build/port/xena.exe build/port/SDL2.dll "$DEST/"
cp port/README_RU.txt "$DEST/README.txt"
if ls build/texreplace/*.png >/dev/null 2>&1; then
    mkdir -p "$DEST/textures"
    cp build/texreplace/*.png "$DEST/textures/"
    cp build/texdump2/manifest.txt "$DEST/textures/manifest.txt"
    echo "texture pack: $(ls build/texreplace/*.png | wc -l) textures"
fi
echo "packaged into $DEST"
