using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The office at the hall's south end, the far end from the sliding door: one long, abandoned room built against the
/// south wall, limewashed block walls and a suspended ceiling, windows with half their panes gone
/// looking back up the hall, and a door standing open. Two desks and filing cabinets are left in it, and buckets stand
/// all round its walls, under the leaks. Its light is the ceiling panels', worked out in the shader (indoorLight) from this same box.
/// </summary>
public sealed partial class Level
{
    private const float OfficeX0 = -9f, OfficeX1 = 9f, OfficeZ0 = 22f, OfficeHeight = 3f, OfficeWall = 0.15f;
    private const float OfficeDoorX = 3f, OfficeDoorWidth = 1.1f, OfficeDoorHeight = 2.2f;

    /// <summary>The inside of the office (its own reverb room).</summary>
    private static Aabb FactoryOffice => new(new float3(OfficeX0, 0, OfficeZ0), new float3(OfficeX1, OfficeHeight, HalfLength));

    private void BuildFactoryOffice(Random random)
    {
        var lime = new float3(0.66f, 0.67f, 0.64f);

        // The north wall, with the door and the windows; its ends and the ceiling close the room against the south wall.
        (float A, float Y, float W, float H)[] windows = [(-7.6f, 1f, 2.6f, 1.2f), (-4.2f, 1f, 2.6f, 1.2f), (-0.8f, 1f, 2.6f, 1.2f), (5f, 1f, 2.6f, 1.2f)];
        var northZ = OfficeZ0 - OfficeWall;
        BrickWall(0, northZ, OfficeWall, OfficeX0 - OfficeWall, OfficeX1 + OfficeWall, 0, OfficeHeight + 0.14f, lime, Material.Whitewash,
            [.. windows, (OfficeDoorX, 0f, OfficeDoorWidth, OfficeDoorHeight)], lintels: false);
        BrickWall(2, OfficeX0 - OfficeWall, OfficeWall, OfficeZ0, HalfLength, 0, OfficeHeight + 0.14f, lime, Material.Whitewash, []);
        BrickWall(2, OfficeX1, OfficeWall, OfficeZ0, HalfLength, 0, OfficeHeight + 0.14f, lime, Material.Whitewash, []);
        foreach (var (a, y, w, h) in windows)
            OfficeWindow(a, a + w, y, y + h, northZ + OfficeWall / 2, random);
        _officeGlass = _officeGlassBuilder.Build();

        // Inside: limewash on the hall's wall, a worn carpet, the suspended ceiling and its light panels, a skirting; a flat roof over it all.
        FBox(new float3(OfficeX0, 0.1f, HalfLength - 0.02f), new float3(OfficeX1, OfficeHeight, HalfLength), lime, Material.Whitewash, collides: false);
        FBox(new float3(OfficeX0, 0, OfficeZ0), new float3(OfficeX1, 0.012f, HalfLength), new float3(0.26f, 0.27f, 0.28f), Material.Carpet, collides: false, blocksShots: false);
        FBox(new float3(OfficeX0, OfficeHeight, OfficeZ0), new float3(OfficeX1, OfficeHeight + 0.02f, HalfLength), new float3(0.62f, 0.61f, 0.57f), Material.CeilingTile);
        FBox(new float3(OfficeX0 - OfficeWall - 0.04f, OfficeHeight + 0.14f, northZ - 0.04f), new float3(OfficeX1 + OfficeWall + 0.04f, OfficeHeight + 0.2f, HalfLength), OldSteel, Material.RustyMetal);
        for (var x = -6f; x <= 6f; x += 4f)
            FBox(new float3(x - 0.3f, OfficeHeight - 0.015f, 25.4f), new float3(x + 0.3f, OfficeHeight, 26.6f), WindowTint, Material.Window, collides: false, blocksShots: false);
        var skirting = new float3(0.20f, 0.19f, 0.18f);
        FBox(new float3(OfficeX0, 0, HalfLength - 0.04f), new float3(OfficeX1, 0.1f, HalfLength), skirting, Material.Plain, collides: false);
        FBox(new float3(OfficeX0, 0, OfficeZ0), new float3(OfficeX0 + 0.02f, 0.1f, HalfLength), skirting, Material.Plain, collides: false);
        FBox(new float3(OfficeX1 - 0.02f, 0, OfficeZ0), new float3(OfficeX1, 0.1f, HalfLength), skirting, Material.Plain, collides: false);

        // The door: a steel frame, and the leaf swung right back into the room.
        var frame = new float3(0.24f, 0.25f, 0.26f);
        foreach (var x in (ReadOnlySpan<float>)[OfficeDoorX - 0.04f, OfficeDoorX + OfficeDoorWidth + 0.04f])
            FBox(new float3(x - 0.04f, 0, northZ - 0.02f), new float3(x + 0.04f, OfficeDoorHeight + 0.04f, OfficeZ0 + 0.02f), frame, Material.PaintedMetal, collides: false);
        FBox(new float3(OfficeDoorX - 0.08f, OfficeDoorHeight, northZ - 0.02f), new float3(OfficeDoorX + OfficeDoorWidth + 0.08f, OfficeDoorHeight + 0.08f, OfficeZ0 + 0.02f), frame, Material.PaintedMetal, collides: false);
        var leafX = OfficeDoorX + OfficeDoorWidth - 0.05f;
        FBox(new float3(leafX, 0.02f, OfficeZ0 + 0.02f), new float3(leafX + 0.045f, OfficeDoorHeight - 0.02f, OfficeZ0 + OfficeDoorWidth), new float3(0.40f, 0.42f, 0.44f), Material.PaintedMetal);
        foreach (var x in (ReadOnlySpan<float>)[leafX - 0.05f, leafX + 0.06f])
            FBox(new float3(x, 1.02f, OfficeZ0 + OfficeDoorWidth - 0.2f), new float3(x + 0.035f, 1.05f, OfficeZ0 + OfficeDoorWidth - 0.08f), new float3(0.6f, 0.6f, 0.58f), Material.Steel, collides: false);

        // What's been left: two desks with their chairs (users facing the windows), filing cabinets along the west wall.
        foreach (var (x, turn) in (ReadOnlySpan<(float, float)>)[(-3.5f, 0.5f), (-0.2f, -0.35f)])
        {
            var desk = Frame(new float3(x, 0, 26f), 0f);
            AddWorkstation(desk, random, occupied: false, pedestalRight: x > -1f);
            FrameCollider(desk, new float3(0, 0.37f, -0.375f), new float3(1.5f, 0.76f, 0.75f));
            var chair = float4x4.CreateRotationY(turn) * float4x4.CreateTranslation(0.1f, 0, 0.55f) * desk;
            AddOfficeChair(chair);
            FrameCollider(chair, new float3(0, 0.45f, 0.02f), new float3(0.5f, 0.9f, 0.5f), blocksShots: false);
        }
        for (var i = 0; i < 3; i++)
            AddFilingCabinet(new float3(OfficeX0 + 0.36f, 0, 25.2f + i * 0.5f), -float.Pi / 2, random);

        BuildOfficeBuckets(random);
    }

    private static readonly float3 OfficeGlassTint = new(0.62f, 0.70f, 0.72f);
    private readonly MeshBuilder _officeGlassBuilder = new();
    private readonly List<Aabb> _officeGlassPanes = [];
    private Mesh? _officeGlass;

    /// <summary>The office's clear panes, over what's behind them (drawn after everything solid).</summary>
    public void DrawGlass(Renderer renderer, Zones zones)
    {
        if ((zones & Zones.Warehouse) != 0 && _officeGlass is { } glass)
            renderer.DrawGlass(glass, OfficeGlassTint);
    }

    /// <summary>A steel-framed window across the wall at <paramref name="z"/>: every pane is either clear glass or gone.</summary>
    private void OfficeWindow(float x0, float x1, float y0, float y1, float z, Random random)
    {
        var frame = PB(Material.RustyMetal, OldSteel * 0.9f, occludes: false);
        const int rows = 3;
        var columns = int.Max(1, (int)float.Round((x1 - x0) / 0.55f));
        var w = (x1 - x0) / columns;
        var h = (y1 - y0) / rows;
        for (var i = 0; i <= columns; i++)
            OBox(frame, float4x4.Identity, new float3(x0 + i * w, (y0 + y1) / 2, z), new float3(0.045f, y1 - y0, 0.05f));
        for (var r = 0; r <= rows; r++)
            OBox(frame, float4x4.Identity, new float3((x0 + x1) / 2, y0 + r * h, z), new float3(x1 - x0, 0.045f, 0.05f));
        for (var i = 0; i < columns; i++)
            for (var r = 0; r < rows; r++)
            {
                if (random.NextDouble() < 0.4)
                    continue;   // broken out
                float xa = x0 + i * w + 0.02f, xb = x0 + (i + 1) * w - 0.02f, ya = y0 + r * h + 0.02f, yb = y0 + (r + 1) * h - 0.02f;
                _officeGlassBuilder.Quad(new float3(xa, ya, z), new float3(xb, ya, z), new float3(xb, yb, z), new float3(xa, yb, z), new float3(0, 0, -1));
                _officeGlassPanes.Add(new Aabb(new float3(xa, ya, z - 0.004f), new float3(xb, yb, z + 0.004f)));
            }
    }

    /// <summary>Buckets all round the room, close to the walls, leaving the door and the cabinets clear.</summary>
    private void BuildOfficeBuckets(Random random)
    {
        float3[] colors = [new(0.05f, 0.10f, 0.28f), new(0.06f, 0.06f, 0.07f)];   // dark blue, black
        const float inset = 0.3f;
        var spots = new List<float3>();
        void Row(float3 from, float3 to, float spacing)
        {
            var length = float3.Distance(from, to);
            var count = int.Max(1, (int)(length / spacing));
            for (var i = 0; i <= count; i++)
            {
                // Not evenly: they've been put wherever the ceiling dripped.
                var along = (i + ((float)random.NextDouble() - 0.5f) * 0.45f) / count;
                spots.Add(from + (to - from) * float.Clamp(along, 0f, 1f));
            }
        }
        Row(new float3(OfficeX0 + inset, 0, HalfLength - inset), new float3(OfficeX1 - inset, 0, HalfLength - inset), 4.3f);   // south
        Row(new float3(OfficeX1 - inset, 0, HalfLength - 1.5f), new float3(OfficeX1 - inset, 0, OfficeZ0 + inset), 6f);        // east
        Row(new float3(OfficeDoorX - 0.7f, 0, OfficeZ0 + inset), new float3(OfficeX0 + inset, 0, OfficeZ0 + inset), 5.5f);     // north, west of the door
        spots.Add(new float3(6.5f, 0, OfficeZ0 + inset));                  // north, east of the door
        spots.Add(new float3(OfficeX0 + inset, 0, 23.6f));                 // west, either side of the cabinets
        spots.Add(new float3(OfficeX0 + inset, 0, 28.3f));
        for (var i = 0; i < spots.Count; i++)
            AddPailBucket(spots[i], colors[i % colors.Length], (float)random.NextDouble() * float.Tau);
    }

    /// <summary>A plastic pail: tapered, with a rolled rim, dirty water in it, and its wire handle fallen to one side.</summary>
    private void AddPailBucket(float3 ground, float3 tint, float yaw)
    {
        const float height = 0.3f, r0 = 0.125f, r1 = 0.165f;
        var f = Frame(ground, yaw);
        var body = PB(Material.Plain, tint);
        OCyl(body, f, float3.Zero, new float3(0, height, 0), r0, r1, 18);
        OCyl(body, f, new float3(0, height - 0.018f, 0), new float3(0, height + 0.004f, 0), r1 + 0.008f, r1 + 0.008f, 18);
        // The inside: dark above the water line, then the water, just under the rim.
        OCyl(PB(Material.Plain, new float3(0.07f, 0.07f, 0.06f)), f, new float3(0, height + 0.004f, 0), new float3(0, height + 0.006f, 0), r1 - 0.008f, r1 - 0.008f, 18);
        // The handle: a wire arc from one ear to the other, resting on the rim.
        var wire = PB(Material.Steel, new float3(0.45f, 0.45f, 0.44f), occludes: false);
        var last = float3.Transform(new float3(-r1, height, 0), f);
        for (var i = 1; i <= 8; i++)
        {
            var a = float.Pi * i / 8;
            var point = float3.Transform(new float3(-r1 * float.Cos(a), height + 0.01f, r1 * float.Sin(a) + 0.02f * float.Sin(a)), f);
            AddTrunk(wire, last, point, 0.004f, 0.004f, 5);
            last = point;
        }
        AddCollider(ground + new float3(0, height / 2, 0), new float3(r1 * 1.8f, height, r1 * 1.8f));
    }
}
