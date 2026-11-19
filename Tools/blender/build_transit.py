"""
Builds the Ford Transit panel van (Mk8, long wheelbase, high roof), after a three-quarter photo of a white one.
Exports Assets/Models/transit.glb and bakes its own paint/grime texture set ("transit_body"); the scene is saved as
Assets/transit.blend.

    blender -b --factory-startup -P Tools/blender/build_transit.py [-- preview] [nobake]

Coordinates: Blender +Y is the front (the game's -Z), +Z is up and the model is symmetric about X = 0; the origin is on
the ground under the middle of the body. Overall 5.98 m long, 2.06 m wide (2.47 m over the mirrors), 2.77 m high,
3.75 m wheelbase. (Built with the front axle at 1.765 and moved back to the middle at the end: Y_SHIFT.)

The van's front runs from the bumper up the grille, over the bonnet and windscreen and back along the roof in one line
that only ever moves rearward as it climbs, so the body is lofted in horizontal slices: at each height z the plan
outline is a rounded nose (a superellipse between the centre line Yf(z) and the point Yc(z) where the flat side
starts), straight sides W(z) and tight rear corners. Pressings on the sides are a displacement of that surface, and
everything drawn on it (glass, shut lines, grille, lamps, vents) is a thin patch laid over the same surface function.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import modelkit as K  # noqa: E402
from modelkit import box, cyl, mat, add_bevel  # noqa: E402

# ============================================================================================ dimensions
Y_F, Y_R = 2.765, -3.215          # bumper faces
Y_SHIFT = -(Y_F + Y_R) / 2          # moves the finished van so the origin is under its middle
Y_FA = 1.765                      # front axle
Y_RA = Y_FA - 3.75                # rear axle
TRACK = 1.74
R_TYRE, W_TYRE = 0.345, 0.215     # 215/65 R16 C
ARCH_R, ARCH_Z = 0.445, 0.355
Z_BOT, Z_TOP = 0.33, 2.77
Z_BUMPER = 0.745                  # top of the black bumpers and side cladding
Z_CLAD0 = 0.555                   # bottom of the side cladding
Z_SILL = 0.42                     # top of the black sill


def pchip(keys):
    """Monotone cubic interpolation through (x, y) keys (any order)."""
    keys = sorted(keys)
    xs = np.array([k[0] for k in keys], float)
    ys = np.array([k[1] for k in keys], float)
    h = np.diff(xs)
    d = np.diff(ys) / h
    m = np.zeros_like(ys)
    for k in range(1, len(xs) - 1):
        if d[k - 1] * d[k] > 0:
            w1, w2 = 2 * h[k] + h[k - 1], h[k] + 2 * h[k - 1]
            m[k] = (w1 + w2) / (w1 / d[k - 1] + w2 / d[k])
    m[0], m[-1] = d[0], d[-1]

    def f(x):
        x = float(np.clip(x, xs[0], xs[-1]))
        i = int(np.clip(np.searchsorted(xs, x) - 1, 0, len(h) - 1))
        t = (x - xs[i]) / h[i]
        return ((2 * t ** 3 - 3 * t ** 2 + 1) * ys[i] + (t ** 3 - 2 * t ** 2 + t) * h[i] * m[i]
                + (-2 * t ** 3 + 3 * t ** 2) * ys[i + 1] + (t ** 3 - t ** 2) * h[i] * m[i + 1])
    return f


# Key curves up the height (z). Yf: the centre line of the nose, bonnet, windscreen and roof.
YF = pchip([(0.33, 2.66), (0.42, 2.74), (0.55, 2.765), (0.735, 2.765), (0.755, 2.715), (0.98, 2.675), (1.17, 2.625),
            (1.225, 2.575), (1.27, 2.45), (1.34, 2.24), (1.43, 1.98), (1.51, 1.75), (1.56, 1.62), (2.27, 0.70),
            (2.40, 0.42), (2.55, 0.12), (2.68, -0.18), (2.74, -0.42), (2.757, -0.70), (2.77, -1.10)])
# Yc: where the flat side starts, the top of the wing rising to the foot of the A pillar, then the pillar itself.
YC = pchip([(0.33, 2.16), (0.74, 2.22), (1.00, 2.24), (1.18, 2.14), (1.28, 1.93), (1.38, 1.70), (1.47, 1.50), (1.60, 1.27),
            (2.27, 0.40), (2.50, -0.02), (2.66, -0.40), (2.735, -0.70), (2.757, -0.95), (2.77, -1.25)])
# Half width: black sill, white strip, proud cladding, shoulder, tumblehome, the step where the high roof starts, roof.
W_KEYS = [(0.33, 0.990), (0.405, 1.004), (0.42, 0.992), (0.54, 0.998), (0.555, 1.012), (0.735, 1.018), (0.752, 1.006),
          (1.00, 1.026), (1.30, 1.030), (1.44, 1.016), (1.62, 0.992), (2.20, 0.946), (2.40, 0.928), (2.425, 0.915),
          (2.58, 0.888), (2.67, 0.820), (2.735, 0.670), (2.757, 0.460), (2.77, 0.10)]
W = pchip(W_KEYS)
# Yr: the back: bumper, doors, the roof rolling over.
YR = pchip([(0.33, -3.15), (0.42, -3.205), (0.55, -3.215), (0.735, -3.215), (0.755, -3.165), (2.55, -3.140), (2.68, -3.05),
            (2.735, -2.87), (2.757, -2.65), (2.77, -2.25)])
AR = pchip([(0.33, 0.12), (2.55, 0.12), (2.70, 0.10), (2.77, 0.05)])    # rear corner radius


def nose_p(z):
    """The superellipse's exponent: rounder low down, a wrapped screen higher up."""
    return float(np.interp(z, (0.33, 1.20, 1.50, 2.30, 2.77), (2.5, 2.4, 2.25, 2.4, 2.2)))


def smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def rrect_sdf(y, z, y0, y1, z0, z1, r):
    cy, cz, hy, hz = (y0 + y1) / 2, (z0 + z1) / 2, (y1 - y0) / 2 - r, (z1 - z0) / 2 - r
    dy, dz = abs(y - cy) - hy, abs(z - cz) - hz
    return math.hypot(max(dy, 0), max(dz, 0)) + min(max(dy, dz), 0) - r


def side_disp(y, z):
    """Pressings in the flat side: the big recessed load panel, and the swage line under it."""
    d = 0.0
    s = rrect_sdf(y, z, -2.93, -0.12, 1.16, 2.24, 0.12)
    d -= 0.020 * smooth(0.035, -0.035, s)
    u = (z - 0.965) / 0.03
    d += 0.009 * math.exp(-u * u) * smooth(-3.07, -2.95, y) * smooth(1.10, 0.92, y)
    return d


def side_x(y, z):
    """x of the outer surface at (y, z), on the flat side and round the nose and the rear corners."""
    w = W(z)
    yc = YC(z)
    if y > yc:
        a = max(YF(z) - yc, 1e-3)
        p = nose_p(z)
        k = min(1.0, (y - yc) / a)
        return w * (1 - k ** p) ** (1 / p)
    yr, ar = YR(z), AR(z)
    if y < yr + ar:
        k = min(1.0, (yr + ar - y) / ar)
        return w - ar + ar * math.sqrt(max(0.0, 1 - k * k))
    return w + side_disp(y, z)


def nose_y(x, z):
    """y of the nose / bonnet / windscreen / roof at (x, z)."""
    w, yc = W(z), YC(z)
    a = max(YF(z) - yc, 1e-3)
    p = nose_p(z)
    k = min(1.0, abs(x) / w)
    return yc + a * (1 - k ** p) ** (1 / p)


def tail_y(x, z):
    """y of the back at (x, z)."""
    w, yr, ar = W(z), YR(z), AR(z)
    d = abs(x) - (w - ar)
    if d <= 0:
        return yr
    return yr + ar - ar * math.sqrt(max(0.0, 1 - min(1.0, d / ar) ** 2))


def inside(x, y, z):
    x = abs(x)
    w, yc = W(z), YC(z)
    if y > yc:
        a = max(YF(z) - yc, 1e-3)
        p = nose_p(z)
        return y < YF(z) and (x / w) ** p + ((y - yc) / a) ** p <= 1
    yr, ar = YR(z), AR(z)
    if y < yr:
        return False
    if y < yr + ar and x > w - ar:
        return (x - (w - ar)) ** 2 + (y - yr - ar) ** 2 <= ar * ar
    return x <= w


def polar(phi, z, cy):
    """The point on the outline at height z in the plan direction phi (degrees; 90 = straight ahead) from (0, cy)."""
    a = math.radians(phi)
    dx, dy = math.cos(a), math.sin(a)
    lo, hi = 0.0, 4.0
    for _ in range(26):
        mid = (lo + hi) / 2
        if inside(dx * mid, cy + dy * mid, z):
            lo = mid
        else:
            hi = mid
    return Vector((dx * lo, cy + dy * lo, z))


# ============================================================================================ surfaces and patches
class Surface:
    """A parametrised piece of the body: P(u, v) is on it, N(u, v) its outward normal (by finite differences)."""

    def __init__(self, fn, out):
        self.fn, self.out = fn, out

    def P(self, u, v):
        return Vector(self.fn(u, v))

    def N(self, u, v, e=0.004):
        n = (self.P(u + e, v) - self.P(u - e, v)).cross(self.P(u, v + e) - self.P(u, v - e))
        if n.length < 1e-12:
            return self.out(self.P(u, v))
        n.normalize()
        return n if n.dot(self.out(self.P(u, v))) > 0 else -n

    def at(self, u, v, off):
        return self.P(u, v) + self.N(u, v) * off


def _dir(*d):
    return lambda p: Vector(d)


SIDE_R = Surface(lambda y, z: (side_x(y, z), y, z), _dir(1, 0, 0))
SIDE_L = Surface(lambda y, z: (-side_x(y, z), y, z), _dir(-1, 0, 0))
FRONT = Surface(lambda x, z: (x, nose_y(x, z), z), _dir(0, 1, 0.25))
BACK = Surface(lambda x, z: (x, tail_y(x, z), z), _dir(0, -1, 0))
NOSE_R = Surface(lambda phi, z: polar(phi, z, 1.75), lambda p: Vector((p.x, p.y - 1.75, 0)))
NOSE_L = Surface(lambda phi, z: polar(180 - phi, z, 1.75), lambda p: Vector((p.x, p.y - 1.75, 0)))
TAIL_R = Surface(lambda phi, z: polar(phi, z, -2.65), lambda p: Vector((p.x, p.y + 2.65, 0)))
TAIL_L = Surface(lambda phi, z: polar(180 - phi, z, -2.65), lambda p: Vector((p.x, p.y + 2.65, 0)))


def round_poly(pts, r, n=4):
    """Rounds a polygon's corners (r: a radius, or one per corner)."""
    out = []
    m = len(pts)
    for i, p in enumerate(pts):
        rad = r[i] if isinstance(r, (list, tuple)) else r
        p = np.array(p, float)
        if rad <= 0:
            out.append(tuple(p))
            continue
        a, b = np.array(pts[i - 1], float), np.array(pts[(i + 1) % m], float)
        da, db = a - p, b - p
        la, lb = np.linalg.norm(da), np.linalg.norm(db)
        k = min(rad, la * 0.45, lb * 0.45)
        q0, q1 = p + da / la * k, p + db / lb * k
        for j in range(n + 1):
            t = j / n
            out.append(tuple((1 - t) ** 2 * q0 + 2 * t * (1 - t) * p + t * t * q1))
    return out


def _row(poly, v):
    """The span of a polygon along u at height v."""
    us = []
    m = len(poly)
    for i in range(m):
        (u0, v0), (u1, v1) = poly[i], poly[(i + 1) % m]
        if (v0 <= v <= v1) or (v1 <= v <= v0):
            us += [u0, u1] if abs(v1 - v0) < 1e-9 else [u0 + (u1 - u0) * (v - v0) / (v1 - v0)]
    return (min(us), max(us)) if us else None


def patch(name, surface, poly, material, off=0.008, du=0.08, dv=0.08, sink=0.012):
    """A thin plate lying on a surface, its outline a polygon in the surface's (u, v), one span wide on every row of v
    (convex shapes, or ones with no re-entrant corners across v). Its edge goes down below the surface."""
    vs = sorted(set(round(p[1], 5) for p in poly))
    rows = []
    for a, b in zip(vs, vs[1:]):
        n = max(1, int(math.ceil((b - a) / dv)))
        rows += [a + (b - a) * i / n for i in range(n)]
    rows.append(vs[-1])
    eps = 1e-5
    rows[0] += eps
    rows[-1] -= eps
    spans = [_row(poly, v) for v in rows]
    width = max(s[1] - s[0] for s in spans)
    nu = max(1, int(math.ceil(width / du)))
    bm = bmesh.new()
    top = [[bm.verts.new(surface.at(s[0] + (s[1] - s[0]) * i / nu, v, off)) for i in range(nu + 1)] for s, v in zip(spans, rows)]
    bot = [[bm.verts.new(surface.at(s[0] + (s[1] - s[0]) * i / nu, v, -sink)) for i in range(nu + 1)] for s, v in zip(spans, rows)]

    def quad(a, b, c, d):
        if (a.co - c.co).length > 1e-7 and (b.co - d.co).length > 1e-7:
            try:
                bm.faces.new((a, b, c, d))
            except ValueError:
                pass
    for j in range(len(rows) - 1):
        for i in range(nu):
            quad(top[j][i], top[j][i + 1], top[j + 1][i + 1], top[j + 1][i])
        quad(top[j][0], top[j + 1][0], bot[j + 1][0], bot[j][0])
        quad(top[j][nu], bot[j][nu], bot[j + 1][nu], top[j + 1][nu])
    for i in range(nu):
        quad(top[0][i], bot[0][i], bot[0][i + 1], top[0][i + 1])
        quad(top[-1][i], top[-1][i + 1], bot[-1][i + 1], bot[-1][i])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_obj(name, bm, material, smooth_angle=30.0)


def ribbon(name, surface, line, width, material, off=0.003, step=0.05):
    """A narrow strip along a polyline on a surface (shut lines, trim, rails)."""
    pts = []
    for a, b in zip(line, line[1:]):
        n = max(1, int(math.ceil(math.hypot(b[0] - a[0], b[1] - a[1]) / step)))
        pts += [(a[0] + (b[0] - a[0]) * i / n, a[1] + (b[1] - a[1]) * i / n) for i in range(n)]
    pts.append(line[-1])
    bm = bmesh.new()
    rows = []
    m = len(pts)
    for i, p in enumerate(pts):
        a, b = pts[max(i - 1, 0)], pts[min(i + 1, m - 1)]
        tu, tv = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(tu, tv) or 1.0
        nu, nv = -tv / ln * width / 2, tu / ln * width / 2
        rows.append((bm.verts.new(surface.at(p[0] + nu, p[1] + nv, off)), bm.verts.new(surface.at(p[0] - nu, p[1] - nv, off)),
                     bm.verts.new(surface.at(p[0] - nu, p[1] - nv, -0.006)), bm.verts.new(surface.at(p[0] + nu, p[1] + nv, -0.006))))
    for r0, r1 in zip(rows, rows[1:]):
        for i in range(4):
            bm.faces.new((r0[i], r0[(i + 1) % 4], r1[(i + 1) % 4], r1[i]))
    bm.faces.new(rows[0])
    bm.faces.new(rows[-1][::-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_obj(name, bm, material, smooth_angle=40.0)


def mesh_obj(name, bm, material, uv="box", smooth_angle=40.0):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(material)
    o["uv"] = uv
    o["smooth"] = True
    o["smooth_angle"] = smooth_angle
    return o


def sweep(name, rows, material, smooth_angle=40.0):
    """A closed solid swept through rows of points (each row a closed loop, same count), capped at both ends."""
    bm = bmesh.new()
    vr = [[bm.verts.new(p) for p in row] for row in rows]
    n = len(rows[0])
    for a, b in zip(vr, vr[1:]):
        for i in range(n):
            i2 = (i + 1) % n
            bm.faces.new((a[i], a[i2], b[i2], b[i]))
    bm.faces.new(vr[0])
    bm.faces.new(list(reversed(vr[-1])))
    return mesh_obj(name, bm, material, smooth_angle=smooth_angle)


MATS = {}


def materials():
    MATS.update(
        paint=mat("tex_transit_body_tint", (0.88, 0.89, 0.88), 0.28, 0.1),
        glass=mat("flat_glass", (0.035, 0.04, 0.045), 0.03, 0.0),
        trim=mat("flat_trim_gloss", (0.025, 0.025, 0.028), 0.18, 0.0),
        rubber=mat("flat_rubber", (0.03, 0.03, 0.03), 0.8, 0.0),
        plastic=mat("flat_plastic_black", (0.055, 0.055, 0.058), 0.62, 0.0),
        backing=mat("flat_backing", (0.012, 0.012, 0.013), 0.85, 0.0),
        gap=mat("flat_gap", (0.012, 0.012, 0.012), 0.9, 0.0),
        liner=mat("flat_liner", (0.03, 0.028, 0.026), 0.95, 0.0),
        chassis=mat("flat_chassis", (0.05, 0.05, 0.05), 0.8, 0.3),
        steel_wheel=mat("flat_wheel_silver", (0.60, 0.61, 0.62), 0.38, 0.75),
    )


# ============================================================================================ body
def z_levels():
    zs = set()
    for a, b, s in ((Z_BOT, 0.84, 0.024), (0.84, 1.20, 0.03), (1.20, 1.62, 0.014), (1.62, 2.28, 0.05), (2.28, 2.70, 0.022), (2.70, Z_TOP, 0.0072)):
        for z in np.arange(a, b, s):
            zs.add(round(float(z), 4))
    for z, _ in W_KEYS:
        zs.add(round(z, 4))
    for z in (Z_BOT, Z_TOP, Z_SILL, Z_CLAD0, Z_BUMPER, 0.405, 0.54, 0.735, 0.755):
        zs.add(round(z, 4))
    zs = sorted(zs)
    out = []
    keep = {round(z, 4) for z in (Z_SILL, Z_CLAD0, Z_BUMPER, 0.405, 0.54, 0.735)}
    for z in zs:
        if out and z - out[-1] < 0.0045 and z not in keep:
            continue
        out.append(z)
    return out


N_FRONT, N_SIDE, N_CORNER, N_BACK = 15, 84, 6, 8


def ring_at(z):
    """The body's outline at height z, right half from the nose's centre to the back's centre, as (x, y, uv u)."""
    w, yc, yf = W(z), YC(z), YF(z)
    p = nose_p(z)
    a = max(yf - yc, 1e-3)
    yr, ar = YR(z), AR(z)
    pts = []
    for i in range(N_FRONT):
        th = math.pi / 2 * (1 - i / N_FRONT)
        pts.append((w * math.cos(th) ** (2 / p), yc + a * math.sin(th) ** (2 / p)))
    y0, y1 = yc, yr + ar
    for i in range(N_SIDE + 1):
        y = y0 + (y1 - y0) * i / N_SIDE
        pts.append((w + side_disp(y, z), y))
    for i in range(1, N_CORNER):
        th = math.pi / 2 * i / N_CORNER
        pts.append((w - ar + ar * math.cos(th), yr + ar - ar * math.sin(th)))
    for i in range(N_BACK + 1):
        pts.append(((w - ar) * (1 - i / N_BACK), yr))
    return pts


def body_region(x, y, z):
    """Black mouldings by height: sill, side cladding and the bumpers; the rest is paint."""
    if z < Z_SILL or Z_CLAD0 <= z < Z_BUMPER:
        return "plastic"
    if z < Z_CLAD0 and (y > Y_FA - ARCH_R - 0.02 or y < Y_RA + ARCH_R + 0.02):
        return "plastic"
    return "paint"


def build_body():
    zs = z_levels()
    half = ring_at(zs[0])
    nh = len(half)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    rings = []
    for z in zs:
        h = ring_at(z)
        ring = [bm.verts.new((x, y, z)) for x, y in h]                                  # nose -> back, right side
        ring += [bm.verts.new((-x, y, z)) for x, y in reversed(h[1:-1])]                # back -> nose, left side
        rings.append(ring)
    nr = len(rings[0])
    order = ["paint", "plastic"]
    # UVs for the baked set: round the outline by arc length (on the mid-height ring), up by arc length of the profile.
    mid = ring_at(1.2)
    arc = [0.0]
    for a, b in zip(mid, mid[1:]):
        arc.append(arc[-1] + math.hypot(b[0] - a[0], b[1] - a[1]))
    total = arc[-1] * 2
    ucoord = [s / total for s in arc] + [1 - s / total for s in reversed(arc[1:-1])]
    prof = [0.0]
    for z0, z1 in zip(zs, zs[1:]):
        prof.append(prof[-1] + math.hypot(z1 - z0, YF(z1) - YF(z0)))
    for j in range(len(zs) - 1):
        for i in range(nr):
            i2 = (i + 1) % nr
            quad = (rings[j][i], rings[j][i2], rings[j + 1][i2], rings[j + 1][i])
            f = bm.faces.new(quad)
            c = f.calc_center_median()
            f.material_index = order.index(body_region(c.x, c.y, c.z))
            for loop, (jj, ii) in zip(f.loops, ((j, i), (j, i2), (j + 1, i2), (j + 1, i))):
                u = ucoord[ii]
                if i2 == 0 and ii == 0:
                    u = 1.0
                loop[uvl].uv = (u, prof[jj] / prof[-1])
    bot = bm.faces.new(rings[0][::-1])
    bot.material_index = order.index("plastic")
    top = bm.faces.new(rings[-1])
    top.material_index = order.index("paint")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("body")
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new("body", me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(MATS["paint"])
    o.data.materials.append(MATS["plastic"])
    o["uv"] = "keep"
    o["smooth"] = True
    o["smooth_angle"] = 38.0
    print(f"[transit] body: {len(zs)} levels x {nr} = {len(me.polygons)} faces", flush=True)
    return o


def cut_wheel_wells(body):
    cutters = []
    for y in (Y_FA, Y_RA):
        for sx in (-1, 1):
            c = cyl("cut_well", ARCH_R, 0.62, (sx * 1.0, y, ARCH_Z), MATS["liner"], verts=48, rot=(0, 90, 0))
            cutters.append(c)
    for c in cutters:
        mod = body.modifiers.new("well", 'BOOLEAN')
        mod.object = c
        mod.operation = 'DIFFERENCE'
        mod.solver = 'EXACT'
        try:
            mod.material_mode = 'TRANSFER'
        except Exception:
            pass
        c.hide_render = True
        c.hide_set(True)
    bpy.ops.object.select_all(action='DESELECT')
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    for m in list(body.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)
    for c in cutters:
        bpy.data.objects.remove(c)
    return body


# ============================================================================================ wheels
def tyre(name):
    """A 215/65 R16 van tyre around the X axis at the origin: tread (the tread texture), plain rubber sidewalls."""
    R, H = R_TYRE, W_TYRE / 2
    prof = [(0.208, -H * 0.86), (0.255, -H * 0.97), (0.300, -H), (0.328, -H * 0.94), (R - 0.006, -H * 0.82), (R, -H * 0.68),
            (R + 0.001, -H * 0.3), (R + 0.001, 0.0), (R + 0.001, H * 0.3), (R, H * 0.68), (R - 0.006, H * 0.82), (0.328, H * 0.94),
            (0.300, H), (0.255, H * 0.97), (0.208, H * 0.86)]
    tread0, tread1 = 4, 10
    seg = 64
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    rings = []
    for r, a in prof:
        rings.append([bm.verts.new((a, r * math.cos(math.tau * i / seg), r * math.sin(math.tau * i / seg))) for i in range(seg)])
    arc = [0.0]
    for (r0, a0), (r1, a1) in zip(prof, prof[1:]):
        arc.append(arc[-1] + math.hypot(r1 - r0, a1 - a0))
    for j in range(len(prof) - 1):
        for i in range(seg):
            i2 = (i + 1) % seg
            f = bm.faces.new((rings[j][i], rings[j][i2], rings[j + 1][i2], rings[j + 1][i]))
            tread = tread0 <= j < tread1
            f.material_index = 0 if tread else 1
            for loop, (jj, ii) in zip(f.loops, ((j, i), (j, i + 1), (j + 1, i + 1), (j + 1, i))):
                if tread:
                    loop[uvl].uv = (4.0 * ii / seg, (arc[jj] - arc[tread0]) / (arc[tread1] - arc[tread0]))
                else:
                    loop[uvl].uv = (ii / seg, arc[jj] * 4)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(mat("tex_tyre_tread", None, 0.9, 0.0))
    o.data.materials.append(mat("flat_tyre_wall", (0.045, 0.045, 0.047), 0.82, 0.0))
    o["uv"] = "keep"
    o["smooth"] = True
    o["smooth_angle"] = 50.0
    return o


def wheel(name):
    """A plain 16 inch steel wheel: a dished silver disc with a ring of oval slots, a black hub with five exposed
    nuts (the photo's). Axle along X, outer face towards +X, centred at the origin."""
    steel = MATS["steel_wheel"]
    objs = [tyre(name + "_tyre")]
    # Rim and dished disc, one lathe from the barrel over the lip and down the disc to the hub.
    objs.append(K.lathe(name + "_rim", [(0.192, -0.09), (0.192, 0.055), (0.204, 0.078), (0.214, 0.090), (0.206, 0.094), (0.190, 0.072), (0.178, 0.050),
                                        (0.150, 0.046), (0.128, 0.058), (0.104, 0.062), (0.086, 0.074), (0.078, 0.082)],
                        material=steel, verts=64, rot=(0, 90, 0)))
    # The hub: a black cap over the nuts' circle, the nuts standing out of it.
    black = mat("flat_hub_black", (0.035, 0.035, 0.038), 0.45, 0.2)
    objs.append(K.lathe(name + "_hub", [(0.080, 0.078), (0.074, 0.092), (0.042, 0.096), (0.036, 0.112), (0.0, 0.114)], material=black, verts=36, rot=(0, 90, 0)))
    nut = mat("flat_nut", (0.55, 0.55, 0.55), 0.35, 1.0)
    for i in range(5):
        a = math.tau * (i + 0.25) / 5
        objs.append(cyl(name + "_nut", 0.0115, 0.022, (0.098, 0.060 * math.cos(a), 0.060 * math.sin(a)), nut, verts=6, rot=(0, 90, 0)))
    # Ten oval slots round the disc.
    slot = MATS["backing"]
    for i in range(10):
        a = math.tau * i / 10
        c = cyl(name + "_slot", 0.016, 0.006, (0.0, 0, 0), slot, verts=14, rot=(0, 90, 0))
        c.scale = (1.0, 1.0, 1.0)
        bpy.context.view_layer.update()
        c.matrix_world = (Matrix.Rotation(a, 4, 'X') @ Matrix.Translation((0.054, 0, 0.139)) @ Matrix.Scale(1.7, 4, (0, 0, 1)) @ Matrix.Rotation(math.radians(90), 4, 'Y'))
        objs.append(c)
    objs.append(cyl(name + "_disc", 0.160, 0.024, (0.0, 0, 0), mat("steel"), verts=40, rot=(0, 90, 0)))
    return objs


def place(objs, loc, yaw=0.0):
    bpy.context.view_layer.update()
    m = Matrix.Translation(loc) @ Matrix.Rotation(yaw, 4, 'Z')
    for o in objs:
        o.matrix_world = m @ o.matrix_world
    return objs


def wheels():
    out = []
    for y in (Y_FA, Y_RA):
        for sx in (-1, 1):
            w = wheel(f"wheel_{'f' if y > 0 else 'r'}{'r' if sx > 0 else 'l'}")
            place(w, Vector((sx * TRACK / 2, y, R_TYRE)), yaw=0.0 if sx > 0 else math.pi)
            out += w
    return out


def flares():
    """Black wheel-arch mouldings following the side round each opening, wider over the top."""
    loop = [(-0.006, -0.012), (-0.005, 0.0), (0.012, 0.012), (0.042, 0.016), (0.068, 0.010), (0.080, -0.002), (0.082, -0.010), (0.04, -0.03)]
    out = []
    for yc in (Y_FA, Y_RA):
        for sx in (-1, 1):
            rows = []
            for th in np.linspace(math.radians(-4), math.radians(184), 52):
                row = []
                for dr, dx in loop:
                    r = ARCH_R + dr
                    y = yc + r * math.cos(th)
                    z = max(ARCH_Z + r * math.sin(th), Z_BOT + 0.02)
                    x = side_x(y, z) + dx
                    row.append((sx * x, y, z))
                rows.append(row)
            out.append(sweep("flare", rows, MATS["plastic"], smooth_angle=50.0))
    return out


# ============================================================================================ glass and shut lines
# The cab door's window in the side's (y, z): the sill falls towards the front (the Mk8's dropped window line), the
# leading edge follows the A pillar and the top is level with the roof step.
WIN_CAB = [(0.075, 1.555), (1.02, 1.385), (1.335, 1.545), (0.665, 2.240), (0.075, 2.285)]
QUARTER = [(1.04, 1.320), (1.40, 1.340), (1.47, 1.460), (1.335, 1.555), (1.02, 1.395)]


def side_parts():
    objs = []
    gap = MATS["gap"]
    trim = MATS["trim"]
    for s, sx in ((SIDE_R, 1), (SIDE_L, -1)):
        # Cab door window: a rubber surround, the glass inside it, and the black triangle at its foot behind the mirror.
        objs.append(patch("win_surround", s, round_poly(WIN_CAB, 0.05), MATS["rubber"], off=0.005))
        inner = [(0.105, 1.572), (1.01, 1.412), (1.285, 1.550), (0.655, 2.205), (0.105, 2.255)]
        objs.append(patch("win_glass", s, round_poly(inner, 0.04), MATS["glass"], off=0.008))
        objs.append(patch("quarter", s, round_poly(QUARTER, 0.02), trim, off=0.006))
        # Shut lines: the cab door, the sliding door, the track it runs on.
        door = [(1.30, 0.81), (1.27, 1.26), (1.46, 1.46), (1.36, 1.62), (0.50, 2.30), (0.02, 2.345), (0.02, 0.44)]
        objs.append(ribbon("shut", s, door, 0.007, gap))
        objs.append(ribbon("shut", s, [(1.30, 0.44), (1.30, 0.81)], 0.007, gap))
        objs.append(ribbon("shut", s, [(0.02, 0.44), (1.30, 0.44)], 0.007, gap))
        objs.append(ribbon("shut", s, [(-0.02, 0.44), (-0.02, 2.345), (-1.44, 2.345), (-1.44, 0.44), (-0.02, 0.44)], 0.007, gap))
        objs.append(ribbon("sliding_track", s, [(-1.46, 1.075), (-2.89, 1.075)], 0.030, MATS["rubber"], off=0.004))
        # The sliding door's rear lower rail down in the cladding, and the fuel flap on the driver's side.
        objs.append(patch("handle", s, round_poly([(0.145, 1.165), (0.315, 1.165), (0.315, 1.207), (0.145, 1.207)], 0.018), MATS["plastic"], off=0.022, du=0.05, dv=0.02))
        objs.append(patch("handle", s, round_poly([(-0.235, 1.165), (-0.065, 1.165), (-0.065, 1.207), (-0.235, 1.207)], 0.018), MATS["plastic"], off=0.020, du=0.05, dv=0.02))
        # Lock barrel under the handle.
        objs.append(patch("lock", s, [(0.215, 1.085), (0.245, 1.085), (0.245, 1.115), (0.215, 1.115)], MATS["plastic"], off=0.006, du=0.03, dv=0.03))
        # Side indicator behind the front wheel arch.
        objs.append(patch("repeater", s, round_poly([(1.08, 0.86), (1.16, 0.86), (1.16, 0.90), (1.08, 0.90)], 0.015), mat("flat_amber", (0.85, 0.45, 0.08), 0.1, 0.0), off=0.006, du=0.04, dv=0.02))
        # Mirrors: a tall black housing on two arms from the door, the glass facing back, the indicator lens on the
        # housing's front-outer corner.
        mx = sx * 1.185
        my, mz = 1.15, 1.70
        objs.append(box("mirror", (0.175, 0.115, 0.345), (mx, my, mz), MATS["plastic"], rot=(0, 0, sx * -5), bevel=0.028))
        objs.append(box("mirror_glass", (0.145, 0.006, 0.310), (mx, my - 0.059, mz), mat("flat_mirror", (0.62, 0.64, 0.66), 0.02, 1.0), rot=(0, 0, sx * -5), bevel=0.008))
        objs.append(box("mirror_lens", (0.062, 0.03, 0.085), (mx + sx * 0.07, my + 0.052, mz - 0.11), mat("flat_lens_clear", (0.85, 0.82, 0.70), 0.08, 0.3), rot=(0, 0, sx * -5), bevel=0.008))
        for dz in (-0.115,):
            x0 = side_x(my, mz + dz)
            objs.append(box("mirror_arm", (mx * sx - 0.06 - x0 + 0.03, 0.075, 0.085), (sx * (x0 + mx * sx - 0.06) / 2, my, mz + dz), MATS["plastic"], bevel=0.008))
        # A black step-rubbing strip low on the sliding door.
    return objs


def windscreen():
    objs = []
    screen = [(-0.895, 1.565), (0.895, 1.565), (0.745, 2.255), (-0.745, 2.255)]
    objs.append(patch("screen_surround", FRONT, round_poly(screen, 0.07), MATS["rubber"], off=0.005, du=0.12, dv=0.05))
    inner = [(-0.865, 1.590), (0.865, 1.590), (0.720, 2.230), (-0.720, 2.230)]
    objs.append(patch("screen", FRONT, round_poly(inner, 0.06), MATS["glass"], off=0.008, du=0.12, dv=0.05))
    # The dark band behind the mirror at the top, and the black cowl with its vents at the foot.
    objs.append(patch("screen_band", FRONT, round_poly([(-0.10, 2.05), (0.10, 2.05), (0.17, 2.225), (-0.17, 2.225)], 0.02), MATS["trim"], off=0.0095, du=0.08, dv=0.05))
    objs.append(patch("cowl", FRONT, round_poly([(-0.93, 1.525), (0.93, 1.525), (0.90, 1.575), (-0.90, 1.575)], 0.02), MATS["plastic"], off=0.004, du=0.12, dv=0.02))
    # Wipers parked along the foot, from spindles on the driver's and centre, with their arms.
    for x0, x1 in ((-0.72, 0.03), (0.08, 0.80)):
        z0, z1 = 1.612, 1.596
        pts = [(x0 + (x1 - x0) * k / 6, z0 + (z1 - z0) * k / 6) for k in range(7)]
        objs.append(ribbon("wiper", FRONT, pts, 0.016, MATS["rubber"], off=0.022, step=0.1))
        objs.append(ribbon("wiper_arm", FRONT, [(x0 + 0.12, 1.566), (x0 + (x1 - x0) * 0.72, 1.610)], 0.020, MATS["rubber"], off=0.030, step=0.1))
    return objs


def bonnet():
    """The bonnet's shut lines and its two black nostrils in front of the screen."""
    objs = []
    gap = MATS["gap"]
    edge = [(-0.74, 1.200), (-0.84, 1.255), (-0.92, 1.335), (-0.965, 1.470), (-0.975, 1.522)]
    for sign in (-1, 1):
        objs.append(ribbon("shut", FRONT, [(sign * -x, z) for x, z in edge], 0.007, gap, step=0.03))
    objs.append(ribbon("shut", FRONT, [(-0.74, 1.200), (0.74, 1.200)], 0.007, gap, step=0.1))
    for sx in (-1, 1):
        # A vent: a tapered black slot lying along the bonnet just ahead of the cowl, with a few louvre bars.
        poly = [(sx * 0.17, 1.4400), (sx * 0.57, 1.4400), (sx * 0.62, 1.4670), (sx * 0.14, 1.4670)]
        objs.append(patch("vent", FRONT, poly if sx > 0 else [(-p[0], p[1]) for p in poly[::1]], MATS["plastic"], off=0.012, du=0.07, dv=0.008))
        for k in range(3):
            z = 1.447 + k * 0.0075
            objs.append(ribbon("vent_bar", FRONT, [(sx * 0.19, z), (sx * 0.56, z)], 0.008, MATS["backing"], off=0.0135, step=0.1))
    return objs


# ============================================================================================ nose
def ellipse(cu, cv, ru, rv, n=24):
    return [(cu + ru * math.cos(math.tau * i / n), cv + rv * math.sin(math.tau * i / n)) for i in range(n)]


def front_end():
    objs = []
    trim = MATS["trim"]
    # The grille: a big black shape, wider at the top corners, with its horizontal bars and three uprights.
    outer = [(-0.525, 0.790), (0.525, 0.790), (0.575, 0.900), (0.590, 1.125), (0.530, 1.178), (-0.530, 1.178), (-0.590, 1.125), (-0.575, 0.900)]
    objs.append(patch("grille_frame", FRONT, round_poly(outer, 0.05), trim, off=0.016, du=0.1, dv=0.05))
    inner = [(-0.480, 0.825), (0.480, 0.825), (0.535, 0.905), (0.548, 1.105), (0.495, 1.145), (-0.495, 1.145), (-0.548, 1.105), (-0.535, 0.905)]
    objs.append(patch("grille_back", FRONT, round_poly(inner, 0.04), MATS["backing"], off=0.0185, du=0.1, dv=0.05))
    black = MATS["plastic"]
    for k in range(6):
        z = 0.862 + k * 0.0515
        half = _row(inner, z)[1] - 0.012
        objs.append(patch("grille_bar", FRONT, [(-half, z - 0.011), (half, z - 0.011), (half, z + 0.011), (-half, z + 0.011)], black, off=0.030, du=0.2, dv=0.03, sink=-0.005))
    for x in (-0.33, 0.33):
        objs.append(patch("grille_post", FRONT, [(x - 0.011, 0.835), (x + 0.011, 0.835), (x + 0.011, 1.135), (x - 0.011, 1.135)], black, off=0.0315, du=0.03, dv=0.08, sink=-0.005))
    # The badge: a blue oval with a bright rim. (No lettering: the oval reads at this size.)
    objs.append(patch("badge_rim", FRONT, ellipse(0, 0.985, 0.105, 0.052), mat("flat_badge_chrome", (0.85, 0.86, 0.88), 0.14, 1.0), off=0.040, du=0.03, dv=0.015, sink=-0.012))
    objs.append(patch("badge", FRONT, ellipse(0, 0.985, 0.092, 0.043), mat("flat_badge_blue", (0.03, 0.11, 0.36), 0.2, 0.2), off=0.043, du=0.03, dv=0.015, sink=-0.010))
    # Headlamps: long units from the grille's top corner back along the wing, a chrome reflector and projector in each.
    lamp = mat("flat_headlamp", (0.52, 0.55, 0.58), 0.05, 0.5)
    for s in (NOSE_R, NOSE_L):
        poly = [(66.0, 1.205), (46.0, 1.228), (28.0, 1.288), (15.0, 1.305), (21.0, 1.215), (36.0, 1.085), (50.0, 0.985), (61.0, 0.960), (67.0, 1.015)]
        objs.append(patch("lamp_bezel", s, round_poly(poly, 2.5), trim, off=0.006, du=4.0, dv=0.03))
        lens = [(65.0, 1.190), (46.5, 1.214), (29.0, 1.273), (18.5, 1.290), (23.0, 1.215), (37.0, 1.095), (50.5, 1.000), (60.5, 0.977), (65.8, 1.025)]
        objs.append(patch("lamp_lens", s, round_poly(lens, 2.0), lamp, off=0.010, du=4.0, dv=0.03))
        objs.append(patch("lamp_inner", s, round_poly([(63.5, 1.165), (55.0, 1.180), (47.0, 1.190), (48.5, 1.040), (58.0, 1.005), (63.5, 1.040)], 1.5), mat("flat_lamp_chrome", (0.80, 0.81, 0.83), 0.12, 1.0), off=0.012, du=3.0, dv=0.03))
        for phi, r in ((59.5, 0.052), (51.5, 0.046)):
            objs.append(patch("lamp_projector", s, [(phi + r * 58 * math.cos(math.tau * i / 16) / 1.3, 1.095 + r * math.sin(math.tau * i / 16)) for i in range(16)], mat("flat_lens_clear", (0.85, 0.82, 0.70), 0.08, 0.3), off=0.0135, du=1.5, dv=0.02))
        objs.append(patch("lamp_indicator", s, round_poly([(44.5, 1.210), (24.0, 1.272), (25.0, 1.225), (38.0, 1.150)], 1.0), mat("flat_lamp_amber", (0.66, 0.47, 0.26), 0.08, 0.2), off=0.012, du=3.0, dv=0.02))
    # The bumper: a lip under the grille, the lower intake, the backing for the number plate (build_plates.py), fog lamps in dark round pockets.
    objs.append(patch("intake", FRONT, round_poly([(-0.48, 0.445), (0.48, 0.445), (0.52, 0.565), (-0.52, 0.565)], 0.03), MATS["backing"], off=0.006, du=0.15, dv=0.05))
    objs.append(patch("plate_recess", FRONT, round_poly([(-0.30, 0.605), (0.30, 0.605), (0.30, 0.735), (-0.30, 0.735)], 0.02), MATS["plastic"], off=0.012, du=0.15, dv=0.04))
    for sx in (-1, 1):
        objs.append(patch("fog_pocket", FRONT, ellipse(sx * 0.70, 0.565, 0.082, 0.062), MATS["trim"], off=0.006, du=0.05, dv=0.03))
        objs.append(patch("fog_lens", FRONT, ellipse(sx * 0.70, 0.565, 0.046, 0.044), mat("flat_foglamp", (0.80, 0.80, 0.76), 0.08, 0.2), off=0.010, du=0.03, dv=0.02))
    return objs


# ============================================================================================ back
def rear_end():
    objs = []
    gap = MATS["gap"]
    paint = MATS["paint"]
    # Twin barn doors: the split down the middle, their outline, a shallow pressed panel in each, a grab handle.
    objs.append(ribbon("shut", BACK, [(0, Z_BUMPER + 0.03), (0, 2.46)], 0.008, gap, step=0.12))
    objs.append(ribbon("shut", BACK, [(-0.78, 2.46), (0.78, 2.46)], 0.008, gap, step=0.12))
    for sx in (-1, 1):
        objs.append(ribbon("shut", BACK, [(sx * 0.78, Z_BUMPER + 0.03), (sx * 0.78, 2.46)], 0.008, gap, step=0.12))
        # Tail lamps: tall units up the corners; red over amber over red, wrapping onto the side.
        s = TAIL_R if sx > 0 else TAIL_L
        z0, z1 = 0.98, 1.72
        lamp = [(-19.0, z0), (-34.0, z0), (-34.0, z1), (-19.0, z1)]
        objs.append(patch("tail_bezel", s, round_poly(lamp, 1.0), MATS["trim"], off=0.006, du=1.0, dv=0.06))
        for (za, zb, m) in ((1.00, 1.235, mat("flat_tail_lens", (0.46, 0.03, 0.035), 0.06, 0.1)),
                            (1.245, 1.40, mat("flat_amber", (0.85, 0.45, 0.08), 0.1, 0.0)),
                            (1.41, 1.54, mat("flat_lens_clear", (0.85, 0.82, 0.70), 0.08, 0.3)),
                            (1.55, 1.69, mat("flat_tail_lens", (0.46, 0.03, 0.035), 0.06, 0.1))):
            objs.append(patch("tail_lens", s, round_poly([(-19.7, za), (-33.3, za), (-33.3, zb), (-19.7, zb)], 0.5), m, off=0.0095, du=1.0, dv=0.06))
        # The bumper's reflector.
        objs.append(patch("reflector", BACK, round_poly([(sx * 0.60, 0.565), (sx * 0.82, 0.565), (sx * 0.82, 0.60), (sx * 0.60, 0.60)] if sx > 0 else [(sx * 0.82, 0.565), (sx * 0.60, 0.565), (sx * 0.60, 0.60), (sx * 0.82, 0.60)], 0.008), mat("flat_reflector", (0.5, 0.04, 0.04), 0.2, 0.0), off=0.006, du=0.1, dv=0.02))
    # Door handle and lock, the backing for the number plate over the step (build_plates.py has the plates), the high-level brake lamp.
    objs.append(patch("rear_handle", BACK, round_poly([(0.05, 1.215), (0.24, 1.215), (0.24, 1.265), (0.05, 1.265)], 0.018), MATS["plastic"], off=0.02, du=0.06, dv=0.03))
    objs.append(patch("plate_back", BACK, round_poly([(-0.275, 0.608), (0.275, 0.608), (0.275, 0.742), (-0.275, 0.742)], 0.012), MATS["plastic"], off=0.006, du=0.15, dv=0.04))
    objs.append(patch("brake_light", BACK, round_poly([(-0.16, 2.495), (0.16, 2.495), (0.16, 2.525), (-0.16, 2.525)], 0.008), mat("flat_brake_lens", (0.5, 0.03, 0.03), 0.08, 0.1), off=0.006, du=0.1, dv=0.02))
    # The tread plate along the bumper.
    objs.append(box("step_pad", (1.70, 0.10, 0.018), (0, Y_R + 0.07, 0.565), MATS["rubber"], bevel=0.005))
    return objs


# ============================================================================================ roof, underbody
def roof_parts():
    objs = []
    # Shallow transverse ribs on the high roof's load section, painted as the van.
    paint_flat = mat("flat_paint_tint", (0.88, 0.89, 0.88), 0.3, 0.1)
    for y in np.linspace(-2.55, -0.75, 8):
        y = float(y)
        rows = []
        for x in np.linspace(-0.40, 0.40, 9):
            x = float(x)
            # z of the roof above (x, y): the highest level whose outline still reaches this point.
            lo, hi = 2.6, Z_TOP
            for _ in range(18):
                m = (lo + hi) / 2
                if inside(x, y, m):
                    lo = m
                else:
                    hi = m
            rows.append([(x, y - 0.035, lo - 0.01), (x, y - 0.012, lo + 0.012), (x, y + 0.012, lo + 0.012), (x, y + 0.035, lo - 0.01)])
        objs.append(sweep("roof_rib", rows, paint_flat, smooth_angle=60.0))
    # A shut line where the high roof's moulding meets the cab's, and the whip aerial leaning back from over the screen.
    objs.append(K.tube("aerial", [(0.0, 0.14, 2.56), (0.0, -0.05, 2.80), (0.0, -0.30, 3.02)], 0.0035, MATS["rubber"], verts=6))
    objs.append(box("aerial_base", (0.05, 0.09, 0.035), (0, 0.16, 2.545), MATS["rubber"], rot=(22, 0, 0), bevel=0.01))
    return objs


def underbody():
    dark = MATS["chassis"]
    objs = []
    for sx in (-1, 1):
        objs.append(box("rail", (0.09, 5.3, 0.14), (sx * 0.48, -0.3, 0.30), dark))
        objs.append(box("leaf_spring", (0.07, 1.1, 0.05), (sx * 0.56, Y_RA, 0.25), dark))
    objs.append(box("engine", (0.70, 0.9, 0.34), (0, 1.90, 0.47), dark, bevel=0.03))
    # The dark under the van, on the ground (2 cm up, to clear the aprons): indoors nothing else shadows the floor there.
    objs.append(box("ground_shadow", (1.76, 5.00, 0.004), (0, (Y_F + Y_R) / 2, 0.02), mat("flat_ground_shadow", (0.006, 0.006, 0.006), 1.0, 0.0)))
    objs.append(box("floor_pan", (1.70, 4.7, 0.03), (0, -0.4, 0.345), dark))
    objs.append(box("fuel_tank", (0.80, 0.75, 0.20), (0.15, -0.3, 0.26), dark, bevel=0.03))
    objs.append(cyl("rear_axle", 0.04, TRACK - 0.15, (0, Y_RA, R_TYRE), dark, verts=12, rot=(0, 90, 0)))
    objs.append(cyl("rear_diff", 0.12, 0.2, (0, Y_RA, R_TYRE), dark, verts=20, rot=(90, 0, 0)))
    objs.append(cyl("prop_shaft", 0.035, 3.2, (0, Y_RA + 1.7, 0.33), dark, verts=10, rot=(90, 0, 0)))
    objs.append(cyl("front_axle", 0.03, TRACK - 0.15, (0, Y_FA, R_TYRE), dark, verts=10, rot=(0, 90, 0)))
    objs.append(K.tube("exhaust_pipe", [(-0.30, 1.30, 0.28), (-0.45, 0.20, 0.27), (-0.60, -1.60, 0.28), (-0.62, -3.07, 0.30)], 0.03,
                       mat("flat_exhaust", (0.12, 0.11, 0.10), 0.6, 0.7), verts=8))
    return objs


# ============================================================================================ paint and road grime
def grime_material():
    """The bake shader: how much road film, grime and rust-brown streaking lies on the paint (the van has stood about
    in a derelict factory): thick low down and behind the wheels, streaks running down from the roof and the window
    corners, a dull film over the top surfaces."""
    m = bpy.data.materials.new("grime_bake")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    N, L = nt.nodes.new, nt.links.new

    def math_(op, a, b=None, clamp=False):
        n = N("ShaderNodeMath"); n.operation = op; n.use_clamp = clamp
        for i, v in enumerate((a, b)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                L(v, n.inputs[i])
        return n.outputs[0]

    def ramp(v, a, b):
        n = N("ShaderNodeMapRange"); n.interpolation_type = 'SMOOTHSTEP'
        L(v, n.inputs["Value"]); n.inputs["From Min"].default_value = a; n.inputs["From Max"].default_value = b
        return n.outputs["Result"]

    def noise(vec, scale, detail=4.0):
        n = N("ShaderNodeTexNoise"); n.inputs["Scale"].default_value = scale; n.inputs["Detail"].default_value = detail
        L(vec, n.inputs["Vector"])
        return n.outputs["Factor"]

    geo = N("ShaderNodeNewGeometry")
    pos = geo.outputs["Position"]
    sep = N("ShaderNodeSeparateXYZ"); L(pos, sep.inputs[0])
    nsep = N("ShaderNodeSeparateXYZ"); L(geo.outputs["Normal"], nsep.inputs[0])
    y, z = sep.outputs[1], sep.outputs[2]
    streak_vec = N("ShaderNodeVectorMath"); streak_vec.operation = 'MULTIPLY'
    L(pos, streak_vec.inputs[0]); streak_vec.inputs[1].default_value = (9.0, 9.0, 0.5)
    n1 = noise(pos, 2.2)
    n2 = noise(streak_vec.outputs[0], 3.0, 6.0)
    low = math_('MULTIPLY', ramp(z, 1.25, 0.45), math_('ADD', math_('MULTIPLY', n1, 0.55), math_('MULTIPLY', n2, 0.55)))
    low = math_('MULTIPLY', math_('POWER', low, 1.2), 1.1, clamp=True)
    spray = None
    for yc in (Y_FA, Y_RA):
        dy = math_('SUBTRACT', y, yc)
        dz = math_('SUBTRACT', z, ARCH_Z)
        d = math_('SQRT', math_('ADD', math_('MULTIPLY', dy, dy), math_('MULTIPLY', dz, dz)))
        ring = math_('MULTIPLY', ramp(d, 0.90, 0.46), ramp(dy, 0.30, -0.40))
        spray = ring if spray is None else math_('MAXIMUM', spray, ring)
    spray = math_('MULTIPLY', spray, math_('ADD', 0.2, math_('MULTIPLY', n2, 0.6)))
    # Streaks from the roof edge and off the window line, down the upper side.
    streaks = math_('MULTIPLY', ramp(n2, 0.56, 0.72), math_('MULTIPLY', ramp(z, 1.15, 2.1), 0.42))
    top = math_('MULTIPLY', ramp(nsep.outputs[2], 0.55, 0.92), math_('MULTIPLY', ramp(noise(pos, 1.4), 0.32, 0.72), 0.50))
    dirt = math_('MAXIMUM', math_('MAXIMUM', low, spray), math_('MAXIMUM', streaks, top))
    dirt = math_('MINIMUM', dirt, 0.82)
    em = N("ShaderNodeEmission")
    comb = N("ShaderNodeCombineXYZ")
    L(dirt, comb.inputs[0]); L(dirt, comb.inputs[1]); L(dirt, comb.inputs[2])
    L(comb.outputs[0], em.inputs["Color"])
    out = N("ShaderNodeOutputMaterial")
    L(em.outputs[0], out.inputs["Surface"])
    img_node = N("ShaderNodeTexImage")
    nt.nodes.active = img_node
    m["img"] = img_node.name
    return m


def bake_body(body, res=2048):
    """Bakes the transit_body texture set: paint mask and grime in the albedo, AO + roughness + metalness in ORM."""
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    prefs = bpy.context.preferences.addons['cycles'].preferences
    try:
        prefs.compute_device_type = 'OPTIX'
        prefs.get_devices()
        for d in prefs.devices:
            d.use = d.type != 'CPU'
        scene.cycles.device = 'GPU'
    except Exception:
        pass
    if scene.world is None:
        scene.world = bpy.data.worlds.new("bake")
    scene.world.light_settings.distance = 0.6
    tmp = bpy.data.objects.new("body_bake", body.data.copy())
    scene.collection.objects.link(tmp)
    body.hide_render = True
    m = grime_material()
    tmp.data.materials.clear()
    tmp.data.materials.append(m)
    for p in tmp.data.polygons:
        p.material_index = 0
    img = bpy.data.images.new("transit_bake", res, res, alpha=False, float_buffer=True)
    img.colorspace_settings.name = 'Non-Color'
    m.node_tree.nodes[m["img"]].image = img
    bpy.ops.object.select_all(action='DESELECT')
    tmp.select_set(True)
    bpy.context.view_layer.objects.active = tmp

    def grab():
        a = np.empty(res * res * 4, np.float32)
        img.pixels.foreach_get(a)
        return a.reshape(res, res, 4)[..., 0].copy()
    scene.cycles.samples = 4
    bpy.ops.object.bake(type='EMIT', margin=16)
    dirt = grab()
    scene.cycles.samples = 96
    bpy.ops.object.bake(type='AO', margin=16)
    ao = grab()
    bpy.data.objects.remove(tmp)
    body.hide_render = False
    # The colour channels are the grime; alpha is paint coverage, which the game replaces with the paint tint outright.
    col = np.array(K.srgb_to_linear((0.36, 0.32, 0.27)))
    pale = np.array(K.srgb_to_linear((0.52, 0.47, 0.40)))
    k = np.clip((dirt - 0.3) / 0.5, 0, 1)[..., None]
    lin = col[None, None, :] * (1 - k) + pale[None, None, :] * k
    srgb = np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(np.clip(lin, 0, 1), 1 / 2.4) - 0.055)
    ones = np.ones((res, res), np.float32)

    def save(name, arr):
        im = bpy.data.images.new(name, res, res, alpha=True)
        im.colorspace_settings.name = 'Non-Color'
        im.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
        im.filepath_raw = os.path.join(K.TEX, name + ".png")
        im.file_format = 'PNG'
        im.save()
        bpy.data.images.remove(im)
    save("transit_body_albedo", np.dstack([srgb, 1.0 - dirt]))
    save("transit_body_normal", np.dstack([ones * 0.5, ones * 0.5, ones, ones]))
    save("transit_body_orm", np.dstack([0.35 + 0.65 * ao, 0.28 + 0.58 * dirt, 0.15 * (1 - dirt), ones]))
    print("[transit] baked transit_body texture set", flush=True)


# ============================================================================================ preview
def render_views(prefix="transit"):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    prefs = bpy.context.preferences.addons['cycles'].preferences
    try:
        prefs.compute_device_type = 'OPTIX'
        prefs.get_devices()
        for d in prefs.devices:
            d.use = d.type != 'CPU'
        scene.cycles.device = 'GPU'
    except Exception:
        pass
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.view_settings.view_transform = 'AgX'
    world = bpy.data.worlds.new("prev")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.62, 0.72, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
    sun = bpy.data.lights.new("sun", 'SUN'); sun.energy = 3.5; sun.angle = math.radians(1.5)
    so = bpy.data.objects.new("sun", sun); scene.collection.objects.link(so)
    so.rotation_euler = (math.radians(50), math.radians(10), math.radians(-40))
    bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
    ground = bpy.context.active_object
    ground.data.materials.append(mat("prev_ground", (0.42, 0.41, 0.40), 0.9))
    cam_data = bpy.data.cameras.new("cam"); cam_data.lens = 50
    cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 1100, 720
    os.makedirs(K.PREVIEWS, exist_ok=True)
    # "photo" is from the reference photo's side: the van's front towards the viewer's right, seen from its left.
    views = (("photo", (-5.8, 8.2, 1.25), (0, 0.4, 1.35)), ("rear", (5.5, -8.6, 1.9), (0, -0.2, 1.35)), ("side", (11.2, 0.0, 1.35), (0, 0, 1.35)),
             ("nose", (-1.7, 7.0, 1.35), (0, 2.4, 1.15)), ("front", (0.0, 9.5, 1.4), (0, 0, 1.35)), ("top", (-4.5, 3.5, 6.8), (0, 0, 1.3)))
    for name, pos, tgt in views:
        cam.location = pos
        cam.rotation_euler = (Vector(tgt) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(K.PREVIEWS, f"{prefix}_{name}.png")
        bpy.ops.render.render(write_still=True)
    print("[transit] previews rendered", flush=True)


# ============================================================================================ main
def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    K.reset()
    materials()
    body = build_body()
    body = cut_wheel_wells(body)
    objs = [body] + flares() + wheels() + side_parts() + windscreen() + bonnet() + front_end() + rear_end() + roof_parts() + underbody()
    if "nobake" not in argv:
        bake_body(body)
    for o in objs:
        o.matrix_world = Matrix.Translation((0, Y_SHIFT, 0)) @ o.matrix_world
    objs = K.finish(objs)
    print(f"[transit] {len(objs)} objects, {sum(len(o.data.polygons) for o in objs)} faces", flush=True)
    if "preview" in argv:
        render_views()
        return
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.PROJECT, "Assets", "transit.blend"))
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    van = bpy.context.view_layer.objects.active
    van.name = van.data.name = "transit"
    K.export("transit", [van])
    render_views()
    print("[transit] done", flush=True)


if __name__ == "__main__":
    main()
