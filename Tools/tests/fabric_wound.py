"""Numpy port of Renderer.cs applyClothWounds (fabric style) rendered on a flat grey cloth patch.

Five tiles in a 512 x 512 PNG: entry wound at ages 0, 5, 20 and 90 s, then an exit wound at 20 s.
The patch faces +Z in bind space and game up is +Y, so the soak runs towards the bottom of the image.
Usage: python3 fabric_wound.py [out.png]
"""
import sys
import numpy as np


def fract(x):
    return x - np.floor(x)


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def mix(a, b, t):
    return a + (b - a) * t


def hash13(p):
    p = fract(p * 0.1031)
    p = p + np.sum(p * (p[..., [2, 1, 0]] + 31.32), -1, keepdims=True)
    return fract((p[..., 0] + p[..., 1]) * p[..., 2])


def vnoise(p):
    i = np.floor(p)
    f = fract(p)
    f = f * f * (3 - 2 * f)

    def h(o):
        return hash13(i + np.array(o, float))

    a = mix(h((0, 0, 0)), h((1, 0, 0)), f[..., 0])
    b = mix(h((0, 1, 0)), h((1, 1, 0)), f[..., 0])
    c = mix(h((0, 0, 1)), h((1, 0, 1)), f[..., 0])
    d = mix(h((0, 1, 1)), h((1, 1, 1)), f[..., 0])
    return mix(mix(a, b, f[..., 1]), mix(c, d, f[..., 1]), f[..., 2])


def cloth_wound(albedo, rough, pos, nb, wpos, R, age, seed, exit_w, style=1):
    """Mirror of applyClothWounds for one wound. pos: (H,W,3) bind positions. Returns albedo, rough, hole, soak."""
    d = pos - wpos
    dn = d @ nb
    t = d - dn[..., None] * nb
    down_v = np.array([0, -1, 0.0]) - nb * np.dot(nb, [0, -1, 0.0])
    down_len = np.linalg.norm(down_v)
    down = down_v / max(down_len, 1e-4)
    runs = smoothstep(0.1, 0.4, down_len)
    valid = (np.abs(dn) <= 0.04) & (np.linalg.norm(d, axis=-1) <= 0.2)
    r = np.linalg.norm(t, axis=-1)
    H = R * 1.3
    fray = vnoise(t * 900.0 + seed)
    fine = vnoise(t * 2600.0 + seed * 1.7)
    hole = 1 - smoothstep(H * (0.62 + 0.3 * fray), H * (0.9 + 0.35 * fray + 0.15 * fine), r)
    rim = smoothstep(H * 0.8, H * 1.15, r) * (1 - smoothstep(H * 1.25, H * 1.9 + 0.002 * fine, r))
    soak = np.zeros_like(r)
    a0, r0 = albedo, rough
    if style == 1:
        rs = (0.006 + 0.045 * (1 - np.exp(-age / 12.0))) * (1.5 if exit_w else 1.0)
        a = t @ down
        perp = np.linalg.norm(t - a[..., None] * down, axis=-1)
        ae = np.where(a > 0, mix(a, a / 1.6, runs), a)
        e = np.hypot(ae, perp)
        edge = 0.55 + 0.9 * vnoise(t * 380.0 + seed * 3.1) + 0.25 * fine
        soak = 1 - smoothstep(rs * 0.45 * edge, rs * (0.95 + 0.2 * fray), e)
        wet = 1 - smoothstep(40.0, 90.0, age)
        dry = 1 - wet
        stain = mix(np.array([0.30, 0.018, 0.018]), np.array([0.11, 0.018, 0.014]), dry)
        stain = stain * (1 - 0.2 * wet * (1 - smoothstep(rs * 0.2, rs * 0.3, e)))[..., None]
        amount = soak * (0.88 + 0.08 * dry)
        albedo = albedo * mix(1.0, stain, amount[..., None])
        rough = np.clip(rough - 0.3 * wet * soak, 0.05, 1)
        rough = mix(rough, 0.9, dry * soak * 0.5)
        albedo = albedo * (1 + 0.25 * rim * fray * (1 - soak * 0.6))[..., None]
    albedo = mix(albedo, np.array([0.012, 0.010, 0.010]), hole[..., None])
    rough = mix(rough, 0.9, hole)
    m = valid
    return np.where(m[..., None], albedo, a0), np.where(m, rough, r0), np.where(m, hole, 0), np.where(m, soak, 0)


def main(out):
    from PIL import Image
    S, tiles = 512, 5
    W = 103
    cases = [(0, False), (5, False), (20, False), (90, False), (20, True)]
    img = np.zeros((S, W * tiles, 3))
    scale = 0.16 / W          # metres per pixel (tiles are 16 cm wide)
    ys, xs = np.mgrid[0:S, 0:W]
    for k, (age, ex) in enumerate(cases):
        x = (xs - W / 2) * scale
        y = -(ys - S * 0.18) * scale
        pos = np.stack([x, y, np.zeros_like(x)], -1)
        alb = np.full((S, W, 3), 0.5)
        rough = np.full((S, W), 0.8)
        alb, rough, hole, soak = cloth_wound(alb, rough, pos, np.array([0, 0, 1.0]), np.zeros(3), 0.008, age, 3.7, ex)
        lit = alb * (0.55 + 0.45 * (1 - rough)[..., None])      # stand-in lighting: glossier (wet) reads brighter
        img[:, k * W:(k + 1) * W] = lit
        sel = soak > 0.5
        rows = np.where(sel.any(1))[0]
        h = (rows.max() - rows.min() + 1) * scale * 100 if len(rows) else 0
        below = (rows.max() - S * 0.18) * scale * 100 if len(rows) else 0
        print("age %4.0f exit=%d  soak>0.5 px=%5d  soak length=%.1f cm (down %.1f cm)  hole px=%4d  lit stain mean=%.3f"
              % (age, ex, sel.sum(), h, below, (hole > 0.5).sum(), lit[sel].mean() if sel.any() else 0))
    srgb = np.clip(img, 0, 1) ** (1 / 2.2)
    for k in range(1, tiles):
        srgb[:, k * W - 1] = 1.0
    Image.fromarray((srgb[:, :S] * 255 + 0.5).astype(np.uint8)).save(out)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "fabric_wound.png")
