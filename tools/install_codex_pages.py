#!/usr/bin/env python3
"""Install Codex-repainted level texture pages (tools/codex-remaster/pages.sh) as the Remaster
page-N-4x.png (1024x1024) in both Unity projects.

Per page:
- reference = the existing page-N-4x.png, else a Real-ESRGAN x4plus upscale of page-N.png;
- alpha = the original page's alpha upscaled (bilinear, the material's 0.5 cutoff keeps the
  original silhouettes with smooth edges); Codex RGB is ignored where the original is cut out;
- palette lock: each 256x256 cell (64x64 original texels) is matched to the reference's
  per-channel mean and standard deviation, so the repaint keeps the original colours;
- seams: a 12-px band along every cell border blends into the reference, so tiling and
  neighbouring polygons still meet where the original texels met.
The pre-Codex page-N-4x.png files are kept in build/pages/previous-4x (only pages that had one
in the original level document, build/pages/Level01.xenamodel.orig).

usage: install_codex_pages.py [N ...]   (default: every tools/codex-remaster/out/page-N.png)
"""
import argparse
import glob
import json
import os
import re
import shutil
import subprocess

import numpy as np
from PIL import Image

PROJECTS = ["unity/XenaUnity", "unity/XenaLightingVerification"]
OUT = "tools/codex-remaster/out"
REALESRGAN = "../_tools/realesrgan/realesrgan-ncnn-vulkan.exe"
CELL, BAND = 256, 12


def pre_codex_upscaled(work):
    """Pages that had an HD version before the Codex repaint (backup of the level document)."""
    doc = json.load(open(os.path.join(work, "Level01.xenamodel.orig"), encoding="utf-8"))
    return {t["original"] for t in doc["textures"] if t["upscaled"]}


def reference(level, n, work):
    hd = os.path.join(level, "page-%d-4x.png" % n)
    backup = os.path.join(work, "previous-4x", "page-%d-4x.png" % n)
    original_hd = "page-%d.png" % n in pre_codex_upscaled(work)
    if os.path.exists(backup) and original_hd:
        return np.asarray(Image.open(backup).convert("RGB")).astype(np.float32)
    if os.path.exists(hd) and original_hd:
        os.makedirs(os.path.dirname(backup), exist_ok=True); shutil.copy(hd, backup)
        return np.asarray(Image.open(hd).convert("RGB")).astype(np.float32)
    src, dst = os.path.join(work, "esr-in"), os.path.join(work, "esr-out")
    for d in (src, dst):
        shutil.rmtree(d, ignore_errors=True); os.makedirs(d)
    Image.open(os.path.join(level, "page-%d.png" % n)).convert("RGB").save(os.path.join(src, "p.png"))
    subprocess.run([REALESRGAN, "-i", src, "-o", dst, "-n", "realesrgan-x4plus", "-f", "png"], check=True,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return np.asarray(Image.open(os.path.join(dst, "p.png")).convert("RGB").resize((1024, 1024), Image.LANCZOS)).astype(np.float32)


def finish(n, level, work):
    paint = np.asarray(Image.open(os.path.join(OUT, "page-%d.png" % n)).convert("RGB").resize((1024, 1024), Image.LANCZOS)).astype(np.float32)
    ref = reference(level, n, work)
    alpha = Image.open(os.path.join(level, "page-%d.png" % n)).convert("RGBA").getchannel("A").resize((1024, 1024), Image.BILINEAR)
    a = np.asarray(alpha).astype(np.float32) / 255
    # The magenta used to mark cut-out texels must never reach the material (edge filtering,
    # soft alpha): such pixels and all transparent ones take the reference colour.
    r, g, b = paint[..., 0], paint[..., 1], paint[..., 2]
    magenta = (r > 150) & (b > 150) & (g < 110) & (np.abs(r - b) < 90)
    paint[magenta | (a < 0.5)] = ref[magenta | (a < 0.5)]
    out = paint.copy()
    for cy in range(0, 1024, CELL):
        for cx in range(0, 1024, CELL):
            sl = (slice(cy, cy + CELL), slice(cx, cx + CELL))
            w = a[sl] > 0.5
            if w.sum() < 64:
                continue
            p, r = paint[sl][w], ref[sl][w]
            out[sl][w] = ((p - p.mean(0)) / np.maximum(p.std(0), 1) * r.std(0) + r.mean(0))
    # Seam band: weight 1 -> 0 from each cell border into the cell.
    idx = np.arange(1024)
    dist = np.minimum(idx % CELL, CELL - 1 - idx % CELL).astype(np.float32)
    band = np.clip(1 - dist / BAND, 0, 1)
    k = np.maximum(band[:, None], band[None, :])[..., None]
    out = out * (1 - k) + ref * k
    rgba = np.dstack([out.clip(0, 255), a * 255]).astype(np.uint8)
    return Image.fromarray(rgba, "RGBA")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("pages", nargs="*", type=int)
    ap.add_argument("--work", default="build/pages")
    args = ap.parse_args()
    pages = args.pages or sorted(int(re.findall(r"\d+", os.path.basename(f))[0]) for f in glob.glob(os.path.join(OUT, "page-*.png")))
    level0 = os.path.join(PROJECTS[1], "Assets", "Imported", "Level01")
    os.makedirs(args.work, exist_ok=True)
    for n in pages:
        image = finish(n, level0, args.work)
        image.save(os.path.join(args.work, "page-%d-4x.png" % n))
        for project in PROJECTS:
            level = os.path.join(project, "Assets", "Imported", "Level01")
            if not os.path.isdir(level):
                continue
            image.save(os.path.join(level, "page-%d-4x.png" % n))
            # Bind it as the page's Remaster ("upscaled") texture in the level document.
            model = os.path.join(level, "Level01.xenamodel")
            doc = json.load(open(model, encoding="utf-8"))
            for t in doc["textures"]:
                if t["original"] == "page-%d.png" % n:
                    t["upscaled"] = "page-%d-4x.png" % n
            json.dump(doc, open(model, "w", encoding="utf-8"), separators=(",", ":"))
        print("installed page", n)


if __name__ == "__main__":
    main()
