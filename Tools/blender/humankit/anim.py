"""Demo actions that exercise the rig: an in-place walk cycle (IK feet with heel/ball roll, pelvis bob, sway and
twist, counter-rotating chest, FK arm swing) and a breathing idle. Both loop."""
import math

import bpy
from mathutils import Matrix, Quaternion, Vector


def log(*a):
    print("[human]", *a, flush=True)


class Poser:
    def __init__(self, rig):
        self.rig = rig
        self.P = rig.pose.bones

    def upd(self):
        bpy.context.view_layer.update()

    def reset(self):
        for pb in self.P:
            pb.location = (0, 0, 0)
            pb.rotation_quaternion = (1, 0, 0, 0)
            pb.rotation_euler = (0, 0, 0)
        self.upd()

    def rot_world(self, name, axis, deg, pivot=None):
        pb = self.P[name]
        self.upd()
        M = pb.matrix.copy()
        h = M.translation.copy() if pivot is None else Vector(pivot)
        pb.matrix = Matrix.Translation(h) @ Matrix.Rotation(math.radians(deg), 4, Vector(axis)) @ Matrix.Translation(-h) @ M
        self.upd()

    def move_world(self, name, d):
        pb = self.P[name]
        self.upd()
        M = pb.matrix.copy()
        M.translation += Vector(d)
        pb.matrix = M
        self.upd()

    def rot_local(self, name, axis, deg):
        pb = self.P[name]
        q = Quaternion(Vector(axis), math.radians(deg))
        if pb.rotation_mode == 'QUATERNION':
            pb.rotation_quaternion = q @ pb.rotation_quaternion
        else:
            pb.rotation_euler = (q @ pb.rotation_euler.to_quaternion()).to_euler(pb.rotation_mode)
        self.upd()

    def key(self, names, frame):
        for n in names:
            pb = self.P[n]
            pb.keyframe_insert("location", frame=frame, group=n)
            if pb.rotation_mode == 'QUATERNION':
                pb.keyframe_insert("rotation_quaternion", frame=frame, group=n)
            else:
                pb.keyframe_insert("rotation_euler", frame=frame, group=n)


def _new_action(rig, name):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    if rig.animation_data is None:
        rig.animation_data_create()
    rig.animation_data.action = act
    return act


def _cyclic(act):
    for fc in _fcurves(act):
        for k in fc.keyframe_points:
            k.interpolation = 'BEZIER'
        fc.modifiers.new('CYCLES')


def _fcurves(act):
    if hasattr(act, "fcurves") and len(getattr(act, "fcurves", [])):
        return list(act.fcurves)
    out = []
    for layer in getattr(act, "layers", []):
        for strip in layer.strips:
            for bag in strip.channelbags:
                out += list(bag.fcurves)
    return out


def walk(rig, frames=32, stride=0.62, lift=0.11):
    po = Poser(rig)
    act = _new_action(rig, "Walk")
    keyed = ["pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "head"]
    for s in (".L", ".R"):
        keyed += ["foot_ik" + s, "foot_heel" + s, "foot_ball" + s, "upperarm" + s, "forearm" + s, "hand" + s,
                  "clavicle" + s]
    stance = 0.6
    for f in range(0, frames + 1, 2):
        ph = f / frames
        po.reset()
        for s, sg, off in ((".L", 1, 0.0), (".R", -1, 0.5)):
            p = (ph + off) % 1.0
            if p < stance:                                    # planted: slides back under the body
                u = p / stance
                y = -stance * 0 - stride / 2 + stride * u
                z = 0.0
                heel = 14 * max(0.0, 1 - u / 0.12)                                 # toes up at heel strike
                ball = 28 * max(0.0, (u - 0.72) / 0.28) ** 1.4                   # heel peels off before toe-off
            else:                                             # swing: forward through the air
                u = (p - stance) / (1 - stance)
                e = 0.5 - 0.5 * math.cos(math.pi * u)
                y = stride / 2 - stride * e
                z = lift * math.sin(math.pi * u) ** 1.3
                heel = 12 * max(0.0, (u - 0.6) / 0.4)
                ball = 28 * max(0.0, 1 - u / 0.25)
            po.move_world("foot_ik" + s, (0, y, z))
            po.rot_local("foot_heel" + s, (1, 0, 0), heel)
            po.rot_local("foot_ball" + s, (1, 0, 0), ball)
        w = 2 * math.pi * ph
        po.move_world("pelvis", (0.022 * math.sin(w), 0.0, -0.03 + 0.016 * math.cos(2 * w)))
        po.rot_world("pelvis", (0, 0, 1), 6 * math.sin(w))
        po.rot_world("pelvis", (0, 1, 0), -3.5 * math.sin(w))
        po.rot_world("spine_02", (0, 0, 1), -5 * math.sin(w))
        po.rot_world("spine_03", (0, 0, 1), -5 * math.sin(w))
        po.rot_world("spine_01", (1, 0, 0), 4)
        po.rot_world("head", (0, 0, 1), 2 * math.sin(w))
        for s, sg, off in ((".L", 1, 0.5), (".R", -1, 0.0)):
            sw = math.sin(2 * math.pi * (ph + off) + math.pi / 2)
            po.rot_world("upperarm" + s, (0, 1, 0), sg * 47)          # hang the arm from the A-pose
            po.rot_world("upperarm" + s, (1, 0, 0), 12 - 17 * sw)      # swing opposite the leg
            po.rot_local("forearm" + s, (1, 0, 0), -(6 + 16 * max(0.0, sw)))
        po.key(keyed, f + 1)
    _cyclic(act)
    act.frame_range = (1, frames + 1)
    po.reset()
    log(f"  action Walk: {frames} frames, stride {stride * 2:.2f} m per cycle")
    return act


def idle(rig, frames=96):
    po = Poser(rig)
    act = _new_action(rig, "Idle")
    keyed = ["pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "head"]
    for s in (".L", ".R"):
        keyed += ["upperarm" + s, "forearm" + s, "hand" + s, "clavicle" + s]
    for f in range(0, frames + 1, 8):
        ph = f / frames
        w = 2 * math.pi * ph
        po.reset()
        po.move_world("pelvis", (0.012 * math.sin(w), 0, -0.004))
        po.rot_world("spine_03", (1, 0, 0), -1.4 * math.sin(2 * w))       # breathing
        po.rot_world("spine_02", (0, 1, 0), 1.2 * math.sin(w))
        po.rot_world("head", (0, 0, 1), 4 * math.sin(w))
        po.rot_world("head", (1, 0, 0), 2 * math.sin(2 * w + 1))
        for s, sg in ((".L", 1), (".R", -1)):
            po.rot_world("clavicle" + s, (1, 0, 0), 0.8 * math.sin(2 * w))
            po.rot_world("upperarm" + s, (0, 1, 0), sg * 48)
            po.rot_world("upperarm" + s, (1, 0, 0), -3 + 1.5 * math.sin(w))
            po.rot_local("forearm" + s, (1, 0, 0), -14)
            po.rot_local("hand" + s, (0, 1, 0), sg * -30)
        po.key(keyed, f + 1)
    _cyclic(act)
    act.frame_range = (1, frames + 1)
    po.reset()
    log(f"  action Idle: {frames} frames")
    return act


def make_actions(rig):
    acts = [idle(rig), walk(rig)]
    # keep both reachable: NLA tracks (muted) for the exporter, nothing active so the rig opens in its rest pose
    ad = rig.animation_data
    for a in acts:
        tr = ad.nla_tracks.new()
        tr.name = a.name
        st = tr.strips.new(a.name, int(a.frame_range[0]), a)
        tr.mute = True
    ad.action = None
    Poser(rig).reset()             # unassigning an action leaves the bones in its last evaluated pose
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 1, 33
    scene.render.fps = 30
    return acts
