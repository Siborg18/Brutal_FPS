using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The roadhouse's motel: a long, low block facing the road, seven rooms in a row under one roof. Each room has its own
/// teal door (E opens or shuts it; it swings in, hinged on its north edge) and a window with the curtains drawn. Inside:
/// a double bed against the bathroom's partition, a dresser with an old TV, an armchair by the window, a lamp on the
/// bedside table, and a little bathroom at the back. The rooms are dim (<see cref="MotelShadeVolumes"/>: no sun, the sky's
/// light fading away from the door).
/// <para>
/// Someone is waiting behind every door (<see cref="Npcs"/>, the motel's gunmen): when the player comes within
/// <see cref="Npcs.MotelAmbushRange"/> of a room's door, it's thrown open (<see cref="SpringMotelDoor"/>) and whoever's
/// inside comes out shooting. A respawn shuts them all again (<see cref="ShutMotelDoors"/>).
/// </para>
/// </summary>
public sealed partial class Level
{
    // The block, measured from OutbackOrigin: its road-side (west) face at x = MotelX0, running north (-z) to south (+z).
    private const float MotelX0 = 22f, MotelZ0 = -122f, MotelLength = 26f, MotelDepth = 6f, MotelHeight = 2.9f;
    private const float MotelWall = 0.15f;           // the outside walls
    private const float MotelParty = 0.12f;          // the walls between the rooms
    private const float MotelDoorW = 1.0f, MotelDoorH = 2.05f;
    private const float MotelDoorOffset = -0.9f;     // the door's middle, from the room's (the window is on the other side)
    private const float MotelBathX = 4.3f;           // the bathroom's partition, in from the road-side face

    /// <summary>The motel's rooms, numbered 0 (the north end) to 6.</summary>
    public const int MotelRooms = 7;

    private static readonly float3 MotelRoomWall = new(0.76f, 0.64f, 0.52f);   // faded salmon
    private static readonly float3 MotelCeiling = new(0.80f, 0.78f, 0.72f);
    private static readonly float3 MotelCarpet = new(0.34f, 0.19f, 0.15f);
    private static readonly float3 MotelTimber = new(0.36f, 0.24f, 0.15f);
    private static readonly float3 MotelLinen = new(0.82f, 0.80f, 0.74f);
    private static readonly float3 MotelPorcelain = new(0.86f, 0.86f, 0.83f);
    private static readonly float3[] MotelSpreads = [new(0.55f, 0.32f, 0.18f), new(0.24f, 0.36f, 0.30f), new(0.50f, 0.42f, 0.22f), new(0.30f, 0.28f, 0.42f)];
    private static readonly float3[] MotelCurtains = [new(0.62f, 0.40f, 0.18f), new(0.38f, 0.46f, 0.30f), new(0.58f, 0.30f, 0.24f)];

    private readonly List<PrisonDoor> _motelDoors = [];

    /// <summary>"[E] Open door" / "[E] Close door" by a motel room's door, otherwise null.</summary>
    public string? MotelDoorPrompt { get; private set; }

    private static float MotelRoomZ(int room) => OutbackOrigin.Z + MotelZ0 + (room + 0.5f) * MotelLength / MotelRooms;

    /// <summary>The middle of a room's doorway, on the ground (world).</summary>
    public static float3 MotelDoorAt(int room) =>
        new(OutbackOrigin.X + MotelX0 + MotelWall / 2, 0, MotelRoomZ(room) + MotelDoorOffset);

    /// <summary>Gunmen waiting in each room.</summary>
    public const int MotelGunmenPerRoom = 2;

    /// <summary>
    /// Where a room's gunmen wait: the first a couple of metres back from the shut door, the second (<paramref name="spot"/> 1)
    /// off to the side between the foot of the bed and the armchair, clear of the door's swing.
    /// </summary>
    public static float3 MotelHideAt(int room, int spot = 0) => spot == 0
        ? new(OutbackOrigin.X + MotelX0 + 1.9f, 0, MotelRoomZ(room) + MotelDoorOffset + 0.15f)
        : new(OutbackOrigin.X + MotelX0 + 1.6f, 0, MotelRoomZ(room) + 0.3f);

    /// <summary>The way out of the rooms (towards the road, -x), as a heading.</summary>
    public const float MotelFacing = -float.Pi / 2;

    /// <summary>The room's inside along z (between the inner faces of its side walls), world.</summary>
    private static (float Lo, float Hi) MotelRoomSpan(int room)
    {
        var a = OutbackOrigin.Z + MotelZ0 + room * MotelLength / MotelRooms;
        var b = a + MotelLength / MotelRooms;
        return (a + (room == 0 ? MotelWall : MotelParty / 2), b - (room == MotelRooms - 1 ? MotelWall : MotelParty / 2));
    }

    /// <summary>Each room's dark: no sun gets in, and the sky's light dies away from its door.</summary>
    public static ShadeVolume[] MotelShadeVolumes
    {
        get
        {
            var x0 = OutbackOrigin.X + MotelX0;
            var volumes = new ShadeVolume[MotelRooms];
            for (var i = 0; i < MotelRooms; i++)
            {
                var (lo, hi) = MotelRoomSpan(i);
                var door = MotelDoorAt(i);
                volumes[i] = new ShadeVolume(
                    new Aabb(new float3(x0 + MotelWall - 0.02f, -0.05f, lo - 0.02f), new float3(x0 + MotelDepth - MotelWall + 0.02f, MotelHeight - 0.08f, hi + 0.02f)),
                    new float3(door.X - 0.4f, 1.2f, door.Z), 3.8f, 0.1f, NoSun: true);
            }
            return volumes;
        }
    }

    /// <summary>
    /// The block: rendered cream walls, a green fascia, a covered walkway on posts, the rooms' doors and windows, and the rooms
    /// themselves.
    /// </summary>
    private void BuildMotel()
    {
        var o = OutbackOrigin;
        const float h = MotelHeight, t = MotelWall;
        var minX = o.X + MotelX0;
        var maxX = minX + MotelDepth;
        var minZ = o.Z + MotelZ0;
        var maxZ = minZ + MotelLength;
        var midZ = (minZ + maxZ) / 2;

        // The shell. The road-side wall comes in pieces between the doors, with one run over all their heads.
        var z = minZ;
        for (var i = 0; i < MotelRooms; i++)
        {
            var d0 = MotelDoorAt(i).Z - MotelDoorW / 2;
            AddMinMax(new float3(minX, 0, z), new float3(minX + t, MotelDoorH, d0), MotelCream, Material.Concrete);
            z = d0 + MotelDoorW;
        }
        AddMinMax(new float3(minX, 0, z), new float3(minX + t, MotelDoorH, maxZ), MotelCream, Material.Concrete);
        AddMinMax(new float3(minX, MotelDoorH, minZ), new float3(minX + t, h, maxZ), MotelCream, Material.Concrete);
        AddMinMax(new float3(maxX - t, 0, minZ), new float3(maxX, h, maxZ), MotelCream, Material.Concrete);
        AddMinMax(new float3(minX + t, 0, minZ), new float3(maxX - t, h, minZ + t), MotelCream, Material.Concrete);
        AddMinMax(new float3(minX + t, 0, maxZ - t), new float3(maxX - t, h, maxZ), MotelCream, Material.Concrete);
        AddMinMax(new float3(minX + t, h - 0.1f, minZ + t), new float3(maxX - t, h, maxZ - t), MotelCeiling, Material.WallPaint);

        // The roof, the fascia, the walkway and its posts.
        Add(new float3((minX + maxX) / 2 - 0.4f, h + 0.12f, midZ), new float3(MotelDepth + 2.6f, 0.24f, MotelLength + 0.4f), new float3(0.42f, 0.42f, 0.4f), Material.Steel, collides: false);
        Add(new float3(minX - 2.2f, h + 0.05f, midZ), new float3(0.12f, 0.42f, MotelLength + 0.4f), MotelTrimGreen, Material.PaintedMetal, collides: false);
        Add(new float3(maxX + 0.06f, h + 0.05f, midZ), new float3(0.12f, 0.42f, MotelLength + 0.4f), MotelTrimGreen, Material.PaintedMetal, collides: false);
        Add(new float3(minX - 1.1f, 0.06f, midZ), new float3(2.2f, 0.12f, MotelLength), new float3(0.6f, 0.58f, 0.54f), Material.Concrete, collides: false);
        for (var i = 0; i <= 6; i++)
            Add(new float3(minX - 2.1f, h / 2, minZ + 0.3f + i * (MotelLength - 0.6f) / 6), new float3(0.1f, h, 0.1f), MotelTrimGreen, Material.PaintedMetal);

        for (var i = 0; i < MotelRooms; i++)
            BuildMotelRoom(i, minX, maxX);

        // An air-conditioner box on the back wall of every other room.
        for (var i = 0; i < MotelRooms; i += 2)
        {
            var at = new float3(maxX + 0.3f, 1.9f, MotelRoomZ(i));
            if (TryModel("ac_unit") is { } ac)
                AddModel(ac, float4x4.CreateTranslation(at), new float3(0.7f, 0.7f, 0.68f)); // grille faces +X
            else
                Add(at, new float3(0.6f, 0.45f, 0.7f), new float3(0.7f, 0.7f, 0.68f), Material.PaintedMetal, collides: false);
        }

        FlushPoliceBatches();   // (the merged-mesh batches are shared with the police station's builder)
    }

    /// <summary>One room: its wall to the next room south, its linings, furniture, bathroom, window and door.</summary>
    private void BuildMotelRoom(int room, float minX, float maxX)
    {
        var id = float4x4.Identity;
        const float h = MotelHeight, t = MotelWall;
        var zc = MotelRoomZ(room);
        var (lo, hi) = MotelRoomSpan(room);
        var inX = minX + t;                 // the inside face of the road-side wall
        var backX = maxX - t;               // and of the back wall
        var ceiling = h - 0.1f;
        var doorZ = MotelDoorAt(room).Z;
        float d0 = doorZ - MotelDoorW / 2, d1 = doorZ + MotelDoorW / 2;
        float X(float u) => minX + u;       // in from the road-side face
        float Z(float v) => zc + v;         // along from the room's middle (+: south)
        var paint = JB(Material.WallPaint, MotelRoomWall);
        var timber = JB(Material.Veneer, MotelTimber);
        var linen = JB(Material.Cloth, MotelLinen);

        // The wall to the next room (solid; the merged mesh is what's seen and shot, the box what stops people).
        if (room < MotelRooms - 1)
        {
            var wz = OutbackOrigin.Z + MotelZ0 + (room + 1) * MotelLength / MotelRooms;
            var c = new float3((inX + backX) / 2, ceiling / 2, wz);
            var size = new float3(backX - inX, ceiling, MotelParty);
            OBox(paint, id, c, size);
            AddCollider(c, size);
        }

        // Plaster linings on the outside walls' inside faces (the road side round the door), and on the end walls.
        const float lining = 0.01f;
        OBox(paint, id, new float3(inX + lining / 2, MotelDoorH / 2, (lo + d0) / 2), new float3(lining, MotelDoorH, d0 - lo));
        OBox(paint, id, new float3(inX + lining / 2, MotelDoorH / 2, (d1 + hi) / 2), new float3(lining, MotelDoorH, hi - d1));
        OBox(paint, id, new float3(inX + lining / 2, (MotelDoorH + ceiling) / 2, (lo + hi) / 2), new float3(lining, ceiling - MotelDoorH, hi - lo));
        OBox(paint, id, new float3(backX - lining / 2, ceiling / 2, (lo + hi) / 2), new float3(lining, ceiling, hi - lo));
        if (room == 0)
            OBox(paint, id, new float3((inX + backX) / 2, ceiling / 2, lo + lining / 2), new float3(backX - inX - 2 * lining, ceiling, lining));
        if (room == MotelRooms - 1)
            OBox(paint, id, new float3((inX + backX) / 2, ceiling / 2, hi - lining / 2), new float3(backX - inX - 2 * lining, ceiling, lining));
        // A skirting board along the bedroom's side walls.
        var skirting = JB(Material.Plain, MotelRoomWall * 0.55f);
        OBox(skirting, id, new float3((inX + X(MotelBathX) - 0.05f) / 2, 0.06f, lo + 0.006f), new float3(X(MotelBathX) - 0.05f - inX, 0.1f, 0.012f));
        OBox(skirting, id, new float3((inX + X(MotelBathX) - 0.05f) / 2, 0.06f, hi - 0.006f), new float3(X(MotelBathX) - 0.05f - inX, 0.1f, 0.012f));

        // Floors: worn carpet in the bedroom, tiles in the bathroom.
        var bath = X(MotelBathX);
        OBox(JB(Material.Carpet, MotelCarpet), id, new float3((inX + bath) / 2, 0.009f, zc), new float3(bath - inX, 0.018f, hi - lo));
        OBox(JB(Material.Plain, new float3(0.70f, 0.72f, 0.70f)), id, new float3((bath + backX) / 2, 0.009f, zc), new float3(backX - bath, 0.018f, hi - lo));

        // The bathroom's partition, its doorway at the north end.
        {
            const float px = MotelBathX, pw = 0.1f;
            float dz0 = Z(-1.65f), dz1 = Z(-0.85f);
            void Part(float za, float zb, float y0, float y1)
            {
                var c = new float3(X(px), (y0 + y1) / 2, (za + zb) / 2);
                var size = new float3(pw, y1 - y0, zb - za);
                OBox(paint, id, c, size);
                AddCollider(c, size);
            }
            Part(lo, dz0, 0, ceiling);
            Part(dz1, hi, 0, ceiling);
            Part(dz0, dz1, MotelDoorH, ceiling);
            // A plain frame round the doorway.
            foreach (var zz in (ReadOnlySpan<float>)[dz0 + 0.025f, dz1 - 0.025f])
                OBox(timber, id, new float3(X(px), MotelDoorH / 2, zz), new float3(pw + 0.03f, MotelDoorH, 0.05f));
            OBox(timber, id, new float3(X(px), MotelDoorH - 0.025f, (dz0 + dz1) / 2), new float3(pw + 0.03f, 0.05f, dz1 - dz0));
        }

        // The bed, its head against the partition: a timber base, the mattress, a spread and two pillows.
        {
            float u0 = 2.35f, u1 = MotelBathX - 0.1f, v0 = 0.12f, v1 = 1.55f;
            var spread = JB(Material.Cloth, MotelSpreads[room % MotelSpreads.Length]);
            OBox(timber, id, new float3(X((u0 + u1) / 2), 0.17f, Z((v0 + v1) / 2)), new float3(u1 - u0, 0.34f, v1 - v0));
            OBox(linen, id, new float3(X((u0 + u1) / 2 + 0.02f), 0.45f, Z((v0 + v1) / 2)), new float3(u1 - u0 - 0.08f, 0.22f, v1 - v0 - 0.06f));
            OBox(spread, id, new float3(X(u0 + 0.7f), 0.5f, Z((v0 + v1) / 2)), new float3(1.44f, 0.16f, v1 - v0 + 0.02f));
            OBox(spread, id, new float3(X(u0 - 0.01f), 0.38f, Z((v0 + v1) / 2)), new float3(0.02f, 0.3f, v1 - v0 + 0.02f));   // (hanging over the foot)
            foreach (var v in (ReadOnlySpan<float>)[0.48f, 1.2f])
                OBox(linen, id, new float3(X(u1 - 0.28f), 0.6f, Z(v)), new float3(0.38f, 0.12f, 0.6f), float4x4.CreateRotationZ(0.12f));
            OBox(timber, id, new float3(X(u1 + 0.025f), 0.55f, Z((v0 + v1) / 2)), new float3(0.04f, 1.1f, v1 - v0 + 0.08f));   // headboard
            AddCollider(new float3(X((u0 + u1) / 2), 0.3f, Z((v0 + v1) / 2)), new float3(u1 - u0, 0.6f, v1 - v0));
            // A framed print over it.
            OBox(timber, id, new float3(X(MotelBathX) - 0.06f, 1.65f, Z(0.85f)), new float3(0.02f, 0.5f, 0.7f));
            OBox(JB(Material.Plain, new float3(0.70f, 0.56f, 0.32f)), id, new float3(X(MotelBathX) - 0.072f, 1.65f, Z(0.85f)), new float3(0.006f, 0.42f, 0.62f));
        }

        // The bedside table and its lamp.
        {
            float u = MotelBathX - 0.29f, v = -0.17f;
            OBox(timber, id, new float3(X(u), 0.27f, Z(v)), new float3(0.42f, 0.54f, 0.42f));
            OCyl(JB(Material.Plain, new float3(0.55f, 0.48f, 0.36f)), id, new float3(X(u), 0.54f, Z(v)), new float3(X(u), 0.72f, Z(v)), 0.05f, 0.03f, 10);
            OCyl(JB(Material.Emissive, new float3(0.95f, 0.72f, 0.42f) * 0.55f), id, new float3(X(u), 0.72f, Z(v)), new float3(X(u), 0.9f, Z(v)), 0.13f, 0.08f, 12);
            AddCollider(new float3(X(u), 0.27f, Z(v)), new float3(0.42f, 0.54f, 0.42f));
        }

        // The dresser against the north wall, an old TV on it.
        {
            float u0 = 1.9f, u1 = 3.1f;
            float z0 = lo, z1 = lo + 0.48f;
            OBox(timber, id, new float3(X((u0 + u1) / 2), 0.4f, (z0 + z1) / 2), new float3(u1 - u0, 0.8f, z1 - z0));
            var handles = JB(Material.Plain, new float3(0.62f, 0.55f, 0.34f));
            foreach (var y in (ReadOnlySpan<float>)[0.22f, 0.5f])
            {
                OBox(JB(Material.Veneer, MotelTimber * 0.8f), id, new float3(X((u0 + u1) / 2), y, z1 + 0.004f), new float3(u1 - u0 - 0.08f, 0.22f, 0.008f));
                OBox(handles, id, new float3(X((u0 + u1) / 2), y + 0.04f, z1 + 0.014f), new float3(0.18f, 0.025f, 0.02f));
            }
            var tvCase = JB(Material.Polymer, new float3(0.12f, 0.12f, 0.12f));
            OBox(tvCase, id, new float3(X(2.5f), 1.03f, z0 + 0.22f), new float3(0.6f, 0.46f, 0.42f));
            OBox(JB(Material.Glass, new float3(0.05f, 0.06f, 0.06f)), id, new float3(X(2.48f), 1.04f, z0 + 0.435f), new float3(0.46f, 0.34f, 0.012f));
            OCyl(tvCase, id, new float3(X(2.6f), 1.26f, z0 + 0.2f), new float3(X(2.75f), 1.52f, z0 + 0.12f), 0.006f, 0.006f, 5);   // rabbit ears
            OCyl(tvCase, id, new float3(X(2.6f), 1.26f, z0 + 0.2f), new float3(X(2.45f), 1.5f, z0 + 0.1f), 0.006f, 0.006f, 5);
            AddCollider(new float3(X((u0 + u1) / 2), 0.63f, (z0 + z1) / 2), new float3(u1 - u0, 1.26f, z1 - z0));
        }

        // An armchair in the corner by the window.
        {
            float u = 0.85f, v = 1.3f;
            var chair = JB(Material.Cloth, MotelSpreads[(room + 2) % MotelSpreads.Length] * 0.8f);
            OBox(chair, id, new float3(X(u), 0.22f, Z(v)), new float3(0.62f, 0.44f, 0.62f));
            OBox(chair, id, new float3(X(u), 0.62f, Z(v + 0.25f)), new float3(0.62f, 0.5f, 0.14f));
            foreach (var side in (ReadOnlySpan<float>)[-0.27f, 0.27f])
                OBox(chair, id, new float3(X(u + side), 0.52f, Z(v)), new float3(0.1f, 0.18f, 0.6f));
            AddCollider(new float3(X(u), 0.4f, Z(v)), new float3(0.64f, 0.8f, 0.66f));
        }

        // The curtains, drawn across the window (from outside it's dark glass), and their rail.
        {
            var curtain = JB(Material.Cloth, MotelCurtains[room % MotelCurtains.Length]);
            OBox(curtain, id, new float3(inX + 0.05f, 1.45f, Z(0.7f)), new float3(0.03f, 1.2f, 1.6f));
            OBox(JB(Material.Plain, new float3(0.5f, 0.5f, 0.5f)), id, new float3(inX + 0.05f, 2.07f, Z(0.7f)), new float3(0.03f, 0.03f, 1.8f));
        }

        // A light fitting in the middle of the ceiling (off: it's just the shade).
        OCyl(JB(Material.Plain, MotelLinen * 0.9f), id, new float3(X(2.4f), ceiling - 0.14f, zc), new float3(X(2.4f), ceiling, zc), 0.2f, 0.16f, 14);

        // The bathroom: a basin with a mirror over it by the doorway, a toilet, a shower tray in the far corner.
        {
            var porcelain = JB(Material.Plain, MotelPorcelain);
            OBox(porcelain, id, new float3(backX - 0.2f, 0.8f, Z(-1.25f)), new float3(0.4f, 0.14f, 0.5f));
            OBox(porcelain, id, new float3(backX - 0.12f, 0.4f, Z(-1.25f)), new float3(0.12f, 0.66f, 0.16f));
            OBox(JB(Material.Glass, new float3(0.25f, 0.27f, 0.28f)), id, new float3(backX - 0.006f, 1.5f, Z(-1.25f)), new float3(0.008f, 0.6f, 0.5f));
            OBox(porcelain, id, new float3(backX - 0.32f, 0.2f, Z(-0.2f)), new float3(0.5f, 0.4f, 0.38f));
            OBox(porcelain, id, new float3(backX - 0.1f, 0.6f, Z(-0.2f)), new float3(0.18f, 0.4f, 0.44f));
            OBox(porcelain, id, new float3(backX - 0.42f, 0.04f, hi - 0.42f), new float3(0.82f, 0.06f, 0.82f));
            AddCollider(new float3(backX - 0.3f, 0.4f, Z(-0.2f)), new float3(0.6f, 0.8f, 0.44f));
        }

        // Outside: the window (dark glass, the curtains behind it) and its sill, and a trim round the door.
        Add(new float3(minX - 0.02f, 1.45f, Z(0.7f)), new float3(0.04f, 1.0f, 1.4f), DarkGlass, Material.Glass, collides: false);
        Add(new float3(minX - 0.03f, 0.92f, Z(0.7f)), new float3(0.06f, 0.06f, 1.5f), MotelTrimGreen, Material.PaintedMetal, collides: false);
        var trim = JB(Material.PaintedMetal, MotelTrimGreen);
        foreach (var zz in (ReadOnlySpan<float>)[d0 - 0.04f, d1 + 0.04f])
            OBox(trim, id, new float3(minX - 0.015f, MotelDoorH / 2, zz), new float3(0.03f, MotelDoorH + 0.08f, 0.08f));
        OBox(trim, id, new float3(minX - 0.015f, MotelDoorH + 0.04f, doorZ), new float3(0.03f, 0.08f, MotelDoorW + 0.16f));

        // A worn metal threshold in the doorway, out to where the leaf shuts.
        OBox(JB(Material.Plain, new float3(0.42f, 0.41f, 0.38f)), id, new float3((minX + inX - 0.05f) / 2, 0.012f, doorZ), new float3(inX - 0.05f - minX, 0.024f, MotelDoorW));

        BuildMotelDoor(room, inX, d0);
    }

    /// <summary>
    /// A room's door: a teal leaf in the inside of the opening, hinged on its north edge and swinging in against the room's
    /// north side. Built in the hinge's frame: the leaf runs south (+z) from the hinge, its inside face at x = 0.
    /// </summary>
    private void BuildMotelDoor(int room, float inX, float d0)
    {
        var id = float4x4.Identity;
        const float thick = 0.045f, w = MotelDoorW, dh = MotelDoorH - 0.01f;
        var c = new float3(-thick / 2, dh / 2, w / 2);
        var leaf = HingedLeaf(new float3(inX, 0, d0), float.DegreesToRadians(92f), c, new float3(thick, dh, w - 0.01f));
        OBox(leaf.B(Material.Plain, DoorTeal), id, c, new float3(thick, dh, w - 0.01f));
        // Panels on the outside face, a kick plate, the room's number plate, a spyhole and handles on both sides.
        var teal = leaf.B(Material.Plain, DoorTeal * 0.85f);
        foreach (var (y, ph) in (ReadOnlySpan<(float, float)>)[(0.6f, 0.75f), (1.5f, 0.8f)])
            OBox(teal, id, new float3(-thick - 0.003f, y, w / 2), new float3(0.006f, ph, w - 0.26f));
        OBox(leaf.B(Material.Plain, new float3(0.45f, 0.44f, 0.42f)), id, new float3(-thick - 0.004f, 0.13f, w / 2), new float3(0.008f, 0.22f, w - 0.08f));
        var brass = leaf.B(Material.Plain, new float3(0.72f, 0.58f, 0.28f));
        OBox(brass, id, new float3(-thick - 0.005f, 1.6f, w / 2), new float3(0.01f, 0.1f, 0.14f));
        OBox(leaf.B(Material.Plain, new float3(0.05f, 0.05f, 0.05f)), id, new float3(-thick - 0.008f, 1.6f, w / 2), new float3(0.004f, 0.05f, 0.022f));   // the number, worn black
        OCyl(brass, id, new float3(-thick - 0.012f, 1.42f, w / 2), new float3(0.012f, 1.42f, w / 2), 0.012f, 0.012f, 8);   // spyhole
        foreach (var (face, side) in (ReadOnlySpan<(float, float)>)[(-thick, -1f), (0f, 1f)])
        {
            OBox(brass, id, new float3(face + side * 0.0175f, 1.0f, w - 0.1f), new float3(0.035f, 0.02f, 0.02f));   // the handle's neck
            OBox(brass, id, new float3(face + side * 0.035f, 1.0f, w - 0.14f), new float3(0.022f, 0.022f, 0.12f));  // and its lever
        }
        var door = NewDoor($"motel {room + 1}", 0.55f, leaf);
        MoveDoor(door, false, instant: true);
        _motelDoors.Add(door);
    }

    /// <summary>The player is in the way of a room's door as it swings (in the doorway, or just inside it).</summary>
    private static bool InMotelDoorway(int room, float3 player)
    {
        var door = MotelDoorAt(room);
        return player.X > door.X - MotelWall / 2 - 0.35f && player.X < door.X + MotelWall / 2 + MotelDoorW + 0.35f
            && float.Abs(player.Z - door.Z) < MotelDoorW / 2 + 0.35f && player.Y < 2f;
    }

    /// <summary>Near a room's door (outside it or in), E opens or shuts it; it won't shut on the player in its way.</summary>
    public void UpdateMotelDoors(float dt, bool usePressed, float3 player)
    {
        MotelDoorPrompt = null;
        var nearest = -1;
        var best = 1.7f;
        for (var i = 0; i < _motelDoors.Count; i++)
        {
            var at = MotelDoorAt(i);
            var dx = player.X - at.X;
            var dz = player.Z - at.Z;
            var d = float.Sqrt(dx * dx + dz * dz);
            if (d < best && float.Abs(player.Y - at.Y) < 1.5f)
                (nearest, best) = (i, d);
        }

        if (nearest >= 0)
        {
            var door = _motelDoors[nearest];
            var open = door.Target > 0.5f;
            MotelDoorPrompt = open ? "[E] Close door" : "[E] Open door";
            if (usePressed && !(open && InMotelDoorway(nearest, player)) && MoveDoor(door, !open))
                Log.Write($"[DOOR] {(open ? "Shutting" : "Opening")} the door of motel room {nearest + 1}");
        }

        foreach (var door in _motelDoors)
            StepDoor(door, dt);
    }

    /// <summary>Whoever's inside throws the room's door open.</summary>
    public void SpringMotelDoor(int room)
    {
        if (room >= 0 && room < _motelDoors.Count && MoveDoor(_motelDoors[room], true))
            Log.Write($"[MOTEL] The door of room {room + 1} flies open");
    }

    /// <summary>Every room's door open at once (debug: BRUTALFPS_DOOR=motel).</summary>
    public void OpenMotelDoors()
    {
        foreach (var door in _motelDoors)
            MoveDoor(door, true, instant: true);
    }

    /// <summary>Every room's door shut again (a respawn).</summary>
    public void ShutMotelDoors()
    {
        foreach (var door in _motelDoors)
            MoveDoor(door, false, instant: true);
    }

    private void DrawMotelDoors(Renderer renderer)
    {
        foreach (var door in _motelDoors)
            DrawDoor(renderer, door);
    }
}
