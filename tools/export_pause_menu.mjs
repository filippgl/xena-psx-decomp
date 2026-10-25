#!/usr/bin/env node
// Export the in-game PAUSE screen of Xena: Warrior Princess (SLUS-00977) for a Unity rebuild: exact-pixel
// PNGs (pre-rendered labels in the game font, shadow and glow masks, icons, options slider parts) and a JSON
// with layout, colours, blend modes, input rules, actions and sounds. Optional verification rebuilds a paused
// frame from a native screenshot pair and compares it pixel by pixel. Evidence: docs/xena-pause-menu.md.
//
// Evidence chain (MIPS addresses in the SLUS-00977 executable):
//   * main 0x8006628C..0x80066304: pressed mask D_800F16D0 == 0x2 (START) -> func_800672F8 (request, sets
//     D_800F1134) when not paused, func_800673E4 (resume) when paused. main 0x80065EF0 -> func_80067360:
//     D_800F0E00 = 1, func_80086A24 (mute the 24 SFX voices), cursor D_800F10D9..DB = 0, and the
//     subtractive full-screen fade func_800A7F88(cam, 2, 2, 22) (11 on level 8).
//   * while paused main 0x80066374..0x800663A8 calls func_80075C2C (HUD) and func_80066CDC (this screen),
//     or func_8006E508 when a message box is open (D_800F125C).
//   * func_80066CDC: font abr 1 (func_800BA514(1,1)), style 8 (drop shadow); icons by func_800B8D88; counts
//     "%dx" (D_800ECEC8); title D_800F0360 at (160,64) colour 255; two white lines with (1,1,1) shadows;
//     items D_800F0364[0..3] at (160, 90+20i) right-aligned colour 125, the cursor item with style 0xB (shadow +
//     red/green glow); sub dialogs D_800F0398 / D_800F03B8 drawn by func_80066BD4; options by func_8006DE28.
//   * glyph renderer func_800B9CA0: style 8 = copies at (+1,+1),(+2,+2) colour 1 abr 0; style bits 1/2/4 = four
//     1-pixel offset copies, abr 3, channel = clock&0x20 ? (clock&31)*7 : (32-(clock&31))*255>>5, others 15.
//
//   node tools/export_pause_menu.mjs --root DECOMP --out DIR [--verify-shots DIR --verify-ram DIR --pair LIST,DIALOG]
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i < 0 ? fallback : argv[i + 1]; };
const root = path.resolve(option('--root', '.'));
const out = path.resolve(option('--out', 'pause-menu-export'));
const here = path.dirname(fileURLToPath(import.meta.url));
const { png } = await import(pathToFileURL(path.join(here, 'lib', 'png.mjs')).href);
const { readStm } = await import(pathToFileURL(path.join(here, 'lib', 'xena_model.mjs')).href);
fs.mkdirSync(out, { recursive: true });
const hex = n => '0x' + (n >>> 0).toString(16).toUpperCase();

// ---------- executable ----------
const exe = fs.readFileSync(path.join(root, 'xena.exe'));
const base = exe.readUInt32LE(0x18);
const eo = a => a - base + 0x800;
const u32 = a => exe.readUInt32LE(eo(a));
const cbytes = a => { const o = eo(a); return exe.subarray(o, exe.indexOf(0, o)); };
const imm = a => (u32(a) << 16) >> 16;
// Glyph letters of the Russian build (docs/xena-level-scripts.md, A..Z read from the font; symbols INFERRED)
const GLYPH = { A: 'А', B: 'В', C: 'С', D: 'Д', E: 'Е', F: 'Ы', G: 'Ш', H: 'Н', I: 'І', J: 'П', K: 'К', L: 'Л', M: 'М', N: 'И', O: 'Ф',
  P: 'Р', Q: 'Э', R: 'Я', S: 'Ь', T: 'Т', U: 'Ю', V: 'Ц', W: 'Ж', X: 'Х', Y: 'У', Z: 'Г' };
const SYMBOL = { '$': 'о', '&': 'б', '+': 'л', '#': 'ч', '@': 'з', '0': 'О', '3': 'З', '4': 'Ч', '6': 'Б', '%': '%' };
const translit = b => [...b].map(c => { const ch = String.fromCharCode(c);
  if (c < 32) return c === 10 ? '\n' : `{${c}}`;
  if (SYMBOL[ch]) return SYMBOL[ch]; const up = ch.toUpperCase();
  return GLYPH[up] ? (ch === up ? GLYPH[up] : GLYPH[up].toLowerCase()) : ch; }).join('');
const str = ptrAddr => { const p = u32(ptrAddr), b = cbytes(p); return { pointer: hex(ptrAddr), address: hex(p), bytes: [...b], text: translit(b) }; };
const STR = {
  title: str(0x800F0360), items: [0, 1, 2, 3].map(i => str(0x800F0364 + 4 * i)),
  restartDialog: [0, 1, 2, 3].map(i => str(0x800F0398 + 4 * i)), exitDialog: [0, 1, 2, 3].map(i => str(0x800F03B8 + 4 * i)),
  countFormat: { address: '0x800ECEC8', bytes: [...cbytes(0x800ECEC8)], text: cbytes(0x800ECEC8).toString('latin1') },
  noController: str(0x800F0354),
};

// ---------- disc ----------
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'build/asset-audit/manifest.json')));
const uda = fs.readFileSync(path.join(root, 'disc/files/XENA.UDA'));
function resource(name) {
  const hits = manifest.entries.filter(e => e.candidates.some(c => c.name.toUpperCase() === name.toUpperCase()));
  if (hits.length !== 1 || !hits[0].inArchive) throw Error(`Missing or ambiguous ${name}`);
  const at = (hits[0].lba - manifest.summary.archiveLba) * 2048;
  return { name, lba: hits[0].lba, bytes: uda.subarray(at, at + hits[0].size) };
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
  for (let i = 0; i < w * h; i++) rgba.set([...rgb8(words[i]), words[i] === 0 ? 0 : 255], i * 4);
  return { w, h, words, rgba };
}
function writePng(rel, d) { fs.mkdirSync(path.dirname(path.join(out, rel)), { recursive: true }); fs.writeFileSync(path.join(out, rel), png(d.w, d.h, d.rgba)); return rel; }

// ---------- icons (opaque raw sprites, func_800B8D88(desc, cx, cy, 0x7F,0x7F,0x7F, abr 0, semi 0)) ----------
const shield = decode(readTim(resource('DATA\\SCREENS\\SHLD_HUD.TIM').bytes));
const swordStm = readStm(resource('DATA\\LEVELS\\UP_SWORD.STM').bytes), scrollStm = readStm(resource('DATA\\LEVELS\\SCROLL.STM').bytes);
const swordIcon = decode(swordStm.images[0]), scrollIcon = decode(scrollStm.images[0]);
// immediates of func_80066CDC / func_80066BD4 used below (fail if the executable differs)
const CHECKS = [[0x80066DCC, 0xE0], [0x80066DD0, 0x42], [0x80066DF0, 0xD8], [0x80066DF8, 0x3B], [0x80066EF4, 0xA0], [0x80066EFC, 0x40],
  [0x80067030, 0x7D], [0x800671A4, 0x5A]];
for (const [a, v] of CHECKS) if (imm(a) !== v) throw Error(`immediate at ${hex(a)} is ${imm(a)}, expected ${v}`);
const icons = [
  { what: 'armour level (up_armor pickups)', value: 'D_800F2785', source: 'DATA\\SCREENS\\SHLD_HUD.TIM (descriptor D_800F1440, loaded by func_8007515C)', d: shield, centre: [imm(0x80066DCC), imm(0x80066DD0)], countY: imm(0x80066DF8) },
  { what: 'sword level (up_sword pickups)', value: 'D_800F2786', source: `DATA\\LEVELS\\UP_SWORD.STM tile 0 "${swordStm.images[0].name.replace(/[^ -~]/g, "")}" (D_800F2624 = preloaded STM #8 of D_800C1D1C)`, d: swordIcon, centre: [0xE0, 0x6E], countY: 0x67 },
  { what: 'scrolls carried', value: 'D_800F277A', source: `DATA\\LEVELS\\SCROLL.STM tile 0 "${scrollStm.images[0].name.replace(/[^ -~]/g, "")}" (D_800F262C = preloaded STM #9)`, d: scrollIcon, centre: [0xE7, 0x9B], countY: 0x94 },
].map((ic, i) => ({ ...ic, png: writePng(`icons/${['shield', 'sword', 'scroll'][i]}.png`, ic.d), size: [ic.d.w, ic.d.h],
  topLeft: [ic.centre[0] - Math.trunc(ic.d.w / 2), ic.centre[1] - Math.trunc(ic.d.h / 2)] }));
// options slider parts (func_8006D7F0): hilt.tim at (95,y), sword.tim cropped = level, blood.tim at (160,y)
const sliderParts = ['HILT', 'SWORD', 'BLOOD'].map(n => { const d = decode(readTim(resource(`DATA\\SCREENS\\${n}.TIM`).bytes));
  return { name: n.toLowerCase(), source: `DATA\\SCREENS\\${n}.TIM`, size: [d.w, d.h], png: writePng(`options/${n.toLowerCase()}.png`, d) }; });

// ---------- font (as docs/xena-main-menu.md) ----------
const fontTim = readTim(resource('DATA\\SCREENS\\XENAFONT.TIM').bytes), fontRaw = decode(fontTim);
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
// one text call (func_800B9300): returns glyphs with absolute top-left positions (single-line strings; '\n' ends the line)
function layout(bytes, x, y, align) {
  const glyphs = []; let pen = 0, extent = 0;
  for (const c of bytes) { if (c === 10) break; const g = glyphOf(c); if (g < 0) continue;
    const isDigit = g >= glyphOf(0x30) && g <= glyphOf(0x39), adv = isDigit ? 10 : glyphWidth[g] + 3;
    // func_800BA184 0x800BA294..0x800BA2D4: digit glyphs are drawn 9 px left of the pen ('2' and '4': 8 px)
    const dx = isDigit ? (g === glyphOf(0x32) || g === glyphOf(0x34) ? -8 : -9) : 0;
    glyphs.push({ c, g, x: pen + dx, y: glyphYOffset(g), u: (g % cellsX) * 16, v: 32 + Math.floor(g / cellsX) * 16 }); extent = Math.max(extent, pen + adv); pen += adv; }
  const x0 = align === 1 ? x - extent : align === 2 ? x - (extent >> 1) : x;
  return { extent, glyphs: glyphs.map(q => ({ ...q, x: q.x + x0, y: q.y + y })) };
}
const mod = (t5, c) => Math.min(31, (t5 * c) >> 7);
const pulse = clock => (clock & 0x20) ? (clock & 31) * 7 : ((32 - (clock & 31)) * 255) >> 5;
// Draws one text call into a 15-bit framebuffer in the glyph order of func_800B9CA0.
function drawText(fb, t, clock) {
  const { glyphs } = layout(t.bytes, t.x, t.y, t.align), rgb = t.rgb, style = t.style;
  const each = (q, dx, dy, fn) => { for (let yy = 0; yy < 16; yy++) for (let xx = 0; xx < 16; xx++) {
    const c = fontWords[(q.v + yy) * FW + q.u + xx]; if (!c) continue; const X = q.x + xx + dx, Y = q.y + yy + dy;
    if (X < 0 || X >= 320 || Y < 0 || Y >= 240) continue; const i = Y * 320 + X; fb[i] = fn(fb[i], c); } };
  const ch = (w, n) => (w >>> n) & 31, pack = (r, g, b) => r | (g << 5) | (b << 10);
  for (const q of glyphs) {
    if (style & 8) for (const d of [1, 2]) each(q, d, d, (B, T) => pack(...[0, 5, 10].map(n => (ch(B, n) + mod(ch(T, n), 1)) >> 1))); // abr 0
    each(q, 0, 0, (B, T) => pack(...[0, 5, 10].map((n, k) => Math.min(31, ch(B, n) + mod(ch(T, n), rgb[k]))))); // abr 1
    if (style & 7) { const p = pulse(clock), col = [style & 1 ? p : 15, style & 2 ? p : 15, style & 4 ? p : 15];
      for (const [dx, dy] of [[1, 0], [-1, 0], [0, -1], [0, 1]]) each(q, dx, dy, (B, T) => pack(...[0, 5, 10].map((n, k) => Math.min(31, ch(B, n) + (mod(ch(T, n), col[k]) >> 2))))); } // abr 3
  }
}
// footprint (pixels any part of a text call can touch)
function footprint(t, set) { const { glyphs } = layout(t.bytes, t.x, t.y, t.align), r = (t.style & 8) ? 2 : (t.style & 7) ? 1 : 0;
  for (const q of glyphs) for (let yy = -1; yy < 16 + r; yy++) for (let xx = -1; xx < 16 + r; xx++) { const X = q.x + xx, Y = q.y + yy; if (X >= 0 && X < 320 && Y >= 0 && Y < 240) set.add(Y * 320 + X); } }

// ---------- screen model ----------
function screen(cursor, dialog, dialogCursor) { // text calls in creation order
  const calls = [];
  for (const ic of icons) calls.push({ kind: 'count', bytes: [...Buffer.from('0x')], x: 0xD8, y: ic.countY, align: 1, rgb: [0x6E, 0x6E, 0x6E], style: 8 });
  calls.push({ kind: 'title', bytes: STR.title.bytes, x: 0xA0, y: 0x40, align: 1, rgb: [0xFF, 0xFF, 0xFF], style: 8 });
  const list = dialog === 'restart' ? STR.restartDialog : dialog === 'exit' ? STR.exitDialog : STR.items, cur = dialog ? dialogCursor : cursor;
  list.forEach((s, i) => calls.push({ kind: dialog ? 'dialog' : 'item', index: i, bytes: s.bytes, x: 0xA0, y: 0x5A + 0x14 * i, align: 1, rgb: [0x7D, 0x7D, 0x7D], style: i === cur ? 0xB : 8 }));
  return calls;
}

// ---------- exported label images ----------
// additive layer (texel * colour >> 7, add to the screen), shadow mask (halve the screen), glow mask (outline)
function labelImages(name, bytes, rgb) {
  const { glyphs, extent } = layout(bytes, 0, 0, 0); if (!glyphs.length) return null;
  const minY = Math.min(...glyphs.map(q => q.y)) - 1, minX = Math.min(0, ...glyphs.map(q => q.x)) - 1;
  const w = Math.max(extent, ...glyphs.map(q => q.x + 16)) - minX + 3, h = 16 + 3 + (Math.max(...glyphs.map(q => q.y)) - minY);
  const add = new Uint8Array(w * h * 4), shadow = new Uint8Array(w * h * 4), glow = new Uint8Array(w * h * 4);
  for (const q of glyphs) for (let yy = 0; yy < 16; yy++) for (let xx = 0; xx < 16; xx++) {
    const c = fontWords[(q.v + yy) * FW + q.u + xx]; if (!c) continue;
    const put = (img, dx, dy, px) => { const X = q.x + xx + dx - minX, Y = q.y + yy + dy - minY; if (X < 0 || X >= w || Y < 0 || Y >= h) return; img.set(px, (Y * w + X) * 4); };
    const a = [0, 5, 10].map((n, k) => { const v = mod((c >>> n) & 31, rgb[k]); return (v << 3) | (v >> 2); });
    put(add, 0, 0, [...a, 255]);
    for (const d of [1, 2]) put(shadow, d, d, [0, 0, 0, 128]);
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, -1], [0, 1]]) put(glow, dx, dy, [255, 255, 255, 255]);
  }
  return { name, size: [w, h], originOffset: [minX, minY], note: 'image pixel (0,0) = pen origin + originOffset (left-aligned text at x = 0; for right alignment the pen origin is anchor.x - extent)', extent, add: writePng(`labels/${name}.png`, { w, h, rgba: add }),
    shadow: writePng(`labels/${name}_shadow.png`, { w, h, rgba: shadow }), glow: writePng(`labels/${name}_glow.png`, { w, h, rgba: glow }) };
}
const labels = [
  labelImages('title', STR.title.bytes, [0xFF, 0xFF, 0xFF]),
  ...STR.items.map((s, i) => labelImages(`item${i}`, s.bytes, [0x7D, 0x7D, 0x7D])),
  ...STR.restartDialog.map((s, i) => labelImages(`restart${i}`, s.bytes, [0x7D, 0x7D, 0x7D])),
  labelImages('exit0', STR.exitDialog[0].bytes, [0x7D, 0x7D, 0x7D]),
  labelImages('count0x', [...Buffer.from('0x')], [0x6E, 0x6E, 0x6E]),
].filter(Boolean);

// ---------- options strings (shared with the main menu, func_8006E1EC / func_8006DE28) ----------
const opt = {
  list: [['title', 0x800EFFB4], ['prompt', 0x800EFFC0], ['music', 0x800EFFC4], ['effects', 0x800EFFC8], ['audio (runtime: D_800CCED8[mode])', 0x800EFFCC],
    ['vibration (runtime: D_800CCEE0[D_800F2771])', 0x800EFFD0], ['config (letter patched at +0x10 = A + D_800F1234)', 0x800EFFD4], ['movement (runtime: D_800CCED0[D_800F278C])', 0x800EFFD8]]
    .map(([k, a]) => ({ key: k, ...str(a) })),
  movement: [0, 1].map(i => str(0x800CCED0 + 4 * i)), audio: [0, 1].map(i => str(0x800CCED8 + 4 * i)), vibration: [0, 1].map(i => str(0x800CCEE0 + 4 * i)),
  sliderOff: str(0x800EFF88), sliderFormat: { address: '0x800ED704', text: cbytes(0x800ED704).toString('latin1') },
};

const json = {
  source: 'Xena: Warrior Princess (SLUS-00977) in-game pause screen; see docs/xena-pause-menu.md',
  screen: { width: 320, height: 240, origin: 'top-left, PS1 framebuffer pixels' },
  open: { button: 'START (pressed mask D_800F16D0 == 0x2 exactly, main 0x80066294)', conditions: 'game state 12, D_800F0E02 == 0, camera mode (D_800F10F4->+0x114->+4) != 6; request flag D_800F1134 is applied by main 0x80065EF0 (func_80067360) on the next pass',
    onOpen: 'D_800F0E00 = 1; func_80086A24: SsUtSetVVol(v, 0, 0) for the 24 SFX voices (+0x1C = 1) and func_80086888/func_80087868 (music pause, INFERRED); cursor D_800F10D9 = 0, dialogs D_800F10DA = D_800F10DB = 0, options flag D_800ECEA4 = 0; fade func_800A7F88(cam, 2, 2, 22) (level 8: 11) unless one is running',
    close: 'func_800673E4: D_800F0E00 = 0, D_800F125C = 0, func_800B8D34, func_80086AAC (restore voice volumes, resume music), fade back func_800A7F88(cam, 2, 1, 22|11) over 2 passes',
    gameplay: 'paused: game step D_800ECEA0 and func_80076888 do not run (counter frozen in the capture); the scene is still rendered every frame and the HUD update func_80075C2C runs' },
  background: { treatment: 'frozen scene + full-screen subtractive quad: main 0x80066B64 calls func_800A7F88(cam, 0, 3, 0) every frame while D_800F078E is set: a 320x240 quad (func_800B73F0, semi, abr 2 = B - F) of grey (22*t/2 for t = 0, 1, then 22 = 0x16) (level 8: 0x0B)',
    measured: 'native software renderer: 5-bit channel values drop by 0..2 (dither pattern), about -2 on average from the 3rd frame after START; hardware renderer at 2x: -3 (8-bit ~ -22). No blur, no colour shift in the code.' },
  drawOrder: ['frozen scene (re-rendered)', 'subtractive fade quad', 'icons (DrawPrim immediately)', 'title underline and separator lines (DrawPrim immediately)', 'text slots: counts, title, items/dialog (drawn by the font system after the screen code)'],
  font: { source: 'DATA\\SCREENS\\XENAFONT.TIM (same as docs/xena-main-menu.md)', blend: 'SPRT16 modulated by the call colour (c5 = min(31, t5*c >> 7)), semi-transparent abr 1 (B+F), every visible texel has STP',
    styles: { 8: 'drop shadow: the glyph drawn first at (+1,+1) and (+2,+2), colour (1,1,1) => F = 0, abr 0 => B/2 (half-dark shadow)',
      '1|2|4': 'glow on R|G|B: after the glyph, 4 copies at (+1,0) (-1,0) (0,-1) (0,+1), abr 3 (B + F/4), colour channel = clock&0x20 ? (clock&31)*7 : (32-(clock&31))*255>>5 (D_800F16C0 vblank clock, period 64), other channels 15',
      0x40: 'extra outline copies (not used here)' },
    selectedStyle: '0xB = shadow + red+green glow (pulsing yellow outline), unselected 8 = shadow only' },
  layout: {
    title: { text: STR.title.text, bytes: STR.title.bytes, anchor: [0xA0, 0x40], align: 'right', colour: [255, 255, 255], style: 8, underline: { from: [0x69, 0x4F], to: [0x9D, 0x4F], colour: [255, 255, 255], shadow: { from: [0x6B, 0x51], to: [0x9F, 0x51], colour: [1, 1, 1] } } },
    separator: { from: [0xB4, 0x31], to: [0xB4, 0xB3], colour: [255, 255, 255], shadow: { from: [0xB6, 0x33], to: [0xB6, 0xB5], colour: [1, 1, 1] }, primitive: 'LINE_F2 (SetLineF2), opaque' },
    items: STR.items.map((s, i) => ({ index: i, text: s.text, bytes: s.bytes, anchor: [0xA0, 0x5A + 0x14 * i], align: 'right', colour: [0x7D, 0x7D, 0x7D] })),
    rightColumn: icons.map(({ d, ...ic }) => ({ ...ic, count: { format: '%dx', anchor: [0xD8, ic.countY], align: 'right', colour: [0x6E, 0x6E, 0x6E], style: 8 }, blend: 'opaque raw SPRT (colour 0x7F, abr 0, no semi)' })),
    inventoryCounts: { what: 'the 7 HUD inventory items D_800F277E..D_800F2784 (potions etc.)', anchor: 'x 0x124 = 292 right-aligned, y 27 + 20*i', shown: 'only when the count is non-zero (none in the capture)', colour: [0x6E, 0x6E, 0x6E] },
    noController: { text: STR.noController.text, note: 'D_800F0354, printed by main when the pad is missing (not part of this screen)' },
  },
  input: {
    mask: 'D_800F16D0 = newly pressed buttons in the game layout; with the default controls the port script names map to: START 0x2, UP 0x4, DOWN 0x8, LEFT 0x10, RIGHT 0x20, TRIANGLE 0x400, CROSS 0x800 (VERIFIED in the port; CIRCLE does nothing here)',
    main: { up: 'cursor-- if > 0 (no wrap), sound 0x39', down: 'cursor++ if < 3, sound 0x39', confirm: 'CROSS: action by cursor (no sound)', back: 'TRIANGLE: resume', start: 'START: resume (main loop toggle)' },
    dialog: { lines: '4 lines at the item positions; 0 = repeated item name, 1 = "Ты уверен?", 2 = "Да", 3 = "Не"', cursor: 'starts on 3 ("Не"); UP from 3 -> 2, DOWN from 2 -> 3 (sound 0x39)', confirm: 'CROSS on 2 = yes, on 3 = close the dialog', back: 'TRIANGLE closes the dialog' },
    cursorMemory: 'cursor D_800F10D9 is reset to 0 each time the pause opens (func_80067360)',
  },
  actions: [
    { cursor: 0, item: STR.items[0].text, does: 'func_800673E4: resume' },
    { cursor: 1, item: STR.items[1].text, does: 'confirm dialog D_800F0398 (D_800F10DA = 3); "Да" -> func_800673E4, func_8006C264 (D_800EE200 = current level D_800F2772, func_8006C04C(1): reload the level from its start), func_800659EC' },
    { cursor: 2, item: STR.items[2].text, does: 'D_800ECEA4 = 1, func_8006E1EC (options init, same screen as the main menu Options); each frame func_8006DE28 until it returns 1' },
    { cursor: 3, item: STR.items[3].text, does: 'confirm dialog D_800F03B8 (D_800F10DB = 3); "Да" -> func_8006C2A4: game state 19 (GAME OVER sequence, then main menu, VERIFIED capture)' },
  ],
  sounds: { move: 'func_80084A34(0x39) on every cursor move (list, dialogs, options toggles)', confirm: 'none in func_80066CDC', pause: 'SFX voices muted while paused' },
  strings: STR,
  options: {
    init: 'func_8006E1EC: backups D_800F1234 = controller config D_800F278B, D_800F1254 = vibration D_800F2771, D_800F11FE = movement D_800F278C; input delay 10; cursor restored from D_800ED530',
    items: [
      { index: 0, label: 'Музыка', kind: 'slider', y: 0x4A, value: 'func_800870A8 / func_800870B4 (music volume 0..127)' },
      { index: 1, label: 'Эффекты', kind: 'slider', y: 0x6B, value: 'func_800871AC / func_800871B8 (effects volume); a test sound every 32 vblanks while selected' },
      { index: 2, label: 'Аудио', kind: 'toggle', values: opt.audio.map(s => s.text), rule: 'held LEFT -> mono, held RIGHT -> stereo (func_8008701C), test sounds func_800847C0(0x26, ...) and (0x1011, ...)' },
      { index: 3, label: 'Вибрация', kind: 'toggle', values: opt.vibration.map(s => s.text), rule: 'LEFT/RIGHT toggles D_800F2771, sound 0x39, rumble test func_800AA354' },
      { index: 4, label: 'Конфигурация', kind: 'choice A..D', rule: 'LEFT/RIGHT D_800F1234 0..3, sound 0x39; while selected the screen shows the button layout of the configuration (func_8006DB5C)' },
      { index: 5, label: 'Движения', kind: 'toggle', values: opt.movement.map(s => s.text), rule: 'LEFT/RIGHT toggles D_800F278C, sound 0x39' },
    ],
    slider: { range: 'value v 0..127; held LEFT v -= 2, below 28 -> 0; held RIGHT v += 2, minimum 28', label: `v < 28: "${opt.sliderOff.text}" else "${opt.sliderFormat.text}" of v - 27 (1..100)`,
      parts: sliderParts, draw: 'blood.tim centred at (160, y); sword.tim with width cropped to w-127+v (w-100 when off), left edge fixed; hilt.tim centred at (95, y); label at (160, y-8) centred',
      colours: { selected: [0xAF, 0x32, 0xC8], unselected: [0xAF, 0xC8, 0x32] } },
    exit: 'CROSS or TRIANGLE returns 1 (close options); TRIANGLE first restores the backups (func_8006DCA8) = cancel',
    strings: opt,
  },
  labels,
};

// ---------- verification: rebuild a paused frame from another paused frame ----------
const shotsDir = option('--verify-shots'), ramDir = option('--verify-ram'), pair = option('--pair');
if (shotsDir && ramDir && pair) {
  const [listFrame, dialogFrame] = pair.split(',').map(Number);
  const readBmp = f => { const b = fs.readFileSync(f), at = b.readUInt32LE(10), w = b.readInt32LE(18), h0 = b.readInt32LE(22), h = Math.abs(h0), stride = Math.ceil(w * 3 / 4) * 4, px = new Uint16Array(w * h);
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) { const o = at + (h0 > 0 ? h - 1 - y : y) * stride + x * 3; px[y * w + x] = (b[o + 2] >> 3) | ((b[o + 1] >> 3) << 5) | ((b[o] >> 3) << 10); }
    return { w, h, px }; };
  const ram = f => fs.readFileSync(path.join(ramDir, `ram_${String(f).padStart(6, '0')}.bin`));
  const state = f => { const m = ram(f); return { clock: m.readUInt32LE(0xF16C0), cursor: m[0xF10D9], da: m[0xF10DA], db: m[0xF10DB] }; };
  const results = [];
  for (const off of [0, 1, 2, 3]) {
    const A = readBmp(path.join(shotsDir, `frame_${String(listFrame).padStart(6, '0')}.bmp`)), B = readBmp(path.join(shotsDir, `frame_${String(dialogFrame).padStart(6, '0')}.bmp`));
    const has = f => fs.existsSync(path.join(ramDir, `ram_${String(f).padStart(6, '0')}.bin`));
    if (!has(listFrame - off) || !has(dialogFrame - off)) { results.push({ offset: off, skipped: 'no RAM dump' }); continue; }
    const sa = state(listFrame - off), sb = state(dialogFrame - off);
    if (sa.da || sa.db || !(sb.da || sb.db)) { results.push({ offset: off, skipped: 'state mismatch', sa, sb }); continue; }
    const callsA = screen(sa.cursor, null), callsB = screen(0, sb.da ? 'restart' : 'exit', sb.da || sb.db);
    const varA = new Set(), varB = new Set();
    callsA.filter(c => c.kind === 'item').forEach(c => footprint(c, varA)); callsB.filter(c => c.kind === 'dialog').forEach(c => footprint(c, varB));
    // background = B where B has no dialog text; elsewhere unknown
    const fb = Uint16Array.from(B.px);
    for (const c of callsA.filter(c => c.kind === 'item')) drawText(fb, c, sa.clock);
    let exact = 0, total = 0; const bad = [];
    for (const i of varA) { if (varB.has(i)) continue; total++; if (fb[i] === A.px[i]) exact++; else if (bad.length < 8) bad.push({ x: i % 320, y: Math.floor(i / 320), model: fb[i].toString(16), shot: A.px[i].toString(16) }); }
    // outside both footprints the frames must be identical (same frozen scene, fade, icons, title, counts)
    let same = 0, outside = 0; for (let i = 0; i < 320 * 240; i++) if (!varA.has(i) && !varB.has(i)) { outside++; if (A.px[i] === B.px[i]) same++; }
    // per text call, both directions: exact pixels inside its own footprint when shifted by dx (alignment check)
    const check = (calls, other, bg, shot, clock) => calls.map(c => { const own = new Set(); footprint(c, own);
      const badAt = [];
      const score = dx => { const f2 = Uint16Array.from(bg); for (const d of calls) drawText(f2, d === c ? { ...c, x: c.x + dx } : d, clock); let e = 0, n = 0;
        for (const i of own) { if (other.has(i)) continue; n++; if (f2[i] === shot[i]) e++; else if (!dx && badAt.length < 6) badAt.push([i % 320, Math.floor(i / 320), f2[i].toString(16), shot[i].toString(16)]); } return [e, n]; };
      const shifts = [-6, -5, -4, -3, -2, -1, 0, 1, 2].map(dx => ({ dx, exact: score(dx)[0] }));
      return { kind: c.kind, index: c.index, style: c.style, pixels: score(0)[1], exactAtModel: score(0)[0], best: shifts.reduce((a, b) => b.exact > a.exact ? b : a), badAt: badAt.slice(0, 6) }; });
    const perCall = [...check(callsA.filter(c => c.kind === 'item'), varB, B.px, A.px, sa.clock), ...check(callsB.filter(c => c.kind === 'dialog'), varA, A.px, B.px, sb.clock)];
    results.push({ offset: off, clock: sa.clock, cursor: sa.cursor, modelPixels: total, exact, outsideFootprints: outside, identicalOutside: same, perCall, bad });
    if (off === 2) { // side-by-side evidence image: screenshot | model | difference (red)
      const rgba = new Uint8Array(960 * 240 * 4);
      for (let y = 0; y < 240; y++) for (let x = 0; x < 320; x++) { const i = y * 320 + x;
        rgba.set([...rgb8(A.px[i]), 255], (y * 960 + x) * 4); rgba.set([...rgb8(fb[i]), 255], (y * 960 + 320 + x) * 4);
        rgba.set(varA.has(i) && !varB.has(i) ? (fb[i] === A.px[i] ? [0, 90, 0, 255] : [255, 0, 0, 255]) : [0, 0, 0, 255], (y * 960 + 640 + x) * 4); }
      writePng(`verify/pair_${listFrame}_${dialogFrame}.png`, { w: 960, h: 240, rgba }); }
  }
  const boxes = [...screen(0, null), ...screen(0, 'restart', 3)].map(c => { const l = layout(c.bytes, c.x, c.y, c.align);
    const ink = l.glyphs.filter(q => q.c !== 0x20); return { kind: c.kind, index: c.index, text: translit(Buffer.from(c.bytes)).trim(), extent: l.extent,
      inkLeft: ink.length ? ink[0].x : null, inkRight: ink.length ? Math.max(...ink.map(q => q.x + glyphWidth[q.g])) : null }; });
  json.verification = { listFrame, dialogFrame, results, boxes };
  console.log(JSON.stringify(json.verification, null, 1));
}
fs.writeFileSync(path.join(out, 'pause_menu.json'), JSON.stringify(json, null, 1));
console.log(`wrote ${out}: ${labels.length} labels, ${icons.length} icons, ${sliderParts.length} slider parts`);
