"""
Builds the optics that can be fitted to the guns in the gun locker (Game/Optics.cs) and exports them as
Assets/Models/optics.glb, one part each, with Assets/Models/optics_points.json (each one's line of sight above the rail,
and its length):

    RedDot       a small tube red dot on a riser (the L119A1's)
    Holo         a holographic sight, and HoloMagnifier the 3x magnifier behind it (the L143A2's)
    Prism4x      a squat 4x prism sight
    Acog6x       a 6x ACOG with a mini red dot on top (the L129A1's)
    Scope2to8    a 2-8x tube scope in rings (the L403A1's)
    Scope4to16   a 4-16x tube scope in rings (the L135A1's)
    PistolDot    a pistol's slide-mounted mini red dot
    Mount        a plain riser block, 1 m x 1 m (scaled in the game to fill from a gun with no rail up to the optic)

    blender -b --factory-startup --python-exit-code 1 -P Tools/blender/build_optics.py

Each is built with its eyepiece end at x = 0 and the top of the rail it clamps to at z = 0 (+X towards the muzzle, +Z up),
in millimetres, scaled to metres at the end, with the same kit as the British guns (build_british_guns.py).
"""
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import modelkit as K  # noqa: E402
from modelkit import box  # noqa: E402
import build_british_guns as B  # noqa: E402

MM = 0.001






def rounded(hw, zb, zt, r, n=6, arch=False):
    """A rounded rectangle in (y, z), counter-clockwise from the bottom right; arch: only its top half and sides, open at the bottom."""
    pts = [(hw, zb)] if arch else [(hw - r, zb)] + [(hw - r + r * math.cos(a), zb + r + r * math.sin(a)) for a in
                                                     [-math.pi / 2 + math.pi / 2 * k / n for k in range(1, n)]] + [(hw, zb + r)]
    for cy, cz, a0 in ((hw - r, zt - r, 0.0), (-hw + r, zt - r, math.pi / 2)):
        pts += [(cy + r * math.cos(a0 + math.pi / 2 * k / n), cz + r * math.sin(a0 + math.pi / 2 * k / n)) for k in range(n + 1)]
    if arch:
        return pts + [(-hw, zb)]
    pts += [(-hw, zb + r)] + [(-hw + r + r * math.cos(a), zb + r + r * math.sin(a)) for a in
                              [math.pi + math.pi / 2 * k / n for k in range(1, n)]]
    return pts


def band(K, name, outer, inner, x0, x1, material, closed=True):
    """A wall between two matching outlines in (y, z), from x0 to x1: a frame (closed) or an arch (open at its ends)."""
    import bmesh
    bm = bmesh.new()
    n = len(outer)
    V = {(s, i, k): bm.verts.new((x, (outer if s == 0 else inner)[i][0], (outer if s == 0 else inner)[i][1]))
         for s in (0, 1) for i in range(n) for k, x in ((0, x0), (1, x1))}
    last = n if closed else n - 1
    for i in range(last):
        j = (i + 1) % n
        bm.faces.new((V[0, i, 0], V[0, j, 0], V[0, j, 1], V[0, i, 1]))      # outside
        bm.faces.new((V[1, i, 1], V[1, j, 1], V[1, j, 0], V[1, i, 0]))      # inside
        bm.faces.new((V[0, i, 0], V[1, i, 0], V[1, j, 0], V[0, j, 0]))      # back face
        bm.faces.new((V[0, j, 1], V[1, j, 1], V[1, i, 1], V[0, i, 1]))      # front face
    if not closed:
        for i in (0, n - 1):
            bm.faces.new((V[0, i, 0], V[0, i, 1], V[1, i, 1], V[1, i, 0]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return K._new_obj(name, bm, material)


def eotech(O, B, K, m, axis, x0, x1):
    """A holographic sight like an EOTech, its back at x0 and front at x1, its window round the line of sight at axis
    (rail's top at z = 0): an arched hood over a rounded window frame, on a base whose back is the control panel (two
    rubber buttons, a knob between them, four screws), clamped to the rail."""
    win_hw, win_h = 15.0, 27.0
    zb, zt = axis - win_h / 2, axis + win_h / 2
    # the base: wide feet that the hood's sides come down onto, the body over them, the rail clamp under
    O.append(B.bx("p_eo_feet", x0 + 4, x1 - 2, 0, 8, 50, m.anod, bevel=1.5))
    O.append(B.bx("p_eo_body", x0 + 2, x1 - 2, 6, zb, 38, m.anod, bevel=2.0))
    O.append(B.bx("p_eo_clamp", x0 + 20, x0 + 70, -3, 1, 26, m.anod, bevel=0.6))
    O.append(B.smooth(B.cyl("p_eo_clamp_knob", 6, 5, (x0 + 45, 27, 3), m.anod, verts=16, rot=(90, 0, 0))))
    # the window frame, closed round the window, the length of the body
    O.append(band(K, "p_eo_frame", rounded(win_hw + 3.5, zb - 3, zt + 3, 5.5), rounded(win_hw, zb, zt, 3.5), x0 + 2, x1 - 4, m.anod))
    # the hood: an arch over the top and down both sides onto the feet, a gap between it and the frame
    O.append(band(K, "p_eo_hood", rounded(25, 5, zt + 12, 10, arch=True), rounded(21.5, 5, zt + 8.5, 7, arch=True), x0 + 6, x1, m.anod, closed=False))
    # the control panel on the back of the base: a plate, two square buttons, the knob, four screws
    O.append(B.bx("p_eo_panel", x0, x0 + 3, 7, zb - 2, 34, m.rubber, bevel=0.6))
    for s in (1, -1):
        O.append(B.bx(f"p_eo_button{s}", x0 - 1.6, x0 + 1, 9, zb - 5, 9, m.rubber, y=s * 9.5, bevel=0.8))
        for z in (8.5, zb - 3.5):
            O.append(B.smooth(B.cyl(f"p_eo_screw{s}{int(z)}", 1.6, 1.2, (x0 - 0.3, s * 15, z), m.steel, verts=10, rot=(0, 90, 0))))
    O.append(B.smooth(B.cyl("p_eo_knob", 3.6, 2.4, (x0 - 0.8, 0, zb - 4), m.anod, verts=16, rot=(0, 90, 0))))


def build(m):
    parts, points = {}, {}

    def optic(name, axis, length, make):
        objs = []
        make(objs)
        parts[name] = objs
        points[name] = {"Axis": round(axis * MM, 5), "Length": round(length * MM, 5)}

    optic("RedDot", 40.0, 62.0, lambda O: B.dot_sight(O, m, 0.0, 62.0, 40.0, m.anod, 0.0))

    def holo(O):
        axis = 31.0
        eotech(O, B, K, m, axis, 82.0, 178.0)   # (no glass in its window: the game would draw it opaque)
        # The magnifier is a part of its own: the game swings it aside on its mount at 1x, in line at 3x.
        G = parts["HoloMagnifier"] = []
        G.append(B.smooth(B.lathe("p_magnifier", [(0, 0), (15, 0), (18, 4), (18, 22), (15, 30), (15, 64), (0, 64)], loc=(0, 0, axis), rot=B.RX,
                                  material=m.anod, verts=28)))
        O.append(B.bx("p_mag_mount", 14, 52, 0, axis - 13, 24, m.anod))
        G.append(B.cyl("p_mag_lens", 14, 0.6, (0.4, 0, axis), m.glass, verts=24, rot=B.RX))
    optic("Holo", 31.0, 178.0, holo)

    optic("Prism4x", 40.0, 130.0, lambda O: B.prism_sight(O, m, 0.0, 130.0, 40.0, m.anod, 0.0, r=19.0))
    optic("Acog6x", 44.0, 154.0, lambda O: B.prism_sight(O, m, 0.0, 154.0, 44.0, m.anod, 0.0, r=20.0, top_dot=True))
    optic("Scope2to8", 47.0, 268.0, lambda O: B.tube_scope(O, m, 0.0, 268.0, 47.0, m.anod, 0.0, r_tube=15, r_obj=17.5, r_eye=20, turret=138.0))
    optic("Scope4to16", 62.0, 360.0, lambda O: B.tube_scope(O, m, 0.0, 360.0, 62.0, m.anod, 0.0, r_tube=17, r_obj=30, r_eye=21, turret=170.0))

    def pistol_dot(O):
        # A low mini dot: the body, and a hood round an open window (two posts and a bar over the top) at the front.
        O.append(B.bx("p_pdot_body", 0, 44, 0, 9, 25, m.anod))
        for s in (1, -1):
            O.append(B.bx(f"p_pdot_post{s}", 28, 44, 9, 26, 3, m.anod, y=s * 11, bevel=0.5))
        O.append(B.bx("p_pdot_bar", 28, 44, 23, 26.5, 25, m.anod, bevel=0.5))
        O.append(B.bx("p_pdot_emitter", 6, 12, 9, 12, 6, m.red, bevel=0.3))
        O.append(B.bx("p_pdot_rear", 0, 6, 9, 13, 25, m.anod))
    optic("PistolDot", 16.0, 44.0, pistol_dot)

    parts["Mount"] = [box("p_mount", (1000.0, 24.0, 1000.0), (0, 0, -500.0), m.anod)]
    return parts, points


def make():
    K.reset()
    parts, points = build(B.Mats())
    S = Matrix.Scale(MM, 4)
    bpy.context.view_layer.update()
    for o in list(bpy.data.objects):
        o.matrix_world = S @ o.matrix_world
        if o.get("uv") in ("cyl", "ring"):
            o["uv"] = "box"
    final = []
    for pname, objs in parts.items():
        out = K.finish(objs, join_name=pname, smooth_angle=40.0)[0]
        bpy.ops.object.select_all(action='DESELECT')
        out.select_set(True)
        bpy.context.view_layer.objects.active = out
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        bm = bmesh.new()
        bm.from_mesh(out.data)
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
        bm.to_mesh(out.data)
        bm.free()
        final.append(out)
    points["Note"] = "Metres: Axis is the line of sight above the rail's top, Length from the eyepiece's end (x = 0) to the front."
    with open(os.path.join(K.OUT, "optics_points.json"), "w") as f:
        json.dump(points, f, indent=2)
    K.export("optics", final)
    print(f"[optics] {len(final)} parts", flush=True)


if __name__ == "__main__":
    make()
