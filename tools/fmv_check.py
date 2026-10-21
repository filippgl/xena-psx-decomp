#!/usr/bin/env python3
"""Check the port's video playback (MDEC) against ffmpeg's decoder.

Extracts the opening logo videos from the disc, decodes them with ffmpeg (in WSL), runs
the port through the opening and matches every non-black ffmpeg frame to the closest
picture the port showed. A frame counts as shown when the match is >= 45 dB PSNR (the
port's 15-bit output against ffmpeg's 24-bit frames gives about 50 dB).

usage: fmv_check.py [--exe build/port/xena-console.exe] [--disc ../Xena.bin]

Pass: every UNILOGO frame is shown; at least 95% of EA_LOGO frames are (the port stops
it 5 frames before the end of the file, see docs/game_notes.md).
"""
import argparse
import glob
import os
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import iso_ls  # noqa: E402

VIDEOS = {"UNILOGO": 1.0, "EA_LOGO": 0.95}  # name: share of frames that must be shown


def extract(disc, name, out):
    f = open(disc, "rb")
    pvd = iso_ls.sector(f, 16)
    import struct
    lba, size = struct.unpack_from("<I", pvd, 158)[0], struct.unpack_from("<I", pvd, 166)[0]
    for n, e, s, isdir in iso_ls.entries(f, lba, size):
        if n.upper() == "MEDIA":
            lba, size = e, s
    for n, e, s, isdir in iso_ls.entries(f, lba, size):
        if n.upper().startswith(name + "."):
            f.seek(e * 2352)
            open(out, "wb").write(f.read((s + 2047) // 2048 * 2352))
            return
    sys.exit(f"{name} not on the disc")


def wsl_path(p):
    p = os.path.abspath(p).replace("\\", "/")
    return f"/mnt/{p[0].lower()}{p[2:]}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", default="build/port/xena-console.exe")
    ap.add_argument("--disc", default="../Xena Warrior Princess.bin")
    ap.add_argument("--out", default="build/fmv_check")
    args = ap.parse_args()
    shutil.rmtree(args.out, ignore_errors=True)
    os.makedirs(os.path.join(args.out, "port"))
    for v in VIDEOS:
        raw = os.path.join(args.out, v + ".raw")
        extract(args.disc, v, raw)
        os.makedirs(os.path.join(args.out, v))
        subprocess.run(["wsl", "ffmpeg", "-loglevel", "quiet", "-f", "psxstr", "-i", wsl_path(raw), "-an",
                        wsl_path(os.path.join(args.out, v)) + "/%04d.png"], check=True)
    subprocess.run([os.path.abspath(args.exe), "--headless", "--frames", "1100", "--opt", "renderer=software",
                    "--render-scale", "1", "--aspect", "4:3", "--shots", "2", os.path.join(args.out, "port"),
                    os.path.abspath(args.disc)], capture_output=True, check=True)
    port = []
    for p in sorted(glob.glob(os.path.join(args.out, "port", "*.bmp"))):
        im = Image.open(p).convert("RGB")
        if im.size == (320, 240):
            port.append(np.asarray(im).astype(np.float32))
    port = np.stack(port)
    ok = True
    for v, need in VIDEOS.items():
        shown = total = 0
        missing = []
        for i, p in enumerate(sorted(glob.glob(os.path.join(args.out, v, "*.png")))):
            f = np.asarray(Image.open(p).convert("RGB")).astype(np.float32)
            if f.std() < 3:
                continue  # black frames match anything
            mse = float(((port - f) ** 2).mean(axis=(1, 2, 3)).min())
            total += 1
            if 10 * np.log10(255 ** 2 / max(mse, 1e-9)) >= 45:
                shown += 1
            else:
                missing.append(i)
        good = shown >= need * total
        ok &= good
        print(f"{'PASS' if good else 'FAIL'}  {v}: {shown}/{total} frames shown" + (f", not shown: {missing}" if missing else ""))
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
