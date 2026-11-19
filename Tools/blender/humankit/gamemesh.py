"""The game mesh: the body cut into regions that each get their own quad density (body 17 mm, face ~3.5 mm,
skull 6 mm, ears 3 mm, hands 6 mm, feet 10 mm), QuadriFlow-remeshed, bridged at the seams, mirrored, moved onto
the smooth surface, with the eyes opened and eyeballs added."""
import time

import numpy as np

from . import meshing as MS
from .meshing import log
from .anatomy import V

DENS = dict(body=0.017, head=0.0060, hand=0.0060, foot=0.010, ear=0.0032)
RES = dict(body=0.004, head=0.0018, hand=0.0015, foot=0.002, ear=0.0008)
DELTA, GAP = 0.02, 0.012



def seams(body):
    J = body.joints
    hx = J["hand_axes"][0]
    return dict(
        neck=MS.PlaneSeam(V(0, 0, 1.528), V(0, 0, 1.0), V(0, 0, 1.66), 0.20),
        wrist=MS.PlaneSeam(J["wrist"] - hx * 0.035, hx, J["wrist"] + hx * 0.08, 0.20),
        ankle=MS.PlaneSeam(V(0, 0, 0.150), V(0, 0, -1.0), V(0.11, -0.04, 0.05), 0.20),
        ear=MS.SkullSeam(body, 0.0022, J["ear"], 0.05),
    )



def build_game_mesh(body):
    J = body.joints
    S = seams(body)
    t0 = time.time()
    # body: everything below the neck seam, above the ankle seams, inside the wrist seams (both sides)
    reg_body = MS.Region(body, list(S.values()), -1, DELTA, fold=True)
    ob = MS.build_region("body", reg_body, (-0.80, -0.26, 0.10), (0.80, 0.22, 1.58), RES["body"], DENS["body"], True)
    reg_neck = MS.Region(body, [S["neck"]], +1, DELTA)
    reg_head = MS.Region(reg_neck, [S["ear"]], -1, 0.0015, fold=True)
    oh = MS.build_region("head", reg_head, (-0.12, -0.14, 1.49), (0.12, 0.14, 1.80), RES["head"], DENS["head"], True,
                         warp=MS.FaceWarp(S=1.8))
    reg_hand = MS.Region(body, [S["wrist"]], +1, DELTA)
    pts = np.array([J["wrist"]] + [p for f in J["fingers"].values() for p in f])
    lo, hi = pts.min(0) - 0.075, pts.max(0) + 0.075
    oha = MS.build_region("hand.L", reg_hand, lo, hi, RES["hand"], DENS["hand"], False)
    reg_ear = MS.Region(body, [S["ear"]], +1, 0.0015)
    E = J["ear"]
    oe = MS.build_region("ear.L", reg_ear, E - 0.045, E + 0.045, RES["ear"], DENS["ear"], False)
    reg_foot = MS.Region(body, [S["ankle"]], +1, DELTA)
    of = MS.build_region("foot.L", reg_foot, (0.02, -0.24, -0.01), (0.22, 0.14, 0.18), RES["foot"], DENS["foot"], False)
    log(f"regions built in {time.time() - t0:.1f}s")
    MS.trim(ob, reg_body, GAP / 2)
    MS.trim(oh, reg_neck, GAP / 2)
    MS.trim(oh, reg_head, 0.0008)
    MS.trim(oe, reg_ear, 0.0008)
    MS.trim(oha, reg_hand, GAP / 2)
    MS.trim(of, reg_foot, GAP / 2)
    for o in (ob, oh, oha, of, oe):
        MS.project_all(o, body, iters=4)
    ohr = MS.mirror_copy(oha, "hand.R")
    ofr = MS.mirror_copy(of, "foot.R")
    oer = MS.mirror_copy(oe, "ear.R")
    o = MS.join_objects([ob, oh, oha, ohr, of, ofr, oe, oer], "Human")
    MS.bridge_seams(o, S)
    MS.symmetrize(o)
    MS.project_all(o, body, iters=6)
    # relax the seam bands
    co = MS.get_co(o.data)
    Q = co.copy(); Q[:, 0] = np.abs(Q[:, 0])
    mask = np.zeros(len(co), bool)
    for k, sm in S.items():
        e, bb = sm.terms(Q)
        mask |= (np.abs(e) < (0.006 if k == "ear" else 0.035)) & (bb < 0)
    MS.relax(o, body, mask, iters=10)
    shape = MS.EyeShape(J["eye"])
    MS.cut_eyes(o, shape)
    eyes = []
    for s, nm in ((1, "Eye.L"), (-1, "Eye.R")):
        E = J["eye"] * np.array([s, 1, 1])
        g = shape.g * np.array([s, 1, 1])
        eyes.append(MS.eyeball(nm, E, g))
    me = o.data
    log(f"game mesh: {len(me.vertices)} verts, {len(me.polygons)} faces, "
        f"{sum(len(p.vertices) - 2 for p in me.polygons)} triangles; sizes {np.bincount([len(p.vertices) for p in me.polygons])}")
    return o
