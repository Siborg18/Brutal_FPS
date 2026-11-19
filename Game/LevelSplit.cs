using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The levels' code-built furniture is merged into one mesh per material and colour (a whole room's steel in one
/// draw), so in the level editor a lamp's box used to go round every lamp in the level. Once a level is built, each
/// such mesh is split here into the separate things in it, as <see cref="Prop.Piece"/>s the editor picks one at a time:
/// <list type="number">
/// <item>its triangles that touch (or nearly) are one thing;</item>
/// <item>a thing in another material that lies inside a small thing (a handle in a cabinet, a screen in its bezel) is
/// part of it;</item>
/// <item>each is a piece, still drawn as part of the merged mesh until the editor moves it (<see cref="DrawBatch"/>):
/// no more draws than before.</item>
/// </list>
/// Not split: foliage and trees (their trunks and leaves are separate meshes), ground decals, meshes placed by a
/// transform (shared ones), very big ones (over 60,000 triangles or 300 m), and what's already in pieces (Level.Piece). BRUTALFPS_PROFILE=nosplit
/// leaves them all merged.
/// </summary>
public sealed partial class Level
{
    private const int SplitMaxTriangles = 60000;
    private const float SplitMaxSize = 300f;   // (scenery: the harbour's distant container stacks, a kilometre of them in a mesh)
    private const float SplitTouch = 0.01f, SplitCell = 0.5f, ObjectMax = 4f, ObjectCell = 2f;

    /// <summary>A thing found in a merged mesh: which mesh, its triangles, the box round it.</summary>
    private sealed class SplitCluster(int prop, List<int> triangles, Aabb bounds)
    {
        public int Prop { get; } = prop;
        public List<int> Triangles { get; } = triangles;
        public Aabb Bounds { get; } = bounds;
        public int Parent;   // (union-find, into the list of all clusters: the object it's part of)
    }

    private static bool Splittable(in Prop prop) =>
        prop is { Asset: null, Mesh: not null, Part: null, Decal: false, Arrives: false, BatchMesh: false, InBatch: 0, Piece: 0, Removed: false }
        && prop.Material is not (Material.Bush or Material.Bark or Material.Grass or Material.Sky or Material.Ocean)
        && prop.Model == float4x4.Identity
        && prop.Mesh.SimpleVertices is { } v && v.Length / 18 is > 1 and <= SplitMaxTriangles
        && float.Max(prop.Mesh.Bounds.Size.X, float.Max(prop.Mesh.Bounds.Size.Y, prop.Mesh.Bounds.Size.Z)) is > 0.5f and < SplitMaxSize;

    private void SplitMergedMeshes()
    {
        if (Profile.Has("nosplit"))
            return;
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var clusters = new List<SplitCluster>();
        var perProp = new Dictionary<int, List<int>>();
        for (var i = 0; i < _props.Count; i++)
        {
            if (!Splittable(_props[i]))
                continue;
            var found = ClustersOf(i, _props[i].Mesh!.SimpleVertices!);
            perProp[i] = [];
            foreach (var c in found)
            {
                c.Parent = clusters.Count;
                perProp[i].Add(clusters.Count);
                clusters.Add(c);
            }
        }

        // Things inside small things are part of them (whatever they're made of): look round each small one.
        int Find(int c)
        {
            while (clusters[c].Parent != c)
                c = clusters[c].Parent = clusters[clusters[c].Parent].Parent;
            return c;
        }
        var grid = new Dictionary<(int, int, int), List<int>>();
        static (int, int, int) Cell(float3 p) => ((int)float.Floor(p.X / ObjectCell), (int)float.Floor(p.Y / ObjectCell), (int)float.Floor(p.Z / ObjectCell));
        for (var c = 0; c < clusters.Count; c++)
        {
            var key = Cell(clusters[c].Bounds.Center);
            if (!grid.TryGetValue(key, out var list))
                grid[key] = list = [];
            list.Add(c);
        }
        for (var c = 0; c < clusters.Count; c++)
        {
            var outer = clusters[c].Bounds;
            if (float.Max(outer.Size.X, float.Max(outer.Size.Y, outer.Size.Z)) > ObjectMax)
                continue;
            var around = outer.Expand(0.02f);
            var (lo, hi) = (Cell(around.Min), Cell(around.Max));
            for (var x = lo.Item1; x <= hi.Item1; x++)
                for (var y = lo.Item2; y <= hi.Item2; y++)
                    for (var z = lo.Item3; z <= hi.Item3; z++)
                    {
                        if (!grid.TryGetValue((x, y, z), out var list))
                            continue;
                        foreach (var d in list)
                        {
                            if (d == c)
                                continue;
                            var inner = clusters[d].Bounds;
                            var inside = inner.Min.X >= around.Min.X && inner.Min.Y >= around.Min.Y && inner.Min.Z >= around.Min.Z
                                         && inner.Max.X <= around.Max.X && inner.Max.Y <= around.Max.Y && inner.Max.Z <= around.Max.Z;
                            if (inside)
                                clusters[Find(d)].Parent = Find(c);
                        }
                    }
        }

        // An object of more than one cluster, or a mesh with more than one thing in it, is split; the rest stay as built.
        var objectSize = new Dictionary<int, int>();
        var biggest = new Dictionary<int, int>();   // object root -> its cluster with the most triangles
        for (var c = 0; c < clusters.Count; c++)
        {
            var root = Find(c);
            objectSize[root] = objectSize.GetValueOrDefault(root) + 1;
            if (!biggest.TryGetValue(root, out var b) || clusters[c].Triangles.Count > clusters[b].Triangles.Count)
                biggest[root] = c;
        }
        var pieceOf = new Dictionary<int, int>();   // object root -> piece number
        var (split, pieces) = (0, 0);
        foreach (var (index, mine) in perProp)
        {
            if (mine.Count < 2 && objectSize[Find(mine[0])] < 2)
                continue;
            var source = _props[index];
            var vertices = source.Mesh!.SimpleVertices!;
            var batch = _drawBatches.Count + 1;
            var made = new List<(int, float[])>();
            foreach (var group in mine.GroupBy(Find))
            {
                if (!pieceOf.TryGetValue(group.Key, out var piece))
                {
                    pieceOf[group.Key] = piece = ++_pieceCount;
                    // (Named for what most of it is made of.)
                    _pieceNames[piece] = $"{_props[clusters[biggest[group.Key]].Prop].Material} part".ToLowerInvariant();
                    pieces++;
                }
                var data = new float[group.Sum(c => clusters[c].Triangles.Count) * 18];
                var at = 0;
                foreach (var c in group)
                    foreach (var t in clusters[c].Triangles)
                    {
                        Array.Copy(vertices, t * 18, data, at, 18);
                        at += 18;
                    }
                _props.Add(source with { Mesh = new Mesh(data, upload: false), Piece = piece, InBatch = batch });
                made.Add((_props.Count - 1, data));
            }
            // The mesh as built draws them all until one's moved (it's not the editor's any more).
            _props[index] = source with { BatchMesh = true };
            _drawBatches.Add(new DrawBatch(index, made));
            var occluder = _policeOccluders.IndexOf(index);
            if (occluder >= 0)
            {
                _policeOccluders.RemoveAt(occluder);
                _policeOccluders.AddRange(made.Select(m => m.Item1));
            }
            split++;
        }
        _version++;
        var attached = GroupAttachments();
        Log.Write($"[LEVEL] Split {split} merged meshes into {pieces} things the level editor picks one at a time, and put " +
                  $"{attached} models with what they're fixed to ({System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:0} ms)");
    }

    /// <summary>
    /// Models placed by the very same transform are one thing (a van and its number plates, which are a model of
    /// their own): one piece, named after the biggest. Returns how many were put with another.
    /// </summary>
    private int GroupAttachments()
    {
        var count = 0;
        foreach (var group in Enumerable.Range(0, _props.Count)
                     .Where(i => _props[i] is { Asset: { } a, Piece: 0, Part: null, BatchMesh: false } && a.Name.Length > 0 && _props[i].Model != float4x4.Identity)
                     .GroupBy(i => _props[i].Model)
                     .Where(g => g.Count() > 1))
        {
            var main = group.MaxBy(i => _props[i].Asset!.Bounds.Size.X * _props[i].Asset!.Bounds.Size.Y * _props[i].Asset!.Bounds.Size.Z);
            var piece = ++_pieceCount;
            _pieceNames[piece] = _props[main].Asset!.Name.Replace('_', ' ');
            foreach (var i in group)
                _props[i] = _props[i] with { Piece = piece };
            count += group.Count() - 1;
        }
        return count;
    }

    /// <summary>The separate things in a merged mesh: its triangles, grouped where they touch (or nearly).</summary>
    private static List<SplitCluster> ClustersOf(int prop, float[] v)
    {
        var count = v.Length / 18;
        var parent = new int[count];
        for (var i = 0; i < count; i++)
            parent[i] = i;
        int Find(int i)
        {
            while (parent[i] != i)
                i = parent[i] = parent[parent[i]];
            return i;
        }
        void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b)
                parent[a] = b;
        }
        var lo = new float3[count];
        var hi = new float3[count];
        for (var t = 0; t < count; t++)
        {
            var o = t * 18;
            var a = new float3(v[o], v[o + 1], v[o + 2]);
            var b = new float3(v[o + 6], v[o + 7], v[o + 8]);
            var c = new float3(v[o + 12], v[o + 13], v[o + 14]);
            lo[t] = float3.Min(a, float3.Min(b, c)) - new float3(SplitTouch);
            hi[t] = float3.Max(a, float3.Max(b, c)) + new float3(SplitTouch);
        }

        // First the triangles that share a corner (a box's faces), cheaply, by their corners' positions.
        var corners = new Dictionary<(int, int, int), int>(count * 2);
        for (var t = 0; t < count; t++)
            for (var k = 0; k < 3; k++)
            {
                var o = t * 18 + k * 6;
                var key = ((int)float.Round(v[o] * 1000), (int)float.Round(v[o + 1] * 1000), (int)float.Round(v[o + 2] * 1000));
                if (corners.TryGetValue(key, out var other))
                    Union(t, other);
                else
                    corners[key] = t;
            }

        // Then what's left that touches: the boxes round those groups, in half-metre cells (the big ones checked against all).
        var groups = new Dictionary<int, (float3 Lo, float3 Hi)>();
        for (var t = 0; t < count; t++)
        {
            var r = Find(t);
            groups[r] = groups.TryGetValue(r, out var g) ? (float3.Min(g.Lo, lo[t]), float3.Max(g.Hi, hi[t])) : (lo[t], hi[t]);
        }
        var cells = new Dictionary<(int, int, int), List<int>>();
        var big = new List<int>();
        var roots = groups.Keys.ToList();
        static int C(float x) => (int)float.Floor(x / SplitCell);
        foreach (var r in roots)
        {
            var (l, h) = groups[r];
            long n = (long)(C(h.X) - C(l.X) + 1) * (C(h.Y) - C(l.Y) + 1) * (C(h.Z) - C(l.Z) + 1);
            if (n > 64)
            {
                big.Add(r);
                continue;
            }
            for (var x = C(l.X); x <= C(h.X); x++)
                for (var y = C(l.Y); y <= C(h.Y); y++)
                    for (var z = C(l.Z); z <= C(h.Z); z++)
                    {
                        if (!cells.TryGetValue((x, y, z), out var list))
                            cells[(x, y, z)] = list = [];
                        list.Add(r);
                    }
        }
        static bool Overlap((float3 Lo, float3 Hi) a, (float3 Lo, float3 Hi) b) =>
            a.Lo.X <= b.Hi.X && b.Lo.X <= a.Hi.X && a.Lo.Y <= b.Hi.Y && b.Lo.Y <= a.Hi.Y && a.Lo.Z <= b.Hi.Z && b.Lo.Z <= a.Hi.Z;
        foreach (var list in cells.Values)
            for (var i = 0; i < list.Count; i++)
                for (var j = i + 1; j < list.Count; j++)
                    if (Find(list[i]) != Find(list[j]) && Overlap(groups[list[i]], groups[list[j]]))
                        Union(list[i], list[j]);
        foreach (var b in big)
            foreach (var r in roots)
                if (r != b && Find(r) != Find(b) && Overlap(groups[b], groups[r]))
                    Union(b, r);

        var result = new Dictionary<int, List<int>>();
        for (var t = 0; t < count; t++)
        {
            var r = Find(t);
            if (!result.TryGetValue(r, out var list))
                result[r] = list = [];
            list.Add(t);
        }
        return [.. result.Values.Select(tris =>
        {
            var l = new float3(float.MaxValue);
            var h = new float3(float.MinValue);
            foreach (var t in tris)
            {
                l = float3.Min(l, lo[t] + new float3(SplitTouch));
                h = float3.Max(h, hi[t] - new float3(SplitTouch));
            }
            return new SplitCluster(prop, tris, new Aabb(l, h));
        })];
    }
}
