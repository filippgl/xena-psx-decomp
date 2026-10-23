#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
const out = path.resolve('build/animation-reference');
fs.mkdirSync(out, { recursive: true });
const exe = path.resolve('build/port/xena-console.exe');
const result = spawnSync(exe, ['--headless', '--frames', '9200', '--shots', '200', out,
  '--shots-from', '9000', '--ramdump', out, '9000,9200', '--memcard', path.join(out, 'card'),
  '--render-scale', '1', '--opt', 'renderer=software',
  '--script', '7400:CROSS,7700:CROSS,8000:CROSS,8300:CROSS', path.resolve('../Xena Warrior Princess.bin')],
  { encoding: 'utf8', timeout: 120000, windowsHide: true, maxBuffer: 8 * 1024 * 1024 });
const log = (result.stdout || '') + (result.stderr || '');
fs.writeFileSync(path.join(out, 'runtime.log'), log);
if (result.error || result.status !== 0 || !log.includes('reached frame 9200')) throw Error(result.error?.message || `Reference capture failed; inspect ${out}/runtime.log`);
const objectMatch = [...log.matchAll(/player object ([0-9A-Fa-f]+)/g)].at(-1);
if (!objectMatch) throw Error('Player object not recorded');
const object = parseInt(objectMatch[1], 16) & 0x1fffff;
const captures = [9000, 9200].map(frame => {
  const ram = fs.readFileSync(path.join(out, `ram_${String(frame).padStart(6, '0')}.bin`));
  const pointer = at => ram.readUInt32LE(at) & 0x1fffff;
  const context = pointer(object + 0xa8), transforms = pointer(context + 0x14), hidden = pointer(context + 0xc);
  const matrices = pointer(context + 0x2c);
  return { frame, state: ram.readUInt32LE(object + 0xd0), clip: ram.readUInt16LE(object + 0xe2),
    animationFrame: ram.readUInt16LE(object + 0xe6),
    externalMatrix: { rotation: Array.from({ length: 9 }, (_, i) => ram.readInt16LE(0xf0790 + i * 2)),
      translation: [20, 24, 28].map(n => ram.readInt32LE(0xf0790 + n)) },
    visibility: Array.from(ram.subarray(hidden, hidden + 33), n => !n),
    parts: Array.from({ length: 33 }, (_, i) => ({
      translation: [0, 4, 8].map(n => ram.readInt32LE(transforms + i * 20 + n)),
      rotation: [12, 14, 16].map(n => ram.readInt16LE(transforms + i * 20 + n)),
      parent: ram.readInt16LE(transforms + i * 20 + 18) })),
    matrices: Array.from({ length: 33 }, (_, i) => ({
      rotation: Array.from({ length: 9 }, (_, n) => ram.readInt16LE(matrices + i * 32 + n * 2)),
      translation: [20, 24, 28].map(n => ram.readInt32LE(matrices + i * 32 + n)) })) };
});
const metadata = { executable: exe, modified: fs.statSync(exe).mtime.toISOString(), object, captures,
  caveat: 'Reference from the existing native port, not a PS1 emulator or hardware capture.' };
fs.writeFileSync(path.join(out, 'reference.json'), JSON.stringify(metadata, null, 2) + '\n');
console.log(JSON.stringify({ executable: exe, modified: metadata.modified,
  captures: captures.map(({ frame, state, clip, animationFrame, visibility }) => ({ frame, state, clip, animationFrame,
    hiddenParts: visibility.flatMap((v, i) => v ? [] : [i]) })) }, null, 2));
