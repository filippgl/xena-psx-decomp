#!/usr/bin/env node
// Export the level "script" layer (pickups, signs, generators, level-end NPC/cutscene, messages,
// music set) for Unity. There is no bytecode interpreter: behaviour is hard-coded per class update
// function (class descriptor table 0x800C33E4, +0x1C) and parameterised by PLRS.DAT records.
// Every derived value below cites the MIPS address that consumes it; see docs/xena-level-scripts.md.
//
//   node tools/export_level_scripts.mjs --root <decomp> --out <file.json> [--level 1] [--ram <ram.bin>]
//
// Reads only: <root>/xena.exe, <root>/disc/files/XENA.UDA, <root>/build/asset-audit/manifest.json,
// optional RAM snapshot (default <root>/build/level-assets/01/native-reference/ram_009200.bin).
import fs from 'node:fs';
import path from 'node:path';

const args = Object.fromEntries(process.argv.slice(2).reduce((a, v, i, all) => (v.startsWith('--') ? a.push([v.slice(2), all[i + 1]]) : 0, a), []));
const root = path.resolve(args.root ?? '.');
const level = Number(args.level ?? 1);
const outFile = path.resolve(args.out ?? 'level-scripts.json');
const ramFile = args.ram ?? path.join(root, 'build/level-assets/01/native-reference/ram_009200.bin');
const hex = n => `0x${(n >>> 0).toString(16).toUpperCase().padStart(8, '0')}`;

// ---------- EXE access ----------
const exe = fs.readFileSync(path.join(root, 'xena.exe'));
const at = a => a - 0x80010000 + 0x800;
const u32 = a => exe.readUInt32LE(at(a)), i32 = a => exe.readInt32LE(at(a));
const u16 = a => exe.readUInt16LE(at(a)), i16 = a => exe.readInt16LE(at(a));
const u8 = a => exe[at(a)], s8 = a => exe.readInt8(at(a));
const inExe = p => p >= 0x80010000 && p < 0x80010000 + exe.length - 0x800;
const cstr = (a, max = 96) => { const o = at(a), e = exe.indexOf(0, o); return exe.subarray(o, Math.min(e, o + max)); };

// ---------- Disc archive ----------
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'build/asset-audit/manifest.json')));
const uda = fs.readFileSync(path.join(root, 'disc/files/XENA.UDA'));
function discFile(name) {
  const e = manifest.entries.find(x => x.candidates.some(c => c.name.toUpperCase() === name.toUpperCase()));
  if (!e?.inArchive) throw Error(`missing ${name}`);
  const o = (e.lba - manifest.summary.archiveLba) * 2048;
  return uda.subarray(o, o + e.size);
}
const lv = String(level).padStart(2, '0');

// ---------- PLRS.DAT (same grammar as tools/lib/xena_level.mjs readPlrs; loader 0x80063340..0x800638F4) ----------
function readPlrs(bytes) {
  const out = [];
  for (const [i, raw] of bytes.toString('ascii').split(/\r\n|\n|\r/).entries()) {
    const line = raw.trim();
    if (!line) continue;
    if (line === 'END') break;
    const [name, ...t] = line.split(/\s+/), v = t.map(Number);
    if (v.length !== 23 || v.some(n => !Number.isInteger(n))) throw Error(`bad PLRS line ${i + 1}`);
    out.push({ index: out.length, sourceLine: i + 1, name, position: v.slice(0, 3), rotationTokens: v.slice(3, 6),
      field7: v[6], vectors: [v.slice(7, 10), v.slice(10, 13), v.slice(13, 16)], params: v.slice(16) });
  }
  return out;
}
// LEVEL.BIN placement table (count +0x100, offset +0x104, 40-byte records; docs/level-resource-audit.md)
function readLevelBin(b) {
  const count = b.readUInt32LE(0x100), off = b.readUInt32LE(0x104);
  return Array.from({ length: count }, (_, k) => { const o = off + k * 40;
    return { index: k, name: b.subarray(o, o + 16).toString('ascii').replace(/\0.*$/s, ''), position: [16, 20, 24].map(n => b.readInt32LE(o + n)) }; });
}

// ---------- Class descriptors 0x800C33E4, 0x44 bytes, 249 entries (names end at index 0xF8 sign_pt) ----------
const CLASS_TABLE = 0x800C33E4, CLASS_COUNT = 0xF9;
const classes = Array.from({ length: CLASS_COUNT }, (_, index) => {
  const d = CLASS_TABLE + index * 0x44;
  return { index, descriptor: hex(d), name: cstr(u32(d), 16).toString('latin1'), flags: u32(d + 4), health: i32(d + 8),
    animOwner: u32(d + 0x10), update: u32(d + 0x1C), radius: i16(d + 0x28), height: i16(d + 0x2A),
    range2C: u16(d + 0x2C), byte40: u8(d + 0x40) };
});
const classByName = new Map(classes.map(c => [c.name, c]));

// ---------- Messages: pointer table 0x800CCCD0 (byte index D_800ED74A), up to 8 line pointers each,
// NULL -> default 0x800CCA08. Drawn by func_8006E508; first lines centred at y = 0x72 - 8*lines.
// The disc carries a Russian translation written with remapped glyphs. Letter mapping A..Z is
// confirmed from the font glyphs in VRAM (960,256) of the native snapshot; the symbol/digit
// substitutions ($ & + # @ 0 3 4 6) are INFERRED from glyph shapes and word context.
const GLYPH = { A: 'А', B: 'В', C: 'С', D: 'Д', E: 'Е', F: 'Ы', G: 'Ш', H: 'Н', I: 'І', J: 'П', K: 'К', L: 'Л', M: 'М', N: 'И', O: 'Ф',
  P: 'Р', Q: 'Э', R: 'Я', S: 'Ь', T: 'Т', U: 'Ю', V: 'Ц', W: 'Ж', X: 'Х', Y: 'У', Z: 'Г' };
const SYMBOL = { '$': 'о', '&': 'б', '+': 'л', '#': 'ч', '@': 'з', '0': 'О', '3': 'З', '4': 'Ч', '6': 'Б' };
const translit = s => [...s].map(ch => {
  if (SYMBOL[ch]) return SYMBOL[ch];
  const up = ch.toUpperCase();
  if (GLYPH[up]) return ch === up ? GLYPH[up] : GLYPH[up].toLowerCase();
  return ch;
}).join('');
const MSG_TABLE = 0x800CCCD0, MSG_DEFAULT = 0x800CCA08;
function message(index) {
  let p = u32(MSG_TABLE + index * 4); const fallback = !p; if (!p) p = MSG_DEFAULT;
  const lines = [];
  for (let k = 0; k < 8; k++) { const q = u32(p + 4 * k); if (!q || !inExe(q)) break; lines.push(cstr(q)); }
  return { index, table: hex(MSG_TABLE + index * 4), linesPointer: hex(p), defaultUsed: fallback,
    raw: lines.map(b => b.toString('latin1')), rawHex: lines.map(b => b.toString('hex')),
    transliterationInferred: lines.map(b => translit(b.toString('latin1')).replace(/[\x00-\x1f]/g, c => `{icon ${c.charCodeAt(0)}}`)) };
}

// ---------- Level configuration 0x800C18E4, 40 bytes per level (loader 0x8006289C) ----------
const cfgAddr = 0x800C18E4 + level * 40;
const soundSet = s8(cfgAddr + 4);              // lb 0x4($s4) -> func_80086DCC  (0x80062B1C)
const setRec = u32(0x800EE550 + soundSet * 4);  // func_80086DCC 0x80086E14
const musicIndex = i16(setRec), banks = [];
for (let k = 1; k < 8; k++) { const b = i16(setRec + 2 * k); if (b < 0) break; banks.push(cstr(u32(0x800EE478 + b * 4), 16).toString('latin1')); }
const levelConfig = {
  address: hex(cfgAddr), bytes: exe.subarray(at(cfgAddr), at(cfgAddr) + 40).toString('hex'),
  maxLiveEnemies: { value: s8(cfgAddr + 3), evidence: 'lb D_800C18E7+level*40 vs live count D_800F2774 (0x80032D84, 0x80032EB4, 0x8003350C)' },
  partitionRadius: { value: i16(cfgAddr + 6), evidence: 'docs/level-resource-audit.md (0x800BEBC4)' },
  titleMessage: { pointer: hex(u32(cfgAddr + 0x14)), raw: inExe(u32(cfgAddr + 0x14)) ? cstr(u32(cfgAddr + 0x14)).toString('latin1') : null,
    transliterationInferred: inExe(u32(cfgAddr + 0x14)) ? translit(cstr(u32(cfgAddr + 0x14)).toString('latin1')) : null, status: 'INFERRED: level title string' },
  sound: { set: soundSet, setRecord: hex(setRec), music: musicIndex >= 0 ? cstr(u32(0x800CC37C + musicIndex * 8), 16).toString('latin1') : null,
    musicParam: musicIndex >= 0 ? u32(0x800CC37C + musicIndex * 8 + 4) : null, banks: ['core', ...banks],
    files: 'DATA\\SND\\%s.vh/.vb/.sep (0x800EE58C..0x800EE5AC)', evidence: 'func_80086DCC (SsEnd, D_800EE550[set], music table 0x800CC37C, bank names 0x800EE478)' },
};

// ---------- Behaviour by update function ----------
const BEHAVIOUR = {
  0x80032C18: 'generator', 0x8003FBA0: 'pickup', 0x800406C8: 'sign', 0x80074FB8: 'levelEndNpc', 0x800367BC: 'player',
  0x8001B12C: 'ambientFrog', 0x80019FE0: 'ambientButterfly', 0x80019950: 'ambientBird', 0x8002745C: 'exit', 0x8001B460: 'enemyAi',
};
// Generator param4 -> actor+0x144 flags (loader 0x80063A24..0x80063BAC; base bit 0x8 at 0x800639B0).
const GEN_FLAG_MAP = [[0x2, 0x80], [0x1, 0x100], [0x8, 0x200], [0x10, 0x400], [0x4, 0x800], [0x20, 0x1000], [0x40, 0x2000],
  [0x80, 0x4000], [0x100, 0x80000], [0x200, 0x200000], [0x400, 0x800000]];
const genFlags = p4 => GEN_FLAG_MAP.reduce((f, [bit, flag]) => (p4 & bit ? f | flag : f), 0x8);
const GEN_FLAG_MEANING = {
  0x80: 'camera line test disabled (func_80032B6C 0x80032B84)',
  0x100: 'camera line test from spawn point to camera D_800F10EC+0x1C (func_80032B6C -> func_800A2E98); spawn refused when it returns 0',
  0x200: 'spawned actor +0x60 = generator waypoint 0 (patrol target) (0x800339B4)',
  0x400: 'segment trigger: Xena within 800 of segment waypoint1->waypoint2, projection inside segment box (0x80033268..0x800334C4)',
  0x800: 'also spawns a companion actor (peasantw/peasantm/seer by level) (0x80033BF8..0x80033DC8)',
  0x1000: 'spawned +0x6C = generator position, +0x8C = param5 (0x800339F8)',
  0x2000: 'requires |Xena.y - generator.y| <= 1000 (0x800331C8..0x8003320C)',
  0x4000: 'UNKNOWN (not read by generator code)',
  0x80000: 'spawned +0x30 = 1 (0x80033B0C)',
  0x200000: 'global spawn budget D_800F0A22 decremented (0x800337C0)',
  0x800000: 'flag copied to spawned actor (0x80033AE8)',
};
const PICKUP_EFFECT = {
  health2: { counter: 'D_800F277E (health potions) += 1', firstTimeBit: 0x1, immediateUse: 'if Xena.health + max*30/100 < max (0x80040468..0x800404AC): func_800410C0 heals 30% of max (500 -> +150, capped), companion D_800EE178 +20%, potions -= 1, D_800F0D74 += 1', address: '0x8004044C' },
  continue: { counter: 'D_800F277C += 1; respawn point D_800F0D20 = Xena position', firstTimeBit: 0x800, address: '0x800404C4' },
  scroll: { counter: 'D_800F277A += 1 (capped at 2)', message: 'func_8006E41C: index = level*2 + actor+0x10 - 1; actor+0x10 = descriptor health + param0 (loader 0x80063E04)', address: '0x80040514' },
  key1: { counter: 'D_800F277D |= 1', firstTimeBit: 0x1000, address: '0x80040554' },
  staff: { counter: 'none in pickup switch (Xena code 0x80039034 sets D_800F0D18=100)', address: '0x800406A0' },
  up_armor: { counter: 'D_800F2785 += 1 if < 4, D_800F0CB0 += 1', firstTimeBit: 0x4000, refusedWhenFull: true, address: '0x80040578' },
  up_sword: { counter: 'D_800F2786 += 1 if < 4, D_800F0CB1 += 1', firstTimeBit: 0x8000, refusedWhenFull: true, address: '0x800405BC' },
  p_storm: { counter: 'D_800F277F += 1', firstTimeBit: 0x2, address: '0x800403D4' }, p_suprc: { counter: 'D_800F2780 += 1', firstTimeBit: 0x4, address: '0x800403EC' },
  p_pblade: { counter: 'D_800F2781 += 1', firstTimeBit: 0x8, address: '0x80040404' }, p_mskin: { counter: 'D_800F2782 += 1', firstTimeBit: 0x10, address: '0x8004041C' },
  p_frost: { counter: 'D_800F2783 += 1', firstTimeBit: 0x20, address: '0x80040434' },
};
// func_8006E468(bit): once per bit (D_800F1190 seen-mask) show message 0x36 + log2(bit).
const firstTimeMessage = bit => 0x36 + Math.log2(bit);

const plrs = readPlrs(discFile(`DATA\\LEVELS\\${lv}\\PLRS.DAT`));
const usedMessages = new Map();
const useMessage = (i, why) => { if (!usedMessages.has(i)) usedMessages.set(i, { ...message(i), usedBy: [] }); usedMessages.get(i).usedBy.push(why); return i; };

const actors = plrs.map(r => {
  const cls = classByName.get(r.name);
  const behaviour = cls ? (BEHAVIOUR[cls.update] ?? 'unaudited') : 'unknownClass';
  const a = { index: r.index, sourceLine: r.sourceLine, name: r.name, classIndex: cls?.index ?? null, update: cls ? hex(cls.update) : null,
    behaviour, position: r.position, yaw: r.rotationTokens[0], field7: r.field7, params: r.params, vectors: r.vectors };
  if (!cls) return a;
  a.classFlags = hex(cls.flags);
  if (behaviour === 'pickup') {
    const eff = PICKUP_EFFECT[r.name] ?? null;
    a.pickup = { radius: cls.radius, height: cls.height,
      collect: 'Xena contact actor (+0xF0) with class flags&8 while Xena state +0xD0 in {0..5,0x14..0x18,0x19..0x1F,0x37}: Xena enters state 0x37, at animation frame +0xE6==11 sets pickup +0x10C=1 (0x80038F28..0x80039030); pickup then despawns (+0x94=1), plays sound 15 twice (func_80084A34), D_800F2788=1 (0x80040364..0x800403A0)',
      effect: eff };
    if (eff?.firstTimeBit) a.pickup.firstTimeMessage = useMessage(firstTimeMessage(eff.firstTimeBit), `${r.name}#${r.index} first pickup`);
    if (r.name === 'scroll') a.pickup.message = useMessage(level * 2 + (cls.health + r.params[0]) - 1, `scroll#${r.index}`);
    a.initialHealthField = cls.health + r.params[0];
  } else if (behaviour === 'sign') {
    a.sign = { touchRadius: cls.radius, height: cls.height, rearmDistance: cls.range2C, messageParam: r.params[0],
      initialHealthField: cls.health + r.params[0], logic: 'Xena contact (class 0xF7/0xF8, 0x80038F08..0x80038F24) sets +0x10C; if cooldown +0x118==0 and (D_800F1438==0 or param==14) show message param+0x46 (func_8006E4CC), cooldown 60 frames; cooldown re-armed to 60 while dist(Xena) < 400 (func_800406C8)',
      message: useMessage(r.params[0] + 0x46, `sign#${r.index}`) };
  } else if (behaviour === 'generator') {
    const [p0, p1, p2, p3, p4, p5, p6] = r.params, flags = genFlags(p4);
    const spawnClass = classByName.get(r.name.slice(2));
    const base = cls.range2C + 100 * p2;
    a.generator = {
      spawnClass: r.name.slice(2), spawnClassIndex: spawnClass?.index ?? null, spawnUpdate: spawnClass ? hex(spawnClass.update) : null,
      countsAsEnemy: spawnClass ? Boolean(spawnClass.flags & 0x10) : null,
      remaining: p1, infinite: p1 >= 100, aggression: p0, healthDelta: p3, blockPercent: p6, param5: p5, flagsParam: p4,
      flags: hex(flags), flagMeanings: Object.fromEntries(Object.entries(GEN_FLAG_MEANING).filter(([f]) => flags & Number(f)).map(([f, m]) => [hex(Number(f)), m])),
      trigger: flags & 0x400
        ? { type: 'segment', from: r.vectors[1], to: r.vectors[2], lateral: 800 }
        : { type: 'radius', centre: r.position, radius: base, formula: `desc+0x2C (${cls.range2C}) + 100*param2 (${p2})` },
      pendingRadius: flags & 0x400 ? 'avg(dist(wp1,gen),dist(wp2,gen)) + 2000' : base + 2000,
      lineTestToCamera: Boolean(flags & 0x100) && !(flags & 0x80),
      spawnPosition: r.position, spawnYaw: (r.rotationTokens[0] & 0xFFF) ? r.rotationTokens[0] : 'face Xena (func_80032748)',
      patrolTarget: flags & 0x200 ? r.vectors[0] : null,
      liveLimitBypassed: [0x2F, 0x45].includes(cls.index),
    };
  } else if (behaviour === 'levelEndNpc') {
    a.levelEnd = { triggerDistance: 1450, metric: 'max(|dx|,|dz|) + 3/8*min (0x80074FD0..0x80075064)', extraCondition: 'Xena +0xF8 == 0 (meaning UNKNOWN)',
      start: 'func_8007E410(level) -> cutscene for levels 1/4/0x11/0x19 (0x8007E984)', end: 'after keyframe pair index >= 10: func_8006C2D0 sets game state D_800F2648 = 13 (same call as the exit class 0x80027640)' };
  }
  return a;
});

// ---------- Level 1 Siras cutscene: camera keyframes D_800C804C (28 bytes: u16 frame, pad, int32 dx,dy,dz, angles a,b,c) ----------
let cutscene = null;
if (level === 1) {
  const keys = Array.from({ length: 10 }, (_, k) => { const p = 0x800C804C + k * 28;
    return { frame: u16(p), offset: [i32(p + 4), i32(p + 8), i32(p + 12)], angles: { cam2A_neg: i32(p + 16), cam28: i32(p + 20), cam2C: i32(p + 24) } }; });
  cutscene = { table: '0x800C804C', relativeTo: 'siras actor (class 0xAC, func_80072E60(0,0xAC))', keys,
    segments: Array.from({ length: 5 }, (_, s) => ({ from: 2 * s, to: 2 * s + 1, frames: keys[2 * s + 1].frame - keys[2 * s].frame + 1 })),
    interpolation: 'linear: value = a + frameInSegment*(b-a)/frames; camera D_800F10F4 +0x1C/+0x20/+0x24 = siras pos + offset; +0x2A = -lerp(+16), +0x28 = lerp(+20), +0x2C = lerp(+24), angles &0xFFF (0x8007E780..0x8007E980)',
    xenaPlacement: { offsetX: 0x82, offsetZ: -0x14A, yOnGround: 'func_8003423C', yaw: 0xAE3, address: '0x8007EAB0..0x8007EB94' },
    skippable: 'pad bit 2 skips only when level != 1 (0x8007E440..0x8007E464) — INFERRED: level 1 cutscene not skippable',
    status: 'VERIFIED table/addresses; camera field roles INFERRED' };
}

// ---------- Static placements of interest (LEVEL.BIN). No class instance: these never run code. ----------
const placements = readLevelBin(discFile(`DATA\\LEVELS\\${lv}\\LEVEL.BIN`));
const staticObjects = placements.filter(p => /door|exit|gate|switch/i.test(p.name)).map(p => ({ ...p,
  note: 'LEVEL.BIN TMD object; the EXE has no "2doors" class string and PLRS has no door record -> static geometry (doors never open in level 01)' }));

// ---------- RAM validation (native snapshot) ----------
let validation = null;
if (fs.existsSync(ramFile)) {
  const ram = fs.readFileSync(ramFile), r32 = a => ram.readUInt32LE(a & 0x1FFFFF), r16 = a => ram.readInt16LE(a & 0x1FFFFF),
    ri32 = a => ram.readInt32LE(a & 0x1FFFFF), r8 = a => ram[a & 0x1FFFFF];
  const checks = [], add = (name, ok, detail) => checks.push({ name, ok, detail });
  add('level D_800F2772', r8(0x800F2772) === level, r8(0x800F2772));
  add('sound set D_800F158C', r16(0x800F158C) === soundSet, r16(0x800F158C));
  // message table resident and identical
  const msgEqual = ram.subarray(MSG_TABLE & 0x1FFFFF, (MSG_TABLE & 0x1FFFFF) + 0x74 * 4).equals(exe.subarray(at(MSG_TABLE), at(MSG_TABLE) + 0x74 * 4));
  add('message pointer table 0x800CCCD0 RAM==EXE', msgEqual, '0x74 entries');
  const camEqual = ram.subarray(0x800C804C & 0x1FFFFF, (0x800C804C & 0x1FFFFF) + 280).equals(exe.subarray(at(0x800C804C), at(0x800C804C) + 280));
  add('cutscene table 0x800C804C RAM==EXE', camEqual, '280 bytes');
  // walk actor list D_800F1320 (+0x8 next)
  const live = [];
  for (let p = r32(0x800F1320), n = 0; p && n < 400; p = r32(p + 8), n++) live.push(p);
  const used = new Set();
  let liveEnemies = 0;
  // func_8006FF38 assigns Xena/companion to D_800EE174/D_800EE178 (0x80070414/0x80070430) before the flag-0x10 count.
  const player = r32(0x800EE174), companion = r32(0x800EE178);
  for (const p of live) { const c = classes[r32(p)]; if (c && (c.flags & 0x10) && p !== player && p !== companion) liveEnemies++; }
  add('live enemy count D_800F2774 == live actors with class flag 0x10', r8(0x800F2774) === liveEnemies, { ram: r8(0x800F2774), counted: liveEnemies });
  for (const a of actors) {
    const p = live.find(q => !used.has(q) && r32(q) === a.classIndex && [0, 1, 2].every(k => ri32(q + 0x98 + 4 * k) === a.position[k]));
    if (!p) { add(`actor ${a.index} ${a.name} found`, false, 'no live actor with class + origin +0x98'); continue; }
    used.add(p);
    const params = Array.from({ length: 7 }, (_, k) => r16(p + 0x84 + 2 * k));
    const diff = params.map((v, k) => (v === ((a.params[k] << 16) >> 16) ? null : k)).filter(k => k !== null);
    const detail = { address: hex(p), params };
    let ok = diff.every(k => a.behaviour === 'generator' && k === 1); // +0x86 is the live remaining counter
    if (a.behaviour === 'generator') {
      const flags = r32(p + 0x144), want = Number(a.generator.flags);
      detail.flags = hex(flags); detail.runtimeExtraBits = hex(flags & ~want);
      ok = ok && (flags & want) === want && (flags & ~want & ~0x41) === 0;
      detail.spawnPos = [0, 1, 2].map(k => ri32(p + 0xAC + 4 * k));
      ok = ok && detail.spawnPos.every((v, k) => v === a.position[k]);
      detail.remainingRam = r16(p + 0x86);
    }
    if (a.behaviour === 'pickup' || a.behaviour === 'sign') { detail.plus10 = r16(p + 0x10); ok = ok && r16(p + 0x10) === classes[a.classIndex].health + a.params[0]; }
    add(`actor ${a.index} ${a.name}`, ok, detail);
  }
  validation = { ramFile, note: 'native port snapshot (build/port/xena-console.exe frame 9200), not PS1 hardware', passed: checks.filter(c => c.ok).length, failed: checks.filter(c => !c.ok).length, checks };
}

const out = {
  formatVersion: 1, level, generatedFrom: { exe: 'xena.exe', plrs: `DATA\\LEVELS\\${lv}\\PLRS.DAT`, levelBin: `DATA\\LEVELS\\${lv}\\LEVEL.BIN` },
  units: '400 units = 1 m (project convention); angles 4096 = 360 deg; frames = game update steps',
  interpreter: 'none found: behaviour is per-class update code (descriptor +0x1C) plus level-number checks on D_800F2772',
  levelConfig,
  globals: {
    generatorPeriod: 'each generator runs once per 16 frames, slot index & 15 == frame & 15 (0x80032CB4..0x80032CFC)',
    spawnOccupancyRadius: 350, spawnRateLimit: 'after 3 spawns, next spawn not before frame + (D_800F0A58 ? 480*D_800F0A58+301 : 1) (0x80033724..0x800337BC)',
    liveEnemyCounter: 'D_800F2774: +1 on create for class flag 0x10 (func_8006FF38 0x8007043C), -1 on free (func_80070724 0x800709A0); kills D_800F2775',
    messageBox: 'func_800672F8 only while D_800F2648 == 12 and no cutscene; func_8006E508 draws; dismiss when D_800F10F0 delay elapsed and pad D_800F16D8 & 0x800',
  },
  actors, cutscene, staticObjects,
  messages: [...usedMessages.values()].sort((x, y) => x.index - y.index),
  validation,
};
fs.mkdirSync(path.dirname(outFile), { recursive: true });
fs.writeFileSync(outFile, JSON.stringify(out, null, 1));
console.log(`wrote ${outFile}: ${actors.length} actors, ${out.messages.length} messages` + (validation ? `, RAM checks ${validation.passed} pass / ${validation.failed} fail` : ''));
if (validation?.failed) for (const c of validation.checks.filter(c => !c.ok)) console.log('FAIL', c.name, JSON.stringify(c.detail));
