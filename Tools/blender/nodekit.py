"""
Tiny DSL for building Blender shader node graphs from Python with operator overloading.

    g = Graph(material)
    u, v = g.uv()
    n = g.noise(8, 8, detail=6, seed=3)       # tileable 4D noise, ~8 features across the tile
    h = g.smooth(0.4, 0.6, n) * 0.5 + 0.1     # Math nodes are created behind the scenes

Every texture is sampled on a 4D torus built from the UV coordinates, so all baked maps tile seamlessly.
"""
import math
import bpy

TAU = math.tau


class S:
    """A scalar (float) socket wrapper with operator overloading."""

    def __init__(self, g, sock):
        self.g = g
        self.sock = sock

    def _op(self, op, other, rev=False):
        a, b = (other, self) if rev else (self, other)
        return self.g.math(op, a, b)

    def __add__(self, o): return self._op('ADD', o)
    def __radd__(self, o): return self._op('ADD', o, True)
    def __sub__(self, o): return self._op('SUBTRACT', o)
    def __rsub__(self, o): return self._op('SUBTRACT', o, True)
    def __mul__(self, o): return self._op('MULTIPLY', o)
    def __rmul__(self, o): return self._op('MULTIPLY', o, True)
    def __truediv__(self, o): return self._op('DIVIDE', o)
    def __rtruediv__(self, o): return self._op('DIVIDE', o, True)
    def __neg__(self): return self.g.math('MULTIPLY', self, -1.0)


class C:
    """A colour socket wrapper."""

    def __init__(self, g, sock):
        self.g = g
        self.sock = sock

    def __mul__(self, o): return self.g.cmix('MULTIPLY', 1.0, self, o)
    def __rmul__(self, o): return self.g.cmix('MULTIPLY', 1.0, self, o)
    def __add__(self, o): return self.g.cmix('ADD', 1.0, self, o)


class Graph:
    def __init__(self, mat):
        mat.use_nodes = True
        self.nt = mat.node_tree
        self.nt.nodes.clear()
        self.n = self.nt.nodes
        self.l = self.nt.links
        self._uv = None
        self._x = 0

    # ------------------------------------------------------------------ plumbing
    def node(self, kind, **props):
        nd = self.n.new(kind)
        for k, v in props.items():
            setattr(nd, k, v)
        self._x += 1
        nd.location = ((self._x % 40) * 220, -(self._x // 40) * 300)
        return nd

    def link(self, src, dst_sock):
        if isinstance(src, (S, C)):
            self.l.new(src.sock, dst_sock)
        elif isinstance(src, (int, float)):
            dst_sock.default_value = float(src)
        elif isinstance(src, (tuple, list)):
            v = tuple(src)
            if len(dst_sock.default_value) == 4 and len(v) == 3:
                v = v + (1.0,)
            dst_sock.default_value = v
        else:
            self.l.new(src, dst_sock)

    @staticmethod
    def sock(node, name, kind, out=False):
        for s in (node.outputs if out else node.inputs):
            if s.name == name and s.type == kind and s.enabled:
                return s
        for s in (node.outputs if out else node.inputs):
            if s.name == name and s.type == kind:
                return s
        raise KeyError(f"{node.bl_idname}: {name}/{kind}")

    # ------------------------------------------------------------------ scalars
    def const(self, v):
        nd = self.node('ShaderNodeValue')
        nd.outputs[0].default_value = v
        return S(self, nd.outputs[0])

    def math(self, op, a, b=None, clamp=False):
        nd = self.node('ShaderNodeMath', operation=op, use_clamp=clamp)
        self.link(a, nd.inputs[0])
        if b is not None:
            self.link(b, nd.inputs[1])
        return S(self, nd.outputs[0])

    def fn(self, op, a, b=None): return self.math(op, a, b)
    def floor(self, a): return self.math('FLOOR', a)
    def fract(self, a): return self.math('FRACT', a)
    def absv(self, a): return self.math('ABSOLUTE', a)
    def sin(self, a): return self.math('SINE', a)
    def cos(self, a): return self.math('COSINE', a)
    def mn(self, a, b): return self.math('MINIMUM', a, b)
    def mx(self, a, b): return self.math('MAXIMUM', a, b)
    def pw(self, a, b): return self.math('POWER', a, b)
    def clamp(self, a): return self.math('ADD', a, 0.0, clamp=True)

    def lerp(self, a, b, t):
        nd = self.node('ShaderNodeMix', data_type='FLOAT', clamp_factor=True)
        self.link(t, self.sock(nd, 'Factor', 'VALUE'))
        self.link(a, self.sock(nd, 'A', 'VALUE'))
        self.link(b, self.sock(nd, 'B', 'VALUE'))
        return S(self, self.sock(nd, 'Result', 'VALUE', out=True))

    def smooth(self, e0, e1, x):
        """smoothstep(e0, e1, x); e0 > e1 gives a falling edge."""
        nd = self.node('ShaderNodeMapRange', data_type='FLOAT', interpolation_type='SMOOTHSTEP', clamp=True)
        self.link(x, nd.inputs['Value'])
        self.link(e0, nd.inputs['From Min'])
        self.link(e1, nd.inputs['From Max'])
        nd.inputs['To Min'].default_value = 0.0
        nd.inputs['To Max'].default_value = 1.0
        return S(self, nd.outputs['Result'])

    def remap(self, x, a0, a1, b0, b1, clamp=True):
        nd = self.node('ShaderNodeMapRange', data_type='FLOAT', interpolation_type='LINEAR', clamp=clamp)
        self.link(x, nd.inputs['Value'])
        self.link(a0, nd.inputs['From Min'])
        self.link(a1, nd.inputs['From Max'])
        self.link(b0, nd.inputs['To Min'])
        self.link(b1, nd.inputs['To Max'])
        return S(self, nd.outputs['Result'])

    # ------------------------------------------------------------------ colours
    def rgb(self, r, g, b):
        nd = self.node('ShaderNodeRGB')
        nd.outputs[0].default_value = (r, g, b, 1.0)
        return C(self, nd.outputs[0])

    def gray(self, s):
        nd = self.node('ShaderNodeCombineXYZ')
        for i in range(3):
            self.link(s, nd.inputs[i])
        return C(self, nd.outputs[0])

    def cmix(self, blend, fac, a, b):
        nd = self.node('ShaderNodeMix', data_type='RGBA', blend_type=blend, clamp_factor=True, clamp_result=False)
        self.link(fac, self.sock(nd, 'Factor', 'VALUE'))
        self.link(a, self.sock(nd, 'A', 'RGBA'))
        self.link(b, self.sock(nd, 'B', 'RGBA'))
        return C(self, self.sock(nd, 'Result', 'RGBA', out=True))

    def mixc(self, a, b, t): return self.cmix('MIX', t, a, b)

    def scale(self, c, s):
        """Colour * scalar."""
        return self.cmix('MULTIPLY', 1.0, c, self.gray(s) if isinstance(s, S) else (s, s, s, 1.0))

    def ramp(self, x, stops):
        """stops: [(pos, (r,g,b)), ...] - a colour ramp, linear interpolation."""
        nd = self.node('ShaderNodeValToRGB')
        cr = nd.color_ramp
        cr.interpolation = 'LINEAR'
        while len(cr.elements) < len(stops):
            cr.elements.new(0.5)
        for el, (p, col) in zip(cr.elements, stops):
            el.position = p
            el.color = (*col, 1.0)
        self.link(x, nd.inputs[0])
        return C(self, nd.outputs[0])

    # ------------------------------------------------------------------ coordinates
    def uv(self):
        if self._uv is None:
            tc = self.node('ShaderNodeTexCoord')
            sep = self.node('ShaderNodeSeparateXYZ')
            self.l.new(tc.outputs['UV'], sep.inputs[0])
            self._uv = (S(self, sep.outputs[0]), S(self, sep.outputs[1]))
        return self._uv

    def torus(self, su, sv, seed=0.0, warp_u=None, warp_v=None):
        """4D torus coordinates: Scale=1 noise then has about su x sv features across the tile."""
        u, v = self.uv()
        if warp_u is not None:
            u = u + warp_u
        if warp_v is not None:
            v = v + warp_v
        ru, rv = su / TAU, sv / TAU
        au, av = u * TAU, v * TAU
        x = self.cos(au) * ru + (seed * 3.17 + 11.3)
        y = self.sin(au) * ru + (seed * 1.73 + 5.1)
        z = self.cos(av) * rv + (seed * 2.31 + 7.9)
        w = self.sin(av) * rv + (seed * 0.91 + 3.3)
        vec = self.node('ShaderNodeCombineXYZ')
        self.link(x, vec.inputs[0]); self.link(y, vec.inputs[1]); self.link(z, vec.inputs[2])
        return vec.outputs[0], w

    def noise(self, su, sv=None, detail=4.0, rough=0.5, seed=0.0, distortion=0.0, lac=2.0, warp_u=None, warp_v=None, color=False):
        sv = su if sv is None else sv
        vec, w = self.torus(su, sv, seed, warp_u, warp_v)
        nd = self.node('ShaderNodeTexNoise', noise_dimensions='4D', normalize=True)
        self.l.new(vec, nd.inputs['Vector'])
        self.link(w, nd.inputs['W'])
        nd.inputs['Scale'].default_value = 1.0
        nd.inputs['Detail'].default_value = detail
        nd.inputs['Roughness'].default_value = rough
        nd.inputs['Lacunarity'].default_value = lac
        nd.inputs['Distortion'].default_value = distortion
        if color:
            return C(self, nd.outputs['Color'])
        return S(self, nd.outputs['Factor'])

    def voronoi(self, su, sv=None, feature='F1', seed=0.0, randomness=1.0, warp_u=None, warp_v=None, metric='EUCLIDEAN'):
        """Returns (distance, colour) - distance-to-edge when feature='DISTANCE_TO_EDGE'."""
        sv = su if sv is None else sv
        vec, w = self.torus(su, sv, seed, warp_u, warp_v)
        nd = self.node('ShaderNodeTexVoronoi', voronoi_dimensions='4D', feature=feature, distance=metric)
        self.l.new(vec, nd.inputs['Vector'])
        self.link(w, nd.inputs['W'])
        nd.inputs['Scale'].default_value = 1.0
        nd.inputs['Randomness'].default_value = randomness
        col = nd.outputs.get('Color')
        return S(self, nd.outputs['Distance']), (C(self, col) if col is not None else None)

    def white(self, s):
        """Hash of a scalar (stable random 0..1 per integer cell index)."""
        nd = self.node('ShaderNodeTexWhiteNoise', noise_dimensions='1D')
        self.link(s, nd.inputs['W'])
        return S(self, nd.outputs['Value'])

    def chan(self, c, i):
        sep = self.node('ShaderNodeSeparateColor')
        self.link(c, sep.inputs[0])
        return S(self, sep.outputs[i])

    def comb(self, r, g, b):
        nd = self.node('ShaderNodeCombineColor')
        self.link(r, nd.inputs[0]); self.link(g, nd.inputs[1]); self.link(b, nd.inputs[2])
        return C(self, nd.outputs[0])

    # ------------------------------------------------------------------ output
    def emit(self, src):
        """Routes a colour or scalar into an Emission shader on the material output (for EMIT baking)."""
        out = next((x for x in self.n if x.bl_idname == 'ShaderNodeOutputMaterial'), None) or self.node('ShaderNodeOutputMaterial')
        em = next((x for x in self.n if x.bl_idname == 'ShaderNodeEmission'), None) or self.node('ShaderNodeEmission')
        for lk in list(em.inputs['Color'].links):
            self.l.remove(lk)
        if isinstance(src, S):
            src = self.gray(src)
        self.link(src, em.inputs['Color'])
        em.inputs['Strength'].default_value = 1.0
        if not em.outputs[0].links:
            self.l.new(em.outputs[0], out.inputs['Surface'])
