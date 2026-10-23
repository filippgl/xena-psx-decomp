// Shared by the offline viewer and deterministic PNG rendering. No dependencies.
globalThis.XenaPreviewRenderer = (() => {
  function render(data, matrices, visibility, options = {}) {
    const width = options.width || 640, height = options.height || 720;
    const pixels = new Uint8ClampedArray(width * height * 4);
    const depth = new Float64Array(width * height).fill(-Infinity);
    for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
      const at = (y * width + x) * 4, gradient = 14 + Math.round(y * 9 / height);
      pixels.set([gradient, gradient + 5, gradient + 10, 255], at);
    }
    const yaw = options.yaw ?? Math.PI + 0.25, pitch = options.pitch ?? -0.08;
    const cy = Math.cos(yaw), sy = Math.sin(yaw), cp = Math.cos(pitch), sp = Math.sin(pitch);
    const scale = Math.min(width / 1250, height / 1250) * (options.zoom || 1.5);
    const project = p => {
      const x = p[0] - data.center[0], y = -(p[1] - data.center[1]), z = p[2] - data.center[2];
      const rx = cy * x + sy * z, rz = -sy * x + cy * z;
      return [width / 2 + rx * scale, height / 2 - (cp * y - sp * rz) * scale, sp * y + cp * rz];
    };
    const world = (m, p) => [0, 1, 2].map(row => m[9 + row] +
      (m[row * 3] * p[0] + m[row * 3 + 1] * p[1] + m[row * 3 + 2] * p[2]) / 4096);
    let triangles = 0, fragments = 0;
    for (const object of data.objects) {
      if (visibility && !visibility[object.index]) continue;
      const matrix = matrices[object.index];
      const points = object.vertices.map(v => project(world(matrix, v)));
      for (const face of object.faces) {
        const [a, b, c] = face.indices.map(i => points[i]);
        const area = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1]);
        if (Math.abs(area) < 0.0001) continue;
        const minX = Math.max(0, Math.floor(Math.min(a[0], b[0], c[0]))), maxX = Math.min(width - 1, Math.ceil(Math.max(a[0], b[0], c[0])));
        const minY = Math.max(0, Math.floor(Math.min(a[1], b[1], c[1]))), maxY = Math.min(height - 1, Math.ceil(Math.max(a[1], b[1], c[1])));
        const texture = face.material === undefined ? null : data.textures[face.material];
        const lights = options.vertexLighting && object.lightingRgb ? face.indices.map(i =>
          object.lightingRgb[i].map(value => Math.max(0, Math.min(255, value + (options.lightBias || 0))))) : null;
        triangles++;
        for (let y = minY; y <= maxY; y++) for (let x = minX; x <= maxX; x++) {
          const px = x + 0.5, py = y + 0.5;
          const u = ((b[1] - c[1]) * (px - c[0]) + (c[0] - b[0]) * (py - c[1])) / area;
          const v = ((c[1] - a[1]) * (px - c[0]) + (a[0] - c[0]) * (py - c[1])) / area;
          const w = 1 - u - v;
          if (u < 0 || v < 0 || w < 0) continue;
          const z = a[2] * u + b[2] * v + c[2] * w, at = y * width + x;
          if (z < depth[at]) continue;
          let color = face.color || [200, 200, 200, 255];
          if (texture) {
            const tw = texture.width || 256, th = texture.height || 256;
            const tx = Math.max(0, Math.min(tw - 1, Math.floor((face.uv[0][0] * u + face.uv[1][0] * v + face.uv[2][0] * w + 0.5) * tw / 256)));
            const ty = Math.max(0, Math.min(th - 1, Math.floor((face.uv[0][1] * u + face.uv[1][1] * v + face.uv[2][1] * w + 0.5) * th / 256)));
            const t = (ty * tw + tx) * 4;
            if (!texture.rgba[t + 3]) continue;
            color = texture.rgba.subarray(t, t + 4);
          }
          if (lights) {
            color = [0, 1, 2].map(channel => Math.min(255, Math.max(0, Math.floor(color[channel] *
              (lights[0][channel] * u + lights[1][channel] * v + lights[2][channel] * w) / 128))));
          }
          pixels[at * 4] = color[0]; pixels[at * 4 + 1] = color[1]; pixels[at * 4 + 2] = color[2]; pixels[at * 4 + 3] = 255;
          depth[at] = z; fragments++;
        }
      }
    }
    if (options.skeleton) {
      const plot = (x, y) => {
        if (x >= 0 && x < width && y >= 0 && y < height) pixels.set([70, 232, 212, 255], (y * width + x) * 4);
      };
      data.parents.forEach((parent, i) => {
        if (parent === -1) return;
        const a = project(matrices[i].slice(9)), b = project(matrices[parent].slice(9));
        const count = Math.ceil(Math.max(Math.abs(a[0] - b[0]), Math.abs(a[1] - b[1])));
        for (let n = 0; n <= count; n++) plot(Math.round(a[0] + (b[0] - a[0]) * n / (count || 1)), Math.round(a[1] + (b[1] - a[1]) * n / (count || 1)));
      });
    }
    const markers = [];
    for (const marker of options.markers || []) {
      const point = project(marker.position), x = Math.round(point[0]), y = Math.round(point[1]);
      if (x < 0 || y < 0 || x >= width || y >= height) continue;
      for (let dy = -4; dy <= 4; dy++) for (let dx = -4; dx <= 4; dx++) {
        if (dx * dx + dy * dy > 16 || x + dx < 0 || y + dy < 0 || x + dx >= width || y + dy >= height) continue;
        const outline = dx * dx + dy * dy > 8;
        pixels.set([...(outline ? [16, 22, 28] : marker.color), 255], ((y + dy) * width + x + dx) * 4);
      }
      markers.push({ index: marker.index, x, y });
    }
    return { width, height, rgba: pixels, triangles, fragments, markers };
  }
  return { render };
})();
