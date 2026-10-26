#!/usr/bin/env python3
"""Run the automatic level inspector (PrototypeInspector) on the built Unity player and summarise it.

usage: python tools/run_inspector.py [--out build/inspect] [--top 16] [--keep]

Writes <out>/inspect-sheet.png (the worst flagged views, labelled with mode, point, heading and hole share)
and <out>/inspect-summary.txt; the per-view PNGs are deleted unless --keep (disk use)."""
import argparse, glob, json, os, subprocess, sys
from PIL import Image, ImageDraw

ap = argparse.ArgumentParser(); ap.add_argument('--out', default='build/inspect'); ap.add_argument('--top', type=int, default=16)
ap.add_argument('--keep', action='store_true'); a = ap.parse_args()
out = os.path.abspath(a.out); os.makedirs(out, exist_ok=True)
for f in glob.glob(os.path.join(out, 'inspect-*.png')) + glob.glob(os.path.join(out, 'inspect.json')): os.remove(f)
exe = os.path.abspath('build/unity-movement/XenaMovementPrototype.exe')
code = subprocess.call([exe, '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '-logFile', os.path.join(out, 'inspect.log'), '--no-settings', '--inspect', out], timeout=1800)
report_path = os.path.join(out, 'inspect.json')
if code != 0 or not os.path.exists(report_path): sys.exit('inspector failed (exit %s), see %s' % (code, os.path.join(out, 'inspect.log')))
r = json.load(open(report_path, encoding='utf-8')); views = r['views_']
flagged = [v for v in views if v.get('file')]
lines = ['points %d, Remaster views %d, flagged %d (Remaster hole share below the horizon exceeds Original by > %.3f)' % (r['points'], r['views'], r['flagged'], r['threshold'])]
for mode in ('Remaster', 'Original'):
    m = [v for v in flagged if v['mode'] == mode]; lines.append('%s: %d flagged' % (mode, len(m)))
top = flagged[:a.top]
for v in top: lines.append('%-8s (%.1f, %.1f, %.1f) heading %3d  holes %.1f%% (Original %.1f%%)  %s' % (v['mode'], v['x'], v['y'], v['z'], v['heading'], v['holes'] * 100, v['originalHoles'] * 100, v['file']))
open(os.path.join(out, 'inspect-summary.txt'), 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
if top:
    W, H = 480, 270; cols = 4; sheet = Image.new('RGB', (W * cols, H * ((len(top) + cols - 1) // cols)))
    for k, v in enumerate(top):
        im = Image.open(os.path.join(out, v['file'])).convert('RGB'); d = ImageDraw.Draw(im)
        d.rectangle((0, 0, W, 14), fill=(0, 0, 0)); d.text((3, 1), '(%.0f,%.0f,%.0f) h%d holes %.1f%% vs orig %.1f%%' % (v['x'], v['y'], v['z'], v['heading'], v['holes'] * 100, v['originalHoles'] * 100), fill=(255, 255, 0))
        sheet.paste(im, ((k % cols) * W, (k // cols) * H))
    sheet.save(os.path.join(out, 'inspect-sheet.png'))
if not a.keep:
    for f in glob.glob(os.path.join(out, 'inspect-*-*.png')): os.remove(f)
print('\n'.join(lines))
