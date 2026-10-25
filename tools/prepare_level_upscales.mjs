// Assemble existing native replacement tiles into Unity's full texture pages.
// Page/CLUT alone are reused across levels: decoded source pixels MUST match.
import fs from 'node:fs';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import {readPng, png} from './lib/png.mjs';
import {readStm, buildVram, texturePage} from './lib/xena_model.mjs';
const dir='build/level-assets/01',pack='build/texreplace',dump='build/texdump_all';
const gltf=JSON.parse(fs.readFileSync(`${dir}/level-assembled.gltf`));
const names=new Set(fs.existsSync(pack)?fs.readdirSync(pack).filter(x=>x.endsWith('.png')).map(x=>x.slice(0,-4)):[]);
const rows=names.size?fs.readFileSync(`${dump}/manifest.txt`,'utf8').trim().split(/\r?\n/).map(line=>line.split(/\s+/)).filter(row=>names.has(row[0])):[];
assert.equal(new Set(rows.map(row=>row[0])).size,names.size,'Replacement tiles missing original dump provenance');
const manifest=JSON.parse(fs.readFileSync('build/asset-audit/manifest.json'));
const entry=manifest.entries.find(e=>e.candidates.some(c=>c.name==='DATA\\LEVELS\\01\\LEVEL.STM'));
assert(entry?.inArchive);const archive=fs.readFileSync('disc/files/XENA.UDA'),offset=(entry.lba-manifest.summary.archiveLba)*2048;
const stm=archive.subarray(offset,offset+entry.size);assert.equal(hashBytes(stm),entry.sha256);
const {vram}=buildVram(readStm(stm));
function hashBytes(b){return crypto.createHash('sha256').update(b).digest('hex');}
const report={result:'PASS',replacementFiles:names.size,manifestTiles:rows.length,pages:[],matchedTiles:0,sourceMismatch:0};
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const seen=new Set();
for(let index=0;index<gltf.images.length;index++){
  const material=gltf.materials.find(m=>m.pbrMetallicRoughness.baseColorTexture?.index===index);
  const {sourceTPage:page,sourceClut:clut}=material.extras;
  const original=readPng(fs.readFileSync(`${dir}/${gltf.images[index].uri}`));
  const originalBytes=Buffer.from(original.rgba);
  assert.equal(original.width,256);assert.equal(original.height,256);
  assert(originalBytes.equals(Buffer.from(texturePage(vram,page,clut).rgba)),'Page differs from original STM');
  const mask=Buffer.alloc(256*256*4);let stpPixels=0;
  const depth=(page>>>7)&3,px=(page&15)*64,py=(page&16)*16;
  for(let y=0;y<256;y++)for(let x=0;x<256;x++){
    let word=vram[(py+y)*1024+px+(depth===0?x>>>2:depth===1?x>>>1:x)]||0;
    if(depth<2){const color=depth===0?(word>>>((x&3)*4))&15:(word>>>((x&1)*8))&255;word=vram[(clut>>>6)*1024+(clut&63)*16+color];}
    const at=(y*256+x)*4,value=word&0x8000?255:0; mask[at]=mask[at+1]=mask[at+2]=value;mask[at+3]=255;if(value)stpPixels++;
  }
  fs.writeFileSync(`${dir}/page-${index}-stp.png`,png(256,256,mask));
  const matches=[];
  for(const row of rows){
    if(parseInt(row[1],16)!==(page&0x19f)||parseInt(row[2],16)!==clut)continue;
    const [x,y,w,h]=row.slice(3,7).map(Number);
    assert(x>=0&&y>=0&&x+w<=256&&y+h<=256);
    const crop=Buffer.alloc(w*h*4);
    for(let v=0;v<h;v++)originalBytes.copy(crop,v*w*4,((y+v)*256+x)*4,((y+v)*256+x+w)*4);
    const raw=fs.readFileSync(`${dump}/${row[0]}.rgba`);
    if(!crop.equals(raw)){report.sourceMismatch++;continue;}
    matches.push({name:row[0],x,y,w,h,sourceSha256:hash(crop)});
  }
  if(!matches.length){report.pages.push({index,page,clut,stpPixels,matched:0});continue;}
  const rgba=Buffer.alloc(1024*1024*4);
  for(let y=0;y<1024;y++)for(let x=0;x<1024;x++){
    const at=((y>>>2)*256+(x>>>2))*4, dst=(y*1024+x)*4;
    for(let c=0;c<4;c++)rgba[dst+c]=original.rgba[at+c];
  }
  // Larger verified regions first, then deterministic smaller refinements.
  matches.sort((a,b)=>b.w*b.h-a.w*a.h||a.name.localeCompare(b.name));
  for(const item of matches){
    const bytes=fs.readFileSync(`${pack}/${item.name}.png`), image=readPng(bytes);
    assert.equal(image.width,item.w*4);assert.equal(image.height,item.h*4);
    const imageBytes=Buffer.from(image.rgba);
    for(let y=0;y<image.height;y++)imageBytes.copy(rgba,((item.y*4+y)*1024+item.x*4)*4,y*image.width*4,(y+1)*image.width*4);
    item.replacementSha256=hash(bytes);seen.add(item.name);
  }
  const output=`page-${index}-4x.png`;fs.writeFileSync(`${dir}/${output}`,png(1024,1024,rgba));
  report.pages.push({index,page,clut,output,stpPixels,matched:matches.length,tiles:matches});report.matchedTiles+=matches.length;
}
report.uniqueMatchedTiles=seen.size;report.upscaledPages=report.pages.filter(p=>p.matched>0).length;
if(names.size)assert(report.uniqueMatchedTiles>0,'Existing replacement pack did not match any original level pixels');
fs.mkdirSync('build/unity-import',{recursive:true});fs.writeFileSync('build/unity-import/level-upscale-mapping.json',JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({...report,pages:report.pages.map(p=>({index:p.index,matched:p.matched}))},null,2));
