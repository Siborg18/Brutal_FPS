"""
Builds an AR-15 carbine (5.56 x 45 mm, 30-round polymer magazine; flat-top upper with a 1-4x scope in rings on a riser,
a quad-rail handguard with an angled foregrip, a railed gas block, a long ported muzzle shroud, a skeleton stock on
the buffer tube) and exports it as Assets/Models/ar15.glb. The scene is saved as Assets/ar15.blend (the source), key
points for the game as Assets/Models/ar15_points.json, and preview renders (in the AK's hands) as
Tools/previews/models/ar15_*.png.

    blender -b --factory-startup -P Tools/blender/build_ar15.py              (build, export, previews)
    blender -b --factory-startup -P Tools/blender/build_ar15.py -- nopreview (build and export only)

Coordinates (Blender, like the AK-47 and the UMP40): +X out of the muzzle, +Z up, +Y the gun's left side; x = 0 is
the trigger. z = 0 is the AK's bore, so the pistol grip, trigger and guard can be the AK's exactly and the hands fit;
the AR's own bore (in line with the buffer tube and the stock) sits BORE_Z above it. The side outline is traced from
a reference photo (right side) at PX_MM millimetres per pixel (px() below). About 946 mm long with the stock as in
the photo. Modelled in millimetres and scaled to metres at the end.

Exported objects (names are the part names the game can animate, see Game/Rifle.cs):
    Receiver        upper and lower receiver, rails, handguard, gas block, barrel, muzzle shroud, trigger guard,
                    buffer tube, angled foregrip and the scope with its mount
    Furniture       the pistol grip (texture set tex_grip_stipple) and the stock with its butt pad
    Magazine        the 30-round polymer magazine (drops straight down out of the well, along MagAxis)
    BoltCarrier     the bolt carrier seen through the ejection port (cycles back by BoltTravel with each shot)
    ChargingHandle  the T-handle at the back of the upper (only moves when racked, by HandleTravel)
    Trigger         pivots about TriggerPivot
    Selector        the safety selector levers
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

# The reference photo: the trigger at pixel x 527, the bore at pixel y 203.
PX_MM = 0.85
BORE_Z = 20.0


def px(x, y):
    """A point traced off the side reference (pixels, y down) -> model (x, z) in mm."""
    return ((x - 527) * PX_MM, (203 - y) * PX_MM + BORE_Z)


def outline(*points):
    return [px(x, y) for x, y in points]


# Key dimensions (mm)
MUZZLE_X = px(1178, 0)[0]
BARREL_END_X = 485.0                                 # 406 mm (16 in) from the bolt face, inside the muzzle shroud
UP_X0, UP_X1 = -93.0, 134.0                          # upper receiver: rear (against the lower's tower) / front
UP_BOT, UP_TOP = -4.0, 48.0                          # its bottom edge, and its top under the rail
UP_HALF, LOW_HALF = 14.5, 13.0
RAIL_TOP = UP_TOP + 8.0                              # the top of the Picatinny teeth
HG_X0, HG_X1 = UP_X1, px(900, 0)[0]                  # the quad-rail handguard
GB_X1 = px(960, 0)[0]                                # the front of the gas block
SCOPE_Z = px(0, 117)[1]                              # the scope's axis: the line of sight
EYEPIECE_X, OBJECTIVE_X = px(388, 0)[0], px(745, 0)[0]
TURRET_X = px(610, 0)[0]
RING_XS = (12.0, 137.0)
GRIP_A, GRIP_B = Vector((-46, 0, -30)), Vector((-77, 0, -138))   # the AK-47's grip line
# The AK's left hand cups its lower handguard; moved by LEFT_HAND_SHIFT it cups this one (whose lower half below the
# hand is the AK's section, moved the same way).
LEFT_HAND_SHIFT = (0.0, 0.0, 20.0)
MAG_X = 84.0                                         # the magazine's centre line (front to back) at the top
MAG_TOP = Vector((MAG_X, 0, -2.0))
MAG_KNEE = Vector((MAG_X + 2.0, 0, -70.0))           # straight down to here (in the well), then curving forward
MAG_BOT = Vector((MAG_X + 18.0, 0, -178.0))
MAG_DEPTH, MAG_HALF = 64.0, 11.5                     # front-to-back, half thickness
WELL_X0, WELL_X1, WELL_BOT = 44.0, 125.0, -64.0      # the magazine well (outside)
BOLT_TRAVEL, HANDLE_TRAVEL = 80.0, 76.0


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


def cyl_cutter(name, radius, depth, loc, rot=(0, 0, 0)):
    c = cyl("cut_" + name, radius, depth, loc, mat("flat_recess", (0.02, 0.02, 0.02), 0.8, 0.0), verts=16, rot=rot)
    return c


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


def rail(parts, name, x0, x1, z, material, width=21.0, down=False, y=0.0, side=0):
    """A Picatinny rail from x0 to x1: a base and a tooth every 10 mm. Its teeth face up from z (down: face down; side
    +-1: face out to the left / right from y, centred on height z)."""
    def put(n, size, centre):
        if side:
            size = (size[0], size[2], size[1])
            centre = (centre[0], y + side * (centre[2] - z), z)
        parts.append(add_bevel(box(n, size, centre, material), 0.35))
    s = -1 if down else 1
    put(f"{name}_base", (x1 - x0, width, 4.0), ((x0 + x1) / 2, 0, z + s * 2.0))
    x, i = x0 + 2.5, 0
    while x + 5.3 < x1:
        put(f"{name}_tooth{i}", (5.3, width, 4.0), (x + 2.65, 0, z + s * 6.0))
        x += 10.0
        i += 1


# ============================================================================================ parts
def build():
    poly = mat("polymer", None, 0.6, 0.0)
    stipple = mat("tex_grip_stipple", None, 0.7, 0.0)
    steel = mat("gunmetal", None, 0.35, 0.85)
    anod = mat("flat_anodized", (0.045, 0.045, 0.048), 0.42, 0.35)
    dark = mat("flat_recess", (0.02, 0.02, 0.02), 0.8, 0.0)
    rubber = mat("flat_rubber", (0.035, 0.035, 0.035), 0.85, 0.0)
    glass = mat("flat_lens", (0.05, 0.08, 0.12), 0.08, 0.6)
    brass = mat("flat_brass", (0.72, 0.55, 0.25), 0.35, 1.0)
    white = mat("flat_marking", (0.75, 0.75, 0.72), 0.6, 0.0)
    parts = {"Receiver": [], "Furniture": [], "Magazine": [], "BoltCarrier": [], "ChargingHandle": [], "Trigger": [], "Selector": [], "Optic": []}
    R, F, M = parts["Receiver"], parts["Furniture"], parts["Magazine"]

    # ---------------------------------------------------------------- upper receiver
    up = slab("p_upper", [(UP_X0, UP_BOT + 3), (UP_X0 + 4, UP_BOT), (UP_X1, UP_BOT), (UP_X1, UP_TOP), (UP_X0, UP_TOP)],
              2 * UP_HALF, poly, bevel=0)
    port = (22.0, 72.0, 11.0, 29.0)                   # ejection port (right): x0, x1, z0, z1
    boolean(up, cutter("port", (port[1] - port[0], 12, port[3] - port[2]), ((port[0] + port[1]) / 2, -UP_HALF, (port[2] + port[3]) / 2)))
    # the rail's channel for the charging handle, open at the back
    boolean(up, cutter("handle_channel", (14, 22, 7), (UP_X0, 0, UP_TOP - 3)))
    R.append(smooth(add_bevel(up, 1.6), 35))
    R.append(add_bevel(box("p_port_shadow", (port[1] - port[0] + 4, 4, port[3] - port[2] + 4),
                           ((port[0] + port[1]) / 2, -UP_HALF + 9, (port[2] + port[3]) / 2), dark), 0.3))
    # forward-assist housing and plunger (right rear), brass deflector behind the port
    R.append(smooth(add_bevel(slab("p_fa_housing", [(-70, 16), (-8, 16), (-2, 26), (-8, 42), (-62, 42)], 8, poly,
                                   y=-UP_HALF - 2.5, bevel=0), 1.8), 35))
    R.append(smooth(cyl("p_fa_tube", 7.5, 34, (-48, -UP_HALF - 7, 30), poly, verts=24, rot=(0, 90, 0))))
    R.append(smooth(lathe("p_fa_button", [(0, -84), (8.5, -84), (9.5, -80), (9.5, -66), (0, -66)], loc=(0, -UP_HALF - 7, 30),
                          rot=RX, material=steel, verts=24)))
    defl = prism("p_deflector", [(0, 26), (20, 26), (20, 46), (6, 46)], 9, (0, -UP_HALF - 2.5, 0), poly, axis='Y')
    R.append(smooth(add_bevel(defl, 1.5), 35))
    # dust cover, swung open below the port, on its hinge rod
    R.append(smooth(cyl("p_cover_hinge", 1.6, port[1] - port[0] + 10, ((port[0] + port[1]) / 2, -UP_HALF - 1.2, port[2] - 3),
                        steel, verts=10, rot=RX)))
    R.append(add_bevel(box("p_dust_cover", (port[1] - port[0] + 6, 1.4, 20), ((port[0] + port[1]) / 2, -UP_HALF - 4.5, port[2] - 11),
                           poly, rot=(-22, 0, 0)), 0.5))
    # flat-top rail
    rail(R, "p_up_rail", UP_X0 + 2, UP_X1, UP_TOP, poly)

    # ---------------------------------------------------------------- lower receiver, tower round the buffer tube
    lower = slab("p_lower", [(UP_X0 + 2, UP_BOT), (WELL_X0, UP_BOT), (WELL_X0, -41), (-18, -41), (-30, -40), (-92, -40),
                             (-104, -30), (-109, -14), (-109, 36), (-102, 42), (UP_X0, 42)],
                 2 * LOW_HALF, poly, bevel=1.4)
    boolean(lower, cyl_cutter("lower_tower_bore", 15.2, 40, (-100, 0, BORE_Z), rot=RX))
    R.append(smooth(lower, 35))
    R.append(smooth(cyl("p_tower_ring", 17.5, 4, (-107, 0, BORE_Z), poly, verts=32, rot=RX)))
    well = slab("p_mag_well", [(WELL_X0, UP_BOT), (WELL_X1, UP_BOT), (WELL_X1, WELL_BOT + 6), (WELL_X1 + 3, WELL_BOT),
                               (WELL_X0 - 3, WELL_BOT), (WELL_X0, WELL_BOT + 6)],
                2 * (MAG_HALF + 4.5), poly, bevel=1.5)
    boolean(well, cutter("well_mouth", (MAG_DEPTH + 2, 2 * MAG_HALF + 1, 120), (MAG_X, 0, WELL_BOT - 0.5)))
    R.append(smooth(well, 35))
    # the finger-grooved grip round the front of the well
    wg = slab("p_well_grip", [(WELL_X1 - 6, UP_BOT), (WELL_X1 + 12, UP_BOT), (WELL_X1 + 14, -16), (WELL_X1 + 8, -27),
                              (WELL_X1 + 15, -39), (WELL_X1 + 9, -51), (WELL_X1 + 16, -64), (WELL_X1 + 10, -77),
                              (WELL_X1 + 15, -88), (WELL_X1 + 8, -96), (WELL_X1 + 1, -96), (WELL_X1 - 2, -66),
                              (WELL_X1 - 6, -62)],
              2 * (MAG_HALF + 6.5), poly, bevel=2.0)
    R.append(smooth(wg, 40))
    # magazine release (right), bolt catch (left), takedown and pivot pins, trigger and hammer pins
    R.append(smooth(cyl("p_mag_release", 5.0, 3.0, (WELL_X0 - 8, -LOW_HALF - 1.0, -22), steel, verts=18, rot=(90, 0, 0))))
    R.append(smooth(add_bevel(slab("p_bolt_catch", [(WELL_X0 - 14, -4), (WELL_X0 - 2, -4), (WELL_X0 - 2, -20),
                                                    (WELL_X0 - 8, -26), (WELL_X0 - 14, -20)], 2.5, poly,
                                   y=LOW_HALF + 1.2, bevel=0), 0.6)))
    for i, (x, z) in enumerate(((-86, -12), (WELL_X1 - 14, -12), (6, -26), (-24, -26))):
        R.append(smooth(cyl(f"p_pin{i}", 2.8 if i < 2 else 2.2, 2 * LOW_HALF + 1.0, (x, 0, z), steel, verts=14, rot=(90, 0, 0))))
    # markings: the selector's SAFE / FIRE dots
    for k, a in enumerate((-60, 30)):
        r = math.radians(a)
        R.append(box(f"p_sel_mark{k}", (2.2, 0.5, 2.2), (-38 + 13 * math.cos(r), LOW_HALF + 0.2, -20 + 13 * math.sin(r)), white))

    # trigger guard: the AK's path, ending at the back of the well
    guard_path = [Vector((WELL_X0 - 2, 0, -41)), Vector((WELL_X0 - 2.5, 0, -63)), Vector((WELL_X0 - 7, 0, -70.5)),
                  Vector((WELL_X0 - 15, 0, -73)), Vector((-6, 0, -73)), Vector((-14, 0, -70)), Vector((-18.5, 0, -62)),
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
        rings.append([c + Vector((0, p, 0)) + nrm * q for p, q in se_ring(7.0, 2.6, n=5, count=16)])
    R.append(loft("p_trigger_guard", rings, poly, angle=50))

    # ---------------------------------------------------------------- buffer tube and castle nut
    R.append(smooth(lathe("p_buffer_tube", [(0, -262), (13.5, -262), (14.9, -259), (14.9, -112), (0, -112)],
                          loc=(0, 0, BORE_Z), rot=RX, material=poly, verts=32)))
    nut = lathe("p_castle_nut", [(0, -122), (17.5, -122), (17.5, -112), (0, -112)], loc=(0, 0, BORE_Z), rot=RX, material=steel, verts=32)
    for k in range(4):
        boolean(nut, cutter(f"nut_notch{k}", (5, 40, 4), (-120, 0, BORE_Z), rot=(45 + 90 * k, 0, 0)))
    R.append(smooth(nut))
    R.append(add_bevel(box("p_tube_key", (140, 7, 4), (-188, 0, BORE_Z - 15.5), poly), 0.8))

    # ---------------------------------------------------------------- handguard (quad rail)
    # Under the left hand its lower half is the AK's lower handguard section, moved by LEFT_HAND_SHIFT.
    ak_bot = [(181, -34), (192, -35.5), (205, -33), (225, -26), (250, -23), (300, -21.5), (324, -21)]
    ak_w = [(181, 20.5), (195, 22), (212, 21), (235, 18.5), (300, 17.5), (324, 17)]
    sx, sz = LEFT_HAND_SHIFT[0], LEFT_HAND_SHIFT[2]
    zc = BORE_Z + 6.0

    def hg_ring(x):
        k = lerp_table([(HG_X0, 0.94), (HG_X0 + 6, 1.0), (HG_X1 - 6, 1.0), (HG_X1, 0.94)], x)
        hand = lerp_table([(HG_X0, 0.0), (172, 0.0), (190, 1.0)], x)
        a = (24.0 * (1 - hand) + min(lerp_table(ak_w, x - sx), 22.0) * hand) * k
        bot = min(-8.0, lerp_table(ak_bot, x - sx) + sz) * hand + -8.0 * (1 - hand)
        return [Vector((x, p, zc + q)) for p, q in se_ring(a, (UP_TOP - 1 - zc) * k, (zc - bot) * k, n=3.4, count=36)]

    xs = [HG_X0, HG_X0 + 3] + list(range(int(HG_X0) + 8, int(HG_X1) - 5, 8)) + [HG_X1 - 3, HG_X1]
    R.append(loft("p_handguard", [hg_ring(x) for x in xs], poly, angle=55))
    rail(R, "p_hg_rail", HG_X0 + 1, HG_X1, UP_TOP, poly)
    rail(R, "p_hg_rail_bot", HG_X0 + 1, 188, -6.0, poly, width=19, down=True)
    for s in (1, -1):
        rail(R, f"p_hg_rail_side{s}", HG_X0 + 1, 170, zc, poly, width=19, y=s * 22.0, side=s)
    # vent slots along the sides ahead of the side rails
    for s in (1, -1):
        for k in range(5):
            x = 196 + k * 23
            R.append(add_bevel(box(f"p_hg_vent{s}{k}", (14, 2, 5), (x, s * (lerp_table(ak_w, x - sx) * 0.92 + 0.2), zc + 16), dark), 0.6))
    # angled foregrip: a hand stop just behind the palm
    afg = slab("p_afg", [(HG_X0 + 4, -10), (188, -10), (184, -16), (160, -46), (150, -48), (144, -42), (HG_X0 + 6, -18)], 22, poly, bevel=0)
    boolean(afg, prism("cut_afg_window", [(150, -18), (172, -18), (157, -36)], 40, (0, 0, 0), dark, axis='Y'))
    R.append(smooth(add_bevel(afg, 2.2), 40))

    # ---------------------------------------------------------------- barrel, gas block, muzzle shroud
    R.append(smooth(lathe("p_barrel", [(0, UP_X1 - 4), (9.5, UP_X1 - 4), (9.5, HG_X1), (7.6, HG_X1 + 4), (7.6, BARREL_END_X),
                                       (0, BARREL_END_X)], loc=(0, 0, BORE_Z), rot=RX, material=steel, verts=24)))
    gb = slab("p_gas_block", [(HG_X1 + 3, BORE_Z - 13), (GB_X1, BORE_Z - 13), (GB_X1, UP_TOP), (HG_X1 + 3, UP_TOP)], 26, steel, bevel=1.5)
    R.append(smooth(gb, 35))
    rail(R, "p_gb_rail", HG_X1 + 4, GB_X1, UP_TOP, steel)
    R.append(smooth(cyl("p_gb_screw", 2.6, 27, (GB_X1 - 10, 0, BORE_Z - 6), steel, verts=12, rot=(90, 0, 0))))
    sh0 = px(985, 0)[0]
    shroud = lathe("p_shroud", [(11.5, sh0), (15.2, sh0), (15.2, MUZZLE_X - 6), (13.6, MUZZLE_X), (11.5, MUZZLE_X), (11.5, sh0)],
                   loc=(0, 0, BORE_Z), rot=RX, material=steel, verts=32)
    for k in range(7):                           # oval ports in four rows, staggered
        x = sh0 + 22 + k * 19.5
        for j, a in enumerate((0, 90)):
            xx = x + (9.75 if j else 0.0)
            if xx > MUZZLE_X - 16:
                continue
            for d in (-3.2, 3.2):
                boolean(shroud, cyl_cutter(f"port{k}{j}{d}", 4.6, 40, (xx + d, 0, BORE_Z), rot=(a + 90, 0, 0)))
            boolean(shroud, cutter(f"portb{k}{j}", (6.4, 40, 9.2), (xx, 0, BORE_Z), rot=(a, 0, 0)))
    R.append(smooth(shroud, 40))
    R.append(smooth(lathe("p_shroud_back", [(0, sh0 - 2), (15.2, sh0 - 2), (15.2, sh0 + 4), (0, sh0 + 4)],
                          loc=(0, 0, BORE_Z), rot=RX, material=steel, verts=32)))
    R.append(cyl("p_crown", 11.2, 1.0, (BARREL_END_X + 1, 0, BORE_Z), dark, verts=24, rot=RX))

    # ---------------------------------------------------------------- scope: riser, rings, 1-4x scope
    O = parts["Optic"]
    O.append(add_bevel(box("p_riser", (RING_XS[1] - RING_XS[0] + 40, 23, 10), ((RING_XS[0] + RING_XS[1]) / 2, 0, RAIL_TOP + 4.5), anod), 1.0))
    for s in (1, -1):
        O.append(add_bevel(box(f"p_riser_clamp{s}", (RING_XS[1] - RING_XS[0] + 30, 4, 11), ((RING_XS[0] + RING_XS[1]) / 2, s * 13, RAIL_TOP), anod), 0.8))
    for i, rx in enumerate(RING_XS):
        O.append(smooth(add_bevel(box(f"p_ring_base{i}", (17, 26, SCOPE_Z - RAIL_TOP - 10), (rx, 0, (SCOPE_Z + RAIL_TOP + 10) / 2 - 2), anod), 1.2), 35))
        ring = lathe(f"p_ring{i}", [(12.7, rx - 8.5), (17.5, rx - 8.5), (17.5, rx + 8.5), (12.7, rx + 8.5), (12.7, rx - 8.5)],
                     loc=(0, 0, SCOPE_Z), rot=RX, material=anod, verts=36)
        boolean(ring, cutter(f"ring_split{i}", (30, 50, 1.6), (rx, 0, SCOPE_Z)))
        O.append(smooth(ring))
        for s in (1, -1):
            for k in (-1, 1):
                O.append(smooth(cyl(f"p_ring_screw{i}{s}{k}", 2.2, 5, (rx + k * 4.5, s * 18.5, SCOPE_Z + 2.5), steel, verts=10, rot=(90, 0, 0))))
    e, o = EYEPIECE_X, OBJECTIVE_X
    O.append(smooth(lathe("p_scope", [(0, e + 3), (16.5, e + 3), (17.5, e + 1), (19.5, e), (20, e + 2), (20, e + 30),
                                      (19, e + 36), (16, e + 44), (16.5, e + 46), (16.5, e + 70), (13.5, e + 74), (12.7, e + 78),
                                      (12.7, TURRET_X - 22), (16, TURRET_X - 18), (17, TURRET_X - 12), (17, TURRET_X + 12),
                                      (16, TURRET_X + 18), (12.7, TURRET_X + 22), (12.7, o - 26), (14, o - 20), (15.2, o - 12),
                                      (15.2, o), (13.6, o), (13.6, o - 3), (0, o - 3)],
                          loc=(0, 0, SCOPE_Z), rot=RX, material=anod, verts=40), 35))
    for k in range(14):                        # grip ribs on the power ring
        a = math.tau * k / 14
        O.append(add_bevel(box(f"p_power_rib{k}", (20, 2.2, 1.6), (e + 58, 17.0 * math.cos(a), SCOPE_Z + 17.0 * math.sin(a)),
                               anod, rot=(math.degrees(a) - 90, 0, 0)), 0.3))
    O.append(add_bevel(box("p_power_lever", (6, 3, 9), (e + 58, -10, SCOPE_Z + 18), anod, rot=(30, 0, 0)), 0.8))
    O.append(cyl("p_ocular_lens", 16.6, 0.6, (e + 3.4, 0, SCOPE_Z), glass, verts=32, rot=RX))
    O.append(cyl("p_objective_lens", 13.6, 0.6, (o - 2.6, 0, SCOPE_Z), glass, verts=32, rot=RX))
    O.append(lathe("p_ocular_rim", [(15.5, e + 0.5), (19.6, e + 0.5), (19.6, e + 2), (15.5, e + 2), (15.5, e + 0.5)],
                   loc=(0, 0, SCOPE_Z), rot=RX, material=rubber, verts=40))
    # turrets: elevation on top, windage on the right, illumination on the left
    for name, rot, off in (("elev", (0, 0, 0), (0, 0, 1)), ("wind", (90, 0, 0), (0, -1, 0)), ("illum", (90, 0, 0), (0, 1, 0))):
        base = Vector((TURRET_X, 0, SCOPE_Z)) + Vector(off) * 17
        cap = 10.5 if name != "illum" else 11.5
        O.append(smooth(cyl(f"p_turret_{name}_base", 12.5, 8, tuple(base + Vector(off) * 3), anod, verts=28, rot=rot)))
        O.append(smooth(cyl(f"p_turret_{name}", cap, 13, tuple(base + Vector(off) * 12.5), anod, verts=28, rot=rot)))
        for k in range(16):
            a = math.tau * k / 16
            u = Vector((math.cos(a), math.sin(a), 0))
            if name != "elev":
                u = Vector((u.x, 0, u.y))
            c = base + Vector(off) * 13 + u * cap
            O.append(box(f"p_turret_{name}_rib{k}", (1.6, 1.6, 10) if name == "elev" else (1.6, 10, 1.6), tuple(c), anod))
    O.append(box("p_scope_logo", (16, 0.4, 3), (o - 40, -15.25, SCOPE_Z + 3), white))

    # ---------------------------------------------------------------- stock: skeleton body on the tube, butt pad
    stock = slab("p_stock", outline((80, 180), (296, 178), (303, 186), (303, 240), (270, 243), (205, 258), (180, 285),
                                   (140, 318), (122, 337), (92, 337), (80, 322), (76, 200)), 40, poly, bevel=0)
    boolean(stock, prism("cut_stock_hole", outline((128, 250), (238, 248), (195, 262), (162, 292), (130, 302)), 60, (0, 0, 0), dark, axis='Y'))
    for k in range(7):                         # slanted slots along the body, both sides
        x0 = 128 + 22 * k
        slot = outline((x0, 194), (x0 + 8, 194), (x0 + 22, 214), (x0 + 14, 214))
        for s in (1, -1):
            boolean(stock, prism(f"cut_stock_slot{k}{s}", slot, 8, (0, s * 20, 0), dark, axis='Y'))
    boolean(stock, cyl_cutter("stock_tube", 15.2, 170, (px(303, 0)[0] - 80, 0, BORE_Z), rot=RX))
    F.append(smooth(add_bevel(stock, 2.2), 35))
    bx = px(78, 0)[0]
    pad_top, pad_bot = px(0, 180)[1], px(0, 337)[1]
    F.append(add_bevel(box("p_butt_pad", (10, 44, pad_top - pad_bot), (bx - 4, 0, (pad_top + pad_bot) / 2), rubber), 2.5))
    for k in range(9):
        z = pad_bot + 10 + k * (pad_top - pad_bot - 20) / 8
        F.append(add_bevel(box(f"p_pad_rib{k}", (3, 42, 4), (bx - 9.5, 0, z), rubber), 0.8))
    F.append(smooth(cyl("p_sling_loop", 6, 44, (px(130, 0)[0], 0, px(0, 212)[1]), steel, verts=14, rot=(90, 0, 0))))

    # ---------------------------------------------------------------- pistol grip (the AK's), stippled polymer
    def grip_section(t):
        d = lerp_table([(0, 52), (0.18, 44), (0.5, 40), (0.85, 43), (1.0, 42)], t)
        w = lerp_table([(0, 26), (0.3, 29), (0.8, 31), (1.0, 30)], t)
        k = 1.0 if t <= 1.0 else max(0.08, 1.0 - ((t - 1.0) / 0.06) ** 2) ** 0.5
        return se_ring(w / 2 * k, d / 2 * k, n=2.3, count=32)

    ts = [i / 20 for i in range(21)] + [1.02, 1.035, 1.048, 1.057, 1.06]
    F.append(loft("p_grip", sweep(line(GRIP_A, GRIP_B), ts, grip_section), stipple, angle=60))

    # ---------------------------------------------------------------- magazine: straight in the well, curving below
    top_len = (MAG_KNEE - MAG_TOP).length
    bot_len = (MAG_BOT - MAG_KNEE).length
    upper, lower_path = line(MAG_TOP, MAG_KNEE), bezier(MAG_KNEE, MAG_KNEE + (MAG_KNEE - MAG_TOP).normalized() * 50, MAG_BOT)

    def mag_path(t):
        f = top_len / (top_len + bot_len)
        return upper(t / f) if t <= f else lower_path((t - f) / (1 - f))

    def mag_section(t):
        return se_ring(MAG_HALF, MAG_DEPTH / 2, n=6.0, count=40)

    M.append(loft("p_mag_body", sweep(mag_path, [i / 16 for i in range(17)], mag_section), poly, angle=40))
    p_end, tan_end = mag_path(1.0)
    M.append(loft("p_mag_floor", sweep(line(p_end - tan_end * 2.0, p_end + tan_end * 8.0), [0, 0.5, 1],
                                       lambda t: se_ring(MAG_HALF + 2.0, MAG_DEPTH / 2 + 3.5, n=6.0, count=40)), poly, angle=40))
    # the ribbed grip panel low on each side, and the follower and top round in the feed lips
    for s in (1, -1):
        for k in range(6):
            c, tan = mag_path(0.62 + k * 0.055)
            ang = math.degrees(math.atan2(tan.x, -tan.z))
            M.append(add_bevel(box(f"p_mag_rib{s}{k}", (MAG_DEPTH - 14, 1.6, 2.4), (c.x, s * (MAG_HALF + 0.4), c.z), poly, rot=(0, -ang, 0)), 0.4))
    M.append(smooth(cyl("p_mag_round", 4.8, 40, (MAG_X + 2, 0, MAG_TOP.z - 4), brass, verts=14, rot=RX)))

    # ---------------------------------------------------------------- bolt carrier (in the port), charging handle
    B = parts["BoltCarrier"]
    B.append(smooth(add_bevel(box("p_carrier", (port[1] - port[0] + 30, 12, port[3] - port[2] - 1),
                                  ((port[0] + port[1]) / 2 - 8, -UP_HALF + 8.5, (port[2] + port[3]) / 2), steel), 1.0)))
    B.append(smooth(cyl("p_bolt_head", 6.5, 14, (port[1] - 3, -UP_HALF + 9, BORE_Z), steel, verts=16, rot=RX)))
    B.append(smooth(cyl("p_extractor", 1.4, 14, (port[1] - 3, -UP_HALF + 3.0, BORE_Z + 3), steel, verts=8, rot=RX)))
    H = parts["ChargingHandle"]
    hz = UP_TOP - 4.0
    H.append(add_bevel(box("p_handle_shaft", (40, 9, 5), (UP_X0 + 12, 0, hz), poly), 0.6))
    H.append(smooth(add_bevel(prism("p_handle_t", [(UP_X0 - 1, -24), (UP_X0 - 8, -22), (UP_X0 - 10, -8), (UP_X0 - 10, 8),
                                                   (UP_X0 - 8, 22), (UP_X0 - 1, 24), (UP_X0 + 2, 6), (UP_X0 + 2, -6)],
                                    6, (0, 0, hz), poly, axis='Z'), 1.2)))
    H.append(add_bevel(box("p_handle_latch", (12, 6, 5), (UP_X0 - 4, 20, hz + 3.8), poly), 0.8))

    # ---------------------------------------------------------------- trigger (the AK's) and the selector
    trig = prism("p_trigger", [(4, -36), (-4, -36), (-4.5, -47), (-2.5, -56), (2.5, -63), (5.5, -62.2), (2.2, -55), (1.2, -47)],
                 6.0, (0, 0, 0), steel, axis='Y')
    parts["Trigger"].append(smooth(add_bevel(trig, 0.5)))
    sel_x, sel_z = -38.0, -20.0
    for s in (1, -1):
        y = s * (LOW_HALF + 1.2)
        parts["Selector"].append(smooth(cyl(f"p_selector_hub{s}", 5.5, 2.4, (sel_x, y, sel_z), steel, verts=18, rot=(90, 0, 0))))
        lever = prism(f"p_selector{s}", [(sel_x - 3, sel_z - 3), (sel_x + 3, sel_z + 2.5), (sel_x - 12, sel_z + 14), (sel_x - 17, sel_z + 10)],
                      2.4, (0, y + s * 1.8, 0), steel, axis='Y')
        parts["Selector"].append(smooth(add_bevel(lever, 0.6)))
    return parts


# ============================================================================================ output
POINTS = {
    "Muzzle": (MUZZLE_X, 0, BORE_Z),
    "ScopeEyepiece": (EYEPIECE_X, 0, SCOPE_Z),
    "ScopeObjective": (OBJECTIVE_X, 0, SCOPE_Z),
    "EjectionPort": (47.0, -UP_HALF, BORE_Z),
    "ChargingHandle": (UP_X0 - 6, 22, UP_TOP - 4.0),
    "MagTop": (MAG_X, 0, WELL_BOT),
    "MagPivot": (MAG_X + MAG_DEPTH / 2, 0, WELL_BOT),
    "MagCenter": tuple((MAG_TOP + MAG_BOT) / 2),
    "MagGrab": tuple(MAG_TOP + (MAG_KNEE - MAG_TOP).normalized() * 60),
    "TriggerPivot": (1, 0, -36),
    "GripCenter": (-60, 0, -82),
    "Butt": (px(78, 0)[0] - 9, 0, px(0, 258)[1]),
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
    data["Calibre"] = "5.56 x 45 mm, 30 rounds"
    data["Note"] = ("Blender coordinates in metres: +X muzzle, +Y left, +Z up; x = 0 at the trigger; z = 0 the AK's bore "
                    "(the AR's is at BORE_Z), so the AK's grip, trigger and hands fit.")
    path = os.path.join(K.OUT, "ar15_points.json")
    with open(path, "w") as f:
        json.dump(data, f, indent=2)
    print(f"[ar15] points -> {path}", flush=True)


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
        "left": dict(ortho=1.02, loc=(0.08, 1.5, -0.01), target=(0.08, 0, -0.01), res=(1600, 800), hands=False),
        "right": dict(ortho=1.02, loc=(0.08, -1.5, -0.01), target=(0.08, 0, -0.01), res=(1600, 800), hands=False),
        "three_quarter": dict(lens=50, loc=(0.75, -1.05, 0.45), target=(0.08, 0, -0.01), res=(1400, 900), hands=False),
        "hands_right": dict(ortho=0.70, loc=(0.06, -1.5, -0.04), target=(0.06, 0, -0.04), res=(1400, 900), hands=True),
        "hands_left": dict(ortho=0.70, loc=(0.06, 1.5, -0.04), target=(0.06, 0, -0.04), res=(1400, 900), hands=True),
        "hands_below": dict(ortho=0.55, loc=(0.16, -0.2, -1.5), target=(0.16, 0, -0.02), res=(1400, 900), hands=True),
        "fpv": dict(lens=32, loc=(-0.26, 0.06, 0.15), target=(0.40, -0.02, 0.05), res=(1400, 900), hands=True),
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
        scene.render.filepath = os.path.join(K.PREVIEWS, f"ar15_{name}.png")
        bpy.ops.render.render(write_still=True)
        print(f"[ar15] preview {scene.render.filepath}", flush=True)
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
    print(f"[ar15] {len(final)} objects, ~{tris} triangles", flush=True)
    save_points()
    K.export("ar15", final)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.PROJECT, "Assets", "ar15.blend"))
    if "nopreview" not in argv:
        render_previews()


main()
