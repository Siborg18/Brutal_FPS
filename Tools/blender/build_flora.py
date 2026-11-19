"""
Builds the outback flora from leaf cards and exports Assets/Models/<name>.glb, with previews in
Tools/previews/models:

  saltbush_a, saltbush_b    old-man saltbush: a dome of silvery sprig cards over woody stems, about 1 m across
                            (the game scales and turns each one)
  gum_tree_a, _b, _c        eucalypts: smooth orange-tan trunks and crooked limbs (a branching generator, own bark
                            UVs), drooping grey-green foliage hung in clumps at the branch tips

    blender -b --factory-startup -P Tools/blender/build_flora.py -- [names...]

The cards use the leaf atlases from bake_leaves.py (leaf_<atlas> materials: alpha-tested, two-sided in the game).
Every card carries soft normals pointing out of its clump and the whole crown, so a bush or a crown shades as one
rounded mass instead of a pile of flat planes.
"""
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


# ============================================================================================ leaf cards
def cards_object(name, cards, material):
    """cards: [(base, up, side, size, normal_fn)]: a quad from base (bottom centre of the atlas) up `up` by size,
    size wide along `side`. normal_fn(point) gives the soft normal at a corner."""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    normals = []
    for base, up, side, size, nfn in cards:
        h = side * (size / 2)
        corners = [base - h, base + h, base + h + up * size, base - h + up * size]
        uvs = [(0, 0), (1, 0), (1, 1), (0, 1)]
        vs = [bm.verts.new(c) for c in corners]
        f = bm.faces.new(vs)
        for loop, uv in zip(f.loops, uvs):
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


def soft_normal(*centres_weights, up_bias=0.2):
    def fn(p):
        n = Vector()
        for c, w in centres_weights:
            d = p - c
            if d.length > 1e-6:
                n += d.normalized() * w
        n += UP * up_bias
        return tuple(n.normalized())
    return fn


# ============================================================================================ saltbush
def saltbush(seed, radius=0.5, height=0.45, count=150, stems=5):
    """A rounded dome of sprig cards: bases inside the dome, each sprig reaching outwards and upwards to its skin."""
    rng = random.Random(seed)
    leaf = mat("leaf_saltbush_sprig", None, 0.6, 0.0)
    centre = Vector((0, 0, height * 0.85))
    ncentre = Vector((0, 0, height * 0.25))
    cards = []
    for i in range(count):
        az = rng.random() * math.tau
        if i < count // 4:
            # A skirt of sprigs low down, reaching out and down to the ground, so the bush is dense to its foot.
            el = math.radians(rng.uniform(-35, 5))
        else:
            el = math.asin(min(1.0, -0.15 + 1.15 * rng.random() ** 0.75))
        d = Vector((math.cos(el) * math.cos(az), math.sin(el) * 0 + math.cos(el) * math.sin(az), math.sin(el)))
        rin = rng.uniform(0.08, 0.55)
        base = Vector((d.x * radius * rin, d.y * radius * rin, centre.z + d.z * height * rin))
        base.z = max(base.z, 0.03)
        up = (d + UP * 0.35 + rand_dir(rng) * 0.3).normalized()
        size = rng.uniform(0.30, 0.42) * (0.8 + 0.4 * (1 - rin))
        side = perpendicular(up, rng)
        cards.append((base, up, side, size, soft_normal((ncentre, 1.0), up_bias=0.25)))
    # Fill the inside low down: sprigs from near the root crown reaching outwards in every direction.
    for i in range(count // 6):
        az = rng.random() * math.tau
        base = Vector((math.cos(az) * rng.uniform(0.0, 0.12), math.sin(az) * rng.uniform(0.0, 0.12), rng.uniform(0.02, height * 0.35)))
        el = math.radians(rng.uniform(-5, 35))
        up = Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el)))
        side = perpendicular(up, rng)
        cards.append((base, up, side, rng.uniform(0.32, 0.42), soft_normal((ncentre, 1.0), up_bias=0.25)))
    objs = [cards_object("saltbush_leaves", cards, leaf)]
    # Woody stems from the crown base, seen through the gaps.
    wood = mat("flat_saltbush_wood", (0.36, 0.33, 0.27), 0.85)
    for i in range(stems):
        az = math.tau * i / stems + rng.uniform(-0.4, 0.4)
        tip = Vector((math.cos(az) * radius * 0.35, math.sin(az) * radius * 0.35, height * rng.uniform(0.7, 1.0)))
        mid = Vector((tip.x * 0.35, tip.y * 0.35, tip.z * 0.4))
        objs.append(K.tube("stem", [(0, 0, 0.0), tuple(mid), tuple(tip)], 0.012, wood, verts=5))
    return objs


# ============================================================================================ gum trees
def branch_mesh(bm, uvl, pts, radii, sides, tile=1.2, flare=None):
    """A tube through pts with the given radii; bark UVs in texture repeats (u round, v along); capped tip."""
    n = len(pts)
    tangents = []
    for i in range(n):
        a = pts[max(i - 1, 0)]
        b = pts[min(i + 1, n - 1)]
        tangents.append((b - a).normalized())
    # Parallel-transport frames.
    t0 = tangents[0]
    x = perpendicular(t0, random.Random(1)) if abs(t0.z) > 0.99 else t0.cross(UP).normalized()
    frames = []
    for i in range(n):
        t = tangents[i]
        x = (x - t * x.dot(t)).normalized()
        frames.append((x, t.cross(x)))
    circ = math.tau * sum(radii) / len(radii)
    urep = max(1, round(circ / tile))
    rings = []
    vcoord = [0.0]
    for i in range(1, n):
        vcoord.append(vcoord[-1] + (pts[i] - pts[i - 1]).length)
    for i in range(n):
        x, y = frames[i]
        ring = []
        for k in range(sides):
            a = math.tau * k / sides
            r = radii[i]
            if flare is not None:
                r *= flare(pts[i].z, a)
            ring.append(bm.verts.new(pts[i] + (x * math.cos(a) + y * math.sin(a)) * r))
        rings.append(ring)
    for i in range(n - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            f = bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]))
            for loop, (ii, kk) in zip(f.loops, ((i, k), (i, k + 1), (i + 1, k + 1), (i + 1, k))):
                loop[uvl].uv = (urep * kk / sides, vcoord[ii] / tile)
    tip = bm.verts.new(pts[-1] + tangents[-1] * radii[-1] * 0.8)
    for k in range(sides):
        k2 = (k + 1) % sides
        f = bm.faces.new((rings[-1][k], rings[-1][k2], tip))
        for loop, uv in zip(f.loops, ((urep * k / sides, vcoord[-1] / tile), (urep * (k + 1) / sides, vcoord[-1] / tile),
                                      (urep * (k + 0.5) / sides, (vcoord[-1] + radii[-1]) / tile))):
            loop[uvl].uv = uv


class Tree:
    def __init__(self, seed, height_scale=1.0, spread=1.0, trunks=1, fork_height=3.4):
        self.rng = random.Random(seed)
        self.hs = height_scale
        self.spread = spread
        self.trunks = trunks
        self.fork = fork_height
        self.branches = []      # (pts, radii, sides, flare)
        self.tips = []          # (point, direction, depth)

    def grow(self, base, d, length, radius, depth, max_depth):
        rng = self.rng
        segs = {0: 9, 1: 7, 2: 5}.get(depth, 4)
        sides = {0: 14, 1: 9, 2: 7}.get(depth, 5)
        gnarl = {0: 0.10, 1: 0.22, 2: 0.30}.get(depth, 0.35)
        pts = [base]
        dirs = [d]
        for k in range(segs):
            d = (d + rand_dir(rng) * gnarl + UP * (0.10 if depth < 2 else -0.04)).normalized()
            pts.append(pts[-1] + d * (length / segs))
            dirs.append(d)
        taper = 0.62 if depth < max_depth else 0.35
        radii = [radius * (1 - (1 - taper) * (k / segs)) for k in range(segs + 1)]
        flare = None
        if depth == 0:
            rr = random.Random(rng.random())
            ph = [rr.uniform(0, math.tau) for _ in range(3)]

            def flare(z, a, ph=ph):
                k = max(0.0, 1.0 - z / 0.9)
                buttress = 0.16 * math.sin(4 * a + ph[0]) + 0.08 * math.sin(7 * a + ph[1])
                return 1.0 + k * k * (0.7 + buttress * 1.6) + 0.04 * math.sin(3 * a + ph[2] + z * 2)
        self.branches.append((pts, radii, sides, flare))
        if depth >= max_depth:
            self.tips.append((pts[-1], d, depth))
            self.tips.append((pts[len(pts) // 2], dirs[len(pts) // 2], depth + 1))
            return
        n_child = 3 if depth == 0 else rng.choice((2, 2, 3))
        for c in range(n_child):
            t = 1.0 if c == 0 else rng.uniform(0.5, 0.92)
            idx = min(int(round(t * segs)), segs)
            p = pts[idx]
            pd = dirs[idx]
            axis = perpendicular(pd, rng)
            ang = math.radians(rng.uniform(22, 38) if c == 0 else rng.uniform(32, 55)) * self.spread
            cd = (Matrix.Rotation(ang, 3, axis) @ pd + UP * 0.22).normalized()
            if depth == 0:
                # The main limbs fan out evenly round the trunk.
                az = math.tau * c / n_child + rng.uniform(-0.4, 0.4)
                cd = Vector((math.cos(az) * math.sin(ang * 1.2), math.sin(az) * math.sin(ang * 1.2), math.cos(ang * 1.2))).normalized()
            child_len = length * rng.uniform(0.62, 0.80) * (1.0 if depth else 0.95)
            self.grow(p, cd, child_len, radii[idx] * rng.uniform(0.62, 0.76), depth + 1, max_depth)

    def build(self, name, trunk_len, trunk_r, limb_len, max_depth=3):
        rng = self.rng
        for t in range(self.trunks):
            if self.trunks == 1:
                base, d = Vector((0, 0, 0)), (UP + rand_dir(rng) * 0.08).normalized()
            else:
                az = math.tau * t / self.trunks + rng.uniform(-0.3, 0.3)
                base = Vector((math.cos(az) * 0.12, math.sin(az) * 0.12, 0))
                d = (UP + Vector((math.cos(az), math.sin(az), 0)) * 0.35).normalized()
            pts = [base]
            dirs = [d]
            segs = 8
            for k in range(segs):
                d = (d + rand_dir(rng) * 0.06 + UP * 0.05).normalized()
                pts.append(pts[-1] + d * (trunk_len * self.hs / segs))
                dirs.append(d)
            r = trunk_r / math.sqrt(self.trunks)
            radii = [r * (1 - 0.30 * k / segs) for k in range(segs + 1)]
            rr = random.Random(rng.random())
            ph = [rr.uniform(0, math.tau) for _ in range(3)]

            def flare(z, a, ph=ph):
                k = max(0.0, 1.0 - z / 0.9)
                buttress = 0.16 * math.sin(4 * a + ph[0]) + 0.08 * math.sin(7 * a + ph[1])
                return 1.0 + k * k * (0.7 + buttress * 1.6) + 0.04 * math.sin(3 * a + ph[2] + z * 2)
            self.branches.append((pts, radii, 14, flare))
            # Limbs from the fork.
            n_limb = 3 if self.trunks == 1 else 2
            for c in range(n_limb):
                az = math.tau * (c + 0.5 * t) / n_limb + rng.uniform(-0.5, 0.5)
                ang = math.radians(rng.uniform(24, 40)) * self.spread
                cd = (Vector((math.cos(az) * math.sin(ang), math.sin(az) * math.sin(ang), math.cos(ang))) + d * 0.4).normalized()
                self.grow(pts[-1] - d * 0.05, cd, limb_len * self.hs * rng.uniform(0.85, 1.1), radii[-1] * rng.uniform(0.70, 0.82), 1, max_depth)
        bark = mat("tex_bark_gum", None, 0.75, 0.0)
        bm = bmesh.new()
        uvl = bm.loops.layers.uv.new("UVMap")
        for pts, radii, sides, flare in self.branches:
            branch_mesh(bm, uvl, pts, radii, sides, flare=flare)
        me = bpy.data.meshes.new(name + "_wood")
        bm.to_mesh(me)
        bm.free()
        wood = bpy.data.objects.new(name + "_wood", me)
        bpy.context.scene.collection.objects.link(wood)
        wood.data.materials.append(bark)
        wood["uv"] = "keep"
        wood["smooth"] = True
        wood["smooth_angle"] = 80.0
        # Foliage: drooping clumps of branchlet cards at the tips (and smaller ones part-way along the twigs).
        leaf = mat("leaf_gum_branchlet", None, 0.55, 0.0)
        tips = [tp for tp in self.tips]
        lo = min(p.z for p, _, _ in tips)
        hi = max(p.z for p, _, _ in tips)
        crown = sum((p for p, _, _ in tips), Vector()) / len(tips)
        crown.z = lo + (hi - lo) * 0.45
        cards = []
        for p, d, depth in tips:
            main = depth <= 3
            n = rng.randint(12, 16) if main else rng.randint(5, 8)
            cr = 0.95 if main else 0.55
            out = Vector((p.x - crown.x, p.y - crown.y, 0))
            out = out.normalized() if out.length > 1e-3 else rand_dir(rng)
            centre = p + out * 0.25 - UP * 0.25
            for i in range(n):
                off = rand_dir(rng) * rng.uniform(0.0, cr)
                off = Vector((off.x, off.y, off.z * 0.5))
                base = centre + off
                up = (out * rng.uniform(0.4, 1.0) + rand_dir(rng) * 0.6 + UP * rng.uniform(-0.1, 0.5)).normalized()
                size = rng.uniform(1.0, 1.4) * (1.0 if main else 0.8)
                side = perpendicular(up, rng)
                base -= up * size * 0.25
                cards.append((base, up, side, size, soft_normal((centre - UP * 0.3, 0.55), (crown - UP * 1.0, 0.45), up_bias=0.15)))
        leaves = cards_object(name + "_leaves", cards, leaf)
        print(f"[flora] {name}: {len(self.branches)} branches, {len(cards)} cards, height {hi:.1f} m", flush=True)
        return wood, leaves


def gum_tree(seed, **kw):
    build_kw = dict(trunk_len=kw.pop("trunk_len", 3.4), trunk_r=kw.pop("trunk_r", 0.30), limb_len=kw.pop("limb_len", 3.2),
                    max_depth=kw.pop("max_depth", 3))
    t = Tree(seed, **kw)
    return t.build("gum", **build_kw)


# ============================================================================================ build
def preview(name, objs):
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
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.cycles.transparent_max_bounces = 128     # many overlapping cards
    scene.view_settings.view_transform = 'AgX'
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("prev")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.65, 0.80, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
    sun = bpy.data.lights.new("sun", 'SUN'); sun.energy = 4.0; sun.angle = math.radians(1.5)
    so = bpy.data.objects.new("sun", sun); scene.collection.objects.link(so)
    so.rotation_euler = (math.radians(40), math.radians(10), math.radians(-30))
    lo = Vector((1e9, 1e9, 1e9)); hi = -lo
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
    centre = (lo + hi) / 2
    size = max((hi - lo).x, (hi - lo).y, (hi - lo).z)
    bpy.ops.mesh.primitive_plane_add(size=size * 8, location=(centre.x, centre.y, 0))
    ground = bpy.context.active_object
    ground.data.materials.append(mat("prev_ground", (0.62, 0.40, 0.28), 0.9))
    cam_data = bpy.data.cameras.new("cam"); cam_data.lens = 45
    cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam)
    scene.camera = cam
    d = Vector((0.55, -1.0, 0.18)).normalized()
    cam.location = centre + d * size * 1.9
    cam.rotation_euler = (centre - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.resolution_x, scene.render.resolution_y = 800, 800
    scene.render.filepath = os.path.join(K.PREVIEWS, name + ".png")
    bpy.ops.render.render(write_still=True)


BUILDERS = {
    "saltbush_a": lambda: saltbush(3, radius=0.5, height=0.45, count=150),
    "saltbush_b": lambda: saltbush(11, radius=0.56, height=0.32, count=130, stems=4),
    "gum_tree_a": lambda: gum_tree(21, trunk_len=3.6, trunk_r=0.32, limb_len=3.4),
    "gum_tree_b": lambda: gum_tree(34, trunks=2, spread=1.25, trunk_len=2.2, trunk_r=0.36, limb_len=3.0),
    "gum_tree_c": lambda: gum_tree(57, height_scale=0.75, trunk_len=2.6, trunk_r=0.20, limb_len=2.4),
}


def build(name):
    K.reset()
    result = BUILDERS[name]()
    objs = list(result)
    # Wood and stems get the usual finishing (UVs kept where authored); the cards keep their custom normals.
    rest = [o for o in objs if not o.name.endswith("_leaves") and o.name != "saltbush_leaves"]
    cards = [o for o in objs if o not in rest]
    rest = K.finish(rest)
    objs = rest + cards
    K.export(name, objs)
    try:
        preview(name, objs)
    except Exception as e:
        print(f"[flora] preview of {name} failed: {e}", flush=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for name in argv or list(BUILDERS):
        try:
            build(name)
        except Exception:
            import traceback
            traceback.print_exc()
            print(f"[flora] FAILED {name}", flush=True)
    print("[flora] done", flush=True)


main()
