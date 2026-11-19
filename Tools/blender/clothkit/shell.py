"""The shell: the carved surface moved out along the body normals, smoothed, pushed back out of the skin, and given a rolled hem."""
import numpy as np

from humankit import regions as RG
from .carve import Soup, log

CLEARANCE = 0.001        # a shell vertex is never closer than (offset - this) to the skin


class Graph:
    """Edge lists of a soup: all of them, and the boundary-only ones used to smooth the boundary along itself."""

    def __init__(self, soup):
        e = np.unique(np.sort(soup.edges(), axis=1), axis=0)
        self.n = len(soup.P)
        bd = soup.boundary()
        self.is_bd = np.zeros(self.n, bool)
        self.is_bd[bd.reshape(-1)] = True
        bset = {tuple(sorted(x)) for x in bd.tolist()}
        self.e = e
        self.e_bd = np.array([tuple(x) in bset for x in e.tolist()], bool)

    def smooth(self, X, iters, lam=0.5):
        """Laplacian smoothing; interior vertices average all neighbours, boundary vertices only their boundary ones."""
        e = self.e
        use = ~(self.is_bd[e[:, 0]] | self.is_bd[e[:, 1]]) | self.e_bd
        a, b = e[use, 0], e[use, 1]
        cnt = np.zeros(self.n)
        np.add.at(cnt, a, 1)
        np.add.at(cnt, b, 1)
        X = X.copy()
        for _ in range(iters):
            acc = np.zeros_like(X)
            np.add.at(acc, a, X[b])
            np.add.at(acc, b, X[a])
            avg = acc / np.maximum(cnt, 1).reshape(-1, *([1] * (X.ndim - 1)))
            X = np.where((cnt > 0).reshape(-1, *([1] * (X.ndim - 1))), (1 - lam) * X + lam * avg, X)
        return X


def fill_creases(g, P, N, iters=20, lam=0.6, thresh=0.0005):
    """Smoothing that only moves concave vertices (the neighbours' average lies above the vertex along its normal): it bridges
    the armpit and the crotch, where an offset along the normals folds over."""
    e = g.e
    use = ~(g.is_bd[e[:, 0]] | g.is_bd[e[:, 1]]) | g.e_bd
    a, b = e[use, 0], e[use, 1]
    cnt = np.zeros(g.n)
    np.add.at(cnt, a, 1)
    np.add.at(cnt, b, 1)
    P = P.copy()
    for _ in range(iters):
        acc = np.zeros_like(P)
        np.add.at(acc, a, P[b])
        np.add.at(acc, b, P[a])
        avg = acc / np.maximum(cnt, 1)[:, None]
        m = (((avg - P) * N).sum(axis=1) > thresh) & (cnt > 0)
        P[m] += lam * (avg[m] - P[m])
    return P


def push_out(soup, body, d, rounds=1):
    """Moves every vertex of soup.P that is inside the skin or closer than (d - CLEARANCE) to it straight away from its nearest
    skin point, to that distance. Returns how many vertices were short in the last round."""
    need = d - CLEARANCE
    bad = np.zeros(len(soup.P), bool)
    for _ in range(rounds):
        loc, nor, dist, out = body.nearest(soup.P)
        bad = (~out) | (dist < need - 1e-5)
        if not bad.any():
            break
        away = soup.P - loc
        L = np.linalg.norm(away, axis=1, keepdims=True)
        v = np.where(out[:, None] & (L > 1e-4), away / np.maximum(L, 1e-12), nor)
        soup.P[bad] = loc[bad] + v[bad] * need[bad, None]
    return int(bad.sum())


def make_shell(soup, body, garment):
    """Offsets, smooths and pushes soup.P (in place). Returns the per-vertex offset in metres.

    The cloth starts as the skin with its creases filled, moved out along its normals by the offset. Then it relaxes like a stretched
    membrane: every round is one Laplacian smoothing step (plus `crease_iters` steps that only move concave vertices, which bridge
    the armpit and the crotch) followed by pushing whatever got too close to the skin back out. `smooth_iters` rounds, then a few
    pushes to finish."""
    g = Graph(soup)
    d = np.full(len(soup.P), garment.offset_mm / 1000.0)
    for name, mm in garment.offset_by_region.items():
        d[soup.label == RG.INDEX[name]] = mm / 1000.0
    d = g.smooth(d, 6)                                   # regions ramp into each other
    if garment.offset_fn is not None:
        d = garment.offset_fn(soup, body, d * 1000.0) / 1000.0
    env, _ = body.envelope()
    en = body.envelope_normals()
    t = soup.ps[:, None]
    P0 = env[soup.pa] * (1 - t) + env[soup.pb] * t
    N0 = en[soup.pa] * (1 - t) + en[soup.pb] * t
    N0 /= np.linalg.norm(N0, axis=1, keepdims=True)
    soup.N = N0
    soup.P = P0 + N0 * d[:, None]
    push_out(soup, body, d, rounds=2)
    for r in range(garment.smooth_iters):
        soup.P = g.smooth(soup.P, 1)
        if garment.crease_iters:
            soup.P = fill_creases(g, soup.P, soup.N, iters=garment.crease_iters)
        push_out(soup, body, d)
    short = push_out(soup, body, d, rounds=4)
    _, _, dist, out = body.nearest(soup.P)
    short = int(((~out) | (dist < d - CLEARANCE - 5e-4)).sum())
    a, b, c = (soup.P[soup.tris[:, i]] for i in range(3))
    fn = np.cross(b - a, c - a)
    fn /= np.maximum(np.linalg.norm(fn, axis=1, keepdims=True), 1e-12)
    folded = int(((fn * soup.N[soup.tris].mean(axis=1)).sum(axis=1) < 0.35).sum())
    log(f"{garment.id}: shell offset {garment.offset_mm} mm, {garment.smooth_iters} relaxation rounds, distance to skin "
        f"{1000 * dist.min():.1f}-{1000 * dist.max():.1f} mm, {short} vertices short of the clearance, {folded} folded triangles")
    return d


# --------------------------------------------------------------------------------------------- hem
def _append(soup, P, N, W, label, src):
    n = len(soup.P)
    soup.P, soup.N, soup.W = np.concatenate([soup.P, P]), np.concatenate([soup.N, N]), np.concatenate([soup.W, W])
    soup.label, soup.src = np.concatenate([soup.label, label]), np.concatenate([soup.src, src])
    m = len(P)
    soup.pa, soup.pb = np.concatenate([soup.pa, np.full(m, -1)]), np.concatenate([soup.pb, np.full(m, -1)])
    soup.ps = np.concatenate([soup.ps, np.zeros(m)])
    return n


def add_tris(soup, tris, uv, mat, flat=False):
    soup.tris = np.concatenate([soup.tris, np.asarray(tris, np.int64).reshape(-1, 3)])
    soup.uv = np.concatenate([soup.uv, np.asarray(uv, float).reshape(-1, 3, 2)])
    soup.mat = np.concatenate([soup.mat, np.full(len(tris), mat, np.int32)])
    soup.flat = np.concatenate([soup.flat, np.full(len(tris), flat, bool)])


def add_hems(soup, garment):
    """A rolled edge on every boundary loop: ring B is `roll` towards the skin below the edge, ring C is `tuck` back in under the
    cloth. Both copy the boundary vertex's weights; the strips reuse the edge's UVs."""
    h = garment.hem
    bd = soup.boundary()
    if h.roll_mm <= 0 or len(bd) == 0:
        return 0
    roll, tuck = h.roll_mm / 1000.0, h.tuck_mm / 1000.0
    nrm = soup.vertex_normals()
    verts = np.unique(bd)
    idx = {int(v): i for i, v in enumerate(verts)}
    # direction back into the cloth: mean of the neighbours that are not on the boundary, tangent to the surface
    n = len(soup.P)
    nb = np.zeros((n, 3))
    cnt = np.zeros(n)
    on_bd = np.zeros(n, bool)
    on_bd[verts] = True
    for a, b in np.unique(np.sort(soup.edges(), axis=1), axis=0):
        if on_bd[a] and not on_bd[b]:
            nb[a] += soup.P[b]
            cnt[a] += 1
        if on_bd[b] and not on_bd[a]:
            nb[b] += soup.P[a]
            cnt[b] += 1
    inward = np.zeros((len(verts), 3))
    for v in verts:
        if cnt[v] > 0:
            t = nb[v] / cnt[v] - soup.P[v]
        else:       # a one-triangle-wide strip: towards the third vertex of its triangle is the best guess
            t = np.zeros(3)
        t -= nrm[v] * (t @ nrm[v])
        inward[idx[int(v)]] = t / max(np.linalg.norm(t), 1e-9)
    Pb = soup.P[verts] - nrm[verts] * roll + inward * (0.4 * roll)
    Pc = Pb + inward * tuck
    cols = lambda a: np.asarray(a)
    nb0 = _append(soup, Pb, soup.N[verts], soup.W[verts], soup.label[verts], np.full(len(verts), -1))
    nc0 = _append(soup, Pc, soup.N[verts], soup.W[verts], soup.label[verts], np.full(len(verts), -1)) if tuck > 0 else 0
    # the UV of an edge's two corners, from the triangle that owns it
    owner = {}
    for ti, t in enumerate(soup.tris[:len(soup.uv)]):
        for k in range(3):
            owner[(int(t[k]), int(t[(k + 1) % 3]))] = (ti, k)
    T, UV = [], []
    for u, v in bd:
        u, v = int(u), int(v)
        ti, k = owner[(u, v)]
        uu, uv_ = soup.uv[ti][k], soup.uv[ti][(k + 1) % 3]
        iu, iv = idx[u], idx[v]
        bu, bv, cu, cv = nb0 + iu, nb0 + iv, nc0 + iu, nc0 + iv
        strips = ((v, u, bu), (v, bu, bv))
        if tuck > 0:
            strips += ((bv, bu, cu), (bv, cu, cv))
        for tri in strips:
            T.append(tri)
            UV.append([uv_ if x in (v, bv, cv) else uu for x in tri])
    m = soup.mat[[owner[(int(u), int(v))][0] for u, v in bd]]
    add_tris(soup, T, UV, 0)
    soup.mat[-len(T):] = np.repeat(m, len(T) // len(bd))
    log(f"{garment.id}: hem on {len(bd)} boundary edges")
    return len(bd)
