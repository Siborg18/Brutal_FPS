"""The body as numpy arrays, in the rest pose: the source every garment is carved from and measured against."""
import heapq

import bpy
import numpy as np
from mathutils.bvhtree import BVHTree

from humankit import anim as AN, regions as RG


def log(*a):
    print("[clothkit]", *a, flush=True)


class BodyData:
    """Skin triangles (the eyeballs are left out) with positions, smooth normals, per-corner UVs, dense weights and the
    `_REGION` label of every vertex. Vertex indices are the body mesh's own."""

    def __init__(self, body, rig):
        self.obj, self.rig = body, rig
        if rig.animation_data:
            rig.animation_data.action = None
        AN.Poser(rig).reset()
        bpy.context.scene.frame_set(1)
        if RG.ATTR not in body.data.attributes:
            RG.assign(body, rig)
        self.labels = RG.labels(body)
        ev = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
        me = ev.to_mesh()
        n = len(me.vertices)
        self.co = np.zeros((n, 3))
        me.vertices.foreach_get("co", self.co.reshape(-1))
        self.normals = np.zeros((n, 3))
        me.vertex_normals.foreach_get("vector", self.normals.reshape(-1))
        lt = me.loop_triangles
        tv = np.zeros((len(lt), 3), np.int32)
        tl = np.zeros((len(lt), 3), np.int32)
        tp = np.zeros(len(lt), np.int32)
        lt.foreach_get("vertices", tv.reshape(-1))
        lt.foreach_get("loops", tl.reshape(-1))
        lt.foreach_get("polygon_index", tp)
        pm = np.zeros(len(me.polygons), np.int32)
        me.polygons.foreach_get("material_index", pm)
        uv = np.zeros((len(me.loops), 2))
        me.uv_layers.active.data.foreach_get("uv", uv.reshape(-1))
        skin = pm[tp] == 0
        self.tris, self.uv = tv[skin], uv[tl[skin]]
        ev.to_mesh_clear()
        self.names = [g.name for g in body.vertex_groups]
        self.W = np.zeros((n, len(self.names)))
        for v in body.data.vertices:
            for g in v.groups:
                self.W[v.index, g.group] = g.weight
        dom = np.array(self.names)[self.W.argmax(axis=1)]
        self.side = np.array(["L" if d.endswith(".L") else "R" if d.endswith(".R") else "C" for d in dom])
        e = np.sort(np.concatenate([self.tris[:, [0, 1]], self.tris[:, [1, 2]], self.tris[:, [2, 0]]]), axis=1)
        self.edges = np.unique(e, axis=0)
        self.elen = np.linalg.norm(self.co[self.edges[:, 0]] - self.co[self.edges[:, 1]], axis=1)
        self.adj = [[] for _ in range(n)]
        for (a, b), L in zip(self.edges.tolist(), self.elen.tolist()):
            self.adj[a].append((b, L))
            self.adj[b].append((a, L))
        self.bvh = BVHTree.FromPolygons([tuple(c) for c in self.co], [tuple(t) for t in self.tris.tolist()])
        self.bones = {b.name: (np.array(b.head_local), np.array(b.tail_local)) for b in rig.data.bones}

    def envelope(self, iters=60, lam=0.5, thresh=0.0003):
        """The skin with its creases filled (only concave vertices move, outwards): the surface shells are offset from, so that
        an offset along the normals cannot fold over in the armpit or the crotch. Cached."""
        if getattr(self, "_env", None) is None:
            n = len(self.co)
            a, b = self.edges[:, 0], self.edges[:, 1]
            cnt = np.zeros(n)
            np.add.at(cnt, a, 1)
            np.add.at(cnt, b, 1)
            P = self.co.copy()
            for _ in range(iters):
                acc = np.zeros_like(P)
                np.add.at(acc, a, P[b])
                np.add.at(acc, b, P[a])
                avg = acc / np.maximum(cnt, 1)[:, None]
                m = (((avg - P) * self.normals).sum(axis=1) > thresh) & (cnt > 0)
                P[m] += lam * (avg[m] - P[m])
            self._env = P
            self._env_bvh = BVHTree.FromPolygons([tuple(c) for c in P], [tuple(t) for t in self.tris.tolist()])
            moved = np.linalg.norm(P - self.co, axis=1)
            log(f"envelope: {int((moved > 1e-4).sum())} vertices moved, up to {1000 * moved.max():.1f} mm")
        return self._env, self._env_bvh

    def envelope_normals(self):
        env, _ = self.envelope()
        a, b, c = (env[self.tris[:, i]] for i in range(3))
        fn = np.cross(b - a, c - a)
        n = np.zeros_like(env)
        for i in range(3):
            np.add.at(n, self.tris[:, i], fn)
        return n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)

    # ---------------------------------------------------------------- queries
    def bone(self, name):
        return self.bones[name]

    def region_mask(self, names, side=None):
        m = np.isin(self.labels, [RG.INDEX[r] for r in names])
        if side:
            m &= self.side == side
        return m

    def border_points(self, near, far, side=None):
        """Positions of the vertices in `near` that share an edge with a vertex in `far`."""
        a, b = self.region_mask(near, side), self.region_mask(far)
        e0, e1 = self.edges[:, 0], self.edges[:, 1]
        hit = np.concatenate([e0[a[e0] & b[e1]], e1[a[e1] & b[e0]]])
        return self.co[np.unique(hit)]

    def geodesic(self, sources, cutoff=np.inf, adj=None):
        """Distance over the mesh edges from the nearest of `sources`; inf beyond `cutoff`."""
        adj = self.adj if adj is None else adj
        dist = np.full(len(adj), np.inf)
        h = []
        for s in sources:
            dist[s] = 0.0
            h.append((0.0, int(s)))
        heapq.heapify(h)
        while h:
            d, u = heapq.heappop(h)
            if d > dist[u]:
                continue
            for v, L in adj[u]:
                nd = d + L
                if nd < dist[v] and nd <= cutoff:
                    dist[v] = nd
                    heapq.heappush(h, (nd, v))
        return dist

    def nearest(self, pts, envelope=False):
        """(location, interpolated unit normal, distance, outside) of the closest body surface point to each of pts;
        `outside` is False for a point that is under the surface (on the inner side of the face normal)."""
        co, bvh = (self.co, self.bvh)
        if envelope:
            co, bvh = self.envelope()
        loc = np.zeros((len(pts), 3))
        nor = np.zeros((len(pts), 3))
        dist = np.zeros(len(pts))
        out = np.ones(len(pts), bool)
        for i, p in enumerate(pts):
            l, _, ti, d = bvh.find_nearest(tuple(p))
            l = np.array(l)
            loc[i], dist[i] = l, d
            t = self.tris[ti]
            a, b, c = co[t]
            v0, v1, v2 = b - a, c - a, l - a
            d00, d01, d11, d20, d21 = v0 @ v0, v0 @ v1, v1 @ v1, v2 @ v0, v2 @ v1
            den = d00 * d11 - d01 * d01
            w1 = (d11 * d20 - d01 * d21) / den if abs(den) > 1e-18 else 0.0
            w2 = (d00 * d21 - d01 * d20) / den if abs(den) > 1e-18 else 0.0
            nn = self.normals[t[0]] * (1 - w1 - w2) + self.normals[t[1]] * w1 + self.normals[t[2]] * w2
            nor[i] = nn / max(np.linalg.norm(nn), 1e-12)
            fn = np.cross(b - a, c - a)
            out[i] = (p - l) @ fn >= 0
        return loc, nor, dist, out
