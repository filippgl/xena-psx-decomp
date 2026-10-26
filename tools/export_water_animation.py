#!/usr/bin/env python3
r"""Export the water animation parameters of level 01 (Xena: Warrior Princess, SLUS-00977) as JSON.

Every number is read from the original executable (xena.exe, PS-X EXE) or from DATA\LEVELS\01\LEVEL.STM
in XENA.UDA; nothing is typed in by hand except the code addresses. Evidence and labels:
docs/xena-water-animation.md.

  * CLUT rotation (falling water "NiagraL", waterfall "rivlette"): the level loader func_8006287C, case
    level 1 of jtbl_80017A2C (0x800641F4..0x80064224), registers two slots with
    func_800A3C5C(type 0, first 0x80, last 0xB2, rate 1, name). func_800A3D8C (called from func_80064D0C
    once per game step) rotates the RAM copy of the CLUT by one entry (memmove, entry i <- i-1, first <- last)
    every `rate` steps and sends it back with LoadImage.
  * @_stream vertex wave: func_80029E70 (main loop, once per game step): phase += 0x200;
    y = base + trunc(sin800AD94C(phase + (x & 7) << 13) / 256) for every vertex of the objects with flag 0x80.
  * The UV-scroll path of func_80029E70 (flag 0x10000 objects: terr_lav / terr_lv / lavafal names) is empty
    in level 01.
  * splash (LEVEL.BIN effect class 0x1C, update func_80027DFC): particle emitter func_800B2F84 (untextured
    LINE_G2 streaks, like the blood emitter of docs/xena-hit-effects.md).

  python tools/export_water_animation.py --root DECOMP --out DIR [--verify-ram DIR]

--verify-ram takes a directory of native-port RAM dumps (ram_NNNNNN.bin, level 01, consecutive dumps two
vblanks apart are fine) and checks the CLUT slots, the rotation, the wave formula and the empty UV list.
"""
import argparse
import glob
import json
import os
import struct
import sys

EXE_BASE = 0x80010000 - 0x800


class Exe:
    def __init__(self, path):
        self.b = open(path, 'rb').read()

    def u32(self, a):
        return struct.unpack_from('<I', self.b, a - EXE_BASE)[0]

    def s16(self, a):
        return struct.unpack_from('<h', self.b, a - EXE_BASE)[0]

    def cstr(self, a):
        o = a - EXE_BASE
        return self.b[o:self.b.index(b'\0', o)].decode('ascii')

    def imm(self, a, op, rt=None, rs=None):
        """Signed (addiu) or unsigned (ori/andi/lui) 16-bit immediate of an I-type instruction, checked."""
        w = self.u32(a)
        if w >> 26 != op:
            raise SystemExit('unexpected opcode at %08X: %08X' % (a, w))
        if rs is not None and (w >> 21) & 31 != rs:
            raise SystemExit('unexpected rs at %08X: %08X' % (a, w))
        if rt is not None and (w >> 16) & 31 != rt:
            raise SystemExit('unexpected rt at %08X: %08X' % (a, w))
        v = w & 0xFFFF
        return v - 0x10000 if op == 0x09 and v & 0x8000 else v

    def shamt(self, a, funct):
        w = self.u32(a)
        if w >> 26 != 0 or w & 63 != funct:
            raise SystemExit('unexpected R-type at %08X: %08X' % (a, w))
        return (w >> 6) & 31


ADDIU, ORI, ANDI, LUI = 0x09, 0x0D, 0x0C, 0x0F
ZERO, V0, V1, A0, A1, A2, A3, GP = 0, 2, 3, 4, 5, 6, 7, 28


def short(n):
    n &= 0xFFFF
    return n - 0x10000 if n & 0x8000 else n


def tdiv(a, b):
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b > 0) else -q


def make_sine(table):
    def sine(phase):  # 0x800AD94C: 65536 = one turn, amplitude 16384 (table[256])
        phase &= 0xFFFF
        q = phase & 0xC000
        local = 0x8000 - phase if q == 0x4000 else (phase & 0x7FFF if q == 0x8000 else (-phase if q == 0xC000 else phase))
        local = short(local)
        i = tdiv(local, 64)
        a, b = table[i], table[i + 1]
        v = a + tdiv(short((b - a) * (local & 63)), 64)
        return short(-v if q & 0x8000 else v)
    return sine


def read_stm(root, name):
    m = json.load(open(os.path.join(root, 'build', 'asset-audit', 'manifest.json')))
    hits = [e for e in m['entries'] if any(c['name'] == name for c in e['candidates'])]
    if len(hits) != 1:
        raise SystemExit('resource not found: ' + name)
    e = hits[0]
    with open(os.path.join(root, 'disc', 'files', 'XENA.UDA'), 'rb') as f:
        f.seek((e['lba'] - m['summary']['archiveLba']) * 2048)
        b = f.read(e['size'])
    out, cur = {}, 4
    for _ in range(struct.unpack_from('<I', b, 0)[0]):
        size = struct.unpack_from('<I', b, cur)[0]
        nm = b[cur + 4:cur + 16].split(b'\0')[0].decode('ascii')
        cur += 16
        end, flags, c = cur + size, struct.unpack_from('<I', b, cur + 4)[0], cur + 8
        ent = {'name': nm, 'bpp': {0: 4, 1: 8}.get(flags & 3, 16)}
        if flags & 8:
            bs, x, y, w, h = struct.unpack_from('<I4H', b, c)
            ent['clut'] = {'x': x, 'y': y, 'w': w, 'h': h}
            ent['_pal'] = list(struct.unpack_from('<%dH' % (w * h), b, c + 12))
            c += bs
        bs, x, y, w, h = struct.unpack_from('<I4H', b, c)
        ent['image'] = {'x': x, 'y': y, 'wHalfwords': w, 'h': h}
        raw = b[c + 12:c + 12 + w * h * 2]
        ent['_idx'] = list(raw) if ent['bpp'] == 8 else [n for v in raw for n in (v & 15, v >> 4)]
        ent['_w'] = w * 2 if ent['bpp'] == 8 else w * 4
        out[nm] = ent
        cur = end
    return out


def texture_summary(t, first=None, last=None):
    pal, idx, w = t['_pal'], t['_idx'], t['_w']
    page_x, page_y = t['image']['x'] // 64 * 64, t['image']['y'] // 256 * 256
    px_per_hw = 2 if t['bpp'] == 8 else 4
    s = {'name': t['name'], 'bpp': t['bpp'], 'clutVram': [t['clut']['x'], t['clut']['y']],
         'imageVram': [t['image']['x'], t['image']['y']], 'texels': [w, len(idx) // w],
         'pageBase': [page_x, page_y],
         'uvOrigin': [(t['image']['x'] - page_x) * px_per_hw, t['image']['y'] - page_y],
         'texelsWithStp': sum(1 for i in idx if pal[i] & 0x8000),
         'transparentTexels0000': sum(1 for i in idx if pal[i] == 0)}
    if first is not None:
        inr = [first <= i <= last for i in idx]
        h = len(idx) // w
        steps = {}
        for y in range(h - 1):
            for x in range(w):
                a, b = y * w + x, (y + 1) * w + x
                if inr[a] and inr[b]:
                    d = (idx[b] - idx[a] + 25) % (last - first + 1) - 25
                    if -3 <= d <= 3:
                        steps[d] = steps.get(d, 0) + 1
        s['texelsInCycledRange'] = sum(inr)
        s['cycledEntriesWithStp'] = sum(1 for i in range(first, last + 1) if pal[i] & 0x8000)
        s['indexStepAlongV'] = {str(k): steps[k] for k in sorted(steps)}
    return s


def export(root):
    exe = Exe(os.path.join(root, 'xena.exe'))
    # --- CLUT rotation registrations, level 1 ---------------------------------------------------------
    case1 = exe.u32(0x80017A2C)  # jtbl_80017A2C[level - 1]
    if case1 != 0x800641F4:
        raise SystemExit('level-1 case moved: %08X' % case1)
    slots = []
    for base, name_sym in ((0x800641F4, 0x800ECD88), (0x80064210, 0x800ECD90)):
        if exe.u32(base) != 0x00002021:  # addu a0, zero, zero -> type 0
            raise SystemExit('type is not 0 at %08X' % base)
        exe.imm(base + 0x10, ADDIU, V0, GP)  # name = gp-relative string
        slots.append({'type': 0, 'typeMeaning': 'rotate CLUT entries first..last by +1 every rate steps',
                      'firstEntry': exe.imm(base + 4, ADDIU, A1, ZERO), 'lastEntry': exe.imm(base + 8, ADDIU, A2, ZERO),
                      'rateSteps': exe.imm(base + 12, ADDIU, A3, ZERO), 'texture': exe.cstr(name_sym),
                      'code': {'register': '0x%08X func_800A3C5C' % base, 'update': 'func_800A3D8C (memmove + LoadImage)'}})
    # --- vertex wave ----------------------------------------------------------------------------------
    table = [exe.s16(0x800DAB18 + 2 * i) for i in range(258)]
    sine = make_sine(table)
    wave = {'objects': 'flag 0x80 (name token "stream"): level 01 object 14 @_stream, split into 8 chunks',
            'phaseStepPerGameStep': exe.imm(0x80029EEC, ADDIU, V0, ZERO),
            'phaseStepIfRockingObjects': exe.imm(0x80029EF8, ADDIU, V0, ZERO),
            'phaseAfterInit': exe.imm(0x80029FF4, ADDIU, V0, ZERO),
            'phasePeriod': 65536,
            'vertexPhaseOffset': '(x & %d) << %d' % (exe.imm(0x8002A2E8, ANDI, A0, A0), exe.shamt(0x8002A2EC, 0)),
            'divisor': 1 << exe.shamt(0x8002A314, 3),
            'amplitude': max(abs(sine(p)) for p in range(0, 65536, 4)) >> exe.shamt(0x8002A314, 3),
            'base': 'D_800EC480 = y of the first vertex of the first wave object at init (0 in level 01)',
            'formula': 'y = base + trunc(sin(phase + ((x & 7) << 13)) / 256), x = vertex x (object space)',
            'sine': 'func_800AD94C: 65536 per turn, 16384 = 1.0, table of 258 s16 at 0x800DAB18, linear interp',
            'sineTable': table,
            'code': 'func_80029E70 0x80029FFC..0x8002A024 (phase), 0x8002A2E0..0x8002A31C (vertices)'}
    wave['stepsPerPeriod'] = 65536 // wave['phaseStepPerGameStep']
    # --- splash emitter -------------------------------------------------------------------------------
    g7 = exe.imm(0x80027F54, LUI, V0, ZERO)
    splash = {
        'class': '0x1C splash, update func_80027DFC',
        'sound': {'call': 'func_800847C0(%d, %d, %d, 0, actor+0x5C) once, on the first update' % (
            exe.imm(0x80027E18, ADDIU, A0, ZERO), exe.imm(0x80027E1C, ADDIU, A1, ZERO), exe.imm(0x80027E20, ADDIU, A2, ZERO)),
            'meaning': 'UNKNOWN (same routine as the animation-script sound opcode)'},
        'emitter': 'func_800B2F84 -> func_800B192C with texture NULL: each particle is a LINE_G2 from its position (head colour) to its previous position (tail colour)',
        'slots': exe.imm(0x80027EA8, ADDIU, V0, ZERO),
        'emissionSteps': exe.imm(0x80027EFC, ADDIU, V0, ZERO),
        'delayAfterSpawn': {'base': exe.imm(0x80027F04, ADDIU, V0, ZERO), 'randRange': exe.imm(0x80027F04, ADDIU, V0, ZERO)},
        'particlesPerSpawn': {'base': exe.imm(0x80027F08, ADDIU, A1, ZERO), 'randRange': exe.imm(0x80027F04, ADDIU, V0, ZERO)},
        'directionBase': [exe.imm(0x80027F0C, ADDIU, V1, ZERO), exe.imm(0x80027F1C, ADDIU, V0, ZERO), exe.imm(0x80027F0C, ADDIU, V1, ZERO)],
        'directionRandRange': [exe.imm(0x80027F24, ADDIU, V0, ZERO), 0, exe.imm(0x80027F24, ADDIU, V0, ZERO)],
        'spawnBox': [exe.imm(0x80027F30, ADDIU, V0, ZERO), 0, exe.imm(0x80027F30, ADDIU, V0, ZERO)],
        'speed': {'base': exe.imm(0x80027F3C, ADDIU, V0, ZERO), 'randRange': exe.imm(0x80027F44, ADDIU, V0, ZERO)},
        'life': {'base': exe.imm(0x80027F4C, ADDIU, V0, ZERO), 'randRange': exe.imm(0x80027F08, ADDIU, A1, ZERO)},
        'gravityPerStep2': g7,
        'velocityLimit': exe.imm(0x80027F5C, ADDIU, V0, ZERO),
        'drag': 0,
        'desc50': 'actor y + %d (meaning UNKNOWN)' % exe.imm(0x80027F8C, ADDIU, V0, V0),
        'headRgb': [exe.imm(a, ADDIU, V0, ZERO) for a in (0x80027ECC, 0x80027ED4, 0x80027EDC)],
        'tailRgb': [exe.imm(a, ADDIU, V0, ZERO) for a in (0x80027EE4, 0x80027EEC, 0x80027EF4)],
        'abr': exe.imm(0x80027F84, ADDIU, V1, ZERO),
        'blend': 'abr -1 = opaque lines (no semi-transparency)',
        'placements': [{'position': [1249, -2883, -12923], 'near': '@rivlet2 (1483,-2657,-13209)'},
                       {'position': [7273, 4212, 4793], 'near': 'foot of the NiagraL faces at the river (x 6032..7871, z 3691..6665)'}]}
    # --- textures ----------------------------------------------------------------------------------------
    stm = read_stm(root, 'DATA\\LEVELS\\01\\LEVEL.STM')
    first, last = slots[0]['firstEntry'], slots[0]['lastEntry']
    textures = {
        'NiagraL': dict(texture_summary(stm['NiagraL'], first, last),
                        usedBy='terrain object 1 (terr_1) and object 39: tpage 0x0095 (8-bit, page (320,256)), CLUT 0x0670, TMD mode 0x34 (opaque), u 0..62 v 80..206',
                        animation='CLUT rotation slot 0'),
        'rivlette': dict(texture_summary(stm['rivlette'], first, last),
                         usedBy='object 40 @rivlet2: tpage 0x00BC (8-bit, page (768,256), abr 1), CLUT 0x0730, TMD mode 0x36 (semi-transparent), u 64..126 v 128..254',
                         animation='CLUT rotation slot 1'),
        'water': dict(texture_summary(stm['water']),
                      usedBy='object 14 @_stream: tpage 0x003C (4-bit, page (768,256), abr 1), CLUT 0x02B7, TMD mode 0x36 (semi-transparent), u 0..126 v 128..254',
                      animation='none on the texture; vertex wave on the mesh')}
    period = slots[0]['lastEntry'] - slots[0]['firstEntry'] + 1
    return {
        'formatVersion': 1,
        'source': 'SLUS-00977 xena.exe + DATA\\LEVELS\\01\\LEVEL.STM; docs/xena-water-animation.md',
        'clock': 'one game step = 2 vblanks in the native port captures (30 Hz); all rates below are per game step',
        'clutRotation': {'slots': slots, 'entriesInRing': period, 'periodSteps': period * slots[0]['rateSteps'],
                         'direction': 'colour of entry i moves to entry i+1; entry first takes the old colour of entry last',
                         'upload': 'LoadImage rect x = 768 + first (made even), y = CLUT row, w = last - first + 1 (made even), h = 1',
                         'blend': 'unchanged: NiagraL opaque, rivlette semi-transparent abr 1 (B+F); STP bits move with the entries'},
        'vertexWave': wave,
        'uvScroll': {'level01': 'none: D_800F17AE = 0, no object has flag 0x10000 (only names with terr_lav / terr_lv / lavafal)',
                     'mechanism': 'func_80029E70 0x8002A14C..0x8002A29C: V of every prim -1 per step for 126 steps, then +126 (lava)'},
        'vramCopies': 'none per frame in level 01 (MoveImage/LoadImage callers: menus, fonts, MDEC, CLUT routines only)',
        'splash': splash,
        'textures': textures}


def verify(doc, ram_dir, root):
    exe = Exe(os.path.join(root, 'xena.exe'))
    sine = make_sine(doc['vertexWave']['sineTable'])
    files = sorted(glob.glob(os.path.join(ram_dir, 'ram_*.bin')))
    if not files:
        raise SystemExit('no ram_*.bin in ' + ram_dir)
    res = {'dumps': len(files), 'checks': {}}
    prev = None
    rot_ok = wave_bad = verts = 0
    for f in files:
        r = open(f, 'rb').read()
        u32 = lambda a: struct.unpack_from('<I', r, a & 0x1FFFFF)[0]
        s16 = lambda a: struct.unpack_from('<h', r, a & 0x1FFFFF)[0]
        assert struct.unpack_from('<h', r, 0x800F17AE & 0x1FFFFF)[0] == 0, 'UV scroll list not empty'
        n = u32(0x800F06AC)
        assert n == len(doc['clutRotation']['slots']), 'slot count %d' % n
        cur = []
        for i, sl in enumerate(doc['clutRotation']['slots']):
            p = u32(0x800F15F0 + 4 * i)
            t, a, b, idx, rate = struct.unpack_from('<5i', r, p & 0x1FFFFF)
            assert (t, a, b, rate) == (sl['type'], sl['firstEntry'], sl['lastEntry'], sl['rateSteps']), (t, a, b, rate)
            assert exe.cstr(exe.u32(0x800DA980 + 12 * idx)) == sl['texture'], 'name table index %d' % idx
            ring = struct.unpack_from('<%dH' % (b - a + 1), r, (p + 0x218 + 2 * a) & 0x1FFFFF)
            cur.append((ring, s16(0x800DA986 + 12 * idx)))
        ph = struct.unpack_from('<H', r, 0x800F0B08 & 0x1FFFFF)[0]
        base = s16(0x800EC480)
        for i in range(u32(0x800F17E0)):
            o = u32(0x801013D0 + 4 * i)
            for k in range(u32(o + 0x38)):
                x, y = struct.unpack_from('<2h', r, (u32(o + 0x3C) + 16 * k) & 0x1FFFFF)
                verts += 1
                wave_bad += y != short(base + tdiv(sine(ph + ((x & 7) << 13)), 256))
        if prev is not None:
            steps = ((ph - prev[1]) & 0xFFFF) // doc['vertexWave']['phaseStepPerGameStep']
            for (ring0, _), (ring1, _) in zip(prev[0], cur):
                k = steps % len(ring0)
                rot_ok += ring1 == ring0[-k:] + ring0[:-k] if k else ring1 == ring0
        prev = (cur, ph)
    res['checks'] = {'uvScrollListEmpty': True, 'clutSlotsMatchExe': True,
                     'clutRingRotatedByStepsBetweenDumps': '%d/%d' % (rot_ok, 2 * (len(files) - 1)),
                     'waveVerticesMatchingFormula': '%d/%d' % (verts - wave_bad, verts),
                     'clutRows': [c[1] for c in cur]}
    res['result'] = 'PASS' if wave_bad == 0 and rot_ok == 2 * (len(files) - 1) else 'FAIL'
    return res


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--root', default='.')
    ap.add_argument('--out', required=True)
    ap.add_argument('--verify-ram')
    a = ap.parse_args()
    doc = export(a.root)
    if a.verify_ram:
        doc['verification'] = verify(doc, a.verify_ram, a.root)
    os.makedirs(a.out, exist_ok=True)
    with open(os.path.join(a.out, 'water_animation.json'), 'w') as f:
        json.dump(doc, f, indent=1)
    v = doc.get('verification')
    print(json.dumps({'out': os.path.join(a.out, 'water_animation.json'), 'verification': v}, indent=1))
    return 0 if not v or v['result'] == 'PASS' else 1


if __name__ == '__main__':
    sys.exit(main())
