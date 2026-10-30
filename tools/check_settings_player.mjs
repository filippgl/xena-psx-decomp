import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import {spawn} from 'node:child_process';
const out=path.resolve('build/unity-import'),settings=path.join(out,'settings-test.json');
const executable=path.resolve('build/unity-movement/XenaMovementPrototype.exe');
const gameCode=path.resolve('build/unity-movement/XenaMovementPrototype_Data/Managed/Xena.Assets.dll');
const hash=()=>crypto.createHash('sha256').update(fs.readFileSync(gameCode)).digest('hex');
const testedBuild={executable,gameCode,modified:fs.statSync(gameCode).mtime.toISOString(),sha256:hash()};
if(fs.existsSync(settings))fs.unlinkSync(settings);
const report={result:'PASS',checks:[],phases:[],testedBuild};
for(const phase of ['write','read','malformed']){
  if(phase==='malformed')fs.writeFileSync(settings,'{invalid-json');
  const file=path.join(out,`settings-${phase}-verification.json`);if(fs.existsSync(file))fs.unlinkSync(file);
  const child=spawn(executable,['--mute','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',path.join(out,`settings-${phase}-player.log`),'--settings-file',settings,'--settings-smoke',phase,out],{windowsHide:false,stdio:'ignore'});
  const code=await new Promise((resolve,reject)=>{const timer=setTimeout(()=>{child.kill();reject(Error('Settings phase timed out: '+phase));},30000);child.once('error',error=>{clearTimeout(timer);reject(error);});child.once('exit',value=>{clearTimeout(timer);resolve(value);});});
  const phaseReport=fs.existsSync(file)?JSON.parse(fs.readFileSync(file)):null;
  console.log(JSON.stringify(phaseReport,null,2));assert.equal(code,0,'Settings player failed: '+phase);assert.equal(phaseReport?.result,'PASS');
  report.phases.push(phase);report.checks.push(...phaseReport.checks);
}
assert.equal(hash(),testedBuild.sha256);assert.equal(fs.readFileSync(settings,'utf8'),'{invalid-json');
fs.writeFileSync(path.join(out,'settings-player-verification.json'),JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report,null,2));
