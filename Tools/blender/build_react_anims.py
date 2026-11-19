"""
Builds the reaction and extra idle clips (humankit/reactlib.py: HitFront, HitBack, HitLeg, Startle, CheckWatch, Stretch,
ReloadPistol) and exports them. Derived from build_combat_anims.py; the clips go in their own file so human_anims.glb
and human_combat.glb are untouched.

    blender -b --factory-startup -P Tools/blender/build_react_anims.py                        (all 7 clips)
    blender -b --factory-startup -P Tools/blender/build_react_anims.py -- HitFront Startle     (only these)
    blender -b --factory-startup -P Tools/blender/build_react_anims.py -- preview              (also render contact sheets)

It opens Assets/human.blend (never saves over it), reads the pose of Idle frame 1 from the existing Idle action, drops
the Walk and Idle actions from the session, authors the clips on Human_Rig and writes:
  * Assets/human_react.blend  - the source: the clips as muted NLA tracks.
  * Assets/Models/anims/human_react.glb - the rig (with the Human mesh, which the game ignores) and only these clips,
    baked at 30 fps.
  * Tools/previews/models/anim_<Clip>.png - 6 evenly spaced frames per clip, three-quarter view (with "preview").
Their metadata (loop, tags) is in Assets/Models/anims/clips.json, edited by hand like the others.
"""
import json
import os
import struct
import sys
import time

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from humankit import animlib as AL  # noqa: E402
from humankit import reactlib as RL  # noqa: E402
from humankit.anim import Poser  # noqa: E402

PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
SOURCE = os.path.join(PROJECT, "Assets", "human.blend")
BLEND = os.path.join(PROJECT, "Assets", "human_react.blend")
GLB = os.path.join(PROJECT, "Assets", "Models", "anims", "human_react.glb")
PREVIEWS = os.path.join(PROJECT, "Tools", "previews", "models")
ALL = list(RL.CLIPS)


def log(*a):
    print("[react]", *a, flush=True)


def export_glb(body, rig):
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = rig
    os.makedirs(os.path.dirname(GLB), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=GLB, export_format='GLB', use_selection=True, export_def_bones=True,
                              export_skins=True, export_apply=False, export_yup=True,
                              export_animations=True, export_animation_mode='ACTIONS', export_force_sampling=True,
                              export_rest_position_armature=True, export_anim_slide_to_zero=True,
                              export_leaf_bone=False, export_optimize_animation_size=True)
    log(f"exported {GLB} ({os.path.getsize(GLB) / 1e6:.2f} MB)")


def keep_subtree_only(path, anim, root_name):
    """Drop the channels of animation `anim` whose node is not `root_name` or below it (as build_combat_anims.py)."""
    d = open(path, "rb").read()
    jl, jt = struct.unpack("<II", d[12:20])
    j = json.loads(d[20:20 + jl])
    nodes = j["nodes"]
    keep = set()
    stack = [[i for i, n in enumerate(nodes) if n["name"] == root_name][0]]
    while stack:
        i = stack.pop()
        keep.add(i)
        stack.extend(nodes[i].get("children", []))
    for a in j["animations"]:
        if a["name"] == anim:
            before = len(a["channels"])
            a["channels"] = [c for c in a["channels"] if c["target"]["node"] in keep]
            log(f"  {anim}: kept {len(a['channels'])} of {before} channels (subtree of {root_name})")
    js = json.dumps(j, separators=(",", ":")).encode()
    js += b" " * ((4 - len(js) % 4) % 4)
    rest = d[20 + jl:]
    out = d[:8] + struct.pack("<I", 12 + 8 + len(js) + len(rest)) + struct.pack("<II", len(js), jt) + js + rest
    open(path, "wb").write(out)


def previews(rig, body, names, snap):
    """Contact sheet per clip: 6 evenly spaced frames, three-quarter view, Workbench (quick)."""
    import numpy as np
    from mathutils import Vector
    os.makedirs(PREVIEWS, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'SINGLE'
    sc.render.resolution_x, sc.render.resolution_y = 360, 420
    rig.hide_render = True
    cam = bpy.data.objects["Camera"]
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 2.2
    d = Vector((-1.0, -1.2, 0.15)).normalized()
    target = Vector((0, 0, 0.95))
    cam.location = target + d * 6.0
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    tmp = os.path.join(PREVIEWS, "_tmp_frame.png")
    for n in names:
        act = bpy.data.actions[n]
        po = Poser(rig)
        po.reset()
        if n in RL.OVERLAYS:
            AL.apply_snapshot(po, snap)       # only the upper body is keyed: stand the rest in the Idle pose
        AL._set_ik(rig, 1.0)
        rig.animation_data.action = act
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        frames = [round(f0 + (f1 - f0) * i / 5.0) for i in range(6)]
        tiles = []
        for f in frames:
            sc.frame_set(f)
            sc.render.filepath = tmp
            bpy.ops.render.render(write_still=True)
            img = bpy.data.images.load(tmp, check_existing=False)
            px = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
            img.pixels.foreach_get(px)
            tiles.append(px.reshape(img.size[1], img.size[0], 4))
            bpy.data.images.remove(img)
        # (Blender images are stored bottom row first, so the first row of tiles goes on top by coming last)
        sheet = np.concatenate([np.concatenate(tiles[3:6], axis=1), np.concatenate(tiles[0:3], axis=1)], axis=0)
        out = os.path.join(PREVIEWS, f"anim_{n}.png")
        im = bpy.data.images.new("sheet", sheet.shape[1], sheet.shape[0], alpha=True)
        im.pixels.foreach_set(sheet.ravel())
        im.filepath_raw = out
        im.file_format = "PNG"
        im.save()
        bpy.data.images.remove(im)
        log(f"preview {out} (frames {frames})")
    if os.path.exists(tmp):
        os.remove(tmp)
    rig.animation_data.action = None
    Poser(rig).reset()


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    want_preview = "preview" in argv
    names = [a for a in argv if a in RL.CLIPS] or ALL
    unknown = [a for a in argv if a not in RL.CLIPS and a != "preview"]
    if unknown:
        log("unknown clips ignored (not in reactlib.CLIPS):", unknown)
    t0 = time.time()
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
    acts = [RL.CLIPS[n](rig, snap) for n in names]
    for a in acts:                                    # muted NLA tracks keep them reachable for the exporter
        tr = ad.nla_tracks.new()
        tr.name = a.name
        tr.strips.new(a.name, int(a.frame_range[0]), a)
        tr.mute = True
    ad.action = None
    Poser(rig).reset()
    AL._set_ik(rig, 1.0)
    sc = bpy.context.scene
    sc.frame_start, sc.frame_end = 1, max(int(a.frame_range[1]) for a in acts)
    sc.render.fps = 30
    sc.frame_set(1)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
    log(f"saved {BLEND}")
    export_glb(body, rig)
    for n in names:
        if n in RL.OVERLAYS:
            keep_subtree_only(GLB, n, "spine_02")
    if want_preview:
        previews(rig, body, names, snap)
    log(f"done in {time.time() - t0:.0f}s")


main()
