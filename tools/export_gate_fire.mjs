#!/usr/bin/env node
// Export the burning wooden gate at the end of level 01 of Xena: Warrior Princess (SLUS-00977) for a
// Unity rebuild: exact-pixel PNG frames of the flame sprites (FIRE.STM, FIRE2.STM) and the smoke tile
// (SMOKE3.STM), plus a JSON with placement (level units and Unity metres), size, anchor, billboard rule,
// frame timing, blend mode and the smoke-emitter parameters. Optional --verify-ram checks the rules
// against native-port RAM dumps. Evidence and labels: docs/xena-gate-fire.md.
//
// Evidence chain (MIPS addresses in the SLUS-00977 executable):
//   * func_8006287C 0x800630E8..0x80063108: every record of the second LEVEL.BIN table (count +0x108,
//     offset +0x10C, 40-byte records: name[16], s32 x,y,z, s16 rx,ry,rz, s16 sx,sy,sz) is spawned by
//     func_8006FF38(record), which looks the name up in the class table 0x800C33E4 (0x44 bytes/class).
//   * class "fire" (0x15) / "fire2" (0x16): descriptor +0x40 = 2 -> func_80087A44 builds a sprite object;
//     size/semi/abr come from the per-name table D_800C1D1C (13 x 12 bytes) or D_800C1DB8, otherwise
//     record +0x22/+0x24 and semi = abr = 1 (0x80087B28..0x80087B54).
//   * func_800A7914 -> func_800A7A4C: a two-triangle TMD quad, vertices (-w/2,0) (-w/2,-h) (w/2,-h) (w/2,0):
//     the record position is the bottom centre. func_80071F9C -> func_80010000 draws it with the rotation
//     of D_800F0790 (identity) after transforming only the position by the camera: a camera-facing billboard.
//   * descriptor +0x24 -> animation script (func_80041538 / func_8004180C): 0x8008 n = frame delay n vblanks
//     (D_800F16C0 clock), 0x8002 n = jump to sequence n, other values = frame index. The interpreter writes
//     the UVs of the frame it held before the store (0x800418F8..0x80041A70), so the texture shown lags the
//     stored frame index by one script step.
//   * func_80027C60 (fire update): on the first update it runs the interpreter rand(0,8) times (random start
//     phase); no light, no sound, no damage code in it.
//   * smoke: classes smok_blk (0x1F) / smok_sml (0x1E), update func_8002845C -> particle emitter
//     func_800B3108 with the SMOKE3.STM tile; abr 2 (subtractive) for smok_blk, 1 (additive) for smok_sml.
//
//   node tools/export_gate_fire.mjs --root DECOMP --out DIR [--level 1] [--verify-ram DIR]
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i < 0 ? fallback : argv[i + 1]; };
const root = path.resolve(option('--root', '.'));
const out = path.resolve(option('--out', 'gate-fire-export'));
const level = Number(option('--level', '1'));
const here = path.dirname(fileURLToPath(import.meta.url));
const { png } = await import(pathToFileURL(path.join(here, 'lib', 'png.mjs')).href);
const { readStm } = await import(pathToFileURL(path.join(here, 'lib', 'xena_model.mjs')).href);
fs.mkdirSync(out, { recursive: true });
const hex = n => '0x' + (n >>> 0).toString(16).toUpperCase();

// ---------- executable ----------
const exe = fs.readFileSync(path.join(root, 'xena.exe'));
const base = exe.readUInt32LE(0x18);
const eo = a => a - base + 0x800;
const u32 = a => exe.readUInt32LE(eo(a)), i16 = a => exe.readInt16LE(eo(a)), u16 = a => exe.readUInt16LE(eo(a)), u8 = a => exe[eo(a)];
const inExe = a => a >= base && a < base + exe.length - 0x800;
const cstr = a => { const o = eo(a); return exe.subarray(o, exe.indexOf(0, o)).toString('latin1'); };
// Instruction immediates the JSON relies on (fail loudly if the executable differs).
const imm = (a, expectOp) => { const w = u32(a), op = w >>> 26; if (expectOp !== undefined && op !== expectOp) throw Error(`unexpected opcode at ${hex(a)}`); return (w << 16) >> 16; };
const ADDIU = 9, ORI = 13, LUI = 15;

const CLASS_TABLE = 0x800C33E4, CLASS_COUNT = 0xF9;
const classes = Array.from({ length: CLASS_COUNT }, (_, i) => { const d = CLASS_TABLE + i * 0x44;
  return { index: i, descriptor: d, name: cstr(u32(d)), flags: u32(d + 4), update: u32(d + 0x1C), anim: u32(d + 0x24),
    radius: i16(d + 0x28), renderType: u8(d + 0x40), sound: u32(d + 0x3C) }; });
const classByName = n => classes.find(c => c.name === n);

// Animation script: sequence table (descriptor +0x24) -> list of s16 words.
function decodeScript(table) {
  const seq0 = u32(table), words = []; let a = seq0;
  for (let k = 0; k < 64; k++, a += 2) {
    const w = u16(a); words.push(w);
    if (w === 0x8002) { words.push(u16(a + 2)); break; }
  }
  const ops = []; let delay = null; const frames = [];
  for (let k = 0; k < words.length; k++) {
    const w = words[k];
    if (w & 0x8000) {
      const op = w & 0x7FFF, arg = words[++k];
      ops.push({ op: hex(w), arg, meaning: op === 8 ? `frame delay = ${arg} vblanks (actor +0xE0)` : op === 2 ? `jump to sequence ${arg} (table[${arg}])` : 'UNKNOWN' });
      if (op === 8) delay = arg;
    } else { ops.push({ frame: w }); frames.push(w); }
  }
  return { table: hex(table), sequence0: hex(seq0), words: words.map(hex), ops, delayVblanks: delay, frames };
}

// Sprite size override tables (func_80087A44): {char *name; s16 w, h; u8 semi, abr}.
const sizeTable = (addr, n) => Array.from({ length: n }, (_, i) => { const a = addr + 12 * i, p = u32(a);
  return { name: inExe(p) ? cstr(p) : null, w: i16(a + 4), h: i16(a + 6), semi: u8(a + 8), abr: u8(a + 9) }; });
const SIZE_SHARED = sizeTable(0x800C1D1C, 13), SIZE_LEVEL = sizeTable(0x800C1DB8, 6);
const spriteParams = (name, rec) => {
  const t = SIZE_SHARED.find(e => e.name === name) || SIZE_LEVEL.find(e => e.name === name);
  return t ? { w: t.w, h: t.h, semi: t.semi, abr: t.abr, source: t === SIZE_SHARED.find(e => e.name === name) ? 'D_800C1D1C (shared STM, DATA\\LEVELS\\%s.STM)' : 'D_800C1DB8' }
    : { w: rec.scale[0], h: rec.scale[1], semi: 1, abr: 1, source: 'LEVEL.BIN record +0x22/+0x24, defaults semi=1 abr=1 (0x80087B28..0x80087B54); STM DATA\\LEVELS\\%02d\\%s.STM' };
};

// ---------- disc ----------
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'build/asset-audit/manifest.json')));
const uda = fs.readFileSync(path.join(root, 'disc/files/XENA.UDA'));
function resource(name) {
  const hits = manifest.entries.filter(e => e.candidates.some(c => c.name.toUpperCase() === name.toUpperCase()));
  if (hits.length !== 1 || !hits[0].inArchive) throw Error(`Missing or ambiguous ${name}`);
  const at = (hits[0].lba - manifest.summary.archiveLba) * 2048;
  return { name, lba: hits[0].lba, size: hits[0].size, bytes: uda.subarray(at, at + hits[0].size) };
}
const lv = String(level).padStart(2, '0');
const levelBin = resource(`DATA\\LEVELS\\${lv}\\LEVEL.BIN`);
function readTable(b, countAt, offAt) {
  const count = b.readUInt32LE(countAt), off = b.readUInt32LE(offAt);
  return Array.from({ length: count }, (_, k) => { const o = off + k * 40;
    return { index: k, offset: hex(o), name: b.subarray(o, o + 16).toString('latin1').replace(/\0.*$/s, ''), flag12: b[o + 12],
      position: [16, 20, 24].map(n => b.readInt32LE(o + n)), rotation: [28, 30, 32].map(n => b.readInt16LE(o + n)),
      scale: [34, 36, 38].map(n => b.readInt16LE(o + n)) }; });
}
const statics = readTable(levelBin.bytes, 0x100, 0x104), effects = readTable(levelBin.bytes, 0x108, 0x10C);
const toUnity = p => [p[0] / 400, -p[1] / 400, p[2] / 400].map(v => Math.round(v * 10000) / 10000);

// ---------- textures ----------
function rgb8(c) { return [0, 5, 10].map(n => (((c >>> n) & 31) << 3) | (((c >>> n) & 31) >>> 2)); }
function decode(img) {
  const k = [4, 2, 1][img.depth], w = img.image.width * k, h = img.image.height, words = new Uint16Array(w * h);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const word = img.image.data.readUInt16LE((y * img.image.width + Math.floor(x / k)) * 2);
    words[y * w + x] = img.depth === 0 ? img.clut.data.readUInt16LE(((word >>> ((x & 3) * 4)) & 15) * 2)
      : img.depth === 1 ? img.clut.data.readUInt16LE(((word >>> ((x & 1) * 8)) & 255) * 2) : word;
  }
  const rgba = new Uint8Array(w * h * 4);
  for (let i = 0; i < w * h; i++) rgba.set([...rgb8(words[i]), words[i] === 0 ? 0 : 255], i * 4);
  return { w, h, words, rgba };
}
function writePng(rel, d) { fs.mkdirSync(path.dirname(path.join(out, rel)), { recursive: true }); fs.writeFileSync(path.join(out, rel), png(d.w, d.h, d.rgba)); return rel; }
function strip(tiles) { // horizontal atlas, same height
  const w = tiles.reduce((s, t) => s + t.w, 0), h = Math.max(...tiles.map(t => t.h)), rgba = new Uint8Array(w * h * 4); let x0 = 0;
  for (const t of tiles) { for (let y = 0; y < t.h; y++) rgba.set(t.rgba.subarray(y * t.w * 4, (y + 1) * t.w * 4), (y * w + x0) * 4); x0 += t.w; }
  return { w, h, rgba };
}
function exportStm(file, prefix) {
  const res = resource(file), stm = readStm(res.bytes);
  const tiles = stm.images.map((img, i) => { const d = decode(img);
    const clut = img.clut ? Array.from({ length: img.clut.width }, (_, k) => img.clut.data.readUInt16LE(2 * k)) : [];
    const used = new Set(d.words.filter(c => c));
    return { index: i, name: img.name, depthBits: [4, 8, 16][img.depth], size: [d.w, d.h], fileVram: [img.image.x, img.image.y], fileClut: img.clut ? [img.clut.x, img.clut.y] : null,
      fileClutStpEntries: clut.filter(c => c & 0x8000).length, fileClutNonZero: clut.filter(c => c).length,
      usedColoursWithoutStp: [...used].filter(c => !(c & 0x8000)).length, opaqueTexels: d.words.filter(c => c).length, png: writePng(`${prefix}/${prefix}_${String(i).padStart(2, '0')}.png`, d), d }; });
  const atlas = writePng(`${prefix}/${prefix}_strip.png`, strip(tiles.map(t => t.d)));
  return { source: res.name, lba: res.lba, bytes: res.size, atlas, tiles };
}
const fire = exportStm('DATA\\LEVELS\\FIRE.STM', 'fire');
const fire2 = exportStm(`DATA\\LEVELS\\${lv}\\FIRE2.STM`, 'fire2');
const smoke = exportStm('DATA\\LEVELS\\SMOKE3.STM', 'smoke3');

// ---------- classes / scripts ----------
const C = { fire: classByName('fire'), fire2: classByName('fire2'), smok_blk: classByName('smok_blk'), smok_sml: classByName('smok_sml') };
if (C.fire.update !== 0x80027C60 || C.fire2.update !== 0x80027C60 || C.smok_blk.update !== 0x8002845C) throw Error('class table differs from the audited executable');
const scripts = { fire: decodeScript(C.fire.anim), fire2: decodeScript(C.fire2.anim) };

// smoke emitter descriptor built on the stack by func_8002845C (immediates read back from the code)
const gravWord = ((imm(0x80028494, LUI) << 16) | (imm(0x80028498, ORI) & 0xFFFF)) >>> 0;
const smokeEmitter = {
  constructor: 'func_8002845C -> func_800B3108(desc) -> func_800B192C; follows the actor every update (func_800B3378); flags +0x90 |= 0xE00, &= ~1',
  texture: 'SMOKE3.STM tile 0 (D_800F25E4 = preloaded shared STM #0 of D_800C1D1C), 64x64 4-bit',
  slots: imm(0x800284A0, ADDIU),
  spawnPosition: `actor position (record y + ${imm(0x800284A8, ADDIU)} at creation, then the actor position each update)`,
  emissionSteps: imm(0x800284E0, ADDIU),
  spawnDelay: { base: imm(0x800284E8, ADDIU), range: imm(0x800284E8, ADDIU), fields: 'desc+0x12 / +0x14 (same register value 5)' },
  perSpawn: { base: imm(0x800284F4, ADDIU), range: imm(0x800284FC, ADDIU), note: 'base + trunc(rand*range) = 2 particles per spawn' },
  spawnBox: { x: imm(0x80028504, ADDIU), y: 0, z: imm(0x80028504, ADDIU), note: 'desc+0x34/+0x38/+0x3C: x and z spread 400 (about +-200), y 0' },
  direction: 'desc+0x1C..+0x30 = 0 (no initial direction)',
  speed: { base: imm(0x80028510, ADDIU), range: imm(0x80028518, ADDIU) },
  life: { base: imm(0x800284C8, ADDIU), range: imm(0x80028520, ADDIU), note: 'life = 50 + trunc(rand*60) game steps' },
  gravityPerStep: (gravWord >> 16) << 16 >> 16, word48: hex(gravWord), velocityLimit: imm(0x800284C8, ADDIU),
  field50: imm(0x80028528, ADDIU), field74: imm(0x8002859C, ADDIU),
  abr: { smok_sml: imm(0x80028590, ADDIU), smok_blk: imm(0x8002858C, ADDIU), rule: `class ${hex(imm(0x80028584, ADDIU))} (smok_sml) -> 1, otherwise 2; GetTPage(abr = desc+0x70) and SetSemiTrans(desc+0x70 != -1) in func_800B192C` },
  drawDistance: 'classes 0x1C/0x1E/0x1F use the shorter far limit t8 = near + 3/4*(far-near) of D_800F10F4+0x104/+0x108 (12276 in the capture) instead of camera +0x6C (16108)',
  quadSize: 'UNKNOWN (textured particle draw path not decoded)',
};

// ---------- placements ----------
const fireLike = new Set(['fire', 'fire2', 'smok_blk', 'smok_sml', 'splash']);
const gateDoors = statics.filter(s => /^@2doors/.test(s.name));
const gateCentre = gateDoors.length ? [0, 1, 2].map(k => Math.round(gateDoors.reduce((s, d) => s + d.position[k], 0) / gateDoors.length)) : null;
const dist = (a, b) => Math.hypot(a[0] - b[0], a[2] - b[2]);
const placements = effects.map(r => {
  const cls = classByName(r.name); const e = { ...r, unity: toUnity(r.position), class: cls ? { index: hex(cls.index), update: hex(cls.update), renderType: cls.renderType } : null,
    distanceToGateXZ: gateCentre ? Math.round(dist(r.position, gateCentre)) : null };
  if (r.name === 'fire' || r.name === 'fire2') {
    const sp = spriteParams(r.name, r);
    e.sprite = { width: sp.w, height: sp.h, semiTransparent: !!sp.semi, abr: sp.abr, sizeSource: sp.source,
      anchor: 'bottom centre at the record position (quad y from 0 up to -h)', topCentre: [r.position[0], r.position[1] - sp.h, r.position[2]],
      unityHeightMetres: sp.h / 400, unityWidthMetres: sp.w / 400, recordScaleIgnored: sp.source.startsWith('D_800C1D1C') ? r.scale : undefined };
  }
  return e;
});
const gate = placements.filter(p => p.distanceToGateXZ !== null && p.distanceToGateXZ < 1500);

// ---------- JSON ----------
const json = {
  source: `Xena: Warrior Princess (SLUS-00977), level ${lv} burning gate; see docs/xena-gate-fire.md`,
  units: { level: 'PS1 world units, y down (larger y = lower)', unity: 'x/400, -y/400, z/400 metres (project convention 400 u = 1 m)' },
  gate: { doors: gateDoors.map(d => ({ name: d.name, position: d.position, unity: toUnity(d.position), rotation: d.rotation, note: 'LEVEL.BIN static TMD objects; no door class (see docs/xena-level-scripts.md)' })),
    centre: gateCentre, unityCentre: gateCentre && toUnity(gateCentre) },
  whatBurns: 'Five "fire" sprite actors and one "smok_blk" smoke emitter from the LEVEL.BIN effect table placed in and behind the gate; nothing animates the gate texture, there is no light source or sound in these classes.',
  levelBin: { source: levelBin.name, lba: levelBin.lba, effectTable: { countAt: '0x108', offsetAt: '0x10C', recordBytes: 40, count: effects.length,
    layout: 'name[16], u8 +0x0C = 1 (unused here), s32 x,y,z (+0x10), s16 rx,ry,rz (+0x1C), s16 sx,sy,sz (+0x22)', spawnedBy: 'func_8006287C 0x800630E8 -> func_8006FF38(record)' } },
  gatePlacements: gate.map(p => p.index),
  placements,
  classes: Object.fromEntries(Object.entries(C).map(([k, c]) => [k, { index: hex(c.index), descriptor: hex(c.descriptor), flags: hex(c.flags), update: hex(c.update),
    anim: c.anim ? hex(c.anim) : null, radius28: c.radius, renderType40: c.renderType, soundList3C: c.sound ? hex(c.sound) : null }])),
  sprites: {
    fire: { texture: { source: fire.source, lba: fire.lba, atlas: fire.atlas, frames: fire.tiles.map(({ d, ...t }) => t) }, script: scripts.fire },
    fire2: { texture: { source: fire2.source, lba: fire2.lba, atlas: fire2.atlas, frames: fire2.tiles.map(({ d, ...t }) => t) }, script: scripts.fire2 },
    sizeTables: { D_800C1D1C: SIZE_SHARED, D_800C1DB8: SIZE_LEVEL },
  },
  render: {
    geometry: 'TMD template D_800DA7C0: 4 vertices (-w/2,0,0) (-w/2,-h,0) (w/2,-h,0) (w/2,0,0), two textured triangles (mode 0x34, 0x36 when semi-transparent), the whole STM frame mapped on the quad (u0..u0+w-1, v0..v0+h-1)',
    billboard: 'func_80071F9C -> func_80010000: the position is transformed by the camera matrix and the quad is drawn with the identity rotation D_800F0790 in view space, so it always faces the screen (cylindrical in y is NOT applied: it is screen-aligned)',
    blend: 'semi-transparent, abr 1: framebuffer + texel (additive, per channel clamped to 31). The file CLUTs already have STP on every non-zero entry (frames[].fileClutStpEntries; the STM loader mode 0 also sets it, docs/xena-main-menu.md), so every visible texel is added; texel 0x0000 is not drawn. In Unity: additive shader (One One), PNG alpha only marks texel 0x0000.',
    tpageInCapture: 'fire 0x002E (4-bit, abr 1), fire2 0x00A8 (8-bit, abr 1) - read from the prim packets in RAM',
    shading: 'TMD prim flag 0 (light calculation on); the vertex colour that modulates the texture was not measured (UNKNOWN)',
    visibility: 'actor +0x144: bit 0x40 = view depth inside (D_800EE184 = 10, camera +0x6C = 16108), bit 0x01 = inside the view cone using radius descriptor +0x28 = 300 (func_80071850); fire/fire2 are drawn only when both bits are set',
  },
  timing: {
    clock: 'D_800F16C0, +1 per vblank (60 Hz NTSC); the game step runs every 2 vblanks in the port captures',
    fire: `frame f shown for ${scripts.fire.delayVblanks} vblanks; ${scripts.fire.frames.length} frames 0..${scripts.fire.frames.length - 1}, loop; period ${scripts.fire.frames.length * scripts.fire.delayVblanks} vblanks = ${(scripts.fire.frames.length * scripts.fire.delayVblanks / 60).toFixed(3)} s`,
    fire2: `delay ${scripts.fire2.delayVblanks} vblanks; with 2-vblank steps each frame lasts 4 vblanks (2 steps); ${scripts.fire2.frames.length} frames, loop`,
    rule: 'a new frame is taken on the first game step with clock >= next (actor +0xDC); next = clock + delay. With 2-vblank steps a 3-vblank delay therefore lasts 4 vblanks.',
    displayLag: 'the UVs written in a step are those of the frame index held before the step, so the visible tile is script frame (stored - 1); cyclic order unchanged',
    startPhase: 'func_80027C60 first update: runs the interpreter rand(0,8) times (func_800B4350(0,8)) with next forced to now, so each fire starts at a random frame (the five gate fires showed frames 12/11/9/6/8 at frame 9400 of the capture)',
    interpolation: 'none: frame flips, positions static',
  },
  smoke: { classes: ['smok_blk (0x1F, abr 2 subtractive = dark smoke)', 'smok_sml (0x1E, abr 1 additive)'], emitter: smokeEmitter,
    texture: { source: smoke.source, lba: smoke.lba, frames: smoke.tiles.map(({ d, ...t }) => t) } },
  lightAndSound: 'func_80027C60 and func_8002845C call no light or sound routine; fire/fire2/smok descriptors have no sound list (+0x3C = 0); the fire scripts use only opcodes 0x8008/0x8002 (the interpreter sound opcode is unused). Gate geometry lighting is the static per-vertex LEVEL.LGT (not dynamic). Ambient level audio (if any) was not traced.',
};

// ---------- verification against native RAM dumps ----------
const ramDir = option('--verify-ram');
if (ramDir) {
  const files = fs.readdirSync(ramDir).filter(f => /^ram_\d+\.bin$/.test(f)).sort();
  const checks = { placementsMatched: 0, spriteParams: 0, spriteParamFails: [], uvLag: 0, uvFails: [], abrFromTpage: 0, timingActors: 0, timingFails: [], smoke: [] };
  const series = new Map();
  for (const [fi, f] of files.entries()) {
    const m = fs.readFileSync(path.join(ramDir, f)), frame = +f.match(/\d+/)[0];
    const r32 = a => m.readUInt32LE(a & 0x1FFFFF), i32 = a => m.readInt32LE(a & 0x1FFFFF), r16 = a => m.readUInt16LE(a & 0x1FFFFF), s16 = a => m.readInt16LE(a & 0x1FFFFF), r8 = a => m[a & 0x1FFFFF];
    const name = a => { a &= 0x1FFFFF; return m.subarray(a, m.indexOf(0, a)).toString('latin1'); };
    const clock = r32(0x800F16C0);
    for (let p = r32(0x800F1320), n = 0; p && n < 600; p = r32(p + 8), n++) {
      const nm = name(r32(r32(p + 12))); if (!fireLike.has(nm)) continue;
      const origin = [0, 4, 8].map(k => i32(p + 0x98 + k));
      const rec = effects.find(e => e.name === nm && e.position.every((v, k) => v === origin[k]));
      if (fi === 0 && rec) checks.placementsMatched++;
      if (nm === 'fire' || nm === 'fire2') {
        const so = r32(p + 0xA4), sp = spriteParams(nm, rec), fr = r16(so + 0x1A);
        const ok = s16(so + 0x14) === sp.w && s16(so + 0x16) === sp.h && r8(so + 0x18) === sp.semi && r8(so + 0x19) === sp.abr;
        if (ok) checks.spriteParams++; else checks.spriteParamFails.push({ frame, actor: hex(p), nm });
        const descs = r32(r32(so + 0x24)), cnt = r16(r32(so + 0x24) + 4), pk = r32(r32(so) + 0x40);
        const tile = i => { const d = descs + i * 24, k = [4, 2, 1][s16(d + 12)]; return [(s16(d) & 63) * k, r8(d + 2)]; };
        const shown = (fr + cnt - 1) % cnt, [u, v] = tile(shown);
        if (r8(pk + 8) === u && r8(pk + 9) === v) checks.uvLag++; else checks.uvFails.push({ frame, actor: hex(p), stored: fr, prim: [r8(pk + 8), r8(pk + 9)] });
        if (((r16(pk + 10) >> 5) & 3) === sp.abr) checks.abrFromTpage++;
        if (!series.has(p)) series.set(p, { nm, pts: [] });
        series.get(p).pts.push({ clock, fr, cnt });
      }
      if (nm === 'smok_blk' || nm === 'smok_sml') { const e = r32(p + 0x104);
        if (e && fi === files.length - 1) checks.smoke.push({ actor: hex(p), nm, emitter: hex(e), slots: i32(e + 0x54), emission: i32(e + 0x50), gravity: i32(e + 0x5C), abr: i32(e + 0x70), pos: [0, 4, 8].map(k => i32(e + k)) }); }
    }
  }
  // timing: frames advance once per `delay` (rounded up to whole 2-vblank steps) with a fixed phase
  for (const [p, s] of series) {
    const delay = s.nm === 'fire' ? scripts.fire.delayVblanks : scripts.fire2.delayVblanks, hold = Math.ceil(delay / 2) * 2;
    let okPhase = null;
    for (let c0 = 0; c0 < hold && okPhase === null; c0++) {
      const good = s.pts.slice(1).every((q, i) => { const a = s.pts[i]; const crossings = Math.floor((q.clock - c0) / hold) - Math.floor((a.clock - c0) / hold);
        return ((q.fr - a.fr + q.cnt) % q.cnt) === crossings % q.cnt; });
      if (good) okPhase = c0;
    }
    if (okPhase !== null) checks.timingActors++; else checks.timingFails.push({ actor: hex(p), nm: s.nm, pts: s.pts.map(q => [q.clock, q.fr]) });
  }
  json.verification = { ramDumps: files.length, ...checks, spriteActors: series.size };
  console.log(JSON.stringify(json.verification, null, 1));
}
fs.writeFileSync(path.join(out, 'gate_fire.json'), JSON.stringify(json, null, 1));
console.log(`wrote ${out}: fire ${fire.tiles.length} frames, fire2 ${fire2.tiles.length} frames, ${gate.length} effect records within 1500 of the gate`);
