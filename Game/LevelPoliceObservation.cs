using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The vault's two observation rooms: small dim rooms either side of the corridor's far end, against the vault's south
/// wall, each through a sliding door in the corridor wall (with a keypad: the airlock's code from the corridor, no code
/// from inside) and each looking into the vault through a one-way mirror (<see cref="OneWayMirrors"/>): from the vault
/// it's a mirror, from the room clear glass. The vault's PC (<see cref="PoliceVaultPcZone"/>) is against the east room's far wall.
///
///     +--------------- vault ----------------+
///     |   [ mirror ]              [ mirror ] |
///     +----------------+ inner +-------------+
///     |  west room     |  door |  east room  |
///     |  (obs 1)    [door]   [door] (obs 2)  |
///     +----------------+       +-------------+
///                      | corr. |
/// </summary>
public sealed partial class Level
{
    private const float ObsSouthZ = -33.4f;                 // the rooms' south walls' inside face (north: the vault's wall)
    private const float ObsDoorZ = -35.0f, ObsDoorHalf = 0.45f, ObsDoorTop = 2.1f;
    private const float ObsCeiling = VaultCorrCeiling;
    private const float ObsWindowBottom = 0.95f, ObsWindowTop = 2.05f, ObsWindowHalf = 1.2f;
    private const float ObsMirrorZ = VaultMaxZ + 0.02f;      // the glass, just inside the window from the vault's face

    // Inside x of each room (west of the corridor's west wall; east of its east wall), and its window's middle.
    private const float ObsWestMinX = -11.5f, ObsWestMaxX = VaultDoorX - VaultCorrHalf - PoliceWall;
    private const float ObsEastMinX = VaultDoorX + VaultCorrHalf + PoliceWall, ObsEastMaxX = -1.1f;
    private const float ObsWestWindowX = -9.7f, ObsEastWindowX = -3.1f;

    public static Aabb PoliceObservationWest => new(PAt(ObsWestMinX, 0, VaultSouthWallZ), PAt(ObsWestMaxX, ObsCeiling, ObsSouthZ));
    public static Aabb PoliceObservationEast => new(PAt(ObsEastMinX, 0, VaultSouthWallZ), PAt(ObsEastMaxX, ObsCeiling, ObsSouthZ));

    /// <summary>A pane of one-way glass: its middle, the way its mirror side faces, its width and height.</summary>
    public readonly record struct MirrorPane(float3 Center, float3 Normal, float2 Size);

    /// <summary>The two observation rooms' mirrors, in the vault's south wall, mirror side to the vault (-z).</summary>
    public static IReadOnlyList<MirrorPane> VaultMirrors { get; } =
    [
        new(PAt(ObsWestWindowX, (ObsWindowBottom + ObsWindowTop) / 2, ObsMirrorZ), new float3(0, 0, -1), new float2(ObsWindowHalf * 2, ObsWindowTop - ObsWindowBottom)),
        new(PAt(ObsEastWindowX, (ObsWindowBottom + ObsWindowTop) / 2, ObsMirrorZ), new float3(0, 0, -1), new float2(ObsWindowHalf * 2, ObsWindowTop - ObsWindowBottom)),
    ];

    /// <summary>True if (x, y) on the vault's south wall is in (or right by) one of the windows.</summary>
    private static bool InObsWindow(float x, float y) =>
        y > ObsWindowBottom - 0.1f && y < ObsWindowTop + 0.1f
        && (float.Abs(x - ObsWestWindowX) < ObsWindowHalf + 0.1f || float.Abs(x - ObsEastWindowX) < ObsWindowHalf + 0.1f);

    /// <summary>Each room's ceiling light: dim, so the glass reads as a mirror from the vault.</summary>
    private static IEnumerable<OfficeLight> ObservationLights
    {
        get
        {
            foreach (var room in (Aabb[])[PoliceObservationWest, PoliceObservationEast])
            {
                var c = (room.Min + room.Max) / 2;
                yield return new OfficeLight(new float3(c.X, room.Max.Y - 0.07f, c.Z), new float3(1.0f, 0.9f, 0.78f), 1.1f, 5f, Grow(room));
            }
        }
    }

    private readonly List<PrisonDoor> _obsDoors = [];

    private void BuildObservationRooms()
    {
        BuildObservationRoom(ObsWestMinX, ObsWestMaxX, ObsWestWindowX, wallX: ObsWestMaxX, side: -1f, "obs west", "OBSERVATION 1");
        BuildObservationRoom(ObsEastMinX, ObsEastMaxX, ObsEastWindowX, wallX: ObsEastMinX, side: 1f, "obs east", "OBSERVATION 2");
    }

    /// <param name="wallX">The room-side face of the corridor wall the door is in.</param>
    /// <param name="side">Which way the room lies from the corridor (-1 west, +1 east).</param>
    private void BuildObservationRoom(float minX, float maxX, float windowX, float wallX, float side, string doorName, string sign)
    {
        const float t = PoliceWall, h = ObsCeiling;
        const float z0 = VaultSouthWallZ, z1 = ObsSouthZ;
        var corrX = wallX - side * t;   // the corridor-side face of the same wall

        // Floor, block walls (the corridor's wall on one side, the vault's on the north), a concrete soffit.
        AddMinMax(PAt(side < 0 ? minX - t : minX, -0.3f, z0), PAt(side < 0 ? maxX : maxX + t, 0, z1 + t), new float3(0.4f, 0.4f, 0.4f), Material.Floor);   // (not under the corridor wall: the threshold)
        var outerX = side < 0 ? minX : maxX;
        AddMinMax(PAt(float.Min(outerX, outerX + side * t), 0, z0), PAt(float.Max(outerX, outerX + side * t), h + 0.2f, z1 + t), CorridorBlock, Material.Blockwork);
        AddMinMax(PAt(minX - t, 0, z1), PAt(maxX + t, h + 0.2f, z1 + t), CorridorBlock, Material.Blockwork);
        AddMinMax(PAt(minX - t, h, z0), PAt(maxX + t, h + 0.2f, z1 + t), new float3(0.5f, 0.5f, 0.49f), Material.Concrete);

        // The doorway through the corridor wall: threshold and steel linings.
        var wx0 = float.Min(wallX, corrX);
        var wx1 = float.Max(wallX, corrX);
        CarveOpening(new Aabb(PAt(wx0 - 0.05f, 0f, ObsDoorZ - ObsDoorHalf), PAt(wx1 + 0.05f, ObsDoorTop, ObsDoorZ + ObsDoorHalf)));
        AddMinMax(PAt(wx0, -0.3f, ObsDoorZ - ObsDoorHalf), PAt(wx1, 0f, ObsDoorZ + ObsDoorHalf), Aluminium, Material.Steel);
        var lining = PB(Material.Steel, AirlockSteel * 0.8f, occludes: false);
        foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
            OBox(lining, float4x4.Identity, PAt((wx0 + wx1) / 2, ObsDoorTop / 2, ObsDoorZ + s * (ObsDoorHalf - 0.005f)), new float3(t, ObsDoorTop, 0.01f));
        OBox(lining, float4x4.Identity, PAt((wx0 + wx1) / 2, ObsDoorTop - 0.005f, ObsDoorZ), new float3(t, 0.01f, ObsDoorHalf * 2));

        // The door: a steel leaf on the room side, sliding north along the wall.
        const float w = ObsDoorHalf * 2 + 0.1f, dh = ObsDoorTop + 0.05f, travel = w + 0.02f;
        var lx = wallX + side * 0.06f;
        var leaf = SlidingLeaf(new float3(0, 0, -travel), PAt(lx, dh / 2, ObsDoorZ), new float3(0.06f, dh, w));
        var id = float4x4.Identity;
        OBox(leaf.B(Material.Plain, AirlockSteel), id, PAt(lx, dh / 2, ObsDoorZ), new float3(0.06f, dh, w));
        var trim = leaf.B(Material.Steel, AirlockSteel * 0.6f);
        OBox(trim, id, PAt(lx, dh - 0.03f, ObsDoorZ), new float3(0.075f, 0.06f, w));
        OBox(trim, id, PAt(lx, 0.03f, ObsDoorZ), new float3(0.075f, 0.06f, w));
        foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
        {
            OBox(trim, id, PAt(lx, dh / 2, ObsDoorZ + s * (w / 2 - 0.03f)), new float3(0.075f, dh, 0.06f));
            OBox(leaf.B(Material.Steel, Aluminium), id, PAt(lx + s * 0.06f, 1.05f, ObsDoorZ + 0.36f), new float3(0.03f, 0.45f, 0.03f));
        }
        OBox(PB(Material.Steel, new float3(0.3f, 0.31f, 0.33f), occludes: false), id, PAt(lx, dh + 0.08f, ObsDoorZ - travel / 2), new float3(0.12f, 0.14f, w + travel + 0.1f));
        var door = NewDoor(doorName, 1.4f, leaf);
        MoveDoor(door, false, instant: true);
        _obsDoors.Add(door);
        _doorways[door] = new Aabb(PAt(wx0 - 0.45f, -0.5f, ObsDoorZ - ObsDoorHalf - 0.1f), PAt(wx1 + 0.45f, 2.5f, ObsDoorZ + ObsDoorHalf + 0.1f));

        // Keypads south of the door: the corridor's takes the code, the room's just opens it.
        var keyZ = ObsDoorZ + ObsDoorHalf + 0.35f;
        AddKeypad(PAt(corrX, 1.3f, keyZ), new float3(-side, 0, 0), door, null, needsCode: true, "OBSERVATION");
        AddKeypad(PAt(wallX, 1.3f, keyZ), new float3(side, 0, 0), door, null, needsCode: false, "OBSERVATION");

        // A sign over the door on the corridor side.
        var corridorNormal = new float3(-side, 0, 0);
        OBox(PB(Material.Plain, new float3(0.16f, 0.2f, 0.32f), occludes: false), WallFrame(PAt(corrX, 0, ObsDoorZ), corridorNormal),
             new float3(0, ObsDoorTop + 0.2f, 0.006f), new float3(0.8f, 0.13f, 0.012f));
        AddLettering(sign, PAt(corrX - side * 0.013f, ObsDoorTop + 0.2f, ObsDoorZ), corridorNormal, 0.05f, Material.Plain, new float3(0.92f, 0.92f, 0.9f), 0.003f);

        // The window through the vault's wall: steel frames on both faces, a bulletproof pane (the mirror's drawn by
        // OneWayMirrors), and below it on the room side a narrow counter with two chairs.
        var wy = (ObsWindowBottom + ObsWindowTop) / 2;
        var wh = ObsWindowTop - ObsWindowBottom;
        CarveOpening(new Aabb(PAt(windowX - ObsWindowHalf, ObsWindowBottom, VaultMaxZ - 0.05f), PAt(windowX + ObsWindowHalf, ObsWindowTop, VaultSouthWallZ + 0.05f)));
        AddStatic(new Aabb(PAt(windowX - ObsWindowHalf, ObsWindowBottom, ObsMirrorZ), PAt(windowX + ObsWindowHalf, ObsWindowTop, ObsMirrorZ + 0.03f)), default, visible: false);
        var frame = PB(Material.Steel, new float3(0.22f, 0.23f, 0.24f), occludes: false);
        foreach (var (fz, dz) in (ReadOnlySpan<(float, float)>)[(VaultMaxZ, -1f), (VaultSouthWallZ, 1f)])
        {
            OBox(frame, id, PAt(windowX, ObsWindowBottom - 0.03f, fz + dz * 0.015f), new float3(ObsWindowHalf * 2 + 0.12f, 0.06f, 0.03f));
            OBox(frame, id, PAt(windowX, ObsWindowTop + 0.03f, fz + dz * 0.015f), new float3(ObsWindowHalf * 2 + 0.12f, 0.06f, 0.03f));
            foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
                OBox(frame, id, PAt(windowX + s * (ObsWindowHalf + 0.03f), wy, fz + dz * 0.015f), new float3(0.06f, wh, 0.03f));
        }
        var reveal = PB(Material.Plain, new float3(0.3f, 0.31f, 0.32f), occludes: false);
        OBox(reveal, id, PAt(windowX, ObsWindowBottom + 0.005f, (VaultMaxZ + VaultSouthWallZ) / 2), new float3(ObsWindowHalf * 2, 0.01f, VaultWallT));
        OBox(reveal, id, PAt(windowX, ObsWindowTop - 0.005f, (VaultMaxZ + VaultSouthWallZ) / 2), new float3(ObsWindowHalf * 2, 0.01f, VaultWallT));
        foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
            OBox(reveal, id, PAt(windowX + s * (ObsWindowHalf - 0.005f), wy, (VaultMaxZ + VaultSouthWallZ) / 2), new float3(0.01f, wh, VaultWallT));

        var counter = Frame(PAt(windowX, 0, z0), 0);   // (local +z: south, into the room)
        OBox(PB(Material.Veneer, new float3(0.42f, 0.36f, 0.3f)), counter, new float3(0, 0.74f, 0.25f), new float3(ObsWindowHalf * 2 + 0.4f, 0.04f, 0.5f));
        var legs = PB(Material.Plain, new float3(0.22f, 0.22f, 0.23f));
        foreach (var cx in (ReadOnlySpan<float>)[-ObsWindowHalf, ObsWindowHalf])
            OBox(legs, counter, new float3(cx, 0.36f, 0.25f), new float3(0.04f, 0.72f, 0.45f));
        FrameCollider(counter, new float3(0, 0.38f, 0.25f), new float3(ObsWindowHalf * 2 + 0.4f, 0.76f, 0.5f));
        foreach (var cx in (ReadOnlySpan<float>)[-0.6f, 0.6f])
        {
            var chair = float4x4.CreateRotationY(cx * 0.3f) * float4x4.CreateTranslation(cx, 0, 0.85f) * counter;
            AddOfficeChair(chair, 0.1f);
            FrameCollider(chair, new float3(0, 0.45f, 0), new float3(0.5f, 0.9f, 0.5f), blocksShots: false);
        }

        // The ceiling light (dim: see ObservationLights).
        var mid = PAt((minX + maxX) / 2, h, (z0 + z1) / 2);
        OBox(PB(Material.Plain, new float3(0.86f, 0.86f, 0.85f), occludes: false), id, mid - new float3(0, 0.03f, 0), new float3(0.6f, 0.06f, 0.6f));
        OBox(PB(Material.Emissive, PanelLight * 0.45f, occludes: false), id, mid - new float3(0, 0.062f, 0), new float3(0.54f, 0.004f, 0.54f));
    }
}
