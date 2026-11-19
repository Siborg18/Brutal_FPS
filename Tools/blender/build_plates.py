"""
Builds the Transit vans' number plates: the "van_plates" texture set (an atlas of Albanian, Romanian, German and Dutch
plates, drawn flat in Blender and rendered with an orthographic camera, with a little road dirt and the raised letters
in the normal map) and Assets/Models/transit_plate_<name>.glb for each: the front and rear plates of one van, laid on
the surface of build_transit.py's body. The registrations are made up. Run build_transit.py first for the van itself.

    blender -b --factory-startup -P Tools/blender/build_plates.py

The lettering is Windows' Bahnschrift (a DIN 1451 face, the nearest thing here to the plates' own), thickened. The
contact sheet of the atlas is Tools/previews/models/van_plates.png.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import modelkit as K  # noqa: E402
import build_transit as T  # noqa: E402

FONT = r"C:\Windows\Fonts\bahnschrift.ttf"

# name, country, groups of the registration (one van's plates; the game picks them in Level.BuildVans).
PLATES = [
    ("al1", "AL", ("EL", "482", "KC")),
    ("al2", "AL", ("TR", "715", "BD")),
    ("ro1", "RO", ("CJ", "27", "VNM")),
    ("ro2", "RO", ("TM", "08", "RDC")),
    ("de1", "DE", ("HH", "AK", "2741")),
    ("de2", "DE", ("M", "QT", "5082")),
    ("nl1", "NL", ("VKN", "38", "T")),
    ("al3", "AL", ("DR", "306", "HP")),
    ("al4", "AL", ("KO", "941", "TM")),
    ("ro3", "RO", ("BV", "63", "LKS")),
    ("de3", "DE", ("K", "RM", "6194")),
    ("nl2", "NL", ("GRP", "61", "D")),
    ("nl3", "NL", ("SFB", "07", "J")),
]

# The atlas: 2048 x 2048, two columns of eight cells. 1 unit = 1000 pixels, a plate is 1 unit wide (520 x 110 mm).
ATLAS_W, ATLAS_H = 2.048, 2.048
PLATE_W, PLATE_H = 1.0, 110 / 520
CELL_W, CELL_H = 1.024, 0.256

BLACK = (0.02, 0.02, 0.02)
WHITE = (0.95, 0.95, 0.93)
EU_BLUE = (0.04, 0.30, 0.78)
EU_YELLOW = (1.0, 0.8, 0.0)
NL_YELLOW = (1.0, 0.8, 0.2)
BAND = 0.085                     # the blue band's width


def cell_origin(i):
    col, row = i // 8, i % 8
    return (col * CELL_W + (CELL_W - PLATE_W) / 2, row * CELL_H + (CELL_H - PLATE_H) / 2)


def flat(color):
    name = "plate_%.2f_%.2f_%.2f" % color
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = (*K.srgb_to_linear(color), 1.0)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    return m


class Canvas:
    """Draws in plate coordinates (0..1 across, 0..PLATE_H up); later shapes go on top."""

    def __init__(self, x, y):
        self.x, self.y, self.z = x, y, 0.0

    def _next(self):
        self.z += 0.001
        return self.z

    def poly(self, pts, color):
        z = self._next()
        bm = bmesh.new()
        bm.faces.new([bm.verts.new((self.x + px, self.y + py, z)) for px, py in pts])
        me = bpy.data.meshes.new("shape")
        bm.to_mesh(me)
        bm.free()
        me.materials.append(flat(color))
        bpy.context.scene.collection.objects.link(bpy.data.objects.new("shape", me))

    def rect(self, x0, y0, x1, y1, color):
        self.poly([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], color)

    def rrect(self, x0, y0, x1, y1, r, color):
        self.poly(T.round_poly([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], r, 6), color)

    def disc(self, cx, cy, r, color, n=40):
        self.poly([(cx + r * math.cos(math.tau * i / n), cy + r * math.sin(math.tau * i / n)) for i in range(n)], color)

    def star(self, cx, cy, r, color):
        self.poly([(cx + (r if i % 2 == 0 else r * 0.4) * math.sin(math.tau * i / 10), cy + (r if i % 2 == 0 else r * 0.4) * math.cos(math.tau * i / 10)) for i in range(10)], color)

    def _text_object(self, body, color):
        cu = bpy.data.curves.new("text", 'FONT')
        cu.body = body
        cu.font = bpy.data.fonts.load(FONT, check_existing=True)
        cu.size = 1.0
        cu.materials.append(flat(color))
        o = bpy.data.objects.new("text", cu)
        bpy.context.scene.collection.objects.link(o)
        return o

    def text(self, body, cx, cy, height, color, bold=0.0034, stretch=1.0):
        """Text centred on (cx, cy) with capitals height high. Thickened by drawing it again round a circle of radius
        bold (the curve's own offset leaves spikes at sharp corners), which also rounds the corners as on the plates."""
        z = self._next()
        o = self._text_object(body, color)
        bpy.context.view_layer.update()
        xs = [c[0] for c in o.bound_box]
        ys = [c[1] for c in o.bound_box]
        s = (height - 2 * bold) / (max(ys) - min(ys))
        px = self.x + cx - (min(xs) + max(xs)) / 2 * s * stretch
        py = self.y + cy - (min(ys) + max(ys)) / 2 * s
        o.scale = (s * stretch, s, 1.0)
        o.location = (px, py, z)
        for i in range(12):
            a = math.tau * i / 12
            copy = o.copy()
            bpy.context.scene.collection.objects.link(copy)
            copy.location = (px + bold * math.cos(a), py + bold * math.sin(a), z)

    def measure(self, body, height):
        o = self._text_object(body, BLACK)
        bpy.context.view_layer.update()
        xs = [c[0] for c in o.bound_box]
        ys = [c[1] for c in o.bound_box]
        w = (max(xs) - min(xs)) * height / (max(ys) - min(ys))
        cu = o.data
        bpy.data.objects.remove(o)
        bpy.data.curves.remove(cu)
        return w

    def row(self, items, x0, x1, cy, height, color):
        """Lays out text groups and fixed-width gaps centred between x0 and x1, the letters narrowed if that's what it
        takes to fit. items: strings, or (width, draw) where draw (or None) is given its centre x."""
        natural = [(0.0 if isinstance(i, tuple) else self.measure(i, height)) for i in items]
        fixed = sum(i[0] for i in items if isinstance(i, tuple))
        stretch = min(1.0, (x1 - x0 - 0.05 - fixed) / sum(natural))
        widths = [(i[0] if isinstance(i, tuple) else w * stretch) for i, w in zip(items, natural)]
        x = (x0 + x1 - sum(widths)) / 2
        for item, w in zip(items, widths):
            if isinstance(item, tuple):
                if item[1]:
                    item[1](x + w / 2)
            else:
                self.text(item, x + w / 2, cy, height, color, stretch=stretch)
            x += w


def base(c, fill, border=0.006, r=0.018):
    c.rect(0, 0, PLATE_W, PLATE_H, BLACK)
    c.rrect(border, border, PLATE_W - border, PLATE_H - border, r, fill)


def eu_band(c, code):
    """The blue band: twelve stars over the country's letters."""
    b = 0.006
    c.poly(T.round_poly([(b, b), (BAND, b), (BAND, PLATE_H - b), (b, PLATE_H - b)], [0.018, 0, 0, 0.018], 6), EU_BLUE)
    cx, cy = (b + BAND) / 2, PLATE_H * 0.69
    for i in range(12):
        a = math.tau * i / 12
        c.star(cx + 0.0245 * math.sin(a), cy + 0.0245 * math.cos(a), 0.0062, EU_YELLOW)
    c.text(code, cx, PLATE_H * 0.21, 0.044, WHITE, bold=0.001)


def eagle(c, cx, cy, s, color):
    """The double-headed eagle, as a simple silhouette (s: half its height)."""
    def P(x, y):
        return (cx + x * s, cy + y * s)
    for side in (-1, 1):
        # Wing: up from the shoulder and out, its lower edge in feathers.
        wing = [(0.05, 0.42), (0.34, 0.68), (0.78, 0.78), (0.86, 0.48), (0.68, 0.46), (0.76, 0.20), (0.58, 0.20), (0.62, -0.05), (0.44, -0.02), (0.44, -0.26), (0.26, -0.12), (0.05, -0.10)]
        c.poly([P(side * x, y) for x, y in wing], color)
        # Neck, head and beak, the crest behind it, and a claw.
        c.poly([P(side * x, y) for x, y in ((0.02, 0.38), (0.12, 0.38), (0.24, 0.72), (0.14, 0.76))], color)
        c.disc(*P(side * 0.215, 0.80), 0.105 * s, color, n=14)
        c.poly([P(side * x, y) for x, y in ((0.27, 0.87), (0.46, 0.78), (0.28, 0.72))], color)
        c.poly([P(side * x, y) for x, y in ((0.08, -0.34), (0.36, -0.50), (0.42, -0.42), (0.22, -0.24))], color)
    # Body and tail.
    c.poly([P(x, y) for x, y in ((-0.14, 0.44), (0.14, 0.44), (0.20, 0.0), (0.12, -0.34), (-0.12, -0.34), (-0.20, 0.0))], color)
    c.poly([P(x, y) for x, y in ((-0.13, -0.30), (0.13, -0.30), (0.26, -0.72), (0.12, -0.62), (0.0, -0.90), (-0.12, -0.62), (-0.26, -0.72))], color)


def albanian(c, groups):
    # The eagle over AL on the left band, a blue band with a ring on the right, a small seal between the groups.
    base(c, WHITE)
    b = 0.006
    c.poly(T.round_poly([(b, b), (BAND - 0.012, b), (BAND - 0.012, PLATE_H - b), (b, PLATE_H - b)], [0.018, 0, 0, 0.018], 6), EU_BLUE)
    cx = (b + BAND - 0.012) / 2
    eagle(c, cx, PLATE_H * 0.64, 0.037, WHITE)
    c.text("AL", cx, PLATE_H * 0.17, 0.034, WHITE, bold=0.0008)
    x1 = PLATE_W - b
    c.poly(T.round_poly([(x1 - 0.062, b), (x1, b), (x1, PLATE_H - b), (x1 - 0.062, PLATE_H - b)], [0, 0.018, 0.018, 0], 6), EU_BLUE)
    c.disc(x1 - 0.031, PLATE_H * 0.25, 0.019, WHITE)
    c.disc(x1 - 0.031, PLATE_H * 0.25, 0.015, EU_BLUE)
    h, cy = 0.140, PLATE_H / 2

    def seal(x):
        c.disc(x, cy, 0.016, (0.55, 0.55, 0.52))
        c.disc(x, cy, 0.011, (0.82, 0.82, 0.78))
        c.disc(x, cy, 0.005, (0.55, 0.55, 0.52))
    c.row([groups[0], (0.056, seal), groups[1], (0.040, None), groups[2]], BAND - 0.012, x1 - 0.062, cy, h, BLACK)


def romanian(c, groups):
    base(c, (0.91, 0.90, 0.85))
    eu_band(c, "RO")
    c.row([groups[0], (0.075, None), groups[1], (0.075, None), groups[2]], BAND, PLATE_W, PLATE_H * 0.54, 0.142, BLACK)
    # The maker's serial and barcode under the numbers.
    y = 0.012
    x = 0.47
    c.rect(0.38, y, 0.45, y + 0.006, (0.42, 0.42, 0.40))
    rng = np.random.default_rng(int(sum(map(ord, "".join(groups)))))
    while x < 0.60:
        w = float(rng.choice((0.001, 0.0018, 0.003)))
        c.rect(x, y - 0.002, x + w, y + 0.010, (0.25, 0.25, 0.25))
        x += w + 0.0018


def german(c, groups):
    # The city's letters, the inspection and state seals stacked in the gap, the other letters and the number.
    base(c, WHITE)
    eu_band(c, "D")
    h, cy = 0.140, PLATE_H / 2

    def seals(x):
        y = PLATE_H * 0.73
        c.disc(x, y, 0.036, (0.28, 0.15, 0.08))
        c.disc(x, y, 0.034, (0.82, 0.35, 0.08))
        for i in range(12):
            a = math.tau * i / 12
            c.rect(x + 0.024 * math.sin(a) - 0.0015, y + 0.024 * math.cos(a) - 0.0028, x + 0.024 * math.sin(a) + 0.0015, y + 0.024 * math.cos(a) + 0.0028, (0.25, 0.13, 0.05))
        c.disc(x, y, 0.010, (0.28, 0.15, 0.08))
        c.disc(x, y, 0.0075, (0.82, 0.35, 0.08))
        y = PLATE_H * 0.27
        c.disc(x, y, 0.036, (0.25, 0.25, 0.25))
        c.disc(x, y, 0.034, (0.74, 0.74, 0.72))
        c.poly(T.round_poly([(x - 0.014, y + 0.017), (x + 0.014, y + 0.017), (x + 0.014, y - 0.006), (x, y - 0.021), (x - 0.014, y - 0.006)], 0.004, 3), (0.85, 0.68, 0.10))
        for dy in (0.008, -0.004):
            c.rect(x - 0.009, y + dy - 0.002, x + 0.009, y + dy + 0.002, (0.08, 0.08, 0.08))
    c.row([groups[0], (0.090, seals), groups[1], (0.024, None), groups[2]], BAND, PLATE_W, cy, h, BLACK)


def dutch(c, groups):
    base(c, NL_YELLOW, border=0.010, r=0.022)
    eu_band(c, "NL")
    h, cy = 0.138, PLATE_H / 2

    def dash(x):
        c.rect(x - 0.018, cy - 0.009, x + 0.018, cy + 0.009, BLACK)
    c.row([groups[0], (0.056, dash), groups[1], (0.056, dash), groups[2]], BAND, PLATE_W, cy, h, BLACK)


COUNTRY = dict(AL=albanian, RO=romanian, DE=german, NL=dutch)


# ============================================================================================ the atlas
def render_atlas(path):
    scene = bpy.context.scene
    for i, (_, country, groups) in enumerate(PLATES):
        x, y = cell_origin(i)
        COUNTRY[country](Canvas(x, y), groups)
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 32
    scene.cycles.use_denoising = False
    scene.cycles.filter_width = 1.0
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    scene.render.resolution_x, scene.render.resolution_y = 2048, 2048
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = path
    world = bpy.data.worlds.new("black")
    world.color = (0, 0, 0)
    scene.world = world
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = ATLAS_W
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = (ATLAS_W / 2, ATLAS_H / 2, 3)
    scene.camera = cam
    bpy.ops.render.render(write_still=True)


def value_noise(h, w, cells_y, cells_x, rng):
    """Smooth random values 0..1 on an h x w image."""
    grid = rng.random((cells_y + 2, cells_x + 2))
    ys = np.linspace(0, cells_y, h)
    xs = np.linspace(0, cells_x, w)
    yi, xi = ys.astype(int), xs.astype(int)
    fy, fx = (ys - yi)[:, None], (xs - xi)[None, :]
    fy, fx = fy * fy * (3 - 2 * fy), fx * fx * (3 - 2 * fx)
    a = grid[yi][:, xi]
    b = grid[yi][:, xi + 1]
    c = grid[yi + 1][:, xi]
    d = grid[yi + 1][:, xi + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def box_blur(a, r):
    for axis in (0, 1):
        k = np.ones(2 * r + 1) / (2 * r + 1)
        a = np.apply_along_axis(lambda v: np.convolve(np.pad(v, r, mode='edge'), k, mode='valid'), axis, a)
    return a


def bake_set(path):
    """From the flat render: albedo with road dirt, the raised letters and rim in the normal map, roughness and a
    little metal sheen in the ORM."""
    im = bpy.data.images.load(path)
    im.colorspace_settings.name = 'Non-Color'
    h, w = im.size[1], im.size[0]
    px = np.empty(h * w * 4, np.float32)
    im.pixels.foreach_get(px)
    rgb = px.reshape(h, w, 4)[..., :3].copy()
    bpy.data.images.remove(im)
    rng = np.random.default_rng(1)
    # Dirt: film over everything, thicker along the bottom edge of each plate and in soft patches.
    edge = np.zeros((h, w), np.float32)
    for i in range(len(PLATES)):
        x, y = cell_origin(i)
        x0, y0 = int(x * 1000), int(y * 1000)
        ph, pw = int(PLATE_H * 1000), int(PLATE_W * 1000)
        ramp = np.clip(1 - np.linspace(0, 1, ph) / 0.45, 0, 1) ** 2
        edge[y0:y0 + ph, x0:x0 + pw] = ramp[:, None]
    film = 0.05 + 0.20 * value_noise(h, w, 24, 48, rng) ** 2 + 0.22 * edge * value_noise(h, w, 40, 200, rng)
    speck = np.clip((value_noise(h, w, 340, 680, rng) - 0.72) * 3, 0, 1) * 0.3
    dirt = np.clip(film + speck, 0, 0.5)[..., None]
    grime = np.array((0.30, 0.26, 0.21), np.float32)
    albedo = rgb * (1 - dirt) + grime * dirt
    # Raised letters: anything dark on the plate.
    lum = rgb @ np.array((0.3, 0.6, 0.1), np.float32)
    height = box_blur((lum < 0.18).astype(np.float32), 2)
    gy, gx = np.gradient(height)
    k = 2.2
    nx, ny, nz = -gx * k, -gy * k, np.ones_like(height)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.dstack([nx / ln * 0.5 + 0.5, ny / ln * 0.5 + 0.5, nz / ln * 0.5 + 0.5])
    ones = np.ones((h, w), np.float32)
    rough = np.clip(0.38 + 0.9 * dirt[..., 0], 0, 1)
    orm = np.dstack([ones, rough, 0.05 * ones])

    def save(name, arr):
        out = bpy.data.images.new(name, w, h, alpha=True)
        out.colorspace_settings.name = 'Non-Color'
        out.pixels.foreach_set(np.dstack([np.clip(arr, 0, 1), ones]).astype(np.float32).ravel())
        out.filepath_raw = os.path.join(K.TEX, name + ".png")
        out.file_format = 'PNG'
        out.save()
        bpy.data.images.remove(out)
    save("van_plates_albedo", albedo)
    save("van_plates_normal", normal)
    save("van_plates_orm", orm)
    print("[plates] baked van_plates texture set", flush=True)


# ============================================================================================ the meshes
def plate_quad(bm, uvl, surface, x_left, x_right, z0, z1, off, uv):
    """A plate on a surface: x_left is on the left as you look at it. uv: (u0, v0, u1, v1) of its picture."""
    nu, nv = 8, 2
    u0, v0, u1, v1 = uv
    grid = [[bm.verts.new(surface.at(x_left + (x_right - x_left) * i / nu, z0 + (z1 - z0) * j / nv, off) + T.Vector((0, T.Y_SHIFT, 0))) for i in range(nu + 1)] for j in range(nv + 1)]
    for j in range(nv):
        for i in range(nu):
            f = bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
            for loop, (ii, jj) in zip(f.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))):
                loop[uvl].uv = (u0 + (u1 - u0) * ii / nu, v0 + (v1 - v0) * jj / nv)


def export_plates():
    material = T.mat("tex_van_plates", None, 0.45, 0.05)
    for i, (name, _, _) in enumerate(PLATES):
        x, y = cell_origin(i)
        uv = (x / ATLAS_W, y / ATLAS_H, (x + PLATE_W) / ATLAS_W, (y + PLATE_H) / ATLAS_H)
        bm = bmesh.new()
        uvl = bm.loops.layers.uv.new("UVMap")
        # Looking at the front, +X is on the left; looking at the back, it's on the right.
        plate_quad(bm, uvl, T.FRONT, 0.26, -0.26, 0.615, 0.725, 0.018, uv)
        plate_quad(bm, uvl, T.BACK, -0.26, 0.26, 0.620, 0.730, 0.010, uv)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        # Both faces look outward (recalc can't tell for open sheets, so check them against where they are).
        o = bpy.data.objects.new("transit_plate_" + name, me)
        bpy.context.scene.collection.objects.link(o)
        bm = bmesh.new()
        bm.from_mesh(me)
        for f in bm.faces:
            if f.normal.y * f.calc_center_median().y < 0:
                f.normal_flip()
        bm.to_mesh(me)
        bm.free()
        me.materials.append(material)
        o["uv"] = "keep"
        o["smooth"] = True
        K.finish([o])
        K.export("transit_plate_" + name, [o])


def main():
    K.reset()
    os.makedirs(K.PREVIEWS, exist_ok=True)
    path = os.path.join(K.PREVIEWS, "van_plates.png")
    render_atlas(path)
    bake_set(path)
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    export_plates()
    print("[plates] done", flush=True)


main()
