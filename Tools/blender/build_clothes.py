"""
Builds garments for the human and exports them.

    blender -b --factory-startup -P Tools/blender/build_clothes.py                         (all garments)
    blender -b --factory-startup -P Tools/blender/build_clothes.py -- tshirt boots_work    (only these ids)
    blender -b --factory-startup -P Tools/blender/build_clothes.py -- --no-preview         (skip the Cycles renders)

Opens Assets/human.blend (never saved), derives every garment from the body's own game mesh (clothkit: carve by region, offset,
relax, copy weights and UVs), runs the poke-through and coverage checks, and writes
  * Assets/Models/garments/<id>.glb and <id>.json   (CONTRACTS section 2)
  * Tools/previews/models/garment_<id>_<view>.png and garment_<id>.png (the contact sheet), plus garment_outfit*.png for the
    garments that were built together.
Recipes live in clothkit/catalog.py; another module, Tools/blender/garments_more.py, is imported when it exists and registers more.
"""
import importlib
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
BLEND = os.path.join(PROJECT, "Assets", "human.blend")


def script_args():
    argv = sys.argv
    if "--" in argv:
        return argv[argv.index("--") + 1:]
    return argv[argv.index(__file__) + 1:] if __file__ in argv else []


def main():
    args = script_args()
    previews = "--no-preview" not in args
    ids = [a for a in args if not a.startswith("--")]
    bpy.ops.wm.open_mainfile(filepath=BLEND)

    from clothkit import build, catalog, check, export, preview
    from clothkit.body import BodyData
    try:
        importlib.import_module("garments_more")
    except ModuleNotFoundError as e:
        if e.name != "garments_more":
            raise
    body = BodyData(bpy.data.objects["Human"], bpy.data.objects["Human_Rig"])
    ids = ids or list(catalog.REGISTRY)
    unknown = [i for i in ids if i not in catalog.REGISTRY]
    if unknown:
        raise SystemExit(f"unknown garment ids {unknown}; known: {sorted(catalog.REGISTRY)}")

    built = {i: build.build(body, catalog.REGISTRY[i]) for i in ids}
    pairs = []
    if "tshirt" in built and "trousers_work" in built:
        t, s = built["trousers_work"], built["tshirt"]
        pairs.append((t, s, None))
    if "boots_work" in built and "trousers_work" in built:
        bt, t = built["boots_work"], built["trousers_work"]
        pairs.append((bt, t, None))
    ok = check.summarize(check.run(body, list(built.values()), pairs))
    for inner, outer, _ in pairs:
        print(f"layer clearance {inner.garment.id} under {outer.garment.id}: {check.layer_clearance(inner, outer):.1f} mm at rest (>= 4 required)")
    print("P1-P3", "PASS" if ok else "FAIL")

    for i, b in built.items():
        over = b.triangles > b.garment.tri_budget
        print(f"{i}: {b.triangles} triangles (budget {b.garment.tri_budget}){' OVER BUDGET' if over else ''}")
        export.export_garment(b.obj, body.rig, b.garment, b.triangles)

    if previews:
        out = preview.PREVIEWS
        for i, b in built.items():
            for o in built.values():
                o.obj.hide_render = True            # one garment at a time on the bare body (render_views shows the listed ones)
            paths = preview.render_views(body, [b], i)
            preview.contact_sheet(paths, os.path.join(out, f"garment_{i}.png"))
        if len(built) > 1:
            paths = preview.render_views(body, list(built.values()), "outfit")
            preview.contact_sheet(paths, os.path.join(out, "garment_outfit.png"))
    # the .blend is deliberately not saved


if __name__ == "__main__":
    main()
