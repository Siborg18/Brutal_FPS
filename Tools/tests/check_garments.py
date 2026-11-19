"""Acceptance checks 4 and 5 (and the glTF basics) for every garment in Assets/Models/garments/:

  * joints: 59 deform joints, the same names as human.glb, and inverse bind matrices equal to human.glb's within 1e-4 (by name)
  * _REGION on every vertex
  * at most 4 influences per vertex, weights normalised, no animations
  * the sidecar has exactly the CONTRACTS section 2 keys and its "triangles" equals the glb's triangle count
  * the triangle budgets (tshirt 6000, trousers_work 7000, boots_work 3000)

    python3.13 Tools/tests/check_garments.py [garment ids...]
"""
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from glbread_cloth import Glb  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
GAR = os.path.join(ROOT, "Assets", "Models", "garments")
BUDGET = {"tshirt": 6000, "trousers_work": 7000, "boots_work": 3000}
KEYS = ["id", "slot", "layer", "offset_mm", "hides", "wound", "materials", "triangles"]
SLOTS = {"hair", "head", "torso_base", "torso_outer", "legs", "feet"}
REGIONS = {"scalp", "head", "neck", "chest", "belly", "hips", "upperarm_hi", "upperarm_lo", "forearm", "hand", "thigh_hi",
           "thigh_lo", "calf_hi", "calf_lo", "foot"}
ok = True


def check(name, cond, info):
    global ok
    ok &= bool(cond)
    print(("PASS " if cond else "FAIL ") + name + ": " + str(info))


human = Glb(os.path.join(ROOT, "Assets", "Models", "human.glb"))
hnames, hibm = human.joints()
hmap = {n: hibm[i] for i, n in enumerate(hnames)}
ids = sys.argv[1:] or sorted(f[:-4] for f in os.listdir(GAR) if f.endswith(".glb"))
for gid in ids:
    g = Glb(os.path.join(GAR, gid + ".glb"))
    names, ibm = g.joints()
    check(f"{gid}: joints", len(names) == 59 and set(names) == set(hnames), len(names))
    err = max(float(np.abs(ibm[i] - hmap[n]).max()) for i, n in enumerate(names))
    check(f"{gid}: inverse bind matrices equal human.glb (max abs diff <= 1e-4)", err <= 1e-4, f"{err:.2e}")
    infl, wsum = 0, []
    for m, p in g.primitives():
        w = g.attr(p, "WEIGHTS_0")
        infl = max(infl, int((w > 1e-6).sum(axis=1).max()))
        wsum.append(w.sum(axis=1))
    wsum = np.concatenate(wsum)
    check(f"{gid}: <= 4 influences, weights sum to 1", infl <= 4 and np.abs(wsum - 1).max() < 1e-3, f"{infl} influences, "
          f"max |sum-1| {np.abs(wsum - 1).max():.1e}")
    reg = np.concatenate([g.attr(p, "_REGION")[:, 0] for m, p in g.primitives()])
    check(f"{gid}: _REGION on every vertex, integer 0-14", np.all(reg == np.rint(reg)) and reg.min() >= 0 and reg.max() <= 14, (reg.min(), reg.max()))
    check(f"{gid}: no animations", not g.js.get("animations"), len(g.js.get("animations", [])))
    tris = g.triangles()
    check(f"{gid}: {tris} triangles within budget", tris <= BUDGET.get(gid, 10 ** 9), f"{tris} <= {BUDGET.get(gid)}")
    sc = json.load(open(os.path.join(GAR, gid + ".json")))
    check(f"{gid}: sidecar keys exactly {KEYS}", list(sc) == KEYS, list(sc))
    check(f"{gid}: sidecar values", sc["id"] == gid and sc["slot"] in SLOTS and isinstance(sc["layer"], int)
          and isinstance(sc["offset_mm"], (int, float)) and set(sc["hides"]) <= REGIONS and sc["wound"] in ("skin", "fabric", "hard")
          and isinstance(sc["materials"], list) and sc["triangles"] == tris,
          f"triangles {sc['triangles']} vs glb {tris}")
print("ALL PASS" if ok else "SOME FAILED")
sys.exit(0 if ok else 1)
