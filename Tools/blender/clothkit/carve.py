"""Carving: the part of the body surface a garment is made from, cut exactly along the zone borders.

`Soup` is the working triangle mesh every later stage edits (positions P, body normals N, dense weights W, region labels,
the body vertex each vertex came from, per-corner UVs, per-triangle material index)."""
from dataclasses import dataclass

import numpy as np

from humankit import regions as RG

SNAP = 0.0008        # a cut closer than this to an existing vertex reuses that vertex (no slivers)


def log(*a):
    print("[clothkit]", *a, flush=True)


@dataclass
class Soup:
    P: np.ndarray        # (n,3) positions (the body surface first, the shell later)
    N: np.ndarray        # (n,3) body normals at the vertices (unit)
    W: np.ndarray        # (n,G) dense weights
    label: np.ndarray    # (n,) region of the vertex
    src: np.ndarray      # (n,) body vertex it is a copy of, or -1 (new vertex)
    tris: np.ndarray     # (T,3)
    uv: np.ndarray       # (T,3,2) per-corner UVs
    mat: np.ndarray      # (T,) material slot
    flat: np.ndarray     # (T,) bool: flat shaded (hard faces such as a sole)
    pa: np.ndarray = None   # (n,) the two body vertices a vertex lies between (pa == pb == src for a copied vertex) ...
    pb: np.ndarray = None
    ps: np.ndarray = None   # (n,) ... and where: P = body[pa] * (1 - ps) + body[pb] * ps. -1 for vertices added later.

    def edges(self):
        t = self.tris
        e = np.concatenate([t[:, [0, 1]], t[:, [1, 2]], t[:, [2, 0]]])
        return e

    def boundary(self):
        """Directed boundary edges (u, v) as they run in their triangle (the cloth is on their left)."""
        e = self.edges()
        key = np.sort(e, axis=1)
        _, inv, cnt = np.unique(key, axis=0, return_inverse=True, return_counts=True)
        return e[cnt[inv.reshape(-1)] == 1]

    def vertex_normals(self):
        a, b, c = (self.P[self.tris[:, i]] for i in range(3))
        fn = np.cross(b - a, c - a)
        n = np.zeros_like(self.P)
        for i in range(3):
            np.add.at(n, self.tris[:, i], fn)
        return n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)

    def tri_pieces(self):
        """Connected piece id of every triangle (pieces connect through shared vertices)."""
        parent = np.arange(len(self.P))

        def find(x):
            while parent[x] != x:
                parent[x] = parent[parent[x]]
                x = parent[x]
            return x
        for a, b in self.edges():
            ra, rb = find(a), find(b)
            if ra != rb:
                parent[ra] = rb
        return np.array([find(v) for v in self.tris[:, 0]])

    def components(self):
        return len(np.unique(self.tri_pieces()))

    def drop_small_pieces(self, min_tris=40):
        """Removes connected pieces of fewer than min_tris triangles (the body has a few zero-width fins on the midline)."""
        pid = self.tri_pieces()
        ids, cnt = np.unique(pid, return_counts=True)
        small = np.isin(pid, ids[cnt < min_tris])
        if small.any():
            self.dropped_src = np.unique(self.src[self.tris[small].reshape(-1)])     # body vertices of the dropped fins (the coverage check skips them)
            keep = ~small
            self.tris, self.uv, self.mat, self.flat = self.tris[keep], self.uv[keep], self.mat[keep], self.flat[keep]
            log(f"  dropped {int(small.sum())} triangles in {int((cnt < min_tris).sum())} tiny piece(s)")
        return self

    def compact(self):
        used = np.unique(self.tris)
        remap = np.full(len(self.P), -1, np.int64)
        remap[used] = np.arange(len(used))
        out = Soup(self.P[used], self.N[used], self.W[used], self.label[used], self.src[used], remap[self.tris],
                   self.uv, self.mat, self.flat, self.pa[used], self.pb[used], self.ps[used])
        out.dropped_src = getattr(self, "dropped_src", np.zeros(0, np.int64))
        return out


# --------------------------------------------------------------------------------------------- fields
def zone_fields(body, zones):
    """(G, H): G[z, v] is the signed distance of vertex v outside zone z (<= 0 inside); H[z, v] is True where v is out
    of the zone only because of its region (a border that follows the mesh edges, not a distance)."""
    n = len(body.co)
    G = np.full((len(zones), n), 1.0)
    H = np.ones((len(zones), n), bool)
    for k, z in enumerate(zones):
        inR = body.region_mask(z.regions, z.side)
        if z.grow_mm > 0:
            g = z.grow_mm / 1000.0
            D = body.geodesic(np.where(inR)[0], cutoff=g + 0.02)
            G[k] = np.where(np.isfinite(D), D - g, 1.0)
            G[k][inR] = -g
            H[k] = False
        else:
            G[k][inR] = -1.0
            H[k] = ~inR
        for c in z.cuts:
            d = c.distance(body, z.side)
            soft = ~H[k]
            G[k][soft] = np.maximum(G[k][soft], d[soft])
        if z.exclude is not None:
            ex = z.exclude(body)
            G[k][ex] = 1.0
            H[k][ex] = True
    return G, H


def carve(body, garment):
    zones = []
    for z in garment.zones:
        zones.append(z)
    G, H = zone_fields(body, zones)
    f = G.min(axis=0)
    inside = f <= 0
    tris = body.tris
    nin = inside[tris].sum(axis=1)
    P, N, W = [body.co], [body.normals], [body.W]
    lab, src = [body.labels], [np.arange(len(body.co))]
    PA, PB, PS = [np.arange(len(body.co))], [np.arange(len(body.co))], [np.zeros(len(body.co))]
    n0 = len(body.co)
    new = []                                # (a, b, s) for the created vertices
    cache = {}

    def cut(a, b):
        """The vertex on edge a (inside) -> b (outside) where the union of the zones ends."""
        key = (a, b)
        if key in cache:
            return cache[key]
        s = 0.0
        for k in range(len(zones)):
            if G[k][a] > 0:
                continue
            if H[k][b]:
                zs = 0.0
            else:
                zs = -G[k][a] / (G[k][b] - G[k][a]) if G[k][b] > G[k][a] else 1.0
            s = max(s, zs)
        L = np.linalg.norm(body.co[b] - body.co[a])
        if s * L < SNAP:
            v = a
        elif (1 - s) * L < SNAP:
            v = b
        else:
            v = n0 + len(new)
            new.append((a, b, s))
        cache[key] = (v, s)
        return cache[key]

    out_t, out_uv, out_m = [], [], []
    keep = np.where(nin == 3)[0]
    out_t.extend(tris[keep].tolist())
    out_uv.extend(body.uv[keep].tolist())
    for ti in np.where((nin == 1) | (nin == 2))[0]:
        t, uv = tris[ti].tolist(), body.uv[ti]
        ins = [bool(inside[v]) for v in t]
        if nin[ti] == 1:
            i = ins.index(True)                       # rotate so the lone inside vertex is first
            a, b, c = t[i], t[(i + 1) % 3], t[(i + 2) % 3]
            ua, ub, uc = uv[i], uv[(i + 1) % 3], uv[(i + 2) % 3]
            xab, sab = cut(a, b)
            xac, sac = cut(a, c)
            poly = [(a, ua), (xab, ua + (ub - ua) * sab), (xac, ua + (uc - ua) * sac)]
        else:
            i = ins.index(False)                      # rotate so the lone outside vertex is last: a, b inside, c outside
            a, b, c = t[(i + 1) % 3], t[(i + 2) % 3], t[i]
            ua, ub, uc = uv[(i + 1) % 3], uv[(i + 2) % 3], uv[i]
            xbc, sbc = cut(b, c)
            xca, sca = cut(a, c)
            poly = [(a, ua), (b, ub), (xbc, ub + (uc - ub) * sbc), (xca, ua + (uc - ua) * sca)]
        for j in range(1, len(poly) - 1):
            tri = [poly[0], poly[j], poly[j + 1]]
            if len({p[0] for p in tri}) < 3:
                continue
            out_t.append([p[0] for p in tri])
            out_uv.append([p[1].tolist() for p in tri])
    if new:
        ab = np.array([(a, b) for a, b, _ in new])
        s = np.array([s for _, _, s in new])[:, None]
        a, b = ab[:, 0], ab[:, 1]
        P.append(body.co[a] * (1 - s) + body.co[b] * s)
        nn = body.normals[a] * (1 - s) + body.normals[b] * s
        N.append(nn / np.linalg.norm(nn, axis=1, keepdims=True))
        W.append(np.where(s < 0.5, body.W[a], body.W[b]))   # weights are copied exactly from the nearer parent vertex
        lab.append(np.where(s[:, 0] < 0.5, body.labels[a], body.labels[b]))
        src.append(np.full(len(new), -1))
        PA.append(a)
        PB.append(b)
        PS.append(s[:, 0])
    tris_a = np.array(out_t, np.int64)
    soup = Soup(np.concatenate(P), np.concatenate(N), np.concatenate(W), np.concatenate(lab), np.concatenate(src),
                tris_a, np.array(out_uv, float).reshape(-1, 3, 2), np.zeros(len(tris_a), np.int32),
                np.zeros(len(tris_a), bool), np.concatenate(PA), np.concatenate(PB), np.concatenate(PS)
                ).drop_small_pieces().compact()
    log(f"{garment.id}: carved {len(soup.tris)} triangles, {len(soup.P)} vertices, {soup.components()} piece(s), "
        f"{len(new)} cut vertices")
    return soup
