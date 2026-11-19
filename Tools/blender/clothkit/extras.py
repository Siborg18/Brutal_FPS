"""Extras: geometry a recipe adds to the shell (garment.extras = [fn(soup, body, garment), ...], run before the hem)."""
import numpy as np

from humankit import regions as RG
from .carve import log
from .shell import _append, add_tris


def _loops(edges):
    """Ordered vertex loops from directed edges (u, v). Branching vertices are not expected on these loops."""
    nxt = {int(u): int(v) for u, v in edges}
    seen, loops = set(), []
    for s in nxt:
        if s in seen:
            continue
        loop, v = [], s
        while v not in seen and v in nxt:
            seen.add(v)
            loop.append(v)
            v = nxt[v]
        if v == s and len(loop) >= 3:
            loops.append(loop)
    return loops


def _tri_pieces(tris, mask):
    """Piece id per triangle for the triangles in `mask`, connected through shared edges (others get -1)."""
    idx = np.where(mask)[0]
    parent = {int(i): int(i) for i in idx}

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x
    seen = {}
    for i in idx:
        t = tris[i]
        for k in range(3):
            a, b = int(t[k]), int(t[(k + 1) % 3])
            key = (a, b) if a < b else (b, a)
            if key in seen:
                parent[find(int(i))] = find(seen[key])
            else:
                seen[key] = int(i)
    out = np.full(len(tris), -1)
    for i in idx:
        out[i] = find(int(i))
    return out


def sole(soup, body, garment, thickness_mm=25.0, heel_flare_mm=5.0, toe_flare_mm=2.0, heel_y=-0.02,
         bottom_inset_mm=2.5, rim_smooth=12, material=1):
    """Replaces the underside of the foot shell by a sole slab: the rim of the opening goes straight down to a flat bottom
    `thickness_mm` below the foot's lowest point (the wall flares out by `toe_flare_mm`, and by `heel_flare_mm` behind y =
    `heel_y`: the heel step), the bottom edge is chamfered in, and a flat cap closes it. Sole triangles use `material`."""
    foot = body.labels == RG.INDEX["foot"]
    z_b = body.co[foot, 2].min() - thickness_mm / 1000.0
    a, b, c = (soup.P[soup.tris[:, i]] for i in range(3))
    fn = np.cross(b - a, c - a)
    fn /= np.maximum(np.linalg.norm(fn, axis=1, keepdims=True), 1e-12)
    cen = (a + b + c) / 3
    # the underside: foot triangles facing down at all, then the connected pieces of them that are big enough (one per foot).
    # Its border is the foot's outline seen from above (where the surface turns from facing down to facing up).
    lab = soup.label[soup.tris]
    under0 = (fn[:, 2] < -0.05) & (cen[:, 2] < 0.08) & (lab == RG.INDEX["foot"]).all(axis=1)
    pid = _tri_pieces(soup.tris, under0)
    ids, cnt = np.unique(pid[under0], return_counts=True)
    big = ids[cnt >= 60]
    under = under0 & np.isin(pid, big)
    if not under.any():
        raise RuntimeError("sole: no underside triangles found")
    # edges shared by an underside triangle and a kept one form the rim
    t3 = soup.tris
    e_all = np.stack([t3[:, [0, 1]], t3[:, [1, 2]], t3[:, [2, 0]]], axis=1).reshape(-1, 2)   # per triangle: its 3 directed edges
    tri_of = np.repeat(np.arange(len(soup.tris)), 3)
    key = lambda e: tuple(sorted(e))
    under_edges = {key(e) for e in e_all[under[tri_of]].tolist()}
    rim = [tuple(e) for e, t in zip(e_all.tolist(), tri_of) if not under[t] and key(e) in under_edges]
    keep = ~under
    soup.tris, soup.uv, soup.mat, soup.flat = soup.tris[keep], soup.uv[keep], soup.mat[keep], soup.flat[keep]
    loops = _loops(rim)
    log(f"{garment.id}: sole removes {int(under.sum())} underside triangles, {len(loops)} rim loop(s) "
        f"{[len(l) for l in loops]}, bottom at z {1000 * z_b:.0f} mm")
    n_shell = len(soup.P)
    for loop in loops:
        L = np.array(loop)
        P0 = soup.P[L]
        for _ in range(rim_smooth):          # the border of the underside zigzags in height by a triangle; smooth that along the loop
            z = P0[:, 2]
            P0[:, 2] = 0.5 * z + 0.25 * (np.roll(z, 1) + np.roll(z, -1))
        soup.P[L] = P0
        # the slab outline: the rim's own outline, but never inside the plan-view hull of the lower foot (the toes and the arch
        # leave notches in the rim; the slab fills them). Polar hull about the foot's centre, 72 bins.
        Q = soup.P[:n_shell]
        Q = Q[(np.sign(Q[:, 0]) == np.sign(P0[:, 0].mean())) & (Q[:, 2] < 0.06)][:, :2]
        ctr = Q.mean(axis=0)
        ang = lambda X: np.arctan2(X[:, 1] - ctr[1], X[:, 0] - ctr[0])
        rad = lambda X: np.linalg.norm(X - ctr, axis=1)
        nb = 72
        bins = ((ang(Q) + np.pi) / (2 * np.pi) * nb).astype(int) % nb
        hull = np.zeros(nb)
        np.maximum.at(hull, bins, rad(Q))
        for _ in range(3):
            hull = np.maximum(hull, 0.5 * (np.roll(hull, 1) + np.roll(hull, -1)))
        th = ang(P0[:, :2])
        r0 = rad(P0[:, :2])
        rh = hull[((th + np.pi) / (2 * np.pi) * nb).astype(int) % nb]
        r1 = np.maximum(r0, rh)
        for _ in range(4):                   # even out the outline along the loop, never back inside the hull
            r1 = np.maximum(rh, 0.5 * r1 + 0.25 * (np.roll(r1, 1) + np.roll(r1, -1)))
        out = np.stack([np.cos(th), np.sin(th)], axis=1)
        flare = np.where(P0[:, 1] > heel_y, heel_flare_mm, toe_flare_mm)[:, None] / 1000.0
        P0[:, :2] = ctr + out * r1[:, None]          # the rim itself moves out to the outline: the notches fill, the loop is star-shaped
        soup.P[L] = P0
        P1 = P0.copy()
        P1[:, :2] += out * flare
        P1[:, 2] = z_b + 0.008
        P2 = P1.copy()
        P2[:, :2] -= out * bottom_inset_mm / 1000.0
        P2[:, 2] = z_b
        m = len(L)
        zeros = np.zeros((m, 3))
        i1 = _append(soup, P1, soup.N[L], soup.W[L], soup.label[L], np.full(m, -1))
        i2 = _append(soup, P2, soup.N[L], soup.W[L], soup.label[L], np.full(m, -1))
        cen = np.array([P2[:, 0].mean(), P2[:, 1].mean()])
        cw = soup.W[L[int(np.argmin(np.linalg.norm(P2[:, :2] - cen, axis=1)))]]       # the nearest rim vertex's weights
        ci = _append(soup, np.array([[P2[:, 0].mean(), P2[:, 1].mean(), z_b]]), np.array([[0, 0, -1.0]]), cw[None],
                     np.array([RG.INDEX["foot"]]), np.array([-1]))
        T, flat = [], []
        for k in range(m):
            u, v = k, (k + 1) % m
            T += [(L[v], L[u], i1 + u), (L[v], i1 + u, i1 + v), (i1 + v, i1 + u, i2 + u), (i1 + v, i2 + u, i2 + v)]
            flat += [False] * 4
            cap = (ci, i2 + v, i2 + u)
            T.append(cap)
            flat.append(True)
        T = np.array(T)
        # the cap faces down; flip any triangle that does not
        Pn = soup.P
        for j in np.where(np.array(flat))[0]:
            t = T[j]
            n = np.cross(Pn[t[1]] - Pn[t[0]], Pn[t[2]] - Pn[t[0]])
            if n[2] > 0:
                T[j] = t[[0, 2, 1]]
        add_tris(soup, T, np.zeros((len(T), 3, 2)), material)
        soup.flat[-len(T):] = flat
    return soup
