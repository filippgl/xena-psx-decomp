#!/usr/bin/env node
// Export the original per-frame weapon CONTACT POINTS of Xena and the level-01 bandit.
//
// Original chain (docs/xena-contact-points.md):
//   class descriptor (0x800C33E4 + class*0x44) +0x3C -> list of 16-byte entries
//     { s16 part, s16 pad, s32 ox, s32 oy, s32 oz }  terminated by part == -1
//   func_8006A6E8 (once per game step, stamp actor+0x134 = D_800ECEA0):
//     swap actor+0x12C (current) / +0x130 (previous), then for every entry
//     point = M[part].T - M[part].R * (ox, oy, oz)        (ApplyMatrixLV, Q12)
//     (if ox == 0x13880 the point is instead M.T + M.R * vertex[oy] of the part; unused by these classes)
//   M[] = func_80014DE4 hierarchy (Ry*Rx(-x)*Rz, tools/lib/xena_transforms.mjs) with the root record
//   holding the actor position and rotation, external matrix D_800F0790 = identity.
// Here the root record is replaced by identity, so the points are in the attacker's local frame
// (PSX units, y down, origin = actor position = feet).
//
//   node tools/export_contact_points.mjs [--root DECOMP] [--out FILE]
//   node tools/export_contact_points.mjs [--root DECOMP] --verify RAMDIR [--actor xena|0xADDR] [--from N --to N]
//
// Inputs (read-only): ROOT/xena.exe, ROOT/build/asset-audit/manifest.json, ROOT/disc/files/XENA.UDA.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i < 0 ? fallback : argv[i + 1]; };
const root = path.resolve(option('--root', '.'));
const libDir = [path.join(root, 'tools', 'lib'), path.join(path.dirname(fileURLToPath(import.meta.url)), 'lib')]
  .find(d => fs.existsSync(path.join(d, 'xena_animation.mjs')));
const lib = name => import(pathToFileURL(path.join(libDir, name)).href);
const { readSmi, decodeSma } = await lib('xena_animation.mjs');
const { readTrig, worldMatrices } = await lib('xena_transforms.mjs');

const exe = fs.readFileSync(path.join(root, 'xena.exe'));
const BASE = 0x8000F800;
const u32 = a => exe.readUInt32LE(a - BASE), s16 = a => exe.readInt16LE(a - BASE), s32 = a => exe.readInt32LE(a - BASE);
const trig = readTrig(exe);
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'build/asset-audit/manifest.json')));
const uda = fs.readFileSync(path.join(root, 'disc/files/XENA.UDA'));
function resource(name) {
  const hits = manifest.entries.filter(e => e.candidates.some(c => c.name === name));
  if (hits.length !== 1 || !hits[0].inArchive) throw Error(`Missing or ambiguous ${name}`);
  const at = (hits[0].lba - manifest.summary.archiveLba) * 2048;
  return uda.subarray(at, at + hits[0].size);
}

// Words read back from xena.exe for the important constants (verified below, values from the asm listing).
const WORDS = { 0x8006A784: 0x3C030001, 0x8006A788: 0x34633880, 0x80071188: 0x24420032,
  0x8003CF7C: 0x2A020005, 0x8003CF80: 0x2A020007 };
const wordMismatch = Object.entries(WORDS).filter(([a, w]) => u32(Number(a)) !== w)
  .map(([a, w]) => `${Number(a).toString(16)}: ${u32(Number(a)).toString(16)} != ${w.toString(16)}`);
if (wordMismatch.length) throw Error(`xena.exe does not match the audited code: ${wordMismatch.join(', ')}`);

function descriptor(cls) {
  const d = 0x800C33E4 + cls * 0x44, list = u32(d + 0x3C), points = [];
  for (let e = list; list && s16(e) !== -1; e += 16)
    points.push({ part: s16(e), offset: [s32(e + 4), s32(e + 8), s32(e + 12)] });
  if (points.some(p => p.offset[0] === 0x13880)) throw Error('Vertex-mode contact point not supported');
  return { address: d, list, radius: s16(d + 0x28), height: s16(d + 0x2A), points };
}

const round = v => Math.round(v * 1000) / 1000;
const q12 = (r, v) => [0, 1, 2].map(row => {
  // ApplyMatrixLV: 32-bit vector, Q12 matrix; result truncated toward -inf per row (>>12).
  const sum = r[row * 3] * v[0] + r[row * 3 + 1] * v[1] + r[row * 3 + 2] * v[2];
  return Math.floor(sum / 4096);
});
function contactPoints(matrices, points) {
  return points.map(p => { const m = matrices[p.part], rv = q12(m.rotation, p.offset);
    return m.translation.map((t, i) => t - rv[i]); });
}

// Damage windows (docs/xena-damage-audit.md 1.2 / 2.3): +0xFE is live on frames start+1..end.
const XENA_WINDOWS = [
  [0x51, 30, 4, 10], [0x52, 40, 2, 10], [0x3A, 45, 11, 18], [0x53, 75, 11, 18], [0x4F, 80, 13, 26],
  [0x55, 50, 7, 16], [0x61, -35, 3, 12], [0x62, -35, 0, 10], [0x62, -40, 10, 18], [0x5A, -70, 0, 16],
  [0x56, -35, 0, 3], [0x46, -70, 14, 20], [0x5C, -60, 14, 38], [0x3E, 30, 0, 36], [0x3C, 15, 1, 40]];
const BANDIT_WINDOWS = [[0x3D, 25, 11, 20], [0x3E, 15, 7, 13], [0x3F, 15, 8, 15], [0x50, 15, 9, 16],
  [0x3C, 30, 12, 20], [0x40, 15, 13, 23]];

const ACTORS = {
  xena: { cls: 0x5A, tmd: 'DATA\\LEVELS\\XENA.TMD', smi: 'DATA\\LEVELS\\XENA.SMI', sma: 'DATA\\LEVELS\\XENA.SMA',
    windows: XENA_WINDOWS,
    // func_800367BC 0x8003CE38..0x8003CF80: d >= 0 -> points 0..4, d < 0 -> points 5..6.
    pointSet: d => d >= 0 ? [0, 1, 2, 3, 4] : [5, 6] },
  bandit: { cls: 0x95, tmd: 'DATA\\LEVELS\\01\\BANDIT.TMD', smi: 'DATA\\LEVELS\\01\\BANDIT.SMI',
    sma: 'DATA\\LEVELS\\01\\BANDIT.SMA', windows: BANDIT_WINDOWS,
    // func_8001B460 0x8001F9D8..0x8001FB44: every point of the list.
    pointSet: () => [0, 1, 2, 3, 4, 5, 6],
    // Upper-body aim twist, func_8001B460 0x8001F72C..0x8001F9AC: part D_800C0158[family*2] rotY += actor+0x114,
    // part D_800C015A[family*2] rotY += actor+0x11E (family = actor+0x38; bandit family 0).
    twistTable: 0x800C0158 },
};

function load(actor) {
  const a = ACTORS[actor], desc = descriptor(a.cls);
  const tmd = resource(a.tmd), parts = readSmi(resource(a.smi), tmd.readUInt32LE(8));
  const rootIndex = parts.findIndex(p => p.parent === -1);
  const clips = new Map(decodeSma(resource(a.sma), parts).map(c => [c.slot, c]));
  const twistParts = a.twistTable ? [s16(a.twistTable), s16(a.twistTable + 2)] : [];
  const ancestors = i => { const c = []; for (; i !== -1; i = parts[i].parent) c.push(i); return c; };
  return { ...a, desc, parts, rootIndex, clips, twistParts, ancestors };
}
// Pose of SMA frame k (0-based) with the root record replaced: identity -> attacker-local frame.
// twist: [rotY added to twistParts[0], rotY added to twistParts[1]] (4096 units), as the game adds them.
function framePoints(model, clip, k, rootRecord = { translation: [0, 0, 0], rotation: [0, 0, 0] }, twist = null) {
  const parts = structuredClone(clip.frames[k].parts);
  parts[model.rootIndex] = { ...parts[model.rootIndex], ...rootRecord };
  if (twist) model.twistParts.forEach((p, i) => { parts[p].rotation[1] = ((parts[p].rotation[1] + twist[i]) << 16) >> 16; });
  return contactPoints(worldMatrices(parts, trig), model.desc.points);
}
// Pivots that let a port apply the twist to the exported local points without the skeleton:
// a rotY of delta on part P rotates every point below P about axis = M[parent(P)].R * (0,1,0) through M[P].T.
function twistPivots(model, clip, k) {
  if (!model.twistParts.length) return undefined;
  const parts = structuredClone(clip.frames[k].parts);
  parts[model.rootIndex] = { ...parts[model.rootIndex], translation: [0, 0, 0], rotation: [0, 0, 0] };
  const m = worldMatrices(parts, trig);
  return model.twistParts.map(p => { const r = m[model.parts[p].parent].rotation;
    return { part: p, origin: [...m[p].translation], axis: [r[1], r[4], r[7]].map(v => round(v / 4096)) }; });
}
function rotateAbout(point, origin, axis, angle) {
  const [ux, uy, uz] = axis, n = Math.hypot(ux, uy, uz), a = [ux / n, uy / n, uz / n];
  const v = point.map((x, i) => x - origin[i]), c = Math.cos(angle), s = Math.sin(angle);
  const dot = a[0] * v[0] + a[1] * v[1] + a[2] * v[2];
  const cr = [a[1] * v[2] - a[2] * v[1], a[2] * v[0] - a[0] * v[2], a[0] * v[1] - a[1] * v[0]];
  return v.map((x, i) => origin[i] + x * c + cr[i] * s + a[i] * dot * (1 - c));
}
// Port-side reconstruction: exported local point, then twist pivots (descendant first), then pos + Ry(rotY).
function portPoints(model, clip, k, rec, twist) {
  let local = framePoints(model, clip, k);
  if (twist) {
    const pivots = twistPivots(model, clip, k);
    const order = [...pivots.keys()].sort((i, j) => model.ancestors(pivots[j].part).length - model.ancestors(pivots[i].part).length);
    local = local.map((p, i) => { let q = p; const chain = model.ancestors(model.desc.points[i].part);
      for (const j of order) if (twist[j] && chain.includes(pivots[j].part))
        q = rotateAbout(q, pivots[j].origin, pivots[j].axis, twist[j] * Math.PI / 2048);
      return q; });
  }
  const th = rec.rotation[1] * Math.PI / 2048, c = Math.cos(th), s = Math.sin(th), o = rec.translation;
  return local.map(([lx, ly, lz]) => [o[0] + c * lx + s * lz, o[1] + ly, o[2] - s * lx + c * lz]);
}

function exportAll() {
  const result = { source: 'SLUS-00977 xena.exe + XENA.UDA; tools/export_contact_points.mjs',
    units: 'PSX world units (project convention 400 = 1 m, not measured)',
    frame: 'attacker local: origin = actor position (feet, the root record translation); PSX axes, y down; ' +
      'facing = local -z (INFERRED, see doc); world = rootPos + Ry(rotY) * local (tools/lib/xena_transforms.mjs)',
    unityLocal: 'Unity importer chain (x, -y, z) / 400 metres (export_xena_gltf scale (1,-1,-1)/400 then Z reflection)',
    frameIndex: 'k = SMA frame k (0-based), counter = k + 1 = actor clip counter +0xE6 in the step that uses it; ' +
      'damage/active = the window live at that counter (verified on native RAM dumps)',
    rootTiming: 'the root record (position, rotY) used is the one from BEFORE the movement of this step (previous step)',
    twist: 'bandit only: part twistParts[1] (=3) rotY += actor+0x11E before the points are computed (part 4 += +0x114 ' +
      'affects no point); apply with twistPivots: rotate points whose chain contains the part about axis through origin',
    contactTest: 'func_80070DFC: see doc section 2', actors: {} };
  for (const name of Object.keys(ACTORS)) {
    const model = load(name), clipsOut = [];
    const slots = [...new Set(model.windows.map(w => w[0]))];
    for (const slot of slots) {
      const clip = model.clips.get(slot);
      if (!clip) throw Error(`${name}: clip ${slot.toString(16)} missing`);
      const windows = model.windows.filter(w => w[0] === slot).map(([, damage, start, end]) =>
        ({ damage, start, end, liveFrames: [start + 1, end], points: model.pointSet(damage) }));
      const frames = clip.frames.map((_, k) => {
        const counter = k + 1, live = windows.find(w => counter >= w.liveFrames[0] && counter <= w.liveFrames[1]);
        const pts = framePoints(model, clip, k);
        const out = { k, counter, damage: live ? live.damage : 0, active: live ? live.points : [],
          points: pts.map(p => p.map(round)) };
        const pivots = twistPivots(model, clip, k);
        if (pivots) out.twistPivots = pivots;
        return out;
      });
      clipsOut.push({ slot, hex: slot.toString(16).toUpperCase(), frameCount: clip.frameCount, windows, frames });
    }
    result.actors[name] = { class: model.cls, descriptor: `0x${model.desc.address.toString(16)}`,
      list: `0x${model.desc.list.toString(16)}`, radius: model.desc.radius, height: model.desc.height,
      rootPart: model.rootIndex,
      points: model.desc.points.map(p => ({ ...p, chain: model.ancestors(p.part) })),
      twistParts: model.twistParts.length ? model.twistParts : undefined, clips: clipsOut };
  }
  return result;
}

// --verify: compare stored current points (actor+0x12C) in native RAM dumps with the export.
function verify(dir) {
  const actorArg = option('--actor', 'xena'), from = Number(option('--from', 0)), to = Number(option('--to', 1e9));
  const files = fs.readdirSync(dir).filter(f => /^ram_\d+\.bin$/.test(f)).sort();
  const rows = [], prevRoot = new Map();
  for (const f of files) {
    const t = Number(f.slice(4, -4)); if (t < from || t > to) continue;
    const b = fs.readFileSync(path.join(dir, f)), at = a => (a >>> 0) & 0x1FFFFFFF;
    const U = a => b.readUInt32LE(at(a)), I = a => b.readInt32LE(at(a)), H = a => b.readInt16LE(at(a));
    const x = actorArg === 'xena' ? U(0x800EE174) : Number(actorArg);
    const cls = I(x), name = cls === 0x5A ? 'xena' : (cls >= 0x95 && cls <= 0x98 ? 'bandit' : null);
    if (!name) continue;
    const model = models[name] ??= load(name);
    const state = I(x + 0xD0), counter = H(x + 0xE6), damage = H(x + 0xFE), clip = model.clips.get(state);
    if (!clip || counter < 1 || counter > clip.frameCount) continue;
    const inst = U(x + 0xA8), recs = U(inst + 0x14), r = recs + model.rootIndex * 20;
    const rootRec = { translation: [I(r), I(r + 4), I(r + 8)], rotation: [H(r + 12), H(r + 14), H(r + 16)] };
    // The points are computed before this step's mover runs: use the root record of the previous dump
    // (one game step earlier) when it is the same actor and the step stamp advanced by exactly one.
    const stamp = U(x + 0x134), prev = prevRoot.get(x);
    prevRoot.set(x, { stamp, rec: rootRec });
    const useRec = prev && prev.stamp === stamp - 1 ? prev.rec : null;
    if (!useRec) continue;
    const cur = U(x + 0x12C);
    const stored = model.desc.points.map((_, i) => [I(cur + 12 * i), I(cur + 12 * i + 4), I(cur + 12 * i + 8)]);
    // RAM part records as a pose check (non-root parts): how far the in-game pose is from SMA frame k.
    const twist = model.twistParts.length ? [H(x + 0x114), H(x + 0x11E)] : null;
    const row = { t, actor: name, state: state.toString(16), counter, damage, rootRec: useRec, twist };
    for (const k of [counter - 2, counter - 1, counter]) {
      if (k < 0 || k >= clip.frameCount) continue;
      const exact = framePoints(model, clip, k, useRec, twist);
      // What a port does with the JSON: local point (+ twist pivots) then pos + Ry(rotY), in floats.
      const rigid = portPoints(model, clip, k, useRec, twist);
      const err = pts => pts.map((p, i) => Math.hypot(...p.map((v, a) => v - stored[i][a])));
      row[`k${k - counter}`] = { exact: err(exact).map(round), rigid: err(rigid).map(round) };
    }
    row.stored = stored;
    rows.push(row);
  }
  return rows;
}
const models = {};

if (option('--verify', null)) {
  const rows = verify(option('--verify'));
  for (const r of rows) {
    const fmt = e => e ? `exact max ${Math.max(...e.exact)} rigid max ${round(Math.max(...e.rigid))}` : '-';
    console.log(`${r.t} ${r.actor} st${r.state} fr${r.counter} d${r.damage} rotY${r.rootRec.rotation[1]}${r.twist ? ` tw${r.twist}` : ''} | k=n-2 ${fmt(r['k-2'])} | k=n-1 ${fmt(r['k-1'])} | k=n ${fmt(r.k0)}`);
  }
  if (option('--json', null)) fs.writeFileSync(option('--json'), JSON.stringify(rows, null, 1) + '\n');
} else {
  const outFile = path.resolve(option('--out', path.join(root, 'build', 'contact-points', 'contact_points.json')));
  const data = exportAll();
  fs.mkdirSync(path.dirname(outFile), { recursive: true });
  fs.writeFileSync(outFile, JSON.stringify(data) + '\n');
  const n = Object.values(data.actors).reduce((s, a) => s + a.clips.reduce((t, c) => t + c.frames.length, 0), 0);
  console.log(`wrote ${outFile}: ${Object.keys(data.actors).length} actors, ${n} frames`);
}
