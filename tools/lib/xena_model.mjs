// The TMD primitive layouts used by XENA.TMD; reject unknown layouts explicitly.
export function readTmd(bytes, { allowSemiTransparent = false } = {}) {
  if (bytes.length < 12 || bytes.readUInt32LE(0) !== 0x41 || bytes.readUInt32LE(4) !== 0)
    throw Error('Expected relative TMD');
  const count = bytes.readUInt32LE(8), objects = [];
  const need = (at, length) => { if (at < 0 || at + length > bytes.length) throw Error(`TMD range at ${at}`); };
  need(12, count * 28);
  for (let index = 0; index < count; index++) {
    const at = 12 + index * 28;
    const vertexAt = 12 + bytes.readUInt32LE(at), vertexCount = bytes.readUInt32LE(at + 4);
    const normalAt = 12 + bytes.readUInt32LE(at + 8), normalCount = bytes.readUInt32LE(at + 12);
    const scale = bytes.readInt32LE(at + 24);
    if (scale !== 0) throw Error('Nonzero TMD scale not yet supported');
    need(vertexAt, vertexCount * 8); need(normalAt, normalCount * 8);
    const vertices = Array.from({ length: vertexCount }, (_, i) => [0, 2, 4].map(n => bytes.readInt16LE(vertexAt + i * 8 + n)));
    const normals = Array.from({ length: normalCount }, (_, i) => [0, 2, 4].map(n => bytes.readInt16LE(normalAt + i * 8 + n)));
    const faces = [];
    let cursor = 12 + bytes.readUInt32LE(at + 16);
    for (let p = 0; p < bytes.readUInt32LE(at + 20); p++) {
      need(cursor, 4);
      const length = 4 + bytes[cursor + 1] * 4;
      need(cursor, length);
      const flags = bytes[cursor + 2], mode = bytes[cursor + 3];
      const textured = mode === 0x34 || (allowSemiTransparent && mode === 0x36);
      if (!((textured && length === 28) || (mode === 0x30 && length === 20)))
        throw Error(`Unsupported TMD primitive mode ${mode.toString(16)} length ${length}`);
      if (flags & ~2) throw Error(`Unsupported primitive flags ${flags}`);
      const data = cursor + 4, indices = data + (textured ? 12 : 4);
      const face = { indices: [], normals: [], doubleSided: !!(flags & 2) };
      if (mode & 2) face.semiTransparent = true;
      for (let corner = 0; corner < 3; corner++) {
        const normal = bytes.readUInt16LE(indices + corner * 4);
        const vertex = bytes.readUInt16LE(indices + corner * 4 + 2);
        if (normal >= normals.length || vertex >= vertices.length) throw Error('Invalid TMD face index');
        face.indices.push(vertex); face.normals.push(normal);
      }
      if (textured) {
        face.uv = [0, 4, 8].map(n => [bytes[data + n], bytes[data + n + 1]]);
        face.clut = bytes.readUInt16LE(data + 2);
        face.page = bytes.readUInt16LE(data + 6);
      } else face.color = [...bytes.subarray(data, data + 3)];
      faces.push(face); cursor += length;
    }
    objects.push({ index, vertices, normals, faces });
  }
  return objects;
}

export function readStm(bytes) {
  if (bytes.length < 4) throw Error('Truncated STM');
  const count = bytes.readUInt32LE(0), images = [];
  let cursor = 4;
  for (let index = 0; index < count; index++) {
    if (cursor + 16 > bytes.length) throw Error('Truncated STM entry');
    const size = bytes.readUInt32LE(cursor), name = bytes.subarray(cursor + 4, cursor + 16).toString('ascii').replace(/\0.*$/, '');
    cursor += 16;
    const end = cursor + size;
    if (end > bytes.length || cursor + 8 > end || bytes.readUInt32LE(cursor) !== 0x10) throw Error('Invalid STM TIM image');
    const flags = bytes.readUInt32LE(cursor + 4), depth = flags & 7;
    if (depth > 2 || flags & ~15) throw Error('Unsupported TIM flags');
    cursor += 8;
    const block = () => {
      if (cursor + 12 > end) throw Error('Truncated TIM block');
      const size = bytes.readUInt32LE(cursor);
      const [x, y, width, height] = [4, 6, 8, 10].map(n => bytes.readUInt16LE(cursor + n));
      if (size !== 12 + width * height * 2 || cursor + size > end || x + width > 1024 || y + height > 512)
        throw Error('Invalid TIM VRAM rectangle');
      const data = bytes.subarray(cursor + 12, cursor + size);
      cursor += size;
      return { x, y, width, height, data };
    };
    const clut = flags & 8 ? block() : null;
    const image = block();
    if (cursor !== end) throw Error('Unparsed TIM bytes');
    images.push({ name, depth, clut, image });
  }
  if (bytes.subarray(cursor).some(n => n !== 0)) throw Error('Nonzero STM footer');
  return { images, paddingBytes: bytes.length - cursor };
}

export function buildVram(stm) {
  const vram = new Uint16Array(1024 * 512), populated = new Uint8Array(vram.length);
  for (const texture of stm.images) for (const block of [texture.clut, texture.image].filter(Boolean)) {
    for (let y = 0; y < block.height; y++) for (let x = 0; x < block.width; x++) {
      const at = (block.y + y) * 1024 + block.x + x;
      const word = block.data.readUInt16LE((y * block.width + x) * 2);
      if (populated[at] && vram[at] !== word) throw Error('Conflicting STM VRAM uploads');
      vram[at] = word; populated[at] = 1;
    }
  }
  return { vram, populated };
}

export function color555(word) {
  return [0, 5, 10].map(n => { const v = (word >>> n) & 31; return (v << 3) | (v >>> 2); }).concat(word === 0 ? 0 : 255);
}

export function texturePage(vram, page, clut, populated = null) {
  const depth = (page >>> 7) & 3;
  if (depth > 2) throw Error('Unsupported texture page depth');
  const pageX = (page & 15) * 64, pageY = (page & 16) * 16;
  const clutX = (clut & 63) * 16, clutY = clut >>> 6;
  const rgba = new Uint8Array(256 * 256 * 4);
  let sampledUnpopulated = 0;
  for (let v = 0; v < 256; v++) for (let u = 0; u < 256; u++) {
    const x = pageX + (depth === 0 ? u >>> 2 : depth === 1 ? u >>> 1 : u);
    const at = (pageY + v) * 1024 + x;
    if (x >= 1024 || pageY + v >= 512) continue;
    let word = vram[at];
    if (populated && !populated[at]) sampledUnpopulated++;
    if (depth !== 2) {
      const color = depth === 0 ? (word >>> ((u & 3) * 4)) & 15 : (word >>> ((u & 1) * 8)) & 255;
      if (clutX + color >= 1024 || clutY >= 512) throw Error('CLUT out of range');
      word = vram[clutY * 1024 + clutX + color];
    }
    rgba.set(color555(word), (v * 256 + u) * 4);
  }
  return { width: 256, height: 256, rgba, sampledUnpopulated };
}
