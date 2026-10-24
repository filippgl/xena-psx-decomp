// Reject stale green reports after either code OR Resources assets have changed.
import fs from 'node:fs';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const out='build/unity-import',buildFile=`${out}/movement-build-verification.json`;
const build=JSON.parse(fs.readFileSync(buildFile));assert.equal(build.result,'PASS');
const hash=file=>crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const code='build/unity-movement/XenaMovementPrototype_Data/Managed/Xena.Assets.dll';
const resource='build/unity-movement/XenaMovementPrototype_Data/resources.assets';
const codeHash=hash(code),buildTime=fs.statSync(buildFile).mtimeMs;
assert(fs.statSync(code).mtimeMs<=buildTime,'Game code newer than build report');
assert(fs.statSync(resource).mtimeMs<=buildTime,'Resources newer than build report');
const suites=['movement','combat','settings','chakram'].map(suite=>{
  const file=`${out}/${suite}-player-verification.json`,report=JSON.parse(fs.readFileSync(file));
  assert.equal(report.result,'PASS',suite);assert.equal(report.testedBuild.sha256,codeHash,`${suite} code hash`);
  assert(fs.statSync(file).mtimeMs>=buildTime,`${suite} report predates latest asset build`);
  return{suite,checks:report.checks.length};
});
const logs=['movement-player','combat-player','chakram-player','settings-write-player','settings-read-player','settings-malformed-player'];
for(const name of logs){
  const file=`${out}/${name}.log`,log=fs.readFileSync(file,'utf8');
  assert(fs.statSync(file).mtimeMs>=buildTime,`${name} log predates build`);
  assert(!/Exception:|error CS\d+/.test(log),`Unhandled exception in ${name}`);
}
const report={result:'PASS',total:suites.reduce((n,s)=>n+s.checks,0),suites,logsChecked:logs,
  gameCode:{modified:fs.statSync(code).mtime.toISOString(),sha256:codeHash},
  resource:{modified:fs.statSync(resource).mtime.toISOString(),sha256:hash(resource)},build};
fs.writeFileSync(`${out}/actions-final-verification.json`,JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report,null,2));
