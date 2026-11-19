"""
Builds the BRUTAL FPS props in Blender and exports them to Assets/Models/<name>.glb, with a preview render of each in
Tools/previews/models.

    blender -b --factory-startup -P Tools/blender/build_props.py -- [names...]

Every prop is modelled at its real size around the origin the game expects (see Game/Level.cs):
  crate       1 m cube, origin bottom centre (the game scales it)
  pallet      1.2 x 0.144 x 1.0 m EUR pallet, origin bottom centre
  drum        200 l drum, 0.585 m across, 0.88 m tall, origin bottom centre
  bin         1.6 x 1.0 x 1.3 m open skip bin, origin bottom centre
  lamp        industrial pendant: shade centred on the origin, cable running 1.95 m up
  container   40 ft ISO container 12.19 x 2.59 x 2.44 m along +X, doors at +X, origin bottom centre
  container_lod  the same container in about 400 triangles (the stacks on the ship)
  container_20   a 20 ft container, 6.05 x 2.59 x 2.44 m, otherwise the same (the east block of the yard)
  tank        corrugated rainwater tank on a timber stand, origin on the ground
  fuel_pump   old roadhouse bowser facing -X, origin on its plinth
  ac_unit     window air conditioner, grille facing +X, origin at its centre
  light_pole  6.5 m street light, arm reaching to -X, origin on the ground
  ute         old flat-deck ute facing game -Z (Blender +Y), origin on the ground under the middle
  spinifex    low spinifex hummock (about 1 m across, scaled by the game)
  cctv_dome   ceiling-mounted dome security camera's housing, origin where it meets the ceiling, hanging down; a
              conduit runs along the ceiling to +X (into the corner)
  cctv_eye    its black camera ball, lens looking at game -Z (Blender +Y), origin at the ball's centre, which is
              CCTV_PIVOT below the dome's origin (the game turns it to follow the player)
(saltbush_a/b and the gum trees: build_flora.py; the 4WD wagon: build_wagon.py.)
"""
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import modelkit as K  # noqa: E402
from modelkit import box, cyl, lathe, prism, tube, mat, add_bevel  # noqa: E402

R = random.Random(7)


def jitter(a):
    return R.uniform(-a, a)


# ============================================================================================ warehouse
def crate():
    objs = []
    s = 1.0
    t = 0.022          # board thickness
    batten = 0.075
    # Six panels of planks, slightly inset from the battens.
    wood = mat("wood_planks")
    objs.append(box("panels", (s - 0.03, s - 0.03, s - 0.03), (0, 0, s / 2), wood, bevel=0.004))
    # Battens along all 12 edges, standing proud of the panels.
    for x in (-1, 1):
        for y in (-1, 1):
            objs.append(box("post", (batten, batten, s), (x * (s / 2 - batten / 2), y * (s / 2 - batten / 2), s / 2), wood, bevel=0.006))
    for z in (batten / 2, s - batten / 2):
        for y in (-1, 1):
            objs.append(box("railx", (s - 2 * batten, batten, batten), (0, y * (s / 2 - batten / 2), z), wood, bevel=0.006))
        for x in (-1, 1):
            objs.append(box("raily", (batten, s - 2 * batten, batten), (x * (s / 2 - batten / 2), 0, z), wood, bevel=0.006))
    # Diagonal braces on the four sides.
    diag = math.hypot(s - 2 * batten, s - 2 * batten) - 0.06
    for side, (axis, sign) in enumerate((("x", 1), ("x", -1), ("y", 1), ("y", -1))):
        ang = 45 if side % 2 == 0 else -45
        if axis == "x":
            objs.append(box("brace", (t * 1.4, batten * 0.9, diag), (sign * (s / 2 - t * 0.2), 0, s / 2), wood, rot=(ang, 0, 0), bevel=0.004))
        else:
            objs.append(box("brace", (batten * 0.9, t * 1.4, diag), (0, sign * (s / 2 - t * 0.2), s / 2), wood, rot=(0, ang, 0), bevel=0.004))
    # Steel corner brackets.
    for x in (-1, 1):
        for y in (-1, 1):
            for z in (0.012, s - 0.012):
                objs.append(box("bracket", (0.09, 0.09, 0.024), (x * (s / 2 - 0.04), y * (s / 2 - 0.04), z), mat("rusty_metal"), bevel=0.003))
    return objs


def pallet():
    objs = []
    wood = mat("wood_planks")
    L, W = 1.2, 1.0
    # Bottom boards (3, along X), blocks (9), stringer boards (3, along X), top deck (5 boards along X).
    for y in (-W / 2 + 0.05, 0, W / 2 - 0.05):
        objs.append(box("bottom", (L, 0.1, 0.022), (0, y, 0.011), wood, bevel=0.003))
    for x in (-L / 2 + 0.0725, 0, L / 2 - 0.0725):
        for y in (-W / 2 + 0.05, 0, W / 2 - 0.05):
            objs.append(box("block", (0.145, 0.1 if y else 0.1, 0.078), (x, y, 0.022 + 0.039), wood, rot=(0, 0, jitter(1.5)), bevel=0.004))
    for x in (-L / 2 + 0.0725, 0, L / 2 - 0.0725):
        objs.append(box("stringer", (0.145, W, 0.022), (x, 0, 0.1 + 0.011), wood, bevel=0.003))
    for i, y in enumerate((-W / 2 + 0.0725, -0.215, 0, 0.215, W / 2 - 0.0725)):
        wide = 0.145 if i in (0, 4, 2) else 0.1
        objs.append(box("deck", (L + jitter(0.01), wide, 0.022), (jitter(0.006), y + jitter(0.006), 0.122 + 0.011), wood, rot=(0, 0, jitter(0.8)), bevel=0.003))
    return objs


def drum():
    paint = mat("painted_metal_tint", (0.5, 0.12, 0.08))
    r, h = 0.2925, 0.88
    hoop = 0.008
    # The shell with two rolling hoops pressed out of it, and rolled chimes top and bottom.
    prof = [(r - 0.012, 0.0), (r, 0.004), (r, 0.012), (r - 0.004, 0.02)]
    for zc in (h / 3, 2 * h / 3):
        prof += [(r - 0.004, zc - 0.03), (r + hoop * 0.6, zc - 0.012), (r + hoop, zc), (r + hoop * 0.6, zc + 0.012), (r - 0.004, zc + 0.03)]
    prof += [(r - 0.004, h - 0.02), (r, h - 0.012), (r, h - 0.004), (r - 0.012, h), (r - 0.016, h - 0.012), (r - 0.016, h - 0.016)]
    shell = lathe("shell", prof, material=paint, verts=48)
    lid = cyl("lid", r - 0.016, 0.004, (0, 0, h - 0.014), paint, verts=48)
    base = cyl("base", r - 0.012, 0.004, (0, 0, 0.012), paint, verts=48)
    objs = [shell, lid, base]
    # Two bungs in the lid.
    for (x, y, rr) in ((0.18, 0.0, 0.03), (-0.17, 0.05, 0.02)):
        objs.append(cyl("bung", rr, 0.012, (x, y, h - 0.008), mat("steel"), verts=16))
    return objs


def bin_():
    objs = []
    L, W, H = 1.6, 1.3, 1.0
    t = 0.03
    green = mat("painted_metal", (0.24, 0.31, 0.22))
    rust = mat("rusty_metal")
    # Walls (painted outside, rusty inside: two skins), floor and a rolled lip.
    for y in (-1, 1):
        objs.append(box("side_out", (L, t / 2, H - 0.12), (0, y * (W / 2 - t / 4), 0.12 + (H - 0.12) / 2), green, bevel=0.004))
        objs.append(box("side_in", (L - 2 * t, t / 2, H - 0.14), (0, y * (W / 2 - 3 * t / 4), 0.13 + (H - 0.14) / 2), rust))
    for x in (-1, 1):
        objs.append(box("end_out", (t / 2, W, H - 0.12), (x * (L / 2 - t / 4), 0, 0.12 + (H - 0.12) / 2), green, bevel=0.004))
        objs.append(box("end_in", (t / 2, W - 2 * t, H - 0.14), (x * (L / 2 - 3 * t / 4), 0, 0.13 + (H - 0.14) / 2), rust))
    objs.append(box("floor", (L - 2 * t, W - 2 * t, 0.02), (0, 0, 0.13), rust))
    for y in (-1, 1):
        objs.append(cyl("lip", 0.022, L + 0.02, (0, y * (W / 2), H), green, verts=12, rot=(0, 90, 0), uv="box"))
    for x in (-1, 1):
        objs.append(cyl("lip", 0.022, W + 0.02, (x * (L / 2), 0, H), green, verts=12, rot=(90, 0, 0), uv="box"))
    # Vertical stiffeners on the long sides, skids underneath, lifting lugs on the ends.
    for y in (-1, 1):
        for x in (-0.55, -0.18, 0.18, 0.55):
            objs.append(box("rib", (0.05, 0.03, H - 0.16), (x, y * (W / 2 + 0.012), 0.12 + (H - 0.16) / 2), green, bevel=0.006))
    for y in (-0.42, 0.42):
        objs.append(box("skid", (L + 0.05, 0.12, 0.12), (0, y, 0.06), rust, bevel=0.008))
    for x in (-1, 1):
        for y in (-0.4, 0.4):
            objs.append(box("lug", (0.03, 0.08, 0.14), (x * (L / 2 + 0.018), y, H - 0.25), green, bevel=0.005))
            objs.append(cyl("pin", 0.025, 0.1, (x * (L / 2 + 0.05), y, H - 0.26), mat("steel"), verts=12, rot=(90, 0, 0)))
    return objs


def lamp():
    shade_col = mat("painted_metal", (0.16, 0.24, 0.19))
    # Dome shade from the socket out to a rolled rim (outside), with a white enamel inside.
    prof = [(0.045, 0.11), (0.08, 0.1), (0.17, 0.07), (0.26, 0.02), (0.32, -0.04), (0.35, -0.1), (0.355, -0.11)]
    outer = lathe("shade", prof, material=shade_col, verts=40)
    inner_prof = [(r - 0.006, z - 0.004) for r, z in reversed(prof)]
    inner = lathe("enamel", inner_prof, material=mat("flat_enamel", (0.86, 0.84, 0.78), 0.35), verts=40, flip=True)
    rim = cyl("rim", 0.356, 0.012, (0, 0, -0.11), shade_col, verts=40, caps=False)
    socket = cyl("socket", 0.04, 0.09, (0, 0, 0.14), mat("steel"), verts=16)
    bulb = lathe("bulb", [(0.0, -0.11), (0.03, -0.1), (0.05, -0.07), (0.05, -0.03), (0.025, 0.0), (0.02, 0.03)], material=mat("emit_bulb", (1.0, 0.82, 0.55), emit=(1.0, 0.82, 0.55)), verts=16)
    cable = cyl("cable", 0.006, 1.8, (0, 0, 0.18 + 0.9), mat("flat_cable", (0.04, 0.04, 0.04), 0.5), verts=8)
    return [outer, inner, rim, socket, bulb, cable]


# ============================================================================================ yard
def container(L=12.19):
    W, H = 2.44, 2.59
    paint = mat("corrugated_tint", (0.55, 0.18, 0.12))
    frame = mat("painted_metal_tint", (0.55, 0.18, 0.12))
    rust = mat("rusty_metal")
    steel = mat("steel")
    objs = []
    post = 0.16
    rail_b, rail_t = 0.16, 0.1
    # Side walls (corrugation comes from the texture's normal map), roof and front end wall, slightly inset.
    inset = 0.025
    for y in (-1, 1):
        objs.append(box("side", (L - 2 * post, 0.02, H - rail_b - rail_t), (0, y * (W / 2 - inset), rail_b + (H - rail_b - rail_t) / 2), paint))
    objs.append(box("roof", (L - 2 * post, W - 2 * inset, 0.02), (0, 0, H - 0.04), paint))
    objs.append(box("front", (0.02, W - 2 * post, H - rail_b - rail_t), (-L / 2 + inset, 0, rail_b + (H - rail_b - rail_t) / 2), paint))
    # Frame: corner posts, bottom and top side rails, end rails, corner castings.
    for x in (-1, 1):
        for y in (-1, 1):
            objs.append(box("post", (post, post, H - 0.24), (x * (L / 2 - post / 2), y * (W / 2 - post / 2), H / 2), frame, bevel=0.01))
            for z in (0.06, H - 0.06):
                c = box("casting", (0.178, 0.162, 0.118), (x * (L / 2 - 0.089), y * (W / 2 - 0.081), z), rust, bevel=0.008)
                objs.append(c)
                # The oval holes are suggested by dark recesses.
                objs.append(box("hole", (0.002, 0.065, 0.04), (x * (L / 2 + 0.0005), y * (W / 2 - 0.081), z), mat("flat_hole", (0.03, 0.025, 0.02), 0.9)))
    for y in (-1, 1):
        objs.append(box("rail_bottom", (L - 0.36, 0.06, rail_b), (0, y * (W / 2 - 0.03), rail_b / 2), frame, bevel=0.006))
        objs.append(box("rail_top", (L - 0.36, 0.06, rail_t), (0, y * (W / 2 - 0.03), H - rail_t / 2), frame, bevel=0.006))
    for x in (-1, 1):
        objs.append(box("end_bottom", (0.08, W - 0.32, 0.2 if x > 0 else rail_b), (x * (L / 2 - 0.04), 0, (0.2 if x > 0 else rail_b) / 2), frame, bevel=0.006))
        objs.append(box("end_top", (0.08, W - 0.32, 0.22 if x > 0 else rail_t), (x * (L / 2 - 0.04), 0, H - (0.22 if x > 0 else rail_t) / 2), frame, bevel=0.006))
    # Fork-lift pockets along the bottom rails.
    for y in (-1, 1):
        for x in ((-1.8, 1.8) if L > 10 else (-1.0, 1.0)):
            objs.append(box("pocket", (0.36, 0.01, 0.1), (x, y * (W / 2 + 0.002), 0.07), mat("flat_hole", (0.03, 0.025, 0.02), 0.9)))
    # Doors at +X: two leaves in frames, four locking bars with cams and handles, hinges on the outer edges.
    door_w = (W - 2 * post) / 2
    door_h = H - 0.2 - 0.22
    zc = 0.2 + door_h / 2
    xf = L / 2 - 0.03
    for side in (-1, 1):
        yc = side * door_w / 2
        objs.append(box("door", (0.02, door_w - 0.04, door_h - 0.04), (xf - 0.01, yc, zc), paint))
        for dy in (-1, 1):
            objs.append(box("door_frame_v", (0.05, 0.06, door_h), (xf, yc + dy * (door_w / 2 - 0.03), zc), frame, bevel=0.006))
        for dz in (-1, 1):
            objs.append(box("door_frame_h", (0.05, door_w, 0.08), (xf, yc, zc + dz * (door_h / 2 - 0.04)), frame, bevel=0.006))
        for bar in (0.22, 0.72):
            yb = yc + side * (bar - 0.5) * door_w * -1
            objs.append(cyl("bar", 0.016, door_h + 0.16, (xf + 0.05, yb, zc), steel, verts=10))
            for dz in (-1, 1):
                objs.append(box("keeper", (0.07, 0.07, 0.06), (xf + 0.04, yb, zc + dz * (door_h / 2 + 0.05)), rust, bevel=0.006))
                objs.append(box("guide", (0.05, 0.05, 0.03), (xf + 0.035, yb, zc + dz * (door_h / 2 - 0.35)), steel, bevel=0.004))
            objs.append(box("handle", (0.04, 0.03, 0.42), (xf + 0.08, yb + side * 0.06, 1.15), steel, rot=(0, 0, 0), bevel=0.005))
            objs.append(box("handle_hub", (0.05, 0.08, 0.05), (xf + 0.06, yb + side * 0.03, 1.35), steel, bevel=0.005))
        for hz in (0.5, 1.05, 1.6, 2.1):
            objs.append(cyl("hinge", 0.022, 0.12, (xf + 0.02, side * (W / 2 - post - 0.005), hz), rust, verts=10))
        objs.append(box("gasket", (0.015, 0.015, door_h - 0.1), (xf + 0.02, 0.0, zc), mat("flat_rubber", (0.03, 0.03, 0.03), 0.85)))
    # Spot welds of the data plate on a door.
    objs.append(box("plate", (0.005, 0.3, 0.2), (xf + 0.006, door_w / 2 + 0.15, 1.8), mat("painted_metal", (0.8, 0.8, 0.76))))
    return objs


def container_lod():
    """A low-detail 40 ft container for the stacks on the ship: the same size, paint and frame colours as the
    full model, but its frame, castings and door gear are plain boxes (about 400 triangles instead of 6000)."""
    L, W, H = 12.19, 2.44, 2.59
    paint = mat("corrugated_tint", (0.55, 0.18, 0.12))
    frame = mat("painted_metal_tint", (0.55, 0.18, 0.12))
    rust = mat("rusty_metal")
    steel = mat("steel")
    hole = mat("flat_hole", (0.03, 0.025, 0.02), 0.9)
    post = 0.16
    objs = [box("shell", (L - 0.06, W - 0.05, H - 0.06), (0, 0, H / 2), paint)]
    for x in (-1, 1):
        for y in (-1, 1):
            objs.append(box("post", (post, post, H - 0.22), (x * (L / 2 - post / 2), y * (W / 2 - post / 2), H / 2), frame))
            for z in (0.06, H - 0.06):
                objs.append(box("casting", (0.178, 0.162, 0.118), (x * (L / 2 - 0.089), y * (W / 2 - 0.081), z), rust))
    for y in (-1, 1):
        objs.append(box("rail_bottom", (L - 0.36, 0.06, 0.16), (0, y * (W / 2 - 0.03), 0.08), frame))
        objs.append(box("rail_top", (L - 0.36, 0.06, 0.1), (0, y * (W / 2 - 0.03), H - 0.05), frame))
    for x in (-1, 1):
        objs.append(box("end_bottom", (0.08, W - 0.32, 0.2), (x * (L / 2 - 0.04), 0, 0.1), frame))
        objs.append(box("end_top", (0.08, W - 0.32, 0.22), (x * (L / 2 - 0.04), 0, H - 0.11), frame))
    # Doors at +X: the seam between the leaves, four locking bars, two hinge strips.
    xf = L / 2 - 0.02
    door_h = H - 0.42
    zc = 0.2 + door_h / 2
    objs.append(box("seam", (0.02, 0.025, door_h), (xf + 0.005, 0, zc), hole))
    door_w = (W - 2 * post) / 2
    for side in (-1, 1):
        for bar in (0.22, 0.72):
            yb = side * door_w / 2 + side * (bar - 0.5) * door_w * -1
            objs.append(box("bar", (0.035, 0.035, door_h + 0.12), (xf + 0.04, yb, zc), steel))
            objs.append(box("handle", (0.04, 0.03, 0.4), (xf + 0.07, yb + side * 0.06, 1.15), steel))
        objs.append(box("hinges", (0.03, 0.04, door_h - 0.2), (xf + 0.015, side * (W / 2 - post - 0.01), zc), rust))
    for y in (-1, 1):
        for x in ((-1.8, 1.8) if L > 10 else (-1.0, 1.0)):
            objs.append(box("pocket", (0.36, 0.01, 0.1), (x, y * (W / 2 + 0.002), 0.07), hole))
    return objs


# ============================================================================================ outback
def tank():
    objs = []
    wood = mat("wood_planks")
    galv = mat("galvanized")
    # Timber stand: 4 posts, bearers, a deck of boards, cross bracing.
    for x in (-0.8, 0.8):
        for y in (-0.8, 0.8):
            objs.append(box("post", (0.1, 0.1, 0.56), (x, y, 0.28), wood, bevel=0.005))
    for x in (-0.8, 0.8):
        objs.append(box("bearer", (0.1, 1.8, 0.1), (x, 0, 0.5), wood, bevel=0.005))
    for i in range(9):
        objs.append(box("deck", (1.8, 0.18, 0.04), (0, -0.8 + i * 0.2, 0.58), wood, rot=(0, 0, jitter(0.6)), bevel=0.004))
    for y in (-0.8, 0.8):
        objs.append(box("brace", (2.0, 0.04, 0.08), (0, y + 0.06 * (1 if y > 0 else -1), 0.28), wood, rot=(0, 15, 0), bevel=0.004))
    # The tank: corrugated walls (corrugation runs round it, from the texture), a rolled rim and a domed lid.
    r, z0, h = 0.85, 0.6, 1.8
    shell = cyl("shell", r, h, (0, 0, z0 + h / 2), galv, verts=64, caps=False, uv="ring")
    objs.append(shell)
    objs.append(cyl("base", r, 0.02, (0, 0, z0 + 0.01), galv, verts=64))
    objs.append(lathe("rim", [(r - 0.01, z0 + h - 0.01), (r + 0.025, z0 + h), (r + 0.025, z0 + h + 0.03), (r - 0.01, z0 + h + 0.035)], material=galv, verts=64))
    objs.append(lathe("lid", [(r, z0 + h + 0.03), (r * 0.6, z0 + h + 0.12), (0.2, z0 + h + 0.2), (0.0, z0 + h + 0.21)], material=galv, verts=64))
    objs.append(cyl("hatch", 0.2, 0.05, (0, 0, z0 + h + 0.22), galv, verts=24))
    objs.append(cyl("strainer", 0.12, 0.06, (0.35, 0.2, z0 + h + 0.16), mat("steel"), verts=16))
    # Outlet tap and overflow pipe.
    objs.append(cyl("outlet", 0.025, 0.2, (r + 0.08, 0, z0 + 0.12), mat("steel"), verts=12, rot=(0, 90, 0)))
    objs.append(box("tap", (0.06, 0.04, 0.08), (r + 0.18, 0, z0 + 0.1), mat("flat_brass", (0.75, 0.56, 0.28), 0.35, 1.0), bevel=0.006))
    objs.append(tube("overflow", [(r - 0.05, -0.3, z0 + h - 0.12), (r + 0.1, -0.3, z0 + h - 0.12), (r + 0.12, -0.3, z0 + h - 0.3), (r + 0.12, -0.3, 0.1)], 0.03, mat("flat_pvc", (0.42, 0.44, 0.42), 0.5)))
    return objs


def fuel_pump():
    cream = mat("painted_metal_tint", (0.86, 0.84, 0.76))
    red = mat("painted_metal", (0.72, 0.14, 0.10))
    chrome = mat("flat_chrome", (0.8, 0.8, 0.8), 0.15, 1.0)
    glass = mat("flat_glass", (0.06, 0.07, 0.08), 0.04)
    objs = []
    # Plinth, cabinet (with a slight taper), the head with dial windows both sides, and a red cap.
    objs.append(box("plinth", (0.5, 0.56, 0.08), (0, 0, 0.04), mat("steel"), bevel=0.01))
    objs.append(box("cabinet", (0.42, 0.5, 1.4), (0, 0, 0.08 + 0.7), cream, bevel=0.03))
    objs.append(box("panel", (0.005, 0.38, 0.9), (-0.212, 0, 0.62), cream, bevel=0.004))
    objs.append(box("head", (0.48, 0.56, 0.36), (0, 0, 1.48 + 0.18), cream, bevel=0.06))
    for x in (-1, 1):
        objs.append(box("dial", (0.01, 0.42, 0.24), (x * 0.24, 0, 1.66), mat("flat_dial", (0.88, 0.86, 0.8), 0.6), bevel=0.01))
        objs.append(box("glass", (0.008, 0.44, 0.26), (x * 0.245, 0, 1.66), glass, bevel=0.01))
        objs.append(box("bezel", (0.02, 0.46, 0.28), (x * 0.235, 0, 1.66), chrome, bevel=0.012))
        # Digit wheels behind the glass.
        for i in range(4):
            objs.append(box("digit", (0.012, 0.035, 0.05), (x * 0.241, -0.08 + i * 0.05, 1.7), mat("flat_digits", (0.08, 0.08, 0.08), 0.5)))
    cap = lathe("cap", [(0.0, 1.84), (0.26, 1.84), (0.27, 1.87), (0.22, 1.92), (0.0, 1.93)], material=red, verts=32)
    objs.append(cap)
    objs.append(box("logo", (0.008, 0.2, 0.2), (-0.215, 0, 1.1), red, bevel=0.03))
    # Holster with the nozzle on the side, and the hose looping down from the cabinet.
    objs.append(box("holster", (0.12, 0.06, 0.14), (-0.1, 0.27, 1.1), mat("steel"), bevel=0.01))
    objs.append(box("nozzle", (0.05, 0.05, 0.2), (-0.1, 0.3, 1.02), mat("steel"), rot=(0, 20, 0), bevel=0.012))
    objs.append(cyl("spout", 0.012, 0.18, (-0.2, 0.3, 0.92), mat("steel"), verts=10, rot=(0, -60, 0)))
    objs.append(tube("hose", [(0.05, 0.26, 1.3), (0.06, 0.4, 0.95), (0.0, 0.42, 0.35), (-0.12, 0.38, 0.4), (-0.1, 0.31, 0.95)], 0.017, mat("flat_rubber", (0.04, 0.04, 0.04), 0.7)))
    return objs


def ac_unit():
    beige = mat("painted_metal_tint", (0.7, 0.7, 0.68))
    steel = mat("steel")
    objs = []
    W, H, D = 0.7, 0.45, 0.6     # width (Y), height (Z), depth (X)
    objs.append(box("case", (D, W, H), (0, 0, 0), beige, bevel=0.012))
    # Front grille (+X): horizontal louvres in a frame.
    for i in range(9):
        objs.append(box("louvre", (0.03, W - 0.08, 0.012), (D / 2 + 0.01, 0, -H / 2 + 0.05 + i * 0.044), steel, rot=(0, -25, 0)))
    objs.append(box("grille_frame", (0.02, W - 0.04, H - 0.04), (D / 2 + 0.004, 0, 0), mat("flat_dark", (0.06, 0.06, 0.06), 0.8)))
    # Side vents, a control panel and wall brackets.
    for y in (-1, 1):
        for i in range(6):
            objs.append(box("vent", (0.25, 0.006, 0.012), (-0.05, y * (W / 2 + 0.002), -0.1 + i * 0.04), mat("flat_dark", (0.06, 0.06, 0.06), 0.8)))
    objs.append(box("controls", (0.01, 0.12, 0.08), (D / 2 + 0.006, W / 2 - 0.1, H / 2 - 0.06), mat("flat_plastic", (0.75, 0.74, 0.7), 0.5), bevel=0.004))
    for y in (-0.25, 0.25):
        objs.append(box("bracket", (0.4, 0.04, 0.04), (-0.12, y, -H / 2 - 0.02), mat("rusty_metal"), bevel=0.004))
    return objs


def light_pole():
    steel = mat("painted_metal", (0.62, 0.62, 0.6))
    objs = []
    objs.append(box("base_plate", (0.35, 0.35, 0.02), (0, 0, 0.01), mat("rusty_metal"), bevel=0.005))
    for x in (-0.13, 0.13):
        for y in (-0.13, 0.13):
            objs.append(cyl("bolt", 0.012, 0.05, (x, y, 0.035), mat("rusty_metal"), verts=8))
    objs.append(cyl("pole", 0.075, 6.45, (0, 0, 0.02 + 3.225), steel, verts=20, radius2=0.05))
    objs.append(tube("arm", [(0, 0, 6.2), (0, 0, 6.42), (-0.25, 0, 6.55), (-0.85, 0, 6.5)], 0.03, steel))
    head = lathe("head", [(0.0, -0.02), (0.14, -0.02), (0.16, 0.04), (0.1, 0.1), (0.0, 0.11)], material=mat("painted_metal", (0.62, 0.62, 0.6)), verts=24)
    head.scale = (1.7, 1.0, 1.0)
    head.location = (-1.05, 0, 6.42)
    objs.append(head)
    objs.append(box("lens", (0.4, 0.22, 0.01), (-1.05, 0, 6.395), mat("flat_lens", (0.75, 0.75, 0.7), 0.1), bevel=0.01))
    return objs


# --------------------------------------------------------------------------------------------- cars
def wheel(name, x, y, r=0.34, width=0.2, rim_col=None):
    """A wheel at (x, y), axle along X: a tyre with a rounded profile, a steel rim and hub cap."""
    tyre = lathe(name + "_tyre", [(r * 0.62, -width / 2), (r * 0.93, -width / 2), (r, -width / 2 + 0.03), (r, width / 2 - 0.03), (r * 0.93, width / 2), (r * 0.62, width / 2)],
                 material=mat("flat_rubber", (0.035, 0.035, 0.035), 0.85), verts=32, rot=(0, 90, 0))
    tyre.location = (x, y, r)
    sign = 1 if x > 0 else -1
    rim = cyl(name + "_rim", r * 0.62, width * 0.8, (x, y, r), mat("steel"), verts=24, rot=(0, 90, 0))
    cap = cyl(name + "_cap", r * 0.3, 0.03, (x + sign * width * 0.42, y, r), mat("flat_chrome", (0.8, 0.8, 0.8), 0.2, 1.0), verts=20, rot=(0, 90, 0))
    return [tyre, rim, cap]


def car(ute):
    """An old Australian sedan or a flat-deck ute. Length along Blender Y (front at +Y), width along X."""
    paint = mat("painted_metal_tint", (0.8, 0.78, 0.72))
    glass = mat("flat_glass", (0.05, 0.06, 0.07), 0.04)
    chrome = mat("flat_chrome", (0.8, 0.8, 0.8), 0.15, 1.0)
    black = mat("flat_trim", (0.05, 0.05, 0.05), 0.6)
    L, W = 4.6, 1.8
    objs = []
    # Lower body: side profile (Y, Z) extruded across the width, bevelled for rounded edges.
    body_profile = [(-2.3, 0.32), (2.3, 0.32), (2.32, 0.55), (2.25, 0.82), (1.0, 0.92), (-2.2, 0.9), (-2.32, 0.7)] if not ute else \
                   [(-2.3, 0.34), (2.3, 0.34), (2.32, 0.55), (2.25, 0.84), (0.95, 0.94), (0.2, 0.94), (0.2, 0.9), (-2.3, 0.9)]
    body = prism("body", body_profile, W, material=paint, axis='X')
    add_bevel(body, 0.05, 3)
    objs.append(body)
    # Wheel arches (dark recesses) and wheels.
    for y in (-1.4, 1.4):
        for x in (-1, 1):
            objs.append(cyl("arch", 0.4, 0.04, (x * (W / 2 - 0.005), y, 0.36), black, verts=24, rot=(0, 90, 0)))
            objs += wheel(f"wheel{x}{y}", x * (W / 2 - 0.12), y)
    # Cabin: glass all round, with a painted roof and pillars.
    if ute:
        cab = [(0.95, 0.92), (0.55, 1.43), (-0.55, 1.43), (-0.65, 0.92)]
        cab_y = 0.0
    else:
        cab = [(0.95, 0.9), (0.45, 1.4), (-0.95, 1.4), (-1.55, 0.9)]
        cab_y = 0.0
    greenhouse = prism("glasshouse", cab, W * 0.86, material=glass, axis='X')
    objs.append(greenhouse)
    top = max(z for _, z in cab)
    ys = [y for y, z in cab if z == top]
    objs.append(box("roof", (W * 0.88, abs(ys[0] - ys[1]) + 0.08, 0.05), (0, (ys[0] + ys[1]) / 2, top + 0.02), paint, bevel=0.02))
    # Pillars (A and C/B), as thin painted strips on the glass corners.
    for (y0, z0), (y1, z1) in ((cab[0], cab[1]), (cab[2], cab[3])):
        length = math.hypot(y1 - y0, z1 - z0)
        ang = math.degrees(math.atan2(z1 - z0, y1 - y0))
        for x in (-1, 1):
            objs.append(box("pillar", (0.05, length, 0.07), (x * W * 0.43, (y0 + y1) / 2, (z0 + z1) / 2), paint, rot=(ang, 0, 0), bevel=0.01))
    if not ute:
        for x in (-1, 1):
            objs.append(box("b_pillar", (0.05, 0.08, 0.5), (x * W * 0.432, -0.25, 1.15), paint))
    # Bumpers, grille, lights, mirrors, number plates.
    for y, d in ((L / 2 + 0.03, 1), (-L / 2 - 0.03, -1)):
        objs.append(box("bumper", (W + 0.04, 0.1, 0.12), (0, y, 0.42), chrome, bevel=0.03))
        objs.append(box("plate", (0.37, 0.01, 0.13), (0, y + d * 0.055, 0.56 if d < 0 else 0.42), mat("flat_plate", (0.85, 0.85, 0.82), 0.4)))
    objs.append(box("grille", (W * 0.55, 0.02, 0.2), (0, L / 2 + 0.02, 0.66), black, bevel=0.01))
    for x in (-1, 1):
        objs.append(cyl("headlight", 0.085, 0.04, (x * 0.68, L / 2 + 0.02, 0.68), mat("flat_lens", (0.85, 0.85, 0.8), 0.1), verts=20, rot=(90, 0, 0)))
        objs.append(box("taillight", (0.25, 0.03, 0.12), (x * 0.7, -L / 2 - 0.01, 0.72), mat("flat_red_lens", (0.6, 0.06, 0.04), 0.2), bevel=0.01))
        objs.append(box("mirror", (0.12, 0.06, 0.09), (x * (W / 2 + 0.06), 0.8, 1.05), black, bevel=0.015))
        objs.append(box("door_line", (0.004, 0.006, 0.55), (x * (W / 2 + 0.001), -0.6 if not ute else -0.65, 0.65), black))
        objs.append(box("handle", (0.01, 0.12, 0.025), (x * (W / 2 + 0.006), -0.45 if not ute else -0.5, 0.85), chrome, bevel=0.005))
    if ute:
        # Flat-deck tray with drop sides, a headboard with a rack, and the tray's chassis rails.
        tray_y = -1.25
        objs.append(box("tray", (W + 0.04, 2.05, 0.06), (0, tray_y, 1.0), mat("wood_planks"), bevel=0.01))
        for x in (-1, 1):
            objs.append(box("drop_side", (0.03, 2.05, 0.3), (x * (W / 2 + 0.02), tray_y, 1.18), mat("painted_metal", (0.62, 0.62, 0.6)), bevel=0.008))
        objs.append(box("tail_gate", (W + 0.04, 0.03, 0.3), (0, tray_y - 1.025, 1.18), mat("painted_metal", (0.62, 0.62, 0.6)), bevel=0.008))
        objs.append(box("headboard", (W + 0.04, 0.04, 0.55), (0, -0.25, 1.3), mat("painted_metal", (0.62, 0.62, 0.6)), bevel=0.008))
        for x in (-0.6, -0.2, 0.2, 0.6):
            objs.append(cyl("rack", 0.015, 0.5, (x, -0.24, 1.55), mat("steel"), verts=8))
        objs.append(cyl("rack_top", 0.02, W, (0, -0.24, 1.8), mat("steel"), verts=8, rot=(0, 90, 0)))
        for x in (-0.45, 0.45):
            objs.append(box("chassis", (0.1, 2.0, 0.12), (x, tray_y, 0.9), mat("rusty_metal")))
        # Bull bar on the front.
        objs.append(tube("bullbar", [(-0.8, 2.38, 0.35), (-0.8, 2.42, 0.9), (0.8, 2.42, 0.9), (0.8, 2.38, 0.35)], 0.03, mat("steel")))
    return objs


# --------------------------------------------------------------------------------------------- scrub
def clump(name, center, radius, material, seed, subdiv=1):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdiv, radius=radius, location=center)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(material)
    rng = random.Random(seed)
    for v in o.data.vertices:
        v.co *= 0.75 + 0.5 * rng.random()
    o.scale = (1.0, 1.0, 0.8 + 0.3 * rng.random())
    o["uv"] = "box"
    o["smooth"] = True
    o["smooth_angle"] = 180.0   # soft, rounded foliage: no hard edges
    return o


def spinifex(seed):
    """A low hummock of spiky spinifex grass: a mound with hundreds of needle-like blades."""
    rng = random.Random(seed)
    straw = mat("flat_spinifex", (0.62, 0.55, 0.34), 0.8)
    dry = mat("flat_spinifex_dry", (0.48, 0.42, 0.28), 0.85)
    objs = [clump("mound", (0, 0, 0.05), 0.42, mat("foliage"), seed)]
    objs[0].scale = (1.0, 1.0, 0.45)
    blades = []
    for i in range(55):
        ang = rng.random() * math.tau
        rr = math.sqrt(rng.random()) * 0.42
        base = Vector((math.cos(ang) * rr, math.sin(ang) * rr, 0.08 + 0.1 * (1 - rr / 0.45)))
        out = Vector((math.cos(ang), math.sin(ang), 0)) * (0.35 + 0.4 * rr / 0.45)
        tip = base + out * 0.35 + Vector((jitter(0.08), jitter(0.08), 0.25 + 0.2 * rng.random()))
        d = tip - base
        o = cyl("blade", 0.009, d.length, (base + tip) / 2, straw if i % 3 else dry, verts=3, radius2=0.0005, uv="box")
        o.rotation_euler = d.to_track_quat('Z', 'Y').to_euler()
        blades.append(o)
    return objs + blades


# ============================================================================================ bunker
CCTV_PIVOT = 0.062     # the ball's centre below the ceiling (Game/SecurityCamera.cs: PivotDrop)
CCTV_BALL = 0.05


def cctv_dome():
    white = mat("flat_cctv_white", (0.88, 0.88, 0.86), 0.35)
    dark = mat("flat_dark", (0.06, 0.06, 0.06), 0.8)
    clip = mat("flat_cctv_clip", (0.55, 0.55, 0.55), 0.4, 0.8)
    objs = []
    # One closed solid of revolution: the flat mounting face on the ceiling, the rounded housing bulging out a little,
    # in round the rim and up inside, leaving a cavity the ball sits in.
    prof = [(0.0, 0.0), (0.074, 0.0), (0.075, -0.006), (0.079, -0.016), (0.081, -0.032), (0.079, -0.048),
            (0.073, -0.06), (0.064, -0.066), (0.059, -0.062), (0.057, -0.045), (0.056, -0.02), (0.03, -0.008), (0.0, -0.006)]
    objs.append(lathe("housing", prof, material=white, verts=48))
    # A smoky black liner just inside the cavity (the real thing is dark in there, behind its bubble).
    liner = [(0.0, -0.0068), (0.03, -0.0088), (0.0555, -0.0205), (0.0565, -0.045), (0.0585, -0.0615)]
    for flip in (False, True):   # (an open sheet: both faces, so it shows whichever way the normals came out)
        objs.append(lathe("liner", liner, material=mat("flat_cctv_liner", (0.03, 0.03, 0.033), 0.25), verts=48, flip=flip))
    # The parting line between the base and the housing, and the two screw-access holes in its sides.
    objs.append(cyl("seam", 0.0755, 0.0012, (0, 0, -0.0085), dark, verts=48, caps=False))
    for a in (60, 240):
        r = math.radians(a)
        objs.append(cyl("screw_hole", 0.0055, 0.006, (math.cos(r) * 0.0785, math.sin(r) * 0.0785, -0.036), dark, verts=12,
                        rot=(0, 90, a)))
    # Surface conduit from a gland in the base's side, along the ceiling into the corner, with two saddle clips.
    objs.append(cyl("gland", 0.012, 0.022, (0.082, 0, -0.012), dark, verts=16, rot=(0, 90, 0)))
    objs.append(cyl("conduit", 0.0095, 0.27, (0.093 + 0.135, 0, -0.012), mat("flat_cctv_conduit", (0.78, 0.78, 0.76), 0.55),
                    verts=16, rot=(0, 90, 0)))
    for x in (0.18, 0.32):
        objs.append(box("clip", (0.012, 0.026, 0.004), (x, 0, -0.002), clip))
        for y in (0.0115, -0.0115):
            objs.append(box("clip_strap", (0.012, 0.004, 0.02), (x, y, -0.012), clip))
    return objs


def cctv_eye():
    R = CCTV_BALL
    matte = mat("flat_cctv_bezel", (0.015, 0.015, 0.017), 0.7)
    objs = []
    steps = 24
    prof = [(R * math.sin(math.pi * i / steps), -R * math.cos(math.pi * i / steps)) for i in range(steps + 1)]
    objs.append(lathe("ball", prof, material=mat("flat_cctv_ball", (0.02, 0.02, 0.024), 0.12), verts=40))
    # Lens (Blender +Y = the game's forward): a matte bezel, the barrel standing proud of the ball, its glass.
    objs.append(cyl("bezel", 0.026, 0.004, (0, 0.0445, 0), matte, verts=32, rot=(-90, 0, 0)))
    objs.append(cyl("barrel", 0.019, 0.012, (0, 0.048, 0), matte, verts=32, rot=(-90, 0, 0)))
    objs.append(cyl("barrel_ring", 0.0155, 0.0015, (0, 0.0545, 0), mat("flat_cctv_ring", (0.35, 0.35, 0.36), 0.3, 1.0),
                    verts=32, rot=(-90, 0, 0), caps=False))
    objs.append(cyl("glass", 0.0135, 0.002, (0, 0.0542, 0), mat("flat_cctv_glass", (0.03, 0.045, 0.08), 0.03, 0.3), verts=32, rot=(-90, 0, 0)))
    objs.append(cyl("iris", 0.006, 0.001, (0, 0.0554, 0), mat("flat_cctv_iris", (0.08, 0.02, 0.10), 0.05, 0.6), verts=24, rot=(-90, 0, 0)))
    # Infrared LEDs round the lens (dull red-black), and one that glows: the recording light.
    for i, a in enumerate((45, 135, 225, 315)):
        r = math.radians(a)
        x, z = math.cos(r) * 0.033, math.sin(r) * 0.033
        y = math.sqrt(R * R - 0.033 ** 2) + 0.0005
        led = mat("emit_cctv_rec", (1.0, 0.08, 0.05), emit=(1.0, 0.08, 0.05)) if i == 0 else mat("flat_cctv_ir", (0.18, 0.03, 0.03), 0.15)
        objs.append(cyl("led", 0.0042, 0.003, (x, y, z), led, verts=12, rot=(-90, 0, 0)))
    return objs


def cctv_preview():
    """Both halves together, the ball turned as the game turns it, stood on its ceiling face so the preview sees it."""
    K.reset()
    dome = K.finish(cctv_dome(), join_name="cctv_dome")
    eye = K.finish(cctv_eye(), join_name="cctv_eye")
    eye[0].location = (0, 0, -CCTV_PIVOT)
    eye[0].rotation_euler = (math.radians(-35), 0, math.radians(30))
    bpy.context.view_layer.update()
    for o in dome + eye:
        o.matrix_world = Matrix.Rotation(math.pi, 4, 'X') @ o.matrix_world
    K.preview("cctv_assembled", dome + eye, angle=(-150, 18))


# ============================================================================================ main
BUILDERS = {
    "crate": crate, "pallet": pallet, "drum": drum, "bin": bin_, "lamp": lamp,
    "container": container, "container_20": lambda: container(6.05), "container_lod": container_lod, "tank": tank, "fuel_pump": fuel_pump, "ac_unit": ac_unit, "light_pole": light_pole,
    "ute": lambda: car(True),
    # (The saltbushes and gum trees are leaf-card models: Tools/blender/build_flora.py. The modern 4WD that replaced
    # the sedan is Tools/blender/build_wagon.py.)
    "spinifex": lambda: spinifex(5),
    "cctv_dome": cctv_dome, "cctv_eye": cctv_eye,
}


def build(name):
    K.reset()
    objs = BUILDERS[name]()
    objs = K.finish(objs, join_name=name)
    K.export(name, objs)
    try:
        K.preview(name, objs)
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
    if any(n.startswith("cctv") for n in argv or list(BUILDERS)):
        try:
            cctv_preview()
        except Exception as e:
            print(f"[model] preview of cctv_assembled failed: {e}", flush=True)
    print("[model] done", flush=True)


main()
