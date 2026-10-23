// SLUS-00977: normal playback (context+0x2A == 0), from func_80069040.
const signed = (value, bits) => (value << (32 - bits)) >> (32 - bits);
const short = n => signed(n, 16);

export function readSmi(bytes, objectCount) {
  if (bytes.length < objectCount * 20) throw Error('Truncated SMI transforms');
  const parts = Array.from({ length: objectCount }, (_, i) => {
    const at = i * 20;
    const part = { translation: [0, 4, 8].map(n => bytes.readInt32LE(at + n)),
      rotation: [12, 14, 16].map(n => bytes.readInt16LE(at + n)), parent: bytes.readInt16LE(at + 18) };
    if (part.parent < -1 || part.parent >= objectCount) throw Error(`Invalid parent at part ${i}`);
    return part;
  });
  for (let i = 0; i < parts.length; i++) {
    const visited = new Set();
    for (let j = i; j !== -1; j = parts[j].parent) {
      if (visited.has(j)) throw Error(`Cyclic SMI hierarchy at ${i}`);
      visited.add(j);
    }
  }
  return parts;
}

export function decodeSma(bytes, initialParts, initialVisibility = initialParts.map(() => true)) {
  if (initialVisibility.length !== initialParts.length) throw Error('Visibility size mismatch');
  if (bytes.length < 588 || bytes.readUInt32LE(0) !== 146) throw Error('Unsupported SMA slot table');
  const offsets = Array.from({ length: 146 }, (_, i) => bytes.readUInt32LE(4 + i * 4));
  const clips = [];
  for (let slot = 0; slot < offsets.length; slot++) {
    if (!offsets[slot]) continue;
    const start = offsets[slot] + 4;
    const limit = Math.min(bytes.length, ...offsets.filter(o => o + 4 > start).map(o => o + 4));
    let cursor = start;
    const need = n => { if (cursor + n > limit) throw Error(`SMA slot ${slot}: truncated at ${cursor}`); };
    const u16 = () => { need(2); const n = bytes.readUInt16LE(cursor); cursor += 2; return n; };
    const i16 = () => signed(u16(), 16);
    const u8 = () => { need(1); return bytes[cursor++]; };
    const i8 = () => signed(u8(), 8);
    const magic = u16(), version = u16(), frameCount = u16(), reserved = u16();
    if (magic !== 0x4144 || version !== 0x0200) throw Error(`Unsupported clip header at slot ${slot}`);
    const parts = structuredClone(initialParts);
    const visibility = [...initialVisibility];
    const frames = [];
    const setTranslation = (part, axis, value, absolute) => {
      part.translation[axis] = absolute ? value : (part.translation[axis] + value) | 0;
    };
    const setRotation = (part, axis, value, absolute) => {
      part.rotation[axis] = short(absolute ? value : part.rotation[axis] + value);
    };
    for (let frame = 0; frame < frameCount; frame++) {
      const frameOffset = cursor;
      const tag = u16(), count = u16();
      const commands = [];
      for (let command = 0; command < count; command++) {
        const at = cursor, word = u16(), type = word & 7, target = word >>> 8;
        const record = { offset: at, word, type, target };
        if (type !== 7 && target >= parts.length) throw Error(`Part ${target} out of range at ${at}`);
        const part = parts[target];
        if (type === 1) {
          const axes = word & 0x38;
          if (!axes) {
            const wide = !!(word & 0x40);
            for (const axis of [0, 1, 2]) setTranslation(part, axis, wide ? i16() : i8(), false);
            for (const axis of [1, 0, 2]) setRotation(part, axis, wide ? u16() : i8(), false);
          } else {
            const rotate = !!(word & 0x80), absolute = !!(word & 0x40);
            for (let axis = 0; axis < 3; axis++) {
              if (word & [0x20, 0x10, 0x08][axis]) {
                if (rotate) setRotation(part, [1, 0, 2][axis], u16(), absolute);
                else setTranslation(part, axis, i16(), absolute);
              }
            }
          }
        } else if (type === 2) {
          throw Error(`Vertex deformation command not yet supported at ${at}`);
        } else if (type === 5) {
          // Runtime storage is a HIDDEN flag: 0 renders, 1 skips the mesh.
          visibility[target] = !!(word & 8);
        } else if (type === 6) {
          if (word & 0x80) {
            record.contextFields = { field26: u16(), field24: u16() };
            if ((word & 0x18) === 0x18) record.contextFields.field34 = u16();
          } else if (word & 0x40) {
            for (const axis of [1, 0, 2]) setRotation(part, axis, u16(), false);
          }
        } else if (type === 7) {
          if (word & 8) {
            throw Error(`Packed vertex deformation not yet supported at ${at}`);
          } else {
            let index = 0;
            for (let n = 0; n < target; n++, index++) {
              if (parts[index]?.parent === -1) index++;
              if (!parts[index]) throw Error(`Packed transform target out of range at ${at}`);
              const packed = u16(), translation = !!(packed & 1);
              let angles;
              if ((packed & 0xFE) === 0x7E) {
                const next = u16();
                angles = [signed(packed >>> 8, 8) << 4, signed(next >>> 8, 8) << 4, signed(next & 255, 8) << 4];
              } else {
                const shift = ((packed & 14) >>> 1) + 1;
                angles = [12, 8, 4].map(n => signed((packed >>> n) & 15, 4) << shift);
              }
              for (let axis = 0; axis < 3; axis++) setRotation(parts[index], [1, 0, 2][axis], angles[axis], false);
              if (translation) {
                const packed = u16(), shift = (packed & 14) >>> 1;
                for (let axis = 0; axis < 3; axis++)
                  setTranslation(parts[index], axis, signed((packed >>> [12, 8, 4][axis]) & 15, 4) << shift, false);
              }
            }
          }
        } else throw Error(`Unsupported SMA command ${type} at ${at}`);
        record.bytes = cursor - at;
        commands.push(record);
      }
      frames.push({ frame, offset: frameOffset, tag, commands, parts: structuredClone(parts), visibility: [...visibility] });
    }
    clips.push({ slot, start, end: cursor, limit, trailingBytes: limit - cursor, magic, version, reserved, frameCount, frames });
  }
  return clips;
}
