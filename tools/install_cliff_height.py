#!/usr/bin/env python3
"""Fetch the CC0 Poly Haven rock_face displacement map (https://polyhaven.com/a/rock_face, CC0 1.0) and install
it as Resources/Remaster/Rocks/CliffHeight.bytes (256x256 u8 heights, rows bottom-up, tileable) in both Unity
projects for the Remaster rock-wall relief (PrototypeRemasterGeometry). Local data, not committed.

usage: python tools/install_cliff_height.py [--root .] [--asset rock_face] [--size 256]"""
import argparse, io, json, os, urllib.request
import numpy as np
from PIL import Image

ap = argparse.ArgumentParser(); ap.add_argument('--root', default='.'); ap.add_argument('--asset', default='rock_face'); ap.add_argument('--size', type=int, default=256)
a = ap.parse_args(); H = {'User-Agent': 'xena-psx-decomp (remaster)'}
files = json.load(urllib.request.urlopen(urllib.request.Request('https://api.polyhaven.com/files/' + a.asset, headers=H)))
data = urllib.request.urlopen(urllib.request.Request(files['Displacement']['1k']['png']['url'], headers=H)).read()
h = np.asarray(Image.open(io.BytesIO(data))).astype(np.float64)
h = (h - h.min()) / max(1e-9, h.max() - h.min())
img = Image.fromarray((h * 65535).astype(np.uint16)).resize((a.size, a.size), Image.BILINEAR)
h = np.asarray(img).astype(np.float64) / 65535
out = (np.flipud(h) * 255).round().astype(np.uint8).tobytes()
for proj in ['unity/XenaUnity', 'unity/XenaLightingVerification']:
    d = os.path.join(a.root, proj, 'Assets', 'Imported', 'Resources', 'Remaster', 'Rocks'); os.makedirs(d, exist_ok=True)
    open(os.path.join(d, 'CliffHeight.bytes'), 'wb').write(out)
    json.dump({'source': 'https://polyhaven.com/a/' + a.asset, 'map': 'Displacement 1k', 'licence': 'CC0 1.0'}, open(os.path.join(d, 'CliffHeight.source.json'), 'w'))
print(a.asset, a.size, 'mean', round(float(h.mean()), 3))
