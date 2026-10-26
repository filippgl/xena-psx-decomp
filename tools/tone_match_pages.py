#!/usr/bin/env python3
"""Tone-match Remaster level pages (page-N-4x.png) to their original 256x256 page.

Pages repainted without an earlier HD version were palette-locked to a Real-ESRGAN upscale of the
original, which came out darker on cut-out-heavy pages (2026-10-26: opaque mean 0.64..0.83 of the
original on pages 5, 11, 14, 20, 23; the gate page 20: 28 vs 41). Per channel, the local mean of the HD
page (masked Gaussian, sigma 24 HD px = 6 original texels) is scaled to the local mean of the original
page upscaled, so brightness and hue follow the original while the HD detail stays. Alpha is untouched.
The previous files are kept in build/pages/pre-tone/.

usage: python tools/tone_match_pages.py N [N ...]"""
import argparse, os, shutil
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter

PROJECTS = ["unity/XenaUnity", "unity/XenaLightingVerification"]

def blur(x, sigma):
    return gaussian_filter(x.astype(np.float32), sigma, mode="nearest")

def match(original, hd, sigma=24):
    h, w = hd.shape[:2]
    o = np.asarray(Image.fromarray(original).resize((w, h), Image.BILINEAR)).astype(np.float32)
    mo = (o[..., 3] > 128).astype(np.float32); mh = (hd[..., 3] > 128).astype(np.float32)
    out = hd.astype(np.float32).copy()
    do, dh = blur(mo, sigma) + 1e-3, blur(mh, sigma) + 1e-3
    for c in range(3):
        lo = blur(o[..., c] * mo, sigma) / do; lh = blur(hd[..., c].astype(np.float32) * mh, sigma) / dh
        gain = np.clip((lo + 1) / (lh + 1), 0.6, 2.5)
        out[..., c] = np.where(mh > 0, hd[..., c] * gain, hd[..., c])
    return out.clip(0, 255).astype(np.uint8)

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("pages", nargs="+", type=int); a = ap.parse_args()
    for n in a.pages:
        for project in PROJECTS:
            level = os.path.join(project, "Assets", "Imported", "Level01")
            hd_path = os.path.join(level, "page-%d-4x.png" % n)
            if not os.path.exists(hd_path): continue
            backup = os.path.join("build", "pages", "pre-tone", os.path.basename(project), "page-%d-4x.png" % n)
            os.makedirs(os.path.dirname(backup), exist_ok=True)
            if not os.path.exists(backup): shutil.copy(hd_path, backup)
            original = np.asarray(Image.open(os.path.join(level, "page-%d.png" % n)).convert("RGBA"))
            hd = np.asarray(Image.open(backup).convert("RGBA"))
            out = match(original, hd)
            Image.fromarray(out, "RGBA").save(hd_path)
            m = out[..., 3] > 128; mo = original[..., 3] > 128
            print(project, n, "opaque mean", round(float(hd[m][:, :3].mean()), 1), "->", round(float(out[m][:, :3].mean()), 1),
                  "original", round(float(original[mo][:, :3].mean()), 1))

if __name__ == "__main__":
    main()
