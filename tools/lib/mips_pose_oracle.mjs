// Small validation interpreter for selected ORIGINAL game routines (pose,
// transforms, terrain and asset helpers). Not a game runtime or full MIPS emulator.
export class PoseOracle {
  constructor(exe, smi) {
    this.memory = Buffer.alloc(2 * 1024 * 1024);
    this.scratch = Buffer.alloc(1024);
    this.gteData = new Int32Array(32);
    this.gteControl = new Int32Array(32);
    exe.copy(this.memory, 0x10000, 0x800);
    this.object = 0x80110000;
    this.context = 0x80110200;
    this.transforms = 0x80110400;
    this.command = 0x80120000;
    this.count = Math.floor(smi.length / 20);
    smi.copy(this.memory, this.transforms & 0x1fffff, 0, this.count * 20);
    this.write32(this.object + 0xA8, this.context);
    this.write32(this.context + 0x14, this.transforms);
    this.write16(this.context + 0x20, this.count);
  }
  offset(address, size) {
    const at = address & 0x1fffff;
    if (at + size > this.memory.length) throw Error('Oracle memory overflow');
    return at;
  }
  region(address, size) {
    if ((address >>> 10) === (0x1f800000 >>> 10)) return [this.scratch, address & 1023];
    return [this.memory, this.offset(address, size)];
  }
  read(address, size, signed = false) {
    const [buffer, at] = this.region(address, size);
    if (size === 4) return signed ? buffer.readInt32LE(at) : buffer.readUInt32LE(at);
    if (size === 2) return signed ? buffer.readInt16LE(at) : buffer.readUInt16LE(at);
    return signed ? buffer.readInt8(at) : buffer.readUInt8(at);
  }
  write(address, value, size) {
    const [buffer, at] = this.region(address, size);
    if (size === 4) buffer.writeUInt32LE(value >>> 0, at);
    else if (size === 2) buffer.writeUInt16LE(value & 65535, at);
    else buffer.writeUInt8(value & 255, at);
  }
  read32(address) { return this.read(address, 4); }
  write32(address, value) { this.write(address, value, 4); }
  write16(address, value) { this.write(address, value, 2); }
  run(bytes) {
    bytes.copy(this.memory, this.command & 0x1fffff);
    const result = this.execute(0x80069040, [this.command, this.object]);
    return { consumed: (result.result - this.command) >>> 0, steps: result.steps };
  }
  execute(entry, args, { registers = {}, stopAt = null } = {}) {
    const r = new Uint32Array(32);
    const stop = 0x0badf00d;
    for (let i = 0; i < args.length; i++) r[4 + i] = args[i];
    r[28] = 0x800EC210; r[29] = 0x801ff000; r[31] = stop;
    for (const [index, value] of Object.entries(registers)) {
      if (!Number.isInteger(Number(index)) || Number(index) < 1 || Number(index) > 31) throw Error('Invalid oracle register override');
      r[Number(index)] = value;
    }
    let pc = entry, pending = null, steps = 0, lo = 0, hi = 0;
    while (pc !== stop && pc !== stopAt) {
      if (++steps > 100000) throw Error('Oracle instruction limit');
      // Type 5 invokes the mesh lookup, but only updates a visibility byte after it.
      if (pc === 0x800A5300) { r[2] = 0x80118000; pc = r[31]; continue; }
      if (pc === 0x800AE5C0) throw Error(`Original game assertion ${r[4].toString(16)}`);
      const instruction = this.read32(pc);
      const op = instruction >>> 26, rs = (instruction >>> 21) & 31,
        rt = (instruction >>> 16) & 31, rd = (instruction >>> 11) & 31,
        sh = (instruction >>> 6) & 31, fn = instruction & 63,
        imm = instruction & 65535, simm = (imm << 16) >> 16;
      const previous = pending;
      pending = null;
      const branch = condition => { pending = condition ? (pc + 4 + simm * 4) >>> 0 : (pc + 8) >>> 0; };
      const address = (r[rs] + simm) >>> 0;
      if (op === 0) {
        if (fn === 0) r[rd] = r[rt] << sh;
        else if (fn === 2) r[rd] = r[rt] >>> sh;
        else if (fn === 3) r[rd] = (r[rt] | 0) >> sh;
        else if (fn === 4) r[rd] = r[rt] << (r[rs] & 31);
        else if (fn === 8) pending = r[rs];
        else if (fn === 0x12) r[rd] = lo;
        else if (fn === 0x18 || fn === 0x19) lo = Math.imul(r[rs], r[rt]);
        else if (fn === 0x1a) {
          const numerator = r[rs] | 0, denominator = r[rt] | 0;
          if (denominator === 0) { lo = numerator < 0 ? 1 : -1; hi = numerator; }
          else { lo = Math.trunc(numerator / denominator) | 0; hi = numerator % denominator; }
        }
        else if (fn === 0x20 || fn === 0x21) r[rd] = r[rs] + r[rt];
        else if (fn === 0x22 || fn === 0x23) r[rd] = r[rs] - r[rt];
        else if (fn === 0x24) r[rd] = r[rs] & r[rt];
        else if (fn === 0x25) r[rd] = r[rs] | r[rt];
        else if (fn === 0x26) r[rd] = r[rs] ^ r[rt];
        else if (fn === 0x2a) r[rd] = (r[rs] | 0) < (r[rt] | 0) ? 1 : 0;
        else if (fn === 0x2b) r[rd] = r[rs] < r[rt] ? 1 : 0;
        else throw Error(`Unsupported R instruction ${fn.toString(16)} at ${pc.toString(16)}`);
      } else if (op === 1) {
        if (rt !== 0 && rt !== 1) throw Error('Unsupported REGIMM');
        branch(rt === 0 ? (r[rs] | 0) < 0 : (r[rs] | 0) >= 0);
      } else if (op === 2 || op === 3) {
        if (op === 3) r[31] = pc + 8;
        pending = (((pc + 4) & 0xF0000000) | ((instruction & 0x3ffffff) << 2)) >>> 0;
      } else if (op === 4) branch(r[rs] === r[rt]);
      else if (op === 5) branch(r[rs] !== r[rt]);
      else if (op === 6) branch((r[rs] | 0) <= 0);
      else if (op === 7) branch((r[rs] | 0) > 0);
      else if (op === 8 || op === 9) r[rt] = r[rs] + simm;
      else if (op === 0xa) r[rt] = (r[rs] | 0) < simm ? 1 : 0;
      else if (op === 0xb) r[rt] = r[rs] < (simm >>> 0) ? 1 : 0;
      else if (op === 0xc) r[rt] = r[rs] & imm;
      else if (op === 0xd) r[rt] = r[rs] | imm;
      else if (op === 0xf) r[rt] = imm << 16;
      else if (op === 0x12) {
        if (!(instruction & 0x2000000)) {
          if (rs === 0) r[rt] = this.gteData[rd];
          else if (rs === 2) r[rt] = this.gteControl[rd];
          else if (rs === 4) this.gteData[rd] = rd >= 8 && rd <= 11 ? (r[rt] << 16) >> 16 : r[rt];
          else if (rs === 6) this.gteControl[rd] = r[rt];
          else throw Error('Unsupported COP2 register operation');
        } else {
          const command = instruction & 63;
          const shift = instruction & (1 << 19) ? 12 : 0;
          const saturate = v => Math.min(32767, Math.max(-32768, v));
          const d = this.gteData, c = this.gteControl;
          let values;
          if (command === 0xc) {
            const diagonal = [c[0], c[2], c[4]].map(n => (n << 16) >> 16);
            values = [diagonal[1] * d[11] - diagonal[2] * d[10],
              diagonal[2] * d[9] - diagonal[0] * d[11],
              diagonal[0] * d[10] - diagonal[1] * d[9]].map(n => Math.floor(n / 2 ** shift));
          }
          else if (command === 0x3d) values = [9, 10, 11].map(i => Math.floor(d[8] * d[i] / 2 ** shift));
          else if (command === 0x12) {
            const vector = (instruction >>> 15) & 3, cv = (instruction >>> 13) & 3;
            if (((instruction >>> 17) & 3) !== 0 || (vector !== 0 && vector !== 3) || (cv !== 0 && cv !== 3))
              throw Error('Unsupported MVMVA mode');
            const v = vector === 3 ? [d[9], d[10], d[11]] : [(d[0] << 16) >> 16, d[0] >> 16, (d[1] << 16) >> 16];
            values = [0, 1, 2].map(row => {
              let value = cv === 0 ? c[5 + row] * 4096 : 0;
              for (let col = 0; col < 3; col++) {
                const i = row * 3 + col;
                const coeff = i & 1 ? c[i >>> 1] >> 16 : (c[i >>> 1] << 16) >> 16;
                value += coeff * v[col];
              }
              return Math.floor(value / 2 ** shift);
            });
          } else throw Error(`Unsupported GTE command ${command.toString(16)}`);
          values.forEach((value, i) => { d[25 + i] = value; d[9 + i] = saturate(value); });
        }
      }
      else if (op === 0x20) r[rt] = this.read(address, 1, true);
      else if (op === 0x21) r[rt] = this.read(address, 2, true);
      else if (op === 0x23) r[rt] = this.read32(address);
      else if (op === 0x24) r[rt] = this.read(address, 1);
      else if (op === 0x25) r[rt] = this.read(address, 2);
      else if (op === 0x28) this.write(address, r[rt], 1);
      else if (op === 0x29) this.write16(address, r[rt]);
      else if (op === 0x2b) this.write32(address, r[rt]);
      else if (op === 0x32) this.gteData[rt] = this.read32(address);
      else if (op === 0x3a) this.write32(address, this.gteData[rt]);
      else throw Error(`Unsupported opcode ${op.toString(16)} at ${pc.toString(16)}`);
      r[0] = 0;
      pc = previous === null ? (pc + 4) >>> 0 : previous;
    }
    return { result: r[2], steps };
  }
  parts() {
    return Array.from({ length: this.count }, (_, i) => {
      const at = (this.transforms & 0x1fffff) + i * 20;
      return { translation: [0, 4, 8].map(n => this.memory.readInt32LE(at + n)),
        rotation: [12, 14, 16].map(n => this.memory.readInt16LE(at + n)),
        parent: this.memory.readInt16LE(at + 18) };
    });
  }
}
