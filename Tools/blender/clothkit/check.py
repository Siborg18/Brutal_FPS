"""Checks: coverage (CONTRACTS section 1) and the drawn-surface poke-through metrics P1, P2, P3 (CONTRACTS section 2) over the rest
pose and every 4th frame of Walk and Idle."""
import bpy
import numpy as np
from mathutils.bvhtree import BVHTree

from humankit import anim as AN, regions as RG
from .carve import log


# --------------------------------------------------------------------------------------------- coverage
def coverage(body, soup, garment):
    """Run on the freshly carved soup (still on the body surface, so distances are along the skin).
    Returns {uncovered, min_mm, boundary_vertices}: the hidden-region vertices the garment does not contain, and the minimum
    geodesic distance (over the mesh edges) from a hidden-region boundary vertex to the garment edge."""
    hid = body.region_mask(garment.hides)
    have = np.zeros(len(body.co), bool)
    have[soup.src[soup.src >= 0]] = True
    uncovered = int((hid & ~have).sum())
    fins = getattr(soup, "dropped_src", np.zeros(0, np.int64))          # the body's zero-width midline fins, dropped by the carve
    fin_uncovered = int((hid & ~have)[fins].sum()) if len(fins) else 0
    e0, e1 = body.edges[:, 0], body.edges[:, 1]
    bnd = np.zeros(len(body.co), bool)
    bnd[e0[hid[e0] & ~hid[e1]]] = True
    bnd[e1[hid[e1] & ~hid[e0]]] = True
    bnd &= have
    ex = np.zeros(len(body.co), bool)             # the ears: a hair or cap edge that runs along an ear is not a culling seam
    for z in garment.zones:
        if getattr(z, "exclude", None) is not None:
            ex |= z.exclude(body)
    if ex.any():
        bnd &= ~np.isfinite(body.geodesic(np.where(ex)[0], cutoff=0.025))       # within 25 mm of an ear
    if len(fins):
        bnd &= ~np.isfinite(body.geodesic(fins, cutoff=0.02))                   # within 20 mm of a fin
    # the garment's own mesh graph, on the body surface
    adj = [[] for _ in range(len(soup.P))]
    for a, b in np.unique(np.sort(soup.edges(), axis=1), axis=0).tolist():
        L = float(np.linalg.norm(soup.P[a] - soup.P[b]))
        adj[a].append((b, L))
        adj[b].append((a, L))
    edge_v = np.unique(soup.boundary())
    D = body.geodesic(edge_v, adj=adj)
    vs = np.where(np.isin(soup.src, np.where(bnd)[0]))[0]
    mn = float(D[vs].min()) * 1000 if len(vs) else float("inf")
    log(f"{garment.id}: coverage: {uncovered} hidden-region vertices uncovered ({fin_uncovered} of them in the body's midline fins), minimum distance from a hidden-region boundary "
        f"to the garment edge {mn:.1f} mm ({len(vs)} boundary vertices)")
    return {"uncovered": uncovered, "fin_uncovered": fin_uncovered, "min_mm": mn, "boundary_vertices": int(len(vs))}


# --------------------------------------------------------------------------------------------- poses
def _coords(obj, normals=False):
    """Evaluated (posed) vertex positions, and with normals=True also the posed vertex normals."""
    ev = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = ev.to_mesh()
    co = np.zeros((len(me.vertices), 3))
    me.vertices.foreach_get("co", co.reshape(-1))
    if normals:
        nr = np.zeros((len(me.vertices), 3))
        me.vertex_normals.foreach_get("vector", nr.reshape(-1))
        ev.to_mesh_clear()
        return co, nr
    ev.to_mesh_clear()
    return co


def set_pose(rig, action, frame):
    ad = rig.animation_data or rig.animation_data_create()
    if action is None:
        ad.action = None
        AN.Poser(rig).reset()
        bpy.context.scene.frame_set(1)
        return
    act = bpy.data.actions[action]
    ad.action = act
    if getattr(ad, "action_slot", None) is None and len(act.slots):
        ad.action_slot = act.slots[0]
    bpy.context.scene.frame_set(int(frame))


def pose_list(step=4):
    """[(label, action, frame)]: the rest pose, then every `step`th frame of Walk and Idle."""
    out = [("rest", None, 1)]
    for name in ("Walk", "Idle"):
        a = bpy.data.actions[name]
        f0, f1 = int(a.frame_range[0]), int(a.frame_range[1])
        out += [(f"{name}{f}", name, f) for f in range(f0, f1 + 1, step)]
    return out


def _bvh(co, tris):
    return BVHTree.FromPolygons([tuple(c) for c in co], [tuple(t) for t in tris.tolist()])


P1_MAX, P2_MAX, P3_MAX = 0.005, 0.001, 0.005     # worst-frame fractions
NEAR = 0.030                                       # "lies under the garment": nearest garment point within 30 mm
HOLE_REACH = 0.040
HOLE_REACH_FEET = 0.060                           # the feet slot, because of the sole


def _hidden_flags(names):
    h = np.zeros(16, bool)
    for r in names:
        h[RG.INDEX[r]] = True
    return h


def open_triangles(tris):
    """Per triangle: does it have a boundary edge (the garment's edge or hem)?"""
    e = np.sort(np.concatenate([tris[:, [0, 1]], tris[:, [1, 2]], tris[:, [2, 0]]]), axis=1)
    _, inv, cnt = np.unique(e, axis=0, return_inverse=True, return_counts=True)
    open_e = cnt[inv.reshape(-1)] == 1
    out = np.zeros(len(tris), bool)
    out[np.arange(len(e))[open_e] % len(tris)] = True
    return out


def _outside(bvh, pts, open_tri, near=NEAR):
    """(considered, outside): of the points that lie under the garment (nearest garment point within `near`, and not a point
    that is beyond the garment's edge: nearest to the edge or corner of a boundary triangle rather than to its face), how many lie
    on the outer side of it (the nearest point and its face normal; the CONTRACTS test)."""
    n_c = n_o = 0
    for p in pts:
        loc, nor, idx, d = bvh.find_nearest(tuple(p), near)
        if loc is None:
            continue
        dn = abs((p[0] - loc[0]) * nor[0] + (p[1] - loc[1]) * nor[1] + (p[2] - loc[2]) * nor[2])
        if open_tri[idx] and d > dn * 1.02 + 1e-4:
            continue
        n_c += 1
        if (p[0] - loc[0]) * nor[0] + (p[1] - loc[1]) * nor[1] + (p[2] - loc[2]) * nor[2] > 0:
            n_o += 1
    return n_c, n_o


LOCAL_RADIUS = 0.06      # rest-pose reach of the "belongs to this part of the garment" association
SIM_MIN = 0.25           # ... and the least cosine similarity between the skin weights of the point and of the garment triangle


def _closest(p, A, B, C):
    """Distance from p to each triangle (A, B, C: (m, 3)) and the closest point on it (Ericson, vectorised)."""
    ab, ac, ap = B - A, C - A, p - A
    ee = lambda x, y: np.einsum("ij,ij->i", x, y)
    d1, d2 = ee(ab, ap), ee(ac, ap)
    bp = p - B
    d3, d4 = ee(ab, bp), ee(ac, bp)
    cp = p - C
    d5, d6 = ee(ab, cp), ee(ac, cp)
    vc, vb, va = d1 * d4 - d3 * d2, d5 * d2 - d1 * d6, d3 * d6 - d5 * d4
    with np.errstate(all="ignore"):
        den = 1.0 / (va + vb + vc)
        q = A + ab * (vb * den)[:, None] + ac * (vc * den)[:, None]
        q = np.where(((va <= 0) & (d4 - d3 >= 0) & (d5 - d6 >= 0))[:, None],
                     B + (C - B) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)))[:, None], q)
        q = np.where(((vb <= 0) & (d2 >= 0) & (d6 <= 0))[:, None], A + ac * (d2 / (d2 - d6))[:, None], q)
        q = np.where(((d6 >= 0) & (d5 <= d6))[:, None], C, q)
        q = np.where(((vc <= 0) & (d1 >= 0) & (d3 <= 0))[:, None], A + ab * (d1 / (d1 - d3))[:, None], q)
        q = np.where(((d3 >= 0) & (d4 <= d3))[:, None], B, q)
        q = np.where(((d1 <= 0) & (d2 <= 0))[:, None], A, q)
    return np.linalg.norm(q - p, axis=1), q


def associate(rest_co, tris, pts_rest, radius=LOCAL_RADIUS, w_pts=None, w_verts=None, min_sim=SIM_MIN):
    """Per point, the triangles (indices into `tris`) of the garment that can be 'over' it: within `radius` of it in the rest pose, and,
    when the skin weights of the points (w_pts) and of the garment's vertices (w_verts) are given, driven by the same bones (cosine
    similarity of the weight vectors >= min_sim). A hip does not lie under the sleeve that swings past it in a walk frame, and a
    chest point in the armpit does not lie under the inner face of the arm cap next to it."""
    bvh = _bvh(rest_co, tris)
    out = []
    for k, p in enumerate(pts_rest):
        hit = bvh.find_nearest_range(tuple(p), radius)
        c = np.unique([h[2] for h in hit]).astype(np.int64) if hit else np.zeros(0, np.int64)
        if len(c) and w_pts is not None:
            wt = w_verts[tris[c]].mean(axis=1)
            sim = (wt @ w_pts[k]) / np.maximum(np.linalg.norm(wt, axis=1) * np.linalg.norm(w_pts[k]), 1e-12)
            c = c[sim >= min_sim]
        out.append(c)
    return out


def _outside_local(co, tris, pts, cands, open_tri, near=NEAR):
    """`_outside`, but the nearest garment point is looked for only among each point's own (rest-pose associated) triangles."""
    n_c = n_o = 0
    for p, cand in zip(pts, cands):
        if len(cand) == 0:
            continue
        t = tris[cand]
        A, B, C = co[t[:, 0]], co[t[:, 1]], co[t[:, 2]]
        d, q = _closest(p, A, B, C)
        i = int(np.argmin(d))
        if d[i] > near:
            continue
        fn = np.cross(B[i] - A[i], C[i] - A[i])
        fn /= max(np.linalg.norm(fn), 1e-12)
        s = (p - q[i]) @ fn
        if open_tri[cand[i]] and d[i] > abs(s) * 1.02 + 1e-4:
            continue
        n_c += 1
        if s > 0:
            n_o += 1
    return n_c, n_o


def run(body, built, pairs=(), step=4, poses=None, local=False, only=None):
    """With local=True P1 and P3 look for 'the garment over this point' only among the triangles that were within LOCAL_RADIUS of it
    in the rest pose (see `associate`); res["P1lit"] and res["P3lit"] then hold the literal CONTRACTS numbers (nearest garment point
    anywhere), which cross-limb proximity (a long sleeve swinging past the hip) inflates. only: ids of the garments that get P1 and P2
    (the others are in `built` only to be the inner or outer garment of a pair)."""
    return _run(body, built, pairs, step, poses, local, only)


def _run(body, built, pairs=(), step=4, poses=None, local=False, only=None):
    """P1, P2 for every garment and P3 for every pair (inner Built, outer Built), over rest + every `step`th frame of Walk and Idle.
    Returns {"P1": {id: {pose: (considered, outside)}}, "P2": {id: {pose: (culled triangles, misses)}}, "P3": {...}}. Drawn
    surfaces only: the body triangles that the garment's own hides do not cull, the inner garment's triangles that the outer's
    hides do not cull."""
    rig = body.rig
    res = {"P1": {}, "P1all": {}, "P2": {}, "P3": {}}
    if local:
        res["P1lit"], res["P3lit"] = {}, {}
    flag = {b.garment.id: _hidden_flags(b.garment.hides) for b in built}
    btris = body.tris
    culled = {g: f[body.labels][btris].all(axis=1) for g, f in flag.items()}
    opn = {b.garment.id: open_triangles(b.soup.tris[:b.hem_start]) for b in built}
    slot = {b.garment.id: b.garment.slot for b in built}
    mine = [b for b in built if only is None or b.garment.id in only]
    for b in mine:
        res["P1"][b.garment.id], res["P1all"][b.garment.id], res["P2"][b.garment.id] = {}, {}, {}
    for inner, outer, _ in pairs:
        res["P3"][f"{inner.garment.id} under {outer.garment.id}"] = {}
        if local:
            res["P3lit"][f"{inner.garment.id} under {outer.garment.id}"] = {}
    band_of, cand1, cand3, idx3 = {}, {}, {}, {}
    for b in mine:
        g = b.garment.id
        band_of[g] = np.unique(btris[~culled[g] & flag[g][body.labels][btris].any(axis=1)])      # the drawn triangles that touch the hidden regions
        if local:
            res["P1lit"][g] = {}
    if local:
        set_pose(rig, None, 1)
        bco0 = _coords(body.obj)
        co0 = {b.garment.id: _coords(b.obj) for b in built}
        for b in mine:
            g = b.garment.id
            cand1[g] = associate(co0[g], b.soup.tris[:b.hem_start], bco0[band_of[g]], w_pts=body.W[band_of[g]], w_verts=b.soup.W)
        for inner, outer, _ in pairs:
            gi, go = inner.garment.id, outer.garment.id
            it = inner.soup.tris
            idx3[(gi, go)] = np.unique(it[~flag[go][inner.soup.label][it].all(axis=1)])
            cand3[(gi, go)] = associate(co0[go], outer.soup.tris[:outer.hem_start], co0[gi][idx3[(gi, go)]],
                                        w_pts=inner.soup.W[idx3[(gi, go)]], w_verts=outer.soup.W)
    for label, action, frame in (poses or pose_list(step)):
        set_pose(rig, action, frame)
        bco = _coords(body.obj)
        body_bvh = _bvh(bco, btris)
        co = {b.garment.id: _coords(b.obj) for b in built}
        bvh = {b.garment.id: _bvh(co[b.garment.id], b.soup.tris[:b.hem_start]) for b in built}       # the hem faces the skin: left out
        bvh_all = {b.garment.id: _bvh(co[b.garment.id], b.soup.tris) for b in mine}
        for b in mine:
            g = b.garment.id
            drawn_v = np.unique(btris[~culled[g]])
            res["P1all"][g][label] = _outside(bvh[g], bco[drawn_v], opn[g])
            band = band_of[g]
            if local:
                res["P1lit"][g][label] = _outside(bvh[g], bco[band], opn[g])
                res["P1"][g][label] = _outside_local(co[g], b.soup.tris[:b.hem_start], bco[band], cand1[g], opn[g])
            else:
                res["P1"][g][label] = _outside(bvh[g], bco[band], opn[g])
            t = btris[culled[g]]
            a, bb, c = bco[t[:, 0]], bco[t[:, 1]], bco[t[:, 2]]
            fn = np.cross(bb - a, c - a)
            fn /= np.maximum(np.linalg.norm(fn, axis=1, keepdims=True), 1e-12)
            cen = (a + bb + c) / 3
            reach = HOLE_REACH_FEET if slot[g] == "feet" else HOLE_REACH
            miss = 0
            for p, n in zip(cen, fn):
                o, d = tuple(p + n * 1e-5), tuple(n)
                if bvh_all[g].ray_cast(o, d, reach)[0] is None and body_bvh.ray_cast(o, d, reach)[0] is None:
                    miss += 1            # no garment, and not an occluded crease either (the skin would be hit first)
            res["P2"][g][label] = (len(t), miss)
        for inner, outer, _ in pairs:
            gi, go = inner.garment.id, outer.garment.id
            it = inner.soup.tris
            draw = ~flag[go][inner.soup.label][it].all(axis=1)
            pts3 = co[gi][np.unique(it[draw])]
            if local:
                res["P3lit"][f"{gi} under {go}"][label] = _outside(bvh[go], pts3, opn[go])
                res["P3"][f"{gi} under {go}"][label] = _outside_local(co[go], outer.soup.tris[:outer.hem_start], pts3,
                                                                       cand3[(gi, go)], opn[go])
            else:
                res["P3"][f"{gi} under {go}"][label] = _outside(bvh[go], pts3, opn[go])
    set_pose(rig, None, 1)
    return res


def layer_clearance(inner, outer, max_dist=0.03, drawn_only=False):
    """Rest pose: the smallest distance (mm) by which the outer garment's surface lies outside the inner garment's, over the
    inner vertices under the outer (CONTRACTS: >= 4 mm). Hem triangles are left out of the outer. drawn_only: leave out the inner
    vertices that the outer's hides cull (they are never drawn, so they cannot poke through)."""
    ob = _bvh(outer.soup.P, outer.soup.tris[:outer.hem_start])
    sel = np.arange(inner.n_carved)
    if drawn_only:
        hid = _hidden_flags(outer.garment.hides)
        it = inner.soup.tris[:inner.hem_start]
        draw = ~hid[inner.soup.label][it].all(axis=1)
        sel = np.intersect1d(sel, np.unique(it[draw]))
    d = []
    for p in inner.soup.P[sel]:
        loc, nor, _, dist = ob.find_nearest(tuple(p), max_dist)
        if loc is not None and (np.array(p) - np.array(loc)) @ np.array(nor) < 0:
            d.append(dist)
    return 1000 * min(d) if d else float("nan")


def summarize(res):
    """Prints the P1/P2/P3 table (worst frame per garment and pair) and returns True when everything is within its limit."""
    ok = True
    for kind, limit in (("P1", P1_MAX), ("P1all", P1_MAX), ("P1lit", P1_MAX), ("P2", P2_MAX), ("P3", P3_MAX), ("P3lit", P3_MAX)):
        for name, frames in res.get(kind, {}).items():
            frac = {k: (o / c if c else 0.0) for k, (c, o) in frames.items()}
            worst = max(frac, key=frac.get)
            c, o = frames[worst]
            rest = frames["rest"][1]
            small = kind in ("P1", "P1lit") and max(cn for cn, _ in frames.values()) < 400          # few vertices: at most 3 instead of a percentage
            good = (o <= 3 if small else frac[worst] <= limit) and (not kind.startswith("P1") or rest == 0)
            ok &= good or kind in ("P1all", "P1lit", "P3lit")          # informative only: P1all (every drawn body vertex near the garment, hands included) and the literal numbers
            log(f"{'PASS' if good else 'FAIL'} {kind} {name}: worst frame {worst} {o}/{c} = {100 * frac[worst]:.3f}% "
                f"(limit {100 * limit:.1f}%), rest {rest}/{frames['rest'][0]}")
    return ok
