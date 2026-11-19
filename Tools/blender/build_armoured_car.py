"""
Builds the armoured 4x4 (a desert-sand MRAP-style patrol vehicle with a gunner's turret on the roof, after a three-quarter
photo) and exports two models:

    Assets/Models/armoured_car.glb          the hull, wheels and everything fixed to them
    Assets/Models/armoured_car_turret.glb   the turret ring and its armoured shield, which turns (Game/GunNest.cs)

    blender -b --factory-startup -P Tools/blender/build_armoured_car.py [-- preview]

Coordinates: metres; Blender +Y is the front (the game's -Z), +Z is up, symmetric about X = 0. The hull's origin is on the
ground under the middle of the body. The turret's origin is the middle of its ring, on the roof (TURRET_AT in the hull's
space), with its front along +Y. The M249 lies on the turret's cradle (CRADLE_* below), whose numbers are repeated in
Level.ArmouredCar* (Game/LevelArmouredCar.cs): keep them in step.

Overall about 5.75 m long, 2.48 m wide over the flares, 2.27 m to the roof and 2.9 m to the top of the shield.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import modelkit as K  # noqa: E402
from modelkit import box, cyl, mat  # noqa: E402

# ============================================================================================ dimensions
Z_BELLY, Z_ROOF = 0.48, 2.27
Y_FA, Y_RA = 1.62, -1.68          # axles
TRACK = 2.02                      # between the tyres' middles
R_TYRE, W_TYRE = 0.56, 0.40       # 395/85 R20 run-flats
ARCH_R = 0.68
TURRET_AT = Vector((0.0, -0.30, Z_ROOF))   # the middle of the turret ring, on the roof
CRADLE_AHEAD, CRADLE_UP = 0.80, 0.36       # the gun's cradle: this far ahead of the ring's middle, this far over the roof

# The side profile (y, z), round the hull: belly, nose, bonnet, windscreen, roof, back.
PROFILE = [(2.05, Z_BELLY), (2.60, 0.80), (2.70, 1.30), (2.58, 1.40), (1.45, 1.52), (1.30, 1.56), (0.80, 2.20),
           (0.62, Z_ROOF), (-2.55, Z_ROOF), (-2.72, 2.18), (-2.80, 1.00), (-2.72, 0.80), (-2.15, Z_BELLY)]
W_KEYS = [(Z_BELLY, 0.52), (0.95, 1.12), (1.00, 1.15), (1.56, 1.15), (Z_ROOF, 1.03)]


def W(z):
    """The hull's half width at height z (the V of the belly, the slab sides, the tumblehome)."""
    if z <= W_KEYS[0][0]:
        return W_KEYS[0][1]
    for (z0, w0), (z1, w1) in zip(W_KEYS, W_KEYS[1:]):
        if z <= z1:
            return w0 + (w1 - w0) * (z - z0) / (z1 - z0)
    return W_KEYS[-1][1]


SAND = (0.74, 0.62, 0.44)
M = {}


def materials():
    M.update(
        paint=mat("flat_sand_paint", SAND, 0.68, 0.12),
        paint_dark=mat("flat_sand_paint_dark", tuple(c * 0.82 for c in SAND), 0.72, 0.1),
        black=mat("flat_black_steel", (0.045, 0.045, 0.047), 0.55, 0.35),
        rubber=mat("flat_rubber", (0.03, 0.03, 0.032), 0.85, 0.0),
        glass=mat("flat_armour_glass", (0.05, 0.065, 0.07), 0.04, 0.0),
        gap=mat("flat_gap", (0.012, 0.012, 0.012), 0.9, 0.0),
        rim=mat("flat_rim_black", (0.05, 0.05, 0.052), 0.5, 0.45),
        steel=mat("steel"),
        grille=mat("cut_grille_honeycomb", None, 0.35, 0.0),
        lamp=mat("emit_lamp", (1.0, 0.97, 0.9), emit=(1.0, 0.97, 0.9)),
        amber=mat("flat_amber", (0.85, 0.45, 0.05), 0.2, 0.0),
        red=mat("flat_tail_red", (0.55, 0.03, 0.02), 0.2, 0.0),
        hole=mat("flat_hatch_dark", (0.01, 0.01, 0.01), 0.95, 0.0),
    )


def mesh_obj(name, bm, material, uv="box", smooth=False, smooth_angle=35.0):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(material)
    o["uv"] = uv
    if smooth:
        o["smooth"] = True
        o["smooth_angle"] = smooth_angle
    return o


def sweep(name, rows, material, caps=True, smooth=False):
    """A solid through rows of points (each row a closed loop of the same count), capped at both ends."""
    bm = bmesh.new()
    vr = [[bm.verts.new(p) for p in row] for row in rows]
    n = len(rows[0])
    for a, b in zip(vr, vr[1:]):
        for i in range(n):
            i2 = (i + 1) % n
            bm.faces.new((a[i], a[i2], b[i2], b[i]))
    if caps:
        bm.faces.new(vr[0])
        bm.faces.new(list(reversed(vr[-1])))
    return mesh_obj(name, bm, material, smooth=smooth)


def quad_solid(name, corners, normal, depth, material):
    """A thin plate: four corners (outer face) and the same pushed in along -normal by depth."""
    n = Vector(normal).normalized()
    outer = [Vector(c) for c in corners]
    inner = [c - n * depth for c in outer]
    return sweep(name, [inner, outer], material)


# ============================================================================================ hull
def hull():
    bm = bmesh.new()
    right = [bm.verts.new((W(z), y, z)) for y, z in PROFILE]
    left = [bm.verts.new((-W(z), y, z)) for y, z in PROFILE]
    bm.faces.new(right)
    bm.faces.new(list(reversed(left)))
    n = len(PROFILE)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((right[i], left[i], left[j], right[j]))
    # Extra edge loops where the V of the belly turns into the slab sides, so they shade apart.
    o = mesh_obj("hull", bm, M["paint"])
    # The wheel arches, cut out of the sides only (the middle of the hull is the crew's space, not cut).
    for y in (Y_FA, Y_RA):
        for sx in (-1, 1):
            c = cyl("arch_cut", ARCH_R, 0.9, (sx * 1.20, y, R_TYRE), None, verts=40, rot=(0, 90, 0))
            mod = o.modifiers.new("arch", 'BOOLEAN')
            mod.operation = 'DIFFERENCE'
            mod.solver = 'EXACT'
            mod.object = c
            bpy.context.view_layer.objects.active = o
            bpy.ops.object.modifier_apply(modifier=mod.name)
            bpy.data.objects.remove(c)
    return [o]


def arch_flare(y, sx):
    """The sand-coloured flare round a wheel arch: a band from the front of the arch over the top to the back."""
    rows = []
    for k in range(25):
        a = math.pi * k / 24          # 0 = front, pi = back
        r0, r1 = ARCH_R + 0.005, ARCH_R + 0.11
        cy, cz = math.cos(a), math.sin(a)
        if cz * (r1) + R_TYRE < 0.82 and 0 < k < 24:
            pass
        x0, x1 = sx * 1.08, sx * 1.255
        p = lambda r, x: (x, y + cy * r, R_TYRE + cz * r)   # noqa: E731
        rows.append([p(r0, x0), p(r1, x0), p(r1, x1 - sx * 0.04), p(r0 + 0.02, x1)])
    if sx < 0:
        rows = [list(reversed(r)) for r in rows]
    return sweep("flare", rows, M["paint"])


def flares():
    out = []
    for y in (Y_FA, Y_RA):
        for sx in (-1, 1):
            out.append(arch_flare(y, sx))
            # The flat-topped wing over it, out to the flare (the photo's squared-off fenders).
            out.append(box("wing", (0.14, 1.52, 0.05), (sx * 1.19, y, R_TYRE + ARCH_R + 0.09), M["paint"], bevel=0.01))
            # Mud flap behind it.
            out.append(box("mudflap", (0.34, 0.012, 0.36), (sx * 1.02, y - ARCH_R - 0.06, 0.40), M["rubber"]))
    return out


def side_panel(name, sx, y0, y1, z0, z1, material, proud=0.012, depth=0.03, inset=0.0):
    """A panel lying on the hull's side (x = W(z)), from y0 to y1 and z0 to z1."""
    xs = lambda z: sx * (W(z) + proud - inset)   # noqa: E731
    corners = [(xs(z0), y0, z0), (xs(z0), y1, z0), (xs(z1), y1, z1), (xs(z1), y0, z1)]
    # The outward normal of the side there (it leans in above z = 1.56).
    lean = (W(z1) - W(z0)) / max(z1 - z0, 1e-4)
    normal = Vector((sx, 0, -lean * 1.0)).normalized()
    if sx < 0:
        corners = list(reversed(corners))
    return quad_solid(name, corners, normal, depth, material)


def strip_v(name, sx, y, z0, z1, material, width=0.012):
    return side_panel(name, sx, y - width / 2, y + width / 2, z0, z1, material, proud=0.004, depth=0.01)


def strip_h(name, sx, y0, y1, z, material, width=0.012):
    return side_panel(name, sx, y0, y1, z - width / 2, z + width / 2, material, proud=0.004, depth=0.01)


def windscreen_y(z):
    """Where the windscreen is at height z (it rakes back from y 1.30 at z 1.56 to y 0.80 at z 2.20)."""
    return 1.30 + (0.80 - 1.30) * (z - 1.56) / (2.20 - 1.56)


def sides():
    out = []
    for sx in (-1, 1):
        # Front door: big armoured window, the frame following the A pillar.
        zw0, zw1 = 1.66, 2.12
        yA0, yA1 = windscreen_y(zw0) - 0.13, windscreen_y(zw1) - 0.13
        xs = lambda z, p=0.016: sx * (W(z) + p)   # noqa: E731
        # Rubber frame, then the glass inside it.
        for (pad, prd, m, nm) in ((0.035, 0.012, M["rubber"], "door_win_frame"), (0.0, 0.022, M["glass"], "door_win")):
            corners = [(xs(zw0 - pad, prd), yA0 + pad * 0.3, zw0 - pad), (xs(zw0 - pad, prd), 0.42 - pad, zw0 - pad),
                       (xs(zw1 + pad, prd), 0.42 - pad, zw1 + pad), (xs(zw1 + pad, prd), yA1 + pad * 0.3, zw1 + pad)]
            if sx < 0:
                corners = list(reversed(corners))
            out.append(quad_solid(nm, corners, (sx, 0, -0.17), 0.03, m))
        # Front door shut lines, handle, hinges.
        out.append(strip_v("door_line", sx, 0.33, 0.98, 2.16, M["gap"]))
        out.append(strip_v("door_line", sx, 1.33, 0.98, 1.56, M["gap"]))
        out.append(strip_h("door_line", sx, 0.33, 1.33, 0.985, M["gap"]))
        out.append(box("handle", (0.04, 0.18, 0.035), (sx * 1.175, 0.52, 1.50), M["black"], bevel=0.008))
        for z in (1.20, 1.85):
            out.append(box("hinge", (0.05, 0.08, 0.12), (sx * 1.17, 1.30, z), M["black"], bevel=0.01))
        # Rear (crew) door: a smaller window, its own handle and hinges.
        for (pad, prd, m, nm) in ((0.03, 0.012, M["rubber"], "rdoor_win_frame"), (0.0, 0.022, M["glass"], "rdoor_win")):
            out.append(side_panel(nm, sx, -0.62 - pad, 0.10 + pad, 1.72 - pad, 2.10 + pad, m, proud=prd))
        out.append(strip_v("rdoor_line", sx, -0.80, 0.98, 2.16, M["gap"]))
        out.append(strip_v("rdoor_line", sx, 0.26, 0.98, 2.16, M["gap"]))
        out.append(strip_h("rdoor_line", sx, -0.80, 0.26, 0.985, M["gap"]))
        out.append(box("handle", (0.04, 0.18, 0.035), (sx * 1.175, -0.62, 1.50), M["black"], bevel=0.008))
        for z in (1.20, 1.85):
            out.append(box("hinge", (0.05, 0.08, 0.12), (sx * 1.17, 0.18, z), M["black"], bevel=0.01))
        # Two small square windows towards the back, each with a firing port under it.
        for yc in (-1.25, -2.05):
            for (pad, prd, m, nm) in ((0.03, 0.012, M["rubber"], "rwin_frame"), (0.0, 0.022, M["glass"], "rwin")):
                out.append(side_panel(nm, sx, yc - 0.22 - pad, yc + 0.22 + pad, 1.74 - pad, 2.08 + pad, m, proud=prd))
            out.append(box("port", (0.05, 0.13, 0.13), (sx * 1.17, yc, 1.45), M["black"], bevel=0.01))
            out.append(cyl("port_plug", 0.04, 0.03, (sx * 1.20, yc, 1.45), M["steel"], verts=16, rot=(0, 90, 0)))
        # The black step under the front door and the sand locker under the crew door, with its latches.
        out.append(box("step", (0.24, 0.78, 0.05), (sx * 1.10, 0.75, 0.70), M["black"], bevel=0.01))
        for y in (0.45, 1.05):
            out.append(box("step_bracket", (0.05, 0.05, 0.22), (sx * 1.02, y, 0.80), M["black"]))
        out.append(box("locker", (0.12, 1.30, 0.42), (sx * 1.13, -0.25, 0.86), M["paint"], bevel=0.015))
        for y in (-0.70, -0.25, 0.20):
            out.append(box("latch", (0.03, 0.07, 0.05), (sx * 1.195, y, 1.02), M["black"]))
        out.append(strip_h("locker_line", sx, -0.88, 0.38, 0.86, M["gap"]))
        # A grab rail and the lifting eyes.
        out.append(K.tube("grab", [(sx * 1.17, -2.45, 1.70), (sx * 1.22, -2.42, 1.70), (sx * 1.22, -2.42, 2.05), (sx * 1.17, -2.45, 2.05)], 0.014, M["black"], verts=8))
        # Mirror on its arm off the A pillar.
        arm = [(sx * 1.08, 1.28, 1.90), (sx * 1.32, 1.36, 1.98), (sx * 1.42, 1.36, 2.02)]
        out.append(K.tube("mirror_arm", arm, 0.016, M["black"], verts=8))
        out.append(box("mirror", (0.07, 0.10, 0.30), (sx * 1.45, 1.34, 2.00), M["black"], bevel=0.015))
        out.append(box("mirror_glass", (0.01, 0.08, 0.26), (sx * 1.45, 1.285, 2.00), M["glass"]))
    return out


def windscreen():
    out = []
    base, top = Vector((0, 1.30, 1.56)), Vector((0, 0.80, 2.20))
    d = (top - base).normalized()
    n = Vector((0, -d.z, d.y)) * -1     # forward and up
    if n.y < 0:
        n = -n
    for sx in (-1, 1):
        for (pad, prd, m, nm) in ((0.035, 0.012, M["rubber"], "ws_frame"), (0.0, 0.024, M["glass"], "ws")):
            t0, t1 = 0.10 - pad, 0.90 + pad
            x0, x1 = 0.07 - pad * 0.5, 0.0
            pts = []
            for t, xin in ((t0, True), (t0, False), (t1, False), (t1, True)):
                p = base.lerp(top, t)
                xo = W(p.z) - 0.13 + pad
                x = (x0 if xin else xo) * sx
                pts.append(Vector((x, p.y, p.z)) + n * prd)
            if sx < 0:
                pts = list(reversed(pts))
            out.append(quad_solid(nm, pts, n, 0.03, m))
        # Wipers.
        p = base.lerp(top, 0.12)
        out.append(box("wiper", (0.5, 0.015, 0.015), (sx * 0.45, p.y + 0.03, p.z + 0.02), M["black"], rot=(0, 0, 0)))
    return out


def front():
    out = []
    # The black grille over the whole nose: a frame, the honeycomb in it and a heavy cross bar.
    out.append(box("grille_frame", (1.62, 0.06, 0.50), (0, 2.69, 1.03), M["black"], bevel=0.015))
    out.append(box("grille", (1.42, 0.02, 0.38), (0, 2.725, 1.03), M["grille"]))
    out.append(box("grille_back", (1.42, 0.02, 0.38), (0, 2.70, 1.03), M["gap"]))
    out.append(box("grille_bar", (1.50, 0.05, 0.05), (0, 2.735, 1.08), M["black"], bevel=0.01))
    for x in (-0.36, 0.0, 0.36):
        out.append(box("grille_post", (0.04, 0.05, 0.40), (x, 2.735, 1.03), M["black"]))
    # The two light bars on its top corners, and the headlamps low in the corners.
    for sx in (-1, 1):
        out.append(box("light_bar", (0.26, 0.05, 0.05), (sx * 0.52, 2.73, 1.28), M["black"], bevel=0.01))
        for k in range(4):
            out.append(box("led", (0.04, 0.01, 0.03), (sx * (0.43 + k * 0.06), 2.757, 1.28), M["lamp"]))
        out.append(box("lamp_box", (0.28, 0.12, 0.18), (sx * 0.98, 2.66, 1.12), M["black"], bevel=0.02))
        out.append(box("lamp", (0.20, 0.01, 0.11), (sx * 0.98, 2.722, 1.12), M["lamp"]))
        out.append(box("indicator", (0.08, 0.01, 0.05), (sx * 0.98, 2.722, 1.00), M["amber"]))
        # Bonnet vents by the windscreen.
        out.append(box("vent", (0.42, 0.24, 0.025), (sx * 0.62, 1.70, 1.515), M["black"], rot=(-5, 0, 0)))
        for k in range(5):
            out.append(box("vent_slat", (0.40, 0.012, 0.03), (sx * 0.62, 1.60 + k * 0.05, 1.53), M["gap"], rot=(-5, 0, 0)))
        # Bonnet hinges/catches.
        out.append(box("catch", (0.06, 0.08, 0.04), (sx * 0.85, 2.52, 1.42), M["black"]))
    # The bumper: a heavy black steel beam with tow shackles and a winch fairlead.
    out.append(box("bumper", (2.20, 0.26, 0.24), (0, 2.80, 0.72), M["black"], bevel=0.02))
    out.append(box("bumper_lower", (1.60, 0.20, 0.12), (0, 2.72, 0.55), M["black"], bevel=0.015, rot=(20, 0, 0)))
    out.append(box("fairlead", (0.28, 0.05, 0.08), (0, 2.94, 0.72), M["steel"], bevel=0.01))
    for sx in (-1, 1):
        out.append(K.lathe("shackle", [(0.045, -0.02), (0.06, 0.0), (0.045, 0.02)], (sx * 0.72, 2.97, 0.66), M["steel"], verts=16, rot=(0, 90, 0)))
        out.append(box("hook_plate", (0.05, 0.08, 0.14), (sx * 0.72, 2.94, 0.72), M["black"]))
    # Number plate blank.
    out.append(box("plate", (0.34, 0.01, 0.10), (0, 2.935, 0.80), M["paint_dark"]))
    return out


def rear():
    out = []
    out.append(box("rear_bumper", (2.10, 0.22, 0.20), (0, -2.86, 0.78), M["black"], bevel=0.02))
    # The back door (a big single door, hinged on the right) with a small window.
    out.append(box("rear_door", (1.30, 0.03, 1.10), (0.0, -2.795, 1.55), M["paint"], bevel=0.01))
    out.append(box("rear_win_frame", (0.42, 0.02, 0.34), (0.0, -2.82, 1.80), M["rubber"]))
    out.append(box("rear_win", (0.36, 0.02, 0.28), (0.0, -2.83, 1.80), M["glass"]))
    out.append(box("rear_handle", (0.20, 0.05, 0.04), (-0.45, -2.83, 1.45), M["black"], bevel=0.008))
    for z in (1.15, 1.95):
        out.append(box("rear_hinge", (0.10, 0.06, 0.10), (0.68, -2.82, z), M["black"]))
    for sx in (-1, 1):
        out.append(box("tail_lamp", (0.16, 0.03, 0.20), (sx * 0.96, -2.81, 1.30), M["black"]))
        out.append(box("tail_red", (0.12, 0.01, 0.07), (sx * 0.96, -2.83, 1.35), M["red"]))
        out.append(box("tail_amber", (0.12, 0.01, 0.06), (sx * 0.96, -2.83, 1.25), M["amber"]))
        # Steps up the back.
        out.append(box("rear_step", (0.30, 0.18, 0.04), (sx * 0.35, -2.98, 0.62), M["black"]))
    return out


def roof():
    out = []
    # A lip round the roof's edge, and a few low boxes on it.
    for sx in (-1, 1):
        out.append(box("roof_rail", (0.04, 3.05, 0.05), (sx * (W(Z_ROOF) - 0.04), -0.95, Z_ROOF + 0.025), M["paint_dark"]))
        out.append(cyl("antenna_base", 0.035, 0.07, (sx * 0.80, -2.35, Z_ROOF + 0.035), M["black"], verts=12))
        out.append(cyl("antenna", 0.006, 1.6, (sx * 0.80, -2.35, Z_ROOF + 0.85), M["black"], verts=6))
    out.append(box("roof_box", (0.85, 0.50, 0.18), (0.0, -1.95, Z_ROOF + 0.09), M["paint"], bevel=0.015))
    out.append(box("roof_box_lid", (0.80, 0.44, 0.01), (0.0, -1.95, Z_ROOF + 0.185), M["paint_dark"]))
    # The turret's bearing ring, fixed to the roof (the turret turns on it).
    out.append(K.lathe("bearing", [(0.80, 0.0), (0.80, 0.04), (0.74, 0.05)], (TURRET_AT.x, TURRET_AT.y, Z_ROOF), M["black"], verts=48))
    # Air conditioning pod on the back of the roof, as fitted to these.
    return out


def underbody():
    out = []
    for y in (Y_FA, Y_RA):
        out.append(cyl("axle", 0.10, TRACK - 0.2, (0, y, R_TYRE), M["black"], verts=16, rot=(0, 90, 0)))
        out.append(box("diff", (0.38, 0.34, 0.30), (0, y, R_TYRE - 0.02), M["black"], bevel=0.03))
        for sx in (-1, 1):
            out.append(cyl("damper", 0.05, 0.55, (sx * 0.70, y + 0.18, 0.86), M["steel"], verts=12, rot=(0, 15 * sx, 0)))
            out.append(box("arm", (0.50, 0.10, 0.08), (sx * 0.70, y - 0.12, 0.50), M["black"]))
    out.append(box("skid", (1.0, 3.7, 0.06), (0, -0.03, Z_BELLY + 0.01), M["black"]))
    return out


def tyre(name):
    """A big run-flat, around the X axis at the origin: the tread through its texture, plain rubber walls."""
    k = R_TYRE / 0.403
    a = W_TYRE / 2 / 0.131
    prof = [(0.236, -0.108), (0.300, -0.126), (0.350, -0.131), (0.381, -0.126), (0.396, -0.112), (0.401, -0.094),
            (0.403, -0.05), (0.403, 0.0), (0.403, 0.05), (0.401, 0.094), (0.396, 0.112), (0.381, 0.126), (0.350, 0.131),
            (0.300, 0.126), (0.236, 0.108)]
    prof = [(0.33 + (r - 0.236) * (R_TYRE - 0.33) / (0.403 - 0.236), x * a) for r, x in prof]
    tread0, tread1 = 4, 10
    seg = 72
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    rings = [[bm.verts.new((x, r * math.cos(math.tau * i / seg), r * math.sin(math.tau * i / seg))) for i in range(seg)] for r, x in prof]
    arc = [0.0]
    for (r0, a0), (r1, a1) in zip(prof, prof[1:]):
        arc.append(arc[-1] + math.hypot(r1 - r0, a1 - a0))
    for j in range(len(prof) - 1):
        for i in range(seg):
            i2 = (i + 1) % seg
            f = bm.faces.new((rings[j][i], rings[j][i2], rings[j + 1][i2], rings[j + 1][i]))
            tread = tread0 <= j < tread1
            f.material_index = 0 if tread else 1
            for loop, (jj, ii) in zip(f.loops, ((j, i), (j, i + 1), (j + 1, i + 1), (j + 1, i))):
                if tread:
                    loop[uvl].uv = (5.0 * ii / seg, (arc[jj] - arc[tread0]) / (arc[tread1] - arc[tread0]))
                else:
                    loop[uvl].uv = (ii / seg, arc[jj] * 4)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(mat("tex_tyre_tread", None, 0.9, 0.0))
    o.data.materials.append(M["rubber"])
    o["uv"] = "keep"
    o["smooth"] = True
    o["smooth_angle"] = 50.0
    return o


def wheel(name):
    """Black steel wheel with a beadlock ring and a big hub; axle along X, outer face towards +X."""
    objs = [tyre(name + "_tyre")]
    objs.append(K.lathe(name + "_rim", [(0.31, -0.17), (0.33, -0.17), (0.33, 0.12), (0.34, 0.13), (0.34, 0.15), (0.30, 0.15),
                                       (0.29, 0.10), (0.16, 0.08), (0.16, 0.13), (0.10, 0.15), (0.0, 0.15)],
                        material=M["rim"], verts=48, rot=(0, 90, 0)))
    for i in range(12):
        b = math.tau * i / 12
        objs.append(cyl(name + "_bolt", 0.012, 0.02, (0.155, 0.32 * math.cos(b), 0.32 * math.sin(b)), M["steel"], verts=6, rot=(0, 90, 0)))
    for i in range(8):
        b = math.tau * i / 8
        objs.append(cyl(name + "_nut", 0.016, 0.03, (0.16, 0.12 * math.cos(b), 0.12 * math.sin(b)), M["steel"], verts=6, rot=(0, 90, 0)))
        objs.append(box(name + "_hole", (0.01, 0.07, 0.05), (0.095, 0.23 * math.cos(b + 0.39), 0.23 * math.sin(b + 0.39)), M["gap"], rot=(math.degrees(b + 0.39), 0, 0)))
    objs.append(cyl(name + "_cap", 0.07, 0.06, (0.18, 0, 0), M["black"], verts=20, rot=(0, 90, 0)))
    return objs


def wheels():
    out = []
    for y in (Y_FA, Y_RA):
        for sx in (-1, 1):
            w = wheel("wheel")
            bpy.context.view_layer.update()
            m = Matrix.Translation((sx * TRACK / 2, y, R_TYRE)) @ Matrix.Rotation(0.0 if sx > 0 else math.pi, 4, 'Z')
            for o in w:
                o.matrix_world = m @ o.matrix_world
            out += w
    return out


# ============================================================================================ turret
def turret():
    """In the turret's own space: origin the middle of the ring on the roof, front along +Y."""
    out = []
    p = M["paint"]
    T = 0.03                        # armour plate thickness
    z0, ztop, zside = 0.10, 0.64, 0.52
    # The ring the turret turns on, and the dark hole of the hatch inside it.
    out.append(K.lathe("ring", [(0.60, 0.0), (0.76, 0.0), (0.76, 0.10), (0.60, 0.10)], material=p, verts=48))
    out.append(cyl("hatch_hole", 0.60, 0.01, (0, 0, 0.012), M["hole"], verts=40))
    # A deck from the ring out to the front plate, which carries the shield.
    out.append(box("deck", (1.12, 0.62, 0.04), (0, 0.72, 0.08), p, bevel=0.008))
    # The gun's pintle: a post up from the deck to the cradle, and the cradle's yoke.
    cz = CRADLE_UP
    out.append(cyl("pintle", 0.045, cz - 0.10, (0, CRADLE_AHEAD, 0.10 + (cz - 0.10) / 2), M["black"], verts=16))
    out.append(cyl("pintle_collar", 0.065, 0.05, (0, CRADLE_AHEAD, 0.13), M["black"], verts=16))
    out.append(box("yoke", (0.12, 0.10, 0.03), (0, CRADLE_AHEAD, cz - 0.015), M["black"]))
    for sx in (-1, 1):
        out.append(box("yoke_cheek", (0.012, 0.08, 0.07), (sx * 0.055, CRADLE_AHEAD, cz + 0.01), M["black"]))
    # The front plate, round a window for the gun: below, above, and either side of it.
    yf = 1.02
    wx, wz0, wz1 = 0.13, 0.30, 0.54
    def plate(name, x0, x1, za, zb, y=yf):
        out.append(box(name, (x1 - x0, T, zb - za), ((x0 + x1) / 2, y, (za + zb) / 2), p, bevel=0.006))
    plate("front_low", -0.56, 0.56, z0, wz0)
    plate("front_high", -0.56, 0.56, wz1, ztop)
    plate("front_l", -0.56, -wx, wz0, wz1)
    plate("front_r", wx, 0.56, wz0, wz1)
    # Vision blocks in the front plate either side of the gun.
    for sx in (-1, 1):
        out.append(box("vision", (0.16, 0.012, 0.06), (sx * 0.33, yf + 0.016, 0.50), M["glass"]))
        out.append(box("vision_frame", (0.20, 0.010, 0.10), (sx * 0.33, yf + 0.011, 0.50), M["black"]))
    # The cheeks, raked back from the front plate's edges to the sides, and the sides themselves.
    for sx in (-1, 1):
        a = Vector((sx * 0.56, yf, 0))
        b = Vector((sx * 0.80, 0.62, 0))
        mid = (a + b) / 2
        ln = (b - a).length
        ang = math.degrees(math.atan2(b.y - a.y, b.x - a.x))
        out.append(box("cheek", (ln + 0.02, T, ztop - z0), (mid.x, mid.y, (z0 + ztop) / 2), p, rot=(0, 0, ang), bevel=0.006))
        out.append(box("side", (T, 0.80, zside - z0), (sx * 0.80, 0.22, (z0 + zside) / 2), p, bevel=0.006))
        # The deck under the cheek, and the top lid over the wing (the boxy shoulders either side of the gun).
        out.append(box("wing_deck", (0.30, 0.78, 0.03), (sx * 0.66, 0.42, 0.085), p))
        lid = [(sx * 0.32, yf, ztop), (sx * 0.56, yf, ztop), (sx * 0.80, 0.62, ztop), (sx * 0.80, 0.30, ztop), (sx * 0.32, 0.30, ztop)]
        bm = bmesh.new()
        top = [bm.verts.new(v) for v in lid]
        bot = [bm.verts.new((v[0], v[1], v[2] - T)) for v in lid]
        bm.faces.new(top)
        bm.faces.new(list(reversed(bot)))
        for i in range(len(lid)):
            j = (i + 1) % len(lid)
            bm.faces.new((top[i], bot[i], bot[j], top[j]))
        out.append(mesh_obj("lid", bm, p))
        # The lid's inner edge comes down to the deck: a web under it.
        out.append(box("web", (T, 0.70, ztop - z0 - T), (sx * 0.32, 0.66, (z0 + ztop - T) / 2), p))
        # A grab handle on the side, and bolts round the front plate.
        out.append(K.tube("side_grab", [(sx * 0.815, 0.0, 0.25), (sx * 0.86, 0.02, 0.25), (sx * 0.86, 0.02, 0.42), (sx * 0.815, 0.0, 0.42)], 0.012, M["black"], verts=8))
        for k in range(4):
            out.append(cyl("bolt", 0.012, 0.012, (sx * (0.18 + k * 0.12), yf + 0.02, 0.15), M["black"], verts=6, rot=(90, 0, 0)))
            out.append(cyl("bolt", 0.012, 0.012, (sx * (0.18 + k * 0.12), yf + 0.02, ztop - 0.05), M["black"], verts=6, rot=(90, 0, 0)))
    # A storage box for the belts on the deck to the left of the gun.
    out.append(box("ammo_can", (0.12, 0.28, 0.18), (-0.28, 0.80, 0.19), mat("flat_olive_can", (0.28, 0.30, 0.20), 0.6, 0.2), bevel=0.01))
    return out


# ============================================================================================ main
def preview_views(name, objs, angles):
    for i, ang in enumerate(angles):
        K.preview(f"{name}_{i}", objs, angle=ang)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    K.reset()
    materials()
    body = hull() + flares() + sides() + windscreen() + front() + rear() + roof() + underbody() + wheels()
    body = K.finish(body, join_name="armoured_car")
    top = turret()
    top = K.finish(top, join_name="armoured_car_turret")
    # The turret for the hull's previews sits on it, laid straight ahead.
    bpy.context.view_layer.update()
    if "preview" in argv:
        for o in top:
            o.matrix_world = Matrix.Translation(TURRET_AT) @ o.matrix_world
        bpy.context.view_layer.update()
        preview_views("armoured_car", body + top, [(-35, 18), (145, 20), (-90, 5), (20, 45)])
        return
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(K.PROJECT, "Assets", "armoured_car.blend"))
    K.export("armoured_car", body)
    K.export("armoured_car_turret", top)
    # And a preview of the two together.
    for o in top:
        o.matrix_world = Matrix.Translation(TURRET_AT) @ o.matrix_world
    bpy.context.view_layer.update()
    preview_views("armoured_car", body + top, [(-35, 18), (145, 20)])
    print("[armoured_car] done", flush=True)


main()
