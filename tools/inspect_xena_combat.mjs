import fs from 'node:fs';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
const mixed=process.argv.includes('--mixed');
const base=mixed?'build/combat-mixed-reference':'build/combat-reference', capture=JSON.parse(fs.readFileSync(`${base}/capture.json`));
const clips=new Map(), samples=[], transitions=[];
let comparedValues=0, previous;
for(const tick of capture.frames){
  const file=`${base}/ram_${String(tick).padStart(6,'0')}.bin`,ram=fs.readFileSync(file),object=capture.object;
  assert.equal(ram.length,0x200000);
  const slot=ram.readUInt32LE(object+0xd0),frame=ram.readUInt16LE(object+0xe6)-1;
  if(!clips.has(slot))clips.set(slot,JSON.parse(fs.readFileSync(`build/animation-audit/slot-${slot.toString(16).padStart(2,'0')}.json`)));
  const pose=clips.get(slot).frames[frame]; assert(pose,`Missing pose ${slot}/${frame}`);
  const pointer=at=>ram.readUInt32LE(at)&0x1fffff,context=pointer(object+0xa8),transforms=pointer(context+0x14);
  pose.parts.forEach((part,index)=>{
    if(part.parent===-1)return;
    for(let axis=0;axis<3;axis++){
      assert.equal(ram.readInt32LE(transforms+index*20+axis*4),part.translation[axis],`${tick} part${index} translation${axis}`);
      assert.equal(ram.readInt16LE(transforms+index*20+12+axis*2),part.rotation[axis],`${tick} part${index} rotation${axis}`);
      comparedValues+=2;
    }
  });
  if(previous&&tick===previous.tick+2&&previous.slot!==slot)transitions.push({from:previous.slot,to:slot,lastFrame:previous.frame,firstFrame:frame,tick});
  const sample={tick,slot,frame,pendingE2:ram.readUInt16LE(object+0xe2),sha256:crypto.createHash('sha256').update(ram).digest('hex')};
  samples.push(sample);previous=sample;
}
for(const [from,to] of (mixed
  ? [[0x51,0x57],[0x57,0x62],[0x62,0x4f],[0x62,0x55],[0x61,0x56],[0x56,0x52],[0x52,0x53]]
  : [[0x51,0x52],[0x52,0x3a],[0x61,0x62],[0x62,0x5a],[0,0x80],[0x80,0]]))
  assert(transitions.some(item=>item.from===from&&item.to===to),`Missing native transition ${from}/${to}`);
if(mixed)for(const [from,to,lastFrame,firstFrame] of [
  [0x51,0x57,9,0],[0x57,0x62,5,5],[0x62,0x4f,30,0],
  [0x62,0x55,18,0],[0x61,0x56,10,0],[0x56,0x52,2,2],[0x52,0x53,9,0]])
  assert(transitions.some(t=>t.from===from&&t.to===to&&t.lastFrame===lastFrame&&t.firstFrame===firstFrame),`Unexpected mixed handoff ${from}/${to}`);
const report={result:'PASS',nativeExecutable:capture.executable,modified:capture.modified,samples:samples.length,comparedValues,
  clips:[...clips.values()].map(clip=>({slot:clip.slot,frames:clip.frameCount})),transitions,
  limitations:'Only observed transition frames, not earliest/legal input windows. Source context24/26 values are motion metadata, not proven hit flags. Original combat volumes at actor class+3C remain unaudited; practice range/damage must be marked provisional.',
  samplesDetail:samples};
fs.writeFileSync(`${base}/verification.json`,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({...report,samplesDetail:undefined},null,2));
