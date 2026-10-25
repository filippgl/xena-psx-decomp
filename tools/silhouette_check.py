#!/usr/bin/env python3
"""Shape check for image-to-3D Remaster props: compare the silhouette of a candidate image (Codex
concept or an orthographic render of the generated model) with the orthographic render of the
ORIGINAL object (presentation smoke: build/unity-import/ortho-<name>-original.png).

Masks: non-background pixels (white background, or alpha when present). Each mask is cropped to
its bounding box; aspect = height/width; both are resized to a common box and compared by IoU.
PASS needs aspect within --aspect-tol (relative), IoU >= --iou and IoU <= --max-iou (a near-identical
outline means the low-poly facets were kept, which the Remaster rules reject). An overlay image shows
original-only pixels in red and candidate-only pixels in blue.

usage: silhouette_check.py original.png candidate.png [--iou 0.8] [--aspect-tol 0.1] [--overlay out.png]
"""
import argparse
import sys

import numpy as np
from PIL import Image


def mask(path, flip=False, crop=True):
    im = Image.open(path)
    if im.mode == "RGBA" and im.getextrema()[3][0] < 30:  # real transparency only
        m = np.asarray(im.getchannel("A")) > 128
    else:
        rgb = np.asarray(im.convert("RGB")).astype(np.int16)
        # Background = median colour of the image border (white / off-white studio backgrounds).
        border = np.concatenate([rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]])
        bg = np.median(border, axis=0)
        m = np.abs(rgb - bg).max(axis=2) > 30
    if flip:
        m = m[:, ::-1]
    if not crop:
        return m
    ys, xs = np.nonzero(m)
    if len(ys) == 0:
        raise SystemExit("empty mask: " + path)
    return m[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("original"); ap.add_argument("candidate")
    ap.add_argument("--iou", type=float, default=0.8)
    ap.add_argument("--aspect-tol", type=float, default=0.1)
    ap.add_argument("--max-iou", type=float, default=0.985, help="above this the candidate kept the original (low-poly) outline: rejected")
    ap.add_argument("--overlay")
    ap.add_argument("--mirror", action="store_true", help="also try the candidate mirrored and keep the better")
    ap.add_argument("--same-frame", action="store_true", help="both images come from the same camera (ortho renders): compare in place, no size normalisation")
    args = ap.parse_args()
    if args.same_frame:
        A = mask(args.original, crop=False)
        best = None
        for flip in ([False, True] if args.mirror else [False]):
            B = mask(args.candidate, flip, crop=False)
            iou = float((A & B).sum()) / max(1, float((A | B).sum()))
            if best is None or iou > best[0]: best = (iou, flip, B)
        iou, flip, B = best
        ok = args.iou <= iou <= args.max_iou
        print(f"{'PASS' if ok else 'FAIL'} same-frame iou={iou:.3f} area original={int(A.sum())} candidate={int(B.sum())}" + (" (mirrored)" if flip else ""))
        if args.overlay:
            over = np.full(A.shape + (3,), 255, np.uint8); over[A & B] = (120, 120, 120); over[A & ~B] = (220, 40, 40); over[~A & B] = (40, 90, 220)
            Image.fromarray(over).save(args.overlay)
        sys.exit(0 if ok else 1)
    a = mask(args.original)
    results = []
    for flip in ([False, True] if args.mirror else [False]):
        b = mask(args.candidate, flip)
        ra, rb = a.shape[0] / a.shape[1], b.shape[0] / b.shape[1]
        size = (256, max(8, int(round(256 / ra))))  # (h, w) of the original's box
        A = np.asarray(Image.fromarray(a.astype(np.uint8) * 255).resize((size[1], size[0]))) > 127
        B = np.asarray(Image.fromarray(b.astype(np.uint8) * 255).resize((size[1], size[0]))) > 127
        iou = float((A & B).sum()) / max(1, float((A | B).sum()))
        results.append((iou, abs(rb - ra) / ra, flip, A, B, ra, rb))
    iou, aspect_err, flip, A, B, ra, rb = max(results, key=lambda r: r[0])
    unchanged = iou > args.max_iou
    ok = iou >= args.iou and aspect_err <= args.aspect_tol and not unchanged
    print(f"{'PASS' if ok else 'FAIL (outline unchanged: still the low-poly facets)' if unchanged else 'FAIL'} iou={iou:.3f} aspect original={ra:.3f} candidate={rb:.3f} err={aspect_err:.3f}" + (" (mirrored)" if flip else ""))
    if args.overlay:
        over = np.full(A.shape + (3,), 255, np.uint8)
        over[A & B] = (120, 120, 120); over[A & ~B] = (220, 40, 40); over[~A & B] = (40, 90, 220)
        Image.fromarray(over).save(args.overlay)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
