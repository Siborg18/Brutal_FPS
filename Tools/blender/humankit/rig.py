"""The rig: a game skeleton (deform bones, Unreal-style hierarchy, Blender .L/.R names) with animation controls in
the same armature: IK/FK arms and legs (switch properties on the root bone), elbow and knee poles, a reverse foot
(heel, tip-toe and ball pivots, toe control), eye look-at target, and twist bones driven by constraints."""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

V = lambda *a: Vector(a)


def log(*a):
    print("[human]", *a, flush=True)


# ------------------------------------------------------------------------------------------------ widgets
def _wire(name, verts, edges):
    me = bpy.data.meshes.get(name)
    if me is None:
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(v) for v in verts], edges, [])
    ob = bpy.data.objects.get(name) or bpy.data.objects.new(name, me)
    col = bpy.data.collections.get("Widgets")
    if col is None:
        col = bpy.data.collections.new("Widgets")
        bpy.context.scene.collection.children.link(col)
        col.hide_render = True
        col.hide_viewport = True
    if ob.name not in col.objects:
        col.objects.link(ob)
    return ob


def _circle(n=32, r=1.0, axis='Y', y=0.0):
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        c, s = r * math.cos(a), r * math.sin(a)
        pts.append({'Y': (c, y, s), 'Z': (c, s, y), 'X': (y, c, s)}[axis])
    return pts, [(i, (i + 1) % n) for i in range(n)]


def widgets():
    W = {}
    v, e = _circle(32, 1.0, 'Y', 0.5)
    W["circle"] = _wire("WGT_circle", v, e)                     # ring around the bone, halfway along it
    v, e = _circle(32, 1.0, 'Y', 0.0)
    W["ring_base"] = _wire("WGT_ring_base", v, e)
    # root: big ring with a forward arrow
    v, e = _circle(48, 1.0, 'Z', 0.0)
    n = len(v)
    v += [(0, -1.0, 0), (0, -1.35, 0), (-0.12, -1.18, 0), (0.12, -1.18, 0)]
    e += [(n, n + 1), (n + 1, n + 2), (n + 1, n + 3)]
    W["root"] = _wire("WGT_root", v, e)
    # box
    c = [(x, y, z) for x in (-1, 1) for y in (0, 1) for z in (-1, 1)]
    be = [(i, j) for i in range(8) for j in range(i + 1, 8) if sum(a != b for a, b in zip(c[i], c[j])) == 1]
    W["box"] = _wire("WGT_box", c, be)
    # sphere
    v1, e1 = _circle(24, 1.0, 'X')
    v2, e2 = _circle(24, 1.0, 'Y')
    v3, e3 = _circle(24, 1.0, 'Z')
    W["sphere"] = _wire("WGT_sphere", v1 + v2 + v3, e1 + [(a + 24, b + 24) for a, b in e2] + [(a + 48, b + 48) for a, b in e3])
    # diamond (poles)
    d = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)]
    W["diamond"] = _wire("WGT_diamond", d, [(0, 2), (2, 1), (1, 3), (3, 0), (0, 4), (4, 1), (1, 5), (5, 0), (2, 4), (4, 3), (3, 5), (5, 2)])
    # foot outline (bone space: y forward along the bone, x across)
    fv = [(-0.45, -0.25, 0), (0.45, -0.25, 0), (0.55, 0.6, 0), (0.45, 1.25, 0), (-0.45, 1.25, 0), (-0.55, 0.6, 0)]
    W["foot"] = _wire("WGT_foot", fv, [(i, (i + 1) % 6) for i in range(6)])
    # half ring for pivots (arc over the bone)
    v = [(math.cos(a), 0.0, math.sin(a)) for a in np.linspace(0, math.pi, 13)]
    W["arc"] = _wire("WGT_arc", v, [(i, i + 1) for i in range(12)])
    # eye target: two small rings joined
    v1, e1 = _circle(16, 0.35, 'Z')
    W["eye"] = _wire("WGT_eye", [(x - 0.0, y, z) for x, y, z in v1], e1)
    return W


# ------------------------------------------------------------------------------------------------ skeleton
def spine_points(J):
    return dict(
        pelvis=(V(0, 0.010, 0.955), V(0, 0.015, 1.040)),
        spine_01=(V(0, 0.015, 1.040), V(0, 0.020, 1.150)),
        spine_02=(V(0, 0.020, 1.150), V(0, 0.022, 1.270)),
        spine_03=(V(0, 0.022, 1.270), V(0, 0.022, 1.445)),
        neck_01=(V(0, 0.022, 1.445), V(0, 0.012, 1.585)),
        head=(V(0, 0.012, 1.585), V(0, 0.012, 1.780)),
    )


def build_armature(J, name="Human_Rig"):
    W = widgets()
    arm = bpy.data.armatures.new(name)
    rig = bpy.data.objects.new(name, arm)
    bpy.context.scene.collection.objects.link(rig)
    rig.show_in_front = True
    arm.display_type = 'OCTAHEDRAL'
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.edit_bones
    info = {}           # name -> dict(shape, scale, coll, deform, side)

    def bone(n, head, tail, parent=None, z=None, deform=True, connect=False, coll="Body", shape=None, scale=1.0,
             side=0):
        b = eb.new(n)
        b.head, b.tail = Vector(head), Vector(tail)
        if z is not None:
            b.align_roll(Vector(z))
        if parent:
            b.parent = eb[parent]
            b.use_connect = connect
        b.use_deform = deform
        info[n] = dict(shape=shape, scale=scale, coll=coll, side=side)
        return b

    def mir(v):
        return Vector((-v[0], v[1], v[2]))

    fwd = V(0, -1, 0)
    up = V(0, 0, 1)
    # --- centre
    bone("root", (0, 0, 0), (0, -0.30, 0), z=up, coll="Body", shape="root", scale=1.0)
    sp = spine_points(J)
    bone("pelvis", *sp["pelvis"], parent="root", z=fwd, shape="circle", scale=1.9)
    bone("spine_01", *sp["spine_01"], parent="pelvis", z=fwd, connect=True, shape="circle", scale=1.5)
    bone("spine_02", *sp["spine_02"], parent="spine_01", z=fwd, connect=True, shape="circle", scale=1.35)
    bone("spine_03", *sp["spine_03"], parent="spine_02", z=fwd, connect=True, shape="circle", scale=1.0)
    bone("neck_01", *sp["neck_01"], parent="spine_03", z=fwd, connect=True, shape="circle", scale=0.55)
    bone("head", *sp["head"], parent="neck_01", z=fwd, connect=True, shape="circle", scale=0.75)
    # --- per side (s = +1 left, -1 right)
    hx, hy, hz = (Vector(tuple(a)) for a in J["hand_axes"])
    for s, sfx in ((1, ".L"), (-1, ".R")):
        m = (lambda v: Vector(tuple(v))) if s > 0 else (lambda v: mir(Vector(tuple(v))))
        side = s
        E = J["eye"]
        bone("eye" + sfx, m(E), m(Vector(tuple(E)) + V(0, -0.025, 0)), parent="head", z=up, coll="Mechanism", side=side)
        GH, EL, WR = m(J["shoulder"]), m(J["elbow"]), m(J["wrist"])
        back = V(0, 1, 0)
        bone("clavicle" + sfx, m(J["clav_in"]), GH, parent="spine_03", z=back, coll="Arms FK", shape="circle",
             scale=0.5, side=side)
        bone("upperarm" + sfx, GH, EL, parent="clavicle" + sfx, z=back, connect=True, coll="Arms FK", shape="circle",
             scale=0.25, side=side)
        bone("upperarm_twist" + sfx, GH, GH.lerp(EL, 0.5), parent="upperarm" + sfx, z=back, coll="Mechanism", side=side)
        bone("forearm" + sfx, EL, WR, parent="upperarm" + sfx, z=back, connect=True, coll="Arms FK", shape="circle",
             scale=0.2, side=side)
        bone("forearm_twist" + sfx, EL.lerp(WR, 0.5), WR, parent="forearm" + sfx, z=back, coll="Mechanism", side=side)
        hxs, hzs = (hx if s > 0 else mir(hx)), (hz if s > 0 else mir(hz))
        bone("hand" + sfx, WR, WR + hxs * 0.090, parent="forearm" + sfx, z=hzs, connect=True, coll="Arms FK",
             shape="circle", scale=0.55, side=side)
        for fname, pts in J["fingers"].items():
            pts = [m(p) for p in pts]
            if fname == "thumb":
                d = (pts[1] - pts[0]).normalized()
                tw = (hy * 0.7 + hz * 0.7).normalized()
                zv = tw if s > 0 else mir(tw)
            else:
                zv = hzs
            parent = "hand" + sfx
            for i in range(3):
                n = f"{fname}_0{i + 1}{sfx}"
                bone(n, pts[i], pts[i + 1], parent=parent, z=zv, connect=i > 0, coll="Fingers", shape="circle",
                     scale=1.0, side=side)
                parent = n
        # legs
        HJ, KN, AN = m(J["hip"]), m(J["knee"]), m(J["ankle"])
        BALL = m(J["ball"])
        tip = m(J["toes"][0][-1])
        bone("thigh" + sfx, HJ, KN, parent="pelvis", z=fwd, coll="Legs FK", shape="circle", scale=0.3, side=side)
        bone("calf" + sfx, KN, AN, parent="thigh" + sfx, z=fwd, connect=True, coll="Legs FK", shape="circle",
             scale=0.3, side=side)
        bone("foot" + sfx, AN, BALL, parent="calf" + sfx, z=up, connect=True, coll="Legs FK", shape="circle",
             scale=0.45, side=side)
        toe_tip = Vector((BALL.x + (tip.x - BALL.x) * 0.3, tip.y, BALL.z))
        bone("ball" + sfx, BALL, toe_tip, parent="foot" + sfx, z=up, connect=True, coll="Legs FK", shape="circle",
             scale=0.6, side=side)
        # --- IK controls
        bone("hand_ik" + sfx, WR, WR + hxs * 0.090, parent="root", z=hzs, deform=False, coll="Arms IK", shape="box",
             scale=0.5, side=side)
        bone("arm_pole" + sfx, EL + back * 0.35, EL + back * 0.40, parent="root", z=up, deform=False, coll="Arms IK",
             shape="diamond", scale=0.5, side=side)
        ffwd = Vector((toe_tip.x - AN.x, toe_tip.y - AN.y, 0)).normalized()
        heel = m(J["heel"])
        bone("foot_ik" + sfx, V(AN.x, AN.y, 0.0), V(AN.x, AN.y, 0.0) + ffwd * 0.16, parent="root", z=up,
             deform=False, coll="Legs IK", shape="foot", scale=1.0, side=side)
        bone("foot_heel" + sfx, V(heel.x, heel.y, 0.0), V(heel.x, heel.y, 0.0) + ffwd * 0.05, parent="foot_ik" + sfx,
             z=up, deform=False, coll="Legs IK", shape="arc", scale=0.8, side=side)
        ttip = V(tip.x, tip.y, 0.0)
        bone("foot_tip" + sfx, ttip, ttip - ffwd * 0.05, parent="foot_heel" + sfx, z=up, deform=False,
             coll="Legs IK", shape="arc", scale=0.8, side=side)
        bone("foot_ball" + sfx, BALL, BALL - ffwd * 0.06, parent="foot_tip" + sfx, z=up, deform=False,
             coll="Legs IK", shape="arc", scale=1.0, side=side)
        bone("toe_ik" + sfx, BALL, toe_tip, parent="foot_tip" + sfx, z=up, deform=False, coll="Legs IK",
             shape="circle", scale=0.8, side=side)
        bone("leg_target" + sfx, AN, AN + V(0, 0, 0.05), parent="foot_ball" + sfx, z=fwd, deform=False,
             coll="Mechanism", side=side)
        bone("foot_mch" + sfx, AN, BALL, parent="foot_ball" + sfx, z=up, deform=False, coll="Mechanism", side=side)
        bone("knee_pole" + sfx, KN + fwd * 0.45, KN + fwd * 0.50, parent="foot_ik" + sfx, z=up, deform=False,
             coll="Legs IK", shape="diamond", scale=0.5, side=side)
    # eye target
    E = J["eye"]
    bone("eyes_target", V(0, -0.55, E[2]), V(0, -0.60, E[2]), parent="head", z=up, deform=False, coll="Face",
         shape="box", scale=1.2)
    for s, sfx in ((1, ".L"), (-1, ".R")):
        bone("eye_target" + sfx, V(s * E[0], -0.55, E[2]), V(s * E[0], -0.58, E[2]), parent="eyes_target", z=up,
             deform=False, coll="Face", shape="eye", scale=1.0, side=s)
    # pole angles (rest pose: the IK must not move anything)
    poles = {}
    for sfx in (".L", ".R"):
        poles["forearm" + sfx] = pole_angle(eb["upperarm" + sfx], eb["forearm" + sfx], eb["arm_pole" + sfx].head)
        poles["calf" + sfx] = pole_angle(eb["thigh" + sfx], eb["calf" + sfx], eb["knee_pole" + sfx].head)
    bpy.ops.object.mode_set(mode='POSE')

    # ---------------------------------------------------------------- collections, colours, shapes
    colls = {}
    for cname in ("Body", "Arms FK", "Arms IK", "Legs FK", "Legs IK", "Fingers", "Face", "Mechanism"):
        colls[cname] = arm.collections.new(cname)
    colls["Mechanism"].is_visible = False
    colls["Legs FK"].is_visible = False             # legs start in IK, arms in FK
    palette = {0: 'THEME09', 1: 'THEME04', -1: 'THEME01'}
    for n, d in info.items():
        b = arm.bones[n]
        colls[d["coll"]].assign(b)
        b.color.palette = palette[d["side"]]
        pb = rig.pose.bones[n]
        pb.color.palette = palette[d["side"]]
        if d["shape"]:
            pb.custom_shape = W[d["shape"]]
            pb.custom_shape_scale_xyz = (d["scale"],) * 3
            pb.custom_shape_wire_width = 2.0

    # ---------------------------------------------------------------- properties and constraints
    root = rig.pose.bones["root"]
    for sfx in (".L", ".R"):
        for k in ("ik_arm", "ik_leg"):
            key = k + sfx
            root[key] = 0.0 if k == "ik_arm" else 1.0
            ui = root.id_properties_ui(key)
            ui.update(min=0.0, max=1.0, soft_min=0.0, soft_max=1.0, description="IK (1) / FK (0) blend")
    root["eye_look"] = 1.0
    root.id_properties_ui("eye_look").update(min=0.0, max=1.0, description="Eyes follow eyes_target")
    pbs = rig.pose.bones
    for sfx in (".L", ".R"):
        # arm IK
        c = pbs["forearm" + sfx].constraints.new('IK')
        c.target, c.subtarget = rig, "hand_ik" + sfx
        c.pole_target, c.pole_subtarget = rig, "arm_pole" + sfx
        c.pole_angle = poles["forearm" + sfx]
        c.chain_count = 2
        _drive(c, rig, "ik_arm" + sfx)
        c = pbs["hand" + sfx].constraints.new('COPY_ROTATION')
        c.target, c.subtarget = rig, "hand_ik" + sfx
        _drive(c, rig, "ik_arm" + sfx)
        # leg IK + reverse foot
        c = pbs["calf" + sfx].constraints.new('IK')
        c.target, c.subtarget = rig, "leg_target" + sfx
        c.pole_target, c.pole_subtarget = rig, "knee_pole" + sfx
        c.pole_angle = poles["calf" + sfx]
        c.chain_count = 2
        _drive(c, rig, "ik_leg" + sfx)
        c = pbs["foot" + sfx].constraints.new('COPY_ROTATION')
        c.target, c.subtarget = rig, "foot_mch" + sfx
        _drive(c, rig, "ik_leg" + sfx)
        c = pbs["ball" + sfx].constraints.new('COPY_ROTATION')
        c.target, c.subtarget = rig, "toe_ik" + sfx
        _drive(c, rig, "ik_leg" + sfx)
        # twist bones: the forearm's lower half follows half the hand's twist; the top of the upper arm resists
        # the arm's twist (so the shoulder doesn't wring)
        c = pbs["forearm_twist" + sfx].constraints.new('COPY_ROTATION')
        c.target, c.subtarget = rig, "hand" + sfx
        c.use_x, c.use_y, c.use_z = False, True, False
        c.target_space = c.owner_space = 'LOCAL'
        c.mix_mode = 'REPLACE'
        c.influence = 0.6
        c = pbs["upperarm_twist" + sfx].constraints.new('COPY_ROTATION')
        c.target, c.subtarget = rig, "upperarm" + sfx
        c.use_x, c.use_y, c.use_z = False, True, False
        c.invert_y = True
        c.target_space = c.owner_space = 'LOCAL'
        c.mix_mode = 'REPLACE'
        c.influence = 0.5
        # eyes
        c = pbs["eye" + sfx].constraints.new('DAMPED_TRACK')
        c.target, c.subtarget = rig, "eye_target" + sfx
        c.track_axis = 'TRACK_Y'
        _drive(c, rig, "eye_look", path_bone="root")
    # rotation modes: quaternions for the body, Euler XYZ is friendlier for fingers and hinges
    for pb in pbs:
        pb.rotation_mode = 'QUATERNION'
    for pb in pbs:
        if any(pb.name.startswith(f) for f in ("thumb", "index", "middle", "ring", "pinky")):
            pb.rotation_mode = 'XYZ'
    # pivots and poles only translate/rotate sensibly
    for sfx in (".L", ".R"):
        for n in ("arm_pole", "knee_pole"):
            pbs[n + sfx].lock_rotation = (True, True, True)
        for n in ("foot_heel", "foot_tip", "foot_ball", "toe_ik"):
            pbs[n + sfx].lock_location = (True, True, True)
    bpy.ops.object.mode_set(mode='OBJECT')
    arm.collections_all["Body"].is_visible = True
    log(f"rig: {len(arm.bones)} bones, {sum(b.use_deform for b in arm.bones)} deform")
    return rig


def _drive(con, rig, prop, path_bone="root"):
    fc = con.driver_add("influence")
    drv = fc.driver
    drv.type = 'AVERAGE'
    var = drv.variables.new()
    var.name = "v"
    var.type = 'SINGLE_PROP'
    var.targets[0].id = rig
    var.targets[0].data_path = f'pose.bones["{path_bone}"]["{prop}"]'


def pole_angle(base, ik, pole):
    """The IK pole angle that keeps the chain (base -> ik) exactly in its rest pose for this pole position."""
    def signed_angle(u, v, normal):
        a = u.angle(v)
        if u.cross(v).angle(normal) < 1:
            a = -a
        return a
    pole_normal = (ik.tail - base.head).cross(pole - base.head)
    projected = pole_normal.cross(base.tail - base.head)
    return signed_angle(base.x_axis, projected, base.tail - base.head)
