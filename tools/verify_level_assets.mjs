#!/usr/bin/env node
// Independent raw-packet comparison; does not call the model parser under test.
import fs from 'node:fs';
import assert from 'node:assert/strict';
import { readPng } from './lib/png.mjs';
import { readTmd } from './lib/xena_model.mjs';
const atOption = process.argv.indexOf('--level'), label = String(atOption < 0 ? 1 : Number(process.argv[atOption + 1])).padStart(2, '0');
const out = `build/level-assets/${label}`;
const gltf = JSON.parse(fs.readFileSync(`${out}/level.gltf`)), bin = fs.readFileSync(`${out}/level.bin`);
const manifest = JSON.parse(fs.readFileSync('build/asset-audit/manifest.json'));
const entry = manifest.entries.find(e => e.candidates.some(c => c.name === ['DATA', 'LEVELS', label, 'LEVEL.TMD'].join('\\')));
assert(entry?.inArchive);
const archive = fs.readFileSync('disc/files/XENA.UDA'), offset = (entry.lba - manifest.summary.archiveLba) * 2048;
const source = archive.subarray(offset, offset + entry.size);
const lightEntry = manifest.entries.find(e => e.candidates.some(c => c.name === ['DATA', 'LEVELS', label, 'LEVEL.LGT'].join('\\')));
assert(lightEntry?.inArchive);
const lightOffset = (lightEntry.lba - manifest.summary.archiveLba) * 2048;
const light = archive.subarray(lightOffset, lightOffset + lightEntry.size);
let lightCursor = 0, lightingValuesCompared = 0;
assert.equal(bin.length, gltf.buffers[0].byteLength);
const components = { VEC2: 2, VEC3: 3 };
function read(index) {
  const a = gltf.accessors[index], view = gltf.bufferViews[a.bufferView];
  assert.equal(a.componentType, 5126);
  const count = a.count * components[a.type], start = (view.byteOffset || 0) + (a.byteOffset || 0);
  assert(Number.isInteger(count)); assert.equal(start % 4, 0);
  assert(start + count * 4 <= view.byteOffset + view.byteLength);
  assert(view.byteOffset + view.byteLength <= bin.length);
  return Array.from({ length: count }, (_, i) => { const n = bin.readFloatLE(start + i * 4); assert(Number.isFinite(n)); return n; });
}
for (let i = 0; i < gltf.accessors.length; i++) read(i);
let comparedValues = 0, triangles = 0, semiTransparentTriangles = 0, reconstructedZeroNormals = 0;
function compare(a, b, label) {
  assert.equal(a.length, b.length, label);
  for (let i = 0; i < a.length; i++) { assert(Math.abs(a[i] - b[i]) < 0.00001, `${label} value ${i}`); comparedValues++; }
}
const count = source.readUInt32LE(8);
assert.equal(gltf.meshes.length, count); assert.equal(gltf.nodes.length, count); assert.equal(gltf.scenes.length, count);
for (let object = 0; object < count; object++) {
  const header = 12 + object * 28, vertices = 12 + source.readUInt32LE(header), normals = 12 + source.readUInt32LE(header + 8);
  const groups = new Map(); let packet = 12 + source.readUInt32LE(header + 16);
  assert.equal(source.readInt32LE(header + 24), 0);
  for (let f = 0; f < source.readUInt32LE(header + 20); f++) {
    const flags = source[packet + 2], mode = source[packet + 3];
    assert(mode === 0x34 || mode === 0x36); assert.equal(source[packet + 1], 6); assert.equal(flags & ~2, 0);
    const page = source.readUInt16LE(packet + 10), clut = source.readUInt16LE(packet + 6);
    const material = gltf.materials.findIndex(m => m.extras.sourceTPage === page && m.extras.sourceClut === clut &&
      m.doubleSided === !!(flags & 2) && m.extras.ps1SemiTransparent === !!(mode & 2));
    assert(material >= 0);
    if (!groups.has(material)) groups.set(material, { p: [], n: [], uv: [], lighting: [] });
    const g = groups.get(material);
    const corners = [0, 1, 2].map(corner => {
      const vi = source.readUInt16LE(packet + 18 + corner * 4);
      return [0, 2, 4].map(c => source.readInt16LE(vertices + vi * 8 + c));
    });
    for (const corner of [0, 2, 1]) {
      const ni = source.readUInt16LE(packet + 16 + corner * 4), vi = source.readUInt16LE(packet + 18 + corner * 4);
      let normal = [0, 2, 4].map(c => source.readInt16LE(normals + ni * 8 + c)), len = Math.hypot(...normal);
      if (!len) {
        const u = corners[2].map((n, axis) => n - corners[0][axis]), v = corners[1].map((n, axis) => n - corners[0][axis]);
        normal = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]];
        len = Math.hypot(...normal);
        if (!len) { normal = [0, 1, 0]; len = 1; }
        reconstructedZeroNormals++;
      }
      g.p.push(...[0, 2, 4].map(c => source.readInt16LE(vertices + vi * 8 + c) / 400));
      g.lighting.push(...light.subarray(lightCursor + vi * 4, lightCursor + vi * 4 + 3));
      g.n.push(...normal.map(n => n / len));
      g.uv.push((source[packet + 4 + corner * 4] + 0.5) / 256, (source[packet + 5 + corner * 4] + 0.5) / 256);
    }
    triangles++; if (mode & 2) semiTransparentTriangles++;
    packet += 28;
  }
  assert.equal(gltf.meshes[object].primitives.length, groups.size);
  for (const primitive of gltf.meshes[object].primitives) {
    assert.equal(primitive.mode, 4); const expected = groups.get(primitive.material); assert(expected);
    compare(read(primitive.attributes.POSITION), expected.p, `object ${object} positions`);
    compare(read(primitive.attributes.NORMAL), expected.n, `object ${object} normals`);
    compare(read(primitive.attributes.TEXCOORD_0), expected.uv, `object ${object} UV`);
    const actualLighting = read(primitive.attributes._XENA_LGT);
    assert.deepEqual(actualLighting, expected.lighting, `object ${object} original LGT bytes`);
    lightingValuesCompared += actualLighting.length;
    groups.delete(primitive.material);
  }
  assert.equal(groups.size, 0);
  lightCursor += source.readUInt32LE(header + 4) * 4;
  assert.deepEqual(gltf.nodes[object].rotation, [1, 0, 0, 0]);
  assert.equal(gltf.nodes[object].mesh, object); assert.deepEqual(gltf.scenes[object].nodes, [object]);
}
assert.equal(lightCursor, light.length);
for (const image of gltf.images) {
  assert(/^page-\d+\.png$/.test(image.uri));
  const decoded = readPng(fs.readFileSync(`${out}/${image.uri}`));
  assert.equal(decoded.width, 256); assert.equal(decoded.height, 256);
}
// New transparent-mode support is opt-in; malformed packet layouts stay rejected.
assert.throws(() => readTmd(source), /Unsupported TMD primitive/);
const corrupt = Buffer.from(source), packet = 12 + corrupt.readUInt32LE(28);
corrupt[packet + 1] = 1;
assert.throws(() => readTmd(corrupt, { allowSemiTransparent: true }), /Unsupported TMD primitive/);
assert.throws(() => readTmd(source.subarray(0, 64), { allowSemiTransparent: true }), /TMD range/);
const report = { result: 'PASS', level: Number(label), objects: count, triangles, semiTransparentTriangles,
  comparedValues, lightingValuesCompared, reconstructedZeroNormals, images: gltf.images.length, rejectedCases: 3,
  scope: 'Every exported triangle coordinate, winding, normal and UV independently compared to raw TMD packets. PNG dimensions and bounds checked. Not an in-game or Unity rendering comparison.' };
fs.writeFileSync(`${out}/verification.json`, JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
