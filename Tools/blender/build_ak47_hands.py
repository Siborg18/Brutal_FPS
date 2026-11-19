"""
The hands holding the AK-47: the right hand round the pistol grip (index finger on the trigger, thumb along the left of
the receiver), the left hand cupping the lower handguard from below (fingers up its right side, thumb forward along
its left side), both in fleece sleeves. Run after build_ak47.py (it opens Assets/ak47.blend, adds the hands and
re-exports Assets/Models/ak47.glb):

    blender -b --factory-startup -P Tools/blender/build_ak47_hands.py               (build, bake, export)
    blender -b --factory-startup -P Tools/blender/build_ak47_hands.py -- preview    (live skin shader, renders only)

It reuses the hand toolkit of build_pistol.py (signed distance volumes, voxel remesh, pressing against the gun, the
baked skin): that script's functions are loaded without running it.

The right hand is built by the pistol's own hand builder with the grip lines swapped for the AK's: the AK's grip
sits at nearly the same angle and its back strap, seen from the trigger, almost exactly where the pistol's is once
everything is shifted by GRIP_SHIFT (AK = pistol frame + GRIP_SHIFT), so the palm, thumb and wrist fit as they are;
only the front strap (shallower) and the trigger (closer) differ. The left hand is new (build_guard_hand).

Texture sets: ak_hand, ak_hand_index, ak_hand_left (Assets/Textures). Key points for the game are added to
Assets/Models/ak47_points.json (Blender coordinates, metres).
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import modelkit as K  # noqa: E402

# --------------------------------------------------------------------------------------- the pistol's hand toolkit
P = {"__name__": "pistol_kit", "__file__": os.path.join(HERE, "build_pistol.py")}
_src = open(P["__file__"], encoding="utf8").read()
_src = _src[:_src.rindex("\nmain()")]
exec(compile(_src, P["__file__"], "exec"), P)

GRIP_SHIFT = Vector((0.0152, 0.0, -0.0160))

# The AK's pistol grip (build_ak47.py: GRIP_A, GRIP_B and the depth along it), metres, (x, z).
AK_GRIP_A, AK_GRIP_B = Vector((-0.046, -0.030)), Vector((-0.077, -0.138))
AK_GRIP_DEPTH = [(0, 0.052), (0.18, 0.044), (0.5, 0.040), (0.85, 0.043), (1.0, 0.042)]
AK_TRIGGER_FACE_X = 0.0015            # the trigger blade's front face where the finger pad lies


def lerp_table(table, x):
    if x <= table[0][0]:
        return table[0][1]
    for (x0, v0), (x1, v1) in zip(table, table[1:]):
        if x <= x1:
            return v0 + (v1 - v0) * (x - x0) / (x1 - x0)
    return table[-1][1]


def ak_grip_strap(t, front):
    d = AK_GRIP_B - AK_GRIP_A
    tan = d.normalized()
    fwd = Vector((-tan.y, tan.x))
    c = AK_GRIP_A + d * t
    return c + fwd * (lerp_table(AK_GRIP_DEPTH, t) / 2) * (1 if front else -1)


def use_ak_grip():
    """Points the pistol hand builder at the AK's grip (in the pistol frame)."""
    s = Vector((GRIP_SHIFT.x, GRIP_SHIFT.z))
    P["FRONT0"], P["FRONT1"] = ak_grip_strap(0.15, True) - s, ak_grip_strap(0.80, True) - s
    P["BACK0"], P["BACK1"] = ak_grip_strap(0.10, False) - s, ak_grip_strap(0.80, False) - s
    P["GRIP_W"] = 0.030
    P["TRIGGER_PIVOT"] = Vector((AK_TRIGGER_FACE_X - GRIP_SHIFT.x - 0.0058, 0.0, -0.0225))
    P["GRIP_DOWN"] = Vector((P["BACK1"].x - P["BACK0"].x, 0, P["BACK1"].y - P["BACK0"].y)).normalized()
    print(f"[ak47 hands] grip in the pistol frame: front {tuple(P['FRONT0'])}..{tuple(P['FRONT1'])}, "
          f"back {tuple(P['BACK0'])}..{tuple(P['BACK1'])}", flush=True)


# --------------------------------------------------------------------------------------- the lower handguard
# build_ak47.py's lower handguard: bottom and half width along x (mm). Its top edge is at z = 10 mm, the upper
# handguard's bottom at 12 mm.
LOWER_BOT = [(181, -34), (192, -35.5), (205, -33), (225, -26), (250, -23), (300, -21.5), (324, -21)]
LOWER_W = [(181, 20.5), (195, 22), (212, 21), (235, 18.5), (300, 17.5), (324, 17)]
GUARD_TOP = 0.012

LEFT_WRIST = Vector((0.2440, 0.0520, -0.0930))
LEFT_ARM = Vector((-0.30, 0.32, -0.90)).normalized()
LEFT_CMC = Vector((0.2660, 0.0440, -0.0700))
FINGER_LEAN = 0.22


def guard_section(x):
    bot = lerp_table(LOWER_BOT, x * 1000) / 1000
    half = lerp_table(LOWER_W, x * 1000) / 1000
    c = Vector((x, 0.0, (GUARD_TOP + bot) / 2))
    return c, half, (GUARD_TOP - bot) / 2


def guard_wrap(x, lengths, radius, phi0, press=0.85, extra=0.0015):
    """A finger wrapped round the handguard in the plane at x: MCP at angle phi0 (degrees; 0 = straight under it,
    +90 = its right side, 180 = its top), each phalanx a chord of the offset ellipse (flatter over the top, where the
    upper handguard leaves no room)."""
    c, ra0, rb0 = guard_section(x)
    ra = ra0 + radius * press + extra
    rb_bot = rb0 + radius * press + extra
    rb_top = rb0 + radius * 0.35

    def q(phi):
        rb = rb_bot if math.cos(phi) > 0 else rb_top
        return c + Vector((0, -ra * math.sin(phi), -rb * math.cos(phi)))

    phi = math.radians(phi0)
    pts = [q(phi)]
    for L in lengths:
        lo, hi = phi, phi + math.pi
        for _ in range(40):
            mid = (lo + hi) / 2
            if (q(mid) - pts[-1]).length < L:
                lo = mid
            else:
                hi = mid
        phi = (lo + hi) / 2
        pts.append(q(phi))
    return pts, c


def build_guard_hand():
    """The left hand under the handguard. Returns (objects, joints, tips, pivots)."""
    capsule, ellipsoid, hull = P["capsule"], P["ellipsoid"], P["hull"]
    finger_volumes, FINGERS, THUMB, GROUPS, NAILS = P["finger_volumes"], P["FINGERS"], P["THUMB"], P["GROUPS"], P["NAILS"]
    objs, joints, tips = [], [], []
    mcps = {}
    first_nail = len(NAILS)
    axis = Vector((1, 0, 0))
    # Knuckles about a finger's width apart, so the fingers lie together up the right side (not spread like pegs),
    # each leaning forward as it wraps (FINGER_LEAN: forward metres per metre along the finger), as a cupping hand does.
    for name, x, phi0 in (("index", 0.2900, -30), ("middle", 0.2720, -38), ("ring", 0.2545, -40), ("little", 0.2380, -36)):
        L1, L2, L3, r = FINGERS[name]
        pts, center = guard_wrap(x, (L1, L2, L3), r, phi0)
        run = 0.0
        for i in range(1, 4):
            run += (pts[i] - pts[i - 1]).length
            pts[i] = pts[i] + Vector((run * FINGER_LEAN, 0, 0))
        mcps[name] = (pts[0], center)
        objs += finger_volumes("G_" + name, pts, r, center, axis)
        for i in range(3):
            joints.append((pts[i], (pts[i + 1] - pts[i]).normalized(), r))
        tips.append((pts[3], (pts[3] - pts[2]).normalized(), r))
        nail_c = pts[2].lerp(pts[3], 0.62)
        out = nail_c - center
        out = (out - axis * out.dot(axis)).normalized()
        NAILS.append((pts[2], pts[3], out, r))

    # Thumb: from the heel of the hand up along the left side of the handguard, pointing forward.
    T1, T2, T3, rt = THUMB
    cmc = LEFT_CMC
    t_mcp = cmc + (Vector((0.2860, 0.0360, -0.0300)) - cmc).normalized() * T1
    t_ip = t_mcp + (Vector((0.3080, 0.0290, -0.0080)) - t_mcp).normalized() * T2
    t_tip = t_ip + (Vector((0.3300, 0.0272, 0.0010)) - t_ip).normalized() * T3
    th = [cmc, t_mcp, t_ip, t_tip]
    thumb = [capsule("G_thumb_meta", cmc, t_mcp, rt * 1.12, rt * 0.97, flat=0.80, width_dir=(0, 1, 0)),
             capsule("G_thumb_prox", t_mcp, t_ip, rt * 0.98, rt * 0.90, flat=0.84, width_dir=(0, 1, 0)),
             capsule("G_thumb_dist", t_ip, t_tip, rt * 0.88, rt * 0.70, flat=0.80, width_dir=(0, 1, 0)),
             ellipsoid("G_thumb_joint", t_ip, (rt * 0.92, rt * 0.76, rt * 0.55), z_axis=t_tip - t_ip, x_hint=(0, 1, 0)),
             ellipsoid("G_thumb_mcp", t_mcp, (rt * 1.02, rt * 0.88, rt * 0.88), z_axis=t_ip - t_mcp, x_hint=(0, 1, 0)),
             ellipsoid("G_thumb_pad", t_ip.lerp(t_tip, 0.55) + Vector((0, -1, 0.1)).normalized() * rt * 0.22,
                       (rt * 0.82, rt * 0.68, T3 * 0.40), z_axis=t_tip - t_ip, x_hint=(0, 1, 0))]
    for o in thumb:
        GROUPS[o.name] = "G_thumb"
    P["GROUP_K"]["G_thumb"] = 0.0030
    P["JOIN_K"]["G_thumb"] = 0.0065
    objs += thumb
    NAILS.append((t_ip, t_tip, Vector((0, 0.8, 0.6)).normalized(), rt))
    for i in range(1, 3):
        joints.append((th[i], (th[i + 1] - th[i]).normalized(), rt))
    tips.append((t_tip, (t_tip - t_ip).normalized(), rt))

    # Palm: from the knuckle row (under the handguard's left edge) down to the wrist, below and to the left; the
    # thenar along the thumb's metacarpal, the heel of the hand on the little-finger side.
    wrist, arm_dir = LEFT_WRIST, LEFT_ARM
    across = P["_ortho"]((1.0, 0.0, 0.15), arm_dir)            # thumb side forward
    dorsal = arm_dir.cross(across).normalized()
    if dorsal.dot(Vector((0, 1, -0.6))) < 0:
        dorsal = -dorsal                                        # the back of the hand faces out, left and down
    pts = []
    for name in ("index", "middle", "ring", "little"):
        p, center = mcps[name]
        out = p - center
        out = (out - axis * out.dot(axis)).normalized()
        r = FINGERS[name][3]
        pts += [p + out * r * 0.9, p - out * r * 0.5, p + axis * r * 0.6, p - axis * r * 0.6]
    for d in (-1, 1):
        for t in (-1, 1):
            pts.append(wrist + across * (0.027 * d) + dorsal * (0.011 * t))
    # The body of the hand between the knuckles and the wrist: its back (over the tendons) and the palm's face.
    for name in ("index", "middle", "ring", "little"):
        p = mcps[name][0]
        for f in (0.35, 0.65):
            m = p.lerp(wrist, f)
            pts += [m + dorsal * 0.013, m - dorsal * 0.011]
    objs.append(hull("G_palm", pts))
    th_axis = t_mcp - cmc
    objs.append(ellipsoid("G_thenar", cmc.lerp(t_mcp, 0.42) - dorsal * 0.006, (0.0140, 0.0105, th_axis.length * 0.52),
                          z_axis=th_axis, x_hint=dorsal))
    little = mcps["little"][0]
    objs.append(ellipsoid("G_hypothenar", wrist.lerp(little, 0.40) - dorsal * 0.004 - across * 0.004, (0.0140, 0.0115, 0.0250),
                          z_axis=little - wrist, x_hint=dorsal))
    for name in ("index", "middle", "ring", "little"):
        p, center = mcps[name]
        out = p - center
        out = (out - axis * out.dot(axis)).normalized()
        r = FINGERS[name][3]
        kn = ellipsoid(f"G_{name}_knuckle", p + out * r * 0.30, (r * 1.08, r * 1.0, r * 1.0), z_axis=axis, x_hint=out)
        GROUPS[kn.name] = "G_" + name
        objs.append(kn)
        joints.append((p, -out, r, False))   # no crease lines from the knuckle row
        objs.append(capsule(f"G_{name}_tendon", wrist + dorsal * 0.014, p + out * r * 0.9, 0.0026, 0.0023, flat=0.7, width_dir=across))
    objs.append(capsule("G_forearm", wrist + arm_dir * 0.004, wrist + arm_dir * P["FOREARM_STUB"], 0.0280, 0.0315, flat=0.74, width_dir=across))
    objs.append(ellipsoid("G_wrist_bone", wrist + arm_dir * 0.018 + dorsal * 0.012 - across * 0.018, (0.0075, 0.0075, 0.0075)))
    P["HAND_NAILS"]["HandLeft"] = NAILS[first_nail:]
    pivots = {"LeftWrist": list(wrist), "LeftArm": list(arm_dir), "LeftAcross": list(across)}
    return objs, joints, tips, pivots


# --------------------------------------------------------------------------------------- helpers
def solid_copies(names, shift, inflate=0.0008):
    """Hidden, slightly inflated copies of the gun's parts (moved by shift) to press the hands against."""
    out = []
    for n in names:
        src = bpy.data.objects[n]
        c = src.copy()
        c.data = src.data.copy()
        c.name = "press_" + n
        bpy.context.scene.collection.objects.link(c)
        c.matrix_world = Matrix.Translation(shift) @ src.matrix_world
        P["apply_all"](c)
        sh = c.modifiers.new("inflate", 'DISPLACE')
        sh.strength = inflate
        P["apply_all"](c)
        c.data.materials.clear()
        c["cutter"] = True
        c.hide_set(True)
        c.hide_render = True
        out.append(c)
    return out


def press(o, solids):
    """Presses a hand against the gun: boolean difference with each (inflated) solid. The gun's parts are unions of
    overlapping closed pieces, so the exact solver is told to expect self-intersections."""
    for s in solids:
        mod = o.modifiers.new("press_" + s.name, 'BOOLEAN')
        mod.object = s
        mod.operation = 'DIFFERENCE'
        mod.solver = 'EXACT'
        mod.use_self = True
        mod.use_hole_tolerant = True
    before = len(o.data.polygons)
    P["apply_all"](o)
    o.data.materials.clear()
    for poly in o.data.polygons:
        poly.material_index = 0
    o.data.shade_smooth()
    print(f"[ak47 hands] pressed {o.name}: {before} -> {len(o.data.polygons)} faces", flush=True)
    if len(o.data.polygons) < before * 0.5:
        raise RuntimeError(f"pressing {o.name} against the gun removed most of it")


def move(o, offset):
    o.matrix_world = Matrix.Translation(offset) @ o.matrix_world
    P["apply_all"](o)


def live_skin(o, joints, tips, tag):
    P["skin_attributes"](o, joints, tips)
    m = P["skin_bake_material"]("skin_prev_" + tag)
    nt = m.node_tree
    nt.links.new(nt.nodes[m["bsdf"]].outputs[0], nt.nodes[m["out"]].inputs["Surface"])
    o.data.materials.clear()
    o.data.materials.append(m)
    for poly in o.data.polygons:
        poly.material_index = 0


# --------------------------------------------------------------------------------------- previews
SIGHT = Vector((0.1612, 0.0, 0.044))                       # the rear sight notch (the game's SightPoint)
# Camera-space (x right, y up, z back) position of the sight notch, and the muzzle's turn in (radians): Game/Rifle.cs.
GAME_VIEWS = {"hip": ((0.105, -0.120, -0.300), 0.06), "aim": ((0.0, 0.0, -0.330), 0.0)}


def camera_pose(name):
    (px, py, pz), yaw = GAME_VIEWS[name]
    fwd = Vector((math.cos(yaw), -math.sin(yaw), 0))
    right = Vector((-math.sin(yaw), -math.cos(yaw), 0))
    up = Vector((0, 0, 1))
    return SIGHT - right * px - up * py + fwd * pz, fwd


def render_previews(tag):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        prefs.compute_device_type = 'OPTIX'
        prefs.get_devices()
        for d in prefs.devices:
            d.use = d.type != 'CPU'
        scene.cycles.device = 'GPU'
    except Exception:
        pass
    scene.cycles.samples = 32
    scene.cycles.use_denoising = True
    scene.view_settings.view_transform = 'AgX'
    world = bpy.data.worlds.new("prev")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.55, 0.62, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    sun = bpy.data.lights.new("sun", 'SUN'); sun.energy = 3.5; sun.angle = math.radians(3)
    so = bpy.data.objects.new("sun", sun); scene.collection.objects.link(so)
    so.rotation_euler = (math.radians(40), math.radians(-20), math.radians(120))
    cam_data = bpy.data.cameras.new("view")
    cam_data.sensor_fit = 'VERTICAL'
    cam_data.clip_start = 0.01
    cam = bpy.data.objects.new("view", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    views = []
    for name, fov in (("hip", 80), ("aim", 68)):
        pos, fwd = camera_pose(name)
        views.append((f"{tag}_{name}", pos, pos + fwd, fov, (1280, 720)))
    grip, guard = Vector((-0.05, 0, -0.06)), Vector((0.26, 0, -0.01))
    views += [(f"{tag}_grip_left", Vector((-0.05, 0.36, -0.03)), grip, 30, (900, 700)),
              (f"{tag}_grip_right", Vector((-0.06, -0.36, -0.03)), grip, 30, (900, 700)),
              (f"{tag}_grip_back", Vector((-0.40, 0.08, 0.10)), grip, 30, (900, 700)),
              (f"{tag}_guard_left", Vector((0.25, 0.38, -0.02)), guard, 30, (900, 700)),
              (f"{tag}_guard_right", Vector((0.25, -0.38, 0.0)), guard, 30, (900, 700)),
              (f"{tag}_guard_below", Vector((0.20, 0.12, -0.40)), guard, 30, (900, 700)),
              (f"{tag}_guard_back", Vector((-0.10, 0.10, 0.10)), guard, 30, (900, 700)),
              (f"{tag}_side", Vector((0.10, 1.4, -0.10)), Vector((0.10, 0, -0.08)), 40, (1400, 800))]
    for name, pos, look, fov, res in views:
        cam.location = pos
        cam.rotation_euler = (look - pos).to_track_quat('-Z', 'Y').to_euler()
        cam_data.angle = math.radians(fov)
        scene.render.resolution_x, scene.render.resolution_y = res
        scene.render.filepath = os.path.join(K.PREVIEWS, name + ".png")
        bpy.ops.render.render(write_still=True)
    for o in (so, cam):
        bpy.data.objects.remove(o)
    print("[ak47 hands] previews done", flush=True)


# --------------------------------------------------------------------------------------- main
GUN = ("Receiver", "Furniture", "Magazine", "BoltCarrier", "Trigger", "Selector")
HANDS = ("Hand", "TriggerFinger", "HandLeft", "Sleeve", "SleeveLeft")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    preview = "preview" in argv
    blend = os.path.join(K.PROJECT, "Assets", "ak47.blend")
    bpy.ops.wm.open_mainfile(filepath=blend)
    for o in list(bpy.data.objects):
        if o.name.split(".")[0] in HANDS or o.name.startswith("press_") or o.type in ('CAMERA', 'LIGHT'):
            bpy.data.objects.remove(o, do_unlink=True)
    for name in GUN:
        if bpy.data.objects.get(name) is None:
            raise RuntimeError(f"ak47.blend lacks {name}: run build_ak47.py first")

    # Right hand: the pistol builder round the AK's grip, in the pistol frame, then moved onto the AK.
    use_ak_grip()
    press_r = solid_copies(("Furniture", "Receiver", "Trigger"), -GRIP_SHIFT)
    hand_objs, index_objs, joints, tips, pivots, _ = P["build_hand"]({})
    hand, _ = P["organic"](hand_objs, "Hand")
    finger, _ = P["organic"](index_objs, "TriggerFinger", target_faces=9000)
    press(hand, press_r)
    press(finger, press_r)
    for o in (hand, finger):
        move(o, GRIP_SHIFT)
    joints = [(j[0] + GRIP_SHIFT, *j[1:]) for j in joints]
    tips = [(p + GRIP_SHIFT, a, r) for p, a, r in tips]
    for n in list(P["HAND_NAILS"].get("Hand", [])):
        pass
    shifted = [(d + GRIP_SHIFT, t + GRIP_SHIFT, o, r) for d, t, o, r in P["NAILS"]]
    P["NAILS"][:] = shifted
    P["HAND_NAILS"]["Hand"] = list(shifted)
    P["HAND_NAILS"]["TriggerFinger"] = list(shifted)
    for o in press_r:
        bpy.data.objects.remove(o, do_unlink=True)

    # Left hand round the handguard (AK frame).
    press_l = solid_copies(("Furniture", "Receiver"), Vector((0, 0, 0)))
    l_objs, l_joints, l_tips, l_pivots = build_guard_hand()
    left, _ = P["organic"](l_objs, "HandLeft")
    press(left, press_l)
    for o in press_l:
        bpy.data.objects.remove(o, do_unlink=True)

    # Sleeves.
    r_wrist = P["RIGHT_WRIST"] + GRIP_SHIFT
    sleeves = [P["build_sleeve"](r_wrist, P["RIGHT_ARM"], P["RIGHT_ACROSS"], name="Sleeve"),
               P["build_sleeve"](LEFT_WRIST, LEFT_ARM, Vector(l_pivots["LeftAcross"]), name="SleeveLeft")]
    sleeves = K.finish(sleeves)

    if preview:
        live_skin(hand, joints, tips, "r")
        live_skin(finger, joints, tips, "f")
        live_skin(left, l_joints, l_tips, "l")
        render_previews(argv[argv.index("preview") + 1] if len(argv) > argv.index("preview") + 1 else "akh")
        return

    hand.data.materials.append(K.mat("tex_ak_hand", None, 0.5, 0.0))
    finger.data.materials.append(K.mat("tex_ak_hand_index", None, 0.5, 0.0))
    left.data.materials.append(K.mat("tex_ak_hand_left", None, 0.5, 0.0))
    for o in (hand, finger, left):
        P["unwrap"](o)
    P["bake_skin"](hand, "ak_hand", joints, tips, 2048)
    P["bake_skin"](finger, "ak_hand_index", joints, tips, 1024)
    P["bake_skin"](left, "ak_hand_left", l_joints, l_tips, 2048)

    export = [bpy.data.objects[n] for n in GUN] + [hand, finger, left] + sleeves
    for o in export:
        o.hide_set(False)
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    K.export("ak47", export)

    path = os.path.join(K.OUT, "ak47_points.json")
    data = json.load(open(path)) if os.path.exists(path) else {}
    data["FingerKnuckle"] = [round(c, 5) for c in Vector(pivots["FingerKnuckle"]) + GRIP_SHIFT]
    data["RightWrist"] = [round(c, 5) for c in r_wrist]
    data["RightArm"] = [round(c, 5) for c in P["RIGHT_ARM"]]
    data["LeftWrist"] = [round(c, 5) for c in LEFT_WRIST]
    data["LeftArm"] = [round(c, 5) for c in LEFT_ARM]
    with open(path, "w") as f:
        json.dump(data, f, indent=2)
    print("[ak47 hands] points", {k: data[k] for k in ("FingerKnuckle", "RightWrist", "RightArm", "LeftWrist", "LeftArm")}, flush=True)
    try:
        render_previews("akh")
    except Exception as e:
        print("[ak47 hands] preview failed:", e, flush=True)
    print("[ak47 hands] done", flush=True)


main()
