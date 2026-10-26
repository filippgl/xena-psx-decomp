#!/usr/bin/env node
// Export the in-game MESSAGE WINDOW (signs, scrolls, first-pickup messages) and the LEVEL-COMPLETE (stats) screen of
// Xena: Warrior Princess (SLUS-00977) for a Unity rebuild: pixel-exact label PNGs in the game font (additive layer, shadow
// and glow masks, in screen coordinates), the font glyph atlas with metrics, the next-level load screen, and a JSON with
// layout rules, colours, timing and input. Optional verification composes every captured native frame from a
// "background" run (same inputs, text strings blanked in RAM) plus the modelled text and compares it pixel by pixel.
// Evidence: docs/xena-message-window.md.
//
// Evidence chain (MIPS addresses in the SLUS-00977 executable):
//   * openers: func_8006E4CC(p0) sign -> message 0x46+p0; func_8006E41C(n) scroll -> level*2+n-1; func_8006E468(bit)
//     first pickup of a kind -> 0x36+log2(bit) once (mask D_800F1190). Each sets D_800ED74A = id, D_800F125C = 1,
//     delay D_800F10F0 = 15 and calls func_800672F8 (the pause request: game state 12 only). main then pauses exactly
//     like START (func_80067360: D_800F0E00 = 1, SFX muted, subtractive fade func_800A7F88(cam, 2, 2, 22)).
//   * while paused main 0x80066384 calls func_8006E508 instead of the pause screen when D_800F125C != 0.
//   * func_8006E508: lines = D_800CCCD0[id] (NULL -> D_800CCA08), n = non-NULL entries (max 8); pen (160, 114 - 8n);
//     align 2 (centre), style 8 (shadow), colour (50,150,150); each line printed with "%s\n" (func_800B9300).
//     When D_800F10F0 == 0: colour (64,200,64), y 194, prompt D_800CCEA4; then if HELD pad D_800F16D8 & 0x800 (CROSS):
//     id 0x71 -> func_8006AA04 (main menu), otherwise func_800673E4 (resume, fade out).
//   * main 0x80066008 decrements D_800F10F0 once per main-loop pass (not per vblank).
//   * font stream func_800B95BC: '\n' -> y += 1 + 16, centre alignment shifts each finished line by trunc(width/2).
//   * level complete: func_8006C2D0 (state 13) -> func_8006C2E4 -> func_8006C4D0 (stats, state 14, delay 10) and,
//     when cfg+0xF != 0x12, state 15 + background TIM; fade func_800A7F88(cam, 32, 2, 32); screen func_8006CCF4.
//   * func_8006CCF4: reveal step D_800F1244 += 2 every 30 vblanks (sound 0x38), any new press skips to the end;
//     title (125,125,70) centred at (160,34) "%s\n\n"; rows func_8006C3D8 (label, left x 45) + func_8006C424
//     ("%d\n", right x 270) / func_8006C464 ("%3d   " right x 257 + " %d\n" left x 257), colour (100,100,100);
//     prompts func_8006D734(1) at (160,210) colour (175,200,50) style 0xA, save prompt at (160,194);
//     CROSS (new press) when step >= 11 -> D_800ED6AE = 1 -> main 0x800663EC -> func_8006C04C(0) (next level load);
//     0x1000 (CIRCLE) -> func_800675F0 (memory card save screen, state 4).
//
//   node tools/export_message_window.mjs --root DECOMP --out DIR [--verify DIR_OF_RUNS]
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i < 0 ? fallback : argv[i + 1]; };
const root = path.resolve(option('--root', '.'));
const out = path.resolve(option('--out', 'message-window-export'));
const here = path.dirname(fileURLToPath(import.meta.url));
const { png } = await import(pathToFileURL(path.join(here, 'lib', 'png.mjs')).href);
fs.mkdirSync(out, { recursive: true });
const hex = n => '0x' + (n >>> 0).toString(16).toUpperCase();

// ---------- executable ----------
const exe = fs.readFileSync(path.join(root, 'xena.exe'));
const base = exe.readUInt32LE(0x18);
const eo = a => a - base + 0x800;
const u32 = a => exe.readUInt32LE(eo(a));
const u8 = a => exe[eo(a)];
const cbytes = a => { const o = eo(a); return exe.subarray(o, exe.indexOf(0, o)); };
const imm = a => (u32(a) << 16) >> 16;
// Russian build: Latin code points draw Cyrillic glyphs (docs/xena-level-scripts.md; symbols INFERRED)
const GLYPH = { A: 'А', B: 'В', C: 'С', D: 'Д', E: 'Е', F: 'Ы', G: 'Ш', H: 'Н', I: 'І', J: 'П', K: 'К', L: 'Л', M: 'М', N: 'И', O: 'Ф',
  P: 'Р', Q: 'Э', R: 'Я', S: 'Ь', T: 'Т', U: 'Ю', V: 'Ц', W: 'Ж', X: 'Х', Y: 'У', Z: 'Г' };
const SYMBOL = { '$': 'о', '&': 'б', '+': 'л', '#': 'ч', '@': 'з', '0': 'О', '3': 'З', '4': 'Ч', '6': 'Б' };
const translit = b => [...b].map(c => { const ch = String.fromCharCode(c);
  if (c < 32) return c === 10 ? '\n' : c === 4 ? '{ⓧ}' : c === 3 ? '{○}' : `{${c}}`;
  if (SYMBOL[ch]) return SYMBOL[ch]; const up = ch.toUpperCase();
  return GLYPH[up] ? (ch === up ? GLYPH[up] : GLYPH[up].toLowerCase()) : ch; }).join('');
const strAt = p => { const b = cbytes(p); return { address: hex(p), bytes: [...b], latin1: b.toString('latin1'), text: translit(b) }; };
const ptrStr = ptrAddr => ({ pointer: hex(ptrAddr), ...strAt(u32(ptrAddr)) });

// immediates this exporter relies on (fail if the executable differs)
const CHECKS = [[0x8006E56C, 0xA0], [0x8006E574, 0x72], [0x8006E584, 0x2], [0x8006E58C, 0x8], [0x8006E590, 0x32], [0x8006E594, 0x96], [0x8006E59C, 0x96],
  [0x8006E5E4, 0x40], [0x8006E5E8, 0xC8], [0x8006E5F0, 0x40], [0x8006E5F8, 0xC2], [0x8006E624, 0x71], [0x8006E498, 0x36], [0x8006E4D0, 0x46], [0x8006E4DC, 0xF],
  [0x8006CE70, 0x7D], [0x8006CE74, 0x7D], [0x8006CE7C, 0x46], [0x8006CE84, 0x2], [0x8006CE88, 0xA0], [0x8006CE90, 0x22], [0x8006CDDC, 0x38],
  [0x8006CF04, 0x64], [0x8006CF08, 0x64], [0x8006CF10, 0x64], [0x8006CF38, 0x10E], [0x8006C3EC, 0x3], [0x8006C3F4, 0x2D], [0x8006C438, 0x1], [0x8006C440, 0x10E],
  [0x8006C488, 0x101], [0x8006C4A0, 0x3], [0x8006C4A8, 0x101], [0x8006D740, 0xD2], [0x8006D758, 0xA0], [0x8006D75C, 0xAF], [0x8006D760, 0xC8], [0x8006D768, 0x32],
  [0x8006D770, 0x2], [0x8006D778, 0xA]];
const badChecks = CHECKS.filter(([a, v]) => imm(a) !== v).map(([a, v]) => `${hex(a)}=${imm(a)} (expected ${v})`);

// ---------- messages ----------
const LEVEL = 1, CFG = 0x800C18E4 + 40 * LEVEL;
function message(id) {
  let table = u32(0x800CCCD0 + 4 * id); const fallback = !table; if (fallback) table = 0x800CCA08;
  const lines = []; for (let i = 0; i < 8; i++) { const p = u32(table + 4 * i); if (!p) break; lines.push(strAt(p)); }
  return { id, idHex: hex(id), table: hex(table), fallback, lines };
}
const LEVEL01_MESSAGES = [
  { id: 0x46, source: 'sign_pt #0 (PLRS p0 = 0) at (-5283,-1900,-9432), func_800406C8 -> func_8006E4CC(0)' },
  { id: 0x47, source: 'sign_pt #1 (p0 = 1) at (-9998,333,-6501)' },
  { id: 0x48, source: 'sign_pt #2 (p0 = 2) at (10888,3712,7945)' },
  { id: 0x02, source: 'scroll #5 (+0x10 = 1) at (-5675,-1764,-15865): func_8006E41C(1) -> 1*2+1-1' },
  { id: 0x03, source: 'scroll #6 (+0x10 = 2) at (-4631,-1652,-1777): func_8006E41C(2)' },
  { id: 0x36, source: 'first health2 potion: func_8006E468(0x1) -> 0x36 + 0' },
  { id: 0x45, source: 'first up_sword: func_8006E468(0x8000) -> 0x36 + 15' },
].map(m => ({ ...m, ...message(m.id) }));
const MSG_PROMPT = ptrStr(0x800CCEA4);
const MSG_0x71 = message(0x71);

// ---------- disc / font ----------
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'build/asset-audit/manifest.json')));
const uda = fs.readFileSync(path.join(root, 'disc/files/XENA.UDA'));
// the game's own lookup (func_800A4B1C, tools/uda_find.py): index of 0x52A {hash, lba, size} at 0x800D5424,
// hash = sum(lower(c) * weight[i]) over "\NAME;1", weights at 0x800DA6C4
function udaLookup(name) {
  const s = ('\\' + name.replace(/^\\+/, '') + ';1').toLowerCase(); let h = 0;
  for (let i = 0; i < s.length; i++) h = (h + s.charCodeAt(i) * exe.readInt32LE(eo(0x800DA6C4) + 4 * i)) | 0;
  for (let i = 0; i < 0x52A; i++) { const o = eo(0x800D5424) + 16 * i; if (exe.readInt32LE(o) === h) return { lba: exe.readUInt32LE(o + 4), size: exe.readUInt32LE(o + 8) }; }
  return null;
}
function resource(name) {
  const hits = manifest.entries.filter(e => e.candidates.some(c => c.name.toUpperCase() === name.toUpperCase()));
  const hit = hits.length === 1 && hits[0].inArchive ? hits[0] : udaLookup(name);
  if (!hit) throw Error(`Missing or ambiguous ${name}`);
  const at = (hit.lba - manifest.summary.archiveLba) * 2048;
  return { name, lba: hit.lba, bytes: uda.subarray(at, at + hit.size) };
}
function readTim(bytes) {
  if (bytes.readUInt32LE(0) !== 0x10) throw Error('Not a TIM');
  const flags = bytes.readUInt32LE(4); let cursor = 8;
  const block = () => { const size = bytes.readUInt32LE(cursor), [x, y, width, height] = [4, 6, 8, 10].map(n => bytes.readUInt16LE(cursor + n));
    const data = bytes.subarray(cursor + 12, cursor + 12 + width * height * 2); cursor += size; return { x, y, width, height, data }; };
  const clut = flags & 8 ? block() : null;
  return { depth: flags & 7, clut, image: block() };
}
function rgb8(c) { return [0, 5, 10].map(n => (((c >>> n) & 31) << 3) | (((c >>> n) & 31) >>> 2)); }
function decode(img) {
  const k = [4, 2, 1][img.depth], w = img.image.width * k, h = img.image.height, words = new Uint16Array(w * h);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const word = img.image.data.readUInt16LE((y * img.image.width + Math.floor(x / k)) * 2);
    words[y * w + x] = img.depth === 0 ? img.clut.data.readUInt16LE(((word >>> ((x & 3) * 4)) & 15) * 2)
      : img.depth === 1 ? img.clut.data.readUInt16LE(((word >>> ((x & 1) * 8)) & 255) * 2) : word;
  }
  const rgba = new Uint8Array(w * h * 4);
  for (let i = 0; i < w * h; i++) rgba.set([...rgb8(words[i]), (img.depth === 2 || words[i]) ? 255 : 0], i * 4);
  return { w, h, words, rgba };
}
function writePng(rel, d) { fs.mkdirSync(path.dirname(path.join(out, rel)), { recursive: true }); fs.writeFileSync(path.join(out, rel), png(d.w, d.h, d.rgba)); return rel; }

const fontRaw = decode(readTim(resource('DATA\\SCREENS\\XENAFONT.TIM').bytes));
const fontWords = fontRaw.words.map(c => (c & 0x7FFF) ? (c | 0x8000) : 0); // runtime CLUT: STP on every visible entry
const FW = fontRaw.w;
const glyphOf = c => { if (c >= 0x61 && c < 0x61 + 26) return c - 0x21; if ((c >= 0x41 && c < 0x41 + 30) || (c >= 0x20 && c < 0x20 + 33)) return c - 0x20; return { 1: 0x5D, 2: 0x5C, 4: 0x5B, 3: 0x5A }[c] ?? -1; };
const cellsX = FW >> 4, glyphCount = cellsX * ((fontRaw.h - 32) >> 4);
const glyphWidth = Array.from({ length: glyphCount }, (_, g) => { let wmax = 0; const gx = (g % cellsX) * 16, gy = 32 + Math.floor(g / cellsX) * 16;
  for (let y = 0; y < 16; y++) for (let x = 0; x < 16; x++) if ((fontRaw.words[(gy + y) * FW + gx + x] & 0x7FFF) && x > wmax) wmax = x; return wmax; });
glyphWidth[glyphOf(0x20)] = 2; glyphWidth[glyphOf(0x67)] -= 1; for (let c = 0x30; c <= 0x39; c++) glyphWidth[glyphOf(c)] = 7;
const glyphYOffset = g => { if (g >= 0x21 && g < 0x21 + 26) return 0;
  if (g >= 0x40 && g < 0x40 + 26) return { [glyphOf(0x6A)]: 3, [glyphOf(0x67)]: 2, [glyphOf(0x70)]: 2, [glyphOf(0x71)]: 2, [glyphOf(0x79)]: 2 }[g] ?? -1;
  return g >= glyphOf(0x30) && g <= glyphOf(0x3F) ? 1 : 0; };
const isDigitGlyph = g => g >= glyphOf(0x30) && g <= glyphOf(0x39);
const digitDx = g => (g === glyphOf(0x32) || g === glyphOf(0x34) ? -8 : -9);
const LINE_STEP = 17; // func_800B95BC: '\n' -> y += 1 + glyph size (16)

// ---------- text engine: one func_800B9300 call (formatted string) at the font pen ----------
function sprintf(fmt, args) {
  let i = 0, o = '';
  for (let k = 0; k < fmt.length; k++) { const ch = fmt[k]; if (ch !== '%') { o += ch; continue; }
    const m = /^%(0?)(\d*)(?:\.(\d+))?([sd%])/.exec(fmt.slice(k)); if (!m) { o += ch; continue; } k += m[0].length - 1;
    if (m[4] === '%') { o += '%'; continue; } const v = args[i++];
    if (m[4] === 's') { o += v; continue; }
    let s = String(Math.abs(v)); if (m[3]) s = s.padStart(+m[3], '0'); if (v < 0) s = '-' + s;
    o += m[2] ? s.padStart(+m[2], m[1] ? '0' : ' ') : s; }
  return o;
}
const asLatin1 = b => Buffer.isBuffer(b) ? b.toString('latin1') : typeof b === 'string' ? b : Buffer.from(b).toString('latin1');
// pen = { x, y }, state = { align, style, rgb }. Returns glyph quads (screen top-left) and updates the pen like the game.
function printCall(pen, st, fmtBytes, args = []) {
  const text = Buffer.from(sprintf(asLatin1(fmtBytes), args.map(a => (typeof a === 'object' && a.bytes) ? asLatin1(a.bytes) : a)), 'latin1');
  const x0 = pen.x, y0 = pen.y, glyphs = []; let px = 0, dy = 0, lineStart = 0, maxw = 0;
  const finishLine = () => { if (st.align === 2) { const sh = Math.trunc(px / 2); for (let j = lineStart; j < glyphs.length; j++) glyphs[j].x -= sh; } lineStart = glyphs.length; };
  for (const c of text) {
    if (c === 10) { finishLine(); dy += LINE_STEP; px = 0; continue; }
    if (c === 9) { px = (px + 32) & ~15; continue; }
    const g = glyphOf(c); if (g < 0) continue;
    const adv = isDigitGlyph(g) ? 10 : glyphWidth[g] + 3;
    glyphs.push({ c, g, x: px + (isDigitGlyph(g) ? digitDx(g) : 0), y: dy + glyphYOffset(g), u: (g % cellsX) * 16, v: 32 + Math.floor(g / cellsX) * 16 });
    maxw = Math.max(maxw, px + adv); px += adv;
  }
  finishLine();
  const sx = st.align === 1 ? x0 - maxw : x0;
  pen.y = y0 + dy; // D_801002DC = stream y + advanced lines; D_801002DA = pen x (start x after a final '\n')
  return { text: [...text], rgb: [...st.rgb], style: st.style, align: st.align, anchor: [x0, y0], glyphs: glyphs.map(q => ({ ...q, x: q.x + sx, y: q.y + y0 })) };
}
const mod = (t5, c) => Math.min(31, (t5 * c) >> 7);
const pulse = clock => (clock & 0x20) ? (clock & 31) * 7 : ((32 - (clock & 31)) * 255) >> 5;
const ch5 = (w, n) => (w >>> n) & 31, pack = (r, g, b) => r | (g << 5) | (b << 10);
function each(q, dx, dy, fn) { for (let yy = 0; yy < 16; yy++) for (let xx = 0; xx < 16; xx++) {
  const c = fontWords[(q.v + yy) * FW + q.u + xx]; if (!c) continue; const X = q.x + xx + dx, Y = q.y + yy + dy;
  if (X < 0 || X >= 320 || Y < 0 || Y >= 240) continue; fn(Y * 320 + X, c); } }
// func_800B9CA0 order per glyph: shadow (+1,+1),(+2,+2) abr 0 colour 1; glyph abr 1; glow 4 copies abr 3
function drawCall(fb, t, clock) {
  for (const q of t.glyphs) {
    if (t.style & 8) for (const d of [1, 2]) each(q, d, d, (i, T) => { fb[i] = pack(...[0, 5, 10].map(n => (ch5(fb[i], n) + mod(ch5(T, n), 1)) >> 1)); });
    each(q, 0, 0, (i, T) => { fb[i] = pack(...[0, 5, 10].map((n, k) => Math.min(31, ch5(fb[i], n) + mod(ch5(T, n), t.rgb[k])))); });
    if (t.style & 7) { const p = pulse(clock), col = [t.style & 1 ? p : 15, t.style & 2 ? p : 15, t.style & 4 ? p : 15];
      for (const [dx, dy] of [[1, 0], [-1, 0], [0, -1], [0, 1]]) each(q, dx, dy, (i, T) => { fb[i] = pack(...[0, 5, 10].map((n, k) => Math.min(31, ch5(fb[i], n) + (mod(ch5(T, n), col[k]) >> 2)))); }); }
  }
}
// exported label: additive layer / shadow mask / glow mask, cropped, with its screen top-left
function labelImages(name, calls) {
  const add = new Map(), shadow = new Set(), glow = new Set(); let minX = 320, minY = 240, maxX = -1, maxY = -1;
  const grow = (X, Y) => { minX = Math.min(minX, X); minY = Math.min(minY, Y); maxX = Math.max(maxX, X); maxY = Math.max(maxY, Y); };
  for (const t of calls) for (const q of t.glyphs) {
    each(q, 0, 0, (i, T) => { add.set(i, [0, 5, 10].map((n, k) => { const v = mod(ch5(T, n), t.rgb[k]); return (v << 3) | (v >> 2); })); grow(i % 320, i / 320 | 0); });
    if (t.style & 8) for (const d of [1, 2]) each(q, d, d, i => { shadow.add(i); grow(i % 320, i / 320 | 0); });
    if (t.style & 7) for (const [dx, dy] of [[1, 0], [-1, 0], [0, -1], [0, 1]]) each(q, dx, dy, i => { glow.add(i); grow(i % 320, i / 320 | 0); });
  }
  if (maxX < 0) return null;
  const w = maxX - minX + 1, h = maxY - minY + 1;
  const img = fill => { const rgba = new Uint8Array(w * h * 4); fill(rgba); return { w, h, rgba }; };
  const at = i => (((i / 320 | 0) - minY) * w + (i % 320) - minX) * 4;
  const r = { name, screenTopLeft: [minX, minY], size: [w, h],
    add: writePng(`labels/${name}.png`, img(a => { for (const [i, v] of add) a.set([...v, 255], at(i)); })),
    shadow: shadow.size ? writePng(`labels/${name}_shadow.png`, img(a => { for (const i of shadow) a.set([0, 0, 0, 128], at(i)); })) : null };
  if (glow.size) r.glow = writePng(`labels/${name}_glow.png`, img(a => { for (const i of glow) a.set([255, 255, 255, 255], at(i)); }));
  return r;
}

// ---------- screen models ----------
const MSG = { penX: imm(0x8006E56C), baseY: imm(0x8006E574), perLine: 8, align: 2, style: 8, rgb: [imm(0x8006E590), imm(0x8006E594), imm(0x8006E59C)],
  prompt: { y: imm(0x8006E5F8), rgb: [imm(0x8006E5E4), imm(0x8006E5E8), imm(0x8006E5F0)] }, fmt: strAt(0x800ED5F0) };
function messageCalls(msg, withPrompt) {
  const n = msg.lines.length, pen = { x: MSG.penX, y: MSG.baseY - MSG.perLine * n }, st = { align: 2, style: 8, rgb: MSG.rgb }, calls = [];
  for (const l of msg.lines) calls.push({ kind: 'line', ...printCall(pen, st, MSG.fmt.bytes, [l]) });
  if (withPrompt) { pen.y = MSG.prompt.y; calls.push({ kind: 'prompt', ...printCall(pen, { ...st, rgb: MSG.prompt.rgb }, MSG_PROMPT.bytes) }); }
  return calls;
}
const STATS = {
  title: ptrStr(CFG + 0x14), titleFmt: strAt(0x800ED6C0),
  labels: [0, 2, 4, 6, 8, 10].map(i => ({ index: i, ...ptrStr(0x800CCF20 + 4 * i) })),
  timeFmtH: strAt(0x800ED6C8), timeFmt: strAt(0x800ED6DC), countFmt: strAt(0x800ED6B0), ofLeftFmt: ptrStr(0x800F0150), ofRightFmt: strAt(0x800ED6B4),
  continuePrompt: ptrStr(0x800CCEA0 + 4), savePrompt: ptrStr(0x800F0168),
  final: { quest: ptrStr(0x800F0188), rankFmt: ptrStr(0x800F01DC), results: ptrStr(0x800F01A4), ranks: [0, 1, 2, 3].map(i => ptrStr(0x800CCF4C + 4 * i)) },
};
// s: RAM state of one pass (see readStats). Returns text calls in creation order (func_8006CCF4, non-final level).
function statsCalls(s) {
  const pen = { x: 0, y: 0 }, calls = [], st = { align: 2, style: 8, rgb: [0x7D, 0x7D, 0x46] };
  const put = (kind, fmt, args, x) => { if (x !== undefined) pen.x = x; calls.push({ kind, ...printCall(pen, { ...st }, fmt, args) }); };
  pen.x = 0xA0; pen.y = 0x22; put('title', STATS.titleFmt.bytes, [STATS.title]);
  st.style = 8; st.rgb = [0x64, 0x64, 0x64];
  const label = i => { st.align = 3; put(`label${i}`, STATS.labels.find(l => l.index === i).bytes, [], 0x2D); };
  const count = (i, v) => { st.align = 1; put(`value${i}`, STATS.countFmt.bytes, [v], 0x10E); };
  const of = (i, a, b) => { st.align = 1; put(`value${i}a`, STATS.ofLeftFmt.bytes, [a], 0x101); st.align = 3; put(`value${i}b`, STATS.ofRightFmt.bytes, [b], 0x101); };
  if (s.step) { label(0); st.align = 1; pen.x = 0x10E;
    calls.push({ kind: 'value0', ...printCall(pen, { ...st }, s.hours ? STATS.timeFmtH.bytes : STATS.timeFmt.bytes, s.hours ? [s.hours, s.minutes, s.seconds] : [s.minutes, s.seconds]) }); }
  if (s.step >= 3) { label(2); count(2, s.kills); }
  if (s.hostages && s.step >= 5) { label(4); count(4, s.hostages); }
  if (s.step >= 7) { label(6); count(6, s.items); }
  if (s.step >= 9) { label(8); of(8, s.scrolls, 2); }
  if (s.step >= 11) { st.rgb = [0x7D, 0x7D, 0x46]; label(10); of(10, s.totalScrolls, s.totalScrollsMax); }
  if (s.timer === 0) { pen.x = 0xA0; pen.y = 0xD2; st.rgb = [0xAF, 0xC8, 0x32]; st.align = 2; st.style = 0xA; put('continue', STATS.continuePrompt.bytes, []); }
  if (!s.saveBusy && s.done && s.timer === 0 && !(s.card === -1 && s.card2 === 1)) { pen.x = 0xA0; pen.y = 0xC2; if (s.card !== 1) put('save', STATS.savePrompt.bytes, []); }
  return calls;
}
const readStats = m => ({ step: m[0xF1244], timer: m.readInt16LE(0xF10F0), done: m[0xED6AF], saveBusy: m[0xED663], card: m.readInt32LE(0xF114C), card2: m.readInt32LE(0xF1148),
  hours: m.readUInt16LE(0xF1240), minutes: m[0xF1242], seconds: m[0xF1243], kills: m.readInt16LE(0xF1224), hostages: m.readInt16LE(0xF1228), items: m.readInt16LE(0xF122C),
  scrolls: m.readInt16LE(0xF1230), totalScrolls: m[0xF277B], totalScrollsMax: m.readInt16LE(0xF1232), clock: m.readUInt32LE(0xF16C0), state: m.readUInt32LE(0xF2648) });

// ---------- exported labels ----------
const labels = { messages: [], stats: [] };
for (const msg of LEVEL01_MESSAGES) {
  const calls = messageCalls(msg, false);
  labels.messages.push({ id: msg.idHex, block: labelImages(`msg_${msg.id.toString(16).padStart(2, '0')}`, calls),
    lines: calls.map((c, i) => ({ text: msg.lines[i].text, anchor: c.anchor, label: labelImages(`msg_${msg.id.toString(16).padStart(2, '0')}_line${i}`, [c]) })) });
}
labels.messages.push({ id: 'prompt', block: labelImages('msg_prompt', messageCalls({ lines: [] }, true)) });
// level-01 stats screen as captured (time 0:28, all counts 0, scrolls 0 of 2): static labels + the full screen as one block
const L01 = { step: 12, timer: 0, done: 1, saveBusy: 0, card: 0, card2: 0, hours: 0, minutes: 0, seconds: 28, kills: 0, hostages: 0, items: 0, scrolls: 0, totalScrolls: 0, totalScrollsMax: 2 };
const l01calls = statsCalls(L01);
for (const c of l01calls) labels.stats.push({ kind: c.kind, text: c.kind.startsWith('value') ? Buffer.from(c.text).toString('latin1') : translit(Buffer.from(c.text)), anchor: c.anchor, align: c.align, style: c.style, colour: c.rgb, label: labelImages(`stats_${c.kind}`, [c]) });
labels.stats.push({ kind: 'all (level 01 capture values)', label: labelImages('stats_level01_full', l01calls) });
// hostages row (not on level 01) for completeness
labels.stats.push({ kind: 'label4 (hostages, only when non-zero)', label: labelImages('stats_label4', [printCall({ x: 0x2D, y: 0x77 }, { align: 3, style: 8, rgb: [0x64, 0x64, 0x64] }, STATS.labels.find(l => l.index === 4).bytes)]) });

// font atlas: one 16x16 cell per glyph, texel colours as stored (premultiply by colour/128 at draw time)
const atlas = { w: FW, h: fontRaw.h - 32, rgba: new Uint8Array(FW * (fontRaw.h - 32) * 4) };
for (let y = 0; y < atlas.h; y++) for (let x = 0; x < FW; x++) { const c = fontWords[(y + 32) * FW + x]; if (c) atlas.rgba.set([...rgb8(c), 255], (y * FW + x) * 4); }
const fontPng = writePng('font/xenafont_glyphs.png', atlas);
const glyphTable = [];
for (let c = 1; c < 0x7B; c++) { const g = glyphOf(c); if (g < 0 || g >= glyphCount) continue;
  glyphTable.push({ byte: c, char: translit(Buffer.from([c])), glyph: g, cell: [(g % cellsX) * 16, Math.floor(g / cellsX) * 16], width: glyphWidth[g],
    advance: isDigitGlyph(g) ? 10 : glyphWidth[g] + 3, drawDx: isDigitGlyph(g) ? digitDx(g) : 0, yOffset: glyphYOffset(g) }); }

// next-level load screen (func_8006C04C: data\levscr\load%02d.tim, level cfg+1 = 2)
const nextLevel = u8(CFG + 1);
let loadScreen = null;
try { const d = decode(readTim(resource(`DATA\\LEVSCR\\LOAD${String(nextLevel).padStart(2, '0')}.TIM`).bytes)); loadScreen = { file: `DATA\\LEVSCR\\LOAD${String(nextLevel).padStart(2, '0')}.TIM`, size: [d.w, d.h], png: writePng(`load/load${String(nextLevel).padStart(2, '0')}.png`, d) }; }
catch (e) { loadScreen = { error: String(e.message) }; }

const json = {
  source: 'Xena: Warrior Princess (SLUS-00977) message window and level-complete screen; see docs/xena-message-window.md',
  executableChecks: badChecks.length ? { failed: badChecks } : `all ${CHECKS.length} immediates match`,
  screen: { width: 320, height: 240, origin: 'top-left, PS1 framebuffer pixels' },
  font: { source: 'DATA\\SCREENS\\XENAFONT.TIM (as docs/xena-main-menu.md)', atlas: fontPng, cell: 16, lineStep: LINE_STEP,
    blend: 'SPRT16 modulated by the call colour (c5 = min(31, t5*c >> 7)), abr 1 additive (B + F), every visible texel has STP',
    shadowStyle8: 'before each glyph: copies at (+1,+1) and (+2,+2), colour (1,1,1), abr 0 => B/2',
    glowStyleBits: 'bits 1/2/4 = R/G/B pulse: after the glyph 4 copies at (+-1,0),(0,+-1), abr 3 (B + F/4); channel = clock&0x20 ? (clock&31)*7 : (32-(clock&31))*255>>5, other channels 15 (clock D_800F16C0, vblanks)',
    alignment: { 0: 'left at pen x', 3: 'left at pen x (same as 0 in func_800B9300)', 1: 'right: x - max line extent (extent includes the trailing +3 and trailing spaces)', 2: 'centre: each line shifted left by trunc(line extent / 2)' },
    glyphs: glyphTable },
  messageWindow: {
    openers: [
      { function: 'func_8006E4CC(p0)', who: 'sign / sign_pt actor (func_800406C8) on contact; 60-frame re-arm', id: '0x46 + p0' },
      { function: 'func_8006E41C(n)', who: 'scroll pickup (func_8003FBA0, after Xena pickup state 0x37)', id: 'level*2 + n - 1' },
      { function: 'func_8006E468(bit)', who: 'first pickup of an item kind (mask D_800F1190, once per game; bit 0x1000 is cleared on a new level)', id: '0x36 + log2(bit)' },
    ],
    onOpen: 'D_800ED74A = id, D_800F125C = 1, D_800F10F0 = 15, func_800672F8 (pause request; ignored unless game state 12, not already paused, D_800F0E02 == 0). Next pass: func_80067360 = the START pause: D_800F0E00 = 1, SFX voices muted, music paused (INFERRED), subtractive fade func_800A7F88(cam, 2 passes, mode 2, grey 22) (level 8: 11)',
    gameplay: 'paused: the game step D_800ECEA0 stops (frozen in every capture); the frozen scene, the HUD (func_80075C2C) and the text are re-drawn every frame',
    background: 'no box, frame, panel, portrait or icon: only the frozen scene darkened by a full-screen subtractive grey 22 (0,11,22 over the first passes) quad (0,0)-(320,239), then the HUD, then the text',
    layout: { lines: 'up to 8 line strings from D_800CCCD0[id] (NULL entry -> D_800CCA08); n = count', pen: `(${MSG.penX}, ${MSG.baseY} - ${MSG.perLine}*n)`,
      perLine: `"%s\\n": line i at y = ${MSG.baseY} - 8n + 17i (glyph cell top), centred on x = ${MSG.penX}`, align: 'centre', style: 8, colour: MSG.rgb,
      blankLines: 'a line that is a single space (0x800EE5D4) is an empty row', },
    prompt: { text: MSG_PROMPT.text, bytes: MSG_PROMPT.bytes, when: 'D_800F10F0 == 0 (15 main-loop passes after opening)', anchor: [MSG.penX, MSG.prompt.y], align: 'centre', style: 8, colour: MSG.prompt.rgb },
    close: { input: 'HELD pad word D_800F16D8 & 0x800 (CROSS in the default layout), tested only once the delay is 0: holding CROSS closes the window at the first pass with delay 0', action: 'func_800673E4: resume, fade out func_800A7F88(cam, 2, 1, 22); message 0x71 instead calls func_8006AA04 (main menu)', other: 'START is not handled by func_8006E508; main toggles pause only when not D_800F125C (INFERRED from main 0x8006628C: START resumes = also closes)' },
    level01: LEVEL01_MESSAGES.map(m => ({ id: m.idHex, source: m.source, n: m.lines.length, topY: MSG.baseY - 8 * m.lines.length, lines: m.lines })),
    message0x71: MSG_0x71,
  },
  levelComplete: {
    trigger: 'Siras (func_8007E410) after its camera path -> func_8006C2D0: game state 13 (exit actors use the same call)',
    sequence: [
      'state 13 (one pass): func_8006C2E4 -> func_8006C4D0 computes the stats, sets state 14, delay D_800F10F0 = 10, clears D_800ED6AF/D_800ED6AE/D_800ED663, func_8008699C (audio, UNKNOWN)',
      `cfg+0xF (level 1: ${hex(u8(CFG + 0xF))}) == 0x12 -> no background picture: the live scene stays behind the screen (state 14); otherwise state 15 + data\\screens\\<cfg+0x10>.tim (or bk_slate.tim) drawn by func_8006A924`,
      'fade func_800A7F88(cam, 32 passes, mode 2, grey 32): subtractive grey t*32/32 ramps 0..32 then holds; the HUD is drawn after it (not darkened)',
      'func_8006CCF4 every pass: reveal, rows, prompts, input',
    ],
    reveal: 'step D_800F1244 (0 at start): when clock D_800F16C0 >= D_800F1250: sound 0x38, step += 2, next at clock + 30 (vblanks); step 4 with no hostages adds another 2; step >= 11 -> D_800ED6AF = 1 (done). Any newly pressed button while the delay is 0 and step < 10 sets step = 10, delay 10, and the same pass adds 2 (=12, done)',
    rows: 'title (125,125,70) centred at (160,34) "%s\\n\\n"; then colour (100,100,100): each row = label left at x 45 + value; values right-aligned at x 270 ("%d\\n"), or "%3d   " right at x 257 + " %d\\n" left at x 257; rows advance 17 px from y 68; the last row ("\\nВсего свитков:") starts with a newline (blank row) and uses (125,125,70)',
    rowOrder: [
      { step: 1, label: STATS.labels[0].text, value: 'level time D_800F16B8 (vblanks): hours ? "%d:%02.2d:%02.2d" : "%d:%02.2d" (minutes:seconds)' },
      { step: 3, label: STATS.labels[1].text, value: 'D_800F1224 (enemies killed)' },
      { step: 5, label: STATS.labels[2].text, value: 'D_800F1228 (hostages) - row only when non-zero' },
      { step: 7, label: STATS.labels[3].text, value: 'D_800F122C (items collected)' },
      { step: 9, label: STATS.labels[4].text, value: 'D_800F1230 = scrolls this level, "of" 2 (non-final levels)' },
      { step: 11, label: STATS.labels[5].text, value: 'D_800F277B total scrolls, of D_800F1232' },
    ],
    prompts: [
      { text: STATS.continuePrompt.text, bytes: STATS.continuePrompt.bytes, when: 'delay == 0 (from the first pass after 10)', anchor: [160, 210], align: 'centre', style: '0xA (shadow + green glow pulse)', colour: [175, 200, 50] },
      { text: STATS.savePrompt.text, bytes: STATS.savePrompt.bytes, when: 'done, delay 0, memory card state D_800F114C != 1 and not (D_800F114C == -1 and D_800F1148 == 1)', anchor: [160, 194], align: 'centre', style: '0xA (inherited)', colour: [175, 200, 50] },
    ],
    input: { cross: 'new press 0x800 with step >= 11 and delay 0: sound 0, D_800ED6AE = 1 -> next pass func_8006C04C(0): state 10, level = cfg+1 (level 1 -> 2), load screen data\\levscr\\load02.tim, then state 12 on level 2', circle: 'new press 0x1000 when done: sound 0, func_800675F0: memory card save screen (state 4; returns to the stats screen state stored in D_800F1170, INFERRED)', any: 'any new press during the reveal skips to the end' },
    finalLevel: { level: 0x1A, text: [STATS.final.quest, STATS.final.rankFmt, STATS.final.results], ranks: STATS.final.ranks.map(r => r.text), note: 'only on level 26: title "Квест завершен !", rank line, no scroll-of-2 row, no save prompt; CROSS -> main menu (func_8006AA04)' },
    strings: STATS,
    nextLevel: { level: nextLevel, loadScreen },
  },
  labels,
};

// ---------- verification against native runs ----------
const verifyDir = option('--verify');
if (verifyDir) {
  const readBmp = f => { const b = fs.readFileSync(f), at = b.readUInt32LE(10), w = b.readInt32LE(18), h0 = b.readInt32LE(22), h = Math.abs(h0), stride = Math.ceil(w * 3 / 4) * 4, px = new Uint16Array(w * h);
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) { const o = at + (h0 > 0 ? h - 1 - y : y) * stride + x * 3; px[y * w + x] = (b[o + 2] >> 3) | ((b[o + 1] >> 3) << 5) | ((b[o] >> 3) << 10); }
    return px; };
  const shot = (run, f) => path.join(verifyDir, run, 'shots', `frame_${String(f).padStart(6, '0')}.bmp`);
  const ramf = (run, f) => path.join(verifyDir, run, 'ram', `ram_${String(f).padStart(6, '0')}.bin`);
  const LAG = 2; // RAM written at frame F appears in the screenshot at F+2 (docs/xena-main-menu.md)
  const compare = (run, bgRun, frames, model, clockOffset = 0, lag = LAG) => {
    const rows = []; let evidence = null;
    for (const f of frames) {
      if (!fs.existsSync(shot(run, f)) || !fs.existsSync(shot(bgRun, f)) || !fs.existsSync(ramf(run, f - lag))) continue;
      const A = readBmp(shot(run, f)), fb = Uint16Array.from(readBmp(shot(bgRun, f))), m = fs.readFileSync(ramf(run, f - lag));
      const calls = model(m); if (!calls) continue;
      for (const c of calls) drawCall(fb, c, m.readUInt32LE(0xF16C0) + clockOffset);
      let same = 0, diffText = 0; const bad = [];
      for (let i = 0; i < 76800; i++) if (fb[i] === A[i]) same++; else if (bad.length < 5) bad.push([i % 320, i / 320 | 0, fb[i].toString(16), A[i].toString(16)]);
      const bgSame = (() => { let n = 0; const B = readBmp(shot(bgRun, f)); for (let i = 0; i < 76800; i++) if (B[i] === A[i]) n++; return n; })();
      rows.push({ frame: f, calls: calls.map(c => c.kind).join(','), exactPixels: same, of: 76800, textPixelsChanged: 76800 - bgSame, bad });
      if (!evidence && same < 76800) evidence = { f, A, fb };
      if (!evidence && f === frames[frames.length - 1]) evidence = { f, A, fb };
    }
    return { rows, evidence };
  };
  const sheet = (rel, ev) => { if (!ev) return null; const rgba = new Uint8Array(960 * 240 * 4);
    for (let i = 0; i < 76800; i++) { const x = i % 320, y = i / 320 | 0;
      rgba.set([...rgb8(ev.A[i]), 255], (y * 960 + x) * 4); rgba.set([...rgb8(ev.fb[i]), 255], (y * 960 + 320 + x) * 4);
      rgba.set(ev.A[i] === ev.fb[i] ? [0, 0, 0, 255] : [255, 0, 0, 255], (y * 960 + 640 + x) * 4); }
    return writePng(rel, { w: 960, h: 240, rgba }); };
  const results = { method: 'composite = background run screenshot (same inputs, text strings blanked with --poke) + modelled text from the RAM state 2 frames earlier; compared in 15-bit colour over the full 320x240 frame', messages: [], stats: null };
  for (const msg of LEVEL01_MESSAGES) {
    const run = `m${msg.id.toString(16).padStart(2, '0')}`; if (!fs.existsSync(path.join(verifyDir, run))) continue;
    const frames = []; for (let f = 8600; f <= 8740; f += 2) frames.push(f);
    const { rows, evidence } = compare(run, run + 'bg', frames, m => (m[0xF125C] && m[0xF0E00]) ? messageCalls(msg, m.readInt16LE(0xF10F0) === 0) : []);
    const withText = rows.filter(r => r.calls); const exact = rows.filter(r => r.exactPixels === 76800);
    results.messages.push({ id: msg.idHex, run, framesCompared: rows.length, framesExact: exact.length, framesWithText: withText.length,
      framesWithPrompt: rows.filter(r => r.calls.includes('prompt')).length, firstText: withText[0]?.frame, firstPrompt: rows.find(r => r.calls.includes('prompt'))?.frame,
      worst: rows.reduce((a, b) => (!a || b.exactPixels < a.exactPixels ? b : a), null), sheet: sheet(`verify/${run}.png`, evidence) });
  }
  const statsRun = option('--stats-run', 'endtext');
  if (fs.existsSync(path.join(verifyDir, statsRun))) {
    const frames = []; for (let f = 9742; f <= 9990; f++) frames.push(f);
    // state 13: the pass that runs func_8006C2E4 ends with func_8006CCF4 (step 0, delay 10) -> title only
    const model = m => { const s = readStats(m); return s.state === 14 ? statsCalls(s) : s.state === 13 ? statsCalls({ ...s, step: 0, timer: 10, done: 0 }) : []; };
    const clockScan = [1, 2, 3].flatMap(lag => [-1, 0, 1].map(o => ({ ramLag: lag, clockOffset: o, framesExact: compare(statsRun, 'endbg', frames, model, o, lag).rows.filter(r => r.exactPixels === 76800).length })));
    const best = clockScan.reduce((a, b) => b.framesExact > a.framesExact ? b : a);
    const { rows, evidence } = compare(statsRun, 'endbg', frames, model, best.clockOffset, best.ramLag);
    // pass-aligned rule (needs a RAM dump every frame): screenshot F shows main-loop pass p = pass#(F) - 1 (D_800ECEA0);
    // the pass draws with the state of the last dump of pass p and the clock of its first dump
    const passRule = { framesCompared: 0, framesExact: 0, bad: [] }; let passEvidence = null;
    const passOf = f => fs.existsSync(ramf(statsRun, f)) ? fs.readFileSync(ramf(statsRun, f)).readUInt32LE(0xECEA0) : null;
    for (const f of frames) { const pf = passOf(f); if (pf === null) continue; const p = pf - 1; let s0 = f, e0 = null;
      while (passOf(s0 - 1) !== null && passOf(s0 - 1) >= p) { s0--; if (passOf(s0) === p && e0 === null) e0 = s0; }
      if (e0 === null || passOf(s0 - 1) === null) continue; const S = fs.readFileSync(ramf(statsRun, s0)), E = fs.readFileSync(ramf(statsRun, e0));
      const A = readBmp(shot(statsRun, f)), fb = Uint16Array.from(readBmp(shot('endbg', f)));
      for (const c of model(E)) drawCall(fb, c, S.readUInt32LE(0xF16C0));
      let same = 0; for (let i = 0; i < 76800; i++) if (fb[i] === A[i]) same++;
      passRule.framesCompared++; if (same === 76800) passRule.framesExact++; else if (passRule.bad.length < 8) passRule.bad.push([f, same]);
      if (!passEvidence || passEvidence.same === 76800) passEvidence = { f, A, fb, same }; }
    passRule.sheet = sheet('verify/stats_pass_rule.png', passEvidence); passRule.sheetFrame = passEvidence?.f;
    results.stats = { passRule, glowClock: { rule: 'state and pulse clock = RAM dump ramLag frames before the screenshot, clock + clockOffset', scan: clockScan, used: best }, framesCompared: rows.length, framesExact: rows.filter(r => r.exactPixels === 76800).length, framesInState14: rows.filter(r => r.calls).length,
      byCalls: [...new Set(rows.map(r => r.calls))].map(k => { const rs = rows.filter(r => r.calls === k); return { calls: k, frames: `${rs[0].frame}..${rs[rs.length - 1].frame}`, n: rs.length, exact: rs.filter(r => r.exactPixels === 76800).length, worst: Math.min(...rs.map(r => r.exactPixels)) }; }),
      bad: rows.filter(r => r.exactPixels < 76800).slice(0, 5) };
  }
  json.verification = results;
  console.log(JSON.stringify(results, (k, v) => k === 'bad' && Array.isArray(v) && !v.length ? undefined : v, 1));
}
fs.writeFileSync(path.join(out, 'message_window.json'), JSON.stringify(json, null, 1));
console.log(`wrote ${out}: ${labels.messages.length} message labels, ${labels.stats.length} stats labels, checks: ${badChecks.length ? badChecks.join('; ') : 'ok'}`);
