// Verify original class+3C sample points, not complete hit volumes or damage rules.
import fs from 'node:fs';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import {PoseOracle} from './lib/mips_pose_oracle.mjs';

const destination='build/combat-geometry';
fs.mkdirSync(destination,{recursive:true});
const exe=fs.readFileSync('xena.exe'), oracle=new PoseOracle(exe,Buffer.alloc(0));
let snapshots=0,points=0,components=0,instructions=0,bufferPoints=0;
const sources=[],definitions=new Map();
const previousBufferDifferences=[];
for(const base of ['build/combat-reference','build/combat-mixed-reference']){
  const capture=JSON.parse(fs.readFileSync(`${base}/capture.json`));
  let previous;
  for(const tick of capture.frames){
    const ram=fs.readFileSync(`${base}/ram_${String(tick).padStart(6,'0')}.bin`);
    assert.equal(ram.length,0x200000);
    const pointer=at=>{const raw=ram.readUInt32LE(at);assert(raw>=0x80000000&&raw<0x80200000);return raw&0x1fffff;};
    const object=capture.object,context=pointer(object+0xa8),type=pointer(object+12);
    const table=pointer(type+0x3c),matrices=pointer(context+0x2c),current=pointer(object+0x12c);
    const world=[0,4,8].map(n=>ram.readInt32LE(pointer(object+0x5c)+n));
    ram.copy(oracle.memory);
    const anchors=[],sampledPoints=[];
    let index=0;
    for(;index<64;index++){
      const at=table+index*16,part=ram.readInt16LE(at);
      if(part===-1)break;
      assert(part>=0&&part<33,'Invalid Xena part');
      const offset=[4,8,12].map(n=>ram.readInt32LE(at+n));
      // 8006A790 has a separate mesh-vertex reference mode; none is observed here.
      assert.notEqual(offset[0],80000,'Mesh-referenced point requires separate audit');
      const matrix=matrices+part*32;
      const transformed=Array.from({length:3},(_,row)=>Math.floor(
        offset.reduce((sum,value,col)=>sum+ram.readInt16LE(matrix+(row*3+col)*2)*value,0)/4096));
      const expected=transformed.map((value,axis)=>ram.readInt32LE(matrix+20+axis*4)-value);
      const actual=[0,4,8].map(n=>ram.readInt32LE(current+index*12+n));
      sampledPoints.push(actual);
      if(previous&&tick===previous.tick+2){
        // Captures are after root motion; anchor buffers were generated before it.
        // Use the independently captured preceding root, never a fitted offset.
        const beforeMotion=expected.map((value,axis)=>value+previous.world[axis]-world[axis]);
        assert.deepEqual(beforeMotion,actual,`${base} tick${tick} point${index}: native pre-motion buffer mismatch`);
        const old=[0,4,8].map(n=>ram.readInt32LE(pointer(object+0x130)+index*12+n));
        if(old.some((value,axis)=>value!==previous.points[index][axis]))
          previousBufferDifferences.push({base,tick,point:index,actual:old,preceding:previous.points[index]});
        bufferPoints++;
      }
      const out=0x801ff100;
      const result=oracle.execute(0x8008e40c,[0x80000000+matrix,0x80000000+at+4,out]);
      const original=[0,4,8].map(n=>oracle.read(out+n,4,true));
      assert.deepEqual(original,transformed,`${base} tick${tick} point${index}: MIPS rotation mismatch`);
      instructions+=result.steps;points++;components+=3;
      anchors.push({part,unknownHalfword:ram.readUInt16LE(at+2),offset});
    }
    assert.equal(index,9,'Unexpected Xena anchor count');
    definitions.set(JSON.stringify(anchors),anchors);
    snapshots++;
    previous={tick,world,points:sampledPoints};
  }
  sources.push({capture:`${base}/capture.json`,executable:capture.executable,modified:capture.modified,
    snapshots:capture.frames.length});
}
const report={result:'PASS',snapshots,points,components,bufferPoints,instructions,sources,
  originalExeSha256:crypto.createHash('sha256').update(exe).digest('hex'),
  definitions:[...definitions.values()],
  previousBufferAudit:{result:previousBufferDifferences.length?'UNRESOLVED':'MATCH',differences:previousBufferDifferences},
  scope:'Nine class+3C anchors match original MIPS8008E40C; 4320 current-buffer points match using the independently captured preceding root position. Five first/discontinuous snapshots have no current-buffer comparison. Previous-buffer differences are diagnostic and excluded from PASS. Attack activation, swept-volume radii, damage, victim filtering and full combat parity remain unverified.',
  format:{recordBytes:16,sentinelPart:-1,matrixBytes:32,fractionBits:12,
    equation:'worldPoint = jointTranslation - floor(jointRotation * localOffset / 4096)',
    buffers:'actor+12C current; actor+130 previous; swapped once per native tick by8006A6E8'}};
fs.writeFileSync(`${destination}/anchors-verification.json`,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({...report,definitions:undefined,previousBufferAudit:{result:report.previousBufferAudit.result,differences:previousBufferDifferences.length,examples:previousBufferDifferences.slice(0,2)}},null,2));
