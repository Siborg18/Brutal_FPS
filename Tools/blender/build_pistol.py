"""
Builds the view model: a compact polymer-frame striker pistol (unbranded, no markings) held in a right hand, and
exports it as Assets/Models/pistol.glb. The scene is saved as Assets/pistol.blend (the source).

    blender -b --factory-startup -P Tools/blender/build_pistol.py

Coordinates (Blender): +X out of the muzzle, +Z up, +Y the gun's left side. The bore is at z = 0.0108 and the
muzzle at x = 0.093, as the game expects (Game/Weapon.cs). Object names are the part names the game animates:
Slide (with the sights), Barrel, BarrelHood, Trigger and TriggerFinger; Magazine, MagazineFollower, MagazineRound and
MagazineBase slide out together on a reload. Everything else is static.

    blender -b --factory-startup -P Tools/blender/build_pistol.py -- magazine

re-adds just the magazine to the saved Assets/pistol.blend and re-exports, without re-posing or re-baking the hand.

The hand is built from its skeleton: joint positions for every phalanx (proportions from an anatomical hand),
posed around the actual grip (fingers wrapped round the front strap, index finger on the trigger, thumb along the
frame), fleshed out with tapered capsules, palm and thenar volumes, voxel-remeshed into one smooth surface, pressed
against the grip with a boolean, and given its own baked texture (skin tone, redder knuckles and fingertips,
joint creases, pores) - texture sets "hand" and "hand_index".
"""
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import modelkit as K  # noqa: E402
from modelkit import box, cyl, prism, mat, add_bevel  # noqa: E402

TEX = K.TEX


# ============================================================================================ helpers
def boolean(target, cutter, op='DIFFERENCE'):
    mod = target.modifiers.new("bool_" + cutter.name, 'BOOLEAN')
    mod.object = cutter
    mod.operation = op
    mod.solver = 'EXACT'
    cutter.hide_render = True
    cutter.hide_set(True)
    cutter["cutter"] = True
    return mod


def cutter_box(name, size, loc, rot=(0, 0, 0)):
    return box("cut_" + name, size, loc, None, rot=rot)


def cutter_cyl(name, r, depth, loc, rot=(0, 0, 0), verts=24):
    return cyl("cut_" + name, r, depth, loc, None, verts=verts, rot=rot)


def finish_bevels(obj):
    """Moves bevel modifiers after the booleans."""
    bpy.context.view_layer.objects.active = obj
    for m in [m for m in obj.modifiers if m.type == 'BEVEL']:
        bpy.ops.object.modifier_move_to_index(modifier=m.name, index=len(obj.modifiers) - 1)


def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.name = name
    return o


def apply_all(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.hide_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    for m in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def frame_from(z_axis, x_hint):
    z = Vector(z_axis).normalized()
    x = Vector(x_hint) - z * Vector(x_hint).dot(z)
    if x.length < 1e-6:
        x = z.orthogonal()
    x.normalize()
    y = z.cross(x)
    return Matrix((x, y, z)).transposed()


# Every organic primitive also records its exact signed distance function, so the hand can be given a smooth
# union (muscles flowing into the palm, fingers into the knuckles) instead of the creases of a hard union.
SDF = {}            # object name -> (kind, params)
GROUP_K = {"body": 0.0075, "thumb": 0.0030}     # blend radius inside a group (fingers default to FINGER_K)
JOIN_K = {"thumb": 0.0065}                       # blend radius of a group into "body" (fingers default to 0.0045)
FINGER_K = 0.0022
CONTACT_K = 0.0020                               # fillet where separate groups (fingers, thumb) touch


def _smin(a, b, k):
    h = np.maximum(k - np.abs(a - b), 0.0) / k
    return np.minimum(a, b) - h * h * k * 0.25


def _sd_round_cone(q, L, r1, r2):
    """Inigo Quilez's round cone from the origin to (0, 0, L), in local coordinates q (N, 3)."""
    l2 = L * L
    rr = r1 - r2
    a2 = l2 - rr * rr
    il2 = 1.0 / l2
    y = q[:, 2] * L
    z = y - l2
    x2 = (q[:, 0] ** 2 + q[:, 1] ** 2) * l2 * l2
    y2 = y * y * l2
    z2 = z * z * l2
    k = np.sign(rr) * rr * rr * x2
    d_mid = (np.sqrt(np.maximum(x2 * a2 * il2, 0)) + y * rr) * il2 - r1
    d_b = np.sqrt(x2 + z2) * il2 - r2
    d_a = np.sqrt(x2 + y2) * il2 - r1
    return np.where(np.sign(z) * a2 * z2 > k, d_b, np.where(np.sign(y) * a2 * y2 < k, d_a, d_mid))


def sdf_eval(P, names):
    """Signed distance (approximate where squashed) to the union of the named primitives: smooth unions inside
    each group, each group blended into "body", groups other than body only
    filleted where they touch (CONTACT_K: flesh pressed on flesh, never a folded crease)."""
    groups = {}
    for n in names:
        kind, prm = SDF[n]
        if kind == "cone":
            M, a, L, r1, r2, flat = prm
            q = (P - a) @ M
            q[:, 1] /= flat
            d = _sd_round_cone(q, L, r1, r2) * (0.5 + 0.5 * flat)
        elif kind == "ell":
            M, c, r = prm
            q = (P - c) @ M
            k0 = np.linalg.norm(q / r, axis=1)
            k1 = np.linalg.norm(q / (r * r), axis=1)
            d = k0 * (k0 - 1.0) / np.maximum(k1, 1e-9)
        else:   # convex hull: a smooth maximum of its face planes rounds the edges
            Nn, off, k = prm
            s = (P @ Nn.T - off) / k
            m = s.max(axis=1)
            d = k * (m + np.log(np.exp(s - m[:, None]).sum(axis=1)))
        g = GROUPS.get(n, "body")
        if g in groups:
            groups[g] = _smin(groups[g], d, GROUP_K.get(g, FINGER_K))
        else:
            groups[g] = d
    body = groups.pop("body", None)
    out = None
    for g, d in groups.items():
        dd = d if body is None else _smin(body, d, JOIN_K.get(g, 0.0045))
        out = dd if out is None else _smin(out, dd, CONTACT_K)
    if out is None:
        out = body
    elif body is not None:
        out = np.minimum(out, body)
    return out


GROUPS = {}         # object name -> blend group ("body" if absent)
NAILS = []          # (DIP joint, fingertip, dorsal direction, finger radius)
HAND_NAILS = {}     # hand name -> its nails (so one hand's nails are never painted on the other)
CURRENT = {"nails": None}   # the nails nail_fields uses (None: all of them)


def use_nails(hand_name):
    CURRENT["nails"] = HAND_NAILS.get(hand_name)


def nail_fields(co, nrm):
    """Signed distance inside the nearest nail's outline (metres, negative outside) and the position along it
    (0 = cuticle, 1 = free edge). Both vary smoothly, so the shader can draw a crisp edge from them."""
    best = np.full(len(co), -0.003)
    along = np.zeros(len(co))
    for dip, tip, out, r in (NAILS if CURRENT["nails"] is None else CURRENT["nails"]):
        dip, tip, out = np.array(dip), np.array(tip), np.array(out)
        L = np.linalg.norm(tip - dip)
        ax = (tip - dip) / L
        out = out - ax * out.dot(ax)
        out /= np.linalg.norm(out)
        side = np.cross(ax, out)
        rel = co - dip
        a = rel @ ax / L
        up, sd = rel @ out, rel @ side
        rad = np.sqrt(up * up + sd * sd)
        ang = np.arctan2(sd, up)
        half = math.radians(50)
        a0 = 0.47 + 0.10 * (ang / half) ** 2          # the cuticle is a U shape
        a1 = 1.02 - 0.06 * (ang / half) ** 2          # the free edge follows the fingertip round
        e = np.minimum.reduce([(a - a0) * L, (a1 - a) * L, (half - np.abs(ang)) * rad])
        valid = (rad < r * 1.25) & (a > 0.2) & (a < 1.3) & ((nrm @ out) > 0.0)
        e = np.where(valid, np.clip(e, -0.003, 0.003), -0.003)
        better = e > best
        best = np.where(better, e, best)
        along = np.where(better, np.clip((a - a0) / np.maximum(a1 - a0, 1e-3), 0, 1), along)
    return best, along


def nail_mask(co, nrm, soft=0.00035):
    e, along = nail_fields(co, nrm)
    return np.clip(e / soft, 0.0, 1.0), along


def raise_nails(o, height=0.00016):
    me = o.data
    co = np.array([v.co for v in me.vertices])
    nrm = np.array([v.normal for v in me.vertices])
    m, _ = nail_mask(co, nrm, soft=0.0006)
    co += nrm * (m * height)[:, None]
    me.vertices.foreach_set("co", co.ravel())
    me.update()


CARVE_GAP, CARVE_K = 0.0005, 0.0050     # skin pressed against skin: the gap left, and the softness of the crease


def carved_sdf(P, names, carve):
    """The smooth union of `names`, with the smooth union of `carve` (another hand) softly subtracted."""
    d = sdf_eval(P, names)
    if not carve:
        return d
    other = -(sdf_eval(P, carve) - CARVE_GAP)
    return -_smin(-d, -other, CARVE_K)


def project_to_sdf(o, names, iters=6, carve=None):
    """Moves every vertex of o onto the zero set of the smooth union (Newton steps along the gradient)."""
    me = o.data
    P = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", P)
    P = P.reshape(-1, 3)
    mw = np.array(o.matrix_world)
    assert np.allclose(mw, np.eye(4)), "apply transforms first"
    e = 1e-4
    E = np.eye(3) * e
    iters = iters + (4 if carve else 0)

    def sdf_eval(Q, n):
        return carved_sdf(Q, n, carve)

    for _ in range(iters):
        d = sdf_eval(P, names)
        g = np.stack([(sdf_eval(P + E[i], names) - sdf_eval(P - E[i], names)) / (2 * e) for i in range(3)], axis=1)
        g2 = np.maximum((g * g).sum(axis=1), 1e-6)
        step = (d / g2)[:, None] * g
        ln = np.linalg.norm(step, axis=1)
        step *= np.minimum(1.0, 0.002 / np.maximum(ln, 1e-12))[:, None]
        P -= step
    me.vertices.foreach_set("co", P.ravel())
    me.update()
    print(f"[pistol] projected {len(P)} verts onto the smooth union, max residual {np.abs(sdf_eval(P, names)).max() * 1000:.3f} mm", flush=True)


def relax_contact(o, other, reach=0.0025, iters=20):
    """Laplacian-smooths the vertices within `reach` of the other hand's surface (fading out towards it), ironing
    out the crinkles the carve leaves in narrow pockets."""
    me = o.data
    P = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", P)
    P = P.reshape(-1, 3)
    w = np.clip(1.0 - np.abs(sdf_eval(P, other) - CARVE_GAP) / reach, 0.0, 1.0) * 0.6
    E = np.empty(len(me.edges) * 2, dtype=np.int64)
    me.edges.foreach_get("vertices", E)
    E = E.reshape(-1, 2)
    deg = np.bincount(E.ravel(), minlength=len(P)).astype(float)
    for _ in range(iters):
        acc = np.zeros_like(P)
        np.add.at(acc, E[:, 0], P[E[:, 1]])
        np.add.at(acc, E[:, 1], P[E[:, 0]])
        avg = acc / np.maximum(deg, 1)[:, None]
        P += (avg - P) * w[:, None]
    me.vertices.foreach_set("co", P.ravel())
    me.update()
    print(f"[pistol] relaxed {int((w > 0).sum())} verts at the skin contact", flush=True)


def relax_folds(o, passes=4, iters=6, cos_min=0.75):
    """Finds crinkles (vertices whose normal disagrees sharply with their neighbours', as the remesh leaves in tight
    crevices) and Laplacian-smooths them and their neighbourhood."""
    me = o.data
    E = np.empty(len(me.edges) * 2, dtype=np.int64)
    me.edges.foreach_get("vertices", E)
    E = E.reshape(-1, 2)
    nv = len(me.vertices)
    deg = np.maximum(np.bincount(E.ravel(), minlength=nv).astype(float), 1)

    def spread(v):
        acc = np.zeros_like(v)
        np.add.at(acc, E[:, 0], v[E[:, 1]])
        np.add.at(acc, E[:, 1], v[E[:, 0]])
        return acc / (deg if v.ndim == 1 else deg[:, None])

    total = 0
    for _ in range(passes):
        me.update()
        N = np.empty(nv * 3)
        me.vertices.foreach_get("normal", N)
        N = N.reshape(-1, 3)
        avg = spread(N)
        avg /= np.maximum(np.linalg.norm(avg, axis=1), 1e-9)[:, None]
        bad = ((N * avg).sum(axis=1) < cos_min).astype(float)
        if not bad.any():
            break
        total += int(bad.sum())
        w = np.minimum(1.0, bad + spread(bad) * 2.0)
        w = np.minimum(1.0, w + spread(w))
        P = np.empty(nv * 3)
        me.vertices.foreach_get("co", P)
        P = P.reshape(-1, 3)
        for _ in range(iters):
            P += (spread(P) - P) * (w * 0.6)[:, None]
        me.vertices.foreach_set("co", P.ravel())
    me.update()
    print(f"[pistol] relaxed {total} folded verts on {o.name}", flush=True)


def capsule(name, a, b, ra, rb, flat=1.0, width_dir=(0, 0, 1), segs=20, rings=6):
    """A tapered capsule from a to b (radii ra, rb), squashed to `flat` across width_dir's perpendicular."""
    a, b = Vector(a), Vector(b)
    L = (b - a).length
    SDF[name] = ("cone", (np.array(frame_from(b - a, width_dir)), np.array(a), L, ra, rb, flat))
    bm = bmesh.new()
    prof = []
    for k in range(rings + 1):
        ang = -math.pi / 2 + (math.pi / 2) * k / rings
        prof.append((ra * math.cos(ang), ra * math.sin(ang)))
    for k in range(1, rings + 1):
        ang = (math.pi / 2) * k / rings
        prof.append((rb * math.cos(ang), L + rb * math.sin(ang)))
    rings_v = []
    for r, z in prof:
        ring = [bm.verts.new((r * math.cos(t), r * math.sin(t), z)) for t in (math.tau * i / segs for i in range(segs))]
        rings_v.append(ring)
    for r0, r1 in zip(rings_v, rings_v[1:]):
        for i in range(segs):
            try:
                bm.faces.new((r0[i], r0[(i + 1) % segs], r1[(i + 1) % segs], r1[i]))
            except ValueError:
                pass
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-7)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    rot = frame_from(b - a, width_dir).to_4x4()
    o.matrix_world = Matrix.Translation(a) @ rot @ Matrix.Diagonal((1.0, flat, 1.0, 1.0))
    return o


def ellipsoid(name, center, radii, z_axis=(0, 0, 1), x_hint=(1, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=1.0)
    o = bpy.context.active_object
    o.name = name
    o.matrix_world = Matrix.Translation(center) @ frame_from(z_axis, x_hint).to_4x4() @ Matrix.Diagonal((*radii, 1.0))
    SDF[name] = ("ell", (np.array(frame_from(z_axis, x_hint)), np.array(Vector(center)), np.array(radii, dtype=float)))
    return o


def hull(name, points, round_k=0.003):
    bm = bmesh.new()
    verts = [bm.verts.new(p) for p in points]
    bmesh.ops.convex_hull(bm, input=verts)
    bm.faces.ensure_lookup_table()
    ctr = sum((Vector(p) for p in points), Vector()) / len(points)
    normals, offs = [], []
    for f in bm.faces:
        f.normal_update()
        n = f.normal.copy()
        c = f.calc_center_median()
        if n.dot(c - ctr) < 0:
            n = -n
        normals.append(n)
        offs.append(n.dot(c))
    SDF[name] = ("hull", (np.array(normals), np.array(offs), round_k))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


# ============================================================================================ pistol
BORE_Z = 0.0108
SLIDE_X0, SLIDE_X1 = -0.081, 0.093
SLIDE_ZB, SLIDE_ZT = -0.004, 0.0275
SLIDE_W = 0.0255

# The grip, as straight lines in the side view (x, z): front strap and back strap.
FRONT0, FRONT1 = Vector((-0.0380, -0.0330)), Vector((-0.0570, -0.0950))
BACK0, BACK1 = Vector((-0.0870, -0.0220)), Vector((-0.1075, -0.0950))
GRIP_W = 0.031
TRIGGER_PIVOT = Vector((-0.0025, 0.0, -0.0225))


def build_pistol():
    gun = mat("gunmetal", None, 0.35, 0.85)
    poly = mat("polymer", None, 0.6, 0.0)
    stip = mat("tex_grip_stipple", None, 0.7, 0.0)
    white = mat("flat_sight_white", (0.93, 0.93, 0.9), 0.5, 0.0)
    parts = {}

    # --- Slide: its cross-section (flat top with chamfered edges) run along X.
    w = SLIDE_W / 2
    c = 0.0035
    section = [(-w, SLIDE_ZB), (w, SLIDE_ZB), (w, SLIDE_ZT - c), (w - c, SLIDE_ZT), (-w + c, SLIDE_ZT), (-w, SLIDE_ZT - c)]
    slide = prism("Slide", section, SLIDE_X1 - SLIDE_X0, ((SLIDE_X0 + SLIDE_X1) / 2, 0, 0), gun, axis='X')
    # Rounded nose: chamfers across the front top, bottom and sides.
    boolean(slide, cutter_box("nose_top", (0.012, 0.04, 0.012), (SLIDE_X1 + 0.0015, 0, SLIDE_ZT + 0.002), rot=(0, 45, 0)))
    boolean(slide, cutter_box("nose_bottom", (0.010, 0.04, 0.010), (SLIDE_X1 + 0.002, 0, SLIDE_ZB - 0.0015), rot=(0, 45, 0)))
    for s in (-1, 1):
        boolean(slide, cutter_box(f"nose_side{s}", (0.010, 0.010, 0.05), (SLIDE_X1 + 0.0015, s * (w + 0.0015), 0.012), rot=(0, 0, 45)))
    # Cocking serrations, rear and front, on both sides.
    for s in (-1, 1):
        for i in range(8):
            boolean(slide, cutter_box(f"ser_r{s}{i}", (0.0013, 0.003, 0.022), (SLIDE_X0 + 0.0045 + i * 0.0036, s * w, 0.0125)))
        for i in range(7):
            boolean(slide, cutter_box(f"ser_f{s}{i}", (0.0013, 0.003, 0.020), (SLIDE_X1 - 0.040 + i * 0.0036, s * w, 0.0125)))
    # Ejection port on the top right, and the muzzle opening.
    boolean(slide, cutter_box("port", (0.032, 0.0185, 0.0165), (0.004, -0.0045, 0.0218)))
    boolean(slide, cutter_cyl("muzzle", 0.0070, 0.02, (SLIDE_X1, 0, BORE_Z), rot=(0, 90, 0), verts=32))
    boolean(slide, cutter_box("slide_channel", (0.18, 0.019, 0.006), (0.006, 0, SLIDE_ZB + 0.0015)))
    add_bevel(slide, 0.0007, 3)
    finish_bevels(slide)
    parts["Slide"] = [slide]
    # Extractor (right side, ahead of the port) and the cover plate at the back of the slide.
    parts["Slide"].append(box("extractor", (0.022, 0.0014, 0.0045), (0.026, -w - 0.0004, 0.0165), gun, bevel=0.0004))
    parts["Slide"].append(box("cover_plate", (0.0012, 0.017, 0.022), (SLIDE_X0 - 0.0004, 0, 0.0115), poly, bevel=0.0004))

    # --- Sights: a U-notch rear sight with a white outline, a post front sight with a white dot.
    rear = box("RearSight", (0.009, 0.020, 0.0055), (SLIDE_X0 + 0.010, 0, SLIDE_ZT + 0.0025), gun, bevel=0.0006)
    boolean(rear, cutter_box("notch", (0.012, 0.0036, 0.004), (SLIDE_X0 + 0.010, 0, SLIDE_ZT + 0.0050)))
    finish_bevels(rear)
    rear_white = []
    for y in (-0.0034, 0.0034):
        rear_white.append(box("u_side", (0.0003, 0.0011, 0.0032), (SLIDE_X0 + 0.0055, y, SLIDE_ZT + 0.0035), white))
    rear_white.append(box("u_bottom", (0.0003, 0.0078, 0.0010), (SLIDE_X0 + 0.0055, 0, SLIDE_ZT + 0.0016), white))
    parts["RearSight"] = [rear] + rear_white
    front = box("FrontSight", (0.0045, 0.0035, 0.0050), (SLIDE_X1 - 0.008, 0, SLIDE_ZT + 0.0023), gun, bevel=0.0005)
    dot = cyl("dot", 0.0011, 0.0004, (SLIDE_X1 - 0.0103, 0, SLIDE_ZT + 0.0030), white, verts=16, rot=(0, 90, 0))
    parts["FrontSight"] = [front, dot]

    # --- Barrel (visible in the muzzle) and its hood (seen through the ejection port).
    barrel = cyl("Barrel", 0.0066, 0.105, (0.040, 0, BORE_Z), gun, verts=32, rot=(0, 90, 0))
    boolean(barrel, cutter_cyl("bore", 0.0045, 0.03, (0.093, 0, BORE_Z), rot=(0, 90, 0), verts=24))
    parts["Barrel"] = [barrel]
    hood = box("BarrelHood", (0.032, 0.021, 0.0150), (0.004, 0, BORE_Z + 0.0075), gun, bevel=0.0006)
    parts["BarrelHood"] = [hood]

    # --- Frame: the dust cover with its accessory rail, the receiver and the beavertail.
    frame_outline = [(0.0865, -0.0040), (0.0865, -0.0170), (0.0830, -0.0205), (0.0300, -0.0215), (0.0220, -0.0240),
                     (-0.0280, -0.0240), (-0.0360, -0.0300), (-0.0420, -0.0350), (-0.0850, -0.0260), (-0.0935, -0.0120),
                     (-0.0900, -0.0055), (-0.0810, -0.0040)]
    frame = prism("Frame", [(x, z) for x, z in frame_outline], 0.0265, (0, 0, 0), poly, axis='Y')
    for s in (-1, 1):
        boolean(frame, cutter_box(f"rail{s}", (0.052, 0.003, 0.0022), (0.061, s * 0.0133, -0.0160)))
    boolean(frame, cutter_box("rail_slot", (0.0045, 0.03, 0.003), (0.074, 0, -0.0215)))
    boolean(frame, cutter_box("rail_slot2", (0.0045, 0.03, 0.003), (0.062, 0, -0.0215)))
    add_bevel(frame, 0.0011, 3)
    finish_bevels(frame)
    parts["Frame"] = [frame]

    # --- Grip: straight front and back straps (no finger grooves), flared at the magazine well, stippled all over.
    grip_outline = [(FRONT0.x, FRONT0.y), (FRONT1.x, FRONT1.y), (FRONT1.x + 0.0025, FRONT1.y - 0.004),
                    (BACK1.x - 0.0015, BACK1.y - 0.004), (BACK1.x, BACK1.y), (BACK0.x, BACK0.y), (-0.0600, -0.0240)]
    grip = prism("Grip", grip_outline, GRIP_W, (0, 0, 0), stip, axis='Y')
    add_bevel(grip, 0.0065, 4)
    parts["Grip"] = [grip]

    # --- Trigger guard: a squared front with a small hook, its opening cut out.
    guard_outer = [(0.0300, -0.0210), (0.0300, -0.0440), (0.0325, -0.0458), (0.0285, -0.0482), (-0.0300, -0.0482),
                   (-0.0420, -0.0360), (-0.0360, -0.0290), (-0.0280, -0.0230)]
    guard = prism("TriggerGuard", guard_outer, 0.0125, (0, 0, 0), poly, axis='Y')
    opening = prism("cut_guard", [(0.0225, -0.0242), (0.0225, -0.0420), (0.0195, -0.0438), (-0.0255, -0.0438),
                                  (-0.0330, -0.0345), (-0.0290, -0.0242)], 0.05, (0, 0, 0), None, axis='Y')
    boolean(guard, opening)
    add_bevel(guard, 0.0012, 3)
    finish_bevels(guard)
    parts["TriggerGuard"] = [guard]

    # --- Trigger: a curved blade with the safety lever in its face. Pivots at its top.
    px, pz = TRIGGER_PIVOT.x, TRIGGER_PIVOT.z
    blade = [(px - 0.0025, pz), (px + 0.0040, pz), (px + 0.0060, pz - 0.0075), (px + 0.0050, pz - 0.0145),
             (px + 0.0015, pz - 0.0180), (px - 0.0012, pz - 0.0172), (px + 0.0012, pz - 0.0120), (px + 0.0012, pz - 0.0060)]
    trig = prism("Trigger", blade, 0.0068, (0, 0, 0), poly, axis='Y')
    boolean(trig, cutter_box("safety_slot", (0.004, 0.0026, 0.010), (px + 0.0048, 0, pz - 0.0105)))
    add_bevel(trig, 0.0006, 2)
    finish_bevels(trig)
    lever = prism("safety", [(px + 0.0030, pz - 0.0060), (px + 0.0066, pz - 0.0080), (px + 0.0056, pz - 0.0150), (px + 0.0030, pz - 0.0140)], 0.0022, (0, 0, 0), poly, axis='Y')
    parts["Trigger"] = [trig, lever]

    # --- Controls and pins: slide stop levers, takedown tabs, magazine release, frame pins.
    steel_dark = mat("flat_pin", (0.20, 0.20, 0.21), 0.35, 1.0)
    statics = []
    for s in (-1, 1):
        statics.append(box("slide_stop", (0.017, 0.0016, 0.0036), (0.004, s * 0.0141, -0.0058), gun, bevel=0.0004))
        statics.append(box("stop_tab", (0.004, 0.0020, 0.003), (0.010, s * 0.0143, -0.0040), gun, bevel=0.0004))
        statics.append(box("takedown", (0.0065, 0.0013, 0.0040), (0.019, s * 0.0139, -0.0095), gun, bevel=0.0003))
        statics.append(box("mag_release", (0.0062, 0.0035, 0.0085), (-0.0405, s * 0.0158, -0.0322), poly, bevel=0.0010))
    for x, z in ((0.013, -0.0175), (0.003, -0.0110), (-0.061, -0.0105)):
        statics.append(cyl("pin", 0.0012, 0.0272, (x, 0, z), steel_dark, verts=12, rot=(90, 0, 0)))
    parts["SlideStop"] = statics

    # --- Magazine baseplate with its front lip.
    base = prism("MagazineBase", [(FRONT1.x + 0.0030, FRONT1.y - 0.0045), (FRONT1.x + 0.0055, FRONT1.y - 0.0072),
                                  (FRONT1.x + 0.0030, FRONT1.y - 0.0112), (BACK1.x - 0.0020, BACK1.y - 0.0112),
                                  (BACK1.x - 0.0025, BACK1.y - 0.0045)], 0.0295, (0, 0, 0), poly, axis='Y')
    add_bevel(base, 0.0018, 3)
    parts["MagazineBase"] = [base]
    parts.update(build_magazine())
    return parts


# The magazine (in the grip, out of sight until a reload drops it): a polymer-cased double-stack magazine with steel
# feed lips, its follower and the top round. It lies along the grip's slope; the game slides it (with MagazineBase)
# out along MAG_AXIS, and drops it as a physics body (Game/Weapon.cs mirrors these numbers).
MAG_AXIS = Vector((-0.2932, -0.9560)).normalized()      # (x, z): down the grip
MAG_DEPTH = 0.033                                        # front to back, across its axis
MAG_WIDTH = 0.0215
MAG_BOTTOM_FRONT = Vector((-0.0575, FRONT1.y - 0.0045))  # front bottom corner, on the baseplate
MAG_TOP_Z = -0.0025                                      # just under the barrel


def magazine_outline(inset=0.0):
    up = -MAG_AXIS
    fwd = Vector((up.y, -up.x))                          # across the axis, towards the muzzle
    depth = (MAG_DEPTH - 2 * inset) / up.y               # measured level (the baseplate and top are level)
    bf = MAG_BOTTOM_FRONT - Vector((inset / up.y, 0))
    bb = bf - Vector((depth, 0))
    length = (MAG_TOP_Z - bf.y) / up.y
    return bf, bf + up * length, bb + up * length, bb, up, fwd


def build_magazine():
    poly = mat("polymer", None, 0.6, 0.0)
    gun = mat("gunmetal", None, 0.35, 0.85)
    brass = mat("flat_brass", (0.80, 0.60, 0.26), 0.3, 1.0)
    copper = mat("flat_copper", (0.74, 0.44, 0.28), 0.35, 1.0)
    orange = mat("flat_follower", (0.70, 0.34, 0.10), 0.6, 0.0)
    bf, tf, tb, bb, up, fwd = magazine_outline()
    body = prism("Magazine", [(bf.x, bf.y), (tf.x, tf.y), (tb.x, tb.y), (bb.x, bb.y)], MAG_WIDTH, (0, 0, 0), poly, axis='Y')
    add_bevel(body, 0.0012, 2)
    pieces = [body]
    # Steel feed lips along the top edges, and a steel spine down the back.
    mid = (tf + tb) / 2
    lip_len = (tf - tb).length * 0.8
    angle = math.degrees(math.atan2(up.x, up.y))   # the magazine's lean (about Blender Y)
    for side in (-1, 1):
        pieces.append(box("lip", (lip_len, 0.0022, 0.0035), (mid.x - up.x * 0.0012, side * (MAG_WIDTH / 2 - 0.0011), mid.y - up.y * 0.0012),
                          gun, rot=(0, angle, 0), bevel=0.0004))
    spine = (bb + tb) / 2 + fwd * 0.0009
    pieces.append(box("spine", (0.002, MAG_WIDTH * 0.7, (tb - bb).length * 0.9), (spine.x, 0, spine.y), gun, rot=(0, angle, 0), bevel=0.0004))
    # The follower sits under the top round (it shows when the magazine is empty).
    fbf, ftf, ftb, fbb, _, _ = magazine_outline(0.0016)
    top = MAG_TOP_Z - 0.0006
    low = top - 0.004
    def at_z(a, b, z):
        t = (z - a.y) / (b.y - a.y)
        return a + (b - a) * t
    f_front_top, f_front_low = at_z(fbf, ftf, top), at_z(fbf, ftf, low)
    f_back_top, f_back_low = at_z(fbb, ftb, top), at_z(fbb, ftb, low)
    follower = prism("MagazineFollower", [(f_front_low.x, low), (f_front_top.x, top), (f_back_top.x, top), (f_back_low.x, low)],
                     MAG_WIDTH - 0.0032, (0, 0, 0), orange, axis='Y')
    # The top round: a 9 mm cartridge lying across the magazine, pointing forward (and a touch down, square to the
    # magazine), staggered to one side.
    centre = mid + up * 0.0042
    case_c = centre - fwd * 0.0051
    tip_c = centre + fwd * 0.0095
    lean = math.degrees(math.atan2(fwd.x, fwd.y))   # cylinder Z axis turned onto fwd
    case = cyl("round_case", 0.0049, 0.019, (case_c.x, 0.0025, case_c.y), brass, verts=20, rot=(0, lean, 0))
    tip = cyl("round_tip", 0.00452, 0.0102, (tip_c.x, 0.0025, tip_c.y), copper, verts=20, rot=(0, lean, 0), radius2=0.0021)
    return {"Magazine": pieces, "MagazineFollower": [follower], "MagazineRound": [case, tip]}


# ============================================================================================ hand
# Lengths in metres (soft tissue included), from an adult hand's skeleton proportions.
FINGERS = {
    #          proximal, middle, distal, radius
    "index": (0.041, 0.024, 0.020, 0.0090),
    "middle": (0.045, 0.028, 0.021, 0.0094),
    "ring": (0.042, 0.027, 0.021, 0.0088),
    "little": (0.033, 0.020, 0.018, 0.0078),
}
THUMB = (0.044, 0.032, 0.026, 0.0090)   # metacarpal, proximal, distal, radius


def grip_section(s):
    """The grip's cross-section s metres down the front strap: (centre, forward axis, half depth)."""
    g = (FRONT1 - FRONT0).normalized()
    fp = FRONT0 + g * s
    back_dir = Vector((-g.y, g.x)) if (-g.y) < 0 else Vector((g.y, -g.x))   # perpendicular, pointing backwards
    # Distance from fp along back_dir to the back strap line.
    bd = (BACK1 - BACK0).normalized()
    # Solve fp + t*back_dir = BACK0 + u*bd
    m = np.array([[back_dir.x, -bd.x], [back_dir.y, -bd.y]])
    t, _ = np.linalg.solve(m, np.array([BACK0.x - fp.x, BACK0.y - fp.y]))
    center2 = fp + back_dir * (t / 2)
    fwd = Vector((-back_dir.x, 0, -back_dir.y))
    down = Vector((g.x, 0, g.y))
    return Vector((center2.x, 0, center2.y)), fwd, down, t / 2


def wrap_chain(s, lengths, radius, phi0, press=0.82):
    """Joint positions of a finger wrapped round the grip at depth s: MCP at angle phi0 (degrees, 0 = front strap,
    +90 = left side), each phalanx a chord of the offset ellipse."""
    c, fwd, down, a = grip_section(s)
    side = Vector((0, 1, 0))
    ra, rb = a + radius * press, GRIP_W / 2 + radius * press

    def q(phi):
        return c + fwd * (ra * math.cos(phi)) + side * (rb * math.sin(phi))

    phi = math.radians(phi0)
    pts = [q(phi)]
    for L in lengths:
        lo, hi = phi, phi + math.pi
        for _ in range(40):   # bisection for the chord length
            mid = (lo + hi) / 2
            if (q(mid) - pts[-1]).length < L:
                lo = mid
            else:
                hi = mid
        phi = (lo + hi) / 2
        pts.append(q(phi))
    return pts, c, down


def finger_volumes(name, pts, radius, outward_center, axis_hint, group=None):
    """Tapered phalanges (wider than thick), slight swellings at the joints and a rounded pad at the tip."""
    objs = []
    # A real finger narrows a good deal from knuckle to tip (about 0.7), and its joints only widen it slightly:
    # big round joints read as sausages.
    rs = [radius, radius * 0.87, radius * 0.78, radius * 0.66]
    for i in range(3):
        a, b = pts[i], pts[i + 1]
        o = capsule(f"{name}_ph{i}", a, b, rs[i] * 1.0, rs[i + 1] * (0.96 if i == 2 else 1.0), flat=0.78, width_dir=axis_hint)
        objs.append(o)
        if i > 0:
            objs.append(ellipsoid(f"{name}_joint{i}", a, (rs[i] * 1.02, rs[i] * 0.76, rs[i] * 0.5), z_axis=b - a, x_hint=axis_hint))
    # The pad of the fingertip: a flattened bulge on the palm side of the distal phalanx.
    a, b = pts[2], pts[3]
    inward = (outward_center - a.lerp(b, 0.6))
    inward = inward - (b - a).normalized() * inward.dot((b - a).normalized())
    if inward.length > 1e-6:
        inward.normalize()
        objs.append(ellipsoid(f"{name}_pad", a.lerp(b, 0.58) + inward * rs[2] * 0.20, (rs[2] * 0.86, rs[2] * 0.66, (b - a).length * 0.40),
                              z_axis=b - a, x_hint=axis_hint))
    for o in objs:
        GROUPS[o.name] = group or name
    return objs


def build_hand(parts):
    """Returns (hand objects, index finger objects, joints for texturing, pivots)."""
    joints = []   # (position, axis, radius) for creases and colour
    tips = []

    # Middle, ring and little fingers wrap round the front strap; MCP knuckles sit on the right side of the grip.
    hand_objs = []
    mcps = {}
    for name, s, phi0 in (("middle", 0.027, -112), ("ring", 0.046, -114), ("little", 0.063, -118)):
        L1, L2, L3, r = FINGERS[name]
        pts, center, down = wrap_chain(s, (L1, L2, L3), r, phi0)
        mcps[name] = (pts[0], center, down)
        hand_objs += finger_volumes(name, pts, r, center, down)
        for i in range(3):
            joints.append((pts[i], (pts[i + 1] - pts[i]).normalized(), r))
        tips.append((pts[3], (pts[3] - pts[2]).normalized(), r))
        # Fingernail on the outside of the distal phalanx (raised and painted into the skin later).
        nail_c = pts[2].lerp(pts[3], 0.62)
        out = (nail_c - center)
        out = (out - down * out.dot(down)).normalized()
        NAILS.append((pts[2], pts[3], out, r))

    # Index finger: from the top knuckle along the right side of the frame into the trigger guard, the pad of its
    # last joint across the face of the trigger.
    L1, L2, L3, ri = FINGERS["index"]
    c_i, fwd_i, down_i, a_i = grip_section(0.008)
    mcp_i = c_i + fwd_i * (a_i * 0.15) + Vector((0, -(GRIP_W / 2 + ri * 0.9), 0)) + down_i * 0.002
    trig_face_x = TRIGGER_PIVOT.x + 0.0058
    pip_i = Vector((mcp_i.x + 0.0395, -0.0245, -0.0300))
    dip_i = Vector((trig_face_x + ri * 0.95, -0.0105, -0.0330))
    tip_i = Vector((trig_face_x + ri * 0.88, 0.0060, -0.0345))
    # Keep the phalanx lengths (move the joints along their directions).
    pip_i = mcp_i + (pip_i - mcp_i).normalized() * L1
    dip_i = pip_i + (dip_i - pip_i).normalized() * L2
    tip_i = dip_i + (tip_i - dip_i).normalized() * L3
    idx_pts = [mcp_i, pip_i, dip_i, tip_i]
    index_objs = finger_volumes("index", idx_pts, ri, c_i + Vector((0, 0.01, 0)), Vector((0, 0, 1)))
    index_objs.append(ellipsoid("index_knuckle", mcp_i, (ri * 0.98, ri * 0.92, ri * 0.80), z_axis=pip_i - mcp_i, x_hint=(0, 0, 1)))
    GROUPS["index_knuckle"] = "index"
    NAILS.append((dip_i, tip_i, Vector((1.0, 0, 0.35)).normalized(), ri))
    index_joints = [(idx_pts[i], (idx_pts[i + 1] - idx_pts[i]).normalized(), ri) for i in range(3)]
    index_tips = [(tip_i, (tip_i - dip_i).normalized(), ri)]

    # Thumb: from its base in the heel of the hand behind the back strap, up across the web under the beavertail, then
    # forward along the left side of the frame (thumbs-forward), resting on the left thumb.
    T1, T2, T3, rt = THUMB
    cmc = RIGHT_CMC
    t_mcp = cmc + (Vector((-0.0890, 0.0215, -0.0190)) - cmc).normalized() * T1
    t_ip = t_mcp + (Vector((-0.0620, 0.0250, -0.0120)) - t_mcp).normalized() * T2
    t_tip = t_ip + (Vector((-0.0300, 0.0245, -0.0100)) - t_ip).normalized() * T3
    th = [cmc, t_mcp, t_ip, t_tip]
    hand_objs.append(capsule("thumb_meta", cmc, t_mcp, rt * 1.12, rt * 0.97, flat=0.80, width_dir=(0, 0, 1)))
    thumb = [capsule("thumb_prox", t_mcp, t_ip, rt * 0.98, rt * 0.90, flat=0.84, width_dir=(0, 0, 1)),
             capsule("thumb_dist", t_ip, t_tip, rt * 0.88, rt * 0.70, flat=0.80, width_dir=(0, 0, 1)),
             ellipsoid("thumb_joint", t_ip, (rt * 0.92, rt * 0.76, rt * 0.55), z_axis=t_tip - t_ip, x_hint=(0, 0, 1)),
             ellipsoid("thumb_mcp", t_mcp, (rt * 1.02, rt * 0.88, rt * 0.88), z_axis=t_ip - t_mcp, x_hint=(0, 0, 1)),
             ellipsoid("thumb_pad", t_ip.lerp(t_tip, 0.55) + Vector((0, -0.3, -1)).normalized() * rt * 0.22,
                       (rt * 0.82, rt * 0.68, T3 * 0.40), z_axis=t_tip - t_ip, x_hint=(0, 1, 0))]
    for o in thumb:
        GROUPS[o.name] = "thumb"
    hand_objs += thumb
    NAILS.append((t_ip, t_tip, Vector((0, 0.45, 1)).normalized(), rt))
    for i in range(1, 3):
        joints.append((th[i], (th[i + 1] - th[i]).normalized(), rt))
    tips.append((t_tip, (t_tip - t_ip).normalized(), rt))

    # Palm: it wraps the back strap - from the knuckle row (round the right-rear of the grip) behind the strap to the
    # thumb's base at its left-rear, and down to the wrist, which is right behind the grip (in line with the arm, as
    # in a two-handed hold). The thenar pad lies along the thumb's metacarpal over the strap's top-left, the heel of
    # the hand (hypothenar) round its lower right.
    wrist, arm_dir, across, dorsal = RIGHT_WRIST, RIGHT_ARM, RIGHT_ACROSS, RIGHT_DORSAL
    pts = []
    for name in ("middle", "ring", "little"):
        p, center, down = mcps[name]
        out = (p - center)
        out = (out - down * out.dot(down)).normalized()
        r = FINGERS[name][3]
        pts += [p + out * r * 0.9, p - out * r * 0.5, p + down * r * 0.6, p - down * r * 0.6]
    pts += [mcp_i + Vector((0, -ri * 0.9, 0)), mcp_i + Vector((0, ri * 0.4, 0)), mcp_i + Vector((0, 0, ri * 0.8))]
    for d in (-1, 1):
        for t in (-1, 1):
            pts.append(wrist + across * (0.027 * d) + dorsal * (0.011 * t))
    for z in (-0.030, -0.048, -0.066, -0.084):
        xb = back_strap_x(z)
        pts += [Vector((xb - 0.010, -0.013, z)), Vector((xb - 0.012, 0.000, z)), Vector((xb - 0.009, 0.014, z))]
    palm = hull("palm", pts)
    hand_objs.append(palm)
    th_axis = t_mcp - cmc
    hand_objs.append(ellipsoid("thenar", cmc.lerp(t_mcp, 0.42) + Vector((0.004, -0.002, -0.009)), (0.0140, 0.0105, th_axis.length * 0.52),
                               z_axis=th_axis, x_hint=Vector((0, 1, 0))))
    hand_objs.append(ellipsoid("hypothenar", Vector((-0.1135, -0.0175, -0.0805)), (0.0140, 0.0120, 0.0250), z_axis=GRIP_DOWN, x_hint=(0, 1, 0)))
    # The meat of the palm behind the back strap (a hull alone is too thin a wedge there).
    zb = -0.060
    hand_objs.append(ellipsoid("palm_back", Vector((back_strap_x(zb) - 0.0095, 0.0010, zb)), (0.0115, 0.0165, 0.0300),
                               z_axis=GRIP_DOWN, x_hint=(1, 0, 0)))
    # Knuckle bulges of the wrapped fingers, and the wrist and the start of the forearm (the sleeve covers the rest).
    for name in ("middle", "ring", "little"):
        p, center, down = mcps[name]
        out = (p - center)
        out = (out - down * out.dot(down)).normalized()
        r = FINGERS[name][3]
        kn = ellipsoid(f"{name}_knuckle", p + out * r * 0.30, (r * 1.08, r * 1.0, r * 1.0), z_axis=down, x_hint=out)
        GROUPS[kn.name] = name
        hand_objs.append(kn)
        joints.append((p, -out, r, False))   # no crease lines from the knuckle row
    hand_objs.append(capsule("forearm", wrist + arm_dir * 0.004, wrist + arm_dir * FOREARM_STUB, 0.0285, 0.0320, flat=0.74, width_dir=across))
    hand_objs.append(ellipsoid("wrist_bone", wrist + arm_dir * 0.018 + dorsal * 0.012 - across * 0.018, (0.0075, 0.0075, 0.0075)))
    # Tendons over the back of the hand.
    for name in ("middle", "ring", "little"):
        p, center, down = mcps[name]
        out = (p - center)
        out = (out - down * out.dot(down)).normalized()
        hand_objs.append(capsule(f"{name}_tendon", wrist + dorsal * 0.014 - across * 0.004, p + out * FINGERS[name][3] * 0.9, 0.0026, 0.0023,
                                 flat=0.7, width_dir=across))
    pivots = {"FingerKnuckle": list(mcp_i), "Wrist": list(wrist), "ArmDir": list(arm_dir)}
    HAND_NAILS["Hand"] = list(NAILS)
    HAND_NAILS["TriggerFinger"] = list(NAILS)
    return hand_objs, index_objs, joints + index_joints, tips + index_tips, pivots, (wrist, arm_dir, across)


def organic(objs, name, voxel=0.00085, target_faces=24000, smooth=2, subtract=None, carve=None):
    """Unions the volumes into one smooth surface: voxel remesh of the hard union, then every vertex moved onto the
    smooth union of the primitives' distance functions, decimation; optionally pressed against the gun (boolean
    difference with `subtract`). `carve` (a hand object and its primitives' names) presses the flesh against that
    hand softly in the distance field itself, so skin meets skin in a smooth crease instead of a boolean scar."""
    nails = [o for o in objs if o.get("nail")]
    body = [o for o in objs if not o.get("nail")]
    names = [o.name for o in body if o.name in SDF]
    o = join(body, name)
    apply_all(o)
    rm = o.modifiers.new("remesh", 'REMESH')
    rm.mode = 'VOXEL'
    rm.voxel_size = voxel
    rm.adaptivity = 0.0
    apply_all(o)
    if carve:
        # Carve in the field, then remesh the carved surface, so the crease gets even triangles.
        project_to_sdf(o, names, carve=carve[1])
        rm = o.modifiers.new("remesh", 'REMESH')
        rm.mode = 'VOXEL'
        rm.voxel_size = voxel
        rm.adaptivity = 0.0
    sm = o.modifiers.new("smooth", 'SMOOTH')
    sm.factor = 0.5
    sm.iterations = smooth
    apply_all(o)
    project_to_sdf(o, names, carve=carve[1] if carve else None)
    if carve:
        relax_contact(o, carve[1])
    relax_folds(o)
    use_nails(name)
    raise_nails(o)
    faces = len(o.data.polygons)
    if faces > target_faces:
        dec = o.modifiers.new("decimate", 'DECIMATE')
        dec.ratio = target_faces / faces
        apply_all(o)
    if subtract:
        # Press the hand against the gun: subtract each solid part of the gun (slightly inflated) from it.
        for solid in subtract:
            boolean(o, solid)
        apply_all(o)
        # The booleans bring the gun's material slots along; the hand gets its own material afterwards.
        o.data.materials.clear()
        for poly in o.data.polygons:
            poly.material_index = 0
    o.data.shade_smooth()
    return o, nails


# ============================================================================================ support (left) hand
# A two-handed, thumbs-forward hold: the left hand's knuckles sit at the front-left of the grip; its fingers wrap round
# the front of the right hand's fingers (the index pressed up under the trigger guard) and end on the right side, its
# palm covers the left side of the grip over the right fingertips, its heel the left-rear, and its thumb lies forward
# along the left side of the frame, under the right thumb. Both wrists are behind and below the grip; the arms run back
# from them, inside the sleeves, out of the bottom of the view.
GRIP_DOWN = Vector((BACK1.x - BACK0.x, 0, BACK1.y - BACK0.y)).normalized()
FOREARM_STUB = 0.075                                   # skin forearm inside the sleeve
SLEEVE_LENGTH = 0.52                                   # long enough to reach past the camera with the arms out
SLEEVE_START = -0.006                                  # the cuff, over the wrist
RIGHT_WRIST = Vector((-0.1390, -0.0150, -0.0830))
RIGHT_ARM = Vector((-0.78, -0.30, -0.55)).normalized()
RIGHT_CMC = Vector((-0.1220, 0.0000, -0.0520))
SUPPORT_WRIST = Vector((-0.1280, 0.0400, -0.1080))
SUPPORT_ARM = Vector((-0.70, 0.36, -0.62)).normalized()
SUPPORT_CMC = Vector((-0.1100, 0.0300, -0.0700))
SUPPORT_EXTRA = 0.0165                                 # how far out the right hand's fingers stand from the grip
GROUP_K["L_thumb"] = 0.0030
JOIN_K["L_thumb"] = 0.0065


def _ortho(v, axis):
    v = Vector(v)
    return (v - axis * v.dot(axis)).normalized()


RIGHT_ACROSS = _ortho((0.35, 0.05, 0.94), RIGHT_ARM)        # the wrist's width: thumb side up, little-finger side down
RIGHT_DORSAL = RIGHT_ARM.cross(RIGHT_ACROSS).normalized()
if RIGHT_DORSAL.y > 0:
    RIGHT_DORSAL = -RIGHT_DORSAL                              # the back of the right wrist faces right
SUPPORT_ACROSS = _ortho((0.40, -0.05, 0.92), SUPPORT_ARM)
SUPPORT_DORSAL = SUPPORT_ARM.cross(SUPPORT_ACROSS).normalized()
if SUPPORT_DORSAL.y < 0:
    SUPPORT_DORSAL = -SUPPORT_DORSAL                          # the back of the left wrist faces left


def back_strap_x(z):
    """x of the back strap at height z."""
    t = (z - BACK0.y) / (BACK1.y - BACK0.y)
    return BACK0.x + (BACK1.x - BACK0.x) * t


def wrap_chain_around(s, lengths, radius, phi0, extra=SUPPORT_EXTRA, press=0.9):
    """Like wrap_chain, but round the grip and the right hand's fingers on it (`extra` further out), starting at phi0
    (degrees: 0 = front, +90 = left side) and wrapping round the front towards the right (the angle decreasing)."""
    c, fwd, down, a = grip_section(s)
    side = Vector((0, 1, 0))
    ra, rb = a + extra + radius * press, GRIP_W / 2 + extra + radius * press

    def q(phi):
        return c + fwd * (ra * math.cos(phi)) + side * (rb * math.sin(phi))

    phi = math.radians(phi0)
    pts = [q(phi)]
    for L in lengths:
        lo, hi = phi - math.pi, phi
        for _ in range(40):
            mid = (lo + hi) / 2
            if (q(mid) - pts[-1]).length < L:
                hi = mid
            else:
                lo = mid
        phi = (lo + hi) / 2
        pts.append(q(phi))
    return pts, c, down


def build_support_hand():
    """Returns (objects, joints, tips, pivots) of the left hand."""
    objs, joints, tips = [], [], []
    mcps = {}
    first_nail = len(NAILS)
    for name, s, phi0 in (("index", 0.024, 64), ("middle", 0.041, 60), ("ring", 0.057, 56), ("little", 0.071, 53)):
        L1, L2, L3, r = FINGERS[name]
        pts, center, down = wrap_chain_around(s, (L1, L2, L3), r, phi0)
        mcps[name] = (pts[0], center, down)
        objs += finger_volumes("L_" + name, pts, r, center, down)
        for i in range(3):
            joints.append((pts[i], (pts[i + 1] - pts[i]).normalized(), r))
        tips.append((pts[3], (pts[3] - pts[2]).normalized(), r))
        nail_c = pts[2].lerp(pts[3], 0.62)
        out = nail_c - center
        out = (out - down * out.dot(down)).normalized()
        NAILS.append((pts[2], pts[3], out, r))

    # Thumb: from the heel of the hand forward along the left side of the frame, just under the right thumb, its tip
    # over the front of the trigger guard.
    T1, T2, T3, rt = THUMB
    cmc = SUPPORT_CMC
    t_mcp = cmc + (Vector((-0.0760, 0.0350, -0.0440)) - cmc).normalized() * T1
    t_ip = t_mcp + (Vector((-0.0460, 0.0255, -0.0300)) - t_mcp).normalized() * T2
    t_tip = t_ip + (Vector((-0.0160, 0.0235, -0.0285)) - t_ip).normalized() * T3
    th = [cmc, t_mcp, t_ip, t_tip]
    thumb = [capsule("L_thumb_meta", cmc, t_mcp, rt * 1.12, rt * 0.97, flat=0.80, width_dir=(0, 0, 1)),
             capsule("L_thumb_prox", t_mcp, t_ip, rt * 0.98, rt * 0.90, flat=0.84, width_dir=(0, 0, 1)),
             capsule("L_thumb_dist", t_ip, t_tip, rt * 0.88, rt * 0.70, flat=0.80, width_dir=(0, 0, 1)),
             ellipsoid("L_thumb_joint", t_ip, (rt * 0.92, rt * 0.76, rt * 0.55), z_axis=t_tip - t_ip, x_hint=(0, 0, 1)),
             ellipsoid("L_thumb_mcp", t_mcp, (rt * 1.02, rt * 0.88, rt * 0.88), z_axis=t_ip - t_mcp, x_hint=(0, 0, 1)),
             ellipsoid("L_thumb_pad", t_ip.lerp(t_tip, 0.55) + Vector((0, -1, -0.15)).normalized() * rt * 0.22,
                       (rt * 0.82, rt * 0.68, T3 * 0.40), z_axis=t_tip - t_ip, x_hint=(0, 0, 1))]
    for o in thumb:
        GROUPS[o.name] = "L_thumb"
    objs += thumb
    NAILS.append((t_ip, t_tip, Vector((0, 0.7, 0.7)).normalized(), rt))
    for i in range(1, 3):
        joints.append((th[i], (th[i + 1] - th[i]).normalized(), rt))
    tips.append((t_tip, (t_tip - t_ip).normalized(), rt))

    # Palm: from the knuckle row (at the front-left, outside the right hand's fingers) back along the left side of the
    # grip to the wrist, behind and below it; the thenar along the thumb's metacarpal, the heel at the left-rear.
    wrist, arm_dir, across, dorsal = SUPPORT_WRIST, SUPPORT_ARM, SUPPORT_ACROSS, SUPPORT_DORSAL
    pts = []
    for name in ("index", "middle", "ring", "little"):
        p, center, down = mcps[name]
        out = p - center
        out = (out - down * out.dot(down)).normalized()
        r = FINGERS[name][3]
        pts += [p + out * r * 0.9, p - out * r * 0.5, p + down * r * 0.6, p - down * r * 0.6]
    for d in (-1, 1):
        for t in (-1, 1):
            pts.append(wrist + across * (0.027 * d) + dorsal * (0.011 * t))
    # The palm's face against the left of the grip, and the back of the hand.
    for x, z in ((-0.062, -0.055), (-0.088, -0.050), (-0.066, -0.092), (-0.094, -0.096)):
        pts += [Vector((x, 0.0375, z)), Vector((x - 0.004, 0.0580, z))]
    for z in (-0.056, -0.074, -0.092):
        xb = back_strap_x(z)
        pts += [Vector((xb - 0.012, 0.016, z)), Vector((xb - 0.008, 0.025, z)), Vector((xb - 0.018, 0.034, z))]
    objs.append(hull("L_palm", pts))
    th_axis = t_mcp - cmc
    objs.append(ellipsoid("L_thenar", cmc.lerp(t_mcp, 0.42) + Vector((0.002, -0.007, -0.008)), (0.0140, 0.0105, th_axis.length * 0.52),
                          z_axis=th_axis, x_hint=Vector((0, 0, 1))))
    little = mcps["little"][0]
    objs.append(ellipsoid("L_hypothenar", wrist.lerp(little, 0.42) + Vector((0, -0.006, 0)), (0.0140, 0.0115, 0.0250),
                          z_axis=little - wrist, x_hint=(0, 1, 0)))
    # The heel of the hand round the left-rear corner of the grip, against the right palm.
    zb = -0.074
    objs.append(ellipsoid("L_heel", Vector((back_strap_x(zb) - 0.0080, 0.0270, zb)), (0.0125, 0.0120, 0.0260),
                          z_axis=GRIP_DOWN, x_hint=(1, 0, 0)))
    for name in ("index", "middle", "ring", "little"):
        p, center, down = mcps[name]
        out = p - center
        out = (out - down * out.dot(down)).normalized()
        r = FINGERS[name][3]
        kn = ellipsoid(f"L_{name}_knuckle", p + out * r * 0.30, (r * 1.08, r * 1.0, r * 1.0), z_axis=down, x_hint=out)
        GROUPS[kn.name] = "L_" + name
        objs.append(kn)
        joints.append((p, -out, r, False))
        objs.append(capsule(f"L_{name}_tendon", wrist + dorsal * 0.014, p + out * r * 0.9, 0.0026, 0.0023, flat=0.7, width_dir=across))
    objs.append(capsule("L_forearm", wrist + arm_dir * 0.004, wrist + arm_dir * FOREARM_STUB, 0.0280, 0.0315, flat=0.74, width_dir=across))
    objs.append(ellipsoid("L_wrist_bone", wrist + arm_dir * 0.018 + dorsal * 0.012 - across * 0.018, (0.0075, 0.0075, 0.0075)))

    pivots = {"SupportWrist": list(wrist), "SupportArmDir": list(arm_dir)}
    HAND_NAILS["HandLeft"] = NAILS[first_nail:]
    return objs, joints, tips, pivots


def press_copy(src, name, inflate):
    """A hidden, slightly inflated copy of a mesh, for pressing another one against it (boolean difference)."""
    c = src.copy()
    c.data = src.data.copy()
    c.name = name
    bpy.context.scene.collection.objects.link(c)
    c.modifiers.clear()
    sh = c.modifiers.new("inflate", 'DISPLACE')
    sh.strength = inflate
    apply_all(c)
    c.data.materials.clear()
    c["cutter"] = True
    c.hide_set(True)
    return c


def build_support(right_hand, press, preview=False, right_names=None):
    """The left hand (pressed against the gun's solids and the right hand) and both sleeves. With preview, the left
    hand gets the live skin shader instead of a baked texture set. Returns (left hand, sleeves, pivots)."""
    rc = press_copy(right_hand, "press_right_hand", 0.0006)
    print(f"[pistol] pressing the support hand against {len(press) + 1} solids", flush=True)
    objs, joints, tips, pivots = build_support_hand()
    if right_names:
        hand, nails = organic(objs, "HandLeft", subtract=press, carve=(rc, right_names))
    else:   # (-- support: the right hand's primitives aren't rebuilt, so a plain boolean)
        hand, nails = organic(objs, "HandLeft", subtract=press + [rc])
    bpy.data.objects.remove(rc, do_unlink=True)
    sleeves = [build_sleeve(RIGHT_WRIST, RIGHT_ARM, RIGHT_ACROSS, name="Sleeve"),
               build_sleeve(SUPPORT_WRIST, SUPPORT_ARM, SUPPORT_ACROSS, name="SleeveLeft")]
    if preview:
        skin_attributes(hand, joints, tips)
        m = skin_bake_material("skin_prev_left")
        nt = m.node_tree
        nt.links.new(nt.nodes[m["bsdf"]].outputs[0], nt.nodes[m["out"]].inputs["Surface"])
        hand.data.materials.clear()
        hand.data.materials.append(m)
        for poly in hand.data.polygons:
            poly.material_index = 0
        return hand, K.finish(sleeves), pivots, nails
    hand.data.materials.append(mat("tex_hand_left", None, 0.5, 0.0))
    unwrap(hand)
    bake_skin(hand, "hand_left", joints, tips, 2048)
    nail_mat = bpy.data.materials.get("flat_nail") or mat("flat_nail", (0.86, 0.70, 0.66), 0.28, 0.0)
    for n in nails:
        apply_all(n)
        n.data.materials.append(nail_mat)
        n.data.shade_smooth()
    if nails:
        hand = join([hand] + nails, "HandLeft")
    return hand, K.finish(sleeves), pivots, []


def rebuild_support():
    """`-- support`: re-makes the left (support) hand and both sleeves on the saved Assets/pistol.blend and
    re-exports, leaving the right hand as it is."""
    path = os.path.join(K.PROJECT, "Assets", "pistol.blend")
    bpy.ops.wm.open_mainfile(filepath=path)
    for o in list(bpy.data.objects):
        if o.name.split(".")[0] in ("HandLeft", "SleeveLeft", "Sleeve") or o.name.startswith("L_"):
            bpy.data.objects.remove(o, do_unlink=True)
    press = [o for o in bpy.data.objects if o.name.startswith("gun_solid_")]
    hand, sleeves, pivots, _ = build_support(bpy.data.objects["Hand"], press)
    export = [o for o in (bpy.data.objects.get(n) for n in GUN_PARTS + ("Magazine", "MagazineFollower", "MagazineRound"))
              if o is not None and o.name != "Sleeve"]
    for o in export:
        o.hide_set(False)
    export += [hand] + sleeves
    bpy.ops.wm.save_as_mainfile(filepath=path)
    K.export("pistol", export)
    save_pivots(pivots)
    print("[pistol] support hand rebuilt", flush=True)


def save_pivots(pivots):
    piv_path = os.path.join(K.PREVIEWS, "pistol_pivots.json")
    data = json.load(open(piv_path)) if os.path.exists(piv_path) else {}
    for k in ("SupportPalm", "SupportPalmNormal", "SupportFingers"):
        data.pop(k, None)
    data.update(pivots)
    with open(piv_path, "w") as f:
        json.dump(data, f, indent=2)


# The game's view of the gun (Game/Weapon.cs: HipPosition / HipYaw / AimPosition, about the rear sight's top), so the
# previews show what you see in the game: camera-space offsets (x right, y up, z back).
SIGHT = Vector((SLIDE_X0 + 0.010, 0.0, SLIDE_ZT + 0.005))
GAME_VIEWS = {"hip": ((0.025, -0.048, -0.270), 0.04), "aim": ((0.0, 0.0, -0.265), 0.0)}


def game_camera_pose(name):
    """Position and forward direction (gun space) of the game camera for a view in GAME_VIEWS."""
    (px, py, pz), yaw = GAME_VIEWS[name]
    # Game camera space -> gun (Blender) space with the muzzle turned in by yaw: camera forward is the gun's +X turned
    # towards the gun's right (-Y) by yaw; camera right is -Y turned likewise.
    fwd = Vector((math.cos(yaw), -math.sin(yaw), 0))
    right = Vector((-math.sin(yaw), -math.cos(yaw), 0))
    up = Vector((0, 0, 1))
    pos = SIGHT - right * px - up * py + fwd * pz
    return pos, fwd


def preview_two_hands(tag="th"):
    """Renders the two-handed hold from the game's hip and aiming views and from round about (no baking)."""
    for o in bpy.data.objects:
        if o.get("cutter") or o.name.startswith("cut_") or o.name.startswith("gun_solid") or o.name.startswith("press_"):
            o.hide_set(True)
            o.hide_render = True
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        prefs.compute_device_type = 'OPTIX'
        prefs.get_devices()
        for d in prefs.devices:
            d.use = d.type != 'CPU'
        scene.cycles.device = 'GPU'
    except Exception:
        pass
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    scene.view_settings.view_transform = 'AgX'
    world = bpy.data.worlds.new("prev")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.55, 0.62, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    sun = bpy.data.lights.new("sun", 'SUN'); sun.energy = 3.5; sun.angle = math.radians(3)
    so = bpy.data.objects.new("sun", sun); scene.collection.objects.link(so)
    so.rotation_euler = (math.radians(40), math.radians(-20), math.radians(120))
    cam = view_model_camera()
    scene.camera = cam
    for n in filter(None, os.environ.get("PISTOL_HIDE", "").split(",")):   # debugging: hide these objects
        for o in bpy.data.objects:
            if o.name.split(".")[0] == n:
                o.hide_render = True
    views = []
    for name in ("hip", "aim"):
        pos, fwd = game_camera_pose(name)
        views.append((f"{tag}_{name}", pos, pos + fwd, 80, (1280, 720)))
    target = Vector((-0.08, 0.005, -0.06))
    views += [(f"{tag}_back", Vector((-0.42, 0.02, 0.02)), target, 26, (900, 700)),
              (f"{tag}_left", Vector((-0.08, 0.38, -0.02)), target, 30, (900, 700)),
              (f"{tag}_right", Vector((-0.09, -0.38, -0.03)), target, 30, (900, 700)),
              (f"{tag}_below", Vector((-0.05, 0.10, -0.40)), target, 30, (900, 700)),
              (f"{tag}_front", Vector((0.30, 0.10, -0.12)), target, 30, (900, 700))]
    for name, pos, look, fov, res in views:
        cam.location = pos
        cam.rotation_euler = (look - pos).to_track_quat('-Z', 'Y').to_euler()
        cam.data.angle = math.radians(fov)
        scene.render.resolution_x, scene.render.resolution_y = res
        scene.render.filepath = os.path.join(K.PREVIEWS, name + ".png")
        bpy.ops.render.render(write_still=True)
    print("[pistol] two-hand previews done", flush=True)


# ============================================================================================ hand texture
def unwrap(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(55), island_margin=0.006, area_weight=0.0, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')


def skin_attributes(o, joints, tips):
    """Per-vertex fields the skin shader uses: distance along the finger from the nearest joint (for crease lines),
    how close to a joint (knuckle redness, crease strength) and to a fingertip."""
    me = o.data
    use_nails(o.name.split(".")[0])
    co = np.array([v.co for v in me.vertices])
    jp = np.array([j[0] for j in joints])
    ja = np.array([j[1] for j in joints])
    jr = np.array([j[2] for j in joints])
    d = np.linalg.norm(co[:, None, :] - jp[None, :, :], axis=2)
    k = np.argmin(d, axis=1)
    near = d[np.arange(len(co)), k]
    axial = np.einsum('ij,ij->i', co - jp[k], ja[k])
    knuckle = np.exp(-(near / (jr[k] * 1.35)) ** 2)
    # Crease lines only round the finger and thumb joints (a joint flagged False, the knuckle row, gets none),
    # and only close in: further out the band would slice across the palm.
    creases = np.array([j[3] if len(j) > 3 else True for j in joints], dtype=float)
    crease_w = np.exp(-(near / (jr[k] * 1.05)) ** 2) ** 2 * creases[k]
    tp = np.array([t[0] for t in tips])
    td = np.linalg.norm(co[:, None, :] - tp[None, :, :], axis=2).min(axis=1)
    tip = np.exp(-(td / 0.014) ** 2)
    nrm = np.array([v.normal for v in me.vertices])
    nail_e, nail_along = nail_fields(co, nrm)
    for name, values in (("axial", axial), ("knuckle", knuckle), ("crease_w", crease_w), ("tip", tip), ("nail_e", nail_e * 1000.0),
                         ("nail_along", nail_along)):
        if name in me.attributes:
            me.attributes.remove(me.attributes[name])
        a = me.attributes.new(name, 'FLOAT', 'POINT')
        a.data.foreach_set("value", values.astype(np.float32))


def skin_bake_material(name):
    """The skin shader used for baking (Cycles): colour from the attributes plus mottling; height for creases and pores."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    N = nt.nodes.new
    L = nt.links.new

    def attr(n):
        a = N("ShaderNodeAttribute"); a.attribute_type = 'GEOMETRY'; a.attribute_name = n
        return a.outputs["Fac"]

    def math_(op, a, b=None, c=None, clamp=False):
        n = N("ShaderNodeMath"); n.operation = op; n.use_clamp = clamp
        for i, v in enumerate((a, b, c)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                L(v, n.inputs[i])
        return n.outputs[0]

    def noise(scale, detail=4.0, rough=0.55, coord=None):
        n = N("ShaderNodeTexNoise"); n.inputs["Scale"].default_value = scale; n.inputs["Detail"].default_value = detail
        n.inputs["Roughness"].default_value = rough
        if coord is not None:
            L(coord, n.inputs["Vector"])
        return n.outputs["Factor"]

    def rgb(c):
        n = N("ShaderNodeRGB"); n.outputs[0].default_value = (*K.srgb_to_linear(c), 1.0)
        return n.outputs[0]

    def mix(fac, a, b, blend='MIX'):
        n = N("ShaderNodeMix"); n.data_type = 'RGBA'; n.blend_type = blend; n.clamp_factor = True
        if isinstance(fac, (int, float)):
            n.inputs["Factor"].default_value = fac
        else:
            L(fac, n.inputs["Factor"])
        L(a, n.inputs[6]); L(b, n.inputs[7])
        return n.outputs[2]

    pos = N("ShaderNodeTexCoord").outputs["Object"]
    knuckle, tip, axial, crease_w = attr("knuckle"), attr("tip"), attr("axial"), attr("crease_w")
    nail_e, nail_along = attr("nail_e"), attr("nail_along")      # nail_e in millimetres
    nail = math_('MULTIPLY_ADD', nail_e, 1.0 / 0.16, 0.5, clamp=True)
    # Colour: lightly tanned skin, pinker over the knuckles and fingertips, mottled, a few faint freckles.
    base = mix(noise(55.0, 5, 0.6, pos), rgb((0.75, 0.57, 0.47)), rgb((0.80, 0.62, 0.52)))
    red = math_('ADD', math_('MULTIPLY', knuckle, 0.65), math_('MULTIPLY', tip, 0.75), clamp=True)
    red = math_('MULTIPLY', red, math_('MULTIPLY_ADD', noise(140.0, 3, 0.5, pos), 0.6, 0.55))
    col = mix(red, base, rgb((0.78, 0.47, 0.40)))
    blotch = math_('MULTIPLY', noise(22.0, 6, 0.6, pos), 0.22)
    col = mix(blotch, col, rgb((0.70, 0.47, 0.40)))
    vor = N("ShaderNodeTexVoronoi"); vor.inputs["Scale"].default_value = 900.0; L(pos, vor.inputs["Vector"])
    freckle = math_('MULTIPLY', math_('LESS_THAN', vor.outputs["Distance"], 0.06), math_('GREATER_THAN', noise(40.0, 2, 0.5, pos), 0.60))
    col = mix(math_('MULTIPLY', freckle, 0.30), col, rgb((0.58, 0.38, 0.28)))
    # Crease lines across the joints (stronger right at the joint), pores, fine skin texture.
    wave = math_('SINE', math_('MULTIPLY', math_('ADD', axial, math_('MULTIPLY', noise(260.0, 2, 0.5, pos), 0.0011)), 2 * math.pi / 0.0024))
    band = math_('MULTIPLY', crease_w, math_('EXPONENT', math_('MULTIPLY', math_('MULTIPLY', axial, axial), -1.0 / (0.0030 ** 2))))
    crease = math_('MULTIPLY', math_('POWER', math_('ABSOLUTE', wave), 3.0), band)
    col = mix(math_('MULTIPLY', crease, 0.30), col, rgb((0.52, 0.34, 0.28)))
    # Nails: pink over the nail bed, a pale lunula at the cuticle, whiter free edge, a thin dark cuticle line.
    nail_col = mix(math_('MULTIPLY', math_('LESS_THAN', nail_along, 0.15), 0.35), rgb((0.84, 0.64, 0.60)), rgb((0.88, 0.76, 0.72)))
    nail_col = mix(math_('MULTIPLY', math_('GREATER_THAN', nail_along, 0.90), 0.6), nail_col, rgb((0.90, 0.84, 0.78)))
    edge = math_('EXPONENT', math_('MULTIPLY', math_('MULTIPLY', nail_e, nail_e), -1.0 / (0.12 ** 2)))
    col = mix(nail, col, nail_col)
    col = mix(math_('MULTIPLY', edge, 0.35), col, rgb((0.58, 0.40, 0.35)))
    pores = N("ShaderNodeTexVoronoi"); pores.inputs["Scale"].default_value = 2600.0; L(pos, pores.inputs["Vector"])
    pore = math_('MULTIPLY', math_('LESS_THAN', pores.outputs["Distance"], 0.12), math_('SUBTRACT', 1.0, nail))
    height = math_('SUBTRACT', math_('ADD', math_('MULTIPLY', noise(1800.0, 2, 0.5, pos), math_('MULTIPLY_ADD', nail, -0.2, 0.25)), 0.5),
                   math_('ADD', math_('MULTIPLY', crease, 0.6), math_('ADD', math_('MULTIPLY', pore, 0.2), math_('MULTIPLY', edge, 0.25))))
    bump = N("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.55; bump.inputs["Distance"].default_value = 0.00035
    L(height, bump.inputs["Height"])
    # Roughness: skin 0.45-0.6 (rougher in the creases), shinier over the knuckles' stretched skin, nails glossy.
    rough = math_('ADD', 0.50, math_('MULTIPLY', noise(90.0, 3, 0.5, pos), 0.10))
    rough = math_('ADD', rough, math_('SUBTRACT', math_('MULTIPLY', crease, 0.15), math_('MULTIPLY', knuckle, 0.08)))
    rough = math_('ADD', math_('MULTIPLY', rough, math_('SUBTRACT', 1.0, nail)), math_('MULTIPLY', nail, 0.22), clamp=True)
    bsdf = N("ShaderNodeBsdfPrincipled")
    L(col, bsdf.inputs["Base Color"])
    L(bump.outputs["Normal"], bsdf.inputs["Normal"])
    em = N("ShaderNodeEmission")
    L(col, em.inputs["Color"])
    em_r = N("ShaderNodeEmission")
    L(rough, em_r.inputs["Color"])
    out = N("ShaderNodeOutputMaterial")
    m["bsdf"] = bsdf.name
    m["emit"] = em.name
    m["emit_rough"] = em_r.name
    m["out"] = out.name
    return m


def bake_skin(o, set_name, joints, tips, res=2048):
    """Bakes the hand's own texture set: albedo (EMIT), tangent normal (NORMAL, with the crease/pore bump), AO."""
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
    scene.render.bake.margin = 12
    # Ambient occlusion only from what's close (creases, fingers against each other and the gun), and never from the
    # sleeves round the wrists: otherwise half the hand bakes dark.
    if scene.world is None:
        scene.world = bpy.data.worlds.new("bake")
    scene.world.light_settings.distance = 0.015
    hidden = [x for x in bpy.data.objects if x.name.split(".")[0] in ("Sleeve", "SleeveLeft") and not x.hide_render]
    for x in hidden:
        x.hide_render = True
    skin_attributes(o, joints, tips)
    m = skin_bake_material("skin_bake")
    keep = list(o.data.materials)
    o.data.materials.clear()
    o.data.materials.append(m)
    nt = m.node_tree
    img = bpy.data.images.new("bake_" + set_name, res, res, alpha=False, float_buffer=True)
    img.colorspace_settings.name = 'Non-Color'
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.nodes.active = tex
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o

    def grab():
        a = np.empty(res * res * 4, np.float32)
        img.pixels.foreach_get(a)
        return a.reshape(res, res, 4)[..., :3].copy()

    out = nt.nodes[m["out"]]
    nt.links.new(nt.nodes[m["emit"]].outputs[0], out.inputs["Surface"])
    scene.cycles.samples = 8
    bpy.ops.object.bake(type='EMIT', margin=12)
    albedo = grab()
    nt.links.new(nt.nodes[m["emit_rough"]].outputs[0], out.inputs["Surface"])
    bpy.ops.object.bake(type='EMIT', margin=12)
    rough = grab()[..., 0]
    nt.links.new(nt.nodes[m["bsdf"]].outputs[0], out.inputs["Surface"])
    bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT', margin=12)
    normal = grab()
    scene.cycles.samples = 64
    bpy.ops.object.bake(type='AO', margin=12)
    ao = grab()[..., 0]
    srgb = lambda x: np.where(np.clip(x, 0, 1) <= 0.0031308, np.clip(x, 0, 1) * 12.92, 1.055 * np.power(np.clip(x, 0, 1), 1 / 2.4) - 0.055)
    ones = np.ones((res, res), np.float32)

    def save(name, arr):
        im = bpy.data.images.new(name, res, res, alpha=True)
        im.colorspace_settings.name = 'Non-Color'
        im.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
        im.filepath_raw = os.path.join(TEX, name + ".png")
        im.file_format = 'PNG'
        im.save()
        bpy.data.images.remove(im)

    for x in hidden:
        x.hide_render = False
    save(set_name + "_albedo", np.dstack([srgb(albedo), np.zeros((res, res))]))
    save(set_name + "_normal", np.dstack([normal, ones]))
    save(set_name + "_orm", np.dstack([np.clip(0.35 + 0.65 * ao, 0, 1), np.clip(rough, 0.15, 0.8), np.zeros((res, res)), ones]))
    o.data.materials.clear()
    for mm in keep:
        o.data.materials.append(mm)
    print(f"[pistol] baked skin set {set_name}", flush=True)


# ============================================================================================ sleeve
def build_sleeve(wrist, arm_dir, across, length=SLEEVE_LENGTH, name="Sleeve"):
    """A fleece sleeve: a soft tube from just past the wrist back up the arm, its cuff snug round the wrist, widening
    up the forearm, with loose folds and a rolled edge at the cuff. Its colour is the game's (cloth_tint)."""
    sleeve_mat = mat("fleece_tint", (0.52, 0.63, 0.78), 0.95, 0.0)
    start = wrist + arm_dir * SLEEVE_START
    bm = bmesh.new()
    segs, rings = 40, 30
    up = across
    side = arm_dir.cross(up).normalized()
    up = side.cross(arm_dir).normalized()
    ring_v = []
    for j in range(rings + 1):
        t = j / rings
        L = length * t
        c = start + arm_dir * L
        # Snug at the cuff (round the 3 cm forearm), widening to the elbow and on up the upper arm.
        r = 0.0360 + 0.0150 * min(L / 0.26, 1.0) + 0.0080 * max(0.0, (L - 0.26) / max(length - 0.26, 1e-3))
        ring = []
        for i in range(segs):
            a = math.tau * i / segs
            fold = (0.0030 * math.sin(a * 5 + t * 9) * math.sin(t * 13 + i * 0.7) + 0.0022 * math.sin(L * 110 + a * 2)) * min(L / 0.05, 1.0)
            cuff = 0.0035 * math.exp(-((L - 0.010) / 0.008) ** 2)      # the rolled edge of the cuff
            rr = r + fold + cuff
            ring.append(bm.verts.new(c + side * (math.cos(a) * rr * 0.86) + up * (math.sin(a) * rr)))
        ring_v.append(ring)
    for r0, r1 in zip(ring_v, ring_v[1:]):
        for i in range(segs):
            bm.faces.new((r0[i], r0[(i + 1) % segs], r1[(i + 1) % segs], r1[i]))
    # Inside of the cuff (so you never see through the open end).
    inner = []
    for i in range(segs):
        a = math.tau * i / segs
        inner.append(bm.verts.new(start + arm_dir * 0.012 + side * (math.cos(a) * 0.030 * 0.86) + up * (math.sin(a) * 0.030)))
    cap = set()
    for i in range(segs):
        cap.add(bm.faces.new((ring_v[0][(i + 1) % segs], ring_v[0][i], inner[i], inner[(i + 1) % segs])))
    # Point every face of the tube outwards (away from the arm's axis); the cuff's end ring faces back towards the
    # hand.
    bm.normal_update()
    for f in bm.faces:
        c = f.calc_center_median()
        rel = c - start
        radial = rel - arm_dir * rel.dot(arm_dir)
        want = -arm_dir if f in cap else radial
        if f.normal.dot(want) < 0:
            f.normal_flip()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(sleeve_mat)
    o["uv"] = "box"
    o["smooth"] = True
    o["smooth_angle"] = 180.0
    s = o.modifiers.new("subsurf", 'SUBSURF')
    s.levels = 1
    return o


# ============================================================================================ preview
def view_model_camera():
    """The game's view-model camera, in gun space (see Weapon.PistolToCamera): 80 degree vertical FOV, 16:9."""
    cam_data = bpy.data.cameras.new("view")
    cam_data.sensor_fit = 'VERTICAL'
    cam_data.angle = math.radians(80)
    cam_data.clip_start = 0.01
    cam = bpy.data.objects.new("view", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    pos = Vector((-0.33978, 0.17721, 0.0545))
    fwd = Vector((0.98007, -0.19867, 0.0))
    cam.matrix_world = Matrix.Translation(pos) @ fwd.to_track_quat('-Z', 'Y').to_matrix().to_4x4()
    return cam


def render_views(objs):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.view_settings.view_transform = 'AgX'
    world = bpy.data.worlds.new("prev")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.55, 0.62, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
    sun = bpy.data.lights.new("sun", 'SUN'); sun.energy = 3.0; sun.angle = math.radians(3)
    so = bpy.data.objects.new("sun", sun); scene.collection.objects.link(so)
    so.rotation_euler = (math.radians(40), math.radians(-20), math.radians(120))
    os.makedirs(K.PREVIEWS, exist_ok=True)
    cam = view_model_camera()
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 1280, 720
    scene.render.filepath = os.path.join(K.PREVIEWS, "pistol_view.png")
    bpy.ops.render.render(write_still=True)
    # A close side view (left) and one from the right.
    for name, pos, look in (("pistol_left", (-0.01, 0.42, -0.03), (-0.01, 0, -0.03)), ("pistol_right", (-0.02, -0.42, -0.03), (-0.02, 0, -0.03))):
        cam.location = pos
        cam.rotation_euler = (Vector(look) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
        cam.data.angle = math.radians(30)
        scene.render.resolution_x, scene.render.resolution_y = 1000, 760
        scene.render.filepath = os.path.join(K.PREVIEWS, name + ".png")
        bpy.ops.render.render(write_still=True)


# ============================================================================================ main
def preview_hand(parts, hand, finger, joints, tips):
    """Fast iteration: the skin shader live (not baked), close renders of the hand from the game camera and around it."""
    for o in (hand, finger):
        skin_attributes(o, joints, tips)
        m = skin_bake_material("skin_prev_" + o.name)
        nt = m.node_tree
        nt.links.new(nt.nodes[m["bsdf"]].outputs[0], nt.nodes[m["out"]].inputs["Surface"])
        o.data.materials.clear()
        o.data.materials.append(m)
        for poly in o.data.polygons:
            poly.material_index = 0
    for o in bpy.data.objects:
        if o.get("cutter") or o.name.startswith("cut_") or o.name.startswith("gun_solid"):
            o.hide_set(True)
            o.hide_render = True
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 32
    scene.cycles.use_denoising = True
    scene.view_settings.view_transform = 'AgX'
    world = bpy.data.worlds.new("prev")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.55, 0.62, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    sun = bpy.data.lights.new("sun", 'SUN'); sun.energy = 3.5; sun.angle = math.radians(3)
    so = bpy.data.objects.new("sun", sun); scene.collection.objects.link(so)
    so.rotation_euler = (math.radians(40), math.radians(-20), math.radians(120))
    cam = view_model_camera()
    scene.camera = cam
    target = Vector((-0.075, 0.0, -0.040))
    views = [("hand_game", Vector((-0.33978, 0.17721, 0.0545)), 30),
             ("hand_left", Vector((-0.07, 0.30, 0.02)), 26),
             ("hand_right", Vector((-0.10, -0.30, 0.03)), 30),
             ("hand_back", Vector((-0.34, -0.06, 0.12)), 30),
             ("hand_front", Vector((0.20, 0.10, -0.10)), 30)]
    scene.render.resolution_x, scene.render.resolution_y = 900, 700
    for name, pos, fov in views:
        cam.location = pos
        cam.rotation_euler = (target - pos).to_track_quat('-Z', 'Y').to_euler()
        cam.data.angle = math.radians(fov)
        scene.render.filepath = os.path.join(K.PREVIEWS, name + ".png")
        bpy.ops.render.render(write_still=True)
    print("[pistol] hand previews done", flush=True)


GUN_PARTS = ("Slide", "RearSight", "FrontSight", "Barrel", "BarrelHood", "Frame", "Grip", "TriggerGuard", "Trigger",
             "SlideStop", "MagazineBase", "Hand", "TriggerFinger", "Sleeve")


def rebuild_magazine():
    """`-- magazine`: adds the magazine parts to the saved Assets/pistol.blend and re-exports pistol.glb, leaving the
    hand and its baked skin exactly as they are (a full build re-poses and re-bakes the hand)."""
    path = os.path.join(K.PROJECT, "Assets", "pistol.blend")
    bpy.ops.wm.open_mainfile(filepath=path)
    for name in ("Magazine", "MagazineFollower", "MagazineRound"):
        o = bpy.data.objects.get(name)
        if o is not None:
            bpy.data.objects.remove(o, do_unlink=True)
    for o in list(bpy.data.objects):
        if o.name.split(".")[0] in ("lip", "spine", "round_case", "round_tip"):
            bpy.data.objects.remove(o, do_unlink=True)
    export = []
    missing = []
    for name in GUN_PARTS:
        o = bpy.data.objects.get(name)
        if o is None:
            missing.append(name)
        else:
            o.hide_set(False)
            export.append(o)
    if missing:
        raise RuntimeError(f"pistol.blend lacks {missing}: run the full build instead")
    for name, objs in build_magazine().items():
        objs = K.finish(objs)
        o = join(objs, name) if len(objs) > 1 else objs[0]
        o.name = name
        export.append(o)
    bpy.ops.wm.save_as_mainfile(filepath=path)
    K.export("pistol", export)
    print("[pistol] magazine added", flush=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "magazine" in argv:
        rebuild_magazine()
        return
    if "support" in argv:
        rebuild_support()
        return
    K.reset()
    parts = build_pistol()

    # Solids of the gun for pressing the hands against (copies with their cuts applied, inflated a fraction).
    press = []
    for name in ("Frame", "Grip", "TriggerGuard", "Slide", "MagazineBase", "Trigger"):
        src = parts[name][0]
        c = src.copy()
        c.data = src.data.copy()
        c.name = "gun_solid_" + name
        bpy.context.scene.collection.objects.link(c)
        apply_all(c)
        sh = c.modifiers.new("inflate", 'DISPLACE')
        sh.strength = 0.0008
        apply_all(c)
        c["cutter"] = True
        c.hide_set(True)
        press.append(c)

    preview = "preview" in argv
    hand_objs, index_objs, joints, tips, pivots, arm = build_hand(parts)
    right_names = [o.name for o in hand_objs + index_objs if o.name in SDF]
    nail_mat = mat("flat_nail", (0.86, 0.70, 0.66), 0.28, 0.0)
    hand, hand_nails = organic(hand_objs, "Hand", subtract=press)
    finger, finger_nails = organic(index_objs, "TriggerFinger", target_faces=9000, subtract=press)
    left, sleeves, support_pivots, left_nails = build_support(hand, press, preview=preview, right_names=right_names)
    pivots.update(support_pivots)
    if preview:
        for o in (hand, finger):
            skin_attributes(o, joints, tips)
            m = skin_bake_material("skin_prev_" + o.name)
            nt = m.node_tree
            nt.links.new(nt.nodes[m["bsdf"]].outputs[0], nt.nodes[m["out"]].inputs["Surface"])
            o.data.materials.clear()
            o.data.materials.append(m)
            for poly in o.data.polygons:
                poly.material_index = 0
        for n in hand_nails + finger_nails + left_nails:
            n.hide_render = False
        for name, objs in parts.items():
            K.finish(objs)
        preview_two_hands(argv[argv.index("preview") + 1] if len(argv) > argv.index("preview") + 1 else "th")
        return
    hand.data.materials.append(mat("tex_hand", None, 0.5, 0.0))
    finger.data.materials.append(mat("tex_hand_index", None, 0.5, 0.0))
    for o in (hand, finger):
        unwrap(o)
    bake_skin(hand, "hand", joints, tips, 2048)
    bake_skin(finger, "hand_index", joints, tips, 1024)
    for n in hand_nails + finger_nails:
        apply_all(n)
        n.data.materials.append(nail_mat)
        n.data.shade_smooth()
    if hand_nails:
        hand = join([hand] + hand_nails, "Hand")
    if finger_nails:
        finger = join([finger] + finger_nails, "TriggerFinger")

    # Finish the gun parts: apply cuts and bevels, UVs, shading; join each part's pieces into one object.
    export = []
    for name, objs in parts.items():
        objs = K.finish(objs)
        o = join(objs, name) if len(objs) > 1 else objs[0]
        o.name = name
        export.append(o)
    export += [hand, finger, left] + sleeves
    for o in bpy.data.objects:
        if o.get("cutter") or o.name.startswith("cut_") or o.name.startswith("gun_solid") or o.name.startswith("press_"):
            o.hide_set(True)
            o.hide_render = True

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.PROJECT, "Assets", "pistol.blend"))
    K.export("pistol", export)
    pivots["TriggerPivot"] = list(TRIGGER_PIVOT)
    with open(os.path.join(K.PREVIEWS, "pistol_pivots.json"), "w") as f:
        json.dump(pivots, f, indent=2)
    print("[pistol] pivots", pivots, flush=True)
    try:
        preview_two_hands("pistol")
    except Exception as e:
        print("[pistol] preview failed:", e, flush=True)
    print("[pistol] done", flush=True)


main()
