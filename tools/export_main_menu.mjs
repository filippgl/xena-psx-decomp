#!/usr/bin/env node
// Export the original MAIN MENU screen of Xena: Warrior Princess (SLUS-00977) for a Unity rebuild:
// exact-pixel PNGs (background, highlights, hair/lightning tiles, font + pre-rendered prompt) and a JSON
// that describes the per-tick animation state machine. Optional --verify composites frames from native
// RAM dumps and compares them with native screenshots. See docs/xena-main-menu.md for the evidence.
//
// Evidence chain (MIPS addresses in the SLUS-00977 executable):
//   * func_8006AA04 (menu init): main.tim via func_8006A8E4 (kept in RAM, re-uploaded every tick by
//     func_8006A924 with LoadImage into the draw buffer); allocs a 0x88-byte context D_800F1238;
//     4 x data\screens\menu\%s.tim (names D_800ED538) -> descriptors ctx+0,+0x18,+0x30,+0x48;
//     hair.stm -> ctx+0x60 (count ctx+0x64), ltng.stm -> ctx+0x70 (count ctx+0x74) via func_800A7528;
//     hair channels ctx+0x65+3*i = {seq=i+2, frame=D_800C2880[seq]}; lightning ctx+0x76+4*i =
//     {s16 timer=rand(30,200), s8 state=2, u8 index=i}; idle timer D_800F10E0=900; input delay D_800F10F0=5.
//   * func_8006AC88 (menu tick): cheat-code ring check; background; idle reset; up/down; lightning;
//     hair; highlight; confirm; prompt text (func_800BA598(300,202), right-aligned, D_800CCEA8[0]).
//   * func_800B8D88 -> func_800A5634: DR_MODE + SPRT (0x64), top-left = centre - (w/2,h/2), raw texture
//     (r=g=b=0x7F, abr 0, not semi-transparent), DrawPrim immediately (painter's order = call order).
//   * func_800B42D8: rand = 16 steps of a 32-bit LFSR (bit = s>>30 & 1, xor 1 if s&4; s = s<<1|bit),
//     returns low 16 bits (signed); func_800B4350(lo,hi) = lo + (((hi-lo+1) * (u16)rand) >> 16).
//
//   node tools/export_main_menu.mjs --root DECOMP --out DIR [--verify-shots DIR --verify-ram DIR]
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i < 0 ? fallback : argv[i + 1]; };
const root = path.resolve(option('--root', '.'));
const out = path.resolve(option('--out', 'main-menu-export'));
const { png } = await import(pathToFileURL(path.join(root, 'tools', 'lib', 'png.mjs')).href);
const { readStm } = await import(pathToFileURL(path.join(root, 'tools', 'lib', 'xena_model.mjs')).href);
fs.mkdirSync(out, { recursive: true });

// ---------- executable tables ----------
const exe = fs.readFileSync(path.join(root, 'xena.exe'));
const base = exe.readUInt32LE(0x18);
const eo = a => a - base + 0x800;
const cbytes = a => { const o = eo(a); let e = o; while (exe[e]) e++; return exe.subarray(o, e); };
const s16 = a => exe.readInt16LE(eo(a)), u8 = a => exe[eo(a)], u32 = a => exe.readUInt32LE(eo(a));
const HIGHLIGHT_NAMES = [0, 1, 2, 3].map(i => cbytes(u32(0x800ED538 + 4 * i)).toString('latin1'));
const HIGHLIGHT_CENTRE = [0, 1, 2, 3].map(i => [u8(0x800ED568 + i), u8(0x800ED56C + i)]);
const LIGHTNING_CENTRE = [0, 1, 2, 3].map(i => [s16(0x800ED570 + 2 * i), s16(0x800ED578 + 2 * i)]);
const HAIR_SEQ_CENTRE = [0, 1, 2, 3, 4].map(i => [s16(0x800C2868 + 2 * i), s16(0x800C2874 + 2 * i)]);
const HAIR_SEQ_BOUNDS = [0, 1, 2, 3, 4, 5].map(i => u8(0x800C2880 + i));
const CHEATS = [0, 1, 2, 3].map(i => Array.from({ length: 8 }, (_, k) => u32(0x800C27E8 + i * 0x20 + k * 4)));
const PROMPT = cbytes(u32(0x800CCEA8));
const PROMPT_POS = [300, 202]; // func_8006AC88 0x8006B1D0: func_800BA598(0x12C, 0xCA); func_800BA4E4(1) = right align

// ---------- disc archive ----------
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
  const flags = bytes.readUInt32LE(4), depth = flags & 7;
  let cursor = 8;
  const block = () => {
    const size = bytes.readUInt32LE(cursor), [x, y, width, height] = [4, 6, 8, 10].map(n => bytes.readUInt16LE(cursor + n));
    const data = bytes.subarray(cursor + 12, cursor + 12 + width * height * 2); cursor += size;
    return { x, y, width, height, data };
  };
  const clut = flags & 8 ? block() : null;
  return { depth, clut, image: block() };
}
// Decode to 15-bit PSX words (0x0000 = never drawn by a textured primitive) + RGBA (alpha 0 for 0x0000).
function decode(img) {
  const k = [4, 2, 1][img.depth], w = img.image.width * k, h = img.image.height;
  const words = new Uint16Array(w * h), rgba = new Uint8Array(w * h * 4);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const word = img.image.data.readUInt16LE((y * img.image.width + Math.floor(x / k)) * 2);
    let c = word;
    if (img.depth === 0) c = img.clut.data.readUInt16LE(((word >>> ((x & 3) * 4)) & 15) * 2);
    else if (img.depth === 1) c = img.clut.data.readUInt16LE(((word >>> ((x & 1) * 8)) & 255) * 2);
    words[y * w + x] = c;
  }
  for (let i = 0; i < w * h; i++) rgba.set([...rgb8(words[i]), words[i] === 0 ? 0 : 255], i * 4);
  return { w, h, words, rgba };
}
function rgb8(c) { return [0, 5, 10].map(n => (((c >>> n) & 31) << 3) | (((c >>> n) & 31) >>> 2)); }
function writePng(rel, d) {
  fs.mkdirSync(path.dirname(path.join(out, rel)), { recursive: true });
  fs.writeFileSync(path.join(out, rel), png(d.w, d.h, d.rgba));
  return rel;
}

// ---------- textures ----------
const bgRes = resource('DATA\\SCREENS\\MENU\\MAIN.TIM'), bgTim = readTim(bgRes.bytes);
if (bgTim.depth !== 2) throw Error('main.tim is expected to be 16-bit');
const bg = decode(bgTim);
// LoadImage copies all words, including 0x0000 (black): the background is fully opaque.
const bgOpaque = { ...bg, rgba: bg.rgba.map((v, i) => (i & 3) === 3 ? 255 : v) };
writePng('background.png', bgOpaque);

const highlights = HIGHLIGHT_NAMES.map((name, i) => {
  const res = resource(`DATA\\SCREENS\\MENU\\${name.toUpperCase()}.TIM`), d = decode(readTim(res.bytes));
  const [cx, cy] = HIGHLIGHT_CENTRE[i];
  return { index: i, name, source: res.name, lba: res.lba, png: writePng(`highlights/${name}.png`, d), size: [d.w, d.h],
    centre: [cx, cy], topLeft: [cx - Math.trunc(d.w / 2), cy - Math.trunc(d.h / 2)], d };
});
function stmTiles(file, dir) {
  const res = resource(file), stm = readStm(res.bytes);
  return { res, tiles: stm.images.map((img, i) => {
    const d = decode(img);
    return { index: i, name: img.name, depthBits: [4, 8, 16][img.depth], size: [d.w, d.h],
      vram: [img.image.x, img.image.y], clut: img.clut ? [img.clut.x, img.clut.y] : null,
      png: writePng(`${dir}/${String(i).padStart(3, '0')}_${img.name}.png`, d), d };
  }) };
}
const hair = stmTiles('DATA\\SCREENS\\MENU\\HAIR.STM', 'hair');
const ltng = stmTiles('DATA\\SCREENS\\MENU\\LTNG.STM', 'lightning');

// ---------- font + prompt (func_800B8F78, func_800B9168, func_800BA3E4, func_800B95BC, func_800BA184) ----------
const fontRes = resource('DATA\\SCREENS\\XENAFONT.TIM'), fontTim = readTim(fontRes.bytes);
if (fontTim.depth !== 0) throw Error('xenafont.tim is expected to be 4-bit');
const fontRaw = decode(fontTim);
// func_800B8F78 0x800B9024: CLUT entries whose colour bits are 0 become 0x0000 (transparent) before upload.
// The CLUT copy in RAM (D_800FF400, native dump at frame 7400) has bit 15 (STP) set on all 15 non-zero
// entries (file CLUT | 0x8000; setter not identified), so every visible glyph texel is semi-transparent.
const fontWords = fontRaw.words.map(c => (c & 0x7FFF) ? (c | 0x8000) : 0);
const font = { ...fontRaw, words: fontWords, rgba: new Uint8Array(fontRaw.w * fontRaw.h * 4) };
for (let i = 0; i < font.words.length; i++) font.rgba.set([...rgb8(font.words[i]), font.words[i] ? 255 : 0], i * 4);
writePng('font/xenafont.png', font);
const glyphOf = c => { // func_800BA3E4
  if (c >= 0x61 && c < 0x61 + 26) return c - 0x21;
  if ((c >= 0x41 && c < 0x41 + 30) || (c >= 0x20 && c < 0x20 + 33)) return c - 0x20;
  return { 1: 0x5D, 2: 0x5C, 4: 0x5B, 3: 0x5A }[c] ?? -1;
};
const cellsX = font.w >> 4, glyphCount = cellsX * ((font.h - 32) >> 4);
const glyphWidth = Array.from({ length: glyphCount }, (_, g) => { // func_800B9168: rightmost opaque column (CLUT colour bits)
  let wmax = 0; const gx = (g % cellsX) * 16, gy = 32 + Math.floor(g / cellsX) * 16;
  for (let y = 0; y < 16; y++) for (let x = 0; x < 16; x++) if ((fontRaw.words[(gy + y) * font.w + gx + x] & 0x7FFF) && x > wmax) wmax = x;
  return wmax;
});
glyphWidth[glyphOf(0x20)] = 2; glyphWidth[glyphOf(0x67)] -= 1;
for (let c = 0x30; c <= 0x39; c++) glyphWidth[glyphOf(c)] = 7;
const glyphYOffset = g => { // func_800B9A78
  if (g >= 0x21 && g < 0x21 + 26) return 0;
  if (g >= 0x40 && g < 0x40 + 26) return { [glyphOf(0x6A)]: 3, [glyphOf(0x67)]: 2, [glyphOf(0x70)]: 2, [glyphOf(0x71)]: 2, [glyphOf(0x79)]: 2 }[g] ?? -1;
  return g >= glyphOf(0x30) && g <= glyphOf(0x3F) ? 1 : 0;
};
const promptGlyphs = []; let pen = 0, extent = 0;
for (const c of PROMPT) {
  const g = glyphOf(c); if (g < 0) continue; // control characters (\n, \t, \r) are not used by this string
  const isDigit = g >= glyphOf(0x30) && g <= glyphOf(0x39);
  const advance = isDigit ? 10 : glyphWidth[g] + 3; // size 16 => no scaling
  promptGlyphs.push({ char: c, glyph: g, x: pen, y: glyphYOffset(g), u: (g % cellsX) * 16, v: 32 + Math.floor(g / cellsX) * 16 });
  extent = Math.max(extent, pen + advance); pen += advance;
}
const promptOrigin = [PROMPT_POS[0] - extent, PROMPT_POS[1]]; // func_800B9300 align 1: x -= max extent
const PROMPT_RGB = 0x7F; // func_800BA47C -> func_800BA4F4(0x7F,0x7F,0x7F); SPRT16 code 0x7C (texture modulated)
const modulate = (c, k) => c === 0 ? 0 : ((c & 0x8000) | [0, 5, 10].reduce((s, n) => s | ((((c >>> n) & 31) * k >> 7) << n), 0));
const promptBox = [Math.min(...promptGlyphs.map(p => p.y)), Math.max(...promptGlyphs.map(p => p.y)) + 16];
const prompt = { w: extent, h: promptBox[1] - promptBox[0], words: null, rgba: null };
prompt.words = new Uint16Array(prompt.w * prompt.h);
for (const p of promptGlyphs) for (let y = 0; y < 16; y++) for (let x = 0; x < 16; x++) {
  const c = font.words[(p.v + y) * font.w + p.u + x], tx = p.x + x, ty = p.y - promptBox[0] + y;
  if (c && tx < prompt.w) prompt.words[ty * prompt.w + tx] = modulate(c, PROMPT_RGB);
}
prompt.rgba = new Uint8Array(prompt.w * prompt.h * 4);
for (let i = 0; i < prompt.words.length; i++) prompt.rgba.set([...rgb8(prompt.words[i]), prompt.words[i] ? 255 : 0], i * 4);
writePng('font/prompt.png', prompt);

// ---------- deterministic simulation of one menu tick ----------
function rand(st) { let s = st.seed >>> 0; for (let i = 0; i < 16; i++) { let b = (s >>> 30) & 1; if (s & 4) b ^= 1; s = ((s << 1) | b) >>> 0; } st.seed = s; return (s << 16) >> 16; }
function randRange(st, lo, hi) { if (hi < lo) [lo, hi] = [hi, lo]; const r = rand(st) & 0xFFFF; return ((lo + (((hi - lo + 1) * r) >>> 16)) << 16) >> 16; }
function initialState(seed) {
  const st = { seed, cursor: 0, hair: [2, 3, 4].map(seq => ({ seq, frame: HAIR_SEQ_BOUNDS[seq] })), lightning: [], flag75: 0 };
  for (let i = 0; i < 4; i++) st.lightning.push({ timer: randRange(st, 30, 200), state: 2, index: i });
  return st;
}
// Advances one tick in place and returns the draw list (painter's order) for that tick.
function tick(st) {
  const draws = [];
  for (const l of st.lightning) {
    l.timer = l.timer !== 0 ? l.timer - 1 : randRange(st, 60, 600);
    if (l.timer < 7) {
      if (l.state >= 2) { draws.push({ kind: 'lightning', tile: l.index, centre: LIGHTNING_CENTRE[l.index] }); if (!st.flag75) st.flag75 = 20; }
      l.state = ((l.state - 1) << 24) >> 24;
      if (l.state <= 0) l.state = randRange(st, 0, 3);
    }
  }
  for (const h of st.hair) {
    if (h.seq === 1) h.frame = (h.frame + 1) & 255;
    h.frame = (h.frame + 1) & 255;
    if (h.frame >= HAIR_SEQ_BOUNDS[h.seq + 1]) {
      if (h.seq === 0) h.seq = 1;
      else if (h.seq === 1) { if (rand(st) & 4) h.seq = 2; }
      else if (h.seq === 2) h.seq = 0;
      h.frame = HAIR_SEQ_BOUNDS[h.seq];
    }
    draws.push({ kind: 'hair', tile: h.frame, centre: HAIR_SEQ_CENTRE[h.seq] });
  }
  draws.push({ kind: 'highlight', tile: st.cursor, centre: HIGHLIGHT_CENTRE[st.cursor] });
  return draws;
}
// Draw list for a state already in RAM (after its tick) - the draws that tick produced.
function drawsFromState(st, drewLightning) {
  const draws = drewLightning.map(i => ({ kind: 'lightning', tile: i, centre: LIGHTNING_CENTRE[i] }));
  for (const h of st.hair) draws.push({ kind: 'hair', tile: h.frame, centre: HAIR_SEQ_CENTRE[h.seq] });
  draws.push({ kind: 'highlight', tile: st.cursor, centre: HIGHLIGHT_CENTRE[st.cursor] });
  return draws;
}
function compose(draws, withPrompt = true) {
  const fb = new Uint16Array(320 * 240); fb.set(bg.words);
  const blit = (d, x0, y0) => { for (let y = 0; y < d.h; y++) for (let x = 0; x < d.w; x++) {
    const c = d.words[y * d.w + x], X = x0 + x, Y = y0 + y;
    if (c && X >= 0 && X < 320 && Y >= 0 && Y < 240) fb[Y * 320 + X] = c; } };
  for (const dr of draws) {
    const d = dr.kind === 'hair' ? hair.tiles[dr.tile].d : dr.kind === 'lightning' ? ltng.tiles[dr.tile].d : highlights[dr.tile].d;
    blit(d, dr.centre[0] - Math.trunc(d.w / 2), dr.centre[1] - Math.trunc(d.h / 2));
  }
  if (withPrompt) { // text: abr 1 (B+F) semi-transparent SPRT, so STP texels add, others replace
    const x0 = promptOrigin[0], y0 = promptOrigin[1] + promptBox[0];
    for (let y = 0; y < prompt.h; y++) for (let x = 0; x < prompt.w; x++) {
      const c = prompt.words[y * prompt.w + x], i = (y0 + y) * 320 + x0 + x;
      if (!c) continue;
      fb[i] = c & 0x8000 ? [0, 5, 10].reduce((s, n) => s | (Math.min(31, ((fb[i] >>> n) & 31) + ((c >>> n) & 31)) << n), 0) : c;
    }
  }
  return fb;
}

// ---------- JSON ----------
const tileMeta = t => ({ index: t.index, name: t.name, png: t.png, size: t.size, depthBits: t.depthBits, vram: t.vram, clut: t.clut });
const json = {
  source: 'Xena: Warrior Princess (SLUS-00977) main menu; see docs/xena-main-menu.md',
  screen: { width: 320, height: 240, origin: 'top-left, PS1 framebuffer pixels' },
  timing: {
    unit: 'menu tick = one pass of the main loop (main 0x80066628 VSync(0) then state 2 -> func_8006AC88)',
    note: 'The loop waits for the next vblank, so a tick is >= 1/60 s; the native port measured ~4 ticks per 7 vblanks (~34 Hz) in this menu. The real-hardware rate is not measured here.',
  },
  drawOrder: ['background (LoadImage, opaque)', 'lightning 0..3 (when active)', 'hair channel 0,1,2', 'menu highlight', 'prompt text'],
  blend: 'All sprites: SPRT, raw texture (no shading), not semi-transparent; texel 0x0000 is transparent, every other texel opaque. Sprites are placed by centre: topLeft = centre - trunc(size/2).',
  background: { png: 'background.png', source: bgRes.name, lba: bgRes.lba, size: [bg.w, bg.h], note: 'menu item labels are baked in; re-uploaded every tick by func_8006A924' },
  highlights: highlights.map(({ d, ...h }) => ({ ...h, item: ['New game', 'Load game', 'Training arena', 'Options'][h.index] })),
  menu: {
    cursorVar: 'D_800ED534 (u8, 0..3, kept between visits)',
    up: 'pressed bit 0x4 of D_800F16D0: cursor-- if cursor > 0 (no wrap), sound func_80084A34(0x39)',
    down: 'pressed bit 0x8: cursor++ if cursor < 3, sound 0x39',
    confirm: 'pressed bit 0x800 while input delay D_800F10F0 == 0 (set to 5 on entry): sound func_80084A34(0), free menu (func_8006AC00), then by cursor',
    actions: [
      { cursor: 0, item: 'New game', call: 'func_8006A9B0(1)' },
      { cursor: 1, item: 'Load game', call: 'func_80067640 (memory-card load); with cheat bit D_800F1258&1: func_8006BB70 and D_800F123C = 0x1A' },
      { cursor: 2, item: 'Training arena', call: 'func_8006A9B0(0)' },
      { cursor: 3, item: 'Options', call: 'func_8006E1EC' },
    ],
    blink: 'none: the highlight sprite is drawn every tick, no timer',
  },
  idle: { var: 'D_800F10E0', resetTo: 900, reset: 'menu entry and any newly pressed bit', decrement: 'main loop, 1 per tick',
    onZero: 'func_8006AC00 + func_8006E314: attract demo of level D_800ED748 (initial 2; next 2->11->15->2, 6->11), loading screen data\\levscr\\load%02d.tim, game state 8' },
  cheats: { ring: 'last 8 pressed masks D_800F11D0..D_800F11F0', sequences: CHEATS,
    effect: 'sequence 0 clears D_800F1258, sequence i>0 sets bit (i-1); sound func_80084A34(0xF)' },
  random: {
    rand: 'func_800B42D8: repeat 16: bit=(s>>>30)&1; if (s&4) bit^=1; s=(s<<1|bit)>>>0; return int16(s & 0xFFFF). State D_800F1748 (seeded from VSync(-1) if 0).',
    range: 'func_800B4350(lo,hi): lo + (((hi-lo+1) * (rand() & 0xFFFF)) >>> 16), inclusive',
    callOrderPerTick: 'lightning 0..3 (timer reload, then state reroll), then hair channels 0..2 (seq 1 exit test)',
  },
  hair: {
    source: hair.res.name, lba: hair.res.lba, tiles: hair.tiles.map(tileMeta),
    sequences: [0, 1, 2, 3, 4].map(s => ({ seq: s, firstTile: HAIR_SEQ_BOUNDS[s], endTile: HAIR_SEQ_BOUNDS[s + 1], centre: HAIR_SEQ_CENTRE[s],
      step: s === 1 ? 2 : 1, next: ['1', 'rand() & 4 ? 2 : 1', '0', '3 (loop)', '4 (loop)'][s] })),
    channels: [{ channel: 0, initialSeq: 2 }, { channel: 1, initialSeq: 3 }, { channel: 2, initialSeq: 4 }],
    perTick: 'frame += (seq==1 ? 2 : 1); if frame >= endTile[seq]: seq = next(seq), frame = firstTile[seq]; draw tile[frame] centred at centre[seq]',
  },
  lightning: {
    source: ltng.res.name, lba: ltng.res.lba, tiles: ltng.tiles.map(tileMeta),
    bolts: LIGHTNING_CENTRE.map((c, i) => ({ bolt: i, tile: i, centre: c, topLeft: [c[0] - Math.trunc(ltng.tiles[i].size[0] / 2), c[1] - Math.trunc(ltng.tiles[i].size[1] / 2)] })),
    init: 'timer = rand(30,200), state = 2',
    perTick: 'timer = timer ? timer-1 : rand(60,600); if timer < 7: { if state >= 2: draw bolt (and ctx+0x75 = 20 if 0, not read by the menu); state--; if state <= 0: state = rand(0,3) }',
    blend: 'opaque sprite like all others (no additive flash, background unchanged)',
  },
  prompt: {
    bytes: [...PROMPT], text: 'Russian build: "Нажми (X) для выбора" in the game font (Latin code points map to Cyrillic glyphs); byte 0x04 = button glyph 0x5B',
    anchor: { x: PROMPT_POS[0], y: PROMPT_POS[1], align: 'right (func_800BA4E4(1): x -= text extent incl. trailing 3 px spacing)' },
    topLeft: [promptOrigin[0], promptOrigin[1] + promptBox[0]], png: 'font/prompt.png', size: [prompt.w, prompt.h],
    colour: 'SPRT 16x16 (0x7C) modulated by 0x7F: c5 = (t5 * 127) >> 7',
    blend: 'semi-transparent, abr 1 (B+F additive): every visible glyph texel has STP in the runtime CLUT, so the whole text is added to the background (per channel, clamped to 31) (func_800BA514(1,1) -> D_801002F1 abr, D_801002F0 semi flag)',
    stpTexels: prompt.words.filter(c => c & 0x8000).length, opaqueTexels: prompt.words.filter(c => c && !(c & 0x8000)).length,
    font: { png: 'font/xenafont.png', source: fontRes.name, lba: fontRes.lba, cell: 'glyph g: u=(g%16)*16, v=32+floor(g/16)*16, 16x16',
      advance: 'width[g] + 3 (digits 10); width = rightmost opaque column of the cell; width(space)=2, width(g)-=1, digits 7', glyphWidth },
    glyphs: promptGlyphs.map(p => ({ ...p, x: p.x + promptOrigin[0], y: p.y + promptOrigin[1] })),
  },
};

// ---------- verification against native RAM dumps + screenshots ----------
const shotsDir = option('--verify-shots'), ramDir = option('--verify-ram');
if (shotsDir && ramDir) {
  const readBmp = file => { const b = fs.readFileSync(file), at = b.readUInt32LE(10), w = b.readInt32LE(18), h0 = b.readInt32LE(22), h = Math.abs(h0);
    const stride = Math.ceil(w * 3 / 4) * 4, px = new Uint16Array(w * h);
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) { const o = at + (h0 > 0 ? h - 1 - y : y) * stride + x * 3;
      px[y * w + x] = (b[o + 2] >> 3) | ((b[o + 1] >> 3) << 5) | ((b[o] >> 3) << 10); }
    return { w, h, px }; };
  const readState = m => { const ctx = m.readUInt32LE(0xF1238) & 0x1FFFFF;
    return { seed: m.readUInt32LE(0xF1748), cursor: m[0xED534], flag75: m[ctx + 0x75],
      hair: [0, 1, 2].map(i => ({ seq: m[ctx + 0x65 + 3 * i], frame: m[ctx + 0x66 + 3 * i] })),
      lightning: [0, 1, 2, 3].map(i => ({ timer: m.readInt16LE(ctx + 0x76 + 4 * i), state: m.readInt8(ctx + 0x78 + 4 * i), index: m[ctx + 0x79 + 4 * i] })) }; };
  const key = s => JSON.stringify([s.seed, s.cursor, s.hair, s.lightning]);
  const files = fs.readdirSync(ramDir).filter(f => /^ram_\d+\.bin$/.test(f)).sort();
  const frames = files.map(f => ({ frame: +f.match(/\d+/)[0], m: fs.readFileSync(path.join(ramDir, f)) }))
    .filter(f => f.m.readUInt32LE(0xF2648) === 2 && f.m.readUInt32LE(0xF1238) !== 0) // game state 2 = main menu
    .map(f => ({ frame: f.frame, st: readState(f.m) }));
  // 1) Simulation: from the first dump, step tick() whenever the RAM state changes and require equality.
  const ticks = []; for (const f of frames) if (!ticks.length || key(f.st) !== key(ticks.at(-1).st)) ticks.push(f);
  const sim = structuredClone(ticks[0].st); let simOk = 0, simBad = [];
  const drawn = new Map(); // frame -> draws of the tick that produced this RAM state
  let gaps = 0;
  for (let i = 1; i < ticks.length; i++) {
    if (ticks[i].frame - ticks[i - 1].frame > 3) { gaps++; Object.assign(sim, structuredClone(ticks[i].st)); continue; } // dump gap: resync
    tick(sim);
    // Draw list from RAM alone: a bolt was drawn in this tick iff its new timer < 7 and its previous state >= 2.
    const prev = ticks[i - 1].st, cur = ticks[i].st;
    drawn.set(ticks[i].frame, drawsFromState(cur, cur.lightning.filter((l, k) => l.timer < 7 && prev.lightning[k].state >= 2).map(l => l.index)));
    if (key(sim) === key(ticks[i].st)) simOk++; else { simBad.push(ticks[i].frame); Object.assign(sim, structuredClone(ticks[i].st)); }
  }
  // 2) Composite each RAM state's draws and compare with screenshots at offsets 0..2 frames.
  const compare = (fb, shot) => { let exact = 0, se = 0;
    for (let i = 0; i < fb.length; i++) { if (fb[i] === shot.px[i]) exact++;
      for (const n of [0, 5, 10]) { const d = (((fb[i] >>> n) & 31) - ((shot.px[i] >>> n) & 31)) * 255 / 31; se += d * d; } }
    const mse = se / (fb.length * 3); return { exact, total: fb.length, psnr: mse === 0 ? Infinity : 10 * Math.log10(255 * 255 / mse) }; };
  const results = [];
  for (const [frame, draws] of drawn) for (const off of [0, 1, 2]) {
    const shotFile = path.join(shotsDir, `frame_${String(frame + off).padStart(6, '0')}.bmp`);
    if (!fs.existsSync(shotFile)) continue;
    results.push({ frame, offset: off, lightning: draws.filter(d => d.kind === 'lightning').map(d => d.tile), ...compare(compose(draws), readBmp(shotFile)) });
  }
  const byOffset = [0, 1, 2].map(off => { const r = results.filter(x => x.offset === off);
    return { offset: off, frames: r.length, fullyExact: r.filter(x => x.exact === x.total).length,
      minExact: Math.min(...r.map(x => x.exact)), minPsnr: Math.min(...r.map(x => x.psnr)) }; });
  const best = byOffset.reduce((a, b) => b.fullyExact > a.fullyExact ? b : a);
  const sample = results.filter(r => r.offset === best.offset);
  const sampleFrames = [sample[0], sample.find(r => r.lightning.length), sample.at(-1)].filter(Boolean);
  for (const r of sampleFrames) {
    const fb = compose(drawn.get(r.frame)), rgba = new Uint8Array(320 * 240 * 4);
    for (let i = 0; i < fb.length; i++) rgba.set([...rgb8(fb[i]), 255], i * 4);
    writePng(`verify/composite_${r.frame}.png`, { w: 320, h: 240, rgba });
  }
  json.verification = { ramDumps: frames.length, distinctTicks: ticks.length, simulatedTicksMatchingRam: simOk, simulatedTicksMismatching: simBad, dumpGaps: gaps,
    screenshotOffsetFrames: best.offset, byOffset, worst: sample.filter(r => r.exact !== r.total).slice(0, 10),
    lightningFrames: sample.filter(r => r.lightning.length).map(r => ({ frame: r.frame, bolts: r.lightning, exact: r.exact, psnr: r.psnr })) };
  console.log(JSON.stringify(json.verification, (k, v) => v === Infinity ? 'inf' : v, 1));
}
fs.writeFileSync(path.join(out, 'main_menu.json'), JSON.stringify(json, (k, v) => v === Infinity ? 'inf' : v, 1));
console.log(`wrote ${out}: ${hair.tiles.length} hair tiles, ${ltng.tiles.length} lightning tiles, ${highlights.length} highlights, prompt ${prompt.w}x${prompt.h} at ${json.prompt.topLeft}`);
