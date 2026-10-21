#!/usr/bin/env python3
"""Check the port's video playback (MDEC) against ffmpeg's decoder.

Extracts the opening logo videos from the disc, decodes every frame with ffmpeg (in WSL),
runs the port through the opening and matches each non-black frame to the closest picture
the port showed. A frame counts as shown when the match is >= 45 dB PSNR (the port's 15-bit
output against ffmpeg's 24-bit frames gives about 50 dB).

Which frames the game shows, from its code (func_800AF29C and helpers): the movie table in
the EXE has {name, flag, count}; the player stops when it fetches frame number >= count
(STR frames are numbered from 1), decoding one frame ahead, so the last frame it decodes
(count-1) is put on screen and replaced by the clean-up before the next vblank. Visible:
frames 1 .. count-2.

usage: fmv_check.py [--exe build/port/xena-console.exe] [--disc ../Xena.bin] [--game-exe xena.exe]

Pass: every non-black visible frame is shown.
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

VIDEOS = ("UNILOGO", "EA_LOGO")
BASE = 0x80010000 - 0x800


def table_count(game_exe, name):
    """frames the game plays: movie table entry {char* name, u16 flag, u16 count}"""
    import struct
    d = open(game_exe, "rb").read()
    at = d.find(name.encode() + b"\x00")
    for off in range(0x800, len(d) - 8, 4):
        if struct.unpack_from("<I", d, off)[0] == at + BASE:
            return struct.unpack_from("<H", d, off + 6)[0]
    sys.exit(f"{name} not in the movie table")


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
    ap.add_argument("--game-exe", default="xena.exe")
    args = ap.parse_args()
    shutil.rmtree(args.out, ignore_errors=True)
    os.makedirs(os.path.join(args.out, "port"))
    for v in VIDEOS:
        raw = os.path.join(args.out, v + ".raw")
        extract(args.disc, v, raw)
        os.makedirs(os.path.join(args.out, v))
        subprocess.run(["wsl", "ffmpeg", "-loglevel", "quiet", "-f", "psxstr", "-i", wsl_path(raw), "-an",
                        "-fps_mode", "passthrough", wsl_path(os.path.join(args.out, v)) + "/%04d.png"], check=True)
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
    for v in VIDEOS:
        count = table_count(args.game_exe, v)
        frames = sorted(glob.glob(os.path.join(args.out, v, "*.png")))
        shown = total = 0
        missing = []
        for i, p in enumerate(frames[:count - 2]):
            f = np.asarray(Image.open(p).convert("RGB")).astype(np.float32)
            if f.std() < 3:
                continue  # black frames match anything
            mse = float(((port - f) ** 2).mean(axis=(1, 2, 3)).min())
            total += 1
            if 10 * np.log10(255 ** 2 / max(mse, 1e-9)) >= 45:
                shown += 1
            else:
                missing.append(i)
        good = shown == total
        ok &= good
        print(f"{'PASS' if good else 'FAIL'}  {v}: {shown}/{total} frames shown (visible: 1..{count - 2} of "
              f"{len(frames)})" + (f", not shown: {missing}" if missing else ""))
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
