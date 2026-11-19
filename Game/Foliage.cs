using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Leaves you can hide in but not take cover behind: the bushes, ferns, palms and saplings as soft ellipsoids, each
/// with a density (how much of the view one metre of it blocks). Bullets go straight through (foliage has no
/// collision: <see cref="Level"/> leaves <see cref="Material.Bush"/> out of the physics); people's eyes don't.
/// <para>
/// <see cref="Depth"/> is the "optical depth" along a line of sight: the sum over the volumes it passes through of
/// density x the length inside. 0 = nothing in the way; about 1 = a metre of thick shrub, and you can't make out
/// anyone behind it. A grid of 2 m cells on the ground plane finds the volumes near a line.
/// </para>
/// </summary>
public sealed class FoliageCover
{
    private const float Cell = 2f;

    private readonly List<(float3 Center, float3 InvRadii, float Density)> _volumes = [];
    private readonly Dictionary<long, List<int>> _grid = [];
    private int[] _stamp = [];
    private int _query;

    public int Count => _volumes.Count;

    // A model's leaves (the Leaves / Bush parts) as clumps: the leaf area in each cell of a grid in model space.
    private const float Voxel = 0.5f;               // m, model space
    private const float LeafExtinction = 0.5f;      // optical depth per m of path per m² of leaf per m³ (randomly turned leaves)
    private readonly Dictionary<Engine.Model, (float3 Center, float Area)[]> _clumps = [];

    /// <summary>
    /// The leaves of an instance of <paramref name="model"/> (placed by <paramref name="transform"/>, scaled by
    /// <paramref name="scale"/>): a soft ball for every cell of its leaf grid, as dense as the leaf area in it. Bare stems
    /// and trunks hide nothing; a crown of leaves above head height hides nothing at eye level.
    /// </summary>
    public void AddModel(Engine.Model model, float4x4 transform, float scale)
    {
        if (!_clumps.TryGetValue(model, out var clumps))
            _clumps[model] = clumps = LeafClumps(model);
        var radius = 0.62f * Voxel * scale;   // a ball as big as the cell
        var perArea = LeafExtinction / (Voxel * Voxel * Voxel * scale);   // area x s², volume x s³
        foreach (var (center, area) in clumps)
            Add(float3.Transform(center, transform), new float3(radius), area * perArea);
    }

    private static (float3 Center, float Area)[] LeafClumps(Engine.Model model)
    {
        const int s = Engine.Mesh.FullStride;
        var cells = new Dictionary<(int, int, int), float>();
        foreach (var part in model.Parts)
        {
            if (part.Material.Material is not (Material.Leaves or Material.Bush))
                continue;
            var v = part.Vertices;
            for (var i = 0; i + 3 * s <= v.Length; i += 3 * s)
            {
                var a = new float3(v[i], v[i + 1], v[i + 2]);
                var b = new float3(v[i + s], v[i + s + 1], v[i + s + 2]);
                var c = new float3(v[i + 2 * s], v[i + 2 * s + 1], v[i + 2 * s + 2]);
                var cross = float3.Cross(b - a, c - a);
                var area = 0.5f * float.Sqrt(float3.Dot(cross, cross));
                if (area < 1e-7f)
                    continue;
                // Big triangles (a banana leaf) are cut into n x n smaller ones, each counted in its own cell.
                var edge = float.Max(Len(b - a), float.Max(Len(c - b), Len(a - c)));
                var n = int.Clamp((int)float.Ceiling(edge / (Voxel * 0.5f)), 1, 24);
                var share = area / (n * n);
                for (var p = 0; p < n; p++)
                for (var q = 0; q < n - p; q++)
                {
                    Put((p + 1f / 3f) / n, (q + 1f / 3f) / n);
                    if (p + q < n - 1)
                        Put((p + 2f / 3f) / n, (q + 2f / 3f) / n);
                }

                void Put(float u, float w)
                {
                    var at = a + (b - a) * u + (c - a) * w;
                    var key = (CellOf(at.X, Voxel), CellOf(at.Y, Voxel), CellOf(at.Z, Voxel));
                    cells[key] = cells.GetValueOrDefault(key) + share;
                }
            }
        }

        return cells.Where(kv => kv.Value > 0.01f)
            .Select(kv => (new float3(kv.Key.Item1 + 0.5f, kv.Key.Item2 + 0.5f, kv.Key.Item3 + 0.5f) * Voxel, kv.Value))
            .ToArray();

        static float Len(float3 d) => float.Sqrt(float3.Dot(d, d));
    }

    /// <summary>A clump of leaves: an ellipsoid at <paramref name="center"/> with these radii (x, y, z).</summary>
    public void Add(float3 center, float3 radii, float density)
    {
        if (density <= 0 || radii.X <= 0.05f || radii.Y <= 0.05f || radii.Z <= 0.05f)
            return;
        var index = _volumes.Count;
        _volumes.Add((center, new float3(1f / radii.X, 1f / radii.Y, 1f / radii.Z), density));
        int x0 = CellOf(center.X - radii.X), x1 = CellOf(center.X + radii.X);
        int z0 = CellOf(center.Z - radii.Z), z1 = CellOf(center.Z + radii.Z);
        for (var x = x0; x <= x1; x++)
        for (var z = z0; z <= z1; z++)
        {
            if (!_grid.TryGetValue(Key(x, z), out var list))
                _grid[Key(x, z)] = list = [];
            list.Add(index);
        }
    }

    /// <summary>How much foliage is in the way from <paramref name="from"/> to <paramref name="to"/> (density x metres).</summary>
    public float Depth(float3 from, float3 to)
    {
        if (_volumes.Count == 0)
            return 0f;
        if (_stamp.Length < _volumes.Count)
            _stamp = new int[_volumes.Count];
        _query++;
        var d = to - from;
        var length = float.Sqrt(float3.Dot(d, d));
        if (length < 1e-4f)
            return 0f;
        var depth = 0f;

        // Walk the grid cells under the line (2D DDA on x, z).
        int cx = CellOf(from.X), cz = CellOf(from.Z);
        int ex = CellOf(to.X), ez = CellOf(to.Z);
        int sx = d.X > 0 ? 1 : -1, sz = d.Z > 0 ? 1 : -1;
        var tdx = float.Abs(d.X) > 1e-6f ? Cell / float.Abs(d.X) : float.PositiveInfinity;
        var tdz = float.Abs(d.Z) > 1e-6f ? Cell / float.Abs(d.Z) : float.PositiveInfinity;
        var tx = float.Abs(d.X) > 1e-6f ? ((sx > 0 ? (cx + 1) * Cell : cx * Cell) - from.X) / d.X : float.PositiveInfinity;
        var tz = float.Abs(d.Z) > 1e-6f ? ((sz > 0 ? (cz + 1) * Cell : cz * Cell) - from.Z) / d.Z : float.PositiveInfinity;
        for (var guard = 0; guard < 512; guard++)
        {
            if (_grid.TryGetValue(Key(cx, cz), out var list))
                foreach (var i in list)
                {
                    if (_stamp[i] == _query)
                        continue;
                    _stamp[i] = _query;
                    depth += Chord(_volumes[i], from, d) * length * _volumes[i].Density;
                }

            if (cx == ex && cz == ez)
                break;
            if (tx < tz)
            {
                if (tx > 1f)
                    break;
                cx += sx;
                tx += tdx;
            }
            else
            {
                if (tz > 1f)
                    break;
                cz += sz;
                tz += tdz;
            }
        }

        return depth;
    }

    /// <summary>The share (0..1) of the segment from + t d, t in [0, 1], that lies inside the ellipsoid.</summary>
    private static float Chord((float3 Center, float3 InvRadii, float Density) v, float3 from, float3 d)
    {
        var a = (from - v.Center) * v.InvRadii;
        var b = d * v.InvRadii;
        var bb = float3.Dot(b, b);
        var ab = float3.Dot(a, b);
        var c = float3.Dot(a, a) - 1f;
        var disc = ab * ab - bb * c;
        if (disc <= 0 || bb < 1e-12f)
            return 0f;
        var root = float.Sqrt(disc);
        var t0 = float.Max((-ab - root) / bb, 0f);
        var t1 = float.Min((-ab + root) / bb, 1f);
        return float.Max(t1 - t0, 0f);
    }

    private static int CellOf(float v, float cell = Cell) => (int)float.Floor(v / cell);

    private static long Key(int x, int z) => ((long)x << 32) | (uint)z;
}
