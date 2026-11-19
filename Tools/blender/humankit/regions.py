"""Body regions (Docs/npc_outfits_anim/CONTRACTS.md section 1).

`assign(body, rig)` writes the `_REGION` float point attribute on the body mesh: an integer 0-14 per vertex, taken from
the dominant deform bone of the final (smoothed, limited) weights and, for the arms and legs, the position t along the
bone (t = 0 at bone.head_local, 1 at bone.tail_local). It is exported by the glTF exporter (export_attributes=True) as
`_REGION`, and the game uses it to hide the skin under garments.

The module also holds what the garment kit needs to read the attribute back: NAMES, `labels(body)`, `bit(...)`.
"""
import numpy as np

NAMES = ["head", "scalp", "neck", "chest", "belly", "hips", "upperarm_hi", "upperarm_lo",
         "forearm", "hand", "thigh_hi", "thigh_lo", "calf_hi", "calf_lo", "foot"]
INDEX = {n: i for i, n in enumerate(NAMES)}
ATTR = "_REGION"

# The scalp, from the anatomy (humankit/anatomy.py; the joints are not stored in the .blend, so the numbers are repeated
# here). Brow ridge ellipsoid centre z 1.688; the ear is centred at (0.0805, 0.012, 1.653), 31 mm half height, leaning
# back, so its top is at about z 1.69.
BROW_Z = 1.688
EAR = (0.0805, 0.012, 1.653)
EAR_TOP_Z = 1.690
SCALP_FRONT_ABOVE_BROW = 0.04    # in front of the ear: z > brow z + 0.04
SCALP_BACK_ABOVE_EAR = 0.01      # level with and behind the ear: z > ear top z + 0.01
EAR_RADIUS = 0.05                # head vertices this close to the ear centre (and off the midline) are ear, never scalp


def log(*a):
    print("[human]", *a, flush=True)


def bit(*names):
    m = 0
    for n in names:
        m |= 1 << INDEX[n]
    return m


def _weights(body):
    me = body.data
    names = [g.name for g in body.vertex_groups]
    W = np.zeros((len(me.vertices), len(names)))
    for v in me.vertices:
        for g in v.groups:
            W[v.index, g.group] = g.weight
    return names, W


def _base(name):
    """('upperarm_twist.L') -> ('upperarm_twist', '.L')."""
    for s in (".L", ".R"):
        if name.endswith(s):
            return name[:-2], s
    return name, ""


def assign(body, rig):
    """Writes the `_REGION` attribute on body.data from its (final) vertex groups; returns the int array."""
    me = body.data
    names, W = _weights(body)
    dom = np.array(names)[W.argmax(axis=1)]
    co = np.array([v.co for v in me.vertices])
    bones = rig.data.bones
    region = np.zeros(len(co), int)

    def t_along(bone_name, idx):
        b = bones[bone_name]
        h, tl = np.array(b.head_local), np.array(b.tail_local)
        return ((co[idx] - h) @ (tl - h)) / ((tl - h) @ (tl - h))

    simple = {"head": 0, "neck_01": 2, "spine_03": 3, "clavicle": 3, "spine_02": 4, "spine_01": 4, "pelvis": 5,
              "root": 5, "forearm": 8, "forearm_twist": 8, "hand": 9, "foot": 14, "ball": 14, "eye": 0}
    fingers = ("thumb", "index", "middle", "ring", "pinky")
    # (family, region for t < 0.5, region for t >= 0.5, the bone t is measured on)
    split = {"upperarm": (6, 7, "upperarm"), "upperarm_twist": (6, 7, "upperarm"), "thigh": (10, 11, "thigh"),
             "calf": (12, 13, "calf")}
    for side in (".L", ".R"):
        for fam, (lo, hi, tb) in split.items():
            idx = np.where(dom == fam + side)[0]
            if len(idx):
                region[idx] = np.where(t_along(tb + side, idx) < 0.5, lo, hi)
    for i, d in enumerate(dom):
        base, _ = _base(d)
        if base in split:
            continue
        if base in simple:
            region[i] = simple[base]
        elif base.split("_")[0] in fingers:
            region[i] = 9
        else:
            raise RuntimeError(f"regions: no region for the dominant bone {d!r}")
    # scalp: head-dominated vertices above the hairline (ears and face never)
    ear = np.array(EAR)
    f = np.array([abs(c[0]) for c in co])
    head = (region == 0) & np.array([d == "head" for d in dom])
    d_ear = np.linalg.norm(np.column_stack([f, co[:, 1], co[:, 2]]) - ear, axis=1)
    not_ear = ~((d_ear < EAR_RADIUS) & (f > 0.06))
    front = co[:, 1] < EAR[1]
    thr = np.where(front, BROW_Z + SCALP_FRONT_ABOVE_BROW, EAR_TOP_Z + SCALP_BACK_ABOVE_EAR)
    region[head & not_ear & (co[:, 2] > thr)] = 1

    if ATTR in me.attributes:
        me.attributes.remove(me.attributes[ATTR])
    a = me.attributes.new(ATTR, 'FLOAT', 'POINT')
    a.data.foreach_set("value", region.astype(np.float32))
    me.update()
    cnt = np.bincount(region, minlength=15)
    log("regions: " + ", ".join(f"{n} {c}" for n, c in zip(NAMES, cnt)))
    if (cnt == 0).any():
        log("  WARNING empty regions:", [n for n, c in zip(NAMES, cnt) if c == 0])
    return region


def labels(body):
    """The `_REGION` attribute as an int array (raises if missing)."""
    a = body.data.attributes.get(ATTR)
    if a is None:
        raise RuntimeError("body has no _REGION attribute: run build_human.py -- regions first")
    v = np.zeros(len(body.data.vertices), np.float32)
    a.data.foreach_get("value", v)
    return np.rint(v).astype(int)
