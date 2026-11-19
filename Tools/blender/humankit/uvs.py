"""UVs: seams placed like a hand-made character layout - head (cut down the back of the skull, ears separate),
torso (cut down the spine), arms and legs (cut along their inner side), hands (back / palm), feet (top / sole),
then unwrapped, scaled to an even texel density (the head given more) and packed into one 0-1 tile."""
import math

import bmesh
import bpy
import numpy as np


def log(*a):
    print("[human]", *a, flush=True)


def nrm(v):
    v = np.asarray(v, float)
    return v / np.linalg.norm(v)


def unwrap(ob, body, J, neck_z=1.528, wrist_off=0.035, ankle_z=0.150):
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    bm.edges.ensure_lookup_table()
    fc = np.array([np.array(f.calc_center_median()) for f in bm.faces])
    fn = np.array([np.array(f.normal) for f in bm.faces])
    side = np.where(fc[:, 0] >= 0, 1.0, -1.0)
    F = fc.copy()
    F[:, 0] = np.abs(F[:, 0])                      # folded: every test is written for the left side
    hx, hy, hz = (np.asarray(a) for a in J["hand_axes"])
    W, GH, HJ, A, E = (np.asarray(J[k]) for k in ("wrist", "shoulder", "hip", "ankle", "elbow"))
    eyeball = np.array([f.material_index > 0 for f in bm.faces])     # eyeballs carry their own slots
    # --- parts
    part = np.full(len(fc), "torso", object)
    u_arm = nrm(E - GH)
    part[((F - GH) @ u_arm > -0.012) & (F[:, 2] > 0.75)] = "arm"
    part[((F - (W - hx * wrist_off)) @ hx > 0)] = "hand"
    d_leg = nrm(A - HJ)
    part[(F[:, 2] < 0.955) & ((F - HJ) @ d_leg > -0.035) & (part == "torso")] = "leg"
    part[F[:, 2] < ankle_z] = "foot"
    part[F[:, 2] > neck_z] = "head"
    cand = np.where((part == "head") & (np.linalg.norm(F - np.asarray(J["ear"]), axis=1) < 0.045))[0]
    part[cand[body.eval(fc[cand], skip=("ear",)) > 0.0022]] = "ear"
    part[eyeball] = "eye"
    key = np.array([f"{p}{'' if p in ('torso', 'head') else int(s)}" for p, s in zip(part, side)], object)
    # --- labels inside parts that become cuts (an edge between two different labels is a seam)
    sub = np.zeros(len(fc), int)

    def wrap_label(mask, origin, axis, b1, b2):
        """Faces just either side of the half plane at angle pi (direction -b1) get labels 1 and 2."""
        q = F[mask] - origin
        q -= np.outer(q @ axis, axis)
        phi = np.arctan2(q @ b2, q @ b1)
        lab = np.where(phi > math.pi / 2, 1, np.where(phi < -math.pi / 2, 2, 0))
        sub[np.where(mask)[0]] = lab

    # arms: cut along the inner side (towards the body)
    lat = nrm(np.cross(u_arm, (0, -1, 0)))
    if lat[2] < 0:
        lat = -lat
    wrap_label(part == "arm", GH, u_arm, lat, nrm(np.cross(u_arm, lat)))
    # legs: cut along the inner thigh and calf
    med = np.array([-1.0, 0, 0])
    wrap_label(part == "leg", HJ, d_leg, -med, nrm(np.cross(d_leg, -med)))
    # torso: cut down the back along the spine
    m = part == "torso"
    sub[m & (fc[:, 1] > 0.0)] = np.where(fc[m & (fc[:, 1] > 0.0), 0] >= 0, 1, 2)
    # head: cut down the back of the skull
    m = part == "head"
    sub[m & (fc[:, 1] > 0.005)] = np.where(fc[m & (fc[:, 1] > 0.005), 0] >= 0, 1, 2)
    # hands: back and palm
    m = part == "hand"
    fnf = fn.copy()
    fnf[:, 0] *= side
    sub[m] = np.where(fnf[m] @ hz > 0, 1, 2)
    # feet: sole, and the top cut down the back of the heel
    m = part == "foot"
    sole = m & (fn[:, 2] < -0.55)
    wrap_label(m, A, np.array([0, 0, 1.0]), np.array([0, -1.0, 0]), np.array([1.0, 0, 0]))
    sub[sole] = 3
    cut = 0
    for e in bm.edges:
        lf = e.link_faces
        e.seam = False
        if len(lf) != 2:
            continue
        a, b = lf[0].index, lf[1].index
        pair = {sub[a], sub[b]}
        e.seam = bool(key[a] != key[b] or pair == {1, 2} or (3 in pair and len(pair) == 2))
        cut += e.seam
    bm.to_mesh(me)
    bm.free()
    log(f"  uv: {cut} seam edges")
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.unwrap(method='ANGLE_BASED', fill_holes=True, margin=0.002)
    bpy.ops.object.mode_set(mode='OBJECT')
    # eyeballs: a front projection along the gaze (the back of the ball is never seen)
    uvl = me.uv_layers.active.data
    co = np.array([v.co for v in me.vertices])
    for p in me.polygons:
        if eyeball[p.index]:
            for li in p.loop_indices:
                v = co[me.loops[li].vertex_index]
                s = 1.0 if v[0] >= 0 else -1.0
                c = np.asarray(J["eye"]) * np.array([s, 1, 1])
                q = (v - c) / 0.013
                uvl[li].uv = (0.5 + 0.5 * q[0] * s, 0.5 + 0.5 * q[2])
    # even texel density, more on the head; then pack
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.average_islands_scale()
    bpy.ops.object.mode_set(mode='OBJECT')
    _scale_part(me, fc, part, ("head", "ear"), 1.6)
    _scale_part(me, fc, part, ("hand",), 1.25)
    _scale_part(me, fc, part, ("eye",), 2.0)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin=0.004)
    bpy.ops.object.mode_set(mode='OBJECT')
    log("  uv: unwrapped and packed")
    return part


def _scale_part(me, fc, part, names, k):
    """Scales the UV islands of the given parts about each face's island centre (approximately: about the part's
    UV centroid per connected piece is done by pack later; here every loop of the part is scaled about the part's
    own per-island centre via face connectivity)."""
    uvl = me.uv_layers.active.data
    sel = set(np.where(np.isin(part, names))[0].tolist())
    if not sel:
        return
    # islands by shared UV coordinates
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    uv = bm.loops.layers.uv.active
    seen = set()
    for fi in sel:
        if fi in seen:
            continue
        stack = [fi]
        isl = []
        seen.add(fi)
        while stack:
            f = bm.faces[stack.pop()]
            isl.append(f)
            for e in f.edges:
                if e.seam:
                    continue
                for g in e.link_faces:
                    if g.index in sel and g.index not in seen:
                        seen.add(g.index)
                        stack.append(g.index)
        pts = np.array([np.array(l[uv].uv) for f in isl for l in f.loops])
        c = pts.mean(axis=0)
        for f in isl:
            for l in f.loops:
                l[uv].uv = c + (np.array(l[uv].uv) - c) * k
    bm.to_mesh(me)
    bm.free()
