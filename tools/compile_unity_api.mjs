#!/usr/bin/env node
// Compile with actual Unity assemblies and Editor-generated compiler settings.
// This does not launch the licensed Editor or validate imported assets/rendering.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
const editorRoot = path.resolve('../Unity63/Editor');
const dotnet = path.join(editorRoot, 'Data/NetCoreRuntime/dotnet.exe');
const compiler = path.join(editorRoot, 'Data/DotNetSdkRoslyn/csc.dll');
const project = path.resolve('unity/XenaUnity'), artifacts = path.join(project, 'Library/Bee/artifacts');
const dags = fs.readdirSync(artifacts).filter(name => fs.existsSync(path.join(artifacts, name, 'Xena.Assets.Editor.rsp')));
if (dags.length !== 1) throw Error('Expected exactly one Editor-generated Xena compiler configuration; import the project first');
const dag = path.join(artifacts, dags[0]);
const entries = ['Unity.RenderPipelines.Universal.Editor', 'Xena.Assets', 'Xena.Assets.Editor'];
const out = path.resolve('build/unity-import'); fs.mkdirSync(out, { recursive: true });
const log = [], commands = [];
const urpCache = fs.readdirSync(path.join(project, 'Library/PackageCache')).filter(name => name.startsWith('com.unity.render-pipelines.universal@'));
if (urpCache.length !== 1) throw Error('Expected one resolved URP package');
for (const name of entries) {
  const rsp = path.join(dag, `${name}.rsp`), content = fs.readFileSync(rsp, 'utf8');
  const args = ['exec', compiler, `@${rsp}`, `@${rsp}2`];
  const extra = name === 'Unity.RenderPipelines.Universal.Editor' ? [
    'ReadonlyMaterialConverter.MaterialReferenceBuilder.cs', 'ReadonlyMaterialConverter.MaterialReferenceChanger.cs'
  ].map(leaf => `Library/PackageCache/${urpCache[0]}/Editor/Tools/Converters/ReadonlyMaterialConverter/${leaf}`) :
    fs.readdirSync(`unity/Packages/com.xena.original-assets/${name === 'Xena.Assets' ? 'Runtime' : 'Editor'}`)
      .filter(file => file.endsWith('.cs'))
      .map(file => `../Packages/com.xena.original-assets/${name === 'Xena.Assets' ? 'Runtime' : 'Editor'}/${file}`);
  for (const file of extra) if (!content.includes(path.basename(file))) args.push(file);
  const result = spawnSync(dotnet, args, { cwd: project, encoding: 'utf8', windowsHide: true });
  log.push(name, result.stdout || '', result.stderr || '');
  fs.writeFileSync(path.join(out, 'api-compilation.log'), log.join('\n'));
  if (result.error || result.status !== 0) throw Error(`${name} compile failed: ${result.error?.message || result.stderr || result.stdout}`);
  commands.push({ assembly: name, compilerArguments: args });
}
const report = { result: 'PASS', editorRoot, assemblies: entries, commands, unityApiCompiled: true,
  editorImportVerified: false, shaderCompiled: false,
  note: 'Roslyn compilation using actual Unity assemblies and previously Editor-generated response files. Not a running Editor test.' };
fs.writeFileSync(path.join(out, 'api-compilation.json'), JSON.stringify(report, null, 2));
console.log(JSON.stringify({ result: report.result, assemblies: entries, unityApiCompiled: true, editorImportVerified: false }, null, 2));
