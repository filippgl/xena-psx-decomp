#!/usr/bin/env node
// Read-only audit of the SLUS-00977 resource index. No game assets are exported.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const args = process.argv.slice(2);
const option = (name, fallback) => {
  const i = args.indexOf(name);
  if (i < 0) return fallback;
  if (!args[i + 1] || args[i + 1].startsWith('--')) throw Error(`Missing ${name} value`);
  return args[i + 1];
};
if (args.includes('--help')) {
  console.log('From decomp/: node tools/audit_assets.mjs [--exe xena.exe] [--archive disc/files/XENA.UDA] [--listing disc/filelist.txt] [--disc ../Xena Warrior Princess.bin] [--out build/asset-audit]');
  process.exit(0);
}
const exePath = option('--exe', 'xena.exe');
const archivePath = option('--archive', 'disc/files/XENA.UDA');
const exe = fs.readFileSync(exePath);
const archive = fs.readFileSync(archivePath);
const listing = fs.readFileSync(option('--listing', 'disc/filelist.txt'), 'utf8');
const archiveRecord = listing.match(/^\s*(\d+)\s+(\d+)\s+\S+\s+XENA\.UDA\s*$/mi);
if (!archiveRecord) throw Error('XENA.UDA extent not found in ISO listing');
const archiveLba = Number(archiveRecord[1]);
if (Number(archiveRecord[2]) !== archive.length) throw Error('Archive size differs from ISO listing');
if (exe.subarray(0, 8).toString() !== 'PS-X EXE') throw Error('Expected original PS-X executable');
const fileOffset = va => va - 0x80010000 + 0x800;
if (exe.readUInt32LE(0x18) !== 0x80010000 || exe.length < fileOffset(0x800DA6C4) + 1024)
  throw Error('Unsupported executable layout (expected SLUS-00977)');
const weights = Array.from({ length: 256 }, (_, i) => exe.readInt32LE(fileOffset(0x800DA6C4) + i * 4));
const hashName = name => {
  const text = (`\\${name.replace(/^\\+/, '')};1`).toLowerCase();
  if (text.length > weights.length) throw Error('Resource name exceeds hash weights');
  let hash = 0;
  for (let i = 0; i < text.length; i++) hash = (hash + text.charCodeAt(i) * weights[i]) | 0;
  return hash;
};
const candidates = new Map();
function candidate(name, source) {
  const normalized = name.trim().replace(/\//g, '\\').replace(/^\\+/, '').replace(/;1$/, '').toUpperCase();
  if (!/^[A-Z0-9_\\. -]+\.[A-Z0-9]+$/.test(normalized) || normalized.includes('%')) return;
  if (!candidates.has(normalized)) candidates.set(normalized, new Set());
  candidates.get(normalized).add(source);
}
for (const s of exe.toString('latin1').match(/[\x20-\x7e]{4,}/g) || []) {
  if (/^(DATA|MEDIA)\\/i.test(s)) candidate(s, 'executable-string');
}
for (const match of listing.matchAll(/^\s*\d+\s+\d+\s+\S+\s+(?:RAW\s+)?(\S+)\s*$/gm))
  candidate(match[1], 'iso-listing');
// These historical traces supply candidate names only; an index hit is required.
for (const name of fs.existsSync('build') ? fs.readdirSync('build').filter(n => /^files_.*\.txt$/.test(n)).sort() : []) {
  for (const match of fs.readFileSync(path.join('build', name), 'utf8').matchAll(/^file: (.+)$/gm))
    candidate(match[1], `build/${name}`);
}
const levelReference = 'build/level-assets/01/native-reference/reference.json';
if (fs.existsSync(levelReference)) {
  for (const name of JSON.parse(fs.readFileSync(levelReference)).requestedResources || [])
    candidate(name, 'native-level-01-file-lookup');
}
for (let n = 1; n <= 30; n++) {
  for (const leaf of ['LEVEL.TMD', 'LEVEL.TIM', 'LEVEL.STM', 'ENVIRO.BIN',
    'LEVEL.BIN', 'LEVEL.LGT', 'LEVEL.MIP', 'LEVEL.CLS', 'PLRS.DAT', 'FLEVEL.TMD', 'HORIZ.STM'])
    candidate(`DATA\\LEVELS\\${String(n).padStart(2, '0')}\\${leaf}`, 'generated-level-probe');
}
for (const stem of ['XENA', 'CHAKRAM']) {
  for (const ext of ['TMD', 'TIM', 'STM', 'SMA', 'SMI']) candidate(`DATA\\LEVELS\\${stem}.${ext}`, 'character-probe');
}
const namesByHash = new Map();
for (const [name, sources] of candidates) {
  const hash = hashName(name);
  if (!namesByHash.has(hash)) namesByHash.set(hash, []);
  namesByHash.get(hash).push({ name, sources: [...sources].sort() });
}
const entries = [];
for (let i = 0; i < 0x52A; i++) {
  const offset = fileOffset(0x800D5424) + i * 16;
  const hash = exe.readInt32LE(offset);
  const lba = exe.readUInt32LE(offset + 4);
  const size = exe.readUInt32LE(offset + 8);
  const start = (lba - archiveLba) * 2048;
  const inArchive = start >= 0 && start + size <= archive.length;
  const bytes = inArchive ? archive.subarray(start, start + size) : Buffer.alloc(0);
  const entry = { index: i, hash, lba, size, extra: exe.readUInt32LE(offset + 12), inArchive,
    candidates: namesByHash.get(hash) || [], headerHex: bytes.subarray(0, 32).toString('hex') };
  if (bytes.length) entry.sha256 = crypto.createHash('sha256').update(bytes).digest('hex');
  if (bytes.length >= 12 && bytes.readUInt32LE(0) === 0x41) {
    const count = bytes.readUInt32LE(8);
    entry.tmdHeaderProbe = { flags: bytes.readUInt32LE(4), objectCount: count,
      objectTableFits: count > 0 && 12 + count * 28 <= bytes.length };
    if (entry.tmdHeaderProbe.objectTableFits && entry.tmdHeaderProbe.flags === 0) {
      entry.tmdHeaderProbe.objects = [];
      for (let obj = 0; obj < count; obj++) {
        const at = 12 + obj * 28;
        const vertexOffset = 12 + bytes.readUInt32LE(at);
        const vertices = bytes.readUInt32LE(at + 4);
        const normalOffset = 12 + bytes.readUInt32LE(at + 8);
        const normals = bytes.readUInt32LE(at + 12);
        let cursor = 12 + bytes.readUInt32LE(at + 16);
        const primitives = bytes.readUInt32LE(at + 20);
        let packetsRead = 0;
        while (packetsRead < primitives && cursor + 4 <= bytes.length) {
          const end = cursor + 4 + bytes[cursor + 1] * 4;
          if (end > bytes.length) break;
          cursor = end;
          packetsRead++;
        }
        entry.tmdHeaderProbe.objects.push({ vertices, normals, primitives,
          vertexRangeFits: vertexOffset + vertices * 8 <= bytes.length,
          normalRangeFits: normalOffset + normals * 8 <= bytes.length,
          primitivePacketsFit: packetsRead === primitives });
      }
    }
  }
  if (bytes.length >= 8 && bytes.readUInt32LE(0) === 0x10)
    entry.timHeaderProbe = { flags: bytes.readUInt32LE(4) };
  // func_80069C5C, 0x8006A094..0x8006A10C: 146 offsets relative to file+4.
  if (entry.candidates.some(c => c.name.endsWith('.SMA')) && bytes.length >= 4 + 146 * 4) {
    const slots = Array.from({ length: 146 }, (_, slot) => {
      const relativeOffset = bytes.readUInt32LE(4 + slot * 4);
      const offset = relativeOffset ? relativeOffset + 4 : null;
      const headerFits = offset !== null && offset >= 4 + 146 * 4 && offset + 6 <= bytes.length;
      return { slot, relativeOffset, offset, headerFits,
        headerU16: headerFits ? [0, 2, 4].map(n => bytes.readUInt16LE(offset + n)) : null };
    });
    entry.smaTableProbe = { firstWord: bytes.readUInt32LE(0),
      presentSlots: slots.filter(s => s.offset !== null).length,
      invalidOffsets: slots.filter(s => s.offset !== null && !s.headerFits).length, slots };
  }
  entries.push(entry);
}
let discComparison = null;
const discPath = option('--disc', null);
if (discPath) {
  const fd = fs.openSync(discPath, 'r');
  let compared = 0;
  try {
    for (let offset = 0; offset < archive.length; offset += 2048) {
      const count = Math.min(2048, archive.length - offset);
      const sector = Buffer.alloc(2352);
      if (fs.readSync(fd, sector, 0, 2352, (archiveLba + offset / 2048) * 2352) !== 2352)
        throw Error('Truncated raw disc');
      if (sector[15] !== 2 || sector[18] & 0x20) throw Error('Expected MODE2 Form1 archive sector');
      if (!sector.subarray(24, 24 + count).equals(archive.subarray(offset, offset + count)))
        throw Error(`Disc/archive mismatch at byte ${offset}`);
      compared += count;
    }
  } finally { fs.closeSync(fd); }
  discComparison = { comparedBytes: compared, identical: true };
}
const missing = [...candidates.keys()].filter(n => !entries.some(e => e.hash === hashName(n))).sort();
const summary = { indexEntries: entries.length, archiveLba, archiveBytes: archive.length,
  exeSha256: crypto.createHash('sha256').update(exe).digest('hex'),
  archiveSha256: crypto.createHash('sha256').update(archive).digest('hex'),
  entriesInArchive: entries.filter(e => e.inArchive).length,
  entriesWithCandidateNames: entries.filter(e => e.candidates.length).length,
  tmdHeaderProbes: entries.filter(e => e.tmdHeaderProbe).length,
  timHeaderProbes: entries.filter(e => e.timHeaderProbe).length, discComparison };
const out = option('--out', 'build/asset-audit');
fs.mkdirSync(out, { recursive: true });
fs.writeFileSync(path.join(out, 'manifest.json'), JSON.stringify({ summary,
  caveat: 'Names are hash-matched candidates, not archive filenames. Header signatures do not prove full format compatibility.',
  entries, missingCandidates: missing }, null, 2) + '\n');
console.log(JSON.stringify(summary, null, 2));
console.log(`Metadata written to ${out}/manifest.json`);
