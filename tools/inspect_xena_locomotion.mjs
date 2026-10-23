import fs from 'node:fs';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
const clips = new Map([0x14,0x15,0x17].map(slot => [slot, JSON.parse(fs.readFileSync(`build/animation-audit/slot-${slot.toString(16)}.json`))]));
const samples = [];
let comparedValues = 0;
for (const directory of ['btnprobe2','chakram2','chakram3']) {
  const base = `build/${directory}`;
  const log = fs.readFileSync(`${base}/log.txt`,'utf8');
  const match = [...log.matchAll(/player object ([\da-f]+)/gi)].at(-1);
  assert(match,'No recorded player object');
  const object = parseInt(match[1],16) & 0x1fffff;
  for (const file of fs.readdirSync(base).filter(file => /^ram_\d+\.bin$/.test(file)).sort()) {
    const ram = fs.readFileSync(`${base}/${file}`);
    assert.equal(ram.length,0x200000);
    const state = ram.readUInt32LE(object+0xd0);
    // Retain idle following the stop in this sequence, to prove 17 -> 00.
    if (!clips.has(state) && !(directory==='chakram3' && state===0 && +file.slice(4,10)>=9130)) continue;
    const frame = ram.readUInt16LE(object+0xe6)-1;
    const pointer = at => ram.readUInt32LE(at)&0x1fffff;
    const context = pointer(object+0xa8), transforms = pointer(context+0x14), root = pointer(context+0x18);
    const pose = clips.get(state)?.frames[frame];
    if (clips.has(state)) {
      assert(pose,`Missing clip/frame ${state}/${frame}`);
      pose.parts.forEach((part,index) => {
        if (part.parent===-1) return;
        for (let axis=0;axis<3;axis++) {
          assert.equal(ram.readInt32LE(transforms+index*20+axis*4),part.translation[axis],`${directory}/${file} part${index} translation${axis}`);
          assert.equal(ram.readInt16LE(transforms+index*20+12+axis*2),part.rotation[axis],`${directory}/${file} part${index} rotation${axis}`);
          comparedValues+=2;
        }
      });
    }
    samples.push({source:`${base}/${file}`,sha256:crypto.createHash('sha256').update(ram).digest('hex'),state,frame,
      position:[0,4,8].map(offset => ram.readInt32LE(root+offset))});
  }
}
const sequence = directory => samples.filter(sample => sample.source.startsWith(`build/${directory}/`));
const hasTransition = (rows,from,to) => rows.some((sample,index) => index>0 && rows[index-1].state===from && sample.state===to);
assert(hasTransition(sequence('btnprobe2'),0x14,0x15),'No native start -> run transition');
assert(hasTransition(sequence('chakram3'),0x15,0x17),'No native run -> stop transition');
assert(hasTransition(sequence('chakram3'),0x17,0),'No native stop -> idle transition');
const loop = sequence('chakram2');
assert(loop.some((sample,index) => index>0 && loop[index-1].frame===24 && sample.frame===0),'No full native run wrap');
assert.equal(new Set(loop.map(sample=>sample.frame)).size,25);
assert.deepEqual([clips.get(0x14).frameCount,clips.get(0x15).frameCount,clips.get(0x17).frameCount],[5,25,7]);
const report = {result:'PASS',samples:samples.length,poseSamples:samples.filter(sample=>clips.has(sample.state)).length,comparedValues,
  start:{slot:0x14,frames:5},loop:{slot:0x15,frames:25,allFramesCaptured:true},stop:{slot:0x17,frames:7},
  transitions:['14 -> 15','15 frame24 -> frame0','15 -> 17','17 -> 00'],
  scope:'Archived native port RAM, not console/emulator. Every non-world-root pose component in selected locomotion states compared. Slot16 role not established. Real-time cadence and movement physics not established.',
  samplesDetail:samples};
fs.writeFileSync('build/animation-audit/locomotion-verification.json',JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({...report,samplesDetail:undefined},null,2));
