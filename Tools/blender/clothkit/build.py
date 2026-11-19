"""One garment, end to end.

    shell phase (`shell_phase`): carve -> coverage -> shell -> UVs  (a `ShellState`; weights are the body's, copied exactly)
    finish phase (`finish`):     extras (sole) -> hem -> Blender object                (a `Built`)
"""
from dataclasses import dataclass, field

import bpy
import numpy as np

from . import carve, check, export, shell, uvs
from .carve import log


@dataclass
class ShellState:
    garment: object
    soup: object             # the shell on the rest pose (before extras and hem)
    offset: np.ndarray       # per-vertex shell offset (m)
    coverage: dict
    edges: np.ndarray        # its boundary (the garment's real edge)

    @property
    def n(self):
        return len(self.soup.P)


@dataclass
class Built:
    state: ShellState
    soup: object             # final Soup (rest pose): shell + sole + hem
    obj: object              # Blender object (parented to the rig, armature modifier)
    hem_start: int           # triangles from here on are the hem (they face the skin, so the poke test leaves them out)

    @property
    def garment(self):
        return self.state.garment

    @property
    def n_carved(self):
        return self.state.n

    @property
    def shell_tris(self):
        return self.state.soup.tris

    @property
    def shell_edges(self):
        return self.state.edges

    @property
    def coverage(self):
        return self.state.coverage

    @property
    def triangles(self):
        return len(self.soup.tris)


def shell_phase(body, garment):
    soup = carve.carve(body, garment)
    cov = check.coverage(body, soup, garment)
    d = shell.make_shell(soup, body, garment)
    uvs.rescale(soup, garment)
    return ShellState(garment, soup, d, cov, soup.boundary())


def finish(body, st, name=None):
    """Extras and hem on a copy of the shell, then the Blender object."""
    g = st.garment
    s = st.soup
    soup = type(s)(*[x.copy() if isinstance(x, np.ndarray) else x for x in
                     (s.P, s.N, s.W, s.label, s.src, s.tris, s.uv, s.mat, s.flat, s.pa, s.pb, s.ps)])
    for fn in g.extras:
        fn(soup, body, g)
    hem_start = len(soup.tris)
    shell.add_hems(soup, g)
    soup.W /= np.maximum(soup.W.sum(axis=1, keepdims=True), 1e-9)
    obj = export.make_object(soup, g, body, body.rig, name=name)
    return Built(st, soup, obj, hem_start)


def discard(built):
    me = built.obj.data
    bpy.data.objects.remove(built.obj)
    bpy.data.meshes.remove(me)


def build(body, garment):
    st = shell_phase(body, garment)
    b = finish(body, st)
    over = "" if b.triangles <= garment.tri_budget else f"  OVER BUDGET ({garment.tri_budget})"
    log(f"{garment.id}: {b.triangles} triangles, {len(b.soup.P)} vertices{over}")
    return b
