import fs from 'node:fs';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
const routes={movement:[[0x14,0x15],[0x15,0x17],[0x1a,0x1f]],defense:[[0x80,0x28],[0x80,0x29],[0x80,0x2a]],chakram:[[0,0x64],[0x64,0],[0,0x65],[0x65,0]],
  running:[[0x1a,0x3c],[0x3c,0],[0x1a,0x3e],[0x3e,0],[0x1a,0x31],[0x31,0]],
  spin:[[0,0x4c],[0x4c,0x4d],[0x4d,0x4e],[0x4e,0]],
  'directional-classic':[[0,0x35],[0x35,0],[0,0x51],[0x51,0],[0,0x61],[0x61,0]],
  guardattacks:[[0x80,0x5c],[0x5c,0],[0x80,0x46],[0x46,0],[0x4c,0x4d],[0x4d,0x4e],[0x4e,0]]};
const name=process.argv[2];if(!routes[name])throw Error('Choose '+Object.keys(routes).join(', '));
const base=`build/action-reference-${name}`,capture=JSON.parse(fs.readFileSync(`${base}/capture.json`)),clips=new Map();
let components=0;const transitions=[],samples=[];
for(const tick of capture.frames){
  const ram=fs.readFileSync(`${base}/ram_${String(tick).padStart(6,'0')}.bin`),object=capture.object;
  const slot=ram.readUInt32LE(object+0xd0),frame=ram.readUInt16LE(object+0xe6)-1;
  if(!clips.has(slot))clips.set(slot,JSON.parse(fs.readFileSync(`build/animation-audit/slot-${slot.toString(16).padStart(2,'0')}.json`)));
  const pose=clips.get(slot).frames[frame];assert(pose,`Missing pose ${slot}/${frame}`);
  const pointer=at=>ram.readUInt32LE(at)&0x1fffff,context=pointer(object+0xa8),parts=pointer(context+0x14),root=pointer(context+0x18);
  pose.parts.forEach((p,i)=>{if(p.parent===-1)return;for(let axis=0;axis<3;axis++){
    assert.equal(ram.readInt32LE(parts+i*20+axis*4),p.translation[axis],`${tick}/${i} translation`);
    assert.equal(ram.readInt16LE(parts+i*20+12+axis*2),p.rotation[axis],`${tick}/${i} rotation`);components+=2;
  }});
  const previous=samples.at(-1);
  if(previous&&tick===previous.tick+2&&previous.slot!==slot)transitions.push({from:previous.slot,to:slot,last:previous.frame,first:frame,tick});
  samples.push({tick,slot,frame,heading:ram.readInt16LE(root+14),position:[0,4,8].map(n=>ram.readInt32LE(root+n)),sha256:crypto.createHash('sha256').update(ram).digest('hex')});
}
for(const [from,to] of routes[name])assert(transitions.some(t=>t.from===from&&t.to===to),`Missing ${from}/${to}`);
if(name==='movement'){
  const run=samples.filter(s=>s.slot===0x1a);
  assert.equal(new Set(run.map(s=>s.frame)).size,20);
  assert(run.some((s,i)=>i>0&&run[i-1].frame===19&&s.frame===0),'No run wrap');
}
let jumpTable;
let spinCycles;
if(name==='spin'){
  spinCycles=[[9000,9128],[9200,9416],[9500,9656]].map(([first,last])=>{
    const rows=samples.filter(s=>s.tick>=first&&s.tick<=last&&s.slot===0x4d);
    return 1+rows.filter((s,i)=>i>0&&s.frame<rows[i-1].frame).length;
  });
  assert.deepEqual(spinCycles,[2,2,4],'Tap/hold/repeated spin cycles differ');
}
if(name==='running'){
  const ram=fs.readFileSync(`${base}/ram_${String(capture.frames[0]).padStart(6,'0')}.bin`);
  jumpTable=[0,2,4,6].map(n=>ram.readUInt16LE(0xc1474+n));
  assert.deepEqual(jumpTable,[6,20,34,85],'Unexpected original31 jump phase table');
}
if(name==='directional-classic'){
  const ram=fs.readFileSync(`${base}/ram_${String(capture.frames[0]).padStart(6,'0')}.bin`);
  jumpTable=[0,2,4,6].map(n=>ram.readUInt16LE(0xc146c+n));assert.deepEqual(jumpTable,[4,13,22,40]);
  assert.equal(clips.get(0x35).frameCount,32);
  const flip=samples.filter(s=>s.slot===0x35);
  assert(flip.every(s=>s.heading===flip[0].heading),'Backflip changed native facing');
  const angle=flip[0].heading*Math.PI/2048,delta=flip.at(-1).position.map((v,i)=>v-flip[0].position[i]);
  assert(delta[0]*-Math.sin(angle)+delta[2]*-Math.cos(angle)<0,'Backflip did not move backward');
}
const report={result:'PASS',name,sourceExecutable:capture.executable,modified:capture.modified,snapshots:samples.length,components,
  clips:[...clips.values()].map(c=>({slot:c.slot,frames:c.frameCount})),transitions,jumpTable,spinCycles,
  scope:'Native port pose comparison, excluding world root. Movement displacement/timing is reference data, not complete PS1 behavior parity.',samples};
fs.writeFileSync(`${base}/verification.json`,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({...report,samples:undefined},null,2));
