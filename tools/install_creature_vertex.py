#!/usr/bin/env python3
"""Flatten build/creature-vertex/01/creature-vertex.json (tools/export_creature_vertex.mjs) for Unity's
JsonUtility and install it as Resources/Creatures/creature_vertex.json in both Unity projects.

usage: python tools/install_creature_vertex.py [--root .]

Per model (Lark, Bfly1, Bfly2) and SMA slot-0 frame: per mesh the vertex positions in the imported
mesh order (primitives concatenated, corners as in the .xenamodel; PS1 units, x, y, -z under the
SourceToYUp root). Displayed frame = frames[+0xE6 - 1]; +0xE6 = 0 is the rest mesh
(docs/xena-lark-generator.md)."""
import argparse, json, os

ap = argparse.ArgumentParser(); ap.add_argument('--root', default='.'); a = ap.parse_args()
src = json.load(open(os.path.join(a.root, 'build', 'creature-vertex', '01', 'creature-vertex.json'), encoding='utf-8'))
models = []
for m in src['models']:
    frames = [{'meshes': [{'v': [x for prim in mesh for x in prim]} for mesh in f['unity']]} for f in m['frames']]
    models.append({'name': m['unityPrefix'], 'frames': frames})
for proj in ['unity/XenaUnity', 'unity/XenaLightingVerification']:
    d = os.path.join(a.root, proj, 'Assets', 'Imported', 'Resources', 'Creatures'); os.makedirs(d, exist_ok=True)
    json.dump({'models': models}, open(os.path.join(d, 'creature_vertex.json'), 'w'), separators=(',', ':'))
print([(m['name'], len(m['frames'])) for m in models])
