"""The clip for someone sat at the yard's garden table playing cards (Game/NpcSeat.cs, Level.BuildYardCardTable).

  SitCards   full body, loops (10 s): sat in a garden chair (seat top SEAT_Z, the figure's origin on the ground under the
             middle of the seat), leaning in over the table. The left hand holds a fan of cards up in front of the chest,
             palm to the face; the right hand fingers them, draws one, leans out and lays it on the table, and comes back.
             Now and then they look up across the table.

The cards themselves are drawn by the game on hand.L (Npcs.DrawCards): the fan stands up out of the hand along its Y axis
(wrist -> knuckles) on the palm side (-Z).

Conventions as animlib.py: the figure faces -Y, its left is +X, up is +Z.
"""
import math

from mathutils import Vector

from .animlib import ALL_SET, _build, wrapbump
from .combatlib import _arm_ik, _curl, _set_hand

SEAT_Z = 0.43           # top of the chair's seat (build_garden_furniture.py)
TABLE_Z = 0.72          # top of the table
TABLE_AT = -0.80        # the table's middle, from the chair's middle (along the way they face)

# The left hand holding the fan up in front of the chest, the right beside it.
L_WRIST = Vector((0.085, -0.33, 0.86))
L_Y, L_Z = Vector((-0.40, -0.20, 0.90)), Vector((0.25, -0.95, 0.15))
R_WRIST = Vector((-0.07, -0.36, 0.84))
R_Y, R_Z = Vector((0.55, -0.35, 0.75)), Vector((-0.55, -0.70, 0.30))
# Laying a card down: out over the table, palm down.
LAY_WRIST = Vector((-0.06, -0.58, TABLE_Z + 0.07))
LAY_Y, LAY_Z = Vector((0.15, -0.90, -0.40)), Vector((0.0, 0.35, 0.95))
L_POLE = Vector((0.85, 0.25, -0.6))
R_POLE = Vector((-0.85, 0.25, -0.6))
GRIP_FAN = {"thumb": 35, "index": 40, "middle": 48, "ring": 55, "pinky": 60}
GRIP_LOOSE = {"thumb": 15, "index": 20, "middle": 28, "ring": 34, "pinky": 40}
GRIP_PINCH = {"thumb": 30, "index": 28, "middle": 40, "ring": 55, "pinky": 62}


def _seat(po, lean=0.0, breathe=0.0):
    """Sat down: thighs along the seat, shins straight down, feet a little apart and turned out; leaning in."""
    for s, sg in ((".L", 1), (".R", -1)):
        po.move_world("foot_ik" + s, (sg * 0.035, -0.42, 0))
        po.rot_world("foot_ik" + s, (0, 0, 1), sg * 9)
    po.move_world("pelvis", (0, 0.02, -0.43))
    po.rot_world("pelvis", (1, 0, 0), -4)
    po.rot_world("spine_01", (1, 0, 0), 9 + 0.4 * lean)
    po.rot_world("spine_02", (1, 0, 0), 8 + 0.6 * breathe + 0.6 * lean)
    po.rot_world("spine_03", (1, 0, 0), 5 - 0.8 * breathe)
    po.rot_world("neck_01", (1, 0, 0), 6)
    po.rot_world("head", (1, 0, 0), 12)


def _smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def _band(ph, a, b, c, d):
    """0 -> 1 between a and b, 1 until c, back to 0 at d."""
    return _smooth((ph - a) / (b - a)) * (1 - _smooth((ph - c) / (d - c)))


def sit_cards(rig, snap):
    F = 300    # 10 s

    def pose(po, f, ph):
        w = 2 * math.pi * ph
        reach = _band(ph, 0.50, 0.62, 0.66, 0.78)          # the right hand out to the table and back
        draw = _band(ph, 0.40, 0.45, 0.50, 0.56)           # pulling a card out of the fan first
        glance = wrapbump(ph, 0.90, 0.035)                  # a look up across the table
        _seat(po, lean=10 * reach, breathe=math.sin(3 * w))
        po.rot_world("spine_03", (0, 0, 1), -6 * reach + 1.5 * math.sin(w))
        # The head: down at the cards, following the hand out to the table, up across it now and then.
        po.rot_world("head", (1, 0, 0), -16 * glance + 4 * reach)
        po.rot_world("head", (0, 0, 1), 10 * glance - 8 * reach + 2.5 * math.sin(2 * w + 0.7))
        po.rot_world("neck_01", (1, 0, 0), -6 * glance)

        fidget = Vector((0.006 * math.sin(5 * w), 0.004 * math.sin(3 * w + 1), 0.008 * math.sin(4 * w + 2)))
        left = L_WRIST + Vector((0.01 * math.sin(2 * w), 0, 0.01 * math.sin(w))) + Vector((0.0, 0.0, 0.03)) * glance
        right = R_WRIST + fidget + Vector((0.01, 0.01, 0.05)) * draw
        right = right.lerp(LAY_WRIST, reach)
        _arm_ik(po, ".L", left, L_POLE)
        _arm_ik(po, ".R", right, R_POLE)
        _set_hand(po, ".L", L_Y, L_Z)
        _set_hand(po, ".R", R_Y.lerp(LAY_Y, reach), R_Z.lerp(LAY_Z, reach))
        _curl(po, ".L", GRIP_FAN)
        pinch = max(draw, reach * (1 - _smooth((ph - 0.64) / 0.03)))
        _curl(po, ".R", {k: GRIP_LOOSE[k] + (GRIP_PINCH[k] - GRIP_LOOSE[k]) * pinch for k in GRIP_LOOSE})
        if f == 0:
            po.upd()
            for bone in ("pelvis", "thigh.L", "calf.L", "foot.L", "upperarm.L", "upperarm.R", "head"):
                print(f"[seated] {bone} at {tuple(round(c, 3) for c in po.P[bone].matrix.translation)}", flush=True)
            for side, wrist in ((".R", right), (".L", left)):
                print(f"[seated] hand{side} missed by {(po.P['hand' + side].matrix.translation - wrist).length:.3f} m", flush=True)
        if f == 180:
            po.upd()
            print(f"[seated] laying: hand.R missed by {(po.P['hand.R'].matrix.translation - right).length:.3f} m", flush=True)

    return _build(rig, "SitCards", F, pose, ALL_SET, step=5, loop=True, snap=snap)


CLIPS = {"SitCards": sit_cards}
