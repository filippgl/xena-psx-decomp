#!/bin/bash
# Image-to-3D Remaster prop: concept image -> Hunyuan3D-2 shape -> paint -> Unity install.
# usage: tools/generate_prop.sh <concept.png> <name> [faces]   (run from decomp/)
# See docs/remaster-assets.md for the Hunyuan3D licence; PrototypeRemasterProps maps names.
set -e
concept=$1; name=$2; faces=${3:-12000}
venv="../_tools/Hunyuan3D-2/venv/Scripts/python"
out="build/hy3d/$name"
"$venv" tools/hunyuan_shape.py "$concept" "$out" --faces "$faces" > "build/hy3d/$name-shape.log" 2>&1
tail -1 "build/hy3d/$name-shape.log"
"$venv" tools/hunyuan_paint.py "$out" > "build/hy3d/$name-paint.log" 2>&1
tail -1 "build/hy3d/$name-paint.log"
"$venv" tools/install_generated_prop.py "$out" "$name"
