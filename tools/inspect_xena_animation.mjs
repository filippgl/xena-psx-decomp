#!/usr/bin/env node
import fs from 'node:fs';
import { readSmi, decodeSma } from './lib/xena_animation.mjs';

const out = 'build/animation-audit';
const manifest = JSON.parse(fs.readFileSync('build/asset-audit/manifest.json', 'utf8'));
const uda = fs.readFileSync('disc/files/XENA.UDA');
function resource(name) {
  const hits = manifest.entries.filter(e => e.candidates.some(c => c.name === `DATA\\LEVELS\\${name}`));
  if (hits.length !== 1 || !hits[0].inArchive) throw Error(`Missing or ambiguous ${name}`);
  const entry = hits[0];
  const offset = (entry.lba - manifest.summary.archiveLba) * 2048;
  return uda.subarray(offset, offset + entry.size);
}
const model = resource('XENA.TMD');
const parts = readSmi(resource('XENA.SMI'), model.readUInt32LE(8));
// Observed in fresh native idle captures: staff and hand chakram are hidden.
const initialVisibility = parts.map((_, i) => i !== 31 && i !== 32);
const clips = decodeSma(resource('XENA.SMA'), parts, initialVisibility);
const types = {};
for (const clip of clips) for (const frame of clip.frames) for (const command of frame.commands)
  types[command.type] = (types[command.type] || 0) + 1;
const summary = { parts: parts.length, roots: parts.flatMap((p, i) => p.parent === -1 ? [i] : []),
  clips: clips.length, frames: clips.reduce((s, c) => s + c.frameCount, 0), commandTypes: types,
  trailingBytes: clips.reduce((s, c) => s + c.trailingBytes, 0),
  caveat: 'Normal playback; each clip starts from SMI. Run verify_xena_animation.mjs for comparison with original command instructions. World transforms, rendering and clip transitions are not yet verified.' };
fs.mkdirSync(out, { recursive: true });
fs.writeFileSync(`${out}/summary.json`, JSON.stringify({ summary, initialParts: parts, initialVisibility,
  clips: clips.map(({ frames, ...clip }) => clip) }, null, 2) + '\n');
// Local extracted pose data belongs under ignored build/, never in source control.
for (const clip of clips) fs.writeFileSync(`${out}/slot-${clip.slot.toString(16).padStart(2, '0')}.json`, JSON.stringify(clip) + '\n');
console.log(JSON.stringify(summary, null, 2));
