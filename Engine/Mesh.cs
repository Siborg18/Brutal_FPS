using Brutal.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace BrutalFps.Engine;

/// <summary>
/// A static triangle mesh on the GPU. Two vertex layouts:
/// <list type="bullet">
/// <item>6 floats: position.xyz + normal.xyz (boxes and code-built meshes; textured by world-space triplanar mapping)</item>
/// <item>12 floats: position.xyz + normal.xyz + uv.xy + tangent.xyzw (models from Blender; textured through their UVs)</item>
/// </list>
/// </summary>
public sealed class Mesh
{
    public const int SimpleStride = 6;
    public const int FullStride = 12;

    private GlVertexArray _vao;
    private GlBuffer _vbo;
    private float[]? _pendingUpload;   // (a mesh made with upload: false goes to the GPU the first time it's drawn)

    /// <summary>
    /// The vertices as made (6 floats each, three per triangle), for splitting a merged mesh apart; null once
    /// <see cref="Positions"/> has been asked for, or for an indexed or 12-float mesh.
    /// </summary>
    public float[]? SimpleVertices => _indices is null && _stride == SimpleStride ? _source : null;

    /// <summary>Vertices drawn: three per triangle, whether they're shared or not.</summary>
    public int VertexCount { get; }

    /// <summary>True when the mesh carries UVs and tangents (12-float vertices).</summary>
    public bool HasUVs { get; }

    /// <summary>Bounds of the vertices (model space).</summary>
    public Aabb Bounds { get; }

    /// <summary>
    /// CPU copy of the vertex positions (x, y, z per vertex, three vertices per triangle), for the physics world:
    /// bullets and debris collide with exactly what is drawn. Built when first asked for: leaves never are.
    /// </summary>
    public float[] Positions => _positions ??= BuildPositions();

    private float[]? _positions;
    private float[]? _source;
    private readonly uint[]? _indices;
    private readonly int _stride;

    /// <param name="indices">Three per triangle, into <paramref name="vertices"/>; null: every three vertices are a triangle. (The GPU shades a shared vertex once instead of once per triangle.)</param>
    /// <param name="upload">False: not on the GPU until it's first drawn (a level's pieces, mostly drawn merged, rarely by themselves).</param>
    public Mesh(float[] vertices, int floatsPerVertex = SimpleStride, uint[]? indices = null, bool upload = true)
    {
        if (floatsPerVertex is not (SimpleStride or FullStride))
            throw new ArgumentOutOfRangeException(nameof(floatsPerVertex));

        _stride = floatsPerVertex;
        _source = vertices;
        _indices = indices;
        VertexCount = indices?.Length ?? vertices.Length / floatsPerVertex;
        HasUVs = floatsPerVertex == FullStride;
        var min = new float3(float.MaxValue);
        var max = new float3(float.MinValue);
        for (var i = 0; i + 2 < vertices.Length; i += floatsPerVertex)
        {
            var p = new float3(vertices[i], vertices[i + 1], vertices[i + 2]);
            min = float3.Min(min, p);
            max = float3.Max(max, p);
        }
        Bounds = vertices.Length > 0 ? new Aabb(min, max) : default;
        if (upload)
            Upload(vertices);
        else
            _pendingUpload = vertices;
    }

    private void Upload(float[] vertices)
    {
        using var upload = Perf.MeshUpload.Time();
        _vao = GenVertexArray().Bind();
        _vbo = GenBuffer().Bind(GlBindTarget.ArrayBuffer);
        BufferData(GlBindTarget.ArrayBuffer, vertices, GlBufferUsage.StaticDraw);
        if (_indices is not null)
        {
            GenBuffer().Bind(GlBindTarget.ElementArrayBuffer);   // recorded in the vertex array
            BufferData(GlBindTarget.ElementArrayBuffer, _indices, GlBufferUsage.StaticDraw);
        }

        SetupAttributes(_stride * sizeof(float), HasUVs);
    }

    /// <summary>The vertex layout, recorded into the bound vertex array: position, normal, and maybe uv and tangent.</summary>
    internal static void SetupAttributes(int stride, bool uvs)
    {
        VertexAttribute(new GlAttribute(0, 3, GL_FLOAT, GlScaling.Float, stride, 0));
        EnableVertexAttribArray(0);
        VertexAttribute(new GlAttribute(1, 3, GL_FLOAT, GlScaling.Float, stride, 3 * sizeof(float)));
        EnableVertexAttribArray(1);
        if (uvs)
        {
            VertexAttribute(new GlAttribute(2, 2, GL_FLOAT, GlScaling.Float, stride, 6 * sizeof(float)));
            EnableVertexAttribArray(2);
            VertexAttribute(new GlAttribute(3, 4, GL_FLOAT, GlScaling.Float, stride, 8 * sizeof(float)));
            EnableVertexAttribArray(3);
        }
    }

    public void Draw()
    {
        if (_pendingUpload is { } pending)
        {
            _pendingUpload = null;
            Upload(pending);
        }
        _vao.Bind();
        if (_indices is not null)
            DrawIndexed(GlTopology.Triangles, GlIndexType.UInt, VertexCount, 1, 0, 0, 0);
        else
            OpenGl.Draw(GlTopology.Triangles, VertexCount, 1, 0, 0);
        if (FrameStats.Active)
            FrameStats.DrawCalls++;
    }

    private float[] BuildPositions()
    {
        var vertices = _source!;
        var positions = new float[VertexCount * 3];
        for (var i = 0; i < VertexCount; i++)
        {
            var v = (int)(_indices?[i] ?? (uint)i) * _stride;
            positions[i * 3] = vertices[v];
            positions[i * 3 + 1] = vertices[v + 1];
            positions[i * 3 + 2] = vertices[v + 2];
        }

        _source = null;
        return positions;
    }
}

/// <summary>
/// A mesh whose vertices are rewritten while the game runs (bullet holes, particles): 12-float vertices
/// (position, normal, uv, tangent), drawn as triangles. The buffer grows as needed and is only re-uploaded when the
/// contents change.
/// </summary>
public sealed class DynamicMesh
{
    private GlVertexArray _vao;
    private GlBuffer _vbo;
    private float[] _data = new float[Mesh.FullStride * 6 * 64];
    private int _floats;
    private bool _dirty;

    public int VertexCount => _floats / Mesh.FullStride;

    public DynamicMesh()
    {
        _vao = GenVertexArray().Bind();
        _vbo = GenBuffer().Bind(GlBindTarget.ArrayBuffer);
        BufferData(GlBindTarget.ArrayBuffer, _data, GlBufferUsage.DynamicDraw);
        Mesh.SetupAttributes(Mesh.FullStride * sizeof(float), uvs: true);
    }

    public void Clear()
    {
        _floats = 0;
        _dirty = true;
    }

    /// <summary>Adds one vertex: position, normal, uv, tangent (or whatever the shader mode reads from them).</summary>
    public void Vertex(float3 position, float3 normal, float2 uv, float4 tangent)
    {
        if (_floats + Mesh.FullStride > _data.Length)
            Array.Resize(ref _data, _data.Length * 2);
        var d = _data;
        var i = _floats;
        d[i] = position.X; d[i + 1] = position.Y; d[i + 2] = position.Z;
        d[i + 3] = normal.X; d[i + 4] = normal.Y; d[i + 5] = normal.Z;
        d[i + 6] = uv.X; d[i + 7] = uv.Y;
        d[i + 8] = tangent.X; d[i + 9] = tangent.Y; d[i + 10] = tangent.Z; d[i + 11] = tangent.W;
        _floats += Mesh.FullStride;
        _dirty = true;
    }

    public void Draw()
    {
        if (_floats == 0)
            return;
        _vao.Bind();
        if (_dirty)
        {
            _vbo.Bind(GlBindTarget.ArrayBuffer);
            BufferData(GlBindTarget.ArrayBuffer, new ReadOnlySpan<float>(_data, 0, _floats), GlBufferUsage.DynamicDraw);
            _dirty = false;
        }
        OpenGl.Draw(GlTopology.Triangles, VertexCount, 1, 0, 0);
    }
}

/// <summary>Builds simple procedural meshes. Triangles are wound counter-clockwise when seen from the side the normal points to.</summary>
public sealed class MeshBuilder
{
    private readonly List<float> _data = [];

    public Mesh Build() => new([.. _data]);

    /// <summary>The vertices so far (6 floats each: position, normal), for <see cref="Mesh(float[], int, uint[])"/>.</summary>
    public float[] ToArray() => [.. _data];

    /// <summary>How much has been added so far (a mark for <see cref="MoveSince"/>).</summary>
    public int Mark => _data.Count;

    /// <summary>True when nothing has been added.</summary>
    public bool IsEmpty => _data.Count == 0;

    /// <summary>Moves everything added since <paramref name="mark"/> into <paramref name="into"/>; true when there was any.</summary>
    public bool MoveSince(int mark, MeshBuilder into)
    {
        if (mark >= _data.Count)
            return false;
        for (var i = mark; i < _data.Count; i++)
            into._data.Add(_data[i]);
        _data.RemoveRange(mark, _data.Count - mark);
        return true;
    }

    /// <summary>Adds a triangle, flipping its winding if needed so it faces <paramref name="normal"/>.</summary>
    public void Triangle(float3 a, float3 b, float3 c, float3 normal)
    {
        if (float3.Dot(float3.Cross(b - a, c - a), normal) < 0)
            (b, c) = (c, b);

        Vertex(a, normal);
        Vertex(b, normal);
        Vertex(c, normal);
    }

    /// <summary>A smooth-shaded triangle, with a normal for each corner; the normals say which way it faces.</summary>
    public void Triangle(float3 a, float3 normalA, float3 b, float3 normalB, float3 c, float3 normalC)
    {
        if (float3.Dot(float3.Cross(b - a, c - a), normalA + normalB + normalC) < 0)
        {
            (b, c) = (c, b);
            (normalB, normalC) = (normalC, normalB);
        }

        Vertex(a, normalA);
        Vertex(b, normalB);
        Vertex(c, normalC);
    }

    private void Vertex(float3 p, float3 n)
    {
        _data.Add(p.X);
        _data.Add(p.Y);
        _data.Add(p.Z);
        _data.Add(n.X);
        _data.Add(n.Y);
        _data.Add(n.Z);
    }

    /// <summary>
    /// A thick ring (a disc with a hole, extruded along Z): front face at <paramref name="zFront"/>, back face at
    /// <paramref name="zBack"/>, plus its outer and inner walls. Centred on the origin in the XY plane.
    /// </summary>
    public MeshBuilder Ring(float innerRadius, float outerRadius, float zBack, float zFront, int segments = 72)
    {
        for (var i = 0; i < segments; i++)
        {
            var a0 = float.Tau * i / segments;
            var a1 = float.Tau * (i + 1) / segments;
            var d0 = new float3(float.Cos(a0), float.Sin(a0), 0);
            var d1 = new float3(float.Cos(a1), float.Sin(a1), 0);
            var mid = float3.Normalize(d0 + d1);

            var fi0 = d0 * innerRadius + new float3(0, 0, zFront);
            var fi1 = d1 * innerRadius + new float3(0, 0, zFront);
            var fo0 = d0 * outerRadius + new float3(0, 0, zFront);
            var fo1 = d1 * outerRadius + new float3(0, 0, zFront);
            var bi0 = d0 * innerRadius + new float3(0, 0, zBack);
            var bi1 = d1 * innerRadius + new float3(0, 0, zBack);
            var bo0 = d0 * outerRadius + new float3(0, 0, zBack);
            var bo1 = d1 * outerRadius + new float3(0, 0, zBack);

            // Front and back faces.
            Quad(fi0, fo0, fo1, fi1, float3.UnitZ);
            Quad(bi0, bo0, bo1, bi1, -float3.UnitZ);

            // Outer wall (faces outwards) and inner wall (faces the centre). Per-face normals give a slightly faceted metal look.
            Quad(fo0, bo0, bo1, fo1, mid);
            Quad(fi0, bi0, bi1, fi1, -mid);
        }

        return this;
    }

    /// <summary>
    /// A square plate with a round hole (the hole's wall faces the centre), extruded along Z. Used to fill the corners
    /// of the square opening cut for a portal, so the doorway looks round.
    /// </summary>
    public MeshBuilder SquarePlateWithHole(float holeRadius, float halfSize, float zBack, float zFront, int segments = 72)
    {
        for (var i = 0; i < segments; i++)
        {
            var a0 = float.Tau * i / segments;
            var a1 = float.Tau * (i + 1) / segments;
            var d0 = new float3(float.Cos(a0), float.Sin(a0), 0);
            var d1 = new float3(float.Cos(a1), float.Sin(a1), 0);

            // Where each direction meets the square's edge.
            var s0 = d0 * (halfSize / float.Max(float.Abs(d0.X), float.Abs(d0.Y)));
            var s1 = d1 * (halfSize / float.Max(float.Abs(d1.X), float.Abs(d1.Y)));
            var front = new float3(0, 0, zFront);
            var back = new float3(0, 0, zBack);

            Quad(d0 * holeRadius + front, s0 + front, s1 + front, d1 * holeRadius + front, float3.UnitZ);
            Quad(d0 * holeRadius + back, s0 + back, s1 + back, d1 * holeRadius + back, -float3.UnitZ);
            Quad(d0 * holeRadius + front, d0 * holeRadius + back, d1 * holeRadius + back, d1 * holeRadius + front, -float3.Normalize(d0 + d1));
        }

        return this;
    }

    /// <summary>A closed cylinder along Z (radius, from zBack to zFront).</summary>
    public MeshBuilder Cylinder(float radius, float zBack, float zFront, int segments = 24)
    {
        for (var i = 0; i < segments; i++)
        {
            var a0 = float.Tau * i / segments;
            var a1 = float.Tau * (i + 1) / segments;
            var d0 = new float3(float.Cos(a0), float.Sin(a0), 0) * radius;
            var d1 = new float3(float.Cos(a1), float.Sin(a1), 0) * radius;
            var front = new float3(0, 0, zFront);
            var back = new float3(0, 0, zBack);

            Triangle(front, d0 + front, d1 + front, float3.UnitZ);
            Triangle(back, d0 + back, d1 + back, -float3.UnitZ);
            Quad(d0 + front, d0 + back, d1 + back, d1 + front, float3.Normalize(d0 + d1));
        }

        return this;
    }

    /// <summary>An axis-aligned box (in the mesh's own space).</summary>
    public MeshBuilder Box(float3 center, float3 size) => Box(center, size, 0b111111);

    /// <summary>
    /// Some of an axis-aligned box's faces: bit 0 +x, 1 -x, 2 +y, 3 -y, 4 +z, 5 -z.
    /// </summary>
    public MeshBuilder Box(float3 center, float3 size, int faceMask)
    {
        var h = size * 0.5f;
        ReadOnlySpan<(float3 N, float3 U, float3 V)> faces =
        [
            (new float3(1, 0, 0), new float3(0, 1, 0), new float3(0, 0, 1)),
            (new float3(-1, 0, 0), new float3(0, 0, 1), new float3(0, 1, 0)),
            (new float3(0, 1, 0), new float3(0, 0, 1), new float3(1, 0, 0)),
            (new float3(0, -1, 0), new float3(1, 0, 0), new float3(0, 0, 1)),
            (new float3(0, 0, 1), new float3(1, 0, 0), new float3(0, 1, 0)),
            (new float3(0, 0, -1), new float3(0, 1, 0), new float3(1, 0, 0)),
        ];
        for (var i = 0; i < faces.Length; i++)
        {
            if ((faceMask & (1 << i)) == 0)
                continue;
            var (n, u, v) = faces[i];
            var c = center + n * h;
            float3 P(float a, float b) => c + u * (a * float3.Dot(u, h)) + v * (b * float3.Dot(v, h));
            Quad(P(-1, -1), P(1, -1), P(1, 1), P(-1, 1), n);
        }

        return this;
    }

    /// <summary>
    /// A lumpy, faceted ball (for hills): a low-poly sphere with each vertex pushed in or out a little.
    /// Faces use flat normals.
    /// </summary>
    public MeshBuilder Blob(float3 center, float3 radii, Random random, int around = 9, int rings = 6)
    {
        var points = new float3[rings + 1, around];
        for (var r = 0; r <= rings; r++)
        {
            var phi = float.Pi * r / rings;
            for (var a = 0; a < around; a++)
            {
                var theta = float.Tau * a / around;
                var dir = new float3(float.Sin(phi) * float.Cos(theta), float.Cos(phi), float.Sin(phi) * float.Sin(theta));
                var jitter = r == 0 || r == rings ? 1f : 0.8f + 0.4f * (float)random.NextDouble();
                points[r, a] = center + dir * radii * jitter;
            }
        }

        for (var r = 0; r < rings; r++)
        {
            for (var a = 0; a < around; a++)
            {
                var a1 = (a + 1) % around;
                var p00 = points[r, a];
                var p01 = points[r, a1];
                var p10 = points[r + 1, a];
                var p11 = points[r + 1, a1];
                var mid = (p00 + p01 + p10 + p11) * 0.25f;
                var outward = float3.Normalize(mid - center);
                var n0 = FaceNormal(p00, p10, p11, outward);
                Triangle(p00, p10, p11, n0);
                var n1 = FaceNormal(p00, p11, p01, outward);
                Triangle(p00, p11, p01, n1);
            }
        }

        return this;
    }

    /// <summary>
    /// A smooth, rolling hill: a dome over an ellipse with fbm-like bumps, smooth normals and enough rings to read
    /// as terrain from a distance.
    /// </summary>
    public MeshBuilder Hill(float3 center, float3 radii, Random random, int around = 40, int rings = 14)
    {
        var phase = new float[6];
        for (var i = 0; i < phase.Length; i++)
            phase[i] = (float)random.NextDouble() * float.Tau;

        float3 Point(float t, float theta)
        {
            // t: 0 at the rim, 1 at the top. A soft dome profile with ridges and gullies running down it.
            var r = 1f - t;
            var wobble = 1f + 0.12f * float.Sin(theta * 3 + phase[0]) + 0.07f * float.Sin(theta * 7 + phase[1]);
            var height = float.Pow(float.Cos(r * float.Pi / 2), 1.6f)
                * (1f + 0.18f * float.Sin(theta * 5 + phase[2]) * r + 0.1f * float.Sin(theta * 11 + phase[3]) * r);
            return center + new float3(float.Cos(theta) * radii.X * r * wobble, height * radii.Y, float.Sin(theta) * radii.Z * r * wobble);
        }

        var pts = new float3[rings + 1, around];
        for (var i = 0; i <= rings; i++)
            for (var a = 0; a < around; a++)
                pts[i, a] = Point(i / (float)rings * 0.999f, float.Tau * a / around);

        // Smooth normals from neighbouring points.
        float3 Normal(int i, int a)
        {
            var aPrev = (a + around - 1) % around;
            var aNext = (a + 1) % around;
            var iPrev = int.Max(i - 1, 0);
            var iNext = int.Min(i + 1, rings);
            var n = float3.Cross(pts[iNext, a] - pts[iPrev, a], pts[i, aNext] - pts[i, aPrev]);
            var len = float.Sqrt(float3.Dot(n, n));
            n = len > 1e-6f ? n / len : float3.UnitY;
            return n.Y < 0 ? -n : n;
        }

        for (var i = 0; i < rings; i++)
        {
            for (var a = 0; a < around; a++)
            {
                var a1 = (a + 1) % around;
                SmoothTriangle(pts[i, a], Normal(i, a), pts[i + 1, a], Normal(i + 1, a), pts[i + 1, a1], Normal(i + 1, a1));
                SmoothTriangle(pts[i, a], Normal(i, a), pts[i + 1, a1], Normal(i + 1, a1), pts[i, a1], Normal(i, a1));
            }
        }

        return this;
    }

    private void SmoothTriangle(float3 a, float3 na, float3 b, float3 nb, float3 c, float3 nc)
    {
        if (float3.Dot(float3.Cross(b - a, c - a), na + nb + nc) < 0)
            ((b, nb), (c, nc)) = ((c, nc), (b, nb));
        Vertex(a, na);
        Vertex(b, nb);
        Vertex(c, nc);
    }

    private static float3 FaceNormal(float3 a, float3 b, float3 c, float3 outward)
    {
        var n = float3.Cross(b - a, c - a);
        var length = float.Sqrt(float3.Dot(n, n));
        if (length < 1e-8f)
            return outward;
        n /= length;
        return float3.Dot(n, outward) < 0 ? -n : n;
    }

    /// <summary>Adds a quad (two triangles), flipped if needed to face <paramref name="normal"/>.</summary>
    public void Quad(float3 a, float3 b, float3 c, float3 d, float3 normal)
    {
        Triangle(a, b, c, normal);
        Triangle(a, c, d, normal);
    }
}
