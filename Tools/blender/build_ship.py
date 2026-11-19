"""
Builds the container ship for BRUTAL FPS: the hull (with its deck, forecastle, railings and deck fittings), the
superstructure (accommodation block, wheelhouse, bridge-wing railings, radar mast, funnel, freefall lifeboat) and a
deck crane, and exports them to Assets/Models/<name>.glb with a preview render of each.

    blender -b --factory-startup -P Tools/blender/build_ship.py -- [ship_hull ship_house ship_crane]

Everything here is laid out in the game's ship coordinates (see Game/LevelShip.cs, which must agree): origin at
Level.ShipOrigin on the main deck, bow towards game +X, starboard towards game +Z, the sea at y = -11. The game
places the hull and the house at the origin and a crane at each crane slot, and adds what the player walks on (deck
and walkway collision, the bridge walkway floors, hatch covers, lashing bridges and the containers) itself.
Unbranded: no names, funnel marks or lettering anywhere.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import modelkit as K  # noqa: E402
from modelkit import box, cyl, lathe, prism, tube, mat  # noqa: E402

# ------------------------------------------------------------------------------------------------ layout
# (game coordinates: x forward, y up, z to starboard; Blender: X = x, Y = -z, Z = y)
HALF_BEAM = 16.0
SEA = -11.0
KEEL = -16.0
BOW_START, BOW_TIP, STERN_START, TRANSOM = 40.0, 95.0, -70.0, -95.0
FOCSLE_X, FOCSLE_Y, BULWARK = 79.0, 2.6, 1.2
BLOCK = dict(x0=-78.0, x1=-66.0, z=11.0, top=16.8)          # accommodation block
WHEEL = dict(x0=-73.0, x1=-66.0, z=11.0, y0=16.8, y1=20.0)   # wheelhouse
WING_X0, WALK_X1, WING_Z = -71.0, -63.8, 15.6                # bridge wings and the walkway across the front
PORTAL_GAP = (4.6, 8.4)                                      # z range left open in the wheelhouse front (the game fills it)
LADDER_Z = 10.2                                              # the ladder down the block front (game)
CASING = dict(x0=-90.0, x1=-78.0, z=7.0, top=10.0)
FUNNEL = dict(x0=-88.0, x1=-80.0, z=3.5, top=26.0)


def P(x, y, z):
    """Game coordinates -> Blender."""
    return (x, -z, y)


def gbox(name, size, loc, material, rot=(0, 0, 0)):
    sx, sy, sz = size
    return box(name, (sx, sz, sy), P(*loc), material, rot=rot)


def gspan(name, x0, x1, y0, y1, z0, z1, material):
    """A box between game-space bounds (in either order)."""
    x0, x1 = min(x0, x1), max(x0, x1)
    y0, y1 = min(y0, y1), max(y0, y1)
    z0, z1 = min(z0, z1), max(z0, z1)
    return gbox(name, (x1 - x0, y1 - y0, z1 - z0), ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), material)


def gpost(name, r, x, y0, y1, z, material, verts=8):
    """A vertical round post."""
    return cyl(name, r, y1 - y0, P(x, (y0 + y1) / 2, z), material, verts=verts)


def gtube(name, a, b, r, material, verts=6):
    return tube(name, [P(*a), P(*b)], r, material, verts)


def mesh(name, verts, faces, materials, face_mats=None, smooth=False):
    me = bpy.data.meshes.new(name)
    me.from_pydata([Vector(v) for v in verts], [], faces)
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for m in materials:
        obj.data.materials.append(m)
    if face_mats:
        for poly, mi in zip(obj.data.polygons, face_mats):
            poly.material_index = mi
    obj["uv"] = "box"
    if smooth:
        obj["smooth"] = True
    return obj


def frustum(name, a, b, wa, ha, wb, hb, material, up=(0, 1, 0)):
    """A tapered box from game point a (wa wide, ha tall) to b (wb, hb): jibs, legs."""
    a, b = Vector(a), Vector(b)
    d = (b - a).normalized()
    side = d.cross(Vector(up))
    if side.length < 1e-4:
        side = d.cross(Vector((1, 0, 0)))
    side.normalize()
    upv = side.cross(d).normalized()
    verts = []
    for c, w, h in ((a, wa, ha), (b, wb, hb)):
        for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            p = c + side * (sx * w / 2) + upv * (sy * h / 2)
            verts.append(P(p.x, p.y, p.z))
    faces = [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    obj = mesh(name, verts, faces, [material])
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    return obj


def railing(name, points, y, material, height=1.1, spacing=1.5, rails=(0.35, 0.72, 1.1), closed=False):
    """Stanchions and round rails along a polyline of game (x, z) points, standing on height y."""
    objs = []
    pts = list(points) + ([points[0]] if closed else [])
    for i in range(len(pts) - 1):
        (x0, z0), (x1, z1) = pts[i], pts[i + 1]
        length = math.hypot(x1 - x0, z1 - z0)
        n = max(1, int(math.ceil(length / spacing)))
        for k in range(n + (1 if i == len(pts) - 2 else 0)):
            t = k / n
            objs.append(gpost(f"{name}_post", 0.025, x0 + (x1 - x0) * t, y, y + height, z0 + (z1 - z0) * t, material, verts=6))
        for r in rails:
            objs.append(gtube(f"{name}_rail", (x0, y + r * height / 1.1, z0), (x1, y + r * height / 1.1, z1), 0.022, material))
    return objs


# ------------------------------------------------------------------------------------------------ hull
def bow_tip(y):
    """The stem rakes forward: further forward at the deck than at the waterline."""
    d = min(max(-y / 11.0, 0.0), 1.0)
    return BOW_TIP - 4.0 * d


def half_breadth(x, y):
    d = min(max(-y / 11.0, 0.0), 1.0)          # 0 at the deck, 1 at the waterline and below
    tip = bow_tip(y)
    if x > BOW_START:
        t = min((x - BOW_START) / (tip - BOW_START), 1.0)
        deck = max(1 - t ** 2.2, 0.0) ** 0.5
        water = max(1 - t ** 1.5, 0.0) ** 0.85
        f = deck + (water - deck) * d
    elif x < STERN_START:
        s = (STERN_START - x) / (STERN_START - TRANSOM)
        deck = 1 - 0.19 * s * s
        water = 1 - 0.5 * s ** 1.6
        f = deck + (water - deck) * d
    else:
        f = 1.0
    if y < -12.5:
        b = min((-12.5 - y) / 3.5, 1.0)
        f *= 1 - 0.55 * b * b
    return max(HALF_BEAM * f, 0.03)


def stations():
    xs = []
    x = TRANSOM
    while x < BOW_TIP + 1e-6:
        xs.append(round(x, 3))
        x += 1.25 if x < STERN_START else (5.0 if x < BOW_START else 1.0)
    if xs[-1] < BOW_TIP:
        xs.append(BOW_TIP)
    return xs


def hull_shell(hull_mat, boot_mat):
    levels = [KEEL, -14.0, -12.5, -11.6, -10.5, -9.5, -7.0, -4.5, -2.0, -0.6, 0.0]
    xs = stations()
    verts, faces, fm = [], [], []
    idx = {}
    for i, x in enumerate(xs):
        for j, y in enumerate(levels):
            hx = min(x, bow_tip(y))
            hb = half_breadth(hx, y)
            idx[(i, j, 1)] = len(verts); verts.append(P(hx, y, hb))     # starboard
            idx[(i, j, -1)] = len(verts); verts.append(P(hx, y, -hb))   # port
    for i in range(len(xs) - 1):
        for j in range(len(levels) - 1):
            m = 1 if (levels[j] + levels[j + 1]) / 2 < -10.5 else 0
            s = [idx[(i, j, 1)], idx[(i + 1, j, 1)], idx[(i + 1, j + 1, 1)], idx[(i, j + 1, 1)]]
            p = [idx[(i, j, -1)], idx[(i, j + 1, -1)], idx[(i + 1, j + 1, -1)], idx[(i + 1, j, -1)]]
            faces += [s, p]
            fm += [m, m]
        faces.append([idx[(i, 0, 1)], idx[(i, 0, -1)], idx[(i + 1, 0, -1)], idx[(i + 1, 0, 1)]])
        fm.append(1)
    # Transom.
    n = len(levels)
    faces.append([idx[(0, j, 1)] for j in range(n)] + [idx[(0, j, -1)] for j in reversed(range(n))])
    fm.append(0)
    return mesh("hull", verts, faces, [hull_mat, boot_mat], fm, smooth=True)


def deck_surface(name, x0, x1, y, material, inset=0.0):
    xs = [x for x in stations() if x0 < x < x1]
    xs = [x0] + xs + [x1]
    verts, faces = [], []
    for x in xs:
        hb = half_breadth(x, 0.0) - inset
        verts += [P(x, y, hb), P(x, y, -hb)]
    for i in range(len(xs) - 1):
        a, b = 2 * i, 2 * i + 2
        faces.append([a, b, b + 1, a + 1])
    obj = mesh(name, verts, faces, [material])
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    for f in bm.faces:
        if f.normal.z < 0:
            f.normal_flip()
    bm.to_mesh(obj.data)
    bm.free()
    return obj


def forecastle(hull_mat, white, deck_mat):
    """The raised deck at the bow: its sides carry the hull up into a bulwark, white inside."""
    objs = []
    xs = [FOCSLE_X] + [x for x in stations() if x > FOCSLE_X]
    top = FOCSLE_Y + BULWARK
    for side in (1, -1):
        verts, faces = [], []
        inner_v, inner_f = [], []
        cap_v, cap_f = [], []
        for x in xs:
            hb = half_breadth(x, 0.0)
            hi = max(hb - 0.14, 0.02)
            verts += [P(x, 0.0, side * hb), P(x, top, side * hb)]
            inner_v += [P(x, FOCSLE_Y, side * hi), P(x, top, side * hi)]
            cap_v += [P(x, top, side * hb), P(x, top, side * hi)]
        for i in range(len(xs) - 1):
            a, b = 2 * i, 2 * i + 2
            faces.append([a, b, b + 1, a + 1] if side == 1 else [a, a + 1, b + 1, b])
            inner_f.append([a, a + 1, b + 1, b] if side == 1 else [a, b, b + 1, a + 1])
            cap_f.append([a, b, b + 1, a + 1] if side == 1 else [a, a + 1, b + 1, b])
        objs.append(mesh("focsle_side", verts, faces, [hull_mat], smooth=True))
        objs.append(mesh("bulwark_in", inner_v, inner_f, [white], smooth=True))
        objs.append(mesh("bulwark_cap", cap_v, cap_f, [white]))
    objs.append(deck_surface("focsle_deck", FOCSLE_X, BOW_TIP, FOCSLE_Y, deck_mat, inset=0.13))
    hb = half_breadth(FOCSLE_X, 0.0)
    objs.append(gspan("focsle_front", FOCSLE_X - 0.15, FOCSLE_X, 0.0, top, -hb + 0.01, hb - 0.01, white))
    return objs


def hull():
    hull_mat = mat("marine_paint.hull", (0.13, 0.25, 0.45), 0.5)
    boot = mat("marine_paint.boot", (0.42, 0.12, 0.09), 0.55)
    deck = mat("painted_metal.deck", (0.30, 0.37, 0.31), 0.6)
    white = mat("marine_paint.white", (0.84, 0.84, 0.82), 0.5)
    black = mat("painted_metal.black", (0.11, 0.11, 0.11), 0.5)
    rail = mat("painted_metal.rail", (0.80, 0.80, 0.78), 0.5)
    steel = mat("steel")
    chain = mat("rusty_metal")
    glow = mat("emit_masthead", (1.0, 0.95, 0.85), emit=(1.0, 0.95, 0.85))
    objs = [hull_shell(hull_mat, boot)]
    objs.append(deck_surface("deck", TRANSOM, FOCSLE_X, 0.0, deck, inset=0.02))
    objs += forecastle(hull_mat, white, deck)

    # Main-deck railings from the stern to the forecastle, and across the stern.
    for side in (1, -1):
        xs = [x for x in stations() if TRANSOM + 0.3 < x < FOCSLE_X - 0.2]
        pts = [(x, side * (half_breadth(x, 0.0) - 0.1)) for x in [TRANSOM + 0.3] + xs + [FOCSLE_X - 0.2]]
        # Straight runs along the parallel midbody, short segments round the bow and stern.
        simplified = [pts[0]]
        for p in pts[1:-1]:
            if abs(p[1] - simplified[-1][1]) > 0.02 or p[0] - simplified[-1][0] > 30:
                simplified.append(p)
        simplified.append(pts[-1])
        objs += railing("rail", simplified, 0.0, rail)
    hb_t = half_breadth(TRANSOM + 0.3, 0.0) - 0.1
    objs += railing("stern_rail", [(TRANSOM + 0.3, -hb_t), (TRANSOM + 0.3, hb_t)], 0.0, rail)

    # Bollards along both sides, and at bow and stern.
    for x in (-91.0, -60.0, -30.0, 0.0, 30.0, 60.0):
        for side in (1, -1):
            z = side * (half_breadth(x, 0.0) - 0.9)
            objs.append(gspan("bollard_base", x - 0.7, x + 0.7, 0.0, 0.08, z - 0.35, z + 0.35, black))
            for dx in (-0.4, 0.4):
                objs.append(gpost("bollard", 0.22, x + dx, 0.0, 0.62, z, black, verts=12))
                objs.append(gpost("bollard_cap", 0.27, x + dx, 0.6, 0.68, z, black, verts=12))
    # Mooring winches on the stern deck, windlasses and anchor chains on the forecastle.
    machinery = mat("painted_metal.machinery", (0.40, 0.43, 0.41), 0.5)
    hole = mat("flat_hole", (0.02, 0.02, 0.02), 0.9)
    rope = mat("flat_rope", (0.66, 0.58, 0.42), 0.9)
    for side in (1, -1):
        objs += mooring_winch(-92.0, side * 6.0, side, machinery, black, rope, steel, hole)
        objs += windlass(85.0, side * 3.6, side, machinery, black, chain, steel, hole)
        # The anchor stowed in its pocket on the hull side, under the hawse pipe.
        hz = side * (half_breadth(89.5, -1.5) + 0.02)
        objs.append(gspan("anchor_pocket", 88.4, 90.6, -2.6, -0.4, hz - 0.03, hz + 0.03, black))
        objs.append(gspan("anchor_shank", 89.35, 89.65, -2.3, -0.6, hz - 0.12 * side - 0.1, hz - 0.12 * side + 0.1, chain))
        objs.append(gspan("anchor_flukes", 88.7, 90.3, -2.5, -2.1, hz - 0.12 * side - 0.12, hz - 0.12 * side + 0.12, chain))
    for x in (88.0, 92.0):
        for side in (1, -1):
            z = side * (half_breadth(x, 0.0) - 0.9)
            for dx in (-0.35, 0.35):
                objs.append(gpost("bollard", 0.2, x + dx, FOCSLE_Y, FOCSLE_Y + 0.55, z, black, verts=12))
    # Foremast with its masthead light.
    objs.append(gpost("foremast", 0.22, 90.0, FOCSLE_Y, FOCSLE_Y + 9.5, 0.0, rail, verts=12))
    objs.append(gspan("foremast_yard", 89.9, 90.1, FOCSLE_Y + 7.6, FOCSLE_Y + 7.75, -1.6, 1.6, rail))
    objs.append(gspan("masthead_light", 89.85, 90.15, FOCSLE_Y + 9.5, FOCSLE_Y + 9.85, -0.15, 0.15, glow))
    # A white strip along the top of the hull (the deck edge), and steel fairleads in the bulwark.
    for side in (1, -1):
        for x0, x1 in ((STERN_START, BOW_START),):
            objs.append(gspan("sheer_strip", x0, x1, -0.35, 0.0, side * HALF_BEAM - 0.015, side * HALF_BEAM + 0.015, white))
    for x in (82.0, -93.0):
        for side in (1, -1):
            hb = half_breadth(x, 0.0)
            y = FOCSLE_Y + 0.5 if x > 0 else 0.4
            objs.append(gspan("fairlead", x - 0.5, x + 0.5, y, y + 0.35, side * (hb - 0.2), side * (hb + 0.02), steel))
    return objs


# ------------------------------------------------------------------------------------------------ deck machinery
def chain_links(points, material, pitch=0.24, name="chain"):
    """Studless chain along a polyline of game points: links alternately upright and flat, each an open link (two
    side bars and two end bars)."""
    pts = [Vector(p) for p in points]
    # Resample at the link pitch.
    seg = [(pts[i], pts[i + 1]) for i in range(len(pts) - 1)]
    total = sum((b - a).length for a, b in seg)
    n = max(2, int(total / pitch))
    out = []
    for k in range(n):
        d = (k + 0.5) * total / n
        for a, b in seg:
            l = (b - a).length
            if d <= l or (a, b) == seg[-1]:
                t = min(d / max(l, 1e-6), 1.0)
                out.append((a + (b - a) * t, (b - a).normalized()))
                break
            d -= l
    objs = []
    for k, (c, t) in enumerate(out):
        up = Vector((0, 1, 0))
        side = t.cross(up)
        if side.length < 1e-3:
            side = Vector((1, 0, 0))
        side.normalize()
        across = t.cross(side).normalized() if k % 2 == 0 else side   # upright, then flat
        half = pitch * 0.62
        for s_ in (-1, 1):
            off = across * (0.045 * s_)
            objs.append(frustum(name, tuple(c - t * half + off), tuple(c + t * half + off), 0.034, 0.034, 0.034, 0.034, material,
                                up=tuple(t.cross(across))))
        for e in (-1, 1):
            m = c + t * (half * e)
            objs.append(frustum(name, tuple(m - across * 0.06), tuple(m + across * 0.06), 0.034, 0.034, 0.034, 0.034, material,
                                up=tuple(t)))
    return objs


def zcyl(name, r, z0, z1, x, y, material, verts=20):
    """A cylinder along game z (athwartships) from z0 to z1, its axis at (x, y)."""
    return cyl(name, r, abs(z1 - z0), P(x, y, (z0 + z1) / 2), material, verts=verts, rot=(90, 0, 0))


def windlass(x, zw, side, paint, black, chain, steel, hole):
    """An anchor windlass on the forecastle. Its shaft runs athwartships at x; the gypsy (the chain wheel) sits on the
    chain's line at zw, with its band brake inboard of it, the gearbox and motor further inboard, and a warping head
    on the outboard end. The chain comes up out of the chain pipe behind it, over the top of the gypsy, forward
    through the chain stopper and down the hawse pipe at the bow."""
    D = FOCSLE_Y
    A = D + 0.95                      # shaft height
    i = -side                         # inboard
    objs = [gspan("windlass_bed", x - 0.7, x + 0.7, D, D + 0.3, zw + i * 2.3, zw + side * 1.55, paint)]
    for zp in (zw + side * 0.32, zw + i * 0.62, zw + side * 1.42):
        objs.append(gspan("bearing", x - 0.28, x + 0.28, D + 0.3, A + 0.05, zp - 0.09, zp + 0.09, paint))
        objs.append(zcyl("bearing_cap", 0.2, zp - 0.1, zp + 0.1, x, A, paint))
    objs.append(zcyl("shaft", 0.12, zw + i * 1.0, zw + side * 1.6, x, A, steel))
    # Gypsy: a wheel with pockets for the links (whelps round the rim).
    objs.append(zcyl("gypsy", 0.48, zw - 0.17, zw + 0.17, x, A, black, verts=24))
    for k in range(10):
        a = k * math.tau / 10
        p = Vector((x, A, 0)) + Vector((math.cos(a), math.sin(a), 0)) * 0.47
        objs.append(box("whelp", (0.1, 0.36, 0.08), P(p.x, p.y, zw), black, rot=(0, -math.degrees(a), 0)))
    # Band brake on its drum, and the brake's screw column with a handwheel on top.
    zb = zw + i * 0.4
    objs.append(zcyl("brake_drum", 0.56, zb - 0.1, zb + 0.1, x, A, black, verts=24))
    objs.append(zcyl("brake_band", 0.6, zb - 0.08, zb + 0.08, x, A, steel, verts=24))
    objs.append(gpost("brake_column", 0.06, x - 0.85, D + 0.3, A + 0.35, zb, paint, verts=10))
    objs.append(gpost("brake_wheel", 0.26, x - 0.85, A + 0.35, A + 0.39, zb, steel, verts=20))
    objs.append(gspan("brake_link", x - 0.85, x - 0.3, A + 0.05, A + 0.12, zb - 0.04, zb + 0.04, steel))
    # Gearbox and motor inboard.
    objs.append(gspan("gearbox", x - 0.55, x + 0.55, D + 0.3, A + 0.55, zw + i * 0.72, zw + i * 1.35, paint))
    objs.append(gspan("gearbox_lid", x - 0.6, x + 0.6, A + 0.55, A + 0.6, zw + i * 0.68, zw + i * 1.39, paint))
    objs.append(zcyl("motor", 0.36, zw + i * 1.38, zw + i * 2.15, x, A - 0.1, paint))
    objs.append(zcyl("motor_fins", 0.39, zw + i * 1.5, zw + i * 2.0, x, A - 0.1, paint, verts=12))
    objs.append(zcyl("motor_cap", 0.3, zw + i * 2.15, zw + i * 2.25, x, A - 0.1, paint))
    # Warping head on the outboard end: a waisted drum.
    zh = zw + side * 0.95
    objs.append(lathe("warping_head", [(0.42, -0.3), (0.3, -0.2), (0.26, 0.0), (0.3, 0.2), (0.42, 0.3)], P(x, A, zh), black, verts=20, rot=(90, 0, 0)))
    # Chain pipe in the deck behind, the stopper ahead, the hawse pipe at the bow.
    pipe_x = x - 1.45
    objs.append(gpost("chain_pipe", 0.26, pipe_x, D, D + 0.22, zw, black, verts=16))
    objs.append(gpost("chain_pipe_hole", 0.18, pipe_x, D + 0.22, D + 0.225, zw, hole, verts=16))
    hawse_x = 90.5
    hawse_z = side * (half_breadth(hawse_x, 0.0) - 0.95)
    stop_x = x + 2.4
    stop_z = zw + (hawse_z - zw) * (stop_x - x) / (hawse_x - x)
    objs.append(gspan("stopper_base", stop_x - 0.55, stop_x + 0.55, D, D + 0.08, stop_z - 0.3, stop_z + 0.3, black))
    for s_ in (-1, 1):
        objs.append(gspan("stopper_cheek", stop_x - 0.5, stop_x + 0.5, D + 0.08, D + 0.36, stop_z + s_ * 0.16 - 0.03, stop_z + s_ * 0.16 + 0.03, black))
    objs.append(gspan("stopper_bar", stop_x + 0.05, stop_x + 0.15, D + 0.36, D + 0.42, stop_z - 0.22, stop_z + 0.22, steel))
    objs.append(gpost("hawse_pipe", 0.36, hawse_x, D, D + 0.14, hawse_z, black, verts=16))
    objs.append(gpost("hawse_hole", 0.26, hawse_x, D + 0.14, D + 0.145, hawse_z, hole, verts=16))
    # The chain.
    path = [(pipe_x, D + 0.1, zw), (x - 0.75, A + 0.1, zw), (x - 0.35, A + 0.47, zw), (x, A + 0.55, zw), (x + 0.35, A + 0.47, zw),
            (x + 0.8, A + 0.05, zw + (hawse_z - zw) * 0.8 / (hawse_x - x)),
            (stop_x, D + 0.24, stop_z), (hawse_x - 0.3, D + 0.12, hawse_z + (zw - hawse_z) * 0.3 / (hawse_x - x)), (hawse_x, D + 0.05, hawse_z)]
    objs += chain_links(path, chain)
    return objs


def mooring_winch(x, zc, side, paint, black, rope, steel, hole):
    """A mooring winch on the stern deck: a drum of mooring rope on a shaft running athwartships, the gearbox and
    motor inboard, a warping head outboard, all on a bedplate along the shaft."""
    A = 1.0
    i = -side
    objs = [gspan("winch_bed", x - 0.7, x + 0.7, 0.0, 0.25, zc + i * 1.75, zc + side * 1.55, paint)]
    for zp in (zc + side * 0.75, zc + i * 0.75, zc + side * 1.42):
        objs.append(gspan("bearing", x - 0.28, x + 0.28, 0.25, A + 0.05, zp - 0.09, zp + 0.09, paint))
    objs.append(zcyl("shaft", 0.12, zc + i * 1.0, zc + side * 1.6, x, A, steel))
    objs.append(zcyl("winch_drum", 0.42, zc - 0.62, zc + 0.62, x, A, black, verts=20))
    objs.append(zcyl("rope_wound", 0.58, zc - 0.58, zc + 0.58, x, A, rope, verts=20))
    for dz in (-0.65, 0.65):
        objs.append(zcyl("winch_flange", 0.78, zc + dz - 0.04, zc + dz + 0.04, x, A, black, verts=24))
    objs.append(gspan("gearbox", x - 0.5, x + 0.5, 0.25, A + 0.5, zc + i * 0.85, zc + i * 1.3, paint))
    objs.append(zcyl("motor", 0.33, zc + i * 1.3, zc + i * 1.75, x, A - 0.1, paint))
    objs.append(lathe("warping_head", [(0.4, -0.25), (0.28, -0.15), (0.25, 0.0), (0.28, 0.15), (0.4, 0.25)], P(x, A, zc + side * 1.1), black, verts=20, rot=(90, 0, 0)))
    # The rope's end leads off the top of the drum aft to the fairlead in the bulwark.
    hb = half_breadth(-93.0, 0.0)
    objs.append(tube("rope_lead", [P(x, A + 0.58, zc), P(-92.6, A + 0.3, zc + side * 1.6), P(-93.0, 0.55, side * (hb - 0.3))], 0.05, rope, 8))
    return objs


# ------------------------------------------------------------------------------------------------ house
def house():
    white = mat("marine_paint.white", (0.84, 0.84, 0.82), 0.5)
    trim = mat("marine_paint.trim", (0.72, 0.72, 0.70), 0.5)
    funnel = mat("marine_paint.funnel", (0.70, 0.71, 0.70), 0.5)
    black = mat("painted_metal.black", (0.10, 0.10, 0.10), 0.5)
    deck = mat("painted_metal.deck", (0.30, 0.37, 0.31), 0.6)
    rail = mat("painted_metal.rail", (0.80, 0.80, 0.78), 0.5)
    glass = mat("flat_glass", (0.06, 0.08, 0.10), 0.06)
    door = mat("painted_metal.door", (0.62, 0.63, 0.62), 0.5)
    orange = mat("flat_lifeboat", (0.95, 0.42, 0.08), 0.45)
    steel = mat("steel")
    glow_red = mat("emit_port", (1.0, 0.1, 0.06), emit=(1.0, 0.1, 0.06))
    glow_green = mat("emit_starboard", (0.1, 1.0, 0.25), emit=(0.1, 1.0, 0.25))
    glow_white = mat("emit_light", (1.0, 0.95, 0.85), emit=(1.0, 0.95, 0.85))
    objs = []
    b = BLOCK
    # Accommodation block and its deck lines.
    objs.append(gspan("block", b["x0"], b["x1"], 0.0, b["top"], -b["z"], b["z"], white))
    for k in range(1, 6):
        y = 2.8 * k
        objs.append(gspan("deckline", b["x0"] - 0.12, b["x1"] + 0.12, y - 0.08, y + 0.08, -b["z"] - 0.12, b["z"] + 0.12, trim))
    # Windows: rows on every deck (portholes are too small to read at this scale; these are square ones).
    for k in range(6):
        yc = 2.8 * k + 1.6
        if k == 0:
            for z in (-5.0, 5.0):
                objs.append(gspan("door", b["x1"], b["x1"] + 0.04, 0.05, 2.0, z - 0.45, z + 0.45, door))
        else:
            for i in range(9):
                z = -8.8 + 2.2 * i
                objs.append(gspan("window", b["x1"], b["x1"] + 0.03, yc - 0.5, yc + 0.5, z - 0.5, z + 0.5, glass))
        for side in (1, -1):
            for i in range(5):
                x = b["x0"] + 1.2 + 2.4 * i
                objs.append(gspan("window", x - 0.5, x + 0.5, yc - 0.5, yc + 0.5, side * b["z"] - 0.015, side * b["z"] + 0.015, glass))
        if k >= 4:
            for i in range(9):
                z = -8.8 + 2.2 * i
                objs.append(gspan("window", b["x0"] - 0.03, b["x0"], yc - 0.5, yc + 0.5, z - 0.5, z + 0.5, glass))

    # Wheelhouse: a band of big windows all round; the front wall leaves a gap for the portal.
    w = WHEEL
    y0, y1 = w["y0"], w["y1"]
    band0, band1 = y0 + 1.1, y1 - 0.4
    g0, g1 = PORTAL_GAP
    fx0, fx1 = w["x1"] - 0.3, w["x1"]
    for z0, z1 in ((-w["z"], g0), (g1, w["z"])):
        objs.append(gspan("wh_front_low", fx0, fx1, y0, band0, z0, z1, white))
        objs.append(gspan("wh_front_high", fx0, fx1, band1, y1, z0, z1, white))
        objs.append(gspan("wh_front_glass", fx0 + 0.08, fx1 - 0.08, band0, band1, z0, z1, glass))
        n = max(1, int(round((z1 - z0) / 1.7)))
        for i in range(n + 1):
            z = z0 + (z1 - z0) * i / n
            objs.append(gspan("mullion", fx0, fx1 + 0.02, band0, band1, z - 0.07, z + 0.07, white))
    for side in (1, -1):
        z = side * w["z"]
        objs.append(gspan("wh_side_low", w["x0"], w["x1"], y0, band0, z - 0.15, z + 0.15, white))
        objs.append(gspan("wh_side_high", w["x0"], w["x1"], band1, y1, z - 0.15, z + 0.15, white))
        objs.append(gspan("wh_side_glass", w["x0"] + 0.2, w["x1"] - 0.2, band0, band1, z - 0.07, z + 0.07, glass))
        for x in (w["x0"] + 0.1, (w["x0"] + w["x1"]) / 2, w["x1"] - 0.1):
            objs.append(gspan("mullion", x - 0.08, x + 0.08, band0, band1, z - 0.16, z + 0.16, white))
        # Bridge wing door.
        objs.append(gspan("wing_door", w["x0"] + 0.6, w["x0"] + 1.5, y0 + 0.05, y0 + 2.1, z + side * 0.15, z + side * 0.19, door))
    objs.append(gspan("wh_back", w["x0"], w["x0"] + 0.3, y0, y1, -w["z"], w["z"], white))
    objs.append(gspan("wh_roof", w["x0"] - 0.4, w["x1"] + 0.4, y1, y1 + 0.3, -w["z"] - 0.4, w["z"] + 0.4, white))
    objs.append(gspan("visor", w["x1"], w["x1"] + 0.8, y1 - 0.25, y1 - 0.15, -w["z"], w["z"], white))
    # The deck the wheelhouse stands on, behind it (the block's roof, aft of the wheelhouse).
    objs.append(gspan("block_roof", b["x0"], w["x0"], b["top"] - 0.05, b["top"], -b["z"], b["z"], deck))
    objs += railing("block_roof_rail", [(w["x0"], -b["z"] + 0.1), (b["x0"] + 0.1, -b["z"] + 0.1), (b["x0"] + 0.1, b["z"] - 0.1), (w["x0"], b["z"] - 0.1)], b["top"], rail)

    # Bridge wings and the walkway across the front: railings (the floors are the game's), brackets, consoles.
    floor = b["top"]
    for side in (1, -1):
        objs += railing("wing_rail", [(WING_X0, side * w["z"]), (WING_X0, side * WING_Z), (WALK_X1, side * WING_Z)], floor, rail)
        for x in (WING_X0 + 0.3, -68.5, w["x1"] - 0.3):
            objs.append(prism("wing_bracket", [(-side * w["z"], floor - 0.25), (-side * (WING_Z - 0.3), floor - 0.25), (-side * w["z"], floor - 2.6)],
                              0.18, (x, 0, 0), white, axis='X'))
        objs.append(gspan("wing_console", -66.0, -65.2, floor, floor + 1.15, side * (WING_Z - 1.0) - 0.4, side * (WING_Z - 1.0) + 0.4, white))
        objs.append(gspan("wing_light", -68.0, -67.6, floor + 3.2, floor + 3.5, side * (w["z"] + 0.2), side * (w["z"] + 0.45), glow_green if side > 0 else glow_red))
    objs += railing("walk_rail", [(WALK_X1, -WING_Z), (WALK_X1, WING_Z)], floor, rail)
    for z in (-13.5, -7.0, 0.0, 13.5):
        objs.append(prism("walk_bracket", [(b["x1"], floor - 0.25), (WALK_X1 + 0.3, floor - 0.25), (b["x1"], floor - 2.0)], 0.16, (0, -z, 0), white, axis='Y'))

    # Radar mast on the wheelhouse roof.
    top = y1 + 0.3
    objs.append(gspan("mast_tower", -71.2, -69.8, top, top + 3.6, -0.7, 0.7, white))
    objs.append(gspan("mast_platform", -72.0, -69.0, top + 3.6, top + 3.75, -2.2, 2.2, white))
    objs += railing("mast_rail", [(-72.0, -2.2), (-69.0, -2.2), (-69.0, 2.2), (-72.0, 2.2)], top + 3.75, rail, height=0.9, closed=True)
    for z, yaw in ((-1.2, 25.0), (1.2, -50.0)):
        objs.append(gpost("radar_post", 0.15, -70.5, top + 3.75, top + 4.4, z, black, verts=10))
        objs.append(box("radar", (4.4, 0.3, 0.32), P(-70.5, top + 4.55, z), black, rot=(0, 0, yaw)))
    objs.append(gpost("signal_mast", 0.09, -70.5, top + 3.75, top + 9.0, 0.0, white, verts=10))
    objs.append(gspan("signal_yard", -70.6, -70.4, top + 7.4, top + 7.52, -2.0, 2.0, white))
    objs.append(gspan("masthead_light", -70.65, -70.35, top + 9.0, top + 9.3, -0.15, 0.15, glow_white))
    objs.append(gpost("horn", 0.12, -71.6, top, top + 0.8, 1.8, black, verts=10))

    # Engine casing aft of the block, and the funnel on it.
    c = CASING
    objs.append(gspan("casing", c["x0"], c["x1"], 0.0, c["top"], -c["z"], c["z"], white))
    objs.append(gspan("casing_roof", c["x0"], c["x1"], c["top"], c["top"] + 0.05, -c["z"], c["z"], deck))
    objs += railing("casing_rail", [(c["x1"], -c["z"] + 0.1), (c["x0"] + 0.1, -c["z"] + 0.1), (c["x0"] + 0.1, c["z"] - 0.1), (c["x1"], c["z"] - 0.1)], c["top"], rail)
    for side in (1, -1):
        for x in (-88.0, -84.5, -81.0):
            objs.append(gspan("casing_vent", x - 0.7, x + 0.7, 5.0, 6.4, side * (c["z"] - 0.02), side * (c["z"] + 0.12), trim))
        objs.append(gspan("casing_door", -86.0, -85.1, 0.05, 2.0, side * (c["z"] - 0.02), side * (c["z"] + 0.03), door))
    f = FUNNEL
    r = 1.6
    outline = []
    cx0, cx1, hz = f["x0"] + r, f["x1"] - r, f["z"] - r
    for (cx, cz, a0) in ((cx1, -hz, -90), (cx1, hz, 0), (cx0, hz, 90), (cx0, -hz, 180)):
        for k in range(5):
            a = math.radians(a0 + k * 22.5)
            outline.append((cx + r * math.cos(a), -(cz + r * math.sin(a))))
    body_h = f["top"] - 2.2 - c["top"]
    objs.append(prism("funnel", outline, body_h, (0, 0, c["top"] + body_h / 2), funnel, axis='Z'))
    objs.append(prism("funnel_top", outline, 2.2, (0, 0, f["top"] - 1.1), black, axis='Z'))
    for dx in (-1.5, 0.0, 1.5):
        objs.append(gpost("exhaust", 0.38, (f["x0"] + f["x1"]) / 2 + dx, f["top"] - 0.5, f["top"] + 1.4, 0.0, black, verts=14))

    # The freefall lifeboat on its ramp over the stern, nose down and pointing aft.
    ramp_top = (-87.5, c["top"] + 2.2)
    ramp_end = (-95.5, c["top"] - 2.4)
    ang = math.degrees(math.atan2(ramp_top[1] - ramp_end[1], ramp_top[0] - ramp_end[0]))
    for zs in (-1.0, 1.0):
        objs.append(frustum("ramp_rail", (ramp_top[0], ramp_top[1], zs), (ramp_end[0], ramp_end[1], zs), 0.2, 0.3, 0.2, 0.3, steel))
    for x in (-88.0, -90.5):
        yb = ramp_top[1] + (x - ramp_top[0]) * (ramp_top[1] - ramp_end[1]) / (ramp_top[0] - ramp_end[0])
        for zs in (-1.0, 1.0):
            objs.append(frustum("ramp_leg", (x, c["top"], zs), (x, yb, zs), 0.22, 0.22, 0.22, 0.22, steel))
    mid = ((ramp_top[0] + ramp_end[0]) / 2 - 0.3, (ramp_top[1] + ramp_end[1]) / 2 + 1.65)
    boat = lathe("lifeboat", [(0.0, -4.3), (0.9, -3.9), (1.45, -2.8), (1.6, -0.5), (1.5, 2.2), (1.05, 3.7), (0.0, 4.4)],
                 P(mid[0], mid[1], 0.0), orange, verts=18, rot=(0, -90 - ang, 0))
    boat.scale = (1.0, 0.95, 1.0)
    objs.append(boat)
    return objs


# ------------------------------------------------------------------------------------------------ crane
def crane():
    paint = mat("painted_metal_tint", (0.86, 0.66, 0.08), 0.5)
    dark = mat("painted_metal.dark", (0.16, 0.16, 0.16), 0.5)
    wire = mat("flat_wire", (0.06, 0.06, 0.06), 0.5, 0.6)
    glass = mat("flat_glass", (0.06, 0.08, 0.10), 0.06)
    steel = mat("steel")
    rail = mat("painted_metal_tint.rail", (0.86, 0.66, 0.08), 0.5)
    lamp = mat("emit_lamp", (1.0, 0.9, 0.7), emit=(1.0, 0.9, 0.7))
    objs = []
    ped_h = 13.0
    objs.append(gpost("pedestal_foot", 2.1, 0.0, 0.0, 0.35, 0.0, paint, verts=28))
    objs.append(gpost("pedestal", 1.6, 0.0, 0.0, ped_h, 0.0, paint, verts=28))
    objs.append(gpost("slew_ring", 1.9, 0.0, ped_h, ped_h + 0.35, 0.0, dark, verts=28))
    # A ladder up the pedestal (its rungs as one grid), and a platform round the slewing ring.
    for dz in (-0.25, 0.25):
        objs.append(gspan("ped_ladder", -1.78, -1.72, 0.4, ped_h - 0.2, dz - 0.03, dz + 0.03, paint))
    for k in range(int((ped_h - 0.8) / 0.3)):
        objs.append(gspan("ped_rung", -1.76, -1.72, 0.6 + k * 0.3, 0.63 + k * 0.3, -0.25, 0.25, paint))
    hy0 = ped_h + 0.35
    hy1 = hy0 + 3.6
    objs.append(gspan("house", -3.2, 2.0, hy0, hy1, -1.9, 1.9, paint))
    objs.append(gspan("house_roof", -3.25, 2.05, hy1, hy1 + 0.1, -1.95, 1.95, paint))
    objs.append(gspan("machinery_vent", -3.0, -1.5, hy0 + 0.8, hy0 + 2.2, 1.9, 1.95, dark))
    objs.append(gspan("machinery_vent", -3.0, -1.5, hy0 + 0.8, hy0 + 2.2, -1.95, -1.9, dark))
    # Operator's cab on the starboard front corner.
    objs.append(gspan("cab", 0.6, 2.6, hy0 + 0.4, hy0 + 2.6, 1.9, 3.4, paint))
    objs.append(gspan("cab_glass_front", 2.6, 2.63, hy0 + 1.2, hy0 + 2.4, 2.0, 3.3, glass))
    objs.append(gspan("cab_glass_side", 0.8, 2.5, hy0 + 1.2, hy0 + 2.4, 3.4, 3.43, glass))
    objs.append(gspan("cab_floor_glass", 1.0, 2.5, hy0 + 0.38, hy0 + 0.41, 2.1, 3.2, glass))
    # A-frame for the luffing gear.
    apex_y = hy1 + 4.0
    for zs in (-1.3, 1.3):
        objs.append(frustum("aframe_leg", (-2.6, hy1, zs), (-1.4, apex_y, zs * 0.5), 0.35, 0.35, 0.3, 0.3, paint))
        objs.append(frustum("aframe_strut", (0.8, hy1, zs), (-1.4, apex_y, zs * 0.5), 0.25, 0.25, 0.22, 0.22, paint))
    objs.append(frustum("aframe_head", (-1.4, apex_y, -0.8), (-1.4, apex_y, 0.8), 0.5, 0.5, 0.5, 0.5, paint))
    for zs in (-0.5, 0.5):
        objs.append(cyl("sheave", 0.35, 0.15, P(-1.4, apex_y + 0.15, zs), dark, verts=14, rot=(90, 0, 0)))
    # Jib: a tapered box girder, luffed up 15 degrees and pointing forward.
    heel = Vector((2.4, hy0 + 2.05, 0.0))
    luff = math.radians(15.0)
    length = 28.0
    tip = heel + Vector((math.cos(luff), math.sin(luff), 0.0)) * length
    objs.append(cyl("jib_heel_pin", 0.35, 2.2, P(heel.x, heel.y, 0.0), dark, verts=14, rot=(90, 0, 0)))
    objs.append(frustum("jib", tuple(heel), tuple(tip), 1.6, 1.3, 0.8, 0.7, paint))
    # Lightening holes along the jib's sides suggested by dark panels.
    for k in range(1, 9):
        t = k / 9.5
        p = heel + (tip - heel) * t
        w = 1.6 + (0.8 - 1.6) * t
        h = 1.3 + (0.7 - 1.3) * t
        for zs in (-1, 1):
            objs.append(box("jib_panel", (1.6, 0.02, h * 0.55), P(p.x, p.y, zs * (w / 2 + 0.005)), dark, rot=(0, -15.0, 0)))
    head = tip + Vector((0.45, -0.1, 0.0))
    objs.append(gspan("jib_head", head.x - 0.7, head.x + 0.7, head.y - 0.6, head.y + 0.6, -0.55, 0.55, paint))
    for zs in (-0.3, 0.3):
        objs.append(cyl("jib_sheave", 0.45, 0.12, P(head.x + 0.4, head.y, zs), dark, verts=14, rot=(90, 0, 0)))
    objs.append(gspan("jib_lamp", head.x - 0.2, head.x + 0.2, head.y - 0.85, head.y - 0.6, -0.2, 0.2, lamp))
    # Wires: luffing wires from the A-frame to the jib head, the hoist wire down to the hook block.
    for zs in (-0.25, 0.25):
        objs.append(gtube("luff_wire", (-1.4, apex_y + 0.3, zs), (head.x, head.y + 0.55, zs), 0.03, wire))
        objs.append(gtube("hoist_wire", (head.x + 0.45, head.y - 0.3, zs), (head.x + 0.45, head.y - 9.0, zs * 0.5), 0.025, wire))
    objs.append(gspan("hook_block", head.x - 0.05, head.x + 0.95, head.y - 10.0, head.y - 9.0, -0.35, 0.35, paint))
    objs.append(gspan("hook_shank", head.x + 0.35, head.x + 0.55, head.y - 10.6, head.y - 10.0, -0.08, 0.08, steel))
    objs.append(gspan("hook", head.x + 0.2, head.x + 0.75, head.y - 10.85, head.y - 10.6, -0.08, 0.08, steel))
    # Handrail round the house roof.
    objs += railing("roof_rail", [(-3.15, -1.85), (1.95, -1.85), (1.95, 1.85), (-3.15, 1.85)], hy1 + 0.1, rail, height=0.9, closed=True)
    return objs


# ------------------------------------------------------------------------------------------------ main
BUILDERS = {"ship_hull": hull, "ship_house": house, "ship_crane": crane}
PREVIEW_ANGLE = {"ship_hull": (-35, 20), "ship_house": (-40, 22), "ship_crane": (-40, 18)}


def build(name):
    K.reset()
    objs = BUILDERS[name]()
    objs = K.finish(objs, join_name=name)
    K.export(name, objs)
    try:
        K.preview(name, objs, angle=PREVIEW_ANGLE.get(name, (-40, 25)))
    except Exception as e:  # previews are a convenience
        print(f"[model] preview of {name} failed: {e}", flush=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for name in argv or list(BUILDERS):
        try:
            build(name)
        except Exception:
            import traceback
            traceback.print_exc()
            print(f"[model] FAILED {name}", flush=True)
    print("[model] done", flush=True)


main()
