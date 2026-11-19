"""
Builds the machine gun nest in the jungle clearing and exports it as Assets/Models/gun_nest.glb, with a preview as
Tools/previews/models/gun_nest*.png: a horseshoe of stacked sandbags round a plank floor, an empty tripod (the M249 that
sits on it is the game's, see Game/GunNest.cs), an ammunition crate with tins on it, one open and its belt drawn out,
more belts draped over the bags, spent cases, and behind it all a small concrete bunker with a firing slit, stained and
mossy.

    blender -b --factory-startup -P Tools/blender/build_gun_nest.py

Coordinates: metres, the origin on the ground at the middle of the horseshoe, axis-aligned with the level: Blender +X is
the game's +X and Blender +Y the game's -Z. WALL_SPAN and BUNKER_* are repeated in Level.BuildGunNest (colliders), and CRADLE in
Level.GunNestCradle (where the gun is put).

Exported objects (one per texture set, the game draws them as one prop):
    Sandbags        the bags, in three shades of canvas
    Timber          the plank floor and the crate
    Steel           the tripod, the tins, the bunker's door and vent
    Concrete        the bunker
    Stains          what has run down the concrete, and the moss
    Rounds          the draped belts and the spent cases
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import modelkit as K  # noqa: E402
from modelkit import box, cyl, mat, add_bevel  # noqa: E402

FACING = math.radians(27.0)                  # where the gun looks: this far round from south (game +Z) towards the east
WALL_SPAN = math.radians(105.0)              # the horseshoe runs this far either side of the facing
COURSE = 0.135                               # the height of a course of bags
COURSES = 7
BAG = (0.58, 0.30, 0.17)                     # a filled bag: length, width, thickness
BUNKER_C = Vector((0.05, 3.9))               # the bunker's middle (Blender x, y)
BUNKER_HALF = Vector((1.75, 1.40))
BUNKER_H = 2.3
CRADLE = Vector((-0.25 * math.sin(FACING), 0.25 * math.cos(FACING), 1.06))   # a little back from the middle, gun height


def dir_at(angle):
    """The way out of the horseshoe at this angle off the facing, in Blender's ground plane."""
    a = FACING + angle
    return Vector((math.sin(a), -math.cos(a), 0.0))


def tube(name, points, radius, material, count=8):
    """A round tube through 3D points: rings square to the path."""
    bm = bmesh.new()
    rings = []
    for i, c in enumerate(points):
        t = (points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)]).normalized()
        u = t.cross(Vector((0.31, 0.77, 0.23))).normalized()
        v = t.cross(u).normalized()
        rings.append([bm.verts.new(c + u * (radius * math.cos(math.tau * k / count)) + v * (radius * math.sin(math.tau * k / count))) for k in range(count)])
    for a, b in zip(rings, rings[1:]):
        for k in range(count):
            n = (k + 1) % count
            bm.faces.new((a[k], a[n], b[n], b[k]))
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = K._new_obj(name, bm, material)
    o["smooth"] = True
    return o


# ============================================================================================ sandbags
def add_bag(bm, center, yaw, tilt, roll, rng, size=BAG):
    """One filled bag: a pillow, fuller in the middle, its tied end drawn in, the cloth a little lumpy."""
    L, W, T = size
    nu, nv = 9, 10
    rot = (Matrix.Rotation(yaw, 3, 'Z') @ Matrix.Rotation(tilt, 3, 'Y') @ Matrix.Rotation(roll, 3, 'X'))
    tied = rng.choice((-1, 1))
    phase = rng.uniform(0, 6.28)
    rings = []
    for i in range(nu):
        s = -1 + 2 * i / (nu - 1)
        # The section: full over the middle, rounded off to the ends; the tied end comes in further, and flatter.
        full = (1 - abs(s) ** 5) ** 0.32
        if s * tied > 0.72:
            full *= 0.72
        ring = []
        for j in range(nv):
            a = math.tau * j / nv
            c, d = math.cos(a), math.sin(a)
            # Flat on its underside and sides, where the weight of the wall has pressed it.
            y = math.copysign(abs(c) ** 0.5, c) * W / 2 * (0.22 + 0.78 * full)
            z = math.copysign(abs(d) ** 0.7, d) * T / 2 * (0.12 + 0.88 * full)
            lump = 1 + 0.05 * math.sin(s * 5 + j * 1.3 + phase)
            ring.append(bm.verts.new(center + rot @ Vector((s * L / 2, y * lump, z * lump))))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for j in range(nv):
            n = (j + 1) % nv
            bm.faces.new((a[j], a[n], b[n], b[j]))
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[-1])))


def build_sandbags():
    rng = random.Random(1942)
    meshes = [bmesh.new() for _ in range(3)]
    bags = 0
    for course in range(COURSES):
        z = BAG[2] / 2 + course * COURSE
        # Two skins of bags low down, one over them higher up, stepped in towards the gunner.
        radii = (1.90, 2.26) if course < 4 else (2.02,)
        for ring, r in enumerate(radii):
            step = BAG[0] * 0.97 / r
            count = int(2 * WALL_SPAN / step)
            start = -count * step / 2 + (step / 2 if (course + ring) % 2 else 0.0)
            for k in range(count + 1):
                a = start + k * step + rng.uniform(-0.012, 0.012)
                if abs(a) > WALL_SPAN + 0.02:
                    continue
                d = dir_at(a)
                c = Vector((d.x * r, d.y * r, z + rng.uniform(-0.008, 0.008))) + d * rng.uniform(-0.02, 0.02)
                yaw = math.atan2(d.y, d.x) + math.pi / 2 + rng.uniform(-0.05, 0.05)
                add_bag(meshes[rng.randrange(3)], c, yaw, rng.uniform(-0.04, 0.04), rng.uniform(-0.05, 0.05), rng)
                bags += 1
    # The ends of the horseshoe: headers laid across, tumbled a little, and a couple thrown down at the foot.
    for side in (-1, 1):
        d = dir_at(side * (WALL_SPAN + 0.17))
        for course in range(COURSES - 2):
            c = Vector((d.x * 2.08, d.y * 2.08, BAG[2] / 2 + course * COURSE))
            yaw = math.atan2(d.y, d.x) + rng.uniform(-0.08, 0.08)
            add_bag(meshes[rng.randrange(3)], c, yaw, rng.uniform(-0.05, 0.05), rng.uniform(-0.05, 0.05), rng)
            bags += 1
        loose = dir_at(side * (WALL_SPAN + 0.5))
        add_bag(meshes[rng.randrange(3)], Vector((loose.x * 2.2, loose.y * 2.2, 0.085)), rng.uniform(0, 3), 0, 0, rng)
        bags += 1
    print(f"[gun nest] {bags} sandbags", flush=True)
    shades = [("canvas.khaki", (0.60, 0.50, 0.33)), ("canvas.dun", (0.50, 0.43, 0.30)), ("canvas.rotten", (0.40, 0.38, 0.26))]
    objs = []
    for bm, (name, color) in zip(meshes, shades):
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        o = K._new_obj("p_bags_" + name, bm, mat(name, color, 0.95, 0.0))
        o["smooth"] = True
        o["smooth_angle"] = 70.0
        objs.append(o)
    return objs


# ============================================================================================ timber: floor, crate
def build_timber():
    rng = random.Random(7)
    wood = mat("wood_planks", (0.58, 0.46, 0.33), 0.85, 0.0)
    dark = mat("wood_planks.old", (0.40, 0.31, 0.22), 0.9, 0.0)
    objs = []
    f = dir_at(0)
    right = Vector((-f.y, f.x, 0))     # (to the gunner's left, looking along the facing)
    yaw = math.degrees(math.atan2(f.y, f.x))
    # The floor: planks across the line of fire, on two bearers, on the mud.
    for s in (-1, 1):
        c = right * (s * 0.62)
        objs.append(box(f"p_bearer{s}", (2.3, 0.09, 0.05), (c.x - f.x * 0.25, c.y - f.y * 0.25, 0.022), dark, rot=(0, 0, yaw)))
    for i in range(11):
        along = -1.28 + i * 0.205
        length = 2.1 + rng.uniform(-0.12, 0.12) - abs(along + 0.25) * 0.5
        c = f * along + right * rng.uniform(-0.05, 0.05)
        objs.append(box(f"p_plank{i}", (0.185, length, 0.03), (c.x, c.y, 0.06), wood if i % 3 else dark, rot=(rng.uniform(-0.6, 0.6), 0, yaw + rng.uniform(-1.2, 1.2))))
    # The ammunition crate, to the gunner's right, against the bags: slatted sides, corner battens, the lid off and leaning.
    c = f * 0.35 - right * 1.2
    cy = yaw + 72
    q = Matrix.Rotation(math.radians(cy), 3, 'Z')
    sx, sy, sz = 0.86, 0.46, 0.44

    def at(x, y, z):
        return tuple(Vector((c.x, c.y, 0.0)) + q @ Vector((x, y, z)))
    for k in range(3):
        z = 0.11 + k * 0.14
        for s in (-1, 1):
            objs.append(box(f"p_crate_side{k}{s}", (sx, 0.022, 0.125), at(0, s * sy / 2, z), wood, rot=(0, 0, cy)))
            objs.append(box(f"p_crate_end{k}{s}", (0.022, sy, 0.125), at(s * sx / 2, 0, z), wood, rot=(0, 0, cy)))
    objs.append(box("p_crate_bottom", (sx, sy, 0.02), at(0, 0, 0.05), dark, rot=(0, 0, cy)))
    for s in (-1, 1):
        for t in (-1, 1):
            objs.append(box(f"p_crate_batten{s}{t}", (0.05, 0.05, sz), at(s * (sx / 2 - 0.0), t * (sy / 2 + 0.01), 0.05 + sz / 2 - 0.02), dark, rot=(0, 0, cy)))
    lid_c = at(sx / 2 + 0.16, 0, 0.27)
    objs.append(box("p_crate_lid", (0.5, sy + 0.04, 0.026), lid_c, wood, rot=(0, 62, cy)))
    return objs, (c, cy)


# ============================================================================================ steel: tripod, tins, bunker fittings
def add_tin(objs, name, center, yaw, olive, steel, open_lid=False):
    """A steel ammunition tin (29 x 15 x 19 cm): its body, seams, handle and latch; the lid hinged back if it's open."""
    q = Matrix.Rotation(yaw, 3, 'Z')
    w, d, h = 0.29, 0.15, 0.19
    deg = math.degrees(yaw)

    def at(x, y, z):
        return tuple(center + q @ Vector((x, y, z)))
    objs.append(add_bevel(box(name, (w, d, h - 0.03), at(0, 0, (h - 0.03) / 2), olive, rot=(0, 0, deg)), 0.004))
    objs.append(box(name + "_seam", (w + 0.006, d + 0.006, 0.012), at(0, 0, h - 0.035), olive, rot=(0, 0, deg)))
    if open_lid:
        objs.append(box(name + "_lid", (w, 0.03, d), at(0, d / 2 + 0.02, h - 0.03 + d / 2), olive, rot=(0, 0, deg)))
        objs.append(box(name + "_inside", (w - 0.012, d - 0.012, 0.004), at(0, 0, h - 0.033), mat("flat_recess", (0.02, 0.02, 0.02), 0.8, 0.0), rot=(0, 0, deg)))
    else:
        objs.append(add_bevel(box(name + "_lid", (w + 0.002, d + 0.002, 0.03), at(0, 0, h - 0.015), olive, rot=(0, 0, deg)), 0.004))
        objs.append(box(name + "_handle", (0.11, 0.022, 0.008), at(0, 0, h + 0.006), steel, rot=(0, 0, deg)))
        objs.append(box(name + "_latch", (0.02, 0.006, 0.05), at(w / 2 + 0.002, 0, h - 0.04), steel, rot=(0, 0, deg)))
    # The yellow stencil on its side.
    objs.append(box(name + "_stencil", (0.13, 0.002, 0.03), at(-0.02, -d / 2 - 0.001, h * 0.45), mat("flat_stencil", (0.62, 0.55, 0.22), 0.8, 0.0), rot=(0, 0, deg)))
    objs.append(box(name + "_stencil2", (0.09, 0.002, 0.016), at(-0.04, -d / 2 - 0.001, h * 0.28), mat("flat_stencil", (0.62, 0.55, 0.22), 0.8, 0.0), rot=(0, 0, deg)))


def build_steel(crate):
    olive = mat("painted_metal", (0.27, 0.30, 0.17), 0.65, 0.2)
    steel = mat("flat_steel", (0.10, 0.10, 0.10), 0.5, 0.8)
    rust = mat("rusty_metal", (0.45, 0.33, 0.25), 0.85, 0.5)
    objs = []
    # The tripod: a head on a pintle, three splayed legs, and a traversing bar between the back two.
    f = dir_at(0)
    head = Vector((CRADLE.x, CRADLE.y, 0.84))
    objs.append(cyl("p_head", 0.05, 0.12, tuple(head), olive, verts=18))
    objs.append(cyl("p_pintle", 0.018, 0.18, (CRADLE.x, CRADLE.y, CRADLE.z - 0.09), steel, verts=12))
    # The cradle the gun lies in: a saddle with a cheek either side.
    yaw = math.degrees(math.atan2(f.y, f.x))
    right = Vector((-f.y, f.x, 0))
    objs.append(box("p_saddle", (0.16, 0.08, 0.012), (CRADLE.x, CRADLE.y, CRADLE.z - 0.006), olive, rot=(0, 0, yaw)))
    for s in (-1, 1):
        c = CRADLE + right * (s * 0.038) + Vector((0, 0, 0.012))
        objs.append(box(f"p_cheek{s}", (0.13, 0.007, 0.05), tuple(c), olive, rot=(0, 0, yaw)))
    feet = []
    for name, a, reach in (("front", 0.0, 0.62), ("left", math.radians(128), 0.68), ("right", math.radians(-128), 0.68)):
        d = dir_at(a)
        foot = Vector((head.x + d.x * reach, head.y + d.y * reach, 0.085))
        knee = head.lerp(foot, 0.5)
        objs.append(tube(f"p_leg_{name}_u", [head - Vector((0, 0, 0.03)), knee], 0.024, olive))
        objs.append(tube(f"p_leg_{name}_l", [knee, foot], 0.017, olive))
        objs.append(add_bevel(box(f"p_foot_{name}", (0.11, 0.07, 0.012), (foot.x, foot.y, 0.08), steel, rot=(0, 0, math.degrees(math.atan2(d.y, d.x)))), 0.004))
        feet.append(knee)
    objs.append(tube("p_tie_bar", [feet[1], feet[2]], 0.011, steel))
    mid = feet[1].lerp(feet[2], 0.5)
    objs.append(tube("p_elevating_screw", [mid, Vector((CRADLE.x, CRADLE.y, CRADLE.z)) - f * 0.14 - Vector((0, 0, 0.01))], 0.012, steel))
    objs.append(tube("p_handwheel", [mid + f * 0.02 + right * 0.05, mid + f * 0.02 - right * 0.05], 0.009, steel))

    # Tins on the crate and at its foot: one open, three shut.
    c, cy = crate
    q = Matrix.Rotation(math.radians(cy), 3, 'Z')
    top = 0.47
    add_tin(objs, "p_tin0", Vector((c.x, c.y, top)) + q @ Vector((-0.2, 0.0, 0)), math.radians(cy), olive, steel)
    add_tin(objs, "p_tin1", Vector((c.x, c.y, top)) + q @ Vector((0.14, 0.03, 0)), math.radians(cy + 8), olive, steel)
    add_tin(objs, "p_tin2", Vector((c.x, c.y, top + 0.19)) + q @ Vector((-0.17, 0.0, 0)), math.radians(cy - 6), olive, steel)
    open_at = Vector((c.x, c.y, 0.075)) + q @ Vector((0.05, -0.42, 0))
    add_tin(objs, "p_tin_open", open_at, math.radians(cy - 18), olive, steel, open_lid=True)

    # The bunker's door (on its east side), its frame, and a vent pipe through the roof.
    bx, by = BUNKER_C
    door_x = bx + BUNKER_HALF.x
    objs.append(box("p_door", (0.05, 0.95, 1.95), (door_x - 0.11, by + 0.3, 1.02), rust))
    for k, z in enumerate((0.4, 1.0, 1.6)):
        objs.append(box(f"p_door_strap{k}", (0.012, 0.92, 0.07), (door_x - 0.08, by + 0.3, z), rust))
    objs.append(box("p_door_bolt", (0.02, 0.2, 0.03), (door_x - 0.07, by - 0.05, 1.05), steel))
    objs.append(cyl("p_vent", 0.06, 0.55, (bx - 0.9, by + 0.5, BUNKER_H + 0.5), rust, verts=12))
    objs.append(cyl("p_vent_cap", 0.1, 0.03, (bx - 0.9, by + 0.5, BUNKER_H + 0.8), rust, verts=12))
    # A bar across the firing slit's mouth.
    objs.append(box("p_slit_bar", (1.5, 0.03, 0.03), (bx, by - BUNKER_HALF.y - 0.012, 1.62), rust))
    return objs


# ============================================================================================ the bunker
def build_concrete():
    conc = mat("concrete_wall", (0.60, 0.58, 0.52), 0.92, 0.0)
    dark = mat("flat_recess", (0.02, 0.02, 0.02), 0.8, 0.0)
    bx, by = BUNKER_C
    hx, hy = BUNKER_HALF
    body = box("p_bunker", (2 * hx, 2 * hy, BUNKER_H + 0.1), (bx, by, BUNKER_H / 2 - 0.05), conc)
    # The firing slit: a splayed mouth narrowing into the dark.
    bm = bmesh.new()
    front = by - hy
    mouth = [(-0.95, 1.28), (0.95, 1.28), (0.95, 1.72), (-0.95, 1.72)]
    throat = [(-0.62, 1.42), (0.62, 1.42), (0.62, 1.60), (-0.62, 1.60)]
    a = [bm.verts.new((bx + x, front - 0.02, z)) for x, z in mouth]
    b = [bm.verts.new((bx + x, front + 0.75, z)) for x, z in throat]
    bm.faces.new(a)
    bm.faces.new(list(reversed(b)))
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    slit = K._new_obj("cut_slit", bm, conc)
    mod = body.modifiers.new("bool_slit", 'BOOLEAN'); mod.object = slit; mod.operation = 'DIFFERENCE'; mod.solver = 'EXACT'
    slit["cutter"] = True
    slit.hide_render = True
    # The doorway on the east side, a shallow recess with the door in it.
    door = box("cut_door", (0.3, 1.05, 2.05), (bx + hx, by + 0.3, 1.02), conc)
    mod = body.modifiers.new("bool_door", 'BOOLEAN'); mod.object = door; mod.operation = 'DIFFERENCE'; mod.solver = 'EXACT'
    door["cutter"] = True
    door.hide_render = True
    objs = [body]
    # The dark behind the slit.
    objs.append(box("p_slit_dark", (1.3, 0.02, 0.24), (bx, front + 0.74, 1.51), dark))
    # The roof slab, overhanging, and the shuttering's lines cast into the walls.
    roof = add_bevel(box("p_roof", (2 * hx + 0.5, 2 * hy + 0.5, 0.3), (bx, by, BUNKER_H + 0.15), conc), 0.03)
    objs.append(roof)
    objs.append(add_bevel(box("p_plinth", (2 * hx + 0.16, 2 * hy + 0.16, 0.22), (bx, by, 0.09), conc), 0.02))
    return objs


def build_stains():
    rng = random.Random(3)
    stain = mat("flat_stain", (0.31, 0.30, 0.26), 0.95, 0.0)
    moss = mat("flat_moss", (0.15, 0.21, 0.09), 1.0, 0.0)
    moss2 = mat("flat_moss.pale", (0.21, 0.26, 0.12), 1.0, 0.0)
    bx, by = BUNKER_C
    hx, hy = BUNKER_HALF
    objs = []
    # Dark runs down the front from the roof and from the slit's corners, and along the other walls.
    for i in range(7):
        x = bx + rng.uniform(-hx + 0.1, hx - 0.1)
        if abs(x - bx) < 1.0:
            continue
        h = rng.uniform(0.4, 1.3)
        objs.append(box(f"p_run_f{i}", (rng.uniform(0.02, 0.05), 0.006, h), (x, by - hy - 0.003, BUNKER_H - h / 2), stain))
    for i in range(5):
        y = by + rng.uniform(-hy + 0.1, hy - 0.1)
        h = rng.uniform(0.4, 1.3)
        objs.append(box(f"p_run_w{i}", (0.006, rng.uniform(0.02, 0.05), h), (bx - hx - 0.003, y, BUNKER_H - h / 2), stain))
    # Moss in cushions along the foot of the walls and over the roof's edge.
    bm = bmesh.new()
    bm2 = bmesh.new()

    def cushion(center, size):
        target = bm if rng.random() < 0.6 else bm2
        m = Matrix.Translation(center) @ Matrix.Diagonal((*size, 1.0))
        bmesh.ops.create_icosphere(target, subdivisions=1, radius=1.0, matrix=m)
    for i in range(90):
        side = rng.randrange(4)
        s = rng.uniform(-1, 1)
        sz = Vector((rng.uniform(0.05, 0.15), rng.uniform(0.05, 0.15), rng.uniform(0.02, 0.06)))
        if side == 0:
            p = Vector((bx + s * hx, by - hy - 0.08, 0.14))
        elif side == 1:
            p = Vector((bx + s * hx, by + hy + 0.08, 0.14))
        elif side == 2:
            p = Vector((bx - hx - 0.08, by + s * hy, 0.14))
        else:
            p = Vector((bx + hx + 0.08, by + s * hy, 0.14))
        cushion(p, sz)
    for i in range(60):
        s = rng.uniform(-1, 1)
        t = rng.uniform(-1, 1)
        p = Vector((bx + s * (hx + 0.2), by + t * (hy + 0.2), BUNKER_H + 0.305))
        cushion(p, Vector((rng.uniform(0.05, 0.2), rng.uniform(0.05, 0.2), rng.uniform(0.012, 0.03))))
    for i, (b, m) in enumerate(((bm, moss), (bm2, moss2))):
        o = K._new_obj(f"p_moss{i}", b, m)
        o["smooth"] = True
        objs.append(o)
    # Lianas let down over the front corner, from the trees behind.
    liana = mat("flat_liana", (0.14, 0.12, 0.08), 0.9, 0.0)
    for i, (x, sway) in enumerate(((-hx + 0.25, 0.1), (-hx + 0.7, -0.08), (hx - 0.4, 0.12))):
        pts = [Vector((bx + x + sway * math.sin(k * 0.9), by - hy - 0.28 - 0.03 * math.sin(k * 1.4), BUNKER_H + 0.3 - k * (BUNKER_H + 0.3) / 7 * (0.8 if i != 1 else 0.55))) for k in range(8)]
        objs.append(tube(f"p_liana{i}", pts, 0.012, liana, count=6))
    return objs


# ============================================================================================ rounds: belts and cases
def add_round(bm_brass, bm_copper, bm_steel, c, axis):
    """A round of 5.56 in its link, low in polygons: it's seen from a step away."""
    side = axis.cross(Vector((0.1, 0.2, 1.0))).normalized()
    up = axis.cross(side).normalized()
    n = 5

    def ring(bm, x, r):
        return [bm.verts.new(c + axis * x + side * (r * math.cos(math.tau * k / n)) + up * (r * math.sin(math.tau * k / n))) for k in range(n)]

    def skin(bm, rings):
        for a, b in zip(rings, rings[1:]):
            for k in range(n):
                j = (k + 1) % n
                bm.faces.new((a[k], a[j], b[j], b[k]))
        bm.faces.new(rings[0])
        bm.faces.new(list(reversed(rings[-1])))
    skin(bm_brass, [ring(bm_brass, -0.0285, 0.0046), ring(bm_brass, 0.006, 0.0045), ring(bm_brass, 0.015, 0.0032)])
    skin(bm_copper, [ring(bm_copper, 0.015, 0.0029), ring(bm_copper, 0.0285, 0.0006)])
    skin(bm_steel, [ring(bm_steel, -0.020, 0.0058), ring(bm_steel, -0.004, 0.0058)])


def belt_along(bms, path, axis_of):
    """Rounds one pitch apart along a polyline."""
    run = 0.0
    want = 0.0
    count = 0
    for a, b in zip(path, path[1:]):
        length = (b - a).length
        while want <= run + length:
            t = (want - run) / length
            add_round(*bms, a.lerp(b, t), axis_of(a.lerp(b, t)))
            want += 0.0119
            count += 1
        run += length
    return count


def smooth_path(points, n=10):
    """Catmull-Rom through points, so a belt lies in easy curves."""
    pts = [points[0]] + points + [points[-1]]
    out = []
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = pts[i - 1], pts[i], pts[i + 1], pts[i + 2]
        for k in range(n):
            t = k / n
            out.append(0.5 * ((2 * p1) + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t))
    out.append(points[-1])
    return out


def build_rounds(crate):
    brass = mat("flat_brass", (0.72, 0.55, 0.25), 0.4, 1.0)
    copper = mat("flat_copper", (0.62, 0.34, 0.2), 0.4, 1.0)
    links = mat("flat_link", (0.09, 0.09, 0.09), 0.5, 0.8)
    rng = random.Random(11)
    bms = [bmesh.new(), bmesh.new(), bmesh.new()]
    top = COURSES * COURSE + 0.05
    total = 0

    def over_wall(a0, a1, count, drop_out, drop_in):
        """A belt lying along the top of the bags from angle a0 to a1, each end hanging down a face of the wall."""
        pts = []
        start = dir_at(a0)
        pts.append(Vector((start.x * (2.02 + drop_out * 0.35), start.y * (2.02 + drop_out * 0.35), top - drop_out)))
        for k in range(count + 1):
            a = a0 + (a1 - a0) * k / count
            d = dir_at(a)
            pts.append(Vector((d.x * 2.02, d.y * 2.02, top + 0.015 * math.sin(k * 2.1))))
        end = dir_at(a1)
        pts.append(Vector((end.x * (2.02 - drop_in * 0.25), end.y * (2.02 - drop_in * 0.25), top - drop_in)))
        return smooth_path(pts)

    # Rounds lie square to the belt's run: pointing along the wall's radius on top of it.
    def radial(p):
        return Vector((p.x, p.y, 0)).normalized() * -1

    total += belt_along(bms, over_wall(-0.95, -0.45, 3, 0.55, 0.42), radial)
    total += belt_along(bms, over_wall(0.55, 0.95, 3, 0.62, 0.48), radial)
    # The one drawn out of the open tin, over the crate's corner to the floor in a loop.
    c, cy = crate
    q = Matrix.Rotation(math.radians(cy), 3, 'Z')
    tin = Vector((c.x, c.y, 0.25)) + q @ Vector((0.05, -0.42, 0))
    f = dir_at(0)
    side = Vector((-f.y, f.x, 0))
    loop = smooth_path([tin, tin + Vector((0, 0, 0.03)) + side * 0.10, tin + Vector((0, 0, -0.1)) + side * 0.22,
                        Vector((tin.x, tin.y, 0.09)) + side * 0.36 + f * 0.05, Vector((tin.x, tin.y, 0.085)) + side * 0.56 - f * 0.14,
                        Vector((tin.x, tin.y, 0.085)) + side * 0.34 - f * 0.34, Vector((tin.x, tin.y, 0.085)) + side * 0.1 - f * 0.22])
    total += belt_along(bms, loop, lambda p: (f * 0.8 + side * 0.2).normalized())
    print(f"[gun nest] {total} rounds in belts", flush=True)

    # Spent cases over the floor below where the gun throws them, some with their links.
    head = Vector((CRADLE.x, CRADLE.y, 0))
    for i in range(46):
        a = rng.uniform(-0.8, 1.3)
        r = rng.uniform(0.25, 1.1)
        d = dir_at(math.pi / 2 + a)
        p = head + d * r + Vector((0, 0, 0.082 + rng.uniform(0, 0.006)))
        axis = Vector((math.cos(rng.uniform(0, 6.3)), math.sin(rng.uniform(0, 6.3)), 0)).normalized()
        n = 6
        sd = axis.cross(Vector((0, 0, 1))).normalized()
        up = Vector((0, 0, 1))

        def ring(x, rr):
            return [bms[0].verts.new(p + axis * x + sd * (rr * math.cos(math.tau * k / n)) + up * (rr * math.sin(math.tau * k / n))) for k in range(n)]
        rings = [ring(-0.022, 0.0046), ring(0.012, 0.0044), ring(0.022, 0.0032)]
        for aa, bb in zip(rings, rings[1:]):
            for k in range(n):
                j = (k + 1) % n
                bms[0].faces.new((aa[k], aa[j], bb[j], bb[k]))
        bms[0].faces.new(rings[0])
        bms[0].faces.new(list(reversed(rings[-1])))

    objs = []
    for bm, m, name in zip(bms, (brass, copper, links), ("brass", "copper", "links")):
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        o = K._new_obj("p_rounds_" + name, bm, m)
        o["smooth"] = True
        objs.append(o)
    return objs


# ============================================================================================ output
def bake_triangles(o):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bm.to_mesh(o.data)
    bm.free()


def main():
    K.reset()
    timber, crate = build_timber()
    groups = {
        "Sandbags": build_sandbags(),
        "Timber": timber,
        "Steel": build_steel(crate),
        "Concrete": build_concrete(),
        "Stains": build_stains(),
        "Rounds": build_rounds(crate),
    }
    bpy.context.view_layer.update()
    for o in bpy.data.objects:
        if o.get("uv") in ("cyl", "ring"):
            o["uv"] = "box"
    final = []
    for name, objs in groups.items():
        out = K.finish(objs, join_name=name, smooth_angle=40.0)[0]
        bpy.ops.object.select_all(action='DESELECT')
        out.select_set(True)
        bpy.context.view_layer.objects.active = out
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        bake_triangles(out)
        final.append(out)
    for o in list(bpy.data.objects):
        if o.get("cutter"):
            bpy.data.objects.remove(o)
    K.export("gun_nest", final)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.PROJECT, "Assets", "gun_nest.blend"))
    K.preview("gun_nest", final, angle=(35, 25))
    K.preview("gun_nest_back", final, angle=(160, 30))
    K.preview("gun_nest_top", final, angle=(20, 62))


main()
