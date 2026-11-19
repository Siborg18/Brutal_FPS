"""
Builds the white plastic garden set the yard crew play cards at (Level.BuildYardCardTable) and exports it to
Assets/Models/garden_table.glb and garden_chair.glb, with previews in Tools/previews/models.

    blender -b --factory-startup --python-exit-code 1 -P Tools/blender/build_garden_furniture.py

  garden_table   round resin patio table, 0.92 m across, top at 0.72 m, four splayed legs, a parasol hole in the
                 middle; origin on the ground under its centre
  garden_chair   stacking resin armchair: slatted seat (top at SEAT_Z) and back, a looped frame that runs down
                 into the arms, four splayed legs. It faces game +Z (Blender -Y): the way someone sat in it looks.
                 Origin on the ground under the middle of the seat (where the sitter's ArmedNpc.Position is)

Conventions as modelkit.py: Blender +Z is up, -Y is the game's +Z.
"""
import math
import os
import sys

from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import modelkit as K  # noqa: E402
from modelkit import box, cyl, lathe, tube, mat  # noqa: E402

TABLE_R = 0.46
TABLE_Z = 0.72
SEAT_Z = 0.43


def resin():
    return mat("flat_resin_white", (0.90, 0.90, 0.88), rough=0.42)


def resin_shade():
    # The undersides and slots: the same plastic, a little greyer where it's always in the shade and grubby.
    return mat("flat_resin_grey", (0.70, 0.70, 0.68), rough=0.55)


def leg(name, top, foot, r_top, r_foot, material):
    """A tapered round leg from top to foot."""
    d = Vector(foot) - Vector(top)
    mid = (Vector(top) + Vector(foot)) / 2
    o = cyl(name, r_foot, d.length, mid, material, verts=12, radius2=r_top)
    # cyl runs along local Z from -depth/2 (radius) to +depth/2 (radius2): point -Z at the foot.
    o.rotation_mode = 'QUATERNION'
    o.rotation_quaternion = (-d).to_track_quat('Z', 'Y')
    return o


def garden_table():
    objs = []
    w = resin()
    # Top: a shallow dish with a rolled rim (lathe profile, radius and height), and the parasol hole.
    prof = [(0.03, TABLE_Z - 0.012), (0.03, TABLE_Z), (TABLE_R - 0.04, TABLE_Z), (TABLE_R - 0.012, TABLE_Z + 0.006),
            (TABLE_R, TABLE_Z - 0.008), (TABLE_R - 0.004, TABLE_Z - 0.045), (TABLE_R - 0.025, TABLE_Z - 0.05),
            (TABLE_R - 0.03, TABLE_Z - 0.02), (0.03, TABLE_Z - 0.02)]
    top = lathe("top", prof, material=w, verts=64)
    top["smooth"] = True
    objs.append(top)
    objs.append(cyl("hole", 0.03, 0.022, (0, 0, TABLE_Z - 0.01), mat("flat_hole", (0.08, 0.08, 0.08), rough=0.9), verts=16))
    # A raised ring round the hole.
    objs.append(lathe("boss", [(0.03, TABLE_Z), (0.045, TABLE_Z + 0.004), (0.06, TABLE_Z)], material=w, verts=32))
    # Skirt under the top that the legs plug into.
    objs.append(cyl("hub", 0.20, 0.06, (0, 0, TABLE_Z - 0.05), resin_shade(), verts=32, radius2=0.22))
    # Four legs, splayed out to the corners.
    for i in range(4):
        a = math.radians(45 + 90 * i)
        c, s = math.cos(a), math.sin(a)
        objs.append(leg("leg", (0.17 * c, 0.17 * s, TABLE_Z - 0.06), (0.36 * c, 0.36 * s, 0.0), 0.034, 0.024, w))
        objs.append(cyl("foot", 0.028, 0.012, (0.36 * c, 0.36 * s, 0.006), resin_shade(), verts=12))
    # Stretcher ring between the legs.
    pts = []
    for i in range(33):
        a = 2 * math.pi * i / 32
        pts.append((0.29 * math.cos(a), 0.29 * math.sin(a), 0.24))
    objs.append(tube("ring", pts, 0.012, w, verts=8))
    return objs


def garden_chair():
    objs = []
    w = resin()
    g = resin_shade()
    hw, front, back = 0.24, -0.22, 0.20            # half width, seat front and back (Blender -Y is the front)
    # Seat: a shell with slots (slats across it, the shell's dish under them).
    objs.append(box("seat_pan", (2 * hw - 0.04, back - front - 0.04, 0.02), (0, (front + back) / 2, SEAT_Z - 0.025), g, bevel=0.006))
    n = 7
    for i in range(n):
        y = front + 0.03 + (back - front - 0.06) * i / (n - 1)
        dip = -0.012 * math.sin(math.pi * i / (n - 1))   # dished a little
        objs.append(box("slat", (2 * hw - 0.02, 0.042, 0.022), (0, y, SEAT_Z - 0.01 + dip), w, bevel=0.007))
    objs.append(box("seat_lip", (2 * hw, 0.03, 0.05), (0, front - 0.005, SEAT_Z - 0.03), w, rot=(-20, 0, 0), bevel=0.01))

    # Back: leaning back 14 degrees from the seat's back edge, slatted, up to 0.82 m.
    lean = math.radians(14)
    def on_back(t, x, out=0.0):
        """A point on the back's plane, t metres up it from the seat, x across."""
        return (x, back + math.sin(lean) * t + math.cos(lean) * out, SEAT_Z + math.cos(lean) * t - math.sin(lean) * out)
    height = 0.40
    for i in range(5):
        t = 0.10 + 0.27 * i / 4
        objs.append(box("back_slat", (2 * hw - 0.05, 0.02, 0.04), on_back(t, 0), w, rot=(-math.degrees(lean), 0, 0), bevel=0.006))
    objs.append(box("back_spine", (0.06, 0.018, height - 0.06), on_back(height / 2, 0, 0.012), g, rot=(-math.degrees(lean), 0, 0), bevel=0.006))

    # The looped frame: from the front of one arm, back along it, up round the back's top and down to the other arm.
    arm_z = SEAT_Z + 0.22
    frame = [(-hw - 0.02, front + 0.02, arm_z - 0.03), (-hw - 0.03, front + 0.10, arm_z), (-hw - 0.03, back - 0.02, arm_z + 0.02),
             on_back(height * 0.85, -hw - 0.01), on_back(height, -hw * 0.6), on_back(height + 0.02, 0),
             on_back(height, hw * 0.6), on_back(height * 0.85, hw + 0.01),
             (hw + 0.03, back - 0.02, arm_z + 0.02), (hw + 0.03, front + 0.10, arm_z), (hw + 0.02, front + 0.02, arm_z - 0.03)]
    f = tube("frame", frame, 0.022, w, verts=10)
    f["smooth_angle"] = 60.0
    objs.append(f)
    # Arm supports down to the seat's front corners, and the sides of the back down to the seat's back corners.
    for sx in (-1, 1):
        objs.append(tube("arm_post", [(sx * (hw + 0.02), front + 0.03, arm_z - 0.04), (sx * (hw + 0.005), front + 0.05, SEAT_Z + 0.06),
                                      (sx * hw, front + 0.06, SEAT_Z - 0.01)], 0.018, w, verts=8))
        objs.append(tube("back_side", [on_back(height * 0.85, sx * (hw + 0.01)), on_back(0.15, sx * (hw - 0.005)), (sx * (hw - 0.01), back, SEAT_Z - 0.01)],
                         0.02, w, verts=8))
        # A side rail under the seat edge.
        objs.append(box("rail", (0.025, back - front, 0.05), (sx * (hw - 0.012), (front + back) / 2, SEAT_Z - 0.05), w, bevel=0.008))

    # Legs: splayed out from under the seat corners.
    for sx in (-1, 1):
        for sy, y in ((-1, front + 0.03), (1, back - 0.02)):
            objs.append(leg("leg", (sx * (hw - 0.03), y, SEAT_Z - 0.04), (sx * (hw + 0.03), y + sy * 0.06, 0.0), 0.024, 0.018, w))
    return objs


BUILDERS = {"garden_table": garden_table, "garden_chair": garden_chair}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for name in argv or list(BUILDERS):
        K.reset()
        objs = K.finish(BUILDERS[name](), join_name=name)
        K.export(name, objs)
        try:
            K.preview(name, objs)
        except Exception as e:  # previews are a convenience
            print(f"[model] preview of {name} failed: {e}", flush=True)
    print("[model] done", flush=True)


if __name__ == "__main__":
    main()
