using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// A prison: low grey blockwork wings with dark slate hip roofs round an open yard, built from the user's reference
/// photos (an aerial view of a courtyard prison, a staff locker room, a cell corridor). It's the setting of the game's
/// cinematic intro (<see cref="PrisonIntro"/>): you walk in through the staff entrance, change in the locker room, go
/// down the cell corridor and out into the yard, where a shipping container with a portal in it is lowered in by
/// helicopter (<see cref="PrisonDelivery"/>). Unless the intro is playing, the container already stands in the yard.
///
/// Layout (looking down, north is -z; local coordinates from <see cref="PrisonOrigin"/>):
///
///      +------------------------- north wing (3 storeys) --------------------------+
///      |                                                               | corridor  |
///      |       +-----------------------------------------------+  [yard|  :  cells |
///      |  west |      lamp posts round the ring path           |  door]|  :  cells |
///      |  wing |            +--[ container ]--+  (portal at    |  east |  :  cells |
///      |       |            |   sports court  |   its west end)| wing  |  :  cells |
///      |       |       grass, young trees, hedge, benches      |       |  :  cells |
///      |       +-----------------------------------------------+       |  grille   |
///      |                        south wing                [locker room]            |
///      +-------------------------------------------------------[ gatehouse ]-------+
///                                                             staff entrance
///                                  perimeter road, wall
/// </summary>
public sealed partial class Level
{
    /// <summary>
    /// Where the prison is: two kilometres straight down (the shader's areas 9 and 10: y &lt; -1500), near x = z = 0 like
    /// the police station so its detail keeps full float precision.
    /// </summary>
    public static readonly float3 PrisonOrigin = new(0, -2000, 0);

    /// <summary>Below this height a point is in the prison (see <see cref="ZoneAt"/> and the shader's areaOf).</summary>
    public const float PrisonBelowY = -1500f;

    /// <summary>A point in the prison, from its local coordinates.</summary>
    public static float3 PrisonAt(float x, float y, float z) => PrisonOrigin + new float3(x, y, z);
    private static float3 JAt(float x, float y, float z) => PrisonOrigin + new float3(x, y, z);

    // The wings round the yard (outer faces), and the yard between them (their inner faces).
    private const float CourtX = 22f, CourtZ = 16f;
    private const float WingOutX = 34f, NorthOutZ = -28f, SouthOutZ = 28f;
    private const float NorthWingH = 10.4f, WingH = 7.2f;
    private const float JT = 0.3f;              // outside walls
    private const float RoofPitch = 0.47f;      // rise per metre (25 degrees)
    private const float Eaves = 0.6f;           // overhang

    // The gatehouse: a low white block on the south face, the staff entrance in its south wall.
    private const float GateX0 = 14f, GateX1 = 28f, GateZ1 = 36f, GateH = 4.2f;
    private const float EntranceX = 21f;                         // the entrance and the sally door line up on x = 21

    // The rooms you walk through (inner faces).
    private const float RoomCeiling = 3.0f;                      // gatehouse and locker room
    private const float CorridorCeiling = 3.2f;
    private const float LockerX0 = 14.3f, LockerX1 = 26f, LockerZ0 = 16.3f, LockerZ1 = 27.7f;
    private const float CorrX0 = 22.3f, CorrX1 = 25.3f, CorrZ0 = -15.7f, CorrZ1 = 16f;
    private const float CellWallX = 25.6f;                       // the corridor's east wall: x 25.3 .. 25.6
    private const float OpenCellZ = 2.0f, OpenCellX1 = 29.4f;    // the one cell standing open
    private const float YardDoorZ = -12.5f;                      // the door from the corridor out to the yard

    // The shipping container in the middle of the yard: along x, its closed end at the west, its doors at the east,
    // facing the corridor's yard door. Halfway along, a false wall with the portal in it hides the rear half.
    private const float PcLength = 12.19f, PcWidth = 2.44f, PcHeight = 2.59f, PcInnerHalf = 1.17f, PcInnerHeight = 2.39f;
    private const float PcWestX = -PcLength / 2, PcEastX = PcLength / 2;
    private const float PcEndWall = 0.1f;
    private const float PcFalseWallX = PcWestX + PcLength / 2;   // the false wall's front face

    /// <summary>The crimson portal: in the container's false wall, halfway along it, facing its doors.</summary>
    public static float3 PrisonPortalFloorPoint => JAt(PcFalseWallX, 0, 0);
    public static float3 PrisonPortalNormal => new(1, 0, 0);
    public static readonly float3 PrisonPortalColor = new(0.85f, 0.06f, 0.16f);

    /// <summary>The container's door end (centre of its opening at floor level), for the intro to look at.</summary>
    public static float3 PrisonContainerDoors => JAt(PcEastX, 0, 0);

    // Paint and finishes.
    private static readonly float3 PrisonGrey = new(0.52f, 0.52f, 0.50f);       // the blockwork outside
    private static readonly float3 PrisonPlinth = new(0.40f, 0.40f, 0.40f);
    private static readonly float3 PrisonCream = new(0.80f, 0.76f, 0.62f);      // blockwork inside
    private static readonly float3 PrisonBlue = new(0.30f, 0.40f, 0.48f);       // pilasters, doors and frames
    private static readonly float3 PrisonDoorBlue = new(0.26f, 0.36f, 0.45f);
    private static readonly float3 SlateTint = new(0.30f, 0.31f, 0.33f);
    private static readonly float3 FrameGrey = new(0.20f, 0.22f, 0.24f);
    private static readonly float3 GateWhite = new(0.86f, 0.86f, 0.83f);
    private static readonly float3 LockerSteel = new(0.42f, 0.45f, 0.48f);
    private static readonly float3 VinylGrey = new(0.62f, 0.62f, 0.60f);
    private static readonly float3 PcBlue = new(0.14f, 0.24f, 0.42f);

    // ------------------------------------------------------------------------------------------------
    // Rooms, lights, sun: what the renderer and the acoustics need.
    // ------------------------------------------------------------------------------------------------

    private static Aabb JBoxBetween(float x0, float y0, float z0, float x1, float y1, float z1) => BoxBetween(JAt(x0, y0, z0), JAt(x1, y1, z1));

    public static Aabb PrisonGatehouse => JBoxBetween(GateX0 + JT, 0, SouthOutZ, GateX1 - JT, RoomCeiling, GateZ1 - JT);
    public static Aabb PrisonLockerRoom => JBoxBetween(LockerX0, 0, LockerZ0, LockerX1, RoomCeiling, LockerZ1);
    public static Aabb PrisonCorridor => JBoxBetween(CorrX0, 0, CorrZ0, CorrX1, CorridorCeiling, CorrZ1 + JT);
    public static Aabb PrisonCell => JBoxBetween(CellWallX, 0, OpenCellZ - 1.6f, OpenCellX1, 2.8f, OpenCellZ + 1.6f);

    /// <summary>The whole prison and its grounds (its reverb room).</summary>
    public static Aabb PrisonGrounds => JBoxBetween(-200, -60, -200, 200, 150, 200);

    /// <summary>
    /// The rooms you can walk through: inside these the shader lights the prison like an office (ceiling fittings and
    /// bounce light, no sun). Each reaches a few centimetres into its walls, floor and ceiling.
    /// </summary>
    public static Aabb[] PrisonRooms =>
    [
        Grow(PrisonGatehouse), Grow(PrisonLockerRoom), Grow(PrisonCorridor), Grow(PrisonCell),
    ];

    private static Aabb Grow(Aabb box) => new(box.Min - new float3(0.06f, 0.12f, 0.06f), box.Max + new float3(0.06f, 0.06f, 0.06f));

    // Ceiling fittings: (x, z, room) with rooms 0 gatehouse, 1 locker room, 2 corridor, 3 the open cell.
    private static readonly (float X, float Z, int Room)[] PrisonFittings =
    [
        (16.3f, 32.0f, 0), (21.0f, 30.0f, 0), (21.0f, 33.8f, 0), (25.0f, 30.0f, 0), (25.0f, 33.8f, 0),
        (17.2f, 19.5f, 1), (20.9f, 19.5f, 1), (24.2f, 19.5f, 1), (17.2f, 24.6f, 1), (20.9f, 24.6f, 1), (24.2f, 24.6f, 1),
        (23.8f, 14.0f, 2), (23.8f, 10.5f, 2), (23.8f, 7.0f, 2), (23.8f, 3.5f, 2), (23.8f, 0.0f, 2), (23.8f, -3.5f, 2),
        (23.8f, -7.0f, 2), (23.8f, -10.5f, 2), (23.8f, -14.0f, 2),
        (27.6f, OpenCellZ, 3),
    ];

    private static Aabb PrisonRoomBounds(int room) => Grow(room switch { 0 => PrisonGatehouse, 1 => PrisonLockerRoom, 2 => PrisonCorridor, _ => PrisonCell });

    private static float FittingHeight(int room) => room switch { 2 => CorridorCeiling, 3 => 2.8f, _ => RoomCeiling };

    /// <summary>The prison's interior lights: its ceiling fittings, and daylight in through the corridor's windows.</summary>
    public static OfficeLight[] PrisonLights
    {
        get
        {
            var lights = PrisonFittings
                .Select(f => new OfficeLight(JAt(f.X, FittingHeight(f.Room) - 0.06f, f.Z), new float3(1.0f, 0.98f, 0.95f),
                    f.Room == 2 ? 3.2f : 3.5f, 8f, PrisonRoomBounds(f.Room)))
                .ToList();
            foreach (var z in (ReadOnlySpan<float>)[9.8f, -1.0f, -8.2f])
                lights.Add(new OfficeLight(JAt(CorrX0 + 0.25f, 1.8f, z), new float3(0.78f, 0.84f, 0.95f), 0.9f, 4.5f, PrisonRoomBounds(2), Panel: false));
            return [.. lights];
        }
    }

    /// <summary>The prison's sun: low in the south-west through a thin overcast, so its shadows are soft and weak.</summary>
    public static readonly float3 PrisonSunDirection = float3.Normalize(new float3(-0.42f, 0.58f, 0.70f));
    public static readonly float3 PrisonSunColor = new float3(1.0f, 0.95f, 0.86f) * 1.7f;
    public static float3 PrisonShadowCenter => JAt(0, 0, 4);
    public const float PrisonShadowHalfExtent = 54f;

    /// <summary>Inside the container: dim, lit from its doors.</summary>
    public static ShadeVolume[] PrisonShadeVolumes =>
    [
        new(JBoxBetween(PcWestX, -0.05f, -PcInnerHalf - 0.02f, PcEastX, PcInnerHeight + 0.02f, PcInnerHalf + 0.02f),
            JAt(PcEastX + 0.4f, 1.2f, 0), 7.5f, 0.22f),
    ];

    /// <summary>The container's inside, in front of its false wall (its own reverb room).</summary>
    public static Aabb PrisonContainerInterior => JBoxBetween(PcFalseWallX, 0, -PcInnerHalf, PcEastX, PcInnerHeight, PcInnerHalf);

    // ------------------------------------------------------------------------------------------------
    // Building.
    // ------------------------------------------------------------------------------------------------

    /// <summary>A box between two corners in prison coordinates.</summary>
    private void JBox(float x0, float y0, float z0, float x1, float y1, float z1, float3 tint, Material material, bool collides = true, bool blocksShots = true)
    {
        _boxes.Add(new LevelBox(JBoxBetween(x0, y0, z0, x1, y1, z1), tint, material, BlocksShots: blocksShots, Collides: collides));
        _version++;
    }

    /// <summary>A merged mesh for the prison (these never shade the police station's occlusion map).</summary>
    private MeshBuilder JB(Material material, float3 tint) => PB(material, tint, occludes: false);

    private readonly Dictionary<(Material, float3), MeshBuilder> _prisonDecals = [];

    /// <summary>Flat ground cover drawn over the grass (paths, the court, its lines), merged per material and tint.</summary>
    private MeshBuilder JDecal(Material material, float3 tint)
    {
        if (!_prisonDecals.TryGetValue((material, tint), out var b))
            _prisonDecals[(material, tint)] = b = new MeshBuilder();
        return b;
    }

    private static readonly float3 Up = new(0, 1, 0);

    /// <summary>A flat rectangle on the ground (local x0..x1, z0..z1) at height <paramref name="y"/>.</summary>
    private static void GroundRect(MeshBuilder b, float x0, float z0, float x1, float z1, float y) =>
        b.Quad(JAt(x0, y, z0), JAt(x1, y, z0), JAt(x1, y, z1), JAt(x0, y, z1), Up);

    private void BuildPrison()
    {
        var random = new Random(1906);
        BuildPrisonGround();
        BuildPrisonWings();
        BuildPrisonRoofs(random);
        BuildGatehouse(random);
        BuildLockerRoom(random);
        BuildCellCorridor(random);
        BuildPrisonYard(random);
        BuildPrisonContainer();
        BuildPerimeter(random);
        BuildPrisonDoors();
        FlushPoliceBatches();
        foreach (var ((material, tint), b) in _prisonDecals)
            _props.Add(new Prop(b.Build(), float4x4.Identity, tint, material, Decal: true));
        _prisonDecals.Clear();
    }

    // ------------------------------------------------------------------------------------------------
    // Ground: grass everywhere outside, floor slabs under the wings (their tops are hidden under the rooms' floors).
    // ------------------------------------------------------------------------------------------------

    private void BuildPrisonGround()
    {
        var grass = new float3(0.2f, 0.2f, 0.2f);
        // Outside the wings and the gatehouse (which have their own slabs), and the yard between them.
        JBox(-90, -1, -80, 90, 0, NorthOutZ, grass, Material.Grass);
        JBox(-90, -1, GateZ1, 90, 0, 90, grass, Material.Grass);
        JBox(-90, -1, NorthOutZ, -WingOutX, 0, GateZ1, grass, Material.Grass);
        JBox(WingOutX, -1, NorthOutZ, 90, 0, GateZ1, grass, Material.Grass);
        JBox(-WingOutX, -1, SouthOutZ, GateX0, 0, GateZ1, grass, Material.Grass);
        JBox(GateX1, -1, SouthOutZ, WingOutX, 0, GateZ1, grass, Material.Grass);
        JBox(-CourtX, -1, -CourtZ, CourtX, 0, CourtZ, grass, Material.Grass);
        // The wings' and the gatehouse's slabs, under their rooms' floors.
        var slab = new float3(0.45f, 0.44f, 0.42f);
        JBox(-WingOutX, -1, NorthOutZ, WingOutX, -0.3f, -CourtZ, slab, Material.Floor);
        JBox(-WingOutX, -1, CourtZ, WingOutX, -0.3f, SouthOutZ, slab, Material.Floor);
        JBox(-WingOutX, -1, -CourtZ, -CourtX, -0.3f, CourtZ, slab, Material.Floor);
        JBox(CourtX, -1, -CourtZ, WingOutX, -0.3f, CourtZ, slab, Material.Floor);
        JBox(GateX0, -1, SouthOutZ, GateX1, -0.3f, GateZ1, slab, Material.Floor);
    }

    // ------------------------------------------------------------------------------------------------
    // Walls with openings, and what fills the openings.
    // ------------------------------------------------------------------------------------------------

    /// <summary>A rectangular opening in a wall: from <paramref name="A"/> along it, <paramref name="W"/> wide, from height <paramref name="Y"/>, <paramref name="H"/> tall.</summary>
    private readonly record struct Hole(float A, float Y, float W, float H)
    {
        public static Hole Centered(float a, float y, float w, float h) => new(a - w / 2, y, w, h);
    }

    /// <summary>
    /// A wall running along x (<paramref name="axis"/> 0: a is x, t is z) or along z (axis 2: a is z, t is x), from a0
    /// to a1, t0 to t1 thick, y0 to y1 high, with the holes left open.
    /// </summary>
    private void HoledWall(int axis, float a0, float a1, float t0, float t1, float y0, float y1, float3 tint, Material material, IReadOnlyList<Hole> holes)
    {
        void Piece(float a, float b, float ya, float yb)
        {
            if (b - a < 1e-4f || yb - ya < 1e-4f)
                return;
            if (axis == 0)
                JBox(a, ya, t0, b, yb, t1, tint, material);
            else
                JBox(t0, ya, a, t1, yb, b, tint, material);
        }

        var ys = new SortedSet<float> { y0, y1 };
        foreach (var h in holes)
        {
            ys.Add(float.Clamp(h.Y, y0, y1));
            ys.Add(float.Clamp(h.Y + h.H, y0, y1));
        }
        var levels = ys.ToArray();
        for (var i = 0; i < levels.Length - 1; i++)
        {
            var ya = levels[i];
            var yb = levels[i + 1];
            var mid = (ya + yb) / 2;
            var a = a0;
            foreach (var h in holes.Where(h => h.Y < mid && h.Y + h.H > mid && h.A < a1 && h.A + h.W > a0).OrderBy(h => h.A))
            {
                Piece(a, float.Max(a0, h.A), ya, yb);
                a = float.Max(a, h.A + h.W);
            }
            Piece(a, a1, ya, yb);
        }
    }

    /// <summary>Evenly spaced positions (about <paramref name="spacing"/> apart) along a0..a1, at least one.</summary>
    private static float[] Spread(float a0, float a1, float spacing)
    {
        var n = int.Max(1, (int)float.Floor((a1 - a0) / spacing));
        return [.. Enumerable.Range(0, n).Select(i => a0 + (i + 0.5f) * (a1 - a0) / n)];
    }

    private enum Pane { Dark, Lit, Barred }

    /// <summary>
    /// Fills a window opening: a frame set back from the outer face, a sill outside, and dark glass (seen from
    /// outside), frosted glass lit by the daylight (seen from inside, behind dark glass outside) or, with no glass,
    /// bars. <paramref name="outerT"/> is the wall's outer face, <paramref name="innerT"/> its inner face.
    /// </summary>
    private void WindowFill(int axis, Hole hole, float outerT, float innerT, Pane pane, float3 frameTint)
    {
        var outward = float.Sign(outerT - innerT);
        float3 P(float a, float y, float t) => axis == 0 ? JAt(a, y, t) : JAt(t, y, a);
        float3 S(float sa, float sy, float st) => axis == 0 ? new float3(sa, sy, st) : new float3(st, sy, sa);
        var frame = JB(Material.Plain, frameTint);
        var ac = hole.A + hole.W / 2;
        var yc = hole.Y + hole.H / 2;
        var tf = outerT - outward * 0.1f;          // the frame's middle
        const float fw = 0.06f, fd = 0.07f;
        OBox(frame, float4x4.Identity, P(hole.A + fw / 2, yc, tf), S(fw, hole.H, fd));
        OBox(frame, float4x4.Identity, P(hole.A + hole.W - fw / 2, yc, tf), S(fw, hole.H, fd));
        OBox(frame, float4x4.Identity, P(ac, hole.Y + fw / 2, tf), S(hole.W, fw, fd));
        OBox(frame, float4x4.Identity, P(ac, hole.Y + hole.H - fw / 2, tf), S(hole.W, fw, fd));
        if (hole.H > 1.2f && pane != Pane.Barred)
            OBox(frame, float4x4.Identity, P(ac, hole.Y + hole.H * 0.72f, tf), S(hole.W, 0.05f, fd));   // a transom
        // A concrete sill outside.
        if (hole.Y > 0.3f)
            OBox(JB(Material.Plain, new float3(0.55f, 0.55f, 0.53f)), float4x4.Identity, P(ac, hole.Y - 0.03f, outerT + outward * 0.02f), S(hole.W + 0.12f, 0.06f, 0.14f));

        var paneA = hole.A + fw * 0.5f;
        var paneB = hole.A + hole.W - fw * 0.5f;
        void PaneBox(float t, float thick, float3 tint, Material material)
        {
            var t0 = t - thick / 2;
            var t1 = t + thick / 2;
            if (axis == 0)
                JBox(paneA, hole.Y + fw * 0.5f, t0, paneB, hole.Y + hole.H - fw * 0.5f, t1, tint, material, blocksShots: true);
            else
                JBox(t0, hole.Y + fw * 0.5f, paneA, t1, hole.Y + hole.H - fw * 0.5f, paneB, tint, material, blocksShots: true);
        }
        switch (pane)
        {
            case Pane.Dark:
                PaneBox(tf, 0.02f, new float3(0.10f, 0.11f, 0.12f), Material.Glass);
                break;
            case Pane.Lit:
                PaneBox(tf, 0.02f, new float3(0.10f, 0.11f, 0.12f), Material.Glass);
                PaneBox(tf - outward * 0.025f, 0.02f, new float3(0.86f, 0.88f, 0.90f), Material.Window);
                break;
            case Pane.Barred:
            {
                // Steel bars, and a flat across the middle; no glass (you see out into the yard).
                var bars = JB(Material.Steel, new float3(0.30f, 0.32f, 0.34f));
                var tb = (outerT + innerT) / 2;
                for (var a = hole.A + 0.12f; a < hole.A + hole.W - 0.08f; a += 0.13f)
                    OCyl(bars, float4x4.Identity, P(a, hole.Y, tb), P(a, hole.Y + hole.H, tb), 0.016f, 0.016f, 8);
                OBox(bars, float4x4.Identity, P(ac, yc, tb), S(hole.W, 0.05f, 0.012f));
                var gap = axis == 0 ? new float3(hole.W, hole.H, 0.05f) : new float3(0.05f, hole.H, hole.W);
                AddStatic(Aabb.FromCenterSize(P(ac, yc, tb), gap), default, visible: false, blocksShots: false);
                break;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------
    // The wings: grey blockwork with a dark plinth, rows of windows (dark glass; real openings with bars along the
    // corridor), and the rooms you walk through.
    // ------------------------------------------------------------------------------------------------

    private const float StoreyH = 3.2f;

    /// <summary>The windows of a facade: one row per storey, about 3 m apart along a0..a1 (minus <paramref name="skip"/>).</summary>
    private static List<Hole> FacadeWindows(float a0, float a1, int storeys, Func<float, float, bool>? skip = null, float firstStorey = 0)
    {
        var holes = new List<Hole>();
        for (var s = (int)firstStorey; s < storeys; s++)
            foreach (var a in Spread(a0 + 0.6f, a1 - 0.6f, 3.0f))
            {
                var y = s * StoreyH + 1.0f;
                if (skip?.Invoke(a, y) == true)
                    continue;
                holes.Add(Hole.Centered(a, y, 1.0f, 1.45f));
            }
        return holes;
    }

    /// <summary>A facade wall (outer face at <paramref name="outer"/>, thickness inward) with dark windows, and a darker plinth.</summary>
    private void Facade(int axis, float a0, float a1, float outer, float inwardSign, float y0, float y1, List<Hole> holes, Pane pane = Pane.Dark)
    {
        var inner = outer + inwardSign * JT;
        HoledWall(axis, a0, a1, float.Min(outer, inner), float.Max(outer, inner), y0, y1, PrisonGrey, Material.Blockwork, holes);
        foreach (var h in holes)
            WindowFill(axis, h, outer, inner, pane, FrameGrey);
        // The plinth: a dark band round the foot, a few centimetres proud.
        if (y0 <= 0.01f)
        {
            var t0 = outer - inwardSign * 0.03f;
            var segments = new List<(float, float)>();
            var a = a0;
            foreach (var h in holes.Where(h => h.Y < 0.45f).OrderBy(h => h.A))
            {
                segments.Add((a, h.A));
                a = h.A + h.W;
            }
            segments.Add((a, a1));
            foreach (var (s0, s1) in segments.Where(s => s.Item2 - s.Item1 > 0.01f))
            {
                if (axis == 0)
                    JBox(s0, 0, float.Min(t0, outer), s1, 0.45f, float.Max(t0, outer), PrisonPlinth, Material.Concrete, collides: false);
                else
                    JBox(float.Min(t0, outer), 0, s0, float.Max(t0, outer), 0.45f, s1, PrisonPlinth, Material.Concrete, collides: false);
            }
        }
    }

    private void BuildPrisonWings()
    {
        // --- North wing (three storeys): its south face looks over the yard, its main door in the middle. ---
        var northYard = FacadeWindows(-CourtX, CourtX, 3, (a, y) => y < 2 && float.Abs(a) < 2.2f);
        northYard.Add(Hole.Centered(0, 0, 2.0f, 2.5f));
        Facade(0, -CourtX - JT, CourtX + JT, -CourtZ, -1, 0, NorthWingH, northYard);
        BuildMainDoor();
        // Above the east and west wings' roofs.
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var (a0, a1) = side < 0 ? (-WingOutX, -CourtX - JT) : (CourtX + JT, WingOutX);
            Facade(0, a0, a1, -CourtZ, -1, WingH, NorthWingH, FacadeWindows(a0, a1, 3, firstStorey: 2));
        }
        Facade(0, -WingOutX, WingOutX, NorthOutZ, 1, 0, NorthWingH, FacadeWindows(-WingOutX, WingOutX, 3));
        Facade(2, NorthOutZ, -CourtZ, -WingOutX, 1, 0, NorthWingH, FacadeWindows(NorthOutZ, -CourtZ, 3));
        Facade(2, NorthOutZ, -CourtZ, WingOutX, -1, 0, NorthWingH, FacadeWindows(NorthOutZ, -CourtZ, 3));

        // --- South wing: the locker room behind its yard face's east end, the gatehouse against its south face. ---
        var southYardWest = FacadeWindows(-CourtX, LockerX0 - JT, 2);
        Facade(0, -CourtX - JT, LockerX0 - JT, CourtZ, 1, 0, WingH, southYardWest);
        // The locker room's stretch: frosted windows, the wall two layers thick (grey outside, cream inside).
        var lockerWindows = new List<Hole> { Hole.Centered(16.2f, 1.6f, 1.2f, 1.0f), Hole.Centered(19.4f, 1.6f, 1.2f, 1.0f) };
        HoledWall(0, LockerX0 - JT, CourtX + JT, CourtZ, CourtZ + JT / 2, 0, StoreyH, PrisonGrey, Material.Blockwork, lockerWindows);
        HoledWall(0, LockerX0, CourtX + JT, CourtZ + JT / 2, CourtZ + JT, 0, RoomCeiling + 0.2f, PrisonCream, Material.Blockwork, lockerWindows);
        foreach (var h in lockerWindows)
            WindowFill(0, h, CourtZ, CourtZ + JT, Pane.Lit, FrameGrey);
        JBox(LockerX0 - JT, 0, CourtZ - 0.03f, CourtX + JT, 0.45f, CourtZ, PrisonPlinth, Material.Concrete, collides: false);
        Facade(0, LockerX0 - JT, CourtX + JT, CourtZ, 1, StoreyH, WingH, FacadeWindows(LockerX0 - JT, CourtX + JT, 2, firstStorey: 1));
        // South face: windows, except where the gatehouse stands against it (up to its roof).
        var south = FacadeWindows(-WingOutX, WingOutX, 2, (a, y) => a > GateX0 - 0.8f && a < GateX1 + 0.8f && y < GateH + 0.6f);
        Facade(0, -WingOutX, GateX0, SouthOutZ, -1, 0, WingH, [.. south.Where(h => h.A + h.W < GateX0)]);
        Facade(0, GateX1, WingOutX, SouthOutZ, -1, 0, WingH, [.. south.Where(h => h.A > GateX1)]);
        Facade(0, GateX0, GateX1, SouthOutZ, -1, GateH, WingH, [.. FacadeWindows(GateX0, GateX1, 2, firstStorey: 1).Where(h => h.Y > GateH + 0.5f)]);
        // Between the gatehouse and the locker room: the sally door's wall (cream on the locker side, white on the gatehouse's).
        var sally = new List<Hole> { Hole.Centered(EntranceX, 0, 1.1f, 2.2f) };
        HoledWall(0, GateX0, GateX1, SouthOutZ - JT, SouthOutZ - JT / 2, 0, GateH, PrisonCream, Material.Blockwork, sally);
        HoledWall(0, GateX0, GateX1, SouthOutZ - JT / 2, SouthOutZ, 0, GateH, GateWhite, Material.WallPaint, sally);
        Facade(2, CourtZ, SouthOutZ, -WingOutX, 1, 0, WingH, FacadeWindows(CourtZ, SouthOutZ, 2));
        Facade(2, CourtZ, SouthOutZ, WingOutX, -1, 0, WingH, FacadeWindows(CourtZ, SouthOutZ, 2));

        // --- East wing: the cell corridor runs down its yard side, behind barred windows; the yard door near its north end. ---
        var corridorWindows = Spread(-14.6f, 14.6f, 3.6f).Where(z => float.Abs(z - YardDoorZ) > 1.6f)
            .Select(z => Hole.Centered(z, 1.05f, 1.1f, 1.4f)).ToList();
        var yardDoor = Hole.Centered(YardDoorZ, 0, 1.2f, 2.2f);
        List<Hole> eastGround = [.. corridorWindows, yardDoor];
        HoledWall(2, -CourtZ - JT, CourtZ + JT, CourtX, CourtX + JT / 2, 0, CorridorCeiling, PrisonGrey, Material.Blockwork, eastGround);
        HoledWall(2, CorrZ0, CorrZ1, CourtX + JT / 2, CourtX + JT, 0, CorridorCeiling, PrisonCream, Material.Blockwork, eastGround);
        foreach (var h in corridorWindows)
            WindowFill(2, h, CourtX, CourtX + JT, Pane.Barred, PrisonBlue);
        foreach (var (z0, z1) in Gaps(-CourtZ - JT, CourtZ + JT, [yardDoor]))
            JBox(CourtX - 0.03f, 0, z0, CourtX, 0.45f, z1, PrisonPlinth, Material.Concrete, collides: false);
        Facade(2, -CourtZ - JT, CourtZ + JT, CourtX, 1, CorridorCeiling, WingH, FacadeWindows(-CourtZ, CourtZ, 2, firstStorey: 1));
        Facade(2, -CourtZ, CourtZ, WingOutX, -1, 0, WingH, FacadeWindows(-CourtZ, CourtZ, 2));

        // --- West wing. ---
        Facade(2, -CourtZ - JT, CourtZ + JT, -CourtX, -1, 0, WingH, FacadeWindows(-CourtZ, CourtZ, 2,
            (a, y) => y < 2 && float.Abs(a) < 1.6f));
        BuildSideDoor();
        Facade(2, -CourtZ, CourtZ, -WingOutX, 1, 0, WingH, FacadeWindows(-CourtZ, CourtZ, 2));
    }

    /// <summary>The runs of a0..a1 not taken up by the floor-level holes.</summary>
    private static List<(float, float)> Gaps(float a0, float a1, IEnumerable<Hole> holes)
    {
        var list = new List<(float, float)>();
        var a = a0;
        foreach (var h in holes.OrderBy(h => h.A))
        {
            if (h.A > a)
                list.Add((a, h.A));
            a = float.Max(a, h.A + h.W);
        }
        if (a < a1)
            list.Add((a, a1));
        return list;
    }

    /// <summary>The north wing's main door onto the yard: a pair of steel doors under a concrete canopy (shut).</summary>
    private void BuildMainDoor()
    {
        var face = -CourtZ;
        JBox(-1.0f, 0, face - JT + 0.05f, 1.0f, 2.5f, face - JT + 0.1f, PrisonDoorBlue, Material.Plain);
        var b = JB(Material.Plain, FrameGrey);
        OBox(b, float4x4.Identity, JAt(0, 1.25f, face - 0.12f), new float3(0.05f, 2.5f, 0.06f));
        foreach (var x in (ReadOnlySpan<float>)[-0.5f, 0.5f])
        {
            OBox(JB(Material.Glass, new float3(0.08f, 0.09f, 0.1f)), float4x4.Identity, JAt(x, 1.7f, face - JT + 0.11f), new float3(0.18f, 0.6f, 0.01f));
            OBox(JB(Material.Steel, new float3(0.55f, 0.55f, 0.56f)), float4x4.Identity, JAt(x * 0.3f, 1.05f, face - JT + 0.14f), new float3(0.04f, 0.3f, 0.04f));
        }
        JBox(-2.4f, 2.75f, face, 2.4f, 2.95f, face + 1.6f, new float3(0.62f, 0.61f, 0.58f), Material.Concrete);   // canopy
        AddLettering("WING A", JAt(0, 3.4f, face + 0.01f), new float3(0, 0, 1), 0.28f, Material.Plain, new float3(0.12f, 0.14f, 0.16f));
        GroundRect(JDecal(Material.Floor, new float3(0.5f, 0.5f, 0.48f)), -1.6f, face, 1.6f, face + 1.8f, 0.006f);
    }

    /// <summary>A steel door in the west wing's yard face (shut). It stands proud of the wall and its plinth: a face on the wall's own plane would flicker.</summary>
    private void BuildSideDoor()
    {
        var x = -CourtX;
        JBox(x, 0, -0.6f, x + 0.05f, 2.2f, 0.6f, PrisonDoorBlue, Material.Plain);
        var frame = JB(Material.Plain, FrameGrey);
        foreach (var z in (ReadOnlySpan<float>)[-0.64f, 0.64f])
            OBox(frame, float4x4.Identity, JAt(x + 0.035f, 1.12f, z), new float3(0.07f, 2.24f, 0.08f));
        OBox(frame, float4x4.Identity, JAt(x + 0.035f, 2.24f, 0), new float3(0.07f, 0.08f, 1.36f));
        OBox(JB(Material.Plain, new float3(0.55f, 0.55f, 0.56f)), float4x4.Identity, JAt(x + 0.08f, 1.05f, 0.42f), new float3(0.04f, 0.04f, 0.2f));
        JBox(x, 2.4f, -1.0f, x + 0.9f, 2.55f, 1.0f, new float3(0.62f, 0.61f, 0.58f), Material.Concrete);
    }

    // ------------------------------------------------------------------------------------------------
    // Roofs: dark slate hip roofs on the north and south wings, pitched roofs on the east and west wings running
    // in under them.
    // ------------------------------------------------------------------------------------------------

    private void BuildPrisonRoofs(Random random)
    {
        HipRoof(-WingOutX, WingOutX, NorthOutZ, -CourtZ, NorthWingH);
        HipRoof(-WingOutX, WingOutX, CourtZ, SouthOutZ, WingH);
        var ridgeSouth = (CourtZ + SouthOutZ) / 2;
        var ridgeNorth = (NorthOutZ - CourtZ) / 2;
        PitchedRoofAlongZ(CourtX, WingOutX, ridgeNorth, ridgeSouth, WingH, -CourtZ, CourtZ);
        PitchedRoofAlongZ(-WingOutX, -CourtX, ridgeNorth, ridgeSouth, WingH, -CourtZ, CourtZ);

        // Small white vent housings and flues along the ridges (as on the photo), a few satellite boxes.
        var vent = JB(Material.Plain, new float3(0.86f, 0.86f, 0.84f));
        var dark = JB(Material.Steel, new float3(0.25f, 0.25f, 0.26f));
        foreach (var x in (ReadOnlySpan<float>)[-20f, -6f, 8f, 21f])
        {
            var top = NorthWingH + 6f * RoofPitch;
            OBox(vent, float4x4.Identity, JAt(x, top + 0.2f, -22f), new float3(1.4f, 0.9f, 1.2f));
            OBox(dark, float4x4.Identity, JAt(x, top + 0.68f, -22f), new float3(1.5f, 0.06f, 1.3f));
        }
        foreach (var x in (ReadOnlySpan<float>)[-24f, -10f, 4f])
        {
            var top = WingH + 6f * RoofPitch;
            OBox(vent, float4x4.Identity, JAt(x, top + 0.1f, 22f), new float3(1.0f, 0.7f, 1.0f));
        }
        foreach (var z in (ReadOnlySpan<float>)[-9f, 6f])
            foreach (var x in (ReadOnlySpan<float>)[-28f, 28f])
                OCyl(dark, float4x4.Identity, JAt(x, WingH + 6f * RoofPitch - 0.4f, z), JAt(x, WingH + 6f * RoofPitch + 0.9f, z), 0.16f, 0.16f, 10);
        _ = random;
    }

    /// <summary>A hip roof over the rectangle x0..x1, z0..z1 (the walls' outer faces) with its eaves at <paramref name="eave"/>.</summary>
    private void HipRoof(float x0, float x1, float z0, float z1, float eave)
    {
        var b = JB(Material.RoofSlate, SlateTint);
        var ex0 = x0 - Eaves;
        var ex1 = x1 + Eaves;
        var ez0 = z0 - Eaves;
        var ez1 = z1 + Eaves;
        var ey = eave - Eaves * RoofPitch;
        var alongX = x1 - x0 >= z1 - z0;
        var half = (alongX ? z1 - z0 : x1 - x0) / 2;
        var ridgeY = eave + half * RoofPitch;
        float3 E00 = JAt(ex0, ey, ez0), E10 = JAt(ex1, ey, ez0), E11 = JAt(ex1, ey, ez1), E01 = JAt(ex0, ey, ez1);
        if (alongX)
        {
            var zc = (z0 + z1) / 2;
            var ra = JAt(x0 + half, ridgeY, zc);
            var rb = JAt(x1 - half, ridgeY, zc);
            RoofQuad(b, E00, E10, rb, ra);
            RoofQuad(b, E01, E11, rb, ra);
            RoofTri(b, E00, E01, ra);
            RoofTri(b, E10, E11, rb);
        }
        else
        {
            var xc = (x0 + x1) / 2;
            var ra = JAt(xc, ridgeY, z0 + half);
            var rb = JAt(xc, ridgeY, z1 - half);
            RoofQuad(b, E00, E01, rb, ra);
            RoofQuad(b, E10, E11, rb, ra);
            RoofTri(b, E00, E10, ra);
            RoofTri(b, E01, E11, rb);
        }
        // Under the eaves, and the fascia and gutter round them.
        JB(Material.Plain, new float3(0.70f, 0.70f, 0.68f)).Quad(E00, E10, E11, E01, new float3(0, -1, 0));
        Fascia(ex0, ex1, ez0, ez0, ey);
        Fascia(ex0, ex1, ez1, ez1, ey);
        Fascia(ex0, ex0, ez0, ez1, ey);
        Fascia(ex1, ex1, ez0, ez1, ey);
    }

    /// <summary>
    /// A plain pitched roof over x0..x1 running along z from za to zb (its ends hidden under the hip roofs it runs into),
    /// with its eaves and fascias only between f0 and f1 (where it's out in the open).
    /// </summary>
    private void PitchedRoofAlongZ(float x0, float x1, float za, float zb, float eave, float f0, float f1)
    {
        var b = JB(Material.RoofSlate, SlateTint);
        var ex0 = x0 - Eaves;
        var ex1 = x1 + Eaves;
        var ey = eave - Eaves * RoofPitch;
        var xc = (x0 + x1) / 2;
        var ridgeY = eave + (x1 - x0) / 2 * RoofPitch;
        RoofQuad(b, JAt(ex0, ey, za), JAt(ex0, ey, zb), JAt(xc, ridgeY, zb), JAt(xc, ridgeY, za));
        RoofQuad(b, JAt(ex1, ey, za), JAt(ex1, ey, zb), JAt(xc, ridgeY, zb), JAt(xc, ridgeY, za));
        var soffit = JB(Material.Plain, new float3(0.70f, 0.70f, 0.68f));
        soffit.Quad(JAt(ex0, ey, f0), JAt(x0, ey, f0), JAt(x0, ey, f1), JAt(ex0, ey, f1), new float3(0, -1, 0));
        soffit.Quad(JAt(x1, ey, f0), JAt(ex1, ey, f0), JAt(ex1, ey, f1), JAt(x1, ey, f1), new float3(0, -1, 0));
        Fascia(ex0, ex0, f0, f1, ey);
        Fascia(ex1, ex1, f0, f1, ey);
    }

    private static void RoofQuad(MeshBuilder b, float3 a, float3 c, float3 d, float3 e)
    {
        var n = float3.Normalize(float3.Cross(c - a, e - a));
        if (n.Y < 0)
            n = -n;
        b.Quad(a, c, d, e, n);
        b.Quad(a, c, d, e, -n);   // (the underside: seen from the rooms' windows' level it's never in view, but keeps the slope closed)
    }

    private static void RoofTri(MeshBuilder b, float3 a, float3 c, float3 d)
    {
        var n = float3.Normalize(float3.Cross(c - a, d - a));
        if (n.Y < 0)
            n = -n;
        b.Triangle(a, c, d, n);
        b.Triangle(a, c, d, -n);
    }

    /// <summary>A fascia board with a gutter along one eave edge (a line in plan from (x0, z0) to (x1, z1)).</summary>
    private void Fascia(float x0, float x1, float z0, float z1, float ey)
    {
        var fascia = JB(Material.Plain, new float3(0.18f, 0.19f, 0.2f));
        var alongX = float.Abs(x1 - x0) > float.Abs(z1 - z0);
        var length = alongX ? float.Abs(x1 - x0) : float.Abs(z1 - z0);
        var c = JAt((x0 + x1) / 2, ey - 0.02f, (z0 + z1) / 2);
        OBox(fascia, float4x4.Identity, c, alongX ? new float3(length, 0.24f, 0.04f) : new float3(0.04f, 0.24f, length));
        OBox(fascia, float4x4.Identity, c + new float3(0, 0.06f, 0), alongX ? new float3(length, 0.12f, 0.16f) : new float3(0.16f, 0.12f, length));
    }

    // ------------------------------------------------------------------------------------------------
    // The gatehouse: the staff entrance, a control booth behind glass, a metal detector arch and an x-ray belt, and
    // the sally door through to the locker room.
    // ------------------------------------------------------------------------------------------------

    private void BuildGatehouse(Random random)
    {
        const float x0 = GateX0, x1 = GateX1, z1 = GateZ1;
        const float z0 = SouthOutZ;
        // Walls: white render outside and in (the photo's white block). The entrance in the south wall between two
        // lit windows; small high windows on the sides.
        var front = new List<Hole> { Hole.Centered(EntranceX, 0, 1.8f, 2.4f), Hole.Centered(16.6f, 0.9f, 2.0f, 1.5f), Hole.Centered(25.4f, 0.9f, 2.0f, 1.5f) };
        HoledWall(0, x0, x1, z1 - JT, z1, 0, GateH, GateWhite, Material.WallPaint, front);
        foreach (var h in front.Skip(1))
            WindowFill(0, h, z1, z1 - JT, Pane.Lit, FrameGrey);
        var side = new List<Hole> { Hole.Centered(32.0f, 1.7f, 1.6f, 0.7f) };
        HoledWall(2, z0, z1 - JT, x0, x0 + JT, 0, GateH, GateWhite, Material.WallPaint, side);
        HoledWall(2, z0, z1 - JT, x1 - JT, x1, 0, GateH, GateWhite, Material.WallPaint, side);
        WindowFill(2, side[0], x0, x0 + JT, Pane.Lit, FrameGrey);
        WindowFill(2, side[0], x1, x1 - JT, Pane.Lit, FrameGrey);
        JBox(x0 - 0.03f, 0, z0, x0, 0.4f, z1 + 0.03f, PrisonPlinth, Material.Concrete, collides: false);
        JBox(x1, 0, z0, x1 + 0.03f, 0.4f, z1 + 0.03f, PrisonPlinth, Material.Concrete, collides: false);
        foreach (var (a, b) in Gaps(x0, x1, [front[0]]))
            JBox(a, 0, z1, b, 0.4f, z1 + 0.03f, PrisonPlinth, Material.Concrete, collides: false);

        // Flat roof with a parapet and a glazed lantern (the glass box on the photo's white block), a canopy over the door.
        JBox(x0, RoomCeiling + 0.6f, z0, x1, GateH, z1, GateWhite, Material.WallPaint);
        JBox(x0 - 0.05f, GateH, z0 - 0.05f, x1 + 0.05f, GateH + 0.12f, z1 + 0.05f, new float3(0.25f, 0.25f, 0.26f), Material.Plain);
        foreach (var (ax0, az0, ax1, az1) in (ReadOnlySpan<(float, float, float, float)>)[(x0 - 0.05f, z1 - 0.25f, x1 + 0.05f, z1 + 0.05f), (x0 - 0.05f, z0, x0 + 0.2f, z1), (x1 - 0.2f, z0, x1 + 0.05f, z1)])
            JBox(ax0, GateH, az0, ax1, GateH + 0.55f, az1, GateWhite, Material.WallPaint);
        var lantern = JB(Material.Glass, new float3(0.16f, 0.2f, 0.24f));
        var mullion = JB(Material.Plain, new float3(0.22f, 0.23f, 0.25f));
        OBox(lantern, float4x4.Identity, JAt(21, GateH + 0.55f, 32), new float3(7.5f, 0.9f, 4.5f));
        OBox(mullion, float4x4.Identity, JAt(21, GateH + 1.02f, 32), new float3(7.7f, 0.06f, 4.7f));
        for (var x = 17.25f; x <= 24.8f; x += 1.25f)
            foreach (var z in (ReadOnlySpan<float>)[29.74f, 34.26f])
                OBox(mullion, float4x4.Identity, JAt(x, GateH + 0.55f, z), new float3(0.05f, 0.9f, 0.04f));
        JBox(19.3f, 2.75f, z1, 22.7f, 2.9f, z1 + 1.8f, new float3(0.28f, 0.29f, 0.31f), Material.PaintedMetal);
        AddLettering("STAFF ENTRANCE", JAt(EntranceX, 3.35f, z1 + 0.01f), new float3(0, 0, 1), 0.26f, Material.Plain, new float3(0.14f, 0.16f, 0.2f));
        AddLettering("AUTHORISED PERSONNEL ONLY", JAt(25.4f, 2.62f, z1 + 0.01f), new float3(0, 0, 1), 0.09f, Material.Plain, new float3(0.6f, 0.08f, 0.06f));
        // A card reader and an intercom by the door, a camera over it.
        var grey = JB(Material.Plain, new float3(0.18f, 0.18f, 0.19f));
        OBox(grey, float4x4.Identity, JAt(22.35f, 1.3f, z1 + 0.04f), new float3(0.1f, 0.16f, 0.06f));
        OBox(JB(Material.Emissive, new float3(0.1f, 0.9f, 0.25f)), float4x4.Identity, JAt(22.35f, 1.36f, z1 + 0.072f), new float3(0.03f, 0.012f, 0.004f));
        OBox(grey, float4x4.Identity, JAt(22.6f, 1.5f, z1 + 0.035f), new float3(0.14f, 0.24f, 0.05f));
        AddCamera(JAt(23.2f, 2.7f, z1 + 0.05f), 0.35f);

        // Inside: vinyl floor, a suspended ceiling, the booth, the detector arch and the belt.
        JBox(x0 + JT, -0.3f, z0, x1 - JT, 0, z1 - JT, VinylGrey, Material.WallPaint);
        JBox(EntranceX - 0.9f, -0.3f, z1 - JT, EntranceX + 0.9f, 0, z1, new float3(0.55f, 0.56f, 0.57f), Material.Steel);   // threshold, flush
        JBox(EntranceX - 0.55f, -0.3f, z0 - JT, EntranceX + 0.55f, 0, z0, new float3(0.55f, 0.56f, 0.57f), Material.Steel);
        JBox(x0 + JT, RoomCeiling, z0, x1 - JT, RoomCeiling + 0.1f, z1 - JT, new float3(0.86f, 0.85f, 0.81f), Material.CeilingTile);
        CeilingPanels(0);
        // An entrance mat.
        GroundRect(JDecal(Material.Carpet, new float3(0.12f, 0.12f, 0.13f)), EntranceX - 1.1f, z1 - JT - 2.0f, EntranceX + 1.1f, z1 - JT - 0.1f, 0.003f);

        // The control booth (west): a solid wall to counter height, then glass to the ceiling, with a pass-through tray.
        const float bx = 18.4f, bz0 = 29.4f, bz1 = 34.4f;
        JBox(x0 + JT, 0, bz0 - 0.12f, bx + 0.1f, RoomCeiling, bz0, GateWhite, Material.WallPaint);
        JBox(x0 + JT, 0, bz1, bx + 0.1f, RoomCeiling, bz1 + 0.12f, GateWhite, Material.WallPaint);
        JBox(bx - 0.1f, 0, bz0, bx + 0.1f, 1.05f, bz1, GateWhite, Material.WallPaint);
        JBox(bx - 0.1f, 2.45f, bz0, bx + 0.1f, RoomCeiling, bz1, GateWhite, Material.WallPaint);
        // (Open above the counter: a speaking window, so you can see who's on duty.)
        AddStatic(new Aabb(JAt(bx - 0.015f, 1.05f, bz0), JAt(bx + 0.015f, 2.45f, bz1)), default, visible: false, blocksShots: false);
        var frameB = JB(Material.Plain, FrameGrey);
        for (var z = bz0; z <= bz1 + 0.01f; z += (bz1 - bz0) / 3)
            OBox(frameB, float4x4.Identity, JAt(bx, 1.75f, z), new float3(0.06f, 1.4f, 0.06f));
        OBox(frameB, float4x4.Identity, JAt(bx, 1.07f, (bz0 + bz1) / 2), new float3(0.08f, 0.04f, bz1 - bz0));
        OBox(JB(Material.Steel, new float3(0.6f, 0.6f, 0.62f)), float4x4.Identity, JAt(bx + 0.12f, 1.07f, 31.9f), new float3(0.3f, 0.03f, 0.5f));   // the tray
        OBox(JB(Material.Veneer, new float3(0.58f, 0.5f, 0.4f)), float4x4.Identity, JAt(bx + 0.2f, 1.06f, 31.9f), new float3(0.32f, 0.04f, 4.9f));   // counter ledge
        AddLettering("CONTROL", JAt(bx + 0.11f, 2.7f, 31.9f), new float3(1, 0, 0), 0.16f, Material.Plain, new float3(0.15f, 0.17f, 0.22f));
        // Inside the booth: a desk along the glass with monitors, a key cabinet, a chair.
        var desk = JB(Material.Veneer, new float3(0.62f, 0.55f, 0.45f));
        OBox(desk, float4x4.Identity, JAt(17.6f, 0.74f, 31.9f), new float3(1.2f, 0.04f, 4.6f));
        AddStatic(new Aabb(JAt(17.0f, 0, 29.5f), JAt(18.25f, 0.76f, 34.3f)), default, visible: false);
        var black = JB(Material.Plain, new float3(0.04f, 0.04f, 0.045f));
        foreach (var z in (ReadOnlySpan<float>)[30.4f, 31.3f, 32.5f, 33.4f])
        {
            OBox(black, float4x4.Identity, JAt(17.85f, 1.1f, z), new float3(0.05f, 0.36f, 0.6f));
            OBox(JB(Material.Emissive, new float3(0.22f, 0.3f, 0.36f)), float4x4.Identity, JAt(17.82f, 1.1f, z), new float3(0.004f, 0.32f, 0.56f));
            OBox(black, float4x4.Identity, JAt(17.95f, 0.85f, z), new float3(0.1f, 0.2f, 0.06f));
        }
        OBox(JB(Material.Steel, new float3(0.6f, 0.6f, 0.58f)), float4x4.Identity, JAt(x0 + JT + 0.06f, 1.6f, 33.6f), new float3(0.1f, 0.7f, 0.5f));
        AddOfficeChair(Frame(JAt(16.6f, 0, 31.9f), -float.Pi / 2));

        // The walk-through metal detector (you pass through it on x = 21) and the x-ray belt beside it.
        var arch = JB(Material.Plain, new float3(0.72f, 0.73f, 0.74f));
        const float az = 31.8f;
        foreach (var x in (ReadOnlySpan<float>)[EntranceX - 0.5f, EntranceX + 0.5f])
        {
            OBox(arch, float4x4.Identity, JAt(x, 1.1f, az), new float3(0.12f, 2.2f, 0.62f));
            AddStatic(new Aabb(JAt(x - 0.06f, 0, az - 0.31f), JAt(x + 0.06f, 2.2f, az + 0.31f)), default, visible: false);
        }
        OBox(arch, float4x4.Identity, JAt(EntranceX, 2.26f, az), new float3(1.12f, 0.14f, 0.64f));
        OBox(JB(Material.Plain, new float3(0.15f, 0.16f, 0.18f)), float4x4.Identity, JAt(EntranceX, 2.26f, az + 0.325f), new float3(0.5f, 0.08f, 0.01f));
        OBox(JB(Material.Emissive, new float3(0.15f, 1.0f, 0.3f)), float4x4.Identity, JAt(EntranceX + 0.16f, 2.26f, az + 0.332f), new float3(0.06f, 0.03f, 0.004f));
        var belt = JB(Material.Plain, new float3(0.62f, 0.63f, 0.64f));
        OBox(belt, float4x4.Identity, JAt(24.2f, 0.45f, az), new float3(3.4f, 0.9f, 0.9f));
        OBox(belt, float4x4.Identity, JAt(24.2f, 1.25f, az), new float3(1.5f, 0.7f, 1.0f));
        OBox(JB(Material.Plain, new float3(0.05f, 0.05f, 0.05f)), float4x4.Identity, JAt(24.2f, 0.905f, az), new float3(3.3f, 0.01f, 0.7f));
        foreach (var x in (ReadOnlySpan<float>)[23.5f, 24.9f])
            OBox(JB(Material.Plain, new float3(0.08f, 0.08f, 0.08f)), float4x4.Identity, JAt(x, 1.05f, az), new float3(0.02f, 0.38f, 0.82f));   // lead curtains
        AddStatic(new Aabb(JAt(22.5f, 0, az - 0.5f), JAt(25.9f, 1.6f, az + 0.5f)), default, visible: false);
        // Plastic trays at the belt's end, a bin, a clock and a notice on the wall.
        OBox(JB(Material.Plain, new float3(0.2f, 0.22f, 0.26f)), float4x4.Identity, JAt(25.6f, 0.95f, az), new float3(0.55f, 0.1f, 0.4f));
        OCyl(JB(Material.Plain, new float3(0.18f, 0.18f, 0.2f)), float4x4.Identity, JAt(26.9f, 0, 29.0f), JAt(26.9f, 0.6f, 29.0f), 0.18f, 0.2f, 14);
        var clock = Frame(JAt(x1 - JT, 2.35f, 32.0f), 0);
        OCyl(JB(Material.Plain, new float3(0.9f, 0.9f, 0.88f)), float4x4.Identity, JAt(x1 - JT - 0.01f, 2.35f, 32f), JAt(x1 - JT - 0.05f, 2.35f, 32f), 0.16f, 0.16f, 20);
        _ = clock;
        OBox(JB(Material.Plain, new float3(0.06f, 0.06f, 0.06f)), float4x4.Identity, JAt(x1 - JT - 0.055f, 2.39f, 32f), new float3(0.004f, 0.1f, 0.012f));
        OBox(JB(Material.Plain, new float3(0.06f, 0.06f, 0.06f)), float4x4.Identity, JAt(x1 - JT - 0.055f, 2.35f, 32.05f), new float3(0.004f, 0.012f, 0.12f));
        AddLettering("ALL STAFF MUST BE SCREENED", JAt(x1 - JT - 0.01f, 1.75f, 30.0f), new float3(-1, 0, 0), 0.07f, Material.Plain, new float3(0.15f, 0.15f, 0.18f));
        AddCamera(JAt(x1 - JT - 0.05f, 2.85f, 34.8f), 0.3f);
        // A bench by the east wall.
        BenchAlongZ(26.95f, 33.6f, 1.6f);
        _ = random;
    }

    /// <summary>A small CCTV camera on a bracket.</summary>
    private void AddCamera(float3 at, float drop)
    {
        var b = JB(Material.Plain, new float3(0.88f, 0.88f, 0.86f));
        OBox(b, float4x4.Identity, at + new float3(0, -drop / 2, 0), new float3(0.04f, drop, 0.04f));
        OBox(b, float4x4.Identity, at + new float3(0, -drop - 0.05f, 0.08f), new float3(0.1f, 0.1f, 0.28f));
        OBox(JB(Material.Glass, new float3(0.05f, 0.05f, 0.06f)), float4x4.Identity, at + new float3(0, -drop - 0.05f, 0.225f), new float3(0.07f, 0.07f, 0.01f));
    }

    /// <summary>A slatted wooden bench with steel legs along z, centred at (x, z).</summary>
    private void BenchAlongZ(float x, float z, float length)
    {
        var wood = JB(Material.Veneer, new float3(0.66f, 0.54f, 0.38f));
        var steel = JB(Material.Steel, new float3(0.32f, 0.33f, 0.35f));
        for (var k = -1; k <= 1; k++)
            OBox(wood, float4x4.Identity, JAt(x + k * 0.11f, 0.45f, z), new float3(0.09f, 0.035f, length));
        foreach (var dz in (ReadOnlySpan<float>)[-length / 2 + 0.15f, length / 2 - 0.15f])
        {
            OBox(steel, float4x4.Identity, JAt(x, 0.215f, z + dz), new float3(0.3f, 0.43f, 0.04f));
        }
        AddStatic(new Aabb(JAt(x - 0.18f, 0, z - length / 2), JAt(x + 0.18f, 0.47f, z + length / 2)), default, visible: false);
    }

    /// <summary>The ceiling fittings of one room (see <see cref="PrisonFittings"/>): flat LED panels.</summary>
    private void CeilingPanels(int room)
    {
        var trim = JB(Material.Plain, new float3(0.9f, 0.9f, 0.9f));
        var diffuser = JB(Material.Emissive, new float3(0.97f, 0.98f, 1.0f));
        foreach (var (x, z, r) in PrisonFittings)
        {
            if (r != room)
                continue;
            var y = FittingHeight(r);
            var size = room == 2 ? new float2(0.32f, 1.25f) : new float2(0.6f, 0.6f);
            OBox(trim, float4x4.Identity, JAt(x, y - 0.02f, z), new float3(size.X + 0.04f, 0.04f, size.Y + 0.04f));
            OBox(diffuser, float4x4.Identity, JAt(x, y - 0.042f, z), new float3(size.X, 0.004f, size.Y));
        }
    }

    // ------------------------------------------------------------------------------------------------
    // The locker room: banks of grey steel lockers with mirrored ends and benches (the reference photo), the
    // player's own locker on the east bank.
    // ------------------------------------------------------------------------------------------------

    /// <summary>The player's locker: on the east bank, its door facing west.</summary>
    public const float PlayerLockerZ = 22.2f;
    private const float EastBankBack = LockerX1 - 0.01f;   // the east bank's back, against the wall

    private void BuildLockerRoom(Random random)
    {
        // Shell: floor, ceiling, the east and west walls (cream blockwork), the stretch of north wall east of the grille.
        JBox(LockerX0, -0.3f, LockerZ0, LockerX1, 0, LockerZ1, VinylGrey * 0.95f, Material.WallPaint);
        JBox(LockerX0, RoomCeiling, LockerZ0, LockerX1, RoomCeiling + 0.1f, LockerZ1, new float3(0.86f, 0.85f, 0.81f), Material.CeilingTile);
        JBox(LockerX1, 0, LockerZ0 - JT, LockerX1 + JT, RoomCeiling + 0.2f, LockerZ1, PrisonCream, Material.Blockwork);
        JBox(LockerX0 - JT, 0, LockerZ0, LockerX0, RoomCeiling + 0.2f, LockerZ1, PrisonCream, Material.Blockwork);
        JBox(CorrX1, 0, CourtZ, LockerX1, RoomCeiling + 0.2f, LockerZ0, PrisonCream, Material.Blockwork);
        // Over the grille: a bulkhead down to the gate's head, up to the corridor's ceiling.
        JBox(CorrX0, 2.4f, CourtZ, CorrX1, CorridorCeiling + 0.2f, LockerZ0, PrisonCream, Material.Blockwork);
        CeilingPanels(1);
        // A skirting of dark vinyl, a floor mat between the island and the east bank (as on the photo).
        var skirt = JB(Material.Plain, new float3(0.2f, 0.2f, 0.21f));
        OBox(skirt, float4x4.Identity, JAt(LockerX0 + 0.01f, 0.05f, (LockerZ0 + LockerZ1) / 2), new float3(0.02f, 0.1f, LockerZ1 - LockerZ0));
        GroundRect(JDecal(Material.Carpet, new float3(0.11f, 0.11f, 0.12f)), 22.6f, 20.4f, 24.1f, 24.0f, 0.003f);

        // The banks: along the west wall, back to back down the middle, along the east wall (the player's).
        LockerBank(LockerX0 + 0.01f, 18.2f, 26.6f, facing: 1f, random);
        LockerBank(20.05f, 19.4f, 25.0f, facing: -1f, random);
        LockerBank(20.05f, 19.4f, 25.0f, facing: 1f, random);
        LockerBank(EastBankBack, 18.4f, 26.4f, facing: -1f, random, skipZ: PlayerLockerZ);
        // Benches: between the west bank and the island, and between the island and the east bank.
        BenchAlongZ(17.6f, 22.2f, 3.6f);
        BenchAlongZ(21.6f, 20.8f, 2.4f);
        // Mirrors on the island's ends (as on the photo), a clock, a sign.
        foreach (var z in (ReadOnlySpan<float>)[19.4f - 0.015f, 25.0f + 0.015f])
        {
            OBox(JB(Material.Plain, new float3(0.6f, 0.6f, 0.6f)), float4x4.Identity, JAt(20.05f, 1.45f, z), new float3(0.62f, 1.1f, 0.02f));
            OBox(JB(Material.Glass, new float3(0.62f, 0.66f, 0.68f)), float4x4.Identity, JAt(20.05f, 1.45f, z + float.Sign(z - 22f) * 0.012f), new float3(0.56f, 1.04f, 0.006f));
        }
        AddLettering("STAFF LOCKERS", JAt(LockerX1 - 0.01f, 2.6f, 18.2f), new float3(-1, 0, 0), 0.12f, Material.Plain, new float3(0.15f, 0.17f, 0.22f));
        AddLettering("NO PHONES BEYOND THIS POINT", JAt(CorrX1 + 0.45f, 2.15f, LockerZ0 + 0.01f), new float3(0, 0, 1), 0.06f, Material.Plain, new float3(0.6f, 0.08f, 0.06f));
        // The player's locker itself (its door is one of the moving doors; see BuildPrisonDoors).
        BuildPlayerLocker();
    }

    /// <summary>
    /// A bank of two-tier steel lockers along z (from z0 to z1) with its back at x, doors facing +x (facing 1) or -x
    /// (-1): louvred doors, handles with padlock hasps, a number on each, a wooden plinth step.
    /// </summary>
    private void LockerBank(float x, float z0, float z1, float facing, Random random, float skipZ = float.NaN)
    {
        const float depth = 0.5f, width = 0.4f, height = 1.9f;
        var body = JB(Material.Plain, LockerSteel);
        var door = JB(Material.Plain, LockerSteel * 1.08f);
        var dark = JB(Material.Plain, new float3(0.1f, 0.1f, 0.11f));
        var chrome = JB(Material.Steel, new float3(0.62f, 0.63f, 0.64f));
        var red = JB(Material.Plain, new float3(0.55f, 0.08f, 0.06f));
        var front = x + facing * depth;
        var count = (int)float.Floor((z1 - z0) / width);
        var start = (z0 + z1) / 2 - count * width / 2;
        // Carcass (round the player's open locker, if it's in this bank), top and plinth.
        var cuts = new List<(float, float)>();
        var bankZ0 = (z0 + z1) / 2 - count * width / 2;
        var bankZ1 = bankZ0 + count * width;
        if (float.IsNaN(skipZ))
            cuts.Add((bankZ0, bankZ1));
        else
        {
            cuts.Add((bankZ0, skipZ - width / 2));
            cuts.Add((skipZ + width / 2, bankZ1));
            // The open locker's back, floor and roof.
            OBox(body, float4x4.Identity, JAt(x + facing * 0.01f, 0.15f + height / 2, skipZ), new float3(0.02f, height, width));
            OBox(body, float4x4.Identity, JAt(x + facing * depth / 2, 0.16f, skipZ), new float3(depth, 0.02f, width));
            OBox(body, float4x4.Identity, JAt(x + facing * depth / 2, 0.14f + height, skipZ), new float3(depth, 0.02f, width));
        }
        foreach (var (c0, c1) in cuts.Where(c => c.Item2 - c.Item1 > 0.01f))
            OBox(body, float4x4.Identity, JAt(x + facing * depth / 2, 0.15f + height / 2, (c0 + c1) / 2), new float3(depth, height, c1 - c0));
        OBox(JB(Material.Veneer, new float3(0.6f, 0.5f, 0.37f)), float4x4.Identity, JAt(x + facing * (depth / 2 + 0.03f), 0.075f, (z0 + z1) / 2), new float3(depth + 0.06f, 0.15f, count * width + 0.02f));
        AddStatic(BoxBetween(JAt(x, 0, start), JAt(front + facing * 0.06f, 0.15f + height, start + count * width)), default, visible: false);
        for (var i = 0; i < count; i++)
        {
            var zc = start + (i + 0.5f) * width;
            if (!float.IsNaN(skipZ) && float.Abs(zc - skipZ) < width / 2)
            {
                continue;   // the player's locker: open inside, its door is a moving part (see BuildPrisonDoors)
            }
            foreach (var tier in (ReadOnlySpan<int>)[0, 1])
            {
                var y0 = 0.15f + tier * height / 2;
                var yc = y0 + height / 4;
                OBox(door, float4x4.Identity, JAt(front + facing * 0.008f, yc, zc), new float3(0.016f, height / 2 - 0.02f, width - 0.02f));
                // Louvres, a handle with its hasp (some with red padlocks), a number plate.
                for (var k = 0; k < 3; k++)
                    OBox(dark, float4x4.Identity, JAt(front + facing * 0.017f, y0 + 0.12f + k * 0.035f, zc), new float3(0.004f, 0.012f, 0.16f));
                for (var k = 0; k < 3; k++)
                    OBox(dark, float4x4.Identity, JAt(front + facing * 0.017f, y0 + height / 2 - 0.2f + k * 0.035f, zc), new float3(0.004f, 0.012f, 0.16f));
                var hz = zc + facing * 0.14f;
                OBox(chrome, float4x4.Identity, JAt(front + facing * 0.03f, yc + 0.1f, hz), new float3(0.03f, 0.12f, 0.045f));
                if (random.NextDouble() < 0.3)
                    OBox(red, float4x4.Identity, JAt(front + facing * 0.05f, yc + 0.03f, hz), new float3(0.02f, 0.045f, 0.035f));
                OBox(JB(Material.Plain, new float3(0.85f, 0.85f, 0.82f)), float4x4.Identity, JAt(front + facing * 0.018f, y0 + height / 2 - 0.08f, zc), new float3(0.003f, 0.035f, 0.07f));
            }
        }
        _ = skipZ;
    }

    /// <summary>The open space inside the player's locker: a shelf, a hanging rail, a jacket on a hanger.</summary>
    private void BuildPlayerLocker()
    {
        var x = EastBankBack;
        var z = PlayerLockerZ;
        var front = x - 0.5f;
        var steel = JB(Material.Plain, LockerSteel * 0.85f);
        OBox(steel, float4x4.Identity, JAt(front + 0.25f, 1.55f, z), new float3(0.46f, 0.02f, 0.36f));          // shelf
        foreach (var dz in (ReadOnlySpan<float>)[-0.195f, 0.195f])
            OBox(steel, float4x4.Identity, JAt(front + 0.25f, 0.15f + 0.95f, z + dz), new float3(0.5f, 1.9f, 0.01f));   // its sides
        OCyl(JB(Material.Steel, new float3(0.6f, 0.6f, 0.62f)), float4x4.Identity, JAt(front + 0.25f, 1.45f, z - 0.17f), JAt(front + 0.25f, 1.45f, z + 0.17f), 0.008f);
        var jacket = JB(Material.Cloth, new float3(0.08f, 0.1f, 0.16f));
        OBox(jacket, float4x4.Identity, JAt(front + 0.25f, 1.08f, z), new float3(0.1f, 0.72f, 0.34f));
        OBox(JB(Material.Cloth, new float3(0.75f, 0.75f, 0.72f)), float4x4.Identity, JAt(front + 0.2f, 1.6f, z + 0.05f), new float3(0.22f, 0.07f, 0.25f));   // folded shirt
    }

    // ------------------------------------------------------------------------------------------------
    // The cell corridor (the third photo): cream blockwork, blue-grey pilasters and doors, ducts and pipes under a
    // bare concrete ceiling, barred windows on the yard side. One cell stands open.
    // ------------------------------------------------------------------------------------------------

    private static float[] CellDoorZs => [12.8f, 9.2f, 5.6f, OpenCellZ, -1.6f, -5.2f, -8.8f, -12.4f];

    private void BuildCellCorridor(Random random)
    {
        // Floor (sealed concrete, its joints sawn every 5 m), ceiling slab, the end wall, the cells' wall.
        JBox(CorrX0, -0.3f, CorrZ0, CorrX1, 0, LockerZ0, new float3(0.62f, 0.61f, 0.58f), Material.Floor);
        JBox(CorrX0, CorridorCeiling, CorrZ0, CellWallX, CorridorCeiling + 0.25f, LockerZ0, new float3(0.66f, 0.65f, 0.62f), Material.Concrete);
        JBox(CorrX0, 0, CorrZ0 - JT, CellWallX, CorridorCeiling + 0.2f, CorrZ0, PrisonCream, Material.Blockwork);
        var cellWall = new List<Hole> { Hole.Centered(OpenCellZ, 0, 1.0f, 2.1f) };
        HoledWall(2, CorrZ0, CorrZ1 + JT, CorrX1, CellWallX, 0, CorridorCeiling + 0.2f, PrisonCream, Material.Blockwork, cellWall);
        CeilingPanels(2);

        // Blue-grey blockwork pilasters between the cell doors, and blue reveals round the windows (as on the photo).
        var pilasterZs = new List<float>();
        var doors = CellDoorZs;
        for (var i = 0; i < doors.Length - 1; i++)
            pilasterZs.Add((doors[i] + doors[i + 1]) / 2);
        foreach (var z in pilasterZs)
            JBox(CorrX1 - 0.12f, 0, z - 0.3f, CorrX1, CorridorCeiling, z + 0.3f, PrisonBlue, Material.Blockwork);
        // A blue dado line along the window wall.
        var blue = JB(Material.Plain, PrisonBlue);
        OBox(blue, float4x4.Identity, JAt(CorrX0 + 0.01f, 0.05f, 0), new float3(0.02f, 0.1f, CorrZ1 - CorrZ0));
        OBox(blue, float4x4.Identity, JAt(CorrX1 - 0.13f, 0.05f, 0), new float3(0.02f, 0.1f, CorrZ1 - CorrZ0));

        // Cell doors: heavy steel in a steel frame, a vision panel, a food hatch, a lock, a number over each.
        var n = 1;
        foreach (var z in doors)
        {
            var label = $"C{n:00}";
            n++;
            if (z == OpenCellZ)
            {
                CellDoorFrame(z);
                // Its door stands open against the wall, into the corridor (swung through 90 degrees).
                CellDoorLeaf(Frame(JAt(CorrX1 - 0.04f, 0, z + 0.5f), 0) * float4x4.Identity, open: true);
                AddLettering(label, JAt(CorrX1 - 0.01f, 2.35f, z), new float3(-1, 0, 0), 0.12f, Material.Plain, new float3(0.9f, 0.9f, 0.88f));
                continue;
            }
            CellDoorFrame(z);
            CellDoorLeaf(float4x4.CreateTranslation(JAt(CorrX1, 0, z)), open: false);
            AddLettering(label, JAt(CorrX1 - 0.01f, 2.35f, z), new float3(-1, 0, 0), 0.12f, Material.Plain, new float3(0.9f, 0.9f, 0.88f));
        }

        // Services along the ceiling: two round white ducts, a smaller one, copper pipes and a cable tray on the
        // window side, hangers.
        var duct = JB(Material.Galvanized, new float3(0.86f, 0.86f, 0.84f));
        var copper = JB(Material.Steel, new float3(0.62f, 0.38f, 0.22f));
        var tray = JB(Material.Steel, new float3(0.45f, 0.46f, 0.48f));
        var zA = CorrZ0 + 0.05f;
        var zB = CorrZ1 + JT - 0.05f;
        OCyl(duct, float4x4.Identity, JAt(24.55f, 2.88f, zA), JAt(24.55f, 2.88f, zB), 0.2f, 0.2f, 18);
        OCyl(duct, float4x4.Identity, JAt(23.95f, 2.98f, zA), JAt(23.95f, 2.98f, zB), 0.12f, 0.12f, 14);
        foreach (var dx in (ReadOnlySpan<float>)[0f, 0.09f, 0.18f])
            OCyl(copper, float4x4.Identity, JAt(CorrX0 + 0.12f + dx, 3.02f, zA), JAt(CorrX0 + 0.12f + dx, 3.02f, zB), 0.022f, 0.022f, 8);
        OBox(tray, float4x4.Identity, JAt(CorrX0 + 0.6f, 3.1f, (zA + zB) / 2), new float3(0.3f, 0.03f, zB - zA));
        for (var z = zA + 0.8f; z < zB; z += 2.4f)
        {
            OBox(tray, float4x4.Identity, JAt(24.55f, 3.13f, z), new float3(0.03f, 0.14f, 0.03f));
            OBox(tray, float4x4.Identity, JAt(24.55f, 2.66f, z), new float3(0.46f, 0.03f, 0.05f));
            // Duct joint bands.
            OCyl(duct, float4x4.Identity, JAt(24.55f, 2.88f, z - 0.03f), JAt(24.55f, 2.88f, z + 0.03f), 0.215f, 0.215f, 18);
        }
        // Red fire alarm call points, an extinguisher, a notice board, a wing sign, a camera at each end.
        var redB = JB(Material.Plain, new float3(0.62f, 0.06f, 0.05f));
        OBox(redB, float4x4.Identity, JAt(CorrX1 - 0.14f, 1.4f, 10.9f), new float3(0.05f, 0.1f, 0.1f));
        OBox(redB, float4x4.Identity, JAt(CorrX1 - 0.14f, 1.4f, -7.0f), new float3(0.05f, 0.1f, 0.1f));
        OCyl(redB, float4x4.Identity, JAt(CorrX0 + 0.14f, 0.4f, 12.0f), JAt(CorrX0 + 0.14f, 0.95f, 12.0f), 0.08f, 0.08f, 12);
        OBox(JB(Material.Plain, new float3(0.2f, 0.2f, 0.2f)), float4x4.Identity, JAt(CorrX0 + 0.14f, 1.02f, 12.0f), new float3(0.06f, 0.1f, 0.06f));
        OBox(JB(Material.Plain, new float3(0.35f, 0.26f, 0.18f)), float4x4.Identity, JAt(CorrX1 - 0.13f, 1.5f, 7.4f), new float3(0.02f, 0.75f, 1.0f));
        foreach (var (dy, dz, w) in (ReadOnlySpan<(float, float, float)>)[(0.15f, -0.2f, 0.3f), (-0.1f, 0.15f, 0.25f), (0.05f, 0.3f, 0.2f)])
            OBox(JB(Material.Plain, new float3(0.9f, 0.9f, 0.86f)), float4x4.Identity, JAt(CorrX1 - 0.145f, 1.5f + dy, 7.4f + dz), new float3(0.004f, w * 1.3f, w));
        AddLettering("WING C", JAt(CorrX0 + 0.01f, 2.45f, 14.6f), new float3(1, 0, 0), 0.2f, Material.Plain, new float3(0.15f, 0.2f, 0.28f));
        AddLettering("EXERCISE YARD", JAt(CorrX0 + 0.01f, 2.45f, YardDoorZ + 1.6f), new float3(1, 0, 0), 0.1f, Material.Plain, new float3(0.15f, 0.2f, 0.28f));
        AddCamera(JAt(CorrX1 - 0.3f, CorridorCeiling - 0.02f, CorrZ1 - 0.4f) + new float3(0, 0, 0), 0.15f);
        AddCamera(JAt(CorrX0 + 0.5f, CorridorCeiling - 0.02f, CorrZ0 + 0.4f), 0.15f);

        // The end wall to the north wing: a locked grille in a blue frame.
        var grille = JB(Material.Steel, new float3(0.28f, 0.3f, 0.32f));
        for (var x = CorrX0 + 0.2f; x < CorrX1 - 0.1f; x += 0.14f)
            OCyl(grille, float4x4.Identity, JAt(x, 0, CorrZ0 + 0.12f), JAt(x, 2.4f, CorrZ0 + 0.12f), 0.015f, 0.015f, 8);
        foreach (var y in (ReadOnlySpan<float>)[0.05f, 1.2f, 2.4f])
            OBox(grille, float4x4.Identity, JAt((CorrX0 + CorrX1) / 2, y, CorrZ0 + 0.12f), new float3(CorrX1 - CorrX0, 0.06f, 0.04f));
        OBox(JB(Material.Plain, PrisonDoorBlue), float4x4.Identity, JAt((CorrX0 + CorrX1) / 2, 1.2f, CorrZ0 + 0.02f), new float3(CorrX1 - CorrX0, 2.4f, 0.03f));
        AddStatic(new Aabb(JAt(CorrX0, 0, CorrZ0), JAt(CorrX1, 2.45f, CorrZ0 + 0.16f)), default, visible: false);

        // The yard door's frame on the inside; a push bar sign.
        var frame = JB(Material.Plain, PrisonBlue);
        OBox(frame, float4x4.Identity, JAt(CorrX0 + 0.02f, 1.1f, YardDoorZ - 0.64f), new float3(0.04f, 2.2f, 0.08f));
        OBox(frame, float4x4.Identity, JAt(CorrX0 + 0.02f, 1.1f, YardDoorZ + 0.64f), new float3(0.04f, 2.2f, 0.08f));
        OBox(frame, float4x4.Identity, JAt(CorrX0 + 0.02f, 2.24f, YardDoorZ), new float3(0.04f, 0.08f, 1.36f));
        JBox(CourtX, -0.3f, YardDoorZ - 0.6f, CorrX0, 0, YardDoorZ + 0.6f, new float3(0.55f, 0.56f, 0.57f), Material.Steel);   // threshold, flush

        BuildOpenCell(random);
    }

    /// <summary>A cell door's frame on the corridor side of the cell wall at z.</summary>
    private void CellDoorFrame(float z)
    {
        var frame = JB(Material.Plain, PrisonBlue * 0.9f);
        OBox(frame, float4x4.Identity, JAt(CorrX1 - 0.03f, 1.08f, z - 0.55f), new float3(0.06f, 2.16f, 0.1f));
        OBox(frame, float4x4.Identity, JAt(CorrX1 - 0.03f, 1.08f, z + 0.55f), new float3(0.06f, 2.16f, 0.1f));
        OBox(frame, float4x4.Identity, JAt(CorrX1 - 0.03f, 2.15f, z), new float3(0.06f, 0.1f, 1.2f));
    }

    /// <summary>
    /// A cell door leaf in the frame <paramref name="f"/>: shut, its face on the cell wall (x is out into the corridor,
    /// z along the wall); or standing open against the wall (then the frame's origin is the hinge).
    /// </summary>
    private void CellDoorLeaf(float4x4 f, bool open)
    {
        var leaf = JB(Material.Plain, PrisonDoorBlue);
        var dark = JB(Material.Plain, new float3(0.05f, 0.05f, 0.06f));
        var steel = JB(Material.Plain, new float3(0.5f, 0.5f, 0.52f));
        if (open)
        {
            // The leaf runs out from the hinge into the corridor (along -x), its face towards +z.
            OBox(leaf, f, new float3(-0.5f, 1.04f, 0.035f), new float3(1.0f, 2.08f, 0.07f));
            OBox(dark, f, new float3(-0.55f, 1.6f, 0.075f), new float3(0.12f, 0.36f, 0.01f));
            OBox(steel, f, new float3(-0.5f, 1.05f, 0.08f), new float3(0.36f, 0.22f, 0.02f));
            OBox(steel, f, new float3(-0.88f, 1.05f, 0.09f), new float3(0.12f, 0.24f, 0.04f));
            FrameCollider(f, new float3(-0.5f, 1.04f, 0.035f), new float3(1.0f, 2.08f, 0.08f));
            return;
        }
        OBox(leaf, f, new float3(-0.02f, 1.04f, 0), new float3(0.05f, 2.08f, 1.0f));
        OBox(dark, f, new float3(-0.048f, 1.6f, -0.05f), new float3(0.01f, 0.36f, 0.12f));   // vision panel
        OBox(steel, f, new float3(-0.05f, 1.05f, 0), new float3(0.02f, 0.22f, 0.36f));       // food hatch
        OBox(steel, f, new float3(-0.06f, 1.05f, 0.38f), new float3(0.04f, 0.24f, 0.12f));   // lock
        OBox(steel, f, new float3(-0.055f, 1.92f, -0.42f), new float3(0.02f, 0.06f, 0.06f)); // observation flap
    }

    /// <summary>The open cell: a steel bed with a thin mattress, a desk and stool, a steel toilet and basin, a high window.</summary>
    private void BuildOpenCell(Random random)
    {
        var z0 = OpenCellZ - 1.6f;
        var z1 = OpenCellZ + 1.6f;
        const float x0 = CellWallX, x1 = OpenCellX1, h = 2.8f;
        JBox(x0, -0.3f, z0, x1, 0, z1, new float3(0.55f, 0.54f, 0.52f), Material.Floor);
        JBox(x0, h, z0, x1, h + 0.2f, z1, new float3(0.82f, 0.8f, 0.76f), Material.WallPaint);
        JBox(x0, 0, z0 - 0.2f, x1 + 0.2f, h + 0.2f, z0, PrisonCream, Material.Blockwork);
        JBox(x0, 0, z1, x1 + 0.2f, h + 0.2f, z1 + 0.2f, PrisonCream, Material.Blockwork);
        var window = new List<Hole> { Hole.Centered(OpenCellZ, 1.6f, 0.9f, 0.8f) };
        HoledWall(2, z0, z1, x1, x1 + 0.2f, 0, h + 0.2f, PrisonCream, Material.Blockwork, window);
        WindowFill(2, window[0], x1 + 0.2f, x1, Pane.Lit, PrisonBlue);
        var bars = JB(Material.Steel, new float3(0.3f, 0.32f, 0.34f));
        for (var z = window[0].A + 0.12f; z < window[0].A + window[0].W - 0.06f; z += 0.13f)
            OCyl(bars, float4x4.Identity, JAt(x1 + 0.03f, 1.6f, z), JAt(x1 + 0.03f, 2.4f, z), 0.014f, 0.014f, 8);
        JBox(CorrX1, -0.3f, OpenCellZ - 0.5f, x0, 0, OpenCellZ + 0.5f, new float3(0.55f, 0.56f, 0.57f), Material.Steel);   // threshold
        CeilingPanels(3);

        // Bed along the south wall.
        var steel = JB(Material.PaintedMetal, new float3(0.5f, 0.55f, 0.58f));
        OBox(steel, float4x4.Identity, JAt(27.9f, 0.42f, z1 - 0.45f), new float3(2.0f, 0.06f, 0.8f));
        foreach (var x in (ReadOnlySpan<float>)[26.95f, 28.85f])
            foreach (var dz in (ReadOnlySpan<float>)[-0.35f, 0.35f])
                OBox(steel, float4x4.Identity, JAt(x, 0.2f, z1 - 0.45f + dz), new float3(0.05f, 0.4f, 0.05f));
        OBox(JB(Material.Cloth, new float3(0.25f, 0.33f, 0.42f)), float4x4.Identity, JAt(27.9f, 0.5f, z1 - 0.45f), new float3(1.95f, 0.1f, 0.76f));
        OBox(JB(Material.Cloth, new float3(0.82f, 0.82f, 0.8f)), float4x4.Identity, JAt(28.6f, 0.58f, z1 - 0.45f), new float3(0.45f, 0.08f, 0.6f));
        OBox(JB(Material.Cloth, new float3(0.32f, 0.3f, 0.26f)), float4x4.Identity, JAt(27.5f, 0.57f, z1 - 0.5f), new float3(0.9f, 0.06f, 0.7f), float4x4.CreateRotationY(0.08f));
        AddStatic(new Aabb(JAt(26.9f, 0, z1 - 0.85f), JAt(28.9f, 0.55f, z1)), default, visible: false);
        // Desk and stool against the north wall, a shelf with a few things on it.
        var desk = JB(Material.Veneer, new float3(0.55f, 0.46f, 0.34f));
        OBox(desk, float4x4.Identity, JAt(28.3f, 0.74f, z0 + 0.3f), new float3(1.1f, 0.04f, 0.6f));
        OBox(steel, float4x4.Identity, JAt(28.3f, 0.36f, z0 + 0.05f), new float3(1.1f, 0.72f, 0.04f));
        OCyl(steel, float4x4.Identity, JAt(28.1f, 0, z0 + 0.75f), JAt(28.1f, 0.45f, z0 + 0.75f), 0.04f, 0.04f, 8);
        OCyl(desk, float4x4.Identity, JAt(28.1f, 0.45f, z0 + 0.75f), JAt(28.1f, 0.48f, z0 + 0.75f), 0.17f, 0.17f, 14);
        OBox(desk, float4x4.Identity, JAt(28.3f, 1.55f, z0 + 0.13f), new float3(1.0f, 0.03f, 0.24f));
        foreach (var (dx, hgt, tint) in (ReadOnlySpan<(float, float, float3)>)[(-0.3f, 0.22f, new(0.6f, 0.2f, 0.15f)), (-0.22f, 0.2f, new(0.2f, 0.3f, 0.5f)), (0.1f, 0.12f, new(0.85f, 0.85f, 0.8f))])
            OBox(JB(Material.Plain, tint), float4x4.Identity, JAt(28.3f + dx, 1.565f + hgt / 2, z0 + 0.13f), new float3(0.05f, hgt, 0.17f));
        OBox(JB(Material.Plain, new float3(0.92f, 0.92f, 0.88f)), float4x4.Identity, JAt(28.2f, 0.765f, z0 + 0.3f), new float3(0.3f, 0.01f, 0.22f), float4x4.CreateRotationY(0.2f));
        AddStatic(new Aabb(JAt(27.75f, 0, z0), JAt(28.85f, 0.76f, z0 + 0.6f)), default, visible: false);
        // A stainless steel toilet and basin in the north-east corner.
        var ss = JB(Material.Steel, new float3(0.7f, 0.71f, 0.72f));
        OBox(ss, float4x4.Identity, JAt(x1 - 0.3f, 0.2f, z0 + 1.2f), new float3(0.55f, 0.4f, 0.4f));
        OCyl(ss, float4x4.Identity, JAt(x1 - 0.4f, 0.4f, z0 + 1.2f), JAt(x1 - 0.4f, 0.43f, z0 + 1.2f), 0.2f, 0.2f, 14);
        OBox(ss, float4x4.Identity, JAt(x1 - 0.2f, 0.9f, z0 + 0.45f), new float3(0.35f, 0.25f, 0.45f));
        AddStatic(new Aabb(JAt(x1 - 0.6f, 0, z0 + 0.2f), JAt(x1, 0.45f, z0 + 1.45f)), default, visible: false);
        _ = random;
    }
}
