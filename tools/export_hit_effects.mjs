#!/usr/bin/env node
// Export the original combat hit effects (flash star, shock ring, spark discs, blood streaks) as
// reference PNG frames plus a JSON parameter file. See docs/xena-hit-effects.md for the evidence.
//
// None of these effects uses a texture: func_80028650 draws untextured POLY_G4 quads through
// func_80077260 / func_800776C8 -> func_800B73F0 (GP0 0x3A, semi-transparent, abr 1 = additive), and
// the blood emitter (func_800B2F84 -> func_800B192C with no texture) draws LINE_G2 (GP0 0x50, opaque).
// The PNGs below are therefore rasterised from the original formulas, not cut from VRAM.
// Every constant used here is first checked against the instruction bytes in xena.exe.
//
//   node tools/export_hit_effects.mjs --root DECOMP --out DIR [--depth 256]
//
// --depth is the screen scale factor w = 256 - SZ/32 used by func_80028650 (256 = at the camera,
// 200 is roughly SZ 1800). Output: hit_effects.json, flash_<type>_d<d>.png, ring_<type>_d<d>.png,
// spark_disc_life<n>.png. Each PNG is a strip with one 96x96 cell per game step (birth first).
// Colours are the additive contribution (black = no change); alpha = max(r, g, b).
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i < 0 ? fallback : argv[i + 1]; };
const root = path.resolve(option('--root', '.'));
const out = path.resolve(option('--out', 'hit-effects'));
const depth = Number(option('--depth', '256'));
const { png } = await import(pathToFileURL(path.join(root, 'tools', 'lib', 'png.mjs')).href);

const exe = fs.readFileSync(path.join(root, 'xena.exe'));
const base = exe.readUInt32LE(0x18);
const eo = a => a - base + 0x800;
// Instruction bytes exactly as listed in asm/nonmatchings/game/*.s (little-endian byte order).
const checks = [
  [0x80028938, 'd0010624', 'flash star 1 half-width 0x1D0'],
  [0x8002897c, '99014226', 'flash star 2 angle offset +0x199'],
  [0x800289b8, '20030624', 'flash star 2 half-width 0x320'],
  [0x80028fa4, 'aa020524', 'tilt rand(0x100, 0x2AA)'],
  [0x80029208, '0c001124', 'flash life cap 12'],
  [0x80029260, '1a004228', 'ring only when d >= 26 (slti 0x1A)'],
  [0x800292b4, '12000524', 'spark velocity rand(-18, 18)'],
  [0x800292ec, '14000524', 'spark life rand(10, 20)'],
  [0x80028e4c, '0c00a22a', 'effect pool of 12 slots'],
  [0x800b7504, '38004a35', 'POLY_G4 code 0x38 | semi bit'],
  [0x800b1c34, '50000824', 'blood LINE_G2 code 0x50'],
  [0x80037c7c, '28000224', 'Xena blood pool 40'],
  [0x80037d24, '0f000224', 'blood speed range 15'],
  [0x8001ea88, '14000224', 'bandit death blood emission 20 steps'],
  [0x8001ea8c, '0a000224', 'bandit hit blood emission 10 steps'],
];
for (const [addr, hex, what] of checks) {
  const got = exe.subarray(eo(addr), eo(addr) + 4).toString('hex');
  if (got !== hex) throw Error(`xena.exe mismatch at ${addr.toString(16)} (${what}): ${got} != ${hex}`);
}
// Colour table D_800C0F4C: 3 x {centre/tip c0, side/inner c1, outer c2} RGB0.
const table = [0, 1, 2].map(t => [0, 1, 2].map(c => [...exe.subarray(eo(0x800c0f4c) + t * 12 + c * 4, eo(0x800c0f4c) + t * 12 + c * 4 + 3)]));
const typeName = ['hit_red', 'block_blue', 'special_yellow'];

const sin = a => Math.sin(a * Math.PI / 2048), cos = a => Math.cos(a * Math.PI / 2048);
const scale = (c, f) => c.map(v => (v * f) >> 8);
const trunc = v => Math.trunc(v);

// Additive gouraud rasteriser for PS1 quads (triangles v0 v1 v2 and v1 v2 v3).
function canvas(n) { return { w: 96 * n, h: 96, px: new Float32Array(96 * n * 96 * 3) }; }
function tri(cv, ox, a, b, c) {
  const minX = Math.max(ox, Math.floor(Math.min(a.x, b.x, c.x))), maxX = Math.min(ox + 95, Math.ceil(Math.max(a.x, b.x, c.x)));
  const minY = Math.max(0, Math.floor(Math.min(a.y, b.y, c.y))), maxY = Math.min(95, Math.ceil(Math.max(a.y, b.y, c.y)));
  const area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
  if (!area) return;
  for (let y = minY; y <= maxY; y++) for (let x = minX; x <= maxX; x++) {
    const px = x + 0.5, py = y + 0.5;
    const w0 = ((b.x - px) * (c.y - py) - (c.x - px) * (b.y - py)) / area;
    const w1 = ((c.x - px) * (a.y - py) - (a.x - px) * (c.y - py)) / area;
    const w2 = 1 - w0 - w1;
    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
    const i = (y * cv.w + x) * 3;
    for (let k = 0; k < 3; k++) cv.px[i + k] += w0 * a.c[k] + w1 * b.c[k] + w2 * c.c[k];
  }
}
const quad = (cv, ox, v) => { tri(cv, ox, v[0], v[1], v[2]); tri(cv, ox, v[1], v[2], v[3]); };
function save(cv, file) {
  const rgba = new Uint8Array(cv.w * cv.h * 4);
  for (let i = 0; i < cv.w * cv.h; i++) {
    const r = Math.min(255, cv.px[i * 3]), g = Math.min(255, cv.px[i * 3 + 1]), b = Math.min(255, cv.px[i * 3 + 2]);
    rgba.set([r, g, b, Math.max(r, g, b)], i * 4);
  }
  fs.writeFileSync(path.join(out, file), png(cv.w, cv.h, rgba));
}

// func_80077260: n-point star, spikes from the centre (c2) through sides at r/8 (c1) to the tip (c0).
function star(cv, ox, cx, cy, n, half, angle, radius, c0, c1, c2) {
  const mid = radius >> 3;
  for (let i = 0; i < n; i++) {
    const a = angle + trunc(4096 / n) * i;
    const p = (ang, r, c) => ({ x: cx + sin(ang) * r, y: cy + cos(ang) * r, c });
    quad(cv, ox, [p(a, radius, c0), p(a + half, mid, c1), p(a - half, mid, c1), p(a, 0, c2)]);
  }
}
// func_800776C8: elliptical ring / disc, x = sin(a)*r, y = cos(a + tilt)*r; outer colour cOut, inner cIn.
function ring(cv, ox, cx, cy, thick, inner, spin, tilt, cOut, cIn) {
  const s = inner + thick, seg = s < 6 ? 4 : s < 12 ? 6 : s < 20 ? 8 : s < 40 ? 12 : 16, step = trunc(4096 / seg);
  const p = (ang, r, c) => ({ x: cx + sin(ang) * r, y: cy + cos(ang + tilt) * r, c });
  for (let j = 0; j < seg; j++) {
    const a = spin + step * j;
    quad(cv, ox, [p(a, inner + thick, cOut), p(a + step, inner + thick, cOut), p(a, inner, cIn), p(a + step, inner, cIn)]);
  }
}

fs.mkdirSync(out, { recursive: true });
const files = [];
const tilt = 0x1c0, rot = 0x1c0; // one sample of rand(0x100, 0x2AA); the sign is random in the game
for (const d of [15, 30]) {
  const q = trunc(d / 4), flashLife = Math.min(q + 4, 12), ringLife = d >= 26 ? trunc(flashLife * 5 / 8) : 0;
  for (let t = 0; t < 3; t++) {
    const cv = canvas(flashLife);
    for (let k = 0; k < flashLife; k++) {
      const life = flashLife - k, f = Math.min((life + 2) * 32, 255);
      const [c0, c1, c2] = table[t].map(c => scale(c, f));
      const qq = trunc(life * 192 / flashLife), radius = (depth * trunc(qq * 7 / 32)) >> 8, angle = rot + life * 64;
      star(cv, k * 96, k * 96 + 48, 48, 5, 0x1d0, angle, radius, c0, c1, c2);
      star(cv, k * 96, k * 96 + 48, 48, 5, 0x320, angle + 0x199, trunc(radius * 3 / 4), c0, c1, c2);
    }
    save(cv, `flash_${typeName[t]}_d${d}.png`); files.push(`flash_${typeName[t]}_d${d}.png`);
    if (ringLife) {
      const rc = canvas(ringLife);
      for (let k = 0; k < ringLife; k++) {
        const life = ringLife - k, f = Math.min(life * 14, 255);
        const c1 = scale(table[t][1], f), c2 = scale(table[t][2], f);
        ring(rc, k * 96, k * 96 + 48, 48, (life * 4) | 2, ((ringLife - (life - 1)) * 6) & 0xfffe, (k * 16) & 0xffff, tilt, c2, c1);
      }
      save(rc, `ring_${typeName[t]}_d${d}.png`); files.push(`ring_${typeName[t]}_d${d}.png`);
    }
  }
}
for (const size of [10, 20]) { // kind 2 spark disc, colour independent of the effect type
  const cv = canvas(size);
  for (let k = 0; k < size; k++) {
    const life = size - k, f = Math.min(life * 8 + 16, 255), grey = trunc(f * 99 / 256);
    const radius = (depth * trunc(trunc(((life >> 1) + 4) * 256 / size) * 5 / 32)) >> 8;
    ring(cv, k * 96, k * 96 + 48, 48, radius, 0, (k * 64) & 0xffff, tilt, [0, 0, 0], [grey, grey, grey]);
  }
  save(cv, `spark_disc_life${size}.png`); files.push(`spark_disc_life${size}.png`);
}

const json = {
  source: 'xena.exe (SLUS-00977) code; see docs/xena-hit-effects.md',
  checkedInstructions: checks.map(([a, h, w]) => ({ address: '0x' + a.toString(16).toUpperCase(), bytes: h, what: w })),
  stepRate: 'one update per game step (func_80076888 -> func_80028650); the port runs one step per 2 video frames',
  pool: { address: '0x800F1898', slots: 12, slotSize: 0x30, reuse: 'free slot, or a kind 2/3 slot with life < 5' },
  colourTable: { address: '0x800C0F4C', types: typeName.map((n, t) => ({ type: t, name: n, c0: table[t][0], c1: table[t][1], c2: table[t][2] })) },
  burst: {
    function: 'func_800291A0(pos, d, type)',
    flash: 'kind 0, life = min(trunc(d/4)+4, 12); two 5-point stars, radius R = w*trunc(trunc(life*192/size)*7/32)/256, second 3R/4 at +0x199; half-widths 0x1D0 / 0x320; sides at R/8; colour factor min((life+2)*32,255); tip c0, sides c1, centre c2; angle = tilt + 64*life',
    ring: 'kind 1, only d >= 26, life = trunc(flashLife*5/8); inner radius (size-life+1)*6 px, thickness (life*4)|2 px, not depth scaled; inner colour c1, outer c2, factor min(life*14,255); spin (frame & 0xFFF) << 4',
    sparks: 'kind 2, count clamp(trunc(d/4)-1, 0, 6); velocity rand(-18,18) per axis (world units/step), life rand(10,20); no gravity; every 2nd step each velocity component moves 1 toward 0 (|v| <= 1 stays); disc radius w*trunc(trunc(((life>>1)+4)*256/size)*5/32)/256; centre grey trunc(min(life*8+16,255)*99/256), edge black; type colour not used',
    blend: 'POLY_G4 semi-transparent, abr 1 (B + F, additive), depth w = 256 - SZ/32, OT z = SZ/32',
    killedWhen: 'D_800F0CF2 == 2 removes kinds 0 and 1 at once (meaning of D_800F0CF2 UNKNOWN)',
  },
  blood: {
    function: 'func_800B2F84 descriptor -> emitter pool D_800FF3A0 (20), drawn by func_800B33C8',
    primitive: 'LINE_G2 opaque (GP0 0x50): head (255,0,0) at the current position, tail (0,0,0) at the previous position; colours not faded',
    pool: 40, spawnEvery: '2 steps', perSpawn: '2 + trunc(rand*8) = 2..9', particleLife: '5..10 steps',
    speed: '5..19 units/step along the normalised direction', gravity: '+3 units/step^2 on y (down) while |vy| < 200', drag: 0,
    boxSpread: '+-15 units per axis around the emitter', followsVictim: 'emitter position = victim part (+0x12C) + offset taken at the hit',
    direction: 'dir = (v-100) + rand*(v+200) per axis, v = victim rotation applied to (0,-200,-500) Xena / (0,-80,-500) bandit / (0,-170,-500) bandit kill',
    emission: { xena: 8, bandit: 10, banditKill: 20 },
    colour: { default: [255, 0, 0], family2: [0, 128, 0] },
  },
  files,
};
fs.writeFileSync(path.join(out, 'hit_effects.json'), JSON.stringify(json, null, 2) + '\n');
console.log(JSON.stringify({ out, files: files.length, checks: checks.length }));
