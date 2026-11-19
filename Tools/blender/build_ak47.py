"""
Builds a classic milled-receiver AK-47 (blued steel, reddish varnished wood furniture, 30-round steel magazine) and
exports it as Assets/Models/ak47.glb. The scene is saved as Assets/ak47.blend (the source), key points for the game
as Assets/Models/ak47_points.json, and preview renders as Tools/previews/models/ak47_*.png.

    blender -b --factory-startup -P Tools/blender/build_ak47.py              (build, export, previews)
    blender -b --factory-startup -P Tools/blender/build_ak47.py -- nopreview (build and export only)

Coordinates (Blender, like the pistol): +X out of the muzzle, +Z up, +Y the gun's left side. The bore axis is the X
axis (y = z = 0); x = 0 is the trigger. Real proportions (overall 880 mm, barrel 415 mm), measured off a side photo.
Everything is modelled in millimetres and scaled to metres at the end.

Exported objects (names are the part names the game can animate):
    Receiver     all static steel (barrel, sights, gas block and tube, receiver, dust cover, trigger guard, butt plate)
    Furniture    the wood: stock, pistol grip, upper and lower handguards (texture set tex_gun_wood)
    Magazine     the curved steel magazine (rocks in about MagPivot, see ak47_points.json)
    BoltCarrier  the carrier and its charging handle (slides back along -X by BoltTravel)
    Trigger      pivots about TriggerPivot
    Selector     the safety/selector lever on the right side, pivots about SelectorPivot (axis Y)
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
WOOD_TILE_MM = K.TILE["gun_wood"] * 1000.0
STEEL_TILE_MM = K.TILE["gunmetal"] * 1000.0

# Key dimensions (mm)
MUZZLE_X = 538.0
REC_X0, REC_X1 = -97.0, 171.0        # receiver rear / front
REC_TOP, REC_BOT, REC_HALF = 20.0, -40.0, 15.0
MAG_P0, MAG_P1, MAG_P2 = Vector((91, 0, -12)), Vector((96, 0, -90)), Vector((176, 0, -188))
GRIP_A, GRIP_B = Vector((-46, 0, -30)), Vector((-77, 0, -138))
SIGHT_Z = 44.0


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


# Cutters carry the steel material: the faces a boolean cuts take the cutter's material.
def cutter(name, size, loc, rot=(0, 0, 0)):
    return box("cut_" + name, size, loc, mat("gunmetal", None, 0.35, 0.85), rot=rot)


def cutter_cyl(name, r, depth, loc, rot=(0, 0, 0), verts=20):
    return cyl("cut_" + name, r, depth, loc, mat("gunmetal", None, 0.35, 0.85), verts=verts, rot=rot)


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


def loft(name, rings, material, tile_mm=None, caps=(True, True), angle=50.0):
    """Skins rings of 3D points (equal counts). With tile_mm the sides get UVs in texture repeats: u along the loft
    (so wood grain runs lengthwise), v around the section (seam at index 0); caps are box-projected later."""
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in ring] for ring in rings]
    n = len(rings[0])
    uvl = bm.loops.layers.uv.verify()
    # u: cumulative centroid distance; v: cumulative arc length round each ring
    cents = [sum((Vector(p) for p in r), Vector()) / n for r in rings]
    us = [0.0]
    for j in range(1, len(rings)):
        us.append(us[-1] + (cents[j] - cents[j - 1]).length)
    arcs = []
    for r in rings:
        a = [0.0]
        for i in range(1, n + 1):
            a.append(a[-1] + (Vector(r[i % n]) - Vector(r[i - 1])).length)
        arcs.append(a)
    # normalise round each ring so the grain follows the shape (it fans with the stock instead of slanting)
    mean_perim = sum(a[-1] for a in arcs) / len(arcs)
    arcs = [[x / a[-1] * mean_perim for x in a] for a in arcs]
    for j in range(len(vs) - 1):
        for i in range(n):
            k = (i + 1) % n
            f = bm.faces.new((vs[j][i], vs[j][k], vs[j + 1][k], vs[j + 1][i]))
            if tile_mm:
                for loop, (jj, ii) in zip(f.loops, ((j, i), (j, i + 1), (j + 1, i + 1), (j + 1, i))):
                    loop[uvl].uv = (us[jj] / tile_mm, arcs[jj][ii] / tile_mm)
    cap_faces = []
    if caps[0]:
        cap_faces.append(bm.faces.new(vs[0]))
    if caps[1]:
        cap_faces.append(bm.faces.new(list(reversed(vs[-1]))))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if tile_mm:
        for f in cap_faces:
            nrm = f.normal
            ax = max(range(3), key=lambda i: abs(nrm[i]))
            for loop in f.loops:
                p = loop.vert.co
                u, v = (p.y, p.z) if ax == 0 else ((p.x, p.z) if ax == 1 else (p.x, p.y))
                loop[uvl].uv = (u / tile_mm, v / tile_mm)
    o = K._new_obj(name, bm, material)
    if tile_mm:
        o["uv"] = "keep"
    return smooth(o, angle)


def bezier(t, p0=MAG_P0, p1=MAG_P1, p2=MAG_P2):
    c = (1 - t) ** 2 * p0 + 2 * (1 - t) * t * p1 + t * t * p2
    d = 2 * (1 - t) * (p1 - p0) + 2 * t * (p2 - p1)
    return c, d.normalized()


def line(a, b):
    def f(t):
        return a.lerp(b, t), (b - a).normalized()
    return f


def sweep(path, ts, section, offset=(0.0, 0.0)):
    """Rings along a path in the XZ plane. section(t) -> [(p, q)], p along +Y, q along the in-plane normal."""
    rings = []
    side = Vector((0, 1, 0))
    for t in ts:
        c, tan = path(t)
        nrm = side.cross(tan).normalized()
        rings.append([c + side * (p + offset[0]) + nrm * (q + offset[1]) for p, q in section(t)])
    return rings


def lerp_table(table, x):
    """Piecewise-linear lookup in [(x, value), ...] sorted by x."""
    if x <= table[0][0]:
        return table[0][1]
    for (x0, v0), (x1, v1) in zip(table, table[1:]):
        if x <= x1:
            return v0 + (v1 - v0) * (x - x0) / (x1 - x0)
    return table[-1][1]


def torus(name, center, R, r, material, plane='XZ', seg=24, ring=8):
    bm = bmesh.new()
    rows = []
    for i in range(seg):
        a = math.tau * i / seg
        row = []
        for j in range(ring):
            b = math.tau * j / ring
            rr = R + r * math.cos(b)
            u, w, h = rr * math.cos(a), rr * math.sin(a), r * math.sin(b)
            p = (u, h, w) if plane == 'XZ' else (u, w, h)
            row.append(bm.verts.new(Vector(center) + Vector(p)))
        rows.append(row)
    for i in range(seg):
        for j in range(ring):
            bm.faces.new((rows[i][j], rows[(i + 1) % seg][j], rows[(i + 1) % seg][(j + 1) % ring], rows[i][(j + 1) % ring]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return smooth(K._new_obj(name, bm, material), 60)


# ============================================================================================ parts
def build():
    steel = mat("gunmetal", None, 0.35, 0.85)
    wood = mat("tex_gun_wood", None, 0.45, 0.0)
    dark = mat("flat_bore", (0.025, 0.025, 0.025), 0.7, 0.4)
    parts = {"Receiver": [], "Furniture": [], "Magazine": [], "BoltCarrier": [], "Trigger": [], "Selector": []}
    R, F = parts["Receiver"], parts["Furniture"]

    # ---------------------------------------------------------------- barrel, muzzle, front sight
    R.append(smooth(lathe("p_barrel", [(0, 160), (8.4, 160), (8.4, 300), (7.7, 312), (7.7, 524), (0, 524)],
                          rot=RX, material=steel, verts=28)))
    R.append(smooth(lathe("p_muzzle_nut", [(3.9, 522), (9.4, 522), (9.4, MUZZLE_X - 2.5), (8.6, MUZZLE_X),
                                           (3.9, MUZZLE_X), (3.9, 522)], rot=RX, material=steel, verts=28)))
    R.append(cyl("p_bore", 4.2, 1.0, (MUZZLE_X - 12, 0, 0), dark, verts=16, rot=RX))
    R.append(smooth(cyl("p_fs_collar", 12.0, 34, (510, 0, 0), steel, verts=28, rot=RX)))
    tower = prism("p_fs_tower", [(494, 4), (527, 4), (523, 22), (517, 30), (503, 30), (497, 21)], 14, (0, 0, 0), steel, axis='Y')
    R.append(smooth(add_bevel(tower, 0.8)))
    for side in (1, -1):
        ear = prism(f"p_fs_ear{side}", [(500, 18), (521, 18), (519, 41), (515, 47.5), (507, 47.5), (502, 41)], 2.6,
                    (0, side * 6.6, 0), steel, axis='Y')
        R.append(smooth(add_bevel(ear, 0.5)))
    R.append(smooth(cyl("p_fs_post", 1.3, 16, (510, 0, SIGHT_Z - 8), steel, verts=12)))
    R.append(smooth(add_bevel(box("p_bayonet_lug", (24, 8, 7), (509, 0, -14.5), steel), 0.6)))
    # cleaning rod under the barrel, its head held in the front sight block
    R.append(smooth(cyl("p_cleaning_rod", 3.0, 190, (420, 0, -14.5), steel, verts=12, rot=RX)))
    R.append(smooth(cyl("p_rod_head", 4.4, 7, (526, 0, -14.5), steel, verts=14, rot=RX)))

    # ---------------------------------------------------------------- gas block and gas tube
    R.append(smooth(cyl("p_gas_collar", 11.6, 36, (417, 0, 0), steel, verts=28, rot=RX)))
    R.append(smooth(add_bevel(box("p_gas_web", (33, 12, 22), (417, 0, 11), steel), 0.8)))
    R.append(smooth(lathe("p_gas_cap", [(0, 396), (10.2, 396), (10.2, 431), (9.4, 436), (5.5, 439), (0, 439)],
                          loc=(0, 0, 22), rot=RX, material=steel, verts=24)))
    tube = smooth(lathe("p_gas_tube", [(0, 205), (8.6, 205), (8.6, 398), (0, 398)], loc=(0, 0, 22), rot=RX,
                        material=steel, verts=24))
    for i, x in enumerate((346, 358, 370, 382)):
        boolean(tube, cutter_cyl(f"vent{i}", 2.1, 30, (x, 0, 22), rot=(90, 0, 0), verts=14))
    R.append(tube)
    R.append(cyl("p_gas_core", 5.6, 190, (302, 0, 22), dark, verts=12, rot=RX))
    R.append(smooth(cyl("p_gas_tube_ring", 11.2, 10, (323, 0, 22), steel, verts=24, rot=RX)))

    # ---------------------------------------------------------------- rear sight
    rs = prism("p_rear_sight_block", [(151, -12), (217, -12), (217, 25), (209, 30.5), (165, 38.0), (151, 38.0)], 25, (0, 0, 0), steel, axis='Y')
    R.append(smooth(add_bevel(rs, 1.0)))
    leaf = prism("p_rear_leaf", [(208, 32), (210, 34.5), (162, 42.6), (160, 40.4)], 13, (0, 0, 0), steel, axis='Y')
    R.append(smooth(add_bevel(leaf, 0.3)))
    tab = box("p_rear_notch", (3.2, 13, 5.0), (161.2, 0, SIGHT_Z - 1.6), steel)
    boolean(tab, cutter("notch", (6, 2.4, 3.2), (161.2, 0, SIGHT_Z + 0.6)))
    R.append(smooth(tab))
    R.append(smooth(add_bevel(box("p_rear_slider", (8, 16, 5.5), (186, 0, 38.0), steel, rot=(0, 9.5, 0)), 0.6)))
    R.append(smooth(cyl("p_rear_hinge", 2.6, 26, (207, 0, 31.5), steel, verts=12, rot=(90, 0, 0))))

    # ---------------------------------------------------------------- receiver (milled)
    rec = box("p_receiver", (REC_X1 - REC_X0, 2 * REC_HALF, REC_TOP - REC_BOT),
              ((REC_X0 + REC_X1) / 2, 0, (REC_TOP + REC_BOT) / 2), steel)
    for s in (1, -1):
        y = s * (REC_HALF + 3.5)
        boolean(rec, cutter(f"lighten{s}", (82, 7, 20), (125, y - s * 2.4, -17)))       # the milled lightening cut
        boolean(rec, cutter(f"groove{s}", (236, 7, 5.5), (32, y - s * 0.9, 5.5)))        # long side groove
    boolean(rec, cutter("port", (78, 12, 16.5), (79, -REC_HALF - 1.0, 12.4)))            # ejection port (right)
    boolean(rec, cutter("handle_slot", (40, 12, 6.5), (134, -REC_HALF - 1.0, 13.6)))
    boolean(rec, cutter("magwell", (66, 27.2, 30), (90.5, 0, -40)))
    boolean(rec, cutter("trigger_slot", (14, 7, 12), (0, 0, -40)))
    R.append(smooth(add_bevel(rec, 0.7)))
    R.append(cyl("p_port_shadow", 5.0, 70, (79, -4, 8), dark, verts=10, rot=RX))
    # pins and rivets, through both sides
    for i, (x, z) in enumerate(((49, -27), (34, -26), (-5, -32), (-62, -30), (150, -28))):
        R.append(smooth(cyl(f"p_pin{i}", 2.5 if i < 3 else 2.0, 2 * REC_HALF + 1.4, (x, 0, z), steel, verts=14, rot=(90, 0, 0))))

    # dust cover: an arched lid whose back curves down onto the receiver
    def cover_top(x):
        if x > -45:
            return 32.0
        u = min(1.0, (-45 - x) / 50.0)
        return 32.0 - 12.0 * (u * u * (3 - 2 * u))

    rings = []
    for x in [148, 146] + list(range(140, -84, -8)) + [-86, -90, -93, -95]:
        h = max(1.2, cover_top(x) - 19.0)
        ring = []
        for p, q in se_ring(15.7, h, 3.6, n=2.4, count=32):
            ring.append(Vector((x, p, 19.0 + q)))
        rings.append(ring)
    R.append(loft("p_dust_cover", rings, steel, angle=55))
    R.append(smooth(add_bevel(box("p_spring_button", (5, 9, 6), (-96, 0, 21.5), steel), 0.8)))
    # front trunnion band and the handguard retainers
    lower_bot = [(181, -34), (192, -35.5), (205, -33), (225, -26), (250, -23), (300, -21.5), (324, -21)]
    lower_w = [(181, 20.5), (195, 22), (212, 21), (235, 18.5), (300, 17.5), (324, 17)]

    def lower_ring(x, grow=0.0, count=32):
        b = lerp_table(lower_bot, x)
        a = lerp_table(lower_w, x) + grow
        top = 10.0 + grow * 0.5
        return [Vector((x, p, 8.0 + (q if q > 0 else q))) for p, q in se_ring(a, top - 8.0, 8.0 - b + grow, n=3.2, count=count)]

    R.append(loft("p_rear_retainer", [lower_ring(171, 1.2), lower_ring(182, 1.2)], steel, angle=55))
    R.append(loft("p_front_retainer", [lower_ring(316, 1.0), lower_ring(329, 1.0)], steel, angle=55))

    # trigger guard (a flat strip) and the magazine release paddle in front of it
    guard_path = [Vector((50, 0, -41)), Vector((49.5, 0, -63)), Vector((45, 0, -70.5)), Vector((37, 0, -73)),
                  Vector((-6, 0, -73)), Vector((-14, 0, -70)), Vector((-18.5, 0, -62)), Vector((-20, 0, -41))]
    segs = []
    for a, b in zip(guard_path, guard_path[1:]):
        for k in range(6):
            segs.append(a.lerp(b, k / 6))
    segs.append(guard_path[-1])
    # smooth the corners a little (Chaikin once)
    sm = [segs[0]]
    for a, b in zip(segs, segs[1:]):
        sm += [a.lerp(b, 0.25), a.lerp(b, 0.75)]
    sm.append(segs[-1])
    rings = []
    for i, c in enumerate(sm):
        t = (sm[min(i + 1, len(sm) - 1)] - sm[max(i - 1, 0)]).normalized()
        nrm = Vector((0, 1, 0)).cross(t).normalized()
        rings.append([c + Vector((0, p, 0)) + nrm * q for p, q in se_ring(5.0, 1.7, n=5, count=16)])
    R.append(loft("p_trigger_guard", rings, steel, angle=50))
    rel = prism("p_mag_release", [(51, -40), (58, -40), (59, -50), (57.5, -60), (53, -60.5), (52, -50)], 12, (0, 0, 0), steel, axis='Y')
    R.append(smooth(add_bevel(rel, 0.6)))

    # ---------------------------------------------------------------- stock, rear trunnion socket, butt plate
    def stock_dims(x):
        zt = lerp_table([(-343, -10.5), (-97, -0.5)], x)
        zb = lerp_table([(-333, -139), (-100, -40)], x)
        w = lerp_table([(-333, 42), (-250, 41), (-160, 35), (-110, 30.5), (-97, 30)], x)
        return zt, zb, w

    def stock_ring(x, grow=0.0, slant=0.0, count=40):
        zt, zb, w = stock_dims(x)
        zc = (zt + zb) / 2 - 2.0
        ring = []
        for p, q in se_ring(w / 2 + grow, zt - zc + grow, zc - zb + grow, n=2.7, count=count):
            z = zc + q
            u = (z - zb) / (zt - zb)
            ring.append(Vector((x - slant * u, p, z)))
        return ring

    xs = [-96, -104] + list(range(-112, -321, -12)) + [-321]
    rings = [stock_ring(x) for x in xs] + [stock_ring(-323, slant=4.0), stock_ring(-325, slant=8.0)]
    F.append(loft("p_stock", rings, wood, tile_mm=WOOD_TILE_MM, angle=60))
    R.append(loft("p_stock_socket", [stock_ring(-95, 1.3), stock_ring(-104, 1.3), stock_ring(-110, 0.5)], steel, angle=55))
    R.append(loft("p_butt_plate", [stock_ring(-324.5, 0.6, slant=8.0), stock_ring(-329.5, 0.6, slant=9.5),
                                   stock_ring(-332, -0.9, slant=10.0)], steel, angle=55))
    # sling swivel on the stock's left side
    zt, zb, w = stock_dims(-146)
    zc = (zt + zb) / 2 - 2
    R.append(smooth(add_bevel(box("p_swivel_plate", (26, 2.0, 11), (-146, w / 2 + 0.4, zc), steel), 0.5)))
    R.append(smooth(add_bevel(box("p_swivel_lug", (6, 4.5, 6), (-146, w / 2 + 2.5, zc - 4), steel), 0.5)))
    R.append(torus("p_swivel_loop", (-146, w / 2 + 4.2, zc - 13), 9.0, 1.7, steel, plane='XZ'))

    # ---------------------------------------------------------------- pistol grip
    def grip_section(t):
        d = lerp_table([(0, 52), (0.18, 44), (0.5, 40), (0.85, 43), (1.0, 42)], t)
        w = lerp_table([(0, 26), (0.3, 29), (0.8, 31), (1.0, 30)], t)
        k = 1.0 if t <= 1.0 else max(0.08, 1.0 - ((t - 1.0) / 0.06) ** 2) ** 0.5
        return se_ring(w / 2 * k, d / 2 * k, n=2.3, count=32)

    ts = [i / 20 for i in range(21)] + [1.02, 1.035, 1.048, 1.057, 1.06]
    F.append(loft("p_grip", sweep(line(GRIP_A, GRIP_B), ts, grip_section), wood, tile_mm=WOOD_TILE_MM, angle=60))

    # ---------------------------------------------------------------- handguards (wood)
    rings = [lower_ring(x) for x in (181, 186) + tuple(range(192, 316, 8)) + (318, 322)]
    F.append(loft("p_lower_handguard", rings, wood, tile_mm=WOOD_TILE_MM, angle=60))

    def upper_ring(x):
        k = lerp_table([(213, 0.94), (217, 1.0), (308, 1.0), (318, 0.70)], x)
        return [Vector((x, p, 22.0 + q)) for p, q in se_ring(16.0 * k, 15.0 * k, 10.0 * k, n=2.6, count=32)]

    rings = [upper_ring(x) for x in (216, 219) + tuple(range(224, 308, 8)) + (308, 312, 316, 319)]
    F.append(loft("p_upper_handguard", rings, wood, tile_mm=WOOD_TILE_MM, angle=60))

    # ---------------------------------------------------------------- magazine
    M = parts["Magazine"]

    def mag_section(t):
        d = lerp_table([(0, 60), (1, 64)], t)
        w = lerp_table([(0, 27), (0.3, 27.5), (1, 26.5)], t)
        return se_ring(w / 2, d / 2, n=6.0, count=40)

    ts = [i / 24 for i in range(25)]
    M.append(loft("p_mag_body", sweep(bezier, ts, mag_section), steel, angle=40))
    for s in (1, -1):
        for q in (-19, -7, 5, 17):
            def rib(t, q=q, s=s):
                w = lerp_table([(0, 27), (0.3, 27.5), (1, 26.5)], t)
                return [(s * (w / 2 - 0.6) + p, q + qq) for p, qq in se_ring(1.3, 2.4, n=2.2, count=10)]
            rts = [0.27 + 0.66 * i / 14 for i in range(15)]
            M.append(loft(f"p_mag_rib{s}{q}", sweep(bezier, rts, rib), steel, angle=70))
    end, tan = bezier(1.0)

    def floor(t):
        return se_ring(15.2, 34.0, n=6.0, count=40)
    M.append(loft("p_mag_floor", sweep(line(end - tan * 4.0, end + tan * 2.4), [0, 0.5, 1], floor), steel, angle=40))
    # the lug at the front that hooks into the receiver
    M.append(smooth(add_bevel(box("p_mag_lug", (6, 14, 8), (122.5, 0, -35), steel), 0.6)))

    # ---------------------------------------------------------------- bolt carrier and charging handle (right side)
    B = parts["BoltCarrier"]
    B.append(smooth(add_bevel(box("p_carrier", (118, 9, 14), (92, -9.5, 11.5), steel), 0.6)))
    B.append(smooth(cyl("p_handle_stem", 3.6, 21, (139, -25, 14.5), steel, verts=14, rot=(90, 0, 0))))
    B.append(smooth(lathe("p_handle_knob", [(0, 0), (5.2, 0), (6.4, 3), (6.4, 10), (5.0, 13), (0, 13.5)],
                          loc=(139, -33, 14.5), rot=(90, 0, 0), material=steel, verts=20)))

    # ---------------------------------------------------------------- trigger and selector
    trig = prism("p_trigger", [(4, -36), (-4, -36), (-4.5, -47), (-2.5, -56), (2.5, -63), (5.5, -62.2), (2.2, -55), (1.2, -47)],
                 6.0, (0, 0, 0), steel, axis='Y')
    parts["Trigger"].append(smooth(add_bevel(trig, 0.5)))
    sel = prism("p_selector", [(-87, 2), (-73, 0.5), (60, 3.5), (70, 4), (73, 18.5), (60, 19.0), (-73, 15.5), (-87, 13.5)],
                2.0, (0, -REC_HALF - 1.9, 0), steel, axis='Y')
    parts["Selector"].append(smooth(add_bevel(sel, 0.4)))
    parts["Selector"].append(smooth(cyl("p_selector_hub", 6.0, 3.4, (-80, -REC_HALF - 1.7, 8.5), steel, verts=18, rot=(90, 0, 0))))
    parts["Selector"].append(smooth(add_bevel(box("p_selector_tab", (7, 5, 8), (68, -REC_HALF - 4.2, 11.0), steel, rot=(0, 0, 0)), 0.6)))
    return parts


# ============================================================================================ output
POINTS = {
    "Muzzle": (MUZZLE_X, 0, 0),
    "FrontSightPost": (510, 0, SIGHT_Z),
    "RearSightNotch": (161.2, 0, SIGHT_Z),
    "EjectionPort": (79, -16, 12),
    "ChargingHandle": (139, -40, 14.5),
    "MagTop": (91, 0, -38),
    "MagPivot": (122.5, 0, -38),
    "TriggerPivot": (1, 0, -36),
    "SelectorPivot": (-80, -17, 8.5),
    "GripCenter": (-60, 0, -82),
    "SupportHand": (255, 0, -12),
    "Butt": (-338, 0, -75),
}


def save_points():
    data = {k: [round(c * MM, 5) for c in v] for k, v in POINTS.items()}
    _, tan = bezier(0.0)
    data["MagAxis"] = [round(c, 5) for c in tan]
    data["BoltTravel"] = 0.110
    data["Note"] = "Blender coordinates in metres: +X muzzle, +Y left, +Z up; bore on the X axis; x = 0 at the trigger."
    path = os.path.join(K.OUT, "ak47_points.json")
    with open(path, "w") as f:
        json.dump(data, f, indent=2)
    print(f"[ak47] points -> {path}", flush=True)


def look_at(cam, target):
    d = Vector(target) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()


def render_previews(objs):
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
    fo.location = (0.2, -0.9, 0.4); look_at(fo, (0.1, 0, 0))
    cam_data = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam)
    scene.camera = cam
    views = {
        "left": dict(ortho=0.95, loc=(0.105, 1.5, -0.05), target=(0.105, 0, -0.05), res=(1600, 800)),
        "right": dict(ortho=0.95, loc=(0.105, -1.5, -0.05), target=(0.105, 0, -0.05), res=(1600, 800)),
        "three_quarter": dict(lens=50, loc=(0.75, -0.95, 0.42), target=(0.1, 0, -0.05), res=(1400, 900)),
        "receiver": dict(lens=50, loc=(0.16, -0.42, 0.10), target=(0.06, 0, -0.02), res=(1400, 900)),
        "fpv": dict(lens=32, loc=(-0.30, 0.10, 0.085), target=(0.60, -0.03, -0.01), res=(1400, 900)),
        "front": dict(lens=50, loc=(1.05, 0.30, 0.10), target=(0.45, 0, 0.0), res=(1400, 900)),
    }
    os.makedirs(K.PREVIEWS, exist_ok=True)
    for name, v in views.items():
        if "ortho" in v:
            cam_data.type = 'ORTHO'; cam_data.ortho_scale = v["ortho"]
        else:
            cam_data.type = 'PERSP'; cam_data.lens = v["lens"]
        cam_data.clip_start = 0.01
        cam.location = v["loc"]
        look_at(cam, v["target"])
        scene.render.resolution_x, scene.render.resolution_y = v["res"]
        scene.render.filepath = os.path.join(K.PREVIEWS, f"ak47_{name}.png")
        bpy.ops.render.render(write_still=True)
        print(f"[ak47] preview {scene.render.filepath}", flush=True)
    for o in (so, fo, cam):
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
    print(f"[ak47] {len(final)} objects, ~{tris} triangles", flush=True)
    save_points()
    K.export("ak47", final)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.PROJECT, "Assets", "ak47.blend"))
    if "nopreview" not in argv:
        render_previews(final)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.PROJECT, "Assets", "ak47.blend"))


main()
