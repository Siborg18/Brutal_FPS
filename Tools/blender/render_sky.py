"""
Renders the HDR sky cubemaps for BRUTAL FPS with Cycles: Blender's physically based sky (multiple scattering) with
the sun where the game's sun is, under a procedurally shaded cloud layer.

    blender -b --factory-startup -P Tools/blender/render_sky.py -- yard_night|yard|outback|sea [face_size]

Writes, in Assets/Sky:
  <preset>_{px,nx,py,ny,pz,nz}.hdr   linear HDR faces in OpenGL cubemap layout (game axes, +Y up), sun disc removed
  <preset>.json                      sun irradiance (rgb) and the sky's irradiance on a horizontal surface, both in
                                     the same units as the faces, so the game can light the scene to match the sky
and a tone-mapped preview cross in Tools/previews.

"yard_night" (what the container yard uses) is a night sky instead: no sun, a low cloud deck lit orange from below
by the port's sodium lights, an orange light-pollution glow along the horizon, and a few stars in the gaps. The game
lights that area with its floodlights; the sky only gives the dim ambient light and the reflections.

The sun disc is left out of the faces on purpose: the game lights with the sun directly (with shadows) and draws
the disc itself, so keeping it in the cubemap would count the sun twice in the sky lighting.
"""
import json
import math
import os
import sys
import time

import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(PROJECT, "Assets", "Sky")


def game_to_blender(v):
    x, y, z = v
    return Vector((x, -z, y))


PRESETS = {
    # Overcast over the container yard: a broken stratocumulus deck, grey and heavy, with the sun glowing through.
    "yard": dict(
        sun=(0.35, 0.85, 0.4), air=1.2, aerosol=4.0, ozone=1.0, ground=(0.09, 0.09, 0.09),
        layers=[
            dict(alt=1100.0, scale=1 / 2200.0, coverage=0.30, softness=0.30, detail=8, seed=3.0, lit=1.25, shade=0.45,
                 tint=(0.95, 0.96, 1.0), shade_tint=(0.78, 0.80, 0.86), relief=2.2, stretch=1.3),
            dict(alt=900.0, scale=1 / 900.0, coverage=0.52, softness=0.18, detail=6, seed=8.0, lit=1.0, shade=0.38,
                 tint=(0.93, 0.94, 0.97), shade_tint=(0.70, 0.72, 0.78), relief=2.5, stretch=1.0),
        ],
    ),
    # A heavy grey overcast over the open sea (the container ship): a near-solid stratocumulus deck, soft-edged, with
    # a few thinner patches where the sun glows through, and a dark sea below.
    "sea": dict(
        sun=(0.35, 0.85, 0.4), air=1.3, aerosol=5.0, ozone=1.0, ground=(0.03, 0.045, 0.055),
        layers=[
            dict(alt=1400.0, scale=1 / 2800.0, coverage=0.22, softness=0.40, detail=7, seed=41.0, lit=1.15, shade=0.55,
                 tint=(0.95, 0.96, 0.99), shade_tint=(0.74, 0.77, 0.82), relief=1.6, stretch=1.0),
            dict(alt=850.0, scale=1 / 1200.0, coverage=0.46, softness=0.30, detail=6, seed=17.0, lit=0.95, shade=0.5,
                 tint=(0.92, 0.93, 0.96), shade_tint=(0.66, 0.69, 0.75), relief=1.8, stretch=1.0),
        ],
    ),
    # Hard midday sun over the outback: deep blue, scattered fair-weather cumulus and a few high wisps of cirrus.
    "outback": dict(
        sun=(-0.45, 0.72, -0.4), air=1.0, aerosol=0.5, ozone=1.0, ground=(0.28, 0.11, 0.05),
        layers=[
            dict(alt=7500.0, scale=1 / 12000.0, coverage=0.50, softness=0.25, detail=6, seed=27.0, lit=2.2, shade=1.5,
                 tint=(1.0, 1.0, 1.0), shade_tint=(0.9, 0.93, 1.0), relief=0.6, stretch=6.0, opacity=0.6),
            dict(alt=1600.0, scale=1 / 2600.0, coverage=0.56, softness=0.09, detail=8, seed=11.0, lit=4.5, shade=1.1,
                 tint=(1.0, 0.99, 0.97), shade_tint=(0.72, 0.78, 0.9), relief=4.0, stretch=1.0),
        ],
    ),
}

PRESETS["yard_night"] = dict(
    night=True,
    zenith=(0.010, 0.011, 0.018),          # overhead, between the clouds
    glow=(0.30, 0.13, 0.045),              # sodium light pollution along the horizon
    glow_height=0.10,                      # e-folding height of the glow (sine of elevation)
    cloud_lit=(0.115, 0.050, 0.020),       # cloud base lit from below by the port
    cloud_dark=(0.030, 0.016, 0.010),
    ground=(0.02, 0.016, 0.012),
)

# OpenGL cubemap faces in game space: (name, forward, up). Rendered with a normal camera and then mirrored
# left-right, which gives the s/t orientation glTexImage2D(GL_TEXTURE_CUBE_MAP_*) expects for images uploaded
# top row first.
FACES = [
    ("px", (1, 0, 0), (0, 1, 0)),
    ("nx", (-1, 0, 0), (0, 1, 0)),
    ("py", (0, 1, 0), (0, 0, -1)),
    ("ny", (0, -1, 0), (0, 0, 1)),
    ("pz", (0, 0, 1), (0, 1, 0)),
    ("nz", (0, 0, -1), (0, 1, 0)),
]


def face_rotation(fwd, up):
    f = game_to_blender(fwd).normalized()
    u = game_to_blender(up).normalized()
    right = f.cross(u)
    return Matrix((right, u, -f)).transposed()


# ------------------------------------------------------------------------------------------------ scene
def setup(preset):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    prefs = bpy.context.preferences.addons['cycles'].preferences
    for backend in ('OPTIX', 'CUDA', 'HIP', 'ONEAPI', 'METAL'):
        try:
            prefs.compute_device_type = backend
            prefs.get_devices()
            if any(d.type != 'CPU' for d in prefs.devices):
                for d in prefs.devices:
                    d.use = d.type != 'CPU'
                scene.cycles.device = 'GPU'
                break
        except Exception:
            continue
    scene.cycles.samples = 64
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 4
    scene.cycles.transparent_max_bounces = 16
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    scene.render.image_settings.file_format = 'HDR'

    world = bpy.data.worlds.new("Sky")
    scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    nt.nodes.clear()
    sky = nt.nodes.new("ShaderNodeTexSky")
    sky.sky_type = 'MULTIPLE_SCATTERING'
    sky.sun_disc = True
    sky.sun_size = math.radians(0.545)
    sky.altitude = 300.0
    sky.air_density = preset["air"]
    sky.aerosol_density = preset["aerosol"]
    sky.ozone_density = preset["ozone"]
    bg = nt.nodes.new("ShaderNodeBackground")
    out = nt.nodes.new("ShaderNodeOutputWorld")
    nt.links.new(sky.outputs[0], bg.inputs[0])
    nt.links.new(bg.outputs[0], out.inputs[0])
    cam_data = bpy.data.cameras.new("cam")
    cam_data.sensor_fit = 'HORIZONTAL'
    cam_data.angle = math.pi / 2
    cam_data.clip_start = 0.5
    cam_data.clip_end = 1.0e6
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    return scene, sky, cam


def render_array(scene, size, samples=None, denoise=None):
    """Renders the current camera and returns linear RGBA, rows bottom-up (Blender order)."""
    old = (scene.cycles.samples, scene.cycles.use_denoising)
    if samples is not None:
        scene.cycles.samples = samples
    if denoise is not None:
        scene.cycles.use_denoising = denoise
    scene.render.resolution_x = scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    path = os.path.join(OUT, "_tmp.hdr")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    a = np.empty(size * size * 4, np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    os.remove(path)
    scene.cycles.samples, scene.cycles.use_denoising = old
    return a.reshape(size, size, 4)


def pixel_dirs(size, rot, fov=math.pi / 2):
    """World directions (Blender space) and solid angles of each pixel of a square perspective render."""
    t = math.tan(fov / 2)
    c = (np.arange(size) + 0.5) / size * 2 - 1
    x, y = np.meshgrid(c * t, c * t)          # rows bottom-up, like Blender's pixel buffer
    d = np.stack([x, y, -np.ones_like(x)], -1)
    r2 = (x * x + y * y + 1.0)
    omega = (2 * t / size) ** 2 / r2 ** 1.5
    m = np.array(rot)
    w = d @ m.T
    w /= np.linalg.norm(w, axis=-1, keepdims=True)
    return w, omega


def calibrate_sun(scene, sky, cam, sun_b):
    """Sets sun_elevation/rotation so the sky's sun lands on sun_b, whatever Blender's angle convention is."""
    sky.sun_elevation = math.asin(sun_b.z / sun_b.length)
    target = math.atan2(sun_b.y, sun_b.x)

    def brightest(rotation):
        sky.sun_rotation = rotation
        best = (-1.0, None)
        for _, fwd, up in FACES:
            rot = face_rotation(fwd, up)
            cam.matrix_world = rot.to_4x4()
            a = render_array(scene, 96, samples=2, denoise=False)
            lum = a[..., :3].sum(-1)
            i = np.unravel_index(np.argmax(lum), lum.shape)
            if lum[i] > best[0]:
                dirs, _ = pixel_dirs(96, rot)
                best = (float(lum[i]), Vector(dirs[i]))
        return best[1]

    size = sky.sun_size
    sky.sun_size = math.radians(6.0)   # big enough that low-res calibration renders can't miss it
    d0, d1 = brightest(0.0), brightest(1.0)
    a0, a1 = math.atan2(d0.y, d0.x), math.atan2(d1.y, d1.x)
    sign = 1.0 if math.sin(a1 - a0) > 0 else -1.0
    sky.sun_rotation = sign * (target - a0)
    d = brightest(sky.sun_rotation)
    sky.sun_size = size
    err =math.degrees(math.acos(max(-1.0, min(1.0, d.normalized().dot(sun_b.normalized())))))
    print(f"[sky] sun calibrated: rotation {math.degrees(sky.sun_rotation):.1f} deg, error {err:.2f} deg "
          f"(a0 {math.degrees(a0):.1f}, a1 {math.degrees(a1):.1f}, target {math.degrees(target):.1f}, got {tuple(round(x, 3) for x in d)}, want {tuple(round(x, 3) for x in sun_b.normalized())})", flush=True)


def measure_sun(scene, sky, cam, sun_b):
    """Irradiance of the sun disc alone (rgb): a narrow view of the sun with the disc on, minus the same without it."""
    fov = math.radians(2.0)
    cam.data.angle = fov
    fwd = sun_b.normalized()
    up = Vector((0, 0, 1)) if abs(fwd.z) < 0.99 else Vector((0, 1, 0))
    right = fwd.cross(up).normalized()
    up = right.cross(fwd)
    rot = Matrix((right, up, -fwd)).transposed()
    cam.matrix_world = rot.to_4x4()
    _, omega = pixel_dirs(128, rot, fov)
    sky.sun_disc = True
    with_disc = render_array(scene, 128, samples=16, denoise=False)
    sky.sun_disc = False
    without = render_array(scene, 128, samples=16, denoise=False)
    cam.data.angle = math.pi / 2
    diff = np.maximum(with_disc[..., :3] - without[..., :3], 0)
    e = (diff * omega[..., None]).sum((0, 1))
    print(f"[sky] sun irradiance {e}", flush=True)
    return e


# ------------------------------------------------------------------------------------------------ clouds
def add_ground(color):
    bpy.ops.mesh.primitive_plane_add(size=2.0e6, location=(0, 0, 0))
    plane = bpy.context.active_object
    mat = bpy.data.materials.new("ground")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = 1.0
    plane.data.materials.append(mat)


def add_cloud_layer(layer, sun_b, sky_radiance, sun_color):
    """
    A flat cloud deck at layer['alt'] metres. The shape is fbm noise thresholded by coverage. Lighting is the
    classic 2D trick: compare the density here with the density a little way towards the sun - where it drops,
    the cloud faces the sun and is lit; where it rises, light has to get through more cloud and it is in shade.
    Brightness is set relative to the measured sky radiance, so the clouds sit naturally in the HDR sky.
    """
    size = 1.0e6
    bpy.ops.mesh.primitive_plane_add(size=size, location=(0, 0, layer["alt"]))
    plane = bpy.context.active_object
    mat = bpy.data.materials.new("clouds")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    N = nt.nodes.new
    L = nt.links.new

    def math_node(op, a=None, b=None, clamp=False):
        n = N("ShaderNodeMath"); n.operation = op; n.use_clamp = clamp
        for i, v in enumerate((a, b)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                L(v, n.inputs[i])
        return n.outputs[0]

    tc = N("ShaderNodeTexCoord")
    sep = N("ShaderNodeSeparateXYZ")
    L(tc.outputs["Object"], sep.inputs[0])
    k = layer["scale"]   # object coordinates are metres
    px = math_node('MULTIPLY', sep.outputs[0], k / layer["stretch"])
    py = math_node('MULTIPLY', sep.outputs[1], k)

    # Horizontal direction towards the sun, in noise units (a short step).
    sun_xy = Vector((sun_b.x, sun_b.y))
    sun_xy = sun_xy.normalized() if sun_xy.length > 1e-4 else Vector((1, 0))
    step = 0.06

    def density(dx, dy):
        comb = N("ShaderNodeCombineXYZ")
        L(math_node('ADD', px, dx / layer["stretch"]), comb.inputs[0])
        L(math_node('ADD', py, dy), comb.inputs[1])
        n = N("ShaderNodeTexNoise")
        n.noise_dimensions = '4D'
        n.inputs["W"].default_value = layer["seed"]
        n.inputs["Scale"].default_value = 1.0
        n.inputs["Detail"].default_value = layer["detail"]
        n.inputs["Roughness"].default_value = 0.6
        n.inputs["Distortion"].default_value = 0.2
        L(comb.outputs[0], n.inputs["Vector"])
        return n.outputs["Factor"]

    d0 = density(0.0, 0.0)
    d1 = density(sun_xy.x * step, sun_xy.y * step)
    cov = layer["coverage"]
    alpha = N("ShaderNodeMapRange"); alpha.interpolation_type = 'SMOOTHSTEP'
    L(d0, alpha.inputs["Value"]); alpha.inputs["From Min"].default_value = cov
    alpha.inputs["From Max"].default_value = cov + layer["softness"]
    alpha = alpha.outputs["Result"]
    alpha = math_node('MULTIPLY', alpha, layer.get("opacity", 1.0))
    # Lit where density falls towards the sun; thicker cloud (higher density) is darker underneath.
    slope = math_node('SUBTRACT', d0, d1)
    lit = N("ShaderNodeMath"); lit.operation = 'MULTIPLY_ADD'; lit.use_clamp = True
    L(slope, lit.inputs[0]); lit.inputs[1].default_value = layer["relief"] * 4.0; lit.inputs[2].default_value = 0.55
    thick = N("ShaderNodeMapRange")
    L(d0, thick.inputs["Value"]); thick.inputs["From Min"].default_value = cov
    thick.inputs["From Max"].default_value = cov + 0.35
    thick.inputs["To Min"].default_value = 1.0; thick.inputs["To Max"].default_value = 0.55
    shade = math_node('MULTIPLY', lit.outputs[0], thick.outputs["Result"])

    s = sky_radiance
    mix = N("ShaderNodeMix"); mix.data_type = 'RGBA'
    L(shade, mix.inputs["Factor"])
    sc = np.array(layer["shade_tint"]) * layer["shade"] * s
    lc = np.array(layer["tint"]) * np.array(sun_color) * layer["lit"] * s
    mix.inputs[6].default_value = (*sc, 1.0)
    mix.inputs[7].default_value = (*lc, 1.0)
    em = N("ShaderNodeEmission")
    L(mix.outputs[2], em.inputs["Color"])
    tr = N("ShaderNodeBsdfTransparent")
    mixs = N("ShaderNodeMixShader")
    L(alpha, mixs.inputs[0])
    L(tr.outputs[0], mixs.inputs[1])
    L(em.outputs[0], mixs.inputs[2])
    out = N("ShaderNodeOutputMaterial")
    L(mixs.outputs[0], out.inputs["Surface"])
    plane.data.materials.append(mat)
    plane.visible_shadow = False
    plane.visible_diffuse = False
    plane.visible_glossy = False


def night_world(scene, preset):
    """The night sky as a world shader (emission only): zenith colour, an orange glow towards the horizon that is
    stronger in some directions (where the port and the town are), a low cloud deck lit from below, and stars."""
    world = scene.world
    nt = world.node_tree
    nt.nodes.clear()
    N, L = nt.nodes.new, nt.links.new

    def m(op, a, b=None, c=None, clamp=False):
        n = N("ShaderNodeMath"); n.operation = op; n.use_clamp = clamp
        for i, v in enumerate((a, b, c)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                L(v, n.inputs[i])
        return n.outputs[0]

    def rgb(c, k=1.0):
        n = N("ShaderNodeRGB"); n.outputs[0].default_value = (c[0] * k, c[1] * k, c[2] * k, 1.0)
        return n.outputs[0]

    def mix(fac, a, b, blend='MIX'):
        n = N("ShaderNodeMix"); n.data_type = 'RGBA'; n.blend_type = blend; n.clamp_factor = True
        if isinstance(fac, (int, float)):
            n.inputs["Factor"].default_value = fac
        else:
            L(fac, n.inputs["Factor"])
        L(a, n.inputs[6]); L(b, n.inputs[7])
        return n.outputs[2]

    tc = N("ShaderNodeTexCoord")
    d = tc.outputs["Generated"]                    # view direction (Blender: +Z up)
    sep = N("ShaderNodeSeparateXYZ"); L(d, sep.inputs[0])
    z = sep.outputs[2]
    zc = m('MAXIMUM', z, 0.0)
    # Horizon glow, brighter in some directions.
    az_noise = N("ShaderNodeTexNoise"); az_noise.inputs["Scale"].default_value = 1.3; az_noise.inputs["Detail"].default_value = 2
    flat = N("ShaderNodeCombineXYZ"); L(sep.outputs[0], flat.inputs[0]); L(sep.outputs[1], flat.inputs[1])
    L(flat.outputs[0], az_noise.inputs["Vector"])
    strength = m('MULTIPLY_ADD', az_noise.outputs["Factor"], 1.6, -0.25)
    strength = m('MAXIMUM', strength, 0.35)
    glow = m('MULTIPLY', m('EXPONENT', m('DIVIDE', zc, -preset["glow_height"])), strength)
    glow_wide = m('MULTIPLY', m('EXPONENT', m('DIVIDE', zc, -0.45)), 0.25)
    # zenith + glow colour x glow amount
    sky_col = N("ShaderNodeMix"); sky_col.data_type = 'RGBA'; sky_col.blend_type = 'ADD'; sky_col.clamp_factor = False
    L(m('ADD', glow, glow_wide), sky_col.inputs["Factor"]); L(rgb(preset["zenith"]), sky_col.inputs[6]); L(rgb(preset["glow"]), sky_col.inputs[7])
    sky = sky_col.outputs[2]
    # Stars, only well above the horizon, faint through the haze.
    vor = N("ShaderNodeTexVoronoi"); vor.inputs["Scale"].default_value = 260.0; L(d, vor.inputs["Vector"])
    star_tex = N("ShaderNodeTexNoise"); star_tex.inputs["Scale"].default_value = 300.0; L(d, star_tex.inputs["Vector"])
    star = m('MULTIPLY', m('LESS_THAN', vor.outputs["Distance"], 0.03), m('GREATER_THAN', star_tex.outputs["Factor"], 0.62))
    star = m('MULTIPLY', star, m('MULTIPLY', m('SUBTRACT', 1.0, m('EXPONENT', m('DIVIDE', zc, -0.25))), 0.25))
    sky = mix(star, sky, rgb((0.25, 0.24, 0.22)), 'ADD')
    # Cloud deck: project the direction onto a plane (distance to the cloud base ~ 1/z), mottled cover with gaps.
    zz = m('MAXIMUM', z, 0.03)
    proj = N("ShaderNodeVectorMath"); proj.operation = 'SCALE'; L(d, proj.inputs[0])
    L(m('DIVIDE', 1.0, zz), proj.inputs["Scale"])
    cl = N("ShaderNodeTexNoise"); cl.inputs["Scale"].default_value = 0.9; cl.inputs["Detail"].default_value = 8
    cl.inputs["Roughness"].default_value = 0.62; cl.inputs["Distortion"].default_value = 0.3
    L(proj.outputs[0], cl.inputs["Vector"])
    cover = N("ShaderNodeMapRange"); cover.interpolation_type = 'SMOOTHSTEP'
    L(cl.outputs["Factor"], cover.inputs["Value"]); cover.inputs["From Min"].default_value = 0.40; cover.inputs["From Max"].default_value = 0.58
    cover = cover.outputs["Result"]
    # Thicker cloud is darker underneath; the cloud base is brighter low in the sky (lit by more of the port).
    lit = N("ShaderNodeMapRange")
    L(cl.outputs["Factor"], lit.inputs["Value"]); lit.inputs["From Min"].default_value = 0.45; lit.inputs["From Max"].default_value = 0.85
    lit.inputs["To Min"].default_value = 1.0; lit.inputs["To Max"].default_value = 0.35
    cloud = mix(lit.outputs["Result"], rgb(preset["cloud_dark"]), rgb(preset["cloud_lit"]))
    low = m('ADD', 1.0, m('MULTIPLY', m('EXPONENT', m('DIVIDE', zc, -0.2)), 1.2))
    cloud_scaled = N("ShaderNodeMix"); cloud_scaled.data_type = 'RGBA'; cloud_scaled.blend_type = 'MULTIPLY'; cloud_scaled.clamp_factor = True
    cloud_scaled.inputs["Factor"].default_value = 1.0
    L(cloud, cloud_scaled.inputs[6])
    lowc = N("ShaderNodeCombineXYZ"); L(low, lowc.inputs[0]); L(low, lowc.inputs[1]); L(low, lowc.inputs[2])
    L(lowc.outputs[0], cloud_scaled.inputs[7])
    # Clouds fade into the horizon haze.
    vis = m('MULTIPLY', cover, m('SMOOTH_MAX', m('MULTIPLY', zc, 6.0), 0.0, 0.1))
    vis = m('MINIMUM', vis, 1.0)
    col = mix(vis, sky, cloud_scaled.outputs[2])
    # Below the horizon: the dark ground, with the glow fading into it.
    below = m('LESS_THAN', z, 0.0)
    col = mix(below, col, rgb(preset["ground"]))
    bg = N("ShaderNodeBackground"); L(col, bg.inputs[0]); bg.inputs[1].default_value = 1.0
    out = N("ShaderNodeOutputWorld"); L(bg.outputs[0], out.inputs[0])


# ------------------------------------------------------------------------------------------------ output
def render_faces(scene, cam, name, size):
    e_up = np.zeros(3)
    for face, fwd, up in FACES:
        rot = face_rotation(fwd, up)
        cam.matrix_world = Matrix.Translation((0, 0, 2.0)) @ rot.to_4x4()
        t0 = time.time()
        a = render_array(scene, size)
        dirs, omega = pixel_dirs(size, rot)
        cosz = np.maximum(dirs[..., 2], 0.0)
        e_up += (a[..., :3] * (cosz * omega)[..., None]).sum((0, 1))
        a = a[:, ::-1, :].copy()   # mirror left-right (see FACES)
        img = bpy.data.images.new("face", size, size, alpha=True, float_buffer=True)
        img.colorspace_settings.name = 'Linear Rec.709'
        img.pixels.foreach_set(a.ravel())
        img.filepath_raw = os.path.join(OUT, f"{name}_{face}.hdr")
        img.file_format = 'HDR'
        img.save()
        bpy.data.images.remove(img)
        print(f"[sky] {name}_{face}: {time.time() - t0:.1f}s mean={a[..., :3].mean():.3f} max={a[..., :3].max():.2f}", flush=True)
    return e_up


def preview(name, exposure):
    faces = {}
    for f, _, _ in FACES:
        img = bpy.data.images.load(os.path.join(OUT, f"{name}_{f}.hdr"))
        s = img.size[0]
        a = np.empty(s * s * 4, np.float32)
        img.pixels.foreach_get(a)
        bpy.data.images.remove(img)
        a = a.reshape(s, s, 4)[::-1]  # top row first
        step = max(1, s // 256)
        faces[f] = a[::step, ::step, :3][:256, :256][:, ::-1]  # undo the mirror for viewing
    t = 256
    cross = np.zeros((3 * t, 4 * t, 3), np.float32)
    place = {"py": (0, 1), "nx": (1, 0), "nz": (1, 1), "px": (1, 2), "pz": (1, 3), "ny": (2, 1)}
    for f, (r, c) in place.items():
        cross[r * t:(r + 1) * t, c * t:(c + 1) * t] = faces[f]
    m = cross * exposure
    m = (m * (2.51 * m + 0.03)) / (m * (2.43 * m + 0.59) + 0.14)   # ACES fit, as in the game
    m = np.clip(m, 0, 1) ** (1 / 2.2)
    rgba = np.concatenate([m[::-1], np.ones((*m.shape[:2], 1), np.float32)], axis=-1)
    img = bpy.data.images.new("prev", rgba.shape[1], rgba.shape[0], alpha=True)
    img.colorspace_settings.name = 'Non-Color'
    img.pixels.foreach_set(rgba.ravel())
    os.makedirs(os.path.join(PROJECT, "Tools", "previews"), exist_ok=True)
    img.filepath_raw = os.path.join(PROJECT, "Tools", "previews", f"sky_{name}.png")
    img.file_format = 'PNG'
    img.save()


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    name = argv[0] if argv else "outback"
    size = int(argv[1]) if len(argv) > 1 else 1024
    preset = PRESETS[name]
    os.makedirs(OUT, exist_ok=True)
    if preset.get("night"):
        scene, sky, cam = setup(dict(air=1.0, aerosol=1.0, ozone=1.0))
        night_world(scene, preset)
        scene.cycles.samples = 32
        e_up = render_faces(scene, cam, name, size)
        info = {"sunDirection": [0.0, 1.0, 0.0], "sunIrradiance": [0.0, 0.0, 0.0],
                "skyIrradianceUp": [float(x) for x in e_up], "night": True}
        with open(os.path.join(OUT, f"{name}.json"), "w") as f:
            json.dump(info, f, indent=2)
        print(f"[sky] {info}", flush=True)
        preview(name, 1.0 / max(float(e_up.mean()) / math.pi * 6.0, 1e-6))
        print("[sky] done", flush=True)
        return
    scene, sky, cam = setup(preset)
    sun_b = game_to_blender(Vector(preset["sun"]).normalized())
    calibrate_sun(scene, sky, cam, sun_b)
    sun_e = measure_sun(scene, sky, cam, sun_b)          # leaves the disc off
    sun_color = sun_e / max(sun_e.max(), 1e-9)

    # Clear-sky radiance near the horizon/mid sky, to set the clouds' brightness against.
    probe = []
    for _, fwd, up in FACES[:2] + FACES[4:]:
        cam.matrix_world = face_rotation(fwd, up).to_4x4()
        probe.append(render_array(scene, 32, samples=4, denoise=False)[16:, :, :3].mean())
    sky_radiance = float(np.mean(probe))
    print(f"[sky] clear sky radiance ~{sky_radiance:.4f}", flush=True)

    add_ground(preset["ground"])
    # The sun disc is off in the sky texture now, so a sun lamp of the measured irradiance lights the ground.
    lamp = bpy.data.lights.new("sun", 'SUN')
    lamp.energy = float(sun_e.max())
    lamp.color = tuple(float(c) for c in sun_color)
    lamp.angle = math.radians(0.545)
    lamp_obj = bpy.data.objects.new("sun", lamp)
    lamp_obj.rotation_euler = sun_b.normalized().to_track_quat('Z', 'Y').to_euler()
    scene.collection.objects.link(lamp_obj)
    for layer in preset["layers"]:
        add_cloud_layer(layer, sun_b, sky_radiance, sun_color)
    e_up = render_faces(scene, cam, name, size)
    info = {
        "sunDirection": list(preset["sun"]),
        "sunIrradiance": [float(x) for x in sun_e],
        "skyIrradianceUp": [float(x) for x in e_up],
        "skyRadiance": sky_radiance,
    }
    with open(os.path.join(OUT, f"{name}.json"), "w") as f:
        json.dump(info, f, indent=2)
    print(f"[sky] {info}", flush=True)
    preview(name, 1.0 / max(float(e_up.mean()) / math.pi * 1.5, 1e-6))
    print("[sky] done", flush=True)


main()
