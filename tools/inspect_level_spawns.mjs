#!/usr/bin/env node
import fs from 'node:fs';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import { PoseOracle } from './lib/mips_pose_oracle.mjs';
import { readPlrs, encodePlrsRecord } from './lib/xena_level.mjs';
const out = 'build/level-assets/01', exe = fs.readFileSync('xena.exe');
const manifest = JSON.parse(fs.readFileSync('build/asset-audit/manifest.json')), archive = fs.readFileSync('disc/files/XENA.UDA');
const entry = manifest.entries.find(e => e.candidates.some(c => c.name === ['DATA', 'LEVELS', '01', 'PLRS.DAT'].join('\\')));
assert(entry?.inArchive);
const at = (entry.lba - manifest.summary.archiveLba) * 2048, source = archive.subarray(at, at + entry.size);
assert.equal(crypto.createHash('sha256').update(source).digest('hex'), entry.sha256);
const records = readPlrs(source), lines = source.toString('ascii').split(/\r\n|\r|\n/);
const ram = fs.readFileSync(`${out}/native-reference/ram_009200.bin`), r32 = p => ram.readUInt32LE(p & 0x1fffff);
assert.deepEqual(ram.subarray(0x63340, 0x638f4), exe.subarray(0x63340 - 0x10000 + 0x800, 0x638f4 - 0x10000 + 0x800));
const oracle = new PoseOracle(exe, Buffer.alloc(0));
let instructions = 0, syntheticCases = 0;
function checkRecord(record, line) {
  assert(line.length < 160, 'Original loader line buffer exceeded');
  oracle.memory.fill(0, 0x1fe000, 0x1fe054); oracle.memory.fill(0, 0x1ff040, 0x1ff0e0);
  Buffer.from(line + '\0', 'ascii').copy(oracle.memory, 0x1ff040);
  oracle.write32(0x801ff128, 0x801fe000);
  const result = oracle.execute(0x80063340, [], { registers: { 20: 0x801ff040, 21: 13, 23: 10, 30: 0x801fe044 }, stopAt: 0x800638f4 });
  instructions += result.steps;
  const bytes = encodePlrsRecord(record);
  assert.deepEqual(oracle.memory.subarray(0x1fe000, 0x1fe054), bytes, `PLRS decode mismatch: ${record.name}`);
  return bytes;
}
const binaryRecords = records.map(record => checkRecord(record, lines[record.sourceLine - 1]));
const edge = { name: 'edge', values: [-2147483648, 2147483647, 0, 65535, 32768, -32769, 65536, ...Array(9).fill(-1), ...Array(7).fill(65537)] };
checkRecord(edge, edge.name + ' ' + edge.values.join(' ')); syntheticCases++;
assert.throws(() => encodePlrsRecord({ name: '../escape', values: Array(23).fill(0) }), /Invalid/);
assert.throws(() => encodePlrsRecord({ name: 'xena', values: [1] }), /Invalid/);
assert.throws(() => encodePlrsRecord({ name: 'xena', values: [NaN, ...Array(22).fill(0)] }), /Invalid/);

function pointer(p) {
  const value = r32(p); assert(value === 0 || value >= 0x80010000 && value < 0x80200000, 'Invalid original actor pointer'); return value;
}
function text(p) {
  p &= 0x1fffff; const end = ram.indexOf(0, p); assert(end >= p && end < p + 64); return ram.subarray(p, end).toString('ascii');
}
const actors = [], visited = new Set();
for (let p = pointer(0x800f1320); p; p = pointer(p + 8)) {
  assert(!visited.has(p) && actors.length < 512, 'Invalid/cyclic actor list'); visited.add(p);
  const descriptor = pointer(p + 12), name = text(pointer(descriptor));
  const origin = [0, 4, 8].map(offset => ram.readInt32LE(((p + 0x98) & 0x1fffff) + offset));
  actors.push({ address: p, descriptor, name, origin });
}
const snapshots = [], used = new Set();
let unchangedParameters = 0, counterChanges = 0, counterReplays = 0;
const counterOracle = new PoseOracle(exe, Buffer.alloc(0));
for (const [index, record] of records.entries()) {
  const hits = actors.filter(actor => actor.name === record.name && JSON.stringify(actor.origin) === JSON.stringify(record.position));
  assert.equal(hits.length, 1, `Ambiguous/missing live actor ${index}: ${record.name}`);
  const actor = hits[0]; assert(!used.has(actor.address)); used.add(actor.address);
  const p = actor.address & 0x1fffff;
  const vectors = Array.from({ length: 9 }, (_, i) => ram.readInt32LE(p + 0x60 + i * 4));
  assert.deepEqual(vectors, record.vectorFields.flat(), `Native vectors ${index}`);
  const parameters = Array.from({ length: 7 }, (_, i) => ram.readInt16LE(p + 0x84 + i * 2));
  const initial = record.parameterFields.map(n => (n << 16) >> 16), generator = record.name.startsWith('g_');
  const updateFunction = r32(actor.descriptor + 0x1c);
  if (generator) assert.equal(updateFunction, 0x80032c18, 'Generator update routine differs');
  for (let field = 0; field < 7; field++) {
    if (parameters[field] === initial[field]) { unchangedParameters++; continue; }
    assert(generator && field === 1 && initial[field] >= 0 && initial[field] < 100 && parameters[field] >= 0 && parameters[field] < initial[field], `Unexpected native parameter mutation ${index}:${field}`);
    // Original successful-spawn block decrements +86 when count<100. Replay
    // only this proven transition; it does not recreate the complete AI history.
    let count = initial[field];
    while (count > parameters[field]) {
      counterOracle.write16(0x801fe086, count);
      const result = counterOracle.execute(0x80033bb8, [], { registers: { 16: 0x801fe100, 17: 0x801fe000 }, stopAt: 0x80033bec });
      instructions += result.steps; count = counterOracle.read(0x801fe086, 2, true); counterReplays++;
    }
    assert.equal(count, parameters[field]); counterChanges++;
  }
  snapshots.push({ index, name: record.name, kind: generator ? 'generator' : 'object', sourcePosition: record.position,
    address: `0x${actor.address.toString(16)}`, updateFunction: `0x${updateFunction.toString(16)}`,
    currentPosition: [0, 4, 8].map(offset => ram.readInt32LE((pointer(actor.address + 0x5c) & 0x1fffff) + offset)),
    initialParameters: initial, currentParameters: parameters });
}
const report = { result: 'PASS', sourceBytes: source.length, sourceRecords: records.length, decodedBytesCompared: records.length * 84,
  originalMipsInstructions: instructions, syntheticCases, rejectedCases: 3, nativeActors: actors.length, matchedSourceActors: used.size,
  nativeOriginBytes: used.size * 12, nativeVectorBytes: used.size * 36, unchangedParameters, counterChanges, counterReplays,
  generators: snapshots.filter(s => s.kind === 'generator').length,
  scope: 'All 84-byte decoded records compared to original parser instructions. All source objects matched by class name and stored initial position, with exact copied vectors/parameters except four generator counters whose decrement transition is verified. Native temporary decode buffer was freed; current AI history is not replayed.' };
fs.writeFileSync(`${out}/spawns-records.bin`, Buffer.concat(binaryRecords));
fs.writeFileSync(`${out}/spawns-source.json`, JSON.stringify({ formatVersion: 1, records: records.map((r, index) => ({ ...r, index, kind: r.name.startsWith('g_') ? 'generator' : 'object' })) }));
fs.writeFileSync(`${out}/spawns-native.json`, JSON.stringify({ snapshots, additionalRuntimeActors: actors.filter(a => !used.has(a.address)) }, null, 2));
fs.writeFileSync(`${out}/spawns-verification.json`, JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
