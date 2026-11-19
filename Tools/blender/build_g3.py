"""
Builds a Heckler & Koch G3 battle rifle (7.62 x 51 mm, 20-round steel magazine; stamped receiver with the cocking tube
over the barrel, slim green handguard, fixed green stock, drum (diopter) rear sight, hooded front post, slotted flash
hider; an ACOG 4x32 prism sight on an HK claw mount) and exports it as Assets/Models/g3.glb. The scene is saved as
Assets/g3.blend (the source), key points for the game as Assets/Models/g3_points.json, and preview renders (in the AK's
hands) as Tools/previews/models/g3_*.png.

    blender -b --factory-startup -P Tools/blender/build_g3.py              (build, export, previews)
    blender -b --factory-startup -P Tools/blender/build_g3.py -- nopreview (build and export only)

Coordinates (Blender, like the AK-47, the UMP40 and the AR-15): +X out of the muzzle, +Z up, +Y the gun's left side;
x = 0 is the trigger. z = 0 is the AK's bore, so the pistol grip, trigger and guard can be the AK's exactly and the
hands fit; the G3's own bore sits BORE_Z above it. The side outline is traced from a reference photo (right side) at
PX_MM millimetres per pixel (px() below): 1025 mm long. Modelled in millimetres and scaled to metres at the end.

Exported objects (names are the part names the game can animate, see Game/Rifle.cs):
    Receiver        receiver, cocking tube, front sight, rear sight drum, claw mount, rail and ACOG, barrel, flash hider,
                    trigger housing,
                    trigger guard, magazine well
    Furniture       the green pistol grip, handguard and stock
    Magazine        the 20-round steel magazine (pulled straight down out of the well, along MagAxis)
    BoltCarrier     the bolt seen through the ejection port (cycles back by BoltTravel with each shot)
    ChargingHandle  the cocking handle on the left of the tube over the handguard (moves only when racked, by HandleTravel)
    Trigger         pivots about TriggerPivot
    Selector        the selector lever (left)
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
RX = (0, 90, 0)                      # turns a local-Z cylinder or lathe onto +X (the bore)

# The reference photo: the trigger at pixel x 630, the bore at pixel y 151.
PX_MM = 0.68
BORE_Z = 10.0


def px(x, y):
    """A point traced off the side reference (pixels, y down) -> model (x, z) in mm."""
    return ((x - 630) * PX_MM, (151 - y) * PX_MM + BORE_Z)


# Key dimensions (mm)
MUZZLE_X = px(1560, 0)[0]
BUTT_X = px(53, 0)[0]
REC_X0, REC_X1 = px(415, 0)[0], px(918, 0)[0]        # the receiver: rear (in the stock's end cap) / front
REC_BOT, REC_TOP = -8.0, 46.0
REC_HALF = 16.0
SIGHT_Z = 58.0                                       # the line of sight: the drum's aperture and the front post's tip
DRUM_X = px(525, 0)[0]
FS_X0, FS_X1 = px(1324, 0)[0], px(1376, 0)[0]        # the front sight block
FS_X = (FS_X0 + FS_X1) / 2
TUBE_Z, TUBE_R = 37.0, 8.5                           # the cocking tube over the barrel
HG_X0, HG_X1 = REC_X1, FS_X0 - 4                     # the handguard
HG_TOP, HG_BOT, HG_HALF = 32.0, -11.0, 21.0
GRIP_A, GRIP_B = Vector((-46, 0, -30)), Vector((-77, 0, -138))   # the AK-47's grip line
# The AK's left hand cups its lower handguard; moved by LEFT_HAND_SHIFT it cups this one (whose lower half below the
# hand is the AK's section, moved the same way).
LEFT_HAND_SHIFT = (50.0, 0.0, 12.0)
WELL_X0, WELL_X1, WELL_BOT = 58.0, 152.0, -46.0      # the magazine well (outside)
MAG_X = 112.0                                        # the magazine's centre line (front to back) at the top
MAG_TOP = Vector((MAG_X, 0, -4.0))
MAG_KNEE = Vector((MAG_X + 1.0, 0, WELL_BOT))        # straight down to here (in the well), then a slight forward curve
MAG_BOT = Vector((MAG_X + 6.0, 0, -150.0))
MAG_DEPTH, MAG_HALF = 70.0, 13.5                     # front-to-back, half thickness
PORT = (px(720, 0)[0], px(825, 0)[0], 21.0, 36.0)    # ejection port (right): x0, x1, z0, z1
HANDLE_X = px(1200, 0)[0]                            # the cocking handle, forward (closed)
BOLT_TRAVEL, HANDLE_TRAVEL = 80.0, 95.0
# The ACOG (a 4x32 prism sight) on an HK claw mount: a bridge clamped into the receiver's two claw recesses, over the
# drum, with a Picatinny rail on top.
CLAW_X = (px(480, 0)[0], px(860, 0)[0])
BRIDGE_X0, BRIDGE_X1 = CLAW_X[0] - 10, CLAW_X[1] + 10
BRIDGE_Z0, BRIDGE_Z1 = 72.0, 78.0                    # clears the drum's ears (up to SIGHT_Z + 10)
RAIL_TOP = BRIDGE_Z1 + 6.0
ACOG_Z = RAIL_TOP + 30.0                             # the scope's axis: the line of sight, aimed
ACOG_X0, ACOG_LEN = -80.0, 150.0                     # the eyepiece's end, and its length to the objective's


# ============================================================================================ helpers
def smooth(o, angle=40.0):
    o["smooth"] = True
    o["smooth_angle"] = angle
    return o


def boolean(target, cutter, op='DIFFERENCE'):
    mod = target.modifiers.new("bool_" + cutter.name, 'BOOLEAN')
    mod.object = cutter
    mod.operation = op
    mod.solver = 'EXACT'
    cutter.hide_render = True
    cutter.display_type = 'WIRE'
    cutter["cutter"] = True
    return mod


# Cutters carry the recess material: the faces a boolean cuts take the cutter's material.
def cutter(name, size, loc, rot=(0, 0, 0)):
    return box("cut_" + name, size, loc, mat("flat_recess", (0.02, 0.02, 0.02), 0.8, 0.0), rot=rot)


def cyl_cutter(name, radius, depth, loc, rot=(0, 0, 0), verts=16):
    return cyl("cut_" + name, radius, depth, loc, mat("flat_recess", (0.02, 0.02, 0.02), 0.8, 0.0), verts=verts, rot=rot)


def se_ring(a, b_top, b_bot=None, n=2.5, count=32):
    """Superellipse section [(p, q)], starting at the bottom (the UV seam), counter-clockwise."""
    b_bot = b_top if b_bot is None else b_bot
    pts = []
    for i in range(count):
        t = -math.pi / 2 + math.tau * i / count
        c, s = math.cos(t), math.sin(t)
        p = math.copysign(abs(c) ** (2.0 / n), c) * a
        q = math.copysign(abs(s) ** (2.0 / n), s) * (b_top if s > 0 else b_bot)
        pts.append((p, q))
    return pts


def se_half_width(a, b, n, q):
    """The half width of a superellipse section (half width a, half height b) at height q from its centre."""
    return a * max(0.0, 1.0 - abs(q / b) ** n) ** (1.0 / n)


def loft(name, rings, material, caps=(True, True), angle=50.0):
    """Skins rings of 3D points (equal counts); UVs come from modelkit's box projection."""
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in ring] for ring in rings]
    n = len(rings[0])
    for j in range(len(vs) - 1):
        for i in range(n):
            k = (i + 1) % n
            bm.faces.new((vs[j][i], vs[j][k], vs[j + 1][k], vs[j + 1][i]))
    if caps[0]:
        bm.faces.new(vs[0])
    if caps[1]:
        bm.faces.new(list(reversed(vs[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return smooth(K._new_obj(name, bm, material), angle)


def line(a, b):
    def f(t):
        return a.lerp(b, t), (b - a).normalized()
    return f


def bezier(a, b, c):
    """A quadratic Bezier path (point, tangent) in the XZ plane."""
    def f(t):
        p = a * (1 - t) ** 2 + b * (2 * t * (1 - t)) + c * t * t
        d = (b - a) * (2 * (1 - t)) + (c - b) * (2 * t)
        return p, d.normalized()
    return f


def sweep(path, ts, section):
    """Rings along a path in the XZ plane. section(t) -> [(p, q)], p along +Y, q along the in-plane normal."""
    rings = []
    side = Vector((0, 1, 0))
    for t in ts:
        c, tan = path(t)
        nrm = side.cross(tan).normalized()
        rings.append([c + side * p + nrm * q for p, q in section(t)])
    return rings


def lerp_table(table, x):
    """Piecewise-linear lookup in [(x, value), ...] sorted by x."""
    if x <= table[0][0]:
        return table[0][1]
    for (x0, v0), (x1, v1) in zip(table, table[1:]):
        if x <= x1:
            return v0 + (v1 - v0) * (x - x0) / (x1 - x0)
    return table[-1][1]


def slab(name, pts, width, material, y=0.0, bevel=0.8):
    """A side outline (x, z) extruded across the gun (Y), centred on y."""
    o = prism(name, pts, width, (0, y, 0), material, axis='Y')
    return add_bevel(o, bevel) if bevel else o


# ============================================================================================ parts
def build():
    steel = mat("gunmetal", None, 0.4, 0.8)
    poly = mat("polymer", None, 0.6, 0.0)
    green = mat("flat_furniture_green", (0.25, 0.27, 0.17), 0.55, 0.0)
    dark = mat("flat_recess", (0.02, 0.02, 0.02), 0.8, 0.0)
    brass = mat("flat_brass", (0.72, 0.55, 0.25), 0.35, 1.0)
    white = mat("flat_marking", (0.75, 0.75, 0.72), 0.6, 0.0)
    parts = {"Receiver": [], "Furniture": [], "Magazine": [], "BoltCarrier": [], "ChargingHandle": [], "Trigger": [], "Selector": [], "Optic": []}
    R, F, M = parts["Receiver"], parts["Furniture"], parts["Magazine"]

    # ---------------------------------------------------------------- receiver: a stamped box with a rounded top
    rzc, rb = (REC_TOP + REC_BOT) / 2, (REC_TOP - REC_BOT) / 2
    rec_xs = [REC_X0, REC_X0 + 2] + list(range(int(REC_X0) + 10, int(REC_X1) - 4, 12)) + [REC_X1 - 2, REC_X1]
    rec = loft("p_receiver", [[Vector((x, p, rzc + q)) for p, q in se_ring(REC_HALF, rb, n=3.6, count=40)] for x in rec_xs], steel, angle=40)
    # ejection port (right), and the long stamped flute along both sides
    boolean(rec, cutter("port", (PORT[1] - PORT[0], 14, PORT[3] - PORT[2]), ((PORT[0] + PORT[1]) / 2, -REC_HALF, (PORT[2] + PORT[3]) / 2)))
    for s in (1, -1):
        fx0, fx1 = px(440, 0)[0], px(830, 0)[0]
        boolean(rec, cutter(f"flute{s}", (fx1 - fx0, 4, 4.5), ((fx0 + fx1) / 2, s * REC_HALF, px(0, 149)[1])))
    R.append(rec)
    R.append(add_bevel(box("p_port_shadow", (PORT[1] - PORT[0] + 4, 4, PORT[3] - PORT[2] + 4),
                           ((PORT[0] + PORT[1]) / 2, -REC_HALF + 9, (PORT[2] + PORT[3]) / 2), dark), 0.3))
    # the receiver's front end round the tube and barrel, and the welded seam
    R.append(smooth(add_bevel(box("p_rec_front", (6, 2 * REC_HALF + 1.2, REC_TOP - REC_BOT - 4), (REC_X1 - 3, 0, rzc), steel), 1.2)))
    # claw-mount recesses on top
    for x in CLAW_X:
        R.append(box(f"p_claw_slot{int(x)}", (9, 2 * REC_HALF - 10, 0.6), (x, 0, REC_TOP + 0.05), dark))

    # ---------------------------------------------------------------- cocking tube, front sight, barrel, flash hider
    R.append(smooth(lathe("p_tube", [(0, REC_X1 - 20), (TUBE_R, REC_X1 - 20), (TUBE_R, FS_X0 + 6), (0, FS_X0 + 6)],
                          loc=(0, 0, TUBE_Z), rot=RX, material=steel, verts=28)))
    # the handle's slot along the tube's left side
    R.append(box("p_handle_slot", (HANDLE_TRAVEL + 10, 1.0, 4.0), (HANDLE_X - HANDLE_TRAVEL / 2 + 2, TUBE_R - 0.3, TUBE_Z), dark))
    fs = slab("p_front_sight_block", [(FS_X0, BORE_Z - 11), (FS_X1, BORE_Z - 11), (FS_X1, TUBE_Z + 6), (FS_X1 - 8, 50),
                                      (FS_X0 + 8, 50), (FS_X0, TUBE_Z + 6)], 22, steel, bevel=0)
    R.append(smooth(add_bevel(fs, 1.8), 35))
    hood_z = SIGHT_Z + 2.0
    hood = lathe("p_front_hood", [(8.6, FS_X - 3.5), (11.2, FS_X - 3.5), (11.2, FS_X + 3.5), (8.6, FS_X + 3.5), (8.6, FS_X - 3.5)],
                 loc=(0, 0, hood_z), rot=RX, material=steel, verts=32)
    R.append(smooth(hood))
    R.append(add_bevel(box("p_front_post", (3.0, 2.0, SIGHT_Z - 48.0), (FS_X, 0, (SIGHT_Z + 48.0) / 2), steel), 0.3))
    R.append(smooth(cyl("p_fs_pin", 2.0, 23, (FS_X - 12, 0, BORE_Z + 4), steel, verts=12, rot=(90, 0, 0))))
    # sling hook under the front sight
    R.append(smooth(add_bevel(slab("p_sling_hook", [(FS_X0 + 6, BORE_Z - 11), (FS_X0 + 18, BORE_Z - 11), (FS_X0 + 14, BORE_Z - 20),
                                                    (FS_X0 + 8, BORE_Z - 20)], 6, steel, bevel=0), 0.8)))
    collar_x = px(1398, 0)[0]
    fh0 = px(1480, 0)[0]
    R.append(smooth(lathe("p_barrel", [(0, REC_X1 - 10), (10.5, REC_X1 - 10), (10.5, FS_X1), (10.0, FS_X1 + 1), (10.0, collar_x),
                                       (7.5, collar_x + 3), (7.5, fh0 + 4), (0, fh0 + 4)],
                          loc=(0, 0, BORE_Z), rot=RX, material=steel, verts=28)))
    fh = lathe("p_flash_hider", [(4.2, fh0), (10.5, fh0), (10.5, fh0 + 8), (9.6, fh0 + 10), (9.6, fh0 + 12), (10.5, fh0 + 14),
                                 (10.5, MUZZLE_X - 2), (9.8, MUZZLE_X), (4.2, MUZZLE_X), (4.2, fh0)],
               loc=(0, 0, BORE_Z), rot=RX, material=steel, verts=32)
    for k, a in enumerate((45, 135)):           # four slots round the front
        boolean(fh, cutter(f"fh_slot{k}", (MUZZLE_X - fh0 - 18, 40, 3.2), ((MUZZLE_X + fh0 + 18) / 2 + 1, 0, BORE_Z), rot=(a, 0, 0)))
    R.append(smooth(fh, 40))

    # ---------------------------------------------------------------- rear sight: the diopter drum between two ears
    R.append(add_bevel(box("p_rs_base", (40, 26, 5), (DRUM_X, 0, REC_TOP + 2.0), steel), 1.0))
    for s in (1, -1):
        ear = slab(f"p_rs_ear{s}", [(DRUM_X - 18, REC_TOP), (DRUM_X + 18, REC_TOP), (DRUM_X + 14, SIGHT_Z + 4), (DRUM_X + 6, SIGHT_Z + 10),
                                    (DRUM_X - 10, SIGHT_Z + 10), (DRUM_X - 16, SIGHT_Z + 3)], 2.6, steel, y=s * 12.0, bevel=0)
        R.append(smooth(add_bevel(ear, 0.8), 35))
    drum = cyl("p_rs_drum", 10.0, 19.0, (DRUM_X, 0, SIGHT_Z), steel, verts=28, rot=(90, 0, 0))
    boolean(drum, cyl_cutter("rs_aperture", 3.0, 40, (DRUM_X, 0, SIGHT_Z), rot=RX, verts=20))
    R.append(smooth(drum, 40))
    for k in range(4):                          # the other apertures / notches round the drum
        a = math.radians(90 * k + 45)
        R.append(box(f"p_rs_mark{k}", (2.0, 19.4, 2.0), (DRUM_X + 10.0 * math.cos(a), 0, SIGHT_Z + 10.0 * math.sin(a)), dark, rot=(0, -90 * k - 45, 0)))
    R.append(smooth(cyl("p_rs_knob", 4.5, 3.0, (DRUM_X + 4, -14.5, REC_TOP + 4), steel, verts=16, rot=(90, 0, 0))))

    # ---------------------------------------------------------------- claw mount: two claws, the bridge, the rail
    anod = mat("flat_anodized", (0.045, 0.045, 0.048), 0.42, 0.35)
    rubber = mat("flat_rubber", (0.035, 0.035, 0.035), 0.85, 0.0)
    glass = mat("flat_lens", (0.05, 0.08, 0.12), 0.08, 0.6)
    fiber = mat("emit_fiber", (0.80, 0.10, 0.04), emit=(0.80, 0.10, 0.04))
    for i, cx in enumerate(CLAW_X):
        # an arch over the receiver's top, its legs hooked down its sides, up to the bridge
        claw = prism(f"p_claw{i}", [(-19.5, 33), (-16.6, 33), (-16.6, REC_TOP + 2), (16.6, REC_TOP + 2), (16.6, 33), (19.5, 33),
                                    (19.5, 56), (13, BRIDGE_Z0 + 1), (-13, BRIDGE_Z0 + 1), (-19.5, 56)], 16, (cx, 0, 0), steel, axis='X')
        R.append(smooth(add_bevel(claw, 1.0), 35))
        # the locking lever on the left, folded forward along the receiver
        R.append(add_bevel(box(f"p_claw_lever{i}", (30, 3.2, 7), (cx + 12, 21.0, 46), steel, rot=(0, 6, 0)), 0.8))
        R.append(smooth(cyl(f"p_claw_pivot{i}", 4.0, 4.0, (cx, 21.0, 46), steel, verts=14, rot=(90, 0, 0))))
    R.append(smooth(add_bevel(box("p_bridge", (BRIDGE_X1 - BRIDGE_X0, 24, BRIDGE_Z1 - BRIDGE_Z0),
                                  ((BRIDGE_X0 + BRIDGE_X1) / 2, 0, (BRIDGE_Z0 + BRIDGE_Z1) / 2), steel), 1.4), 35))
    # The Picatinny rail: a dovetail strip, and the cross teeth on it (10 mm apart).
    R.append(smooth(prism("p_rail", [(-8, 0), (8, 0), (8, 1.5), (10.6, 3.2), (10.6, 3.6), (-10.6, 3.6), (-10.6, 3.2), (-8, 1.5)],
                          BRIDGE_X1 - BRIDGE_X0 - 4, ((BRIDGE_X0 + BRIDGE_X1) / 2, 0, BRIDGE_Z1), anod, axis='X'), 30))
    x = BRIDGE_X0 + 6
    while x < BRIDGE_X1 - 6:
        R.append(prism(f"p_rail_tooth{int(x)}", [(-10.6, 3.6), (10.6, 3.6), (10.6, 4.0), (8.2, 6.0), (-8.2, 6.0), (-10.6, 4.0)],
                       5.0, (x, 0, BRIDGE_Z1), anod, axis='X'))
        x += 10.0

    # ---------------------------------------------------------------- ACOG: a 4x32 prism sight on its rail clamp
    O = parts["Optic"]
    E = ACOG_X0
    # The body, eyepiece to objective: the round ocular tube, the squarer prism housing (its top flat for the fiber),
    # the objective bell. (x from the eyepiece end, half width, half height above / below the axis, squareness)
    acog_tab = [(6, 16.5, 16.5, 16.5, 2.0), (30, 16.5, 16.5, 16.5, 2.0), (35, 18.2, 18.8, 17.4, 2.5), (41, 20.5, 22.0, 18.5, 3.6),
                (100, 20.5, 22.5, 18.5, 3.6), (110, 21.5, 23.6, 20.5, 3.0), (122, 23.0, 24.5, 22.5, 2.5),
                (ACOG_LEN + 8, 23.5, 25.0, 23.0, 2.4)]
    body = loft("p_acog", [[Vector((E + x, p, ACOG_Z + q)) for p, q in se_ring(a, bt, bb, n=n, count=40)]
                           for x, a, bt, bb, n in acog_tab], anod, angle=40)
    # the objective's hood: its face slants back from the top, and the lens sits deep in it
    boolean(body, cutter("acog_hood", (40, 80, 120), (E + ACOG_LEN + 2 + 20 * 0.970, 0, ACOG_Z - 20 * 0.242), rot=(0, 14, 0)))
    boolean(body, cyl_cutter("acog_objective", 18.0, 16, (E + ACOG_LEN + 2, 0, ACOG_Z), rot=RX, verts=36))
    boolean(body, cyl_cutter("acog_ocular", 11.5, 10, (E + 6, 0, ACOG_Z), rot=RX, verts=32))
    O.append(smooth(body, 40))
    O.append(cyl("p_acog_objective_lens", 18.1, 0.6, (E + ACOG_LEN - 5.5, 0, ACOG_Z), glass, verts=36, rot=RX))
    O.append(cyl("p_acog_ocular_lens", 11.6, 0.6, (E + 3.5, 0, ACOG_Z), glass, verts=32, rot=RX))
    # the rubber eye guard round the eyepiece
    guard = cyl("p_acog_guard", 17.6, 8, (E + 4, 0, ACOG_Z), rubber, verts=36, rot=RX)
    boolean(guard, cyl_cutter("acog_guard_hole", 12.4, 12, (E + 4, 0, ACOG_Z), rot=RX, verts=32))
    O.append(smooth(add_bevel(guard, 0.8), 40))
    # the turrets under their caps: elevation on top, windage on the right
    turret = [(0, 0), (9.5, 0), (9.5, 8), (10.6, 8.6), (10.6, 15), (9.8, 16), (0, 16)]
    O.append(smooth(lathe("p_acog_elevation", turret, loc=(E + 62, 0, ACOG_Z + 19), material=anod, verts=36), 35))
    O.append(smooth(lathe("p_acog_windage", turret, loc=(E + 50, -17, ACOG_Z), rot=(90, 0, 0), material=anod, verts=36), 35))
    # the fiber-optic channel along the top, forward of the elevation turret: the red light-gatherer between two ribs
    O.append(add_bevel(box("p_acog_fiber_bed", (44, 10, 3.0), (E + 98, 0, ACOG_Z + 23.2), anod), 0.6))
    for s in (1, -1):
        O.append(add_bevel(box(f"p_acog_fiber_rib{s}", (44, 1.6, 1.4), (E + 98, s * 2.9, ACOG_Z + 25.2), anod), 0.3))
    O.append(box("p_acog_fiber", (42, 2.6, 1.2), (E + 98, 0, ACOG_Z + 24.9), fiber))
    # The rail clamp under it: a block from the rail up into the body, its jaws hooked under the rail's edges, the cross
    # bolts' heads on the right and their thumb nuts on the left.
    base_x0, base_x1 = E + 30, E + 128
    O.append(smooth(add_bevel(box("p_acog_base", (base_x1 - base_x0, 30, ACOG_Z - 15 - RAIL_TOP),
                                  ((base_x0 + base_x1) / 2, 0, (ACOG_Z - 15 + RAIL_TOP) / 2), anod), 1.6), 35))
    for s in (1, -1):
        O.append(add_bevel(box(f"p_acog_jaw{s}", (base_x1 - base_x0 - 6, 4.0, 8.0), ((base_x0 + base_x1) / 2, s * 13.0, RAIL_TOP - 3.0), anod), 0.8))
    for bx in (base_x0 + 22, base_x1 - 22):
        O.append(smooth(cyl(f"p_acog_bolt{int(bx)}", 5.0, 2.4, (bx, -16.2, RAIL_TOP - 1.5), steel, verts=18, rot=(90, 0, 0))))
        O.append(smooth(add_bevel(cyl(f"p_acog_nut{int(bx)}", 7.0, 6.0, (bx, 18.0, RAIL_TOP - 1.5), anod, verts=10, rot=(90, 0, 0)), 0.8)))
    O.append(box("p_acog_logo", (15, 0.4, 2.0), (E + 84, -20.55, ACOG_Z + 4), white))

    # ---------------------------------------------------------------- trigger housing (polymer), well, pins, guard
    housing = slab("p_housing", [(REC_X0 + 4, REC_BOT + 2), (WELL_X0, REC_BOT + 2), (WELL_X0, -50), (-24, -50), (-34, -44),
                                 (-72, -33), (-112, -29), (-134, -25), (REC_X0 + 4, -18)], 30, poly, bevel=1.6)
    R.append(smooth(housing, 35))
    well = slab("p_mag_well", [(WELL_X0, REC_BOT + 2), (WELL_X1, REC_BOT + 2), (WELL_X1 + 2, WELL_BOT), (WELL_X0, WELL_BOT)],
                2 * (MAG_HALF + 2.5), steel, bevel=1.2)
    boolean(well, cutter("well_mouth", (MAG_DEPTH + 1.5, 2 * MAG_HALF + 1, 80), (MAG_X, 0, WELL_BOT - 0.5)))
    R.append(smooth(well, 35))
    # magazine catch paddle behind the well, release button on the right
    R.append(smooth(add_bevel(slab("p_mag_paddle", [(WELL_X0 - 6, -44), (WELL_X0 + 2, -44), (WELL_X0 + 4, -58), (WELL_X0 - 2, -60)],
                                   16, steel, bevel=0), 0.8)))
    R.append(smooth(cyl("p_mag_button", 4.0, 3.0, (WELL_X0 - 4, -16.0, -28), steel, verts=16, rot=(90, 0, 0))))
    for i, (x, z) in enumerate(((-117, -15), (-92, -15), (40, -20))):
        R.append(smooth(cyl(f"p_pin{i}", 3.4 if i < 2 else 2.4, 32, (x, 0, z), steel, verts=16, rot=(90, 0, 0))))
    # markings: S / E / F round the selector's spindle (right)
    sel_x, sel_z = -14.0, -24.0
    R.append(smooth(cyl("p_sel_spindle", 2.6, 31.5, (sel_x, 0, sel_z), steel, verts=12, rot=(90, 0, 0))))
    for k, a in enumerate((120, 90, 60)):
        r = math.radians(a)
        R.append(box(f"p_sel_mark{k}", (2.0, 0.5, 2.6), (sel_x + 11 * math.cos(r) - 8, -15.2, sel_z + 11 * math.sin(r) - 2), white))

    # trigger guard: the AK's path
    gx = 44.0
    guard_path = [Vector((gx, 0, -41)), Vector((gx - 0.5, 0, -63)), Vector((gx - 5, 0, -70.5)),
                  Vector((gx - 13, 0, -73)), Vector((-6, 0, -73)), Vector((-14, 0, -70)), Vector((-18.5, 0, -62)),
                  Vector((-20, 0, -41))]
    segs = []
    for a, b in zip(guard_path, guard_path[1:]):
        for k in range(6):
            segs.append(a.lerp(b, k / 6))
    segs.append(guard_path[-1])
    sm = [segs[0]]
    for a, b in zip(segs, segs[1:]):
        sm += [a.lerp(b, 0.25), a.lerp(b, 0.75)]
    sm.append(segs[-1])
    rings = []
    for i, c in enumerate(sm):
        t = (sm[min(i + 1, len(sm) - 1)] - sm[max(i - 1, 0)]).normalized()
        nrm = Vector((0, 1, 0)).cross(t).normalized()
        rings.append([c + Vector((0, p, 0)) + nrm * q for p, q in se_ring(6.5, 2.4, n=5, count=16)])
    R.append(loft("p_trigger_guard", rings, poly, angle=50))

    # ---------------------------------------------------------------- handguard (green, slim, vented)
    # Under the left hand its lower half is the AK's lower handguard section, moved by LEFT_HAND_SHIFT.
    ak_bot = [(181, -34), (192, -35.5), (205, -33), (225, -26), (250, -23), (300, -21.5), (324, -21)]
    ak_w = [(181, 20.5), (195, 22), (212, 21), (235, 18.5), (300, 17.5), (324, 17)]
    sx, sz = LEFT_HAND_SHIFT[0], LEFT_HAND_SHIFT[2]
    zc = 12.0
    hand_tab = [(HG_X0, 0.0), (262, 0.0), (280, 1.0), (380, 1.0), (405, 0.0)]   # (behind the palm, not the AK's flare)

    def hg_section(x):
        k = lerp_table([(HG_X0, 0.94), (HG_X0 + 6, 1.0), (HG_X1 - 6, 1.0), (HG_X1, 0.92)], x)
        hand = lerp_table(hand_tab, x)
        a = (HG_HALF * (1 - hand) + min(lerp_table(ak_w, x - sx), 22.0) * hand) * k
        bot = HG_BOT * (1 - hand) + min(-9.0, lerp_table(ak_bot, x - sx) + sz) * hand
        return a, (HG_TOP - zc) * k, (zc - bot) * k

    def hg_ring(x):
        a, bt, bb = hg_section(x)
        return [Vector((x, p, zc + q)) for p, q in se_ring(a, bt, bb, n=3.0, count=36)]

    xs = [HG_X0, HG_X0 + 3] + list(range(int(HG_X0) + 8, int(HG_X1) - 5, 8)) + [HG_X1 - 3, HG_X1]
    F.append(loft("p_handguard", [hg_ring(x) for x in xs], green, angle=55))
    # two rows of vent slots along each side
    for s in (1, -1):
        for row, z in enumerate((px(0, 135)[1], px(0, 157)[1])):
            for k in range(7):
                x = HG_X0 + 22 + k * 37 + (18 if row else 0)
                if x > HG_X1 - 16:
                    continue
                a, bt, bb = hg_section(x)
                q = z - zc
                y = se_half_width(a, bt if q > 0 else bb, 3.0, q)
                F.append(add_bevel(box(f"p_hg_vent{s}{row}{k}", (13, 2.0, 3.2), (x, s * (y - 0.3), z), dark), 0.6))
    F.append(smooth(cyl("p_hg_pin", 2.4, 2 * HG_HALF + 1.5, (HG_X0 + 9, 0, zc - 2), steel, verts=12, rot=(90, 0, 0))))

    # ---------------------------------------------------------------- stock (green): lofted from the traced outline
    top_px = [(53, 140), (345, 139), (386, 136), (404, 124), (418, 112)]
    bot_px = [(60, 300), (150, 277), (250, 250), (350, 222), (418, 203)]
    half_px = [(53, 23.0), (200, 21.0), (418, 16.5)]

    def stock_ring(xp, scale=1.0, dx=0.0):
        x = px(xp, 0)[0] + dx
        top = px(0, lerp_table(top_px, xp))[1]
        bot = px(0, lerp_table(bot_px, xp))[1]
        zc_, b = (top + bot) / 2, (top - bot) / 2
        return [Vector((x, p, zc_ + q)) for p, q in se_ring(lerp_table(half_px, xp) * scale, b * scale, n=2.7, count=40)]

    sxs = list(range(56, 416, 12)) + [418]
    rings = [stock_ring(53, 0.82, -2.6), stock_ring(53, 0.95, -1.2), stock_ring(53)] + [stock_ring(x) for x in sxs]
    F.append(loft("p_stock", rings, green, angle=45))
    F.append(loft("p_stock_band", [stock_ring(x, 1.02) for x in range(346, 386, 8)], poly, angle=45))
    for xp, yp in ((110, 193), (115, 243)):          # sling holes through the butt
        x, z = px(xp, yp)
        y = lerp_table(half_px, xp) * 0.98
        for s in (1, -1):
            F.append(cyl(f"p_butt_hole{xp}{s}", 4.0, 1.0, (x, s * y, z), dark, verts=16, rot=(90, 0, 0)))

    # ---------------------------------------------------------------- pistol grip (the AK's line), green
    def grip_section(t):
        d = lerp_table([(0, 52), (0.18, 44), (0.5, 41), (0.85, 44), (1.0, 43)], t)
        w = lerp_table([(0, 27), (0.3, 30), (0.8, 32), (1.0, 31)], t)
        k = 1.0 if t <= 1.0 else max(0.08, 1.0 - ((t - 1.0) / 0.06) ** 2) ** 0.5
        return se_ring(w / 2 * k, d / 2 * k, n=2.3, count=32)

    ts = [i / 20 for i in range(21)] + [1.02, 1.035, 1.048, 1.057, 1.06]
    F.append(loft("p_grip", sweep(line(GRIP_A, GRIP_B), ts, grip_section), green, angle=60))

    # ---------------------------------------------------------------- magazine: steel, nearly straight
    top_len = (MAG_KNEE - MAG_TOP).length
    bot_len = (MAG_BOT - MAG_KNEE).length
    upper, lower_path = line(MAG_TOP, MAG_KNEE), bezier(MAG_KNEE, MAG_KNEE + (MAG_KNEE - MAG_TOP).normalized() * 50, MAG_BOT)

    def mag_path(t):
        f = top_len / (top_len + bot_len)
        return upper(t / f) if t <= f else lower_path((t - f) / (1 - f))

    M.append(loft("p_mag_body", sweep(mag_path, [i / 16 for i in range(17)], lambda t: se_ring(MAG_HALF, MAG_DEPTH / 2, n=7.0, count=40)),
                  steel, angle=40))
    p_end, tan_end = mag_path(1.0)
    M.append(loft("p_mag_floor", sweep(line(p_end - tan_end * 1.5, p_end + tan_end * 4.0), [0, 0.5, 1],
                                       lambda t: se_ring(MAG_HALF + 1.2, MAG_DEPTH / 2 + 1.5, n=7.0, count=40)), steel, angle=40))
    f0 = top_len / (top_len + bot_len)
    for s in (1, -1):
        # two long stamped ribs down each side, and short cross ribs front and back
        for j, off in enumerate((-8.0, 6.0)):
            pts = []
            for k in range(9):
                c, tan = mag_path(f0 + 0.03 + k * (0.93 - f0) / 8)
                pts.append(c + Vector((tan.z, 0, -tan.x)) * -off)
            for k, (a, b) in enumerate(zip(pts, pts[1:])):
                mid, d = (a + b) / 2, b - a
                ang = math.degrees(math.atan2(d.x, -d.z))
                M.append(add_bevel(box(f"p_mag_rib{s}{j}{k}", (2.4, 1.6, d.length + 0.5), (mid.x, s * (MAG_HALF + 0.3), mid.z), steel,
                                       rot=(0, ang, 0)), 0.4))
        for k in range(5):
            c, tan = mag_path(f0 + 0.08 + k * 0.17 * (1 - f0))
            ang = math.degrees(math.atan2(tan.x, -tan.z))
            for side, off in ((0, -24.0), (1, 22.0)):
                p = c + Vector((tan.z, 0, -tan.x)) * -off
                M.append(add_bevel(box(f"p_mag_dash{s}{k}{side}", (12, 1.2, 2.2), (p.x, s * (MAG_HALF + 0.2), p.z), steel, rot=(0, -ang, 0)), 0.3))
    M.append(smooth(cyl("p_mag_round", 5.2, 50, (MAG_X + 2, 0, MAG_TOP.z - 5), brass, verts=14, rot=RX)))

    # ---------------------------------------------------------------- bolt (in the port), cocking handle
    B = parts["BoltCarrier"]
    B.append(smooth(add_bevel(box("p_bolt", (PORT[1] - PORT[0] + 30, 12, PORT[3] - PORT[2] - 1),
                                  ((PORT[0] + PORT[1]) / 2 - 8, -REC_HALF + 8.5, (PORT[2] + PORT[3]) / 2), steel), 1.0)))
    B.append(smooth(cyl("p_bolt_head", 7.0, 14, (PORT[1] - 4, -REC_HALF + 9, BORE_Z + 14), steel, verts=16, rot=RX)))
    B.append(smooth(cyl("p_bolt_roller", 3.0, 12, (PORT[1] - 16, -REC_HALF + 9, PORT[2] + 4), steel, verts=12, rot=(90, 0, 0))))
    B.append(smooth(cyl("p_extractor", 1.4, 14, (PORT[1] - 4, -REC_HALF + 3.0, BORE_Z + 17), steel, verts=8, rot=RX)))
    H = parts["ChargingHandle"]
    # a stem out of the tube's left side, swung a little forward, and a round knob
    H.append(smooth(add_bevel(box("p_handle_stem", (6, 24, 6), (HANDLE_X + 3, TUBE_R + 10, TUBE_Z), steel, rot=(0, 0, 18)), 1.0)))
    H.append(smooth(lathe("p_handle_knob", [(0, -6), (5.5, -6), (6.5, -3), (6.5, 3), (5.5, 6), (0, 6)],
                          loc=(HANDLE_X + 7, TUBE_R + 23, TUBE_Z), rot=(90, 0, 0), material=steel, verts=20)))
    H.append(smooth(cyl("p_handle_root", 5.0, 4, (HANDLE_X, TUBE_R + 1, TUBE_Z), steel, verts=16, rot=(90, 0, 0))))

    # ---------------------------------------------------------------- trigger (the AK's) and the selector (left)
    trig = prism("p_trigger", [(4, -36), (-4, -36), (-4.5, -47), (-2.5, -56), (2.5, -63), (5.5, -62.2), (2.2, -55), (1.2, -47)],
                 6.0, (0, 0, 0), steel, axis='Y')
    parts["Trigger"].append(smooth(add_bevel(trig, 0.5)))
    y = 15.0 + 1.4
    parts["Selector"].append(smooth(cyl("p_selector_hub", 5.5, 2.6, (sel_x, y, sel_z), steel, verts=18, rot=(90, 0, 0))))
    lever = prism("p_selector", [(sel_x - 3, sel_z - 3), (sel_x + 3, sel_z + 2.5), (sel_x - 15, sel_z + 13), (sel_x - 20, sel_z + 8)],
                  2.6, (0, y + 1.8, 0), steel, axis='Y')
    parts["Selector"].append(smooth(add_bevel(lever, 0.6)))
    return parts


# ============================================================================================ output
POINTS = {
    "Muzzle": (MUZZLE_X, 0, BORE_Z),
    "RearAperture": (DRUM_X, 0, SIGHT_Z),
    "FrontSightPost": (FS_X, 0, SIGHT_Z),
    "ScopeEyepiece": (ACOG_X0, 0, ACOG_Z),
    "ScopeObjective": (ACOG_X0 + ACOG_LEN, 0, ACOG_Z),
    "EjectionPort": ((PORT[0] + PORT[1]) / 2, -REC_HALF, BORE_Z + 14),
    "ChargingHandle": (HANDLE_X + 7, TUBE_R + 23, TUBE_Z),
    "MagTop": (MAG_X, 0, WELL_BOT),
    "MagPivot": (MAG_X + MAG_DEPTH / 2, 0, WELL_BOT),
    "MagCenter": tuple((MAG_TOP + MAG_BOT) / 2),
    "MagGrab": tuple(MAG_KNEE + (MAG_BOT - MAG_KNEE).normalized() * 36),
    "TriggerPivot": (1, 0, -36),
    "GripCenter": (-60, 0, -82),
    "Butt": (BUTT_X - 2, 0, px(0, 220)[1]),
}


def save_points():
    data = {k: [round(c * MM, 5) for c in v] for k, v in POINTS.items()}
    axis = (MAG_KNEE - MAG_TOP).normalized()
    data["MagAxis"] = [round(c, 5) for c in axis]
    data["MagHalfExtents"] = [round(c * MM, 5) for c in (MAG_DEPTH / 2, (MAG_BOT - MAG_TOP).length / 2, MAG_HALF)]
    lean = (MAG_BOT - MAG_TOP).normalized()
    data["MagLean"] = round(math.atan2(lean.x, -lean.z), 5)
    data["LeftHandShift"] = [round(c * MM, 5) for c in LEFT_HAND_SHIFT]
    data["BoltTravel"] = round(BOLT_TRAVEL * MM, 5)
    data["HandleTravel"] = round(HANDLE_TRAVEL * MM, 5)
    data["Calibre"] = "7.62 x 51 mm, 20 rounds"
    data["Note"] = ("Blender coordinates in metres: +X muzzle, +Y left, +Z up; x = 0 at the trigger; z = 0 the AK's bore "
                    "(the G3's is at BORE_Z), so the AK's grip, trigger and hands fit.")
    path = os.path.join(K.OUT, "g3_points.json")
    with open(path, "w") as f:
        json.dump(data, f, indent=2)
    print(f"[g3] points -> {path}", flush=True)


def look_at(cam, target):
    d = Vector(target) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()


def add_hands():
    """Preview only: the AK's hands and sleeves (the left ones moved by LEFT_HAND_SHIFT), as the game draws them."""
    path = os.path.join(K.OUT, "ak47.glb")
    if not os.path.exists(path):
        return []
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    added = [o for o in bpy.data.objects if o not in before]
    keep = []
    for o in added:
        name = o.name.split(".")[0]
        if name in ("Hand", "TriggerFinger", "Sleeve", "HandLeft", "SleeveLeft"):
            if name in ("HandLeft", "SleeveLeft"):
                o.location.x += LEFT_HAND_SHIFT[0] * MM
                o.location.z += LEFT_HAND_SHIFT[2] * MM
            keep.append(o)
        else:
            bpy.data.objects.remove(o)
    return keep


def render_previews():
    scene = bpy.context.scene
    hands = add_hands()
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
    scene.cycles.samples = 64
    scene.cycles.use_denoising = True
    scene.render.image_settings.file_format = 'PNG'
    scene.view_settings.view_transform = 'AgX'
    world = bpy.data.worlds.new("prev")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.62, 0.64, 0.68, 1)
    bg.inputs[1].default_value = 0.35
    sun = bpy.data.lights.new("sun", 'SUN'); sun.energy = 2.6; sun.angle = math.radians(4)
    so = bpy.data.objects.new("sun", sun); scene.collection.objects.link(so)
    so.rotation_euler = (math.radians(35), math.radians(-20), math.radians(30))
    fill = bpy.data.lights.new("fill", 'AREA'); fill.energy = 30; fill.size = 1.5
    fo = bpy.data.objects.new("fill", fill); scene.collection.objects.link(fo)
    fo.location = (0.0, -0.9, 0.4); look_at(fo, (0.0, 0, 0))
    cam_data = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam)
    scene.camera = cam
    views = {
        "left": dict(ortho=1.10, loc=(0.12, 1.5, -0.02), target=(0.12, 0, -0.02), res=(1600, 800), hands=False),
        "right": dict(ortho=1.10, loc=(0.12, -1.5, -0.02), target=(0.12, 0, -0.02), res=(1600, 800), hands=False),
        "three_quarter": dict(lens=50, loc=(0.85, -1.15, 0.5), target=(0.12, 0, -0.01), res=(1400, 900), hands=False),
        "hands_right": dict(ortho=0.80, loc=(0.10, -1.5, -0.04), target=(0.10, 0, -0.04), res=(1400, 900), hands=True),
        "hands_left": dict(ortho=0.80, loc=(0.10, 1.5, -0.04), target=(0.10, 0, -0.04), res=(1400, 900), hands=True),
        "hands_below": dict(ortho=0.60, loc=(0.20, -0.2, -1.5), target=(0.20, 0, -0.02), res=(1400, 900), hands=True),
        "fpv": dict(lens=32, loc=(-0.26, 0.06, 0.15), target=(0.40, -0.02, 0.05), res=(1400, 900), hands=True),
        "sights": dict(lens=40, loc=(ACOG_X0 * MM - 0.12, 0, ACOG_Z * MM + 0.01), target=(1.0, 0, ACOG_Z * MM), res=(1000, 700), hands=False),
        "acog": dict(lens=60, loc=(0.12, -0.42, 0.24), target=(-0.005, 0, ACOG_Z * MM - 0.01), res=(1400, 900), hands=False),
    }
    os.makedirs(K.PREVIEWS, exist_ok=True)
    for name, v in views.items():
        for h in hands:
            h.hide_render = not v["hands"]
        if "ortho" in v:
            cam_data.type = 'ORTHO'; cam_data.ortho_scale = v["ortho"]
        else:
            cam_data.type = 'PERSP'; cam_data.lens = v["lens"]
        cam_data.clip_start = 0.01
        cam.location = v["loc"]
        look_at(cam, v["target"])
        scene.render.resolution_x, scene.render.resolution_y = v["res"]
        scene.render.filepath = os.path.join(K.PREVIEWS, f"g3_{name}.png")
        bpy.ops.render.render(write_still=True)
        print(f"[g3] preview {scene.render.filepath}", flush=True)
    for o in [so, fo, cam] + hands:
        bpy.data.objects.remove(o)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    K.reset()
    parts = build()
    S = Matrix.Scale(MM, 4)
    bpy.context.view_layer.update()          # matrix_world must reflect the rotations/locations set above
    for o in list(bpy.data.objects):
        o.matrix_world = S @ o.matrix_world
        if o.get("uv") in ("cyl", "ring"):
            o["uv"] = "box"          # cylinders here lie along X; modelkit's cyl mapping assumes Z
    final = []
    for name, objs in parts.items():
        out = K.finish(objs, join_name=name, smooth_angle=40.0)[0]
        bpy.ops.object.select_all(action='DESELECT')
        out.select_set(True)
        bpy.context.view_layer.objects.active = out
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        # n-gons (caps, prism faces) -> triangles, so the exporter can compute tangents
        bm = bmesh.new()
        bm.from_mesh(out.data)
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
        bm.to_mesh(out.data)
        bm.free()
        final.append(out)
    for o in list(bpy.data.objects):
        if o.get("cutter"):
            bpy.data.objects.remove(o)
    tris = sum(len(p.vertices) - 2 for o in final for p in o.data.polygons)
    print(f"[g3] {len(final)} objects, ~{tris} triangles", flush=True)
    save_points()
    K.export("g3", final)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.PROJECT, "Assets", "g3.blend"))
    if "nopreview" not in argv:
        render_previews()


main()
