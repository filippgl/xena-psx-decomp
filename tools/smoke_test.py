#!/usr/bin/env python3
"""Automated smoke tests for the native port.

usage: smoke_test.py [--exe build/port/xena-console.exe] [--disc ../Xena.bin] [--only NAME] [--jobs N]

Each scenario boots the game headless with scripted input and checks that it ran to the
requested frame without a fatal error, that the last frame is not blank, and (where the
scenario has sound) that the audio is not silent. Random input is seeded, so failures
are reproducible.
"""
import argparse
import concurrent.futures as cf
import math
import os
import random
import shutil
import struct
import subprocess
import sys
import time
import wave

import numpy as np
from PIL import Image

MENU = 7400  # frame at which the main menu accepts input (intro skipped by waiting)
START_LEVEL1 = f"{MENU}:CROSS,{MENU + 300}:CROSS,{MENU + 600}:CROSS,{MENU + 900}:CROSS"
START_ARENA = f"{MENU}:DOWN,{MENU + 60}:DOWN,{MENU + 120}:CROSS,{MENU + 400}:CROSS,{MENU + 700}:CROSS,{MENU + 1000}:CROSS"

START_ARENA_SHORT = f"{MENU}:DOWN,{MENU + 60}:DOWN,{MENU + 120}:CROSS,{MENU + 400}:CROSS,{MENU + 700}:CROSS,{MENU + 1000}:CROSS"
NATIVE_1X = ["--render-scale", "1", "--aspect", "4:3", "--opt", "perspective=0", "--opt", "texture_filter=0",
             "--opt", "subpixel=0"]

BUTTONS = ["UP", "DOWN", "LEFT", "RIGHT", "CROSS", "SQUARE", "CIRCLE", "TRIANGLE", "L1", "R1", "L2", "R2"]


def fuzz(start, end, seed, allow_start=False):
    rng = random.Random(seed)
    steps = []
    f = start
    pool = BUTTONS + (["START"] if allow_start else [])
    while f < end:
        n = rng.choice([1, 1, 2])
        combo = "+".join(rng.sample(pool, n))
        length = rng.choice([4, 8, 15, 30, 60, 120])
        steps.append(f"{f}:{combo}:{length}")
        f += length + rng.randint(5, 90)
    return ",".join(steps)


SCENARIOS = [
    # name, frames, script, extra args, expect audio
    ("intro_fmv", 1500, "", [], True),
    ("main_menu", 7350, "", [], True),
    ("level1_walk", 9700, START_LEVEL1 + f",{MENU + 1100}:UP:600", [], True),
    ("level1_fuzz", 30000, START_LEVEL1 + "," + fuzz(MENU + 1100, 30000, 1), [], True),
    ("level1_fuzz2", 30000, START_LEVEL1 + "," + fuzz(MENU + 1100, 30000, 2), [], True),
    ("arena_fuzz", 25000, START_ARENA + "," + fuzz(MENU + 1300, 25000, 3), [], True),
    ("pause_menu", 12000, START_LEVEL1 + "," + fuzz(MENU + 1100, 12000, 4, allow_start=True), [], False),
    ("options_menu", 8200, f"{MENU}:DOWN,{MENU + 60}:DOWN,{MENU + 120}:DOWN,{MENU + 200}:CROSS,{MENU + 400}:DOWN,{MENU + 500}:RIGHT", [], False),
    ("load_menu", 7900, f"{MENU}:DOWN,{MENU + 100}:CROSS", [], False),
    ("software_hd", 9700, START_LEVEL1 + f",{MENU + 1100}:UP:300", ["--opt", "renderer=software", "--render-scale", "2", "--aspect", "16:9"], False),
    ("native_4x3", 9700, START_LEVEL1 + f",{MENU + 1100}:UP:300", ["--opt", "renderer=software", "--render-scale", "1", "--aspect", "4:3",
                                                                    "--opt", "perspective=0", "--opt", "texture_filter=0"], False),
    ("memcard_selftest", 7400, "", ["--selftest", "memcard", "7350"], False),
    ("modern_controls", 8760, START_LEVEL1 + f",{MENU + 1240}:RIGHT:120", ["--ramdump", "{out}", "8760"], False),
    # save through the game's save screen (game mode 4) on level 4, then load it from the
    # main menu in a second run: the load screen must offer it and stage 5 must start
    ("save_load", 9600, "7400:DOWN,7500:CROSS,7800:CROSS,8100:CROSS,8400:CROSS,8700:CROSS",
     ["--memcard", "{out}/card", "--watch", "800EE200"], False),
    # the HD renderer at 1x without enhancements against the native rasterizer, in the
    # arena: at most 1% of pixels may differ beyond colour precision and 1-pixel shifts
    ("hd_matches_native", 9300, START_ARENA_SHORT, NATIVE_1X + ["--opt", "renderer=software", "--opt", "force_hd=1"], False),
    ("gl_matches_native", 9300, START_ARENA_SHORT, NATIVE_1X + ["--opt", "renderer=opengl", "--opt", "force_hd=1"], False),
    ("gl_8x_219_ssaa", 9700, START_LEVEL1 + f",{MENU + 1100}:UP:300", ["--render-scale", "8", "--aspect", "21:9", "--opt", "ssaa=2"], False),
]


def frame_ok(path):
    im = np.asarray(Image.open(path).convert("L"), dtype=np.float32)
    return float(im.std()) > 4.0, float(im.mean())


def audio_ok(path):
    with wave.open(path) as w:
        data = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float32)
    if not len(data):
        return False, 0.0
    tail = data[-44100 * 2 * 20:]  # last 20 seconds
    return float(np.sqrt((tail ** 2).mean())) > 30.0, float(np.sqrt((tail ** 2).mean()))


def check_modern_controls(out, log):
    """Right with camera-relative controls: Xena turns 90 degrees right of the camera and
    runs that way (the camera starts at yaw 402 on level 1; she moves along rotY + 2048)."""
    ram = open(os.path.join(out, "ram_008760.bin"), "rb").read()
    obj = int(log.split("player object ")[-1].split()[0], 16) & 0x1FFFFF
    tr = struct.unpack_from("<I", ram, (struct.unpack_from("<I", ram, obj + 0xA8)[0] & 0x1FFFFF) + 0x18)[0] & 0x1FFFFF
    x, _, z = struct.unpack_from("<iii", ram, tr)
    heading = (struct.unpack_from("<H", ram, tr + 0xE)[0] + 2048) & 4095
    err = (heading - (402 + 1024) + 2048) % 4096 - 2048
    moved = math.hypot(x + 7821, z + 17988)
    if abs(err) > 64 or moved < 1000:
        return f"heading {heading} (off by {err}), moved {moved:.0f}"
    return None


def prepare_save_load(args, out):
    """First run of save_load: start level 4 and save it."""
    shutil.rmtree(os.path.join(out, "card"), ignore_errors=True)
    subprocess.run([args.exe, "--headless", "--frames", "9300", "--memcard", os.path.join(out, "card"),
                    "--poke", f"{MENU + 20}:800EE200:4", "--poke", "9000:800F2648:4",
                    "--script", START_LEVEL1, args.disc], capture_output=True, timeout=600)
    if not os.path.exists(os.path.join(out, "card", "bu00", "BASLUS-00977")):
        return "the game did not write a save"
    return None


def check_save_load(out, log):
    # the level variable holds the level being loaded, then goes back to -1
    levels = {int(l.split("=")[-1], 16) & 0xFFFF for l in log.splitlines() if l.startswith("watch")}
    if 5 not in levels:
        return f"stage 5 was not loaded (level variable: {sorted(levels)})"
    return None


def prepare_hd_matches_native(args, out):
    """Reference picture from the native rasterizer."""
    ref = os.path.join(out, "native")
    os.makedirs(ref, exist_ok=True)
    subprocess.run([args.exe, "--headless", "--frames", "9300", "--shots", "9300", ref, "--opt", "renderer=software"]
                   + NATIVE_1X + ["--script", START_ARENA_SHORT, args.disc], capture_output=True, timeout=600)
    return None if os.path.exists(os.path.join(ref, "frame_009300.bmp")) else "no native picture"


def structural_diff(a, b, tol=24):
    """Pixels of b with no pixel of a within one pixel whose colour is within tol (so
    true-colour precision and one-texel shifts don't count)."""
    h, w, _ = a.shape
    pad = np.pad(a, ((1, 1), (1, 1), (0, 0)), mode="edge")
    best = np.full((h, w), 1 << 30)
    for dy in range(3):
        for dx in range(3):
            best = np.minimum(best, np.abs(pad[dy:dy + h, dx:dx + w] - b).max(2))
    return best > tol


def check_hd_matches_native(out, log):
    a = np.asarray(Image.open(os.path.join(out, "native", "frame_009300.bmp")).convert("RGB")).astype(int)
    b = np.asarray(Image.open(os.path.join(out, "frame_009300.bmp")).convert("RGB")).astype(int)
    n = int((structural_diff(a, b) | structural_diff(b, a)).sum())
    return f"{n} pixels differ ({n * 100 / a[..., 0].size:.1f}%)" if n * 100 > a[..., 0].size else None


CHECKS = {"modern_controls": check_modern_controls, "save_load": check_save_load,
          "hd_matches_native": check_hd_matches_native, "gl_matches_native": check_hd_matches_native}
PREPARE = {"save_load": prepare_save_load, "hd_matches_native": prepare_hd_matches_native,
           "gl_matches_native": prepare_hd_matches_native}


def run(s, args):
    name, frames, script, extra, want_audio = s
    out = os.path.join(args.out, name)
    os.makedirs(out, exist_ok=True)
    extra = [e.replace("{out}", out) for e in extra]
    if name in PREPARE:
        msg = PREPARE[name](args, out)
        if msg:
            return name, False, msg, 0.0
    cmd = [args.exe, "--headless", "--frames", str(frames), "--shots", str(frames), out,
           "--wav", os.path.join(out, "audio.wav")] + extra
    if script:
        cmd += ["--script", script]
    cmd.append(args.disc)
    t0 = time.time()
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=1800)
        log = p.stdout + p.stderr
    except subprocess.TimeoutExpired:
        return name, False, "timeout", time.time() - t0
    open(os.path.join(out, "log.txt"), "w", encoding="utf-8").write(log)
    problems = []
    for bad in ("FATAL", "game halted"):
        if bad in log:
            problems.append(log[log.index(bad):].splitlines()[0])
    if "--selftest" in extra and "selftest memcard: PASS" not in log:
        problems.append("self test failed")
    if f"reached frame {frames}" not in log:
        problems.append("did not reach the last frame")
    shot = os.path.join(out, f"frame_{frames:06d}.bmp")
    if os.path.exists(shot):
        ok, mean = frame_ok(shot)
        if not ok:
            problems.append(f"blank final frame (mean {mean:.0f})")
        Image.open(shot).convert("RGB").save(shot[:-4] + ".png")
    else:
        problems.append("no screenshot")
    if name in CHECKS and not problems:
        msg = CHECKS[name](out, log)
        if msg:
            problems.append(msg)
    if want_audio:
        ok, rms = audio_ok(os.path.join(out, "audio.wav"))
        if not ok:
            problems.append(f"silent audio (rms {rms:.0f})")
    return name, not problems, "; ".join(problems) or "ok", time.time() - t0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", default="build/port/xena-console.exe")
    ap.add_argument("--disc", default="../Xena Warrior Princess.bin")
    ap.add_argument("--out", default="build/smoke")
    ap.add_argument("--only")
    ap.add_argument("--jobs", type=int, default=6)
    args = ap.parse_args()
    args.exe = os.path.abspath(args.exe)
    args.disc = os.path.abspath(args.disc)
    todo = [s for s in SCENARIOS if not args.only or s[0] == args.only]
    failed = 0
    with cf.ThreadPoolExecutor(args.jobs) as ex:
        for name, ok, msg, dt in ex.map(lambda s: run(s, args), todo):
            print(f"{'PASS' if ok else 'FAIL'}  {name:16s} {dt:6.1f}s  {msg}", flush=True)
            failed += not ok
    print(f"{len(todo) - failed}/{len(todo)} passed")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
