// Compare archived native poses, not state labels alone, with decoded SMA slots.
import fs from 'node:fs';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
const sources = [{directory:'flatjump', states:[0x2f,0x1a]}, {directory:'jf1',states:[0x35]}];
const clips = new Map();
const samples = [];
let comparedValues = 0;
for (const source of sources) {
  const directory = `build/${source.directory}`;
  const log = fs.readFileSync(`${directory}/log.txt`, 'utf8');
  const match = [...log.matchAll(/player object ([\da-f]+)/gi)].at(-1);
  assert(match, 'Missing recorded player object');
  const object = parseInt(match[1],16) & 0x1fffff;
  for (const file of fs.readdirSync(directory).filter(name => /^ram_\d+\.bin$/.test(name)).sort()) {
    const ram = fs.readFileSync(`${directory}/${file}`);
    assert.equal(ram.length, 0x200000);
    const state = ram.readUInt32LE(object + 0xd0);
    if (!source.states.includes(state)) continue;
    if (!clips.has(state)) clips.set(state, JSON.parse(fs.readFileSync(`build/animation-audit/slot-${state.toString(16).padStart(2,'0')}.json`)));
    const clip = clips.get(state);
    const frameIndex = ram.readUInt16LE(object + 0xe6) - 1;
    const frame = clip.frames[frameIndex];
    assert(frame, `Missing SMA frame ${state}/${frameIndex}`);
    const pointer = at => ram.readUInt32LE(at) & 0x1fffff;
    const context = pointer(object + 0xa8), transforms = pointer(context + 0x14), root = pointer(context + 0x18);
    frame.parts.forEach((part, index) => {
      if (part.parent === -1) return; // Original runtime supplies the world root.
      for (let axis = 0; axis < 3; axis++) {
        assert.equal(ram.readInt32LE(transforms + index*20 + axis*4), part.translation[axis], `${file} joint${index} translation${axis}`);
        assert.equal(ram.readInt16LE(transforms + index*20 + 12 + axis*2), part.rotation[axis], `${file} joint${index} rotation${axis}`);
        comparedValues += 2;
      }
    });
    samples.push({source:`${directory}/${file}`, sha256:crypto.createHash('sha256').update(ram).digest('hex'),
      state, slot:state, frame:frameIndex, pendingFieldE2:ram.readUInt16LE(object + 0xe2),
      position:[0,4,8].map(offset => ram.readInt32LE(root + offset))});
  }
}
const jump = samples.filter(sample => sample.state === 0x2f);
assert.equal(jump.length,32,'Expected complete archived standing-jump capture interval');
const baselineY = jump[0].position[1];
const takeoff = jump.find(sample => baselineY - sample.position[1] > 24);
const apex = jump.reduce((best,sample) => sample.position[1] < best.position[1] ? sample : best);
const touchdown = jump.find(sample => sample.frame > apex.frame && Math.abs(sample.position[1] - baselineY) < 8);
assert.deepEqual([takeoff.frame,apex.frame,touchdown.frame],[6,17,29]);
const report = {result:'PASS', samples:samples.length, comparedValues,
  standingJump:{slot:0x2f, frameCount:clips.get(0x2f).frameCount, takeoff:takeoff.frame,apex:apex.frame,touchdown:touchdown.frame,
    heightSourceUnits:baselineY-apex.position[1]},
  correction:'Slot/state35 is a moving airborne leap in these captures, NOT a standalone landing clip.',
  scope:'Archived native port RAM, not hardware/emulator. Every non-world-root joint component compared. Capture spacing/timing and prototype scale do not prove original real-time physics.',
  samplesDetail:samples};
fs.writeFileSync('build/animation-audit/jump-verification.json',JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({...report,samplesDetail:undefined},null,2));
