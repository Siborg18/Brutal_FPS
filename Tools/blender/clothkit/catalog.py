"""The garment registry and the recipes S2 built. Add garments in a new module (e.g. garments_more.py) that does
`from .catalog import register` and calls `register(Garment(...))`; build_clothes.py imports it by name."""
from .recipe import BoneCut, FitCut, Garment, Hem, PlaneCut, Zone, sided

REGISTRY = {}


def register(g):
    REGISTRY[g.id] = g
    return g


# --------------------------------------------------------------------------------------------- tshirt
register(Garment(
    id="tshirt", slot="torso_base", layer=3, offset_mm=9,
    hides=["chest", "belly", "upperarm_hi"], materials=["cloth_tint"], wound="fabric", tri_budget=6000,
    zones=[
        # trunk: from the collar (30 mm above the chest/neck border, tilted to follow it) to a hem 80 mm below the belly/hips border
        Zone(regions=["chest", "belly", "hips", "thigh_hi", "neck"], cuts=[
            FitCut(near=["belly"], far=["hips", "thigh_hi"], margin_mm=80, remove=(0, 0, -1)),
            FitCut(near=["chest"], far=["neck"], margin_mm=30, remove=(0, 0, 1)),
        ]),
        # short sleeves: 30 mm past the upperarm_hi/upperarm_lo border
        *sided(Zone(regions=["upperarm_hi", "upperarm_lo", "forearm"], cuts=[
            FitCut(near=["upperarm_hi"], far=["upperarm_lo"], margin_mm=30, remove=(0.7, 0, -0.7))])),
    ],
    offset_by_region={"belly": 11, "hips": 15, "thigh_hi": 15},        # the hem hangs loose over the trousers
    preview_colors={"cloth_tint": (0.30, 0.38, 0.55)},
))

# --------------------------------------------------------------------------------------------- trousers
register(Garment(
    id="trousers_work", slot="legs", layer=2, offset_mm=6,
    hides=["hips", "thigh_hi", "thigh_lo", "calf_hi", "calf_lo"], materials=["canvas_tint"], wound="fabric", tri_budget=7000,
    zones=[
        # waist: 30 mm above the highest border between the belly and the hips/thighs
        Zone(regions=["belly", "hips", "thigh_hi", "thigh_lo", "calf_hi", "calf_lo"], cuts=[
            FitCut(near=["hips", "thigh_hi"], far=["belly"], margin_mm=30, remove=(0, 0, 1))]),
        # ankles: a plane fitted to the calf_lo/foot border, 30 mm below it (down the foot), one zone per leg
        *sided(Zone(regions=["calf_lo", "foot"], cuts=[
            FitCut(near=["calf_lo"], far=["foot"], margin_mm=30, remove=(0, 0, -1))])),
    ],
    offset_by_region={"calf_lo": 14, "foot": 14},           # flares over the boot shafts
    preview_colors={"canvas_tint": (0.45, 0.38, 0.26)},
))

# --------------------------------------------------------------------------------------------- boots
from .extras import sole  # noqa: E402

register(Garment(
    id="boots_work", slot="feet", layer=1, offset_mm=6,
    hides=["foot"], materials=["leather_tint", "flat_sole"], wound="hard", tri_budget=3000,
    zones=[
        # the foot, and a shaft up the calf to 120 mm above the ankle joint (the calf bone's tail)
        *sided(Zone(regions=["foot", "calf_lo"], cuts=[BoneCut("calf{s}", remove="head", mm_from_tail=120)])),
    ],
    extras=[sole], hem=Hem(roll_mm=3.0, tuck_mm=0.0),      # a plain rolled lip keeps the boots inside their budget
    preview_colors={"leather_tint": (0.30, 0.18, 0.10)},
))


# the rest of the catalogue (S3); it registers itself on import
from . import garments_more  # noqa: E402,F401
