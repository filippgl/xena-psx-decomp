// Export the standalone disc projectile; the hand mesh also contains fingers.
import fs from 'node:fs';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import {readTmd,readStm,buildVram,texturePage} from './lib/xena_model.mjs';
import {readSmi} from './lib/xena_animation.mjs';
import {readTrig,worldMatrices,transformPoint} from './lib/xena_transforms.mjs';
import {png} from './lib/png.mjs';
const manifest=JSON.parse(fs.readFileSync('build/asset-audit/manifest.json'));
const archive=fs.openSync('disc/files/XENA.UDA','r'),sources=[];
function resource(extension){
  const entries=manifest.entries.filter(e=>e.candidates.some(c=>c.name.endsWith('\\CHAKRAM.'+extension)));
  assert.equal(entries.length,1);const entry=entries[0];assert(entry.inArchive);
  const bytes=Buffer.alloc(entry.size);assert.equal(fs.readSync(archive,bytes,0,bytes.length,(entry.lba-manifest.summary.archiveLba)*2048),bytes.length);
  sources.push({extension,bytes:bytes.length,sha256:crypto.createHash('sha256').update(bytes).digest('hex')});return bytes;
}
let tmd,smi,stm;
try{tmd=resource('TMD');smi=resource('SMI');stm=resource('STM');}finally{fs.closeSync(archive);}
const objects=readTmd(tmd),parts=readSmi(smi,objects.length),matrices=worldMatrices(parts,readTrig(fs.readFileSync('xena.exe')));
assert.equal(objects.length,2);assert.equal(objects.reduce((n,o)=>n+o.faces.length,0),46);
const {vram,populated}=buildVram(readStm(stm));
const out='unity/XenaUnity/Assets/Imported/Resources/Chakram';fs.mkdirSync(out,{recursive:true});
const materials=[],textures=[],materialKeys=new Map();let uvCorners=0;
function material(face){
  const key=face.uv?`texture:${face.page}:${face.clut}`:`color:${face.color.join(',')}`;
  if(materialKeys.has(key))return materialKeys.get(key);
  let texture=-1;
  if(face.uv){
    texture=textures.length;const image=texturePage(vram,face.page,face.clut),filename=`page-${texture}.png`;
    fs.writeFileSync(`${out}/${filename}`,png(256,256,image.rgba));textures.push({original:filename,upscaled:''});
  }
  const index=materials.length;materialKeys.set(key,index);
  materials.push({name:key,doubleSided:true,vertexLighting:false,texture,color:face.uv?[1,1,1,1]:[...face.color.map(n=>n/255),1]});return index;
}
const meshes=objects.map((object,i)=>{
  const groups=new Map(),matrix=matrices[i];
  for(const face of object.faces){
    const index=material(face);
    if(!groups.has(index))groups.set(index,{material:index,positions:[],normals:[],uv:[],indices:[],lightingRgb:[]});
    const primitive=groups.get(index),start=primitive.positions.length/3;
    for(let corner=0;corner<3;corner++){
      const point=transformPoint(matrix,object.vertices[face.indices[corner]]);
      primitive.positions.push(point[0]/400,-point[1]/400,point[2]/400);
      const n=object.normals[face.normals[corner]],rotated=[0,1,2].map(row=>matrix.rotation.slice(row*3,row*3+3).reduce((sum,value,col)=>sum+value*n[col],0));
      const length=Math.hypot(...rotated)||1;primitive.normals.push(rotated[0]/length,-rotated[1]/length,rotated[2]/length);
      if(face.uv){
        const [u,v]=face.uv[corner],depth=(face.page>>>7)&3,x=(face.page&15)*64,y=(face.page&16)*16;
        assert(populated[(y+v)*1024+x+(u>>>(2-depth))],'Chakram UV outside loaded texture');uvCorners++;
        primitive.uv.push(u/256,1-v/256);
      }else primitive.uv.push(0,0);
    }
    primitive.indices.push(start,start+2,start+1);
  }
  return{name:`Chakram_${i}`,primitives:[...groups.values()]};
});
const nodes=[{name:'ChakramRoot',parent:-1,mesh:-1,translation:[0,0,0],rotation:[0,0,0,1],scale:[1,1,1]},
  ...meshes.map((_,i)=>({name:`ChakramGeometry_${i}`,parent:0,mesh:i,translation:[0,0,0],rotation:[0,0,0,1],scale:[1,1,1]}))];
const document={formatVersion:1,source:'SLUS-00977 standalone CHAKRAM.TMD/SMI/STM',unitsPerMeter:400,
  conventions:'Original bind transforms baked; positions scaled1/400, Y reflected, triangle winding reversed. Unlit opaque preview; flight physics not derived here.',nodes,meshes,materials,textures,clips:[]};
fs.writeFileSync(`${out}/Chakram.xenamodel`,JSON.stringify(document));
const report={result:'PASS',sources,meshes:meshes.length,triangles:46,sourceVertices:39,uvCorners,textures:textures.length,output:`${out}/Chakram.xenamodel`};
fs.mkdirSync('build/unity-import',{recursive:true});fs.writeFileSync('build/unity-import/chakram-export.json',JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report,null,2));
