"""
Builds the base human for NPCs and exports it.

    blender -b --factory-startup -P Tools/blender/build_human.py              (about 1-3 minutes)
    blender -b --factory-startup -P Tools/blender/build_human.py -- preview   (also renders previews)

The figure is an adult male, 1.78 m, in an A-pose (arms 45 degrees down, elbows and knees slightly bent), facing
-Y with its left side on +X, feet on the ground at the origin. It is modelled as signed-distance anatomy
(humankit/anatomy.py: a lofted torso and head with muscle masses, hands and feet from their joints), turned into a
quad game mesh at several densities (humankit/gamemesh.py), with opened eyes and separate eyeballs.

Outputs:
  * Assets/human.blend - the source: mesh "Human" (one object, materials flat_skin and flat_eye_*), armature
    "Human_Rig" (59 deform bones plus controls), actions Idle and Walk (as muted NLA tracks), camera and lights.
  * Assets/Models/human.glb - the skinned mesh with the deform skeleton only and both actions baked.
  * Tools/previews/models/human_*.png - with "-- preview".

    blender -b --factory-startup -P Tools/blender/build_human.py -- regions   (seconds)

opens Assets/human.blend, writes the _REGION attribute (humankit/regions.py) and re-exports human.glb only. The .blend is
not saved, so nothing else about the human changes.

The rig (humankit/rig.py): root / pelvis / spine_01-03 / neck_01 / head / eye.L,R; clavicle, upperarm (+twist),
forearm (+twist), hand, thumb/index/middle/ring/pinky_01-03; thigh, calf, foot, ball. Controls: hand_ik + arm_pole
(arms), foot_ik with foot_heel / foot_tip / foot_ball pivots and toe_ik, knee_pole (legs), eyes_target. IK/FK
switches are custom properties on the root bone: ik_arm.L/R (default 0 = FK), ik_leg.L/R (default 1 = IK),
eye_look.
"""
import math
import os
import sys
import time

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from humankit import anatomy, gamemesh, rig as R, skin as SK, uvs as UV, materials as MT, anim as AN, regions as RG  # noqa: E402

PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
BLEND = os.path.join(PROJECT, "Assets", "human.blend")
GLB = os.path.join(PROJECT, "Assets", "Models", "human.glb")
PREVIEWS = os.path.join(PROJECT, "Tools", "previews", "models")


def log(*a):
    print("[human]", *a, flush=True)


def scene_setup(body, rig):
    sc = bpy.context.scene
    col = bpy.data.collections.new("Human")
    sc.collection.children.link(col)
    for o in (body, rig):
        for c in list(o.users_collection):
            c.objects.unlink(o)
        col.objects.link(o)
    body.parent = rig
    body.matrix_parent_inverse.identity()
    # camera and a soft three-point light, so F12 gives a usable look straight away
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    sc.collection.objects.link(cam)
    cam.data.lens = 50
    cam.location = (1.45, -2.65, 1.12)
    from mathutils import Vector
    cam.rotation_euler = (Vector((0, 0, 0.90)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.camera = cam
    for name, loc, rot, energy, size in (("Key", (2.5, -2.5, 3.2), (50, 0, 45), 500, 2.0),
                                         ("Fill", (-3.0, -2.0, 1.6), (75, 0, -55), 150, 3.0),
                                         ("Rim", (0.5, 3.0, 2.8), (-55, 0, 175), 350, 1.5)):
        L = bpy.data.objects.new(name, bpy.data.lights.new(name, 'AREA'))
        L.data.energy, L.data.size = energy, size
        L.location = loc
        L.rotation_euler = tuple(math.radians(a) for a in rot)
        sc.collection.objects.link(L)
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.05, 0.055, 0.06, 1)
    sc.world = world
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 1080, 1350
    # viewport: material preview, bones in front
    for scr in bpy.data.screens:
        for area in scr.areas:
            if area.type == 'VIEW_3D':
                for sp in area.spaces:
                    if sp.type == 'VIEW_3D':
                        sp.shading.type = 'MATERIAL'
                        sp.clip_start = 0.01
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig


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
                              export_leaf_bone=False, export_optimize_animation_size=True,
                              export_attributes=True)       # the _REGION attribute (humankit/regions.py)
    log(f"exported {GLB} ({os.path.getsize(GLB) / 1e6:.2f} MB)")


def previews(body, rig):
    os.makedirs(PREVIEWS, exist_ok=True)
    sc = bpy.context.scene
    old = sc.camera
    cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
    sc.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    sc.camera = cam
    rig.hide_render = True
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    from mathutils import Vector

    def shot(name, target, yaw, pitch, scale, res):
        sc.render.resolution_x, sc.render.resolution_y = res
        y, p = math.radians(yaw), math.radians(pitch)
        d = Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p)))
        cam.location = Vector(target) + d * 4.0
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        cam.data.ortho_scale = scale
        sc.render.filepath = os.path.join(PREVIEWS, f"human_{name}.png")
        bpy.ops.render.render(write_still=True)

    for name, yaw in (("front", 0), ("side", 90), ("back", 180), ("three_quarter", 35)):
        shot(name, (0, 0, 0.9), yaw, 0, 1.95, (700, 1000))
    shot("face", (0, -0.01, 1.65), 25, 4, 0.32, (800, 800))
    shot("hand", (0.63, -0.10, 0.97), 15, 40, 0.30, (800, 800))
    rig.animation_data.action = bpy.data.actions["Walk"]
    sc.frame_set(9)
    shot("walk", (0, 0, 0.9), 70, 0, 1.95, (700, 1000))
    rig.animation_data.action = None
    AN.Poser(rig).reset()
    sc.frame_set(1)
    bpy.data.objects.remove(cam)
    sc.camera = old
    sc.render.engine = 'BLENDER_EEVEE'
    rig.hide_render = False
    log(f"previews in {PREVIEWS}")


def script_args():
    """The arguments after "--"; without one, everything after the script path."""
    if "--" in sys.argv:
        return sys.argv[sys.argv.index("--") + 1:]
    me = os.path.abspath(__file__)
    for i, a in enumerate(sys.argv):
        if os.path.abspath(a) == me:
            return sys.argv[i + 1:]
    return []


def regions_only():
    """Adds _REGION to the existing human.blend's body and re-exports human.glb. Does not save the .blend."""
    t0 = time.time()
    bpy.ops.wm.open_mainfile(filepath=BLEND)
    body, rig = bpy.data.objects["Human"], bpy.data.objects["Human_Rig"]
    RG.assign(body, rig)
    export_glb(body, rig)
    log(f"regions done in {time.time() - t0:.0f}s (human.blend not saved)")


def main():
    argv = script_args()
    if "regions" in argv:
        regions_only()
        return
    t0 = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    body = anatomy.build()
    mesh = gamemesh.build_game_mesh(body)
    mesh.data.shade_smooth()
    eyes = [bpy.data.objects["Eye.L"], bpy.data.objects["Eye.R"]]
    MT.assign(mesh, eyes)
    rig = R.build_armature(body.joints)
    SK.skin(mesh, rig, eyes)
    UV.unwrap(mesh, body, body.joints)
    AN.make_actions(rig)
    RG.assign(mesh, rig)
    scene_setup(mesh, rig)
    if "preview" in argv:
        try:
            previews(mesh, rig)
        except Exception as e:  # previews need a GPU context; the build doesn't
            log("previews failed:", e)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
    log(f"saved {BLEND}")
    export_glb(mesh, rig)
    log(f"done in {time.time() - t0:.0f}s")


main()
