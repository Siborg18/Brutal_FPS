"""
Builds the British service guns from one parametric kit, and exports each as Assets/Models/<id>.glb, its source as
Assets/<id>.blend, its key points for the game as Assets/Models/<id>_points.json (Game/BritishGuns.cs makes its RifleSpec
from them), and a side preview as Tools/previews/models/<id>_right.png:

    l85a2 l85a3     SA80 bullpups, SUSAT sight
    l403a1          Knight's KS-1, low-power scope, suppressor
    l143a2          SIG MCX, holographic sight and magnifier, suppressor
    l119a1          Colt Canada C8 SFW, red dot
    l129a1          LMT sharpshooter rifle, 7.62 mm, ACOG
    l135a1          Barrett M82A1, .50 BMG
    l92a1           MP5SD3, integral suppressor
    l80a1           MP5K

    blender -b --factory-startup --python-exit-code 1 -P Tools/blender/build_british_guns.py [-- nopreview] [-- l85a2 l80a1 ...]

Coordinates (Blender, like the other long guns): +X out of the muzzle, +Z up, +Y the gun's left side; x = 0 is the trigger;
z = 0 is the AK's bore, so the pistol grip, trigger and guard are the AK's and the AK's hands fit: the left hand is moved
by SHIFT onto a handguard whose lower half under it is the AK's section. In millimetres, scaled to metres at the end.
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
from modelkit import box, cyl, lathe, prism, mat, add_bevel  # noqa: E402

MM = 0.001
RX = (0, 90, 0)
GRIP_A, GRIP_B = Vector((-46, 0, -30)), Vector((-77, 0, -138))


# ============================================================================================ helpers
def smooth(o, angle=40.0):
    o["smooth"] = True
    o["smooth_angle"] = angle
    return o


def se_ring(a, b_top, b_bot=None, n=2.5, count=32):
    b_bot = b_top if b_bot is None else b_bot
    pts = []
    for i in range(count):
        t = -math.pi / 2 + math.tau * i / count
        c, s = math.cos(t), math.sin(t)
        pts.append((math.copysign(abs(c) ** (2.0 / n), c) * a, math.copysign(abs(s) ** (2.0 / n), s) * (b_top if s > 0 else b_bot)))
    return pts


def loft(name, rings, material, angle=50.0):
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in ring] for ring in rings]
    n = len(rings[0])
    for j in range(len(vs) - 1):
        for i in range(n):
            k = (i + 1) % n
            bm.faces.new((vs[j][i], vs[j][k], vs[j + 1][k], vs[j + 1][i]))
    bm.faces.new(vs[0])
    bm.faces.new(list(reversed(vs[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return smooth(K._new_obj(name, bm, material), angle)


def lerp_table(table, x):
    if x <= table[0][0]:
        return table[0][1]
    for (x0, v0), (x1, v1) in zip(table, table[1:]):
        if x <= x1:
            return v0 + (v1 - v0) * (x - x0) / (x1 - x0)
    return table[-1][1]


def slab(name, pts, width, material, y=0.0, bevel=1.2):
    o = prism(name, pts, width, (0, y, 0), material, axis='Y')
    return smooth(add_bevel(o, bevel), 35) if bevel else o


def bx(name, x0, x1, z0, z1, width, material, y=0.0, bevel=0.8):
    o = box(name, (x1 - x0, width, z1 - z0), ((x0 + x1) / 2, y, (z0 + z1) / 2), material)
    return add_bevel(o, bevel) if bevel else o


def tube(name, x0, x1, r, z, material, y=0.0, verts=24):
    return smooth(lathe(name, [(0, x0), (r, x0), (r, x1), (0, x1)], loc=(0, y, z), rot=RX, material=material, verts=verts))


def rail(R, name, x0, x1, z, material, width=21.0):
    R.append(add_bevel(box(f"{name}_base", (x1 - x0, width, 4.0), ((x0 + x1) / 2, 0, z + 2.0), material), 0.35))
    x, i = x0 + 2.5, 0
    while x + 5.3 < x1:
        R.append(add_bevel(box(f"{name}_t{i}", (5.3, width, 4.0), (x + 2.65, 0, z + 6.0), material), 0.35))
        x += 10.0
        i += 1


class Mats:
    def __init__(self):
        self.poly = mat("polymer", None, 0.6, 0.0)
        self.stipple = mat("tex_grip_stipple", None, 0.7, 0.0)
        self.steel = mat("gunmetal", None, 0.35, 0.85)
        self.anod = mat("flat_anodized", (0.045, 0.045, 0.048), 0.42, 0.35)
        self.dark = mat("flat_recess", (0.02, 0.02, 0.02), 0.8, 0.0)
        self.rubber = mat("flat_rubber", (0.035, 0.035, 0.035), 0.85, 0.0)
        self.glass = mat("flat_lens", (0.05, 0.08, 0.12), 0.08, 0.6)
        self.brass = mat("flat_brass", (0.72, 0.55, 0.25), 0.35, 1.0)
        self.tan = mat("flat_fde", (0.42, 0.33, 0.21), 0.55, 0.05)
        self.sand = mat("flat_sand", (0.55, 0.46, 0.32), 0.5, 0.15)
        self.od = mat("flat_od", (0.17, 0.17, 0.09), 0.6, 0.0)
        self.grey = mat("flat_scope_grey", (0.13, 0.14, 0.13), 0.5, 0.2)
        self.red = mat("flat_red", (0.7, 0.05, 0.03), 0.5, 0.0)


# ============================================================================================ what they share
def common(p, m, grip=None, guard=None):
    """The AK's grip, trigger and guard."""
    grip = grip or m.stipple
    guard = guard or m.poly

    def section(t):
        d = lerp_table([(0, 52), (0.18, 44), (0.5, 40), (0.85, 43), (1.0, 42)], t)
        w = lerp_table([(0, 26), (0.3, 29), (0.8, 31), (1.0, 30)], t)
        k = 1.0 if t <= 1.0 else max(0.08, 1.0 - ((t - 1.0) / 0.06) ** 2) ** 0.5
        return se_ring(w / 2 * k, d / 2 * k, n=2.3, count=32)

    tan = (GRIP_B - GRIP_A).normalized()
    side = Vector((0, 1, 0))
    nrm = side.cross(tan).normalized()
    rings = []
    for t in [i / 16 for i in range(17)] + [1.02, 1.035, 1.048, 1.057, 1.06]:
        c = GRIP_A.lerp(GRIP_B, t)
        rings.append([c + side * a + nrm * b for a, b in section(t)])
    p["Furniture"].append(loft("p_grip", rings, grip, angle=60))
    trig = prism("p_trigger", [(4, -36), (-4, -36), (-4.5, -47), (-2.5, -56), (2.5, -63), (5.5, -62.2), (2.2, -55), (1.2, -47)],
                 6.0, (0, 0, 0), m.steel, axis='Y')
    p["Trigger"].append(smooth(add_bevel(trig, 0.5)))
    path = [Vector((44, 0, -41)), Vector((43.5, 0, -63)), Vector((39, 0, -70.5)), Vector((31, 0, -73)), Vector((-6, 0, -73)),
            Vector((-14, 0, -70)), Vector((-18.5, 0, -62)), Vector((-20, 0, -41))]
    pts = []
    for a, b in zip(path, path[1:]):
        pts += [a.lerp(b, k / 4) for k in range(4)]
    pts.append(path[-1])
    rings = []
    for i, c in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        n = side.cross(t).normalized()
        rings.append([c + side * a + n * b for a, b in se_ring(7.0, 2.6, n=5, count=14)])
    p["Receiver"].append(loft("p_guard", rings, guard, angle=50))


def handguard(R, name, x0, x1, top, half, bot, shift, material):
    """A handguard whose lower half under the left hand is the AK's lower handguard section, moved by shift."""
    ak_bot = [(181, -34), (192, -35.5), (205, -33), (225, -26), (250, -23), (300, -21.5), (324, -21)]
    ak_w = [(181, 20.5), (195, 22), (212, 21), (235, 18.5), (300, 17.5), (324, 17)]
    sx, sz = shift[0], shift[2]
    zc = (top + min(bot, -21 + sz)) / 2 + 6

    def ring(x):
        k = lerp_table([(x0, 0.94), (x0 + 6, 1.0), (x1 - 6, 1.0), (x1, 0.94)], x)
        hand = lerp_table([(181 + sx - 18, 0.0), (181 + sx, 1.0), (324 + sx, 1.0), (324 + sx + 18, 0.0)], x)
        a = (half * (1 - hand) + min(lerp_table(ak_w, x - sx), half) * hand) * k
        b = bot * (1 - hand) + min(bot, lerp_table(ak_bot, x - sx) + sz) * hand
        return [Vector((x, u, zc + v)) for u, v in se_ring(a, (top - zc) * k, (zc - b) * k, n=3.4, count=36)]

    xs = [x0, x0 + 3] + list(range(int(x0) + 8, int(x1) - 5, 8)) + [x1 - 3, x1]
    R.append(loft(name, [ring(x) for x in xs], material, angle=55))
    return zc


def straight_mag(p, m, top, bottom, depth, half, material, curve=0.0, floor=None, ribs=True, round_mat=None):
    """A box magazine from its top point to its bottom, bowed forward by curve."""
    M = p["Magazine"]
    side = Vector((0, 1, 0))
    rings = []
    a = bottom - top
    tan0 = a.normalized()
    nrm = side.cross(tan0).normalized()
    count = 14
    for i in range(count + 1):
        t = i / count
        c = top.lerp(bottom, t) - Vector((abs(nrm.x), 0, nrm.z)) * (curve * math.sin(math.pi * t))
        rings.append([c + side * u + nrm * v for u, v in se_ring(half, depth / 2, n=6.0, count=28)])
    M.append(loft("p_mag", rings, material, angle=40))
    e = bottom + tan0 * 6
    fl = [[c + side * u + nrm * v for u, v in se_ring(half + 1.8, depth / 2 + 3, n=6.0, count=28)] for c in (bottom - tan0 * 1, e)]
    M.append(loft("p_mag_floor", fl, floor or material, angle=40))
    if ribs:
        ang = math.degrees(math.atan2(tan0.x, -tan0.z))
        for s in (1, -1):
            for k in range(5):
                c = top.lerp(bottom, 0.45 + k * 0.09)
                M.append(add_bevel(box(f"p_mag_rib{s}{k}", (depth - 14, 1.4, 2.2), (c.x, s * (half + 0.3), c.z), material, rot=(0, -ang, 0)), 0.3))
    M.append(smooth(cyl("p_mag_round", min(half * 0.42, 6), depth * 0.6, (top.x + 2, 0, top.z - 2), round_mat or m.brass, verts=12, rot=RX)))
    return dict(MagTop=top, MagBot=bottom, MagDepth=depth, MagHalf=half)


def action(p, m, port, half, bore, body=None):
    """The port's dark recess on the right and the bolt seen through it; returns the bolt's face x."""
    x0, x1, z0, z1 = port
    p["Receiver"].append(add_bevel(box("p_port", (x1 - x0, 1.6, z1 - z0), ((x0 + x1) / 2, -half - 0.2, (z0 + z1) / 2), m.dark), 0.2))
    B = p["BoltCarrier"]
    B.append(add_bevel(box("p_carrier", (x1 - x0 - 2, 1.4, z1 - z0 - 3), ((x0 + x1) / 2 - 1, -half - 1.0, (z0 + z1) / 2), body or m.steel), 0.2))
    B.append(smooth(cyl("p_bolt_head", 5.0, 1.6, (x1 - 8, -half - 1.8, (z0 + z1) / 2), m.steel, verts=14, rot=(90, 0, 0))))
    return x1


def selector(p, m, half, x=-38.0, z=-20.0, material=None):
    material = material or m.steel
    for s in (1, -1):
        y = s * (half + 1.2)
        p["Selector"].append(smooth(cyl(f"p_sel_hub{s}", 5.5, 2.4, (x, y, z), material, verts=16, rot=(90, 0, 0))))
        lever = prism(f"p_sel{s}", [(x - 3, z - 3), (x + 3, z + 2.5), (x - 12, z + 14), (x - 17, z + 10)], 2.4, (0, y + s * 1.8, 0), material, axis='Y')
        p["Selector"].append(smooth(add_bevel(lever, 0.5)))


# ============================================================================================ muzzles
def flash_hider(R, m, x0, x1, bore, r=10.0):
    R.append(smooth(lathe("p_hider", [(0, x0), (r, x0), (r, x0 + 4), (r + 1.5, x0 + 8), (r + 1.5, x1 - 2), (r, x1), (r * 0.72, x1), (0, x1 - 14)],
                          loc=(0, 0, bore), rot=RX, material=m.anod, verts=20)))
    for k in range(3):
        R.append(box(f"p_hider_slot{k}", (x1 - x0 - 16, 2 * r + 6, 2.6), ((x0 + x1) / 2 + 5, 0, bore), m.dark, rot=(k * 60, 0, 0)))


def suppressor(R, m, x0, x1, bore, r, material):
    R.append(smooth(lathe("p_suppressor", [(0, x0), (r * 0.7, x0), (r * 0.9, x0 + 8), (r, x0 + 14), (r, x1 - 4), (r - 2.5, x1), (5, x1), (0, x1 - 1)],
                          loc=(0, 0, bore), rot=RX, material=material, verts=32)))
    R.append(cyl("p_suppressor_bore", 5.0, 1.0, (x1 + 0.2, 0, bore), m.dark, verts=14, rot=RX))


# ============================================================================================ sights
def tube_scope(R, m, e, o, z, material, base_top, r_tube=15.0, r_obj=19.0, r_eye=19.0, turret=None):
    """A riflescope from its eyepiece e to its objective o, on axis z, in two rings on a base down to base_top."""
    turret = turret if turret is not None else (e + o) / 2
    R.append(smooth(lathe("p_scope", [(0, e + 2), (r_eye - 2, e + 2), (r_eye, e), (r_eye, e + 36), (r_tube + 1, e + 52), (r_tube, e + 62), (r_tube, o - 56),
                                      (r_obj - 1, o - 22), (r_obj, o - 12), (r_obj, o), (r_obj - 2, o), (r_obj - 2, o - 3), (0, o - 3)],
                          loc=(0, 0, z), rot=RX, material=material, verts=36), 35))
    R.append(cyl("p_eye_lens", r_eye - 2.4, 0.6, (e + 2.4, 0, z), m.glass, verts=28, rot=RX))
    R.append(cyl("p_obj_lens", r_obj - 2.4, 0.6, (o - 2.6, 0, z), m.glass, verts=28, rot=RX))
    R.append(smooth(cyl("p_turret_top", 11, 15, (turret, 0, z + r_tube + 6), material, verts=24)))
    R.append(smooth(cyl("p_turret_side", 11, 15, (turret, -r_tube - 6, z), material, verts=24, rot=(90, 0, 0))))
    for i, x in enumerate((turret - 44, turret + 44)):
        R.append(smooth(lathe(f"p_ring{i}", [(r_tube - 0.2, x - 8), (r_tube + 4.5, x - 8), (r_tube + 4.5, x + 8), (r_tube - 0.2, x + 8), (r_tube - 0.2, x - 8)],
                              loc=(0, 0, z), rot=RX, material=m.anod, verts=32)))
        R.append(bx(f"p_ring_base{i}", x - 9, x + 9, base_top - 1, z - r_tube, 24, m.anod))
    R.append(bx("p_mount", turret - 60, turret + 60, base_top - 3, base_top + 2, 26, m.anod))


def prism_sight(R, m, e, o, z, material, base_top, r=21.0, top_dot=False):
    """A squat prism sight (SUSAT, ACOG): a fat tapered body from e to o on axis z, on a tall base."""
    R.append(smooth(lathe("p_prism", [(0, e + 2), (r * 0.72, e + 2), (r * 0.8, e), (r * 0.8, e + 18), (r * 0.72, e + 30), (r * 0.8, e + 50), (r, (e + o) / 2),
                                      (r * 1.1, o - 24), (r * 1.2, o - 6), (r * 1.2, o), (r * 1.08, o), (r * 1.08, o - 3), (0, o - 3)],
                          loc=(0, 0, z), rot=RX, material=material, verts=36), 35))
    R.append(cyl("p_eye_lens", r * 0.68, 0.6, (e + 2.4, 0, z), m.glass, verts=24, rot=RX))
    R.append(cyl("p_obj_lens", r * 1.04, 0.6, (o - 2.6, 0, z), m.glass, verts=24, rot=RX))
    mid = (e + o) / 2
    R.append(smooth(cyl("p_adjuster", 10, 13, (mid, -r - 3, z), material, verts=20, rot=(90, 0, 0))))
    R.append(bx("p_prism_base", mid - 34, mid + 34, base_top - 1, z - r * 0.8, 28, m.anod))
    R.append(bx("p_prism_clamp", mid - 50, mid + 50, base_top - 3, base_top + 4, 30, m.anod))
    if top_dot:
        R.append(bx("p_mini_dot", mid - 16, mid + 16, z + r * 0.85, z + r * 0.85 + 15, 22, m.anod))
        R.append(bx("p_mini_glass", mid - 16.5, mid - 15.5, z + r * 0.85 + 3, z + r * 0.85 + 13, 16, m.glass, bevel=0))
        R.append(smooth(cyl("p_top_knob", 7, 9, (mid + 2, 0, z + r * 0.85 + 18), m.anod, verts=16)))


def holo_sight(R, m, x0, x1, z, material, base_top):
    """A holographic sight: a boxy hood round an open window on the line of sight z."""
    hw, ht, hb = 21.0, 21.0, 19.0
    for s in (1, -1):
        R.append(bx(f"p_holo_wall{s}", x0, x1, z - hb, z + ht, 5, material, y=s * (hw - 2.5)))
    R.append(bx("p_holo_roof", x0, x1, z + ht - 4, z + ht + 2, 2 * hw, material))
    R.append(bx("p_holo_body", x0, x1 + 16, base_top, z - hb + 2, 2 * hw, material))
    R.append(bx("p_holo_glass", x1 - 2, x1 - 1, z - hb, z + ht - 3, 2 * hw - 9, m.glass, bevel=0))


def dot_sight(R, m, x0, x1, z, material, base_top):
    """A small tube red dot, open on the line of sight z, on a riser."""
    R.append(smooth(lathe("p_dot", [(12.5, x0), (17, x0), (18, x0 + 3), (18, x1 - 3), (17, x1), (12.5, x1), (12.5, x0)],
                          loc=(0, 0, z), rot=RX, material=material, verts=30)))
    R.append(smooth(cyl("p_dot_knob", 9, 11, ((x0 + x1) / 2, 0, z + 22), material, verts=20)))
    R.append(smooth(cyl("p_dot_knob2", 9, 10, ((x0 + x1) / 2, -22, z), material, verts=20, rot=(90, 0, 0))))
    R.append(bx("p_dot_riser", x0 - 2, x1 + 2, base_top, z - 16, 25, m.anod))


def iron_ring(R, m, x, z_sight, material, r=11.0, base=None):
    """A hooded front post in a ring (the MP5's)."""
    R.append(smooth(lathe("p_front_ring", [(r - 2, x - 3), (r, x - 3), (r, x + 3), (r - 2, x + 3), (r - 2, x - 3)], loc=(0, 0, z_sight), rot=RX, material=material, verts=24)))
    R.append(bx("p_front_post", x - 1, x + 1, z_sight - r, z_sight, 2.2, material))
    if base is not None:
        R.append(bx("p_front_base", x - 7, x + 7, base, z_sight - r + 1, 12, material))


def drum_sight(R, m, x, z_sight, base, material):
    """The rear sight, just below the line of sight, with two ears to look between."""
    R.append(smooth(cyl("p_rear_drum", 8.5, 20, (x, 0, z_sight - 14), material, verts=20, rot=(90, 0, 0))))
    R.append(bx("p_rear_base", x - 12, x + 12, base, z_sight - 17, 26, material))
    for s in (1, -1):
        R.append(bx(f"p_rear_ear{s}", x - 2, x + 2, z_sight - 16, z_sight + 3, 3.5, material, y=s * 12))


# ============================================================================================ the guns
def new_parts():
    return {"Receiver": [], "Furniture": [], "Magazine": [], "BoltCarrier": [], "ChargingHandle": [], "Trigger": [], "Selector": [], "Optic": []}


def build_l85(m, a3):
    """785 mm bullpup, 518 mm barrel. The magazine behind the grip; the charging handle on the right, reached from the left hand."""
    p = new_parts()
    R, F = p["Receiver"], p["Furniture"]
    body = m.sand if a3 else m.anod
    guard_mat = m.tan if a3 else m.sand
    butt_mat = m.od if a3 else m.tan
    bore, half = -2.0, 19.0
    shift = (-120.0, 0.0, -6.0)
    muzzle, butt = 385.0, -400.0
    top = 20.0
    common(p, m, grip=m.od, guard=m.anod)
    R.append(slab("p_body", [(-392, 18), (32, top), (32, -38), (-20, -40), (-52, -40), (-118, -44), (-118, -62), (-392, -66)], 2 * half, body))
    R.append(bx("p_well", -226, -148, -78, -44, 2 * half - 4, body))
    # the top cover's raised rear, ribs along the side, the pins
    R.append(bx("p_cheek", -392, -232, top - 2, top + 5, 26, m.steel if not a3 else m.sand))
    R.append(bx("p_side_rib", -380, 26, -22, -20, 2 * half + 1.4, m.dark, bevel=0))
    for i, x in enumerate((-340, -280, -70, 10)):
        R.append(smooth(cyl(f"p_pin{i}", 3.8, 2 * half + 1.6, (x, 0, -38 if x < -100 else -32), m.steel, verts=14, rot=(90, 0, 0))))
    for i, x in enumerate((-105, -75, -45, -15, 12)):
        R.append(bx(f"p_cooling{i}", x, x + 18, 3, 9, 2 * half + 0.8, m.dark, bevel=0.2))
    # butt plate and hinged shoulder piece
    R.append(slab("p_butt", [(-392, 20), (-392, -66), (-352, -84), (-362, -88), (-408, -92), (butt, -30), (butt - 6, 14), (-398, 22)], 32, butt_mat, bevel=2.0))
    # handguard and rails
    zc = handguard(R, "p_handguard", 32, 250, top - 2, 22 if not a3 else 21, -24, shift, guard_mat)
    rail(R, "p_top_rail", -188, 30, top, m.anod)
    rail(R, "p_hg_rail", 34, 244, top - 2, guard_mat)
    if not a3:
        for s in (1, -1):
            for k in range(7):
                R.append(bx(f"p_hg_side{s}{k}", 42 + k * 28, 58 + k * 28, zc - 6, zc + 6, 2, m.anod, y=s * 21.5, bevel=0.3))
    else:
        for s in (1, -1):
            for k in range(8):
                R.append(bx(f"p_keymod{s}{k}", 40 + k * 25, 56 + k * 25, zc + 4, zc + 10, 1.4, m.dark, y=s * 20.8, bevel=0.3))
    # foregrip, ahead of the hand (it's folded forward a little)
    gm = m.tan if a3 else m.rubber
    R.append(smooth(lathe("p_foregrip", [(0, -24), (15, -24), (15, -120), (18, -124), (18, -136), (0, -136)], loc=(228, 0, 0), rot=(0, 0, 0), material=gm, verts=20)))
    # barrel, gas, hider
    tube_x = 250
    R.append(tube("p_barrel", 200, muzzle - 40, 8.5, bore, m.anod))
    R.append(smooth(lathe("p_hider_collar", [(0, muzzle - 62), (11, muzzle - 62), (11, muzzle - 42), (0, muzzle - 42)], loc=(0, 0, bore), rot=RX, material=m.anod, verts=20)))
    flash_hider(R, m, muzzle - 44, muzzle, bore, r=9.5)
    if not a3:
        R.append(bx("p_gas_block", tube_x - 14, tube_x + 8, top - 6, top + 22, 16, m.anod))
    else:
        R.append(bx("p_flip_sight", tube_x - 34, tube_x - 8, top + 4, top + 12, 14, m.anod))
    # port, far behind the grip; the charging handle on the right
    action(p, m, (-128, -60, -6, 10), half, bore)
    H = p["ChargingHandle"]
    H.append(slab("p_handle", [(-58, 8), (-46, 10), (-40, 34), (-46, 40), (-54, 36), (-60, 12)], 7, m.anod, y=-half - 5, bevel=1.0))
    selector(p, m, half, x=-290.0, z=-28.0, material=m.anod)
    M = straight_mag(p, m, Vector((-187, 0, -60)), Vector((-168, 0, -198)), 60, 11.5, m.rubber if a3 else m.poly, curve=8.0)
    eye, obj = -150.0, 12.0
    axis = 80.0
    prism_sight(p["Optic"], m, eye, obj, axis, m.grey if not a3 else m.anod, top + 8.0, r=23.0, top_dot=True)
    return p, dict(name="l85a3" if a3 else "l85a2", muzzle=muzzle, bore=bore, shift=shift, port=(-94, -half - 1, 2), handle=(-49, -half - 7, 28), eyepiece=eye, sight=axis,
                   bolt_travel=90.0, handle_travel=85.0, butt=butt, **M)


def ar_body(p, m, body, bore=20.0, up_half=14.5, low_half=13.0, well_x=(44.0, 125.0), mag_x=84.0, big=False):
    """The AR family's upper and lower."""
    R = p["Receiver"]
    k = 1.08 if big else 1.0
    R.append(slab("p_upper", [(-93, -1), (-89, -4), (134, -4), (134, 48), (-93, 48)], 2 * up_half * k, body, bevel=1.6))
    R.append(slab("p_lower", [(-91, -4), (well_x[0], -4), (well_x[0], -41), (-18, -41), (-30, -40), (-92, -40), (-104, -30), (-109, -14), (-109, 36), (-102, 42), (-93, 42)],
                  2 * low_half * k, body, bevel=1.4))
    R.append(slab("p_well", [(well_x[0], -4), (well_x[1], -4), (well_x[1], -58), (well_x[1] + 3, -64), (well_x[0] - 3, -64), (well_x[0], -58)], 2 * (11.5 + 4.5) * k, body, bevel=1.5))
    R.append(smooth(cyl("p_tower", 17.5, 4, (-107, 0, bore), body, verts=28, rot=RX)))
    R.append(smooth(lathe("p_fa", [(0, -84), (8.5, -84), (9, -66), (0, -66)], loc=(0, -up_half * k - 6, 30), rot=RX, material=m.steel, verts=18)))
    R.append(smooth(cyl("p_fa_tube", 7, 34, (-48, -up_half * k - 6, 30), body, verts=20, rot=RX)))
    R.append(bx("p_deflector", 0, 18, 26, 46, 8, body, y=-up_half * k - 2.5))
    R.append(smooth(cyl("p_release", 5, 3, (well_x[0] - 8, -low_half * k - 1, -22), m.steel, verts=16, rot=(90, 0, 0))))
    for i, (x, z) in enumerate(((-86, -12), (well_x[1] - 14, -12), (6, -26), (-24, -26))):
        R.append(smooth(cyl(f"p_pin{i}", 2.6, 2 * low_half * k + 1, (x, 0, z), m.steel, verts=12, rot=(90, 0, 0))))
    rail(R, "p_up_rail", -91, 134, 48, body)
    x = action(p, m, (22, 72, 11, 29), up_half * k, bore)
    H = p["ChargingHandle"]
    H.append(bx("p_handle", -103, -88, 41, 47, 44, body))
    selector(p, m, low_half * k, material=m.steel)
    return x


def tube_stock(p, m, material, butt, bore=20.0):
    """A carbine stock on a buffer tube, its butt at x = butt."""
    R, F = p["Receiver"], p["Furniture"]
    R.append(tube("p_buffer", butt + 40, -108, 15.0, bore, m.anod, verts=28))
    # the body round the tube, the rear post and the diagonal strut: an open triangle between them
    F.append(slab("p_stock", [(butt + 8, bore + 18), (butt + 118, bore + 18), (butt + 122, bore - 18), (butt + 8, bore - 18)], 32, material, bevel=2.0))
    F.append(slab("p_stock_post", [(butt + 8, bore - 18), (butt + 30, bore - 18), (butt + 34, bore - 118), (butt + 8, bore - 118)], 30, material, bevel=1.6))
    F.append(slab("p_stock_strut", [(butt + 96, bore - 18), (butt + 122, bore - 18), (butt + 42, bore - 112), (butt + 30, bore - 96)], 24, material, bevel=1.6))
    F.append(bx("p_butt_pad", butt, butt + 8, bore - 120, bore + 20, 36, m.rubber, bevel=2.0))


def mlok(R, m, x0, x1, zc, half, name="p_slot"):
    k = 0
    x = x0
    while x + 26 < x1:
        for s in (1, -1):
            R.append(bx(f"{name}{s}{k}", x, x + 22, zc - 3, zc + 3, 1.2, m.dark, y=s * (half - 0.2), bevel=0.3))
        x += 34
        k += 1


def build_l403(m):
    """Knight's KS-1: 14.5 in barrel, long free-float rail, low-power scope, a suppressor."""
    p = new_parts()
    R = p["Receiver"]
    body = m.tan
    common(p, m, grip=m.tan, guard=m.tan)
    bore = 20.0
    face = ar_body(p, m, body)
    shift = (10.0, 0.0, 20.0)
    end = face + 368
    muzzle = end + 150
    zc = handguard(R, "p_handguard", 134, 390, 48, 22, -8, shift, body)
    mlok(R, m, 142, 390, zc, 22)
    rail(R, "p_hg_rail", 136, 388, 48, body)
    R.append(tube("p_barrel", 130, end + 8, 8.5, bore, m.anod))
    R.append(smooth(lathe("p_supp_mount", [(0, 390), (13, 390), (13, 402), (0, 402)], loc=(0, 0, bore), rot=RX, material=body, verts=20)))
    suppressor(R, m, end - 48, muzzle - 48, bore, 21.0, m.tan)
    muzzle -= 48
    tube_stock(p, m, m.sand, -391)
    M = straight_mag(p, m, Vector((84, 0, -2)), Vector((102, 0, -178)), 62, 11.5, m.tan, curve=7.0)
    eye, obj = -118.0, 150.0
    axis = 103.0
    tube_scope(p["Optic"], m, eye, obj, axis, body, 56.0, r_tube=15, r_obj=17.5, r_eye=20, turret=20.0)
    return p, dict(name="l403a1", muzzle=muzzle, bore=bore, shift=shift, port=(47, -14.5, bore), handle=(-96, 22, 44), eyepiece=eye, sight=axis,
                   bolt_travel=80.0, handle_travel=76.0, butt=-391.0, **M)


def build_l143(m):
    """SIG MCX: short barrel, slim rail, holographic sight and swung-up magnifier in line, a suppressor."""
    p = new_parts()
    R = p["Receiver"]
    common(p, m, grip=m.stipple, guard=m.anod)
    bore = 20.0
    face = ar_body(p, m, m.anod)
    shift = (-55.0, 0.0, 16.0)
    end = face + 229
    muzzle = end + 118
    zc = handguard(R, "p_handguard", 134, 290, 48, 21, -10, shift, m.anod)
    mlok(R, m, 142, 288, zc + 6, 21)
    rail(R, "p_hg_rail", 136, 288, 48, m.anod)
    R.append(bx("p_hg_panel", 170, 278, zc - 12, zc - 2, 43.5, m.sand, bevel=1.0))
    R.append(tube("p_barrel", 130, end + 8, 8.5, bore, m.anod))
    suppressor(R, m, end - 30, muzzle, bore, 23.0, m.anod)
    tube_stock(p, m, m.sand, -422)
    M = straight_mag(p, m, Vector((84, 0, -2)), Vector((102, 0, -178)), 62, 11.5, m.poly, curve=7.0)
    axis = 87.0
    holo_sight(p["Optic"], m, 8.0, 94.0, axis, m.sand, 56.0)
    # the magnifier behind it
    p["Optic"].append(smooth(lathe("p_magnifier", [(0, -74), (15, -74), (18, -70), (18, -52), (15, -44), (15, -10), (0, -10)], loc=(0, 0, axis), rot=RX, material=m.sand, verts=28)))
    p["Optic"].append(bx("p_mag_mount", -60, -22, 56, axis - 13, 24, m.anod))
    p["Optic"].append(cyl("p_mag_lens", 14, 0.6, (-73.6, 0, axis), m.glass, verts=24, rot=RX))
    return p, dict(name="l143a2", muzzle=muzzle, bore=bore, shift=shift, port=(47, -14.5, bore), handle=(-96, 22, 44), eyepiece=-74.0, sight=axis,
                   bolt_travel=80.0, handle_travel=76.0, butt=-422.0, **M)


def build_l119(m):
    """Colt Canada C8 SFW, the short barrel: a railed handguard, angled foregrip, A-frame front sight, red dot on a riser."""
    p = new_parts()
    R = p["Receiver"]
    common(p, m, grip=m.tan, guard=m.anod)
    bore = 20.0
    face = ar_body(p, m, m.anod)
    shift = (-55.0, 0.0, 18.0)
    end = face + 254
    muzzle = end + 34
    zc = handguard(R, "p_handguard", 150, 288, 44, 20, -8, shift, m.poly)
    for k in range(9):
        R.append(bx(f"p_hg_rib{k}", 158 + k * 14, 165 + k * 14, zc - 8, zc + 12, 41, m.anod, bevel=0.4))
    R.append(smooth(lathe("p_delta", [(0, 134), (21, 134), (21, 142), (17, 150), (0, 150)], loc=(0, 0, bore), rot=RX, material=m.anod, verts=24)))
    rail(R, "p_hg_rail", 152, 286, 44, m.anod)
    R.append(tube("p_barrel", 130, end + 4, 8.5, bore, m.anod))
    R.append(bx("p_gas_block", 292, 306, bore - 13, 34, 24, m.anod))
    # the A-frame post
    R.append(slab("p_a_frame", [(288, 34), (308, 34), (302, 84), (298, 84)], 5, m.anod, bevel=0.4))
    R.append(bx("p_a_hood", 293, 305, 80, 86, 16, m.anod))
    flash_hider(R, m, end, muzzle, bore, r=10.0)
    tube_stock(p, m, m.tan, -380)
    M = straight_mag(p, m, Vector((84, 0, -2)), Vector((102, 0, -178)), 62, 11.5, m.poly, curve=7.0)
    axis = 105.0
    dot_sight(p["Optic"], m, -16.0, 46.0, axis, m.anod, 56.0)
    return p, dict(name="l119a1", muzzle=muzzle, bore=bore, shift=shift, port=(47, -14.5, bore), handle=(-96, 22, 44), eyepiece=-16.0, sight=axis,
                   bolt_travel=80.0, handle_travel=76.0, butt=-380.0, open_sight=True, **M)


def build_l129(m):
    """LMT sharpshooter: 990 mm long, 406 mm barrel, 20-round 7.62 box, ACOG with a mini dot, folded bipod."""
    p = new_parts()
    R = p["Receiver"]
    common(p, m, grip=m.tan, guard=m.tan)
    bore = 20.0
    body = m.tan
    face = ar_body(p, m, body, big=True)
    shift = (25.0, 0.0, 18.0)
    end = face + 406
    muzzle = end + 62
    zc = handguard(R, "p_handguard", 134, 420, 48, 24, -9, shift, body)
    rail(R, "p_hg_rail", 136, 418, 48, m.tan)
    for s in (1, -1):
        for k in range(6):
            R.append(bx(f"p_panel{s}{k}", 190 + k * 34, 216 + k * 34, zc - 10, zc + 8, 2, m.sand, y=s * 23.5, bevel=0.5))
    R.append(tube("p_barrel", 130, end + 4, 9.5, bore, m.anod))
    flash_hider(R, m, end, muzzle, bore, r=10.5)
    # the bipod, folded forward along the barrel
    R.append(bx("p_bipod_clamp", 396, 420, -24, -6, 30, m.anod))
    for s in (1, -1):
        R.append(tube(f"p_bipod_leg{s}", 412, 525, 4.5, -18, m.anod, y=s * 12, verts=12))
    tube_stock(p, m, m.tan, -449)
    M = straight_mag(p, m, Vector((86, 0, -2)), Vector((92, 0, -150)), 78, 13.5, m.poly, curve=1.0)
    eye, obj = -82.0, 72.0
    axis = 100.0
    prism_sight(p["Optic"], m, eye, obj, axis, m.anod, 56.0, r=20.0, top_dot=True)
    return p, dict(name="l129a1", muzzle=muzzle, bore=bore, shift=shift, port=(47, -15.6, bore), handle=(-96, 22, 44), eyepiece=eye, sight=axis,
                   bolt_travel=90.0, handle_travel=85.0, butt=-449.0, **M)


def build_l135(m):
    """Barrett M82A1: 1448 mm long, 737 mm fluted barrel, the chevron brake, 10-round box, a bipod."""
    p = new_parts()
    R, F = p["Receiver"], p["Furniture"]
    common(p, m, grip=m.stipple, guard=m.anod)
    body = m.anod
    bore, half = 22.0, 24.0
    shift = (160.0, 0.0, -4.0)
    muzzle, butt, end = 1022.0, -426.0, 880.0
    top = 55.0
    # upper receiver and barrel jacket, lower with the skeleton butt
    R.append(slab("p_upper", [(-410, top - 8), (-395, top), (582, top), (586, -10), (-150, -10), (-410, 0)], 2 * half - 4, body))
    R.append(slab("p_lower", [(-408, 2), (-150, -8), (-46, -38), (120, -38), (205, -70), (225, -10), (-150, -10), (-300, -10)], 2 * half, m.grey))
    R.append(slab("p_mag_house", [(44, -10), (205, -10), (160, -72), (44, -72)], 2 * half + 2, body))
    # the butt: top strut, rear post and rubber pad, lower strut
    R.append(bx("p_butt_lower", -400, -180, -92, -74, 26, body))
    R.append(slab("p_butt_strut", [(-195, -74), (-178, -74), (-150, -10), (-168, -10)], 24, body))
    R.append(bx("p_butt_post", -412, -384, -100, 0, 26, body))
    F.append(slab("p_butt_pad", [(-406, 20), (butt, 6), (butt, -92), (-420, -112), (-400, -112), (-400, 20)], 40, m.rubber, bevel=3.0))
    F.append(bx("p_cheek", -395, -300, top - 8, top, 40, m.rubber))
    for i, x in enumerate(range(-380, -200, 42)):
        R.append(smooth(cyl(f"p_butt_hole{i}", 4, 28, (x, 0, -83), m.dark, verts=10, rot=(90, 0, 0))))
    # jacket's vents, the rail and the top slot
    for s in (1, -1):
        for k in range(8):
            R.append(smooth(bx(f"p_vent{s}{k}", 305 + k * 34, 324 + k * 34, 28, 40, 1.2, m.dark, y=s * (half - 1.8), bevel=0.4)))
            R.append(bx(f"p_vent_low{s}{k}", 305 + k * 34, 324 + k * 34, 2, 10, 1.2, m.dark, y=s * (half - 1.8), bevel=0.3))
    rail(R, "p_top_rail", -175, 575, top, m.anod, width=25)
    # handguard for the left hand under the jacket
    handguard(R, "p_hand_stock", 340, 600, 0, 22, -24, shift, body)
    # barrel: heavy and fluted; the brake
    R.append(tube("p_barrel", 570, end + 4, 16.0, bore, m.anod))
    for k in range(6):
        a = math.tau * k / 6
        R.append(bx(f"p_flute{k}", 590, end - 10, bore + 15 * math.sin(a) - 1.2, bore + 15 * math.sin(a) + 1.2, 2.6, m.dark, y=15 * math.cos(a), bevel=0))
    R.append(smooth(bx("p_brake", end, muzzle, bore - 22, bore + 22, 56, m.anod, bevel=2.0)))
    for k in range(3):
        x = end + 28 + k * 42
        R.append(bx(f"p_brake_baffle{k}", x, x + 20, bore - 23, bore + 23, 58, m.dark, bevel=0))
    # bipod, deployed
    R.append(bx("p_bipod_clamp", 462, 492, -24, -6, 36, m.anod))
    for s in (1, -1):
        leg = slab(f"p_bipod_leg{s}", [(464, -8), (484, -8), (486, -282), (466, -282)], 7, m.anod, y=s * 24, bevel=0.8)
        R.append(leg)
        R.append(bx(f"p_bipod_foot{s}", 450, 500, -290, -282, 18, m.anod, y=s * 26))
    # the handle, the bolt carrier and selector
    action(p, m, (-48, 120, 16, 42), half - 2, bore, m.steel)
    p["ChargingHandle"].append(slab("p_handle", [(92, 42), (108, 42), (120, 62), (106, 66)], 8, m.steel, y=-half - 2, bevel=0.8))
    selector(p, m, half, x=-38.0, z=-20.0, material=m.steel)
    M = straight_mag(p, m, Vector((122, 0, -28)), Vector((118, 0, -178)), 118, 22.0, m.anod, curve=0.0, round_mat=m.brass)
    eye, obj = -180.0, 180.0
    axis = 125.0
    tube_scope(p["Optic"], m, eye, obj, axis, m.anod, top + 8, r_tube=17, r_obj=30, r_eye=21, turret=-10.0)
    return p, dict(name="l135a1", muzzle=muzzle, bore=bore, shift=shift, port=(36, -half + 1, 29), handle=(100, -half - 4, 54), eyepiece=eye, sight=axis,
                   bolt_travel=110.0, handle_travel=95.0, butt=butt, **M)


def mp5_body(p, m, front, bore=8.0):
    """The MP5's stamped receiver from the rear cap to x = front, with its cocking tube and fire-control housing."""
    R = p["Receiver"]
    half = 15.5
    R.append(slab("p_receiver", [(-108, 8), (-90, 36), (front, 36), (front, -14), (-108, -14)], 2 * half, m.anod))
    R.append(bx("p_end_cap", -122, -106, -14, 30, 2 * half + 2, m.poly, bevel=1.4))
    R.append(tube("p_cock_tube", -40, front + 4, 9.0, 30, m.anod))
    R.append(bx("p_rib", -100, front, 3, 5, 2 * half + 0.8, m.dark, bevel=0))
    # the trigger housing, as the AK's lower, with its push pins
    R.append(slab("p_housing", [(-112, -14), (46, -14), (46, -41), (-18, -41), (-30, -40), (-92, -42), (-108, -30)], 2 * half - 2, m.poly, bevel=1.2))
    for i, x in enumerate((-96, 34)):
        R.append(smooth(cyl(f"p_pin{i}", 3, 2 * half, (x, 0, -24 if i == 0 else -8), m.steel, verts=12, rot=(90, 0, 0))))
    # magazine well and release paddle
    R.append(slab("p_well", [(46, -14), (92, -14), (92, -38), (46, -38)], 26, m.anod, bevel=1.0))
    R.append(bx("p_paddle", 40, 50, -50, -40, 12, m.steel))
    selector(p, m, half - 1, material=m.steel)
    action(p, m, (-6, 58, 4, 22), half, bore)
    return half


def mp5_handle(p, m, x, half):
    H = p["ChargingHandle"]
    H.append(smooth(cyl("p_handle_arm", 3.6, 24, (x, half + 2, 30), m.steel, verts=12, rot=(90, 0, 0))))
    H.append(smooth(lathe("p_handle_knob", [(0, -9), (7.5, -7), (8.5, 0), (7.5, 7), (0, 9)], loc=(x, half + 15, 30), rot=(90, 0, 0), material=m.anod, verts=18)))


def build_l92(m):
    """MP5SD3: 805 mm with the stock out, a ported 146 mm barrel in the integral suppressor, 30-round magazine."""
    p = new_parts()
    R, F = p["Receiver"], p["Furniture"]
    common(p, m, grip=m.poly, guard=m.poly)
    bore = 8.0
    half = mp5_body(p, m, 118.0, bore)
    shift = (-62.0, 0.0, 10.0)
    muzzle, butt = 456.0, -349.0
    # the ribbed forward shroud (under the left hand) and the suppressor tube
    R.append(smooth(lathe("p_shroud", [(0, 112), (24, 112), (24, 280), (0, 280)], loc=(0, 0, bore), rot=RX, material=m.poly, verts=32)))
    for k in range(22):
        R.append(smooth(lathe(f"p_shroud_rib{k}", [(23.5, 120 + k * 7), (24.8, 121 + k * 7), (24.8, 124 + k * 7), (23.5, 125 + k * 7), (23.5, 120 + k * 7)],
                              loc=(0, 0, bore), rot=RX, material=m.poly, verts=24)))
    R.append(smooth(lathe("p_supp", [(0, 280), (21, 280), (21, muzzle - 6), (18, muzzle), (0, muzzle)], loc=(0, 0, bore), rot=RX, material=m.anod, verts=32)))
    z_sight = 56.0
    R.append(tube("p_cock_tube_long", 118, 304, 9.0, 30, m.anod))
    iron_ring(R, m, 296.0, z_sight, m.anod, base=34.0)
    drum_sight(R, m, -58.0, z_sight, 34.0, m.anod)
    # the retractable stock: two bars and a shaped butt
    for s in (1, -1):
        R.append(tube(f"p_stock_bar{s}", butt + 24, -118, 5.0, 8.0, m.anod, y=s * 13, verts=14))
    F.append(slab("p_stock_butt", [(butt + 28, 22), (butt + 4, 26), (butt, 12), (butt + 10, -46), (butt + 4, -92), (butt + 16, -98), (butt + 30, -76), (butt + 36, 10)], 34, m.rubber, bevel=2.0))
    mp5_handle(p, m, 220.0, half)
    M = straight_mag(p, m, Vector((70, 0, -20)), Vector((88, 0, -205)), 32, 11.5, m.anod, curve=6.0, ribs=False)
    return p, dict(name="l92a1", muzzle=muzzle, bore=bore, shift=shift, port=(26, -half, 13), handle=(220.0, half + 15, 30), eyepiece=-58.0, sight=z_sight,
                   bolt_travel=70.0, handle_travel=85.0, butt=butt, open_sight=True, **M)


def build_l80(m):
    """MP5K: a very short 115 mm barrel, a vertical foregrip, a curved 30-round magazine, a folding brace."""
    p = new_parts()
    R, F = p["Receiver"], p["Furniture"]
    common(p, m, grip=m.poly, guard=m.poly)
    bore = 8.0
    half = mp5_body(p, m, 92.0, bore)
    shift = (-132.0, 0.0, -8.0)
    muzzle, butt = 200.0, -146.0
    # the front: polymer stub round the barrel, foregrip under it
    zc = handguard(R, "p_front", 90.0, 172.0, 22, 20, -24, shift, m.poly)
    R.append(tube("p_barrel", 88, 186, 8.0, bore, m.anod))
    R.append(smooth(lathe("p_muzzle", [(0, 180), (11, 180), (11, muzzle - 2), (9, muzzle), (0, muzzle)], loc=(0, 0, bore), rot=RX, material=m.anod, verts=18)))
    R.append(cyl("p_muzzle_bore", 4.8, 0.8, (muzzle + 0.1, 0, bore), m.dark, verts=14, rot=RX))
    z_sight = 56.0
    R.append(tube("p_cock_tube_long", 92, 176, 9.0, 30, m.anod))
    iron_ring(R, m, 170.0, z_sight, m.anod, base=34.0)
    drum_sight(R, m, -62.0, z_sight, 34.0, m.anod)
    R.append(smooth(lathe("p_foregrip", [(0, 0), (14, 0), (14, -55), (17, -60), (17, -86), (14, -90), (0, -90)], loc=(142, 0, -24), rot=(0, 0, 0), material=m.poly, verts=22)))
    # the folded brace behind the end cap
    F.append(slab("p_brace", [(butt + 8, 30), (butt, 26), (butt, -62), (butt + 10, -72), (butt + 22, -60), (butt + 22, 26)], 34, m.poly, bevel=2.0))
    R.append(bx("p_brace_hinge", -124, butt + 20, -6, 8, 24, m.anod))
    mp5_handle(p, m, 158.0, half)
    M = straight_mag(p, m, Vector((68, 0, -20)), Vector((128, 0, -176)), 32, 11.5, m.anod, curve=22.0, ribs=False)
    return p, dict(name="l80a1", muzzle=muzzle, bore=bore, shift=shift, port=(26, -half, 13), handle=(158.0, half + 15, 30), eyepiece=-62.0, sight=z_sight,
                   bolt_travel=60.0, handle_travel=70.0, butt=butt, open_sight=True, **M)


GUNS = {
    "l85a2": lambda m: build_l85(m, False),
    "l85a3": lambda m: build_l85(m, True),
    "l403a1": build_l403,
    "l143a2": build_l143,
    "l119a1": build_l119,
    "l129a1": build_l129,
    "l135a1": build_l135,
    "l92a1": build_l92,
    "l80a1": build_l80,
}


# ============================================================================================ output
def save_points(d):
    top, bot = d["MagTop"], d["MagBot"]
    axis = (bot - top).normalized()
    mid = (top + bot) / 2
    front = top + Vector((d["MagDepth"] / 2, 0, -2))
    data = {
        "Muzzle": [round(d["muzzle"] * MM, 5), 0.0, round(d["bore"] * MM, 5)],
        "EjectionPort": [round(c * MM, 5) for c in d["port"]],
        "ChargingHandle": [round(c * MM, 5) for c in d["handle"]],
        "SightZ": round(d["sight"] * MM, 5),
        "EyepieceX": round(d["eyepiece"] * MM, 5),
        "MagPivot": [round(c * MM, 5) for c in front],
        "MagAxis": [round(c, 5) for c in axis],
        "MagCenter": [round(c * MM, 5) for c in mid],
        "MagGrab": [round(c * MM, 5) for c in top.lerp(bot, 0.55)],
        "MagHalfExtents": [round(d["MagDepth"] / 2 * MM, 5), round((bot - top).length / 2 * MM, 5), round(d["MagHalf"] * MM, 5)],
        "MagLean": round(math.atan2(axis.x, -axis.z), 5),
        "LeftHandShift": [round(c * MM, 5) for c in d["shift"]],
        "BoltTravel": round(d["bolt_travel"] * MM, 5),
        "HandleTravel": round(d["handle_travel"] * MM, 5),
        "Butt": round(d["butt"] * MM, 5),
        "Note": "Blender coordinates in metres: +X muzzle, +Y left, +Z up; x = 0 at the trigger; z = 0 the AK's bore.",
    }
    path = os.path.join(K.OUT, d["name"] + "_points.json")
    with open(path, "w") as f:
        json.dump(data, f, indent=2)


def look_at(cam, target):
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()


def preview(name, d):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in [i.identifier for i in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'CYCLES'
    if scene.render.engine == 'CYCLES':
        scene.cycles.samples = 16
    scene.render.image_settings.file_format = 'PNG'
    world = bpy.data.worlds.new("prev")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes.get("Background").inputs[0].default_value = (0.62, 0.64, 0.68, 1)
    world.node_tree.nodes.get("Background").inputs[1].default_value = 0.5
    sun = bpy.data.lights.new("sun", 'SUN')
    sun.energy = 2.6
    so = bpy.data.objects.new("sun", sun)
    scene.collection.objects.link(so)
    so.rotation_euler = (math.radians(55), math.radians(-20), math.radians(10))
    cam_data = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam_data.type = 'ORTHO'
    mid = (d["muzzle"] + d["butt"]) / 2 * MM
    cam_data.ortho_scale = (d["muzzle"] - d["butt"]) * MM * 1.1
    cam.location = (mid, -2.0, -0.02)
    look_at(cam, (mid, 0, -0.02))
    scene.render.resolution_x, scene.render.resolution_y = 1000, 500
    os.makedirs(K.PREVIEWS, exist_ok=True)
    scene.render.filepath = os.path.join(K.PREVIEWS, f"{name}_right.png")
    bpy.ops.render.render(write_still=True)
    for o in (so, cam):
        bpy.data.objects.remove(o)


def make(name, render):
    K.reset()
    parts, d = GUNS[name](Mats())
    S = Matrix.Scale(MM, 4)
    bpy.context.view_layer.update()
    for o in list(bpy.data.objects):
        o.matrix_world = S @ o.matrix_world
        if o.get("uv") in ("cyl", "ring"):
            o["uv"] = "box"
    final = []
    for pname, objs in parts.items():
        if not objs:
            continue
        out = K.finish(objs, join_name=pname, smooth_angle=40.0)[0]
        bpy.ops.object.select_all(action='DESELECT')
        out.select_set(True)
        bpy.context.view_layer.objects.active = out
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        bm = bmesh.new()
        bm.from_mesh(out.data)
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
        bm.to_mesh(out.data)
        bm.free()
        final.append(out)
    tris = sum(len(p.vertices) - 2 for o in final for p in o.data.polygons)
    print(f"[{name}] {len(final)} objects, ~{tris} triangles", flush=True)
    save_points(d)
    K.export(name, final)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.PROJECT, "Assets", name + ".blend"))
    if render:
        preview(name, d)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    render = "nopreview" not in argv
    chosen = [a for a in argv if a in GUNS] or list(GUNS)
    for gun in chosen:
        make(gun, render)
