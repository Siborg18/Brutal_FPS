"""Logic test for Engine/SkinnedModel.cs (S1): the region triangle filter and the joint remap, ported to Python.

Run: python3 Tools/tests/test_skinned_filter.py [Assets/Models/human.glb]
human.glb has no _REGION attribute yet, so regions are faked from each vertex's dominant joint name
(CONTRACTS.md section 1, ignoring the position t along the bone).
"""
import json
import struct
import sys

import numpy as np

REGIONS = ["head", "scalp", "neck", "chest", "belly", "hips", "upperarm_hi", "upperarm_lo", "forearm", "hand",
           "thigh_hi", "thigh_lo", "calf_hi", "calf_lo", "foot"]


def load_glb(path):
    data = open(path, "rb").read()
    _, _, _ = struct.unpack_from("<III", data, 0)
    off, js, bin_ = 12, None, None
    while off < len(data):
        ln, ty = struct.unpack_from("<II", data, off)
        chunk = data[off + 8:off + 8 + ln]
        if ty == 0x4E4F534A:
            js = json.loads(chunk)
        elif ty == 0x004E4942:
            bin_ = chunk
        off += 8 + ln
    return js, bin_


def accessor(js, bin_, idx):
    a = js["accessors"][idx]
    v = js["bufferViews"][a["bufferView"]]
    comps = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}[a["type"]]
    dt = {5126: "<f4", 5125: "<u4", 5123: "<u2", 5121: "u1", 5120: "i1", 5122: "<i2"}[a["componentType"]]
    size = np.dtype(dt).itemsize
    stride = v.get("byteStride", comps * size)
    start = v.get("byteOffset", 0) + a.get("byteOffset", 0)
    out = np.empty((a["count"], comps), dtype=np.float64)
    for c in range(comps):
        out[:, c] = np.ndarray(a["count"], dt, bin_, start + c * size, (stride,))
    if a.get("normalized"):
        out /= {"u1": 255, "<u2": 65535, "i1": 127, "<i2": 32767}.get(dt, 1)
    return out


def region_of(name):
    n = name.lower()
    for key, r in [("scalp", 1), ("head", 0), ("eye", 0), ("neck", 2), ("clavicle", 3), ("spine_03", 3), ("spine_02", 4),
                   ("spine_01", 4), ("pelvis", 5), ("root", 5), ("upperarm", 6), ("forearm", 8), ("hand", 9),
                   ("thigh", 10), ("calf", 12), ("foot", 14), ("ball", 14)]:
        if key in n:
            return r
    if any(f in n for f in ("thumb", "index", "middle", "ring", "pinky")):
        return 9
    return 5


def mask_of(names):
    m = 0
    for n in names:
        m |= 1 << REGIONS.index(n)
    return m


def filter_indices(indices, regions, mask):
    """Port of SkinnedPart.FilterIndices: drop a triangle only if all three vertices' regions are hidden."""
    kept = []
    for k in range(0, len(indices) - 2, 3):
        a, b, c = indices[k:k + 3]
        if (mask >> regions[a]) & (mask >> regions[b]) & (mask >> regions[c]) & 1:
            continue
        kept.extend((a, b, c))
    return kept


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else "Assets/Models/human.glb"
    js, bin_ = load_glb(path)
    skin = js["skins"][0]
    node_names = [n.get("name", f"node{i}") for i, n in enumerate(js["nodes"])]
    joint_names = [node_names[j] for j in skin["joints"]]

    # --- joint remap: body skeleton vs a "garment" whose joint order is shuffled
    rng = np.random.default_rng(1)
    perm = rng.permutation(len(joint_names))           # garment joint g is body joint perm[g]
    garment_names = [joint_names[p] for p in perm]
    body_index = {n: i for i, n in enumerate(joint_names)}
    remap = [body_index[n] for n in garment_names]
    assert remap == list(perm), "remap by name must undo the shuffle"
    assert len(set(remap)) == len(remap)
    try:
        body_index["no_such_joint"]
        raise SystemExit("FAIL missing joint not detected")
    except KeyError:
        pass
    print(f"remap: {len(remap)} joints shuffled and restored by name: PASS")

    total = {0: 0}
    results = {}
    for mesh in js["meshes"]:
        for prim in mesh["primitives"]:
            at = prim["attributes"]
            if "JOINTS_0" not in at:
                continue
            idx = accessor(js, bin_, prim["indices"])[:, 0].astype(np.int64)
            j = accessor(js, bin_, at["JOINTS_0"]).astype(np.int64)
            w = accessor(js, bin_, at["WEIGHTS_0"])
            dom = j[np.arange(len(j)), w.argmax(axis=1)]
            regions = [region_of(joint_names[d]) for d in dom]
            for label, names in [("none", []), ("chest|belly|upperarm_hi", ["chest", "belly", "upperarm_hi"]),
                                 ("trousers", ["hips", "thigh_hi", "thigh_lo", "calf_hi", "calf_lo"])]:
                m = mask_of(names)
                kept = filter_indices(idx.tolist(), regions, m)
                results.setdefault(label, [0, 0])
                results[label][0] += len(idx) // 3
                results[label][1] += len(kept) // 3
                # no triangle with a visible vertex may be dropped, and every all-hidden one must be
                expected = [idx[k:k + 3].tolist() for k in range(0, len(idx), 3)
                            if any(not (m >> regions[v]) & 1 for v in idx[k:k + 3])]
                assert [kept[k:k + 3] for k in range(0, len(kept), 3)] == expected, "filter differs from the visibility rule"
    for label, (before, after) in results.items():
        print(f"mask {label}: {before} -> {after} triangles ({before - after} dropped)")
    assert results["none"][0] == results["none"][1]
    assert results["chest|belly|upperarm_hi"][1] < results["none"][1]
    assert results["trousers"][1] < results["none"][1]
    print("filter: PASS")


if __name__ == "__main__":
    main()
