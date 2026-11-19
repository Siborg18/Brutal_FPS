using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The jungle lab: a clandestine cocaine camp in a clearing deep in the Amazon rainforest. The olive portal is in the
/// back wall of a plank storage shack at the south edge of the clearing; step out and the camp is ahead: a big open-sided
/// shelter of poles under black plastic sheeting, with a green tarp hung down one side, a heap of fresh coca leaf on a
/// plastic sheet, blue and black plastic drums on a plank stand, jerrycans, buckets and sacks. A smaller lean-to stands
/// off to the east, a tarp hut with hammocks and a cooking fire to the west, a generator by the shack, and a plank
/// walkway disappears west into the forest. Round it all the rainforest closes in: tall trees, palms, bananas, ferns and
/// a canopy that shades everything but the clearing (the shader's area 6: humid haze, a high sun through the leaves).
/// </summary>
public sealed partial class Level
{
    /// <summary>Where the jungle is (the shader's area 6: x &gt;= 16000).</summary>
    public static readonly float3 JungleOrigin = new(20000, 0, 0);

    /// <summary>How far the olive portal stands out from the shack's wall (more than the player's radius: they need that room behind the water to walk through).</summary>
    public const float JunglePortalDepth = 0.35f;

    /// <summary>The olive portal: on the storage shack's north wall, facing out into the clearing.</summary>
    public static float3 JunglePortalFloorPoint => JungleOrigin + new float3(0, 0, -JunglePortalDepth);
    public static float3 JunglePortalNormal => new(0, 0, -1);

    /// <summary>The clearing: an ellipse round this point (x, z radii). The shader opens the canopy over it.</summary>
    public static float3 JungleClearingCenter => JungleOrigin + new float3(0, 0, -12);
    public static readonly float2 JungleClearingRadii = new(19f, 16f);

    /// <summary>The sun over the jungle (high, a little west of south) and its shadow map's extent.</summary>
    public static readonly float3 JungleSunDirection = new(0.30f, 0.88f, 0.36f);
    public static float3 JungleShadowCenter => JungleClearingCenter;
    public const float JungleShadowHalfExtent = 70f;

    // The storage shack: 5.2 x 3.6 m, its north wall (with the portal) facing the clearing.
    private static readonly float3 BodegaMin = JungleOrigin + new float3(-2.6f, 0, 0);
    private static readonly float3 BodegaMax = JungleOrigin + new float3(2.6f, 2.6f, 3.6f);
    private const float BodegaWall = 0.15f;

    // The main shelter: 10 x 7 m of poles, its ridge running east-west.
    private static readonly float3 LabMin = JungleOrigin + new float3(-5f, 0, -16f);
    private static readonly float3 LabMax = JungleOrigin + new float3(5f, 3.4f, -9f);
    private const float LabEaves = 2.3f;

    // Where the player can go: a box round the clearing (the forest is too dense to get far into).
    private static readonly float3 JungleBoundsMin = JungleOrigin + new float3(-27f, 0, -37f);
    private static readonly float3 JungleBoundsMax = JungleOrigin + new float3(27f, 0, 9f);

    private static readonly float3 JungleGroundTint = new(0.25f, 0.18f, 0.12f);
    private static readonly float3 PoleTint = new(0.40f, 0.33f, 0.24f);
    private static readonly float3 PlankTint = new(0.44f, 0.35f, 0.24f);
    private static readonly float3 BlackPlastic = new(0.05f, 0.05f, 0.055f);
    private static readonly float3 GreenTarp = new(0.10f, 0.34f, 0.24f);
    private static readonly float3 BlueTarp = new(0.10f, 0.22f, 0.45f);
    private static readonly float3 BlueDrum = new(0.08f, 0.24f, 0.55f);
    private static readonly float3 BlackDrum = new(0.06f, 0.06f, 0.065f);
    private static readonly float3 CocaLeaf = new(0.26f, 0.40f, 0.10f);
    private static readonly float3 CanopyGreen = new(0.17f, 0.30f, 0.11f);
    private static readonly float3 ShrubDark = new(0.19f, 0.33f, 0.12f);
    private static readonly float3 ShrubLight = new(0.30f, 0.44f, 0.15f);
    private static readonly float3 PalmGreen = new(0.26f, 0.40f, 0.14f);
    private static readonly float3 BananaGreen = new(0.36f, 0.50f, 0.15f);

    /// <summary>The dim spaces under the shelters' roofs (the sky's light falls off under them).</summary>
    public static ShadeVolume[] JungleShadeVolumes =>
    [
        new(new Aabb(LabMin + new float3(0, -0.05f, 0), new float3(LabMax.X, LabEaves + 0.9f, LabMax.Z)),
            (LabMin + LabMax) / 2 + new float3(0, 5f, 0), 6f, 0.45f),
        new(new Aabb(JungleOrigin + new float3(6.5f, -0.05f, -22.5f), JungleOrigin + new float3(11.5f, 2.6f, -18.5f)),
            JungleOrigin + new float3(9f, 4.5f, -18.5f), 4f, 0.5f),
        new(new Aabb(JungleOrigin + new float3(-14f, -0.05f, -8.5f), JungleOrigin + new float3(-9f, 2.4f, -4.5f)),
            JungleOrigin + new float3(-11.5f, 4f, -6.5f), 3.5f, 0.5f),
        BodegaShade,
    ];

    private void BuildJungle()
    {
        var o = JungleOrigin;
        var random = new Random(1977);

        // The forest floor: dark wet earth (the shader strews it with leaf litter and moss).
        AddMinMax(o + new float3(-300, -1, -300), o + new float3(300, 0, 300), JungleGroundTint, Material.RedDirt);

        BuildBodega();
        BuildLabShelter(random);
        BuildLeanTo();
        BuildCamp(random);
        BuildGenerator();
        BuildWalkway();
        BuildGunNest();
        BuildRainforest(random);
        Log.Write($"Jungle foliage cover: {Foliage.Count} leaf volumes (NPCs can't see through them; bullets can)");

        // You can only go so far: invisible bounds round the clearing and its edge.
        var min = JungleBoundsMin;
        var max = JungleBoundsMax;
        AddStatic(new Aabb(new float3(min.X, 0, min.Z - 2), new float3(max.X, 30, min.Z)), default, visible: false);
        AddStatic(new Aabb(new float3(min.X, 0, max.Z), new float3(max.X, 30, max.Z + 2)), default, visible: false);
        AddStatic(new Aabb(new float3(min.X - 2, 0, min.Z - 2), new float3(min.X, 30, max.Z + 2)), default, visible: false);
        AddStatic(new Aabb(new float3(max.X, 0, min.Z - 2), new float3(max.X + 2, 30, max.Z + 2)), default, visible: false);
    }

    /// <summary>True if (x, z) (relative to the jungle origin) is inside the clearing's ellipse scaled by <paramref name="scale"/>.</summary>
    private static bool InClearing(float x, float z, float scale = 1f)
    {
        var dx = x / (JungleClearingRadii.X * scale);
        var dz = (z + 12f) / (JungleClearingRadii.Y * scale);
        return dx * dx + dz * dz < 1f;
    }

    /// <summary>True if (x, z) (relative to the jungle origin) is where the player can walk.</summary>
    private static bool InJungleBounds(float x, float z, float margin = 0f) =>
        x > JungleBoundsMin.X - JungleOrigin.X - margin && x < JungleBoundsMax.X - JungleOrigin.X + margin
        && z > JungleBoundsMin.Z - JungleOrigin.Z - margin && z < JungleBoundsMax.Z - JungleOrigin.Z + margin;

    // ------------------------------------------------------------------------------------------------
    // The camp.
    // ------------------------------------------------------------------------------------------------

    /// <summary>The storage shack: rough plank walls under a rusty tin roof; the portal is in its north wall.</summary>
    private void BuildBodega()
    {
        var o = JungleOrigin;
        var t = BodegaWall;
        var min = BodegaMin;
        var max = BodegaMax;
        AddMinMax(min, new float3(max.X, max.Y, min.Z + t), PlankTint, Material.Wood);                          // north (portal)
        AddMinMax(new float3(min.X, 0, max.Z - t), max, PlankTint * 0.9f, Material.Wood);                       // south
        AddMinMax(min, new float3(min.X + t, max.Y, max.Z), PlankTint * 0.95f, Material.Wood);                  // west
        AddMinMax(new float3(max.X - t, 0, min.Z), max, PlankTint * 0.95f, Material.Wood);                      // east
        AddMinMax(new float3(min.X, max.Y - 0.12f, min.Z), max, PlankTint * 0.8f, Material.Wood);               // ceiling
        // Corner posts and a sill plate, so the plank walls read as a frame.
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(min.X, min.Z), (max.X, min.Z), (min.X, max.Z), (max.X, max.Z)])
            Add(new float3(x, max.Y / 2, z), new float3(0.16f, max.Y, 0.16f), PoleTint * 0.8f, Material.Wood, collides: false);
        Add(new float3(o.X, 0.1f, min.Z - 0.02f), new float3(max.X - min.X + 0.1f, 0.2f, 0.06f), PoleTint * 0.7f, Material.Wood, collides: false);
        // A skillion roof of rusty corrugated iron, falling to the back, with deep eaves over the portal.
        var roof = float4x4.CreateScale(max.X - min.X + 1.0f, 0.05f, max.Z - min.Z + 1.6f)
            * float4x4.CreateRotationX(0.12f)
            * float4x4.CreateTranslation(new float3(o.X, max.Y + 0.35f, (min.Z + max.Z) / 2 - 0.3f));
        _props.Add(new Prop(UnitBox, roof, new float3(0.38f, 0.26f, 0.18f), Material.RustyMetal));
        // Its door, the back of the portal, the PC and the whiteboard (Game/LevelJungleShack.cs).
        BuildBodegaInterior();
        // Odds and ends against the front wall: sacks, a jerrycan, a stack of plastic crates.
        AddSack(o + new float3(-1.9f, 0, -0.45f), 0.3f);
        AddSack(o + new float3(-2.0f, 0.32f, -0.5f), -0.4f);
        AddSack(o + new float3(-2.2f, 0, -1.0f), 1.2f);
        AddJerrycan(o + new float3(1.8f, 0, -0.4f), new float3(0.80f, 0.55f, 0.10f), 0.3f);
        AddJerrycan(o + new float3(2.15f, 0, -0.45f), new float3(0.75f, 0.20f, 0.10f), -0.2f);
        AddCollider(o + new float3(-2.05f, 0.35f, -0.7f), new float3(0.9f, 0.7f, 1.0f));
        AddCollider(o + new float3(2.0f, 0.25f, -0.42f), new float3(0.8f, 0.5f, 0.4f));
        // A rain barrel at the corner, under the roof's drip.
        AddDrum(o + new float3(3.2f, 0, 3.15f), BlueDrum, 0.4f, Material.PaintedMetal);   // (clear of the door's leaf, open)
    }

    /// <summary>
    /// The main shelter: an open frame of poles under black plastic sheeting, a green tarp hung down its north side.
    /// Under it, the heap of coca leaf on a plastic sheet, drums on a plank stand, buckets, a table and sacks.
    /// </summary>
    private void BuildLabShelter(Random random)
    {
        var o = JungleOrigin;
        var min = LabMin;
        var max = LabMax;
        var midZ = (min.Z + max.Z) / 2;
        var halfDepth = (max.Z - min.Z) / 2;

        // Poles: a row along each long side and taller ones under the ridge; tie beams along the eaves.
        for (var i = 0; i <= 4; i++)
        {
            var x = min.X + i * (max.X - min.X) / 4;
            foreach (var z in (ReadOnlySpan<float>)[min.Z, max.Z])
                AddPole(new float3(x, 0, z), LabEaves + 0.1f, 0.07f);
            if (i == 0 || i == 4)
                AddPole(new float3(x, 0, midZ), max.Y, 0.08f);
        }
        foreach (var z in (ReadOnlySpan<float>)[min.Z, max.Z])
            Add(new float3(o.X, LabEaves, z), new float3(max.X - min.X + 0.3f, 0.1f, 0.1f), PoleTint * 0.9f, Material.Wood, collides: false);
        Add(new float3(o.X, max.Y - 0.05f, midZ), new float3(max.X - min.X + 0.4f, 0.1f, 0.1f), PoleTint * 0.9f, Material.Wood, collides: false);

        // The roof: two slopes of black plastic, sagging a little between the rafters (drawn as a slab per bay).
        var rise = max.Y - LabEaves;
        var slope = float.Atan2(rise, halfDepth);
        var slab = float.Sqrt(rise * rise + halfDepth * halfDepth) + 0.6f;
        for (var bay = 0; bay < 4; bay++)
        {
            var bx = min.X + (bay + 0.5f) * (max.X - min.X) / 4;
            var sag = 0.04f + 0.03f * (float)random.NextDouble();
            foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            {
                var sheet = float4x4.CreateScale((max.X - min.X) / 4 + 0.08f, 0.02f, slab)
                    * float4x4.CreateRotationX(side * slope)
                    * float4x4.CreateTranslation(new float3(bx, (LabEaves + max.Y) / 2 - sag, midZ + side * halfDepth / 2));
                _props.Add(new Prop(UnitBox, sheet, BlackPlastic, Material.Cloth));
            }
        }
        // The roof blocks shots and keeps the rain off; it's out of reach, so it needs no player collision.

        // A green tarp hung down the north side from the eaves, tied off short of the ground, and a blue one half
        // way along the west end.
        Add(new float3(o.X - 0.5f, (LabEaves + 0.35f) / 2, min.Z - 0.04f), new float3(max.X - min.X - 1.6f, LabEaves - 0.35f, 0.03f), GreenTarp, Material.Cloth);
        Add(new float3(min.X - 0.04f, (LabEaves + 0.5f) / 2, min.Z + 2.0f), new float3(0.03f, LabEaves - 0.5f, 3.6f), BlueTarp, Material.Cloth);

        // The floor under the roof: a big black plastic sheet, and the heap of fresh green leaf on it.
        var sheetModel = float4x4.CreateScale(4.6f, 0.02f, 3.4f) * float4x4.CreateTranslation(o + new float3(-1.4f, 0.01f, -12.2f));
        _props.Add(new Prop(UnitBox, sheetModel, BlackPlastic * 1.2f, Material.Cloth, Decal: true));
        var heap = new MeshBuilder();
        // A low, lumpy mound of small clumps, highest in the middle, spilling out at the edges.
        for (var i = 0; i < 260; i++)
        {
            var a = (float)random.NextDouble() * float.Tau;
            var r = float.Sqrt((float)random.NextDouble());
            var x = float.Cos(a) * r * 1.9f;
            var z = float.Sin(a) * r * 1.3f;
            var h = 0.55f * (1f - r * r);
            var s = 0.12f + 0.12f * (float)random.NextDouble();
            heap.Blob(o + new float3(-1.4f + x, h * 0.8f, -12.2f + z), new float3(s * 1.3f, s * 0.5f, s * 1.3f), random, 6, 3);
        }
        // Loose leaf trodden about round it.
        for (var i = 0; i < 70; i++)
        {
            var x = -1.4f + ((float)random.NextDouble() * 2 - 1) * 2.6f;
            var z = -12.2f + ((float)random.NextDouble() * 2 - 1) * 1.9f;
            heap.Blob(o + new float3(x, 0.02f, z), new float3(0.12f, 0.025f, 0.12f), random, 5, 2);
        }
        _props.Add(new Prop(heap.Build(), float4x4.Identity, CocaLeaf, Material.Bush));
        // A sack being filled at the edge of the heap, and a plastic basket.
        AddSack(o + new float3(1.0f, 0, -11.4f), 0.6f);
        AddBucket(o + new float3(0.95f, 0, -13.2f), new float3(0.12f, 0.30f, 0.62f), 0.36f, 0.32f);

        // A plank stand along the north side, the drums on it: blue plastic and black.
        var stand = o + new float3(-0.8f, 0, -15.3f);
        foreach (var dx in (ReadOnlySpan<float>)[-2.6f, -0.9f, 0.9f, 2.6f])
            foreach (var dz in (ReadOnlySpan<float>)[-0.35f, 0.35f])
                Add(stand + new float3(dx, 0.2f, dz), new float3(0.1f, 0.4f, 0.1f), PoleTint * 0.8f, Material.Wood, collides: false);
        Add(stand + new float3(0, 0.43f, 0), new float3(5.6f, 0.06f, 0.9f), PlankTint * 0.85f, Material.Wood);
        AddCollider(stand + new float3(0, 0.2f, 0), new float3(5.6f, 0.4f, 0.9f));
        foreach (var (dx, tint) in (ReadOnlySpan<(float, float3)>)[(-2.3f, BlueDrum), (-1.6f, BlueDrum), (-0.9f, BlackDrum), (-0.2f, BlueDrum), (0.5f, BlackDrum), (1.2f, BlackDrum)])
            AddDrum(stand + new float3(dx, 0.46f, 0), tint, dx * 2.1f, Material.PaintedMetal);
        // On the ground in front: an open drum, buckets, a basin, jerrycans.
        AddDrum(o + new float3(3.4f, 0, -14.6f), BlueDrum * 0.9f, 1.0f, Material.PaintedMetal);
        AddDrum(o + new float3(3.9f, 0, -13.8f), BlackDrum, 2.3f, Material.PaintedMetal);
        AddBucket(o + new float3(2.4f, 0, -14.4f), new float3(0.85f, 0.45f, 0.10f), 0.3f, 0.36f);
        AddBucket(o + new float3(2.1f, 0, -13.7f), new float3(0.80f, 0.78f, 0.72f), 0.3f, 0.36f);
        AddBucket(o + new float3(-4.2f, 0, -10.2f), new float3(0.12f, 0.30f, 0.62f), 0.55f, 0.18f);     // a wide basin
        AddJerrycan(o + new float3(4.2f, 0, -12.6f), new float3(0.82f, 0.60f, 0.12f), 0.9f);
        AddJerrycan(o + new float3(4.3f, 0, -12.0f), new float3(0.82f, 0.60f, 0.12f), 1.4f);
        AddJerrycan(o + new float3(4.0f, 0, -11.4f), new float3(0.78f, 0.22f, 0.10f), 1.1f);

        // A rough work table at the east end, a crate seat, a few things on it.
        var table = o + new float3(3.0f, 0, -10.2f);
        foreach (var (dx, dz) in (ReadOnlySpan<(float, float)>)[(-0.8f, -0.35f), (0.8f, -0.35f), (-0.8f, 0.35f), (0.8f, 0.35f)])
            Add(table + new float3(dx, 0.4f, dz), new float3(0.07f, 0.8f, 0.07f), PoleTint * 0.85f, Material.Wood, collides: false);
        Add(table + new float3(0, 0.82f, 0), new float3(1.8f, 0.05f, 0.85f), PlankTint, Material.Wood);
        AddCollider(table + new float3(0, 0.4f, 0), new float3(1.8f, 0.8f, 0.85f));
        AddBucket(table + new float3(-0.5f, 0.845f, 0.1f), new float3(0.82f, 0.82f, 0.78f), 0.15f, 0.22f);
        Add(table + new float3(0.3f, 0.87f, -0.1f), new float3(0.45f, 0.04f, 0.32f), new float3(0.55f, 0.55f, 0.55f), Material.Steel, collides: false);  // a scale pan
        Add(table + new float3(0.45f, 0.9f, 0.22f), new float3(0.08f, 0.12f, 0.08f), new float3(0.70f, 0.70f, 0.66f), Material.Plain, collides: false); // a plastic bottle
        AddCrate(table + new float3(0.2f, 0, 0.9f), 0.45f, WoodTint * 0.8f, 1);

        // Sacks stacked on a pallet at the west end.
        AddPallet(o + new float3(-3.7f, 0, -14.6f), 1);
        for (var i = 0; i < 5; i++)
            AddSack(o + new float3(-4.1f + (i % 3) * 0.4f, 0.15f + (i / 3) * 0.3f, -14.6f + (i % 2) * 0.15f), i * 0.7f);

        // A bare bulb hanging from the ridge on its flex (the generator's lead runs up the east pole to it).
        var bulb = o + new float3(0.5f, max.Y - 0.1f, midZ);
        Add(bulb + new float3(0, -0.35f, 0), new float3(0.012f, 0.7f, 0.012f), new float3(0.05f, 0.05f, 0.05f), Material.Plain, blocksShots: false, collides: false);
        Add(bulb + new float3(0, -0.75f, 0), new float3(0.07f, 0.09f, 0.07f), new float3(1.0f, 0.86f, 0.62f), Material.Emissive, blocksShots: false, collides: false);
        Add(new float3(o.X + 0.25f, max.Y - 0.12f, midZ), new float3(max.X - o.X - 0.3f, 0.015f, 0.015f), new float3(0.05f, 0.05f, 0.05f), Material.Plain, blocksShots: false, collides: false);
    }

    /// <summary>The lean-to east of the main shelter: a plank counter under black plastic, with drums and cans.</summary>
    private void BuildLeanTo()
    {
        var o = JungleOrigin;
        var min = o + new float3(6.5f, 0, -22.5f);
        var max = o + new float3(11.5f, 0, -18.5f);
        const float front = 2.4f, back = 1.8f;
        foreach (var x in (ReadOnlySpan<float>)[min.X, (min.X + max.X) / 2, max.X])
        {
            AddPole(new float3(x, 0, max.Z), front, 0.06f);
            AddPole(new float3(x, 0, min.Z), back, 0.06f);
        }
        var fall = float.Atan2(front - back, max.Z - min.Z);
        var roof = float4x4.CreateScale(max.X - min.X + 0.6f, 0.02f, max.Z - min.Z + 0.8f) * float4x4.CreateRotationX(-fall)
            * float4x4.CreateTranslation(new float3((min.X + max.X) / 2, (front + back) / 2 + 0.05f, (min.Z + max.Z) / 2));
        _props.Add(new Prop(UnitBox, roof, BlackPlastic, Material.Cloth));
        Add(new float3((min.X + max.X) / 2, back / 2 + 0.2f, min.Z - 0.04f), new float3(max.X - min.X, back - 0.4f, 0.03f), GreenTarp * 0.9f, Material.Cloth);

        // A raised plank counter along the back with drums and cans on it, a red drum and a white one beside.
        var counter = new float3((min.X + max.X) / 2, 0, min.Z + 0.7f);
        foreach (var dx in (ReadOnlySpan<float>)[-2.0f, 0f, 2.0f])
            foreach (var dz in (ReadOnlySpan<float>)[-0.3f, 0.3f])
                Add(counter + new float3(dx, 0.35f, dz), new float3(0.08f, 0.7f, 0.08f), PoleTint * 0.8f, Material.Wood, collides: false);
        Add(counter + new float3(0, 0.72f, 0), new float3(4.4f, 0.06f, 0.8f), PlankTint * 0.85f, Material.Wood);
        AddCollider(counter + new float3(0, 0.36f, 0), new float3(4.4f, 0.72f, 0.8f));
        AddJerrycan(counter + new float3(-1.4f, 0.75f, 0), new float3(0.12f, 0.30f, 0.62f), 0.2f);
        AddJerrycan(counter + new float3(-0.9f, 0.75f, 0.05f), new float3(0.82f, 0.82f, 0.78f), -0.3f);
        AddBucket(counter + new float3(0.3f, 0.75f, 0), new float3(0.85f, 0.45f, 0.10f), 0.28f, 0.34f);
        AddBucket(counter + new float3(1.3f, 0.75f, -0.05f), new float3(0.15f, 0.42f, 0.2f), 0.24f, 0.3f);
        AddDrum(new float3(min.X - 0.7f, 0, min.Z + 0.8f), new float3(0.55f, 0.14f, 0.10f), 0.3f, Material.PaintedMetal);
        AddDrum(new float3(min.X - 0.75f, 0, min.Z + 1.5f), new float3(0.78f, 0.78f, 0.74f), 1.7f, Material.PaintedMetal);
        AddDrum(new float3(max.X + 0.6f, 0, (min.Z + max.Z) / 2), BlueDrum, 0.9f, Material.PaintedMetal);
        AddCrate(new float3(max.X - 0.8f, 0, max.Z - 0.9f), 0.55f, WoodTint * 0.75f);
    }

    /// <summary>The workers' camp west of the shelter: a tarp hut with two hammocks, and a cooking fire.</summary>
    private void BuildCamp(Random random)
    {
        var o = JungleOrigin;
        var min = o + new float3(-14f, 0, -8.5f);
        var max = o + new float3(-9f, 0, -4.5f);
        const float eaves = 2.0f, ridge = 2.7f;
        var midZ = (min.Z + max.Z) / 2;
        foreach (var x in (ReadOnlySpan<float>)[min.X, max.X])
        {
            AddPole(new float3(x, 0, min.Z), eaves, 0.06f);
            AddPole(new float3(x, 0, max.Z), eaves, 0.06f);
            AddPole(new float3(x, 0, midZ), ridge, 0.06f);
        }
        Add(new float3((min.X + max.X) / 2, ridge - 0.05f, midZ), new float3(max.X - min.X + 0.3f, 0.08f, 0.08f), PoleTint, Material.Wood, collides: false);
        var half = (max.Z - min.Z) / 2;
        var slope = float.Atan2(ridge - eaves, half);
        var slab = float.Sqrt((ridge - eaves) * (ridge - eaves) + half * half) + 0.4f;
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var sheet = float4x4.CreateScale(max.X - min.X + 0.5f, 0.02f, slab) * float4x4.CreateRotationX(side * slope)
                * float4x4.CreateTranslation(new float3((min.X + max.X) / 2, (eaves + ridge) / 2, midZ + side * half / 2));
            _props.Add(new Prop(UnitBox, sheet, side < 0 ? BlueTarp : BlackPlastic, Material.Cloth));
        }
        // Two hammocks slung between the poles: a sagging length of cloth each (two tilted halves), and their ropes.
        foreach (var z in (ReadOnlySpan<float>)[min.Z + 0.9f, max.Z - 0.9f])
        {
            var tint = z < midZ ? new float3(0.55f, 0.18f, 0.12f) : new float3(0.18f, 0.40f, 0.30f);
            var length = max.X - min.X - 0.6f;
            foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            {
                var cloth = float4x4.CreateScale(length / 2 + 0.05f, 0.02f, 0.75f) * float4x4.CreateRotationZ(side * 0.32f)
                    * float4x4.CreateTranslation(new float3((min.X + max.X) / 2 + side * length / 4, 0.95f, z));
                _props.Add(new Prop(UnitBox, cloth, tint, Material.Cloth));
            }
        }
        // Clothes on a line, boots, a plastic chair.
        Add(new float3((min.X + max.X) / 2, 1.75f, max.Z + 0.05f), new float3(max.X - min.X, 0.01f, 0.01f), new float3(0.2f, 0.2f, 0.18f), Material.Plain, blocksShots: false, collides: false);
        foreach (var (dx, tint) in (ReadOnlySpan<(float, float3)>)[(-1.5f, new(0.75f, 0.72f, 0.65f)), (-0.6f, new(0.20f, 0.30f, 0.55f)), (0.6f, new(0.65f, 0.15f, 0.12f))])
            Add(new float3((min.X + max.X) / 2 + dx, 1.45f, max.Z + 0.05f), new float3(0.55f, 0.6f, 0.02f), tint, Material.Cloth, blocksShots: false, collides: false);
        foreach (var dx in (ReadOnlySpan<float>)[0f, 0.16f])
            Add(new float3(max.X + 0.4f + dx, 0.15f, midZ + 0.8f), new float3(0.1f, 0.3f, 0.28f), new float3(0.05f, 0.05f, 0.05f), Material.Plain, collides: false); // rubber boots
        var chair = new float3(max.X + 1.4f, 0, midZ - 0.6f);
        Add(chair + new float3(0, 0.42f, 0), new float3(0.45f, 0.04f, 0.45f), new float3(0.82f, 0.82f, 0.78f), Material.Plain);
        Add(chair + new float3(0, 0.72f, 0.2f), new float3(0.45f, 0.55f, 0.04f), new float3(0.82f, 0.82f, 0.78f), Material.Plain, collides: false);
        foreach (var (dx, dz) in (ReadOnlySpan<(float, float)>)[(-0.2f, -0.2f), (0.2f, -0.2f), (-0.2f, 0.2f), (0.2f, 0.2f)])
            Add(chair + new float3(dx, 0.2f, dz), new float3(0.04f, 0.4f, 0.04f), new float3(0.82f, 0.82f, 0.78f), Material.Plain, collides: false);

        // The cooking fire: three stones, a blackened pot on them, ash and charred sticks, firewood stacked by it.
        var fire = o + new float3(-11.5f, 0, -2.5f);
        var stones = new MeshBuilder();
        for (var i = 0; i < 3; i++)
        {
            var a = i * float.Tau / 3 + 0.4f;
            stones.Blob(fire + new float3(float.Cos(a) * 0.28f, 0.08f, float.Sin(a) * 0.28f), new float3(0.14f, 0.12f, 0.13f), random, 7, 4);
        }
        _props.Add(new Prop(stones.Build(), float4x4.Identity, new float3(0.30f, 0.29f, 0.27f), Material.Rock));
        var ash = float4x4.CreateScale(0.9f, 0.02f, 0.9f) * float4x4.CreateTranslation(fire + new float3(0, 0.01f, 0));
        _props.Add(new Prop(UnitBox, ash, new float3(0.10f, 0.09f, 0.085f), Material.Plain, Decal: true));
        _bucketMesh ??= new MeshBuilder().Cylinder(1f, -0.5f, 0.5f, 18).Build();
        _props.Add(new Prop(_bucketMesh, float4x4.CreateScale(0.24f, 0.24f, 0.22f) * float4x4.CreateRotationX(-float.Pi / 2) * float4x4.CreateTranslation(fire + new float3(0, 0.31f, 0)),
            new float3(0.08f, 0.08f, 0.08f), Material.Steel));
        for (var i = 0; i < 4; i++)
        {
            var stick = float4x4.CreateScale(0.6f, 0.05f, 0.05f) * float4x4.CreateRotationY(i * 1.3f) * float4x4.CreateTranslation(fire + new float3(0, 0.04f, 0));
            _props.Add(new Prop(UnitBox, stick, new float3(0.07f, 0.06f, 0.05f), Material.Wood));
        }
        AddCollider(fire + new float3(0, 0.25f, 0), new float3(0.8f, 0.5f, 0.8f), blocksShots: false);
        var wood = o + new float3(-13.4f, 0, -2.2f);
        for (var row = 0; row < 3; row++)
            for (var k = 0; k < 4 - row; k++)
                Add(wood + new float3(0, 0.07f + row * 0.13f, (k - (3 - row) / 2f) * 0.14f), new float3(0.9f, 0.12f, 0.12f), PoleTint * (0.75f + 0.08f * k), Material.Wood, collides: false);
        AddCollider(wood + new float3(0, 0.2f, 0), new float3(0.9f, 0.4f, 0.6f));
    }

    /// <summary>A small petrol generator on a pallet by the shack, a fuel drum beside it, and its lead run off on stakes.</summary>
    private void BuildGenerator()
    {
        var o = JungleOrigin;
        var at = o + new float3(5.0f, 0, -3.5f);
        AddPallet(at, 1);
        var red = new float3(0.62f, 0.12f, 0.08f);
        var frame = new float3(0.12f, 0.12f, 0.12f);
        Add(at + new float3(0, 0.45f, 0), new float3(0.7f, 0.42f, 0.48f), red, Material.PaintedMetal);
        Add(at + new float3(0.12f, 0.73f, 0), new float3(0.38f, 0.14f, 0.36f), red * 1.1f, Material.PaintedMetal, collides: false);   // tank
        Add(at + new float3(-0.22f, 0.5f, 0.25f), new float3(0.2f, 0.2f, 0.03f), new float3(0.2f, 0.2f, 0.2f), Material.Steel, collides: false);  // panel
        foreach (var (dx, dz) in (ReadOnlySpan<(float, float)>)[(-0.38f, -0.27f), (0.38f, -0.27f), (-0.38f, 0.27f), (0.38f, 0.27f)])
            Add(at + new float3(dx, 0.5f, dz), new float3(0.03f, 0.7f, 0.03f), frame, Material.Steel, collides: false);
        Add(at + new float3(0, 0.86f, -0.27f), new float3(0.8f, 0.03f, 0.03f), frame, Material.Steel, collides: false);
        Add(at + new float3(0, 0.86f, 0.27f), new float3(0.8f, 0.03f, 0.03f), frame, Material.Steel, collides: false);
        AddDrum(at + new float3(1.1f, 0, 0.2f), new float3(0.20f, 0.30f, 0.18f), 0.6f, Material.RustyMetal);
        // The lead, on stakes across to the shelter's east pole and up it to the bulb.
        var stakes = new[] { at + new float3(-0.6f, 0, -1.5f), o + new float3(5.3f, 0, -6.5f), o + new float3(5.2f, 0, -8.6f) };
        foreach (var s in stakes)
            Add(s + new float3(0, 0.6f, 0), new float3(0.04f, 1.2f, 0.04f), PoleTint, Material.Wood, blocksShots: false, collides: false);
        var cable = new float3(0.04f, 0.04f, 0.04f);
        var points = new List<float3> { at + new float3(-0.35f, 0.5f, 0) };
        points.AddRange(stakes.Select(s => s + new float3(0, 1.15f, 0)));
        points.Add(new float3(LabMax.X, LabMax.Y - 0.2f, (LabMin.Z + LabMax.Z) / 2));
        for (var i = 0; i + 1 < points.Count; i++)
            AddCable(points[i], points[i + 1], cable);
    }

    /// <summary>A walkway of planks laid over the mud, heading west out of the clearing into the forest.</summary>
    private void BuildWalkway()
    {
        var o = JungleOrigin;
        var random = new Random(31);
        for (var i = 0; i < 26; i++)
        {
            var x = -16.5f - i * 0.9f;
            var z = -14f + float.Sin(i * 0.25f) * 1.2f;
            var plank = float4x4.CreateScale(0.32f, 0.05f, 1.1f + 0.2f * (float)random.NextDouble())
                * float4x4.CreateRotationY(((float)random.NextDouble() - 0.5f) * 0.3f)
                * float4x4.CreateTranslation(o + new float3(x, 0.03f, z));
            _props.Add(new Prop(UnitBox, plank, PlankTint * (0.6f + 0.25f * (float)random.NextDouble()), Material.Wood));
        }
    }

    /// <summary>The middle of the gun nest on the ground, in the clearing's north-west corner, its bunker behind it.</summary>
    public static float3 GunNestOrigin => JungleOrigin + new float3(-9.8f, 0, -19f);

    /// <summary>Which way the nest looks (radians from south, +Z, towards the east): over the clearing, at the shack's door and the portal.</summary>
    public const float GunNestFacing = 0.471f;

    /// <summary>Where the gun lies on the tripod (Tools/blender/build_gun_nest.py: CRADLE).</summary>
    public static float3 GunNestCradle => GunNestOrigin + new float3(-0.25f * 0.4540f, 1.06f, -0.25f * 0.8910f);

    /// <summary>
    /// The gun nest (Assets/Models/gun_nest.glb: Tools/blender/build_gun_nest.py): a horseshoe of sandbags on a plank floor,
    /// a tripod, a crate of ammunition tins, and a concrete bunker behind it. It sits inside the clearing's ellipse, so no
    /// tree comes up through it; the ring of guards walk their beats behind it, with the nest's own man inside
    /// (Npcs.Populate). The M249 on the tripod is <see cref="GunNest"/>'s.
    /// </summary>
    private void BuildGunNest()
    {
        if (TryModel("gun_nest") is not { } nest)
            return;
        var o = GunNestOrigin;
        AddModel(nest, float4x4.CreateTranslation(o), float3.One);

        // The bags, stopping the player (the model stops bullets): boxes round the horseshoe, high enough to crouch behind.
        // (WALL_SPAN in the script. Its ends are 105 degrees or so either side of the facing, plus the headers laid across.)
        const float span = 2.02f, inner = 1.78f, outer = 2.40f, height = 0.98f;
        const int pieces = 14;
        for (var i = 0; i < pieces; i++)
        {
            var a0 = GunNestFacing - span + i * 2 * span / pieces;
            var a1 = a0 + 2 * span / pieces;
            float3 At(float angle, float r) => new(float.Sin(angle) * r, 0, float.Cos(angle) * r);
            var min = float3.Min(float3.Min(At(a0, inner), At(a0, outer)), float3.Min(At(a1, inner), At(a1, outer)));
            var max = float3.Max(float3.Max(At(a0, inner), At(a0, outer)), float3.Max(At(a1, inner), At(a1, outer)));
            AddStatic(new Aabb(o + new float3(min.X, 0, min.Z), o + new float3(max.X, height, max.Z)), default, visible: false);
        }

        // The bunker (BUNKER_* in the script), the tripod's legs and the crate with its tins.
        AddCollider(o + new float3(0.05f, 1.3f, -3.9f), new float3(3.66f, 2.6f, 2.96f));
        AddCollider(new float3(GunNestCradle.X, 0.5f, GunNestCradle.Z), new float3(0.5f, 1f, 0.5f), blocksShots: false);
        AddCollider(o + new float3(-0.91f, 0.35f, 0.857f), new float3(0.8f, 0.7f, 0.8f));

        // Ferns on the bunker's roof and bedding it in along its foot, where the forest is taking it back.
        var random = new Random(249);
        JungleTree?[] plants = [JungleTree.Load("jungle_fern_a"), JungleTree.Load("jungle_fern_b")];
        if (plants[0] is { } fernA && plants[1] is { } fernB)
        {
            (float X, float Y, float Z, float Size)[] ferns =
            [
                (-1.2f, 2.6f, -4.6f, 0.8f), (1.1f, 2.6f, -4.9f, 0.7f), (0.3f, 2.6f, -3.3f, 0.6f), (-1.75f, 2.6f, -2.75f, 0.55f),
                (-2.1f, 0, -2.8f, 1.0f), (-2.15f, 0, -4.9f, 1.2f), (2.0f, 0, -5.2f, 1.1f), (-0.6f, 0, -5.55f, 1.2f), (1.2f, 0, -5.5f, 1.0f),
                (-2.7f, 0, -0.6f, 0.9f), (-1.6f, 0, 2.7f, 0.8f), (2.75f, 0, 0.8f, 0.9f),
            ];
            foreach (var (x, y, z, size) in ferns)
            {
                var fern = random.Next(2) == 0 ? fernA : fernB;
                AddModel(fern.Model, float4x4.CreateScale(size) * float4x4.CreateRotationY((float)random.NextDouble() * float.Tau) * float4x4.CreateTranslation(o + new float3(x, y, z)), float3.One);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------
    // The rainforest round the clearing.
    // ------------------------------------------------------------------------------------------------

    private static readonly float3 BarkGrey = new(0.52f, 0.49f, 0.43f);
    private static readonly float3 BarkRed = new(0.48f, 0.33f, 0.24f);
    private static readonly float3 BarkMossy = new(0.30f, 0.35f, 0.20f);
    private static readonly float3 LianaTint = new(0.24f, 0.21f, 0.15f);
    private static readonly float3 LeafBright = new(0.36f, 0.54f, 0.16f);
    private static readonly float3 DeadFrond = new(0.40f, 0.29f, 0.16f);
    private static readonly float3 WallDark = new(0.15f, 0.27f, 0.10f);

    // The vegetation wall: its front runs round a rounded rectangle just inside the walkable bounds.
    private const float WallCenterZ = -14f;
    private const float WallHalfX = 25.8f;
    private const float WallHalfZ = 21.8f;
    private const float WallRoundness = 6f;

    /// <summary>
    /// A Blender tree (Tools/blender/build_jungle_trees.py): its model, and from its _points.json the trunk's radius
    /// (for the collider), the stilt roots' spread (walking palms) and the buttresses' outlines ([x, z, top] along each
    /// fin, model space). Instances are collected, then merged into one mesh per model part.
    /// </summary>
    private sealed record JungleTree(Engine.Model Model, float TrunkRadius, float Spread, float3[][] Fins)
    {
        public List<float4x4> Instances { get; } = [];

        public static JungleTree? Load(string name)
        {
            if (TryModel(name) is not { } model)
                return null;
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Models", name + "_points.json")));
                var root = json.RootElement;
                var fins = root.GetProperty("Fins"u8).EnumerateArray()
                    .Select(fin => fin.EnumerateArray().Select(p => new float3(p[0].GetSingle(), p[2].GetSingle(), p[1].GetSingle())).ToArray())
                    .ToArray();   // (x, top, z)
                var spread = root.TryGetProperty("Spread"u8, out var s) ? s.GetSingle() : 0f;
                return new JungleTree(model, root.GetProperty("TrunkRadius"u8).GetSingle(), spread, fins);
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
            {
                Log.Write($"[WARN] Jungle tree '{name}': no usable {name}_points.json ({e.Message}); using the built-in trees.");
                return null;
            }
        }
    }

    /// <summary>
    /// The leaves people can hide in (<see cref="FoliageCover"/>): NPCs can't see the player through enough of them,
    /// though bullets go straight through. Filled in as the undergrowth and the vegetation wall are placed.
    /// </summary>
    public FoliageCover Foliage { get; } = new();

    // How much of the view a metre of each kind of plant blocks (FoliageCover densities): for the built-in plants (the
    // Blender ones are measured from their leaves) and the vegetation wall's solid blobs.
    private const float FernCover = 0.5f, PalmCover = 0.35f, SaplingCover = 0.4f, ShrubCover = 1.2f, BananaCover = 0.3f, WallCover = 3f;

    /// <summary>Leaves filling (most of) a box: an ellipsoid a little inside it, as the tips of the leaves are sparse.</summary>
    private void AddFoliage(float3 center, float3 halfSize, float density) =>
        Foliage.Add(center, halfSize * 0.85f, density);

    /// <summary>
    /// Places an instance of <paramref name="tree"/> standing at <paramref name="ground"/>. With <paramref name="collide"/>
    /// its trunk (or cone of stilt roots) stops the player, and so do its buttresses where they're more than knee high.
    /// With <paramref name="hides"/> its leaves hide people from NPCs (<see cref="Foliage"/>).
    /// </summary>
    private void PlaceTree(JungleTree tree, float3 ground, float scale, float yaw, bool collide, bool hides = false)
    {
        var transform = float4x4.CreateScale(scale) * float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(ground - new float3(0, 0.05f, 0));
        tree.Instances.Add(transform);
        if (hides)
            Foliage.AddModel(tree.Model, transform, scale);
        if (!collide)
            return;
        var width = tree.Spread > 0 ? tree.Spread * 1.3f * scale : tree.TrunkRadius * 1.6f * scale;
        AddCollider(ground + new float3(0, 2f, 0), new float3(width, 4f, width));
        foreach (var fin in tree.Fins)
        {
            for (var k = 0; k + 1 < fin.Length; k++)
            {
                var top = float.Min(fin[k].Y, fin[k + 1].Y) * scale;
                if (top < 0.35f)
                    continue;
                var a = float3.Transform(new float3(fin[k].X, 0, fin[k].Z), transform);
                var b = float3.Transform(new float3(fin[k + 1].X, 0, fin[k + 1].Z), transform);
                var pad = new float3(0.15f * scale, 0, 0.15f * scale);
                var lo = float3.Min(a, b) - pad;
                var hi = float3.Max(a, b) + pad;
                AddStatic(new Aabb(new float3(lo.X, ground.Y, lo.Z), new float3(hi.X, ground.Y + float.Min(top, 2.5f), hi.Z)), default, visible: false);
            }
        }
    }

    /// <summary>
    /// The undergrowth modelled in Blender (Tools/blender/build_jungle_trees.py), each at a nominal size the game scales:
    /// ferns (fronds ~1 m), bananas (a 2.4 m stem), stemless palms (fronds ~3.5 m), broad-leaved saplings (1.6 m) and
    /// shrubs (~1 m). Null if any is missing (then the plants are built here as before).
    /// </summary>
    private sealed record Undergrowth(JungleTree[] Ferns, JungleTree[] Bananas, JungleTree[] GroundPalms, JungleTree[] Saplings, JungleTree[] Shrubs)
    {
        public IEnumerable<JungleTree> All => Ferns.Concat(Bananas).Concat(GroundPalms).Concat(Saplings).Concat(Shrubs);

        public static Undergrowth? Load()
        {
            JungleTree[]? Set(params string[] names)
            {
                var set = names.Select(JungleTree.Load).ToArray();
                return set.All(t => t is not null) ? set.Select(t => t!).ToArray() : null;
            }
            var (ferns, bananas, palms) = (Set("jungle_fern_a", "jungle_fern_b"), Set("jungle_banana_a", "jungle_banana_b"), Set("jungle_ground_palm"));
            var (saplings, shrubs) = (Set("jungle_sapling_a", "jungle_sapling_b"), Set("jungle_shrub_a", "jungle_shrub_b"));
            return ferns is null || bananas is null || palms is null || saplings is null || shrubs is null
                ? null
                : new Undergrowth(ferns, bananas, palms, saplings, shrubs);
        }
    }

    private Undergrowth? _undergrowth;

    private static JungleTree Pick(JungleTree[] set, Random random) => set[random.Next(set.Length)];

    private void Fern(MeshBuilder b, Random random, float3 ground, float size)
    {
        if (_undergrowth is { } u)
            PlaceTree(Pick(u.Ferns, random), ground, size, (float)random.NextDouble() * float.Tau, collide: false, hides: true);
        else
        {
            AddFern(b, random, ground, size);
            AddFoliage(ground + new float3(0, 0.4f * size, 0), new float3(0.9f * size, 0.45f * size, 0.9f * size), FernCover);
        }
    }

    private void GroundPalm(MeshBuilder b, Random random, float3 ground, float size)
    {
        if (_undergrowth is { } u)
            PlaceTree(Pick(u.GroundPalms, random), ground, size / 3.5f, (float)random.NextDouble() * float.Tau, collide: false, hides: true);
        else
        {
            AddGroundPalm(b, random, ground, size);
            AddFoliage(ground + new float3(0, 0.35f * size, 0), new float3(0.7f * size, 0.35f * size, 0.7f * size), PalmCover);
        }
    }

    private void BroadLeafPlant(ForestMeshes m, Random random, float3 ground, float height)
    {
        if (_undergrowth is { } u)
            PlaceTree(Pick(u.Saplings, random), ground, float.Max(height, 0.5f) / 1.6f, (float)random.NextDouble() * float.Tau, collide: false, hides: true);
        else
        {
            AddBroadLeafPlant(m, random, ground, height);
            AddFoliage(ground + new float3(0, 0.6f * height, 0), new float3(0.35f * height, 0.4f * height, 0.35f * height), SaplingCover);
        }
    }

    private void Shrub(MeshBuilder b, Random random, float3 ground, float size)
    {
        if (_undergrowth is { } u)
            PlaceTree(Pick(u.Shrubs, random), ground, size, (float)random.NextDouble() * float.Tau, collide: false, hides: true);
        else
        {
            AddShrub(b, random, ground, size);
            AddFoliage(ground + new float3(0, 0.5f * size, 0), new float3(0.6f * size, 0.5f * size, 0.6f * size), ShrubCover);
        }
    }

    private sealed class ForestMeshes
    {
        public readonly MeshBuilder BarkGrey = new(), BarkRed = new(), BarkMossy = new(), Lianas = new();
        public readonly MeshBuilder Canopy = new(), ShrubDark = new(), ShrubLight = new(), Wall = new(), WallBack = new();
        public readonly MeshBuilder Palms = new(), Bananas = new(), Leaves = new(), Dead = new();
    }

    private void BuildRainforest(Random random)
    {
        var o = JungleOrigin;
        var m = new ForestMeshes();

        // The ground beyond the walkable area rolls a little (low mounds and root-humps between the trees).
        var mounds = new MeshBuilder();
        for (var i = 0; i < 160; i++)
        {
            var x = ((float)random.NextDouble() * 2 - 1) * 110f;
            var z = -12f + ((float)random.NextDouble() * 2 - 1) * 100f;
            var r = 4f + (float)random.NextDouble() * 9f;
            if (InJungleBounds(x, z, r + 1f))
                continue;
            mounds.Hill(o + new float3(x, -0.4f, z), new float3(r, 0.6f + (float)random.NextDouble() * 1.8f, r * (0.7f + 0.6f * (float)random.NextDouble())), random, 20, 6);
        }
        var moundMesh = mounds.Build();
        _props.Add(new Prop(moundMesh, float4x4.Identity, JungleGroundTint * 0.9f, Material.RedDirt));
        var floor = new HeightField(moundMesh.Positions, 0f);
        float Ground(float x, float z) => floor.HeightAt(x, z);

        // Keep the camp, the shack and the walkway clear of plants.
        bool Clear(float x, float z) =>
            InClearing(x, z, 0.98f)
            || (x > -4f && x < 4f && z > -1.5f && z < 5.5f)                      // the shack
            || (x < -15f && x > -42f && float.Abs(z + 14f) < 1.6f);              // the walkway
        float Rand(float a, float b) => a + (float)random.NextDouble() * (b - a);

        // The trees modelled in Blender (Tools/blender/build_jungle_trees.py): two giants (a kapok and a strangler fig),
        // two medium trees and two walking palms. Without them, the trees are built here as before.
        JungleTree?[] loaded = [JungleTree.Load("jungle_giant_a"), JungleTree.Load("jungle_giant_b"), JungleTree.Load("jungle_tree_a"),
                                JungleTree.Load("jungle_tree_b"), JungleTree.Load("walking_palm_a"), JungleTree.Load("walking_palm_b")];
        var models = loaded.All(t => t is not null) ? loaded.Select(t => t!).ToArray() : null;
        // The canopy: masses of broadleaf cards (or, without the model, the old blobs).
        var canopy = JungleTree.Load("jungle_canopy");
        _undergrowth = Undergrowth.Load();   // ferns, bananas, palms, saplings, shrubs (or null: the built-in plants)
        var smallCrown = JungleTree.Load("jungle_crown_small");   // the understorey poles' crowns
        // Scale a model so its trunk has the radius asked for (TrunkRadius is 1.25 x the modelled trunk radius).
        float ScaleFor(JungleTree t, float radius) => radius * 1.25f / t.TrunkRadius;

        void Giant(float3 ground, float radius, float height, int bark, bool collide, float maxReach)
        {
            if (models is null)
            {
                AddGiant(m, random, ground, radius, height, bark, collide, maxReach);
                return;
            }
            var tree = models[random.Next(2)];
            PlaceTree(tree, ground, float.Clamp(ScaleFor(tree, radius), 0.65f, 1.25f), Rand(0f, float.Tau), collide);
        }

        // --- Giants: kapok and fig trees with great sinuous plank buttresses. Four stand in the open forest between
        // the clearing and the wall, where you can walk among their roots; more stand beyond the wall, their trunks
        // and crowns showing above it.
        var heroes = new (float X, float Z, float Radius, float Height, int Bark)[]
        {
            (-21f, -26f, 1.35f, 32f, 0), (22f, -9f, 1.2f, 30f, 1), (13f, -33f, 1.0f, 28f, 0), (-21.5f, 3f, 1.1f, 29f, 1),
        };
        foreach (var h in heroes)
            Giant(o + new float3(h.X, Ground(h.X, h.Z), h.Z), h.Radius, h.Height, h.Bark, collide: true, maxReach: 6.5f);
        var giants = heroes.Select(h => (h.X, h.Z)).ToList();
        for (var i = 0; i < 900 && giants.Count < 46; i++)
        {
            var x = Rand(-80f, 80f);
            var z = -12f + Rand(-75f, 75f);
            if (InJungleBounds(x, z, 3f) || giants.Any(g => (g.X - x) * (g.X - x) + (g.Z - z) * (g.Z - z) < 11f * 11f))
                continue;
            giants.Add((x, z));
            Giant(o + new float3(x, Ground(x, z), z), Rand(0.8f, 1.5f), Rand(24f, 34f), random.Next(3), collide: false, maxReach: 8f);
        }

        // --- Medium trees in the open forest: smaller buttresses, mossy or grey trunks, crowns in the canopy.
        var trees = new List<(float X, float Z)>(giants);
        var mediumTarget = giants.Count + 70;
        for (var i = 0; i < 1200 && trees.Count < mediumTarget; i++)
        {
            var x = Rand(-60f, 60f);
            var z = -12f + Rand(-55f, 55f);
            if (InClearing(x, z, 1.12f) || Clear(x, z) || trees.Any(t => (t.X - x) * (t.X - x) + (t.Z - z) * (t.Z - z) < 5f * 5f))
                continue;
            trees.Add((x, z));
            var at = o + new float3(x, Ground(x, z), z);
            var radius = Rand(0.32f, 0.6f);
            if (models is null)
                AddGiant(m, random, at, radius, Rand(14f, 24f), random.Next(3), collide: InJungleBounds(x, z, 0.5f), maxReach: 2.5f);
            else
            {
                var tree = models[2 + random.Next(2)];
                PlaceTree(tree, at, float.Clamp(ScaleFor(tree, radius), 0.75f, 1.3f), Rand(0f, float.Tau), InJungleBounds(x, z, 0.5f));
            }
        }

        // --- Slender poles: the many thin straight trunks of the understorey trees, some mossy, with small crowns.
        var poleTarget = trees.Count + 220;
        for (var i = 0; i < 2500 && trees.Count < poleTarget; i++)
        {
            var x = Rand(-50f, 50f);
            var z = -12f + Rand(-45f, 45f);
            if (InClearing(x, z, 1.04f) || Clear(x, z) || trees.Any(t => (t.X - x) * (t.X - x) + (t.Z - z) * (t.Z - z) < 1.6f * 1.6f))
                continue;
            trees.Add((x, z));
            var r = Rand(0.06f, 0.16f);
            var height = Rand(5f, 16f);
            var foot = o + new float3(x, Ground(x, z) - 0.3f, z);
            var top = foot + new float3(Rand(-0.6f, 0.6f), height, Rand(-0.6f, 0.6f));
            var bark = random.NextDouble() < 0.45 ? m.BarkMossy : (random.NextDouble() < 0.5 ? m.BarkGrey : m.BarkRed);
            AddTrunk(bark, foot, top, r * 1.2f, r * 0.7f, 6);
            var crown = Rand(0.9f, 2.2f);
            if (smallCrown is not null)
                PlaceTree(smallCrown, top, crown * 0.32f, Rand(0f, float.Tau), collide: false);
            else
                for (var c = 0; c < 4; c++)
                    m.Canopy.Blob(top + new float3(Rand(-crown, crown) * 0.6f, Rand(-0.5f, 0.6f), Rand(-crown, crown) * 0.6f), new float3(crown * 0.6f, crown * 0.35f, crown * 0.6f), random, 6, 4);
            if (InJungleBounds(x, z, 0.3f))
                AddCollider(new float3(foot.X, 1.2f, foot.Z), new float3(r * 2.4f, 2.4f, r * 2.4f));
        }

        // --- Walking palms: slim mossy trunks up on cones of stilt roots, crowned with long drooping fronds.
        var placedPalms = 0;
        for (var i = 0; i < 1000 && placedPalms < 70; i++)
        {
            var x = Rand(-50f, 50f);
            var z = -12f + Rand(-46f, 46f);
            if (InClearing(x, z, 1.05f) || Clear(x, z) || trees.Any(t => (t.X - x) * (t.X - x) + (t.Z - z) * (t.Z - z) < 2.5f * 2.5f))
                continue;
            trees.Add((x, z));
            placedPalms++;
            if (models is null)
                AddWalkingPalm(m, random, o + new float3(x, Ground(x, z), z), InJungleBounds(x, z, 0.5f));
            else
                PlaceTree(models[4 + random.Next(2)], o + new float3(x, Ground(x, z), z), Rand(0.8f, 1.2f), Rand(0f, float.Tau), InJungleBounds(x, z, 0.5f));
        }

        // --- Lianas: woody vines hanging from the canopy, swinging down to the ground.
        for (var i = 0; i < 160; i++)
        {
            var (tx, tz) = trees[random.Next(trees.Count)];
            var x = tx + Rand(-3f, 3f);
            var z = tz + Rand(-3f, 3f);
            if (InClearing(x, z, 1.0f))
                continue;
            var top = o + new float3(x, Rand(11f, 20f), z);
            var bottom = o + new float3(x + Rand(-2.5f, 2.5f), Ground(x, z) - 0.1f, z + Rand(-2.5f, 2.5f));
            var sway = new float3(Rand(-1.5f, 1.5f), 0, Rand(-1.5f, 1.5f));
            var points = new List<float3>();
            for (var k = 0; k <= 10; k++)
            {
                var t = k / 10f;
                points.Add(Mix(top, bottom, t) + sway * float.Sin(t * float.Pi) + new float3(0, 0, float.Sin(t * 9f + i) * 0.15f));
            }
            AddTube(m.Lianas, points, Rand(0.03f, 0.07f), 0.03f, 5);
        }

        // --- Bananas round the edge of the clearing: a soft stem and a few huge, broad, drooping leaves each.
        for (var i = 0; i < 60; i++)
        {
            var a = (float)random.NextDouble() * float.Tau;
            var s = Rand(0.97f, 1.27f);
            var x = float.Cos(a) * JungleClearingRadii.X * s;
            var z = -12f + float.Sin(a) * JungleClearingRadii.Y * s;
            if (Clear(x, z))
                continue;
            var height = Rand(2.0f, 3.6f);
            var foot = o + new float3(x, 0, z);
            if (_undergrowth is { } u)
            {
                PlaceTree(Pick(u.Bananas, random), foot, height / 2.4f, Rand(0f, float.Tau), collide: false, hides: true);
                continue;
            }
            AddFoliage(foot + new float3(0, 0.75f * height, 0), new float3(0.6f * height, 0.35f * height, 0.6f * height), BananaCover);
            AddTrunk(m.BarkMossy, foot, foot + new float3(0, height, 0), 0.13f, 0.1f, 6);
            var leaves = 5 + random.Next(3);
            for (var k = 0; k < leaves; k++)
            {
                var la = k * float.Tau / leaves + Rand(0f, 0.5f);
                AddFrond(m.Bananas, foot + new float3(0, height - 0.1f, 0), Heading(la), Rand(1.6f, 2.4f), 0.36f, 0.9f, Rand(1.3f, 1.9f));
            }
        }

        // --- The open forest floor between the clearing and the wall: broad-leaved saplings and herbs, ferns, stemless
        // palms with great feathery fronds, a few shrubs, and dead palm fronds lying about.
        for (var i = 0; i < 1100; i++)
        {
            var x = Rand(-30f, 30f);
            var z = -12f + Rand(-27f, 23f);
            if (Clear(x, z) || (InClearing(x, z, 1.0f) && random.NextDouble() < 0.85))
                continue;
            var at = o + new float3(x, Ground(x, z), z);
            var kind = random.NextDouble();
            if (kind < 0.45)
                BroadLeafPlant(m, random, at, Rand(0.4f, 2.4f));
            else if (kind < 0.62)
                Fern(m.Palms, random, at, Rand(0.6f, 1.3f));
            else if (kind < 0.7)
                GroundPalm(m.Palms, random, at, Rand(2.2f, 4.2f));
            else if (kind < 0.86)
                Shrub(random.NextDouble() < 0.5 ? m.ShrubDark : m.ShrubLight, random, at, Rand(0.5f, 1.6f));
            else
                AddFrond(m.Dead, at + new float3(0, 0.03f, 0), Heading(Rand(0f, float.Tau)), Rand(2f, 3.5f), 0.3f, 0.12f, 0.1f);
        }

        // --- The wall: a dense, layered bank of vegetation all the way round, from ground-level leaves at the front up
        // through shrubs and saplings to the canopy at the back. Its front is a little inside the invisible bounds, so
        // you push into the leaves before you're stopped.
        BuildVegetationWall(m, random, Ground, canopy);

        // --- The canopy over the forest (but not the clearing): it shades the forest floor and closes in the sky.
        // (Fewer, bigger masses of leaf cards than blobs: the cards' overdraw is what they cost.)
        for (var i = 0; i < (canopy is not null ? 340 : 480); i++)
        {
            var x = Rand(-85f, 85f);
            var z = -12f + Rand(-80f, 80f);
            if (InClearing(x, z, 1.2f))
                continue;
            var cr = Rand(4f, 8f);
            if (canopy is not null)
                PlaceTree(canopy, o + new float3(x, Rand(13f, 24f), z), cr * 1.15f / 4f, Rand(0f, float.Tau), collide: false);
            else
                m.Canopy.Blob(o + new float3(x, Rand(13f, 24f), z), new float3(cr, cr * 0.4f, cr), random, 7, 4);
        }

        // The Blender trees and canopy: every instance of a model merged into one mesh per part (bark, vines, leaves).
        foreach (var tree in (models ?? []).Append(canopy).Append(smallCrown).Concat(_undergrowth?.All ?? []))
            if (tree is { Instances.Count: > 0 })
                foreach (var (mesh, material) in Engine.Model.MergeInstances(tree.Model, tree.Instances))
                    _props.Add(new Prop(mesh, float4x4.Identity, float3.One, material.Material, Part: material));

        _props.Add(new Prop(m.BarkGrey.Build(), float4x4.Identity, BarkGrey, Material.Bark));
        _props.Add(new Prop(m.BarkRed.Build(), float4x4.Identity, BarkRed, Material.Bark));
        _props.Add(new Prop(m.BarkMossy.Build(), float4x4.Identity, BarkMossy, Material.Bark));
        _props.Add(new Prop(m.Lianas.Build(), float4x4.Identity, LianaTint, Material.Bark));
        _props.Add(new Prop(m.Canopy.Build(), float4x4.Identity, CanopyGreen, Material.Bush));
        _props.Add(new Prop(m.ShrubDark.Build(), float4x4.Identity, ShrubDark, Material.Bush));
        _props.Add(new Prop(m.ShrubLight.Build(), float4x4.Identity, ShrubLight, Material.Bush));
        _props.Add(new Prop(m.Wall.Build(), float4x4.Identity, WallDark, Material.Bush));
        _props.Add(new Prop(m.WallBack.Build(), float4x4.Identity, WallDark * 0.45f, Material.Bush));   // (deep in the shade behind the leaves)
        _props.Add(new Prop(m.Palms.Build(), float4x4.Identity, PalmGreen, Material.Bush));
        _props.Add(new Prop(m.Bananas.Build(), float4x4.Identity, BananaGreen, Material.Bush));
        _props.Add(new Prop(m.Leaves.Build(), float4x4.Identity, LeafBright, Material.Bush));
        _props.Add(new Prop(m.Dead.Build(), float4x4.Identity, DeadFrond, Material.Bush));
    }

    /// <summary>
    /// The vegetation wall round the whole area: points every 0.55 m round a rounded rectangle (a superellipse), and at
    /// each, rows of plants going outwards, lowest at the front and tallest at the back.
    /// </summary>
    private void BuildVegetationWall(ForestMeshes m, Random random, Func<float, float, float> ground, JungleTree? canopy)
    {
        var o = JungleOrigin;
        float Rand(float a, float b) => a + (float)random.NextDouble() * (b - a);

        // The front line, finely sampled, then walked at an even spacing.
        const int fine = 4000;
        var line = new (float3 P, float3 N)[fine];
        for (var i = 0; i < fine; i++)
        {
            var a = i * float.Tau / fine;
            var c = float.Cos(a);
            var s = float.Sin(a);
            var x = float.Sign(c) * WallHalfX * float.Pow(float.Abs(c), 2f / WallRoundness);
            var z = float.Sign(s) * WallHalfZ * float.Pow(float.Abs(s), 2f / WallRoundness);
            // The outward normal: the superellipse's gradient.
            var gx = float.Sign(x) * float.Pow(float.Abs(x / WallHalfX), WallRoundness - 1) / WallHalfX;
            var gz = float.Sign(z) * float.Pow(float.Abs(z / WallHalfZ), WallRoundness - 1) / WallHalfZ;
            var n = new float3(gx, 0, gz);
            var len = float.Sqrt(float3.Dot(n, n));
            line[i] = (new float3(x, 0, WallCenterZ + z), len > 1e-6f ? n / len : new float3(c, 0, s));
        }

        var travelled = 0f;
        var step = 0;
        for (var i = 0; i < fine; i++)
        {
            var next = line[(i + 1) % fine].P - line[i].P;
            travelled += float.Sqrt(float3.Dot(next, next));
            if (travelled < 0.55f)
                continue;
            travelled = 0f;
            var (p, n) = line[i];
            var along = new float3(-n.Z, 0, n.X);
            // The front wanders in and out a little, so it never reads as a hedge.
            var wobble = float.Sin(i * 0.013f) * 0.6f + float.Sin(i * 0.051f + 1.3f) * 0.4f;
            float3 At(float depth, float side)
            {
                var q = p + n * (depth + wobble) + along * side;
                return o + new float3(q.X, ground(q.X, q.Z), q.Z);
            }

            var onTrail = p.X < -20f && float.Abs(p.Z + 14f) < 1.4f;   // the walkway tunnels in under the leaves

            // Front row: low broad leaves, ferns and herbs spilling out onto the forest floor.
            if (!onTrail)
            {
                var front = At(Rand(-1.6f, -0.4f), Rand(-0.3f, 0.3f));
                var pick = random.NextDouble();
                if (pick < 0.45)
                    BroadLeafPlant(m, random, front, Rand(0.5f, 1.8f));
                else if (pick < 0.7)
                    Fern(m.Palms, random, front, Rand(0.7f, 1.4f));
                else
                    Shrub(m.ShrubLight, random, front, Rand(0.5f, 1.1f));
            }

            // Second row: thick shrubs, broad-leaved saplings and, every few metres, a stemless palm's fan of fronds.
            Shrub(random.NextDouble() < 0.5 ? m.ShrubDark : m.ShrubLight, random, At(Rand(0f, 1.2f), Rand(-0.3f, 0.3f)), Rand(1.2f, 2.4f));
            if (random.NextDouble() < 0.35)
                BroadLeafPlant(m, random, At(Rand(0f, 1f), Rand(-0.3f, 0.3f)), Rand(1.8f, 3.2f));
            if (i % 7 == 0)
                GroundPalm(m.Palms, random, At(Rand(0.6f, 1.8f), 0), Rand(3f, 4.8f));

            // Back rows: a bank of foliage rising from a metre up to the canopy. With the Blender models it's all leaves you
            // can see (shrubs below, canopy leaf masses higher up), and a dark solid bank behind the last row stops you
            // seeing out through the gaps; without them it's solid blobs as before.
            // (The rows behind the first are half as many, each plant bigger: the leaf cards' overdraw is what they cost.)
            step++;
            for (var row = 0; row < 4; row++)
            {
                var sparse = row > 0 && _undergrowth is not null;
                if (sparse && (step + row) % 2 != 0)
                    continue;
                var depth = 2.2f + row * 1.9f + Rand(-0.5f, 0.5f);
                var baseY = 1.0f + row * 2.2f;
                var stacks = 2 + row / 2;
                for (var k = 0; k < stacks; k++)
                {
                    var c = At(depth, Rand(-0.4f, 0.4f));
                    var r = (Rand(1.0f, 1.7f) + row * 0.35f) * (sparse ? 1.3f : 1f);
                    var y = baseY + k * r * 1.1f;
                    if (canopy is not null && row >= 2 && k == stacks - 1)
                        PlaceTree(canopy, c + new float3(0, y, 0), r * 1.4f / 4f, Rand(0f, float.Tau), collide: false, hides: true);
                    else if (_undergrowth is not null)
                        Shrub(m.Wall, random, c + new float3(0, y - r * 0.9f, 0), r * 1.9f);
                    else
                    {
                        m.Wall.Blob(c + new float3(0, y, 0), new float3(r, r * 0.9f, r), random, row < 2 ? 6 : 5, row < 2 ? 4 : 3);
                        AddFoliage(c + new float3(0, y, 0), new float3(r, r * 0.9f, r), WallCover);
                    }
                }
            }
            if (_undergrowth is not null && i % 2 == 0)
            {
                var back = At(11.5f + Rand(-0.5f, 0.5f), 0);
                var r = Rand(2.6f, 3.2f);
                for (var k = 0; k < 3; k++)
                    m.WallBack.Blob(back + new float3(0, r * 0.6f + k * r * 1.3f, 0), new float3(r, r * 0.9f, r), random, 5, 3);
            }

            // A slender trunk standing in the bank now and then, and a vine across its face.
            if (i % 5 == 0)
            {
                var foot = At(Rand(1f, 5f), Rand(-0.3f, 0.3f)) - new float3(0, 0.3f, 0);
                AddTrunk(random.NextDouble() < 0.5 ? m.BarkMossy : m.BarkGrey, foot, foot + new float3(Rand(-0.5f, 0.5f), Rand(8f, 15f), Rand(-0.5f, 0.5f)), Rand(0.08f, 0.2f), 0.06f, 6);
            }
            if (i % 11 == 0)
            {
                var top = At(Rand(0.5f, 2f), 0) + new float3(0, Rand(6f, 10f), 0);
                var bottom = At(Rand(-0.5f, 0.5f), Rand(-2f, 2f));
                var points = new List<float3>();
                for (var k = 0; k <= 6; k++)
                {
                    var t = k / 6f;
                    points.Add(Mix(top, bottom, t) - n * (float.Sin(t * float.Pi) * 0.6f));
                }
                AddTube(m.Lianas, points, 0.04f, 0.03f, 5);
            }
        }
    }

    /// <summary>
    /// A rainforest giant: a tall trunk flaring at its foot into sinuous plank buttresses that snake out across the
    /// ground as surface roots, and branches up to a broad crown in the canopy.
    /// </summary>
    private void AddGiant(ForestMeshes m, Random random, float3 ground, float radius, float height, int bark, bool collide, float maxReach)
    {
        float Rand(float a, float b) => a + (float)random.NextDouble() * (b - a);
        var trunkMesh = bark == 0 ? m.BarkGrey : bark == 1 ? m.BarkRed : m.BarkMossy;
        var foot = ground - new float3(0, 0.6f, 0);
        var lean = new float3(Rand(-0.8f, 0.8f), 0, Rand(-0.8f, 0.8f));
        var p1 = ground + new float3(0, height * 0.22f, 0) + lean * 0.2f;
        var p2 = ground + new float3(0, height * 0.65f, 0) + lean * 0.65f;
        var top = ground + new float3(0, height, 0) + lean;
        AddTrunk(trunkMesh, foot, p1, radius, radius * 0.66f, 14);
        AddTrunk(trunkMesh, p1, p2, radius * 0.66f, radius * 0.5f, 12);
        AddTrunk(trunkMesh, p2, top, radius * 0.5f, radius * 0.36f, 10);
        if (collide)
            AddCollider(new float3(ground.X, ground.Y + 2f, ground.Z), new float3(radius * 1.7f, 4f, radius * 1.7f));

        // The buttresses: spread round the trunk, each its own height, reach and wiggle.
        var count = radius > 0.7f ? 6 + random.Next(4) : 3 + random.Next(3);
        var start = Rand(0f, float.Tau);
        for (var k = 0; k < count; k++)
        {
            var a = start + k * float.Tau / count + Rand(-0.25f, 0.25f);
            var dir = Heading(a);
            var reach = float.Min(maxReach, radius * Rand(3.2f, 5.5f));
            var rise = radius * Rand(2.4f, 4.2f);
            var thick = float.Max(0.12f, radius * Rand(0.2f, 0.3f));
            var end = AddButtress(trunkMesh, ground, dir, radius * 0.5f, reach, rise, thick, Rand(0.3f, 1.0f) * reach * 0.25f, Rand(0f, float.Tau), collide);
            // The root carries on along the ground, half buried, winding away.
            var points = new List<float3> { end.P };
            var heading = end.Dir;
            var at = end.P;
            var steps = 4 + random.Next(4);
            for (var s = 0; s < steps; s++)
            {
                heading = float3.Normalize(heading + new float3(-heading.Z, 0, heading.X) * Rand(-0.5f, 0.5f));
                at += heading * Rand(0.5f, 0.9f);
                at = new float3(at.X, ground.Y + 0.02f, at.Z);
                points.Add(at);
            }
            AddTube(trunkMesh, points, thick * 0.6f, thick * 0.2f, 6);
        }

        // Branches out to a broad, flat-topped crown in the canopy.
        var crown = radius * Rand(5f, 7f) + 3f;
        var branches = 4 + random.Next(3);
        for (var k = 0; k < branches; k++)
        {
            var a = k * float.Tau / branches + Rand(-0.3f, 0.3f);
            var tip = top + new float3(float.Cos(a) * crown * 0.75f, Rand(-1f, 2f), float.Sin(a) * crown * 0.75f);
            AddTrunk(trunkMesh, top - new float3(0, height * 0.12f, 0), tip, radius * 0.25f, radius * 0.1f, 6);
        }
        var clumps = 8 + random.Next(6);
        for (var c = 0; c < clumps; c++)
        {
            var a = Rand(0f, float.Tau);
            var d = float.Sqrt((float)random.NextDouble()) * crown;
            var cr = crown * Rand(0.35f, 0.6f);
            m.Canopy.Blob(top + new float3(float.Cos(a) * d, Rand(-0.5f, 2.5f), float.Sin(a) * d), new float3(cr, cr * 0.4f, cr), random, 8, 5);
        }
        // A couple of lianas hanging off the lower branches.
        for (var k = 0; k < 2; k++)
        {
            var a = Rand(0f, float.Tau);
            var hang = top + new float3(float.Cos(a) * crown * 0.5f, -Rand(1f, 3f), float.Sin(a) * crown * 0.5f);
            var low = new float3(hang.X + Rand(-1f, 1f), ground.Y - 0.1f, hang.Z + Rand(-1f, 1f));
            var points = new List<float3>();
            for (var s = 0; s <= 8; s++)
            {
                var t = s / 8f;
                points.Add(Mix(hang, low, t) + new float3(float.Sin(t * 5f + a) * 0.3f, 0, float.Cos(t * 4f + a) * 0.3f));
            }
            AddTube(m.Lianas, points, 0.06f, 0.04f, 5);
        }
    }

    /// <summary>
    /// A plank buttress: a thin, tall fin running out from the trunk along a winding path, its top falling in a concave
    /// curve from high up the trunk to the ground. Returns where it ends and which way it was heading.
    /// </summary>
    private (float3 P, float3 Dir) AddButtress(MeshBuilder b, float3 ground, float3 dir, float startOffset, float reach, float height,
        float thick, float wiggle, float phase, bool collide)
    {
        const int segments = 16;
        var side0 = float3.Normalize(float3.Cross(float3.UnitY, dir));
        var bottomY = ground.Y - 0.5f;
        var pts = new float3[segments + 1];
        var tops = new float[segments + 1];
        var halves = new float[segments + 1];
        for (var k = 0; k <= segments; k++)
        {
            var t = k / (float)segments;
            pts[k] = ground + dir * (startOffset + reach * t) + side0 * (wiggle * float.Sin(t * 3.4f + phase) * t);
            tops[k] = ground.Y + 0.1f + height * float.Pow(1f - t, 2.1f);
            halves[k] = thick * (1f - 0.55f * t) / 2f;
        }
        var perps = new float3[segments + 1];
        for (var k = 0; k <= segments; k++)
        {
            var d = pts[int.Min(k + 1, segments)] - pts[int.Max(k - 1, 0)];
            perps[k] = float3.Normalize(float3.Cross(float3.UnitY, new float3(d.X, 0, d.Z)));
        }

        for (var k = 0; k < segments; k++)
        {
            var mid = float3.Normalize(perps[k] + perps[k + 1]);
            foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
            {
                var a0 = pts[k] + perps[k] * (halves[k] * s);
                var a1 = pts[k + 1] + perps[k + 1] * (halves[k + 1] * s);
                b.Quad(new float3(a0.X, bottomY, a0.Z), new float3(a1.X, bottomY, a1.Z), new float3(a1.X, tops[k + 1], a1.Z), new float3(a0.X, tops[k], a0.Z), mid * s);
                // The rounded top edge: a bevel from each face up to a ridge along the middle.
                var r0 = new float3(pts[k].X, tops[k] + halves[k], pts[k].Z);
                var r1 = new float3(pts[k + 1].X, tops[k + 1] + halves[k + 1], pts[k + 1].Z);
                b.Quad(new float3(a0.X, tops[k], a0.Z), new float3(a1.X, tops[k + 1], a1.Z), r1, r0, float3.Normalize(mid * s + float3.UnitY));
            }

            if (collide && tops[k] - ground.Y > 0.35f)
            {
                // The fin stops the player: a box round each piece of it.
                var pad = new float3(halves[k] + 0.03f, 0, halves[k] + 0.03f);
                var lo = float3.Min(pts[k], pts[k + 1]) - pad;
                var hi = float3.Max(pts[k], pts[k + 1]) + pad;
                AddStatic(new Aabb(new float3(lo.X, ground.Y, lo.Z), new float3(hi.X, ground.Y + float.Min(tops[k] - ground.Y, 2.5f), hi.Z)), default, visible: false);
            }
        }

        var last = pts[segments] - pts[segments - 1];
        return (new float3(pts[segments].X, ground.Y + 0.02f, pts[segments].Z), float3.Normalize(new float3(last.X, 0, last.Z)));
    }

    /// <summary>
    /// A walking palm (Socratea): a slim, mossy trunk standing on a cone of stilt roots that spread from a metre or two
    /// up the trunk to the ground, and a crown of long fronds whose leaflets hang down.
    /// </summary>
    private void AddWalkingPalm(ForestMeshes m, Random random, float3 ground, bool collide)
    {
        float Rand(float a, float b) => a + (float)random.NextDouble() * (b - a);
        var rootTop = Rand(1.2f, 2.6f);
        var spread = 0.45f + rootTop * Rand(0.35f, 0.55f);
        var height = Rand(9f, 18f);
        var lean = new float3(Rand(-1f, 1f), 0, Rand(-1f, 1f));
        var trunkFoot = ground + new float3(0, rootTop - 0.3f, 0);
        var top = ground + new float3(0, height, 0) + lean;
        AddTrunk(m.BarkMossy, trunkFoot, top, 0.13f, 0.11f, 8);
        var roots = 9 + random.Next(7);
        for (var k = 0; k < roots; k++)
        {
            var a = k * float.Tau / roots + Rand(-0.2f, 0.2f);
            var dir = Heading(a);
            var from = ground + new float3(0, rootTop * Rand(0.55f, 1f), 0) + dir * 0.08f;
            var to = ground + dir * (spread * Rand(0.75f, 1.15f)) - new float3(0, 0.15f, 0);
            var bow = (from + to) * 0.5f + dir * Rand(0.05f, 0.2f) + new float3(0, 0.1f, 0);
            AddTube(m.BarkMossy, new[] { from, bow, to }, 0.045f, 0.04f, 5);
        }
        var fronds = 8 + random.Next(4);
        for (var k = 0; k < fronds; k++)
        {
            var a = k * float.Tau / fronds + Rand(-0.2f, 0.2f);
            AddPinnateFrond(m.Palms, top, Heading(a), Rand(2.8f, 4.2f), Rand(0.6f, 1.3f), Rand(1.6f, 2.8f), Rand(0.6f, 0.9f), 0.9f);
        }
        if (collide)
            AddCollider(new float3(ground.X, ground.Y + 1f, ground.Z), new float3(spread * 1.3f, 2f, spread * 1.3f));
    }

    /// <summary>A broad-leaved sapling or herb: a thin stem with big oval leaves held out at intervals up it.</summary>
    private static void AddBroadLeafPlant(ForestMeshes m, Random random, float3 ground, float height)
    {
        float Rand(float a, float b) => a + (float)random.NextDouble() * (b - a);
        var top = ground + new float3(Rand(-0.2f, 0.2f), height, Rand(-0.2f, 0.2f));
        AddTrunk(m.Leaves, ground - new float3(0, 0.05f, 0), top, 0.02f + height * 0.008f, 0.012f, 4);   // (green, and shots pass through)
        var leaves = 4 + (int)(height * 3f);
        var size = 0.25f + height * 0.12f;
        for (var k = 0; k < leaves; k++)
        {
            var t = 0.3f + 0.7f * k / leaves;
            var at = Mix(ground, top, t);
            AddFrond(m.Leaves, at, Heading(k * 2.4f + Rand(-0.3f, 0.3f)), size * Rand(0.8f, 1.3f), size * 0.42f, size * 0.25f, size * Rand(0.4f, 0.8f), 4);
        }
    }

    /// <summary>A fern: a rosette of feathery fronds arching out from the ground.</summary>
    private static void AddFern(MeshBuilder b, Random random, float3 ground, float size)
    {
        var n = 6 + random.Next(4);
        for (var k = 0; k < n; k++)
        {
            var a = k * float.Tau / n + (float)random.NextDouble() * 0.4f;
            AddPinnateFrond(b, ground + new float3(0, 0.05f, 0), Heading(a), size * (0.8f + 0.4f * (float)random.NextDouble()), size * 0.6f, size * 0.7f, size * 0.25f, 0.5f, 9);
        }
    }

    /// <summary>A stemless palm: a great fan of feathery fronds rising straight out of the ground.</summary>
    private static void AddGroundPalm(MeshBuilder b, Random random, float3 ground, float size)
    {
        var n = 6 + random.Next(4);
        for (var k = 0; k < n; k++)
        {
            var a = k * float.Tau / n + (float)random.NextDouble() * 0.5f;
            AddPinnateFrond(b, ground + new float3(0, 0.1f, 0), Heading(a) * 0.55f, size, size * (0.9f + 0.4f * (float)random.NextDouble()), size * 0.55f, size * 0.22f, 0.7f, 14);
        }
    }

    /// <summary>A shrub: a cluster of lumpy foliage clumps.</summary>
    private static void AddShrub(MeshBuilder b, Random random, float3 ground, float size)
    {
        var clumps = 4 + random.Next(4);
        for (var c = 0; c < clumps; c++)
        {
            var a = (float)random.NextDouble() * float.Tau;
            var spread = float.Sqrt((float)random.NextDouble()) * size * 0.6f;
            var r = size * (0.3f + 0.2f * (float)random.NextDouble());
            var h = size * (0.3f + 0.7f * (float)random.NextDouble());
            b.Blob(ground + new float3(float.Cos(a) * spread, h, float.Sin(a) * spread), new float3(r, r * 0.85f, r), random, 6, 4);
        }
    }

    private static float3 Mix(float3 a, float3 b, float t) => a + (b - a) * t;

    /// <summary>The horizontal unit vector at <paramref name="angle"/> radians.</summary>
    private static float3 Heading(float angle) => new(float.Cos(angle), 0, float.Sin(angle));

    /// <summary>A tube along <paramref name="points"/>, its radius tapering from <paramref name="r0"/> to <paramref name="r1"/>.</summary>
    private static void AddTube(MeshBuilder b, IReadOnlyList<float3> points, float r0, float r1, int sides)
    {
        for (var k = 0; k + 1 < points.Count; k++)
        {
            var ta = k / (float)(points.Count - 1);
            var tb = (k + 1) / (float)(points.Count - 1);
            var d = points[k + 1] - points[k];
            if (float3.Dot(d, d) < 1e-8f)
                continue;
            // Each piece runs a little past the next point, so the joints don't show gaps.
            AddTrunk(b, points[k], points[k + 1] + d * 0.04f, r0 + (r1 - r0) * ta, r0 + (r1 - r0) * tb, sides);
        }
    }

    /// <summary>
    /// A feathery (pinnate) frond: a midrib arching out along <paramref name="dir"/> (horizontal; shorter than unit
    /// makes it steeper) with narrow leaflets either side along its length, each angled forward and hanging down by
    /// <paramref name="hang"/>; two-sided.
    /// </summary>
    private static void AddPinnateFrond(MeshBuilder b, float3 at, float3 dir, float length, float rise, float droop, float leaflet, float hang, int pairs = 16)
    {
        var flat = float3.Normalize(new float3(dir.X, 0, dir.Z));
        var side = float3.Normalize(float3.Cross(float3.UnitY, flat));
        var reach = float.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
        float3 Mid(float t) => at + flat * (length * reach * t) + new float3(0, rise * t * (2f - reach) - droop * t * t, 0);
        // The midrib.
        for (var k = 0; k < 6; k++)
        {
            var p0 = Mid(k / 6f);
            var p1 = Mid((k + 1) / 6f);
            b.Quad(p0 - side * 0.015f, p1 - side * 0.01f, p1 + side * 0.01f, p0 + side * 0.015f, float3.UnitY);
            b.Quad(p0 - side * 0.015f, p1 - side * 0.01f, p1 + side * 0.01f, p0 + side * 0.015f, -float3.UnitY);
        }
        for (var k = 1; k <= pairs; k++)
        {
            var t = k / (float)(pairs + 1);
            var p = Mid(t);
            var forward = float3.Normalize(Mid(float.Min(t + 0.05f, 1f)) - p);
            var len = leaflet * float.Sin(float.Pi * (0.15f + 0.85f * t));
            var across = forward * 0.035f;
            foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
            {
                var outward = float3.Normalize(side * (s * 0.8f) + forward * 0.45f - float3.UnitY * (hang * (0.4f + 0.6f * t)));
                var tip = p + outward * len;
                var n = float3.Cross(outward, across);
                var nl = float.Sqrt(float3.Dot(n, n));
                n = nl > 1e-6f ? n / nl : float3.UnitY;
                b.Quad(p - across, tip - across * 0.3f, tip + across * 0.3f, p + across, n);
                b.Quad(p - across, tip - across * 0.3f, tip + across * 0.3f, p + across, -n);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Small props and mesh pieces.
    // ------------------------------------------------------------------------------------------------

    private Mesh? _bucketMesh;

    /// <summary>A rough round pole set in the ground (it blocks the player).</summary>
    private void AddPole(float3 foot, float height, float radius)
    {
        var mesh = new MeshBuilder();
        AddTrunk(mesh, foot, foot + new float3(0, height, 0), radius * 1.1f, radius * 0.9f, 7);
        _props.Add(new Prop(mesh.Build(), float4x4.Identity, PoleTint, Material.Wood));
        AddCollider(foot + new float3(0, height / 2, 0), new float3(radius * 2, height, radius * 2));
    }

    /// <summary>A plastic bucket (or basin) standing on <paramref name="ground"/>.</summary>
    private void AddBucket(float3 ground, float3 tint, float radius, float height)
    {
        _bucketMesh ??= new MeshBuilder().Cylinder(1f, -0.5f, 0.5f, 18).Build();
        var model = float4x4.CreateScale(radius, radius, height) * float4x4.CreateRotationX(-float.Pi / 2) * float4x4.CreateTranslation(ground + new float3(0, height / 2, 0));
        _props.Add(new Prop(_bucketMesh, model, tint, Material.Plain));
        // The rim, a shade darker, and the dark water (or slurry of leaf) inside.
        Add(ground + new float3(0, height - 0.005f, 0), new float3(radius * 1.4f, 0.012f, radius * 1.4f), tint * 0.3f, Material.Plain, blocksShots: false, collides: false);
    }

    /// <summary>A plastic jerrycan standing on <paramref name="ground"/>, turned by <paramref name="yaw"/>.</summary>
    private void AddJerrycan(float3 ground, float3 tint, float yaw)
    {
        var turn = float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(ground);
        _props.Add(new Prop(UnitBox, float4x4.CreateScale(0.34f, 0.42f, 0.17f) * float4x4.CreateTranslation(0, 0.21f, 0) * turn, tint, Material.Plain));
        _props.Add(new Prop(UnitBox, float4x4.CreateScale(0.16f, 0.05f, 0.05f) * float4x4.CreateTranslation(-0.04f, 0.46f, 0) * turn, tint * 0.9f, Material.Plain));
        _props.Add(new Prop(UnitBox, float4x4.CreateScale(0.06f, 0.06f, 0.06f) * float4x4.CreateTranslation(0.12f, 0.45f, 0) * turn, tint * 0.6f, Material.Plain));
    }

    /// <summary>A full woven plastic sack lying on <paramref name="ground"/>.</summary>
    private void AddSack(float3 ground, float yaw)
    {
        var model = float4x4.CreateScale(0.6f, 0.28f, 0.4f) * float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(ground + new float3(0, 0.14f, 0));
        _props.Add(new Prop(SackMesh, model, new float3(0.80f, 0.78f, 0.70f), Material.Cloth));
    }

    private Mesh? _sackMesh;

    /// <summary>A plump, slightly lumpy unit cushion (a squashed sphere), for sacks.</summary>
    private Mesh SackMesh => _sackMesh ??= new MeshBuilder().Blob(float3.Zero, new float3(0.5f, 0.5f, 0.5f), new Random(5), 10, 6).Build();

    /// <summary>A thin straight cable between two points.</summary>
    private void AddCable(float3 a, float3 b, float3 tint)
    {
        var mesh = new MeshBuilder();
        AddTrunk(mesh, a, b, 0.008f, 0.008f, 4);
        _props.Add(new Prop(mesh.Build(), float4x4.Identity, tint, Material.Plain));
    }

    /// <summary>A tapered prism from <paramref name="foot"/> to <paramref name="top"/> (a trunk, a pole, a stem).</summary>
    private static void AddTrunk(MeshBuilder b, float3 foot, float3 top, float footRadius, float topRadius, int sides)
    {
        var axis = float3.Normalize(top - foot);
        var side = float3.Normalize(float3.Cross(axis, float.Abs(axis.Y) > 0.9f ? float3.UnitX : float3.UnitY));
        var other = float3.Cross(side, axis);
        for (var i = 0; i < sides; i++)
        {
            var a0 = float.Tau * i / sides;
            var a1 = float.Tau * (i + 1) / sides;
            var d0 = side * float.Cos(a0) + other * float.Sin(a0);
            var d1 = side * float.Cos(a1) + other * float.Sin(a1);
            b.Quad(foot + d0 * footRadius, foot + d1 * footRadius, top + d1 * topRadius, top + d0 * topRadius, float3.Normalize(d0 + d1));
        }
    }

    /// <summary>
    /// A leaf or frond: a two-sided strip arching out from <paramref name="at"/> along <paramref name="dir"/> (horizontal),
    /// rising by <paramref name="rise"/> then drooping by <paramref name="droop"/> over its <paramref name="length"/>,
    /// widest in the middle and folded a little along its midrib.
    /// </summary>
    private static void AddFrond(MeshBuilder b, float3 at, float3 dir, float length, float width, float rise, float droop, int segments = 6)
    {
        var side = float3.Normalize(float3.Cross(float3.UnitY, dir));
        float3 Mid(float t) => at + dir * (length * t) + new float3(0, rise * t - droop * t * t, 0);
        float Half(float t) => width * float.Sin(float.Pi * (0.08f + 0.92f * t)) * (t < 0.08f ? t / 0.08f : 1f);
        for (var k = 0; k < segments; k++)
        {
            var t0 = k / (float)segments;
            var t1 = (k + 1) / (float)segments;
            var p0 = Mid(t0);
            var p1 = Mid(t1);
            var w0 = Half(t0);
            var w1 = Half(t1);
            var fold0 = new float3(0, w0 * 0.3f, 0);
            var fold1 = new float3(0, w1 * 0.3f, 0);
            // Each half of the leaf, either side of the midrib, both faces.
            foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
            {
                var e0 = p0 + side * (w0 * s) + fold0;
                var e1 = p1 + side * (w1 * s) + fold1;
                var n = float3.Cross(p1 - p0, e0 - p0);
                var len = float.Sqrt(float3.Dot(n, n));
                n = len > 1e-6f ? n / len : float3.UnitY;
                if (n.Y < 0)
                    n = -n;
                b.Quad(p0, p1, e1, e0, n);
                b.Quad(p0, p1, e1, e0, -n);
            }
        }
    }
}
