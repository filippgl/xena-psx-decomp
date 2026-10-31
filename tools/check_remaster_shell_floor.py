#!/usr/bin/env python3
"""Independent check of the baked shell: at reachable standing points, the drawn ground height against the original
terrain height (what the feet use). Reads only the baked level.xenashell and the original level data.

usage: ../_tools/remaster-venv/Scripts/python tools/check_remaster_shell_floor.py [--samples 6000]
Exit 1 when more than 1% of the points are off by over 10 cm, or any drawn surface hides the feet by over 30 cm."""
import argparse, collections, json, struct, sys
import numpy as np
sys.path.insert(0, "tools")
import build_remaster_shell as b


def load_shell():
    data = open(b.OUT + "level.xenashell", "rb").read(); assert data[:4] == b"XSH2"
    tiles = struct.unpack_from("<I", data, 4)[0]; at = 8; tris = []
    for _ in range(tiles):
        nv, nt = struct.unpack_from("<II", data, at); at += 8
        V = np.frombuffer(data, np.float32, nv * 3, at).reshape(-1, 3); at += nv * 12 + nv * 12 + nv * 4 + nv * 4
        F = np.frombuffer(data, np.uint32, nt * 3, at).reshape(-1, 3); at += nt * 12
        tris.append(V[F])
    return np.concatenate(tris)


def heights(tris, points):
    """All surface heights of triangles over each xz point, through a 1 m bin grid."""
    lo = tris[:, :, [0, 2]].min(1); hi = tris[:, :, [0, 2]].max(1); bins = collections.defaultdict(list)
    wanted = {(int(np.floor(x)), int(np.floor(z))) for x, z in points}
    for f in range(len(tris)):
        for i in range(int(np.floor(lo[f, 0])), int(np.floor(hi[f, 0])) + 1):
            for k in range(int(np.floor(lo[f, 1])), int(np.floor(hi[f, 1])) + 1):
                if (i, k) in wanted: bins[(i, k)].append(f)
    out = []
    for x, z in points:
        ids = bins.get((int(np.floor(x)), int(np.floor(z))), []); ys = []
        for f in ids:
            a, c, d = tris[f]; det = (c[2] - d[2]) * (a[0] - d[0]) + (d[0] - c[0]) * (a[2] - d[2])
            if abs(det) < 1e-9: continue
            l1 = ((c[2] - d[2]) * (x - d[0]) + (d[0] - c[0]) * (z - d[2])) / det; l2 = ((d[2] - a[2]) * (x - d[0]) + (a[0] - d[0]) * (z - d[2])) / det
            if min(l1, l2, 1 - l1 - l2) >= -1e-4: ys.append(l1 * a[1] + l2 * c[1] + (1 - l1 - l2) * d[1])
        out.append(ys)
    return out


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--samples", type=int, default=6000); args = ap.parse_args()
    tri, uv, _, mat, materials, page = b.load_terrain(); n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]); un = n / np.maximum(np.linalg.norm(n, axis=1), 1e-9)[:, None]
    pts = b.flood(tri, un); keep = np.random.default_rng(7).choice(len(pts), min(args.samples, len(pts)), replace=False); pts = pts[keep]
    # Real standing places only: the collider keeps the capsule centre this far from any wall.
    from scipy.spatial import cKDTree
    steep = tri[np.abs(un[:, 1]) < .5]; w = np.random.default_rng(1).dirichlet((1, 1, 1), 40); kd = cKDTree(np.concatenate([w @ t for t in steep]))
    clear = np.min([kd.query(pts + [0, h, 0], workers=-1)[0] for h in (.42, .85, 1.3, 1.7)], axis=0); pts = pts[clear >= b.CAPSULE_RADIUS]
    # Faces the shell does not replace draw themselves: water, cut-out cards, the dry-stone walls (page 8).
    weights = np.random.default_rng(5).dirichlet((1, 1, 1), 60); cut = np.array([(b.sample_page(page, mat[f], weights @ uv[f])[:, 3] < 128).mean() for f in range(len(tri))])
    water = np.array([materials[m]["name"].startswith(("page-9", "page-17", "page-8")) for m in mat]) | ((cut > .25) & ~np.array([materials[m]["name"].startswith("page-5") for m in mat]))
    flat_y = heights(tri[un[:, 1] >= .7], pts[:, [0, 2]]); original = heights(tri, pts[:, [0, 2]]); water_y = heights(tri[water], pts[:, [0, 2]]); shell = heights(load_shell(), pts[:, [0, 2]])
    errors, missing, skipped, hidden = [], 0, 0, 0; where = collections.Counter(); slope_errors = []
    bad = []
    for p, ys, wy, sy, fy in zip(pts, original, water_y, shell, flat_y):
        if not ys: skipped += 1; continue
        floor = min(ys, key=lambda y: abs(y - p[1]))
        if any(abs(floor - w) < .02 for w in wy): skipped += 1; continue  # water faces stay the original's, the shell stands back
        if not sy: missing += 1; where[(int(p[0] // 5) * 5, int(p[2] // 5) * 5)] += 1; continue
        d = min(sy, key=lambda y: abs(y - floor)) - floor
        # Slopes steeper than 45 degrees are banks the height query can touch, not ground: reported apart.
        if not any(abs(floor - y) < .02 for y in fy): slope_errors.append(d); continue
        errors.append(d)
        if abs(d) > .1: where[(int(p[0] // 5) * 5, int(p[2] // 5) * 5)] += 1
        if d > .3: hidden += 1
        if abs(d) > .1: bad.append((round(float(p[0]), 1), round(float(floor), 2), round(float(p[2]), 1), round(float(d), 2), [round(float(y), 2) for y in sorted(sy)][:6]))
    e = np.abs(errors); over = float((e > .1).mean()); report = dict(points=len(errors), skipped_water_or_card=skipped, no_shell_surface=missing, p50_m=float(np.percentile(e, 50)), p99_m=float(np.percentile(e, 99)), max_m=float(e.max()), over_10cm_share=over, feet_hidden_over_30cm=hidden, steep_points=len(slope_errors), steep_p50_m=float(np.percentile(np.abs(slope_errors), 50)) if slope_errors else 0, steep_p99_m=float(np.percentile(np.abs(slope_errors), 99)) if slope_errors else 0, worst_cells=where.most_common(8), examples=bad[:14])
    print(json.dumps(report, indent=1, default=str)); json.dump(report, open(b.REPORT_DIR + "floor-check.json", "w"), indent=1, default=str)
    sys.exit(0 if over <= .01 and hidden == 0 and missing == 0 else 1)


if __name__ == "__main__":
    main()
