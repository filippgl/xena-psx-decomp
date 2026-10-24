import fs from 'node:fs';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
const scenarios={
  movement:{end:9380,intervals:[[9000,9098],[9160,9340]],script:'9000:UP:60,9160:UP+L1:120'},
  defense:{end:9490,intervals:[[9000,9100],[9160,9260],[9320,9420]],script:'9000:CIRCLE+LEFT:12,9160:CIRCLE+RIGHT:12,9320:CIRCLE+DOWN:12'},
  running:{end:9640,intervals:[[9000,9140],[9200,9320],[9400,9620]],script:'9000:UP+L1:40,9020:UP+L1+SQUARE:8,9200:UP+L1:40,9220:UP+L1+CROSS:8,9400:UP+L1:40,9420:UP+L1+TRIANGLE:8'},
  guardattacks:{end:9580,intervals:[[9000,9160],[9200,9360],[9400,9540]],script:'9000:CIRCLE:40,9010:CIRCLE+SQUARE:8,9200:CIRCLE:40,9210:CIRCLE+CROSS:8,9400:CROSS+SQUARE:8'},
  chakram:{end:9380,intervals:[[9000,9340]],script:'9000:R1:260,9020:R1+CROSS:8,9070:R1+RIGHT:40'}
};
const name=process.argv[2];if(!scenarios[name])throw Error('Choose '+Object.keys(scenarios).join(', '));
const scenario=scenarios[name],directory=path.resolve(`build/action-reference-${name}`);
fs.mkdirSync(directory,{recursive:true});
const frames=scenario.intervals.flatMap(([first,last])=>Array.from({length:(last-first)/2+1},(_,i)=>first+i*2));
if(frames.length>256)throw Error('Native RAM capture limit');
const executable=path.resolve('build/port/xena-console.exe');
const script='7400:CROSS,7700:CROSS,8000:CROSS,8300:CROSS,'+scenario.script;
const result=spawnSync(executable,['--headless','--frames',String(scenario.end),'--ramdump',directory,frames.join(','),'--memcard',path.join(directory,'card'),
  '--render-scale','1','--opt','renderer=software','--script',script,path.resolve('../Xena Warrior Princess.bin')],
  {encoding:'utf8',timeout:180000,windowsHide:true,maxBuffer:8*1024*1024});
const log=(result.stdout||'')+(result.stderr||'');fs.writeFileSync(path.join(directory,'runtime.log'),log);
if(result.error||result.status!==0||!log.includes(`reached frame ${scenario.end}`))throw Error('Native capture failed');
const match=[...log.matchAll(/player object ([\da-f]+)/gi)].at(-1);if(!match)throw Error('No player');
const object=parseInt(match[1],16)&0x1fffff;
const samples=frames.map(tick=>{const b=fs.readFileSync(path.join(directory,`ram_${String(tick).padStart(6,'0')}.bin`));
  return{tick,slot:b.readUInt32LE(object+0xd0),frame:b.readUInt16LE(object+0xe6)-1};});
const transitions=samples.filter((s,i)=>!i||s.slot!==samples[i-1].slot).map(s=>({...s,hex:s.slot.toString(16)}));
fs.writeFileSync(path.join(directory,'capture.json'),JSON.stringify({executable,modified:fs.statSync(executable).mtime.toISOString(),script,object,frames,samples,transitions},null,2)+'\n');
console.log(JSON.stringify({name,snapshots:frames.length,transitions},null,2));
