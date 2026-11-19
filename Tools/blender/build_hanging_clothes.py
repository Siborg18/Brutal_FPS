"""
Builds the arctic kit hanging on hooks, for the storage room by Rio's portal (Game/LevelRioStore.cs): static models of the
garments (clothkit/garments_more.py) as if taken off and hung up.

    blender -b --factory-startup --python-exit-code 1 -P Tools/blender/build_hanging_clothes.py

Each garment is built on the body (then decimated: there are a dozen of them in the room), posed with the arms and legs hanging (Idle, frame 1), baked to a plain mesh without the
body, squashed flat against the wall behind it (front to back, about its back; the hood most of all), given a cloth inside (solidified 4 mm) and moved so the hook is the origin: the top
of the hood for the parka (hung up by it), the middle of the back of the waistband for the trousers. The wall is the
model's z = 0 plane in the game (Blender y = 0) and the clothes stand out from it towards +z (Blender -y).

    Assets/Models/hanging_parka.glb           the red parka with its hood
    Assets/Models/hanging_snow_trousers.glb   the red insulated trousers
Their cloth is "cloth_tint" (the game's cloth texture, in the colour it draws them with); the yoke, tape and fur keep theirs.
Previews: Tools/previews/models/hanging_parka.png, hanging_snow_trousers.png.
"""
import os
import sys

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
BLEND = os.path.join(PROJECT, "Assets", "human.blend")

FLATTEN = {"parka": 0.5, "snow_trousers": 0.55}   # how much of their depth is left, hung flat against the wall
DECIMATE = {"hanging_parka": 0.2, "hanging_snow_trousers": 0.4}   # a room of them: about 12k and 5k triangles each, inside and out
HOOD_FLATTEN = 0.3   # of what's left of the hood's depth


def baked(built, rig, name):
    """The garment as it is posed now, as a plain mesh object (no rig, no modifiers)."""
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(built.obj.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    me.name = name
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def coords(obj):
    co = np.empty(len(obj.data.vertices) * 3)
    obj.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def put(obj, co):
    obj.data.vertices.foreach_set("co", co.reshape(-1))
    obj.data.update()


def hang(objs, keep, hook_from):
    """Squashes them front to back about their back (keeping `keep` of their depth) and moves the hook to the origin, the back to y = 0."""
    all_co = np.concatenate([coords(o) for o in objs])
    back = all_co[:, 1].max()
    hook = hook_from(all_co, objs)
    for o in objs:
        co = coords(o)
        co[:, 1] = back + (co[:, 1] - back) * keep
        co -= np.array([hook[0], back, hook[2]])
        put(o, co)


def main():
    bpy.ops.wm.open_mainfile(filepath=BLEND)
    import modelkit
    from clothkit import build, catalog
    from clothkit.body import BodyData
    from clothkit.check import set_pose

    rig, human = bpy.data.objects["Human_Rig"], bpy.data.objects["Human"]
    body = BodyData(human, rig)
    built = {i: build.build(body, catalog.REGISTRY[i]) for i in ("parka", "parka_hood", "snow_trousers")}
    set_pose(rig, "Idle", 1)
    bpy.context.view_layer.update()

    parka = baked(built["parka"], rig, "hanging_parka")
    hood = baked(built["parka_hood"], rig, "hanging_parka_hood")
    trousers = baked(built["snow_trousers"], rig, "hanging_snow_trousers")
    for b in built.values():
        b.obj.hide_render = True
    human.hide_render = True
    rig.hide_render = True

    # The parka hangs by the top of its hood; the trousers by the back of the waistband.
    hang([parka, hood], FLATTEN["parka"], lambda co, objs: (0.0, 0.0, coords(objs[1])[:, 2].max()))
    hang([trousers], FLATTEN["snow_trousers"], lambda co, objs: (0.0, 0.0, co[:, 2].max()))
    # A hood hung up collapses: squashed most of the way flat against the wall, so its opening is a slit, not a face.
    co = coords(hood)
    co[:, 1] *= HOOD_FLATTEN
    put(hood, co)

    bpy.ops.object.select_all(action='DESELECT')
    parka.select_set(True)
    hood.select_set(True)
    bpy.context.view_layer.objects.active = parka
    bpy.ops.object.join()
    parka.name = parka.data.name = "hanging_parka"

    for obj in (parka, trousers):
        for poly in obj.data.polygons:
            poly.use_smooth = True
        bpy.ops.object.select_all(action='DESELECT')
        # The game draws one side of a face: a cloth inside (4 mm in) so collars, hems and the hood aren't see-through.
        solid = obj.modifiers.new("Solidify", 'SOLIDIFY')
        solid.thickness = 0.004
        solid.offset = -1.0
        solid.use_rim = True
        dec = obj.modifiers.new("Decimate", 'DECIMATE')
        dec.ratio = DECIMATE[obj.name]
        dec.use_collapse_triangulate = True
        modelkit.export(obj.name, [obj])

    # Previews, the cloth shown red.
    for m in bpy.data.materials:
        if m.name.startswith("cloth_tint") and m.use_nodes:
            m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.45, 0.01, 0.01, 1)
    for obj in (parka, trousers):
        for o in bpy.context.scene.objects:
            o.hide_render = o is not obj and o.type == 'MESH'
        modelkit.preview(obj.name, [obj], angle=(-25, 15))


if __name__ == "__main__":
    main()
