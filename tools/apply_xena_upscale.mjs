#!/usr/bin/env node
import fs from 'node:fs';
import { png, readPng } from './lib/png.mjs';
import './preview/xena-renderer.js';
const out = 'build/xena-upscale', preview = 'build/xena-preview';
const original = readPng(fs.readFileSync(`${out}/xena-atlas-original.png`));
const generated = readPng(fs.readFileSync(`${out}/xena-atlas-ai-v1.png`));
const layout = JSON.parse(fs.readFileSync(`${out}/layout.json`));
const registration = JSON.parse(fs.readFileSync(new URL('./preview/xena-upscale-registration-v1.json', import.meta.url)));
if (generated.width !== registration.sourceWidth || generated.height !== registration.sourceHeight)
  throw Error('Generated image dimensions differ; register this variant before applying');
const factor = 4, width = original.width * factor, height = original.height * factor;
const rgba = new Uint8Array(width * height * 4);
const anchor = (anchors, coordinate) => {
  const exact = anchors.find(([value]) => value === coordinate);
  if (!exact) throw Error(`Missing registration anchor ${coordinate}`);
  return exact[1];
};
const cubic = value => {
  value = Math.abs(value);
  return value < 1 ? 1.5 * value ** 3 - 2.5 * value ** 2 + 1 : value < 2 ? -0.5 * value ** 3 + 2.5 * value ** 2 - 4 * value + 2 : 0;
};
function sample(x, y, rect) {
  const result = [0, 0, 0]; let total = 0;
  const ix = Math.floor(x), iy = Math.floor(y);
  for (let dy = -1; dy <= 2; dy++) for (let dx = -1; dx <= 2; dx++) {
    const px = Math.min(rect.x + rect.width - 1, Math.max(rect.x, ix + dx));
    const py = Math.min(rect.y + rect.height - 1, Math.max(rect.y, iy + dy));
    const at = (py * generated.width + px) * 4;
    const weight = cubic(x - ix - dx) * cubic(y - iy - dy) * generated.rgba[at + 3] / 255;
    total += weight;
    for (let channel = 0; channel < 3; channel++) result[channel] += generated.rgba[at + channel] * weight;
  }
  if (total > 0.01) return result.map(n => Math.min(255, Math.max(0, Math.round(n / total))));
  // If the restored silhouette differs, borrow the nearest nontransparent colour.
  for (let radius = 1; radius <= 12; radius++) for (let dy = -radius; dy <= radius; dy++) for (let dx = -radius; dx <= radius; dx++) {
    const px = Math.min(rect.x + rect.width - 1, Math.max(rect.x, ix + dx));
    const py = Math.min(rect.y + rect.height - 1, Math.max(rect.y, iy + dy));
    const at = (py * generated.width + px) * 4;
    if (generated.rgba[at + 3] > 128) return [...generated.rgba.subarray(at, at + 3)];
  }
  throw Error('No colour available inside registered island');
}
for (const island of layout.islands) {
  const rect = island.name === 'XEscabrd' ? registration.rightStrip : island.name === 'XEknee' ? registration.rightKnee : {
    x: anchor(registration.xAnchors, island.x), y: anchor(registration.yAnchors, island.y),
    width: anchor(registration.xAnchors, island.x + island.width) - anchor(registration.xAnchors, island.x),
    height: anchor(registration.yAnchors, island.y + island.height) - anchor(registration.yAnchors, island.y) };
  for (let y = 0; y < island.height * factor; y++) for (let x = 0; x < island.width * factor; x++) {
    const originalX = island.x + Math.floor(x / factor), originalY = island.y + Math.floor(y / factor);
    const alpha = original.rgba[(originalY * original.width + originalX) * 4 + 3];
    if (!alpha) continue;
    const color = sample(rect.x + (x + 0.5) * rect.width / (island.width * factor) - 0.5,
      rect.y + (y + 0.5) * rect.height / (island.height * factor) - 0.5, rect);
    rgba.set([...color, alpha], ((island.y * factor + y) * width + island.x * factor + x) * 4);
  }
}
let alphaMismatches = 0;
for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
  const expected = original.rgba[(Math.floor(y / factor) * original.width + Math.floor(x / factor)) * 4 + 3];
  if (rgba[(y * width + x) * 4 + 3] !== expected) alphaMismatches++;
}
if (alphaMismatches) throw Error('UV alpha mask mismatch');
fs.writeFileSync(`${out}/xena-atlas-4x.png`, png(width, height, rgba));
fs.mkdirSync(`${out}/textures`, { recursive: true });
for (const island of layout.islands) {
  const w = island.width * factor, h = island.height * factor, cropped = new Uint8Array(w * h * 4);
  for (let y = 0; y < h; y++) {
    const at = ((island.y * factor + y) * width + island.x * factor) * 4;
    cropped.set(rgba.subarray(at, at + w * 4), y * w * 4);
  }
  fs.writeFileSync(`${out}/textures/${island.name}-4x.png`, png(w, h, cropped));
}
const pages = [[128, 0], [0, 0], [384, 256]].map(([originX, originY], i) => {
  const size = 256 * factor, pixels = new Uint8Array(size * size * 4);
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const sx = originX * factor + x, sy = originY * factor + y;
    if (sx < width && sy < height) pixels.set(rgba.subarray((sy * width + sx) * 4, (sy * width + sx) * 4 + 4), (y * size + x) * 4);
  }
  const filename = `page-${i}-4x.png`;
  fs.writeFileSync(`${preview}/${filename}`, png(size, size, pixels));
  return { width: size, height: size, filename, rgbaBase64: Buffer.from(pixels).toString('base64'), rgba: pixels };
});
fs.writeFileSync(`${preview}/xena-hd-data.js`, `globalThis.XenaHdTextures=${JSON.stringify(pages.map(({ rgba, ...page }) => page))};\n`);
const source = fs.readFileSync(`${preview}/xena-data.js`, 'utf8');
const data = JSON.parse(source.slice(source.indexOf('=') + 1).trim().replace(/;$/, ''));
data.textures.forEach(t => { t.rgba = Buffer.from(t.rgbaBase64, 'base64'); });
const pose = data.clips.find(c => c.slot === 0).frames[0];
const before = XenaPreviewRenderer.render(data, pose.matrices, pose.visibility, { zoom: 1.4 });
const after = XenaPreviewRenderer.render({ ...data, textures: pages }, pose.matrices, pose.visibility, { zoom: 1.4 });
fs.writeFileSync(`${out}/xena-model-4x.png`, png(after.width, after.height, after.rgba));
const comparison = new Uint8Array(before.width * 2 * before.height * 4);
for (let y = 0; y < before.height; y++) {
  comparison.set(before.rgba.subarray(y * before.width * 4, (y + 1) * before.width * 4), y * before.width * 8);
  comparison.set(after.rgba.subarray(y * before.width * 4, (y + 1) * before.width * 4), y * before.width * 8 + before.width * 4);
}
fs.writeFileSync(`${out}/comparison.png`, png(before.width * 2, before.height, comparison));
const report = { result: 'PASS', width, height, sourceGeneratedWidth: generated.width,
  sourceGeneratedHeight: generated.height, islands: layout.islands.length, alphaMismatches,
  method: 'Built-in imagegen restoration, registered to original islands, bicubic resize to exact 4x, original alpha mask restored.',
  originalPreserved: true, texturePages: pages.map(({ filename, width, height }) => ({ filename, width, height })) };
fs.writeFileSync(`${out}/verification.json`, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
