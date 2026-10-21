#!/usr/bin/env bash
# Assemble a runnable folder: package.sh <dest_dir>
set -euo pipefail
cd "$(dirname "$0")/.."
DEST=${1:-build/dist/XenaPC}
mkdir -p "$DEST"
cp build/port/xena.exe build/port/SDL2.dll "$DEST/"
cp port/README_RU.txt "$DEST/README.txt"
echo "packaged into $DEST"
