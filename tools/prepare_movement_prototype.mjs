import fs from 'node:fs';
import assert from 'node:assert/strict';
import './prepare_movement_input.mjs';
const source=JSON.parse(fs.readFileSync('build/level-assets/01/collision-source.json'));
const vertices=[];
for(const group of source.groups){
  assert(group.flags&2,'Only verified absolute-Y level01 CLS supported');
  for(const triangle of group.triangles)for(const point of triangle.vertices)
    point.forEach((v,axis)=>vertices.push((v+group.position[axis])|0));
}
const native=JSON.parse(fs.readFileSync('build/level-assets/01/collision-native.json'));
assert.deepEqual(vertices,native.snapshots.flatMap(group=>group.triangles.flat(2)),'Collision staging differs from native CLS');
assert.equal(vertices.length,1071);
fs.writeFileSync('unity/XenaUnity/Assets/Imported/Level01/CollisionMesh.json',JSON.stringify({formatVersion:1,vertices}));
if(process.argv.includes('--verification')){
  const target='unity/XenaLightingVerification';
  if(!fs.existsSync(target))for(const folder of ['Assets','Packages','ProjectSettings'])fs.cpSync('unity/XenaUnity/'+folder,target+'/'+folder,{recursive:true});
  else fs.cpSync('unity/XenaUnity/Assets/Imported',target+'/Assets/Imported',{recursive:true});
  const settings=target+'/ProjectSettings/ProjectSettings.asset';
  fs.writeFileSync(settings,fs.readFileSync(settings,'utf8').replace(/activeInputHandler: \d+/,'activeInputHandler: 2'));
}
console.log('Staged 119 original CLS triangles; all 1071 coordinate values match native.');
