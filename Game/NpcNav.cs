using System.Diagnostics;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// How people get about: where someone can stand on the floor they're on, a way round what's in the way (through doorways
/// and round corners), and a step that slides along walls and keeps the body, and the arms swinging at its sides, off them.
/// <para>
/// Each floor is a flat grid, made from the same collision boxes the player walks on, a chunk at a time as people first go
/// there, and kept. A search on a floor goes in two steps, so a long way costs little more than a short one: first over the
/// chunks, from each connected piece of open floor in a chunk to those it touches next door, for a corridor there; then A*
/// over the cells of that corridor. Floors are joined by <see cref="NavLink"/>s (ladders, flights of stairs): if the goal is on
/// another floor, or can't be got to on this one, a search over the links finds which to take, and <see cref="FindPath"/>
/// gives the way to it.
/// </para>
/// <para>
/// What moves or comes and goes (doors, an offline portal's blocker) isn't in the grid: <see cref="BeginFrame"/> takes a copy
/// each frame. The body is stopped by things as they are; the searches take a door people can open to stand open (they'll
/// open it when they get there: <see cref="DoorInWay"/>), and any other shut door to stop a path. Geometry that only stops
/// rays (it isn't a collision box) is learned as they bump into it: <see cref="Learn"/>.
/// </para>
/// </summary>
public sealed class NpcNav(Level level)
{
    /// <summary>How far the middle of a body stays from a wall (m): the shoulders, and the elbows out a little.</summary>
    public const float WallRadius = 0.3f;

    private const float Cell = 0.2f;              // m (the narrowest doorway, 0.9 m, leaves 0.3 m of room: always a line of cells)
    private const int ChunkShift = 5;             // 32 x 32 cells to a chunk
    private const int ChunkCells = 1 << ChunkShift;
    private const float ChunkSize = Cell * ChunkCells;
    private const float FeetClear = 0.2f;         // m: what's lower than this they step over
    private const float BodyTop = Npc.BodyHeight; // m: what's higher than this they walk under
    private const float GroundBelow = 0.6f;       // m: a floor this far below their feet still counts
    private const float Skin = 0.03f;             // m: a body this far into the margin still counts as clear (far from the origin, a float is only good to a few mm)
    private const float SampleStep = 0.1f;        // m along a straight line
    private const int MaxCorridorNodes = 1500;    // pieces of floor a search looks over
    private const int MaxExpansions = 12000;      // cells; a search gives up here, and goes as near as it got
    private const float Estimated = 1.25f;        // the cell search leans towards the goal: a little longer way, far fewer cells (it's pulled tight after)
    private const float LearnedFor = 20f;         // s: something bumped into stays on the map this long
    private const float FrameBudgetMs = 3f;       // ms of searching a frame: the rest wait for the next
    private const int GiantChunks = 256;          // a box wider than this many chunks isn't put in each of them (the ground)

    private const byte Blocked = 1, Ground = 2, KeepOff = 4;

    private sealed class Chunk(byte[] cells)
    {
        public readonly byte[] Cells = cells;
        public short[]? Regions;                  // which piece of open floor each cell's in (-1: not open)
        public int RegionCount;
        public int RegionsFor;                    // the doors' places these regions were worked out for
        public bool HadDoor;                      // something that moves was in the chunk then
        public int AllowedSearch;                 // the search Allowed is for
        public ulong Allowed;                     // the regions in that search's corridor (bit 63: and any beyond)
        public int Left, NextX, NextZ;            // chunks left along the corridor from here, and the middle cell of the next
    }

    private readonly Dictionary<long, List<Aabb>> _boxIndex = [];
    private readonly List<Aabb> _giantBoxes = [];
    private readonly Dictionary<long, Aabb[]> _chunkBoxes = [];
    private readonly Dictionary<long, Chunk> _chunks = [];
    private long _lastChunkKey = long.MinValue;
    private Chunk? _lastChunk;
    private readonly List<Aabb> _moving = [];
    private readonly HashSet<long> _movingChunks = [];
    private readonly List<Aabb> _planned = [];    // what the searches take to be in the way: a door people can open as if it stood open
    private readonly HashSet<long> _plannedChunks = [];
    private readonly List<Aabb> _movedLast = [];
    private int _plannedVersion;                  // changes when what the searches take to be in the way has moved
    private readonly Dictionary<long, float> _learned = [];
    private int _version = int.MinValue;
    private float _now, _sweepLeft;
    private long _frameTicks;
    private bool _searchedThisFrame;
    private int _search;

    // The search's own storage, kept between searches.
    private readonly Dictionary<long, int> _nodeAt = [];
    private readonly List<Node> _nodes = [];
    private readonly PriorityQueue<int, float> _open = new();
    private readonly List<(int X, int Z)> _cells = [];
    private readonly List<int> _touching = [];

    private struct Node
    {
        public int X, Z, Parent, Region;
        public float Cost;
        public bool Closed;
    }

    /// <summary>What the searches have cost, for BRUTALFPS_NAVCHECK.</summary>
    public int Searches { get; private set; }
    public long SearchTicks { get; private set; }
    public double WorstSearchMs { get; private set; }

    /// <summary>Debug: count again from here.</summary>
    public void StartWindow()
    {
        Searches = 0;
        SearchTicks = 0;
        WorstSearchMs = 0;
    }

    public int BoxCount => level.Boxes.Count;

    /// <summary>Whether there's time left this frame for another search (always the first).</summary>
    public bool CanSearch => !_searchedThisFrame || _frameTicks * 1000.0 / Stopwatch.Frequency < FrameBudgetMs;

    // (Made once: a lambda written in BeginFrame's loops would be a new delegate for every moving box, every frame.)
    private Action<int, int>? _addMovingChunk;
    private Action<int, int>? _addPlannedChunk;

    /// <summary>Call once a frame, before anyone moves: where the doors are now.</summary>
    public void BeginFrame(float now, float dt)
    {
        _now = now;
        _frameTicks = 0;
        _searchedThisFrame = false;
        _moving.Clear();
        _movingChunks.Clear();
        _plannedChunks.Clear();
        foreach (var box in level.MovingBoxes)
        {
            if (!box.Collides)
                continue;
            _moving.Add(box.Bounds);
            ForChunksOf(box.Bounds, WallRadius + Cell, _addMovingChunk ??= (x, z) => _movingChunks.Add(Key(x, z)));
        }

        level.NavPlanBoxes(_planned);
        foreach (var box in _planned)
            ForChunksOf(box, WallRadius + Cell, _addPlannedChunk ??= (x, z) => _plannedChunks.Add(Key(x, z)));
        if (!_planned.SequenceEqual(_movedLast))
        {
            _plannedVersion++;
            _reachable.Clear();
            _movedLast.Clear();
            _movedLast.AddRange(_planned);
        }

        if (level.BoxVersion != _version)
        {
            _version = level.BoxVersion;
            BuildBoxIndex();
            BuildLinks();
        }

        _sweepLeft -= dt;
        if (_sweepLeft <= 0f && _learned.Count > 0)
        {
            _sweepLeft = 2f;
            foreach (var (cell, until) in _learned)
            {
                if (until <= now)
                    _learned.Remove(cell);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------- the map

    private static int CellOf(float v) => (int)float.Floor(v / Cell);
    private static float CentreOf(int cell) => (cell + 0.5f) * Cell;
    private static int FloorOf(float y) => (int)float.Round(y * 20f);
    private static long Key(int x, int z) => ((long)x << 32) | (uint)z;
    private static long ChunkKey(int cx, int cz, int floor) => ((long)(cx & 0xFFFFFF) << 40) | ((long)(cz & 0xFFFFFF) << 16) | (uint)(floor & 0xFFFF);
    private static long LearnKey(int x, int z, int floor) => ((long)x << 40) | ((long)(z & 0xFFFFF) << 20) | (uint)(floor & 0xFFFFF);
    private static long RegionKey(int cx, int cz, int region) => ((long)(cx & 0xFFFFFF) << 40) | ((long)(cz & 0xFFFFFF) << 16) | (uint)(region & 0xFFFF);

    private static void ForChunksOf(in Aabb box, float margin, Action<int, int> visit)
    {
        var x0 = CellOf(box.Min.X - margin) >> ChunkShift;
        var x1 = CellOf(box.Max.X + margin) >> ChunkShift;
        var z0 = CellOf(box.Min.Z - margin) >> ChunkShift;
        var z1 = CellOf(box.Max.Z + margin) >> ChunkShift;
        for (var z = z0; z <= z1; z++)
        {
            for (var x = x0; x <= x1; x++)
                visit(x, z);
        }
    }

    private void BuildBoxIndex()
    {
        _boxIndex.Clear();
        _giantBoxes.Clear();
        _chunkBoxes.Clear();
        _chunks.Clear();
        _lastChunk = null;
        _lastChunkKey = long.MinValue;
        var margin = WallRadius + Cell;
        foreach (var levelBox in level.Boxes)
        {
            if (!levelBox.Collides)
                continue;
            var box = levelBox.Bounds;
            var size = box.Size;
            if (float.Max(size.X, size.Z) > GiantChunks * ChunkSize)
            {
                _giantBoxes.Add(box);
                continue;
            }

            ForChunksOf(box, margin, (x, z) =>
            {
                if (!_boxIndex.TryGetValue(Key(x, z), out var list))
                    _boxIndex[Key(x, z)] = list = [];
                list.Add(box);
            });
        }
    }

    /// <summary>The collision boxes that could touch a body standing in that chunk.</summary>
    private Aabb[] BoxesIn(int cx, int cz)
    {
        var key = Key(cx, cz);
        if (_chunkBoxes.TryGetValue(key, out var found))
            return found;
        var list = new List<Aabb>();
        if (_boxIndex.TryGetValue(key, out var indexed))
            list.AddRange(indexed);
        var margin = WallRadius + Cell;
        var x0 = cx * ChunkSize - margin;
        var z0 = cz * ChunkSize - margin;
        var x1 = x0 + ChunkSize + margin * 2f;
        var z1 = z0 + ChunkSize + margin * 2f;
        foreach (var box in _giantBoxes)
        {
            if (box.Min.X < x1 && box.Max.X > x0 && box.Min.Z < z1 && box.Max.Z > z0)
                list.Add(box);
        }

        return _chunkBoxes[key] = [.. list];
    }

    private Chunk ChunkAt(int cx, int cz, int floor)
    {
        var key = ChunkKey(cx, cz, floor);
        if (key == _lastChunkKey)
            return _lastChunk!;
        if (!_chunks.TryGetValue(key, out var chunk))
            _chunks[key] = chunk = new Chunk(Rasterize(cx, cz, floor));
        _lastChunkKey = key;
        _lastChunk = chunk;
        return chunk;
    }

    private byte[] Rasterize(int cx, int cz, int floor)
    {
        using var timer = Perf.NavRasterize.Time();
        _stats.Rasters++;
        var cells = new byte[ChunkCells * ChunkCells];
        var y = floor / 20f;
        var (ox, oz) = (cx << ChunkShift, cz << ChunkShift);
        foreach (var box in BoxesIn(cx, cz))
        {
            var blocks = InBand(box, y);
            var top = box.Max.Y;
            var ground = top >= y - GroundBelow && top <= y + FeetClear;
            if (!blocks && !ground)
                continue;
            var pad = blocks ? WallRadius : 0f;
            var x0 = int.Max(CellOf(box.Min.X - pad) - 1, ox);
            var x1 = int.Min(CellOf(box.Max.X + pad) + 1, ox + ChunkCells - 1);
            var z0 = int.Max(CellOf(box.Min.Z - pad) - 1, oz);
            var z1 = int.Min(CellOf(box.Max.Z + pad) + 1, oz + ChunkCells - 1);
            for (var z = z0; z <= z1; z++)
            {
                var pz = CentreOf(z);
                if (pz < box.Min.Z - pad || pz > box.Max.Z + pad)
                    continue;
                for (var x = x0; x <= x1; x++)
                {
                    var px = CentreOf(x);
                    if (px < box.Min.X - pad || px > box.Max.X + pad)
                        continue;
                    cells[(z - oz) * ChunkCells + (x - ox)] |= blocks ? Blocked : Ground;
                }
            }
        }

        // A flight of stairs is not a floor: nobody walks out level over it, or under it. They go by the link.
        foreach (var link in _links)
        {
            if (link.KeepOff is not { } off || y < off.Min.Y - 0.05f || y > off.Max.Y + 0.05f)
                continue;
            var x0 = int.Max(CellOf(off.Min.X), ox);
            var x1 = int.Min(CellOf(off.Max.X), ox + ChunkCells - 1);
            var z0 = int.Max(CellOf(off.Min.Z), oz);
            var z1 = int.Min(CellOf(off.Max.Z), oz + ChunkCells - 1);
            for (var z = z0; z <= z1; z++)
            {
                for (var x = x0; x <= x1; x++)
                    cells[(z - oz) * ChunkCells + (x - ox)] |= KeepOff;
            }
        }

        return cells;
    }

    /// <summary>
    /// Numbers the connected pieces of open floor in a chunk (flood fill, a cell at a time along the four sides), as the
    /// doors are now: a shut door parts the floor either side of it, and so does a sliding leaf that's rolled up against a wall.
    /// </summary>
    private void FindRegions(Chunk chunk, int cx, int cz, int floor)
    {
        using var timer = Perf.NavRegions.Time();
        _stats.Regions++;
        var regions = chunk.Regions ?? new short[ChunkCells * ChunkCells];
        Array.Fill(regions, (short)-1);
        var door = _plannedChunks.Contains(Key(cx, cz));
        Span<bool> open = stackalloc bool[ChunkCells * ChunkCells];
        var y = floor / 20f;
        for (var i = 0; i < open.Length; i++)
        {
            open[i] = chunk.Cells[i] == Ground
                      && (!door || PlannedDepth(CentreOf((cx << ChunkShift) + (i & (ChunkCells - 1))), CentreOf((cz << ChunkShift) + (i >> ChunkShift)), y) <= 0f);
        }

        Span<short> stack = stackalloc short[ChunkCells * ChunkCells];
        short count = 0;
        for (var start = 0; start < regions.Length; start++)
        {
            if (regions[start] >= 0 || !open[start])
                continue;
            var top = 0;
            stack[top++] = (short)start;
            regions[start] = count;
            while (top > 0)
            {
                int at = stack[--top];
                var (x, z) = (at & (ChunkCells - 1), at >> ChunkShift);
                for (var side = 0; side < 4; side++)
                {
                    var (nx, nz) = side switch { 0 => (x - 1, z), 1 => (x + 1, z), 2 => (x, z - 1), _ => (x, z + 1) };
                    if (nx < 0 || nz < 0 || nx >= ChunkCells || nz >= ChunkCells)
                        continue;
                    var next = nz * ChunkCells + nx;
                    if (regions[next] >= 0 || !open[next])
                        continue;
                    regions[next] = count;
                    stack[top++] = (short)next;
                }
            }

            count++;
        }

        chunk.Regions = regions;
        chunk.RegionCount = count;
        chunk.RegionsFor = _plannedVersion;
        chunk.HadDoor = door;
    }

    private static bool InBand(in Aabb box, float y) => box.Max.Y > y + FeetClear && box.Min.Y < y + BodyTop;

    private byte CellFlags(int x, int z, int floor) =>
        ChunkAt(x >> ChunkShift, z >> ChunkShift, floor).Cells[(z & (ChunkCells - 1)) * ChunkCells + (x & (ChunkCells - 1))];

    /// <summary>Whether there's a floor to stand on there, at about that height.</summary>
    public bool HasGround(float3 p) => (CellFlags(CellOf(p.X), CellOf(p.Z), FloorOf(p.Y)) & (Ground | KeepOff)) == Ground;

    private bool CellOpen(int x, int z, int floor, float y)
    {
        if (CellFlags(x, z, floor) != Ground)
            return false;
        if (_learned.Count > 0 && _learned.TryGetValue(LearnKey(x, z, floor), out var until) && until > _now)
            return false;
        return !_plannedChunks.Contains(Key(x >> ChunkShift, z >> ChunkShift)) || PlannedDepth(CentreOf(x), CentreOf(z), y) <= 0f;
    }

    /// <summary>Whether someone could stand at <paramref name="p"/>: a floor under them and room for the body.</summary>
    public bool Walkable(float3 p) => CellOpen(CellOf(p.X), CellOf(p.Z), FloorOf(p.Y), p.Y);

    /// <summary>Something that isn't a collision box stopped someone standing at <paramref name="from"/>, just <paramref name="at"/>: keep off it for a while.</summary>
    public void Learn(float3 from, float3 at)
    {
        var (x, z) = (CellOf(at.X), CellOf(at.Z));
        var own = (CellOf(from.X), CellOf(from.Z));
        foreach (var cell in (ReadOnlySpan<(int, int)>)[(x, z), (x - 1, z), (x + 1, z), (x, z - 1), (x, z + 1)])
        {
            if (cell != own)
                _learned[LearnKey(cell.Item1, cell.Item2, FloorOf(from.Y))] = _now + LearnedFor;
        }
    }

    // ---------------------------------------------------------------------------------------------- the body

    private static float DepthIn(in Aabb box, float x, float z)
    {
        var depth = float.Min(float.Min(x - box.Min.X, box.Max.X - x), float.Min(z - box.Min.Z, box.Max.Z - z)) + WallRadius;
        return depth > 0f ? depth : 0f;
    }

    private float PlannedDepth(float x, float z, float y)
    {
        var deepest = 0f;
        foreach (var box in _planned)
        {
            if (InBand(box, y))
                deepest = float.Max(deepest, DepthIn(box, x, z));
        }

        return deepest;
    }

    /// <summary>How far a body at <paramref name="p"/> is into something (0: it's clear), and what it's deepest in.</summary>
    private float Depth(float3 p, out Aabb deepestBox)
    {
        var deepest = 0f;
        deepestBox = default;
        var (cx, cz) = (CellOf(p.X) >> ChunkShift, CellOf(p.Z) >> ChunkShift);
        if (_movingChunks.Contains(Key(cx, cz)))
        {
            foreach (var box in _moving)
            {
                if (!InBand(box, p.Y))
                    continue;
                var depth = DepthIn(box, p.X, p.Z);
                if (depth > deepest)
                {
                    deepest = depth;
                    deepestBox = box;
                }
            }
        }

        foreach (var box in BoxesIn(cx, cz))
        {
            if (!InBand(box, p.Y))
                continue;
            var depth = DepthIn(box, p.X, p.Z);
            if (depth > deepest)
            {
                deepest = depth;
                deepestBox = box;
            }
        }

        return deepest;
    }

    /// <summary>
    /// One step of a body from <paramref name="p"/> along <paramref name="move"/>, an axis at a time so it slides along a
    /// wall, and not off the edge of a floor. Someone too near a wall already (they were put there, or a door shut on them)
    /// is eased out by the nearest way as they go, and can't go any further in.
    /// </summary>
    public float3 Slide(float3 p, float3 move)
    {
        var q = p;
        var depth = Depth(q, out var inside);
        var grounded = HasGround(q);
        // (Not one just into the margin, within the skin, which counts as clear: at the corner of a wall, the nearest way
        // out of it could be straight back against where they're going, and they'd rock to and fro there, a step out and
        // a step back in, never sliding round it.)
        if (depth > Skin)
        {
            var amount = float.Sqrt(move.X * move.X + move.Z * move.Z);
            var (left, right) = (q.X - inside.Min.X + WallRadius, inside.Max.X - q.X + WallRadius);
            var (back, front) = (q.Z - inside.Min.Z + WallRadius, inside.Max.Z - q.Z + WallRadius);
            var least = float.Min(float.Min(left, right), float.Min(back, front));
            var push = float.Min(amount, least);
            var o = q;
            if (least == left)
                o.X -= push;
            else if (least == right)
                o.X += push;
            else if (least == back)
                o.Z -= push;
            else
                o.Z += push;
            var there = Depth(o, out _);
            if (there < depth && (!grounded || HasGround(o)))
            {
                q = o;
                depth = there;
            }
        }

        // Along each axis in turn. Whatever one's stopped, the other gets, so they keep their pace along a wall and round a corner.
        var allowed = float.Max(depth, Skin);
        var x = q with { X = q.X + move.X };
        var xOk = move.X != 0f && Depth(x, out _) <= allowed && (!grounded || HasGround(x));
        if (xOk)
            q = x;
        var z = q with { Z = q.Z + move.Z };
        var zOk = move.Z != 0f && Depth(z, out _) <= allowed && (!grounded || HasGround(z));
        if (zOk)
            q = z;
        if (xOk != zOk && move.X != 0f && move.Z != 0f)
        {
            var length = float.Sqrt(move.X * move.X + move.Z * move.Z);
            var more = q;
            if (xOk)
                more.X += float.Sign(move.X) * (length - float.Abs(move.X));
            else
                more.Z += float.Sign(move.Z) * (length - float.Abs(move.Z));
            if (Depth(more, out _) <= allowed && (!grounded || HasGround(more)))
                q = more;
        }

        return q;
    }

    /// <summary>Whether a body can walk the straight line from <paramref name="a"/> to <paramref name="b"/> (at <paramref name="a"/>'s height).</summary>
    public bool LineClear(float3 a, float3 b)
    {
        var d = new float3(b.X - a.X, 0, b.Z - a.Z);
        var length = float.Sqrt(float3.Dot(d, d));
        var steps = int.Max(1, (int)float.Ceiling(length / SampleStep));
        var floor = FloorOf(a.Y);
        var (lastX, lastZ) = (int.MinValue, int.MinValue);
        for (var i = 0; i <= steps; i++)
        {
            var t = (float)i / steps;
            var (x, z) = (CellOf(a.X + d.X * t), CellOf(a.Z + d.Z * t));
            if (x == lastX && z == lastZ)
                continue;
            (lastX, lastZ) = (x, z);
            if (!CellOpen(x, z, floor, a.Y))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The first thing solid along the straight line from <paramref name="from"/> to <paramref name="to"/> (not the floor
    /// under <paramref name="feet"/>, nor anything <paramref name="from"/> is inside). For arms and guns: what they'd poke through.
    /// </summary>
    public bool Reach(float3 from, float3 to, float feet, out float distance, out float3 normal)
    {
        distance = float.MaxValue;
        normal = default;
        var d = to - from;
        var length = float.Sqrt(float3.Dot(d, d));
        if (length < 1e-4f)
            return false;
        var dir = d / length;
        var hit = false;
        var (cx0, cz0) = (CellOf(from.X) >> ChunkShift, CellOf(from.Z) >> ChunkShift);
        var (cx1, cz1) = (CellOf(to.X) >> ChunkShift, CellOf(to.Z) >> ChunkShift);
        if (_movingChunks.Contains(Key(cx0, cz0)) || _movingChunks.Contains(Key(cx1, cz1)))
        {
            foreach (var box in _moving)
                hit |= ReachBox(box, from, dir, length, feet, ref distance, ref normal);
        }

        foreach (var box in BoxesIn(cx0, cz0))
            hit |= ReachBox(box, from, dir, length, feet, ref distance, ref normal);
        if (cx1 != cx0 || cz1 != cz0)
        {
            foreach (var box in BoxesIn(cx1, cz1))
                hit |= ReachBox(box, from, dir, length, feet, ref distance, ref normal);
        }

        return hit;
    }

    private static bool ReachBox(in Aabb box, float3 from, float3 dir, float length, float feet, ref float best, ref float3 normal)
    {
        if (box.Max.Y <= feet + FeetClear || box.Contains(from))
            return false;
        if (!box.Raycast(from, dir, float.Min(length, best), out var t, out var boxNormal) || t >= best)
            return false;
        best = t;
        normal = boxNormal;
        return true;
    }

    /// <summary>Debug: the map of a floor as text, a character to a cell: '.' open, '#' too near something, ' ' no floor, 'm' a door or the like, 'L' learned.</summary>
    public IEnumerable<string> Map(float x0, float z0, float x1, float z1, float y)
    {
        var floor = FloorOf(y);
        for (var z = CellOf(z1); z >= CellOf(z0); z--)
        {
            var line = new System.Text.StringBuilder($"{CentreOf(z),7:0.0} ");
            for (var x = CellOf(x0); x <= CellOf(x1); x++)
            {
                var flags = CellFlags(x, z, floor);
                line.Append((flags & Blocked) != 0 ? '#' : (flags & Ground) == 0 ? ' ' : _learned.ContainsKey(LearnKey(x, z, floor)) ? 'L' : PlannedDepth(CentreOf(x), CentreOf(z), y) > 0f ? 'm' : '.');
            }

            yield return line.ToString();
        }
    }

    /// <summary>Debug: what's round a body there.</summary>
    public string Describe(float3 p)
    {
        var near = new List<string>();
        foreach (var box in BoxesIn(CellOf(p.X) >> ChunkShift, CellOf(p.Z) >> ChunkShift))
        {
            if (InBand(box, p.Y) && float.Max(float.Max(box.Min.X - p.X, p.X - box.Max.X), float.Max(box.Min.Z - p.Z, p.Z - box.Max.Z)) < 0.6f)
                near.Add($"[{box.Min.X:0.00}..{box.Max.X:0.00}, {box.Min.Y:0.00}..{box.Max.Y:0.00}, {box.Min.Z:0.00}..{box.Max.Z:0.00}]");
        }

        return $"depth {Depth(p, out _):0.00}, ground {HasGround(p)}, moving boxes {_moving.Count}, near: {string.Join(" ", near)}";
    }

    // ---------------------------------------------------------------------------------------------- doors

    /// <summary>The doors people can open.</summary>
    public IReadOnlyList<NpcDoor> Doors => level.NpcDoors;

    /// <summary>
    /// A door people can open that's stopping a body going straight from <paramref name="from"/> towards <paramref name="to"/>,
    /// looking <paramref name="reach"/> m past where the body would touch it.
    /// </summary>
    public NpcDoor? DoorInWay(float3 from, float3 to, float reach = 0.95f)
    {
        var d = new float3(to.X - from.X, 0, to.Z - from.Z);
        var length = float.Sqrt(float3.Dot(d, d));
        if (length < 1e-3f)
            return null;
        var dir = d / length;
        var far = float.Min(length, reach + WallRadius);
        foreach (var door in level.NpcDoors)
        {
            if (!door.Allowed || door.Openness >= 1f)
                continue;
            if (Crosses(door.Shut, from, dir, far) && (door.Hinged ? door.Openness < HingedClear : Crosses(door.LeavesNow, from, dir, far)))
                return door;
        }

        return null;
    }

    private static bool Crosses(IEnumerable<Aabb> boxes, float3 from, float3 dir, float far)
    {
        var (lo, hi) = (from.Y + FeetClear, from.Y + BodyTop);
        foreach (var box in boxes)
        {
            if (box.Max.Y <= lo || box.Min.Y >= hi)
                continue;
            var pad = new float3(WallRadius, 0, WallRadius);
            var wide = new Aabb(new float3(box.Min.X - pad.X, lo - 1f, box.Min.Z - pad.Z), new float3(box.Max.X + pad.X, hi + 1f, box.Max.Z + pad.Z));
            var at = new float3(from.X, (lo + hi) / 2, from.Z);
            if (wide.Contains(at) || (wide.Raycast(at, dir, far, out var t, out _) && t <= far))
                return true;
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------- floors and links

    private const float HingedClear = 0.6f;       // how far a hinged door has to be open to go through it
    private const float LinksWithin = 800f;       // m: a link further off than this is in another part of the world
    private readonly List<NavLink> _links = [];
    private readonly Dictionary<(long, long, int), bool> _reachable = [];   // from one piece of floor to another, on a floor, as things are now
    private readonly PriorityQueue<int, float> _linkOpen = new();
    private float[] _linkCost = [];
    private int[] _linkFirst = [];

    /// <summary>All the ladders and flights of stairs.</summary>
    public List<NavLink> Links => _links;

    /// <summary>The far end of <paramref name="link"/> from a point on it.</summary>
    public static float3 Far(NavLink link, float3 near) =>
        float3.DistanceSquared(link.Bottom, near) <= float3.DistanceSquared(link.Top, near) ? link.Top : link.Bottom;

    private void BuildLinks()
    {
        _links.Clear();
        _reachable.Clear();
        _links.AddRange(level.StairLinks);
        _links.AddRange(level.Ladders.Select(LadderLink).OfType<NavLink>());
        _linkCost = new float[_links.Count * 2];
        _linkFirst = new int[_links.Count * 2];
        // (The flights are off the floors from now on: what was worked out before they were known is out of date.)
        _chunks.Clear();
        _lastChunk = null;
        _lastChunkKey = long.MinValue;
    }

    /// <summary>
    /// A ladder's link: a body on the floor in front of it, then at the rungs, then up them, then off at the top the way the
    /// player would go, as far as there's floor to stand on. Null if there's nowhere to stand at one end.
    /// </summary>
    private NavLink? LadderLink(Ladder ladder, int index)
    {
        var (volume, facing) = (ladder.Volume, ladder.Facing);
        var c = volume.Center;
        var near = new float3(c.X, volume.Min.Y, c.Z) + facing * (float.Abs(float3.Dot(volume.Size, facing)) / 2);   // the face of the rungs, on the floor
        var stand = near - facing * 0.24f;
        float3? foot = null;
        for (var d = 0.45f; d < 1.6f && foot is null; d += 0.1f)
        {
            var p = near - facing * d;
            if (Walkable(p))
                foot = p;
        }

        var high = stand with { Y = ladder.Top };
        float3? head = null;
        foreach (var dir in (ReadOnlySpan<float3>)[ladder.Exit, ladder.Exit == facing ? -facing : facing, new float3(facing.Z, 0, -facing.X), new float3(-facing.Z, 0, facing.X)])
        {
            for (var d = 0.1f; d < 2.6f && head is null; d += 0.1f)
            {
                var p = high + dir * d;
                if (Walkable(p))
                    head = p + dir * 0.15f;
            }

            if (head is not null)
                break;
        }

        if (foot is not { } a || head is not { } b)
        {
            Log.Write($"[WARN] Ladder {index} at ({c.X:0.0}, {volume.Min.Y:0.0}, {c.Z:0.0}) has nowhere to stand ({(foot is null ? "at its foot" : "at its head")}): nobody will use it");
            return null;
        }

        return new NavLink($"ladder {index + 1}", [a, stand, high, b], 1, facing, null);
    }

    /// <summary>
    /// Which floor a point is on: the highest that has ground there at or under it, out of the floor they're on and those the
    /// links nearby lead to. (A point can be someone's feet or their eyes. There's nowhere you can stand under a floor that's lower than your eyes.)
    /// </summary>
    public float FloorHeight(float3 p, float standingOn)
    {
        var best = float.MinValue;
        void Try(float y)
        {
            if (y > best && y <= p.Y + 0.3f && HasGroundAnyway(p with { Y = y }))
                best = y;
        }

        Try(standingOn);
        // A link's end within a step of the floor they're on, where that floor has ground, is that floor: the top of a ladder
        // out of a pit that comes up at 0.2 m isn't another storey over the yard round it.
        var own = best == standingOn;
        foreach (var link in _links)
        {
            if (!InReach(link.Bottom, p))
                continue;
            foreach (var y in (ReadOnlySpan<float>)[link.Bottom.Y, link.Top.Y])
            {
                if (!own || float.Abs(y - standingOn) > SameLevel)
                    Try(y);
            }
        }

        if (best == float.MinValue)
            return standingOn;
        if (own)
            return best;
        // From another storey: of the levels within a step of each other that have ground there, the lowest (a floor counts
        // ground a little above it, so it's the one that takes in the others: the yard, not the top of a ladder out of a pit in it).
        var lowest = best;
        foreach (var link in _links)
        {
            if (!InReach(link.Bottom, p))
                continue;
            foreach (var y in (ReadOnlySpan<float>)[link.Bottom.Y, link.Top.Y])
            {
                if (y < lowest && y >= best - SameLevel && HasGroundAnyway(p with { Y = y }))
                    lowest = y;
            }
        }

        return lowest;
    }

    private const float SameLevel = 0.35f;   // m: a link's end this near the floor they're on is on it (a step up or down)

    /// <summary>As <see cref="HasGround"/>, on a flight too: someone on the stairs counts as on the floor at the bottom or the top of it.</summary>
    private bool HasGroundAnyway(float3 p) => (CellFlags(CellOf(p.X), CellOf(p.Z), FloorOf(p.Y)) & Ground) != 0;

    /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> are on the same floor, as far as anyone can tell.</summary>
    public bool SameFloor(float3 a, float3 b) => FloorOf(a.Y) == FloorOf(FloorHeight(b, a.Y));

    private static bool InReach(float3 a, float3 b) => float.Abs(a.X - b.X) < LinksWithin && float.Abs(a.Z - b.Z) < LinksWithin;

    private bool Plan(float3 from, float3 to, List<float3> path, out NavLink? via)
    {
        via = null;
        var floor = FloorOf(from.Y);
        var goalY = FloorHeight(to, from.Y);
        _stats.GoalY = goalY;
        if (FloorOf(goalY) == floor)
        {
            var found = Search(from, to with { Y = from.Y }, path, out var arrived);
            if (arrived)
                return found;
        }

        if (FirstLink(from, to with { Y = goalY }, out var link))
        {
            var entry = float3.DistanceSquared(link.Bottom, from) <= float3.DistanceSquared(link.Top, from) ? link.Bottom : link.Top;
            if (FloorOf(entry.Y) != floor)
                entry = FloorOf(link.Bottom.Y) == floor ? link.Bottom : link.Top;
            path.Clear();
            var found = Search(from, entry with { Y = from.Y }, path, out _);
            via = link;
            return found;
        }

        if (path.Count > 0)
            return true;   // (no link leads there: this floor's way, as far as it goes)
        return Search(from, to with { Y = from.Y }, path, out _);
    }

    /// <summary>
    /// Whether two points on a floor are joined, doors counted open. Remembered by the pieces of floor they're on, until
    /// something moves: a corridor search that fails has looked over everywhere it could go.
    /// </summary>
    private bool Reachable(float3 a, float3 b, int floor)
    {
        if (!NearestOpen(a, 6, floor, out var ca) || !NearestOpen(b, 8, floor, out var cb))
            return false;
        var (ax, az) = (ca.X >> ChunkShift, ca.Z >> ChunkShift);
        var (bx, bz) = (cb.X >> ChunkShift, cb.Z >> ChunkShift);
        var ra = RegionsOf(ax, az, floor).Regions![(ca.Z & (ChunkCells - 1)) * ChunkCells + (ca.X & (ChunkCells - 1))];
        var rb = RegionsOf(bx, bz, floor).Regions![(cb.Z & (ChunkCells - 1)) * ChunkCells + (cb.X & (ChunkCells - 1))];
        if (ra < 0 || rb < 0)
            return false;
        var key = (RegionKey(ax, az, ra), RegionKey(bx, bz, rb), floor);
        if (_reachable.TryGetValue(key, out var known))
            return known;
        FindCorridor(ca, cb, floor, out var reached);
        return _reachable[key] = reached;
    }

    /// <summary>
    /// Dijkstra over the links: each ladder or flight, in each direction, is a step from one end of it to the other, and
    /// each end joins all the others on its floor that can be walked to. The answer is the first link on the cheapest way to the goal.
    /// </summary>
    private bool FirstLink(float3 from, float3 goal, out NavLink first)
    {
        first = null!;
        if (_links.Count == 0)
            return false;
        using var timer = Perf.NavLinks.Time();
        _stats.LinkSearches++;
        var goalFloor = FloorOf(goal.Y);
        Array.Fill(_linkCost, float.MaxValue);
        _linkOpen.Clear();

        // A state: 2i, having just gone up link i (at its top); 2i + 1, having just gone down it.
        void Leave(float3 at, float cost, int firstLink)
        {
            var floor = FloorOf(at.Y);
            for (var i = 0; i < _links.Count; i++)
            {
                var link = _links[i];
                foreach (var goingUp in (ReadOnlySpan<bool>)[true, false])
                {
                    var entry = goingUp ? link.Bottom : link.Top;
                    if (FloorOf(entry.Y) != floor || !InReach(entry, at))
                        continue;
                    var state = goingUp ? 2 * i : 2 * i + 1;
                    var total = cost + FlatDistance(at, entry) + link.Cost;
                    if (total >= _linkCost[state] || !Reachable(at, entry, floor))
                        continue;
                    _linkCost[state] = total;
                    _linkFirst[state] = firstLink >= 0 ? firstLink : i;
                    _linkOpen.Enqueue(state, total);
                }
            }
        }

        Leave(from, 0f, -1);
        var best = float.MaxValue;
        var bestFirst = -1;
        while (_linkOpen.TryDequeue(out var state, out var cost))
        {
            if (cost > _linkCost[state] || cost >= best)
                continue;
            var link = _links[state / 2];
            var at = state % 2 == 0 ? link.Top : link.Bottom;
            if (FloorOf(at.Y) == goalFloor && Reachable(at, goal, goalFloor))
            {
                var total = cost + FlatDistance(at, goal);
                if (total < best)
                {
                    best = total;
                    bestFirst = _linkFirst[state];
                }
            }

            Leave(at, cost, _linkFirst[state]);
        }

        if (bestFirst < 0)
            return false;
        first = _links[bestFirst];
        return true;
    }

    private static float FlatDistance(float3 a, float3 b) => float.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    // ---------------------------------------------------------------------------------------------- the search

    /// <summary>The open cell nearest <paramref name="p"/>, within <paramref name="reach"/> cells.</summary>
    private bool NearestOpen(float3 p, int reach, int floor, out (int X, int Z) cell)
    {
        var (x, z) = (CellOf(p.X), CellOf(p.Z));
        cell = (x, z);
        if (CellOpen(x, z, floor, p.Y))
            return true;
        var best = float.MaxValue;
        var found = false;
        for (var r = 1; r <= reach; r++)
        {
            for (var dz = -r; dz <= r; dz++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    if (int.Max(int.Abs(dx), int.Abs(dz)) != r || !CellOpen(x + dx, z + dz, floor, p.Y))
                        continue;
                    var (ex, ez) = (CentreOf(x + dx) - p.X, CentreOf(z + dz) - p.Z);
                    var dd = ex * ex + ez * ez;
                    if (dd < best)
                    {
                        best = dd;
                        cell = (x + dx, z + dz);
                        found = true;
                    }
                }
            }

            if (found)
                return true;
        }

        return false;
    }

    private static float Estimate(int x, int z, (int X, int Z) to)
    {
        var (dx, dz) = (int.Abs(x - to.X), int.Abs(z - to.Z));
        return (dx + dz + (1.4142f - 2f) * int.Min(dx, dz)) * Cell;
    }

    private Chunk RegionsOf(int cx, int cz, int floor)
    {
        var chunk = ChunkAt(cx, cz, floor);
        if (chunk.Regions is null || (chunk.RegionsFor != _plannedVersion && (chunk.HadDoor || _plannedChunks.Contains(Key(cx, cz)))))
            FindRegions(chunk, cx, cz, floor);
        return chunk;
    }

    /// <summary>The regions next door to this one on one side: those with an open cell against one of its own.</summary>
    private void Touching(Chunk chunk, int region, Chunk other, int side)
    {
        _touching.Clear();
        for (var i = 0; i < ChunkCells; i++)
        {
            var (mine, theirs) = side switch
            {
                0 => (i * ChunkCells, i * ChunkCells + ChunkCells - 1),                           // -X
                1 => (i * ChunkCells + ChunkCells - 1, i * ChunkCells),                           // +X
                2 => (i, (ChunkCells - 1) * ChunkCells + i),                                      // -Z
                _ => ((ChunkCells - 1) * ChunkCells + i, i),                                      // +Z
            };
            if (chunk.Regions![mine] != region)
                continue;
            int r = other.Regions![theirs];
            if (r >= 0 && !_touching.Contains(r))
                _touching.Add(r);
        }
    }

    /// <summary>
    /// The first step: over the chunks, from the piece of floor they're on towards the goal's (or, if it can't be got to,
    /// the piece nearest it). Marks the way as the search's corridor; <paramref name="reached"/>: it got to the goal's own piece.
    /// </summary>
    private (int Cx, int Cz, int Region) FindCorridor((int X, int Z) start, (int X, int Z) goal, int floor, out bool reached)
    {
        using var timer = Perf.NavCorridor.Time();
        var corridor = FindCorridorTimed(start, goal, floor, out reached, out var looked);
        _stats.Corridors++;
        _stats.CorridorNodes += looked;
        if (!reached)
            _stats.CorridorsFailed++;
        if (looked >= MaxCorridorNodes)
            _stats.CorridorsCapped++;
        return corridor;
    }

    private (int Cx, int Cz, int Region) FindCorridorTimed((int X, int Z) start, (int X, int Z) goal, int floor, out bool reached, out int looked)
    {
        var (gx, gz) = (goal.X >> ChunkShift, goal.Z >> ChunkShift);
        var goalChunk = RegionsOf(gx, gz, floor);
        int goalRegion = goalChunk.Regions![(goal.Z & (ChunkCells - 1)) * ChunkCells + (goal.X & (ChunkCells - 1))];
        var (sx, sz) = (start.X >> ChunkShift, start.Z >> ChunkShift);
        var startChunk = RegionsOf(sx, sz, floor);
        int startRegion = startChunk.Regions![(start.Z & (ChunkCells - 1)) * ChunkCells + (start.X & (ChunkCells - 1))];

        float Guess(int cx, int cz)
        {
            var (dx, dz) = (cx - gx, cz - gz);
            return float.Sqrt(dx * dx + dz * dz) * ChunkSize;
        }

        _nodeAt.Clear();
        _nodes.Clear();
        _open.Clear();
        _nodes.Add(new Node { X = sx, Z = sz, Region = startRegion, Parent = -1 });
        _nodeAt[RegionKey(sx, sz, startRegion)] = 0;
        _open.Enqueue(0, Guess(sx, sz));
        var best = 0;
        var bestLeft = Guess(sx, sz);
        reached = false;
        looked = 0;
        while (_open.Count > 0 && looked < MaxCorridorNodes)
        {
            var i = _open.Dequeue();
            var node = _nodes[i];
            if (node.Closed)
                continue;
            node.Closed = true;
            _nodes[i] = node;
            looked++;
            var left = Guess(node.X, node.Z);
            if (left < bestLeft)
            {
                bestLeft = left;
                best = i;
            }

            if (node.X == gx && node.Z == gz && node.Region == goalRegion)
            {
                best = i;
                reached = true;
                break;
            }

            var chunk = RegionsOf(node.X, node.Z, floor);
            for (var side = 0; side < 4; side++)
            {
                var (nx, nz) = side switch { 0 => (node.X - 1, node.Z), 1 => (node.X + 1, node.Z), 2 => (node.X, node.Z - 1), _ => (node.X, node.Z + 1) };
                Touching(chunk, node.Region, RegionsOf(nx, nz, floor), side);
                var cost = node.Cost + ChunkSize;
                foreach (var region in _touching)
                {
                    var key = RegionKey(nx, nz, region);
                    if (_nodeAt.TryGetValue(key, out var seen))
                    {
                        var other = _nodes[seen];
                        if (other.Closed || other.Cost <= cost)
                            continue;
                        other.Cost = cost;
                        other.Parent = i;
                        _nodes[seen] = other;
                        _open.Enqueue(seen, cost + Guess(nx, nz));
                    }
                    else
                    {
                        _nodeAt[key] = _nodes.Count;
                        _nodes.Add(new Node { X = nx, Z = nz, Region = region, Parent = i, Cost = cost });
                        _open.Enqueue(_nodes.Count - 1, cost + Guess(nx, nz));
                    }
                }
            }
        }

        _search++;
        var chunksLeft = 0;
        var (nextX, nextZ) = (0, 0);
        for (var i = best; i >= 0; i = _nodes[i].Parent)
        {
            var chunk = ChunkAt(_nodes[i].X, _nodes[i].Z, floor);
            if (chunk.AllowedSearch != _search)
            {
                // (A corridor can leave a chunk and come back into it, round a wall: out of a shack's back, round to its
                // door, back in. The chunk keeps the way on from where it's nearest the goal, or every cell in it would
                // look further off than it is, the goal's own among them.)
                chunk.AllowedSearch = _search;
                chunk.Allowed = 0;
                chunk.Left = chunksLeft;
                chunk.NextX = nextX;
                chunk.NextZ = nextZ;
            }

            chunk.Allowed |= 1UL << int.Min(_nodes[i].Region, 63);
            chunksLeft++;
            (nextX, nextZ) = ((_nodes[i].X << ChunkShift) + ChunkCells / 2, (_nodes[i].Z << ChunkShift) + ChunkCells / 2);
        }

        return (_nodes[best].X, _nodes[best].Z, _nodes[best].Region);
    }

    /// <summary>How far on from a cell, for the cell search: on along the corridor to the next chunk's middle; in the last chunk, to the goal itself.</summary>
    private float EstimateAlong(int x, int z, (int X, int Z) goal, int floor)
    {
        var chunk = ChunkAt(x >> ChunkShift, z >> ChunkShift, floor);
        return chunk.AllowedSearch != _search || chunk.Left == 0
            ? Estimate(x, z, goal)
            : chunk.Left * ChunkSize + Estimate(x, z, (chunk.NextX, chunk.NextZ));
    }

    private bool InCorridor(int x, int z, int floor)
    {
        var chunk = ChunkAt(x >> ChunkShift, z >> ChunkShift, floor);
        if (chunk.AllowedSearch != _search)
            return false;
        int region = chunk.Regions![(z & (ChunkCells - 1)) * ChunkCells + (x & (ChunkCells - 1))];
        return region >= 0 && (chunk.Allowed >> int.Min(region, 63) & 1UL) != 0;
    }

    /// <summary>
    /// A way from <paramref name="from"/> towards <paramref name="to"/>, as few straight legs as will do, into
    /// <paramref name="path"/>: all the way if it's on this floor; otherwise, up to the start of <paramref name="via"/>, the
    /// ladder or flight to take next (look again at the far end). If there's no way there it's the way to the nearest place
    /// they could get to on this floor. False: they can't go anywhere (no floor, or walled in).
    /// </summary>
    /// <param name="who">Who's asking, for the log line a slow search gets (<see cref="SlowSearchMs"/>).</param>
    public bool FindPath(float3 from, float3 to, List<float3> path, out NavLink? via, string? who = null)
    {
        path.Clear();
        _stats = default;
        _stats.ChunksBefore = _chunks.Count;
        var started = Stopwatch.GetTimestamp();
        var found = Plan(from, to, path, out via);
        var ticks = Stopwatch.GetTimestamp() - started;
        _frameTicks += ticks;
        _searchedThisFrame = true;
        Searches++;
        SearchTicks += ticks;
        LastSearchMs = ticks * 1000.0 / Stopwatch.Frequency;
        WorstSearchMs = double.Max(WorstSearchMs, LastSearchMs);
        _stats.From = from;
        _stats.To = to;
        _stats.Found = found;
        _stats.Via = via?.Name;
        _stats.Legs = path.Count;
        if (LastSearchMs >= SlowSearchMs)
            Log.Write($"[NAV] {who ?? "someone"}: slow path search, {DescribeLastSearch()}");
        return found;
    }

    /// <summary>How long the last <see cref="FindPath"/> took (ms).</summary>
    public double LastSearchMs { get; private set; }

    /// <summary>A search slower than this is worth a line in the log (<see cref="DescribeLastSearch"/>).</summary>
    public const double SlowSearchMs = 5.0;

    private struct SearchStats
    {
        public float3 From, To;
        public float GoalY;
        public bool Found;
        public string? Via;
        public int Legs, ChunksBefore;
        public int Corridors, CorridorsFailed, CorridorsCapped, CorridorNodes;
        public int CellSearches, CellsCapped, Expansions, PathCells, NotArrived, GoalShut;
        public int LinkSearches, Regions, Rasters;
    }

    private SearchStats _stats;

    /// <summary>What the last search did, for a slow one's line in the log.</summary>
    public string DescribeLastSearch()
    {
        var s = _stats;
        return $"{LastSearchMs:0.0} ms from ({s.From.X:0.0}, {s.From.Y:0.0}, {s.From.Z:0.0}) to ({s.To.X:0.0}, {s.To.Y:0.0}, {s.To.Z:0.0}), "
            + $"{FlatDistance(s.From, s.To):0.0} m, goal taken to be on the floor at {s.GoalY:0.00} (floor {FloorOf(s.GoalY)}; start's {FloorOf(s.From.Y)}): {(s.Found ? $"{s.Legs} legs" : "no way")}{(s.Via is null ? "" : $", via {s.Via}")}; "
            + $"corridor searches {s.Corridors} ({s.CorridorsFailed} failed, {s.CorridorsCapped} hit the {MaxCorridorNodes} cap, {s.CorridorNodes} nodes); "
            + $"cell searches {s.CellSearches} ({s.Expansions} cells, {s.CellsCapped} hit the {MaxExpansions} cap, {s.NotArrived} didn't arrive, {s.GoalShut} goal not open, {s.PathCells} on the way); "
            + $"link searches {s.LinkSearches} of {_links.Count} links; regions worked out {s.Regions}, chunks rasterized {s.Rasters} ({s.ChunksBefore} cached before)";
    }

    private bool Search(float3 from, float3 to, List<float3> path, out bool arrived)
    {
        arrived = false;
        var floor = FloorOf(from.Y);
        if (!NearestOpen(from, 6, floor, out var start))
            return false;
        to.Y = from.Y;
        var goalOpen = NearestOpen(to, 12, floor, out var goal);

        // Over the chunks first. If the goal itself can't be got to, the cell search aims at what's nearest it in the last piece of floor.
        var (lastX, lastZ, lastRegion) = FindCorridor(start, goal, floor, out var reached);
        if (!reached || !goalOpen)
        {
            var chunk = RegionsOf(lastX, lastZ, floor);
            var bestLeft = float.MaxValue;
            var near = goal;
            for (var i = 0; i < chunk.Regions!.Length; i++)
            {
                if (chunk.Regions[i] != lastRegion)
                    continue;
                var (cx, cz) = ((lastX << ChunkShift) + (i & (ChunkCells - 1)), (lastZ << ChunkShift) + (i >> ChunkShift));
                var (ex, ez) = (CentreOf(cx) - to.X, CentreOf(cz) - to.Z);
                var left = ex * ex + ez * ez;
                if (left < bestLeft)
                {
                    bestLeft = left;
                    near = (cx, cz);
                }
            }

            goal = near;
        }

        // Then A* over the corridor's cells.
        var cellsTimer = Perf.NavCells.Time();
        _nodeAt.Clear();
        _nodes.Clear();
        _open.Clear();
        _nodes.Add(new Node { X = start.X, Z = start.Z, Parent = -1 });
        _nodeAt[Key(start.X, start.Z)] = 0;
        _open.Enqueue(0, EstimateAlong(start.X, start.Z, goal, floor));
        var best = 0;
        var nearest = EstimateAlong(start.X, start.Z, goal, floor);
        var expansions = 0;
        while (_open.Count > 0 && expansions < MaxExpansions)
        {
            var i = _open.Dequeue();
            var node = _nodes[i];
            if (node.Closed)
                continue;
            node.Closed = true;
            _nodes[i] = node;
            expansions++;
            var left = EstimateAlong(node.X, node.Z, goal, floor);
            if (left < nearest)
            {
                nearest = left;
                best = i;
            }

            if (node.X == goal.X && node.Z == goal.Z)
            {
                best = i;   // (there: whatever the estimate made of somewhere else on the way)
                break;
            }

            for (var dz = -1; dz <= 1; dz++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    var (nx, nz) = (node.X + dx, node.Z + dz);
                    if (!InCorridor(nx, nz, floor) || !CellOpen(nx, nz, floor, from.Y))
                        continue;
                    // Not across the corner of something.
                    if (dx != 0 && dz != 0 && (!CellOpen(node.X + dx, node.Z, floor, from.Y) || !CellOpen(node.X, node.Z + dz, floor, from.Y)))
                        continue;
                    var cost = node.Cost + (dx != 0 && dz != 0 ? Cell * 1.4142f : Cell);
                    var key = Key(nx, nz);
                    if (_nodeAt.TryGetValue(key, out var seen))
                    {
                        var other = _nodes[seen];
                        if (other.Closed || other.Cost <= cost)
                            continue;
                        other.Cost = cost;
                        other.Parent = i;
                        _nodes[seen] = other;
                        _open.Enqueue(seen, cost + Estimated * EstimateAlong(nx, nz, goal, floor));
                    }
                    else
                    {
                        _nodeAt[key] = _nodes.Count;
                        _nodes.Add(new Node { X = nx, Z = nz, Parent = i, Cost = cost });
                        _open.Enqueue(_nodes.Count - 1, cost + Estimated * EstimateAlong(nx, nz, goal, floor));
                    }
                }
            }
        }

        cellsTimer.Dispose();
        _stats.CellSearches++;
        _stats.Expansions += expansions;
        if (expansions >= MaxExpansions)
            _stats.CellsCapped++;

        // Back along the parents, then pull it tight: from each point, on to the furthest one in a straight line.
        using var smoothTimer = Perf.NavSmooth.Time();
        _cells.Clear();
        for (var i = best; i >= 0; i = _nodes[i].Parent)
            _cells.Add((_nodes[i].X, _nodes[i].Z));
        _cells.Reverse();
        var at = new float3(CentreOf(_cells[0].X), from.Y, CentreOf(_cells[0].Z));
        path.Add(at);
        var anchor = 0;
        while (anchor < _cells.Count - 1)
        {
            var next = anchor + 1;
            for (var j = _cells.Count - 1; j > next; j -= int.Max(1, (j - next) / 4))
            {
                if (LineClear(at, new float3(CentreOf(_cells[j].X), from.Y, CentreOf(_cells[j].Z))))
                {
                    next = j;
                    break;
                }
            }

            at = new float3(CentreOf(_cells[next].X), from.Y, CentreOf(_cells[next].Z));
            path.Add(at);
            anchor = next;
        }

        _stats.PathCells += _cells.Count;
        arrived = reached && goalOpen && _cells[^1] == goal;
        if (!arrived)
            _stats.NotArrived++;
        if (!goalOpen)
            _stats.GoalShut++;
        if (arrived && LineClear(at, to))
            path.Add(to);
        return true;
    }
}
