"""Reaction and extra idle clips for the human rig: HitFront, HitBack, HitLeg (full-body flinches when shot and not
killed), Startle (the jump of fright when someone first notices danger), ReloadPistol (an upper-body overlay for armed
NPCs, from the aim back to the aim), and two idle variants, CheckWatch and Stretch.

Authored with the same `Poser` toolkit and conventions as animlib.py (the figure faces -Y, its left is +X, up is +Z;
rot_world about +X by a positive angle leans an upright part forward and swings a hanging limb back). Every full-body clip
starts and ends exactly on the pose of Idle frame 1 (an envelope that is 0 at both ends), so the game can cross-fade
in and out of it from anything. ReloadPistol starts and ends exactly on combatlib.aim_pose(), the first frame of
AimPistol, so it slots between two aims without a pop.
"""
import math

from mathutils import Vector

from .animlib import ALL_SET, FINGERS, SIDES, UPPER_SET, _build, apply_snapshot, bump, lerp, ramp
from .combatlib import LEFT_POLE, LEFT_WRIST, RIGHT_POLE, RIGHT_WRIST, _arm_ik, _curl, _set_hand, aim_pose

ARM_CHAIN = {s: ["clavicle" + s, "upperarm" + s, "forearm" + s, "hand" + s] + [f"{f}_0{i}{s}" for f in FINGERS for i in (1, 2, 3)]
             for s in (".L", ".R")}


# ------------------------------------------------------------------------------------------------ helpers
def _capture(po, names):
    po.upd()
    return {n: po.P[n].matrix.copy() for n in names}


def _blend_world(po, names, start, target, a):
    """Set each bone (parents first) to its armature-space matrix part way from `start` to `target`."""
    if a <= 1e-4:
        return
    for n in names:
        s, t = start[n], target[n]
        loc = s.translation.lerp(t.translation, a)
        rot = s.to_quaternion().slerp(t.to_quaternion(), a)
        m = rot.to_matrix().to_4x4()
        m.translation = loc
        po.P[n].matrix = m
        po.upd()


def _arm_to(po, snap, side, build, a):
    """Pose one arm by `build(po)` (an IK placement on the Idle pose) and blend from the Idle arm to it by `a`, so the
    clip leaves the Idle pose without a pop however differently the IK lays out the elbow."""
    names = ARM_CHAIN[side]
    if a <= 1e-4:
        return
    idle = _capture(po, names)
    build(po)
    target = _capture(po, names)
    for n in names:                      # back to the Idle arm, then blend
        pb = po.P[n]
        loc, quat, eul = snap[n]
        pb.location, pb.rotation_quaternion, pb.rotation_euler = loc, quat, eul
    po.upd()
    _blend_world(po, names, idle, target, a)


def _flail(po, e, fwd, out, bend):
    """Arms thrown by a jolt: swung forward (negative = back), out to the sides, elbows bent."""
    for s, sg in SIDES:
        po.rot_world("upperarm" + s, (1, 0, 0), -fwd * e)
        po.rot_world("upperarm" + s, (0, 1, 0), sg * out * e)
        po.rot_local("forearm" + s, (1, 0, 0), -bend * e)


# ------------------------------------------------------------------------------------------------ hit reactions
def hit_front(rig, snap):
    """Shot from the front: the chest is punched back, the head snaps back, the arms fly up and out, the right foot
    steps back to catch the weight, then everything recovers to Idle (0.6 s)."""
    F = 18

    def pose(po, f, ph):
        e = ramp(f, 0, 2) * (1 - ramp(f, 4, 18))            # the jolt: full in 2 frames, gone by the end
        lag = ramp(f, 1, 4) * (1 - ramp(f, 6, 18))          # the head and arms follow a beat later
        step = bump(f, 2, 7, 9, 17)
        arc = math.sin(math.pi * min(1.0, max(0.0, (f - 2) / 5.0))) if 2 <= f <= 7 else 0.0
        po.move_world("foot_ik.R", (0.0, 0.13 * step, 0.06 * arc))
        po.rot_local("foot_heel.R", (1, 0, 0), 10 * arc)
        po.move_world("pelvis", (0.0, 0.05 * e + 0.03 * step, -0.035 * e - 0.02 * step))
        po.rot_world("pelvis", (1, 0, 0), -4 * e)
        po.rot_world("spine_01", (1, 0, 0), -4 * e)
        po.rot_world("spine_02", (1, 0, 0), -9 * e)
        po.rot_world("spine_03", (1, 0, 0), -8 * e)
        po.rot_world("spine_03", (0, 0, 1), 5 * lag)
        po.rot_world("neck_01", (1, 0, 0), -5 * lag)
        po.rot_world("head", (1, 0, 0), -14 * lag)
        _flail(po, lag, 24, 14, 32)

    return _build(rig, "HitFront", F, pose, ALL_SET, step=1, snap=snap)


def hit_back(rig, snap):
    """Shot from behind: the chest is thrown forward with the head whipping back, then forward; the left foot lurches
    a step forward to stay up, arms flung back, and recovery to Idle (0.6 s)."""
    F = 18

    def pose(po, f, ph):
        e = ramp(f, 0, 2) * (1 - ramp(f, 4, 18))
        lag = ramp(f, 2, 5) * (1 - ramp(f, 7, 18))
        whip = bump(f, 0, 2, 2, 5)                           # the head lags behind the chest at first
        step = bump(f, 2, 7, 9, 17)
        arc = math.sin(math.pi * min(1.0, max(0.0, (f - 2) / 5.0))) if 2 <= f <= 7 else 0.0
        po.move_world("foot_ik.L", (0.0, -0.15 * step, 0.07 * arc))
        po.rot_local("foot_heel.L", (1, 0, 0), 12 * arc)
        po.move_world("pelvis", (0.0, -0.06 * e - 0.04 * step, -0.03 * e - 0.02 * step))
        po.rot_world("pelvis", (1, 0, 0), 5 * e)
        po.rot_world("spine_01", (1, 0, 0), 5 * e)
        po.rot_world("spine_02", (1, 0, 0), 10 * e)
        po.rot_world("spine_03", (1, 0, 0), 9 * e)
        po.rot_world("spine_03", (0, 0, 1), -4 * lag)
        po.rot_world("neck_01", (1, 0, 0), -6 * whip + 6 * lag)
        po.rot_world("head", (1, 0, 0), -10 * whip + 10 * lag)
        _flail(po, lag, -22, 16, 20)

    return _build(rig, "HitBack", F, pose, ALL_SET, step=1, snap=snap)


def hit_leg(rig, snap):
    """Shot in the leg: the right knee gives (pelvis drops, the hip on that side sags), the body pitches over it, the
    arms go out for balance, and they push themselves back up (0.8 s)."""
    F = 24

    def pose(po, f, ph):
        e = ramp(f, 0, 3) * (1 - ramp(f, 9, 24))
        lag = ramp(f, 2, 6) * (1 - ramp(f, 10, 24))
        po.rot_local("foot_ball.R", (1, 0, 0), 14 * e)        # heel comes up as the knee goes
        po.move_world("pelvis", (-0.035 * e, 0.0, -0.13 * e))
        po.rot_world("pelvis", (0, 1, 0), -7 * e)             # right hip drops
        po.rot_world("pelvis", (1, 0, 0), 8 * e)
        po.rot_world("spine_01", (1, 0, 0), 4 * e)
        po.rot_world("spine_02", (1, 0, 0), 6 * e)
        po.rot_world("spine_02", (0, 1, 0), 4 * e)            # leaning back over to the good side
        po.rot_world("spine_03", (1, 0, 0), 5 * e)
        po.rot_world("spine_03", (0, 1, 0), 3 * e)
        po.rot_world("head", (1, 0, 0), 10 * lag)             # looking down at it
        po.rot_world("head", (0, 0, 1), -10 * lag)
        _flail(po, lag, 8, 28, 26)
        po.rot_world("upperarm.R", (1, 0, 0), -26 * lag)      # the right hand reaches down to the wound
        po.rot_local("forearm.R", (1, 0, 0), -20 * lag)

    return _build(rig, "HitLeg", F, pose, ALL_SET, step=1, snap=snap)


def startle(rig, snap):
    """A jump of fright: shoulders up, a crouching flinch back with the hands thrown up in front of the face, a half step
    back, then easing towards Idle (0.8 s). The game plays it for the reaction time and fades into running or cowering,
    so the first half carries it."""
    F = 24

    def pose(po, f, ph):
        e = ramp(f, 0, 3) * (1 - ramp(f, 12, 24))
        hands = ramp(f, 1, 5) * (1 - ramp(f, 13, 24))
        step = bump(f, 1, 5, 10, 20)
        arc = math.sin(math.pi * min(1.0, max(0.0, (f - 1) / 4.0))) if 1 <= f <= 5 else 0.0
        po.move_world("foot_ik.R", (-0.02 * step, 0.10 * step, 0.05 * arc))
        po.move_world("pelvis", (0.0, 0.05 * e, -0.07 * e))
        po.rot_world("pelvis", (1, 0, 0), 6 * e)
        po.rot_world("spine_02", (1, 0, 0), -6 * e)
        po.rot_world("spine_03", (1, 0, 0), -8 * e)
        po.rot_world("neck_01", (1, 0, 0), 8 * e)              # head pulled in
        po.rot_world("head", (1, 0, 0), -4 * e)
        for s, sg in SIDES:
            po.rot_world("clavicle" + s, (0, 1, 0), -sg * 9 * e)   # shoulders up
            po.rot_world("upperarm" + s, (1, 0, 0), -62 * hands)
            po.rot_world("upperarm" + s, (0, 1, 0), sg * 10 * hands)
            po.rot_local("forearm" + s, (1, 0, 0), -70 * hands)
            po.rot_local("hand" + s, (1, 0, 0), 25 * hands)        # palms out

    return _build(rig, "Startle", F, pose, ALL_SET, step=1, snap=snap)


# ------------------------------------------------------------------------------------------------ idle variants
def check_watch(rig, snap):
    """Raises the left wrist in front of the chest, turns it and looks down at the watch, lowers it (4 s)."""
    F = 120
    wrist = Vector((0.07, -0.30, 1.16))
    pole = Vector((1.0, 0.3, -0.6))

    def raise_arm(po):
        _arm_ik(po, ".L", wrist, pole)
        _set_hand(po, ".L", Vector((-1.0, -0.35, 0.05)), Vector((0.0, 0.45, 1.0)))   # back of the hand up, to the eyes
        _curl(po, ".L", {"thumb": 10, "index": 18, "middle": 22, "ring": 24, "pinky": 26})

    def pose(po, f, ph):
        a = bump(f, 10, 34, 82, 108)
        look = bump(f, 26, 40, 76, 96)
        _arm_to(po, snap, ".L", raise_arm, a)
        po.rot_world("spine_03", (0, 0, 1), 4 * a)
        po.rot_world("spine_03", (1, 0, 0), 2 * look)
        po.rot_world("neck_01", (1, 0, 0), 8 * look)
        po.rot_world("head", (1, 0, 0), 16 * look)
        po.rot_world("head", (0, 0, 1), 10 * look)
        po.rot_world("head", (1, 0, 0), -2 * look * math.sin(2 * math.pi * (f - 40) / 36.0) * bump(f, 40, 46, 70, 76))

    return _build(rig, "CheckWatch", F, pose, ALL_SET, step=3, snap=snap)


def stretch(rig, snap):
    """Arms up over the head and a lean back, a roll of the neck, back down to Idle (5 s)."""
    F = 150

    def pose(po, f, ph):
        a = bump(f, 12, 46, 84, 118)
        roll = bump(f, 96, 106, 128, 140)
        w = 2 * math.pi * (f - 96) / 34.0
        po.move_world("pelvis", (0.0, -0.015 * a, 0.012 * a))  # up on the toes a little
        po.rot_local("foot_ball.L", (1, 0, 0), 8 * a)
        po.rot_local("foot_ball.R", (1, 0, 0), 8 * a)
        po.rot_world("spine_01", (1, 0, 0), -3 * a)
        po.rot_world("spine_02", (1, 0, 0), -4 * a)
        po.rot_world("spine_03", (1, 0, 0), -5 * a)
        po.rot_world("head", (1, 0, 0), -10 * a)
        po.rot_world("neck_01", (0, 1, 0), 10 * roll * math.sin(w))
        po.rot_world("head", (1, 0, 0), 6 * roll * math.cos(w))
        for s, sg in SIDES:
            po.rot_world("clavicle" + s, (0, 1, 0), -sg * 10 * a)
            po.rot_local("forearm" + s, (1, 0, 0), 26 * a)        # straighten the Idle's bent elbow
            po.rot_world("upperarm" + s, (1, 0, 0), -158 * a)     # forward and up over the head
            po.rot_world("upperarm" + s, (0, 1, 0), sg * 14 * a)
            po.rot_local("hand" + s, (1, 0, 0), -20 * a)
            for fn in FINGERS:                                      # fingers spread straight
                for i in (1, 2, 3):
                    po.P[f"{fn}_0{i}{s}"].rotation_euler[0] -= math.radians(10 * a)
        po.upd()

    return _build(rig, "Stretch", F, pose, ALL_SET, step=3, snap=snap)


# ------------------------------------------------------------------------------------------------ ReloadPistol
def _lerpv(a, b, t):
    return a.lerp(b, t)


def reload_pistol(rig, snap):
    """From the aim: the gun comes in to the chest, muzzle up; the left hand drops to the belt for a magazine, brings it
    up and seats it with a push, reaches over and racks the slide back, and both hands go back out into the aim (2.2 s,
    upper body only, like AimPistol)."""
    F = 66
    chest_r = Vector((-0.06, -0.33, 1.20))         # the gun hand held in, under the chin
    belt = Vector((0.15, -0.08, 0.97))             # left hand at the magazine pouch on the belt
    under = Vector((0.0, 0.0, -0.12))              # the magazine well, below the right wrist
    slide = Vector((0.03, -0.05, 0.06))            # over the top of the slide

    def pose(po, f, ph):
        if f == 0 or f == F:
            aim_pose(po)
            return
        tin = ramp(f, 0, 10) * (1 - ramp(f, 52, 64))          # the gun in to the chest and back out
        rw = _lerpv(RIGHT_WRIST, chest_r, tin)
        # left hand: out of the aim to the belt, up under the gun, a push, over the slide, a pull, back to the aim
        lw = LEFT_WRIST.copy()
        if f <= 14:
            lw = _lerpv(LEFT_WRIST, belt, ramp(f, 2, 14))
        elif f <= 32:
            lw = _lerpv(belt, rw + under, ramp(f, 18, 30))
        elif f <= 38:
            lw = rw + under + Vector((0, 0, 0.03 * bump(f, 32, 34, 35, 38)))
        elif f <= 48:
            lw = _lerpv(rw + under, rw + slide, ramp(f, 38, 43)) + Vector((0, 0.07 * bump(f, 43, 46, 46, 48), 0))
        else:
            lw = _lerpv(rw + slide, LEFT_WRIST, ramp(f, 48, 62))
        look = tin
        po.rot_world("spine_02", (1, 0, 0), 4 + 2 * look)
        po.rot_world("spine_03", (1, 0, 0), 3 + 2 * look)
        po.rot_world("spine_03", (0, 0, 1), -4 + 6 * look)
        po.rot_world("neck_01", (1, 0, 0), 2 + 8 * look)
        po.rot_world("head", (1, 0, 0), 2 + 14 * look)
        po.rot_world("head", (0, 1, 0), 4 * (1 - look))
        for s in (".L", ".R"):
            po.rot_world("clavicle" + s, (1, 0, 0), -4)
        _arm_ik(po, ".R", rw, RIGHT_POLE)
        _arm_ik(po, ".L", lw, LEFT_POLE.lerp(Vector((1.0, 0.4, -0.8)), bump(f, 2, 10, 50, 60)))
        # gun hand: knuckles from along the barrel (aim) to tipped up and across (muzzle up and left, port to the eyes)
        y = Vector((0.02, -1.0, -0.08)).lerp(Vector((0.35, -0.55, 0.75)), tin)
        z = Vector((-1.0, 0.0, 0.35)).lerp(Vector((-0.6, 0.6, 0.3)), tin)
        _set_hand(po, ".R", y, z)
        grab = bump(f, 12, 16, 30, 34) + bump(f, 40, 43, 46, 49)   # holding the magazine, then pinching the slide
        cup = 1 - ramp(f, 2, 8) * (1 - ramp(f, 54, 62))
        ly = Vector((-0.45, -1.0, -0.05)).lerp(Vector((0.0, -0.4, 1.0)), 1 - cup)
        lz = Vector((0.55, 0.0, -1.0)).lerp(Vector((0.0, -1.0, 0.0)), 1 - cup)
        _set_hand(po, ".L", ly, lz)
        _curl(po, ".R", {"thumb": 25, "index": lerp(30, 12, tin), "middle": 72, "ring": 76, "pinky": 80})
        _curl(po, ".L", {k: lerp(v, 70, grab * (1 - cup)) for k, v in
                         {"thumb": 18, "index": 55, "middle": 60, "ring": 64, "pinky": 66}.items()})

    return _build(rig, "ReloadPistol", F, pose, UPPER_SET, step=1, snap=snap)


CLIPS = {"HitFront": hit_front, "HitBack": hit_back, "HitLeg": hit_leg, "Startle": startle,
         "CheckWatch": check_watch, "Stretch": stretch, "ReloadPistol": reload_pistol}
# upper-body overlays: the game masks them to spine_02 and below, and the build drops the lower body's channels
OVERLAYS = ("ReloadPistol",)
