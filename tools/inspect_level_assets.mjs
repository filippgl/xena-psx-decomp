#!/usr/bin/env node
// Static resource inspection only. Object placement and collisions are not inferred.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { readTmd, readStm, buildVram, texturePage } from './lib/xena_model.mjs';
import { readLgt } from './lib/xena_level.mjs';
import { png } from './lib/png.mjs';
import './preview/xena-renderer.js';

const option = process.argv.indexOf('--level');
const level = option < 0 ? 1 : Number(process.argv[option + 1]);
if (!Number.isInteger(level) || level < 1 || level > 30) throw Error('Expected --level 1..30');
const label = String(level).padStart(2, '0'), out = `build/level-assets/${label}`;
const manifest = JSON.parse(fs.readFileSync('build/asset-audit/manifest.json'));
const archive = fs.readFileSync('disc/files/XENA.UDA');
const resources = [];
function resource(leaf) {
  const name = ['DATA', 'LEVELS', label, leaf].join('\\');
  const hits = manifest.entries.filter(e => e.candidates.some(c => c.name === name));
  if (hits.length !== 1 || !hits[0].inArchive) throw Error(`Missing or ambiguous resource ${name}`);
  const entry = hits[0], at = (entry.lba - manifest.summary.archiveLba) * 2048;
  const bytes = archive.subarray(at, at + entry.size);
  const sha256 = crypto.createHash('sha256').update(bytes).digest('hex');
  if (sha256 !== entry.sha256) throw Error(`Resource changed: ${name}`);
  resources.push({ name, index: entry.index, bytes: bytes.length, sha256 });
  return bytes;
}
const objects = readTmd(resource('LEVEL.TMD'), { allowSemiTransparent: true });
const lighting = readLgt(resource('LEVEL.LGT'), objects.map(o => o.vertices.length));
objects.forEach((object, index) => { object.lightingRgb = lighting[index].map(color => color.slice(0, 3)); });
const stm = readStm(resource('LEVEL.STM')), { vram, populated } = buildVram(stm);
fs.mkdirSync(out, { recursive: true });
const textures = [], textureKeys = new Map(), materials = [], materialKeys = new Map();
let missingUvCorners = 0, missingClutCorners = 0, reconstructedZeroNormals = 0;
function material(face) {
  const textureKey = `${face.page}:${face.clut}`;
  if (!textureKeys.has(textureKey)) {
    const image = texturePage(vram, face.page, face.clut);
    const index = textures.length, filename = `page-${index}.png`;
    fs.writeFileSync(path.join(out, filename), png(image.width, image.height, image.rgba));
    textures.push({ page: face.page, clut: face.clut, filename, rgba: image.rgba });
    textureKeys.set(textureKey, index);
  }
  const texture = textureKeys.get(textureKey);
  face.material = texture;
  const depth = (face.page >>> 7) & 3, px = (face.page & 15) * 64, py = (face.page & 16) * 16;
  for (const [u, v] of face.uv) {
    const x = px + (u >>> (2 - depth)), y = py + v;
    if (x >= 1024 || y >= 512 || !populated[y * 1024 + x]) { missingUvCorners++; continue; }
    if (depth < 2) {
      const word = vram[y * 1024 + x];
      const color = depth === 0 ? (word >>> ((u & 3) * 4)) & 15 : (word >>> ((u & 1) * 8)) & 255;
      if (!populated[(face.clut >>> 6) * 1024 + (face.clut & 63) * 16 + color]) missingClutCorners++;
    }
  }
  const key = `${texture}:${face.doubleSided}:${!!face.semiTransparent}`;
  if (!materialKeys.has(key)) {
    const index = materials.length;
    materials.push({ name: `page-${texture}${face.doubleSided ? '-double' : ''}${face.semiTransparent ? '-ps1-blend' : ''}`,
      pbrMetallicRoughness: { baseColorTexture: { index: texture }, metallicFactor: 0, roughnessFactor: 1 },
      doubleSided: face.doubleSided, alphaMode: 'MASK', alphaCutoff: 0.5,
      extensions: { KHR_materials_unlit: {} },
      extras: { sourceTPage: face.page, sourceClut: face.clut,
        ps1SemiTransparent: !!face.semiTransparent, ps1BlendMode: (face.page >>> 5) & 3,
        note: 'PS1 per-texel STP blending is retained as metadata, not reproduced by this diagnostic material.' } });
    materialKeys.set(key, index);
  }
  return materialKeys.get(key);
}
const views = [], accessors = [], chunks = [];
let byteLength = 0;
function accessor(values, components, type, bounds = false) {
  const buffer = Buffer.alloc(values.length * 4);
  for (let i = 0; i < values.length; i++) buffer.writeFloatLE(values[i], i * 4);
  const view = views.length;
  views.push({ buffer: 0, byteOffset: byteLength, byteLength: buffer.length });
  chunks.push(buffer); byteLength += buffer.length;
  const entry = { bufferView: view, componentType: 5126, count: values.length / components, type };
  if (bounds) {
    entry.min = Array(components).fill(Infinity); entry.max = Array(components).fill(-Infinity);
    for (let i = 0; i < values.length; i++) {
      const axis = i % components;
      entry.min[axis] = Math.min(entry.min[axis], values[i]); entry.max[axis] = Math.max(entry.max[axis], values[i]);
    }
  }
  accessors.push(entry); return accessors.length - 1;
}
const objectSummaries = [];
const meshes = objects.map(object => {
  const groups = new Map();
  for (const face of object.faces) {
    if (!face.uv) throw Error('Level exporter currently requires textured triangles');
    const index = material(face);
    if (!groups.has(index)) groups.set(index, { positions: [], normals: [], uv: [], lighting: [] });
    const group = groups.get(index);
    const points = face.indices.map(i => object.vertices[i]);
    const a = points[2].map((n, axis) => n - points[0][axis]), b = points[1].map((n, axis) => n - points[0][axis]);
    const cross = [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
    const fallback = Math.hypot(...cross) ? cross : [0, 1, 0];
    for (const corner of [0, 2, 1]) {
      group.positions.push(...object.vertices[face.indices[corner]].map(n => n / 400));
      group.lighting.push(...object.lightingRgb[face.indices[corner]]);
      let normal = object.normals[face.normals[corner]], length = Math.hypot(...normal);
      if (!length) { normal = fallback; length = Math.hypot(...normal); reconstructedZeroNormals++; }
      group.normals.push(...normal.map(n => n / length));
      group.uv.push(...face.uv[corner].map(n => (n + 0.5) / 256));
    }
  }
  const bounds = [0, 1, 2].map(axis => [Math.min(...object.vertices.map(v => v[axis])), Math.max(...object.vertices.map(v => v[axis]))]);
  objectSummaries.push({ index: object.index, vertices: object.vertices.length, triangles: object.faces.length,
    semiTransparentTriangles: object.faces.filter(f => f.semiTransparent).length, bounds });
  return { name: `level-${label}-object-${object.index}`, primitives: [...groups].map(([material, g]) => ({
    attributes: { POSITION: accessor(g.positions, 3, 'VEC3', true), NORMAL: accessor(g.normals, 3, 'VEC3'), TEXCOORD_0: accessor(g.uv, 2, 'VEC2'),
      _XENA_LGT: accessor(g.lighting, 3, 'VEC3') }, material, mode: 4 })) };
});
const largest = objectSummaries.reduce((a, b) => b.triangles > a.triangles ? b : a);
const nodes = objects.map((o, i) => ({ name: meshes[i].name, mesh: i, rotation: [1, 0, 0, 0] }));
// Rotate 180 degrees about X: same source Y-down -> glTF Y-up convention as Xena.
// Each scene isolates one source object. No guessed dynamic object placement.
const gltf = { asset: { version: '2.0', generator: 'Xena original level resource inspector' },
  extensionsUsed: ['KHR_materials_unlit'], scene: largest.index,
  scenes: objects.map((o, i) => ({ name: meshes[i].name, nodes: [i] })), nodes, meshes, materials,
  textures: textures.map((t, i) => ({ sampler: 0, source: i })), images: textures.map(t => ({ uri: t.filename })),
  samplers: [{ magFilter: 9728, minFilter: 9728, wrapS: 33071, wrapT: 33071 }],
  buffers: [{ uri: 'level.bin', byteLength }], bufferViews: views, accessors,
  extras: { originalUnitsPerMeterAssumed: 400, objectPlacementsRecovered: false, collisionGeometryRecovered: false } };
fs.writeFileSync(path.join(out, 'level.bin'), Buffer.concat(chunks));
fs.writeFileSync(path.join(out, 'level.gltf'), JSON.stringify(gltf, null, 2));
let placements = null, worldBounds = null;
const placementFile = path.join(out, 'placements-environment.json');
if (fs.existsSync(placementFile)) {
  const source = JSON.parse(fs.readFileSync(placementFile));
  if (source.report.result !== 'PASS' || source.report.sourceTmdSha256 !== resources[0].sha256 || source.objects.length !== objects.length)
    throw Error('Placement snapshot does not match current geometry');
  placements = source.objects;
  const assembled = JSON.parse(JSON.stringify(gltf)), binary = Buffer.concat(chunks);
  worldBounds = [[Infinity, -Infinity], [Infinity, -Infinity], [Infinity, -Infinity]];
  for (let mesh = 0; mesh < objects.length; mesh++) {
    const matrix = placements[mesh].matrix;
    for (const primitive of assembled.meshes[mesh].primitives) {
      for (const [attribute, normal] of [['POSITION', false], ['NORMAL', true]]) {
        const entry = assembled.accessors[primitive.attributes[attribute]], view = assembled.bufferViews[entry.bufferView];
        const min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
        for (let vertex = 0; vertex < entry.count; vertex++) {
          const at = view.byteOffset + vertex * 12;
          const point = [0, 4, 8].map(n => binary.readFloatLE(at + n));
          if (!normal) for (let axis = 0; axis < 3; axis++) point[axis] = Math.round(point[axis] * 400);
          let transformed = [0, 1, 2].map(row => matrix.slice(row * 3, row * 3 + 3).reduce((s, coefficient, axis) => s + coefficient * point[axis], 0) / 4096);
          if (normal) { const length = Math.hypot(...transformed) || 1; transformed = transformed.map(n => n / length); }
          else transformed = transformed.map((n, axis) => (Math.floor(n) + matrix[9 + axis]) / 400);
          for (let axis = 0; axis < 3; axis++) {
            binary.writeFloatLE(transformed[axis], at + axis * 4);
            const value = binary.readFloatLE(at + axis * 4);
            min[axis] = Math.min(min[axis], value); max[axis] = Math.max(max[axis], value);
            if (!normal) {
              worldBounds[axis][0] = Math.min(worldBounds[axis][0], value * 400);
              worldBounds[axis][1] = Math.max(worldBounds[axis][1], value * 400);
            }
          }
        }
        if (!normal) { entry.min = min; entry.max = max; }
      }
    }
  }
  assembled.nodes = placements.map((p, i) => ({ name: `${i}-${p.name}`, mesh: i,
    extras: { sourcePosition: p.position, sourceEuler: p.rotation, rawFields34To38: p.fields34To38 } }));
  assembled.nodes.push({ name: 'SourceToYUp', rotation: [1, 0, 0, 0], children: placements.map((_, i) => i) });
  assembled.scenes = [{ name: 'Level01-SourcePlacementSnapshot', nodes: [placements.length] }]; assembled.scene = 0;
  assembled.buffers[0].uri = 'level-assembled.bin';
  assembled.extras.objectPlacementsRecovered = true;
  assembled.extras.note = 'Original placement snapshot. Original integer rotation baked into geometry. Billboard and dynamic behavior are not reproduced.';
  fs.writeFileSync(path.join(out, 'level-assembled.gltf'), JSON.stringify(assembled, null, 2));
  fs.writeFileSync(path.join(out, 'level-assembled.bin'), binary);
}
const primary = objects[largest.index];
let flagRecords = null, water = null, spawns = null;
if (fs.existsSync(path.join(out, 'flags-source.json'))) {
  flagRecords = JSON.parse(fs.readFileSync(path.join(out, 'flags-source.json'))).records;
  if (!placements || flagRecords.length !== objects.length || flagRecords.some((r, i) => r.index !== i || r.name !== placements[i].name))
    throw Error('Flag snapshot does not match level placements');
}
if (fs.existsSync(path.join(out, 'water-source.json'))) water = JSON.parse(fs.readFileSync(path.join(out, 'water-source.json')));
if (fs.existsSync(path.join(out, 'spawns-source.json'))) spawns = JSON.parse(fs.readFileSync(path.join(out, 'spawns-source.json'))).records;
const center = largest.bounds.map(([min, max]) => (min + max) / 2);
const span = Math.max(...largest.bounds.map(([min, max]) => max - min));
const matrices = objects.map(() => [4096, 0, 0, 0, 4096, 0, 0, 0, 4096, 0, 0, 0]);
for (const [name, yaw, pitch] of [['overview', Math.PI + 0.35, 0.8], ['top', 0, Math.PI / 2]]) {
  const image = XenaPreviewRenderer.render({ objects: [primary], textures, center }, matrices, null,
    { width: 1280, height: 960, yaw, pitch, zoom: 1000 / span });
  fs.writeFileSync(path.join(out, `${name}.png`), png(image.width, image.height, image.rgba));
}
const report = { level, resources, objects: objectSummaries, totalTriangles: objects.reduce((n, o) => n + o.faces.length, 0),
  stmImages: stm.images.length, texturePages: textures.length, materialVariants: materials.length,
  lightingVertices: lighting.reduce((n, object) => n + object.length, 0),
  missingUvCorners, missingClutCorners, reconstructedZeroNormals, primaryObject: largest.index,
  originalPlacementSnapshotAvailable: !!placements,
  note: 'Source mesh library, one scene per object, plus optional separately verified assembled snapshot. No dynamic behavior or events. Source zero normals reconstructed geometrically for glTF. PS1 STP blending is metadata only. Scale 400 is an assumption.' };
fs.writeFileSync(path.join(out, 'summary.json'), JSON.stringify(report, null, 2));
fs.writeFileSync(path.join(out, 'level-data.js'), `globalThis.LevelPreviewData=${JSON.stringify({ objects,
  primaryObject: largest.index, bounds: objectSummaries.map(o => o.bounds), placements, worldBounds, flags: flagRecords, water, spawns,
  textures: textures.map(({ rgba, ...texture }) => ({ ...texture, rgbaBase64: Buffer.from(rgba).toString('base64') })) })};\n`);
fs.copyFileSync(fileURLToPath(new URL('./preview/level-viewer.html', import.meta.url)), path.join(out, 'preview.html'));
fs.copyFileSync(fileURLToPath(new URL('./preview/xena-renderer.js', import.meta.url)), path.join(out, 'xena-renderer.js'));
fs.copyFileSync(fileURLToPath(new URL('./preview/xena-water.js', import.meta.url)), path.join(out, 'xena-water.js'));
console.log(JSON.stringify({ out, objects: objects.length, triangles: report.totalTriangles, stmImages: stm.images.length,
  texturePages: textures.length, missingUvCorners, missingClutCorners, primaryObject: largest.index }, null, 2));
