"""Two-handed rifle clips for armed NPCs, and the two the prison intro's raiders need. All key the upper body only, so the
game plays them as masked overlays on Idle / Walk.

  AimRifle        the AK shouldered, cheek on the stock, left hand on the handguard (loops)
  FireRifle       one shot's kick, from the aim and back to it
  ReloadRifle     gun down a little, left hand to the belt and back, starting and ending on the aim
  PushDoor        both palms pushing something away, the arms straightening, leaning in (holds its last frame)
  DrawRifleBack   right hand over the shoulder to the rifle slung on the back, round the right side, shouldered; the left
                  hand meets it. It ends exactly on rifle_pose(), so AimRifle follows without a pop.

The game draws the gun from the right hand: Game/ArmedNpc.cs. Before GRAB_FRAME of DrawRifleBack it's on the back: the game
places it there from where this clip's hand is at that frame, so the hand arrives exactly on the grip.

Conventions as animlib.py: the figure faces -Y, its left is +X, up is +Z. A hand's Y axis runs from the wrist to the
knuckles, its Z axis comes out of the back of the hand.
"""
import math

from mathutils import Quaternion, Vector

from .animlib import UPPER_SET, _build
from .combatlib import _arm_ik, _curl, _set_hand

# The aim. Shoulders are at about (+-0.18, 0, 1.42) before the torso turns; the arm reaches 0.55 m.
R_WRIST = Vector((-0.095, -0.27, 1.49))
L_WRIST = Vector((-0.04, -0.50, 1.475))
R_POLE = Vector((-0.9, 0.25, -0.55))
L_POLE = Vector((0.65, 0.1, -1.0))
R_Y, R_Z = Vector((0.03, -1.0, 0.0)), Vector((-1.0, 0.0, 0.35))
L_Y, L_Z = Vector((-0.34, -0.90, 0.22)), Vector((0.5, 0.0, -1.0))
GRIP_R = {"thumb": 25, "index": 30, "middle": 72, "ring": 76, "pinky": 80}   # index along the trigger
GRIP_L = {"thumb": 25, "index": 52, "middle": 58, "ring": 62, "pinky": 66}

# The rifle slung on the back: the hand reaching for its grip behind the right shoulder, the barrel running down to the
# left hip behind the plate carrier.
GRAB_FRAME = 13
BACK_WRIST = Vector((-0.17, 0.215, 1.515))
BACK_Y, BACK_Z = Vector((0.55, 0.08, -0.83)), Vector((0.0, 1.0, 0.0))
REACH_POLE = Vector((-0.45, -0.75, 0.75))


def _smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def _quat(y, z):
    """The rotation with the hand's Y and Z axes along these (as combatlib._set_hand makes it)."""
    from mathutils import Matrix
    y = y.normalized()
    z = (z - y * z.dot(y)).normalized()
    return Matrix((y.cross(z), y, z)).transposed().to_quaternion()


def _set_hand_quat(po, side, q):
    pb = po.P["hand" + side]
    po.upd()
    m = q.to_matrix().to_4x4()
    m.translation = pb.matrix.translation
    pb.matrix = m
    po.upd()


def _torso(po, w=1.0, kick=0.0, sway=0.0, breathe=0.0):
    """Squared up behind the gun: chest turned to the right, left shoulder forward, leaning in, cheek down on the stock."""
    po.rot_world("spine_02", (1, 0, 0), w * 5 + w * 1.0 * breathe - 2.0 * kick)
    po.rot_world("spine_03", (1, 0, 0), w * 4 - 1.5 * kick)
    po.rot_world("spine_02", (0, 0, 1), w * -8)
    po.rot_world("spine_03", (0, 0, 1), w * -10 + 0.6 * sway)
    po.rot_world("neck_01", (0, 0, 1), w * 9)
    po.rot_world("head", (0, 0, 1), w * 9)
    po.rot_world("neck_01", (1, 0, 0), w * 3)
    po.rot_world("head", (1, 0, 0), w * 9 - 1.5 * kick)
    po.rot_world("head", (0, 1, 0), w * 9)              # the head canted over onto the stock
    po.rot_world("clavicle.R", (0, 0, 1), w * 4)
    po.rot_world("clavicle.L", (1, 0, 0), w * -5)


def rifle_pose(po, kick=0.0, sway=0.0, breathe=0.0, dip=0.0):
    """The shouldered aim. `kick` 0..1 is the recoil, `sway`, `breathe` small idle motion, `dip` 0..1 the gun lowered."""
    _torso(po, 1.0, kick, sway, breathe)
    off = Vector((0.004 * sway, 0.03 * kick + 0.04 * dip, 0.012 * kick + 0.003 * breathe - 0.14 * dip))
    _arm_ik(po, ".R", R_WRIST + off, R_POLE)
    _arm_ik(po, ".L", L_WRIST + off + Vector((0, 0, -0.03 * dip)), L_POLE)
    _set_hand(po, ".R", R_Y + Vector((0, 0, 0.22 * kick - 0.25 * dip)), R_Z)
    _set_hand(po, ".L", L_Y + Vector((0, 0, 0.22 * kick - 0.25 * dip)), L_Z)
    _curl(po, ".R", GRIP_R)
    _curl(po, ".L", GRIP_L)


def aim_rifle(rig, snap):
    F = 120

    def pose(po, f, ph):
        w = 2 * math.pi * ph
        rifle_pose(po, sway=math.sin(w) + 0.4 * math.sin(3 * w), breathe=math.sin(2 * w))

    return _build(rig, "AimRifle", F, pose, UPPER_SET, step=4, loop=True, snap=snap)


def fire_rifle(rig, snap):
    F = 6

    def pose(po, f, ph):
        k = 0.0 if f in (0, F) else math.exp(-((f - 1.5) / 1.5) ** 2)
        rifle_pose(po, kick=k)

    return _build(rig, "FireRifle", F, pose, UPPER_SET, step=1, loop=False, snap=snap)


def reload_rifle(rig, snap):
    """2.2 s: as long as ArmedNpc.ReloadTime. The gun dips, the left hand goes to the belt for a magazine and comes back."""
    F = 66
    BELT = Vector((0.10, -0.17, 1.00))

    def pose(po, f, ph):
        dip = _smooth(ph / 0.15) * (1 - _smooth((ph - 0.82) / 0.18))
        rifle_pose(po, dip=dip)
        away = _smooth((ph - 0.14) / 0.2) * (1 - _smooth((ph - 0.52) / 0.2))
        if away > 0:
            wrist = po.P["hand.L"].matrix.translation.copy().lerp(BELT, away)
            _arm_ik(po, ".L", wrist, L_POLE)
            _set_hand(po, ".L", L_Y.lerp(Vector((-0.2, -0.3, -1.0)), away), L_Z)

    return _build(rig, "ReloadRifle", F, pose, UPPER_SET, step=2, loop=False, snap=snap)


def push_door(rig, snap):
    """1.2 s: palms on the door at chest height, then the arms straighten and the shoulders go in behind them."""
    F = 36

    def pose(po, f, ph):
        e = _smooth(ph / 0.92)
        po.rot_world("spine_02", (1, 0, 0), 6 + 7 * e)
        po.rot_world("spine_03", (1, 0, 0), 4 + 5 * e)
        po.rot_world("neck_01", (1, 0, 0), -3 - 3 * e)
        po.rot_world("head", (1, 0, 0), -3 - 4 * e)
        for s, x, pole in ((".L", 0.2, Vector((0.8, 0.3, -0.7))), (".R", -0.2, Vector((-0.8, 0.3, -0.7)))):
            po.rot_world("clavicle" + s, (1, 0, 0), -3 - 8 * e)
            _arm_ik(po, s, Vector((x, -0.23 - 0.27 * e, 1.33 - 0.015 * e)), pole)
            # Palm forward, fingers up and a little inward.
            _set_hand(po, s, Vector((-x * 0.7, -0.15, 1.0)), Vector((0.0, 1.0, 0.1)))
            _curl(po, s, {"thumb": 8, "index": 6, "middle": 6, "ring": 8, "pinky": 10})

    return _build(rig, "PushDoor", F, pose, UPPER_SET, step=2, loop=False, snap=snap)


def _path(keys, f):
    """Catmull-Rom through keys [(frame, point), ...]: the point at frame f."""
    if f <= keys[0][0]:
        return keys[0][1].copy()
    if f >= keys[-1][0]:
        return keys[-1][1].copy()
    i = next(k for k in range(len(keys) - 1) if keys[k][0] <= f <= keys[k + 1][0])
    p0 = keys[max(i - 1, 0)][1]
    p1, p2 = keys[i][1], keys[i + 1][1]
    p3 = keys[min(i + 2, len(keys) - 1)][1]
    t = (f - keys[i][0]) / (keys[i + 1][0] - keys[i][0])
    return 0.5 * ((2 * p1) + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t)


def _orient(keys, f):
    """Slerp between keys [(frame, quaternion), ...], eased."""
    if f <= keys[0][0]:
        return keys[0][1].copy()
    if f >= keys[-1][0]:
        return keys[-1][1].copy()
    i = next(k for k in range(len(keys) - 1) if keys[k][0] <= f <= keys[k + 1][0])
    return keys[i][1].slerp(keys[i + 1][1], _smooth((f - keys[i][0]) / (keys[i + 1][0] - keys[i][0])))


def draw_rifle_back(rig, snap):
    """1.6 s. Frames 0-13 the reach over the shoulder, 13-18 the rifle lifted off the back, 18-40 round the right side and up
    to the shoulder; the left hand comes up from frame 22 and takes the handguard by frame 40."""
    F = 48
    wrist_keys = [(GRAB_FRAME, BACK_WRIST),
                  (18, Vector((-0.26, 0.17, 1.60))),
                  (24, Vector((-0.42, -0.02, 1.47))),
                  (31, Vector((-0.30, -0.24, 1.33))),
                  (40, R_WRIST)]
    hand_keys = [(GRAB_FRAME, _quat(BACK_Y, BACK_Z)),
                 (19, _quat(Vector((0.05, 0.1, -1.0)), Vector((-0.5, 0.85, 0.0)))),
                 (28, _quat(Vector((-0.1, -0.65, -0.75)), Vector((-1.0, 0.1, 0.1)))),
                 (40, _quat(R_Y, R_Z))]

    def pose(po, f, ph):
        w = _smooth((f - 14) / 24)
        _torso(po, w)
        po.upd()
        # The right arm: from wherever the Idle pose hangs it, up in front of the shoulder and back over it.
        if f < GRAB_FRAME:
            start = po.P["hand.R"].matrix.translation.copy()
            q0 = po.P["hand.R"].matrix.to_quaternion()
            keys = [(0, start), (6, Vector((-0.31, -0.20, 1.40))), (10, Vector((-0.24, -0.02, 1.66))), (GRAB_FRAME, BACK_WRIST)]
            pole = Vector((-0.8, 0.2, -0.6)).lerp(REACH_POLE, _smooth(f / 7))
            _arm_ik(po, ".R", _path(keys, f), pole)
            _set_hand_quat(po, ".R", q0.slerp(_quat(BACK_Y, BACK_Z), _smooth(f / GRAB_FRAME)))
        else:
            pole = REACH_POLE.lerp(R_POLE, _smooth((f - 16) / 18))
            _arm_ik(po, ".R", _path(wrist_keys, f), pole)
            _set_hand_quat(po, ".R", _orient(hand_keys, f))
        # The fingers close on the grip as the hand gets there, and stay closed.
        c = _smooth((f - GRAB_FRAME + 3) / 3)
        _curl(po, ".R", {k: v * c for k, v in GRIP_R.items()})
        # The left arm comes up to the handguard.
        t = _smooth((f - 22) / 18)
        if t > 0:
            start = po.P["hand.L"].matrix.translation.copy()
            q0 = po.P["hand.L"].matrix.to_quaternion()
            _arm_ik(po, ".L", start.lerp(L_WRIST, t), L_POLE)
            _set_hand_quat(po, ".L", q0.slerp(_quat(L_Y, L_Z), t))
            _curl(po, ".L", {k: v * t for k, v in GRIP_L.items()})

    return _build(rig, "DrawRifleBack", F, pose, UPPER_SET, step=1, loop=False, snap=snap)


THROW_RELEASE = 14   # the frame the flare leaves the hand: ArmedNpc.FlareThrowTime


def throw_flare(rig, snap):
    """0.9 s, thrown from the off hand with the gun held low in the right: the left hand comes up from the belt past the
    ear, the chest winding to the left, then over and out in front, chest turning into it, letting go at THROW_RELEASE."""
    F = 27
    wrist_keys = [(0, Vector((0.20, -0.20, 1.12))),
                  (5, Vector((0.30, -0.08, 1.30))),
                  (10, Vector((0.33, 0.14, 1.60))),
                  (THROW_RELEASE, Vector((0.14, -0.52, 1.66))),
                  (18, Vector((0.05, -0.55, 1.34))),
                  (F, Vector((0.17, -0.27, 1.14)))]
    hand_keys = [(0, _quat(Vector((-0.2, -0.5, -0.8)), Vector((1.0, 0.0, -0.2)))),
                 (10, _quat(Vector((-0.2, -0.3, 1.0)), Vector((1.0, 0.3, 0.2)))),
                 (THROW_RELEASE, _quat(Vector((-0.1, -1.0, 0.3)), Vector((0.2, 0.3, 1.0)))),
                 (F, _quat(Vector((-0.2, -0.7, -0.6)), Vector((1.0, 0.0, -0.2))))]

    def pose(po, f, ph):
        wind = _smooth(f / 9) * (1 - _smooth((f - 9) / 5))
        follow = _smooth((f - 10) / 5) * (1 - _smooth((f - 17) / 10))
        turn = 16 * wind - 20 * follow
        po.rot_world("spine_02", (0, 0, 1), turn * 0.45)
        po.rot_world("spine_03", (0, 0, 1), turn * 0.55)
        po.rot_world("spine_02", (1, 0, 0), 3 + 6 * follow - 3 * wind)
        po.rot_world("spine_03", (1, 0, 0), 2 + 4 * follow - 2 * wind)
        po.rot_world("neck_01", (0, 0, 1), -turn * 0.4)
        po.rot_world("head", (0, 0, 1), -turn * 0.5)
        po.upd()
        # The gun held low, out of the way.
        _arm_ik(po, ".R", Vector((-0.24, -0.26, 1.13)), R_POLE)
        _set_hand(po, ".R", Vector((0.1, -0.9, -0.3)), R_Z)
        _curl(po, ".R", GRIP_R)
        _arm_ik(po, ".L", _path(wrist_keys, f), Vector((0.9, 0.35, -0.5)))
        _set_hand_quat(po, ".L", _orient(hand_keys, f))
        # Gripping the flare, then open fingers as it goes.
        o = _smooth((f - THROW_RELEASE) / 3)
        _curl(po, ".L", {k: v * (1 - 0.75 * o) for k, v in {"thumb": 30, "index": 62, "middle": 66, "ring": 68, "pinky": 70}.items()})

    return _build(rig, "ThrowFlare", F, pose, UPPER_SET, step=1, loop=False, snap=snap)


CLIPS = {"AimRifle": aim_rifle, "FireRifle": fire_rifle, "ReloadRifle": reload_rifle, "PushDoor": push_door, "DrawRifleBack": draw_rifle_back,
         "ThrowFlare": throw_flare}
