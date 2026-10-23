#!/usr/bin/env node
// Verify archive-only subdivision and ordered grid against original native RAM.
import fs from 'node:fs';
import assert from 'node:assert/strict';
import { readTmd } from './lib/xena_model.mjs';
import { PoseOracle } from './lib/mips_pose_oracle.mjs';
const out = 'build/level-assets/01';
const m = JSON.parse(fs.readFileSync('build/asset-audit/manifest.json')), archive = fs.readFileSync('disc/files/XENA.UDA');
const e = m.entries.find(e => e.candidates.some(c => c.name === ['DATA', 'LEVELS', '01', 'LEVEL.TMD'].join('\\')));
assert(e?.inArchive);
const start = (e.lba - m.summary.archiveLba) * 2048;
const source = readTmd(archive.subarray(start, start + e.size), { allowSemiTransparent: true })[1];
const triangles = source.faces.map(face => face.indices.map(i => source.vertices[i]));
const origin = [0, 2].map(axis => Math.min(...triangles.flat().map(v => v[axis])));
const native = JSON.parse(fs.readFileSync(`${out}/terrain-runtime.json`));
assert.deepEqual(origin, native.origin);
const cells = Array.from({ length: 256 }, () => []);
const oracle = new PoseOracle(fs.readFileSync('xena.exe'), Buffer.alloc(0));
oracle.write32(0x800f17f4, origin[0]); oracle.write32(0x800f17f8, origin[1]);
let instructions = 0;
for (let i = 0; i < triangles.length; i++) {
  const triangle = triangles[i], bucket = axis => {
    const values = triangle.map(v => v[axis]);
    return Math.max(0, Math.min(15, Math.trunc((Math.trunc((Math.min(...values) + Math.max(...values)) / 2) - origin[axis === 0 ? 0 : 1]) / 4096)));
  };
  triangle.forEach((v, index) => v.forEach((n, axis) => oracle.write16(0x801ff700 + index * 8 + axis * 2, n)));
  const args = [0x801ff700, 0x801ff708, 0x801ff710];
  const x = oracle.execute(0x800bcc84, args), z = oracle.execute(0x800bcd70, args);
  instructions += x.steps + z.steps;
  assert.equal(bucket(0), x.result | 0); assert.equal(bucket(2), z.result | 0);
  cells[bucket(2) * 16 + bucket(0)].push(i);
}
const key = triangle => triangle.map(v => v.join(',')).sort().join('|');
let sourceOrderingDifferences = 0;
for (let i = 0; i < cells.length; i++) {
  const fromSource = cells[i].map(j => key(triangles[j])), fromNative = native.cells[i].map(j => key(native.triangles[j]));
  assert.deepEqual([...fromSource].sort(), [...fromNative].sort(), `Grid membership mismatch cell ${i}`);
  if (JSON.stringify(fromSource) !== JSON.stringify(fromNative)) sourceOrderingDifferences++;
}
const rebuilt = JSON.parse(fs.readFileSync(`${out}/terrain-archive.json`));
assert.deepEqual(rebuilt.origin, native.origin);
assert.deepEqual(rebuilt.cells, native.cells, 'Archive grid index/order mismatch');
assert.deepEqual(rebuilt.triangles, native.triangles, 'Archive grid oriented triangle/order mismatch');
const subdivision = JSON.parse(fs.readFileSync(`${out}/subdivision-source.json`));
const ram = fs.readFileSync(`${out}/native-reference/ram_009200.bin`);
const r32 = p => ram.readUInt32LE(p & 0x1fffff), r16 = p => ram.readInt16LE(p & 0x1fffff);
assert.equal(subdivision.radiusLimit, r32(0x800f17cc));
assert.equal(subdivision.leaves.length, r32(0x800f1800));
let chunkVertices = 0, chunkTriangles = 0;
for (const [index, mesh] of subdivision.leaves.entries()) {
  const ptr = r32(0x801006f8 + index * 4), vertexBase = r32(ptr + 0x3c), polygonBase = r32(ptr + 0x40);
  assert.equal(mesh.vertices.length, r32(ptr + 0x34));
  assert.equal(mesh.faces.length, r32(ptr + 0x24));
  for (const [i, vertex] of mesh.vertices.entries()) {
    assert.deepEqual(vertex.position, [0, 2, 4].map(axis => r16(vertexBase + i * 16 + axis)), `Chunk ${index} vertex ${i}`);
    chunkVertices++;
  }
  for (const [i, face] of mesh.faces.entries()) {
    assert.deepEqual(face.indices, [14, 16, 18].map(offset => ram.readUInt16LE((polygonBase + i * 32 + offset) & 0x1fffff) / 16), `Chunk ${index} face ${i}`);
    chunkTriangles++;
  }
}
assert.deepEqual([...subdivision.sourceFaces].sort((a, b) => a - b), triangles.map((_, i) => i));
const report = { membership: 'PASS', ordering: 'PASS', subdivision: 'PASS',
  triangles: triangles.length, cells: cells.length, origin, cellsWithDifferentOrdering: 0,
  sourceFaceOrderMismatchCellsBeforeRecovery: sourceOrderingDifferences,
  chunks: subdivision.leaves.length, chunkVertices, chunkTriangles, radiusLimit: subdivision.radiusLimit,
  originalMipsInstructions: instructions,
  note: 'Archive-only static subdivision matches every native chunk vertex and polygon index. Stable integer-mean-Y cell sorting reproduces the exact oriented native grid. No native RAM is read by build_level_terrain.mjs.' };
fs.writeFileSync(`${out}/grid-membership-verification.json`, JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
