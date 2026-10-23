#!/usr/bin/env node
// Rebuild local derived assets without any network calls or new AI generation.
import fs from 'node:fs';
import { spawnSync } from 'node:child_process';
const stages = [
  ['tools/audit_assets.mjs', '--disc', '../Xena Warrior Princess.bin'],
  ['tools/inspect_xena_animation.mjs'],
  ['tools/verify_xena_animation.mjs'],
  ['tools/verify_xena_transforms.mjs'],
  ['tools/export_xena_preview.mjs'],
  ['tools/prepare_xena_upscale.mjs'],
];
if (fs.existsSync('build/xena-upscale/xena-atlas-ai-v1.png')) stages.push(['tools/apply_xena_upscale.mjs']);
stages.push(['tools/export_xena_gltf.mjs']);
if (fs.existsSync('build/xena-upscale/xena-atlas-ai-v1.png')) stages.push(['tools/verify_xena_gltf.mjs']);
if (process.argv.includes('--unity')) stages.push(['tools/export_xena_unity.mjs'], ['tools/prepare_unity_project.mjs'], ['tools/check_unity_assets.mjs']);
if (process.argv.includes('--native-reference')) stages.push(['tools/capture_xena_reference.mjs'], ['tools/verify_xena_reference.mjs']);
if (process.argv.includes('--browser')) stages.push(['tools/check_xena_preview.mjs']);
for (const args of stages) {
  console.log(`\nRunning ${args.join(' ')}`);
  const result = spawnSync(process.execPath, args, { stdio: 'inherit', windowsHide: true });
  if (result.error || result.status !== 0) throw Error(`${args[0]} failed: ${result.error?.message || result.status}`);
}
console.log('\nPASS: local Xena asset pipeline complete. Open build/xena-preview/preview.html');
