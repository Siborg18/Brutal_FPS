"""UVs: the body's own UVs per vertex (carved with the surface), each island scaled uniformly so that 1 UV unit is one tile of
its texture set (CONTRACTS section 5 tile sizes; modelkit.TILE has the older ones)."""
import numpy as np

from .carve import log

# metres per repeat. denim/canvas/leather/knit/hair are the new sets (CONTRACTS section 5); the rest match modelkit.TILE.
TILE = {"cloth": 0.05, "fleece": 0.05, "skin": 0.08, "denim": 0.05, "canvas": 0.06, "leather": 0.08, "knit": 0.04,
        "hair": 0.03}


def set_of(material):
    n = material.split(".")[0]
    if n.endswith("_tint"):
        n = n[:-5]
    return n if n in TILE else None


def islands(soup):
    """Island id per triangle: triangles that share an edge and have the same UV at both of its ends."""
    T = len(soup.tris)
    parent = list(range(T))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x
    seen = {}
    for ti in range(T):
        t, uv = soup.tris[ti], soup.uv[ti]
        for k in range(3):
            a, b = int(t[k]), int(t[(k + 1) % 3])
            ua, ub = uv[k], uv[(k + 1) % 3]
            key = (a, b) if a < b else (b, a)
            ends = (ua, ub) if a < b else (ub, ua)
            for tj, (e0, e1) in seen.get(key, []):
                if np.abs(e0 - ends[0]).max() < 1e-6 and np.abs(e1 - ends[1]).max() < 1e-6:
                    parent[find(ti)] = find(tj)
            seen.setdefault(key, []).append((ti, ends))
    return np.array([find(i) for i in range(T)])


def rescale(soup, garment):
    """Scales each island by sqrt(3D area / UV area) / tile so that texels are the real size. Call before any extras
    (hem, sole) are added."""
    iso = islands(soup)
    a, b, c = (soup.P[soup.tris[:, i]] for i in range(3))
    A3 = 0.5 * np.linalg.norm(np.cross(b - a, c - a), axis=1)
    u0, u1, u2 = soup.uv[:, 0], soup.uv[:, 1], soup.uv[:, 2]
    Auv = 0.5 * np.abs((u1[:, 0] - u0[:, 0]) * (u2[:, 1] - u0[:, 1]) - (u2[:, 0] - u0[:, 0]) * (u1[:, 1] - u0[:, 1]))
    ks = []
    for isl in np.unique(iso):
        m = iso == isl
        mats = np.bincount(soup.mat[m]).argmax()
        name = garment.materials[mats]
        ts = garment.tile or set_of(name)
        if ts is None or Auv[m].sum() < 1e-12:
            continue
        k = np.sqrt(A3[m].sum() / Auv[m].sum()) / TILE[ts]
        c0 = soup.uv[m].reshape(-1, 2).mean(axis=0)
        soup.uv[m] = c0 + (soup.uv[m] - c0) * k
        ks.append(k)
    if ks:
        log(f"{garment.id}: {len(np.unique(iso))} UV islands, scale {min(ks):.1f}-{max(ks):.1f} (1 UV unit = tile)")
    return iso
