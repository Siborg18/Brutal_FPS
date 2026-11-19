"""
Builds the extra animation clips for the human and exports them.

    blender -b --factory-startup -P Tools/blender/build_anims.py                       (all 8 clips)
    blender -b --factory-startup -P Tools/blender/build_anims.py -- Run Cower           (only these)
    blender -b --factory-startup -P Tools/blender/build_anims.py -- preview             (also render contact sheets)
    python3 Tools/blender/build_anims.py                                                (headless bpy module, same thing)

It opens Assets/human.blend (never saves over it), reads the pose of Idle frame 1 from the existing Idle action, drops
the Walk and Idle actions from the session (they stay in human.glb and are not exported again), authors the clips of
humankit/animlib.py on Human_Rig and writes:
  * Assets/human_anims.blend  - the source: the clips as muted NLA tracks, like Walk and Idle in human.blend.
  * Assets/Models/anims/human_anims.glb - the rig (with the Human mesh, which the game ignores) and only these clips,
    baked at 30 fps with linear interpolation.
  * Tools/previews/models/anim_<Clip>.png - 6 evenly spaced frames per clip, side view (with "preview").
"""
import os
import sys
import time

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from humankit import animlib as AL  # noqa: E402
from humankit.anim import Poser  # noqa: E402

PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
SOURCE = os.path.join(PROJECT, "Assets", "human.blend")
BLEND = os.path.join(PROJECT, "Assets", "human_anims.blend")
GLB = os.path.join(PROJECT, "Assets", "Models", "anims", "human_anims.glb")
PREVIEWS = os.path.join(PROJECT, "Tools", "previews", "models")
ALL = ["Run", "IdleLook", "IdleShift", "ShuffleTurn", "Cower", "DeathFwd", "DeathBack", "Talk"]


def log(*a):
    print("[anims]", *a, flush=True)


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
    """Drop the channels of animation `anim` whose node is not `root_name` or one of its descendants. A clip that keys
    only the upper body (Talk) would otherwise export the lower body's stale pose from the clip evaluated before it;
    the game masks those joints anyway, so they are left out of the file."""
    import json
    import struct
    d = open(path, "rb").read()
    jl, jt = struct.unpack("<II", d[12:20])
    j = json.loads(d[20:20 + jl])
    nodes = j["nodes"]
    keep = set()
    start = [i for i, n in enumerate(nodes) if n["name"] == root_name][0]
    stack = [start]
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
    """Contact sheet per clip: 6 evenly spaced frames, side view, with the body mesh, on a grey floor."""
    import numpy as np
    from PIL import Image
    from mathutils import Vector
    os.makedirs(PREVIEWS, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = 16
    sc.cycles.use_denoising = False
    sc.render.resolution_x, sc.render.resolution_y = 480, 400
    sc.render.film_transparent = False
    rig.hide_render = True
    cam = bpy.data.objects["Camera"]
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 2.5
    d = Vector((1, 0, 0))
    target = Vector((0, 0, 0.82))
    cam.location = target + d * 6.0
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    floor = bpy.data.objects.new("Floor", bpy.data.meshes.new("Floor"))
    floor.data.from_pydata([(x, y, z) for z in (-0.06, 0.0) for y in (-3, 3) for x in (-3, 3)], [],
                           [(0, 1, 3, 2), (4, 5, 7, 6), (0, 1, 5, 4), (2, 3, 7, 6), (0, 2, 6, 4), (1, 3, 7, 5)])
    sc.collection.objects.link(floor)
    mat = bpy.data.materials.new("floor")
    mat.diffuse_color = (0.35, 0.37, 0.4, 1)
    floor.data.materials.append(mat)
    world = sc.world
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.6, 0.66, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0
    tmp = os.path.join(PREVIEWS, "_tmp_frame.png")
    for n in names:
        act = bpy.data.actions[n]
        po = Poser(rig)
        po.reset()
        if n == "Talk":                       # only the upper body is keyed: stand the rest in the Idle pose
            AL.apply_snapshot(po, snap)
        AL._set_ik(rig, 1.0)
        rig.animation_data.action = act
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        frames = [round(f0 + (f1 - f0) * i / 5.0) for i in range(6)]
        tiles = []
        for f in frames:
            sc.frame_set(f)
            sc.render.filepath = tmp
            bpy.ops.render.render(write_still=True)
            im = Image.open(tmp).convert("RGB")
            tiles.append(np.asarray(im).copy())
        rows = [np.concatenate(tiles[0:3], axis=1), np.concatenate(tiles[3:6], axis=1)]
        sheet = np.concatenate(rows, axis=0)
        out = os.path.join(PREVIEWS, f"anim_{n}.png")
        Image.fromarray(sheet).save(out)
        log(f"preview {out} (frames {frames})")
    if os.path.exists(tmp):
        os.remove(tmp)
    rig.animation_data.action = None
    Poser(rig).reset()


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    want_preview = "preview" in argv
    names = [a for a in argv if a in AL.CLIPS] or [n for n in ALL if n in AL.CLIPS]
    unknown = [a for a in argv if a not in AL.CLIPS and a != "preview"]
    if unknown:
        log("unknown clips ignored (not in animlib.CLIPS):", unknown)
    t0 = time.time()
    bpy.ops.wm.open_mainfile(filepath=SOURCE)
    rig = bpy.data.objects["Human_Rig"]
    body = bpy.data.objects["Human"]
    ad = rig.animation_data
    snap = AL.idle_snapshot(rig)                      # the pose of Idle frame 1, read from the existing action
    # Walk and Idle stay in human.glb: take them out of this session so they are not exported again
    for tr in list(ad.nla_tracks):
        ad.nla_tracks.remove(tr)
    ad.action = None
    for nm in ("Walk", "Idle"):
        if nm in bpy.data.actions:
            bpy.data.actions.remove(bpy.data.actions[nm])
    acts = []
    for n in names:
        acts.append(AL.CLIPS[n](rig, snap))
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
    if "Talk" in names:
        keep_subtree_only(GLB, "Talk", "spine_02")
    if want_preview:
        previews(rig, body, names, snap)
    log(f"done in {time.time() - t0:.0f}s")


main()
