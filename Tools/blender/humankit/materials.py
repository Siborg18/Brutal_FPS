"""Preview materials. Names follow the game's convention: flat_* = untextured (base colour, roughness)."""
import bpy


def srgb(c):
    return tuple((x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4) for x in c) + (1.0,)


def material(name, color, rough, sss=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = srgb(color)
    b.inputs["Roughness"].default_value = rough
    if sss:
        b.inputs["Subsurface Weight"].default_value = sss
        b.inputs["Subsurface Radius"].default_value = (1.0, 0.35, 0.2)
        b.inputs["Subsurface Scale"].default_value = 0.012
    m.diffuse_color = srgb(color)
    return m


SKIN = (0.78, 0.57, 0.46)


def assign(body, eyes):
    # "_tint": the game draws the skin in each character's own tone (the instance colour)
    skin = material("flat_skin_tint", SKIN, 0.48, sss=0.2)
    body.data.materials.clear()
    body.data.materials.append(skin)
    for p in body.data.polygons:
        p.material_index = 0
    mats = [material("flat_eye_sclera", (0.90, 0.88, 0.84), 0.12), material("flat_eye_iris", (0.33, 0.42, 0.50), 0.18),
            material("flat_eye_pupil", (0.015, 0.015, 0.015), 0.1)]
    for e in eyes:
        idx = [p.material_index for p in e.data.polygons]      # clearing the slots resets the indices
        e.data.materials.clear()
        for m in mats:
            e.data.materials.append(m)
        for p, i in zip(e.data.polygons, idx):
            p.material_index = i
