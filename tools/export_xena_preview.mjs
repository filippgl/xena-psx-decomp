#!/usr/bin/env node
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import { readTmd, readStm, buildVram, texturePage } from './lib/xena_model.mjs';
import { readTrig, worldMatrices, transformPoint } from './lib/xena_transforms.mjs';
import { png } from './lib/png.mjs';
import './preview/xena-renderer.js';

const out = 'build/xena-preview';
const manifest = JSON.parse(fs.readFileSync('build/asset-audit/manifest.json'));
const uda = fs.readFileSync('disc/files/XENA.UDA');
const resource = ext => {
  const matches = manifest.entries.filter(e => e.candidates.some(c => c.name === `DATA\\LEVELS\\XENA.${ext}`));
  if (matches.length !== 1 || !matches[0].inArchive) throw Error(`Missing Xena ${ext}`);
  const entry = matches[0], at = (entry.lba - manifest.summary.archiveLba) * 2048;
  return uda.subarray(at, at + entry.size);
};
const objects = readTmd(resource('TMD'));
const stm = readStm(resource('STM')), { vram, populated } = buildVram(stm);
const summary = JSON.parse(fs.readFileSync('build/animation-audit/summary.json'));
const trig = readTrig(fs.readFileSync('xena.exe'));
const textures = [], keys = new Map();
fs.mkdirSync(out, { recursive: true });
let missingUvCorners = 0;
for (const object of objects) for (const face of object.faces) {
  if (!face.uv) continue;
  const key = `${face.page}-${face.clut}`;
  if (!keys.has(key)) {
    const image = texturePage(vram, face.page, face.clut);
    const material = textures.length;
    keys.set(key, material);
    const filename = `page-${material}.png`;
    fs.writeFileSync(`${out}/${filename}`, png(256, 256, image.rgba));
    textures.push({ page: face.page, clut: face.clut, filename, rgba: image.rgba,
      rgbaBase64: Buffer.from(image.rgba).toString('base64') });
  }
  face.material = keys.get(key);
  const depth = (face.page >>> 7) & 3, px = (face.page & 15) * 64, py = (face.page & 16) * 16;
  for (const [u, v] of face.uv) if (!populated[(py + v) * 1024 + px + (u >>> (2 - depth))]) missingUvCorners++;
}
if (missingUvCorners) throw Error(`${missingUvCorners} UV corners point outside uploaded images`);
const bind = worldMatrices(summary.initialParts, trig);
const bindPoints = objects.flatMap((o, i) => o.vertices.map(v => transformPoint(bind[i], v)));
const bounds = [0, 1, 2].map(axis => [Math.min(...bindPoints.map(p => p[axis])), Math.max(...bindPoints.map(p => p[axis]))]);
const center = bounds.map(([min, max]) => (min + max) / 2);
const clips = summary.clips.map(meta => {
  const source = JSON.parse(fs.readFileSync(`build/animation-audit/slot-${meta.slot.toString(16).padStart(2, '0')}.json`));
  return { slot: source.slot, frames: source.frames.map(frame => ({
    matrices: worldMatrices(frame.parts, trig).map(m => [...m.rotation, ...m.translation]), visibility: frame.visibility })) };
});
const data = { objects, parents: summary.initialParts.map(p => p.parent), center, bounds, textures, clips };
const report = { objects: objects.length, vertices: objects.reduce((n, o) => n + o.vertices.length, 0),
  triangles: objects.reduce((n, o) => n + o.faces.length, 0), stmImages: stm.images.length,
  stmPaddingBytes: stm.paddingBytes, texturePages: textures.length, missingUvCorners, clips: clips.length, bounds,
  limitations: 'Diagnostic orthographic unlit rendering; no PS1 semi-transparency or gameplay transitions. Pose timing selectable, not established from original scheduler.' };
fs.writeFileSync(`${out}/xena-data.js`, `globalThis.XenaPreviewData=${JSON.stringify({ ...data,
  textures: textures.map(({ rgba, ...texture }) => texture) })};\n`);
for (const name of ['preview.html', 'xena-viewer.js', 'xena-renderer.js'])
  fs.copyFileSync(fileURLToPath(new URL(`./preview/${name}`, import.meta.url)), `${out}/${name}`);
if (!fs.existsSync(`${out}/xena-hd-data.js`)) fs.writeFileSync(`${out}/xena-hd-data.js`, 'globalThis.XenaHdTextures=null;\n');
const shots = [[0, 0, 'idle'], [0x14, 2, 'run'], [0x51, 7, 'sword'], [0x61, 8, 'kick']];
const sheet = new Uint8Array(640 * 720 * shots.length * 4);
shots.forEach(([slot, frame, name], panel) => {
  const clip = clips.find(c => c.slot === slot), pose = clip.frames[Math.min(frame, clip.frames.length - 1)];
  const image = XenaPreviewRenderer.render(data, pose.matrices, pose.visibility);
  if (!image.fragments) throw Error(`Blank preview ${name}`);
  fs.writeFileSync(`${out}/${name}.png`, png(image.width, image.height, image.rgba));
  for (let y = 0; y < image.height; y++) sheet.set(image.rgba.subarray(y * 640 * 4, (y + 1) * 640 * 4), (y * 640 * shots.length + panel * 640) * 4);
});
fs.writeFileSync(`${out}/contact-sheet.png`, png(640 * shots.length, 720, sheet));
fs.writeFileSync(`${out}/report.json`, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
console.log(`Open ${out}/preview.html`);
