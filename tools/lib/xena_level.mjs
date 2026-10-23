// Partial level data formats established from original func_800BCEFC and
// func_8006287C. Unknown fields are retained, not assigned gameplay meanings.
export function readLgt(bytes, vertexCounts) {
  if (!Array.isArray(vertexCounts) || vertexCounts.some(n => !Number.isSafeInteger(n) || n < 0 || n > 65535))
    throw Error('Invalid LGT vertex counts');
  const length = vertexCounts.reduce((n, count) => n + count * 4, 0);
  if (bytes.length !== length) throw Error(`Invalid LGT size: expected ${length}, got ${bytes.length}`);
  let at = 0;
  return vertexCounts.map(count => Array.from({ length: count }, () => {
    const color = [...bytes.subarray(at, at + 4)]; at += 4; return color;
  }));
}

// 800BD14C stores biased channels as signed shorts before clamping to bytes.
// The fourth source byte is retained as a code, not interpreted as alpha.
export function applyLgtBias(color, bias) {
  if (!Number.isInteger(bias) || bias < -32768 || bias > 32767) throw Error('Invalid LGT bias');
  return color.slice(0, 3).map(value => Math.max(0, Math.min(255, ((value + bias) << 16) >> 16)));
}

export function readLevelBin(bytes) {
  if (bytes.length < 0x11c) throw Error('Truncated level BIN header');
  const count = bytes.readUInt32LE(0x100), offset = bytes.readUInt32LE(0x104);
  if (count > 4096 || offset < 0x11c || offset + count * 40 > bytes.length) throw Error('Invalid level BIN object table');
  return { offset, objects: Array.from({ length: count }, (_, index) => {
    const at = offset + index * 40;
    return { index, offset: at, name: bytes.subarray(at, at + 16).toString('ascii').replace(/\0.*$/, ''),
      position: [16, 20, 24].map(n => bytes.readInt32LE(at + n)),
      rotation: [30, 28, 32].map(n => bytes.readInt16LE(at + n)),
      fields34To38: [34, 36, 38].map(n => bytes.readInt16LE(at + n)) };
  }) };
}

export function readCls(bytes) {
  const need = (at, size) => { if (at < 0 || at + size > bytes.length) throw Error('Truncated CLS'); };
  need(0, 4);
  const count = bytes.readInt16LE(0), groups = [];
  if (count < 0 || count > 4096) throw Error('Invalid CLS group count');
  let cursor = 4;
  for (let index = 0; index < count; index++) {
    need(cursor, 28);
    const triangles = bytes.readInt16LE(cursor);
    if (triangles < 0) throw Error('Invalid CLS triangle count');
    need(cursor, 28 + triangles * 48);
    const group = { index, offset: cursor, byteLength: 28 + triangles * 48,
      headerHex: bytes.subarray(cursor, cursor + 28).toString('hex'),
      position: [12, 16, 20].map(n => bytes.readInt32LE(cursor + n)),
      flags: bytes.readUInt16LE(cursor + 26), triangles: [] };
    cursor += 28;
    for (let i = 0; i < triangles; i++, cursor += 48) group.triangles.push({
      offset: cursor, prefixHex: bytes.subarray(cursor, cursor + 8).toString('hex'),
      vertices: [8, 20, 32].map(n => [0, 4, 8].map(axis => bytes.readInt32LE(cursor + n + axis))),
      suffixHex: bytes.subarray(cursor + 44, cursor + 48).toString('hex') });
    groups.push(group);
  }
  if (cursor !== bytes.length) throw Error('Unparsed CLS footer');
  return { headerHex: bytes.subarray(0, 4).toString('hex'), groups };
}

export function readPlrs(bytes) {
  if (bytes.some(n => n !== 9 && n !== 10 && n !== 13 && (n < 32 || n > 126))) throw Error('Non-ASCII PLRS data');
  const entries = [];
  let ended = false;
  for (const [lineIndex, line] of bytes.toString('ascii').split(/\r\n|\n|\r/).entries()) {
    if (!line.trim()) continue;
    if (ended) throw Error('PLRS data after END');
    if (line.trim() === 'END') { ended = true; continue; }
    const [name, ...tokens] = line.trim().split(/\s+/);
    if (!/^[A-Za-z0-9_]{1,13}$/.test(name) || tokens.length !== 23) throw Error(`Invalid PLRS record at line ${lineIndex + 1}`);
    const values = tokens.map(token => {
      if (!/^-?\d+$/.test(token)) throw Error('Invalid PLRS integer');
      const n = Number(token);
      if (!Number.isSafeInteger(n) || n < -2147483648 || n > 2147483647) throw Error('PLRS integer overflow');
      return n;
    });
    entries.push({ name, sourceLine: lineIndex + 1, position: values.slice(0, 3),
      rotationTokens: values.slice(3, 6), field7: values[6],
      vectorFields: [values.slice(7, 10), values.slice(10, 13), values.slice(13, 16)],
      parameterFields: values.slice(16), values });
  }
  if (!ended) throw Error('PLRS END marker missing');
  return entries;
}

// Original temporary PLRS record (84 bytes), 80063340..800638F4.
export function encodePlrsRecord(record) {
  if (!record || !/^[A-Za-z0-9_]{1,13}$/.test(record.name) || !Array.isArray(record.values) || record.values.length !== 23 ||
      record.values.some(n => !Number.isInteger(n) || n < -2147483648 || n > 2147483647)) throw Error('Invalid PLRS runtime record');
  const bytes = Buffer.alloc(84), values = record.values;
  values.slice(0, 3).forEach((n, i) => bytes.writeInt32LE(n, i * 4));
  values.slice(7, 16).forEach((n, i) => bytes.writeInt32LE(n, 12 + i * 4));
  values.slice(16).forEach((n, i) => bytes.writeUInt16LE(n & 65535, 48 + i * 2));
  [62, 66, 64].forEach((at, i) => bytes.writeUInt16LE(values[3 + i] & 65535, at));
  bytes.writeUInt16LE(values[6] & 65535, 68);
  bytes.write(record.name, 70, 'ascii');
  return bytes;
}

// Original height query, func_800142EC, uncached path (a1=0, a2=0).
// Uses original grid membership; fixed-point products deliberately wrap to int32.
export function queryLevelHeight(terrain, point) {
  const signed16 = value => (value << 16) >> 16;
  const cell = (value, origin) => Math.max(0, Math.min(14, Math.trunc(((value - 2048 - origin) | 0) / 4096)));
  const x = cell(point[0], terrain.origin[0]), z = cell(point[2], terrain.origin[1]);
  let best = 0x7fffffff, height = -16777216;
  for (const index of [z * 16 + x, z * 16 + x + 1, (z + 1) * 16 + x, (z + 1) * 16 + x + 1]) {
    for (const triangle of terrain.cells[index]) {
      const vertices = terrain.triangles[triangle];
      const dx = vertices.map(v => (v[0] - point[0]) | 0), dz = vertices.map(v => (v[2] - point[2]) | 0);
      if ((dx[0] & dx[1] & dx[2]) < 0 || (dx[0] | dx[1] | dx[2]) > 0 ||
          (dz[0] & dz[1] & dz[2]) < 0 || (dz[0] | dz[1] | dz[2]) > 0) continue;
      let inside = true;
      for (let i = 0; i < 3; i++) {
        const next = (i + 1) % 3;
        const cross = (signed16(dz[i]) * signed16(dx[next] - dx[i]) - signed16(dx[i]) * signed16(dz[next] - dz[i])) | 0;
        if (cross < 0) { inside = false; break; }
      }
      if (!inside) continue;
      const a = [dx[2] - dx[1], vertices[2][1] - vertices[1][1], dz[2] - dz[1]].map(signed16);
      const b = [dx[0] - dx[2], vertices[0][1] - vertices[2][1], dz[0] - dz[2]].map(signed16);
      const normal = [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]].map(n => n | 0);
      const denominator = normal[1] >> 4;
      if (!denominator) continue;
      const slopeX = Math.trunc((normal[0] << 8) / denominator) | 0;
      const slopeZ = Math.trunc((normal[2] << 8) / denominator) | 0;
      const candidate = ((Math.imul(dx[0], slopeX) >> 12) + (Math.imul(dz[0], slopeZ) >> 12) + vertices[0][1]) | 0;
      let difference = (candidate - point[1]) | 0;
      if (difference < 0) difference = (-difference) | 0;
      if (best < difference) continue;
      best = difference; height = candidate;
    }
  }
  return height;
}
