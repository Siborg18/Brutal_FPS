"""Skin weights: bone-heat weights from Blender, then the twist bones' share split off by position along the arm,
the eyes bound rigidly to their bones, and at most four influences per vertex (normalised) for the game."""
import bpy
import numpy as np


def log(*a):
    print("[human]", *a, flush=True)


TWIST = ("upperarm_twist", "forearm_twist")


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def vgroup_weights(ob, name):
    n = len(ob.data.vertices)
    w = np.zeros(n)
    vg = ob.vertex_groups.get(name)
    if vg is None:
        return w
    gi = vg.index
    for v in ob.data.vertices:
        for g in v.groups:
            if g.group == gi:
                w[v.index] = g.weight
    return w


def set_weights(ob, name, w):
    vg = ob.vertex_groups.get(name) or ob.vertex_groups.new(name=name)
    idx = np.where(w > 1e-4)[0]
    zero = np.where(w <= 1e-4)[0]
    if len(zero):
        vg.remove(zero.tolist())
    for i in idx:
        vg.add([int(i)], float(w[i]), 'REPLACE')


def skin(body, rig, eyes):
    bones = rig.data.bones
    off = ["root", "eye.L", "eye.R"] + [t + s for t in TWIST for s in (".L", ".R")]
    for n in off:
        bones[n].use_deform = False
    bpy.ops.object.select_all(action='DESELECT')
    body.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    for n in off:
        bones[n].use_deform = True
    missing = [b.name for b in bones if b.use_deform and b.name not in off and
               (body.vertex_groups.get(b.name) is None or vgroup_weights(body, b.name).max() < 0.05)]
    if missing:
        log("  WARNING bones without weights:", missing)
    co = np.array([v.co for v in body.data.vertices])
    # twist bones: share of the upper arm near the shoulder, share of the forearm near the wrist
    for s in (".L", ".R"):
        ua, fa = bones["upperarm" + s], bones["forearm" + s]
        for bname, b, share in (("upperarm_twist" + s, ua, lambda t: 1.0 - smoothstep(0.05, 0.65, t)),
                                ("forearm_twist" + s, fa, lambda t: smoothstep(0.20, 0.90, t))):
            h, tl = np.array(b.head_local), np.array(b.tail_local)
            L = np.linalg.norm(tl - h)
            t = ((co - h) @ (tl - h)) / (L * L)
            w = vgroup_weights(body, b.name)
            k = share(t) * (w > 0)
            set_weights(body, bname, w * k)
            set_weights(body, b.name, w * (1 - k))
    # eyes: rigid on their bones, joined into the body
    for e, bn in zip(eyes, ("eye.L", "eye.R")):
        vg = e.vertex_groups.new(name=bn)
        vg.add(list(range(len(e.data.vertices))), 1.0, 'REPLACE')
    bpy.ops.object.select_all(action='DESELECT')
    for e in eyes:
        e.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    smooth_and_limit(body, iters=8, alpha=0.5)
    nz = np.array([len([g for g in v.groups if g.weight > 0]) for v in body.data.vertices])
    log(f"  weights: {len(body.vertex_groups)} groups, influences per vertex max {nz.max()}, "
        f"unweighted {(nz == 0).sum()}")
    return body


SMOOTH_BONES = ("pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "clavicle", "upperarm", "thigh", "calf")


def smooth_and_limit(body, iters=8, alpha=0.5, limit=4):
    """Laplacian smoothing of the weights over the torso, shoulders, hips and legs (bone heat leaves hard lines
    where a limb meets the trunk), then the four largest influences per vertex, normalised."""
    me = body.data
    n = len(me.vertices)
    names = [g.name for g in body.vertex_groups]
    Wt = np.zeros((n, len(names)))
    for v in me.vertices:
        for g in v.groups:
            Wt[v.index, g.group] = g.weight
    ev = np.empty(len(me.edges) * 2, np.int64)
    me.edges.foreach_get("vertices", ev)
    ev = ev.reshape(-1, 2)
    dom = np.array(names)[Wt.argmax(axis=1)]
    mask = np.array([any(d.startswith(b) for b in SMOOTH_BONES) and "twist" not in d for d in dom])
    # also smooth the vertices next to the masked ones, so the region's edge isn't a step
    near = mask.copy()
    near[ev[mask[ev[:, 0]], 1]] = True
    near[ev[mask[ev[:, 1]], 0]] = True
    cnt = np.zeros(n)
    np.add.at(cnt, ev[:, 0], 1)
    np.add.at(cnt, ev[:, 1], 1)
    for _ in range(iters):
        acc = np.zeros_like(Wt)
        np.add.at(acc, ev[:, 0], Wt[ev[:, 1]])
        np.add.at(acc, ev[:, 1], Wt[ev[:, 0]])
        avg = acc / np.maximum(cnt, 1)[:, None]
        Wt[near] = (1 - alpha) * Wt[near] + alpha * avg[near]
    # top `limit` influences, normalised
    order = np.argsort(-Wt, axis=1)
    keep = np.zeros_like(Wt, bool)
    np.put_along_axis(keep, order[:, :limit], True, axis=1)
    Wt = np.where(keep & (Wt > 0.01), Wt, 0.0)
    Wt /= np.maximum(Wt.sum(axis=1, keepdims=True), 1e-9)
    for gi, name in enumerate(names):
        vg = body.vertex_groups[name]
        vg.remove(list(range(n)))
        col = Wt[:, gi]
        for i in np.where(col > 0)[0]:
            vg.add([int(i)], float(col[i]), 'REPLACE')
    log(f"  smoothed weights on {near.sum()} vertices")
