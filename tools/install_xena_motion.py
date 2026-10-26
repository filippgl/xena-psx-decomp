#!/usr/bin/env python3
"""Export Xena's per-frame root motion (SMA speed/direction, shared DATA\LEVELS\XENA.*) with
tools/export_enemy_unity.mjs --shared and install it as Resources/Xena/XenaMotion.json in both Unity
projects. VERIFIED on the native port: the displacement of the step with clip counter n is
moves[n-1] (per axis trunc(speed*sin/cos)), e.g. 0x51 and 0x2A (docs/ai-progress.md).

usage: python tools/install_xena_motion.py [--root .]"""
import argparse, os, shutil, subprocess

ap = argparse.ArgumentParser(); ap.add_argument('--root', default='.'); a = ap.parse_args()
out = os.path.join(a.root, 'build', 'xena-motion')
subprocess.run(['node', os.path.join(a.root, 'tools', 'export_enemy_unity.mjs'), '--root', a.root, '--shared', '--anim', 'XENA',
                '--model', 'XENA', '--out', out], check=True, stdout=subprocess.DEVNULL)
for proj in ['unity/XenaUnity', 'unity/XenaLightingVerification']:
    d = os.path.join(a.root, proj, 'Assets', 'Imported', 'Resources', 'Xena'); os.makedirs(d, exist_ok=True)
    shutil.copyfile(os.path.join(out, 'Xena', 'XenaMotion.json'), os.path.join(d, 'XenaMotion.json'))
print('installed XenaMotion.json')
