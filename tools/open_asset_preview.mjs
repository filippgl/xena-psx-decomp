#!/usr/bin/env node
// Explicit browser launch: Windows' .html association may be absent or an editor.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { spawn } from 'node:child_process';
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const level = process.argv.includes('--level');
const folder = path.join(repo, 'build', level ? 'level-assets/01' : 'xena-preview');
const required = level ? ['preview.html', 'level-data.js', 'xena-renderer.js', 'xena-water.js'] : ['preview.html', 'xena-data.js', 'xena-renderer.js'];
try {
  for (const name of required) {
    const filename = path.join(folder, name);
    if (!fs.existsSync(filename)) throw Error(`Missing preview file: ${filename}. Run tools/build_${level ? 'level' : 'xena'}_assets.mjs from decomp.`);
  }
  const candidates = [
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
    'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
    path.join(process.env.LOCALAPPDATA || '', 'Google/Chrome/Application/chrome.exe'),
    'C:/Program Files/Mozilla Firefox/firefox.exe',
  ];
  const browser = candidates.find(filename => fs.existsSync(filename));
  if (!browser) throw Error('No supported browser found. Open the preview.html printed below in a browser.');
  const url = pathToFileURL(path.join(folder, 'preview.html')).href;
  console.log(`Browser: ${browser}\nPreview: ${url}`);
  if (!process.argv.includes('--check')) {
    const child = spawn(browser, [url], { detached: true, stdio: 'ignore', windowsHide: false });
    await new Promise((resolve, reject) => { child.once('spawn', resolve); child.once('error', reject); });
    child.unref();
  }
} catch (error) {
  console.error(error.message);
  console.error(`Preview folder: ${folder}`);
  process.exitCode = 1;
}
