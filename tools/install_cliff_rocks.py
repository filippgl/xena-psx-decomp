#!/usr/bin/env python3
"""Install Hunyuan3D cliff-rock shapes (build/hy3d/cliffrockN/shape.obj, from Codex concepts
tools/codex-remaster/out/cliffrockN-concept.png) as Resources/Remaster/Rocks/cliffrocks.json in both Unity
projects for PrototypeRemasterRockWalls. Shapes only (no paint): the rocks take the texture of the wall they
clad. Each shape is centred, its back (the -z side, the flattest) is found by PCA so the thinnest axis is z.

usage: python tools/install_cliff_rocks.py [--root .] [N ...]"""
import argparse, json, os
import numpy as np

ap = argparse.ArgumentParser(); ap.add_argument('--root', default='.'); ap.add_argument('ids', nargs='*', type=int); a = ap.parse_args()
ids = a.ids or [1, 2, 3]
shapes = []
for k in ids:
    v, f = [], []
    for line in open(os.path.join(a.root, 'build', 'hy3d', 'cliffrock%d' % k, 'shape.obj'), encoding='utf-8', errors='ignore'):
        p = line.split()
        if not p: continue
        if p[0] == 'v': v.append([float(x) for x in p[1:4]])
        elif p[0] == 'f':
            idx = [int(q.split('/')[0]) - 1 for q in p[1:]]
            for i in range(1, len(idx) - 1): f.append([idx[0], idx[i], idx[i + 1]])
    v = np.array(v); v -= (v.min(0) + v.max(0)) / 2
    # Thinnest principal axis -> z (depth into the wall), then y stays as up when possible.
    ext = v.max(0) - v.min(0); order = list(np.argsort(-ext))  # largest first
    thin = int(np.argmin(ext)); up = 1 if thin != 1 else int(np.argsort(-ext)[1])
    side = [i for i in range(3) if i not in (thin, up)][0]
    v = v[:, [side, up, thin]]
    # Flat back toward -z: the side with the smaller spread of depths faces the wall.
    z = v[:, 2]
    if np.std(z[z > 0]) < np.std(z[z < 0]): v[:, 2] = -v[:, 2]; v[:, 0] = -v[:, 0]  # keep handedness
    v /= (v.max(0) - v.min(0)).max()  # unit size (largest extent 1)
    shapes.append({'name': 'cliffrock%d' % k, 'vertices': [round(x, 5) for x in v.flatten().tolist()], 'triangles': [i for t in f for i in t],
                   'extents': [round(x, 4) for x in (v.max(0) - v.min(0)).tolist()]})
for proj in ['unity/XenaUnity', 'unity/XenaLightingVerification']:
    d = os.path.join(a.root, proj, 'Assets', 'Imported', 'Resources', 'Remaster', 'Rocks'); os.makedirs(d, exist_ok=True)
    json.dump({'shapes': shapes}, open(os.path.join(d, 'cliffrocks.json'), 'w'), separators=(',', ':'))
print([(s['name'], len(s['triangles']) // 3, s['extents']) for s in shapes])
