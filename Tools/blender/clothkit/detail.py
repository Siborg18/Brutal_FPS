"""Detail geometry on a finished shell (run from a garment's `extras`, before the hem): seams, bands, pockets, belt loops.

The shell is smooth and coarse (about 20 mm triangles on the torso), and its relaxation would wipe out anything of a few mm, so the
detail is made afterwards in two steps:

  * `split_field(soup, f, level)`: every edge that crosses the level set f(P) = level of a scalar field gets a new vertex exactly
    on it (conforming: both triangles of an edge are split the same way, so there are no cracks). The new vertices copy the
    nearer end's weights and region, positions, normals and UVs are interpolated.
  * `feature(soup, f, amount_mm, wall_mm)`: splits at f = 0 and f = -wall, then moves the vertices with f < 0 along the surface
    normal by amount * clamp(-f / wall, 0, 1): a raised (amount > 0) or sunk (amount < 0) area with a chamfered wall.

A field is a function of the (n, 3) positions in the rest pose, negative inside the feature, with metres as units (planes and
half-space intersections are exact distances; `convex` is the usual max of them).
"""
import numpy as np

from .carve import log


# --------------------------------------------------------------------------------------------- fields
def plane(point, normal):
    n = np.asarray(normal, float)
    n = n / np.linalg.norm(n)
    p = np.asarray(point, float)
    return lambda P: (P - p) @ n


def convex(*fields):
    """Inside all of them (the max of the signed distances)."""
    return lambda P: np.max([f(P) for f in fields], axis=0)


def either(*fields):
    return lambda P: np.min([f(P) for f in fields], axis=0)


def outline(f, half_width):
    """A band of the given half width around the zero set of f (inside where |f| < half_width)."""
    return lambda P: np.abs(f(P)) - half_width


def by_side(f_left, f_right):
    """f_left where x > 0 (the figure's left), f_right where x <= 0."""
    return lambda P: np.where(P[:, 0] > 0, f_left(P), f_right(P))


def box(lo, hi):
    """Axis aligned box (any bound may be None)."""
    fs = []
    for i in range(3):
        if lo[i] is not None:
            fs.append(plane([lo[i] if j == i else 0 for j in range(3)], [-1 if j == i else 0 for j in range(3)]))
        if hi[i] is not None:
            fs.append(plane([hi[i] if j == i else 0 for j in range(3)], [1 if j == i else 0 for j in range(3)]))
    return convex(*fs)


def polygon_xz(pts, y_max=None, y_min=None):
    """Convex polygon in the x/z plane (extruded along y, counter-clockwise seen from +y... the sign is fixed internally),
    optionally limited to y_min <= y <= y_max. The value is the max of the edge distances (exact inside, a bound outside)."""
    pts = np.asarray(pts, float)
    c = pts.mean(0)
    fs = []
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        e = b - a
        nx, nz = e[1], -e[0]                      # a normal of the edge in (x, z)
        nrm = np.hypot(nx, nz)
        nx, nz = nx / nrm, nz / nrm
        if (c[0] - a[0]) * nx + (c[1] - a[1]) * nz > 0:
            nx, nz = -nx, -nz                     # pointing away from the centre
        fs.append(plane([a[0], 0, a[1]], [nx, 0, nz]))
    if y_max is not None:
        fs.append(plane([0, y_max, 0], [0, 1, 0]))
    if y_min is not None:
        fs.append(plane([0, y_min, 0], [0, -1, 0]))
    return convex(*fs)


# --------------------------------------------------------------------------------------------- splitting
def split_edges(soup, marks):
    """Adds one vertex on every marked edge and re-triangulates the triangles around them without T-junctions.
    marks: {(a, b): t} with a < b, the vertex at P[a] * (1 - t) + P[b] * t. Triangles with one marked edge become 2, with two 3,
    with three 4."""
    if not marks:
        return 0
    from .carve import Soup
    n0 = len(soup.P)
    keys = sorted(marks)
    nv = {k: n0 + i for i, k in enumerate(keys)}
    lo = np.array([k[0] for k in keys])
    hi = np.array([k[1] for k in keys])
    t = np.array([marks[k] for k in keys])[:, None]
    P = soup.P[lo] * (1 - t) + soup.P[hi] * t
    N = soup.N[lo] * (1 - t) + soup.N[hi] * t
    N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-12)
    near = np.where(t[:, 0] < 0.5, lo, hi)
    soup.P = np.concatenate([soup.P, P])
    soup.N = np.concatenate([soup.N, N])
    soup.W = np.concatenate([soup.W, soup.W[near]])
    soup.label = np.concatenate([soup.label, soup.label[near]])
    soup.src = np.concatenate([soup.src, np.full(len(keys), -1)])
    soup.pa = np.concatenate([soup.pa, np.full(len(keys), -1)])
    soup.pb = np.concatenate([soup.pb, np.full(len(keys), -1)])
    soup.ps = np.concatenate([soup.ps, np.zeros(len(keys))])

    def mid(p, q, up, uq):
        """New vertex on edge p-q and its UV (corner UVs up, uq belong to p and q)."""
        key = (p, q) if p < q else (q, p)
        v = nv[key]
        tt = marks[key]
        tp = tt if p < q else 1 - tt              # fraction of the way from p to q
        return v, up * (1 - tp) + uq * tp

    T, UV, M, F = [], [], [], []
    for ti in range(len(soup.tris)):
        a, b, c = (int(x) for x in soup.tris[ti])
        ua, ub, uc = soup.uv[ti]
        eab = (min(a, b), max(a, b)) in nv
        ebc = (min(b, c), max(b, c)) in nv
        eca = (min(c, a), max(c, a)) in nv
        k = eab + ebc + eca
        out = []
        if k == 0:
            out = [((a, b, c), (ua, ub, uc))]
        else:
            # rotate so that the marked edges are: (a,b) alone; (a,b),(b,c); or all three
            if k == 1:
                if ebc:
                    a, b, c, ua, ub, uc = b, c, a, ub, uc, ua
                elif eca:
                    a, b, c, ua, ub, uc = c, a, b, uc, ua, ub
                x, ux = mid(a, b, ua, ub)
                out = [((a, x, c), (ua, ux, uc)), ((x, b, c), (ux, ub, uc))]
            elif k == 2:
                if not eca:
                    pass
                elif not eab:
                    a, b, c, ua, ub, uc = b, c, a, ub, uc, ua
                else:
                    a, b, c, ua, ub, uc = c, a, b, uc, ua, ub
                x, ux = mid(a, b, ua, ub)
                y, uy = mid(b, c, ub, uc)
                out = [((x, b, y), (ux, ub, uy))]
                Q = soup.P
                if np.linalg.norm(Q[a] - Q[y]) <= np.linalg.norm(Q[x] - Q[c]):
                    out += [((a, x, y), (ua, ux, uy)), ((a, y, c), (ua, uy, uc))]
                else:
                    out += [((a, x, c), (ua, ux, uc)), ((x, y, c), (ux, uy, uc))]
            else:
                x, ux = mid(a, b, ua, ub)
                y, uy = mid(b, c, ub, uc)
                z, uz = mid(c, a, uc, ua)
                out = [((a, x, z), (ua, ux, uz)), ((x, b, y), (ux, ub, uy)), ((z, y, c), (uz, uy, uc)), ((x, y, z), (ux, uy, uz))]
        for tri, uv in out:
            T.append(tri)
            UV.append(uv)
            M.append(soup.mat[ti])
            F.append(soup.flat[ti])
    soup.tris = np.array(T, np.int64)
    soup.uv = np.array(UV, float).reshape(-1, 3, 2)
    soup.mat = np.array(M, np.int32)
    soup.flat = np.array(F, bool)
    return len(keys)


def split_field(soup, f, level=0.0, where=None):
    """Splits every edge crossing f(P) = level (only where `where(P_mid)` is true when given). Returns the vertex count added."""
    v = f(soup.P) - level
    e = np.unique(np.sort(soup.edges(), axis=1), axis=0)
    a, b = e[:, 0], e[:, 1]
    cross = (v[a] * v[b] < 0)
    if where is not None and cross.any():
        mid = 0.5 * (soup.P[a] + soup.P[b])
        cross &= where(mid)
    marks = {}
    for i in np.where(cross)[0]:
        t = v[a[i]] / (v[a[i]] - v[b[i]])
        marks[(int(a[i]), int(b[i]))] = float(np.clip(t, 0.02, 0.98))
    return split_edges(soup, marks)


def refine(soup, f, max_edge_mm, passes=4, samples=9):
    """Bisects every edge longer than `max_edge_mm` that crosses the border of the feature f < 0 (f is sampled at `samples` points along
    the edge, so a band narrower than the edge is found too), up to `passes` times. Run before `feature` for features narrower than
    the mesh's edges: the split-at-the-border step only sees edges whose end points are on different sides."""
    added = 0
    ts = np.linspace(0, 1, samples)[None, :, None]
    for _ in range(passes):
        e = np.unique(np.sort(soup.edges(), axis=1), axis=0)
        a, b = e[:, 0], e[:, 1]
        L = np.linalg.norm(soup.P[a] - soup.P[b], axis=1)
        cand = np.where(L > max_edge_mm / 1000.0)[0]
        if len(cand) == 0:
            break
        pts = soup.P[a[cand], None, :] * (1 - ts) + soup.P[b[cand], None, :] * ts            # (m, samples, 3)
        v = f(pts.reshape(-1, 3)).reshape(len(cand), samples)
        cross = (v < 0).any(axis=1) & (v >= 0).any(axis=1)
        marks = {(int(x), int(y)): 0.5 for x, y in e[cand[cross]]}
        if not marks:
            break
        added += split_edges(soup, marks)
    return added


# --------------------------------------------------------------------------------------------- features
def feature(soup, f, amount_mm=0.0, wall_mm=2.0, material=None, material_where=0.5, refine_mm=0.0):
    """A raised (amount > 0) or sunk (< 0) area where f < 0 with a chamfered wall `wall_mm` wide inside its border, and optionally
    the material of the triangles inside it. refine_mm: first bisect the edges near the border down to that length (narrow grooves).
    Returns the number of vertices inside."""
    wall = wall_mm / 1000.0
    if refine_mm:
        refine(soup, f, refine_mm)
    split_field(soup, f, 0.0)
    split_field(soup, f, -wall)
    v = f(soup.P)
    if amount_mm:
        w = np.clip(-v / wall, 0.0, 1.0)
        nrm = soup.N / np.maximum(np.linalg.norm(soup.N, axis=1, keepdims=True), 1e-12)      # smooth (envelope) normals: no ripples
        move = w > 0
        soup.P = soup.P.copy()
        soup.P[move] += nrm[move] * (amount_mm / 1000.0) * w[move, None]
    if material is not None:
        a, b, c = (soup.P[soup.tris[:, i]] for i in range(3))
        # the field at the (rest pose, unmoved) triangle centres: use the vertex values
        fv = v[soup.tris].mean(axis=1)
        soup.mat[fv < -material_where * wall] = material
    return int((v < 0).sum())


def edge_distance(soup):
    """Geodesic distance (m, over the mesh edges) from the boundary of the soup to every vertex."""
    import heapq
    n = len(soup.P)
    adj = [[] for _ in range(n)]
    for a, b in np.unique(np.sort(soup.edges(), axis=1), axis=0).tolist():
        L = float(np.linalg.norm(soup.P[a] - soup.P[b]))
        adj[a].append((b, L))
        adj[b].append((a, L))
    dist = np.full(n, np.inf)
    h = []
    for s in np.unique(soup.boundary()):
        dist[s] = 0.0
        h.append((0.0, int(s)))
    heapq.heapify(h)
    while h:
        d, u = heapq.heappop(h)
        if d > dist[u]:
            continue
        for v, L in adj[u]:
            if d + L < dist[v]:
                dist[v] = d + L
                heapq.heappush(h, (d + L, v))
    return dist
