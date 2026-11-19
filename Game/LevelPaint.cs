using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Walls built as two boxes where one painted box will do (run once the level is built, before the level editor
/// numbers what's in it, so the pieces a wall is cut into here still move as one):
/// <list type="bullet">
/// <item>A thin coat laid over a wall's face (a band of paint, a kick band, a whitewashed panel): the wall is cut at the
/// coat's edges and the piece behind it has that face painted the coat's colour and surface (<see cref="LevelBox.Paint"/>,
/// <see cref="LevelBox.PaintMaterial"/>); the coat goes. A coat a few millimetres off a face z-fights with it from afar.</item>
/// <item>A wall of two half-thickness boxes side by side, each painted for its own side (and a container's end and its
/// lining): one box, its face on the second's side painted the second's colour.</item>
/// </list>
/// Only on walls (faces facing along x or z): coats on floors are paths and rugs, and stay. BRUTALFPS_PROFILE=nopaint
/// leaves them all as built, to compare.
/// </summary>
public sealed partial class Level
{
    private const float CoatMax = 0.035f, HalfMax = 0.35f, Flush = 0.003f;

    private static float Along(float3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    private static float3 With(float3 v, int axis, float value) => axis switch
    {
        0 => new float3(value, v.Y, v.Z),
        1 => new float3(v.X, value, v.Z),
        _ => new float3(v.X, v.Y, value),
    };

    /// <summary>The face bit of <paramref name="axis"/>'s face on the + side (or the - side).</summary>
    private static int FaceBit(int axis, bool plus) => 1 << (axis * 2 + (plus ? 0 : 1));

    /// <summary>True when <paramref name="inner"/>'s extent on the two axes other than <paramref name="axis"/> is inside <paramref name="outer"/>'s.</summary>
    private static bool FaceInside(in Aabb inner, in Aabb outer, int axis)
    {
        for (var o = 0; o < 3; o++)
        {
            if (o == axis)
                continue;
            if (Along(inner.Min, o) < Along(outer.Min, o) - Flush || Along(inner.Max, o) > Along(outer.Max, o) + Flush)
                return false;
        }
        return true;
    }

    private static LevelBox Retired(in LevelBox box) => box with { Visible = false, Collides = false, BlocksShots = false, Id = 0 };

    // Which boxes are near where (8 m cells across x and z; the few spanning more than 64 cells are always looked at).
    private const float PaintCell = 8f;
    private readonly Dictionary<(int, int), List<int>> _paintGrid = [];
    private readonly List<int> _paintBig = [];
    private readonly List<int> _paintNear = [];

    private static (int X0, int Z0, int X1, int Z1) Cells(in Aabb b) =>
        ((int)float.Floor((b.Min.X - Flush) / PaintCell), (int)float.Floor((b.Min.Z - Flush) / PaintCell),
         (int)float.Floor((b.Max.X + Flush) / PaintCell), (int)float.Floor((b.Max.Z + Flush) / PaintCell));

    private void AddToPaintGrid(int index, in Aabb bounds)
    {
        var (x0, z0, x1, z1) = Cells(bounds);
        if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > 64)
        {
            _paintBig.Add(index);
            return;
        }
        for (var x = x0; x <= x1; x++)
            for (var z = z0; z <= z1; z++)
            {
                if (!_paintGrid.TryGetValue((x, z), out var list))
                    _paintGrid[(x, z)] = list = [];
                if (list.Count == 0 || list[^1] != index)
                    list.Add(index);
            }
    }

    /// <summary>The boxes that might touch <paramref name="bounds"/> (each once).</summary>
    private List<int> PaintNear(in Aabb bounds)
    {
        _paintNear.Clear();
        _paintNear.AddRange(_paintBig);
        var (x0, z0, x1, z1) = Cells(bounds);
        for (var x = x0; x <= x1; x++)
            for (var z = z0; z <= z1; z++)
                if (_paintGrid.TryGetValue((x, z), out var list))
                    _paintNear.AddRange(list);
        return _paintNear;
    }

    private void PaintDoubleWalls()
    {
        if (Profile.Has("nopaint"))
            return;
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var i = 0; i < _boxes.Count; i++)
            if (_boxes[i].Visible && _boxes[i].Id != 0)
                AddToPaintGrid(i, _boxes[i].Bounds);
        var (coats, halves, cuts) = (0, 0, 0);
        // (Boxes are only ever replaced, retired or added at the end here, so the indices hold.)
        for (var i = 0; i < _boxes.Count; i++)
        {
            var thin = _boxes[i];
            if (!thin.Visible || thin.Id == 0)
                continue;
            foreach (var axis in (ReadOnlySpan<int>)[0, 2])
            {
                var t = Along(thin.Bounds.Size, axis);
                if (t > HalfMax)
                    continue;
                if (FindHost(i, axis) is not ({ } host, var plus))
                    continue;
                var wall = _boxes[host];
                var face = FaceBit(axis, plus);   // (the host's face the thin one lies on)
                var h = Along(wall.Bounds.Size, axis);
                var area = 1f;
                for (var o = 0; o < 3; o++)
                    if (o != axis)
                        area *= Along(thin.Bounds.Size, o);
                if (t <= CoatMax && h > t * 2 && area >= 0.9f)
                {
                    cuts += PaintCoat(host, face, axis, thin) - 1;
                    _boxes[i] = Retired(thin);
                    coats++;
                    break;
                }
                if (t > CoatMax && float.Abs(h - t) < 0.01f && thin.Material == wall.Material && thin.PaintFaces == 0
                    && thin.Color != wall.Color)
                {
                    // Two halves: the host grows through the thin one, and its face on that side takes the thin one's paint.
                    var lo = float3.Min(wall.Bounds.Min, thin.Bounds.Min);
                    var hi = float3.Max(wall.Bounds.Max, thin.Bounds.Max);
                    var grown = new Aabb(With(wall.Bounds.Min, axis, Along(lo, axis)), With(wall.Bounds.Max, axis, Along(hi, axis)));
                    _boxes[host] = wall with { Bounds = grown, Paint = thin.Color, PaintFaces = wall.PaintFaces | face, PaintMaterial = null };
                    AddToPaintGrid(host, grown);
                    _boxes[i] = Retired(thin);
                    halves++;
                    break;
                }
            }
        }
        _version++;
        _paintGrid.Clear();
        _paintBig.Clear();
        if (coats + halves > 0)
            Log.Write($"[LEVEL] Painted walls: {coats} coats of paint turned into painted faces ({cuts} walls cut at their edges), {halves} walls of two halves made one " +
                      $"({System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:0} ms)");
    }

    /// <summary>
    /// The box whose face (facing along <paramref name="axis"/>) box <paramref name="thin"/> lies flat against, covering
    /// no more than it, with no paint on that face yet; and which side of it that is.
    /// </summary>
    private (int? Host, bool Plus) FindHost(int thin, int axis)
    {
        var t = _boxes[thin].Bounds;
        var best = ((int?)null, false);
        var bestArea = float.MaxValue;
        foreach (var j in PaintNear(t))
        {
            var wall = _boxes[j];
            if (j == thin || !wall.Visible || wall.Id == 0)
                continue;
            var plus = float.Abs(Along(t.Min, axis) - Along(wall.Bounds.Max, axis)) < Flush;
            var minus = float.Abs(Along(t.Max, axis) - Along(wall.Bounds.Min, axis)) < Flush;
            if ((!plus && !minus) || !FaceInside(t, wall.Bounds, axis) || (wall.PaintFaces & FaceBit(axis, plus)) != 0)
                continue;
            // (A box has one paint: a wall painted already takes only the same again, on another face.)
            var other = _boxes[thin];
            if (wall.PaintFaces != 0 && (wall.Paint != other.Color || (wall.PaintMaterial ?? wall.Material) != other.Material))
                continue;
            // (The smallest face it fits on: after a wall is cut, the piece it's on.)
            var area = 1f;
            for (var o = 0; o < 3; o++)
                if (o != axis)
                    area *= Along(wall.Bounds.Size, o);
            if (area < bestArea)
                (best, bestArea) = ((j, plus), area);
        }
        return best;
    }

    /// <summary>
    /// Cuts box <paramref name="host"/> at the edges of <paramref name="coat"/> (on the two axes across
    /// <paramref name="axis"/>), paints the face <paramref name="face"/> of the piece behind it; returns how many pieces it's now.
    /// </summary>
    private int PaintCoat(int host, int face, int axis, LevelBox coat)
    {
        var wall = _boxes[host];
        var others = axis == 0 ? (1, 2) : (0, 1);
        List<(float Lo, float Hi, bool Under)> Cuts(int o)
        {
            float lo = Along(wall.Bounds.Min, o), hi = Along(wall.Bounds.Max, o);
            float cLo = float.Max(lo, Along(coat.Bounds.Min, o)), cHi = float.Min(hi, Along(coat.Bounds.Max, o));
            var spans = new List<(float, float, bool)>();
            if (cLo - lo > Flush) spans.Add((lo, cLo, false));
            spans.Add((cLo, cHi, true));
            if (hi - cHi > Flush) spans.Add((cHi, hi, false));
            if (spans.Count == 1)
                spans[0] = (lo, hi, true);   // (it covers the whole face that way: no cut)
            return spans;
        }
        var first = true;
        var pieces = 0;
        foreach (var (lo1, hi1, under1) in Cuts(others.Item1))
        {
            foreach (var (lo2, hi2, under2) in Cuts(others.Item2))
            {
                var min = With(With(wall.Bounds.Min, others.Item1, lo1), others.Item2, lo2);
                var max = With(With(wall.Bounds.Max, others.Item1, hi1), others.Item2, hi2);
                var piece = wall with { Bounds = new Aabb(min, max) };
                if (under1 && under2)
                    piece = piece with { Paint = coat.Color, PaintFaces = wall.PaintFaces | face, PaintMaterial = coat.Material == wall.Material ? null : coat.Material };
                if (first)
                {
                    _boxes[host] = piece;   // (the pieces keep the wall's Id: the editor moves them as one)
                }
                else
                {
                    _boxes.Add(piece);
                    AddToPaintGrid(_boxes.Count - 1, piece.Bounds);
                }
                first = false;
                pieces++;
            }
        }
        return pieces;
    }
}
