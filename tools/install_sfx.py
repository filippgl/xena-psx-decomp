#!/usr/bin/env python3
"""Install the exported sound effects (tools/export_sfx.mjs -> build/sfx/export) as Unity
Resources/Sfx in both Unity projects, plus Resources/Sfx/index.json for PrototypeSfx.

usage: python tools/install_sfx.py [--root .]

One-shot sounds are copied as rendered (note 60, vol 127, pan 64; note variants alongside).
Looping VAB sounds also get a loop body: the raw sample from its loop start, played by Unity
with loop = true at pitch = sampleRateAtNote60 * 2^((note-60)/12) / rawRate; its gain matches
the rendered 2 s hold (RMS ratio, APPROXIMATED: the ADSR shape of the hold is not reproduced).
Original data stays ignored by Git (Assets/Imported)."""
import argparse, json, math, os, shutil, struct, wave

ap = argparse.ArgumentParser(); ap.add_argument('--root', default='.'); a = ap.parse_args()
src = os.path.join(a.root, 'build', 'sfx', 'export')
meta = json.load(open(os.path.join(src, 'sfx.json'), encoding='utf-8'))

def frames(path):
    w = wave.open(path); n = w.getnframes(); ch = w.getnchannels(); data = w.readframes(n); rate = w.getframerate(); w.close()
    v = struct.unpack('<%dh' % (len(data) // 2), data)
    return [sum(v[i * ch:(i + 1) * ch]) / ch for i in range(n)], rate

def rms(v):
    return math.sqrt(sum(x * x for x in v) / max(1, len(v)))

files, index = {}, []
for s in meta['sounds']:
    sid = int(s['id'], 16); entry = {'id': sid, 'file': '', 'loop': bool(s.get('loop')), 'notes': [], 'noteFiles': [],
                                    'loopFile': '', 'loopRate60': 0.0, 'loopGain': 0.0}
    if not s.get('file') or not os.path.exists(os.path.join(src, s['file'])): continue
    entry['file'] = 'x%04X' % sid; files[entry['file']] = s['file']
    for v in s.get('variants', []):
        name = 'x%04X_n%d' % (sid, v['note']); files[name] = v['file']; entry['notes'].append(v['note']); entry['noteFiles'].append(name)
    tone = next((t for t in s.get('tones', []) if t.get('looped') and t.get('rawFile')), None)
    if entry['loop'] and tone:
        raw, rate = frames(os.path.join(src, tone['rawFile'])); start = tone.get('loopStartFrame') or 0
        body = raw[start:]; render, _ = frames(os.path.join(src, s['file']))
        hold = render[: int(meta['rate'] * 2)]
        name = 'x%04X_loop' % sid; out = os.path.join(src, name + '.wav')
        w = wave.open(out, 'wb'); w.setnchannels(1); w.setsampwidth(2); w.setframerate(rate)
        w.writeframes(struct.pack('<%dh' % len(body), *[int(max(-32768, min(32767, x))) for x in body])); w.close()
        files[name] = name + '.wav'
        entry.update(loopFile=name, loopRate60=tone['sampleRateAtNote60'] / rate, loopGain=rms(hold) / max(1e-6, rms(body)))
    index.append(entry)
for proj in ['unity/XenaUnity', 'unity/XenaLightingVerification']:
    d = os.path.join(a.root, proj, 'Assets', 'Imported', 'Resources', 'Sfx'); os.makedirs(d, exist_ok=True)
    for name, f in files.items(): shutil.copyfile(os.path.join(src, f), os.path.join(d, name + '.wav'))
    json.dump({'sounds': index}, open(os.path.join(d, 'index.json'), 'w'), separators=(',', ':'))
print('sounds', len(index), 'files', len(files), 'loops', [hex(e['id']) for e in index if e['loopFile']])
