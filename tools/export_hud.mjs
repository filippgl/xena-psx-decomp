#!/usr/bin/env node
// Export the original in-game HUD of Xena: Warrior Princess (SLUS-00977) for a Unity rebuild:
// exact-pixel PNGs of every HUD texture, a JSON layout (320x240 PS1 screen space) with the value
// mappings, and checks against a native RAM/VRAM/frame snapshot. See docs/xena-hud-audit.md.
//
// Evidence chain (MIPS addresses, all in the SLUS-00977 executable):
//   * func_8007515C (called from main 0x80065C7C) loads every HUD TIM with func_800A7224 (TIM uploaded
//     at its own VRAM rectangle; descriptor +0 x,+2 y,+4 width in texels,+6 h,+8/+A CLUT x/y,+C depth)
//     and creates each sprite with func_800A7D3C(cx, cy, desc, r, g, b, abr, semiTrans, layer).
//   * func_800A7D3C/func_800A5634: entry = D_800FC088 + id*0x24 (max 0x3A, count D_800F078A) =
//     DR_MODE (dfe=1, dtd=0, tpage=GetTPage(depth, abr, x, y)) + SPRT (code 0x64, SetShadeTex(1) => raw
//     texture), top-left = centre - (w/2, h/2); +0x20 visible, +0x22 layer.
//   * func_800A55D0 (via func_800A7EEC) re-crops a sprite: x, y, u = (x&63)*k + du, v = (y&255) + dv, w, h.
//   * func_800A57B8 adds every visible entry to OT[0] when layer != 0 (all HUD sprites have layer 1).
//   * func_80075C2C (per frame, from 0x8006D26C / 0x80066380 / 0x800769E0) updates health, companion,
//     enemy timers, man icons, key icon, karma ring; func_80076258 shows enemy/boss bars;
//     func_80076D9C shows inventory icons; func_8007674C resets the HUD.
//
//   node tools/export_hud.mjs --root DECOMP --out DIR [--ram ram.bin --vram vram.bmp --frame frame.bmp]
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const argv = process.argv.slice(2);
const option = (name, fallback) => { const i = argv.indexOf(name); return i < 0 ? fallback : argv[i + 1]; };
const root = path.resolve(option('--root', '.'));
const out = path.resolve(option('--out', 'hud-export'));
const { png } = await import(pathToFileURL(path.join(root, 'tools', 'lib', 'png.mjs')).href);
const { readStm } = await import(pathToFileURL(path.join(root, 'tools', 'lib', 'xena_model.mjs')).href);
fs.mkdirSync(out, { recursive: true });
const hex = n => '0x' + (n >>> 0).toString(16).toUpperCase();

// ---------- executable ----------
const exe = fs.readFileSync(path.join(root, 'xena.exe'));
const base = exe.readUInt32LE(0x18);
const eo = a => a - base + 0x800;
const cstr = a => { let o = eo(a), e = o; while (exe[e]) e++; return exe.toString('latin1', o, e); };
const ICON_NAMES = Array.from({ length: 7 }, (_, i) => cstr(exe.readUInt32LE(eo(0x800EE204 + i * 4))));
const ICON_MASKS = Array.from({ length: 7 }, (_, i) => exe.readUInt32LE(eo(0x800EE258 + i * 4)));
const KARMA_OFFSETS = Array.from({ length: 29 }, (_, i) => [exe[eo(0x800C76C0 + i * 2)], exe[eo(0x800C76C1 + i * 2)]]);

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
    if (size !== 12 + width * height * 2) throw Error('Bad TIM block');
    const data = bytes.subarray(cursor + 12, cursor + size); cursor += size;
    return { x, y, width, height, data };
  };
  const clut = flags & 8 ? block() : null;
  return { depth, clut, image: block() };
}

// Decode a TIM image with an optional replacement CLUT. PSX rules: texel colour 0x0000 is never drawn
// (alpha 0); texels with STP (bit 15) are blended when the primitive is semi-transparent; others opaque.
function decode(img, clutData = img.clut?.data) {
  const k = [4, 2, 1][img.depth], w = img.image.width * k, h = img.image.height;
  const rgba = new Uint8Array(w * h * 4), stp = new Uint8Array(w * h * 4), words = new Uint16Array(w * h);
  const counts = { transparent: 0, stp: 0, opaque: 0 };
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const word = img.image.data.readUInt16LE((y * img.image.width + Math.floor(x / k)) * 2);
    let c = word;
    if (img.depth === 0) c = clutData.readUInt16LE(((word >>> ((x & 3) * 4)) & 15) * 2);
    else if (img.depth === 1) c = clutData.readUInt16LE(((word >>> ((x & 1) * 8)) & 255) * 2);
    const i = (y * w + x) * 4; words[y * w + x] = c;
    rgba.set([0, 5, 10].map(n => ((c >>> n) & 31) << 3 | ((c >>> n) & 31) >>> 2), i);
    rgba[i + 3] = c === 0 ? 0 : 255;
    if (c === 0) counts.transparent++; else if (c & 0x8000) { counts.stp++; stp.set([255, 255, 255, 255], i); } else { counts.opaque++; stp[i + 3] = 255; }
  }
  return { w, h, rgba, stp, words, counts };
}

// ---------- snapshots ----------
function readBmp(file) {
  const b = fs.readFileSync(file), at = b.readUInt32LE(10), w = b.readInt32LE(18), h0 = b.readInt32LE(22), h = Math.abs(h0), bpp = b.readUInt16LE(28);
  if (bpp !== 24) throw Error('Expected 24-bit BMP');
  const stride = Math.ceil(w * 3 / 4) * 4;
  return { w, h, px: (x, y) => { const o = at + (h0 > 0 ? h - 1 - y : y) * stride + x * 3; return [b[o + 2], b[o + 1], b[o]]; } };
}
const ram = option('--ram') ? fs.readFileSync(option('--ram')) : null;
const vramBmp = option('--vram') ? readBmp(option('--vram')) : null;
const frameBmp = option('--frame') ? readBmp(option('--frame')) : null;
if (vramBmp && (vramBmp.w !== 1024 || vramBmp.h !== 512)) throw Error('Expected 1024x512 VRAM BMP');
const vram5 = (x, y) => vramBmp.px(x, y).map(v => v >> 3);
const ro = a => a & 0x1FFFFF, r16 = a => ram.readInt16LE(ro(a)), ru16 = a => ram.readUInt16LE(ro(a)), r32 = a => ram.readUInt32LE(ro(a)), rs8 = a => ram.readInt8(ro(a));

// ---------- textures ----------
const TIMS = {
  hilt: 'DATA\\SCREENS\\HILT.TIM', sword: 'DATA\\SCREENS\\SWORD.TIM', blood: 'DATA\\SCREENS\\BLOOD.TIM',
  dagger: 'DATA\\SCREENS\\DAGGER.TIM', daggered: 'DATA\\SCREENS\\DAGGERED.TIM', karmag3: 'DATA\\SCREENS\\KARMAG3.TIM',
  man_icon: 'DATA\\SCREENS\\MAN_ICON.TIM', key1_hud: 'DATA\\SCREENS\\KEY1_HUD.TIM', shld_hud: 'DATA\\SCREENS\\SHLD_HUD.TIM',
};
for (const n of ICON_NAMES) TIMS[n] = `DATA\\SCREENS\\${n.toUpperCase()}.TIM`;
const textures = {};
function vramCheck(blocks) {
  if (!vramBmp) return null;
  let compared = 0, different = 0;
  for (const bl of blocks) for (let y = 0; y < bl.height; y++) for (let x = 0; x < bl.width; x++) {
    const w = bl.data.readUInt16LE((y * bl.width + x) * 2), v = vram5(bl.x + x, bl.y + y); compared++;
    if (v[0] !== (w & 31) || v[1] !== ((w >> 5) & 31) || v[2] !== ((w >> 10) & 31)) different++;
  }
  return { comparedWords: compared, differentWords: different };
}
function writeTexture(key, img, meta, clutData) {
  const d = decode(img, clutData);
  fs.mkdirSync(path.join(out, 'textures'), { recursive: true });
  fs.writeFileSync(path.join(out, 'textures', `${key}.png`), png(d.w, d.h, d.rgba));
  fs.writeFileSync(path.join(out, 'textures', `${key}-stp.png`), png(d.w, d.h, d.stp));
  textures[key] = { ...meta, png: `textures/${key}.png`, stpMask: `textures/${key}-stp.png`, size: [d.w, d.h], depthBits: [4, 8, 16][img.depth],
    vramPixels: [img.image.x, img.image.y, img.image.width, img.image.height], clut: img.clut ? [img.clut.x, img.clut.y] : null,
    texelCounts: d.counts, decoded: d };
  return textures[key];
}
for (const [key, file] of Object.entries(TIMS)) {
  const res = resource(file), tim = readTim(res.bytes);
  writeTexture(key, tim, { source: file, lba: res.lba, vramCheck: vramCheck([tim.clut, tim.image].filter(Boolean)) });
}
// karma.stm: 29 ring segments, one 8-bit CLUT; karmag3.tim supplies the "lit" CLUT (D_800F13A0).
const karmaRes = resource('DATA\\LEVELS\\KARMA.STM'), karma = readStm(karmaRes.bytes);
const glowClut = readTim(resource(TIMS.karmag3).bytes).clut;
karma.images.forEach((img, i) => {
  const meta = { source: karmaRes.name, lba: karmaRes.lba, stmName: img.name, vramCheck: vramCheck([img.clut, img.image].filter(Boolean)) };
  writeTexture(`karma-${String(i).padStart(2, '0')}`, img, meta);
  writeTexture(`karma-${String(i).padStart(2, '0')}-lit`, img, { ...meta, clutOverride: [glowClut.x, glowClut.y], vramCheck: null }, glowClut.data);
});
const T = k => textures[k];
// Convenience composites of the whole ring (segment i top-left = (250+dx_i, 191+dy_i) on screen).
const ringBounds = KARMA_OFFSETS.reduce((b, [dx, dy], i) => { const t = T(`karma-${String(i).padStart(2, '0')}`);
  return [Math.max(b[0], dx + t.size[0]), Math.max(b[1], dy + t.size[1])]; }, [0, 0]);
for (const suffix of ['', '-lit']) {
  const rgba = new Uint8Array(ringBounds[0] * ringBounds[1] * 4);
  KARMA_OFFSETS.forEach(([dx, dy], i) => { const d = T(`karma-${String(i).padStart(2, '0')}${suffix}`).decoded;
    for (let y = 0; y < d.h; y++) for (let x = 0; x < d.w; x++) if (d.words[y * d.w + x])
      rgba.set(d.rgba.subarray((y * d.w + x) * 4, (y * d.w + x) * 4 + 4), ((dy + y) * ringBounds[0] + dx + x) * 4); });
  fs.writeFileSync(path.join(out, 'textures', `karma-ring${suffix}.png`), png(ringBounds[0], ringBounds[1], rgba));
}
const W = k => T(k).size[0], H = k => T(k).size[1];

// ---------- layout (PS1 320x240 framebuffer coordinates, top-left of each sprite) ----------
const half = v => Math.trunc(v / 2);
const sprite = (id, tex, x, y, blend, extra = {}) => ({ id, texture: tex, x, y, w: W(tex), h: H(tex), ...blend, ...extra });
const OPAQUE_ADD = { abr: 1, semiTransparent: false }; // abr set but SPRT not semi => plain opaque (0x0000 = hole)
const elements = [];
elements.push({ group: 'xenaHealth', evidence: 'func_8007515C 0x80075244-0x800753BC; update func_80075C2C 0x80075CE4-0x80075E1C',
  value: 'Xena actor D_800EE174: hp = +0x10, max = (+0xC)->+8 (500 in level 01)',
  mapping: 'len = hp<=0 ? 0 : clamp(trunc((swordW-3)*hp/max), 3, swordW-3); shown len (D_800F1330) grows +1 px/frame toward len, drops instantly to len; when D_800F1330 is 0 it is set to len+1 first, which only forces an immediate redraw at len',
  sprites: [
    sprite('xena_hilt', 'hilt', 16, 206, OPAQUE_ADD, { id_var: 'D_800F1418' }),
    sprite('xena_blade', 'sword', 41, 209, OPAQUE_ADD, { id_var: 'D_800F1478', crop: 'x=41, u=0, w=len, h=swordH' }),
    sprite('xena_blade_empty', 'blood', 41, 209, OPAQUE_ADD, { id_var: 'D_800F13E0', crop: 'x=41+len, u=len, w=bloodW-len, h=bloodH' }),
  ] });
elements.push({ group: 'bossHealth', evidence: 'func_8007515C 0x800753E8-0x80075564; func_80076258 0x800764BC-0x800765EC; slot D_800F2F08',
  value: 'enemy whose class descriptor (+0xC)->+6 bit0 is set (except level D_800F2772==0x12); hp +0x10, max = desc+8 + (+0x138 ? (+0x138)->+0x8A : +0x84)',
  mapping: 'same as Xena: len = clamp(trunc((swordW-3)*hp/max), 3, swordW-3), applied immediately; visible 60 frames after each update (slot +4 timer), hides enemy slots 1-2',
  sprites: [
    sprite('boss_hilt', 'hilt', 93, 24, OPAQUE_ADD, { id_var: 'D_800F1332' }),
    sprite('boss_blade', 'sword', 118, 27, OPAQUE_ADD, { id_var: 'D_800F2F10', crop: 'x=118, u=0, w=len' }),
    sprite('boss_blade_empty', 'blood', 118, 27, OPAQUE_ADD, { id_var: 'D_800F2F11', crop: 'x=118+len, u=len, w=bloodW-len' }),
  ] });
for (let i = 0; i < 4; i++) elements.push({ group: `enemyHealth${i}`, evidence: 'func_8007515C 0x80075708-0x80075814; func_80076258 0x800765F4-0x8007671C; slot D_800F2ED8+i*0xC',
  value: 'enemy actor within range of Xena (func_80032868: 4000, class 0xAF 30000, boss 7500), checked when (actor>>5)&7 == frame&7; hp +0x10, max as boss',
  mapping: 'len = clamp(trunc((daggerW-22)*hp/max)+18, 18, daggerW-4); visible 60 frames after last update; slot freed when hp<=0',
  sprites: [
    sprite(`enemy${i}_full`, 'dagger', 40 + 65 * i, 25, OPAQUE_ADD, { crop: `x=${40 + 65 * i}, u=0, w=len` }),
    sprite(`enemy${i}_empty`, 'daggered', 40 + 65 * i, 25, OPAQUE_ADD, { crop: `x=${40 + 65 * i}+len, u=len, w=daggeredW-len` }),
  ] });
elements.push({ group: 'companionHealth', evidence: 'func_8007515C 0x80075818-0x80075920; func_80075C2C 0x80075E20-0x80075F44; func_80075AA8',
  value: 'companion actor D_800EE178 (null => hidden); hp +0x10, max (+0xC)->+8',
  mapping: 'len = hp<=0 ? 16 : clamp(trunc((daggerW-22)*hp/max)+18, 18, daggerW-4); updated when changed (D_800F1336)',
  sprites: [
    sprite('companion_full', 'dagger', 16, 193, OPAQUE_ADD, { id_var: 'D_800F1333', crop: 'x=16, u=0, w=len' }),
    sprite('companion_empty', 'daggered', 16, 193, OPAQUE_ADD, { id_var: 'D_800F1334', crop: 'x=16+len, u=len, w=daggeredW-len' }),
  ] });
elements.push({ group: 'karmaRing', evidence: 'func_8007515C 0x800755A4-0x80075660 (offsets D_800C76C0); func_80075C2C 0x8007605C-0x800761D8',
  value: 'meter D_800F1135 (0..96, +10/-10/-30 per kill, see xena-damage-audit 2.1); power-up timer D_800F1136',
  mapping: 'n = trunc(meter*29/96); segment (i+15)%29 uses the lit CLUT (karmag3, D_800F13A0) for i<n, else the normal CLUT (D_800F139E); while D_800F1136>0: every 4th segment from phase=(frame>>1)&3 -> SetSemiTrans(1), segments phase+1 step 4 -> lit CLUT + SetSemiTrans(0) (chase animation)',
  sprites: KARMA_OFFSETS.map(([dx, dy], i) => sprite(`karma${i}`, `karma-${String(i).padStart(2, '0')}`, 250 + dx, 191 + dy, { abr: 0, semiTransparent: true }, { id_var: `D_800F1380+${i}` })) });
elements.push({ group: 'inventoryIcons', evidence: 'func_8007515C 0x80075184-0x80075228; func_80076D9C; func_80075C2C 0x80075C50-0x80075CE0; selection func_80041228',
  value: 'counts D_800F277E..D_800F2784 (icon i), selected item bitmask D_800F2787 (bits D_800EE258)',
  mapping: 'icon i visible iff count_i != 0; semiTransparent iff (D_800F2787 & (1<<i)) == 0 (only STP texels blend); refreshed when D_800F2788 set or D_800F2787 changes',
  sprites: ICON_NAMES.map((n, i) => sprite(`icon_${n}`, n, 301 - half(W(n)), (i === 0 ? 32 : 56 + 20 * (i - 1)) - half(H(n)), { abr: 0, semiTransparent: 'when not selected' },
    { id_var: `D_800F2E28+${i}*0x1C`, countVar: hex(0x800F277E + i), mask: ICON_MASKS[i] })) });
elements.push({ group: 'manIcons', evidence: 'func_8007515C 0x80075924-0x800759D8; func_80075B9C; func_80075C2C 0x80075FD4-0x80076014',
  value: 'level D_800F2772 == 2: n = 4 - D_800F2777; other levels hidden when D_800F264C < 5',
  mapping: 'icon i visible if i < n; icon n blinks with bit 1 of message timer D_800F132A',
  sprites: [0, 1, 2, 3].map(i => sprite(`man${i}`, 'man_icon', 176 + 18 * i, 200, { abr: 0, semiTransparent: false }, { id_var: `D_800F1358+${i}` })) });
elements.push({ group: 'keyIcon', evidence: 'func_8007515C 0x800759DC-0x80075A64; func_80075C2C 0x80076018-0x80076058',
  value: 'levels D_800F2772 in {2,3,10,25}: visible = D_800F277D & 1', mapping: 'on/off',
  sprites: [sprite('key', 'key1_hud', 296, 170, { abr: 0, semiTransparent: false }, { id_var: 'D_800F1378' })] });

// ---------- RAM: live sprite table ----------
let ramReport = null;
const live = [];
if (ram) {
  const count = r16(0x800F078A);
  for (let i = 0; i < count; i++) {
    const e = 0x800FC088 + i * 0x24, tp = r32(e + 4) & 0xFFFF, code = ram[ro(e + 0x13)], clut = ru16(e + 0x1A);
    live.push({ id: i, visible: r16(e + 0x20), layer: r16(e + 0x22), code, semi: !!(code & 2), raw: !!(code & 1),
      x: r16(e + 0x14), y: r16(e + 0x16), u: ram[ro(e + 0x18)], v: ram[ro(e + 0x19)], w: r16(e + 0x1C), h: r16(e + 0x1E),
      tpage: { x: (tp & 15) * 64, y: tp & 16 ? 256 : 0, abr: (tp >> 5) & 3, depth: (tp >> 7) & 3 }, clut: [(clut & 63) * 16, clut >> 6] });
  }
  const ids = { xena_hilt: r32(0x800F1418), xena_blade: r32(0x800F1478), xena_blade_empty: r32(0x800F13E0), boss_hilt: rs8(0x800F1332),
    boss_blade: rs8(0x800F2F10), boss_blade_empty: rs8(0x800F2F11), companion_full: rs8(0x800F1333), companion_empty: rs8(0x800F1334), key: r32(0x800F1378) };
  for (let i = 0; i < 4; i++) { ids[`enemy${i}_full`] = rs8(0x800F2ED8 + i * 12 + 8); ids[`enemy${i}_empty`] = rs8(0x800F2ED8 + i * 12 + 9); ids[`man${i}`] = rs8(0x800F1358 + i); }
  for (let i = 0; i < 29; i++) ids[`karma${i}`] = rs8(0x800F1380 + i);
  ICON_NAMES.forEach((n, i) => { ids[`icon_${n}`] = r32(0x800F2E28 + i * 0x1C); });
  const xena = r32(0x800EE174);
  const state = { xenaHp: r16(xena + 0x10), xenaMax: r16(r32(xena + 0xC) + 8), shownLen: r16(0x800F1330), companion: hex(r32(0x800EE178)),
    meter: ram[ro(0x800F1135)], powerTimer: r16(0x800F1136), selected: ram[ro(0x800F2787)], counts: ICON_NAMES.map((_, i) => ram[ro(0x800F277E + i)]),
    level: ram[ro(0x800F2772)], frameCounter: r32(0x800ECEA0), clutLit: hex(ru16(0x800F13A0)), clutNormal: hex(ru16(0x800F139E)) };
  // Compare the static layout with the live sprite entries (static parts only; cropped bars use their mapping).
  const checks = [];
  for (const g of elements) for (const s of g.sprites) {
    const id = ids[s.id], l = live[id], t = T(s.texture);
    if (!l) { checks.push({ id: s.id, error: 'no live sprite' }); continue; }
    const k = 16 / t.depthBits, [vx, vy] = t.vramPixels, cropped = !!s.crop;
    const texOk = l.tpage.depth === [4, 8, 16].indexOf(t.depthBits) && l.tpage.x === (vx & ~63) && l.tpage.y === (vy & 256)
      && (cropped && !l.visible ? true : l.v === (vy & 255)) && (cropped || l.u === (vx & 63) * k);
    const clutTex = t.clut;
    const clutOk = !clutTex || (l.clut[0] === clutTex[0] && l.clut[1] === clutTex[1]) || (s.texture.startsWith('karma') && l.clut[0] === glowClut.x && l.clut[1] === glowClut.y);
    const posOk = cropped ? null : l.x === s.x && l.y === s.y && l.w === s.w && l.h === s.h;
    checks.push({ id: s.id, sprite: id, live: { x: l.x, y: l.y, w: l.w, h: l.h, u: l.u, v: l.v, visible: l.visible, semi: l.semi, abr: l.tpage.abr, clut: l.clut },
      expected: { x: s.x, y: s.y, w: s.w, h: s.h }, positionMatches: posOk, texturePageMatches: texOk, clutMatches: clutOk });
  }
  // Health bar mapping check
  const swordW = W('sword'), hp = state.xenaHp, max = state.xenaMax;
  const len = hp <= 0 ? 0 : Math.min(Math.max(Math.trunc((swordW - 3) * hp / max), 3), swordW - 3);
  const blade = live[ids.xena_blade], empty = live[ids.xena_blade_empty];
  const healthCheck = { hp, max, expectedLen: len, shownLen: state.shownLen, blade: [blade.x, blade.u, blade.w], empty: [empty.x, empty.u, empty.w],
    matches: blade.w === len && empty.x === 41 + len && empty.w === W('blood') - len && empty.u === (T('blood').vramPixels[0] & 63) * 4 + len };
  const karmaN = Math.trunc(state.meter * 29 / 96);
  const karmaCheck = { meter: state.meter, litSegments: karmaN, liveCluts: [...new Set(Array.from({ length: 29 }, (_, i) => live[ids[`karma${i}`]].clut.join(',')))] };
  ramReport = { spriteCount: count, ids, state, checks, healthCheck, karmaCheck,
    karmaOffsetsMatchRam: KARMA_OFFSETS.every(([dx, dy], i) => ram[ro(0x800C76C0 + i * 2)] === dx && ram[ro(0x800C76C1 + i * 2)] === dy) };
}

// ---------- reconstruction vs framebuffer ----------
// Rebuild every visible HUD sprite from the exported PNG pixels (texture-space u,v relative to the
// texture origin) at the live position/crop, then compare non-transparent, non-blended texels with
// the PS1 framebuffers in the VRAM snapshot (dither is off for SPRT, raw texture => exact 5-bit match).
let fbReport = null;
if (ram && vramBmp) {
  const byId = new Map(); for (const g of elements) for (const s of g.sprites) byId.set(ramReport.ids[s.id], s);
  // AddPrim prepends to OT[0], so the GPU draws the highest sprite id first and id 0 last (on top).
  // compose(reverse=true) reproduces that; reverse=false (creation order) is kept as a control.
  const compose = reverse => {
    const overlay = new Uint8Array(320 * 240 * 4), top = new Map();
    const order = live.filter(l => l.visible && byId.has(l.id)); if (reverse) order.reverse();
    for (const l of order) {
      const s = byId.get(l.id);
      let tex = T(s.texture);
      if (s.texture.startsWith('karma') && l.clut[0] === glowClut.x && l.clut[1] === glowClut.y) tex = T(`${s.texture}-lit`);
      const k = [4, 2, 1][l.tpage.depth], u0 = (tex.vramPixels[0] & 63) * k, v0 = tex.vramPixels[1] & 255, d = tex.decoded;
      for (let j = 0; j < l.h; j++) for (let i = 0; i < l.w; i++) {
        const tx = l.u + i - u0, ty = l.v + j - v0, sx = l.x + i, sy = l.y + j;
        if (tx < 0 || ty < 0 || tx >= d.w || ty >= d.h || sx < 0 || sy < 0 || sx >= 320 || sy >= 240) continue;
        const c = d.words[ty * d.w + tx]; if (!c) continue;
        const blended = l.semi && !!(c & 0x8000), key = sy * 320 + sx;
        overlay.set(d.rgba.subarray((ty * d.w + tx) * 4, (ty * d.w + tx) * 4 + 4), key * 4);
        if (blended) overlay[key * 4 + 3] = 128;
        top.set(key, { sx, sy, c, blended, abr: l.tpage.abr, sprite: s.id });
      }
    }
    return { overlay, cells: [...top.values()] };
  };
  const { overlay, cells } = compose(true), control = compose(false).cells;
  fs.writeFileSync(path.join(out, 'hud_overlay_320x240.png'), png(320, 240, overlay));
  // Opaque texels must match exactly. A blended abr-0 texel F over an unknown background B gives
  // R = (B + F) >> 1, so it is consistent only if some B in 0..31 produces R on every channel.
  const score = (list, by) => {
    let opaque = 0, match = 0, blended = 0, blendConsistent = 0; const bad = {};
    for (const p of list) {
      const v = vram5(p.sx, by + p.sy), f = [p.c & 31, (p.c >> 5) & 31, (p.c >> 10) & 31];
      if (p.blended) {
        blended++;
        if (p.abr === 0 && f.every((fc, i) => { const b0 = 2 * v[i] - fc; return (b0 >= 0 && b0 <= 31) || (b0 + 1 >= 0 && b0 + 1 <= 31); })) blendConsistent++;
        continue;
      }
      opaque++;
      if (v[0] === f[0] && v[1] === f[1] && v[2] === f[2]) match++; else bad[p.sprite] = (bad[p.sprite] ?? 0) + 1;
    }
    return { framebufferY: by, opaqueTexels: opaque, exactMatches: match, mismatches: opaque - match, mismatchBySprite: bad,
      blendedTexels: blended, blendedConsistentWithAbr0: blendConsistent };
  };
  const buffers = [0, 256].map(by => score(cells, by));
  const creationOrderControl = [0, 256].map(by => { const r = score(control, by); return { framebufferY: by, mismatches: r.mismatches, mismatchBySprite: r.mismatchBySprite }; });
  // Diff image for the best buffer: green = match, red = mismatch, blue = blended (not compared).
  const best = buffers.reduce((a, b) => (b.exactMatches > a.exactMatches ? b : a));
  const diff = new Uint8Array(320 * 240 * 4);
  for (let y = 0; y < 240; y++) for (let x = 0; x < 320; x++) { const v = vramBmp.px(x, best.framebufferY + y); diff.set([v[0] >> 2, v[1] >> 2, v[2] >> 2, 255], (y * 320 + x) * 4); }
  for (const p of cells) {
    const v = vram5(p.sx, best.framebufferY + p.sy);
    const ok = v[0] === (p.c & 31) && v[1] === ((p.c >> 5) & 31) && v[2] === ((p.c >> 10) & 31);
    diff.set(p.blended ? [0, 0, 255, 255] : ok ? [0, 255, 0, 255] : [255, 0, 0, 255], (p.sy * 320 + p.sx) * 4);
  }
  fs.writeFileSync(path.join(out, 'hud_diff_vram_framebuffer.png'), png(320, 240, diff));
  // Native port frame (wider than 320): search the horizontal offset where the HUD matches best.
  let frame = null;
  if (frameBmp) {
    const opaqueCells = cells.filter(p => !p.blended);
    let bestF = null;
    for (let dy = 0; dy <= frameBmp.h - 240; dy++) for (let dx = 0; dx <= frameBmp.w - 320; dx++) {
      let m = 0;
      for (const p of opaqueCells) { const v = frameBmp.px(p.sx + dx, p.sy + dy); if ((v[0] >> 3) === (p.c & 31) && (v[1] >> 3) === ((p.c >> 5) & 31) && (v[2] >> 3) === ((p.c >> 10) & 31)) m++; }
      if (!bestF || m > bestF.exactMatches) bestF = { dx, dy, exactMatches: m };
    }
    frame = { size: [frameBmp.w, frameBmp.h], bestOffset: [bestF.dx, bestF.dy], opaqueTexels: opaqueCells.length, exactMatches: bestF.exactMatches,
      note: 'port output frame; the 320-wide PS1 HUD appears shifted by bestOffset (widescreen centring) if exactMatches is high' };
  }
  fbReport = { drawOrder: "highest sprite id first (AddPrim prepends to OT[0])", buffers, creationOrderControl, frame, overlay: 'hud_overlay_320x240.png', diff: 'hud_diff_vram_framebuffer.png' };
}

// ---------- write layout ----------
const texOut = Object.fromEntries(Object.entries(textures).map(([k, v]) => { const { decoded, ...rest } = v; return [k, rest]; }));
const layout = {
  tool: 'export_hud.mjs', game: 'SLUS-00977', screen: { width: 320, height: 240, origin: 'top-left, y down',
    evidence: 'VRAM snapshot framebuffers 320x240 at (0,0) and (0,256); all HUD coordinates are immediates in func_8007515C' },
  primitive: { type: 'SPRT (GPU 0x64) + DR_MODE', shading: 'raw texture (SetShadeTex 1), no colour modulation', dither: false,
    transparency: 'texel 0x0000 never drawn; when semiTransparent, only texels with STP (bit 15, see *-stp.png) blend',
    abr: ['0.5*B + 0.5*F', 'B + F', 'B - F', 'B + 0.25*F'], order: 'OT[0] via func_800A57B8 (layer 1) => drawn over the 3D scene in creation order' },
  iconNames: ICON_NAMES, karmaOffsets: KARMA_OFFSETS, karmaRing: { origin: [250, 191], size: ringBounds, png: ['textures/karma-ring.png', 'textures/karma-ring-lit.png'], note: 'composite convenience images; every texel has STP and segments are always semi-transparent abr 0 (50/50)' }, elements, textures: texOut,
  notIngameHud: { shld_hud: 'loaded by func_8007515C into D_800F1440 but only drawn by func_80066CDC (pause/inventory screen), which also reads sword level D_800F2786 / armour D_800F2785',
    message: 'D_800F132A > 0: func_80075C2C prints D_800F03F4 with the font at (160,178), colour 0x40,0xC4,0x40 (func_800BA4F4/func_800BA598), see xena-level-scripts.md' },
  ram: ramReport, framebuffer: fbReport,
};
fs.writeFileSync(path.join(out, 'hud_layout.json'), JSON.stringify(layout, null, 1) + '\n');
console.log(JSON.stringify({
  textures: Object.keys(textures).length, vramMismatch: Object.entries(textures).filter(([, t]) => t.vramCheck && t.vramCheck.differentWords).map(([k, t]) => [k, t.vramCheck]),
  ramChecksFailed: ramReport?.checks.filter(c => c.error || c.positionMatches === false || !c.texturePageMatches || !c.clutMatches).map(c => c.id),
  health: ramReport?.healthCheck, karma: ramReport?.karmaCheck, karmaOffsetsMatchRam: ramReport?.karmaOffsetsMatchRam, framebuffer: fbReport,
}, null, 1));
