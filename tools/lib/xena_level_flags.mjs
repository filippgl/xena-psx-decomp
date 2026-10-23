// Name-driven flags from original 800BAA3C..800BAD88. Read literals from EXE.
export const levelFlagMask = 0x7fff0;
const offsets = [0x4708, 0x470c, 0x4714, 0x471c, 0x4724, 0x472c, 0x4738,
  0x4740, 0x474c, 0x4754, 0x475c, 0x4764, 0x476c, 0x4774, 0x477c];
export function readLevelFlagTokens(exe) {
  return Object.fromEntries(offsets.map(offset => {
    const at = 0xec210 + offset - 0x10000 + 0x800;
    const end = exe.indexOf(0, at);
    if (end < at || end > at + 16) throw Error('Invalid original flag token');
    return [offset, exe.subarray(at, end).toString('ascii')];
  }));
}

// Preserve original BFB70's forward-only partial match. String.includes differs
// for overlapping candidates such as "ttree" and must not replace this helper.
export function originalNameContains(name, token) {
  let matched = 0;
  for (const character of name) {
    if (character === token[matched]) { if (++matched === token.length) return true; }
    else matched = 0;
  }
  return false;
}

export function classifyLevelName(name, tokens, initialFlags = 1) {
  const has = offset => originalNameContains(name, tokens[offset]);
  let flags = initialFlags;
  const set = (bit, condition) => { flags = condition ? flags | bit : flags & ~bit; };
  set(0x8000, !has(0x4708)); set(0x10, has(0x470c)); set(0x20, name[0] === '@');
  for (const [offset, bit] of [[0x4714, 0x40], [0x471c, 0x80], [0x4724, 0x200], [0x472c, 0x400]]) set(bit, has(offset));
  if (has(0x4738)) {
    const before = flags; flags |= 0x100;
    if (flags & 0x8000) flags &= ~(0x8000 | 0x10);
    else flags = ((before | 0x110) & ~0x100) | 0x400;
  } else flags &= ~0x100;
  set(0x10000, has(0x4740) || has(0x474c) || has(0x4754));
  for (const [offset, bit] of [[0x475c, 0x800], [0x4764, 0x1000], [0x476c, 0x2000], [0x4774, 0x4000], [0x4754, 0x20000], [0x477c, 0x40000]]) set(bit, has(offset));
  return flags >>> 0;
}
