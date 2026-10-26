#!/usr/bin/env python3
"""Flatten build/contact-points/contact_points.json (tools/export_contact_points.mjs) for Unity's
JsonUtility and install it as Resources/Combat/contact_points.json in both Unity projects.

usage: python tools/install_contact_points.py [--root .]

Per clip frame: damage, active point mask, points (x,y,z PSX attacker-local units, y down),
and for the bandit the part-3 twist pivot (origin, axis) plus a mask of the points on part 3's chain
(docs/xena-contact-points.md sections 2..6)."""
import argparse, json, os

ap = argparse.ArgumentParser(); ap.add_argument('--root', default='.'); a = ap.parse_args()
src = json.load(open(os.path.join(a.root, 'build', 'contact-points', 'contact_points.json'), encoding='utf-8'))
actors = []
for name, actor in src['actors'].items():
    twist = (actor.get('twistParts') or [None, None])[1]
    chained = sum(1 << i for i, p in enumerate(actor['points']) if twist is not None and twist in p['chain'])
    clips = []
    for c in actor['clips']:
        frames = []
        for f in c['frames']:
            piv = next((p for p in f.get('twistPivots', []) if p['part'] == twist), None)
            frames.append({'damage': f['damage'], 'active': sum(1 << i for i in f['active']),
                           'points': [v for p in f['points'] for v in p],
                           'pivot': (piv['origin'] + piv['axis']) if piv else []})
        clips.append({'slot': c['slot'], 'frames': frames})
    actors.append({'name': name, 'radius': actor['radius'], 'height': actor['height'], 'twistChain': chained, 'clips': clips})
out = {'actors': actors}
for proj in ['unity/XenaUnity', 'unity/XenaLightingVerification']:
    d = os.path.join(a.root, proj, 'Assets', 'Imported', 'Resources', 'Combat'); os.makedirs(d, exist_ok=True)
    json.dump(out, open(os.path.join(d, 'contact_points.json'), 'w'), separators=(',', ':'))
print('actors', [(x['name'], len(x['clips'])) for x in actors])
