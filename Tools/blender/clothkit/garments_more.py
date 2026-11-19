"""The rest of the garment catalogue (S3): longsleeve, hoodie, work_jacket, hivis_vest, jeans, shorts, sneakers, cap, beanie,
hair_short, hair_buzz. `clothkit/catalog.py` imports this module last, so they register themselves.

Body frame (Blender, metres): z up, the figure faces -y, its left is +x. Torso front surface y = -0.12, back +0.11; the chest/neck
border is at z 1.45-1.52, the belly/hips border at 1.04, the head bone starts at 1.585 (scalp z 1.70-1.78).

Detail (cuff bands, collars, seams, pockets, belt loops, reflective bands) is added after the shell, with clothkit/detail.py.
"""
import numpy as np

from humankit import regions as RG
from . import detail as D
from .catalog import register
from .extras import sole
from .recipe import BoneCut, FitCut, Garment, Hem, PlaneCut, Zone, sided
from .shell import _append, add_tris

# --------------------------------------------------------------------------------------------- shared pieces
TRUNK_REGIONS = ["chest", "belly", "hips", "thigh_hi", "neck"]
HEM_CUT = lambda mm: FitCut(near=["belly"], far=["hips", "thigh_hi"], margin_mm=mm, remove=(0, 0, -1))
COLLAR_CUT = lambda mm: FitCut(near=["chest"], far=["neck"], margin_mm=mm, remove=(0, 0, 1))
WRIST_CUT = FitCut(near=["forearm"], far=["hand"], margin_mm=30, remove=(0.5, -0.3, -0.8))
FRONT = -0.03           # "the front half": y < FRONT


def trunk(hem_mm, collar_mm):
    """The torso from the collar (collar_mm above the chest/neck border) to a hem hem_mm below the belly/hips border."""
    return Zone(regions=TRUNK_REGIONS, cuts=[HEM_CUT(hem_mm), COLLAR_CUT(collar_mm)])


def long_sleeves():
    """Sleeves to 30 mm onto the hand (coverage: >= 25 mm past the forearm/hand border)."""
    return sided(Zone(regions=["upperarm_hi", "upperarm_lo", "forearm", "hand"], cuts=[WRIST_CUT]))


def edge_band(cut, width_mm, extra_mm, wall_mm=3.0, sides=False, only=None):
    """An extra: a band `width_mm` wide along the garment edge made by `cut` (a FitCut), `extra_mm` thicker (negative: thinner),
    with a chamfered inner edge. sides=True: one plane per side (sleeves, trouser legs). `only`: a field that restricts it."""
    def run(soup, body, garment):
        w = width_mm / 1000.0
        mk = lambda p, n: (lambda P: -((P - p) @ n) - w)
        if sides:
            f = D.by_side(mk(*cut.plane(body, "L")), mk(*cut.plane(body, "R")))
        else:
            f = mk(*cut.plane(body, None))
        D.feature(soup, f if only is None else D.convex(f, only), extra_mm, wall_mm)
    return run


def arms_only(P):
    return 0.25 - np.abs(P[:, 0])           # inside where |x| > 0.25: the sleeves, not the trunk


def run_all(*fns):
    def run(soup, body, garment):
        for fn in fns:
            fn(soup, body, garment)
    return run


def feat(f, amount_mm, wall_mm=2.0, material=None, refine_mm=0.0):
    """An extra: a raised / sunk area where the field f is negative."""
    def run(soup, body, garment):
        D.feature(soup, f, amount_mm, wall_mm, material=material, refine_mm=refine_mm)
    return run


# --------------------------------------------------------------------------------------------- longsleeve
register(Garment(
    id="longsleeve", slot="torso_base", layer=3, offset_mm=9,
    hides=["chest", "belly", "upperarm_hi", "upperarm_lo", "forearm"], materials=["cloth_tint"], wound="fabric", tri_budget=9000,
    zones=[trunk(80, 30), *long_sleeves()],
    offset_by_region={"belly": 11, "hips": 15, "thigh_hi": 15},
    extras=[edge_band(WRIST_CUT, 30, 2.5, sides=True, only=arms_only)],          # ribbed cuffs, 30 mm
    preview_colors={"cloth_tint": (0.55, 0.20, 0.20)},
))

# --------------------------------------------------------------------------------------------- hoodie
HOODIE_POCKET = D.polygon_xz([(-0.125, 1.065), (0.125, 1.065), (0.098, 1.185), (-0.098, 1.185)], y_max=FRONT)

register(Garment(
    id="hoodie", slot="torso_outer", layer=4, offset_mm=18,
    hides=["chest", "belly", "upperarm_hi", "upperarm_lo", "forearm"], materials=["knit_tint"], wound="fabric", tri_budget=12500,
    zones=[trunk(95, 42), *long_sleeves()],
    offset_by_region={"belly": 22, "hips": 22, "thigh_hi": 22},
    extras=[
        edge_band(WRIST_CUT, 30, 2.5, sides=True, only=arms_only),             # ribbed cuffs
        edge_band(HEM_CUT(95), 40, 2.5),                                       # ribbed waistband
        edge_band(COLLAR_CUT(42), 35, 8.0, wall_mm=6.0),                       # the rolled collar ring, 35 mm high
        feat(D.outline(HOODIE_POCKET, 0.0055), -2.5, 2.5, refine_mm=10.0),                     # kangaroo pocket: an inset seam
    ],
    preview_colors={"knit_tint": (0.30, 0.34, 0.30)},
))

# --------------------------------------------------------------------------------------------- work_jacket
JACKET_ZIP = D.convex(lambda P: np.abs(P[:, 0]) - 0.0025, lambda P: P[:, 1] - FRONT)
JACKET_POCKETS = D.either(D.polygon_xz([(0.055, 1.30), (0.135, 1.30), (0.135, 1.385), (0.055, 1.385)], y_max=FRONT),
                          D.polygon_xz([(-0.135, 1.30), (-0.055, 1.30), (-0.055, 1.385), (-0.135, 1.385)], y_max=FRONT))
JACKET_FLAPS = D.either(D.polygon_xz([(0.012, 1.46), (0.085, 1.56), (0.012, 1.56)], y_max=FRONT),
                        D.polygon_xz([(-0.012, 1.46), (-0.085, 1.56), (-0.012, 1.56)], y_max=FRONT))

register(Garment(
    id="work_jacket", slot="torso_outer", layer=4, offset_mm=18,
    hides=["chest", "belly", "upperarm_hi", "upperarm_lo", "forearm"], materials=["canvas_tint"], wound="fabric", tri_budget=11500,
    zones=[trunk(100, 40), *long_sleeves()],
    offset_by_region={"belly": 22, "hips": 22, "thigh_hi": 22},
    extras=[
        edge_band(WRIST_CUT, 30, 2.0, sides=True, only=arms_only),             # cuffs
        edge_band(COLLAR_CUT(40), 50, 6.0, wall_mm=5.0),                       # the stand collar, 50 mm
        feat(JACKET_FLAPS, 5.0, 3.0),                                          # collar flaps lying on the chest
        feat(JACKET_POCKETS, -2.5, 2.0),                                       # chest pockets: inset panels
        feat(JACKET_ZIP, -2.0, 1.2),                                           # the front zip line: a seam
    ],
    preview_colors={"canvas_tint": (0.40, 0.33, 0.20)},
))

# --------------------------------------------------------------------------------------------- hivis_vest
SHOULDER_CUT = FitCut(near=["chest"], far=["upperarm_hi"], margin_mm=27, remove=(0.9, 0, -0.3))


def band_z(z0, z1):
    """A horizontal band round the torso (below the armholes)."""
    zc, h = 0.5 * (z0 + z1), 0.5 * (z1 - z0)
    return lambda P: np.abs(P[:, 2] - zc) - h


register(Garment(
    id="hivis_vest", slot="torso_outer", layer=4, offset_mm=18,
    hides=["chest", "belly"], materials=["cloth_tint", "flat_reflective"], wound="fabric", tri_budget=7500,
    zones=[trunk(90, 48), *sided(Zone(regions=["upperarm_hi"], cuts=[SHOULDER_CUT]))],
    offset_by_region={"belly": 22, "hips": 22, "thigh_hi": 22, "upperarm_hi": 28},
    extras=[
        feat(band_z(1.05, 1.10), 1.5, 1.5, material=1),                        # two 50 mm reflective bands
        feat(band_z(1.19, 1.24), 1.5, 1.5, material=1),
    ],
    preview_colors={"cloth_tint": (0.85, 0.80, 0.02), "flat_reflective": (0.75, 0.75, 0.75)},
))

# --------------------------------------------------------------------------------------------- plate_carrier
CARRIER_FRONT_PLATE = D.polygon_xz([(-0.125, 1.17), (0.125, 1.17), (0.115, 1.42), (-0.115, 1.42)], y_max=FRONT)
CARRIER_BACK_PLATE = D.convex(D.polygon_xz([(-0.125, 1.17), (0.125, 1.17), (0.115, 1.42), (-0.115, 1.42)]), lambda P: FRONT - P[:, 1])
CARRIER_POUCHES = D.either(D.polygon_xz([(-0.135, 1.05), (-0.055, 1.05), (-0.055, 1.155), (-0.135, 1.155)], y_max=FRONT),
                           D.polygon_xz([(-0.04, 1.05), (0.04, 1.05), (0.04, 1.155), (-0.04, 1.155)], y_max=FRONT),
                           D.polygon_xz([(0.055, 1.05), (0.135, 1.05), (0.135, 1.155), (0.055, 1.155)], y_max=FRONT))

register(Garment(
    id="plate_carrier", slot="torso_outer", layer=4, offset_mm=20,
    hides=["chest", "belly"], materials=["canvas_tint", "flat_sole"], wound="hard", tri_budget=11500,
    zones=[trunk(90, 48), *sided(Zone(regions=["upperarm_hi"], cuts=[SHOULDER_CUT]))],
    offset_by_region={"belly": 24, "hips": 24, "thigh_hi": 24, "upperarm_hi": 28},
    extras=[
        feat(CARRIER_FRONT_PLATE, 9.0, 3.0, refine_mm=10.0),                   # the plates, front and back
        feat(CARRIER_BACK_PLATE, 9.0, 3.0, refine_mm=10.0),
        feat(CARRIER_POUCHES, 14.0, 3.0, material=1, refine_mm=10.0),          # three magazine pouches across the belly
    ],
    preview_colors={"canvas_tint": (0.16, 0.18, 0.13), "flat_sole": (0.05, 0.05, 0.05)},
))

# --------------------------------------------------------------------------------------------- jeans
JEANS_WAIST = FitCut(near=["hips", "thigh_hi"], far=["belly"], margin_mm=30, remove=(0, 0, 1))


def belt_loops(top_cut, loops, height_mm=45, width_mm=12, extra_mm=2.0):
    """Vertical belt loops hanging from the waist edge made by `top_cut`: (axis, position, where) with axis "x" (a strip at that x on
    the "front" or "back" of the body) or "y" (a strip at that y on both sides)."""
    def run(soup, body, garment):
        p, n = top_cut.plane(body, None)
        d = lambda P: (P - p) @ n                                   # <= 0 on the garment, 0 at its edge
        w = width_mm / 2000.0
        for axis, pos, where in loops:
            if axis == "x":
                strip = lambda P, pos=pos: np.abs(P[:, 0] - pos) - w
                half = (lambda P: P[:, 1] - FRONT) if where == "front" else (lambda P: 0.03 - P[:, 1])
            else:
                strip = lambda P, pos=pos: np.abs(P[:, 1] - pos) - w
                half = lambda P: 0.11 - np.abs(P[:, 0])
            f = D.convex(strip, half, lambda P: d(P) + 0.0025, lambda P: -d(P) - height_mm / 1000.0)
            D.feature(soup, f, extra_mm, 2.0, refine_mm=width_mm * 0.9)
    return run


register(Garment(
    id="jeans", slot="legs", layer=2, offset_mm=6,
    hides=["hips", "thigh_hi", "thigh_lo", "calf_hi", "calf_lo"], materials=["denim_tint"], wound="fabric", tri_budget=8500,
    zones=[
        Zone(regions=["belly", "hips", "thigh_hi", "thigh_lo", "calf_hi", "calf_lo"], cuts=[JEANS_WAIST]),
        *sided(Zone(regions=["calf_lo", "foot"], cuts=[FitCut(near=["calf_lo"], far=["foot"], margin_mm=30, remove=(0, 0, -1))])),
    ],
    offset_by_region={"calf_lo": 14, "foot": 24},                              # flares over the boot shafts (CONTRACTS section 2: 14 mm)
    extras=[
        edge_band(JEANS_WAIST, 35, 2.0, wall_mm=3.0),                          # the waistband
        belt_loops(JEANS_WAIST, [("x", -0.07, "front"), ("x", 0.07, "front"), ("y", 0.01, None), ("x", -0.075, "back"),
                                 ("x", 0.0, "back"), ("x", 0.075, "back")]),
    ],
    preview_colors={"denim_tint": (0.12, 0.20, 0.40)},
))

# --------------------------------------------------------------------------------------------- shorts
register(Garment(
    id="shorts", slot="legs", layer=2, offset_mm=6,
    hides=["hips", "thigh_hi"], materials=["canvas_tint"], wound="fabric", tri_budget=4500,
    zones=[
        Zone(regions=["belly", "hips", "thigh_hi"], cuts=[JEANS_WAIST]),
        *sided(Zone(regions=["thigh_hi", "thigh_lo"], cuts=[FitCut(near=["thigh_hi"], far=["thigh_lo"], margin_mm=30, remove=(0, 0, -1))])),
    ],
    offset_by_region={"thigh_lo": 12},                                         # the legs flare to the hem
    preview_colors={"canvas_tint": (0.45, 0.38, 0.26)},
))

# --------------------------------------------------------------------------------------------- sneakers
register(Garment(
    id="sneakers", slot="feet", layer=1, offset_mm=6,
    hides=["foot"], materials=["cloth_tint", "flat_sole_white"], wound="hard", tri_budget=3500,
    zones=[*sided(Zone(regions=["foot", "calf_lo"], cuts=[BoneCut("calf{s}", remove="head", mm_from_tail=38)]))],
    extras=[lambda soup, body, g: sole(soup, body, g, thickness_mm=20.0, heel_flare_mm=4.0, toe_flare_mm=3.0, material=1)],
    hem=Hem(roll_mm=3.0, tuck_mm=0.0),
    preview_colors={"cloth_tint": (0.80, 0.80, 0.80), "flat_sole_white": (0.85, 0.85, 0.85)},
))


# --------------------------------------------------------------------------------------------- head: cap, beanie, hair
def ears(body):
    """The ears, which stick out through hair and under caps: head vertices around the ear centre (humankit/regions.py)."""
    c = np.array(RG.EAR)
    p = np.column_stack([np.abs(body.co[:, 0]), body.co[:, 1], body.co[:, 2]])
    return (np.linalg.norm(p - c, axis=1) < RG.EAR_RADIUS) & (np.abs(body.co[:, 0]) > 0.06)


def rigid(bone="head"):
    """An extra: every vertex is weighted 1.0 to `bone` (a rigid cap)."""
    def run(soup, body, garment):
        soup.W[:] = 0
        soup.W[:, body.names.index(bone)] = 1.0
    return run


def peak(length_mm=70.0, droop=0.18, thickness_mm=2.5, rows=5, front_y=-0.025, half_width=0.074):
    """A cap peak: a curved sheet fanning out from the front of the crown's edge, `length_mm` long at the centre and tapering
    to the sides, drooping a little, with an underside `thickness_mm` below and a rim. Weights, region and normals from the
    nearest crown edge vertex."""
    def run(soup, body, garment):
        bd = soup.boundary()
        loops = _boundary_loops(bd)
        loop = max(loops, key=len)
        ring = np.array(loop)
        P = soup.P[ring]
        ctr = np.array([0.0, 0.012, P[:, 2].mean()])
        front = (P[:, 1] < front_y) & (np.abs(P[:, 0]) < half_width)
        order = np.argsort(P[front][:, 0])
        idx = ring[front][order]
        B = soup.P[idx]
        m = len(idx)
        u = np.clip(B[:, 0] / half_width, -1, 1)
        L = length_mm / 1000.0 * np.sqrt(np.clip(1 - u ** 2, 0, 1)) ** 1.2
        radial = B - ctr
        radial[:, 2] = 0
        radial /= np.linalg.norm(radial, axis=1, keepdims=True)
        fwd = 0.55 * radial + 0.45 * np.array([0, -1.0, 0])
        fwd[:, 2] = -droop
        fwd /= np.linalg.norm(fwd, axis=1, keepdims=True)
        th = thickness_mm / 1000.0
        top, bot = [], []
        for k in range(rows + 1):
            s = (k / rows)
            lift = 0.004 * np.sin(np.pi * s)                      # a slight crown to the sheet
            Q = B + fwd * (L * s)[:, None] + np.array([0, 0, 1.0]) * lift
            top.append(Q)
            bot.append(Q - np.array([0, 0, th]) - fwd * 0.0)
        top, bot = np.array(top), np.array(bot)                    # (rows+1, m, 3)
        flat = lambda a: a.reshape(-1, 3)
        src = np.repeat(idx[None, :], rows + 1, axis=0).reshape(-1)
        Wn, lab = soup.W[src], soup.label[src]
        nt = _append(soup, flat(top), np.tile([0, 0, 1.0], (flat(top).shape[0], 1)), Wn, lab, np.full(len(src), -1))
        nb = _append(soup, flat(bot), np.tile([0, 0, -1.0], (flat(bot).shape[0], 1)), Wn, lab, np.full(len(src), -1))
        ti = lambda k, i: nt + k * m + i
        bi = lambda k, i: nb + k * m + i
        T = []
        for k in range(rows):
            for i in range(m - 1):
                a, b, c, d = ti(k, i), ti(k, i + 1), ti(k + 1, i + 1), ti(k + 1, i)
                T += [(a, b, c), (a, c, d)]                          # the top sheet
                a, b, c, d = bi(k, i), bi(k, i + 1), bi(k + 1, i + 1), bi(k + 1, i)
                T += [(a, b, c), (a, c, d)]                          # the underside
        for i in range(m - 1):                                       # the rim and the root, so the peak is a closed piece
            T += [(ti(rows, i), ti(rows, i + 1), bi(rows, i + 1)), (ti(rows, i), bi(rows, i + 1), bi(rows, i))]
            T += [(ti(0, i), ti(0, i + 1), bi(0, i + 1)), (ti(0, i), bi(0, i + 1), bi(0, i))]
        for i in (0, m - 1):                                         # the two ends
            for k in range(rows):
                T += [(ti(k, i), ti(k + 1, i), bi(k + 1, i)), (ti(k, i), bi(k + 1, i), bi(k, i))]
        T = np.array(T)
        # orientation: the top sheet faces up, the underside down, and the rim, root and ends away from the piece's centre
        Pn = soup.P
        piece = np.unique(T)
        c0 = Pn[piece].mean(axis=0)
        for j, t in enumerate(T):
            n = np.cross(Pn[t[1]] - Pn[t[0]], Pn[t[2]] - Pn[t[0]])
            cen = Pn[t].mean(axis=0)
            if abs(n[2]) > 0.8 * np.linalg.norm(n):                  # a sheet triangle: top up, underside down
                top_t = (t >= nt).all() and (t < nb).all()
                ok = (n[2] > 0) == top_t
            else:                                                    # rim, root, ends: outward from the piece
                ok = (n[:2] @ (cen[:2] - c0[:2])) > 0
            if not ok:
                T[j] = t[[0, 2, 1]]
        UV = Pn[T][:, :, :2] / 0.05                                  # planar UVs at the cloth tile scale
        add_tris(soup, T, UV, 0, flat=False)
    return run


def _boundary_loops(bd):
    nxt = {}
    for u, v in bd:
        nxt.setdefault(int(u), []).append(int(v))
    seen, loops = set(), []
    for s in list(nxt):
        if s in seen:
            continue
        loop, v = [], s
        while v not in seen and v in nxt:
            seen.add(v)
            loop.append(v)
            v = nxt[v][0]
        loops.append(loop)
    return loops


register(Garment(
    id="cap", slot="head", layer=3, offset_mm=14,
    hides=["scalp"], materials=["cloth_tint"], wound="fabric", tri_budget=6500, coverage_mm=10.0,
    zones=[Zone(regions=["scalp"], grow_mm=20, exclude=ears)],
    extras=[peak(), rigid("head")], hem=Hem(roll_mm=3.0, tuck_mm=0.0),
    preview_colors={"cloth_tint": (0.55, 0.12, 0.12)},
))

BEANIE_CUT = PlaneCut(point=(0, -0.09, 1.696), normal=(0, -0.163, -1))
BEANIE_PLANE = FitCut  # (documentation: the brim band below is measured from BEANIE_CUT)


def beanie_brim(width_mm=40.0, extra_mm=4.0):
    """The rolled brim: a band `width_mm` above the lower edge, `extra_mm` thicker, with a rounded top edge."""
    def run(soup, body, garment):
        n = np.asarray(BEANIE_CUT.normal, float)
        n /= np.linalg.norm(n)
        p = np.asarray(BEANIE_CUT.point, float)
        f = lambda P: -((P - p) @ n) - width_mm / 1000.0
        D.feature(soup, f, extra_mm, 6.0)
    return run


register(Garment(
    id="beanie", slot="head", layer=3, offset_mm=14,
    hides=["scalp"], materials=["knit_tint"], wound="fabric", tri_budget=6800, coverage_mm=10.0,
    zones=[Zone(regions=["scalp", "head"], cuts=[BEANIE_CUT], exclude=ears)],
    extras=[beanie_brim(), rigid("head")], hem=Hem(roll_mm=3.0, tuck_mm=0.0),
    preview_colors={"knit_tint": (0.10, 0.10, 0.12)},
))


def eye_slot(body):
    """The balaclava's opening: a rounded slot across both eyes (humankit/anatomy.py has them at x +-0.032, z 1.6625)."""
    x, z = np.abs(body.co[:, 0]) / 0.058, (body.co[:, 2] - 1.6625) / 0.021
    return (x ** 4 + z ** 4 < 1.0) & (body.co[:, 1] < 0)


register(Garment(
    id="balaclava", slot="head", layer=3, offset_mm=5,
    hides=["scalp", "neck"], materials=["knit_tint"], wound="fabric", tri_budget=16000, coverage_mm=10.0,
    zones=[Zone(regions=["scalp", "head", "neck"], grow_mm=15, exclude=eye_slot)],
    hem=Hem(roll_mm=2.0, tuck_mm=0.0),
    preview_colors={"knit_tint": (0.10, 0.10, 0.12)},
))


def hairline_thinning(thick_mm=9.0, thin_mm=4.0, over_mm=15.0):
    """offset_fn: `thick_mm` over the scalp, thinning to `thin_mm` at the edge over `over_mm` (smoothstep)."""
    def fn(soup, body, mm):
        d = D.edge_distance(soup) / (over_mm / 1000.0)
        s = np.clip(d, 0, 1)
        s = s * s * (3 - 2 * s)
        return thin_mm + (thick_mm - thin_mm) * s
    return fn


register(Garment(
    id="hair_short", slot="hair", layer=1, offset_mm=9,
    hides=["scalp"], materials=["hair_tint"], wound="skin", tri_budget=4700, coverage_mm=10.0,
    zones=[Zone(regions=["scalp"], grow_mm=12, exclude=ears)],
    offset_fn=hairline_thinning(), hem=Hem(roll_mm=2.0, tuck_mm=0.0),
    preview_colors={"hair_tint": (0.05, 0.03, 0.02)},
))

def hair_fall(soup, body, garment, top=1.70, bottom=1.25, material=0):
    """Long hair: a closed band of hair hanging from under the scalp shell, round the back of the head from ear to ear, down
    behind the neck and over the shoulder blades. Each level is an arc (outer and inner surface); it rides on the head at the
    top, on the upper back at the bottom. The figure faces -y, so the back is +y."""
    nu, nv = 17, 15
    levels = np.linspace(top, bottom, 60)
    near = np.abs(body.co[:, 0]) < 0.07
    back = np.array([body.co[near & (np.abs(body.co[:, 2] - z) < 0.015), 1].max() for z in levels])
    clearance = np.interp(levels, [1.45, 1.66], [0.016, 0.005])
    back = np.maximum(back + clearance, back[0] - 0.02)     # hangs straight down behind the neck rather than following it in
    for _ in range(6):
        back[1:-1] = 0.25 * back[:-2] + 0.5 * back[1:-1] + 0.25 * back[2:]
    z_table, width_table, depth_table = ([1.25, 1.40, 1.50, 1.60, 1.70], [0.085, 0.118, 0.106, 0.102, 0.088], [0.02, 0.03, 0.05, 0.105, 0.125])

    def ring(v):
        out, inner = [], []
        for u in np.linspace(-1, 1, nu):
            a = u * np.radians(100)
            z = top + (bottom + 0.05 * abs(u) ** 2 - top) * v          # the ends curve up a little at the sides
            x_w = np.interp(z, z_table, width_table) * (1 - 0.35 * max(v - 0.88, 0) / 0.12)   # (narrows into the tips)
            d = np.interp(z, z_table, depth_table)
            y_b = np.interp(z, levels[::-1], back[::-1])
            out.append((x_w * np.sin(a), y_b - d * (1 - np.cos(a)), z))
            inner.append((x_w * 0.86 * np.sin(a), y_b - 0.016 - d * (1 - np.cos(a)), z))
        return np.array(out + inner[::-1])                              # one closed loop of 2 * nu points

    loops = np.array([ring(v) for v in np.linspace(0, 1, nv)])
    P = loops.reshape(-1, 3)
    groups = {n: i for i, n in enumerate(body.names)}
    W = np.zeros((len(P), soup.W.shape[1]))
    s = np.clip((P[:, 2] - 1.46) / 0.13, 0, 1)
    s = s * s * (3 - 2 * s)
    W[:, groups["head"]] = s
    W[:, groups["spine_03"]] = 1 - s
    N = np.zeros_like(P)
    N[:, 1] = 1.0
    start = _append(soup, P, N, W, np.full(len(P), RG.INDEX["scalp"]), np.full(len(P), -1))
    m = 2 * nu
    T = []
    for v in range(nv - 1):
        for k in range(m):
            a, b, c, d = start + v * m + k, start + v * m + (k + 1) % m, start + (v + 1) * m + (k + 1) % m, start + (v + 1) * m + k
            T += [(a, b, c), (a, c, d)]
    for level in (0, nv - 1):                                           # close the top and the bottom: the strip between outer and inner
        for k in range(nu - 1):
            a, b, c, d = start + level * m + k, start + level * m + k + 1, start + level * m + m - 2 - k, start + level * m + m - 1 - k
            T += [(a, b, c), (a, c, d)] if level else [(a, c, b), (a, d, c)]
    T = np.array(T)
    p = soup.P[T]
    if np.einsum("ij,ij->i", np.cross(p[:, 0], p[:, 1]), p[:, 2]).sum() < 0:   # a closed surface: outward means a positive volume
        T = T[:, [0, 2, 1]]
    add_tris(soup, T, np.zeros((len(T), 3, 2)), material)
    return soup


register(Garment(
    id="hair_long", slot="hair", layer=1, offset_mm=9,
    hides=["scalp"], materials=["hair_tint"], wound="skin", tri_budget=6000, coverage_mm=10.0,
    zones=[Zone(regions=["scalp"], grow_mm=12, exclude=ears)],
    offset_fn=hairline_thinning(), hem=Hem(roll_mm=2.0, tuck_mm=0.0), extras=[hair_fall],
    preview_colors={"hair_tint": (0.05, 0.03, 0.02)},
))

register(Garment(
    id="hair_buzz", slot="hair", layer=1, offset_mm=4,
    hides=["scalp"], materials=["hair_tint"], wound="skin", tri_budget=4700, coverage_mm=10.0,
    zones=[Zone(regions=["scalp"], grow_mm=12, exclude=ears)],
    hem=Hem(roll_mm=1.5, tuck_mm=0.0),
    preview_colors={"hair_tint": (0.05, 0.03, 0.02)},
))


# --------------------------------------------------------------------------------------------- helmet
# A commando's ballistic helmet: a hard shell standing well off the scalp, its rim just above the brow at the front and
# lower round the back of the skull. The game's armour check (NpcProfile.HelmetCovers) uses the same plane, so keep them together.
HELMET_CUT = PlaneCut(point=(0, -0.09, 1.705), normal=(0, -0.35, -1))


def helmet_rim(width_mm=14.0, extra_mm=5.0):
    """The rim: a band `width_mm` above the lower edge, `extra_mm` proud of the shell."""
    def run(soup, body, garment):
        n = np.asarray(HELMET_CUT.normal, float)
        n /= np.linalg.norm(n)
        p = np.asarray(HELMET_CUT.point, float)
        f = lambda P: -((P - p) @ n) - width_mm / 1000.0
        D.feature(soup, f, extra_mm, 4.0)
    return run


register(Garment(
    id="helmet", slot="head", layer=3, offset_mm=24,
    hides=["scalp"], materials=["shell_tint"], wound="hard", tri_budget=6800, coverage_mm=10.0,
    zones=[Zone(regions=["scalp", "head"], cuts=[HELMET_CUT], exclude=ears)],
    extras=[helmet_rim(), rigid("head")], hem=Hem(roll_mm=5.0, tuck_mm=0.0),
    preview_colors={"shell_tint": (0.16, 0.17, 0.13)},
))


# --------------------------------------------------------------------------------------------- hi-vis waterproofs (the ship's crew)
def ring_x(xc, half_mm):
    """A ring round each arm (out along x in the rest pose) centred |x| = xc, `half_mm` either side."""
    return lambda P: np.abs(np.abs(P[:, 0]) - xc) - half_mm / 1000.0


def trunk_only(P):
    return np.abs(P[:, 0]) - 0.22          # inside where |x| < 0.22: the body, not the sleeves


HIVIS_BANDS = D.either(D.convex(band_z(1.03, 1.08), trunk_only), D.convex(band_z(1.18, 1.23), trunk_only),
                       D.convex(ring_x(0.40, 25), arms_only), D.convex(ring_x(0.56, 25), arms_only))

register(Garment(
    id="hivis_jacket", slot="torso_outer", layer=4, offset_mm=20,
    hides=["chest", "belly", "upperarm_hi", "upperarm_lo", "forearm"], materials=["cloth_tint", "flat_reflective"], wound="fabric", tri_budget=13000,
    zones=[trunk(110, 45), *long_sleeves()],
    offset_by_region={"belly": 24, "hips": 24, "thigh_hi": 24},
    extras=[
        edge_band(WRIST_CUT, 30, 2.0, sides=True, only=arms_only),             # storm cuffs
        edge_band(COLLAR_CUT(45), 60, 6.0, wall_mm=5.0),                       # a high collar against the spray
        feat(JACKET_ZIP, -2.0, 1.2),                                           # the zip's storm flap: a seam
        feat(HIVIS_BANDS, 1.5, 1.5, material=1, refine_mm=12.0),               # 50 mm reflective tape: two hoops round the body, two round each arm
    ],
    preview_colors={"cloth_tint": (0.85, 0.80, 0.02), "flat_reflective": (0.75, 0.75, 0.75)},
))

TROUSER_WAIST = FitCut(near=["hips", "thigh_hi"], far=["belly"], margin_mm=30, remove=(0, 0, 1))
TROUSER_ANKLE = FitCut(near=["calf_lo"], far=["foot"], margin_mm=30, remove=(0, 0, -1))

register(Garment(
    id="hivis_trousers", slot="legs", layer=2, offset_mm=10,
    hides=["hips", "thigh_hi", "thigh_lo", "calf_hi", "calf_lo"], materials=["cloth_tint", "flat_black", "flat_reflective"], wound="fabric", tri_budget=9000,
    zones=[
        Zone(regions=["belly", "hips", "thigh_hi", "thigh_lo", "calf_hi", "calf_lo"], cuts=[TROUSER_WAIST]),
        *sided(Zone(regions=["calf_lo", "foot"], cuts=[TROUSER_ANKLE])),
    ],
    offset_by_region={"calf_lo": 16, "foot": 18},
    extras=[
        edge_band(TROUSER_WAIST, 40, 2.0, wall_mm=3.0),                        # the waistband
        feat(lambda P: P[:, 2] - 0.56, 1.0, 1.5, material=1, refine_mm=12.0),  # black from the knees down (they take the dirt)
        feat(D.either(band_z(0.30, 0.35), band_z(0.20, 0.25)), 1.5, 1.5, material=2, refine_mm=12.0),   # two reflective hoops round each shin
    ],
    preview_colors={"cloth_tint": (0.85, 0.80, 0.02), "flat_black": (0.035, 0.035, 0.04), "flat_reflective": (0.75, 0.75, 0.75)},
))


# --------------------------------------------------------------------------------------------- red arctic kit (the Arctic's commandos)
PARKA_YOKE = D.convex(lambda P: 1.37 - P[:, 2], lambda P: np.abs(P[:, 0]) - 0.34)    # black across the shoulders and the tops of the sleeves
PARKA_POCKETS = D.either(D.polygon_xz([(0.05, 1.02), (0.16, 1.02), (0.16, 1.14), (0.05, 1.14)], y_max=FRONT),
                         D.polygon_xz([(-0.16, 1.02), (-0.05, 1.02), (-0.05, 1.14), (-0.16, 1.14)], y_max=FRONT))

register(Garment(
    id="parka", slot="torso_outer", layer=4, offset_mm=30,
    hides=["chest", "belly", "upperarm_hi", "upperarm_lo", "forearm"], materials=["cloth_tint", "flat_black", "flat_reflective"], wound="fabric", tri_budget=14000,
    zones=[trunk(170, 55), *long_sleeves()],
    offset_by_region={"belly": 34, "hips": 36, "thigh_hi": 36, "forearm": 26},
    extras=[
        edge_band(WRIST_CUT, 40, 3.0, sides=True, only=arms_only),             # knit storm cuffs
        edge_band(HEM_CUT(170), 45, 3.0),                                      # the drawcord hem
        edge_band(COLLAR_CUT(55), 70, 8.0, wall_mm=6.0),                       # a deep collar, the hood's base
        feat(PARKA_YOKE, 1.5, 2.0, material=1, refine_mm=12.0),
        feat(D.convex(ring_x(0.45, 20), arms_only), 1.5, 1.5, material=2, refine_mm=12.0),   # a reflective band round each upper arm
        feat(PARKA_POCKETS, 4.0, 2.5),                                         # bellows pockets
        feat(JACKET_ZIP, -2.5, 1.5),                                           # the zip's storm flap
    ],
    preview_colors={"cloth_tint": (0.70, 0.03, 0.03), "flat_black": (0.035, 0.035, 0.04), "flat_reflective": (0.75, 0.75, 0.75)},
))

register(Garment(
    id="snow_trousers", slot="legs", layer=2, offset_mm=16,
    hides=["hips", "thigh_hi", "thigh_lo", "calf_hi", "calf_lo"], materials=["cloth_tint"], wound="fabric", tri_budget=8000,
    zones=[
        Zone(regions=["belly", "hips", "thigh_hi", "thigh_lo", "calf_hi", "calf_lo"], cuts=[TROUSER_WAIST]),
        *sided(Zone(regions=["calf_lo", "foot"], cuts=[TROUSER_ANKLE])),
    ],
    offset_by_region={"calf_lo": 24, "foot": 26},                              # insulated, flaring over the boots
    extras=[edge_band(TROUSER_WAIST, 45, 2.5, wall_mm=3.0)],
    preview_colors={"cloth_tint": (0.70, 0.03, 0.03)},
))

# The hood, up: round the head and neck, open at the face, with a deep fur ruff round the opening. The opening is a plane
# just in front of the ears, leaning back a little towards the chin; the hem a level cut round the neck, inside the parka's collar.
HOOD_FACE = PlaneCut(point=(0, -0.045, 1.63), normal=(0, -1.0, -0.12))
HOOD_HEM = PlaneCut.z(1.49, remove="below")


def hood_ruff(width_mm=45.0, extra_mm=10.0):
    """The ruff: a band `width_mm` back from the face opening, `extra_mm` proud of the hood, in fur."""
    def run(soup, body, garment):
        n = np.asarray(HOOD_FACE.normal, float)
        n /= np.linalg.norm(n)
        p = np.asarray(HOOD_FACE.point, float)
        f = lambda P: -((P - p) @ n) - width_mm / 1000.0
        D.feature(soup, D.convex(f, lambda P: 1.50 - P[:, 2]), extra_mm, 5.0, material=1, refine_mm=10.0)
    return run


register(Garment(
    id="parka_hood", slot="head", layer=3, offset_mm=40,
    hides=[], materials=["cloth_tint", "flat_fur"], wound="fabric", tri_budget=12000, coverage_mm=0.0,   # (the face cut opens the scalp's front: it stays drawn)
    zones=[Zone(regions=["scalp", "head", "neck"], cuts=[HOOD_FACE, HOOD_HEM])],
    extras=[hood_ruff()],
    hem=Hem(roll_mm=4.0, tuck_mm=0.0),
    preview_colors={"cloth_tint": (0.70, 0.03, 0.03), "flat_fur": (0.62, 0.53, 0.40)},
))
