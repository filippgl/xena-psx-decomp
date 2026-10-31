#!/usr/bin/env python3
"""Bake the closed Remaster shell of level 01 (presentation only).

The level's terrain is one single-sided sheet: its front is air, its back is empty. This tool treats the back as
solid rock: a signed distance field of the sheet (Baerentzen-Aanaes pseudo-normal sign) is capped at the sheet's top
and a hill outside it, CC0 rock scans are melted into it, the swept volume of the player's capsule is carved back out,
and one mesh is extracted with marching cubes. The mesh is closed by construction, so no T-junction, removed face or
culled back can open a hole. Walkable faces are snapped back to the exact original planes; colliders and the height
query are not touched. Vertex colours carry the low-frequency original look (the painted texture times the baked
light, blurred across the atlas mosaic), so the palette and the layout of path, moss and rock stay the original's.

Prominent shapes stay: rock may stand only centimetres in front of the original surface and relief goes inward, no
top rises over the original rim, and hills grow only behind painted forest. The bake fails if an oracle does not hold.

usage: ../_tools/remaster-venv/Scripts/python tools/build_remaster_shell.py [--cell 0.125] [--triangles 3500000] [--no-rocks]
needs: tools/fetch_remaster_shell_assets.py; output in Resources/Remaster/Shell (local data, not committed).
"""
import argparse, collections, json, os, struct, time
import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.interpolate import griddata
import pyfqmr
from scipy.spatial import cKDTree
from skimage import measure

PROJECT = "unity/XenaLightingVerification/Assets/Imported"
LEVEL = PROJECT + "/Level01/"
OUT = PROJECT + "/Resources/Remaster/Shell/"
SCANS = "../_tools/remaster-scans/"
REPORT_DIR = "build/remaster-shell/"
# The shell covers the whole level; ZCUT lies beyond it (the region test stays for a later partial bake).
ZCUT = 1e4
TILE = 24.0
SPAWN = np.array([-20.245, 4.6, -45.955])
CAPSULE_RADIUS = 0.28
# The trail the original paints on the south wall (page 16): a real path now leaves through the wall into the forest.
# The original wall collider stays, so like the painted path it can be seen, not walked.
TRAIL_FOOT, TRAIL_OUT, TRAIL_LENGTH, TRAIL_WIDTH = np.array([-20.6, 4.58, -50.7]), np.array([.3, 0, -.954]), 17.0, 1.0
rng = np.random.default_rng(2026)


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def qmat(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)], [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)], [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def load_terrain():
    doc = json.load(open(LEVEL + "Level01.xenamodel"))
    nodes, materials = doc["nodes"], doc["materials"]

    def world(i):
        n = nodes[i]; R = qmat(n["rotation"]) * np.array(n["scale"]); T = np.array(n["translation"], float)
        if n.get("parent", -1) < 0: return R, T
        Rp, Tp = world(n["parent"]); return Rp @ R, Rp @ T + Tp
    index = next(i for i, n in enumerate(nodes) if n["name"] == "1-terr_1")
    R, T = world(index)
    tri, uv, light, mat = [], [], [], []
    for p in doc["meshes"][nodes[index]["mesh"]]["primitives"]:
        P = np.array(p["positions"], float).reshape(-1, 3) @ R.T + T; I = np.array(p["indices"], int).reshape(-1, 3)
        tri.append(P[I]); uv.append(np.array(p["uv"], float).reshape(-1, 2)[I]); light.append(np.array(p["lightingRgb"], float).reshape(-1, 3)[I]); mat.append(np.full(len(I), p["material"]))
    pages = {}

    def page(m):
        name = materials[m]["name"].split("-ps1")[0].replace("-double", "")
        if name not in pages: pages[name] = np.array(Image.open(LEVEL + name + ".png").convert("RGBA"))
        return pages[name]
    return np.vstack(tri), np.vstack(uv), np.vstack(light), np.concatenate(mat), materials, page


def sample_page(page, m, uv):
    # Atlas V runs bottom-up: image row = h - 1 - v * h.
    im = page(m); h, w = im.shape[:2]
    x = np.clip((uv[:, 0] * w).astype(int), 0, w - 1); y = h - 1 - np.clip((uv[:, 1] * h).astype(int), 0, h - 1)
    return im[y, x]


def flood(tri, un):
    """Standing cells reachable from the spawn with the prototype's own limits (0.75 m step, 5 m fatal drop)."""
    cls = np.array(json.load(open(LEVEL + "CollisionMesh.json"))["vertices"], float).reshape(-1, 3)
    cls = (np.c_[cls[:, 0], -cls[:, 1], cls[:, 2]] / 400).reshape(-1, 3, 3)
    cell = 0.25; x0, z0 = tri[:, :, 0].min() - 1, tri[:, :, 2].min() - 1
    cand, blocks = collections.defaultdict(list), collections.defaultdict(list)
    for f, (a, b, c) in enumerate(tri):
        i0, i1 = int((min(a[0], b[0], c[0]) - x0) / cell), int((max(a[0], b[0], c[0]) - x0) / cell) + 1
        k0, k1 = int((min(a[2], b[2], c[2]) - z0) / cell), int((max(a[2], b[2], c[2]) - z0) / cell) + 1
        d = (b[2] - c[2]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[2] - c[2])
        if abs(d) < 1e-9: continue
        for i in range(i0, i1 + 1):
            for k in range(k0, k1 + 1):
                px, pz = x0 + (i + .5) * cell, z0 + (k + .5) * cell
                l1 = ((b[2] - c[2]) * (px - c[0]) + (c[0] - b[0]) * (pz - c[2])) / d; l2 = ((c[2] - a[2]) * (px - c[0]) + (a[0] - c[0]) * (pz - c[2])) / d
                if min(l1, l2, 1 - l1 - l2) >= 0: cand[(i, k)].append((l1 * a[1] + l2 * b[1] + (1 - l1 - l2) * c[1], f))
    for a, b, c in np.vstack([cls, tri[np.abs(un[:, 1]) < .35]]):
        n = int(max(np.linalg.norm(b - a), np.linalg.norm(c - a), np.linalg.norm(c - b)) / (cell * .5)) + 2
        for u in range(n + 1):
            for v in range(n + 1 - u):
                p = (a * u + b * v + c * (n - u - v)) / n; blocks[(int((p[0] - x0) / cell), int((p[2] - z0) / cell))].append(p[1])
    limits = {k: (min(v), max(v)) for k, v in blocks.items()}

    def height(i, k, y):
        best, bd, bf = None, 1e9, -1
        for yy, f in cand.get((i, k), []):
            if abs(yy - y) < bd: bd, best, bf = abs(yy - y), yy, f
        return best, bf
    i, k = int((SPAWN[0] - x0) / cell), int((SPAWN[2] - z0) / cell); y, f = height(i, k, SPAWN[1])
    seen = {(i, k, round(y * 8))}; queue = collections.deque(seen)
    while queue:
        i, k, yy = queue.popleft(); y = yy / 8
        for di, dk in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            h, f = height(i + di, k + dk, y)
            if h is None or h - y > .75 or y - h > 5 or abs(un[f, 1]) < .35: continue
            b = limits.get((i + di, k + dk))
            if b and b[0] < y + 1.4 and b[1] > y + .5: continue
            s = (i + di, k + dk, round(h * 8))
            if s not in seen: seen.add(s); queue.append(s)
    return np.array([[x0 + (i + .5) * cell, yy / 8, z0 + (k + .5) * cell] for i, k, yy in seen])


class Sheet:
    """Exact signed distance to a set of triangles, sign by angle-weighted pseudo-normals."""
    def __init__(self, tri):
        self.a, self.b, self.c = tri[:, 0], tri[:, 1], tri[:, 2]
        n = np.cross(self.b - self.a, self.c - self.a); area = np.linalg.norm(n, axis=1) / 2; self.un = n / np.maximum(2 * area, 1e-9)[:, None]
        key = lambda v: tuple(np.round(v * 256).astype(int))
        vn, en = collections.defaultdict(lambda: np.zeros(3)), collections.defaultdict(lambda: np.zeros(3))
        for f, t in enumerate(tri):
            ks = [key(v) for v in t]
            for i in range(3):
                e0, e1 = t[(i + 1) % 3] - t[i], t[(i + 2) % 3] - t[i]; cos = np.dot(e0, e1) / max(np.linalg.norm(e0) * np.linalg.norm(e1), 1e-12)
                vn[ks[i]] += self.un[f] * np.arccos(np.clip(cos, -1, 1)); en[tuple(sorted((ks[i], ks[(i + 1) % 3])))] += self.un[f]
        # Region 0 face, 1..3 edges ab, bc, ca, 4..6 vertices a, b, c.
        self.pseudo = np.zeros((len(tri), 7, 3)); self.pseudo[:, 0] = self.un
        for f, t in enumerate(tri):
            ks = [key(v) for v in t]
            for i in range(3):
                self.pseudo[f, 1 + i] = en[tuple(sorted((ks[i], ks[(i + 1) % 3])))]; self.pseudo[f, 4 + i] = vn[ks[i]]
        count = np.maximum((area * 300).astype(int), 8); owner = np.repeat(np.arange(len(tri)), count)
        w = rng.dirichlet((1, 1, 1), len(owner)); self.samples = w[:, 0, None] * self.a[owner] + w[:, 1, None] * self.b[owner] + w[:, 2, None] * self.c[owner]
        self.owner = owner; self.kd = cKDTree(self.samples)

    def closest(self, P, f):
        a, b, c = self.a[f], self.b[f], self.c[f]; ab, ac, ap = b - a, c - a, P - a
        dot = lambda u, v: (u * v).sum(1)
        d1, d2 = dot(ab, ap), dot(ac, ap); bp = P - b; d3, d4 = dot(ab, bp), dot(ac, bp); cp = P - c; d5, d6 = dot(ab, cp), dot(ac, cp)
        vc, vb, va = d1 * d4 - d3 * d2, d5 * d2 - d1 * d6, d3 * d6 - d5 * d4
        denom = 1 / np.maximum(va + vb + vc, 1e-12); v, w = vb * denom, vc * denom; u = 1 - v - w; region = np.zeros(len(P), int)

        def put(mask, uu, vv, ww, code):
            u[mask], v[mask], w[mask], region[mask] = uu[mask] if hasattr(uu, "shape") else uu, vv[mask] if hasattr(vv, "shape") else vv, ww[mask] if hasattr(ww, "shape") else ww, code
        t = d1 / np.where(d1 - d3 == 0, 1, d1 - d3); put((vc <= 0) & (d1 >= 0) & (d3 <= 0), 1 - t, t, 0, 1)
        t = d2 / np.where(d2 - d6 == 0, 1, d2 - d6); put((vb <= 0) & (d2 >= 0) & (d6 <= 0), 1 - t, 0, t, 3)
        t = (d4 - d3) / np.where((d4 - d3) + (d5 - d6) == 0, 1, (d4 - d3) + (d5 - d6)); put((va <= 0) & (d4 - d3 >= 0) & (d5 - d6 >= 0), 0, 1 - t, t, 2)
        put((d1 <= 0) & (d2 <= 0), 1, 0, 0, 4); put((d3 >= 0) & (d4 <= d3), 0, 1, 0, 5); put((d6 >= 0) & (d5 <= d6), 0, 0, 1, 6)
        return u[:, None] * a + v[:, None] * b + w[:, None] * c, region, np.c_[u, v, w]

    def signed(self, P, chunk=400000):
        out, face = np.zeros(len(P), np.float32), np.zeros(len(P), np.int32)
        for s in range(0, len(P), chunk):
            Q = P[s:s + chunk]; _, idx = self.kd.query(Q, workers=-1); f = self.owner[idx]
            C, region, _ = self.closest(Q, f); d = Q - C
            sign = np.sign((d * self.pseudo[f, region]).sum(1)); sign[sign == 0] = 1
            out[s:s + chunk] = np.linalg.norm(d, axis=1) * sign; face[s:s + chunk] = f
        return out, face


# Periodic value noise on a random lattice.
LATTICE = rng.random((64, 64, 64), dtype=np.float32) * 2 - 1


def noise(P):
    return ndimage.map_coordinates(LATTICE, [P[:, 0] % 64, P[:, 1] % 64, P[:, 2] % 64], order=1, mode="grid-wrap")


def rock_noise(P):
    # Domain-warped ridges: broken slabs, stretched horizontally like bedding.
    warp = np.c_[noise(P * .31 + 7), noise(P * .31 + 19), noise(P * .31 + 31)] * 1.6
    Q = (P + warp) * np.array([1, 1.8, 1])
    v, amp, f = np.zeros(len(P), np.float32), 1.0, .28
    for o in range(5):
        v += amp * (1 - 2 * np.abs(noise(Q * f + o * 11.3))); amp *= .5; f *= 2.1
    return v / 1.94


def load_scan(name):
    g = json.load(open(SCANS + name + "/model.gltf")); blob = open(SCANS + name + "/" + g["buffers"][0]["uri"], "rb").read()

    def accessor(i, comps):
        a = g["accessors"][i]; bv = g["bufferViews"][a["bufferView"]]; start = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        return np.frombuffer(blob, np.float32, a["count"] * comps, start).reshape(-1, comps).astype(np.float64)
    V, N = [], []
    for mesh in g["meshes"]:
        for p in mesh["primitives"]:
            V.append(accessor(p["attributes"]["POSITION"], 3)); N.append(accessor(p["attributes"]["NORMAL"], 3))
    V, N = np.vstack(V), np.vstack(N); V -= (V.min(0) + V.max(0)) / 2
    front = N.mean(0); front = front / np.linalg.norm(front) if np.linalg.norm(front) > .12 else np.array([0, 0, 1.0])
    if len(V) > 120000:
        keep = rng.choice(len(V), 120000, replace=False); V, N = V[keep], N[keep]
    return dict(name=name, V=V, N=N, front=front, size=np.ptp(V, axis=0).max())


def trail_points():
    side = np.array([TRAIL_OUT[2], 0, -TRAIL_OUT[0]]); s_ = np.arange(0, TRAIL_LENGTH, .25)
    p = TRAIL_FOOT + s_[:, None] * TRAIL_OUT + np.sin(s_ * .2)[:, None] * 3.6 * side; p[:, 1] += .055 * s_
    return p


def rotation_between(a, b):
    v = np.cross(a, b); c = np.dot(a, b)
    if c < -.9999: return np.diag([-1.0, 1, -1])
    K = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]]); return np.eye(3) + K + K @ K / (1 + c)


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--cell", type=float, default=0.125); ap.add_argument("--triangles", type=int, default=3500000); ap.add_argument("--no-rocks", action="store_true"); args = ap.parse_args()
    t0 = time.time(); log = lambda *a: print(f"[{time.time() - t0:5.0f}s]", *a, flush=True)
    os.makedirs(OUT, exist_ok=True); os.makedirs(REPORT_DIR, exist_ok=True)
    tri, uv, light, mat, materials, page = load_terrain()
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]); area = np.linalg.norm(n, axis=1) / 2; un = n / np.maximum(2 * area, 1e-9)[:, None]
    weights = rng.dirichlet((1, 1, 1), 60)
    cut = np.array([(sample_page(page, mat[f], weights @ uv[f])[:, 3] < 128).mean() for f in range(len(tri))])
    names = [materials[m]["name"] for m in mat]
    # Painted forest (the wall depicts trees, not stone): becomes a soft bank under real plants, not rock.
    face_col = np.array([np.nan_to_num(sample_page(page, mat[f], weights @ uv[f])[:, :3].mean(0)) for f in range(len(tri))])
    painted_forest = (np.abs(un[:, 1]) < .6) & (face_col[:, 1] >= 1.45 * np.maximum(face_col[:, 0], 1)) & (face_col[:, 1] >= 1.45 * np.maximum(face_col[:, 2], 1))
    water = np.array([x.startswith("page-9") or x.startswith("page-17") for x in names])
    # Cut-out cards (fences, hanging leaves) are not surfaces of the solid: they stay as they are.
    # So is the whole black tree line on the tops of walls (page 5): with its opaque panels in the solid, a thin fin rose
    # over every rim, the relief ate it to lace, and its black paint coloured it. The low dry-stone walls along the path
    # (page 8) stay too: thin, recognisable masonry, not a part of the rock mass.
    page5 = np.array([x.startswith('page-5') for x in names]); card = (cut > .25) | page5 | np.array([x.startswith('page-8') for x in names])
    centre = tri.mean(1)
    pts = flood(tri, un); log("reachable cells", len(pts))
    # Region: everything south of ZCUT within the level.
    inregion = np.ones(len(tri), bool)
    # The black tree-line silhouette cards (page 5) give way to the real forest behind them.
    silhouette = page5
    drop = inregion & (~card | silhouette) & ~water
    log("painted forest faces in region", (inregion & painted_forest).sum(), "area", round(float(area[inregion & painted_forest].sum())))
    log("region faces", inregion.sum(), "replaced", drop.sum(), "water kept", (inregion & water).sum(), "cards kept", (inregion & card).sum())
    x0, x1, z0, z1 = tri[:, :, 0].min() - 12, tri[:, :, 0].max() + 12, tri[:, :, 2].min() - 18, tri[:, :, 2].max() + 12
    near = np.ones(len(tri), bool)
    solid_faces = near & ~card
    sheet_tri = tri[solid_faces]; sheet = Sheet(sheet_tri); log("sheet faces", len(sheet_tri), "samples", len(sheet.samples))
    sheet_water = water[solid_faces]; sheet_forest = painted_forest[solid_faces]
    y0, y1 = sheet_tri[:, :, 1].min() - 2, sheet_tri[:, :, 1].max() + 9
    c = args.cell; xs = np.arange(x0, x1 + c, c); ys = np.arange(y0, y1 + c, c); zs = np.arange(z0, z1 + c, c)
    log("grid", len(xs), len(ys), len(zs), round(len(xs) * len(ys) * len(zs) / 1e6, 1), "M")
    # Coarse field, then the exact distance in a band around the surface.
    f = 4; cx, cy, cz = xs[::f], ys[::f], zs[::f]
    G = np.stack(np.meshgrid(cx, cy, cz, indexing="ij"), -1).reshape(-1, 3); coarse, _ = sheet.signed(G); coarse = coarse.reshape(len(cx), len(cy), len(cz)); log("coarse done")
    field = ndimage.map_coordinates(coarse, np.meshgrid(np.arange(len(xs)) / f, np.arange(len(ys)) / f, np.arange(len(zs)) / f, indexing="ij"), order=1, mode="nearest").astype(np.float32)
    band = np.abs(field) < 1.8; ids = np.argwhere(band); B = np.c_[xs[ids[:, 0]], ys[ids[:, 1]], zs[ids[:, 2]]]
    sd, face = sheet.signed(B); field[band] = sd; log("band cells", len(B))
    sheet_abs = np.abs(field)
    # Height cap: the sheet's top in its own columns, a rising hill outside.
    cols = np.full((len(xs), len(zs)), -np.inf, np.float32); ci = ((sheet.samples[:, 0] - x0) / c).astype(int); ck = ((sheet.samples[:, 2] - z0) / c).astype(int)
    ok = (ci >= 0) & (ci < len(xs)) & (ck >= 0) & (ck < len(zs)); np.maximum.at(cols, (ci[ok], ck[ok]), sheet.samples[ok, 1].astype(np.float32))
    has = np.isfinite(cols); has = ndimage.binary_closing(has, iterations=2)
    filled = np.where(np.isfinite(cols), cols, -np.inf); filled = ndimage.maximum_filter(filled, 5); cols = np.where(np.isfinite(cols), cols, filled)
    dist, inds = ndimage.distance_transform_edt(~has, return_indices=True); dist = dist * c; nearest = cols[inds[0], inds[1]]; nearest = np.where(np.isfinite(nearest), nearest, y0)
    nearest_sharp = ndimage.gaussian_filter(nearest, .5 / c); nearest = ndimage.gaussian_filter(nearest, 3 / c)
    order = np.argsort(sheet.samples[ok, 1]); col_class = np.zeros_like(cols); owner_ok = sheet.owner[ok][order]
    col_class[ci[ok][order], ck[ok][order]] = sheet_forest[owner_ok].astype(np.float32) + 2 * sheet_water[owner_ok].astype(np.float32)
    near_class = col_class[inds[0], inds[1]]; near_forest = ndimage.gaussian_filter((near_class == 1).astype(np.float32), 1.5 / c); near_water = ndimage.gaussian_filter((near_class == 2).astype(np.float32), 1.5 / c)
    X2, Z2 = np.meshgrid(xs, zs, indexing="ij"); P2 = np.c_[X2.ravel(), np.zeros(X2.size), Z2.ravel()]
    hill = (.55 * noise(P2 * .09 + 3) + .3 * noise(P2 * .23 + 9) + .15 * noise(P2 * .6)).reshape(X2.shape)
    rise = near_forest * (.4 + .38 * dist + 3.2 * hill * smoothstep(0, 5, dist)) + (1 - near_forest) * np.maximum(-.3 * dist - .25, -6) - near_water * 1.5
    # Behind rock and water the ground starts at that very rim: a smoothed height would raise a mass behind a low wall beside a tall one.
    H = near_forest * nearest + (1 - near_forest) * nearest_sharp + rise
    low = has & ((col_class == 2) | (np.where(np.isfinite(cols), cols, 99) < -7)); d_low, low_at = ndimage.distance_transform_edt(~low, return_indices=True); shore = cols[low_at[0], low_at[1]] + .3 + .25 * d_low * c
    # Every wall keeps a spine of rock behind it, falling from its own top; only beyond that the ground may come down
    # to the shore. Without the spine a wall is a thin sliver that the relief eats through (windows in the world).
    spine = nearest_sharp - .2 - .7 * dist
    H = ndimage.gaussian_filter(np.maximum(np.minimum(H, shore), spine), 1.5 / c); H = np.where(has, cols, H)
    # Roofs: the arch is only an underside and side walls, so its mass lies ABOVE the sheet. Where a column's top
    # surface faces down, fill up to the rim around it (the cap only removes solid, so floors below are unaffected).
    col_ny = np.zeros_like(cols); col_ny[ci[ok][order], ck[ok][order]] = sheet.un[sheet.owner[ok][order], 1]
    roof_col = ndimage.binary_dilation(col_ny < -.25, iterations=2) & has
    closed = ndimage.grey_closing(np.where(np.isfinite(cols), cols, y0), size=int(9 / c))
    H = np.where(roof_col, np.maximum(H, closed + .3 + hill * 1.2), H); log("roof columns", int(roof_col.sum()), "m2", round(float(roof_col.sum() * c * c)))
    # The arch massif keeps its original outline: its top is the surface through the rim of its side walls (their open
    # upper edges), and on it relief only goes inward. Nothing may grow out of this shape.
    down = centre[(un[:, 1] < -.5) & (centre[:, 1] > 7)]; log("down-facing roof faces", len(down), "plan box", down[:, [0, 2]].min(0).round(1), down[:, [0, 2]].max(0).round(1))
    is_massif = np.array([x.split("-")[1] == "1" for x in names]) & (centre[:, 1] > 6.5) & (cKDTree(down[:, [0, 2]]).query(centre[:, [0, 2]])[0] < 6)
    mw = rng.dirichlet((1, 1, 1), 60); mp = np.concatenate([mw @ t for t in tri[is_massif]])
    mask = np.zeros((len(xs), len(zs)), bool); mi_, mk_ = ((mp[:, 0] - x0) / c).astype(int), ((mp[:, 2] - z0) / c).astype(int); keep_ = (mi_ >= 0) & (mi_ < len(xs)) & (mk_ >= 0) & (mk_ < len(zs)); mask[mi_[keep_], mk_[keep_]] = True
    mass_col = ndimage.binary_fill_holes(ndimage.binary_closing(mask, iterations=int(1 / c)))
    edge_count = collections.Counter(); key3 = lambda v: tuple(np.round(v * 256).astype(int))
    for t in tri:
        k_ = [key3(v) for v in t]
        for e_ in range(3): edge_count[tuple(sorted((k_[e_], k_[(e_ + 1) % 3])))] += 1
    rim_pts = []
    for t in tri[is_massif]:
        k_ = [key3(v) for v in t]
        for e_ in range(3):
            if edge_count[tuple(sorted((k_[e_], k_[(e_ + 1) % 3])))] == 1: rim_pts += [t[e_], t[(e_ + 1) % 3]]
    rim_pts = np.unique(np.round(np.array(rim_pts), 3), axis=0); rim_pts = rim_pts[rim_pts[:, 1] > 8]; log("massif faces", int(is_massif.sum()), "rim points", len(rim_pts), "y", rim_pts[:, 1].min().round(1), rim_pts[:, 1].max().round(1), "columns m2", round(float(mass_col.sum() * c * c)))
    hull = griddata(rim_pts[:, [0, 2]], rim_pts[:, 1], (X2, Z2), method="linear"); near_hull = griddata(rim_pts[:, [0, 2]], rim_pts[:, 1], (X2, Z2), method="nearest"); hull = np.where(np.isnan(hull), near_hull, hull)
    hull = ndimage.gaussian_filter(hull, .3 / c); H = np.where(mass_col, np.maximum(hull, np.where(np.isfinite(cols), cols, -np.inf)), H); mass_near = ndimage.binary_dilation(mass_col, iterations=int(1.5 / c))
    # Where the rim hull runs close over the roof the slab is thin and relief ate through it (sky through the roof, strips).
    roof_body = mass_col & (col_ny < -.25) & np.isfinite(cols); thick = np.zeros_like(H, dtype=np.float32); thick[roof_body] = np.maximum(0, cols[roof_body] + .8 - H[roof_body]); thick = ndimage.gaussian_filter(thick, .5 / c); H = H + thick; log("roof slab thickened: columns", int((thick > .05).sum()), "max m", round(float(thick.max()), 2))
    # No slit between the massif and the slope beside it: the original shows rock there, not sky.
    around = ndimage.binary_dilation(mass_col, iterations=int(4 / c)) & ~mass_col; H_closed = ndimage.grey_closing(np.where(np.isfinite(H), H, y0), size=int(5 / c)); H = np.where(around, np.maximum(H, H_closed), H); log("slit closing: columns raised", int((around & (H_closed > H + .01)).sum()))
    # Sign outside the sheet's columns: the back of the walls is solid, so the outside is solid under the hill.
    outside = ~has
    Y = ys[None, :, None]; field = np.maximum(field, (Y - H[:, None, :]).astype(np.float32))
    below = (Y < H[:, None, :]) & outside[:, None, :]; field = np.where(below & (field > 0), -np.minimum(field, 1), field); log("cap applied")
    # Cap and fill leave a step in the field where the surface is not the sheet's: there noise could not move it and it
    # stayed a flat plane. Give those places a true distance to the solid; surfaces at the sheet keep their exact one.
    solid0 = field < 0; true_d = (ndimage.distance_transform_edt(~solid0).astype(np.float32) - ndimage.distance_transform_edt(solid0).astype(np.float32)) * c
    field = np.where(sheet_abs < .5, field, true_d); del solid0, true_d, sheet_abs; log("true distance applied")
    # Player's swept capsule (centres kept off the original walls: the collider stops the capsule there).
    region_pts = pts[(pts[:, 0] > x0) & (pts[:, 0] < x1) & (pts[:, 2] > z0) & (pts[:, 2] < z1 + 2)]
    steep_samples = sheet.samples[np.abs(sheet.un[sheet.owner, 1]) < .5]; kd_steep = cKDTree(steep_samples)
    dwall = np.min([kd_steep.query(region_pts + [0, h, 0], workers=-1)[0] for h in (.42, .85, 1.3, 1.7)], axis=0)
    standing = region_pts[dwall >= CAPSULE_RADIUS - .06]; log("standing cells", len(standing), "of", len(region_pts))
    spheres = np.vstack([standing + [0, h, 0] for h in (.62, .95, 1.3, 1.7)]); kd_player = cKDTree(spheres)
    water_samples = sheet.samples[sheet_water[sheet.owner]]; kd_water = cKDTree(water_samples) if len(water_samples) else None
    # Fields on the working band (wider, so scans and noise have room).
    working = np.abs(field) < 1.6; ids = np.argwhere(working); W = np.c_[xs[ids[:, 0]], ys[ids[:, 1]], zs[ids[:, 2]]].astype(np.float32)
    dp, _ = kd_player.query(W, workers=-1); dw = kd_water.query(W, workers=-1)[0] if kd_water else np.full(len(W), 99.0)
    free = smoothstep(.45, 1.7, dp) * smoothstep(.5, 1.3, dw) * smoothstep(ZCUT + .5, ZCUT - 2.5, W[:, 2])
    w_owner = sheet.owner[sheet.kd.query(W, workers=-1)[1]]; wforest = np.maximum(sheet_forest[w_owner], ~has[ids[:, 0], ids[:, 2]]).astype(np.float32)
    w_mass = mass_near[ids[:, 0], ids[:, 2]] & (W[:, 1] > 5.5); free_all = free.copy(); free = np.where(w_mass, 0, free)
    log("working cells", len(W))
    values = field[working]; base = values.copy()
    # Scanned rock forms.
    placed = []
    if not args.no_rocks:
        scans = [load_scan(s) for s in ("mountainside", "namaqualand_cliff_01", "namaqualand_cliff_02", "rock_face_01", "rock_face_02", "namaqualand_boulder_03", "boulder_01")]
        log("scans", [(s["name"], len(s["V"]), round(float(s["size"]), 1)) for s in scans])
        target = {"mountainside": (6, 10), "namaqualand_cliff_01": (5, 9), "namaqualand_cliff_02": (7, 12), "rock_face_01": (5, 8), "rock_face_02": (4, 7), "namaqualand_boulder_03": (2.5, 5), "boulder_01": (1.5, 3)}
        rocky = np.where(drop & (np.abs(un[:, 1]) < .6) & ~painted_forest)[0]; choice = rng.choice(rocky, 3000, p=area[rocky] / area[rocky].sum())
        spots = []; kd_stand = cKDTree(standing[:, [0, 2]])
        for fi in choice:
            p = rng.dirichlet((1, 1, 1)) @ tri[fi]
            if p[2] > ZCUT - 2 or any(np.linalg.norm(p - q) < 2.6 for q in spots[-400:]): continue
            d_stand, i_stand = kd_stand.query(p[[0, 2]])
            if d_stand < 1.5 and p[1] > standing[i_stand, 1] + 3: continue
            spots.append(p); placed.append((fi, p))
        log("rock spots", len(placed))
        order = np.lexsort((W[:, 2], W[:, 0]))  # by x, then z
        Wx = W[order, 0]
        for num, (fi, p) in enumerate(placed):
            s = scans[rng.integers(len(scans))]; lo, hi = target[s["name"]]; scale = rng.uniform(lo, hi) / s["size"]
            nrm = un[fi].copy(); nrm[1] = np.clip(nrm[1], -.2, .25); nrm /= np.linalg.norm(nrm)
            Rot = rotation_between(s["front"], nrm)
            spin = np.radians(rng.uniform(-30, 30)); K = np.array([[0, -nrm[2], nrm[1]], [nrm[2], 0, -nrm[0]], [-nrm[1], nrm[0], 0]]); Rot = (np.eye(3) + np.sin(spin) * K + (1 - np.cos(spin)) * K @ K) @ Rot
            V = s["V"] @ Rot.T * scale; Vn = s["N"] @ Rot.T
            protrude = rng.uniform(.15, .75); V = V + p + nrm * (protrude - (V @ nrm).max())
            lo3, hi3 = V.min(0) - .7, V.max(0) + .7
            a, b = np.searchsorted(Wx, lo3[0]), np.searchsorted(Wx, hi3[0]); sel = order[a:b]; Q = W[sel]
            sel = sel[(Q[:, 1] > lo3[1]) & (Q[:, 1] < hi3[1]) & (Q[:, 2] > lo3[2]) & (Q[:, 2] < hi3[2])]
            if len(sel) == 0: continue
            kd = cKDTree(V); d, idx = kd.query(W[sel], workers=-1); sign = np.sign(((W[sel] - V[idx]) * Vn[idx]).sum(1))
            rock = np.minimum(d, .7) * sign; rock = np.where(free[sel] > .001, rock + (1 - free[sel]) * .9 * (dp[sel] < 1.7), 9.0)
            # Smooth union keeps scan forms and blends their contact with the wall.
            k = .22; h = np.clip(.5 + .5 * (values[sel] - rock) / k, 0, 1); values[sel] = values[sel] * (1 - h) + rock * h - k * h * (1 - h)
            if num % 40 == 0: log("rocks", num)
    # Fine fracture noise where the player cannot touch it, then carve the player's volume back out.
    # @plr_cx1, @plr_cx2 axes (plan position of the pillar objects).
    d_pillar = np.minimum(np.hypot(W[:, 0] + 15.1, W[:, 2] + 33), np.hypot(W[:, 0] + 8.5, W[:, 2] + 27))
    soft = noise(W * .55 + 41) * .6 + noise(W * 1.3 + 5) * .25
    # Relief fades out towards the top edge, so the edge keeps the original's clean line instead of teeth and hooks.
    rim_fade = smoothstep(0, 1.6, H[ids[:, 0], ids[:, 2]] - W[:, 1]); free = free * rim_fade
    values = values - rock_noise(W) * .6 * free * (1 - wforest) + soft * .5 * free * wforest + .12 * free + (rock_noise(W) * .5 + .5) * .2 * w_mass * free_all * smoothstep(2.2, 3.6, d_pillar)
    # The roof sits slightly into the separate pillar objects, so no light shows between them.
    values = values - .16 * (1 - smoothstep(1.8, 3.0, d_pillar)) * (W[:, 1] > 7)
    # Nothing stands more than these centimetres in front of the original surface: relief is cut into the same shape.
    limit = base - np.where(wforest > .5, .5, .14 + .2 * (noise(W * 1.7 + 13) * .5 + .5)); k = .1; h = np.clip(.5 + .5 * (limit - values) / k, 0, 1); values = values * (1 - h) + limit * h + k * h * (1 - h)
    # Water faces stay the original's (their own Remaster shaders): the solid stands back behind them.
    values = values + .3 * (1 - smoothstep(.25, .9, dw))
    # Ground the player can reach is exact: a slab along the original plane, whatever relief or water recess did around it.
    slab = (sheet.un[w_owner, 1] > .5) & ~sheet_water[w_owner] & (dp < 2.2) & (np.abs(base) < .4); values = np.where(slab, base, values)
    edge = .27 + .12 * (noise(W * 2.3) * .5 + .5)
    carve = edge - dp; k = .08; h = np.clip(.5 + .5 * (carve - values) / k, 0, 1); values = values * (1 - h) + carve * h + k * h * (1 - h)
    field[working] = values
    reach_over = int(.8 / c) * 2 + 1; H2 = np.where(mass_near, H, ndimage.maximum_filter(H, reach_over) + .1); field = np.maximum(field, (Y - H2[:, None, :]).astype(np.float32))
    trail = trail_points(); kd_trail = cKDTree(trail[:, [0, 2]]); dt2, it2 = kd_trail.query(np.c_[X2.ravel(), Z2.ravel()], workers=-1); dt2, it2 = dt2.reshape(X2.shape), it2.reshape(X2.shape)
    bank = trail[it2, 1] + np.clip(dt2 - TRAIL_WIDTH, 0, None) * .85 + np.clip(dt2 - TRAIL_WIDTH, 0, 3) * hill * .5
    field = np.where((dt2 < 7)[:, None, :], np.maximum(field, (Y - bank[:, None, :]).astype(np.float32)), field)
    stand_y = np.full((len(xs), len(zs)), -np.inf, np.float32); si_, sk_ = ((standing[:, 0] - x0) / c).astype(int), ((standing[:, 2] - z0) / c).astype(int); ok_ = (si_ >= 0) & (si_ < len(xs)) & (sk_ >= 0) & (sk_ < len(zs))
    np.maximum.at(stand_y, (si_[ok_], sk_[ok_]), standing[ok_, 1].astype(np.float32)); stand_y = ndimage.maximum_filter(stand_y, 3)
    walk_mask = np.isfinite(stand_y); inside_walk = ndimage.distance_transform_edt(walk_mask) * c
    natural_roof = np.isfinite(cols) & (cols > np.where(walk_mask, stand_y, np.inf) + 2.5)
    above = Y - stand_y[:, None, :]; lean = np.clip(.3 * (above - 2.2), 0, .9)
    roof = walk_mask[:, None, :] & ~natural_roof[:, None, :] & (above > 1.9) & (inside_walk[:, None, :] > lean)
    field = np.where(roof, np.maximum(field, np.minimum(inside_walk[:, None, :] - lean, .4).astype(np.float32)), field)
    # Below the level everything is solid: no surfaces nobody can see (bottom of the box, cavities inside the ground).
    # Only up-facing ground counts: in a column over the hole of a pillar the one surface is the arch's roof, and everything
    # below it would become a box around the pillar.
    grounded = ok & (sheet.un[sheet.owner, 1] > .2); col_min = np.full_like(cols, np.inf); np.minimum.at(col_min, (ci[grounded], ck[grounded]), sheet.samples[grounded, 1].astype(np.float32))
    col_min = ndimage.minimum_filter(col_min, 3); col_min = np.where(np.isfinite(col_min), col_min, -np.inf)
    field = np.where((Y < col_min[:, None, :] - .6) & has[:, None, :], np.minimum(field, -.5), field)
    field = np.where((Y < H[:, None, :] - 1) & outside[:, None, :], np.minimum(field, -.5), field)
    cavity = ndimage.binary_fill_holes(field < 0) & (field >= 0); log("filled cavity cells", int(cavity.sum())); field[cavity] = -.5; del cavity
    # The sheet's sign calls the gap between the massif's east back and the wall behind it air (it lies in front of that
    # wall): above the arch opening (y 9.6) it is rock, as the original shows it.
    near_back = ndimage.binary_dilation(mass_col, iterations=int(2.5 / c)) & ~mass_col & (xs[:, None] > -1) & (zs[None, :] > -36) & (zs[None, :] < -24)  # only the massif's east back
    slit = near_back[:, None, :] & (Y > 9.6) & (Y < H[:, None, :]); sl_ = np.argwhere(slit); slit[tuple(sl_[kd_player.query(np.c_[xs[sl_[:, 0]], ys[sl_[:, 1]], zs[sl_[:, 2]]], workers=-1)[0] < .6].T)] = False; del sl_; log("slit cells filled", int(slit.sum())); field = np.where(slit, np.minimum(field, -.3), field); del slit
    # Thin solid above the ground is never rock: fins, hooks and lace on the tops. What a 0.8 m ball cannot reach goes (the massif's body keeps its own outline).
    # Ground itself may be thin (decks), so everything up to 0.4 m over the column's ground stays.
    up = (sheet.un[sheet.owner[ok], 1] > .2); col_ground = np.full_like(cols, -np.inf); np.maximum.at(col_ground, (ci[ok][up], ck[ok][up]), sheet.samples[ok][up, 1].astype(np.float32)); col_ground = ndimage.maximum_filter(col_ground, 3)
    solid = field < 0; core = ndimage.distance_transform_edt(solid) * c > .4; reach = ndimage.distance_transform_edt(~core) * c <= .4 + c; thin = solid & ~reach & (Y > col_ground[:, None, :] + .4) & ~mass_col[:, None, :]; del core, reach
    # Real partitions are thin too: thin solid right at an original surface stays, only what stands away from it goes.
    tid = np.argwhere(thin); kept_at_sheet = sheet.kd.query(np.c_[xs[tid[:, 0]], ys[tid[:, 1]], zs[tid[:, 2]]], workers=-1)[0] < .55; thin[tid[kept_at_sheet, 0], tid[kept_at_sheet, 1], tid[kept_at_sheet, 2]] = False; del tid, kept_at_sheet
    # At the very top nothing thin may stay, not even at the original surface: teeth live there.
    top_zone = Y > ndimage.maximum_filter(np.where(np.isfinite(cols), cols, y0), 5)[:, None, :] - 1.2
    core = ndimage.distance_transform_edt(solid) * c > .6; reach = ndimage.distance_transform_edt(~core) * c <= .6 + c; thin |= solid & ~reach & top_zone & (Y > col_ground[:, None, :] + .4) & ~mass_col[:, None, :]; del core, reach, top_zone
    tl, tn = ndimage.label(thin); ts = ndimage.sum_labels(np.ones_like(tl), tl, np.arange(1, tn + 1)) * c ** 3 if tn else np.zeros(0)
    log("thin solid removed: pieces", tn, "m3", round(float(ts.sum()), 1), "largest m3", np.sort(ts)[-5:].round(2).tolist(), "centres", [np.round(np.array(ndimage.center_of_mass(thin, tl, int(i) + 1)) * c + [x0, y0, z0], 1).tolist() for i in np.argsort(ts)[-5:]]); del tl
    field[thin] = np.maximum(field[thin], .1); del thin
    solid = field < 0; labels, count = ndimage.label(solid); sizes = ndimage.sum_labels(np.ones_like(labels), labels, np.arange(1, count + 1)); small = sizes < 8.0 / c ** 3; under = np.c_[((standing[:, 0] - x0) / c).astype(int), ((standing[:, 1] - .2 - y0) / c).astype(int), ((standing[:, 2] - z0) / c).astype(int)]; under = under[(under >= 0).all(1) & (under < np.array(field.shape)).all(1)]
    carrying = np.unique(labels[under[:, 0], under[:, 1], under[:, 2]]); carrying = carrying[carrying > 0]; small[carrying - 1] = False; small = np.where(small)[0] + 1; field[np.isin(labels, small) & solid] = .1
    # Loose pieces under 8 m3 are removed, never one carrying ground the player stands on (thin decks over a lower level).
    log("solid components", count, "removed as crumbs", len(small), "carrying ground", len(carrying))
    pad = np.pad(field, 1, constant_values=1.0); smooth = ndimage.gaussian_filter(pad, .55)
    # Outer faces of the working box close the mesh.
    smooth[0], smooth[-1], smooth[:, 0], smooth[:, -1], smooth[:, :, 0], smooth[:, :, -1] = 1, 1, 1, 1, 1, 1
    # Oracle: behind every replaced face of the original there is solid (the arch's roof above all).
    # Relief goes inward by up to 0.7 m, so look for the solid at three depths.
    solid_behind = np.any([ndimage.map_coordinates(smooth, ((centre[drop] - un[drop] * d_ - [x0 - c, y0 - c, z0 - c]) / c).T, order=1, mode="nearest") < 0 for d_ in (.3, .6, .9)], axis=0)
    # Not applicable: thin fins (the point behind is already in front of another original face) and the cut trail.
    thin = sheet.signed(centre[drop] - un[drop] * .3)[0] > 0; cut_by_trail = kd_trail.query(centre[drop][:, [0, 2]])[0] < 4.5
    applicable = ~thin & ~cut_by_trail & ~card[drop]; log("oracle: thin fins", int(thin.sum()), "cut by trail", int(cut_by_trail.sum()), "of", int(drop.sum()))
    roof_faces = (un[drop][:, 1] < -.5) & (centre[drop][:, 1] > 8)
    missing = centre[drop][~solid_behind & applicable]; log("faces with no solid behind, by normal y", np.histogram(un[drop][~solid_behind & applicable][:, 1], [-1, -.5, .5, 1])[0].tolist(), "by 5 m cell", collections.Counter((int(q[0] // 5) * 5, int(q[2] // 5) * 5) for q in missing).most_common(8))
    log("replaced faces with solid behind", round(float(solid_behind[applicable].mean()), 3), "arch roof faces", int(roof_faces.sum()), "with solid behind", round(float(solid_behind[roof_faces].mean()), 3) if roof_faces.any() else None)
    assert solid_behind[applicable].mean() > .9 and (not roof_faces.any() or solid_behind[roof_faces].mean() > .9), "original surfaces lost their solid"
    # Oracle: the game camera. It is stopped only by the original collider, not by the shell, so sweep it the way
    # PrototypeCamera does (focus 1.1 m above the player, up to 4 m back, stops 0.12 m before the sheet) and look
    # for lens positions inside the new solid: from there the world would be seen through culled back faces.
    def field_at(q): return ndimage.map_coordinates(smooth, ((q - [x0 - c, y0 - c, z0 - c]) / c).T, order=1, mode="nearest")
    focus = standing[::20] + [0, 1.1, 0]
    lens_dirs = []
    for yaw in np.radians(np.arange(0, 360, 30)):
        for pitch in np.radians((-20, 0, 15, 30, 50, 70)): lens_dirs.append((-np.sin(yaw) * np.cos(pitch), np.sin(pitch), -np.cos(yaw) * np.cos(pitch)))
    lens_dirs = np.array(lens_dirs); radii = np.arange(.35, 4.01, .15)
    FP = np.repeat(focus, len(lens_dirs), axis=0); FD = np.tile(lens_dirs, (len(focus), 1)); stop = np.full(len(FP), 4.0); alive = np.ones(len(FP), bool)
    for r_ in radii:
        q = FP[alive] + FD[alive] * r_; sd_q, _ = sheet.signed(q); hit = sd_q < .12; idx_ = np.where(alive)[0][hit]; stop[idx_] = np.maximum(.35, r_ - .15); alive[idx_] = False
    lens = FP + FD * stop[:, None]; inside = field_at(lens) < -.02
    log("lens oracle: positions", len(lens), "inside the shell", int(inside.sum()), "share", round(float(inside.mean()), 4), "by 5 m cell", collections.Counter((int(q[0] // 5) * 5, int(q[2] // 5) * 5) for q in lens[inside]).most_common(6))
    # The same sweep with the camera's Remaster-only pull-in on the baked solid grid (not closer than 1.6 m).
    # field_at reads the fine field; the grid written below keeps a 0.15 m margin, so this is the conservative side.
    pulled = stop.copy(); blocked = field_at(FP + FD * pulled[:, None]) < .15
    while blocked.any():
        pulled[blocked] = np.maximum(1.6, pulled[blocked] - .1); blocked &= pulled > 1.6
        blocked[blocked] = field_at(FP[blocked] + FD[blocked] * pulled[blocked, None]) < .15
    lens_left = field_at(FP + FD * pulled[:, None]) < -.02
    log("lens oracle with pull-in: inside", int(lens_left.sum()), "share", round(float(lens_left.mean()), 5), "mean radius lost m", round(float((stop - pulled).mean()), 3))
    report_lens = dict(lens_positions=len(lens), lens_inside_before=int(inside.sum()), lens_inside_after_pull_in=int(lens_left.sum()))
    V, F, _, _ = measure.marching_cubes(smooth, 0, spacing=(c, c, c), allow_degenerate=False, method="lewiner")
    V += np.array([x0 - c, y0 - c, z0 - c]); log("marching cubes", len(V), len(F))
    # Marching cubes winds by gradient direction (air = higher); make normals point to air.
    t = V[F[:50000]]; fn = np.cross(t[:, 1] - t[:, 0], t[:, 2] - t[:, 0]); test = t.mean(1) + fn / np.maximum(np.linalg.norm(fn, axis=1), 1e-9)[:, None] * 2 * c
    ii = np.clip(((test - [x0 - c, y0 - c, z0 - c]) / c).astype(int), 0, np.array(smooth.shape) - 1); frac = (smooth[ii[:, 0], ii[:, 1], ii[:, 2]] > 0).mean()
    if frac < .5: F = F[:, ::-1]
    # Snap walkable surfaces back to the exact original planes; simplify (flat floors collapse most); snap again.
    def snap_floor(V):
        sv, sf = sheet.signed(V); floor = (sheet.un[sf, 1] > .5) & ~sheet_water[sf] & (np.abs(sv) < .2)
        qa, normal = sheet.a[sf], sheet.un[sf]
        plane_y = qa[:, 1] - (normal[:, 0] * (V[:, 0] - qa[:, 0]) + normal[:, 2] * (V[:, 2] - qa[:, 2])) / np.where(np.abs(normal[:, 1]) < 1e-3, 1, normal[:, 1])
        C, _, _ = sheet.closest(V, sf); inside = np.linalg.norm((V - C)[:, [0, 2]], axis=1) < .06; snap = floor & inside; V[snap, 1] = plane_y[snap]
        return sv, sf, floor, int(snap.sum())
    snap_floor(V); edges_mc = np.sort(np.vstack([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]]), axis=1); valence_mc = np.unique(edges_mc, axis=0, return_counts=True)[1]; log("marching cubes open edges", int((valence_mc == 1).sum())); del edges_mc, valence_mc
    simplify = pyfqmr.Simplify(); simplify.setMesh(V, F); simplify.simplify_mesh(target_count=args.triangles, aggressiveness=4, preserve_border=True, verbose=0); V, F, _ = simplify.getMesh(); V = V.astype(np.float64); F = F.astype(np.int64); log("simplified", len(V), len(F))
    t = V[F[:50000]]; fn = np.cross(t[:, 1] - t[:, 0], t[:, 2] - t[:, 0]); test = t.mean(1) + fn / np.maximum(np.linalg.norm(fn, axis=1), 1e-9)[:, None] * 2 * c
    ii = np.clip(((test - [x0 - c, y0 - c, z0 - c]) / c).astype(int), 0, np.array(smooth.shape) - 1); log("winding: air along normals", round(float((smooth[ii[:, 0], ii[:, 1], ii[:, 2]] > 0).mean()), 3))
    sv, sf, floor, snapped = snap_floor(V); log("snapped floor vertices", snapped)
    # Vertex normals.
    fn = np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]]); N = np.zeros_like(V)
    for i in range(3): np.add.at(N, F[:, i], fn)
    N /= np.maximum(np.linalg.norm(N, axis=1), 1e-9)[:, None]
    # Original look: painted texel times baked light, blurred over the atlas mosaic with nearest samples.
    all_faces = np.where(near)[0]; per = np.maximum((area[all_faces] * 14).astype(int), 4); owner = np.repeat(all_faces, per)
    bw = rng.dirichlet((1, 1, 1), len(owner)); Sp = (bw[:, None, :] @ tri[owner]).squeeze(1); Suv = (bw[:, None, :] @ uv[owner]).squeeze(1); Sl = (bw[:, None, :] @ light[owner]).squeeze(1)
    Scol = np.zeros((len(owner), 3)); Sa = np.zeros(len(owner))
    for m in np.unique(mat[owner]):
        s = mat[owner] == m; px = sample_page(page, m, Suv[s]); Scol[s] = px[:, :3] / 255; Sa[s] = px[:, 3] / 255
    Sraw = Scol.copy(); Scol = np.clip(Scol * Sl / 128, 0, 1); keep = (Sa > .5) & ~page5[owner]
    kd_col = cKDTree(Sp[keep]); Sc32, Sr32 = Scol[keep].astype(np.float32), Sraw[keep].astype(np.float32); floor_mix = smoothstep(.6, .85, N[:, 1])
    guide, raw = np.zeros_like(V, dtype=np.float32), np.zeros_like(V, dtype=np.float32); far_ = np.zeros(len(V), np.float32)
    for st in range(0, len(V), 300000):
        en = st + 300000; dk, idx = kd_col.query(V[st:en], k=48, workers=-1); dk = dk.astype(np.float32); fm = floor_mix[st:en][:, None, None]
        wk = (np.exp(-(dk / .5) ** 2)[:, :, None] * fm + np.exp(-(dk / .9) ** 2)[:, :, None] * (1 - fm)) + 1e-6
        guide[st:en] = (Sc32[idx] * wk).sum(1) / wk.sum(1); raw[st:en] = (Sr32[idx] * wk).sum(1) / wk.sum(1); far_[st:en] = dk[:, 0]
    near_paint = far_ < .6; steep_ = N[:, 1] < .55
    rock_tint = np.median(guide[near_paint & steep_ & (raw[:, 1] < 1.25 * raw[:, 0])], axis=0); ground_tint = np.median(guide[near_paint & ~steep_ & (raw[:, 1] > 1.4 * raw[:, 0])], axis=0); log("far tints", rock_tint.round(3), ground_tint.round(3))
    away = smoothstep(.8, 2.2, far_)[:, None]; tinted = np.where(steep_[:, None], rock_tint, ground_tint); guide = guide * (1 - away) + tinted * away
    # Ambient occlusion from the field itself (open space along the normal).
    occ = np.ones(len(V))
    for dist_ in (.25, .6, 1.2):
        q = V + N * dist_; ii = ((q - [x0 - c, y0 - c, z0 - c]) / c).T; val = ndimage.map_coordinates(smooth, ii, order=1, mode="nearest"); occ *= np.clip(.35 + .65 * val / dist_, .35, 1)
    # Layers from the final shape and the original paint.
    up = N[:, 1]; gr = raw[:, 1] / np.maximum(raw[:, 0], .02); flat = smoothstep(.55, .8, up); wob = noise(V * .9) * .5 + noise(V * 2.7) * .25
    forest_v = sheet_forest[sf] | ((sv > .3) & (gr > 1.35))
    wob2 = noise(V * .45 + 3) * .9 + noise(V * 1.4 + 8) * .5 + noise(V * 3.7) * .25
    moss = np.clip((gr - 1.08) * 3 + wob2 + smoothstep(.25, .6, up) * .8, 0, 1); moss = np.where(forest_v, 1, smoothstep(.3, .6, moss))
    pathy = smoothstep(-.02, .06, raw[:, 0] - .86 * raw[:, 1]) * smoothstep(1.3, 1.9, raw[:, 0] / np.maximum(raw[:, 2], .02))
    d_trail = kd_trail.query(V[:, [0, 2]], workers=-1)[0]; on_trail = smoothstep(TRAIL_WIDTH + .35, TRAIL_WIDTH - .25, d_trail) * smoothstep(.3, .6, up)
    pathy = np.maximum(pathy, on_trail + (noise(V * 2.1) * .35) * on_trail * (1 - on_trail))
    path_colour = np.median(guide[(pathy > .7) & (flat > .7) & (V[:, 2] < ZCUT) & (d_trail > 3)], axis=0)
    guide = guide * (1 - on_trail[:, None]) + path_colour * on_trail[:, None]
    splat = np.c_[(1 - flat) * (1 - moss), (1 - flat) * moss, flat * (1 - pathy), flat * pathy]
    colour = np.c_[np.round(guide * 255), np.round(np.clip(occ, 0, 1) * 255)].astype(np.uint8); splat8 = np.round(splat * 255).astype(np.uint8)
    # Oracle: the massif has not grown. Vertices over its columns: distance in front of the original surfaces, height over the hull.
    # (Inner columns only: on the mask's own edge a vertex can belong to the neighbouring taller wall.)
    box = ndimage.binary_erosion(mass_col, iterations=3)[np.clip(((V[:, 0] - x0) / c).astype(int), 0, len(xs) - 1), np.clip(((V[:, 2] - z0) / c).astype(int), 0, len(zs) - 1)] & (V[:, 1] > 6.5)
    over_hull = V[box, 1] - hull[np.clip(((V[box, 0] - x0) / c).astype(int), 0, len(xs) - 1), np.clip(((V[box, 2] - z0) / c).astype(int), 0, len(zs) - 1)]
    report_growth = dict(massif_vertices=int(box.sum()), massif_max_over_hull_m=float(over_hull.max())); log("massif oracle: vertices", int(box.sum()), "max over hull m", round(float(over_hull.max()), 3), "p99", round(float(np.percentile(over_hull, 99)), 3))
    # Over the rim hull only where an original surface itself stands that high.
    over_original = over_hull - np.maximum(0, np.where(np.isfinite(cols), cols, -np.inf)[np.clip(((V[box, 0] - x0) / c).astype(int), 0, len(xs) - 1), np.clip(((V[box, 2] - z0) / c).astype(int), 0, len(zs) - 1)] - hull[np.clip(((V[box, 0] - x0) / c).astype(int), 0, len(xs) - 1), np.clip(((V[box, 2] - z0) / c).astype(int), 0, len(zs) - 1)])
    # (The slab's height is taken over the neighbouring cells too: the mesh smoothing spreads a step over about 25 cm.)
    over_original = over_original - ndimage.maximum_filter(thick, 5)[np.clip(((V[box, 0] - x0) / c).astype(int), 0, len(xs) - 1), np.clip(((V[box, 2] - z0) / c).astype(int), 0, len(zs) - 1)]
    log("massif over its outline, beyond original surfaces and the 0.8 m roof slab: max m", round(float(over_original.max()), 3)); off_ = V[box][over_original > .25]; log("  over 25 cm:", len(off_), "of", int(box.sum()), "bbox", off_.min(0).round(1).tolist() if len(off_) else None, off_.max(0).round(1).tolist() if len(off_) else None, "normal y", np.round(N[box][over_original > .25][:, 1].mean(), 2) if len(off_) else None)
    if c < .2: assert over_original.max() < .3 or len(off_) < 40 and over_original.max() < .4, "the massif grew over its original outline"
    black = np.round(guide @ np.array([.3, .59, .11]), 3); steep_v = N[:, 1] < .6; log("colour: darkest 1% of steep guide luminance", round(float(np.percentile(black[steep_v], 1)), 3), "near page 5 black (< 0.02):", int((black[steep_v] < .02).sum()))
    # Oracle: no growths anywhere. On the original's footprint: distance in front of the original surface, and height
    # over the original top (with the small reach a wall's relief has over the floor beside it).
    col_i = np.clip(((V[:, 0] - x0) / c).astype(int), 0, len(xs) - 1); col_k = np.clip(((V[:, 2] - z0) / c).astype(int), 0, len(zs) - 1)
    inner = ndimage.binary_erosion(has, iterations=4)[col_i, col_k] & (kd_trail.query(V[:, [0, 2]], workers=-1)[0] > 7) & ~forest_v & ~mass_near[col_i, col_k] & (near_forest[col_i, col_k] < .2) & (V[:, 1] > col_min[col_i, col_k])  # support under a rim and hills behind forest are not growths
    front = sv[inner]; over_top = (V[:, 1] - ndimage.maximum_filter(np.where(np.isfinite(cols), cols, y0), reach_over)[col_i, col_k])[inner]
    growth = np.maximum(front, over_top); worst = V[inner][growth > .45]
    report_growth.update(rock_vertices=int(inner.sum()), rock_max_in_front_m=float(front.max()), rock_front_p99_m=float(np.percentile(front, 99)), rock_max_over_top_m=float(over_top.max()), rock_vertices_over_45cm=len(worst))
    log("growth oracle: rock vertices", int(inner.sum()), "in front p99", round(float(np.percentile(front, 99)), 3), "max", round(float(front.max()), 3), "over top max", round(float(over_top.max()), 3), "over 45 cm", len(worst), "at", collections.Counter((int(q[0] // 5) * 5, int(q[2] // 5) * 5) for q in worst).most_common(8))
    # The flagged remainder is known closure, judged by eye in the review stills: the hood above the lake closed into a cliff
    # (around x 0, z -15) and the slot above the falls (around x 2, z -32). More than this means a new real growth.
    if c < .2: assert len(worst) < inner.sum() * .02 and front.max() < 6, "rock grew out of its original shape"
    # Mesh checks.
    edges = np.sort(np.vstack([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]]), axis=1); _, valence = np.unique(edges, axis=0, return_counts=True)
    open_edges, nonmanifold = int((valence == 1).sum()), int((valence > 2).sum())
    S = ndimage.map_coordinates(smooth, ((spheres.astype(np.float64) - [x0 - c, y0 - c, z0 - c]) / c).T, order=1, mode="nearest")
    embedded = int((S < -.12).sum())
    # Floor accuracy: distance from the original surface of shell vertices right at standing cells.
    kd_st = cKDTree(standing[:, [0, 2]]); ds, si = kd_st.query(V[:, [0, 2]], workers=-1)
    near_floor = (ds < .1) & (np.abs(V[:, 1] - standing[si, 1]) < .3)
    checker = Sheet(tri[near]); floor_error = np.abs(checker.signed(V[near_floor])[0])
    bad = V[near_floor][floor_error > .05]
    if len(bad):
        lift = (V[near_floor][:, 1] - standing[si[near_floor], 1])[floor_error > .05]
        log("bad floor vertices: height above standing cell p1/p50/p99", np.percentile(lift, [1, 50, 99]).round(3), "surface distance p50/p90", np.percentile(floor_error[floor_error > .05], [50, 90]).round(3))
        cells = collections.Counter((int(q[0] // 2) * 2, int(q[2] // 2) * 2) for q in bad)
        log("floor error > 5 cm:", len(bad), "of", int(near_floor.sum()), "in 2 m cells", cells.most_common(14))
    # The palette's moss: the original's painted fern-rock green at its median light.
    green = (raw[:, 1] > 1.3 * raw[:, 0]) & (raw[:, 1] > 1.3 * raw[:, 2])
    moss_tint = np.median(guide[green], axis=0); moss_lum = float(moss_tint @ [.3, .59, .11]); log("moss tint", moss_tint.round(3))
    # Real plants: thickets on the painted forest and over the hills, ferns on mossy rock, trees on the hills.
    dp_v = kd_player.query(V, workers=-1)[0]; dv_v = cKDTree(region_pts).query(V, workers=-1)[0]; out = V[:, 2] < ZCUT - 1.5
    def scatter(mask, spacing):
        ids_ = np.where(mask)[0]; ids_ = ids_[rng.permutation(len(ids_))]; cells_ = np.floor(V[ids_] / spacing).astype(int)
        _, first = np.unique(cells_, axis=0, return_index=True); return ids_[first]
    out = out & (d_trail > TRAIL_WIDTH + .9) & ((dv_v < 14) | (d_trail < 6))
    # Hill plants only beyond the original level: nothing new grows on the arch or on any other original top.
    on_original = ndimage.binary_dilation(has, iterations=int(.75 / c))[col_i, col_k]
    hills = (out & (dv_v > 3) & (dv_v < 11) | (d_trail < 8) & out) & (up > .55) & ~floor & ~on_original & (near_forest[col_i, col_k] > .5)
    dress = []
    for kind, mask, spacing, lo, hi in ((0, out & forest_v & (up > -.3) & (up < .55) & (dp_v > .6), 2.1, 2.2, 3.8), (0, hills & (dv_v > 5.5), 3.6, 3, 5), (2, hills & (dv_v > 7), 6.5, 8, 13), (1, out & ~forest_v & (gr > 1.05) & (up > -.4) & (up < .85) & (dp_v > .45), 1.5, .8, 1.7)):
        for i in scatter(mask, spacing):
            # Crowns sit back into the bank: only the front of a plant may lean over the walkway, and it shrinks there.
            size = min(rng.uniform(lo, hi), .9 + 1.1 * float(dp_v[i])) if kind != 2 else rng.uniform(lo, hi)
            at = V[i] - N[i] * size * .28 * (N[i, 1] < .8) * (kind == 0)
            dress.append((kind, *at, *N[i], size))
    dress = np.array(dress, np.float32)
    # Nothing new on the arch or on any original top: trees and crowns not on the sheet's top, nothing at all on a
    # natural roof over the walkway. The bake fails if the oracle does not hold.
    top_i = np.clip(((dress[:, 1] - x0) / c).astype(int), 0, len(xs) - 1); top_k = np.clip(((dress[:, 3] - z0) / c).astype(int), 0, len(zs) - 1)
    wide = ndimage.binary_dilation(has, iterations=int(.75 / c))
    on_top = (dress[:, 0] != 1) & wide[top_i, top_k] & (dress[:, 2] > ndimage.maximum_filter(cols, int(1.5 / c))[top_i, top_k] - 1.5)
    over_walk = ndimage.binary_dilation(walk_mask, iterations=int(2 / c))[top_i, top_k] & (dress[:, 2] > ndimage.maximum_filter(stand_y, int(4 / c))[top_i, top_k] + 3)
    arch = mass_near[top_i, top_k] & (dress[:, 2] > 7)
    log("removed from original tops", int(on_top.sum()), "from roofs over the walkway", int(over_walk.sum()), "from the arch", int(arch.sum()))
    # A crown without a trunk needs ground right under it, or it hangs in the air beside a crest.
    bush = dress[:, 0] == 0; crown = dress[:, 1:4] + np.array([0, .26, 0]) * dress[:, 7:8]; under = crown - np.array([0, .6, 0]) * dress[:, 7:8]
    hanging = bush & (field_at(under.astype(np.float64)) > 0); log("hanging crowns removed", int(hanging.sum()))
    dress = dress[~on_top & ~over_walk & ~arch & ~hanging]
    log("dress", {k: int((dress[:, 0] == k).sum()) for k in (0, 1, 2)})
    report = dict(**report_lens, **report_growth, solid_behind_replaced_faces=float(solid_behind[applicable].mean()), oracle_thin_faces=int(thin.sum()), oracle_trail_faces=int(cut_by_trail.sum()), plants=len(dress), vertices=len(V), triangles=len(F), open_edges=open_edges, nonmanifold_edges=nonmanifold, rocks_placed=len(placed), capsule_samples=len(spheres), capsule_samples_inside_solid=embedded,
                  floor_vertices_checked=int(near_floor.sum()), floor_error_m_p50=float(np.percentile(floor_error, 50)), floor_error_m_p99=float(np.percentile(floor_error, 99)), floor_error_m_max=float(floor_error.max()), faces_replaced=int(drop.sum()), trail_metres=TRAIL_LENGTH, cell=c, zcut=ZCUT,
                  bounds=[[float(V[:, 0].min()), float(V[:, 1].min()), float(V[:, 2].min())], [float(V[:, 0].max()), float(V[:, 1].max()), float(V[:, 2].max())]])
    log(report)
    # Tiles for frustum culling: shared edges keep identical positions and normals, so there are no cracks.
    tile = np.floor(V[F].mean(1)[:, [0, 2]] / TILE).astype(int); keys_, inverse = np.unique(tile, axis=0, return_inverse=True); inverse = inverse.ravel()
    for old in os.listdir(OUT):
        if old.startswith("pocket"): os.remove(OUT + old)
    with open(OUT + "level.xenashell", "wb") as out:
        out.write(b"XSH2"); out.write(struct.pack("<I", len(keys_)))
        for t_ in range(len(keys_)):
            ft = F[inverse == t_]; used_ = np.unique(ft); remap_ = np.full(len(V), -1, np.int64); remap_[used_] = np.arange(len(used_)); ft = remap_[ft]
            out.write(struct.pack("<II", len(used_), len(ft))); out.write(V[used_].astype(np.float32).tobytes()); out.write(N[used_].astype(np.float32).tobytes()); out.write(colour[used_].tobytes()); out.write(splat8[used_].tobytes()); out.write(ft.astype(np.uint32).tobytes())
    log("tiles", len(keys_))
    # Faces the shell replaces, as centroids (cm), and the region for the other Remaster providers.
    keys = np.round(centre[drop] * 100).astype(np.int32)
    with open(OUT + "level-faces.bytes", "wb") as out:
        out.write(struct.pack("<ffffffff", ZCUT, float(x0), float(x1), *[float(v) for v in moss_tint], moss_lum, TRAIL_LENGTH)); out.write(keys.tobytes())
    open(OUT + "level-dress.bytes", "wb").write(dress.tobytes())
    # Solid grid for the camera: 0.25 m cells as bits, 1 = closer than 0.15 m to the shell or inside it.
    grid = smooth[::2, ::2, ::2] < .15
    with open(OUT + "level-solid.bytes", "wb") as out:
        out.write(struct.pack("<ffffiii", x0 - c, y0 - c, z0 - c, 2 * c, *grid.shape)); out.write(np.packbits(grid.ravel(order="F"), bitorder="little").tobytes())
    json.dump(report, open(REPORT_DIR + "report.json", "w"), indent=1)
    # A native look at the result: vertical sections of the final field (grey) with the original sheet (yellow).
    for name, axis, value in (("z-40", 2, -40.0), ("x-20", 0, -20.0), ("z-30", 2, -30.0), ("z-27", 2, -27.0), ("z-21", 2, -21.0), ("x1", 0, 1.5), ("x-14", 0, -14.0), ("z0", 2, 0.0), ("z30", 2, 30.0)):
        k_ = int((value - (z0 if axis == 2 else x0)) / c) + 1; slab = (smooth[:, :, k_] if axis == 2 else smooth[k_, :, :]).T; im = np.zeros(slab.shape + (3,), np.uint8); im[...] = (14, 16, 28); im[slab < 0] = (92, 84, 70)
        other, origin = (0, x0 - c) if axis == 2 else (2, z0 - c)
        for t_ in tri[np.abs(centre[:, axis] - value) < 12]:
            sgn = t_[:, axis] - value; cross_ = [t_[a_] + (t_[(a_ + 1) % 3] - t_[a_]) * sgn[a_] / (sgn[a_] - sgn[(a_ + 1) % 3]) for a_ in range(3) if sgn[a_] * sgn[(a_ + 1) % 3] < 0]
            if len(cross_) == 2:
                for u_ in np.linspace(0, 1, 60):
                    pt = cross_[0] * (1 - u_) + cross_[1] * u_; ih, jh = int((pt[other] - origin) / c), int((pt[1] - y0 + c) / c)
                    if 0 <= ih < im.shape[1] and 0 <= jh < im.shape[0]: im[jh, ih] = (255, 200, 60)
        Image.fromarray(im[::-1]).save(REPORT_DIR + "section_" + name + ".png")
    log("written", os.path.getsize(OUT + "level.xenashell") // (1024 * 1024), "MiB")


if __name__ == "__main__":
    main()
