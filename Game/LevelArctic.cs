using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The Arctic: a whiteout on a high snowfield. The white portal is in the wall of an old timber survey hut, half
/// drifted over; a line of flagged bamboo wands leads off north towards a slope that disappears into the blowing snow.
/// There's no sun: everything is lit by the same flat white glare, and the shader's fog and drifting snow swallow
/// everything more than a few tens of metres away.
/// </summary>
public sealed partial class Level
{
    /// <summary>Where the Arctic is (the shader's area 4).</summary>
    public static readonly float3 ArcticOrigin = new(7000, 0, 0);

    // The hut: 5.2 x 4.2 m, its door wall (with the portal) facing north.
    private static readonly float3 HutMin = ArcticOrigin + new float3(-2.6f, 0, 0);
    private static readonly float3 HutMax = ArcticOrigin + new float3(2.6f, 2.7f, 4.2f);
    private const float HutWall = 0.25f;

    /// <summary>The white portal: on the hut's north wall, facing out into the snow.</summary>
    public static float3 ArcticPortalFloorPoint => ArcticOrigin;
    public static float3 ArcticPortalNormal => new(0, 0, -1);

    /// <summary>The colour of the whiteout (linear): sky, fog and blowing snow.</summary>
    public static readonly float3 WhiteoutColor = new(0.74f, 0.78f, 0.83f);

    /// <summary>The same at night: blowing snow in the dark, just a blue-grey glow.</summary>
    public static readonly float3 WhiteoutNightColor = new(0.05f, 0.065f, 0.10f);

    private static readonly float3 SnowTint = new(0.86f, 0.89f, 0.93f);
    private static readonly float3 RockTint = new(0.30f, 0.30f, 0.31f);
    private static readonly float3 HutTimber = new(0.40f, 0.33f, 0.26f);

    private void BuildArctic()
    {
        var o = ArcticOrigin;
        var random = new Random(77);
        AddMinMax(o + new float3(-600, -1, -600), o + new float3(600, 0, 600), SnowTint, Material.Snow);

        // --- The hut: timber walls under a pitched roof, its windows boarded up, a stove pipe ---
        var t = HutWall;
        AddMinMax(HutMin, new float3(HutMax.X, HutMax.Y, HutMin.Z + t), HutTimber, Material.Wood);                    // north (portal)
        AddMinMax(new float3(HutMin.X, 0, HutMax.Z - t), HutMax, HutTimber, Material.Wood);                            // south
        AddMinMax(HutMin, new float3(HutMin.X + t, HutMax.Y, HutMax.Z), HutTimber, Material.Wood);                     // west
        AddMinMax(new float3(HutMax.X - t, 0, HutMin.Z), HutMax, HutTimber, Material.Wood);                            // east
        AddMinMax(new float3(HutMin.X, HutMax.Y - 0.3f, HutMin.Z), new float3(HutMax.X, HutMax.Y, HutMax.Z), HutTimber * 0.9f, Material.Wood); // ceiling
        // Gable ends and the two roof slopes (running east-west, so the gables face east and west).
        var ridgeY = HutMax.Y + 1.0f;
        var midZ = (HutMin.Z + HutMax.Z) / 2;
        var halfDepth = (HutMax.Z - HutMin.Z) / 2 + 0.35f;
        var slope = float.Atan2(1.0f, halfDepth - 0.35f);
        var slab = float.Sqrt(1.0f + (halfDepth - 0.35f) * (halfDepth - 0.35f)) + 0.45f;
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var roof = float4x4.CreateScale(HutMax.X - HutMin.X + 0.6f, 0.12f, slab)
                * float4x4.CreateRotationX(side * slope)
                * float4x4.CreateTranslation(new float3(o.X, (HutMax.Y + ridgeY) / 2 + 0.06f, midZ + side * (halfDepth - 0.35f) / 2));
            _props.Add(new Prop(UnitBox, roof, new float3(0.36f, 0.36f, 0.37f), Material.Galvanized));
        }
        for (var i = 0; i < 4; i++)
        {
            // Stepped boards fill the gable triangles.
            var w = (halfDepth - 0.35f) * 2 * (1 - (i + 0.5f) / 4);
            foreach (var x in (ReadOnlySpan<float>)[HutMin.X + 0.06f, HutMax.X - 0.06f])
                Add(new float3(x, HutMax.Y + (i + 0.5f) * 0.25f, midZ), new float3(0.12f, 0.25f, w), HutTimber * 0.85f, Material.Wood, collides: false);
        }
        // Boarded window on each side wall, corner posts, the stove pipe.
        foreach (var x in (ReadOnlySpan<float>)[HutMin.X - 0.03f, HutMax.X + 0.03f])
        {
            Add(new float3(x, 1.5f, midZ), new float3(0.06f, 0.8f, 1.0f), HutTimber * 0.6f, Material.Wood, collides: false);
            for (var k = 0; k < 3; k++)
                Add(new float3(x + float.Sign(x - o.X) * 0.03f, 1.25f + k * 0.25f, midZ), new float3(0.04f, 0.16f, 1.15f), HutTimber * 1.15f, Material.Wood, collides: false);
        }
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(HutMin.X, HutMin.Z), (HutMax.X, HutMin.Z), (HutMin.X, HutMax.Z), (HutMax.X, HutMax.Z)])
            Add(new float3(x, HutMax.Y / 2, z), new float3(0.22f, HutMax.Y, 0.22f), HutTimber * 0.75f, Material.Wood, collides: false);
        Add(new float3(HutMax.X - 1.0f, ridgeY + 0.4f, midZ + 0.8f), new float3(0.16f, 1.6f, 0.16f), new float3(0.18f, 0.17f, 0.16f), Material.RustyMetal, collides: false);
        Add(new float3(HutMax.X - 1.0f, ridgeY + 1.25f, midZ + 0.8f), new float3(0.3f, 0.06f, 0.3f), new float3(0.18f, 0.17f, 0.16f), Material.RustyMetal, collides: false);

        // An instrument mast beside the hut: a lattice pole with a wind vane and cups up top.
        var mast = o + new float3(4.6f, 0, 2.0f);
        Add(mast + new float3(0, 2.5f, 0), new float3(0.08f, 5f, 0.08f), new float3(0.55f, 0.55f, 0.53f), Material.Galvanized);
        Add(mast + new float3(0, 4.9f, 0), new float3(0.9f, 0.04f, 0.04f), new float3(0.55f, 0.55f, 0.53f), Material.Galvanized, collides: false);
        Add(mast + new float3(0.45f, 5.05f, 0), new float3(0.03f, 0.3f, 0.03f), new float3(0.55f, 0.55f, 0.53f), Material.Galvanized, collides: false);
        Add(mast + new float3(0.45f, 5.22f, 0), new float3(0.28f, 0.05f, 0.28f), new float3(0.75f, 0.2f, 0.12f), Material.PaintedMetal, collides: false);
        Add(mast + new float3(-0.45f, 5.1f, 0), new float3(0.5f, 0.18f, 0.02f), new float3(0.75f, 0.2f, 0.12f), Material.PaintedMetal, collides: false);
        Add(mast + new float3(0, 1.6f, 0.08f), new float3(0.35f, 0.45f, 0.2f), new float3(0.82f, 0.82f, 0.8f), Material.PaintedMetal, collides: false);

        // Drums, crates and an old sledge round the hut, half buried.
        foreach (var (x, z, tint) in (ReadOnlySpan<(float, float, float3)>)[
            (-3.6f, 1.0f, new(0.62f, 0.16f, 0.12f)), (-4.3f, 1.5f, new(0.62f, 0.16f, 0.12f)), (-3.8f, 2.3f, new(0.16f, 0.26f, 0.46f)),
            (3.6f, -1.2f, new(0.72f, 0.58f, 0.14f)), (-4.0f, 3.4f, new(0.62f, 0.16f, 0.12f))])
            AddDrum(o + new float3(x, -0.3f, z), tint, x * 1.7f, Material.RustyMetal);
        AddCrate(o + new float3(3.4f, -0.2f, 3.2f), 0.9f, WoodTint * 0.8f);
        AddCrate(o + new float3(3.5f, 0.7f, 3.3f), 0.6f, WoodTint * 0.65f, 1);
        var sledge = o + new float3(-4.2f, 0, -2.2f);
        foreach (var dz in (ReadOnlySpan<float>)[-0.45f, 0.45f])
            Add(sledge + new float3(0, 0.08f, dz), new float3(2.6f, 0.06f, 0.06f), WoodTint, Material.Wood, collides: false);
        Add(sledge + new float3(0, 0.22f, 0), new float3(2.2f, 0.05f, 1.0f), WoodTint * 0.9f, Material.Wood);
        Add(sledge + new float3(0.1f, 0.45f, 0), new float3(1.4f, 0.4f, 0.8f), new float3(0.25f, 0.35f, 0.22f), Material.Cloth, collides: false);

        // An old container standing in for a store hut further off, drifted up along one side.
        AddContainer(o + new float3(-21f, -0.8f, 6f), alongX: false, new float3(0.72f, 0.36f, 0.12f), false);
        // And a second one beside it, hollow, with the slate portal inside (see BuildArcticContainer).
        BuildArcticContainer();
        // The research station, 50 m north-east, its door and window facing the portal (Game/LevelArcticStation.cs).
        BuildArcticStation();

        // --- Snow: drifts against the hut and everywhere, a slope rising north into the cloud, rocks ---
        var drifts = new MeshBuilder();
        // (Their crests are just off the walls, banked up against the timber with no gully between: the hut's shut,
        // so the half that's inside it is never seen.)
        drifts.Hill(o + new float3(0, -0.6f, HutMax.Z - o.Z + 0.2f), new float3(4.2f, 1.6f, 1.6f), random);     // lee side of the hut
        drifts.Hill(o + new float3(HutMin.X - o.X - 0.2f, -0.5f, 2.2f), new float3(1.3f, 1.1f, 3.0f), random);
        drifts.Hill(o + new float3(-21f, -0.6f, 9.2f), new float3(3.2f, 1.9f, 8.0f), random);  // over the container
        var ownRandom = new Random(78);   // (so the drifts and rocks after these stay where they always were)
        var buried = new MeshBuilder();   // (drifts that would come up inside the second container: made, so the randoms stay in step, and dropped)
        drifts.Hill(o + new float3(ArcticContainerX + 3.0f, -0.45f, 4.5f), new float3(1.6f, 1.1f, 5.5f), ownRandom);  // along the second one's east side
        drifts.Hill(o + new float3(ArcticContainerX, -0.5f, ArcticContainerZ0 - 1.6f), new float3(2.2f, 0.9f, 1.3f), ownRandom);   // against its closed end
        for (var i = 0; i < 40; i++)
        {
            var x = ((float)random.NextDouble() * 2 - 1) * 70f;
            var z = ((float)random.NextDouble() * 2 - 1) * 60f;
            if (float.Abs(x) < 7 && z > -8 && z < 9)
                continue;
            var len = 3f + (float)random.NextDouble() * 8f;
            var inContainer = float.Abs(x - ArcticContainerX) < 1.6f + len * 0.35f && z > ArcticContainerZ0 - len && z < ArcticContainerZ1 + len;
            (inContainer ? buried : drifts).Hill(o + new float3(x, -0.25f, z), new float3(len * 0.35f, 0.3f + (float)random.NextDouble() * 0.35f, len), random);
        }
        // The slope: a broad snow shoulder rising from 40 m north of the hut, with rock bands showing through.
        for (var i = 0; i < 7; i++)
        {
            var x = -120f + i * 40f + (float)random.NextDouble() * 15f;
            var z = -110f - (float)random.NextDouble() * 50f;
            drifts.Hill(o + new float3(x, -12f, z), new float3(60f + (float)random.NextDouble() * 30f, 55f + (float)random.NextDouble() * 25f, 70f), random, 48, 16);
        }
        var driftMesh = drifts.Build();
        _props.Add(new Prop(driftMesh, float4x4.Identity, SnowTint, Material.Snow));
        var snowSurface = new HeightField(driftMesh.Positions, 0f);

        // Rocks sit on the snow wherever they are (on the flat or up on the slope): each one is set down on the
        // lowest point of the snow under it and sunk a little into it, so none hangs in the air, and snow has drifted
        // up round its foot. (In the whiteout the slope behind them is the colour of the sky, so a rock without snow
        // round it looks as if it's floating.)
        var rocks = new MeshBuilder();
        var skirts = new MeshBuilder();
        void Outcrop(float3 at, float size, int pieces)
        {
            for (var k = 0; k < pieces; k++)
            {
                var offset = new float3(((float)random.NextDouble() * 2 - 1) * size, 0, ((float)random.NextDouble() * 2 - 1) * size * 0.7f);
                var r = size * (0.35f + 0.4f * (float)random.NextDouble());
                var radii = new float3(r, r * (0.6f + 0.5f * (float)random.NextDouble()), r * 0.8f);
                var x = at.X + offset.X;
                var z = at.Z + offset.Z;
                var ground = float.MaxValue;
                foreach (var (dx, dz) in (ReadOnlySpan<(float, float)>)[(0, 0), (radii.X * 0.8f, 0), (-radii.X * 0.8f, 0), (0, radii.Z * 0.8f), (0, -radii.Z * 0.8f)])
                    ground = float.Min(ground, snowSurface.HeightAt(x + dx, z + dz));
                rocks.Blob(new float3(x, ground + radii.Y * 0.35f, z), radii, random, 9, 6);
                // The drift round its foot: a low mound set on the lowest snow round its edge, so its rim never
                // stands off the slope (uphill it's buried).
                var rim = float.MaxValue;
                for (var k2 = 0; k2 < 8; k2++)
                {
                    var a = k2 * float.Tau / 8;
                    rim = float.Min(rim, snowSurface.HeightAt(x + float.Cos(a) * radii.X * 1.3f, z + float.Sin(a) * radii.Z * 1.3f));
                }
                skirts.Hill(new float3(x, rim - 0.02f, z), new float3(radii.X * 1.4f, radii.Y * 0.55f + (ground - rim) * 0.6f, radii.Z * 1.45f), random, 18, 6);
            }
        }
        Outcrop(o + new float3(14f, 0, -9f), 2.2f, 5);
        Outcrop(o + new float3(-12f, 0, -20f), 3.0f, 6);
        Outcrop(o + new float3(20f, 0, -36f), 4.0f, 7);
        Outcrop(o + new float3(-30f, 0, -33f), 3.5f, 6);
        Outcrop(o + new float3(-4f, 6f, -62f), 5.0f, 8);   // bands up on the slope
        Outcrop(o + new float3(26f, 9f, -70f), 6.0f, 8);
        Outcrop(o + new float3(-36f, 12f, -78f), 6.0f, 8);
        _props.Add(new Prop(rocks.Build(), float4x4.Identity, RockTint, Material.Rock));
        _props.Add(new Prop(skirts.Build(), float4x4.Identity, SnowTint, Material.Snow));
        foreach (var (x, z, r) in (ReadOnlySpan<(float, float, float)>)[(14f, -9f, 2.2f), (-12f, -20f, 3.0f), (20f, -36f, 4.0f), (-30f, -33f, 3.5f)])
            AddCollider(o + new float3(x, 1.0f, z), new float3(r * 1.6f, 2f, r * 1.2f));

        // --- The route: bamboo wands with little flags every few metres, wandering off north ---
        var pole = new float3(0.58f, 0.48f, 0.28f);
        for (var i = 0; i < 9; i++)
        {
            var z = -5f - i * 6.5f;
            var x = 1.5f + float.Sin(i * 0.7f) * 3f - i * 0.9f;
            var lean = (float)random.NextDouble() * 0.1f;
            var at = o + new float3(x, 0, z);
            Add(at + new float3(lean, 0.85f, 0), new float3(0.035f, 1.7f, 0.035f), pole, Material.Wood, blocksShots: false, collides: false);
            Add(at + new float3(lean + 0.17f, 1.55f, 0), new float3(0.32f, 0.22f, 0.012f), new float3(0.90f, 0.30f, 0.08f), Material.Cloth, blocksShots: false, collides: false);
        }

        // You can only go so far: the slope to the north, and invisible bounds round the snowfield.
        AddStatic(new Aabb(o + new float3(-60, 0, -52), o + new float3(60, 30, -50)), default, visible: false);
        AddStatic(new Aabb(o + new float3(-60, 0, 40), o + new float3(60, 30, 42)), default, visible: false);
        AddStatic(new Aabb(o + new float3(-62, 0, -52), o + new float3(-60, 30, 42)), default, visible: false);
        AddStatic(new Aabb(o + new float3(60, 0, -52), o + new float3(62, 30, 42)), default, visible: false);
    }

    // ------------------------------------------------------------------------------------------------ the second container

    // It stands on the snow beside the store container (local x = -21), running north-south, its closed end to the north
    // (where you come out of the hut) and its doors at the south end, out of sight from there.
    private const float ArcticContainerX = -16f;
    private const float ArcticContainerZ0 = -0.1f;                                      // the closed end's outer face
    private const float ArcticContainerZ1 = ArcticContainerZ0 + ContainerLength;        // the door end
    private const float ArcticContainerVoid = 0.6f;                                     // between the end wall and the false wall

    /// <summary>The false wall the slate portal is cut into: as thin as a container's end, with a gap behind it.</summary>
    public const float ArcticContainerFalseWall = 0.1f;

    /// <summary>The slate portal: on the false wall near the container's closed end, facing its doors.</summary>
    public static float3 ArcticContainerPortalFloorPoint =>
        ArcticOrigin + new float3(ArcticContainerX, 0, ArcticContainerZ0 + EndWallThickness + ArcticContainerVoid + ArcticContainerFalseWall);
    public static float3 ArcticContainerPortalNormal => new(0, 0, 1);

    /// <summary>A dull blue-grey glow: nothing that would draw the eye from outside.</summary>
    public static readonly float3 ArcticContainerPortalColor = new(0.42f, 0.50f, 0.60f);

    /// <summary>The inside, from the false wall to the doors.</summary>
    public static Aabb ArcticContainerInterior => BoxBetween(
        ArcticOrigin + new float3(ArcticContainerX - InnerHalfWidth, 0, ArcticContainerPortalFloorPoint.Z - ArcticOrigin.Z),
        ArcticOrigin + new float3(ArcticContainerX + InnerHalfWidth, InnerHeight, ArcticContainerZ1));

    /// <summary>Its dark: the whiteout's light only gets in at the door that stands open.</summary>
    public static ShadeVolume ArcticContainerShade => new(
        BoxBetween(ArcticOrigin + new float3(ArcticContainerX - InnerHalfWidth - 0.02f, -0.05f, ArcticContainerZ0),
                   ArcticOrigin + new float3(ArcticContainerX + InnerHalfWidth + 0.02f, InnerHeight + 0.02f, ArcticContainerZ1 - 0.04f)),
        ArcticOrigin + new float3(ArcticContainerX + InnerHalfWidth / 2, 1.1f, ArcticContainerZ1 + 0.6f), 3.2f, 0.03f, NoSun: true);

    /// <summary>
    /// An old 40 ft container like the store beside it, weathered the same colour as the stacks in the yard, hollow: thin
    /// steel walls with a grimy inside, a steel floor, and a false wall near the closed end with the slate portal in it.
    /// Its doors are at the south end: the right-hand one shut, the left-hand one swung most of the way open on its
    /// hinges, so you can squeeze in. No light, no marks, nothing on the outside to say there's anything in it.
    /// </summary>
    private void BuildArcticContainer()
    {
        var c = ArcticOrigin + new float3(ArcticContainerX, 0, 0);
        var north = ArcticOrigin.Z + ArcticContainerZ0;
        var south = ArcticOrigin.Z + ArcticContainerZ1;
        var midZ = (north + south) / 2;
        var length = south - north;
        var outer = new float3(0.20f, 0.30f, 0.36f);      // faded petrol blue
        var inside = ContainerInteriorTint * 0.85f;
        const float skin = 0.03f;

        // Floor (visual only: you stand on the snow under it), side walls inner and outer, roof.
        Add(new float3(c.X, -0.045f, midZ), new float3(ContainerWidth, 0.1f, length), new float3(0.26f, 0.26f, 0.26f), Material.Steel, collides: false);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            Add(new float3(c.X + side * (InnerHalfWidth + skin / 2), InnerHeight / 2, midZ), new float3(skin, InnerHeight, length), inside, Material.Corrugated);
            Add(new float3(c.X + side * (ContainerWidth / 2 - skin / 2), ContainerHeight / 2, midZ), new float3(skin, ContainerHeight, length), outer, Material.Corrugated);
        }
        Add(new float3(c.X, InnerHeight + skin / 2, midZ), new float3(InnerHalfWidth * 2, skin, length), inside, Material.Corrugated);
        Add(new float3(c.X, ContainerHeight - 0.04f, midZ), new float3(ContainerWidth, 0.08f, length), outer, Material.Corrugated);

        // The closed end, whole (the portal isn't cut through it), and its door-less outside: corner posts and bars like any other.
        Add(new float3(c.X, InnerHeight / 2, north + EndWallThickness * 0.75f), new float3(InnerHalfWidth * 2, InnerHeight, EndWallThickness / 2), inside, Material.Corrugated);
        Add(new float3(c.X, ContainerHeight / 2, north + EndWallThickness * 0.25f), new float3(ContainerWidth, ContainerHeight, EndWallThickness / 2), outer, Material.Corrugated);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            Add(new float3(c.X + side * (ContainerWidth / 2 - 0.08f), ContainerHeight / 2, north + 0.08f), new float3(0.16f, ContainerHeight, 0.16f), outer * 0.8f, Material.Steel);

        // The false wall the portal is cut into, the same grimy steel as the inside, with the void behind it.
        var falseWall = ArcticContainerPortalFloorPoint.Z - ArcticContainerFalseWall / 2;
        Add(new float3(c.X, InnerHeight / 2, falseWall), new float3(InnerHalfWidth * 2, InnerHeight, ArcticContainerFalseWall), inside * 0.9f, Material.Corrugated);

        // Door end: corner posts, a header and a sill.
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            Add(new float3(c.X + side * (ContainerWidth / 2 - 0.08f), ContainerHeight / 2, south - 0.08f), new float3(0.16f, ContainerHeight, 0.16f), outer * 0.8f, Material.Steel);
        Add(new float3(c.X, InnerHeight + 0.1f, south - 0.08f), new float3(ContainerWidth, ContainerHeight - InnerHeight, 0.16f), outer * 0.8f, Material.Steel);

        // Looking in from the doors, the right-hand door (west, -x) is shut; the left-hand one (east) stands open about 70
        // degrees on its hinge at the east corner, leaving a gap you can get through.
        var doorWidth = InnerHalfWidth;
        Add(new float3(c.X - doorWidth / 2, InnerHeight / 2, south - 0.03f), new float3(doorWidth, InnerHeight, 0.05f), outer, Material.Corrugated);
        AddDoorBars(new float3(c.X - doorWidth / 2, 0, south), alongZ: false, doorWidth, outer);
        var hinge = new float3(c.X + InnerHalfWidth, 0, south);
        var open = float.DegreesToRadians(70f);
        // (Turning about y by +open swings the door's free end, at -x from the hinge, out to +z.)
        var swing = float4x4.CreateTranslation(new float3(-doorWidth / 2, InnerHeight / 2, 0.025f)) * float4x4.CreateRotationY(open) * float4x4.CreateTranslation(hinge);
        _props.Add(new Prop(UnitBox, float4x4.CreateScale(doorWidth, InnerHeight, 0.05f) * swing, outer, Material.Corrugated));
        foreach (var t in (ReadOnlySpan<float>)[-0.22f, 0.22f])
            _props.Add(new Prop(UnitBox, float4x4.CreateScale(0.05f, 2.3f, 0.05f) * float4x4.CreateTranslation(new float3(t * doorWidth, 1.25f - InnerHeight / 2, 0.05f)) * swing, outer * 0.55f, Material.RustyMetal));
        var free = hinge + new float3(-doorWidth * float.Cos(open), 0, doorWidth * float.Sin(open));
        AddStatic(BoxBetween(new float3(float.Min(free.X, hinge.X) - 0.03f, 0, south), new float3(float.Max(free.X, hinge.X) + 0.03f, InnerHeight, free.Z + 0.06f)), default, visible: false);
    }
}

/// <summary>
/// The height of a triangle mesh's top surface (e.g. the snow drifts and slope) at any x, z: the highest triangle
/// above or below the point, or a floor height where there's none. Triangles are bucketed on a grid, so a lookup only
/// tests the few that cover it.
/// </summary>
internal sealed class HeightField
{
    private const float Cell = 4f;
    private readonly float[] _p;
    private readonly float _floor;
    private readonly Dictionary<(int, int), List<int>> _cells = [];

    public HeightField(float[] positions, float floor)
    {
        _p = positions;
        _floor = floor;
        for (var t = 0; t + 8 < positions.Length; t += 9)
        {
            var minX = float.Min(positions[t], float.Min(positions[t + 3], positions[t + 6]));
            var maxX = float.Max(positions[t], float.Max(positions[t + 3], positions[t + 6]));
            var minZ = float.Min(positions[t + 2], float.Min(positions[t + 5], positions[t + 8]));
            var maxZ = float.Max(positions[t + 2], float.Max(positions[t + 5], positions[t + 8]));
            for (var cx = (int)float.Floor(minX / Cell); cx <= (int)float.Floor(maxX / Cell); cx++)
            {
                for (var cz = (int)float.Floor(minZ / Cell); cz <= (int)float.Floor(maxZ / Cell); cz++)
                {
                    if (!_cells.TryGetValue((cx, cz), out var list))
                        _cells[(cx, cz)] = list = [];
                    list.Add(t);
                }
            }
        }
    }

    public float HeightAt(float x, float z)
    {
        var best = _floor;
        if (!_cells.TryGetValue(((int)float.Floor(x / Cell), (int)float.Floor(z / Cell)), out var list))
            return best;
        var p = _p;
        foreach (var t in list)
        {
            float ax = p[t], ay = p[t + 1], az = p[t + 2];
            float bx = p[t + 3], by = p[t + 4], bz = p[t + 5];
            float cx = p[t + 6], cy = p[t + 7], cz = p[t + 8];
            // Barycentric coordinates of (x, z) in the triangle's footprint.
            var d = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
            if (float.Abs(d) < 1e-9f)
                continue;
            var u = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / d;
            var v = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / d;
            var w = 1 - u - v;
            if (u < -1e-4f || v < -1e-4f || w < -1e-4f)
                continue;
            best = float.Max(best, u * ay + v * by + w * cy);
        }

        return best;
    }
}
