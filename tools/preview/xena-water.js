// Original 800AD94C integer sine interpolation and level01 water displacement.
// Shared by browser and native-code verification; no wall-clock timing assumed.
globalThis.XenaWater = (() => {
  const short = n => (n << 16) >> 16;
  function sine(table, phase) {
    phase &= 65535;
    const quadrant = phase & 0xc000;
    let local = quadrant === 0x4000 ? 0x8000 - phase : quadrant === 0x8000 ? phase & 0x7fff : quadrant === 0xc000 ? -phase : phase;
    local = short(local);
    const index = Math.trunc(local / 64), a = table[index], b = table[index + 1];
    const interpolated = a + Math.trunc(short((b - a) * (local & 63)) / 64);
    return short(quadrant & 0x8000 ? -interpolated : interpolated);
  }
  function height(table, x, base, phase) {
    return short(base + Math.trunc(sine(table, phase + ((x & 7) << 13)) / 256));
  }
  return { sine, height };
})();
