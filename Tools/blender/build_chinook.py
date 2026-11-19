"""
Builds the CH-47 Chinook for the prison intro (it lowers the shipping container into the yard) and exports
Assets/Models/chinook.glb, Assets/Models/chinook_points.json and previews in Tools/previews/models; the scene is saved
as Assets/chinook.blend.

    blender -b --factory-startup --python-exit-code 1 -P Tools/blender/build_chinook.py

Real size: 18.29 m rotors on centres 11.94 m apart, fuselage about 15.7 m long. It faces game -Z (Blender +Y) with its
origin on the ground, under the middle between the rotor hubs, wheels on the ground.

Parts (one glTF node each, baked in model space, see Engine/Model.cs):
  airframe           the fuselage, pylons, engines, gear; its paint takes the tint it's drawn with
  rotor_fwd_hub      forward rotor head: turns anticlockwise seen from above, about points "rotor_fwd" / "axis"
  rotor_fwd_blades   its three blades, in the flight pose (coned up, twisted): swapped for the blur at speed
  rotor_aft_hub      aft rotor head: turns clockwise seen from above, a mirror image of the forward one
  rotor_aft_blades   its three blades
  sling              the two hook pendants and four legs down to a 40 ft container's top corners

The rotors are geared together: they must keep PHASE_DEG apart (aft azimuth = PHASE_DEG - forward azimuth) or their
blades, which overlap by a third of the rotor, collide. main() checks that they clear each other.
"""
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import modelkit as K  # noqa: E402
from modelkit import box, cyl, lathe, mat  # noqa: E402

# --- The rotors ------------------------------------------------------------------------------------------------
RADIUS = 9.145
ROOT = 1.55            # where the aerofoil starts
CHORD = 0.81
CONE_DEG = 2.6         # the blades ride up on the lift ...
BEND = 0.26            # ... and bend up a little more towards the tip (m at the tip)
PITCH_DEG = 8.0        # at three-quarters of the radius
TWIST_DEG = 12.0       # root to tip
PHASE_DEG = 60.0
HUB_FWD = (0.0, 5.97, 4.78)
HUB_AFT = (0.0, -5.97, 5.56)
TILT_FWD_DEG = 6.0     # the shafts lean forward
TILT_AFT_DEG = 4.0
RPM = 225.0

# --- The load ---------------------------------------------------------------------------------------------------
HOOK_Y = 3.7
HOOK_Z = 0.72
SLING_LENGTH = 10.0     # hook down to the container's roof
CONTAINER = (2.44, 12.19, 2.59)   # the same 40 ft box as Level.PcLength etc., lengthways under the helicopter


# ---------------------------------------------------------------------------------------------------- materials
def materials():
    return dict(
        # Plain, not the painted_metal set: its chipped-paint mask reads as white camouflage on something this big.
        paint=mat("flat_olive_paint_tint", (0.30, 0.31, 0.19), 0.62, 0.0),
        dark=mat("flat_panel_line", (0.09, 0.10, 0.07), 0.8),
        walk=mat("flat_walkway", (0.13, 0.13, 0.11), 0.95),
        glass=mat("flat_cockpit_glass", (0.05, 0.07, 0.09), 0.08, 0.85),
        hole=mat("flat_hole", (0.02, 0.02, 0.02), 0.95),
        blade=mat("flat_blade", (0.075, 0.078, 0.075), 0.7, 0.0),
        tip=mat("flat_blade_tip", (0.46, 0.46, 0.44), 0.35, 0.8),
        head=mat("flat_rotor_head", (0.11, 0.115, 0.10), 0.45, 0.6),
        link=mat("flat_pitch_link", (0.5, 0.5, 0.48), 0.35, 0.9),
        steel=mat("steel"),
        burnt=mat("flat_exhaust", (0.16, 0.13, 0.11), 0.6, 0.7),
        rubber=mat("flat_rubber", (0.03, 0.03, 0.03), 0.85),
        strut=mat("flat_gear_strut", (0.55, 0.56, 0.52), 0.3, 0.8),
        sling=mat("flat_sling", (0.10, 0.09, 0.06), 0.9),
        red=mat("emit_nav_red", (1.0, 0.1, 0.06), emit=(1.0, 0.1, 0.06)),
    )


# -------------------------------------------------------------------------------------------------------- lofts
def smooth_table(table, x):
    """Monotone cubic interpolation through (x, values...) rows sorted by x: no overshoot between stations."""
    if x <= table[0][0]:
        return list(table[0][1:])
    if x >= table[-1][0]:
        return list(table[-1][1:])
    i = next(k for k in range(len(table) - 1) if table[k][0] <= x <= table[k + 1][0])
    out = []
    x0, x1 = table[i][0], table[i + 1][0]
    h = x1 - x0
    t = (x - x0) / h

    def slope(k, c):
        if k == 0 or k == len(table) - 1:
            j = min(k, len(table) - 2)
            return (table[j + 1][c] - table[j][c]) / (table[j + 1][0] - table[j][0])
        a = (table[k][c] - table[k - 1][c]) / (table[k][0] - table[k - 1][0])
        b = (table[k + 1][c] - table[k][c]) / (table[k + 1][0] - table[k][0])
        return 0.0 if a * b <= 0 else 2 * a * b / (a + b)

    for c in range(1, len(table[0])):
        y0, y1 = table[i][c], table[i + 1][c]
        m0, m1 = slope(i, c), slope(i + 1, c)
        out.append((2 * t ** 3 - 3 * t ** 2 + 1) * y0 + (t ** 3 - 2 * t ** 2 + t) * h * m0 +
                   (-2 * t ** 3 + 3 * t ** 2) * y1 + (t ** 3 - t ** 2) * h * m1)
    return out


def spow(v, e):
    return math.copysign(abs(v) ** e, v)


def loft(name, rings, material, smooth=True, smooth_angle=50.0):
    """A skin through rings of the same size, with the two ends capped."""
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in ring] for ring in rings]
    n = len(rings[0])
    for a, b in zip(vs, vs[1:]):
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(vs[0])
    bm.faces.new(list(reversed(vs[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    obj = K._new_obj(name, bm, material)
    obj["smooth"] = smooth
    obj["smooth_angle"] = smooth_angle
    return obj


def section_y(y, w, top, bot, cx=0.0, n_top=2.8, n_bot=3.8, seg=44, waist=0.45):
    """One ring across the length: a superellipse, squarer below the waist than above it."""
    zw = bot + waist * (top - bot)
    ring = []
    for i in range(seg):
        a = math.tau * i / seg
        c, s = math.cos(a), math.sin(a)
        n = n_top if s >= 0 else n_bot
        h = (top - zw) if s >= 0 else (zw - bot)
        ring.append((cx + w * spow(c, 2 / n), y, zw + h * spow(s, 2 / n)))
    return ring


def section_z(z, y0, y1, xh, n=3.2, seg=36):
    """One ring across the height, for the pylons (y0: rear edge, y1: front edge)."""
    yc, yh = (y0 + y1) / 2, (y1 - y0) / 2
    ring = []
    for i in range(seg):
        a = math.tau * i / seg
        ring.append((xh * spow(math.cos(a), 2 / n), yc + yh * spow(math.sin(a), 2 / n), z))
    return ring


def rod(name, a, b, r, material, verts=8, r2=None):
    """A cylinder from point a to point b."""
    a, b = Vector(a), Vector(b)
    axis = b - a
    q = axis.to_track_quat('Z', 'Y')
    m = Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4()
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=verts, radius1=r, radius2=r if r2 is None else r2,
                          depth=axis.length, matrix=m)
    obj = K._new_obj(name, bm, material)
    obj["smooth"] = True
    return obj


# ----------------------------------------------------------------------------------------------------- fuselage
FUSELAGE = [  # y, half width, top, bottom: nose to tail
    (8.86, 0.07, 1.78, 1.58),
    (8.78, 0.40, 2.02, 1.34),
    (8.58, 0.72, 2.28, 1.15),
    (8.18, 1.05, 2.66, 0.99),
    (7.70, 1.27, 3.04, 0.93),
    (7.15, 1.39, 3.38, 0.90),
    (6.55, 1.44, 3.56, 0.90),
    (5.00, 1.45, 3.58, 0.90),
    (0.00, 1.45, 3.60, 0.90),
    (-2.60, 1.45, 3.60, 0.90),
    (-3.40, 1.44, 3.60, 1.02),
    (-4.30, 1.38, 3.60, 1.40),
    (-5.20, 1.22, 3.60, 1.94),
    (-6.00, 0.96, 3.55, 2.46),
    (-6.55, 0.62, 3.45, 2.84),
    (-6.86, 0.20, 3.30, 3.06),
]
NTOP, NBOT, WAIST = 2.8, 3.8, 0.45


def fus_point(y, phi, side=1, lift=0.0):
    """The point on the fuselage's skin at length y, phi degrees up from its waist (+-90: the centre line)."""
    w, top, bot = smooth_table(FUSELAGE[::-1], y)
    zw = bot + WAIST * (top - bot)
    a = math.radians(phi)
    c, s = math.cos(a), math.sin(a)
    n = NTOP if s >= 0 else NBOT
    h = (top - zw) if s >= 0 else (zw - bot)
    x = w * spow(c, 2 / n)
    z = zw + h * spow(s, 2 / n)
    p = Vector((x, y, z))
    if lift:
        # Push out along the skin's normal, found by stepping across it.
        e = 0.05
        def f(yy, pp):
            ww, tt, bb = smooth_table(FUSELAGE[::-1], yy)
            zz = bb + WAIST * (tt - bb)
            aa = math.radians(pp)
            cc, ss = math.cos(aa), math.sin(aa)
            nn = NTOP if ss >= 0 else NBOT
            hh = (tt - zz) if ss >= 0 else (zz - bb)
            return Vector((ww * spow(cc, 2 / nn), yy, zz + hh * spow(ss, 2 / nn)))
        nrm = (f(y + e, phi) - f(y - e, phi)).cross(f(y, phi + 1.5) - f(y, phi - 1.5))
        if nrm.length > 1e-8:
            nrm.normalize()
            if nrm.dot(Vector((p.x, 0, p.z - zw))) < 0:
                nrm = -nrm
            p += nrm * lift
    p.x *= side
    return p


def patch(name, corners, material, side=1, lift=0.012, nu=8, nv=8):
    """A panel lying on the fuselage's skin: corners (y, phi) in order round it, bilinear between them."""
    (a, b, c, d) = corners
    bm = bmesh.new()
    grid = []
    for j in range(nv + 1):
        v = j / nv
        row = []
        for i in range(nu + 1):
            u = i / nu
            y = (1 - v) * ((1 - u) * a[0] + u * b[0]) + v * ((1 - u) * d[0] + u * c[0])
            phi = (1 - v) * ((1 - u) * a[1] + u * b[1]) + v * ((1 - u) * d[1] + u * c[1])
            row.append(bm.verts.new(fus_point(y, phi, side, lift)))
        grid.append(row)
    for j in range(nv):
        for i in range(nu):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    obj = K._new_obj(name, bm, material)
    obj["smooth"] = True
    obj["smooth_angle"] = 60.0
    return obj


def disc(name, y, z, radius, material, side=1, lift=0.012, n=24):
    """A round panel lying on the skin, centred at length y and height z (for the cabin's round windows)."""
    def on_skin(yy, zz):
        _, top, bot = smooth_table(FUSELAGE[::-1], yy)
        zw = bot + WAIST * (top - bot)
        h = (top - zw) if zz >= zw else (zw - bot)
        e = NTOP / 2 if zz >= zw else NBOT / 2
        s = spow(max(-1.0, min(1.0, (zz - zw) / h)), e)
        return fus_point(yy, math.degrees(math.asin(s)), side, lift)
    bm = bmesh.new()
    rim = [bm.verts.new(on_skin(y + radius * math.cos(math.tau * i / n), z + radius * math.sin(math.tau * i / n))) for i in range(n)]
    bm.faces.new(rim)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return K._new_obj(name, bm, material)


def fuselage(M):
    objs = []
    stations = [FUSELAGE[0][0] - (FUSELAGE[0][0] - FUSELAGE[-1][0]) * i / 90 for i in range(91)]
    nose_rings = [8.86 - 0.04 * i for i in range(6)]
    ys = sorted(set(nose_rings + stations), reverse=True)
    rings = [section_y(y, *smooth_table(FUSELAGE[::-1], y), n_top=NTOP, n_bot=NBOT, waist=WAIST) for y in ys]
    objs.append(loft("fuselage", rings, M["paint"]))

    # The fuel pods along the lower sides.
    pod = [
        (-4.85, 0.05, 1.46, 1.38), (-4.55, 0.22, 1.62, 1.24), (-3.95, 0.38, 1.76, 1.03), (-3.20, 0.42, 1.80, 0.95),
        (3.00, 0.42, 1.80, 0.95), (3.90, 0.38, 1.74, 0.98), (4.45, 0.25, 1.58, 1.10), (4.80, 0.05, 1.36, 1.26),
    ]
    ys = [-4.85 + 9.65 * i / 44 for i in range(45)]
    for side in (-1, 1):
        rings = [section_y(y, *smooth_table(pod, y), cx=side * 1.47, n_top=2.5, n_bot=3.0, seg=24, waist=0.5) for y in ys]
        objs.append(loft("pod", rings, M["paint"]))
        for y in (-3.2, -1.2, 0.8, 2.8):    # filler caps and straps
            objs.append(cyl("cap", 0.07, 0.015, (side * 1.885, y, 1.42), M["dark"], verts=10, rot=(0, 90, 0)))
    # The roof tunnel that carries the shaft between the rotors, with the walkway rails either side.
    tunnel = [(-3.2, 0.02, 3.72, 3.55), (-2.9, 0.34, 3.86, 3.55), (3.5, 0.34, 3.88, 3.55), (3.9, 0.02, 3.72, 3.55)]
    ys = [-3.2 + 7.1 * i / 12 for i in range(13)]
    rings = [section_y(y, *smooth_table(tunnel, y), n_top=3.5, n_bot=3.5, seg=16) for y in ys]
    objs.append(loft("tunnel", rings, M["paint"]))
    for side in (-1, 1):
        objs.append(rod("rail", (side * 0.74, -2.7, 3.95), (side * 0.74, 3.4, 3.95), 0.016, M["strut"], verts=6))
        for i in range(8):
            y = -2.7 + 6.1 * i / 7
            objs.append(rod("stanchion", (side * 0.74, y, 3.55), (side * 0.74, y, 3.95), 0.013, M["strut"], verts=6))
        objs.append(box("walkway", (0.34, 6.2, 0.015), (side * 0.52, 0.35, 3.602), M["walk"]))
    return objs


def glazing(M):
    """Windows and doors, laid on the skin. Side: +1 is the right of the aircraft (+X)."""
    objs = []
    for side in (-1, 1):
        # Windscreen, the side windows behind it, the chin windows under it, and an eyebrow over it.
        objs.append(patch("windscreen", [(8.46, 26), (7.58, 26), (7.42, 86), (8.02, 86)], M["glass"], side))
        objs.append(patch("windscreen_side", [(7.47, 8), (6.84, 8), (6.80, 66), (7.32, 74)], M["glass"], side))
        objs.append(patch("chin_window", [(8.64, -24), (7.86, -38), (7.72, 16), (8.52, 16)], M["glass"], side))
        # Round cabin windows.
        for y in (3.72, 2.0, 0.28, -1.44, -3.16):
            objs.append(disc("port_rim", y, 2.56, 0.265, M["dark"], side, lift=0.010))
            objs.append(disc("port", y, 2.56, 0.215, M["glass"], side, lift=0.016))
        # Panel lines.
        for y in (6.3, 4.5, 2.86, 1.14, -0.58, -2.3, -3.95, -4.9):
            objs.append(patch("frame", [(y + 0.012, -8), (y - 0.012, -8), (y - 0.012, 86), (y + 0.012, 86)], M["dark"], side, lift=0.006, nu=1, nv=10))
        objs.append(patch("stringer", [(6.5, 62), (-4.5, 62), (-4.5, 63), (6.5, 63)], M["dark"], side, lift=0.006, nu=16, nv=1))
        objs.append(patch("stringer", [(6.5, 4), (-3.0, 4), (-3.0, 5), (6.5, 5)], M["dark"], side, lift=0.006, nu=16, nv=1))
    # Crew door on the right, standing open; an escape hatch window on the left.
    objs.append(patch("door_frame", [(5.82, -28), (4.62, -28), (4.62, 58), (5.82, 58)], M["dark"], 1, lift=0.008))
    objs.append(patch("door_gap", [(5.75, -24), (4.69, -24), (4.69, 53), (5.75, 53)], M["hole"], 1, lift=0.014))
    objs.append(patch("hatch_frame", [(6.06, 14), (5.02, 14), (5.02, 56), (6.06, 56)], M["dark"], -1, lift=0.008))
    objs.append(patch("hatch", [(6.0, 18), (5.08, 18), (5.08, 52), (6.0, 52)], M["glass"], -1, lift=0.014))
    # Ramp at the back, its hinge line and sides.
    for side in (-1, 1):
        objs.append(patch("ramp_line", [(-3.55, -70), (-6.30, 18), (-6.30, 22), (-3.55, -66)], M["dark"], side, lift=0.008, nu=12, nv=1))
    # Pitot tubes, wipers, the chin sensor.
    for side in (-1, 1):
        objs.append(rod("pitot", (side * 0.32, 8.72, 1.62), (side * 0.42, 9.1, 1.62), 0.012, M["strut"], verts=6))
    objs.append(lathe("sensor", [(0.0, -0.12), (0.07, -0.11), (0.12, -0.06), (0.13, 0.0), (0.12, 0.05)], (0.0, 8.42, 1.05), M["dark"], verts=16))
    return objs


def pylons(M):
    objs = []
    # Forward: a doghouse over the cockpit, its nose sloping back from the windscreen.
    table = [
        (3.20, 3.20, 7.72, 1.04), (3.55, 3.45, 7.58, 0.94), (3.86, 3.95, 7.28, 0.84),
        (4.14, 4.62, 6.98, 0.68), (4.34, 5.02, 6.78, 0.52), (4.44, 5.18, 6.66, 0.40), (4.48, 5.32, 6.56, 0.24),
    ]
    zs = [3.20 + 1.28 * i / 16 for i in range(17)]
    objs.append(loft("pylon_fwd", [section_z(z, *smooth_table(table, z), n=3.4) for z in zs], M["paint"]))
    # Aft: the tall tail, straight-raked at the front and sweeping back and down behind.
    table = [
        (2.70, -7.05, -2.65, 1.34), (3.55, -7.28, -3.00, 1.12), (4.15, -7.30, -3.65, 0.90),
        (4.70, -7.15, -4.25, 0.74), (5.02, -6.98, -4.72, 0.60), (5.20, -6.82, -5.05, 0.44), (5.27, -6.68, -5.25, 0.26),
    ]
    zs = [2.70 + 2.57 * i / 22 for i in range(23)]
    objs.append(loft("pylon_aft", [section_z(z, *smooth_table(table, z), n=3.6) for z in zs], M["paint"]))
    # Vents and panels on the tail, intake grilles on the forward pylon.
    for side in (-1, 1):
        objs.append(box("vent", (0.02, 0.8, 0.34), (side * 0.72, -5.2, 4.72), M["dark"]))
        objs.append(box("vent", (0.02, 0.34, 0.5), (side * 0.72, 6.0, 3.92), M["dark"]))
    # The anti-collision beacon on the tail.
    objs.append(cyl("beacon", 0.05, 0.1, (0, -6.9, 4.98), M["red"], verts=10))

    # The two engines, on stubs from the tail. Lathe profiles run from the back to the front.
    prof = [
        (0.00, -1.72), (0.20, -1.72), (0.30, -1.66), (0.36, -1.45), (0.43, -1.1), (0.50, -0.7), (0.53, 0.0),
        (0.53, 0.9), (0.50, 1.33), (0.46, 1.46), (0.43, 1.52),
    ]
    for side in (-1, 1):
        loc = (side * 1.55, -5.06, 3.98)
        objs.append(lathe("nacelle", prof, loc, M["paint"], verts=32, rot=(-90, 0, 0)))
        objs.append(lathe("intake_lip", [(0.47, 1.40), (0.49, 1.48), (0.46, 1.56), (0.40, 1.58), (0.36, 1.50), (0.33, 1.36)], loc, M["steel"], verts=32, rot=(-90, 0, 0)))
        objs.append(cyl("intake_back", 0.335, 0.04, (loc[0], loc[1] + 1.26, loc[2]), M["hole"], verts=24, rot=(90, 0, 0)))
        objs.append(lathe("bullet", [(0.26, 1.15), (0.2, 1.35), (0.1, 1.54), (0.02, 1.62), (0.0, 1.63)], loc, M["steel"], verts=20, rot=(-90, 0, 0)))
        objs.append(lathe("exhaust", [(0.0, -1.95), (0.2, -1.95), (0.27, -1.8), (0.32, -1.62)], loc, M["burnt"], verts=24, rot=(-90, 0, 0)))
        objs.append(cyl("exhaust_hole", 0.205, 0.02, (loc[0], loc[1] - 1.96, loc[2]), M["hole"], verts=20, rot=(90, 0, 0)))
        objs.append(box("engine_band", (1.09, 0.03, 1.09), (loc[0], loc[1] - 0.15, loc[2]), M["dark"]))
        # Stub: faired back into the tail.
        objs.append(box("stub", (1.2, 1.5, 0.32), (side * 0.92, -5.3, 3.88), M["paint"], bevel=0.05))
        objs.append(box("stub_top", (0.34, 1.0, 0.25), (side * 1.33, -5.2, 4.44), M["paint"], bevel=0.05))
    return objs


def gear(M):
    objs = []
    def wheel(x, y, z=0.46, r=0.46, w=0.24):
        out = [lathe("tyre", [(r * 0.55, -w / 2), (r * 0.88, -w / 2), (r * 0.97, -w * 0.38), (r, -w * 0.15), (r, w * 0.15), (r * 0.97, w * 0.38), (r * 0.88, w / 2), (r * 0.55, w / 2)],
                     (x, y, z), M["rubber"], verts=26, rot=(0, 90, 0)),
               cyl("rim", r * 0.57, w * 0.88, (x, y, z), M["strut"], verts=18, rot=(0, 90, 0))]
        return out
    for side in (-1, 1):
        # Forward: twin wheels on a stout leg under the pod.
        x, y = side * 1.56, 3.28
        objs += wheel(x - 0.15, y)
        objs += wheel(x + 0.15, y)
        objs.append(cyl("axle", 0.07, 0.6, (x, y, 0.46), M["dark"], verts=10, rot=(0, 90, 0)))
        objs.append(rod("leg", (x, y, 0.46), (x, y, 1.05), 0.075, M["strut"], verts=10))
        objs.append(rod("leg_sleeve", (x, y, 0.72), (x, y, 1.05), 0.105, M["dark"], verts=10))
        objs.append(rod("drag", (x, y, 0.5), (x - side * 0.3, y - 0.65, 1.0), 0.045, M["dark"], verts=8))
        objs.append(rod("drag", (x, y, 0.5), (x - side * 0.1, y + 0.55, 1.03), 0.045, M["dark"], verts=8))
        # Aft: one wheel each, on a swivelling leg.
        x, y = side * 1.60, -3.70
        objs += wheel(x, y, z=0.43, r=0.43, w=0.22)
        objs.append(rod("leg", (x, y + 0.08, 0.43), (x, y + 0.08, 1.12), 0.07, M["strut"], verts=10))
        objs.append(rod("leg_sleeve", (x, y + 0.08, 0.76), (x, y + 0.08, 1.12), 0.1, M["dark"], verts=10))
        objs.append(rod("fork", (x + side * 0.15, y, 0.43), (x + side * 0.05, y + 0.08, 0.8), 0.035, M["dark"], verts=8))
        objs.append(rod("fork", (x - side * 0.15, y, 0.43), (x - side * 0.05, y + 0.08, 0.8), 0.035, M["dark"], verts=8))
    # The cargo hooks on the belly.
    for y in (HOOK_Y, 0.0, -HOOK_Y):
        objs.append(box("hook_beam", (0.16, 0.34, 0.12), (0, y, 0.86), M["dark"], bevel=0.015))
        objs.append(rod("hook", (0, y, HOOK_Z + 0.02), (0, y, 0.84), 0.035, M["strut"], verts=8))
    return objs


def masts(M):
    """The fixed part of each rotor stack, down into its pylon: the mast and the swashplate's lower ring."""
    objs = []
    for hub, tilt, base in ((HUB_FWD, TILT_FWD_DEG, 4.40), (HUB_AFT, TILT_AFT_DEG, 5.20)):
        m = hub_matrix(hub, tilt)
        depth = hub[2] - base + 0.1
        o = lathe("mast", [(0.27, -depth), (0.22, -depth + 0.1), (0.17, -0.42), (0.14, -0.3)], (0, 0, 0), M["head"], verts=20)
        o2 = lathe("swash_lower", [(0.0, -0.66), (0.40, -0.66), (0.46, -0.61), (0.44, -0.56), (0.0, -0.56)], (0, 0, 0), M["head"], verts=20)
        for x in (o, o2):
            x["local_matrix"] = [v for row in m for v in row]
        objs += [o, o2]
    return objs


# ------------------------------------------------------------------------------------------------------- rotors
def hub_matrix(hub, tilt_deg):
    """Hub space (spin axis +Z, blades in the XY plane) to model space. A shaft leaning forward tips +Z towards +Y."""
    return Matrix.Translation(hub) @ Matrix.Rotation(math.radians(-tilt_deg), 4, 'X')


def blade_height(r):
    """How far a point on the blade has ridden up, r metres out from the hub (Game/Chinook.cs repeats this)."""
    return r * math.tan(math.radians(CONE_DEG)) + BEND * (r / RADIUS) ** 2


def aerofoil(n=9):
    """A cambered section, chord 1 from the leading edge (x = 0) to the trailing edge, thin at the back, from the
    trailing edge round the top and back underneath."""
    pts = []
    def thick(x):
        return 0.5 * (0.2969 * math.sqrt(x) - 0.1260 * x - 0.3516 * x * x + 0.2843 * x ** 3 - 0.1036 * x ** 4) * 1.15
    def camber(x):
        return 0.02 * math.sin(math.pi * x) * (1.0 - 0.35 * x)
    xs = [0.5 * (1 - math.cos(math.pi * i / n)) for i in range(n + 1)]
    for x in reversed(xs):
        pts.append((x, camber(x) + thick(x)))
    for x in xs[1:-1]:
        pts.append((x, camber(x) - thick(x)))
    return pts


def blade(turn, M):
    """One blade along +X in hub space, for rotation direction turn (+1 anticlockwise from above: leading edge +Y)."""
    foil = aerofoil()
    stations = [ROOT, ROOT + 0.25, ROOT + 0.8, 3.2, 4.8, 6.3, 7.5, 8.2, 8.55, 8.8, 9.0, RADIUS]
    rings = []
    for r in stations:
        c = CHORD
        if r < ROOT + 0.8:
            c = CHORD * (0.52 + 0.48 * math.sin((r - ROOT) / 0.8 * math.pi / 2))
        if r > 8.2:   # the last bit of the tip tapers and sweeps back
            t = (r - 8.2) / (RADIUS - 8.2)
            c = CHORD * (1.0 - 0.60 * t * t)
        sweep = 0.0
        if r > 8.2:
            t = (r - 8.2) / (RADIUS - 8.2)
            sweep = 0.14 * t * t
        pitch = math.radians(PITCH_DEG + TWIST_DEG * (0.75 - r / RADIUS))
        ring = []
        for x, z in foil:
            # About the quarter chord: leading edge forward of it, and up with the pitch.
            a = (0.25 - x) * c - sweep          # + towards the leading edge
            b = z * c
            tang = a * math.cos(pitch) - b * math.sin(pitch)
            up = a * math.sin(pitch) + b * math.cos(pitch)
            ring.append((r, turn * tang, up + blade_height(r)))
        rings.append(ring)
    o = loft("blade", rings, M["blade"], smooth=True, smooth_angle=62.0)
    objs = [o]
    # The tip's erosion cap.
    tip_rings = []
    for r in (8.8, 9.0, RADIUS):
        idx = stations.index(r)
        tip_rings.append([(x, y, z + 0.002) for x, y, z in rings[idx]])
    objs.append(loft("blade_tip", tip_rings, M["tip"], smooth=True, smooth_angle=62.0))
    return objs


def rotor_head(turn, M):
    objs = []
    # The central hub and the swashplate's turning ring.
    objs.append(lathe("hub", [(0.0, -0.3), (0.2, -0.3), (0.27, -0.2), (0.3, -0.1), (0.3, 0.1), (0.25, 0.2), (0.14, 0.28), (0.0, 0.31)], (0, 0, 0), M["head"], verts=24))
    objs.append(lathe("swash_upper", [(0.14, -0.55), (0.44, -0.55), (0.46, -0.5), (0.42, -0.46), (0.14, -0.46)], (0, 0, 0), M["head"], verts=20))
    objs.append(cyl("hub_top", 0.09, 0.14, (0, 0, 0.36), M["steel"], verts=12))
    for k in range(3):
        rot = Matrix.Rotation(math.radians(120 * k), 4, 'Z')
        parts = []
        # Yoke: the arm out to the flap and lag hinges, and the pitch housing beyond them.
        parts.append(box("yoke", (0.42, 0.24, 0.26), (0.36, 0, 0.0), M["head"], bevel=0.03))
        parts.append(rod("housing", (0.45, 0, 0.0), (1.02, 0, blade_height(1.02)), 0.135, M["head"], verts=14, r2=0.115))
        parts.append(rod("hinge", (0.48, -0.17, 0), (0.48, 0.17, 0), 0.06, M["steel"], verts=10))
        # The spar out of the cuff: it stays solid at speed, where the blades themselves become a blur.
        parts.append(rod("spar", (0.95, 0, blade_height(0.95)), (ROOT + 0.08, 0, blade_height(ROOT + 0.08)), 0.1, M["blade"], verts=12, r2=0.08))
        # The lag damper alongside.
        parts.append(rod("damper", (0.25, -turn * 0.24, -0.04), (0.92, -turn * 0.16, 0.0), 0.05, M["steel"], verts=8))
        # The pitch horn and link, running down to the swashplate.
        parts.append(box("horn", (0.09, 0.34, 0.05), (0.7, turn * 0.26, -0.03), M["head"]))
        parts.append(rod("pitch_link", (0.7, turn * 0.4, -0.03), (0.42, turn * 0.22, -0.5), 0.022, M["link"], verts=6))
        for p in parts:
            K._apply_modifiers_and_transform(p)
            p.data.transform(rot)
        objs += parts
    return objs


def rotor_blades(turn, M):
    objs = []
    for k in range(3):
        rot = Matrix.Rotation(math.radians(120 * k), 4, 'Z')
        for p in blade(turn, M):
            K._apply_modifiers_and_transform(p)
            p.data.transform(rot)
            objs.append(p)
    return objs


# --------------------------------------------------------------------------------------------------------- load
def sling(M):
    objs = []
    roof = HOOK_Z - SLING_LENGTH
    wx, ly, _ = CONTAINER
    for s in (1, -1):
        hook = Vector((0, s * HOOK_Y, HOOK_Z))
        ring = Vector((0, s * HOOK_Y * 1.05, HOOK_Z - 4.2))
        objs.append(rod("pendant", hook, ring, 0.03, M["sling"], verts=7))
        objs.append(rod("ring", ring + Vector((0, 0, 0.12)), ring - Vector((0, 0, 0.12)), 0.06, M["steel"], verts=8))
        for side in (-1, 1):
            corner = Vector((side * (wx / 2 - 0.08), s * (ly / 2 - 0.09), roof))
            objs.append(rod("leg", ring, corner + Vector((0, 0, 0.12)), 0.022, M["sling"], verts=6))
            objs.append(box("shackle", (0.09, 0.09, 0.14), corner + Vector((0, 0, 0.06)), M["steel"]))
    return objs


# --------------------------------------------------------------------------------------------------------- main
def check_blades_clear():
    """Sweeps a whole turn with the rotors geared at PHASE_DEG: the closest any two blades' centre lines come."""
    mf, ma = hub_matrix(HUB_FWD, TILT_FWD_DEG), hub_matrix(HUB_AFT, TILT_AFT_DEG)
    rs = [ROOT + (RADIUS - ROOT) * i / 18 for i in range(19)]
    def pts(m, az):
        a = math.radians(az)
        return [m @ Vector((r * math.cos(a), r * math.sin(a), blade_height(r))) for r in rs]
    closest = 1e9
    for step in range(0, 120):
        theta = float(step)
        for i in range(3):
            pf = pts(mf, theta + 120 * i)
            for j in range(3):
                pa = pts(ma, PHASE_DEG - theta + 120 * j)
                closest = min(closest, min((p - q).length for p in pf for q in pa))
    print(f"[chinook] blades pass {closest:.2f} m apart at their closest", flush=True)
    if closest < 1.0:
        raise RuntimeError(f"The rotors' blades come within {closest:.2f} m of each other")
    return closest


def game_vec(v):
    """Blender (x, y, z) to the game's (x, z, -y)."""
    return [round(v[0], 4), round(v[2], 4), round(-v[1], 4)]


def write_points():
    roof = HOOK_Z - SLING_LENGTH
    def axis(tilt):
        return Matrix.Rotation(math.radians(-tilt), 3, 'X') @ Vector((0, 0, 1))
    points = {
        "rotor_fwd": game_vec(HUB_FWD), "rotor_fwd_axis": game_vec(axis(TILT_FWD_DEG)),
        "rotor_aft": game_vec(HUB_AFT), "rotor_aft_axis": game_vec(axis(TILT_AFT_DEG)),
        "phase_deg": PHASE_DEG, "rpm": RPM,
        "blade": {"radius": RADIUS, "root": ROOT, "chord": CHORD, "cone_deg": CONE_DEG, "bend": BEND},
        "hook_fwd": game_vec((0, HOOK_Y, HOOK_Z)), "hook_aft": game_vec((0, -HOOK_Y, HOOK_Z)),
        "container_origin": game_vec((0, 0, roof - CONTAINER[2])),
    }
    path = os.path.join(K.OUT, "chinook_points.json")
    with open(path, "w") as f:
        json.dump(points, f, indent=2)
    print(f"[chinook] {path}", flush=True)


def part(name, objs):
    out = K.finish(objs, join_name=name, smooth_angle=45.0)[0]
    bpy.ops.object.select_all(action='DESELECT')
    out.select_set(True)
    bpy.context.view_layer.objects.active = out
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return out


def main():
    K.reset()
    check_blades_clear()
    M = materials()

    # The fixed rotor stacks are built in hub space and moved into place before joining.
    stacks = masts(M)
    for o in stacks:
        K._apply_modifiers_and_transform(o)
        o.data.transform(hub_matrix(HUB_FWD if o is stacks[0] or o is stacks[1] else HUB_AFT, TILT_FWD_DEG if o is stacks[0] or o is stacks[1] else TILT_AFT_DEG))
        del o["local_matrix"]

    airframe = part("airframe", fuselage(M) + glazing(M) + pylons(M) + gear(M) + stacks)
    final = [airframe]
    for name, hub, tilt, turn in (("rotor_fwd", HUB_FWD, TILT_FWD_DEG, 1), ("rotor_aft", HUB_AFT, TILT_AFT_DEG, -1)):
        h = part(name + "_hub", rotor_head(turn, M))
        b = part(name + "_blades", rotor_blades(turn, M))
        m = hub_matrix(hub, tilt)
        h.data.transform(m)
        b.data.transform(m)
        h.data.update()
        b.data.update()
        final += [h, b]
    slings = part("sling", sling(M))
    final.append(slings)

    K.export("chinook", final)
    write_points()
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.PROJECT, "Assets", "chinook.blend"))

    # Previews: the whole helicopter, and closer views of the airframe. The sling is hidden, as it would go through
    # the preview's ground.
    slings.hide_render = True
    no_sling = [o for o in final if o is not slings]
    K.preview("chinook_full", no_sling, angle=(-48, 18))
    K.preview("chinook_side", [airframe], angle=(90, 3))
    K.preview("chinook_left", [airframe], angle=(-90, 3))
    K.preview("chinook_front", [airframe], angle=(160, 14))
    K.preview("chinook_rear", [airframe], angle=(-35, 20))
    K.preview("chinook_top", no_sling, angle=(20, 78))
    K.preview("chinook_head", [o for o in final if o.name == "rotor_fwd_hub"], angle=(70, 22))


main()
