"""
Builds the two-handed rifle clips (humankit/riflelib.py: AimRifle, FireRifle, ReloadRifle, PushDoor, DrawRifleBack) and
exports them to their own file, Assets/Models/anims/human_rifle.glb, as build_combat_anims.py does for the pistol's. They
key the upper body only. Their metadata is in Assets/Models/anims/clips.json.

    blender -b --factory-startup --python-exit-code 1 -P Tools/blender/build_rifle_anims.py

It opens Assets/human.blend and never saves over it. To check them, play them in the game:
BRUTALFPS_NPCANIM="test subject:AimRifle" or the prison intro.
"""
import json
import os
import struct
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from humankit import animlib as AL  # noqa: E402
from humankit import riflelib as RL  # noqa: E402
from humankit.anim import Poser  # noqa: E402

PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
SOURCE = os.path.join(PROJECT, "Assets", "human.blend")
GLB = os.path.join(PROJECT, "Assets", "Models", "anims", "human_rifle.glb")


def keep_upper_body(path, names, root_name="spine_02"):
    """Drops the channels of the clips outside the upper body: they're played masked, so they'd only be stale poses."""
    d = open(path, "rb").read()
    jl, jt = struct.unpack("<II", d[12:20])
    j = json.loads(d[20:20 + jl])
    nodes = j["nodes"]
    keep = set()
    stack = [next(i for i, n in enumerate(nodes) if n["name"] == root_name)]
    while stack:
        i = stack.pop()
        keep.add(i)
        stack.extend(nodes[i].get("children", []))
    for a in j["animations"]:
        if a["name"] in names:
            a["channels"] = [c for c in a["channels"] if c["target"]["node"] in keep]
    js = json.dumps(j, separators=(",", ":")).encode()
    js += b" " * ((4 - len(js) % 4) % 4)
    rest = d[20 + jl:]
    open(path, "wb").write(d[:8] + struct.pack("<I", 12 + 8 + len(js) + len(rest)) + struct.pack("<II", len(js), jt) + js + rest)


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
    names = list(RL.CLIPS)
    for n in names:
        a = RL.CLIPS[n](rig, snap)
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
    keep_upper_body(GLB, names)
    print(f"[rifle] exported {GLB} ({os.path.getsize(GLB) / 1e6:.2f} MB): {', '.join(names)}", flush=True)


main()
