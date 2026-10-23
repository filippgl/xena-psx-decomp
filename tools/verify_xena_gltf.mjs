#!/usr/bin/env node
import fs from 'node:fs';
import assert from 'node:assert/strict';
import { readTrig, worldMatrices, transformPoint } from './lib/xena_transforms.mjs';
import { readPng } from './lib/png.mjs';
const out = 'build/xena-preview';
const gltf = JSON.parse(fs.readFileSync(`${out}/xena-4x.gltf`));
const binary = fs.readFileSync(`${out}/${gltf.buffers[0].uri}`);
const source = fs.readFileSync(`${out}/xena-data.js`, 'utf8');
const data = JSON.parse(source.slice(source.indexOf('=') + 1).trim().replace(/;$/, ''));
const trig = readTrig(fs.readFileSync('xena.exe'));
assert.equal(gltf.asset.version, '2.0'); assert.equal(binary.length, gltf.buffers[0].byteLength);
const components = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4 };
function read(index) {
  const accessor = gltf.accessors[index], view = gltf.bufferViews[accessor.bufferView];
  assert.equal(accessor.componentType, 5126);
  const count = accessor.count * components[accessor.type];
  assert(Number.isInteger(count));
  const start = (view.byteOffset || 0) + (accessor.byteOffset || 0);
  assert.equal(start % 4, 0); assert(start + count * 4 <= view.byteOffset + view.byteLength);
  assert(view.byteOffset + view.byteLength <= binary.length);
  return Array.from({ length: count }, (_, i) => {
    const n = binary.readFloatLE(start + i * 4); assert(Number.isFinite(n)); return n;
  });
}
gltf.accessors.forEach((_, i) => read(i));
for (const image of gltf.images) {
  const texture = readPng(fs.readFileSync(`${out}/${image.uri}`));
  assert.equal(texture.width, 1024); assert.equal(texture.height, 1024);
}
let triangles = 0;
const canonical = triangle => triangle.map(p => p.join(',')).sort().join('|');
for (const object of data.objects) {
  const mesh = gltf.meshes[object.index], actual = [];
  for (const primitive of mesh.primitives) {
    assert.equal(primitive.mode, 4);
    const positions = read(primitive.attributes.POSITION);
    assert.equal(positions.length % 9, 0);
    for (let i = 0; i < positions.length; i += 9) actual.push(canonical([positions.slice(i, i + 3), positions.slice(i + 3, i + 6), positions.slice(i + 6, i + 9)]));
    if (primitive.attributes.TEXCOORD_0 !== undefined) {
      const uv = read(primitive.attributes.TEXCOORD_0);
      assert.equal(uv.length, positions.length * 2 / 3);
      assert(uv.every(n => n >= 0 && n <= 1));
    }
  }
  const expected = object.faces.map(face => canonical(face.indices.map(i => object.vertices[i])));
  assert.deepEqual(actual.sort(), expected.sort(), `Mesh topology differs for ${object.index}`);
  triangles += actual.length;
}
const parents = new Array(gltf.nodes.length).fill(-1);
gltf.nodes.forEach((node, parent) => {
  for (const child of node.children || []) { assert.equal(parents[child], -1, 'Multiple parents'); parents[child] = parent; }
});
for (let i = 0; i < 33; i++) assert.equal(parents[i + 1], data.parents[i] === -1 ? 0 : data.parents[i] + 1);
function rotation(q) {
  const [x, y, z, w] = q;
  assert(Math.abs(Math.hypot(...q) - 1) < 1e-6, 'Invalid quaternion');
  return [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w),
    2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
    2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)];
}
let frames = 0, vertices = 0, maxPositionError = 0;
for (const animation of gltf.animations) {
  const slot = animation.extras.originalSlot;
  const clip = JSON.parse(fs.readFileSync(`build/animation-audit/slot-${slot.toString(16).padStart(2, '0')}.json`));
  const channels = animation.channels.map(channel => {
    const sampler = animation.samplers[channel.sampler];
    assert.equal(sampler.interpolation, 'STEP');
    const times = read(sampler.input); assert.equal(times.length, clip.frameCount + 1);
    times.forEach((t, i) => assert(Math.abs(t - i / 30) < 1e-5));
    const values = read(sampler.output), count = components[gltf.accessors[sampler.output].type];
    assert.equal(values.length, times.length * count);
    return { ...channel.target, values, count };
  });
  for (let frame = 0; frame < clip.frameCount; frame++) {
    const translations = {}, rotations = {}, scales = {};
    for (const channel of channels) {
      const value = channel.values.slice(frame * channel.count, (frame + 1) * channel.count);
      if (channel.path === 'translation') translations[channel.node] = value;
      else if (channel.path === 'rotation') rotations[channel.node] = rotation(value);
      else if (channel.path === 'scale') scales[channel.node] = value;
      else throw Error('Unexpected animation target');
    }
    const matrices = new Array(33);
    function resolve(part) {
      if (matrices[part]) return matrices[part];
      const node = part + 1, parent = parents[node] - 1;
      const r = rotations[node], t = translations[node]; assert(r && t);
      if (parent === -1) return matrices[part] = { rotation: r, translation: t };
      const p = resolve(parent);
      const composed = Array.from({ length: 9 }, (_, i) => {
        const row = Math.floor(i / 3), col = i % 3;
        return [0, 1, 2].reduce((s, k) => s + p.rotation[row * 3 + k] * r[k * 3 + col], 0);
      });
      const position = p.translation.map((v, row) => v + [0, 1, 2].reduce((s, k) => s + p.rotation[row * 3 + k] * t[k], 0));
      return matrices[part] = { rotation: composed, translation: position };
    }
    const fixed = worldMatrices(clip.frames[frame].parts, trig);
    for (const object of data.objects) {
      const m = resolve(object.index);
      for (const point of object.vertices) {
        const expected = transformPoint(fixed[object.index], point);
        const actual = m.translation.map((v, row) => v + m.rotation.slice(row * 3, row * 3 + 3).reduce((s, n, k) => s + n * point[k], 0));
        maxPositionError = Math.max(maxPositionError, Math.hypot(...actual.map((v, axis) => v - expected[axis]))); vertices++;
      }
      const geometryNode = gltf.nodes.findIndex(n => n.mesh === object.index);
      const scale = scales[geometryNode] || gltf.nodes[geometryNode].scale || [1, 1, 1];
      assert.equal(scale[0] !== 0, clip.frames[frame].visibility[object.index], 'Visibility track differs');
    }
    frames++;
  }
}
assert(maxPositionError < 2, `Excessive float conversion error: ${maxPositionError}`);
const unitsPerMeter = gltf.nodes[0].extras.unitsPerMeter;
const result = { result: 'PASS', animations: gltf.animations.length, frames, triangles, comparedVertices: vertices,
  maxPositionErrorSourceUnits: maxPositionError, maxPositionErrorAtExportScaleMeters: maxPositionError / unitsPerMeter,
  unitsPerMeter, physicalScale: '400 is a configurable preview assumption, not a measured game scale.',
  checks: 'Buffer/accessor ranges, PNG pages, triangle topology, hierarchy, quaternion normalization, every sampled vertex and visibility track. Not a Khronos conformance validator or Unity import test.' };
fs.writeFileSync(`${out}/gltf-verification.json`, JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify(result, null, 2));
