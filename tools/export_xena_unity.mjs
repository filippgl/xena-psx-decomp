#!/usr/bin/env node
// Bridge the verified glTF into a deliberately small, JsonUtility-safe contract.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
const levelMode = process.argv.includes('--level-01');
const sourceDir = levelMode ? 'build/level-assets/01' : 'build/xena-preview';
const assetName = levelMode ? 'Level01' : 'Xena';
const gltf = JSON.parse(fs.readFileSync(`${sourceDir}/${levelMode ? 'level-assembled' : 'xena-original'}.gltf`));
const binary = fs.readFileSync(`${sourceDir}/${gltf.buffers[0].uri}`);
const out = `unity/XenaUnity/Assets/Imported/${assetName}`;
const components = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4 };
function read(index) {
  const accessor = gltf.accessors[index], view = gltf.bufferViews[accessor.bufferView];
  if (accessor.componentType !== 5126) throw Error('Only verified float accessors supported');
  const count = accessor.count * components[accessor.type], at = (view.byteOffset || 0) + (accessor.byteOffset || 0);
  if (!Number.isInteger(count) || at + count * 4 > binary.length || count * 4 > view.byteLength) throw Error('Accessor outside buffer');
  return Array.from({ length: count }, (_, i) => binary.readFloatLE(at + i * 4));
}
const reflectVectors = values => values.map((n, i) => i % 3 === 2 ? -n : n);
const reflectQuaternions = values => values.map((n, i) => i % 4 < 2 ? -n : n);
const parents = new Array(gltf.nodes.length).fill(-1);
gltf.nodes.forEach((node, i) => (node.children || []).forEach(child => {
  if (parents[child] !== -1) throw Error('Multiple parents'); parents[child] = i;
}));
const nodes = gltf.nodes.map((node, i) => ({ name: node.name, parent: parents[i], mesh: node.mesh ?? -1,
  translation: reflectVectors(node.translation || [0, 0, 0]), rotation: reflectQuaternions(node.rotation || [0, 0, 0, 1]),
  scale: node.scale || [1, 1, 1] }));
const meshes = gltf.meshes.map(mesh => ({ name: mesh.name, primitives: mesh.primitives.map(p => {
  if (p.mode !== 4) throw Error('Triangles only');
  const positions = reflectVectors(read(p.attributes.POSITION)), normals = reflectVectors(read(p.attributes.NORMAL));
  const uv = p.attributes.TEXCOORD_0 === undefined ? [] : read(p.attributes.TEXCOORD_0).map((n, i) => i % 2 ? 1 - n : n);
  const indices = Array.from({ length: positions.length / 3 }, (_, i) => i - i % 3 + [0, 2, 1][i % 3]);
  const lightingRgb = p.attributes._XENA_LGT === undefined ? [] : read(p.attributes._XENA_LGT);
  if (lightingRgb.some(n => !Number.isInteger(n) || n < 0 || n > 255)) throw Error('Invalid source LGT byte');
  return { material: p.material, positions, normals, uv, indices, lightingRgb };
}) }));
const materials = gltf.materials.map(m => ({ name: m.name, doubleSided: m.doubleSided,
  texture: m.pbrMetallicRoughness.baseColorTexture?.index ?? -1,
  color: m.pbrMetallicRoughness.baseColorFactor || [1, 1, 1, 1], vertexLighting: levelMode,
  semiTransparent: !!m.extras?.ps1SemiTransparent, blendMode: m.extras?.ps1BlendMode ?? 0 }));
const clips = (gltf.animations || []).map(animation => ({ name: animation.name, slot: animation.extras.originalSlot,
  sourceFrameCount: animation.extras.sourceFrameCount, stepsPerSecond: animation.extras.assumedStepsPerSecond,
  times: read(animation.samplers[0].input), channels: animation.channels.map(channel => {
    const sampler = animation.samplers[channel.sampler];
    if (sampler.interpolation !== 'STEP') throw Error('Only sampled STEP channels supported');
    const source = read(sampler.output);
    return { node: channel.target.node, property: channel.target.path,
      components: components[gltf.accessors[sampler.output].type],
      values: channel.target.path === 'translation' ? reflectVectors(source) :
        channel.target.path === 'rotation' ? reflectQuaternions(source) : source };
  }) }));
fs.mkdirSync(out, { recursive: true });
const textures = gltf.images.map((image, i) => {
  const original = path.basename(image.uri), upscaled = `page-${i}-4x.png`;
  fs.copyFileSync(`${sourceDir}/${original}`, `${out}/${original}`);
  const hasUpscale = fs.existsSync(`${sourceDir}/${upscaled}`);
  if (hasUpscale) fs.copyFileSync(`${sourceDir}/${upscaled}`, `${out}/${upscaled}`);
  const mask=`page-${i}-stp.png`;
  if(levelMode && fs.existsSync(`${sourceDir}/${mask}`))fs.copyFileSync(`${sourceDir}/${mask}`,`${out}/${mask}`);
  return { original, upscaled: hasUpscale ? upscaled : '', semiTransparentMask: levelMode ? mask : '' };
});
const document = { formatVersion: 1, source: 'SLUS-00977 verified asset export',
  sourceBufferSha256: crypto.createHash('sha256').update(binary).digest('hex'),
  unitsPerMeter: levelMode ? 400 : gltf.nodes[0].extras.unitsPerMeter,
  conventions: levelMode ? 'Unity left-handed Y-up. Original static placement snapshot, scale400 assumed. Original LGT RGB bytes, independent brightness; PS1 pixel parity, billboard behavior and STP blending not reproduced.' :
    'Unity left-handed Y-up: reflect glTF Z, reverse triangles, invert V. Physical scale and 30 steps/sec are preview assumptions.',
  nodes, meshes, materials, textures, clips };
fs.writeFileSync(`${out}/${assetName}.xenamodel`, JSON.stringify(document));
if (levelMode) {
  fs.copyFileSync(`${sourceDir}/terrain-unity.json`, `${out}/Terrain.json`);
  fs.copyFileSync(`${sourceDir}/lighting-source.json`, `${out}/Lighting.json`);
  fs.copyFileSync(`${sourceDir}/flags-source.json`, `${out}/Flags.json`);
  fs.copyFileSync(`${sourceDir}/water-source.json`, `${out}/Water.json`);
  fs.copyFileSync(`${sourceDir}/spawns-source.json`, `${out}/Spawns.json`);
}
const report = { nodes: nodes.length, meshes: meshes.length, materials: materials.length, clips: clips.length,
  frames: clips.reduce((n, c) => n + c.sourceFrameCount, 0), output: `${out}/${assetName}.xenamodel`, unityEditorTested: false };
fs.mkdirSync('build/unity-import', { recursive: true });
fs.writeFileSync(`build/unity-import/${levelMode ? 'level-export-report' : 'export-report'}.json`, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
