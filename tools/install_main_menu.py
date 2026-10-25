#!/usr/bin/env python3
"""Install the main menu (docs/xena-main-menu.md) into the Unity projects' Resources/Menu.

Original: tools/export_main_menu.mjs output (background, hair/lightning tiles, highlights,
prompt, main_menu.json), composited bit-exactly at runtime by PrototypeMainMenu.
Remaster (Resources/Menu/HD), all optional:
- background.png  Codex image_gen remaster of main.tim without the baked labels
                  (tools/codex-remaster/out/menu-main.png, 1536x1024; the 4:3 picture spans
                  x 85..1450 at full height);
- hair-mask.png   8-bit mask of Xena's dark hair in that image (wind animation);
- lightning/*.png Real-ESRGAN x4plus upscales of the LTNG tiles;
(Remaster labels and prompt are drawn with Tenor Sans at runtime, PrototypeMenuText.)

usage: install_main_menu.py [--realesrgan ../_tools/realesrgan/realesrgan-ncnn-vulkan.exe]
"""
import argparse
import json
import os
import shutil
import subprocess

import numpy as np
from PIL import Image, ImageFilter

PROJECTS = ["unity/XenaUnity", "unity/XenaLightingVerification"]


def upscale(exe, src, dst):
    subprocess.run([exe, "-i", src, "-o", dst, "-n", "realesrgan-x4plus", "-f", "png"], check=True,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def hair_mask(path):
    """Soft mask of Xena's hair on the Codex image: dark, not blue (the night sky is violet-blue),
    not skin, connected to the crown, fading out above the shoulder armour."""
    im = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    h, w, _ = im.shape
    r, g, b = im[..., 0], im[..., 1], im[..., 2]
    lum = 0.299 * r + 0.587 * g + 0.114 * b
    hair = (lum < 70) & (b - r < 18) & (r < 95)
    x0, x1, y0, y1 = 1040, 1200, 90, 262
    box = np.zeros_like(hair); box[y0:y1, x0:x1] = True
    hair &= box
    # Keep the component connected to the crown (flood fill from a seed on the hair).
    from collections import deque
    seed = min(((y, x) for y in range(110, 140) for x in range(1100, 1150) if hair[y, x]), default=None)
    keep = np.zeros_like(hair)
    if seed is not None:
        q = deque([seed]); keep[seed] = True
        while q:
            y, x = q.popleft()
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                ny, nx = y + dy, x + dx
                if hair[ny, nx] and not keep[ny, nx]:
                    keep[ny, nx] = True; q.append((ny, nx))
    mask = keep.astype(np.float32)
    yy = np.arange(h, dtype=np.float32)[:, None]
    mask *= np.clip((250 - yy) / 24, 0, 1)  # fade out where the hair lies on the shoulder
    m = Image.fromarray((mask * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.5))
    return m


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--realesrgan", default="../_tools/realesrgan/realesrgan-ncnn-vulkan.exe")
    ap.add_argument("--work", default="build/menu")
    args = ap.parse_args()
    export = os.path.join(args.work, "export")
    shutil.rmtree(export, ignore_errors=True)
    subprocess.run(["node", "tools/export_main_menu.mjs", "--root", ".", "--out", export], check=True)
    hd = os.path.join(args.work, "hd")
    shutil.rmtree(hd, ignore_errors=True)
    os.makedirs(os.path.join(hd, "lightning"))
    codex = "tools/codex-remaster/out"
    if os.path.exists(os.path.join(codex, "menu-main.png")):
        shutil.copy(os.path.join(codex, "menu-main.png"), os.path.join(hd, "background.png"))
        hair_mask(os.path.join(hd, "background.png")).save(os.path.join(hd, "hair-mask.png"))
    if os.path.exists(args.realesrgan):
        upscale(args.realesrgan, os.path.join(export, "lightning"), os.path.join(hd, "lightning"))
        # The LTNG tiles are opaque rectangles with main.tim's sky baked in (no texel 0): on the
        # HD sky keep only the flash, alpha = brightness gain over the original background.
        bg = Image.open(os.path.join(export, "background.png")).convert("RGB")
        meta = json.load(open(os.path.join(export, "main_menu.json"), encoding="utf-8"))["lightning"]
        for bolt in meta["bolts"]:
            tile = meta["tiles"][bolt["tile"]]; name = os.path.basename(tile["png"])
            x, y = bolt["topLeft"]; w, h = tile["size"]
            src = np.asarray(Image.open(os.path.join(export, "lightning", name)).convert("RGB")).astype(np.float32)
            under = np.asarray(bg.crop((x, y, x + w, y + h))).astype(np.float32)
            gain = np.clip((src - under).max(axis=2) / 96, 0, 1)
            mask = Image.fromarray((gain * 255).astype(np.uint8)).resize((w * 4, h * 4), Image.BILINEAR)
            up = Image.open(os.path.join(hd, "lightning", name)).convert("RGB")
            up.putalpha(mask); up.save(os.path.join(hd, "lightning", name))
    for project in PROJECTS:
        out = os.path.join(project, "Assets", "Imported", "Resources", "Menu")
        shutil.rmtree(out, ignore_errors=True)
        shutil.copytree(export, out)
        shutil.copytree(hd, os.path.join(out, "HD"))
        print("installed", out, len(os.listdir(os.path.join(out, "hair"))), "hair tiles,",
              len(os.listdir(os.path.join(out, "HD"))), "HD entries")


if __name__ == "__main__":
    main()
