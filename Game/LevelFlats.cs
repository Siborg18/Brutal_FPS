using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// A Dutch post-war block of flats, three kilometres down (the shader's areas 11 and 12: y &lt; -2500), near x = z = 0 like
/// the police station and the prison so its detail keeps full float precision. It's always day here, as in the prison.
/// <code>
///     looking down, north is -z; local coordinates from FlatsOrigin; undercroft floor at y = 0
///
///     z -34   grass, hedges, the block behind
///     z -6    +------------------- rear of the block ---------------------+
///             | west flank  |   undercroft: open front and rear  |#stair| east flank
///             |     (cobalt)|)   -> Rook ->    columns          [door]  |
///     z +6    +----------- white fascia, brick storeys above -------------+
///                     planter  \   tarmac ramp down   /  planter
///     z +11   paved forecourt (y = 0.6), the young trees, the utility box
///     z +18   the road, a parked car, the block across the street
/// </code>
/// The ground floor stands open on square concrete columns; the sky's light dies away inside (<see cref="FlatsShadeVolumes"/>),
/// from both open sides, to almost nothing in the middle. The door in its east wall opens on a cramped dog-leg stairwell up
/// four floors, lit like a building's interior (the shader's area 12) by a lamp on every landing, each flickering in the shader (flatsLampFlicker): those on even floors hold steady with the odd stutter, those on odd floors are failing.
/// Both its open sides are blockaded (LevelFlatsBarricades.cs), so the undercroft and the stairwell are all you can walk in.
/// </summary>
public sealed partial class Level
{
    public static readonly float3 FlatsOrigin = new(0, -3000, 0);

    /// <summary>Below this height a point is in the block of flats (see <see cref="ZoneAt"/> and the shader's areaOf).</summary>
    public const float FlatsBelowY = -2500f;

    /// <summary>A point at the flats, from its local coordinates.</summary>
    public static float3 FlatsAt(float x, float y, float z) => FlatsOrigin + new float3(x, y, z);

    private const float FlatsHalfX = 24f, FlatsHalfZ = 6f;     // the block's outer faces
    private const float UnderHalfX = 9f;                       // the undercroft's side walls (inner faces)
    private const float UnderCeiling = 3.1f;
    private const float FasciaTop = 3.8f;                      // the brick starts here
    private const float StoreyHeight = 3f;
    private const int Storeys = 4;
    private const float FlatsRoof = FasciaTop + Storeys * StoreyHeight;
    private const float FlatsStreetY = 0.6f;                   // the street, up the ramp from the undercroft's floor
    private const float RampHalfX = 5.5f, RampEndZ = 11f;

    // The stairwell (inner faces): a door from the undercroft at z = FlatsDoorZ, a dog-leg up along its two lanes.
    private const float StairX0 = 9.3f, StairX1 = 11.9f, StairZ0 = -3.4f, StairZ1 = 3.7f;
    private const float StairWall = 0.3f;
    private const float StairFlightZ0 = -1.7f, StairFlightZ1 = 1.7f;   // the flights run between these
    private const float SpineX0 = 10.5f, SpineX1 = 10.7f;
    private const float StairStorey = 3f;
    private const int StairStoreys = 4;
    private const float StairTop = StairStorey * StairStoreys + 2.8f;
    private const int StairRisers = 9;                         // a riser of 1/6 m: well within what you can step up
    private const float FlatsDoorZ = 2.5f, FlatsDoorW = 1.1f, FlatsDoorH = 2.08f;

    private static readonly float3 FlatsBrick = new(0.33f, 0.145f, 0.10f);
    private static readonly float3 FlatsConcrete = new(0.50f, 0.51f, 0.52f);
    private static readonly float3 FlatsDarkWall = new(0.17f, 0.18f, 0.19f);
    private static readonly float3 FlatsFascia = new(0.80f, 0.80f, 0.78f);
    private static readonly float3 FlatsTarmac = new(0.13f, 0.13f, 0.13f);
    private static readonly float3 FlatsPaving = new(0.46f, 0.46f, 0.45f);
    private static readonly float3 FlatsBlue = new(0.05f, 0.17f, 0.52f);
    private static readonly float3 FlatsFrame = new(0.80f, 0.80f, 0.78f);

    /// <summary>The cobalt portal: in the undercroft's west wall, looking across it at the stairwell's door.</summary>
    public static float3 FlatsPortalFloorPoint => FlatsAt(-UnderHalfX, 0, FlatsDoorZ);
    public static float3 FlatsPortalNormal => new(1, 0, 0);
    public static readonly float3 FlatsPortalColor = new(0.10f, 0.32f, 0.95f);

    /// <summary>The wall the portal is cut through, with the empty space behind it for your body as you walk in.</summary>
    public const float FlatsPortalWall = 0.4f;

    private const float FlatsPcZ = -1.6f;   // the middle of the PC's table, along the west wall north of the portal

    /// <summary>Where the player stands to use the PC wired to the cobalt portal: in front of its table.</summary>
    public static Aabb FlatsPcZone => BoxBetween(FlatsAt(-UnderHalfX + 0.7f, -0.5f, FlatsPcZ - 1.1f), FlatsAt(-UnderHalfX + 2.0f, 3f, FlatsPcZ + 1.1f));

    /// <summary>Where you start with BRUTALFPS_START=flats: in front of the cobalt portal, looking across at the door.</summary>
    public static float3 FlatsStart => FlatsPortalFloorPoint + new float3(2.5f, 0, 0);

    public static float3 FlatsShadowCenter => FlatsAt(0, 4, 2);
    public const float FlatsShadowHalfExtent = 52f;

    /// <summary>Inside the stairwell (a little into its walls): the shader's area 12.</summary>
    public static Aabb FlatsStairwell => BoxBetween(FlatsAt(UnderHalfX + 0.04f, -0.12f, StairZ0 - 0.06f), FlatsAt(StairX1 + 0.06f, StairTop + 0.06f, StairZ1 + 0.06f));

    /// <summary>The undercroft (its reverb room).</summary>
    public static Aabb FlatsUndercroft => BoxBetween(FlatsAt(-UnderHalfX, 0, -FlatsHalfZ), FlatsAt(UnderHalfX, UnderCeiling, FlatsHalfZ));

    /// <summary>The street, the undercroft's outside and the rear (their reverb room).</summary>
    public static Aabb FlatsGrounds => BoxBetween(FlatsAt(-200, -60, -200), FlatsAt(200, 150, 200));

    /// <summary>
    /// The undercroft's dark: two boxes meeting in the middle, each fading away from its own open side (the front, the rear),
    /// so it's at its darkest halfway through. They're a shade below the ceiling's underside so the fascia and the
    /// brick outside stay in daylight.
    /// </summary>
    public static ShadeVolume[] FlatsShadeVolumes =>
    [
        new(BoxBetween(FlatsAt(-UnderHalfX - 0.05f, -0.05f, 0), FlatsAt(UnderHalfX + 0.05f, UnderCeiling + 0.02f, FlatsHalfZ + 0.3f)),
            FlatsAt(0, 0.6f, FlatsHalfZ + 3.2f), 5.4f, 0.02f),
        new(BoxBetween(FlatsAt(-UnderHalfX - 0.05f, -0.05f, -FlatsHalfZ - 0.3f), FlatsAt(UnderHalfX + 0.05f, UnderCeiling + 0.02f, 0)),
            FlatsAt(0, 0.6f, -FlatsHalfZ - 3.2f), 5.4f, 0.02f),
    ];

    /// <summary>The stairwell's lamps, one on each landing. The shader flickers them by their height (flatsLampFlicker): keep them on these heights.</summary>
    public static OfficeLight[] FlatsLights
    {
        get
        {
            var lights = new List<OfficeLight>();
            for (var floor = 0; floor <= StairStoreys; floor++)
                lights.Add(new OfficeLight(FlatsAt(10.6f, floor * StairStorey + 2.5f, StairZ1 - 0.25f), new float3(1.0f, 0.86f, 0.62f), floor % 2 == 0 ? 4f : 3f, 7.5f, FlatsStairwell, Panel: false));
            return [.. lights];
        }
    }

    /// <summary>The portal's glow on the undercroft's dark floor and columns.</summary>
    public static LocalLight FlatsPortalGlow =>
        new(FlatsPortalFloorPoint + new float3(0.8f, 1f, 0), FlatsPortalColor, 1.6f, 7f, BoxBetween(FlatsAt(-UnderHalfX, -0.1f, -FlatsHalfZ), FlatsAt(0, UnderCeiling + 0.1f, FlatsHalfZ)));

    /// <summary>Everything you can step up onto, and down from without falling, is here and on the steps up to the factory's rear door: the rest of the game has no stairs.</summary>
    public static bool HasStairs(float3 p) => p.Y < FlatsBelowY || RearStepsArea.Contains(p) || StationStepsArea.Contains(p);

    // ------------------------------------------------------------------------------------------------ Rook's way out

    /// <summary>Where he stops, hand on the door, until you've come through after him.</summary>
    public static float3 RookAtDoor => FlatsAt(UnderHalfX - 0.55f, 0, FlatsDoorZ);

    /// <summary>Where the two masked gunmen stand, either side of the stairwell's door: clear of its swing, and of Rook's way to it.</summary>
    public static float3[] FlatsDoorGuards =>
    [
        FlatsAt(UnderHalfX - 0.6f, 0, FlatsDoorZ - 1.35f),
        FlatsAt(UnderHalfX - 0.6f, 0, FlatsDoorZ + 1.35f),
    ];

    /// <summary>Through the door, and up the dog-leg to the top landing.</summary>
    public static float3[] RookUpTheStairs
    {
        get
        {
            const float east = 11.3f, west = 9.9f, landing = 2.5f, turn = -2.5f;
            var way = new List<float3> { FlatsAt(9.75f, 0, FlatsDoorZ), FlatsAt(east, 0, landing) };
            for (var floor = 0; floor < StairStoreys; floor++)
            {
                var y = floor * StairStorey;
                way.Add(FlatsAt(east, y, StairFlightZ1));
                way.Add(FlatsAt(east, y + StairStorey / 2, StairFlightZ0));
                way.Add(FlatsAt(east - 0.2f, y + StairStorey / 2, turn));
                way.Add(FlatsAt(west + 0.2f, y + StairStorey / 2, turn));
                way.Add(FlatsAt(west, y + StairStorey / 2, StairFlightZ0));
                way.Add(FlatsAt(west, y + StairStorey, StairFlightZ1));
                way.Add(FlatsAt(west + 0.2f, y + StairStorey, landing));
                if (floor < StairStoreys - 1)
                    way.Add(FlatsAt(east - 0.2f, y + StairStorey, landing));
            }
            way.Add(FlatsAt(StairX1 - 0.6f, StairStoreys * StairStorey, TopDoorZ));
            return [.. way];
        }
    }

    /// <summary>In through the door on the top landing: where he ends up, out of sight once it's shut.</summary>
    public static float3 RookGone => FlatsAt(13.2f, StairStoreys * StairStorey, TopDoorZ);

    // ------------------------------------------------------------------------------------------------ building

    private static Aabb FBoxBetween(float x0, float y0, float z0, float x1, float y1, float z1) => BoxBetween(FlatsAt(x0, y0, z0), FlatsAt(x1, y1, z1));

    /// <summary>A box between two corners in the flats' coordinates.</summary>
    private void FBox(float x0, float y0, float z0, float x1, float y1, float z1, float3 tint, Material material, bool collides = true)
    {
        _boxes.Add(new LevelBox(FBoxBetween(x0, y0, z0, x1, y1, z1), tint, material, Collides: collides));
        _version++;
    }

    /// <summary>An invisible wall in the flats' coordinates.</summary>
    private void FCollider(float x0, float y0, float z0, float x1, float y1, float z1) =>
        AddStatic(FBoxBetween(x0, y0, z0, x1, y1, z1), default, visible: false, blocksShots: false);

    private void BuildFlats()
    {
        var random = new Random(327);
        BuildFlatsGround();
        BuildFlatsUndercroft();
        BuildFlatsBlock(random);
        BuildFlatsStairwell();
        BuildFlatsDoors();
        BuildFlatsPc();
        BuildFlatsSurroundings(random);
        BuildFlatsBarricades(random);
        FlushPoliceBatches();
    }

    private void BuildFlatsGround()
    {
        // The undercroft's floor and the tarmac on out of the back, the grass round that.
        FBox(-UnderHalfX, -1, -FlatsHalfZ - 4, UnderHalfX, 0, FlatsHalfZ, FlatsTarmac, Material.Floor);
        FBox(-40, -1, -44, 40, 0, -FlatsHalfZ - 4, new float3(0.2f, 0.2f, 0.2f), Material.Grass);
        FBox(-40, -1, -FlatsHalfZ - 4, -UnderHalfX, 0, -FlatsHalfZ, new float3(0.2f, 0.2f, 0.2f), Material.Grass);
        FBox(UnderHalfX, -1, -FlatsHalfZ - 4, 40, 0, -FlatsHalfZ, new float3(0.2f, 0.2f, 0.2f), Material.Grass);
        // Under the flanks (hidden by what stands on them).
        FBox(-FlatsHalfX, -1, -FlatsHalfZ, -UnderHalfX, 0, FlatsHalfZ, FlatsDarkWall, Material.Concrete);
        FBox(UnderHalfX, -1, -FlatsHalfZ, FlatsHalfX, 0, FlatsHalfZ, FlatsDarkWall, Material.Concrete);

        // The ramp, up from the undercroft's floor to the forecourt. You walk on a stair of tiny steps just under its
        // surface, and shots stop on them; you see one sloping sheet of tarmac.
        var tarmac = JB(Material.Floor, FlatsTarmac);
        var a = FlatsAt(-RampHalfX, 0, FlatsHalfZ);
        var b = FlatsAt(RampHalfX, 0, FlatsHalfZ);
        var c = FlatsAt(RampHalfX, FlatsStreetY, RampEndZ);
        var d = FlatsAt(-RampHalfX, FlatsStreetY, RampEndZ);
        tarmac.Quad(a, b, c, d, float3.Normalize(new float3(0, RampEndZ - FlatsHalfZ, -FlatsStreetY)));
        const int rampSteps = 12;
        for (var i = 0; i < rampSteps; i++)
        {
            var z0 = FlatsHalfZ + (RampEndZ - FlatsHalfZ) * i / rampSteps;
            var z1 = FlatsHalfZ + (RampEndZ - FlatsHalfZ) * (i + 1) / rampSteps;
            FBox(-RampHalfX, -1, z0, RampHalfX, FlatsStreetY * i / rampSteps, z1, FlatsTarmac, Material.Floor);
        }

        // The ground either side of the ramp, at street level.
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            FBox(side * RampHalfX, -1, FlatsHalfZ, side * 60f, FlatsStreetY, RampEndZ, new float3(0.2f, 0.2f, 0.2f), Material.Grass);
        // The paved forecourt, the pavement, the road, the far pavement.
        FBox(-60, -1, RampEndZ, 60, FlatsStreetY, 18, FlatsPaving, Material.Floor);
        FBox(-60, -1, 18, 60, FlatsStreetY - 0.13f, 25.5f, FlatsTarmac * 1.15f, Material.Floor);
        FBox(-60, -1, 25.5f, 60, FlatsStreetY, 28, FlatsPaving, Material.Floor);
        // Lines of pale slabs across the forecourt, the way a brick-paved court is laid.
        var joints = JB(Material.Plain, FlatsPaving * 0.62f);
        for (var x = -26f; x <= 26f; x += 0.42f)
            OBox(joints, float4x4.Identity, FlatsAt(x, FlatsStreetY + 0.002f, 14.5f), new float3(0.012f, 0.004f, 7f));
        for (var z = 11.3f; z < 18f; z += 0.21f)
            OBox(joints, float4x4.Identity, FlatsAt(0, FlatsStreetY + 0.002f, z), new float3(52f, 0.004f, 0.012f));
    }

    private void BuildFlatsUndercroft()
    {
        // The underside of the block over it, in two layers: bare concrete soffit, and the slab it's cast on.
        FBox(-UnderHalfX, UnderCeiling, -FlatsHalfZ, UnderHalfX, FasciaTop, FlatsHalfZ, FlatsConcrete * 0.7f, Material.Concrete);

        // The west wall with the portal in it, and the empty space behind it (0.4 m of wall, then the void your body reaches into).
        FBox(-UnderHalfX - FlatsPortalWall, 0, -FlatsHalfZ, -UnderHalfX, UnderCeiling, FlatsHalfZ, FlatsDarkWall, Material.Concrete);
        // The east wall, with the door in it, is the stairwell's west wall where they meet (see BuildFlatsStairwell).
        FBox(UnderHalfX, 0, -FlatsHalfZ, StairX0, UnderCeiling, StairZ0 - StairWall, FlatsDarkWall, Material.Concrete);
        FBox(UnderHalfX, 0, StairZ1 + StairWall, StairX0, UnderCeiling, FlatsHalfZ, FlatsDarkWall, Material.Concrete);

        // Three rows of tall square columns, wider than deep, a little chamfered by a band at their heads. A clear lane is left
        // along z = 2.5 from the portal to the door.
        ReadOnlySpan<float> columnsX = [-7f, -2.35f, 2.35f, 7f];
        ReadOnlySpan<float> columnsZ = [5.25f, 0f, -5.25f];
        var head = JB(Material.WallPaint, FlatsConcrete * 0.8f);
        foreach (var z in columnsZ)
        {
            foreach (var x in columnsX)
            {
                // (The front row stands on the ramp's edge. They all start under the floor.)
                FBox(x - 0.3f, -0.1f, z - 0.3f, x + 0.3f, UnderCeiling, z + 0.3f, FlatsConcrete * 0.8f, Material.WallPaint);
                OBox(head, float4x4.Identity, FlatsAt(x, UnderCeiling - 0.1f, z), new float3(0.72f, 0.2f, 0.72f));
            }
        }

        // Pipes along the soffit, and a dead strip light.
        var pipe = JB(Material.Steel, new float3(0.24f, 0.24f, 0.25f));
        foreach (var dz in (ReadOnlySpan<float>)[-2.1f, -1.9f, -1.7f])
            OCyl(pipe, float4x4.Identity, FlatsAt(-UnderHalfX, UnderCeiling - 0.12f, dz), FlatsAt(UnderHalfX, UnderCeiling - 0.12f, dz), 0.05f, 0.05f, 10);
        OBox(JB(Material.Plain, new float3(0.28f, 0.28f, 0.27f)), float4x4.Identity, FlatsAt(3f, UnderCeiling - 0.04f, 2.5f), new float3(1.3f, 0.08f, 0.14f));
    }

    /// <summary>
    /// A folding table against the west wall, north of the cobalt portal, with the PC that's wired to the ring: a monitor showing
    /// the portal, a keyboard, a tower under it, and a lead along the wall into the ring. x runs north along the wall (the
    /// portal is on -x), y up, z out into the undercroft.
    /// </summary>
    private void BuildFlatsPc()
    {
        var desk = WallFrame(FlatsAt(-UnderHalfX, 0, FlatsPcZ), new float3(1, 0, 0));
        var top = JB(Material.Plain, new float3(0.24f, 0.23f, 0.21f));
        var legs = JB(Material.Steel, new float3(0.3f, 0.3f, 0.31f));
        OBox(top, desk, new float3(0, 0.735f, 0.35f), new float3(1.4f, 0.03f, 0.68f));
        foreach (var x in (ReadOnlySpan<float>)[-0.64f, 0.64f])
            foreach (var z in (ReadOnlySpan<float>)[0.06f, 0.64f])
                OBox(legs, desk, new float3(x, 0.36f, z), new float3(0.04f, 0.72f, 0.04f));
        FrameCollider(desk, new float3(0, 0.38f, 0.35f), new float3(1.4f, 0.76f, 0.68f));

        // The monitor: the portal's ring, its status lines.
        var plastic = JB(Material.Polymer, new float3(0.06f, 0.06f, 0.065f));
        var monitor = float4x4.CreateTranslation(0, 0.75f, 0.2f) * desk;
        OBox(plastic, monitor, new float3(0, 0.012f, 0), new float3(0.28f, 0.024f, 0.2f));
        OBox(plastic, monitor, new float3(0, 0.15f, -0.065f), new float3(0.06f, 0.28f, 0.03f));
        OBox(plastic, monitor, new float3(0, 0.36f, -0.03f), new float3(0.7f, 0.42f, 0.04f));
        OBox(JB(Material.Emissive, new float3(0.012f, 0.02f, 0.05f)), monitor, new float3(0, 0.365f, -0.005f), new float3(0.65f, 0.37f, 0.012f));
        var ring = JB(Material.Emissive, FlatsPortalColor);
        for (var i = 0; i < 28; i++)
        {
            var a = float.Tau * i / 28;
            OBox(ring, monitor, float3.Zero, new float3(0.022f, 0.006f, 0.002f),
                 float4x4.CreateRotationZ(a + float.Pi / 2) * float4x4.CreateTranslation(-0.17f + float.Cos(a) * 0.1f, 0.38f + float.Sin(a) * 0.1f, 0.003f));
        }
        var status = JB(Material.Emissive, new float3(0.35f, 0.55f, 1.0f));
        for (var i = 0; i < 9; i++)
            OBox(status, monitor, new float3(0.14f, 0.5f - i * 0.028f, 0.003f), new float3(0.2f - (i * 37 % 5) * 0.03f, 0.01f, 0.002f));

        // Keyboard and mouse; the tower under the table, its light at the front.
        OBox(plastic, desk, new float3(0, 0.762f, 0.5f), new float3(0.44f, 0.024f, 0.15f));
        OBox(plastic, desk, new float3(0.32f, 0.765f, 0.5f), new float3(0.06f, 0.03f, 0.1f));
        var tower = new float3(0.4f, 0, 0.33f);
        OBox(plastic, desk, tower + new float3(0, 0.23f, 0), new float3(0.2f, 0.46f, 0.46f));
        OBox(JB(Material.Emissive, new float3(0.25f, 0.45f, 1.0f)), desk, tower + new float3(0.05f, 0.41f, 0.235f), new float3(0.014f, 0.014f, 0.01f));

        // Its leads: the monitor's to the tower, and a heavy one from the tower up the wall and along it into the ring.
        var wire = JB(Material.Polymer, new float3(0.03f, 0.03f, 0.03f));
        OCyl(wire, desk, new float3(0, 0.85f, 0.14f), new float3(0.35f, 0.76f, 0.02f), 0.006f, 0.006f, 6);
        OCyl(wire, desk, new float3(0.35f, 0.76f, 0.02f), tower + new float3(-0.05f, 0.3f, -0.23f), 0.006f, 0.006f, 6);
        const float wireY = 1.1f, r = 0.014f;
        var ringEdge = FlatsPcZ - FlatsDoorZ + 1.43f;   // (x of the ring's rim, at about this height)
        OCyl(wire, desk, tower + new float3(0, 0.25f, -0.23f), new float3(0.4f, 0.25f, 0.018f), r, r, 8);
        OCyl(wire, desk, new float3(0.4f, 0.25f, 0.018f), new float3(0.4f, wireY, 0.018f), r, r, 8);
        OCyl(wire, desk, new float3(0.4f, wireY, 0.018f), new float3(ringEdge, wireY, 0.018f), r, r, 8);
        var clip = JB(Material.Steel, new float3(0.22f, 0.22f, 0.23f));
        for (var x = 0.2f; x > ringEdge; x -= 0.7f)
            OBox(clip, desk, new float3(x, wireY, 0.014f), new float3(0.03f, 0.05f, 0.03f));
    }

    /// <summary>The block itself: the flanks of the ground floor, the fascia, four storeys of brick with their windows, the roof.</summary>
    private void BuildFlatsBlock(Random random)
    {
        var id = float4x4.Identity;
        // The flanks of the ground floor: solid behind dark grey panels (you can't get in), with a band of white-framed windows.
        var westBack = -UnderHalfX - FlatsPortalWall - 1f;   // the void behind the portal's wall
        FBox(-FlatsHalfX, 0, -FlatsHalfZ, westBack, UnderCeiling, FlatsHalfZ, FlatsDarkWall, Material.Concrete);
        FBox(westBack, 0, -FlatsHalfZ, -UnderHalfX - FlatsPortalWall, UnderCeiling, StairZ0, FlatsDarkWall, Material.Concrete);
        FBox(westBack, 0, StairZ1 + StairWall, -UnderHalfX - FlatsPortalWall, UnderCeiling, FlatsHalfZ, FlatsDarkWall, Material.Concrete);
        FBox(StairX1 + StairWall, 0, -FlatsHalfZ, FlatsHalfX, UnderCeiling, FlatsHalfZ, FlatsDarkWall, Material.Concrete);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            foreach (var z in (ReadOnlySpan<float>)[-FlatsHalfZ, FlatsHalfZ])
            {
                var out_ = z > 0 ? 1f : -1f;
                for (var x = UnderHalfX + 0.8f; x + 2.8f < FlatsHalfX; x += 3.4f)
                    FlatsWindow(side * (x + 1.4f), z + out_ * 0.015f, out_, 1.4f, 3.0f, 2.8f, random, curtain: false);
            }
        }

        // The white fascia round the front and rear, its cap, and the grey band over it where the brick starts.
        foreach (var z in (ReadOnlySpan<float>)[-FlatsHalfZ, FlatsHalfZ])
        {
            var out_ = z > 0 ? 1f : -1f;
            FBox(-FlatsHalfX, UnderCeiling, z - (out_ > 0 ? 0.3f : -0.15f), FlatsHalfX, FasciaTop - 0.08f, z + (out_ > 0 ? 0.15f : -0.3f), FlatsFascia, Material.Plain);
            FBox(-FlatsHalfX - 0.05f, FasciaTop - 0.08f, z - (out_ > 0 ? 0.3f : -0.22f), FlatsHalfX + 0.05f, FasciaTop, z + (out_ > 0 ? 0.22f : -0.3f), new float3(0.28f, 0.28f, 0.28f), Material.Concrete);
        }
        FBox(-FlatsHalfX, UnderCeiling, -FlatsHalfZ, -UnderHalfX, FasciaTop, FlatsHalfZ, FlatsConcrete * 0.7f, Material.Concrete);
        FBox(UnderHalfX, UnderCeiling, -FlatsHalfZ, StairX0, FasciaTop, StairZ0 - StairWall, FlatsConcrete * 0.7f, Material.Concrete);
        FBox(UnderHalfX, UnderCeiling, StairZ1 + StairWall, FlatsHalfX, FasciaTop, FlatsHalfZ, FlatsConcrete * 0.7f, Material.Concrete);
        FBox(StairX1 + StairWall, UnderCeiling, StairZ0 - StairWall, FlatsHalfX, FasciaTop, StairZ1 + StairWall, FlatsConcrete * 0.7f, Material.Concrete);

        // Brick front and rear, the blue strips between the bays.
        FlatsFacade(FlatsHalfZ, 1f, FlatsHalfX, FasciaTop, Storeys, random, blueStrips: true);
        FlatsFacade(-FlatsHalfZ, -1f, FlatsHalfX, FasciaTop, Storeys, random, blueStrips: true);
        var brick = JB(Material.Brick, FlatsBrick);
        // The ends, the roof with its parapet.
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var x = side * (FlatsHalfX - 0.15f);
            OBox(brick, id, FlatsAt(x, 0.3f + FlatsRoof / 2, 0), new float3(0.3f, FlatsRoof - 0.6f, 2 * FlatsHalfZ));
        }
        var parapet = JB(Material.Concrete, new float3(0.3f, 0.3f, 0.3f));
        OBox(parapet, id, FlatsAt(0, FlatsRoof - 0.1f, 0), new float3(2 * FlatsHalfX, 0.2f, 2 * FlatsHalfZ));
        foreach (var z in (ReadOnlySpan<float>)[-FlatsHalfZ, FlatsHalfZ])
            OBox(parapet, id, FlatsAt(0, FlatsRoof + 0.3f, z), new float3(2 * FlatsHalfX + 0.1f, 0.6f, 0.3f));
        foreach (var x in (ReadOnlySpan<float>)[-FlatsHalfX, FlatsHalfX])
            OBox(parapet, id, FlatsAt(x, FlatsRoof + 0.3f, 0), new float3(0.3f, 0.6f, 2 * FlatsHalfZ));

        // Shots stop on the mass of the block behind its windows (it's never seen: the windows hide it), and so do you.
        FBox(-FlatsHalfX + 0.3f, FasciaTop, -FlatsHalfZ + 0.32f, UnderHalfX, FlatsRoof - 0.2f, FlatsHalfZ - 0.32f, new float3(0.1f, 0.1f, 0.1f), Material.Plain);
        FBox(UnderHalfX, FasciaTop, -FlatsHalfZ + 0.32f, FlatsHalfX - 0.3f, FlatsRoof - 0.2f, StairZ0 - StairWall, new float3(0.1f, 0.1f, 0.1f), Material.Plain);
        FBox(UnderHalfX, FasciaTop, StairZ1 + StairWall, FlatsHalfX - 0.3f, FlatsRoof - 0.2f, FlatsHalfZ - 0.32f, new float3(0.1f, 0.1f, 0.1f), Material.Plain);
        FBox(TopHallX1 + 0.05f, FasciaTop, StairZ0 - StairWall, FlatsHalfX - 0.3f, FlatsRoof - 0.2f, StairZ1 + StairWall, new float3(0.1f, 0.1f, 0.1f), Material.Plain);
        FBox(StairX1 + StairWall, FasciaTop, StairZ0 - StairWall, TopHallX1, StairStoreys * StairStorey - 0.25f, StairZ1 + StairWall, new float3(0.1f, 0.1f, 0.1f), Material.Plain);
        FBox(StairX1 + StairWall, StairStoreys * StairStorey + TopHallHeight + 0.05f, StairZ0 - StairWall, TopHallX1, FlatsRoof - 0.2f, StairZ1 + StairWall, new float3(0.1f, 0.1f, 0.1f), Material.Plain);

        // The small blue street sign on the fascia, and the block's number board at its corner.
        OBox(JB(Material.Plain, FlatsBlue), id, FlatsAt(3.1f, UnderCeiling + 0.36f, FlatsHalfZ + 0.17f), new float3(0.78f, 0.24f, 0.03f));
        AddLettering("HOF", FlatsAt(3.1f, UnderCeiling + 0.36f, FlatsHalfZ + 0.186f), new float3(0, 0, 1), 0.12f, Material.Plain, new float3(0.85f, 0.87f, 0.9f));
        OBox(JB(Material.Plain, new float3(0.80f, 0.80f, 0.78f)), id, FlatsAt(-6.8f, UnderCeiling + 0.16f, FlatsHalfZ + 0.18f), new float3(0.62f, 0.86f, 0.04f));
        OBox(JB(Material.Plain, new float3(0.72f, 0.60f, 0.08f)), id, FlatsAt(-7.04f, UnderCeiling + 0.16f, FlatsHalfZ + 0.205f), new float3(0.08f, 0.86f, 0.01f));
        AddLettering("327", FlatsAt(-6.74f, UnderCeiling + 0.06f, FlatsHalfZ + 0.205f), new float3(0, 0, 1), 0.14f, Material.Plain, new float3(0.1f, 0.12f, 0.22f));
    }

    /// <summary>
    /// A brick face along x at z = <paramref name="faceZ"/>: piers and spandrels with the windows recessed between them,
    /// tall ones to the floor with a thin rail across on the upper storeys, a sill band under each floor.
    /// </summary>
    private void FlatsFacade(float faceZ, float outward, float halfX, float baseY, int storeys, Random random, bool blueStrips)
    {
        const float bay = 4.1f, windowW = 3.0f, depth = 0.3f;
        var id = float4x4.Identity;
        var brick = JB(Material.Brick, FlatsBrick);
        var band = JB(Material.Concrete, new float3(0.46f, 0.46f, 0.44f));
        var bays = (int)(2 * halfX / bay);
        var start = -bays * bay / 2;
        var z = faceZ - outward * depth / 2;
        for (var storey = 0; storey < storeys; storey++)
        {
            var y0 = baseY + storey * StoreyHeight;
            var sill = y0 + 0.28f;
            var head = y0 + 2.5f;
            OBox(brick, id, FlatsAt(0, (y0 + sill) / 2, z), new float3(2 * halfX, sill - y0, depth));
            OBox(brick, id, FlatsAt(0, (head + y0 + StoreyHeight) / 2, z), new float3(2 * halfX, y0 + StoreyHeight - head, depth));
            OBox(band, id, FlatsAt(0, y0 + 0.03f, faceZ + outward * 0.01f), new float3(2 * halfX, 0.06f, 0.02f));
            for (var b = 0; b <= bays; b++)
            {
                var x = start + b * bay;
                // The pier between two windows, and the bit of wall at each end of the block.
                OBox(brick, id, FlatsAt(x, (sill + head) / 2, z), new float3(bay - windowW, head - sill, depth));
                if (b < bays)
                {
                    FlatsWindow(x + bay / 2, faceZ - outward * (depth - 0.015f), outward, sill, head, windowW, random, curtain: true);
                    if (storey > 0)
                        FlatsRail(x + bay / 2, faceZ + outward * 0.05f, y0 + 1.15f, windowW);
                }
            }
        }
        // The ends of the bays' brick, if the bays don't reach the block's ends.
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var inner = bays * bay / 2 + (bay - windowW) / 2;
            if (inner < halfX)
                OBox(brick, id, FlatsAt(side * (inner + halfX) / 2, baseY + storeys * StoreyHeight / 2, z), new float3(halfX - inner, storeys * StoreyHeight, depth));
        }
        if (!blueStrips)
            return;
        // A strip of blue paint on every third pier, floor to roof, as on the photographs.
        var blue = JB(Material.PaintedMetal, FlatsBlue);
        for (var b = 1; b < bays; b += 3)
        {
            var x = start + b * bay;
            OBox(blue, id, FlatsAt(x, baseY + storeys * StoreyHeight / 2 - 0.2f, faceZ + outward * 0.03f), new float3(0.42f, storeys * StoreyHeight - 0.4f, 0.06f));
        }
    }

    /// <summary>A white-framed window: an outer frame, three lights and a top hopper; dark glass that shows the sky, some with curtains drawn.</summary>
    private void FlatsWindow(float x, float z, float outward, float sill, float head, float width, Random random, bool curtain)
    {
        var id = float4x4.Identity;
        var frame = JB(Material.Plain, FlatsFrame);
        var glass = JB(Material.Glass, new float3(0.22f, 0.25f, 0.28f));
        var height = head - sill;
        const float bar = 0.07f, proud = 0.05f;
        var midY = (sill + head) / 2;
        OBox(glass, id, FlatsAt(x, midY, z + outward * 0.004f), new float3(width, height, 0.01f));
        foreach (var dx in (ReadOnlySpan<float>)[-width / 2 + bar / 2, width / 2 - bar / 2, -width / 6, width / 6])
            OBox(frame, id, FlatsAt(x + dx, midY, z + outward * proud / 2), new float3(bar, height, proud));
        OBox(frame, id, FlatsAt(x, sill + bar / 2, z + outward * proud / 2), new float3(width, bar, proud));
        OBox(frame, id, FlatsAt(x, head - bar / 2, z + outward * proud / 2), new float3(width, bar, proud));
        OBox(frame, id, FlatsAt(x, head - 0.5f, z + outward * proud / 2), new float3(width, bar * 0.7f, proud));
        if (!curtain)
            return;
        // Curtains or a blind, behind some of the lights.
        for (var i = 0; i < 3; i++)
        {
            var r = random.NextDouble();
            if (r < 0.62)
                continue;
            var tint = r < 0.88 ? new float3(0.58f, 0.54f, 0.44f) : new float3(0.30f, 0.27f, 0.25f);
            var part = 0.3f + 0.55f * (float)random.NextDouble();
            var drop = (height - 0.6f) * part;
            OBox(JB(Material.Cloth, tint), id, FlatsAt(x + (i - 1) * width / 3, head - 0.56f - drop / 2, z + outward * 0.012f), new float3(width / 3 - bar, drop, 0.006f));
        }
    }

    /// <summary>A thin black balcony rail across the front of a window.</summary>
    private void FlatsRail(float x, float z, float y, float width)
    {
        var id = float4x4.Identity;
        var rail = JB(Material.PaintedMetal, new float3(0.035f, 0.035f, 0.04f));
        OBox(rail, id, FlatsAt(x, y, z), new float3(width + 0.3f, 0.04f, 0.04f));
        OBox(rail, id, FlatsAt(x, y - 0.75f, z), new float3(width + 0.3f, 0.03f, 0.03f));
        for (var dx = -width / 2; dx <= width / 2 + 0.01f; dx += 0.3f)
            OBox(rail, id, FlatsAt(x + dx, y - 0.38f, z), new float3(0.016f, 0.75f, 0.016f));
    }

    // ------------------------------------------------------------------------------------------------ the stairwell

    private void BuildFlatsStairwell()
    {
        var walls = new float3(0.42f, 0.42f, 0.40f);
        var y1 = StairTop + 0.3f;
        // The floor under it, the four walls (the west one is the undercroft's east wall), the lid.
        FBox(UnderHalfX, -1, StairZ0 - StairWall, StairX1 + StairWall, 0, StairZ1 + StairWall, new float3(0.3f, 0.3f, 0.3f), Material.Floor);
        FBox(UnderHalfX, 0, StairZ0 - StairWall, StairX0, y1, StairZ1 + StairWall, walls, Material.Blockwork);
        FBox(StairX1, 0, StairZ0 - StairWall, StairX1 + StairWall, y1, StairZ1 + StairWall, walls, Material.Blockwork);
        FBox(StairX0, 0, StairZ0 - StairWall, StairX1, y1, StairZ0, walls, Material.Blockwork);
        FBox(StairX0, 0, StairZ1, StairX1, y1, StairZ1 + StairWall, walls, Material.Blockwork);
        FBox(StairX0, StairTop, StairZ0, StairX1, y1, StairZ1, new float3(0.34f, 0.34f, 0.33f), Material.Concrete);
        // The spine wall between the two flights.
        FBox(SpineX0, 0, StairFlightZ0, SpineX1, StairTop, StairFlightZ1, walls, Material.Blockwork);

        var steps = new float3(0.33f, 0.33f, 0.32f);
        var rise = StairStorey / 2 / StairRisers;
        var tread = (StairFlightZ1 - StairFlightZ0) / (StairRisers - 1);
        var id = float4x4.Identity;
        var rail = JB(Material.Steel, new float3(0.2f, 0.2f, 0.21f));
        for (var floor = 0; floor < StairStoreys; floor++)
        {
            var y = floor * StairStorey;
            for (var i = 1; i < StairRisers; i++)
            {
                // Up the east lane, going north...
                var t0 = StairFlightZ1 - (i - 1) * tread;
                var t1 = StairFlightZ1 - i * tread;
                FBox(SpineX1, y + i * rise - 0.2f, t1, StairX1, y + i * rise, t0, steps, Material.Concrete);
                // ...and on up the west lane, going south. (Its steps are at the same places, in reverse.)
                var s0 = StairFlightZ0 + (i - 1) * tread;
                var s1 = StairFlightZ0 + i * tread;
                FBox(StairX0, y + StairStorey / 2 + i * rise - 0.2f, s0, SpineX0, y + StairStorey / 2 + i * rise, s1, steps, Material.Concrete);
            }

            // The half landing at the north end and the floor's landing at the south end, the floor's number on the wall.
            FBox(StairX0, y + StairStorey / 2 - 0.2f, StairZ0, StairX1, y + StairStorey / 2, StairFlightZ0, steps, Material.Floor);
            FBox(StairX0, y + StairStorey - 0.2f, StairFlightZ1, StairX1, y + StairStorey, StairZ1, steps, Material.Floor);
            // How people get up them by themselves (NpcNav): up the east lane to the half landing, up the west lane to the next floor.
            const float eastLane = 11.3f, westLane = 9.9f, onLanding = 0.4f;
            var half = y + StairStorey / 2;
            AddStairFlight($"flight {floor * 2 + 1}", FlatsAt(eastLane, y, StairFlightZ1 + onLanding), FlatsAt(eastLane, y, StairFlightZ1), FlatsAt(eastLane, half, StairFlightZ0), FlatsAt(eastLane, half, StairFlightZ0 - onLanding),
                FBoxBetween(SpineX1, y, StairFlightZ0, StairX1, half, StairFlightZ1));
            AddStairFlight($"flight {floor * 2 + 2}", FlatsAt(westLane, half, StairFlightZ0 - onLanding), FlatsAt(westLane, half, StairFlightZ0), FlatsAt(westLane, y + StairStorey, StairFlightZ1), FlatsAt(westLane, y + StairStorey, StairFlightZ1 + onLanding),
                FBoxBetween(StairX0, half, StairFlightZ0, SpineX0, y + StairStorey, StairFlightZ1));
            AddLettering($"{floor + 1}", FlatsAt(StairX0 + 0.012f, y + StairStorey + 1.55f, 3.2f), new float3(1, 0, 0), 0.22f, Material.Plain, new float3(0.1f, 0.1f, 0.12f));

            // A handrail up each flight, against the spine wall.
            OCyl(rail, id, FlatsAt(SpineX1 + 0.06f, y + 0.9f, StairFlightZ1), FlatsAt(SpineX1 + 0.06f, y + 0.9f + StairStorey / 2, StairFlightZ0), 0.022f, 0.022f, 8);
            OCyl(rail, id, FlatsAt(SpineX0 - 0.06f, y + 0.9f + StairStorey / 2, StairFlightZ0), FlatsAt(SpineX0 - 0.06f, y + 0.9f + StairStorey, StairFlightZ1), 0.022f, 0.022f, 8);

            // A flat's front door on each floor's landing but the top one (that one can open: see BuildFlatsDoors).
            if (floor < StairStoreys - 1)
                FlatsFlatDoor(y + StairStorey, floor);
        }


        // The stairs stop at the top landing: wall off the top of the lane that would carry on up, or you'd step off into the flight below.
        FBox(SpineX1, StairStoreys * StairStorey - 0.2f, StairFlightZ1 - 0.12f, StairX1, StairTop, StairFlightZ1, walls, Material.Blockwork);

        // The lamps: a caged bulkhead light on every floor (their lights are FlatsLights; the shader flickers both).
        var lit = JB(Material.Emissive, new float3(1.0f, 0.82f, 0.55f));
        var cage = JB(Material.Steel, new float3(0.14f, 0.14f, 0.15f));
        for (var floor = 0; floor <= StairStoreys; floor++)
        {
            var at = FlatsAt(10.6f, floor * StairStorey + 2.5f, StairZ1 - 0.05f);
            OBox(lit, id, at, new float3(0.2f, 0.11f, 0.08f));
            OBox(cage, id, at + new float3(0, 0, -0.045f), new float3(0.24f, 0.015f, 0.01f));
            OBox(cage, id, at + new float3(0, 0.06f, 0.01f), new float3(0.26f, 0.015f, 0.1f));
            OBox(cage, id, at + new float3(0, -0.06f, 0.01f), new float3(0.26f, 0.015f, 0.1f));
        }

        // Rubbish in the corner under the first half landing.
        OBox(JB(Material.Plain, new float3(0.05f, 0.05f, 0.055f)), id, FlatsAt(9.75f, 0.2f, StairZ0 + 0.35f), new float3(0.46f, 0.4f, 0.5f));
    }

    /// <summary>A flat's front door on a landing, in the east wall: shut for good (it's part of the wall).</summary>
    private void FlatsFlatDoor(float floorY, int number)
    {
        var id = float4x4.Identity;
        var x = StairX1 - 0.012f;
        var doorTint = number % 2 == 0 ? new float3(0.24f, 0.13f, 0.07f) : new float3(0.10f, 0.16f, 0.13f);
        OBox(JB(Material.Plain, doorTint), id, FlatsAt(x, floorY + FlatsDoorH / 2, TopDoorZ), new float3(0.024f, FlatsDoorH, FlatsDoorW));
        OBox(JB(Material.Plain, new float3(0.17f, 0.17f, 0.16f)), id, FlatsAt(x - 0.006f, floorY + FlatsDoorH / 2 + 0.03f, TopDoorZ), new float3(0.03f, FlatsDoorH + 0.08f, FlatsDoorW + 0.14f));
        OBox(JB(Material.Plain, doorTint), id, FlatsAt(x - 0.012f, floorY + FlatsDoorH / 2, TopDoorZ), new float3(0.03f, FlatsDoorH, FlatsDoorW));
        OBox(JB(Material.Steel, new float3(0.55f, 0.55f, 0.56f)), id, FlatsAt(x - 0.04f, floorY + 1.02f, TopDoorZ - 0.36f), new float3(0.02f, 0.04f, 0.12f));
    }

    // ------------------------------------------------------------------------------------------------ the doors

    private const float TopDoorZ = 2.75f, TopHallX1 = 14f, TopHallHeight = 2.4f;

    private PrisonDoor? _flatsStairDoor, _flatsTopDoor;

    /// <summary>"[E] Open door" / "[E] Close door" by the stairwell's door, "Locked" by the flat's on the top landing, otherwise null.</summary>
    public string? FlatsDoorPrompt { get; private set; }

    private void BuildFlatsDoors()
    {
        // The steel door from the undercroft to the stairwell, in pale paint, hinged on its south edge, opening towards you.
        float z0 = FlatsDoorZ - FlatsDoorW / 2, z1 = FlatsDoorZ + FlatsDoorW / 2;
        CarveOpening(FBoxBetween(UnderHalfX - 0.02f, 0, z0, StairX0 + 0.02f, FlatsDoorH, z1));
        // The opening's reveal, and the frame round it.
        var id = float4x4.Identity;
        var frame = JB(Material.Plain, new float3(0.34f, 0.34f, 0.33f));
        foreach (var z in (ReadOnlySpan<float>)[z0 - 0.04f, z1 + 0.04f])
            OBox(frame, id, FlatsAt(UnderHalfX - 0.015f, FlatsDoorH / 2 + 0.03f, z), new float3(0.05f, FlatsDoorH + 0.06f, 0.08f));
        OBox(frame, id, FlatsAt(UnderHalfX - 0.015f, FlatsDoorH + 0.03f, FlatsDoorZ), new float3(0.05f, 0.08f, FlatsDoorW + 0.16f));

        const float thick = 0.05f;
        var c = new float3(thick / 2, FlatsDoorH / 2, -FlatsDoorW / 2);
        var leaf = HingedLeaf(FlatsAt(UnderHalfX, 0, z1), float.DegreesToRadians(95f), c, new float3(thick, FlatsDoorH, FlatsDoorW));
        var pale = new float3(0.72f, 0.72f, 0.68f);
        OBox(leaf.B(Material.Plain, pale), id, c, new float3(thick, FlatsDoorH - 0.01f, FlatsDoorW - 0.01f));
        // Scuffed kick plate, a wired-glass light, a pull handle on the undercroft's side, a lever on the stair side.
        OBox(leaf.B(Material.Plain, new float3(0.28f, 0.28f, 0.28f)), id, new float3(-0.004f, 0.16f, -FlatsDoorW / 2), new float3(0.01f, 0.3f, FlatsDoorW - 0.05f));
        OBox(leaf.B(Material.Glass, new float3(0.06f, 0.07f, 0.08f)), id, new float3(thick / 2, 1.55f, -FlatsDoorW / 2), new float3(thick + 0.006f, 0.42f, 0.22f));
        var steel = leaf.B(Material.Plain, new float3(0.46f, 0.46f, 0.47f));
        OBox(steel, id, new float3(-0.045f, 1.05f, -FlatsDoorW + 0.1f), new float3(0.022f, 0.3f, 0.022f));
        OBox(steel, id, new float3(-0.024f, 1.2f, -FlatsDoorW + 0.1f), new float3(0.05f, 0.02f, 0.02f));
        OBox(steel, id, new float3(-0.024f, 0.9f, -FlatsDoorW + 0.1f), new float3(0.05f, 0.02f, 0.02f));
        OBox(steel, id, new float3(thick + 0.03f, 1.02f, -FlatsDoorW + 0.14f), new float3(0.02f, 0.02f, 0.12f));
        _flatsStairDoor = NewDoor("flats stairs", 0.8f, leaf);
        MoveDoor(_flatsStairDoor, false, instant: true);

        // The flat's door on the top landing: he has the key, you don't. Hinged on its south edge, opening away into the flat.
        var y = StairStoreys * StairStorey;
        float t0 = TopDoorZ - FlatsDoorW / 2, t1 = TopDoorZ + FlatsDoorW / 2;
        CarveOpening(FBoxBetween(StairX1 - 0.02f, y, t0, StairX1 + StairWall + 0.02f, y + FlatsDoorH, t1));
        foreach (var z in (ReadOnlySpan<float>)[t0 - 0.05f, t1 + 0.05f])
            OBox(frame, id, FlatsAt(StairX1 - 0.012f, y + FlatsDoorH / 2 + 0.03f, z), new float3(0.03f, FlatsDoorH + 0.06f, 0.1f));
        OBox(frame, id, FlatsAt(StairX1 - 0.012f, y + FlatsDoorH + 0.03f, TopDoorZ), new float3(0.03f, 0.08f, FlatsDoorW + 0.2f));
        var cTop = new float3(-thick / 2 + 0.03f, FlatsDoorH / 2, -FlatsDoorW / 2);
        var topLeaf = HingedLeaf(FlatsAt(StairX1 + 0.03f, y, t1), float.DegreesToRadians(-92f), cTop, new float3(thick, FlatsDoorH, FlatsDoorW));
        var brown = new float3(0.24f, 0.13f, 0.07f);
        OBox(topLeaf.B(Material.Plain, brown), id, cTop, new float3(thick, FlatsDoorH - 0.01f, FlatsDoorW - 0.01f));
        OBox(topLeaf.B(Material.Plain, new float3(0.55f, 0.55f, 0.56f)), id, new float3(-0.035f, 1.02f, -FlatsDoorW + 0.1f), new float3(0.02f, 0.04f, 0.12f));
        OBox(topLeaf.B(Material.Plain, new float3(0.72f, 0.7f, 0.62f)), id, new float3(-0.026f, 1.5f, -FlatsDoorW / 2), new float3(0.004f, 0.08f, 0.14f));
        _flatsTopDoor = NewDoor("flats top", 0.7f, topLeaf);
        MoveDoor(_flatsTopDoor, false, instant: true);

        // The dark hall behind it: floor, ceiling, walls, all he needs to vanish into.
        var dark = new float3(0.06f, 0.055f, 0.05f);
        FBox(StairX1 + StairWall, y - 0.2f, t0 - 0.5f, TopHallX1, y, t1 + 0.5f, dark, Material.Plain);
        FBox(StairX1 + StairWall, y + TopHallHeight, t0 - 0.5f, TopHallX1, y + TopHallHeight + 0.05f, t1 + 0.5f, dark, Material.Plain);
        FBox(StairX1 + StairWall, y, t0 - 0.55f, TopHallX1, y + TopHallHeight, t0 - 0.5f, dark, Material.Plain);
        FBox(StairX1 + StairWall, y, t1 + 0.5f, TopHallX1, y + TopHallHeight, t1 + 0.55f, dark, Material.Plain);
        FBox(TopHallX1, y, t0 - 0.5f, TopHallX1 + 0.05f, y + TopHallHeight, t1 + 0.5f, dark, Material.Plain);
    }

    /// <summary>Whether the stairwell's door is shut (Rook's script opens it; so can you).</summary>
    public bool FlatsStairDoorShut => _flatsStairDoor is { Target: < 0.5f };

    /// <summary>Opens or shuts the stairwell's door (<paramref name="top"/>: the flat's door on the top landing).</summary>
    public void SetFlatsDoor(bool top, bool open)
    {
        if ((top ? _flatsTopDoor : _flatsStairDoor) is { } door && MoveDoor(door, open))
            Log.Write($"[DOOR] {(open ? "Opening" : "Shutting")} {(top ? "the door on the top landing" : "the stairwell's door")} at the flats");
    }

    /// <summary>The player is in the way of the stairwell's door: it won't be shut on them.</summary>
    public static bool InFlatsDoorway(float3 player)
    {
        var p = player - FlatsOrigin;
        return p.X > UnderHalfX - 1.1f && p.X < StairX0 + 0.5f && float.Abs(p.Z - FlatsDoorZ) < 1.0f && p.Y < 3f;
    }

    /// <summary>Near the stairwell's door, E opens or shuts it. The door on the top landing is someone else's: it's locked.</summary>
    public void UpdateFlatsDoors(float dt, bool usePressed, float3 player)
    {
        if (_flatsStairDoor is not { } stairs || _flatsTopDoor is not { } top)
            return;
        var p = player - FlatsOrigin;
        FlatsDoorPrompt = null;
        if (p.Y < FasciaTop && p.Y > -1f && p.X > UnderHalfX - 1.8f && p.X < StairX0 + 1.3f && float.Abs(p.Z - FlatsDoorZ) < 1.35f)
        {
            var open = stairs.Target > 0.5f;
            FlatsDoorPrompt = open ? "[E] Close door" : "[E] Open door";
            if (usePressed && !(open && InFlatsDoorway(player) && p.X > UnderHalfX - 0.5f && p.X < StairX0 + 0.35f) && MoveDoor(stairs, !open))
                Log.Write(open ? "[DOOR] Shutting the stairwell's door" : "[DOOR] Opening the stairwell's door");
        }
        else if (top.Target < 0.5f && p.Y > StairStoreys * StairStorey - 0.5f && p.Y < StairTop && p.X > StairX1 - 1.3f && float.Abs(p.Z - TopDoorZ) < 1.0f)
        {
            FlatsDoorPrompt = "Locked";
        }
        StepDoor(stairs, dt);
        StepDoor(top, dt);
    }

    private void DrawFlatsDoors(Renderer renderer)
    {
        if (_flatsStairDoor is { } stairs)
            DrawDoor(renderer, stairs);
        if (_flatsTopDoor is { } top)
            DrawDoor(renderer, top);
    }

    // ------------------------------------------------------------------------------------------------ round the block

    private void BuildFlatsSurroundings(Random random)
    {
        var id = float4x4.Identity;
        var leaves = JB(Material.Bush, new float3(0.13f, 0.27f, 0.08f));
        var wallTint = new float3(0.38f, 0.39f, 0.40f);

        // The planters each side of the ramp: grey concrete walls, shrubs in them, against the block's front.
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            float inner = side * RampHalfX, outer = side * (RampHalfX + 0.22f), far = side * (UnderHalfX + 7f);
            // Its wall along the ramp, as high as the planter's soil; it stands the height of the ramp above the tarmac at the block.
            FBox(inner, 0, FlatsHalfZ, outer, 1.16f, RampEndZ - 0.1f, wallTint, Material.Concrete);
            FBox(outer, FlatsStreetY, RampEndZ - 0.3f, far, 1.16f, RampEndZ - 0.1f, wallTint, Material.Concrete);
            // (Inside, the soil's up near the top of the wall.)
            FBox(outer, FlatsStreetY, FlatsHalfZ, far, 1.02f, RampEndZ - 0.3f, new float3(0.1f, 0.08f, 0.06f), Material.Gravel);
            for (var i = 0; i < 170; i++)
            {
                var x = side * (RampHalfX + 0.5f + (UnderHalfX + 6f - RampHalfX) * (float)random.NextDouble());
                var z = FlatsHalfZ + 0.5f + (RampEndZ - FlatsHalfZ - 1.2f) * (float)random.NextDouble();
                var s = 0.25f + 0.28f * (float)random.NextDouble();
                var mound = 0.35f + 1.05f * (float)random.NextDouble();
                leaves.Blob(FlatsAt(x, 1.02f + mound, z), new float3(0.9f, 0.75f, 0.9f) * s, random, 7, 5);
            }
            // It stops short of the columns by the undercroft's corner, closing the sides of the way in.
            FBox(inner, 0, FlatsHalfZ - 0.05f, side * UnderHalfX, FlatsStreetY, FlatsHalfZ + 0.02f, wallTint, Material.Concrete);
        }
        // Keep to the street: the end of the block, and the shrubs (you'd need a long jump to clear the walls).
        FCollider(-RampHalfX - 0.22f - 18f, 1.1f, FlatsHalfZ, -RampHalfX - 0.22f, 4f, RampEndZ);
        FCollider(RampHalfX + 0.22f, 1.1f, FlatsHalfZ, RampHalfX + 0.22f + 18f, 4f, RampEndZ);

        // The young trees on the forecourt, staked, in their squares of earth. The utility box.
        FlatsTree(8.1f, 12.5f, random);
        FlatsTree(-9.2f, 13.1f, random);
        FlatsTree(16.5f, 13f, random);
        FlatsTree(-17.5f, 12.6f, random);
        var box = new float3(0.56f, 0.56f, 0.53f);
        FBox(6.5f, FlatsStreetY, 12f, 7.25f, FlatsStreetY + 1.08f, 12.42f, box, Material.PaintedMetal);
        OBox(JB(Material.Concrete, box * 0.8f), id, FlatsAt(6.875f, FlatsStreetY + 1.1f, 12.21f), new float3(0.84f, 0.05f, 0.5f));
        OBox(JB(Material.Plain, new float3(0.2f, 0.2f, 0.2f)), id, FlatsAt(6.875f, FlatsStreetY + 0.75f, 12.425f), new float3(0.3f, 0.2f, 0.004f));
        // A car parked at the kerb, if its model's there.
        if (TryModel("sedan") is { } sedan)
        {
            AddModel(sedan, float4x4.CreateRotationY(float.Pi / 2) * float4x4.CreateTranslation(FlatsAt(-3.5f, FlatsStreetY - 0.13f, 19.4f)), new float3(0.48f, 0.5f, 0.54f));
            FCollider(-5.9f, FlatsStreetY - 0.13f, 18.5f, -1.1f, FlatsStreetY + 1.3f, 20.3f);
        }

        // At the rear: hedges round the grass, a low grey wall by the tarmac, and more of the estate beyond.
        var hedge = JB(Material.Bush, new float3(0.14f, 0.26f, 0.09f));
        for (var x = -38f; x <= 38f; x += 1.3f)
        {
            if (float.Abs(x) > 2.5f)
                hedge.Blob(FlatsAt(x, 0.75f, -24f + 0.4f * float.Sin(x)), new float3(0.95f, 0.95f, 0.9f), random, 8, 5);
        }
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            FBox(side * UnderHalfX, 0, -FlatsHalfZ - 4, side * (UnderHalfX + 0.25f), 0.45f, -FlatsHalfZ, wallTint, Material.Concrete);
            for (var z = -FlatsHalfZ + 0.3f; z > -24f; z -= 1.3f)
                hedge.Blob(FlatsAt(side * (FlatsHalfX + 1.5f), 0.75f, z), new float3(0.9f, 0.95f, 0.9f), random, 8, 5);
            // The block's own hedge against its rear flanks.
            for (var x = UnderHalfX + 1.2f; x < FlatsHalfX; x += 1.4f)
                hedge.Blob(FlatsAt(side * x, 0.65f, -FlatsHalfZ - 0.6f), new float3(0.8f, 0.8f, 0.6f), random, 8, 5);
        }
        FlatsTree(-5f, -13f, random, 0f);
        FlatsTree(12f, -15f, random, 0f);
        FlatsTree(-17f, -16f, random, 0f);

        // Blocks round about, plain masses with the same windows: across the street, and past the hedge at the rear.
        FBox(-40, FlatsStreetY, 30, 40, 16f, 40, FlatsBrick * 0.9f, Material.Brick);
        FlatsFacade(30f, -1f, 40f, FlatsStreetY + 0.2f, 5, random, blueStrips: false);
        FBox(-38, 0, -44, 38, 13f, -34, FlatsBrick * 0.8f, Material.Brick);
        FlatsFacade(-34f, 1f, 38f, 0.3f, 4, random, blueStrips: false);

        // ...and at the ends of the street, closing the view.
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            FBox(side * 62f, 0, -44, side * 74f, 15f, 40, FlatsBrick * 0.85f, Material.Brick);

        // The edges of what you can walk on.
        FCollider(-FlatsHalfX - 0.3f, 0, -30, -FlatsHalfX, 6, FlatsHalfZ);
        FCollider(FlatsHalfX, 0, -30, FlatsHalfX + 0.3f, 6, FlatsHalfZ);
        FCollider(-FlatsHalfX, 0, -24.3f, FlatsHalfX, 6, -24f);
        FCollider(-26.3f, 0, FlatsHalfZ, -26f, 6, 30);
        FCollider(26f, 0, FlatsHalfZ, 26.3f, 6, 30);
        FCollider(-26f, 0, 28f, 26f, 6, 30);
    }

    /// <summary>A young tree: a slender trunk, a few branches, a loose crown of leaf blobs, staked, in its square of earth.</summary>
    private void FlatsTree(float x, float z, Random random, float ground = FlatsStreetY)
    {
        var id = float4x4.Identity;
        var bark = JB(Material.Bark, new float3(0.32f, 0.28f, 0.22f));
        var h = 3.2f + 1.2f * (float)random.NextDouble();
        OCyl(bark, id, FlatsAt(x, ground - 0.1f, z), FlatsAt(x, ground + h, z), 0.09f, 0.04f, 9);
        var leaves = JB(Material.Bush, new float3(0.2f, 0.34f, 0.1f) * (0.85f + 0.3f * (float)random.NextDouble()));
        for (var i = 0; i < 7; i++)
        {
            var a = (float)random.NextDouble() * float.Tau;
            var r = 0.3f + 0.6f * (float)random.NextDouble();
            var y = ground + h - 0.7f + 1.3f * (float)random.NextDouble();
            var c = FlatsAt(x + float.Cos(a) * r, y, z + float.Sin(a) * r);
            OCyl(bark, id, FlatsAt(x, ground + h - 1f, z), c, 0.025f, 0.012f, 6);
            leaves.Blob(c, new float3(0.72f, 0.62f, 0.72f) * (0.8f + 0.4f * (float)random.NextDouble()), random, 8, 5);
        }
        OCyl(JB(Material.Wood, new float3(0.5f, 0.4f, 0.28f)), id, FlatsAt(x + 0.16f, ground, z), FlatsAt(x + 0.16f, ground + 1.5f, z), 0.025f, 0.025f, 6);
        OBox(JB(Material.Plain, new float3(0.09f, 0.07f, 0.05f)), id, FlatsAt(x, ground + 0.004f, z), new float3(1f, 0.008f, 1f));
        AddStatic(FBoxBetween(x - 0.1f, ground, z - 0.1f, x + 0.1f, ground + h, z + 0.1f), default, visible: false);
    }
}
