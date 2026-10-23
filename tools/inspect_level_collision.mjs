#!/usr/bin/env node
import fs from 'node:fs';
import assert from 'node:assert/strict';
import { readCls, readPlrs } from './lib/xena_level.mjs';
import { PoseOracle } from './lib/mips_pose_oracle.mjs';
const out = 'build/level-assets/01';
const m = JSON.parse(fs.readFileSync('build/asset-audit/manifest.json')), archive = fs.readFileSync('disc/files/XENA.UDA');
function resource(name) {
  const full = ['DATA', 'LEVELS', '01', name].join('\\');
  const hits = m.entries.filter(e => e.candidates.some(c => c.name === full));
  assert.equal(hits.length, 1); assert(hits[0].inArchive);
  const e = hits[0], at = (e.lba - m.summary.archiveLba) * 2048;
  return archive.subarray(at, at + e.size);
}
const raw = resource('LEVEL.CLS'), collision = readCls(raw), placements = readPlrs(resource('PLRS.DAT'));
fs.mkdirSync(out, { recursive: true });
fs.writeFileSync(`${out}/collision-source.json`, JSON.stringify(collision, null, 2));
fs.writeFileSync(`${out}/placements-source.json`, JSON.stringify({ records: placements,
  note: 'Original numeric fields retained. Position token storage is confirmed by loader instructions; remaining gameplay meanings and NPC path semantics are not yet established.' }, null, 2));

const ram = fs.readFileSync(`${out}/native-reference/ram_009200.bin`);
assert.equal(ram.length, 2 * 1024 * 1024);
const pointer = at => { assert(at >= 0 && at + 4 <= ram.length); const p = ram.readUInt32LE(at); assert((p >>> 20) === 0x801 || (p >>> 20) === 0x800); return p & 0x1fffff; };
const table = pointer(0xf15cc);
assert.equal(ram.readInt32LE(0xf17f0), collision.groups.length);
const short = value => (value << 16) >> 16, snapshots = [];
const oracle = new PoseOracle(fs.readFileSync('xena.exe'), Buffer.alloc(0)); oracle.memory = Buffer.from(ram);
const headerAddress = pointer(table) - 4;
assert.deepEqual(ram.subarray(headerAddress, headerAddress + 4), raw.subarray(0, 4));
let comparedBytes = 4, comparedVertices = 0, terrainHeightQueries = 0;
for (const group of collision.groups) {
  const address = pointer(table + group.index * 4), expected = Buffer.from(raw.subarray(group.offset, group.offset + group.byteLength));
  assert(address + group.byteLength <= ram.length);
  const origin = group.position;
  expected.writeInt16LE(short(expected.readInt16LE(4) + origin[0]), 4);
  expected.writeInt16LE(short(expected.readInt16LE(6) + origin[2]), 6);
  // Independently reproduce the loader's displacement, including its optional
  // original terrain height query. All level01 groups currently use absolute Y.
  let dy = origin[1];
  if (!(group.flags & 2)) {
    origin.forEach((n, axis) => oracle.write32(0x801ff800 + axis * 4, n));
    const height = oracle.execute(0x800142ec, [0x801ff800, 0, 0]).result | 0;
    terrainHeightQueries++;
    if (height !== -16777216) dy = (height + 10) | 0;
  }
  const vertices = [];
  for (const [triangleIndex, triangle] of group.triangles.entries()) {
    const world = [];
    for (let corner = 0; corner < 3; corner++) {
      const target = 28 + triangleIndex * 48 + 8 + corner * 12;
      const point = triangle.vertices[corner].map((v, axis) => (v + [origin[0], dy, origin[2]][axis]) | 0);
      for (let axis = 0; axis < 3; axis++) expected.writeInt32LE(point[axis], target + axis * 4);
      comparedVertices++; world.push(point);
    }
    vertices.push(world);
  }
  assert.deepEqual(ram.subarray(address, address + group.byteLength), expected, `CLS relocation mismatch group ${group.index}`);
  comparedBytes += expected.length;
  snapshots.push({ group: group.index, runtimeAddress: `0x${(address + 0x80000000).toString(16)}`,
    sourceOrigin: origin, observedYDisplacement: dy, flags: group.flags, triangles: vertices });
}
assert.equal(ram.readUInt32LE(table + collision.groups.length * 4), 0);
assert.throws(() => readCls(raw.subarray(0, raw.length - 1)), /Truncated/);
const invalid = Buffer.from(raw); invalid.writeInt16LE(-1, 4);
assert.throws(() => readCls(invalid), /Invalid/);
assert.throws(() => readPlrs(Buffer.from('xena 1 2\r\nEND\r\n')), /Invalid/);
assert.throws(() => readPlrs(Buffer.from('END\nrogue')), /after END/);
const reference = JSON.parse(fs.readFileSync(`${out}/native-reference/reference.json`));
const report = { result: 'PASS', groups: collision.groups.length, triangles: comparedVertices / 3, comparedVertices,
  comparedBytes, terrainHeightQueries, placements: placements.length, nativeExecutable: reference.executable, nativeModified: reference.modified,
  rejectedCases: 4, scope: 'All CLS bytes compared against native RAM with independently calculated source displacement. All level01 groups use absolute Y. This validates resource relocation, not full collision response or gameplay.' };
fs.writeFileSync(`${out}/collision-native.json`, JSON.stringify({ report, snapshots }, null, 2));
fs.writeFileSync(`${out}/collision-verification.json`, JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
