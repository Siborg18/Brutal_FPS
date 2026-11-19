"""
Builds an M249 SAW light machine gun (5.56 x 45 mm, belt-fed from a soft 200-round pouch under the receiver; long barrel
with a slotted flash hider, gas block with the hooded front post, folding bipod, a carrying handle swung up and to the
left, a hinged feed cover with a rail on it, a ring rear sight, a fixed polymer stock) and exports it as
Assets/Models/m249.glb. The scene is saved as Assets/m249.blend (the source), key points for the game as
Assets/Models/m249_points.json, and preview renders (in the AK's hands) as Tools/previews/models/m249_*.png.

    blender -b --factory-startup -P Tools/blender/build_m249.py              (build, export, previews)
    blender -b --factory-startup -P Tools/blender/build_m249.py -- nopreview (build and export only)

Coordinates (Blender, like the other long guns): +X out of the muzzle, +Z up, +Y the gun's left side; x = 0 is the
trigger. z = 0 is the AK's bore, so the pistol grip, trigger and guard can be the AK's exactly and the hands fit; the
M249's own bore sits BORE_Z above it. Under the left hand (x = 181 to 324) the handguard's lower half is the AK's lower
handguard section, unmoved. Proportions come from a side photo of the gun being fired: 1035 mm long, with about 521 mm of barrel. Modelled in
millimetres and scaled to metres at the end.

Exported objects (names are the part names the game can animate, see Game/Lmg.cs):
    Receiver        receiver, feed tray, handguard, gas block and cylinder, barrel, flash hider, sights, trigger guard,
                    carrying handle and bipod
    Furniture       the pistol grip (texture set tex_grip_stipple) and the stock with its butt plate
    FeedCover       the feed cover and its rail (opens about CoverHinge)
    Pouch           the soft ammunition pouch (comes away with its belt, along PouchAxis)
    Belt0 .. Belt10 the links and rounds, one each, from the feed tray (0) down the left side into the pouch (10): they
                    go, from the pouch end, as the last rounds are fired
    BoltCarrier     the bolt seen through the ejection port (cycles back by BoltTravel with each shot)
    ChargingHandle  the knob on the right (only moves when racked, by HandleTravel)
    Trigger         pivots about TriggerPivot
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

# Key dimensions (mm)
BORE_Z = 22.0
SIGHT_Z = 84.0                                       # the line of sight
MUZZLE_X = 684.0
HIDER_X = 626.0                                     # the back of the flash hider
GB_X0, GB_X1 = 396.0, 452.0                          # the gas block
HG_X0, HG_X1 = 174.0, 392.0                          # the handguard
REC_X0, REC_X1 = -135.0, 128.0                       # the receiver
REC_HALF, LOW_HALF = 27.0, 14.0
REC_TOP = 36.0                                       # its top under the feed cover (the feed tray)
COVER_X0, COVER_X1, COVER_TOP = -100.0, 120.0, 66.0  # the feed cover (hinged at its back)
HINGE = (COVER_X0 + 3.0, COVER_TOP - 6.0)
REAR_X = -112.0                                      # the ring rear sight
FRONT_X = 432.0                                      # the front post
POUCH_X0, POUCH_X1, POUCH_TOP, POUCH_BOT = 50.0, 166.0, -44.0, -176.0
POUCH_HALF = 36.0
PORT = (18.0, 92.0, 8.0, 32.0)                       # ejection port (right): x0, x1, z0, z1
BELT_X = 80.0                                        # the middle of the rounds in the belt
ROUND_LEN = 57.0
BELT_PITCH = 11.9
BELT_COUNT = 11
# The belt's path across the gun, (y, z): out of the tray, over the side and down into the pouch.
BELT_PATH = [(6.0, 40.0), (30.0, 40.0), (40.0, 33.0), (44.5, 12.0), (44.5, -20.0), (30.0, -54.0)]
GRIP_A, GRIP_B = Vector((-46, 0, -30)), Vector((-77, 0, -138))   # the AK-47's grip line
GUARD_X = 44.0                                       # the trigger guard's front (as the AR-15's)
BOLT_TRAVEL, HANDLE_TRAVEL = 80.0, 70.0
HANDLE_X = 116.0


# ============================================================================================ helpers
def smooth(o, angle=40.0):
    o["smooth"] = True
    o["smooth_angle"] = angle
    return o


def boolean(target, cutter_obj, op='DIFFERENCE'):
    mod = target.modifiers.new("bool_" + cutter_obj.name, 'BOOLEAN')
    mod.object = cutter_obj
    mod.operation = op
    mod.solver = 'EXACT'
    cutter_obj.hide_render = True
    cutter_obj.display_type = 'WIRE'
    cutter_obj["cutter"] = True
    return mod


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


def rail(parts, name, x0, x1, z, material, width=21.0):
    """A Picatinny rail from x0 to x1, its teeth facing up from z: a base and a tooth every 10 mm."""
    parts.append(add_bevel(box(f"{name}_base", (x1 - x0, width, 4.0), ((x0 + x1) / 2, 0, z + 2.0), material), 0.35))
    x, i = x0 + 2.5, 0
    while x + 5.3 < x1:
        parts.append(add_bevel(box(f"{name}_tooth{i}", (5.3, width, 4.0), (x + 2.65, 0, z + 6.0), material), 0.35))
        x += 10.0
        i += 1


def tube_x(points, radius, name, material, count=10):
    """A round tube through 3D points, as a loft (rings square to the path), for arms and legs."""
    rings = []
    for i, c in enumerate(points):
        t = (points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)]).normalized()
        u = t.cross(Vector((0.3, 1, 0.2))).normalized()
        v = t.cross(u).normalized()
        rings.append([c + u * (radius * math.cos(math.tau * k / count)) + v * (radius * math.sin(math.tau * k / count)) for k in range(count)])
    return loft(name, rings, material, angle=60)


def belt_points():
    """Where each link sits on BELT_PATH, one pitch apart: [(y, z, ty, tz)] (tangent towards the pouch)."""
    pts = [Vector(p) for p in BELT_PATH]
    out = []
    i, run = 0, 0.0
    for k in range(BELT_COUNT):
        want = k * BELT_PITCH
        while i < len(pts) - 2 and want - run > (pts[i + 1] - pts[i]).length:
            run += (pts[i + 1] - pts[i]).length
            i += 1
        d = pts[i + 1] - pts[i]
        p = pts[i] + d.normalized() * (want - run)
        out.append((p.x, p.y, d.normalized().x, d.normalized().y))
    return out


# ============================================================================================ parts
def build():
    poly = mat("polymer", None, 0.6, 0.0)
    stipple = mat("tex_grip_stipple", None, 0.7, 0.0)
    steel = mat("gunmetal", None, 0.35, 0.85)
    dark = mat("flat_recess", (0.02, 0.02, 0.02), 0.8, 0.0)
    rubber = mat("flat_rubber", (0.035, 0.035, 0.035), 0.85, 0.0)
    brass = mat("flat_brass", (0.72, 0.55, 0.25), 0.35, 1.0)
    copper = mat("flat_copper", (0.62, 0.34, 0.2), 0.35, 1.0)
    cloth = mat("canvas", (0.40, 0.40, 0.25), 0.9, 0.0)
    webbing = mat("flat_webbing", (0.16, 0.17, 0.11), 0.9, 0.0)
    parts = {"Receiver": [], "Furniture": [], "FeedCover": [], "Pouch": [], "BoltCarrier": [], "ChargingHandle": [], "Trigger": []}
    for k in range(BELT_COUNT):
        parts[f"Belt{k}"] = []
    R, F, C, P = parts["Receiver"], parts["Furniture"], parts["FeedCover"], parts["Pouch"]

    # ---------------------------------------------------------------- receiver: wide above, narrow where the hand is
    up = slab("p_upper", [(REC_X0, 6), (REC_X1, 6), (REC_X1, REC_TOP), (COVER_X0, REC_TOP), (COVER_X0, COVER_TOP - 6),
                          (REC_X0 + 10, COVER_TOP - 6), (REC_X0, COVER_TOP - 18)], 2 * REC_HALF, steel, bevel=0)
    x0, x1, z0, z1 = PORT
    boolean(up, cutter("port", (x1 - x0, 14, z1 - z0), ((x0 + x1) / 2, -REC_HALF, (z0 + z1) / 2)))
    boolean(up, cutter("handle_slot", (HANDLE_TRAVEL + 18, 8, 7), (HANDLE_X - HANDLE_TRAVEL / 2, -REC_HALF, 20.0)))
    R.append(smooth(add_bevel(up, 1.6), 35))
    R.append(add_bevel(box("p_port_shadow", (x1 - x0 + 4, 5, z1 - z0 + 4), ((x0 + x1) / 2, -REC_HALF + 11, (z0 + z1) / 2), dark), 0.3))
    # the shoulder down to the lower, ribs stamped along the side
    for s in (1, -1):
        R.append(add_bevel(box(f"p_shoulder{s}", (REC_X1 - REC_X0 - 2, REC_HALF - LOW_HALF + 3, 6),
                               ((REC_X0 + REC_X1) / 2, s * (REC_HALF + LOW_HALF) / 2, 6.5), steel, rot=(s * 18, 0, 0)), 0.8))
        for k in range(3):
            R.append(add_bevel(box(f"p_side_rib{s}{k}", (70, 0.9, 3), (-52, s * (REC_HALF + 0.3), 16 + k * 7), steel), 0.25))
    # the lower, narrow enough for the AK's right hand
    lower = slab("p_lower", [(REC_X0 + 2, 8), (POUCH_X1 + 4, 8), (POUCH_X1 + 4, -4), (GUARD_X + 2, -4), (GUARD_X + 2, -41),
                             (-18, -41), (-30, -40), (-92, -40), (-112, -22), (REC_X0 + 2, -10)],
                 2 * LOW_HALF, steel, bevel=1.4)
    R.append(smooth(lower, 35))
    # the feed tray, its ribs and cartridge stop, seen with the cover up
    R.append(add_bevel(box("p_tray", (112, 2 * REC_HALF - 6, 2.5), (62, 0, REC_TOP + 1), steel), 0.5))
    for k in range(5):
        R.append(add_bevel(box(f"p_tray_rib{k}", (5, 2 * REC_HALF - 8, 2.5), (22 + k * 20, 0, REC_TOP + 3), steel), 0.4))
    R.append(add_bevel(box("p_cart_stop", (6, 16, 8), (BELT_X + ROUND_LEN / 2 + 8, 0, REC_TOP + 5), steel), 0.6))
    # the pouch hanger under the feed, its latch at the back
    hanger = slab("p_hanger", [(POUCH_X0 - 4, -4), (POUCH_X1 + 4, -4), (POUCH_X1 + 4, -30), (POUCH_X1, POUCH_TOP + 4),
                               (POUCH_X0, POUCH_TOP + 4), (POUCH_X0 - 4, -30)], 2 * (POUCH_HALF - 8), steel, bevel=1.5)
    R.append(smooth(hanger, 35))
    R.append(add_bevel(box("p_hanger_latch", (8, 10, 12), (POUCH_X0 - 7, 0, -30), steel), 1.0))
    for i, (x, z) in enumerate(((-86, -12), (6, -26), (-24, -26), (112, -18))):
        R.append(smooth(cyl(f"p_pin{i}", 2.6, 2 * LOW_HALF + 1.0, (x, 0, z), steel, verts=14, rot=(90, 0, 0))))

    # trigger guard: the AK's path, ending at the front of the lower
    guard_path = [Vector((GUARD_X - 2, 0, -41)), Vector((GUARD_X - 2.5, 0, -63)), Vector((GUARD_X - 7, 0, -70.5)),
                  Vector((GUARD_X - 15, 0, -73)), Vector((-6, 0, -73)), Vector((-14, 0, -70)), Vector((-18.5, 0, -62)),
                  Vector((-20, 0, -41))]
    segs = []
    for a, b in zip(guard_path, guard_path[1:]):
        for k in range(6):
            segs.append(a.lerp(b, k / 6))
    segs.append(guard_path[-1])
    rings = []
    for i, c in enumerate(segs):
        t = (segs[min(i + 1, len(segs) - 1)] - segs[max(i - 1, 0)]).normalized()
        nrm = Vector((0, 1, 0)).cross(t).normalized()
        rings.append([c + Vector((0, p, 0)) + nrm * q for p, q in se_ring(7.0, 2.6, n=5, count=16)])
    R.append(loft("p_trigger_guard", rings, steel, angle=50))

    # ---------------------------------------------------------------- feed cover: a closed lid, hinged at its back
    cover = slab("p_cover", [(COVER_X0, REC_TOP + 5), (COVER_X1, REC_TOP + 5), (COVER_X1, COVER_TOP - 8), (COVER_X1 - 8, COVER_TOP),
                             (COVER_X0, COVER_TOP)], 2 * REC_HALF + 2, steel, bevel=0)
    C.append(smooth(add_bevel(cover, 2.0), 35))
    C.append(box("p_cover_under", (COVER_X1 - COVER_X0 - 8, 2 * REC_HALF - 4, 1.0), ((COVER_X0 + COVER_X1) / 2, 0, REC_TOP + 4.6), dark))
    C.append(add_bevel(box("p_feed_pawl", (40, 20, 6), (BELT_X - 20, 4, REC_TOP + 2), steel), 1.0))
    rail(C, "p_cover_rail", COVER_X0 + 6, COVER_X1 - 8, COVER_TOP, steel, width=22.0)
    for s in (1, -1):
        C.append(add_bevel(box(f"p_cover_latch{s}", (10, 4, 14), (COVER_X0 + 18, s * (REC_HALF + 1.5), REC_TOP + 14), steel), 0.8))
    R.append(smooth(cyl("p_hinge", 3.5, 2 * REC_HALF + 6, (HINGE[0], 0, HINGE[1]), steel, verts=14, rot=(90, 0, 0))))

    # ---------------------------------------------------------------- rear sight: a ring on a block
    R.append(smooth(add_bevel(slab("p_rear_block", [(REAR_X - 20, COVER_TOP - 18), (REAR_X + 11, COVER_TOP - 8), (REAR_X + 11, COVER_TOP - 2),
                                                    (REAR_X - 20, COVER_TOP - 2)], 24, steel, bevel=0), 1.2), 35))
    R.append(add_bevel(box("p_rear_tower", (9, 7, SIGHT_Z - COVER_TOP - 2), (REAR_X, 0, (SIGHT_Z + COVER_TOP) / 2 - 7), steel), 0.8))
    R.append(smooth(lathe("p_rear_ring", [(6.0, REAR_X - 2), (8.5, REAR_X - 2), (8.5, REAR_X + 2), (6.0, REAR_X + 2), (6.0, REAR_X - 2)],
                          loc=(0, 0, SIGHT_Z), rot=RX, material=steel, verts=28)))

    # ---------------------------------------------------------------- handguard: the AK's lower half under the hand
    ak_bot = [(181, -34), (192, -35.5), (205, -33), (225, -26), (250, -23), (300, -21.5), (324, -21)]
    ak_w = [(181, 20.5), (195, 22), (212, 21), (235, 18.5), (300, 17.5), (324, 17)]
    zc = 10.0
    top = BORE_Z + 16.0

    def hg_ring(x):
        k = lerp_table([(HG_X0, 0.94), (HG_X0 + 6, 1.0), (HG_X1 - 6, 1.0), (HG_X1, 0.94)], x)
        hand = lerp_table([(HG_X0, 0.0), (176, 1.0), (330, 1.0), (345, 0.0)], x)
        a = (23.0 * (1 - hand) + min(lerp_table(ak_w, x), 22.0) * hand) * k
        bot = lerp_table(ak_bot, x) * hand + -20.0 * (1 - hand)
        return [Vector((x, p, zc + q)) for p, q in se_ring(a, (top - zc) * k, (zc - bot) * k, n=3.4, count=36)]

    xs = [HG_X0, HG_X0 + 3] + list(range(int(HG_X0) + 8, int(HG_X1) - 5, 8)) + [HG_X1 - 3, HG_X1]
    R.append(loft("p_handguard", [hg_ring(x) for x in xs], poly, angle=55))
    rail(R, "p_hg_rail", HG_X0 + 2, HG_X1 - 4, top - 1, steel, width=19.0)
    for s in (1, -1):
        for k in range(6):
            x = 192 + k * 32
            R.append(add_bevel(box(f"p_hg_vent{s}{k}", (20, 2, 6), (x, s * (lerp_table(ak_w, x) * 0.92 + 1.6), BORE_Z + 4), dark), 0.6))

    # ---------------------------------------------------------------- barrel, gas cylinder and block, flash hider
    R.append(smooth(lathe("p_barrel", [(0, REC_X1 - 6), (12, REC_X1 - 6), (12, HG_X1), (10.8, HG_X1 + 6), (10.0, HIDER_X + 4), (0, HIDER_X + 4)],
                          loc=(0, 0, BORE_Z), rot=RX, material=steel, verts=24)))
    R.append(smooth(cyl("p_gas_tube", 10.0, GB_X0 - HG_X0 + 8, ((GB_X0 + HG_X0) / 2, 0, BORE_Z - 22), steel, verts=20, rot=RX)))
    gb = slab("p_gas_block", [(GB_X0, BORE_Z - 34), (GB_X1, BORE_Z - 34), (GB_X1, BORE_Z + 14), (GB_X0, BORE_Z + 14)], 28, steel, bevel=1.6)
    R.append(smooth(gb, 35))
    R.append(smooth(cyl("p_gas_plug", 7.5, 12, (GB_X1 + 5, 0, BORE_Z - 22), steel, verts=16, rot=RX)))
    # the front post in its hood, on a tower off the gas block
    R.append(smooth(add_bevel(slab("p_front_tower", [(FRONT_X - 14, BORE_Z + 12), (FRONT_X + 14, BORE_Z + 12), (FRONT_X + 5, SIGHT_Z - 18),
                                                     (FRONT_X - 5, SIGHT_Z - 18)], 12, steel, bevel=0), 1.0), 35))
    R.append(add_bevel(box("p_front_post", (2.4, 2.4, 18), (FRONT_X, 0, SIGHT_Z - 9), steel), 0.3))
    for s in (1, -1):
        R.append(add_bevel(prism(f"p_front_ear{s}", [(FRONT_X - 6, SIGHT_Z - 20), (FRONT_X + 6, SIGHT_Z - 20), (FRONT_X + 4, SIGHT_Z + 5), (FRONT_X - 4, SIGHT_Z + 5)],
                                 2.4, (0, s * 9.0, 0), steel, axis='Y'), 0.4))
    R.append(add_bevel(box("p_front_hood_base", (14, 21, 3), (FRONT_X, 0, SIGHT_Z - 19), steel), 0.5))
    # flash hider: a sleeve with slots round the front half
    hider = lathe("p_hider", [(7.5, HIDER_X), (11.5, HIDER_X), (12.0, HIDER_X + 4), (12.0, MUZZLE_X - 3), (10.8, MUZZLE_X), (8.0, MUZZLE_X), (7.0, HIDER_X + 20), (7.5, HIDER_X)],
                  loc=(0, 0, BORE_Z), rot=RX, material=steel, verts=32)
    for k in range(3):
        boolean(hider, cutter(f"hider_slot{k}", (28, 40, 3.4), (MUZZLE_X - 13, 0, BORE_Z), rot=(k * 60 + 30, 0, 0)))
    R.append(smooth(hider, 40))
    R.append(smooth(cyl("p_hider_bore", 7.8, 1.0, (HIDER_X + 22, 0, BORE_Z), dark, verts=20, rot=RX)))

    # ---------------------------------------------------------------- carrying handle: swung up and to the left, clear of the sights
    R.append(smooth(lathe("p_handle_collar", [(0, 276), (13, 276), (13, 304), (0, 304)], loc=(0, 0, BORE_Z), rot=RX, material=steel, verts=24)))
    a = Vector((292, 8, BORE_Z + 10))
    b = Vector((272, 40, BORE_Z + 34))
    c = Vector((238, 74, BORE_Z + 54))
    arm = [a, a.lerp(b, 0.5), b, b.lerp(c, 0.5), c]
    R.append(tube_x(arm, 5.5, "p_handle_arm", steel))
    d = c + (c - b).normalized() * 6
    e = Vector((170, 118, BORE_Z + 72))
    grip_pts = [d.lerp(e, k / 8) for k in range(9)]
    R.append(tube_x(grip_pts, 11.0, "p_handle_grip", poly, count=14))
    for k in range(6):
        p = d.lerp(e, 0.12 + 0.14 * k)
        t = (e - d).normalized()
        rot = t.to_track_quat('Z', 'Y').to_euler()
        R.append(smooth(cyl(f"p_handle_rib{k}", 12.0, 3.2, tuple(p), poly, verts=14, rot=tuple(math.degrees(v) for v in rot))))

    # ---------------------------------------------------------------- bipod: down from a clamp under the gas block
    clamp_z = BORE_Z - 40
    R.append(smooth(cyl("p_bipod_clamp", 14, 26, (GB_X0 + 24, 0, clamp_z), steel, verts=20, rot=RX)))
    for s in (1, -1):
        top_pt = Vector((GB_X0 + 24, s * 14, clamp_z - 3))
        knee = Vector((GB_X0 + 30, s * 44, clamp_z - 130))
        foot = Vector((GB_X0 + 34, s * 66, clamp_z - 278))
        R.append(tube_x([top_pt.lerp(knee, k / 3) for k in range(4)], 8.5, f"p_bipod_upper{s}", steel))
        R.append(tube_x([knee.lerp(foot, k / 3) for k in range(4)], 5.8, f"p_bipod_lower{s}", steel))
        R.append(smooth(cyl(f"p_bipod_collar{s}", 10, 12, tuple(knee), steel, verts=14,
                            rot=tuple(math.degrees(v) for v in (foot - knee).to_track_quat('Z', 'Y').to_euler()))))
        R.append(add_bevel(box(f"p_bipod_foot{s}", (30, 16, 5), tuple(foot + Vector((0, 0, -3))), steel), 1.0))

    # ---------------------------------------------------------------- stock, its rubber butt plate, a sling loop
    stock = slab("p_stock", [(REC_X0 + 2, 26), (-190, 22), (-336, 30), (-340, 22), (-340, -74), (-326, -80), (-262, -48),
                             (-190, -24), (REC_X0 + 2, -12)], 38, poly, bevel=0)
    boolean(stock, prism("cut_stock_hole", [(-280, 14), (-192, 10), (-192, -8), (-254, -26), (-286, -40)], 60, (0, 0, 0), dark, axis='Y'))
    F.append(smooth(add_bevel(stock, 2.4), 35))
    F.append(add_bevel(box("p_butt_plate", (9, 41, 120), (-344, 0, -19), rubber), 2.5))
    for k in range(8):
        F.append(add_bevel(box(f"p_butt_rib{k}", (3, 39, 4), (-349.5, 0, -70 + k * 14.5), rubber), 0.8))
    F.append(smooth(cyl("p_sling_loop", 5, 42, (-228, 0, -34), steel, verts=14, rot=(90, 0, 0))))

    # ---------------------------------------------------------------- pistol grip (the AK's), stippled polymer
    def grip_section(t):
        d = lerp_table([(0, 52), (0.18, 44), (0.5, 40), (0.85, 43), (1.0, 42)], t)
        w = lerp_table([(0, 26), (0.3, 29), (0.8, 31), (1.0, 30)], t)
        k = 1.0 if t <= 1.0 else max(0.08, 1.0 - ((t - 1.0) / 0.06) ** 2) ** 0.5
        return se_ring(w / 2 * k, d / 2 * k, n=2.3, count=32)

    ts = [i / 20 for i in range(21)] + [1.02, 1.035, 1.048, 1.057, 1.06]
    F.append(loft("p_grip", sweep(line(GRIP_A, GRIP_B), ts, grip_section), stipple, angle=60))

    # ---------------------------------------------------------------- pouch: a soft sack, slumped, strapped and buckled
    def pouch_ring(z):
        t = (POUCH_TOP - z) / (POUCH_TOP - POUCH_BOT)
        bulge = 1.0 + 0.10 * math.sin(math.pi * min(t * 1.2, 1.0))
        end = max(0.05, 1.0 - max(0.0, (t - 0.86) / 0.14) ** 2) ** 0.5
        hx = (POUCH_X1 - POUCH_X0) / 2 * bulge * end
        hy = POUCH_HALF * bulge * end
        cx = (POUCH_X0 + POUCH_X1) / 2 + 3.0 * math.sin(t * 3.0)
        ring = []
        for i, (p, q) in enumerate(se_ring(hx, hy, n=3.0, count=32)):
            sag = 1.5 * math.sin(i * 1.7 + z * 0.09)            # the cloth isn't taut: it hangs in shallow folds
            ring.append(Vector((cx + p, q * (1 + sag * 0.012), z + sag * 0.5)))
        return ring

    zs = [POUCH_TOP - (POUCH_TOP - POUCH_BOT) * k / 18 for k in range(19)]
    P.append(loft("p_pouch", [pouch_ring(z) for z in zs], cloth, angle=70))
    flap = slab("p_pouch_flap", [(POUCH_X0 - 2, POUCH_TOP + 3), (POUCH_X1 + 2, POUCH_TOP + 3), (POUCH_X1 + 4, POUCH_TOP - 10), (POUCH_X0 - 4, POUCH_TOP - 10)],
                2 * POUCH_HALF + 6, cloth, bevel=2.0)
    P.append(smooth(flap, 40))
    for x in (POUCH_X0 + 22, POUCH_X1 - 22):
        P.append(add_bevel(box(f"p_pouch_strap{x}", (12, 2 * POUCH_HALF + 13, 2.6), (x, 0, POUCH_TOP - 11), webbing), 0.5))
        for s in (1, -1):
            P.append(add_bevel(box(f"p_pouch_tab{x}{s}", (14, 2.6, 44), (x, s * (POUCH_HALF + 5.5), POUCH_TOP - 33), webbing), 0.5))
            P.append(add_bevel(box(f"p_pouch_buckle{x}{s}", (17, 3.4, 12), (x, s * (POUCH_HALF + 6.3), POUCH_TOP - 52), rubber), 0.6))

    # ---------------------------------------------------------------- the belt: a round and its link, each a part of its own
    for k, (y, z, ty, tz) in enumerate(belt_points()):
        B = parts[f"Belt{k}"]
        x0 = BELT_X - ROUND_LEN / 2
        # the round: case, shoulder, neck and a copper bullet
        B.append(smooth(lathe(f"p_case{k}", [(0, x0), (4.6, x0), (4.6, x0 + 1.2), (4.1, x0 + 2.4), (4.7, x0 + 3.6), (4.5, x0 + 34), (3.2, x0 + 38), (3.2, x0 + 43.5), (0, x0 + 43.5)],
                              loc=(0, y, z), rot=RX, material=brass, verts=8)))
        B.append(smooth(lathe(f"p_bullet{k}", [(0, x0 + 43.5), (2.9, x0 + 43.5), (2.8, x0 + 50), (1.4, x0 + ROUND_LEN - 0.8), (0, x0 + ROUND_LEN)],
                              loc=(0, y, z), rot=RX, material=copper, verts=8)))
        # the link: two loops round the case, a third between them on the next round's side
        ang = math.degrees(math.atan2(tz, ty))
        for j, lx in enumerate((x0 + 8, x0 + 24)):
            B.append(smooth(lathe(f"p_link{k}{j}", [(4.5, lx - 3.5), (5.6, lx - 3.5), (5.6, lx + 3.5), (4.5, lx + 3.5), (4.5, lx - 3.5)],
                                  loc=(0, y, z), rot=RX, material=steel, verts=8)))
            B.append(box(f"p_link_web{k}{j}", (5, 7.5, 1.0), (lx, y + ty * 4.5 + tz * 4.5, z + tz * 4.5 - ty * 4.5), steel, rot=(ang, 0, 0)))

    # ---------------------------------------------------------------- bolt (seen in the port), charging handle, trigger
    Bc = parts["BoltCarrier"]
    Bc.append(smooth(add_bevel(box("p_carrier", (x1 - x0 + 30, 12, z1 - z0 - 1), ((x0 + x1) / 2 - 8, -REC_HALF + 8.5, (z0 + z1) / 2), steel), 1.0)))
    Bc.append(smooth(cyl("p_bolt_head", 7, 14, (x1 - 3, -REC_HALF + 9, BORE_Z), steel, verts=16, rot=RX)))
    H = parts["ChargingHandle"]
    H.append(add_bevel(box("p_handle_shaft", (16, 12, 5), (HANDLE_X, -REC_HALF - 2, 20), steel), 0.6))
    H.append(smooth(cyl("p_handle_knob", 8, 16, (HANDLE_X, -REC_HALF - 12, 20), steel, verts=16, rot=(90, 0, 0))))
    trig = prism("p_trigger", [(4, -36), (-4, -36), (-4.5, -47), (-2.5, -56), (2.5, -63), (5.5, -62.2), (2.2, -55), (1.2, -47)],
                 6.0, (0, 0, 0), steel, axis='Y')
    parts["Trigger"].append(smooth(add_bevel(trig, 0.5)))
    return parts


# ============================================================================================ output
POINTS = {
    "Muzzle": (MUZZLE_X, 0, BORE_Z),
    "RearSight": (REAR_X, 0, SIGHT_Z),
    "FrontPost": (FRONT_X, 0, SIGHT_Z),
    "EjectionPort": ((PORT[0] + PORT[1]) / 2, -REC_HALF, BORE_Z - 2),
    "ChargingHandle": (HANDLE_X, -REC_HALF - 12, 20),
    "CoverHinge": (HINGE[0], 0, HINGE[1]),
    "CoverFront": (COVER_X1, REC_HALF, REC_TOP + 12),
    "PouchCenter": ((POUCH_X0 + POUCH_X1) / 2, 0, (POUCH_TOP + POUCH_BOT) / 2),
    "TriggerPivot": (1, 0, -36),
    "GripCenter": (-60, 0, -82),
    "Butt": (-352, 0, -19),
    "BipodFoot": (GB_X0 + 34, 66, BORE_Z - 40 - 281),
}


def save_points():
    data = {k: [round(c * MM, 5) for c in v] for k, v in POINTS.items()}
    data["PouchHalfExtents"] = [round(c * MM, 5) for c in ((POUCH_X1 - POUCH_X0) / 2, (POUCH_TOP - POUCH_BOT) / 2, POUCH_HALF)]
    data["BeltPitch"] = round(BELT_PITCH * MM, 5)
    data["BeltCount"] = BELT_COUNT
    data["BoltTravel"] = round(BOLT_TRAVEL * MM, 5)
    data["HandleTravel"] = round(HANDLE_TRAVEL * MM, 5)
    data["Calibre"] = "5.56 x 45 mm, 200-round belt"
    data["Note"] = ("Blender coordinates in metres: +X muzzle, +Y left, +Z up; x = 0 at the trigger; z = 0 the AK's bore "
                    "(the M249's is at BORE_Z), so the AK's grip, trigger and hands fit.")
    path = os.path.join(K.OUT, "m249_points.json")
    with open(path, "w") as f:
        json.dump(data, f, indent=2)
    print(f"[m249] points -> {path}", flush=True)


def look_at(cam, target):
    d = Vector(target) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()


def add_hands():
    """Preview only: the AK's hands and sleeves, as the game draws them."""
    path = os.path.join(K.OUT, "ak47.glb")
    if not os.path.exists(path):
        return []
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    added = [o for o in bpy.data.objects if o not in before]
    keep = []
    for o in added:
        if o.name.split(".")[0] in ("Hand", "TriggerFinger", "Sleeve", "HandLeft", "SleeveLeft"):
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
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.render.image_settings.file_format = 'PNG'
    scene.view_settings.view_transform = 'AgX'
    world = bpy.data.worlds.new("prev")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.62, 0.64, 0.68, 1)
    bg.inputs[1].default_value = 0.45
    sun = bpy.data.lights.new("sun", 'SUN'); sun.energy = 2.6; sun.angle = math.radians(4)
    so = bpy.data.objects.new("sun", sun); scene.collection.objects.link(so)
    so.rotation_euler = (math.radians(35), math.radians(-20), math.radians(30))
    cam_data = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam)
    scene.camera = cam
    views = {
        "left": dict(ortho=1.15, loc=(0.17, 1.5, -0.08), target=(0.17, 0, -0.08), res=(1600, 800), hands=False),
        "right": dict(ortho=1.15, loc=(0.17, -1.5, -0.08), target=(0.17, 0, -0.08), res=(1600, 800), hands=False),
        "three_quarter": dict(lens=50, loc=(0.85, 1.1, 0.5), target=(0.17, 0, -0.04), res=(1400, 900), hands=False),
        "hands_left": dict(ortho=0.8, loc=(0.1, 1.5, -0.05), target=(0.1, 0, -0.05), res=(1400, 900), hands=True),
        "hands_right": dict(ortho=0.8, loc=(0.1, -1.5, -0.05), target=(0.1, 0, -0.05), res=(1400, 900), hands=True),
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
        scene.render.filepath = os.path.join(K.PREVIEWS, f"m249_{name}.png")
        bpy.ops.render.render(write_still=True)
        print(f"[m249] preview {scene.render.filepath}", flush=True)
    for o in [so, cam] + hands:
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
    print(f"[m249] {len(final)} objects, ~{tris} triangles", flush=True)
    save_points()
    K.export("m249", final)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.PROJECT, "Assets", "m249.blend"))
    if "nopreview" not in argv:
        render_previews()


main()
