"""Signed-distance modelling kit: primitives, smooth CSG groups, surface extraction and projection (numpy only)."""
import math
import numpy as np

BIG = 1.0e3


def nrm(v):
    v = np.asarray(v, float)
    return v / np.linalg.norm(v)


def frame(z_axis, x_hint):
    """3x3 matrix whose columns are the local x, y, z axes (z along z_axis, x towards x_hint)."""
    z = nrm(z_axis)
    x = np.asarray(x_hint, float)
    x = x - z * x.dot(z)
    if np.linalg.norm(x) < 1e-8:
        x = np.cross(z, [1.0, 0, 0]) if abs(z[0]) < 0.9 else np.cross(z, [0, 1.0, 0])
    x = nrm(x)
    y = np.cross(z, x)
    return np.stack([x, y, z], axis=1)


def smin(a, b, k):
    if k <= 0:
        return np.minimum(a, b)
    h = np.maximum(k - np.abs(a - b), 0.0) / k
    return np.minimum(a, b) - h * h * k * 0.25


def smax(a, b, k):
    return -smin(-a, -b, k)


def hermite(ts, vals, t):
    """Smooth (C1) interpolation of vals (n, m) at knots ts (n,) evaluated at t (N,), clamped at the ends."""
    ts = np.asarray(ts, float)
    vals = np.asarray(vals, float)
    n = len(ts)
    t = np.clip(t, ts[0], ts[-1])
    if n == 1:
        return np.repeat(vals[:1], len(t), axis=0)
    sl = (vals[1:] - vals[:-1]) / (ts[1:] - ts[:-1])[:, None]
    m = np.zeros_like(vals)
    m[0], m[-1] = sl[0], sl[-1]
    if n > 2:
        m[1:-1] = 0.5 * (sl[1:] + sl[:-1])
        # keep monotone pieces monotone (no overshoot)
        flat = (sl[1:] * sl[:-1]) <= 0
        m[1:-1][flat] = 0.0
    i = np.clip(np.searchsorted(ts, t, side='right') - 1, 0, n - 2)
    h = ts[i + 1] - ts[i]
    u = ((t - ts[i]) / h)[:, None]
    h00 = 2 * u ** 3 - 3 * u ** 2 + 1
    h10 = u ** 3 - 2 * u ** 2 + u
    h01 = -2 * u ** 3 + 3 * u ** 2
    h11 = u ** 3 - u ** 2
    return h00 * vals[i] + h10 * h[:, None] * m[i] + h01 * vals[i + 1] + h11 * h[:, None] * m[i + 1]


# ------------------------------------------------------------------------------------------------ primitives
class Prim:
    center = np.zeros(3)
    radius = 1.0

    def bound(self):
        return self.center, self.radius


class Ellipsoid(Prim):
    def __init__(self, c, r, z_axis=(0, 0, 1), x_hint=(1, 0, 0)):
        self.c = np.asarray(c, float)
        self.r = np.asarray(r, float)
        self.M = frame(z_axis, x_hint)
        self.center, self.radius = self.c, float(self.r.max())

    def __call__(self, P):
        q = (P - self.c) @ self.M
        k0 = np.linalg.norm(q / self.r, axis=1)
        k1 = np.linalg.norm(q / (self.r * self.r), axis=1)
        return k0 * (k0 - 1.0) / np.maximum(k1, 1e-9)


def _sd_round_cone(q, L, r1, r2):
    l2 = L * L
    rr = r1 - r2
    a2 = l2 - rr * rr
    il2 = 1.0 / l2
    y = q[:, 2] * L
    z = y - l2
    x2 = (q[:, 0] ** 2 + q[:, 1] ** 2) * l2 * l2
    y2 = y * y * l2
    z2 = z * z * l2
    k = np.sign(rr) * rr * rr * x2
    d_mid = (np.sqrt(np.maximum(x2 * a2 * il2, 0)) + y * rr) * il2 - r1
    d_b = np.sqrt(x2 + z2) * il2 - r2
    d_a = np.sqrt(x2 + y2) * il2 - r1
    return np.where(np.sign(z) * a2 * z2 > k, d_b, np.where(np.sign(y) * a2 * y2 < k, d_a, d_mid))


class Cone(Prim):
    """Round cone (tapered capsule) from a (radius ra) to b (radius rb); squashed to `flat` along local y,
    where local x points towards x_hint (the wide direction)."""

    def __init__(self, a, b, ra, rb, flat=1.0, x_hint=(1, 0, 0)):
        self.a = np.asarray(a, float)
        self.b = np.asarray(b, float)
        self.L = float(np.linalg.norm(self.b - self.a))
        self.M = frame(self.b - self.a, x_hint)
        self.ra, self.rb, self.flat = ra, rb, flat
        self.center = 0.5 * (self.a + self.b)
        self.radius = 0.5 * self.L + max(ra, rb)

    def __call__(self, P):
        q = (P - self.a) @ self.M
        q[:, 1] /= self.flat
        return _sd_round_cone(q, self.L, self.ra, self.rb) * (0.5 + 0.5 * self.flat)


class Loft(Prim):
    """A generalised cylinder from a to b. At each fraction t along it, a section made of two superellipse halves:
    half width rx along local x, half depths ry_pos / ry_neg on either side of local y, centre offset (ox, oy),
    exponents n_pos / n_neg. stations: (t, rx, ry, ox, oy, n) or (t, rx, ry_pos, ry_neg, ox, oy, n_pos, n_neg).
    The ends are capped (rounded by cap_k)."""

    def __init__(self, a, b, x_hint, stations, cap_k=0.02, t_range=(0.0, 1.0)):
        self.a = np.asarray(a, float)
        self.b = np.asarray(b, float)
        self.L = float(np.linalg.norm(self.b - self.a))
        self.M = frame(self.b - self.a, x_hint)
        st = []
        for s in stations:
            if len(s) == 6:
                t, rx, ry, ox, oy, n = s
                s = (t, rx, ry, ry, ox, oy, n, n)
            st.append(s)
        st = np.asarray(st, float)
        self.ts = st[:, 0]
        self.vals = st[:, 1:]
        self.cap_k = cap_k
        self.t0, self.t1 = t_range
        self.center = 0.5 * (self.a + self.b)
        self.radius = 0.5 * self.L * max(1.0, abs(self.t0 - 0.5) * 2, abs(self.t1 - 0.5) * 2) + float(
            np.abs(st[:, 1:6]).max()) * 2.0

    def __call__(self, P):
        q = (P - self.a) @ self.M
        t = q[:, 2] / self.L
        v = hermite(self.ts, self.vals, t)
        x = q[:, 0] - v[:, 3]
        y = q[:, 1] - v[:, 4]
        pos = y >= 0
        rx = v[:, 0]
        ry = np.where(pos, v[:, 1], v[:, 2])
        n = np.where(pos, v[:, 5], v[:, 6])
        u = np.abs(x) / rx
        w = np.abs(y) / ry
        k0 = (u ** n + w ** n) ** (1.0 / n)
        g = np.maximum(k0, 1e-9) ** (1.0 - n)
        gx = g * np.maximum(u, 1e-12) ** (n - 1) / rx
        gy = g * np.maximum(w, 1e-12) ** (n - 1) / ry
        d2 = (k0 - 1.0) / np.maximum(np.sqrt(gx * gx + gy * gy), 1e-9)
        da = np.maximum((self.t0 - t) * self.L, (t - self.t1) * self.L)
        return smax(d2, da, self.cap_k)


class Hull(Prim):
    """Convex hull of points, edges rounded by a smooth maximum of the face planes."""

    def __init__(self, points, round_k=0.004):
        pts = np.asarray(points, float)
        self.normals, self.offs = _hull_planes(pts)
        self.k = round_k
        self.center = pts.mean(axis=0)
        self.radius = float(np.linalg.norm(pts - self.center, axis=1).max()) + 0.01

    def __call__(self, P):
        s = P @ self.normals.T - self.offs
        if s.shape[1] < 3:
            return s.max(axis=1)
        top = -np.partition(-s, 2, axis=1)[:, :3]          # the three nearest planes, rounded together
        return smax(smax(top[:, 0], top[:, 1], self.k), top[:, 2], self.k)


def _hull_planes(pts):
    """Face planes of the convex hull (brute force; fine for the few points used here)."""
    n = len(pts)
    ctr = pts.mean(axis=0)
    planes = []
    for i in range(n):
        for j in range(i + 1, n):
            for k in range(j + 1, n):
                nn = np.cross(pts[j] - pts[i], pts[k] - pts[i])
                ln = np.linalg.norm(nn)
                if ln < 1e-10:
                    continue
                nn /= ln
                off = nn.dot(pts[i])
                s = pts @ nn - off
                if np.all(s <= 1e-7):
                    pass
                elif np.all(s >= -1e-7):
                    nn, off = -nn, -off
                else:
                    continue
                if not any(np.allclose(nn, p[0], atol=1e-6) and abs(off - p[1]) < 1e-7 for p in planes):
                    planes.append((nn, off))
    return np.array([p[0] for p in planes]), np.array([p[1] for p in planes])


class Plane(Prim):
    """Half space: inside where (P - p0) . n < 0."""

    def __init__(self, p0, n, center=None, radius=10.0):
        self.p0 = np.asarray(p0, float)
        self.n = nrm(n)
        self.center = self.p0 if center is None else np.asarray(center, float)
        self.radius = radius

    def __call__(self, P):
        return (P - self.p0) @ self.n


class Torus(Prim):
    """Torus segment: ring radius R (in local xy plane), tube radius r, around axis z_axis, only for angles in
    [a0, a1] (radians, from local x), capped smoothly."""

    def __init__(self, c, z_axis, x_hint, R, r, a0=-math.pi, a1=math.pi, flat=1.0):
        self.c = np.asarray(c, float)
        self.M = frame(z_axis, x_hint)
        self.R, self.r, self.a0, self.a1, self.flat = R, r, a0, a1, flat
        self.center, self.radius = self.c, R + r

    def __call__(self, P):
        q = (P - self.c) @ self.M
        ang = np.arctan2(q[:, 1], q[:, 0])
        ang = np.clip(ang, self.a0, self.a1)
        cx, cy = self.R * np.cos(ang), self.R * np.sin(ang)
        dx, dy, dz = q[:, 0] - cx, q[:, 1] - cy, q[:, 2] / self.flat
        return (np.sqrt(dx * dx + dy * dy + dz * dz) - self.r) * (0.5 + 0.5 * self.flat)


# ------------------------------------------------------------------------------------------------ CSG tree
class Group:
    """A smooth union of primitives (each blended in with its own radius), minus smooth subtractions, with child
    groups each blended into this one with their join radius but never into each other."""

    def __init__(self, name, join_k=0.0):
        self.name = name
        self.join_k = join_k
        self.ops = []        # (op, prim, k)
        self.children = []

    def add(self, prim, k=0.01, tag=None):
        prim.tag = tag
        self.ops.append(("add", prim, k))
        return prim

    def sub(self, prim, k=0.004):
        self.ops.append(("sub", prim, k))
        return prim

    def inter(self, prim, k=0.004):
        self.ops.append(("int", prim, k))
        return prim

    def child(self, name, join_k):
        g = Group(name, join_k)
        self.children.append(g)
        return g

    def prims(self):
        out = [p for _, p, _ in self.ops]
        for c in self.children:
            out += c.prims()
        return out

    def eval(self, P, margin=0.05, skip=()):
        d = np.full(len(P), BIG)
        for op, prim, k in self.ops:
            c, r = prim.bound()
            near = np.einsum('ij,ij->i', P - c, P - c) < (r + margin + k) ** 2
            if not near.any():
                continue
            dd = np.full(len(P), BIG)
            dd[near] = prim(P[near])
            if op == "add":
                d = smin(d, dd, k)
            elif op == "sub":
                d = np.where(near, smax(d, -dd, k), d)
            else:
                d = np.where(near, smax(d, dd, k), d)
        out = d
        for c in self.children:
            if c.name in skip:
                continue
            cd = c.eval(P, margin, skip)
            out = np.minimum(out, smin(d, cd, c.join_k))
        return out


# ------------------------------------------------------------------------------------------------ meshing
def surface_nets(F, origin, h):
    """Naive surface nets on a dense grid of signed values: returns (verts (V, 3), quads (Q, 4)), outward facing."""
    nx, ny, nz = F.shape
    s = F < 0
    corners = [(i, j, k) for i in (0, 1) for j in (0, 1) for k in (0, 1)]
    sl = lambda i, j, k: (slice(i, nx - 1 + i), slice(j, ny - 1 + j), slice(k, nz - 1 + k))
    anyin = np.zeros((nx - 1, ny - 1, nz - 1), bool)
    allin = np.ones((nx - 1, ny - 1, nz - 1), bool)
    for c in corners:
        anyin |= s[sl(*c)]
        allin &= s[sl(*c)]
    active = anyin & ~allin
    cells = np.argwhere(active)
    vid = np.full(active.shape, -1, np.int64)
    vid[active] = np.arange(len(cells))
    # vertex = mean of the zero crossings on the cell's edges
    acc = np.zeros((len(cells), 3))
    cnt = np.zeros(len(cells))
    ci, cj, ck = cells[:, 0], cells[:, 1], cells[:, 2]
    edges = []
    for a in corners:
        for b in corners:
            if a < b and sum(abs(x - y) for x, y in zip(a, b)) == 1:
                edges.append((a, b))
    for a, b in edges:
        fa = F[ci + a[0], cj + a[1], ck + a[2]]
        fb = F[ci + b[0], cj + b[1], ck + b[2]]
        cr = (fa < 0) != (fb < 0)
        t = np.where(cr, fa / np.where(cr, fa - fb, 1.0), 0.0)
        pa = np.array(a, float)
        pb = np.array(b, float)
        p = pa[None] + t[:, None] * (pb - pa)[None]
        acc += np.where(cr[:, None], p, 0.0)
        cnt += cr
    verts = origin + (cells + acc / np.maximum(cnt, 1)[:, None]) * h
    quads = []
    # edges along each axis with a sign change -> one quad from the four cells around it
    for axis in range(3):
        o1, o2 = [a for a in range(3) if a != axis]
        sel = [slice(None)] * 3
        sel2 = [slice(None)] * 3
        sel[axis] = slice(0, F.shape[axis] - 1)
        sel2[axis] = slice(1, F.shape[axis])
        s0 = s[tuple(sel)]
        s1 = s[tuple(sel2)]
        ch = s0 != s1
        # need the four surrounding cells: index along o1, o2 must be in [1, n-2]
        lim = [slice(None)] * 3
        lim[o1] = slice(1, F.shape[o1] - 1)
        lim[o2] = slice(1, F.shape[o2] - 1)
        m = np.zeros_like(ch)
        m[tuple(lim)] = ch[tuple(lim)]
        e = np.argwhere(m)
        inside_first = s0[tuple(e.T)]

        def cell(d1, d2):
            c = e.copy()
            c[:, o1] -= d1
            c[:, o2] -= d2
            return vid[c[:, 0], c[:, 1], c[:, 2]]
        q = np.stack([cell(1, 1), cell(0, 1), cell(0, 0), cell(1, 0)], axis=1)
        # orientation: outward normal points from inside to outside along +axis if inside_first
        flip = ~inside_first
        if axis == 1:
            flip = ~flip
        q[flip] = q[flip][:, ::-1]
        quads.append(q)
    quads = np.concatenate(quads)
    return verts, quads


def sample(root, lo, hi, h, coarse=4, band=1.6):
    """Dense grid of the field over [lo, hi] at spacing h, evaluated exactly near the surface: a coarse pass picks
    the band, then any fine cell whose corners change sign without having been evaluated is refined until none is
    left (so a field that overestimates distance cannot leave stray sheets at the band's edge)."""
    lo = np.asarray(lo, float)
    hi = np.asarray(hi, float)
    H = h * coarse
    nc = np.ceil((hi - lo) / H).astype(int) + 1
    gc = np.stack(np.meshgrid(*[np.arange(n) for n in nc], indexing='ij'), -1).reshape(-1, 3)
    Fc = root.eval(lo + gc * H).reshape(nc)
    nf = (nc - 1) * coarse + 1
    near_c = np.abs(Fc) < band * H * math.sqrt(3)
    nd = near_c.copy()
    for ax in range(3):
        nd |= np.roll(near_c, 1, ax) | np.roll(near_c, -1, ax)
    idx = [np.minimum(np.round(np.arange(n) / coarse).astype(int), nc[a] - 1) for a, n in enumerate(nf)]
    F = Fc[np.ix_(*idx)].astype(np.float32)
    done = np.zeros(F.shape, bool)
    todo = nd[np.ix_(*idx)]
    for it in range(12):
        pts = np.argwhere(todo & ~done)
        if len(pts) == 0:
            break
        for s in range(0, len(pts), 400000):
            ch = pts[s:s + 400000]
            F[tuple(ch.T)] = root.eval(lo + ch * h)
        done[tuple(pts.T)] = True
        # cells with a sign change that have an unevaluated corner -> evaluate their neighbourhood
        sg = F < 0
        nx, ny, nz = F.shape
        sl = lambda i, j, k: (slice(i, nx - 1 + i), slice(j, ny - 1 + j), slice(k, nz - 1 + k))
        anyin = np.zeros((nx - 1, ny - 1, nz - 1), bool)
        allin = np.ones_like(anyin)
        alldone = np.ones_like(anyin)
        for c in [(i, j, k) for i in (0, 1) for j in (0, 1) for k in (0, 1)]:
            anyin |= sg[sl(*c)]
            allin &= sg[sl(*c)]
            alldone &= done[sl(*c)]
        bad = anyin & ~allin & ~alldone
        if not bad.any():
            break
        todo = np.zeros(F.shape, bool)
        for c in [(i, j, k) for i in (0, 1) for j in (0, 1) for k in (0, 1)]:
            todo[sl(*c)] |= bad
        for ax in range(3):
            todo |= np.roll(todo, 1, ax) | np.roll(todo, -1, ax)
        print("  refine pass", it, int(bad.sum()), "cells", flush=True)
    return F, lo


def gradient(root, P, e=1.0e-4):
    E = np.eye(3) * e
    return np.stack([(root.eval(P + E[i]) - root.eval(P - E[i])) / (2 * e) for i in range(3)], axis=1)


def project(root, P, iters=5, max_step=0.004):
    P = P.copy()
    for _ in range(iters):
        d = root.eval(P)
        g = gradient(root, P)
        g2 = np.maximum((g * g).sum(axis=1), 1e-6)
        step = (d / g2)[:, None] * g
        ln = np.linalg.norm(step, axis=1)
        step *= np.minimum(1.0, max_step / np.maximum(ln, 1e-12))[:, None]
        P -= step
    return P
