#!/usr/bin/env node
// Decode SMA clips that contain type-2 "vertex deformation" commands (LARK.SMA, BFLY1.SMA), which
// tools/lib/xena_animation.mjs rejects. Mirrors func_80069040 (SLUS-00977), normal playback
// (context+0x2A == 0) only:
//   type 2 word: bits 0-2 = 2, bit 3 = packed 5:5:5 deltas, bit 6 = absolute (set, else add),
//   bit 7 = byte form, bits 8-15 = mesh (TMD object) index. Next s16 = record count. Records:
//     packed (0x08): [s16 index unless next word has bit 15 set -> index = previous + 1]
//                    u16 d = (dx: bits 10-14, dy: bits 5-9, dz: bits 0-4, each 5-bit signed)
//     bytes  (0x80): u8 index delta (cumulative from 0), s8 dx, s8 dy, s8 dz
//     words  (else): s16 index, s16 dx, s16 dy, s16 dz
//   The deltas are applied to the runtime copy of the mesh vertices (mesh+0x3C, 16-byte stride) and
//   persist across frames and loops (nothing resets them).
// Types 1/5/6/7 are decoded exactly as in tools/lib/xena_animation.mjs (copied, read-only parity).
//
//   node tools/decode_sma_vertex.mjs --root DECOMP --tmd 'DATA\LEVELS\01\LARK.TMD'
//        --smi 'DATA\LEVELS\01\LARK.SMI' --sma 'DATA\LEVELS\01\LARK.SMA' --out FILE.json
//        [--ram RAM.bin --actor 0x8014EF00]   (compare an animating actor's live vertices)
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i < 0 ? fallback : argv[i + 1]; };
const root = path.resolve(option('--root', '.'));
const lib = name => import(pathToFileURL(path.join(root, 'tools', 'lib', name)).href);
const { readTmd } = await lib('xena_model.mjs');
const { readSmi } = await lib('xena_animation.mjs');

const manifest = JSON.parse(fs.readFileSync(path.join(root, 'build/asset-audit/manifest.json')));
const uda = fs.readFileSync(path.join(root, 'disc/files/XENA.UDA'));
function resource(name) {
  const hits = manifest.entries.filter(e => e.candidates.some(c => c.name === name));
  if (hits.length !== 1 || !hits[0].inArchive) throw Error(`Missing or ambiguous ${name}`);
  const at = (hits[0].lba - manifest.summary.archiveLba) * 2048;
  return uda.subarray(at, at + hits[0].size);
}
const signed = (value, bits) => (value << (32 - bits)) >> (32 - bits);
const short = n => signed(n, 16);

export function decodeSmaWithVertices(bytes, initialParts, meshes) {
  if (bytes.length < 588 || bytes.readUInt32LE(0) !== 146) throw Error('Unsupported SMA slot table');
  const offsets = Array.from({ length: 146 }, (_, i) => bytes.readUInt32LE(4 + i * 4));
  const clips = [];
  for (let slot = 0; slot < offsets.length; slot++) {
    if (!offsets[slot]) continue;
    const start = offsets[slot] + 4;
    const limit = Math.min(bytes.length, ...offsets.filter(o => o + 4 > start).map(o => o + 4));
    let cursor = start;
    const need = n => { if (cursor + n > limit) throw Error(`SMA slot ${slot}: truncated at ${cursor}`); };
    const u16 = () => { need(2); const n = bytes.readUInt16LE(cursor); cursor += 2; return n; };
    const i16 = () => signed(u16(), 16);
    const u8 = () => { need(1); return bytes[cursor++]; };
    const i8 = () => signed(u8(), 8);
    const magic = u16(), version = u16(), frameCount = u16(), reserved = u16();
    if (magic !== 0x4144 || version !== 0x0200) throw Error(`Unsupported clip header at slot ${slot}`);
    const parts = structuredClone(initialParts);
    const verts = meshes.map(m => m.map(v => [...v]));
    const frames = [];
    const setT = (part, axis, value, absolute) => { part.translation[axis] = absolute ? value : (part.translation[axis] + value) | 0; };
    const setR = (part, axis, value, absolute) => { part.rotation[axis] = short(absolute ? value : part.rotation[axis] + value); };
    const counts = {};
    for (let frame = 0; frame < frameCount; frame++) {
      const tag = u16(), count = u16();
      for (let command = 0; command < count; command++) {
        const at = cursor, word = u16(), type = word & 7, target = word >>> 8;
        counts[type] = (counts[type] ?? 0) + 1;
        if (type !== 7 && target >= parts.length) throw Error(`Part ${target} out of range at ${at}`);
        const part = parts[target];
        if (type === 1) {
          const axes = word & 0x38;
          if (!axes) {
            const wide = !!(word & 0x40);
            for (const axis of [0, 1, 2]) setT(part, axis, wide ? i16() : i8(), false);
            for (const axis of [1, 0, 2]) setR(part, axis, wide ? u16() : i8(), false);
          } else {
            const rotate = !!(word & 0x80), absolute = !!(word & 0x40);
            for (let axis = 0; axis < 3; axis++) if (word & [0x20, 0x10, 0x08][axis]) {
              if (rotate) setR(part, [1, 0, 2][axis], u16(), absolute); else setT(part, axis, i16(), absolute);
            }
          }
        } else if (type === 2) {
          const n = i16(), absolute = !!(word & 0x40), mesh = verts[target];
          if (!mesh) throw Error(`Mesh ${target} missing at ${at}`);
          let index = -1;
          for (let r = 0; r < n; r++) {
            let d;
            if (word & 0x08) {
              const peek = bytes.readInt16LE(cursor);
              index++;
              if (!(peek & 0x8000)) { index = peek; cursor += 2; }
              const p = u16();
              const s5 = v => signed(v & 31, 5);
              d = [s5(p >>> 10), s5(p >>> 5), s5(p)];
            } else if (word & 0x80) {
              if (r === 0) index = 0; // s1 starts at 0 and accumulates the u8 deltas (0x800695DC)
              index += u8();
              d = [i8(), i8(), i8()];
            } else {
              index = i16();
              d = [i16(), i16(), i16()];
            }
            if (index < 0 || index >= mesh.length) throw Error(`Vertex ${index} out of range (mesh ${target}) at ${at}`);
            for (let k = 0; k < 3; k++) mesh[index][k] = short(absolute ? d[k] : mesh[index][k] + d[k]);
          }
        } else if (type === 5) {
          // visibility: runtime HIDDEN flag (not tracked here)
        } else if (type === 6) {
          if (word & 0x80) { u16(); u16(); if ((word & 0x18) === 0x18) u16(); }
          else if (word & 0x40) for (const axis of [1, 0, 2]) setR(part, axis, u16(), false);
        } else if (type === 7) {
          if (word & 8) {
            // 0x800698C4: mesh 0, vertices 0..target-1 in order; u16 = dx:4 (bits 12-15), dy:4 (8-11),
            // dz:4 (4-7), all signed, each shifted left by bits 0-3; always added.
            const mesh = verts[0];
            if (target > mesh.length) throw Error(`Packed vertex count ${target} > mesh 0 size at ${at}`);
            for (let v = 0; v < target; v++) {
              const p = u16(), shift = p & 15;
              const d = [12, 8, 4].map(s => signed((p >>> s) & 15, 4) << shift);
              for (let k = 0; k < 3; k++) mesh[v][k] = short(mesh[v][k] + d[k]);
            }
            continue;
          }
          let index = 0;
          for (let n = 0; n < target; n++, index++) {
            if (parts[index]?.parent === -1) index++;
            const packed = u16(), translation = !!(packed & 1);
            let angles;
            if ((packed & 0xFE) === 0x7E) { const next = u16(); angles = [signed(packed >>> 8, 8) << 4, signed(next >>> 8, 8) << 4, signed(next & 255, 8) << 4]; }
            else { const shift = ((packed & 14) >>> 1) + 1; angles = [12, 8, 4].map(s => signed((packed >>> s) & 15, 4) << shift); }
            for (let axis = 0; axis < 3; axis++) setR(parts[index], [1, 0, 2][axis], angles[axis], false);
            if (translation) { const p = u16(), shift = (p & 14) >>> 1; for (let axis = 0; axis < 3; axis++) setT(parts[index], axis, signed((p >>> [12, 8, 4][axis]) & 15, 4) << shift, false); }
          }
        } else throw Error(`Unsupported SMA command ${type} at ${at}`);
      }
      frames.push({ frame, tag, parts: structuredClone(parts), vertices: verts.map(m => m.map(v => [...v])) });
    }
    const closes = verts.every((m, i) => m.every((v, j) => v.every((c, k) => c === meshes[i][j][k])));
    clips.push({ slot, frameCount, commandCounts: counts, trailingBytes: limit - cursor, loopReturnsToRest: closes, frames });
  }
  return clips;
}

const tmdName = option('--tmd'), smiName = option('--smi'), smaName = option('--sma'), out = option('--out');
if (tmdName && smiName && smaName) {
  const objects = readTmd(resource(tmdName));
  const parts = readSmi(resource(smiName), objects.length);
  const rest = objects.map(o => o.vertices);
  const clips = decodeSmaWithVertices(resource(smaName), parts, rest);
  const report = { tool: 'decode_sma_vertex.mjs', tmd: tmdName, smi: smiName, sma: smaName,
    meshes: objects.map(o => ({ index: o.index, vertices: o.vertices.length, faces: o.faces.length })),
    restVertices: rest, clips };
  const ramFile = option('--ram'), actor = option('--actor');
  if (ramFile && actor) {
    // Live check: actor+0xA8 model; model+8 -> mesh table; mesh+0x3C vertices (16-byte stride);
    // actor+0xE6 = frames played; deltas are cumulative, so loops are replayed from the rest pose.
    const ram = fs.readFileSync(ramFile), o = a => a & 0x1FFFFF;
    const r32 = a => ram.readUInt32LE(o(a)), r16 = a => ram.readInt16LE(o(a)), u16r = a => ram.readUInt16LE(o(a));
    const a = parseInt(actor, 16), model = r32(a + 0xA8), table = r32(r32(model + 8));
    const live = rest.map((m, i) => { const vp = r32(r32(table + i * 4) + 0x3C); return m.map((_, j) => [0, 2, 4].map(k => r16(vp + j * 16 + k))); });
    const played = u16r(a + 0xE6), clip = clips[0];
    const matches = [];
    for (let f = 0; f < clip.frameCount; f++) {
      const same = clip.frames[f].vertices.every((m, i) => m.every((v, j) => v.every((c, k) => c === live[i][j][k])));
      if (same) matches.push(f);
    }
    report.ramCheck = { actor, e6: played, liveVertices: live, matchingFrames: matches };
  }
  fs.mkdirSync(path.dirname(path.resolve(out)), { recursive: true });
  fs.writeFileSync(out, JSON.stringify(report, null, 1) + '\n');
  console.log(JSON.stringify({ tmd: tmdName, meshes: report.meshes, clips: clips.map(({ frames, ...c }) => c), ramCheck: report.ramCheck ? { ...report.ramCheck, liveVertices: undefined } : null }));
}
