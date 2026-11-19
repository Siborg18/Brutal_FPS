"""Turns the signed-distance body into the game mesh: regions at their own densities (body, head, hands, feet),
each quad-remeshed with QuadriFlow, trimmed at its seams, bridged together, mirrored for exact symmetry and moved
onto the smooth surface."""
import math
import time

import bmesh
import bpy
import numpy as np

from . import sdf


def log(*a):
    print("[human]", *a, flush=True)


def make_mesh(name, V, Q):
    me = bpy.data.meshes.new(name)
    me.vertices.add(len(V))
    me.vertices.foreach_set("co", np.asarray(V, np.float32).ravel())
    me.loops.add(len(Q) * 4)
    me.loops.foreach_set("vertex_index", np.asarray(Q, np.int32).ravel())
    me.polygons.add(len(Q))
    me.polygons.foreach_set("loop_start", np.arange(0, len(Q) * 4, 4, dtype=np.int32))
    me.update(calc_edges=True)
    me.validate()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def get_co(me):
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def set_co(me, co):
    me.vertices.foreach_set("co", np.asarray(co, np.float64).ravel())
    me.update()


class PlaneSeam:
    """A cut along the plane through p0 (normal n pointing into the extremity), limited to the ball (c, R)."""

    def __init__(self, p0, n, c, R):
        self.p0, self.n, self.c, self.R = np.asarray(p0, float), np.asarray(n, float), np.asarray(c, float), R
        self.anchor = self.p0

    def terms(self, Q):
        return -((Q - self.p0) @ self.n), np.linalg.norm(Q - self.c, axis=1) - self.R


class SkullSeam:
    """A cut that follows the head: the ear is everything more than tau outside the body without its ears."""

    def __init__(self, body, tau, c, R):
        self.body, self.tau, self.c, self.R = body, tau, np.asarray(c, float), R
        self.anchor = self.c

    def terms(self, Q):
        return self.tau - self.body.eval(Q, skip=("ear",)), np.linalg.norm(Q - self.c, axis=1) - self.R


class Region:
    """The body on one side of some seams. side=+1 keeps the extremity (grown `delta` past the cut), side=-1 keeps
    the rest (the extremities removed, minus `delta`). fold: evaluate the seams at |x| (each one cuts both sides of
    the symmetric body)."""

    def __init__(self, body, seams, side, delta, fold=False):
        self.body, self.seams, self.side, self.delta, self.fold = body, seams, side, delta, fold

    def _terms(self, P):
        Q = P.copy()
        if self.fold:
            Q[:, 0] = np.abs(Q[:, 0])
        return [s.terms(Q) for s in self.seams]

    def eval(self, P):
        d = self.body.eval(P)
        for e, b in self._terms(P):
            if self.side > 0:
                d = np.maximum(d, np.maximum(e - self.delta, b))
            else:
                d = np.maximum(d, -np.maximum(e + self.delta, b))
        return d

    def outside(self, P, margin):
        """True where a point is not at least `margin` clear of the seams on this region's side."""
        out = np.zeros(len(P), bool)
        for e, b in self._terms(P):
            if self.side > 0:
                out |= e > -margin
            else:
                out |= (e < margin) & (b < 0)
        return out


def quad_remesh(o, target_faces, symmetric, voxel):
    """Voxel remesh (manifold, no degenerate edges) then QuadriFlow. Works in centimetres: QuadriFlow's
    manifold check rejects any edge shorter than 1e-4 units."""
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    m = o.modifiers.new("vox", 'REMESH')
    m.mode = 'VOXEL'
    m.voxel_size = voxel
    m.adaptivity = 0.0
    bpy.ops.object.modifier_apply(modifier=m.name)
    me = o.data
    set_co(me, get_co(me) * 100.0)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.002)
    bmesh.ops.dissolve_degenerate(bm, dist=0.002, edges=bm.edges)
    bm.to_mesh(me)
    bm.free()
    me.use_mirror_x = symmetric
    t = time.time()
    r = bpy.ops.object.quadriflow_remesh(use_mesh_symmetry=symmetric, use_preserve_sharp=False,
                                         use_preserve_boundary=False, smooth_normals=False, mode='FACES',
                                         target_faces=int(target_faces), seed=0)
    me = o.data
    set_co(me, get_co(me) / 100.0)
    log(f"  quadriflow {o.name}: {r} {len(me.polygons)} faces in {time.time() - t:.1f}s")
    if 'FINISHED' not in r:
        raise RuntimeError("QuadriFlow failed on " + o.name)
    if symmetric:
        symmetrize(o)
    return o


def area_of(o):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    a = sum(f.calc_area() for f in bm.faces)
    bm.free()
    return a


class FaceWarp:
    """Magnifies the front of the head in x and z (by S where y < y1, easing to 1 at y0), so QuadriFlow puts
    S times more edges across the face than over the skull. Exactly invertible: y is unchanged."""

    def __init__(self, S=1.8, y0=-0.015, y1=-0.065, zc=1.64):
        self.S, self.y0, self.y1, self.zc = S, y0, y1, zc

    def s(self, y):
        t = np.clip((y - self.y0) / (self.y1 - self.y0), 0.0, 1.0)
        return 1.0 + (self.S - 1.0) * t * t * (3 - 2 * t)

    def fwd(self, P):
        s = self.s(P[:, 1])
        return np.stack([P[:, 0] * s, P[:, 1], (P[:, 2] - self.zc) * s + self.zc], axis=1)

    def inv(self, P):
        s = self.s(P[:, 1])
        return np.stack([P[:, 0] / s, P[:, 1], (P[:, 2] - self.zc) / s + self.zc], axis=1)


def build_region(name, field, lo, hi, h, edge, symmetric, warp=None):
    t = time.time()
    lo = np.asarray(lo, float)
    hi = np.asarray(hi, float)
    if symmetric:
        n = math.ceil(max(-lo[0], hi[0]) / h)
        lo[0], hi[0] = -n * h, n * h
    F, lo2 = sdf.sample(field, lo, hi, h)
    border = min(F[0].min(), F[-1].min(), F[:, 0].min(), F[:, -1].min(), F[:, :, 0].min(), F[:, :, -1].min())
    if border <= 0:
        raise RuntimeError(f"{name}: the surface reaches the sampling box; enlarge it")
    V, Q = sdf.surface_nets(F, lo2, h)
    if warp is not None:
        V = warp.fwd(V)
    o = make_mesh(name, V, Q)
    a = area_of(o)
    log(f"  {name}: nets {len(Q)} quads, area {a:.3f} m2, sampled in {time.time() - t:.1f}s")
    quad_remesh(o, a / (edge * edge), symmetric, voxel=h)
    if warp is not None:
        set_co(o.data, warp.inv(get_co(o.data)))
    return o


def trim(o, region, margin):
    """Deletes the faces that are not at least `margin` inside the region's cuts."""
    me = o.data
    bad = region.outside(get_co(me), margin)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    kill = [f for f in bm.faces if any(bad[v.index] for v in f.verts)]
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(me)
    bm.free()


def mirror_copy(o, name):
    c = o.copy()
    c.data = o.data.copy()
    c.name = name
    bpy.context.scene.collection.objects.link(c)
    me = c.data
    co = get_co(me)
    co[:, 0] *= -1
    set_co(me, co)
    me.flip_normals()
    return c


def boundary_loops(bm):
    """Closed boundary loops as lists of edges."""
    seen = set()
    loops = []
    for e in bm.edges:
        if e.is_boundary and e.index not in seen:
            loop = []
            cur = e
            v = e.verts[0]
            while cur is not None and cur.index not in seen:
                seen.add(cur.index)
                loop.append(cur)
                v = cur.other_vert(v)
                nxt = None
                for e2 in v.link_edges:
                    if e2.is_boundary and e2.index not in seen:
                        nxt = e2
                        break
                cur = nxt
            loops.append(loop)
    return loops


def join_objects(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.name = name
    return o


def bridge_seams(o, seams):
    """Bridges the two boundary loops at each seam. seams: {name: (p0, n)}; wrist/ankle seams are mirrored."""
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.edges.index_update()
    loops = boundary_loops(bm)
    small = [L for L in loops if len(L) <= 8]
    for L in small:
        bmesh.ops.holes_fill(bm, edges=L, sides=len(L))
        log(f"    filled a {len(L)}-edge hole at {np.round(np.mean([np.array(v.co) for e in L for v in e.verts], axis=0), 3)}")
    loops = [L for L in loops if len(L) > 8]
    groups = {}
    for L in loops:
        c = np.mean([np.array(v.co) for e in L for v in e.verts], axis=0)
        cf = c.copy()
        cf[0] = abs(cf[0])
        best = min(seams, key=lambda k: np.linalg.norm(cf - seams[k].anchor))
        side = 0 if abs(c[0]) < 0.02 else int(np.sign(c[0]))
        groups.setdefault((best, side), []).append(L)
        log(f"    loop {len(L)} edges at {np.round(c, 3)} -> {best} {side}")
    new_faces = []
    for key, Ls in groups.items():
        if len(Ls) != 2:
            raise RuntimeError(f"seam {key}: {len(Ls)} boundary loops")
        r = bmesh.ops.bridge_loops(bm, edges=Ls[0] + Ls[1])
        new_faces += r["faces"]
        log(f"  bridged {key}: loops of {len(Ls[0])} and {len(Ls[1])} edges")
    bmesh.ops.join_triangles(bm, faces=new_faces, angle_face_threshold=math.radians(60),
                             angle_shape_threshold=math.radians(60))
    bm.to_mesh(o.data)
    bm.free()


def symmetrize(o):
    """Keeps the +X half (bisected at x = 0) and mirrors it, merging the midline."""
    bm = bmesh.new()
    bm.from_mesh(o.data)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=(0, 0, 0), plane_no=(-1, 0, 0), clear_outer=True)
    for v in bm.verts:
        if abs(v.co.x) < 2e-5:
            v.co.x = 0.0
    bm.to_mesh(o.data)
    bm.free()
    m = o.modifiers.new("mirror", 'MIRROR')
    m.use_clip = True
    m.use_mirror_merge = True
    m.merge_threshold = 1e-4
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)


def neighbours(me):
    ev = np.empty(len(me.edges) * 2, np.int64)
    me.edges.foreach_get("vertices", ev)
    return ev.reshape(-1, 2)


def relax(o, body, mask, iters=8, lam=0.5):
    """Tangential Laplacian smoothing of the masked vertices, re-projected onto the surface each step."""
    me = o.data
    co = get_co(me)
    E = neighbours(me)
    n = len(co)
    for _ in range(iters):
        acc = np.zeros((n, 3))
        cnt = np.zeros(n)
        np.add.at(acc, E[:, 0], co[E[:, 1]])
        np.add.at(acc, E[:, 1], co[E[:, 0]])
        np.add.at(cnt, E[:, 0], 1)
        np.add.at(cnt, E[:, 1], 1)
        lap = acc / np.maximum(cnt, 1)[:, None] - co
        idx = np.where(mask)[0]
        g = sdf.gradient(body, co[idx])
        g /= np.maximum(np.linalg.norm(g, axis=1), 1e-9)[:, None]
        l = lap[idx]
        l -= g * (l * g).sum(axis=1)[:, None]
        co[idx] += lam * l
        co[idx] = sdf.project(body, co[idx], iters=3)
    set_co(me, co)


def project_all(o, body, iters=6):
    me = o.data
    co = get_co(me)
    t = time.time()
    co = sdf.project(body, co, iters=iters)
    res = np.abs(body.eval(co))
    set_co(me, co)
    log(f"  projected {len(co)} verts, residual max {res.max() * 1000:.3f} mm, mean {res.mean() * 1e6:.1f} um "
        f"({time.time() - t:.1f}s)")


# ------------------------------------------------------------------------------------------------ eyes
class EyeShape:
    """The eye opening on the eyeball (centre E, left eye; the right is its mirror): an almond in the plane across
    the gaze, a little tilted up at the outer corner."""

    def __init__(self, E, r_skin=0.0130, W=0.0150, Hu=0.0060, Hl=0.0046, tilt=0.07, a0=0.0006, out=0.10):
        self.E = np.asarray(E, float)
        self.g = sdf.nrm((out, -1.0, 0.0))                 # gaze (folded, left eye): forward, a touch outward
        self.l = sdf.nrm(np.cross((0, 0, 1.0), self.g)) * -1.0
        if self.l[0] < 0:
            self.l = -self.l
        self.u = np.cross(self.g, self.l)
        if self.u[2] < 0:
            self.u = -self.u
        self.r, self.W, self.Hu, self.Hl, self.tilt, self.a0 = r_skin, W, Hu, Hl, tilt, a0

    def local(self, P):
        s = np.where(P[:, 0] < 0, -1.0, 1.0)
        Q = P.copy()
        Q[:, 0] = np.abs(Q[:, 0])
        q = Q - self.E
        return q @ self.l, q @ self.u, q @ self.g, s

    def edges(self, t):
        return self.Hu * np.clip(1 - t * t, 0, 1) ** 0.72, -self.Hl * np.clip(1 - t * t, 0, 1) ** 0.85

    def inside(self, P, grow=0.0):
        a, b, f, _ = self.local(P)
        t = (a - self.a0) / (self.W + grow)
        top, bot = self.edges(t)
        bb = b - self.tilt * (a - self.a0)
        return (np.abs(t) < 1) & (bb < top + grow) & (bb > bot - grow) & (f > 0.004)

    def to_world(self, a, b, f, s):
        P = self.E + np.outer(a, self.l) + np.outer(b, self.u) + np.outer(f, self.g)
        P[:, 0] *= s
        return P

    def snap(self, P):
        """Moves points onto the rim of the opening (same lateral position), on the eyeball surface."""
        a, b, f, s = self.local(P)
        t = np.clip((a - self.a0) / self.W, -0.985, 0.985)
        top, bot = self.edges(t)
        upper = (b - self.tilt * (a - self.a0)) > 0.5 * (top + bot)
        a2 = self.a0 + t * self.W
        b2 = np.where(upper, top, bot) + self.tilt * (a2 - self.a0)
        f2 = np.sqrt(np.maximum(self.r ** 2 - a2 * a2 - b2 * b2, 1e-8))
        return self.to_world(a2, b2, f2, s)


def cut_eyes(o, shape):
    """Opens the eyes: deletes the skin over the eyeball inside the almond, snaps the rim onto the almond and
    turns it in behind the eyeball (the lid margin)."""
    me = o.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    co = np.array([v.co for v in bm.verts])
    ins = shape.inside(co)
    kill = [f for f in bm.faces if all(ins[v.index] for v in f.verts)]
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    rims = [L for L in boundary_loops(bm) if len(L) < 200]
    rims = [L for L in rims if shape.inside(np.array([np.array(v.co) for e in L for v in e.verts]), grow=0.004).all()]
    for L in rims:
        verts = list({v for e in L for v in e.verts})
        P = np.array([np.array(v.co) for v in verts])
        S = shape.snap(P)
        for v, p in zip(verts, S):
            v.co = p
        # two rings turned in: the lid margin, then tucked behind the eyeball
        edges = L
        for k, (rad, toward) in enumerate(((0.0122, 0.0006), (0.0110, 0.0012))):
            r = bmesh.ops.extrude_edge_only(bm, edges=edges)
            nv = [g for g in r["geom"] if isinstance(g, bmesh.types.BMVert)]
            for v in nv:
                p = np.array(v.co)
                a, b, f, s = shape.local(p[None])
                c = shape.to_world(np.array([shape.a0]), np.array([0.0]), np.array([0.0]), s)[0]
                q = p - shape.E * np.array([s[0], 1, 1])
                q = q / np.linalg.norm(q) * rad
                p2 = shape.E * np.array([s[0], 1, 1]) + q
                d = c - p2
                p2 = p2 + d / max(np.linalg.norm(d), 1e-9) * toward
                v.co = p2
            edges = [g for g in r["geom"] if isinstance(g, bmesh.types.BMEdge) and all(v in nv for v in g.verts)]
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    log(f"  opened {len(rims)} eyes, {len(kill)} faces removed")


def eyeball(name, E, gaze, r=0.0122, iris=0.0060, pupil=0.0021):
    """A UV sphere with its pole on the gaze: sclera, iris and pupil material slots, a slight corneal bulge."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=16, radius=r, calc_uvs=True)
    g = sdf.nrm(gaze)
    M = sdf.frame(g, (0, 0, 1))
    for v in bm.verts:
        p = np.array(v.co)                       # pole on +z
        z = p[2] / r
        ang = math.acos(np.clip(z, -1, 1))
        lift = 0.0009 * max(0.0, 1 - ang / math.asin(iris / r)) ** 2   # cornea dome
        p = p * (1 + lift / r)
        v.co = E + M @ p
    for f in bm.faces:
        c = np.array(f.calc_center_median()) - E
        ang = math.acos(np.clip(c @ g / np.linalg.norm(c), -1, 1))
        f.material_index = 2 if r * math.sin(ang) < pupil and c @ g > 0 else (
            1 if r * math.sin(ang) < iris and c @ g > 0 else 0)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    me.shade_smooth()
    return ob
