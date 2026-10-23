import { deflateSync, inflateSync } from 'node:zlib';
const crcTable = Array.from({ length: 256 }, (_, n) => {
  for (let i = 0; i < 8; i++) n = n & 1 ? 0xedb88320 ^ (n >>> 1) : n >>> 1;
  return n >>> 0;
});
const chunk = (type, data) => {
  const name = Buffer.from(type), result = Buffer.alloc(data.length + 12);
  result.writeUInt32BE(data.length); name.copy(result, 4); data.copy(result, 8);
  let crc = 0xffffffff;
  for (const byte of Buffer.concat([name, data])) crc = crcTable[(crc ^ byte) & 255] ^ (crc >>> 8);
  result.writeUInt32BE((crc ^ 0xffffffff) >>> 0, result.length - 4);
  return result;
};
export function png(width, height, rgba) {
  if (rgba.length !== width * height * 4) throw Error('Invalid RGBA size');
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = 6;
  const raw = Buffer.alloc(height * (width * 4 + 1));
  for (let y = 0; y < height; y++) Buffer.from(rgba.buffer, rgba.byteOffset + y * width * 4, width * 4)
    .copy(raw, y * (width * 4 + 1) + 1);
  return Buffer.concat([Buffer.from('89504e470d0a1a0a', 'hex'), chunk('IHDR', header), chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}

export function readPng(bytes) {
  if (bytes.subarray(0, 8).toString('hex') !== '89504e470d0a1a0a') throw Error('Not a PNG');
  let width, height, depth, color, interlace;
  const compressed = [];
  for (let cursor = 8; cursor + 12 <= bytes.length;) {
    const length = bytes.readUInt32BE(cursor), type = bytes.toString('ascii', cursor + 4, cursor + 8);
    if (cursor + 12 + length > bytes.length) throw Error('Truncated PNG chunk');
    const data = bytes.subarray(cursor + 8, cursor + 8 + length);
    if (type === 'IHDR') {
      width = data.readUInt32BE(0); height = data.readUInt32BE(4); depth = data[8]; color = data[9]; interlace = data[12];
    } else if (type === 'IDAT') compressed.push(data);
    cursor += 12 + length;
    if (type === 'IEND') break;
  }
  if (!width || !height || width * height > 64000000 || depth !== 8 || ![2, 6].includes(color) || interlace)
    throw Error('Only noninterlaced 8-bit RGB/RGBA PNG supported');
  const channels = color === 6 ? 4 : 3, stride = width * channels;
  const raw = inflateSync(Buffer.concat(compressed), { maxOutputLength: (stride + 1) * height });
  if (raw.length !== (stride + 1) * height) throw Error('Invalid PNG scanline size');
  const reconstructed = Buffer.alloc(stride * height);
  const paeth = (a, b, c) => {
    const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
    return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
  };
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    if (filter > 4) throw Error('Invalid PNG filter');
    for (let x = 0; x < stride; x++) {
      const left = x >= channels ? reconstructed[y * stride + x - channels] : 0;
      const above = y ? reconstructed[(y - 1) * stride + x] : 0;
      const corner = y && x >= channels ? reconstructed[(y - 1) * stride + x - channels] : 0;
      const prediction = [0, left, above, Math.floor((left + above) / 2), paeth(left, above, corner)][filter];
      reconstructed[y * stride + x] = (raw[y * (stride + 1) + 1 + x] + prediction) & 255;
    }
  }
  const rgba = new Uint8Array(width * height * 4);
  for (let i = 0; i < width * height; i++) {
    rgba[i * 4] = reconstructed[i * channels]; rgba[i * 4 + 1] = reconstructed[i * channels + 1];
    rgba[i * 4 + 2] = reconstructed[i * channels + 2]; rgba[i * 4 + 3] = channels === 4 ? reconstructed[i * channels + 3] : 255;
  }
  return { width, height, rgba };
}
