import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
import {readPng} from './lib/png.mjs';
const project=path.resolve(process.argv[2]||'unity/XenaLightingVerification');
const cli=process.env.UNITY_CLI||path.join(process.env.LOCALAPPDATA,'Unity/bin/unity.exe');
const out=path.resolve('build/unity-import');
let index=0,started=false;
const report={result:'FAIL',project,checks:[]};
function run(...args){
  const command=args.shift();
  const child=spawnSync(cli,[command,'--project-path',project,'--format','json',...(command==='command'?['--caller','plugin','--skill','unity-cli']:[]),...args],{encoding:'utf8',windowsHide:true,maxBuffer:12*1024*1024});
  fs.writeFileSync(path.join(out,`pipeline-step-${String(++index).padStart(2,'0')}.json`),child.stdout||child.stderr||'');
  assert.equal(child.status,0,child.stderr||child.stdout);
  const envelope=JSON.parse(child.stdout);assert.equal(envelope.success,true,JSON.stringify(envelope.errors));
  const result=envelope.data?.result??envelope.data;
  assert.notEqual(result?.success,false,JSON.stringify(result));
  return result;
}
try{
  const state=run('command','editor','status');
  assert.equal(state.playMode,'stopped','Do not interrupt an existing Play session');
  assert.equal(path.resolve(state.projectPath).toLowerCase(),project.toLowerCase());
  report.checks.push('explicit project routing and ready editor');
  const version=run('command','script','eval','return UnityEngine.Application.unityVersion;');
  assert.equal(version.result,'6000.3.25f1');report.checks.push('live C# evaluation');
  run('command','scene','open','Assets/Scenes/Level01MovementPrototype.unity');
  run('command','editor','set_auto_tick','--','--persist','false');
  run('command','editor.playmode','set_run_in_background');
  run('command','editor.playmode','start');started=true;
  run('status','--until-ready');
  run('command','script','eval','UnityEngine.Object.FindFirstObjectByType<Xena.Assets.PrototypeInput>().UseDevices(UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>(), UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>(), UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>());');
  const progress=run('command','job','wait','--','--condition',JSON.stringify({member:'UnityEngine.Time.frameCount',op:'changed'}),'--timeout','10');
  assert.equal(progress.met,true,JSON.stringify(progress));report.progress=progress;report.checks.push('live Play mode actually advances');
  const scene=run('command','script','eval','return new { scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, ticks=UnityEngine.Object.FindFirstObjectByType<Xena.Assets.PrototypeMotor>().Ticks, clips=UnityEngine.Object.FindFirstObjectByType<Xena.Assets.PrototypeMotor>().GetComponentInChildren<Xena.Assets.XenaAssetPreview>().ClipCount, dummy=UnityEngine.Object.FindFirstObjectByType<Xena.Assets.PrototypePracticeTarget>().Health };');
  report.live=scene.result;assert.equal(report.live.clips,81);assert(report.live.ticks>0);report.checks.push('original player and practice target live');
  const chakram=run('command','script','eval','var asset=UnityEngine.Resources.Load<UnityEngine.GameObject>("Chakram/Chakram"); var meshes=asset.GetComponentsInChildren<UnityEngine.MeshFilter>(); return new { state=UnityEngine.Object.FindFirstObjectByType<Xena.Assets.PrototypeMotor>().GetComponent<Xena.Assets.PrototypeChakram>().State.ToString(), meshes=meshes.Length, triangles=System.Linq.Enumerable.Sum(meshes, m=>m.sharedMesh.triangles.Length/3) };');
  report.chakram=chakram.result;assert.equal(report.chakram.state,'Ready');assert.equal(report.chakram.meshes,2);assert.equal(report.chakram.triangles,46);report.checks.push('standalone original chakram imported and ability initialized');
  const capture=run('command','capture','game_view','--','--width','1280','--height','720','--source','camera','--save_path','Assets/Generated/PipelineVerification/live.png');
  const file=path.resolve(project,capture.savedPath);assert(file.toLowerCase().startsWith(project.toLowerCase()+path.sep));assert(fs.existsSync(file));
  const picture=readPng(fs.readFileSync(file));assert.equal(picture.width,1280);assert.equal(picture.height,720);
  fs.copyFileSync(file,path.join(out,'pipeline-live.png'));report.checks.push('camera image captured through Pipeline');
  report.console=run('command','console','get','--','--level','error','--tail','20');
  const entries=report.console.entries??report.console.logs??[];
  assert.equal(entries.length,0,JSON.stringify(entries));report.checks.push('no editor console errors');
  report.result='PASS';
}catch(error){report.error=error.stack;}
finally{
  if(started){try{run('command','editor.playmode','stop');run('status','--until-ready');run('command','editor.playmode','set_run_in_background','--','--enable','false');run('command','editor','set_auto_tick','--','--enable','false','--persist','false');}catch(error){report.result='FAIL';report.cleanupError=error.stack;}}
  fs.writeFileSync(path.join(out,'pipeline-verification.json'),JSON.stringify(report,null,2)+'\n');
}
console.log(JSON.stringify(report,null,2));if(report.result!=='PASS')process.exit(1);
