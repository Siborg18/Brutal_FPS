"""Minimal glTF binary reader (numpy only), shared by the asset checks in this folder."""
import json
import struct

import numpy as np

CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


class Glb:
    def __init__(self, path):
        d = open(path, "rb").read()
        magic, ver, total = struct.unpack("<III", d[:12])
        assert magic == 0x46546C67, "not a GLB"
        off, self.js, self.bin = 12, None, b""
        while off < total:
            cl, ct = struct.unpack("<II", d[off:off + 8])
            off += 8
            if ct == 0x4E4F534A:
                self.js = json.loads(d[off:off + cl])
            elif ct == 0x004E4942:
                self.bin = d[off:off + cl]
            off += cl

    def accessor(self, i):
        a = self.js["accessors"][i]
        n = NC[a["type"]]
        dt = np.dtype(CT[a["componentType"]])
        cnt = a["count"]
        if "bufferView" not in a:
            return np.zeros((cnt, n), dt)
        bv = self.js["bufferViews"][a["bufferView"]]
        o = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        stride = bv.get("byteStride", n * dt.itemsize)
        if stride == n * dt.itemsize:
            return np.frombuffer(self.bin, dt, cnt * n, o).reshape(cnt, n).copy()
        out = np.empty((cnt, n), dt)
        for k in range(cnt):
            out[k] = np.frombuffer(self.bin, dt, n, o + k * stride)
        return out

    def primitives(self):
        for m in self.js["meshes"]:
            for p in m["primitives"]:
                yield m, p

    def attr(self, p, name):
        return self.accessor(p["attributes"][name])

    def joints(self):
        """Joint node names of the first skin, and its inverse bind matrices (row-major 4x4 as stored column-major)."""
        s = self.js["skins"][0]
        names = [self.js["nodes"][j]["name"] for j in s["joints"]]
        ibm = self.accessor(s["inverseBindMatrices"]).reshape(-1, 4, 4)
        return names, ibm

    def triangles(self):
        t = 0
        for m, p in self.primitives():
            t += self.js["accessors"][p["indices"]]["count"] // 3
        return t

    def vertices(self):
        return sum(self.js["accessors"][p["attributes"]["POSITION"]]["count"] for m, p in self.primitives())
