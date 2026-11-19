"""The extra animation clips for the human rig (CONTRACTS section 3): Run, IdleLook, IdleShift, ShuffleTurn, Cower,
DeathFwd, DeathBack and Talk. Authored with the same `Poser` toolkit as Walk and Idle (humankit/anim.py): every key is a
pose built from world-space rotations and moves on top of a starting pose, then keyed.

Conventions (the figure faces -Y, its left side is +X, up is +Z):
  * rot_world about +X by a positive angle leans / swings forward-to-back: a body part pointing up goes toward -Y
    (forward); a limb pointing down goes toward +Y (backward). So "lean forward" is +X and "swing an arm back" is +X.
  * about +Y a positive angle tips the top toward +X (the figure's left) and drops the left side.
  * about +Z a positive angle turns the figure to its left (counter-clockwise from above).
  * a knee flexes with a positive world-X rotation of the calf, a hip flexes with a negative one of the thigh.

Clips that start and end on the Idle pose are built on top of the pose of Idle frame 1 (`idle_snapshot`, read from the
existing Idle action, never re-derived) with an envelope that is exactly 0 at both ends.
"""
import math

import bpy
import numpy as np
from mathutils import Vector

from .anim import Poser, _new_action, _cyclic, _fcurves, log  # noqa: F401

FPS = 30
SIDES = ((".L", 1), (".R", -1))
FINGERS = ("thumb", "index", "middle", "ring", "pinky")
IDLE_SET = ["pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "head"] + \
    [b + s for s in (".L", ".R") for b in ("clavicle", "upperarm", "forearm", "hand")]
FINGER_SET = [f"{f}_0{i}{s}" for s in (".L", ".R") for f in FINGERS for i in (1, 2, 3)]
LEG_IK_SET = [b + s for s in (".L", ".R") for b in ("foot_ik", "foot_heel", "foot_ball")]
LEG_FK_SET = [b + s for s in (".L", ".R") for b in ("thigh", "calf", "foot", "ball")]
UPPER_SET = ["spine_02", "spine_03", "neck_01", "head"] + \
    [b + s for s in (".L", ".R") for b in ("clavicle", "upperarm", "forearm", "hand")] + FINGER_SET
IK_PROPS = ("ik_leg.L", "ik_leg.R")
# every clip but Talk keys all of these, so no bone can keep a stale pose from the clip exported before it
ALL_SET = IDLE_SET + LEG_IK_SET + LEG_FK_SET + FINGER_SET


# ------------------------------------------------------------------------------------------------ helpers
def smooth(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3 - 2 * x)


def ramp(f, a, b):
    """0 before frame a, 1 after frame b, smoothstep in between."""
    return smooth((f - a) / float(b - a)) if b > a else (1.0 if f >= b else 0.0)


def bump(f, a, b, c, d):
    """0 -> 1 between a and b, 1 until c, back to 0 at d."""
    return ramp(f, a, b) * (1.0 - ramp(f, c, d))


def lerp(a, b, t):
    return a + (b - a) * t


def wrapbump(ph, centre, width):
    """A periodic gaussian bump (phase 0..1) so loops close exactly."""
    d = (ph - centre + 0.5) % 1.0 - 0.5
    return math.exp(-0.5 * (d / width) ** 2)


def idle_snapshot(rig):
    """The local pose of every bone at frame 1 of the existing Idle action."""
    act = bpy.data.actions["Idle"]
    ad = rig.animation_data
    ad.action = act
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()
    snap = {}
    for pb in rig.pose.bones:
        snap[pb.name] = (tuple(pb.location), tuple(pb.rotation_quaternion), tuple(pb.rotation_euler))
    ad.action = None
    Poser(rig).reset()
    return snap


def apply_snapshot(po, snap):
    for n, (loc, quat, eul) in snap.items():
        pb = po.P[n]
        pb.location = loc
        pb.rotation_quaternion = quat
        pb.rotation_euler = eul
    po.upd()


def _set_ik(rig, value):
    root = rig.pose.bones["root"]
    for k in IK_PROPS:
        root[k] = float(value)


def _key_ik(rig, frame, value):
    root = rig.pose.bones["root"]
    for k in IK_PROPS:
        root[k] = float(value)
        root.keyframe_insert(f'["{k}"]', frame=frame, group="root")


def _constant_ik_curves(act):
    for fc in _fcurves(act):
        if "ik_leg" in fc.data_path:
            for k in fc.keyframe_points:
                k.interpolation = 'CONSTANT'


def _build(rig, name, frames, pose_fn, keyed, step=2, loop=False, snap=None, ik=1.0, extra_keys=()):
    """Create action `name`: for every key frame f (0..frames) reset the rig (to the Idle frame 1 pose when `snap`),
    call pose_fn(po, f, f/frames), key `keyed`. Frame f is stored at Blender frame f + 1 (like Walk and Idle)."""
    po = Poser(rig)
    act = _new_action(rig, name)
    fr = sorted(set(list(range(0, frames, step)) + [frames] + list(extra_keys)))
    for f in fr:
        po.reset()
        _set_ik(rig, ik)
        if snap is not None:
            apply_snapshot(po, snap)
        pose_fn(po, f, f / frames)
        po.key(keyed, f + 1)
        _key_ik(rig, f + 1, ik)
    for fc in _fcurves(act):
        for k in fc.keyframe_points:
            k.interpolation = 'BEZIER'
    _constant_ik_curves(act)
    if loop:
        for fc in _fcurves(act):
            if "ik_leg" not in fc.data_path:
                fc.modifiers.new('CYCLES')
    act.frame_range = (1, frames + 1)
    _set_ik(rig, 1.0)
    po.reset()
    log(f"  action {name}: {frames} frames ({frames / FPS:.3f} s), {len(fr)} keys")
    return act


def _hang_arm(po, s, sg, deg=47):
    """Bring the arm down from the A-pose (as Walk does)."""
    po.rot_world("upperarm" + s, (0, 1, 0), sg * deg)


# ------------------------------------------------------------------------------------------------ Run
RUN_FRAMES = 22
RUN_STANCE = 5                       # frames of foot contact (22.7 % of the cycle): the longest stance the legs can reach
RUN_SWING = RUN_FRAMES - RUN_STANCE  # without crouching, since the planted foot must travel back at 3.68 m/s
RUN_SPEED = 2.70 / (RUN_FRAMES / FPS)            # m/s: the planted foot goes back at this speed
RUN_D = RUN_SPEED * RUN_STANCE / FPS             # how far the foot travels back during the stance (0.61 m)
RUN_PELVIS_Z = -0.055                            # pelvis height relative to standing, at contact / toe-off


def _hermite(v, y0, y1, m0, m1):
    h00 = 2 * v ** 3 - 3 * v ** 2 + 1
    h10 = v ** 3 - 2 * v ** 2 + v
    h01 = -2 * v ** 3 + 3 * v ** 2
    h11 = v ** 3 - v ** 2
    return h00 * y0 + h10 * m0 + h01 * y1 + h11 * m1


def _spline(v, pts, m_start, m_end):
    """Cubic Hermite through pts [(v, value)] with finite-difference tangents (given tangents at the two ends)."""
    n = len(pts)
    m = [m_start] + [(pts[i + 1][1] - pts[i - 1][1]) / (pts[i + 1][0] - pts[i - 1][0]) for i in range(1, n - 1)] + [m_end]
    i = max(j for j in range(n - 1) if pts[j][0] <= v)
    v0, y0 = pts[i]
    v1, y1 = pts[i + 1]
    h = v1 - v0
    return _hermite((v - v0) / h, y0, y1, m[i] * h, m[i + 1] * h)


def run_foot(p):
    """(y, lift z, heel toes-up deg, ball heel-lift deg, plantarflex deg) of a foot control at cycle frame p
    (0 = heel strike). The ankle follows a jogger's loop: heel kicked up behind toward the buttock, knee drives
    forward, shin extends to land slightly ahead of the body."""
    D = RUN_D
    H = D / 2
    if p <= RUN_STANCE:
        u = p / RUN_STANCE
        return -H + D * u, 0.0, 2.0 * max(0.0, 1 - u / 0.3), 18.0 * max(0.0, (u - 0.5) / 0.5) ** 1.6, 0.0
    v = (p - RUN_STANCE) / RUN_SWING
    per_v = D * RUN_SWING / RUN_STANCE
    ys = [(0.0, H), (0.14, H + 0.04), (0.28, H + 0.0), (0.46, 0.04), (0.62, -0.17), (0.80, -0.30), (1.0, -H)]
    zs = [(0.0, 0.0), (0.14, 0.22), (0.28, 0.41), (0.46, 0.34), (0.62, 0.16), (0.80, 0.08), (1.0, 0.0)]
    y = _spline(v, ys, 0.5 * per_v, 0.3 * per_v)
    z = max(0.0, _spline(v, zs, 1.6, -0.4))
    heel = 2.0 * smooth((v - 0.8) / 0.2)
    ball = 18.0 * (1 - smooth(v / 0.3))
    plant = 22.0 * math.sin(math.pi * min(1.0, v / 0.6)) if v < 0.6 else 0.0
    return y, z, heel, ball, plant


def run(rig, snap=None):
    F = RUN_FRAMES
    half = F // 2

    def pose(po, f, ph):
        for s, sg, shift in ((".L", 1, 0), (".R", -1, half)):
            p = (f - shift) % F
            y, z, heel, ball, plant = run_foot(p)
            po.move_world("foot_ik" + s, (0, y, z))
            if plant:
                po.rot_world("foot_ik" + s, (1, 0, 0), plant)
            po.rot_local("foot_heel" + s, (1, 0, 0), heel)
            po.rot_local("foot_ball" + s, (1, 0, 0), ball)
        # pelvis: a few cm of bob: highest in flight, lowest around contact / mid-stance
        h = f % half
        if h <= RUN_STANCE:
            dz = RUN_PELVIS_Z - 0.002 * math.sin(math.pi * h / RUN_STANCE)
        else:
            dz = RUN_PELVIS_Z + 0.055 * math.sin(math.pi * (h - RUN_STANCE) / (half - RUN_STANCE))
        w = 2 * math.pi * ph
        sway = math.sin(2 * math.pi * (f + 1) / F)
        po.move_world("pelvis", (0.018 * sway, 0.0, dz))
        po.rot_world("pelvis", (0, 0, 1), -6 * math.cos(w))              # hip of the leg that strikes goes forward
        po.rot_world("pelvis", (0, 1, 0), -3 * sway)                     # swing-side hip drops
        po.rot_world("pelvis", (1, 0, 0), 3)                             # trunk lean: 3 + 2 + 1 + 1 = 7 degrees
        po.rot_world("spine_01", (1, 0, 0), 2)
        po.rot_world("spine_01", (0, 1, 0), 1.5 * sway)
        po.rot_world("spine_02", (1, 0, 0), 1)
        po.rot_world("spine_02", (0, 0, 1), 5 * math.cos(w))
        po.rot_world("spine_03", (1, 0, 0), 1)
        po.rot_world("spine_03", (0, 0, 1), 4 * math.cos(w))
        po.rot_world("neck_01", (0, 0, 1), -2 * math.cos(w))
        po.rot_world("head", (1, 0, 0), -5)                                # eyes level again
        po.rot_world("head", (0, 0, 1), 3 * math.cos(w))
        for s, sg, off in ((".L", 1, 0.0), (".R", -1, 0.5)):
            c = math.cos(2 * math.pi * (ph + off))                           # +1: arm back (the opposite foot is forward)
            po.rot_world("clavicle" + s, (0, 0, 1), -sg * 2 * c)
            _hang_arm(po, s, sg, 46)
            po.rot_world("upperarm" + s, (1, 0, 0), -4 + 30 * c)
            po.rot_local("forearm" + s, (1, 0, 0), -(85 - 8 * c))
            po.rot_local("hand" + s, (0, 1, 0), sg * -30)

    keyed = ALL_SET
    return _build(rig, "Run", F, pose, keyed, step=1, loop=True, snap=None)


# ------------------------------------------------------------------------------------------------ idle variants
def _idle_pose(po, snap):
    po.reset()
    apply_snapshot(po, snap)


def idle_look(rig, snap):
    """Looks left (head 50 deg, chest 15), holds, looks right, back. Starts and ends on Idle frame 1."""
    F = 150

    def pose(po, f, ph):
        a = ramp(f, 12, 32) - 2 * ramp(f, 62, 90) + ramp(f, 118, 142)      # +1 left, -1 right
        live = bump(f, 10, 30, 118, 142)                                    # a little breathing while it looks
        po.rot_world("spine_02", (0, 0, 1), 6 * a)
        po.rot_world("spine_03", (0, 0, 1), 9 * a)                         # chest: 15 degrees
        po.rot_world("spine_03", (1, 0, 0), -1.0 * live * math.sin(2 * math.pi * 3 * ph))
        po.rot_world("neck_01", (0, 0, 1), 15 * a)
        po.rot_world("head", (0, 0, 1), 20 * a)                            # head: 50 degrees in all
        po.rot_world("head", (1, 0, 0), -2.5 * abs(a))                     # eyes up a touch while looking
        for s, sg in SIDES:
            po.rot_world("clavicle" + s, (0, 0, 1), -1.5 * a)

    return _build(rig, "IdleLook", F, pose, ALL_SET, step=3, snap=snap)


def idle_shift(rig, snap):
    """Weight onto the right leg (pelvis 4 cm, hip drop), the left foot relaxes; holds, and back."""
    F = 120

    def pose(po, f, ph):
        s_ = bump(f, 10, 38, 80, 108)
        sway = s_ * (1 + 0.08 * math.sin(2 * math.pi * (f - 38) / 42.0) * bump(f, 38, 46, 76, 80))
        # relaxed free (left) foot: a little forward and out, heel lifted; the right foot stays planted
        po.move_world("foot_ik.L", (0.035 * s_, -0.045 * s_, 0))
        po.rot_world("foot_ik.L", (0, 0, 1), 8 * s_)
        po.rot_local("foot_ball.L", (1, 0, 0), 9 * s_)
        po.move_world("pelvis", (-0.040 * sway, 0.0, -0.014 * s_))
        po.rot_world("pelvis", (0, 1, 0), 4.0 * sway)                      # left hip drops
        po.rot_world("pelvis", (0, 0, 1), 2.0 * s_)
        po.rot_world("spine_01", (0, 1, 0), -2.0 * sway)
        po.rot_world("spine_02", (0, 1, 0), -1.6 * sway)
        po.rot_world("spine_03", (0, 1, 0), -1.0 * sway)
        po.rot_world("spine_03", (0, 0, 1), -2.0 * s_)
        po.rot_world("head", (0, 1, 0), 0.8 * sway)
        po.rot_world("head", (1, 0, 0), 1.2 * s_ * math.sin(2 * math.pi * 2 * ph))
        po.rot_world("clavicle.L", (0, 1, 0), 1.5 * sway)
        po.rot_world("upperarm.L", (1, 0, 0), 2.5 * sway)
        po.rot_world("upperarm.R", (1, 0, 0), -2.0 * sway)

    return _build(rig, "IdleShift", F, pose, ALL_SET, step=3, snap=snap)


def shuffle_turn(rig, snap):
    """Two small in-place steps (left foot lands at 0.1 s, right at 0.4667 s) while the body twists 14 degrees toward the
    turn and unwinds: no net rotation. The game turns the heading while it plays."""
    F = 24

    def pr(f, a, b):
        return min(1.0, max(0.0, (f - a) / float(b - a)))

    def pose(po, f, ph):
        uw = 1 - ramp(f, 15, 24)
        # left foot: lifts and lands at frame 3, yawed and a little forward / out
        u = ramp(f, 0, 3) * uw
        arcL = math.sin(math.pi * pr(f, 0, 3)) if f <= 3 else 0.0
        po.move_world("foot_ik.L", (0.04 * u, -0.06 * u, 0.07 * arcL))
        po.rot_world("foot_ik.L", (0, 0, 1), 22 * u)
        po.rot_local("foot_heel.L", (1, 0, 0), 12 * arcL)
        # right foot: lifts at 9 and lands at 14
        u = ramp(f, 9, 14) * uw
        arcR = math.sin(math.pi * pr(f, 9, 14)) if 9 <= f <= 14 else 0.0
        po.move_world("foot_ik.R", (0.0, -0.05 * u, 0.08 * arcR))
        po.rot_world("foot_ik.R", (0, 0, 1), 22 * u)
        po.rot_local("foot_heel.R", (1, 0, 0), 12 * arcR)
        # weight over the planted foot
        q = ramp(f, 0, 8) * uw
        po.move_world("pelvis", (-0.022 * arcL + 0.022 * arcR, 0.0, -0.012 * q))
        po.rot_world("pelvis", (0, 0, 1), 14 * q)
        po.rot_world("spine_02", (0, 0, 1), -7 * q)
        po.rot_world("spine_03", (0, 0, 1), -3 * q)
        po.rot_world("head", (0, 0, 1), 12 * ramp(f, 0, 6) * (1 - ramp(f, 10, 22)))
        for s, sg in SIDES:
            po.rot_world("upperarm" + s, (1, 0, 0), sg * 3 * math.sin(math.pi * pr(f, 0, 14)) * uw)

    return _build(rig, "ShuffleTurn", F, pose, ALL_SET, step=1, snap=snap)


# ------------------------------------------------------------------------------------------------ Cower
def _arms_over_head(po, amount=1.0):
    for s, sg in SIDES:
        _hang_arm(po, s, sg, 47)
        po.rot_world("upperarm" + s, (1, 0, 0), -118 * amount)
        po.rot_world("upperarm" + s, (0, 0, 1), sg * 18 * amount)
        po.rot_local("forearm" + s, (1, 0, 0), -(125 * amount + 14 * (1 - amount)))
        po.rot_local("hand" + s, (0, 1, 0), sg * -20 * amount)


def cower(rig, snap):
    """Crouched (pelvis down 0.45 m), hunched, arms over the head, a slight tremble."""
    F = 60

    def pose(po, f, ph):
        w = 2 * math.pi * ph

        def tr(k, p0):
            return math.sin(w * k + p0)
        po.move_world("foot_ik.L", (0.07, -0.02, 0))
        po.move_world("foot_ik.R", (-0.07, -0.02, 0))
        po.rot_world("foot_ik.L", (0, 0, 1), 12)
        po.rot_world("foot_ik.R", (0, 0, 1), -12)
        po.move_world("pelvis", (0.004 * tr(5, 0.3), 0.15 + 0.002 * tr(5, 1.1), -0.45 + 0.004 * tr(4, 2.0)))
        po.rot_world("pelvis", (1, 0, 0), 24 + 1.0 * tr(5, 0.7))
        po.rot_world("spine_01", (1, 0, 0), 14 + 1.2 * tr(4, 1.9))
        po.rot_world("spine_02", (1, 0, 0), 12 + 1.0 * tr(5, 2.7))
        po.rot_world("spine_02", (0, 0, 1), 1.5 * tr(5, 0.2))
        po.rot_world("spine_03", (1, 0, 0), 10 + 1.2 * tr(5, 0.4))
        po.rot_world("spine_03", (0, 1, 0), 1.5 * tr(4, 1.3))
        po.rot_world("neck_01", (1, 0, 0), 10)
        po.rot_world("head", (1, 0, 0), 8 + 1.5 * tr(5, 1.7))
        po.rot_world("head", (0, 0, 1), 2.0 * tr(5, 2.2))
        _arms_over_head(po, 1.0)
        for s, sg in SIDES:
            po.rot_world("clavicle" + s, (0, 1, 0), sg * 1.2 * tr(5, 0.9 + sg))
            po.rot_world("upperarm" + s, (0, 0, 1), 1.5 * tr(5, 0.5 + sg))

    return _build(rig, "Cower", F, pose, ALL_SET, step=2, loop=True, snap=snap)


# ------------------------------------------------------------------------------------------------ deaths
PIN_BONES = {"feet": ("ball.L", "ball.R"), "knees": ("calf.L", "calf.R")}


def rot_rel(po, name, ref, axis, deg):
    """Rotate `name` about its head around `axis` given in the frame of bone `ref` (so 'swing the arm forward' stays
    forward for the chest even when the body is lying down)."""
    po.upd()
    R = po.P[ref].matrix.to_3x3() @ po.rig.data.bones[ref].matrix_local.to_3x3().inverted()
    po.rot_world(name, R @ Vector(axis), deg)


def _min_z(body):
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    return float(co.reshape(-1, 3)[:, 2].min())


def _ground(po, body, minz0, state, pins):
    """Keep the body on the ground: the pelvis moves so the lowest vertex stays where it is at rest and, for each pin
    with weight > 0, those bones keep the y they had when the pin started."""
    for k, w in pins.items():
        if w <= 0:
            continue
        y = sum(po.P[b].head.y for b in PIN_BONES[k]) / len(PIN_BONES[k])
        state.setdefault(k, y)
        po.move_world("pelvis", (0, (state[k] - y) * w, 0))
    dz = -(_min_z(body) - minz0)
    po.move_world("pelvis", (0, 0, dz))


def _death(rig, snap, name, pose_fn, F=54):
    body = bpy.data.objects["Human"]
    po = Poser(rig)
    _set_ik(rig, 0.0)
    _idle_pose(po, snap)
    minz0 = _min_z(body)
    po.reset()
    state = {}

    def pose(po_, f, ph):
        pins = pose_fn(po_, f, ph)
        _ground(po_, body, minz0, state, pins)

    return _build(rig, name, F, pose, ALL_SET, step=1, snap=snap, ik=0.0)


def death_fwd(rig, snap):
    """Knees buckle into a kneel, the body falls forward over them and lies prone."""
    def pose(po, f, ph):
        jolt = bump(f, 0, 3, 3, 9)
        kb = ramp(f, 4, 20)                                      # buckle
        fall = ramp(f, 20, 40)
        theta = 14 * kb + 76 * fall + 3 * bump(f, 42, 46, 46, 54)      # pelvis pitch forward, 90 at the end
        phi_t = -8 * kb + 98 * ramp(f, 24, 44)                   # thigh angle in the world (+ = swung back)
        phi_c = 90 * kb                                          # shin: horizontal behind the knee
        po.rot_world("pelvis", (1, 0, 0), theta)
        po.rot_world("spine_01", (1, 0, 0), 5 * ramp(f, 14, 30) - 4 * ramp(f, 40, 54))
        po.rot_world("spine_02", (1, 0, 0), -8 * jolt + 4 * ramp(f, 14, 30))
        po.rot_world("spine_03", (1, 0, 0), -9 * jolt + 3 * ramp(f, 14, 30))
        po.rot_world("neck_01", (1, 0, 0), -4 * jolt)
        po.rot_world("head", (1, 0, 0), -10 * jolt - 20 * ramp(f, 24, 44))
        po.rot_local("neck_01", (0, 1, 0), 30 * ramp(f, 40, 54))
        po.rot_local("head", (0, 1, 0), 22 * ramp(f, 40, 54))
        for s, sg in SIDES:
            po.rot_world("thigh" + s, (1, 0, 0), phi_t - theta + 2 * sg * kb)
            po.rot_world("calf" + s, (1, 0, 0), phi_c - phi_t)
            po.rot_world("foot" + s, (1, 0, 0), 55 * kb + 15 * ramp(f, 40, 54))
            brace = bump(f, 26, 34, 36, 46)
            rot_rel(po, "upperarm" + s, "spine_03", (1, 0, 0), -75 * brace + 16 * ramp(f, 44, 54))
            rot_rel(po, "upperarm" + s, "spine_03", (0, 1, 0), -sg * (10 * brace + 22 * ramp(f, 40, 54)))
            po.rot_local("forearm" + s, (1, 0, 0), -20 * brace + 14 * ramp(f, 44, 54))   # lying: the arm straightens
        return {"knees": 1.0 if f >= 20 else 0.0}

    return _death(rig, snap, "DeathFwd", pose)


def death_back(rig, snap):
    """Staggers back a step, falls backward, ends supine."""
    def pose(po, f, ph):
        jolt = bump(f, 0, 4, 4, 12)
        theta = -(12 * ramp(f, 2, 12) + 8 * ramp(f, 12, 24) + 70 * ramp(f, 24, 42)) + 2 * bump(f, 42, 46, 46, 54)
        step = bump(f, 4, 12, 20, 28)                             # the stagger: the right foot goes back
        lf = ramp(f, 24, 44)                                      # legs follow the body down
        arm = bump(f, 4, 14, 30, 50)
        po.rot_world("pelvis", (0, 1, 0), 3 * bump(f, 6, 14, 24, 34))
        po.rot_world("pelvis", (1, 0, 0), theta)
        po.rot_world("spine_01", (1, 0, 0), 5 * jolt - 3 * ramp(f, 40, 54))
        po.rot_world("spine_02", (1, 0, 0), 6 * jolt)
        po.rot_world("spine_03", (1, 0, 0), -7 * jolt + 4 * ramp(f, 38, 54))
        po.rot_world("neck_01", (1, 0, 0), -6 * jolt + 6 * ramp(f, 26, 40))
        po.rot_world("head", (1, 0, 0), -8 * jolt + 8 * ramp(f, 26, 44) - 6 * ramp(f, 44, 54))
        po.rot_local("neck_01", (0, 1, 0), -18 * ramp(f, 40, 54))
        for s, sg in SIDES:
            back = step if s == ".R" else 0.0
            bend = bump(f, 6, 14, 24, 34) if s == ".L" else 0.0
            phi_t = (0.35 * theta * ramp(f, 12, 34) + 24 * back - 8 * bend) * (1 - lf) + theta * lf
            knee = 8 + 24 * back + 14 * bend * (1 - lf) + 26 * ramp(f, 16, 34) * (1 - lf)
            po.rot_world("thigh" + s, (1, 0, 0), phi_t - theta)
            po.rot_world("calf" + s, (1, 0, 0), knee)
            po.rot_world("foot" + s, (1, 0, 0), -8 * back - 6 * ramp(f, 30, 54))
            rot_rel(po, "upperarm" + s, "spine_03", (0, 1, 0), -sg * (50 * arm + 24 * ramp(f, 40, 54)))
            rot_rel(po, "upperarm" + s, "spine_03", (1, 0, 0), 28 * arm)
            po.rot_local("forearm" + s, (1, 0, 0), -14 * arm)
        return {"feet": 1 - ramp(f, 26, 36)}

    return _death(rig, snap, "DeathBack", pose)


# ------------------------------------------------------------------------------------------------ Talk
def talk(rig, snap):
    """Upper-body gestures only (spine_02 and up are keyed; below that the Idle pose stays)."""
    F = 120

    def pose(po, f, ph):
        w = 2 * math.pi * ph
        g1 = wrapbump(ph, 0.16, 0.055)           # right hand: an explaining gesture
        g2 = wrapbump(ph, 0.46, 0.07)            # both hands open
        g3 = wrapbump(ph, 0.78, 0.05)            # left hand small beat
        g4 = wrapbump(ph, 0.93, 0.03)
        nod = 0.6 * g1 + 0.8 * g2 + 0.5 * g3 + 0.4 * g4
        po.rot_world("spine_02", (0, 0, 1), 2.5 * math.sin(w) + 2 * g2)
        po.rot_world("spine_03", (0, 0, 1), 3 * math.sin(w + 1.0) - 3 * g1)
        po.rot_world("spine_03", (1, 0, 0), 2.0 * nod - 1.0 * math.sin(2 * w))
        po.rot_world("neck_01", (0, 0, 1), 4 * math.sin(w + 0.5))
        po.rot_world("head", (0, 0, 1), 5 * math.sin(w + 0.8) - 3 * g1 + 3 * g3)
        po.rot_world("head", (1, 0, 0), 5 * nod)
        po.rot_world("head", (0, 1, 0), 2.5 * math.sin(w + 2.0))
        for s, sg in SIDES:
            po.rot_world("clavicle" + s, (0, 0, 1), -sg * (2 * g1 if s == ".R" else 2 * g3 + 2 * g2))
            _hang_arm(po, s, sg, 48)
        a = 1.0 * g1 + 0.9 * g2                  # right hand
        po.rot_world("upperarm.R", (1, 0, 0), -3 - 38 * a)
        po.rot_world("upperarm.R", (0, 1, 0), 10 * a + 3 * g1)
        po.rot_local("forearm.R", (1, 0, 0), -(14 + 78 * a + 10 * g4))
        po.rot_local("hand.R", (0, 1, 0), 30 * (1 - a) - 18 * g2)
        po.rot_local("hand.R", (1, 0, 0), 14 * g1 * math.sin(w * 8))
        b = 0.9 * g2 + 0.7 * g3 + 0.25 * g4      # left hand
        po.rot_world("upperarm.L", (1, 0, 0), -3 - 32 * b)
        po.rot_world("upperarm.L", (0, 1, 0), -8 * b)
        po.rot_local("forearm.L", (1, 0, 0), -(14 + 68 * b))
        po.rot_local("hand.L", (0, 1, 0), -30 * (1 - b) + 18 * g2)
        po.rot_local("hand.L", (1, 0, 0), 10 * g3 * math.sin(w * 6))
        for s, sg in SIDES:                      # fingers: a relaxed curl that eases while the hand is active
            act = a if s == ".R" else b
            for fn in FINGERS:
                base = 20 if fn != "thumb" else 8
                for i in (1, 2, 3):
                    c = (base + 6 * i) * (1 - 0.6 * act) + 5 * act * math.sin(w * 4 + i)
                    pb = po.P[f"{fn}_0{i}{s}"]
                    pb.rotation_euler[0] += math.radians(c if fn != "thumb" else c * 0.5)
        po.upd()

    return _build(rig, "Talk", F, pose, UPPER_SET, step=2, loop=True, snap=snap)


CLIPS = {"Run": run, "IdleLook": idle_look, "IdleShift": idle_shift, "ShuffleTurn": shuffle_turn,
         "Cower": cower, "DeathFwd": death_fwd, "DeathBack": death_back, "Talk": talk}
