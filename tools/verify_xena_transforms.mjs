#!/usr/bin/env node
import fs from 'node:fs';
import assert from 'node:assert/strict';
import { PoseOracle } from './lib/mips_pose_oracle.mjs';
import { readTrig, worldMatrices } from './lib/xena_transforms.mjs';

const exe = fs.readFileSync('xena.exe'), trig = readTrig(exe);
const summary = JSON.parse(fs.readFileSync('build/animation-audit/summary.json'));
const parts = summary.initialParts;
const smi = Buffer.alloc(parts.length * 20);
const oracle = new PoseOracle(exe, smi);
const shared = 0x80111000, children = 0x80111200, matrices = 0x80115000, identity = 0x80116000;
oracle.write32(oracle.context, shared);
oracle.write32(shared + 0x1c, children);
oracle.write32(oracle.context + 0x2c, matrices);
for (let i = 0; i < 9; i++) oracle.write16(identity + i * 2, i % 4 === 0 ? 4096 : 0);
let next = children + parts.length * 4;
parts.forEach((_, i) => {
  oracle.write32(children + i * 4, next);
  parts.forEach((part, child) => {
    if (part.parent === i) { oracle.write16(next, child); next += 2; }
  });
  oracle.write16(next, -1); next += 2;
});
let frames = 0, comparedMatrices = 0, instructions = 0;
for (const meta of summary.clips) {
  const clip = JSON.parse(fs.readFileSync(`build/animation-audit/slot-${meta.slot.toString(16).padStart(2, '0')}.json`));
  for (const frame of clip.frames) {
    frame.parts.forEach((part, index) => {
      const at = oracle.transforms + index * 20;
      part.translation.forEach((n, i) => oracle.write32(at + i * 4, n));
      part.rotation.forEach((n, i) => oracle.write16(at + 12 + i * 2, n));
      oracle.write16(at + 18, part.parent);
    });
    oracle.write16(oracle.context + 0x28, 0);
    const root = frame.parts.findIndex(p => p.parent === -1);
    const result = oracle.execute(0x80014DE4, [oracle.context, root, identity, 0]);
    instructions += result.steps;
    const expected = worldMatrices(frame.parts, trig);
    expected.forEach((matrix, index) => {
      const at = matrices + index * 32;
      const actual = { rotation: Array.from({ length: 9 }, (_, i) => oracle.read(at + i * 2, 2, true)),
        translation: [20, 24, 28].map(n => oracle.read(at + n, 4, true)) };
      assert.deepEqual(actual, matrix, `Matrix mismatch slot=${clip.slot} frame=${frame.frame} part=${index}`);
      comparedMatrices++;
    });
    frames++;
  }
}
const verification = { result: 'PASS', frames, comparedMatrices, originalMipsInstructions: instructions,
  compared: 'All world rotation and translation matrices against original recursive func_80014DE4/80014E78, original rotation func_80014BE4 and composition func_80014AB8; identity external transform, no optional scale.' };
fs.writeFileSync('build/animation-audit/transform-verification.json', JSON.stringify(verification, null, 2) + '\n');
console.log(JSON.stringify(verification, null, 2));
