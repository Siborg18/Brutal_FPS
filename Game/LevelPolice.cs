using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// A small police station (the shader's area 7: a kilometre down, y &lt; -500), reached through the navy portal in the tech room.
/// Layout (looking down, north is -z):
///
///     +-------------+-----------------[sign]-------------[door]-+---------------+
///     |  [PC desk]  |  copier   [credenza]                      | clearing  :   |
///     | [rack]      |                                           | barrel    :[L]|
///     (portal)     door ======== walkway to the armoury ======== door      :[L]|
///     |  cables     |      [desks]      [desks]      [desks]    |    (cage door) |
///     |             |                                           |  bench    :[L]|
///     +-------------+    [desks]      [desks]      [desks]     +---------------+
///                   |  filing                    water cooler  |
///                   +--------------- windows ------------------+
///
/// The bullpen is an open-plan office of cubicle clusters (frosted glass screens, light veneer tops, dark wood bases,
/// black chairs, dual monitors) under a suspended ceiling of fluorescent panels; a green accent wall on the west side,
/// the department's sign over a credenza on the north wall, windows along the south. West of it the tech room: the
/// portal in its far wall is wired into a PC on a desk (the cables run along the skirting into a junction box by the
/// ring). East, through a doorway, the armoury: a welded-mesh cage across the room with a counter-shelf door standing
/// open, and behind it gun lockers full of carbines and shotguns, ammunition on shelving, vests on hooks. North, through
/// an airlock door in the sign wall, a long corridor runs to the vault (see LevelPoliceVault.cs).
/// </summary>
public sealed partial class Level
{
    /// <summary>
    /// Where the police station is: a kilometre straight down under the warehouse (the shader's area 7: y &lt; -500).
    /// It's kept near x = z = 0 on purpose: its fine detail (whiteboards, screens, lettering a millimetre or two proud)
    /// z-fought out at x = 30050, where a 32-bit float only resolves about 2 mm.
    /// </summary>
    public static readonly float3 PoliceOrigin = new(0, -1000, 0);

    /// <summary>Below this height a point is in the police station (see <see cref="ZoneAt"/> and the shader's areaOf).</summary>
    public const float PoliceBelowY = -500f;

    private static float3 PAt(float x, float y, float z) => PoliceOrigin + new float3(x, y, z);

    private const float PoliceCeiling = 2.8f;   // the suspended ceiling's underside
    private const float PoliceWall = 0.2f;

    // The rooms (inside faces, relative to the origin).
    private const float BullpenHalfX = 9f, BullpenHalfZ = 7f;
    private const float TechMinX = -14.5f;                 // tech room: x -14.5 .. -9.2, z -7 .. 0
    private const float ArmouryMaxX = 16f;                 // armoury: x 9.2 .. 16, z -7 .. 0 (the cage across it at x 12.4)
    private const float SideRoomMaxZ = 0f;
    private const float CageX = 12.4f;
    private const float DoorZ = -3.5f;                     // both side doorways (1 m wide) and the cage door are on this line
    private const float DoorHalf = 0.5f;
    private const float DoorTop = 2.12f;

    /// <summary>The navy portal: in the tech room's west wall, facing back into it (+x), on the line of the doorways.</summary>
    public static float3 PolicePortalFloorPoint => PAt(TechMinX, 0, DoorZ);
    /// <summary>The foot of the door signed EXIT, in the middle of the bullpen's north wall: it leads nowhere, except on a raid (out to the van).</summary>
    public static float3 PoliceExitDoor => PAt(6.3f, 0, -BullpenHalfZ);
    public static float3 PolicePortalNormal => new(1, 0, 0);
    public static readonly float3 PolicePortalColor = new(0.20f, 0.34f, 1.0f);

    /// <summary>
    /// Where the magenta portal was: in the warehouse's east wall (the gap between the piers at z = -14). It's out of the
    /// game for now (the navy portal starts offline), kept for reuse: see PortalSystem's constructor.
    /// </summary>
    public static float3 WarehouseEastPortalFloorPoint => new(HalfWidth, 0, -14);
    public static float3 WarehouseEastPortalNormal => new(-1, 0, 0);
    public static readonly float3 WarehouseEastPortalColor = new(1.0f, 0.22f, 0.78f);

    /// <summary>
    /// Where the player can use the tech room's PC (press E): the floor in front of its desk, by the portal. Walking out
    /// of it closes the terminal (see <see cref="PcTerminal"/>).
    /// </summary>
    public static Aabb PolicePcZone => new(PAt(-14.2f, -0.5f, -6.3f), PAt(-12.1f, 3f, -4.85f));

    /// <summary>The whole station, with room to spare (its reverb room).</summary>
    public static Aabb PoliceBuilding => new(PAt(-20, -5, -12), PAt(22, 8, 12));

    public static Aabb PoliceBullpen => new(PAt(-BullpenHalfX, 0, -BullpenHalfZ), PAt(BullpenHalfX, PoliceCeiling, BullpenHalfZ));
    public static Aabb PoliceTechRoom => new(PAt(TechMinX, 0, -BullpenHalfZ), PAt(-BullpenHalfX - PoliceWall, PoliceCeiling, SideRoomMaxZ));
    /// <summary>The armoury's gun locker (<see cref="GunLocker"/>): the cleaning bench with guns laid out on it.</summary>
    public static float3 ArmouryLockerPoint => PAt(13.95f, 0, -2.2f);

    public static Aabb PoliceArmoury => new(PAt(BullpenHalfX + PoliceWall, 0, -BullpenHalfZ), PAt(ArmouryMaxX, PoliceCeiling, SideRoomMaxZ));

    // Paint and finishes.
    /// <summary>The tech room's CCTV monitor: on the north wall over the PC desk (x right, y up, z out of the wall).</summary>
    private static float4x4 CctvMonitorFrame => WallFrame(PAt(-13.1f, 1.8f, -BullpenHalfZ), new float3(0, 0, 1));

    /// <summary>The CCTV monitor's picture: the unit cube onto the screen, its front face showing (see <see cref="Renderer.DrawScreen"/>).</summary>
    public static float4x4 PoliceCctvScreen => float4x4.CreateScale(0.72f, 0.405f, 0.002f) * float4x4.CreateTranslation(0, 0.01f, 0.1225f) * CctvMonitorFrame;

    /// <summary>The second CCTV monitor, beside the first: the police vault's camera.</summary>
    private static float4x4 VaultCctvMonitorFrame => WallFrame(PAt(-12.0f, 1.8f, -BullpenHalfZ), new float3(0, 0, 1));

    /// <summary>The vault monitor's picture (as <see cref="PoliceCctvScreen"/>).</summary>
    public static float4x4 PoliceVaultCctvScreen => float4x4.CreateScale(0.72f, 0.405f, 0.002f) * float4x4.CreateTranslation(0, 0.01f, 0.1225f) * VaultCctvMonitorFrame;

    private static readonly float3 PoliceBeige = new(0.80f, 0.74f, 0.62f);
    private static readonly float3 PoliceGreen = new(0.34f, 0.44f, 0.37f);
    private static readonly float3 TechGrey = new(0.72f, 0.73f, 0.72f);
    private static readonly float3 ArmouryPaint = new(0.70f, 0.71f, 0.68f);
    private static readonly float3 CarpetTint = new(0.42f, 0.40f, 0.35f);
    private static readonly float3 CeilingTint = new(0.86f, 0.85f, 0.81f);
    private static readonly float3 DeskTop = new(0.64f, 0.52f, 0.38f);
    private static readonly float3 DarkWood = new(0.20f, 0.13f, 0.09f);
    private static readonly float3 Frosted = new(0.80f, 0.83f, 0.84f);
    private static readonly float3 Skirting = new(0.22f, 0.17f, 0.13f);
    private static readonly float3 CageSteel = new(0.27f, 0.28f, 0.30f);
    private static readonly float3 LockerGrey = new(0.80f, 0.80f, 0.77f);
    private static readonly float3 PanelLight = new(0.97f, 0.98f, 1.0f);

    // The ceiling's light panels (60 x 120 cm, long side along z), by their centres: (x, z, room). Rooms: 0 bullpen,
    // 1 tech room, 2 armoury.
    private static readonly (float X, float Z, int Room)[] PolicePanels =
    [
        (-6.3f, -4.2f, 0), (-2.7f, -4.2f, 0), (2.7f, -4.2f, 0), (6.3f, -4.2f, 0),
        (-6.3f, 0.0f, 0), (-2.7f, 0.0f, 0), (2.7f, 0.0f, 0), (6.3f, 0.0f, 0),
        (-6.3f, 4.2f, 0), (-2.7f, 4.2f, 0), (2.7f, 4.2f, 0), (6.3f, 4.2f, 0),
        (-11.7f, -5.4f, 1), (-11.7f, -1.8f, 1),
        (10.5f, -3.6f, 2), (14.1f, -5.4f, 2), (14.1f, -1.8f, 2),
    ];

    private static Aabb PoliceRoomBounds(int room)
    {
        var box = room switch { 0 => PoliceBullpen, 1 => PoliceTechRoom, _ => PoliceArmoury };
        return new Aabb(box.Min - new float3(0.05f, 0.1f, 0.05f), box.Max + new float3(0.05f, 0.1f, 0.05f));
    }

    /// <summary>The station's lights: its ceiling panels and the navy portal's glow in the tech room.</summary>
    public static OfficeLight[] PoliceLights
    {
        get
        {
            var lights = PolicePanels
                .Select(p => new OfficeLight(PAt(p.X, PoliceCeiling - 0.02f, p.Z), new float3(1.0f, 0.985f, 0.96f), 3.8f, 9f, PoliceRoomBounds(p.Room)))
                .ToList();
            var portal = PolicePortalFloorPoint + new float3(0, PortalSystem.CenterHeight, 0) + PolicePortalNormal * 0.7f;
            lights.Add(new OfficeLight(portal, PolicePortalColor, 0.6f, 5f, PoliceRoomBounds(1), Panel: false));
            lights.AddRange(VaultLights);
            return [.. lights];
        }
    }

    /// <summary>The bounce light that fills the station (radiance off its pale walls and ceiling).</summary>
    public static readonly float3 PoliceAmbient = new(0.24f, 0.24f, 0.235f);

    /// <summary>The occlusion map: straight down (a hair off vertical, so the view has an up direction) over the whole station.</summary>
    public static float3 PoliceOcclusionCenter => PAt(1f, 0, 0);
    public const float PoliceOcclusionHalfExtent = 16.5f;
    public static readonly float3 PoliceOcclusionDirection = new(0.004f, 1f, 0.006f);

    /// <summary>
    /// Draws what shades the floor under it in the station (furniture below the ceiling, not the walls, the ceiling or
    /// its fittings), for <see cref="PoliceOcclusionDirection"/>'s depth map.
    /// </summary>
    public void DrawPoliceOccluders(Renderer renderer)
    {
        foreach (var index in _policeOccluders)
        {
            var prop = _props[index];   // (where the level editor has put it)
            if (!prop.Removed)
                renderer.DrawMesh(prop.Mesh!, prop.Model, prop.Tint, ShadeMode.Lit, prop.Material);
        }
        foreach (var box in _boxes)
            if (box.Visible && ZoneAt(box.Bounds.Center) == Zones.Police && box.Bounds.Max.Y <= PoliceOrigin.Y + 2.3f)
                box.Draw(renderer);
    }

    private void BuildPolice()
    {
        var random = new Random(911);
        BuildPoliceShell();
        BuildPoliceCeiling();
        BuildBullpen(random);
        BuildTechRoom(random);
        BuildArmoury(random);
        BuildPoliceVault();
        FlushPoliceBatches();
    }

    // ------------------------------------------------------------------------------------------------
    // Shell: floors, walls (painted differently on each side), doorways, windows, the ceiling.
    // ------------------------------------------------------------------------------------------------

    /// <summary>A wall from (x0, z0) to (x1, z1) (min/max corners in plan), floor to above the ceiling.</summary>
    private void PoliceWallBox(float x0, float z0, float x1, float z1, float3 tint, float y0 = 0f, float y1 = PoliceCeiling + 0.15f) =>
        AddMinMax(PAt(x0, y0, z0), PAt(x1, y1, z1), tint, Material.WallPaint);

    private void BuildPoliceShell()
    {
        const float t = PoliceWall;
        const float hx = BullpenHalfX, hz = BullpenHalfZ;
        var doorLo = DoorZ - DoorHalf;
        var doorHi = DoorZ + DoorHalf;

        // Floors: carpet tiles in the bullpen, grey vinyl in the tech room, sealed concrete in the armoury, and
        // aluminium threshold strips across the doorways.
        AddMinMax(PAt(-hx, -0.3f, -hz), PAt(hx, 0, hz), CarpetTint, Material.Carpet);
        AddMinMax(PAt(TechMinX, -0.3f, -hz), PAt(-hx - t, 0, SideRoomMaxZ), new float3(0.50f, 0.51f, 0.52f), Material.WallPaint);
        AddMinMax(PAt(hx + t, -0.3f, -hz), PAt(ArmouryMaxX, 0, SideRoomMaxZ), new float3(0.52f, 0.51f, 0.49f), Material.Floor);
        foreach (var x in (ReadOnlySpan<float>)[-hx - t, hx])
            AddMinMax(PAt(x, -0.3f, doorLo), PAt(x + t, 0f, doorHi), Aluminium, Material.Steel);   // flush: the player has no step-up

        // Walls between two rooms are one box, each face painted for its own room (LevelBox.Paint).
        const float h2 = t / 2;
        // North wall (the sign wall) across the whole front: the tech room's, the bullpen's and the armoury's parts.
        PoliceWallBox(TechMinX - t, -hz - t, -hx - h2, -hz, TechGrey);
        PoliceWallBox(-hx - h2, -hz - t, hx + h2, -hz, PoliceBeige);
        PoliceWallBox(hx + h2, -hz - t, ArmouryMaxX + t, -hz, ArmouryPaint);
        // South wall: windows between piers (built below).
        BuildPoliceWindows();
        // A wall between the bullpen and a side room from x0 to x1, with the doorway in it: painted for the bullpen
        // (the door's jambs too), its face towards the side room (sideFace) in the side room's paint.
        void SharedWall(float x0, float x1, int sideFace, float3 bullpenTint, float3 sideTint)
        {
            void Part(float z0, float z1, float y0 = 0f) =>
                _boxes.Add(new LevelBox(new Aabb(PAt(x0, y0, z0), PAt(x1, PoliceCeiling + 0.15f, z1)), bullpenTint, Material.WallPaint,
                                        Paint: sideTint, PaintFaces: sideFace));
            Part(-hz, doorLo);
            Part(doorLo, doorHi, DoorTop);
            Part(doorHi, SideRoomMaxZ);
            PoliceWallBox(x0, SideRoomMaxZ, x1, hz, bullpenTint);   // south of the side room: nothing behind it
            _version++;
        }
        // West: the bullpen's green wall, the tech room behind it; the tech room's south and west (portal) walls.
        SharedWall(-hx - t, -hx, LevelBox.FaceNegX, PoliceGreen, TechGrey);
        PoliceWallBox(TechMinX - t, SideRoomMaxZ, -hx - t, SideRoomMaxZ + t, TechGrey);
        PoliceWallBox(TechMinX - t, -hz, TechMinX, SideRoomMaxZ, TechGrey);            // the portal wall
        // East: the bullpen's beige wall, the armoury behind it; the armoury's south and east walls.
        SharedWall(hx, hx + t, LevelBox.FacePosX, PoliceBeige, ArmouryPaint);
        PoliceWallBox(hx + t, SideRoomMaxZ, ArmouryMaxX + t, SideRoomMaxZ + t, ArmouryPaint);
        PoliceWallBox(ArmouryMaxX, -hz, ArmouryMaxX + t, SideRoomMaxZ, ArmouryPaint);

        // Skirting round the bullpen (dark wood, gaps at the doorways), and door casings.
        var skirt = PB(Material.Veneer, Skirting);
        void Skirt(float x0, float z0, float x1, float z1) =>
            OBox(skirt, float4x4.Identity, PAt((x0 + x1) / 2, 0.05f, (z0 + z1) / 2), new float3(float.Abs(x1 - x0), 0.1f, float.Abs(z1 - z0)));
        Skirt(-hx, -hz, VaultDoorX - VaultDoorHalf, -hz + 0.015f);   // (a gap for the airlock door to the vault)
        Skirt(VaultDoorX + VaultDoorHalf, -hz, hx, -hz + 0.015f);
        Skirt(-hx, -hz, -hx + 0.015f, doorLo);
        Skirt(-hx, doorHi, -hx + 0.015f, hz);
        Skirt(hx - 0.015f, -hz, hx, doorLo);
        Skirt(hx - 0.015f, doorHi, hx, hz);
        foreach (var (x, normal) in (ReadOnlySpan<(float, float)>)[(-hx, 1f), (-hx - t, -1f), (hx, -1f), (hx + t, 1f)])
            AddCasing(PAt(x, 0, DoorZ), new float3(normal, 0, 0), DoorHalf * 2, DoorTop);

        // The doors themselves stand open, back against the side rooms' walls... (hinged on the doorways' south jambs)
        AddDoorLeaf(PAt(-hx - t, 0, doorHi), float.Pi / 2, swingSign: -1f);   // into the tech room
        AddDoorLeaf(PAt(hx + t, 0, doorHi), float.Pi / 2, swingSign: 1f);     // into the armoury
        // ...and a closed door in the north wall, out to the rest of the station (and a lit exit sign over it).
        BuildHallDoor(PoliceExitDoor);
    }

    /// <summary>A door casing (architrave) round an opening in a wall face at <paramref name="foot"/> (the opening's centre on the floor).</summary>
    private void AddCasing(float3 foot, float3 normal, float width, float height)
    {
        using var piece = Piece("door casing");
        var xf = WallFrame(foot, normal);
        var b = PB(Material.Veneer, Skirting, occludes: false);
        const float w = 0.07f, d = 0.018f;
        OBox(b, xf, new float3(-width / 2 - w / 2, height / 2, d / 2), new float3(w, height, d));
        OBox(b, xf, new float3(width / 2 + w / 2, height / 2, d / 2), new float3(w, height, d));
        OBox(b, xf, new float3(0, height + w / 2, d / 2), new float3(width + 2 * w, w, d));
    }

    /// <summary>
    /// A door leaf standing open against a wall: hinged at <paramref name="hinge"/>, closed it would run along -z;
    /// it's swung <paramref name="angle"/> to the +x side (<paramref name="swingSign"/> 1) or the -x side (-1).
    /// </summary>
    private void AddDoorLeaf(float3 hinge, float angle, float swingSign)
    {
        using var piece = Piece("door");
        var f = float4x4.CreateRotationY(-swingSign * angle) * float4x4.CreateTranslation(hinge);
        var leaf = PB(Material.Veneer, new float3(0.46f, 0.36f, 0.25f), occludes: false);
        const float width = DoorHalf * 2 - 0.04f;
        OBox(leaf, f, new float3(0, (DoorTop - 0.02f) / 2, -width / 2 - 0.02f), new float3(0.04f, DoorTop - 0.03f, width));
        var steel = PB(Material.Steel, new float3(0.55f, 0.55f, 0.56f), occludes: false);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            OBox(steel, f, new float3(side * 0.045f, 1.02f, -width + 0.06f), new float3(0.02f, 0.02f, 0.13f));
            OBox(steel, f, new float3(side * 0.03f, 1.02f, -width + 0.12f), new float3(0.03f, 0.06f, 0.02f));
            OBox(steel, f, new float3(side * 0.021f, 0.12f, -width / 2 - 0.02f), new float3(0.003f, 0.22f, width - 0.06f));   // kick plate
        }
        FrameCollider(f, new float3(0, DoorTop / 2, -width / 2 - 0.02f), new float3(0.05f, DoorTop, width), blocksShots: true);
    }

    /// <summary>A closed door in the north wall at <paramref name="foot"/> (centre of its foot on the wall face), with an exit sign.</summary>
    private void BuildHallDoor(float3 foot)
    {
        using var piece = Piece("door");
        var xf = WallFrame(foot, new float3(0, 0, 1));
        var leaf = PB(Material.Veneer, new float3(0.46f, 0.36f, 0.25f), occludes: false);
        OBox(leaf, xf, new float3(0, 1.03f, 0.02f), new float3(0.92f, 2.05f, 0.04f));
        OBox(PB(Material.Glass, new float3(0.25f, 0.28f, 0.3f), occludes: false), xf, new float3(0.2f, 1.55f, 0.041f), new float3(0.14f, 0.6f, 0.004f));
        var steel = PB(Material.Steel, new float3(0.55f, 0.55f, 0.56f), occludes: false);
        OBox(steel, xf, new float3(-0.36f, 1.02f, 0.075f), new float3(0.13f, 0.02f, 0.02f));
        OBox(steel, xf, new float3(-0.40f, 1.02f, 0.055f), new float3(0.02f, 0.06f, 0.03f));
        OBox(steel, xf, new float3(0, 0.12f, 0.042f), new float3(0.86f, 0.22f, 0.003f));
        AddCasing(foot, new float3(0, 0, 1), 0.96f, 2.08f);
        // Exit sign: a white box with green lettering, lit.
        var sign = foot + new float3(0, 2.42f, 0);
        OBox(PB(Material.Plain, new float3(0.9f, 0.9f, 0.88f), occludes: false), xf, new float3(0, 2.42f, 0.03f), new float3(0.36f, 0.15f, 0.06f));
        OBox(PB(Material.Emissive, new float3(0.88f, 0.98f, 0.9f), occludes: false), xf, new float3(0, 2.42f, 0.061f), new float3(0.33f, 0.12f, 0.002f));
        AddLettering("EXIT", sign + new float3(0, 0, 0.062f), new float3(0, 0, 1), 0.075f, Material.Emissive, new float3(0.05f, 0.75f, 0.3f), 0.002f);
    }

    /// <summary>The south wall: a ribbon of windows between piers, half-lowered blinds, radiators under the sills.</summary>
    private void BuildPoliceWindows()
    {
        const float hz = BullpenHalfZ, t = PoliceWall;
        const float sill = 0.92f, head = 2.42f;
        // Six bays, piers between them (and in the corners, where the side walls stop at the room's face).
        float[] piers = [-9f, -6.05f, -3.05f, -0.05f, 2.95f, 5.95f, 9f];
        PoliceWallBox(-BullpenHalfX - t, hz, -BullpenHalfX, hz + t, PoliceBeige, sill, head);
        PoliceWallBox(BullpenHalfX, hz, BullpenHalfX + t, hz + t, PoliceBeige, sill, head);
        PoliceWallBox(-BullpenHalfX - t, hz, BullpenHalfX + t, hz + t, PoliceBeige, 0, sill);
        PoliceWallBox(-BullpenHalfX - t, hz, BullpenHalfX + t, hz + t, PoliceBeige, head, PoliceCeiling + 0.15f);
        var frame = PB(Material.Plain, new float3(0.16f, 0.16f, 0.17f), occludes: false);
        var slats = PB(Material.Plain, new float3(0.84f, 0.84f, 0.82f), occludes: false);
        for (var i = 0; i < piers.Length - 1; i++)
        {
            var x0 = piers[i] + (i == 0 ? 0f : 0.15f);
            var x1 = piers[i + 1] - (i == piers.Length - 2 ? 0f : 0.15f);
            if (i > 0)
                PoliceWallBox(piers[i] - 0.15f, hz, piers[i] + 0.15f, hz + t, PoliceBeige, sill, head);
            // Glass (daylight, or the dark outside), set back in the wall; a black steel frame with a transom; a sill board.
            var piece = Piece("window frame");
            AddMinMax(PAt(x0, sill, hz + 0.12f), PAt(x1, head, hz + 0.14f), TimeOfDay.Daylight(DayNightLevel.Police, new float3(0.93f, 0.95f, 0.97f)), Material.Window, blocksShots: false);
            var mid = (x0 + x1) / 2;
            OBox(frame, float4x4.Identity, PAt(mid, sill + 0.025f, hz + 0.1f), new float3(x1 - x0, 0.05f, 0.04f));
            OBox(frame, float4x4.Identity, PAt(mid, head - 0.025f, hz + 0.1f), new float3(x1 - x0, 0.05f, 0.04f));
            OBox(frame, float4x4.Identity, PAt(mid, 1.98f, hz + 0.1f), new float3(x1 - x0, 0.04f, 0.04f));
            OBox(frame, float4x4.Identity, PAt(mid, (sill + head) / 2, hz + 0.1f), new float3(0.04f, head - sill, 0.04f));
            foreach (var x in (ReadOnlySpan<float>)[x0 + 0.02f, x1 - 0.02f])
                OBox(frame, float4x4.Identity, PAt(x, (sill + head) / 2, hz + 0.1f), new float3(0.04f, head - sill, 0.04f));
            OBox(PB(Material.Plain, new float3(0.86f, 0.85f, 0.82f), occludes: false), float4x4.Identity, PAt(mid, sill + 0.01f, hz - 0.04f), new float3(x1 - x0 + 0.04f, 0.03f, 0.2f));
            piece.Dispose();
            // Venetian blinds, let down part way (more on some windows than others), and a head box.
            piece = Piece("blind");
            var drop = i % 3 == 1 ? 0.95f : i % 3 == 2 ? 0.25f : 0.5f;
            OBox(slats, float4x4.Identity, PAt(mid, head - 0.03f, hz + 0.02f), new float3(x1 - x0 - 0.04f, 0.05f, 0.05f));
            for (var y = head - 0.08f; y > head - drop; y -= 0.035f)
                OBox(slats, float4x4.Identity, PAt(mid, y, hz + 0.02f), new float3(x1 - x0 - 0.06f, 0.004f, 0.045f), float4x4.CreateRotationX(0.35f));
            OBox(slats, float4x4.Identity, PAt(mid, head - drop, hz + 0.02f), new float3(x1 - x0 - 0.06f, 0.015f, 0.05f));
            piece.Dispose();
            // A radiator under the window: white, finned.
            piece = Piece("radiator");
            var rad = PB(Material.Plain, new float3(0.86f, 0.86f, 0.84f));
            var rw = float.Min(1.6f, x1 - x0 - 0.5f);
            OBox(rad, float4x4.Identity, PAt(mid, 0.42f, hz - 0.06f), new float3(rw, 0.55f, 0.04f));
            for (var x = mid - rw / 2 + 0.03f; x < mid + rw / 2 - 0.02f; x += 0.05f)
                OBox(rad, float4x4.Identity, PAt(x, 0.42f, hz - 0.1f), new float3(0.012f, 0.55f, 0.05f));
            AddStatic(new Aabb(PAt(mid - rw / 2, 0.1f, hz - 0.13f), PAt(mid + rw / 2, 0.7f, hz)), default, visible: false);
            piece.Dispose();
        }
    }

    private void BuildPoliceCeiling()
    {
        // One suspended ceiling over everything (the shader draws its T-bar grid); the light panels sit in its grid.
        AddMinMax(PAt(TechMinX - PoliceWall, PoliceCeiling, -BullpenHalfZ - PoliceWall), PAt(ArmouryMaxX + PoliceWall, PoliceCeiling + 0.1f, BullpenHalfZ + PoliceWall),
                  CeilingTint, Material.CeilingTile);
        var trim = PB(Material.Plain, new float3(0.9f, 0.9f, 0.9f), occludes: false);
        var diffuser = PB(Material.Emissive, PanelLight, occludes: false);
        foreach (var (x, z, _) in PolicePanels)
        {
            using var piece = Piece("light panel");
            OBox(trim, float4x4.Identity, PAt(x, PoliceCeiling - 0.006f, z), new float3(0.6f, 0.012f, 1.2f));
            OBox(diffuser, float4x4.Identity, PAt(x, PoliceCeiling - 0.014f, z), new float3(0.54f, 0.006f, 1.14f));
            // Louvre lines across the diffuser.
            for (var k = -2; k <= 2; k++)
                OBox(trim, float4x4.Identity, PAt(x, PoliceCeiling - 0.019f, z + k * 0.22f), new float3(0.54f, 0.006f, 0.015f));
        }

        // Air diffusers (square, with nested rings), smoke alarms and a CCTV dome over the bullpen.
        var vent = PB(Material.Plain, new float3(0.82f, 0.82f, 0.8f), occludes: false);
        var ventDark = PB(Material.Plain, new float3(0.25f, 0.25f, 0.25f), occludes: false);
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(-4.5f, -2.1f), (4.5f, -2.1f), (-4.5f, 2.1f), (4.5f, 2.1f), (0f, 5.7f), (-11.7f, -3.6f), (14.1f, -3.6f)])
        {
            using var piece = Piece("air vent");
            OBox(ventDark, float4x4.Identity, PAt(x, PoliceCeiling - 0.004f, z), new float3(0.56f, 0.008f, 0.56f));
            for (var k = 0; k < 4; k++)
            {
                var s = 0.58f - k * 0.13f;
                foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
                {
                    OBox(vent, float4x4.Identity, PAt(x + side * s / 2, PoliceCeiling - 0.02f - k * 0.008f, z), new float3(0.03f, 0.02f, s));
                    OBox(vent, float4x4.Identity, PAt(x, PoliceCeiling - 0.02f - k * 0.008f, z + side * s / 2), new float3(s, 0.02f, 0.03f));
                }
            }
        }
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(0f, -1.8f), (-4.5f, 4.5f), (4.5f, 4.5f), (-11.7f, -0.6f), (11f, -1.5f), (14.1f, -0.5f)])
        {
            using var piece = Piece("smoke alarm");
            OCyl(vent, float4x4.Identity, PAt(x, PoliceCeiling, z), PAt(x, PoliceCeiling - 0.04f, z), 0.06f, 0.055f, 14);
            OBox(PB(Material.Emissive, new float3(0.9f, 0.15f, 0.1f), occludes: false), float4x4.Identity, PAt(x + 0.03f, PoliceCeiling - 0.041f, z), new float3(0.006f, 0.002f, 0.006f));
        }
        using var dome = Piece("CCTV dome");
        var cctv = float4x4.CreateTranslation(PAt(0.3f, PoliceCeiling - 0.03f, -6.2f));
        OCyl(PB(Material.Plain, new float3(0.85f, 0.85f, 0.83f), occludes: false), float4x4.Identity, PAt(0.3f, PoliceCeiling, -6.2f), PAt(0.3f, PoliceCeiling - 0.03f, -6.2f), 0.09f, 0.09f, 18);
        OHemisphere(PB(Material.Glass, new float3(0.06f, 0.06f, 0.07f), occludes: false), cctv, 0.075f);
    }

    // ------------------------------------------------------------------------------------------------
    // The bullpen.
    // ------------------------------------------------------------------------------------------------

    private void BuildBullpen(Random random)
    {
        // Two rows of three clusters of four desks, between the walkway (z = -3.5) and the windows.
        foreach (var cz in (ReadOnlySpan<float>)[0.15f, 4.55f])
            foreach (var cx in (ReadOnlySpan<float>)[-5.2f, 0f, 5.2f])
                AddDeskCluster(cx, cz, random);

        BuildSignWall(random);
        BuildGreenWall(random);
        BuildEastWall(random);
    }

    /// <summary>
    /// Four desks round a frosted-glass spine running along x at z = <paramref name="cz"/>: two on its north side (their
    /// users face south), two on its south side.
    /// </summary>
    private void AddDeskCluster(float cx, float cz, Random random)
    {
        const float deskW = 1.5f, deskD = 0.75f, top = 0.74f;
        var frosted = PB(Material.Plain, Frosted);
        var rail = PB(Material.Steel, Aluminium);
        var dark = PB(Material.Veneer, DarkWood);
        var spine = Frame(PAt(cx, 0, cz), 0);
        var halfLen = deskW + 0.04f;

        // The spine: a dark wood modesty panel under the tops, frosted glass above, an aluminium cap rail and posts.
        using (Piece("desk divider"))
        {
            OBox(dark, spine, new float3(0, top / 2, 0), new float3(halfLen * 2, top, 0.05f));
            OBox(frosted, spine, new float3(0, (top + 1.24f) / 2, 0), new float3(halfLen * 2, 1.24f - top, 0.024f));
            OBox(rail, spine, new float3(0, 1.25f, 0), new float3(halfLen * 2 + 0.02f, 0.025f, 0.045f));
            foreach (var x in (ReadOnlySpan<float>)[-halfLen, 0f, halfLen])
                OBox(rail, spine, new float3(x, (top + 1.24f) / 2, 0), new float3(0.035f, 1.24f - top, 0.04f));
            FrameCollider(spine, new float3(0, 0.63f, 0), new float3(halfLen * 2 + 0.04f, 1.26f, 0.08f));
        }

        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            // side -1: the north row (desk between z = cz - D and cz, user on the north facing south: yaw pi).
            var yaw = side < 0 ? float.Pi : 0f;
            var frontZ = cz + side * deskD;
            // Gables (dark wood ends) at both ends of the row and between the desks; frosted screens on the ends and
            // between the two desks.
            using (Piece("desk screens"))
            foreach (var x in (ReadOnlySpan<float>)[-halfLen + 0.015f, 0f, halfLen - 0.015f])
            {
                var gable = Frame(PAt(cx + x, 0, cz + side * deskD / 2), 0);
                OBox(dark, gable, new float3(0, (top - 0.03f) / 2, 0), new float3(x == 0 ? 0.04f : 0.03f, top - 0.03f, deskD));
                OBox(frosted, gable, new float3(0, (top + 1.12f) / 2 + 0.01f, 0), new float3(0.02f, 1.12f - top, deskD - 0.04f));
                OBox(rail, gable, new float3(0, 1.135f, 0), new float3(0.04f, 0.022f, deskD - 0.02f));
                OBox(rail, gable, new float3(0, (top + 1.12f) / 2, side * (deskD / 2 - 0.02f)), new float3(0.035f, 1.12f - top, 0.035f));
            }
            foreach (var dx in (ReadOnlySpan<float>)[-deskW / 2 - 0.02f, deskW / 2 + 0.02f])
            {
                var f = Frame(PAt(cx + dx, 0, frontZ), yaw);
                // Leave the odd desk empty (a chair pushed in, nothing on it but paper).
                var occupied = random.NextDouble() > 0.15;
                using (Piece("desk"))
                {
                    AddWorkstation(f, random, occupied, pedestalRight: dx > 0 == side > 0);
                    FrameCollider(f, new float3(0, top / 2, -deskD / 2), new float3(deskW, top + 0.02f, deskD));
                }
                // The chair: pulled out (and turned) at a desk in use, tucked in at an empty one.
                var pull = occupied ? 0.45f + (float)random.NextDouble() * 0.25f : 0.22f;
                var turn = occupied ? ((float)random.NextDouble() - 0.5f) * 1.1f : ((float)random.NextDouble() - 0.5f) * 0.2f;
                var chair = float4x4.CreateRotationY(turn) * float4x4.CreateTranslation(((float)random.NextDouble() - 0.5f) * 0.3f, 0, pull) * f;
                AddOfficeChair(chair);
                FrameCollider(chair, new float3(0, 0.45f, 0.02f), new float3(0.5f, 0.9f, 0.5f), blocksShots: false);
            }
        }
    }

    private static readonly float3[] ScreenColors =
    [
        new(0.30f, 0.40f, 0.52f),   // desktop blue
        new(0.78f, 0.80f, 0.82f),   // a document
        new(0.22f, 0.30f, 0.36f),   // the records system
    ];

    /// <summary>
    /// One desk in the frame <paramref name="f"/>: its front edge (the user's side) along the frame's x axis at z = 0, the
    /// desk running back to z = -0.75; its user sits at +z facing -z.
    /// </summary>
    private void AddWorkstation(in float4x4 f, Random random, bool occupied, bool pedestalRight)
    {
        const float w = 1.5f, d = 0.75f, top = 0.74f;
        var veneer = PB(Material.Veneer, DeskTop);
        var dark = PB(Material.Veneer, DarkWood);
        var plastic = PB(Material.Polymer, OfficeBlack);
        var steel = PB(Material.Steel, Aluminium);
        OBox(veneer, f, new float3(0, top - 0.015f, -d / 2), new float3(w, 0.03f, d));
        OBox(dark, f, new float3(0, top - 0.015f, -0.006f), new float3(w, 0.031f, 0.012f));          // edge band

        // A drawer pedestal under one end, a PC tower on the floor at the other.
        var px = pedestalRight ? w / 2 - 0.26f : -w / 2 + 0.26f;
        OBox(dark, f, new float3(px, 0.34f, -0.31f), new float3(0.42f, 0.66f, 0.56f));
        foreach (var y in (ReadOnlySpan<float>)[0.24f, 0.46f, 0.6f])
        {
            OBox(PB(Material.Veneer, DarkWood * 0.55f), f, new float3(px, y, -0.028f), new float3(0.4f, 0.006f, 0.004f));
            OBox(steel, f, new float3(px, y + (y < 0.3f ? 0.09f : 0.06f), -0.022f), new float3(0.12f, 0.012f, 0.012f));
        }
        var tx = -px * 0.97f;
        using (Piece("PC tower"))
        {
            OBox(plastic, f, new float3(tx, 0.215f, -0.4f), new float3(0.19f, 0.43f, 0.44f));
            OBox(PB(Material.Emissive, new float3(0.3f, 0.6f, 1.0f), occludes: false), f, new float3(tx + 0.05f, 0.38f, -0.179f), new float3(0.01f, 0.01f, 0.002f));
        }

        // Paperwork whether or not anyone's here.
        var paper = PB(Material.Plain, new float3(0.88f, 0.88f, 0.86f));
        var stack = 0.01f + (float)random.NextDouble() * 0.05f;
        var px2 = (pedestalRight ? -1 : 1) * (0.45f + (float)random.NextDouble() * 0.15f);
        OBox(paper, f, new float3(px2, top + stack / 2, -0.32f), new float3(0.22f, stack, 0.3f), float4x4.CreateRotationY(((float)random.NextDouble() - 0.5f) * 0.4f));
        if (random.NextDouble() < 0.5)
            OBox(PB(Material.Plain, new float3(0.25f, 0.32f, 0.55f)), f, new float3(px2 * 0.8f, top + stack + 0.02f, -0.3f), new float3(0.24f, 0.04f, 0.31f), float4x4.CreateRotationY(0.3f)); // a binder

        // Monitors (one or two) at the back, facing the user.
        var dual = random.NextDouble() < 0.6;
        var screenTint = ScreenColors[random.Next(ScreenColors.Length)];
        if (!occupied)
            screenTint = new float3(0.03f, 0.035f, 0.04f);
        float[] monitorsAt = dual ? [-0.28f, 0.28f] : [0f];
        foreach (var mx in monitorsAt)
        {
            using var monitor = Piece("monitor");
            var m = float4x4.CreateRotationY(dual ? -mx * 0.6f : 0f) * float4x4.CreateTranslation(mx, top, -d + 0.22f) * f;
            OBox(plastic, m, new float3(0, 0.008f, 0), new float3(0.22f, 0.016f, 0.17f));
            OBox(plastic, m, new float3(0, 0.15f, -0.03f), new float3(0.045f, 0.28f, 0.025f));
            OBox(plastic, m, new float3(0, 0.32f, 0), new float3(0.55f, 0.33f, 0.028f));
            OBox(PB(Material.Emissive, screenTint, occludes: false), m, new float3(0, 0.325f, 0.0145f), new float3(0.52f, 0.29f, 0.002f));
            if (occupied)
            {
                // A window open on the screen, and its title bar.
                OBox(PB(Material.Emissive, new float3(0.86f, 0.87f, 0.88f), occludes: false), m, new float3(0.04f, 0.31f, 0.0158f), new float3(0.34f, 0.2f, 0.001f));
                OBox(PB(Material.Emissive, new float3(0.16f, 0.28f, 0.55f), occludes: false), m, new float3(0.04f, 0.418f, 0.0159f), new float3(0.34f, 0.02f, 0.001f));
            }
        }
        // Keyboard, mouse, desk phone, a mug.
        using (Piece("keyboard and mouse"))
        {
            OBox(plastic, f, new float3(0, top + 0.012f, -0.22f), new float3(0.44f, 0.022f, 0.15f));
            OBox(PB(Material.Polymer, new float3(0.12f, 0.12f, 0.13f)), f, new float3(0, top + 0.024f, -0.22f), new float3(0.41f, 0.004f, 0.12f));
            OBox(plastic, f, new float3(0.33f, top + 0.015f, -0.2f), new float3(0.06f, 0.03f, 0.1f));
        }
        var phone = float4x4.CreateRotationY(0.4f) * float4x4.CreateTranslation(-0.55f * (pedestalRight ? -1 : 1), top, -0.5f) * f;
        OBox(plastic, phone, new float3(0, 0.035f, 0), new float3(0.2f, 0.05f, 0.21f), float4x4.CreateRotationX(-0.18f));
        OBox(plastic, phone, new float3(-0.07f, 0.075f, 0), new float3(0.05f, 0.04f, 0.2f));
        if (occupied && random.NextDouble() < 0.6)
        {
            var mug = new float3(0.5f * (pedestalRight ? 1 : -1), top, -0.12f);
            var mugTint = random.NextDouble() < 0.5 ? new float3(0.85f, 0.85f, 0.82f) : new float3(0.12f, 0.2f, 0.45f);
            OCyl(PB(Material.Plain, mugTint), f, mug, mug + new float3(0, 0.1f, 0), 0.04f, 0.04f, 12);
        }
    }

    /// <summary>The north wall: the department's sign over a long credenza, a copier and printer by the green wall.</summary>
    private void BuildSignWall(Random random)
    {
        const float hz = BullpenHalfZ;
        var wall = PAt(0, 0, -hz);
        var normal = new float3(0, 0, 1);
        // Three framed panels: sheriff's star, "POLICE DEPT.", star.
        var sign = Piece("department sign");
        var frameWood = PB(Material.Veneer, new float3(0.32f, 0.23f, 0.17f), occludes: false);
        var inset = PB(Material.WallPaint, new float3(0.68f, 0.60f, 0.55f), occludes: false);
        var xf = WallFrame(wall, normal);
        foreach (var (x, width) in (ReadOnlySpan<(float, float)>)[(-1.9f, 0.8f), (0f, 2.7f), (1.9f, 0.8f)])
        {
            OBox(frameWood, xf, new float3(x, 2.02f, 0.015f), new float3(width, 0.68f, 0.03f));
            OBox(inset, xf, new float3(x, 2.02f, 0.031f), new float3(width - 0.1f, 0.58f, 0.004f));
        }
        var ink = new float3(0.10f, 0.10f, 0.12f);
        AddLettering("POLICE DEPT.", wall + new float3(0, 2.02f, 0.034f), normal, 0.2f, Material.Plain, ink, 0.008f);
        foreach (var x in (ReadOnlySpan<float>)[-1.9f, 1.9f])
            AddSheriffStar(wall + new float3(x, 2.02f, 0.034f), normal, 0.22f, ink, new float3(0.78f, 0.76f, 0.70f));
        sign.Dispose();

        // The credenza: dark wood, sliding doors, with a plant, binders and a trophy on it.
        var credenza = Piece("credenza");
        var dark = PB(Material.Veneer, DarkWood);
        OBox(dark, xf, new float3(0, 0.37f, 0.25f), new float3(4.6f, 0.74f, 0.5f));
        OBox(PB(Material.Veneer, DeskTop), xf, new float3(0, 0.755f, 0.26f), new float3(4.64f, 0.03f, 0.53f));
        for (var x = -2.3f + 0.575f; x < 2.3f; x += 1.15f)
        {
            OBox(PB(Material.Veneer, DarkWood * 0.5f), xf, new float3(x - 0.575f + 0.003f, 0.37f, 0.502f), new float3(0.006f, 0.66f, 0.004f));
            OBox(PB(Material.Steel, Aluminium), xf, new float3(x + 0.45f, 0.45f, 0.505f), new float3(0.015f, 0.14f, 0.012f));
        }
        AddStatic(new Aabb(PAt(-2.32f, 0, -hz), PAt(2.32f, 0.77f, -hz + 0.53f)), default, visible: false);
        credenza.Dispose();
        AddPottedPlant(wall + new float3(-1.9f, 0.77f, 0.25f), 0.45f, random);
        AddPottedPlant(PAt(3.2f, 0, -hz + 0.35f), 1.1f, random);
        AddPottedPlant(PAt(-3.2f, 0, -hz + 0.35f), 1.1f, random);
        using var ornaments = Piece("binders and trophy");
        for (var i = 0; i < 6; i++)
            OBox(PB(Material.Plain, i % 2 == 0 ? new float3(0.12f, 0.2f, 0.42f) : new float3(0.5f, 0.12f, 0.1f)), xf,
                 new float3(0.9f + i * 0.065f, 0.77f + 0.15f, 0.25f), new float3(0.055f, 0.3f, 0.26f), float4x4.CreateRotationZ(i == 5 ? -0.25f : 0f));
        var gold = PB(Material.Steel, new float3(0.75f, 0.6f, 0.25f));
        OBox(PB(Material.Veneer, DarkWood * 0.7f), xf, new float3(-0.6f, 0.82f, 0.25f), new float3(0.14f, 0.08f, 0.14f));
        OCyl(gold, xf, new float3(-0.6f, 0.86f, 0.25f), new float3(-0.6f, 1.02f, 0.25f), 0.012f, 0.012f, 8);
        OCyl(gold, xf, new float3(-0.6f, 1.02f, 0.25f), new float3(-0.6f, 1.12f, 0.25f), 0.03f, 0.06f, 14);
    }

    /// <summary>A plant in a pot standing on <paramref name="ground"/> (a desk or the floor), about <paramref name="height"/> tall.</summary>
    private void AddPottedPlant(float3 ground, float height, Random random)
    {
        using var piece = Piece("potted plant");
        AddPottedPlantParts(ground, height, random);
    }

    private void AddPottedPlantParts(float3 ground, float height, Random random)
    {
        var potH = float.Min(0.35f, height * 0.35f);
        var potR = potH * 0.55f;
        OCyl(PB(Material.Plain, new float3(0.18f, 0.18f, 0.19f)), float4x4.Identity, ground, ground + new float3(0, potH, 0), potR * 0.8f, potR, 16);
        var leaves = new MeshBuilder();
        var crown = height - potH;
        for (var i = 0; i < 6; i++)
        {
            var a = float.Tau * i / 6 + (float)random.NextDouble();
            var off = new float3(float.Cos(a), 0, float.Sin(a)) * crown * 0.18f;
            leaves.Blob(ground + new float3(0, potH + crown * (0.35f + 0.12f * (i % 3)), 0) + off, new float3(crown * 0.32f, crown * 0.38f, crown * 0.32f), random, 8, 5);
        }
        _props.Add(new Prop(leaves.Build(), float4x4.Identity, new float3(0.20f, 0.38f, 0.14f), Material.Bush));
        AddStatic(Aabb.FromCenterSize(ground + new float3(0, height / 2, 0), new float3(potR * 2, height, potR * 2)), default, visible: false, blocksShots: false);
    }

    /// <summary>The green wall (west): copier and printer, a notice board, filing cabinets, a clock.</summary>
    private void BuildGreenWall(Random random)
    {
        const float x = -BullpenHalfX;
        var normal = new float3(1, 0, 0);

        // The copier: a big pale grey machine with a document feeder, its control panel lit, an output tray.
        var piece = Piece("photocopier");
        var copier = Frame(PAt(x + 0.42f, 0, -1.4f), -float.Pi / 2);   // facing +x (local -z -> +x)
        var body = PB(Material.Plain, new float3(0.80f, 0.81f, 0.80f));
        var grey = PB(Material.Plain, new float3(0.38f, 0.39f, 0.41f));
        OBox(body, copier, new float3(0, 0.45f, 0), new float3(0.62f, 0.9f, 0.66f));
        OBox(grey, copier, new float3(0, 0.95f, 0.02f), new float3(0.6f, 0.1f, 0.58f));
        OBox(grey, copier, new float3(0, 1.02f, 0.05f), new float3(0.5f, 0.04f, 0.4f), float4x4.CreateRotationX(-0.12f));
        OBox(grey, copier, new float3(0, 0.93f, -0.36f), new float3(0.3f, 0.05f, 0.12f), float4x4.CreateRotationX(0.35f));
        OBox(PB(Material.Emissive, new float3(0.45f, 0.65f, 0.85f), occludes: false), copier, new float3(-0.02f, 0.958f, -0.40f), new float3(0.12f, 0.004f, 0.06f), float4x4.CreateRotationX(0.35f));
        for (var i = 0; i < 4; i++)
            OBox(grey, copier, new float3(0, 0.12f + i * 0.17f, -0.332f), new float3(0.58f, 0.008f, 0.006f));
        OBox(grey, copier, new float3(0.38f, 0.62f, 0), new float3(0.16f, 0.02f, 0.3f), float4x4.CreateRotationZ(0.2f));
        OBox(PB(Material.Plain, new float3(0.9f, 0.9f, 0.88f)), copier, new float3(0.39f, 0.64f, 0), new float3(0.14f, 0.01f, 0.24f), float4x4.CreateRotationZ(0.2f));
        FrameCollider(copier, new float3(0, 0.55f, 0), new float3(0.7f, 1.1f, 0.7f));
        piece.Dispose();

        // A desktop printer on a small cabinet.
        piece = Piece("printer");
        var printer = Frame(PAt(x + 0.3f, 0, 0.6f), -float.Pi / 2);
        OBox(PB(Material.Veneer, DarkWood), printer, new float3(0, 0.35f, 0), new float3(0.6f, 0.7f, 0.5f));
        OBox(PB(Material.Polymer, OfficeBlack), printer, new float3(0, 0.84f, 0), new float3(0.48f, 0.28f, 0.42f));
        OBox(body, printer, new float3(0, 0.99f, 0.03f), new float3(0.46f, 0.02f, 0.3f));
        FrameCollider(printer, new float3(0, 0.5f, 0), new float3(0.6f, 1.0f, 0.5f));
        piece.Dispose();

        // Notice board (cork, pinned papers) over the printer, and a clock.
        piece = Piece("notice board");
        var board = WallFrame(PAt(x, 0, 0.6f), normal);
        OBox(PB(Material.Veneer, Skirting, occludes: false), board, new float3(0, 1.62f, 0.012f), new float3(1.3f, 0.95f, 0.024f));
        OBox(PB(Material.Plain, new float3(0.55f, 0.40f, 0.26f), occludes: false), board, new float3(0, 1.62f, 0.025f), new float3(1.22f, 0.87f, 0.004f));
        float3[] papers = [new(0.92f, 0.92f, 0.9f), new(0.92f, 0.92f, 0.9f), new(0.95f, 0.88f, 0.45f), new(0.62f, 0.78f, 0.92f), new(0.92f, 0.92f, 0.9f)];
        for (var i = 0; i < 9; i++)
        {
            var c = new float3(-0.48f + (i % 5) * 0.24f + ((float)random.NextDouble() - 0.5f) * 0.08f, 1.45f + (i / 5) * 0.36f + ((float)random.NextDouble() - 0.5f) * 0.08f, 0.029f);
            OBox(PB(Material.Plain, papers[i % papers.Length], occludes: false), board, c, new float3(0.19f, 0.26f, 0.002f), float4x4.CreateRotationZ(((float)random.NextDouble() - 0.5f) * 0.25f));
            OBox(PB(Material.Plain, new float3(0.8f, 0.1f, 0.1f), occludes: false), board, c + new float3(0, 0.11f, 0.004f), new float3(0.012f, 0.012f, 0.008f));
        }
        piece.Dispose();
        piece = Piece("wall clock");
        var clock = WallFrame(PAt(x, 2.32f, -3.5f + 1.3f), normal);
        ODisc(PB(Material.Polymer, OfficeBlack, occludes: false), clock, float3.Zero, 0.16f, 0.04f, 28);
        ODisc(PB(Material.Plain, new float3(0.95f, 0.95f, 0.93f), occludes: false), clock, new float3(0, 0, 0.04f), 0.145f, 0.002f, 28);
        var hands = PB(Material.Plain, new float3(0.05f, 0.05f, 0.05f), occludes: false);
        for (var i = 0; i < 12; i++)
            OBox(hands, clock, float3.Zero, new float3(0.006f, 0.02f, 0.003f), float4x4.CreateTranslation(0, 0.125f, 0.043f) * float4x4.CreateRotationZ(i * float.Tau / 12));
        OBox(hands, clock, float3.Zero, new float3(0.01f, 0.08f, 0.003f), float4x4.CreateTranslation(0, 0.035f, 0.046f) * float4x4.CreateRotationZ(-1.2f));
        OBox(hands, clock, float3.Zero, new float3(0.007f, 0.12f, 0.003f), float4x4.CreateTranslation(0, 0.055f, 0.048f) * float4x4.CreateRotationZ(2.4f));
        piece.Dispose();

        // Filing cabinets along the south end of the wall.
        for (var i = 0; i < 4; i++)
            AddFilingCabinet(PAt(x + 0.32f, 0, 4.2f + i * 0.5f), -float.Pi / 2, random);
        // A fire extinguisher by the tech room door, and a sign over the door.
        AddExtinguisher(PAt(x + 0.1f, 0, DoorZ - 0.9f));
        using var techSign = Piece("TECH ROOM sign");
        AddLettering("TECH ROOM", PAt(x + 0.016f, DoorTop + 0.24f, DoorZ), normal, 0.09f, Material.Plain, new float3(0.92f, 0.92f, 0.9f), 0.004f);
        OBox(PB(Material.Plain, new float3(0.12f, 0.2f, 0.42f), occludes: false), WallFrame(PAt(x, 0, DoorZ), normal), new float3(0, DoorTop + 0.24f, 0.008f), new float3(1.0f, 0.17f, 0.016f));
    }

    /// <summary>A four-drawer steel filing cabinet standing on <paramref name="ground"/>, its drawers facing the frame's -z after turning by <paramref name="yaw"/>.</summary>
    private void AddFilingCabinet(float3 ground, float yaw, Random random)
    {
        using var piece = Piece("filing cabinet");
        AddFilingCabinetParts(ground, yaw, random);
    }

    private void AddFilingCabinetParts(float3 ground, float yaw, Random random)
    {
        var f = Frame(ground, yaw);
        var tint = new float3(0.70f, 0.68f, 0.62f);
        var body = PB(Material.Plain, tint);
        OBox(body, f, new float3(0, 0.66f, 0), new float3(0.47f, 1.32f, 0.62f));
        var handle = PB(Material.Steel, Aluminium);
        for (var i = 0; i < 4; i++)
        {
            var y = 0.17f + i * 0.32f;
            var out_ = random.NextDouble() < 0.12 ? 0.18f : 0f;   // the odd drawer left open
            OBox(body, f, new float3(0, y + 0.01f, -0.31f - out_ / 2), new float3(0.43f, 0.29f, 0.012f + out_));
            OBox(handle, f, new float3(0, y + 0.08f, -0.322f - out_), new float3(0.14f, 0.022f, 0.02f));
            OBox(PB(Material.Plain, new float3(0.9f, 0.9f, 0.86f)), f, new float3(0, y + 0.03f, -0.318f - out_), new float3(0.08f, 0.04f, 0.002f));
        }
        FrameCollider(f, new float3(0, 0.66f, 0), new float3(0.47f, 1.32f, 0.62f));
    }

    /// <summary>A red fire extinguisher on a bracket, its foot at <paramref name="ground"/> (against a wall).</summary>
    private void AddExtinguisher(float3 ground)
    {
        using var piece = Piece("fire extinguisher");
        AddExtinguisherParts(ground);
    }

    private void AddExtinguisherParts(float3 ground)
    {
        var red = PB(Material.Plain, new float3(0.7f, 0.07f, 0.05f), occludes: false);
        var black = PB(Material.Polymer, OfficeBlack, occludes: false);
        OCyl(red, float4x4.Identity, ground + new float3(0, 0.55f, 0), ground + new float3(0, 1.05f, 0), 0.075f, 0.075f, 14);
        OCyl(red, float4x4.Identity, ground + new float3(0, 1.05f, 0), ground + new float3(0, 1.1f, 0), 0.075f, 0.03f, 14);
        OBox(black, float4x4.Identity, ground + new float3(0, 1.13f, 0), new float3(0.04f, 0.06f, 0.12f));
        OCyl(black, float4x4.Identity, ground + new float3(0, 1.12f, 0.06f), ground + new float3(0.02f, 0.72f, 0.09f), 0.012f, 0.012f, 6);
        OBox(PB(Material.Plain, new float3(0.9f, 0.9f, 0.86f), occludes: false), float4x4.Identity, ground + new float3(0.075f, 0.8f, 0), new float3(0.002f, 0.14f, 0.1f));
    }

    /// <summary>The east wall: the way through to the armoury, a water cooler and a whiteboard.</summary>
    private void BuildEastWall(Random random)
    {
        const float x = BullpenHalfX;
        var normal = new float3(-1, 0, 0);
        var wall = WallFrame(PAt(x, 0, DoorZ), normal);
        var piece = Piece("ARMOURY sign");
        OBox(PB(Material.Plain, new float3(0.62f, 0.07f, 0.06f), occludes: false), wall, new float3(0, DoorTop + 0.24f, 0.008f), new float3(1.0f, 0.17f, 0.016f));
        AddLettering("ARMOURY", PAt(x - 0.016f, DoorTop + 0.24f, DoorZ), normal, 0.09f, Material.Plain, new float3(0.95f, 0.95f, 0.93f), 0.004f);
        piece.Dispose();
        // "Authorised personnel only" beside the door, and a card reader.
        piece = Piece("authorised personnel sign");
        var plate = WallFrame(PAt(x, 1.55f, DoorZ + 0.95f), normal);
        OBox(PB(Material.Plain, new float3(0.9f, 0.85f, 0.15f), occludes: false), plate, new float3(0, 0, 0.004f), new float3(0.56f, 0.22f, 0.008f));
        AddLettering("AUTHORISED", PAt(x - 0.009f, 1.6f, DoorZ + 0.95f), normal, 0.042f, Material.Plain, new float3(0.05f, 0.05f, 0.05f), 0.002f);
        AddLettering("PERSONNEL ONLY", PAt(x - 0.009f, 1.5f, DoorZ + 0.95f), normal, 0.042f, Material.Plain, new float3(0.05f, 0.05f, 0.05f), 0.002f);
        piece.Dispose();
        piece = Piece("card reader");
        var reader = WallFrame(PAt(x, 1.2f, DoorZ + 0.72f), normal);
        OBox(PB(Material.Polymer, OfficeBlack, occludes: false), reader, new float3(0, 0, 0.015f), new float3(0.08f, 0.13f, 0.03f));
        OBox(PB(Material.Emissive, new float3(0.9f, 0.15f, 0.1f), occludes: false), reader, new float3(0, 0.045f, 0.031f), new float3(0.012f, 0.012f, 0.002f));

        piece.Dispose();
        // Water cooler.
        piece = Piece("water cooler");
        var cooler = PAt(x - 0.25f, 0, 1.8f);
        OBox(PB(Material.Plain, new float3(0.88f, 0.88f, 0.86f)), float4x4.Identity, cooler + new float3(0, 0.5f, 0), new float3(0.32f, 1.0f, 0.32f));
        OBox(PB(Material.Plain, new float3(0.3f, 0.3f, 0.32f)), float4x4.Identity, cooler + new float3(-0.161f, 0.75f, 0), new float3(0.004f, 0.16f, 0.2f));
        OCyl(PB(Material.Plain, new float3(0.45f, 0.62f, 0.82f)), float4x4.Identity, cooler + new float3(0, 1.0f, 0), cooler + new float3(0, 1.42f, 0), 0.13f, 0.13f, 18);
        OCyl(PB(Material.Plain, new float3(0.45f, 0.62f, 0.82f)), float4x4.Identity, cooler + new float3(0, 1.42f, 0), cooler + new float3(0, 1.5f, 0), 0.13f, 0.05f, 18);
        AddStatic(Aabb.FromCenterSize(cooler + new float3(0, 0.75f, 0), new float3(0.34f, 1.5f, 0.34f)), default, visible: false);

        piece.Dispose();
        // Whiteboard: an aluminium frame, marker tray, someone's duty roster and a sketch.
        piece = Piece("whiteboard");
        var wb = WallFrame(PAt(x, 0, 4.4f), normal);
        OBox(PB(Material.Steel, Aluminium, occludes: false), wb, new float3(0, 1.5f, 0.01f), new float3(1.84f, 1.04f, 0.02f));
        OBox(PB(Material.Plain, new float3(0.95f, 0.95f, 0.95f), occludes: false), wb, new float3(0, 1.5f, 0.021f), new float3(1.8f, 1.0f, 0.002f));
        OBox(PB(Material.Steel, Aluminium, occludes: false), wb, new float3(0, 0.98f, 0.04f), new float3(1.6f, 0.02f, 0.06f));
        var blue = PB(Material.Plain, new float3(0.08f, 0.15f, 0.55f), occludes: false);
        var red = PB(Material.Plain, new float3(0.65f, 0.08f, 0.06f), occludes: false);
        for (var row = 0; row < 6; row++)
        {
            OBox(blue, wb, new float3(-0.55f, 1.86f - row * 0.11f, 0.023f), new float3(0.5f, 0.006f, 0.001f));
            for (var k = 0; k < 4; k++)
                OBox(row % 2 == 0 ? blue : red, wb, new float3(-0.12f + k * 0.14f + (float)random.NextDouble() * 0.02f, 1.86f - row * 0.11f, 0.023f), new float3(0.06f + (float)random.NextDouble() * 0.04f, 0.035f, 0.001f));
        }
        OBox(blue, wb, new float3(-0.3f, 1.92f, 0.023f), new float3(1.1f, 0.005f, 0.001f));
        for (var i = 0; i < 16; i++)
        {
            var a = float.Tau * i / 16;
            OBox(red, wb, new float3(0.62f + float.Cos(a) * 0.17f, 1.3f + float.Sin(a) * 0.17f, 0.023f), new float3(0.07f, 0.007f, 0.001f), float4x4.CreateRotationZ(a + float.Pi / 2));
        }
        for (var i = 0; i < 4; i++)
            OBox(i % 2 == 0 ? blue : red, wb, new float3(-0.6f + i * 0.05f, 0.995f, 0.05f), new float3(0.016f, 0.016f, 0.12f), float4x4.CreateRotationY(float.Pi / 2));
        piece.Dispose();
        AddPottedPlant(PAt(x - 0.35f, 0, 6.5f), 1.3f, random);
        AddPottedPlant(PAt(-x + 0.35f, 0, -0.4f), 1.0f, random);
    }

    // ------------------------------------------------------------------------------------------------
    // The tech room: the navy portal, wired into a PC.
    // ------------------------------------------------------------------------------------------------

    private void BuildTechRoom(Random random)
    {
        const float wallX = TechMinX;

        // The PC desk against the north wall, its user facing the wall (north), the portal off to their left.
        var desk = Frame(PAt(-13.2f, 0, -BullpenHalfZ + 0.75f), 0);
        var piece = Piece("PC desk");
        var veneer = PB(Material.Veneer, new float3(0.70f, 0.70f, 0.68f));
        var legs = PB(Material.Plain, new float3(0.22f, 0.22f, 0.23f));
        OBox(veneer, desk, new float3(0, 0.725f, -0.375f), new float3(1.6f, 0.03f, 0.75f));
        foreach (var lx in (ReadOnlySpan<float>)[-0.76f, 0.76f])
        {
            OBox(legs, desk, new float3(lx, 0.355f, -0.06f), new float3(0.05f, 0.71f, 0.05f));
            OBox(legs, desk, new float3(lx, 0.355f, -0.69f), new float3(0.05f, 0.71f, 0.05f));
            OBox(legs, desk, new float3(lx, 0.68f, -0.375f), new float3(0.04f, 0.05f, 0.65f));
        }
        OBox(legs, desk, new float3(0, 0.68f, -0.69f), new float3(1.5f, 0.05f, 0.03f));
        FrameCollider(desk, new float3(0, 0.37f, -0.375f), new float3(1.6f, 0.76f, 0.75f));
        piece.Dispose();
        AddOfficeChair(float4x4.CreateRotationY(0.5f) * float4x4.CreateTranslation(0.1f, 0, 0.55f) * desk, 0.18f);
        FrameCollider(float4x4.CreateTranslation(0.1f, 0, 0.55f) * desk, new float3(0, 0.45f, 0), new float3(0.5f, 0.9f, 0.5f), blocksShots: false);

        // The PC: a big tower under the west end of the desk, a wide monitor showing the portal's status.
        var plastic = PB(Material.Polymer, OfficeBlack);
        var tower = new float3(-0.55f, 0, -0.38f);
        using (Piece("PC tower"))
        {
            OBox(plastic, desk, tower + new float3(0, 0.25f, 0), new float3(0.22f, 0.5f, 0.5f));
            OBox(PB(Material.Glass, new float3(0.08f, 0.09f, 0.1f)), desk, tower + new float3(0.111f, 0.27f, 0), new float3(0.002f, 0.4f, 0.42f));
            OBox(PB(Material.Emissive, new float3(0.35f, 0.5f, 1.0f), occludes: false), desk, tower + new float3(0.05f, 0.47f, 0.251f), new float3(0.01f, 0.01f, 0.002f));
        }
        piece = Piece("monitor");
        var monitor = float4x4.CreateTranslation(0.1f, 0.74f, -0.55f) * desk;
        OBox(plastic, monitor, new float3(0, 0.008f, 0), new float3(0.26f, 0.016f, 0.2f));
        OBox(plastic, monitor, new float3(0, 0.17f, -0.04f), new float3(0.05f, 0.32f, 0.03f));
        OBox(plastic, monitor, new float3(0, 0.38f, 0), new float3(0.82f, 0.38f, 0.03f));
        var screen = PB(Material.Emissive, new float3(0.03f, 0.05f, 0.12f), occludes: false);
        OBox(screen, monitor, new float3(0, 0.385f, 0.0155f), new float3(0.79f, 0.35f, 0.002f));
        // On screen: the portal's ring, a waveform and a column of readouts.
        var glow = PB(Material.Emissive, PolicePortalColor * 0.9f + new float3(0.05f), occludes: false);
        var text = PB(Material.Emissive, new float3(0.55f, 0.75f, 0.95f), occludes: false);
        for (var i = 0; i < 24; i++)
        {
            var a = float.Tau * i / 24;
            OBox(glow, monitor, float3.Zero, new float3(0.034f, 0.008f, 0.001f),
                 float4x4.CreateRotationZ(a + float.Pi / 2) * float4x4.CreateTranslation(-0.2f + float.Cos(a) * 0.1f, 0.39f + float.Sin(a) * 0.1f, 0.0168f));
        }
        for (var i = 0; i < 18; i++)
        {
            var h = 0.01f + 0.04f * float.Abs(float.Sin(i * 0.9f)) * (0.6f + 0.4f * (float)random.NextDouble());
            OBox(glow, monitor, new float3(0.02f + i * 0.012f, 0.27f + h / 2, 0.0168f), new float3(0.007f, h, 0.001f));
        }
        for (var i = 0; i < 7; i++)
            OBox(text, monitor, new float3(0.28f, 0.52f - i * 0.03f, 0.0168f), new float3(0.06f + (i * 37 % 5) * 0.012f, 0.009f, 0.001f));
        OBox(PB(Material.Emissive, new float3(0.2f, 0.9f, 0.45f), occludes: false), monitor, new float3(-0.2f, 0.39f, 0.0168f), new float3(0.028f, 0.028f, 0.001f));
        piece.Dispose();
        using (Piece("keyboard and mouse"))
        {
            OBox(plastic, desk, new float3(0.1f, 0.752f, -0.25f), new float3(0.44f, 0.022f, 0.15f));
            OBox(plastic, desk, new float3(0.43f, 0.755f, -0.23f), new float3(0.06f, 0.03f, 0.1f));
        }
        using (Piece("papers and mug"))
        {
            OBox(PB(Material.Plain, new float3(0.9f, 0.9f, 0.88f)), desk, new float3(0.6f, 0.75f, -0.4f), new float3(0.21f, 0.02f, 0.3f), float4x4.CreateRotationY(-0.2f));
            OCyl(PB(Material.Plain, new float3(0.6f, 0.1f, 0.08f)), desk, new float3(-0.35f, 0.74f, -0.12f), new float3(-0.35f, 0.84f, -0.12f), 0.04f, 0.04f, 12);
        }

        // Above the desk, two CCTV monitors on wall brackets showing the security cameras (the bunker's, and beside it
        // the vault's; the pictures themselves are drawn live: Game/SecurityCamera.cs, PoliceCctvScreen and
        // PoliceVaultCctvScreen), each with a label strip under it and its lead down the wall.
        void CctvMonitor(in float4x4 cctv, string label, float lead)
        {
            using var cctvPiece = Piece("CCTV monitor");
            OBox(plastic, cctv, new float3(0, 0, 0.02f), new float3(0.2f, 0.16f, 0.04f));
            OBox(plastic, cctv, new float3(0, 0, 0.06f), new float3(0.05f, 0.05f, 0.06f));
            OBox(plastic, cctv, new float3(0, 0, 0.105f), new float3(0.78f, 0.47f, 0.035f));
            OBox(PB(Material.Polymer, new float3(0.02f, 0.02f, 0.022f), occludes: false), cctv, new float3(0, 0.01f, 0.1225f), new float3(0.74f, 0.42f, 0.001f));
            OBox(PB(Material.Emissive, new float3(0.2f, 1.0f, 0.35f), occludes: false), cctv, new float3(0.35f, -0.222f, 0.1228f), new float3(0.008f, 0.005f, 0.001f));
            OBox(PB(Material.Plain, new float3(0.93f, 0.93f, 0.9f), occludes: false), cctv, new float3(0, -0.29f, 0.0015f), new float3(0.3f, 0.05f, 0.003f));
            AddLettering(label, float3.Transform(new float3(0, -0.29f, 0.0035f), cctv), new float3(0, 0, 1), 0.022f, Material.Plain, new float3(0.05f, 0.05f, 0.05f), 0.001f);
            AddTrunk(PB(Material.Polymer, OfficeBlack, occludes: false), float3.Transform(new float3(0.06f, -0.08f, 0.012f), cctv), float3.Transform(new float3(0.06f, -lead, 0.012f), cctv), 0.006f, 0.006f, 6);
        }
        CctvMonitor(CctvMonitorFrame, "BUNKER CAM 01", 0.98f);
        CctvMonitor(VaultCctvMonitorFrame, "VAULT CAM 02", 1.78f);
        piece.Dispose();
        piece = Piece("cables");
        // The wiring: three thick cables out of the front of the tower, down to the floor and across it under a
        // yellow-and-black cable protector to the wall below a junction box beside the ring, up into the box, and
        // from the box in steel conduits into the ring itself.
        float3[] cableTints = [new(0.04f, 0.04f, 0.045f), new(0.75f, 0.42f, 0.08f), new(0.10f, 0.20f, 0.55f)];
        var junction = PAt(wallX + 0.07f, 0.85f, DoorZ - 1.75f);
        var floorFrom = PAt(-13.72f, 0, -6.15f);
        var floorTo = PAt(-14.22f, 0, -5.42f);
        for (var k = 0; k < 3; k++)
        {
            var b = PB(Material.Polymer, cableTints[k], occludes: false);
            var off = (k - 1) * 0.04f;
            var r = 0.014f + 0.003f * k;
            float3[] path =
            [
                PAt(-13.72f + off, 0.16f, -6.37f),
                PAt(-13.72f + off, 0.05f, -6.3f),
                floorFrom + new float3(off, r, 0),
                floorTo + new float3(off * 0.8f, r, off * 0.4f),
                new float3(junction.X - 0.02f + k * 0.035f, PoliceOrigin.Y + r, junction.Z - 0.12f + off * 0.3f),
                new float3(junction.X - 0.02f + k * 0.035f, PoliceOrigin.Y + 0.2f, junction.Z - 0.05f),
                new float3(junction.X - 0.02f + k * 0.035f, PoliceOrigin.Y + 0.66f, junction.Z - 0.05f + off * 0.5f),
            ];
            for (var i = 0; i < path.Length - 1; i++)
                AddTrunk(b, path[i], path[i + 1], r, r, 8);
        }
        piece.Dispose();
        piece = Piece("cable protector");
        // The cable protector: a low rubber ramp along the run, black with yellow stripes along it.
        {
            var along = floorTo - floorFrom;
            var len = float.Sqrt(along.X * along.X + along.Z * along.Z) + 0.3f;
            var yaw = float.Atan2(along.X, along.Z);
            var ramp = float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation((floorFrom + floorTo) / 2);
            OBox(PB(Material.Polymer, new float3(0.05f, 0.05f, 0.05f), occludes: false), ramp, new float3(0, 0.022f, 0), new float3(0.3f, 0.044f, len));
            foreach (var x in (ReadOnlySpan<float>)[-0.12f, 0f, 0.12f])
                OBox(PB(Material.Plain, new float3(0.9f, 0.72f, 0.08f), occludes: false), ramp, new float3(x, 0.0445f, 0), new float3(0.03f, 0.002f, len - 0.04f));
        }

        // A power cabinet on the other side of the ring, its own conduit into the ring's south side.
        var cab = WallFrame(PAt(wallX, 0, -1.4f), new float3(1, 0, 0));
        piece = Piece("power cabinet");
        var cabinetGrey = PB(Material.Plain, new float3(0.62f, 0.64f, 0.63f));
        OBox(cabinetGrey, cab, new float3(0, 0.95f, 0.25f), new float3(0.7f, 1.9f, 0.5f));
        OBox(PB(Material.Plain, new float3(0.5f, 0.52f, 0.51f)), cab, new float3(0, 0.95f, 0.501f), new float3(0.004f, 1.84f, 0.002f));
        OBox(PB(Material.Steel, new float3(0.3f, 0.3f, 0.32f)), cab, new float3(0.05f, 1.0f, 0.515f), new float3(0.02f, 0.18f, 0.03f));
        foreach (var (dx, dy) in (ReadOnlySpan<(float, float)>)[(-0.2f, 1.55f), (0.2f, 1.55f)])
        {
            ODisc(PB(Material.Polymer, OfficeBlack, occludes: false), cab, new float3(dx, dy, 0.5f), 0.075f, 0.02f, 20);
            ODisc(PB(Material.Plain, new float3(0.93f, 0.92f, 0.86f), occludes: false), cab, new float3(dx, dy, 0.52f), 0.062f, 0.002f, 20);
            OBox(PB(Material.Plain, new float3(0.7f, 0.05f, 0.04f), occludes: false), cab, new float3(dx + 0.015f, dy + 0.015f, 0.524f), new float3(0.05f, 0.005f, 0.002f), float4x4.CreateRotationZ(0.7f));
        }
        float3[] lamps = [new(0.2f, 1.0f, 0.35f), new(0.2f, 1.0f, 0.35f), new(1.0f, 0.65f, 0.1f), PolicePortalColor];
        for (var i = 0; i < lamps.Length; i++)
            OCyl(PB(Material.Emissive, lamps[i], occludes: false), cab, new float3(-0.24f + i * 0.16f, 1.3f, 0.5f), new float3(-0.24f + i * 0.16f, 1.3f, 0.52f), 0.02f, 0.02f, 12);
        OBox(PB(Material.Plain, new float3(0.95f, 0.95f, 0.93f), occludes: false), cab, new float3(0, 1.79f, 0.502f), new float3(0.5f, 0.07f, 0.002f));
        AddLettering("PORTAL SUPPLY", PAt(wallX + 0.505f, 1.79f, -1.4f), new float3(1, 0, 0), 0.035f, Material.Plain, new float3(0.05f, 0.05f, 0.05f), 0.002f);
        AddStatic(new Aabb(PAt(wallX, 0, -1.75f), PAt(wallX + 0.5f, 1.9f, -1.05f)), default, visible: false);
        piece.Dispose();
        {
            var ringC = PolicePortalFloorPoint + new float3(0, PortalSystem.CenterHeight, 0);
            var conduitSteel = PB(Material.Steel, new float3(0.5f, 0.5f, 0.52f), occludes: false);
            foreach (var dy in (ReadOnlySpan<float>)[1.5f, 1.2f])
            {
                var a = float.Asin(float.Clamp((dy - PortalSystem.CenterHeight) / 1.34f, -1f, 1f));
                var end = ringC + new float3(0.06f, float.Sin(a) * 1.34f, float.Cos(a) * 1.34f);
                var start = PAt(wallX + 0.06f, dy, -1.75f);
                AddTrunk(conduitSteel, start, end, 0.018f, 0.018f, 8);
                OCyl(conduitSteel, float4x4.Identity, end + new float3(-0.01f, 0, 0), end + new float3(0.03f, 0, 0), 0.028f, 0.028f, 8);
            }
        }

        piece.Dispose();
        piece = Piece("hazard marking");
        // Yellow-and-black hazard marking on the floor round the front of the ring.
        for (var i = 0; i < 22; i++)
        {
            var a = -1.45f + 2.9f * (i + 0.5f) / 22;
            var c = PolicePortalFloorPoint + new float3(float.Cos(a) * 1.75f, 0.002f, float.Sin(a) * 1.75f);
            OBox(PB(Material.Plain, i % 2 == 0 ? new float3(0.9f, 0.72f, 0.08f) : new float3(0.05f, 0.05f, 0.05f), occludes: false),
                 float4x4.CreateRotationY(-a) * float4x4.CreateTranslation(c), float3.Zero, new float3(0.08f, 0.004f, 0.24f));
        }

        piece.Dispose();
        piece = Piece("laptop trolley");
        // A trolley with a laptop on it (running diagnostics), against the north wall between the desk and the UPS.
        var trolley = Frame(PAt(-11.6f, 0, -BullpenHalfZ + 0.25f), 0);
        var trolleySteel = PB(Material.Steel, new float3(0.55f, 0.56f, 0.58f));
        foreach (var ty in (ReadOnlySpan<float>)[0.2f, 0.82f])
            OBox(trolleySteel, trolley, new float3(0, ty, 0), new float3(0.6f, 0.025f, 0.42f));
        foreach (var (lx, lz) in (ReadOnlySpan<(float, float)>)[(-0.28f, -0.19f), (0.28f, -0.19f), (-0.28f, 0.19f), (0.28f, 0.19f)])
        {
            OBox(trolleySteel, trolley, new float3(lx, 0.47f, lz), new float3(0.025f, 0.8f, 0.025f));
            OCyl(PB(Material.Polymer, OfficeBlack), trolley, new float3(lx - 0.02f, 0.035f, lz), new float3(lx + 0.02f, 0.035f, lz), 0.035f, 0.035f, 10);
        }
        OBox(PB(Material.Polymer, new float3(0.1f, 0.1f, 0.11f)), trolley, new float3(0, 0.845f, 0.04f), new float3(0.34f, 0.02f, 0.24f));
        var lid = float4x4.CreateRotationX(-0.25f) * float4x4.CreateTranslation(0, 0.855f, -0.08f) * trolley;
        OBox(PB(Material.Polymer, new float3(0.1f, 0.1f, 0.11f)), lid, new float3(0, 0.12f, 0), new float3(0.34f, 0.24f, 0.012f));
        OBox(PB(Material.Emissive, new float3(0.06f, 0.08f, 0.16f), occludes: false), lid, new float3(0, 0.125f, 0.0065f), new float3(0.31f, 0.2f, 0.001f));
        for (var i = 0; i < 14; i++)
            OBox(PB(Material.Emissive, PolicePortalColor * 0.9f + new float3(0.05f), occludes: false), lid,
                 new float3(-0.13f + i * 0.019f, 0.1f + 0.03f * float.Sin(i * 0.8f), 0.0072f), new float3(0.012f, 0.006f, 0.001f));
        OBox(PB(Material.Plain, new float3(0.55f, 0.56f, 0.55f)), trolley, new float3(0, 0.3f, 0), new float3(0.4f, 0.18f, 0.3f));
        FrameCollider(trolley, new float3(0, 0.45f, 0), new float3(0.62f, 0.9f, 0.44f), blocksShots: false);
        AddCable(float3.Transform(new float3(0.17f, 0.85f, 0.0f), trolley), float3.Transform(new float3(0.3f, 0.3f, 0.0f), trolley), new float3(0.04f, 0.04f, 0.045f));

        piece.Dispose();
        piece = Piece("junction box");
        // The junction box: grey steel, a yellow warning label, a red emergency stop; conduits into the ring's rim.
        var box = WallFrame(junction - new float3(0.07f, 0, 0), new float3(1, 0, 0));
        OBox(PB(Material.Plain, new float3(0.55f, 0.56f, 0.55f)), box, new float3(0, 0, 0.07f), new float3(0.3f, 0.4f, 0.14f));
        OBox(PB(Material.Plain, new float3(0.9f, 0.75f, 0.1f), occludes: false), box, new float3(0, 0.12f, 0.141f), new float3(0.16f, 0.07f, 0.002f));
        AddLettering("DANGER", junction + new float3(0.073f, 0.12f, 0), new float3(1, 0, 0), 0.03f, Material.Plain, new float3(0.05f, 0.05f, 0.05f), 0.002f);
        OCyl(PB(Material.Plain, new float3(0.95f, 0.8f, 0.1f), occludes: false), box, new float3(0, -0.06f, 0.14f), new float3(0, -0.06f, 0.15f), 0.045f, 0.045f, 16);
        OCyl(PB(Material.Plain, new float3(0.75f, 0.05f, 0.04f), occludes: false), box, new float3(0, -0.06f, 0.15f), new float3(0, -0.06f, 0.18f), 0.028f, 0.032f, 16);
        OBox(PB(Material.Emissive, new float3(0.2f, 1.0f, 0.4f), occludes: false), box, new float3(0.1f, 0.15f, 0.141f), new float3(0.015f, 0.015f, 0.003f));
        var conduit = PB(Material.Steel, new float3(0.5f, 0.5f, 0.52f), occludes: false);
        var ringCentre = PolicePortalFloorPoint + new float3(0, PortalSystem.CenterHeight, 0);
        foreach (var (dy, dx) in (ReadOnlySpan<(float, float)>)[(0.1f, 0.05f), (-0.08f, 0.07f)])
        {
            var start = junction + new float3(dx, dy, 0.15f);
            var a = float.Asin(float.Clamp((start.Y - ringCentre.Y) / 1.38f, -1f, 1f));
            var end = ringCentre + new float3(dx, float.Sin(a) * 1.34f, -float.Cos(a) * 1.34f);
            AddTrunk(conduit, start, end, 0.016f, 0.016f, 8);
            OCyl(conduit, float4x4.Identity, end + new float3(-0.01f, 0, 0), end + new float3(0.03f, 0, 0), 0.026f, 0.026f, 8);
        }

        piece.Dispose();
        piece = Piece("server rack");
        // A server rack in the north-east corner, front to the south, lights blinking (well, lit).
        var rack = Frame(PAt(-10.0f, 0, -6.45f), float.Pi);   // front (local -z) facing south
        var rackBody = PB(Material.Plain, new float3(0.08f, 0.08f, 0.09f));
        OBox(rackBody, rack, new float3(0, 1.0f, 0), new float3(0.62f, 2.0f, 1.0f));
        var units = PB(Material.Polymer, new float3(0.15f, 0.15f, 0.16f));
        var green = PB(Material.Emissive, new float3(0.2f, 1.0f, 0.35f), occludes: false);
        var amber = PB(Material.Emissive, new float3(1.0f, 0.65f, 0.15f), occludes: false);
        var y = 0.15f;
        while (y < 1.8f)
        {
            var u = 0.045f * (1 + random.Next(3));
            OBox(units, rack, new float3(0, y + u / 2, -0.505f), new float3(0.48f, u - 0.004f, 0.01f));
            for (var i = 0; i < 6; i++)
                if (random.NextDouble() < 0.7)
                    OBox(random.NextDouble() < 0.8 ? green : amber, rack, new float3(-0.2f + i * 0.025f, y + u / 2, -0.511f), new float3(0.008f, 0.008f, 0.002f));
            y += u + (random.NextDouble() < 0.3 ? 0.045f : 0f);
        }
        FrameCollider(rack, new float3(0, 1.0f, 0), new float3(0.62f, 2.0f, 1.0f));
        piece.Dispose();
        piece = Piece("UPS and cable tray");
        // A UPS beside it, and a cable tray along the wall over to the desk.
        OBox(PB(Material.Polymer, OfficeBlack), Frame(PAt(-10.7f, 0, -6.7f), 0), new float3(0, 0.3f, 0), new float3(0.25f, 0.6f, 0.55f));
        OBox(PB(Material.Emissive, new float3(0.2f, 1.0f, 0.35f), occludes: false), Frame(PAt(-10.7f, 0, -6.7f), 0), new float3(0, 0.5f, 0.276f), new float3(0.04f, 0.015f, 0.002f));
        AddStatic(new Aabb(PAt(-10.83f, 0, -6.98f), PAt(-10.57f, 0.6f, -6.42f)), default, visible: false);
        var tray = PB(Material.Steel, new float3(0.45f, 0.46f, 0.48f), occludes: false);
        OBox(tray, float4x4.Identity, PAt(-12.0f, 2.45f, -BullpenHalfZ + 0.12f), new float3(4.0f, 0.06f, 0.2f));
        for (var k = 0; k < 4; k++)
            OBox(PB(Material.Polymer, cableTints[k % 3], occludes: false), float4x4.Identity, PAt(-12.0f, 2.49f, -BullpenHalfZ + 0.06f + k * 0.035f), new float3(4.0f, 0.025f, 0.025f));

        piece.Dispose();
        piece = Piece("whiteboard");
        // A whiteboard on the south wall covered in someone's portal sums.
        var wb = WallFrame(PAt(-11.85f, 0, SideRoomMaxZ), new float3(0, 0, -1));
        OBox(PB(Material.Steel, Aluminium, occludes: false), wb, new float3(0, 1.5f, 0.01f), new float3(1.84f, 1.04f, 0.02f));
        OBox(PB(Material.Plain, new float3(0.95f, 0.95f, 0.95f), occludes: false), wb, new float3(0, 1.5f, 0.021f), new float3(1.8f, 1.0f, 0.002f));
        var ink = PB(Material.Plain, new float3(0.08f, 0.15f, 0.55f), occludes: false);
        var redInk = PB(Material.Plain, new float3(0.65f, 0.08f, 0.06f), occludes: false);
        foreach (var (cx, cy, rr) in (ReadOnlySpan<(float, float, float)>)[(-0.5f, 1.55f, 0.22f), (0.35f, 1.55f, 0.22f)])
            for (var i = 0; i < 20; i++)
            {
                var a = float.Tau * i / 20;
                OBox(ink, wb, new float3(cx + float.Cos(a) * rr, cy + float.Sin(a) * rr, 0.023f), new float3(0.072f, 0.007f, 0.001f), float4x4.CreateRotationZ(a + float.Pi / 2));
            }
        OBox(redInk, wb, new float3(-0.07f, 1.62f, 0.023f), new float3(0.32f, 0.007f, 0.001f));
        OBox(redInk, wb, new float3(0.06f, 1.635f, 0.023f), new float3(0.06f, 0.007f, 0.001f), float4x4.CreateRotationZ(0.6f));
        OBox(redInk, wb, new float3(0.06f, 1.605f, 0.023f), new float3(0.06f, 0.007f, 0.001f), float4x4.CreateRotationZ(-0.6f));
        for (var row = 0; row < 4; row++)
            OBox(ink, wb, new float3(-0.3f + (row % 2) * 0.1f, 1.22f - row * 0.06f, 0.023f), new float3(0.7f - row * 0.08f, 0.02f, 0.001f));
        piece.Dispose();
        piece = Piece("shelving");
        // Steel shelving in the south-west corner: reels of cable, boxed parts, a spare monitor; a crate on the floor.
        var shelves = PB(Material.Steel, new float3(0.42f, 0.43f, 0.45f));
        const float shx0 = -14.42f, shx1 = -13.3f, shz = -0.25f;
        foreach (var x in (ReadOnlySpan<float>)[shx0, shx1])
            foreach (var z in (ReadOnlySpan<float>)[shz - 0.19f, shz + 0.19f])
                OBox(shelves, float4x4.Identity, PAt(x, 0.9f, z), new float3(0.03f, 1.8f, 0.03f));
        foreach (var sy in (ReadOnlySpan<float>)[0.1f, 0.55f, 1.0f, 1.45f, 1.78f])
            OBox(shelves, float4x4.Identity, PAt((shx0 + shx1) / 2, sy, shz), new float3(shx1 - shx0 + 0.03f, 0.02f, 0.4f));
        AddStatic(new Aabb(PAt(shx0 - 0.02f, 0, shz - 0.21f), PAt(shx1 + 0.02f, 1.8f, shz + 0.21f)), default, visible: false);
        foreach (var (x, tint) in (ReadOnlySpan<(float, float3)>)[(-14.15f, new float3(0.75f, 0.42f, 0.08f)), (-13.85f, new float3(0.04f, 0.04f, 0.045f)), (-13.55f, new float3(0.1f, 0.2f, 0.55f))])
        {
            OCyl(PB(Material.Polymer, tint), float4x4.Identity, PAt(x - 0.06f, 0.73f, shz), PAt(x + 0.06f, 0.73f, shz), 0.16f, 0.16f, 18);
            OCyl(PB(Material.Polymer, new float3(0.6f, 0.45f, 0.25f)), float4x4.Identity, PAt(x - 0.07f, 0.73f, shz), PAt(x + 0.07f, 0.73f, shz), 0.17f, 0.17f, 18);
        }
        for (var i = 0; i < 4; i++)
            OBox(PB(Material.Plain, new float3(0.62f, 0.5f, 0.34f)), float4x4.Identity, PAt(-14.2f + i * 0.27f, 1.16f + (i % 2) * 0.03f, shz), new float3(0.24f, 0.3f + (i % 2) * 0.06f, 0.3f));
        OBox(PB(Material.Polymer, OfficeBlack), float4x4.Identity, PAt(-13.85f, 1.62f, shz), new float3(0.55f, 0.33f, 0.05f));
        piece.Dispose();
        AddCrate(PAt(-10.1f, 0, -0.55f), 0.6f, new float3(0.46f, 0.34f, 0.22f), 1);
        AddExtinguisher(PAt(-9.3f, 0, -0.3f));
        using (Piece("first aid cabinet"))
            BuildPoliceMedkit();   // (on the east wall by the doorway: see LevelPoliceMedkit.cs)
    }

    // ------------------------------------------------------------------------------------------------
    // The armoury: a mesh cage with a counter door, gun lockers, shelving, vests.
    // ------------------------------------------------------------------------------------------------

    private void BuildArmoury(Random random)
    {
        PieceScope piece = default;
        BuildCage();
        BuildArmouryAnteroom(random);

        // Gun lockers on the east wall: two standing open (one straight through the cage door), one shut.
        AddGunLocker(-5.55f, open: true, random);
        AddGunLocker(DoorZ, open: true, random);
        AddGunLocker(-1.25f, open: false, random);

        piece.Dispose();
        piece = Piece("ammo shelving");
        // Steel shelving along the north wall: ammunition cans, boxes, helmets.
        var shelf = PB(Material.Steel, new float3(0.42f, 0.43f, 0.45f));
        const float sx0 = 12.8f, sx1 = 15.0f, sz = -BullpenHalfZ + 0.3f;
        foreach (var x in (ReadOnlySpan<float>)[sx0, sx1])
            foreach (var z in (ReadOnlySpan<float>)[sz - 0.24f, sz + 0.24f])
                OBox(shelf, float4x4.Identity, PAt(x, 1.0f, z), new float3(0.035f, 2.0f, 0.035f));
        float[] levels = [0.12f, 0.6f, 1.08f, 1.56f, 1.98f];
        foreach (var y in levels)
            OBox(shelf, float4x4.Identity, PAt((sx0 + sx1) / 2, y, sz), new float3(sx1 - sx0 + 0.04f, 0.025f, 0.5f));
        AddStatic(new Aabb(PAt(sx0 - 0.02f, 0, sz - 0.27f), PAt(sx1 + 0.02f, 2.0f, sz + 0.27f)), default, visible: false);
        var olive = PB(Material.PaintedMetal, new float3(0.24f, 0.28f, 0.16f));
        var boxGreen = PB(Material.Plain, new float3(0.18f, 0.42f, 0.22f));
        var boxLid = PB(Material.Plain, new float3(0.78f, 0.78f, 0.15f));
        for (var li = 0; li < levels.Length - 1; li++)
        {
            var y = levels[li] + 0.0125f;
            var x = sx0 + 0.08f;
            while (x < sx1 - 0.25f)
            {
                if (li == 3)
                {
                    // Green crates with yellow lids, as in any stores cage.
                    OBox(boxGreen, float4x4.Identity, PAt(x + 0.2f, y + 0.12f, sz), new float3(0.38f, 0.24f, 0.4f));
                    OBox(boxLid, float4x4.Identity, PAt(x + 0.2f, y + 0.25f, sz), new float3(0.4f, 0.025f, 0.42f));
                    x += 0.45f;
                    continue;
                }
                if (li == 2 && random.NextDouble() < 0.5)
                {
                    // A riot helmet with its visor up.
                    var h = float4x4.CreateScale(1f, 0.85f, 1.1f) * float4x4.CreateRotationX(float.Pi) * float4x4.CreateTranslation(PAt(x + 0.15f, y + 0.02f, sz));
                    OHemisphere(PB(Material.Polymer, new float3(0.07f, 0.07f, 0.08f)), h, 0.14f, 14, 5);
                    OBox(PB(Material.Glass, new float3(0.25f, 0.27f, 0.3f)), float4x4.Identity, PAt(x + 0.15f, y + 0.2f, sz + 0.1f), new float3(0.24f, 0.04f, 0.1f), float4x4.CreateRotationX(-0.6f));
                    x += 0.34f;
                    continue;
                }
                // An ammunition can: olive steel, a ridged lid and a handle; some stencilled.
                OBox(olive, float4x4.Identity, PAt(x + 0.07f, y + 0.09f, sz), new float3(0.14f, 0.18f, 0.29f));
                OBox(olive, float4x4.Identity, PAt(x + 0.07f, y + 0.19f, sz), new float3(0.15f, 0.02f, 0.3f));
                OBox(PB(Material.Steel, new float3(0.2f, 0.2f, 0.18f)), float4x4.Identity, PAt(x + 0.07f, y + 0.21f, sz), new float3(0.03f, 0.012f, 0.1f));
                OBox(PB(Material.Plain, new float3(0.85f, 0.8f, 0.3f), occludes: false), float4x4.Identity, PAt(x + 0.07f, y + 0.1f, sz + 0.146f), new float3(0.09f, 0.03f, 0.002f));
                x += 0.17f;
            }
        }

        piece.Dispose();
        piece = Piece("vest rail");
        // Vests on hooks along the south wall: hi-vis jackets and black ballistic vests marked POLICE.
        var hook = PB(Material.Steel, new float3(0.3f, 0.3f, 0.32f), occludes: false);
        OBox(PB(Material.Veneer, DarkWood, occludes: false), float4x4.Identity, PAt(13.9f, 1.85f, -0.0125f), new float3(2.6f, 0.08f, 0.025f));
        for (var i = 0; i < 6; i++)
        {
            var x = 12.95f + i * 0.4f;
            OBox(hook, float4x4.Identity, PAt(x, 1.86f, -0.08f), new float3(0.02f, 0.02f, 0.1f));
            var hiVis = i % 3 != 1;
            var vest = float4x4.CreateTranslation(PAt(x, 1.82f, -0.12f));
            using var vestPiece = Piece(hiVis ? "hi-vis vest" : "ballistic vest");
            if (hiVis)
            {
                var yellow = PB(Material.Cloth, new float3(0.85f, 0.88f, 0.12f), occludes: false);
                var silver = PB(Material.Plain, new float3(0.78f, 0.79f, 0.8f), occludes: false);
                OBox(yellow, vest, new float3(0, -0.34f, 0), new float3(0.38f, 0.62f, 0.07f));
                foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
                    OBox(yellow, vest, new float3(side * 0.12f, -0.02f, 0), new float3(0.1f, 0.06f, 0.06f));
                foreach (var yy in (ReadOnlySpan<float>)[-0.3f, -0.48f])
                    OBox(silver, vest, new float3(0, yy, -0.036f), new float3(0.385f, 0.045f, 0.003f));
            }
            else
            {
                var black = PB(Material.Canvas, new float3(0.07f, 0.075f, 0.085f), occludes: false);
                OBox(black, vest, new float3(0, -0.32f, 0), new float3(0.36f, 0.56f, 0.09f));
                foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
                    OBox(black, vest, new float3(side * 0.12f, -0.02f, 0), new float3(0.09f, 0.08f, 0.07f));
                for (var k = 0; k < 3; k++)
                    OBox(black, vest, new float3(-0.11f + k * 0.11f, -0.5f, -0.06f), new float3(0.09f, 0.13f, 0.04f));
                OBox(PB(Material.Plain, new float3(0.06f, 0.06f, 0.07f), occludes: false), vest, new float3(0, -0.2f, -0.046f), new float3(0.3f, 0.08f, 0.002f));
                AddLettering("POLICE", float3.Transform(new float3(0, -0.2f, -0.048f), vest), new float3(0, 0, -1), 0.045f, Material.Plain, new float3(0.9f, 0.9f, 0.88f), 0.002f);
            }
        }

        piece.Dispose();
        piece = Piece("cleaning bench");
        // A cleaning bench in the middle: a rifle on a green mat, a pistol, a bottle of oil.
        var bench = Frame(PAt(13.95f, 0, -2.2f), 0);
        var benchSteel = PB(Material.Steel, new float3(0.4f, 0.41f, 0.43f));
        OBox(PB(Material.Veneer, new float3(0.55f, 0.43f, 0.3f)), bench, new float3(0, 0.88f, 0), new float3(1.4f, 0.04f, 0.7f));
        foreach (var (lx, lz) in (ReadOnlySpan<(float, float)>)[(-0.66f, -0.31f), (0.66f, -0.31f), (-0.66f, 0.31f), (0.66f, 0.31f)])
            OBox(benchSteel, bench, new float3(lx, 0.43f, lz), new float3(0.05f, 0.86f, 0.05f));
        OBox(benchSteel, bench, new float3(0, 0.18f, 0), new float3(1.36f, 0.02f, 0.66f));
        FrameCollider(bench, new float3(0, 0.45f, 0), new float3(1.4f, 0.9f, 0.7f));
        OBox(PB(Material.Cloth, new float3(0.12f, 0.3f, 0.16f)), bench, new float3(0, 0.903f, 0), new float3(1.1f, 0.006f, 0.45f));
        AddLongGun(float4x4.CreateRotationX(float.Pi / 2) * float4x4.CreateRotationY(0.08f) * float4x4.CreateTranslation(-0.5f, 0.93f, 0.05f) * bench, LongGun.Carbine);
        AddPistolProp(float4x4.CreateRotationX(float.Pi / 2) * float4x4.CreateRotationY(-0.5f) * float4x4.CreateTranslation(0.35f, 0.925f, -0.1f) * bench);
        OCyl(PB(Material.Plain, new float3(0.75f, 0.6f, 0.15f)), bench, new float3(0.55f, 0.9f, 0.2f), new float3(0.55f, 1.05f, 0.2f), 0.03f, 0.03f, 10);
        OBox(PB(Material.Steel, Aluminium), bench, new float3(0.2f, 0.915f, 0.2f), new float3(0.3f, 0.02f, 0.05f));
        piece.Dispose();
    }

    /// <summary>The welded-mesh cage across the armoury at x = 12.4, with its door standing open.</summary>
    private void BuildCage()
    {
        var post = PB(Material.Plain, CageSteel, occludes: false);
        var wire = PB(Material.Plain, CageSteel * 1.1f, occludes: false);
        const float top = PoliceCeiling;
        var doorLo = DoorZ - DoorHalf;
        var doorHi = DoorZ + DoorHalf;

        // A mesh panel in the plane x = CageX from z0 to z1, y0 to y1: framed, wires every 5 cm across and 10 cm up.
        void Panel(float z0, float z1, float y0, float y1)
        {
            var mz = (z0 + z1) / 2;
            OBox(post, float4x4.Identity, PAt(CageX, y0 + 0.015f, mz), new float3(0.03f, 0.03f, z1 - z0));
            OBox(post, float4x4.Identity, PAt(CageX, y1 - 0.015f, mz), new float3(0.03f, 0.03f, z1 - z0));
            for (var z = z0 + 0.05f; z < z1 - 0.02f; z += 0.05f)
                OBox(wire, float4x4.Identity, PAt(CageX, (y0 + y1) / 2, z), new float3(0.005f, y1 - y0 - 0.03f, 0.005f));
            for (var y = y0 + 0.1f; y < y1 - 0.05f; y += 0.1f)
                OBox(wire, float4x4.Identity, PAt(CageX + 0.006f, y, mz), new float3(0.005f, 0.005f, z1 - z0));
        }

        float[] posts = [-BullpenHalfZ, -5.6f, doorLo - 0.04f, doorHi + 0.04f, -1.6f, SideRoomMaxZ];
        foreach (var z in posts)
            OBox(post, float4x4.Identity, PAt(CageX, top / 2, float.Clamp(z, -BullpenHalfZ + 0.03f, SideRoomMaxZ - 0.03f)), new float3(0.06f, top, 0.06f));
        for (var i = 0; i < posts.Length - 1; i++)
        {
            if (i == 2)
                continue;   // the doorway
            Panel(posts[i] + 0.03f, posts[i + 1] - 0.03f, 0.05f, top - 0.01f);
            AddStatic(new Aabb(PAt(CageX - 0.04f, 0, posts[i]), PAt(CageX + 0.04f, top, posts[i + 1])), default, visible: false, blocksShots: false);
        }
        // Over the door: a header and mesh up to the ceiling.
        OBox(post, float4x4.Identity, PAt(CageX, DoorTop + 0.03f, DoorZ), new float3(0.06f, 0.06f, DoorHalf * 2 + 0.08f));
        Panel(doorLo, doorHi, DoorTop + 0.06f, top - 0.01f);
        AddStatic(new Aabb(PAt(CageX - 0.04f, DoorTop, doorLo), PAt(CageX + 0.04f, top, doorHi)), default, visible: false, blocksShots: false);
        // The ARMOURY sign on the mesh over the door (facing the anteroom).
        OBox(PB(Material.Plain, new float3(0.62f, 0.07f, 0.06f), occludes: false), float4x4.Identity, PAt(CageX - 0.04f, 2.42f, DoorZ), new float3(0.01f, 0.18f, 1.1f));
        AddLettering("ARMOURY", PAt(CageX - 0.046f, 2.42f, DoorZ), new float3(-1, 0, 0), 0.1f, Material.Plain, new float3(0.95f, 0.95f, 0.93f), 0.003f);

        // The door: a stable door of two framed mesh leaves on hinges at its south jamb. The bottom half is shut and
        // latched, with a counter shelf across its top on the anteroom side, so kit is handed out over it; the top
        // half stands open, swung out into the anteroom.
        var hinge = PAt(CageX, 0, doorHi);
        var low = float4x4.CreateTranslation(hinge);                                     // shut: runs along -z
        var up = float4x4.CreateRotationY(1.85f) * float4x4.CreateTranslation(hinge);    // open
        const float w = DoorHalf * 2 - 0.05f, h = DoorTop - 0.04f, split = 1.04f;
        var lz = -w / 2 - 0.02f;
        // A leaf from y0 to y1 in the frame xf: stiles, rails, mesh.
        void Leaf(in float4x4 xf, float y0, float y1)
        {
            foreach (var z in (ReadOnlySpan<float>)[-0.035f, -w - 0.005f])
                OBox(post, xf, new float3(0, (y0 + y1) / 2, z), new float3(0.035f, y1 - y0, 0.035f));
            foreach (var y in (ReadOnlySpan<float>)[y0 + 0.0175f, y1 - 0.0175f])
                OBox(post, xf, new float3(0, y, lz), new float3(0.035f, 0.035f, w));
            for (var z = -0.07f; z > -w; z -= 0.05f)
                OBox(wire, xf, new float3(0, (y0 + y1) / 2, z), new float3(0.005f, y1 - y0 - 0.06f, 0.005f));
            for (var y = y0 + 0.1f; y < y1 - 0.05f; y += 0.1f)
                OBox(wire, xf, new float3(0.006f, y, lz), new float3(0.005f, 0.005f, w - 0.04f));
        }
        Leaf(low, 0.03f, split);
        Leaf(up, split + 0.03f, h);
        // The counter shelf on top of the bottom half, sticking out into the anteroom, brackets under it; a
        // clipboard (the sign-out sheet) and a pen on it.
        OBox(post, low, new float3(-0.13f, split + 0.0125f, lz), new float3(0.3f, 0.025f, w + 0.04f));
        foreach (var z in (ReadOnlySpan<float>)[-0.12f, -w + 0.09f])
            OBox(post, low, new float3(-0.09f, split - 0.06f, z), new float3(0.15f, 0.1f, 0.012f), float4x4.CreateRotationZ(-0.6f));
        OBox(PB(Material.Veneer, new float3(0.45f, 0.3f, 0.18f), occludes: false), low, new float3(-0.15f, split + 0.03f, lz + 0.05f), new float3(0.22f, 0.008f, 0.3f), float4x4.CreateRotationY(0.15f));
        OBox(PB(Material.Plain, new float3(0.93f, 0.93f, 0.9f), occludes: false), low, new float3(-0.15f, split + 0.035f, lz + 0.05f), new float3(0.2f, 0.002f, 0.27f), float4x4.CreateRotationY(0.15f));
        OBox(PB(Material.Polymer, new float3(0.1f, 0.15f, 0.5f), occludes: false), low, new float3(-0.2f, split + 0.04f, lz - 0.18f), new float3(0.13f, 0.008f, 0.008f), float4x4.CreateRotationY(0.5f));
        // Lock box and handles on the bottom half's latch side; a bolt joining the halves when the top is shut.
        var brass = PB(Material.Steel, new float3(0.78f, 0.62f, 0.26f), occludes: false);
        OBox(brass, low, new float3(0, 0.88f, -w + 0.06f), new float3(0.07f, 0.24f, 0.13f));
        OBox(PB(Material.Polymer, OfficeBlack, occludes: false), low, new float3(-0.036f, 0.94f, -w + 0.06f), new float3(0.004f, 0.08f, 0.06f));
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            OBox(PB(Material.Steel, Aluminium, occludes: false), low, new float3(side * 0.06f, 0.8f, -w + 0.1f), new float3(0.02f, 0.02f, 0.12f));
        OBox(PB(Material.Steel, Aluminium, occludes: false), up, new float3(0.03f, split + 0.12f, -w + 0.12f), new float3(0.02f, 0.1f, 0.02f));
        // Shut, the bottom half closes the doorway (the cage is staff only); the open top half is out of the way.
        FrameCollider(low, new float3(-0.01f, split / 2, lz), new float3(0.06f, split, w + 0.04f), blocksShots: false);
        FrameCollider(up, new float3(0, (split + h) / 2, lz), new float3(0.06f, h - split, w), blocksShots: false);
        // The strike plate on the latch-side post.
        OBox(brass, float4x4.Identity, PAt(CageX, 0.88f, doorLo - 0.04f), new float3(0.07f, 0.2f, 0.07f));
    }

    /// <summary>The space in front of the cage: a clearing barrel, a bench, a sign-out book on the door's shelf.</summary>
    private void BuildArmouryAnteroom(Random random)
    {
        PieceScope piece = default;
        piece.Dispose();
        piece = Piece("clearing barrel");
        // A clearing barrel (unload into the sand), in the north-west corner under its sign.
        var barrel = PAt(10.0f, 0, -6.35f);
        AddDrum(barrel, new float3(0.85f, 0.62f, 0.1f), 0.3f, Material.Plain);
        OCyl(PB(Material.Steel, new float3(0.25f, 0.25f, 0.26f)), float4x4.Identity, barrel + new float3(0, 0.75f, 0), barrel + new float3(0.25f, 1.06f, 0.25f), 0.06f, 0.06f, 14);
        var signXf = WallFrame(PAt(10.0f, 0, -BullpenHalfZ), new float3(0, 0, 1));
        OBox(PB(Material.Plain, new float3(0.9f, 0.85f, 0.15f), occludes: false), signXf, new float3(0, 1.75f, 0.005f), new float3(0.9f, 0.3f, 0.01f));
        AddLettering("CLEARING", PAt(10.0f, 1.8f, -BullpenHalfZ + 0.011f), new float3(0, 0, 1), 0.07f, Material.Plain, new float3(0.05f, 0.05f, 0.05f), 0.002f);
        AddLettering("POINT", PAt(10.0f, 1.68f, -BullpenHalfZ + 0.011f), new float3(0, 0, 1), 0.07f, Material.Plain, new float3(0.05f, 0.05f, 0.05f), 0.002f);
        piece.Dispose();
        piece = Piece("hazard stripes");
        // Hazard stripes on the floor round it.
        for (var i = 0; i < 8; i++)
            OBox(PB(Material.Plain, i % 2 == 0 ? new float3(0.85f, 0.7f, 0.1f) : new float3(0.06f, 0.06f, 0.06f), occludes: false), float4x4.Identity,
                 PAt(9.55f + i * 0.12f, 0.002f, -5.75f), new float3(0.12f, 0.004f, 0.08f));

        piece.Dispose();
        piece = Piece("steel bench");
        // A steel bench along the south wall.
        var benchSteel = PB(Material.Steel, new float3(0.4f, 0.41f, 0.43f));
        OBox(PB(Material.Veneer, new float3(0.55f, 0.43f, 0.3f)), float4x4.Identity, PAt(10.8f, 0.45f, -0.25f), new float3(1.8f, 0.04f, 0.38f));
        foreach (var x in (ReadOnlySpan<float>)[10.0f, 11.6f])
            OBox(benchSteel, float4x4.Identity, PAt(x, 0.215f, -0.25f), new float3(0.05f, 0.43f, 0.34f));
        AddStatic(new Aabb(PAt(9.9f, 0, -0.45f), PAt(11.7f, 0.47f, -0.05f)), default, visible: false);
        piece.Dispose();
        piece = Piece("sign-out book");
        // The sign-out book and a pen on the bench, a box of gloves.
        OBox(PB(Material.Plain, new float3(0.15f, 0.12f, 0.35f)), float4x4.Identity, PAt(10.5f, 0.485f, -0.25f), new float3(0.32f, 0.03f, 0.24f));
        OBox(PB(Material.Plain, new float3(0.92f, 0.92f, 0.9f)), float4x4.Identity, PAt(10.5f, 0.5f, -0.25f), new float3(0.3f, 0.004f, 0.22f));
        OBox(PB(Material.Plain, new float3(0.85f, 0.85f, 0.88f)), float4x4.Identity, PAt(11.3f, 0.52f, -0.25f), new float3(0.24f, 0.1f, 0.12f));
        AddExtinguisher(PAt(9.3f, 0, -2.7f));
        piece.Dispose();
        piece = Piece("CCTV camera");
        // A CCTV camera watching the cage door.
        var cam = float4x4.CreateRotationY(-0.6f) * float4x4.CreateTranslation(PAt(9.4f, 2.5f, -6.8f));
        OBox(PB(Material.Plain, new float3(0.85f, 0.85f, 0.83f), occludes: false), cam, new float3(0, 0, 0.12f), new float3(0.09f, 0.08f, 0.22f), float4x4.CreateRotationX(-0.35f));
        OBox(PB(Material.Plain, new float3(0.85f, 0.85f, 0.83f), occludes: false), float4x4.Identity, PAt(9.4f, 2.62f, -6.9f), new float3(0.03f, 0.3f, 0.03f));
        piece.Dispose();
    }

    /// <summary>
    /// A tall steel gun locker against the east wall centred on <paramref name="z"/>, facing west. Open: both doors swung
    /// wide, a pegboard back, a comb rack of long guns standing on the shelf, pistols and magazines above.
    /// </summary>
    private void AddGunLocker(float z, bool open, Random random)
    {
        using var piece = Piece("gun locker");
        var f = Frame(PAt(ArmouryMaxX - 0.29f, 0, z), float.Pi / 2);   // front (local -z) facing west (-x)
        const float w = 1.0f, d = 0.55f, h = 2.0f, t = 0.02f;
        var body = PB(Material.Plain, LockerGrey);
        OBox(body, f, new float3(0, h - t / 2, 0), new float3(w, t, d));
        OBox(body, f, new float3(0, 0.05f, 0), new float3(w, 0.1f, d));
        OBox(body, f, new float3(-w / 2 + t / 2, h / 2, 0), new float3(t, h, d));
        OBox(body, f, new float3(w / 2 - t / 2, h / 2, 0), new float3(t, h, d));
        OBox(body, f, new float3(0, h / 2, d / 2 - t / 2), new float3(w, h, t));
        FrameCollider(f, new float3(0, h / 2, 0), new float3(w, h, d));
        var leafW = w / 2 - 0.01f;
        var handle = PB(Material.Steel, new float3(0.25f, 0.25f, 0.27f));
        if (!open)
        {
            foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            {
                OBox(body, f, new float3(side * w / 4, h / 2 + 0.05f, -d / 2 + 0.01f), new float3(leafW, h - 0.12f, 0.02f));
                for (var k = 0; k < 3; k++)
                    OBox(PB(Material.Plain, LockerGrey * 0.8f), f, new float3(side * w / 4, 1.8f - k * 0.025f, -d / 2), new float3(0.3f, 0.008f, 0.004f));
            }
            OBox(handle, f, new float3(0.05f, 1.05f, -d / 2 - 0.02f), new float3(0.03f, 0.2f, 0.03f));
            OBox(PB(Material.Polymer, OfficeBlack), f, new float3(-0.08f, 1.15f, -d / 2), new float3(0.07f, 0.1f, 0.02f));
            OBox(PB(Material.Emissive, new float3(0.9f, 0.15f, 0.1f), occludes: false), f, new float3(-0.08f, 1.18f, -d / 2 - 0.011f), new float3(0.01f, 0.01f, 0.002f));
            return;
        }

        // Pegboard back (a grid of holes), a shelf at the bottom with a drawer, a comb rack for the muzzles.
        OBox(PB(Material.Plain, LockerGrey * 0.92f), f, new float3(0, h / 2, d / 2 - t - 0.006f), new float3(w - 2 * t, h - 0.12f, 0.012f));
        var holes = PB(Material.Plain, new float3(0.12f, 0.12f, 0.12f), occludes: false);
        for (var hy = 0.5f; hy < h - 0.15f; hy += 0.05f)
            for (var hx = -w / 2 + 0.06f; hx < w / 2 - 0.05f; hx += 0.05f)
                OBox(holes, f, new float3(hx, hy, d / 2 - t - 0.0125f), new float3(0.008f, 0.008f, 0.002f));
        OBox(body, f, new float3(0, 0.42f, 0), new float3(w - 2 * t, 0.025f, d - t));
        OBox(body, f, new float3(0, 0.26f, -d / 2 + 0.02f), new float3(w - 2 * t - 0.01f, 0.3f, 0.02f));
        OBox(handle, f, new float3(0, 0.33f, -d / 2), new float3(0.2f, 0.02f, 0.025f));
        var rubber = PB(Material.Polymer, OfficeBlack);
        OBox(rubber, f, new float3(0, 1.25f, 0.06f), new float3(w - 2 * t, 0.05f, 0.16f));
        OBox(rubber, f, new float3(0, 0.46f, 0.0f), new float3(w - 2 * t, 0.06f, 0.2f));
        // Long guns standing in the rack, muzzles up, their tops facing out (the odd slot empty).
        for (var i = 0; i < 6; i++)
        {
            if (random.NextDouble() < 0.12)
                continue;
            var kind = i == 5 ? LongGun.LessLethal : i >= 3 ? LongGun.Shotgun : LongGun.Carbine;
            var gx = -w / 2 + 0.12f + i * 0.152f;
            var gun = float4x4.CreateRotationZ(float.Pi / 2) * float4x4.CreateRotationY(-float.Pi / 2) * float4x4.CreateRotationX(-0.06f)
                      * float4x4.CreateTranslation(gx, 0.44f, 0.06f) * f;
            AddLongGun(gun, kind);
        }
        // A rail of pistols (muzzle down in a rack) and a basket of magazines at the top.
        OBox(rubber, f, new float3(0, 1.72f, 0.12f), new float3(w - 2 * t, 0.04f, 0.12f));
        for (var i = 0; i < 6; i++)
        {
            var gx = -w / 2 + 0.11f + i * 0.155f;
            AddPistolProp(float4x4.CreateRotationZ(-float.Pi / 2) * float4x4.CreateRotationY(float.Pi / 2) * float4x4.CreateTranslation(gx, 1.86f, 0.1f) * f);
        }
        // Wire baskets on the insides of the doors, doors swung wide (each about 110 degrees, hinged on the outer edges).
        var basket = PB(Material.Steel, new float3(0.82f, 0.82f, 0.82f), occludes: false);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var hingeLocal = new float3(side * (w / 2), 0, -d / 2);
            var leaf = float4x4.CreateRotationY(-side * 1.92f) * float4x4.CreateTranslation(hingeLocal) * f;
            // Closed, the leaf runs from the hinge towards the middle: local -side * x.
            OBox(PB(Material.Plain, LockerGrey, occludes: false), leaf, new float3(-side * leafW / 2, h / 2 + 0.05f, -0.01f), new float3(leafW, h - 0.12f, 0.02f));
            OBox(handle, leaf, new float3(-side * (leafW - 0.05f), 1.05f, -0.03f), new float3(0.03f, 0.2f, 0.03f));
            for (var k = 0; k < 2; k++)
            {
                var by = 1.25f + k * 0.4f;
                OBox(basket, leaf, new float3(-side * leafW / 2, by, 0.07f), new float3(leafW - 0.1f, 0.006f, 0.1f));
                OBox(basket, leaf, new float3(-side * leafW / 2, by + 0.05f, 0.12f), new float3(leafW - 0.1f, 0.1f, 0.006f));
                for (var m = 0; m < 3; m++)
                    OBox(PB(Material.Polymer, new float3(0.07f, 0.07f, 0.08f)), leaf, new float3(-side * (0.12f + m * 0.1f), by + 0.06f, 0.07f), new float3(0.03f, 0.12f, 0.06f));
            }
            FrameCollider(leaf, new float3(-side * leafW / 2, h / 2, 0.03f), new float3(leafW, h, 0.12f), blocksShots: false);
        }
    }
}
