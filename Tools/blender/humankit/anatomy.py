"""The base human (adult male, 1.78 m, A-pose) as a signed-distance model, plus the joint positions the rig uses.

Blender axes: +Z up, the figure faces -Y, its left side is +X. Units are metres. Everything is defined for the left
half (x >= 0) and the midline; the field is evaluated at (|x|, y, z), so the figure is exactly symmetric.
Proportions follow adult male anthropometry (head 0.235 m = 1/7.6 of the height, shoulder joints 0.36 m apart,
crotch at 0.47 H, knees at 0.28 H, fingertips at mid thigh when the arms hang).
"""
import math
import numpy as np
from .sdf import Ellipsoid, Cone, Loft, Hull, Plane, Torus, Group, nrm, frame

V = lambda *a: np.array(a, float)


def rot(v, axis, deg):
    """Rotate v about axis (Rodrigues)."""
    v = np.asarray(v, float)
    k = nrm(axis)
    a = math.radians(deg)
    return v * math.cos(a) + np.cross(k, v) * math.sin(a) + k * k.dot(v) * (1 - math.cos(a))


class Body:
    def __init__(self, root, joints):
        self.root = root
        self.joints = joints

    def eval(self, P, skip=()):
        Q = P.copy()
        Q[:, 0] = np.abs(Q[:, 0])
        return self.root.eval(Q, skip=skip)


def build():
    J = {}
    body = Group("body")

    # ------------------------------------------------------------------ trunk
    # (z, half width, front y, back y, front exponent, back exponent): the silhouette front and side, crotch to neck.
    trunk = [
        (0.795, 0.060, -0.040, 0.060, 2.0, 2.0),
        (0.835, 0.126, -0.068, 0.088, 2.2, 2.4),
        (0.885, 0.153, -0.082, 0.100, 2.3, 2.5),
        (0.940, 0.163, -0.090, 0.104, 2.3, 2.5),
        (1.000, 0.158, -0.100, 0.094, 2.2, 2.3),
        (1.060, 0.146, -0.104, 0.080, 2.1, 2.3),
        (1.120, 0.146, -0.106, 0.084, 2.1, 2.3),
        (1.180, 0.150, -0.108, 0.093, 2.2, 2.3),
        (1.240, 0.155, -0.108, 0.104, 2.3, 2.3),
        (1.300, 0.158, -0.106, 0.110, 2.4, 2.3),
        (1.360, 0.154, -0.099, 0.112, 2.4, 2.3),
        (1.410, 0.141, -0.086, 0.110, 2.4, 2.3),
        (1.450, 0.112, -0.062, 0.097, 2.3, 2.3),
        (1.480, 0.078, -0.047, 0.083, 2.2, 2.2),
        (1.505, 0.062, -0.040, 0.072, 2.0, 2.0),
    ]
    z0, z1 = trunk[0][0], trunk[-1][0]
    st = [((z - z0) / (z1 - z0), hw, yb - 0.5 * (yf + yb), 0.5 * (yb - yf), 0.0, 0.5 * (yf + yb), nb, nf)
          for z, hw, yf, yb, nf, nb in trunk]
    body.add(Loft(V(0, 0, z0), V(0, 0, z1), (1, 0, 0), st, cap_k=0.03), 0.0, "trunk")

    # chest: pectorals (fan from the sternum up to the armpit), shoulder girdle, clavicles
    pec_axis = nrm((0.85, 0.0, 0.42))
    body.add(Ellipsoid(V(0.068, -0.087, 1.324), (0.054, 0.019, 0.090), z_axis=pec_axis, x_hint=(0, 0, 1)), 0.06)
    gird = [V(0.035, -0.035, 1.470), V(0.035, 0.070, 1.500), V(0.045, 0.010, 1.540), V(0.045, 0.045, 1.535),
            V(0.185, -0.020, 1.462), V(0.185, 0.030, 1.462), V(0.150, -0.060, 1.395), V(0.150, 0.095, 1.395),
            V(0.035, -0.070, 1.420), V(0.035, 0.105, 1.420)]
    body.add(Hull(gird, round_k=0.03), 0.035, "girdle")
    body.add(Cone(V(0.026, -0.050, 1.466), V(0.160, -0.016, 1.474), 0.0065, 0.0075), 0.025)       # clavicle
    # abdomen: rectus abdominis either side of the linea alba, obliques over the hips
    for z, rz in ((1.176, 0.030), (1.118, 0.028), (1.060, 0.027), (0.990, 0.040)):
        yf = np.interp(z, [t[0] for t in trunk], [t[2] for t in trunk])
        body.add(Ellipsoid(V(0.034, yf + 0.0125, z), (0.030, 0.013, rz)), 0.026)
    body.add(Ellipsoid(V(0.116, -0.030, 1.035), (0.030, 0.058, 0.058), z_axis=(0.35, 0, 1)), 0.05)    # oblique
    # back: erector columns either side of the spine, shoulder blades, lats
    body.add(Cone(V(0.028, 0.062, 0.985), V(0.028, 0.086, 1.300), 0.030, 0.022, flat=0.75), 0.04)
    body.add(Ellipsoid(V(0.086, 0.094, 1.345), (0.056, 0.020, 0.075), z_axis=(-0.25, 0.1, 1), x_hint=(1, 0.3, 0)), 0.04)
    body.add(Ellipsoid(V(0.110, 0.042, 1.250), (0.036, 0.050, 0.110), z_axis=(0.25, 0, 1)), 0.05)     # lat

    # ------------------------------------------------------------------ neck
    neck = [(0.0, 0.060, 0.063, 0.0, 0.002, 2.0), (0.35, 0.056, 0.058, 0.0, -0.002, 2.0),
            (0.75, 0.053, 0.056, 0.0, 0.000, 2.0), (1.0, 0.054, 0.058, 0.0, 0.004, 2.0)]
    body.add(Loft(V(0, 0.024, 1.455), V(0, 0.010, 1.605), (1, 0, 0), neck, cap_k=0.02, t_range=(-0.1, 1.05)), 0.03)
    body.add(Ellipsoid(V(0, -0.035, 1.532), (0.009, 0.008, 0.012)), 0.02)                             # larynx
    body.add(Cone(V(0.050, 0.020, 1.612), V(0.018, -0.036, 1.474), 0.012, 0.0085), 0.02)             # SCM

    # ------------------------------------------------------------------ head
    head = body.child("head", 0.022)
    # (z, half width, front y, back y, front exponent, back exponent): skull and face as horizontal sections
    hs = [
        (1.540, 0.030, -0.066, 0.000, 2.0, 2.0),
        (1.552, 0.044, -0.080, 0.020, 1.8, 2.0),
        (1.568, 0.050, -0.087, 0.040, 1.85, 2.0),
        (1.590, 0.056, -0.089, 0.062, 1.95, 2.0),
        (1.615, 0.063, -0.089, 0.084, 2.1, 2.0),
        (1.640, 0.071, -0.087, 0.100, 2.1, 2.0),
        (1.665, 0.075, -0.085, 0.108, 2.25, 2.0),
        (1.690, 0.077, -0.090, 0.111, 2.3, 2.0),
        (1.720, 0.076, -0.086, 0.107, 2.2, 2.0),
        (1.750, 0.067, -0.067, 0.093, 2.1, 2.0),
        (1.770, 0.048, -0.042, 0.066, 2.0, 2.0),
        (1.781, 0.020, -0.012, 0.030, 2.0, 2.0),
    ]
    HY = 0.010
    z0, z1 = hs[0][0], hs[-1][0]
    st = [((z - z0) / (z1 - z0), hw, yb - HY, HY - yf, 0.0, 0.0, nb, nf) for z, hw, yf, yb, nf, nb in hs]
    head.add(Loft(V(0, HY, z0), V(0, HY, z1), (1, 0, 0), st, cap_k=0.012), 0.0, "skull")
    head.add(Ellipsoid(V(0.052, -0.063, 1.648), (0.018, 0.012, 0.012), z_axis=(0.3, 0, 1)), 0.03)     # cheekbone
    head.add(Ellipsoid(V(0.049, -0.004, 1.586), (0.009, 0.019, 0.022), z_axis=(0, 0.5, 1)), 0.03)     # jaw angle
    head.add(Ellipsoid(V(0, -0.082, 1.558), (0.017, 0.009, 0.011)), 0.015)                            # chin
    head.sub(Ellipsoid(V(0.080, -0.040, 1.700), (0.008, 0.020, 0.020)), 0.025)                        # temple
    # brow, eyes
    head.add(Ellipsoid(V(0.031, -0.085, 1.688), (0.022, 0.008, 0.007), z_axis=(0, 0, 1), x_hint=(1, 0.25, -0.1)), 0.02)
    EYE = V(0.032, -0.071, 1.6625)
    J["eye"] = EYE
    head.sub(Ellipsoid(EYE + V(0.0, -0.018, 0.002), (0.018, 0.014, 0.012)), 0.012)                   # orbit
    head.add(Ellipsoid(EYE, (0.0128, 0.0128, 0.0125)), 0.003, "eyeball")
    head.add(Ellipsoid(EYE + V(0.001, -0.004, 0.0082), (0.0145, 0.0098, 0.0045), z_axis=(0, 0.5, 1)), 0.004)   # upper lid
    head.add(Ellipsoid(EYE + V(0.001, -0.005, -0.0092), (0.013, 0.0078, 0.0035), z_axis=(0, -0.5, 1)), 0.004)  # lower lid
    # nose
    head.add(Cone(V(0, -0.088, 1.668), V(0, -0.109, 1.626), 0.0065, 0.0095, flat=0.95), 0.008)
    head.add(Ellipsoid(V(0, -0.106, 1.623), (0.0105, 0.011, 0.0105)), 0.006)
    head.add(Ellipsoid(V(0.0125, -0.097, 1.6195), (0.0075, 0.0095, 0.0075), z_axis=(0, 0.4, 1)), 0.009)
    head.sub(Ellipsoid(V(0.0075, -0.1005, 1.611), (0.0045, 0.0065, 0.0034), z_axis=(0, 0.3, 1)), 0.003)
    # mouth
    head.add(Ellipsoid(V(0, -0.0895, 1.5975), (0.023, 0.0075, 0.0058), z_axis=(0, 0.25, 1)), 0.012)   # upper lip
    head.add(Ellipsoid(V(0, -0.0875, 1.5840), (0.020, 0.0078, 0.0064), z_axis=(0, -0.3, 1)), 0.012)   # lower lip
    head.add(Ellipsoid(V(0.042, -0.055, 1.617), (0.016, 0.015, 0.021)), 0.04)                          # cheek
    head.add(Ellipsoid(V(0, -0.086, 1.690), (0.012, 0.007, 0.009)), 0.016)                            # glabella
    head.sub(Ellipsoid(V(0, -0.100, 1.5905), (0.024, 0.012, 0.0014)), 0.0025)                        # lip line
    J["head_top"] = V(0, 0.012, 1.780)
    J["chin"] = V(0, -0.085, 1.545)

    # ears (own group: blended into the head with a small radius only)
    ear = head.child("ear", 0.005)
    en = nrm((1.0, -0.30, 0.0))                 # the ear's outward normal (flares forward)
    eup = nrm((0.0, 0.22, 1.0))                 # leans back
    EAR = V(0.0805, 0.012, 1.653)
    ear.add(Ellipsoid(EAR, (0.0065, 0.018, 0.031), z_axis=eup, x_hint=en), 0.0)
    ear.add(Torus(EAR + en * 0.0035 + eup * 0.002, z_axis=en, x_hint=eup, R=0.0235, r=0.0036, a0=-2.3, a1=2.6,
                  flat=1.0), 0.004)
    ear.add(Ellipsoid(EAR - eup * 0.026 + V(0, -0.003, 0), (0.0045, 0.0085, 0.0090), z_axis=eup, x_hint=en), 0.004)
    ear.sub(Ellipsoid(EAR + en * 0.0072 - eup * 0.004 + V(0, -0.002, 0), (0.0045, 0.0095, 0.012), z_axis=eup, x_hint=en), 0.003)
    J["ear"] = EAR

    # ------------------------------------------------------------------ arm (A-pose: 45 degrees down, elbow bent 12)
    GH = V(0.180, 0.004, 1.425)
    u = nrm((0.7071, -0.045, -0.7071))
    E = GH + 0.295 * u
    fwd = V(0, -1, 0)
    fwd_perp = nrm(fwd - u * u.dot(fwd))
    f = nrm(u * math.cos(math.radians(12)) + fwd_perp * math.sin(math.radians(12)))
    W = E + 0.255 * f
    J.update(shoulder=GH, elbow=E, wrist=W, clav_in=V(0.020, -0.012, 1.455))
    arm = body.child("arm", 0.045)
    lat_u = nrm(np.cross(u, fwd_perp))          # in the coronal plane, pointing up-and-out from the arm
    if lat_u[2] < 0:
        lat_u = -lat_u
    # upper arm: local x anterior, local y towards the body
    ua = [(-0.08, 0.050, 0.052, 0.0, 0.0, 2.0), (0.15, 0.054, 0.050, 0.0, 0.0, 2.0),
          (0.45, 0.054, 0.048, 0.004, 0.0, 2.0), (0.75, 0.047, 0.043, 0.004, 0.0, 2.0),
          (0.95, 0.038, 0.044, -0.004, 0.0, 2.0), (1.05, 0.036, 0.043, -0.004, 0.0, 2.0)]
    arm.add(Loft(GH, E, fwd_perp, ua, cap_k=0.03, t_range=(-0.1, 1.08)), 0.0)
    arm.add(Ellipsoid(GH + 0.050 * u + 0.014 * lat_u - 0.004 * fwd_perp, (0.056, 0.050, 0.095), z_axis=u, x_hint=fwd_perp), 0.02, "deltoid")
    arm.add(Ellipsoid(GH + 0.012 * lat_u + V(0, 0, 0.012), (0.048, 0.052, 0.040), z_axis=lat_u, x_hint=fwd_perp), 0.03)   # shoulder cap
    arm.add(Ellipsoid(GH + 0.165 * u + 0.022 * fwd_perp, (0.030, 0.026, 0.075), z_axis=u, x_hint=lat_u), 0.016)    # biceps
    arm.add(Ellipsoid(GH + 0.125 * u - 0.022 * fwd_perp, (0.032, 0.026, 0.095), z_axis=u, x_hint=lat_u), 0.018)    # triceps
    arm.add(Ellipsoid(E - 0.024 * fwd_perp - 0.004 * u, (0.012, 0.011, 0.014)), 0.01)                          # olecranon
    # forearm: local x towards the thumb (forward), local y = f x thumb
    fa_thumb = nrm(fwd - f * f.dot(fwd))
    fa = [(-0.06, 0.043, 0.045, 0.0, 0.0, 2.0), (0.15, 0.046, 0.043, 0.004, 0.0, 2.0),
          (0.40, 0.040, 0.035, 0.002, 0.0, 2.0), (0.70, 0.033, 0.026, 0.0, 0.0, 2.1),
          (0.92, 0.030, 0.020, 0.0, 0.0, 2.2), (1.04, 0.029, 0.019, 0.0, 0.0, 2.2)]
    arm.add(Loft(E, W, fa_thumb, fa, cap_k=0.02, t_range=(-0.08, 1.05)), 0.02)
    arm.add(Ellipsoid(E + 0.07 * f + 0.022 * fa_thumb + 0.012 * lat_u, (0.020, 0.018, 0.065), z_axis=f, x_hint=fa_thumb), 0.016)  # brachioradialis

    # ------------------------------------------------------------------ hand (frame at the wrist)
    hx = f                                       # towards the fingers
    hz = nrm(lat_u - hx * hx.dot(lat_u))        # dorsal: the back of the hand faces up and out
    hy = np.cross(hz, hx)                        # thumb side
    if hy.dot(fwd) < 0:
        hy = -hy
    H = lambda x, y, z: W + hx * x + hy * y + hz * z
    J["hand_axes"] = (hx, hy, hz)
    hand = arm.child("hand", 0.014)
    # palm: sections from the wrist to the knuckles (local x across towards the thumb, local y dorsal)
    pl = [(-0.05, 0.026, 0.013, 0.014, 0.000, 0.0, 2.2, 2.2), (0.25, 0.032, 0.013, 0.017, 0.002, 0.0, 2.4, 2.2),
          (0.55, 0.037, 0.011, 0.015, 0.000, 0.0, 2.6, 2.3), (0.85, 0.040, 0.010, 0.012, -0.001, 0.0, 2.6, 2.3),
          (1.00, 0.040, 0.0095, 0.0105, -0.001, 0.0, 2.6, 2.3)]
    hand.add(Loft(W, W + hx * 0.090, hy, pl, cap_k=0.012, t_range=(-0.05, 1.0)), 0.0, "palm")
    hand.add(Ellipsoid(H(0.040, 0.019, -0.016), (0.018, 0.013, 0.030), z_axis=hx * 0.8 + hy * 0.6, x_hint=hy - hx), 0.012)  # thenar
    hand.add(Ellipsoid(H(0.055, 0.029, -0.003), (0.010, 0.009, 0.022), z_axis=hx, x_hint=hy), 0.012)                       # web
    hand.add(Ellipsoid(H(0.046, -0.029, -0.008), (0.011, 0.011, 0.034), z_axis=hx, x_hint=hy), 0.012)                     # hypothenar
    fingers = {  # MCP (x, y, z), spread (deg), (proximal, middle, distal), radius
        "index": ((0.087, 0.0300, 0.000), 6.0, (0.041, 0.024, 0.021), 0.0094),
        "middle": ((0.092, 0.0095, 0.001), 0.5, (0.045, 0.028, 0.022), 0.0097),
        "ring": ((0.087, -0.0110, 0.000), -5.5, (0.042, 0.027, 0.021), 0.0091),
        "pinky": ((0.078, -0.0305, -0.002), -12.0, (0.033, 0.020, 0.019), 0.0082),
    }
    curl = (10.0, 14.0, 8.0)
    J["fingers"] = {}
    for name, (mcp, spread, lens, r) in fingers.items():
        g = hand.child(name, 0.008)
        p = H(*mcp)
        d = rot(hx, hz, spread)
        pts = [p]
        rr = [r, r * 0.92, r * 0.86, r * 0.80]
        for i, L in enumerate(lens):
            d = rot(d, np.cross(d, hz), -curl[i])       # flex towards the palm
            q = p + d * L
            g.add(Cone(p, q, rr[i], rr[i + 1], flat=0.86, x_hint=hy), 0.0025)
            pts.append(q)
            p = q
        g.add(Ellipsoid(pts[0] + hz * 0.0035, (r * 0.95, r * 0.95, r * 0.95)), 0.006)     # knuckle
        J["fingers"][name] = pts
    # thumb: from the base of the palm, along the index side, pad facing the fingers, nail up and out
    g = hand.child("thumb", 0.016)
    pts = [H(0.020, 0.020, -0.009)]
    tw = nrm(hy * 0.7 + hz * 0.7)                 # nail direction
    for i, (dv, L, r0, r1) in enumerate(((V(0.86, 0.45, -0.25), 0.046, 0.0130, 0.0108),
                                         (V(0.93, 0.25, -0.27), 0.032, 0.0108, 0.0098),
                                         (V(0.95, 0.12, -0.28), 0.026, 0.0098, 0.0084))):
        d = nrm(hx * dv[0] + hy * dv[1] + hz * dv[2])
        q = pts[-1] + d * L
        g.add(Cone(pts[-1], q, r0, r1, flat=0.88 if i else 1.0, x_hint=np.cross(d, tw)), 0.003 if i else 0.0)
        pts.append(q)
    J["fingers"]["thumb"] = pts

    # ------------------------------------------------------------------ leg
    HJ = V(0.088, -0.004, 0.925)
    K = V(0.097, -0.012, 0.505)
    A = V(0.106, 0.024, 0.083)
    J.update(hip=HJ, knee=K, ankle=A)
    leg = body.child("leg", 0.04)
    th = [(-0.15, 0.084, 0.092, 0.006, 0.016, 2.0), (0.08, 0.088, 0.093, 0.006, 0.020, 2.0),
          (0.25, 0.086, 0.087, 0.008, 0.018, 2.0), (0.50, 0.078, 0.073, 0.010, 0.010, 2.0),
          (0.75, 0.064, 0.060, 0.006, 0.005, 2.0), (0.92, 0.053, 0.053, 0.002, 0.0, 2.0),
          (1.05, 0.050, 0.051, 0.0, 0.0, 2.0)]
    tfwd = nrm(fwd - nrm(K - HJ) * nrm(K - HJ).dot(fwd))
    leg.add(Loft(HJ, K, tfwd, th, cap_k=0.03, t_range=(-0.2, 1.08)), 0.0)
    tl_ = lambda t: HJ + (K - HJ) * t
    tmed = nrm(np.cross(nrm(K - HJ), tfwd))      # local y: towards the midline
    if tmed[0] > 0:
        tmed = -tmed
    leg.add(Ellipsoid(tl_(0.45) + 0.052 * tfwd, (0.034, 0.026, 0.150), z_axis=K - HJ, x_hint=-tmed), 0.03)   # rectus femoris
    leg.add(Ellipsoid(tl_(0.52) - 0.040 * tmed + 0.012 * tfwd, (0.030, 0.034, 0.160), z_axis=K - HJ, x_hint=-tmed), 0.03)   # vastus lat.
    leg.add(Ellipsoid(tl_(0.84) + 0.036 * tmed + 0.022 * tfwd, (0.028, 0.028, 0.070), z_axis=K - HJ, x_hint=-tmed), 0.02)   # vastus med.
    leg.add(Ellipsoid(tl_(0.40) - 0.048 * tfwd + 0.006 * tmed, (0.040, 0.030, 0.170), z_axis=K - HJ, x_hint=-tmed), 0.03)   # hamstrings
    leg.add(Ellipsoid(V(0.068, 0.062, 0.915), (0.066, 0.058, 0.076), z_axis=(0.25, 0.1, 1)), 0.035, "glute")
    leg.add(Ellipsoid(K + 0.044 * tfwd + V(0, 0, 0.012), (0.022, 0.012, 0.026), z_axis=(0, 0, 1), x_hint=(1, 0, 0)), 0.012)   # patella
    # lower leg
    cfwd = nrm(fwd - nrm(A - K) * nrm(A - K).dot(fwd))
    cmed = nrm(np.cross(nrm(A - K), cfwd))
    if cmed[0] > 0:
        cmed = -cmed
    cl = [(-0.06, 0.050, 0.052, 0.0, 0.0, 2.0), (0.08, 0.051, 0.050, -0.006, 0.0, 2.0),
          (0.26, 0.056, 0.054, -0.014, 0.004, 2.0), (0.46, 0.048, 0.045, -0.012, 0.003, 2.0),
          (0.70, 0.036, 0.032, -0.006, 0.0, 2.0), (0.88, 0.032, 0.030, -0.002, 0.0, 2.0),
          (1.04, 0.031, 0.032, 0.0, 0.0, 2.0)]
    leg.add(Loft(K, A, cfwd, cl, cap_k=0.02, t_range=(-0.08, 1.06)), 0.025)
    cl_ = lambda t: K + (A - K) * t
    leg.add(Ellipsoid(cl_(0.27) - 0.034 * cfwd + 0.019 * cmed, (0.027, 0.027, 0.100), z_axis=A - K, x_hint=cmed), 0.02)   # gastroc med.
    leg.add(Ellipsoid(cl_(0.24) - 0.031 * cfwd - 0.021 * cmed, (0.024, 0.024, 0.085), z_axis=A - K, x_hint=cmed), 0.02)   # gastroc lat.
    leg.add(Ellipsoid(cl_(0.30) + 0.030 * cfwd - 0.014 * cmed, (0.016, 0.016, 0.110), z_axis=A - K, x_hint=cmed), 0.018)  # tibialis
    leg.add(Cone(cl_(0.68) - 0.026 * cfwd, A - 0.046 * cfwd - V(0, 0, 0.035), 0.011, 0.009, flat=0.6, x_hint=cmed), 0.012)  # Achilles
    leg.add(Ellipsoid(A - 0.030 * cmed - V(0, 0, 0.012) - 0.004 * cfwd, (0.010, 0.011, 0.013)), 0.008)   # lateral malleolus
    leg.add(Ellipsoid(A + 0.026 * cmed + V(0, 0, 0.002), (0.010, 0.011, 0.012)), 0.008)                  # medial malleolus

    # ------------------------------------------------------------------ foot (toe-out ~6 degrees)
    foot = leg.child("foot", 0.018)
    ffwd = nrm((0.07, -1.0, 0.0))
    fmed = nrm(np.cross(ffwd, (0, 0, 1)))       # towards the midline
    if fmed[0] > 0:
        fmed = -fmed
    HB = V(A[0] - 0.004, A[1] + 0.058, 0.033)
    BALL = HB + ffwd * 0.198
    BALL[2] = 0.033
    J.update(heel=V(HB[0], HB[1] + 0.02, 0.0), ball=V(BALL[0], BALL[1], 0.024))
    fs = [(0.00, 0.033, 0.029, 0.000, 0.000, 2.0), (0.12, 0.038, 0.032, 0.006, 0.0, 2.2),
          (0.35, 0.046, 0.034, 0.013, 0.000, 2.4), (0.60, 0.037, 0.040, 0.007, 0.003, 2.6),
          (0.85, 0.025, 0.046, -0.002, 0.0, 2.8), (1.00, 0.020, 0.049, -0.008, -0.001, 2.8),
          (1.10, 0.017, 0.048, -0.010, 0.0, 2.6)]
    foot.add(Loft(HB, BALL, (0, 0, 1), fs, cap_k=0.025, t_range=(-0.02, 1.12)), 0.0)
    foot.sub(Ellipsoid(HB + ffwd * 0.10 + fmed * 0.034 + V(0, 0, -0.022), (0.016, 0.046, 0.016), z_axis=(0, 0, 1),
                       x_hint=fmed), 0.02)           # medial arch
    foot.inter(Plane(V(0, 0, 0.0), (0, 0, -1), center=V(0.1, -0.05, 0.0), radius=0.3), 0.004)   # flat sole
    toes = [(0.029, 0.0, (0.033, 0.027), 0.0128), (0.0080, 0.006, (0.030, 0.021), 0.0098),
            (-0.0105, -0.001, (0.028, 0.019), 0.0094), (-0.0275, -0.011, (0.025, 0.017), 0.0088),
            (-0.0430, -0.025, (0.021, 0.015), 0.0080)]
    J["toes"] = []
    big = foot.child("hallux", 0.008)
    small = foot.child("toes", 0.008)
    for i, (m, fw, lens, r) in enumerate(toes):
        g = big if i == 0 else small
        p = BALL + fmed * m + ffwd * (fw - 0.004) + V(0, 0, -0.008 + (0.004 if i == 0 else 0.0))
        d = nrm(ffwd + fmed * (0.04 if i == 0 else -0.03 * i) + V(0, 0, 0.02))
        pts = [p]
        for j, L in enumerate(lens):
            d = rot(d, np.cross(d, (0, 0, 1)), -16.0 if j else 4.0)
            q = p + d * L
            q[2] = max(q[2], r)
            g.add(Cone(p, q, r * (1.0 if j == 0 else 0.93), r * (0.93 if j == 0 else 0.86), flat=0.8,
                       x_hint=fmed), 0.003 if j else 0.0025)
            pts.append(q)
            p = q
        J["toes"].append(pts)
    return Body(body, J)
