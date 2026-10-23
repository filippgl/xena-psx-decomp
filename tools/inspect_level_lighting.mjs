#!/usr/bin/env node
import fs from 'node:fs';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import { readTmd } from './lib/xena_model.mjs';
import { readLgt, applyLgtBias } from './lib/xena_level.mjs';
import { subdivideMeshLibrary, originalDistance } from './lib/xena_subdivision.mjs';
import { PoseOracle } from './lib/mips_pose_oracle.mjs';
const out = 'build/level-assets/01';
const manifest = JSON.parse(fs.readFileSync('build/asset-audit/manifest.json'));
const archive = fs.readFileSync('disc/files/XENA.UDA'), exe = fs.readFileSync('xena.exe');
function resource(name) {
  const full = ['DATA', 'LEVELS', '01', name].join('\\');
  const matches = manifest.entries.filter(e => e.candidates.some(c => c.name === full));
  assert.equal(matches.length, 1); const e = matches[0]; assert(e.inArchive);
  const at = (e.lba - manifest.summary.archiveLba) * 2048;
  const bytes = archive.subarray(at, at + e.size);
  assert.equal(crypto.createHash('sha256').update(bytes).digest('hex'), e.sha256);
  return bytes;
}
const objects = readTmd(resource('LEVEL.TMD'), { allowSemiTransparent: true });
const raw = resource('LEVEL.LGT'), counts = objects.map(o => o.vertices.length), colors = readLgt(raw, counts);
const ram = fs.readFileSync(`${out}/native-reference/ram_009200.bin`);
const r32 = p => ram.readUInt32LE(p & 0x1fffff);
const radiusLimit = exe.readInt16LE(0xc18e4 - 0x10000 + 0x800 + 6);
const initialBias = exe.readInt32LE(0xc1644 - 0x10000 + 0x800), nativeBias = ram.readInt32LE(0xc1644);
const sourceSeen = new Set(), distance = originalDistance(exe);
let comparedVertices = 0, comparedFaces = 0;
const oracle = new PoseOracle(exe, Buffer.alloc(0)); oracle.memory = Buffer.from(ram);
let waterVertices = 0, waterInstructions = 0;
const groups = [
  { objects: [objects[1]], table: 0x801006f8, count: r32(0x800f1800) },
  { objects: objects.filter(o => o.index !== 1), table: 0x80101898, count: r32(0x800f17ec) },
];
for (const group of groups) {
  const leaves = subdivideMeshLibrary(group.objects, { radiusLimit, distance });
  assert.equal(leaves.length, group.count);
  for (const [index, leaf] of leaves.entries()) {
    const ptr = r32(group.table + index * 4), vertexBase = r32(ptr + 0x3c), polygonBase = r32(ptr + 0x40);
    assert.equal(r32(ptr + 0x34), leaf.vertices.length);
    assert.equal(r32(ptr + 0x24), leaf.faces.length);
    for (const [vertexIndex, vertex] of leaf.vertices.entries()) {
      const at = (vertexBase + vertexIndex * 16) & 0x1fffff;
      const expectedPosition = [...vertex.position];
      if (r32(ptr) & 0x80) {
        // Original water update 8002A2E0..334 replaces Y, leaving RGB intact.
        // The extra level04 Z deformation is outside this level01 verifier.
        assert.notEqual(ram[0xf2772], 4);
        const phase = ram.readUInt16LE(0xec210 + 0x48f8);
        const angle = ((phase + ((vertex.position[0] & 7) << 13)) << 16) >> 16;
        const sine = oracle.execute(0x800ad94c, [angle]);
        expectedPosition[1] = ((ram.readUInt16LE(0xec210 + 0x270) + Math.trunc(((sine.result << 16) >> 16) / 256)) << 16) >> 16;
        waterVertices++; waterInstructions += sine.steps;
      }
      assert.deepEqual([0, 2, 4].map(axis => ram.readInt16LE(at + axis)), expectedPosition,
        `Object ${leaf.sourceObject}, chunk ${index}, vertex ${vertex.sourceIndex}, flags ${r32(ptr).toString(16)}`);
      const source = colors[leaf.sourceObject][vertex.sourceIndex];
      assert.deepEqual([...ram.subarray(at + 12, at + 16)], [...source.slice(0, 3), 0x44], `Base color ${leaf.sourceObject}:${vertex.sourceIndex}`);
      assert.deepEqual([...ram.subarray(at + 8, at + 12)], [...applyLgtBias(source, nativeBias), source[3]], `Biased color ${leaf.sourceObject}:${vertex.sourceIndex}`);
      sourceSeen.add(`${leaf.sourceObject}:${vertex.sourceIndex}`); comparedVertices++;
    }
    for (const [faceIndex, face] of leaf.faces.entries()) {
      assert.deepEqual([14, 16, 18].map(offset => ram.readUInt16LE(((polygonBase + faceIndex * 32) & 0x1fffff) + offset) / 16), face.indices);
      comparedFaces++;
    }
  }
}
assert.equal(sourceSeen.size, counts.reduce((n, count) => n + count, 0));
assert.throws(() => readLgt(raw.subarray(0, raw.length - 1), counts), /Invalid LGT size/);
assert.throws(() => readLgt(Buffer.concat([raw, Buffer.alloc(1)]), counts), /Invalid LGT size/);
assert.throws(() => readLgt(raw, [-1]), /Invalid LGT vertex counts/);
assert.deepEqual(applyLgtBias([0, 128, 255, 0], 35), [35, 163, 255]);
assert.deepEqual(applyLgtBias([0, 128, 255, 0], -35), [0, 93, 220]);
const sourceSha256 = crypto.createHash('sha256').update(raw).digest('hex');
fs.writeFileSync(`${out}/lighting-source.json`, JSON.stringify({ formatVersion: 1, sourceSha256, initialBias,
  objects: colors.map((colors, index) => ({ index, colors })),
  note: 'Original per-vertex RGB plus uninterpreted fourth code byte. Not alpha. Initial EXE bias is 0; captured native runtime bias is separately reported. Dynamic lighting/fog/render modulation not yet reproduced.' }));
const report = { result: 'PASS', sourceBytes: raw.length, sourceVertices: sourceSeen.size, objects: objects.length,
  staticChunks: groups[0].count, dynamicChunks: groups[1].count, comparedVertices, comparedFaces,
  comparedColorBytes: comparedVertices * 8, initialBias, nativeBias, sourceSha256, rejectedCases: 3,
  waterVertices, originalWaterSineInstructions: waterInstructions,
  scope: 'All source vertices covered. Exact original and biased vertex color bytes, chunk geometry and face indices match native RAM. Bias35 is a runtime snapshot, not baked into source. Final pixel rendering, dynamic color updates and PS1 STP blend remain separate.' };
fs.writeFileSync(`${out}/lighting-verification.json`, JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
