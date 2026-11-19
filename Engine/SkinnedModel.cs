using System.Runtime.InteropServices;
using System.Text.Json;
using Brutal.GltfApi;
using Brutal.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;
using MeshOpt = Brutal.MeshOptimizer.MeshOptimizer;

namespace BrutalFps.Engine;

/// <summary>A node's local transform: translation, rotation, scale.</summary>
public struct NodePose
{
    public float3 T;
    public floatQuat R;
    public float3 S;

    public readonly float4x4 Matrix => float4x4.CreateScale(S) * float4x4.CreateFromQuaternion(R) * float4x4.CreateTranslation(T);

    public static NodePose Blend(in NodePose a, in NodePose b, float t) => new()
    {
        T = float3.Lerp(a.T, b.T, t),
        R = floatQuat.Normalize(floatQuat.Slerp(a.R, b.R, t)),
        S = float3.Lerp(a.S, b.S, t),
    };
}

/// <summary>Per-clip metadata from clips.json.</summary>
public sealed record ClipInfo(string Name, bool Loop, float Stride, string[] Tags, (string Name, float Time)[] Events)
{
    /// <summary>Loops, stride 0, no tags or events.</summary>
    public static ClipInfo Default(string name) => new(name, true, 0f, [], []);
}

/// <summary>
/// An animation from a glTF file: per-node translation / rotation / scale keys (linear or step interpolation).
/// <see cref="Sample"/> writes the animated nodes into a pose and leaves the others as they are.
/// </summary>
public sealed class AnimationClip
{
    private enum Path { Translation, Rotation, Scale }

    private sealed record Channel(int Node, Path Path, float[] Times, float[] Values, bool Step);

    private readonly Channel[] _channels;

    public string Name { get; }

    /// <summary>Length in seconds (the last key's time).</summary>
    public float Duration { get; }

    /// <summary>Loop / stride / tags / events; <see cref="ClipInfo.Default"/> until a <see cref="ClipLibrary"/> assigns it.</summary>
    public ClipInfo Info { get; internal set; }

    /// <summary>Metres per second at 1x playback (stride / duration), 0 for a stationary clip.</summary>
    public float Speed => Duration > 0 ? Info.Stride / Duration : 0;

    /// <summary>Channels dropped by a retarget because their node has no namesake in the target skeleton.</summary>
    internal int DroppedChannels { get; }

    internal AnimationClip(Gltf gltf, byte[] buffer, Animation animation) : this(gltf, buffer, animation, null)
    {
    }

    /// <param name="nodeRemap">Source node → target node; -1 drops the channel. Null keeps the file's own node numbers.</param>
    internal AnimationClip(Gltf gltf, byte[] buffer, Animation animation, int[]? nodeRemap)
    {
        Name = animation.Name ?? "clip";
        Info = ClipInfo.Default(Name);
        var channels = new List<Channel>();
        foreach (var c in animation.Channels)
        {
            if (c.Target.Node is not { } node)
                continue;
            var path = c.Target.Path switch
            {
                AnimationChannelTarget.PathEnum.Translation => Path.Translation,
                AnimationChannelTarget.PathEnum.Rotation => Path.Rotation,
                AnimationChannelTarget.PathEnum.Scale => Path.Scale,
                _ => (Path?)null,
            };
            if (path is null)
                continue; // morph weights: none on our characters
            var sampler = animation.Samplers[c.Sampler];
            if (sampler.Interpolation == AnimationSampler.InterpolationEnum.Cubicspline)
                throw new FormatException($"Animation '{Name}' uses cubic-spline keys; export it with linear interpolation.");
            var times = GltfData.Read(gltf, buffer, sampler.Input, out _);
            Duration = float.Max(Duration, times[^1]); // a dropped channel still counts towards the length
            if (nodeRemap is not null)
            {
                node = nodeRemap[node];
                if (node < 0)
                {
                    DroppedChannels++;
                    continue;
                }
            }

            var values = GltfData.Read(gltf, buffer, sampler.Output, out _);
            channels.Add(new Channel(node, path.Value, times, values, sampler.Interpolation == AnimationSampler.InterpolationEnum.Step));
        }

        _channels = [.. channels];
    }

    /// <summary>Writes the clip's pose at <paramref name="time"/> (seconds, wrapped round the clip) into <paramref name="pose"/>.</summary>
    public void Sample(float time, Span<NodePose> pose)
    {
        if (Duration > 0)
        {
            time %= Duration;
            if (time < 0)
                time += Duration;
        }

        foreach (var c in _channels)
        {
            var times = c.Times;
            int i0, i1;
            float u;
            if (time <= times[0])
            {
                i0 = i1 = 0;
                u = 0;
            }
            else if (time >= times[^1])
            {
                i0 = i1 = times.Length - 1;
                u = 0;
            }
            else
            {
                var k = Array.BinarySearch(times, time);
                if (k >= 0)
                {
                    i0 = i1 = k;
                    u = 0;
                }
                else
                {
                    i1 = ~k;
                    i0 = i1 - 1;
                    u = c.Step ? 0 : (time - times[i0]) / (times[i1] - times[i0]);
                }
            }

            var v = c.Values;
            ref var p = ref pose[c.Node];
            switch (c.Path)
            {
                case Path.Translation:
                    p.T = float3.Lerp(new float3(v[i0 * 3], v[i0 * 3 + 1], v[i0 * 3 + 2]), new float3(v[i1 * 3], v[i1 * 3 + 1], v[i1 * 3 + 2]), u);
                    break;
                case Path.Scale:
                    p.S = float3.Lerp(new float3(v[i0 * 3], v[i0 * 3 + 1], v[i0 * 3 + 2]), new float3(v[i1 * 3], v[i1 * 3 + 1], v[i1 * 3 + 2]), u);
                    break;
                default:
                    var a = new floatQuat(v[i0 * 4], v[i0 * 4 + 1], v[i0 * 4 + 2], v[i0 * 4 + 3]);
                    var b = new floatQuat(v[i1 * 4], v[i1 * 4 + 1], v[i1 * 4 + 2], v[i1 * 4 + 3]);
                    p.R = u == 0 ? a : floatQuat.Normalize(floatQuat.Slerp(a, b, u));
                    break;
            }
        }
    }
}

/// <summary>
/// The node hierarchy of a skinned glTF model and its skin: which nodes are joints, and their inverse bind matrices.
/// Matrices follow the engine's row-vector convention (v * M), so a joint's skinning matrix is
/// inverseBind * jointWorld.
/// </summary>
public sealed class Skeleton
{
    private readonly int[] _parent;
    private readonly int[] _order;              // every node after its parent
    private readonly int[] _joints;             // skin joint -> node
    private readonly float4x4[] _inverseBind;
    private readonly string[] _names;
    private readonly NodePose[] _rest;

    public int NodeCount => _parent.Length;
    public int JointCount => _joints.Length;
    public ReadOnlySpan<NodePose> RestPose => _rest;

    internal Skeleton(Gltf gltf, byte[] buffer, Skin skin)
    {
        var nodes = gltf.Nodes!;
        var n = nodes.Length;
        _parent = new int[n];
        Array.Fill(_parent, -1);
        _names = new string[n];
        _rest = new NodePose[n];
        for (var i = 0; i < n; i++)
        {
            foreach (var c in nodes[i].Children ?? [])
                _parent[c] = i;
            _names[i] = nodes[i].Name ?? $"node{i}";
            var s = nodes[i].Scale;
            var r = nodes[i].Rotation;
            _rest[i] = new NodePose
            {
                T = nodes[i].Translation,
                R = r.X == 0 && r.Y == 0 && r.Z == 0 && r.W == 0 ? floatQuat.Identity : r,
                S = s.X == 0 && s.Y == 0 && s.Z == 0 ? float3.One : s,
            };
        }

        var order = new List<int>(n);
        var placed = new bool[n];
        for (var i = 0; i < n; i++)
            Place(i);
        _order = [.. order];

        _joints = skin.Joints;
        _inverseBind = new float4x4[_joints.Length];
        if (skin.InverseBindMatrices is { } ibm)
        {
            var m = GltfData.Read(gltf, buffer, ibm, out _);
            for (var j = 0; j < _joints.Length; j++)
                _inverseBind[j] = MemoryMarshal.Read<float4x4>(MemoryMarshal.AsBytes(m.AsSpan(j * 16, 16)));
        }
        else
        {
            Array.Fill(_inverseBind, float4x4.Identity);
        }

        void Place(int i)
        {
            if (placed[i])
                return;
            if (_parent[i] >= 0)
                Place(_parent[i]);
            placed[i] = true;
            order.Add(i);
        }
    }

    public int NodeIndex(string name) => Array.IndexOf(_names, name);

    /// <summary>The node's parent, or -1 for a root.</summary>
    public int Parent(int node) => _parent[node];

    /// <summary>Every node, each after its parent.</summary>
    public ReadOnlySpan<int> Order => _order;

    /// <summary>The skin joint that drives the node called <paramref name="nodeName"/>, or -1 if there is none.</summary>
    public int JointIndex(string nodeName)
    {
        for (var j = 0; j < _joints.Length; j++)
        {
            if (_names[_joints[j]] == nodeName)
                return j;
        }

        return -1;
    }

    internal float4x4 InverseBind(int joint) => _inverseBind[joint];

    public string NodeName(int node) => _names[node];

    /// <summary>The node a skin joint drives.</summary>
    public int JointNode(int joint) => _joints[joint];

    /// <summary>A node and everything below it in the hierarchy.</summary>
    public int[] Subtree(int node) => [.. _order.Where(i => IsUnder(i, node))];

    private bool IsUnder(int i, int ancestor)
    {
        for (; i >= 0; i = _parent[i])
        {
            if (i == ancestor)
                return true;
        }

        return false;
    }

    /// <summary>World (model-space) matrices of every node for <paramref name="pose"/>.</summary>
    public void ComputeWorld(ReadOnlySpan<NodePose> pose, Span<float4x4> world)
    {
        foreach (var i in _order)
        {
            var local = pose[i].Matrix;
            world[i] = _parent[i] >= 0 ? local * world[_parent[i]] : local;
        }
    }

    /// <summary>Skinning matrices (one per joint) from the node world matrices.</summary>
    public void ComputeSkin(ReadOnlySpan<float4x4> world, Span<float4x4> skin)
    {
        for (var j = 0; j < _joints.Length; j++)
            skin[j] = _inverseBind[j] * world[_joints[j]];
    }
}

/// <summary>
/// One material's worth of a skinned mesh on the GPU: indexed 20-float vertices (position, normal, uv, tangent,
/// four joint indices, four weights) and an index buffer holding every level of detail one after another.
/// </summary>
public sealed class SkinnedPart
{
    public const int Stride = 20;

    private sealed class Filtered
    {
        public GlVertexArray Vao;
        public int Count;
    }

    private readonly GlVertexArray _vao;
    private readonly (int ByteOffset, int Count)[] _lods;
    private readonly IReadOnlyList<uint[]> _lodIndices;
    private readonly byte[]? _regions;
    private readonly Action _bindVertexBuffer;
    private readonly Dictionary<(int Lod, uint Mask), Filtered> _filtered = [];

    public string Name { get; }
    public PartMaterial Material { get; }

    /// <summary>True when the mesh carries a per-vertex <c>_REGION</c> attribute (see <see cref="BodyRegions"/>).</summary>
    public bool HasRegions => _regions is not null;

    public int Triangles(int lod) => _lods[int.Min(lod, _lods.Length - 1)].Count / 3;

    internal SkinnedPart(string name, PartMaterial material, float[] vertices, IReadOnlyList<uint[]> lods, byte[]? regions = null)
    {
        Name = name;
        Material = material;
        _lodIndices = lods;
        _regions = regions;
        _vao = GenVertexArray().Bind();
        var vertexBuffer = GenBuffer();
        _bindVertexBuffer = () => vertexBuffer.Bind(GlBindTarget.ArrayBuffer);
        vertexBuffer.Bind(GlBindTarget.ArrayBuffer);
        BufferData(GlBindTarget.ArrayBuffer, vertices, GlBufferUsage.StaticDraw);
        SetupAttributes();

        var all = new uint[lods.Sum(l => l.Length)];
        _lods = new (int, int)[lods.Count];
        var o = 0;
        for (var i = 0; i < lods.Count; i++)
        {
            lods[i].CopyTo(all, o);
            _lods[i] = (o * sizeof(uint), lods[i].Length);
            o += lods[i].Length;
        }

        GenBuffer().Bind(GlBindTarget.ElementArrayBuffer); // recorded in the vertex array
        BufferData(GlBindTarget.ElementArrayBuffer, all, GlBufferUsage.StaticDraw);
        new GlVertexArray(0).Bind();
    }

    /// <summary>The vertex layout, recorded into the bound vertex array (the vertex buffer must be bound).</summary>
    private static void SetupAttributes()
    {
        const int stride = Stride * sizeof(float);
        Mesh.SetupAttributes(stride, uvs: true);
        VertexAttribute(new GlAttribute(4, 4, GL_FLOAT, GlScaling.Float, stride, 12 * sizeof(float)));
        EnableVertexAttribArray(4);
        VertexAttribute(new GlAttribute(5, 4, GL_FLOAT, GlScaling.Float, stride, 16 * sizeof(float)));
        EnableVertexAttribArray(5);
    }

    public void Draw(int lod) => Draw(lod, 0);

    /// <summary>
    /// Draws a level of detail, skipping every triangle whose three vertices are all in a region of
    /// <paramref name="hideRegions"/> (bit i = region i). Mask 0, or a mesh without regions, takes the unfiltered path.
    /// </summary>
    public void Draw(int lod, uint hideRegions)
    {
        lod = int.Min(lod, _lods.Length - 1);
        if (hideRegions == 0 || _regions is null)
        {
            var (offset, count) = _lods[lod];
            _vao.Bind();
            DrawIndexed(GlTopology.Triangles, GlIndexType.UInt, count, 1, offset, 0, 0);
            if (FrameStats.Active)
                FrameStats.DrawCalls++;
            return;
        }

        if (!_filtered.TryGetValue((lod, hideRegions), out var f))
        {
            f = BuildFiltered(lod, hideRegions);
            _filtered[(lod, hideRegions)] = f;
        }

        if (f.Count == 0)
            return;
        f.Vao.Bind();
        DrawIndexed(GlTopology.Triangles, GlIndexType.UInt, f.Count, 1, 0, 0, 0);
        if (FrameStats.Active)
            FrameStats.DrawCalls++;
    }

    private Filtered BuildFiltered(int lod, uint mask)
    {
        var kept = FilterIndices(_lodIndices[lod], _regions!, mask);
        var f = new Filtered { Count = kept.Length };
        f.Vao = GenVertexArray().Bind();
        _bindVertexBuffer();
        SetupAttributes();
        GenBuffer().Bind(GlBindTarget.ElementArrayBuffer); // recorded in this vertex array
        BufferData(GlBindTarget.ElementArrayBuffer, kept.Length > 0 ? kept : new uint[3], GlBufferUsage.StaticDraw);
        new GlVertexArray(0).Bind();
        return f;
    }

    /// <summary>The triangles of <paramref name="indices"/> that have at least one vertex outside <paramref name="mask"/>.</summary>
    internal static uint[] FilterIndices(uint[] indices, byte[] regions, uint mask)
    {
        var kept = new List<uint>(indices.Length);
        for (var k = 0; k + 2 < indices.Length; k += 3)
        {
            var a = indices[k];
            var b = indices[k + 1];
            var c = indices[k + 2];
            if (((mask >> regions[a]) & (mask >> regions[b]) & (mask >> regions[c]) & 1u) != 0)
                continue; // all three hidden
            kept.Add(a);
            kept.Add(b);
            kept.Add(c);
        }

        return [.. kept];
    }
}

/// <summary>The body-region bitmask (see CONTRACTS.md section 1): the <c>_REGION</c> vertex attribute holds 0-14.</summary>
public static class BodyRegions
{
    public const int Count = 15;

    public static readonly string[] Names = ["head", "scalp", "neck", "chest", "belly", "hips", "upperarm_hi", "upperarm_lo",
                                             "forearm", "hand", "thigh_hi", "thigh_lo", "calf_hi", "calf_lo", "foot"];

    /// <summary>Bitmask of the named regions; unknown names are logged ([WARN]) and ignored.</summary>
    public static uint Mask(IEnumerable<string> names)
    {
        uint mask = 0;
        foreach (var name in names)
        {
            var i = Array.IndexOf(Names, name);
            if (i < 0)
                Log.Write($"[WARN] Unknown body region '{name}'.");
            else
                mask |= 1u << i;
        }

        return mask;
    }
}

/// <summary>
/// A skinned, animated model exported from Blender as glTF binary (Assets/Models/{name}.glb): one skin, its
/// animations, and levels of detail built at load time with meshoptimizer's simplifier (they share the vertices;
/// only the index lists differ). Space is the game's (glTF +Y up, as the static <see cref="Model"/>).
/// </summary>
public sealed class SkinnedModel
{
    /// <summary>Fraction of the triangles kept at each level of detail, and the error each may introduce
    /// (relative to the model's size).</summary>
    public static readonly (float Ratio, float Error)[] LodSettings = [(1f, 0f), (0.4f, 0.01f), (0.15f, 0.03f), (0.05f, 0.08f)];

    private static readonly Dictionary<(string Name, Skeleton? BindTo), SkinnedModel> Cache = [];

    /// <summary>The level of detail rays are tested against (see <see cref="SkinnedCollision"/>).</summary>
    public const int CollisionLod = 1;

    public Skeleton Skeleton { get; }
    public List<SkinnedPart> Parts { get; }

    /// <summary>The triangles bullets hit (one level of detail, all parts), posed on the CPU when needed.</summary>
    public SkinnedCollision Collision { get; }
    public IReadOnlyDictionary<string, AnimationClip> Clips { get; }

    /// <summary>Bind-pose bounds (model space).</summary>
    public Aabb Bounds { get; }

    public int LodCount => LodSettings.Length;

    public int Triangles(int lod) => Parts.Sum(p => p.Triangles(lod));

    /// <summary>True for a model loaded with <c>bindTo</c>: its joints index the body's skeleton, which it shares.</summary>
    public bool IsAttachment { get; }

    private SkinnedModel(Skeleton skeleton, List<SkinnedPart> parts, Dictionary<string, AnimationClip> clips, Aabb bounds, SkinnedCollision collision, bool isAttachment = false)
    {
        IsAttachment = isAttachment;
        Skeleton = skeleton;
        Parts = parts;
        Collision = collision;
        Clips = clips;
        Bounds = bounds;
    }

    /// <summary>
    /// <paramref name="name"/> may contain a subfolder ("garments/tshirt"). With <paramref name="bindTo"/> the model is an
    /// attachment: its joint indices are remapped by joint node name to <paramref name="bindTo"/>'s (a
    /// <see cref="FormatException"/> names a missing joint) and its inverse bind matrices are compared with the body's
    /// (a [WARN] if any differs by more than 1e-3). Cached per (name, bindTo).
    /// </summary>
    public static SkinnedModel Get(string name, Skeleton? bindTo = null)
    {
        if (Cache.TryGetValue((name, bindTo), out var model))
            return model;
        var parts = new List<string> { AppContext.BaseDirectory, "Assets", "Models" };
        parts.AddRange(name.Split('/', StringSplitOptions.RemoveEmptyEntries));
        parts[^1] += ".glb";
        model = Load(Path.Combine(parts.ToArray()), bindTo);
        Cache[(name, bindTo)] = model;
        return model;
    }

    public static SkinnedModel Load(string path) => Load(path, null);

    private static SkinnedModel Load(string path, Skeleton? bindTo)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var gltf = GltfUtility.LoadModel(path);
        var buffer = GltfUtility.LoadBinaryBuffer(gltf, 0, path);
        if (gltf.Skins is not { Length: > 0 })
            throw new FormatException($"{path} has no skin.");
        var skeleton = new Skeleton(gltf, buffer, gltf.Skins[0]);
        var jointMap = bindTo is null ? null : BuildJointMap(skeleton, bindTo, path);

        var parts = new List<SkinnedPart>();
        var collision = new SkinnedCollision.Builder();
        var min = new float3(float.MaxValue);
        var max = new float3(float.MinValue);
        foreach (var node in gltf.Nodes!)
        {
            if (node.Mesh is not { } meshIndex || node.Skin is null)
                continue;
            var mesh = gltf.Meshes[meshIndex];
            foreach (var primitive in mesh.Primitives)
            {
                var material = primitive.Material is { } m
                    ? Model.ToPartMaterial(gltf.Materials[m])
                    : new PartMaterial(Engine.Material.Plain, new float3(0.6f), false, 0.7f, 0f);
                var (vertices, indices, regions) = ReadPrimitive(gltf, buffer, primitive, ref min, ref max);
                if (jointMap is not null)
                    RemapJoints(vertices, jointMap);
                var lods = BuildLods(vertices, indices);
                collision.Add(vertices, lods[int.Min(CollisionLod, lods.Count - 1)]);
                var name = gltf.Materials is { } mats && primitive.Material is { } mi ? mats[mi].Name ?? mesh.Name : mesh.Name;
                parts.Add(new SkinnedPart(name ?? "part", material, vertices, lods, regions));
            }
        }

        if (parts.Count == 0)
            throw new FormatException($"{path} has no skinned meshes.");

        var clips = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
        foreach (var animation in bindTo is null ? (IEnumerable<Animation>)(gltf.Animations ?? []) : Array.Empty<Animation>())
        {
            var clip = new AnimationClip(gltf, buffer, animation);
            clips[clip.Name] = clip;
        }

        var model = new SkinnedModel(bindTo ?? skeleton, parts, clips, new Aabb(min, max), collision.Build(), bindTo is not null);
        if ((bindTo ?? skeleton).JointCount > Renderer.MaxBones)
            Log.Write($"[WARN] {Path.GetFileName(path)} has {(bindTo ?? skeleton).JointCount} joints but the shader takes {Renderer.MaxBones}: " +
                      "vertices weighted to the extra joints will be distorted. Raise Renderer.MaxBones (and uBones in the shader).");
        Log.Write($"Loaded skinned {(bindTo is null ? "model" : "attachment")} {Path.GetFileName(path)}: {skeleton.JointCount} joints, {clips.Count} clips " +
                  $"({string.Join(", ", clips.Values.Select(c => $"{c.Name} {c.Duration:0.00} s"))}), triangles per LOD " +
                  $"{string.Join(" / ", Enumerable.Range(0, model.LodCount).Select(model.Triangles))} " +
                  $"({System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:0} ms)");
        return model;
    }

    /// <summary>Source skin joint → <paramref name="body"/> joint with the same node name; warns if inverse binds differ.</summary>
    private static int[] BuildJointMap(Skeleton source, Skeleton body, string path)
    {
        var map = new int[source.JointCount];
        var worst = 0f;
        var worstName = "";
        // (animations of an attachment are skipped below: their node numbers belong to the garment's own file)
        for (var j = 0; j < map.Length; j++)
        {
            var name = source.NodeName(source.JointNode(j));
            var t = body.JointIndex(name);
            if (t < 0)
                throw new FormatException($"{path}: joint '{name}' does not exist in the body skeleton.");
            map[j] = t;
            var a = source.InverseBind(j);
            var b = body.InverseBind(t);
            var d = 0f;
            var fa = MemoryMarshal.Cast<float4x4, float>(MemoryMarshal.CreateReadOnlySpan(in a, 1));
            var fb = MemoryMarshal.Cast<float4x4, float>(MemoryMarshal.CreateReadOnlySpan(in b, 1));
            for (var e = 0; e < 16; e++)
                d = float.Max(d, float.Abs(fa[e] - fb[e]));

            if (d > worst)
            {
                worst = d;
                worstName = name;
            }
        }

        if (worst > 1e-3f)
            Log.Write($"[WARN] {Path.GetFileName(path)}: inverse bind matrices differ from the body's by up to {worst:0.0000} (joint '{worstName}'); the garment may not sit on the skin.");
        return map;
    }

    private static void RemapJoints(float[] vertices, int[] map)
    {
        for (var o = 0; o < vertices.Length; o += SkinnedPart.Stride)
        {
            for (var k = 0; k < 4; k++)
                vertices[o + 12 + k] = map[(int)vertices[o + 12 + k]];
        }
    }

    private static (float[] Vertices, uint[] Indices, byte[]? Regions) ReadPrimitive(Gltf gltf, byte[] buffer, MeshPrimitive primitive, ref float3 min, ref float3 max)
    {
        var a = primitive.Attributes;
        var positions = GltfData.Read(gltf, buffer, a["POSITION"], out _);
        var count = positions.Length / 3;
        var normals = a.TryGetValue("NORMAL", out var ni) ? GltfData.Read(gltf, buffer, ni, out _) : null;
        var uvs = a.TryGetValue("TEXCOORD_0", out var ti) ? GltfData.Read(gltf, buffer, ti, out _) : null;
        var joints = a.TryGetValue("JOINTS_0", out var ji) ? GltfData.Read(gltf, buffer, ji, out _) : null;
        var weights = a.TryGetValue("WEIGHTS_0", out var wi) ? GltfData.Read(gltf, buffer, wi, out _) : null;
        var index = primitive.Indices is { } ii ? GltfData.Read(gltf, buffer, ii, out _) : null;
        var indices = new uint[index?.Length ?? count];
        for (var i = 0; i < indices.Length; i++)
            indices[i] = index is null ? (uint)i : (uint)index[i];
        var tangents = a.TryGetValue("TANGENT", out var gi) ? GltfData.Read(gltf, buffer, gi, out _)
            : uvs is not null && normals is not null ? Model.ComputeTangents(positions, normals, uvs, Array.ConvertAll(indices, x => (int)x)) : null;

        byte[]? regions = null;
        if (a.TryGetValue("_REGION", out var ri))
        {
            var raw = GltfData.Read(gltf, buffer, ri, out var comps);
            regions = new byte[count];
            for (var i = 0; i < count; i++)
                regions[i] = (byte)int.Clamp((int)MathF.Round(raw[i * comps]), 0, 15);
        }

        var v = new float[count * SkinnedPart.Stride];
        for (var i = 0; i < count; i++)
        {
            var o = i * SkinnedPart.Stride;
            var p = new float3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
            min = float3.Min(min, p);
            max = float3.Max(max, p);
            v[o] = p.X; v[o + 1] = p.Y; v[o + 2] = p.Z;
            if (normals is not null)
            {
                v[o + 3] = normals[i * 3]; v[o + 4] = normals[i * 3 + 1]; v[o + 5] = normals[i * 3 + 2];
            }
            else
            {
                v[o + 4] = 1;
            }
            if (uvs is not null)
            {
                v[o + 6] = uvs[i * 2];
                v[o + 7] = 1f - uvs[i * 2 + 1]; // glTF's V runs down the image; ours runs up (as in Model)
            }
            if (tangents is not null)
            {
                v[o + 8] = tangents[i * 4]; v[o + 9] = tangents[i * 4 + 1]; v[o + 10] = tangents[i * 4 + 2]; v[o + 11] = tangents[i * 4 + 3];
            }
            else
            {
                v[o + 8] = 1; v[o + 11] = 1;
            }
            // Four influences, renormalised (the exporter's are already, but quantised weights may not quite sum to 1).
            var sum = 0f;
            for (var k = 0; k < 4; k++)
                sum += weights?[i * 4 + k] ?? 0;
            for (var k = 0; k < 4; k++)
            {
                v[o + 12 + k] = joints?[i * 4 + k] ?? 0;
                v[o + 16 + k] = sum > 0 ? weights![i * 4 + k] / sum : k == 0 ? 1 : 0;
            }
        }

        return (v, indices, regions);
    }

    /// <summary>The index lists for every level of detail: the original, then meshoptimizer simplifications of it.</summary>
    private static unsafe List<uint[]> BuildLods(float[] vertices, uint[] indices)
    {
        var lods = new List<uint[]>();
        var vertexCount = vertices.Length / SkinnedPart.Stride;
        var previous = indices;
        foreach (var (ratio, error) in LodSettings)
        {
            uint[] lod;
            if (ratio >= 1f)
            {
                lod = (uint[])indices.Clone();
            }
            else
            {
                var target = (long)(indices.Length * ratio) / 3 * 3;
                var dest = new uint[indices.Length];
                long written;
                float resultError;
                fixed (uint* d = dest, src = indices)
                fixed (float* pos = vertices)
                {
                    written = (int)MeshOpt.Simplify((int*)d, (int*)src, indices.Length, pos, vertexCount,
                        (Brutal.ByteSize64)(ulong)(SkinnedPart.Stride * sizeof(float)), target, error, 0, &resultError);
                }

                // Never let a level have more triangles than the one before it (the simplifier stops early on tiny parts).
                lod = written > 0 && written <= previous.Length ? dest[..(int)written] : previous;
            }

            var optimized = new uint[lod.Length];
            fixed (uint* d = optimized, src = lod)
                MeshOpt.OptimizeVertexCache((int*)d, (int*)src, lod.Length, vertexCount);
            lods.Add(optimized);
            previous = optimized;
        }

        return lods;
    }
}

/// <summary>Clips from several glTF files, retargeted by node name onto one skeleton, with the metadata of clips.json.</summary>
public sealed class ClipLibrary
{
    private readonly Dictionary<string, AnimationClip> _clips = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<AnimationClip> All => _clips.Values;

    private ClipLibrary()
    {
    }

    /// <summary>Case-insensitive; null if there is no such clip.</summary>
    public AnimationClip? Get(string name) => _clips.GetValueOrDefault(name);

    public IEnumerable<AnimationClip> WithTag(string tag) =>
        _clips.Values.Where(c => c.Info.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Loads Assets/Models/{file}.glb for each file in order (a later file overrides an earlier clip name, with a [WARN]),
    /// retargeting each channel's node by name to <paramref name="target"/> (channels on unknown nodes are dropped and
    /// counted in the log), then reads Assets/Models/anims/clips.json if present. Missing files are a [WARN].
    /// </summary>
    public static ClipLibrary Load(Skeleton target, params string[] files)
    {
        ArgumentNullException.ThrowIfNull(target);
        var lib = new ClipLibrary();
        lib.AddFiles(target, files);
        lib.Finish();
        return lib;
    }

    /// <summary>The model's own clips (no retarget needed) plus the files.</summary>
    public static ClipLibrary Load(SkinnedModel model, params string[] files)
    {
        ArgumentNullException.ThrowIfNull(model);
        var lib = new ClipLibrary();
        foreach (var clip in model.Clips.Values)
            lib._clips[clip.Name] = clip;
        lib.AddFiles(model.Skeleton, files);
        lib.Finish();
        return lib;
    }

    private int _dropped;

    private void AddFiles(Skeleton target, string[] files)
    {
        foreach (var file in files)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", Path.Combine(file.Split('/', StringSplitOptions.RemoveEmptyEntries)) + ".glb");
            if (!File.Exists(path))
            {
                Log.Write($"[WARN] Clip file {path} not found.");
                continue;
            }

            try
            {
                var gltf = GltfUtility.LoadModel(path);
                var buffer = GltfUtility.LoadBinaryBuffer(gltf, 0, path);
                var nodes = gltf.Nodes ?? [];
                var remap = new int[nodes.Length];
                for (var i = 0; i < remap.Length; i++)
                    remap[i] = nodes[i].Name is { } n ? target.NodeIndex(n) : -1;
                foreach (var animation in gltf.Animations ?? [])
                {
                    var clip = new AnimationClip(gltf, buffer, animation, remap);
                    if (_clips.ContainsKey(clip.Name))
                        Log.Write($"[WARN] Clip '{clip.Name}' from {Path.GetFileName(path)} replaces an earlier clip of the same name.");
                    _clips[clip.Name] = clip;
                    _dropped += clip.DroppedChannels;
                }
            }
            catch (Exception e)
            {
                Log.Write($"[WARN] Could not load clips from {path}: {e.Message}");
            }
        }
    }

    private void Finish()
    {
        var infoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "anims", "clips.json");
        var withInfo = 0;
        if (File.Exists(infoPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(infoPath));
                if (doc.RootElement.TryGetProperty("clips"u8, out var clips) && clips.ValueKind == JsonValueKind.Object)
                {
                    foreach (var entry in clips.EnumerateObject())
                    {
                        if (!_clips.TryGetValue(entry.Name, out var clip) || entry.Value.ValueKind != JsonValueKind.Object)
                            continue;
                        clip.Info = ParseInfo(clip.Name, entry.Value);
                        withInfo++;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Write($"[WARN] Could not read {infoPath}: {e.Message}");
            }
        }

        Log.Write($"Clip library: {_clips.Count} clips ({string.Join(", ", _clips.Values.Select(c => $"{c.Name} {c.Duration:0.00} s"))}), " +
                  $"{withInfo} with clips.json info, {_dropped} channels dropped");
    }

    private static ClipInfo ParseInfo(string name, JsonElement e)
    {
        var info = ClipInfo.Default(name);
        var loop = e.TryGetProperty("loop"u8, out var l) && (l.ValueKind == JsonValueKind.True || l.ValueKind == JsonValueKind.False) ? l.GetBoolean() : info.Loop;
        var stride = e.TryGetProperty("stride"u8, out var st) && st.ValueKind == JsonValueKind.Number ? st.GetSingle() : info.Stride;
        var tags = new List<string>();
        if (e.TryGetProperty("tags"u8, out var tg) && tg.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tg.EnumerateArray())
            {
                if (t.ValueKind == JsonValueKind.String && t.GetString() is { } ts)
                    tags.Add(ts);
            }
        }

        var events = new List<(string, float)>();
        if (e.TryGetProperty("events"u8, out var ev) && ev.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in ev.EnumerateArray())
            {
                if (x.ValueKind == JsonValueKind.Object && x.TryGetProperty("name"u8, out var n) && n.ValueKind == JsonValueKind.String
                    && x.TryGetProperty("time"u8, out var tm) && tm.ValueKind == JsonValueKind.Number)
                    events.Add((n.GetString()!, tm.GetSingle()));
            }
        }

        return new ClipInfo(name, loop, stride, [.. tags], [.. events]);
    }
}

/// <summary>Where a ray met a posed skinned mesh.</summary>
/// <param name="Distance">Along the ray to where it went in.</param>
/// <param name="ExitDistance">Along the ray to where it came out again (the next back-facing surface), or infinity.</param>
/// <param name="Normal">The surface's normal where it went in (world space, facing the ray).</param>
/// <param name="BindPosition">The same point in the mesh's bind pose (model space before skinning): it stays on the
/// skin however the model moves.</param>
/// <param name="Joint">The skin joint that moves that point the most.</param>
public readonly record struct SkinHit(float Distance, float ExitDistance, float3 Normal, float3 BindPosition, int Joint);

/// <summary>
/// The triangles of one level of detail of a skinned model, with each vertex's bind position and joints, for testing
/// rays (bullets) against the model as it is posed. <see cref="Pose"/> skins the vertices on the CPU into a buffer the
/// caller keeps (one per character, refreshed only when a ray comes near it).
/// </summary>
public sealed class SkinnedCollision
{
    private readonly float3[] _bind;
    private readonly int4[] _joints;
    private readonly float4[] _weights;
    private readonly int[] _triangles;

    public int VertexCount => _bind.Length;
    public int TriangleCount => _triangles.Length / 3;

    private SkinnedCollision(float3[] bind, int4[] joints, float4[] weights, int[] triangles)
    {
        _bind = bind;
        _joints = joints;
        _weights = weights;
        _triangles = triangles;
    }

    public sealed class Builder
    {
        private readonly List<float3> _bind = [];
        private readonly List<int4> _joints = [];
        private readonly List<float4> _weights = [];
        private readonly List<int> _triangles = [];

        /// <summary>Adds the vertices one index list uses (renumbered compactly) and its triangles.</summary>
        public void Add(float[] vertices, uint[] indices)
        {
            var map = new Dictionary<uint, int>();
            foreach (var i in indices)
            {
                if (!map.TryGetValue(i, out var j))
                {
                    j = _bind.Count;
                    map[i] = j;
                    var o = (int)i * SkinnedPart.Stride;
                    _bind.Add(new float3(vertices[o], vertices[o + 1], vertices[o + 2]));
                    _joints.Add(new int4((int)vertices[o + 12], (int)vertices[o + 13], (int)vertices[o + 14], (int)vertices[o + 15]));
                    _weights.Add(new float4(vertices[o + 16], vertices[o + 17], vertices[o + 18], vertices[o + 19]));
                }

                _triangles.Add(j);
            }
        }

        public SkinnedCollision Build() => new([.. _bind], [.. _joints], [.. _weights], [.. _triangles]);
    }

    /// <summary>World positions of the vertices for skinning matrices <paramref name="skin"/> and placement <paramref name="world"/>.</summary>
    public void Pose(ReadOnlySpan<float4x4> skin, in float4x4 world, float3[] posed)
    {
        for (var i = 0; i < _bind.Length; i++)
        {
            var j = _joints[i];
            var w = _weights[i];
            var b = _bind[i];
            var p = float3.Transform(b, skin[j.X]) * w.X + float3.Transform(b, skin[j.Y]) * w.Y
                  + float3.Transform(b, skin[j.Z]) * w.Z + float3.Transform(b, skin[j.W]) * w.W;
            posed[i] = float3.Transform(p, world);
        }
    }

    /// <summary>
    /// The first front-facing triangle the ray (unit <paramref name="direction"/>) meets within
    /// <paramref name="maxDistance"/>, and where it next comes out through a back-facing one.
    /// </summary>
    public bool Raycast(float3[] posed, float3 origin, float3 direction, float maxDistance, out SkinHit hit)
    {
        hit = default;
        var best = maxDistance;
        var bestTri = -1;
        float bu = 0, bv = 0;
        var tris = _triangles;
        // Exits are only known after the entry, so remember every back face crossed.
        Span<float> exits = stackalloc float[32];
        var exitCount = 0;
        for (var k = 0; k < tris.Length; k += 3)
        {
            var a = posed[tris[k]];
            var e1 = posed[tris[k + 1]] - a;
            var e2 = posed[tris[k + 2]] - a;
            var pv = float3.Cross(direction, e2);
            var det = float3.Dot(e1, pv);
            if (float.Abs(det) < 1e-12f)
                continue;
            var inv = 1f / det;
            var tv = origin - a;
            var u = float3.Dot(tv, pv) * inv;
            if (u < 0 || u > 1)
                continue;
            var qv = float3.Cross(tv, e1);
            var v = float3.Dot(direction, qv) * inv;
            if (v < 0 || u + v > 1)
                continue;
            var t = float3.Dot(e2, qv) * inv;
            if (t <= 0)
                continue;
            if (det > 0)       // counter-clockwise from the ray's side: facing it (front face)
            {
                if (t < best)
                {
                    best = t;
                    bestTri = k;
                    bu = u;
                    bv = v;
                }
            }
            else if (exitCount < exits.Length)
            {
                exits[exitCount++] = t;
            }
        }

        if (bestTri < 0)
            return false;
        var exit = float.PositiveInfinity;
        for (var i = 0; i < exitCount; i++)
        {
            if (exits[i] > best && exits[i] < exit)
                exit = exits[i];
        }

        int i0 = tris[bestTri], i1 = tris[bestTri + 1], i2 = tris[bestTri + 2];
        var normal = float3.Cross(posed[i1] - posed[i0], posed[i2] - posed[i0]);
        var len = float.Sqrt(float3.Dot(normal, normal));
        normal = len > 1e-12f ? normal / len : -direction;
        if (float3.Dot(normal, direction) > 0)
            normal = -normal;
        var w0 = 1 - bu - bv;
        var bind = _bind[i0] * w0 + _bind[i1] * bu + _bind[i2] * bv;
        var nearest = w0 >= bu && w0 >= bv ? i0 : bu >= bv ? i1 : i2;
        var wts = _weights[nearest];
        var js = _joints[nearest];
        var joint = js.X;
        var top = wts.X;
        if (wts.Y > top) { top = wts.Y; joint = js.Y; }
        if (wts.Z > top) { top = wts.Z; joint = js.Z; }
        if (wts.W > top) joint = js.W;
        hit = new SkinHit(best, exit, normal, bind, joint);
        return true;
    }
}

/// <summary>Reads glTF accessors of any component type into floats (normalised integers mapped to 0..1 / -1..1).</summary>
internal static class GltfData
{
    public static float[] Read(Gltf gltf, byte[] buffer, int accessorIndex, out int components)
    {
        var accessor = gltf.Accessors[accessorIndex];
        components = accessor.Type switch
        {
            Accessor.TypeEnum.Scalar => 1,
            Accessor.TypeEnum.Vec2 => 2,
            Accessor.TypeEnum.Vec3 => 3,
            Accessor.TypeEnum.Vec4 => 4,
            Accessor.TypeEnum.Mat2 => 4,
            Accessor.TypeEnum.Mat3 => 9,
            _ => 16,
        };
        var size = accessor.ComponentType switch
        {
            Accessor.ComponentTypeEnum.Byte or Accessor.ComponentTypeEnum.UnsignedByte => 1,
            Accessor.ComponentTypeEnum.Short or Accessor.ComponentTypeEnum.UnsignedShort => 2,
            _ => 4,
        };
        var view = gltf.BufferViews[accessor.BufferView ?? throw new FormatException("Sparse accessors aren't supported.")];
        var stride = view.ByteStride ?? components * size;
        var start = view.ByteOffset + accessor.ByteOffset;
        var normalized = accessor.Normalized;
        var result = new float[accessor.Count * components];
        var span = buffer.AsSpan();
        for (var i = 0; i < accessor.Count; i++)
        {
            var at = start + i * stride;
            for (var c = 0; c < components; c++)
            {
                var b = span.Slice(at + c * size, size);
                result[i * components + c] = accessor.ComponentType switch
                {
                    Accessor.ComponentTypeEnum.Float => MemoryMarshal.Read<float>(b),
                    Accessor.ComponentTypeEnum.UnsignedByte => normalized ? b[0] / 255f : b[0],
                    Accessor.ComponentTypeEnum.Byte => normalized ? float.Max((sbyte)b[0] / 127f, -1f) : (sbyte)b[0],
                    Accessor.ComponentTypeEnum.UnsignedShort => normalized ? MemoryMarshal.Read<ushort>(b) / 65535f : MemoryMarshal.Read<ushort>(b),
                    Accessor.ComponentTypeEnum.Short => normalized ? float.Max(MemoryMarshal.Read<short>(b) / 32767f, -1f) : MemoryMarshal.Read<short>(b),
                    _ => MemoryMarshal.Read<uint>(b),
                };
            }
        }

        return result;
    }
}
