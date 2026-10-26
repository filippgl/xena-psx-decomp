#!/usr/bin/env node
// Export the sound effects of a level sound set (default set 1 = level 01) as 44.1 kHz WAV + sfx.json.
// Facts and labels: docs/xena-sfx.md. Read-only on the game data.
//
// Game side (SLUS-00977):
//   func_800847C0(id, note, vol, pan, pos) plays every effect.
//     id & 0x2000 -> music sequence (func_800865A8, SsSepPlay)            [not exported here]
//     id & 0x1000 -> CD-XA voice clip (func_8008751C): group q = (id&0xFFF)/31, channel = %31,
//                    start sector = MEDIA\XA1.XA + D_800CC650[q]; length from the group's data sector
//     else        -> VAB program: D_800CC424[id] = {s16 bank slot, s16 program};
//                    SsVoKeyOn((vabId << 8) | prog, note << 8, 1, 1) then SsUtSetVVol(voice, L, R)
//                    with L/R from func_800844BC (vol 0..127, pan 0..127, scale, master D_800F1596)
//   Wrappers: func_80084A34(id) = (id, 60 | 64 for XA, 127, 64, NULL); func_80084AD0(id, pos) = (id, 60, 127, 64, pos)
//             func_80084A70(id, note, pos); func_80084AA0(id, vol, pos); func_80084E80(list, n, pos, chance)
//
// The renderer replays one voice per matching tone exactly like the native port's SPU model
// (port/runtime/spu.c: PS-ADPCM, 12-bit pitch counter with linear interpolation, ADSR, voice volume
// = vol * 129, master 127 * 129) with the default call (note 60, vol 127, pan 64, no position, dry: level 01
// has SFX reverb off). Looping samples are held for --hold seconds and then released (key off).
// XA clips: XA-ADPCM decode (18.9 kHz mono) resampled to 44.1 kHz with the port's linear interpolation.
//
//   node tools/export_sfx.mjs --root DECOMP [--bin DISC.bin] [--out build/sfx/export] [--set 1]
//        [--xa referenced|all|none] [--hold 2] [--raw]
import fs from 'node:fs';
import path from 'node:path';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i < 0 ? fallback : argv[i + 1]; };
const flag = name => argv.includes(name);
const root = path.resolve(option('--root', '.'));
const out = path.resolve(option('--out', path.join(root, 'build/sfx/export')));
const binPath = path.resolve(option('--bin', path.join(root, '../Xena Warrior Princess.bin')));
const setIndex = Number(option('--set', '1'));
const xaMode = option('--xa', 'referenced');
const holdSeconds = Number(option('--hold', '2'));
const RATE = 44100;
const hex = (v, w = 2) => '0x' + v.toString(16).toUpperCase().padStart(w, '0');

// ---- EXE ----------------------------------------------------------------------------------------
const exe = fs.readFileSync(path.join(root, 'xena.exe'));
const base = exe.readUInt32LE(0x18), eo = a => a - base + 0x800;
const u16 = a => exe.readUInt16LE(eo(a)), s16 = a => exe.readInt16LE(eo(a)), u32 = a => exe.readUInt32LE(eo(a));
const cstr = a => { let o = eo(a), e = o; while (exe[e]) e++; return exe.toString('latin1', o, e); };
const bankNames = []; for (let i = 0; i < 32; i++) { const p = u32(0x800EE478 + i * 4); if ((p >>> 24) !== 0x80) break; bankNames.push(cstr(p)); }
const setPtr = u32(0x800EE550 + setIndex * 4);
const setList = []; for (let k = 0; k < 16; k++) { const v = s16(setPtr + k * 2); if (k > 0 && v === -1) break; setList.push(v); }
const loadedSlots = [0, ...setList.slice(1)]; // func_80086DCC: slot 0 "core" always, then the set's banks
// D_800CC424: {s16 bank slot, s16 program} per sound id; it ends where the XA table D_800CC650 starts.
const SOUND_TABLE = 0x800CC424, SOUND_COUNT = (0x800CC650 - 0x800CC424) / 4;
const soundTable = Array.from({ length: SOUND_COUNT }, (_, id) => ({ id, slot: s16(SOUND_TABLE + id * 4), prog: s16(SOUND_TABLE + id * 4 + 2) }));
const XA_GROUPS = 8; // D_800CC650: u16 sector offsets of the XA1.XA groups (the 9th halfword is unrelated data)
const xaGroupOffsets = Array.from({ length: XA_GROUPS }, (_, q) => u16(0x800CC650 + q * 2));

// ---- archive ----------------------------------------------------------------------------------------
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'build/asset-audit/manifest.json')));
const uda = fs.readFileSync(path.join(root, 'disc/files/XENA.UDA'));
function resource(name) {
  const hits = manifest.entries.filter(e => e.candidates.some(c => c.name.toUpperCase() === name.toUpperCase()));
  if (hits.length !== 1 || !hits[0].inArchive) throw Error(`Missing or ambiguous ${name}`);
  const at = (hits[0].lba - manifest.summary.archiveLba) * 2048;
  return { name, lba: hits[0].lba, bytes: uda.subarray(at, at + hits[0].size) };
}

// ---- VAB (same layout checks as tools/export_music.mjs) ------------------------------------------
function readVab(vh, vb) {
  if (vh.toString('latin1', 0, 4) !== 'pBAV') throw Error('Not a VAB header');
  const h = { version: vh.readUInt32LE(4), id: vh.readUInt32LE(8), ps: vh.readUInt16LE(18), ts: vh.readUInt16LE(20), vs: vh.readUInt16LE(22), mvol: vh[24], pan: vh[25] };
  if (vh.length !== 32 + 2048 + h.ps * 512 + 512) throw Error('VH size');
  const programs = Array.from({ length: 128 }, (_, i) => { const o = 32 + i * 16; return { index: i, tones: vh[o], mvol: vh[o + 1], prior: vh[o + 2], mode: vh[o + 3], pan: vh[o + 4] }; });
  let block = 0;
  for (const p of programs) {
    p.toneList = [];
    if (!p.tones) continue;
    for (let t = 0; t < p.tones; t++) {
      const o = 32 + 2048 + block * 512 + t * 32;
      p.toneList.push({ prior: vh[o], mode: vh[o + 1], vol: vh[o + 2], pan: vh[o + 3], center: vh[o + 4], shift: vh[o + 5], min: vh[o + 6], max: vh[o + 7],
        adsr1: vh.readUInt16LE(o + 16), adsr2: vh.readUInt16LE(o + 18), vag: vh.readInt16LE(o + 22) });
    }
    block++;
  }
  const tableAt = 32 + 2048 + h.ps * 512, vags = []; let at = 0;
  for (let i = 1; i <= h.vs; i++) { const size = vh.readUInt16LE(tableAt + i * 2) * 8; vags[i] = { offset: at, size }; at += size; }
  if (at !== vb.length) throw Error('VB size');
  return { header: h, programs, vags, vb };
}

// ---- libsnd SsPitchFromNote (0x80096A68), tables D_800D46B4 / D_800D46CC ---------------------------
const semiTable = Array.from({ length: 12 }, (_, i) => u16(0x800D46B4 + i * 2));
const fineTable = Array.from({ length: 128 }, (_, i) => u16(0x800D46CC + i * 2));
function ssPitchFromNote(note, fine, center, shift) {
  const total = (((shift & 255) + fine) << 16) >> 16;
  const whole = Math.trunc(total / 128);
  let n = note + whole - (center & 255), frac = total - whole * 128;
  if (frac < 0) { frac += 128; n -= 1; }
  n = (n << 16) >> 16;
  let oct = Math.trunc(n / 12), semi = n - oct * 12, shiftDown = oct - 2;
  if (((semi << 16) >> 16) < 0) { semi += 12; shiftDown = oct - 3; }
  const p = (semiTable[semi] * fineTable[frac & 127]) >>> 16;
  if (shiftDown >= 0) return 0x3FFF;
  const s = -shiftDown; return ((p + (1 << (s - 1))) >>> s) & 0xFFFF;
}

// ---- volume law: func_800844BC / func_800845C8 ------------------------------------------------------
function volumeLR(vol, pan, scale, master, stereo = true) { // func_800844BC
  let v;
  if (scale <= 0 || master <= 0) v = 0;
  else { v = vol; if (scale !== 127) v = (scale * vol + 64) >> 7; if (master < 127) v = (master * v + 64) >> 7; }
  if (v > 127) v = 127;
  let L = v, R = v;
  if (v && stereo && pan !== 64) { if (pan < 64) R = (v * pan) >> 6; else L = (v * (127 - pan)) >> 6; }
  return [L, R];
}

// ---- SPU voice, a transcription of port/runtime/spu.c (voice_sample, env_tick, decode_block) --------
const POS = [0, 60, 115, 98, 122], NEG = [0, 0, -52, -55, -60];
class SpuVoice {
  constructor(vb, offset, size, pitch, adsr1, adsr2, volL, volR) {
    Object.assign(this, { vb, start: offset, end: offset + size, addr: offset, repeat: offset, counter: 0, hist: [0, 0], samples: new Int16Array(28), prev: 0,
      env: 1, level: 0, wait: 0, flags: 0, pitch: Math.min(pitch, 0x4000), adsr1, adsr2, vl: (volL * 129) << 1, vr: (volR * 129) << 1 });
    this.decode();
  }
  decode() {
    const b = this.vb, o = this.addr;
    if (o + 16 > b.length) { this.flags = 1; this.samples.fill(0); return; }
    let shift = b[o] & 15, filter = (b[o] >> 4) & 7; if (shift > 12) shift = 9; if (filter > 4) filter = 4;
    this.flags = b[o + 1];
    for (let i = 0; i < 28; i++) {
      const nib = (b[o + 2 + (i >> 1)] >> ((i & 1) * 4)) & 15;
      let s = ((nib << 28) >> 16) >> shift; // (s16)(nib << 12) >> shift
      s += (this.hist[0] * POS[filter] + this.hist[1] * NEG[filter] + 32) >> 6;
      s = Math.max(-32768, Math.min(32767, s)); this.hist[1] = this.hist[0]; this.hist[0] = s; this.samples[i] = s;
    }
  }
  envTick() {
    const lo = this.adsr1, hi = this.adsr2; let rate, exp, dec, target = -1;
    switch (this.env) {
      case 1: rate = (lo >> 8) & 0x7F; exp = lo >> 15; dec = 0; break;
      case 2: rate = ((lo >> 4) & 15) << 2; exp = 1; dec = 1; target = ((lo & 15) + 1) * 0x800; break;
      case 3: rate = (hi >> 6) & 0x7F; exp = hi >> 15; dec = (hi >> 14) & 1; break;
      case 4: rate = (hi & 31) << 2; exp = (hi >> 5) & 1; dec = 1; break;
      default: return;
    }
    if (this.wait > 0) { this.wait--; return; }
    const shift = rate >> 2; let step = dec ? -8 + (rate & 3) : 7 - (rate & 3); let cycles = 1 << (shift > 11 ? shift - 11 : 0);
    step = step * (1 << (shift < 11 ? 11 - shift : 0));
    if (exp && !dec && this.level > 0x6000) cycles *= 4;
    if (exp && dec) step = Math.trunc(step * this.level / 0x8000);
    // An exponential release truncates to step 0 near silence (also in the port's integer code, where
    // the voice then idles inaudibly); end the voice there so the WAV has a finite tail.
    if (this.env === 4 && step === 0) { this.level = 0; this.env = 0; return; }
    this.wait = cycles - 1; this.level = Math.max(0, Math.min(0x7FFF, this.level + step));
    if (this.env === 1 && this.level >= 0x7FFF) { this.env = 2; this.wait = 0; }
    else if (this.env === 2 && this.level <= target) { this.env = 3; this.wait = 0; }
    else if (this.env === 4 && this.level === 0) this.env = 0;
  }
  keyOff() { if (this.env) { this.env = 4; this.wait = 0; } }
  next() {
    if (!this.env) return [0, 0];
    const idx = this.counter >> 12, frac = this.counter & 0xFFF, cur = this.samples[idx], last = idx > 0 ? this.samples[idx - 1] : this.prev;
    let s = last + ((cur - last) * frac >> 12);
    this.counter += this.pitch;
    while ((this.counter >> 12) >= 28) {
      this.counter -= 28 << 12; this.prev = this.samples[27];
      const fl = this.flags;
      if (fl & 4) this.repeat = this.addr;
      if (fl & 1) { if (fl & 2) this.addr = this.repeat; else { this.env = 0; this.level = 0; this.addr += 16; } }
      else this.addr += 16;
      this.decode();
      if (this.flags & 4) this.repeat = this.addr;
    }
    this.envTick();
    s = (s * this.level) >> 15;
    return [(s * this.vl) >> 15, (s * this.vr) >> 15];
  }
}
const MASTER = (127 * 129) << 1; // SsSetMVol(127, 127) in func_80086B68
function renderProgram(vab, prog, { note = 60, vol = 127, pan = 64, scale = 127, master = 127 } = {}) {
  const tones = vab.programs[prog].toneList.filter(t => note >= t.min && note <= t.max);
  const [L, R] = volumeLR(vol, pan, scale, master);
  const voices = tones.map(t => { const v = vab.vags[t.vag]; return new SpuVoice(vab.vb, v.offset, v.size, ssPitchFromNote(note, 0, t.center, t.shift), t.adsr1, t.adsr2, L, R); });
  const loops = tones.map(t => sampleLoop(vab, t.vag));
  const anyLoop = loops.some(l => l.looped);
  const holdN = Math.round(holdSeconds * RATE), cap = 30 * RATE;
  const left = [], right = [];
  for (let n = 0; n < cap; n++) {
    if (anyLoop && n === holdN) voices.forEach(v => v.keyOff());
    if (voices.every(v => !v.env)) break;
    let l = 0, r = 0; for (const v of voices) { const [a, b] = v.next(); l += a; r += b; }
    left.push(Math.max(-32768, Math.min(32767, (l * MASTER) >> 15))); right.push(Math.max(-32768, Math.min(32767, (r * MASTER) >> 15)));
  }
  return { tones, loops, left, right, volL: L, volR: R, keyOffAt: anyLoop ? holdN : null };
}
function sampleLoop(vab, vag) { // flags of the VAG blocks: 4 = loop start, 1 = end, 2 = repeat
  const { offset, size } = vab.vags[vag], b = vab.vb; let loopStart = -1, end = -1;
  for (let k = 0; k < size >> 4; k++) { const f = b[offset + k * 16 + 1]; if (f & 4) loopStart = k * 28; if (f & 1) { end = (k + 1) * 28; return { looped: !!(f & 2) && loopStart >= 0, loopStart, end }; } }
  return { looped: false, loopStart: -1, end: (size >> 4) * 28 };
}
function decodeVagRaw(vab, vag) { // plain PS-ADPCM decode up to the end flag (natural-rate sample)
  const { offset, size } = vab.vags[vag], b = vab.vb, pcm = []; let h0 = 0, h1 = 0;
  for (let k = 0; k < size >> 4; k++) {
    const o = offset + k * 16; let shift = b[o] & 15, filter = Math.min(4, (b[o] >> 4) & 7); if (shift > 12) shift = 9;
    for (let i = 0; i < 28; i++) { const nib = (b[o + 2 + (i >> 1)] >> ((i & 1) * 4)) & 15; let s = ((nib << 28) >> 16) >> shift; s += (h0 * POS[filter] + h1 * NEG[filter] + 32) >> 6; s = Math.max(-32768, Math.min(32767, s)); h1 = h0; h0 = s; pcm.push(s); }
    if (b[o + 1] & 1) break;
  }
  return Int16Array.from(pcm);
}

// ---- CD-XA (func_8008751C / func_800872B8) -----------------------------------------------------------
function xaFileLba() { // disc/filelist.txt: "LBA size mode [RAW] path"; size / 2048 = sectors
  const list = fs.readFileSync(path.join(root, 'disc/filelist.txt'), 'latin1').split(/\r?\n/);
  const line = list.find(l => /\bMEDIA\/XA1\.XA$/i.test(l.trim()));
  if (!line) throw Error('MEDIA/XA1.XA not in disc/filelist.txt');
  const f = line.trim().split(/\s+/); XA_SECTORS = Number(f[1]) / 2048;
  return Number(f[0]);
}
let XA_SECTORS = 0;
function decodeXaSectors(sectors) { // XA-ADPCM 4-bit mono (coding 0x04), continuous filter history
  const pcm = [], hist = [0, 0];
  for (const s of sectors) for (let g = 0; g < 18; g++) {
    const grp = 24 + g * 128;
    for (let unit = 0; unit < 8; unit++) {
      const param = s[grp + 4 + unit]; let shift = param & 15; const filter = (param >> 4) & 3; if (shift > 12) shift = 9;
      for (let n = 0; n < 28; n++) {
        const byte = s[grp + 16 + n * 4 + (unit >> 1)], nib = unit & 1 ? byte >> 4 : byte & 15;
        let v = ((nib << 28) >> 16) >> shift; v += (hist[0] * POS[filter] + hist[1] * NEG[filter] + 32) >> 6;
        v = Math.max(-32768, Math.min(32767, v)); hist[1] = hist[0]; hist[0] = v; pcm.push(v);
      }
    }
  }
  return Int16Array.from(pcm);
}
const sectorHasAudio = s => { let n = 0; for (let g = 0; g < 18; g++) for (let i = 16; i < 128; i++) if (s[24 + g * 128 + i]) n++; return n > 100; };
let binFd = null;
function rawSector(lba) { const b = Buffer.alloc(2352); fs.readSync(binFd, b, 0, 2352, lba * 2352); return b; }
function xaClip(id) {
  const i = id & 0xFFF, q = Math.floor(i / 31), ch = i % 31;
  if (q >= XA_GROUPS) return null;
  const start = XA_LBA + xaGroupOffsets[q];
  const dataSector = rawSector(start + 31);
  if (dataSector[16] !== 1 || (dataSector[18] & 4)) throw Error(`XA group ${q}: sector 31 is not the data sector`);
  const extra = dataSector.readInt32LE(24 + ch * 4); // D_800F35E0 += table[channel]
  if (extra <= 0) return { q, ch, start, extra, sectors: 0, pcm: new Int16Array(0) };
  // Playback stops in the data-ready callback once pos (start + 32 per data sector) >= start + 64 + extra:
  // the channel delivers one audio sector per 32-sector cycle, so 2 + extra/32 sectors are heard.
  const cycles = 2 + Math.floor(extra / 32);
  // Every audio sector of this channel up to the next group: where is the recorded content?
  const groupEnd = XA_LBA + (q + 1 < XA_GROUPS ? xaGroupOffsets[q + 1] : XA_SECTORS);
  const all = []; let coding = null, eof = -1;
  for (let k = 0; start + k * 32 + ch < groupEnd; k++) {
    const s = rawSector(start + k * 32 + ch);
    if (s[16] !== 1 || s[17] !== ch || !(s[18] & 4)) throw Error(`XA ${hex(id, 4)}: sector ${k} is not audio of channel ${ch}`);
    if (k < cycles) { coding = s[19]; if (s[18] & 0x80 && eof < 0) eof = k; }
    all.push(s);
  }
  const audible = all.map(sectorHasAudio), first = audible.indexOf(true), last = audible.lastIndexOf(true);
  const heard = audible.slice(0, cycles).filter(Boolean).length, recorded = first < 0 ? 0 : last - first + 1;
  return { q, ch, start, extra, sectors: cycles, coding, eofSector: eof, rate: coding & 4 ? 18900 : 37800, pcm: decodeXaSectors(all.slice(0, cycles)),
    content: { firstSector: first, lastSector: last, recordedSectors: recorded, heardSectors: heard,
      status: recorded === 0 ? 'no content' : heard === 0 ? 'silent in game (content lies after the playback window)' : last >= cycles ? 'truncated in game' : 'complete' },
    fullPcm: first >= 0 && last >= cycles ? decodeXaSectors(all.slice(0, last + 1)) : null };
}
function resampleLinear(pcm, rate) { // port xa_next(): 16.16 position, 12-bit fraction
  const outp = []; let r = 0, frac = 0; const step = Math.floor(rate * 65536 / RATE);
  while (r + 1 < pcm.length) { const f = frac >> 4; outp.push(pcm[r] + ((pcm[r + 1] - pcm[r]) * f >> 12)); frac += step; r += frac >>> 16; frac &= 0xFFFF; }
  return Int16Array.from(outp);
}

// ---- WAV ------------------------------------------------------------------------------------------
function wav(channels, rate, interleaved, loop) {
  const smpl = loop ? 60 : 0, b = Buffer.alloc(44 + interleaved.length * 2 + smpl);
  b.write('RIFF', 0); b.writeUInt32LE(36 + interleaved.length * 2 + smpl, 4); b.write('WAVE', 8); b.write('fmt ', 12);
  b.writeUInt32LE(16, 16); b.writeUInt16LE(1, 20); b.writeUInt16LE(channels, 22); b.writeUInt32LE(rate, 24);
  b.writeUInt32LE(rate * channels * 2, 28); b.writeUInt16LE(channels * 2, 32); b.writeUInt16LE(16, 34); b.write('data', 36); b.writeUInt32LE(interleaved.length * 2, 40);
  for (let i = 0; i < interleaved.length; i++) b.writeInt16LE(interleaved[i], 44 + i * 2);
  if (loop) { // 'smpl' chunk with one forward loop (sample frames, end inclusive)
    const o = 44 + interleaved.length * 2; b.write('smpl', o); b.writeUInt32LE(52, o + 4); b.writeUInt32LE(Math.round(1e9 / rate), o + 16); b.writeUInt32LE(60, o + 20);
    b.writeUInt32LE(1, o + 36); b.writeUInt32LE(loop.start, o + 52); b.writeUInt32LE(loop.end - 1, o + 56);
  }
  return b;
}
const stats = pcm => { let peak = 0, sum = 0; for (const v of pcm) { const a = Math.abs(v); if (a > peak) peak = a; sum += v * v; } return { peak, rms: +Math.sqrt(sum / Math.max(1, pcm.length)).toFixed(1) }; };

// ---- triggers (hand-curated from the MIPS call sites; docs/xena-sfx.md has the evidence) ------------
const TRIGGERS = JSON.parse(fs.readFileSync(new URL('./sfx_triggers.json', import.meta.url)));

// ---- main -----------------------------------------------------------------------------------------
fs.mkdirSync(out, { recursive: true });
const report = { tool: 'export_sfx.mjs', rate: RATE, soundSet: { set: setIndex, table: '0x800EE550', banks: loadedSlots.map(s => ({ slot: s, name: bankNames[s] })) },
  playback: TRIGGERS.playback, attenuation: TRIGGERS.attenuation, sounds: [] };
const vabs = {};
for (const slot of loadedSlots) {
  const name = bankNames[slot];
  vabs[slot] = readVab(resource(`DATA\\SND\\${name.toUpperCase()}.VH`).bytes, resource(`DATA\\SND\\${name.toUpperCase()}.VB`).bytes);
}
const nameOf = id => (TRIGGERS.sounds[hex(id, id >= 0x1000 ? 4 : 2)]?.name) ?? null;
for (const e of soundTable) {
  if (!loadedSlots.includes(e.slot)) continue; // func_800847C0 skips ids whose bank slot is not loaded
  const vab = vabs[e.slot], prog = vab.programs[e.prog];
  const key = hex(e.id), info = TRIGGERS.sounds[key] ?? {};
  const name = (info.name ?? `${bankNames[e.slot]}_p${e.prog}`).replace(/[^a-z0-9_]+/gi, '_');
  const r = renderProgram(vab, e.prog, { note: 60 });
  const file = `${key}_${name}.wav`;
  const pcm = new Int16Array(r.left.length * 2); for (let n = 0; n < r.left.length; n++) { pcm[n * 2] = r.left[n]; pcm[n * 2 + 1] = r.right[n]; }
  fs.writeFileSync(path.join(out, file), wav(2, RATE, pcm));
  const tones = r.tones.map((t, k) => {
    const pitch = ssPitchFromNote(60, 0, t.center, t.shift), loop = r.loops[k];
    const rec = { vag: t.vag, center: t.center, shift: t.shift, range: [t.min, t.max], toneVol: t.vol, tonePan: t.pan, prior: t.prior, adsr1: hex(t.adsr1, 4), adsr2: hex(t.adsr2, 4),
      spuPitch: hex(pitch, 4), sampleRateAtNote60: +(RATE * Math.min(pitch, 0x4000) / 4096).toFixed(2), sampleFrames: loop.end, looped: loop.looped, loopStartFrame: loop.looped ? loop.loopStart : null };
    if (flag('--raw') || loop.looped) { // natural-rate sample: exact loop points for an engine-side loop
      const raw = decodeVagRaw(vab, t.vag), rf = `${key}_${name}_vag${t.vag}_raw.wav`;
      const rate = Math.round(RATE * Math.min(pitch, 0x4000) / 4096);
      fs.writeFileSync(path.join(out, rf), wav(1, rate, raw, loop.looped ? { start: loop.loopStart, end: raw.length } : null));
      rec.rawFile = rf; rec.rawRate = rate;
    }
    return rec;
  });
  const variants = (info.renderNotes ?? []).filter(n => n !== 60).map(n => { // notes the game itself uses (same vol 127, pan 64)
    const v = renderProgram(vab, e.prog, { note: n }), f = `${key}_${name}_n${n}.wav`;
    const p = new Int16Array(v.left.length * 2); for (let k = 0; k < v.left.length; k++) { p[k * 2] = v.left[k]; p[k * 2 + 1] = v.right[k]; }
    fs.writeFileSync(path.join(out, f), wav(2, RATE, p));
    return { note: n, file: f, frames: v.left.length, spuPitch: v.tones.map(t => hex(ssPitchFromNote(n, 0, t.center, t.shift), 4)) };
  });
  report.sounds.push({ id: key, kind: 'vab', name, file, bank: bankNames[e.slot], bankSlot: e.slot, program: e.prog, programPrior: prog.prior, programMvol: prog.mvol,
    defaultCall: { note: 60, vol: 127, pan: 64 }, variants, spuVolume: [r.volL * 129, r.volR * 129], tones, loop: tones.some(t => t.looped),
    keyOffFrame: r.keyOffAt, frames: r.left.length, seconds: +(r.left.length / RATE).toFixed(3), ...stats(r.left), triggers: info.triggers ?? [], notes: info.notes });
}
let XA_LBA = 0;
if (xaMode !== 'none') {
  XA_LBA = xaFileLba(); binFd = fs.openSync(binPath, 'r');
  const ids = xaMode === 'all' ? Array.from({ length: XA_GROUPS * 31 }, (_, i) => 0x1000 + i)
    : Object.keys(TRIGGERS.sounds).map(k => Number(k)).filter(v => v >= 0x1000 && v < 0x2000);
  for (const id of [...new Set(ids)].sort((a, b) => a - b)) {
    const c = xaClip(id); if (!c || !c.sectors) continue;
    const key = hex(id, 4), info = TRIGGERS.sounds[key] ?? {};
    const name = (info.name ?? `xa_g${c.q}_c${c.ch}`).replace(/[^a-z0-9_]+/gi, '_');
    const pcm = resampleLinear(c.pcm, c.rate), file = `${key}_${name}.wav`;
    fs.writeFileSync(path.join(out, file), wav(1, RATE, pcm));
    let fullFile = null; // not what the game plays: the whole recording, for reference / an optional fix
    if (c.fullPcm) { fullFile = `${key}_${name}_fullrecording.wav`; fs.writeFileSync(path.join(out, fullFile), wav(1, RATE, resampleLinear(c.fullPcm, c.rate))); }
    report.sounds.push({ id: key, kind: 'xa', name, file, xa: { file: 'MEDIA\\XA1.XA', fileLba: XA_LBA, group: c.q, groupSectorOffset: xaGroupOffsets[c.q], channel: c.ch, lengthTableValue: c.extra,
      audioSectors: c.sectors, coding: hex(c.coding), sourceRate: c.rate, eofSector: c.eofSector, content: c.content, fullRecordingFile: fullFile }, defaultCall: { priority: 64, vol: 127, pan: 64 },
      frames: pcm.length, seconds: +(pcm.length / RATE).toFixed(3), ...stats(pcm), triggers: info.triggers ?? [], notes: info.notes });
  }
}
report.sounds.sort((a, b) => parseInt(a.id) - parseInt(b.id));
for (const g of TRIGGERS.randomGroups ?? []) for (const s of report.sounds) if (g.ids.includes(s.id)) s.randomGroup = g;
fs.writeFileSync(path.join(out, 'sfx.json'), JSON.stringify(report, null, 1) + '\n');
console.log(JSON.stringify({ out, vab: report.sounds.filter(s => s.kind === 'vab').length, xa: report.sounds.filter(s => s.kind === 'xa').length,
  looped: report.sounds.filter(s => s.loop).map(s => s.id), silent: report.sounds.filter(s => s.peak === 0).map(s => s.id) }));
