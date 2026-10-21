#!/usr/bin/env python3
"""Redraw dumped game textures in high resolution with the Codex CLI (image generation).

usage: codex_textures.py SELECTED.txt DUMP_DIR OUT_DIR [--workers N] [--scale 4] [--limit N]

For each texture listed in SELECTED.txt (manifest lines, see port/runtime/texdump.inc):
  1. Codex generates a detailed remaster of the texture.
  2. Detail transfer: the low frequencies (colours, shapes, exact positions) come from the
     bicubic-upscaled original, only the high-frequency detail from the Codex image, so the
     result stays aligned with the game's UV mapping.
  3. Transparency comes from the original texel mask (smoothly upscaled).
The result OUT_DIR/<name>.png is SCALE times the original size. Existing outputs are skipped,
so the script can be stopped and resumed.
"""
import argparse
import concurrent.futures as cf
import os
import shutil
import subprocess
import sys
import tempfile
import time

from PIL import Image, ImageFilter

PROMPT = (
    "The attached image is a {w}x{h} texture from the 1999 PlayStation game 'Xena: Warrior Princess'"
    "{kind}. Using the image generation tool, create a high-resolution remaster of exactly this "
    "texture: keep the same composition with every feature at the same position, the same colours, "
    "brightness and contrast, the same tiling at the edges; only add fine natural detail and "
    "sharpness, painted in a realistic style. Magenta (#FF00FF) areas are transparent: keep them flat "
    "magenta. No new objects, no text, no borders, no lighting changes. Aspect ratio {w}:{h}. "
    "Save the generated image into the current directory as out.png (copy it from where the tool "
    "stored it). Do nothing else."
)


def prepare_input(src, dst, w, h):
    im = Image.open(src).convert("RGBA")
    bg = Image.new("RGBA", im.size, (255, 0, 255, 255))
    bg.alpha_composite(im)
    # give the model a reasonably sized view of the texture
    k = max(1, 512 // max(w, h))
    bg.convert("RGB").resize((w * k, h * k), Image.NEAREST).save(dst)


def run_codex(workdir, w, h, is3d):
    kind = " used on 3D geometry" if is3d else " used in the 2D interface"
    prompt = PROMPT.format(w=w, h=h, kind=kind)
    with open(os.path.join(workdir, "prompt.txt"), "w", encoding="utf-8") as f:
        f.write(prompt)
    cmd = ["codex", "exec", "--skip-git-repo-check", "-s", "workspace-write", "-C", workdir,
           "-i", os.path.join(workdir, "in.png"), "-"]
    with open(os.path.join(workdir, "prompt.txt"), "rb") as pin, \
            open(os.path.join(workdir, "codex.log"), "wb") as log:
        subprocess.run(cmd, stdin=pin, stdout=log, stderr=subprocess.STDOUT, timeout=900,
                       shell=(os.name == "nt"))
    out = os.path.join(workdir, "out.png")
    return out if os.path.exists(out) else None


def detail_transfer(orig_path, gen_path, scale):
    orig = Image.open(orig_path).convert("RGBA")
    w, h = orig.size
    W, H = w * scale, h * scale
    base = orig.convert("RGB").resize((W, H), Image.BICUBIC)
    gen = Image.open(gen_path).convert("RGB").resize((W, H), Image.LANCZOS)
    radius = scale * 1.2
    gen_low = gen.filter(ImageFilter.GaussianBlur(radius))
    base_low = base.filter(ImageFilter.GaussianBlur(radius))
    # result = original low frequencies + generated high frequencies
    bp, gp, glp, blp = base_low.load(), gen.load(), gen_low.load(), base_low.load()
    out = Image.new("RGBA", (W, H))
    op = out.load()
    alpha = orig.getchannel("A").resize((W, H), Image.BICUBIC).filter(ImageFilter.GaussianBlur(scale * 0.3))
    ap = alpha.load()
    for y in range(H):
        for x in range(W):
            b = bp[x, y]
            g = gp[x, y]
            gl = glp[x, y]
            px = tuple(max(0, min(255, b[i] + (g[i] - gl[i]))) for i in range(3))
            op[x, y] = px + (255 if ap[x, y] >= 128 else 0,)
    return out


def process(entry, args):
    name, w, h, is3d = entry
    dst = os.path.join(args.out, name + ".png")
    if os.path.exists(dst):
        return name, "skip"
    src = os.path.join(args.dump, name + ".png")
    if not os.path.exists(src):
        return name, "missing"
    work = tempfile.mkdtemp(prefix="xtex_", dir=args.tmp)
    try:
        prepare_input(src, os.path.join(work, "in.png"), w, h)
        for attempt in range(2):
            gen = run_codex(work, w, h, is3d)
            if gen:
                break
        if not gen:
            return name, "codex failed"
        shutil.copy(gen, os.path.join(args.raw, name + ".png"))
        detail_transfer(src, gen, args.scale).save(dst)
        return name, "ok"
    except Exception as e:  # keep the batch going
        return name, f"error: {e}"
    finally:
        shutil.rmtree(work, ignore_errors=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("selected")
    ap.add_argument("dump")
    ap.add_argument("out")
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--scale", type=int, default=4)
    ap.add_argument("--limit", type=int, default=0)
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    args.raw = os.path.join(args.out, "_codex_raw")
    args.tmp = os.path.join(args.out, "_tmp")
    os.makedirs(args.raw, exist_ok=True)
    os.makedirs(args.tmp, exist_ok=True)
    entries = []
    for line in open(args.selected):
        p = line.split()
        entries.append((p[0], int(p[5]), int(p[6]), p[7] == "1"))
    # big 3D textures first: they matter most on screen
    entries.sort(key=lambda e: (not e[3], -e[1] * e[2]))
    if args.limit:
        entries = entries[:args.limit]
    t0 = time.time()
    done = 0
    with cf.ThreadPoolExecutor(args.workers) as ex:
        for name, status in ex.map(lambda e: process(e, args), entries):
            done += 1
            print(f"[{done}/{len(entries)} {time.time() - t0:6.0f}s] {name}: {status}", flush=True)


if __name__ == "__main__":
    main()
