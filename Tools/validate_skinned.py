#!/usr/bin/env python3
"""Validate the NPC skinned assets against Docs/npc_outfits_anim/CONTRACTS.md (sections 1, 2, 3 and 7).

Standard library + numpy only (no bpy). Covers human.glb, garments/*.glb + sidecars, anims/human_anims.glb,
anims/clips.json and garments/wardrobe.json.

Usage:
    python3 Tools/validate_skinned.py [--root <project root>] [--strict]

Output: one line per check, "PASS <check>: <detail>", "FAIL ...", "WARN ..." or "SKIP <check>: <reason>".
The last line is "ALL PASS" or "<n> FAILURES". Exit code 0 when there are no FAILs, otherwise 1.
Without --strict, a missing garment referenced by wardrobe.json is a WARN; with --strict it is a FAIL.
"""
import argparse
import glob
import json
import os
import struct
import sys

import numpy as np

# ---------------------------------------------------------------- contract constants (CONTRACTS.md)

REGIONS = ["head", "scalp", "neck", "chest", "belly", "hips", "upperarm_hi", "upperarm_lo", "forearm",
           "hand", "thigh_hi", "thigh_lo", "calf_hi", "calf_lo", "foot"]
REGION_SET = set(REGIONS)
HUMAN_JOINT_COUNT = 59
SLOTS = ["hair", "head", "torso_base", "torso_outer", "legs", "feet"]
WOUNDS = ["skin", "fabric", "hard"]
SIDECAR_KEYS = {"id", "slot", "layer", "offset_mm", "hides", "wound", "materials", "triangles"}

# slot -> (layer, (offset_mm min, offset_mm max)); CONTRACTS section 2 layer table.
LAYERS = {
    "hair": (1, (4, 9)),
    "feet": (1, (6, 6)),
    "legs": (2, (6, 6)),
    "torso_base": (3, (9, 9)),
    "head": (3, (14, 14)),
    "torso_outer": (4, (18, 18)),
}

# CONTRACTS section 2 garment catalogue: id -> slot, hides, materials.
CATALOGUE = {
    "tshirt": {"slot": "torso_base", "hides": ["chest", "belly", "upperarm_hi"], "materials": ["cloth_tint"]},
    "trousers_work": {"slot": "legs", "hides": ["hips", "thigh_hi", "thigh_lo", "calf_hi", "calf_lo"],
                      "materials": ["canvas_tint"]},
    "boots_work": {"slot": "feet", "hides": ["foot"], "materials": ["leather_tint", "flat_sole"]},
    "longsleeve": {"slot": "torso_base", "hides": ["chest", "belly", "upperarm_hi", "upperarm_lo", "forearm"],
                   "materials": ["cloth_tint"]},
    "hoodie": {"slot": "torso_outer", "hides": ["chest", "belly", "upperarm_hi", "upperarm_lo", "forearm"],
               "materials": ["knit_tint"]},
    "work_jacket": {"slot": "torso_outer", "hides": ["chest", "belly", "upperarm_hi", "upperarm_lo", "forearm"],
                    "materials": ["canvas_tint"]},
    "hivis_vest": {"slot": "torso_outer", "hides": ["chest", "belly"],
                   "materials": ["cloth_tint", "flat_reflective"]},
    "plate_carrier": {"slot": "torso_outer", "hides": ["chest", "belly"],
                      "materials": ["canvas_tint", "flat_sole"]},
    "jeans": {"slot": "legs", "hides": ["hips", "thigh_hi", "thigh_lo", "calf_hi", "calf_lo"],
              "materials": ["denim_tint"]},
    "shorts": {"slot": "legs", "hides": ["hips", "thigh_hi"], "materials": ["canvas_tint"]},
    "sneakers": {"slot": "feet", "hides": ["foot"], "materials": ["cloth_tint", "flat_sole_white"]},
    "cap": {"slot": "head", "hides": ["scalp"], "materials": ["cloth_tint"]},
    "beanie": {"slot": "head", "hides": ["scalp"], "materials": ["knit_tint"]},
    "balaclava": {"slot": "head", "hides": ["scalp", "neck"], "materials": ["knit_tint"]},
    "hair_short": {"slot": "hair", "hides": ["scalp"], "materials": ["hair_tint"]},
    "hair_buzz": {"slot": "hair", "hides": ["scalp"], "materials": ["hair_tint"]},
}

ANIM_NAMES = {"Run", "IdleLook", "IdleShift", "ShuffleTurn", "Cower", "DeathFwd", "DeathBack", "Talk"}

# CONTRACTS section 3 clip table: duration (s), loop, stride (m), tags, events [(name, time)].
CLIP_TABLE = {
    "Idle": dict(duration=3.2, loop=True, stride=0.0, tags=["idle"], events=[]),
    "Walk": dict(duration=1.0667, loop=True, stride=1.24, tags=["locomotion"],
                 events=[("footL", 0.0), ("footR", 0.5333)]),
    "Run": dict(duration=0.7333, loop=True, stride=2.70, tags=["locomotion"],
                events=[("footL", 0.0), ("footR", 0.3667)]),
    "IdleLook": dict(duration=5.0, loop=False, stride=0.0, tags=["idle", "variant"], events=[]),
    "IdleShift": dict(duration=4.0, loop=False, stride=0.0, tags=["idle", "variant"], events=[]),
    "ShuffleTurn": dict(duration=0.8, loop=False, stride=0.0, tags=["turn"],
                        events=[("footL", 0.1), ("footR", 0.4667)]),
    "Cower": dict(duration=2.0, loop=True, stride=0.0, tags=["fear"], events=[]),
    "DeathFwd": dict(duration=1.8, loop=False, stride=0.0, tags=["death"], events=[]),
    "DeathBack": dict(duration=1.8, loop=False, stride=0.0, tags=["death"], events=[]),
    "Talk": dict(duration=4.0, loop=True, stride=0.0, tags=["overlay", "talk"], events=[]),
}

NPC_NAMES = {"test subject", "patrol", "dock", "road", "pumps", "verandah", "car", "motel", "billboard"}

DURATION_TOL = 1.0 / 30.0 + 1e-4   # +-1/30 s, plus float slack
IBM_TOL = 1e-4
WEIGHT_SUM_TOL = 0.01
CLIP_TIME_TOL = 1e-4

# ---------------------------------------------------------------- glTF reading

_CTYPES = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
_NCOMP = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT2": 4, "MAT3": 9, "MAT4": 16}


def read_glb(path):
    """Returns (json_dict, bin_bytes) of a GLB file."""
    with open(path, "rb") as f:
        data = f.read()
    if len(data) < 12 or data[:4] != b"glTF":
        raise ValueError("not a GLB file")
    off = 12
    json_obj = None
    bin_bytes = b""
    while off + 8 <= len(data):
        clen, ctype = struct.unpack_from("<II", data, off)
        off += 8
        chunk = data[off:off + clen]
        off += clen
        if ctype == 0x4E4F534A:
            json_obj = json.loads(chunk.rstrip(b" \x00").decode("utf-8"))
        elif ctype == 0x004E4942:
            bin_bytes = chunk
    if json_obj is None:
        raise ValueError("no JSON chunk")
    return json_obj, bin_bytes


def read_accessor(gltf, binb, index):
    """Returns a float64 array of shape (count, components), honouring byteOffset, byteStride and normalized."""
    acc = gltf["accessors"][index]
    dtype = np.dtype(_CTYPES[acc["componentType"]]).newbyteorder("<")
    ncomp = _NCOMP[acc["type"]]
    count = acc["count"]
    if "bufferView" not in acc:
        return np.zeros((count, ncomp), dtype=np.float64)
    bv = gltf["bufferViews"][acc["bufferView"]]
    base = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = bv.get("byteStride", 0) or 0
    elem = ncomp * dtype.itemsize
    if count == 0:
        raw = np.zeros((0, ncomp), dtype=dtype)
    elif stride in (0, elem):
        raw = np.frombuffer(binb, dtype=dtype, count=count * ncomp, offset=base).reshape(count, ncomp)
    else:
        rows = np.ndarray(shape=(count, elem), dtype=np.uint8, buffer=binb, offset=base, strides=(stride, 1))
        raw = np.ascontiguousarray(rows).view(dtype).reshape(count, ncomp)
    vals = raw.astype(np.float64)
    if acc.get("normalized"):
        if dtype.kind == "u":
            vals = vals / float(np.iinfo(dtype).max)
        elif dtype.kind == "i":
            vals = np.maximum(vals / float(np.iinfo(dtype).max), -1.0)
    return vals


def node_names(gltf):
    return [n.get("name", "") for n in gltf.get("nodes", [])]


def skin_joint_names(gltf, skin_index=0):
    names = node_names(gltf)
    return [names[j] for j in gltf["skins"][skin_index]["joints"]]


def skinned_mesh_indices(gltf):
    return sorted({n["mesh"] for n in gltf.get("nodes", []) if "mesh" in n and "skin" in n})


def used_material_names(gltf):
    mats = gltf.get("materials", [])
    names = set()
    for mesh in gltf.get("meshes", []):
        for p in mesh.get("primitives", []):
            if "material" in p:
                names.add(mats[p["material"]].get("name", ""))
    return names


def primitive_triangles(gltf):
    total = 0
    for mesh in gltf.get("meshes", []):
        for p in mesh.get("primitives", []):
            if "indices" in p:
                total += gltf["accessors"][p["indices"]]["count"] // 3
            else:
                total += gltf["accessors"][p["attributes"]["POSITION"]]["count"] // 3
    return total


def animation_durations(gltf, binb):
    """Clip name -> duration in seconds (max of the sampler input times)."""
    out = {}
    for a in gltf.get("animations", []):
        d = 0.0
        for s in a.get("samplers", []):
            t = read_accessor(gltf, binb, s["input"])[:, 0]
            if t.size:
                d = max(d, float(t.max()))
        out[a.get("name", "")] = d
    return out


# ---------------------------------------------------------------- reporting

class Report:
    def __init__(self, strict):
        self.strict = strict
        self.fails = 0

    def add(self, status, check, detail):
        if status == "FAIL":
            self.fails += 1
        print(f"{status} {check}: {detail}")

    def skip(self, check, reason):
        print(f"SKIP {check}: {reason}")


class Loader:
    """Reads each GLB once; missing files and parse errors are reported by the caller."""

    def __init__(self, root):
        self.root = root
        self.cache = {}

    def path(self, rel):
        return os.path.join(self.root, rel)

    def exists(self, rel):
        return os.path.isfile(self.path(rel))

    def glb(self, rel):
        if rel not in self.cache:
            self.cache[rel] = read_glb(self.path(rel))
        return self.cache[rel]


# ---------------------------------------------------------------- shared weight check

def check_weights(rep, check, gltf, binb, meshes):
    if not meshes:
        rep.add("FAIL", check, "no skinned mesh")
        return
    n_vert = 0
    bad_sum = 0
    worst = 0.0
    too_many = 0
    missing = []
    for mi in meshes:
        for pi, p in enumerate(gltf["meshes"][mi]["primitives"]):
            attrs = p["attributes"]
            if "WEIGHTS_0" not in attrs:
                missing.append(f"mesh {mi} prim {pi}")
                continue
            w = read_accessor(gltf, binb, attrs["WEIGHTS_0"])
            sums = w.sum(axis=1)
            err = np.abs(sums - 1.0)
            n_vert += len(w)
            if err.size:
                worst = max(worst, float(err.max()))
            bad_sum += int(np.count_nonzero(err > WEIGHT_SUM_TOL))
            too_many += int(np.count_nonzero(np.count_nonzero(w > 0, axis=1) > 4))
            if "WEIGHTS_1" in attrs:
                w1 = read_accessor(gltf, binb, attrs["WEIGHTS_1"])
                too_many += int(np.count_nonzero(np.any(w1 > 0, axis=1)))
    if missing:
        rep.add("FAIL", check, f"no WEIGHTS_0 on {', '.join(missing[:5])}")
    elif bad_sum or too_many:
        rep.add("FAIL", check, f"{n_vert} vertices, {bad_sum} with weight sum off by more than 0.01 "
                               f"(worst {worst:.4f}), {too_many} with more than 4 influences")
    else:
        rep.add("PASS", check, f"{n_vert} vertices, weight sums within 0.01 of 1 (worst {worst:.2e}), "
                               f"at most 4 influences each")


# ---------------------------------------------------------------- human.glb (checks 1-4)

HUMAN = "Assets/Models/human.glb"


def check_human(rep, loader):
    rel = HUMAN
    if not loader.exists(rel):
        for name in ("human skin", "human regions", "human weights", "human clips"):
            rep.add("FAIL", name, f"{rel} missing")
        return None
    try:
        g, b = loader.glb(rel)
    except Exception as e:
        for name in ("human skin", "human regions", "human weights", "human clips"):
            rep.add("FAIL", name, f"cannot read {rel}: {e}")
        return None

    # 1. skin and joint count
    human = None
    try:
        skins = g.get("skins") or []
        if not skins:
            rep.add("FAIL", "human skin", "no skin")
        else:
            n = len(skins[0].get("joints", []))
            if n == HUMAN_JOINT_COUNT:
                rep.add("PASS", "human skin", f"{n} joints")
            else:
                rep.add("FAIL", "human skin", f"{n} joints, expected {HUMAN_JOINT_COUNT}")
            ibm = None
            if "inverseBindMatrices" in skins[0]:
                ibm = read_accessor(g, b, skins[0]["inverseBindMatrices"]).reshape(-1, 16)
            joints = skin_joint_names(g)
            human = {"joints": joints, "index": {nm: i for i, nm in enumerate(joints)}, "ibm": ibm,
                     "g": g, "b": b, "meshes": skinned_mesh_indices(g), "body": None}
    except Exception as e:
        rep.add("FAIL", "human skin", f"error: {e!r}")

    # 2. regions
    try:
        meshes = skinned_mesh_indices(g)
        if not meshes:
            rep.add("FAIL", "human regions", "no skinned mesh")
        else:
            counts = np.zeros(16, dtype=np.int64)
            problems = []
            for mi in meshes:
                for pi, p in enumerate(g["meshes"][mi]["primitives"]):
                    if "_REGION" not in p["attributes"]:
                        problems.append(f"mesh {mi} prim {pi} has no _REGION")
                        continue
                    v = read_accessor(g, b, p["attributes"]["_REGION"])[:, 0]
                    if v.size and np.any(np.abs(v - np.round(v)) >= 1e-3):
                        problems.append(f"mesh {mi} prim {pi} has non-integer _REGION values")
                        continue
                    ints = np.round(v).astype(np.int64)
                    if ints.size and (ints.min() < 0 or ints.max() > 14):
                        problems.append(f"mesh {mi} prim {pi} has _REGION outside 0-14")
                        continue
                    counts += np.bincount(ints, minlength=16)[:16]
            unused = [REGIONS[i] for i in range(len(REGIONS)) if counts[i] == 0]
            if unused:
                problems.append("regions never used: " + ", ".join(unused))
            per = ", ".join(f"{REGIONS[i]} {counts[i]}" for i in range(len(REGIONS)))
            if problems:
                rep.add("FAIL", "human regions", "; ".join(problems[:4]) +
                        (f" (+{len(problems) - 4} more)" if len(problems) > 4 else ""))
            else:
                rep.add("PASS", "human regions", per)
    except Exception as e:
        rep.add("FAIL", "human regions", f"error: {e!r}")

    # 3. weights
    try:
        check_weights(rep, "human weights", g, b, skinned_mesh_indices(g))
    except Exception as e:
        rep.add("FAIL", "human weights", f"error: {e!r}")

    # 4. clips
    try:
        names = {a.get("name", "") for a in g.get("animations", [])}
        missing = [n for n in ("Walk", "Idle") if n not in names]
        if missing:
            rep.add("FAIL", "human clips", f"missing {', '.join(missing)}")
        else:
            rep.add("PASS", "human clips", f"animations {', '.join(sorted(names))}")
    except Exception as e:
        rep.add("FAIL", "human clips", f"error: {e!r}")
    return human


# ---------------------------------------------------------------- garment regions and weight match

WEIGHT_MATCH_NEAR = 0.030     # m: garment vertices farther than this from the body are hems/soles, skipped
WEIGHT_MATCH_DIFF = 0.05      # max-weight difference that counts as a mismatch
WEIGHT_MATCH_WARN = 15.0      # % of mismatching vertices; above this is WARN (never FAIL, see check_weights_match)


def check_garment_regions(rep, pre, g, b):
    check = f"{pre} regions"
    meshes = skinned_mesh_indices(g)
    if not meshes:
        rep.add("FAIL", check, "no skinned mesh")
        return
    problems = []
    n_prims = 0
    n_verts = 0
    for mi in meshes:
        for pi, p in enumerate(g["meshes"][mi]["primitives"]):
            n_prims += 1
            if "_REGION" not in p["attributes"]:
                problems.append(f"mesh {mi} prim {pi} has no _REGION")
                continue
            v = read_accessor(g, b, p["attributes"]["_REGION"])[:, 0]
            n_verts += len(v)
            if v.size and np.any(np.abs(v - np.round(v)) >= 1e-3):
                problems.append(f"mesh {mi} prim {pi} has non-integer _REGION values")
            elif v.size and (np.round(v).min() < 0 or np.round(v).max() > 14):
                problems.append(f"mesh {mi} prim {pi} has _REGION outside 0-14")
    if problems:
        rep.add("FAIL", check, "; ".join(problems[:4]) +
                (f" (+{len(problems) - 4} more)" if len(problems) > 4 else ""))
    else:
        rep.add("PASS", check, f"{n_prims} primitives, every _REGION integer in 0-14 ({n_verts} vertices)")


def gather_vertices(gltf, binb, meshes, skin_joints, index_map, njoints, need_normals):
    """Positions, dense weights (vertex x njoints, columns = index_map of joint name) and optional normals.
    Joints absent from index_map are dropped. Returns (P, W, N_or_None, n_bad_joint_refs)."""
    col_of = np.array([index_map.get(nm, -1) for nm in skin_joints], dtype=np.int64)
    P, W, N = [], [], []
    bad = 0
    for mi in meshes:
        for p in gltf["meshes"][mi]["primitives"]:
            a = p["attributes"]
            if not all(k in a for k in ("POSITION", "JOINTS_0", "WEIGHTS_0")):
                raise ValueError(f"mesh {mi} primitive lacks POSITION, JOINTS_0 or WEIGHTS_0")
            pos = read_accessor(gltf, binb, a["POSITION"])
            jn = read_accessor(gltf, binb, a["JOINTS_0"]).astype(np.int64)
            wt = read_accessor(gltf, binb, a["WEIGHTS_0"])
            dense = np.zeros((len(pos), njoints))
            for k in range(4):
                ji = jn[:, k]
                in_range = (ji >= 0) & (ji < len(skin_joints))
                bad += int(np.count_nonzero(~in_range & (wt[:, k] > 0)))
                cols = col_of[np.clip(ji, 0, len(skin_joints) - 1)]
                ok = in_range & (cols >= 0) & (wt[:, k] > 0)
                rows = np.nonzero(ok)[0]
                np.add.at(dense, (rows, cols[ok]), wt[ok, k])
            P.append(pos)
            W.append(dense)
            if need_normals:
                if "NORMAL" not in a:
                    raise ValueError(f"mesh {mi} primitive has no NORMAL")
                N.append(read_accessor(gltf, binb, a["NORMAL"]))
    P = np.concatenate(P) if P else np.zeros((0, 3))
    W = np.concatenate(W) if W else np.zeros((0, njoints))
    N = (np.concatenate(N) if N else np.zeros((0, 3))) if need_normals else None
    return P, W, N, bad


def nearest_body(Q, B, chunk=256):
    """Chunked brute-force nearest neighbour. Returns (distance, index into B) for each row of Q."""
    bn = (B * B).sum(axis=1)
    dist = np.empty(len(Q))
    idx = np.empty(len(Q), dtype=np.int64)
    for s in range(0, len(Q), chunk):
        q = Q[s:s + chunk]
        d2 = (q * q).sum(axis=1)[:, None] + bn[None, :] - 2.0 * (q @ B.T)
        j = np.argmin(d2, axis=1)
        idx[s:s + chunk] = j
        dist[s:s + chunk] = np.sqrt(np.maximum(d2[np.arange(len(q)), j], 0.0))
    return dist, idx


def check_weights_match(rep, pre, g, b, offset_mm, human):
    check = f"{pre} weights match"
    if human is None:
        rep.skip(check, "human.glb missing or unreadable")
        return
    if not (isinstance(offset_mm, (int, float)) and not isinstance(offset_mm, bool)):
        rep.skip(check, "no numeric offset_mm in sidecar")
        return
    gmeshes = skinned_mesh_indices(g)
    if not gmeshes or not g.get("skins"):
        rep.add("FAIL", check, "no skinned mesh")
        return
    if human["body"] is None:
        P_b, W_b, _, _ = gather_vertices(human["g"], human["b"], human["meshes"], human["joints"],
                                         human["index"], len(human["joints"]), False)
        human["body"] = (P_b, W_b)
    P_b, W_b = human["body"]

    gjoints = skin_joint_names(g)
    P_g, W_g, N_g, bad = gather_vertices(g, b, gmeshes, gjoints, human["index"], len(human["joints"]), True)
    norm = np.linalg.norm(N_g, axis=1)
    unit = np.where(norm[:, None] > 0, N_g / np.where(norm > 0, norm, 1.0)[:, None], 0.0)
    Q = P_g - unit * (offset_mm / 1000.0)

    dist, idx = nearest_body(Q, P_b)
    close = dist <= WEIGHT_MATCH_NEAR
    n_close = int(np.count_nonzero(close))
    if n_close == 0:
        rep.add("FAIL", check, f"no garment vertex within 30 mm of the body ({len(Q)} garment vertices)")
        return
    diff = np.abs(W_g[close] - W_b[idx[close]]).max(axis=1)
    pct = 100.0 * np.count_nonzero(diff > WEIGHT_MATCH_DIFF) / n_close
    detail = (f"{pct:.2f}% of {n_close} vertices within 30 mm have a joint weight differing by more than 0.05 "
              f"from the body (offset {offset_mm} mm, {len(Q)} garment vertices")
    detail += f", {bad} joint refs dropped)" if bad else ")"
    detail += " (heuristic: nearest body vertex)"
    # Heuristic: the garment shell is smoothed and bridged across creases, so the nearest body vertex is often a
    # neighbour of the true source. Never FAIL.
    status = "PASS" if pct <= WEIGHT_MATCH_WARN else "WARN"
    rep.add(status, check, detail)


# ---------------------------------------------------------------- garments (checks 5-9, wardrobe refs)

def check_garment(rep, loader, path, human):
    gid = os.path.splitext(os.path.basename(path))[0]
    pre = f"garment {gid}"
    rel = os.path.relpath(path, loader.root)
    try:
        g, b = read_glb(path)
    except Exception as e:
        for suffix in ("skin", "bind", "weights", "sidecar", "catalogue"):
            rep.add("FAIL", f"{pre} {suffix}", f"cannot read {rel}: {e}")
        return

    # 5. skin: joint names all exist in human.glb
    gjoints = []
    try:
        if not g.get("skins"):
            rep.add("FAIL", f"{pre} skin", "no skin")
        else:
            gjoints = skin_joint_names(g)
            if human is None:
                rep.add("FAIL", f"{pre} skin", "cannot compare joints: human.glb missing or unreadable")
            else:
                missing = sorted({n for n in gjoints if n not in human["index"]})
                if missing:
                    rep.add("FAIL", f"{pre} skin", f"{len(missing)} joints not in human.glb: "
                                                   f"{', '.join(missing[:5])}")
                else:
                    rep.add("PASS", f"{pre} skin", f"{len(gjoints)} joints, all in human.glb")
    except Exception as e:
        rep.add("FAIL", f"{pre} skin", f"error: {e!r}")

    # 6. bind: inverse bind matrices equal the body's, per joint name
    try:
        if not g.get("skins") or human is None or human["ibm"] is None:
            rep.add("FAIL", f"{pre} bind", "cannot compare: no skin or no inverse bind matrices on one side")
        elif "inverseBindMatrices" not in g["skins"][0]:
            rep.add("FAIL", f"{pre} bind", "garment has no inverseBindMatrices")
        else:
            gibm = read_accessor(g, b, g["skins"][0]["inverseBindMatrices"]).reshape(-1, 16)
            if gibm.shape[0] != len(gjoints):
                rep.add("FAIL", f"{pre} bind", f"{gibm.shape[0]} matrices for {len(gjoints)} joints")
            else:
                worst_name, worst = "", -1.0
                n = 0
                for j, name in enumerate(gjoints):
                    if name not in human["index"]:
                        continue
                    d = float(np.max(np.abs(gibm[j] - human["ibm"][human["index"][name]])))
                    n += 1
                    if d > worst:
                        worst, worst_name = d, name
                if n == 0:
                    rep.add("FAIL", f"{pre} bind", "no joints to compare")
                else:
                    status = "PASS" if worst <= IBM_TOL else "FAIL"
                    rep.add(status, f"{pre} bind", f"worst joint '{worst_name}' max diff {worst:.2e} "
                                                   f"over {n} joints (tolerance 1e-4)")
    except Exception as e:
        rep.add("FAIL", f"{pre} bind", f"error: {e!r}")

    # 7. weights
    try:
        check_weights(rep, f"{pre} weights", g, b, skinned_mesh_indices(g))
    except Exception as e:
        rep.add("FAIL", f"{pre} weights", f"error: {e!r}")

    # 7b. regions: _REGION on every primitive, integers 0-14
    try:
        check_garment_regions(rep, pre, g, b)
    except Exception as e:
        rep.add("FAIL", f"{pre} regions", f"error: {e!r}")

    # 8. sidecar
    sidecar = None
    side_rel = f"Assets/Models/garments/{gid}.json"
    try:
        if not loader.exists(side_rel):
            rep.add("FAIL", f"{pre} sidecar", f"{side_rel} missing")
        else:
            with open(loader.path(side_rel), encoding="utf-8") as f:
                sidecar = json.load(f)
            problems = []
            if not isinstance(sidecar, dict):
                problems.append("sidecar is not a JSON object")
            else:
                keys = set(sidecar.keys())
                if keys != SIDECAR_KEYS:
                    problems.append(f"keys: missing {sorted(SIDECAR_KEYS - keys)}, "
                                    f"extra {sorted(keys - SIDECAR_KEYS)}")
                if sidecar.get("id") != gid:
                    problems.append(f"id {sidecar.get('id')!r} != file stem {gid!r}")
                slot = sidecar.get("slot")
                if slot not in SLOTS:
                    problems.append(f"slot {slot!r} not a valid slot")
                hides = sidecar.get("hides")
                if not isinstance(hides, list):
                    problems.append("hides is not a list")
                else:
                    bad = [h for h in hides if h not in REGION_SET]
                    if bad:
                        problems.append(f"hides not region names: {bad}")
                if sidecar.get("wound") not in WOUNDS:
                    problems.append(f"wound {sidecar.get('wound')!r} not in skin/fabric/hard")
                if slot in LAYERS:
                    layer_want, (lo, hi) = LAYERS[slot]
                    if gid == "hair_buzz":
                        lo = hi = 4
                    layer = sidecar.get("layer")
                    off = sidecar.get("offset_mm")
                    if not (isinstance(layer, int) and not isinstance(layer, bool) and layer == layer_want):
                        problems.append(f"layer {layer!r}, expected {layer_want} for slot {slot}")
                    if not (isinstance(off, (int, float)) and not isinstance(off, bool) and lo <= off <= hi):
                        problems.append(f"offset_mm {off!r} outside {lo}-{hi} for slot {slot}")
                mats = sidecar.get("materials")
                used = used_material_names(g)
                if not isinstance(mats, list) or set(mats) != used:
                    problems.append(f"materials {mats!r} != glb materials {sorted(used)}")
                tri = sidecar.get("triangles")
                real_tri = primitive_triangles(g)
                if tri != real_tri:
                    problems.append(f"triangles {tri!r} != glb {real_tri}")
            if problems:
                rep.add("FAIL", f"{pre} sidecar", "; ".join(problems))
            else:
                rep.add("PASS", f"{pre} sidecar", f"slot {sidecar['slot']}, layer {sidecar['layer']}, "
                                                  f"offset {sidecar['offset_mm']} mm, "
                                                  f"{len(sidecar['hides'])} hidden regions, "
                                                  f"wound {sidecar['wound']}, "
                                                  f"{sidecar['triangles']} triangles")
    except Exception as e:
        rep.add("FAIL", f"{pre} sidecar", f"cannot read {side_rel}: {e}")

    # 8b. weights match the body under the cloth (nearest body vertex after moving inward by offset_mm)
    try:
        offset = sidecar.get("offset_mm") if isinstance(sidecar, dict) else None
        check_weights_match(rep, pre, g, b, offset, human)
    except Exception as e:
        rep.add("FAIL", f"{pre} weights match", f"error: {e!r}")

    # 9. catalogue
    if sidecar is None or not isinstance(sidecar, dict):
        rep.skip(f"{pre} catalogue", "no sidecar to compare")
        return
    try:
        row = CATALOGUE.get(gid)
        if row is None:
            rep.add("FAIL", f"{pre} catalogue", f"id '{gid}' is not in the CONTRACTS section 2 catalogue")
            return
        problems = []
        if sidecar.get("slot") != row["slot"]:
            problems.append(f"slot {sidecar.get('slot')!r} != {row['slot']!r}")
        if not isinstance(sidecar.get("hides"), list) or set(sidecar["hides"]) != set(row["hides"]):
            problems.append(f"hides {sidecar.get('hides')} != {sorted(row['hides'])}")
        if not isinstance(sidecar.get("materials"), list) or set(sidecar["materials"]) != set(row["materials"]):
            problems.append(f"materials {sidecar.get('materials')} != {sorted(row['materials'])}")
        if problems:
            rep.add("FAIL", f"{pre} catalogue", "; ".join(problems))
        else:
            rep.add("PASS", f"{pre} catalogue", f"matches row for slot {row['slot']}")
    except Exception as e:
        rep.add("FAIL", f"{pre} catalogue", f"error: {e!r}")


def check_garments(rep, loader, human):
    paths = sorted(glob.glob(loader.path("Assets/Models/garments/*.glb")))
    if not paths:
        rep.skip("garments", "no garment .glb files in Assets/Models/garments")
        return
    for p in paths:
        try:
            check_garment(rep, loader, p, human)
        except Exception as e:
            gid = os.path.splitext(os.path.basename(p))[0]
            rep.add("FAIL", f"garment {gid} load", f"unexpected error: {e!r}")


# ---------------------------------------------------------------- human_anims.glb (checks 10-13)

ANIMS = "Assets/Models/anims/human_anims.glb"


def check_anims(rep, loader, human_names):
    if not loader.exists(ANIMS):
        for name in ("anims names", "anims targets", "anims interpolation", "anims durations"):
            rep.skip(name, f"{ANIMS} missing")
        return
    try:
        g, b = loader.glb(ANIMS)
    except Exception as e:
        for name in ("anims names", "anims targets", "anims interpolation", "anims durations"):
            rep.add("FAIL", name, f"cannot read {ANIMS}: {e}")
        return

    anims = g.get("animations", [])
    names = [a.get("name", "") for a in anims]

    # 10
    try:
        got = set(names)
        extra = sorted(got - ANIM_NAMES)
        lack = sorted(ANIM_NAMES - got)
        if len(names) != len(got) or extra or lack:
            rep.add("FAIL", "anims names", f"missing {lack}, extra {extra}, {len(names)} entries")
        else:
            rep.add("PASS", "anims names", f"exactly the 8 new clips: {', '.join(sorted(got))}")
    except Exception as e:
        rep.add("FAIL", "anims names", f"error: {e!r}")

    # 11
    try:
        nodes = node_names(g)
        hset = set(human_names)
        unknown = []
        n_ch = 0
        for a in anims:
            for ch in a.get("channels", []):
                n_ch += 1
                nm = nodes[ch["target"]["node"]]
                if nm not in hset:
                    unknown.append(f"{a.get('name')}:{nm}")
        if unknown:
            rep.add("FAIL", "anims targets", f"{len(unknown)} of {n_ch} channels target unknown nodes: "
                                             f"{', '.join(unknown[:5])}")
        else:
            rep.add("PASS", "anims targets", f"all {n_ch} channel targets exist in human.glb")
    except Exception as e:
        rep.add("FAIL", "anims targets", f"error: {e!r}")

    # 12
    try:
        bad = []
        n_s = 0
        for a in anims:
            for s in a.get("samplers", []):
                n_s += 1
                interp = s.get("interpolation", "LINEAR")
                if interp not in ("LINEAR", "STEP"):
                    bad.append(f"{a.get('name')}:{interp}")
        if bad:
            rep.add("FAIL", "anims interpolation", f"{len(bad)} samplers not LINEAR/STEP: {', '.join(bad[:5])}")
        else:
            rep.add("PASS", "anims interpolation", f"all {n_s} samplers LINEAR or STEP")
    except Exception as e:
        rep.add("FAIL", "anims interpolation", f"error: {e!r}")

    # 13
    try:
        durs = animation_durations(g, b)
        bad = []
        parts = []
        for nm in sorted(durs):
            if nm not in CLIP_TABLE:
                continue
            want = CLIP_TABLE[nm]["duration"]
            d = durs[nm]
            parts.append(f"{nm} {d:.4f}")
            if abs(d - want) > DURATION_TOL:
                bad.append(f"{nm} {d:.4f} vs {want}")
        if bad:
            rep.add("FAIL", "anims durations", "; ".join(bad))
        else:
            rep.add("PASS", "anims durations", ", ".join(parts))
    except Exception as e:
        rep.add("FAIL", "anims durations", f"error: {e!r}")


# ---------------------------------------------------------------- clips.json (checks 14-16)

CLIPS = "Assets/Models/anims/clips.json"


def _is_num(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def check_clips(rep, loader):
    if not loader.exists(CLIPS):
        for name in ("clips schema", "clips coverage", "clips values"):
            rep.skip(name, f"{CLIPS} missing")
        return
    try:
        with open(loader.path(CLIPS), encoding="utf-8") as f:
            data = json.load(f)
    except Exception as e:
        for name in ("clips schema", "clips coverage", "clips values"):
            rep.add("FAIL", name, f"cannot read {CLIPS}: {e}")
        return

    # 14
    try:
        problems = []
        if not isinstance(data, dict):
            problems.append("top level is not an object")
        else:
            if not (_is_num(data.get("fps")) and data.get("fps") == 30):
                problems.append(f"fps is {data.get('fps')!r}, expected 30")
            clips = data.get("clips")
            if not isinstance(clips, dict):
                problems.append("clips is not an object")
                clips = {}
            for nm, c in clips.items():
                if not isinstance(c, dict):
                    problems.append(f"{nm}: not an object")
                    continue
                if not isinstance(c.get("loop"), bool):
                    problems.append(f"{nm}: loop is not a bool")
                st = c.get("stride")
                if not (_is_num(st) and st >= 0):
                    problems.append(f"{nm}: stride {st!r} not a number >= 0")
                tags = c.get("tags")
                if not (isinstance(tags, list) and all(isinstance(t, str) for t in tags)):
                    problems.append(f"{nm}: tags not a list of strings")
                evs = c.get("events")
                if not isinstance(evs, list) or not all(
                        isinstance(e, dict) and isinstance(e.get("name"), str) and _is_num(e.get("time"))
                        for e in evs):
                    problems.append(f"{nm}: events not a list of {{name, time}}")
        if problems:
            rep.add("FAIL", "clips schema", "; ".join(problems[:6]))
        else:
            rep.add("PASS", "clips schema", f"fps 30, {len(data['clips'])} clips, all fields typed")
    except Exception as e:
        rep.add("FAIL", "clips schema", f"error: {e!r}")
        return

    clips = data.get("clips") if isinstance(data.get("clips"), dict) else {}

    # 15: coverage and event times
    try:
        anim_names = set()
        durs = {}
        for rel in ("Assets/Models/human.glb", ANIMS):
            if loader.exists(rel):
                g, b = loader.glb(rel)
                d = animation_durations(g, b)
                durs.update(d)
                anim_names |= set(d)
        if not anim_names:
            rep.skip("clips coverage", "no animation files found")
        else:
            got = set(clips)
            missing = sorted(anim_names - got)
            extra = sorted(got - anim_names)
            bad_ev = []
            for nm, c in clips.items():
                if nm not in durs or not isinstance(c, dict):
                    continue
                for e in c.get("events", []) if isinstance(c.get("events"), list) else []:
                    if isinstance(e, dict) and _is_num(e.get("time")) and e["time"] > durs[nm] + CLIP_TIME_TOL:
                        bad_ev.append(f"{nm}.{e.get('name')} at {e['time']} > {durs[nm]:.4f}")
            if missing or extra or bad_ev:
                parts = []
                if missing:
                    parts.append(f"missing {missing}")
                if extra:
                    parts.append(f"extra {extra}")
                if bad_ev:
                    parts.append("events past end: " + "; ".join(bad_ev[:4]))
                rep.add("FAIL", "clips coverage", ", ".join(parts))
            else:
                rep.add("PASS", "clips coverage", f"{len(got)} clips match the animations, "
                                                  f"all event times within their duration")
    except Exception as e:
        rep.add("FAIL", "clips coverage", f"error: {e!r}")

    # 16: values against the CONTRACTS table
    try:
        bad = []
        for nm, c in clips.items():
            if not isinstance(c, dict):
                continue
            if nm not in CLIP_TABLE:
                bad.append(f"{nm}: not in the CONTRACTS table")
                continue
            want = CLIP_TABLE[nm]
            if c.get("loop") != want["loop"]:
                bad.append(f"{nm}: loop {c.get('loop')} != {want['loop']}")
            st = c.get("stride")
            if not (_is_num(st) and abs(st - want["stride"]) <= 1e-6):
                bad.append(f"{nm}: stride {st} != {want['stride']}")
            if c.get("tags") != want["tags"]:
                bad.append(f"{nm}: tags {c.get('tags')} != {want['tags']}")
            evs = c.get("events") if isinstance(c.get("events"), list) else []
            got_ev = [(e.get("name"), float(e.get("time"))) for e in evs if isinstance(e, dict)
                      and _is_num(e.get("time"))]
            ok = len(got_ev) == len(want["events"]) and all(
                gn == wn and abs(gt - wt) <= CLIP_TIME_TOL
                for (gn, gt), (wn, wt) in zip(got_ev, want["events"]))
            if not ok:
                bad.append(f"{nm}: events {got_ev} != {want['events']}")
        if bad:
            rep.add("FAIL", "clips values", "; ".join(bad[:6]) + (f" (+{len(bad) - 6} more)" if len(bad) > 6 else ""))
        else:
            rep.add("PASS", "clips values", f"{len(clips)} clips match the CONTRACTS table")
    except Exception as e:
        rep.add("FAIL", "clips values", f"error: {e!r}")


# ---------------------------------------------------------------- wardrobe.json (checks 17-18)

WARDROBE = "Assets/Models/garments/wardrobe.json"


def _choices(archetype):
    slots = archetype.get("slots") if isinstance(archetype, dict) else None
    return slots if isinstance(slots, dict) else {}


def check_wardrobe(rep, loader):
    if not loader.exists(WARDROBE):
        rep.skip("wardrobe schema", f"{WARDROBE} missing")
        rep.skip("wardrobe refs", f"{WARDROBE} missing")
        return
    try:
        with open(loader.path(WARDROBE), encoding="utf-8") as f:
            wd = json.load(f)
    except Exception as e:
        rep.add("FAIL", "wardrobe schema", f"cannot read {WARDROBE}: {e}")
        rep.skip("wardrobe refs", "wardrobe.json unreadable")
        return

    # 17
    try:
        problems = []
        if not isinstance(wd, dict) or set(wd.keys()) != {"palettes", "archetypes", "npcs"}:
            keys = sorted(wd.keys()) if isinstance(wd, dict) else type(wd).__name__
            problems.append(f"top-level keys {keys}, expected palettes, archetypes, npcs")
            wd = wd if isinstance(wd, dict) else {}
        palettes = wd.get("palettes", {})
        if not isinstance(palettes, dict):
            problems.append("palettes is not an object")
            palettes = {}
        for pn, cols in palettes.items():
            if not isinstance(cols, list) or not cols:
                problems.append(f"palette {pn}: not a non-empty list")
                continue
            for col in cols:
                if not (isinstance(col, list) and len(col) == 3 and all(_is_num(v) and 0 <= v <= 1 for v in col)):
                    problems.append(f"palette {pn}: colour {col} is not 3 numbers in 0-1")
        archetypes = wd.get("archetypes", {})
        if not isinstance(archetypes, dict):
            problems.append("archetypes is not an object")
            archetypes = {}
        for an, arch in archetypes.items():
            if not isinstance(arch, dict):
                problems.append(f"archetype {an}: not an object")
                continue
            if not (_is_num(arch.get("weight")) and arch["weight"] > 0):
                problems.append(f"archetype {an}: weight must be a number > 0")
            slots = arch.get("slots")
            if not isinstance(slots, dict):
                problems.append(f"archetype {an}: slots is not an object")
                continue
            for sn, choices in slots.items():
                if sn not in SLOTS:
                    problems.append(f"archetype {an}: slot '{sn}' is not a valid slot")
                if not isinstance(choices, list):
                    problems.append(f"archetype {an}.{sn}: not a list")
                    continue
                for ch in choices:
                    if not isinstance(ch, dict):
                        problems.append(f"archetype {an}.{sn}: choice not an object")
                        continue
                    cid = ch.get("id")
                    if not (cid is None or isinstance(cid, str)):
                        problems.append(f"archetype {an}.{sn}: id must be a string or null")
                    if not (_is_num(ch.get("weight")) and ch["weight"] > 0):
                        problems.append(f"archetype {an}.{sn}: weight must be a number > 0")
                    if cid is not None and not isinstance(ch.get("palette"), str):
                        problems.append(f"archetype {an}.{sn}: palette required (string) when id is set")
        npcs = wd.get("npcs", {})
        if not isinstance(npcs, dict):
            problems.append("npcs is not an object")
            npcs = {}
        for nn, entry in npcs.items():
            if not (isinstance(entry, dict) and isinstance(entry.get("archetype"), str)):
                problems.append(f"npc {nn}: archetype must be a string")
        if problems:
            rep.add("FAIL", "wardrobe schema", "; ".join(problems[:6]) +
                    (f" (+{len(problems) - 6} more)" if len(problems) > 6 else ""))
        else:
            rep.add("PASS", "wardrobe schema", f"{len(palettes)} palettes, {len(archetypes)} archetypes, "
                                               f"{len(npcs)} npcs")
    except Exception as e:
        rep.add("FAIL", "wardrobe schema", f"error: {e!r}")
        rep.skip("wardrobe refs", "schema check raised an error")
        return

    # 18
    try:
        hard = []
        missing = []
        palettes = wd.get("palettes", {}) if isinstance(wd.get("palettes"), dict) else {}
        archetypes = wd.get("archetypes", {}) if isinstance(wd.get("archetypes"), dict) else {}
        npcs = wd.get("npcs", {}) if isinstance(wd.get("npcs"), dict) else {}
        for an, arch in archetypes.items():
            slots = _choices(arch)
            for sn, choices in slots.items():
                if not isinstance(choices, list):
                    continue
                for ch in choices:
                    if not isinstance(ch, dict):
                        continue
                    cid = ch.get("id")
                    if isinstance(ch.get("palette"), str) and ch["palette"] not in palettes:
                        hard.append(f"archetype {an}.{sn}: palette '{ch['palette']}' not defined")
                    if cid is None:
                        continue
                    side_rel = f"Assets/Models/garments/{cid}.json"
                    glb_rel = f"Assets/Models/garments/{cid}.glb"
                    if not loader.exists(side_rel) or not loader.exists(glb_rel):
                        absent = [r for r, ok in ((glb_rel, loader.exists(glb_rel)),
                                                  (side_rel, loader.exists(side_rel))) if not ok]
                        missing.append(f"{cid} ({', '.join(os.path.basename(a) for a in absent)})")
                        continue
                    try:
                        with open(loader.path(side_rel), encoding="utf-8") as f:
                            side = json.load(f)
                        if side.get("slot") != sn:
                            hard.append(f"{cid} listed under {sn} but its sidecar slot is {side.get('slot')!r}")
                    except Exception as e:
                        hard.append(f"{cid}: sidecar unreadable ({e})")
            for need in ("legs", "feet"):
                choices = slots.get(need)
                if not (isinstance(choices, list) and any(
                        isinstance(c, dict) and c.get("id") is not None for c in choices)):
                    hard.append(f"archetype {an} has no non-null option under {need}")
        for nn, entry in npcs.items():
            if not isinstance(entry, dict):
                continue
            if entry.get("archetype") not in archetypes:
                hard.append(f"npc {nn}: archetype '{entry.get('archetype')}' does not exist")
            if nn not in NPC_NAMES:
                hard.append(f"npc name '{nn}' is not one of the allowed names")
        if hard:
            detail = "; ".join(hard[:5])
            if missing:
                detail += f"; missing garments: {', '.join(sorted(set(missing)))}"
            rep.add("FAIL", "wardrobe refs", detail)
        elif missing:
            detail = f"missing garments: {', '.join(sorted(set(missing)))}"
            rep.add("FAIL" if rep.strict else "WARN", "wardrobe refs", detail)
        else:
            rep.add("PASS", "wardrobe refs", "all palettes, garments, slots, archetypes and npc names resolve")
    except Exception as e:
        rep.add("FAIL", "wardrobe refs", f"error: {e!r}")


# ---------------------------------------------------------------- main

def main(argv=None):
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description="Validate NPC skinned assets against CONTRACTS.md")
    ap.add_argument("--root", default=os.path.dirname(here), help="project root (default: parent of Tools/)")
    ap.add_argument("--strict", action="store_true", help="missing referenced garments are FAIL, not WARN")
    args = ap.parse_args(argv)

    root = os.path.abspath(args.root)
    rep = Report(args.strict)
    loader = Loader(root)

    try:
        human = check_human(rep, loader)
    except Exception as e:
        rep.add("FAIL", "human", f"unexpected error: {e!r}")
        human = None
    try:
        check_garments(rep, loader, human)
    except Exception as e:
        rep.add("FAIL", "garments", f"unexpected error: {e!r}")
    try:
        check_anims(rep, loader, human["joints"] if human else [])
    except Exception as e:
        rep.add("FAIL", "anims", f"unexpected error: {e!r}")
    try:
        check_clips(rep, loader)
    except Exception as e:
        rep.add("FAIL", "clips", f"unexpected error: {e!r}")
    try:
        check_wardrobe(rep, loader)
    except Exception as e:
        rep.add("FAIL", "wardrobe", f"unexpected error: {e!r}")

    if rep.fails == 0:
        print("ALL PASS")
        return 0
    print(f"{rep.fails} FAILURES")
    return 1


if __name__ == "__main__":
    sys.exit(main())
