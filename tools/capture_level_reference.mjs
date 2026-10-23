#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
const out = path.resolve('build/level-assets/01/native-reference');
fs.mkdirSync(out, { recursive: true });
const executable = path.resolve('build/port/xena-console.exe');
const logPath = path.join(out, 'runtime.log'), logFile = fs.openSync(logPath, 'w');
let result;
try {
  result = spawnSync(executable, ['--headless', '--verbose', '--frames', '9200', '--shots', '9200', out,
    '--shots-from', '9200', '--ramdump', out, '9200', '--memcard', path.join(out, 'card'),
    '--render-scale', '1', '--opt', 'renderer=software', '--poke', '7420:800EE200:1',
    '--script', '7400:CROSS,7700:CROSS,8000:CROSS,8300:CROSS', path.resolve('../Xena Warrior Princess.bin')],
    { stdio: ['ignore', logFile, logFile], windowsHide: true, timeout: 120000 });
} finally { fs.closeSync(logFile); }
if (result.error) throw result.error;
if (result.status !== 0) throw Error(`Native reference failed (${result.status}); inspect ${logPath}`);
const log = fs.readFileSync(logPath, 'utf8');
if (!log.includes('reached frame 9200')) throw Error('Native reference did not reach the requested frame');
const filenames = [...new Set([...log.matchAll(/file: ([^\r\n]+)/g)].map(m => m[1].trim()))];
const levelFiles = filenames.filter(name => name.includes('\\01\\'));
if (!levelFiles.some(name => name.toUpperCase().includes('LEVEL.TMD'))) throw Error('Reference did not request level 01 geometry');
const metadata = { executable, modified: fs.statSync(executable).mtime.toISOString(), frame: 9200,
  requestedLevel: 1, levelFiles, requestedResources: filenames,
  note: 'Existing native port reference, not original hardware or emulator. Level request is observed in file lookup logs.' };
fs.writeFileSync(path.join(out, 'reference.json'), JSON.stringify(metadata, null, 2));
console.log(JSON.stringify({ executable, modified: metadata.modified, levelFiles, out }, null, 2));
