"""
Builds the S3 garments (clothkit/garments_more.py), checks them against CONTRACTS section 2, exports them, and renders the previews.

    python3.13 Tools/blender/build_clothes_more.py                        (all 11 garments, all checks, export, previews)
    python3.13 Tools/blender/build_clothes_more.py cap hair_short         (only these ids)
    python3.13 Tools/blender/build_clothes_more.py --no-preview --no-export --no-check

The checks (clothkit/check.py): coverage; P1 (body poke-through), P2 (holes), P3 (layers) over the rest pose and every 4th frame of
Walk, Idle, Run and Cower (Run and Cower come from Assets/human_anims.blend); the pairs the brief lists; the layer clearance (>= 4 mm).
S2's garments are built in memory for the pairs and the outfit sheet; their files are never written.
Previews: Tools/previews/models/garment_<id>.png (front, back, three-quarter, Walk 9; 400x600, 16 samples) and
garment_outfits.png (one outfit per wardrobe.json archetype: the first non-null choice per slot, the first colour of its palette).
"""
import json
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
BLEND = os.path.join(PROJECT, "Assets", "human.blend")
ANIMS = os.path.join(PROJECT, "Assets", "human_anims.blend")
WARDROBE = os.path.join(PROJECT, "Assets", "Models", "garments", "wardrobe.json")

S3 = ["longsleeve", "hoodie", "work_jacket", "hivis_vest", "plate_carrier", "jeans", "shorts", "sneakers", "cap", "beanie", "balaclava", "hair_short", "hair_buzz", "helmet",
      "hivis_jacket", "hivis_trousers", "parka", "snow_trousers", "parka_hood"]
HEAD = {"cap", "beanie", "balaclava", "hair_short", "hair_buzz", "helmet", "parka_hood"}
TORSO = ["tshirt", "longsleeve", "hoodie", "work_jacket", "hivis_vest", "plate_carrier", "hivis_jacket", "parka"]
PAIRS = ([(h, h2) for h in ("hair_short", "hair_buzz") for h2 in ("cap", "beanie", "helmet", "parka_hood")]
         + [(i, o) for i in ("tshirt", "longsleeve") for o in ("hoodie", "work_jacket", "hivis_vest", "plate_carrier", "hivis_jacket", "parka")]
         + [(l, t) for l in ("jeans", "shorts", "hivis_trousers", "snow_trousers") for t in TORSO]
         + [(f, "jeans") for f in ("boots_work", "sneakers")] + [("boots_work", l) for l in ("hivis_trousers", "snow_trousers")])


def args():
    a = sys.argv
    return a[a.index("--") + 1:] if "--" in a else a[1:]


def poses(step=4, quick=False):
    """Rest, then every `step`th frame of Walk, Idle, Run and Cower (quick: a handful of frames, for iterating)."""
    out = [("rest", None, 1)]
    if quick:
        return out + [(f"{n}{f}", n, f) for n, fs in (("Walk", (9, 21)), ("Run", (1, 9, 17)), ("Cower", (1, 25, 49))) for f in fs]
    for name in ("Walk", "Idle", "Run", "Cower"):
        a = bpy.data.actions[name]
        f0, f1 = int(a.frame_range[0]), int(a.frame_range[1])
        out += [(f"{name}{f}", name, f) for f in range(f0, f1 + 1, step)]
    return out


def srgb(c):
    """linear -> sRGB (wardrobe palettes are linear; the preview materials take sRGB)."""
    return tuple(12.92 * x if x <= 0.0031308 else 1.055 * x ** (1 / 2.4) - 0.055 for x in c)


def outfits():
    w = json.load(open(WARDROBE))
    out = {}
    for name, a in w["archetypes"].items():
        ids = []
        for slot, choices in a["slots"].items():
            pick = next((c for c in choices if c["id"]), None)
            if pick:
                ids.append((pick["id"], w["palettes"][pick["palette"]][0]))
        out[name] = ids
    return out


def main():
    A = args()
    flags = {x for x in A if x.startswith("--")}
    ids = [x for x in A if not x.startswith("--")] or S3
    bpy.ops.wm.open_mainfile(filepath=BLEND)
    with bpy.data.libraries.load(ANIMS) as (src, dst):
        dst.actions = [n for n in src.actions if n in ("Run", "Cower")]

    from clothkit import build, catalog, check, export, preview
    from clothkit.body import BodyData
    from clothkit.carve import log
    body = BodyData(bpy.data.objects["Human"], bpy.data.objects["Human_Rig"])

    need = set(ids)
    if "--no-check" not in flags:
        for i, o in PAIRS:
            if i in need or o in need:
                need |= {i, o}
    if "--no-preview" not in flags:
        for _, cols in outfits().items():
            need |= {g for g, _ in cols}
    built = {i: build.build(body, catalog.REGISTRY[i]) for i in catalog.REGISTRY if i in need}

    report = {}
    if "--no-check" not in flags:
        pairs = [(built[i], built[o], None) for i, o in PAIRS if i in built and o in built and (i in ids or o in ids)]
        res = check.run(body, [built[i] for i in need if i in built], pairs, poses=poses(quick="--quick" in flags), local=True, only=set(ids))
        ok = check.summarize(res)
        log("P1-P3", "PASS" if ok else "FAIL")
        for inner, outer, _ in pairs:
            log(f"layer clearance {inner.garment.id} under {outer.garment.id}: {check.layer_clearance(inner, outer, drawn_only=True):.1f} mm at rest, drawn vertices only "
                f"({check.layer_clearance(inner, outer):.1f} mm over every vertex; >= 4 required)")
        for i in ids:
            b = built[i]
            log(f"{i}: {b.triangles} triangles (budget {b.garment.tri_budget}), coverage {b.coverage['uncovered']} uncovered, "
                f"min {b.coverage['min_mm']:.1f} mm (>= {b.garment.coverage_mm:.0f})")
        os.makedirs(os.environ.get("S3_OUT", "/tmp"), exist_ok=True)
        json.dump({k: {n: {f: list(v) for f, v in fr.items()} for n, fr in d.items()} for k, d in res.items()},
                  open(os.path.join(os.environ.get("S3_OUT", "/tmp"), "s3_results.json"), "w"))

    if "--no-export" not in flags:
        for i in ids:
            b = built[i]
            export.export_garment(b.obj, body.rig, b.garment, b.triangles)

    if "--no-preview" not in flags:
        preview.W, preview.H, preview.SAMPLES = 400, 600, 16
        tmp = os.path.join(os.environ.get("S3_OUT", "/tmp"), "s3_views")
        views = [("front", 0, "rest"), ("back", 180, "rest"), ("three_quarter", 35, "rest"), ("walk9", 60, ("Walk", 9))]
        for i in ids:
            preview.ORTHO, preview.CENTRE_Z = (0.62, 1.64) if i in HEAD else ((0.8, 0.32) if i == "sneakers" else (2.0, 0.9))
            for o in built.values():
                o.obj.hide_render = True
            paths = preview.render_views(body, [built[i]], i, outdir=tmp, views=views)
            preview.contact_sheet(paths, os.path.join(preview.PREVIEWS, f"garment_{i}.png"), cols=2)
        preview.ORTHO, preview.CENTRE_Z = 2.0, 0.9
        paths = []
        for n, (name, cols) in enumerate(outfits().items()):
            items = [(built[g], {m: srgb(c) for m in built[g].garment.preview_colors}) for g, c in cols]
            for o in built.values():
                o.obj.hide_render = True
            paths += preview.render_outfit(body, items, f"outfit_{name}", outdir=tmp, views=[("front", 0, "rest"), ("walk9", 60, ("Walk", 9))])
            log(f"outfit {name}: {[g for g, _ in cols]}")
        # a 4 x 2 sheet: the archetypes left to right, the front above the walk frame
        order = [paths[0], paths[2], paths[4], paths[6], paths[1], paths[3], paths[5], paths[7]]
        preview.contact_sheet(order, os.path.join(preview.PREVIEWS, "garment_outfits.png"), cols=4)


if __name__ == "__main__":
    main()
