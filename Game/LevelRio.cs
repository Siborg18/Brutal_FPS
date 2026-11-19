using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Rio de Janeiro: a favela on a steep hillside, four kilometres down (the shader's area 13: y &lt; -3500), near x = z = 0
/// like the flats so its detail keeps full float precision. It's reached by the magenta portal, in the wall at the dead
/// end of the bottom alley, from the slate portal hidden in the Arctic's second container.
/// <code>
///     looking down, +z is up the hill; local coordinates from RioOrigin
///
///     z 56-63  block 6: the top row of houses, the hill and the forest above
///     z 54-56  terrace 6 (y 16.2)  &lt;- the laje: a flat roof you can walk out onto, over the roofs and across the valley
///     z 47-54  block 5             |
///       ...                        | stair alleys (15 risers of 18 cm) climb each block between the houses
///     z 2-9    block 0             |
///     z 0-2    terrace 0 (y 0): (magenta portal) -&gt; the bottom alley, walled in by the backs of the houses below it
///     z -7-0   block -1: the row below, then the hill falls away to the valley and the favela on the far side
/// </code>
/// Every terrace is a flat alley 2 m wide running across the hill, walled in on both sides by houses two to four storeys
/// high: rendered blockwork painted every colour, or bare brick in a grey concrete frame, with barred windows, shutters,
/// balconies over the alley, water tanks and rebar on the roofs, and wires strung everywhere. Stairways climb from one
/// terrace to the next. You can't leave the alleys (the houses are solid), except onto the one roof at the top, into the
/// little hiding rooms behind open doors (Game/LevelRioHideouts.cs), and through the stair houses, one in every block,
/// whose stairs inside climb to a back door on the alley above (Game/LevelRioStairHouses.cs).
/// It's always a bright afternoon here: the outback's sky and sun, turned so the sun stands over the north-east
/// (<see cref="RioSunDirection"/>), with a shadow map of its own.
/// </summary>
public sealed partial class Level
{
    public static readonly float3 RioOrigin = new(0, -4000, 0);

    /// <summary>Below this height a point is in Rio (see <see cref="ZoneAt"/> and the shader's areaOf).</summary>
    public const float RioBelowY = -3500f;

    /// <summary>A point in Rio, from its local coordinates.</summary>
    public static float3 RioAt(float x, float y, float z) => RioOrigin + new float3(x, y, z);

    // The terraces and the blocks of houses between them.
    private const float RioAlleyW = 2f;         // a terrace's alley, across the hill
    private const float RioBlockD = 7f;         // a block of houses, up the hill
    private const float RioRise = 2.7f;         // from one terrace to the next
    private const int RioRisers = 15;           // 18 cm each: well within what you can step up
    private const int RioTerraces = 7;
    private const float RioStorey = 2.8f;
    private const float RioHalfX = 28f;         // where the alleys end, walled off
    private const float RioCapX = 34f;          // the outside of the houses that wall them off

    private static float RioTerraceZ(int k) => k * (RioAlleyW + RioBlockD);
    private static float RioTerraceY(int k) => k * RioRise;

    // The stair alleys that climb each block (x from, x to); the first in every block is the main one, straight up the middle.
    private static readonly (float X0, float X1)[][] RioStairs =
    [
        [(-1f, 1f), (-15.4f, -14.0f), (21.6f, 22.8f)],
        [(-1f, 1f), (-15.0f, -13.6f), (12.2f, 13.6f)],
        [(-1f, 1f), (-24.2f, -23.0f), (12.6f, 14.0f)],
        [(-1f, 1f), (-15.6f, -14.2f), (12.0f, 13.4f)],
        [(-1f, 1f), (-15.2f, -13.8f), (21.2f, 22.4f)],
        [(-1f, 1f), (-24.4f, -23.2f), (12.4f, 13.8f)],
    ];

    // The laje: a flat roof in block 5, flush with terrace 6, with a low wall along its front. The houses below it, in
    // the view down the hill, are kept low so you can see over them.
    private const float LajeX0 = 3.5f, LajeX1 = 9.0f;
    private const float ViewX0 = 2f, ViewX1 = 12f;

    // A little garden in block 2 where a house would be, a retaining wall in front, the alley above behind a wall.
    private const float GardenX0 = -10f, GardenX1 = -5.5f;

    // The portal's pocket: the bottom alley widens at its west end, where the magenta portal is.
    private const float RioPocketZ0 = -1.2f, RioPocketZ1 = 2f, RioPocketX1 = -24f;
    private const float RioPortalVoid = 1.0f;   // behind the portal's wall

    /// <summary>The wall the magenta portal is cut into (a rendered block wall), with the empty space behind it for your body as you walk in.</summary>
    public const float RioPortalWall = 0.3f;

    /// <summary>The magenta portal: in the wall at the west end of the bottom alley, looking east along it.</summary>
    public static float3 RioPortalFloorPoint => RioAt(-RioHalfX, 0, (RioPocketZ0 + RioPocketZ1) / 2);
    public static float3 RioPortalNormal => new(1, 0, 0);
    public static readonly float3 RioPortalColor = new(1.0f, 0.22f, 0.78f);   // (magenta: Level.WarehouseEastPortalColor, the colour it always was)
    public static readonly float3 RioPortalWallTint = new(0.29f, 0.66f, 0.63f);

    /// <summary>Where you start with BRUTALFPS_START=rio: in front of the magenta portal, looking east along the bottom alley.</summary>
    public static float3 RioStart => RioPortalFloorPoint + new float3(2.2f, 0, 0);
    public const float RioStartYaw = float.Pi / 2;

    /// <summary>
    /// Rio's sun: as high as the outback's (the sky is the outback's, turned to match), but over the north-east, down the
    /// hill and off to the side, so it lights the house fronts you see as you climb and shines along the stairways.
    /// </summary>
    public static float3 RioSunDirection
    {
        get
        {
            var s = float3.Normalize(OutbackSunDirection);
            var h = float.Sqrt(s.X * s.X + s.Z * s.Z);
            return new float3(h * 0.55f, s.Y, -h * 0.835f);
        }
    }

    public static float3 RioShadowCenter => RioAt(0, 8, 28);
    public const float RioShadowHalfExtent = 54f;

    /// <summary>All of Rio (its reverb room).</summary>
    public static Aabb RioGrounds => BoxBetween(RioAt(-600, -200, -700), RioAt(600, 480, 500));

    /// <summary>Rio is all out in the open, but for its hiding places' dark little rooms (Game/LevelRioHideouts.cs; filled in as the level is built).</summary>
    public static ShadeVolume[] RioShadeVolumes => _rioShade;

    // ------------------------------------------------------------------------------------------------ night

    private static bool RioNight => TimeOfDay.IsNight(DayNightLevel.Rio);

    private static readonly float3 RioBulbColor = new(1.0f, 0.74f, 0.42f);   // bare incandescent bulbs, warm orange
    private static readonly float[] RioLampXs = [-25.5f, -18f, -10.5f, -4f, 3.5f, 11f, 18.5f, 25.5f];

    /// <summary>
    /// Rio's lamps: a bare bulb under a little shade, hung on a wire strung across every alley (every 7 m or so) and
    /// halfway up every stairway, about 3 m over the ground. Each lights only its own alley or stairway (its box), so
    /// none of it shows through the houses. They're there by day too, just not lit.
    /// </summary>
    private static IEnumerable<(float3 Bulb, Aabb Bounds, float3 WireA, float3 WireB, float Intensity)> RioLampPlaces()
    {
        for (var k = 0; k < RioTerraces; k++)
        {
            var z = RioTerraceZ(k);
            var y = RioTerraceY(k);
            foreach (var x in RioLampXs)
            {
                var bulb = new float3(x, y + 2.95f, z + RioAlleyW / 2);
                var box = BoxBetween(RioAt(x - 11f, y - 1f, z - 0.12f), RioAt(x + 11f, y + 12f, z + RioAlleyW + 0.12f));
                yield return (bulb, box, new float3(x, y + 3.5f, z), new float3(x, y + 3.5f, z + RioAlleyW), 5f);
            }
        }
        for (var block = 0; block < RioStairs.Length; block++)
        {
            var zs = RioTerraceZ(block) + RioAlleyW;
            var y = RioTerraceY(block);
            foreach (var (x0, x1) in RioStairs[block])
            {
                var zm = zs + RioBlockD / 2;
                var ym = y + RioRise / 2 + 0.1f;
                var bulb = new float3((x0 + x1) / 2, ym + 2.9f, zm);
                var box = BoxBetween(RioAt(x0 - 0.15f, y - 0.5f, zs - 0.2f), RioAt(x1 + 0.15f, y + RioRise + 10f, zs + RioBlockD + 0.2f));
                yield return (bulb, box, new float3(x0, ym + 3.45f, zm), new float3(x1, ym + 3.45f, zm), 2.6f);   // (between walls a stairway's width apart)
            }
        }
    }

    /// <summary>Rio's lamps as lights, for people's eyes (<see cref="LightAt"/>): none by day.</summary>
    private static IEnumerable<LocalLight> RioNightLamps =>
        RioNight ? RioLampPlaces().Select(l => new LocalLight(RioOrigin + l.Bulb, RioBulbColor, l.Intensity, 10f, l.Bounds)) : [];

    /// <summary>Rio's lamps for the renderer (they share the police station's light buffer): none by day.</summary>
    public static OfficeLight[] RioLights =>
        RioNight ? [.. RioLampPlaces().Select(l => new OfficeLight(RioOrigin + l.Bulb, RioBulbColor, l.Intensity, 10f, l.Bounds, Panel: false))] : [];

    private void BuildRioLamps()
    {
        var shade = new float3(0.16f, 0.2f, 0.17f);
        foreach (var (bulb, _, a, b, _) in RioLampPlaces())
        {
            RioWire(a, b, 0.12f);
            var hang = (a + b) / 2 - new float3(0, 0.12f, 0);   // (the wire's sag at its middle)
            RioBeam(hang, bulb + new float3(0, 0.15f, 0), 0.012f, RioWireTint, Material.Plain);
            RioCylinder(bulb + new float3(0, 0.08f, 0), bulb + new float3(0, 0.15f, 0), 0.13f, shade, Material.PaintedMetal, 12);
            RioCylinder(bulb - new float3(0, 0.03f, 0), bulb + new float3(0, 0.08f, 0), 0.035f,
                RioNight ? RioBulbColor : new float3(0.78f, 0.77f, 0.72f), RioNight ? Material.Emissive : Material.Plain, 8);
        }
    }

    /// <summary>A number 0..1 that stays the same for the same place (so day and night lay the favela out alike).</summary>
    private static float RioHash(float a, float b, float c)
    {
        var s = float.Sin(a * 12.9898f + b * 78.233f + c * 37.719f) * 43758.547f;
        return s - float.Floor(s);
    }

    // A lit window at night: most a warm lamp, some a cold tube, a few the blue flicker of a TV.
    private static readonly float3[] RioRoomLight = [new(0.70f, 0.52f, 0.30f), new(0.62f, 0.45f, 0.24f), new(0.80f, 0.64f, 0.38f), new(0.52f, 0.57f, 0.58f), new(0.17f, 0.22f, 0.36f)];

    private static float3? RioLitWindow(float a, float b, float c, float chance)
    {
        if (!RioNight)
            return null;
        var h = RioHash(a, b, c);
        return h < chance ? RioRoomLight[(int)(h / chance * RioRoomLight.Length) % RioRoomLight.Length] * (0.75f + 0.25f * float.Floor(RioHash(c, a, b) * 3f)) : null;   // (a few brightnesses: each colour is a batch)
    }

    // Paint: every colour, as in the photos.
    private static readonly float3[] RioPaint =
    [
        new(0.29f, 0.66f, 0.63f), // teal
        new(0.90f, 0.47f, 0.60f), // pink
        new(0.64f, 0.80f, 0.32f), // lime
        new(0.96f, 0.80f, 0.27f), // yellow
        new(0.40f, 0.64f, 0.86f), // sky blue
        new(0.93f, 0.55f, 0.22f), // orange
        new(0.90f, 0.89f, 0.84f), // white
        new(0.92f, 0.62f, 0.48f), // salmon
        new(0.65f, 0.55f, 0.80f), // lilac
        new(0.33f, 0.64f, 0.40f), // green
        new(0.82f, 0.28f, 0.24f), // red
        new(0.94f, 0.87f, 0.68f), // cream
        new(0.22f, 0.72f, 0.78f), // turquoise
        new(0.66f, 0.87f, 0.72f), // mint
    ];
    private static readonly float3[] RioDoorPaint =
    [
        new(0.15f, 0.30f, 0.62f), new(0.12f, 0.45f, 0.30f), new(0.62f, 0.14f, 0.12f), new(0.45f, 0.46f, 0.47f),
        new(0.86f, 0.85f, 0.80f), new(0.85f, 0.62f, 0.12f), new(0.20f, 0.20f, 0.21f),
    ];
    private static readonly float3 RioBrickTint = new(0.66f, 0.34f, 0.22f);
    private static readonly float3 RioRender = new(0.63f, 0.62f, 0.59f);      // bare grey render
    private static readonly float3 RioConcrete = new(0.57f, 0.56f, 0.53f);
    private static readonly float3 RioPaving = new(0.52f, 0.51f, 0.48f);
    private static readonly float3 RioTankBlue = new(0.10f, 0.32f, 0.68f);
    private static readonly float3 RioWireTint = new(0.035f, 0.035f, 0.035f);
    private static readonly float3 RioGlassTint = new(0.10f, 0.12f, 0.13f);
    private static readonly float3 RioBarTint = new(0.13f, 0.13f, 0.13f);
    private static readonly float3 RioFrameTint = new(0.78f, 0.78f, 0.75f);
    private static readonly float3 RioLeafTint = new(0.20f, 0.36f, 0.13f);
    private static readonly float3 RioGrassTint = new(0.34f, 0.42f, 0.20f);

    /// <param name="Block">-1 below the bottom alley, 0-5 between the terraces, 6 above the top one.</param>
    private readonly record struct RioHouse(int Block, float X0, float X1, float Z0, float Z1, float Base, float Top, bool Garden = false);

    // A face of a house: its plane is at Coord on Axis (0 = x, 2 = z), facing out towards Sign; it spans U0..U1 along
    // the other horizontal axis, from Ground (what's in front of it) up to Top.
    private readonly record struct RioFace(int Axis, float Coord, float Sign, float U0, float U1, float Ground, float Top);

    private Dictionary<(float, float, float, Material), MeshBuilder>? _rioBatch;
    private readonly List<RioHouse> _rioHouses = [];

    private void BuildRio()
    {
        var random = new Random(1565);   // (the year Rio was founded)
        _rioBatch = [];
        PlanRioHouses(random);
        _rioHidingPlaces.Clear();
        _rioShade = [];
        PickRioHideouts();   // (no rolls: the favela looks as it did)
        PickRioStairHouses();
        BuildRioAlleys(random);
        foreach (var house in _rioHouses)
            BuildRioHouse(house, random);
        BuildRioEndCaps(random);
        BuildRioPortalEnd();
        BuildRioLaje(random);
        BuildRioGarden(random);
        BuildRioWires(random);
        BuildRioLamps();
        BuildRioBackdrop(random);

        foreach (var ((r, g, b, material), builder) in _rioBatch)
            _props.Add(new Prop(builder.Build(), float4x4.Identity, new float3(r, g, b), material));
        _rioBatch = null;
    }

    // ------------------------------------------------------------------------------------------------ building blocks

    private static float3 RioL(float x, float y, float z) => RioAt(x, y, z);

    private MeshBuilder RioBatch(float3 tint, Material material)
    {
        var key = (tint.X, tint.Y, tint.Z, material);
        if (!_rioBatch!.TryGetValue(key, out var builder))
            _rioBatch[key] = builder = new MeshBuilder();
        return builder;
    }

    /// <summary>A box (local coordinates) drawn in its colour's batch, with nothing to bump into.</summary>
    private void RioBox(float3 a, float3 b, float3 tint, Material material)
    {
        var min = float3.Min(a, b);
        var max = float3.Max(a, b);
        if (max.X - min.X < 1e-4f || max.Y - min.Y < 1e-4f || max.Z - min.Z < 1e-4f)
            return;
        RioBatch(tint, material).Box(RioOrigin + (min + max) / 2, max - min);
    }

    /// <summary>A box you bump into: drawn in its batch, with an invisible box for the player.</summary>
    private void RioSolid(float3 a, float3 b, float3 tint, Material material)
    {
        RioBox(a, b, tint, material);
        AddStatic(BoxBetween(RioOrigin + a, RioOrigin + b), default, visible: false);
    }

    /// <summary>A square bar from <paramref name="a"/> to <paramref name="b"/> (local), <paramref name="thickness"/> across.</summary>
    private void RioBeam(float3 a, float3 b, float thickness, float3 tint, Material material)
    {
        var d = b - a;
        var length = d.Length();
        if (length < 1e-4f)
            return;
        d /= length;
        var u = float.Abs(d.Y) > 0.95f ? new float3(1, 0, 0) : float3.Normalize(float3.Cross(d, new float3(0, 1, 0)));
        var v = float3.Cross(u, d);
        var h = thickness / 2;
        var builder = RioBatch(tint, material);
        var pa = RioOrigin + a;
        var pb = RioOrigin + b;
        foreach (var (n, s, t) in (ReadOnlySpan<(float3, float3, float3)>)[(u, v, -v), (-u, v, -v), (v, u, -u), (-v, u, -u)])
            builder.Quad(pa + n * h + s * h, pb + n * h + s * h, pb + n * h + t * h, pa + n * h + t * h, n);
        builder.Quad(pa + (u + v) * h, pa + (u - v) * h, pa + (-u - v) * h, pa + (-u + v) * h, -d);
        builder.Quad(pb + (u + v) * h, pb + (u - v) * h, pb + (-u - v) * h, pb + (-u + v) * h, d);
    }

    /// <summary>A round post or tank from <paramref name="a"/> to <paramref name="b"/> (local), with its ends closed.</summary>
    private void RioCylinder(float3 a, float3 b, float radius, float3 tint, Material material, int segments = 14)
    {
        var d = float3.Normalize(b - a);
        var u = float.Abs(d.Y) > 0.95f ? new float3(1, 0, 0) : float3.Normalize(float3.Cross(d, new float3(0, 1, 0)));
        var v = float3.Cross(u, d);
        var builder = RioBatch(tint, material);
        var pa = RioOrigin + a;
        var pb = RioOrigin + b;
        for (var i = 0; i < segments; i++)
        {
            var a0 = float.Tau * i / segments;
            var a1 = float.Tau * (i + 1) / segments;
            var r0 = (u * float.Cos(a0) + v * float.Sin(a0)) * radius;
            var r1 = (u * float.Cos(a1) + v * float.Sin(a1)) * radius;
            builder.Quad(pa + r0, pb + r0, pb + r1, pa + r1, float3.Normalize(r0 + r1));
            builder.Triangle(pb, pb + r0, pb + r1, d);
            builder.Triangle(pa, pa + r0, pa + r1, -d);
        }
    }

    /// <summary>A wire sagging between two points (local).</summary>
    private void RioWire(float3 a, float3 b, float sag, float thickness = 0.022f)
    {
        const int pieces = 6;
        var previous = a;
        for (var i = 1; i <= pieces; i++)
        {
            var t = (float)i / pieces;
            var p = a + (b - a) * t - new float3(0, 4 * sag * t * (1 - t), 0);
            RioBeam(previous, p, thickness, RioWireTint, Material.Plain);
            previous = p;
        }
    }

    /// <summary>A blue plastic water tank with its lid, standing on <paramref name="ground"/> (local).</summary>
    private void RioTank(float3 ground, float radius, float height)
    {
        RioCylinder(ground, ground + new float3(0, height, 0), radius, RioTankBlue, Material.Plain, 16);
        RioCylinder(ground + new float3(0, height, 0), ground + new float3(0, height + 0.07f, 0), radius * 1.06f, RioTankBlue * 1.15f, Material.Plain, 16);
        RioCylinder(ground + new float3(0, height + 0.07f, 0), ground + new float3(0, height + 0.13f, 0), radius * 0.25f, RioTankBlue * 1.15f, Material.Plain, 8);
    }

    private static float RioRandom(Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

    private static T RioPick<T>(Random random, T[] items) => items[random.Next(items.Length)];

    // A point on a face: u along it, y up, d out from it.
    private static float3 RioOnFace(in RioFace f, float u, float y, float d) =>
        f.Axis == 2 ? new float3(u, y, f.Coord + f.Sign * d) : new float3(f.Coord + f.Sign * d, y, u);

    private void RioFaceBox(in RioFace f, float u0, float u1, float y0, float y1, float d0, float d1, float3 tint, Material material) =>
        RioBox(RioOnFace(f, u0, y0, d0), RioOnFace(f, u1, y1, d1), tint, material);

    // ------------------------------------------------------------------------------------------------ the plan

    /// <summary>
    /// Lays out the houses block by block: each block's width, less its stair alleys (and the laje and the garden), cut
    /// into houses 2.8 to 6 m wide, each two to four storeys high above the alley in front of it.
    /// </summary>
    private void PlanRioHouses(Random random)
    {
        for (var block = -1; block <= 6; block++)
        {
            var spans = new List<(float X0, float X1, float Z1)>();
            float z0, z1, baseY;
            if (block == -1)
            {
                (z0, z1, baseY) = (-RioBlockD, 0f, -RioRise);
                spans.Add((-RioCapX, RioPocketX1, RioPocketZ0));   // (behind the portal's pocket)
                spans.Add((RioPocketX1, RioCapX, z1));
            }
            else if (block == 6)
            {
                (z0, z1, baseY) = (RioTerraceZ(6) + RioAlleyW, RioTerraceZ(6) + RioAlleyW + RioBlockD, RioTerraceY(6));
                spans.Add((-RioCapX, RioCapX, z1));
            }
            else
            {
                (z0, z1, baseY) = (RioTerraceZ(block) + RioAlleyW, RioTerraceZ(block + 1), RioTerraceY(block));
                var x = -RioCapX;
                foreach (var (a, b) in RioStairs[block].OrderBy(s => s.X0))
                {
                    spans.Add((x, a, z1));
                    x = b;
                }
                spans.Add((x, RioCapX, z1));
            }

            var row = new List<RioHouse>();
            foreach (var (s0, s1, sz1) in spans)
            {
                // Fixed pieces first: the laje (and the houses either side of it), the garden.
                var cuts = new List<(float, float, bool Laje, bool Garden)>();
                if (block == 5 && s0 < LajeX0 && s1 > LajeX1)
                {
                    cuts.AddRange(Cut(s0, LajeX0, random));
                    cuts.Add((LajeX0, LajeX1, true, false));
                    cuts.AddRange(Cut(LajeX1, s1, random));
                }
                else if (block == 2 && s0 < GardenX0 && s1 > GardenX1)
                {
                    cuts.AddRange(Cut(s0, GardenX0, random));
                    cuts.Add((GardenX0, GardenX1, false, true));
                    cuts.AddRange(Cut(GardenX1, s1, random));
                }
                else
                {
                    cuts.AddRange(Cut(s0, s1, random));
                }

                foreach (var (a, b, laje, garden) in cuts)
                {
                    float top;
                    if (laje)
                        top = RioTerraceY(6) - 0.05f;
                    else if (garden)
                        top = baseY + 1.2f;
                    else if (block == 6)
                        top = baseY + RioStorey * random.Next(2, 5);
                    else
                    {
                        var storeys = random.NextDouble() switch { < 0.3 => 1, < 0.75 => 2, _ => 3 };
                        if (block <= 4 && b > ViewX0 && a < ViewX1)
                            storeys = 1;   // (low, under the view from the laje)
                        top = baseY + RioRise + RioStorey * storeys;
                    }
                    row.Add(new RioHouse(block, a, b, z0, sz1, baseY, top, garden));
                }
            }
            _rioHouses.AddRange(row);
        }

        static IEnumerable<(float, float, bool, bool)> Cut(float a, float b, Random random)
        {
            var x = a;
            while (b - x > 0.01f)
            {
                var w = 2.8f + (float)random.NextDouble() * 3.2f;
                if (b - x - w < 2.6f)
                    w = b - x;
                yield return (x, x + w, false, false);
                x += w;
            }
        }
    }

    /// <summary>The top of whatever stands in <paramref name="block"/> at <paramref name="x"/>, or null in a stair alley.</summary>
    private float? RioTopAt(int block, float x)
    {
        foreach (var h in _rioHouses)
            if (h.Block == block && x >= h.X0 && x <= h.X1)
                return h.Top;
        return null;
    }

    private RioHouse? RioNeighbour(in RioHouse house, bool west)
    {
        foreach (var h in _rioHouses)
            if (h.Block == house.Block && (west ? float.Abs(h.X1 - house.X0) < 0.01f : float.Abs(h.X0 - house.X1) < 0.01f))
                return h;
        return null;
    }

    // ------------------------------------------------------------------------------------------------ alleys and stairs

    private void BuildRioAlleys(Random random)
    {
        for (var k = 0; k < RioTerraces; k++)
        {
            var z = RioTerraceZ(k);
            var y = RioTerraceY(k);
            var x0 = k == 0 ? -RioHalfX - RioPortalWall - RioPortalVoid : -RioCapX;
            RioSolid(new float3(x0, y - 3f, z), new float3(RioCapX, y, z + RioAlleyW), RioPaving, Material.Floor);
            // A drain channel down the middle.
            RioBox(new float3(-RioHalfX, y, z + RioAlleyW / 2 - 0.09f), new float3(RioHalfX, y + 0.004f, z + RioAlleyW / 2 + 0.09f), RioPaving * 0.55f, Material.Floor);
        }
        // The pocket at the bottom alley's west end, where the portal is.
        RioSolid(new float3(-RioHalfX - RioPortalWall - RioPortalVoid, -3f, RioPocketZ0), new float3(RioPocketX1, 0, 0), RioPaving, Material.Floor);

        // The stairways: 15 steps from each terrace up to the next, with a painted handrail up one side.
        var railColours = new[] { new float3(0.85f, 0.70f, 0.15f), new float3(0.15f, 0.40f, 0.70f), new float3(0.80f, 0.80f, 0.78f), new float3(0.20f, 0.55f, 0.30f) };
        const float tread = RioBlockD / RioRisers;
        const float riser = RioRise / RioRisers;
        for (var block = 0; block < RioStairs.Length; block++)
        {
            var zs = RioTerraceZ(block) + RioAlleyW;
            var y = RioTerraceY(block);
            foreach (var (x0, x1) in RioStairs[block])
            {
                for (var i = 0; i < RioRisers; i++)
                {
                    var top = y + riser * (i + 1);
                    RioSolid(new float3(x0, y - 1f, zs + i * tread), new float3(x1, top, zs + (i + 1) * tread), RioPaving * 1.05f, Material.Floor);
                    RioBox(new float3(x0, top - 0.035f, zs + i * tread - 0.012f), new float3(x1, top, zs + i * tread + 0.02f), RioPaving * 0.72f, Material.Floor);   // worn nosing
                }
                var rail = RioPick(random, railColours);
                var side = random.Next(2) == 0 ? x0 + 0.09f : x1 - 0.09f;
                var bottom = new float3(side, y + 0.18f + 0.9f, zs + 0.25f);
                var high = new float3(side, y + RioRise + 0.9f, zs + RioBlockD - 0.1f);
                RioBeam(bottom, high, 0.045f, rail, Material.PaintedMetal);
                for (var p = 0; p <= 3; p++)
                {
                    var zp = zs + 0.25f + p * (RioBlockD - 0.35f) / 3;
                    var step = (int)float.Ceiling((zp - zs) / tread);
                    var yp = y + riser * int.Clamp(step, 1, RioRisers);
                    RioBeam(new float3(side, yp, zp), new float3(side, bottom.Y + (high.Y - bottom.Y) * (zp - bottom.Z) / (high.Z - bottom.Z), zp), 0.04f, rail, Material.PaintedMetal);
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ houses

    private void BuildRioHouse(RioHouse h, Random random)
    {
        if (h.Garden)
            return;   // (BuildRioGarden)
        var laje = h.Block == 5 && h.X0 == LajeX0;
        var brick = !laje && random.NextDouble() < 0.33;
        var wall = brick ? RioBrickTint * RioRandom(random, 0.88f, 1.1f)
                 : random.NextDouble() < 0.1 ? RioRender : RioPick(random, RioPaint);
        var material = brick ? Material.Brick : Material.Blockwork;
        var bottom = h.Block == -1 ? -9f : h.Base - 1.5f;
        var hideDoor = RioHideDoorOf(h);
        var stairHouse = RioStairHouseOf(h);
        var store = IsRioStore(h);
        if (stairHouse is { } sh)
            BuildRioStairHouse(h, sh.Door, sh.Side, bottom, wall, material);
        else if (store)
            _rioStore = (h, bottom, wall, material);   // (built round its room once the facade has placed the door: BuildRioStore)
        else if (float.IsNaN(hideDoor))
            RioSolid(new float3(h.X0, bottom, h.Z0), new float3(h.X1, h.Top, h.Z1), wall, material);
        else
            BuildRioHideoutHouse(h, hideDoor, bottom, wall, material);

        // The faces you can see, and what's in front of each: the alley below (the front), the alley above (the back),
        // a stairway or a lower neighbour (the sides).
        var faces = new List<(RioFace Face, bool Door, float Rise)>();
        var frontGround = h.Block == -1 ? -5f : h.Base;
        faces.Add((new RioFace(2, h.Z0, -1, h.X0, h.X1, frontGround, h.Top), h.Block >= 0, 0f));
        if (h.Block < 6)
        {
            var backGround = h.Block == -1 ? 0f : h.Base + RioRise;
            if (h.Top - backGround > 2.4f)
                faces.Add((new RioFace(2, h.Z1, 1, h.X0, h.X1, backGround, h.Top), random.NextDouble() < 0.45 || stairHouse is not null, 0f));
        }
        foreach (var west in (ReadOnlySpan<bool>)[true, false])
        {
            var x = west ? h.X0 : h.X1;
            var neighbour = RioNeighbour(h, west);
            var ground = neighbour is { } n ? n.Top : h.Base;
            var outside = float.Abs(x) >= RioCapX - 0.01f;
            if (outside)
                ground = h.Base;   // (the outside of the favela's core, seen from far off)
            if (h.Top - ground > 2.4f)
                faces.Add((new RioFace(0, x, west ? -1 : 1, h.Z0, h.Z1, ground, h.Top), false,
                    neighbour is null && !outside && h.Block is >= 0 and <= 5 ? RioRise : 0f));   // (a stairway beside it climbs along it)
        }

        foreach (var (face, door, rise) in faces)
        {
            var front = face.Axis == 2 && face.Sign < 0 && h.Block >= 0;
            var back = face.Axis == 2 && face.Sign > 0;
            // An open doorway: a hiding place's, or a stair house's at either end (the leaf hung on the side away from its stair).
            var (openDoor, hinge) = stairHouse is { } s && (front || back) ? (s.Door, -s.Side) : (front ? hideDoor : float.NaN, 1f);
            BuildRioFacade(face, door, wall, brick, front, random, rise, openDoor, hinge, store: store && front);
        }
        if (store)
            BuildRioStoreFallback();   // (if the facade put no door on its front)

        if (brick)
        {
            // The grey concrete frame the brick fills: columns at the corners, a beam at every floor.
            foreach (var x in (ReadOnlySpan<float>)[h.X0, h.X1])
                foreach (var z in (ReadOnlySpan<float>)[h.Z0, h.Z1])
                    RioBox(new float3(x - 0.13f, h.Base - 0.2f, z - 0.13f), new float3(x + 0.13f, h.Top + 0.02f, z + 0.13f), RioConcrete, Material.Blockwork);
        }

        if (!laje)
            BuildRioRoof(h, wall, material, random);
    }

    /// <summary>One face of a house: a beam at every floor, windows (barred, shuttered, framed or just holes), a door,
    /// the odd balcony and air conditioner.</summary>
    /// <param name="hideDoor">Where the door into a hiding place or a stair house is (NaN: there isn't one): it stands open on a doorway (<see cref="BuildRioHideDoor"/>).</param>
    /// <param name="hinge">Which jamb that open door's leaf hangs on (<see cref="BuildRioHideDoor"/>).</param>
    /// <param name="store">The storage room's house front: its door opens into it (<see cref="BuildRioStore"/>). It rolls just as before.</param>
    private void BuildRioFacade(in RioFace f, bool door, float3 wall, bool brick, bool front, Random random, float rise = 0f, float hideDoor = float.NaN, float hinge = 1f, bool store = false)
    {
        var length = f.U1 - f.U0;
        if (length < 1.2f)
            return;
        var hideout = door && !float.IsNaN(hideDoor);
        var band = brick ? RioConcrete : wall * 0.8f;
        // Some painted houses have their bottom storey another colour (round a hiding place's doorway).
        float3? storeLower = null;   // (the storage room's: drawn round its doorway once that's placed)
        if (!brick && random.NextDouble() < 0.25)
        {
            var lower = RioPick(random, RioPaint);
            if (store)
                storeLower = lower;
            else if (!hideout)
            {
                RioFaceBox(f, f.U0, f.U1, f.Ground - 0.3f, f.Ground + RioStorey, 0, 0.012f, lower, Material.Blockwork);
            }
            else
            {
                RioFaceBox(f, f.U0, hideDoor - RioHideDoorHalf, f.Ground - 0.3f, f.Ground + RioStorey, 0, 0.012f, lower, Material.Blockwork);
                RioFaceBox(f, hideDoor + RioHideDoorHalf, f.U1, f.Ground - 0.3f, f.Ground + RioStorey, 0, 0.012f, lower, Material.Blockwork);
                RioFaceBox(f, hideDoor - RioHideDoorHalf, hideDoor + RioHideDoorHalf, f.Ground + RioHideDoorTop, f.Ground + RioStorey, 0, 0.012f, lower, Material.Blockwork);
            }
        }

        var slots = int.Max(1, (int)((length - 0.8f) / 2.1f));
        var spacing = length / slots;
        var doorSlot = door ? random.Next(slots) : -1;
        if (hideout)
            doorSlot = slots / 2;   // (RioMiddleDoor)
        BuildRioWallClutter(f, door ? f.U0 + spacing * (doorSlot + 0.5f) : float.NaN, front, rise, random);
        var shop = door && front && random.NextDouble() < 0.12 && length > 3.4f && !hideout && !store;
        var acModel = TryModel("ac_unit");
        for (var j = 0; ; j++)
        {
            var floor = f.Ground + j * RioStorey;
            if (floor + 2.3f > f.Top)
                break;
            if (j > 0)
                RioFaceBox(f, f.U0, f.U1, floor - 0.08f, floor + 0.08f, 0, 0.05f, band, Material.Blockwork);

            if (j == 0 && shop)
            {
                // A little bar: a roller shutter half up, a painted board over it.
                var c = (f.U0 + f.U1) / 2;
                RioFaceBox(f, c - 1.3f, c + 1.3f, floor + 1.25f, floor + 2.35f, 0, 0.06f, new float3(0.55f, 0.56f, 0.57f), Material.Galvanized);
                RioFaceBox(f, c - 1.3f, c + 1.3f, floor, floor + 1.25f, -0.02f, 0.005f, new float3(0.05f, 0.05f, 0.05f), Material.Plain);
                RioFaceBox(f, c - 1.45f, c + 1.45f, floor + 2.4f, floor + 2.75f, 0, 0.04f, RioPick(random, RioPaint) * 1.05f, Material.PaintedMetal);
                continue;
            }

            var balcony = front && j > 0 && random.NextDouble() < 0.2 && length > 2.6f;
            for (var s = 0; s < slots; s++)
            {
                var c = f.U0 + spacing * (s + 0.5f);
                if (j == 0 && s == doorSlot)
                {
                    var paint = RioPick(random, RioDoorPaint);
                    if (store)
                    {
                        BuildRioStore(f, c, floor, paint, storeLower);
                        continue;
                    }
                    if (hideout)
                    {
                        BuildRioHideDoor(f, c, floor, paint, hinge);
                        continue;
                    }
                    RioFaceBox(f, c - 0.47f, c + 0.47f, floor, floor + 2.1f, 0, 0.035f, paint * 0.75f, Material.PaintedMetal);   // frame
                    RioFaceBox(f, c - 0.42f, c + 0.42f, floor + 0.02f, floor + 2.05f, 0, 0.05f, paint, Material.PaintedMetal);
                    RioFaceBox(f, c + 0.26f, c + 0.32f, floor + 0.98f, floor + 1.06f, 0.05f, 0.09f, RioBarTint, Material.Steel);   // handle
                    continue;
                }
                if (random.NextDouble() < 0.15)
                    continue;
                var w = float.Min(1.25f, spacing - 0.7f) * RioRandom(random, 0.75f, 1f);
                var tall = balcony && s == slots / 2;
                var y0 = floor + (tall ? 0.05f : 1.0f);
                var y1 = floor + 2.1f;
                if (y0 < f.Ground + rise * (c + w / 2 - f.U0) / length + 0.3f)
                    continue;   // (the stairway beside it has climbed past it)
                BuildRioWindow(f, c, w, y0, y1, random);
                if (j > 0 && acModel is not null && random.NextDouble() < 0.07)
                {
                    var side = c + (w / 2 + 0.45f) * (random.Next(2) == 0 ? -1 : 1);
                    if (side > f.U0 + 0.4f && side < f.U1 - 0.4f)
                    {
                        var at = RioOrigin + RioOnFace(f, side, floor + 1.7f, 0.3f);
                        var yaw = f.Axis == 0 ? (f.Sign > 0 ? 0f : float.Pi) : (f.Sign > 0 ? -float.Pi / 2 : float.Pi / 2);
                        AddModel(acModel, float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(at), new float3(0.78f, 0.78f, 0.75f));
                    }
                }
            }

            if (balcony)
            {
                // A slab out over the alley, with a low wall or a railing.
                var bw = float.Min(length - 0.5f, 2.6f);
                var c = (f.U0 + f.U1) / 2;
                var lo = RioOnFace(f, c - bw / 2, floor - 0.13f, 0);
                var hi = RioOnFace(f, c + bw / 2, floor, 0.75f);
                RioSolid(float3.Min(lo, hi), float3.Max(lo, hi), RioConcrete, Material.Blockwork);
                if (random.NextDouble() < 0.5)
                {
                    RioFaceBox(f, c - bw / 2, c + bw / 2, floor, floor + 0.95f, 0.66f, 0.75f, wall, brick ? Material.Brick : Material.Blockwork);
                }
                else
                {
                    var rail = RioPick(random, RioDoorPaint);
                    RioFaceBox(f, c - bw / 2, c + bw / 2, floor + 0.92f, floor + 0.97f, 0.68f, 0.73f, rail, Material.PaintedMetal);
                    for (var u = c - bw / 2 + 0.03f; u <= c + bw / 2; u += 0.14f)
                        RioFaceBox(f, u - 0.012f, u + 0.012f, floor, floor + 0.92f, 0.69f, 0.715f, rail, Material.PaintedMetal);
                }
                // Washing on the rail, sometimes.
                if (random.NextDouble() < 0.4)
                    for (var u = c - bw / 2 + 0.3f; u < c + bw / 2 - 0.3f; u += RioRandom(random, 0.45f, 0.8f))
                        RioFaceBox(f, u - 0.2f, u + 0.2f, floor + 0.35f, floor + 0.97f, 0.76f, 0.78f, RioPick(random, RioPaint), Material.Cloth);
            }
        }
    }

    /// <summary>
    /// What gets fixed to a wall: a drainpipe down a corner, a bundle of wires down to a meter box, spray-painted tags low
    /// down, and by a door, a plant in a pot or a gas bottle.
    /// </summary>
    private void BuildRioWallClutter(RioFace f, float doorAt, bool front, float rise, Random random)
    {
        var length = f.U1 - f.U0;
        float GroundAt(float u) => f.Ground + rise * (u - f.U0) / length;
        if (random.NextDouble() < 0.35)
        {
            var u = random.Next(2) == 0 ? f.U0 + 0.2f : f.U1 - 0.2f;
            var white = new float3(0.84f, 0.84f, 0.81f);
            RioCylinder(RioOnFace(f, u, GroundAt(u) + 0.1f, 0.08f), RioOnFace(f, u, f.Top - 0.05f, 0.08f), 0.045f, white, Material.Plain, 8);
        }
        if (random.NextDouble() < 0.4)
        {
            // Wires come down the wall to a meter box at head height.
            var u = RioRandom(random, f.U0 + 0.5f, f.U1 - 0.5f);
            if (float.IsNaN(doorAt) || float.Abs(u - doorAt) > 0.9f)
            {
                var meter = GroundAt(u) + 1.6f;
                RioFaceBox(f, u - 0.18f, u + 0.18f, meter, meter + 0.45f, 0, 0.14f, new float3(0.62f, 0.63f, 0.62f), Material.PaintedMetal);
                for (var k = 0; k < 3; k++)
                {
                    var du = (k - 1) * 0.05f;
                    RioBeam(RioOnFace(f, u + du, meter + 0.45f, 0.04f + k * 0.015f), RioOnFace(f, u + du * 3 + RioRandom(random, -0.3f, 0.3f), f.Top - 0.1f, 0.04f + k * 0.015f), 0.018f, RioWireTint, Material.Plain);
                }
            }
        }
        if (random.NextDouble() < 0.22)
        {
            // Tags: a few spray-painted scrawls, as flat smears of colour.
            var tag = random.Next(3) switch { 0 => new float3(0.05f, 0.05f, 0.06f), 1 => new float3(0.75f, 0.1f, 0.12f), _ => new float3(0.12f, 0.22f, 0.7f) };
            for (var k = random.Next(1, 4); k > 0; k--)
            {
                var u = RioRandom(random, f.U0 + 0.4f, f.U1 - 1.2f);
                var y = GroundAt(u) + RioRandom(random, 0.5f, 1.5f);
                var w = RioRandom(random, 0.5f, 1.1f);
                var h = RioRandom(random, 0.12f, 0.3f);
                if (!float.IsNaN(doorAt) && u < doorAt + 0.55f && u + w > doorAt - 0.55f)
                    continue;   // (not across a door: it might be standing open)
                RioFaceBox(f, u, u + w, y, y + h, 0.014f, 0.017f, tag, Material.Plain);
            }
        }
        if (front && !float.IsNaN(doorAt))
        {
            // Beside the door: a plant in a pot, or a blue gas bottle.
            var u = doorAt + (random.Next(2) == 0 ? -0.8f : 0.8f);
            if (u > f.U0 + 0.3f && u < f.U1 - 0.3f)
            {
                var foot = RioOnFace(f, u, f.Ground, 0.25f);
                if (random.NextDouble() < 0.55)
                {
                    RioCylinder(foot, foot + new float3(0, 0.38f, 0), 0.17f, new float3(0.62f, 0.32f, 0.2f), Material.Plain, 10);
                    RioBatch(RioLeafTint * RioRandom(random, 0.8f, 1.2f), Material.Bush).Blob(RioOrigin + foot + new float3(0, 0.65f, 0), new float3(0.28f, 0.35f, 0.28f), random);
                    AddStatic(BoxBetween(RioOrigin + foot - new float3(0.18f, 0, 0.18f), RioOrigin + foot + new float3(0.18f, 0.4f, 0.18f)), default, visible: false);
                }
                else if (random.NextDouble() < 0.5)
                {
                    RioCylinder(foot, foot + new float3(0, 0.55f, 0), 0.16f, new float3(0.1f, 0.25f, 0.62f), Material.PaintedMetal, 12);
                    RioCylinder(foot + new float3(0, 0.55f, 0), foot + new float3(0, 0.66f, 0), 0.05f, new float3(0.6f, 0.6f, 0.58f), Material.Steel, 8);
                    AddStatic(BoxBetween(RioOrigin + foot - new float3(0.17f, 0, 0.17f), RioOrigin + foot + new float3(0.17f, 0.66f, 0.17f)), default, visible: false);
                }
            }
        }
    }

    private void BuildRioWindow(in RioFace f, float c, float w, float y0, float y1, Random random)
    {
        var u0 = c - w / 2;
        var u1 = c + w / 2;
        var kind = random.Next(5);
        // The pane (or, unglazed, just the dark inside); at night, a light on in some of the rooms behind.
        if (kind != 3 && RioLitWindow(f.Coord, c, y0, 0.36f) is { } room)
            RioFaceBox(f, u0, u1, y0, y1, -0.02f, 0.01f, room, Material.Emissive);
        else
            RioFaceBox(f, u0, u1, y0, y1, -0.02f, 0.01f, kind == 4 ? new float3(0.03f, 0.03f, 0.03f) : RioGlassTint, kind == 4 ? Material.Plain : Material.Glass);
        RioFaceBox(f, u0 - 0.08f, u1 + 0.08f, y0 - 0.06f, y0, 0, 0.07f, RioConcrete, Material.Blockwork);   // sill
        switch (kind)
        {
            case 0:
            case 1:
                // A frame and bars over it.
                var frame = kind == 0 ? RioFrameTint : RioPick(random, RioDoorPaint);
                RioFaceBox(f, u0 - 0.05f, u0, y0, y1, 0, 0.04f, frame, Material.PaintedMetal);
                RioFaceBox(f, u1, u1 + 0.05f, y0, y1, 0, 0.04f, frame, Material.PaintedMetal);
                RioFaceBox(f, u0 - 0.05f, u1 + 0.05f, y1, y1 + 0.05f, 0, 0.04f, frame, Material.PaintedMetal);
                for (var u = u0 + 0.12f; u < u1 - 0.05f; u += 0.13f)
                    RioFaceBox(f, u - 0.01f, u + 0.01f, y0, y1, 0.05f, 0.07f, RioBarTint, Material.Steel);
                RioFaceBox(f, u0, u1, (y0 + y1) / 2 - 0.01f, (y0 + y1) / 2 + 0.01f, 0.05f, 0.07f, RioBarTint, Material.Steel);
                break;
            case 2:
                // Wooden shutters, open either side.
                var shutter = RioPick(random, RioDoorPaint);
                RioFaceBox(f, u0 - w / 2 - 0.02f, u0 - 0.02f, y0, y1, 0.01f, 0.05f, shutter, Material.Wood);
                RioFaceBox(f, u1 + 0.02f, u1 + w / 2 + 0.02f, y0, y1, 0.01f, 0.05f, shutter, Material.Wood);
                break;
            case 3:
                // A shutter pulled to.
                RioFaceBox(f, u0, u1, y0, y1, 0.01f, 0.04f, RioPick(random, RioDoorPaint), Material.Wood);
                break;
            default:
                // An aluminium frame round a hole.
                RioFaceBox(f, u0 - 0.04f, u1 + 0.04f, y1, y1 + 0.04f, 0, 0.03f, RioFrameTint, Material.Steel);
                break;
        }
    }

    /// <summary>What's on a flat roof: a low wall round it, a water tank, rebar left for the next storey, washing, a dish, a sheet roof.</summary>
    private void BuildRioRoof(in RioHouse h, float3 wall, Material material, Random random)
    {
        var top = h.Top;
        var (x0, x1, z0, z1) = (h.X0, h.X1, h.Z0, h.Z1);
        if (random.NextDouble() < 0.45)
        {
            const float t = 0.12f, ph = 0.55f;
            RioBox(new float3(x0, top, z0), new float3(x1, top + ph, z0 + t), wall, material);
            RioBox(new float3(x0, top, z1 - t), new float3(x1, top + ph, z1), wall, material);
            RioBox(new float3(x0, top, z0), new float3(x0 + t, top + ph, z1), wall, material);
            RioBox(new float3(x1 - t, top, z0), new float3(x1, top + ph, z1), wall, material);
        }
        if (random.NextDouble() < 0.4)
        {
            // Columns started for the next storey, their rebar sticking out.
            foreach (var x in (ReadOnlySpan<float>)[x0 + 0.2f, x1 - 0.2f])
            {
                foreach (var z in (ReadOnlySpan<float>)[z0 + 0.2f, z1 - 0.2f])
                {
                    var stub = RioRandom(random, 0.1f, 0.5f);
                    RioBox(new float3(x - 0.11f, top, z - 0.11f), new float3(x + 0.11f, top + stub, z + 0.11f), RioConcrete, Material.Blockwork);
                    for (var b = 0; b < 4; b++)
                    {
                        var bx = x + (b % 2 == 0 ? -0.06f : 0.06f);
                        var bz = z + (b < 2 ? -0.06f : 0.06f);
                        RioBeam(new float3(bx, top + stub, bz), new float3(bx + RioRandom(random, -0.08f, 0.08f), top + stub + RioRandom(random, 0.5f, 1.0f), bz + RioRandom(random, -0.08f, 0.08f)), 0.016f, new float3(0.32f, 0.17f, 0.09f), Material.RustyMetal);
                    }
                }
            }
        }
        if (random.NextDouble() < 0.7 && x1 - x0 > 2.2f)
        {
            var r = RioRandom(random, 0.45f, 0.62f);
            var at = new float3(RioRandom(random, x0 + r + 0.3f, x1 - r - 0.3f), top, RioRandom(random, z0 + r + 0.3f, z1 - r - 0.3f));
            RioBox(at + new float3(-r, 0, -r), at + new float3(r, 0.12f, r), RioConcrete, Material.Blockwork);
            RioTank(at + new float3(0, 0.12f, 0), r, RioRandom(random, 0.7f, 0.95f));
        }
        if (random.NextDouble() < 0.15)
        {
            var z = RioRandom(random, z0 + 0.6f, z1 - 0.6f);
            RioBeam(new float3(x0 + 0.3f, top, z), new float3(x0 + 0.3f, top + 1.8f, z), 0.04f, RioRender, Material.Steel);
            RioBeam(new float3(x1 - 0.3f, top, z), new float3(x1 - 0.3f, top + 1.8f, z), 0.04f, RioRender, Material.Steel);
            RioWire(new float3(x0 + 0.3f, top + 1.75f, z), new float3(x1 - 0.3f, top + 1.75f, z), 0.06f, 0.012f);
            for (var x = x0 + 0.7f; x < x1 - 0.7f; x += RioRandom(random, 0.5f, 0.9f))
                RioBox(new float3(x - 0.22f, top + 1.0f + RioRandom(random, 0f, 0.3f), z - 0.012f), new float3(x + 0.22f, top + 1.72f, z + 0.012f), RioPick(random, RioPaint), Material.Cloth);
        }
        if (random.NextDouble() < 0.22)
        {
            // A satellite dish on a bracket at the roof's edge, looking up at the sky to the north.
            var at = new float3(RioRandom(random, x0 + 0.5f, x1 - 0.5f), top + 0.9f, z0 + 0.35f);
            RioBeam(at - new float3(0, 0.9f, 0), at, 0.04f, RioBarTint, Material.Steel);
            RioCylinder(at, at + float3.Normalize(new float3(0.2f, 0.8f, -0.55f)) * 0.06f, 0.36f, new float3(0.82f, 0.82f, 0.80f), Material.PaintedMetal, 16);
        }
        if (random.NextDouble() < 0.14 && x1 - x0 > 3f)
        {
            // A sheet roof on four posts over half of it.
            var zm = (z0 + z1) / 2;
            var sheet = random.Next(2) == 0 ? new float3(0.55f, 0.55f, 0.53f) : new float3(0.62f, 0.58f, 0.52f);
            foreach (var x in (ReadOnlySpan<float>)[x0 + 0.2f, x1 - 0.2f])
                foreach (var z in (ReadOnlySpan<float>)[zm, z1 - 0.2f])
                    RioBeam(new float3(x, top, z), new float3(x, top + 2.2f, z), 0.08f, RioRender, Material.Wood);
            RioBox(new float3(x0 - 0.1f, top + 2.2f, zm - 0.2f), new float3(x1 + 0.1f, top + 2.26f, z1 + 0.15f), sheet, Material.Galvanized);
        }
    }

    /// <summary>The houses across each alley's ends: they wall it off, with a door and windows looking down it.</summary>
    private void BuildRioEndCaps(Random random)
    {
        for (var k = 0; k < RioTerraces; k++)
        {
            var z0 = k == 0 ? RioPocketZ0 : RioTerraceZ(k);
            var z1 = RioTerraceZ(k) + RioAlleyW;
            var y = RioTerraceY(k);
            foreach (var west in (ReadOnlySpan<bool>)[true, false])
            {
                var inner = west ? (k == 0 ? -RioHalfX - RioPortalWall - RioPortalVoid : -RioHalfX) : RioHalfX;
                var outer = west ? -RioCapX : RioCapX;
                var top = y + RioStorey * random.Next(2, 4);
                var brick = random.NextDouble() < 0.33;
                var wall = brick ? RioBrickTint : RioPick(random, RioPaint);
                RioSolid(new float3(float.Min(inner, outer), y - 3f, z0), new float3(float.Max(inner, outer), top, z1), wall, brick ? Material.Brick : Material.Blockwork);
                if (k == 0 && west)
                    continue;   // (behind the portal: no one sees it)
                BuildRioFacade(new RioFace(0, inner, west ? 1 : -1, z0, z1, y, top), true, wall, brick, front: false, random);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ the portal's end

    /// <summary>
    /// The bottom alley's west end: a pocket a little wider than the alley, and across it a rendered block wall painted
    /// teal, the magenta portal cut into it (a level box, so the portal can cut its doorway), a void behind it.
    /// </summary>
    private void BuildRioPortalEnd()
    {
        var face = -RioHalfX;
        Add(RioOrigin + new float3(face - RioPortalWall / 2, 2.1f, (RioPocketZ0 + RioPocketZ1) / 2),
            new float3(RioPortalWall, 6.2f, RioPocketZ1 - RioPocketZ0), RioPortalWallTint, Material.Blockwork);
        RioBox(new float3(face - RioPortalWall - RioPortalVoid, 5.2f, RioPocketZ0), new float3(face, 5.34f, RioPocketZ1), RioConcrete, Material.Blockwork);
        // A strip light's bracket and a meter box on the wall beside it, wires up to them.
        RioBox(new float3(face, 3.0f, RioPocketZ1 - 0.45f), new float3(face + 0.18f, 3.45f, RioPocketZ1 - 0.15f), new float3(0.82f, 0.82f, 0.8f), Material.Plain);
        RioWire(new float3(face + 0.1f, 3.45f, RioPocketZ1 - 0.3f), new float3(face + 2.5f, 4.6f, RioPocketZ1 - 0.02f), 0.2f);
    }

    // ------------------------------------------------------------------------------------------------ the laje

    /// <summary>
    /// The flat roof at the top you can walk out onto from terrace 6: a concrete deck, a low wall at its front, a water
    /// tank, a couple of plastic chairs, washing on a line and plants in pots, and the whole hill below you.
    /// </summary>
    private void BuildRioLaje(Random random)
    {
        var y = RioTerraceY(6);
        var z0 = RioTerraceZ(5) + RioAlleyW;
        var z1 = RioTerraceZ(6);
        RioSolid(new float3(LajeX0, y - 0.05f, z0), new float3(LajeX1, y, z1), RioPaving * 1.05f, Material.Floor);
        var parapet = new float3(0.94f, 0.87f, 0.68f);
        RioBox(new float3(LajeX0, y, z0), new float3(LajeX1, y + 1.0f, z0 + 0.15f), parapet, Material.Blockwork);
        RioBox(new float3(LajeX0 - 0.03f, y + 1.0f, z0 - 0.03f), new float3(LajeX1 + 0.03f, y + 1.06f, z0 + 0.18f), RioConcrete, Material.Blockwork);
        AddStatic(BoxBetween(RioL(LajeX0, y, z0), RioL(LajeX1, y + 2.4f, z0 + 0.15f)), default, visible: false);

        // The tank on its stand, chairs and a little table, a line of washing, pots.
        var tank = new float3(LajeX1 - 0.8f, y, z1 - 0.8f);
        RioBox(tank + new float3(-0.65f, 0, -0.65f), tank + new float3(0.65f, 0.5f, 0.65f), RioConcrete, Material.Blockwork);
        RioTank(tank + new float3(0, 0.5f, 0), 0.6f, 0.95f);
        AddStatic(BoxBetween(RioL(tank.X - 0.7f, y, tank.Z - 0.7f), RioL(tank.X + 0.7f, y + 1.6f, tank.Z + 0.7f)), default, visible: false);
        var white = new float3(0.92f, 0.92f, 0.9f);
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(LajeX0 + 1.4f, z0 + 1.1f), (LajeX0 + 2.9f, z0 + 1.0f)])
        {
            RioBox(new float3(x - 0.22f, y + 0.42f, z - 0.22f), new float3(x + 0.22f, y + 0.46f, z + 0.22f), white, Material.Plain);   // seat
            RioBox(new float3(x - 0.22f, y + 0.46f, z + 0.18f), new float3(x + 0.22f, y + 0.9f, z + 0.22f), white, Material.Plain);    // back
            foreach (var (lx, lz) in (ReadOnlySpan<(float, float)>)[(-0.19f, -0.19f), (0.19f, -0.19f), (-0.19f, 0.19f), (0.19f, 0.19f)])
                RioBox(new float3(x + lx - 0.02f, y, z + lz - 0.02f), new float3(x + lx + 0.02f, y + 0.42f, z + lz + 0.02f), white, Material.Plain);
        }
        var table = new float3(LajeX0 + 2.15f, y, z0 + 1.6f);
        RioCylinder(table + new float3(0, 0.68f, 0), table + new float3(0, 0.71f, 0), 0.38f, white, Material.Plain, 18);
        RioBeam(table, table + new float3(0, 0.68f, 0), 0.05f, white, Material.Plain);
        AddStatic(BoxBetween(RioL(LajeX0 + 1.1f, y, z0 + 0.8f), RioL(LajeX0 + 3.2f, y + 0.8f, z0 + 1.9f)), default, visible: false);
        var lineZ = z1 - 0.6f;
        RioBeam(new float3(LajeX0 + 0.25f, y, lineZ), new float3(LajeX0 + 0.25f, y + 1.85f, lineZ), 0.04f, RioRender, Material.Steel);
        RioBeam(new float3(LajeX1 - 1.7f, y, lineZ), new float3(LajeX1 - 1.7f, y + 1.85f, lineZ), 0.04f, RioRender, Material.Steel);
        RioWire(new float3(LajeX0 + 0.25f, y + 1.8f, lineZ), new float3(LajeX1 - 1.7f, y + 1.8f, lineZ), 0.05f, 0.012f);
        for (var x = LajeX0 + 0.6f; x < LajeX1 - 2.0f; x += 0.55f)
            RioBox(new float3(x - 0.2f, y + 1.05f + RioRandom(random, 0f, 0.3f), lineZ - 0.012f), new float3(x + 0.2f, y + 1.78f, lineZ + 0.012f), RioPick(random, RioPaint), Material.Cloth);
        foreach (var x in (ReadOnlySpan<float>)[LajeX0 + 0.35f, LajeX1 - 0.35f])
        {
            RioCylinder(new float3(x, y, z0 + 0.4f), new float3(x, y + 0.4f, z0 + 0.4f), 0.2f, new float3(0.62f, 0.32f, 0.2f), Material.Plain, 12);
            RioBatch(RioLeafTint, Material.Bush).Blob(RioL(x, y + 0.7f, z0 + 0.4f), new float3(0.35f, 0.4f, 0.35f), random);
        }
    }

    // ------------------------------------------------------------------------------------------------ the garden

    /// <summary>A gap in block 2 grown over: a retaining wall along the alley, bananas and a palm on the terrace behind it, a wall along the alley above.</summary>
    private void BuildRioGarden(Random random)
    {
        var y = RioTerraceY(2);
        var z0 = RioTerraceZ(2) + RioAlleyW;
        var z1 = RioTerraceZ(3);
        var ground = y + 1.2f;
        RioSolid(new float3(GardenX0, y - 1.5f, z0), new float3(GardenX1, y + 1.35f, z0 + 0.25f), RioRender, Material.Blockwork);   // retaining wall
        RioSolid(new float3(GardenX0, y - 1.5f, z0 + 0.25f), new float3(GardenX1, ground, z1 - 0.15f), RioGrassTint * 0.8f, Material.Grass);
        var upper = RioTerraceY(3);
        RioSolid(new float3(GardenX0, y - 1.5f, z1 - 0.15f), new float3(GardenX1, upper + 1.0f, z1), RioPick(random, RioPaint), Material.Blockwork);
        AddStatic(BoxBetween(RioL(GardenX0, upper, z1 - 0.15f), RioL(GardenX1, upper + 2.4f, z1)), default, visible: false);
        AddStatic(BoxBetween(RioL(GardenX0, y, z0), RioL(GardenX1, y + 2.6f, z0 + 0.25f)), default, visible: false);   // (can't climb in)

        var leaves = RioBatch(RioLeafTint, Material.Bush);
        for (var i = 0; i < 7; i++)
            leaves.Blob(RioL(RioRandom(random, GardenX0 + 0.5f, GardenX1 - 0.5f), ground + 0.2f, RioRandom(random, z0 + 0.7f, z1 - 0.7f)), new float3(RioRandom(random, 0.4f, 0.8f), RioRandom(random, 0.3f, 0.6f), RioRandom(random, 0.4f, 0.8f)), random);
        var plants = new[] { ("jungle_banana_a", 1.1f), ("jungle_banana_b", 1.0f), ("jungle_ground_palm", 0.7f), ("jungle_banana_a", 0.9f) };
        for (var i = 0; i < plants.Length; i++)
        {
            if (TryModel(plants[i].Item1) is not { } model)
                continue;
            var at = RioL(GardenX0 + 0.9f + i * (GardenX1 - GardenX0 - 1.8f) / (plants.Length - 1), ground, z0 + 1.6f + (i % 2) * 3.0f);
            AddModel(model, float4x4.CreateScale(plants[i].Item2) * float4x4.CreateRotationY(i * 1.9f) * float4x4.CreateTranslation(at), new float3(1, 1, 1));
        }
    }

    // ------------------------------------------------------------------------------------------------ wires

    /// <summary>
    /// Wires everywhere: across every alley from wall to wall, along the walls, over the stairways, and fanned out from a
    /// concrete pole or two in each alley.
    /// </summary>
    private void BuildRioWires(Random random)
    {
        for (var k = 0; k < RioTerraces; k++)
        {
            var z = RioTerraceZ(k);
            var y = RioTerraceY(k);
            float Lower(float x) => (RioTopAt(k - 1, x) ?? float.MinValue / 2) - 0.25f;   // the downhill wall's top (none over a stairway: no wire)
            float Upper(float x) => (RioTopAt(k, x) ?? float.MinValue / 2) - 0.25f;           // the uphill wall's (block 6 above the top alley)

            // Across the alley.
            for (var i = 0; i < 9; i++)
            {
                var x1 = RioRandom(random, -RioHalfX + 1, RioHalfX - 1);
                var x2 = x1 + RioRandom(random, -2.5f, 2.5f);
                var h1 = float.Min(y + RioRandom(random, 3.3f, 4.8f), Lower(x1));
                var h2 = float.Min(y + RioRandom(random, 3.3f, 4.8f), Upper(x2));
                if (h1 < y + 2.6f || h2 < y + 2.6f)
                    continue;
                RioWire(new float3(x1, h1, z + 0.02f), new float3(x2, h2, z + RioAlleyW - 0.02f), RioRandom(random, 0.05f, 0.18f));
            }
            // Along the house fronts, a little off them.
            for (var i = 0; i < 5; i++)
            {
                var x1 = RioRandom(random, -RioHalfX + 1, RioHalfX - 12);
                var x2 = x1 + RioRandom(random, 6f, 14f);
                var off = RioRandom(random, 0.12f, 0.4f);
                var zz = random.Next(2) == 0 ? z + off : z + RioAlleyW - off;
                var h = y + RioRandom(random, 3.0f, 4.2f);
                RioWire(new float3(x1, h, zz), new float3(x2, h + RioRandom(random, -0.4f, 0.4f), zz), RioRandom(random, 0.2f, 0.45f));
            }

            // A pole or two against the uphill side, with wires fanned out from its top.
            for (var p = 0; p < 2; p++)
            {
                var px = RioRandom(random, -RioHalfX + 3, RioHalfX - 3);
                if (k < RioStairs.Length && RioStairs[k].Any(s => px > s.X0 - 0.5f && px < s.X1 + 0.5f))
                    continue;
                if (k > 0 && RioStairs[k - 1].Any(s => px > s.X0 - 0.5f && px < s.X1 + 0.5f))
                    continue;
                px = RioAsideOfHideDoors(k, px);   // (not in front of a hiding place's door)
                var pz = z + RioAlleyW - 0.13f;
                var topY = y + 7.2f;
                RioSolid(new float3(px - 0.11f, y, pz - 0.11f), new float3(px + 0.11f, topY, pz + 0.11f), new float3(0.6f, 0.59f, 0.56f), Material.Blockwork);
                RioBeam(new float3(px, topY - 0.4f, pz - 0.7f), new float3(px, topY - 0.4f, pz + 0.05f), 0.08f, new float3(0.6f, 0.59f, 0.56f), Material.Blockwork);
                if (random.NextDouble() < 0.6)
                    RioCylinder(new float3(px, topY - 2.0f, pz - 0.35f), new float3(px, topY - 1.0f, pz - 0.35f), 0.22f, new float3(0.45f, 0.47f, 0.47f), Material.PaintedMetal, 12);
                for (var w = 0; w < 9; w++)
                {
                    var tx = px + RioRandom(random, -14f, 14f);
                    if (float.Abs(tx) > RioHalfX - 0.5f)
                        continue;
                    var downhill = random.Next(2) == 0;
                    var tz = downhill ? z + 0.03f : z + RioAlleyW - 0.03f;
                    var th = float.Min(y + RioRandom(random, 3.2f, 5.0f), downhill ? Lower(tx) : Upper(tx));
                    if (th < y + 2.6f)
                        continue;
                    RioWire(new float3(px, topY - 0.4f, pz - RioRandom(random, 0f, 0.6f)), new float3(tx, th, tz), RioRandom(random, 0.1f, 0.4f));
                }
            }
        }

        // Over the stairways.
        for (var block = 0; block < RioStairs.Length; block++)
        {
            foreach (var (x0, x1) in RioStairs[block])
            {
                for (var i = 0; i < 3; i++)
                {
                    var zw = RioTerraceZ(block) + RioAlleyW + RioRandom(random, 0.8f, RioBlockD - 0.8f);
                    var stairY = RioTerraceY(block) + RioRise * (zw - RioTerraceZ(block) - RioAlleyW) / RioBlockD;
                    var a = new float3(x0 + 0.02f, stairY + RioRandom(random, 3.0f, 4.2f), zw);
                    var b = new float3(x1 - 0.02f, stairY + RioRandom(random, 3.0f, 4.2f), zw + RioRandom(random, -1f, 1f));
                    if (a.Y > (RioTopAt(block, x0 - 0.1f) ?? 0) - 0.2f || b.Y > (RioTopAt(block, x1 + 0.1f) ?? 0) - 0.2f)
                        continue;
                    RioWire(a, b, RioRandom(random, 0.04f, 0.15f));
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ the rest of the city

    // The hills: the spur the favela climbs (falling away to either side), the valley below, and the hill across it.
    private static float RioHill(float x, float z)
    {
        var spur = 0.3f * z - 3.5f - 0.0012f * x * x;
        var across = -36f + 0.45f * (-150f - z) - 0.0004f * x * x + 8f * float.Sin(x * 0.013f) + 5f * float.Sin(z * 0.021f + x * 0.007f);
        return float.Max(float.Max(spur, across), -38f);
    }

    /// <summary>
    /// Everything you see but can't reach: the hills under a skin of grass and forest, the favela carried on down the spur,
    /// out to either side, up the hill above and all over the hill across the valley (thousands of little houses, painted
    /// every colour or bare brick, a water tank on every other roof), and a great granite peak beyond.
    /// </summary>
    private void BuildRioBackdrop(Random random)
    {
        // The ground: a height field over the whole sky box.
        var ground = RioBatch(RioGrassTint, Material.Grass);
        const float cell = 12f;
        for (var x = -480f; x < 480f; x += cell)
        {
            for (var z = -580f; z < 390f; z += cell)
            {
                var a = new float3(x, RioHill(x, z), z);
                var b = new float3(x + cell, RioHill(x + cell, z), z);
                var c = new float3(x + cell, RioHill(x + cell, z + cell), z + cell);
                var d = new float3(x, RioHill(x, z + cell), z + cell);
                var n1 = float3.Normalize(float3.Cross(c - a, b - a));
                var n2 = float3.Normalize(float3.Cross(d - a, c - a));
                ground.Triangle(RioOrigin + a, RioOrigin + b, RioOrigin + c, n1.Y < 0 ? -n1 : n1);
                ground.Triangle(RioOrigin + a, RioOrigin + c, RioOrigin + d, n2.Y < 0 ? -n2 : n2);
            }
        }

        // The houses: on the spur round the favela's core, and over the lower part of the hill across the valley.
        void Houses(float x0, float x1, float z0, float z1, float step, Func<float, float, float> density)
        {
            for (var x = x0; x < x1; x += step)
            {
                for (var z = z0; z < z1; z += step)
                {
                    var hx = x + RioRandom(random, -1.2f, 1.2f);
                    var hz = z + RioRandom(random, -1.2f, 1.2f);
                    var w = RioRandom(random, 3.8f, step + 0.6f);
                    var d = RioRandom(random, 3.8f, step + 0.6f);
                    // (Clear of the favela's core, which has houses of its own right out to its edges.)
                    if (hx + w / 2 > -RioCapX - 0.4f && hx - w / 2 < RioCapX + 0.4f && hz + d / 2 > -RioBlockD - 0.4f && hz - d / 2 < RioTerraceZ(6) + RioAlleyW + RioBlockD + 0.4f)
                        continue;
                    if (random.NextDouble() > density(hx, hz))
                        continue;
                    BackdropHouse(hx, hz, w, d);
                }
            }
        }

        void BackdropHouse(float x, float z, float w, float d)
        {
            var lo = float.MaxValue;
            var hi = float.MinValue;
            foreach (var (dx, dz) in (ReadOnlySpan<(float, float)>)[(-w / 2, -d / 2), (w / 2, -d / 2), (-w / 2, d / 2), (w / 2, d / 2)])
            {
                var hgt = RioHill(x + dx, z + dz);
                lo = float.Min(lo, hgt);
                hi = float.Max(hi, hgt);
            }
            var top = hi + RioStorey * random.Next(1, 4) + 0.3f;
            var pick = random.NextDouble();
            var (tint, material) = pick < 0.33 ? (RioBrickTint * RioRandom(random, 0.85f, 1.1f), Material.Brick)
                                 : pick < 0.42 ? (RioRender, Material.Blockwork)
                                               : (RioPick(random, RioPaint), Material.Blockwork);
            RioBox(new float3(x - w / 2, lo - 1.5f, z - d / 2), new float3(x + w / 2, top, z + d / 2), tint, material);
            // Windows, dark, on the faces looking up and down the hill; a tank on every other roof.
            foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            {
                for (var y = top - 1.9f; y > hi + 0.5f; y -= RioStorey)
                {
                    for (var u = -w / 2 + 0.9f; u < w / 2 - 0.5f; u += 1.8f)
                    {
                        if (random.NextDouble() < 0.3)
                            continue;
                        var room = RioLitWindow(x + u, y, z + side, 0.3f);
                        RioBox(new float3(x + u - 0.0f, y, z + side * d / 2), new float3(x + u + 0.9f, y + 1.1f, z + side * (d / 2 + 0.04f)), room ?? RioGlassTint, room is null ? Material.Plain : Material.Emissive);
                    }
                }
            }
            if (random.NextDouble() < 0.5)
                RioCylinder(new float3(x + w * 0.2f, top, z), new float3(x + w * 0.2f, top + 0.8f, z), 0.5f, RioTankBlue, Material.Plain, 8);
        }

        // Down the spur below the favela, and out to either side of it.
        Houses(-210f, 210f, -125f, 140f, 7.2f, (x, z) => RioHill(x, z) < -36f ? 0f : 0.9f - 0.0028f * float.Abs(x));
        // Across the valley, thinning out towards the top, gaps where the hill is too steep.
        Houses(-330f, 330f, -340f, -150f, 7.8f, (x, z) =>
        {
            var h = RioHill(x, z);
            return h > 62f ? 0f : (0.85f - float.Max(h - 30f, 0f) * 0.02f) * (0.5f + 0.5f * float.Sin(x * 0.031f + z * 0.017f) * float.Sin(z * 0.043f - x * 0.011f) + 0.5f);
        });

        // Forest where there are no houses: clumps of dark crowns on the upper slopes, and the odd tree in between.
        var forest = RioBatch(new float3(0.16f, 0.30f, 0.12f), Material.Bush);
        var forestLight = RioBatch(new float3(0.25f, 0.40f, 0.15f), Material.Bush);
        for (var i = 0; i < 900; i++)
        {
            var x = RioRandom(random, -470f, 470f);
            var z = RioRandom(random, -560f, 380f);
            var h = RioHill(x, z);
            var nearHouses = (z > -130f && z < 150f && float.Abs(x) < 200f && h > -36f) || (z < -145f && z > -345f && h < 58f && float.Abs(x) < 330f);
            if (nearHouses && random.NextDouble() < 0.85)
                continue;
            if (float.Abs(x) < RioCapX + 6 && z > -RioBlockD - 6 && z < 70f)
                continue;
            var r = RioRandom(random, 4f, 9f);
            (random.Next(3) == 0 ? forestLight : forest).Blob(RioL(x, h + r * 0.5f, z), new float3(r, r * 0.75f, r), random, 8, 5);
        }

        // The peak beyond the hill across the valley: bare granite above a skirt of forest.
        var rock = new MeshBuilder();
        rock.Hill(RioL(-170f, 40f, -500f), new float3(120f, 300f, 85f), random, 40, 16);
        rock.Hill(RioL(-40f, 40f, -540f), new float3(110f, 170f, 60f), random, 40, 14);
        _props.Add(new Prop(rock.Build(), float4x4.Identity, new float3(0.50f, 0.48f, 0.45f), Material.Rock));
        var green = RioBatch(RioGrassTint * 0.75f, Material.Grass);
        green.Hill(RioL(160f, 30f, -560f), new float3(230f, 150f, 80f), random, 40, 14);
        green.Hill(RioL(-330f, 20f, -470f), new float3(160f, 110f, 120f), random, 40, 14);
        for (var i = 0; i < 160; i++)
        {
            var a = RioRandom(random, 0f, float.Tau);
            var x = -170f + float.Cos(a) * RioRandom(random, 90f, 140f);
            var z = -500f + float.Sin(a) * RioRandom(random, 60f, 95f);
            var r = RioRandom(random, 6f, 11f);
            forest.Blob(RioL(x, 40f + RioRandom(random, 0f, 60f), z), new float3(r, r * 0.8f, r), random, 8, 5);
        }
    }
}
