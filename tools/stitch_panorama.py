#!/usr/bin/env python3
"""Stitch the Codex panorama segments into one seamless 360-degree strip for Remaster.

Segments (tools/codex-remaster/pano_next.sh / pano_close.sh, 1536x1024 each):
pano-1 fresh; pano-2/3 continue the previous one (their left 40% repaints its right 40%);
pano-4 closes the loop (left 30% = right of pano-3, right 30% = left of pano-1).
Each overlap is cross-faded over its middle band with a smoothstep. The strip wraps
horizontally (x = W continues at x = 0). Optional Real-ESRGAN (4x, then Lanczos to 2x) for a sharper result.

usage: stitch_panorama.py [--out build/pano/panorama.png] [--realesrgan EXE] [--install]
"""
import argparse
import os
import shutil
import subprocess

import numpy as np
from PIL import Image

SRC = "tools/codex-remaster/out"


def load(n):
    return np.asarray(Image.open(os.path.join(SRC, "pano-%d.png" % n)).convert("RGB")).astype(np.float32)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="build/pano/panorama.png")
    ap.add_argument("--realesrgan", default="../_tools/realesrgan/realesrgan-ncnn-vulkan.exe")
    ap.add_argument("--install", action="store_true", help="copy into both Unity projects' Resources/Remaster")
    args = ap.parse_args()
    segs = [load(n) for n in (1, 2, 3, 4)]
    h, w, _ = segs[0].shape
    k40, k30 = int(w * 0.4), int(w * 0.3)
    starts = [0, w - k40, 2 * (w - k40), 3 * (w - k40)]
    width = starts[3] + w - k30
    acc = np.zeros((h, width, 3), np.float32); weight = np.zeros((h, width, 1), np.float32)
    for i, seg in enumerate(segs):
        # Weight ramps: 0 -> 1 over the middle half of the left overlap, 1 -> 0 over the right one.
        ramp = np.ones(w, np.float32)
        left = k40 if i > 0 else k30          # pano-1's left side overlaps pano-4's right 30%
        right = k40 if i < 3 else k30
        def band(n):
            t = np.clip((np.arange(n) - n * 0.25) / (n * 0.5), 0, 1)
            return t * t * (3 - 2 * t)
        ramp[:left] = np.minimum(ramp[:left], band(left))
        ramp[w - right:] = np.minimum(ramp[w - right:], band(right)[::-1])
        cols = (starts[i] + np.arange(w)) % width
        np.add.at(acc, (slice(None), cols), seg * ramp[None, :, None])
        np.add.at(weight, (slice(None), cols), np.broadcast_to(ramp[None, :, None], (h, w, 1)))
    strip = (acc / np.maximum(weight, 1e-4)).clip(0, 255).astype(np.uint8)
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    Image.fromarray(strip).save(args.out)
    print("stitched", args.out, strip.shape[1], "x", strip.shape[0])
    final = args.out
    if os.path.exists(args.realesrgan):
        tmp = os.path.join(os.path.dirname(args.out), "up")
        shutil.rmtree(tmp, ignore_errors=True); os.makedirs(tmp)
        # Wrap-pad 64 px on both sides so the upscaled edges still meet at x = 0 / W.
        pad = 64
        Image.fromarray(np.concatenate([strip[:, -pad:], strip, strip[:, :pad]], axis=1)).save(os.path.join(tmp, "panorama.png"))
        shutil.rmtree(tmp + "-2x", ignore_errors=True); os.makedirs(tmp + "-2x")
        # realesrgan-x4plus only supports 4x (-s 2 assembles a broken tile mosaic): 4x, then
        # Lanczos down to 2x.
        subprocess.run([args.realesrgan, "-i", tmp, "-o", tmp + "-2x", "-n", "realesrgan-x4plus", "-f", "png"],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        final = os.path.join(tmp + "-2x", "panorama.png")
        up4 = Image.open(final); up4.resize((up4.width // 2, up4.height // 2), Image.LANCZOS).save(final)
        # Crop the pads; the last 2*pad columns fade into the upscaled left pad, which is the
        # same strip end upscaled next to the strip start, so x = W meets x = 0 continuously.
        up = np.asarray(Image.open(final).convert("RGB")).astype(np.float32); p2 = 2 * pad
        body = up[:, p2:-p2].copy(); t = np.linspace(0, 1, p2)[None, :, None]
        body[:, -p2:] = body[:, -p2:] * (1 - t) + up[:, :p2] * t
        Image.fromarray(body.clip(0, 255).astype(np.uint8)).save(final)
        print("upscaled", final, Image.open(final).size)
    if args.install:
        for project in ("unity/XenaUnity", "unity/XenaLightingVerification"):
            out = os.path.join(project, "Assets", "Imported", "Resources", "Remaster")
            os.makedirs(out, exist_ok=True)
            shutil.copy(final, os.path.join(out, "Panorama.png"))
            print("installed", os.path.join(out, "Panorama.png"))


if __name__ == "__main__":
    main()
