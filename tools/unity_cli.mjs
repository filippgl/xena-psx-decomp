// UTF-8 JSON wrapper: Windows PowerShell 5's native > redirection writes UTF-16.
import fs from 'node:fs';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
const args=process.argv.slice(2);
function take(flag,fallback){const at=args.indexOf(flag);if(at<0)return fallback;const value=args[at+1];if(!value)throw Error(`Missing ${flag}`);args.splice(at,2);return value;}
const output=take('--out',null),project=take('--project','unity/XenaLightingVerification');
const cli=process.env.UNITY_CLI||path.join(process.env.LOCALAPPDATA,'Unity/bin/unity.exe');
if(!fs.existsSync(cli))throw Error('Unity CLI unavailable; set UNITY_CLI to its executable');
const command=args.shift(),common=['--format','json'];
if(['command','status','list','recompile'].includes(command))common.push('--project-path',path.resolve(project));
if(command==='close')common.push(path.resolve(project));
if(command==='command')common.push('--caller','plugin','--skill','unity-cli');
const result=spawnSync(cli,[command,...common,...args],{encoding:'utf8',windowsHide:true,maxBuffer:12*1024*1024});
if(result.error)throw result.error;
if(output)fs.writeFileSync(output,result.stdout);
if(result.status!==0){process.stderr.write(result.stderr||result.stdout);process.exit(result.status||1);}
if(!result.stdout.trim())throw Error('CLI returned no JSON; inspect command/version support');
const report=JSON.parse(result.stdout);
if(report.notifications?.length)console.log(JSON.stringify({notifications:report.notifications}));
console.log(JSON.stringify(output?{success:report.success,command:report.command,output,...(report.success===false?{errors:report.errors}:{})}:report,null,2));
if(report.success===false||report.data?.result?.success===false)process.exit(1);
