#!/usr/bin/env python3
"""Pad the Codex grass atlas (2x2 cells, tufts standing on each cell's bottom edge) for Unity.
Each cell's tuft is scaled to CONTENT (72 %) of the cell, bottom-centred, with transparent margins
on the top and sides, so neither bilinear filtering nor the small mip levels can pull a
neighbouring cell's tuft base into the top of a grass quad (PrototypeRemasterGrass samples only
the content rectangle). Edge texels of each tuft are also cleared (3 % side / 12 % top bands).
usage: clean_grass_atlas.py tools/codex-remaster/out/grass.png out.png
"""
import sys
import numpy as np
from PIL import Image

CONTENT = 0.72
src = Image.open(sys.argv[1]).convert("RGBA")
w, h = src.size; cw, ch = w // 2, h // 2
out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
for cy in range(2):
    for cx in range(2):
        cell = np.asarray(src.crop((cx * cw, cy * ch, cx * cw + cw, cy * ch + ch))).copy()
        cell[:int(ch * 0.12), :, 3] = 0; cell[:, :int(cw * 0.03), 3] = 0; cell[:, cw - int(cw * 0.03):, 3] = 0
        tw, th = int(cw * CONTENT), int(ch * CONTENT)
        small = Image.fromarray(cell).resize((tw, th), Image.LANCZOS)
        out.paste(small, (cx * cw + (cw - tw) // 2, cy * ch + ch - th))  # bottom-centred (image rows top-down)
out.save(sys.argv[2]); print("padded", sys.argv[2])
