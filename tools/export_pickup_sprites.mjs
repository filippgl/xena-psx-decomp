#!/usr/bin/env node
// Export the billboard sprites used by level pickups (health2, scroll, up_sword, ...) exactly as the
// game builds them, with optional checks against the native RAM/VRAM snapshot.
//
// Evidence chain (SLUS-00977, see docs/xena-ambient-audit.md):
//   * class descriptor +0x40 == 2 -> actor create func_8006FF38 calls func_80087A44 (sprite object)
//     instead of the TMD loader func_80069C5C (+0x40 == 1). No TMD exists for these classes.
//   * func_80087A44 looks the class name up in the 13-entry table D_800C1D1C (12 bytes:
//     name*, s16 width, s16 height, u8 semiTrans, u8 mode) and uses the STM image list preloaded by
//     func_8006276C ("DATA\LEVELS\%s.STM"; mode 0xFF -> STM load flag 2).
//   * func_800A7914 instantiates the 2-triangle template TMD at 0x800DA7C0 and func_800A7A4C sets the
//     quad to x = -w/2..+w/2, y = 0..-h, z = 0 (pivot at the bottom centre, PSX y is down).
//   * func_80087FE0(obj, 0) maps the whole STM image (u0..u0+w-1, v0..v0+h-1), tpage abr = mode & 3,
//     SetSemiTrans on both triangles when semiTrans != 0. Only frame 0 is ever selected.
//   * func_80071F9C draws it: translation = actor position (+h/2 if obj+0x20), then func_80010000 /
//     func_80014FD0 transform only the translation by the camera and draw the vertices with the
//     identity rotation D_800F0790 -> screen-aligned billboard.
//
//   node tools/export_pickup_sprites.mjs --root DECOMP --out DIR [--names health2,scroll,up_sword]
//        [--ram ram_009200.bin] [--vram vram_009200.bmp]
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i < 0 ? fallback : argv[i + 1]; };
const root = path.resolve(option('--root', '.'));
const out = path.resolve(option('--out', 'pickup-sprites'));
const names = option('--names', 'health2,scroll,up_sword').split(',').map(s => s.trim().toLowerCase());
const lib = name => import(pathToFileURL(path.join(root, 'tools', 'lib', name)).href);
const { readStm, color555 } = await lib('xena_model.mjs');
const { png } = await lib('png.mjs');

const exe = fs.readFileSync(path.join(root, 'xena.exe'));
const base = exe.readUInt32LE(0x18);
const eo = a => a - base + 0x800;
const cstr = a => { let o = eo(a), e = o; while (exe[e]) e++; return exe.toString('latin1', o, e); };
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'build/asset-audit/manifest.json')));
const uda = fs.readFileSync(path.join(root, 'disc/files/XENA.UDA'));
function resource(name) {
  const hits = manifest.entries.filter(e => e.candidates.some(c => c.name === name));
  if (hits.length !== 1 || !hits[0].inArchive) throw Error(`Missing or ambiguous ${name}`);
  const at = (hits[0].lba - manifest.summary.archiveLba) * 2048;
  return { name, lba: hits[0].lba, bytes: uda.subarray(at, at + hits[0].size) };
}

const CLASS_TABLE = 0x800C33E4, CLASS_COUNT = 249, SPRITE_TABLE = 0x800C1D1C, SPRITE_COUNT = 13;
const classIndex = {};
for (let i = 0; i < CLASS_COUNT; i++) {
  const p = exe.readUInt32LE(eo(CLASS_TABLE + i * 0x44));
  if (p) classIndex[cstr(p).toLowerCase()] = i;
}
const table = Array.from({ length: SPRITE_COUNT }, (_, i) => {
  const a = SPRITE_TABLE + i * 12;
  return { address: a, name: cstr(exe.readUInt32LE(eo(a))), width: exe.readInt16LE(eo(a + 4)), height: exe.readInt16LE(eo(a + 6)),
    semiTransparent: exe[eo(a + 8)], mode: exe[eo(a + 9)] };
});

let ram = null, vram = null;
if (option('--ram')) ram = fs.readFileSync(option('--ram'));
if (option('--vram')) {
  const b = fs.readFileSync(option('--vram'));
  const at = b.readUInt32LE(10), w = b.readInt32LE(18), h = b.readInt32LE(22), bpp = b.readUInt16LE(28);
  if (w !== 1024 || Math.abs(h) !== 512 || bpp !== 24) throw Error('Expected 1024x512x24 VRAM BMP');
  const stride = w * 3;
  vram = (x, y) => { const row = h > 0 ? 511 - y : y, o = at + row * stride + x * 3; return [b[o + 2] >> 3, b[o + 1] >> 3, b[o] >> 3]; };
}
const r32 = a => ram.readUInt32LE(a & 0x1FFFFF), r16 = a => ram.readInt16LE(a & 0x1FFFFF), ru16 = a => ram.readUInt16LE(a & 0x1FFFFF), ru8 = a => ram[a & 0x1FFFFF];

fs.mkdirSync(out, { recursive: true });
const sprites = [];
for (const name of names) {
  const entry = table.find(t => t.name.toLowerCase() === name);
  if (!entry) throw Error(`${name} is not in the sprite table D_800C1D1C`);
  const cls = classIndex[name];
  const desc = cls === undefined ? null : CLASS_TABLE + cls * 0x44;
  const createMode = desc ? exe[eo(desc + 0x40)] : null;
  const res = resource(`DATA\\LEVELS\\${name.toUpperCase()}.STM`);
  const stm = readStm(res.bytes);
  const images = stm.images.map((img, index) => {
    const depthBits = [4, 8, 16][img.depth], texelsPerWord = 16 / depthBits;
    const tw = img.image.width * texelsPerWord, th = img.image.height;
    const rgba = new Uint8Array(tw * th * 4);
    let transparent = 0, stp = 0, opaque = 0;
    for (let y = 0; y < th; y++) for (let x = 0; x < tw; x++) {
      const word = img.image.data.readUInt16LE((y * img.image.width + Math.floor(x / texelsPerWord)) * 2);
      let c = word;
      if (img.depth < 2) {
        const idx = img.depth === 0 ? (word >>> ((x & 3) * 4)) & 15 : (word >>> ((x & 1) * 8)) & 255;
        c = img.clut.data.readUInt16LE(idx * 2);
      }
      const px = color555(c);
      // PSX: 0x0000 is never drawn; STP (bit 15) texels are blended with the abr mode; others opaque.
      if (c === 0) { px[3] = 0; transparent++; } else if (c & 0x8000) { px[3] = 255; stp++; } else opaque++;
      rgba.set(px, (y * tw + x) * 4);
    }
    const file = `${name}${stm.images.length > 1 ? '-' + index : ''}.png`;
    fs.writeFileSync(path.join(out, file), png(tw, th, rgba));
    // Separate mask: white where the texel has STP set (blended), so a material can reproduce it.
    const mask = new Uint8Array(tw * th * 4);
    for (let y = 0; y < th; y++) for (let x = 0; x < tw; x++) {
      const word = img.image.data.readUInt16LE((y * img.image.width + Math.floor(x / texelsPerWord)) * 2);
      let c = word;
      if (img.depth < 2) { const idx = img.depth === 0 ? (word >>> ((x & 3) * 4)) & 15 : (word >>> ((x & 1) * 8)) & 255; c = img.clut.data.readUInt16LE(idx * 2); }
      const v = c & 0x8000 ? 255 : 0; mask.set([v, v, v, 255], (y * tw + x) * 4);
    }
    const maskFile = file.replace('.png', '-stp.png');
    fs.writeFileSync(path.join(out, maskFile), png(tw, th, mask));
    let vramCheck = null;
    if (vram) {
      let compared = 0, different = 0;
      for (const block of [img.clut, img.image].filter(Boolean)) for (let y = 0; y < block.height; y++) for (let x = 0; x < block.width; x++) {
        const w = block.data.readUInt16LE((y * block.width + x) * 2), v = vram(block.x + x, block.y + y);
        compared++; if (v[0] !== (w & 31) || v[1] !== ((w >> 5) & 31) || v[2] !== ((w >> 10) & 31)) different++;
      }
      vramCheck = { comparedWords: compared, differentWords: different };
    }
    return { index, stmName: img.name, depthBits, texels: [tw, th], vramRect: [img.image.x, img.image.y, img.image.width, img.image.height],
      clut: img.clut ? [img.clut.x, img.clut.y] : null, png: file, stpMask: maskFile, texelCounts: { transparent, semiTransparentStp: stp, opaque }, vramCheck };
  });
  const sprite = {
    name, classIndex: cls ?? null, descriptor: desc ? `0x${desc.toString(16).toUpperCase()}` : null, createMode,
    spriteTableEntry: `0x${entry.address.toString(16).toUpperCase()}`,
    source: res.name, lba: res.lba,
    quad: { width: entry.width, height: entry.height, widthMeters: entry.width / 400, heightMeters: entry.height / 400,
      vertices: [[-(entry.width >> 1), 0, 0], [-(entry.width >> 1), -entry.height, 0], [entry.width >> 1, -entry.height, 0], [entry.width >> 1, 0, 0]],
      pivot: 'bottom centre at the actor position (PSX y down; vertices y = 0..-height)', verticalOffset: 0 },
    billboard: 'screen-aligned: view-space identity rotation (D_800F0790), translation = camera * position',
    blending: { semiTransparent: !!entry.semiTransparent, abr: entry.mode & 3,
      abrMeaning: ['0.5*B + 0.5*F', 'B + F', 'B - F', 'B + 0.25*F'][entry.mode & 3],
      appliesTo: 'texels with STP bit set only; 0x0000 texels transparent; other texels opaque',
      stmLoadFlag: entry.mode === 0xFF ? 2 : 0 },
    animation: { frames: 1, frameSelected: 0, spin: false, bob: false,
      evidence: 'func_80087FE0 called once with frame 0 (0x80087F18); func_8003FBA0 moves nothing for classes 0x5C..0x62' },
    images,
  };
  if (ram) {
    const live = [];
    for (let i = 0; i < 60; i++) {
      const p = r32(0x800F3748 + i * 4); if (!p || ru16(p + 0x1C) !== cls) continue;
      const obj = r32(p), prim = r32(obj + 0x40), vp = r32(obj + 0x3C), img = r32(r32(p + 0x24));
      const tp = ru16(prim + 0xA), clut = ru16(prim + 6);
      const verts = [0, 1, 2, 3].map(k => [r16(vp + k * 16), r16(vp + k * 16 + 2), r16(vp + k * 16 + 4)]);
      const rec = [0, 2, 4, 6, 8, 10, 12].map(k => ru16(img + k));
      const first = images[0];
      live.push({ object: `0x${p.toString(16).toUpperCase()}`, position: [r32(p + 8) | 0, r32(p + 12) | 0, r32(p + 16) | 0],
        verticesMatch: JSON.stringify(verts) === JSON.stringify(sprite.quad.vertices),
        imageRecord: rec, imageMatchesStm: rec[0] === first.vramRect[0] && rec[1] === first.vramRect[1] && rec[3] === first.texels[1] && rec[4] === first.clut?.[0] && rec[5] === first.clut?.[1],
        tpage: { abr: (tp >> 5) & 3, depth: (tp >> 7) & 3, x: (tp & 15) * 64, y: (tp & 16) * 16 }, clut: [(clut & 63) * 16, clut >> 6],
        semiTransparentCode: !!(ru8(prim + 3) & 2), uv: [[ru8(prim + 4), ru8(prim + 5)], [ru8(prim + 8), ru8(prim + 9)], [ru8(prim + 12), ru8(prim + 13)]],
        offsetHalfHeight: ru8(p + 0x20), frame: ru16(p + 0x1A) });
    }
    sprite.ramCheck = live;
  }
  sprites.push(sprite);
}
const report = { tool: 'export_pickup_sprites.mjs', unitsPerMeter: 400, spriteTable: table, sprites };
fs.writeFileSync(path.join(out, 'pickup-sprites.json'), JSON.stringify(report, null, 1) + '\n');
console.log(JSON.stringify(sprites.map(s => ({ name: s.name, quad: [s.quad.width, s.quad.height], abr: s.blending.abr,
  images: s.images.map(i => ({ texels: i.texels, depth: i.depthBits, counts: i.texelCounts, vram: i.vramCheck })),
  ram: s.ramCheck?.map(l => ({ verticesMatch: l.verticesMatch, imageMatchesStm: l.imageMatchesStm, abr: l.tpage.abr, semi: l.semiTransparentCode, frame: l.frame })) }))));
