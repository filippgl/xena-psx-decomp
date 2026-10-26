#!/usr/bin/env python3
"""Identify exported SFX renders inside a native-port audio capture (docs/xena-sfx.md, verification).

The native port records with `xena-console.exe --headless --frames N --script S --wav FILE`. Two runs
that differ only by one input are sample-identical until that input, so EVENT - BASE isolates the
sounds it caused (use "-" as BASE to search a single capture). Every render in DIR (default pattern
0x*.wav, env PAT) is cross-correlated with the isolated left channel between t0 and t1; the score is
the normalised correlation at the best offset, the gain is native/render amplitude there.

  python tools/sfx_match.py BASE.wav EVENT.wav build/sfx/export t0 t1 [--top 6] [--exact]
"""
import glob, os, sys, wave
import numpy as np


def load(p):
    w = wave.open(p)
    a = np.frombuffer(w.readframes(w.getnframes()), '<i2').reshape(-1, w.getnchannels()).astype(float)
    return a if a.shape[1] == 2 else np.repeat(a, 2, axis=1)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    top = int(sys.argv[sys.argv.index('--top') + 1]) if '--top' in sys.argv else 6
    if '--top' in sys.argv: args.remove(str(top))
    base, event, folder, t0, t1 = args[0], args[1], args[2], float(args[3]), float(args[4])
    e = load(event); b = np.zeros_like(e) if base == '-' else load(base)
    m = min(len(b), len(e)); d = e[:m] - b[:m]
    i0, i1 = int(t0 * 44100), int(t1 * 44100)
    y = d[i0:i1, 0]
    c2 = np.concatenate([[0], np.cumsum(y ** 2)])
    res = []
    for f in sorted(glob.glob(os.path.join(folder, os.environ.get('PAT', '0x*.wav')))):
        if '_raw' in f or '_fullrecording' in f: continue
        r = load(f); x = r[:44100 * 3, 0]
        ex = float(np.dot(x, x))
        if ex == 0: continue
        n = 1 << int(np.ceil(np.log2(len(y) + len(x))))
        cc = np.fft.irfft(np.fft.rfft(y, n) * np.conj(np.fft.rfft(x, n)), n)[:len(y)]
        idx = np.arange(len(y)); ey = c2[np.minimum(idx + len(x), len(y))] - c2[idx]
        nc = cc / np.sqrt(ey * ex + 1e-9); nc[ey < 0.02 * ey.max()] = 0
        k = int(np.argmax(nc))
        res.append((float(nc[k]), os.path.basename(f), i0 + k, float(cc[k] / ex), r))
    res.sort(key=lambda t: -t[0])
    for score, name, at, gain, _ in res[:top]:
        print(f"{score:.6f}  {name}  start {at / 44100:.5f}s (sample {at}, frame {at / 735:.2f})  gain {gain:.4f}")
    if '--exact' in sys.argv and res:
        score, name, at, gain, r = res[0]
        seg = d[at:at + len(r)]; r = r[:len(seg)]
        resid = seg - gain * r
        print(f"best: corr {np.corrcoef(seg[:, 0], r[:, 0])[0, 1]:.6f}, residual/native rms {np.sqrt((resid ** 2).mean() / (seg ** 2).mean()):.5f}, "
              f"samples within 1 LSB of gain*render {np.mean(np.abs(seg - np.round(gain * r)) <= 1) * 100:.1f}%, native L/R rms "
              f"{np.sqrt((seg[:, 0] ** 2).mean()):.0f}/{np.sqrt((seg[:, 1] ** 2).mean()):.0f}")


if __name__ == '__main__':
    main()
