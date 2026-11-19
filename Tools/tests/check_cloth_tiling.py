"""S7 acceptance: file set + 1024^2 + seamless tiling of the cloth texture sets.
Run: python3.13 Tools/tests/check_cloth_tiling.py   (needs bpy for PNG loading)
Edge difference (right vs left column, top vs bottom row) must be <= 2x the mean difference of adjacent interior columns/rows."""
import os, sys
import bpy, numpy as np
TEX = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Textures")
SETS = sys.argv[1:] or ["denim", "canvas", "leather", "knit", "hair"]
SUFFIX = ["albedo", "normal", "orm"]

def load(path):
    im = bpy.data.images.load(path); w, h = im.size
    a = np.empty(w * h * 4, np.float32); im.pixels.foreach_get(a)
    bpy.data.images.remove(im); return a.reshape(h, w, 4)

ok = True
for s in SETS:
    for suf in SUFFIX:
        p = os.path.join(TEX, f"{s}_{suf}.png")
        if not os.path.exists(p):
            print(f"FAIL {s}_{suf}: missing"); ok = False; continue
        a = load(p)
        if a.shape[:2] != (1024, 1024):
            print(f"FAIL {s}_{suf}: size {a.shape}"); ok = False
        if suf == "orm":
            continue
        c = a[..., :3]
        ex = np.abs(c[:, -1] - c[:, 0]).mean(); ix = np.abs(c[:, 1:] - c[:, :-1]).mean()
        ey = np.abs(c[-1] - c[0]).mean(); iy = np.abs(c[1:] - c[:-1]).mean()
        rx, ry = ex / ix, ey / iy
        good = rx <= 2 and ry <= 2
        ok &= good
        print(f"{'PASS' if good else 'FAIL'} {s}_{suf}: LR edge {ex:.5f} vs interior {ix:.5f} (x{rx:.2f}); TB edge {ey:.5f} vs {iy:.5f} (x{ry:.2f})")
print("ALL PASS" if ok else "SOME FAIL")
