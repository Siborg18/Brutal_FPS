"""
Bakes the tileable PBR texture library for BRUTAL FPS.

Run headless (from the project folder):
    blender -b --factory-startup -P Tools/blender/bake_textures.py -- [names...]   (or -- sheet: just the contact sheet)

Every material is a procedural Blender node graph (see RECIPES). For each one this bakes, from a 1 x 1 m UV plane:
  - base colour, a paint mask, roughness, metalness and height (Cycles EMIT bakes),
then derives a tangent-space normal map and a cavity/AO term from the height in numpy, and writes three PNGs to
Assets/Textures:
  <name>_albedo.png   sRGB colour, alpha = paint mask (where the game's per-object tint applies)
  <name>_normal.png   tangent-space normal (OpenGL convention, +Y = +V)
  <name>_orm.png      R = ambient occlusion, G = roughness, B = metalness
All textures tile seamlessly: every noise is sampled on a 4D torus built from the UVs (see nodekit.py).
"""
import os
import sys
import time

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import nodekit  # noqa: E402

PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(PROJECT, "Assets", "Textures")


# ======================================================================================================== recipes
# Each recipe returns dict(color=C, mask=S|float, rough=S|float, metal=S|float, height=S) with height in 0..1.
# Meta: tile = metres the texture covers in the game, res = pixels, depth = metres of relief for height 0..1,
#       ao = cavity strength, ao_r = cavity radius in metres.

def concrete_wall(g):
    u, v = g.uv()
    mott = g.noise(5, detail=8, rough=0.62, seed=1)
    fine = g.noise(70, detail=3, seed=2)
    pd, _ = g.voronoi(260, seed=3)
    pore = g.smooth(0.16, 0.0, pd) * g.smooth(0.45, 0.6, g.noise(20, seed=31))
    edge, _ = g.voronoi(2.5, feature='DISTANCE_TO_EDGE', seed=4, warp_u=g.noise(9, seed=41) * 0.04, warp_v=g.noise(9, seed=42) * 0.04)
    crack = g.smooth(0.010, 0.0, edge) * g.smooth(0.52, 0.66, g.noise(3, seed=5))
    flake = g.noise(4, detail=8, rough=0.64, seed=6, distortion=0.25)
    painted = g.smooth(0.515, 0.495, flake)
    paint_lip = g.smooth(0.485, 0.495, flake) * g.smooth(0.515, 0.497, flake)
    conc = g.ramp(mott * 0.75 + fine * 0.25, [(0.15, (0.30, 0.29, 0.27)), (0.5, (0.47, 0.45, 0.42)), (0.85, (0.60, 0.58, 0.55))])
    conc = g.scale(conc, 1.0 - pore * 0.45)
    paint = g.ramp(g.noise(3, detail=5, seed=7), [(0.3, (0.62, 0.61, 0.57)), (0.7, (0.74, 0.73, 0.69))])
    paint = g.scale(paint, 0.9 + fine * 0.12)
    col = g.mixc(conc, paint, painted)
    col = g.scale(col, 1.0 - crack * 0.6)
    col = g.scale(col, 0.82 + 0.18 * g.smooth(0.25, 0.65, g.noise(9, detail=6, seed=8)))  # grime
    h = g.clamp(0.5 + (mott - 0.5) * 0.35 + fine * 0.08 - pore * 0.22 - crack * 0.45 + painted * 0.12 + paint_lip * 0.05)
    return dict(color=col, mask=0.0, rough=g.lerp(0.93, 0.78, painted), metal=0.0, height=h)


def concrete_floor(g):
    mott = g.noise(4, detail=8, rough=0.6, seed=11)
    fine = g.noise(90, detail=3, seed=12)
    ad, acol = g.voronoi(140, seed=13)
    agg = g.smooth(0.32, 0.12, ad) * g.smooth(0.55, 0.7, g.noise(30, seed=14))
    tone = g.chan(acol, 0)
    polish = g.smooth(0.35, 0.7, g.noise(1.5, detail=4, seed=15))
    stain = g.smooth(0.6, 0.75, g.noise(2.5, detail=6, seed=16))
    edge, _ = g.voronoi(1.6, feature='DISTANCE_TO_EDGE', seed=17, warp_u=g.noise(8, seed=171) * 0.05, warp_v=g.noise(8, seed=172) * 0.05)
    crack = g.smooth(0.006, 0.0, edge) * g.smooth(0.5, 0.62, g.noise(2, seed=18))
    base = g.ramp(mott * 0.7 + fine * 0.3, [(0.15, (0.25, 0.24, 0.22)), (0.5, (0.36, 0.34, 0.31)), (0.9, (0.46, 0.44, 0.40))])
    stone = g.ramp(tone, [(0.0, (0.22, 0.21, 0.20)), (0.5, (0.42, 0.40, 0.36)), (1.0, (0.55, 0.52, 0.47))])
    col = g.mixc(base, stone, agg * 0.8)
    col = g.scale(col, 1.0 - stain * 0.45)
    col = g.scale(col, 1.0 - crack * 0.6)
    h = g.clamp(0.5 + (mott - 0.5) * 0.2 + fine * 0.1 + agg * 0.15 - crack * 0.5)
    rough = g.clamp(g.lerp(0.88, 0.55, polish) - stain * 0.2)
    return dict(color=col, mask=0.0, rough=rough, metal=0.0, height=h)


def rust_color(g, seed):
    return g.ramp(g.noise(30, detail=6, seed=seed), [(0.25, (0.13, 0.05, 0.02)), (0.5, (0.30, 0.12, 0.04)), (0.75, (0.48, 0.22, 0.07))])


def rusty_metal(g):
    rn = g.noise(3.5, detail=9, rough=0.66, seed=21, distortion=0.2)
    rust = g.smooth(0.40, 0.56, rn)
    heavy = g.smooth(0.55, 0.68, rn)
    pits, _ = g.voronoi(320, seed=22)
    pit = g.smooth(0.18, 0.0, pits) * rust
    blister = g.noise(35, detail=4, seed=23)
    scratch_n = g.noise(2, 140, detail=2, seed=24)
    scratch = g.smooth(0.012, 0.0, g.absv(scratch_n - 0.5)) * (1.0 - rust)
    steel = g.scale(g.rgb(0.36, 0.36, 0.37), 0.75 + 0.35 * g.noise(12, detail=4, seed=25))
    col = g.mixc(steel, rust_color(g, 26), rust)
    col = g.mixc(col, g.rgb(0.09, 0.035, 0.015), heavy * 0.5)
    col = g.mixc(col, g.rgb(0.55, 0.55, 0.56), scratch * 0.7)
    h = g.clamp(0.45 + rust * 0.2 + heavy * blister * 0.25 - pit * 0.25 + scratch * -0.1)
    return dict(color=col, mask=0.0, rough=g.lerp(0.42, 0.93, rust), metal=1.0 - rust, height=h)


def steel(g):
    base = g.noise(6, detail=6, seed=31)
    grime = g.smooth(0.45, 0.75, g.noise(2.5, detail=7, seed=32))
    spots = g.smooth(0.66, 0.74, g.noise(5, detail=8, seed=33, distortion=0.3))
    scratch_n = g.noise(3, 180, detail=2, seed=34)
    scratch = g.smooth(0.01, 0.0, g.absv(scratch_n - 0.5)) * (1.0 - spots)
    st = g.scale(g.rgb(0.30, 0.30, 0.31), 0.8 + 0.3 * base)
    col = g.scale(st, 1.0 - grime * 0.55)
    col = g.mixc(col, rust_color(g, 35), spots * 0.9)
    col = g.mixc(col, g.rgb(0.6, 0.6, 0.6), scratch * 0.6)
    h = g.clamp(0.5 + spots * 0.25 + base * 0.05 - scratch * 0.2)
    rough = g.clamp(0.48 + grime * 0.3 + spots * 0.35 - scratch * 0.2)
    return dict(color=col, mask=0.0, rough=rough, metal=g.clamp(1.0 - spots - grime * 0.3), height=h)


def wood_planks(g):
    u, v = g.uv()
    planks = 8
    pv = v * planks
    idx = g.floor(pv)
    f = g.fract(pv)
    rnd = g.white(idx * 1.37 + 0.5)
    rnd2 = g.white(idx * 2.71 + 9.5)
    gap = g.smooth(0.0, 0.035, f) * g.smooth(1.0, 0.965, f)
    # one butt joint per plank per tile, at a random spot
    ju = g.fract(u + rnd2)
    joint = g.smooth(0.0, 0.0025, ju) * g.smooth(1.0, 0.9975, ju)
    # nails just either side of the joint, two per plank end
    nail_u = g.mn(g.absv(ju - 0.012), g.absv(ju - 0.988))
    nail_v = g.mn(g.absv(f - 0.3), g.absv(f - 0.7))
    nail = g.smooth(0.0036, 0.0022, g.pw(nail_u * nail_u + (nail_v / planks) * (nail_v / planks), 0.5))
    wob = g.noise(3, 3, detail=3, seed=41) * 0.02
    grain = g.noise(1.2, 110, detail=5, rough=0.55, seed=42, warp_v=wob)
    grain2 = g.noise(4, 300, detail=3, seed=43)
    kd, _ = g.voronoi(5, 9, seed=44)
    knot = g.smooth(0.07, 0.0, kd) * g.smooth(0.55, 0.7, g.noise(7, seed=45))
    weather = g.clamp(g.smooth(0.3, 0.7, g.noise(2, detail=6, seed=46)) * 0.7 + rnd * 0.4)
    brown = g.ramp(rnd, [(0.0, (0.26, 0.16, 0.08)), (0.5, (0.38, 0.25, 0.13)), (1.0, (0.46, 0.32, 0.18))])
    silver = g.rgb(0.40, 0.37, 0.33)
    col = g.mixc(brown, silver, weather)
    col = g.scale(col, 0.68 + 0.42 * grain)
    col = g.scale(col, 0.92 + 0.12 * grain2)
    col = g.mixc(col, g.rgb(0.08, 0.05, 0.03), knot * 0.85)
    col = g.scale(col, 0.25 + 0.75 * gap * joint)
    col = g.scale(col, 0.85 + 0.15 * g.noise(6, detail=6, seed=47))  # dirt
    col = g.mixc(col, g.rgb(0.10, 0.06, 0.04), nail)
    h = g.clamp(0.15 + gap * joint * (0.55 + grain * 0.18 + grain2 * 0.06 - knot * 0.05) + (rnd - 0.5) * 0.08 * gap + nail * 0.03)
    return dict(color=col, mask=0.0, rough=g.lerp(0.75, 0.9, weather), metal=nail * 0.8, height=h)


def painted_metal(g):
    chip_n = g.noise(5, detail=9, rough=0.62, seed=51, distortion=0.4)
    chip = g.smooth(0.585, 0.615, chip_n)
    primer = g.smooth(0.56, 0.585, chip_n) * (1.0 - chip)
    rust_amt = g.smooth(0.45, 0.6, g.noise(9, detail=5, seed=52))
    scratch_n = g.noise(2, 90, detail=2, seed=53)
    scratch = g.smooth(0.006, 0.0, g.absv(scratch_n - 0.5)) * g.smooth(0.5, 0.7, g.noise(3, seed=54))
    grime = g.smooth(0.4, 0.8, g.noise(3, detail=7, seed=55))
    paint = g.scale(g.rgb(0.86, 0.86, 0.84), 0.9 + 0.1 * g.noise(15, detail=4, seed=56))
    bare = g.mixc(g.rgb(0.45, 0.45, 0.46), rust_color(g, 57), rust_amt)
    col = g.mixc(paint, g.rgb(0.42, 0.38, 0.34), primer)
    col = g.mixc(col, bare, g.clamp(chip + scratch))
    col = g.scale(col, 1.0 - grime * 0.35)
    paint_amt = g.clamp(1.0 - chip - scratch - primer)
    h = g.clamp(0.35 + (1.0 - chip) * 0.3 + primer * -0.1 + rust_amt * chip * 0.15 - scratch * 0.2)
    rough = g.clamp(g.lerp(0.48, 0.88, rust_amt * chip) + grime * 0.15)
    metal = g.clamp((chip + scratch) * (1.0 - rust_amt))
    return dict(color=col, mask=paint_amt, rough=rough, metal=metal, height=h)


def corrugated(g):
    u, v = g.uv()
    ribs = 8
    p = g.fract(u * ribs)
    tri = g.absv(p - 0.5) * 2.0
    prof = g.smooth(0.28, 0.72, tri)
    dents = g.noise(1.2, detail=3, seed=61)
    fade = g.smooth(0.35, 0.8, g.noise(1.5, detail=6, seed=62))
    streak = g.smooth(0.68, 0.88, g.noise(36, 1.6, detail=4, seed=63)) * g.smooth(0.4, 0.7, g.noise(2, seed=64))
    patch_n = g.noise(4, detail=9, rough=0.65, seed=65, distortion=0.3)
    patch = g.smooth(0.6, 0.68, patch_n)
    scuff_n = g.noise(1.5, 50, detail=3, seed=66)
    scuff = g.smooth(0.01, 0.0, g.absv(scuff_n - 0.5)) * g.smooth(0.55, 0.75, g.noise(2.5, seed=67))
    paint = g.scale(g.rgb(0.88, 0.88, 0.86), 0.85 + 0.15 * g.noise(10, detail=4, seed=68))
    paint = g.scale(paint, 1.0 + fade * 0.18)
    rust = g.clamp(streak * 0.8 + patch)
    col = g.mixc(paint, rust_color(g, 69), rust)
    col = g.mixc(col, g.rgb(0.42, 0.42, 0.43), scuff)
    col = g.scale(col, 0.88 + 0.12 * g.noise(20, detail=5, seed=70))
    h = g.clamp(0.08 + prof * 0.8 + (dents - 0.5) * 0.12 + patch * 0.04)
    return dict(color=col, mask=g.clamp(1.0 - rust - scuff), rough=g.clamp(g.lerp(0.55, 0.9, rust) - scuff * 0.2), metal=scuff * 0.9, height=h)


def galvanized(g):
    u, v = g.uv()
    waves = 30
    prof = 0.5 + 0.5 * g.sin(u * (waves * nodekit.TAU))
    sd, scol = g.voronoi(70, seed=71)
    spangle = g.chan(scol, 0)
    white_rust = g.smooth(0.6, 0.82, g.noise(5, detail=8, seed=72, distortion=0.4))
    red = g.smooth(0.64, 0.74, g.noise(4, detail=9, seed=73, distortion=0.4))
    streak = g.smooth(0.7, 0.9, g.noise(40, 1.5, detail=4, seed=74)) * g.smooth(0.45, 0.65, g.noise(2.5, seed=75))
    rust = g.clamp(red + streak * 0.7)
    zinc = g.scale(g.rgb(0.55, 0.56, 0.56), 0.85 + 0.25 * spangle)
    col = g.mixc(zinc, g.rgb(0.66, 0.66, 0.63), white_rust * 0.45)
    col = g.mixc(col, rust_color(g, 76), rust)
    col = g.scale(col, 0.85 + 0.15 * g.noise(12, detail=5, seed=77))
    h = g.clamp(0.1 + prof * 0.8 + rust * 0.05)
    rough = g.clamp(0.5 + spangle * 0.12 + white_rust * 0.3 + rust * 0.4)
    metal = g.clamp(0.3 - white_rust * 0.25 - rust)   # weathered zinc: dull and chalky, mostly diffuse
    return dict(color=col, mask=0.0, rough=rough, metal=metal, height=h)


def gravel(g):
    d1, c1 = g.voronoi(34, seed=81, randomness=0.9)
    d2, c2 = g.voronoi(75, seed=82, randomness=1.0)
    s1 = g.pw(g.smooth(0.5, 0.0, d1), 0.6)
    s2 = g.pw(g.smooth(0.5, 0.0, d2), 0.6) * 0.7
    top = g.mx(s1, s2)
    is1 = g.smooth(-0.02, 0.02, s1 - s2)
    tone = g.lerp(g.chan(c2, 1), g.chan(c1, 0), is1)
    stone = g.ramp(tone, [(0.0, (0.16, 0.16, 0.16)), (0.3, (0.30, 0.29, 0.27)), (0.6, (0.42, 0.40, 0.37)), (0.85, (0.50, 0.44, 0.36)), (1.0, (0.58, 0.57, 0.55))])
    stone = g.scale(stone, 0.85 + 0.2 * g.noise(120, detail=3, seed=83))
    fines = g.scale(g.rgb(0.26, 0.24, 0.22), 0.8 + 0.3 * g.noise(150, detail=3, seed=84))
    cover = g.smooth(0.05, 0.25, top)
    col = g.mixc(fines, stone, cover)
    col = g.scale(col, 0.75 + 0.25 * g.smooth(0.0, 0.6, top))
    h = g.clamp(top * 0.9 + g.noise(200, detail=2, seed=85) * 0.06)
    return dict(color=col, mask=0.0, rough=g.lerp(0.95, 0.82, cover), metal=0.0, height=h)


def red_dirt(g):
    mott = g.noise(3, detail=8, rough=0.6, seed=91)
    pale = g.smooth(0.5, 0.72, g.noise(1.2, detail=5, seed=92))
    edge, _ = g.voronoi(9, feature='DISTANCE_TO_EDGE', seed=93, warp_u=g.noise(20, seed=931) * 0.01, warp_v=g.noise(20, seed=932) * 0.01)
    crust = g.smooth(0.5, 0.62, g.noise(2, detail=5, seed=94))
    crack = g.smooth(0.008, 0.0, edge) * crust
    pd, pcol = g.voronoi(90, seed=95)
    pebble_sel = g.smooth(0.8, 0.84, g.chan(pcol, 0))
    pebble = g.pw(g.smooth(0.42, 0.0, pd), 0.7) * pebble_sel
    straw = g.smooth(0.82, 0.86, g.noise(4, 160, detail=2, seed=96)) * g.smooth(0.45, 0.7, g.noise(3, seed=97))
    fine = g.noise(180, detail=3, seed=98)
    base = g.ramp(mott, [(0.1, (0.30, 0.10, 0.04)), (0.5, (0.45, 0.17, 0.07)), (0.9, (0.58, 0.25, 0.10))])
    base = g.mixc(base, g.rgb(0.62, 0.36, 0.20), pale * 0.6)
    base = g.scale(base, 0.85 + 0.25 * fine)
    col = g.mixc(base, g.ramp(g.chan(pcol, 1), [(0.0, (0.18, 0.08, 0.04)), (0.5, (0.35, 0.22, 0.14)), (1.0, (0.55, 0.50, 0.44))]), pebble_sel * g.smooth(0.0, 0.2, pebble))
    col = g.mixc(col, g.rgb(0.62, 0.52, 0.30), straw * 0.8)
    col = g.scale(col, 1.0 - crack * 0.5)
    h = g.clamp(0.4 + (mott - 0.5) * 0.25 + fine * 0.08 + crust * 0.06 - crack * 0.3 + pebble * 0.45 + straw * 0.05)
    return dict(color=col, mask=0.0, rough=g.lerp(0.97, 0.9, pebble_sel), metal=0.0, height=h)


def dirt_road(g):
    mott = g.noise(2.5, detail=8, rough=0.6, seed=101)
    fine = g.noise(220, detail=3, seed=102)
    pd, pcol = g.voronoi(70, seed=103)
    sel = g.smooth(0.88, 0.9, g.chan(pcol, 0))
    stone = g.pw(g.smooth(0.4, 0.0, pd), 0.7) * sel
    packed = g.smooth(0.4, 0.7, g.noise(1.5, detail=4, seed=104))
    base = g.ramp(mott, [(0.1, (0.55, 0.29, 0.15)), (0.5, (0.68, 0.38, 0.20)), (0.9, (0.76, 0.46, 0.27))])
    base = g.scale(base, 0.85 + 0.25 * fine)
    base = g.scale(base, 1.0 - packed * 0.12)
    col = g.mixc(base, g.rgb(0.42, 0.32, 0.24), sel * g.smooth(0.0, 0.2, stone))
    h = g.clamp(0.4 + (mott - 0.5) * 0.15 + fine * 0.1 + stone * 0.4 - packed * 0.05)
    return dict(color=col, mask=0.0, rough=g.lerp(0.95, 0.85, packed), metal=0.0, height=h)


def foliage(g):
    d, c = g.voronoi(26, seed=111, randomness=1.0)
    leaf = g.pw(g.smooth(0.55, 0.0, d), 0.5)
    d2, c2 = g.voronoi(55, seed=112)
    leaf2 = g.pw(g.smooth(0.55, 0.0, d2), 0.5) * 0.8
    top = g.mx(leaf, leaf2)
    tone = g.lerp(g.chan(c2, 0), g.chan(c, 0), g.smooth(-0.02, 0.02, leaf - leaf2))
    green = g.ramp(tone, [(0.0, (0.20, 0.25, 0.16)), (0.45, (0.33, 0.38, 0.26)), (0.8, (0.46, 0.50, 0.40)), (1.0, (0.36, 0.30, 0.18))])
    dry = g.smooth(0.88, 0.92, tone)
    col = g.mixc(g.rgb(0.04, 0.045, 0.03), green, g.smooth(0.02, 0.2, top))
    col = g.mixc(col, g.rgb(0.42, 0.33, 0.20), dry * 0.6)
    col = g.scale(col, 0.7 + 0.3 * top)
    return dict(color=col, mask=0.0, rough=0.8, metal=0.0, height=g.clamp(top))


def gunmetal(g):
    brush = g.noise(2, 260, detail=3, seed=121)
    mott = g.noise(4, detail=6, seed=122)
    pits, _ = g.voronoi(160, seed=123)
    pit = g.smooth(0.12, 0.0, pits) * g.smooth(0.6, 0.75, g.noise(8, seed=124))
    col = g.scale(g.rgb(0.13, 0.13, 0.14), 0.85 + 0.2 * brush + 0.1 * mott)
    h = g.clamp(0.5 + (brush - 0.5) * 0.3 - pit * 0.3)
    return dict(color=col, mask=0.0, rough=g.clamp(0.33 + brush * 0.12 + mott * 0.1), metal=0.85, height=h)


def polymer(g):
    d, _ = g.voronoi(70, seed=131, randomness=0.8)
    stip = g.pw(g.smooth(0.5, 0.0, d), 0.6)
    mott = g.noise(6, detail=5, seed=132)
    col = g.scale(g.rgb(0.05, 0.05, 0.055), 0.85 + 0.25 * mott + 0.15 * stip)
    return dict(color=col, mask=0.0, rough=g.clamp(0.62 - stip * 0.12 + mott * 0.08), metal=0.0, height=g.clamp(0.3 + stip * 0.6))


def skin(g):
    pd, _ = g.voronoi(150, seed=141)
    pore = g.smooth(0.12, 0.0, pd)
    crease_n = g.noise(3, 40, detail=4, seed=142, distortion=0.4)
    crease = g.smooth(0.006, 0.0, g.absv(crease_n - 0.5)) * g.smooth(0.4, 0.7, g.noise(5, seed=146))
    blot = g.smooth(0.45, 0.7, g.noise(3, detail=6, seed=143))
    col = g.scale(g.rgb(0.50, 0.31, 0.23), 0.92 + 0.12 * g.noise(25, detail=4, seed=144))
    col = g.mixc(col, g.rgb(0.55, 0.26, 0.20), blot * 0.4)
    col = g.scale(col, 1.0 - pore * 0.12 - crease * 0.07)
    h = g.clamp(0.6 - pore * 0.25 - crease * 0.15 + g.noise(60, detail=3, seed=145) * 0.1)
    return dict(color=col, mask=1.0, rough=g.clamp(0.5 + crease * 0.1 + pore * 0.1), metal=0.0, height=h)


def cloth(g):
    u, v = g.uv()
    n = 24
    iu = g.floor(u * n)
    iv = g.floor(v * n)
    fu = g.fract(u * n)
    fv = g.fract(v * n)
    twill = g.fract((iu + iv) / 4.0)
    over = g.smooth(0.49, 0.51, twill)          # warp over weft in a diagonal twill
    tu = g.sin(fu * 3.14159)                     # rounded thread profiles
    tv = g.sin(fv * 3.14159)
    thread = g.lerp(tv, tu, over)
    fuzz = g.noise(40, detail=5, seed=151)
    wear = g.smooth(0.55, 0.8, g.noise(2, detail=5, seed=152))
    col = g.scale(g.rgb(0.62, 0.62, 0.62), 0.7 + 0.3 * thread + 0.1 * fuzz)
    col = g.scale(col, 1.0 + wear * 0.15)
    return dict(color=col, mask=1.0, rough=0.88, metal=0.0, height=g.clamp(thread * 0.8 + fuzz * 0.2))


def fleece(g):
    """Polyester fleece (the sleeves): a soft brushed pile in little tufts, fine fibres combed one way, and pills of
    balled-up fibre scattered over it. One repeat is 5 cm; the paint mask takes the game's colour."""
    tuft = g.noise(14, detail=6, rough=0.62, seed=171)
    fibre = g.noise(120, 22, detail=3, seed=172)
    pd, _ = g.voronoi(22, seed=173, randomness=0.95)
    pill = g.smooth(0.24, 0.02, pd) * g.smooth(0.42, 0.62, g.noise(5, detail=3, seed=174))
    mott = g.noise(2.5, detail=4, seed=175)
    h = g.clamp(0.30 + tuft * 0.40 + (fibre - 0.5) * 0.12 + pill * 0.40)
    col = g.scale(g.rgb(0.70, 0.70, 0.70), 0.76 + 0.24 * tuft + 0.06 * fibre + 0.10 * pill - 0.08 * mott)
    return dict(color=col, mask=1.0, rough=g.clamp(0.96 - pill * 0.06), metal=0.0, height=h)


def denim(g):
    """Indigo denim: a 2/1 right-hand twill (diagonal ribs), slubby warp threads, the pale weft showing through where
    it floats, and uneven fading. One repeat is 5 cm (42 threads); the paint mask takes the game's colour."""
    u, v = g.uv()
    n = 42                                                         # divisible by 3, so the diagonal repeat tiles
    u = u + 0.5 / n                                                # half a thread of phase: the tile seam falls mid-thread
    v = v + 0.5 / n
    fu = g.fract(u * n)
    fv = g.fract(v * n)
    # 2 up, 1 down on a continuous diagonal phase (cos = 1 for one thread in three, -0.5 for the other two)
    warp_up = g.smooth(0.25, -0.25, g.sin(((u + v) * n - 1.0) * (6.28318 / 3.0) + 1.5708))
    pu = g.sin(fu * 3.14159)                                       # round thread profiles
    pv = g.sin(fv * 3.14159)
    slub = g.noise(n, 3, detail=3, rough=0.6, seed=181)            # thick-thin warp yarn along each thread
    slub = g.smooth(0.35, 0.75, slub)
    fade = g.noise(3, detail=5, seed=182)
    fuzz = g.noise(90, detail=2, seed=183)
    warp = g.scale(g.rgb(0.50, 0.50, 0.50), 0.62 + 0.30 * pu + 0.18 * slub)
    weft = g.scale(g.rgb(0.90, 0.90, 0.90), 0.60 + 0.34 * pv)
    col = g.mixc(weft, warp, warp_up)
    col = g.scale(col, 0.90 + 0.10 * fuzz + 0.10 * fade * (1.0 - warp_up * 0.4))
    h = g.clamp(0.25 + 0.50 * g.lerp(pv, pu * (0.8 + 0.2 * slub), warp_up) + 0.10 * fuzz)
    return dict(color=col, mask=1.0, rough=g.clamp(0.82 - 0.07 * fade + 0.03 * fuzz), metal=0.0, height=h)


def canvas(g):
    """Heavy cotton canvas / duck: a tight plain weave with fat threads, soft stiffness wrinkles, a few pale scuffs.
    One repeat is 6 cm (30 threads)."""
    u, v = g.uv()
    n = 30
    u = u + 0.5 / n                                                # the tile seam falls mid-thread, not on a thread edge
    v = v + 0.5 / n
    fu = g.fract(u * n)
    fv = g.fract(v * n)
    over = g.smooth(-0.3, 0.3, g.sin(u * (n * 3.14159)) * g.sin(v * (n * 3.14159)))   # soft checkerboard: warp over, then weft over
    pu = g.sin(fu * 3.14159)
    pv = g.sin(fv * 3.14159)
    thread = g.lerp(pv, pu, over)
    slub = g.noise(n, n, detail=2, seed=191)
    wrinkle = g.noise(2, 3, detail=4, rough=0.55, seed=192)
    scuff = g.smooth(0.66, 0.80, g.noise(6, detail=5, seed=193)) * g.smooth(0.40, 0.60, g.noise(90, detail=2, seed=194))
    col = g.scale(g.rgb(0.62, 0.62, 0.62), 0.66 + 0.30 * thread + 0.08 * slub - 0.14 * g.smooth(0.3, 0.1, wrinkle))
    col = g.scale(col, 1.0 + scuff * 0.22)
    h = g.clamp(0.30 + 0.45 * thread + 0.06 * slub + (wrinkle - 0.5) * 0.45 + scuff * 0.04)
    return dict(color=col, mask=1.0, rough=g.clamp(0.80 + 0.06 * slub - 0.08 * scuff), metal=0.0, height=h)


def leather(g):
    """Fine pebble-grain leather: rounded pebbles split by thin grooves, a few long creases, and glossier worn
    patches. One repeat is 8 cm."""
    edge, _ = g.voronoi(64, seed=201, randomness=0.9, feature='DISTANCE_TO_EDGE',
                           warp_u=g.noise(20, seed=2011) * 0.006, warp_v=g.noise(20, seed=2012) * 0.006)
    pebble = g.smooth(0.0, 0.16, edge)
    top = g.pw(pebble, 0.7)
    crease_n = g.noise(3, 2, detail=4, seed=202, distortion=0.4)
    crease = g.smooth(0.020, 0.0, g.absv(crease_n - 0.5)) * g.smooth(0.35, 0.6, g.noise(2, detail=3, seed=203))
    worn = g.smooth(0.50, 0.72, g.noise(2.5, detail=5, seed=204))
    mott = g.noise(8, detail=5, seed=205)
    _, cell = g.voronoi(64, seed=201, randomness=0.9)          # same cells, for a per-pebble tone
    tone = g.chan(cell, 0)
    col = g.scale(g.rgb(0.55, 0.55, 0.55), 0.62 + 0.30 * top + 0.10 * mott + 0.05 * tone)
    col = g.scale(col, 1.0 - crease * 0.40 + worn * 0.10)
    h = g.clamp(0.20 + 0.50 * top - crease * 0.35 + (mott - 0.5) * 0.08)
    return dict(color=col, mask=1.0, rough=g.clamp(0.62 - worn * 0.17 - top * 0.05 + crease * 0.05), metal=0.0, height=h)


def knit(g):
    """Stockinette knit (jumpers, beanies): columns of interlocking V's. Each stitch is two raised, rounded, slanted
    lobes (+25 and -25 degrees from vertical) meeting at a point; grooves run between columns and a small dip
    between rows. One repeat is 4 cm: 8 stitches across, 10 rows up. Fuzz is fine noise in colour and roughness."""
    u, v = g.uv()
    nu, nv = 8, 10
    u = u + 0.25 / nu                                          # moves the tile seam off the stitch edges
    v = v + 0.3 / nv
    # cell-local coordinates in tile units (a stitch is 1/8 wide, 1/10 tall), origin at the cell centre
    x = (g.fract(u * nu) - 0.5) * (1.0 / nu)
    y = (g.fract(v * nv) - 0.5) * (1.0 / nv)
    fv = g.fract(v * nv)
    s25, c25 = 0.4226, 0.9063
    half, radius = 0.043, 0.026

    def lobe(sign, row):
        yy = y + row * (1.0 / nv)                              # lobes of the rows above/below overlap into this cell
        px = x - sign * 0.025
        # axis runs from the top (outer) to the bottom (towards the centre line): left lobe goes down-right
        dx, dy = -sign * s25, -c25
        t = g.mx(g.mn(px * (-dx) + yy * (-dy), half), -half)     # projection on the axis, clamped to the segment
        qx = px + dx * t
        qy = yy + dy * t
        dist = g.pw(qx * qx + qy * qy, 0.5)                    # distance to the segment
        return g.sin(g.smooth(radius, 0.0, dist) * 1.5708)     # rounded bump, 1 on the spine, 0 at the lobe edge

    body = g.mx(g.mx(lobe(-1.0, 0), lobe(1.0, 0)), g.mx(g.mx(lobe(-1.0, -1), lobe(1.0, -1)), g.mx(lobe(-1.0, 1), lobe(1.0, 1))))
    body = body * (0.82 + 0.18 * g.sin(fv * 3.14159))   # small dip between rows
    fuzz = g.noise(110, detail=3, seed=211)
    fuzz2 = g.noise(26, 30, detail=4, seed=212)
    mott = g.noise(3, detail=4, seed=213)
    col = g.scale(g.rgb(0.68, 0.68, 0.68), 0.40 + 0.50 * body + 0.14 * fuzz - 0.08 * mott)
    h = g.clamp(0.10 + 0.70 * body + 0.04 * fuzz + 0.03 * fuzz2)
    return dict(color=col, mask=1.0, rough=g.clamp(0.92 + 0.08 * fuzz - 0.06 * body), metal=0.0, height=h)


def hair(g):
    """Hair: fine strands running along V, grouped into clumps, darker towards the roots. Neutral grey so the tint
    gives the colour. One repeat is 3 cm."""
    u, v = g.uv()
    strand = g.noise(300, 3, detail=2, rough=0.5, seed=221)
    fine = g.noise(180, 6, detail=1, seed=222)
    clump = g.noise(14, 2, detail=3, seed=223)
    clump_edge = g.smooth(0.30, 0.55, clump)
    sheen = g.noise(5, 1, detail=3, seed=224)
    root = 0.5 + 0.5 * g.sin((v - 0.25) * 6.28318)             # seamless: dark roots at one edge of the repeat, lighter tips
    root = g.pw(root, 0.8)
    col = g.scale(g.rgb(0.62, 0.62, 0.62), (0.45 + 0.35 * strand + 0.10 * fine + 0.12 * clump_edge) * (0.65 + 0.35 * root))
    h = g.clamp(0.20 + 0.45 * strand + 0.20 * clump_edge + 0.10 * fine)
    return dict(color=col, mask=1.0, rough=g.clamp(0.55 - sheen * 0.10 + (1.0 - strand) * 0.08), metal=0.0, height=h)


def grip_stipple(g):
    """Aggressive pistol-grip texture: a dense grid of small raised pyramids, worn a little on top."""
    u, v = g.uv()
    n = 14
    fu = g.fract(u * n)
    fv = g.fract(v * n)
    pyr = 1.0 - g.mx(g.absv(fu - 0.5), g.absv(fv - 0.5)) * 2.0
    pyr = g.clamp(pyr * 1.15 + (g.noise(60, detail=3, seed=161) - 0.5) * 0.2)
    wear = g.smooth(0.75, 0.95, pyr) * g.smooth(0.45, 0.7, g.noise(3, detail=4, seed=162))
    col = g.scale(g.rgb(0.045, 0.045, 0.05), 0.85 + 0.2 * g.noise(8, detail=4, seed=163) + 0.25 * wear)
    return dict(color=col, mask=0.0, rough=g.clamp(0.72 - wear * 0.25 - pyr * 0.05), metal=0.0, height=pyr)


def bark_gum(g):
    """Smooth gum bark: cream and salmon-orange skin, old grey bark peeling off in patches, fine creases."""
    u, v = g.uv()
    mott = g.noise(3, 1.5, detail=7, rough=0.6, seed=171)
    warm = g.smooth(0.35, 0.7, g.noise(2, 1.2, detail=5, seed=172))
    # Orange-tan, warmer where the new skin shows, with paler cream patches (river-red-gum colouring).
    base = g.mixc(g.rgb(0.46, 0.27, 0.15), g.rgb(0.50, 0.19, 0.065), warm)
    base = g.mixc(base, g.rgb(0.60, 0.47, 0.34), g.smooth(0.6, 0.8, mott) * 0.55)
    pd, _ = g.voronoi(5, 3, feature='F1', seed=173, warp_u=g.noise(8, seed=1731) * 0.05)
    patch_n = g.noise(4, 2.5, detail=6, seed=174, distortion=0.3)
    old = g.smooth(0.66, 0.69, patch_n)
    lip = g.smooth(0.63, 0.66, patch_n) * (1.0 - old)
    crease_n = g.noise(2, 45, detail=3, seed=175)
    crease = g.smooth(0.01, 0.0, g.absv(crease_n - 0.5)) * g.smooth(0.55, 0.8, g.noise(6, 2, seed=176))
    streak = g.noise(40, 2, detail=3, seed=177)
    col = g.mixc(base, g.scale(g.rgb(0.33, 0.28, 0.23), 0.8 + 0.3 * g.noise(30, detail=4, seed=178)), old)
    col = g.scale(col, 0.9 + 0.12 * streak - crease * 0.35 - lip * 0.25)
    h = g.clamp(0.4 + (mott - 0.5) * 0.15 + old * 0.25 + lip * 0.15 - crease * 0.3 + streak * 0.05)
    return dict(color=col, mask=0.0, rough=g.lerp(0.62, 0.9, old), metal=0.0, height=h)


def bark_kapok(g):
    """Rainforest giant (kapok, fig): smooth grey-green bark in shallow vertical ridges, crusted with pale lichen
    blotches and thick green moss in the cracks and patches, dark damp streaks running down. V runs up the trunk."""
    mott = g.noise(2.5, 1.2, detail=7, rough=0.6, seed=301)
    # Ridges up the trunk, wandering (warped in U), the fissures between them, and a fine fibrous grain along them.
    warp = g.noise(3, 1, detail=5, seed=302) * 0.08
    ridge_n = g.noise(18, 3, detail=5, rough=0.55, seed=303, warp_u=warp)
    fissure = g.smooth(0.05, 0.0, g.absv(ridge_n - 0.5)) * g.smooth(0.3, 0.6, g.noise(6, 2, seed=304))
    ridge = g.smooth(0.3, 0.75, ridge_n)
    grain = g.noise(160, 12, detail=3, seed=310, warp_u=warp)
    flake_d, _ = g.voronoi(24, 10, seed=311, warp_u=warp)
    flake = g.smooth(0.0, 0.35, flake_d)
    # Lichen: grey-green crusty blotches with soft, lobed edges; moss: deep green, in the fissures and in damp patches.
    lich_n = g.noise(7, 4, detail=7, rough=0.68, seed=305, distortion=0.4)
    lichen = g.smooth(0.62, 0.67, lich_n) * g.smooth(0.3, 0.6, g.noise(90, detail=3, seed=312))
    lichen_edge = g.smooth(0.57, 0.62, lich_n) * (1.0 - lichen)
    moss_n = g.noise(3, 2, detail=7, rough=0.62, seed=306, distortion=0.3)
    tuft = g.noise(150, 80, detail=3, seed=307)
    moss = g.clamp((g.smooth(0.52, 0.64, moss_n) + fissure * 0.8 * g.smooth(0.3, 0.5, moss_n)) * g.smooth(0.25, 0.55, tuft))
    streak = g.smooth(0.5, 0.75, g.noise(30, 1.2, detail=4, seed=308))   # damp running down
    bark = g.ramp(mott * 0.7 + grain * 0.3, [(0.15, (0.20, 0.19, 0.15)), (0.5, (0.32, 0.31, 0.25)), (0.85, (0.44, 0.42, 0.34))])
    bark = g.scale(bark, 0.75 + 0.3 * ridge + 0.15 * grain - fissure * 0.6 - streak * 0.25 - (1.0 - flake) * 0.15)
    lich_col = g.ramp(g.noise(40, detail=3, seed=309), [(0.3, (0.48, 0.52, 0.44)), (0.7, (0.62, 0.65, 0.56))])
    moss_col = g.ramp(tuft * 0.6 + mott * 0.4, [(0.25, (0.08, 0.14, 0.04)), (0.6, (0.17, 0.27, 0.07)), (0.9, (0.28, 0.36, 0.11))])
    col = g.mixc(bark, lich_col, lichen)
    col = g.scale(col, 1.0 - lichen_edge * 0.25)
    col = g.mixc(col, moss_col, moss)
    h = g.clamp(0.42 + ridge * 0.22 + grain * 0.08 - fissure * 0.4 - (1.0 - flake) * 0.06 + lichen * 0.06
                + moss * (0.1 + tuft * 0.18) + (mott - 0.5) * 0.1)
    return dict(color=col, mask=0.0, rough=g.lerp(g.lerp(0.78, 0.9, lichen), 0.95, moss), metal=0.0, height=h)


def bark_palm(g):
    """Walking palm (Socratea) trunk and stilt roots: dark olive skin mottled with pale grey-green speckles, faint
    rings of old leaf scars, tiny blunt spines on the roots, green algae in patches. V runs along the stem."""
    mott = g.noise(3, 2, detail=7, rough=0.62, seed=321)
    # Dense pale speckle (lichen dots), clumped into patches, over the dark skin.
    speck_d, _ = g.voronoi(60, 40, seed=322)
    speck = g.smooth(0.3, 0.12, speck_d) * g.smooth(0.35, 0.6, g.noise(9, 6, detail=4, seed=323))
    blotch = g.smooth(0.56, 0.64, g.noise(8, 5, detail=6, rough=0.65, seed=324, distortion=0.5))
    ring_v = g.fract(g.uv()[1] * 9.0 + g.noise(2, 1, seed=325) * 0.4)
    ring = g.smooth(0.45, 0.495, g.absv(ring_v - 0.5)) * g.smooth(0.25, 0.6, g.noise(6, 2, seed=330))   # faint grooves at old leaf scars
    spine_d, _ = g.voronoi(70, 50, seed=326)
    spine = g.smooth(0.14, 0.0, spine_d) * g.smooth(0.4, 0.6, g.noise(5, 3, seed=329))
    algae = g.smooth(0.55, 0.7, g.noise(3, 2, detail=6, seed=327))
    base = g.ramp(mott, [(0.2, (0.08, 0.10, 0.06)), (0.6, (0.14, 0.17, 0.10)), (0.9, (0.21, 0.24, 0.15))])
    pale = g.ramp(g.noise(30, detail=3, seed=328), [(0.3, (0.48, 0.52, 0.43)), (0.7, (0.64, 0.66, 0.57))])
    col = g.mixc(base, pale, g.clamp(speck * 0.9 + blotch * 0.7))
    col = g.mixc(col, g.rgb(0.13, 0.22, 0.06), algae * 0.6)
    col = g.scale(col, 1.0 - ring * 0.12 - spine * 0.35)
    h = g.clamp(0.45 + (mott - 0.5) * 0.12 + speck * 0.06 + blotch * 0.05 - ring * 0.1 + spine * 0.35)
    return dict(color=col, mask=0.0, rough=g.lerp(0.62, 0.88, g.clamp(blotch + algae)), metal=0.0, height=h)


def tyre_tread(g):
    """All-terrain tread: U runs round the tyre, V across it (the whole tread width). Dust settles in the grooves."""
    u, v = g.uv()
    blocks_u = 18
    col_idx = g.floor(v * 4.0)
    stagger = g.fract(col_idx * 0.5) * 0.5
    wig = g.sin((u * blocks_u + stagger) * nodekit.TAU) * 0.012
    vv = v + wig
    zig = g.mn(g.absv(g.fract(vv * 4.0) - 0.0), g.absv(g.fract(vv * 4.0) - 1.0))
    circ = g.smooth(0.03, 0.06, zig)                                      # circumferential grooves (zig-zag)
    lat = g.fract(u * blocks_u + stagger)
    lateral = g.smooth(0.0, 0.05, lat) * g.smooth(1.0, 0.86, lat)          # lateral grooves between blocks
    sipe = g.smooth(0.006, 0.0, g.absv(g.fract(u * blocks_u * 2 + stagger + 0.25) - 0.5) - 0.0) * g.smooth(0.15, 0.2, g.fract(vv * 4.0)) * g.smooth(0.85, 0.8, g.fract(vv * 4.0))
    shoulder = g.smooth(0.12, 0.0, v) + g.smooth(0.88, 1.0, v)
    block = g.clamp(circ * lateral - sipe * 0.6)
    dust = (1.0 - block) * g.smooth(0.3, 0.7, g.noise(6, 2, detail=4, seed=181))
    col = g.scale(g.rgb(0.035, 0.034, 0.033), 0.85 + 0.25 * g.noise(80, detail=3, seed=182))
    col = g.mixc(col, g.rgb(0.32, 0.18, 0.10), dust * 0.65)
    h = g.clamp(block * (0.85 - shoulder * 0.25) + g.noise(120, detail=2, seed=183) * 0.05)
    return dict(color=col, mask=0.0, rough=g.lerp(0.97, 0.82, block), metal=0.0, height=h)


def grille_honeycomb(g):
    """A honeycomb grille mesh: black gloss plastic; the cells are holes (alpha = 0)."""
    u, v = g.uv()
    nu, nv = 8.0, 5.0            # 8 cells across, 5 lattice periods down (very slightly squashed hexagons)
    rx, ry = 1.0, 1.7320508
    x = u * nu
    y = v * nv * ry

    def mod(a, m):
        return a - g.floor(a / m) * m

    # Two interleaved rectangular lattices; whichever centre is nearer owns the point (a hexagonal cell).
    ax = mod(x, rx) - rx * 0.5
    ay = mod(y, ry) - ry * 0.5
    bx = mod(x - rx * 0.5, rx) - rx * 0.5
    by = mod(y - ry * 0.5, ry) - ry * 0.5
    use_a = g.smooth(-0.0005, 0.0005, (bx * bx + by * by) - (ax * ax + ay * ay))
    gx = g.absv(g.lerp(bx, ax, use_a))
    gy = g.absv(g.lerp(by, ay, use_a))
    # Hexagon "radius" with pointy tops: max(|x|, x*0.5 + y*0.866); the cell edge is at 0.5.
    hexd = g.mx(gx, gx * 0.5 + gy * 0.8660254)
    frame = g.smooth(0.40, 0.44, hexd)
    col = g.scale(g.rgb(0.03, 0.03, 0.032), 0.9 + 0.2 * g.noise(10, detail=3, seed=191))
    return dict(color=col, mask=frame, rough=0.35, metal=0.0, height=frame)


# name: (recipe, tile metres, resolution, relief depth metres, ao strength, ao radius metres)
def snow(g):
    """Wind-packed snow: soft sastrugi ridges carved along the wind, a fine sugary grain, glazed icy patches."""
    ridge = g.noise(0.9, 5.0, detail=6, rough=0.55, seed=301, distortion=0.4)
    ridge2 = g.noise(2.2, 11.0, detail=4, seed=302)
    big = g.noise(0.7, detail=5, seed=303)
    grain = g.noise(300, detail=2, seed=304)
    glaze = g.smooth(0.63, 0.74, g.noise(1.3, detail=5, seed=305))
    sculpt = g.smooth(0.35, 0.78, ridge)
    h = g.clamp(0.35 + sculpt * 0.42 + (ridge2 - 0.5) * 0.18 + (big - 0.5) * 0.2 + grain * 0.035)
    col = g.ramp(big * 0.5 + ridge2 * 0.3 + sculpt * 0.2, [(0.0, (0.76, 0.80, 0.86)), (0.5, (0.86, 0.89, 0.93)), (1.0, (0.94, 0.95, 0.97))])
    col = g.scale(col, 0.96 + 0.07 * grain)
    col = g.mixc(col, g.rgb(0.74, 0.80, 0.88), glaze * 0.45)
    rough = g.clamp(g.lerp(0.88, 0.32, glaze) - grain * 0.06)
    return dict(color=col, mask=0.0, rough=rough, metal=0.0, height=h)


def rock(g):
    """Dark, frost-shattered basalt: layered, blocky and broken by thin fractures, with grey-green lichen and pale
    mineral flecks."""
    mott = g.noise(3, detail=8, rough=0.65, seed=313)
    strata = g.noise(0.8, 6.0, detail=5, seed=317, distortion=0.6)
    fine = g.noise(160, detail=3, seed=314)
    _, cell = g.voronoi(2.2, seed=311)
    edge, _ = g.voronoi(2.2, feature='DISTANCE_TO_EDGE', seed=311, warp_u=g.noise(5, seed=3111) * 0.05, warp_v=g.noise(5, seed=3112) * 0.05)
    facet = g.chan(cell, 0)
    crack = g.smooth(0.012, 0.0, edge) * g.smooth(0.35, 0.6, g.noise(5, seed=318))
    lichen = g.smooth(0.68, 0.76, g.noise(6, detail=6, seed=315))
    fleck = g.smooth(0.80, 0.83, g.noise(220, detail=1, seed=316))
    base = g.ramp(mott * 0.5 + facet * 0.25 + strata * 0.25, [(0.0, (0.022, 0.022, 0.025)), (0.5, (0.05, 0.05, 0.054)), (1.0, (0.10, 0.10, 0.104))])
    base = g.scale(base, 0.85 + 0.25 * fine)
    col = g.mixc(base, g.rgb(0.16, 0.17, 0.13), lichen * 0.6)
    col = g.mixc(col, g.rgb(0.22, 0.22, 0.22), fleck * 0.4)
    col = g.scale(col, 1.0 - crack * 0.7)
    h = g.clamp(0.4 + facet * 0.3 + (strata - 0.5) * 0.25 + (mott - 0.5) * 0.25 + fine * 0.05 - crack * 0.45)
    return dict(color=col, mask=0.0, rough=g.lerp(0.88, 0.7, fleck), metal=0.0, height=h)


def marine_paint(g):
    """Well-kept ship's paint over steel plate: welded seams on a 3 x 1.5 m grid, a few shallow dents, and thin rust
    weeps running down from the seams. The paint (mask) takes the game's colour; the rust doesn't."""
    u, v = g.uv()
    du = g.mn(u, 1.0 - u)                                   # distance to the vertical seam (one per 3 m tile)
    fv = g.fract(v * 2.0)
    dv = g.mn(fv, 1.0 - fv) * 0.5                           # distance to the horizontal seams (every 1.5 m)
    weld = g.mx(g.smooth(0.0035, 0.0, du), g.smooth(0.0035, 0.0, dv))
    below = 1.0 - fv                                         # 0 at the seam above, growing downwards
    weep = g.smooth(0.80, 0.93, g.noise(55, 1.4, detail=3, seed=401)) * g.smooth(0.45, 0.02, below)
    weep = weep * g.smooth(0.4, 0.6, g.noise(3, detail=3, seed=402))
    stain = g.smooth(0.62, 0.8, g.noise(2.5, detail=6, seed=403)) * 0.5
    dent = g.noise(1.6, detail=3, seed=404)
    fine = g.noise(140, detail=2, seed=405)
    paint = g.scale(g.rgb(1.0, 1.0, 1.0), 0.93 + 0.06 * fine - stain * 0.12)
    rust = g.ramp(g.noise(40, detail=4, seed=406), [(0.2, (0.16, 0.06, 0.02)), (0.8, (0.36, 0.15, 0.05))])
    r = g.clamp(weep * 0.9 + weld * 0.15)
    col = g.mixc(paint, rust, r)
    h = g.clamp(0.5 + (dent - 0.5) * 0.25 + weld * 0.35 + fine * 0.02)
    return dict(color=col, mask=g.clamp(1.0 - r), rough=g.clamp(0.42 + fine * 0.08 + r * 0.4 + stain * 0.2), metal=0.0, height=h)


def gun_wood(g):
    """Rifle furniture: oiled and varnished reddish hardwood (birch/beech), grain along U, handling wear."""
    u, v = g.uv()
    wob = g.noise(2, 6, detail=3, seed=171) * 0.03
    rings = g.noise(1.5, 70, detail=4, rough=0.6, seed=172, warp_v=wob)
    fine = g.noise(5, 420, detail=3, seed=173)
    flecks, _ = g.voronoi(12, 220, seed=174)
    fleck = g.smooth(0.10, 0.0, flecks) * g.smooth(0.55, 0.7, g.noise(9, seed=175))
    band = g.smooth(0.42, 0.62, rings)
    streak = g.smooth(0.55, 0.8, g.noise(1, 9, detail=3, seed=176))
    wear = g.smooth(0.55, 0.8, g.noise(3, detail=6, rough=0.6, seed=177))
    col = g.ramp(band, [(0.0, (0.29, 0.095, 0.035)), (1.0, (0.16, 0.045, 0.018))])
    col = g.mixc(col, g.rgb(0.12, 0.035, 0.014), streak * 0.45)
    col = g.scale(col, 0.88 + 0.18 * fine)
    col = g.mixc(col, g.rgb(0.40, 0.17, 0.07), wear * 0.30)
    col = g.mixc(col, g.rgb(0.12, 0.05, 0.02), fleck * 0.6)
    h = g.clamp(0.5 + (rings - 0.5) * 0.12 + (fine - 0.5) * 0.06 - fleck * 0.15 - wear * 0.04)
    return dict(color=col, mask=0.0, rough=g.clamp(0.36 + fine * 0.08 + wear * 0.22 + fleck * 0.2), metal=0.0, height=h)


RECIPES = {
    "concrete_wall": (concrete_wall, 2.5, 2048, 0.008, 1.2, 0.01),
    "concrete_floor": (concrete_floor, 4.0, 2048, 0.004, 1.0, 0.01),
    "rusty_metal": (rusty_metal, 1.5, 2048, 0.003, 1.0, 0.004),
    "steel": (steel, 1.5, 2048, 0.002, 0.8, 0.004),
    "wood_planks": (wood_planks, 2.0, 2048, 0.008, 1.4, 0.006),
    "painted_metal": (painted_metal, 1.0, 2048, 0.0015, 0.8, 0.003),
    "corrugated": (corrugated, 2.24, 2048, 0.036, 0.9, 0.06),
    "galvanized": (galvanized, 2.28, 2048, 0.016, 0.7, 0.03),
    "gravel": (gravel, 2.0, 2048, 0.035, 1.6, 0.02),
    "red_dirt": (red_dirt, 3.0, 2048, 0.012, 1.2, 0.01),
    "dirt_road": (dirt_road, 4.0, 2048, 0.008, 1.0, 0.01),
    "foliage": (foliage, 0.6, 1024, 0.03, 1.6, 0.01),
    "gunmetal": (gunmetal, 0.15, 1024, 0.0002, 0.6, 0.0005),
    "polymer": (polymer, 0.06, 1024, 0.0004, 0.8, 0.0005),
    "skin": (skin, 0.08, 1024, 0.0003, 0.6, 0.0008),
    "cloth": (cloth, 0.05, 1024, 0.0006, 1.0, 0.0005),
    "fleece": (fleece, 0.05, 1024, 0.0009, 1.1, 0.0006),
    "denim": (denim, 0.05, 1024, 0.0006, 1.0, 0.0005),
    "canvas": (canvas, 0.06, 1024, 0.0009, 1.0, 0.0006),
    "leather": (leather, 0.08, 1024, 0.0007, 1.0, 0.0006),
    "knit": (knit, 0.04, 1024, 0.0006, 1.0, 0.0006),
    "hair": (hair, 0.03, 1024, 0.0004, 0.8, 0.0003),
    # Models' own sets (used through their UVs as baked: "tex_<name>" materials).
    "grip_stipple": (grip_stipple, 0.03, 1024, 0.0004, 1.2, 0.0004),
    "gun_wood": (gun_wood, 0.3, 1024, 0.0002, 0.5, 0.0004),
    "bark_gum": (bark_gum, 1.2, 2048, 0.008, 1.0, 0.006),
    "bark_kapok": (bark_kapok, 2.0, 2048, 0.012, 1.3, 0.01),
    "bark_palm": (bark_palm, 0.6, 1024, 0.004, 1.0, 0.004),
    "tyre_tread": (tyre_tread, 0.6, 1024, 0.012, 1.4, 0.01),
    "grille_honeycomb": (grille_honeycomb, 0.12, 1024, 0.004, 0.8, 0.002),
    "snow": (snow, 3.0, 2048, 0.03, 0.6, 0.05),
    "rock": (rock, 2.0, 2048, 0.05, 1.3, 0.03),
    "marine_paint": (marine_paint, 3.0, 2048, 0.004, 0.6, 0.01),
}


# ======================================================================================================== baking
def setup_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    prefs = bpy.context.preferences.addons['cycles'].preferences
    for backend in ('OPTIX', 'CUDA', 'HIP', 'ONEAPI', 'METAL'):
        try:
            prefs.compute_device_type = backend
            prefs.get_devices()
            gpus = [d for d in prefs.devices if d.type != 'CPU']
            if gpus:
                for d in prefs.devices:
                    d.use = d.type != 'CPU'
                scene.cycles.device = 'GPU'
                print(f"[bake] using {backend}: {[d.name for d in gpus]}", flush=True)
                break
        except Exception:
            continue
    scene.cycles.samples = 4
    scene.cycles.use_denoising = False
    scene.render.bake.margin = 0
    scene.render.bake.use_clear = True
    scene.view_settings.view_transform = 'Standard'
    bpy.ops.mesh.primitive_plane_add(size=1.0)
    plane = bpy.context.active_object
    return scene, plane


def bake_pass(plane, g, img, src):
    g.emit(src)
    tex = g.n.get("BAKE_TARGET") or g.node('ShaderNodeTexImage')
    tex.name = "BAKE_TARGET"
    tex.image = img
    g.nt.nodes.active = tex
    bpy.ops.object.bake(type='EMIT', margin=0, use_clear=True)
    w, h = img.size
    a = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(h, w, 4)


def box_blur_wrap(x, r):
    if r < 1:
        return x
    k = 2 * r + 1
    out = x
    for axis in (0, 1):
        c = np.cumsum(np.concatenate([out.take(range(-r - 1, 0), axis=axis), out, out.take(range(0, r), axis=axis)], axis=axis), axis=axis)
        n = out.shape[axis]
        hi = c.take(range(k, k + n), axis=axis)
        lo = c.take(range(0, n), axis=axis)
        out = (hi - lo) / k
    return out


def srgb(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def save_png(name, arr):
    """arr: (h, w, 4) floats 0..1, bottom row first (Blender order). Written as-is, no colour transform."""
    h, w, _ = arr.shape
    img = bpy.data.images.new(name, w, h, alpha=True, float_buffer=False)
    img.colorspace_settings.name = 'Non-Color'
    img.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = os.path.join(OUT, name + ".png")
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def bake_material(plane, name):
    recipe, tile, res, depth, ao_k, ao_r = RECIPES[name]
    t0 = time.time()
    mat = bpy.data.materials.new(name)
    plane.data.materials.clear()
    plane.data.materials.append(mat)
    g = nodekit.Graph(mat)
    outs = recipe(g)

    def as_s(x):
        return x if isinstance(x, nodekit.S) else g.const(float(x))

    img = bpy.data.images.new("bake_" + name, res, res, alpha=True, float_buffer=True)
    img.colorspace_settings.name = 'Non-Color'

    color = bake_pass(plane, g, img, outs["color"])[..., :3].copy()
    packed = bake_pass(plane, g, img, g.comb(as_s(outs["mask"]), as_s(outs["rough"]), as_s(outs["metal"])))[..., :3].copy()
    height = bake_pass(plane, g, img, as_s(outs["height"]))[..., 0].copy()
    bpy.data.images.remove(img)

    # Normal from height (wrap-around central differences, so the normal map tiles too).
    hm = height.astype(np.float64) * depth
    px = tile / res
    dx = (np.roll(hm, -1, axis=1) - np.roll(hm, 1, axis=1)) / (2 * px)
    dy = (np.roll(hm, -1, axis=0) - np.roll(hm, 1, axis=0)) / (2 * px)
    nrm = np.stack([-dx, -dy, np.ones_like(hm)], axis=-1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)

    # Cavity AO: how far below its blurred neighbourhood each texel sits, at two radii.
    r1 = max(1, int(round(ao_r / px)))
    cav = (box_blur_wrap(hm, r1) - hm) + 0.5 * (box_blur_wrap(hm, r1 * 4) - hm)
    ao = np.clip(1.0 - ao_k * np.maximum(cav, 0.0) / max(depth * 0.25, 1e-6), 0.25, 1.0)

    ones = np.ones_like(height)
    save_png(name + "_albedo", np.dstack([srgb(color), np.clip(packed[..., 0], 0, 1)]))
    save_png(name + "_normal", np.dstack([nrm * 0.5 + 0.5, ones]))
    save_png(name + "_orm", np.dstack([ao, np.clip(packed[..., 1], 0.02, 1), np.clip(packed[..., 2], 0, 1), ones]))

    # Small preview (lit with a fixed light) for the contact sheet.
    light = np.clip(np.einsum('ijk,k->ij', nrm, np.array([-0.5, 0.5, 0.7]) / np.linalg.norm([-0.5, 0.5, 0.7])), 0, 1)
    prev = srgb(color * (0.35 + 0.75 * light)[..., None] * ao[..., None])
    step = max(1, res // 256)
    PREVIEWS[name] = prev[::step, ::step][:256, :256]
    bpy.data.materials.remove(mat)
    print(f"[bake] {name}: {time.time() - t0:.1f}s", flush=True)


PREVIEWS = {}


def preview_from_pngs(name):
    """The contact-sheet tile of a set baked earlier, from its PNGs (so baking a few sets keeps the whole sheet)."""
    def load(suffix):
        path = os.path.join(OUT, f"{name}_{suffix}.png")
        if not os.path.exists(path):
            return None
        im = bpy.data.images.load(path)
        im.colorspace_settings.name = 'Non-Color'
        w, h = im.size
        a = np.empty(w * h * 4, np.float32)
        im.pixels.foreach_get(a)
        bpy.data.images.remove(im)
        return a.reshape(h, w, 4)
    alb, nrm, orm = load("albedo"), load("normal"), load("orm")
    if alb is None or nrm is None or orm is None:
        return
    lin = lambda x: np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)
    n = nrm[..., :3] * 2 - 1
    light = np.clip(np.einsum('ijk,k->ij', n, np.array([-0.5, 0.5, 0.7]) / np.linalg.norm([-0.5, 0.5, 0.7])), 0, 1)
    prev = srgb(lin(alb[..., :3]) * (0.35 + 0.75 * light)[..., None] * orm[..., :1])
    step = max(1, alb.shape[0] // 256)
    PREVIEWS[name] = prev[::step, ::step][:256, :256]


def contact_sheet():
    for name in RECIPES:
        if name not in PREVIEWS:
            preview_from_pngs(name)
    names = [n for n in RECIPES if n in PREVIEWS]
    if not names:
        return
    cols = 4
    rows = (len(names) + cols - 1) // cols
    sheet = np.zeros((rows * 256, cols * 256, 4), np.float32)
    sheet[..., 3] = 1
    for i, n in enumerate(names):
        r, c = divmod(i, cols)
        p = PREVIEWS[n]
        y0 = (rows - 1 - r) * 256
        sheet[y0:y0 + p.shape[0], c * 256:c * 256 + p.shape[1], :3] = p
    os.makedirs(os.path.join(PROJECT, "Tools", "previews"), exist_ok=True)
    img = bpy.data.images.new("sheet", sheet.shape[1], sheet.shape[0], alpha=True)
    img.colorspace_settings.name = 'Non-Color'
    img.pixels.foreach_set(sheet.ravel())
    img.filepath_raw = os.path.join(PROJECT, "Tools", "previews", "textures.png")
    img.file_format = 'PNG'
    img.save()
    print("[bake] order:", names, flush=True)


def main():
    # `blender -b -P bake_textures.py -- names...` or `python3 bake_textures.py names...` (bpy as a module)
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    names = argv or list(RECIPES)
    if names == ["sheet"]:
        names = []             # just rebuild the contact sheet from the PNGs
    os.makedirs(OUT, exist_ok=True)
    _, plane = setup_scene()
    for n in names:
        try:
            bake_material(plane, n)
        except Exception as e:  # keep going so one broken recipe doesn't stop the rest
            import traceback
            traceback.print_exc()
            print(f"[bake] FAILED {n}: {e}", flush=True)
    contact_sheet()
    print("[bake] done", flush=True)


main()
