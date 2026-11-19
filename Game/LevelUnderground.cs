using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Under the outback: the tin shed at the north end of the roadhouse has a hatch in its floor, and a ladder goes down
/// a concrete shaft into a small cellar. The green portal in the cellar's far wall leads to the bunker: a bare concrete
/// room, deep underground, lit only by daylight down its one ceiling vent and the glow of its four portals. They lead
/// back to the cellar, out into an Arctic whiteout, onto the bridge of a container ship and to a jungle lab in the Amazon.
/// </summary>
public sealed partial class Level
{
    // ------------------------------------------------------------------------------------------------
    // The cellar under the shed (outback coordinates, relative to OutbackOrigin).
    // ------------------------------------------------------------------------------------------------

    private static float3 OutbackAt(float x, float y, float z) => OutbackOrigin + new float3(x, y, z);

    /// <summary>The shed over the cellar, the shaft and the cellar itself (drawn on their own from down there).</summary>
    public static Aabb CellarBounds => new(OutbackAt(29.5f, -5f, -131.5f), OutbackAt(36f, 3f, -122.5f));

    /// <summary>Below this the camera is underground (in the shaft or the cellar) and can't see the outback.</summary>
    public static float CellarSurfaceY => OutbackOrigin.Y - 0.8f;

    // The shed: 3.0 x 2.6 m, door in its north wall (the side you see coming up from the motel).
    private static float3 ShedMin => OutbackAt(31.5f, 0, -130.3f);
    private static float3 ShedMax => OutbackAt(34.5f, 2.2f, -127.7f);
    // The hatch (and shaft) in the shed's floor, and the ladder on its east side.
    private static float3 HatchMin => OutbackAt(33.0f, 0, -129.2f);
    private static float3 HatchMax => OutbackAt(34.2f, 0, -128.0f);
    // The cellar: 3.8 x 5.8 m inside, 2.9 m high, its floor 4.2 m down.
    private const float CellarFloorY = -4.2f;
    private const float CellarCeilingY = -1.3f;
    private static float3 CellarMin => OutbackAt(30.4f, CellarFloorY, -129.2f);
    private static float3 CellarMax => OutbackAt(34.2f, CellarCeilingY, -123.4f);

    /// <summary>The green portal: in the cellar's south wall, facing back into it.</summary>
    public static float3 CellarPortalFloorPoint => new((CellarMin.X + CellarMax.X) / 2, CellarMin.Y, CellarMax.Z);
    public static float3 CellarPortalNormal => new(0, 0, -1);

    private static readonly float3 CellarConcreteTint = new(0.50f, 0.49f, 0.46f);
    private static readonly float3 CellarFloorTint = new(0.38f, 0.36f, 0.33f);
    private static readonly float3 LadderTint = new(0.30f, 0.30f, 0.30f);

    /// <summary>The dim insides of the shed and the cellar (the sky's light fades away from the door and the hatch).</summary>
    public static ShadeVolume[] ShadeVolumes =>
    [
        new(new Aabb(ShedMin + new float3(0, -0.05f, 0), ShedMax + new float3(0, 0.3f, 0)),
            new float3(32.35f, 1.0f, -130.3f) + OutbackOrigin, 2.6f, 0.32f),
        new(new Aabb(OutbackAt(30.0f, -4.6f, -129.6f), OutbackAt(34.6f, -0.02f, -123.0f)),
            (HatchMin + HatchMax) / 2, 1.1f, 0.0f),
        .. FactoryShadeVolumes,
        .. MotelShadeVolumes,
        YardPcShade,
    ];

    private void BuildCellar()
    {
        float3 At(float x, float y, float z) => OutbackAt(x, y, z);
        const float wall = 0.05f;

        // --- The shed: thin galvanised walls, a lean-to roof, its door standing open ---
        var tin = TinTint;
        AddMinMax(ShedMin, new float3(ShedMin.X + wall, ShedMax.Y, ShedMax.Z), tin, Material.Galvanized);                       // west
        AddMinMax(new float3(ShedMax.X - wall, 0, ShedMin.Z), ShedMax, tin, Material.Galvanized);                                // east
        AddMinMax(new float3(ShedMin.X, 0, ShedMax.Z - wall), ShedMax, tin, Material.Galvanized);                                // south
        const float doorMinX = 31.9f, doorMaxX = 32.8f, doorTop = 1.95f;
        AddMinMax(ShedMin, At(doorMinX, ShedMax.Y, ShedMin.Z + wall - OutbackOrigin.Z), tin, Material.Galvanized);               // north, either side
        AddMinMax(At(doorMaxX, 0, ShedMin.Z - OutbackOrigin.Z), new float3(ShedMax.X, ShedMax.Y, ShedMin.Z + wall), tin, Material.Galvanized);
        AddMinMax(At(doorMinX, doorTop, ShedMin.Z - OutbackOrigin.Z), new float3(At(doorMaxX, 0, 0).X, ShedMax.Y, ShedMin.Z + wall), tin, Material.Galvanized);
        // Timber door frame, and the door swung wide open against the outside of the wall.
        AddMinMax(At(doorMinX - 0.06f, 0, -130.36f), At(doorMinX, doorTop + 0.05f, -130.22f), WeatheredTimber * 0.8f, Material.Wood, collides: false);
        AddMinMax(At(doorMaxX, 0, -130.36f), At(doorMaxX + 0.06f, doorTop + 0.05f, -130.22f), WeatheredTimber * 0.8f, Material.Wood, collides: false);
        AddMinMax(At(doorMinX - 0.06f, doorTop, -130.36f), At(doorMaxX + 0.06f, doorTop + 0.06f, -130.22f), WeatheredTimber * 0.8f, Material.Wood, collides: false);
        // (The leaf hangs on the door's west post and has swung out past square.)
        var leaf = float4x4.CreateScale(0.86f, doorTop - 0.05f, 0.04f) * float4x4.CreateTranslation(0.45f, doorTop / 2, -0.03f)
            * float4x4.CreateRotationY(float.DegreesToRadians(115f)) * float4x4.CreateTranslation(At(doorMinX, 0, -130.32f));
        _props.Add(new Prop(UnitBox, leaf, WeatheredTimber * 0.75f, Material.Wood));
        var roof = float4x4.CreateScale(3.5f, 0.05f, 3.1f) * float4x4.CreateRotationZ(0.12f) * float4x4.CreateTranslation(At(33, 2.3f, -129));
        _props.Add(new Prop(UnitBox, roof, tin * 0.85f, Material.Galvanized));
        // A translucent fibreglass sheet in the roof lets a little daylight in over the hatch.
        var skylight = float4x4.CreateScale(1.1f, 0.02f, 0.85f) * float4x4.CreateRotationZ(0.12f) * float4x4.CreateTranslation(At(33.5f, 2.38f, -128.7f));
        _props.Add(new Prop(UnitBox, skylight, TimeOfDay.Daylight(DayNightLevel.Outback, new float3(0.95f, 0.92f, 0.78f)), Material.Emissive));

        // A concrete slab floor (a decal on the ground) with a timber kerb round the hatch.
        void Slab(float3 min, float3 max)
        {
            var model = float4x4.CreateScale(max.X - min.X, 0.02f, max.Z - min.Z) * float4x4.CreateTranslation(new float3((min.X + max.X) / 2, 0.005f, (min.Z + max.Z) / 2));
            _props.Add(new Prop(UnitBox, model, CellarConcreteTint * 0.9f, Material.Floor, Decal: true));
        }
        var inMin = ShedMin + new float3(wall, 0, wall);
        var inMax = ShedMax - new float3(wall, 0, wall);
        Slab(inMin, new float3(inMax.X, 0, HatchMin.Z));
        Slab(new float3(inMin.X, 0, HatchMax.Z), inMax);
        Slab(new float3(inMin.X, 0, HatchMin.Z), new float3(HatchMin.X, 0, HatchMax.Z));
        Slab(new float3(HatchMax.X, 0, HatchMin.Z), new float3(inMax.X, 0, HatchMax.Z));
        // The hatch's steel lid, hinged up and leaning back against the south wall.
        var lid = float4x4.CreateScale(1.2f, 1.2f, 0.04f) * float4x4.CreateRotationX(0.2f)
            * float4x4.CreateTranslation(new float3((HatchMin.X + HatchMax.X) / 2, 0.62f, HatchMax.Z + 0.12f));
        _props.Add(new Prop(UnitBox, lid, new float3(0.34f, 0.33f, 0.31f), Material.Steel));
        // Odds and ends: a drum and a crate in the corners.
        AddDrum(At(31.95f, 0, -128.2f), new float3(0.22f, 0.30f, 0.18f), 0.4f, Material.RustyMetal);
        AddCrate(At(33.95f, 0, -129.85f), 0.6f, WoodTint * 0.8f, 1);

        // --- The cellar, then the shaft: cut through the ground and the cellar's ceiling, lined with concrete ---
        BuildCellarRoom();
        const float liner = 0.2f;
        CarveOpening(new Aabb(new float3(HatchMin.X - liner, CellarCeilingY - 0.02f, HatchMin.Z - liner), new float3(HatchMax.X + liner, 0.02f, HatchMax.Z + liner)));
        BuildShaftAndCellarContents();

        // A timber kerb round the hatch (on three sides: the ladder's stiles come up through the fourth).
        var kerb = WeatheredTimber * 0.7f;
        AddMinMax(new float3(HatchMin.X - 0.1f, 0, HatchMin.Z - 0.1f), new float3(HatchMax.X + 0.1f, 0.04f, HatchMin.Z), kerb, Material.Wood, collides: false);
        AddMinMax(new float3(HatchMin.X - 0.1f, 0, HatchMax.Z), new float3(HatchMax.X + 0.1f, 0.04f, HatchMax.Z + 0.1f), kerb, Material.Wood, collides: false);
        AddMinMax(new float3(HatchMin.X - 0.1f, 0, HatchMin.Z), new float3(HatchMin.X, 0.04f, HatchMax.Z), kerb, Material.Wood, collides: false);
    }

    /// <summary>The cellar's floor, ceiling and walls (0.3 m concrete).</summary>
    private void BuildCellarRoom()
    {
        const float t = 0.3f;
        var min = CellarMin;
        var max = CellarMax;
        AddMinMax(new float3(min.X - t, min.Y - t, min.Z - t), new float3(max.X + t, min.Y, max.Z + t), CellarFloorTint, Material.Floor);           // floor
        AddMinMax(new float3(min.X - t, max.Y, min.Z - t), new float3(max.X + t, max.Y + t, max.Z + t), CellarConcreteTint * 0.9f, Material.Concrete); // ceiling
        AddMinMax(new float3(min.X - t, min.Y, min.Z - t), new float3(min.X, max.Y, max.Z + t), CellarConcreteTint, Material.Concrete);              // west
        AddMinMax(new float3(max.X, min.Y, min.Z - t), new float3(max.X + t, max.Y, max.Z + t), CellarConcreteTint, Material.Concrete);              // east
        AddMinMax(new float3(min.X, min.Y, min.Z - t), new float3(max.X, max.Y, min.Z), CellarConcreteTint, Material.Concrete);                      // north
        AddMinMax(new float3(min.X, min.Y, max.Z), new float3(max.X, max.Y, max.Z + t), CellarConcreteTint, Material.Concrete);                      // south (portal)
    }

    private void BuildShaftAndCellarContents()
    {
        const float liner = 0.2f;
        var top = OutbackOrigin.Y;
        var bottom = CellarCeilingY;
        var concrete = CellarConcreteTint * 0.95f;
        AddMinMax(new float3(HatchMin.X - liner, bottom, HatchMin.Z - liner), new float3(HatchMin.X, top, HatchMax.Z + liner), concrete, Material.Concrete);
        AddMinMax(new float3(HatchMax.X, bottom, HatchMin.Z - liner), new float3(HatchMax.X + liner, top, HatchMax.Z + liner), concrete, Material.Concrete);
        AddMinMax(new float3(HatchMin.X, bottom, HatchMin.Z - liner), new float3(HatchMax.X, top, HatchMin.Z), concrete, Material.Concrete);
        AddMinMax(new float3(HatchMin.X, bottom, HatchMax.Z), new float3(HatchMax.X, top, HatchMax.Z + liner), concrete, Material.Concrete);

        // The ladder: on the shaft's east wall, which carries on down as the cellar's east wall.
        AddLadder(new float3(HatchMax.X, CellarFloorY, (HatchMin.Z + HatchMax.Z) / 2), new float3(1, 0, 0), top, LadderTint, rails: 1.0f,
                  exit: new float3(0, 0, -1)); // step off towards the door

        // A bare bulb on its flex in the middle of the ceiling.
        var bulb = new float3((CellarMin.X + CellarMax.X) / 2, CellarCeilingY, (CellarMin.Z + CellarMax.Z) / 2 + 0.6f);
        Add(bulb + new float3(0, -0.2f, 0), new float3(0.012f, 0.4f, 0.012f), new float3(0.05f, 0.05f, 0.05f), Material.Plain, collides: false);
        Add(bulb + new float3(0, -0.43f, 0), new float3(0.05f, 0.07f, 0.05f), new float3(0.2f, 0.2f, 0.2f), Material.Steel, collides: false);
        Add(bulb + new float3(0, -0.5f, 0), new float3(0.07f, 0.09f, 0.07f), new float3(1.0f, 0.86f, 0.62f), Material.Emissive, collides: false);

        // A steel shelf along the west wall with a few things on it, a couple of crates and a drum.
        var shelfX = CellarMin.X + 0.25f;
        foreach (var z in (ReadOnlySpan<float>)[-128.6f, -126.2f])
            Add(new float3(shelfX, CellarFloorY + 0.9f, OutbackOrigin.Z + z), new float3(0.04f, 1.8f, 0.04f), LadderTint, Material.Steel);
        foreach (var y in (ReadOnlySpan<float>)[0.45f, 1.05f, 1.65f])
            Add(new float3(shelfX, CellarFloorY + y, OutbackOrigin.Z - 127.4f), new float3(0.45f, 0.03f, 2.5f), LadderTint * 1.2f, Material.Steel, collides: false);
        AddStatic(new Aabb(new float3(CellarMin.X, CellarFloorY, OutbackOrigin.Z - 128.7f), new float3(CellarMin.X + 0.5f, CellarFloorY + 1.8f, OutbackOrigin.Z - 126.1f)), default, visible: false);
        AddCrate(new float3(shelfX, CellarFloorY + 0.47f, OutbackOrigin.Z - 128.2f), 0.4f, WoodTint * 0.85f);
        AddCrate(new float3(shelfX, CellarFloorY + 1.07f, OutbackOrigin.Z - 126.8f), 0.35f, WoodTint * 0.7f, 1);
        AddDrum(new float3(CellarMin.X + 0.5f, CellarFloorY, CellarMax.Z - 0.55f), new float3(0.16f, 0.26f, 0.46f), 1.1f, Material.RustyMetal);
        AddCrate(new float3(CellarMax.X - 0.55f, CellarFloorY, CellarMax.Z - 0.5f), 0.8f, WoodTint * 0.75f);
    }

    /// <summary>Daylight through the shed's fibreglass roof sheet.</summary>
    private static LocalLight ShedSkylight => new(
        OutbackAt(33.5f, 2.0f, -128.7f), TimeOfDay.Daylight(DayNightLevel.Outback, new float3(1.0f, 0.95f, 0.85f)), 2.2f, 4.5f,
        new Aabb(OutbackAt(31.5f, -1.4f, -130.3f), OutbackAt(34.5f, 2.3f, -127.7f)));

    /// <summary>The cellar's bare bulb (it only lights the cellar and the shaft).</summary>
    private static LocalLight CellarBulb => new(
        new float3((CellarMin.X + CellarMax.X) / 2, CellarCeilingY - 0.6f, (CellarMin.Z + CellarMax.Z) / 2 + 0.6f),
        new float3(1.0f, 0.70f, 0.40f), 3.2f, 8f,
        new Aabb(OutbackAt(30.0f, -4.6f, -129.6f), OutbackAt(34.6f, -0.05f, -123.0f)));

    // ------------------------------------------------------------------------------------------------
    // The bunker.
    // ------------------------------------------------------------------------------------------------

    /// <summary>Where the bunker is (the shader's area 3: lit only by its own lights).</summary>
    public static readonly float3 BunkerOrigin = new(5000, 0, 0);
    private const float BunkerHalf = 6f;       // 12 x 12 m inside
    private const float BunkerHeight = 3.6f;
    private const float BunkerWall = 1f;

    /// <summary>The bunker's four portals: west (back to the cellar), north (the Arctic), east (the ship), south (the jungle).</summary>
    public static float3 BunkerWestPortalFloorPoint => BunkerOrigin + new float3(-BunkerHalf, 0, 0);
    public static float3 BunkerNorthPortalFloorPoint => BunkerOrigin + new float3(0, 0, -BunkerHalf);
    public static float3 BunkerEastPortalFloorPoint => BunkerOrigin + new float3(BunkerHalf, 0, 0);
    public static float3 BunkerSouthPortalFloorPoint => BunkerOrigin + new float3(0, 0, BunkerHalf);

    /// <summary>Where the security camera's dome meets the ceiling (0.36 m out of the north-east corner along the diagonal).</summary>
    public static float3 BunkerCameraMount => BunkerOrigin + new float3(BunkerHalf - 0.255f, BunkerHeight, -BunkerHalf + 0.255f);

    /// <summary>The dome's turn: its conduit (the model's +x) runs into the north-east corner.</summary>
    private const float BunkerCameraYaw = float.Pi / 4;

    private static readonly float3 BunkerWallTint = new(0.56f, 0.56f, 0.55f);
    private static readonly float3 BunkerFloorTint = new(0.44f, 0.44f, 0.43f);
    private static readonly float3 VentCenter = BunkerOrigin + new float3(0, BunkerHeight, 0);
    private const float VentHalf = 0.5f;

    private void BuildBunker()
    {
        var o = BunkerOrigin;
        const float h = BunkerHalf, w = BunkerWall, H = BunkerHeight;
        AddMinMax(o + new float3(-h - w, -0.5f, -h - w), o + new float3(h + w, 0, h + w), BunkerFloorTint, Material.Floor);
        AddMinMax(o + new float3(-h - w, H, -h - w), o + new float3(h + w, H + w, h + w), BunkerWallTint * 0.92f, Material.Concrete);
        AddMinMax(o + new float3(-h - w, 0, -h - w), o + new float3(-h, H, h + w), BunkerWallTint, Material.Concrete);
        AddMinMax(o + new float3(h, 0, -h - w), o + new float3(h + w, H, h + w), BunkerWallTint, Material.Concrete);
        AddMinMax(o + new float3(-h, 0, -h - w), o + new float3(h, H, -h), BunkerWallTint, Material.Concrete);
        AddMinMax(o + new float3(-h, 0, h), o + new float3(h, H, h + w), BunkerWallTint, Material.Concrete);

        // The one vent: a square hole in the ceiling with a steel louvre grille, a short duct above it and daylight
        // at its top.
        var v = VentCenter;
        CarveOpening(new Aabb(v + new float3(-VentHalf, -0.01f, -VentHalf), v + new float3(VentHalf, w + 0.01f, VentHalf)));
        const float duct = 1.6f;
        var steel = new float3(0.30f, 0.30f, 0.29f);
        AddMinMax(v + new float3(-VentHalf - 0.05f, 0, -VentHalf - 0.05f), v + new float3(-VentHalf, w + duct, VentHalf + 0.05f), steel, Material.Steel, collides: false);
        AddMinMax(v + new float3(VentHalf, 0, -VentHalf - 0.05f), v + new float3(VentHalf + 0.05f, w + duct, VentHalf + 0.05f), steel, Material.Steel, collides: false);
        AddMinMax(v + new float3(-VentHalf, 0, -VentHalf - 0.05f), v + new float3(VentHalf, w + duct, -VentHalf), steel, Material.Steel, collides: false);
        AddMinMax(v + new float3(-VentHalf, 0, VentHalf), v + new float3(VentHalf, w + duct, VentHalf + 0.05f), steel, Material.Steel, collides: false);
        AddMinMax(v + new float3(-VentHalf, w + duct, -VentHalf), v + new float3(VentHalf, w + duct + 0.05f, VentHalf), TimeOfDay.Daylight(DayNightLevel.Bunker, new float3(0.80f, 0.86f, 0.92f)), Material.Emissive, collides: false);
        // Grille: a frame flush with the ceiling and angled-looking louvres (thin slats, alternately high and low).
        AddMinMax(v + new float3(-VentHalf - 0.08f, -0.04f, -VentHalf - 0.08f), v + new float3(VentHalf + 0.08f, 0, -VentHalf), steel, Material.Steel, collides: false);
        AddMinMax(v + new float3(-VentHalf - 0.08f, -0.04f, VentHalf), v + new float3(VentHalf + 0.08f, 0, VentHalf + 0.08f), steel, Material.Steel, collides: false);
        AddMinMax(v + new float3(-VentHalf - 0.08f, -0.04f, -VentHalf), v + new float3(-VentHalf, 0, VentHalf), steel, Material.Steel, collides: false);
        AddMinMax(v + new float3(VentHalf, -0.04f, -VentHalf), v + new float3(VentHalf + 0.08f, 0, VentHalf), steel, Material.Steel, collides: false);
        for (var i = 0; i < 7; i++)
        {
            var z = -VentHalf + (i + 0.5f) * (2 * VentHalf / 7);
            AddMinMax(v + new float3(-VentHalf, 0.02f + (i % 2) * 0.03f, z - 0.035f), v + new float3(VentHalf, 0.10f + (i % 2) * 0.03f, z + 0.005f), steel * 0.9f, Material.Steel, collides: false);
        }
        // A dome security camera on the ceiling in the north-east corner, its conduit running into the corner (its ball,
        // which turns to follow the player, is Game/SecurityCamera.cs).
        if (TryModel("cctv_dome") is { } dome)
            AddModel(dome, float4x4.CreateRotationY(BunkerCameraYaw) * float4x4.CreateTranslation(BunkerCameraMount), float3.One);

        // A fan in the duct, its blades just visible through the louvres.
        AddMinMax(v + new float3(-0.04f, 0.5f, -0.04f), v + new float3(0.04f, 0.62f, 0.04f), steel * 0.6f, Material.Steel, collides: false);
        for (var i = 0; i < 4; i++)
        {
            var a = i * float.Pi / 2 + 0.3f;
            var tip = new float3(float.Cos(a), 0, float.Sin(a)) * 0.22f;
            Add(v + new float3(0, 0.56f, 0) + tip, new float3(float.Abs(float.Cos(a)) * 0.36f + 0.06f, 0.015f, float.Abs(float.Sin(a)) * 0.36f + 0.06f), steel * 0.7f, Material.Steel, collides: false);
        }
    }

    /// <summary>The bunker's lights: daylight down the vent, and each portal's glow.</summary>
    private static Aabb BunkerInterior => new(BunkerOrigin + new float3(-BunkerHalf - 0.2f, -0.1f, -BunkerHalf - 0.2f), BunkerOrigin + new float3(BunkerHalf + 0.2f, BunkerHeight + 2.8f, BunkerHalf + 0.2f));

    public static LocalLight[] LocalLights(IEnumerable<Portal> bunkerPortals)
    {
        var lights = new List<LocalLight>
        {
            CellarBulb,
            ShedSkylight,
            BodegaBulb,
            new(VentCenter + new float3(0, -0.35f, 0), TimeOfDay.Daylight(DayNightLevel.Bunker, new float3(0.80f, 0.88f, 1.0f)), 12f, 20f, BunkerInterior),
        };
        foreach (var portal in bunkerPortals)
            lights.Add(new LocalLight(portal.Center + portal.Normal * 0.7f, portal.Color, 0.45f, 4.5f, BunkerInterior));
        lights.AddRange(NightLights);
        lights.Add(FlatsPortalGlow);   // (last: the shader has room for 12, and on the worst night the others matter more)
        return [.. lights];
    }

    /// <summary>The lamps that only matter once it's dark: the roadhouse's light pole, the camp's bare bulb, the ship's working lights.</summary>
    private static IEnumerable<LocalLight> NightLights
    {
        get
        {
            if (TimeOfDay.IsNight(DayNightLevel.Outback))
            {
                var pole = OutbackAt(9, 6.2f, -86);
                yield return new(pole, new float3(1.0f, 0.82f, 0.58f), 70f, 34f, new Aabb(pole - new float3(34, 8, 34), pole + new float3(34, 8, 34)));
            }
            if (TimeOfDay.IsNight(DayNightLevel.Jungle))
            {
                var bulb = new float3(JungleOrigin.X + 0.5f, LabMax.Y - 0.85f, (LabMin.Z + LabMax.Z) / 2);
                yield return new(bulb, new float3(1.0f, 0.80f, 0.52f), 16f, 20f, new Aabb(bulb - new float3(20, 6, 20), bulb + new float3(20, 6, 20)));
            }
            if (TimeOfDay.IsNight(DayNightLevel.Ship))
            {
                foreach (var at in new float3[] { new(-40f, 26f, 0), new(25f, 26f, 0) })   // (well above the stacks, and clear of the bridge)
                {
                    var lamp = ShipOrigin + at;
                    yield return new(lamp, new float3(1.0f, 0.9f, 0.75f), 110f, 60f, new Aabb(lamp - new float3(60, 40, 40), lamp + new float3(60, 40, 40)));
                }
            }
        }
    }
}
