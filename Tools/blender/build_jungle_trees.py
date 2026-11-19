"""
Builds the jungle's trees and exports Assets/Models/<name>.glb, with previews in Tools/previews/models:

  jungle_giant_a, _b    rainforest giants (kapok, strangler fig): a fluted trunk flaring at its foot into tall, thin,
                        sinuous plank buttresses that run on across the ground as surface roots; strangler roots and
                        cords down the trunk; limbs high up to a broad crown of broadleaf cards; lianas hanging down
  jungle_tree_a, _b     the same kind of tree, smaller: the medium trees of the forest
  walking_palm_a, _b    walking palms (Socratea exorrhiza): a cone of spiny stilt roots, a slim ringed trunk and a
                        crownshaft, and long arching pinnate fronds (chained frond cards)

    blender -b --factory-startup -P Tools/blender/build_jungle_trees.py -- [names...]

Each also writes Assets/Models/<name>_points.json in the game's coordinates (x, z; +Y up): the trunk's radius and the
buttresses' outlines ([x, z, top] along each fin), so the game can stop the player at the fins.

Bark is tex_bark_kapok / tex_bark_palm (bake_textures.py), the leaves leaf_jungle_leaves / leaf_palm_frond
(bake_leaves.py). UVs are in texture repeats: metres / the set's tile.
"""
import json
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import modelkit as K  # noqa: E402
from modelkit import mat  # noqa: E402

UP = Vector((0, 0, 1))
KAPOK_TILE = K.TILE["bark_kapok"]
PALM_TILE = K.TILE["bark_palm"]


def rand_dir(rng):
    while True:
        v = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1)))
        if 0.05 < v.length <= 1.0:
            return v.normalized()


def perpendicular(d, rng):
    p = d.cross(rand_dir(rng))
    while p.length < 1e-3:
        p = d.cross(rand_dir(rng))
    return p.normalized()


def heading(a):
    return Vector((math.cos(a), math.sin(a), 0))


def bezier(p0, p1, p2, steps):
    return [p0 * (1 - t) ** 2 + p1 * 2 * (1 - t) * t + p2 * t * t for t in (i / steps for i in range(steps + 1))]


# ============================================================================================ meshes
class Mesh:
    """A bmesh with a UV layer, turned into one object at the end."""

    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")

    def face(self, verts, uvs):
        f = self.bm.faces.new(verts)
        for loop, uv in zip(f.loops, uvs):
            loop[self.uv].uv = uv
        return f

    def tube(self, pts, radii, sides, tile, radius_fn=None, cap=True, u_offset=0.0):
        """A tube through pts (parallel-transported rings). radius_fn(i, angle, z) scales a ring vertex's radius."""
        n = len(pts)
        tangents = [(pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized() for i in range(n)]
        t0 = tangents[0]
        x = (t0.cross(UP) if abs(t0.z) < 0.99 else t0.cross(Vector((1, 0, 0)))).normalized()
        frames = []
        for t in tangents:
            x = (x - t * x.dot(t)).normalized()
            frames.append((x, t.cross(x)))
        circ = math.tau * sum(radii) / n
        urep = max(1, round(circ / tile))
        vcoord = [0.0]
        for i in range(1, n):
            vcoord.append(vcoord[-1] + (pts[i] - pts[i - 1]).length / tile)
        rings = []
        for i in range(n):
            fx, fy = frames[i]
            ring = []
            for k in range(sides):
                a = math.tau * k / sides
                r = radii[i] * (radius_fn(i, a, pts[i].z) if radius_fn else 1.0)
                ring.append(self.bm.verts.new(pts[i] + (fx * math.cos(a) + fy * math.sin(a)) * r))
            rings.append(ring)
        for i in range(n - 1):
            for k in range(sides):
                k2 = (k + 1) % sides
                self.face((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]),
                          [(u_offset + urep * kk / sides, vcoord[ii]) for ii, kk in ((i, k), (i, k + 1), (i + 1, k + 1), (i + 1, k))])
        if cap:
            tip = self.bm.verts.new(pts[-1] + tangents[-1] * radii[-1] * 0.8)
            for k in range(sides):
                k2 = (k + 1) % sides
                self.face((rings[-1][k], rings[-1][k2], tip),
                          [(urep * k / sides, vcoord[-1]), (urep * (k + 1) / sides, vcoord[-1]), (urep * (k + 0.5) / sides, vcoord[-1] + 0.1)])
        return rings

    def object(self, name, material, smooth_angle=70.0):
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        o = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(o)
        o.data.materials.append(material)
        o["uv"] = "keep"
        o["smooth"] = True
        o["smooth_angle"] = smooth_angle
        return o


def cards_object(name, cards, material):
    """cards: [(base, up, side, size, normal_fn)]: a quad from base (the atlas's bottom centre) up `up` by size, size
    wide along `side`; normal_fn(point) gives the soft normal at a corner (so a crown shades as one rounded mass)."""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    normals = []
    for base, up, side, size, nfn in cards:
        h = side * (size / 2)
        corners = [base - h, base + h, base + h + up * size, base - h + up * size]
        f = bm.faces.new([bm.verts.new(c) for c in corners])
        for loop, uv in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
            loop[uvl].uv = uv
        normals.append([nfn(c) for c in corners])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.shade_smooth()
    loop_normals = [None] * len(me.loops)
    for poly, ns in zip(me.polygons, normals):
        for k, li in enumerate(poly.loop_indices):
            loop_normals[li] = ns[k]
    me.normals_split_custom_set(loop_normals)
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(material)
    return o


def soft_normal(centre, up_bias=0.3):
    def fn(p):
        d = p - centre
        n = (d.normalized() if d.length > 1e-6 else UP) + UP * up_bias
        return tuple(n.normalized())
    return fn


# ============================================================================================ rainforest trees
def rainforest_tree(name, seed, radius, height, fins, fin_rise, fin_reach, stranglers, limbs, crown, lianas):
    """A buttressed rainforest tree. radius: the trunk's radius above the buttresses; fins: how many plank buttresses,
    rising fin_rise x radius up the trunk and reaching fin_reach x radius out; stranglers: roots and cords down the
    trunk; limbs: main limbs to the crown, crown: its radius (m); lianas: vines hanging from the limbs."""
    rng = random.Random(seed)
    bark = mat("tex_bark_kapok", None, 0.85, 0.0)
    vine = mat("tex_bark_palm", None, 0.8, 0.0)
    wood = Mesh()
    cords = Mesh()
    lean = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)) * height * 0.012
    fin_angles = sorted(rng.uniform(0, math.tau) if fins < 3 else math.tau * k / fins + rng.uniform(-0.3, 0.3) for k in range(fins))
    rise = radius * fin_rise
    phase = [rng.uniform(0, math.tau) for _ in range(4)]

    # --- The trunk: a fluted, lobed column that flares at the foot towards each buttress.
    zs = [-0.6, 0.0, 0.3, 0.7, 1.2, 1.9, 2.8, 3.9, 5.2]
    while zs[-1] < height * 0.9:
        zs.append(zs[-1] + float(min(3.0, height * 0.1)))
    zs.append(height)
    pts, radii = [], []
    for z in zs:
        t = max(z, 0.0) / height
        wobble = Vector((math.sin(z * 0.35 + phase[0]), math.cos(z * 0.29 + phase[1]), 0)) * radius * 0.08
        pts.append(Vector((0, 0, z)) + lean * t * t + wobble * min(1.0, z / 4.0))
        radii.append(radius * (1.0 - 0.45 * t) * (1.0 + 0.35 * math.exp(-max(z, 0.0) / 1.2)))

    def flute(i, a, z):
        r = 1.0 + 0.07 * math.sin(3 * a + phase[2]) + 0.045 * math.sin(7 * a + phase[3] + z * 0.2) + 0.025 * math.sin(13 * a + z)
        k = max(0.0, 1.0 - max(z, 0.0) / rise) ** 1.3
        for fa in fin_angles:
            da = math.atan2(math.sin(a - fa), math.cos(a - fa))
            r += k * 1.1 * math.exp(-(da / 0.2) ** 2)
        return r
    wood.tube(pts, radii, 28, KAPOK_TILE, radius_fn=flute)

    def trunk_at(z):
        """Centre and radius of the trunk at height z."""
        for i in range(len(zs) - 1):
            if zs[i] <= z <= zs[i + 1]:
                t = (z - zs[i]) / (zs[i + 1] - zs[i])
                return pts[i].lerp(pts[i + 1], t), radii[i] + (radii[i + 1] - radii[i]) * t
        return pts[-1], radii[-1]

    # --- Plank buttresses: thin, tall fins winding out from the trunk, their tops falling in a concave curve to the
    # ground; then surface roots running on, half buried.
    fin_outlines = []
    for fa in fin_angles:
        d = heading(fa)
        side0 = Vector((-d.y, d.x, 0))
        reach = radius * fin_reach * rng.uniform(0.75, 1.15)
        height_k = rise * rng.uniform(0.7, 1.1)
        thick = max(0.1, radius * rng.uniform(0.24, 0.34))
        wig, wph = rng.uniform(0.3, 1.0) * reach * 0.22, rng.uniform(0, math.tau)
        segs = 18
        centres, tops, halves = [], [], []
        for k in range(segs + 1):
            t = k / segs
            centres.append(d * (radius * 0.6 + reach * t) + side0 * (wig * math.sin(t * 3.3 + wph) * t))
            # High where it leaves the trunk, sweeping down in a long concave curve, and steeply down at its end.
            tops.append(0.08 + height_k * (1 - t) ** 1.15 * (1 - 0.3 * t))
            halves.append(thick * (1.0 - 0.6 * t) / 2)
        outline = []
        rows = []
        u = 0.0
        for k in range(segs + 1):
            if k:
                u += (centres[k] - centres[k - 1]).length / KAPOK_TILE
            a = centres[max(k - 1, 0)]
            b = centres[min(k + 1, segs)]
            perp = Vector((-(b - a).y, (b - a).x, 0)).normalized()
            c, top, hw = centres[k], tops[k], halves[k]
            # Cross-section: the fin's two faces (thicker at the foot, a little concave) and a rounded top edge.
            prof = [(-hw * 1.5, -0.5), (-hw * 1.15, top * 0.3), (-hw * 0.95, top * 0.75), (-hw * 0.6, top + hw * 0.4),
                    (0.0, top + hw * 0.9), (hw * 0.6, top + hw * 0.4), (hw * 0.95, top * 0.75), (hw * 1.15, top * 0.3), (hw * 1.5, -0.5)]
            ring = [wood.bm.verts.new(Vector((c.x, c.y, 0)) + perp * s + Vector((0, 0, h))) for s, h in prof]
            vs = [0.0]
            for j in range(1, len(prof)):
                vs.append(vs[-1] + math.hypot(prof[j][0] - prof[j - 1][0], prof[j][1] - prof[j - 1][1]) / KAPOK_TILE)
            rows.append((ring, u, vs))
            outline.append([round(c.x, 3), round(-c.y, 3), round(top, 3)])   # game x, z, top
        for k in range(segs):
            (r0, u0, v0), (r1, u1, v1) = rows[k], rows[k + 1]
            for j in range(len(r0) - 1):
                wood.face((r0[j], r0[j + 1], r1[j + 1], r1[j]), [(u0, v0[j]), (u0, v0[j + 1]), (u1, v1[j + 1]), (u1, v1[j])])
        end_ring = rows[-1][0]
        wood.face(list(reversed(end_ring)), [(0.0, 0.0)] * len(end_ring))
        fin_outlines.append(outline)
        # The root carries on along the ground, half buried, winding away.
        at = centres[-1] + Vector((0, 0, 0.02))
        hd = (centres[-1] - centres[-2]).normalized()
        root = [at.copy()]
        for s in range(4 + rng.randrange(4)):
            hd = (hd + Vector((-hd.y, hd.x, 0)) * rng.uniform(-0.5, 0.5)).normalized()
            at = at + hd * rng.uniform(0.6, 1.0)
            root.append(Vector((at.x, at.y, rng.uniform(-0.02, 0.06))))
        wood.tube(root, [thick * 0.75 * (1 - 0.7 * i / (len(root) - 1)) for i in range(len(root))], 7, KAPOK_TILE)

    # --- Strangler roots and cords: down the trunk from high up, hugging it and drifting round it, into the ground.
    for s in range(stranglers):
        a = rng.uniform(0, math.tau)
        z0 = height * rng.uniform(0.35, 0.7)
        thick_root = rng.random() < 0.4
        r_c = radius * (rng.uniform(0.07, 0.12) if thick_root else rng.uniform(0.025, 0.05))
        path = []
        steps = 26
        drift = rng.uniform(-0.8, 0.8)
        for i in range(steps + 1):
            t = i / steps
            z = z0 * (1 - t) - 0.3 * t
            a_i = a + drift * t + 0.15 * math.sin(t * 9 + s)
            c, r = trunk_at(max(z, 0.0))
            r *= flute(0, a_i, max(z, 0.0))
            path.append(c + heading(a_i) * (r + r_c * 0.6) + Vector((0, 0, z - c.z)))
        (wood if thick_root else cords).tube(path, [r_c * (0.7 + 0.3 * i / steps) for i in range(steps + 1)], 7 if thick_root else 5,
                                             KAPOK_TILE if thick_root else PALM_TILE)

    # --- Limbs: from high on the trunk, out and up to the crown, each forking once or twice.
    tips = []
    top_c, top_r = trunk_at(height * 0.98)
    for k in range(limbs):
        a = math.tau * k / limbs + rng.uniform(-0.35, 0.35)
        z0 = height * rng.uniform(0.7, 0.88)
        c, r = trunk_at(z0)
        out = heading(a)
        reach = crown * rng.uniform(0.6, 0.85)
        p0 = c + out * r * 0.5
        p2 = c + out * reach + Vector((0, 0, height * rng.uniform(0.1, 0.2)))
        p1 = c + out * reach * 0.35 + Vector((0, 0, height * 0.1))
        limb = bezier(p0, p1, p2, 8)
        lr = r * rng.uniform(0.45, 0.6)
        wood.tube(limb, [lr * (1 - 0.6 * i / 8) for i in range(9)], 10, KAPOK_TILE)
        tips.append(limb[-1])
        for f in range(2):
            fp = limb[rng.randrange(4, 8)]
            fa = a + rng.choice((-1, 1)) * rng.uniform(0.5, 1.1)
            fe = fp + heading(fa) * crown * rng.uniform(0.25, 0.4) + Vector((0, 0, rng.uniform(0.5, 2.5)))
            twig = bezier(fp, (fp + fe) / 2 + Vector((0, 0, 0.8)), fe, 5)
            wood.tube(twig, [lr * 0.45 * (1 - 0.6 * i / 5) for i in range(6)], 6, KAPOK_TILE)
            tips.append(fe)
            # Lianas hang from some of the forks.
            if lianas > 0 and rng.random() < lianas / (limbs * 2):
                low = Vector((fp.x + rng.uniform(-1, 1), fp.y + rng.uniform(-1, 1), -0.3))
                swing = (fp + low) / 2 + heading(rng.uniform(0, math.tau)) * rng.uniform(0.5, 2.0)
                hang = bezier(fp, swing, low, 16)
                hang = [p + Vector((math.sin(i * 0.9 + k) * 0.12, math.cos(i * 0.7 + k) * 0.12, 0)) for i, p in enumerate(hang)]
                cords.tube(hang, [rng.uniform(0.035, 0.06)] * len(hang), 5, PALM_TILE)
    tips.append(top_c + Vector((0, 0, 1.0)))

    # --- The crown: clumps of broadleaf cards round every limb tip, a broad, flat-bottomed mass.
    leaf = mat("leaf_jungle_leaves", None, 0.45, 0.0)
    centre = sum(tips, Vector()) / len(tips) - Vector((0, 0, 1.5))
    cards = []
    for p in tips:
        n = max(8, int(rng.randint(22, 30) * min(1.0, crown / 9.0)))   # (cards by crown size: overdraw)
        for i in range(n):
            off = rand_dir(rng) * rng.uniform(0.0, crown * 0.42)
            base = p + Vector((off.x, off.y, off.z * 0.35 - 0.6))
            out = base - centre
            out = Vector((out.x, out.y, 0)).normalized() if out.length > 1e-3 else rand_dir(rng)
            up = (out * rng.uniform(0.5, 1.0) + rand_dir(rng) * 0.5 + UP * rng.uniform(0.0, 0.7)).normalized()
            size = rng.uniform(1.8, 2.8) * min(1.0, crown / 7.0 + 0.4)
            cards.append((base - up * size * 0.2, up, perpendicular(up, rng), size, soft_normal(centre, 0.45)))
    leaves = cards_object(name + "_leaves", cards, leaf)

    objs = [wood.object(name + "_wood", bark), leaves]
    if len(cords.bm.faces):
        objs.insert(1, cords.object(name + "_vines", vine))
    else:
        cords.bm.free()
    points = {"TrunkRadius": round(radius * 1.25, 3), "Height": height, "Fins": fin_outlines,
              "Note": "Game coordinates (x, z; +Y up), metres. Fins: [x, z, top] along each buttress, from the trunk out."}
    print(f"[jungle] {name}: {fins} buttresses, {stranglers} strangler roots, {len(tips)} tips, {len(cards)} leaf cards", flush=True)
    return objs, points


# ============================================================================================ walking palm
def walking_palm(name, seed, height, root_top, spread, roots, fronds):
    """Socratea exorrhiza: a slim trunk standing on a cone of stilt roots from root_top metres up, spreading spread
    metres; a smooth crownshaft; fronds whose leaflets hang from arching rachises."""
    rng = random.Random(seed)
    bark = mat("tex_bark_palm", None, 0.7, 0.0)
    stem = Mesh()
    lean = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)) * 0.6
    # The trunk ends in a rounded point down inside the cone of roots (built top-down so the cap is at the foot).
    foot = Vector((0, 0, root_top * 0.3))
    top = Vector((0, 0, height)) + lean
    n = 14
    trunk = [top.lerp(foot, i / n) + Vector((math.sin(i * 0.8 + seed), math.cos(i * 0.6 + seed), 0)) * 0.03 for i in range(n + 1)]
    stem.tube(trunk, [0.11 + 0.03 * i / n for i in range(n + 1)], 10, PALM_TILE)
    # The crownshaft: a smooth, slightly swollen sheath at the top, from which the fronds spring.
    shaft = [top + Vector((0, 0, 1.4 * i / 6)) for i in range(7)]
    stem.tube(shaft, [0.12 + 0.05 * math.sin(math.pi * i / 6) for i in range(7)], 10, PALM_TILE)
    # Stilt roots: from the trunk's foot, bowing out and down to a ring on the ground (and a little into it).
    for k in range(roots):
        a = math.tau * k / roots + rng.uniform(-0.2, 0.2)
        d = heading(a)
        start = Vector((0, 0, root_top * rng.uniform(0.6, 1.0))) + d * 0.06 + lean * (root_top / height)
        end = d * spread * rng.uniform(0.75, 1.15) - Vector((0, 0, 0.25))
        bow = (start + end) / 2 + d * rng.uniform(0.08, 0.25) + Vector((0, 0, 0.12))
        path = bezier(start, bow, end, 8)
        stem.tube(path, [0.05 - 0.012 * i / 8 for i in range(9)], 6, PALM_TILE, cap=False)

    # Fronds: each rachis rises out of the crownshaft and arches over; leaf cards chained along it, lying flat.
    leaf = mat("leaf_palm_frond", None, 0.45, 0.0)
    crown_top = shaft[-1]
    cards = []
    for k in range(fronds):
        a = math.tau * k / fronds + rng.uniform(-0.25, 0.25)
        d = heading(a)
        length = rng.uniform(3.0, 4.2)
        rise = math.radians(rng.uniform(25, 60))
        pts = [crown_top - Vector((0, 0, 0.3))]
        direction = (d * math.cos(rise) + UP * math.sin(rise)).normalized()
        step = 0.1
        for i in range(int(length / step)):
            direction = (direction - UP * 0.028 * (1 + i * 0.02)).normalized()   # it droops more towards the tip
            pts.append(pts[-1] + direction * step)
        stem.tube(pts[:6], [0.035, 0.032, 0.03, 0.028, 0.026, 0.024], 5, PALM_TILE)   # the bare petiole
        size = 1.15
        i = 4
        twist = rng.uniform(-0.4, 0.4)
        while i < len(pts) - 2:
            j = min(i + int(size / step), len(pts) - 1)
            up = (pts[j] - pts[i]).normalized()
            side = up.cross(UP)
            side = side.normalized() if side.length > 1e-3 else perpendicular(up, rng)
            # Rolled a little about the rachis, so the leaflets hang down on both sides like a shallow V.
            side = (side * math.cos(twist) + up.cross(side) * math.sin(twist)).normalized()
            seg = (pts[j] - pts[i]).length
            cards.append((pts[i], up, side, max(seg, 0.4), soft_normal(crown_top - Vector((0, 0, 2.0)), 0.6)))
            i = j
    leaves = cards_object(name + "_fronds", cards, leaf)
    objs = [stem.object(name + "_stem", bark, smooth_angle=80.0), leaves]
    points = {"TrunkRadius": 0.14, "Height": height + 1.4, "Spread": round(spread, 3), "Fins": [],
              "Note": "Game coordinates (x, z; +Y up), metres."}
    print(f"[jungle] {name}: {roots} stilt roots, {fronds} fronds, {len(cards)} frond cards", flush=True)
    return objs, points


# ============================================================================================ canopy
def canopy_clump(name, seed, radius, height, count):
    """A mass of the canopy: a broad, flattened dome of broadleaf cards (radius metres across, height deep), densest
    on top and round its rim, with a few twigs holding it up. Scaled and scattered by the game to close in the sky."""
    rng = random.Random(seed)
    leaf = mat("leaf_jungle_leaves", None, 0.45, 0.0)
    bark = mat("tex_bark_kapok", None, 0.85, 0.0)
    wood = Mesh()
    centre = Vector((0, 0, -height * 0.6))
    cards = []
    # Sub-clumps, so the mass has a lumpy, cauliflower top rather than a smooth dome.
    lumps = [Vector((0, 0, 0))] + [heading(rng.uniform(0, math.tau)) * radius * rng.uniform(0.35, 0.75)
                                   + Vector((0, 0, rng.uniform(-0.4, 0.3) * height)) for _ in range(6)]
    for i in range(count):
        lump = lumps[rng.randrange(len(lumps))]
        off = rand_dir(rng) * rng.uniform(0.2, 1.0)
        base = lump + Vector((off.x * radius * 0.55, off.y * radius * 0.55, off.z * height * 0.5))
        out = base - centre
        out = out.normalized() if out.length > 1e-3 else UP
        up = (out * rng.uniform(0.5, 1.0) + rand_dir(rng) * 0.6).normalized()
        size = rng.uniform(1.6, 2.6) * radius / 4.0
        cards.append((base - up * size * 0.25, up, perpendicular(up, rng), size, soft_normal(centre, 0.5)))
    # Twigs from below up into the lumps (seen from the forest floor through the gaps).
    for lump in lumps[1:]:
        foot = Vector((rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), -height * 1.2))
        twig = bezier(foot, (foot + lump) / 2 + Vector((0, 0, height * 0.3)), lump - Vector((0, 0, height * 0.2)), 5)
        wood.tube(twig, [0.12 * radius / 4.0 * (1 - 0.6 * i / 5) for i in range(6)], 6, KAPOK_TILE)
    objs = [wood.object(name + "_wood", bark), cards_object(name + "_leaves", cards, leaf)]
    print(f"[jungle] {name}: {len(cards)} leaf cards", flush=True)
    return objs, {"TrunkRadius": 0.0, "Height": height, "Fins": [], "Note": "A canopy mass: no collision."}


# ============================================================================================ undergrowth
NO_COLLISION = {"TrunkRadius": 0.0, "Height": 0.0, "Fins": [], "Note": "Undergrowth: no collision (you push through it)."}


def leaf_strip(mesh, base, d, length, width, rise, droop, fold=0.25, twist=0.0, segs=8):
    """A leaf atlas (its base at the bottom centre of the card, the tip at the top) bent onto a strip: leaving base
    heading d (horizontal) at rise radians above it, sagging by droop x length towards the tip, its two halves folded
    up by fold (a shallow V along the midrib) and turned by twist radians about the midrib by the tip."""
    d = Vector((d.x, d.y, 0)).normalized()
    pts = []
    for i in range(segs + 1):
        t = i / segs
        pts.append(base + d * (length * t * math.cos(rise)) + UP * (length * t * math.sin(rise) - droop * length * t * t))
    side0 = d.cross(UP).normalized()
    rows = []
    for i, p in enumerate(pts):
        t = i / segs
        tangent = (pts[min(i + 1, segs)] - pts[max(i - 1, 0)]).normalized()
        side = (side0 - tangent * side0.dot(tangent)).normalized()
        normal = side.cross(tangent).normalized()
        a = twist * t
        side, normal = side * math.cos(a) + normal * math.sin(a), normal * math.cos(a) - side * math.sin(a)
        h = width / 2
        rows.append([mesh.bm.verts.new(p - side * h + normal * fold * h), mesh.bm.verts.new(p), mesh.bm.verts.new(p + side * h + normal * fold * h)])
    for i in range(segs):
        v0, v1 = i / segs, (i + 1) / segs
        mesh.face((rows[i][0], rows[i][1], rows[i + 1][1], rows[i + 1][0]), [(0, v0), (0.5, v0), (0.5, v1), (0, v1)])
        mesh.face((rows[i][1], rows[i][2], rows[i + 1][2], rows[i + 1][1]), [(0.5, v0), (1, v0), (1, v1), (0.5, v1)])
    return pts


def fern(name, seed, fronds):
    """A ground fern: a rosette of fronds about a metre long arching out from a low crown (the game scales it)."""
    rng = random.Random(seed)
    blades = Mesh()
    for k in range(fronds):
        a = math.tau * k / fronds + rng.uniform(-0.25, 0.25)
        length = rng.uniform(0.8, 1.1)
        leaf_strip(blades, Vector((0, 0, 0.04)) + heading(a) * 0.05, heading(a), length, length * 0.95,
                   math.radians(rng.uniform(45, 75)), rng.uniform(0.55, 0.95), fold=0.12, twist=rng.uniform(-0.5, 0.5), segs=7)
    # The young fronds in the middle, still upright.
    for k in range(3):
        a = rng.uniform(0, math.tau)
        leaf_strip(blades, Vector((0, 0, 0.04)), heading(a), 0.55, 0.45, math.radians(80), 0.15, fold=0.2, segs=4)
    print(f"[jungle] {name}: {fronds + 3} fronds", flush=True)
    return [blades.object(name + "_blades", mat("leaf_fern_frond", None, 0.5, 0.0))], NO_COLLISION


def banana(name, seed, stem_height, leaves):
    """A banana: a soft pseudostem of leaf sheaths, a crown of huge paddle leaves on short petioles arching out and
    drooping, torn along their veins, and a young leaf still rolled up in the middle."""
    rng = random.Random(seed)
    stem = Mesh()
    blades = Mesh()
    top = Vector((rng.uniform(-0.1, 0.1), rng.uniform(-0.1, 0.1), stem_height))
    pts = [Vector((0, 0, -0.1)).lerp(top, i / 6) for i in range(7)]
    stem.tube(pts, [0.13 - 0.04 * i / 6 for i in range(7)], 10, PALM_TILE, cap=False)
    for k in range(leaves):
        a = math.tau * k / leaves + rng.uniform(-0.3, 0.3)
        d = heading(a)
        rise = math.radians(rng.uniform(40, 70))
        petiole_end = top + d * 0.25 * math.cos(rise) + UP * (0.35 * math.sin(rise))
        stem.tube([top - UP * 0.15, (top + petiole_end) / 2 + UP * 0.05, petiole_end], [0.04, 0.035, 0.03], 6, PALM_TILE)
        length = rng.uniform(1.9, 2.5)
        leaf_strip(blades, petiole_end, d, length, length * rng.uniform(0.26, 0.32), rise * rng.uniform(0.5, 0.8),
                   rng.uniform(0.35, 0.7), fold=0.2, twist=rng.uniform(-0.6, 0.6), segs=10)
    # The rolled young leaf (a pale green cigar) standing out of the top.
    stem.tube([top, top + UP * 0.6 + Vector((0.05, 0, 0))], [0.05, 0.02], 6, PALM_TILE)
    print(f"[jungle] {name}: {leaves} leaves", flush=True)
    return [stem.object(name + "_stem", mat("tex_bark_palm", None, 0.6, 0.0)),
            blades.object(name + "_blades", mat("leaf_banana_leaf", None, 0.4, 0.0))], NO_COLLISION


def frond_cards(cards, start, d, length, rise, centre, rng, size=1.15, step=0.1):
    """A palm frond: a rachis arching out from start along heading d at rise radians, drooping more towards its tip,
    and palm_frond cards chained along it lying flat (rolled a little). Returns the rachis points."""
    pts = [start]
    direction = (d * math.cos(rise) + UP * math.sin(rise)).normalized()
    for i in range(int(length / step)):
        direction = (direction - UP * 0.028 * (1 + i * 0.02)).normalized()
        pts.append(pts[-1] + direction * step)
    i = 4
    twist = rng.uniform(-0.4, 0.4)
    while i < len(pts) - 2:
        j = min(i + int(size / step), len(pts) - 1)
        up = (pts[j] - pts[i]).normalized()
        side = up.cross(UP)
        side = side.normalized() if side.length > 1e-3 else perpendicular(up, rng)
        side = (side * math.cos(twist) + up.cross(side) * math.sin(twist)).normalized()
        cards.append((pts[i], up, side, max((pts[j] - pts[i]).length, 0.4), soft_normal(centre, 0.6)))
        i = j
    return pts


def ground_palm(name, seed, fronds):
    """A stemless palm: a great fan of feathery fronds (about 3.5 m) rising straight out of the forest floor."""
    rng = random.Random(seed)
    stem = Mesh()
    cards = []
    for k in range(fronds):
        a = math.tau * k / fronds + rng.uniform(-0.3, 0.3)
        pts = frond_cards(cards, Vector((0, 0, 0.05)), heading(a), rng.uniform(3.0, 3.8), math.radians(rng.uniform(55, 78)),
                          Vector((0, 0, -1.0)), rng)
        stem.tube(pts[:6], [0.035, 0.032, 0.03, 0.028, 0.026, 0.024], 5, PALM_TILE)
    print(f"[jungle] {name}: {fronds} fronds, {len(cards)} frond cards", flush=True)
    return [stem.object(name + "_stem", mat("tex_bark_palm", None, 0.6, 0.0)),
            cards_object(name + "_fronds", cards, mat("leaf_palm_frond", None, 0.45, 0.0))], NO_COLLISION


def sapling(name, seed, leaves):
    """A broad-leaved sapling about 1.6 m tall: a thin, slightly crooked stem holding big glossy leaves out on short
    petioles, alternately, smaller towards the top."""
    rng = random.Random(seed)
    stem = Mesh()
    blades = Mesh()
    top = Vector((rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), 1.6))
    pts = bezier(Vector((0, 0, -0.05)), Vector((rng.uniform(-0.1, 0.1), rng.uniform(-0.1, 0.1), 0.8)), top, 8)
    stem.tube(pts, [0.016 - 0.01 * i / 8 for i in range(9)], 5, PALM_TILE)
    a = rng.uniform(0, math.tau)
    for k in range(leaves):
        t = 0.3 + 0.7 * k / max(leaves - 1, 1)
        at = pts[min(8, int(t * 8))]
        a += math.radians(137.5)   # spiral phyllotaxis
        d = heading(a)
        size = rng.uniform(0.38, 0.52) * (1.0 - 0.35 * t)
        petiole = at + d * 0.07 + UP * 0.03
        stem.tube([at, petiole], [0.006, 0.004], 4, PALM_TILE)
        leaf_strip(blades, petiole, d, size, size * 0.7, math.radians(rng.uniform(5, 30)), rng.uniform(0.15, 0.4),
                   fold=0.18, twist=rng.uniform(-0.4, 0.4), segs=5)
    print(f"[jungle] {name}: {leaves} leaves", flush=True)
    return [stem.object(name + "_stem", mat("tex_bark_palm", None, 0.6, 0.0)),
            blades.object(name + "_blades", mat("leaf_big_leaf", None, 0.35, 0.0))], NO_COLLISION


def shrub(name, seed, cards_count, big_leaves):
    """A rainforest shrub about a metre tall and as wide: a rounded, lumpy mass of broadleaf cards, with a few big
    single leaves sticking out of it."""
    rng = random.Random(seed)
    centre = Vector((0, 0, 0.35))
    cards = []
    for i in range(cards_count):
        off = rand_dir(rng) * rng.uniform(0.15, 1.0)
        base = Vector((off.x * 0.55, off.y * 0.55, 0.45 + off.z * 0.4))
        out = (base - centre).normalized()
        up = (out * rng.uniform(0.5, 1.0) + rand_dir(rng) * 0.5 + UP * 0.3).normalized()
        size = rng.uniform(0.55, 0.85)
        cards.append((base - up * size * 0.3, up, perpendicular(up, rng), size, soft_normal(centre, 0.4)))
    blades = Mesh()
    for k in range(big_leaves):
        a = rng.uniform(0, math.tau)
        at = Vector((math.cos(a) * 0.3, math.sin(a) * 0.3, rng.uniform(0.2, 0.7)))
        size = rng.uniform(0.35, 0.5)
        leaf_strip(blades, at, heading(a), size, size * 0.7, math.radians(rng.uniform(5, 35)), 0.3, fold=0.18, segs=4)
    print(f"[jungle] {name}: {cards_count} cards, {big_leaves} big leaves", flush=True)
    return [blades.object(name + "_blades", mat("leaf_big_leaf", None, 0.35, 0.0)),
            cards_object(name + "_leaves", cards, mat("leaf_jungle_leaves", None, 0.45, 0.0))], NO_COLLISION


# ============================================================================================ build
BUILDERS = {
    "jungle_giant_a": lambda: rainforest_tree("giant", 101, radius=1.2, height=30.0, fins=7, fin_rise=3.4, fin_reach=3.4,
                                              stranglers=6, limbs=6, crown=9.0, lianas=3),
    "jungle_giant_b": lambda: rainforest_tree("fig", 202, radius=1.3, height=32.0, fins=9, fin_rise=2.9, fin_reach=3.2,
                                              stranglers=16, limbs=5, crown=10.0, lianas=4),
    "jungle_tree_a": lambda: rainforest_tree("tree", 303, radius=0.45, height=20.0, fins=4, fin_rise=3.0, fin_reach=4.0,
                                             stranglers=3, limbs=4, crown=5.0, lianas=1),
    "jungle_tree_b": lambda: rainforest_tree("tree", 404, radius=0.38, height=17.0, fins=3, fin_rise=2.5, fin_reach=3.2,
                                             stranglers=2, limbs=4, crown=4.2, lianas=1),
    "walking_palm_a": lambda: walking_palm("palm", 505, height=14.0, root_top=2.0, spread=1.3, roots=14, fronds=9),
    "walking_palm_b": lambda: walking_palm("palm", 606, height=11.0, root_top=1.5, spread=1.0, roots=11, fronds=8),
    "jungle_canopy": lambda: canopy_clump("canopy", 707, radius=4.0, height=1.8, count=95),
    "jungle_crown_small": lambda: canopy_clump("crown", 808, radius=4.0, height=1.8, count=34),
    # Undergrowth (scaled by the game): ferns ~1 m fronds, bananas a 2.4 m stem, the ground palm ~3.5 m fronds,
    # saplings 1.6 m tall, shrubs ~1 m.
    "jungle_fern_a": lambda: fern("fern", 911, fronds=9),
    "jungle_fern_b": lambda: fern("fern", 912, fronds=12),
    "jungle_banana_a": lambda: banana("banana", 921, stem_height=2.4, leaves=7),
    "jungle_banana_b": lambda: banana("banana", 922, stem_height=2.4, leaves=6),
    "jungle_ground_palm": lambda: ground_palm("gpalm", 931, fronds=9),
    "jungle_sapling_a": lambda: sapling("sapling", 941, leaves=9),
    "jungle_sapling_b": lambda: sapling("sapling", 942, leaves=7),
    "jungle_shrub_a": lambda: shrub("shrub", 951, cards_count=24, big_leaves=5),
    "jungle_shrub_b": lambda: shrub("shrub", 952, cards_count=20, big_leaves=7),
}


def build(name):
    K.reset()
    objs, points = BUILDERS[name]()
    rest = [o for o in objs if not (o.name.endswith("_leaves") or o.name.endswith("_fronds"))]
    cards = [o for o in objs if o not in rest]
    rest = K.finish(rest)
    objs = rest + cards
    K.export(name, objs)
    with open(os.path.join(K.OUT, name + "_points.json"), "w") as f:
        json.dump(points, f, indent=1)
    try:
        K.preview(name, objs)
        if points["TrunkRadius"] <= 0:
            return
        # A close-up of the foot (buttresses, stilt roots), framed by a hidden box round it.
        r = 2.2 if name.startswith("walking") else points["TrunkRadius"] * 5.0
        bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, r * 0.4))
        frame = bpy.context.active_object
        frame.data.transform(Matrix.Diagonal((r * 2, r * 2, r * 0.8, 1)))
        frame.data.update()
        bpy.context.view_layer.update()
        frame.hide_render = True
        K.preview(name + "_base", [frame])
    except Exception as e:
        print(f"[jungle] preview of {name} failed: {e}", flush=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for name in argv or list(BUILDERS):
        try:
            build(name)
        except Exception:
            import traceback
            traceback.print_exc()
            print(f"[jungle] FAILED {name}", flush=True)
    print("[jungle] done", flush=True)


main()
