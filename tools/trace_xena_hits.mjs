#!/usr/bin/env node
// Read-only: scan native-port RAM snapshots (2 MiB ram_NNNNNN.bin) for Xena hit / death states
// and print the fields used by the hit-reaction code in func_800367BC (docs/xena-hit-reactions.md).
// Usage: node tools/trace_xena_hits.mjs [buildDir=build] [--context N]
//   Prints every snapshot where Xena +0xD0 is in 0x66..0x7F, 0x88 or 0x8A..0x91, plus N snapshots
//   before/after each run (default 2) from the same directory.
import fs from 'fs';
import path from 'path';

const argv = process.argv.slice(2);
const ci = argv.indexOf('--context');
const context = ci >= 0 ? Number(argv[ci + 1]) : 2;
const root = path.resolve(argv.find((a, i) => !a.startsWith('--') && argv[i - 1] !== '--context') ?? 'build');
const hex = v => v.toString(16).toUpperCase();
const isHit = st => (st >= 0x66 && st <= 0x7F) || st === 0x88 || (st >= 0x8A && st <= 0x91);

function sample(file) {
  const b = fs.readFileSync(file);
  if (b.length !== 0x200000) return null;
  const xp = b.readUInt32LE(0xEE174) & 0x1FFFFF;            // D_800EE174 = Xena actor
  if (!xp) return null;
  const pos = b.readUInt32LE(xp + 0x5C) & 0x1FFFFF;
  return {
    st: b.readUInt32LE(xp + 0xD0), e6: b.readUInt16LE(xp + 0xE6), e2: b.readUInt16LE(xp + 0xE2),
    e0: b.readInt16LE(xp + 0xE0), hp: b.readInt16LE(xp + 0x10), d12: b.readInt16LE(xp + 0x12),
    last: b.readInt16LE(xp + 0x14), kb: b.readInt16LE(xp + 0x10A),
    d2c: b.readInt16LE(0xF0D2C), d30: b.readUInt16LE(0xF0D30), c7c: b.readUInt32LE(0xF0C7C),
    x: pos ? b.readInt32LE(pos) : 0, y: pos ? b.readInt32LE(pos + 4) : 0, z: pos ? b.readInt32LE(pos + 8) : 0,
  };
}

let total = 0, hits = 0;
function scanDir(dir) {
  const ents = fs.readdirSync(dir, { withFileTypes: true });
  for (const e of ents) if (e.isDirectory() && e.name !== 'card') scanDir(path.join(dir, e.name));
  const frames = ents.map(e => /^ram_(\d+)\.bin$/.exec(e.name)).filter(Boolean).map(m => +m[1]).sort((a, b) => a - b);
  if (!frames.length) return;
  const rows = frames.map(f => ({ f, s: sample(path.join(dir, `ram_${String(f).padStart(6, '0')}.bin`)) }));
  total += rows.length;
  const keep = new Set();
  rows.forEach((r, i) => { if (r.s && isHit(r.s.st)) { hits++; for (let k = i - context; k <= i + context; k++) keep.add(k); } });
  if (!keep.size) return;
  console.log(`== ${path.relative(root, dir) || '.'}`);
  [...keep].filter(i => i >= 0 && i < rows.length).sort((a, b) => a - b).forEach(i => {
    const { f, s } = rows[i]; if (!s) return;
    console.log(`${f} st=${hex(s.st)} E6=${s.e6} E2=${hex(s.e2)} E0=${s.e0} hp=${s.hp} d12=${s.d12} last=${s.last}` +
      ` kb10A=${s.kb} D2C=${s.d2c} D30=${hex(s.d30)} C7C=${hex(s.c7c)} pos=${s.x},${s.y},${s.z}`);
  });
}
scanDir(root);
console.log(`snapshots=${total} xenaHitOrDeathSnapshots=${hits}`);
