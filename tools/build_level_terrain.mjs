#!/usr/bin/env node
// Build level01 terrain from original archive and executable; no RAM input.
import fs from 'node:fs';
import assert from 'node:assert/strict';
import { readTmd } from './lib/xena_model.mjs';
import { originalDistance, subdivideStaticMesh, buildTerrainGrid } from './lib/xena_subdivision.mjs';
const out = 'build/level-assets/01';
const manifest = JSON.parse(fs.readFileSync('build/asset-audit/manifest.json'));
const archive = fs.readFileSync('disc/files/XENA.UDA'), exe = fs.readFileSync('xena.exe');
const entry = manifest.entries.find(e => e.candidates.some(c => c.name === ['DATA', 'LEVELS', '01', 'LEVEL.TMD'].join('\\')));
assert(entry?.inArchive);
const at = (entry.lba - manifest.summary.archiveLba) * 2048;
const source = readTmd(archive.subarray(at, at + entry.size), { allowSemiTransparent: true })[1];
// Loader 8006287C: 40-byte level config record at 800C18E4, short+6.
const radiusLimit = exe.readInt16LE(0x800c18e4 - 0x80010000 + 0x800 + 6);
const leaves = subdivideStaticMesh(source, { radiusLimit, distance: originalDistance(exe) });
const { terrain, sourceFaces } = buildTerrainGrid(leaves);
fs.mkdirSync(out, { recursive: true });
fs.writeFileSync(`${out}/terrain-archive.json`, JSON.stringify({ ...terrain,
  note: 'Reconstructed from original LEVEL.TMD, original executable distance table and level config. Native RAM is only used by the separate verifier.' }));
fs.writeFileSync(`${out}/subdivision-source.json`, JSON.stringify({ radiusLimit, leaves, sourceFaces }));
const cellStarts = [0], cellTriangles = [];
for (const cell of terrain.cells) { cellTriangles.push(...cell); cellStarts.push(cellTriangles.length); }
fs.writeFileSync(`${out}/terrain-unity.json`, JSON.stringify({ formatVersion: 1, origin: terrain.origin,
  cellStarts, cellTriangles, vertices: terrain.triangles.flat(2) }));
console.log(`Built archive terrain: ${leaves.length} chunks, ${terrain.triangles.length} triangles, ${terrain.cells.length} cells; radius ${radiusLimit}.`);
