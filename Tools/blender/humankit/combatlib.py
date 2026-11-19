"""Combat clips for armed NPCs: AimPistol (a held two-handed pistol aim, loops) and FirePistol (one shot's recoil, a
one-shot). Both key the upper body only (spine_02 and up, like Talk), so the game plays them as a masked overlay on top
of Idle / Walk / Run: people can aim while standing, walking or running.

The arms are placed with a two-bone IK solve (shoulder -> elbow -> wrist, elbows dropped down and out), then the hands
are turned to hold the grip: the right hand's knuckle line points along the barrel (forward, -Y), the left hand cups
it from the left and below. The game attaches the pistol to hand.R and points it along the NPC's aim, so the exact
grip geometry is set there; these clips only have to bring the hands together in front of the eyes.

Conventions as animlib.py: the figure faces -Y, its left is +X, up is +Z.
"""
import math

from mathutils import Matrix, Vector

from .animlib import FINGERS, UPPER_SET, _build

# Wrist targets in the Idle-relative body frame (metres): right hand out in front of the right eye, left hand under and
# just left of it. Shoulders are at about (+-0.18, 0, 1.42); arm 0.295 + forearm 0.255 = 0.55 m reach.
RIGHT_WRIST = Vector((-0.07, -0.50, 1.45))
LEFT_WRIST = Vector((-0.015, -0.45, 1.415))
# Where the elbows point (added to the arm's mid point to choose the bend plane).
RIGHT_POLE = Vector((-0.6, 0.2, -1.0))
LEFT_POLE = Vector((0.7, 0.2, -1.0))


def _align(po, name, want):
    """Rotate bone `name` about its head (in world space) so its Y axis points along `want`."""
    pb = po.P[name]
    po.upd()
    cur = pb.matrix.col[1].to_3d().normalized()
    want = want.normalized()
    axis = cur.cross(want)
    s = axis.length
    if s < 1e-7:
        return
    ang = math.degrees(math.atan2(s, cur.dot(want)))
    po.rot_world(name, tuple(axis / s), ang)


def _arm_ik(po, side, target, pole):
    """Point upperarm<side> at the elbow and forearm<side> at `target` (wrist), the elbow bent towards `pole`."""
    up, fo = po.P["upperarm" + side], po.P["forearm" + side]
    po.upd()
    shoulder = up.matrix.translation.copy()
    a, b = up.bone.length, fo.bone.length
    d = target - shoulder
    dist = min(d.length, (a + b) * 0.999)
    dn = d.normalized()
    # elbow: law of cosines, in the plane of (shoulder->target, pole)
    cos_a = (a * a + dist * dist - b * b) / (2 * a * dist)
    cos_a = max(-1.0, min(1.0, cos_a))
    sin_a = math.sqrt(1 - cos_a * cos_a)
    bend = (pole - dn * pole.dot(dn)).normalized()
    elbow = shoulder + dn * (a * cos_a) + bend * (a * sin_a)
    _align(po, "upperarm" + side, elbow - shoulder)
    po.upd()
    _align(po, "forearm" + side, target - fo.matrix.translation)


def _set_hand(po, side, y_dir, z_dir):
    """Turn hand<side> so its Y axis (wrist -> knuckles) is `y_dir` and its Z axis is as close to `z_dir` as allowed."""
    pb = po.P["hand" + side]
    po.upd()
    y = y_dir.normalized()
    z = (z_dir - y * z_dir.dot(y)).normalized()
    x = y.cross(z)
    M = pb.matrix.copy()
    R = Matrix((x, y, z)).transposed().to_4x4()
    R.translation = M.translation
    pb.matrix = R
    po.upd()


def _curl(po, side, amounts):
    """Finger curl in degrees per finger (all three joints), added to the current pose like Talk's relaxed curl."""
    for fn in FINGERS:
        c = amounts[fn]
        for i in (1, 2, 3):
            pb = po.P[f"{fn}_0{i}{side}"]
            k = c * (0.8 if i == 1 else 1.0)
            pb.rotation_euler[0] += math.radians(k if fn != "thumb" else k * 0.5)
    po.upd()


def aim_pose(po, kick=0.0, sway=0.0, breathe=0.0):
    """The two-handed aim. `kick` 0..1 is the recoil (muzzle up and back), `sway` and `breathe` small idle motion."""
    # torso squared up behind the gun, a touch forward, head down to the sights
    po.rot_world("spine_02", (1, 0, 0), 4 + 1.0 * breathe - 2.5 * kick)
    po.rot_world("spine_03", (1, 0, 0), 3 - 2.0 * kick)
    po.rot_world("spine_03", (0, 0, 1), -4 + 0.6 * sway)
    po.rot_world("neck_01", (1, 0, 0), 2)
    po.rot_world("head", (1, 0, 0), 2 - 2 * kick)
    po.rot_world("head", (0, 1, 0), 4)                   # cant the head slightly towards the right-hand sights
    for s, sg in ((".L", 1), (".R", -1)):
        po.rot_world("clavicle" + s, (1, 0, 0), -4)      # shoulders forward into the aim
    off = Vector((0.004 * sway, 0.035 * kick, 0.045 * kick + 0.004 * breathe))
    _arm_ik(po, ".R", RIGHT_WRIST + off, RIGHT_POLE)
    _arm_ik(po, ".L", LEFT_WRIST + off, LEFT_POLE)
    # right hand: knuckles along the barrel, back of the hand to the right; the muzzle flips up with the kick
    fwd = Vector((0.02, -1.0, -0.08 + 0.45 * kick))
    _set_hand(po, ".R", fwd, Vector((-1.0, 0.0, 0.35)))
    # left hand: angled in under the right, palm up and in
    _set_hand(po, ".L", Vector((-0.45, -1.0, -0.05 + 0.45 * kick)), Vector((0.55, 0.0, -1.0)))
    _curl(po, ".R", {"thumb": 25, "index": 30, "middle": 72, "ring": 76, "pinky": 80})   # index on the trigger
    _curl(po, ".L", {"thumb": 18, "index": 55, "middle": 60, "ring": 64, "pinky": 66})


def aim_pistol(rig, snap):
    """Looping hold of the aim with a slow sway and breathing (4 s)."""
    F = 120

    def pose(po, f, ph):
        w = 2 * math.pi * ph
        aim_pose(po, sway=math.sin(w) + 0.4 * math.sin(3 * w), breathe=math.sin(2 * w))

    return _build(rig, "AimPistol", F, pose, UPPER_SET, step=4, loop=True, snap=snap)


def fire_pistol(rig, snap):
    """One shot: the kick peaks at frame 2 (0.067 s) and settles back to the aim by frame 9 (0.3 s)."""
    F = 9

    def pose(po, f, ph):
        k = math.exp(-((f - 2.0) / 1.4) ** 2) if f <= 2 else math.exp(-((f - 2.0) / 2.6) ** 2)
        if f == 0 or f == F:
            k = 0.0
        aim_pose(po, kick=k)

    return _build(rig, "FirePistol", F, pose, UPPER_SET, step=1, loop=False, snap=snap)


CLIPS = {"AimPistol": aim_pistol, "FirePistol": fire_pistol}
