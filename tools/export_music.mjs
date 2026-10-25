#!/usr/bin/env node
// Convert the level sound set (Sony libsnd VAB banks + SEP music) to WAV / MIDI.
//
// Game side (SLUS-00977, docs/xena-ambient-audit.md): func_80086DCC(set) reads sound-set table
// D_800EE550[set] = { music index | -1, bank indices..., -1 }. Music table D_800CC37C (8 bytes:
// name*, u16 sequence count) -> "DATA\SND\%s.sep" opened with SsSepOpenJ and played with SsSepPlay
// (play mode 1, l_count 0). Bank names D_800EE478[] ("core" is always bank 0); each is a .VH/.VB VAB.
//
// Formats (standard PsyQ libsnd; layout checked against the file sizes below, see --info):
//   VH  'pBAV' v7: 32-byte header, 128 x 16-byte program attrs, ps x 16 x 32-byte tone attrs,
//       256 x u16 VAG sizes (>> 3). VB: the VAG bodies back to back (PS-ADPCM, 16-byte blocks).
//   SEP 'pQES' v0: per sequence u16 id, u16 resolution, u24 tempo (us/quarter), u16 rhythm,
//       u32 data size, then MIDI events with running status (FF 2F 00 ends a sequence).
//
// Rendering is an approximation of the SPU (no reverb, linear instead of gaussian interpolation,
// libsnd volume/pan law approximated); pitch: 44100 Hz at the tone's centre note (+shift/128).
//
//   node tools/export_music.mjs --root DECOMP --out DIR [--set 1] [--info] [--samples] [--midi]
//        [--render] [--seconds-cap 600]
import fs from 'node:fs';
import path from 'node:path';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i < 0 ? fallback : argv[i + 1]; };
const flag = name => argv.includes(name);
const root = path.resolve(option('--root', '.'));
const out = path.resolve(option('--out', 'music-export'));
const setIndex = Number(option('--set', '1'));
const RATE = 44100;

const exe = fs.readFileSync(path.join(root, 'xena.exe'));
const base = exe.readUInt32LE(0x18), eo = a => a - base + 0x800;
const cstr = a => { let o = eo(a), e = o; while (exe[e]) e++; return exe.toString('latin1', o, e); };
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'build/asset-audit/manifest.json')));
const uda = fs.readFileSync(path.join(root, 'disc/files/XENA.UDA'));
function resource(name) {
  const hits = manifest.entries.filter(e => e.candidates.some(c => c.name.toUpperCase() === name.toUpperCase()));
  if (hits.length !== 1 || !hits[0].inArchive) throw Error(`Missing or ambiguous ${name}`);
  const at = (hits[0].lba - manifest.summary.archiveLba) * 2048;
  return { name, lba: hits[0].lba, bytes: uda.subarray(at, at + hits[0].size) };
}

// ---- EXE tables -------------------------------------------------------------------------------
const bankNames = []; for (let i = 0; i < 32; i++) { const p = exe.readUInt32LE(eo(0x800EE478 + i * 4)); if ((p >>> 24) !== 0x80) break; bankNames.push(cstr(p)); }
const music = []; for (let i = 0; i < 16; i++) { const a = 0x800CC37C + i * 8, p = exe.readUInt32LE(eo(a)); if ((p >>> 24) !== 0x80) break; music.push({ name: cstr(p), sequences: exe.readUInt16LE(eo(a + 4)) }); }
const setPtr = exe.readUInt32LE(eo(0x800EE550 + setIndex * 4));
const setList = []; for (let k = 0; k < 16; k++) { const v = exe.readInt16LE(eo(setPtr + k * 2)); if (k > 0 && v === -1) break; setList.push(v); }
const soundSet = { set: setIndex, table: '0x800EE550', music: setList[0] >= 0 ? music[setList[0]] : null, banks: ['core', ...setList.slice(1).map(i => bankNames[i])] };

// ---- VAB --------------------------------------------------------------------------------------
function readVab(vh, vb) {
  if (vh.toString('latin1', 0, 4) !== 'pBAV') throw Error('Not a VAB header');
  const h = { version: vh.readUInt32LE(4), id: vh.readUInt32LE(8), fileSize: vh.readUInt32LE(12), ps: vh.readUInt16LE(18), ts: vh.readUInt16LE(20), vs: vh.readUInt16LE(22), mvol: vh[24], pan: vh[25] };
  const expected = 32 + 2048 + h.ps * 512 + 512;
  if (vh.length !== expected) throw Error(`VH size ${vh.length} != ${expected}`);
  const programs = Array.from({ length: 128 }, (_, i) => { const o = 32 + i * 16; return { index: i, tones: vh[o], mvol: vh[o + 1], prior: vh[o + 2], mode: vh[o + 3], pan: vh[o + 4], attr: vh.readUInt16LE(o + 6) }; });
  const toneBase = 32 + 2048, tones = [];
  // Tone blocks are stored for the ps programs that have tones, in program order (16 slots each).
  let block = 0;
  for (const p of programs) {
    if (!p.tones) continue;
    if (block >= h.ps) throw Error('More programs with tones than ps');
    p.toneList = [];
    for (let t = 0; t < p.tones; t++) {
      const o = toneBase + block * 512 + t * 32;
      const tone = { prior: vh[o], mode: vh[o + 1], vol: vh[o + 2], pan: vh[o + 3], center: vh[o + 4], shift: vh[o + 5], min: vh[o + 6], max: vh[o + 7],
        pbmin: vh[o + 12], pbmax: vh[o + 13], adsr1: vh.readUInt16LE(o + 16), adsr2: vh.readUInt16LE(o + 18), prog: vh.readInt16LE(o + 20), vag: vh.readInt16LE(o + 22) };
      p.toneList.push(tone); tones.push(tone);
    }
    block++;
  }
  const tableAt = toneBase + h.ps * 512;
  const sizes = Array.from({ length: 256 }, (_, i) => vh.readUInt16LE(tableAt + i * 2) * 8);
  const vags = []; let at = 0;
  for (let i = 1; i <= h.vs; i++) { vags[i] = { index: i, offset: at, size: sizes[i] }; at += sizes[i]; }
  return { header: h, programs, tones, vags, vbBytes: vb.length, vbSum: at, sizes };
}
const F0 = [0, 60, 115, 98, 122], F1 = [0, 0, -52, -55, -60];
function decodeVag(vb, offset, size) {
  const pcm = [], blocks = size >> 4; let s1 = 0, s2 = 0, loopStart = -1, loopEnd = -1, ended = false;
  for (let b = 0; b < blocks; b++) {
    const o = offset + b * 16, shift = vb[o] & 15, filter = Math.min(4, vb[o] >> 4), flags = vb[o + 1];
    if (flags & 4) loopStart = pcm.length;
    for (let i = 0; i < 28; i++) {
      const byte = vb[o + 2 + (i >> 1)], nib = i & 1 ? byte >> 4 : byte & 15;
      let s = ((nib << 28) >> 28) << 12; s >>= shift;
      s += (s1 * F0[filter] + s2 * F1[filter] + 32) >> 6;
      s = Math.max(-32768, Math.min(32767, s)); s2 = s1; s1 = s; pcm.push(s);
    }
    if (flags & 1) { loopEnd = pcm.length; ended = true; if (!(flags & 2)) loopStart = -1; break; }
  }
  return { pcm: Int16Array.from(pcm), loopStart, loopEnd: ended ? loopEnd : -1, looped: loopStart >= 0 && ended };
}
function wav(channels, rate, samples) { // samples: Int16Array interleaved
  const b = Buffer.alloc(44 + samples.length * 2);
  b.write('RIFF', 0); b.writeUInt32LE(36 + samples.length * 2, 4); b.write('WAVE', 8); b.write('fmt ', 12);
  b.writeUInt32LE(16, 16); b.writeUInt16LE(1, 20); b.writeUInt16LE(channels, 22); b.writeUInt32LE(rate, 24);
  b.writeUInt32LE(rate * channels * 2, 28); b.writeUInt16LE(channels * 2, 32); b.writeUInt16LE(16, 34); b.write('data', 36); b.writeUInt32LE(samples.length * 2, 40);
  for (let i = 0; i < samples.length; i++) b.writeInt16LE(samples[i], 44 + i * 2);
  return b;
}

// ---- SEP --------------------------------------------------------------------------------------
function readSep(bytes) {
  if (bytes.toString('latin1', 0, 4) !== 'pQES') throw Error('Not a SEP/SEQ');
  const version = bytes.readUInt16BE(4); let cursor = 6; const seqs = [];
  while (cursor + 13 <= bytes.length) {
    const id = bytes.readUInt16BE(cursor), resolution = bytes.readUInt16BE(cursor + 2);
    const tempo = (bytes[cursor + 4] << 16) | (bytes[cursor + 5] << 8) | bytes[cursor + 6];
    const rhythm = [bytes[cursor + 7], bytes[cursor + 8]], size = bytes.readUInt32BE(cursor + 9);
    const data = bytes.subarray(cursor + 13, cursor + 13 + size);
    seqs.push({ id, resolution, tempo, rhythm, size, offset: cursor + 13, events: parseEvents(data) });
    cursor += 13 + size;
    if (seqs.length > 64) break;
  }
  return { version, seqs, trailing: bytes.length - cursor };
}
function parseEvents(d) {
  const ev = []; let i = 0, tick = 0, status = 0;
  const vlq = () => { let v = 0, b; do { b = d[i++]; v = (v << 7) | (b & 127); } while (b & 128 && i < d.length); return v; };
  // A SEQ stream starts with a delta time before every event.
  while (i < d.length) {
    tick += vlq();
    let b = d[i];
    if (b & 0x80) { status = b; i++; }
    if (status === 0xFF) {
      const type = d[i++];
      if (type === 0x2F) { ev.push({ tick, type: 'end' }); i++; break; }
      if (type === 0x51) { const t = (d[i] << 16) | (d[i + 1] << 8) | d[i + 2]; i += 3; ev.push({ tick, type: 'tempo', tempo: t }); continue; }
      throw Error(`Unknown meta FF ${type.toString(16)} at ${i}`);
    }
    const kind = status & 0xF0, ch = status & 15;
    if (kind === 0x90) { const note = d[i++], vel = d[i++]; ev.push({ tick, type: vel ? 'on' : 'off', ch, note, vel }); }
    else if (kind === 0x80) { const note = d[i++], vel = d[i++]; ev.push({ tick, type: 'off', ch, note, vel }); }
    else if (kind === 0xB0) { const cc = d[i++], val = d[i++]; ev.push({ tick, type: 'cc', ch, cc, val }); }
    else if (kind === 0xC0) { ev.push({ tick, type: 'prog', ch, prog: d[i++] }); }
    else if (kind === 0xE0) { const lo = d[i++], hi = d[i++]; ev.push({ tick, type: 'bend', ch, value: ((hi << 7) | lo) - 8192 }); }
    else if (kind === 0xA0 || kind === 0xD0) { ev.push({ tick, type: kind === 0xA0 ? 'aftertouch' : 'pressure', ch }); i += kind === 0xA0 ? 2 : 1; }
    else throw Error(`Unknown status ${status.toString(16)} at ${i}`);
  }
  return ev;
}
function timeline(seq) { // tick -> seconds with tempo changes
  let tempo = seq.tempo, last = 0, sec = 0; const out = [];
  for (const e of seq.events) {
    sec += (e.tick - last) * tempo / 1e6 / seq.resolution; last = e.tick;
    if (e.type === 'tempo') tempo = e.tempo;
    out.push({ ...e, sec });
  }
  return out;
}
function loopMarkers(seq) { // libsnd: CC99 (NRPN MSB) data 20 = loop start, 30 = loop end; CC6 = count
  return seq.events.filter(e => e.type === 'cc' && (e.cc === 99 || e.cc === 98 || e.cc === 6)).map(e => ({ tick: e.tick, ch: e.ch, cc: e.cc, val: e.val }));
}
function midiFile(seq) {
  const chunks = [];
  const vlq = v => { const b = [v & 127]; while ((v >>= 7)) b.unshift((v & 127) | 128); return b; };
  let last = 0; const t = [];
  t.push(0, 0xFF, 0x51, 3, (seq.tempo >> 16) & 255, (seq.tempo >> 8) & 255, seq.tempo & 255);
  for (const e of seq.events) {
    const d = vlq(e.tick - last); last = e.tick;
    if (e.type === 'on') t.push(...d, 0x90 | e.ch, e.note, e.vel);
    else if (e.type === 'off') t.push(...d, 0x80 | e.ch, e.note, e.vel ?? 0);
    else if (e.type === 'cc') t.push(...d, 0xB0 | e.ch, e.cc, e.val);
    else if (e.type === 'prog') t.push(...d, 0xC0 | e.ch, e.prog);
    else if (e.type === 'bend') { const v = e.value + 8192; t.push(...d, 0xE0 | e.ch, v & 127, v >> 7); }
    else if (e.type === 'tempo') t.push(...d, 0xFF, 0x51, 3, (e.tempo >> 16) & 255, (e.tempo >> 8) & 255, e.tempo & 255);
    else if (e.type === 'end') t.push(...d, 0xFF, 0x2F, 0);
  }
  const head = Buffer.from([0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 0, 0, 1, seq.resolution >> 8, seq.resolution & 255]);
  const trk = Buffer.concat([Buffer.from('MTrk'), Buffer.alloc(4), Buffer.from(t)]); trk.writeUInt32BE(t.length, 4);
  return Buffer.concat([head, trk]);
}

// ---- SPU-like renderer ------------------------------------------------------------------------
// Exact libsnd SsPitchFromNote (0x80096A68) with its tables D_800D46B4 (12 semitones) and
// D_800D46CC (128 fine steps): pitch 0x1000 = 44100 Hz when note == center and fine+shift == 0;
// shift raises the pitch by shift/128 semitone, capped at 0x3FFF.
const semiTable = Array.from({ length: 12 }, (_, i) => exe.readUInt16LE(eo(0x800D46B4 + i * 2)));
const fineTable = Array.from({ length: 128 }, (_, i) => exe.readUInt16LE(eo(0x800D46CC + i * 2)));
function ssPitchFromNote(note, fine, center, shift) {
  const total = (((shift & 255) + fine) << 16) >> 16;
  const whole = Math.trunc(total / 128);
  let n = note + whole - (center & 255), frac = total - whole * 128;
  if (frac < 0) { frac += 128; n -= 1; }
  n = (n << 16) >> 16;
  let oct = Math.trunc(n / 12), semi = n - oct * 12, shiftDown = oct - 2;
  if (((semi << 16) >> 16) < 0) { semi += 12; shiftDown = oct - 3; }
  let p = (semiTable[semi] * fineTable[frac & 127]) >>> 16;
  if (shiftDown >= 0) return 0x3FFF;
  const s = -shiftDown; return ((p + (1 << (s - 1))) >>> s) & 0xFFFF;
}
function envelopeStep(level, rate, decrease, exp) {
  const shift = rate >> 2, stepBits = rate & 3;
  let step = decrease ? -8 + stepBits : 7 - stepBits;
  let cycles = 1 << Math.max(0, shift - 11);
  step = step << Math.max(0, 11 - shift);
  if (exp && !decrease && level > 0x6000) cycles *= 4;
  if (exp && decrease) step = (step * level) >> 15;
  return { step, cycles };
}
class Voice {
  constructor(sample, tone, rateRatio, volL, volR) {
    Object.assign(this, { sample, tone, rateRatio, volL, volR, pos: 0, level: 0, phase: 'attack', counter: 0, done: false });
    const a1 = tone.adsr1, a2 = tone.adsr2;
    this.env = { attackExp: !!(a1 & 0x8000), attackRate: (a1 >> 8) & 127, decayShift: (a1 >> 4) & 15, sustainLevel: Math.min(0x7FFF, ((a1 & 15) + 1) * 0x800),
      sustainExp: !!(a2 & 0x8000), sustainDec: !!(a2 & 0x4000), sustainRate: (a2 >> 6) & 127, releaseExp: !!(a2 & 0x20), releaseShift: a2 & 31 };
  }
  stepEnv() {
    const e = this.env; let rate, dec, exp;
    if (this.phase === 'attack') { rate = e.attackRate; dec = false; exp = e.attackExp; }
    else if (this.phase === 'decay') { rate = e.decayShift << 2; dec = true; exp = true; }
    else if (this.phase === 'sustain') { rate = e.sustainRate; dec = e.sustainDec; exp = e.sustainExp; }
    else { rate = e.releaseShift << 2; dec = true; exp = e.releaseExp; }
    const { step, cycles } = envelopeStep(this.level, rate, dec, exp);
    if (++this.counter >= cycles) { this.counter = 0; this.level = Math.max(0, Math.min(0x7FFF, this.level + step)); }
    if (this.phase === 'attack' && this.level >= 0x7FFF) this.phase = 'decay';
    else if (this.phase === 'decay' && this.level <= e.sustainLevel) this.phase = 'sustain';
    else if (this.phase === 'release' && this.level <= 0) this.done = true;
  }
  next() {
    const s = this.sample, i = Math.floor(this.pos), f = this.pos - i;
    const n = s.pcm.length;
    const at = k => { if (k < n) return s.pcm[k]; if (s.looped) return s.pcm[s.loopStart + ((k - n) % (n - s.loopStart))]; return 0; };
    const v = at(i) * (1 - f) + at(i + 1) * f;
    this.pos += this.rateRatio;
    if (this.pos >= n) { if (s.looped) this.pos = s.loopStart + ((this.pos - n) % (n - s.loopStart)); else this.done = true; }
    this.stepEnv();
    return v * this.level / 0x7FFF;
  }
}
function render(seq, vabs, capSeconds) {
  const tl = timeline(seq);
  const endSec = tl.length ? tl[tl.length - 1].sec : 0;
  const total = Math.min(capSeconds, endSec + 4) * RATE | 0;
  const L = new Float32Array(total), R = new Float32Array(total);
  const ch = Array.from({ length: 16 }, () => ({ prog: 0, vol: 127, expr: 127, pan: 64, bend: 0, voices: [] }));
  const vab = vabs[0]; // music VAB: SsSepOpenJ binds the SEP to the music VAB id
  const findTones = (prog, note) => (vab.programs[prog]?.toneList ?? []).filter(t => note >= t.min && note <= t.max);
  let voices = [], ei = 0, maxVoices = 0, missing = new Set();
  for (let n = 0; n < total; n++) {
    const now = n / RATE;
    while (ei < tl.length && tl[ei].sec <= now) {
      const e = tl[ei++], c = ch[e.ch ?? 0];
      if (e.type === 'prog') c.prog = e.prog;
      else if (e.type === 'cc') { if (e.cc === 7) c.vol = e.val; else if (e.cc === 11) c.expr = e.val; else if (e.cc === 10) c.pan = e.val; }
      else if (e.type === 'bend') c.bend = e.value;
      else if (e.type === 'on') {
        const tones = findTones(c.prog, e.note);
        if (!tones.length) missing.add(`${c.prog}:${e.note}`);
        for (const t of tones) {
          const s = vab.samples[t.vag]; if (!s) { missing.add(`vag${t.vag}`); continue; }
          const prog = vab.programs[c.prog];
          const vol = (vab.header.mvol / 127) * (prog.mvol / 127) * (t.vol / 127) * (e.vel / 127) * (c.vol / 127) * (c.expr / 127);
          const pan = Math.max(0, Math.min(127, c.pan + (t.pan - 64) + (prog.pan - 64)));
          const v = new Voice(s, t, ssPitchFromNote(e.note, 0, t.center, t.shift) / 4096, vol * Math.min(1, (127 - pan) / 63.5), vol * Math.min(1, pan / 63.5));
          v.ch = e.ch; v.note = e.note; v.baseRatio = v.rateRatio; voices.push(v);
        }
      } else if (e.type === 'off') { for (const v of voices) if (v.ch === e.ch && v.note === e.note && v.phase !== 'release') v.phase = 'release'; }
    }
    let l = 0, r = 0;
    for (const v of voices) {
      const c = ch[v.ch];
      if (c.bend) { const range = c.bend > 0 ? v.tone.pbmax : v.tone.pbmin; v.rateRatio = v.baseRatio * Math.pow(2, (c.bend / 8192) * range / 12); }
      const s = v.next(); l += s * v.volL; r += s * v.volR;
    }
    if (voices.length > maxVoices) maxVoices = voices.length;
    if (n % 64 === 0) voices = voices.filter(v => !v.done);
    L[n] = l; R[n] = r;
  }
  let peak = 0; for (let n = 0; n < total; n++) peak = Math.max(peak, Math.abs(L[n]), Math.abs(R[n]));
  const gain = peak > 0 ? Math.min(1, 30000 / peak) : 1;
  const pcm = new Int16Array(total * 2);
  for (let n = 0; n < total; n++) { pcm[n * 2] = Math.round(L[n] * gain); pcm[n * 2 + 1] = Math.round(R[n] * gain); }
  return { pcm, seconds: total / RATE, sequenceSeconds: endSec, peak, gain, maxVoices, missingTones: [...missing] };
}

// ---- main -------------------------------------------------------------------------------------
fs.mkdirSync(out, { recursive: true });
const report = { tool: 'export_music.mjs', soundSet, bankTable: '0x800EE478', bankNames, musicTable: '0x800CC37C', music, outputs: [] };
const vabNames = [...(soundSet.music ? [soundSet.music.name] : []), ...soundSet.banks];
const vabs = {};
for (const name of vabNames) {
  const vh = resource(`DATA\\SND\\${name.toUpperCase()}.VH`), vb = resource(`DATA\\SND\\${name.toUpperCase()}.VB`);
  const v = readVab(vh.bytes, vb.bytes);
  v.samples = [];
  for (let i = 1; i <= v.header.vs; i++) v.samples[i] = decodeVag(vb.bytes, v.vags[i].offset, v.vags[i].size);
  vabs[name] = v;
  const info = { name, vh: vh.name, vb: vb.name, header: v.header, programsWithTones: v.programs.filter(p => p.tones).map(p => ({ index: p.index, tones: p.tones, mvol: p.mvol, pan: p.pan })),
    vbBytes: v.vbBytes, vagSizeSum: v.vbSum, sizeCheck: v.vbBytes === v.vbSum,
    samples: v.samples.slice(1).map((s, k) => ({ vag: k + 1, frames: s.pcm.length, secondsAt44100: +(s.pcm.length / RATE).toFixed(3), looped: s.looped, loopStart: s.loopStart,
      tones: v.tones.filter(t => t.vag === k + 1).map(t => ({ prog: t.prog, center: t.center, shift: t.shift, range: [t.min, t.max], adsr: [t.adsr1.toString(16), t.adsr2.toString(16)] })) })) };
  if (flag('--samples')) {
    const dir = path.join(out, 'samples', name); fs.mkdirSync(dir, { recursive: true });
    for (let i = 1; i <= v.header.vs; i++) fs.writeFileSync(path.join(dir, `${name}_${String(i).padStart(3, '0')}.wav`), wav(1, RATE, v.samples[i].pcm));
    info.samplesDir = dir;
  }
  report.outputs.push(info);
}
if (soundSet.music) {
  const sep = resource(`DATA\\SND\\${soundSet.music.name.toUpperCase()}.SEP`);
  const parsed = readSep(sep.bytes);
  const seqInfo = parsed.seqs.map(s => {
    const tl = timeline(s), notes = s.events.filter(e => e.type === 'on');
    return { id: s.id, resolution: s.resolution, tempo: s.tempo, bpm: +(60e6 / s.tempo).toFixed(2), rhythm: s.rhythm, bytes: s.size, events: s.events.length,
      notes: notes.length, channels: [...new Set(notes.map(e => e.ch))], programs: [...new Set(s.events.filter(e => e.type === 'prog').map(e => e.prog))],
      seconds: +(tl.length ? tl[tl.length - 1].sec : 0).toFixed(2), endsWithEot: s.events.at(-1)?.type === 'end', loopControllers: loopMarkers(s).slice(0, 12) };
  });
  report.sep = { file: sep.name, bytes: sep.bytes.length, version: parsed.version, sequences: seqInfo, trailingBytes: parsed.trailing, expectedSequences: soundSet.music.sequences };
  if (flag('--midi')) for (const s of parsed.seqs) fs.writeFileSync(path.join(out, `${soundSet.music.name}_seq${s.id}.mid`), midiFile(s));
  if (flag('--render')) {
    const cap = Number(option('--seconds-cap', '600'));
    report.renders = [];
    for (const s of parsed.seqs) {
      const r = render(s, [vabs[soundSet.music.name]], cap);
      const file = path.join(out, `${soundSet.music.name}_seq${s.id}.wav`);
      fs.writeFileSync(file, wav(2, RATE, r.pcm));
      report.renders.push({ seq: s.id, file, rate: RATE, seconds: +r.seconds.toFixed(2), sequenceSeconds: +r.sequenceSeconds.toFixed(2), maxSimultaneousVoices: r.maxVoices, normalizeGain: +r.gain.toFixed(3), missingTones: r.missingTones });
    }
  }
}
fs.writeFileSync(path.join(out, 'music-report.json'), JSON.stringify(report, null, 1) + '\n');
console.log(JSON.stringify({ soundSet, vabs: report.outputs.map(o => ({ name: o.name, ps: o.header.ps, ts: o.header.ts, vs: o.header.vs, sizeCheck: o.sizeCheck, loopedSamples: o.samples.filter(s => s.looped).length })),
  sep: report.sep && { ...report.sep, sequences: report.sep.sequences.map(({ loopControllers, ...s }) => s) }, renders: report.renders }, null, 1));
