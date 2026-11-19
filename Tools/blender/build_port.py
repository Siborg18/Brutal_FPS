"""
Builds the night port structures around the container yard and exports Assets/Models/<name>.glb, with previews in
Tools/previews/models:

  flood_mast     a 24 m galvanised high mast with a caged ladder, a service platform and a cross-arm of ten sodium
                 floodlights aimed towards +X (the game turns it to face the yard); origin at its foot. The lenses are
                 emissive.
  gantry_crane   a rubber-tyred gantry crane (container stacker): 23.5 m span along X, 7 m wheelbase along Y, about
                 25 m high, with a trolley and machinery house, the operator's cab (lit), the spreader hanging on its
                 ropes, a zig-zag stair up one leg, walkways and railings, and lamps under the girders. The paint is
                 "flat_crane_tint", so the game chooses the colour (yellow). Origin on the ground at its centre.

    blender -b --factory-startup -P Tools/blender/build_port.py -- [names...]

Lamp positions the game needs (for their glow and light) are listed in LAMPS below and in the game's Level.cs.
"""
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import modelkit as K  # noqa: E402
from modelkit import box, cyl, mat  # noqa: E402

SODIUM = (1.0, 0.80, 0.50)        # lens colour (display space; the game draws emissive colours as they are)


def strut(name, a, b, thickness, material, width=None):
    """A square beam from a to b."""
    a, b = Vector(a), Vector(b)
    d = b - a
    o = box(name, (width or thickness, d.length, thickness), tuple((a + b) / 2), material)
    o.rotation_mode = 'QUATERNION'
    o.rotation_quaternion = Vector((0, 1, 0)).rotation_difference(d.normalized())
    return o


# ============================================================================================ flood mast
MAST_HEIGHT = 24.0
LADDER_FOOT = 3.2                 # the mast's ladder starts this high (out of reach from the ground)


def flood_mast():
    """A tapered galvanised mast with a caged ladder; at the top a service platform with railings, and a cross-arm
    in front carrying two rows of five sodium floodlights tilted down 35 degrees towards +X."""
    galv = mat("galvanized")
    steel = mat("steel")
    housing = mat("flat_lamp_housing", (0.30, 0.31, 0.32), 0.45, 0.6)
    lens = mat("emit_sodium", SODIUM, emit=SODIUM)
    zt = MAST_HEIGHT - 0.55                       # the floodlights' centre (the game's light position)
    zp = zt - 1.05                                # service platform
    objs = [
        cyl("pole", 0.31, zp, (0, 0, zp / 2), galv, verts=16, radius2=0.16),
        cyl("flange", 0.62, 0.16, (0, 0, 0.08), steel, verts=16),
        cyl("pole_top", 0.16, MAST_HEIGHT - zp, (0, 0, zp + (MAST_HEIGHT - zp) / 2), galv, verts=12),
        cyl("cap", 0.2, 0.12, (0, 0, MAST_HEIGHT + 0.06), steel, verts=12),
        cyl("spike", 0.02, 1.2, (0, 0, MAST_HEIGHT + 0.6), steel, verts=6),
    ]
    # Ladder up the back of the mast, starting out of reach (LADDER_FOOT up: in the game you can't climb it, so it
    # shouldn't look as if you could), with a safety cage of whole hoops round the ladder and the climber behind it.
    for sy in (-0.2, 0.2):
        objs.append(box("ladder_rail", (0.04, 0.04, zp - LADDER_FOOT), (-0.42, sy, (zp - LADDER_FOOT) / 2 + LADDER_FOOT), steel))
    for z in [LADDER_FOOT + 0.3 + 0.3 * k for k in range(int((zp - LADDER_FOOT - 0.6) / 0.3))]:
        objs.append(box("rung", (0.025, 0.4, 0.025), (-0.42, 0, z), steel))
    for z in [LADDER_FOOT + 0.6 + 1.2 * k for k in range(int((zp - LADDER_FOOT - 0.6) / 1.2))]:
        pts = [(-0.72 + 0.38 * math.cos(t), 0.38 * math.sin(t), z) for t in np.linspace(0, 2 * math.pi, 17)]
        objs.append(K.tube("cage_hoop", pts, 0.015, steel, verts=4))
    # Service platform with railings.
    objs.append(box("platform", (2.4, 2.4, 0.06), (0, 0, zp), steel))
    for sx in (-1, 1):
        objs.append(box("rail_x", (0.04, 2.4, 0.04), (sx * 1.18, 0, zp + 1.0), steel))
        objs.append(box("rail_y", (2.4, 0.04, 0.04), (0, sx * 1.18, zp + 1.0), steel))
        for k in (-1, 0, 1):
            objs.append(box("rail_post", (0.04, 0.04, 1.0), (sx * 1.18, k * 1.18, zp + 0.5), steel))
            objs.append(box("rail_post", (0.04, 0.04, 1.0), (k * 1.18, sx * 1.18, zp + 0.5), steel))
    # Cross-arm frame in front of the mast.
    fx = 0.55
    for dz in (-0.36, 0.36):
        objs.append(box("arm_bar", (0.08, 3.8, 0.08), (fx, 0, zt + dz), steel))
    for sy in (-1, 1):
        objs.append(box("arm_end", (0.08, 0.08, 0.9), (fx, sy * 1.9, zt), steel))
    objs.append(box("arm_mount", (fx + 0.1, 0.18, 0.9), (fx / 2, 0, zt), steel))
    # Two rows of five floodlights, tilted down 35 degrees, lenses facing +X.
    tilt = math.radians(35)
    front = Vector((math.cos(tilt), 0, -math.sin(tilt)))
    for dz in (-0.36, 0.36):
        for k in range(5):
            c = Vector((fx + 0.16, -1.48 + 0.74 * k, zt + dz))
            objs.append(box("housing", (0.28, 0.58, 0.46), tuple(c), housing, rot=(0, 35, 0), bevel=0.03))
            objs.append(box("lens", (0.02, 0.50, 0.38), tuple(c + front * 0.145), lens, rot=(0, 35, 0)))
    return objs


# ============================================================================================ gantry crane
W2, Y2 = 11.75, 3.5           # half span (leg centres), half wheelbase
HG, GH = 19.6, 1.6            # girder bottom height, girder depth

# Lamps (Blender coordinates): (position, kind) - "flood" under the girders, "cab" the lit cab, "beacon" on top.
LAMPS = [((x, sy * 2.5, HG - 0.35), "flood") for x in (-8.5, 0.0, 8.5) for sy in (-1, 1)] + \
        [((5.5, -3.3, HG - 1.4), "cab"), ((3.0, 0.0, HG + GH + 4.15), "beacon")]


def gantry_crane():
    # Clean, glossy-ish yellow paint (the game picks the colour): a chipped-paint texture at this size read as wood.
    paint = mat("flat_crane_tint", (0.86, 0.64, 0.06), 0.45, 0.1)
    steel = mat("steel")
    rubber = mat("flat_rubber", (0.035, 0.035, 0.035), 0.85)
    housing = mat("flat_lamp_housing", (0.30, 0.31, 0.32), 0.45, 0.6)
    lens = mat("emit_sodium", SODIUM, emit=SODIUM)
    cab_glass = mat("emit_cab", (0.85, 0.62, 0.36), emit=(0.85, 0.62, 0.36))
    beacon = mat("emit_beacon", (1.0, 0.25, 0.08), emit=(1.0, 0.25, 0.08))
    dark = mat("flat_dark_steel", (0.08, 0.08, 0.085), 0.6, 0.5)
    objs = []
    top = HG + GH
    # Legs, sill beams, wheels.
    for sx in (-1, 1):
        for sy in (-1, 1):
            objs.append(box("leg", (0.9, 1.4, HG - 1.3), (sx * W2, sy * Y2, 1.3 + (HG - 1.3) / 2), paint, bevel=0.04))
            for k in (-0.34, 0.34):
                objs.append(cyl("wheel", 0.56, 0.42, (sx * W2 + k, sy * (Y2 + 0.55), 0.56), rubber, verts=20, rot=(0, 90, 0)))
                objs.append(cyl("hub", 0.28, 0.44, (sx * W2 + k, sy * (Y2 + 0.55), 0.56), dark, verts=12, rot=(0, 90, 0)))
            objs.append(box("bogie", (1.3, 1.6, 0.5), (sx * W2, sy * (Y2 + 0.55), 1.15), paint, bevel=0.04))
        objs.append(box("sill", (1.1, 2 * Y2 + 1.2, 0.9), (sx * W2, 0, 1.6), paint, bevel=0.04))
        # Side frame: a tie half-way up and two diagonals.
        objs.append(box("tie", (0.55, 2 * Y2, 0.55), (sx * W2, 0, 10.0), paint))
        objs.append(strut("diag", (sx * W2, -Y2, 2.2), (sx * W2, Y2, 9.8), 0.45, paint))
        objs.append(strut("diag", (sx * W2, Y2, 10.2), (sx * W2, -Y2, HG - 0.4), 0.45, paint))
        # Portal head (end beam) on top of the legs.
        objs.append(box("end_beam", (1.3, 2 * Y2 + 1.6, GH), (sx * W2, 0, HG + GH / 2), paint, bevel=0.05))
        # Knee braces between the legs and the girders.
        for sy in (-1, 1):
            objs.append(strut("knee", (sx * W2, sy * Y2, HG - 3.6), (sx * (W2 - 3.4), sy * 2.5, HG), 0.4, paint))
    # Main girders, trolley rails, walkways with railings.
    for sy in (-1, 1):
        objs.append(box("girder", (2 * W2 + 2.6, 1.1, GH), (0, sy * 2.5, HG + GH / 2), paint, bevel=0.05))
        objs.append(box("rail", (2 * W2, 0.12, 0.12), (0, sy * 2.5, top + 0.06), steel))
        y_walk = sy * 3.55
        objs.append(box("walkway", (2 * W2 + 1.6, 1.0, 0.06), (0, y_walk, top - 0.15), steel))
        for x in [(-W2 - 0.6) + 2.0 * k for k in range(13)]:
            objs.append(box("post", (0.05, 0.05, 1.05), (x, sy * 4.0, top + 0.4), paint))
        for h in (0.5, 1.0):
            objs.append(box("handrail", (2 * W2 + 1.4, 0.05, 0.05), (0, sy * 4.0, top - 0.12 + h), paint))
    # Trolley with its machinery house; the spreader on four ropes; the cab hanging off one side.
    tx = 3.0
    objs.append(box("trolley", (6.2, 6.4, 0.8), (tx, 0, top + 0.5), paint, bevel=0.05))
    objs.append(box("house", (5.0, 5.2, 3.0), (tx, 0, top + 0.9 + 1.5), paint, bevel=0.06))
    for sy in (-1, 1):
        for k in range(3):
            objs.append(box("vent", (1.0, 0.04, 0.8), (tx - 1.5 + 1.5 * k, sy * 2.62, top + 2.6), dark))
    objs.append(box("house_roof", (5.2, 5.4, 0.15), (tx, 0, top + 0.9 + 3.07), steel))
    objs.append(cyl("beacon", 0.12, 0.25, (tx, 0, top + 0.9 + 3.27), beacon, verts=10))
    objs.append(box("spreader", (12.2, 2.5, 0.45), (tx, 0, 11.0), paint, bevel=0.04))
    objs.append(box("spreader_beam", (3.0, 1.2, 0.8), (tx, 0, 11.6), paint, bevel=0.04))
    for dx in (-1.2, 1.2):
        for dy in (-0.45, 0.45):
            objs.append(strut("rope", (tx + dx, dy, 11.9), (tx + dx * 1.4, dy * 3.0, top + 0.1), 0.035, dark))
    cab = Vector((5.5, -3.3, HG - 1.4))
    objs.append(box("cab", (2.3, 2.1, 2.4), tuple(cab), paint, bevel=0.06))
    objs.append(box("cab_glass", (2.34, 2.14, 1.1), tuple(cab + Vector((0, 0, 0.35))), cab_glass))
    objs.append(box("cab_hanger", (1.6, 0.8, 1.6), tuple(cab + Vector((0, 1.2, 1.6))), paint))
    # Engine house on one sill.
    objs.append(box("engine_house", (2.4, 4.6, 2.6), (W2 + 1.75, 0, 3.4), paint, bevel=0.06))
    objs.append(box("engine_door", (0.04, 0.9, 2.0), (W2 + 2.96, -1.2, 3.2), dark))
    objs.append(box("engine_bracket", (2.2, 1.0, 0.4), (W2 + 1.1, 0, 2.0), steel))
    # Zig-zag stair up the outside of the leg at (-W2, +Y2), with landings and handrails.
    sx_out = -W2 - 1.15
    z = 1.3
    flights = 6
    rise = (HG - 1.3) / flights
    for k in range(flights):
        y0, y1 = (Y2 - 1.8, Y2 + 1.8) if k % 2 == 0 else (Y2 + 1.8, Y2 - 1.8)
        objs.append(strut("flight", (sx_out, y0, z), (sx_out, y1, z + rise), 0.08, steel, width=0.85))
        objs.append(strut("stair_rail", (sx_out - 0.44, y0, z + 0.95), (sx_out - 0.44, y1, z + rise + 0.95), 0.045, paint))
        objs.append(box("landing", (0.95, 1.0, 0.08), (sx_out, y1 + (0.5 if y1 > y0 else -0.5), z + rise), steel))
        objs.append(box("landing_rail", (0.05, 1.0, 0.05), (sx_out - 0.46, y1 + (0.5 if y1 > y0 else -0.5), z + rise + 1.0), paint))
        z += rise
    for y in (Y2 - 2.3, Y2 + 2.3):
        objs.append(box("stair_post", (0.08, 0.08, HG - 1.0), (sx_out - 0.42, y, (HG - 1.0) / 2 + 1.2), paint))
    # Floodlights under the girders, facing down.
    for (x, y, zz), kind in LAMPS:
        if kind != "flood":
            continue
        objs.append(box("lamp", (0.62, 0.48, 0.26), (x, y, zz), housing, bevel=0.03))
        objs.append(box("lamp_lens", (0.52, 0.38, 0.02), (x, y, zz - 0.14), lens))
    return objs


# ============================================================================================ build
BUILDERS = {"flood_mast": flood_mast, "gantry_crane": gantry_crane}


def build(name):
    K.reset()
    objs = BUILDERS[name]()
    objs = K.finish(objs, join_name=name)
    K.export(name, objs)
    try:
        K.preview(name, objs, angle=(-35, 18))
    except Exception as e:
        print(f"[port] preview of {name} failed: {e}", flush=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for name in argv or list(BUILDERS):
        try:
            build(name)
        except Exception:
            import traceback
            traceback.print_exc()
            print(f"[port] FAILED {name}", flush=True)
    print("[port] done", flush=True)


main()
