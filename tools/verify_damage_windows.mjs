#!/usr/bin/env node
// Cross-check Xena's attack-damage windows decoded from the original action scripts
// (tools/decode_action_scripts.mjs --windows) against actor +0xFE in existing native RAM captures.
// A sample (state = +0xD0, frame = +0xE6, damage = +0xFE) is "explained" when a window of that clip
// has the same damage and start < frame <= end ("end" = clip end). Read-only; runs no game.
//   node tools/verify_damage_windows.mjs [--root DECOMP] [--captures build/combat-reference,...]
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : fallback; };
const root = path.resolve(option('--root', '.'));
const here = path.dirname(fileURLToPath(import.meta.url));
const text = execFileSync(process.execPath, [path.join(here, 'decode_action_scripts.mjs'), '--root', root, '--class', '5A', '--windows'], { encoding: 'utf8' });
const windows = text.trim().split('\n').map(line => {
  const [slot, damage, range] = line.split(/\s+/);
  const [start, end] = range.split('-');
  return { slot: parseInt(slot, 16), damage: Number(damage), start: start === 'end' ? Infinity : Number(start), end: end === 'end' ? Infinity : Number(end) };
});
const dirs = option('--captures', null)?.split(',') ??
  fs.readdirSync(path.join(root, 'build')).filter(d => /^(combat-.*reference|action-reference-.*)$/.test(d)).map(d => path.join('build', d));
const XENA_PTR = 0x800EE174;
let samples = 0, active = 0, explained = 0;
const unexplained = new Map();
for (const dir of dirs) {
  const full = path.join(root, dir);
  if (!fs.existsSync(full)) continue;
  for (const file of fs.readdirSync(full).filter(f => /^ram_\d+\.bin$/.test(f)).sort()) {
    const ram = fs.readFileSync(path.join(full, file));
    if (ram.length < 0x200000) continue;
    const xena = ram.readUInt32LE(XENA_PTR & 0x1FFFFF);
    if (xena >>> 24 !== 0x80) continue;
    const at = xena & 0x1FFFFF;
    const state = ram.readUInt32LE(at + 0xD0), frame = ram.readUInt16LE(at + 0xE6), damage = ram.readInt16LE(at + 0xFE);
    samples++;
    if (!damage) continue;
    active++;
    if (windows.some(w => w.slot === state && w.damage === damage && w.start < frame && frame <= w.end)) explained++;
    else {
      const key = `${state.toString(16).toUpperCase()} dmg ${damage}`;
      const entry = unexplained.get(key) ?? { frames: new Set(), example: `${dir}/${file}` };
      entry.frames.add(frame); unexplained.set(key, entry);
    }
  }
}
console.log(JSON.stringify({
  windows: windows.length, samples, activeSamples: active, explained,
  unexplained: [...unexplained].map(([k, v]) => ({ clipDamage: k, frames: [...v.frames].sort((a, b) => a - b), example: v.example })),
}, null, 1));
