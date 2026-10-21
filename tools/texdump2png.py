#!/usr/bin/env python3
"""Convert dumped .rgba textures (see port/runtime/texdump.inc) to PNG, optionally a contact sheet.
usage: texdump2png.py DUMP_DIR [--sheet out.png]"""
import os
import sys
from PIL import Image

d = sys.argv[1]
entries = []
seen = set()
for line in open(os.path.join(d, "manifest.txt")):
    p = line.split()
    if len(p) < 8 or p[0] in seen:
        continue
    seen.add(p[0])
    name, w, h, used3d = p[0], int(p[5]), int(p[6]), int(p[7])
    raw = os.path.join(d, name + ".rgba")
    png = os.path.join(d, name + ".png")
    if not os.path.exists(raw):
        continue
    if not os.path.exists(png):
        Image.frombytes("RGBA", (w, h), open(raw, "rb").read()).save(png)
    entries.append((name, w, h, used3d))
print(len(entries), "textures")
if "--sheet" in sys.argv:
    out = sys.argv[sys.argv.index("--sheet") + 1]
    cell = 132
    cols = 16
    big = [e for e in entries if e[1] * e[2] >= 32 * 32][:16 * 12]
    sheet = Image.new("RGB", (cols * cell, ((len(big) + cols - 1) // cols) * cell), (40, 40, 40))
    for i, (name, w, h, _) in enumerate(big):
        im = Image.open(os.path.join(d, name + ".png")).convert("RGBA")
        im.thumbnail((128, 128))
        bg = Image.new("RGB", im.size, (255, 0, 255))
        bg.paste(im, mask=im.split()[3])
        sheet.paste(bg, ((i % cols) * cell + 2, (i // cols) * cell + 2))
    sheet.save(out)
