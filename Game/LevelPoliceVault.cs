using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The police station's vault: through a steel airlock door in the bullpen's north (sign) wall, a long, bare corridor
/// runs 30 m north to a second airlock door, and beyond it a big empty room of raw concrete with nothing in it but the
/// gold portal in its far wall. The PC that drives it (a second <see cref="PcTerminal"/>, which connects the gold
/// portal rather than the HQ's navy one) is next door, in the east observation room.
///
///     bullpen north wall (z = -7)
///          [K] outer door [K]          K: a keypad (one each side of each door)
///             |  corridor  |
///             |   30 m     |
///          [K] inner door [K]
///     +--------------------------+
///     |                          |
///     |        the vault         |
///     |      16 x 16 x 5 m       |
///     |                          |
///     +-------(portal)-----------+
///
/// The two doors are an interlock: only one can be open at a time. At a keypad, E and the code (<see cref="AirlockCode"/>,
/// typed on <see cref="AirlockKeypad"/>) opens its door; if the other door is open, that one shuts first (the airlock
/// cycles) and this one opens once it's shut. E at an open door's keypad shuts it, no code (not with you standing in
/// the doorway). A dome camera (<see cref="VaultCameraMount"/>, <see cref="SecurityCamera.Vault"/>) watches the room.
/// </summary>
public sealed partial class Level
{
    // Plan (relative to the police origin): the corridor runs north from the outer door (the bullpen's north wall) to
    // the vault's south wall; the vault lies north of that, the portal in its north wall on the corridor's line.
    private const float VaultDoorX = -6.3f;                 // both doors and the portal are on this line
    private const float VaultDoorHalf = 0.6f;
    private const float VaultDoorTop = 2.2f;
    private const float VaultCorrHalf = 1.0f;               // corridor: x -7.3 .. -5.3 inside
    private const float VaultCorrCeiling = 2.6f;
    private const float VaultCorrStartZ = -BullpenHalfZ - PoliceWall;   // the north wall's back face (z = -7.2)
    private const float VaultSouthWallZ = -37.2f;           // the corridor's end: the vault's south wall, 0.4 m thick
    private const float VaultWallT = 0.4f;
    private const float VaultMinX = -14.3f, VaultMaxX = 1.7f;
    private const float VaultMinZ = -53.6f;                 // the portal wall's face (1 m of concrete behind it)
    private const float VaultMaxZ = VaultSouthWallZ - VaultWallT;
    private const float VaultHeight = 5f;

    private static readonly float3 VaultConcrete = new(0.74f, 0.74f, 0.72f);
    private static readonly float3 VaultFloorTint = new(0.46f, 0.46f, 0.45f);
    private static readonly float3 CorridorBlock = new(0.70f, 0.71f, 0.70f);
    private static readonly float3 AirlockSteel = new(0.42f, 0.45f, 0.48f);

    /// <summary>The gold portal: in the vault's north wall, facing back into it (+z), at the end of the corridor's line.</summary>
    public static float3 PoliceVaultPortalFloorPoint => PAt(VaultDoorX, 0, VaultMinZ);
    public static float3 PoliceVaultPortalNormal => new(0, 0, 1);
    public static readonly float3 PoliceVaultPortalColor = new(1.0f, 0.74f, 0.2f);

    // The vault PC's desk, against the far (east) wall of the east observation room: its front edge and its middle.
    private const float VaultPcDeskX = ObsEastMaxX - 0.75f, VaultPcDeskZ = -34.6f;

    /// <summary>Where the player can use the vault's PC (the floor in front of its desk, in the east observation room).</summary>
    public static Aabb PoliceVaultPcZone => new(PAt(VaultPcDeskX - 1.55f, -0.5f, VaultPcDeskZ - 1.1f), PAt(VaultPcDeskX, 3f, VaultPcDeskZ + 1.1f));

    /// <summary>The vault's dome camera (CAM 02): in the ceiling's south-east corner, over the inner door, looking across at the portal.</summary>
    public static float3 VaultCameraMount => PAt(VaultMaxX - 0.255f, VaultHeight, VaultMaxZ - 0.255f);

    public static Aabb PoliceVaultCorridor => new(PAt(VaultDoorX - VaultCorrHalf, 0, VaultSouthWallZ), PAt(VaultDoorX + VaultCorrHalf, VaultCorrCeiling, VaultCorrStartZ));
    public static Aabb PoliceVault => new(PAt(VaultMinX, 0, VaultMinZ), PAt(VaultMaxX, VaultHeight, VaultMaxZ));

    // The corridor's strip lights (every 5 m down its middle) and the vault's six high-bay lamps, each one a light.
    private static readonly float[] CorridorLightZ = [-9.7f, -14.7f, -19.7f, -24.7f, -29.7f, -34.7f];
    private static readonly (float X, float Z)[] VaultLamps = [(-10.3f, -42.5f), (-6.3f, -42.5f), (-2.3f, -42.5f), (-10.3f, -49f), (-6.3f, -49f), (-2.3f, -49f)];
    private const float VaultLampDrop = 0.85f;   // the lamps hang this far below the soffit on their rods

    /// <summary>The corridor's strip lights, the vault's high-bay lamps and the gold portal's glow.</summary>
    private static IEnumerable<OfficeLight> VaultLights
    {
        get
        {
            var corridor = Grow(PoliceVaultCorridor);
            foreach (var z in CorridorLightZ)
                yield return new OfficeLight(PAt(VaultDoorX, VaultCorrCeiling - 0.07f, z), new float3(0.94f, 0.97f, 1.0f), 2.6f, 6.5f, corridor);
            var vault = Grow(PoliceVault);
            foreach (var (x, z) in VaultLamps)
                yield return new OfficeLight(PAt(x, VaultHeight - VaultLampDrop - 0.3f, z), new float3(1.0f, 0.95f, 0.86f), 9f, 11f, vault);
            var portal = PoliceVaultPortalFloorPoint + new float3(0, PortalSystem.CenterHeight, 0) + PoliceVaultPortalNormal * 0.7f;
            yield return new OfficeLight(portal, PoliceVaultPortalColor, 0.6f, 6f, vault, Panel: false);
            foreach (var light in ObservationLights)
                yield return light;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // The airlock: two sliding doors, four keypads.
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// A keypad by a door: where it is on its wall, the door it opens, the airlock door it's interlocked with (if any),
    /// whether opening takes the code, the name on its display, and its status lights.
    /// </summary>
    private sealed record Keypad(float3 Position, float3 Normal, PrisonDoor Door, PrisonDoor? Other, bool NeedsCode, string Label, Aabb Led, Aabb Lamp);

    private PrisonDoor? _airlockOuter, _airlockInner;
    private PrisonDoor? _airlockPending;   // the door waiting to open once the other one has shut
    private readonly List<Keypad> _keypads = [];
    private float _keypadDenied;            // seconds left of a keypad's "blocked" flash
    private Keypad? _deniedAt;              // (the keypad refused: its door's keypads, and its interlocked door's, flash)
    private readonly Dictionary<PrisonDoor, Aabb> _doorways = [];   // each keypad door's doorway, with room for someone in it
    private float _airlockTime;

    /// <summary>"[E] Open door" and the like while the player is at one of the keypads, otherwise null.</summary>
    public string? AirlockPrompt { get; private set; }

    /// <summary>The code for the airlock and the observation rooms: open (or cycle to) a door with it. Shutting an open door needs no code.</summary>
    public const string AirlockCode = "1234";

    /// <summary>
    /// For now the keypad doors (the airlock's and the observation rooms') stand open and the keypads do nothing; set
    /// this false to bring them back.
    /// </summary>
    private const bool KeypadDoorsHeldOpen = true;

    private Keypad? _keypadAt;   // the keypad the player is standing at, if any
    private string _deniedWhy = "";

    /// <summary>True while the player stands at one of the keypads.</summary>
    public bool AirlockKeypadInReach => _keypadAt is not null;

    /// <summary>The name on the display of the keypad the player is at.</summary>
    public string KeypadLabel => _keypadAt?.Label ?? "AIRLOCK";

    /// <summary>True while the player's keypad's door is moving (or, for the airlock's, either door is or it's cycling): it takes nothing then.</summary>
    public bool AirlockBusy => _keypadAt is { } k && Busy(k);

    private bool Busy(Keypad k) =>
        k.Other is null ? k.Door.T < 1f : _airlockPending is not null || _airlockOuter?.T < 1f || _airlockInner?.T < 1f;

    /// <summary>True if the keypad the player is at would open its door (or cycle to it), and that takes the code.</summary>
    public bool AirlockNeedsCode => _keypadAt is { } k && k.NeedsCode && k.Door.Target < 0.5f;

    /// <summary>Moves the keypad doors on, and works out which keypad (if any) the player is at and looking towards.</summary>
    public void UpdateAirlock(float dt, float3 player, float3 look)
    {
        if (_airlockOuter is not { } outer || _airlockInner is not { } inner)
            return;
        if (KeypadDoorsHeldOpen)
        {
            foreach (var door in (ReadOnlySpan<PrisonDoor>)[outer, inner, .. _obsDoors])
            {
                if (door.Open < 1f)
                {
                    door.Open = door.Target = door.T = 1f;
                    UpdateDoorColliders(door);
                }
            }
            _keypadAt = null;
            AirlockPrompt = null;
            return;
        }
        _airlockTime += dt;
        _keypadDenied = float.Max(0f, _keypadDenied - dt);
        StepDoor(outer, dt);
        StepDoor(inner, dt);
        foreach (var door in _obsDoors)
            StepDoor(door, dt);
        if (_airlockPending is { } pending)
        {
            var other = pending == outer ? inner : outer;
            if (other.Open <= 0f && other.T >= 1f)
            {
                _airlockPending = null;
                MoveDoor(pending, true);
                Log.Write($"[DOOR] Airlock: opening the {pending.Name} door");
            }
        }

        // The keypad in reach that the player's looking most towards (two can be in reach across the corridor).
        _keypadAt = null;
        var best = float.MinValue;
        var lookFlat = float2.Normalize(new float2(look.X, look.Z) + new float2(1e-5f, 0));
        foreach (var k in _keypads)
        {
            var d = player - k.Position;
            var front = float3.Dot(d, k.Normal);
            if (front <= 0.05f || front >= 1.4f || float.Abs(d.Y + 1.3f) >= 1f || new float2(d.X, d.Z).Length() >= 1.5f)
                continue;
            var score = float2.Dot(lookFlat, float2.Normalize(-new float2(d.X, d.Z)));
            if (score > best)
            {
                best = score;
                _keypadAt = k;
            }
        }
        AirlockPrompt = _keypadAt is not { } at ? null
            : _keypadDenied > 0f ? _deniedWhy
            : Busy(at) ? (at.Other is null ? "Door moving..." : "Airlock cycling...")
            : at.Door.Target > 0.5f ? "[E] Keypad: close door"
            : at.NeedsCode ? "[E] Keypad: enter code"
            : "[E] Keypad: open door";
    }

    /// <summary>
    /// The keypad the player is at has been used (with the code, if its door is shut and needs it): its door opens or
    /// shuts; at the airlock, if the other door is open, that one shuts first and this one opens once it's shut.
    /// </summary>
    public void UseAirlockKeypad(float3 player)
    {
        if (_keypadAt is not { } at || AirlockBusy)
            return;
        var door = at.Door;
        if (door.Target > 0.5f)
        {
            // Shut this door (not on whoever's standing in it).
            if (Inside(_doorways[door], player))
                DenyAirlockKeypad("doorway blocked");
            else if (MoveDoor(door, false))
                Log.Write($"[DOOR] Keypad: shutting the {door.Name} door");
        }
        else if (at.Other is { } other && (other.Target > 0.5f || other.Open > 0f))
        {
            // The other door's open: shut it first, then this one opens (see UpdateAirlock).
            if (Inside(_doorways[other], player))
                DenyAirlockKeypad("doorway blocked");
            else
            {
                MoveDoor(other, false);
                _airlockPending = door;
                Log.Write($"[DOOR] Airlock: cycling (shutting the {other.Name} door first)");
            }
        }
        else if (MoveDoor(door, true))
        {
            Log.Write($"[DOOR] Keypad: opening the {door.Name} door");
        }
    }

    /// <summary>Flashes the keypad's lights red (a wrong code, or someone in the doorway).</summary>
    public void DenyAirlockKeypad(string why)
    {
        _keypadDenied = 1.2f;
        _deniedAt = _keypadAt;
        _deniedWhy = char.ToUpperInvariant(why[0]) + why[1..];
        Log.Write($"[DOOR] Keypad: {why}");
    }

    private static bool Inside(in Aabb box, float3 p) =>
        p.X >= box.Min.X && p.X <= box.Max.X && p.Y >= box.Min.Y && p.Y <= box.Max.Y && p.Z >= box.Min.Z && p.Z <= box.Max.Z;

    /// <summary>Opens or shuts an airlock door at once ("outer" or "inner"), for test switches. The interlock still holds.</summary>
    public void SetAirlockDoor(string which, bool open)
    {
        if (_airlockOuter is not { } outer || _airlockInner is not { } inner)
            return;
        var door = which == "inner" ? inner : outer;
        if (open)
            MoveDoor(door == outer ? inner : outer, false, instant: true);
        MoveDoor(door, open, instant: true);
    }

    private void DrawAirlock(Renderer renderer)
    {
        if (_airlockOuter is null || _airlockInner is null)
            return;
        DrawDoor(renderer, _airlockOuter);
        DrawDoor(renderer, _airlockInner);
        foreach (var door in _obsDoors)
            DrawDoor(renderer, door);
        // Status lights: green over an open door, amber blinking while it moves (or the airlock cycles), red while it's
        // shut (and a red flash on the refusing keypad's door, and the door it's interlocked with).
        var blink = (int)(_airlockTime * 4f) % 2 == 0;
        foreach (var k in _keypads)
        {
            var moving = k.Door.T < 1f || _airlockPending == k.Door;
            var denied = _keypadDenied > 0f && _deniedAt is { } d && (k.Door == d.Door || k.Door == d.Other);
            var color = denied && blink ? new float3(1.0f, 0.08f, 0.05f)
                : moving ? (blink ? new float3(1.0f, 0.6f, 0.08f) : new float3(0.2f, 0.12f, 0.02f))
                : k.Door.Open >= 1f ? new float3(0.15f, 1.0f, 0.3f)
                : new float3(1.0f, 0.1f, 0.06f);
            renderer.DrawBox(k.Led, color, ShadeMode.Lit, Material.Emissive);
            renderer.DrawBox(k.Lamp, color, ShadeMode.Lit, Material.Emissive);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Building it.
    // ------------------------------------------------------------------------------------------------

    private void BuildPoliceVault()
    {
        BuildVaultCorridor();
        BuildVaultRoom();
        BuildAirlockDoors();
        BuildObservationRooms();
        BuildVaultPc();
        // The security camera's housing (its ball is drawn by SecurityCamera.Vault, its feed is in the tech room).
        if (TryModel("cctv_dome") is { } dome)
            AddModel(dome, float4x4.CreateRotationY(-float.Pi / 4) * float4x4.CreateTranslation(VaultCameraMount), float3.One);
    }

    private void BuildVaultCorridor()
    {
        const float x0 = VaultDoorX - VaultCorrHalf, x1 = VaultDoorX + VaultCorrHalf, t = PoliceWall;
        const float z0 = VaultSouthWallZ, z1 = VaultCorrStartZ;

        // The doorway through the bullpen's north wall, with a steel threshold and steel jamb linings.
        CarveOpening(new Aabb(PAt(VaultDoorX - VaultDoorHalf, 0f, z1 - 0.05f), PAt(VaultDoorX + VaultDoorHalf, VaultDoorTop, -BullpenHalfZ + 0.05f)));
        AddMinMax(PAt(VaultDoorX - VaultDoorHalf, -0.3f, z1), PAt(VaultDoorX + VaultDoorHalf, 0f, -BullpenHalfZ), Aluminium, Material.Steel);
        var lining = PB(Material.Steel, AirlockSteel * 0.8f, occludes: false);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            OBox(lining, float4x4.Identity, PAt(VaultDoorX + side * (VaultDoorHalf - 0.005f), VaultDoorTop / 2, z1 + t / 2), new float3(0.01f, VaultDoorTop, t));
        OBox(lining, float4x4.Identity, PAt(VaultDoorX, VaultDoorTop - 0.005f, z1 + t / 2), new float3(VaultDoorHalf * 2, 0.01f, t));

        // Floor, painted blockwork walls, a concrete soffit.
        AddMinMax(PAt(x0, -0.3f, z0), PAt(x1, 0, z1), new float3(0.48f, 0.48f, 0.47f), Material.Floor);
        PoliceCorridorWall(x0 - t, x0);
        PoliceCorridorWall(x1, x1 + t);
        AddMinMax(PAt(x0 - t, VaultCorrCeiling, z0), PAt(x1 + t, VaultCorrCeiling + 0.2f, z1), new float3(0.62f, 0.62f, 0.6f), Material.Concrete);
        void PoliceCorridorWall(float a, float b) => AddMinMax(PAt(a, 0, z0), PAt(b, VaultCorrCeiling + 0.2f, z1), CorridorBlock, Material.Blockwork);

        // A dark kick band along both walls, and a conduit and cable tray along the east side of the ceiling.
        var band = PB(Material.Plain, new float3(0.24f, 0.25f, 0.26f), occludes: false);
        foreach (var (x, nx) in (ReadOnlySpan<(float, float)>)[(x0, 1f), (x1, -1f)])
            foreach (var (za, zb) in (ReadOnlySpan<(float, float)>)[(z0, ObsDoorZ - ObsDoorHalf), (ObsDoorZ + ObsDoorHalf, z1)])   // (not across the observation rooms' doors)
                OBox(band, float4x4.Identity, PAt(x + nx * 0.006f, 0.075f, (za + zb) / 2), new float3(0.012f, 0.15f, zb - za));
        var conduit = PB(Material.Steel, new float3(0.55f, 0.56f, 0.57f), occludes: false);
        OCyl(conduit, float4x4.Identity, PAt(x1 - 0.12f, VaultCorrCeiling - 0.08f, z0), PAt(x1 - 0.12f, VaultCorrCeiling - 0.08f, z1), 0.025f, 0.025f, 8);
        OBox(conduit, float4x4.Identity, PAt(x1 - 0.32f, VaultCorrCeiling - 0.06f, (z0 + z1) / 2), new float3(0.22f, 0.03f, z1 - z0));
        for (var z = z0 + 1.5f; z < z1; z += 3f)
            OBox(conduit, float4x4.Identity, PAt(x1 - 0.22f, VaultCorrCeiling - 0.04f, z), new float3(0.44f, 0.08f, 0.03f));   // brackets

        // Strip lights down the middle every 5 m.
        var housing = PB(Material.Plain, new float3(0.86f, 0.86f, 0.85f), occludes: false);
        var tube = PB(Material.Emissive, PanelLight, occludes: false);
        foreach (var z in CorridorLightZ)
        {
            OBox(housing, float4x4.Identity, PAt(VaultDoorX, VaultCorrCeiling - 0.03f, z), new float3(0.2f, 0.06f, 1.5f));
            OBox(tube, float4x4.Identity, PAt(VaultDoorX, VaultCorrCeiling - 0.062f, z), new float3(0.15f, 0.004f, 1.42f));
        }

        // Hazard stripes on the floor in front of both doors, inside the corridor.
        var yellow = PB(Material.Plain, new float3(0.85f, 0.65f, 0.08f), occludes: false);
        var black = PB(Material.Plain, new float3(0.05f, 0.05f, 0.05f), occludes: false);
        foreach (var z in (ReadOnlySpan<float>)[z1 - 0.45f, z0 + 0.45f])
        {
            OBox(yellow, float4x4.Identity, PAt(VaultDoorX, 0.002f, z), new float3(VaultCorrHalf * 2 - 0.1f, 0.004f, 0.3f));
            for (var x = x0 + 0.15f; x < x1 - 0.1f; x += 0.25f)
                OBox(black, float4x4.Identity, PAt(x, 0.0045f, z), new float3(0.09f, 0.002f, 0.3f), float4x4.CreateRotationY(0.6f));
        }

        // Signs: RESTRICTED over the outer door (bullpen side), and the airlock's rule inside the corridor.
        var north = new float3(0, 0, 1);
        OBox(PB(Material.Plain, new float3(0.62f, 0.07f, 0.06f), occludes: false), WallFrame(PAt(VaultDoorX, 0, -BullpenHalfZ), north),
             new float3(0, VaultDoorTop + 0.36f, 0.008f), new float3(1.3f, 0.17f, 0.016f));
        AddLettering("RESTRICTED", PAt(VaultDoorX, VaultDoorTop + 0.36f, -BullpenHalfZ + 0.016f), north, 0.09f, Material.Plain, new float3(0.95f, 0.95f, 0.93f), 0.004f);
        var rule = WallFrame(PAt(x0, 1.6f, z1 - 1.6f), new float3(1, 0, 0));
        OBox(PB(Material.Plain, new float3(0.9f, 0.75f, 0.1f), occludes: false), rule, new float3(0, 0, 0.004f), new float3(0.7f, 0.3f, 0.008f));
        AddLettering("AIRLOCK", PAt(x0 + 0.009f, 1.66f, z1 - 1.6f), new float3(1, 0, 0), 0.06f, Material.Plain, new float3(0.05f, 0.05f, 0.05f), 0.002f);
        AddLettering("ONE DOOR AT A TIME", PAt(x0 + 0.009f, 1.54f, z1 - 1.6f), new float3(1, 0, 0), 0.035f, Material.Plain, new float3(0.05f, 0.05f, 0.05f), 0.002f);
    }

    private void BuildVaultRoom()
    {
        const float t = VaultWallT, h = VaultHeight;
        // Floor (running on under the inner door), raw concrete walls and soffit; a metre of it behind the portal.
        AddMinMax(PAt(VaultMinX - t, -0.3f, VaultMinZ - 1f), PAt(VaultMaxX + t, 0, VaultSouthWallZ), VaultFloorTint, Material.Floor);
        AddMinMax(PAt(VaultMinX - t, 0, VaultMaxZ), PAt(VaultMaxX + t, h, VaultSouthWallZ), VaultConcrete, Material.Concrete);
        AddMinMax(PAt(VaultMinX - t, 0, VaultMinZ - 1f), PAt(VaultMinX, h, VaultMaxZ), VaultConcrete, Material.Concrete);
        AddMinMax(PAt(VaultMaxX, 0, VaultMinZ - 1f), PAt(VaultMaxX + t, h, VaultMaxZ), VaultConcrete, Material.Concrete);
        AddMinMax(PAt(VaultMinX, 0, VaultMinZ - 1f), PAt(VaultMaxX, h, VaultMinZ), VaultConcrete, Material.Concrete);
        AddMinMax(PAt(VaultMinX - t, h, VaultMinZ - 1f), PAt(VaultMaxX + t, h + t, VaultSouthWallZ), VaultConcrete * 0.92f, Material.Concrete);

        // The inner doorway, its threshold and linings.
        CarveOpening(new Aabb(PAt(VaultDoorX - VaultDoorHalf, 0f, VaultMaxZ - 0.05f), PAt(VaultDoorX + VaultDoorHalf, VaultDoorTop, VaultSouthWallZ + 0.05f)));
        var lining = PB(Material.Steel, AirlockSteel * 0.8f, occludes: false);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            OBox(lining, float4x4.Identity, PAt(VaultDoorX + side * (VaultDoorHalf - 0.005f), VaultDoorTop / 2, VaultMaxZ + t / 2), new float3(0.01f, VaultDoorTop, t));
        OBox(lining, float4x4.Identity, PAt(VaultDoorX, VaultDoorTop - 0.005f, VaultMaxZ + t / 2), new float3(VaultDoorHalf * 2, 0.01f, t));
        OBox(PB(Material.Steel, Aluminium, occludes: false), float4x4.Identity, PAt(VaultDoorX, 0.002f, VaultMaxZ + t / 2), new float3(VaultDoorHalf * 2, 0.004f, t));

        // Six high-bay lamps hanging on rods from the soffit.
        var rod = PB(Material.Steel, new float3(0.3f, 0.3f, 0.31f), occludes: false);
        var shade = PB(Material.PaintedMetal, new float3(0.32f, 0.36f, 0.34f), occludes: false);
        var bulb = PB(Material.Emissive, new float3(1.0f, 0.95f, 0.85f), occludes: false);
        foreach (var (x, z) in VaultLamps)
            {
                var top = PAt(x, h, z);
                var hang = PAt(x, h - VaultLampDrop, z);
                OCyl(rod, float4x4.Identity, top, hang, 0.012f, 0.012f, 6);
                OCyl(shade, float4x4.Identity, hang, hang - new float3(0, 0.12f, 0), 0.08f, 0.12f, 14);
                OCyl(shade, float4x4.Identity, hang - new float3(0, 0.12f, 0), hang - new float3(0, 0.3f, 0), 0.12f, 0.3f, 18);
                OCyl(bulb, float4x4.Identity, hang - new float3(0, 0.28f, 0), hang - new float3(0, 0.29f, 0), 0.27f, 0.27f, 18);
            }

        // Formwork tie holes in rows over the walls (raw cast concrete), and a yellow keep-clear line round the portal.
        var tie = PB(Material.Plain, new float3(0.22f, 0.22f, 0.22f), occludes: false);
        for (var y = 0.6f; y < h; y += 1.2f)
        {
            for (var x = VaultMinX + 0.6f; x < VaultMaxX; x += 1.2f)
            {
                if (float.Abs(x - VaultDoorX) > 2.2f)   // (not round the portal's ring)
                    OCyl(tie, float4x4.Identity, PAt(x, y, VaultMinZ), PAt(x, y, VaultMinZ + 0.004f), 0.018f, 0.018f, 8);
                if ((float.Abs(x - VaultDoorX) > 1f || y > VaultDoorTop + 0.2f) && !InObsWindow(x, y))
                    OCyl(tie, float4x4.Identity, PAt(x, y, VaultMaxZ), PAt(x, y, VaultMaxZ - 0.004f), 0.018f, 0.018f, 8);
            }
            for (var z = VaultMinZ + 0.6f; z < VaultMaxZ; z += 1.2f)
            {
                OCyl(tie, float4x4.Identity, PAt(VaultMinX, y, z), PAt(VaultMinX + 0.004f, y, z), 0.018f, 0.018f, 8);
                OCyl(tie, float4x4.Identity, PAt(VaultMaxX, y, z), PAt(VaultMaxX - 0.004f, y, z), 0.018f, 0.018f, 8);
            }
        }
        var line = PB(Material.Plain, new float3(0.85f, 0.65f, 0.08f), occludes: false);
        const float lineZ = VaultMinZ + 2.6f, lx0 = VaultDoorX - 2.6f, lx1 = VaultDoorX + 2.6f;
        OBox(line, float4x4.Identity, PAt(VaultDoorX, 0.002f, lineZ), new float3(lx1 - lx0 + 0.1f, 0.004f, 0.1f));
        foreach (var x in (ReadOnlySpan<float>)[lx0, lx1])
            OBox(line, float4x4.Identity, PAt(x, 0.002f, (VaultMinZ + lineZ) / 2), new float3(0.1f, 0.004f, lineZ - VaultMinZ));
    }

    private void BuildAirlockDoors()
    {
        // Each door: a heavy steel leaf on the far side of the doorway from the corridor, sliding west along the wall
        // face on a track. Outer: on the bullpen's side of its north wall; inner: on the vault's side of its south wall.
        const float w = VaultDoorHalf * 2 + 0.1f, h = VaultDoorTop + 0.05f, travel = w + 0.02f;
        PrisonDoor Leaf(string name, float faceZ, float side)
        {
            var zc = faceZ + side * 0.06f;   // (clear of the wall face)
            var leaf = SlidingLeaf(new float3(-travel, 0, 0), PAt(VaultDoorX, h / 2, zc), new float3(w, h, 0.06f));
            var id = float4x4.Identity;
            OBox(leaf.B(Material.Plain, AirlockSteel), id, PAt(VaultDoorX, h / 2, zc), new float3(w, h, 0.06f));
            // A heavier frame round the leaf, a wired-glass vision panel, and on both faces a pull bar and a yellow kick band.
            var trim = leaf.B(Material.Steel, AirlockSteel * 0.6f);
            OBox(trim, id, PAt(VaultDoorX, h - 0.03f, zc), new float3(w, 0.06f, 0.075f));
            OBox(trim, id, PAt(VaultDoorX, 0.03f, zc), new float3(w, 0.06f, 0.075f));
            OBox(leaf.B(Material.Glass, new float3(0.12f, 0.14f, 0.16f)), id, PAt(VaultDoorX, 1.6f, zc), new float3(0.22f, 0.5f, 0.07f));
            foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
            {
                OBox(trim, id, PAt(VaultDoorX + s * (w / 2 - 0.03f), h / 2, zc), new float3(0.06f, h, 0.075f));
                OBox(leaf.B(Material.Steel, Aluminium), id, PAt(VaultDoorX + 0.42f, 1.05f, zc + s * 0.06f), new float3(0.03f, 0.45f, 0.03f));
                OBox(leaf.B(Material.Plain, new float3(0.85f, 0.65f, 0.08f)), id, PAt(VaultDoorX, 0.2f, zc + s * 0.031f), new float3(w - 0.14f, 0.12f, 0.002f));
            }
            // The track over the doorway and on along the wall (the leaf parks on it, west of the opening).
            OBox(PB(Material.Steel, new float3(0.3f, 0.31f, 0.33f), occludes: false), id, PAt(VaultDoorX - travel / 2, h + 0.08f, zc), new float3(w + travel + 0.1f, 0.14f, 0.12f));
            var door = NewDoor(name, 1.8f, leaf);
            MoveDoor(door, false, instant: true);
            return door;
        }
        _airlockOuter = Leaf("vault outer", -BullpenHalfZ, 1f);
        _airlockInner = Leaf("vault inner", VaultMaxZ, -1f);
        _doorways[_airlockOuter] = new Aabb(PAt(VaultDoorX - VaultDoorHalf - 0.1f, -0.5f, VaultCorrStartZ - 0.45f), PAt(VaultDoorX + VaultDoorHalf + 0.1f, 2.5f, -BullpenHalfZ + 0.4f));
        _doorways[_airlockInner] = new Aabb(PAt(VaultDoorX - VaultDoorHalf - 0.1f, -0.5f, VaultMaxZ - 0.45f), PAt(VaultDoorX + VaultDoorHalf + 0.1f, 2.5f, VaultSouthWallZ + 0.45f));

        // Keypads: one on each side of each door, beside the opening (east of it: the leaves slide west).
        AddKeypad(PAt(VaultDoorX + VaultDoorHalf + 0.35f, 1.3f, -BullpenHalfZ), new float3(0, 0, 1), _airlockOuter, _airlockInner);
        AddKeypad(PAt(VaultDoorX + VaultDoorHalf + 0.2f, 1.3f, VaultCorrStartZ), new float3(0, 0, -1), _airlockOuter, _airlockInner);
        AddKeypad(PAt(VaultDoorX + VaultDoorHalf + 0.2f, 1.3f, VaultSouthWallZ), new float3(0, 0, 1), _airlockInner, _airlockOuter);
        AddKeypad(PAt(VaultDoorX + VaultDoorHalf + 0.35f, 1.3f, VaultMaxZ), new float3(0, 0, -1), _airlockInner, _airlockOuter);
    }

    /// <summary>A keypad on a wall at <paramref name="at"/> (its centre on the wall face), facing <paramref name="normal"/>.</summary>
    private void AddKeypad(float3 at, float3 normal, PrisonDoor door, PrisonDoor? other, bool needsCode = true, string label = "AIRLOCK")
    {
        var xf = WallFrame(at, normal);
        OBox(PB(Material.Steel, new float3(0.5f, 0.51f, 0.52f), occludes: false), xf, new float3(0, 0, 0.012f), new float3(0.11f, 0.18f, 0.024f));
        OBox(PB(Material.Emissive, new float3(0.1f, 0.25f, 0.18f), occludes: false), xf, new float3(0, 0.055f, 0.0245f), new float3(0.07f, 0.025f, 0.002f));
        var key = PB(Material.Polymer, new float3(0.12f, 0.12f, 0.13f), occludes: false);
        for (var row = 0; row < 4; row++)
            for (var col = -1; col <= 1; col++)
                OBox(key, xf, new float3(col * 0.025f, 0.018f - row * 0.022f, 0.027f), new float3(0.018f, 0.016f, 0.006f));
        // Its status LED (top right) and a beacon over it, drawn each frame in the door's state (see DrawAirlock).
        var led = float3.Transform(new float3(0.04f, 0.075f, 0.026f), xf);
        var lamp = float3.Transform(new float3(0, 0.95f, 0.03f), xf);
        OBox(PB(Material.Steel, new float3(0.3f, 0.31f, 0.33f), occludes: false), xf, new float3(0, 0.95f, 0.012f), new float3(0.12f, 0.12f, 0.024f));
        _keypads.Add(new Keypad(at, normal, door, other, needsCode, label, Aabb.FromCenterSize(led, new float3(0.008f)), Aabb.FromCenterSize(lamp, new float3(0.07f, 0.07f, 0.07f))));
    }

    /// <summary>The vault's PC: a steel table against the east observation room's far wall, wired through the ceiling to the ring.</summary>
    private void BuildVaultPc()
    {
        var desk = Frame(PAt(VaultPcDeskX, 0, VaultPcDeskZ), -float.Pi / 2);   // (local +z: west, into the room)
        var top = PB(Material.Steel, new float3(0.5f, 0.51f, 0.5f));
        var legs = PB(Material.Plain, new float3(0.22f, 0.22f, 0.23f));
        OBox(top, desk, new float3(0, 0.725f, -0.375f), new float3(1.6f, 0.03f, 0.75f));
        foreach (var lx in (ReadOnlySpan<float>)[-0.76f, 0.76f])
        {
            OBox(legs, desk, new float3(lx, 0.355f, -0.06f), new float3(0.05f, 0.71f, 0.05f));
            OBox(legs, desk, new float3(lx, 0.355f, -0.69f), new float3(0.05f, 0.71f, 0.05f));
            OBox(legs, desk, new float3(lx, 0.12f, -0.375f), new float3(0.04f, 0.04f, 0.65f));
        }
        FrameCollider(desk, new float3(0, 0.37f, -0.375f), new float3(1.6f, 0.76f, 0.75f));
        AddOfficeChair(float4x4.CreateRotationY(-0.35f) * float4x4.CreateTranslation(-0.1f, 0, 0.5f) * desk, 0.15f);
        FrameCollider(float4x4.CreateTranslation(-0.1f, 0, 0.5f) * desk, new float3(0, 0.45f, 0), new float3(0.5f, 0.9f, 0.5f), blocksShots: false);

        // A tower under the north end, a monitor (the portal's ring on it, offline grey until something links it), a keyboard.
        var plastic = PB(Material.Polymer, OfficeBlack);
        var tower = new float3(-0.55f, 0, -0.38f);
        OBox(plastic, desk, tower + new float3(0, 0.25f, 0), new float3(0.22f, 0.5f, 0.5f));
        OBox(PB(Material.Emissive, new float3(1.0f, 0.7f, 0.2f), occludes: false), desk, tower + new float3(0.05f, 0.47f, 0.251f), new float3(0.01f, 0.01f, 0.002f));
        var monitor = float4x4.CreateTranslation(0.1f, 0.74f, -0.5f) * desk;
        OBox(plastic, monitor, new float3(0, 0.008f, 0), new float3(0.26f, 0.016f, 0.2f));
        OBox(plastic, monitor, new float3(0, 0.17f, -0.04f), new float3(0.05f, 0.32f, 0.03f));
        OBox(plastic, monitor, new float3(0, 0.38f, 0), new float3(0.62f, 0.37f, 0.03f));
        OBox(PB(Material.Emissive, new float3(0.02f, 0.02f, 0.025f), occludes: false), monitor, new float3(0, 0.385f, 0.0155f), new float3(0.59f, 0.34f, 0.002f));
        var text = PB(Material.Emissive, new float3(0.75f, 0.75f, 0.75f), occludes: false);
        for (var i = 0; i < 7; i++)
            OBox(text, monitor, new float3(-0.24f + (i % 3) * 0.02f + 0.08f, 0.52f - i * 0.03f, 0.0168f), new float3(0.16f + (i * 37 % 5) * 0.03f, 0.01f, 0.001f));
        OBox(PB(Material.Emissive, PoliceVaultPortalColor, occludes: false), monitor, new float3(-0.2f, 0.28f, 0.0168f), new float3(0.09f, 0.012f, 0.001f));   // the prompt's cursor
        OBox(plastic, desk, new float3(0.1f, 0.752f, -0.22f), new float3(0.44f, 0.022f, 0.15f));
        OBox(plastic, desk, new float3(0.43f, 0.755f, -0.2f), new float3(0.06f, 0.03f, 0.1f));

        // Thick cables from the tower along the floor to a junction box under the window's wall, which conduits leave
        // through the ceiling; they come back down the portal wall to a second box by the ring, and into its rim.
        var cable = PB(Material.Polymer, new float3(0.06f, 0.06f, 0.065f), occludes: false);
        var conduit = PB(Material.Steel, new float3(0.55f, 0.56f, 0.57f), occludes: false);
        var pcBox = PAt(ObsEastMaxX - 0.33f, 0, VaultSouthWallZ + 0.1f);
        var ringBox = PAt(-4.3f, 0, VaultMinZ + 0.15f);
        foreach (var dz in (ReadOnlySpan<float>)[-0.5f, -0.42f, -0.34f])
        {
            var from = float3.Transform(new float3(-0.55f, 0.03f, dz), desk);
            var end = new float3(from.X, from.Y, pcBox.Z);
            OCyl(cable, float4x4.Identity, from, end, 0.022f, 0.022f, 8);
            OCyl(cable, float4x4.Identity, from, from + new float3(0, 0.2f, 0), 0.022f, 0.022f, 8);
            OCyl(cable, float4x4.Identity, end, end + new float3(0, 0.1f, 0), 0.022f, 0.022f, 8);
        }
        foreach (var (box, height) in (ReadOnlySpan<(float3, float)>)[(pcBox, ObsCeiling), (ringBox, VaultHeight)])
        {
            OBox(PB(Material.PaintedMetal, new float3(0.55f, 0.56f, 0.5f)), float4x4.Identity, box + new float3(0, 0.3f, 0), new float3(0.3f, 0.4f, 0.2f));
            foreach (var dx in (ReadOnlySpan<float>)[-0.07f, 0.07f])
                OCyl(conduit, float4x4.Identity, box + new float3(dx, 0.5f, 0), box + new float3(dx, height, 0), 0.025f, 0.025f, 8);
            AddStatic(Aabb.FromCenterSize(box + new float3(0, 0.3f, 0), new float3(0.32f, 0.6f, 0.22f)), default, visible: false);
        }
        foreach (var dy in (ReadOnlySpan<float>)[0.25f, 0.4f])
            OCyl(conduit, float4x4.Identity, ringBox + new float3(-0.15f, dy, 0), PAt(VaultDoorX + 1.62f, dy, VaultMinZ + 0.15f), 0.025f, 0.025f, 8);
    }
}
