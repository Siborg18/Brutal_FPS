"""Cycles CPU previews of garments on the body (the body drawn the way the game draws it: triangles whose three vertices are in
hidden regions are left out, so a gap in the cloth shows as a hole). 600x900 per view, plus a contact sheet."""
import math
import os

import bmesh
import bpy
import numpy as np
from mathutils import Vector

import modelkit
from humankit import anim as AN, regions as RG
from humankit import materials as MT
from .carve import log

PREVIEWS = modelkit.PREVIEWS
VIEWS = [("front", 0, "rest"), ("back", 180, "rest"), ("three_quarter", 35, "rest"),
         ("walk1", 60, ("Walk", 1)), ("walk9", 60, ("Walk", 9)), ("walk17", 60, ("Walk", 17))]
W, H = 600, 900
SAMPLES = 32
ORTHO, CENTRE_Z = 2.0, 0.9          # camera framing (the whole figure); head garments use a close-up


def _scene():
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = SAMPLES
    sc.cycles.use_denoising = False
    sc.render.resolution_x, sc.render.resolution_y = W, H
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    sc.view_settings.view_transform = 'Standard'
    world = bpy.data.worlds.new("prev_world")
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.62, 0.66, 0.72, 1)
    bg.inputs[1].default_value = 0.9
    sc.world = world
    objs = []
    sun = bpy.data.objects.new("prev_sun", bpy.data.lights.new("prev_sun", 'SUN'))
    sun.data.energy, sun.data.angle = 3.0, math.radians(8)
    sun.rotation_euler = (math.radians(55), math.radians(5), math.radians(-35))
    fill = bpy.data.objects.new("prev_fill", bpy.data.lights.new("prev_fill", 'SUN'))
    fill.data.energy, fill.data.angle = 1.2, math.radians(20)
    fill.rotation_euler = (math.radians(70), 0, math.radians(150))
    bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, -0.001))
    ground = bpy.context.active_object
    ground.name = "prev_ground"
    ground.data.materials.append(MT.material("prev_ground", (0.5, 0.48, 0.45), 0.9))
    cam = bpy.data.objects.new("prev_cam", bpy.data.cameras.new("prev_cam"))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = ORTHO
    for o in (sun, fill, cam):
        sc.collection.objects.link(o)
    sc.camera = cam
    return sc, cam, [sun, fill, cam, ground]


def _cull_copy(body, hidden_regions):
    """A copy of the body object with the triangles whose three vertices are in hidden regions removed (the engine's rule)."""
    lab = RG.labels(body)
    hid = np.isin(lab, [RG.INDEX[r] for r in hidden_regions])
    me = body.data.copy()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.verts.ensure_lookup_table()
    kill = [f for f in bm.faces if all(hid[v.index] for v in f.verts)]
    bmesh.ops.delete(bm, geom=kill, context='FACES_ONLY')
    bm.to_mesh(me)
    bm.free()
    ob = body.copy()
    ob.data = me
    ob.name = "prev_body"
    bpy.context.scene.collection.objects.link(ob)
    return ob


def _pose(rig, pose):
    ad = rig.animation_data_create() if not rig.animation_data else rig.animation_data
    if pose == "rest":
        ad.action = None
        AN.Poser(rig).reset()
        bpy.context.scene.frame_set(1)
        return
    name, frame = pose
    act = bpy.data.actions[name]
    ad.action = act
    if getattr(ad, "action_slot", None) is None and len(act.slots):
        ad.action_slot = act.slots[0]
    bpy.context.scene.frame_set(frame)


def _preview_materials(objs_garments):
    """Swaps each garment's tint materials for a coloured preview version (the exported file keeps the white ones)."""
    for obj, g in objs_garments:
        for i, m in enumerate(obj.data.materials):
            col = g.preview_colors.get(m.name)
            if col is not None:
                obj.data.materials[i] = MT.material("prev_" + m.name, col, 0.85)


def render_views(body_data, built, label, outdir=None, views=VIEWS, hide=None):
    """Renders the garments in `built` (list of Built) on the body. Returns the file paths."""
    outdir = outdir or PREVIEWS
    os.makedirs(outdir, exist_ok=True)
    body, rig = body_data.obj, body_data.rig
    sc, cam, scene_objs = _scene()
    hide = hide if hide is not None else sorted({r for b in built for r in (b.garment.hide_for_preview or b.garment.hides)})
    pbody = _cull_copy(body, hide)
    skin = MT.material("prev_skin", (0.78, 0.57, 0.46), 0.55)
    pbody.data.materials[0] = skin
    body.hide_render = True
    for b in built:
        b.obj.hide_render = False
    _preview_materials([(b.obj, b.garment) for b in built])
    paths = []
    centre = Vector((0, 0, CENTRE_Z))
    for name, yaw, pose in views:
        _pose(rig, pose)
        y = math.radians(yaw)
        d = Vector((math.sin(y), -math.cos(y), 0.0))
        cam.location = centre + d * 4.0
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(outdir, f"garment_{label}_{name}.png")
        bpy.ops.render.render(write_still=True)
        paths.append(sc.render.filepath)
    _pose(rig, "rest")
    body.hide_render = False
    for o in scene_objs + [pbody]:
        bpy.data.objects.remove(o)
    return paths


def contact_sheet(paths, out, cols=3):
    imgs = []
    for p in paths:
        im = bpy.data.images.load(p)
        a = np.zeros(len(im.pixels), np.float32)
        im.pixels.foreach_get(a)
        imgs.append(a.reshape(im.size[1], im.size[0], 4))
        bpy.data.images.remove(im)
    h, w = imgs[0].shape[:2]
    rows = (len(imgs) + cols - 1) // cols
    sheet = np.ones((rows * h, cols * w, 4), np.float32)
    for i, a in enumerate(imgs):
        r, c = divmod(i, cols)
        sheet[(rows - 1 - r) * h:(rows - r) * h, c * w:(c + 1) * w] = a       # image rows run bottom to top
    im = bpy.data.images.new("sheet", cols * w, rows * h)
    im.pixels.foreach_set(sheet.reshape(-1))
    im.filepath_raw = out
    im.file_format = 'PNG'
    im.save()
    bpy.data.images.remove(im)
    log(f"contact sheet {out}")


def _cull_garment(obj, soup, hidden_regions, name):
    """A copy of a garment object without the triangles whose three vertices are in hidden regions (the engine's layered drawing)."""
    hid = np.zeros(16, bool)
    for r in hidden_regions:
        hid[RG.INDEX[r]] = True
    kill_idx = np.where(hid[soup.label][soup.tris].all(axis=1))[0]
    ob = obj.copy()
    ob.data = obj.data.copy()
    ob.name = name
    ob.hide_render = False
    bpy.context.scene.collection.objects.link(ob)
    if len(kill_idx):
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.faces[int(i)] for i in kill_idx], context='FACES_ONLY')
        bm.to_mesh(ob.data)
        bm.free()
    return ob


def render_outfit(body_data, items, label, outdir=None, views=VIEWS):
    """Renders several garments on the body the way the engine draws them: each garment without the triangles that a garment of a
    strictly higher layer hides, the body without the union of all hides. items: [(Built, {material: sRGB colour})]."""
    outdir = outdir or PREVIEWS
    os.makedirs(outdir, exist_ok=True)
    body, rig = body_data.obj, body_data.rig
    sc, cam, scene_objs = _scene()
    hide_all = sorted({r for b, _ in items for r in b.garment.hides})
    pbody = _cull_copy(body, hide_all)
    pbody.data.materials[0] = MT.material("prev_skin", (0.78, 0.57, 0.46), 0.55)
    body.hide_render = True
    copies = []
    for b, colors in items:
        g = b.garment
        higher = sorted({r for o, _ in items if o.garment.layer > g.layer for r in o.garment.hides})
        ob = _cull_garment(b.obj, b.soup, higher, "prev_" + g.id)
        for i, m in enumerate(ob.data.materials):
            col = colors.get(m.name)
            if col is not None:
                ob.data.materials[i] = MT.material(f"prev_{g.id}_{m.name}", col, 0.85)
        b.obj.hide_render = True
        copies.append(ob)
    paths = []
    centre = Vector((0, 0, CENTRE_Z))
    for name, yaw, pose in views:
        _pose(rig, pose)
        y = math.radians(yaw)
        d = Vector((math.sin(y), -math.cos(y), 0.0))
        cam.location = centre + d * 4.0
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(outdir, f"garment_{label}_{name}.png")
        bpy.ops.render.render(write_still=True)
        paths.append(sc.render.filepath)
    _pose(rig, "rest")
    body.hide_render = False
    for b, _ in items:
        b.obj.hide_render = False
    for o in scene_objs + [pbody] + copies:
        bpy.data.objects.remove(o)
    return paths
