"""Mesh and vertex groups from a soup. A garment vertex carries its source body vertex's weights exactly (CONTRACTS section 2:
no re-smoothing); cut vertices take the nearer parent's, added geometry (hem, sole) its nearest carved vertex's."""
import bpy
import numpy as np



def mesh_from_soup(soup, name):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(p) for p in soup.P], [], [tuple(t) for t in soup.tris.tolist()])
    me.update(calc_edges=True)
    return me


def set_groups(obj, soup, names):
    used = np.where((soup.W > 1e-6).any(axis=0))[0]
    for gi in used:
        vg = obj.vertex_groups.new(name=names[gi])
        col = soup.W[:, gi]
        for v in np.where(col > 1e-6)[0]:
            vg.add([int(v)], float(col[v]), 'REPLACE')
