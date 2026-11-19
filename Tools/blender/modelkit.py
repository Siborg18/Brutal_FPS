"""
Helpers for building BRUTAL FPS props in Blender from Python and exporting them as glTF binaries.

Conventions (see Engine/Model.cs in the game):
  * Blender +Z is the game's +Y (up); Blender +Y is the game's -Z (forward); X is X. Units are metres.
  * Material names choose the shading: a texture set name ("corrugated", "wood_planks", ...) uses that tileable PBR
    set through the UVs; "<set>_tint" takes the colour the game draws the model with; "flat_*" is untextured
    (base colour, roughness, metalness); "emit_*" glows.
  * UVs are in texture repeats: 1 UV unit = one tile of the material's texture set (TILE below), so texel density
    matches the boxes the level is built from.
"""
import math
import os
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(PROJECT, "Assets", "Models")
TEX = os.path.join(PROJECT, "Assets", "Textures")
PREVIEWS = os.path.join(PROJECT, "Tools", "previews", "models")

# Metres per texture repeat (must match Engine/TextureLibrary.cs).
TILE = {
    "concrete_wall": 2.5, "concrete_floor": 4.0, "rusty_metal": 1.5, "steel": 1.5, "wood_planks": 2.0,
    "painted_metal": 1.0, "corrugated": 2.24, "galvanized": 2.28, "gravel": 2.0, "red_dirt": 3.0,
    "dirt_road": 4.0, "snow": 3.0, "rock": 2.0, "marine_paint": 3.0, "foliage": 0.6, "gunmetal": 0.15, "polymer": 0.06, "skin": 0.08, "cloth": 0.05, "fleece": 0.05,
    "denim": 0.05, "canvas": 0.06, "leather": 0.08, "knit": 0.04, "hair": 0.03,
    # Models' own sets ("tex_<name>"): metres per repeat when box/cylinder-mapped.
    "grip_stipple": 0.03, "gun_wood": 0.3, "bark_gum": 1.2, "bark_kapok": 2.0, "bark_palm": 0.6, "tyre_tread": 1.0, "grille_honeycomb": 0.12,
    "hand": 1.0, "hand_index": 1.0, "wagon_body": 1.0, "transit_body": 1.0, "van_plates": 1.0, "saltbush_sprig": 1.0, "gum_branchlet": 1.0, "jungle_leaves": 1.0, "palm_frond": 1.0, "fern_frond": 1.0, "banana_leaf": 1.0, "big_leaf": 1.0,
}


def srgb_to_linear(c):
    return tuple((x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4) for x in c)


def set_of(material_name):
    name = material_name.split(".")[0]
    if name.endswith("_tint"):
        name = name[:-5]
    for prefix in ("tex_", "leaf_", "cut_"):
        if name.startswith(prefix):
            name = name[len(prefix):]
    return name if name in TILE else None


# --------------------------------------------------------------------------------------------- scene
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    random.seed(1)


def mat(name, color=None, rough=0.6, metal=0.0, emit=None):
    """Get or create a material. color is display-space (sRGB) like the game's tints."""
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    # No colour: plain white, which the game reads as "the texture set as baked".
    bsdf.inputs["Base Color"].default_value = (*srgb_to_linear(color or (1.0, 1.0, 1.0)), 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if emit is not None:
        bsdf.inputs["Emission Color"].default_value = (*srgb_to_linear(emit), 1.0)
        bsdf.inputs["Emission Strength"].default_value = 4.0
    # Preview only: wire up the texture set so preview renders show the real texture scale. (Not exported.)
    s = set_of(name)
    if s and os.path.exists(os.path.join(TEX, s + "_albedo.png")):
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(os.path.join(TEX, s + "_albedo.png"), check_existing=True)
        tex.image.alpha_mode = 'CHANNEL_PACKED'   # alpha is the paint mask, not transparency
        if color is not None or name.endswith("_tint"):
            mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
            mix.inputs["Factor"].default_value = 1.0
            nt.links.new(tex.outputs["Color"], mix.inputs[6])
            mix.inputs[7].default_value = (*srgb_to_linear(color or (0.6, 0.25, 0.15)), 1.0)
            nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
        else:
            nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        if name.startswith(("leaf_", "cut_")):
            # Alpha-tested cards: the alpha is coverage here.
            tex.image.alpha_mode = 'STRAIGHT'
            nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
        nrm = nt.nodes.new("ShaderNodeTexImage")
        nrm.image = bpy.data.images.load(os.path.join(TEX, s + "_normal.png"), check_existing=True)
        nrm.image.colorspace_settings.name = 'Non-Color'
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(nrm.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    return m


def _new_obj(name, bm, material):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    if material is not None:
        obj.data.materials.append(material if isinstance(material, bpy.types.Material) else mat(material))
    obj["uv"] = "box"
    return obj


def box(name, size, loc=(0, 0, 0), material=None, rot=(0, 0, 0), bevel=0.0):
    """An axis-aligned box of size (x, y, z) centred at loc, optionally rotated (degrees, XYZ)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    obj = _new_obj(name, bm, material)
    obj.rotation_euler = tuple(math.radians(a) for a in rot)
    obj.location = loc
    if bevel > 0:
        add_bevel(obj, bevel)
    return obj


def cyl(name, radius, depth, loc=(0, 0, 0), material=None, verts=32, rot=(0, 0, 0), radius2=None, caps=True, uv="cyl"):
    """A cylinder (or cone, radius2) along local Z, centred at loc."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=verts, radius1=radius,
                          radius2=radius if radius2 is None else radius2, depth=depth)
    obj = _new_obj(name, bm, material)
    obj.rotation_euler = tuple(math.radians(a) for a in rot)
    obj.location = loc
    obj["uv"] = uv
    obj["smooth"] = True
    return obj


def lathe(name, profile, loc=(0, 0, 0), material=None, verts=48, uv="cyl", rot=(0, 0, 0), flip=False):
    """Surface of revolution around local Z. profile: [(radius, z), ...] from bottom to top."""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        ring = []
        for i in range(verts):
            a = math.tau * i / verts
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), z)))
        rings.append(ring)
    for j in range(len(rings) - 1):
        for i in range(verts):
            a, b = rings[j][i], rings[j][(i + 1) % verts]
            c, d = rings[j + 1][(i + 1) % verts], rings[j + 1][i]
            if a.co.xy.length < 1e-6 and d.co.xy.length < 1e-6:
                continue
            try:
                bm.faces.new((a, b, c, d))
            except ValueError:
                pass
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if flip:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    obj = _new_obj(name, bm, material)
    obj.location = loc
    obj.rotation_euler = tuple(math.radians(a) for a in rot)
    obj["uv"] = uv
    obj["smooth"] = True
    return obj


def prism(name, outline, width, loc=(0, 0, 0), material=None, axis='X'):
    """Extrudes a 2D outline [(a, b), ...] (counter-clockwise) by width, centred. axis X: outline in (Y, Z)."""
    bm = bmesh.new()
    h = width / 2
    def P(a, b, w):
        return (w, a, b) if axis == 'X' else ((a, w, b) if axis == 'Y' else (a, b, w))
    front = [bm.verts.new(P(a, b, h)) for a, b in outline]
    back = [bm.verts.new(P(a, b, -h)) for a, b in outline]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    n = len(outline)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[i], back[i], back[j], front[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    obj = _new_obj(name, bm, material)
    obj.location = loc
    return obj


def tube(name, points, radius, material=None, verts=10):
    """A round tube along a polyline of points (for hoses, cables, pipes)."""
    curve = bpy.data.curves.new(name, 'CURVE')
    curve.dimensions = '3D'
    curve.bevel_depth = radius
    curve.bevel_resolution = max(1, verts // 4)
    curve.use_fill_caps = True
    spline = curve.splines.new('POLY' if len(points) < 3 else 'BEZIER')
    if spline.type == 'BEZIER':
        spline.bezier_points.add(len(points) - 1)
        for bp, p in zip(spline.bezier_points, points):
            bp.co = p
            bp.handle_left_type = bp.handle_right_type = 'AUTO'
    else:
        spline.points.add(len(points) - 1)
        for sp, p in zip(spline.points, points):
            sp.co = (*p, 1.0)
    curve.resolution_u = 8 if verts > 6 else 3
    cobj = bpy.data.objects.new(name, curve)
    bpy.context.scene.collection.objects.link(cobj)
    # Convert to a mesh.
    deps = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(cobj.evaluated_get(deps))
    bpy.data.objects.remove(cobj)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    if material is not None:
        obj.data.materials.append(material if isinstance(material, bpy.types.Material) else mat(material))
    obj["uv"] = "box"
    obj["smooth"] = True
    return obj


def add_bevel(obj, width, segments=2):
    mod = obj.modifiers.new("bevel", 'BEVEL')
    mod.width = width
    mod.segments = segments
    mod.limit_method = 'ANGLE'
    mod.angle_limit = math.radians(35)
    mod.harden_normals = False
    return obj


def displace(obj, strength, scale, seed=0):
    tex = bpy.data.textures.new(f"{obj.name}_noise", 'CLOUDS')
    tex.noise_scale = scale
    tex.noise_depth = 2
    mod = obj.modifiers.new("displace", 'DISPLACE')
    mod.texture = tex
    mod.strength = strength
    mod.texture_coords = 'OBJECT' if False else 'LOCAL'
    mod.mid_level = 0.5
    return obj


# --------------------------------------------------------------------------------------------- UVs
def _apply_modifiers_and_transform(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    for m in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)


def uv_project(obj):
    """UVs in texture repeats: box projection in world space (texture 'up' = Blender Z), or cylindrical."""
    mode = obj.get("uv", "box")
    if mode == "keep":       # authored UVs (models' own texture sets)
        return
    material = obj.data.materials[0] if obj.data.materials else None
    tile = TILE.get(set_of(material.name) if material else None, 1.0)
    mw = obj.matrix_world
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    uvl = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = (mw.to_3x3() @ f.normal).normalized()
        coords = [mw @ l.vert.co for l in f.loops]
        local = [l.vert.co for l in f.loops]
        if mode in ("cyl", "ring") and abs(f.normal.z) < 0.7:
            # Around the object's own Z axis: arc length and height.
            us = [math.atan2(p.y, p.x) for p in local]
            # Keep faces that straddle the seam continuous.
            base = us[0]
            us = [u + math.tau if u - base < -math.pi else (u - math.tau if u - base > math.pi else u) for u in us]
            rads = [max(Vector((p.x, p.y)).length, 1e-4) for p in local]
            r = sum(rads) / len(rads)
            for l, u, p in zip(f.loops, us, local):
                arc, h = u * r / tile, p.z / tile
                l[uvl].uv = (h, arc) if mode == "ring" else (arc, h)
            continue
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l, p in zip(f.loops, coords):
            if ax == 0:
                u, v = p.y, p.z
            elif ax == 1:
                u, v = p.x, p.z
            else:
                u, v = p.x, p.y
            l[uvl].uv = (u / tile, v / tile)
    bm.to_mesh(me)
    bm.free()


def finish(objs, join_name=None, smooth_angle=35.0):
    """Applies modifiers and transforms, UV-maps, shades, and optionally joins everything into one object."""
    for o in objs:
        _apply_modifiers_and_transform(o)
        uv_project(o)
        if o.get("smooth"):
            o.data.shade_smooth()
            o.data.set_sharp_from_angle(angle=math.radians(o.get("smooth_angle", smooth_angle)))
        else:
            o.data.shade_flat()
    if join_name:
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.join()
        objs[0].name = join_name
        objs[0].data.name = join_name
        return [objs[0]]
    return objs


def export(name, objs):
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(
        filepath=path, export_format='GLB', use_selection=True, export_apply=True, export_yup=True,
        export_tangents=True, export_normals=True, export_texcoords=True, export_materials='EXPORT',
        export_image_format='NONE', export_animations=False, export_skins=False, export_morph=False,
        export_cameras=False, export_lights=False)
    tris = sum(len(p.vertices) - 2 for o in objs for p in o.data.polygons)
    print(f"[model] {name}: {len(objs)} objects, ~{tris} triangles -> {path}", flush=True)
    return path


# --------------------------------------------------------------------------------------------- preview
def preview(name, objs, angle=(-40, 25), engine='CYCLES'):
    """Renders a 3/4 view with the texture sets applied, for checking UV scale and proportions."""
    scene = bpy.context.scene
    scene.render.engine = engine
    if engine == 'CYCLES':
        prefs = bpy.context.preferences.addons['cycles'].preferences
        try:
            prefs.compute_device_type = 'OPTIX'
            prefs.get_devices()
            for d in prefs.devices:
                d.use = d.type != 'CPU'
            scene.cycles.device = 'GPU'
        except Exception:
            pass
        scene.cycles.samples = 32
        scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 800, 600
    scene.render.image_settings.file_format = 'PNG'
    scene.view_settings.view_transform = 'AgX'
    world = bpy.data.worlds.new("prev") if not scene.world else scene.world
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.55, 0.6, 0.68, 1)
    bg.inputs[1].default_value = 0.8
    sun = bpy.data.lights.new("sun", 'SUN'); sun.energy = 3.5; sun.angle = math.radians(2)
    so = bpy.data.objects.new("sun", sun); scene.collection.objects.link(so)
    so.rotation_euler = (math.radians(50), math.radians(10), math.radians(-30))
    # Frame the bounds.
    lo = Vector((1e9, 1e9, 1e9)); hi = -lo
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
    center = (lo + hi) / 2
    radius = (hi - lo).length / 2
    cam_data = bpy.data.cameras.new("cam"); cam_data.lens = 50
    cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam)
    yaw, pitch = map(math.radians, angle)
    d = Vector((math.cos(pitch) * math.sin(yaw), -math.cos(pitch) * math.cos(yaw), math.sin(pitch)))
    cam.location = center + d * radius * 2.9
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cam_data.clip_end = radius * 20
    scene.camera = cam
    # A ground plane to catch shadows.
    bpy.ops.mesh.primitive_plane_add(size=radius * 12, location=(center.x, center.y, lo.z))
    ground = bpy.context.active_object
    ground.data.materials.append(mat("prev_ground", (0.45, 0.43, 0.4), 0.9))
    os.makedirs(PREVIEWS, exist_ok=True)
    scene.render.filepath = os.path.join(PREVIEWS, name + ".png")
    bpy.ops.render.render(write_still=True)
    for o in (so, cam, ground):
        bpy.data.objects.remove(o)
