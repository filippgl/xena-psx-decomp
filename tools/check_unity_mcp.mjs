import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
const project=path.resolve(process.argv[2]||'unity/XenaUnity');
const cli=process.env.UNITY_CLI||path.join(process.env.LOCALAPPDATA,'Unity/bin/unity.exe');
const child=spawn(cli,['mcp','--project-path',project],{windowsHide:true,stdio:['pipe','pipe','pipe']});
const send=value=>child.stdin.write(JSON.stringify(value)+'\n');
const report={result:'FAIL',project};
let buffer='',stderr='';child.stderr.on('data',bytes=>stderr+=bytes);
await new Promise((resolve,reject)=>{
  let settled=false;
  const timer=setTimeout(()=>{child.kill();reject(Error('MCP handshake timed out'));},20000);
  const finish=error=>{if(settled)return;settled=true;clearTimeout(timer);child.stdin.end();child.kill();error?reject(error):resolve();};
  child.on('error',finish);
  child.once('exit',code=>{if(!settled)finish(Error(`MCP exited ${code}: ${stderr}`));});
  child.stdin.on('error',finish);
  child.stdout.on('data',bytes=>{
    buffer+=bytes.toString();let at;
    while((at=buffer.indexOf('\n'))>=0){
      const line=buffer.slice(0,at).trim();buffer=buffer.slice(at+1);if(!line)continue;
      try{
        const message=JSON.parse(line);if(message.error)throw Error(JSON.stringify(message.error));
        if(message.id===1){report.protocol=message.result.protocolVersion;send({jsonrpc:'2.0',method:'notifications/initialized'});send({jsonrpc:'2.0',id:2,method:'tools/list',params:{}});}
        if(message.id===2){
          const tools=message.result.tools;report.tools=tools.map(tool=>tool.name);
          const evalTool=tools.find(tool=>/eval$/.test(tool.name));assert(evalTool,'No live C# evaluator exposed');
          send({jsonrpc:'2.0',id:3,method:'tools/call',params:{name:evalTool.name,arguments:{code:'return UnityEngine.Application.unityVersion;'}}});
        }
        if(message.id===3){assert(!message.result.isError,JSON.stringify(message.result));assert(JSON.stringify(message.result).includes('6000.3.25f1'));report.result='PASS';finish();}
      }catch(error){finish(error);}
    }
  });
  send({jsonrpc:'2.0',id:1,method:'initialize',params:{protocolVersion:'2024-11-05',capabilities:{},clientInfo:{name:'Xena-MCP-verification',version:'1'}}});
}).catch(error=>{report.error=error.stack;report.stderr=stderr;});
fs.writeFileSync('build/unity-import/mcp-verification.json',JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report,null,2));if(report.result!=='PASS')process.exit(1);
