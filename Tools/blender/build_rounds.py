"""
Builds the rounds the long guns fire, one .glb per calibre (Assets/Models/round_<id>.glb), from each cartridge's published
dimensions:

    762x51   7.62 x 51 mm NATO (M80 ball, the G3): gilding-metal boat-tail bullet, brass case
    762x39   7.62 x 39 mm (M43, the AK-47): copper-washed flat-based bullet, lacquered steel case with its strong taper
    556x45   5.56 x 45 mm NATO (M855, the AR-15): boat-tail bullet with the green tip, brass case with its sharp shoulder
    40sw     .40 S&W (165 grain, the UMP40): flat-nosed bullet, straight-walled brass case

Each file holds these parts (Game/Debris.cs loads them by name; see Game/Cartridge.cs for the masses):

    Bullet, Case, Spent           drawn: the bullet in flight, the fired case (primer struck) and the bullet after it hit
                                  something hard (mushroomed, the lead showing)
    BulletHull, CaseHull,         not drawn: low-poly outlines the game makes Bullet convex hulls from, so a bottlenecked
    SpentHull                     case rolls in an arc as the real one does

Every part's origin is its own centre of mass, worked out from the profile (a case is much heavier at its head), since a
Bullet body turns about its origin. Model space: +Z (game) is the nose / the case's mouth, which is how GunBase.EjectCasing
holds a case and ProjectileSystem.Draw a bullet.

Run with:
    blender -b --factory-startup --python-exit-code 1 -P Tools/blender/build_rounds.py              (export, preview)
    blender -b --factory-startup --python-exit-code 1 -P Tools/blender/build_rounds.py -- nopreview (export only)
Then build the game, which copies the models next to the exe.
"""
import math
import os
import random
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import modelkit as K  # noqa: E402
from modelkit import lathe, mat  # noqa: E402

MM = 0.001
NOSE_FORWARD = (90, 0, 0)    # local Z (the lathe's axis) onto Blender -Y, which is the game's +Z

BRASS = dict(name="flat_case_brass", color=(0.80, 0.60, 0.26), rough=0.34, metal=1.0)
STEEL = dict(name="flat_case_lacquer", color=(0.31, 0.27, 0.17), rough=0.42, metal=0.55)
COPPER = dict(name="flat_jacket_copper", color=(0.80, 0.47, 0.30), rough=0.32, metal=1.0)
WASHED = dict(name="flat_jacket_washed", color=(0.70, 0.43, 0.30), rough=0.40, metal=1.0)
GREEN = dict(name="flat_tip_green", color=(0.16, 0.36, 0.20), rough=0.55, metal=0.0)
LEAD = dict(name="flat_lead", color=(0.42, 0.43, 0.46), rough=0.72, metal=0.6)
PRIMER = dict(name="flat_primer", color=(0.78, 0.55, 0.34), rough=0.45, metal=1.0)


def material(spec):
    return mat(spec["name"], spec["color"], spec["rough"], spec["metal"])


# Millimetres, from the cartridge's base. A case: rim radius and its height, the extractor groove (radius, from, to), the
# body at its base and where the shoulder starts, the neck, the mouth's height; the solid head's height, and the wall at
# the head and at the mouth. A bullet: its length, radius, the boat tail's base radius and height, where the ogive starts,
# the meplat's radius, and how far it sits out of the case (the overall length).
ROUNDS = {
    "762x51": dict(
        case_mat=BRASS, jacket=COPPER, tip=None, primer_r=2.65, length_all=71.1,
        rim=(6.00, 1.40), groove=(4.95, 1.75, 2.75), base_r=5.97, shoulder=(5.77, 39.6), neck=(4.39, 43.6), mouth=51.2,
        head=5.0, wall=(0.75, 0.34),
        bullet=dict(length=29.0, r=3.91, tail=(3.30, 4.0), ogive=12.5, meplat=0.55, cannelure=8.5)),
    "762x39": dict(
        case_mat=STEEL, jacket=WASHED, tip=None, primer_r=2.65, length_all=56.0,
        rim=(5.67, 1.50), groove=(4.75, 1.85, 2.85), base_r=5.65, shoulder=(5.03, 30.5), neck=(4.30, 33.2), mouth=38.7,
        head=4.5, wall=(0.65, 0.30),
        bullet=dict(length=26.8, r=3.96, tail=(3.45, 3.0), ogive=10.0, meplat=0.60, cannelure=6.5)),
    "556x45": dict(
        case_mat=BRASS, jacket=COPPER, tip=GREEN, primer_r=2.20, length_all=57.4,
        rim=(4.80, 1.15), groove=(4.20, 1.45, 2.25), base_r=4.79, shoulder=(4.50, 36.5), neck=(3.22, 39.6), mouth=44.7,
        head=4.0, wall=(0.55, 0.25),
        bullet=dict(length=23.0, r=2.85, tail=(2.35, 3.0), ogive=9.5, meplat=0.40, cannelure=7.5)),
    "9x19": dict(
        case_mat=BRASS, jacket=COPPER, tip=None, primer_r=2.20, length_all=29.69,
        rim=(4.98, 1.27), groove=(4.25, 1.55, 2.35), base_r=4.96, shoulder=None, neck=(4.82, 18.6), mouth=19.15,
        head=3.2, wall=(0.50, 0.25),
        bullet=dict(length=15.5, r=4.505, tail=None, ogive=8.5, meplat=1.40, cannelure=None)),
    "50bmg": dict(
        case_mat=BRASS, jacket=COPPER, tip=None, primer_r=4.40, length_all=138.43,
        rim=(10.21, 2.26), groove=(8.90, 2.80, 4.20), base_r=10.21, shoulder=(9.07, 70.0), neck=(7.11, 76.0), mouth=99.31,
        head=9.0, wall=(1.30, 0.60),
        bullet=dict(length=58.0, r=6.49, tail=(5.40, 8.0), ogive=25.0, meplat=0.90, cannelure=20.0)),
    "40sw": dict(
        case_mat=BRASS, jacket=COPPER, tip=None, primer_r=2.20, length_all=28.8,
        rim=(5.38, 1.30), groove=(4.60, 1.60, 2.40), base_r=5.38, shoulder=None, neck=(5.37, 21.0), mouth=21.6,
        head=3.5, wall=(0.55, 0.28),
        bullet=dict(length=15.0, r=5.08, tail=None, ogive=7.5, meplat=2.90, cannelure=None)),
}


# --------------------------------------------------------------------------------------------- profiles
def case_outer(c):
    """The case's outside from the base's edge to the mouth, without the axis: [(radius, z), ...] in mm."""
    rim_r, rim_h = c["rim"]
    groove_r, groove_a, groove_b = c["groove"]
    pts = [(rim_r - 0.35, 0.0), (rim_r, 0.30), (rim_r, rim_h), (groove_r, groove_a), (groove_r, groove_b), (c["base_r"], groove_b + 0.75)]
    if c["shoulder"]:
        pts += [c["shoulder"]]
        # A small radius where the body meets the shoulder, and again at the neck.
        r_s, z_s = c["shoulder"]
        r_n, z_n = c["neck"]
        pts += [(r_s - (r_s - r_n) * 0.08, z_s + (z_n - z_s) * 0.10), (r_n + (r_s - r_n) * 0.06, z_n - (z_n - z_s) * 0.06), (r_n, z_n)]
    pts += [(c["neck"][0], c["mouth"])]
    return pts


def case_inner(c):
    """The inside from the mouth's lip down to the axis at the head's top, following the outside at the wall's thickness."""
    head_wall, mouth_wall = c["wall"]
    pts = []
    for r, z in reversed(case_outer(c)):
        if z <= c["head"]:
            break
        t = (z - c["head"]) / (c["mouth"] - c["head"])
        pts.append((r - (head_wall + (mouth_wall - head_wall) * t), z))
    pts.append((pts[-1][0] - 0.05, c["head"] + 1.0))
    pts.append((pts[-1][0] * 0.82, c["head"]))
    pts.append((0.0, c["head"]))
    return pts


def bullet_outline(b, rings=9):
    """The bullet's outline from the axis at its base to the axis at its nose (a tangent ogive)."""
    r, length = b["r"], b["length"]
    pts = [(0.0, 0.0)]
    if b["tail"]:
        pts += [(b["tail"][0], 0.0), (r, b["tail"][1])]
    else:
        pts += [(r - 0.25, 0.0), (r, 0.3)]
    if b.get("cannelure"):
        z = b["cannelure"]
        pts += [(r, z - 0.6), (r - 0.10, z - 0.25), (r - 0.10, z + 0.25), (r, z + 0.6)]
    start, tip = b["ogive"], b["meplat"]
    ogive_len = length - start
    # The radius of the arc tangent to the body at the ogive's start and passing through the meplat's edge.
    rho = ((r - tip) ** 2 + ogive_len ** 2) / (2 * (r - tip))
    for i in range(rings + 1):
        z = ogive_len * i / rings
        pts.append((r - rho + math.sqrt(rho * rho - z * z), start + z))
    # The nose rounds over the last little way, and a flat-nosed pistol bullet keeps its flat.
    pts.append((tip * 0.6, length + tip * 0.08))
    pts.append((0.0, length + tip * 0.10))
    return pts


def centre_of_mass(rings):
    """Height (mm) of the centre of a solid of revolution given as slices [(radius_out, radius_in, z), ...]."""
    volume = moment = 0.0
    for (r0, i0, z0), (r1, i1, z1) in zip(rings, rings[1:]):
        dz = z1 - z0
        if dz <= 0:
            continue
        v = math.pi * ((r0 * r0 + r1 * r1) / 2 - (i0 * i0 + i1 * i1) / 2) * dz
        volume += v
        moment += v * (z0 + z1) / 2
    return moment / volume, volume


def radius_at(profile, z):
    pts = sorted((p for p in profile), key=lambda p: p[1])
    if z <= pts[0][1]:
        return pts[0][0]
    for (r0, z0), (r1, z1) in zip(pts, pts[1:]):
        if z0 <= z <= z1 and z1 > z0:
            return r0 + (r1 - r0) * (z - z0) / (z1 - z0)
    return pts[-1][0]


def case_centre(c):
    outer, inner = case_outer(c), case_inner(c)
    steps = [c["mouth"] * i / 120 for i in range(121)]
    return centre_of_mass([(radius_at(outer, z), radius_at(inner, z) if z >= c["head"] else 0.0, z) for z in steps])


def bullet_centre(b):
    outline = bullet_outline(b)[1:-1]
    steps = [b["length"] * i / 120 for i in range(121)]
    return centre_of_mass([(radius_at(outline, z), 0.0, z) for z in steps])


def shifted(profile, z0):
    return [(r * MM, (z - z0) * MM) for r, z in profile]


# --------------------------------------------------------------------------------------------- the parts
def build_bullet(c):
    b = c["bullet"]
    com, _ = bullet_centre(b)
    outline = bullet_outline(b)
    if c["tip"]:
        # The painted tip: the outline above where the paint starts is a second object.
        paint = b["length"] - 7.0
        r = radius_at(outline[1:-1], paint)
        body = [p for p in outline if p[1] < paint] + [(r, paint), (0.0, paint)]
        tip = [(0.0, paint), (r, paint)] + [p for p in outline if p[1] > paint]
        parts = [lathe("BulletBody", shifted(body, com), material=material(c["jacket"]), verts=20, rot=NOSE_FORWARD),
                 lathe("BulletTip", shifted(tip, com), material=material(c["tip"]), verts=20, rot=NOSE_FORWARD)]
    else:
        parts = [lathe("BulletBody", shifted(outline, com), material=material(c["jacket"]), verts=20, rot=NOSE_FORWARD)]
    return K.finish(parts, join_name="Bullet")


def build_case(c):
    com, _ = case_centre(c)
    # Outside, the lip, then back down the inside: closed, so the mouth looks hollow.
    profile = [(0.0, 0.0)] + case_outer(c) + case_inner(c)
    case = lathe("CaseBody", shifted(profile, com), material=material(c["case_mat"]), verts=20, rot=NOSE_FORWARD)
    # The primer, set in the base, with the firing pin's dent in it (this case has been fired).
    pr = c["primer_r"]
    primer_profile = [(0.0, 0.34), (0.55, 0.26), (0.95, -0.02), (pr - 0.10, -0.03), (pr, 0.06), (pr, 0.6), (0.0, 0.6)]
    primer = lathe("CasePrimer", shifted(primer_profile, com), material=material(PRIMER), verts=16, rot=NOSE_FORWARD)
    return K.finish([case, primer], join_name="Case")


def build_spent(c, rng):
    """After a hard stop: about two thirds of its length, the nose peeled back into a lumpy mushroom, the lead showing."""
    b = c["bullet"]
    r = b["r"]
    length = b["length"] * (0.70 if b["length"] > 18 else 0.74)
    bloom = r * (1.75 if b["length"] > 18 else 1.55)
    shank = length * 0.52
    jacket_profile = [(0.0, 0.0), (r * 0.92, 0.0), (r, 0.5), (r * 1.02, shank * 0.55), (r * 1.14, shank), (bloom * 0.86, length * 0.72),
                      (bloom, length * 0.84), (bloom * 0.93, length * 0.93), (bloom * 0.72, length * 0.90), (0.0, length * 0.86)]
    lead_profile = [(0.0, length * 0.84), (bloom * 0.74, length * 0.86), (bloom * 0.56, length * 0.99), (bloom * 0.24, length * 1.03), (0.0, length * 1.02)]
    rings = [(radius_at(jacket_profile[1:-1], length * i / 80), 0.0, length * i / 80) for i in range(81)]
    com, _ = centre_of_mass(rings)
    jacket = lathe("SpentJacket", shifted(jacket_profile, com), material=material(c["jacket"]), verts=22, rot=(0, 0, 0))
    lead = lathe("SpentLead", shifted(lead_profile, com), material=material(LEAD), verts=22, rot=(0, 0, 0))
    # Peel the petals and bend the shank: nothing that hit steel stays round.
    petals = rng.randint(4, 6)
    phase = rng.uniform(0, math.tau)
    lean = rng.uniform(0.06, 0.12)
    for obj in (jacket, lead):
        for v in obj.data.vertices:
            x, y, z = v.co
            a = math.atan2(y, x)
            h = (z / MM + com) / length                         # 0 at the base, 1 at the nose
            flare = max(0.0, (h - 0.45) / 0.55)
            scale = 1 + flare * (0.20 * math.sin(petals * a + phase) + 0.08 * math.sin(2 * petals * a + phase * 2.0) + rng.uniform(-0.03, 0.03))
            v.co.x = x * scale + lean * r * MM * flare * flare * 2
            v.co.y = y * scale
            v.co.z = z * (1 + 0.12 * flare * math.sin(petals * a + phase + 1.0))
    for obj in (jacket, lead):
        obj.rotation_euler = tuple(math.radians(a) for a in NOSE_FORWARD)
    return K.finish([jacket, lead], join_name="Spent", smooth_angle=55.0)


def hull(name, profile, verts=10):
    """A plain outline for the physics: never drawn, so it can stay as coarse as Bullet likes its hulls."""
    obj = lathe(name, profile, material=mat("flat_hull", (1, 0, 1), 1.0, 0.0), verts=verts, rot=NOSE_FORWARD)
    return K.finish([obj])[0]


def build_hulls(c):
    b = c["bullet"]
    com_b, _ = bullet_centre(b)
    r, length = b["r"], b["length"]
    tail = b["tail"][0] if b["tail"] else r - 0.25
    mid = (r + b["meplat"]) * 0.62
    bullet_hull = hull("BulletHull", shifted([(0.0, 0.0), (tail, 0.0), (r, b["tail"][1] if b["tail"] else 0.3), (r, b["ogive"]),
                                              (mid, (b["ogive"] + length) * 0.55), (b["meplat"], length), (0.0, length)], com_b))
    com_c, _ = case_centre(c)
    rim_r, _ = c["rim"]
    points = [(0.0, 0.0), (rim_r, 0.0), (c["base_r"], c["groove"][2])]
    if c["shoulder"]:
        points += [c["shoulder"], c["neck"]]
    points += [(c["neck"][0], c["mouth"]), (0.0, c["mouth"])]
    case_hull = hull("CaseHull", shifted(points, com_c), verts=12)
    length_s = length * (0.70 if length > 18 else 0.74)
    bloom = r * (1.75 if length > 18 else 1.55)
    rings = [(radius_at([(r, 0), (r * 1.14, length_s * 0.52), (bloom, length_s * 0.84), (bloom * 0.56, length_s)], length_s * i / 80), 0.0, length_s * i / 80) for i in range(81)]
    com_s, _ = centre_of_mass(rings)
    spent_hull = hull("SpentHull", shifted([(0.0, 0.0), (r, 0.0), (r * 1.14, length_s * 0.52), (bloom, length_s * 0.84), (bloom * 0.56, length_s), (0.0, length_s)], com_s), verts=8)
    return [bullet_hull, case_hull, spent_hull]


# --------------------------------------------------------------------------------------------- export and preview
def build_round(name):
    c = ROUNDS[name]
    rng = random.Random(name)
    return build_bullet(c) + build_case(c) + build_spent(c, rng) + build_hulls(c)


def report(name):
    c = ROUNDS[name]
    com_c, vol_c = case_centre(c)
    com_b, vol_b = bullet_centre(c["bullet"])
    density = 7.85 if c["case_mat"] is STEEL else 8.5
    print(f"[round] {name}: case {vol_c * density / 1000:.1f} g of metal, centre {com_c / c['mouth'] * 100:.0f}% up from the head; "
          f"bullet {vol_b:.0f} mm3, centre {com_b / c['bullet']['length'] * 100:.0f}% up from the base", flush=True)


def lineup():
    """All four side by side for checking: the loaded round, the bullet alone, the fired case, the spent bullet."""
    K.reset()
    drawn = []
    for row, name in enumerate(ROUNDS):
        c = ROUNDS[name]
        y = -(row - 1.5) * 19 * MM
        bullet = build_bullet(c)[0]
        case = build_case(c)[0]
        spent = build_spent(c, random.Random(name))[0]
        loaded_case = case.copy(); loaded_case.data = case.data.copy(); bpy.context.scene.collection.objects.link(loaded_case)
        loaded_bullet = bullet.copy(); loaded_bullet.data = bullet.data.copy(); bpy.context.scene.collection.objects.link(loaded_bullet)
        com_c, _ = case_centre(c)
        com_b, _ = bullet_centre(c["bullet"])
        seat = c["length_all"] - c["bullet"]["length"]
        # Nose along +X for the picture; the row's cartridge base at x = 0.
        for obj, x in ((loaded_case, com_c), (loaded_bullet, seat + com_b), (bullet, 90 + com_b), (case, 130 + com_c), (spent, 200.0)):
            obj.rotation_euler = (0, 0, math.radians(90))
            obj.location = (x * MM, y, c["rim"][0] * MM)
            drawn.append(obj)
    bpy.context.view_layer.update()
    K.preview("rounds_lineup", drawn, angle=(0, 52))


def main():
    for name in ROUNDS:
        K.reset()
        objs = build_round(name)
        K.export("round_" + name, objs)
        report(name)
    if "nopreview" not in sys.argv:
        lineup()


main()
