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
- prompt.png      Real-ESRGAN upscale of the prompt text, alpha = brightness (additive look);
- label-*.png     Codex HD labels (tools/codex-remaster/out/label-*.png) when present.

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
LABELS = ["ng", "lg", "ta", "op"]


def upscale(exe, src, dst):
    subprocess.run([exe, "-i", src, "-o", dst, "-n", "realesrgan-x4plus", "-f", "png"], check=True,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def hair_mask(path):
    """Soft mask of the dark hair around Xena's head (box chosen on the Codex image)."""
    im = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    h, w, _ = im.shape
    lum = im @ np.array([0.299, 0.587, 0.114], np.float32)
    mask = np.zeros((h, w), np.float32)
    x0, x1, y0, y1 = 1050, 1195, 95, 262
    box = lum[y0:y1, x0:x1]
    mask[y0:y1, x0:x1] = np.clip((78 - box) / 30, 0, 1)
    # Keep the face/neck (lit skin) still: no hair left of the jaw line below the crown.
    yy, xx = np.mgrid[0:h, 0:w]
    mask[(xx < 1120) & (yy > 140)] = 0
    m = Image.fromarray((mask * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(3))
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
    for name in LABELS:
        for suffix in ("", "-sel"):
            src = os.path.join(codex, "label-%s%s.png" % (name, suffix))
            if os.path.exists(src):
                shutil.copy(src, os.path.join(hd, "label-%s%s.png" % (name, suffix)))
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
        tmp = os.path.join(args.work, "prompt-up")
        shutil.rmtree(tmp, ignore_errors=True); os.makedirs(tmp)
        # Prompt: upscale on black, then alpha = brightness so normal blending looks additive.
        prompt = Image.open(os.path.join(export, "font", "prompt.png")).convert("RGBA")
        # Real-ESRGAN fails on very small images: pad by 32 px, crop the 4x result back.
        pad = 32
        black = Image.new("RGB", (prompt.width + 2 * pad, prompt.height + 2 * pad)); black.paste(prompt, (pad, pad), mask=prompt.split()[3])
        black.save(os.path.join(tmp, "prompt.png"))
        os.makedirs(tmp + "-hd", exist_ok=True)
        upscale(args.realesrgan, tmp, tmp + "-hd")
        up = Image.open(os.path.join(tmp + "-hd", "prompt.png")).convert("RGB")
        up = np.asarray(up.crop((4 * pad, 4 * pad, 4 * (pad + prompt.width), 4 * (pad + prompt.height)))).astype(np.float32)
        alpha = up.max(axis=2)
        rgb = np.where(alpha[..., None] > 0, up * 255 / np.maximum(alpha[..., None], 1), 0)
        Image.fromarray(np.dstack([rgb, alpha]).clip(0, 255).astype(np.uint8), "RGBA").save(os.path.join(hd, "prompt.png"))
    for project in PROJECTS:
        out = os.path.join(project, "Assets", "Imported", "Resources", "Menu")
        shutil.rmtree(out, ignore_errors=True)
        shutil.copytree(export, out)
        shutil.copytree(hd, os.path.join(out, "HD"))
        print("installed", out, len(os.listdir(os.path.join(out, "hair"))), "hair tiles,",
              len(os.listdir(os.path.join(out, "HD"))), "HD entries")


if __name__ == "__main__":
    main()
