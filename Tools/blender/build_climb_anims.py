"""
Builds Climb, an NPC going up a ladder, and exports it to its own file, Assets/Models/anims/human_climb.glb, as
build_rifle_anims.py does for the rifle's. Its metadata is in Assets/Models/anims/clips.json.

    blender -b --factory-startup --python-exit-code 1 -P Tools/blender/build_climb_anims.py

It opens Assets/human.blend and never saves over it. To check it, play it in the game: BRUTALFPS_NPCANIM="test subject:Climb".

The clip stays in place: the game moves them up the ladder. A cycle (0.6 s, at speed 1) is two rungs of the game's ladders
(0.3 m apart) for each hand and foot, so at speed 1 it matches 1 m/s up the rungs. Opposite hand and foot go together:
each spends half the cycle on its rung, sliding down past the body as the body rises, and half reaching up for the next.
The figure faces -Y, its left is +X, up is +Z. The rungs are 0.2 m or so in front of them.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from humankit import animlib as AL  # noqa: E402
from humankit.anim import Poser  # noqa: E402
from humankit.combatlib import _arm_ik, _curl, _set_hand  # noqa: E402

PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
SOURCE = os.path.join(PROJECT, "Assets", "human.blend")
GLB = os.path.join(PROJECT, "Assets", "Models", "anims", "human_climb.glb")

FRAMES = 18          # 0.6 s
TRAVEL = 0.3         # m: how far a hand or foot goes on its rung, and back up again
HAND_LOW = 1.42      # m: where a hand leaves its rung
FOOT_LOW = 0.06      # m: where a foot leaves its rung
HAND_Y = -0.27
FOOT_Y = -0.15


def limb(ph):
    """Where a limb is at phase ph of the cycle: (height 0..1 on its travel, how far it's off the rungs 0..1)."""
    ph %= 1.0
    if ph < 0.5:
        t = AL.smooth(ph / 0.5)    # reaching up
        return t, math.sin(t * math.pi)
    return 1.0 - (ph - 0.5) / 0.5, 0.0   # on the rung: going down past the body at the speed it's rising


def climb(rig, snap):
    def pose(po, f, ph):
        w = 2 * math.pi * ph
        # Close in to the ladder, knees out a little, rocking across as the weight goes from side to side.
        po.move_world("pelvis", (0.018 * math.sin(w), -0.02, -0.10 + 0.012 * math.sin(2 * w)))
        po.rot_world("pelvis", (1, 0, 0), 6)
        po.rot_world("spine_01", (1, 0, 0), 3)
        po.rot_world("spine_02", (1, 0, 0), 3)
        po.rot_world("spine_02", (0, 1, 0), 3 * math.sin(w))
        po.rot_world("spine_03", (0, 1, 0), 2 * math.sin(w))
        po.rot_world("neck_01", (1, 0, 0), -8)
        po.rot_world("head", (1, 0, 0), -10)

        # The left hand and the right foot first; then the others.
        for side, sign, hand_ph, foot_ph in ((".L", 1.0, ph, ph + 0.5), (".R", -1.0, ph + 0.5, ph)):
            h, off = limb(foot_ph)
            at = FOOT_LOW + TRAVEL * h
            po.move_world("foot_ik" + side, (sign * 0.015, FOOT_Y + 0.07 * off, at))
            po.rot_world("foot_ik" + side, (1, 0, 0), -14 * (1 - off) + 22 * off)

            h, off = limb(hand_ph)
            wrist = Vector((sign * 0.17, HAND_Y + 0.07 * off, HAND_LOW + TRAVEL * h))
            _arm_ik(po, side, wrist, Vector((sign * 0.9, 0.35, -0.8)))
            _set_hand(po, side, Vector((-sign * 0.1, -0.75, 0.65)), Vector((0.0, 0.65, 0.75)))
            # The fingers open as a hand leaves its rung, and close on the next.
            c = 1.0 - 0.75 * off
            _curl(po, side, {"thumb": 40 * c, "index": 68 * c, "middle": 72 * c, "ring": 74 * c, "pinky": 76 * c})

    return AL._build(rig, "Climb", FRAMES, pose, AL.ALL_SET, step=1, loop=True, snap=snap)


def main():
    bpy.ops.wm.open_mainfile(filepath=SOURCE)
    rig = bpy.data.objects["Human_Rig"]
    body = bpy.data.objects["Human"]
    ad = rig.animation_data
    snap = AL.idle_snapshot(rig)
    for tr in list(ad.nla_tracks):
        ad.nla_tracks.remove(tr)
    ad.action = None
    for nm in ("Walk", "Idle"):
        if nm in bpy.data.actions:
            bpy.data.actions.remove(bpy.data.actions[nm])
    a = climb(rig, snap)
    tr = ad.nla_tracks.new()
    tr.name = a.name
    tr.strips.new(a.name, int(a.frame_range[0]), a)
    tr.mute = True
    ad.action = None
    Poser(rig).reset()
    AL._set_ik(rig, 1.0)
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.gltf(filepath=GLB, export_format='GLB', use_selection=True, export_def_bones=True,
                              export_skins=True, export_apply=False, export_yup=True,
                              export_animations=True, export_animation_mode='ACTIONS', export_force_sampling=True,
                              export_rest_position_armature=True, export_anim_slide_to_zero=True,
                              export_leaf_bone=False, export_optimize_animation_size=True)
    print(f"[climb] exported {GLB} ({os.path.getsize(GLB) / 1e6:.2f} MB)", flush=True)


main()
