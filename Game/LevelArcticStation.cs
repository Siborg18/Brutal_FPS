using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The Arctic's research station: a small cluster of red insulated modules up on steel stilts, 50 m north-east of the
/// survey hut, its front (the door, at the head of a short flight of steel steps, and a small window beside it) facing
/// the white portal. The main module is a mess room with lockers, a galley counter, a table and benches, a radio desk
/// and a bunk; the modules either side are shut. The crew inside (<see cref="Npcs"/>, Game/NpcsStation.cs) come out
/// when the white portal is connected, guns up, and make their way down to it.
/// </summary>
public sealed partial class Level
{
    // The main module (local to ArcticOrigin): 12 x 6 m, its front wall to the south, facing the portal, on stilts.
    private const float StationX0 = 30f, StationX1 = 42f;
    private const float StationZ0 = -44f, StationZ1 = -38f;          // back (north) and front (south) outer faces
    private const float StationWall = 0.15f;

    /// <summary>The station's floor height above the snow (m).</summary>
    public const float StationFloorY = 0.9f;

    private const float StationRoofY = 3.4f;                          // the inside of the ceiling
    private const float StationDoorX0 = 32f, StationDoorX1 = 33f, StationDoorH = 2.0f;
    private const float StationWindowX0 = 35.4f, StationWindowX1 = 36.4f;
    private const float StationWindowY0 = 1.05f, StationWindowY1 = 1.75f;   // above the floor
    private const float StationDeckX0 = 31.6f, StationDeckX1 = 35.0f, StationDeckZ1 = -36.6f;
    // (The steps run the deck's east end out, so their railings carry on from the deck's: no rail end to walk into.)
    private const float StationStairX0 = 33.4f, StationStairX1 = StationDeckX1;
    private const int StationRisers = 5;
    private const float StationTread = 0.3f;

    private static readonly float3 StationRed = new(0.56f, 0.07f, 0.05f);
    private static readonly float3 StationWhite = new(0.84f, 0.84f, 0.81f);
    private static readonly float3 StationLining = new(0.80f, 0.77f, 0.70f);
    private static readonly float3 StationSteel = new(0.34f, 0.34f, 0.35f);
    private static readonly float3 StationRail = new(0.62f, 0.62f, 0.60f);

    private PrisonDoor? _stationDoor;

    /// <summary>"[E] Open door" / "[E] Close door" while the player is at the station's door, otherwise null.</summary>
    public string? StationDoorPrompt { get; private set; }

    private static float3 StationLocal(float x, float y, float z) => ArcticOrigin + new float3(x, y, z);

    /// <summary>The middle of the station's doorway, on its floor.</summary>
    public static float3 StationDoorAt => StationLocal((StationDoorX0 + StationDoorX1) / 2, StationFloorY, StationZ1);

    /// <summary>The way out for the crew: in front of the door inside, the doorway, the deck, the head and foot of the steps, the snow beyond.</summary>
    public static float3[] StationWayOut =>
    [
        StationLocal(32.5f, StationFloorY, -39.4f),
        StationLocal(32.5f, StationFloorY, StationZ1 + 0.1f),
        StationLocal(33.5f, StationFloorY, -37.1f),
        StationLocal((StationStairX0 + StationStairX1) / 2, StationFloorY, StationDeckZ1 - 0.1f),
        StationLocal((StationStairX0 + StationStairX1) / 2, 0f, StationDeckZ1 + StationRisers * StationTread - 0.1f),
        StationLocal((StationStairX0 + StationStairX1) / 2 - 0.6f, 0f, -33.6f),
    ];

    /// <summary>Where the player steps up and down (<see cref="HasStairs"/>): the steps and the deck, with a margin round them.</summary>
    private static Aabb StationStepsArea => BoxBetween(
        StationLocal(StationDeckX0 - 0.5f, -0.5f, StationZ1 - 0.6f),
        StationLocal(StationDeckX1 + 0.5f, StationFloorY + 2.5f, StationDeckZ1 + StationRisers * StationTread + 1.0f));

    /// <summary>The inside of the main module.</summary>
    public static Aabb StationInterior => BoxBetween(
        StationLocal(StationX0 + StationWall, StationFloorY, StationZ0 + StationWall),
        StationLocal(StationX1 - StationWall, StationRoofY, StationZ1 - StationWall));

    /// <summary>Indoors: no blowing snow or whiteout inside, the sky's light coming in at the door and window.</summary>
    public static ShadeVolume ArcticStationShade => new(
        BoxBetween(StationLocal(StationX0 + StationWall - 0.02f, StationFloorY - 0.05f, StationZ0 + StationWall - 0.02f),
                   StationLocal(StationX1 - StationWall + 0.02f, StationRoofY + 0.02f, StationZ1 - 0.04f)),
        StationLocal(34.2f, StationFloorY + 1.4f, StationZ1 + 0.5f), 7f, 0.42f, NoSun: true);

    private void BuildArcticStation()
    {
        var id = float4x4.Identity;
        const float f = StationFloorY, t = StationWall, lining = 0.04f;
        const float x0 = StationX0, x1 = StationX1, z0 = StationZ0, z1 = StationZ1;
        float3 L(float x, float y, float z) => StationLocal(x, y, z);

        // --- The main module: floor, walls (red outside, a pale lining in), ceiling and a flat roof with a white band ---
        AddMinMax(L(x0 + t, f - 0.3f, z0 + t), L(x1 - t, f, z1 - t), new float3(0.30f, 0.33f, 0.36f), Material.Plain);    // floor (lino on top)
        void Wall(float ax, float ay, float az, float bx, float by, float bz, bool alongX)
        {
            // Outer skin and inner lining, split through the wall's thickness.
            if (alongX)
            {
                var mid = az < z0 + 1 ? az + t - lining : az + lining;   // (the back wall's lining is on its +z side, the front's on its -z side)
                if (az < z0 + 1)
                {
                    AddMinMax(L(ax, ay, az), L(bx, by, mid), StationRed, Material.PaintedMetal);
                    AddMinMax(L(ax, ay, mid), L(bx, by, bz), StationLining, Material.WallPaint);
                }
                else
                {
                    AddMinMax(L(ax, ay, az), L(bx, by, mid), StationLining, Material.WallPaint);
                    AddMinMax(L(ax, ay, mid), L(bx, by, bz), StationRed, Material.PaintedMetal);
                }
            }
            else
            {
                var west = ax < x0 + 1;
                var mid = west ? ax + t - lining : ax + lining;
                AddMinMax(L(ax, ay, az), L(mid, by, bz), west ? StationRed : StationLining, west ? Material.PaintedMetal : Material.WallPaint);
                AddMinMax(L(mid, ay, az), L(bx, by, bz), west ? StationLining : StationRed, west ? Material.WallPaint : Material.PaintedMetal);
            }
        }
        const float wy0 = f - 0.3f, wy1 = StationRoofY;
        Wall(x0, wy0, z0, x1, wy1, z0 + t, alongX: true);                                   // back
        Wall(x0, wy0, z0 + t, x0 + t, wy1, z1 - t, alongX: false);                          // west
        Wall(x1 - t, wy0, z0 + t, x1, wy1, z1 - t, alongX: false);                          // east
        // The front, round the door and the window.
        const float dx0 = StationDoorX0, dx1 = StationDoorX1, wx0 = StationWindowX0, wx1 = StationWindowX1;
        Wall(x0, wy0, z1 - t, dx0, wy1, z1, alongX: true);
        Wall(dx0, f + StationDoorH, z1 - t, dx1, wy1, z1, alongX: true);
        Wall(dx0, wy0, z1 - t, dx1, f, z1, alongX: true);
        Wall(dx1, wy0, z1 - t, wx0, wy1, z1, alongX: true);
        Wall(wx0, wy0, z1 - t, wx1, f + StationWindowY0, z1, alongX: true);
        Wall(wx0, f + StationWindowY1, z1 - t, wx1, wy1, z1, alongX: true);
        Wall(wx1, wy0, z1 - t, x1, wy1, z1, alongX: true);
        // The window: a double-glazed pane in a white frame, a sill outside.
        var frame = JB(Material.PaintedMetal, StationWhite);
        var wcx = (wx0 + wx1) / 2;
        var wcy = f + (StationWindowY0 + StationWindowY1) / 2;
        Add(L(wcx, wcy, z1 - t / 2), new float3(wx1 - wx0, StationWindowY1 - StationWindowY0, 0.04f), new float3(0.10f, 0.12f, 0.14f), Material.Glass);
        foreach (var (cx, cy, sx, sy) in (ReadOnlySpan<(float, float, float, float)>)[
            (wcx, f + StationWindowY0 - 0.03f, wx1 - wx0 + 0.16f, 0.06f), (wcx, f + StationWindowY1 + 0.03f, wx1 - wx0 + 0.16f, 0.06f),
            (wx0 - 0.03f, wcy, 0.06f, StationWindowY1 - StationWindowY0), (wx1 + 0.03f, wcy, 0.06f, StationWindowY1 - StationWindowY0),
            (wcx, wcy, 0.04f, StationWindowY1 - StationWindowY0)])
            OBox(frame, id, L(cx, cy, z1 + 0.02f), new float3(sx, sy, 0.05f));
        OBox(frame, id, L(wcx, f + StationWindowY0 - 0.08f, z1 + 0.07f), new float3(wx1 - wx0 + 0.24f, 0.04f, 0.14f));
        // The door's frame outside, and a lamp over it.
        foreach (var x in (ReadOnlySpan<float>)[dx0 - 0.04f, dx1 + 0.04f])
            OBox(frame, id, L(x, f + StationDoorH / 2, z1 + 0.02f), new float3(0.08f, StationDoorH + 0.08f, 0.05f));
        OBox(frame, id, L((dx0 + dx1) / 2, f + StationDoorH + 0.04f, z1 + 0.02f), new float3(dx1 - dx0 + 0.16f, 0.08f, 0.05f));
        OBox(JB(Material.Steel, StationSteel), id, L((dx0 + dx1) / 2, f + StationDoorH + 0.32f, z1 + 0.1f), new float3(0.22f, 0.14f, 0.18f));
        OBox(JB(Material.Emissive, new float3(1.0f, 0.86f, 0.6f)), id, L((dx0 + dx1) / 2, f + StationDoorH + 0.24f, z1 + 0.12f), new float3(0.16f, 0.03f, 0.12f));

        // Ceiling, roof (a little proud all round), and the white band along the top of the walls, as on the old modules.
        AddMinMax(L(x0 + t, StationRoofY - 0.06f, z0 + t), L(x1 - t, StationRoofY, z1 - t), new float3(0.86f, 0.86f, 0.84f), Material.CeilingTile, collides: false);
        AddMinMax(L(x0 - 0.15f, StationRoofY, z0 - 0.15f), L(x1 + 0.15f, StationRoofY + 0.22f, z1 + 0.15f), StationWhite * 0.92f, Material.Galvanized);
        var band = JB(Material.PaintedMetal, StationWhite);
        OBox(band, id, L((x0 + x1) / 2, StationRoofY - 0.12f, z1 + 0.025f), new float3(x1 - x0 + 0.05f, 0.3f, 0.05f));
        OBox(band, id, L((x0 + x1) / 2, StationRoofY - 0.12f, z0 - 0.025f), new float3(x1 - x0 + 0.05f, 0.3f, 0.05f));
        OBox(band, id, L(x0 - 0.025f, StationRoofY - 0.12f, (z0 + z1) / 2), new float3(0.05f, 0.3f, z1 - z0 + 0.05f));
        OBox(band, id, L(x1 + 0.025f, StationRoofY - 0.12f, (z0 + z1) / 2), new float3(0.05f, 0.3f, z1 - z0 + 0.05f));
        // Lights inside: two fluorescent panels.
        foreach (var x in (ReadOnlySpan<float>)[33.5f, 38.5f])
            OBox(JB(Material.Emissive, new float3(0.95f, 0.95f, 0.88f)), id, L(x, StationRoofY - 0.075f, -41f), new float3(1.2f, 0.03f, 0.3f));
        // A flue and a whip aerial on the roof, a vent box on the back wall.
        var steel = JB(Material.Steel, StationSteel);
        OCyl(steel, id, L(40.6f, StationRoofY + 0.2f, -42.6f), L(40.6f, StationRoofY + 1.3f, -42.6f), 0.09f);
        OCyl(steel, id, L(40.6f, StationRoofY + 1.3f, -42.6f), L(40.6f, StationRoofY + 1.42f, -42.6f), 0.15f, 0.04f);
        OCyl(steel, id, L(31.0f, StationRoofY + 0.2f, -43.3f), L(31.0f, StationRoofY + 3.6f, -43.3f), 0.025f, 0.01f, 6);
        OBox(steel, id, L(36f, 2.5f, z0 - 0.15f), new float3(0.8f, 0.5f, 0.3f));

        // --- Stilts: steel legs and bracing under the floor, a sooty skirt of shadow ---
        var legs = JB(Material.Steel, StationSteel * 0.8f);
        for (var x = x0 + 0.2f; x < x1; x += 2.9f)
        {
            foreach (var z in (ReadOnlySpan<float>)[z0 + 0.2f, z1 - 0.2f])
            {
                OBox(legs, id, L(x, (f - 0.3f) / 2, z), new float3(0.14f, f - 0.3f, 0.14f));
                OBox(legs, id, L(x, 0.04f, z), new float3(0.4f, 0.08f, 0.4f));
            }
            OBox(legs, id, L(x, 0.35f, (z0 + z1) / 2), new float3(0.06f, 0.06f, z1 - z0 - 0.4f));
        }
        AddCollider(L((x0 + x1) / 2, (f - 0.3f) / 2, (z0 + z1) / 2), new float3(x1 - x0, f - 0.3f, z1 - z0));   // (no crawling under it)

        // --- The deck in front of the door, the steps down to the snow, railings ---
        const float deckTop = f;
        AddMinMax(L(StationDeckX0, deckTop - 0.08f, z1), L(StationDeckX1, deckTop, StationDeckZ1), StationSteel, Material.Steel);
        foreach (var x in (ReadOnlySpan<float>)[StationDeckX0 + 0.06f, StationStairX0 - 0.06f, StationDeckX1 - 0.06f])
            OBox(legs, id, L(x, (deckTop - 0.08f) / 2, StationDeckZ1 - 0.06f), new float3(0.08f, deckTop - 0.08f, 0.08f));   // (under the deck, clear of the steps)

        // The steps and every railing round the deck: one piece, so the editor picks and moves them together.
        var steps = Piece("station steps");
        var treads = JB(Material.Steel, StationSteel * 1.1f);
        var lane = (StationStairX0 + StationStairX1) / 2;
        const float rise = StationFloorY / StationRisers;
        for (var i = 1; i < StationRisers; i++)
        {
            var za = StationDeckZ1 + (i - 1) * StationTread;
            var zb = za + StationTread;
            var y = deckTop - i * rise;
            OBox(treads, id, L(lane, y - 0.02f, (za + zb) / 2), new float3(StationStairX1 - StationStairX0 - 0.1f, 0.04f, StationTread - 0.015f));
            AddStatic(new Aabb(L(StationStairX0, y - 0.2f, za), L(StationStairX1, y, zb)), default, visible: false, blocksShots: false);
        }
        var run = StationRisers * StationTread;
        var slope = float.Atan2(deckTop, run);
        foreach (var x in (ReadOnlySpan<float>)[StationStairX0 + 0.03f, StationStairX1 - 0.03f])
            OBox(treads, id, L(x, deckTop / 2 - 0.1f, StationDeckZ1 + run / 2), new float3(0.05f, 0.22f, float.Sqrt(run * run + deckTop * deckTop)), float4x4.CreateRotationX(slope));

        // Railings: round the deck (not across the door or the head of the steps), and down both sides of the steps.
        var rail = JB(Material.Galvanized, StationRail);
        void Railing(float3 a, float3 b)
        {
            foreach (var h in (ReadOnlySpan<float>)[0.5f, 1.05f])
                OCyl(rail, id, a + new float3(0, h, 0), b + new float3(0, h, 0), 0.025f, 0.025f, 6);
            var len = float3.Distance(a, b);
            var posts = int.Max(1, (int)float.Ceiling(len / 1.2f));
            for (var k = 0; k <= posts; k++)
            {
                var p = float3.Lerp(a, b, k / (float)posts);
                OCyl(rail, id, p, p + new float3(0, 1.08f, 0), 0.025f, 0.025f, 6);
            }
        }
        Railing(L(StationDeckX0, deckTop, z1 + 0.4f), L(StationDeckX0, deckTop, StationDeckZ1));
        Railing(L(StationDeckX0, deckTop, StationDeckZ1), L(StationStairX0, deckTop, StationDeckZ1));
        Railing(L(StationDeckX1, deckTop, z1), L(StationDeckX1, deckTop, StationDeckZ1));
        Railing(L(StationStairX0, deckTop, StationDeckZ1), L(StationStairX0, 0f, StationDeckZ1 + run));   // (the foot posts stand on the snow)
        Railing(L(StationStairX1, deckTop, StationDeckZ1), L(StationStairX1, 0f, StationDeckZ1 + run));
        foreach (var (a, b) in ((float3, float3)[])
                 [(L(StationDeckX0 - 0.05f, deckTop, z1 + 0.4f),L(StationDeckX0, deckTop + 1.1f, StationDeckZ1)),
                  (L(StationDeckX0, deckTop, StationDeckZ1 - 0.05f), L(StationStairX0, deckTop + 1.1f, StationDeckZ1)),
                  (L(StationDeckX1, deckTop, z1), L(StationDeckX1 + 0.05f, deckTop + 1.1f, StationDeckZ1)),
                  (L(StationStairX0 - 0.05f, 0, StationDeckZ1), L(StationStairX0, deckTop + 1.1f, StationDeckZ1 + run)),
                  (L(StationStairX1, 0, StationDeckZ1), L(StationStairX1 + 0.05f, deckTop + 1.1f, StationDeckZ1 + run))])
            AddStatic(new Aabb(a, b), default, visible: false, blocksShots: false);
        AddStairFlight("station steps", L(lane, 0, StationDeckZ1 + run + 0.4f), L(lane, 0, StationDeckZ1 + run), L(lane, deckTop, StationDeckZ1),
            L(lane, deckTop, StationDeckZ1 - 0.5f), new Aabb(L(StationStairX0, 0, StationDeckZ1), L(StationStairX1, deckTop, StationDeckZ1 + run)));
        steps.Dispose();

        BuildStationDoor();
        BuildStationInterior();
        BuildStationAnnexes();
        FlushPoliceBatches();
    }

    /// <summary>
    /// The door: an insulated steel door, white, hinged on its west edge and opening outwards to lie back against the
    /// front wall. Built in the hinge's frame: the leaf runs east (+x) from the hinge, its outside face at z = 0.
    /// </summary>
    private void BuildStationDoor()
    {
        var id = float4x4.Identity;
        const float thick = 0.06f, w = StationDoorX1 - StationDoorX0, h = StationDoorH - 0.01f;
        var c = new float3(w / 2, h / 2, -thick / 2);
        var leaf = HingedLeaf(StationLocal(StationDoorX0, StationFloorY, StationZ1), float.DegreesToRadians(-165f), c, new float3(w - 0.01f, h, thick));
        OBox(leaf.B(Material.PaintedMetal, StationWhite * 0.95f), id, c, new float3(w - 0.01f, h, thick));
        var dark = leaf.B(Material.Steel, new float3(0.15f, 0.15f, 0.16f));
        foreach (var (z, side) in (ReadOnlySpan<(float, float)>)[(0f, 1f), (-thick, -1f)])
        {
            OBox(dark, id, new float3(w - 0.12f, 1.0f, z + side * 0.03f), new float3(0.04f, 0.3f, 0.04f));   // the lever handle
            OBox(dark, id, new float3(w - 0.12f, 1.1f, z + side * 0.015f), new float3(0.12f, 0.025f, 0.03f));
        }
        OBox(leaf.B(Material.Plain, new float3(0.85f, 0.65f, 0.05f)), id, new float3(w / 2, 1.55f, 0.004f), new float3(0.32f, 0.12f, 0.008f));   // a hazard label
        _stationDoor = NewDoor("station", 0.7f, leaf);
        MoveDoor(_stationDoor, false, instant: true);
    }

    /// <summary>Near the station's door, E opens or shuts it. It won't shut on the player standing in the doorway.</summary>
    public void UpdateStationDoor(float dt, bool usePressed, float3 player)
    {
        if (_stationDoor is not { } door)
            return;
        var at = StationDoorAt;
        var near = float.Abs(player.X - at.X) < 1.3f && player.Z > at.Z - 1.6f && player.Z < at.Z + 1.8f && player.Y > at.Y - 0.6f && player.Y < at.Y + 2.5f;
        var inDoorway = float.Abs(player.X - at.X) < 0.85f && float.Abs(player.Z - at.Z) < 0.45f;
        var open = door.Target > 0.5f;
        StationDoorPrompt = near ? (open ? "[E] Close door" : "[E] Open door") : null;
        if (near && usePressed && !(inDoorway && open) && MoveDoor(door, !open))
            Log.Write(open ? "[DOOR] Shutting the station's door" : "[DOOR] Opening the station's door");
        StepDoor(door, dt);
    }

    /// <summary>The crew throw the station's door open on their way out.</summary>
    public void OpenStationDoor()
    {
        if (_stationDoor is { } door && MoveDoor(door, true))
            Log.Write("[STATION] The station's door is thrown open");
    }

    /// <summary>The mess room: lockers, a galley counter, a table and benches, the radio desk, a bunk and a heater.</summary>
    private void BuildStationInterior()
    {
        var id = float4x4.Identity;
        const float f = StationFloorY;
        float3 L(float x, float y, float z) => StationLocal(x, y, z);
        const float back = StationZ0 + StationWall;

        // Lockers against the back wall, west end: four grey steel ones.
        var locker = JB(Material.PaintedMetal, new float3(0.45f, 0.48f, 0.50f));
        var slot = JB(Material.Steel, new float3(0.12f, 0.12f, 0.13f));
        for (var i = 0; i < 4; i++)
        {
            var x = 30.45f + i * 0.5f;
            OBox(locker, id, L(x, f + 0.95f, back + 0.25f), new float3(0.48f, 1.9f, 0.5f));
            for (var k = 0; k < 3; k++)
                OBox(slot, id, L(x, f + 1.6f + k * 0.05f, back + 0.505f), new float3(0.3f, 0.015f, 0.01f));
            OBox(slot, id, L(x + 0.17f, f + 1.0f, back + 0.51f), new float3(0.03f, 0.12f, 0.02f));
        }
        AddCollider(L(31.2f, f + 0.95f, back + 0.25f), new float3(2.0f, 1.9f, 0.5f));
        // Parkas on hooks beside them.
        var parka = JB(Material.Cloth, new float3(0.62f, 0.03f, 0.03f));
        foreach (var x in (ReadOnlySpan<float>)[32.55f, 32.85f])
            OBox(parka, id, L(x, f + 1.3f, back + 0.12f), new float3(0.26f, 0.85f, 0.18f));

        // The galley: a counter with a sink and a two-ring hob, a kettle, a shelf of tins above.
        var counter = JB(Material.Veneer, new float3(0.62f, 0.58f, 0.52f));
        var unit = JB(Material.PaintedMetal, new float3(0.82f, 0.82f, 0.80f));
        OBox(unit, id, L(34.25f, f + 0.44f, back + 0.3f), new float3(2.3f, 0.88f, 0.6f));
        OBox(counter, id, L(34.25f, f + 0.9f, back + 0.32f), new float3(2.34f, 0.04f, 0.64f));
        // (The sink stands 5 mm proud of the worktop: level with it, the two flicker through each other.)
        OBox(JB(Material.Steel, new float3(0.6f, 0.6f, 0.62f)), id, L(33.6f, f + 0.915f, back + 0.33f), new float3(0.5f, 0.02f, 0.4f));
        foreach (var x in (ReadOnlySpan<float>)[34.8f, 35.05f])
            OCyl(JB(Material.Steel, new float3(0.08f, 0.08f, 0.08f)), id, L(x, f + 0.92f, back + 0.3f), L(x, f + 0.935f, back + 0.3f), 0.09f);
        OCyl(JB(Material.Steel, new float3(0.7f, 0.7f, 0.72f)), id, L(34.1f, f + 0.92f, back + 0.4f), L(34.1f, f + 1.12f, back + 0.4f), 0.08f, 0.07f);
        OBox(counter, id, L(34.25f, f + 1.75f, back + 0.15f), new float3(2.0f, 0.03f, 0.28f));
        var tins = JB(Material.Plain, new float3(0.70f, 0.55f, 0.20f));
        for (var i = 0; i < 9; i++)
            OCyl(tins, id, L(33.4f + i * 0.2f, f + 1.77f, back + 0.15f), L(33.4f + i * 0.2f, f + 1.9f, back + 0.15f), 0.045f, 0.045f, 8);
        AddCollider(L(34.25f, f + 0.45f, back + 0.3f), new float3(2.34f, 0.9f, 0.64f));

        // The table and its two benches, in the middle of the room.
        var top = JB(Material.Veneer, new float3(0.55f, 0.42f, 0.30f));
        var metal = JB(Material.Steel, StationSteel);
        OBox(top, id, L(35.5f, f + 0.74f, -41f), new float3(1.8f, 0.04f, 0.9f));
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(34.75f, -41.35f), (36.25f, -41.35f), (34.75f, -40.65f), (36.25f, -40.65f)])
            OBox(metal, id, L(x, f + 0.36f, z), new float3(0.05f, 0.72f, 0.05f));
        foreach (var z in (ReadOnlySpan<float>)[-41.85f, -40.15f])
        {
            OBox(top, id, L(35.5f, f + 0.44f, z), new float3(1.7f, 0.04f, 0.32f));
            foreach (var x in (ReadOnlySpan<float>)[34.8f, 36.2f])
                OBox(metal, id, L(x, f + 0.21f, z), new float3(0.05f, 0.42f, 0.25f));
        }
        AddCollider(L(35.5f, f + 0.38f, -41f), new float3(1.8f, 0.76f, 0.9f));
        // Mugs, a map and a thermos on the table.
        OBox(JB(Material.Plain, new float3(0.85f, 0.82f, 0.70f)), id, L(35.2f, f + 0.765f, -41.05f), new float3(0.6f, 0.005f, 0.42f));
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(34.85f, -40.8f), (36.0f, -41.2f)])
            OCyl(JB(Material.Plain, new float3(0.2f, 0.32f, 0.55f)), id, L(x, f + 0.76f, z), L(x, f + 0.86f, z), 0.04f, 0.04f, 8);
        OCyl(JB(Material.Steel, new float3(0.55f, 0.1f, 0.08f)), id, L(36.2f, f + 0.76f, -40.8f), L(36.2f, f + 1.06f, -40.8f), 0.05f, 0.05f, 10);

        // The radio desk against the back wall: a steel desk, the HF set, a handset, a monitor and a chair.
        OBox(metal, id, L(38f, f + 0.73f, back + 0.35f), new float3(2.0f, 0.04f, 0.7f));
        foreach (var x in (ReadOnlySpan<float>)[37.05f, 38.95f])
            OBox(metal, id, L(x, f + 0.36f, back + 0.35f), new float3(0.05f, 0.72f, 0.65f));
        AddCollider(L(38f, f + 0.37f, back + 0.35f), new float3(2.0f, 0.75f, 0.7f));
        var set = JB(Material.Polymer, new float3(0.12f, 0.13f, 0.12f));
        OBox(set, id, L(37.5f, f + 0.88f, back + 0.3f), new float3(0.5f, 0.26f, 0.38f));
        OBox(JB(Material.Emissive, new float3(0.95f, 0.55f, 0.1f)), id, L(37.4f, f + 0.92f, back + 0.495f), new float3(0.14f, 0.05f, 0.005f));
        OBox(set, id, L(37.85f, f + 0.78f, back + 0.45f), new float3(0.06f, 0.05f, 0.2f));
        OBox(set, id, L(38.5f, f + 1.0f, back + 0.25f), new float3(0.55f, 0.36f, 0.05f));
        OBox(JB(Material.Emissive, new float3(0.25f, 0.45f, 0.55f)), id, L(38.5f, f + 1.0f, back + 0.28f), new float3(0.5f, 0.31f, 0.005f));
        var chair = JB(Material.Polymer, new float3(0.08f, 0.08f, 0.09f));
        OBox(chair, id, L(38f, f + 0.46f, -42.75f), new float3(0.45f, 0.06f, 0.45f));
        OBox(chair, id, L(38f, f + 0.8f, -42.52f), new float3(0.42f, 0.5f, 0.05f));
        OCyl(metal, id, L(38f, f, -42.75f), L(38f, f + 0.43f, -42.75f), 0.025f);

        // A bunk against the east wall, a red heater under the window side.
        var bunk = JB(Material.Steel, new float3(0.3f, 0.4f, 0.32f));
        var bedding = JB(Material.Cloth, new float3(0.28f, 0.34f, 0.42f));
        const float bx = StationX1 - StationWall - 0.47f;
        foreach (var y in (ReadOnlySpan<float>)[0.4f, 1.45f])
        {
            OBox(bunk, id, L(bx, f + y, -42.6f), new float3(0.9f, 0.06f, 2.0f));
            OBox(bedding, id, L(bx, f + y + 0.1f, -42.6f), new float3(0.84f, 0.14f, 1.94f));
        }
        foreach (var z in (ReadOnlySpan<float>)[-43.57f, -41.63f])
            OBox(bunk, id, L(bx - 0.42f, f + 0.9f, z), new float3(0.05f, 1.8f, 0.05f));
        AddCollider(L(bx, f + 0.9f, -42.6f), new float3(0.92f, 1.8f, 2.0f));
        var heater = JB(Material.PaintedMetal, new float3(0.70f, 0.16f, 0.10f));
        OBox(heater, id, L(39.2f, f + 0.35f, StationZ1 - StationWall - 0.12f), new float3(1.0f, 0.6f, 0.16f));
        AddCollider(L(39.2f, f + 0.35f, StationZ1 - StationWall - 0.12f), new float3(1.0f, 0.7f, 0.2f));
    }

    /// <summary>
    /// The modules either side: a red one up on a taller frame to the west, and a white link and a red store module to
    /// the east, both shut. Fuel drums, a pallet and a marker flag round about.
    /// </summary>
    private void BuildStationAnnexes()
    {
        var id = float4x4.Identity;
        float3 L(float x, float y, float z) => StationLocal(x, y, z);
        var steel = JB(Material.Steel, StationSteel * 0.8f);
        var band = JB(Material.PaintedMetal, StationWhite);

        void Module(float ax, float az, float bx, float bz, float floor, float height, float3 tint, Material material)
        {
            AddMinMax(L(ax, floor, az), L(bx, floor + height, bz), tint, material);
            AddMinMax(L(ax - 0.1f, floor + height, az - 0.1f), L(bx + 0.1f, floor + height + 0.15f, bz + 0.1f), StationWhite * 0.9f, Material.Galvanized);
            OBox(band, id, L((ax + bx) / 2, floor + height - 0.12f, bz + 0.02f), new float3(bx - ax + 0.04f, 0.24f, 0.04f));
            for (var x = ax + 0.15f; x < bx; x += float.Max(1.5f, (bx - ax - 0.3f) / 2))
            {
                foreach (var z in (ReadOnlySpan<float>)[az + 0.15f, bz - 0.15f])
                    OBox(steel, id, L(x, floor / 2, z), new float3(0.12f, floor, 0.12f));
            }
            AddCollider(L((ax + bx) / 2, floor / 2, (az + bz) / 2), new float3(bx - ax, floor, bz - az));
        }

        // West: raised higher, on a braced frame, with a shut door at the top of a ladder of steps.
        Module(25.4f, -44.6f, 29.8f, -42.1f, 1.5f, 2.5f, StationRed, Material.Corrugated);
        // Its bracing: a diagonal across each bay between the legs, front and back, from the foot of one leg to the top
        // of the next. Each is bolted on the legs' outer faces (not through them), and both its ends are on a leg.
        {
            const float ax = 25.4f, bx = 29.8f, floor = 1.5f;
            var legXs = new List<float>();
            for (var x = ax + 0.15f; x < bx; x += float.Max(1.5f, (bx - ax - 0.3f) / 2))
                legXs.Add(x);   // (as Module places them)
            foreach (var (z, outward) in (ReadOnlySpan<(float, float)>)[(-42.1f - 0.15f, 1f), (-44.6f + 0.15f, -1f)])
            {
                for (var k = 0; k + 1 < legXs.Count; k++)
                {
                    var (xa, xb) = (legXs[k], legXs[k + 1]);
                    var (ya, yb) = k % 2 == 0 ? (0.08f, floor - 0.08f) : (floor - 0.08f, 0.08f);   // (a chevron: the two meet at the middle leg's top)
                    var length = float.Sqrt((xb - xa) * (xb - xa) + (yb - ya) * (yb - ya));
                    OBox(steel, id, L((xa + xb) / 2, (ya + yb) / 2, z + outward * 0.09f), new float3(length, 0.06f, 0.05f),
                        float4x4.CreateRotationZ(float.Atan2(yb - ya, xb - xa)));
                }
            }
        }
        OBox(JB(Material.PaintedMetal, StationWhite * 0.9f), id, L(28.6f, 1.5f + 1.0f, -42.08f), new float3(0.9f, 1.95f, 0.04f));

        // East: a white link (the boot room) and the red store module beyond it, its own steps and a shut door.
        AddMinMax(L(StationX1, 0.6f, -43.2f), L(43.2f, StationRoofY - 0.2f, -40.6f), StationWhite * 0.95f, Material.PaintedMetal);
        AddMinMax(L(StationX1 - 0.1f, StationRoofY - 0.2f, -43.3f), L(43.3f, StationRoofY - 0.05f, -40.5f), StationWhite * 0.85f, Material.Galvanized);
        AddCollider(L(42.6f, 0.3f, -41.9f), new float3(1.2f, 0.6f, 2.6f));
        Module(43.2f, -44.0f, 49.2f, -41.4f, 1.0f, 2.5f, StationRed, Material.Corrugated);
        OBox(JB(Material.PaintedMetal, StationWhite * 0.9f), id, L(46.8f, 1.0f + 1.0f, -41.38f), new float3(0.9f, 1.95f, 0.04f));
        BuildStoreSteps();

        // Fuel drums on a pallet by the store module, a pallet of crates, an orange marker flag on a pole.
        Add(L(50.6f, 0.06f, -42.4f), new float3(1.2f, 0.12f, 1.0f), WoodTint * 0.7f, Material.Wood);
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(50.3f, -42.65f), (50.9f, -42.65f), (50.3f, -42.1f), (50.9f, -42.1f)])
            AddDrum(L(x, 0.12f, z), new float3(0.18f, 0.32f, 0.55f), x * 3f, Material.PaintedMetal);
        AddCrate(L(44.5f, 0, -39.2f), 0.8f, WoodTint * 0.8f);
        AddCrate(L(44.6f, 0.8f, -39.25f), 0.55f, WoodTint * 0.7f, 1);
        Add(L(28.4f, 1.5f, -36.6f), new float3(0.05f, 3.0f, 0.05f), new float3(0.58f, 0.48f, 0.28f), Material.Wood, blocksShots: false, collides: false);
        Add(L(28.6f, 2.8f, -36.6f), new float3(0.4f, 0.3f, 0.012f), new float3(0.95f, 0.35f, 0.06f), Material.Cloth, blocksShots: false, collides: false);
    }

    /// <summary>
    /// The store module's steps, one piece for the editor: a landing at its door, four treads between two stringers
    /// down to the snow, and a railing along each stringer. Every tread's front edge is on the stringers' line, and
    /// the railings' posts stand on the landing and the stringers.
    /// </summary>
    private void BuildStoreSteps()
    {
        var id = float4x4.Identity;
        float3 L(float x, float y, float z) => StationLocal(x, y, z);
        const float cx = 46.8f, face = -41.4f, top = 1.0f;          // the door's middle, the module's front face, its floor
        const float width = 1.1f, landing = 0.5f;                    // outside the stringers; the landing's depth
        const int risers = 5;
        const float rise = top / risers, going = 0.28f;
        const float head = face + landing;                           // the landing's front edge: the top of the flight
        const float foot = head + risers * going;                    // where the stringers meet the snow
        var half = width / 2;
        var stringerX = half - 0.025f;

        using var piece = Piece("store module steps");
        var steel = JB(Material.Steel, StationSteel);
        var frame = JB(Material.Steel, StationSteel * 0.8f);

        // The landing on two legs, its back against the module.
        OBox(steel, id, L(cx, top - 0.04f, (face + head) / 2), new float3(width, 0.08f, landing));
        AddStatic(new Aabb(L(cx - half, top - 0.2f, face), L(cx + half, top, head)), default, visible: false, blocksShots: false);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            OBox(frame, id, L(cx + side * (half - 0.05f), (top - 0.08f) / 2, head - 0.05f), new float3(0.06f, top - 0.08f, 0.06f));

        // The treads: tread k's front edge on the line from the landing's edge down to the foot.
        for (var k = 1; k < risers; k++)
        {
            var y = top - k * rise;
            var (za, zb) = (head + (k - 1) * going, head + k * going);
            OBox(steel, id, L(cx, y - 0.02f, (za + zb) / 2), new float3(width - 0.1f, 0.04f, going - 0.01f));
            AddStatic(new Aabb(L(cx - half, y - 0.2f, za), L(cx + half, y, zb)), default, visible: false, blocksShots: false);
        }

        // The stringers, carrying the treads' ends, from the landing down into the snow.
        var run = foot - head;
        var slope = float.Atan2(top, run);
        var length = float.Sqrt(run * run + top * top);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            OBox(frame, id, L(cx + side * stringerX, top / 2 - 0.1f, (head + foot) / 2), new float3(0.05f, 0.22f, length), float4x4.CreateRotationX(slope));

        // The railings: level along the landing, then down the stringers, a post at each corner and halfway down.
        var rail = JB(Material.Galvanized, StationRail);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var x = cx + side * stringerX;
            float3[] line = [L(x, top, face + 0.05f), L(x, top, head), L(x, top * 0.05f / run, foot - 0.05f)];   // (the last on the stringers' line, just short of the snow)
            foreach (var h in (ReadOnlySpan<float>)[0.5f, 1.0f])
                for (var i = 0; i + 1 < line.Length; i++)
                    OCyl(rail, id, line[i] + new float3(0, h, 0), line[i + 1] + new float3(0, h, 0), 0.025f, 0.025f, 6);
            foreach (var p in (ReadOnlySpan<float3>)[line[0], line[1], float3.Lerp(line[1], line[2], 0.5f)])
                OCyl(rail, id, p, p + new float3(0, 1.03f, 0), 0.025f, 0.025f, 6);
            OCyl(rail, id, line[2] with { Y = 0f }, line[2] + new float3(0, 1.03f, 0), 0.025f, 0.025f, 6);   // (the foot post down into the snow)
            AddStatic(new Aabb(L(x - 0.03f, 0, face), L(x + 0.03f, top + 1.05f, foot)), default, visible: false, blocksShots: false);
        }
    }
}
