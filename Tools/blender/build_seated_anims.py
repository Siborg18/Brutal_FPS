"""
Builds the clip for someone sat playing cards at the yard's garden table (humankit/seatedlib.py: SitCards) and exports it
to its own file, Assets/Models/anims/human_seated.glb. Its metadata is in Assets/Models/anims/clips.json.

    blender -b --factory-startup --python-exit-code 1 -P Tools/blender/build_seated_anims.py [-- preview]

It opens Assets/human.blend and never saves over it. With "preview" it also renders the figure in the garden chair at the
table (Assets/Models/garden_*.glb, from build_garden_furniture.py) to Tools/previews/seated_*.png, holding the cards and
laying one down.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from humankit import animlib as AL  # noqa: E402
from humankit import seatedlib as SL  # noqa: E402
from humankit.anim import Poser  # noqa: E402

PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
SOURCE = os.path.join(PROJECT, "Assets", "human.blend")
GLB = os.path.join(PROJECT, "Assets", "Models", "anims", "human_seated.glb")
PREVIEWS = os.path.join(PROJECT, "Tools", "previews")


def preview(rig, frames):
    scene = bpy.context.scene
    import build_garden_furniture as G   # (built in this scene, as the game has them: not imported, which turns them)
    import modelkit as K
    for build, at in ((G.garden_chair, (0, 0, 0)), (G.garden_table, (0, SL.TABLE_AT, 0))):
        for o in K.finish(build(), join_name=build.__name__):
            o.location += Vector(at)
    scene.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'CYCLES'
    scene.render.resolution_x, scene.render.resolution_y = 700, 560
    scene.render.image_settings.file_format = 'PNG'
    world = scene.world or bpy.data.worlds.new("w")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.6, 0.68, 1)
    sun = bpy.data.lights.new("sun", 'SUN'); sun.energy = 3.5
    so = bpy.data.objects.new("sun", sun); scene.collection.objects.link(so)
    so.rotation_euler = (math.radians(50), math.radians(10), math.radians(-30))
    bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, 0))
    cam_data = bpy.data.cameras.new("cam"); cam_data.lens = 40
    cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam)
    scene.camera = cam
    ad = rig.animation_data
    ad.action = bpy.data.actions["SitCards"]
    for tr in ad.nla_tracks:
        tr.mute = True
    for f in frames:
        scene.frame_set(f + 1)
        for tag, (yaw, pitch, dist) in (("side", (80, 12, 2.6)), ("front", (200, 25, 2.6))):
            yw, pt = math.radians(yaw), math.radians(pitch)
            d = Vector((math.cos(pt) * math.sin(yw), -math.cos(pt) * math.cos(yw), math.sin(pt)))
            c = Vector((0, -0.3, 0.7))
            cam.location = c + d * dist
            cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
            scene.render.filepath = os.path.join(PREVIEWS, f"seated_{f}_{tag}.png")
            bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
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
    names = list(SL.CLIPS)
    for n in names:
        a = SL.CLIPS[n](rig, snap)
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
    print(f"[seated] exported {GLB} ({os.path.getsize(GLB) / 1e6:.2f} MB): {', '.join(names)}", flush=True)
    if "preview" in argv:
        preview(rig, [0, 180])


main()
