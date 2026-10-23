#!/usr/bin/env node
import fs from 'node:fs';
import assert from 'node:assert/strict';
import { PoseOracle } from './lib/mips_pose_oracle.mjs';
import { readTmd } from './lib/xena_model.mjs';
import { originalDistance, subdivideMeshLibrary } from './lib/xena_subdivision.mjs';
import { readLevelFlagTokens, classifyLevelName, levelFlagMask } from './lib/xena_level_flags.mjs';
import { queryLevelHeight } from './lib/xena_level.mjs';
const out = 'build/level-assets/01';
const exe = fs.readFileSync('xena.exe'), ram = fs.readFileSync(`${out}/native-reference/ram_009200.bin`);
const tokens = readLevelFlagTokens(exe), placements = JSON.parse(fs.readFileSync(`${out}/placements-environment.json`)).objects;
assert.deepEqual(ram.subarray(0xbaa3c, 0xbad88), exe.subarray(0xbaa3c - 0x10000 + 0x800, 0xbad88 - 0x10000 + 0x800));
const oracle = new PoseOracle(exe, Buffer.alloc(0));
const terrain = JSON.parse(fs.readFileSync(`${out}/terrain-archive.json`));
let groundedObjects = 0;
const records = placements.map(record => {
  const flags = classifyLevelName(record.name, tokens), effectivePosition = [...record.position];
  if (!(flags & 0x10) && !(flags & 0x20)) {
    const height = queryLevelHeight(terrain, record.position);
    if (height !== -16777216) { effectivePosition[1] = height; groundedObjects++; }
  }
  return { index: record.index, name: record.name, flags, sourcePosition: record.position, effectivePosition };
});
let instructions = 0, syntheticCases = 0;
function check(name, initialFlags = 1) {
  Buffer.from(name + '\0', 'ascii').copy(oracle.memory, 0x1ff700);
  oracle.write32(0x801ff680 + 4, 0x801ff700); oracle.write32(0x801ff600, initialFlags);
  const result = oracle.execute(0x800baa3c, [], { registers: { 16: 0x801ff600, 18: 0, 21: 0x801ff680 }, stopAt: 0x800bad88 });
  instructions += result.steps;
  assert.equal(classifyLevelName(name, tokens, initialFlags), oracle.read32(0x801ff600), `Original classifier differs: ${name}`);
}
for (const record of records) check(record.name);
const names = ['', '@', 'ttree', 'TREE', 'terr_', 'cxship', 'shipcx', '@ship', 'terr_ship', 'terr_dec', 'terr_lav', 'terr_lv', 'lavafal', '@teleprt', ...Object.values(tokens)];
for (const name of names) for (const flags of [1, 0x7ffff]) { check(name, flags); syntheticCases++; }
const manifest = JSON.parse(fs.readFileSync('build/asset-audit/manifest.json')), archive = fs.readFileSync('disc/files/XENA.UDA');
const entry = manifest.entries.find(e => e.candidates.some(c => c.name === ['DATA', 'LEVELS', '01', 'LEVEL.TMD'].join('\\')));
const at = (entry.lba - manifest.summary.archiveLba) * 2048;
const source = readTmd(archive.subarray(at, at + entry.size), { allowSemiTransparent: true });
const r32 = p => ram.readUInt32LE(p & 0x1fffff);
const radiusLimit = exe.readInt16LE(0xc18e4 - 0x10000 + 0x800 + 6), distance = originalDistance(exe);
let chunks = 0;
for (const [staticGroup, table, countAddress] of [[true, 0x801006f8, 0x800f1800], [false, 0x80101898, 0x800f17ec]]) {
  const meshes = source.filter(o => !!(records[o.index].flags & 0x10) === staticGroup);
  const leaves = subdivideMeshLibrary(meshes, { radiusLimit, distance }); assert.equal(leaves.length, r32(countAddress));
  for (const [index, leaf] of leaves.entries()) {
    const ptr = r32(table + index * 4);
    assert.equal(r32(ptr) & levelFlagMask, records[leaf.sourceObject].flags & levelFlagMask, `Runtime flags object ${leaf.sourceObject}`);
    assert.equal(r32(ptr + 0x34), leaf.vertices.length); assert.equal(r32(ptr + 0x24), leaf.faces.length); chunks++;
    const expected = placements[leaf.sourceObject].matrix;
    const rotation = Array.from({ length: 9 }, (_, i) => ram.readInt16LE(((ptr + 4) & 0x1fffff) + i * 2));
    assert.deepEqual(rotation, expected.slice(0, 9), `Runtime rotation object ${leaf.sourceObject}`);
    assert.deepEqual([0x18, 0x1c, 0x20].map(offset => ram.readInt32LE((ptr + offset) & 0x1fffff)),
      records[leaf.sourceObject].effectivePosition, `Runtime placement object ${leaf.sourceObject}`);
  }
}
const report = { result: 'PASS', objects: records.length, chunks, syntheticCases, originalMipsInstructions: instructions,
  groundedObjects, absolutePositionObjects: records.filter(r => r.flags & 0x20).length, treeTaggedObjects: records.filter(r => r.flags & 0x40).length,
  scope: 'Whole original name classifier executed, including overlapping substring behavior and ship special case. All inherited flag bits, rotations and effective positions compared against native runtime chunks. Camera-facing render behavior remains a separate audit.' };
fs.writeFileSync(`${out}/flags-source.json`, JSON.stringify({ formatVersion: 1, tokens, mask: levelFlagMask, records }));
fs.writeFileSync(`${out}/flags-verification.json`, JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
