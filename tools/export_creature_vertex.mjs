#!/usr/bin/env node
// Export the SMA vertex animation of the level-01 ambient creatures (LARK, BFLY1, BFLY2) as JSON:
// per model, per SMA frame, the absolute vertex positions of every mesh, in two orders:
//   vertices : TMD vertex order, PS1 model units, mesh-local, Y down (same as RAM mesh+0x3C)
//   unity    : exactly the `meshes[m].primitives[p].positions` order and transform of
//              `node tools/export_enemy_unity.mjs --static` (.xenamodel): per mesh, primitives grouped by
//              material in first-use order, per face corners [0,2,1], value = float32 (x, y, -z), PS1 units
//              (the 1/400 and Y/Z flip live in the exporter's `<Prefix>_SourceToYUp` root node).
// Frames come from tools/decode_sma_vertex.mjs (mirror of func_80069040). Playback rules: docs/xena-lark-generator.md.
//
//   node tools/export_creature_vertex.mjs --root DECOMP --out FILE.json
//        [--check-static DIR]   compare frame 0 with DIR/<Prefix>/<Prefix>.xenamodel (export_enemy_unity --static)
//        [--ram DIR]            compare every live lark/bfly1/bfly2 in DIR/ram_*.bin with frame (+0xE6 - 1)
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { pathToFileURL, fileURLToPath } from 'node:url';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i < 0 ? fallback : argv[i + 1]; };
const root = path.resolve(option('--root', '.'));
const out = option('--out');
if (!out) throw Error('--out FILE.json required');
const lib = name => import(pathToFileURL(path.join(root, 'tools', 'lib', name)).href);
const { readTmd } = await lib('xena_model.mjs');
const { readSmi } = await lib('xena_animation.mjs');
const { decodeSmaWithVertices } = await import(pathToFileURL(path.join(path.dirname(fileURLToPath(import.meta.url)), 'decode_sma_vertex.mjs')).href);

const manifest = JSON.parse(fs.readFileSync(path.join(root, 'build/asset-audit/manifest.json')));
const uda = fs.readFileSync(path.join(root, 'disc/files/XENA.UDA'));
function resource(file) {
  const name = `DATA\\LEVELS\\01\\${file}`;
  const hits = manifest.entries.filter(e => e.candidates.some(c => c.name === name));
  if (hits.length !== 1 || !hits[0].inArchive) throw Error(`Missing or ambiguous ${name}`);
  const at = (hits[0].lba - manifest.summary.archiveLba) * 2048, bytes = uda.subarray(at, at + hits[0].size);
  return { name, bytes, sha256: crypto.createHash('sha256').update(bytes).digest('hex') };
}

// actor class name (descriptor +0) -> model files. bfly2 descriptor +0x10 = 0x49 -> shares BFLY1.SMI/SMA.
const MODELS = [
  { model: 'LARK', prefix: 'Lark', anim: 'LARK', actor: 'lark' },
  { model: 'BFLY1', prefix: 'Bfly1', anim: 'BFLY1', actor: 'bfly1' },
  { model: 'BFLY2', prefix: 'Bfly2', anim: 'BFLY1', actor: 'bfly2' },
];

const f32 = Math.fround;
// Same grouping key as export_enemy_unity.mjs materialFor(): texture page/clut (first-use index there),
// double-sided flag, flat colour. Equal key <=> same material index, so the group order is identical.
const materialKey = face => `${face.uv ? `${face.page}-${face.clut}` : 'solid'}:${!!face.doubleSided}:${face.color?.join(',') || ''}`;
function unityPositions(objects, vertices) {
  return objects.map((object, m) => {
    const groups = new Map();
    for (const face of object.faces) {
      const key = materialKey(face);
      if (!groups.has(key)) groups.set(key, []);
      const g = groups.get(key);
      for (const corner of [0, 2, 1]) { const v = vertices[m][face.indices[corner]]; g.push(f32(v[0]), f32(v[1]), -f32(v[2])); }
    }
    return [...groups.values()];
  });
}

const models = MODELS.map(spec => {
  const tmd = resource(`${spec.model}.TMD`), smi = resource(`${spec.anim}.SMI`), sma = resource(`${spec.anim}.SMA`);
  const objects = readTmd(tmd.bytes), parts = readSmi(smi.bytes, objects.length), rest = objects.map(o => o.vertices);
  const clips = decodeSmaWithVertices(sma.bytes, parts, rest);
  if (clips.length !== 1 || clips[0].slot !== 0) throw Error(`${spec.model}: expected only SMA slot 0`);
  const clip = clips[0];
  const partsConstant = clip.frames.every(f => JSON.stringify(f.parts) === JSON.stringify(parts));
  const frame0IsRest = JSON.stringify(clip.frames[0].vertices) === JSON.stringify(rest);
  return { ...spec, files: { tmd: tmd.name, tmdSha256: tmd.sha256, smi: smi.name, sma: sma.name, smaSha256: sma.sha256 },
    slot: 0, frameCount: clip.frameCount, partTransformsConstant: partsConstant, frame0EqualsRest: frame0IsRest,
    meshes: objects.map(o => ({ index: o.index, vertexCount: o.vertices.length, faceCount: o.faces.length,
      unityPrimitiveVertexCounts: unityPositions([o], [o.vertices])[0].map(p => p.length / 3) })),
    restVertices: rest,
    frames: clip.frames.map(f => ({ frame: f.frame, e6: f.frame + 1, tag: f.tag, vertices: f.vertices, unity: unityPositions(objects, f.vertices) })),
    objects };
});
// BFLY2 shares BFLY1's animation; record whether its geometry is identical too.
const b1 = models.find(m => m.model === 'BFLY1'), b2 = models.find(m => m.model === 'BFLY2');
b2.sameVerticesAs = JSON.stringify(b1.frames.map(f => f.vertices)) === JSON.stringify(b2.frames.map(f => f.vertices)) ? 'BFLY1' : null;

const checks = {};
const staticDir = option('--check-static');
if (staticDir) {
  checks.static = models.map(m => {
    const doc = JSON.parse(fs.readFileSync(path.join(staticDir, m.prefix, `${m.prefix}.xenamodel`)));
    const meshes = doc.meshes.map(mesh => mesh.primitives.map(p => p.positions));
    const same = JSON.stringify(meshes) === JSON.stringify(m.frames[0].unity);
    return { model: m.model, xenamodel: `${m.prefix}/${m.prefix}.xenamodel`, frame0UnityEqualsStaticPositions: same,
      floats: meshes.flat(2).length };
  });
}
const ramDir = option('--ram');
if (ramDir) {
  const result = { dumps: 0, actors: 0, match: 0, mismatch: [], perModel: {} };
  for (const file of fs.readdirSync(ramDir).filter(f => /^ram_\d+\.bin$/.test(f)).sort()) {
    const ram = fs.readFileSync(path.join(ramDir, file)), o = a => a & 0x1FFFFF;
    const r32 = a => ram.readUInt32LE(o(a)), r16 = a => ram.readInt16LE(o(a)), u16 = a => ram.readUInt16LE(o(a));
    const text = a => { const s = o(a), e = ram.indexOf(0, s); return e > s && e < s + 32 ? ram.subarray(s, e).toString('ascii') : ''; };
    result.dumps++;
    const seen = new Set();
    for (let p = r32(0x800F1320); p && !seen.has(p) && seen.size < 1024; p = r32(p + 8)) {
      seen.add(p);
      const m = models.find(x => x.actor === text(r32(r32(p + 12))));
      if (!m) continue;
      const model = r32(p + 0xA8); if (!model) continue;
      const table = r32(r32(model + 8));
      // Same live read as decode_sma_vertex.mjs: model+8 -> mesh table; mesh+0x3C -> vertices, 16-byte stride.
      const live = m.restVertices.map((mesh, i) => { const vp = r32(r32(table + i * 4) + 0x3C); return mesh.map((_, j) => [0, 2, 4].map(k => r16(vp + j * 16 + k))); });
      const e6 = u16(p + 0xE6), expected = e6 === 0 ? m.restVertices : m.frames[e6 - 1]?.vertices;
      const ok = !!expected && JSON.stringify(live) === JSON.stringify(expected);
      result.actors++; if (ok) result.match++; else if (result.mismatch.length < 20) result.mismatch.push({ file, actor: p.toString(16), model: m.model, e6 });
      const k = result.perModel[m.model] ??= { actors: 0, match: 0, e6: {} };
      k.actors++; if (ok) k.match++; k.e6[e6] = (k.e6[e6] ?? 0) + 1;
    }
  }
  checks.ram = result;
}

const report = {
  formatVersion: 1, tool: 'tools/export_creature_vertex.mjs',
  units: 'PS1 TMD model units, mesh-local (no part/actor transform), Y down. unity arrays: float32 (x, y, -z), still PS1 units; ' +
    'export_enemy_unity.mjs root node <Prefix>_SourceToYUp scales by (1/400, -1/400, -1/400).',
  playback: 'Animation script (descriptor +0x24) loops SMA slot 0 forward, one frame per step: frames 0..N-1 then frame 0 again ' +
    '(no ping-pong). Frame 0 rewrites the rest pose, so loops do not drift. A step happens at most once per game tick (30 Hz) when ' +
    'D_800F16C0 (VSync counter, 60 Hz) >= actor+0xDC; after a step +0xDC = now + actor+0xE0. +0xE0 = 0 freezes the wings. ' +
    'Displayed frame = frames[+0xE6 - 1]; +0xE6 = 0 is the untouched rest pose.',
  models: models.map(({ objects, actor, prefix, ...m }) => ({ actorClass: actor, unityPrefix: prefix, ...m })),
  checks,
};
fs.mkdirSync(path.dirname(path.resolve(out)), { recursive: true });
fs.writeFileSync(out, JSON.stringify(report) + '\n');
console.log(JSON.stringify({ out, models: report.models.map(m => ({ model: m.model, frames: m.frameCount, meshes: m.meshes.map(x => x.vertexCount),
  unityPrimitives: m.meshes.map(x => x.unityPrimitiveVertexCounts), partTransformsConstant: m.partTransformsConstant,
  frame0EqualsRest: m.frame0EqualsRest, sameVerticesAs: m.sameVerticesAs })), checks: { static: checks.static,
  ram: checks.ram && { ...checks.ram, mismatch: checks.ram.mismatch.slice(0, 5) } } }, null, 1));
