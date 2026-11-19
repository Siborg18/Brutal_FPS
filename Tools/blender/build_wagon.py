"""
Builds the outback 4WD "wagon": a modern full-size four-wheel-drive wagon (proportions of a current large
Japanese 4WD in sport trim: black honeycomb grille, black arch flares and wheels, slim LED lights) with no
badges, lettering or makers' marks. Exports Assets/Models/wagon.glb and bakes its own paint/dust texture set
("wagon_body"); the scene is saved as Assets/wagon.blend.

    blender -b --factory-startup -P Tools/blender/build_wagon.py [-- preview]

Coordinates: Blender +Y is the front (the game's -Z), +Z is up and the model is symmetric about X = 0; the origin
is on the ground under the middle of the body. Overall 4.98 m long, 1.98 m wide, 1.93 m high, 2.85 m wheelbase.

The body is one lofted surface: a cross-section (bottom, sill, door, shoulder, belt ledge, greenhouse, roof rail
radius, roof) is evaluated at stations along the length from smooth key curves (hood / windscreen / roof line,
belt line, widths), with rounded corners in plan and flat end caps. Window, pillar and shut-line boundaries are
stations or section parameters, so every material edge is a clean mesh line; the glass is recessed behind black
surrounds and the shut lines are grooves. Wheel wells are cut with booleans and lined.
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
Y_F, Y_R = 2.49, -2.49            # bumper faces
Y_FA = 1.535                      # front axle
Y_RA = Y_FA - 2.85                # rear axle
TRACK = 1.665
R_TYRE, W_TYRE = 0.401, 0.265     # 265/65 R18
ARCH_R, ARCH_Z = 0.462, 0.425
RF, RR = 0.13, 0.14               # plan-view corner radii, front and rear

Y_WS0, Y_WS1 = 1.06, 0.42         # windscreen base (cowl) and top
Y_GLASS_F, Y_GLASS_R = 1.02, -2.22
B_PILLAR = (-0.13, -0.025)
C_PILLAR = (-1.20, -1.09)
DOOR_CUTS = (1.045, -0.0775, -1.145)
GAP = 0.0028


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


# Key curves along the length (y).
ZT = pchip([(-2.49, 1.800), (-2.43, 1.830), (-2.30, 1.852), (-1.20, 1.866), (0.15, 1.866), (0.32, 1.856), (0.42, 1.838),
            (0.60, 1.700), (0.90, 1.420), (1.02, 1.318), (1.06, 1.288), (1.12, 1.266), (1.40, 1.252), (1.90, 1.236),
            (2.20, 1.215), (2.40, 1.200), (2.46, 1.180), (2.49, 1.160)])          # roof / windscreen / bonnet centre
ZBELT = pchip([(-2.49, 1.335), (-2.25, 1.335), (-1.30, 1.305), (-0.20, 1.272), (0.90, 1.252), (1.02, 1.245),
               (1.06, 1.228), (1.16, 1.205), (1.60, 1.192), (2.10, 1.178), (2.38, 1.165), (2.49, 1.135)])   # belt / wing top
HW = pchip([(-2.49, 0.935), (-2.34, 0.958), (-2.10, 0.965), (2.10, 0.965), (2.28, 0.958), (2.49, 0.935)])
ZS = pchip([(-2.49, 1.00), (-1.5, 0.99), (1.0, 0.975), (2.49, 0.94)])           # shoulder (widest) height
ZB = pchip([(-2.49, 0.540), (-2.38, 0.480), (-2.00, 0.462), (1.90, 0.455), (2.25, 0.430), (2.40, 0.440), (2.49, 0.480)])
HWT = pchip([(-2.49, 0.640), (-2.30, 0.690), (-1.50, 0.715), (0.30, 0.712), (0.42, 0.708), (1.06, 0.745), (1.14, 0.690),
             (1.40, 0.640), (2.20, 0.620), (2.49, 0.600)])                     # half width of the top surface
RC = pchip([(-2.49, 0.07), (0.30, 0.075), (0.42, 0.065), (1.00, 0.055), (1.10, 0.035), (2.49, 0.030)])
CROWN = pchip([(-2.49, 0.020), (0.30, 0.030), (0.45, 0.045), (1.05, 0.040), (1.15, 0.022), (2.49, 0.018)])
DBELT = pchip([(-2.49, 0.028), (1.0, 0.028), (1.15, 0.032), (2.49, 0.030)])


def params(y):
    return dict(zt=ZT(y), zbelt=ZBELT(y), hw=HW(y), zs=ZS(y), zb=ZB(y), hwt=HWT(y), rc=RC(y), crown=CROWN(y),
                dbelt=DBELT(y))


# Section parameter t: 0 = bottom centre ... 1 = top centre. Segment boundaries:
T_SEG = dict(floor=(0.0, 0.10), corner=(0.10, 0.14), lower=(0.14, 0.30), upper=(0.30, 0.44), ledge=(0.44, 0.47),
             green=(0.47, 0.70), rail=(0.70, 0.78), top=(0.78, 1.0))
T_GLASS = (0.49, 0.685)
T_WS = 0.80


def _bez3(p0, p1, p2, p3, s):
    a = 1 - s
    return a ** 3 * p0 + 3 * a * a * s * p1 + 3 * a * s * s * p2 + s ** 3 * p3


def _bez2(p0, p1, p2, s):
    a = 1 - s
    return a * a * p0 + 2 * a * s * p1 + s * s * p2


def section(p, ts):
    """Half cross-section (x >= 0) as an array of (x, z) for the parameter values ts."""
    V = lambda x, z: np.array((x, z), float)  # noqa: E731
    zb, hw, zs, zbelt, zt, hwt, crown = p["zb"], p["hw"], p["zs"], p["zbelt"], p["zt"], p["hwt"], p["crown"]
    rb = 0.05
    hw_low = hw - 0.035
    hw_belt = hw - p["dbelt"]
    g0 = V(hw_belt - 0.022, zbelt + 0.012)
    q = V(hwt + p["rc"], zt)
    rc = min(p["rc"], 0.6 * np.linalg.norm(q - g0))
    dg = (q - g0) / np.linalg.norm(q - g0)
    a = q - dg * rc
    b = V(q[0] - rc, zt)
    h1 = zs - zb - rb
    h2 = zbelt - zs
    out = []
    for t in ts:
        if t <= 0.10:
            s = t / 0.10
            out.append(V(s * (hw_low - rb), zb))
        elif t <= 0.14:
            s = (t - 0.10) / 0.04
            ph = -math.pi / 2 + s * math.pi / 2
            out.append(V(hw_low - rb + rb * math.cos(ph), zb + rb + rb * math.sin(ph)))
        elif t <= 0.30:
            s = (t - 0.14) / 0.16
            out.append(_bez3(V(hw_low, zb + rb), V(hw_low, zb + rb + 0.45 * h1), V(hw - 0.010, zs - 0.40 * h1), V(hw, zs), s))
        elif t <= 0.44:
            s = (t - 0.30) / 0.14
            out.append(_bez3(V(hw, zs), V(hw - 0.008, zs + 0.40 * h2), V(hw_belt + 0.25 * (hw - hw_belt), zbelt - 0.35 * h2), V(hw_belt, zbelt), s))
        elif t <= 0.47:
            s = (t - 0.44) / 0.03
            out.append(V(hw_belt, zbelt) * (1 - s) + g0 * s)
        elif t <= 0.70:
            s = (t - 0.47) / 0.23
            out.append(g0 * (1 - s) + a * s)
        elif t <= 0.78:
            s = (t - 0.70) / 0.08
            out.append(_bez2(a, q, b, s))
        else:
            s = (t - 0.78) / 0.22
            out.append(_bez2(b, V(b[0] * 0.5, zt + crown), V(0.0, zt + crown), s))
    return np.array(out)


def t_samples():
    ts = list(np.linspace(0.0, 0.10, 6))
    ts += list(np.linspace(0.10, 0.14, 5)[1:])
    ts += list(np.linspace(0.14, 0.30, 9)[1:])
    ts += list(np.linspace(0.30, 0.44, 9)[1:])
    ts += [0.455, 0.47]
    ts += [0.49] + list(np.linspace(0.49, 0.685, 9)[1:]) + [0.70]
    ts += list(np.linspace(0.70, 0.78, 7)[1:])
    ts += [0.80] + list(np.linspace(0.80, 1.0, 11)[1:])
    return np.array(ts)


def side_x(y, z):
    """x of the body's side surface at (y, z) (on the lower body, between the sill and the belt)."""
    ts = np.linspace(0.10, 0.44, 120)
    pts = section(params(y), ts)
    return float(np.interp(z, pts[:, 1], pts[:, 0]))


# ============================================================================================ body
def stations():
    """(y, x scale, kind, cap fraction) from the rear cap's centre to the front cap's centre."""
    y0, y1 = Y_R + RR, Y_F - RF
    ys = set(float(v) for v in np.round(np.arange(y0, y1, 0.045), 4))
    for b in (Y_WS0, Y_WS1, Y_WS0 + 0.05, Y_WS1 + 0.035, Y_WS0 - 0.03, Y_GLASS_F, Y_GLASS_R, *B_PILLAR, *C_PILLAR):
        ys.add(round(b, 4))
    for c in DOOR_CUTS:
        ys.add(round(c - GAP / 2, 5))
        ys.add(round(c + GAP / 2, 5))
    ys = sorted(v for v in ys if y0 <= v <= y1)
    # Drop stations closer than 8 mm to a boundary (but keep the boundaries).
    keep = set(round(b, 4) for b in (Y_WS0, Y_WS1, Y_WS0 + 0.05, Y_WS1 + 0.035, Y_WS0 - 0.03, Y_GLASS_F, Y_GLASS_R,
                                       *B_PILLAR, *C_PILLAR)) | {round(c + d, 5) for c in DOOR_CUTS for d in (-GAP / 2, GAP / 2)}
    pruned = []
    for v in ys:
        if v in keep or all(abs(v - k) > 0.008 for k in keep):
            pruned.append(v)
    ys = pruned
    out = []
    # Rear cap (centre outwards) then the rear corner.
    hw_r = HW(Y_R)
    end_r = (hw_r - RR) / hw_r
    for f in (0.0, 0.35, 0.7, 0.86, 0.92):
        out.append((Y_R, end_r * f, "capR", f))
    for k in range(8, 0, -1):
        th = math.pi / 2 * k / 8
        y = Y_R + RR - RR * math.sin(th)
        out.append((y, (HW(y) - RR * (1 - math.cos(th))) / HW(y), "side", 1.0))
    for y in ys:
        out.append((y, 1.0, "side", 1.0))
    for k in range(1, 9):
        th = math.pi / 2 * k / 8
        y = Y_F - RF + RF * math.sin(th)
        out.append((y, (HW(y) - RF * (1 - math.cos(th))) / HW(y), "side", 1.0))
    hw_f = HW(Y_F)
    end_f = (hw_f - RF) / hw_f
    for f in (0.92, 0.86, 0.77, 0.6, 0.3, 0.0):
        out.append((Y_F, end_f * f, "capF", f))
    return out


def body_region(y, t, z, kind, f):
    """Which material a face of the body gets: paint, glass, trim (gloss black), plastic (textured black),
    backing (dark, behind the grille), gap (shut lines)."""
    if kind == "capF":
        if z < 0.66:
            return "plastic"
        return "backing" if f < 0.77 else "paint"
    if kind == "capR":
        if T_GLASS[0] <= t <= T_GLASS[1] and f <= 0.86:
            return "glass"
        if T_SEG["green"][0] <= t <= T_SEG["green"][1] and f <= 0.92:
            return "trim"
        if z < 0.62:
            return "plastic"
        return "paint"
    # Bumpers.
    if y > 2.06 and z < 0.66:
        return "plastic"
    if y < -2.10 and z < 0.62:
        return "plastic"
    # Windscreen and cowl.
    if t >= T_SEG["rail"][1] and Y_WS1 <= y <= Y_WS0:
        if t >= T_WS and Y_WS1 + 0.035 <= y <= Y_WS0 - 0.03:
            return "glass"
        return "trim"
    if t >= T_SEG["rail"][0] and Y_WS0 <= y <= Y_WS0 + 0.05:
        return "trim"
    # Side glass, its surround and the blacked-out pillars.
    if Y_GLASS_R <= y <= Y_GLASS_F + 0.04 and T_SEG["green"][0] <= t <= T_SEG["green"][1]:
        if y > Y_GLASS_F:
            return "trim"
        if B_PILLAR[0] <= y <= B_PILLAR[1] or C_PILLAR[0] <= y <= C_PILLAR[1]:
            return "trim"
        if T_GLASS[0] <= t <= T_GLASS[1]:
            return "glass"
        return "trim"
    # Bonnet shut line along the wings.
    if y > Y_WS0 + 0.05 and 0.455 <= t <= 0.47:
        return "gap"
    # Door shut lines.
    for c in DOOR_CUTS:
        if abs(y - c) < GAP and 0.13 <= t <= 0.47:
            if c == DOOR_CUTS[2] and z < 0.86:
                continue
            return "gap"
    return "paint"


MATS = {}


def materials():
    MATS.update(
        paint=mat("tex_wagon_body_tint", (0.24, 0.25, 0.27), 0.25, 0.5),
        glass=mat("flat_glass", (0.035, 0.04, 0.045), 0.03, 0.0),
        trim=mat("flat_trim_gloss", (0.025, 0.025, 0.028), 0.18, 0.0),
        plastic=mat("flat_plastic_black", (0.05, 0.05, 0.053), 0.62, 0.0),
        backing=mat("flat_backing", (0.03, 0.03, 0.032), 0.8, 0.0),
        gap=mat("flat_gap", (0.01, 0.01, 0.01), 0.9, 0.0),
        liner=mat("flat_liner", (0.03, 0.028, 0.026), 0.95, 0.0),
    )


def build_body():
    ts = t_samples()
    nt = len(ts)
    ring_t = list(ts) + list(ts[-2:0:-1])            # right half up, left half back down
    ring_side = [1] * nt + [-1] * (nt - 2)
    nr = len(ring_t)
    st = stations()
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    verts = []
    ucoord = []
    u = 0.0
    prev = None
    for (y, xs, kind, f) in st:
        sec = section(params(y), ts)
        ring = []
        for k in range(nr):
            x, z = sec[k if k < nt else (2 * nt - 2 - k)]
            ring.append(bm.verts.new((ring_side[k] * x * xs, y, z)))
        verts.append(ring)
        if prev is not None:
            u += abs(y - prev[0]) + abs(xs - prev[1]) * 0.9
        ucoord.append(u)
        prev = (y, xs)
    total_u = ucoord[-1]
    order = ["paint", "glass", "trim", "plastic", "backing", "gap"]
    depth_of = {"glass": 0.007, "gap": 0.0045}
    sink = {}
    for j in range(len(st) - 1):
        y = (st[j][0] + st[j + 1][0]) / 2
        kind = st[j][2] if st[j][2] == st[j + 1][2] else "side"
        fm = (st[j][3] + st[j + 1][3]) / 2
        for k in range(nr):
            k2 = (k + 1) % nr
            corners = ((j, k), (j, k2), (j + 1, k2), (j + 1, k))
            quad = [verts[a][b] for a, b in corners]
            p = [q.co for q in quad]
            area = ((p[2] - p[0]).cross(p[3] - p[1])).length / 2
            if area < 1e-9:
                continue
            face = bm.faces.new(quad)
            t = (ring_t[k] + ring_t[k2]) / 2
            z = sum(q.z for q in p) / 4
            reg = body_region(y, t, z, kind, fm)
            face.material_index = order.index(reg)
            for loop, (jj, kk) in zip(face.loops, corners):
                v = ring_t[kk] / 2 if kk < nt else 1 - ring_t[kk] / 2
                if k2 == 0 and kk == 0:
                    v = 1.0
                loop[uvl].uv = (ucoord[jj] / total_u, v)
                if reg in depth_of:
                    sink[(jj, kk)] = max(sink.get((jj, kk), 0.0), depth_of[reg])
    # Recess the glass behind its surround and sink the shut lines (along the outward surface normal).
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    for (jj, kk), d in sink.items():
        v = verts[jj][kk]
        v.co -= v.normal * d
    # Weld the caps' centre lines.
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("body")
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new("body", me)
    bpy.context.scene.collection.objects.link(o)
    print(f"[wagon] body: {len(st)} stations x {nr} = {len(me.polygons)} faces", flush=True)
    for name in order:
        o.data.materials.append(MATS[name])
    o["uv"] = "keep"
    o["smooth"] = True
    o["smooth_angle"] = 38.0
    return o


def cut_wheel_wells(body):
    """Booleans out the wheel wells (pockets, lined) and returns the body."""
    cutters = []
    for y in (Y_FA, Y_RA):
        for sx in (-1, 1):
            c = cyl("cut_well", ARCH_R, 0.62, (sx * 0.98, y, ARCH_Z), MATS["liner"], verts=48, rot=(0, 90, 0))
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
    """A 265/65 R18 all-terrain tyre around the X axis at the origin: tread with the tread texture (U round the
    tyre, V across the tread), plain rubber sidewalls."""
    prof = [(0.236, -0.108), (0.300, -0.126), (0.350, -0.131), (0.381, -0.126), (0.396, -0.112), (0.401, -0.094),
            (0.403, -0.05), (0.403, 0.0), (0.403, 0.05), (0.401, 0.094), (0.396, 0.112), (0.381, 0.126), (0.350, 0.131),
            (0.300, 0.126), (0.236, 0.108)]
    tread0, tread1 = 4, 10        # profile indices bounding the tread face (shoulder to shoulder)
    seg = 72
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


def spoke(name, r0, r1, w0, w1, a0, a1, th, ang, material):
    """A tapered spoke from radius r0 to r1 (widths w0, w1), its face at axial a0 (hub) to a1 (rim), thickness th,
    at angle ang (radians) round the X axis."""
    bm = bmesh.new()
    pts = []
    for (r, w, a) in ((r0, w0, a0), (r1, w1, a1)):
        for s in (-1, 1):
            pts.append((r, s * w / 2, a))
    vs = []
    for d in (0.0, -th):
        for (r, w, a) in pts:
            vs.append(bm.verts.new((a + d, w, r)))
    # vs: 0..3 front (r0-, r0+, r1-, r1+), 4..7 back
    for idx in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 2, 6, 4), (1, 5, 7, 3), (0, 4, 5, 1), (2, 3, 7, 6)):
        bm.faces.new([vs[i] for i in idx])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(material)
    o.rotation_euler = (ang, 0, 0)
    add_bevel(o, 0.006, 2)
    return o


def wheel(name):
    """An 18 inch six-spoke alloy in matt black with the tyre, a brake disc and calliper; axle along X, outer face
    towards +X, centred at the origin."""
    alloy = mat("flat_alloy_black", (0.05, 0.05, 0.055), 0.42, 0.55)
    objs = [tyre(name + "_tyre")]
    # Rim: outer lip and barrel.
    lip = K.lathe(name + "_lip", [(0.200, 0.080), (0.226, 0.082), (0.236, 0.096), (0.236, 0.104), (0.226, 0.108),
                                  (0.214, 0.100), (0.212, 0.060), (0.212, -0.110)], material=alloy, verts=72, rot=(0, 90, 0))
    objs.append(lip)
    barrel = K.lathe(name + "_barrel", [(0.205, -0.110), (0.205, 0.075)], material=mat("flat_barrel", (0.04, 0.04, 0.042), 0.6, 0.4),
                     verts=48, rot=(0, 90, 0), flip=True)
    objs.append(barrel)
    # Hub and six spokes, dished: the hub stands proud of the rim face.
    objs.append(cyl(name + "_hub", 0.088, 0.045, (0.075, 0, 0), alloy, verts=36, rot=(0, 90, 0)))
    objs.append(cyl(name + "_cap", 0.032, 0.012, (0.102, 0, 0), mat("flat_cap_black", (0.03, 0.03, 0.03), 0.3, 0.3), verts=24, rot=(0, 90, 0)))
    for i in range(6):
        a = math.tau * i / 6
        objs.append(spoke(name + "_spoke", 0.06, 0.212, 0.050, 0.068, 0.096, 0.083, 0.03, a, alloy))
        # Lug nuts (6 on a 139.7 mm circle).
        b = a + math.tau / 12
        objs.append(cyl(name + "_nut", 0.0095, 0.016, (0.100, 0.0698 * math.cos(b), 0.0698 * math.sin(b)),
                        mat("flat_nut", (0.55, 0.55, 0.55), 0.35, 1.0), verts=6, rot=(0, 90, 0)))
    # Brake disc and calliper behind the spokes.
    objs.append(cyl(name + "_disc", 0.172, 0.028, (0.0, 0, 0), mat("steel"), verts=48, rot=(0, 90, 0)))
    cal = box(name + "_caliper", (0.07, 0.10, 0.14), (0.02, -0.12, 0.10), mat("flat_caliper", (0.08, 0.08, 0.085), 0.5, 0.3), rot=(40, 0, 0), bevel=0.012)
    objs.append(cal)
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


# ============================================================================================ details
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


def sweep(name, rows, material, caps=True, uv="box", smooth_angle=40.0):
    """A closed solid swept through rows of points (each row a closed loop, same count), capped at both ends."""
    bm = bmesh.new()
    vr = [[bm.verts.new(p) for p in row] for row in rows]
    n = len(rows[0])
    for a, b in zip(vr, vr[1:]):
        for i in range(n):
            i2 = (i + 1) % n
            bm.faces.new((a[i], a[i2], b[i2], b[i]))
    if caps:
        bm.faces.new(vr[0])
        bm.faces.new(list(reversed(vr[-1])))
    return mesh_obj(name, bm, material, uv, smooth_angle)


def split_off(o, names, new_name, uv="box"):
    """Moves the faces that use the named materials into a new object."""
    idx = {i for i, m in enumerate(o.data.materials) if m and m.name.split(".")[0] in names}
    o2 = bpy.data.objects.new(new_name, o.data.copy())
    bpy.context.scene.collection.objects.link(o2)
    for target, keep in ((o2.data, True), (o.data, False)):
        bm = bmesh.new()
        bm.from_mesh(target)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if (f.material_index in idx) != keep], context='FACES')
        bm.to_mesh(target)
        bm.free()
    o2["uv"] = uv
    o2["smooth"] = True
    o2["smooth_angle"] = o.get("smooth_angle", 38.0)
    return o2


def flares():
    """Black wheel-arch flares following the body side round each opening."""
    loop = [(-0.006, -0.028), (-0.005, 0.0), (0.003, 0.029), (0.028, 0.034), (0.058, 0.022), (0.076, 0.004),
            (0.080, -0.010), (0.040, -0.034)]
    out = []
    for yc in (Y_FA, Y_RA):
        for sx in (-1, 1):
            rows = []
            for th in np.linspace(math.radians(4), math.radians(176), 46):
                row = []
                for dr, dx in loop:
                    r = ARCH_R + dr
                    y = yc + r * math.cos(th)
                    z = ARCH_Z + r * math.sin(th)
                    x = side_x(y, max(z, ZB(y) + 0.06)) + dx
                    row.append((sx * x, y, z))
                rows.append(row)
            out.append(sweep("flare", rows, MATS["plastic"], smooth_angle=50.0))
    return out


def plan_curve(end, z, x_start, theta_end_deg, n=10):
    """The body's outline in plan at height z: along the end face from x_start, then round the corner to the
    angle theta_end (90 = the end face, 0 = the side). Returns [(x, y, outward normal (x, y))] for the +x side."""
    r = RF if end > 0 else RR
    y_end = Y_F if end > 0 else Y_R
    pts = []
    th90 = math.pi / 2

    def corner(th):
        y = y_end - end * r + end * r * math.sin(th)
        xs = (HW(y) - r * (1 - math.cos(th))) / HW(y)
        return side_x(y, z) * xs, y
    x_face = corner(th90)[0]
    for x in np.linspace(x_start, x_face, 4)[:-1]:
        pts.append((x, y_end))
    for th in np.linspace(th90, math.radians(theta_end_deg), n):
        pts.append(corner(th))
    out = []
    ref = (0.0, y_end - end * 1.5)          # a point well inside the body
    for i, (x, y) in enumerate(pts):
        a = pts[max(i - 1, 0)]
        b = pts[min(i + 1, len(pts) - 1)]
        tx, ty = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(tx, ty)
        nx, ny = ty / ln, -tx / ln
        if nx * (x - ref[0]) + ny * (y - ref[1]) < 0:
            nx, ny = -nx, -ny
        out.append((x, y, (nx, ny)))
    return out


def corner_band(name, end, z0, z1, x_start, theta_end, proud, material, taper=0.6, depth=0.03, n=10):
    """A light unit (or trim) following the body's end face and corner as a closed band from z0 to z1, standing
    `proud` out of the surface; its height tapers towards the corner end."""
    out = []
    for sx in (-1, 1):
        curve = plan_curve(end, (z0 + z1) / 2, x_start, theta_end, n)
        rows = []
        m = len(curve)
        for i, (x, y, (nx, ny)) in enumerate(curve):
            k = i / (m - 1)
            zc = (z0 + z1) / 2
            hh = (z1 - z0) / 2 * (1 - (1 - taper) * max(0.0, (k - 0.3) / 0.7))
            row = []
            for d, zz in ((proud, zc - hh), (proud, zc + hh), (-depth, zc + hh), (-depth, zc - hh)):
                row.append((sx * (x + nx * d), y + ny * d, zz))
            rows.append(row)
        o = sweep(name, rows, material, smooth_angle=60.0)
        add_bevel(o, min(0.006, (z1 - z0) * 0.2), 2)
        out.append(o)
    return out


def front_end():
    objs = []
    trim = MATS["trim"]
    # Grille: a gloss-black surround (trapezoid, wider at the bottom), the honeycomb mesh and a plain bar across.
    top, bot = 1.105, 0.665
    xt, xb = 0.50, 0.615
    fw = 0.042
    outer = [(-xb, bot), (xb, bot), (xt, top), (-xt, top)]
    inner = [(-xb + fw, bot + fw), (xb - fw, bot + fw), (xt - fw, top - fw), (-xt + fw, top - fw)]
    bm = bmesh.new()
    vo = [bm.verts.new((x, Y_F + 0.034, z)) for x, z in outer]
    vi = [bm.verts.new((x, Y_F + 0.034, z)) for x, z in inner]
    vob = [bm.verts.new((x, Y_F - 0.01, z)) for x, z in outer]
    vib = [bm.verts.new((x, Y_F - 0.01, z)) for x, z in inner]
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((vo[i], vo[j], vi[j], vi[i]))
        bm.faces.new((vob[i], vo[i], vo[j], vob[j])[::1])
        bm.faces.new((vi[i], vi[j], vib[j], vib[i]))
        bm.faces.new((vob[j], vib[j], vib[i], vob[i]))
    g = mesh_obj("grille_frame", bm, trim, smooth_angle=30.0)
    add_bevel(g, 0.008, 2)
    objs.append(g)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    vs = [bm.verts.new((x, Y_F + 0.014, z)) for x, z in inner]
    f = bm.faces.new(vs)
    for loop in f.loops:
        loop[uvl].uv = (loop.vert.co.x / 0.30, loop.vert.co.z / 0.30)
    gm = mesh_obj("grille_mesh", bm, mat("cut_grille_honeycomb", None, 0.35, 0.0), uv="keep")
    if gm.data.polygons[0].normal.y < 0:
        gm.data.flip_normals()
    objs.append(gm)
    objs.append(box("grille_bar", (2 * xt + 0.05, 0.05, 0.085), (0, Y_F + 0.03, 0.93), trim, bevel=0.012))
    # Headlights: slim units from the grille round the corners, a bright LED strip along their lower edge and
    # three projector lenses.
    lamp = mat("flat_headlamp", (0.11, 0.12, 0.13), 0.04, 0.4)
    objs += corner_band("headlamp", 1, 0.985, 1.085, xt + 0.03, 28, 0.010, lamp, taper=0.55)
    objs += corner_band("drl", 1, 0.990, 1.003, xt + 0.04, 30, 0.016, mat("emit_drl", (1.0, 0.98, 0.94)), taper=1.0, depth=0.01)
    for sx in (-1, 1):
        for i, x in enumerate((0.60, 0.67, 0.74)):
            objs.append(cyl("projector", 0.024, 0.02, (sx * x, Y_F + 0.012, 1.045), mat("flat_projector", (0.75, 0.76, 0.78), 0.08, 1.0), verts=20, rot=(90, 0, 0)))
            objs.append(cyl("projector_lens", 0.016, 0.02, (sx * x, Y_F + 0.016, 1.045), mat("flat_projector_lens", (0.04, 0.05, 0.06), 0.02, 0.0), verts=16, rot=(90, 0, 0)))
        # Fog lamps low in the bumper and an indicator strip.
        objs.append(box("foglamp", (0.14, 0.03, 0.05), (sx * 0.70, Y_F + 0.008, 0.545), mat("flat_foglamp", (0.80, 0.80, 0.76), 0.08, 0.2), bevel=0.012))
        objs.append(box("fog_bezel", (0.20, 0.02, 0.085), (sx * 0.70, Y_F + 0.002, 0.545), trim, bevel=0.015))
    # Lower intake (honeycomb) and a satin skid plate.
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    vs = [bm.verts.new((x, Y_F + 0.008, z)) for x, z in ((-0.46, 0.47), (0.46, 0.47), (0.50, 0.60), (-0.50, 0.60))]
    f = bm.faces.new(vs)
    for loop in f.loops:
        loop[uvl].uv = (loop.vert.co.x / 0.30, loop.vert.co.z / 0.30)
    li = mesh_obj("intake_mesh", bm, mat("cut_grille_honeycomb", None, 0.35, 0.0), uv="keep")
    if li.data.polygons[0].normal.y < 0:
        li.data.flip_normals()
    objs.append(li)
    objs.append(box("intake_back", (0.98, 0.02, 0.15), (0, Y_F - 0.02, 0.535), MATS["backing"]))
    objs.append(box("skid_plate", (1.00, 0.34, 0.014), (0, Y_F - 0.17, 0.405), mat("flat_skid", (0.42, 0.43, 0.44), 0.45, 0.6), rot=(22, 0, 0), bevel=0.006))
    return objs


def rear_end():
    objs = []
    trim = MATS["trim"]
    tail = mat("flat_tail_lens", (0.40, 0.025, 0.03), 0.06, 0.1)
    objs += corner_band("taillamp", -1, 1.125, 1.235, 0.50, 22, 0.010, tail, taper=0.7)
    objs += corner_band("tail_led", -1, 1.200, 1.212, 0.52, 26, 0.016, mat("emit_tail", (0.85, 0.06, 0.05)), taper=1.0, depth=0.01)
    # Garnish between the lamps, blank number plate, bumper step pad, tow hitch, exhaust, reflectors.
    objs.append(box("garnish", (1.00, 0.02, 0.07), (0, Y_R - 0.008, 1.17), trim, bevel=0.01))
    objs.append(box("plate_recess", (0.42, 0.012, 0.17), (0, Y_R - 0.003, 0.80), trim, bevel=0.01))
    objs.append(box("plate", (0.372, 0.01, 0.134), (0, Y_R - 0.012, 0.80), mat("flat_plate", (0.84, 0.84, 0.80), 0.4, 0.0), bevel=0.004))
    objs.append(box("step_pad", (1.25, 0.11, 0.02), (0, Y_R - 0.02, 0.612), mat("flat_rubber", (0.04, 0.04, 0.04), 0.9, 0.0), bevel=0.006))
    objs.append(box("hitch", (0.07, 0.30, 0.07), (0, Y_R + 0.02, 0.43), mat("rusty_metal"), bevel=0.005))
    objs.append(cyl("exhaust", 0.036, 0.30, (-0.58, Y_R + 0.10, 0.36), mat("flat_exhaust", (0.12, 0.11, 0.10), 0.6, 0.7), verts=16, rot=(90, 0, 0)))
    for sx in (-1, 1):
        objs.append(box("reflector", (0.16, 0.02, 0.03), (sx * 0.74, Y_R - 0.006, 0.55), mat("flat_reflector", (0.5, 0.04, 0.04), 0.2, 0.0), bevel=0.006))
    # Roof spoiler over the tailgate glass with the high-level brake light, and the rear wiper.
    paint = mat("flat_paint_tint", (0.24, 0.25, 0.27), 0.25, 0.5)
    objs.append(box("spoiler", (1.34, 0.12, 0.032), (0, Y_R - 0.005, 1.808), paint, rot=(6, 0, 0), bevel=0.012))
    objs.append(box("brake_light", (0.40, 0.012, 0.012), (0, Y_R - 0.045, 1.787), mat("emit_brake", (0.7, 0.05, 0.04)), bevel=0.003))
    objs.append(box("rear_wiper", (0.42, 0.015, 0.012), (0.10, Y_R - 0.012, 1.42), trim, rot=(0, 25, 0)))
    return objs


def side_parts():
    objs = []
    trim = MATS["trim"]
    paint = mat("flat_paint_tint", (0.24, 0.25, 0.27), 0.25, 0.5)
    handle = mat("flat_handle", (0.08, 0.08, 0.085), 0.22, 0.7)
    for sx in (-1, 1):
        # Mirrors (black caps), on arms from the door's front corner.
        objs.append(box("mirror", (0.20, 0.10, 0.145), (sx * 1.075, 0.955, 1.335), trim, rot=(0, 0, sx * -6), bevel=0.035))
        objs.append(box("mirror_glass", (0.165, 0.006, 0.112), (sx * 1.078, 0.904, 1.335), mat("flat_mirror", (0.62, 0.64, 0.66), 0.02, 1.0), rot=(0, 0, sx * -6), bevel=0.01))
        objs.append(box("mirror_arm", (0.11, 0.07, 0.05), (sx * 0.965, 0.975, 1.29), trim, bevel=0.012))
        objs.append(box("indicator", (0.10, 0.012, 0.012), (sx * 1.10, 1.004, 1.28), mat("flat_amber", (0.85, 0.45, 0.08), 0.1, 0.0), rot=(0, 0, sx * -6), bevel=0.004))
        # Door handles.
        for y in (0.36, -0.70):
            x = side_x(y, 1.135)
            objs.append(box("handle", (0.024, 0.20, 0.036), (sx * (x + 0.010), y, 1.135), handle, bevel=0.010))
        # Side steps with three supports and rubber treads.
        y0, y1 = Y_FA - ARCH_R - 0.07, Y_RA + ARCH_R + 0.07
        objs.append(box("step", (0.20, y0 - y1, 0.035), (sx * 0.895, (y0 + y1) / 2, 0.40), MATS["plastic"], bevel=0.012))
        for k in range(3):
            objs.append(box("step_tread", (0.16, y0 - y1 - 0.06, 0.008), (sx * (0.84 + 0.055 * k * 0 + 0.0), (y0 + y1) / 2, 0.42), mat("flat_rubber", (0.04, 0.04, 0.04), 0.9, 0.0), bevel=0.003) if k == 0 else
                        box("step_rib", (0.008, y0 - y1 - 0.08, 0.006), (sx * (0.82 + 0.05 * k), (y0 + y1) / 2, 0.426), mat("flat_rubber", (0.04, 0.04, 0.04), 0.9, 0.0)))
            objs.append(box("step_bracket", (0.12, 0.05, 0.06), (sx * 0.80, y1 + (y0 - y1) * (0.15 + 0.35 * k), 0.43), mat("flat_chassis", (0.05, 0.05, 0.05), 0.8, 0.3)))
        # Roof rails on three feet each.
        ys = np.linspace(0.30, -2.26, 14)
        pts = [(sx * 0.645, float(y), ZT(float(y)) + 0.062) for y in ys]
        pts = [(sx * 0.645, 0.36, ZT(0.36) + 0.012)] + pts + [(sx * 0.645, -2.32, ZT(-2.32) + 0.012)]
        objs.append(K.tube("roof_rail", pts, 0.015, mat("flat_rail", (0.035, 0.035, 0.04), 0.35, 0.5), verts=10))
        for y in (0.22, -1.0, -2.18):
            objs.append(box("rail_foot", (0.045, 0.11, 0.07), (sx * 0.645, y, ZT(y) + 0.03), trim, bevel=0.012))
    # Shark-fin aerial and windscreen wipers.
    fin = K.prism("aerial", [(-2.18, ZT(-2.1) - 0.01), (-2.02, ZT(-2.1) - 0.01), (-2.13, ZT(-2.1) + 0.06)], 0.05, material=trim, axis='X')
    add_bevel(fin, 0.008, 2)
    objs.append(fin)
    for x0 in (-0.62, 0.04):
        y = Y_WS0 - 0.045
        objs.append(box("wiper", (0.56, 0.014, 0.012), (x0 + 0.28, y, ZT(y) + 0.012), trim, rot=(-38, 0, 4)))
    return objs


def underbody():
    dark = mat("flat_chassis", (0.05, 0.05, 0.05), 0.8, 0.3)
    objs = []
    for sx in (-1, 1):
        objs.append(box("rail", (0.08, 4.3, 0.16), (sx * 0.47, -0.05, 0.40), dark))
    objs.append(box("gearbox", (0.34, 1.0, 0.24), (0, 0.55, 0.40), dark, bevel=0.03))
    objs.append(box("fuel_tank", (0.95, 0.70, 0.20), (0.05, -0.65, 0.37), dark, bevel=0.03))
    objs.append(cyl("front_diff", 0.11, 0.24, (0.08, Y_FA, 0.36), dark, verts=20, rot=(90, 0, 0)))
    objs.append(cyl("rear_axle", 0.045, TRACK - 0.2, (0, Y_RA, R_TYRE), dark, verts=12, rot=(0, 90, 0)))
    objs.append(cyl("rear_diff", 0.14, 0.22, (0.05, Y_RA, R_TYRE), dark, verts=20, rot=(90, 0, 0)))
    objs.append(cyl("prop_shaft", 0.035, 2.0, (0.03, Y_RA + 1.05, 0.38), dark, verts=10, rot=(90, 0, 0)))
    objs.append(K.tube("exhaust_pipe", [(-0.30, 1.10, 0.34), (-0.40, 0.40, 0.33), (-0.52, -1.0, 0.34), (-0.58, -2.30, 0.36)], 0.03,
                       mat("flat_exhaust", (0.12, 0.11, 0.10), 0.6, 0.7), verts=8))
    for y in (Y_FA, Y_RA):
        for sx in (-1, 1):
            objs.append(cyl("shock", 0.035, 0.40, (sx * 0.62, y - 0.18 * (1 if y > 0 else -1), 0.62), mat("flat_shock", (0.5, 0.45, 0.1), 0.5, 0.3), verts=10, rot=(10, 0, 0)))
    return objs


# ============================================================================================ paint and dust
def dust_material():
    """The bake shader: paint (mask 1) and red outback dust - heavy low down and sprayed behind the wheels,
    lighter on top surfaces, with streaks."""
    m = bpy.data.materials.new("dust_bake")
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

    def ramp(v, a, b):        # smooth 0..1 as v goes a -> b
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
    L(pos, streak_vec.inputs[0]); streak_vec.inputs[1].default_value = (5.0, 5.0, 0.7)
    n1 = noise(pos, 2.5)
    n2 = noise(streak_vec.outputs[0], 3.0, 6.0)
    low = math_('MULTIPLY', ramp(z, 0.85, 0.42), math_('ADD', math_('MULTIPLY', n1, 0.5), math_('MULTIPLY', n2, 0.5)))
    low = math_('MULTIPLY', math_('POWER', low, 1.3), 1.15, clamp=True)
    spray = None
    for yc in (Y_FA, Y_RA):
        dy = math_('SUBTRACT', y, yc)
        dz = math_('SUBTRACT', z, ARCH_Z)
        d = math_('SQRT', math_('ADD', math_('MULTIPLY', dy, dy), math_('MULTIPLY', dz, dz)))
        ring = math_('MULTIPLY', ramp(d, 0.85, 0.47), ramp(dy, 0.25, -0.35))         # behind each wheel
        spray = ring if spray is None else math_('MAXIMUM', spray, ring)
    spray = math_('MULTIPLY', spray, math_('ADD', 0.15, math_('MULTIPLY', n2, 0.6)))
    top = math_('MULTIPLY', ramp(nsep.outputs[2], 0.6, 0.95), math_('MULTIPLY', ramp(noise(pos, 1.6), 0.4, 0.75), 0.16))
    dust = math_('MAXIMUM', math_('MAXIMUM', low, spray), top)
    dust = math_('MINIMUM', dust, 0.85)
    em = N("ShaderNodeEmission")
    comb = N("ShaderNodeCombineXYZ")
    L(dust, comb.inputs[0]); L(dust, comb.inputs[1]); L(dust, comb.inputs[2])
    L(comb.outputs[0], em.inputs["Color"])
    out = N("ShaderNodeOutputMaterial")
    L(em.outputs[0], out.inputs["Surface"])
    img_node = N("ShaderNodeTexImage")
    nt.nodes.active = img_node
    m["img"] = img_node.name
    return m


def bake_body(body, res=2048):
    """Bakes the wagon_body texture set: paint mask and dust in the albedo, AO + roughness + metalness in ORM."""
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
    scene.world.light_settings.distance = 0.5
    tmp = bpy.data.objects.new("body_bake", body.data.copy())
    scene.collection.objects.link(tmp)
    body.hide_render = True
    m = dust_material()
    tmp.data.materials.clear()
    tmp.data.materials.append(m)
    for p in tmp.data.polygons:
        p.material_index = 0
    img = bpy.data.images.new("wagon_bake", res, res, alpha=False, float_buffer=True)
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
    dust = grab()
    scene.cycles.samples = 96
    bpy.ops.object.bake(type='AO', margin=16)
    ao = grab()
    bpy.data.objects.remove(tmp)
    body.hide_render = False
    # The colour channels are the dust (paler where it lies thick); alpha is paint coverage, which the game
    # replaces with the paint tint outright.
    dust_col = np.array(K.srgb_to_linear((0.66, 0.50, 0.40)))
    pale = np.array(K.srgb_to_linear((0.76, 0.61, 0.50)))
    k = np.clip((dust - 0.3) / 0.6, 0, 1)[..., None]
    lin = dust_col[None, None, :] * (1 - k) + pale[None, None, :] * k
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
    save("wagon_body_albedo", np.dstack([srgb, 1.0 - dust]))
    save("wagon_body_normal", np.dstack([ones * 0.5, ones * 0.5, ones, ones]))
    save("wagon_body_orm", np.dstack([0.35 + 0.65 * ao, 0.24 + 0.62 * dust, 0.5 * (1 - dust), ones]))
    print("[wagon] baked wagon_body texture set", flush=True)


# ============================================================================================ preview
def render_views(objs, prefix="wagon"):
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
    sun = bpy.data.lights.new("sun", 'SUN'); sun.energy = 4.0; sun.angle = math.radians(1.5)
    so = bpy.data.objects.new("sun", sun); scene.collection.objects.link(so)
    so.rotation_euler = (math.radians(45), math.radians(15), math.radians(-35))
    bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
    ground = bpy.context.active_object
    ground.data.materials.append(mat("prev_ground", (0.62, 0.40, 0.28), 0.9))
    cam_data = bpy.data.cameras.new("cam"); cam_data.lens = 50
    cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 1000, 640
    target = Vector((0, 0, 0.85))
    for name, pos in (("front", (5.2, 6.6, 1.9)), ("rear", (-5.0, -6.8, 2.2)), ("side", (8.2, 0.0, 1.1)),
                      ("nose", (1.4, 4.6, 1.3))):
        cam.location = pos
        tgt = target if name != "nose" else Vector((0.5, 2.2, 0.85))
        cam.rotation_euler = (tgt - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(K.PREVIEWS, f"{prefix}_{name}.png")
        bpy.ops.render.render(write_still=True)
    print("[wagon] previews rendered", flush=True)


# ============================================================================================ main
def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    K.reset()
    materials()
    body = build_body()
    body = cut_wheel_wells(body)
    plastic = split_off(body, ("flat_plastic_black",), "body_plastic")
    liner = split_off(body, ("flat_liner",), "body_liner")
    objs = [body, plastic, liner] + flares() + wheels() + front_end() + rear_end() + side_parts() + underbody()
    if "nobake" not in argv:
        bake_body(body)
    objs = K.finish(objs)
    print(f"[wagon] {len(objs)} objects, {sum(len(o.data.polygons) for o in objs)} faces", flush=True)
    if "preview" in argv:
        render_views(objs)
        return
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.PROJECT, "Assets", "wagon.blend"))
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    car = bpy.context.view_layer.objects.active
    car.name = car.data.name = "wagon"
    K.export("wagon", [car])
    render_views([car])
    print("[wagon] done", flush=True)


main()
