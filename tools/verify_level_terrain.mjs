#!/usr/bin/env node
import fs from 'node:fs';
import assert from 'node:assert/strict';
import { readTmd } from './lib/xena_model.mjs';
import { queryLevelHeight } from './lib/xena_level.mjs';
import { PoseOracle } from './lib/mips_pose_oracle.mjs';
const out = 'build/level-assets/01';
const ram = fs.readFileSync(`${out}/native-reference/ram_009200.bin`), exe = fs.readFileSync('xena.exe');
assert.deepEqual(ram.subarray(0x142ec, 0x14950), exe.subarray(0x142ec - 0x10000 + 0x800, 0x14950 - 0x10000 + 0x800));
const pointer = at => { assert(at >= 0 && at + 4 <= ram.length); const p = ram.readUInt32LE(at); assert(p === 0 || (p >>> 20) === 0x801 || (p >>> 20) === 0x800); return p & 0x1fffff; };
const terrain = { origin: [ram.readInt32LE(0xf17f4), ram.readInt32LE(0xf17f8)], cells: [], triangles: [],
  note: 'Independent native RAM reference for verification of archive-only reconstruction.' };
const faces = new Map();
for (let cell = 0; cell < 256; cell++) {
  const indices = []; let list = pointer(0x101498 + cell * 4);
  if (list) for (let n = 0; ; n++, list += 8) {
    assert(n < 10000, 'Unterminated terrain cell');
    const base = pointer(list), polygon = pointer(list + 4);
    if (!polygon) break;
    assert(polygon + 20 <= ram.length);
    const key = `${base}:${polygon}`;
    if (!faces.has(key)) {
      const vertices = [14, 16, 18].map(offset => {
        const vertex = base + ram.readUInt16LE(polygon + offset);
        assert(vertex + 6 <= ram.length);
        return [0, 2, 4].map(axis => ram.readInt16LE(vertex + axis));
      });
      faces.set(key, terrain.triangles.length); terrain.triangles.push(vertices);
    }
    indices.push(faces.get(key));
  }
  terrain.cells.push(indices);
}
const m = JSON.parse(fs.readFileSync('build/asset-audit/manifest.json')), archive = fs.readFileSync('disc/files/XENA.UDA');
const entry = m.entries.find(e => e.candidates.some(c => c.name === ['DATA', 'LEVELS', '01', 'LEVEL.TMD'].join('\\')));
assert(entry?.inArchive);
const at = (entry.lba - m.summary.archiveLba) * 2048;
const original = readTmd(archive.subarray(at, at + entry.size), { allowSemiTransparent: true })[1];
const key = triangle => triangle.map(v => v.join(',')).sort().join('|');
const counts = triangles => { const result = new Map(); for (const triangle of triangles) result.set(key(triangle), (result.get(key(triangle)) || 0) + 1); return [...result].sort((a, b) => a[0].localeCompare(b[0])); };
assert.deepEqual(counts(terrain.triangles), counts(original.faces.map(f => f.indices.map(i => original.vertices[i]))));
const placements = JSON.parse(fs.readFileSync(`${out}/placements-source.json`)).records;
const collision = JSON.parse(fs.readFileSync(`${out}/collision-source.json`));
const points = [...placements.map(p => p.position), ...collision.groups.map(g => g.position)];
for (let i = 0; i < terrain.triangles.length; i += 3) points.push([0, 1, 2].map(axis => Math.trunc(terrain.triangles[i].reduce((s, v) => s + v[axis], 0) / 3)));
let seed = 0x58454e41;
const random = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed / 2 ** 32; };
for (let i = 0; i < 512; i++) points.push([Math.trunc(random() * 50000 - 25000), Math.trunc(random() * 12000 - 6000), Math.trunc(random() * 60000 - 30000)]);
for (const triangle of terrain.triangles.slice(0, 16)) points.push(...triangle);
const oracle = new PoseOracle(exe, Buffer.alloc(0)); oracle.memory = Buffer.from(ram);
const rebuilt = JSON.parse(fs.readFileSync(`${out}/terrain-archive.json`));
assert.deepEqual(rebuilt.origin, terrain.origin);
assert.deepEqual(rebuilt.cells, terrain.cells);
assert.deepEqual(rebuilt.triangles, terrain.triangles);
let instructions = 0, hit = 0, miss = 0;
const results = [];
for (const point of points) {
  point.forEach((n, axis) => oracle.write32(0x801ff800 + axis * 4, n));
  const actual = oracle.execute(0x800142ec, [0x801ff800, 0, 0]);
  const expected = queryLevelHeight(terrain, point);
  assert.equal(queryLevelHeight(rebuilt, point), expected, `Archive height query differs at ${point}`);
  assert.equal(expected, actual.result | 0, `Height query differs at ${point}`);
  instructions += actual.steps;
  if (expected === -16777216) miss++; else hit++;
  results.push({ point, height: expected });
}
const report = { result: 'PASS', originalTriangles: original.faces.length, runtimeTriangles: terrain.triangles.length,
  gridCells: terrain.cells.length, populatedCells: terrain.cells.filter(c => c.length).length,
  queries: points.length, hit, miss, originalMipsInstructions: instructions,
  archiveReconstruction: 'PASS',
  scope: 'Archive-only ordered terrain grid exactly matches native RAM. Both grids give identical heights and match original func_800142EC instructions for all sampled positions. Cached queries, normals and movement integration not validated.' };
fs.writeFileSync(`${out}/terrain-runtime.json`, JSON.stringify(terrain));
fs.writeFileSync(`${out}/terrain-query-samples.json`, JSON.stringify(results));
fs.writeFileSync(`${out}/terrain-verification.json`, JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
