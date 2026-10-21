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


def bleed(img, alpha, passes):
    """Fill transparent pixels with the colour of nearby opaque ones, so texture filtering
    never pulls in the colour of transparent areas."""
    from PIL import ImageChops
    rgb = img.convert("RGB")
    mask = alpha.point(lambda a: 255 if a >= 128 else 0)
    for _ in range(passes):
        grown = rgb.filter(ImageFilter.MaxFilter(3))
        grown_mask = mask.filter(ImageFilter.MaxFilter(3))
        new_px = ImageChops.subtract(grown_mask, mask)
        rgb = Image.composite(grown, rgb, new_px)
        mask = grown_mask
    return rgb


def detail_transfer(orig_path, gen_path, scale):
    import numpy as np
    orig = Image.open(orig_path).convert("RGBA")
    w, h = orig.size
    W, H = w * scale, h * scale
    alpha_lo = orig.getchannel("A")
    has_alpha = alpha_lo.getextrema()[0] < 128
    base_rgb = bleed(orig, alpha_lo, 4) if has_alpha else orig.convert("RGB")
    base = np.asarray(base_rgb.resize((W, H), Image.BICUBIC), dtype=np.float32)
    gen_img = Image.open(gen_path).convert("RGB").resize((W, H), Image.LANCZOS)
    gen = np.asarray(gen_img, dtype=np.float32)
    radius = scale * 1.2
    gen_low = np.asarray(gen_img.filter(ImageFilter.GaussianBlur(radius)), dtype=np.float32)
    detail = gen - gen_low
    alpha = np.asarray(alpha_lo.resize((W, H), Image.BICUBIC).filter(ImageFilter.GaussianBlur(scale * 0.3)), dtype=np.float32)
    if has_alpha:
        # no detail near transparent edges or where the model kept the magenta key colour
        solid = Image.fromarray((alpha >= 128).astype(np.uint8) * 255).filter(ImageFilter.MinFilter(2 * scale + 1))
        weight = np.asarray(solid.filter(ImageFilter.GaussianBlur(scale)), dtype=np.float32) / 255.0
        magenta = (gen[..., 0] > 180) & (gen[..., 2] > 180) & (gen[..., 1] < 110)
        weight[magenta] = 0
        detail *= weight[..., None]
    out = np.clip(base + detail, 0, 255).astype(np.uint8)
    a = np.where(alpha >= 128, 255, 0).astype(np.uint8)
    rgba = np.dstack([out, a])
    result = Image.fromarray(rgba, "RGBA")
    if has_alpha:  # colour under transparent pixels: bleed again at full resolution
        rgb = bleed(result, result.getchannel("A"), 2 * scale)
        result = Image.merge("RGBA", (*rgb.split(), result.getchannel("A")))
    return result


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
    ap.add_argument("--reprocess", action="store_true",
                    help="rebuild outputs from saved Codex images without calling Codex")
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
    if args.reprocess:
        n = 0
        for name, w, h, is3d in entries:
            raw = os.path.join(args.raw, name + ".png")
            if os.path.exists(raw):
                detail_transfer(os.path.join(args.dump, name + ".png"), raw, args.scale).save(os.path.join(args.out, name + ".png"))
                n += 1
        print("reprocessed", n)
        return
    t0 = time.time()
    done = 0
    with cf.ThreadPoolExecutor(args.workers) as ex:
        for name, status in ex.map(lambda e: process(e, args), entries):
            done += 1
            print(f"[{done}/{len(entries)} {time.time() - t0:6.0f}s] {name}: {status}", flush=True)


if __name__ == "__main__":
    main()
