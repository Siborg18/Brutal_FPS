using System.Globalization;
using System.Text;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// BRUTALFPS_CLIMBSCAN=file: walks round every solid box in the level, tries a vault at it from each side
/// (<see cref="PlayerVault.Plan"/>), and writes out every place a climb up onto something would be possible (and the low
/// things you'd only vault over), grouped into places, by area. For deciding which climbs the level allows
/// (<see cref="Level.NoClimbAreas"/>).
/// </summary>
public static class ClimbScan
{
    /// <summary>The level's solid boxes in a grid (4 m cells), for many queries.</summary>
    private sealed class GridSolids : ISolids
    {
        private const float CellSize = 4f;
        private readonly List<Aabb> _boxes = [];
        private readonly Dictionary<long, List<int>> _cells = [];
        private readonly List<int> _giant = [];
        private readonly int[] _stamp;
        private int _query;

        public GridSolids(Level level)
        {
            foreach (var b in level.Boxes)
                if (b.Collides)
                    _boxes.Add(b.Bounds);
            _stamp = new int[_boxes.Count];
            for (var i = 0; i < _boxes.Count; i++)
            {
                var (x0, z0, x1, z1) = Range(_boxes[i]);
                if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > 4096)
                {
                    _giant.Add(i);
                    continue;
                }
                for (var x = x0; x <= x1; x++)
                    for (var z = z0; z <= z1; z++)
                    {
                        var key = Key(x, z);
                        if (!_cells.TryGetValue(key, out var list))
                            _cells[key] = list = [];
                        list.Add(i);
                    }
            }
        }

        public IReadOnlyList<Aabb> Boxes => _boxes;

        private static (int, int, int, int) Range(in Aabb b) =>
            ((int)float.Floor(b.Min.X / CellSize), (int)float.Floor(b.Min.Z / CellSize), (int)float.Floor(b.Max.X / CellSize), (int)float.Floor(b.Max.Z / CellSize));

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        private void Each(in Aabb q, Func<Aabb, bool> visit)
        {
            _query++;
            foreach (var i in _giant)
                if (visit(_boxes[i]))
                    return;
            var (x0, z0, x1, z1) = Range(q);
            for (var x = x0; x <= x1; x++)
                for (var z = z0; z <= z1; z++)
                {
                    if (!_cells.TryGetValue(Key(x, z), out var list))
                        continue;
                    foreach (var i in list)
                    {
                        if (_stamp[i] == _query)
                            continue;
                        _stamp[i] = _query;
                        if (visit(_boxes[i]))
                            return;
                    }
                }
        }

        public bool Blocked(in Aabb box)
        {
            var b = box;
            var hit = false;
            Each(box, o => hit = o.Overlaps(b));
            return hit;
        }

        public float HighestTop(in Aabb column)
        {
            var c = column;
            var top = float.NegativeInfinity;
            Each(column, o =>
            {
                if (o.Max.Y > top && o.Overlaps(c))
                    top = o.Max.Y;
                return false;
            });
            return top;
        }
    }

    private readonly record struct Found(Zones Zone, VaultPlan Plan, int Box);

    /// <summary>
    /// Where the player can walk (no vaults): a flood over a 0.3 m grid from the seeds, stepping up only where the level
    /// has stairs, dropping off any edge, and through gaps a crouching body fits.
    /// </summary>
    private sealed class Walkable(GridSolids solids)
    {
        private const float Cell = 0.3f;
        private readonly HashSet<long> _nodes = [];
        private readonly Queue<(int, int, int)> _open = [];

        public int Count => _nodes.Count;

        // (x and z cells and the floor's height in cm, 21 bits each, signed.)
        private static long Pack((int X, int Z, int Y) n) =>
            ((long)(n.X & 0x1FFFFF) << 42) | ((long)(n.Z & 0x1FFFFF) << 21) | (long)(n.Y & 0x1FFFFF);

        private static (int, int, int) Unpack(long k) =>
            ((int)(k << 1 >> 43), (int)(k << 22 >> 43), (int)(k << 43 >> 43));

        /// <summary>How many cells each area has, and how far each spreads (for spotting a flood that's got out).</summary>
        public string Report() => string.Join("; ", _nodes.Select(Unpack)
            .GroupBy(n => Level.ZoneAt(new float3(n.Item1 * Cell, n.Item3 / 100f, n.Item2 * Cell)))
            .Select(g => $"{g.Key} {g.Count()} x {g.Min(n => n.Item1) * Cell:0}..{g.Max(n => n.Item1) * Cell:0} z {g.Min(n => n.Item2) * Cell:0}..{g.Max(n => n.Item2) * Cell:0}"));

        private static (int, int) CellOf(float3 p) => ((int)float.Round(p.X / Cell), (int)float.Round(p.Z / Cell));

        /// <summary>Starts a flood from where the feet would land below <paramref name="p"/>.</summary>
        public void Seed(float3 p)
        {
            var (x, z) = CellOf(p);
            var at = new float3(x * Cell, p.Y, z * Cell);
            var floor = Floor(at, p.Y + 0.3f, 4f);
            if (float.IsNegativeInfinity(floor) || floor > p.Y + 0.3f || solids.Blocked(Body(at with { Y = floor + 0.001f }, PlayerController.CrouchHeight)))
                return;
            var key = (x, z, (int)float.Round(floor * 100f));
            if (_nodes.Add(Pack(key)))
                _open.Enqueue(key);
        }

        public void Flood(int limit = 20_000_000)
        {
            ReadOnlySpan<(int, int)> steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];
            while (_open.Count > 0 && _nodes.Count < limit)
            {
                var (x, z, ycm) = _open.Dequeue();
                var y = ycm / 100f;
                foreach (var (dx, dz) in steps)
                {
                    var p = new float3((x + dx) * Cell, y, (z + dz) * Cell);
                    var up = Level.HasStairs(p) ? 0.42f : 0.01f;   // (a stride of a cell can take in two risers: the treads are narrower than it)
                    var floor = Floor(p, y + up, 30f);
                    if (float.IsNegativeInfinity(floor) || floor > y + up + 0.001f)   // (nothing to stand on, or something too high to step onto)
                        continue;
                    // Room to go: crouched at the higher of the two floors, and where it lands.
                    if (solids.Blocked(Body(p with { Y = float.Max(y, floor) + 0.001f }, PlayerController.CrouchHeight))
                        || solids.Blocked(Body(p with { Y = floor + 0.001f }, PlayerController.CrouchHeight)))
                        continue;
                    var key = (x + dx, z + dz, (int)float.Round(floor * 100f));
                    if (_nodes.Add(Pack(key)))
                        _open.Enqueue(key);
                }
            }
        }

        /// <summary>Whether the feet at <paramref name="p"/> are somewhere walked to.</summary>
        public bool Has(float3 p)
        {
            var (x, z) = CellOf(p);
            var ycm = (int)float.Round(p.Y * 100f);
            for (var dx = -1; dx <= 1; dx++)
                for (var dz = -1; dz <= 1; dz++)
                    for (var dy = -3; dy <= 3; dy++)
                        if (_nodes.Contains(Pack((x + dx, z + dz, ycm + dy))))
                            return true;
            return false;
        }

        private float Floor(float3 p, float top, float depth) => solids.HighestTop(new Aabb(
            new float3(p.X - PlayerController.Radius + 0.01f, top - depth, p.Z - PlayerController.Radius + 0.01f),
            new float3(p.X + PlayerController.Radius - 0.01f, top, p.Z + PlayerController.Radius - 0.01f)));

        private static Aabb Body(float3 feet, float height) =>
            new(feet - new float3(PlayerController.Radius, 0, PlayerController.Radius), feet + new float3(PlayerController.Radius, height, PlayerController.Radius));
    }

    public static void Run(Level level, IEnumerable<float3> seeds, string path)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var solids = new GridSolids(level);
        var boxes = solids.Boxes;
        const float r = PlayerController.Radius;
        var found = new List<Found>();
        var all = Environment.GetEnvironmentVariable("BRUTALFPS_CLIMBALL") == "1";
        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            var size = b.Size;
            if (size.Y < PlayerVault.MinRise)
                continue;
            // Each side: stand just off the face, facing it, on whatever floor is there below its top.
            for (var side = 0; side < 4; side++)
            {
                var alongX = side >= 2;   // sides 0,1: the x faces (walk along z); 2,3: the z faces
                var outward = side switch { 0 => new float3(1, 0, 0), 1 => new float3(-1, 0, 0), 2 => new float3(0, 0, 1), _ => new float3(0, 0, -1) };
                var length = alongX ? size.X : size.Z;
                var samples = int.Max(1, (int)float.Floor((length - 0.2f) / 0.75f) + 1);
                for (var k = 0; k < samples; k++)
                {
                    var t = samples == 1 ? 0.5f : (0.1f + k * (length - 0.2f) / (samples - 1)) / length;
                    var x = alongX ? b.Min.X + size.X * t : side == 0 ? b.Max.X + r + 0.06f : b.Min.X - r - 0.06f;
                    var z = !alongX ? b.Min.Z + size.Z * t : side == 2 ? b.Max.Z + r + 0.06f : b.Min.Z - r - 0.06f;
                    var floor = solids.HighestTop(new Aabb(
                        new float3(x - r + 0.01f, b.Max.Y - PlayerVault.MaxRise - 0.01f, z - r + 0.01f),
                        new float3(x + r - 0.01f, b.Max.Y - PlayerVault.MinRise, z + r - 0.01f)));
                    if (float.IsNegativeInfinity(floor))
                        continue;
                    var feet = new float3(x, floor + 0.0005f, z);
                    if (solids.Blocked(new Aabb(feet - new float3(r, 0, r), feet + new float3(r, PlayerController.Height, r))))
                        continue;
                    // (As the player gets them: nothing that ends in a no-climb area, unless BRUTALFPS_CLIMBALL=1 asks for everything.)
                    if (PlayerVault.Plan(solids, feet, -outward, all ? null : level.ClimbAllowed) is { } plan && (all || level.ClimbAllowed(plan.To)))
                        found.Add(new Found(Level.ZoneAt(feet), plan, i));
                }
            }
        }

        // Where you can get to on foot (round 0), then with the vaults from there too (rounds 1, 2...: up onto a crate,
        // then from it onto the next).
        if (Environment.GetEnvironmentVariable("BRUTALFPS_CLIMBDEBUG")?.Split(",").Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray() is [var dx0, var dy0, var dz0, var ddx, var ddz])
        {
            var at = new float3(dx0, dy0, dz0);
            Log.Write($"[CLIMBSCAN] debug from {at}: {PlayerVault.Plan(solids, at, new float3(ddx, 0, ddz))}");
        }
        var walk = new Walkable(solids);
        foreach (var s in seeds)
            walk.Seed(s);
        walk.Flood();
        var onFoot = walk.Count;
        Log.Write($"[CLIMBSCAN] on foot: {walk.Report()}");
        var walked = found.Select(f => walk.Has(f.Plan.To)).ToArray();
        var round = found.Select(f => walk.Has(f.Plan.From) ? 0 : -1).ToArray();
        for (var pass = 1; pass <= 4; pass++)
        {
            var any = false;
            for (var i = 0; i < found.Count; i++)
                if (round[i] >= 0 && round[i] < pass && !walk.Has(found[i].Plan.To))
                {
                    walk.Seed(found[i].Plan.To + new float3(0, 0.05f, 0));
                    any = true;
                }
            if (!any)
                break;
            walk.Flood();
            for (var i = 0; i < found.Count; i++)
                if (round[i] < 0 && walk.Has(found[i].Plan.From))
                    round[i] = pass;
        }

        // Only what matters: a vault you can get to that takes you somewhere you couldn't walk to. Grouped into places:
        // landings on the same top (within 5 cm) and near each other. (Box: the index into found, from here on.)
        var keep = Enumerable.Range(0, found.Count).Where(i => round[i] >= 0 && (!walked[i] || Environment.GetEnvironmentVariable("BRUTALFPS_CLIMBLISTWALKED") == "1")).ToList();
        var onto = keep.Where(i => found[i].Plan.Onto).Select(i => found[i] with { Box = i }).ToList();
        var over = keep.Where(i => !found[i].Plan.Onto).Select(i => found[i] with { Box = i }).ToList();
        var groups = Cluster(onto, 1.0f);
        var overGroups = Cluster(over, 1.0f);

        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.AppendLine(inv, $"# Climb scan: {found.Count} vaults from {boxes.Count} boxes; {onFoot} cells walkable on foot, {walk.Count} with vaults. " +
                           $"Leading somewhere new: {onto.Count} up onto things in {groups.Count} places, {over.Count} over things in {overGroups.Count} places ({System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} s)");
        sb.AppendLine("# kind\tzone\tlocal landing centre (x,y,z)\textent x z\trise min-max\tcount\tchain round\tfirst from (local)\tobstacle box local (min..max)");
        foreach (var (kind, list) in new[] { ("ONTO", groups), ("OVER", overGroups) })
        {
            foreach (var g in list.OrderBy(g => g[0].Zone).ThenBy(g => g.Average(f => f.Plan.To.X)).ThenBy(g => g.Average(f => f.Plan.To.Z)))
            {
                var zone = g[0].Zone;
                var o = OriginOf(zone);
                var min = new float3(g.Min(f => f.Plan.To.X), g.Min(f => f.Plan.To.Y), g.Min(f => f.Plan.To.Z));
                var max = new float3(g.Max(f => f.Plan.To.X), g.Max(f => f.Plan.To.Y), g.Max(f => f.Plan.To.Z));
                var local = (min + max) * 0.5f - o;
                var rises = g.Select(f => f.Plan.Top - f.Plan.From.Y).ToList();
                var chain = g.Min(f => round[f.Box]) + (g.All(f => walked[f.Box]) ? 10 : 0);   // (+10: you can walk there anyway)
                var from = g[0].Plan.From - o;
                var ob = boxes[found[g[0].Box].Box];
                sb.AppendLine(inv, $"{kind}\t{zone}\t{local.X:0.0},{local.Y:0.00},{local.Z:0.0}\t{max.X - min.X:0.0} {max.Z - min.Z:0.0}\t{rises.Min():0.00}-{rises.Max():0.00}\t{g.Count}\t{chain}\t" +
                                   $"{from.X:0.0},{from.Y:0.00},{from.Z:0.0}\t" +
                                   $"{ob.Min.X - o.X:0.00},{ob.Min.Y - o.Y:0.00},{ob.Min.Z - o.Z:0.00}..{ob.Max.X - o.X:0.00},{ob.Max.Y - o.Y:0.00},{ob.Max.Z - o.Z:0.00}");
            }
        }
        File.WriteAllText(path, sb.ToString());
        Log.Write($"[CLIMBSCAN] {found.Count} vaults; {groups.Count} new places to climb onto, {overGroups.Count} to vault over to: written to {path}");
    }

    private static List<List<Found>> Cluster(List<Found> items, float join)
    {
        // (Hashed into cells the size of the join, so each landing only looks at its neighbours.)
        var cells = new Dictionary<(int, int), List<int>>();
        (int, int) CellOf(float3 p) => ((int)float.Floor(p.X / join), (int)float.Floor(p.Z / join));
        for (var i = 0; i < items.Count; i++)
        {
            var key = CellOf(items[i].Plan.To);
            if (!cells.TryGetValue(key, out var list))
                cells[key] = list = [];
            list.Add(i);
        }
        var groups = new List<List<Found>>();
        var used = new bool[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            if (used[i])
                continue;
            used[i] = true;
            var group = new List<Found> { items[i] };
            for (var head = 0; head < group.Count; head++)
            {
                var a = group[head].Plan.To;
                var (cx, cz) = CellOf(a);
                for (var dx = -1; dx <= 1; dx++)
                    for (var dz = -1; dz <= 1; dz++)
                    {
                        if (!cells.TryGetValue((cx + dx, cz + dz), out var list))
                            continue;
                        foreach (var j in list)
                        {
                            if (used[j])
                                continue;
                            var c = items[j].Plan.To;
                            if (float.Abs(c.Y - a.Y) < 0.05f && float.Abs(c.X - a.X) < join && float.Abs(c.Z - a.Z) < join)
                            {
                                used[j] = true;
                                group.Add(items[j]);
                            }
                        }
                    }
            }
            groups.Add(group);
        }
        return groups;
    }

    public static float3 OriginOf(Zones zone) => zone switch
    {
        Zones.Yard => Level.YardOrigin,
        Zones.Outback or Zones.Cellar => Level.OutbackOrigin,
        Zones.Arctic => Level.ArcticOrigin,
        Zones.Jungle => Level.JungleOrigin,
        Zones.Police => Level.PoliceOrigin,
        Zones.Prison => Level.PrisonOrigin,
        Zones.Flats => Level.FlatsOrigin,
        Zones.Rio => Level.RioOrigin,
        Zones.Ship => Level.ShipOrigin,
        Zones.Bunker => Level.BunkerOrigin,
        _ => float3.Zero,
    };
}
