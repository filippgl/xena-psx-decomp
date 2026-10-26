// Export a level's horizon strip (DATA\LEVELS\NN\HORIZ.STM: TIMs horiz01..08, 8-bit, 64x128 each) as one
// 512x128 RGBA panorama in horiz01..08 order (docs/xena-level-background.md) and install it as
// Resources/Original/Horizon<NN>.png in both Unity projects (local data, not committed).
//   node tools/export_horizon.mjs [--root DECOMP] [--level 01]
import fs from 'node:fs';
import path from 'node:path';
const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : fallback; };
const root = path.resolve(option('--root', '.')), level = option('--level', '01');
const { readStm } = await import(new URL('file:///' + path.join(root, 'tools/lib/xena_model.mjs').replace(/\\/g, '/')));
const { png } = await import(new URL('file:///' + path.join(root, 'tools/lib/png.mjs').replace(/\\/g, '/')));
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'build/asset-audit/manifest.json')));
const uda = fs.readFileSync(path.join(root, 'disc/files/XENA.UDA'));
const name = `DATA\\LEVELS\\${level}\\HORIZ.STM`;
const hits = manifest.entries.filter(e => e.candidates.some(c => c.name === name));
if (hits.length !== 1 || !hits[0].inArchive) throw Error(`Missing or ambiguous ${name}`);
const at = (hits[0].lba - manifest.summary.archiveLba) * 2048;
const stm = readStm(uda.subarray(at, at + hits[0].size));
const tiles = stm.images.filter(t => /^horiz0[1-8]$/i.test(t.name)).sort((a, b) => a.name.localeCompare(b.name));
if (tiles.length !== 8) throw Error(`expected horiz01..08, got ${stm.images.map(t => t.name).join(',')}`);
const W = 512, H = 128, rgba = Buffer.alloc(W * H * 4);
tiles.forEach((t, k) => {
  if (t.depth !== 1 || !t.clut) throw Error(`${t.name}: not 8-bit CLUT`);
  const tw = t.image.width * 2, th = t.image.height;
  if (tw !== 64 || th !== 128) throw Error(`${t.name}: ${tw}x${th}`);
  for (let y = 0; y < th; y++) for (let x = 0; x < tw; x++) {
    const index = t.image.data[y * tw + x], c = t.clut.data.readUInt16LE(index * 2), o = (y * W + k * 64 + x) * 4;
    rgba[o] = (c & 31) * 255 / 31 | 0; rgba[o + 1] = (c >> 5 & 31) * 255 / 31 | 0; rgba[o + 2] = (c >> 10 & 31) * 255 / 31 | 0; rgba[o + 3] = 255; // drawn raw and opaque
  }
});
const file = png(W, H, rgba);
for (const project of ['unity/XenaUnity', 'unity/XenaLightingVerification']) {
  const dir = path.join(root, project, 'Assets', 'Imported', 'Resources', 'Original'); fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, `Horizon${level}.png`), file);
}
console.log(JSON.stringify({ level, tiles: tiles.map(t => t.name), size: [W, H] }));
