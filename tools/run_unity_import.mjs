#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
const option = process.argv.indexOf('--editor');
const editor = option >= 0 ? process.argv[option + 1] : process.env.UNITY_EDITOR;
if (!editor || !fs.existsSync(editor)) {
  console.error('Unity Editor was not found. Supply --editor "C:/.../Editor/Unity.exe" or set UNITY_EDITOR. No Editor validation has run.');
  process.exit(2);
}
const projectOption = process.argv.indexOf('--project');
const project = path.resolve(projectOption < 0 ? 'unity/XenaUnity' : process.argv[projectOption + 1]), out = path.resolve('build/unity-import');
fs.mkdirSync(out, { recursive: true });
const render = process.argv.includes('--render');
const build = process.argv.includes('--build');
const movementScene = process.argv.includes('--movement-scene');
const movement = movementScene || process.argv.includes('--movement');
if (render && build) throw Error('Run --render and --build separately');
const mode = movementScene ? 'movement-scene' : movement ? 'movement-build' : build ? 'build' : render ? 'render' : 'editor';
const reportPath = path.join(out, `${mode}-verification.json`);
if (fs.existsSync(reportPath)) fs.unlinkSync(reportPath);
const logPath = path.join(out, render || build || movement ? `${mode}-editor.log` : 'editor.log');
const result = spawnSync(editor, ['-batchmode', ...(render ? [] : ['-nographics']), '-projectPath', project,
  '-executeMethod', `Xena.Assets.Editor.${movement ? 'XenaMovementPrototype' : build ? 'XenaPreviewBuild' : render ? 'XenaRenderVerification' : 'XenaImportVerification'}.RunBatch`,
  ...(movement && !movementScene ? ['--build-movement'] : []), '-logFile', logPath],
  { stdio: 'inherit', windowsHide: true });
if (result.error) throw result.error;
if (result.status !== 0) {
  const log = fs.existsSync(logPath) ? fs.readFileSync(logPath, 'utf8') : '';
  console.error(`Unity ${mode} failed (${result.status}); inspect ${logPath}`);
  if (/No valid Unity Editor license found/.test(log)) {
    console.error('Unity has no active Editor license. Sign in and activate your license using Unity Hub, then rerun.');
  } else {
    const errors = [...new Set(log.split(/\r?\n/).filter(line => /error CS\d+|Scripts have compiler errors|Exception:/.test(line)))];
    for (const line of errors.slice(-8)) console.error(line);
    if (/MaterialReferenceChanger/.test(errors.join('\n'))) console.error('URP installation may be incomplete due to Windows path length limits. Use the short Unity63 installation folder.');
  }
  process.exit(result.status || 1);
}
if (!fs.existsSync(reportPath)) throw Error('Unity exited without producing a new verification report');
const report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));
if (report.result !== 'PASS') throw Error('Unity verification did not pass');
console.log(JSON.stringify(report, null, 2));
