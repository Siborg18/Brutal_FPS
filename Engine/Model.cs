using System.Runtime.InteropServices;
using Brutal.GltfApi;
using Brutal.Numerics;

namespace BrutalFps.Engine;

/// <summary>How one part of a model is shaded.</summary>
/// <param name="Material">Texture set (or <see cref="Material.Plain"/> / <see cref="Material.Emissive"/> for flat surfaces).</param>
/// <param name="Color">Base tint, in the game's display-space tint convention (as the old procedural colours were).</param>
/// <param name="TakesInstanceTint">True for paint: the colour passed when drawing the model replaces <paramref name="Color"/>.</param>
/// <param name="Roughness">For flat (untextured) surfaces.</param>
/// <param name="Metalness">For flat (untextured) surfaces.</param>
/// <param name="Custom">The model's own texture set (materials named <c>tex_*</c> / <c>leaf_*</c>), used through the UVs as baked.</param>
public readonly record struct PartMaterial(Material Material, float3 Color, bool TakesInstanceTint, float Roughness, float Metalness, TextureSet? Custom = null);

/// <summary>
/// Paint the level editor gives the things on an item that are painted on their own, its "slots" (a box's six
/// faces, a model's parts, a code-built mesh as a whole): a texture set each (a <see cref="Material"/>, -1 as built)
/// and a colour each (null as built).
/// </summary>
public sealed record SlotPaint(int[] Textures, float3?[] Colors)
{
    /// <summary>Nothing painted, on <paramref name="slots"/> slots.</summary>
    public static SlotPaint Empty(int slots) => new([.. Enumerable.Repeat(-1, slots)], new float3?[slots]);

    public int Count => Textures.Length;

    /// <summary>Every slot as built.</summary>
    public bool IsEmpty => Array.TrueForAll(Textures, t => t < 0) && Array.TrueForAll(Colors, c => c is null);

    public SlotPaint Copy() => new([.. Textures], [.. Colors]);

    public Material? TextureAt(int slot) => slot < Textures.Length && Textures[slot] >= 0 ? (Material)Textures[slot] : null;

    public float3? ColorAt(int slot) => slot < Colors.Length ? Colors[slot] : null;

    public bool SameAs(SlotPaint? other) =>
        other is not null && Textures.AsSpan().SequenceEqual(other.Textures) && Colors.SequenceEqual(other.Colors);

    /// <summary>
    /// A model part's material in another texture set and colour (either as it was when null). A colour of its own
    /// is drawn as it is, not the colour the model is drawn with; pure white shows the texture's own colours.
    /// </summary>
    public static PartMaterial Apply(in PartMaterial material, Material? texture, float3? color)
    {
        var m = material;
        if (texture is { } t)
            m = m with { Material = t, Custom = null };
        if (color is { } c)
            m = m with { Color = c, TakesInstanceTint = false };
        return m;
    }
}

/// <summary>One drawable piece of a model: a node of the glTF scene with one material.</summary>
public sealed class ModelPart(string name, Mesh mesh, PartMaterial material, float[] vertices, uint[]? indices = null)
{
    private float[]? _triangles = indices is null ? vertices : null;

    /// <summary>The Blender object name (several parts share it when an object has several materials).</summary>
    public string Name { get; } = name;
    public Mesh Mesh { get; } = mesh;

    /// <summary>How it's drawn: as loaded, unless the level editor has given it another texture set or colour (<see cref="Repaint"/>).</summary>
    public PartMaterial Material { get; private set; } = material;

    /// <summary>The material as it was loaded from the file.</summary>
    public PartMaterial BuiltMaterial { get; } = material;

    /// <summary>
    /// Draws it in the texture set <paramref name="texture"/> and colour <paramref name="color"/> (keeping its
    /// roughness and metalness); either as loaded when null.
    /// </summary>
    public void Repaint(Material? texture, float3? color) => Material = SlotPaint.Apply(BuiltMaterial, texture, color);

    /// <summary>CPU copy of the distinct 12-float vertices (model space), for merging instances.</summary>
    public float[] SharedVertices { get; } = vertices;

    /// <summary>Which of <see cref="SharedVertices"/> each triangle uses (null: every three are a triangle).</summary>
    public uint[]? Indices { get; } = indices;

    /// <summary>The 12-float vertices of every triangle, three each (model space).</summary>
    public float[] Vertices
    {
        get
        {
            if (_triangles is not null)
                return _triangles;
            var triangles = new float[Indices!.Length * Mesh.FullStride];
            for (var i = 0; i < Indices.Length; i++)
                Array.Copy(SharedVertices, Indices[i] * Mesh.FullStride, triangles, i * Mesh.FullStride, Mesh.FullStride);
            return _triangles = triangles;
        }
    }
}

/// <summary>
/// A model exported from Blender as glTF binary (.glb), loaded with BRUTAL's glTF bindings (<see cref="GltfUtility"/>).
/// Node transforms are baked into the vertices, so every part is in model space; parts are named after their
/// Blender objects so they can be animated separately (e.g. the pistol's slide).
/// <para>
/// Material naming convention (Blender material name -> shading): the name of a texture set (<c>corrugated</c>,
/// <c>wood_planks</c>, ...) uses that tileable PBR set through the mesh's UVs; append <c>_tint</c> to take the colour
/// the game draws the model with (container paint, car paint); <c>flat_*</c> is an untextured surface using the
/// material's base colour, roughness and metalness; <c>emit_*</c> glows in its base colour.
/// </para>
/// </summary>
public sealed class Model
{
    public List<ModelPart> Parts { get; }   // (the list itself, not an interface: a foreach over it then makes no garbage)

    /// <summary>Bounds of the whole model (model space).</summary>
    public Aabb Bounds { get; }

    /// <summary>The name it was loaded by with <see cref="Get"/> (Assets/Models/<c>Name</c>.glb), or "" if it wasn't.</summary>
    public string Name { get; private set; } = "";

    private Model(List<ModelPart> parts, Aabb bounds)
    {
        Parts = parts;
        Bounds = bounds;
    }

    /// <summary>Wraps meshes from elsewhere (e.g. an OBJ file) as a model.</summary>
    public static Model FromMeshes(IEnumerable<(string Name, Mesh Mesh, PartMaterial Material)> meshes)
    {
        var parts = meshes.Select(m => new ModelPart(m.Name, m.Mesh, m.Material, [])).ToList();
        return new Model(parts, new Aabb(float3.Zero, float3.Zero));
    }

    private static readonly Dictionary<string, Model> Cache = [];

    /// <summary>Loads (once) <c>Assets/Models/{name}.glb</c>.</summary>
    public static Model Get(string name)
    {
        if (Cache.TryGetValue(name, out var model))
            return model;
        model = Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Models", name + ".glb"));
        model.Name = name;
        Cache[name] = model;
        if (Repaints.TryGetValue(name, out var paint))
            model.ApplyRepaint(paint);
        return model;
    }

    // Texture sets and colours given to every copy of a model in the level editor (Game/LevelEditing.cs), by model name.
    private static readonly Dictionary<string, SlotPaint> Repaints = [];

    /// <summary>
    /// Gives every copy of Assets/Models/<paramref name="name"/>.glb this paint, a texture set and colour per part,
    /// or puts it back as loaded (null): now if it's loaded, else when it is.
    /// </summary>
    public static void SetRepaint(string name, SlotPaint? paint)
    {
        if (paint is null || paint.IsEmpty)
            Repaints.Remove(name);
        else
            Repaints[name] = paint.Copy();
        if (Cache.TryGetValue(name, out var model))
            model.ApplyRepaint(Repaints.GetValueOrDefault(name));
    }

    /// <summary>The paint given to every copy of the model (see <see cref="SetRepaint"/>), or null.</summary>
    public static SlotPaint? RepaintOf(string name) => Repaints.GetValueOrDefault(name);

    /// <summary>Every model given paint (see <see cref="SetRepaint"/>).</summary>
    public static IReadOnlyDictionary<string, SlotPaint> AllRepaints => Repaints;

    private void ApplyRepaint(SlotPaint? paint)
    {
        for (var i = 0; i < Parts.Count; i++)
            Parts[i].Repaint(paint?.TextureAt(i), paint?.ColorAt(i));
    }

    public static Model Load(string path)
    {
        var gltf = GltfUtility.LoadModel(path);
        var buffer = GltfUtility.LoadBinaryBuffer(gltf, 0, path);
        var parts = new List<ModelPart>();
        var min = new float3(float.MaxValue);
        var max = new float3(float.MinValue);

        var sceneIndex = gltf.Scene ?? 0;
        var roots = gltf.Scenes is { Length: > 0 } ? gltf.Scenes[sceneIndex].Nodes : Enumerable.Range(0, gltf.Nodes?.Length ?? 0).ToArray();
        foreach (var root in roots ?? [])
            Visit(root, float4x4.Identity);

        if (parts.Count == 0)
            throw new FormatException($"{path} has no meshes.");
        return new Model(parts, new Aabb(min, max));

        void Visit(int nodeIndex, float4x4 parentWorld)
        {
            var node = gltf.Nodes![nodeIndex];
            var world = LocalMatrix(node) * parentWorld;
            if (node.Mesh is { } meshIndex)
            {
                var mesh = gltf.Meshes[meshIndex];
                foreach (var primitive in mesh.Primitives)
                {
                    var (vertices, indices) = ReadPrimitive(gltf, buffer, primitive, world, ref min, ref max);
                    var material = primitive.Material is { } m ? ToPartMaterial(gltf.Materials[m]) : new PartMaterial(Engine.Material.Plain, new float3(0.6f), false, 0.7f, 0f);
                    parts.Add(new ModelPart(node.Name ?? mesh.Name ?? $"node{nodeIndex}", new Mesh(vertices, Mesh.FullStride, indices), material, vertices, indices));
                }
            }

            foreach (var child in node.Children ?? [])
                Visit(child, world);
        }
    }

    /// <summary>How wide the patches of ground are that <see cref="MergeInstances"/> merges together (m).</summary>
    private const float MergeCell = 30f;

    /// <summary>
    /// Merges many copies of a model into one mesh per part for each patch of ground (e.g. a thousand bushes in a few
    /// draw calls a patch), so that what's out of view isn't drawn: a mesh of the whole forest always would be.
    /// </summary>
    public static List<(Mesh Mesh, PartMaterial Material)> MergeInstances(Model model, IReadOnlyList<float4x4> instances)
    {
        var patches = new Dictionary<(int, int), List<float4x4>>();
        foreach (var m in instances)
        {
            var key = ((int)float.Floor(m.W.X / MergeCell), (int)float.Floor(m.W.Z / MergeCell));
            if (!patches.TryGetValue(key, out var patch))
                patches[key] = patch = [];
            patch.Add(m);
        }

        var result = new List<(Mesh, PartMaterial)>();
        foreach (var part in model.Parts)
        {
            var src = part.SharedVertices;
            var count = src.Length / Mesh.FullStride;
            if (count == 0)
                continue;
            var srcIndices = part.Indices ?? Enumerable.Range(0, count).Select(i => (uint)i).ToArray();
            foreach (var patch in patches.Values)
            {
                var data = new float[src.Length * patch.Count];
                var indices = new uint[srcIndices.Length * patch.Count];
                var o = 0;
                var k = 0;
                for (var c = 0; c < patch.Count; c++)
                {
                    var m = patch[c];
                    var first = (uint)(c * count);
                    foreach (var index in srcIndices)
                        indices[k++] = first + index;
                    for (var i = 0; i < src.Length; i += Mesh.FullStride)
                    {
                        var p = float3.Transform(new float3(src[i], src[i + 1], src[i + 2]), m);
                        var n = Normalize(float3.TransformNormal(new float3(src[i + 3], src[i + 4], src[i + 5]), m));
                        var t = Normalize(float3.TransformNormal(new float3(src[i + 8], src[i + 9], src[i + 10]), m));
                        data[o++] = p.X; data[o++] = p.Y; data[o++] = p.Z;
                        data[o++] = n.X; data[o++] = n.Y; data[o++] = n.Z;
                        data[o++] = src[i + 6]; data[o++] = src[i + 7];
                        data[o++] = t.X; data[o++] = t.Y; data[o++] = t.Z; data[o++] = src[i + 11];
                    }
                }

                result.Add((new Mesh(data, Mesh.FullStride, indices), part.Material));
            }
        }

        return result;
    }

    private static float4x4 LocalMatrix(Node node)
    {
        var s = node.Scale;
        if (s.X == 0 && s.Y == 0 && s.Z == 0)
            s = float3.One;
        var r = node.Rotation;
        var q = r.X == 0 && r.Y == 0 && r.Z == 0 && r.W == 0 ? floatQuat.Identity : r;
        return float4x4.CreateScale(s) * float4x4.CreateFromQuaternion(q) * float4x4.CreateTranslation(node.Translation);
    }

    internal static PartMaterial ToPartMaterial(Brutal.GltfApi.Material material)
    {
        var name = (material.Name ?? "").ToLowerInvariant();
        var dot = name.IndexOf('.');
        if (dot >= 0)
            name = name[..dot]; // Blender's ".001" suffixes
        var pbr = material.PbrMetallicRoughness;
        var factor = pbr?.BaseColorFactor ?? new float4(1, 1, 1, 1);
        if (factor.X == 0 && factor.Y == 0 && factor.Z == 0 && factor.W == 0)
            factor = new float4(1, 1, 1, 1);
        // glTF colours are linear; the game's tints are display-space values.
        var color = new float3(float.Pow(factor.X, 1 / 2.2f), float.Pow(factor.Y, 1 / 2.2f), float.Pow(factor.Z, 1 / 2.2f));
        var roughness = pbr?.RoughnessFactor ?? 0.6f;
        var metalness = pbr?.MetallicFactor ?? 0f;

        var tint = name.EndsWith("_tint", StringComparison.Ordinal);
        if (tint)
            name = name[..^5];

        // A model's own texture set (Assets/Textures/<set>_*.png): tex_<set> for surfaces, leaf_<set> / cut_<set> for
        // alpha cut-outs (leaves, grille mesh).
        foreach (var (prefix, kind) in (ReadOnlySpan<(string, Material)>)[("tex_", Engine.Material.Custom), ("leaf_", Engine.Material.Leaves), ("cut_", Engine.Material.Leaves)])
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            var custom = TextureLibrary.Current?.GetCustom(name[prefix.Length..]);
            if (custom is null)
            {
                Log.Write($"[WARN] Texture set for model material '{material.Name}' not found; drawing it flat.");
                return new PartMaterial(Engine.Material.Plain, color, tint, roughness, metalness);
            }

            return new PartMaterial(kind, tint ? color : new float3(1, 1, 1), tint, roughness, metalness, custom);
        }

        if (name.StartsWith("emit", StringComparison.Ordinal))
            return new PartMaterial(Engine.Material.Emissive, color, tint, 1f, 0f);
        if (name.StartsWith("flat", StringComparison.Ordinal))
            return new PartMaterial(Engine.Material.Plain, color, tint, roughness, metalness);

        var set = TextureLibrary.MaterialForSet(name);
        if (set is null)
        {
            Log.Write($"[WARN] Model material '{material.Name}' isn't a known texture set; drawing it flat.");
            return new PartMaterial(Engine.Material.Plain, color, tint, roughness, metalness);
        }

        return new PartMaterial(set.Value, color, tint, roughness, metalness);
    }

    /// <summary>The primitive's distinct vertices (12 floats each, model space) and the triangles that use them.</summary>
    private static (float[] Vertices, uint[] Indices) ReadPrimitive(Gltf gltf, byte[] buffer, MeshPrimitive primitive, in float4x4 world, ref float3 min, ref float3 max)
    {
        var attributes = primitive.Attributes;
        var positions = ReadFloats(gltf, buffer, attributes["POSITION"], 3);
        var normals = attributes.TryGetValue("NORMAL", out var ni) ? ReadFloats(gltf, buffer, ni, 3) : null;
        var uvs = attributes.TryGetValue("TEXCOORD_0", out var ti) ? ReadFloats(gltf, buffer, ti, 2) : null;
        var tangents = attributes.TryGetValue("TANGENT", out var gi) ? ReadFloats(gltf, buffer, gi, 4) : null;
        var count = positions.Length / 3;
        var indices = primitive.Indices is { } ii ? ReadIndices(gltf, buffer, ii) : Enumerable.Range(0, count).ToArray();
        // Blender only exports tangents for some materials; build them from the UVs when they're missing.
        if (tangents is null && uvs is not null && normals is not null)
            tangents = ComputeTangents(positions, normals, uvs, indices);

        var data = new float[count * Mesh.FullStride];
        var o = 0;
        for (var index = 0; index < count; index++)
        {
            var p = float3.Transform(new float3(positions[index * 3], positions[index * 3 + 1], positions[index * 3 + 2]), world);
            var n = normals is null ? float3.UnitY : Normalize(float3.TransformNormal(new float3(normals[index * 3], normals[index * 3 + 1], normals[index * 3 + 2]), world));
            var t = tangents is null ? float3.UnitX : Normalize(float3.TransformNormal(new float3(tangents[index * 4], tangents[index * 4 + 1], tangents[index * 4 + 2]), world));
            var w = tangents is null ? 1f : tangents[index * 4 + 3];
            min = float3.Min(min, p);
            max = float3.Max(max, p);
            data[o++] = p.X; data[o++] = p.Y; data[o++] = p.Z;
            data[o++] = n.X; data[o++] = n.Y; data[o++] = n.Z;
            data[o++] = uvs is null ? 0 : uvs[index * 2];
            data[o++] = uvs is null ? 0 : 1f - uvs[index * 2 + 1]; // glTF's V runs down the image; ours runs up (Blender's)
            data[o++] = t.X; data[o++] = t.Y; data[o++] = t.Z; data[o++] = w;
        }

        var triangles = new uint[indices.Length];
        for (var i = 0; i < indices.Length; i++)
            triangles[i] = (uint)indices[i];
        return (data, triangles);
    }

    /// <summary>Per-vertex tangents (xyz + handedness in w) from the triangles' UV gradients (Lengyel's method).</summary>
    internal static float[] ComputeTangents(float[] positions, float[] normals, float[] uvs, int[] indices)
    {
        var count = positions.Length / 3;
        var tan = new float3[count];
        var bit = new float3[count];
        float3 P(int i) => new(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
        float2 T(int i) => new(uvs[i * 2], 1f - uvs[i * 2 + 1]); // the same V flip as the vertices get
        for (var k = 0; k + 2 < indices.Length; k += 3)
        {
            int i0 = indices[k], i1 = indices[k + 1], i2 = indices[k + 2];
            var e1 = P(i1) - P(i0);
            var e2 = P(i2) - P(i0);
            var d1 = T(i1) - T(i0);
            var d2 = T(i2) - T(i0);
            var det = d1.X * d2.Y - d2.X * d1.Y;
            if (float.Abs(det) < 1e-12f)
                continue;
            var r = 1f / det;
            var t = (e1 * d2.Y - e2 * d1.Y) * r;
            var b = (e2 * d1.X - e1 * d2.X) * r;
            foreach (var i in (ReadOnlySpan<int>)[i0, i1, i2])
            {
                tan[i] += t;
                bit[i] += b;
            }
        }

        var result = new float[count * 4];
        for (var i = 0; i < count; i++)
        {
            var n = new float3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2]);
            var t = tan[i] - n * float3.Dot(n, tan[i]);
            t = float3.Dot(t, t) > 1e-20f ? Normalize(t) : Normalize(float3.Cross(n, float.Abs(n.Y) < 0.9f ? float3.UnitY : float3.UnitX));
            result[i * 4] = t.X;
            result[i * 4 + 1] = t.Y;
            result[i * 4 + 2] = t.Z;
            result[i * 4 + 3] = float3.Dot(float3.Cross(n, t), bit[i]) < 0 ? -1f : 1f;
        }

        return result;
    }

    private static float3 Normalize(float3 v)
    {
        var len = float.Sqrt(float3.Dot(v, v));
        return len > 1e-12f ? v / len : float3.UnitY;
    }

    private static float[] ReadFloats(Gltf gltf, byte[] buffer, int accessorIndex, int components)
    {
        var accessor = gltf.Accessors[accessorIndex];
        if (accessor.ComponentType != Accessor.ComponentTypeEnum.Float)
            throw new FormatException("Only float vertex attributes are supported.");
        var view = gltf.BufferViews[accessor.BufferView ?? throw new FormatException("Sparse accessors aren't supported.")];
        var stride = view.ByteStride ?? components * 4;
        var start = view.ByteOffset + accessor.ByteOffset;
        var result = new float[accessor.Count * components];
        for (var i = 0; i < accessor.Count; i++)
        {
            var src = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(start + i * stride, components * 4));
            src.CopyTo(result.AsSpan(i * components, components));
        }

        return result;
    }

    private static int[] ReadIndices(Gltf gltf, byte[] buffer, int accessorIndex)
    {
        var accessor = gltf.Accessors[accessorIndex];
        var view = gltf.BufferViews[accessor.BufferView ?? 0];
        var start = view.ByteOffset + accessor.ByteOffset;
        var result = new int[accessor.Count];
        var span = buffer.AsSpan(start);
        switch (accessor.ComponentType)
        {
            case Accessor.ComponentTypeEnum.UnsignedByte:
                for (var i = 0; i < result.Length; i++) result[i] = span[i];
                break;
            case Accessor.ComponentTypeEnum.UnsignedShort:
                var u16 = MemoryMarshal.Cast<byte, ushort>(span[..(result.Length * 2)]);
                for (var i = 0; i < result.Length; i++) result[i] = u16[i];
                break;
            default:
                var u32 = MemoryMarshal.Cast<byte, uint>(span[..(result.Length * 4)]);
                for (var i = 0; i < result.Length; i++) result[i] = (int)u32[i];
                break;
        }

        return result;
    }
}
