using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The container ship: a 190 m geared container ship lying stopped at sea under a grey overcast. The pink portal is
/// in the front of the wheelhouse, on the walkway across the bridge front 17 m above the deck: you come out looking
/// forward over nine bays of containers stacked two to five high, three yellow deck cranes and the bow. A ladder
/// goes down the front of the accommodation block to the deck; walkways run along both sides to the forecastle
/// (another ladder) and aft past the superstructure to the stern.
/// </summary>
/// <remarks>
/// The hull, the superstructure and the cranes are Blender models (Tools/blender/build_ship.py, which uses the same
/// layout as the constants here: keep them in step). The game adds the walkway floors, hatch covers, lashing bridges,
/// the containers (the low-detail container model, merged by colour) and all the collision.
/// </remarks>
public sealed partial class Level
{
    /// <summary>Where the ship is (the shader's area 5). Its main deck is at y = 0, the bow towards +X.</summary>
    public static readonly float3 ShipOrigin = new(12000, 0, 0);

    /// <summary>The sea surface (must match SEA_LEVEL in the shader).</summary>
    public const float SeaLevel = -11f;

    // Hull (see build_ship.py).
    private const float HalfBeam = 16f;
    private const float BowStart = 40f, BowTip = 95f, SternStart = -70f, Transom = -95f;
    private const float ForecastleX = 79f, ForecastleY = 2.6f;
    // Superstructure.
    private const float BlockX0 = -78f, BlockX1 = -66f, BlockHalfZ = 11f, BridgeFloorY = 16.8f;
    private const float WheelX0 = -73f, WheelTopY = 20f;
    private const float WingX0 = -71f, WalkX1 = -63.8f, WingZ = 15.6f;
    private const float PortalGapZ0 = 4.6f, PortalGapZ1 = 8.4f;
    private const float BlockLadderZ = 10.2f;
    // Cargo: bays of 40 ft containers (13.2 m each, with a metre between bays for the lashing bridges), and three
    // crane slots on the centreline.
    private const float BayLength = 13.2f;
    private const float CraneSlot = 7f;
    private const float HatchTop = 1.5f;
    private const float RowPitch = 2.5f;
    private const int MaxTiers = 5;

    public static float3 ShipShadowCenter => ShipOrigin + new float3(-5, 0, 0);
    public const float ShipShadowHalfExtent = 105f;
    public static readonly float3 ShipSunDirection = new(0.35f, 0.85f, 0.4f);

    /// <summary>The pink portal: in the wheelhouse front, facing forward over the bridge-front walkway.</summary>
    public static float3 ShipPortalFloorPoint => ShipOrigin + new float3(BlockX1, BridgeFloorY, (PortalGapZ0 + PortalGapZ1) / 2);
    public static float3 ShipPortalNormal => new(1, 0, 0);

    /// <summary>The sky box round the ship, and the sea inside it (the shader carries the sea on to the horizon).</summary>
    public const float ShipSkySize = 1500f;
    public static Aabb OceanBounds => new(
        ShipOrigin + new float3(-ShipSkySize / 2 + 5, SeaLevel - 0.3f, -ShipSkySize / 2 + 5),
        ShipOrigin + new float3(ShipSkySize / 2 - 5, SeaLevel, ShipSkySize / 2 - 5));

    private static readonly float3 DeckGreen = new(0.30f, 0.37f, 0.31f);
    private static readonly float3 HatchGreen = new(0.27f, 0.33f, 0.28f);
    private static readonly float3 ShipWhite = new(0.84f, 0.84f, 0.82f);
    private static readonly float3 LashingGrey = new(0.52f, 0.54f, 0.55f);
    private static readonly float3 CraneYellow = new(0.86f, 0.66f, 0.08f);

    // The colours of the boxes in the stacks: lots of blue, red, white and grey, some green, orange and yellow.
    private static readonly float3[] ShipContainerColors =
    [
        new(0.14f, 0.30f, 0.56f), new(0.14f, 0.30f, 0.56f), new(0.11f, 0.22f, 0.42f), // blues
        new(0.62f, 0.16f, 0.12f), new(0.46f, 0.12f, 0.12f),                             // red, maroon
        new(0.82f, 0.82f, 0.79f), new(0.82f, 0.82f, 0.79f),                             // white
        new(0.46f, 0.48f, 0.50f),                                                         // grey
        new(0.18f, 0.42f, 0.28f),                                                         // green
        new(0.80f, 0.42f, 0.12f),                                                         // orange
        new(0.36f, 0.56f, 0.70f),                                                         // light blue
        new(0.78f, 0.64f, 0.14f),                                                         // yellow
    ];

    /// <summary>The hull's half-breadth at the main deck (as in build_ship.py).</summary>
    private static float DeckHalfBreadth(float x)
    {
        float f;
        if (x > BowStart)
        {
            var t = float.Min((x - BowStart) / (BowTip - BowStart), 1f);
            f = float.Sqrt(float.Max(1 - float.Pow(t, 2.2f), 0f));
        }
        else if (x < SternStart)
        {
            var s = (SternStart - x) / (SternStart - Transom);
            f = 1 - 0.19f * s * s;
        }
        else
        {
            f = 1f;
        }
        return HalfBeam * f;
    }

    private static float3 ShipAt(float x, float y, float z) => ShipOrigin + new float3(x, y, z);

    /// <summary>
    /// A run of side-by-side stacks of the same height in one bay: <see cref="Box"/> is their collision (its top is where you
    /// stand on them). <see cref="LadderFoot"/>: there's a ladder up it from the deck, and this is a spot on the deck in front of it.
    /// </summary>
    public readonly record struct ShipStackRun(Aabb Box, int Bay, float3? LadderFoot);

    /// <summary>A bay of containers: from <see cref="X0"/> to <see cref="X1"/> (ship space), its hatch cover <see cref="CoverHalf"/> either side of the centreline.</summary>
    public readonly record struct ShipBay(float X0, float X1, float CoverHalf);

    private static readonly List<ShipStackRun> _shipStacks = [];
    private static readonly List<ShipBay> _shipBays = [];

    /// <summary>The tops of the container stacks, for people to stand on (<see cref="Npcs"/>). Filled in when the ship is built.</summary>
    public static IReadOnlyList<ShipStackRun> ShipStacks => _shipStacks;

    /// <summary>The bays, aft to forward. Filled in when the ship is built.</summary>
    public static IReadOnlyList<ShipBay> ShipBays => _shipBays;

    /// <summary>The hull's half-breadth at the main deck, <paramref name="x"/> m forward of the ship's origin.</summary>
    public static float ShipHalfBreadth(float x) => DeckHalfBreadth(x);

    private void BuildShip()
    {
        var random = new Random(2024);
        _shipStacks.Clear();
        _shipBays.Clear();

        // --- The Blender models ---
        if (TryModel("ship_hull") is { } hull)
            AddModel(hull, float4x4.CreateTranslation(ShipOrigin), ShipWhite);
        if (TryModel("ship_house") is { } house)
            AddModel(house, float4x4.CreateTranslation(ShipOrigin), ShipWhite);

        // --- Deck and walkway collision (the hull model shows them) ---
        // The main deck, in 3 m strips that follow the hull's taper, with railings (taller than they look, so you
        // can't jump over the side).
        for (var x = Transom; x < ForecastleX - 0.01f; x += 3f)
        {
            var x1 = float.Min(x + 3f, ForecastleX);
            var hb = float.Min(DeckHalfBreadth(x), DeckHalfBreadth(x1));
            AddStatic(new Aabb(ShipAt(x, -0.5f, -hb), ShipAt(x1, 0, hb)), default, visible: false);
            foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            {
                var z = side * (hb - 0.12f);
                AddStatic(BoxBetween(ShipAt(x, 0, z - 0.06f), ShipAt(x1, 1.7f, z + 0.06f)), default, visible: false, blocksShots: false);
            }
        }
        AddStatic(BoxBetween(ShipAt(Transom, 0, -DeckHalfBreadth(Transom)), ShipAt(Transom + 0.35f, 1.7f, DeckHalfBreadth(Transom))), default, visible: false, blocksShots: false);
        // The forecastle: a solid block up to its deck, with its bulwark round the bow.
        for (var x = ForecastleX - 0.15f; x < BowTip - 0.01f; x += 2f)
        {
            var x1 = float.Min(x + 2f, BowTip);
            var hb = float.Max(float.Min(DeckHalfBreadth(x), DeckHalfBreadth(x1)), 0.2f);
            AddStatic(new Aabb(ShipAt(x, 0, -hb), ShipAt(x1, ForecastleY, hb)), default, visible: false);
            foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
                AddStatic(BoxBetween(ShipAt(x, ForecastleY, side * (hb - 0.3f)), ShipAt(x1, ForecastleY + 1.7f, side * hb)), default, visible: false, blocksShots: false);
        }
        AddLadder(ShipAt(ForecastleX - 0.15f, 0, 5.5f), new float3(1, 0, 0), ForecastleY, LadderTint, rails: 1.0f);
        AddLadder(ShipAt(ForecastleX - 0.15f, 0, -5.5f), new float3(1, 0, 0), ForecastleY, LadderTint, rails: 1.0f);
        // The windlasses on the forecastle and the mooring winches on the stern deck (build_ship.py's windlass and
        // mooring_winch: shafts athwartships, gearbox and motor inboard).
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            AddStatic(BoxBetween(ShipAt(84.3f, ForecastleY, side * 1.3f), ShipAt(85.7f, ForecastleY + 1.6f, side * 5.15f)), default, visible: false);
            AddStatic(BoxBetween(ShipAt(-92.8f, 0, side * 4.25f), ShipAt(-91.2f, 1.8f, side * 7.55f)), default, visible: false);
        }

        // --- Superstructure collision, the bridge-front walkway and wings, the portal's bulkhead panel ---
        AddStatic(new Aabb(ShipAt(BlockX0, 0, -BlockHalfZ), ShipAt(BlockX1, BridgeFloorY, BlockHalfZ)), default, visible: false);
        AddStatic(new Aabb(ShipAt(WheelX0, BridgeFloorY, -BlockHalfZ), ShipAt(BlockX1, WheelTopY + 0.3f, BlockHalfZ)), default, visible: false);
        AddStatic(new Aabb(ShipAt(-90f, 0, -7f), ShipAt(BlockX0, 10f, 7f)), default, visible: false);       // engine casing
        AddStatic(new Aabb(ShipAt(-88f, 10f, -3.5f), ShipAt(-80f, 26f, 3.5f)), default, visible: false);    // funnel
        // The wheelhouse front either side of the portal is the model's; the panel the portal is cut into is ours.
        AddMinMax(ShipAt(BlockX1 - 0.3f, BridgeFloorY, PortalGapZ0), ShipAt(BlockX1, WheelTopY, PortalGapZ1), ShipWhite, Material.MarinePaint);
        // Walkway floor (with the hatch for the ladder down the block front) and the two wings.
        const float hatchX1 = BlockX1 + 1.1f, hatchZ0 = BlockLadderZ - 0.6f, hatchZ1 = BlockLadderZ + 0.6f;
        void Floor(float x0, float x1, float z0, float z1) =>
            AddMinMax(ShipAt(x0, BridgeFloorY - 0.25f, z0), ShipAt(x1, BridgeFloorY, z1), DeckGreen, Material.PaintedMetal);
        Floor(BlockX1, WalkX1, -WingZ, hatchZ0);
        Floor(BlockX1, WalkX1, hatchZ1, WingZ);
        Floor(hatchX1, WalkX1, hatchZ0, hatchZ1);
        Floor(WingX0, BlockX1, -WingZ, -BlockHalfZ);
        Floor(WingX0, BlockX1, BlockHalfZ, WingZ);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            AddStatic(BoxBetween(ShipAt(WingX0, BridgeFloorY, side * (WingZ - 0.1f)), ShipAt(WalkX1, BridgeFloorY + 1.7f, side * WingZ)), default, visible: false, blocksShots: false);
            AddStatic(BoxBetween(ShipAt(WingX0, BridgeFloorY, side * BlockHalfZ), ShipAt(WingX0 + 0.1f, BridgeFloorY + 1.7f, side * WingZ)), default, visible: false, blocksShots: false);
        }
        AddStatic(BoxBetween(ShipAt(WalkX1 - 0.1f, BridgeFloorY, -WingZ), ShipAt(WalkX1, BridgeFloorY + 1.7f, WingZ)), default, visible: false, blocksShots: false);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])   // the wing consoles
            AddStatic(BoxBetween(ShipAt(BlockX1, BridgeFloorY, side * (WingZ - 1.4f)), ShipAt(BlockX1 + 0.8f, BridgeFloorY + 1.15f, side * (WingZ - 0.6f))), default, visible: false);
        AddLadder(ShipAt(BlockX1, 0, BlockLadderZ), new float3(-1, 0, 0), BridgeFloorY, LadderTint, rails: 1.1f, exit: new float3(1, 0, 0));

        // --- Cargo: hatch covers, stacks of containers, lashing bridges, cranes ---
        var bays = new List<(float X0, float X1)>();
        var cranes = new List<float>();
        var cursor = BlockX1 + 4f;
        foreach (var slot in "BBCBBCBBCBBB")
        {
            if (slot == 'C')
            {
                cranes.Add(cursor + CraneSlot / 2);
                cursor += CraneSlot;
            }
            else
            {
                bays.Add((cursor, cursor + BayLength));
                cursor += BayLength;
            }
        }

        var container = TryModel("container_lod") ?? TryModel("container");
        var byColor = new Dictionary<int, List<float4x4>>();
        var lashing = new MeshBuilder();
        var hatches = new MeshBuilder();
        for (var b = 0; b < bays.Count; b++)
        {
            var (x0, x1) = bays[b];
            var forwardEnd = float.Min(DeckHalfBreadth(x1), DeckHalfBreadth(x0));
            // As many rows as fit inside the walkways at this bay's narrower end.
            var rowsHalf = 5;
            while (rowsHalf > 0 && rowsHalf * RowPitch + 1.22f > forwardEnd - 1.9f)
                rowsHalf--;
            var coverHalf = rowsHalf * RowPitch + 1.45f;
            var midX = (x0 + x1) / 2;
            // Hatch cover: a raised steel lid with a coaming, in two halves.
            hatches.Box(ShipAt(midX, HatchTop / 2, 0), new float3(BayLength - 0.6f, HatchTop, coverHalf * 2));
            hatches.Box(ShipAt(midX, HatchTop + 0.03f, 0), new float3(BayLength - 0.2f, 0.06f, 0.12f));
            AddStatic(new Aabb(ShipAt(x0 + 0.3f, 0, -coverHalf), ShipAt(x1 - 0.3f, HatchTop, coverHalf)), default, visible: false);

            // Stacks: higher in the middle, a little random, lower towards the bow so the bridge can see over them.
            var heights = new int[2 * rowsHalf + 1];
            var stackTop = 0;
            for (var r = -rowsHalf; r <= rowsHalf; r++)
            {
                var cap = MaxTiers - (b >= bays.Count - 2 ? 1 : 0) - (int.Abs(r) >= 4 ? 1 : 0) - (b == 0 ? 2 : b == 1 ? 1 : 0);
                var h = int.Clamp(cap - random.Next(3) + (random.Next(5) == 0 ? -1 : 0), 1, cap);
                if (random.Next(14) == 0)
                    h = 0; // an empty slot here and there
                heights[r + rowsHalf] = h;
                stackTop = int.Max(stackTop, h);
                for (var tier = 0; tier < h; tier++)
                {
                    var c = random.Next(ShipContainerColors.Length);
                    if (!byColor.TryGetValue(c, out var list))
                        byColor[c] = list = [];
                    var yaw = random.Next(5) == 0 ? 0f : float.Pi;   // doors mostly aft
                    list.Add(float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(ShipAt(midX, HatchTop + tier * ContainerHeight, r * RowPitch)));
                }
            }
            _shipBays.Add(new ShipBay(x0, x1, coverHalf));

            // Collision at each stack's own height, side-by-side stacks of the same height as one box (no gaps between
            // them to fall through), so the tops are where you see them: people walk about on them.
            var runs = new List<(int R0, int R1, int H)>();
            for (var r0 = -rowsHalf; r0 <= rowsHalf;)
            {
                var h = heights[r0 + rowsHalf];
                var r1 = r0;
                while (r1 < rowsHalf && heights[r1 + 1 + rowsHalf] == h)
                    r1++;
                if (h > 0)
                {
                    runs.Add((r0, r1, h));
                    AddStatic(new Aabb(ShipAt(midX - ContainerLength / 2, HatchTop, r0 * RowPitch - ContainerWidth / 2),
                        ShipAt(midX + ContainerLength / 2, HatchTop + h * ContainerHeight, r1 * RowPitch + ContainerWidth / 2)), default, visible: false);
                }
                r0 = r1 + 1;
            }

            // Ladders up the end of a stack where the deck in front of it is open (not a lashing bridge): the aft end of
            // the first bay and of each bay behind a crane, the forward end of each bay in front of one. Each goes up the
            // widest run there (the tallest, of two as wide), in the middle.
            var aftOpen = b == 0 || bays[b - 1].X1 < x0 - 0.01f;
            var fwdOpen = b + 1 < bays.Count && bays[b + 1].X0 > x1 + 0.01f;
            var best = runs.Count == 0 ? -1 : runs.Select((run, i) => (Score: (run.R1 - run.R0) * 10 + run.H, i)).Max().i;
            var ladders = new List<(int Run, float3 Foot)>();
            foreach (var (open, sign) in (ReadOnlySpan<(bool, float)>)[(aftOpen, -1f), (fwdOpen, 1f)])
            {
                if (!open || best < 0)
                    continue;
                var (r0, r1, h) = runs[best];
                // (A row's middle: between two rows of an even run, the ladder would straddle the gap.)
                var z = ((r0 + r1) / 2) * RowPitch;
                var face = midX + sign * ContainerLength / 2;
                // Stood off the stack's end far enough to clear the hatch cover's edge at its foot.
                AddLadder(ShipAt(face, 0, z), new float3(-sign, 0, 0), HatchTop + h * ContainerHeight, LadderTint, rails: 1.0f, standoff: 0.41f);
                ladders.Add((best, ShipAt(face + sign * 1.6f, 0, z)));
            }
            for (var i = 0; i < runs.Count; i++)
            {
                var (r0, r1, h) = runs[i];
                var box = new Aabb(ShipAt(midX - ContainerLength / 2, HatchTop, r0 * RowPitch - ContainerWidth / 2),
                    ShipAt(midX + ContainerLength / 2, HatchTop + h * ContainerHeight, r1 * RowPitch + ContainerWidth / 2));
                var foot = ladders.FindIndex(l => l.Run == i) is var l and >= 0 ? ladders[l].Foot : (float3?)null;
                _shipStacks.Add(new ShipStackRun(box, b, foot));
            }

            // A lashing bridge between this bay and the one in front (if it's right up against it): posts between
            // the rows, two grating platforms and a handrail, two tiers high.
            if (b + 1 < bays.Count && float.Abs(bays[b + 1].X0 - x1) < 0.01f)
            {
                var gx = x1;
                var half = float.Min(coverHalf, float.Min(DeckHalfBreadth(bays[b + 1].X1), DeckHalfBreadth(x1)) - 1.9f);
                const float bridgeTop = HatchTop + 2 * 2.59f;
                for (var z = -half; z <= half + 0.01f; z += RowPitch)
                    lashing.Box(ShipAt(gx, bridgeTop / 2, z), new float3(0.6f, bridgeTop, 0.12f));
                foreach (var y in (ReadOnlySpan<float>)[HatchTop + 2.4f, bridgeTop])
                    lashing.Box(ShipAt(gx, y, 0), new float3(0.8f, 0.06f, half * 2));
                lashing.Box(ShipAt(gx, bridgeTop + 1.05f, 0), new float3(0.05f, 0.05f, half * 2));
                for (var z = -half; z <= half + 0.01f; z += RowPitch)
                    lashing.Box(ShipAt(gx, bridgeTop + 0.52f, z), new float3(0.05f, 1.05f, 0.05f));
                AddStatic(new Aabb(ShipAt(gx - 0.5f, 0, -half - 0.1f), ShipAt(gx + 0.5f, bridgeTop + 1.1f, half + 0.1f)), default, visible: false);
            }
        }
        _props.Add(new Prop(hatches.Build(), float4x4.Identity, HatchGreen, Material.PaintedMetal));
        _props.Add(new Prop(lashing.Build(), float4x4.Identity, LashingGrey, Material.PaintedMetal));
        if (container is not null)
        {
            foreach (var (c, transforms) in byColor)
                foreach (var (mesh, material) in Engine.Model.MergeInstances(container, transforms))
                    _props.Add(new Prop(mesh, float4x4.Identity, ShipContainerColors[c], material.Material, Part: material));
        }

        // Cranes on their pedestals in the slots between the bays, jibs resting fore and aft.
        var crane = TryModel("ship_crane");
        for (var i = 0; i < cranes.Count; i++)
        {
            var at = ShipAt(cranes[i], 0, 0);
            AddCollider(at + new float3(0, 6.5f, 0), new float3(3.2f, 13f, 3.2f));
            var yaw = i == 1 ? float.Pi : 0f;   // the middle one faces aft
            if (crane is not null)
                AddModel(crane, float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(at), CraneYellow);
        }
    }
}
