"""Minimal glTF/GLB reader for the animation checks (numpy only): nodes, animations (sampled channels), skin and mesh."""
import json
import struct

import numpy as np

_COMP = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
_NCOMP = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


class Glb:
    def __init__(self, path):
        d = open(path, "rb").read()
        assert d[:4] == b"glTF", "not a GLB"
        off = 12
        self.json, self.bin = None, b""
        while off < len(d):
            ln, typ = struct.unpack("<II", d[off:off + 8])
            chunk = d[off + 8:off + 8 + ln]
            if typ == 0x4E4F534A:
                self.json = json.loads(chunk)
            elif typ == 0x004E4942:
                self.bin = chunk
            off += 8 + ln
        self.nodes = self.json["nodes"]
        self.parent = {}
        for i, n in enumerate(self.nodes):
            for c in n.get("children", []):
                self.parent[c] = i
        self.by_name = {n["name"]: i for i, n in enumerate(self.nodes)}

    def accessor(self, idx):
        a = self.json["accessors"][idx]
        bv = self.json["bufferViews"][a["bufferView"]]
        dt = np.dtype(_COMP[a["componentType"]])
        nc = _NCOMP[a["type"]]
        start = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        stride = bv.get("byteStride", 0)
        cnt = a["count"]
        if stride and stride != dt.itemsize * nc:
            raw = np.frombuffer(self.bin, dtype=np.uint8, count=stride * cnt, offset=start).reshape(cnt, stride)
            arr = raw[:, :dt.itemsize * nc].copy().view(dt).reshape(cnt, nc)
        else:
            arr = np.frombuffer(self.bin, dtype=dt, count=cnt * nc, offset=start).reshape(cnt, nc)
        if a.get("normalized"):
            info = np.iinfo(dt)
            arr = np.maximum(arr.astype(np.float32) / info.max, -1.0)
        return arr[:, 0] if nc == 1 else arr

    def animations(self):
        """{name: {node_name: {path: (times, values, interpolation)}}}"""
        out = {}
        for a in self.json.get("animations", []):
            ch = {}
            for c in a["channels"]:
                s = a["samplers"][c["sampler"]]
                nm = self.nodes[c["target"]["node"]]["name"]
                ch.setdefault(nm, {})[c["target"]["path"]] = (
                    self.accessor(s["input"]).astype(np.float64), self.accessor(s["output"]).astype(np.float64),
                    s.get("interpolation", "LINEAR"))
            out[a["name"]] = ch
        return out

    def duration(self, anim):
        return max(float(t[-1]) for per in anim.values() for (t, v, i) in per.values())


def sample(times, values, t, path):
    """Linear (slerp-free nlerp for rotations) sample of a channel at time t."""
    if len(times) == 1:
        return values[0]
    t = min(max(t, times[0]), times[-1])
    i = int(np.searchsorted(times, t, side="right") - 1)
    i = min(i, len(times) - 2)
    u = (t - times[i]) / (times[i + 1] - times[i]) if times[i + 1] > times[i] else 0.0
    a, b = values[i], values[i + 1]
    if path == "rotation":
        if np.dot(a, b) < 0:
            b = -b
        q = a * (1 - u) + b * u
        return q / np.linalg.norm(q)
    return a * (1 - u) + b * u


def quat_mat(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def quat_angle_deg(a, b):
    d = abs(float(np.dot(a, b) / (np.linalg.norm(a) * np.linalg.norm(b))))
    return float(np.degrees(2 * np.arccos(min(1.0, d))))


class Rig:
    """Forward kinematics of the glTF node tree driven by an animation's channels."""

    def __init__(self, glb):
        self.g = glb
        self.n = len(glb.nodes)
        # parents before children
        order, seen = [], set()

        def visit(i):
            if i in seen:
                return
            if i in glb.parent:
                visit(glb.parent[i])
            seen.add(i)
            order.append(i)
        for i in range(self.n):
            visit(i)
        self.order = order

    def local(self, i, anim, t):
        n = self.g.nodes[i]
        tr = np.array(n.get("translation", [0, 0, 0]), dtype=np.float64)
        q = np.array(n.get("rotation", [0, 0, 0, 1]), dtype=np.float64)
        sc = np.array(n.get("scale", [1, 1, 1]), dtype=np.float64)
        ch = anim.get(n["name"]) if anim else None
        if ch:
            if "translation" in ch:
                tr = sample(*ch["translation"][:2], t, "translation")
            if "rotation" in ch:
                q = sample(*ch["rotation"][:2], t, "rotation")
            if "scale" in ch:
                sc = sample(*ch["scale"][:2], t, "scale")
        return tr, q, sc

    def world(self, anim, t):
        W = {}
        for i in self.order:
            tr, q, sc = self.local(i, anim, t)
            M = np.eye(4)
            M[:3, :3] = quat_mat(q) * sc
            M[:3, 3] = tr
            p = self.g.parent.get(i)
            W[i] = W[p] @ M if p is not None else M
        return W
