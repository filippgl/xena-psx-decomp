(() => {
  const data = globalThis.XenaPreviewData;
  for (const texture of data.textures) texture.rgba = Uint8Array.from(atob(texture.rgbaBase64), c => c.charCodeAt(0));
  const hdTextures = globalThis.XenaHdTextures;
  if (hdTextures) for (const texture of hdTextures) texture.rgba = Uint8Array.from(atob(texture.rgbaBase64), c => c.charCodeAt(0));
  const textureSelector = document.getElementById('textures');
  textureSelector.value = hdTextures ? 'hd' : 'original';
  textureSelector.querySelector('[value="hd"]').disabled = !hdTextures;
  const canvas = document.getElementById('view'), context = canvas.getContext('2d');
  const selector = document.getElementById('clip'), frameInput = document.getElementById('frame');
  const status = document.getElementById('status'), play = document.getElementById('play');
  const options = { width: canvas.width, height: canvas.height, yaw: Math.PI + 0.25, pitch: -0.08, zoom: 1.5 };
  let selected = data.clips.find(c => c.slot === 0) || data.clips[0], frame = 0, playing = false, last = 0;
  const names = { 0: '00 — покой (кандидат)', 20: '14 — бег (кандидат)', 47: '2F — прыжок (кандидат)',
    81: '51 — удар мечом', 97: '61 — удар ногой', 100: '64 — прицеливание чакрамом' };
  for (const clip of data.clips) {
    const option = document.createElement('option'); option.value = clip.slot;
    option.textContent = `${names[clip.slot] || clip.slot.toString(16).padStart(2, '0').toUpperCase()} · ${clip.frames.length} кадров`;
    selector.append(option);
  }
  selector.value = selected.slot;
  function draw() {
    options.skeleton = document.getElementById('skeleton').checked;
    const pose = selected.frames[frame];
    const image = XenaPreviewRenderer.render({ ...data, textures: textureSelector.value === 'hd' ? hdTextures : data.textures }, pose.matrices, pose.visibility, options);
    context.putImageData(new ImageData(image.rgba, image.width, image.height), 0, 0);
    frameInput.max = selected.frames.length - 1; frameInput.value = frame;
    status.textContent = `Кадр ${frame + 1} / ${selected.frames.length} · ${image.triangles} видимых треугольников`;
    document.documentElement.dataset.ready = 'true';
  }
  selector.onchange = () => { selected = data.clips.find(c => c.slot === Number(selector.value)); frame = 0; draw(); };
  frameInput.oninput = () => { frame = Number(frameInput.value); draw(); };
  play.onclick = () => { playing = !playing; play.textContent = playing ? 'Пауза' : 'Воспроизвести'; last = 0; };
  document.getElementById('skeleton').onchange = draw;
  textureSelector.onchange = draw;
  let drag = null;
  canvas.onpointerdown = event => { drag = [event.clientX, event.clientY]; canvas.setPointerCapture(event.pointerId); };
  canvas.onpointerup = () => { drag = null; };
  canvas.onpointercancel = () => { drag = null; };
  canvas.onpointermove = event => {
    if (!drag) return;
    options.yaw += (event.clientX - drag[0]) * 0.01;
    options.pitch = Math.max(-1.2, Math.min(1.2, options.pitch + (event.clientY - drag[1]) * 0.01));
    drag = [event.clientX, event.clientY]; draw();
  };
  canvas.onwheel = event => { event.preventDefault(); options.zoom = Math.max(0.4, Math.min(3, options.zoom * Math.exp(-event.deltaY * 0.001))); draw(); };
  function tick(time) {
    if (playing && time - last >= 1000 / Number(document.getElementById('rate').value)) {
      frame = (frame + 1) % selected.frames.length; last = time; draw();
    }
    requestAnimationFrame(tick);
  }
  draw(); requestAnimationFrame(tick);
})();
