#!/usr/bin/env node
// Local Chrome fallback; the in-app browser automation tool is unavailable here.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { spawn } from 'node:child_process';
import assert from 'node:assert/strict';
const levelMode = process.argv.includes('--level');
const out = path.resolve(levelMode ? 'build/level-assets/01' : 'build/xena-preview');
const profile = path.join(out, `qa-profile-${Date.now()}`);
fs.mkdirSync(profile, { recursive: true });
const child = spawn('C:/Program Files/Google/Chrome/Application/chrome.exe', ['--headless', '--disable-gpu',
  '--no-sandbox', '--in-process-gpu',
  '--no-first-run', '--disable-background-networking', '--disable-sync', '--disable-extensions',
  '--remote-debugging-port=0', `--user-data-dir=${profile}`, '--window-size=1200,900', 'about:blank'],
  { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
let launchError = null, log = '';
child.on('error', error => { launchError = error; });
child.stdout.on('data', b => { log += b; }); child.stderr.on('data', b => { log += b; });
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
let socket;
try {
  const portFile = path.join(profile, 'DevToolsActivePort');
  for (let i = 0; !fs.existsSync(portFile) && i < 100; i++) { if (launchError) throw launchError; await pause(100); }
  if (!fs.existsSync(portFile)) throw Error('Chrome debugging endpoint unavailable');
  const [port, endpoint] = fs.readFileSync(portFile, 'utf8').trim().split(/\r?\n/);
  socket = new WebSocket(`ws://127.0.0.1:${port}${endpoint}`);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let id = 0;
  const pending = new Map(), errors = [];
  socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.id && pending.has(message.id)) {
      const promise = pending.get(message.id); pending.delete(message.id);
      if (message.error) promise.reject(Error(message.error.message)); else promise.resolve(message.result);
    }
    if (message.method === 'Runtime.exceptionThrown') errors.push(message.params.exceptionDetails);
  };
  function call(method, params = {}, sessionId = undefined) {
    return new Promise((resolve, reject) => {
      const request = ++id;
      const timer = setTimeout(() => { pending.delete(request); reject(Error(`CDP timeout: ${method}`)); }, 10000);
      pending.set(request, { resolve: value => { clearTimeout(timer); resolve(value); }, reject: error => { clearTimeout(timer); reject(error); } });
      socket.send(JSON.stringify({ id: request, method, params, sessionId }));
    });
  }
  const { targetId } = await call('Target.createTarget', { url: pathToFileURL(path.join(out, 'preview.html')).href });
  const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true });
  await call('Runtime.enable', {}, sessionId); await call('Page.enable', {}, sessionId);
  const evaluate = async expression => {
    const result = await call('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true }, sessionId);
    if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
  };
  let ready = false;
  for (let i = 0; i < 100; i++) {
    ready = await evaluate(levelMode ? '!!globalThis.LevelPreviewLastRender' : 'document.documentElement.dataset.ready === "true"');
    if (ready) break; await pause(100);
  }
  assert(ready, 'Viewer did not render');
  const digest = `(() => { const c=document.getElementById('view'), b=c.getContext('2d').getImageData(0,0,c.width,c.height).data; let h=2166136261; for(let i=0;i<b.length;i+=17) h=Math.imul(h^b[i],16777619); return h>>>0; })()`;
  const initial = await evaluate(digest);
  let checks;
  if (levelMode) {
    const rendered = async expression => { await evaluate(expression); await evaluate('new Promise(r=>requestAnimationFrame(r))'); };
    assert.equal(await evaluate('document.getElementById("object").options.length'), 74);
    assert.equal(await evaluate('globalThis.LevelPreviewLastRender.object'), 'all');
    assert(await evaluate('globalThis.LevelPreviewLastRender.fragments') > 1000);
    await rendered('document.getElementById("top").click()');
    assert.notEqual(await evaluate(digest), initial, 'Top view has no effect');
    await rendered('document.getElementById("overview").click();document.getElementById("object").value="0";document.getElementById("object").dispatchEvent(new Event("change"))');
    assert.equal(await evaluate('globalThis.LevelPreviewLastRender.object'), 0);
    assert(await evaluate('globalThis.LevelPreviewLastRender.triangles') > 0);
    await rendered('document.getElementById("object").value="1";document.getElementById("object").dispatchEvent(new Event("change"));document.getElementById("overview").click()');
    const allTriangles = await evaluate('globalThis.LevelPreviewLastRender.triangles');
    await rendered('document.getElementById("transparent").checked=false;document.getElementById("transparent").dispatchEvent(new Event("change"))');
    assert(await evaluate('globalThis.LevelPreviewLastRender.triangles') < allTriangles, 'Transparency flag filter has no effect');
    await rendered('document.getElementById("transparent").checked=true;document.getElementById("transparent").dispatchEvent(new Event("change"))');
    await rendered('document.getElementById("assembled").checked=true;document.getElementById("assembled").dispatchEvent(new Event("change"))');
    assert.equal(await evaluate('globalThis.LevelPreviewLastRender.object'), 'all');
    const unlit = await evaluate(digest);
    await rendered('document.getElementById("lighting").checked=true;document.getElementById("lighting").dispatchEvent(new Event("change"))');
    const lit = await evaluate(digest); assert.notEqual(lit, unlit, 'LGT toggle has no effect');
    await rendered('document.getElementById("brightness").value="35";document.getElementById("brightness").dispatchEvent(new Event("input"))');
    assert.notEqual(await evaluate(digest), lit, 'LGT brightness has no effect');
    assert.equal(await evaluate('document.getElementById("brightness-value").textContent'), '35');
    const sourcePlacement = await evaluate(digest);
    await rendered('document.getElementById("grounded").checked=true;document.getElementById("grounded").dispatchEvent(new Event("change"))');
    assert(await evaluate('globalThis.LevelPreviewLastRender.grounded'));
    assert.notEqual(await evaluate(digest), sourcePlacement, 'Original grounding has no visible effect');
    await rendered('document.getElementById("object").value="14";document.getElementById("object").dispatchEvent(new Event("change"));document.getElementById("water").checked=true;document.getElementById("water-phase").value="0";document.getElementById("water").dispatchEvent(new Event("change"))');
    const phase0 = await evaluate(digest);
    await rendered('document.getElementById("water-phase").value="16384";document.getElementById("water-phase").dispatchEvent(new Event("input"))');
    assert.notEqual(await evaluate(digest), phase0, 'Water phase has no visible effect');
    assert.equal(await evaluate('globalThis.LevelPreviewLastRender.waterPhase'), 16384);
    await rendered('document.getElementById("assembled").checked=true;document.getElementById("assembled").dispatchEvent(new Event("change"))');
    const withoutSpawns = await evaluate(digest);
    await rendered('document.getElementById("spawns").checked=true;document.getElementById("spawns").dispatchEvent(new Event("change"))');
    assert.notEqual(await evaluate(digest), withoutSpawns, 'Spawn markers have no visible effect');
    assert.equal(await evaluate('document.getElementById("spawn-record").options.length'), 38);
    assert.equal(await evaluate('globalThis.LevelPreviewLastRender.spawnMarkers.length'), 37);
    await rendered('document.getElementById("spawn-record").value="10";document.getElementById("spawn-record").dispatchEvent(new Event("change"))');
    assert.equal(await evaluate('globalThis.LevelPreviewLastRender.spawnMarkers.length'), 1);
    assert.equal(await evaluate('globalThis.LevelPreviewLastRender.spawnMarkers[0].index'), 10);
    assert(await evaluate('document.getElementById("spawn-info").textContent.startsWith("xena:")'));
    await rendered('document.getElementById("spawn-record").value="all";document.getElementById("spawn-record").dispatchEvent(new Event("change"))');
    checks = ['offline file load', '74 object options', 'assembled level renders', 'top view', 'object selection', 'PS1 transparency flag filter', 'assembly toggle', 'LGT color toggle', 'independent brightness35', 'original grounding mode', 'original water phases', '37 source spawn markers', 'individual Xena spawn selection', 'no script exceptions'];
  } else {
  assert.equal(await evaluate('document.getElementById("clip").options.length'), 81);
  assert.equal(await evaluate('document.getElementById("textures").value'), 'hd');
  await evaluate('document.getElementById("textures").value="original";document.getElementById("textures").onchange();');
  const original = await evaluate(digest); assert.notEqual(original, initial, 'Texture toggle has no effect');
  await evaluate('document.getElementById("textures").value="hd";document.getElementById("textures").onchange();document.getElementById("clip").value="81";document.getElementById("clip").onchange();');
  const sword = await evaluate(digest); assert.notEqual(sword, initial, 'Clip selector has no effect');
  await evaluate('document.getElementById("frame").value="7";document.getElementById("frame").oninput();');
  assert.notEqual(await evaluate(digest), sword, 'Frame slider has no effect');
  const frame = await evaluate('document.getElementById("frame").value');
  await evaluate('document.getElementById("play").click();');
  let advanced = false;
  for (let i = 0; i < 10; i++) { await pause(100); advanced = await evaluate('document.getElementById("frame").value') !== frame; if (advanced) break; }
  assert(advanced, 'Playback did not advance');
  await evaluate('document.getElementById("play").click();document.getElementById("skeleton").checked=true;document.getElementById("skeleton").onchange();');
  const skeleton = await evaluate(digest);
  await evaluate('document.getElementById("skeleton").checked=false;document.getElementById("skeleton").onchange();');
  assert.notEqual(await evaluate(digest), skeleton, 'Skeleton switch has no effect');
  checks = ['offline file load', '81 clip options', 'HD enabled', 'texture toggle', 'clip selection', 'frame scrubbing', 'playback', 'skeleton toggle', 'no script exceptions'];
  }
  assert.equal(errors.length, 0, 'Browser script exceptions');
  const screenshot = await call('Page.captureScreenshot', { format: 'png' }, sessionId);
  fs.writeFileSync(path.join(out, 'browser-qa.png'), Buffer.from(screenshot.data, 'base64'));
  const result = { result: 'PASS', checks,
    screenshot: path.relative(process.cwd(), path.join(out, 'browser-qa.png')).replaceAll('\\', '/') };
  fs.writeFileSync(path.join(out, 'browser-verification.json'), JSON.stringify(result, null, 2) + '\n');
  console.log(JSON.stringify(result, null, 2));
  await call('Browser.close');
} finally {
  socket?.close(); child.kill();
  fs.writeFileSync(path.join(out, 'browser-qa.log'), log);
}
