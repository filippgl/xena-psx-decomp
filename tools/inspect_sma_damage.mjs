#!/usr/bin/env node
// List SMA type-6 context commands (fields 24/26/34) per clip frame for XENA.SMA / BANDIT.SMA.
// Read-only. Used by docs/xena-damage-audit.md to compare SMA data with actor +0xFE seen in RAM.
//   node tools/inspect_sma_damage.mjs [--root DECOMP] [--anim XENA|BANDIT] [--slots 51,52,3A]
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : fallback; };
const root = path.resolve(option('--root', '.'));
const anim = option('--anim', 'XENA').toUpperCase();
const slotFilter = option('--slots', null)?.split(',').map(s => parseInt(s, 16));
const { readSmi, decodeSma } = await import(pathToFileURL(path.join(root, 'tools/lib/xena_animation.mjs')).href);
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'build/asset-audit/manifest.json')));
const uda = fs.readFileSync(path.join(root, 'disc/files/XENA.UDA'));
function resource(name) {
  const hits = manifest.entries.filter(e => e.candidates.some(c => c.name === name));
  if (hits.length !== 1 || !hits[0].inArchive) throw Error(`Missing or ambiguous ${name}`);
  const at = (hits[0].lba - manifest.summary.archiveLba) * 2048;
  return uda.subarray(at, at + hits[0].size);
}
const dir = anim === 'XENA' ? 'DATA\\LEVELS\\' : 'DATA\\LEVELS\\01\\';
const smi = resource(`${dir}${anim}.SMI`), sma = resource(`${dir}${anim}.SMA`);
const partCount = (smi.length - 4) / 20;
const clips = decodeSma(sma, readSmi(smi, partCount));
const out = [];
for (const clip of clips) {
  if (slotFilter && !slotFilter.includes(clip.slot)) continue;
  const rows = [];
  clip.frames.forEach((f, i) => {
    for (const c of f.commands) if (c.type === 6 && c.contextFields) rows.push({ frame: i, tag: f.tag, word: c.word.toString(16), ...c.contextFields });
  });
  out.push({ slot: clip.slot.toString(16).toUpperCase(), frames: clip.frames.length, tags: [...new Set(clip.frames.map(f => f.tag))], context: rows.filter(r => r.field34 !== undefined) });
}
console.log(JSON.stringify({ anim, partCount, clips: out }, null, 1));
