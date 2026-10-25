#!/usr/bin/env node
// Decode the per-class action scripts run by the original interpreter func_8004180C
// (stream pointer actor+0xD8, table actor+0xD4 / class descriptor +0x24) straight from xena.exe.
// Opcode argument counts and meanings follow the MIPS handlers (jtbl_8001762C); see
// docs/xena-damage-audit.md. Read-only.
//   node tools/decode_action_scripts.mjs [--root DECOMP] [--class 5A] [--table 0x800C90D8] [--count N] [--json]
import fs from 'node:fs';
import path from 'node:path';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : fallback; };
const root = path.resolve(option('--root', '.'));
const exe = fs.readFileSync(path.join(root, 'xena.exe'));
const BASE = 0x8000F800; // file offset = address - BASE (PS-X EXE header 0x800, load 0x80010000)
const inExe = a => a >= 0x80010000 && a - BASE + 4 <= exe.length;
const u32 = a => exe.readUInt32LE(a - BASE);
const u16 = a => exe.readUInt16LE(a - BASE);
const s16 = a => exe.readInt16LE(a - BASE);

const cls = parseInt(option('--class', '5A'), 16);
const descriptor = 0x800C33E4 + cls * 0x44;
const table = option('--table', null) ? Number(option('--table')) : u32(descriptor + 0x24);
// [name, argument count] for opcode 0x8000 + n, from the handlers in func_8004180C.
const OPS = {
  0x00: ['end', 0], 0x01: ['stop', 0], 0x02: ['goto', 1], 0x03: ['loopE2', 1], 0x04: ['loopE4', 1],
  0x05: ['delay', 1], 0x06: ['delayRand', 1], 0x07: ['sound', 1], 0x08: ['setE0', 1], 0x09: ['callTable', 1],
  0x0D: ['clrE2', 0], 0x0E: ['clrE4', 0], 0x0F: ['incE2', 0], 0x10: ['incE4', 0], 0x11: ['setE2', 1],
  0x12: ['setE4', 1], 0x13: ['ifE2eq', 2], 0x14: ['ifE4eq', 2], 0x15: ['ifE2ne', 2], 0x16: ['ifE4ne', 2],
  0x17: ['waitFrames', 1], 0x18: ['clip', 1], 0x1A: ['soundRand', 2], 0x1B: ['damage', 1],
  0x1C: ['untilFrame', 1], 0x1D: ['call8004D7A4', 2], 0x1E: ['advance', 1], 0x1F: ['soundAt', 1],
  0x20: ['soundAtRand', 2],
};
const count = Number(option('--count', '160'));
const streams = [];
for (let i = 0; i < count; i++) {
  const ptr = u32(table + i * 4);
  if (!inExe(ptr)) break;
  const ops = [];
  let a = ptr, guard = 0;
  while (guard++ < 400) {
    const h = u16(a); a += 2;
    if (!(h & 0x8000)) { ops.push({ op: 'raw', value: h }); continue; }
    const n = h & 0x7FFF, def = OPS[n];
    if (!def) { ops.push({ op: `unknown_${n.toString(16)}` }); break; }
    const args = Array.from({ length: def[1] }, (_, k) => s16(a + k * 2));
    a += def[1] * 2;
    ops.push({ op: def[0], args });
    if (n === 0x00 || n === 0x01 || n === 0x02) break;
  }
  streams.push({ index: i, address: '0x' + ptr.toString(16).toUpperCase(), ops });
}
if (argv.includes('--windows')) {
  // Linear walk of every stream: a window opens when `damage` becomes nonzero and closes at the next
  // `damage 0` or clip change. Frames are the actor+0xE6 thresholds of the surrounding untilFrame /
  // advance opcodes (9999 = clip end). Branch targets are walked as their own streams.
  const seen = new Map();
  for (const s of streams) {
    let clip = null, frame = 0, damage = 0, start = 0;
    const close = end => {
      if (clip === null || !damage) return;
      const key = `${clip.toString(16).toUpperCase()} ${damage} ${start}-${end}`;
      if (!seen.has(key)) seen.set(key, s.address);
    };
    // Follow up to 3 `goto`s (targets index the same table, as handler .L80041C20 reads +0xD4).
    const ops = [...s.ops];
    for (let hop = 0; hop < 3 && ops.at(-1)?.op === 'goto'; hop++) {
      const target = streams[ops.at(-1).args[0]];
      if (!target) break;
      ops.push(...target.ops);
    }
    for (const o of ops) {
      if (o.op === 'clip') { close('end'); clip = o.args[0]; frame = 0; start = 0; }
      else if (o.op === 'untilFrame') frame = o.args[0] === 9999 ? 'end' : o.args[0];
      else if (o.op === 'advance') frame = o.args[0];
      else if (o.op === 'damage') { close(frame); damage = o.args[0]; start = frame; }
    }
    close('end');
  }
  for (const [k, a] of [...seen].sort()) console.log(`${k}  (first seen in stream at ${a})`);
  process.exit(0);
}
if (argv.includes('--json')) { console.log(JSON.stringify({ class: cls, table, streams }, null, 1)); process.exit(0); }
for (const s of streams) {
  const text = s.ops.map(o => o.op === 'raw' ? `#${o.value}` : o.op === 'clip' ? `clip ${o.args[0].toString(16).toUpperCase()}`
    : `${o.op}${o.args?.length ? ' ' + o.args.join(',') : ''}`).join(' | ');
  console.log(`${s.index.toString().padStart(3)} ${s.address}: ${text}`);
}
