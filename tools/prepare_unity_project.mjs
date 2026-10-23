#!/usr/bin/env node
// Stable source GUIDs; preserve existing .meta files if the Editor has made them.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
const root = 'unity/Packages/com.xena.original-assets';
let written = 0;
function visit(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const filename = path.join(directory, entry.name).replaceAll('\\', '/');
    if (entry.name.endsWith('.meta') || entry.name === 'package.json') continue;
    const guid = crypto.createHash('md5').update(`xena-original-assets-v1:${filename}`).digest('hex');
    if (!fs.existsSync(`${filename}.meta`)) {
      fs.writeFileSync(`${filename}.meta`, `fileFormatVersion: 2\nguid: ${guid}\n${entry.isDirectory() ? 'folderAsset: yes\n' : ''}`);
      written++;
    }
    if (entry.isDirectory()) visit(filename);
  }
}
visit(root);
for (const entry of ['Assets/Imported', 'Assets/Generated', 'Assets/Scenes'])
  fs.mkdirSync(`unity/XenaUnity/${entry}`, { recursive: true });
const manifestPath = 'unity/XenaUnity/Packages/manifest.json';
const manifest = JSON.parse(fs.readFileSync(manifestPath));
const local = manifest.dependencies['com.xena.original-assets'];
const resolved = path.resolve(path.dirname(manifestPath), local.slice('file:'.length));
if (!fs.existsSync(path.join(resolved, 'package.json'))) throw Error('Local UPM package path is broken');
console.log(`Unity project staged; ${written} source GUID files created. Local package resolved: ${resolved}`);
