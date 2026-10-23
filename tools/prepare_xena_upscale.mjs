#!/usr/bin/env node
import fs from 'node:fs';
import { readStm, color555 } from './lib/xena_model.mjs';
import { png } from './lib/png.mjs';
const manifest = JSON.parse(fs.readFileSync('build/asset-audit/manifest.json'));
const uda = fs.readFileSync('disc/files/XENA.UDA');
const e = manifest.entries.find(e => e.candidates.some(c => c.name === 'DATA\\LEVELS\\XENA.STM'));
const at = (e.lba - manifest.summary.archiveLba) * 2048;
const stm = readStm(uda.subarray(at, at + e.size));
const width = 512, height = 512, rgba = new Uint8Array(width * height * 4), islands = [];
for (const { name, depth, image, clut } of stm.images) {
  if (depth !== 1 || !clut) throw Error('Expected 8-bit paletted Xena texture');
  const x = (image.x - 768) * 2, y = image.y, w = image.width * 2, h = image.height;
  if (x < 0 || x + w > width || y + h > height) throw Error('Atlas bounds');
  for (let row = 0; row < h; row++) for (let col = 0; col < w; col++) {
    const color = image.data[row * w + col];
    rgba.set(color555(clut.data.readUInt16LE(color * 2)), ((y + row) * width + x + col) * 4);
  }
  islands.push({ name, x, y, width: w, height: h });
}
const out = 'build/xena-upscale'; fs.mkdirSync(out, { recursive: true });
fs.writeFileSync(`${out}/xena-atlas-original.png`, png(width, height, rgba));
fs.writeFileSync(`${out}/layout.json`, JSON.stringify({ width, height, scale: 4, targetWidth: 2048,
  targetHeight: 2048, vramOriginWords: [768, 0], pixelsPerWord: 2, islands }, null, 2) + '\n');
console.log(`Prepared original 512x512 atlas, ${islands.length} islands, target 2048x2048: ${out}/xena-atlas-original.png`);
