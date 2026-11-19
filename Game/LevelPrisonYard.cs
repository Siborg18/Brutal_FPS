using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The prison's yard (as on the aerial photo: a ring path of grey pavers round a tarmac sports court, grass with young
/// trees, a curved hedge, lamp posts all round), the shipping container with the crimson portal standing on the
/// court, and the grounds outside the wings: the perimeter road, the staff path to the gatehouse and the wall.
/// </summary>
public sealed partial class Level
{
    // The ring path: an ellipse round the court (outer radii; it's RingWidth wide).
    private const float RingRx = 16f, RingRz = 12.2f, RingWidth = 1.8f;
    private const float CourtHalfX = 10.5f, CourtHalfZ = 7.5f;

    private static readonly float3 PaverGrey = new(0.50f, 0.49f, 0.47f);
    private static readonly float3 Tarmac = new(0.21f, 0.21f, 0.22f);
    private static readonly float3 LineWhite = new(0.85f, 0.85f, 0.82f);

    private void BuildPrisonYard(Random random)
    {
        // A strip of gravel along the foot of the walls all round the yard.
        var gravel = JDecal(Material.Gravel, new float3(0.46f, 0.45f, 0.43f));
        const float g = 0.8f;
        GroundRect(gravel, -CourtX, -CourtZ, CourtX, -CourtZ + g, 0.002f);
        GroundRect(gravel, -CourtX, CourtZ - g, CourtX, CourtZ, 0.002f);
        GroundRect(gravel, -CourtX, -CourtZ + g, -CourtX + g, CourtZ - g, 0.002f);
        GroundRect(gravel, CourtX - g, -CourtZ + g, CourtX, CourtZ - g, 0.002f);

        // The ring path, and paths from the doors to it and from it to the court.
        var pavers = JDecal(Material.Brick, PaverGrey);
        EllipseBand(pavers, RingRx - RingWidth, RingRz - RingWidth, RingRx, RingRz, 0.004f, 96);
        GroundRect(pavers, -0.8f, -CourtZ, 0.8f, -RingRz + 0.3f, 0.0041f);                    // north door
        GroundRect(pavers, -CourtX, -0.8f, -RingRx + 0.3f, 0.8f, 0.0041f);                    // west door
        GroundRect(pavers, -0.8f, CourtHalfZ, 0.8f, RingRz - RingWidth + 0.3f, 0.0041f);      // court, south
        GroundRect(pavers, -0.8f, -RingRz + RingWidth - 0.3f, 0.8f, -CourtHalfZ, 0.0041f);    // court, north
        GroundRect(pavers, -RingRx + RingWidth - 0.3f, -0.8f, -CourtHalfX, 0.8f, 0.0041f);    // court, west
        // From the corridor's yard door: a slanting path down to the ring.
        PathStrip(pavers, new float2(CourtX, YardDoorZ), new float2(13.6f, -5.6f), 1.6f, 0.0042f);
        GroundRect(pavers, CourtX - 2.2f, YardDoorZ - 0.9f, CourtX, YardDoorZ + 0.9f, 0.0043f);

        // The sports court: tarmac, white lines, a hoop at each end.
        GroundRect(JDecal(Material.Floor, Tarmac), -CourtHalfX, -CourtHalfZ, CourtHalfX, CourtHalfZ, 0.006f);
        var lines = JDecal(Material.Plain, LineWhite);
        const float lw = 0.06f, ly = 0.008f;
        void Line(float x0, float z0, float x1, float z1) => GroundRect(lines, x0, z0, x1, z1, ly);
        const float cx = CourtHalfX - 0.4f, cz = CourtHalfZ - 0.4f;
        Line(-cx, -cz, cx, -cz + lw);
        Line(-cx, cz - lw, cx, cz);
        Line(-cx, -cz, -cx + lw, cz);
        Line(cx - lw, -cz, cx, cz);
        Line(-lw / 2, -cz, lw / 2, cz);
        EllipseBand(lines, 1.75f, 1.75f, 1.81f, 1.81f, ly + 0.0005f, 48);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var x0 = side * cx;
            var x1 = side * (cx - 4.6f);
            Line(float.Min(x0, x1), -1.8f, float.Max(x0, x1), -1.8f + lw);
            Line(float.Min(x0, x1), 1.8f - lw, float.Max(x0, x1), 1.8f);
            Line(float.Min(x1, x1 + side * lw), -1.8f, float.Max(x1, x1 + side * lw), 1.8f);
            AddHoop(side);
        }

        // Lamp posts round the ring (as on the photo), and floodlights on tall poles at the court's corners.
        for (var i = 0; i < 12; i++)
        {
            var a = (i + 0.5f) * float.Tau / 12;
            var p = new float2(float.Cos(a) * (RingRx + 0.45f), float.Sin(a) * (RingRz + 0.45f));
            if (p.X > 10f && p.Y < -2f && p.Y > -12f)
                continue;   // (keep the way from the yard door clear)
            AddLampPost(p.X, p.Y);
        }
        foreach (var x in (ReadOnlySpan<float>)[-CourtHalfX - 0.5f, CourtHalfX + 0.5f])
            foreach (var z in (ReadOnlySpan<float>)[-CourtHalfZ - 0.5f, CourtHalfZ + 0.5f])
                AddFloodPole(x, z);

        // Young trees on the grass between the ring and the wings, a curved hedge along the south of the ring.
        ReadOnlySpan<float2> trees =
        [
            new(-18.5f, -12.5f), new(-18.8f, 12.0f), new(18.6f, 12.2f), new(-9.0f, 14.0f), new(8.5f, 14.0f),
            new(-9.5f, -14.0f), new(9.5f, -14.2f), new(-19.5f, 5.0f), new(19.2f, 3.5f),
        ];
        foreach (var t in trees)
            AddYardTree(t.X, t.Y, random);
        var hedge = JB(Material.Bush, new float3(0.16f, 0.24f, 0.10f));
        for (var a = 0.35f; a < float.Pi - 0.35f; a += 0.055f)
        {
            var p = new float2(float.Cos(a) * (RingRx + 1.2f), float.Sin(a) * (RingRz + 1.0f));
            hedge.Blob(JAt(p.X, 0.5f, p.Y), new float3(0.55f, 0.55f, 0.5f), random, 7, 4);
        }
        // Benches on the ring, bins beside them.
        BenchAlongX(-5f, RingRz + 0.55f, 1.8f);
        BenchAlongX(5f, RingRz + 0.55f, 1.8f);
        BenchAlongZ(-RingRx - 0.6f, -5f, 1.8f);
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(-3.6f, RingRz + 0.6f), (6.4f, RingRz + 0.6f), (-RingRx - 0.6f, -3.6f)])
        {
            OCyl(JB(Material.PaintedMetal, new float3(0.18f, 0.25f, 0.2f)), float4x4.Identity, JAt(x, 0, z), JAt(x, 0.8f, z), 0.22f, 0.24f, 14);
            AddStatic(new Aabb(JAt(x - 0.24f, 0, z - 0.24f), JAt(x + 0.24f, 0.8f, z + 0.24f)), default, visible: false);
        }

        // Outside the corridor's yard door: a step pad, a canopy, a lamp over it, a camera.
        JBox(CourtX - 0.9f, 2.45f, YardDoorZ - 1.0f, CourtX, 2.6f, YardDoorZ + 1.0f, new float3(0.62f, 0.61f, 0.58f), Material.Concrete);
        OBox(JB(Material.Plain, new float3(0.2f, 0.2f, 0.21f)), float4x4.Identity, JAt(CourtX - 0.08f, 2.3f, YardDoorZ + 0.9f), new float3(0.14f, 0.12f, 0.24f));
        OBox(JB(Material.Emissive, new float3(0.9f, 0.88f, 0.8f)), float4x4.Identity, JAt(CourtX - 0.08f, 2.235f, YardDoorZ + 0.9f), new float3(0.1f, 0.01f, 0.18f));
        AddCamera(JAt(CourtX - 0.05f, 3.1f, YardDoorZ - 1.4f), 0.25f);
        var frame = JB(Material.Plain, PrisonBlue);
        OBox(frame, float4x4.Identity, JAt(CourtX - 0.02f, 1.1f, YardDoorZ - 0.64f), new float3(0.04f, 2.2f, 0.08f));
        OBox(frame, float4x4.Identity, JAt(CourtX - 0.02f, 1.1f, YardDoorZ + 0.64f), new float3(0.04f, 2.2f, 0.08f));
        OBox(frame, float4x4.Identity, JAt(CourtX - 0.02f, 2.24f, YardDoorZ), new float3(0.04f, 0.08f, 1.36f));
    }

    /// <summary>A flat elliptical band (centred on the yard) between the inner and outer radii.</summary>
    private static void EllipseBand(MeshBuilder b, float rx0, float rz0, float rx1, float rz1, float y, int segments)
    {
        for (var i = 0; i < segments; i++)
        {
            var a0 = float.Tau * i / segments;
            var a1 = float.Tau * (i + 1) / segments;
            float3 P(float rx, float rz, float a) => JAt(float.Cos(a) * rx, y, float.Sin(a) * rz);
            b.Quad(P(rx0, rz0, a0), P(rx1, rz1, a0), P(rx1, rz1, a1), P(rx0, rz0, a1), Up);
        }
    }

    /// <summary>A straight path <paramref name="width"/> wide on the ground from a to b (local x, z).</summary>
    private static void PathStrip(MeshBuilder b, float2 a, float2 c, float width, float y)
    {
        var d = float2.Normalize(c - a);
        var side = new float2(-d.Y, d.X) * (width / 2);
        b.Quad(JAt(a.X - side.X, y, a.Y - side.Y), JAt(a.X + side.X, y, a.Y + side.Y), JAt(c.X + side.X, y, c.Y + side.Y), JAt(c.X - side.X, y, c.Y - side.Y), Up);
    }

    /// <summary>A slatted bench along x, centred at (x, z).</summary>
    private void BenchAlongX(float x, float z, float length)
    {
        var wood = JB(Material.Veneer, new float3(0.6f, 0.47f, 0.32f));
        var steel = JB(Material.Steel, new float3(0.25f, 0.27f, 0.28f));
        for (var k = -1; k <= 1; k++)
            OBox(wood, float4x4.Identity, JAt(x, 0.45f, z + k * 0.11f), new float3(length, 0.035f, 0.09f));
        OBox(wood, float4x4.Identity, JAt(x, 0.8f, z + 0.2f), new float3(length, 0.1f, 0.03f));
        foreach (var dx in (ReadOnlySpan<float>)[-length / 2 + 0.15f, length / 2 - 0.15f])
        {
            OBox(steel, float4x4.Identity, JAt(x + dx, 0.215f, z), new float3(0.04f, 0.43f, 0.32f));
            OBox(steel, float4x4.Identity, JAt(x + dx, 0.62f, z + 0.2f), new float3(0.04f, 0.4f, 0.03f));
        }
        AddStatic(new Aabb(JAt(x - length / 2, 0, z - 0.18f), JAt(x + length / 2, 0.47f, z + 0.22f)), default, visible: false);
    }

    /// <summary>A lamp post on the ring: a slim galvanised pole with a lantern head.</summary>
    private void AddLampPost(float x, float z)
    {
        var pole = JB(Material.Galvanized, new float3(0.42f, 0.43f, 0.44f));
        OCyl(pole, float4x4.Identity, JAt(x, 0, z), JAt(x, 0.5f, z), 0.1f, 0.08f, 10);
        OCyl(pole, float4x4.Identity, JAt(x, 0.5f, z), JAt(x, 4.0f, z), 0.06f, 0.045f, 10);
        var head = JB(Material.Plain, new float3(0.16f, 0.17f, 0.18f));
        OCyl(head, float4x4.Identity, JAt(x, 4.0f, z), JAt(x, 4.35f, z), 0.17f, 0.12f, 12);
        OCyl(JB(Material.Plain, new float3(0.82f, 0.82f, 0.8f)), float4x4.Identity, JAt(x, 3.88f, z), JAt(x, 4.0f, z), 0.14f, 0.17f, 12);
        AddStatic(new Aabb(JAt(x - 0.1f, 0, z - 0.1f), JAt(x + 0.1f, 4.3f, z + 0.1f)), default, visible: false);
    }

    /// <summary>A tall floodlight pole with two lamps aimed in at the court.</summary>
    private void AddFloodPole(float x, float z)
    {
        var pole = JB(Material.Galvanized, new float3(0.45f, 0.46f, 0.47f));
        OCyl(pole, float4x4.Identity, JAt(x, 0, z), JAt(x, 7.5f, z), 0.1f, 0.07f, 10);
        var toCourt = float3.Normalize(new float3(-x, 0, -z));
        var lamp = JB(Material.Plain, new float3(0.18f, 0.19f, 0.2f));
        foreach (var side in (ReadOnlySpan<float>)[-0.25f, 0.25f])
        {
            var across = new float3(-toCourt.Z, 0, toCourt.X) * side;
            var c = JAt(x, 7.35f, z) + across + toCourt * 0.2f;
            OBox(lamp, float4x4.CreateRotationY(float.Atan2(toCourt.X, toCourt.Z)) * float4x4.CreateRotationX(0.0f) * float4x4.CreateTranslation(c), float3.Zero, new float3(0.36f, 0.24f, 0.14f));
        }
        OBox(pole, float4x4.Identity, JAt(x, 7.35f, z) + toCourt * 0.1f, new float3(0.6f, 0.05f, 0.6f) * 0.8f);
        AddStatic(new Aabb(JAt(x - 0.1f, 0, z - 0.1f), JAt(x + 0.1f, 7.5f, z + 0.1f)), default, visible: false);
    }

    /// <summary>A basketball hoop on a pole just off the court's end (side -1 west, 1 east), facing in.</summary>
    private void AddHoop(float side)
    {
        var post = JB(Material.PaintedMetal, new float3(0.2f, 0.3f, 0.45f));
        var px = side * (CourtHalfX + 0.2f);
        OCyl(post, float4x4.Identity, JAt(px, 0, 0), JAt(px, 3.1f, 0), 0.08f, 0.08f, 12);
        OBox(post, float4x4.Identity, JAt(px - side * 0.55f, 3.05f, 0), new float3(1.1f, 0.08f, 0.08f));
        var bx = px - side * 1.1f;
        OBox(JB(Material.Plain, new float3(0.86f, 0.86f, 0.84f)), float4x4.Identity, JAt(bx, 3.3f, 0), new float3(0.04f, 1.05f, 1.8f));
        OBox(JB(Material.Plain, new float3(0.6f, 0.1f, 0.05f)), float4x4.Identity, JAt(bx - side * 0.022f, 3.15f, 0), new float3(0.006f, 0.45f, 0.59f));
        OBox(JB(Material.Plain, new float3(0.86f, 0.86f, 0.84f)), float4x4.Identity, JAt(bx - side * 0.024f, 3.17f, 0), new float3(0.006f, 0.39f, 0.53f));
        var rim = JB(Material.Steel, new float3(0.75f, 0.25f, 0.08f));
        var c = JAt(bx - side * 0.25f, 3.05f, 0);
        for (var i = 0; i < 12; i++)
        {
            var a0 = float.Tau * i / 12;
            var a1 = float.Tau * (i + 1) / 12;
            OCyl(rim, float4x4.Identity, c + new float3(float.Cos(a0), 0, float.Sin(a0)) * 0.23f, c + new float3(float.Cos(a1), 0, float.Sin(a1)) * 0.23f, 0.01f, 0.01f, 6);
        }
        AddStatic(new Aabb(JAt(px - 0.08f, 0, -0.08f), JAt(px + 0.08f, 3.1f, 0.08f)), default, visible: false);
    }

    /// <summary>A young tree: a slender trunk, a few branches, a loose crown of leaf blobs.</summary>
    private void AddYardTree(float x, float z, Random random)
    {
        var bark = JB(Material.Bark, new float3(0.34f, 0.31f, 0.27f));
        var h = 2.6f + 0.8f * (float)random.NextDouble();
        OCyl(bark, float4x4.Identity, JAt(x, -0.1f, z), JAt(x, h, z), 0.1f, 0.05f, 9);
        var leaves = JB(Material.Bush, new float3(0.20f, 0.30f, 0.12f) * (0.85f + 0.3f * (float)random.NextDouble()));
        for (var i = 0; i < 6; i++)
        {
            var a = (float)random.NextDouble() * float.Tau;
            var r = 0.35f + 0.55f * (float)random.NextDouble();
            var y = h - 0.1f + 0.9f * (float)random.NextDouble();
            var c = JAt(x + float.Cos(a) * r, y, z + float.Sin(a) * r);
            OCyl(bark, float4x4.Identity, JAt(x, h - 0.6f, z), c, 0.03f, 0.015f, 6);
            leaves.Blob(c, new float3(0.75f, 0.6f, 0.75f) * (0.8f + 0.4f * (float)random.NextDouble()), random, 8, 5);
        }
        leaves.Blob(JAt(x, h + 0.7f, z), new float3(0.8f, 0.7f, 0.8f), random, 8, 5);
        // A stake and a tie, and a ring of bare earth round the foot.
        OCyl(JB(Material.Wood, new float3(0.5f, 0.4f, 0.28f)), float4x4.Identity, JAt(x + 0.18f, 0, z), JAt(x + 0.18f, 1.4f, z), 0.025f, 0.025f, 6);
        EllipseBandAt(JDecal(Material.RedDirt, new float3(0.28f, 0.23f, 0.18f)), x, z, 0.6f, 0.003f);
        AddStatic(new Aabb(JAt(x - 0.1f, 0, z - 0.1f), JAt(x + 0.1f, h, z + 0.1f)), default, visible: false);
    }

    /// <summary>A disc of radius r on the ground at (x, z).</summary>
    private static void EllipseBandAt(MeshBuilder b, float x, float z, float r, float y)
    {
        const int n = 20;
        for (var i = 0; i < n; i++)
        {
            var a0 = float.Tau * i / n;
            var a1 = float.Tau * (i + 1) / n;
            b.Triangle(JAt(x, y, z), JAt(x + float.Cos(a0) * r, y, z + float.Sin(a0) * r), JAt(x + float.Cos(a1) * r, y, z + float.Sin(a1) * r), Up);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // The shipping container in the middle of the yard, with the crimson portal in a false wall halfway along it and a
    // satellite dish on the roof at its rear end.
    // ------------------------------------------------------------------------------------------------

    private void BuildPrisonContainer()
    {
        _buildingContainer = true;
        const float skin = 0.03f;
        var inside = ContainerInteriorTint;
        // Floor (drawn only: you stand on the court under it, so you walk straight in) and the steel cross members under it.
        OBox(JB(Material.Steel, new float3(0.3f, 0.28f, 0.26f)), float4x4.Identity, JAt(0, 0.012f, 0), new float3(PcLength, 0.02f, PcWidth));
        // Sides: a pale inside skin and the painted outside skin.
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var zi = side * PcInnerHalf;
            var zo = side * PcWidth / 2;
            JBox(PcWestX, 0.02f, zi, PcEastX - 0.16f, PcInnerHeight, zi + side * skin, inside, Material.Corrugated);
            JBox(PcWestX, 0.02f, zo - side * skin, PcEastX - 0.16f, PcHeight, zo, PcBlue, Material.Corrugated);
            // Bottom and top side rails.
            JBox(PcWestX, 0.02f, zo - side * 0.01f, PcEastX, 0.2f, zo + side * 0.03f, PcBlue * 0.8f, Material.Steel, collides: false);
            JBox(PcWestX, PcHeight - 0.12f, zo - side * 0.01f, PcEastX, PcHeight, zo + side * 0.03f, PcBlue * 0.8f, Material.Steel, collides: false);
        }
        // Roof.
        JBox(PcWestX, PcInnerHeight, -PcInnerHalf, PcEastX - 0.16f, PcInnerHeight + skin, PcInnerHalf, inside, Material.Corrugated);
        JBox(PcWestX, PcHeight - 0.05f, -PcWidth / 2, PcEastX, PcHeight, PcWidth / 2, PcBlue, Material.Corrugated);
        // The closed end: inside skin and outside skin.
        JBox(PcWestX + PcEndWall / 2, 0.02f, -PcInnerHalf, PcWestX + PcEndWall, PcInnerHeight, PcInnerHalf, inside, Material.Corrugated);
        JBox(PcWestX, 0.02f, -PcWidth / 2, PcWestX + PcEndWall / 2, PcHeight, PcWidth / 2, PcBlue, Material.Corrugated);
        // The false wall halfway along (the portal is cut into it), hiding the sealed rear half.
        JBox(PcFalseWallX - PcEndWall, 0.02f, -PcInnerHalf, PcFalseWallX, PcInnerHeight, PcInnerHalf, inside, Material.Corrugated);
        // The door end's frame: corner posts and a header, set back behind the doors.
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            JBox(PcEastX - 0.22f, 0.02f, side * (PcWidth / 2 - 0.16f), PcEastX - 0.06f, PcHeight, side * PcWidth / 2, PcBlue * 0.75f, Material.Steel);
        JBox(PcEastX - 0.22f, PcInnerHeight, -PcWidth / 2 + 0.16f, PcEastX - 0.06f, PcHeight, PcWidth / 2 - 0.16f, PcBlue * 0.75f, Material.Steel);
        // Corner castings.
        var cast = JB(Material.Steel, new float3(0.22f, 0.21f, 0.2f));
        foreach (var x in (ReadOnlySpan<float>)[PcWestX + 0.09f, PcEastX - 0.09f])
            foreach (var z in (ReadOnlySpan<float>)[-PcWidth / 2 + 0.08f, PcWidth / 2 - 0.08f])
                foreach (var y in (ReadOnlySpan<float>)[0.07f, PcHeight - 0.06f])
                    OBox(cast, float4x4.Identity, JAt(x, y, z), new float3(0.18f, 0.12f, 0.17f));
        // Its marking: an owner code and number on both sides and on the door end, a small "PORTAL" warning.
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            AddLettering("PRTU 000112 2", JAt(PcEastX - 2.2f * 1, 2.15f, side * (PcWidth / 2 + 0.031f)), new float3(0, 0, side), 0.2f, Material.Plain, new float3(0.88f, 0.88f, 0.86f));
            AddLettering("45G1", JAt(PcEastX - 2.2f, 1.85f, side * (PcWidth / 2 + 0.031f)), new float3(0, 0, side), 0.12f, Material.Plain, new float3(0.88f, 0.88f, 0.86f));
        }
        // Inside: the portal's power feed along the floor to the false wall (cables under a yellow and black protector).
        var yellow = JB(Material.Plain, new float3(0.75f, 0.6f, 0.05f));
        var black = JB(Material.Plain, new float3(0.04f, 0.04f, 0.045f));
        const float psuX = PcFalseWallX + 1.6f;   // (clear of the portal's ring)
        var k = 0;
        for (var x = psuX + 0.2f; x < PcEastX - 0.6f; x += 0.5f, k++)
            OBox(k % 2 == 0 ? yellow : black, float4x4.Identity, JAt(x + 0.25f, 0.04f, PcInnerHalf - 0.25f), new float3(0.5f, 0.04f, 0.28f));
        OBox(JB(Material.Steel, new float3(0.55f, 0.56f, 0.58f)), float4x4.Identity, JAt(psuX, 0.5f, PcInnerHalf - 0.12f), new float3(0.4f, 0.8f, 0.2f));
        OBox(JB(Material.Emissive, new float3(1.0f, 0.15f, 0.2f)), float4x4.Identity, JAt(psuX, 0.78f, PcInnerHalf - 0.222f), new float3(0.03f, 0.03f, 0.004f));
        // Its feed to the portal: two thick cables along the wall into the ring's lower edge.
        var cable = JB(Material.Plain, new float3(0.05f, 0.05f, 0.055f));
        foreach (var dz in (ReadOnlySpan<float>)[0f, 0.07f])
        {
            OCyl(cable, float4x4.Identity, JAt(psuX - 0.2f, 0.15f, PcInnerHalf - 0.08f - dz), JAt(PcFalseWallX + 0.3f, 0.05f, PcInnerHalf - 0.08f - dz), 0.025f, 0.025f, 6);
            OCyl(cable, float4x4.Identity, JAt(PcFalseWallX + 0.3f, 0.05f, PcInnerHalf - 0.08f - dz), JAt(PcFalseWallX + 0.06f, 0.12f, 0.75f - dz), 0.025f, 0.025f, 6);
        }
        AddStatic(new Aabb(JAt(psuX - 0.2f, 0, PcInnerHalf - 0.22f), JAt(psuX + 0.2f, 0.9f, PcInnerHalf)), default, visible: false);
        AddPrisonContainerDish();
        _buildingContainer = false;
        FinishContainerMeshes();
    }

    // ------------------------------------------------------------------------------------------------
    // Outside the wings: the perimeter road, the staff path to the gatehouse, the wall, trees beyond it.
    // ------------------------------------------------------------------------------------------------

    private const float WallX = 48f, WallZ0 = -44f, WallZ1 = 56f, WallH = 5.5f;

    private void BuildPerimeter(Random random)
    {
        // The road round the wings, a dashed centre line; the staff path and an apron in front of the gatehouse.
        var road = JDecal(Material.Floor, Tarmac);
        GroundRect(road, -43, -39, 43, -34, 0.004f);
        GroundRect(road, -43, 44, 43, 49, 0.004f);
        GroundRect(road, -43, -34, -38, 44, 0.004f);
        GroundRect(road, 38, -34, 43, 44, 0.004f);
        var dash = JDecal(Material.Plain, LineWhite * 0.9f);
        for (var x = -40f; x < 40f; x += 3f)
        {
            GroundRect(dash, x, 46.45f, x + 1.5f, 46.55f, 0.006f);
            GroundRect(dash, x, -36.55f, x + 1.5f, -36.45f, 0.006f);
        }
        for (var z = -31f; z < 42f; z += 3f)
        {
            GroundRect(dash, -40.55f, z, -40.45f, z + 1.5f, 0.006f);
            GroundRect(dash, 40.45f, z, 40.55f, z + 1.5f, 0.006f);
        }
        var pavers = JDecal(Material.Brick, PaverGrey);
        GroundRect(pavers, GateX0 + 1f, GateZ1, GateX1 - 1f, GateZ1 + 2.5f, 0.005f);
        GroundRect(pavers, EntranceX - 1.2f, GateZ1 + 2.5f, EntranceX + 1.2f, 44f, 0.005f);
        // A gravel strip round the outside of the wings.
        var gravel = JDecal(Material.Gravel, new float3(0.46f, 0.45f, 0.43f));
        GroundRect(gravel, -WingOutX - 0.8f, NorthOutZ - 0.8f, WingOutX + 0.8f, NorthOutZ, 0.002f);
        GroundRect(gravel, -WingOutX - 0.8f, NorthOutZ, -WingOutX, SouthOutZ + 0.8f, 0.002f);
        GroundRect(gravel, WingOutX, NorthOutZ, WingOutX + 0.8f, SouthOutZ + 0.8f, 0.002f);
        GroundRect(gravel, -WingOutX, SouthOutZ, GateX0, SouthOutZ + 0.8f, 0.002f);
        GroundRect(gravel, GateX1, SouthOutZ, WingOutX, SouthOutZ + 0.8f, 0.002f);

        // The wall: tall precast concrete panels with a round anti-climb cap, a buttress at every joint.
        var concrete = new float3(0.60f, 0.59f, 0.56f);
        JBox(-WallX - 0.4f, 0, WallZ0 - 0.4f, WallX + 0.4f, WallH, WallZ0, concrete, Material.Concrete);
        JBox(-WallX - 0.4f, 0, WallZ1, WallX + 0.4f, WallH, WallZ1 + 0.4f, concrete, Material.Concrete);
        JBox(-WallX - 0.4f, 0, WallZ0, -WallX, WallH, WallZ1, concrete, Material.Concrete);
        JBox(WallX, 0, WallZ0, WallX + 0.4f, WallH, WallZ1, concrete, Material.Concrete);
        var cap = JB(Material.Plain, new float3(0.68f, 0.67f, 0.64f));
        OCyl(cap, float4x4.Identity, JAt(-WallX - 0.4f, WallH + 0.25f, WallZ0 - 0.2f), JAt(WallX + 0.4f, WallH + 0.25f, WallZ0 - 0.2f), 0.4f, 0.4f, 14);
        OCyl(cap, float4x4.Identity, JAt(-WallX - 0.4f, WallH + 0.25f, WallZ1 + 0.2f), JAt(WallX + 0.4f, WallH + 0.25f, WallZ1 + 0.2f), 0.4f, 0.4f, 14);
        OCyl(cap, float4x4.Identity, JAt(-WallX - 0.2f, WallH + 0.25f, WallZ0), JAt(-WallX - 0.2f, WallH + 0.25f, WallZ1), 0.4f, 0.4f, 14);
        OCyl(cap, float4x4.Identity, JAt(WallX + 0.2f, WallH + 0.25f, WallZ0), JAt(WallX + 0.2f, WallH + 0.25f, WallZ1), 0.4f, 0.4f, 14);
        var joint = JB(Material.Concrete, concrete * 0.92f);
        for (var x = -WallX + 4f; x < WallX; x += 4f)
        {
            OBox(joint, float4x4.Identity, JAt(x, WallH / 2, WallZ0 + 0.1f), new float3(0.3f, WallH, 0.2f));
            OBox(joint, float4x4.Identity, JAt(x, WallH / 2, WallZ1 - 0.1f), new float3(0.3f, WallH, 0.2f));
        }
        for (var z = WallZ0 + 4f; z < WallZ1; z += 4f)
        {
            OBox(joint, float4x4.Identity, JAt(-WallX + 0.1f, WallH / 2, z), new float3(0.2f, WallH, 0.3f));
            OBox(joint, float4x4.Identity, JAt(WallX - 0.1f, WallH / 2, z), new float3(0.2f, WallH, 0.3f));
        }
        // Tall floodlight masts in the corners, lamp posts along the road by the entrance, a camera mast.
        foreach (var x in (ReadOnlySpan<float>)[-45f, 45f])
            foreach (var z in (ReadOnlySpan<float>)[-41f, 53f])
            {
                var pole = JB(Material.Galvanized, new float3(0.45f, 0.46f, 0.47f));
                OCyl(pole, float4x4.Identity, JAt(x, 0, z), JAt(x, 14f, z), 0.18f, 0.1f, 10);
                OBox(JB(Material.Plain, new float3(0.18f, 0.19f, 0.2f)), float4x4.Identity, JAt(x - float.Sign(x) * 0.4f, 14f, z - float.Sign(z) * 0.4f), new float3(0.9f, 0.3f, 0.6f));
                AddStatic(new Aabb(JAt(x - 0.2f, 0, z - 0.2f), JAt(x + 0.2f, 14f, z + 0.2f)), default, visible: false);
            }
        foreach (var x in (ReadOnlySpan<float>)[8f, 30f, -10f, -28f])
            AddLampPost(x, 43.3f);
        // A row of bike racks and a bin by the staff path.
        var rack = JB(Material.Galvanized, new float3(0.55f, 0.56f, 0.57f));
        for (var i = 0; i < 4; i++)
        {
            var x = 24.5f + i * 0.8f;
            OCyl(rack, float4x4.Identity, JAt(x, 0, 40.0f), JAt(x, 0.8f, 40.0f), 0.025f, 0.025f, 8);
            OCyl(rack, float4x4.Identity, JAt(x, 0.8f, 40.0f), JAt(x, 0.8f, 40.6f), 0.025f, 0.025f, 8);
            OCyl(rack, float4x4.Identity, JAt(x, 0, 40.6f), JAt(x, 0.8f, 40.6f), 0.025f, 0.025f, 8);
        }
        OCyl(JB(Material.PaintedMetal, new float3(0.18f, 0.25f, 0.2f)), float4x4.Identity, JAt(19.2f, 0, 38.0f), JAt(19.2f, 0.8f, 38.0f), 0.22f, 0.24f, 14);
        AddStatic(new Aabb(JAt(18.95f, 0, 37.75f), JAt(19.45f, 0.8f, 38.25f)), default, visible: false);

        // Trees beyond the wall (their crowns show over it), and low scrub along its foot outside.
        var crowns = JB(Material.Bush, new float3(0.13f, 0.19f, 0.09f));
        var trunks = JB(Material.Bark, new float3(0.3f, 0.27f, 0.24f));
        void Tree(float x, float z)
        {
            var h = 9f + 6f * (float)random.NextDouble();
            OCyl(trunks, float4x4.Identity, JAt(x, 0, z), JAt(x, h * 0.6f, z), 0.3f, 0.18f, 8);
            for (var i = 0; i < 4; i++)
                crowns.Blob(JAt(x + ((float)random.NextDouble() - 0.5f) * 3f, h * (0.65f + 0.15f * i), z + ((float)random.NextDouble() - 0.5f) * 3f),
                    new float3(2.6f, 2.0f, 2.6f) * (0.8f + 0.4f * (float)random.NextDouble()), random, 9, 5);
        }
        for (var x = -70f; x <= 70f; x += 7f + 3f * (float)random.NextDouble())
        {
            Tree(x, WallZ1 + 9f + 6f * (float)random.NextDouble());
            Tree(x, WallZ0 - 9f - 6f * (float)random.NextDouble());
        }
        for (var z = -50f; z <= 60f; z += 7f + 3f * (float)random.NextDouble())
        {
            Tree(-WallX - 9f - 6f * (float)random.NextDouble(), z);
            Tree(WallX + 9f + 6f * (float)random.NextDouble(), z);
        }
    }
}
