"""
Renders foliage texture atlases for leaf cards: a real sprig of leaves is modelled in Blender and rendered straight
down with an orthographic camera into albedo + alpha, a tangent-space normal map and occlusion/roughness.

    blender -b --factory-startup -P Tools/blender/bake_leaves.py -- [saltbush_sprig] [gum_branchlet]

Writes Assets/Textures/<name>_albedo.png (alpha = leaf coverage), _normal.png, _orm.png. In the game these are used
by "leaf_<name>" materials: alpha-tested, two-sided cards (see Engine/Renderer.cs). The card's UV square matches the
camera's view: the sprig's base is at the bottom centre of the image.
"""
import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(PROJECT, "Assets", "Textures")
PREVIEWS = os.path.join(PROJECT, "Tools", "previews")


def srgb_to_linear(c):
    return tuple((x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4) for x in c)


# --------------------------------------------------------------------------------------------- geometry
def leaf_mesh(name, outline, length, width, cup=0.15, droop=0.0, rib=0.04):
    """A leaf lying along +Y from the origin. outline(t) -> half-width (0..1) at t in 0..1 along the leaf."""
    bm = bmesh.new()
    rows = 14
    cols = 4
    grid = []
    for i in range(rows + 1):
        t = i / rows
        hw = max(outline(t), 0.0) * width * 0.5
        row = []
        for j in range(-cols, cols + 1):
            s = j / cols
            x = s * hw
            y = t * length
            z = cup * width * (s * s) - droop * length * t * t + rib * width * (1 - abs(s)) * 0.3
            row.append(bm.verts.new((x, y, z)))
        grid.append(row)
    for i in range(rows):
        for j in range(2 * cols):
            a, b, c, d = grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]
            if (a.co - b.co).length < 1e-7 and (c.co - d.co).length < 1e-7:
                continue
            try:
                bm.faces.new((a, b, c, d))
            except ValueError:
                pass
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-7)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    me.shade_smooth()
    return obj


def stem_mesh(name, points, r0, r1, segments=8):
    """A tapered tube along points."""
    bm = bmesh.new()
    rings = []
    n = len(points)
    for i, p in enumerate(points):
        p = Vector(p)
        t = (Vector(points[min(i + 1, n - 1)]) - Vector(points[max(i - 1, 0)])).normalized()
        side = t.cross(Vector((0, 0, 1)))
        if side.length < 1e-4:
            side = t.cross(Vector((1, 0, 0)))
        side.normalize()
        up = side.cross(t)
        r = r0 + (r1 - r0) * i / max(n - 1, 1)
        ring = [bm.verts.new(p + (side * math.cos(a) + up * math.sin(a)) * r)
                for a in (math.tau * k / segments for k in range(segments))]
        rings.append(ring)
    for a_ring, b_ring in zip(rings, rings[1:]):
        for k in range(segments):
            bm.faces.new((a_ring[k], a_ring[(k + 1) % segments], b_ring[(k + 1) % segments], b_ring[k]))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    me.shade_smooth()
    return obj


def bezier_points(p0, p1, p2, steps=12):
    return [tuple(Vector(p0) * (1 - t) ** 2 + Vector(p1) * 2 * (1 - t) * t + Vector(p2) * t * t) for t in (i / steps for i in range(steps + 1))]


def place(obj, base, direction, roll=0.0):
    """Points the leaf's +Y along direction from base, rolled about its own axis."""
    d = Vector(direction).normalized()
    q = d.to_track_quat('Y', 'Z')
    obj.matrix_world = Matrix.Translation(base) @ q.to_matrix().to_4x4() @ Matrix.Rotation(roll, 4, 'Y')


# --------------------------------------------------------------------------------------------- materials
def make_material(name, colors, rough, kind):
    """kind: 'leaf' (random colour between colors per object) or 'stem'. Three outputs selected at render time."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    N = nt.nodes.new
    L = nt.links.new
    out = N("ShaderNodeOutputMaterial")
    emit = N("ShaderNodeEmission")
    L(emit.outputs[0], out.inputs["Surface"])
    info = N("ShaderNodeObjectInfo")
    ramp = N("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    while len(cr.elements) < len(colors):
        cr.elements.new(0.5)
    for el, (p, c) in zip(cr.elements, colors):
        el.position = p
        el.color = (*srgb_to_linear(c), 1.0)
    L(info.outputs["Random"], ramp.inputs[0])
    # Fine variation across each leaf (scales, veins).
    tex = N("ShaderNodeTexNoise"); tex.inputs["Scale"].default_value = 600.0; tex.inputs["Detail"].default_value = 3
    mul = N("ShaderNodeMix"); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs["Factor"].default_value = 0.25
    L(ramp.outputs[0], mul.inputs[6]); L(tex.outputs["Color"], mul.inputs[7])
    # Pass 1: albedo. Pass 2: camera-space normal. Pass 3: AO, roughness.
    geo = N("ShaderNodeNewGeometry")
    flip = N("ShaderNodeVectorMath"); flip.operation = 'SCALE'
    sign = N("ShaderNodeMath"); sign.operation = 'MULTIPLY_ADD'
    sign.inputs[1].default_value = -2.0; sign.inputs[2].default_value = 1.0
    L(geo.outputs["Backfacing"], sign.inputs[0])
    L(geo.outputs["Normal"], flip.inputs[0]); L(sign.outputs[0], flip.inputs["Scale"])
    vt = N("ShaderNodeVectorTransform"); vt.vector_type = 'NORMAL'; vt.convert_from = 'WORLD'; vt.convert_to = 'CAMERA'
    L(flip.outputs[0], vt.inputs[0])
    enc = N("ShaderNodeVectorMath"); enc.operation = 'MULTIPLY_ADD'
    enc.inputs[1].default_value = (0.5, 0.5, 0.5); enc.inputs[2].default_value = (0.5, 0.5, 0.5)
    L(vt.outputs[0], enc.inputs[0])
    ao = N("ShaderNodeAmbientOcclusion"); ao.samples = 16; ao.inputs["Distance"].default_value = 0.03
    comb = N("ShaderNodeCombineXYZ"); L(ao.outputs["AO"], comb.inputs[0]); comb.inputs[1].default_value = rough
    m["outputs"] = 1
    m["nodes"] = [mul.name, enc.name, comb.name]
    m["emit"] = emit.name
    return m


def select_pass(index):
    for m in bpy.data.materials:
        if "nodes" not in m:
            continue
        nt = m.node_tree
        src = nt.nodes[m["nodes"][index]]
        emit = nt.nodes[m["emit"]]
        for lk in list(emit.inputs["Color"].links):
            nt.links.remove(lk)
        outs = [o for o in src.outputs if o.enabled]
        nt.links.new(outs[0] if index != 0 else src.outputs[2], emit.inputs["Color"])


# --------------------------------------------------------------------------------------------- sprigs
def saltbush_sprig(rng):
    """Old-man saltbush: a woody stem with side shoots, crowded with small silvery hastate leaves and pale bracts."""
    # Silvery grey-green, but a real foliage albedo (the game's sun and leaf translucency do the rest).
    leaf_mat = make_material("leaf", [(0.0, (0.31, 0.37, 0.30)), (0.5, (0.39, 0.45, 0.37)), (1.0, (0.47, 0.51, 0.43))], 0.72, 'leaf')
    bract_mat = make_material("bract", [(0.0, (0.50, 0.54, 0.38)), (1.0, (0.58, 0.60, 0.46))], 0.6, 'leaf')
    stem_mat = make_material("stem", [(0.0, (0.48, 0.47, 0.38)), (1.0, (0.58, 0.56, 0.46))], 0.8, 'stem')
    objs = []

    def hastate(t):
        lobe = 0.55 * math.exp(-((t - 0.12) / 0.08) ** 2)
        return min(1.0, math.sin(math.pi * min(t * 1.05, 1.0)) ** 0.7 + lobe) * (1.0 - t ** 4)

    def shoot(p0, p1, p2, r0, leaves, size):
        pts = bezier_points(p0, p1, p2, 14)
        s = stem_mesh("stem", pts, r0, r0 * 0.35)
        s.data.materials.append(stem_mat)
        objs.append(s)
        for i in range(leaves):
            t = 0.12 + 0.85 * i / max(leaves - 1, 1)
            k = int(t * (len(pts) - 1))
            base = Vector(pts[k])
            tan = (Vector(pts[min(k + 1, len(pts) - 1)]) - Vector(pts[max(k - 1, 0)])).normalized()
            side = 1 if i % 2 == 0 else -1
            out = (tan + Vector((side * tan.y, -side * tan.x, 0)) * (1.1 + 0.3 * rng.random()) + Vector((0, 0, rng.uniform(-0.4, 0.6)))).normalized()
            sz = size * (0.55 + 0.45 * (1 - t)) * (0.8 + 0.4 * rng.random())
            lf = leaf_mesh("leaf", hastate, 0.036 * sz, 0.026 * sz, cup=0.25, droop=0.05)
            lf.data.materials.append(leaf_mat)
            place(lf, base, out, roll=rng.uniform(-0.6, 0.6))
            objs.append(lf)
            if rng.random() < 0.45 and t > 0.3:
                # A little cluster of round fruiting bracts in the axil.
                for b in range(rng.randint(2, 5)):
                    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=6, radius=0.0028 * sz,
                                                         location=base + out * 0.006 + Vector((rng.uniform(-0.005, 0.005), rng.uniform(-0.005, 0.005), 0.002)))
                    o = bpy.context.active_object
                    o.scale = (1, 1, 0.6)
                    o.data.materials.append(bract_mat)
                    objs.append(o)

    # Main stem up the middle, curving a little, with side shoots.
    # Crowded, like old-man saltbush: a main stem, many short side shoots, leaves overlapping all the way up.
    shoot((0, 0.0, 0), (0.01, 0.12, 0.01), (-0.01, 0.28, 0.0), 0.0035, 34, 1.0)
    for i in range(11):
        y = 0.03 + i * 0.022
        side = 1 if i % 2 == 0 else -1
        p0 = (0.003 * side, y, 0)
        reach = 0.06 + 0.07 * (1 - abs(i - 4) / 7) * rng.random() + 0.03
        tip = (side * reach, y + 0.04 + 0.05 * rng.random(), rng.uniform(-0.02, 0.02))
        shoot(p0, (side * reach * 0.5, y + 0.015, 0.005), tip, 0.0018, 12, 0.9)
    return objs, 0.3, (0.0, 0.15)


def gum_branchlet(rng):
    """Eucalyptus: a drooping twig with long, narrow, grey-green leaves hanging from it and clusters of cream blossom."""
    leaf_mat = make_material("leaf", [(0.0, (0.30, 0.38, 0.28)), (0.4, (0.40, 0.49, 0.38)), (0.8, (0.50, 0.57, 0.44)), (1.0, (0.58, 0.58, 0.40))], 0.5, 'leaf')
    flower_mat = make_material("flower", [(0.0, (0.82, 0.80, 0.62)), (1.0, (0.92, 0.88, 0.70))], 0.7, 'leaf')
    stem_mat = make_material("stem", [(0.0, (0.55, 0.35, 0.22)), (1.0, (0.62, 0.45, 0.30))], 0.75, 'stem')
    objs = []

    def lanceolate(t):
        return math.sin(math.pi * t ** 0.8) ** 1.2 * (1.0 - 0.15 * t)

    def twig(p0, p1, p2, r0, leaves, size):
        pts = bezier_points(p0, p1, p2, 16)
        s = stem_mesh("twig", pts, r0, r0 * 0.4)
        s.data.materials.append(stem_mat)
        objs.append(s)
        for i in range(leaves):
            t = 0.15 + 0.85 * i / max(leaves - 1, 1)
            k = int(t * (len(pts) - 1))
            base = Vector(pts[k])
            side = 1 if i % 2 == 0 else -1
            # Leaves hang: mostly downwards, splayed sideways, a few sticking out.
            out = Vector((side * rng.uniform(0.3, 0.9), -1.0 + rng.uniform(0.0, 0.6), rng.uniform(-0.5, 0.5))).normalized()
            sz = size * (0.75 + 0.4 * rng.random())
            lf = leaf_mesh("leaf", lanceolate, 0.16 * sz, 0.026 * sz, cup=0.08, droop=-0.08 * side, rib=0.06)
            lf.data.materials.append(leaf_mat)
            place(lf, base, out, roll=rng.uniform(-1.2, 1.2))
            objs.append(lf)
            if rng.random() < 0.18:
                for b in range(rng.randint(3, 7)):
                    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=6, radius=0.006,
                                                         location=base + Vector((rng.uniform(-0.018, 0.018), rng.uniform(-0.02, 0.01), 0.01)))
                    objs.append(bpy.context.active_object)
                    objs[-1].data.materials.append(flower_mat)

    # Twig from the bottom centre, arching up and over to the right, with a fork.
    # A leafy branchlet: the twig arches up from the bottom centre and forks; leaves hang densely from every twig.
    twig((0, 0.0, 0), (0.05, 0.45, 0.0), (0.3, 0.66, 0.0), 0.006, 26, 1.0)
    twig((0.04, 0.3, 0.0), (-0.12, 0.5, 0.01), (-0.33, 0.58, -0.01), 0.0045, 22, 0.95)
    twig((0.1, 0.5, 0.0), (0.1, 0.7, 0.0), (-0.02, 0.78, 0.0), 0.0035, 16, 0.85)
    twig((0.02, 0.15, 0.0), (0.2, 0.25, 0.0), (0.34, 0.3, 0.0), 0.004, 16, 0.9)
    twig((0.03, 0.2, 0.0), (-0.15, 0.25, 0.0), (-0.34, 0.24, 0.01), 0.004, 16, 0.9)
    return objs, 0.8, (0.0, 0.4)


def jungle_leaves(rng):
    """Rainforest canopy: a spray of twigs crowded with big, glossy, elliptical leaves with drip tips, dark green to
    a fresh yellow-green, overlapping into a dense mass (the crowns of the jungle's giants)."""
    leaf_mat = make_material("leaf", [(0.0, (0.05, 0.13, 0.03)), (0.45, (0.09, 0.21, 0.04)), (0.8, (0.15, 0.30, 0.06)),
                                      (1.0, (0.24, 0.38, 0.08))], 0.38, 'leaf')
    stem_mat = make_material("stem", [(0.0, (0.30, 0.26, 0.16)), (1.0, (0.38, 0.33, 0.20))], 0.8, 'stem')
    objs = []

    def elliptic(t):
        # Broad in the middle, rounded at the base, drawn out into a drip tip.
        return math.sin(math.pi * min(t / 0.92, 1.0) ** 0.9) ** 0.7 * (1.0 - 0.6 * max(0.0, t - 0.8) / 0.2)

    def twig(p0, p1, p2, r0, leaves, size):
        pts = bezier_points(p0, p1, p2, 16)
        s = stem_mesh("twig", pts, r0, r0 * 0.4)
        s.data.materials.append(stem_mat)
        objs.append(s)
        for i in range(leaves):
            t = 0.12 + 0.88 * i / max(leaves - 1, 1)
            k = int(t * (len(pts) - 1))
            base = Vector(pts[k])
            ahead = (Vector(pts[min(k + 1, len(pts) - 1)]) - Vector(pts[max(k - 1, 0)])).normalized()
            side = Vector((-ahead.y, ahead.x, 0)) * (1 if i % 2 == 0 else -1)
            out = (side * rng.uniform(0.7, 1.0) + ahead * rng.uniform(0.3, 0.8)).normalized()
            out.z = rng.uniform(-0.15, 0.25)
            sz = size * (0.75 + 0.45 * rng.random()) * (0.7 + 0.3 * t)
            lf = leaf_mesh("leaf", elliptic, 0.24 * sz, 0.10 * sz, cup=0.12, droop=-0.06, rib=0.05)
            lf.data.materials.append(leaf_mat)
            place(lf, base, out, roll=rng.uniform(-0.4, 0.4))
            objs.append(lf)
        # A leaf at the very tip.
        lf = leaf_mesh("leaf", elliptic, 0.22 * size, 0.09 * size, cup=0.12, droop=-0.06, rib=0.05)
        lf.data.materials.append(leaf_mat)
        place(lf, Vector(pts[-1]), (Vector(pts[-1]) - Vector(pts[-2])).normalized(), roll=0.2)
        objs.append(lf)

    twig((0, 0.0, 0), (0.02, 0.4, 0.0), (-0.02, 0.62, 0.0), 0.006, 9, 1.0)
    twig((0.01, 0.12, 0.0), (0.13, 0.3, 0.01), (0.17, 0.5, 0.0), 0.0045, 7, 0.85)
    twig((0.0, 0.18, 0.0), (-0.13, 0.32, 0.01), (-0.17, 0.48, 0.0), 0.0045, 7, 0.85)
    twig((0.01, 0.06, 0.0), (0.12, 0.1, 0.0), (0.18, 0.2, 0.01), 0.004, 5, 0.75)
    twig((0.0, 0.08, 0.0), (-0.12, 0.12, 0.0), (-0.18, 0.2, 0.01), 0.004, 5, 0.75)
    return objs, 0.8, (0.0, 0.4)


def palm_frond(rng):
    """A metre of a walking palm's pinnate frond seen from above: the rachis straight up the middle of the card from
    its bottom edge, long narrow leaflets in pairs angled forwards and drooping, split into ragged groups (cards are
    chained along each frond in the game)."""
    leaf_mat = make_material("leaf", [(0.0, (0.12, 0.22, 0.06)), (0.5, (0.18, 0.31, 0.08)), (1.0, (0.27, 0.40, 0.12))], 0.42, 'leaf')
    stem_mat = make_material("stem", [(0.0, (0.26, 0.30, 0.14)), (1.0, (0.33, 0.36, 0.18))], 0.6, 'stem')
    objs = []
    pts = bezier_points((0, -0.02, 0), (0, 0.45, 0.0), (0, 0.94, 0.0), 16)
    s = stem_mesh("rachis", pts, 0.009, 0.007)
    s.data.materials.append(stem_mat)
    objs.append(s)

    def narrow(t):
        return math.sin(math.pi * t ** 0.75) ** 0.9

    y = 0.0
    while y < 0.9:
        for side in (1, -1):
            a = math.radians(rng.uniform(42, 62))
            out = Vector((side * math.sin(a), math.cos(a), rng.uniform(-0.2, 0.05))).normalized()
            ln = rng.uniform(0.34, 0.42)
            lf = leaf_mesh("leaflet", narrow, ln, rng.uniform(0.032, 0.042), cup=0.2, droop=-0.12 * side, rib=0.07)
            lf.data.materials.append(leaf_mat)
            place(lf, Vector((0, y + rng.uniform(-0.008, 0.008), 0.004)), out, roll=rng.uniform(-0.5, 0.5))
            objs.append(lf)
        y += rng.uniform(0.034, 0.05) + (0.035 if rng.random() < 0.12 else 0.0)   # now and then a gap
    return objs, 0.9, (0.0, 0.45)


def quad_object(name, corners, material):
    """One flat quad (four corners, in order) as its own object."""
    bm = bmesh.new()
    bm.faces.new([bm.verts.new(c) for c in corners])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(material)
    return obj


def fern_frond(rng):
    """A fern frond seen from above, twice divided: the rachis straight up the middle from the card's bottom edge;
    pinnae in pairs angled forwards, longest a third of the way up and shortening to the tip; each pinna a row of small
    rounded, toothed pinnules. Bright, fresh green."""
    leaf_mat = make_material("leaf", [(0.0, (0.12, 0.27, 0.05)), (0.5, (0.19, 0.38, 0.07)), (1.0, (0.29, 0.47, 0.10))], 0.55, 'leaf')
    stem_mat = make_material("stem", [(0.0, (0.20, 0.27, 0.08)), (1.0, (0.28, 0.33, 0.12))], 0.6, 'stem')
    objs = []
    s = stem_mesh("rachis", bezier_points((0, 0.0, 0), (0, 0.5, 0.0), (0.0, 0.98, 0.0), 16), 0.006, 0.002)
    s.data.materials.append(stem_mat)
    objs.append(s)

    def pinnule(t):
        return math.sin(math.pi * min(t / 0.95, 1.0)) ** 0.6

    y = 0.06
    while y < 0.95:
        t = y / 0.98
        length = 0.47 * math.sin(math.pi * (0.2 + 0.8 * t)) ** 0.7 * (1.0 - 0.45 * t)
        for side in (1, -1):
            a = math.radians(rng.uniform(58, 72))
            d = Vector((side * math.sin(a), math.cos(a), 0)).normalized()
            base = Vector((0, y, 0.002))
            sub = stem_mesh("pinna", [base, base + d * length], 0.0025, 0.001)
            sub.data.materials.append(stem_mat)
            objs.append(sub)
            n = max(3, int(length / 0.03))
            for k in range(n):
                u = (k + 0.5) / n
                at = base + d * (length * u)
                size = 0.05 * (1.0 - 0.55 * u) * (0.9 + 0.2 * rng.random())
                for ss in (1, -1):
                    out = (d * 0.45 + Vector((-d.y, d.x, 0)) * ss).normalized()
                    lf = leaf_mesh("pinnule", pinnule, size, size * 0.55, cup=0.1, droop=0.0, rib=0.02)
                    lf.data.materials.append(leaf_mat)
                    place(lf, at, out, roll=rng.uniform(-0.3, 0.3))
                    objs.append(lf)
        y += 0.045 * (1.0 - 0.35 * t)
    return objs, 1.0, (0.0, 0.5)


def banana_leaf(rng):
    """A banana leaf filling the card (the game stretches it onto a long, bent strip): a pale midrib up the middle,
    the blade either side torn along its parallel veins into ragged strips, a few dry brown edges and tears that reach
    the midrib. Bright yellow-green."""
    leaf_mat = make_material("leaf", [(0.0, (0.20, 0.38, 0.07)), (0.5, (0.27, 0.46, 0.09)), (0.9, (0.34, 0.52, 0.12)),
                                      (1.0, (0.45, 0.40, 0.16))], 0.35, 'leaf')
    rib_mat = make_material("stem", [(0.0, (0.55, 0.60, 0.32)), (1.0, (0.62, 0.66, 0.38))], 0.4, 'stem')
    objs = []
    rib = stem_mesh("midrib", [Vector((0, 0.0, 0.004)), Vector((0, 0.5, 0.004)), Vector((0, 0.99, 0.004))], 0.022, 0.006)
    rib.data.materials.append(rib_mat)
    objs.append(rib)

    def half_width(t):
        return 0.47 * math.sin(math.pi * min(t / 0.98, 1.0)) ** 0.35 * (1.0 - 0.15 * t)

    for side in (1, -1):
        y = 0.01
        while y < 0.98:
            step = rng.uniform(0.018, 0.05)
            y1 = min(0.985, y + step)
            # A tear: the strip ends short of the edge, or a gap is left before the next one.
            edge0 = half_width(y) * (rng.uniform(0.55, 0.9) if rng.random() < 0.15 else 1.0)
            edge1 = half_width(y1) * (edge0 / max(half_width(y), 1e-4))
            slant = 0.06   # the veins run out and a little forwards from the midrib
            corners = [Vector((side * 0.012, y, 0)), Vector((side * edge0, y + slant, 0)),
                       Vector((side * edge1, y1 + slant, 0)), Vector((side * 0.012, y1, 0))]
            if side < 0:
                corners.reverse()
            objs.append(quad_object("strip", corners, leaf_mat))
            y = y1 + (rng.uniform(0.004, 0.012) if rng.random() < 0.35 else 0.0)
    return objs, 1.0, (0.0, 0.5)


def big_leaf(rng):
    """A broad rainforest leaf filling the card (stretched onto bent strips in the game): ovate, with a drawn-out
    drip tip, a pale midrib and arching side veins; glossy dark green."""
    leaf_mat = make_material("leaf", [(0.0, (0.07, 0.17, 0.04)), (0.5, (0.10, 0.23, 0.05)), (1.0, (0.15, 0.29, 0.07))], 0.3, 'leaf')
    vein_mat = make_material("stem", [(0.0, (0.30, 0.42, 0.14)), (1.0, (0.36, 0.48, 0.18))], 0.4, 'stem')
    objs = []

    def ovate(t):
        return math.sin(math.pi * min(t / 0.97, 1.0) ** 0.75) ** 0.55 * (1.0 - 0.7 * max(0.0, t - 0.82) / 0.18)

    lf = leaf_mesh("blade", ovate, 0.97, 0.94, cup=0.0, droop=0.0, rib=0.0)
    lf.data.materials.append(leaf_mat)
    place(lf, Vector((0, 0.015, 0)), Vector((0, 1, 0)))
    objs.append(lf)
    mid = stem_mesh("midrib", [Vector((0, 0.0, 0.004)), Vector((0, 0.5, 0.004)), Vector((0, 0.95, 0.004))], 0.008, 0.002)
    mid.data.materials.append(vein_mat)
    objs.append(mid)
    for k in range(9):
        t = 0.1 + 0.08 * k
        hw = ovate(t) * 0.47
        for side in (1, -1):
            p0 = Vector((0, 0.015 + t * 0.97, 0.004))
            p2 = Vector((side * hw * 0.85, p0.y + 0.16 * (1 - t * 0.5), 0.004))
            p1 = Vector((side * hw * 0.45, p0.y + 0.03, 0.004))
            v = stem_mesh("vein", bezier_points(p0, p1, p2, 8), 0.0028, 0.0008)
            v.data.materials.append(vein_mat)
            objs.append(v)
    return objs, 1.0, (0.0, 0.5)


SPRIGS = {"saltbush_sprig": saltbush_sprig, "gum_branchlet": gum_branchlet, "jungle_leaves": jungle_leaves, "palm_frond": palm_frond,
          "fern_frond": fern_frond, "banana_leaf": banana_leaf, "big_leaf": big_leaf}


# --------------------------------------------------------------------------------------------- rendering
def setup(size, ortho, center, res):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    prefs = bpy.context.preferences.addons['cycles'].preferences
    try:
        prefs.compute_device_type = 'OPTIX'
        prefs.get_devices()
        for d in prefs.devices:
            d.use = d.type != 'CPU'
        scene.cycles.device = 'GPU'
    except Exception:
        pass
    scene.cycles.samples = 64
    scene.cycles.use_denoising = False
    scene.cycles.pixel_filter_type = 'BLACKMAN_HARRIS'
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = res
    scene.render.image_settings.file_format = 'OPEN_EXR'
    scene.render.image_settings.color_depth = '32'
    scene.view_settings.view_transform = 'Standard'
    world = bpy.data.worlds.new("w")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.0
    cam_data = bpy.data.cameras.new("ortho")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = ortho
    cam = bpy.data.objects.new("ortho", cam_data)
    scene.collection.objects.link(cam)
    cam.location = (center[0], center[1], 2.0)
    cam.rotation_euler = (0, 0, 0)   # looking down -Z, +Y up in the image
    cam_data.clip_end = 10
    scene.camera = cam
    return scene


def render_pass(scene, index, res):
    select_pass(index)
    path = os.path.join(OUT, "_leafpass.exr")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    a = np.empty(res * res * 4, np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    os.remove(path)
    return a.reshape(res, res, 4)


def dilate(rgb, alpha, iterations=24):
    """Bleeds colour from covered pixels into empty ones, so mipmaps don't pick up dark halos."""
    out = rgb.copy()
    w = (alpha > 0.5).astype(np.float32)
    for _ in range(iterations):
        acc = np.zeros_like(out)
        cnt = np.zeros_like(w)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                acc += np.roll(np.roll(out * w[..., None], dy, 0), dx, 1)
                cnt += np.roll(np.roll(w, dy, 0), dx, 1)
        grow = (w == 0) & (cnt > 0)
        out[grow] = acc[grow] / cnt[grow][..., None]
        w = np.maximum(w, grow.astype(np.float32))
    mean = (rgb * (alpha[..., None] > 0.5)).sum((0, 1)) / max((alpha > 0.5).sum(), 1)
    out[w == 0] = mean
    return out


def srgb(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def save_png(name, arr):
    h, w, _ = arr.shape
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.colorspace_settings.name = 'Non-Color'
    img.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = os.path.join(OUT, name + ".png")
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def build(name, res=1024):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rng = random.Random(sum(map(ord, name)))
    objs, ortho, center = SPRIGS[name](rng)
    scene = setup(res, ortho, center, res)
    albedo = render_pass(scene, 0, res)
    normal = render_pass(scene, 1, res)
    orm = render_pass(scene, 2, res)
    alpha = albedo[..., 3]
    cover = np.clip(alpha, 0, 1)
    # Un-premultiply the colours (Cycles' transparent film premultiplies by coverage), then bleed them outwards.
    safe = np.maximum(cover, 1e-4)[..., None]
    col = dilate(np.clip(albedo[..., :3] / safe, 0, 1), cover)
    nrm = normal[..., :3] / safe
    nrm = nrm * 2 - 1
    nrm[cover < 0.02] = (0, 0, 1)
    nrm /= np.maximum(np.linalg.norm(nrm, axis=-1, keepdims=True), 1e-6)
    nrm = dilate(nrm * 0.5 + 0.5, cover)
    ormc = dilate(np.clip(orm[..., :3] / safe, 0, 1), cover)
    ones = np.ones_like(cover)
    save_png(name + "_albedo", np.dstack([srgb(col), cover]))
    save_png(name + "_normal", np.dstack([nrm, ones]))
    save_png(name + "_orm", np.dstack([ormc[..., 0], ormc[..., 1], np.zeros_like(cover), ones]))
    # Preview: the sprig over a sandy background.
    bg = np.array([0.45, 0.22, 0.12])
    prev = srgb(col * cover[..., None] + bg * (1 - cover[..., None]))
    os.makedirs(PREVIEWS, exist_ok=True)
    img = bpy.data.images.new("prev", res, res, alpha=True)
    img.colorspace_settings.name = 'Non-Color'
    img.pixels.foreach_set(np.dstack([prev, ones]).astype(np.float32).ravel())
    img.filepath_raw = os.path.join(PREVIEWS, name + ".png")
    img.file_format = 'PNG'
    img.save()
    print(f"[leaves] {name}: {len(objs)} objects, coverage {cover.mean():.2f}", flush=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for n in argv or list(SPRIGS):
        try:
            build(n)
        except Exception:
            import traceback
            traceback.print_exc()
            print(f"[leaves] FAILED {n}", flush=True)
    print("[leaves] done", flush=True)


main()
