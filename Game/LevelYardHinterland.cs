using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The rest of the terminal round the Antwerp yard, for show: the quay goes on north and south along the water for 650 m
/// each way and back east of the yard, covered in blocks of stacks with lanes between them, flood masts over the lanes, and
/// a container ship tied up at each end. None of it is solid or reachable (the yard's bounds stop you at its stacks): it's
/// there so the harbour looks as if it goes on for miles. The nearest blocks use the container model; the rest are plain
/// boxes, a few faces each, merged into one mesh per colour.
/// </summary>
public sealed partial class Level
{
    // All from the yard's origin. The play area's quay is x -28.9 .. 33, z -20 .. 44 (YardQuayX, YardEastX, YardSouthZ, YardNorthZ).
    private const float HinterlandReach = 650f;    // along the water, north and south of the play area
    private const float HinterlandEastX = 900f;    // the ground goes back this far (still in the yard's area)
    private const float HinterlandStacksEastX = 380f;
    private const float ApronX = -6f;              // the open quay along the water, for the cranes and the trucks
    private const float NearModelReach = 70f;      // blocks this near the play area use the container model

    // A block: 4 containers end to end along x, 6 rows across, lanes between blocks.
    private const int BlockLength = 4, BlockRows = 6;
    private const float SlotX = 12.5f, RowZ = 2.6f, LaneX = 14f, LaneZ = 9f;
    private static float BlockSizeX => BlockLength * SlotX;
    private static float BlockSizeZ => BlockRows * RowZ;

    /// <summary>The two ships tied up along our quay: the middle of each (z), and which way the bow points (+1: north).</summary>
    private static readonly (float Z, float Bow)[] QuayShips = [(330f, 1f), (-330f, -1f)];

    private void BuildHinterland()
    {
        var o = YardOrigin;
        var night = TimeOfDay.IsNight(DayNightLevel.Yard);
        var concrete = new float3(0.52f, 0.51f, 0.48f);

        // --- The ground, the quay's face and kerb along the water, bollards for a way ---
        void Slab(float x0, float x1, float z0, float z1) =>
            Add(o + new float3((x0 + x1) / 2, YardSeaLevel / 2, (z0 + z1) / 2), new float3(x1 - x0, -YardSeaLevel, z1 - z0),
                GravelTint, Material.Gravel, blocksShots: false, collides: false);
        Slab(YardQuayX, HinterlandEastX, YardNorthZ, HinterlandReach);
        Slab(YardQuayX, HinterlandEastX, -HinterlandReach, YardSouthZ);
        Slab(YardEastX, HinterlandEastX, YardSouthZ, YardNorthZ);
        foreach (var (z0, z1) in ((float, float)[])[(YardNorthZ, HinterlandReach), (-HinterlandReach, YardSouthZ)])
        {
            AddMinMax(o + new float3(YardQuayX - 0.15f, YardSeaLevel, z0), o + new float3(YardQuayX, 0, z1), concrete * 0.8f, Material.Concrete, blocksShots: false, collides: false);
            AddMinMax(o + new float3(YardQuayX - 0.15f, 0, z0), o + new float3(YardQuayX + 0.35f, 0.22f, z1), concrete, Material.Concrete, blocksShots: false, collides: false);
        }
        for (var z = YardNorthZ + 7f; z < 250f; z += 9f)
            foreach (var side in (ReadOnlySpan<float>)[1f, -1f])
            {
                var bz = side > 0 ? z : YardSouthZ - (z - YardNorthZ);
                Add(o + new float3(YardQuayX + 0.75f, 0.3f, bz), new float3(0.38f, 0.6f, 0.38f), new float3(0.12f, 0.12f, 0.11f), Material.PaintedMetal, blocksShots: false, collides: false);
            }

        // --- The blocks of stacks ---
        var random = new Random(2468);
        var boxes = new MeshBuilder[ContainerColors.Length];
        var modelled = new Dictionary<int, List<float4x4>>();
        var container = TryModel("container_lod") ?? TryModel("container");
        void Blocks(float x0, float x1, float z0, float z1)
        {
            for (var bx = x0; bx + BlockSizeX <= x1; bx += BlockSizeX + LaneX)
                for (var bz = z0; bz + BlockSizeZ <= z1; bz += BlockSizeZ + LaneZ)
                {
                    var high = 2 + random.Next(4);   // (some blocks are kept lower than others)
                    for (var slot = 0; slot < BlockLength; slot++)
                        for (var row = 0; row < BlockRows; row++)
                        {
                            if (random.NextDouble() < 0.08)
                                continue;   // an empty slot
                            var x = bx + (slot + 0.5f) * SlotX;
                            var z = bz + (row + 0.5f) * RowZ;
                            var tiers = int.Max(1, high - random.Next(3));
                            var near = DistanceOutside(x, z) < NearModelReach && container is not null;
                            for (var t = 0; t < tiers; t++)
                            {
                                var c = random.Next(ContainerColors.Length);
                                var at = o + new float3(x, t * ContainerHeight, z);
                                NoteContainer(at, alongX: true, "hinterland");
                                if (near)
                                {
                                    if (!modelled.TryGetValue(c, out var list))
                                        modelled[c] = list = [];
                                    list.Add(float4x4.CreateRotationY(random.Next(2) == 0 ? 0 : float.Pi) * float4x4.CreateTranslation(at));
                                }
                                else
                                    ContainerBox(boxes[c] ??= new MeshBuilder(), at, top: t == tiers - 1);
                            }
                        }
                }
        }
        Blocks(ApronX, HinterlandStacksEastX, YardNorthZ + 9f, HinterlandReach - 10f);
        Blocks(ApronX, HinterlandStacksEastX, -HinterlandReach + 10f, YardSouthZ - 9f);
        Blocks(YardEastX + 9f, HinterlandStacksEastX, YardSouthZ, YardNorthZ);

        for (var c = 0; c < boxes.Length; c++)
            if (boxes[c] is { } b)
                _props.Add(new Prop(b.Build(), float4x4.Identity, ContainerColors[c] * 0.9f, Material.PaintedMetal));
        if (container is not null)
            foreach (var (c, transforms) in modelled)
                foreach (var (mesh, material) in Engine.Model.MergeInstances(container, transforms))
                    _props.Add(new Prop(mesh, float4x4.Identity, ContainerColors[c], material.Material, Part: material));

        // --- Flood masts over the lanes, in a line along the apron and over the blocks; lit by night ---
        var mast = TryModel("flood_mast");
        var sodium = night ? new float3(1.0f, 0.8f, 0.5f) : new float3(0.42f, 0.4f, 0.36f);
        foreach (var side in (ReadOnlySpan<float>)[1f, -1f])
            for (var d = 90f; d < HinterlandReach; d += 110f)
                foreach (var x in (ReadOnlySpan<float>)[ApronX - 4f, 120f, 250f])
                {
                    var foot = o + new float3(x, 0, side > 0 ? YardNorthZ + d : YardSouthZ - d);
                    if (mast is not null)
                        AddModel(mast, float4x4.CreateRotationY(float.Pi) * float4x4.CreateTranslation(foot), new float3(0.6f, 0.6f, 0.58f));
                    else
                        Add(foot + new float3(0, MastHeight / 2, 0), new float3(0.45f, MastHeight, 0.45f), new float3(0.5f, 0.5f, 0.5f), Material.Galvanized, blocksShots: false, collides: false);
                    Add(foot + new float3(-0.9f, MastHeight - 0.55f, 0), new float3(0.8f, 0.45f, 1.6f), sodium, Material.Emissive, blocksShots: false, collides: false);
                    if (night)
                        Add(foot + new float3(-6f, 0.02f, 0), new float3(26f, 0.04f, 26f), new float3(0.11f, 0.06f, 0.022f), Material.Emissive, blocksShots: false, collides: false);
                }

        // --- A few straddle gantries over the blocks ---
        if (TryModel("gantry_crane") is { } rtg)
            foreach (var (x, z) in ((float, float)[])[(60f, 130f), (190f, 300f), (60f, -160f), (250f, -420f), (125f, 520f)])
                AddModel(rtg, float4x4.CreateRotationY(float.Pi / 2) * float4x4.CreateTranslation(o + new float3(x, 0, z)), new float3(0.86f, 0.64f, 0.06f));

        BuildQuayShips(night);
        FlushPoliceBatches();
    }

    /// <summary>How far (x, z) is outside the play area's quay (0 inside it).</summary>
    private static float DistanceOutside(float x, float z)
    {
        var dx = float.Max(float.Max(YardQuayX - x, x - YardEastX), 0f);
        var dz = float.Max(float.Max(YardSouthZ - z, z - YardNorthZ), 0f);
        return float.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>A container as a plain box (along x), standing at <paramref name="foot"/>: its sides, and its top if nothing's on it.</summary>
    private static void ContainerBox(MeshBuilder b, float3 foot, bool top)
    {
        var h = new float3(ContainerLength / 2, ContainerHeight, ContainerWidth / 2);
        float3 P(float x, float y, float z) => foot + new float3(x * h.X, y * h.Y, z * h.Z);
        b.Quad(P(-1, 0, 1), P(1, 0, 1), P(1, 1, 1), P(-1, 1, 1), new float3(0, 0, 1));
        b.Quad(P(1, 0, -1), P(-1, 0, -1), P(-1, 1, -1), P(1, 1, -1), new float3(0, 0, -1));
        b.Quad(P(1, 0, 1), P(1, 0, -1), P(1, 1, -1), P(1, 1, 1), new float3(1, 0, 0));
        b.Quad(P(-1, 0, -1), P(-1, 0, 1), P(-1, 1, 1), P(-1, 1, -1), new float3(-1, 0, 0));
        if (top)
            b.Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), new float3(0, 1, 0));
    }

    /// <summary>The two ships tied up along our quay, as on the far bank: hull, house, hatch covers and the boxes on them.</summary>
    private void BuildQuayShips(bool night)
    {
        var hull = TryModel("ship_hull");
        var house = TryModel("ship_house");
        var container = TryModel("container_lod") ?? TryModel("container");
        var hatchGreen = JB(Material.PaintedMetal, new float3(0.27f, 0.33f, 0.28f));
        var windows = JB(night ? Material.Emissive : Material.Glass, night ? new float3(0.85f, 0.7f, 0.45f) : new float3(0.08f, 0.09f, 0.1f));
        var random = new Random(1357);
        var models = new Dictionary<int, List<float4x4>>();
        foreach (var (z, bow) in QuayShips)
        {
            // (Its port side to the quay, the centreline 17.5 m out from the face; the ship's +x, its bow, along the quay.)
            var yaw = bow < 0 ? float.Pi / 2 : -float.Pi / 2;
            var ship = float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(YardOrigin + new float3(YardQuayX - 17.5f, FarShipDeckY, z));
            if (hull is not null)
                AddModel(hull, ship, new float3(0.84f, 0.84f, 0.82f));
            if (house is not null)
                AddModel(house, ship, new float3(0.84f, 0.84f, 0.82f));
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
                var cap = b >= 9 ? 3 : 5;
                for (var r = -rowsHalf; r <= rowsHalf; r++)
                {
                    var tiers = cap - random.Next(3);
                    for (var t = 0; t < tiers; t++)
                    {
                        var c = random.Next(FarContainerColors.Length);
                        if (!models.TryGetValue(c, out var list))
                            models[c] = list = [];
                        list.Add(float4x4.CreateRotationY(random.Next(2) == 0 ? 0 : float.Pi) * float4x4.CreateTranslation(mid, 1.5f + t * ContainerHeight, r * 2.5f) * ship);
                    }
                }
            }
            for (var deck = 0; deck < 5; deck++)
            {
                var y = 4.2f + deck * 2.9f;
                OBox(windows, ship, new float3(-65.95f, y, 0), new float3(0.06f, 0.9f, 19f));
                foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
                    OBox(windows, ship, new float3(-72f, y, side * 11.03f), new float3(9f, 0.9f, 0.06f));
            }
        }
        if (container is not null)
            foreach (var (c, transforms) in models)
                foreach (var (mesh, material) in Engine.Model.MergeInstances(container, transforms))
                    _props.Add(new Prop(mesh, float4x4.Identity, FarContainerColors[c], material.Material, Part: material));
    }
}
