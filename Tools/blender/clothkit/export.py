"""Garment objects in Blender and their export (CONTRACTS section 2)."""
import json
import os

import bpy
import numpy as np

import modelkit
from .carve import log
from .weights import mesh_from_soup, set_groups

PROJECT = modelkit.PROJECT
GARMENTS = os.path.join(PROJECT, "Assets", "Models", "garments")

# material -> (sRGB colour or None for white, roughness). *_tint materials are white: the game draws them with the instance colour.
MATERIALS = {
    "flat_sole": ((0.05, 0.05, 0.05), 0.9),
    "flat_sole_white": ((0.85, 0.85, 0.85), 0.7),
    "flat_reflective": ((0.75, 0.75, 0.75), 0.25),
    "flat_black": ((0.035, 0.035, 0.04), 0.6),          # waterproofs' black panels, a parka's yoke
    "flat_fur": ((0.62, 0.53, 0.40), 0.95),             # a parka hood's ruff
}


def get_material(name):
    color, rough = MATERIALS.get(name, (None, 0.8))
    return modelkit.mat(name, color, rough)


def make_object(soup, garment, body, rig, name=None):
    """A skinned garment object: parented to the rig with an armature modifier, vertex groups from soup.W, UVs, material
    slots, smooth shading (hard faces flat)."""
    name = name or garment.id
    me = mesh_from_soup(soup, name)
    uvl = me.uv_layers.new(name="UVMap")
    uvl.data.foreach_set("uv", soup.uv.reshape(-1, 2).reshape(-1))
    for m in garment.materials:
        me.materials.append(get_material(m))
    me.polygons.foreach_set("material_index", soup.mat.astype(np.int32))
    me.polygons.foreach_set("use_smooth", ~soup.flat)
    me.attributes.new("_REGION", 'FLOAT', 'POINT').data.foreach_set("value", soup.label.astype(np.float32))
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    set_groups(obj, soup, body.names)
    obj.parent = rig
    obj.matrix_parent_inverse.identity()
    mod = obj.modifiers.new("Armature", 'ARMATURE')
    mod.object = rig
    return obj


def export_glb(obj, rig, path):
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # the flags of build_human.export_glb, without the animations
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_def_bones=True,
                              export_skins=True, export_apply=False, export_yup=True,
                              export_animations=False, export_animation_mode='ACTIONS', export_force_sampling=True,
                              export_rest_position_armature=True, export_anim_slide_to_zero=True,
                              export_leaf_bone=False, export_optimize_animation_size=True,
                              export_attributes=True)
    return path


def write_sidecar(garment, triangles, path):
    with open(path, "w") as f:
        json.dump(garment.sidecar(triangles), f, indent=2)
        f.write("\n")


def export_garment(obj, rig, garment, triangles):
    glb = os.path.join(GARMENTS, garment.id + ".glb")
    export_glb(obj, rig, glb)
    write_sidecar(garment, triangles, os.path.join(GARMENTS, garment.id + ".json"))
    log(f"{garment.id}: exported {glb} ({os.path.getsize(glb) / 1e3:.0f} KB), {triangles} triangles")
    return glb
