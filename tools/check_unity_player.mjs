#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
const executable = path.resolve('build/unity-preview/XenaAssetPreview.exe');
const output = path.resolve('build/unity-import');
assert(fs.existsSync(executable), 'Build the standalone preview with run_unity_import.mjs --build first');
const reportPath = path.join(output, 'player-verification.json');
if (fs.existsSync(reportPath)) fs.unlinkSync(reportPath);
const child = spawn(executable, ['--mute','-screen-fullscreen', '0', '-screen-width', '960', '-screen-height', '720',
  '-logFile', path.join(output, 'player.log'), '--xena-smoke', output], { windowsHide: false, stdio: 'ignore' });
const code = await new Promise((resolve, reject) => {
  const timeout = setTimeout(() => { child.kill(); reject(Error('Player verification timed out after 60s; inspect build/unity-import/player.log')); }, 60000);
  child.once('error', error => { clearTimeout(timeout); reject(error); });
  child.once('exit', code => { clearTimeout(timeout); resolve(code); });
});
assert.equal(code, 0, 'Standalone player exited with an error; inspect build/unity-import/player.log');
assert(fs.existsSync(reportPath), 'Player did not write a fresh report');
const report = JSON.parse(fs.readFileSync(reportPath));
assert.equal(report.result, 'PASS');
for (const name of ['player-original.png', 'player-upscale.png']) {
  const png = fs.readFileSync(path.join(output, name));
  assert.equal(png.subarray(0, 8).toString('hex'), '89504e470d0a1a0a');
  assert(png.length > 10000, `Empty screenshot: ${name}`);
}
console.log(JSON.stringify(report, null, 2));
