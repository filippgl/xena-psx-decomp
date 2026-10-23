const signed16 = value => (value << 16) >> 16;
const product = (a, b) => Math.floor(a * b / 4096);
export const identity = () => ({ rotation: [4096, 0, 0, 0, 4096, 0, 0, 0, 4096], translation: [0, 0, 0] });

// The game's own trig table and operation ordering from func_80014BE4.
// func_80014E78 negates the first SMI rotation component before calling it.
export function localMatrix(part, trig) {
  const lookup = angle => {
    angle = signed16(angle);
    const i = Math.abs(angle) & 4095;
    return [trig[i * 2] * (angle < 0 ? -1 : 1), trig[i * 2 + 1]];
  };
  const [sx, cx] = lookup(-part.rotation[0]);
  const [sy, cy] = lookup(part.rotation[1]);
  const [sz, cz] = lookup(part.rotation[2]);
  const cysx = product(cy, sx), sysx = product(sy, sx);
  return { rotation: [
    product(cy, cz) + product(sz, sysx), product(cz, sysx) - product(cy, sz), product(cx, sy),
    product(sz, cx), product(cz, cx), -sx,
    product(sz, cysx) - product(sy, cz), product(cz, cysx) + product(sy, sz), product(cx, cy),
  ].map(signed16), translation: [...part.translation] };
}

export function compose(parent, local) {
  const rotation = Array.from({ length: 9 }, (_, i) => {
    const row = Math.floor(i / 3), col = i % 3;
    let sum = 0;
    for (let k = 0; k < 3; k++) sum += parent.rotation[row * 3 + k] * local.rotation[k * 3 + col];
    return Math.min(32767, Math.max(-32768, Math.floor(sum / 4096)));
  });
  // Original composition passes each local translation component through s16 GTE V0.
  const translation = parent.translation.map((t, row) => {
    let sum = t * 4096;
    for (let col = 0; col < 3; col++) sum += parent.rotation[row * 3 + col] * signed16(local.translation[col]);
    return Math.floor(sum / 4096) | 0;
  });
  return { rotation, translation };
}

export function worldMatrices(parts, trig, external = identity()) {
  const matrices = new Array(parts.length), visiting = new Set();
  function resolve(index) {
    if (matrices[index]) return matrices[index];
    if (!parts[index] || visiting.has(index)) throw Error('Invalid transform hierarchy');
    visiting.add(index);
    const part = parts[index], local = localMatrix(part, trig);
    matrices[index] = compose(part.parent === -1 ? external : resolve(part.parent), local);
    visiting.delete(index);
    return matrices[index];
  }
  parts.forEach((_, i) => resolve(i));
  return matrices;
}

export function transformPoint(matrix, point) {
  return matrix.translation.map((t, row) => t + matrix.rotation.slice(row * 3, row * 3 + 3)
    .reduce((s, coefficient, col) => s + coefficient * point[col] / 4096, 0));
}

export function readTrig(exe) {
  const start = 0x800CE8B8 - 0x80010000 + 0x800;
  if (exe.length < start + 4096 * 4) throw Error('Missing trig table');
  return Array.from({ length: 8192 }, (_, i) => exe.readInt16LE(start + i * 2));
}
