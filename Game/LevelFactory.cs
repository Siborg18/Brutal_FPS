using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The outside of the warehouse: an old, half-derelict factory standing in rough grass under a blue sky with fair-weather
/// cloud (the shader's area 8, the "factory grounds": everything at x &lt; 100 outside the hall's walls and above its
/// roof, lit by the outback's sun and sky with its own shadow map).
///
///                         perimeter wall
///     +------------------------------------------------------------+
///     |      poles and wires                    [boiler house](chimney)
///     |                       ====door====  gallery                |
///     |  [outbuilding]   +-------------------+          concrete   |
///     |                  |    the hall       |   [second hall]     |
///     |                  |   (warehouse)     |                     |
///     |                  +-------------------+                     |
///     +------------------------------------------------------------+
///
/// The hall: a red-brick plinth with pilasters, a concrete frame, and above it rusty ochre corrugated cladding with two
/// ribbon windows of steel-framed glass (half the panes gone), a concrete band at the eaves and a glazed lantern along
/// the roof. In its north end, a big corrugated sliding door on a top track: press E near it and it rolls open (or
/// shut) to the east. To the north-east, a ruined two-storey brick boiler house joined to the hall by a concrete
/// gallery, and a 60 m brick chimney; to the west, a long limewashed brick outbuilding with a steel double door; to
/// the south-east, a second hall. Concrete aprons, weeds, bushes and trees, concrete poles carrying sagging wires, all
/// inside a precast concrete perimeter wall.
/// </summary>
public sealed partial class Level
{
    // The hall's outer skin (the warehouse's 1 m walls end at |x| = 21, |z| = 31; the portals are cut only that deep,
    // so the skin outside them is never cut).
    private const float SkinIn = 0.05f;      // gap between the warehouse wall and the skin
    private const float SkinT = 0.3f;        // skin thickness
    private const float RoofTop = WallHeight + 0.6f;
    private const float PlinthTop = 3.4f, BandTop = 3.9f, EavesBand = 10.0f;

    /// <summary>The big sliding door in the hall's north end: its opening (x from -3.5 to 3.5, up to 5 m).</summary>
    public const float FactoryDoorHalfWidth = 3.5f, FactoryDoorHeight = 5.0f;
    private const float DoorLeafHalf = FactoryDoorHalfWidth + 0.7f;  // the leaf laps well over the concrete jambs (no seeing round its edge)
    private const float DoorTravel = 8.4f;                       // how far it rolls east to open (its west edge clears the east jamb)
    private static readonly float FactoryDoorZ = -(HalfLength + WallThickness) - 0.85f;   // the leaf's centre, outside the wall

    /// <summary>Where the player walks around outside: the perimeter wall's inside.</summary>
    public static readonly float3 GroundsMin = new(-86, 0, -104), GroundsMax = new(86, 0, 84);

    /// <summary>The sun over the factory (the outback's: the grounds share its sky) and its shadow map's extent.</summary>
    public static float3 FactorySunDirection => OutbackSunDirection;
    public static readonly float3 FactoryShadowCenter = new(0, 0, -10);
    public const float FactoryShadowHalfExtent = 110f;

    private static readonly float3 BrickRed = new(0.50f, 0.22f, 0.15f);
    private static readonly float3 Ochre = new(0.62f, 0.48f, 0.26f);
    private static readonly float3 RustOchre = new(0.46f, 0.31f, 0.18f);
    private static readonly float3 PaleClad = new(0.58f, 0.54f, 0.44f);
    private static readonly float3 FrameConcrete = new(0.62f, 0.60f, 0.56f);
    private static readonly float3 OldSteel = new(0.30f, 0.27f, 0.25f);
    private static readonly float3 Void = new(0.015f, 0.015f, 0.016f);
    private static readonly float3 Lime = new(0.80f, 0.78f, 0.72f);

    /// <summary>The dark insides of the smaller buildings: the sky's light dies away from their doorways.</summary>
    public static ShadeVolume[] FactoryShadeVolumes =>
    [
        // (Each just past its walls' inner faces and ceiling, so they're in it too. The boiler house: its ground floor.)
        new(new Aabb(new float3(14.38f, -0.05f, -55.62f), new float3(27.62f, 4.22f, -42.38f)), new float3(21f, 1.5f, -42.2f), 5.5f, 0.14f, NoSun: true),
        new(new Aabb(new float3(-63.62f, -0.05f, -17.62f), new float3(-38.38f, 4.12f, 3.62f)), new float3(-38.2f, 1.2f, -6.2f), 7f, 0.1f, NoSun: true),    // the outbuilding
        new(new Aabb(new float3(34.28f, -0.05f, 8.28f), new float3(63.72f, 8.22f, 43.72f)), new float3(34f, 1.4f, 23f), 9f, 0.08f, NoSun: true),            // the second hall
    ];

    /// <summary>A box between two corners (any order) of the factory grounds.</summary>
    private void FBox(float3 a, float3 b, float3 tint, Material material, bool collides = true, bool blocksShots = true) =>
        _boxes.Add(new LevelBox(BoxBetween(a, b), tint, material, BlocksShots: blocksShots, Collides: collides));

    private void BuildFactoryExterior()
    {
        var random = new Random(1919);

        // The door's opening through the north wall (the catwalk above it is at 5.6 m). From y = 0 exactly: the wall
        // stands on the floor at 0, and starting the cut any higher left a sliver of wall across the doorway that the
        // player (who stands half a millimetre above the floor) couldn't walk over without jumping. Overlaps is strict,
        // so the floor slab (top at 0) isn't touched.
        CarveOpening(new Aabb(new float3(-FactoryDoorHalfWidth, 0f, -HalfLength - WallThickness - 0.4f),
                              new float3(FactoryDoorHalfWidth, FactoryDoorHeight, -HalfLength + 0.05f)));

        BuildGrounds(random);
        BuildHallSkin(random);
        BuildFactoryDoor();
        BuildFactoryRearEntrance();
        BuildFactorySideDoor();
        BuildBoilerHouse(random);
        BuildChimney(new float3(36, 0, -50), 62f, 3.1f, 1.9f);
        BuildOutbuilding(random);
        BuildSecondHall(random);
        BuildPolesAndWires();
        BuildPerimeter();
        BuildVegetation(random);
        BuildFactoryOffice(random);
        BuildVans();
        FlushPoliceBatches();   // (the merged-mesh batches are shared with the police station's builder)
    }

    // ------------------------------------------------------------------------------------------------
    // The Ford Transit vans (build_transit.py), in the hall and round the grounds, clear of the NPCs' routes.
    // ------------------------------------------------------------------------------------------------

    private void BuildVans()
    {
        var white = new float3(0.86f, 0.87f, 0.86f);
        var blue = new float3(0.10f, 0.19f, 0.36f);
        var silver = new float3(0.55f, 0.57f, 0.58f);

        // In the hall: north of the loading dock with its nose to the hall's middle, beyond the door's east side, two side
        // by side along the west wall, and two along the east wall.
        AddVan(new float3(-13.5f, 0, -15.05f), 90f, white, "al1");
        AddVan(new float3(13.5f, 0, -19.5f), 183f, white, "ro1");
        AddVan(new float3(-13.8f, 0, 8f), 1f, blue, "de1");
        AddVan(new float3(-16.8f, 0, 8f), -2f, white, "al3");
        AddVan(new float3(16f, 0, -6f), 178f, silver, "nl2");
        AddVan(new float3(15.5f, 0, 14f), 2f, white, "de3");

        // Outside: along the hall's north wall, backed up to the second hall's roller door, by the outbuilding's double
        // door, on the road up to the chained gate, by the hall's north-east corner, and north of the second hall.
        AddVan(new float3(-16f, 0, -36f), 94f, white, "nl1");
        AddVan(new float3(30.4f, 0, 23f), -90f, silver, "al2");
        AddVan(new float3(-34.5f, 0, -10.5f), 178f, blue, "ro2");
        AddVan(new float3(3.4f, 0, -84f), 3f, white, "de2");
        AddVan(new float3(24.5f, 0, -38f), 176f, white, "ro3");
        AddVan(new float3(46f, 0, 3f), -88f, blue, "al4");
        AddVan(new float3(25.5f, 0, -14f), 3f, silver, "nl3");

        // The one a factory raid can ride in on: side on to the hall's wall just west of the big door, its back to the west.
        AddVan(ArrivingVanPark, ArrivingVanYawDegrees, ArrivingVanPaint, ArrivingVanPlate, arrives: true);
    }

    /// <summary>
    /// Where the delivery van parks on a factory raid, and which way its nose points (the player's yaw, degrees): to the right
    /// of the hall's sliding door as you face it (west of it), side on to the wall with its nose to the east, its middle
    /// 7 m out from the door's face and its nose 0.5 m short of the doorway's west edge. It's backed in, so its back doors
    /// open to the west (<see cref="RaidOfficers"/> take cover there).
    /// </summary>
    public static readonly float3 ArrivingVanPark = new(-FactoryDoorHalfWidth - 0.5f - 2.99f, 0, FactoryDoorZ - 0.14f - 4f - 2.99f);
    public const float ArrivingVanYawDegrees = 90f;
    public static readonly float3 ArrivingVanPaint = new(0.86f, 0.87f, 0.86f);
    private const string ArrivingVanPlate = "nl2";

    /// <summary>The parked delivery van isn't drawn while this is set: it's still on its way (see <see cref="FactoryRaid"/>).</summary>
    public bool ArrivingVanHidden { get; set; }

    /// <summary>While <see cref="ArrivingVanHidden"/>, it still casts its shadow: it's parked, but drawn by the raid (its back doors open).</summary>
    public bool ArrivingVanCastsShadow { get; set; }

    // The parked delivery van's solid parts, which go when it drives off on a raid (SetArrivingVanThere): its collision
    // boxes (blockers, not static boxes) and its body in the physics world (movable parts, like the doors').
    private readonly List<Aabb> _arrivingVanBoxes = [];
    private readonly List<BulletSharp.CollisionObject> _arrivingVanPhysics = [];
    private float4x4 _arrivingVanModel = float4x4.Identity;
    private bool _arrivingVanThere = true;

    /// <summary>The parked delivery van is solid (true), or it's driven off and there's nothing there (false).</summary>
    public void SetArrivingVanThere(bool there)
    {
        _arrivingVanThere = there;
        for (var i = 0; i < _arrivingVanBoxes.Count; i++)
            SetBlocker($"arriving van {i}", there ? _arrivingVanBoxes[i] : null);
        for (var i = 0; i < _boxes.Count; i++)
        {
            var box = _boxes[i];
            if (!box.Visible && !box.Collides && box.BlocksShots != there && _arrivingVanBoxes.Contains(box.Bounds))
                _boxes[i] = box with { BlocksShots = there };
        }
        _version++;
        var pose = there ? _arrivingVanModel : _arrivingVanModel * float4x4.CreateTranslation(0, -500f, 0);
        foreach (var part in _arrivingVanPhysics)
            _physics?.MoveDoorPart(part, pose);
    }

    /// <summary>The delivery van's model and its number plates, to draw it moving (null if the model isn't there).</summary>
    public static (Engine.Model? Van, Engine.Model? Plates) ArrivingVanModels => (TryModel("transit"), TryModel($"transit_plate_{ArrivingVanPlate}"));

    /// <summary>
    /// A parked van, its middle over <paramref name="ground"/>, its nose towards <paramref name="yawDegrees"/> (as the
    /// player's yaw: 0 = north, -Z; 90 = east). Collision is a row of boxes that follows the side profile, short enough
    /// that a van a few degrees off square keeps a close fit. <paramref name="plate"/> names its number plates
    /// (transit_plate_*, build_plates.py). Without the model, there's no van.
    /// </summary>
    private void AddVan(float3 ground, float yawDegrees, float3 paint, string plate, bool arrives = false)
    {
        if (TryModel("transit") is not { } van)
            return;
        const float width = 2.04f;
        var yaw = float.DegreesToRadians(yawDegrees);
        var (sin, cos) = float.SinCos(yaw);
        var forward = new float3(sin, 0, -cos);

        void Span(float from, float to, float height)
        {
            var center = ground + forward * ((from + to) / 2) + new float3(0, height / 2, 0);
            var length = to - from;
            var size = new float3(float.Abs(cos) * width + float.Abs(sin) * length, height, float.Abs(sin) * width + float.Abs(cos) * length);
            if (arrives)
            {
                // (It can drive off, SetArrivingVanThere: blockers to walk into, and boxes that only stop shots and sight.)
                _arrivingVanBoxes.Add(Aabb.FromCenterSize(center, size));
                AddStatic(Aabb.FromCenterSize(center, size), default, visible: false, blocksShots: true, collides: false);
            }
            else
                AddCollider(center, size);
        }
        for (var s = -2.99f; s < 0.9f; s += 0.975f)
            Span(s, s + 0.975f, 2.76f);         // the load space and the cab under the high roof
        Span(0.91f, 1.4f, 2.3f);                // up the windscreen
        Span(1.4f, 1.9f, 1.8f);
        Span(1.9f, 2.99f, 1.35f);               // the bonnet
        var model = float4x4.CreateRotationY(-yaw) * float4x4.CreateTranslation(ground);
        if (arrives)
        {
            _arrivingVanModel = model;
            SetArrivingVanThere(true);
        }
        _props.Add(new Prop(null, model, paint, Material.Plain, Asset: van, Arrives: arrives));
        if (TryModel($"transit_plate_{plate}") is { } plates)
            _props.Add(new Prop(null, model, paint, Material.Plain, Asset: plates, Arrives: arrives));
    }

    // ------------------------------------------------------------------------------------------------
    // Ground: rough grass, concrete aprons and roads.
    // ------------------------------------------------------------------------------------------------

    private void BuildGrounds(Random random)
    {
        // Grass round the warehouse's floor slab (which already covers |x| < 22, |z| < 32).
        var grass = new float3(0.2f, 0.2f, 0.2f);
        const float fx = HalfWidth + 2, fz = HalfLength + 2;
        FBox(new float3(-150, -1, -340), new float3(98, 0, -fz), grass, Material.Grass);   // (a long way north: you can see up the road from the van)
        FBox(new float3(-150, -1, fz), new float3(98, 0, 140), grass, Material.Grass);
        FBox(new float3(-150, -1, -fz), new float3(-fx, 0, fz), grass, Material.Grass);
        FBox(new float3(fx, -1, -fz), new float3(98, 0, fz), grass, Material.Grass);

        // Concrete aprons (thin slabs you walk over): round the hall, out to the boiler house and the outbuilding,
        // a road through the gate to the north, in front of the second hall.
        var concrete = new float3(0.56f, 0.55f, 0.52f);
        void Apron(float x0, float z0, float x1, float z1) =>
            FBox(new float3(x0, 0, z0), new float3(x1, 0.015f, z1), concrete, Material.Floor, collides: false, blocksShots: false);
        Apron(-32, -48, 30, -fz);           // north, in front of the door
        Apron(-32, -fz, -fx, 20);           // west side, to the outbuilding
        Apron(fx, -fz, 28, fz);             // east side
        Apron(-6, -104, 6, -48);            // the road to the north gate
        Apron(-6, -340, 6, -104);           // ...and on outside it
        Apron(28, -2, 66, 50);              // the second hall's yard
        Apron(-66, -22, -32, 12);           // the outbuilding's yard

        // Rubble, broken slabs and weeds on the concrete.
        for (var i = 0; i < 70; i++)
        {
            var x = -30 + (float)random.NextDouble() * 56;
            var z = -46 + (float)random.NextDouble() * 12;
            if (float.Abs(x) < 12 && z > -38)
                continue;   // keep the door's approach clear
            var size = new float3(0.1f + (float)random.NextDouble() * 0.4f, 0.03f + (float)random.NextDouble() * 0.1f, 0.1f + (float)random.NextDouble() * 0.4f);
            Add(new float3(x, size.Y / 2, z), size, random.Next(3) == 0 ? BrickRed : FrameConcrete * 0.9f, random.Next(3) == 0 ? Material.Brick : Material.Concrete, collides: false);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // The hall's outside.
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// One face of the hall: a run along <paramref name="axis"/> (0 = x, 2 = z) from <paramref name="a0"/> to
    /// <paramref name="a1"/>, its skin starting at <paramref name="face"/> and going outward by <paramref name="outward"/>
    /// (+1 or -1). Pilasters at <paramref name="pilasters"/>; gaps (along the run) where a door cuts through it, up to
    /// <paramref name="gapTop"/>. <paramref name="doorInWindow"/>: a door above the plinth, cut through the lower ribbon window
    /// (its span, and how high it goes). <paramref name="keepClear"/>: no canopy brackets along this span.
    /// </summary>
    private void HallFace(int axis, float face, float outward, float a0, float a1, float[] pilasters, (float From, float To)? gap, float gapTop, Random random,
        (float From, float To, float Top)? doorInWindow = null, (float From, float To)? keepClear = null)
    {
        // A box spanning a..b along the run, from depth d0 to d1 outward of the face, y0..y1.
        void Box(float a, float b, float d0, float d1, float y0, float y1, float3 tint, Material m, bool collides = true)
        {
            var p0 = face + outward * d0;
            var p1 = face + outward * d1;
            var min = axis == 0 ? new float3(a, y0, p0) : new float3(p0, y0, a);
            var max = axis == 0 ? new float3(b, y1, p1) : new float3(p1, y1, b);
            FBox(min, max, tint, m, collides);
        }
        // The same, with the door's gap cut out of it (below gapTop).
        void Span(float a, float b, float d0, float d1, float y0, float y1, float3 tint, Material m, bool collides = true)
        {
            if (gap is { } g && y0 < gapTop && g.From < b && g.To > a)
            {
                if (g.From > a)
                    Box(a, g.From, d0, d1, y0, y1, tint, m, collides);
                if (g.To < b)
                    Box(g.To, b, d0, d1, y0, y1, tint, m, collides);
                if (y1 > gapTop)
                    Box(float.Max(a, g.From), float.Min(b, g.To), d0, d1, gapTop, y1, tint, m, collides);
                return;
            }
            Box(a, b, d0, d1, y0, y1, tint, m, collides);
        }

        var skin0 = SkinIn;
        var skin1 = SkinIn + SkinT;
        // Brick plinth, the concrete band over it, the eaves band and a rusty gutter.
        Span(a0, a1, skin0, skin1, 0, PlinthTop, BrickRed, Material.Brick);
        Span(a0, a1, skin0, skin1 + 0.4f, PlinthTop, BandTop, FrameConcrete, Material.Concrete);
        Span(a0, a1, skin0, skin1 + 0.25f, EavesBand, RoofTop, FrameConcrete * 0.95f, Material.Concrete);
        Box(a0, a1, skin1 + 0.05f, skin1 + 0.35f, RoofTop - 0.1f, RoofTop + 0.15f, OldSteel, Material.RustyMetal, collides: false);

        // Pilasters: brick below the band, concrete columns above it.
        foreach (var c in pilasters)
        {
            if (gap is { } g && c > g.From - 0.4f && c < g.To + 0.4f)
                continue;
            Box(c - 0.3f, c + 0.3f, skin1, skin1 + 0.3f, 0, PlinthTop, BrickRed * 0.95f, Material.Brick);
            Box(c - 0.22f, c + 0.22f, skin1, skin1 + 0.22f, BandTop, EavesBand, FrameConcrete, Material.Concrete);
        }

        // Between the columns: corrugated cladding with two ribbon windows.
        var cols = new List<float> { a0 };
        cols.AddRange(pilasters.Where(c => c > a0 + 0.5f && c < a1 - 0.5f));
        cols.Add(a1);
        cols.Sort();
        float[] bandsY = [4.6f, 6.1f, 7.7f, 9.2f];
        for (var i = 0; i < cols.Count - 1; i++)
        {
            var b0 = cols[i] + (i == 0 ? 0 : 0.22f);
            var b1 = cols[i + 1] - (i == cols.Count - 2 ? 0 : 0.22f);
            if (b1 - b0 < 0.3f)
                continue;
            var tint = random.Next(4) switch { 0 => RustOchre, 1 => PaleClad, _ => Ochre } * (0.9f + 0.2f * (float)random.NextDouble());
            // Solid cladding: under, between and over the windows (a panel here and there missing, showing the
            // concrete wall behind).
            Span(b0, b1, skin0, skin0 + 0.12f, BandTop, bandsY[0], tint, Material.Corrugated);
            if (random.NextDouble() > 0.12)
                Span(b0, b1, skin0, skin0 + 0.12f, bandsY[1], bandsY[2], tint, Material.Corrugated);
            Span(b0, b1, skin0, skin0 + 0.12f, bandsY[3], EavesBand, tint * 0.95f, Material.Corrugated);
            // The windows: dark behind, steel frames, glass with many panes gone.
            foreach (var (y0, y1) in (ReadOnlySpan<(float, float)>)[(bandsY[0], bandsY[1]), (bandsY[2], bandsY[3])])
            {
                if (gap is { } g && y0 < gapTop && g.From < b1 && g.To > b0)
                    continue;
                Box(b0, b1, skin0, skin0 + 0.02f, y0, y1, Void, Material.Plain, collides: false);
                var door = doorInWindow is { } d && y0 < d.Top && d.Top < y1 && b0 < d.From && d.To < b1 ? doorInWindow : null;
                RibbonWindow(axis, face, outward, b0, b1, y0, y1, skin0 + 0.1f, random, door);
            }
            // The odd cladding sheet hanging loose from one fixing.
            if (random.NextDouble() < 0.18)
            {
                var along = b0 + (float)random.NextDouble() * (b1 - b0 - 1f) + 0.5f;
                var y = bandsY[3] + 0.2f;
                var pivot = axis == 0 ? new float3(along, y, face + outward * (skin1 + 0.05f)) : new float3(face + outward * (skin1 + 0.05f), y, along);
                var swing = axis == 0 ? float4x4.CreateRotationZ(0.5f + (float)random.NextDouble() * 0.5f) : float4x4.CreateRotationX(0.5f + (float)random.NextDouble() * 0.5f);
                var size = axis == 0 ? new float3(0.9f, 2.2f, 0.03f) : new float3(0.03f, 2.2f, 0.9f);
                OBox(PB(Material.Corrugated, RustOchre * 0.9f, occludes: false), swing * float4x4.CreateTranslation(pivot), new float3(0, -1.1f, 0), size);
            }
        }

        // Rusty brackets sticking out of the band (where a canopy used to hang) and a downpipe at each end.
        for (var c = a0 + 3f; c < a1 - 2f; c += 6f)
        {
            if (gap is not null)
                break;   // (none on the door's face: the door rolls along it)
            if (keepClear is { } clear && c > clear.From && c < clear.To)
                continue;
            Box(c - 0.04f, c + 0.04f, skin1 + 0.4f, skin1 + 1.4f, BandTop + 0.6f, BandTop + 0.72f, OldSteel, Material.RustyMetal, collides: false);
        }
        foreach (var c in (ReadOnlySpan<float>)[a0 + 0.8f, a1 - 0.8f])
            Box(c - 0.07f, c + 0.07f, skin1 + 0.1f, skin1 + 0.24f, 0, RoofTop, OldSteel, Material.RustyMetal, collides: false);
    }

    /// <summary>A ribbon of steel-framed glazing (rows of small panes, about a third of them broken out), less whatever a door's been cut through.</summary>
    private void RibbonWindow(int axis, float face, float outward, float b0, float b1, float y0, float y1, float depth, Random random, (float From, float To, float Top)? door = null)
    {
        bool InDoor(float along) => door is { } d && along > d.From && along < d.To;
        float3 At(float along, float y, float d) => axis == 0 ? new float3(along, y, face + outward * d) : new float3(face + outward * d, y, along);
        float3 Size(float along, float y, float d) => axis == 0 ? new float3(along, y, d) : new float3(d, y, along);
        var frame = PB(Material.RustyMetal, OldSteel * 0.9f, occludes: false);
        var glass = PB(Material.Glass, new float3(0.22f, 0.26f, 0.28f), occludes: false);
        var milky = PB(Material.Plain, new float3(0.55f, 0.57f, 0.56f), occludes: false);
        const float pane = 0.55f;
        var columns = int.Max(1, (int)float.Round((b1 - b0) / pane));
        var w = (b1 - b0) / columns;
        const int rows = 3;
        var h = (y1 - y0) / rows;
        for (var i = 0; i <= columns; i++)
        {
            if (!InDoor(b0 + i * w))
                OBox(frame, float4x4.Identity, At(b0 + i * w, (y0 + y1) / 2, depth), Size(0.045f, y1 - y0, 0.05f));
        }

        for (var r = 0; r <= rows; r++)
        {
            var y = y0 + r * h;
            if (door is { } d && y < d.Top)
            {
                OBox(frame, float4x4.Identity, At((b0 + d.From) / 2, y, depth), Size(d.From - b0, 0.045f, 0.05f));
                OBox(frame, float4x4.Identity, At((d.To + b1) / 2, y, depth), Size(b1 - d.To, 0.045f, 0.05f));
            }
            else
                OBox(frame, float4x4.Identity, At((b0 + b1) / 2, y, depth), Size(b1 - b0, 0.045f, 0.05f));
        }

        for (var i = 0; i < columns; i++)
            for (var r = 0; r < rows; r++)
            {
                var roll = random.NextDouble();
                if (roll < 0.35 || InDoor(b0 + (i + 0.5f) * w))
                    continue;   // broken out (or a door's there now: what's left of the window goes on either side of it)
                OBox(roll < 0.45 ? milky : glass, float4x4.Identity, At(b0 + (i + 0.5f) * w, y0 + (r + 0.5f) * h, depth - outward * 0f), Size(w - 0.04f, h - 0.04f, 0.008f));
            }
    }

    private void BuildHallSkin(Random random)
    {
        const float hw = HalfWidth + WallThickness, hl = HalfLength + WallThickness;   // the warehouse walls' outside
        float[] sidePilasters = [-30f, -24f, -18f, -12f, -6f, 0f, 6f, 12f, 18f, 24f, 30f];
        float[] endPilasters = [-21f, -15f, -9f, -3.95f, 3.95f, 9f, 15f, 21f];
        float[] southPilasters = [-21f, -15f, -9f, -3f, 3f, 9f, 15f, 21f];
        HallFace(2, hw, 1, -hl, hl, sidePilasters, null, 0, random);                     // east
        HallFace(2, -hw, -1, -hl, hl, sidePilasters, null, 0, random);                   // west
        HallFace(0, hl, 1, -hw - SkinIn - SkinT, hw + SkinIn + SkinT, southPilasters, null, 0, random,     // south (with the rear door: LevelFactoryRear.cs)
            doorInWindow: (RearDoorX0, RearDoorX1, RearDeckY + RearDoorHeight), keepClear: (RearFootX, RearLandX1));
        // North: the door's opening, with a concrete frame round it.
        HallFace(0, -hl, -1, -hw - SkinIn - SkinT, hw + SkinIn + SkinT, endPilasters, (-FactoryDoorHalfWidth - 0.15f, FactoryDoorHalfWidth + 0.15f), FactoryDoorHeight + 0.6f, random);
        foreach (var x in (ReadOnlySpan<float>)[-FactoryDoorHalfWidth - 0.35f, FactoryDoorHalfWidth + 0.35f])
            FBox(new float3(x - 0.2f, 0, -hl - SkinIn), new float3(x + 0.2f, FactoryDoorHeight + 0.6f, FactoryDoorZ + 0.07f), FrameConcrete, Material.Concrete);   // (out to just behind the leaf: no gap to see past)
        FBox(new float3(-FactoryDoorHalfWidth - 0.55f, FactoryDoorHeight, -hl - SkinIn), new float3(FactoryDoorHalfWidth + 0.55f, FactoryDoorHeight + 0.6f, FactoryDoorZ + 0.07f), FrameConcrete, Material.Concrete);
        // The door's reveal through the old wall: steel angle at the jambs and head, a worn steel threshold.
        foreach (var x in (ReadOnlySpan<float>)[-FactoryDoorHalfWidth, FactoryDoorHalfWidth])
            FBox(new float3(x - 0.06f, 0, -hl - SkinIn), new float3(x + 0.06f, FactoryDoorHeight, -HalfLength + 0.05f), OldSteel, Material.RustyMetal, collides: false);
        FBox(new float3(-FactoryDoorHalfWidth, FactoryDoorHeight - 0.12f, -hl - SkinIn), new float3(FactoryDoorHalfWidth, FactoryDoorHeight, -HalfLength + 0.05f), OldSteel, Material.RustyMetal, collides: false);
        FBox(new float3(-FactoryDoorHalfWidth, -0.3f, -hl - SkinIn - SkinT - 0.3f), new float3(FactoryDoorHalfWidth, 0f, -HalfLength), OldSteel, Material.Steel);

        // Corner columns.
        foreach (var sx in (ReadOnlySpan<float>)[-1f, 1f])
            foreach (var sz in (ReadOnlySpan<float>)[-1f, 1f])
                FBox(new float3(sx * (hw + SkinIn - 0.05f), 0, sz * (hl + SkinIn - 0.05f)), new float3(sx * (hw + SkinIn + SkinT + 0.35f), RoofTop, sz * (hl + SkinIn + SkinT + 0.35f)), FrameConcrete, Material.Concrete);

        // The roof: rusty corrugated sheets over the slab, and a glazed lantern along the ridge.
        var roofW = hw + SkinIn + SkinT + 0.3f;
        var roofL = hl + SkinIn + SkinT + 0.3f;
        FBox(new float3(-roofW, RoofTop, -roofL), new float3(roofW, RoofTop + 0.1f, roofL), new float3(0.30f, 0.25f, 0.22f), Material.RustyMetal);
        const float lw = 6f, lh = 2.2f;
        var lanternTop = RoofTop + 0.1f + lh;
        foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
        {
            FBox(new float3(s * lw, RoofTop + 0.1f, -HalfLength), new float3(s * (lw + 0.15f), RoofTop + 0.4f, HalfLength), FrameConcrete, Material.Concrete);
            FBox(new float3(s * lw, RoofTop + 0.4f, -HalfLength), new float3(s * (lw + 0.02f), lanternTop - 0.1f, HalfLength), Void, Material.Plain, collides: false);
            for (var z = -HalfLength; z < HalfLength - 0.1f; z += 6f)
            {
                RibbonWindow(2, s * lw, s, z + 0.12f, z + 5.88f, RoofTop + 0.4f, lanternTop - 0.1f, 0.08f, random);
                FBox(new float3(s * lw, RoofTop + 0.4f, z - 0.12f), new float3(s * (lw + 0.2f), lanternTop, z + 0.12f), OldSteel, Material.RustyMetal);
            }
        }
        foreach (var sz in (ReadOnlySpan<float>)[-1f, 1f])
            FBox(new float3(-lw - 0.15f, RoofTop + 0.1f, sz * HalfLength), new float3(lw + 0.15f, lanternTop, sz * (HalfLength + 0.15f)), Ochre * 0.85f, Material.Corrugated);
        FBox(new float3(-lw - 0.6f, lanternTop, -HalfLength - 0.5f), new float3(lw + 0.6f, lanternTop + 0.12f, HalfLength + 0.5f), new float3(0.28f, 0.24f, 0.21f), Material.RustyMetal);
        // A couple of roof vents.
        foreach (var z in (ReadOnlySpan<float>)[-20f, 8f])
            foreach (var x in (ReadOnlySpan<float>)[-13f, 13f])
            {
                OCyl(PB(Material.Galvanized, new float3(0.5f, 0.5f, 0.48f), occludes: false), float4x4.Identity, new float3(x, RoofTop + 0.1f, z), new float3(x, RoofTop + 1.4f, z), 0.35f, 0.35f, 14);
                OCyl(PB(Material.Galvanized, new float3(0.45f, 0.45f, 0.43f), occludes: false), float4x4.Identity, new float3(x, RoofTop + 1.4f, z), new float3(x, RoofTop + 1.75f, z), 0.6f, 0.05f, 14);
            }
    }

    // ------------------------------------------------------------------------------------------------
    // The sliding door.
    // ------------------------------------------------------------------------------------------------

    private readonly List<(Mesh Mesh, float3 Tint, Material Material)> _doorParts = [];
    private readonly List<BulletSharp.CollisionObject> _factoryDoorPhysics = [];
    private readonly List<LevelBox> _movingBoxes = [];
    private float _doorOpen;          // 0 shut .. 1 open (eased while it moves)
    private float _doorTarget;
    private float _doorT;             // 0..1 along the move, before easing

    private readonly Dictionary<string, LevelBox> _blockers = [];

    /// <summary>
    /// Colliders that move or come and go (the factory's sliding door; a portal's doorway while it's offline): the
    /// player collides with these as well as <see cref="Boxes"/>.
    /// </summary>
    public List<LevelBox> MovingBoxes
    {
        get
        {
            if (_blockers.Count == 0)
                return _movingBoxes;
            // (One list of both, put together again only after either has changed: the player and the NPCs' map go
            // through this several times a frame, and a Concat would be new garbage each time.)
            if (_movingAndBlockersStale)
            {
                _movingAndBlockersStale = false;
                _movingAndBlockers.Clear();
                _movingAndBlockers.AddRange(_movingBoxes);
                foreach (var blocker in _blockers.Values)
                    _movingAndBlockers.Add(blocker);
            }

            return _movingAndBlockers;
        }
    }

    private readonly List<LevelBox> _movingAndBlockers = [];
    private bool _movingAndBlockersStale = true;

    /// <summary>Puts an invisible blocking box named <paramref name="key"/> at <paramref name="bounds"/>, or removes it (null).</summary>
    public void SetBlocker(string key, Aabb? bounds)
    {
        if (bounds is { } b)
            _blockers[key] = new LevelBox(b, default, Visible: false, BlocksShots: false);
        else
            _blockers.Remove(key);
        _movingAndBlockersStale = true;
    }

    /// <summary>"[E] Open door" / "[E] Close door" while the player is near the factory door, otherwise null.</summary>
    public string? FactoryDoorPrompt { get; private set; }

    /// <summary>True while the sliding door is rolling (for a sound).</summary>
    public bool FactoryDoorMoving => _doorT > 0f && _doorT < 1f && _doorOpen != _doorTarget;

    private static float DoorCenterX(float open) => open * DoorTravel;

    private void BuildFactoryDoor()
    {
        // The track: a steel beam over the opening and on along the wall to the east, on brackets.
        var hl = HalfLength + WallThickness + SkinIn + SkinT;
        FBox(new float3(-DoorLeafHalf - 0.4f, FactoryDoorHeight + 0.62f, FactoryDoorZ - 0.12f), new float3(DoorLeafHalf + DoorTravel + 0.4f, FactoryDoorHeight + 0.8f, FactoryDoorZ + 0.12f), OldSteel, Material.RustyMetal, collides: false);
        for (var x = -DoorLeafHalf - 0.2f; x < DoorLeafHalf + DoorTravel + 0.4f; x += 2.2f)
            FBox(new float3(x - 0.06f, FactoryDoorHeight + 0.62f, FactoryDoorZ + 0.1f), new float3(x + 0.06f, FactoryDoorHeight + 0.9f, -hl), OldSteel, Material.RustyMetal, collides: false);
        // A floor guide (a stub post the leaf's bottom runs past) at each end of its travel.
        foreach (var x in (ReadOnlySpan<float>)[-DoorLeafHalf - 0.3f, DoorLeafHalf + DoorTravel + 0.3f])
            FBox(new float3(x - 0.08f, 0, FactoryDoorZ - 0.16f), new float3(x + 0.08f, 0.35f, FactoryDoorZ - 0.08f), OldSteel, Material.RustyMetal);

        // The leaf, built round its own centre at floor level (moved as a whole when drawn): corrugated sheet on a
        // steel frame with cross bracing, hung from two trolleys on the track, a pull handle on each face.
        var w = DoorLeafHalf * 2;
        var h = FactoryDoorHeight + 0.55f;
        var sheet = new MeshBuilder();
        var frame = new MeshBuilder();
        var dark = new MeshBuilder();
        var id = float4x4.Identity;
        OBox(sheet, id, new float3(0, h / 2 + 0.03f, 0), new float3(w - 0.1f, h - 0.1f, 0.05f));
        foreach (var x in (ReadOnlySpan<float>)[-w / 2 + 0.05f, -w / 6, w / 6, w / 2 - 0.05f])
            OBox(frame, id, new float3(x, h / 2 + 0.03f, -0.06f), new float3(0.12f, h, 0.08f));
        foreach (var y in (ReadOnlySpan<float>)[0.08f, h * 0.5f, h - 0.02f])
            OBox(frame, id, new float3(0, y, -0.06f), new float3(w, 0.12f, 0.08f));
        for (var i = 0; i < 3; i++)
        {
            var x0 = -w / 2 + i * w / 3;
            foreach (var (ya, yb) in (ReadOnlySpan<(float, float)>)[(0.08f, h * 0.5f), (h * 0.5f, h - 0.02f)])
            {
                var a = new float3(x0 + 0.08f, ya, -0.06f);
                var b = new float3(x0 + w / 3 - 0.08f, yb, -0.06f);
                var mid = (a + b) / 2;
                var len = float3.Distance(a, b);
                var ang = float.Atan2(b.Y - a.Y, b.X - a.X);
                OBox(frame, float4x4.CreateRotationZ(ang) * float4x4.CreateTranslation(mid), float3.Zero, new float3(len, 0.08f, 0.05f));
            }
        }
        foreach (var x in (ReadOnlySpan<float>)[-w / 2 + 0.8f, w / 2 - 0.8f])
        {
            OBox(dark, id, new float3(x, h + 0.12f, 0), new float3(0.3f, 0.3f, 0.12f));
            OCyl(dark, id, new float3(x, h + 0.26f, -0.1f), new float3(x, h + 0.26f, 0.1f), 0.11f, 0.11f, 12);
        }
        foreach (var z in (ReadOnlySpan<float>)[-0.16f, 0.06f])
        {
            OBox(dark, id, new float3(-w / 2 + 1.0f, 1.15f, z), new float3(0.04f, 0.6f, 0.04f));
            OBox(dark, id, new float3(-w / 2 + 1.0f, 0.88f, z + (z < 0 ? 0.04f : -0.04f)), new float3(0.04f, 0.04f, 0.1f));
            OBox(dark, id, new float3(-w / 2 + 1.0f, 1.42f, z + (z < 0 ? 0.04f : -0.04f)), new float3(0.04f, 0.04f, 0.1f));
        }
        _doorParts.Add((sheet.Build(), RustOchre, Material.Corrugated));
        _doorParts.Add((frame.Build(), OldSteel, Material.RustyMetal));
        _doorParts.Add((dark.Build(), new float3(0.18f, 0.17f, 0.16f), Material.Steel));
        _movingBoxes.Add(DoorCollider(0));
        _movingAndBlockersStale = true;
    }

    private static LevelBox DoorCollider(float open)
    {
        var cx = DoorCenterX(open);
        var half = DoorLeafHalf;
        return new LevelBox(new Aabb(new float3(cx - half, 0, FactoryDoorZ - 0.14f), new float3(cx + half, FactoryDoorHeight + 0.6f, FactoryDoorZ + 0.06f)),
            default, Visible: false, BlocksShots: false);
    }

    /// <summary>
    /// The sliding door: near it, E opens or closes it; it rolls over 3 s, slow to start and stop. It won't close on
    /// the player standing in the doorway.
    /// </summary>
    public void UpdateFactoryDoor(float dt, bool usePressed, float3 player)
    {
        if (_factoryGate is { } gate)
            StepDoor(gate, dt);
        var near = player.X > -FactoryDoorHalfWidth - 2.5f && player.X < FactoryDoorHalfWidth + 2.5f
                   && player.Z > FactoryDoorZ - 5f && player.Z < -HalfLength + 4f && player.Y < 3f && player.Y > -1f;
        var inDoorway = float.Abs(player.X) < FactoryDoorHalfWidth + 0.4f && player.Z > FactoryDoorZ - 0.6f && player.Z < -HalfLength + 0.4f;
        FactoryDoorPrompt = near ? (_doorTarget > 0.5f ? "[E] Close door" : "[E] Open door") : null;
        if (near && usePressed && !(inDoorway && _doorTarget > 0.5f) && !(FactoryDoorHeld && _doorTarget > 0.5f))
        {
            _doorTarget = _doorTarget > 0.5f ? 0f : 1f;
            _doorT = 0f;
            Log.Write(_doorTarget > 0.5f ? "[DOOR] Opening the factory door" : "[DOOR] Closing the factory door");
        }

        if (_doorOpen == _doorTarget)
            return;
        var from = _doorTarget > 0.5f ? 0f : 1f;
        _doorT = float.Min(1f, _doorT + dt / 3f);
        var eased = _doorT * _doorT * (3f - 2f * _doorT);
        _doorOpen = from + (_doorTarget - from) * eased;
        if (_doorT >= 1f)
            _doorOpen = _doorTarget;
        PlaceFactoryDoor();
    }

    /// <summary>The door's colliders, for the player and for shots, where it is now.</summary>
    private void PlaceFactoryDoor()
    {
        _movingBoxes[0] = DoorCollider(_doorOpen);
        _movingAndBlockersStale = true;
        if (_physics is null)
            return;
        var pose = FactoryDoorPose(_doorOpen);
        foreach (var part in _factoryDoorPhysics)
            _physics.MoveDoorPart(part, pose);
    }

    private static float4x4 FactoryDoorPose(float open) => float4x4.CreateTranslation(DoorCenterX(open), 0.02f, FactoryDoorZ);

    /// <summary>The army's lead car is coming through: the big door won't shut till it's in (Game/FactoryArmy.cs).</summary>
    public bool FactoryDoorHeld { get; set; }

    /// <summary>The big door has rolled right open.</summary>
    public bool FactoryDoorFullyOpen => _doorOpen >= 0.98f;

    /// <summary>Starts the big door rolling open (if it isn't already open or opening).</summary>
    public void OpenFactoryDoor()
    {
        if (_doorTarget > 0.5f)
            return;
        _doorTarget = 1f;
        _doorT = 0f;
        Log.Write("[DOOR] The factory door's being rolled open (the army)");
    }

    /// <summary>The hall's big sliding door is open or opening.</summary>
    public bool FactoryDoorOpening => _doorTarget > 0.5f;

    /// <summary>The hall's rear door, at the top of the outside steps, is open or opening.</summary>
    public bool FactoryRearDoorOpening => _factoryRearDoor is { Target: > 0.5f };

    /// <summary>Opens (or shuts) the door straight away (BRUTALFPS_DOOR=open, for screenshots).</summary>
    public void SetFactoryDoor(bool open)
    {
        _doorOpen = _doorTarget = open ? 1f : 0f;
        _doorT = 1f;
        PlaceFactoryDoor();
    }

    private void DrawFactoryDoor(Renderer renderer)
    {
        var model = FactoryDoorPose(_doorOpen);
        foreach (var (mesh, tint, material) in _doorParts)
            renderer.DrawMesh(mesh, model, tint, ShadeMode.Lit, material);
        if (_factoryGate is { } gate)
            DrawDoor(renderer, gate);
        if (_factoryRearDoor is { } rear)
            DrawDoor(renderer, rear);
        if (_factorySideDoor is { } side)
            DrawDoor(renderer, side);
    }

    // ------------------------------------------------------------------------------------------------
    // The boiler house, the gallery to the hall, and the chimney.
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// A brick wall along x (axis 0) or z (axis 2) from a0 to a1 at <paramref name="at"/> (its inner face), thickness
    /// <paramref name="t"/> outward, from y0 to y1, with window openings (along, bottom, width, height) left empty.
    /// </summary>
    private void BrickWall(int axis, float at, float t, float a0, float a1, float y0, float y1, float3 tint, Material material, IEnumerable<(float A, float Y, float W, float H)> holes, bool lintels = true)
    {
        float3 P(float a, float y, float d) => axis == 0 ? new float3(a, y, at + d) : new float3(at + d, y, a);
        // Cut the wall into horizontal strips at the holes' edges, then each strip into runs between holes.
        // Nothing overlaps: the concrete lintel over an opening (20 cm, 15 cm past each side) is the piece of wall there
        // with its faces painted concrete, and on a limewashed wall the reveals, sills and soffits are painted plain
        // lime render (the flaking wash, worked out on each face on its own, never met at the corners). A lintel as a
        // box of its own sat inside the wall, its underside flush with the opening's top: the two z-fought.
        const float lintelH = 0.2f, lintelOver = 0.15f;
        var list = holes.ToList();
        var lintelList = lintels ? list.Where(hl => hl.Y + hl.H < y1 - lintelH).ToList() : [];
        var ys = new SortedSet<float> { y0, y1 };
        foreach (var hole in list)
        {
            ys.Add(float.Clamp(hole.Y, y0, y1));
            ys.Add(float.Clamp(hole.Y + hole.H, y0, y1));
        }
        foreach (var hole in lintelList)
            ys.Add(hole.Y + hole.H + lintelH);
        // Face bits: along the wall (its ends), and across it (its two faces).
        int alongPlus = axis == 0 ? LevelBox.FacePosX : LevelBox.FacePosZ, alongMinus = axis == 0 ? LevelBox.FaceNegX : LevelBox.FaceNegZ;
        var across = axis == 0 ? LevelBox.FacePosZ | LevelBox.FaceNegZ : LevelBox.FacePosX | LevelBox.FaceNegX;
        var render = material == Material.Whitewash;
        var levels = ys.ToArray();
        var marks = new SortedSet<float>();
        for (var i = 0; i < levels.Length - 1; i++)
        {
            var ya = levels[i];
            var yb = levels[i + 1];
            var cuts = list.Where(hl => hl.Y < yb - 1e-3f && hl.Y + hl.H > ya + 1e-3f).ToList();
            var bands = lintelList.Where(hl => hl.Y + hl.H < yb - 1e-3f && hl.Y + hl.H + lintelH > ya + 1e-3f).ToList();
            marks.Clear();
            marks.Add(a0);
            marks.Add(a1);
            foreach (var hole in cuts)
            {
                marks.Add(float.Clamp(hole.A, a0, a1));
                marks.Add(float.Clamp(hole.A + hole.W, a0, a1));
            }
            foreach (var hole in bands)
            {
                marks.Add(float.Clamp(hole.A - lintelOver, a0, a1));
                marks.Add(float.Clamp(hole.A + hole.W + lintelOver, a0, a1));
            }
            // Each span between marks is an opening (left empty), lintel, or wall; runs of the same kind make one box.
            int Kind(float mid) => cuts.Any(hl => mid > hl.A && mid < hl.A + hl.W) ? 0
                : bands.Any(hl => mid > hl.A - lintelOver && mid < hl.A + hl.W + lintelOver) ? 1 : 2;
            var m = marks.ToArray();
            var j = 0;
            while (j < m.Length - 1)
            {
                var kind = Kind((m[j] + m[j + 1]) / 2);
                var k = j + 1;
                while (k < m.Length - 1 && Kind((m[k] + m[k + 1]) / 2) == kind)
                    k++;
                float lo = m[j], hi = m[k];
                j = k;
                if (kind == 0 || hi - lo < 1e-3f)
                    continue;
                var bounds = BoxBetween(P(lo, ya, 0), P(hi, yb, t));
                if (kind == 1)
                {
                    _boxes.Add(new LevelBox(bounds, tint, material, Paint: FrameConcrete * 0.9f, PaintFaces: across | LevelBox.FaceNegY, PaintMaterial: Material.Concrete));
                    continue;
                }
                var faces = 0;
                if (render)
                {
                    // Reveals (an end against an opening), a sill (an opening on top), a soffit (one below, no lintel).
                    if (cuts.Any(hl => float.Abs(hl.A + hl.W - lo) < 1e-3f))
                        faces |= alongMinus;
                    if (cuts.Any(hl => float.Abs(hl.A - hi) < 1e-3f))
                        faces |= alongPlus;
                    if (list.Any(hl => float.Abs(hl.Y - yb) < 1e-3f && hl.A < hi && hl.A + hl.W > lo))
                        faces |= LevelBox.FacePosY;
                    if (list.Any(hl => float.Abs(hl.Y + hl.H - ya) < 1e-3f && hl.A < hi && hl.A + hl.W > lo))
                        faces |= LevelBox.FaceNegY;
                }
                _boxes.Add(faces == 0
                    ? new LevelBox(bounds, tint, material)
                    : new LevelBox(bounds, tint, material, Paint: tint * 1.06f, PaintFaces: faces, PaintMaterial: Material.WallPaint));
            }
        }
    }

    private void BuildBoilerHouse(Random random)
    {
        // Two storeys of brick round a concrete frame, roofless in places, every window empty (photo: the ruined
        // blocks at the chimney's foot).
        const float x0 = 14f, x1 = 28f, z0 = -56f, z1 = -42f, t = 0.4f;
        const float storey = 4.2f, top = 8.6f;
        var holes = new List<(float, float, float, float)>();
        IEnumerable<(float, float, float, float)> Windows(float a0, float a1, bool door)
        {
            var spots = new List<(float, float, float, float)>();
            for (var a = a0 + 1.4f; a < a1 - 1.6f; a += 2.6f)
            {
                spots.Add((a, 1.2f, 1.2f, 1.8f));
                spots.Add((a, storey + 1.0f, 1.2f, 1.9f));
            }
            if (door)
                spots.Add(((a0 + a1) / 2 - 1.2f, 0, 2.4f, 3.2f));
            return spots;
        }
        BrickWall(0, z1 - t, t, x0, x1, 0, top, BrickRed * 0.9f, Material.Brick, Windows(x0, x1, door: true).Where(h => h.Item1 > (x0 + x1) / 2 + 1.5f || h.Item1 < (x0 + x1) / 2 - 3.5f || h.Item2 < 0.1f));
        BrickWall(0, z0, t, x0, x1, 0, top, BrickRed * 0.85f, Material.Brick, Windows(x0, x1, door: false));
        BrickWall(2, x0, t, z0 + t, z1 - t, 0, top, BrickRed * 0.92f, Material.Brick, Windows(z0, z1, door: false));
        BrickWall(2, x1 - t, t, z0 + t, z1 - t, 0, top - 1.6f, BrickRed * 0.88f, Material.Brick, Windows(z0, z1, door: true));   // the east wall half fallen
        // The concrete frame: columns at the corners and mid-walls, a floor slab with a hole, a roof slab half gone.
        foreach (var x in (ReadOnlySpan<float>)[x0 + 0.25f, (x0 + x1) / 2, x1 - 0.25f])
            foreach (var z in (ReadOnlySpan<float>)[z0 + 0.25f, z1 - 0.25f])
                FBox(new float3(x - 0.25f, 0, z - 0.25f), new float3(x + 0.25f, top + 0.3f, z + 0.25f), FrameConcrete * 0.92f, Material.Concrete);
        FBox(new float3(x0, storey, z0), new float3((x0 + x1) / 2 - 1.5f, storey + 0.3f, z1), FrameConcrete * 0.85f, Material.Concrete);
        FBox(new float3((x0 + x1) / 2 + 1.5f, storey, z0), new float3(x1, storey + 0.3f, z1), FrameConcrete * 0.85f, Material.Concrete);
        FBox(new float3(x0, top, z0), new float3(x1 - 5f, top + 0.3f, z1), FrameConcrete * 0.8f, Material.Concrete);
        // Roof beams left where the roof fell in, a smaller brick tower on top at the west end (with its own
        // little chimney), and rubble inside.
        for (var x = x1 - 4.5f; x < x1; x += 1.5f)
            FBox(new float3(x - 0.12f, top, z0), new float3(x + 0.12f, top + 0.3f, z1), FrameConcrete * 0.8f, Material.Concrete);
        BrickWall(0, z1 - 4.5f, 0.3f, x0, x0 + 5f, top + 0.3f, top + 3.6f, BrickRed * 0.9f, Material.Brick, [(x0 + 1.5f, top + 1.2f, 1.2f, 1.4f)]);
        BrickWall(0, z0, 0.3f, x0, x0 + 5f, top + 0.3f, top + 3.6f, BrickRed * 0.86f, Material.Brick, [(x0 + 2.2f, top + 1.2f, 1.2f, 1.4f)]);
        BrickWall(2, x0, 0.3f, z0 + 0.3f, z1 - 4.5f, top + 0.3f, top + 3.6f, BrickRed * 0.88f, Material.Brick, [(z0 + 3.5f, top + 1.2f, 1.2f, 1.4f)]);
        BrickWall(2, x0 + 4.7f, 0.3f, z0 + 0.3f, z1 - 4.5f, top + 0.3f, top + 3.6f, BrickRed * 0.92f, Material.Brick, []);
        FBox(new float3(x0 - 0.2f, top + 3.6f, z0 - 0.2f), new float3(x0 + 5.2f, top + 3.85f, z1 - 4.3f), FrameConcrete * 0.85f, Material.Concrete);
        OCyl(PB(Material.Brick, BrickRed * 0.8f), float4x4.Identity, new float3(x0 + 2.5f, top + 3.85f, z0 + 3f), new float3(x0 + 2.5f, top + 5.6f, z0 + 3f), 0.35f, 0.3f, 10);
        for (var i = 0; i < 26; i++)
        {
            var p = new float3(x0 + 1 + (float)random.NextDouble() * (x1 - x0 - 2), 0, z0 + 1 + (float)random.NextDouble() * (z1 - z0 - 2));
            var s = new float3(0.3f + (float)random.NextDouble() * 0.6f, 0.1f + (float)random.NextDouble() * 0.3f, 0.3f + (float)random.NextDouble() * 0.6f);
            Add(p + new float3(0, s.Y / 2, 0), s, random.Next(2) == 0 ? BrickRed * 0.9f : FrameConcrete * 0.8f, random.Next(2) == 0 ? Material.Brick : Material.Concrete, collides: false);
        }

        // The gallery: a concrete frame bridge from the boiler house to the hall's north-east corner, carrying pipes.
        const float gx0 = 15.5f, gx1 = 19.5f, gy = 5.4f;
        var gz0 = -HalfLength - WallThickness - SkinIn - SkinT;
        for (var z = z1; z <= gz0 - 0.5f; z += 3.5f)
            foreach (var x in (ReadOnlySpan<float>)[gx0, gx1])
                FBox(new float3(x - 0.2f, 0, z - 0.2f), new float3(x + 0.2f, gy + 2.4f, z + 0.2f), FrameConcrete * 0.9f, Material.Concrete);
        FBox(new float3(gx0 - 0.2f, gy, z1), new float3(gx1 + 0.2f, gy + 0.35f, gz0), FrameConcrete * 0.85f, Material.Concrete);
        FBox(new float3(gx0 - 0.2f, gy + 2.4f, z1), new float3(gx1 + 0.2f, gy + 2.7f, gz0), FrameConcrete * 0.85f, Material.Concrete);
        foreach (var x in (ReadOnlySpan<float>)[gx0, gx1])
            FBox(new float3(x - 0.2f, gy + 0.35f, z1), new float3(x + 0.2f, gy + 1.0f, gz0), FrameConcrete * 0.88f, Material.Concrete);
        var pipe = PB(Material.RustyMetal, OldSteel, occludes: false);
        foreach (var (dx, dy, r) in (ReadOnlySpan<(float, float, float)>)[(0.8f, 1.3f, 0.22f), (1.6f, 1.3f, 0.16f), (2.6f, 1.25f, 0.3f)])
            OCyl(pipe, float4x4.Identity, new float3(gx0 + dx, gy + dy, z1 - 0.5f), new float3(gx0 + dx, gy + dy, gz0 - 0.1f), r, r, 12);
    }

    /// <summary>A tall round brick chimney on a square plinth: tapering, iron bands, a corbelled cap, a ladder.</summary>
    private void BuildChimney(float3 foot, float height, float r0, float r1)
    {
        // The plinth, with a bricked-up arch (the old flue entry) on its west side.
        const float plinth = 4.5f;
        var half = r0 + 0.6f;
        FBox(foot + new float3(-half, 0, -half), foot + new float3(half, plinth, half), BrickRed * 0.85f, Material.Brick);
        FBox(foot + new float3(-half - 0.2f, plinth, -half - 0.2f), foot + new float3(half + 0.2f, plinth + 0.35f, half + 0.2f), FrameConcrete * 0.85f, Material.Concrete);
        FBox(foot + new float3(-half - 0.05f, 0.4f, -1.2f), foot + new float3(-half, 2.8f, 1.2f), BrickRed * 0.7f, Material.Brick, collides: false);
        // The stack, in sections (darker with soot towards the top).
        const int sections = 6;
        var bands = PB(Material.RustyMetal, OldSteel * 0.8f);
        for (var i = 0; i < sections; i++)
        {
            var ya = plinth + 0.35f + (height - plinth) * i / sections;
            var yb = plinth + 0.35f + (height - plinth) * (i + 1) / sections;
            var ra = r0 + (r1 - r0) * i / sections;
            var rb = r0 + (r1 - r0) * (i + 1) / sections;
            var soot = 1f - 0.45f * (i / (float)(sections - 1)) * (i / (float)(sections - 1));
            OCyl(PB(Material.Brick, BrickRed * soot * (i % 2 == 0 ? 1f : 0.96f)), float4x4.Identity, foot + new float3(0, ya, 0), foot + new float3(0, yb, 0), ra, rb, 32);
            OCyl(bands, float4x4.Identity, foot + new float3(0, yb - 0.15f, 0), foot + new float3(0, yb + 0.1f, 0), rb + 0.06f, rb + 0.06f, 32);
        }
        // The cap: corbelled courses stepping out, then the dark rim.
        var capY = plinth + 0.35f + (height - plinth);
        OCyl(PB(Material.Brick, BrickRed * 0.5f), float4x4.Identity, foot + new float3(0, capY, 0), foot + new float3(0, capY + 0.5f, 0), r1 + 0.25f, r1 + 0.35f, 32);
        OCyl(PB(Material.Brick, BrickRed * 0.42f), float4x4.Identity, foot + new float3(0, capY + 0.5f, 0), foot + new float3(0, capY + 1.3f, 0), r1 + 0.35f, r1 + 0.3f, 32);
        OCyl(PB(Material.Plain, Void), float4x4.Identity, foot + new float3(0, capY + 1.31f, 0), foot + new float3(0, capY + 1.32f, 0), r1 - 0.1f, r1 - 0.1f, 32);
        // A lightning conductor up the side, and iron rungs on the north face.
        var rod = PB(Material.Steel, new float3(0.25f, 0.27f, 0.25f), occludes: false);
        AddTrunk(rod, foot + new float3(r0 + 0.05f, plinth, 0.3f), foot + new float3(r1 + 0.05f, capY + 2.4f, 0.3f), 0.02f, 0.02f, 5);
        for (var y = plinth + 1f; y < capY; y += 0.4f)
        {
            var r = r0 + (r1 - r0) * (y - plinth) / (height - plinth);
            OBox(bands, float4x4.Identity, foot + new float3(0, y, -r - 0.12f), new float3(0.5f, 0.03f, 0.03f));
        }
        // Collision: the plinth is a box already; the stack as a box that fits inside it.
        AddCollider(foot + new float3(0, (plinth + capY) / 2, 0), new float3(r1 * 1.6f, capY - plinth, r1 * 1.6f));
    }

    // ------------------------------------------------------------------------------------------------
    // The outbuilding (west), the second hall (south-east), poles and wires, the perimeter wall, plants.
    // ------------------------------------------------------------------------------------------------

    private void BuildOutbuilding(Random random)
    {
        // A long single-storey limewashed brick block, its front (east) facing the hall: a steel double door, a
        // doorway with no door, small square windows; behind its parapet a raised clerestory of red brick with a
        // band of windows, and a tall steel flue.
        const float x0 = -64f, x1 = -38f, z0 = -18f, z1 = 4f, h = 4.4f, t = 0.4f;
        BrickWall(2, x1 - t, t, z0, z1, 0, h, Lime, Material.Whitewash,
            [(z0 + 2.0f, 0, 2.4f, 2.6f), (z0 + 7.0f, 0, 1.2f, 2.3f), (z0 + 5.0f, 1.6f, 0.8f, 0.8f), (z0 + 9.5f, 1.6f, 0.8f, 0.8f), (z0 + 12.0f, 1.6f, 0.8f, 0.8f),
             (z0 + 14.6f, 0, 1.6f, 2.6f), (z0 + 17.5f, 1.6f, 0.8f, 0.8f), (z0 + 19.6f, 1.6f, 0.8f, 0.8f)]);
        BrickWall(2, x0, t, z0, z1, 0, h, Lime * 0.95f, Material.Whitewash, [(z0 + 4f, 1.6f, 0.8f, 0.8f), (z0 + 12f, 1.6f, 0.8f, 0.8f)]);
        BrickWall(0, z0, t, x0 + t, x1 - t, 0, h, Lime * 0.97f, Material.Whitewash, [(x0 + 8f, 1.6f, 0.8f, 0.8f), (x0 + 16f, 1.6f, 0.8f, 0.8f)]);
        BrickWall(0, z1 - t, t, x0 + t, x1 - t, 0, h, Lime * 0.97f, Material.Whitewash, [(x0 + 10f, 1.6f, 0.8f, 0.8f)]);
        FBox(new float3(x0, h - 0.3f, z0), new float3(x1, h, z1), FrameConcrete * 0.8f, Material.Concrete);
        // The steel double door (shut) in the first opening; you can walk in through the two open doorways. It's dark
        // inside (FactoryShadeVolumes), with some old crates left in it.
        var doorX = x1 + 0.02f;
        FBox(new float3(doorX, 0, z0 + 2.0f), new float3(doorX + 0.06f, 2.6f, z0 + 4.4f), new float3(0.72f, 0.73f, 0.70f), Material.PaintedMetal);
        FBox(new float3(doorX + 0.06f, 0.1f, z0 + 3.18f), new float3(doorX + 0.08f, 2.5f, z0 + 3.22f), new float3(0.35f, 0.35f, 0.34f), Material.Steel, collides: false);
        foreach (var dz in (ReadOnlySpan<float>)[2.9f, 3.5f])
            FBox(new float3(doorX + 0.06f, 1.1f, z0 + dz), new float3(doorX + 0.1f, 1.3f, z0 + dz + 0.04f), new float3(0.3f, 0.3f, 0.3f), Material.Steel, collides: false);
        AddCrate(new float3(-48f, 0, -9f), 1.3f, WoodTint);
        AddCrate(new float3(-53f, 0, -8f), 1.1f, WoodTint * 0.85f, 1);
        AddCrate(new float3(-56f, 0, 0f), 1.4f, WoodTint * 0.9f, 2);
        AddCrate(new float3(-43f, 0, -15f), 1.2f, WoodTint, 3);
        // The raised clerestory behind the parapet: red brick and a band of windows, a flat roof.
        const float cx0 = x0 + 3f, cx1 = x1 - 6f, cz0 = z0 + 3f, cz1 = z1 - 3f, ch = h + 2.4f;
        FBox(new float3(cx0, h, cz0), new float3(cx1, h + 0.8f, cz1), BrickRed, Material.Brick);
        FBox(new float3(cx0, h + 0.8f, cz0), new float3(cx1, ch - 0.4f, cz1), Void, Material.Plain);
        RibbonWindow(2, cx1, 1, cz0 + 0.2f, cz1 - 0.2f, h + 0.85f, ch - 0.45f, 0.05f, random);
        RibbonWindow(0, cz0, -1, cx0 + 0.2f, cx1 - 0.2f, h + 0.85f, ch - 0.45f, 0.05f, random);
        RibbonWindow(0, cz1, 1, cx0 + 0.2f, cx1 - 0.2f, h + 0.85f, ch - 0.45f, 0.05f, random);
        FBox(new float3(cx0 - 0.2f, ch - 0.4f, cz0 - 0.2f), new float3(cx1 + 0.2f, ch, cz1 + 0.2f), FrameConcrete * 0.8f, Material.Concrete);
        // Weeds growing along the parapet, a tall steel flue at the north end, a railing on the roof.
        for (var z = z0 + 1f; z < z1 - 1f; z += 2.3f)
        {
            var b = new MeshBuilder();
            b.Blob(new float3(x1 - 1f, h + 0.25f, z), new float3(0.35f, 0.3f, 0.35f), random, 6, 4);
            _props.Add(new Prop(b.Build(), float4x4.Identity, new float3(0.22f, 0.36f, 0.12f), Material.Bush));
        }
        OCyl(PB(Material.RustyMetal, new float3(0.18f, 0.16f, 0.15f)), float4x4.Identity, new float3(x0 + 1.5f, 0, z0 - 0.6f), new float3(x0 + 1.5f, 11f, z0 - 0.6f), 0.25f, 0.22f, 12);
        var rail = PB(Material.RustyMetal, new float3(0.4f, 0.12f, 0.1f), occludes: false);
        OBox(rail, float4x4.Identity, new float3((x0 + x1) / 2, h + 1.0f, z0 + 0.4f), new float3(x1 - x0, 0.05f, 0.05f));
        for (var x = x0 + 1f; x < x1; x += 2f)
            OBox(rail, float4x4.Identity, new float3(x, h + 0.5f, z0 + 0.4f), new float3(0.05f, 1.0f, 0.05f));
    }

    private void BuildSecondHall(Random random)
    {
        // A lower hall to the south-east: brick plinth, cladding, a window band, a monitor roof. Its roller door has
        // stuck part-way up, and inside it's a dark, empty concrete shed (FactoryShadeVolumes): columns and old crates.
        const float x0 = 34f, x1 = 64f, z0 = 8f, z1 = 44f, h = 8.5f;
        const float doorZ0 = 20f, doorZ1 = 26f, doorTop = 2.8f;
        const float shell = 0.3f;
        var inner = FrameConcrete * 0.7f;
        BrickWall(2, x0, shell, z0, z1, 0, h, inner, Material.Concrete, [(doorZ0, 0f, doorZ1 - doorZ0, doorTop)], lintels: false);
        BrickWall(2, x1 - shell, shell, z0, z1, 0, h, inner, Material.Concrete, []);
        BrickWall(0, z0, shell, x0 + shell, x1 - shell, 0, h, inner, Material.Concrete, []);
        BrickWall(0, z1 - shell, shell, x0 + shell, x1 - shell, 0, h, inner, Material.Concrete, []);
        FBox(new float3(x0 + shell, h - shell, z0 + shell), new float3(x1 - shell, h, z1 - shell), inner, Material.Concrete);
        foreach (var x in (ReadOnlySpan<float>)[44f, 54f])
            foreach (var z in (ReadOnlySpan<float>)[17f, 26f, 35f])
                FBox(new float3(x - 0.35f, 0, z - 0.35f), new float3(x + 0.35f, h - shell, z + 0.35f), FrameConcrete * 0.75f, Material.Concrete);
        AddCrate(new float3(40f, 0, 16f), 1.5f, WoodTint);
        AddCrate(new float3(41.4f, 0, 16.4f), 1.1f, WoodTint * 0.85f, 1);
        AddCrate(new float3(50f, 0, 21f), 1.4f, WoodTint * 0.9f, 2);
        AddCrate(new float3(60f, 0, 27f), 1.6f, WoodTint, 3);
        AddCrate(new float3(48f, 0, 38f), 1.3f, WoodTint * 0.9f, 1);
        AddCrate(new float3(39f, 0, 33f), 1.2f, WoodTint * 0.85f);
        void Skin(int axis, float face, float outward, float a0, float a1)
        {
            float3 P(float a, float y, float d) => axis == 0 ? new float3(a, y, face + outward * d) : new float3(face + outward * d, y, a);
            var hasDoor = axis == 2 && outward < 0;   // the west end: the plinth has a gap for the roller door
            if (hasDoor)
            {
                FBox(P(a0, 0, 0), P(doorZ0, 3.0f, 0.3f), BrickRed * 0.95f, Material.Brick);
                FBox(P(doorZ1, 0, 0), P(a1, 3.0f, 0.3f), BrickRed * 0.95f, Material.Brick);
                FBox(P(doorZ0, doorTop, 0), P(doorZ1, 3.0f, 0.3f), BrickRed * 0.95f, Material.Brick);
            }
            else
            {
                FBox(P(a0, 0, 0), P(a1, 3.0f, 0.3f), BrickRed * 0.95f, Material.Brick);
            }
            FBox(P(a0, 3.0f, 0), P(a1, 3.4f, 0.55f), FrameConcrete, Material.Concrete);
            FBox(P(a0, 3.4f, 0), P(a1, 5.0f, 0.12f), Ochre * 0.9f, Material.Corrugated);
            FBox(P(a0, 5.0f, 0), P(a1, 6.4f, 0.02f), Void, Material.Plain, collides: false);
            RibbonWindow(axis, face, outward, a0 + 0.2f, a1 - 0.2f, 5.0f, 6.4f, 0.1f, random);
            FBox(P(a0, 6.4f, 0), P(a1, h, 0.12f), RustOchre, Material.Corrugated);
            FBox(P(a0, h - 0.4f, 0), P(a1, h, 0.4f), FrameConcrete * 0.9f, Material.Concrete);
            for (var a = a0 + 4f; a < a1 - 1f; a += 6f)
                FBox(P(a - 0.2f, hasDoor && a > doorZ0 && a < doorZ1 ? doorTop : 0, 0.3f), P(a + 0.2f, h, 0.5f), FrameConcrete, Material.Concrete);
        }
        Skin(2, x0, -1, z0, z1);
        Skin(2, x1, 1, z0, z1);
        Skin(0, z0, -1, x0 - 0.3f, x1 + 0.3f);
        Skin(0, z1, 1, x0 - 0.3f, x1 + 0.3f);
        // The roller door in its west end, stuck part-way up.
        FBox(new float3(x0 - 0.35f, doorTop, doorZ0), new float3(x0 - 0.3f, 4.5f, doorZ1), new float3(0.45f, 0.42f, 0.38f), Material.Corrugated);
        FBox(new float3(x0 - 0.75f, 4.5f, 19.8f), new float3(x0 - 0.3f, 5.0f, 26.2f), OldSteel, Material.RustyMetal);
        FBox(new float3(x0 - 0.3f, h, z0 - 0.3f), new float3(x1 + 0.3f, h + 0.1f, z1 + 0.3f), new float3(0.30f, 0.25f, 0.22f), Material.RustyMetal);
        FBox(new float3(46f, h + 0.1f, z0 + 2f), new float3(52f, h + 1.8f, z1 - 2f), Ochre * 0.8f, Material.Corrugated);
        FBox(new float3(45.6f, h + 1.8f, z0 + 1.6f), new float3(52.4f, h + 1.9f, z1 - 1.6f), new float3(0.28f, 0.24f, 0.21f), Material.RustyMetal);
    }

    private void BuildPolesAndWires()
    {
        // Square concrete poles with a cross-arm and insulators, along the north road and past the hall's east side;
        // three sagging wires between each pair.
        float3[] poles = [new(-70, 0, -64), new(-40, 0, -64), new(-12, 0, -64), new(14, 0, -64), new(44, 0, -64), new(74, 0, -64), new(74, 0, -30), new(74, 0, 4), new(74, 0, 40)];
        var concrete = PB(Material.Concrete, FrameConcrete * 0.95f);
        var steel = PB(Material.RustyMetal, OldSteel);
        var porcelain = PB(Material.Plain, new float3(0.82f, 0.80f, 0.74f), occludes: false);
        const float top = 10.5f;
        for (var i = 0; i < poles.Length; i++)
        {
            var p = poles[i];
            OCyl(concrete, float4x4.Identity, p, p + new float3(0, top, 0), 0.22f, 0.13f, 4);
            AddCollider(p + new float3(0, top / 2, 0), new float3(0.4f, top, 0.4f));
            var along = i + 1 < poles.Length ? float3.Normalize(poles[i + 1] - p) : float3.Normalize(p - poles[i - 1]);
            var across = new float3(-along.Z, 0, along.X);
            OBox(steel, float4x4.CreateRotationY(float.Atan2(across.X, across.Z)) * float4x4.CreateTranslation(p + new float3(0, top - 0.6f, 0)), float3.Zero, new float3(0.1f, 0.1f, 1.9f));
            foreach (var k in (ReadOnlySpan<float>)[-0.8f, 0f, 0.8f])
                OCyl(porcelain, float4x4.Identity, p + across * k + new float3(0, top - 0.55f, 0), p + across * k + new float3(0, top - 0.3f, 0), 0.05f, 0.04f, 8);
            if (i + 1 >= poles.Length)
                continue;
            var q = poles[i + 1];
            var nextAlong = i + 2 < poles.Length ? float3.Normalize(poles[i + 2] - q) : along;
            var nextAcross = new float3(-nextAlong.Z, 0, nextAlong.X);
            foreach (var k in (ReadOnlySpan<float>)[-0.8f, 0f, 0.8f])
            {
                var a = p + across * k + new float3(0, top - 0.3f, 0);
                var b = q + nextAcross * k + new float3(0, top - 0.3f, 0);
                var sag = float3.Distance(a, b) * 0.035f;
                var last = a;
                for (var s = 1; s <= 10; s++)
                {
                    var tt = s / 10f;
                    var pt = a + (b - a) * tt - new float3(0, sag * 4 * tt * (1 - tt), 0);
                    AddTrunk(PB(Material.Plain, new float3(0.05f, 0.05f, 0.05f), occludes: false), last, pt, 0.012f, 0.012f, 4);
                    last = pt;
                }
            }
        }
        // The lone pole by the hall's south-east corner (as in the photo), wires running off to the second hall.
        var lone = new float3(27, 0, 36);
        OCyl(concrete, float4x4.Identity, lone, lone + new float3(0, 9f, 0), 0.24f, 0.15f, 12);
        AddCollider(lone + new float3(0, 4.5f, 0), new float3(0.45f, 9f, 0.45f));
        OBox(steel, float4x4.Identity, lone + new float3(0, 8.4f, 0), new float3(1.6f, 0.1f, 0.1f));
    }

    private void BuildPerimeter()
    {
        // Precast concrete panels between posts, 2.6 m high, round the whole site, with a gate gap on the road north.
        var min = GroundsMin;
        var max = GroundsMax;
        const float h = 2.6f;
        var panel = FrameConcrete * 0.85f;
        void Run(float3 a, float3 b)
        {
            var length = float3.Distance(a, b);
            var dir = (b - a) / length;
            for (var d = 0f; d < length; d += 4f)
            {
                var p = a + dir * d;
                Add(p + new float3(0, (h + 0.3f) / 2, 0), new float3(0.3f, h + 0.3f, 0.3f), panel * 0.95f, Material.Concrete);
                var span = float.Min(4f, length - d);
                var c = p + dir * (span / 2);
                var size = float.Abs(dir.X) > 0.5f ? new float3(span, h, 0.15f) : new float3(0.15f, h, span);
                Add(c + new float3(0, h / 2, 0), size, panel, Material.Concrete);
            }
        }
        Run(new float3(min.X, 0, min.Z), new float3(-7, 0, min.Z));
        Run(new float3(7, 0, min.Z), new float3(max.X, 0, min.Z));
        Run(new float3(max.X, 0, min.Z), new float3(max.X, 0, max.Z));
        Run(new float3(max.X, 0, max.Z), new float3(min.X, 0, max.Z));
        Run(new float3(min.X, 0, max.Z), new float3(min.X, 0, min.Z));
        // The gate: two rusty mesh leaves chained shut, hung from their posts. (They swing in for the van on a factory raid and
        // shut behind it. The invisible boxes keep the player and people's paths out, as the leaves are shut whenever that matters.)
        var leaves = new List<DoorLeaf>();
        foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
        {
            AddCollider(new float3(s * 3.5f, 1.2f, min.Z), new float3(7f, 2.4f, 0.06f), blocksShots: false);
            Add(new float3(s * 7f, 1.5f, min.Z), new float3(0.3f, 3f, 0.3f), OldSteel, Material.RustyMetal);
            var center = new float3(-s * 3.5f, 1.2f, 0);
            var size = new float3(7f, 2.4f, 0.06f);
            var leaf = HingedLeaf(new float3(s * 7f, 0, min.Z), s * float.Pi / 2, center, size);
            leaf.B(Material.RustyMetal, new float3(0.3f, 0.2f, 0.15f)).Box(center, size);
            leaves.Add(leaf);
        }
        _factoryGate = NewDoor("factory gate", 2.6f, [.. leaves]);
    }

    private PrisonDoor? _factoryGate;

    /// <summary>The perimeter wall's height, and where along the west wall the raid climbs it (halfway along a panel).</summary>
    public const float PerimeterHeight = 2.6f, FenceClimbZ = -38f;

    /// <summary>Opens or shuts the factory's gate on the road north.</summary>
    public void SetFactoryGate(bool open, bool instant = false)
    {
        if (_factoryGate is { } gate)
            MoveDoor(gate, open, instant);
    }

    private void BuildVegetation(Random random)
    {
        // Bushes and weeds along the walls and the fence, scattered clumps in the grass, a few trees.
        var bushes = new MeshBuilder();
        var dryBushes = new MeshBuilder();
        var notGrown = new MeshBuilder();   // (those in the van's way: made all the same, so the rest come out as they always did)
        void Bush(float3 at, float size)
        {
            var b = random.NextDouble() < 0.35 ? dryBushes : bushes;
            // None where the delivery van drives in on a raid: a clump reaches about its size from its middle, the van 1.2 m from its line.
            if (DeliveryVan.NearRoad(at.X, at.Z, size + 1.8f))
                b = notGrown;
            // Nor in front of the hall's side door, where its guards stand.
            if (float.Abs(at.X - SideDoorCenter.X) < 2.6f + size / 2 && at.Z > HalfLength && at.Z < HalfLength + 5f)
                b = notGrown;
            var n = 2 + random.Next(3);
            for (var i = 0; i < n; i++)
            {
                var off = new float3(((float)random.NextDouble() - 0.5f) * size, 0, ((float)random.NextDouble() - 0.5f) * size);
                b.Blob(at + off + new float3(0, size * 0.35f, 0), new float3(size * 0.5f, size * 0.42f, size * 0.5f), random, 7, 4);
            }
        }
        const float hw = HalfWidth + WallThickness + 1f, hl = HalfLength + WallThickness + 1f;
        for (var z = -hl + 2; z < hl - 2; z += 2.5f + (float)random.NextDouble() * 4f)
            foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
                if (random.NextDouble() < 0.55)
                    Bush(new float3(s * (hw + 0.4f), 0, z), 0.7f + (float)random.NextDouble() * 1.0f);
        for (var x = -hw + 2; x < hw - 2; x += 2.5f + (float)random.NextDouble() * 4f)
            if (random.NextDouble() < 0.6)
                Bush(new float3(x, 0, hl + 0.4f), 0.7f + (float)random.NextDouble() * 1.0f);
        for (var i = 0; i < 140; i++)
        {
            var p = new float3(GroundsMin.X + 2 + (float)random.NextDouble() * (GroundsMax.X - GroundsMin.X - 4), 0,
                               GroundsMin.Z + 2 + (float)random.NextDouble() * (GroundsMax.Z - GroundsMin.Z - 4));
            if (OnFactoryPlan(p, 2.5f))
                continue;
            Bush(p, 0.5f + (float)random.NextDouble() * 1.6f);
        }
        // Bushes along the inside of the perimeter wall.
        for (var x = GroundsMin.X + 3; x < GroundsMax.X - 3; x += 3f + (float)random.NextDouble() * 5f)
            foreach (var z in (ReadOnlySpan<float>)[GroundsMin.Z + 1.2f, GroundsMax.Z - 1.2f])
                if (float.Abs(x) > 9f && random.NextDouble() < 0.7)
                    Bush(new float3(x, 0, z), 1.0f + (float)random.NextDouble() * 1.4f);
        _props.Add(new Prop(bushes.Build(), float4x4.Identity, new float3(0.20f, 0.34f, 0.12f), Material.Bush));
        _props.Add(new Prop(dryBushes.Build(), float4x4.Identity, new float3(0.36f, 0.36f, 0.16f), Material.Bush));

        // Trees: a trunk and a crown of leafy blobs.
        float3[] trees = [new(-30, 0, -40), new(-27, 0, 28), new(-80, 0, -40), new(30, 0, 60), new(-60, 0, 50), new(70, 0, -90), new(-75, 0, -95), new(40, 0, -80)];
        var crowns = new MeshBuilder();
        foreach (var t in trees)
        {
            var height = 7f + (float)random.NextDouble() * 4f;
            var trunk = new MeshBuilder();
            AddTrunk(trunk, t, t + new float3(0.3f, height * 0.6f, 0.2f), 0.3f, 0.18f, 9);
            AddTrunk(trunk, t + new float3(0.3f, height * 0.45f, 0.2f), t + new float3(1.6f, height * 0.75f, -0.4f), 0.14f, 0.08f, 6);
            AddTrunk(trunk, t + new float3(0.3f, height * 0.5f, 0.2f), t + new float3(-1.2f, height * 0.8f, 0.9f), 0.13f, 0.07f, 6);
            _props.Add(new Prop(trunk.Build(), float4x4.Identity, new float3(0.32f, 0.27f, 0.22f), Material.Bark));
            AddCollider(t + new float3(0, 1.5f, 0), new float3(0.6f, 3f, 0.6f));
            for (var i = 0; i < 9; i++)
            {
                var off = new float3(((float)random.NextDouble() - 0.5f) * 4.5f, height * 0.72f + (float)random.NextDouble() * height * 0.3f, ((float)random.NextDouble() - 0.5f) * 4.5f);
                crowns.Blob(t + off, new float3(1.8f, 1.4f, 1.8f) * (0.7f + 0.5f * (float)random.NextDouble()), random, 9, 5);
            }
        }
        _props.Add(new Prop(crowns.Build(), float4x4.Identity, new float3(0.18f, 0.32f, 0.10f), Material.Bush));
    }

    /// <summary>True if (x, z) is on a building's footprint, an apron by a door, or the road (plants keep off).</summary>
    private static bool OnFactoryPlan(float3 p, float margin)
    {
        bool In(float x0, float z0, float x1, float z1) => p.X > x0 - margin && p.X < x1 + margin && p.Z > z0 - margin && p.Z < z1 + margin;
        return In(-24, -50, 24, 34)          // the hall and its north apron
            || In(12, -58, 40, -40)          // boiler house and chimney
            || In(-66, -22, -32, 12)         // outbuilding and its yard
            || In(28, -2, 66, 50)            // second hall
            || In(-8, -106, 8, -48)          // the road
            || In(-36, -48, 30, -32)         // the north apron
            || In(-27, -69, -15, -63);       // a bare patch in the rough grass north-west of the hall
    }
}
