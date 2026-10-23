#!/usr/bin/env node
import fs from 'node:fs';
import assert from 'node:assert/strict';
import { readSmi, decodeSma } from './lib/xena_animation.mjs';
import { PoseOracle } from './lib/mips_pose_oracle.mjs';

const manifest = JSON.parse(fs.readFileSync('build/asset-audit/manifest.json'));
const uda = fs.readFileSync('disc/files/XENA.UDA');
const exe = fs.readFileSync('xena.exe');
const resource = ext => {
  const entry = manifest.entries.find(e => e.candidates.some(c => c.name === `DATA\\LEVELS\\XENA.${ext}`));
  assert(entry?.inArchive);
  const at = (entry.lba - manifest.summary.archiveLba) * 2048;
  return uda.subarray(at, at + entry.size);
};
const smi = resource('SMI'), sma = resource('SMA');
const parts = readSmi(smi, resource('TMD').readUInt32LE(8));
const initialVisibility = parts.map((_, i) => i !== 31 && i !== 32);
const clips = decodeSma(sma, parts, initialVisibility);
let commands = 0, frames = 0, instructions = 0;
for (const clip of clips) {
  const oracle = new PoseOracle(exe, smi);
  // Type 5 visibility array, deliberately separate from transform storage.
  oracle.write32(oracle.context + 0xC, 0x80119000);
  initialVisibility.forEach((visible, i) => oracle.write(0x80119000 + i, visible ? 0 : 1, 1));
  const expectedFields = { field24: 0, field26: 0, field34: 0 };
  for (const frame of clip.frames) {
    for (const command of frame.commands) {
      const bytes = sma.subarray(command.offset, command.offset + command.bytes);
      const result = oracle.run(bytes);
      assert.equal(result.consumed, command.bytes, `Cursor mismatch slot=${clip.slot} frame=${frame.frame} command=${command.offset}`);
      commands++; instructions += result.steps;
      if (command.contextFields) Object.assign(expectedFields, command.contextFields);
    }
    assert.deepEqual(oracle.parts(), frame.parts, `Pose mismatch slot=${clip.slot} frame=${frame.frame}`);
    assert.deepEqual(Array.from(oracle.memory.subarray(0x119000, 0x119000 + parts.length), n => !n),
      frame.visibility, `Visibility mismatch slot=${clip.slot} frame=${frame.frame}`);
    for (const [name, offset] of [['field24', 0x24], ['field26', 0x26], ['field34', 0x34]])
      assert.equal(oracle.memory.readUInt16LE((oracle.context & 0x1fffff) + offset), expectedFields[name]);
    frames++;
  }
  assert.equal(clip.trailingBytes, 0, `Unparsed bytes after slot ${clip.slot}`);
}
// Meaningful malformed-data checks: truncation, invalid offset, cyclic hierarchy.
assert.throws(() => decodeSma(sma.subarray(0, sma.length - 1), parts), /truncated/);
const invalid = Buffer.from(sma); invalid.writeUInt32LE(0x7fffffff, 4);
assert.throws(() => decodeSma(invalid, parts), /truncated/);
const cyclic = Buffer.from(smi); cyclic.writeInt16LE(0, 18);
assert.throws(() => readSmi(cyclic, parts.length), /Cyclic/);
const summary = { result: 'PASS', clips: clips.length, frames, commands, originalMipsInstructions: instructions,
  compared: 'Every command cursor and every frame transform, visibility and metadata against original func_80069040, normal playback; mesh lookup stubbed for visibility commands.',
  malformedChecks: ['truncated SMA', 'invalid slot offset', 'cyclic SMI hierarchy'] };
fs.mkdirSync('build/animation-audit', { recursive: true });
fs.writeFileSync('build/animation-audit/verification.json', JSON.stringify(summary, null, 2) + '\n');
console.log(JSON.stringify(summary, null, 2));
