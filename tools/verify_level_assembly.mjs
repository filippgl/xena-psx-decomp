#!/usr/bin/env node
import fs from 'node:fs';
import assert from 'node:assert/strict';
import { PoseOracle } from './lib/mips_pose_oracle.mjs';
const out = 'build/level-assets/01';
const source = JSON.parse(fs.readFileSync(`${out}/level.gltf`)), assembled = JSON.parse(fs.readFileSync(`${out}/level-assembled.gltf`));
const sourceBin = fs.readFileSync(`${out}/level.bin`), assembledBin = fs.readFileSync(`${out}/level-assembled.bin`);
const placements = JSON.parse(fs.readFileSync(`${out}/placements-environment.json`)).objects;
assert.equal(assembled.meshes.length, source.meshes.length);
assert.equal(assembled.nodes.length, placements.length + 1);
assert.deepEqual(assembled.nodes.at(-1).rotation, [1, 0, 0, 0]);
assert.equal(assembledBin.length, assembled.buffers[0].byteLength);
const oracle = new PoseOracle(fs.readFileSync('xena.exe'), Buffer.alloc(0));
function read(gltf, binary, accessor, n) {
  const a = gltf.accessors[accessor], view = gltf.bufferViews[a.bufferView];
  const at = (view.byteOffset || 0) + (a.byteOffset || 0) + n * 4;
  assert(at + 4 <= view.byteOffset + view.byteLength); assert(view.byteOffset + view.byteLength <= binary.length);
  const value = binary.readFloatLE(at); assert(Number.isFinite(value)); return value;
}
let vertices = 0, instructions = 0;
for (let mesh = 0; mesh < placements.length; mesh++) {
  const matrix = placements[mesh].matrix;
  for (let i = 0; i < 5; i++) oracle.gteControl[i] = ((matrix[i * 2] & 65535) | ((matrix[i * 2 + 1] || 0) << 16));
  for (let i = 0; i < 3; i++) oracle.gteControl[5 + i] = matrix[9 + i];
  assert.equal(assembled.nodes[mesh].mesh, mesh);
  for (let primitive = 0; primitive < source.meshes[mesh].primitives.length; primitive++) {
    const from = source.meshes[mesh].primitives[primitive], to = assembled.meshes[mesh].primitives[primitive];
    assert.equal(from.material, to.material);
    const count = source.accessors[from.attributes.POSITION].count;
    assert.equal(assembled.accessors[to.attributes.POSITION].count, count);
    for (let vertex = 0; vertex < count; vertex++) {
      for (let axis = 0; axis < 3; axis++) {
        const n = Math.round(read(source, sourceBin, from.attributes.POSITION, vertex * 3 + axis) * 400);
        oracle.write16(0x801ff700 + axis * 2, n);
      }
      instructions += oracle.execute(0x800bfbe0, [0x801ff700, 0x801ff720, 0x801ff730]).steps;
      for (let axis = 0; axis < 3; axis++) {
        const expected = oracle.gteData[25 + axis] / 400;
        assert(Math.abs(read(assembled, assembledBin, to.attributes.POSITION, vertex * 3 + axis) - expected) < 0.00001,
          `Original affine transform mismatch mesh=${mesh} vertex=${vertex} axis=${axis}`);
      }
      let normalLength = 0;
      for (let axis = 0; axis < 3; axis++) normalLength += read(assembled, assembledBin, to.attributes.NORMAL, vertex * 3 + axis) ** 2;
      assert(Math.abs(normalLength - 1) < 0.00001, 'Invalid transformed normal');
      for (let axis = 0; axis < 2; axis++) assert.equal(read(source, sourceBin, from.attributes.TEXCOORD_0, vertex * 2 + axis),
        read(assembled, assembledBin, to.attributes.TEXCOORD_0, vertex * 2 + axis));
      vertices++;
    }
  }
}
const report = { result: 'PASS', objects: placements.length, comparedVertices: vertices, originalMipsInstructions: instructions,
  scope: 'All assembled glTF vertices compared to original affine GTE routine MAC outputs with original extracted Q12 placement matrices. UVs unchanged, normals normalized. Render flags, billboard behavior and native screenshots are not matched.' };
fs.writeFileSync(`${out}/assembly-verification.json`, JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
