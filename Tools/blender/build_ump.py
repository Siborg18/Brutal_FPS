"""
Builds a UMP40 submachine gun (.40 S&W, 30-round straight magazine; black polymer receiver, skeleton folding stock,
Picatinny rail on top, hooded front sight) and exports it as Assets/Models/ump.glb. The scene is saved as
Assets/ump.blend (the source), key points for the game as Assets/Models/ump_points.json, and preview renders (in the
AK's hands) as Tools/previews/models/ump_*.png.

    blender -b --factory-startup -P Tools/blender/build_ump.py              (build, export, previews)
    blender -b --factory-startup -P Tools/blender/build_ump.py -- nopreview (build and export only)

Coordinates (Blender, like the AK-47): +X out of the muzzle, +Z up, +Y the gun's left side; x = 0 is the trigger.
Unlike the AK the bore is not on the X axis: z = 0 is the AK's bore, so the pistol grip, trigger and guard can be the
AK's exactly (z = -40 the bottom of the receiver) and the UMP's own straight-line bore sits BORE_Z above it. The side
outline is traced from a reference photo at PX_MM millimetres per pixel (px() below). Overall about 690 mm with the
stock out and a 200 mm barrel. Modelled in millimetres and scaled to metres at the end.

Exported objects (names are the part names the game can animate, see Game/Smg.cs):
    Receiver        the polymer receiver, handguard, sights, trigger guard, magazine well, rail, barrel and stock hinge
    Furniture       the pistol grip (texture set tex_grip_stipple) and the skeleton stock with its butt pad
    Magazine        the straight polymer magazine (pulled straight down out of the well, along MagAxis)
    BoltCarrier     the bolt seen through the ejection port on the right (cycles back by BoltTravel with each shot)
    ChargingHandle  the non-reciprocating handle on the left (only moves when racked, by HandleTravel)
    Trigger         pivots about TriggerPivot
    Selector        the ambidextrous selector levers
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

# The reference photo: the trigger at pixel x 400, the bore at pixel y 88.
PX_MM = 0.94
BORE_Z = 18.3


def px(x, y):
    """A point traced off the side reference (pixels, y down) -> model (x, z) in mm."""
    return ((x - 400) * PX_MM, (88 - y) * PX_MM + BORE_Z)


def outline(*points):
    return [px(x, y) for x, y in points]


# Key dimensions (mm)
STOCK_TRIM_PX = 10.0 / PX_MM                         # 10 mm off the stock's butt end
MUZZLE_X = px(745, 0)[0] - 7.0                      # the photo's muzzle, less 7 mm: a 200 mm barrel
REC_X0, REC_X1 = px(272, 0)[0], px(704, 0)[0]       # receiver rear / front of the handguard
REC_TOP, REC_BOT = px(0, 46)[1], -40.0
REC_HALF, LOWER_HALF = 16.0, 15.0                  # upper receiver / lower (the AK's width round the grip)
UPPER_BOT = -6.0                                     # the upper receiver's bottom edge (the handguard hangs below it)
SIGHT_Z = 69.0                                       # the line through the rear aperture and the front post
REAR_SIGHT_X = px(293, 0)[0]
FRONT_POST_X = px(688, 0)[0]
GRIP_A, GRIP_B = Vector((-46, 0, -30)), Vector((-77, 0, -138))   # the AK-47's grip line
# The AK's left hand cups its lower handguard; moved by LEFT_HAND_SHIFT it cups this one (the same section).
LEFT_HAND_SHIFT = (-40.0, 0.0, -14.0)
HG_X0, HG_X1 = px(560, 0)[0], REC_X1
MAG_TOP, MAG_BOT = Vector((124.0, 0, -28.0)), Vector((136.0, 0, -205.0))
MAG_DEPTH, MAG_HALF = 51.0, 13.0                     # front-to-back, half thickness
BOLT_TRAVEL, HANDLE_TRAVEL = 60.0, 72.0


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
    poly = mat("polymer", None, 0.6, 0.0)
    stipple = mat("tex_grip_stipple", None, 0.7, 0.0)
    steel = mat("gunmetal", None, 0.35, 0.85)
    dark = mat("flat_recess", (0.02, 0.02, 0.02), 0.8, 0.0)
    rubber = mat("flat_rubber", (0.035, 0.035, 0.035), 0.85, 0.0)
    window = mat("flat_smoke", (0.22, 0.20, 0.17), 0.25, 0.0)
    red = mat("flat_red", (0.7, 0.05, 0.03), 0.5)
    parts = {"Receiver": [], "Furniture": [], "Magazine": [], "BoltCarrier": [], "ChargingHandle": [], "Trigger": [], "Selector": []}
    R, F, M = parts["Receiver"], parts["Furniture"], parts["Magazine"]

    # ---------------------------------------------------------------- upper receiver
    up = slab("p_upper", [(REC_X0, UPPER_BOT), (REC_X1, UPPER_BOT), px(704, 60), px(686, 46), (REC_X0, REC_TOP)],
              2 * REC_HALF, poly, bevel=0)
    port = (px(440, 0)[0], px(500, 0)[0], px(0, 98)[1], px(0, 74)[1])          # ejection port (right)
    boolean(up, cutter("port", (port[1] - port[0], 8, port[3] - port[2]), ((port[0] + port[1]) / 2, -REC_HALF, (port[2] + port[3]) / 2)))
    slot = (px(434, 0)[0], px(522, 0)[0], px(0, 98)[1], px(0, 86)[1])          # charging handle slot (left)
    boolean(up, cutter("handle_slot", (slot[1] - slot[0], 6, slot[3] - slot[2]), ((slot[0] + slot[1]) / 2, REC_HALF, (slot[2] + slot[3]) / 2)))
    for s in (1, -1):         # vent slots along the front
        for x0, x1 in ((584, 628), (640, 684)):
            a, b = px(x0, 0)[0], px(x1, 0)[0]
            boolean(up, cutter(f"vent{s}{x0}", (b - a, 4, 5.5), ((a + b) / 2, s * REC_HALF, px(0, 75)[1])))
    R.append(smooth(add_bevel(up, 1.4), 35))
    R.append(add_bevel(box("p_port_shadow", (port[1] - port[0] - 2, 4, port[3] - port[2] - 2),
                           ((port[0] + port[1]) / 2, -REC_HALF + 5, (port[2] + port[3]) / 2), dark), 0.3))

    # ---------------------------------------------------------------- handguard: the AK's lower handguard section
    # (under the left hand) moved by LEFT_HAND_SHIFT, carried up into the receiver
    ak_bot = [(181, -34), (192, -35.5), (205, -33), (225, -26), (250, -23), (300, -21.5), (324, -21)]
    ak_w = [(181, 20.5), (195, 22), (212, 21), (235, 18.5), (300, 17.5), (324, 17)]
    sx, sz = LEFT_HAND_SHIFT[0], LEFT_HAND_SHIFT[2]

    def hg_ring(x, grow=0.0):
        k = lerp_table([(HG_X0, 0.92), (HG_X0 + 8, 1.0), (HG_X1 - 10, 1.0), (HG_X1, 0.9)], x)
        a = min(lerp_table(ak_w, x - sx), 18.0) * k + grow
        bot = lerp_table(ak_bot, x - sx) + sz
        zc = UPPER_BOT + 2.0
        return [Vector((x, p, zc + q)) for p, q in se_ring(a, 6.0, (zc - bot) * k + grow, n=3.2, count=32)]

    R.append(loft("p_handguard", [hg_ring(x) for x in [HG_X0, HG_X0 + 4] + list(range(int(HG_X0) + 10, int(HG_X1) - 8, 10)) + [HG_X1 - 4, HG_X1]],
                  poly, angle=55))

    # ---------------------------------------------------------------- lower receiver and magazine well
    lower = slab("p_lower", [(REC_X0 + 8, UPPER_BOT + 2), (HG_X0 + 6, UPPER_BOT + 2), (HG_X0 + 6, -50), (MAG_TOP.x - 32, -50),
                             (MAG_TOP.x - 36, REC_BOT), (-96, REC_BOT), (-104, -28), (REC_X0 + 8, -16)],
                 2 * LOWER_HALF, poly, bevel=1.2)
    R.append(smooth(lower, 35))
    well = slab("p_mag_well", [(MAG_TOP.x - 32, -50), (HG_X0 + 8, -50), (HG_X0 + 8, -20), (MAG_TOP.x - 32, -20)],
                2 * (MAG_HALF + 3.5), poly, bevel=1.5)
    boolean(well, cutter("well_mouth", (MAG_DEPTH + 2, 2 * MAG_HALF + 1, 40), (MAG_TOP.x + 2, 0, -50)))
    R.append(smooth(well, 35))
    # magazine release paddle behind the well
    R.append(smooth(slab("p_mag_release", [(80, -44), (93, -44), (94, -52), (90, -60), (82, -59), (81, -50)], 14, poly, bevel=0.6)))

    # trigger guard: the AK's path, polymer
    guard_path = [Vector((50, 0, -41)), Vector((49.5, 0, -63)), Vector((45, 0, -70.5)), Vector((37, 0, -73)),
                  Vector((-6, 0, -73)), Vector((-14, 0, -70)), Vector((-18.5, 0, -62)), Vector((-20, 0, -41))]
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

    # pins through the receiver
    for i, (x, z) in enumerate(((-105, 30), (-104, -20), (66, -26), (140, -12), (255, 6))):
        R.append(smooth(cyl(f"p_pin{i}", 2.6, 2 * REC_HALF + 1.0, (x, 0, z), steel, verts=14, rot=(90, 0, 0))))

    # ---------------------------------------------------------------- sights and rail
    # rear sight: a ghost ring (it's only ~14 cm from the eye when aiming, so a small peep would black out the view)
    R.append(smooth(add_bevel(slab("p_rear_sight", [(REAR_SIGHT_X - 10, REC_TOP - 1), (REAR_SIGHT_X + 10, REC_TOP - 1),
                                                    (REAR_SIGHT_X + 3, SIGHT_Z - 8), (REAR_SIGHT_X - 3, SIGHT_Z - 8)], 12, poly, bevel=0), 0.8), 35))
    R.append(smooth(lathe("p_rear_ring", [(6.5, -3), (9.5, -3), (9.5, 3), (6.5, 3), (6.5, -3)],
                          loc=(REAR_SIGHT_X, 0, SIGHT_Z), rot=RX, material=poly, verts=32)))
    hood = slab("p_front_sight", outline((664, 48), (704, 48), (700, 30), (694, 14), (684, 14), (676, 22)), 20, poly, bevel=0)
    boolean(hood, cutter("hood_window", (80, 13, 16), (FRONT_POST_X, 0, SIGHT_Z + 1)))
    R.append(smooth(add_bevel(hood, 1.0), 35))
    R.append(add_bevel(box("p_front_post", (3.0, 2.4, SIGHT_Z - REC_TOP + 4), (FRONT_POST_X, 0, (SIGHT_Z + REC_TOP - 4) / 2), steel), 0.3))

    rail_x0, rail_x1 = px(350, 0)[0], px(522, 0)[0]
    R.append(add_bevel(box("p_rail_base", (rail_x1 - rail_x0, 21, 4.5), ((rail_x0 + rail_x1) / 2, 0, REC_TOP + 1.5), steel), 0.4))
    x, i = rail_x0 + 3, 0
    while x + 5.3 < rail_x1:
        R.append(add_bevel(box(f"p_rail_tooth{i}", (5.3, 21, 4.5), (x + 2.65, 0, REC_TOP + 5.5), steel), 0.3))
        x += 10.0
        i += 1

    # ---------------------------------------------------------------- barrel, nut and flash hider
    R.append(smooth(lathe("p_barrel", [(0, REC_X1 - 4), (8.5, REC_X1 - 4), (8.5, MUZZLE_X - 14), (0, MUZZLE_X - 14)],
                          loc=(0, 0, BORE_Z), rot=RX, material=steel, verts=24)))
    R.append(smooth(lathe("p_barrel_nut", [(0, REC_X1 - 2), (13, REC_X1 - 2), (13, REC_X1 + 6), (10, REC_X1 + 8), (0, REC_X1 + 8)],
                          loc=(0, 0, BORE_Z), rot=RX, material=steel, verts=24)))
    hider = lathe("p_flash_hider", [(0, MUZZLE_X - 16), (10, MUZZLE_X - 16), (10, MUZZLE_X - 2), (8.6, MUZZLE_X), (0, MUZZLE_X)],
                  loc=(0, 0, BORE_Z), rot=RX, material=steel, verts=24)
    for k in range(4):
        a = 45 + 90 * k
        boolean(hider, cutter(f"hider_slot{k}", (11, 3, 30), (MUZZLE_X - 5, 0, BORE_Z), rot=(a, 0, 0)))
    R.append(smooth(hider))
    R.append(cyl("p_bore", 5.2, 1.0, (MUZZLE_X - 0.2, 0, BORE_Z), dark, verts=16, rot=RX))

    # ---------------------------------------------------------------- stock hinge, stock and butt pad
    for k, z in enumerate((40, 10, -20)):
        R.append(smooth(cyl(f"p_hinge{k}", 6.5, 22, (REC_X0 - 2, 0, z), steel, verts=18)))
        R.append(add_bevel(box(f"p_hinge_leaf{k}", (12, 2 * REC_HALF - 2, 20), (REC_X0 - 2, 0, z), steel), 1.0))
    # The butt end comes STOCK_TRIM forward of the photo's, so the gun is 690 mm long with the stock out.
    stock = slab("p_stock", outline((6 + STOCK_TRIM_PX, 68), (196, 68), (258, 46), (272, 46), (272, 150), (248, 146), (46 + STOCK_TRIM_PX, 198),
                                    (12 + STOCK_TRIM_PX, 198), (4 + STOCK_TRIM_PX, 186)),
                 30, poly, bevel=0)
    stock.location.x -= 4.0
    for k, hole in enumerate((outline((42 + STOCK_TRIM_PX, 86), (132, 86), (132, 150), (42 + STOCK_TRIM_PX, 176)),
                              outline((146, 104), (188, 86), (232, 74), (250, 84), (250, 120), (230, 132), (150, 150), (144, 138)))):
        c = prism(f"cut_stock_hole{k}", hole, 60, (-4.0, 0, 0), dark, axis='Y')
        boolean(stock, c)
    F.append(smooth(add_bevel(stock, 2.0), 35))
    butt_x = px(4 + STOCK_TRIM_PX, 0)[0] - 4.0
    F.append(add_bevel(box("p_butt_pad", (8, 34, px(0, 66)[1] - px(0, 200)[1]),
                           (butt_x - 2, 0, (px(0, 66)[1] + px(0, 200)[1]) / 2), rubber), 2.0))

    # ---------------------------------------------------------------- pistol grip (the AK's), stippled polymer
    def grip_section(t):
        d = lerp_table([(0, 52), (0.18, 44), (0.5, 40), (0.85, 43), (1.0, 42)], t)
        w = lerp_table([(0, 26), (0.3, 29), (0.8, 31), (1.0, 30)], t)
        k = 1.0 if t <= 1.0 else max(0.08, 1.0 - ((t - 1.0) / 0.06) ** 2) ** 0.5
        return se_ring(w / 2 * k, d / 2 * k, n=2.3, count=32)

    ts = [i / 20 for i in range(21)] + [1.02, 1.035, 1.048, 1.057, 1.06]
    F.append(loft("p_grip", sweep(line(GRIP_A, GRIP_B), ts, grip_section), stipple, angle=60))

    # ---------------------------------------------------------------- magazine: straight, with witness holes
    axis = (MAG_BOT - MAG_TOP).normalized()

    def mag_section(t):
        return se_ring(MAG_HALF, MAG_DEPTH / 2, n=6.0, count=40)
    mag_line = line(MAG_TOP, MAG_BOT)
    M.append(loft("p_mag_body", sweep(mag_line, [i / 8 for i in range(9)], mag_section), poly, angle=40))
    M.append(loft("p_mag_floor", sweep(line(MAG_BOT - axis * 2.0, MAG_BOT + axis * 7.0), [0, 0.5, 1],
                                       lambda t: se_ring(MAG_HALF + 2.0, MAG_DEPTH / 2 + 3.0, n=6.0, count=40)), poly, angle=40))
    fwd = Vector((-axis.z, 0, axis.x))     # towards the front of the magazine
    lean = math.degrees(math.atan2(axis.x, -axis.z))
    for s in (1, -1):
        for k in range(15):
            c = MAG_TOP + axis * (30 + k * 9.5) + fwd * (-6.0)
            M.append(box(f"p_mag_window{s}_{k}", (9, 1.0, 4.0), (c.x, s * MAG_HALF, c.z), window, rot=(0, -lean, 0)))

    # ---------------------------------------------------------------- bolt (seen through the port), charging handle
    B = parts["BoltCarrier"]
    B.append(smooth(add_bevel(box("p_bolt", (port[1] - port[0] + 10, 10, port[3] - port[2] - 4),
                                  ((port[0] + port[1]) / 2 + 4, -REC_HALF + 7, (port[2] + port[3]) / 2), steel), 0.8)))
    B.append(cyl("p_extractor", 1.4, 16, (port[1] - 4, -REC_HALF + 2.2, (port[2] + port[3]) / 2 + 3), steel, verts=8, rot=RX))
    H = parts["ChargingHandle"]
    hx, hz = slot[1] - 8, (slot[2] + slot[3]) / 2
    H.append(add_bevel(box("p_handle_stem", (8, 10, 8), (hx, REC_HALF + 2, hz), steel), 0.4))
    H.append(smooth(add_bevel(box("p_handle_knob", (16, 9, 13), (hx, REC_HALF + 9, hz), poly), 2.0)))

    # ---------------------------------------------------------------- trigger (the AK's) and selector levers
    trig = prism("p_trigger", [(4, -36), (-4, -36), (-4.5, -47), (-2.5, -56), (2.5, -63), (5.5, -62.2), (2.2, -55), (1.2, -47)],
                 6.0, (0, 0, 0), steel, axis='Y')
    parts["Trigger"].append(smooth(add_bevel(trig, 0.5)))
    sel_x, sel_z = px(372, 0)[0], -14.0
    for s in (1, -1):
        y = s * (LOWER_HALF + 1.2)
        parts["Selector"].append(smooth(cyl(f"p_selector_hub{s}", 5.0, 2.4, (sel_x, y, sel_z), steel, verts=18, rot=(90, 0, 0))))
        lever = prism(f"p_selector{s}", [(sel_x - 3, sel_z - 2.5), (sel_x + 2, sel_z + 3), (sel_x - 20, sel_z + 9), (sel_x - 22, sel_z + 4)],
                      2.2, (0, y + s * 1.8, 0), poly, axis='Y')
        parts["Selector"].append(smooth(add_bevel(lever, 0.5)))
        R.append(box(f"p_selector_mark{s}", (12, 0.6, 2.6), (sel_x - 14, s * (LOWER_HALF + 0.2), sel_z - 9), red))
    return parts


# ============================================================================================ output
POINTS = {
    "Muzzle": (MUZZLE_X, 0, BORE_Z),
    "FrontSightPost": (FRONT_POST_X, 0, SIGHT_Z),
    "RearSightAperture": (REAR_SIGHT_X, 0, SIGHT_Z),
    "EjectionPort": (px(470, 0)[0], -REC_HALF, px(0, 86)[1]),
    "ChargingHandle": (px(522, 0)[0] - 8, REC_HALF + 9, px(0, 92)[1]),
    "MagTop": (MAG_TOP.x, 0, REC_BOT),
    "MagPivot": (MAG_TOP.x + MAG_DEPTH / 2, 0, REC_BOT),
    "MagCenter": tuple((MAG_TOP + MAG_BOT) / 2),
    "TriggerPivot": (1, 0, -36),
    "GripCenter": (-60, 0, -82),
    "Butt": (px(4 + STOCK_TRIM_PX, 0)[0] - 8, 0, px(0, 130)[1]),
}


def save_points():
    data = {k: [round(c * MM, 5) for c in v] for k, v in POINTS.items()}
    axis = (MAG_BOT - MAG_TOP).normalized()
    data["MagAxis"] = [round(c, 5) for c in axis]
    data["MagHalfExtents"] = [round(c * MM, 5) for c in (MAG_DEPTH / 2, (MAG_BOT - MAG_TOP).length / 2, MAG_HALF)]
    data["LeftHandShift"] = [round(c * MM, 5) for c in LEFT_HAND_SHIFT]
    data["BoltTravel"] = round(BOLT_TRAVEL * MM, 5)
    data["HandleTravel"] = round(HANDLE_TRAVEL * MM, 5)
    data["Calibre"] = ".40 S&W, 30 rounds"
    data["Note"] = ("Blender coordinates in metres: +X muzzle, +Y left, +Z up; x = 0 at the trigger; z = 0 the AK's bore "
                    "(the UMP's is at BORE_Z), so the AK's grip, trigger and hands fit.")
    path = os.path.join(K.OUT, "ump_points.json")
    with open(path, "w") as f:
        json.dump(data, f, indent=2)
    print(f"[ump] points -> {path}", flush=True)


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
        "left": dict(ortho=0.80, loc=(-0.02, 1.5, -0.04), target=(-0.02, 0, -0.04), res=(1600, 800), hands=False),
        "right": dict(ortho=0.80, loc=(-0.02, -1.5, -0.04), target=(-0.02, 0, -0.04), res=(1600, 800), hands=False),
        "three_quarter": dict(lens=50, loc=(0.60, -0.85, 0.40), target=(-0.02, 0, -0.04), res=(1400, 900), hands=False),
        "hands_right": dict(ortho=0.62, loc=(0.03, -1.5, -0.06), target=(0.03, 0, -0.06), res=(1400, 900), hands=True),
        "hands_left": dict(ortho=0.62, loc=(0.03, 1.5, -0.06), target=(0.03, 0, -0.06), res=(1400, 900), hands=True),
        "fpv": dict(lens=32, loc=(-0.24, 0.06, 0.12), target=(0.40, -0.02, 0.02), res=(1400, 900), hands=True),
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
        scene.render.filepath = os.path.join(K.PREVIEWS, f"ump_{name}.png")
        bpy.ops.render.render(write_still=True)
        print(f"[ump] preview {scene.render.filepath}", flush=True)
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
    print(f"[ump] {len(final)} objects, ~{tris} triangles", flush=True)
    save_points()
    K.export("ump", final)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.PROJECT, "Assets", "ump.blend"))
    if "nopreview" not in argv:
        render_previews()


main()
