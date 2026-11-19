"""
Builds a pump-action 12 gauge shotgun (blued steel, varnished wood stock and pump, black rubber recoil pad) and
exports it as Assets/Models/shotgun.glb. The scene is saved as Assets/shotgun.blend (the source), key points for the
game as Assets/Models/shotgun_points.json, and preview renders as Tools/previews/models/shotgun_*.png.

    blender -b --factory-startup -P Tools/blender/build_shotgun.py              (build, export, previews)
    blender -b --factory-startup -P Tools/blender/build_shotgun.py -- nopreview (build and export only)

Coordinates (Blender, like the AK-47): +X out of the muzzle, +Z up, +Y the gun's left side. The bore axis is the X
axis (y = z = 0); x = 0 is the trigger. Overall about 997 mm with a 508 mm barrel. Modelled in millimetres and scaled
to metres at the end.

Exported objects (names are the part names the game can animate):
    Receiver     all static steel (barrel, rib and bead, magazine tube, receiver, trigger guard, safety)
    Furniture    the wood stock and pistol grip (texture set tex_gun_wood) and the rubber recoil pad
    Pump         the wooden forend and its action bars (slides back along -X by PumpTravel)
    Bolt         the bolt seen through the ejection port (moves with the pump)
    Trigger      pivots about TriggerPivot
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

# Key dimensions (mm)
MUZZLE_X = 640.0                     # 508 mm (20 in) of barrel ahead of the receiver: the shortest that takes a 7-shell tube
BARREL_R = 10.5
REC_X0, REC_X1 = -82.0, 132.0        # receiver rear / front
REC_TOP, REC_BOT, REC_HALF = 18.0, -40.0, 15.0     # bottom and width as the AK's (its hands fit)
TUBE_Z, TUBE_R, TUBE_X1 = -24.0, 10.5, 616.0   # magazine tube under the barrel (its cap flush with the muzzle)
RIB_TOP = 16.5
SIGHT_Z = 20.0                       # the line over the bead and along the receiver's top groove
PUMP_X0, PUMP_X1 = 208.0, 392.0
PUMP_TRAVEL = 90.0
# The pistol grip, trigger and guard are the AK-47's exactly, and the pump's section under the left hand is the AK's
# lower handguard's (shifted by LEFT_HAND_SHIFT), so the game can reuse the AK's hands (see Game/Shotgun.cs).
GRIP_A, GRIP_B = Vector((-46, 0, -30)), Vector((-77, 0, -138))
LEFT_HAND_SHIFT = (45.0, 0.0, -18.4)


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
    (so wood grain runs lengthwise), v around the section (seam at index 0); caps are box-projected."""
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in ring] for ring in rings]
    n = len(rings[0])
    uvl = bm.loops.layers.uv.verify()
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


def sphere(name, center, r, material, seg=16):
    prof = [(r * math.sin(math.pi * i / seg), -r * math.cos(math.pi * i / seg)) for i in range(seg + 1)]
    return smooth(lathe(name, prof, loc=center, material=material, verts=seg), 60)


# ============================================================================================ parts
def build():
    steel = mat("gunmetal", None, 0.35, 0.85)
    wood = mat("tex_gun_wood", None, 0.45, 0.0)
    dark = mat("flat_bore", (0.025, 0.025, 0.025), 0.7, 0.4)
    rubber = mat("flat_rubber", (0.035, 0.035, 0.035), 0.85, 0.0)
    brass = mat("flat_brass", (0.80, 0.62, 0.30), 0.3, 1.0)
    parts = {"Receiver": [], "Furniture": [], "Pump": [], "Bolt": [], "Trigger": []}
    R, F, P = parts["Receiver"], parts["Furniture"], parts["Pump"]

    # ---------------------------------------------------------------- barrel, muzzle, rib and bead
    R.append(smooth(lathe("p_barrel", [(0, REC_X1 - 6), (11.6, REC_X1 - 6), (11.6, REC_X1 + 22), (BARREL_R, REC_X1 + 30),
                                       (BARREL_R, MUZZLE_X - 2), (BARREL_R - 0.8, MUZZLE_X), (0, MUZZLE_X)],
                          rot=RX, material=steel, verts=32)))
    R.append(cyl("p_bore", 9.3, 1.0, (MUZZLE_X - 14, 0, 0), dark, verts=24, rot=RX))
    # ventilated rib: a flat strip on posts along the top of the barrel
    rib_x0, rib_x1 = REC_X1 + 4, MUZZLE_X - 4
    R.append(smooth(add_bevel(box("p_rib", (rib_x1 - rib_x0, 7.0, 2.0), ((rib_x0 + rib_x1) / 2, 0, RIB_TOP - 1.0), steel), 0.4)))
    n_posts = 16
    for i in range(n_posts + 1):
        x = rib_x0 + 6 + (rib_x1 - rib_x0 - 12) * i / n_posts
        R.append(add_bevel(box(f"p_rib_post{i}", (6.0, 4.0, RIB_TOP - BARREL_R + 0.5), (x, 0, (RIB_TOP + BARREL_R) / 2 - 0.5), steel), 0.3))
    R.append(sphere("p_bead", (MUZZLE_X - 8, 0, RIB_TOP + 1.6), 2.2, brass))

    # ---------------------------------------------------------------- magazine tube, cap and barrel clamp
    R.append(smooth(lathe("p_mag_tube", [(0, REC_X1 - 4), (TUBE_R, REC_X1 - 4), (TUBE_R, TUBE_X1), (0, TUBE_X1)],
                          loc=(0, 0, TUBE_Z), rot=RX, material=steel, verts=28)))
    R.append(smooth(lathe("p_mag_cap", [(0, TUBE_X1 - 1), (11.8, TUBE_X1 - 1), (12.2, TUBE_X1 + 14), (11.0, TUBE_X1 + 20),
                                        (7.0, TUBE_X1 + 23), (0, TUBE_X1 + 23.5)],
                          loc=(0, 0, TUBE_Z), rot=RX, material=steel, verts=28)))
    for i in range(10):     # knurling on the cap: shallow ribs round it
        a = math.tau * i / 10
        R.append(add_bevel(box(f"p_cap_knurl{i}", (12, 1.6, 1.4), (TUBE_X1 + 8, 12.1 * math.cos(a), TUBE_Z + 12.1 * math.sin(a)),
                               steel, rot=(math.degrees(a), 0, 0)), 0.3))
    # the barrel's clamp ring round the tube, just behind the cap
    clamp = prism("p_barrel_clamp", [(-13, TUBE_Z - 12.5), (13, TUBE_Z - 12.5), (13, -4), (8, 11.5), (-8, 11.5), (-13, -4)],
                  16, (TUBE_X1 - 22, 0, 0), steel, axis='X')
    R.append(smooth(add_bevel(clamp, 1.0)))
    R.append(smooth(cyl("p_clamp_screw", 3.0, 30, (TUBE_X1 - 22, 0, -11), steel, verts=14, rot=(90, 0, 0))))

    # ---------------------------------------------------------------- receiver
    rec = box("p_receiver", (REC_X1 - REC_X0, 2 * REC_HALF, REC_TOP - REC_BOT),
              ((REC_X0 + REC_X1) / 2, 0, (REC_TOP + REC_BOT) / 2), steel)
    boolean(rec, cutter("port", (66, 12, 22), (62, -REC_HALF - 1.0, 3)))                    # ejection port (right)
    boolean(rec, cutter("loading_port", (62, 22, 12), (92, 0, REC_BOT)))                     # loading port underneath
    boolean(rec, cutter("trigger_slot", (14, 8, 12), (0, 0, REC_BOT)))
    boolean(rec, cutter("sight_groove", (REC_X1 - REC_X0 + 4, 3.0, 2.4), ((REC_X0 + REC_X1) / 2, 0, REC_TOP)))
    # the rear of the receiver slopes down into the stock
    slope = prism("cut_rear_slope", [(REC_X0 - 5, 4.5), (REC_X0 + 34, REC_TOP - 0.2), (REC_X0 + 34, REC_TOP + 10),
                                     (REC_X0 - 5, REC_TOP + 10)], 2 * REC_HALF + 10, (0, 0, 0), steel, axis='Y')
    boolean(rec, slope)
    for s in (1, -1):         # shallow flats along each side
        boolean(rec, cutter(f"flat{s}", (REC_X1 - REC_X0 - 30, 6, 16), ((REC_X0 + REC_X1) / 2 + 8, s * (REC_HALF + 2.4), -24)))
    R.append(smooth(add_bevel(rec, 1.2)))
    R.append(cyl("p_port_shadow", 6.0, 60, (62, -2, 3), dark, verts=12, rot=RX))
    R.append(cyl("p_loading_shadow", 8.0, 56, (92, 0, REC_BOT + 6), dark, verts=12, rot=RX))
    for i, (x, z) in enumerate(((-30, -28), (40, -30), (124, -30))):      # trigger-group pins
        R.append(smooth(cyl(f"p_pin{i}", 2.6, 2 * REC_HALF + 1.2, (x, 0, z), steel, verts=14, rot=(90, 0, 0))))
    # cross-bolt safety just in front of the trigger guard
    R.append(smooth(cyl("p_safety", 3.6, 2 * REC_HALF + 4, (58, 0, -34), steel, verts=16, rot=(90, 0, 0))))
    R.append(cyl("p_safety_band", 3.7, 1.2, (58, REC_HALF + 1.4, -34), mat("flat_red", (0.7, 0.05, 0.03), 0.5), verts=16, rot=(90, 0, 0)))

    # trigger guard: a flat strip from the front of the trigger slot round to the grip
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
        rings.append([c + Vector((0, p, 0)) + nrm * q for p, q in se_ring(5.0, 1.7, n=5, count=16)])
    R.append(loft("p_trigger_guard", rings, steel, angle=50))

    # ---------------------------------------------------------------- stock and recoil pad
    def stock_dims(x):
        zt = lerp_table([(-345, -42), (-160, -14), (-82, 4)], x)     # the comb drops well below the rib: the cheek, not the eye
        zb = lerp_table([(-335, -142), (-140, -66), (-84, -40)], x)
        w = lerp_table([(-335, 42), (-250, 41), (-160, 35), (-100, 31), (-82, 31)], x)
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

    xs = [-80, -88] + list(range(-96, -320, -12)) + [-320]
    rings = [stock_ring(x) for x in xs] + [stock_ring(-322, slant=4.0), stock_ring(-324, slant=8.0)]
    F.append(loft("p_stock", rings, wood, tile_mm=WOOD_TILE_MM, angle=60))
    F.append(loft("p_recoil_pad", [stock_ring(-323.5, 0.4, slant=8.0), stock_ring(-336, 0.8, slant=10.0),
                                   stock_ring(-344, 0.2, slant=11.0), stock_ring(-346, -2.5, slant=11.5)], rubber, angle=55))
    F.append(loft("p_pad_spacer", [stock_ring(-322.5, 0.6, slant=7.6), stock_ring(-325, 0.6, slant=8.2)],
                  mat("flat_spacer", (0.85, 0.83, 0.78), 0.6), angle=55))

    # ---------------------------------------------------------------- pistol grip
    def grip_section(t):
        d = lerp_table([(0, 52), (0.18, 44), (0.5, 40), (0.85, 43), (1.0, 42)], t)
        w = lerp_table([(0, 26), (0.3, 29), (0.8, 31), (1.0, 30)], t)
        k = 1.0 if t <= 1.0 else max(0.08, 1.0 - ((t - 1.0) / 0.06) ** 2) ** 0.5
        return se_ring(w / 2 * k, d / 2 * k, n=2.3, count=32)

    ts = [i / 20 for i in range(21)] + [1.02, 1.035, 1.048, 1.057, 1.06]
    F.append(loft("p_grip", sweep(line(GRIP_A, GRIP_B), ts, grip_section), wood, tile_mm=WOOD_TILE_MM, angle=60))
    end, tan = line(GRIP_A, GRIP_B)(1.0)
    F.append(loft("p_grip_cap", sweep(line(end + tan * 1.5, end + tan * 5.5), [0, 1],
                                      lambda t: se_ring(14.9, 20.9, n=2.3, count=32)), steel, angle=55))

    # ---------------------------------------------------------------- pump (forend) with grip grooves
    grooves = [x for x in range(232, 372, 14)]

    # the AK's lower handguard section (half width 18, a square-ish bottom) round the tube
    def pump_ring(x, inset=0.0, count=36):
        k = lerp_table([(PUMP_X0, 0.86), (PUMP_X0 + 10, 1.0), (PUMP_X1 - 10, 1.0), (PUMP_X1, 0.88)], x)
        a = 18.0 * k - inset
        bot = 17.0 * k - inset
        return [Vector((x, p, TUBE_Z + q)) for p, q in se_ring(a, 26.0 * k - inset * 0.5, bot, n=3.2, count=count)]

    xs = [(PUMP_X0, 0.0), (PUMP_X0 + 4, 0.0), (PUMP_X0 + 10, 0.0)]
    for g in grooves:
        xs += [(g - 3.0, 0.0), (g - 1.6, 1.6), (g + 1.6, 1.6), (g + 3.0, 0.0)]
    xs += [(PUMP_X1 - 10, 0.0), (PUMP_X1 - 4, 0.0), (PUMP_X1, 0.0)]
    P.append(loft("p_pump", [pump_ring(x, i) for x, i in xs], wood, tile_mm=WOOD_TILE_MM, angle=40))
    P.append(smooth(cyl("p_pump_liner", TUBE_R + 1.2, PUMP_X1 - PUMP_X0 + 8, ((PUMP_X0 + PUMP_X1) / 2, 0, TUBE_Z), steel, verts=28, rot=RX)))
    for s in (1, -1):        # action bars back into the receiver
        P.append(add_bevel(box(f"p_action_bar{s}", (PUMP_X0 - REC_X1 + 40, 2.2, 6), ((PUMP_X0 + REC_X1 - 30) / 2, s * 12.0, TUBE_Z + 4), steel), 0.3))

    # ---------------------------------------------------------------- bolt (seen through the port) and trigger
    B = parts["Bolt"]
    B.append(smooth(add_bevel(box("p_bolt", (70, 10, 16), (62, -8.5, 3), steel), 0.8)))
    B.append(cyl("p_extractor", 1.5, 18, (88, -13.6, 6), steel, verts=8, rot=RX))
    trig = prism("p_trigger", [(4, -36), (-4, -36), (-4.5, -47), (-2.5, -56), (2.5, -63), (5.5, -62.2), (2.2, -55), (1.2, -47)],
                 6.0, (0, 0, 0), steel, axis='Y')
    parts["Trigger"].append(smooth(add_bevel(trig, 0.5)))
    return parts


# ============================================================================================ output
POINTS = {
    "Muzzle": (MUZZLE_X, 0, 0),
    "FrontBead": (MUZZLE_X - 8, 0, SIGHT_Z),
    "RearSightNotch": (REC_X0 + 30, 0, SIGHT_Z),
    "EjectionPort": (62, -16, 3),
    "TriggerPivot": (1, 0, -36),
    "GripCenter": (-60, 0, -82),
    "SupportHand": ((PUMP_X0 + PUMP_X1) / 2, 0, TUBE_Z - 6),
    "Butt": (-340, 0, -74),
}


def save_points():
    data = {k: [round(c * MM, 5) for c in v] for k, v in POINTS.items()}
    data["LeftHandShift"] = [round(c * MM, 5) for c in LEFT_HAND_SHIFT]
    data["PumpTravel"] =round(PUMP_TRAVEL * MM, 5)
    data["Gauge"] = "12 gauge, 18.5 mm bore, 7 + 1 rounds"
    data["Note"] = "Blender coordinates in metres: +X muzzle, +Y left, +Z up; bore on the X axis; x = 0 at the trigger."
    path = os.path.join(K.OUT, "shotgun_points.json")
    with open(path, "w") as f:
        json.dump(data, f, indent=2)
    print(f"[shotgun] points -> {path}", flush=True)


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
        "left": dict(ortho=1.02, loc=(0.13, 1.5, -0.04), target=(0.13, 0, -0.04), res=(1600, 700)),
        "right": dict(ortho=1.02, loc=(0.13, -1.5, -0.04), target=(0.13, 0, -0.04), res=(1600, 700)),
        "three_quarter": dict(lens=50, loc=(0.80, -1.0, 0.45), target=(0.13, 0, -0.04), res=(1400, 900)),
        "receiver": dict(lens=50, loc=(0.16, -0.45, 0.10), target=(0.04, 0, -0.03), res=(1400, 900)),
        "fpv": dict(lens=32, loc=(-0.30, 0.10, 0.08), target=(0.60, -0.03, -0.01), res=(1400, 900)),
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
        scene.render.filepath = os.path.join(K.PREVIEWS, f"shotgun_{name}.png")
        bpy.ops.render.render(write_still=True)
        print(f"[shotgun] preview {scene.render.filepath}", flush=True)
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
    print(f"[shotgun] {len(final)} objects, ~{tris} triangles", flush=True)
    save_points()
    K.export("shotgun", final)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.PROJECT, "Assets", "shotgun.blend"))
    if "nopreview" not in argv:
        render_previews(final)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.PROJECT, "Assets", "shotgun.blend"))


main()
