#!/usr/bin/env python3
"""Export the level-01 CLUT colour cycling (docs/xena-water-animation.md) for the Unity port:
for the 8-bit textures NiagraL (terrain falling water, Unity page-9) and rivlette (@rivlet2) write the
texel palette indices, the 256-entry CLUT (15-bit) and the texture's UV origin in its 256x256 page.
Unity (PrototypeWaterCycle) rotates entries 128..178 by one per game step and rewrites the texels.
usage: python tools/export_water_cycle.py [--out unity/.../Resources/Effects/water_cycle.json]
"""
import json, os, sys
sys.path.insert(0, os.path.dirname(__file__))
import export_water_animation as wa

def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    stm = wa.read_stm(root, 'DATA' + chr(92) + 'LEVELS' + chr(92) + '01' + chr(92) + 'LEVEL.STM')
    origins = {'NiagraL': [0, 80], 'rivlette': [64, 128]}  # uvOrigin from water_animation.json
    out = {'first': 128, 'last': 178, 'rateSteps': 1, 'textures': []}
    for name, uv in origins.items():
        t = stm[name]
        out['textures'].append({'name': name, 'uvOrigin': uv, 'width': t['_w'], 'height': t['image']['h'],
                                'indices': t['_idx'], 'clut': t['_pal']})
    for proj in ['unity/XenaUnity', 'unity/XenaLightingVerification']:
        d = os.path.join(root, proj, 'Assets', 'Imported', 'Resources', 'Effects'); os.makedirs(d, exist_ok=True)
        json.dump(out, open(os.path.join(d, 'water_cycle.json'), 'w'), separators=(',', ':'))
    for t in out['textures']: print(t['name'], t['width'], t['height'], len(t['indices']), len(t['clut']))

if __name__ == '__main__':
    main()
