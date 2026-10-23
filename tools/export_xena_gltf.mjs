#!/usr/bin/env node
import fs from 'node:fs';
import { readTrig, worldMatrices } from './lib/xena_transforms.mjs';
const out = 'build/xena-preview';
const source = fs.readFileSync(`${out}/xena-data.js`, 'utf8');
const data = JSON.parse(source.slice(source.indexOf('=') + 1).trim().replace(/;$/, ''));
const summary = JSON.parse(fs.readFileSync('build/animation-audit/summary.json'));
const trig = readTrig(fs.readFileSync('xena.exe'));
const argument = process.argv.indexOf('--units-per-meter');
const unitsPerMeter = argument < 0 ? 400 : Number(process.argv[argument + 1]);
if (!Number.isFinite(unitsPerMeter) || unitsPerMeter <= 0) throw Error('Invalid units-per-meter');
const chunks = [], views = [], accessors = [];
let byteLength = 0;
function accessor(values, components, type, target = undefined, bounds = false) {
  const bytes = Buffer.alloc(values.length * 4);
  values.forEach((value, i) => { if (!Number.isFinite(value)) throw Error('Nonfinite accessor'); bytes.writeFloatLE(value, i * 4); });
  const view = views.length;
  views.push({ buffer: 0, byteOffset: byteLength, byteLength: bytes.length, ...(target ? { target } : {}) });
  chunks.push(bytes); byteLength += bytes.length;
  const entry = { bufferView: view, componentType: 5126, count: values.length / components, type };
  if (!Number.isInteger(entry.count)) throw Error('Invalid accessor length');
  if (bounds) {
    entry.min = Array.from({ length: components }, (_, c) => Math.min(...values.filter((_, i) => i % components === c)));
    entry.max = Array.from({ length: components }, (_, c) => Math.max(...values.filter((_, i) => i % components === c)));
  }
  accessors.push(entry); return accessors.length - 1;
}
function multiply(a, b) {
  const [x, y, z, w] = a, [u, v, q, r] = b;
  return [w * u + x * r + y * q - z * v, w * v - x * q + y * r + z * u,
    w * q + x * v - y * u + z * r, w * r - x * u - y * v - z * q];
}
function quaternion(rotation) {
  const [x, y, z] = rotation.map(n => n * Math.PI / 4096);
  return multiply(multiply([0, Math.sin(y), 0, Math.cos(y)], [-Math.sin(x), 0, 0, Math.cos(x)]),
    [0, 0, Math.sin(z), Math.cos(z)]);
}
function matrix(q) {
  const [x, y, z, w] = q;
  return [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w),
    2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
    2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)];
}
// Bake the fixed-point joint origins into float TRS, avoiding accumulated
// sub-unit translation rounding along the hierarchy. Keep original local angles.
function bake(frame) {
  const fixed = worldMatrices(frame.parts, trig), worldQ = new Array(33);
  const localQ = frame.parts.map(p => quaternion(p.rotation));
  function resolve(i) {
    if (worldQ[i]) return worldQ[i];
    const parent = frame.parts[i].parent;
    return worldQ[i] = parent === -1 ? localQ[i] : multiply(resolve(parent), localQ[i]);
  }
  const translations = frame.parts.map((part, i) => {
    if (part.parent === -1) return fixed[i].translation;
    const parent = part.parent, r = matrix(resolve(parent));
    const delta = fixed[i].translation.map((n, axis) => n - fixed[parent].translation[axis]);
    return [0, 1, 2].map(col => [0, 1, 2].reduce((s, row) => s + r[row * 3 + col] * delta[row], 0));
  });
  return { translations, rotations: localQ };
}
const nodes = [{ name: 'Xena_SourceToYUp', scale: [1 / unitsPerMeter, -1 / unitsPerMeter, -1 / unitsPerMeter], children: [],
  extras: { unitsPerMeter, physicalScale: 'Preview assumption; not measured from original game', sourceCoordinates: 'PS1 Y-down, conversion rotates 180 degrees about X' } }];
const firstPose = JSON.parse(fs.readFileSync('build/animation-audit/slot-00.json')).frames[0];
const firstBake = bake(firstPose);
firstPose.parts.forEach((part, i) => nodes.push({ name: `Part_${i.toString().padStart(2, '0')}`,
  translation: firstBake.translations[i], rotation: firstBake.rotations[i], children: [], extras: { originalPartIndex: i } }));
const meshes = [], materials = [], materialMap = new Map();
function material(face) {
  const key = `${face.material ?? 'solid'}:${!!face.doubleSided}:${face.color?.join(',') || ''}`;
  if (materialMap.has(key)) return materialMap.get(key);
  const pbr = { metallicFactor: 0, roughnessFactor: 1 };
  if (face.material !== undefined) pbr.baseColorTexture = { index: face.material };
  else pbr.baseColorFactor = [...face.color.map(n => { const s = n / 255; return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; }), 1];
  const index = materials.length;
  materials.push({ name: `Original_${key}`, pbrMetallicRoughness: pbr, extensions: { KHR_materials_unlit: {} },
    doubleSided: face.doubleSided, alphaMode: 'MASK', alphaCutoff: 0.5 });
  materialMap.set(key, index); return index;
}
const meshNodes = [];
for (const object of data.objects) {
  const groups = new Map();
  for (const face of object.faces) {
    const index = material(face);
    if (!groups.has(index)) groups.set(index, { positions: [], normals: [], uv: [], textured: face.uv !== undefined });
    const group = groups.get(index);
    // PS1 front faces use the opposite winding; keep normals paired with corners.
    for (const corner of [0, 2, 1]) {
      group.positions.push(...object.vertices[face.indices[corner]]);
      const normal = object.normals[face.normals[corner]], length = Math.hypot(...normal) || 1;
      group.normals.push(...normal.map(n => n / length));
      if (face.uv) group.uv.push(...face.uv[corner].map(n => (n + 0.5) / 256));
    }
  }
  const primitives = [...groups].map(([index, group]) => {
    const attributes = { POSITION: accessor(group.positions, 3, 'VEC3', 34962, true), NORMAL: accessor(group.normals, 3, 'VEC3', 34962) };
    if (group.textured) attributes.TEXCOORD_0 = accessor(group.uv, 2, 'VEC2', 34962);
    return { attributes, material: index, mode: 4 };
  });
  const meshIndex = meshes.length; meshes.push({ name: `Xena_Mesh_${object.index}`, primitives });
  const node = nodes.length; meshNodes.push(node);
  nodes.push({ name: `Geometry_${object.index}`, mesh: meshIndex, scale: firstPose.visibility[object.index] ? [1, 1, 1] : [0, 0, 0] });
  nodes[object.index + 1].children.push(node);
}
data.parents.forEach((parent, i) => nodes[parent === -1 ? 0 : parent + 1].children.push(i + 1));
const animations = [];
for (const meta of summary.clips) {
  const clip = JSON.parse(fs.readFileSync(`build/animation-audit/slot-${meta.slot.toString(16).padStart(2, '0')}.json`));
  const samples = [...clip.frames, clip.frames.at(-1)];
  const baked = samples.map(bake);
  const time = accessor(samples.map((_, i) => i / 30), 1, 'SCALAR', undefined, true);
  const samplers = [], channels = [];
  const channel = (node, path, values, components, type) => {
    const sampler = samplers.length;
    samplers.push({ input: time, output: accessor(values, components, type), interpolation: 'STEP' });
    channels.push({ sampler, target: { node, path } });
  };
  for (let part = 0; part < data.objects.length; part++) {
    channel(part + 1, 'translation', baked.flatMap(f => f.translations[part]), 3, 'VEC3');
    channel(part + 1, 'rotation', baked.flatMap(f => f.rotations[part]), 4, 'VEC4');
    // Leaf geometry visibility is separated from the hierarchy's transform nodes.
    if ([30, 31, 32].includes(part) || clip.frames.some(f => f.visibility[part] !== firstPose.visibility[part]))
      channel(meshNodes[part], 'scale', samples.flatMap(f => f.visibility[part] ? [1, 1, 1] : [0, 0, 0]), 3, 'VEC3');
  }
  animations.push({ name: `Xena_${meta.slot.toString(16).padStart(2, '0').toUpperCase()}`, samplers, channels,
    extras: { originalSlot: meta.slot, sourceFrameCount: clip.frameCount, assumedStepsPerSecond: 30,
      lastFrameHoldSeconds: 1 / 30, initialState: 'Each clip decoded from SMI and native idle equipment baseline' } });
}
const gltf = { asset: { version: '2.0', generator: 'Xena SLUS-00977 asset audit', extras: {
  timing: '30 steps/sec is an export convention; original scheduler not established',
  precision: 'Original local angles with baked fixed-point joint origins; float TRS approximates fixed-point rotations. See gltf-verification.json',
  gameAssets: 'Extracted from local disc; do not commit generated assets' } },
  extensionsUsed: ['KHR_materials_unlit'], scene: 0, scenes: [{ name: 'Xena', nodes: [0] }], nodes, meshes, materials,
  textures: data.textures.map((_, i) => ({ sampler: 0, source: i })),
  samplers: [{ magFilter: 9728, minFilter: 9728, wrapS: 33071, wrapT: 33071 }],
  images: data.textures.map(t => ({ uri: t.filename })),
  animations, accessors, bufferViews: views, buffers: [{ uri: 'xena.bin', byteLength }] };
fs.writeFileSync(`${out}/xena.bin`, Buffer.concat(chunks));
fs.writeFileSync(`${out}/xena-original.gltf`, JSON.stringify(gltf));
if (fs.existsSync(`${out}/page-0-4x.png`)) {
  gltf.images = data.textures.map((_, i) => ({ uri: `page-${i}-4x.png` }));
  fs.writeFileSync(`${out}/xena-4x.gltf`, JSON.stringify(gltf));
}
console.log(JSON.stringify({ nodes: nodes.length, meshes: meshes.length, materials: materials.length,
  animations: animations.length, binaryBytes: byteLength, unitsPerMeter, outputs: ['xena-original.gltf', 'xena-4x.gltf', 'xena.bin'] }, null, 2));
