import fs from 'node:fs';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
const directory=path.resolve('build/combat-reference');
fs.mkdirSync(directory,{recursive:true});
const frames=Array.from({length:251},(_,i)=>9000+i*2);
const script='7400:CROSS,7700:CROSS,8000:CROSS,8300:CROSS,9000:CROSS:4,9014:CROSS:4,9030:CROSS:4,9120:SQUARE:4,9134:SQUARE:4,9152:SQUARE:4,9240:CROSS:4,9254:SQUARE:4,9274:CROSS:4,9380:CIRCLE:60';
const executable=path.resolve('build/port/xena-console.exe');
const result=spawnSync(executable,['--headless','--frames','9500','--ramdump',directory,frames.join(','),'--memcard',path.join(directory,'card'),
  '--render-scale','1','--opt','renderer=software','--script',script,path.resolve('../Xena Warrior Princess.bin')],{encoding:'utf8',timeout:180000,windowsHide:true,maxBuffer:8*1024*1024});
const log=(result.stdout||'')+(result.stderr||'');
fs.writeFileSync(path.join(directory,'runtime.log'),log);
if(result.error||result.status!==0||!log.includes('reached frame 9500')) throw Error(result.error?.message||'Native combat capture failed');
const match=[...log.matchAll(/player object ([\da-f]+)/gi)].at(-1);
if(!match)throw Error('No player object recorded');
const object=parseInt(match[1],16)&0x1fffff;
const metadata={executable,modified:fs.statSync(executable).mtime.toISOString(),script,object,frames,
  scope:'Controlled native port reference, not console/emulator. No native source changes.'};
fs.writeFileSync(path.join(directory,'capture.json'),JSON.stringify(metadata,null,2)+'\n');
const states=[];
for(const frame of frames){const ram=fs.readFileSync(path.join(directory,`ram_${String(frame).padStart(6,'0')}.bin`));const state=ram.readUInt32LE(object+0xd0);if(states.at(-1)?.state!==state) states.push({at:frame,state,hex:state.toString(16)});}
console.log(JSON.stringify({modified:metadata.modified,states},null,2));
