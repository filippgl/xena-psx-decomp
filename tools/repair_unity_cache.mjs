#!/usr/bin/env node
// Restore only missing files, from the same installed built-in package version.
import fs from 'node:fs';
import path from 'node:path';
const option = process.argv.indexOf('--editor');
const editor = path.resolve(option < 0 ? '../Unity63/Editor/Unity.exe' : process.argv[option + 1]);
const builtins = path.join(path.dirname(editor), 'Data/Resources/PackageManager/BuiltInPackages');
const cache = path.resolve('unity/XenaUnity/Library/PackageCache');
const restored = [];
if (fs.existsSync(cache)) for (const entry of fs.readdirSync(cache)) {
  const target = path.join(cache, entry), json = path.join(target, 'package.json');
  if (!fs.existsSync(json)) continue;
  const pkg = JSON.parse(fs.readFileSync(json)), source = path.resolve(builtins, pkg.name);
  if (!source.startsWith(path.resolve(builtins) + path.sep)) throw Error('Invalid package name');
  if (!fs.existsSync(path.join(source, 'package.json'))) continue;
  if (JSON.parse(fs.readFileSync(path.join(source, 'package.json'))).version !== pkg.version) continue;
  function visit(directory, relative = '') {
    for (const item of fs.readdirSync(directory, { withFileTypes: true })) {
      const rel = path.join(relative, item.name), from = path.join(directory, item.name), to = path.join(target, rel);
      if (item.isSymbolicLink()) throw Error('Unexpected symlink in built-in package');
      if (item.isDirectory()) visit(from, rel);
      else if (!fs.existsSync(to)) {
        fs.mkdirSync(path.dirname(to), { recursive: true }); fs.copyFileSync(from, to);
        restored.push({ package: pkg.name, version: pkg.version, file: rel });
      }
    }
  }
  visit(source);
}
fs.mkdirSync('build/unity-import', { recursive: true });
fs.writeFileSync('build/unity-import/cache-repair.json', JSON.stringify({ restored }, null, 2));
console.log(`Unity cache: restored ${restored.length} missing files from matching installed package versions.`);
