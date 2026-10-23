#!/usr/bin/env node
import fs from 'node:fs';
import assert from 'node:assert/strict';
import { readTrig, worldMatrices } from './lib/xena_transforms.mjs';
const reference = JSON.parse(fs.readFileSync('build/animation-reference/reference.json'));
const trig = readTrig(fs.readFileSync('xena.exe'));
let localParts = 0, matrices = 0;
for (const capture of reference.captures) {
  const clip = JSON.parse(fs.readFileSync(`build/animation-audit/slot-${capture.state.toString(16).padStart(2, '0')}.json`));
  const frame = clip.frames[capture.animationFrame - 1];
  assert(frame, 'Missing captured frame');
  capture.parts.forEach((part, i) => {
    if (part.parent === -1) return; // The game replaces the root with its world pose.
    assert.deepEqual(part, frame.parts[i], `Runtime pose mismatch capture=${capture.frame} part=${i}`);
    localParts++;
  });
  assert.deepEqual(capture.visibility, frame.visibility, 'Runtime equipment/visibility differs');
  const expected = worldMatrices(capture.parts, trig, capture.externalMatrix);
  expected.forEach((matrix, i) => {
    assert.deepEqual(matrix, capture.matrices[i], `Runtime world matrix mismatch capture=${capture.frame} part=${i}`);
    matrices++;
  });
}
const verification = { result: 'PASS', nativeExecutable: reference.executable, modified: reference.modified,
  captures: reference.captures.length, localParts, matrices,
  scope: 'Native idle captures, local poses except game-controlled root, all camera-space world matrices and equipment visibility. Not emulator verification.' };
fs.writeFileSync('build/animation-reference/verification.json', JSON.stringify(verification, null, 2) + '\n');
console.log(JSON.stringify(verification, null, 2));
