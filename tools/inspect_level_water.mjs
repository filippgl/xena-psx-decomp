#!/usr/bin/env node
import fs from 'node:fs';
import assert from 'node:assert/strict';
import { PoseOracle } from './lib/mips_pose_oracle.mjs';
import './preview/xena-water.js';
const out = 'build/level-assets/01', exe = fs.readFileSync('xena.exe');
const at = 0xdab18 - 0x10000 + 0x800;
const table = Array.from({ length: 258 }, (_, i) => exe.readInt16LE(at + i * 2));
const oracle = new PoseOracle(exe, Buffer.alloc(0));
let instructions = 0;
for (let phase = 0; phase < 65536; phase++) {
  const result = oracle.execute(0x800ad94c, [(phase << 16) >> 16]);
  assert.equal(XenaWater.sine(table, phase), result.result | 0, `Sine phase ${phase}`);
  instructions += result.steps;
}
const ram = fs.readFileSync(`${out}/native-reference/ram_009200.bin`), r32 = p => ram.readUInt32LE(p & 0x1fffff);
const phase = ram.readUInt16LE(0xec210 + 0x48f8), base = ram.readInt16LE(0xec210 + 0x270);
const flags = JSON.parse(fs.readFileSync(`${out}/flags-source.json`)).records;
let compared = 0;
for (let i = 0; i < r32(0xf17e0); i++) {
  const object = r32(0x801013d0 + i * 4), vertices = r32(object + 0x3c);
  for (let v = 0; v < r32(object + 0x34); v++) {
    const at = (vertices + v * 16) & 0x1fffff, x = ram.readInt16LE(at);
    assert.equal(XenaWater.height(table, x, base, phase), ram.readInt16LE(at + 2)); compared++;
  }
}
const data = { formatVersion: 1, table, base, capturedPhase: phase, phaseStep: 512,
  objects: flags.filter(r => r.flags & 0x80).map(r => r.index),
  note: 'Level01 original Y-only wave, no level04 Z deformation. Manual phase preview makes no claim about gameplay timing.' };
const report = { result: 'PASS', phasesCompared: 65536, originalMipsInstructions: instructions, nativeVerticesCompared: compared, phase, base, objects: data.objects };
fs.writeFileSync(`${out}/water-source.json`, JSON.stringify(data));
fs.writeFileSync(`${out}/water-verification.json`, JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
