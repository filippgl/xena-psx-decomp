#!/usr/bin/env python3
"""Export the level-01 ambient creatures (BFLY1, BFLY2 with BFLY1's animation set, LARK, FROG) with
tools/export_enemy_unity.mjs --static and install them as Resources/Creatures/<Name> in both Unity
projects (docs/xena-ambient-audit.md section 2). Wing vertex animation is separate data.

usage: python tools/install_creatures.py [--root .]"""
import argparse, os, shutil, subprocess

ap = argparse.ArgumentParser(); ap.add_argument('--root', default='.'); a = ap.parse_args()
out = os.path.join(a.root, 'build', 'creatures-unity')
for anim, model in [('BFLY1', 'BFLY1'), ('BFLY1', 'BFLY2'), ('LARK', 'LARK'), ('FROG', 'FROG')]:
    subprocess.run(['node', os.path.join(a.root, 'tools', 'export_enemy_unity.mjs'), '--root', a.root, '--level', '01',
                    '--anim', anim, '--model', model, '--static', '--out', out], check=True, stdout=subprocess.DEVNULL)
for proj in ['unity/XenaUnity', 'unity/XenaLightingVerification']:
    dest = os.path.join(a.root, proj, 'Assets', 'Imported', 'Resources', 'Creatures')
    for name in ['Bfly1', 'Bfly2', 'Lark', 'Frog']:
        d = os.path.join(dest, name); os.makedirs(d, exist_ok=True)
        for f in os.listdir(os.path.join(out, name)): shutil.copyfile(os.path.join(out, name, f), os.path.join(d, f))
print('installed Bfly1 Bfly2 Lark Frog')
