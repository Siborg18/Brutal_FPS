"""The clips for someone on the M249 on the gun nest's tripod (Game/GunNest.cs, ArmedNpc: Game/NpcGunner.cs).

  ManGun          full body, loops: knees bent and feet staggered behind the tripod, leaning in with the butt in the right
                  shoulder, right hand on the grip, left hand flat on the left of the receiver by the feed tray
  ManGunReload    upper body, 5.5 s (ArmedNpc.GunReloadTime), starting and ending on ManGun: the left hand goes up to the feed
                  cover, down and out to the side for a fresh belt, back to the cover and home

The gun doesn't move with the clip: it's on the tripod, where these hands have to meet it. So the figure is placed by the
game from the tripod: its origin stands TRIPOD_BACK behind the pintle and GUN_RIGHT to the left of the line of the gun.
Those two, and the cradle's height, are repeated in GunNest (SeatBack, SeatRight, Level.GunNestCradle).

Conventions as animlib.py: the figure faces -Y, its left is +X, up is +Z. A hand's Y axis runs from the wrist to the
knuckles, its Z axis comes out of the back of the hand.
"""
import math

from mathutils import Vector

from .animlib import ALL_SET, UPPER_SET, _build
from .combatlib import _arm_ik, _curl, _set_hand

TRIPOD_BACK = 0.80      # m from the figure's origin forward to the pintle
GUN_RIGHT = 0.13        # m to the figure's right of its middle, the line of the gun
CRADLE_Z = 1.06         # the height of the cradle: Level.GunNestCradle
MOUNT = (0.20, -0.02)   # where the gun sits on the cradle, in its own space: GunNest.MountPoint


def gun_point(x, y, z=0.0):
    """A point in the gun's model space (+X the muzzle, +Y up, +Z its right), with the gun level on the tripod, in the figure's space."""
    return Vector((-GUN_RIGHT - z, -(TRIPOD_BACK + x - MOUNT[0]), CRADLE_Z + y - MOUNT[1]))


# The right wrist behind the grip: ArmedNpc.GunInHand holds the gun's grip (-0.060, -0.082) 7.5 cm along the hand from the
# wrist and 1 cm under it. The left wrist behind the receiver's left side, by the feed tray.
R_WRIST = gun_point(-0.060 - 0.075, -0.082 + 0.01)
L_WRIST = gun_point(0.005, 0.012, -0.062)
COVER_WRIST = gun_point(-0.02, 0.105, -0.045)   # over the feed cover
BELT_WRIST = gun_point(-0.12, -0.26, -0.34)     # out to the side, for a tin
R_POLE = Vector((-0.9, 0.25, -0.75))
L_POLE = Vector((0.85, 0.35, -0.7))
R_Y, R_Z = Vector((0.0, -1.0, -0.02)), Vector((-1.0, 0.0, 0.35))
L_Y, L_Z = Vector((-0.10, -1.0, 0.02)), Vector((1.0, 0.0, 0.42))
GRIP_R = {"thumb": 25, "index": 30, "middle": 72, "ring": 76, "pinky": 80}   # index along the trigger
GRIP_L = {"thumb": 20, "index": 14, "middle": 16, "ring": 18, "pinky": 20}   # open: laid along it


def _smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def _stance(po, breathe=0.0, sway=0.0):
    """Legs and back: the left foot forward, the right back and turned out, knees bent, leaning in over the gun with the
    head up behind the sights. Only the upper body moves with the breath (the hands stay on the gun)."""
    po.move_world("foot_ik.L", (0.03, -0.14, 0))
    po.rot_world("foot_ik.L", (0, 0, 1), 10)
    po.move_world("foot_ik.R", (-0.10, 0.24, 0))
    po.rot_world("foot_ik.R", (0, 0, 1), -28)
    po.move_world("pelvis", (-0.01, -0.03, -0.27))
    po.rot_world("pelvis", (1, 0, 0), 18)
    po.rot_world("pelvis", (0, 0, 1), -8)
    po.rot_world("spine_01", (1, 0, 0), 12)
    po.rot_world("spine_02", (1, 0, 0), 11 + 0.7 * breathe)
    po.rot_world("spine_02", (0, 0, 1), -5)
    po.rot_world("spine_03", (1, 0, 0), 8 + 0.5 * sway)
    po.rot_world("spine_03", (0, 0, 1), -5)
    po.rot_world("neck_01", (1, 0, 0), -14)
    po.rot_world("neck_01", (0, 0, 1), 8)
    po.rot_world("head", (1, 0, 0), -16)
    po.rot_world("head", (0, 0, 1), 8)
    po.rot_world("head", (0, 1, 0), 8)                  # the head canted over towards the stock
    po.rot_world("clavicle.R", (0, 0, 1), 4)
    po.rot_world("clavicle.R", (1, 0, 0), 6)
    po.rot_world("clavicle.L", (1, 0, 0), 2)


def _on_gun(po, left=None, left_y=None, left_z=None, grip_l=None):
    _arm_ik(po, ".R", R_WRIST, R_POLE)
    _arm_ik(po, ".L", left if left is not None else L_WRIST, L_POLE)
    _set_hand(po, ".R", R_Y, R_Z)
    _set_hand(po, ".L", left_y if left_y is not None else L_Y, left_z if left_z is not None else L_Z)
    _curl(po, ".R", GRIP_R)
    _curl(po, ".L", grip_l if grip_l is not None else GRIP_L)


def man_gun(rig, snap):
    F = 120

    def pose(po, f, ph):
        w = 2 * math.pi * ph
        _stance(po, math.sin(2 * w), math.sin(w))
        _on_gun(po)
        if f == 0:
            po.upd()
            for bone in ("upperarm.R", "upperarm.L", "head", "pelvis"):
                print(f"[gunner] {bone} at {tuple(round(c, 3) for c in po.P[bone].matrix.translation)}", flush=True)
            print(f"[gunner] butt at {tuple(round(c, 3) for c in gun_point(-0.352, -0.019))}, hand.R to {tuple(round(c, 3) for c in R_WRIST)}, "
                  f"hand.L to {tuple(round(c, 3) for c in L_WRIST)}", flush=True)
            for side, wrist in ((".R", R_WRIST), (".L", L_WRIST)):
                reached = po.P["hand" + side].matrix.translation
                print(f"[gunner] hand{side} missed by {(reached - wrist).length:.3f} m", flush=True)

    return _build(rig, "ManGun", F, pose, ALL_SET, step=4, loop=True, snap=snap)


def man_gun_reload(rig, snap):
    F = 165    # 5.5 s

    def pose(po, f, ph):
        _stance(po)
        up = _smooth((ph - 0.03) / 0.10) * (1 - _smooth((ph - 0.86) / 0.10))
        out = _smooth((ph - 0.20) / 0.16) * (1 - _smooth((ph - 0.56) / 0.18))
        wrist = L_WRIST.lerp(COVER_WRIST, up).lerp(BELT_WRIST, out)
        y = L_Y.lerp(Vector((-0.15, -0.8, -0.5)), up).lerp(Vector((0.3, -0.5, -0.8)), out)
        z = L_Z.lerp(Vector((0.1, -0.5, 1.0)), up).lerp(Vector((1.0, 0.0, 0.4)), out)
        grip = {k: GRIP_L[k] + (v - GRIP_L[k]) * up for k, v in {"thumb": 25, "index": 45, "middle": 50, "ring": 54, "pinky": 58}.items()}
        _on_gun(po, wrist, y, z, grip)

    return _build(rig, "ManGunReload", F, pose, UPPER_SET, step=3, loop=False, snap=snap)


CLIPS = {"ManGun": man_gun, "ManGunReload": man_gun_reload}
