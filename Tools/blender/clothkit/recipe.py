"""Garment recipes: plain data that says what a garment is and which part of the body it is carved from.

A recipe is a `Garment` (identity, CONTRACTS section 2 fields, offsets, materials) plus `zones`. The carved surface is the
UNION of the zones. A zone is

    Zone(regions=[...], cuts=[...], grow_mm=0, side=None)

* `regions`: body regions (humankit/regions.py names) whose vertices are in the zone. Without `grow_mm` the zone's border
  follows the region's mesh edges (jagged, fine for borders that are covered by something else).
* `grow_mm`: also take everything within this geodesic distance (over the mesh edges) of those regions. Gives a smooth
  hem that is a fixed distance away from a region, the right tool for "25 mm past the boundary".
* `cuts`: planes that trim the zone exactly (the mesh is cut along them, so a hem is a straight line, not a staircase).
  `BoneCut` (plane perpendicular to a bone at t along it), `PlaneCut` (a world plane) and `FitCut` (a plane fitted to the
  border between two region sets and moved away from it by a margin: the usual way to place a hem or a neckline).
* `side`: "L" or "R" limits the zone to vertices whose dominant bone is on that side; `{s}` in a bone name becomes
  ".L" / ".R". `sided(zone)` returns the pair of zones for both sides.

Distances in cuts are metres; a positive value means "this vertex is cut away".
"""
from dataclasses import dataclass, field, replace
from typing import Callable, List, Optional, Sequence

import numpy as np


# --------------------------------------------------------------------------------------------- cuts
@dataclass
class BoneCut:
    """Plane perpendicular to `bone` at `t` along it (0 = head, 1 = tail). `remove` is the side that is cut away."""
    bone: str
    t: float = 0.5
    remove: str = "tail"          # "tail": everything beyond t towards the tail; "head": towards the head
    mm_from_tail: Optional[float] = None    # instead of t: this many mm up the bone from its tail

    def distance(self, body, side):
        h, tl = body.bone(self.bone.format(s="." + side if side else ""))
        axis = (tl - h) / np.linalg.norm(tl - h)
        t = self.t if self.mm_from_tail is None else 1 - self.mm_from_tail / 1000.0 / np.linalg.norm(tl - h)
        d = (body.co - (h + t * (tl - h))) @ axis
        return d if self.remove == "tail" else -d


@dataclass
class PlaneCut:
    """World plane through `point` with `normal` pointing to the side that is cut away."""
    point: Sequence[float]
    normal: Sequence[float]

    def distance(self, body, side):
        n = np.asarray(self.normal, float)
        return (body.co - np.asarray(self.point, float)) @ (n / np.linalg.norm(n))

    @staticmethod
    def z(z, remove="above"):
        return PlaneCut((0, 0, z), (0, 0, 1.0 if remove == "above" else -1.0))


@dataclass
class FitCut:
    """A plane fitted (least squares) to the border between the regions `near` and `far` (the vertices of `near` that touch
    `far`), then moved `margin_mm` towards `remove` past the farthest border vertex. Everything beyond it is cut away.
    `remove` is a direction vector, roughly the plane normal (it only picks the sign and breaks ties)."""
    near: List[str]
    far: List[str]
    margin_mm: float
    remove: Sequence[float] = (0, 0, 1)

    def plane(self, body, side):
        """(point, unit normal) of the cut plane; the normal points to the side that is cut away."""
        pts = body.border_points(self.near, self.far, side)
        if len(pts) < 3:
            raise RuntimeError(f"FitCut: no border between {self.near} and {self.far} (side {side})")
        c = pts.mean(0)
        n = np.linalg.svd(pts - c)[2][-1]
        r = np.asarray(self.remove, float) * ((-1.0, 1.0, 1.0) if side == "R" else (1.0, 1.0, 1.0))
        if n @ r < 0:
            n = -n
        return c + n * (((pts - c) @ n).max() + self.margin_mm / 1000.0), n

    def distance(self, body, side):
        p, n = self.plane(body, side)
        return (body.co - p) @ n


# --------------------------------------------------------------------------------------------- zones
@dataclass
class Zone:
    regions: List[str]
    cuts: List[object] = field(default_factory=list)
    grow_mm: float = 0.0
    side: Optional[str] = None
    exclude: Optional[Callable] = None     # fn(body) -> bool mask of body vertices that are never part of the zone (the ears)


def sided(zone: Zone) -> List[Zone]:
    """The zone for the left and the right side (cut bone names use {s})."""
    return [replace(zone, side=s) for s in ("L", "R")]


# --------------------------------------------------------------------------------------------- garment
@dataclass
class Hem:
    """The rolled edge on every open boundary: the boundary loop goes `roll_mm` towards the skin, then `tuck_mm` back in under
    the cloth. 0 disables."""
    roll_mm: float = 3.0
    tuck_mm: float = 4.0


@dataclass
class Garment:
    id: str
    slot: str                          # hair head torso_base torso_outer legs feet
    layer: int
    offset_mm: float
    hides: List[str]                   # body regions this garment hides
    materials: List[str]               # material names; triangles use material_index into this list
    zones: List[Zone]
    wound: str = "fabric"              # skin fabric hard
    tri_budget: int = 6000             # LOD0 triangles, from the brief
    offset_by_region: dict = field(default_factory=dict)    # region name -> mm (smoothed into its neighbours)
    hem: Hem = field(default_factory=Hem)
    extras: List[Callable] = field(default_factory=list)    # fn(soup, body, garment) run after the shell, before the hem
    smooth_iters: int = 12             # relaxation rounds (one Laplacian step + a push out of the skin each)
    crease_iters: int = 1              # extra smoothing steps per round that only move concave vertices: bridges armpits and the crotch
    tile: Optional[str] = None         # texture set that sets the UV scale; default from the first material name
    preview_colors: dict = field(default_factory=dict)      # material -> sRGB, preview renders only
    hide_for_preview: Optional[List[str]] = None            # default: hides
    coverage_mm: float = 25.0          # what check.coverage requires (CONTRACTS: 10 for hair and head slot garments)
    offset_fn: Optional[Callable] = None    # fn(soup, body, offset_mm) -> offset_mm: final say on the per-vertex offset (e.g. hair that thins to its edge)

    def sidecar(self, triangles: int) -> dict:
        """CONTRACTS section 2: exactly these keys, in this order."""
        return {"id": self.id, "slot": self.slot, "layer": self.layer, "offset_mm": self.offset_mm,
                "hides": list(self.hides), "wound": self.wound, "materials": list(self.materials),
                "triangles": int(triangles)}
