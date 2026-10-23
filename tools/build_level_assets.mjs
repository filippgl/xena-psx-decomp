#!/usr/bin/env node
import fs from 'node:fs';
import { spawnSync } from 'node:child_process';
const stages = [];
if (process.argv.includes('--native-reference') || !fs.existsSync('build/level-assets/01/native-reference/reference.json'))
  stages.push(['tools/capture_level_reference.mjs']);
stages.push(['tools/audit_assets.mjs', '--disc', '../Xena Warrior Princess.bin'],
  ['tools/inspect_level_collision.mjs'], ['tools/build_level_terrain.mjs'], ['tools/verify_level_terrain.mjs'], ['tools/verify_level_grid.mjs'], ['tools/inspect_level_lighting.mjs'], ['tools/inspect_level_placements.mjs'],
  ['tools/inspect_level_flags.mjs'], ['tools/inspect_level_water.mjs'],
  ['tools/inspect_level_spawns.mjs'],
  ['tools/inspect_level_assets.mjs', '--level', '1'], ['tools/verify_level_assets.mjs', '--level', '1'],
  ['tools/verify_level_assembly.mjs'], ['tools/export_xena_unity.mjs', '--level-01'],
  ['tools/prepare_unity_project.mjs'], ['tools/check_unity_assets.mjs']);
if (process.argv.includes('--api')) stages.push(['tools/compile_unity_api.mjs']);
if (process.argv.includes('--browser')) stages.push(['tools/check_xena_preview.mjs', '--level']);
for (const args of stages) {
  console.log(`Running ${args.join(' ')}`);
  const result = spawnSync(process.execPath, args, { stdio: 'inherit', windowsHide: true });
  if (result.error || result.status !== 0) throw Error(`${args[0]} failed: ${result.error?.message || result.status}`);
}
console.log('PASS: first-level resource pipeline complete. Editor import is a separate check.');
