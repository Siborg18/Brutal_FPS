using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The far bank of the dock, across the water from the Antwerp yard's quay (about 250 m west of it): a container terminal
/// with two ships moored along its quay under six ship-to-shore cranes, stacks and straddle gantries behind them, and
/// flood masts. All for looking at: nothing over there is solid, and nobody goes there. The ships are the container
/// ship's own models (ship_hull, ship_house) with a load of boxes (container_lod); the cranes are built here from boxes.
/// What moves (the cranes' trolleys, spreaders and the boxes they carry, the tug, the cranes' beacons) is in
/// <see cref="Harbour"/>. At night the lamps, the lit quay under the cranes and the ships' windows glow.
/// </summary>
public sealed partial class Level
{
    // All from the yard's origin. The far quay's face runs north-south; the land behind it goes back to x = -400 (world
    // x 120, still in the yard's area: see YardOrigin).
    public const float FarQuayX = -280f;
    private const float FarLandX = -400f, FarLandZ = 330f;

    /// <summary>The ships' centrelines, and their deck height (11 m above the water, as on the container ship).</summary>
    public const float FarShipX = FarQuayX + 17.5f;
    public const float FarShipDeckY = YardSeaLevel + 11f;

    /// <summary>The two ships: the middle of each (z), and which way its bow points (+1: north, +z).</summary>
    public static readonly (float Z, float Bow)[] FarShips = [(-105f, -1f), (100f, 1f)];

    // The ship-to-shore cranes: legs 30 m apart across the quay and 18 m apart along it; a boom 47 m up reaching out over
    // the ships; an A-frame above the waterside legs.
    public const float StsWaterLegX = FarQuayX - 4f, StsLandLegX = StsWaterLegX - 30f, StsLegHalfZ = 9f;
    public const float StsBoomY = 46f, StsBoomTipX = -215f, StsBoomBackX = -342f, StsGirderHalfZ = 3.5f;
    public static readonly float3 StsApex = new(StsWaterLegX - 8f, 80f, 0);

    /// <summary>The cranes along the quay (z), three to a ship.</summary>
    public static readonly float[] StsCraneZ = [-165f, -118f, -66f, 38f, 88f, 140f];

    /// <summary>Where the cranes set the boxes down on the quay: the lane between their legs (x).</summary>
    public const float StsQuayLaneX = StsWaterLegX - 8f;

    public static readonly float3 StsPaint = new(0.40f, 0.50f, 0.55f);
    public static readonly float3[] FarContainerColors =
    [
        new(0.14f, 0.30f, 0.56f), new(0.11f, 0.22f, 0.42f), new(0.62f, 0.16f, 0.12f), new(0.46f, 0.12f, 0.12f),
        new(0.82f, 0.82f, 0.79f), new(0.46f, 0.48f, 0.50f), new(0.18f, 0.42f, 0.28f), new(0.80f, 0.42f, 0.12f),
        new(0.36f, 0.56f, 0.70f), new(0.78f, 0.64f, 0.14f),
    ];

    /// <summary>The ship lying at <paramref name="index"/>: ship space (x forward, z to port, deck at y = 0) to world.</summary>
    public static float4x4 FarShipTransform(int index)
    {
        var (z, bow) = FarShips[index];
        // (Turned a quarter: the ship's +x, its bow, along the quay. Turning by +pi/2 takes +x to -z.)
        var yaw = bow < 0 ? float.Pi / 2 : -float.Pi / 2;
        return float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(YardOrigin + new float3(FarShipX, FarShipDeckY, z));
    }

    /// <summary>The top of the boxes the cranes are taking off the ships (y, from the yard's origin): four high on the hatches.</summary>
    public const float FarShipLoadTop = FarShipDeckY + 1.5f + 4 * ContainerHeight;

    private void BuildFarBank()
    {
        var o = YardOrigin;
        var night = TimeOfDay.IsNight(DayNightLevel.Yard);
        var sodium = new float3(1.0f, 0.62f, 0.25f);

        // The land and its quay wall, down to the water.
        Add(o + new float3((FarLandX + FarQuayX) / 2, YardSeaLevel / 2 - 0.5f, 0), new float3(FarQuayX - FarLandX, 1 - YardSeaLevel, 2 * FarLandZ),
            new float3(0.46f, 0.45f, 0.42f), Material.Floor, collides: false);
        Add(o + new float3(FarQuayX + 0.3f, YardSeaLevel / 2 - 0.4f, 0), new float3(0.6f, 0.8f - YardSeaLevel, 2 * FarLandZ),
            new float3(0.36f, 0.35f, 0.33f), Material.Concrete, collides: false);

        var models = new Dictionary<int, List<float4x4>>();
        var random = new Random(4711);
        void Box(float4x4 transform)
        {
            var c = random.Next(FarContainerColors.Length);
            if (!models.TryGetValue(c, out var list))
                models[c] = list = [];
            list.Add(transform);
        }

        // --- The ships: hull and house, hatch covers, and the boxes on them (fewer where the cranes are at work) ---
        var hull = TryModel("ship_hull");
        var house = TryModel("ship_house");
        var hatchGreen = JB(Material.PaintedMetal, new float3(0.27f, 0.33f, 0.28f));
        var windows = JB(night ? Material.Emissive : Material.Glass, night ? new float3(0.85f, 0.7f, 0.45f) : new float3(0.08f, 0.09f, 0.1f));
        var deckLamps = JB(Material.Emissive, night ? new float3(1.0f, 0.85f, 0.6f) : new float3(0.35f, 0.35f, 0.33f));
        for (var s = 0; s < FarShips.Length; s++)
        {
            var ship = FarShipTransform(s);
            if (hull is not null)
                AddModel(hull, ship, new float3(0.84f, 0.84f, 0.82f));
            if (house is not null)
                AddModel(house, ship, new float3(0.84f, 0.84f, 0.82f));

            // Eleven bays from just forward of the house to the bow, as many rows as fit the deck there.
            for (var b = 0; b < 11; b++)
            {
                var x0 = -62f + b * 13.2f;
                var x1 = x0 + 12.6f;
                var mid = (x0 + x1) / 2;
                var half = float.Min(ShipHalfBreadth(x0), ShipHalfBreadth(x1)) - 1.9f;
                var rowsHalf = (int)((half - 1.22f) / 2.5f);
                if (rowsHalf < 1)
                    continue;
                OBox(hatchGreen, ship, new float3(mid, 0.75f, 0), new float3(12.6f, 1.5f, rowsHalf * 5f + 2.9f));
                // Lower towards the bow, so the bridge can see; one bay in three nearly cleared (the cranes are on it).
                var cap = b >= 9 ? 3 : 5;
                var worked = b % 3 == 1;
                for (var r = -rowsHalf; r <= rowsHalf; r++)
                {
                    var tiers = worked ? (r > 0 ? 1 : 3) : cap - random.Next(2);
                    for (var t = 0; t < tiers; t++)
                        Box(float4x4.CreateRotationY(random.Next(2) == 0 ? 0 : float.Pi) * float4x4.CreateTranslation(mid, 1.5f + t * ContainerHeight, r * 2.5f) * ship);
                }
            }

            // The house's windows, a row on each deck, on its front and both sides; deck lamps along the rails.
            for (var deck = 0; deck < 5; deck++)
            {
                var y = 4.2f + deck * 2.9f;
                OBox(windows, ship, new float3(-65.95f, y, 0), new float3(0.06f, 0.9f, 19f));
                foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
                    OBox(windows, ship, new float3(-72f, y, side * 11.03f), new float3(9f, 0.9f, 0.06f));
            }
            for (var x = -55f; x < 80f; x += 18f)
                foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
                    OBox(deckLamps, ship, new float3(x, 1.2f, side * (ShipHalfBreadth(x) - 0.4f)), new float3(0.5f, 0.35f, 0.35f));
        }

        // --- The ship-to-shore cranes ---
        var paint = JB(Material.PaintedMetal, StsPaint);
        var housePaint = JB(Material.PaintedMetal, new float3(0.78f, 0.79f, 0.76f));
        var dark = JB(Material.Steel, new float3(0.12f, 0.12f, 0.13f));
        var lamps = JB(Material.Emissive, night ? sodium : new float3(0.30f, 0.30f, 0.28f));
        var pool = JB(Material.Emissive, new float3(0.11f, 0.06f, 0.022f));
        var poolCore = JB(Material.Emissive, new float3(0.2f, 0.11f, 0.04f));
        var id = float4x4.Identity;
        foreach (var cz in StsCraneZ)
        {
            float3 P(float x, float y, float z) => o + new float3(x, y, cz + z);
            foreach (var lx in (ReadOnlySpan<float>)[StsWaterLegX, StsLandLegX])
                foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
                {
                    OBox(paint, id, P(lx, 23.5f, side * StsLegHalfZ), new float3(1.8f, 43f, 1.8f));   // legs
                    OBox(dark, id, P(lx, 1f, side * StsLegHalfZ), new float3(3f, 2f, 2.4f));           // bogies
                }
            foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            {
                OBox(paint, id, P((StsWaterLegX + StsLandLegX) / 2, 3f, side * StsLegHalfZ), new float3(30f, 1.4f, 1.6f));   // sills
                OBox(paint, id, P((StsWaterLegX + StsLandLegX) / 2, 24f, side * StsLegHalfZ), new float3(30f, 1.2f, 1.2f));  // ties
                // The boom's two girders, from the back reach out over the water.
                OBox(paint, id, P((StsBoomTipX + StsBoomBackX) / 2, StsBoomY + 1.25f, side * StsGirderHalfZ), new float3(StsBoomTipX - StsBoomBackX, 2.5f, 1.2f));
                // The A-frame over the waterside legs, and its stays out to the boom.
                var apex = P(StsApex.X, StsApex.Y, side * 3f);
                OCyl(paint, id, P(StsWaterLegX, StsBoomY + 2.5f, side * StsLegHalfZ), apex, 0.7f, 0.6f, 4);
                OCyl(paint, id, P(StsLandLegX + 6f, StsBoomY + 2.5f, side * StsLegHalfZ), apex, 0.7f, 0.6f, 4);
                OCyl(paint, id, apex, P(StsBoomTipX - 8f, StsBoomY + 2.5f, side * StsGirderHalfZ), 0.25f, 0.25f, 4);   // (the tip is the boom's east end)
                OCyl(paint, id, apex, P(-245f, StsBoomY + 2.5f, side * StsGirderHalfZ), 0.25f, 0.25f, 4);
                OCyl(paint, id, apex, P(StsBoomBackX + 4f, StsBoomY + 2.5f, side * StsGirderHalfZ), 0.3f, 0.3f, 4);
            }
            // Portal beams across the tops of the legs; cross ties along the boom; the apex beam.
            foreach (var lx in (ReadOnlySpan<float>)[StsWaterLegX, StsLandLegX])
                OBox(paint, id, P(lx, StsBoomY - 0.2f, 0), new float3(2f, 2.4f, 2 * StsLegHalfZ + 1.8f));
            for (var x = StsBoomBackX + 4f; x < StsBoomTipX; x += 12f)
                OBox(paint, id, P(x, StsBoomY + 2.3f, 0), new float3(0.6f, 0.5f, 2 * StsGirderHalfZ));
            OBox(paint, id, P(StsApex.X, StsApex.Y, 0), new float3(1.6f, 1.6f, 7.4f));
            // The machinery house on the back reach.
            OBox(housePaint, id, P(StsBoomBackX + 12f, StsBoomY + 5.5f, 0), new float3(15f, 6f, 9f));
            // Floodlights under the boom and on the portal; at night a pool of light on the quay under the crane.
            foreach (var x in (ReadOnlySpan<float>)[-226f, -246f, -266f, StsWaterLegX - 1f, StsLandLegX + 1f, -325f])
                foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
                    OBox(lamps, id, P(x, StsBoomY - 0.35f, side * (StsGirderHalfZ + 0.2f)), new float3(0.9f, 0.5f, 0.7f));
            if (night)
            {
                OBox(pool, id, P((StsWaterLegX + StsLandLegX) / 2, 0.03f, 0), new float3(36f, 0.04f, 30f));
                OBox(poolCore, id, P(StsQuayLaneX - 4f, 0.05f, 0), new float3(18f, 0.04f, 16f));
            }
        }

        // --- Behind the cranes: blocks of stacks, two straddling gantries, flood masts ---
        foreach (var bz in (ReadOnlySpan<float>)[-230f, -95f, 35f, 170f])
            for (var row = 0; row < 7; row++)
                for (var bay = 0; bay < 4; bay++)
                {
                    var x = -350f - row * 2.6f;
                    var z = bz + bay * 13f;
                    var tiers = 2 + random.Next(3);
                    for (var t = 0; t < tiers; t++)
                        Box(float4x4.CreateRotationY(float.Pi / 2) * float4x4.CreateTranslation(o + new float3(x, t * ContainerHeight, z)));
                }
        if (TryModel("gantry_crane") is { } rtg)
            foreach (var z in (ReadOnlySpan<float>)[-75f, 190f])
                AddModel(rtg, float4x4.CreateRotationY(0) * float4x4.CreateTranslation(o + new float3(-357.8f, 0, z)), new float3(0.86f, 0.64f, 0.06f));
        if (TryModel("flood_mast") is { } mast)
            foreach (var z in (ReadOnlySpan<float>)[-280f, -40f, 110f, 250f])
                AddModel(mast, float4x4.CreateTranslation(o + new float3(-338f, 0, z)), new float3(0.6f, 0.6f, 0.58f));   // (lamps facing +x: the quay)

        if ((TryModel("container_lod") ?? TryModel("container")) is { } container)
            foreach (var (c, transforms) in models)
                foreach (var (mesh, material) in Engine.Model.MergeInstances(container, transforms))
                    _props.Add(new Prop(mesh, float4x4.Identity, FarContainerColors[c], material.Material, Part: material));

        FlushPoliceBatches();
    }
}
