"""Acceptance check 1: human.glb carries _REGION (0-14, all 15 regions non-empty), 59 joints, clips Walk and Idle, and the
same vertex count as the committed-before file.

    python3.13 Tools/tests/check_human_regions.py [new.glb] [old.glb]
"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from glbread_cloth import Glb  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
new = Glb(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "Assets", "Models", "human.glb"))
old = Glb(sys.argv[2]) if len(sys.argv) > 2 else None
ok = True


def check(name, cond, info):
    global ok
    ok &= bool(cond)
    print(("PASS " if cond else "FAIL ") + name + ": " + str(info))


reg = []
for m, p in new.primitives():
    if "_REGION" in p["attributes"]:
        reg.append(new.attr(p, "_REGION")[:, 0])
    else:
        reg.append(None)
body = reg[0]
check("_REGION on the body primitive", body is not None, body is not None)
check("_REGION on the eye primitives", all(r is not None for r in reg), [None if r is None else len(r) for r in reg])
allr = np.concatenate([r for r in reg if r is not None])
check("values integer in 0-14", np.all(allr == np.rint(allr)) and allr.min() >= 0 and allr.max() <= 14, (allr.min(), allr.max()))
cnt = np.bincount(np.rint(allr).astype(int), minlength=15)
check("all 15 regions non-empty", (cnt > 0).all(), cnt.tolist())
names, ibm = new.joints()
check("joint count 59", len(names) == 59, len(names))
anims = [a["name"] for a in new.js.get("animations", [])]
check("clips Walk and Idle", "Walk" in anims and "Idle" in anims, anims)
if old:
    check("vertex count unchanged", new.vertices() == old.vertices(), (new.vertices(), old.vertices()))
    check("triangle count unchanged", new.triangles() == old.triangles(), (new.triangles(), old.triangles()))
    pn = np.concatenate([new.attr(p, "POSITION") for m, p in new.primitives()])
    po = np.concatenate([old.attr(p, "POSITION") for m, p in old.primitives()])
    check("positions identical", pn.shape == po.shape and np.abs(pn - po).max() < 1e-6,
          np.abs(pn - po).max() if pn.shape == po.shape else "shape")
    on, oi = old.joints()
    check("inverse bind matrices identical", on == names and np.abs(oi - ibm).max() < 1e-6, np.abs(oi - ibm).max())
print("ALL PASS" if ok else "FAILED")
sys.exit(0 if ok else 1)
