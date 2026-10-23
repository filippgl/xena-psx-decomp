// Original static-mesh subdivision, 800BEBC4. Keep vertex and face order:
// stable axis sort, majority-of-three partition, boundary vertex swaps, FIFO.
// Not Unity mesh optimization: the resulting order also feeds terrain queries.
export function originalDistance(exe) {
  const table = 0x800ce598 - 0x80010000 + 0x800;
  return (a, b) => {
    const d = a.map((n, i) => ((n - b[i]) << 16) >> 16);
    const squared = d.reduce((sum, n) => (sum + Math.imul(n, n)) | 0, 0);
    if (squared === 0) return 0;
    const leading = Math.clz32(squared < 0 ? ~squared : squared), even = leading & ~1;
    const normalized = even >= 24 ? squared << (even - 24) : squared >> (24 - even);
    const value = exe.readInt16LE(table + (normalized - 64) * 2);
    return (value << ((31 - even) >> 1)) >>> 12;
  };
}

export function subdivideStaticMesh(source, { radiusLimit, distance }) {
  return subdivideMeshLibrary([source], { radiusLimit, distance });
}

export function subdivideMeshLibrary(sources, { radiusLimit, distance }) {
  if (!Number.isInteger(radiusLimit) || radiusLimit <= 0 || typeof distance !== 'function')
    throw Error('Subdivision requires the original radius limit and distance implementation');
  const queue = sources.map(source => ({ sourceObject: source.index,
    vertices: source.vertices.map((position, sourceIndex) => ({ position, sourceIndex })),
    faces: source.faces.map((face, sourceIndex) => ({ indices: [...face.indices], sourceIndex })) })), leaves = [];
  for (let cursor = 0; cursor < queue.length; cursor++) {
    if (queue.length > 20000) throw Error('Subdivision failed to converge');
    const mesh = queue[cursor], n = mesh.vertices.length;
    let radius = 0, center = [0, 0, 0];
    if (n < 43) {
      for (let i = 1; i < n; i++) for (let j = 0; j < i; j++) {
        const a = mesh.vertices[i].position, b = mesh.vertices[j].position;
        const half = Math.trunc(distance(a, b) / 2);
        if (half > radius) { radius = half; center = a.map((v, axis) => Math.trunc((v + b[axis]) / 2)); }
      }
      for (let i = 1; i < n; i++) radius = Math.max(radius, distance(mesh.vertices[i].position, center));
    }
    if (mesh.faces.length < 2 || (n < 43 && radius < radiusLimit)) {
      if (mesh.faces.length) leaves.push(mesh);
      continue;
    }
    const spans = [0, 1, 2].map(axis => {
      const values = mesh.vertices.map(v => v.position[axis]);
      return Math.max(...values) - Math.min(...values);
    });
    // Original strict comparisons choose Z on ties, then Y over X.
    const axis = spans[1] < spans[0] ? (spans[2] < spans[0] ? 0 : 2) : (spans[2] < spans[1] ? 1 : 2);
    const order = Array.from({ length: n }, (_, i) => i).sort((a, b) => mesh.vertices[a].position[axis] - mesh.vertices[b].position[axis]);
    const children = [];
    let unsplit = false;
    for (let side = 0; side < 2; side++) {
      if (side) order.reverse();
      const rank = Array(n); order.forEach((v, i) => { rank[v] = i; });
      const midpoint = side ? Math.trunc((n + 1) / 2) : Math.trunc(n / 2);
      let count = midpoint;
      const faces = [];
      for (const face of mesh.faces) {
        if (face.indices.filter(i => rank[i] < midpoint).length < 2) continue;
        faces.push(face);
        for (const i of face.indices) {
          if (rank[i] < count) continue;
          const displaced = order[count], oldRank = rank[i];
          order[count] = i; order[oldRank] = displaced;
          rank[displaced] = oldRank; rank[i] = count++;
        }
      }
      if (count === n) { unsplit = true; break; }
      children.push({ sourceObject: mesh.sourceObject, vertices: order.slice(0, count).map(i => mesh.vertices[i]),
        faces: faces.map(face => ({ sourceIndex: face.sourceIndex, indices: face.indices.map(i => rank[i]) })) });
    }
    if (unsplit) leaves.push(mesh);
    else queue.push(...children);
  }
  return leaves;
}

export function buildTerrainGrid(leaves) {
  const all = leaves.flatMap(mesh => mesh.faces.map(face => ({ sourceIndex: face.sourceIndex,
    vertices: face.indices.map(i => mesh.vertices[i].position) })));
  const origin = [0, 2].map(axis => Math.min(...all.flatMap(f => f.vertices.map(v => v[axis]))));
  const buckets = Array.from({ length: 256 }, () => []);
  for (const face of all) {
    const bucket = axis => {
      const values = face.vertices.map(v => v[axis]);
      const center = Math.trunc((Math.min(...values) + Math.max(...values)) / 2);
      return Math.max(0, Math.min(15, Math.trunc((center - origin[axis === 0 ? 0 : 1]) / 4096)));
    };
    buckets[bucket(2) * 16 + bucket(0)].push(face);
  }
  // 800BD604/800BCBBC: stable ascending integer mean Y, not the sum.
  // Truncation creates extra ties whose initial subdivision order must survive.
  const meanY = face => Math.trunc(face.vertices.reduce((s, v) => s + v[1], 0) / 3);
  const triangles = [], sourceFaces = [];
  const cells = buckets.map(bucket => {
    bucket.sort((a, b) => meanY(a) - meanY(b));
    return bucket.map(face => { const index = triangles.length; triangles.push(face.vertices); sourceFaces.push(face.sourceIndex); return index; });
  });
  return { terrain: { origin, cells, triangles }, sourceFaces };
}
