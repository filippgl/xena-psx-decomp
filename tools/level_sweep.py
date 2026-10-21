#!/usr/bin/env python3
"""Load every level (by setting the level number after "New game") and play it with
random input, checking for crashes and dumping the textures it uses.

usage: level_sweep.py [--levels 1-30] [--frames 7000] [--dump DIR] [--jobs N]

Fails a level that crashes, does not run to the end, ends on blank frames, or where
Xena never gets control. Numbers without level data in the game's file index are
listed and skipped.
"""
import argparse
import concurrent.futures as cf
import os
import subprocess
import sys
import time

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from smoke_test import fuzz  # noqa: E402
import uda_find  # noqa: E402

LEVEL_VAR = "800EE200"   # level number used by the loader (set right after "New game")
MENU = 7400


def run(level, args):
    out = os.path.join(args.out, f"level_{level:02d}")
    os.makedirs(out, exist_ok=True)
    end = MENU + 1100 + args.frames
    script = (f"{MENU}:CROSS,{MENU + 300}:CROSS,{MENU + 600}:CROSS,{MENU + 900}:CROSS,"
              + fuzz(MENU + 1100, end, 100 + level))
    cmd = [args.exe, "--headless", "--frames", str(end), "--shots", "1500", out,
           "--poke", f"{MENU + 20}:{LEVEL_VAR}:{level}", "--script", script]
    if args.dump:
        d = os.path.join(args.dump, f"level_{level:02d}")
        os.makedirs(d, exist_ok=True)
        cmd += ["--dump-textures", d]
    cmd.append(args.disc)
    t0 = time.time()
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=1800)
        log = p.stdout + p.stderr
    except subprocess.TimeoutExpired:
        return level, False, "timeout", 0
    open(os.path.join(out, "log.txt"), "w", encoding="utf-8").write(log)
    problems = []
    if "FATAL" in log:
        problems.append(log[log.index("FATAL"):].splitlines()[0])
    if f"reached frame {end}" not in log:
        problems.append("did not finish")
    shots = sorted(f for f in os.listdir(out) if f.endswith(".bmp"))
    alive = 0
    for f in shots[-3:]:
        im = np.asarray(Image.open(os.path.join(out, f)).convert("L"), dtype=np.float32)
        alive += im.std() > 4
        Image.open(os.path.join(out, f)).convert("RGB").save(os.path.join(out, f[:-4] + ".png"))
    if shots and not alive:
        problems.append("blank frames")
    lost = {l for l in log.splitlines() if l.startswith("file not found")}
    if "player object" not in log:
        problems.append("Xena never got control")
    note = f"{len(lost)} optional files missing" if lost else ""
    return level, not problems, "; ".join(problems) or ("ok " + note), time.time() - t0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", default="build/port/xena-console.exe")
    ap.add_argument("--disc", default="../Xena Warrior Princess.bin")
    ap.add_argument("--exe-data", default="xena.exe", help="the game EXE (MEDIA/XA2.XA) for the file index")
    ap.add_argument("--out", default="build/levels")
    ap.add_argument("--levels", default="1-30")
    ap.add_argument("--frames", type=int, default=7000)
    ap.add_argument("--dump")
    ap.add_argument("--jobs", type=int, default=8)
    args = ap.parse_args()
    args.exe = os.path.abspath(args.exe)
    args.disc = os.path.abspath(args.disc)
    a, b = (int(x) for x in args.levels.split("-"))
    # level numbers whose data is in XENA.UDA's index (in the EXE); the others make the
    # game stop with an error code, as it would on a PlayStation
    table, weights = uda_find.load(args.exe_data)
    levels = [n for n in range(a, b + 1) if uda_find.name_hash(f"DATA\LEVELS\{n:02d}\LEVEL.TMD", weights) in table]
    skipped = sorted(set(range(a, b + 1)) - set(levels))
    if skipped:
        print("not levels on this disc:", " ".join(map(str, skipped)))
    fails = 0
    with cf.ThreadPoolExecutor(args.jobs) as ex:
        for level, ok, msg, dt in ex.map(lambda l: run(l, args), levels):
            print(f"{'PASS' if ok else 'FAIL'}  level {level:02d}  {dt:6.1f}s  {msg}", flush=True)
            fails += not ok
    print(f"{len(levels) - fails}/{len(levels)} levels passed")
    sys.exit(1 if fails else 0)


if __name__ == "__main__":
    main()
