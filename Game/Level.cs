using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <param name="Material">Procedural surface used by the shader (concrete, rusty metal, ...).</param>
/// <param name="Visible">False for collision-only boxes (e.g. hidden inside a portal's ring).</param>
/// <param name="BlocksShots">False for boxes that don't stop projectiles or aim rays.</param>
/// <param name="Collides">False for decoration the player walks straight through (small debris).</param>
/// <param name="Id">
/// Which box the level built this from (1-based, its place in the list once the level is built; pieces cut by
/// <see cref="Level.CarveOpening"/> keep their box's). 0 for boxes added afterwards by other systems (portals,
/// targets): the level editor (Game/LevelEditor.cs) only moves boxes that have one.
/// </param>
/// <param name="Paint">The colour of the faces in <paramref name="PaintFaces"/> (the rest are <paramref name="Color"/>).</param>
/// <param name="PaintMaterial">The surface of the faces in <paramref name="PaintFaces"/> (null: the box's own <paramref name="Material"/>).</param>
/// <param name="PaintFaces">
/// Faces painted <paramref name="Paint"/> instead (bit 0 +x, 1 -x, 2 +y, 3 -y, 4 +z, 5 -z): a wall between two rooms
/// painted differently on each side is one box, not two half-thickness ones.
/// </param>
/// <param name="FaceTextures">
/// The texture set of each face (in the bits' order: +x, -x, +y, -y, +z, -z), when the level editor's paint tool has
/// given any of them one of their own; null: <paramref name="Material"/> and <paramref name="PaintMaterial"/> as above.
/// </param>
/// <param name="FaceColors">Likewise each face's colour (null: <paramref name="Color"/> and <paramref name="Paint"/> as above).</param>
public readonly record struct LevelBox(
    Aabb Bounds,
    float3 Color,
    Material Material = Material.Plain,
    bool Visible = true,
    bool BlocksShots = true,
    bool Collides = true,
    int Id = 0,
    float3 Paint = default,
    int PaintFaces = 0,
    Material? PaintMaterial = null,
    Material[]? FaceTextures = null,
    float3[]? FaceColors = null)
{
    public const int FacePosX = 1, FaceNegX = 2, FacePosY = 4, FaceNegY = 8, FacePosZ = 16, FaceNegZ = 32;

    /// <summary>The texture set face <paramref name="face"/> (0 +x .. 5 -z) is drawn in.</summary>
    public Material TextureOf(int face) =>
        FaceTextures is { } own ? own[face] : (PaintFaces & (1 << face)) != 0 ? PaintMaterial ?? Material : Material;

    /// <summary>The colour face <paramref name="face"/> (0 +x .. 5 -z) is drawn in.</summary>
    public float3 ColorOf(int face) => FaceColors is { } own ? own[face] : (PaintFaces & (1 << face)) != 0 ? Paint : Color;

    /// <summary>The faces drawn alike (in one colour and texture set), each set of them once.</summary>
    private void DrawFaceGroups(Renderer renderer, in float4x4 model)
    {
        var done = 0;
        for (var face = 0; face < 6; face++)
        {
            if ((done & (1 << face)) != 0)
                continue;
            var color = ColorOf(face);
            var texture = TextureOf(face);
            var faces = 0;
            for (var other = face; other < 6; other++)
            {
                if ((done & (1 << other)) == 0 && TextureOf(other) == texture && ColorOf(other) == color)
                    faces |= 1 << other;
            }
            done |= faces;
            renderer.DrawCubeFaces(model, faces, color, ShadeMode.Lit, texture);
        }
    }

    /// <summary>The colour of the face whose outward normal is nearest <paramref name="normal"/>.</summary>
    public float3 ColorFacing(float3 normal) => PaintFaces != 0 && (PaintFaces & FaceOf(normal)) != 0 ? Paint : Color;

    /// <summary>The face bit for the axis direction nearest <paramref name="normal"/>.</summary>
    public static int FaceOf(float3 normal)
    {
        var a = float3.Abs(normal);
        return a.X >= a.Y && a.X >= a.Z ? (normal.X >= 0 ? FacePosX : FaceNegX)
             : a.Y >= a.Z ? (normal.Y >= 0 ? FacePosY : FaceNegY)
             : (normal.Z >= 0 ? FacePosZ : FaceNegZ);
    }

    /// <summary>The face bits <paramref name="faces"/> once the box is turned by <paramref name="yaw"/> about the vertical.</summary>
    public static int TurnFaces(int faces, float yaw)
    {
        if (faces == 0 || yaw == 0)
            return faces;
        var turn = float4x4.CreateRotationY(yaw);
        var turned = 0;
        ReadOnlySpan<float3> normals = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];
        for (var i = 0; i < 6; i++)
            if ((faces & (1 << i)) != 0)
                turned |= FaceOf(float3.Transform(normals[i], turn));
        return turned;
    }

    /// <summary>Draws the box (in its two colours, when it has two).</summary>
    public void Draw(Renderer renderer)
    {
        if (FaceTextures is not null || FaceColors is not null)
        {
            DrawFaceGroups(renderer, float4x4.CreateScale(Bounds.Size) * float4x4.CreateTranslation(Bounds.Center));
            return;
        }
        if (PaintFaces == 0)
        {
            renderer.DrawBox(Bounds, Color, ShadeMode.Lit, Material);
            return;
        }
        renderer.DrawBoxFaces(Bounds, 63 & ~PaintFaces, Color, ShadeMode.Lit, Material);
        renderer.DrawBoxFaces(Bounds, PaintFaces, Paint, ShadeMode.Lit, PaintMaterial ?? Material);
    }

    /// <summary>Draws the box placed by <paramref name="pose"/> (the prison's container, as it moves).</summary>
    public void Draw(Renderer renderer, in float4x4 pose)
    {
        var model = float4x4.CreateScale(Bounds.Size) * float4x4.CreateTranslation(Bounds.Center) * pose;
        if (FaceTextures is not null || FaceColors is not null)
        {
            DrawFaceGroups(renderer, model);
            return;
        }
        renderer.DrawCubeFaces(model, 63 & ~PaintFaces, Color, ShadeMode.Lit, Material);
        renderer.DrawCubeFaces(model, PaintFaces, Paint, ShadeMode.Lit, PaintMaterial ?? Material);
    }
}

/// <summary>
/// The level's separate areas (and the cellar under the outback's tin shed). Only the areas you can see from where
/// the camera is are drawn: see <see cref="Level.ZonesSeenFrom"/>.
/// </summary>
[Flags]
public enum Zones
{
    None = 0,
    Warehouse = 1,
    Yard = 2,
    Outback = 4,
    Cellar = 8,
    Bunker = 16,
    Arctic = 32,
    Ship = 64,
    Jungle = 128,
    Police = 256,
    Prison = 512,
    Flats = 1024,
    Rio = 2048,
}

/// <summary>
/// A ladder you can climb. <paramref name="Volume"/> is where the player has to be to hold on to it (in front of the
/// rungs, from its foot to a little above its top); <paramref name="Facing"/> points from the climber to the rungs;
/// <paramref name="Top"/> is the height of the floor you step off onto at the top, and <paramref name="Exit"/> the way
/// you step off it when you climb on up (over the top of the rungs, or back away from a wall onto a walkway).
/// <paramref name="Prop"/> is the level's prop that draws it (so the level editor can move the ladder with it), or -1.
/// </summary>
public readonly record struct Ladder(Aabb Volume, float3 Facing, float Top, float3 Exit, int Prop = -1);

/// <summary>
/// An old, run-down warehouse built entirely from boxes; the shader gives each box a procedural material.
/// Layout (looking down from above, player spawns at the south end facing north / -Z):
///
///     +----------- north wall (lit doorway, catwalk) ----------+
///     |  ladder [ catwalk ]          [far target]              |
///     |   [loading dock + target]                    [bins]    |
///     |      steps              [columns]                      |
///     |   [blue portal on the dock] [near target] (orange portal) |
///     |   pallets  crates                         crates       |
///     |                       (spawn)                          |
///     +---------------------- south wall ----------------------+
///
/// Side walls have concrete piers, tall grimy windows and pipes; steel roof trusses and hanging lamps overhead.
///
/// The orange portal leads somewhere else entirely: into a shipping container standing in an outdoor
/// container yard (see <see cref="YardOrigin"/>). Walk out of its open doors into a small yard walled in by stacks.
/// In the yard's south-east pocket, the purple portal leads on to the Australian outback (see <see cref="OutbackOrigin"/>):
/// a tin shack beside a long, empty dirt road, with open red scrubland all around. A short walk north up the road
/// there's an old roadhouse: a timber-and-tin store with a verandah and fuel pumps, a motel block, sheds and a mast.
/// </summary>
public sealed partial class Level
{
    private readonly List<LevelBox> _boxes = [];
    private readonly List<float4> _lamps = [];

    public List<LevelBox> Boxes => _boxes;   // (the lists themselves: a foreach over an interface boxes its enumerator, every time)

    /// <summary>Changes whenever <see cref="Boxes"/> does.</summary>
    public int BoxVersion => _version;

    /// <summary>Hanging lamp lights: xyz = position, w = intensity.</summary>
    public IReadOnlyList<float4> Lamps => _lamps;

    /// <summary>
    /// Where the player starts (and comes back after dying): the police HQ's tech room, beside the navy portal, looking
    /// east through the doorway down the bullpen.
    /// </summary>
    public float3 SpawnPoint { get; private set; } = PolicePortalFloorPoint + new float3(2.5f, 0, 0);
    public float SpawnYaw { get; private set; } = float.Pi / 2;

    /// <summary>Where the player comes back after dying from now on (a factory raid: where they got in).</summary>
    public void SetSpawn(float3 point, float yaw) => (SpawnPoint, SpawnYaw) = (point, yaw);

    public const float HalfWidth = 20f;
    public const float HalfLength = 30f;
    public const float WallHeight = 10f;

    /// <summary>
    /// The blue portal: freestanding on the loading dock (x -18 .. -10, z -12 .. -4, 2 m up), its front facing south
    /// towards the steps you come up, with room to walk round behind it.
    /// </summary>
    public static float3 WarehousePortalFloorPoint => new(-14f, 2f, -9f);
    public static float3 WarehousePortalNormal => new(0, 0, 1);
    private const float WallThickness = 1f;

    // Base tints; the materials add the stains, rust and dirt.
    private static readonly float3 WallTint = new(0.62f, 0.60f, 0.55f);
    private static readonly float3 FloorTint = new(0.40f, 0.37f, 0.33f);
    private static readonly float3 ConcreteTint = new(0.50f, 0.48f, 0.44f);
    private static readonly float3 SteelTint = new(0.24f, 0.24f, 0.25f);
    private static readonly float3 RustTint = new(0.30f, 0.27f, 0.25f);
    private static readonly float3 WoodTint = new(0.46f, 0.34f, 0.22f);
    private static readonly float3 WindowTint = new(0.95f, 0.93f, 0.85f);
    private static readonly float3 DoorwayTint = new(1.0f, 0.95f, 0.82f);
    private static readonly float3 BulbTint = new(1.0f, 0.82f, 0.55f);

    public static readonly float3 StandColor = SteelTint;

    /// <param name="firingRangeOnly">
    /// Builds nothing but the firing range on its plain (BRUTALFPS_START=range): none of the other levels are loaded, and
    /// you start and respawn on the firing line.
    /// </param>
    /// <param name="factoryRaid">Builds only the factory with its grounds and the police HQ (a factory raid: see <see cref="FactoryRaid"/>).</param>
    public Level(bool firingRangeOnly = false, bool factoryRaid = false)
    {
        Build(firingRangeOnly, factoryRaid);
        SplitMergedMeshes();   // (the things in the merged meshes, one at a time for the editor: Game/LevelSplit.cs)
        AssignBuildIds(firingRangeOnly ? "range" : factoryRaid ? "raid" : "full");   // (for the level editor: Game/LevelEditing.cs)
        PaintDoubleWalls();   // (after: a wall cut round a coat of paint keeps its number, so its pieces move as one)
    }

    private void Build(bool firingRangeOnly, bool factoryRaid)
    {
        if (firingRangeOnly)
        {
            SpawnPoint = RangeFiringPoint;
            SpawnYaw = 0f;
            BuildOutback(firingRangeOnly: true);
            TagProps(Zones.Outback);
            return;
        }

        BuildShell();
        BuildRoof();
        BuildSideWalls();
        BuildNorthEnd();
        BuildLamps();
        BuildPlayArea();
        BuildFactoryPc();
        BuildJunk();
        BuildFactoryExterior();
        TagProps(Zones.Warehouse);
        if (factoryRaid)
        {
            BuildPolice();
            TagProps(Zones.Police);
            return;
        }

        BuildYard();
        TagProps(Zones.Yard);
        BuildOutback();
        TagProps(Zones.Outback);
        BuildCellar();
        TagProps(Zones.Cellar);
        BuildBunker();
        TagProps(Zones.Bunker);
        BuildArctic();
        TagProps(Zones.Arctic);
        BuildShip();
        TagProps(Zones.Ship);
        BuildJungle();
        TagProps(Zones.Jungle);
        BuildPolice();
        TagProps(Zones.Police);
        BuildPrison();
        TagProps(Zones.Prison);
        BuildFlats();
        TagProps(Zones.Flats);
        BuildRio();
        TagProps(Zones.Rio);
    }

    // ------------------------------------------------------------------------------------------------
    // Zones: which part of the world each box and prop belongs to, so only what can be seen gets drawn.
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// How far down the player (or a piece of debris) can fall at <paramref name="p"/> before it's lost: 30 m below the
    /// floor of the area it's in (the police station is a kilometre down).
    /// </summary>
    public static float KillHeightAt(float3 p) => (p.Y < RioBelowY ? RioOrigin.Y - 10f : p.Y < FlatsBelowY ? FlatsOrigin.Y : p.Y < PrisonBelowY ? PrisonOrigin.Y : p.Y < PoliceBelowY ? PoliceOrigin.Y : 0f) - 30f;

    /// <summary>
    /// The zone a point is in (by x, like the shader's areas; the cellar is a box of its own, the police station is
    /// everything a kilometre down, the prison everything two kilometres down, the flats everything three).
    /// </summary>
    public static Zones ZoneAt(float3 p)
    {
        if (p.Y < RioBelowY)
            return Zones.Rio;
        if (p.Y < FlatsBelowY)
            return Zones.Flats;
        if (p.Y < PrisonBelowY)
            return Zones.Prison;
        if (p.Y < PoliceBelowY)
            return Zones.Police;
        if (p.X >= CellarBounds.Min.X && p.X <= CellarBounds.Max.X && p.Y >= CellarBounds.Min.Y && p.Y <= CellarBounds.Max.Y
            && p.Z >= CellarBounds.Min.Z && p.Z <= CellarBounds.Max.Z)
            return Zones.Cellar;
        return p.X < 100 ? Zones.Warehouse : p.X < 1500 ? Zones.Yard : p.X < 4500 ? Zones.Outback
             : p.X < 5500 ? Zones.Bunker : p.X < 9000 ? Zones.Arctic : p.X < 16000 ? Zones.Ship : Zones.Jungle;
    }

    /// <summary>
    /// The zones visible from <paramref name="eye"/>: its own, and from the outback (or the tin shed over the cellar)
    /// both the outback and the cellar. Down in the cellar only the cellar is visible.
    /// </summary>
    public static Zones ZonesSeenFrom(float3 eye)
    {
        var zone = ZoneAt(eye);
        if (zone == Zones.Cellar)
            return eye.Y < CellarSurfaceY ? Zones.Cellar : Zones.Outback | Zones.Cellar;
        return zone == Zones.Outback ? Zones.Outback | Zones.Cellar : zone;
    }

    private int _taggedProps;

    /// <summary>Puts every prop added since the last call into <paramref name="zone"/>.</summary>
    private void TagProps(Zones zone)
    {
        for (var i = _taggedProps; i < _props.Count; i++)
            _props[i] = _props[i] with { Zone = zone };
        _taggedProps = _props.Count;
    }

    private const int ZoneCount = 12;
    private int _version;
    private int _drawVersion = -1;
    private readonly List<LevelBox>[] _zoneBoxes = [.. Enumerable.Range(0, ZoneCount).Select(_ => new List<LevelBox>())];
    private readonly List<(Prop Prop, Aabb Bounds)>[] _zoneProps = [.. Enumerable.Range(0, ZoneCount).Select(_ => new List<(Prop, Aabb)>())];

    private void RebuildDrawLists()
    {
        foreach (var list in _zoneBoxes)
            list.Clear();
        foreach (var list in _zoneProps)
            list.Clear();
        _yardContainerBoxes.Clear();
        _containerBoxes.Clear();   // (rebuilt below: the level editor rebuilds these lists every time it moves something)
        foreach (var box in _boxes)
        {
            if (!box.Visible)
                continue;
            if (InPrisonContainer(box.Bounds))
                _containerBoxes.Add(box);   // (it can move: see LevelPrisonContainer.cs)
            else
                _zoneBoxes[System.Numerics.BitOperations.Log2((uint)ZoneAt(box.Bounds.Center))].Add(box);
        }
        RebuildDrawBatches();   // (round pieces the level editor has moved)
        foreach (var prop in _props)
        {
            if (prop.Removed || prop.InBatch != 0)
                continue;   // (deleted in the level editor; drawn in its piece's batch)
            var bounds = (prop.Asset?.Bounds ?? prop.Mesh!.Bounds).Transformed(prop.Model);
            _zoneProps[System.Numerics.BitOperations.Log2((uint)(prop.Zone == Zones.None ? Zones.Warehouse : prop.Zone))].Add((prop, bounds));
        }
        _drawVersion = _version;
    }

    private readonly List<Ladder> _ladders = [];

    /// <summary>Ladders the player can climb.</summary>
    public List<Ladder> Ladders => _ladders;

    /// <summary>
    /// A steel ladder fixed to a wall. <paramref name="foot"/> is on the wall's face at the bottom of the ladder,
    /// <paramref name="facing"/> points into the wall (from the climber to the rungs), <paramref name="top"/> is the
    /// height of the floor at its top; the stiles carry on <paramref name="rails"/> metres higher as handholds.
    /// </summary>
    private void AddLadder(float3 foot, float3 facing, float top, float3 tint, float rails = 1.0f, float width = 0.5f, float standoff = 0.16f,
        float3? exit = null)
    {
        facing = float3.Normalize(facing);
        var across = float3.Normalize(float3.Cross(float3.UnitY, facing));   // along the wall
        var height = top - foot.Y;
        var builder = new MeshBuilder();
        // Boxes in ladder space (x across, y up, z out from the wall), turned into the world.
        void Bar(float3 center, float3 size)
        {
            var c = foot + across * center.X + float3.UnitY * center.Y - facing * center.Z;
            var s = float3.Abs(across * size.X) + new float3(0, size.Y, 0) + float3.Abs(facing * size.Z);
            builder.Box(c, s);
        }
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            Bar(new float3(side * width / 2, (height + rails) / 2, standoff), new float3(0.06f, height + rails, 0.07f));
            // Brackets back to the wall every couple of metres.
            for (var y = 0.6f; y < height; y += 2.2f)
                Bar(new float3(side * width / 2, y, standoff / 2), new float3(0.05f, 0.05f, standoff));
        }
        for (var y = 0.3f; y < height + rails - 0.1f; y += 0.3f)
            Bar(new float3(0, y, standoff), new float3(width, 0.03f, 0.03f));
        _props.Add(new Prop(builder.Build(), float4x4.Identity, tint, Material.Steel));
        var prop = _props.Count - 1;

        // The rungs stop the player; the climbing volume is the space in front of them.
        var near = foot - facing * (standoff + 0.04f);
        AddStatic(BoxBetween(foot - across * (width / 2 + 0.04f), near + across * (width / 2 + 0.04f) + float3.UnitY * height), default, visible: false, blocksShots: false);
        var volume = BoxBetween(near - across * (width / 2 + 0.15f) - facing * 0.75f, near + across * (width / 2 + 0.15f) + float3.UnitY * (height + 0.9f));
        _ladders.Add(new Ladder(volume, facing, top, exit ?? facing, prop));
    }

    /// <summary>The box spanning two opposite corners (in any order).</summary>
    private static Aabb BoxBetween(float3 a, float3 b) => new(float3.Min(a, b), float3.Max(a, b));

    /// <summary>A box given by its min and max corners.</summary>
    private void AddMinMax(float3 min, float3 max, float3 tint, Material material, bool blocksShots = true, bool collides = true)
    {
        _boxes.Add(new LevelBox(new Aabb(min, max), tint, material, BlocksShots: blocksShots, Collides: collides));
        _version++;
    }

    // ------------------------------------------------------------------------------------------------
    // Container yard: a separate outdoor area, reached only through the orange portal.
    // ------------------------------------------------------------------------------------------------

    /// <summary>Where the yard is in the world (far from the warehouse; the shader treats x &gt; 100 as outdoors).</summary>
    // (It stands well east of x = 100, where the shader's container-yard area begins: the water off its quay runs ~250 m
    // west to the far bank of the dock (LevelYardHarbour.cs), and all of that has to be in the yard's area too.)
    public static readonly float3 YardOrigin = new(520, 0, 0);

    // A 40 ft container: 12.19 x 2.44 x 2.59 m outside, about 2.35 x 2.39 m inside.
    private const float ContainerLength = 12.19f;
    private const float ContainerWidth = 2.44f;
    private const float ContainerHeight = 2.59f;
    // A 20 ft container (the container_20 model): 6.06 m long by the standard, a hair shorter here so three of them fit
    // end to end between the yard's north and south rows (18.16 m apart).
    private const float Container20Length = 6.05f;
    /// <summary>The middle of the yard's inner rows (either side), against the inside of the north and south rows (at 10.3).</summary>
    public const float YardInnerRowZ = 10.3f - ContainerWidth;
    private const float InnerHalfWidth = 1.17f;
    private const float InnerHeight = 2.39f;

    // The open container the orange portal leads into: it runs along +X, closed end at the west, doors at the east.
    private static readonly float ContainerWestX = YardOrigin.X - 6.1f;
    private static readonly float ContainerEastX = ContainerWestX + ContainerLength;
    private const float EndWallThickness = 0.1f;

    /// <summary>The orange portal sits on the inside face of the container's closed end, facing the doors.</summary>
    public static float3 ContainerPortalFloorPoint => new(ContainerWestX + EndWallThickness, 0, YardOrigin.Z);
    public static float3 ContainerPortalNormal => new(1, 0, 0);

    /// <summary>The orange portal stands this much higher than a portal usually does (<see cref="Portal.Lift"/>), so its foot isn't sunk in the floor.</summary>
    public const float ContainerPortalLift = 0.1f;

    /// <summary>The dim inside of the open container (the shader lights it mostly from the open doors).</summary>
    public static Aabb ContainerInterior => new(
        new float3(ContainerWestX + EndWallThickness, 0, YardOrigin.Z - InnerHalfWidth),
        new float3(ContainerEastX, InnerHeight, YardOrigin.Z + InnerHalfWidth));

    public static readonly float3 ContainerInteriorTint = new(0.66f, 0.66f, 0.63f);
    private static readonly float3 GravelTint = new(0.40f, 0.39f, 0.37f);

    // Weathered paint colours for the stacks.
    private static readonly float3[] ContainerColors =
    [
        new(0.55f, 0.18f, 0.12f), // oxide red
        new(0.72f, 0.36f, 0.12f), // orange
        new(0.74f, 0.60f, 0.14f), // mustard yellow
        new(0.20f, 0.42f, 0.24f), // green
        new(0.16f, 0.28f, 0.50f), // blue
        new(0.42f, 0.44f, 0.46f), // grey
        new(0.75f, 0.74f, 0.70f), // off-white
    ];

    private readonly List<Prop> _props = [];
    private Mesh? _drumMesh;

    /// <summary>A non-box object (drawn with its own mesh or a Blender model), with a box for collision.</summary>
    /// <param name="Decal">Lies flat on the ground: drawn with a depth bias so it never flickers against it.</param>
    /// <param name="Asset">A Blender model, drawn with <paramref name="Model"/> as its transform (Mesh is then null).</param>
    /// <param name="Part">Shading of a merged model mesh (e.g. all the bushes in one draw).</param>
    /// <param name="Arrives">The van that drives in on a factory raid: not drawn until it's there (<see cref="ArrivingVanHidden"/>), though it's solid all along.</param>
    /// <param name="Id">Non-zero for props the level editor can move (Game/LevelEditing.cs).</param>
    /// <param name="Removed">Deleted in the level editor: not drawn.</param>
    /// <param name="Piece">
    /// The object it's part of (a desk, a chair, a smoke alarm: see <see cref="Piece(string)"/>), which is drawn as one
    /// prop per material; 0 when it's not part of one. The level editor picks and moves a piece's props together.
    /// </param>
    /// <param name="InBatch">
    /// A piece's prop that's drawn (and is solid) as part of a merged mesh of every piece in that material, the
    /// <paramref name="BatchMesh"/> prop of <c>_drawBatches[InBatch - 1]</c>, rather than by itself: a room of desks then
    /// costs a draw per material, not one per desk. 0 once the editor has moved it (then it's drawn by itself).
    /// </param>
    /// <param name="BatchMesh">The merged mesh pieces are drawn in (see <paramref name="InBatch"/>): drawn, but not editable.</param>
    /// <param name="Painted">A Blender model's texture set and colour per part given by the level editor's paint tool, or null.</param>
    private readonly record struct Prop(Mesh? Mesh, float4x4 Model, float3 Tint, Material Material, bool Decal = false,
        Engine.Model? Asset = null, PartMaterial? Part = null, Zones Zone = Zones.None, bool Arrives = false, int Id = 0, bool Removed = false,
        int Piece = 0, int InBatch = 0, bool BatchMesh = false, SlotPaint? Painted = null);

    private static readonly HashSet<string> MissingModels = [];

    /// <summary>The Blender model Assets/Models/<paramref name="name"/>.glb, or null (then boxes stand in for it).</summary>
    private static Engine.Model? TryModel(string name)
    {
        if (MissingModels.Contains(name))
            return null;
        try
        {
            return Engine.Model.Get(name);
        }
        catch (Exception e)
        {
            Log.Write($"[WARN] Model '{name}' couldn't be loaded ({e.GetType().Name}: {e.Message}); using boxes instead.");
            MissingModels.Add(name);
            return null;
        }
    }

    private void AddModel(Engine.Model model, in float4x4 transform, float3 tint) =>
        _props.Add(new Prop(null, transform, tint, Material.Plain, Asset: model));

    /// <summary>Invisible collision (the model shows what's there).</summary>
    private void AddCollider(float3 center, float3 size, bool blocksShots = true) =>
        AddStatic(Aabb.FromCenterSize(center, size), default, visible: false, blocksShots: blocksShots);

    /// <summary>An oil drum standing on <paramref name="ground"/>, turned by <paramref name="yaw"/>.</summary>
    private void AddDrum(float3 ground, float3 tint, float yaw, Material material)
    {
        AddCollider(ground + new float3(0, 0.44f, 0), new float3(0.58f, 0.88f, 0.58f));
        if (TryModel("drum") is { } drum)
        {
            AddModel(drum, float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(ground), tint);
            return;
        }

        _drumMesh ??= new MeshBuilder().Cylinder(0.29f, -0.44f, 0.44f).Build();
        var model = float4x4.CreateRotationX(-float.Pi / 2) * float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(ground + new float3(0, 0.44f, 0));
        _props.Add(new Prop(_drumMesh, model, tint, material));
    }

    /// <summary>A rainwater tank on its timber stand, standing on <paramref name="ground"/>.</summary>
    private void AddTank(float3 ground, float3 tint)
    {
        AddCollider(ground + new float3(0, 0.3f, 0), new float3(1.8f, 0.6f, 1.8f));
        AddCollider(ground + new float3(0, 1.5f, 0), new float3(1.7f, 1.8f, 1.7f));
        if (TryModel("tank") is { } tank)
        {
            AddModel(tank, float4x4.CreateTranslation(ground), tint);
            return;
        }

        Add(ground + new float3(0, 0.3f, 0), new float3(1.8f, 0.6f, 1.8f), WoodTint * 0.7f, Material.Wood, collides: false);
        _tankMesh ??= new MeshBuilder().Cylinder(0.85f, -0.9f, 0.9f, 28).Build();
        _props.Add(new Prop(_tankMesh, float4x4.CreateRotationX(-float.Pi / 2) * float4x4.CreateTranslation(ground + new float3(0, 1.5f, 0)), tint, Material.Galvanized));
    }

    /// <summary>A wooden crate (a cube of <paramref name="size"/> metres) standing on <paramref name="ground"/>.</summary>
    private void AddCrate(float3 ground, float size, float3 tint, int quarterTurns = 0)
    {
        if (TryModel("crate") is { } crate)
        {
            AddCollider(ground + new float3(0, size / 2, 0), new float3(size));
            AddModel(crate, float4x4.CreateScale(size) * float4x4.CreateRotationY(quarterTurns * float.Pi / 2) * float4x4.CreateTranslation(ground), tint);
        }
        else
        {
            Add(ground + new float3(0, size / 2, 0), new float3(size), tint, Material.Wood);
        }
    }

    private void BuildYard()
    {
        var o = YardOrigin;
        var random = new Random(77);
        float3 RandomColor() => ContainerColors[random.Next(ContainerColors.Length)];

        // Ground: gravel and broken asphalt. Its top is at y = 0, the same height as the warehouse floor. It's the quay: it
        // runs on under the crane to the north, and west to the water's edge (YardQuayX); its sides go down to the sea.
        Add(o + new float3((YardQuayX + YardEastX) / 2, YardSeaLevel / 2, (YardSouthZ + YardNorthZ) / 2),
            new float3(YardEastX - YardQuayX, -YardSeaLevel, YardNorthZ - YardSouthZ), GravelTint, Material.Gravel);
        BuildQuayEdge();

        BuildOpenContainer();
        // A container stacked on top of the open one.
        AddContainer(new float3(o.X, ContainerHeight, o.Z), alongX: true, RandomColor(), doorsAtPositiveEnd: true);

        // West side: a row of containers beside the open one, their door ends facing the yard, stacked two high. The grey
        // one on the ground just south of the open one is hollow too: the orange portal's PC is in it (BuildYardPc).
        foreach (var dz in (ReadOnlySpan<float>)[-7.8f, -5.2f, -2.6f, 2.6f, 5.2f, 7.8f])
        {
            var color = RandomColor();   // (drawn even for the PC's container, so the rest keep their colours)
            if (dz == YardPcContainerDz)
                BuildHollowContainer(o.Z + dz, ContainerColors[5]);
            else
                AddContainer(new float3(o.X, 0, o.Z + dz), alongX: true, color, doorsAtPositiveEnd: true);
            AddContainer(new float3(o.X, ContainerHeight, o.Z + dz), alongX: true, RandomColor(), doorsAtPositiveEnd: true);
        }
        BuildYardPc();

        // A ladder up the door end of the container south of the PC's, to the top of the west block.
        AddLadder(new float3(o.X + ContainerLength / 2, 0, o.Z + YardLadderDz), new float3(-1, 0, 0), 2 * ContainerHeight, LadderTint,
            width: 0.6f, standoff: 0.2f);
        // A second one outside, up the middle of the back of the north row's west container from the lane beyond it, onto
        // the same block top: a way back in for anyone who's dropped off it onto the quay.
        AddLadder(new float3(o.X, 0, o.Z + 10.3f + ContainerWidth / 2), new float3(0, 0, -1), 2 * ContainerHeight, LadderTint,
            width: 0.6f, standoff: 0.2f);
        // Two more outside, from the lanes beyond the north and south rows (each two high): on the north, up the middle of
        // the row's middle container (in line with the grey one on the inner row, just west of the crane's engine house);
        // on the south, up the back of the row's east container, butting onto the east block's top.
        AddLadder(new float3(o.X + ContainerLength, 0, o.Z + 10.3f + ContainerWidth / 2), new float3(0, 0, -1),
            2 * ContainerHeight, LadderTint, width: 0.6f, standoff: 0.2f);
        AddLadder(new float3(o.X + 21.5f, 0, o.Z - (10.3f + ContainerWidth / 2)), new float3(0, 0, 1),
            2 * ContainerHeight, LadderTint, width: 0.6f, standoff: 0.2f);

        // Behind the open container: a row running north-south, so there's no way round the back. It stands 0.7 m
        // back, because right after you come out of the portal your body still reaches a little way behind it.
        foreach (var z in (ReadOnlySpan<float>)[-6.1f, 6.1f])
            for (var level = 0; level < 2; level++)
                AddContainer(new float3(ContainerWestX - ContainerWidth / 2 - 0.7f, level * ContainerHeight, o.Z + z), alongX: false, RandomColor(), doorsAtPositiveEnd: null);

        // North and south sides: rows running east-west, stacked two high; and inside each, a second row two high (z 6.64
        // .. 9.08 either side), from the west block's door end (x 6.1) to x 18.29, with a grey container on top of it, so
        // the sides step up: the yard floor, the inner row's top with the grey on it, the outer row's top behind. Between
        // the inner row's east end and the east block (x 18.29 .. 23.24) is an alcove off the yard, open to it.
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var z = o.Z + side * 10.3f;
            for (var i = 0; i < 3; i++)
                for (var level = 0; level < 2; level++)
                    AddContainer(new float3(o.X + i * ContainerLength, level * ContainerHeight, z), alongX: true, RandomColor(), doorsAtPositiveEnd: null);
            var inner = o.Z + side * YardInnerRowZ;
            for (var level = 0; level < 2; level++)
                AddContainer(new float3(o.X + ContainerLength, level * ContainerHeight, inner), alongX: true, RandomColor(), doorsAtPositiveEnd: null);
            AddContainer(new float3(o.X + ContainerLength, 2 * ContainerHeight, inner), alongX: true, ContainerColors[5], doorsAtPositiveEnd: true);
        }

        // East side: closes the yard. Three columns of 20 ft containers running north-south, end to end between the north
        // and south rows (z -9.08 .. 9.07) and side by side from the yard (x 23.24) to the east edge's stacks (x 30.56), so
        // there's no pit left between them: two high on the yard's side, stepping up to three against the four-high wall.
        // (The guards' posts on the yard side's top, Npcs.AddYardCrew, are at x 25.3: on the first column.)
        var eastInnerZ = 10.3f - ContainerWidth / 2;
        // (The east edge's stacks fill x 30.56 .. 33: the columns' centres are 24.46, 26.90 and 29.34.)
        ReadOnlySpan<int> eastHigh = [2, 2, 3];
        for (var col = 0; col < eastHigh.Length; col++)
        {
            var x = YardEastX - (3.5f - col) * ContainerWidth;
            for (var slot = 0; slot < 3; slot++)
            {
                var z = -eastInnerZ + (slot + 0.5f) * Container20Length;
                for (var level = 0; level < eastHigh[col]; level++)
                    AddContainer(new float3(o.X + x, level * ContainerHeight, o.Z + z), alongX: false, RandomColor(),
                        doorsAtPositiveEnd: (slot + col + level) % 2 == 0, length: Container20Length);
            }
        }

        // A lone container in the yard to use as cover: a 20 ft one now (z -2.0 .. 4.0), with room to walk round it between
        // the inner rows (whose faces are at z -6.64 and 6.64).
        AddContainer(new float3(o.X + 16.5f, 0, o.Z + 1.0f), alongX: false, ContainerColors[3], doorsAtPositiveEnd: false, length: Container20Length);

        // Oil drums and pallets.
        ReadOnlySpan<float3> drums = [new(10.2f, 0, -3.8f), new(10.8f, 0, -4.4f), new(10.0f, 0, -4.6f), new(21.5f, 0, -8.5f), new(20.4f, 0, 8.3f)];   // (the last two in the alcoves at the inner rows' east ends)
        for (var i = 0; i < drums.Length; i++)
        {
            var tint = i % 3 == 0 ? new float3(0.14f, 0.24f, 0.45f) : i % 3 == 1 ? new float3(0.50f, 0.12f, 0.08f) : new float3(0.30f, 0.32f, 0.20f);
            AddDrum(o + drums[i], tint, i * 1.3f, Material.PaintedMetal);
        }

        AddPallet(o + new float3(19, 0, -2), 4);
        AddPallet(o + new float3(8.5f, 0, 5.7f), 2);   // (against the north inner row)
        BuildYardCardTable();   // (past the pallets, up the lane by the east stacks: Game/LevelYardCards.cs)

        BuildYardRoofs();
        BuildYardBounds();
        BuildFarBank();
        BuildHinterland();   // (the rest of the terminal round it, for show: Game/LevelYardHinterland.cs)
        BuildYardNight();
        if (Environment.GetEnvironmentVariable("BRUTALFPS_CONTAINERCHECK") == "1")
            LogContainerOverlaps();
        _yardContainerBoxes.Clear();
    }

    // Every container placed round the yard (its box and where it came from), so a check can find any two that cut into
    // each other: BRUTALFPS_CONTAINERCHECK=1 logs each such pair ([CONTAINERS]) once the yard is built.
    private readonly List<(Aabb Box, string What)> _yardContainerBoxes = [];

    private void NoteContainer(float3 baseCenter, bool alongX, string what, float length = ContainerLength) =>
        _yardContainerBoxes.Add((Aabb.FromCenterSize(baseCenter + new float3(0, ContainerHeight / 2, 0),
            alongX ? new float3(length, ContainerHeight, ContainerWidth) : new float3(ContainerWidth, ContainerHeight, length)), what));

    private void LogContainerOverlaps()
    {
        const float slack = 0.02f;   // (touching, or a hair over from rounding, isn't cutting in)
        var boxes = _yardContainerBoxes.OrderBy(b => b.Box.Min.X).ToArray();
        var found = 0;
        for (var i = 0; i < boxes.Length; i++)
            for (var j = i + 1; j < boxes.Length && boxes[j].Box.Min.X < boxes[i].Box.Max.X - slack; j++)
            {
                var (a, b) = (boxes[i].Box, boxes[j].Box);
                var lo = float3.Max(a.Min, b.Min);
                var hi = float3.Min(a.Max, b.Max);
                var d = hi - lo;
                if (d.X > slack && d.Y > slack && d.Z > slack)
                {
                    found++;
                    Log.Write($"[CONTAINERS] {boxes[i].What} at {a.Center - YardOrigin} cuts into {boxes[j].What} at {b.Center - YardOrigin} by {d}");
                }
            }
        Log.Write($"[CONTAINERS] {boxes.Length} containers checked, {found} overlapping pairs");
    }

    /// <summary>
    /// The west block's top (the open container and the rows beside it, two high) as one floor for the guards posted up
    /// there: an invisible slab bridges the hand's-breadth gaps between its containers, and between it and the ends of the
    /// north and south rows. One ladder goes up to it (<see cref="YardLadderDz"/>), on the door end of the block.
    /// </summary>
    private void BuildYardRoofs()
    {
        var o = YardOrigin;
        var two = 2 * ContainerHeight;
        AddStatic(new Aabb(o + new float3(-ContainerLength / 2, two - 0.05f, -11.52f), o + new float3(ContainerLength / 2, two, 11.52f)), default, visible: false, blocksShots: false);
    }

    // ------------------------------------------------------------------------------------------------
    // Night in the yard: four high masts with sodium floodlights stand outside the stacks, aimed into the yard;
    // a gantry crane towers over the north stacks; taller stacks beyond the walls make the skyline.
    // ------------------------------------------------------------------------------------------------

    public const float MastHeight = 24f;
    // (In the lanes between the yard's stacks and the stacks round the quay's edges: on the quay to the north-west, by the
    // crane; in the lane east of the crane's stacks; in the lane south of the yard, at either end.)
    private static readonly float3[] MastFeet =
    [
        YardOrigin + new float3(1, 0, 18),
        YardOrigin + new float3(29, 0, 18.5f),
        YardOrigin + new float3(29, 0, -13.2f),
        YardOrigin + new float3(1, 0, -13.2f),
    ];

    // More masts: two at the water's end of the quay, west of the yard, and two across the port beyond the stacks that
    // light rows you can't see (glow only, no light on the yard).
    private static readonly float3[] DistantMastFeet =
    [
        YardOrigin + new float3(YardQuayX + 1.6f, 0, 30),
        YardOrigin + new float3(62, 0, 52),
        YardOrigin + new float3(78, 0, -46),
        YardOrigin + new float3(YardQuayX + 1.6f, 0, -6),
    ];

    // How strong each mast is (x FloodIntensity): a key light from the north-west, so the stacks still throw shadows
    // across the yard, and fills from the other corners strong enough that the yard's floor is well lit throughout.
    // (The tops of the stacks and the quay are kept dark by YardFloodReach instead.)
    private static readonly float[] MastStrength = [1.8f, 0.75f, 0.95f, 0.6f];

    /// <summary>
    /// How much of the masts' floodlight reaches <paramref name="p"/> (the shader's yardFloodReach, without its patches):
    /// their visors cut the light off above the stacks' second tier, so the tops of the stacks are in shade, and the
    /// quay along the water, west of the west block, only gets spill (and the pools under its own masts: <see cref="YardGlows"/>).
    /// </summary>
    public static float YardFloodReach(float3 p)
    {
        var q = p - YardOrigin;
        var up = SmoothStep(4.3f, 5.3f, q.Y);
        var quay = float.Lerp(0.15f, 1f, SmoothStep(-12f, -6.5f, q.X));
        return float.Lerp(1f, 0.1f, up) * quay;
    }

    private static float SmoothStep(float a, float b, float x)
    {
        var t = float.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>How far the two masts on the quay (<see cref="DistantMastFeet"/> 0 and 3) light the ground round them: pools of light with dark between.</summary>
    public const float QuayMastRange = 34f;

    /// <summary>The quay masts' lamp heads (they light the quay in pools, unshadowed).</summary>
    public static float3[] QuayMastLamps =>
        [.. new[] { DistantMastFeet[0], DistantMastFeet[3] }.Select(f => f + new float3(0, MastHeight - 0.55f, 0) + MastHeading(f) * 0.9f)];

    /// <summary>Where the masts' floodlights are aimed: the middle of the yard.</summary>
    private static float3 FloodAim => YardOrigin + new float3(14, 0, 0);

    /// <summary>High-pressure sodium: deep orange (linear).</summary>
    public static readonly float3 SodiumColor = new(1.0f, 0.50f, 0.13f);

    /// <summary>Each mast's floodlights together: irradiance at 1 m (game units).</summary>
    public static float FloodIntensity = 1800f;

    private static float3 MastHeading(float3 foot)
    {
        var d = FloodAim - foot;
        return float3.Normalize(new float3(d.X, 0, d.Z));
    }

    /// <summary>The masts' floodlights (one shadowed spot light per mast).</summary>
    public static FloodLight[] YardFloods => MastFeet
        .Select((f, i) => new FloodLight(f + new float3(0, MastHeight - 0.55f, 0) + MastHeading(f) * 0.9f, FloodAim, SodiumColor, FloodIntensity * MastStrength[i]))
        .ToArray();

    // The gantry crane straddles the rows north of the yard, its span running north-south: its legs either end of them
    // (z 15.85 and 39.35), its engine house to the south (z 12.9 .. 15.3), its stair up the north-west leg.
    private static readonly float3 CraneCenter = YardOrigin + new float3(15, 0, 27.6f);
    private static readonly float4x4 CraneTransform = float4x4.CreateRotationY(float.Pi / 2) * float4x4.CreateTranslation(CraneCenter);

    // Its lamps, in Blender coordinates (Tools/blender/build_port.py, LAMPS): floods under the girders, the lit cab,
    // the beacon on the machinery house.
    private static readonly (float3 Position, float Intensity, float3 Color, float Range)[] CraneLamps =
    [
        (new(-8.5f, -2.5f, 19.25f), 160f, SodiumColor, 14f), (new(-8.5f, 2.5f, 19.25f), 160f, SodiumColor, 14f),
        (new(0f, -2.5f, 19.25f), 160f, SodiumColor, 14f), (new(0f, 2.5f, 19.25f), 160f, SodiumColor, 14f),
        (new(8.5f, -2.5f, 19.25f), 160f, SodiumColor, 14f), (new(8.5f, 2.5f, 19.25f), 160f, SodiumColor, 14f),
        (new(5.5f, -3.3f, 18.2f), 25f, new(1.0f, 0.62f, 0.3f), 5f),
        (new(3.0f, 0f, 25.35f), 6f, new(1.0f, 0.15f, 0.05f), 0f),
    ];

    /// <summary>Lamps that glow in the night air: the mast heads, and the crane's lamps (which also light the crane).</summary>
    public static GlowPoint[] YardGlows
    {
        get
        {
            var glows = YardFloods.Select(f => new GlowPoint(f.Position, SodiumColor, f.Intensity, 0f)).ToList();
            foreach (var (p, intensity, color, range) in CraneLamps)
                glows.Add(new GlowPoint(float3.Transform(new float3(p.X, p.Z, -p.Y), CraneTransform), color, intensity, range));
            // (The two on the quay also light it, in pools: QuayMastRange.)
            for (var i = 0; i < DistantMastFeet.Length; i++)
            {
                var foot = DistantMastFeet[i];
                glows.Add(new GlowPoint(foot + new float3(0, MastHeight - 0.55f, 0) + MastHeading(foot) * 0.9f, SodiumColor, FloodIntensity,
                    i is 0 or 3 ? QuayMastRange : 0f));
            }
            return glows.ToArray();
        }
    }

    private void BuildYardNight()
    {
        var o = YardOrigin;
        if (TryModel("flood_mast") is { } mast)
        {
            foreach (var foot in MastFeet.Concat(DistantMastFeet))
            {
                var h = MastHeading(foot);
                var yaw = float.Atan2(-h.Z, h.X);  // the model's lamps face +X
                AddModel(mast, float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(foot), new float3(0.6f, 0.6f, 0.58f));
            }
        }
        else
        {
            foreach (var foot in MastFeet)
            {
                Add(foot + new float3(0, MastHeight / 2, 0), new float3(0.45f, MastHeight, 0.45f), new float3(0.5f, 0.5f, 0.5f), Material.Galvanized, collides: false);
                Add(foot + new float3(0, MastHeight - 0.5f, 0) + MastHeading(foot) * 0.9f, new float3(0.8f, 0.4f, 0.8f), TimeOfDay.IsNight(DayNightLevel.Yard) ? new float3(1.0f, 0.8f, 0.5f) : new float3(0.42f, 0.4f, 0.36f), Material.Emissive, collides: false);
            }
        }

        // The masts you can walk up to (all but the two out across the port) are solid. (Their ladders start out of reach.)
        foreach (var foot in MastFeet.Concat([DistantMastFeet[0], DistantMastFeet[3]]))
            AddCollider(foot + new float3(0, 11, 0), new float3(0.62f, 22, 0.62f));

        if (TryModel("gantry_crane") is { } crane)
            AddModel(crane, CraneTransform, new float3(0.86f, 0.64f, 0.06f));
        // The crane's south end is in the lane north of the yard: its legs, the sill between them, the engine house.
        // (The model's x is the game's -z here, its y the game's -x: see CraneTransform.)
        foreach (var end in (ReadOnlySpan<float>)[-11.75f, 11.75f])
        {
            var z = CraneCenter.Z + end;
            foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
                AddCollider(new float3(CraneCenter.X + side * 3.5f, 9.8f, z), new float3(1.4f, 19.6f, 0.9f));
            AddCollider(new float3(CraneCenter.X, 1.0f, z), new float3(9.7f, 2.0f, 1.3f));
        }
        AddCollider(new float3(CraneCenter.X, 3.3f, CraneCenter.Z - 13.5f), new float3(4.6f, 2.8f, 2.4f));

        // Stacks round the edges of the quay, three and four high, and under the crane: they show above the yard's own
        // stacks against the sky. All the same model, so they're merged into one mesh per colour and part. They're solid
        // too: you can get out of the yard (off the top of the west block onto the quay outside), and they wall you in
        // there, with lanes between them and the yard's stacks, and the open quay along the water to the west.
        var container = TryModel("container");
        var random = new Random(91);
        var byColor = new Dictionary<int, List<float4x4>>();
        void Stack(float x, float z, bool alongX, int height)
        {
            var footprint = alongX ? new float3(ContainerLength, 0, ContainerWidth) : new float3(ContainerWidth, 0, ContainerLength);
            var top = height * ContainerHeight;
            for (var level = 0; level < height; level++)
                NoteContainer(o + new float3(x, level * ContainerHeight, z), alongX, "quay stack");
            AddCollider(o + new float3(x, top / 2, z), new float3(footprint.X, top, footprint.Z));
            if (container is null)
                return;
            var yaw = alongX ? 0f : float.Pi / 2;
            for (var level = 0; level < height; level++)
            {
                var c = random.Next(ContainerColors.Length);
                if (!byColor.TryGetValue(c, out var list))
                    byColor[c] = list = [];
                list.Add(float4x4.CreateRotationY(yaw + (random.Next(2) == 0 ? 0 : float.Pi)) * float4x4.CreateTranslation(o + new float3(x, level * ContainerHeight, z)));
            }
        }
        // Under the crane, between its legs: two rows of eight, no gaps (z 16.58 .. 37.22, x 2.9 .. 27.5).
        foreach (var z in (ReadOnlySpan<float>)[17.8f, 20.4f, 23.0f, 25.6f, 28.2f, 30.8f, 33.4f, 36.0f])
            foreach (var x in (ReadOnlySpan<float>)[9.0f, 21.4f])
                Stack(x, z, alongX: true, 2 + random.Next(3));
        // Either side of the crane's north end, closing off the space round its stair.
        foreach (var x in (ReadOnlySpan<float>)[1.68f, 29.0f])
            Stack(x, 35.4f, alongX: false, 3);
        // Along the south edge of the quay (z -20 .. -14.94), from the water to the east edge.
        foreach (var z in (ReadOnlySpan<float>)[-16.16f, -18.78f])
            foreach (var x in (ReadOnlySpan<float>)[-22.8f, -10.4f, 2.0f, 14.4f, 26.8f])
                Stack(x, z, alongX: true, 3 + random.Next(2));
        // Along the north edge (z 41.56 .. 44).
        foreach (var x in (ReadOnlySpan<float>)[-22.8f, -10.4f, 2.0f, 14.4f, 26.8f])
            Stack(x, YardNorthZ - ContainerWidth / 2, alongX: true, 3 + random.Next(2));
        // Along the east edge (x 30.56 .. 33), from the south stacks to past the crane.
        foreach (var z in (ReadOnlySpan<float>)[-8.845f, 3.345f, 15.535f, 27.725f])
            Stack(YardEastX - ContainerWidth / 2, z, alongX: false, 4);
        // West of the yard, out on the quay: a narrow passage between them and the yard's back row.
        foreach (var z in (ReadOnlySpan<float>)[-6.1f, 6.1f])
            Stack(-11.5f, z, alongX: false, 3 + random.Next(2));
        if (container is null)
            return;
        foreach (var (c, transforms) in byColor)
            foreach (var (mesh, material) in Engine.Model.MergeInstances(container, transforms))
                _props.Add(new Prop(mesh, float4x4.Identity, ContainerColors[c], material.Material, Part: material));
    }

    // The quay the yard stands on (from the yard's origin): the water's edge to the west, past the portal's end of the
    // yard; stacks along its other three edges, so there's no seeing or walking off them.
    private const float YardQuayX = -28.9f, YardEastX = 33f, YardSouthZ = -20f, YardNorthZ = 44f;

    /// <summary>The sea off the quay (must match YARD_SEA_LEVEL in the shader, which draws it beyond the quay's edge).</summary>
    public const float YardSeaLevel = -2f;

    /// <summary>The quay's edge: a concrete face down to the water, a kerb along the top, bollards.</summary>
    private void BuildQuayEdge()
    {
        var o = YardOrigin;
        var concrete = new float3(0.52f, 0.51f, 0.48f);
        AddMinMax(o + new float3(YardQuayX - 0.15f, YardSeaLevel, YardSouthZ), o + new float3(YardQuayX, 0, YardNorthZ), concrete * 0.8f, Material.Concrete);
        AddMinMax(o + new float3(YardQuayX - 0.15f, 0, YardSouthZ), o + new float3(YardQuayX + 0.35f, 0.22f, YardNorthZ), concrete, Material.Concrete);
        for (var z = YardSouthZ + 9f; z < YardNorthZ - 4f; z += 9f)
            Add(o + new float3(YardQuayX + 0.75f, 0.3f, z), new float3(0.38f, 0.6f, 0.38f), new float3(0.12f, 0.12f, 0.11f), Material.PaintedMetal);
        BuildQuayLanding();   // (the ledge and ladders where the boat ties up on an Antwerp raid: Game/LevelAntwerpRaid.cs)
        // Under the water: something for thrown things to come to rest on (it's never seen).
        AddStatic(new Aabb(o + new float3(-100, YardSeaLevel - 0.3f, -100), o + new float3(YardQuayX, YardSeaLevel, 160)), default, visible: false, blocksShots: false);
    }

    /// <summary>
    /// Invisible walls round the edge of the quay, well above the tallest stack you can stand on (on the west, along the
    /// kerb at the water's edge). Off the top of the west block you can drop down outside the stacks, and the ground out
    /// there used to just stop: you walked off its edge and fell out of the world.
    /// </summary>
    private void BuildYardBounds()
    {
        var o = YardOrigin;
        const float x0 = YardQuayX + 0.35f, x1 = YardEastX, z0 = YardSouthZ, z1 = YardNorthZ, top = 30f, t = 1f;
        // The west side, along the kerb, but out round the Antwerp raid's landing ledge (Game/LevelAntwerpRaid.cs): you can
        // go down to it and back up its ladders, but no further.
        const float lz0 = QuayLandingZ - LedgeHalfLength, lz1 = QuayLandingZ + LedgeHalfLength, lx = YardQuayX - 0.15f - LedgeWidth;
        const float low = YardSeaLevel - 1f;
        AddStatic(new Aabb(o + new float3(x0 - t, -1, z0 - t), o + new float3(x0, top, lz0)), default, visible: false, blocksShots: false);
        AddStatic(new Aabb(o + new float3(x0 - t, -1, lz1), o + new float3(x0, top, z1 + t)), default, visible: false, blocksShots: false);
        AddStatic(new Aabb(o + new float3(lx - t, low, lz0 - t), o + new float3(lx, top, lz1 + t)), default, visible: false, blocksShots: false);
        AddStatic(new Aabb(o + new float3(lx - t, low, lz0 - t), o + new float3(x0, top, lz0)), default, visible: false, blocksShots: false);
        AddStatic(new Aabb(o + new float3(lx - t, low, lz1), o + new float3(x0, top, lz1 + t)), default, visible: false, blocksShots: false);
        AddStatic(new Aabb(o + new float3(x1, -1, z0 - t), o + new float3(x1 + t, top, z1 + t)), default, visible: false, blocksShots: false);
        AddStatic(new Aabb(o + new float3(x0, -1, z0 - t), o + new float3(x1, top, z0)), default, visible: false, blocksShots: false);
        AddStatic(new Aabb(o + new float3(x0, -1, z1), o + new float3(x1, top, z1 + t)), default, visible: false, blocksShots: false);
    }

    /// <summary>The container the orange portal opens into.</summary>
    private void BuildOpenContainer()
    {
        var outer = RandomColorFor(YardOrigin);
        BuildHollowContainer(YardOrigin.Z, outer);
        // The portal cuts its doorway right through the closed end (PortalSystem.BuildDoorway), and from the narrow gap
        // behind the container you could see the back of it through the hole. A sheet over the outside of the end hides
        // it: just clear of the cut, and not solid (the body of someone stepping in reaches further back than this).
        Add(new float3(ContainerWestX - 0.04f, ContainerHeight / 2, YardOrigin.Z), new float3(0.03f, ContainerHeight, ContainerWidth),
            outer, Material.Corrugated, collides: false);
    }

    /// <summary>
    /// A hollow container in the yard's west row (the orange portal's, or its PC's): thin walls with a pale, grimy inside,
    /// a steel floor, and doors at the east end, the south one (-z) swung open square to it and the north one shut. (The
    /// orange portal cuts its own opening in its container's closed end.)
    /// </summary>
    private void BuildHollowContainer(float z, float3 outer)
    {
        var o = new float3(YardOrigin.X, YardOrigin.Y, z);
        NoteContainer(new float3((ContainerWestX + ContainerEastX) / 2, YardOrigin.Y, z), alongX: true, "hollow container");
        var west = ContainerWestX;
        var east = ContainerEastX;
        var midX = (west + east) / 2;
        var length = east - west;
        const float skin = 0.03f;

        // Floor (visual only: the yard ground underneath is what you stand on, so you can walk straight in and out).
        Add(new float3(midX, -0.045f, o.Z), new float3(length, 0.1f, ContainerWidth), new float3(0.27f, 0.27f, 0.27f), Material.Steel, collides: false);

        // Side walls: a pale inner skin and a painted outer skin.
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            Add(new float3(midX, InnerHeight / 2, o.Z + side * (InnerHalfWidth + skin / 2)), new float3(length, InnerHeight, skin), ContainerInteriorTint, Material.Corrugated);
            Add(new float3(midX, ContainerHeight / 2, o.Z + side * (ContainerWidth / 2 - skin / 2)), new float3(length, ContainerHeight, skin), outer, Material.Corrugated);
        }

        // Roof.
        Add(new float3(midX, InnerHeight + skin / 2, o.Z), new float3(length, skin, InnerHalfWidth * 2), ContainerInteriorTint, Material.Corrugated);
        Add(new float3(midX, ContainerHeight - 0.04f, o.Z), new float3(length, 0.08f, ContainerWidth), outer, Material.Corrugated);

        // Closed end wall (the portal is cut into this).
        Add(new float3(west + EndWallThickness * 0.75f, InnerHeight / 2, o.Z), new float3(EndWallThickness / 2, InnerHeight, InnerHalfWidth * 2), ContainerInteriorTint, Material.Corrugated);
        Add(new float3(west + EndWallThickness * 0.25f, ContainerHeight / 2, o.Z), new float3(EndWallThickness / 2, ContainerHeight, ContainerWidth), outer, Material.Corrugated);

        // Door end: corner posts, a header and a sill.
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            Add(new float3(east - 0.08f, ContainerHeight / 2, o.Z + side * (ContainerWidth / 2 - 0.08f)), new float3(0.16f, ContainerHeight, 0.16f), outer * 0.8f, Material.Steel);
        Add(new float3(east - 0.08f, InnerHeight + 0.1f, o.Z), new float3(0.16f, ContainerHeight - InnerHeight, ContainerWidth), outer * 0.8f, Material.Steel);

        // Right-hand door (seen from inside) shut; left-hand door swung open flat against the outside of the container.
        var doorWidth = InnerHalfWidth;
        Add(new float3(east - 0.03f, InnerHeight / 2, o.Z + doorWidth / 2), new float3(0.05f, InnerHeight, doorWidth), outer, Material.Corrugated);
        AddDoorBars(new float3(east, 0, o.Z + doorWidth / 2), alongZ: true, doorWidth, outer);
        Add(new float3(east + doorWidth / 2, InnerHeight / 2, o.Z - ContainerWidth / 2 - 0.05f), new float3(doorWidth, InnerHeight, 0.05f), outer, Material.Corrugated);
    }

    /// <summary>A solid container. <paramref name="doorsAtPositiveEnd"/>: which end gets door bars (null for none).</summary>
    private void AddContainer(float3 baseCenter, bool alongX, float3 color, bool? doorsAtPositiveEnd, float length = ContainerLength)
    {
        NoteContainer(baseCenter, alongX, "yard container", length);
        var size = alongX
            ? new float3(length, ContainerHeight, ContainerWidth)
            : new float3(ContainerWidth, ContainerHeight, length);
        // (With the purple portal back in the yard, the container it's cut into has to stay a box: CarveOpening can cut a
        // hole in a box, not a model. Add "&& !bounds.Overlaps(portalZone)" back to the condition below, with
        //     var bounds = Aabb.FromCenterSize(baseCenter + new float3(0, ContainerHeight / 2, 0), size);
        //     var portalZone = Aabb.FromCenterSize(YardPortalFloorPoint + new float3(0, 1, 0), new float3(4, 2.5f, 4));)
        if (TryModel(length < ContainerLength - 1f ? "container_20" : "container") is { } container)
        {
            // The model is built along +X with its doors at the +X end.
            AddCollider(baseCenter + new float3(0, ContainerHeight / 2, 0), size);
            var yaw = (alongX, doorsAtPositiveEnd ?? true) switch
            {
                (true, true) => 0f,
                (true, false) => float.Pi,
                (false, true) => -float.Pi / 2,   // +X turns to +Z
                (false, false) => float.Pi / 2,
            };
            AddModel(container, float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(baseCenter), color);
            return;
        }

        Add(baseCenter + new float3(0, ContainerHeight / 2, 0), size, color, Material.Corrugated);

        if (doorsAtPositiveEnd is not { } positive)
            return;

        var sign = positive ? 1f : -1f;
        if (alongX)
        {
            var x = baseCenter.X + sign * (length / 2 + 0.03f);
            AddDoorBars(new float3(x, baseCenter.Y, baseCenter.Z - ContainerWidth / 4), alongZ: true, ContainerWidth / 2, color);
            AddDoorBars(new float3(x, baseCenter.Y, baseCenter.Z + ContainerWidth / 4), alongZ: true, ContainerWidth / 2, color);
        }
        else
        {
            var z = baseCenter.Z + sign * (length / 2 + 0.03f);
            AddDoorBars(new float3(baseCenter.X - ContainerWidth / 4, baseCenter.Y, z), alongZ: false, ContainerWidth / 2, color);
            AddDoorBars(new float3(baseCenter.X + ContainerWidth / 4, baseCenter.Y, z), alongZ: false, ContainerWidth / 2, color);
        }
    }

    /// <summary>Two vertical locking bars and a horizontal rib across one container door.</summary>
    private void AddDoorBars(float3 doorBase, bool alongZ, float doorWidth, float3 color)
    {
        var across = alongZ ? new float3(0, 0, 1) : new float3(1, 0, 0);
        var barSize = alongZ ? new float3(0.05f, 2.3f, 0.05f) : new float3(0.05f, 2.3f, 0.05f);
        foreach (var t in (ReadOnlySpan<float>)[-0.22f, 0.22f])
            Add(doorBase + across * (t * doorWidth) + new float3(0, 1.25f, 0), barSize, color * 0.55f, Material.RustyMetal, collides: false);
        var rib = alongZ ? new float3(0.04f, 0.08f, doorWidth * 0.95f) : new float3(doorWidth * 0.95f, 0.08f, 0.04f);
        Add(doorBase + new float3(0, 2.2f, 0), rib, color * 0.7f, Material.Steel, collides: false);
        Add(doorBase + new float3(0, 0.3f, 0), rib, color * 0.7f, Material.Steel, collides: false);
    }

    private static float3 RandomColorFor(float3 seed) => ContainerColors[(int)float.Abs(seed.X * 7 + seed.Z * 13) % ContainerColors.Length];

    // ------------------------------------------------------------------------------------------------
    // Yard -> outback portal anchor, and the outback itself.
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Where the purple portal goes (out of the game for now; see PortalSystem): on the face of the south stack at the
    /// back of the yard's south-east pocket (between the lone container and the east stack), facing back into the yard.
    /// </summary>
    public static float3 YardPortalFloorPoint => YardOrigin + new float3(20.9f, 0, 10.3f - ContainerWidth / 2);
    public static float3 YardPortalNormal => new(0, 0, -1);

    // Sun shadows: one shadow map over the container yard, one over the area around the outback shack.
    public static readonly float3 YardSunDirection = new(0.35f, 0.85f, 0.4f);
    public static readonly float3 OutbackSunDirection = new(-0.45f, 0.72f, -0.4f);
    public static float3 YardShadowCenter => YardOrigin + new float3(2, 0, 12);   // (the whole quay: x -29 .. 33, z -20 .. 44)
    public const float YardShadowHalfExtent = 36f;
    public static float3 OutbackShadowCenter => OutbackOrigin + new float3(10, 0, 0);
    public const float OutbackShadowHalfExtent = 150f;

    /// <summary>Where the outback is in the world (the shader treats x &gt; 1500 as outback; the road runs along x = 3000).</summary>
    public static readonly float3 OutbackOrigin = new(3000, 0, 0);

    // The road runs north-south (along Z) through OutbackOrigin; the shack stands on its east side.
    private const float RoadWidth = 7f;
    private static readonly float3 ShackMin = OutbackOrigin + new float3(7f, 0, -2.5f);
    private static readonly float3 ShackSize = new(4f, 2.7f, 5f);
    private const float ShackWall = 0.1f;

    /// <summary>The yellow portal: on the shack's back (east) wall, facing away from the road into a fenced yard.</summary>
    public static float3 OutbackPortalFloorPoint => new(ShackMin.X + ShackSize.X, 0, ShackMin.Z + ShackSize.Z / 2);
    public static float3 OutbackPortalNormal => new(1, 0, 0);

    // The fenced yard behind the shack (x from the shack's back wall to FenceFarX, z between the fence sides).
    private static float FenceNearX => ShackMin.X + ShackSize.X;
    private static float FenceFarX => FenceNearX + 10f;
    private const float FenceHalfZ = 6f;
    private const float GateMinX = 3f, GateMaxX = 5f; // the gate gap in the south fence, measured from FenceNearX

    private static readonly float3 RedDirtTint = new(0.72f, 0.30f, 0.12f);
    private static readonly float3 RoadTint = new(0.92f, 0.52f, 0.27f);
    private static readonly float3 TinTint = new(0.55f, 0.55f, 0.53f);
    private static readonly float3 BushTint = new(0.50f, 0.56f, 0.36f);

    private void BuildOutback(bool firingRangeOnly = false)
    {
        var o = OutbackOrigin;

        // Endless red plain, and the dirt road running dead straight to the horizon in both directions.
        Add(o + new float3(0, -0.5f, 0), new float3(2400, 1, 2400), RedDirtTint, Material.RedDirt);
        // (The road itself is painted into the ground by the shader along x = OutbackOrigin.X; a separate thin road
        // slab on top of the ground flickered at a distance, because the two surfaces were too close together.)

        if (!firingRangeOnly)
        {
            BuildShack();
            BuildArmouredCar();   // (Game/LevelArmouredCar.cs)
            BuildRoadhouse();
        }
        BuildFiringRange();

        // Low, rounded hills and ridges on the horizon (the haze turns them hazy blue-brown).
        var random = new Random(4242);
        var hills = new MeshBuilder();
        for (var i = 0; i < 26; i++)
        {
            var side = i % 2 == 0 ? -1f : 1f;
            var distance = 380f + (float)random.NextDouble() * 220f;
            var z = ((float)random.NextDouble() * 2 - 1) * 650f;
            var radii = new float3(30 + (float)random.NextDouble() * 50, 5 + (float)random.NextDouble() * 16, 60 + (float)random.NextDouble() * 160);
            hills.Hill(o + new float3(side * distance, -radii.Y * 0.25f, z), radii, random);
        }
        _props.Add(new Prop(hills.Build(), float4x4.Identity, RedDirtTint * 0.85f, Material.RedDirt));

        // Scrub: one merged mesh of lumpy bushes scattered over the plain, thinning out with distance,
        // with a clear verge along the road.
        var bushes = new MeshBuilder();
        var shade = new MeshBuilder();
        for (var i = 0; i < 1500; i++)
        {
            float x, z;
            if (i < 500)
            {
                // A dense band of scrub lining both verges, for a few hundred metres either way.
                x = float.CopySign(RoadWidth / 2 + 3f + (float)random.NextDouble() * 18f, (float)random.NextDouble() - 0.5f);
                z = ((float)random.NextDouble() * 2 - 1) * 260f;
            }
            else
            {
                x = (float)random.NextDouble() * 2 - 1;
                x = float.Sign(x) * x * x * 260f;          // open plain, denser nearer the road
                z = ((float)random.NextDouble() * 2 - 1) * 320f;
            }

            if (float.Abs(x) < RoadWidth / 2 + 2.5f)
                continue;                                  // keep the road and its verge clear
            if (x > 3 && x < 24 && z > -10 && z < 17)
                continue;                                  // and the shack, its fenced yard and the 4WD by the road
            if (x > RoadhouseMinX && x < RoadhouseMaxX && z > RoadhouseMinZ && z < RoadhouseMaxZ)
                continue;                                  // and the roadhouse

            // Each bush is a cluster of small clumps; tall saltbush here and there, low spinifex mounds elsewhere.
            var tall = random.NextDouble() < (i < 500 ? 0.7 : 0.3);
            var size = tall ? 0.8f + (float)random.NextDouble() * 1.2f : 0.3f + (float)random.NextDouble() * 0.5f;
            // (For the Blender models: a saltbush or a spinifex mound, sized and turned at random.)
            var yaw = (float)random.NextDouble() * float.Tau;
            var squash = 0.8f + 0.4f * (float)random.NextDouble();
            var instance = float4x4.CreateScale(size, size * squash, size) * float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(o + new float3(x, 0, z));
            _bushInstances[tall ? (i % 2) : 2].Add(instance);
            // Lots of small clumps in a rough dome read as foliage; a few big ones read as rocks.
            var clumps = tall ? 9 + random.Next(7) : 3 + random.Next(3);
            for (var c = 0; c < clumps; c++)
            {
                var angle = (float)random.NextDouble() * float.Tau;
                var spread = float.Sqrt((float)random.NextDouble()) * size * 0.6f;
                var offset = new float3(float.Cos(angle) * spread, 0, float.Sin(angle) * spread);
                var r = size * (tall ? 0.2f + 0.16f * (float)random.NextDouble() : 0.3f + 0.15f * (float)random.NextDouble());
                var height = tall
                    ? size * (0.25f + 0.75f * (float)random.NextDouble()) * (1f - spread / (size * 0.9f))
                    : r * 0.4f;
                bushes.Blob(o + new float3(x, float.Max(height, r * 0.5f), z) + offset, new float3(r, r * 0.8f, r), random, 6, 4);
            }

            // Its shade on the ground. Near the shack the shadow map gives real shadows; further out (beyond the
            // shadow map), a soft dark patch cast away from the sun stands in for them.
            if (float.Abs(x - 10) < OutbackShadowHalfExtent - 5 && float.Abs(z) < OutbackShadowHalfExtent - 5)
                continue;
            var shadeCenter = o + new float3(x + size * 0.45f, 0.01f, z + size * 0.4f);
            shade.Blob(shadeCenter, new float3(size * 0.95f, 0.004f, size * 0.85f), random, 9, 3);
        }

        // Gum trees: a line of big eucalypts along a dry creek bed winding across the plain west of the road, and
        // scattered trees elsewhere, clear of the road, the shack and the roadhouse. Their trunks block the player.
        var treeModels = new[] { TryModel("gum_tree_a"), TryModel("gum_tree_b"), TryModel("gum_tree_c") };
        if (treeModels.All(m => m is not null))
        {
            var treeInstances = new List<float4x4>[] { [], [], [] };
            var placed = new List<(float X, float Z)>();
            for (var i = 0; i < 600 && placed.Count < 48; i++)
            {
                float x, z;
                if (i % 3 != 2)
                {
                    var t = (float)random.NextDouble();
                    z = -300f + t * 560f;
                    x = -55f - 30f * float.Sin(t * 5.2f) + ((float)random.NextDouble() * 2 - 1) * 9f;
                }
                else
                {
                    x = ((float)random.NextDouble() * 2 - 1) * 200f;
                    z = ((float)random.NextDouble() * 2 - 1) * 280f;
                }

                if (float.Abs(x) < RoadWidth / 2 + 10f)
                    continue;
                if (x > -6 && x < 32 && z > -20 && z < 20)
                    continue;
                if (x > RoadhouseMinX - 8 && x < RoadhouseMaxX + 8 && z > RoadhouseMinZ - 8 && z < RoadhouseMaxZ + 8)
                    continue;
                if (placed.Any(q => (q.X - x) * (q.X - x) + (q.Z - z) * (q.Z - z) < 11f * 11f))
                    continue;
                placed.Add((x, z));
                var variant = random.Next(3);
                var scale = 0.85f + (float)random.NextDouble() * 0.4f;
                var yaw = (float)random.NextDouble() * float.Tau;
                var at = o + new float3(x, 0, z);
                treeInstances[variant].Add(float4x4.CreateScale(scale) * float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(at));
                AddCollider(at + new float3(0, 2f, 0), new float3(0.7f * scale, 4f, 0.7f * scale));
                // Shade on the ground where the shadow map doesn't reach.
                if (!(float.Abs(x - 10) < OutbackShadowHalfExtent - 5 && float.Abs(z) < OutbackShadowHalfExtent - 5))
                    shade.Blob(at + new float3(2.5f * scale, 0.01f, 2.2f * scale), new float3(4.5f * scale, 0.004f, 4f * scale), random, 9, 3);
            }

            foreach (var (model, transforms) in treeModels.Zip(treeInstances))
                if (transforms.Count > 0)
                    foreach (var (mesh, material) in Engine.Model.MergeInstances(model!, transforms))
                        _props.Add(new Prop(mesh, float4x4.Identity, BushTint, material.Material, Part: material));
        }

        _props.Add(new Prop(shade.Build(), float4x4.Identity, new float3(0.16f, 0.06f, 0.03f), Material.Plain, Decal: true));

        // With the Blender scrub models, every bush is an instance of one of them, merged into one mesh per model part.
        var variants = new[] { TryModel("saltbush_a"), TryModel("saltbush_b"), TryModel("spinifex") };
        if (variants.All(v => v is not null))
        {
            foreach (var (variant, transforms) in variants.Zip(_bushInstances))
                foreach (var (mesh, material) in Engine.Model.MergeInstances(variant!, transforms))
                    _props.Add(new Prop(mesh, float4x4.Identity, BushTint, material.Material, Part: material));
        }
        else
        {
            _props.Add(new Prop(bushes.Build(), float4x4.Identity, BushTint, Material.Bush));
        }
    }

    /// <summary>
    /// A small corrugated-iron shack with a skillion roof, a shut door and a letterbox. Behind it, a yard enclosed by a
    /// tall corrugated-iron fence, with a rainwater tank and an open gate; the yellow portal is in the shack's back
    /// wall, facing into that yard.
    /// </summary>
    private void BuildShack()
    {
        var min = ShackMin;
        var size = ShackSize;
        var max = min + size;
        var t = ShackWall;
        var midZ = min.Z + size.Z / 2;

        // Walls (thin sheets of rusty tin). The east (back) wall gets the portal cut into it.
        Add(new float3(min.X + t / 2, size.Y / 2, midZ), new float3(t, size.Y, size.Z), TinTint, Material.Galvanized);
        Add(new float3(max.X - t / 2, size.Y / 2, midZ), new float3(t, size.Y, size.Z), TinTint * 0.9f, Material.Galvanized);
        Add(new float3(min.X + size.X / 2, size.Y / 2, min.Z + t / 2), new float3(size.X, size.Y, t), TinTint, Material.Galvanized);
        Add(new float3(min.X + size.X / 2, size.Y / 2, max.Z - t / 2), new float3(size.X, size.Y, t), TinTint, Material.Galvanized);
        Add(new float3(min.X + size.X / 2, 0.02f, midZ), new float3(size.X, 0.04f, size.Z), WoodTint * 0.7f, Material.Wood, collides: false);

        // Skillion roof, sloping down towards the back, overhanging on every side.
        _roofMesh ??= new MeshBuilder().Box(float3.Zero, new float3(1, 1, 1)).Build();
        var roof = float4x4.CreateScale(size.X + 1.2f, 0.06f, size.Z + 1.0f)
            * float4x4.CreateRotationZ(-0.1f)
            * float4x4.CreateTranslation(min.X + size.X / 2, size.Y + 0.12f, midZ);
        _props.Add(new Prop(_roofMesh, roof, TinTint * 0.8f, Material.Galvanized));
        Add(new float3(min.X + size.X / 2, size.Y + 0.05f, midZ), new float3(size.X, 0.1f, size.Z), WoodTint * 0.6f, Material.Wood, blocksShots: false, collides: false);

        // Door on the south wall (shut, weathered timber); small windows on the north wall and facing the road.
        Add(new float3(min.X + 1.6f, 1.0f, min.Z - 0.02f), new float3(0.9f, 2.0f, 0.05f), WoodTint * 0.8f, Material.Wood, collides: false);
        Add(new float3(min.X + 2.2f, 1.6f, max.Z + 0.02f), new float3(0.9f, 0.7f, 0.04f), new float3(0.08f, 0.09f, 0.1f), Material.Glass, collides: false);
        Add(new float3(min.X - 0.02f, 1.6f, midZ), new float3(0.04f, 0.7f, 1.1f), new float3(0.08f, 0.09f, 0.1f), Material.Glass, collides: false);

        BuildFencedYard();

        // Rainwater tank on a timber stand in the far corner of the fenced yard.
        AddTank(new float3(FenceFarX - 1.6f, 0, FenceHalfZ - 1.6f), TinTint * 1.05f);

        // A deep red 4WD parked on the verge just up the road from the letterbox.
        AddCar(OutbackOrigin + new float3(RoadWidth / 2 + 2.6f, 0, midZ + 12f), alongZ: true, new float3(0.42f, 0.06f, 0.05f), "wagon");

        // An old drum on a post by the road for a letterbox, lying on its side.
        var post = OutbackOrigin + new float3(RoadWidth / 2 + 1.2f, 0, midZ + 5f);
        Add(post + new float3(0, 0.55f, 0), new float3(0.1f, 1.1f, 0.1f), WoodTint * 0.7f, Material.Wood);
        var letterboxTint = new float3(0.55f, 0.16f, 0.08f);
        if (TryModel("drum") is { } drum)
        {
            AddModel(drum, float4x4.CreateTranslation(0, -0.44f, 0) * float4x4.CreateRotationX(float.Pi / 2) * float4x4.CreateScale(0.7f)
                * float4x4.CreateTranslation(post + new float3(0, 1.3f, 0)), letterboxTint);
        }
        else
        {
            _drumMesh ??= new MeshBuilder().Cylinder(0.29f, -0.44f, 0.44f).Build();
            _props.Add(new Prop(_drumMesh, float4x4.CreateScale(0.7f) * float4x4.CreateTranslation(post + new float3(0, 1.3f, 0)), letterboxTint, Material.PaintedMetal));
        }

        // A couple of rusty drums against the shack.
        AddDrum(new float3(max.X - 0.6f, 0, max.Z + 0.6f), new float3(0.30f, 0.32f, 0.20f), 0.4f, Material.RustyMetal);
        AddDrum(new float3(max.X - 1.3f, 0, max.Z + 0.55f), new float3(0.45f, 0.28f, 0.12f), 2.1f, Material.RustyMetal);
    }

    private const float FenceHeight = 2.2f; // taller than eye height (1.62 m): you can't see over it

    /// <summary>
    /// A tall corrugated-iron fence (like the shack's walls) around the yard behind the shack, on steel posts and
    /// rails, with an open corrugated gate in the south side.
    /// </summary>
    private void BuildFencedYard()
    {
        var near = FenceNearX;
        var far = FenceFarX;
        var hz = FenceHalfZ;
        var shackHalfZ = ShackSize.Z / 2;
        var cz = ShackMin.Z + shackHalfZ;

        AddFence(new float3(near, 0, cz + hz), new float3(far, 0, cz + hz));                          // north
        AddFence(new float3(far, 0, cz - hz), new float3(far, 0, cz + hz));                           // east
        AddFence(new float3(near, 0, cz - hz), new float3(near + GateMinX, 0, cz - hz));              // south, west of the gate
        AddFence(new float3(near + GateMaxX, 0, cz - hz), new float3(far, 0, cz - hz));               // south, east of the gate
        AddFence(new float3(near, 0, cz + shackHalfZ), new float3(near, 0, cz + hz));                 // from the shack's corners
        AddFence(new float3(near, 0, cz - hz), new float3(near, 0, cz - shackHalfZ));

        // Gateposts either side of the opening.
        var postTint = SteelTint * 1.4f;
        foreach (var x in (ReadOnlySpan<float>)[GateMinX, GateMaxX])
            Add(new float3(near + x, (FenceHeight + 0.1f) / 2, cz - hz), new float3(0.1f, FenceHeight + 0.1f, 0.1f), postTint, Material.RustyMetal);

        // The gate: a corrugated sheet in a steel frame, hinged on the west gatepost and swung wide open outwards (south).
        var hinge = new float3(near + GateMinX + 0.08f, 0, cz - hz - 0.06f);
        var length = GateMaxX - GateMinX - 0.15f;
        var gateZ = hinge.Z - length / 2;
        var height = FenceHeight - 0.1f;
        Add(new float3(hinge.X, 0.05f + height / 2, gateZ), new float3(0.04f, height - 0.1f, length - 0.08f), TinTint * 0.95f, Material.Galvanized);
        foreach (var y in (ReadOnlySpan<float>)[0.08f, 0.05f + height - 0.03f])
            Add(new float3(hinge.X + 0.03f, y, gateZ), new float3(0.05f, 0.06f, length), postTint, Material.Steel, collides: false);
        foreach (var dz in (ReadOnlySpan<float>)[0f, 1f])
            Add(new float3(hinge.X + 0.03f, 0.05f + height / 2, hinge.Z - dz * length), new float3(0.05f, height, 0.06f), postTint, Material.Steel, collides: false);
        _roofMesh ??= new MeshBuilder().Box(float3.Zero, new float3(1, 1, 1)).Build();
        var brace = float4x4.CreateScale(0.04f, 0.05f, float.Sqrt(length * length + height * height) * 0.92f)
            * float4x4.CreateRotationX(-float.Atan2(height, length))
            * float4x4.CreateTranslation(hinge.X + 0.04f, 0.05f + height / 2, gateZ);
        _props.Add(new Prop(_roofMesh, brace, postTint, Material.Steel));
    }

    /// <summary>A straight run of tall corrugated-iron fence between two ground points (along X or along Z).</summary>
    private void AddFence(float3 from, float3 to)
    {
        var delta = to - from;
        var length = float.Max(float.Abs(delta.X), float.Abs(delta.Z));
        var alongX = float.Abs(delta.X) > float.Abs(delta.Z);
        var mid = (from + to) / 2;
        var postTint = SteelTint * 1.4f;

        // The sheeting itself (solid: you can't see through it, over it, or walk through it).
        var sheet = alongX ? new float3(length, FenceHeight, 0.05f) : new float3(0.05f, FenceHeight, length);
        Add(mid + new float3(0, FenceHeight / 2, 0), sheet, TinTint * (0.9f + 0.1f * ((int)(mid.X + mid.Z) % 2)), Material.Galvanized);

        // Steel posts every 2.4 m and two rails along the inside face.
        var posts = (int)float.Ceiling(length / 2.4f);
        var inward = alongX ? new float3(0, 0, mid.Z > ShackMin.Z + ShackSize.Z / 2 ? -0.06f : 0.06f) : new float3(mid.X > FenceNearX + 0.5f ? -0.06f : 0.06f, 0, 0);
        for (var i = 0; i <= posts; i++)
        {
            var p = from + delta * (i / (float)posts) + inward;
            Add(p + new float3(0, (FenceHeight + 0.1f) / 2, 0), new float3(0.08f, FenceHeight + 0.1f, 0.08f), postTint, Material.RustyMetal, collides: false);
        }

        var rail = alongX ? new float3(length, 0.07f, 0.04f) : new float3(0.04f, 0.07f, length);
        Add(mid + inward + new float3(0, 0.4f, 0), rail, postTint, Material.Steel, collides: false);
        Add(mid + inward + new float3(0, FenceHeight - 0.25f, 0), rail, postTint, Material.Steel, collides: false);
    }

    // ------------------------------------------------------------------------------------------------
    // Roadhouse: an old fuel stop and motel a short walk north up the road from the shack.
    // ------------------------------------------------------------------------------------------------

    /// <summary>Roadhouse site, measured from <see cref="OutbackOrigin"/> (x = east of the road's centre line).</summary>
    private const float RoadhouseMinX = 3f, RoadhouseMaxX = 55f, RoadhouseMinZ = -135f, RoadhouseMaxZ = -65f;

    private static readonly float3 WeatheredTimber = new(0.52f, 0.47f, 0.40f);
    private static readonly float3 VerandahGreen = new(0.16f, 0.36f, 0.24f);
    private static readonly float3 MotelCream = new(0.80f, 0.74f, 0.60f);
    private static readonly float3 MotelTrimGreen = new(0.18f, 0.40f, 0.28f);
    private static readonly float3 DoorTeal = new(0.30f, 0.55f, 0.50f);
    private static readonly float3 DarkGlass = new(0.07f, 0.08f, 0.09f);
    private static readonly float3 PumpCream = new(0.86f, 0.84f, 0.76f);
    private static readonly float3 ForecourtTint = new(0.86f, 0.62f, 0.44f);

    private Mesh UnitBox => _roofMesh ??= new MeshBuilder().Box(float3.Zero, new float3(1, 1, 1)).Build();

    private void BuildRoadhouse()
    {
        var o = OutbackOrigin;
        float3 At(float x, float y, float z) => o + new float3(x, y, z);

        // Pale, packed-dirt forecourt (drawn as a ground decal so it never flickers against the ground).
        // (In pieces round the shed over the cellar, so it doesn't lie across the hatch in the shed's floor.)
        var courtMin = new float2(RoadhouseMinX + 4, RoadhouseMinZ + 4);
        var courtMax = new float2(RoadhouseMaxX - 2, RoadhouseMaxZ - 4);
        var shedMin = new float2(ShedMin.X - o.X - 0.1f, ShedMin.Z - o.Z - 0.1f);
        var shedMax = new float2(ShedMax.X - o.X + 0.1f, ShedMax.Z - o.Z + 0.1f);
        void Court(float2 min, float2 max)
        {
            if (max.X - min.X < 0.01f || max.Y - min.Y < 0.01f)
                return;
            var model = float4x4.CreateScale(max.X - min.X, 0.02f, max.Y - min.Y)
                * float4x4.CreateTranslation(At((min.X + max.X) / 2, 0.0f, (min.Y + max.Y) / 2));
            _props.Add(new Prop(UnitBox, model, ForecourtTint, Material.RedDirt, Decal: true));
        }
        // (Nor under the motel: a decal's depth bias grows as you look along it, so from a couple of metres off the
        // forecourt came up through the rooms' carpets and tiles, sunlit dirt and pebbles and all.)
        var motelMin = new float2(MotelX0 + 0.02f, MotelZ0 + 0.02f);
        var motelMax = new float2(MotelX0 + MotelDepth - 0.02f, MotelZ0 + MotelLength - 0.02f);
        var pieces = new List<(float2 Min, float2 Max)> { (courtMin, courtMax) };
        foreach (var (holeMin, holeMax) in ((float2, float2)[])[(shedMin, shedMax), (motelMin, motelMax)])
        {
            var next = new List<(float2 Min, float2 Max)>();
            foreach (var (min, max) in pieces)
            {
                if (holeMin.X >= max.X || holeMax.X <= min.X || holeMin.Y >= max.Y || holeMax.Y <= min.Y)
                {
                    next.Add((min, max));
                    continue;
                }
                var (z0, z1) = (float.Max(min.Y, holeMin.Y), float.Min(max.Y, holeMax.Y));
                next.Add((min, new float2(max.X, z0)));                                        // north of the hole
                next.Add((new float2(min.X, z1), max));                                        // south of it
                next.Add((new float2(min.X, z0), new float2(float.Min(max.X, holeMin.X), z1)));   // either side
                next.Add((new float2(float.Max(min.X, holeMax.X), z0), new float2(max.X, z1)));
            }
            pieces = next;
        }
        foreach (var (min, max) in pieces)
            Court(min, max);

        BuildStore(At(12, 0, -96), length: 8f, depth: 4f);
        BuildFuelIsland(At(7, 0, -92));
        BuildMotel();   // (Game/LevelMotel.cs: seven rooms, each with someone waiting behind the door)

        // Tin sheds out the back.
        AddShed(At(31, 0, -84), new float3(3.2f, 2.3f, 3.0f), TinTint * 0.95f);
        AddShed(At(37, 0, -91), new float3(2.6f, 2.1f, 2.4f), TinTint * 0.85f);
        // (The third shed, out at the north end past the motel, is the one over the cellar: see BuildCellar.)

        // Tall light pole by the road, and a flagpole by the store.
        if (TryModel("light_pole") is { } pole)
        {
            AddCollider(At(9, 3.25f, -86), new float3(0.2f, 6.5f, 0.2f));
            AddModel(pole, float4x4.CreateTranslation(At(9, 0, -86)), SteelTint * 1.6f); // its arm reaches out towards the road (-X)
        }
        else
        {
            Add(At(9, 3.25f, -86), new float3(0.14f, 6.5f, 0.14f), SteelTint * 1.6f, Material.Steel);
            Add(At(8.6f, 6.45f, -86), new float3(0.9f, 0.08f, 0.08f), SteelTint * 1.6f, Material.Steel, collides: false);
            Add(At(8.2f, 6.35f, -86), new float3(0.45f, 0.14f, 0.3f), new float3(0.6f, 0.6f, 0.58f), Material.PaintedMetal, collides: false);
        }
        Add(At(10, 3.75f, -101), new float3(0.08f, 7.5f, 0.08f), new float3(0.75f, 0.75f, 0.72f), Material.PaintedMetal);
        Add(At(10.05f, 7.0f, -100.45f), new float3(0.02f, 0.7f, 1.1f), new float3(0.72f, 0.14f, 0.1f), Material.Cloth, blocksShots: false, collides: false);

        // Red-and-white radio mast behind everything.
        const float mastHeight = 27f;
        for (var i = 0; i < 9; i++)
        {
            var colour = i % 2 == 0 ? new float3(0.78f, 0.16f, 0.12f) : new float3(0.85f, 0.85f, 0.82f);
            var y0 = i * mastHeight / 9;
            foreach (var (dx, dz) in (ReadOnlySpan<(float, float)>)[(-0.35f, -0.35f), (0.35f, -0.35f), (-0.35f, 0.35f), (0.35f, 0.35f)])
                Add(At(47 + dx, y0 + mastHeight / 18, -76 + dz), new float3(0.07f, mastHeight / 9, 0.07f), colour, Material.PaintedMetal, collides: false);
            Add(At(47, y0 + 0.05f, -76), new float3(0.8f, 0.05f, 0.8f), colour, Material.PaintedMetal, collides: false);
        }
        AddStatic(Aabb.FromCenterSize(At(47, mastHeight / 2, -76), new float3(0.85f, mastHeight, 0.85f)), default, visible: false);
        Add(At(47, mastHeight + 0.6f, -76), new float3(0.06f, 1.2f, 0.06f), new float3(0.85f, 0.85f, 0.82f), Material.PaintedMetal, collides: false);

        // A big weathered billboard facing the road (blank: the paint has long since faded).
        foreach (var z in (ReadOnlySpan<float>)[-75.5f, -70.5f])
            Add(At(34, 1.4f, z), new float3(0.18f, 2.8f, 0.18f), WeatheredTimber, Material.Wood);
        Add(At(33.9f, 3.6f, -73), new float3(0.12f, 1.8f, 6.0f), new float3(0.22f, 0.45f, 0.62f), Material.PaintedMetal);
        Add(At(33.82f, 3.6f, -73), new float3(0.04f, 0.35f, 5.4f), new float3(0.80f, 0.68f, 0.22f), Material.PaintedMetal, collides: false);

        // Parked vehicles, drums and a water tank.
        AddCar(At(17.5f, 0, -80), alongZ: true, new float3(0.86f, 0.86f, 0.83f), "wagon");   // pearl white 4WD
        AddCar(At(8.4f, 0, -106), alongZ: true, new float3(0.26f, 0.27f, 0.29f), "wagon");   // graphite 4WD
        var drumIndex = 0;
        foreach (var (x, z, tint) in (ReadOnlySpan<(float, float, float3)>)[(17.2f, -97.5f, new(0.16f, 0.26f, 0.46f)), (17.3f, -98.2f, new(0.45f, 0.28f, 0.12f)), (16.6f, -97.9f, new(0.30f, 0.32f, 0.20f))])
            AddDrum(At(x, 0, z), tint, 0.9f * drumIndex++, Material.RustyMetal);
        AddTank(At(18.2f, 0, -90.5f), TinTint);
    }

    /// <summary>
    /// The old store: weathered timber and corrugated-iron walls under a pitched tin roof, with a verandah on green
    /// posts along its road side. <paramref name="corner"/> is its road-side corner with the smallest Z, on the ground;
    /// the building runs <paramref name="length"/> along +Z.
    /// </summary>
    private void BuildStore(float3 corner, float length, float depth)
    {
        const float wallHeight = 2.6f, pitch = 0.42f, overhang = 0.35f;
        var minX = corner.X; var maxX = minX + depth;
        var minZ = corner.Z;
        var maxZ = minZ + length;
        var midZ = (minZ + maxZ) / 2;
        var midX = (minX + maxX) / 2;

        // Walls: timber boards on the road side and ends, tin on the back.
        Add(new float3(minX + 0.05f, wallHeight / 2, midZ), new float3(0.1f, wallHeight, length), WeatheredTimber, Material.Wood);
        Add(new float3(maxX - 0.05f, wallHeight / 2, midZ), new float3(0.1f, wallHeight, length), TinTint, Material.Galvanized);
        Add(new float3(midX, wallHeight / 2, minZ + 0.05f), new float3(depth, wallHeight, 0.1f), TinTint * 0.95f, Material.Galvanized);
        Add(new float3(midX, wallHeight / 2, maxZ - 0.05f), new float3(depth, wallHeight, 0.1f), WeatheredTimber * 0.95f, Material.Wood);

        // Door, windows and a bench on the road side.
        Add(new float3(minX - 0.02f, 1.0f, midZ + 1.0f), new float3(0.05f, 2.0f, 0.95f), WeatheredTimber * 0.7f, Material.Wood, collides: false);
        foreach (var z in (ReadOnlySpan<float>)[minZ + 1.4f, maxZ - 1.2f])
        {
            Add(new float3(minX - 0.02f, 1.55f, z), new float3(0.04f, 0.9f, 1.1f), DarkGlass, Material.Glass, collides: false);
            Add(new float3(minX - 0.05f, 1.55f, z), new float3(0.03f, 1.0f, 1.2f), WeatheredTimber * 0.6f, Material.Wood, collides: false);
        }
        Add(new float3(minX - 0.6f, 0.45f, midZ - 1.8f), new float3(0.45f, 0.06f, 1.8f), WoodTint * 0.8f, Material.Wood, collides: false);
        Add(new float3(minX - 0.6f, 0.22f, midZ - 1.8f), new float3(0.4f, 0.44f, 1.6f), WoodTint * 0.5f, Material.Wood, collides: false);

        // Pitched roof (ridge along the building's length) with triangular gable ends.
        var halfSpan = depth / 2 + overhang;
        var rise = halfSpan * float.Tan(pitch);
        var slopeLength = halfSpan / float.Cos(pitch);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var model = float4x4.CreateScale(slopeLength, 0.06f, length + overhang * 2)
                * float4x4.CreateRotationZ(-side * pitch)
                * float4x4.CreateTranslation(midX + side * halfSpan / 2, wallHeight + rise / 2 - overhang * float.Tan(pitch) + 0.03f, midZ);
            _props.Add(new Prop(UnitBox, model, TinTint * 0.85f, Material.Galvanized));
        }
        var gable = new MeshBuilder();
        foreach (var z in (ReadOnlySpan<float>)[minZ + 0.05f, maxZ - 0.05f])
        {
            var a = new float3(minX, wallHeight, z); var b = new float3(maxX, wallHeight, z); var c = new float3(midX, wallHeight + depth / 2 * float.Tan(pitch), z);
            gable.Triangle(a, b, c, new float3(0, 0, z < midZ ? -1 : 1));
            gable.Triangle(a, c, b, new float3(0, 0, z < midZ ? 1 : -1));
        }
        _props.Add(new Prop(gable.Build(), float4x4.Identity, WeatheredTimber, Material.Wood));

        // Verandah: a lean-to roof on green posts along the road side.
        const float verandahDepth = 2.6f;
        var verandah = float4x4.CreateScale(verandahDepth, 0.05f, length + 0.6f)
            * float4x4.CreateRotationZ(0.12f)
            * float4x4.CreateTranslation(minX - verandahDepth / 2, wallHeight - 0.2f, midZ);
        _props.Add(new Prop(UnitBox, verandah, TinTint * 0.9f, Material.Galvanized));
        var postX = minX - verandahDepth + 0.15f;
        for (var i = 0; i < 4; i++)
        {
            var z = minZ + 0.3f + i * (length - 0.6f) / 3;
            Add(new float3(postX, (wallHeight - 0.35f) / 2, z), new float3(0.12f, wallHeight - 0.35f, 0.12f), VerandahGreen, Material.PaintedMetal);
        }
        Add(new float3(postX, wallHeight - 0.4f, midZ), new float3(0.12f, 0.12f, length + 0.4f), VerandahGreen, Material.PaintedMetal, collides: false);
        Add(new float3(minX - verandahDepth / 2, 0.05f, midZ), new float3(verandahDepth, 0.1f, length), new float3(0.55f, 0.53f, 0.5f), Material.Concrete, collides: false);
    }

    /// <summary>A concrete island with two old fuel pumps (bodies, heads, hoses), on the road side of the store.</summary>
    private void BuildFuelIsland(float3 center)
    {
        Add(center + new float3(0, 0.1f, 0), new float3(1.2f, 0.2f, 4.2f), new float3(0.62f, 0.6f, 0.56f), Material.Concrete);
        foreach (var dz in (ReadOnlySpan<float>)[-1.1f, 1.1f])
        {
            var p = center + new float3(0, 0.2f, dz);
            if (TryModel("fuel_pump") is { } pump)
            {
                // The model faces -X (the road), standing on the island.
                AddCollider(p + new float3(0, 0.95f, 0), new float3(0.58f, 1.9f, 0.5f));
                AddModel(pump, float4x4.CreateTranslation(p), PumpCream);
                continue;
            }

            Add(p + new float3(0, 0.75f, 0), new float3(0.5f, 1.5f, 0.42f), PumpCream, Material.PaintedMetal);
            Add(p + new float3(0, 1.68f, 0), new float3(0.56f, 0.36f, 0.48f), PumpCream * 1.02f, Material.PaintedMetal, collides: false);
            Add(p + new float3(-0.26f, 1.68f, 0), new float3(0.02f, 0.22f, 0.3f), DarkGlass, Material.Glass, collides: false);
            Add(p + new float3(0, 1.88f, 0), new float3(0.58f, 0.06f, 0.5f), new float3(0.72f, 0.14f, 0.1f), Material.PaintedMetal, collides: false);
            Add(p + new float3(-0.26f, 1.1f, 0.12f), new float3(0.06f, 0.2f, 0.08f), DarkGlass * 2, Material.Steel, collides: false); // nozzle
            Add(p + new float3(-0.3f, 0.8f, 0.14f), new float3(0.035f, 0.9f, 0.035f), DarkGlass, Material.Polymer, collides: false);    // hose
        }
    }

    /// <summary>A small corrugated-tin shed with a lean-to roof and a door. <paramref name="center"/> is on the ground.</summary>
    private void AddShed(float3 center, float3 size, float3 tint)
    {
        Add(center + new float3(0, size.Y / 2, 0), size, tint, Material.Galvanized);
        var roof = float4x4.CreateScale(size.X + 0.5f, 0.05f, size.Z + 0.5f)
            * float4x4.CreateRotationZ(0.12f)
            * float4x4.CreateTranslation(center + new float3(0, size.Y + 0.1f, 0));
        _props.Add(new Prop(UnitBox, roof, tint * 0.85f, Material.Galvanized));
        Add(center + new float3(-size.X / 2 - 0.02f, 0.95f, 0), new float3(0.04f, 1.9f, 0.8f), WeatheredTimber * 0.8f, Material.Wood, collides: false);
    }

    /// <summary>
    /// A parked vehicle: <paramref name="kind"/> is "ute" (an old flat-deck ute) or "wagon" (a modern full-size 4WD).
    /// <paramref name="ground"/> is under its middle. Without the models, a simple box car stands in.
    /// </summary>
    private void AddCar(float3 ground, bool alongZ, float3 paint, string kind)
    {
        // Built along Z, then swapped if it runs along X.
        float3 S(float x, float y, float z) => alongZ ? new float3(x, y, z) : new float3(z, y, x);

        var ute = kind == "ute";
        const float length = 4.6f, width = 1.8f;
        if (TryModel(kind) is { } car)
        {
            // Models face -Z (bonnet first) and run along Z.
            if (ute)
            {
                AddCollider(ground + S(0, 0.65f, 0), S(width, 0.9f, length));
                AddCollider(ground + S(0, 1.25f, -0.5f), S(width * 0.92f, 0.6f, 1.7f));
            }
            else
            {
                // 4.98 x 1.98 m: the body down to the ground (no crawling under), then the cabin behind the windscreen.
                AddCollider(ground + S(0, 0.735f, 0), S(1.96f, 1.07f, 4.96f));
                AddCollider(ground + S(0, 1.57f, 0.8f), S(1.62f, 0.6f, 3.35f));
            }
            AddModel(car, (alongZ ? float4x4.Identity : float4x4.CreateRotationY(-float.Pi / 2)) * float4x4.CreateTranslation(ground), paint);
            return;
        }

        Add(ground + S(0, 0.65f, 0), S(width, 0.6f, length), paint, Material.PaintedMetal);
        var cabZ = ute ? -0.5f : 0.1f;
        var cabLength = ute ? 1.7f : 2.4f;
        Add(ground + S(0, 1.2f, cabZ), S(width * 0.92f, 0.55f, cabLength), paint, Material.PaintedMetal);
        Add(ground + S(0, 1.22f, cabZ), S(width * 0.94f, 0.42f, cabLength * 0.9f), DarkGlass, Material.Glass, collides: false);
        foreach (var z in (ReadOnlySpan<float>)[-length / 2 - 0.03f, length / 2 + 0.03f])
            Add(ground + S(0, 0.45f, z), S(width, 0.15f, 0.1f), new float3(0.5f, 0.5f, 0.5f), Material.Steel, collides: false);
        if (ute)
        {
            // Tray sides.
            foreach (var x in (ReadOnlySpan<float>)[-width / 2 + 0.05f, width / 2 - 0.05f])
                Add(ground + S(x, 1.12f, 1.25f), S(0.06f, 0.35f, 2.0f), paint * 0.9f, Material.PaintedMetal, collides: false);
        }

        _wheelMesh ??= new MeshBuilder().Cylinder(0.36f, -0.12f, 0.12f, 18).Build();
        foreach (var x in (ReadOnlySpan<float>)[-width / 2 + 0.05f, width / 2 - 0.05f])
            foreach (var z in (ReadOnlySpan<float>)[-1.4f, 1.4f])
            {
                var p = ground + S(x, 0.36f, z);
                var model = alongZ ? float4x4.CreateRotationY(float.Pi / 2) * float4x4.CreateTranslation(p) : float4x4.CreateTranslation(p);
                _props.Add(new Prop(_wheelMesh, model, new float3(0.07f, 0.07f, 0.07f), Material.Polymer));
            }
    }

    private Mesh? _wheelMesh;
    private readonly List<float4x4>[] _bushInstances = [[], [], []];

    private Mesh? _roofMesh;
    private Mesh? _tankMesh;

    private void BuildShell()
    {
        const float hw = HalfWidth, hl = HalfLength, h = WallHeight, t = WallThickness;

        // Floor (top at y = 0). It runs under the walls too, so there's ground inside portal openings.
        Add(new float3(0, -0.5f, 0), new float3(hw * 2 + 4, 1, hl * 2 + 4), FloorTint, Material.Floor);

        // Perimeter walls.
        Add(new float3(0, h / 2, -hl - t / 2), new float3(hw * 2 + 2, h, t), WallTint, Material.Concrete);
        Add(new float3(0, h / 2, hl + t / 2), new float3(hw * 2 + 2, h, t), WallTint, Material.Concrete);
        Add(new float3(-hw - t / 2, h / 2, 0), new float3(t, h, hl * 2), WallTint, Material.Concrete);
        Add(new float3(hw + t / 2, h / 2, 0), new float3(t, h, hl * 2), WallTint, Material.Concrete);

        // Roof slab.
        Add(new float3(0, h + 0.3f, 0), new float3(hw * 2 + 2, 0.6f, hl * 2 + 2), SteelTint * 0.6f, Material.Steel);
    }

    private void BuildRoof()
    {
        const float bottomChord = 8.4f, topChord = 9.75f;

        // Steel trusses across the hall every 6 m: two chords joined by vertical struts.
        for (var z = -24f; z <= 24f; z += 6f)
        {
            Add(new float3(0, bottomChord, z), new float3(HalfWidth * 2, 0.3f, 0.3f), RustTint, Material.RustyMetal);
            Add(new float3(0, topChord, z), new float3(HalfWidth * 2, 0.3f, 0.3f), RustTint, Material.RustyMetal);
            for (var x = -16f; x <= 16f; x += 4f)
                Add(new float3(x, (bottomChord + topChord) / 2, z), new float3(0.14f, topChord - bottomChord, 0.14f), RustTint, Material.RustyMetal);
        }

        // Purlins running the length of the hall.
        for (var x = -15f; x <= 15f; x += 10f)
            Add(new float3(x, 9.95f, 0), new float3(0.25f, 0.2f, HalfLength * 2), SteelTint, Material.Steel);
    }

    private void BuildSideWalls()
    {
        // Concrete piers. (They leave gaps where portals used to be: blue at z = 12 west, now on the loading dock, and
        // magenta at z = -14 east, now out of the game.)
        float[] pierZ = [-25f, -19f, -7f, 1f, 19f, 25f];
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var faceX = side * HalfWidth;
            foreach (var z in pierZ)
                Add(new float3(faceX - side * 0.6f, WallHeight / 2, z), new float3(1.2f, WallHeight, 1.4f), ConcreteTint, Material.Concrete);

            // Tall windows high up between the piers (bright, dirty glass), with a concrete sill.
            float[] windowZ = [-22f, -13f, -3f, 6f, 13f, 22f];
            foreach (var z in windowZ)
            {
                Add(new float3(faceX - side * 0.02f, 6.3f, z), new float3(0.04f, 5.0f, 3.0f), TimeOfDay.Daylight(DayNightLevel.Factory, WindowTint), Material.Window, blocksShots: false, collides: false);
                Add(new float3(faceX - side * 0.12f, 3.75f, z), new float3(0.24f, 0.12f, 3.3f), ConcreteTint, Material.Concrete, collides: false);
            }

            // Pipe runs along the wall, in front of the piers.
            var pipeX = faceX - side * 1.35f;
            Add(new float3(pipeX, side < 0 ? 4.6f : 7.2f, 0), new float3(0.22f, 0.22f, HalfLength * 2), RustTint, Material.RustyMetal, collides: false);
            Add(new float3(pipeX - side * 0.05f, side < 0 ? 5.0f : 7.6f, 0), new float3(0.14f, 0.14f, HalfLength * 2), SteelTint, Material.RustyMetal, collides: false);

            // A couple of downpipes.
            foreach (var z in (ReadOnlySpan<float>)[-7.75f, 19.75f])
                Add(new float3(faceX - side * 1.3f, 4f, z), new float3(0.12f, 8f, 0.12f), RustTint, Material.RustyMetal, collides: false);
        }
    }

    private void BuildNorthEnd()
    {
        var wallZ = -HalfLength;

        // The big sliding door's opening is in the middle of this wall (cut, and the door built, in LevelFactory.cs):
        // a steel frame round it on the inside.
        foreach (var x in (ReadOnlySpan<float>)[-FactoryDoorHalfWidth - 0.15f, FactoryDoorHalfWidth + 0.15f])
            Add(new float3(x, FactoryDoorHeight / 2, wallZ + 0.12f), new float3(0.3f, FactoryDoorHeight, 0.24f), SteelTint, Material.RustyMetal);
        Add(new float3(0, FactoryDoorHeight + 0.15f, wallZ + 0.12f), new float3(FactoryDoorHalfWidth * 2 + 0.6f, 0.3f, 0.24f), SteelTint, Material.RustyMetal);

        // Catwalk across the north end at 5.6 m (over the door), on steel legs, with a railing.
        const float deckY = 5.6f, front = -26.5f;
        Add(new float3(0, deckY, (wallZ + front) / 2), new float3(12f, 0.2f, front - wallZ), RustTint, Material.RustyMetal);
        foreach (var x in (ReadOnlySpan<float>)[-5.8f, 5.8f])
            Add(new float3(x, deckY / 2, front + 0.15f), new float3(0.2f, deckY, 0.2f), SteelTint, Material.Steel);
        // The railing, with a gap at the head of the ladder.
        const float ladderX = -5.3f, gap = 0.42f, railEnd = 6f;
        foreach (var (x0, x1) in (ReadOnlySpan<(float, float)>)[(-railEnd, ladderX - gap), (ladderX + gap, railEnd)])
        {
            Add(new float3((x0 + x1) / 2, deckY + 1.0f, front + 0.05f), new float3(x1 - x0, 0.06f, 0.06f), SteelTint, Material.Steel, collides: false);
            Add(new float3((x0 + x1) / 2, deckY + 0.5f, front + 0.05f), new float3(x1 - x0, 0.05f, 0.05f), SteelTint, Material.Steel, collides: false);
        }
        for (var x = -5.5f; x <= 5.5f; x += 1.1f)
            if (float.Abs(x - ladderX) > gap)
                Add(new float3(x, deckY + 0.55f, front + 0.05f), new float3(0.05f, 0.95f, 0.05f), SteelTint, Material.Steel, collides: false);
        foreach (var x in (ReadOnlySpan<float>)[ladderX - gap, ladderX + gap])
            Add(new float3(x, deckY + 0.55f, front + 0.05f), new float3(0.05f, 0.95f, 0.05f), SteelTint, Material.Steel, collides: false);

        // Ladder up to the catwalk: on the deck's front edge, stepping off through the gap.
        const float top = deckY + 0.1f, hoopZ = front + 0.95f;
        AddLadder(new float3(ladderX, 0, front), new float3(0, 0, -1), top, RustTint, width: 0.6f);
        // A safety hoop over the ladder's head. It's what stops you walking straight out over the hall from the deck: you
        // come up against it and drop onto the rungs, where W (looking down) or S climbs on down.
        foreach (var x in (ReadOnlySpan<float>)[ladderX - 0.3f, ladderX + 0.3f])
            Add(new float3(x, top + 0.97f, (front + hoopZ) / 2 + 0.08f), new float3(0.05f, 0.05f, hoopZ - front - 0.16f), RustTint, Material.Steel, collides: false);
        Add(new float3(ladderX, top + 0.97f, hoopZ), new float3(0.65f, 0.05f, 0.05f), RustTint, Material.Steel, collides: false);
        AddStatic(Aabb.FromCenterSize(new float3(ladderX, top + 1.1f, hoopZ), new float3(1.1f, 2.2f, 0.1f)), default, visible: false, blocksShots: false);
    }

    private void BuildLamps()
    {
        ReadOnlySpan<float2> spots = [new(-8, 18), new(8, 18), new(0, 6), new(-8, -6), new(8, -6), new(0, -18)];
        var lamp = TryModel("lamp");
        foreach (var spot in spots)
        {
            const float shadeY = 6.3f;
            if (lamp is not null)
            {
                // Shade centred at shadeY, cable running up to the truss at 8.25 m.
                AddModel(lamp, float4x4.CreateRotationY(spot.X * 0.37f + spot.Y * 0.11f) * float4x4.CreateTranslation(spot.X, shadeY, spot.Y), RustTint);
                _lamps.Add(new float4(spot.X, shadeY - 0.4f, spot.Y, 2.4f));
                continue;
            }

            Add(new float3(spot.X, (8.25f + shadeY) / 2, spot.Y), new float3(0.03f, 8.25f - shadeY, 0.03f), SteelTint, Material.Steel, collides: false);
            Add(new float3(spot.X, shadeY, spot.Y), new float3(0.7f, 0.22f, 0.7f), RustTint, Material.RustyMetal, collides: false);
            Add(new float3(spot.X, shadeY - 0.15f, spot.Y), new float3(0.22f, 0.1f, 0.22f), BulbTint, Material.Window, blocksShots: false, collides: false);
            _lamps.Add(new float4(spot.X, shadeY - 0.4f, spot.Y, 2.4f));
        }
    }

    private void BuildPlayArea()
    {
        // Cover crates near spawn (old wooden crates).
        AddCrate(new float3(-9, 0, 16), 1.2f, WoodTint);
        AddCrate(new float3(-7.8f, 0, 16.4f), 1.0f, WoodTint * 0.9f, 1);
        AddCrate(new float3(10, 0, 14), 1.5f, WoodTint, 2);
        AddCrate(new float3(10, 1.5f, 14), 0.8f, WoodTint * 1.1f, 3);

        // Structural columns in the middle of the hall (floor to roof).
        Add(new float3(6, WallHeight / 2, 0), new float3(1.2f, WallHeight, 1.2f), ConcreteTint, Material.Concrete);
        Add(new float3(12, WallHeight / 2, -6), new float3(1.2f, WallHeight, 1.2f), ConcreteTint, Material.Concrete);
        Add(new float3(-4, WallHeight / 2, -14), new float3(1.2f, WallHeight, 1.2f), ConcreteTint, Material.Concrete);

        // Concrete loading dock on the west side, reached by vaulting up the steps (each step is low enough to climb onto).
        Add(new float3(-14, 1.0f, -8), new float3(8, 2, 8), ConcreteTint * 0.9f, Material.Concrete);
        Add(new float3(-14, 0.35f, -2.75f), new float3(3, 0.7f, 2.5f), ConcreteTint * 0.85f, Material.Concrete);
        Add(new float3(-14, 0.7f, -3.5f), new float3(3, 1.4f, 1.0f), ConcreteTint * 0.85f, Material.Concrete);
        Add(new float3(-14, 2.03f, -4.05f), new float3(8, 0.06f, 0.1f), SteelTint, Material.RustyMetal, collides: false); // steel nosing on the dock edge
    }

    private void BuildJunk()
    {
        // Open-topped rusty bins on the east side.
        AddBin(new float3(15.5f, 0, 6), new float3(1.8f, 1.1f, 1.3f));
        AddBin(new float3(15.2f, 0, 2.6f), new float3(1.6f, 1.0f, 1.3f));
        AddBin(new float3(-15.5f, 0, -20f), new float3(1.6f, 1.0f, 1.3f));

        // Pallets (stacked slats) and an old box.
        AddPallet(new float3(-12, 0, 21), 3);
        AddPallet(new float3(4, 0, -21), 2);
        AddPallet(new float3(14, 0, 22), 1);
        Add(new float3(-2.5f, 0.15f, 12.5f), new float3(0.9f, 0.3f, 0.6f), WoodTint * 0.8f, Material.Wood);

        // Scattered rubble and scraps you walk over (deterministic, so it's the same every run).
        var random = new Random(1234);
        for (var i = 0; i < 70; i++)
        {
            var x = (float)(random.NextDouble() * 2 - 1) * (HalfWidth - 3);
            var z = (float)(random.NextDouble() * 2 - 1) * (HalfLength - 3);
            if (float.Abs(x) < 3 && z > 18) // keep the spawn area clear
                continue;

            var size = new float3(
                0.08f + (float)random.NextDouble() * 0.35f,
                0.03f + (float)random.NextDouble() * 0.08f,
                0.08f + (float)random.NextDouble() * 0.35f);
            var kind = random.Next(3);
            var (tint, material) = kind switch
            {
                0 => (ConcreteTint * 0.9f, Material.Concrete),
                1 => (RustTint, Material.RustyMetal),
                _ => (WoodTint * 0.8f, Material.Wood),
            };
            Add(new float3(x, size.Y / 2, z), size, tint, material, blocksShots: false, collides: false);
        }
    }

    private void AddBin(float3 groundCenter, float3 size)
    {
        const float wall = 0.06f;
        var c = groundCenter;
        if (TryModel("bin") is { } bin)
        {
            // Model is 1.6 x 1.0 x 1.3 m; the walls stay solid for collision.
            AddCollider(c + new float3(0, 0.05f, 0), new float3(size.X, 0.1f, size.Z));
            AddCollider(c + new float3(0, size.Y / 2, size.Z / 2 - wall / 2), new float3(size.X, size.Y, wall));
            AddCollider(c + new float3(0, size.Y / 2, -size.Z / 2 + wall / 2), new float3(size.X, size.Y, wall));
            AddCollider(c + new float3(size.X / 2 - wall / 2, size.Y / 2, 0), new float3(wall, size.Y, size.Z));
            AddCollider(c + new float3(-size.X / 2 + wall / 2, size.Y / 2, 0), new float3(wall, size.Y, size.Z));
            AddModel(bin, float4x4.CreateScale(size.X / 1.6f, size.Y / 1.0f, size.Z / 1.3f) * float4x4.CreateTranslation(c), RustTint);
            return;
        }

        Add(c + new float3(0, 0.05f, 0), new float3(size.X, 0.1f, size.Z), RustTint, Material.RustyMetal);
        Add(c + new float3(0, size.Y / 2, size.Z / 2 - wall / 2), new float3(size.X, size.Y, wall), RustTint, Material.RustyMetal);
        Add(c + new float3(0, size.Y / 2, -size.Z / 2 + wall / 2), new float3(size.X, size.Y, wall), RustTint, Material.RustyMetal);
        Add(c + new float3(size.X / 2 - wall / 2, size.Y / 2, 0), new float3(wall, size.Y, size.Z), RustTint, Material.RustyMetal);
        Add(c + new float3(-size.X / 2 + wall / 2, size.Y / 2, 0), new float3(wall, size.Y, size.Z), RustTint, Material.RustyMetal);
    }

    private void AddPallet(float3 groundCenter, int stack)
    {
        if (TryModel("pallet") is { } pallet)
        {
            for (var level = 0; level < stack; level++)
            {
                var y = groundCenter.Y + level * 0.15f;
                AddCollider(new float3(groundCenter.X, y + 0.075f, groundCenter.Z), new float3(1.2f, 0.15f, 1.0f));
                // Each one a little askew, as pallets get stacked.
                var turn = ((level * 7 + (int)groundCenter.X * 3) % 5 - 2) * 0.02f;
                AddModel(pallet, float4x4.CreateRotationY(turn) * float4x4.CreateTranslation(groundCenter.X, y, groundCenter.Z), WoodTint * 0.8f);
            }

            return;
        }

        for (var level = 0; level < stack; level++)
        {
            var y = groundCenter.Y + level * 0.15f;
            Add(new float3(groundCenter.X, y + 0.05f, groundCenter.Z), new float3(1.2f, 0.1f, 1.0f), WoodTint * 0.75f, Material.Wood);
            Add(new float3(groundCenter.X, y + 0.125f, groundCenter.Z), new float3(1.2f, 0.05f, 1.0f), WoodTint * 0.85f, Material.Wood);
        }
    }

    private void Add(float3 center, float3 size, float3 tint, Material material, bool blocksShots = true, bool collides = true)
    {
        _boxes.Add(new LevelBox(Aabb.FromCenterSize(center, size), tint, material, BlocksShots: blocksShots, Collides: collides));
        _version++;
    }

    /// <summary>Adds extra static geometry (e.g. target stands, portal collision) that is drawn and collided like the level.</summary>
    public void AddStatic(
        Aabb bounds,
        float3 color,
        Material material = Material.Plain,
        bool visible = true,
        bool blocksShots = true,
        bool collides = true)
    {
        _boxes.Add(new LevelBox(bounds, color, material, visible, blocksShots, collides));
        _version++;
    }

    /// <summary>
    /// Cuts a hole through every box that <paramref name="opening"/> overlaps (used for portal doorways).
    /// Each affected box is replaced by up to six boxes surrounding the hole.
    /// </summary>
    public void CarveOpening(Aabb opening)
    {
        _version++;
        for (var i = _boxes.Count - 1; i >= 0; i--)
        {
            var box = _boxes[i];
            if (!box.Bounds.Overlaps(opening))
                continue;

            _boxes.RemoveAt(i);

            var min = box.Bounds.Min;
            var max = box.Bounds.Max;
            var holeMin = float3.Max(min, opening.Min);
            var holeMax = float3.Min(max, opening.Max);

            // Slice off the parts outside the hole one axis at a time, shrinking the remainder each time.
            for (var axis = 0; axis < 3; axis++)
            {
                if (holeMin[axis] > min[axis])
                {
                    var pieceMax = max;
                    pieceMax[axis] = holeMin[axis];
                    _boxes.Add(box with { Bounds = new Aabb(min, pieceMax) });
                }

                if (holeMax[axis] < max[axis])
                {
                    var pieceMin = min;
                    pieceMin[axis] = holeMax[axis];
                    _boxes.Add(box with { Bounds = new Aabb(pieceMin, max) });
                }

                min[axis] = holeMin[axis];
                max[axis] = holeMax[axis];
            }
        }
    }

    /// <summary>Casts a sphere of <paramref name="radius"/> (0 for a ray) and returns the nearest hit.</summary>
    public bool Raycast(float3 origin, float3 direction, float maxDistance, float radius, out float distance, out float3 normal)
    {
        distance = maxDistance;
        normal = default;
        var hit = false;

        foreach (var box in _boxes)
        {
            if (!box.BlocksShots)
                continue;

            var bounds = radius > 0 ? box.Bounds.Expand(radius) : box.Bounds;
            if (bounds.Raycast(origin, direction, distance, out var t, out var n) && t <= distance)
            {
                distance = t;
                normal = n;
                hit = true;
            }
        }

        if (RaycastDoors(origin, direction, ref distance, out var doorNormal, out _))
        {
            normal = doorNormal;
            hit = true;
        }

        return hit;
    }

    /// <summary>
    /// How hard each metre of a box's material is for a heavy bullet to get through, against brick's 1: wood and glass
    /// barely slow it, steel plate is many times harder.
    /// </summary>
    private static float Toughness(Material material) => material switch
    {
        Material.Wood or Material.Bark or Material.Veneer => 0.2f,
        Material.Window or Material.Glass or Material.CeilingTile => 0.05f,
        Material.Corrugated or Material.Galvanized => 2f,
        Material.Concrete or Material.Floor => 1.2f,
        Material.Rock => 1.6f,
        Material.Steel or Material.RustyMetal or Material.PaintedMetal or Material.Gunmetal or Material.MarinePaint => 15f,
        _ => 1f,   // brick, block, render, plaster
    };

    /// <summary>
    /// Whether a bullet that has just struck a wall at <paramref name="entry"/>, flying along <paramref name="direction"/>,
    /// gets through it with <paramref name="budget"/> metres of brick's worth to spend: it follows the ray through the
    /// level's boxes from one into the next until it's in the open again. Only walls (an upright face struck) and only the
    /// level's own boxes, not models, the ground or floors. <paramref name="used"/> is how much of the budget it took;
    /// <paramref name="exit"/> where it comes out, on a face of <paramref name="exitSurface"/> facing <paramref name="exitNormal"/>.
    /// </summary>
    public bool ShootThrough(float3 entry, float3 direction, float budget, out float3 exit, out float3 exitNormal, out float used, out SurfaceInfo exitSurface)
    {
        exit = entry;
        exitNormal = direction;
        exitSurface = default;
        used = 0f;
        const float Nudge = 0.002f;
        var p = entry + direction * Nudge;
        for (var step = 0; step < 8; step++)
        {
            // The box it's in that it stays in longest (walls are built of pieces that can touch or overlap).
            var far = -1f;
            var farNormal = default(float3);
            LevelBox inside = default;
            foreach (var box in _boxes)
            {
                if (!box.BlocksShots || !box.Bounds.Contains(p))
                    continue;
                var t = float.PositiveInfinity;
                var n = default(float3);
                for (var axis = 0; axis < 3; axis++)
                {
                    var d = direction[axis];
                    if (float.Abs(d) < 1e-6f)
                        continue;
                    var to = ((d > 0 ? box.Bounds.Max[axis] : box.Bounds.Min[axis]) - p[axis]) / d;
                    if (to < t)
                    {
                        t = to;
                        var s = d > 0 ? 1f : -1f;
                        n = axis == 0 ? new float3(s, 0, 0) : axis == 1 ? new float3(0, s, 0) : new float3(0, 0, s);
                    }
                }

                if (t > far)
                    (far, farNormal, inside) = (t, n, box);
            }

            if (far < 0f)
                return step > 0;   // out in the open again (or, the first time, it wasn't one of the level's boxes it hit)
            if (step == 0 && float.Abs(farNormal.Y) > 0.5f)
                return false;      // it would come out of the top or bottom: a floor, a slab, a roof, not a wall
            used += far * Toughness(inside.Material);
            if (used > budget)
                return false;
            exit = p + direction * far;
            exitNormal = farNormal;
            exitSurface = SurfaceInfo.ForMaterial(inside.Material, inside.Color);
            p = exit + direction * Nudge;
        }

        return false;   // too many pieces: call it solid
    }

    /// <summary>Draws the parts of the level in <paramref name="zones"/>.</summary>
    /// <param name="shadowPass">True when rendering a shadow map: skip the skies and flat ground decals.</param>
    public void Draw(Renderer renderer, Zones zones, bool shadowPass = false)
    {
        using var timer = Perf.LevelDraw.Time();
        if (_drawVersion != _version)
            RebuildDrawLists();

        if (!shadowPass && !NoPrepass)
        {
            renderer.BeginDepthPrepass();
            for (var zone = 0; zone < ZoneCount; zone++)
            {
                if (((int)zones & (1 << zone)) != 0)
                    DrawZone(renderer, zone, shadowPass: true, prepass: true);   // (no decals: they lie on what's drawn here)
            }
            renderer.EndDepthPrepass();
        }

        if ((zones & Zones.Warehouse) != 0)
            DrawFactoryDoor(renderer);
        if ((zones & Zones.Prison) != 0)
            DrawPrisonDoors(renderer);
        if ((zones & Zones.Police) != 0)
            DrawAirlock(renderer);
        if ((zones & Zones.Flats) != 0)
            DrawFlatsDoors(renderer);
        if ((zones & Zones.Jungle) != 0 && _bodegaDoor is { } bodegaDoor)
            DrawDoor(renderer, bodegaDoor);
        if ((zones & Zones.Arctic) != 0 && _stationDoor is { } stationDoor)
            DrawDoor(renderer, stationDoor);
        if ((zones & Zones.Rio) != 0 && _rioStoreDoor is { } rioStoreDoor)
            DrawDoor(renderer, rioStoreDoor);
        if ((zones & Zones.Outback) != 0)
            DrawMotelDoors(renderer);

        for (var zone = 0; zone < ZoneCount; zone++)
        {
            if (((int)zones & (1 << zone)) != 0)
                DrawZone(renderer, zone, shadowPass);
        }

        // Skies around the outdoor areas (the warehouse, the cellar and the bunker are closed in, so they never see
        // one). The shader picks each area's sky from where the sky box is. They go last: the depth test then spares
        // them every pixel something already covers, which is most of the screen, and the sea is just as dear.
        if (!shadowPass)
        {
            if ((zones & Zones.Yard) != 0)
                renderer.DrawSky(float4x4.CreateScale(1000f) * float4x4.CreateTranslation(YardOrigin + new float3(-150, 0, 0)));   // (round the far bank of the dock too)
            if ((zones & Zones.Outback) != 0)
                renderer.DrawSky(float4x4.CreateScale(2800f) * float4x4.CreateTranslation(OutbackOrigin));   // (past the plain's edge, and the firing range's far end)
            if ((zones & Zones.Arctic) != 0)
                renderer.DrawSky(float4x4.CreateScale(700f) * float4x4.CreateTranslation(ArcticOrigin));
            if ((zones & Zones.Ship) != 0)
            {
                renderer.DrawSky(float4x4.CreateScale(ShipSkySize) * float4x4.CreateTranslation(ShipOrigin));
                renderer.DrawBox(OceanBounds, new float3(0.1f, 0.2f, 0.3f), ShadeMode.Lit, Material.Ocean);
            }
            if ((zones & Zones.Jungle) != 0)
                renderer.DrawSky(float4x4.CreateScale(600f) * float4x4.CreateTranslation(JungleOrigin));
            if ((zones & Zones.Warehouse) != 0)
                renderer.DrawSky(float4x4.CreateScale(600f) * float4x4.CreateTranslation(new float3(0, 0, -10)));   // over the factory grounds
            if ((zones & Zones.Prison) != 0)
                renderer.DrawSky(float4x4.CreateScale(400f) * float4x4.CreateTranslation(PrisonOrigin));
            if ((zones & Zones.Flats) != 0)
                renderer.DrawSky(float4x4.CreateScale(400f) * float4x4.CreateTranslation(FlatsOrigin));
            if ((zones & Zones.Rio) != 0)
                renderer.DrawSky(float4x4.CreateScale(1000f) * float4x4.CreateTranslation(RioOrigin + new float3(0, 0, -100)));   // (round the hills across the valley too)
        }
    }

    private static readonly bool NoProps = Profile.Has("noprops"), NoBush = Profile.Has("nobush"), NoPrepass = Profile.Has("noprepass");

    private void DrawZone(Renderer renderer, int zone, bool shadowPass, bool prepass = false)
    {
        foreach (var (prop, bounds) in _zoneProps[zone])
        {
            if ((prop.Decal && shadowPass) || (prop.Arrives && ArrivingVanHidden && !(shadowPass && !prepass && ArrivingVanCastsShadow)))
                continue;
            if (NoProps || (NoBush && prop.Part is not null) || !renderer.InView(bounds))
                continue;
            DrawProp(renderer, prop);
        }

        foreach (var box in _zoneBoxes[zone])
            if (renderer.InView(box.Bounds))
                box.Draw(renderer);
    }

    private static void DrawProp(Renderer renderer, in Prop prop)
    {
        if (prop.Asset is { } asset)
        {
            if (prop.Painted is not { } paint)
            {
                renderer.DrawModel(asset, prop.Model, prop.Tint);
                return;
            }
            // (Painted in the level editor, this one only: its own texture set and colour for some of its parts.)
            for (var i = 0; i < asset.Parts.Count; i++)
            {
                var part = asset.Parts[i];
                renderer.DrawMesh(part.Mesh, prop.Model, SlotPaint.Apply(part.Material, paint.TextureAt(i), paint.ColorAt(i)), prop.Tint);
            }
        }
        else if (prop.Part is { } part)
            renderer.DrawMesh(prop.Mesh!, prop.Model, part, prop.Tint);
        else if (prop.Decal)
            renderer.DrawDecal(prop.Mesh!, prop.Model, prop.Tint, prop.Material);
        else
            renderer.DrawMesh(prop.Mesh!, prop.Model, prop.Tint, ShadeMode.Lit, prop.Material);
    }
}
