#!/usr/bin/env node
import fs from 'node:fs';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import { readLevelBin } from './lib/xena_level.mjs';
import { PoseOracle } from './lib/mips_pose_oracle.mjs';
const out = 'build/level-assets/01';
const m = JSON.parse(fs.readFileSync('build/asset-audit/manifest.json')), archive = fs.readFileSync('disc/files/XENA.UDA');
function resource(leaf) {
  const name = ['DATA', 'LEVELS', '01', leaf].join('\\');
  const hits = m.entries.filter(e => e.candidates.some(c => c.name === name));
  assert.equal(hits.length, 1); const e = hits[0]; assert(e.inArchive);
  const at = (e.lba - m.summary.archiveLba) * 2048;
  return archive.subarray(at, at + e.size);
}
const source = resource('LEVEL.BIN'), data = readLevelBin(source);
assert.equal(data.objects.length, resource('LEVEL.TMD').readUInt32LE(8));
const ram = fs.readFileSync(`${out}/native-reference/ram_009200.bin`);
const levelBin = ram.readUInt32LE(0xecb68) & 0x1fffff;
assert(levelBin + 0x108 <= ram.length);
assert.equal(ram.readUInt32LE(levelBin + 0x100), data.objects.length);
const records = ram.readUInt32LE(levelBin + 0x104) & 0x1fffff;
assert.deepEqual(ram.subarray(records, records + data.objects.length * 40), source.subarray(data.offset, data.offset + data.objects.length * 40));
const oracle = new PoseOracle(fs.readFileSync('xena.exe'), Buffer.alloc(0));
let instructions = 0;
const objects = data.objects.map(object => {
  object.rotation.forEach((n, axis) => oracle.write16(0x801ff700 + axis * 2, n));
  const result = oracle.execute(0x8008e8ec, [0x801ff700, 0x801ff720]);
  instructions += result.steps;
  const rotationQ12 = Array.from({ length: 9 }, (_, i) => oracle.read(0x801ff720 + i * 2, 2, true));
  return { ...object, matrix: [...rotationQ12, ...object.position] };
});
assert.deepEqual(objects[1].matrix, [4096, 0, 0, 0, 4096, 0, 0, 0, 4096, 0, 0, 0]);
assert.throws(() => readLevelBin(source.subarray(0, 64)), /Truncated/);
const invalid = Buffer.from(source); invalid.writeUInt32LE(0x7fffffff, 0x104);
assert.throws(() => readLevelBin(invalid), /Invalid/);
const report = { result: 'PASS', objects: objects.length, comparedNativeBytes: objects.length * 40,
  originalRotationInstructions: instructions, rejectedCases: 2,
  sourceBinSha256: crypto.createHash('sha256').update(source).digest('hex'),
  sourceTmdSha256: crypto.createHash('sha256').update(resource('LEVEL.TMD')).digest('hex'),
  note: 'All source placement records match native RAM. XYZ translation and reordered Euler fields follow original func_800BA9EC; Q12 matrices extracted by executing original func_8008E8EC. Camera-facing flags, color/intensity fields and later dynamic behavior are not reproduced.' };
fs.writeFileSync(`${out}/placements-environment.json`, JSON.stringify({ report, objects }, null, 2));
fs.writeFileSync(`${out}/placement-verification.json`, JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
