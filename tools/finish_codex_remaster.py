"""Fit Codex image_gen remasters to exact 4x HUD/pickup sprites.

Crop the generated image to its alpha box, resize to 4x the original sprite,
keeping the remaster's own colour and alpha. Output <name>-4x.png.
Usage: python tools/finish_codex_remaster.py <originals dir> <generated dir> <out dir> <factor> <suffix> [names...]
"""
import sys, os
from PIL import Image, ImageFilter, ImageChops

def finish(original_path, generated_path, out_path, factor=4):
    orig = Image.open(original_path).convert('RGBA'); w, h = orig.size
    gen = Image.open(generated_path).convert('RGBA')
    box = gen.getchannel('A').point(lambda a: 255 if a > 16 else 0).getbbox() or (0, 0, *gen.size)
    gen = gen.crop(box).resize((w * factor, h * factor), Image.LANCZOS)
    # Use the remaster's own colour and alpha: mixing in the original silhouette
    # leaves PS1 pixel blocks wherever the painted layout differs slightly.
    colour = gen
    colour.save(out_path)

if __name__ == '__main__':
    src, gen, out, factor, suffix = sys.argv[1:6]
    names = sys.argv[6:] or [f[:-4] for f in os.listdir(gen) if f.endswith('.png')]
    for n in names:
        g = os.path.join(gen, n + '.png')
        if not os.path.exists(g): print('missing', n); continue
        finish(os.path.join(src, n + '.png'), g, os.path.join(out, n + suffix + '.png'), int(factor)); print('ok', n)
