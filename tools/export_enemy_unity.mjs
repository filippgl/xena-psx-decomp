#!/usr/bin/env node
// Export a level character (TMD + STM model, shared SMI/SMA animation set) straight into the
// JsonUtility-safe .xenamodel contract read by XenaModelImporter.cs.
//
// It reproduces, in one step, the verified Xena chain
//   inspect_xena_animation.mjs -> export_xena_preview.mjs -> export_xena_gltf.mjs -> export_xena_unity.mjs
// including every float32 rounding the glTF buffer introduces, so `--xena-parity` can compare the
// result for XENA.TMD/SMA with the already imported Xena.xenamodel value for value.
//
//   node tools/export_enemy_unity.mjs [--root DECOMP] [--level 01] [--anim BANDIT]
//        [--model BANDIT --model BANDIT1 ...] [--out DIR] [--xena-parity FILE]
//
// Inputs (all read-only): ROOT/build/asset-audit/manifest.json, ROOT/disc/files/XENA.UDA,
// ROOT/xena.exe (trig table), ROOT/tools/lib/*.mjs (falls back to the parsers next to this script).
// Unknown TMD/SMA layouts throw inside the shared parsers; nothing is guessed here.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath, pathToFileURL } from 'node:url';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i < 0 ? fallback : argv[i + 1]; };
const options = name => argv.flatMap((a, i) => a === name ? [argv[i + 1]] : []);
const root = path.resolve(option('--root', '.'));
const level = option('--level', '01');
const parityFileArg = option('--xena-parity', null);
const anim = option('--anim', parityFileArg ? 'XENA' : 'BANDIT').toUpperCase();
const parityFile = option('--xena-parity', null);
let models = options('--model').map(m => m.toUpperCase());
if (!models.length) models = parityFile ? ['XENA'] : ['BANDIT', 'BANDIT1', 'BANDIT2', 'BANDIT4'];
const out = path.resolve(option('--out', path.join(root, 'build', 'enemy-unity', level)));
const unitsPerMeter = 400; // Same project convention as Xena/Level01 (not measured from the game).

const libDir = [path.join(root, 'tools', 'lib'), path.join(path.dirname(fileURLToPath(import.meta.url)), 'lib')]
  .find(d => fs.existsSync(path.join(d, 'xena_model.mjs')));
if (!libDir) throw Error('tools/lib parsers not found');
const lib = name => import(pathToFileURL(path.join(libDir, name)).href);
const { readTmd, readStm, buildVram, texturePage } = await lib('xena_model.mjs');
const { readSmi, decodeSma } = await lib('xena_animation.mjs');
const { readTrig, worldMatrices } = await lib('xena_transforms.mjs');
const { png } = await lib('png.mjs');

const manifest = JSON.parse(fs.readFileSync(path.join(root, 'build/asset-audit/manifest.json')));
const uda = fs.readFileSync(path.join(root, 'disc/files/XENA.UDA'));
const trig = readTrig(fs.readFileSync(path.join(root, 'xena.exe')));
// --shared reads models kept in DATA\LEVELS itself (e.g. SIGN_PT, used by every level).
const shared = argv.includes('--shared');
const resourceName = file => parityFile || shared ? `DATA\\LEVELS\\${file}` : `DATA\\LEVELS\\${level}\\${file}`;
function resource(file) {
  const name = resourceName(file);
  const hits = manifest.entries.filter(e => e.candidates.some(c => c.name === name));
  if (hits.length !== 1 || !hits[0].inArchive) throw Error(`Missing or ambiguous ${name}`);
  const at = (hits[0].lba - manifest.summary.archiveLba) * 2048;
  return { name, lba: hits[0].lba, bytes: uda.subarray(at, at + hits[0].size) };
}

const f32 = Math.fround;
const reflectVectors = v => v.map((n, i) => i % 3 === 2 ? -n : n);
const reflectQuaternions = v => v.map((n, i) => i % 4 < 2 ? -n : n);
function multiply(a, b) {
  const [x, y, z, w] = a, [u, v, q, r] = b;
  return [w * u + x * r + y * q - z * v, w * v - x * q + y * r + z * u,
    w * q + x * v - y * u + z * r, w * r - x * u - y * v - z * q];
}
function quaternion(rotation) {
  const [x, y, z] = rotation.map(n => n * Math.PI / 4096);
  return multiply(multiply([0, Math.sin(y), 0, Math.cos(y)], [-Math.sin(x), 0, 0, Math.cos(x)]), [0, 0, Math.sin(z), Math.cos(z)]);
}
function matrix(q) {
  const [x, y, z, w] = q;
  return [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w),
    2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
    2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)];
}
// Identical to export_xena_gltf.mjs bake(): original fixed-point joint origins, original local angles.
function bake(frame) {
  const fixed = worldMatrices(frame.parts, trig), worldQ = new Array(frame.parts.length);
  const localQ = frame.parts.map(p => quaternion(p.rotation));
  const resolve = i => worldQ[i] || (worldQ[i] = frame.parts[i].parent === -1 ? localQ[i] : multiply(resolve(frame.parts[i].parent), localQ[i]));
  const translations = frame.parts.map((part, i) => {
    if (part.parent === -1) return fixed[i].translation;
    const parent = part.parent, r = matrix(resolve(parent));
    const delta = fixed[i].translation.map((n, axis) => n - fixed[parent].translation[axis]);
    return [0, 1, 2].map(col => [0, 1, 2].reduce((s, row) => s + r[row * 3 + col] * delta[row], 0));
  });
  return { translations, rotations: localQ }; // node TRS stay JSON doubles; accessor samples are float32 below
}
const srgbToLinear = n => { const s = n / 255; return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; };
const titleCase = s => s[0] + s.slice(1).toLowerCase();

// ---- shared animation set ----
// --static: models without an SMA on disc (e.g. SIGN_PT): one clip, slot 0, the SMI rest pose.
const staticPose = argv.includes('--static');
const smiRes = resource(`${anim}.SMI`), smaRes = staticPose ? null : resource(`${anim}.SMA`);
const animationDecodeCache = new Map();
function animation(partCount, initialVisibility) {
  const key = `${partCount}:${initialVisibility.join('')}`;
  if (!animationDecodeCache.has(key)) {
    const parts = readSmi(smiRes.bytes, partCount);
    const clips = staticPose ? [{ slot: 0, frameCount: 1, frames: [{ parts: structuredClone(parts), visibility: [...initialVisibility], commands: [] }] }]
      : decodeSma(smaRes.bytes, parts, initialVisibility);
    const commandTypes = {};
    for (const c of clips) for (const f of c.frames) for (const cmd of f.commands) commandTypes[cmd.type] = (commandTypes[cmd.type] || 0) + 1;
    animationDecodeCache.set(key, { parts, clips, commandTypes });
  }
  return animationDecodeCache.get(key);
}

function exportModel(model) {
  const xena = model === 'XENA';
  const prefix = xena ? 'Xena' : titleCase(model);
  const tmdRes = resource(`${model}.TMD`), stmRes = resource(`${model}.STM`);
  const objects = readTmd(tmdRes.bytes), stm = readStm(stmRes.bytes), { vram, populated } = buildVram(stm);
  // Xena: staff/hand chakram hidden, observed in native idle captures (inspect_xena_animation.mjs).
  // Other characters: no hidden-part baseline is established; every part starts visible (UNVERIFIED).
  const initialVisibility = objects.map((_, i) => !(xena && (i === 31 || i === 32)));
  const forcedVisibilityParts = xena ? [30, 31, 32] : [];
  const { parts, clips, commandTypes } = animation(objects.length, initialVisibility);
  if (parts.length !== objects.length) throw Error('SMI/TMD part count mismatch');

  // Texture pages in first-use order, exactly as export_xena_preview.mjs.
  const pages = [], pageKeys = new Map();
  let missingUvCorners = 0;
  for (const object of objects) for (const face of object.faces) {
    if (!face.uv) continue;
    const key = `${face.page}-${face.clut}`;
    if (!pageKeys.has(key)) { pageKeys.set(key, pages.length); pages.push({ page: face.page, clut: face.clut }); }
    face.material = pageKeys.get(key);
    const depth = (face.page >>> 7) & 3, px = (face.page & 15) * 64, py = (face.page & 16) * 16;
    for (const [u, v] of face.uv) if (!populated[(py + v) * 1024 + px + (u >>> (2 - depth))]) missingUvCorners++;
  }
  if (missingUvCorners) throw Error(`${model}: ${missingUvCorners} UV corners outside uploaded STM images`);

  // Materials in first-use order, exactly as export_xena_gltf.mjs material().
  const materials = [], materialKeys = new Map();
  const materialFor = face => {
    const key = `${face.material ?? 'solid'}:${!!face.doubleSided}:${face.color?.join(',') || ''}`;
    if (!materialKeys.has(key)) {
      materialKeys.set(key, materials.length);
      materials.push({ name: `Original_${key}`, doubleSided: face.doubleSided, texture: face.material ?? -1,
        color: face.material !== undefined ? [1, 1, 1, 1] : [...face.color.map(srgbToLinear), 1],
        vertexLighting: false, semiTransparent: false, blendMode: 0 });
    }
    return materialKeys.get(key);
  };

  const firstPose = clips.find(c => c.slot === 0)?.frames[0];
  if (!firstPose) throw Error('No slot 00 bind pose');
  const firstBake = bake(firstPose);
  const nodes = [{ name: `${prefix}_SourceToYUp`, parent: -1, mesh: -1, translation: [0, 0, 0], rotation: [0, 0, 0, 1],
    scale: [1 / unitsPerMeter, -1 / unitsPerMeter, -1 / unitsPerMeter] }];
  parts.forEach((part, i) => nodes.push({ name: `Part_${String(i).padStart(2, '0')}`, parent: part.parent === -1 ? 0 : part.parent + 1, mesh: -1,
    translation: reflectVectors(firstBake.translations[i]), rotation: reflectQuaternions(firstBake.rotations[i]), scale: [1, 1, 1] }));
  const meshes = [], meshNodes = [];
  let triangles = 0;
  for (const object of objects) {
    const groups = new Map();
    for (const face of object.faces) {
      const index = materialFor(face);
      if (!groups.has(index)) groups.set(index, { positions: [], normals: [], uv: [], textured: face.uv !== undefined });
      const group = groups.get(index);
      for (const corner of [0, 2, 1]) { // PS1 front faces use the opposite winding (glTF stage).
        group.positions.push(...object.vertices[face.indices[corner]].map(f32));
        const normal = object.normals[face.normals[corner]], length = Math.hypot(...normal) || 1;
        group.normals.push(...normal.map(n => f32(n / length)));
        if (face.uv) group.uv.push(...face.uv[corner].map(n => f32((n + 0.5) / 256)));
      }
      triangles++;
    }
    const primitives = [...groups].map(([material, g]) => ({ material,
      positions: reflectVectors(g.positions), normals: reflectVectors(g.normals),
      uv: g.textured ? g.uv.map((n, i) => i % 2 ? 1 - n : n) : [],
      indices: Array.from({ length: g.positions.length / 3 }, (_, i) => i - i % 3 + [0, 2, 1][i % 3]), lightingRgb: [] }));
    meshes.push({ name: `${prefix}_Mesh_${object.index}`, primitives });
    meshNodes.push(nodes.length);
    nodes.push({ name: `Geometry_${object.index}`, parent: object.index + 1, mesh: meshes.length - 1,
      translation: [0, 0, 0], rotation: [0, 0, 0, 1], scale: firstPose.visibility[object.index] ? [1, 1, 1] : [0, 0, 0] });
  }

  const unityClips = clips.map(clip => {
    const samples = [...clip.frames, clip.frames.at(-1)], channels = [];
    const baked = samples.map(bake).map(b => ({ translations: b.translations.map(t => t.map(f32)), rotations: b.rotations.map(q => q.map(f32)) }));
    for (let part = 0; part < objects.length; part++) {
      channels.push({ node: part + 1, property: 'translation', components: 3, values: reflectVectors(baked.flatMap(f => f.translations[part])) });
      channels.push({ node: part + 1, property: 'rotation', components: 4, values: reflectQuaternions(baked.flatMap(f => f.rotations[part])) });
      if (forcedVisibilityParts.includes(part) || clip.frames.some(f => f.visibility[part] !== firstPose.visibility[part]))
        channels.push({ node: meshNodes[part], property: 'scale', components: 3, values: samples.flatMap(f => f.visibility[part] ? [1, 1, 1] : [0, 0, 0]) });
    }
    return { name: `${prefix}_${clip.slot.toString(16).padStart(2, '0').toUpperCase()}`, slot: clip.slot,
      sourceFrameCount: clip.frameCount, stepsPerSecond: 30, times: samples.map((_, i) => f32(i / 30)), channels };
  });

  const dir = path.join(out, prefix);
  fs.mkdirSync(dir, { recursive: true });
  const textures = pages.map(({ page, clut }, i) => {
    const original = `page-${i}.png`;
    fs.writeFileSync(path.join(dir, original), png(256, 256, texturePage(vram, page, clut).rgba));
    return { original, upscaled: '', semiTransparentMask: '' };
  });
  const sourceHash = crypto.createHash('sha256');
  for (const r of [tmdRes, stmRes, smiRes, smaRes].filter(Boolean)) sourceHash.update(r.bytes);
  const document = { formatVersion: 1, source: `SLUS-00977 ${tmdRes.name} + ${stmRes.name} + ${smaRes ? smaRes.name : smiRes.name + " rest pose"}`,
    sourceBufferSha256: sourceHash.digest('hex'), unitsPerMeter,
    conventions: 'Unity left-handed Y-up: reflect Z, reverse triangles, invert V (same chain as Xena). scale400 and 30 steps/sec are project conventions, not measured. ' +
      (xena ? 'Xena idle equipment visibility baseline.' : 'All parts start visible: no hidden-part baseline established for this character.'),
    nodes, meshes, materials, textures, clips: unityClips };
  const file = path.join(dir, `${prefix}.xenamodel`);
  fs.writeFileSync(file, JSON.stringify(document));
  // Root motion: SMA type-6 context fields. AI 0x8001E0F8/0x8001FCB8 pass context+0x24 (speed, units/step)
  // and rotY + context+0x26 (direction, 2048 = forward) to the mover 0x8006E78C, then clear +0x24.
  const motion = { source: smaRes ? smaRes.name : smiRes.name, convention: 'per SMA frame: [frame, speed units/step (s16 field24), direction offset (s16 field26, 4096=360deg, 2048=forward)]',
    clips: clips.map(clip => ({ slot: clip.slot, frames: clip.frameCount, moves: clip.frames.flatMap((f, i) => f.commands.filter(c => c.contextFields)
      .map(c => [i, (c.contextFields.field24 << 16) >> 16, (c.contextFields.field26 << 16) >> 16])) })) };
  fs.writeFileSync(path.join(dir, `${prefix}.motion.json`), JSON.stringify(motion));
  // JsonUtility cannot read nested arrays: the Unity runtime reads this object form.
  fs.writeFileSync(path.join(dir, `${prefix}Motion.json`), JSON.stringify({ clips: motion.clips.map(c => ({ slot: c.slot, frames: c.frames, moves: c.moves.map(([frame, speed, direction]) => ({ frame, speed, direction })) })) }));
  const problems = validate(document);
  return { document, report: { model, file, tmd: tmdRes.name, tmdBytes: tmdRes.bytes.length, stm: stmRes.name, stmBytes: stmRes.bytes.length,
    stmImages: stm.images.map(i => i.name), objects: objects.length, vertices: objects.reduce((n, o) => n + o.vertices.length, 0), triangles,
    nodes: nodes.length, meshes: meshes.length, materials: materials.length, texturePages: textures.length,
    pageClut: pages.map(p => `${p.page}/${p.clut}`), clips: unityClips.length, frames: unityClips.reduce((n, c) => n + c.sourceFrameCount, 0),
    visibilityChannels: unityClips.reduce((n, c) => n + c.channels.filter(ch => ch.property === 'scale').length, 0),
    smaCommandTypes: commandTypes, validatorProblems: problems } };
}

// JS mirror of the checks in Runtime/XenaDocumentValidator.cs (Unity itself is not run here).
function validate(d) {
  const p = [], req = (ok, m) => { if (!ok) p.push(m); };
  const finite = a => a.every(Number.isFinite), unit = (a, i) => Math.abs(a[i] ** 2 + a[i + 1] ** 2 + a[i + 2] ** 2 + a[i + 3] ** 2 - 1) < 1e-4;
  req(d.nodes.filter(n => n.parent === -1).length === 1, 'roots');
  req(new Set(d.nodes.map(n => n.name)).size === d.nodes.length, 'duplicate node name');
  d.nodes.forEach((n, i) => { req(n.parent >= -1 && n.parent < d.nodes.length && n.parent !== i && n.mesh < d.meshes.length, `node ${i}`); req(unit(n.rotation, 0), `node ${i} quaternion`); });
  for (const m of d.meshes) for (const q of m.primitives) {
    const vc = q.positions.length / 3;
    req(q.positions.length % 3 === 0 && q.normals.length === q.positions.length && finite(q.positions) && finite(q.normals), `${m.name} arrays`);
    req(d.materials[q.material].texture < 0 || q.uv.length === vc * 2, `${m.name} uv`);
    req(q.indices.length % 3 === 0 && q.indices.every(i => i >= 0 && i < vc), `${m.name} indices`);
  }
  const slots = new Set();
  for (const c of d.clips) {
    req(!slots.has(c.slot), `dup slot ${c.slot}`); slots.add(c.slot);
    req(c.times.length === c.sourceFrameCount + 1 && c.times[0] === 0 && c.times.every((t, i) => !i || t > c.times[i - 1]), `${c.name} times`);
    const targets = new Set();
    for (const ch of c.channels) {
      req(!targets.has(`${ch.node}:${ch.property}`), `${c.name} dup channel`); targets.add(`${ch.node}:${ch.property}`);
      req(ch.values.length === c.times.length * ch.components && finite(ch.values), `${c.name} samples`);
      if (ch.property === 'rotation') for (let i = 0; i < ch.values.length; i += 4) if (!unit(ch.values, i)) { p.push(`${c.name} quaternion`); break; }
    }
  }
  return p;
}

function compare(a, b, pathName, stats) {
  if (typeof a === 'number' && typeof b === 'number') {
    stats.numbers++;
    const d = Math.abs(a - b);
    if (d > 0) { stats.different++; if (d > stats.maxDiff) { stats.maxDiff = d; stats.maxAt = pathName; } }
    return;
  }
  if (Array.isArray(a) && Array.isArray(b)) {
    if (a.length !== b.length) { stats.structural.push(`${pathName} length ${a.length}/${b.length}`); return; }
    a.forEach((v, i) => compare(v, b[i], `${pathName}[${i}]`, stats)); return;
  }
  if (a && b && typeof a === 'object' && typeof b === 'object') {
    for (const k of Object.keys(b)) if (k in a) compare(a[k], b[k], `${pathName}.${k}`, stats); else stats.structural.push(`${pathName}.${k} missing`);
    return;
  }
  if (a !== b) stats.structural.push(`${pathName} ${JSON.stringify(a)}/${JSON.stringify(b)}`);
}

// Original static class data from the EXE (not RAM): descriptor table 0x800C33E4, 0x44 bytes per class
// (loader 0x80069C5C), class->family 0x800BFE8C (8-byte records, AI 0x8001B5B8), family action table
// 0x800C0184 (47 words per family, AI index actor+0x38 * 0xBC), attack weights 0x800BFF5C (10 u16, 0x80023794).
function originalTables() {
  const exe = fs.readFileSync(path.join(root, 'xena.exe')), at = a => a - 0x80010000 + 0x800;
  const u32 = a => exe.readUInt32LE(at(a)), i16 = a => exe.readInt16LE(at(a)), u16 = a => exe.readUInt16LE(at(a));
  const text = a => { const o = at(a); if (o < 0 || o >= exe.length) return null; const e = exe.indexOf(0, o); return e > o && e - o < 16 ? exe.toString('latin1', o, e) : null; };
  const wanted = new Set(models.map(m => m.toLowerCase()));
  const classes = [];
  for (let index = 0; index < 0xB2; index++) {
    const d = 0x800C33E4 + index * 0x44, name = text(u32(d));
    if (!name || !wanted.has(name)) continue;
    classes.push({ index, descriptor: `0x${d.toString(16)}`, name, flags: `0x${u32(d + 4).toString(16)}`, health: u32(d + 8),
      animationOwnerClass: u32(d + 0x10) || index, crowdWeightField14: u32(d + 0x14), update: `0x${u32(d + 0x1c).toString(16)}`,
      halfwords28to3B: Array.from({ length: 10 }, (_, i) => u16(d + 0x28 + i * 2)), anchorList: `0x${u32(d + 0x3c).toString(16)}` });
  }
  const families = new Map();
  for (let a = 0x800BFE8C; a < 0x800BFE8C + 0xD0; a += 8) families.set(u32(a), i16(a + 4));
  const familyIds = [...new Set(classes.map(c => families.get(c.index) ?? 0))];
  return { classes: classes.map(c => ({ ...c, family: families.get(c.index) ?? 0 })),
    families: familyIds.map(f => ({ family: f,
      actionSlots: Object.fromEntries(Array.from({ length: 47 }, (_, i) => [`0x${(0x184 + i * 4).toString(16)}`, u32(0x800C0184 + f * 0xBC + i * 4)])),
      attackWeights: Array.from({ length: 10 }, (_, i) => u16(0x800BFF5C + f * 20 + i * 2)) })) };
}
const reports = [];
for (const model of models) {
  const { document, report } = exportModel(model);
  if (parityFile) {
    const reference = JSON.parse(fs.readFileSync(parityFile));
    const stats = { numbers: 0, different: 0, maxDiff: 0, maxAt: '', structural: [] };
    for (const key of ['formatVersion', 'unitsPerMeter', 'nodes', 'meshes', 'textures', 'clips']) compare(document[key], reference[key], key, stats);
    // Older Xena.xenamodel predates vertexLighting/semiTransparent/blendMode; compare the shared material fields.
    compare(document.materials.map(({ name, doubleSided, texture, color }) => ({ name, doubleSided, texture, color })), reference.materials, 'materials', stats);
    stats.structural = stats.structural.filter(s => !/textures\[\d+\]\.upscaled/.test(s));
    report.parity = { reference: parityFile, ...stats, structural: stats.structural.slice(0, 20), structuralCount: stats.structural.length };
  }
  reports.push(report);
}
const summary = { tool: 'export_enemy_unity.mjs', level, animationSet: { smi: smiRes.name, smiBytes: smiRes.bytes.length, sma: smaRes?.name ?? null, smaBytes: smaRes?.bytes.length ?? 0 },
  unitsPerMeter, unityEditorTested: false, originalTables: parityFile ? null : originalTables(), models: reports };
fs.mkdirSync(out, { recursive: true });
fs.writeFileSync(path.join(out, parityFile ? 'xena-parity-report.json' : 'export-report.json'), JSON.stringify(summary, null, 2) + '\n');
console.log(JSON.stringify({ ...summary, models: reports.map(({ smaCommandTypes, stmImages, pageClut, ...r }) => r) }, null, 2));
