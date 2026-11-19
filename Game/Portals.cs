using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// How a portal is set into its wall (or, freestanding, stands on its own).
/// </summary>
/// <param name="WallDepth">Thickness of the wall it's cut into (the doorway tunnel and corner plate go this deep).</param>
/// <param name="RingOuterRadius">Outer radius of the metal ring on the wall's face.</param>
/// <param name="RingFront">How far the ring stands out from the wall.</param>
/// <param name="PlateTint">Colour of the plate that fills the square hole's corners (matches the wall).</param>
/// <param name="PlateMaterial">Material of that plate.</param>
/// <param name="ChunkyRing">True for a heavy ring with a raised band; false for a slim rim.</param>
/// <param name="Freestanding">True for a ring standing on its own, not in a wall: <paramref name="WallDepth"/> is then how
/// far the ring reaches back from the water. From behind it's a window onto the same place (you see through it, shots
/// splash on its water) but you can't walk into it from that side.</param>
public readonly record struct PortalStyle(
    float WallDepth,
    float RingOuterRadius,
    float RingFront,
    float3 PlateTint,
    Material PlateMaterial,
    bool ChunkyRing,
    bool Freestanding = false);

/// <summary>
/// One end of a portal pair: a round doorway cut through a wall, filled with water and set in a metal ring.
/// Local space: origin at the centre of the circle, +X right, +Y up, +Z out of the wall (the side you walk in from).
/// </summary>
public sealed class Portal
{
    public string Name { get; }
    public PortalStyle Style { get; }
    public float3 Center { get; }
    public float3 Normal { get; }
    public float Radius { get; }
    public float3 Color { get; }
    /// <summary>The portal this one opens onto; null while it's offline (unlinked: the terminal at the police HQ took its partner).</summary>
    public Portal? Linked { get; internal set; }

    public bool Online => Linked is not null;

    /// <summary>
    /// Open onto nothing (the factory's, on a raid): its water and flashing lamps look like any live portal's, but there's only
    /// blackness beyond it. It's linked to nothing, so as far as the player, bullets and the people's own wits go it's offline:
    /// nobody can walk into it, and no PC can dial it. A script takes people through (<see cref="FactoryEvacuation"/>).
    /// </summary>
    public bool Dark { get; internal set; }

    /// <summary>When it was last connected (<see cref="PortalSystem.Time"/>): its warning lamps start flashing from then.</summary>
    public float OnlineSince { get; internal set; }

    /// <summary>Not drawn: it isn't there yet (the crimson portal, until the helicopter has landed its container).</summary>
    public bool Hidden { get; set; }

    /// <summary>The scene as seen through this portal (rendered from behind the linked portal).</summary>
    public RenderTarget View { get; } = new();

    /// <summary>
    /// The scene through this portal when it is itself seen through another portal (a second level of view), and
    /// whether it holds one for the view being rendered now.
    /// </summary>
    public RenderTarget InnerView { get; } = new();
    public bool InnerViewReady { get; set; }

    private const float RippleLifetime = 3f;
    private readonly List<float4> _ripples = []; // xy = hit point in portal space, z = start time, w = strength

    /// <summary>Active ripples on the water surface, oldest first.</summary>
    public ReadOnlySpan<float4> Ripples => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_ripples);

    /// <summary>Starts a ripple where <paramref name="worldPoint"/> touches the surface.</summary>
    public void AddRipple(float3 worldPoint, float time, float strength = 1f)
    {
        var local = float3.Transform(worldPoint, WorldToLocal);
        if (_ripples.Count >= Renderer.MaxRipples)
            _ripples.RemoveAt(0); // drop the oldest
        _ripples.Add(new float4(local.X, local.Y, time, strength));
    }

    public void RemoveExpiredRipples(float time) => _ripples.RemoveAll(r => time - r.Z > RippleLifetime);

    /// <summary>Portal-local space -> world.</summary>
    public float4x4 LocalToWorld { get; }
    public float4x4 WorldToLocal { get; }

    public Portal(string name, float3 center, float3 normal, float radius, float3 color, PortalStyle style)
    {
        Name = name;
        Style = style;
        Center = center;
        Normal = float3.Normalize(normal);
        Radius = radius;
        Color = color;

        // Portals stand upright, so up is always world up.
        var up = float3.UnitY;
        var right = float3.Normalize(float3.Cross(up, Normal));
        LocalToWorld = new float4x4(
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            Normal.X, Normal.Y, Normal.Z, 0,
            center.X, center.Y, center.Z, 1);
        float4x4.Invert(LocalToWorld, out var worldToLocal);
        WorldToLocal = worldToLocal;
    }

    /// <summary>
    /// Maps anything at this portal to the same place relative to the linked portal. Going in through the front
    /// of this portal comes out of the front of the linked one, so the local frame is turned 180 degrees about Y.
    /// </summary>
    /// <remarks>It's the floors that line up, not the circles' centres: see <see cref="Lift"/>.</remarks>
    public float4x4 ToLinked => WorldToLocal * float4x4.CreateRotationY(float.Pi)
        * float4x4.CreateTranslation(0, Lift - Linked!.Lift, 0) * Linked!.LocalToWorld;

    /// <summary>
    /// How much higher than usual the circle stands above its floor (<see cref="PortalSystem.CenterHeight"/> + this), so a
    /// low room doesn't hide its foot (the orange portal's container). Walking through still keeps your feet on the floor.
    /// </summary>
    public float Lift { get; init; }

    /// <summary>The floor under the circle's centre, in portal-local Y.</summary>
    public float LocalFloor => -(PortalSystem.CenterHeight + Lift);

    /// <summary>
    /// Its wall can be got behind (the orange portal's container has a narrow gap behind its closed end): while you're
    /// behind, its walk-through gap is closed off, as a freestanding portal's is (<see cref="PortalSystem.UpdateBackBlockers"/>).
    /// </summary>
    public bool BlockBehind { get; init; }

    /// <summary>
    /// The view mapping for an eye at <paramref name="eye"/>, and the portal the view looks out of. Through the front
    /// that's <see cref="ToLinked"/> out of the linked portal; from behind a freestanding portal it's a plain window:
    /// no mapping, looking out of this portal's own front at what's really on the far side.
    /// </summary>
    public float4x4 ViewThrough(float3 eye, out Portal exit)
    {
        if (Style.Freestanding && SignedDistance(eye) < 0)
        {
            exit = this;
            return float4x4.Identity;
        }
        exit = Linked!;
        return ToLinked;
    }

    /// <summary>
    /// The line of sight from <paramref name="eye"/> to <paramref name="at"/> by way of this doorway, as <see cref="ViewThrough"/>
    /// shows it: false if it doesn't pass through the doorway. It reaches the water at <paramref name="entry"/>, comes out at
    /// <paramref name="leaving"/>, and arrives going along <paramref name="along"/>, <paramref name="length"/> m from the eye in all.
    /// Only the doorway is tested, not what's in the way on either side.
    /// </summary>
    public bool LineThrough(float3 eye, float3 at, out float3 entry, out float3 leaving, out float3 along, out float length)
    {
        entry = leaving = along = float3.Zero;
        length = 0;
        if (!Online || (!Style.Freestanding && SignedDistance(eye) <= 0))
            return false;

        var map = ViewThrough(eye, out var exit);
        var from = float3.Transform(eye, map);
        var dFrom = exit.SignedDistance(from);
        var dTo = exit.SignedDistance(at);
        if (dFrom >= 0 || dTo <= 0)
            return false;

        leaving = from + (at - from) * (dFrom / (dFrom - dTo));
        if (!exit.InsideDoorway(leaving))
            return false;

        float4x4.Invert(map, out var back);
        entry = float3.Transform(leaving, back);
        var d = at - from;
        length = float.Sqrt(float3.Dot(d, d));
        along = d / length;
        return true;
    }

    /// <summary>Signed distance from the portal plane (positive in front).</summary>
    public float SignedDistance(float3 point) => float3.Dot(point - Center, Normal);

    /// <summary>Converts a box given in portal-local coordinates to a world-space box.</summary>
    public Aabb LocalBoxToWorld(float3 min, float3 max)
    {
        var a = float3.Transform(min, LocalToWorld);
        var b = float3.Transform(max, LocalToWorld);
        return new Aabb(float3.Min(a, b), float3.Max(a, b));
    }

    /// <summary>True if the segment passes from the front of the portal to the back, through the doorway.</summary>
    public bool IsCrossedBy(float3 from, float3 to)
    {
        var dFrom = SignedDistance(from);
        var dTo = SignedDistance(to);
        if (dFrom < 0 || dTo >= 0)
            return false;

        var hit = from + (to - from) * (dFrom / (dFrom - dTo));
        return InsideDoorway(hit);
    }

    /// <summary>Ray against the front face of the doorway (either face of a freestanding portal's).</summary>
    public bool Raycast(float3 origin, float3 direction, float maxDistance, out float distance)
    {
        distance = 0;
        var facing = float3.Dot(direction, Normal);
        if (facing >= 0 && !Style.Freestanding || float.Abs(facing) < 1e-6f)
            return false;

        distance = float3.Dot(Center - origin, Normal) / facing;
        return distance >= 0 && distance <= maxDistance && InsideDoorway(origin + direction * distance);
    }

    private bool InsideDoorway(float3 point)
    {
        var local = float3.Transform(point, WorldToLocal);
        return local.X * local.X + local.Y * local.Y <= Radius * Radius;
    }

    /// <summary>Ray against the front face of the ring frame (from behind a freestanding portal, its back face).</summary>
    public bool RaycastRing(float3 origin, float3 direction, float maxDistance, out float distance)
    {
        var outerRadius = Style.RingOuterRadius;
        var frontZ = Style.RingFront;
        distance = 0;
        var facing = float3.Dot(direction, Normal);
        if (facing >= 0 && Style.Freestanding && facing > 1e-6f)
            frontZ = -Style.WallDepth;
        else if (facing >= 0)
            return false;

        distance = float3.Dot(Center + Normal * frontZ - origin, Normal) / facing;
        if (distance < 0 || distance > maxDistance)
            return false;

        var local = float3.Transform(origin + direction * distance, WorldToLocal);
        var r2 = local.X * local.X + local.Y * local.Y;
        return r2 > Radius * Radius && r2 <= outerRadius * outerRadius;
    }
}

/// <summary>
/// A pair of linked portals: cuts their doorways into the level, renders the view through each one,
/// and moves the player across when they walk through.
/// The blue portal stands on its own on the warehouse's loading dock; the orange one is in the closed end of a shipping container
/// in the container yard. (The purple portal at the back of the yard is out of the game for now; see the constructor.)
/// </summary>
public sealed class PortalSystem : IDisposable
{
    // --- Shape (portal-local units, metres). Both ends must match so the teleport lines up. ---
    // Sized to fit inside a shipping container (about 2.35 m wide and 2.39 m tall inside).
    public const float PortalRadius = 1.15f;     // radius of the water surface
    public const float CenterHeight = 1.0f;      // circle centre above the floor; the bottom of the circle sinks below it
    private const float WalkHalfWidth = 0.55f;   // the player-passable gap: a rectangle that fits inside the circle
    private const float WalkTop = 0.9f;          // top of that gap, relative to the circle centre (1.9 m above the floor)

    private static readonly float3 RingColor = new(0.36f, 0.37f, 0.40f);
    private static readonly float3 BandColor = new(0.52f, 0.53f, 0.56f);

    /// <summary>The warehouse wall: thick concrete, a heavy ring.</summary>
    private static readonly PortalStyle WarehouseStyle = new(
        WallDepth: 1.0f, RingOuterRadius: 1.75f, RingFront: 0.18f,
        PlateTint: new float3(0.62f, 0.60f, 0.55f), PlateMaterial: Material.Concrete, ChunkyRing: true);

    /// <summary>The container end wall: thin steel, a slim rim that fits inside the container.</summary>
    private static readonly PortalStyle ContainerStyle = new(
        WallDepth: 0.1f, RingOuterRadius: 1.21f, RingFront: 0.05f,
        PlateTint: Level.ContainerInteriorTint, PlateMaterial: Material.Corrugated, ChunkyRing: false);

    /// <summary>The yard end of the second pair: notched into the side of a solid container stack.</summary>
    private static readonly PortalStyle YardStyle = new(
        WallDepth: 0.5f, RingOuterRadius: 1.65f, RingFront: 0.15f, // a notch deep enough to step into
        PlateTint: new float3(0.30f, 0.27f, 0.25f), PlateMaterial: Material.RustyMetal, ChunkyRing: true);

    /// <summary>The outback end: cut through the shack's thin tin wall, with a rim that fits under its roof.</summary>
    private static readonly PortalStyle OutbackStyle = new(
        WallDepth: 0.1f, RingOuterRadius: 1.45f, RingFront: 0.1f,
        PlateTint: new float3(0.55f, 0.55f, 0.53f), PlateMaterial: Material.Galvanized, ChunkyRing: false);

    /// <summary>The cellar's south wall: 0.3 m of concrete, a slim rim under its low ceiling.</summary>
    private static readonly PortalStyle CellarStyle = new(
        WallDepth: 0.3f, RingOuterRadius: 1.45f, RingFront: 0.1f,
        PlateTint: new float3(0.50f, 0.49f, 0.46f), PlateMaterial: Material.Concrete, ChunkyRing: false);

    /// <summary>The bunker: a metre of bare grey concrete, a heavy ring.</summary>
    private static readonly PortalStyle BunkerStyle = new(
        WallDepth: 1.0f, RingOuterRadius: 1.75f, RingFront: 0.18f,
        PlateTint: new float3(0.56f, 0.56f, 0.55f), PlateMaterial: Material.Concrete, ChunkyRing: true);

    /// <summary>The Arctic hut's timber wall.</summary>
    private static readonly PortalStyle ArcticStyle = new(
        WallDepth: 0.25f, RingOuterRadius: 1.5f, RingFront: 0.12f,
        PlateTint: new float3(0.40f, 0.33f, 0.26f), PlateMaterial: Material.Wood, ChunkyRing: false);

    /// <summary>The false wall at the closed end of the Arctic's second container: thin steel, a slim rim that fits inside it.</summary>
    private static readonly PortalStyle ArcticContainerStyle = new(
        WallDepth: Level.ArcticContainerFalseWall, RingOuterRadius: 1.17f, RingFront: 0.05f,
        PlateTint: Level.ContainerInteriorTint * 0.8f, PlateMaterial: Material.Corrugated, ChunkyRing: false);

    /// <summary>Rio: a rendered block wall at the dead end of an alley, painted, a slim rim.</summary>
    private static readonly PortalStyle RioStyle = new(
        WallDepth: Level.RioPortalWall, RingOuterRadius: 1.4f, RingFront: 0.08f,
        PlateTint: Level.RioPortalWallTint, PlateMaterial: Material.Blockwork, ChunkyRing: false);

    /// <summary>The ship's wheelhouse front: a painted steel bulkhead.</summary>
    private static readonly PortalStyle ShipStyle = new(
        WallDepth: 0.3f, RingOuterRadius: 1.45f, RingFront: 0.1f,
        PlateTint: new float3(0.84f, 0.84f, 0.82f), PlateMaterial: Material.MarinePaint, ChunkyRing: false);

    /// <summary>A plank casing built out from the jungle storage shack's wall.</summary>
    private static readonly PortalStyle JungleStyle = new(
        WallDepth: Level.JunglePortalDepth, RingOuterRadius: 1.4f, RingFront: 0.1f,
        PlateTint: new float3(0.44f, 0.35f, 0.24f), PlateMaterial: Material.Wood, ChunkyRing: false);

    /// <summary>The police station's tech room: a plasterboard partition, a slim ring.</summary>
    private static readonly PortalStyle PoliceStyle = new(
        WallDepth: 0.2f, RingOuterRadius: 1.45f, RingFront: 0.1f,
        PlateTint: new float3(0.72f, 0.73f, 0.72f), PlateMaterial: Material.WallPaint, ChunkyRing: false);

    /// <summary>The police vault: a metre of raw concrete, a heavy ring.</summary>
    private static readonly PortalStyle VaultStyle = new(
        WallDepth: 1.0f, RingOuterRadius: 1.75f, RingFront: 0.18f,
        PlateTint: new float3(0.74f, 0.74f, 0.72f), PlateMaterial: Material.Concrete, ChunkyRing: true);

    /// <summary>The undercroft at the flats: dark painted concrete, a slim ring.</summary>
    private static readonly PortalStyle FlatsStyle = new(
        WallDepth: Level.FlatsPortalWall, RingOuterRadius: 1.45f, RingFront: 0.1f,
        PlateTint: new float3(0.17f, 0.18f, 0.19f), PlateMaterial: Material.Concrete, ChunkyRing: false);

    /// <summary>
    /// Freestanding (the warehouse's loading dock): a heavy ring standing on its own, 0.3 m thick, sunk into the floor
    /// at its foot, with a raised band on both faces.
    /// </summary>
    private static readonly PortalStyle FreestandingStyle = new(
        WallDepth: 0.15f, RingOuterRadius: 1.75f, RingFront: 0.15f,
        PlateTint: default, PlateMaterial: Material.Steel, ChunkyRing: true, Freestanding: true);

    // (a freestanding portal has no plate, and its band and trim go on both faces: BackBand, BackTrim)
    // The warning lamp at the top of the ring, front and back: its housing and its red lens.
    private sealed record PortalMeshes(Mesh Body, Mesh? Band, Mesh Trim, Mesh? Plate, Mesh Lamp, Mesh Lens, Mesh BackLamp, Mesh BackLens,
        Mesh? BackBand = null, Mesh? BackTrim = null);

    // --- The warning lamps: a small red lamp at the top of each ring, on both faces; it flashes while the portal is connected. ---
    private const float LampLensRadius = 0.045f;
    private const float LampHousing = 0.13f;    // square housing round the lens
    private const float LampFlashPeriod = 0.8f; // s: on for the first half of each
    private static readonly float3 LampHousingColor = new(0.13f, 0.13f, 0.14f);
    private static readonly float3 LampOnColor = new(1.0f, 0.07f, 0.04f);
    private static readonly float3 LampDimColor = new(0.22f, 0.02f, 0.015f);   // connected, between flashes
    private static readonly float3 LampOffColor = new(0.28f, 0.04f, 0.035f);   // offline: an unlit red lens

    private readonly List<Portal> _portals = [];
    private readonly List<Portal> _named;        // (the PCs' portals by name: they're there even in a run without portals)
    private readonly Dictionary<Portal, PortalMeshes> _meshes = [];

    public List<Portal> Portals => _portals;

    /// <summary>Seconds since start; drives the water animation.</summary>
    public float Time { get; private set; }

    /// <summary>Total shots that have landed on a portal's water (shown in the HUD).</summary>
    public int SplashCount { get; private set; }

    private readonly Level _level;

    /// <param name="level">The level to cut the doorways into.</param>
    /// <param name="none">There are no portals in this run (the firing range on its own): they're neither built nor drawn.</param>
    /// <param name="factoryRaid">Only the factory's blue portal, open onto nothing (<see cref="Portal.Dark"/>), and the police HQ's two: the other levels aren't there.</param>
    public PortalSystem(Level level, bool none = false, bool factoryRaid = false)
    {
        _level = level;
        var blue = new Portal(
            "Blue portal", Level.WarehousePortalFloorPoint + new float3(0, CenterHeight, 0), Level.WarehousePortalNormal,
            PortalRadius, new float3(0.15f, 0.55f, 1.0f), FreestandingStyle);
        // (Lifted a little: at the usual height its foot sank into the container's floor.)
        var orange = new Portal(
            "Orange portal", Level.ContainerPortalFloorPoint + new float3(0, CenterHeight + Level.ContainerPortalLift, 0), Level.ContainerPortalNormal,
            PortalRadius, new float3(1.0f, 0.55f, 0.1f), ContainerStyle) { Lift = Level.ContainerPortalLift, BlockBehind = true };
        blue.Linked = orange;
        orange.Linked = blue;
        _portals.Add(blue);
        _portals.Add(orange);

        // The outback shack's yellow portal: offline until something connects it.
        // The purple portal that used to be its partner, at the back of the Antwerp container yard, is out of the game for
        // now but kept for reuse (Level.YardPortal*, YardStyle, its PortalIndex entry). To put it back (and turn the
        // container there back into a box: Level.AddContainer):
        //     Link("Purple portal", Level.YardPortalFloorPoint, Level.YardPortalNormal, new float3(0.65f, 0.3f, 1.0f), YardStyle,
        //          "Yellow portal", Level.OutbackPortalFloorPoint, Level.OutbackPortalNormal, new float3(1.0f, 0.85f, 0.2f), OutbackStyle);
        _portals.Add(new Portal(
            "Yellow portal", Level.OutbackPortalFloorPoint + new float3(0, CenterHeight, 0), Level.OutbackPortalNormal,
            PortalRadius, new float3(1.0f, 0.85f, 0.2f), OutbackStyle));

        // Down the ladder under the outback shed: the cellar's green portal and the bunker's three.
        Link("Green portal", Level.CellarPortalFloorPoint, Level.CellarPortalNormal, new float3(0.25f, 0.95f, 0.35f), CellarStyle,
             "Teal portal", Level.BunkerWestPortalFloorPoint, new float3(1, 0, 0), new float3(0.1f, 0.85f, 0.75f), BunkerStyle);
        Link("Ice portal", Level.BunkerNorthPortalFloorPoint, new float3(0, 0, 1), new float3(0.75f, 0.92f, 1.0f), BunkerStyle,
             "White portal", Level.ArcticPortalFloorPoint, Level.ArcticPortalNormal, new float3(0.92f, 0.95f, 1.0f), ArcticStyle);
        // ...but they start offline: the white portal comes on only when a terminal connects something to it, and that brings
        // the Arctic research station's crew out to it (Game/NpcsStation.cs). Reconnecting the ice portal to it restores the pair.
        _portals[^1].Linked = _portals[^2].Linked = null;
        Link("Red portal", Level.BunkerEastPortalFloorPoint, new float3(-1, 0, 0), new float3(1.0f, 0.22f, 0.16f), BunkerStyle,
             "Pink portal", Level.ShipPortalFloorPoint, Level.ShipPortalNormal, new float3(1.0f, 0.35f, 0.62f), ShipStyle);
        Link("Lime portal", Level.BunkerSouthPortalFloorPoint, new float3(0, 0, -1), new float3(0.6f, 1.0f, 0.15f), BunkerStyle,
             "Olive portal", Level.JunglePortalFloorPoint, Level.JunglePortalNormal, new float3(0.62f, 0.68f, 0.18f), JungleStyle);
        // The Arctic: a second old container beside the store, its doors on the far side, one of them ajar. A slate portal
        // in a false wall at its closed end (nothing outside gives it away), linked to the magenta portal in Rio de
        // Janeiro, in the wall at the dead end of an alley at the foot of the favela.
        Link("Slate portal", Level.ArcticContainerPortalFloorPoint, Level.ArcticContainerPortalNormal, Level.ArcticContainerPortalColor, ArcticContainerStyle,
             "Magenta portal", Level.RioPortalFloorPoint, Level.RioPortalNormal, Level.RioPortalColor, RioStyle);
        // The police station's tech room: the HQ's navy portal, offline until its PC (or the prison intro) connects it.
        // The magenta portal that used to be its partner, in the warehouse's east wall, is in Rio now (above). The
        // warehouse end is kept for reuse (Level.WarehouseEastPortal*, WarehouseStyle): Level.WarehouseEastPortalColor
        // with a portal of a new colour (and a PortalIndex entry for it), linked to the navy portal, would put it back.
        _portals.Add(new Portal(
            "Navy portal", Level.PolicePortalFloorPoint + new float3(0, CenterHeight, 0), Level.PolicePortalNormal,
            PortalRadius, Level.PolicePortalColor, PoliceStyle));
        // The prison yard: a portal in a false wall halfway along a shipping container standing in the middle of the yard (the
        // intro lowers it in by helicopter). It's linked to nothing until the intro brings it online, or the HQ's
        // terminal connects to it.
        _portals.Add(new Portal(
            "Crimson portal", Level.PrisonPortalFloorPoint + new float3(0, CenterHeight, 0), Level.PrisonPortalNormal,
            PortalRadius, Level.PrisonPortalColor, ContainerStyle));
        // The undercroft of the block of flats: what the crimson portal connects to when the prison intro brings it online (Rook's way out).
        _portals.Add(new Portal(
            "Cobalt portal", Level.FlatsPortalFloorPoint + new float3(0, CenterHeight, 0), Level.FlatsPortalNormal,
            PortalRadius, Level.FlatsPortalColor, FlatsStyle));
        // The police vault, at the end of the corridor behind the airlock: offline until its own PC connects it.
        _portals.Add(new Portal(
            "Gold portal", Level.PoliceVaultPortalFloorPoint + new float3(0, CenterHeight, 0), Level.PoliceVaultPortalNormal,
            PortalRadius, Level.PoliceVaultPortalColor, VaultStyle));

        _named = [.. _portals];
        if (none)
        {
            _portals.Clear();
            return;
        }

        if (factoryRaid)
        {
            var (navy, gold) = (Hq, Vault);
            orange.Linked = null;
            blue.Linked = null;
            blue.Dark = true;
            _portals.RemoveAll(p => p != blue && p != navy && p != gold);
        }

        foreach (var portal in _portals)
        {
            BuildDoorway(level, portal);
            _meshes[portal] = BuildMeshes(portal.Style);
        }
        foreach (var portal in _portals)
            if (!portal.Online)
                UpdateBlocker(portal);
    }

    /// <summary>The portal of that colour ("crimson", or its full name "Crimson portal"), if there is one.</summary>
    public Portal? Find(string colour) =>
        _portals.FirstOrDefault(p => PortalIndex.Short(p.Name).Equals(PortalIndex.Short(colour), StringComparison.OrdinalIgnoreCase));

    private void Link(string nameA, float3 floorA, float3 normalA, float3 colorA, PortalStyle styleA,
                      string nameB, float3 floorB, float3 normalB, float3 colorB, PortalStyle styleB)
    {
        var a = new Portal(nameA, floorA + new float3(0, CenterHeight, 0), normalA, PortalRadius, colorA, styleA);
        var b = new Portal(nameB, floorB + new float3(0, CenterHeight, 0), normalB, PortalRadius, colorB, styleB);
        a.Linked = b;
        b.Linked = a;
        _portals.Add(a);
        _portals.Add(b);
    }

    /// <summary>The player walked into this portal (and out of the one it's linked to).</summary>
    public event Action<Portal>? PlayerCrossed;

    /// <summary>A portal has just been connected (<see cref="Connect"/>): raised for each end. Its lamps start flashing.</summary>
    public event Action<Portal>? PortalOpened;

    /// <summary>The police HQ's portal (in its tech room): the one the terminal there can point anywhere.</summary>
    public Portal Hq => _hqPortal ??= _named.First(p => p.Name == "Navy portal");
    private Portal? _hqPortal;

    /// <summary>The factory's portal, on the loading dock.</summary>
    public Portal Factory => _factoryPortal ??= _named.First(p => p.Name == "Blue portal");
    private Portal? _factoryPortal;

    /// <summary>The police vault's portal: the one the vault's PC can point anywhere.</summary>
    public Portal Vault => _vaultPortal ??= _named.First(p => p.Name == "Gold portal");
    private Portal? _vaultPortal;

    /// <summary>The cobalt portal in the undercroft at the flats: the one the PC beside it dials the police with.</summary>
    public Portal Flats => _flatsPortal ??= _named.First(p => p.Name == "Cobalt portal");
    private Portal? _flatsPortal;

    /// <summary>The container yard's orange portal: the one the PC in the grey container beside it can point anywhere.</summary>
    public Portal Yard => _yardPortal ??= _named.First(p => p.Name == "Orange portal");
    private Portal? _yardPortal;

    /// <summary>The jungle shack's portal: the one the PC behind it can point anywhere.</summary>
    public Portal Jungle => _junglePortal ??= _named.First(p => p.Name == "Olive portal");
    private Portal? _junglePortal;

    /// <summary>
    /// Connects <paramref name="a"/> to <paramref name="b"/>. Only two portals can share a link: whatever either was
    /// connected to before is cut off and goes offline (a dim glow; you can't walk into it) until something connects to
    /// it again. Returns the portals that went offline.
    /// </summary>
    /// <param name="announce">False: nobody near it reacts, and no <see cref="PortalOpened"/> (it was always on: a respawn).</param>
    public IReadOnlyList<Portal> Connect(Portal a, Portal b, bool announce = true)
    {
        var dropped = new List<Portal>();
        if (a == b || a.Linked == b || a.Dark || b.Dark)
            return dropped;
        foreach (var old in (ReadOnlySpan<Portal?>)[a.Linked, b.Linked])
        {
            if (old is null || old == a || old == b)
                continue;
            old.Linked = null;
            dropped.Add(old);
        }
        a.Linked = b;
        b.Linked = a;
        a.OnlineSince = b.OnlineSince = Time;
        foreach (var p in _portals)
            UpdateBlocker(p);
        Log.Write($"[PORTAL] {a.Name} connected to {b.Name}" + (dropped.Count > 0 ? $"; offline: {string.Join(", ", dropped.Select(p => p.Name))}" : ""));
        if (announce)
        {
            PortalOpened?.Invoke(a);
            PortalOpened?.Invoke(b);
        }

        return dropped;
    }

    /// <summary>Takes <paramref name="portal"/> offline (and whatever it was connected to).</summary>
    public void Disconnect(Portal portal)
    {
        if (portal.Linked is not { } other)
            return;
        other.Linked = null;
        portal.Linked = null;
        UpdateBlocker(portal);
        UpdateBlocker(other);
        Log.Write($"[PORTAL] {portal.Name} disconnected; offline: {portal.Name}, {other.Name}");
    }

    /// <summary>An offline portal's walk-through gap is closed off (there's nothing beyond its wall).</summary>
    private void UpdateBlocker(Portal portal)
    {
        _level.SetBlocker("portal " + portal.Name, portal.Online ? null
            : portal.LocalBoxToWorld(new float3(-WalkHalfWidth, portal.LocalFloor, -portal.Style.WallDepth), new float3(WalkHalfWidth, WalkTop, -0.03f)));
    }

    /// <summary>The portals in the bunker (their glow lights it).</summary>
    public IEnumerable<Portal> BunkerPortals => _portals.Where(p => Level.ZoneAt(p.Center) == Zones.Bunker);

    /// <summary>The warning lamps' centre above the portal's centre: halfway across the ring's face, or just above a slim rim's trim.</summary>
    private static float LampHeight(PortalStyle style) =>
        float.Max((PortalRadius + 0.04f + style.RingOuterRadius) / 2, PortalRadius + 0.04f + LampHousing / 2);

    private static PortalMeshes BuildMeshes(PortalStyle style)
    {
        var r = PortalRadius;
        var band = style.ChunkyRing
            ? new MeshBuilder().Ring(r + 0.1f, style.RingOuterRadius - 0.08f, style.RingFront - 0.01f, style.RingFront + 0.08f).Build()
            : null;
        var trim = new MeshBuilder().Ring(r - 0.04f, r + 0.04f, style.RingFront - 0.04f, style.RingFront + 0.02f).Build();

        // The lamps: top centre, halfway across the ring's face (on a slim rim, just above the trim, onto the wall),
        // on the front of the ring (or its band) and on the back (the back band, or the wall's far face).
        // (The lenses are built round the portal's axis and lifted to LampHeight when drawn.)
        var lampY = LampHeight(style);
        var front = style.RingFront + (style.ChunkyRing ? 0.08f : 0f);
        var backFace = -style.WallDepth - (style.Freestanding ? 0.08f : 0f);
        var lamp = new MeshBuilder().Box(new float3(0, lampY, front - 0.03f), new float3(LampHousing, LampHousing, 0.08f)).Build();
        var lens = new MeshBuilder().Cylinder(LampLensRadius, front, front + 0.03f).Build();
        var backLamp = new MeshBuilder().Box(new float3(0, lampY, backFace + 0.03f), new float3(LampHousing, LampHousing, 0.08f)).Build();
        var backLens = new MeshBuilder().Cylinder(LampLensRadius, backFace - 0.03f, backFace).Build();

        if (style.Freestanding)
        {
            // A ring right through, from its back face to its front, with the band and trim on both faces.
            var back = style.WallDepth;
            var body = new MeshBuilder().Ring(r, style.RingOuterRadius, -back, style.RingFront).Build();
            var backBand = new MeshBuilder().Ring(r + 0.1f, style.RingOuterRadius - 0.08f, -back - 0.08f, -back + 0.01f).Build();
            var backTrim = new MeshBuilder().Ring(r - 0.04f, r + 0.04f, -back - 0.02f, -back + 0.04f).Build();
            return new PortalMeshes(body, band, trim, null, lamp, lens, backLamp, backLens, backBand, backTrim);
        }
        var plate = new MeshBuilder().SquarePlateWithHole(r, r, -style.WallDepth, 0f).Build();
        var ring = new MeshBuilder().Ring(r, style.RingOuterRadius, 0f, style.RingFront).Build();
        return new PortalMeshes(ring, band, trim, plate, lamp, lens, backLamp, backLens);
    }

    /// <summary>
    /// Freestanding portals can only be walked into from the front: while the player is behind one, its walk-through
    /// gap is closed off. (Not while they're in front: going in, their body reaches behind the water before their eye
    /// crosses it.) Call every frame with where the player is.
    /// </summary>
    public void UpdateBackBlockers(float3 player)
    {
        foreach (var portal in _portals)
        {
            if (!(portal.Style.Freestanding || portal.BlockBehind) || !portal.Online)
                continue;
            _level.SetBlocker("portal back " + portal.Name, portal.SignedDistance(player) >= 0f ? null
                : portal.LocalBoxToWorld(new float3(-WalkHalfWidth, portal.LocalFloor, -portal.Style.WallDepth), new float3(WalkHalfWidth, WalkTop, 0f)));
        }
    }

    /// <summary>
    /// Cuts a square hole (the circle's bounding box) through the wall; a plate mesh fills its corners so the doorway
    /// looks round. Invisible collision then limits the player to a rectangle that fits inside the circle. A freestanding
    /// portal has no wall to cut: the collision just keeps the player out of its ring.
    /// </summary>
    private static void BuildDoorway(Level level, Portal portal)
    {
        var style = portal.Style;
        var floor = portal.LocalFloor; // floor height in portal-local Y
        var r = PortalRadius;

        if (style.Freestanding)
        {
            var outer = style.RingOuterRadius;
            var b = -style.WallDepth - 0.08f;
            const float f = -0.03f;
            // Behind the water, round the walkable gap, out to the ring's edge...
            level.AddStatic(portal.LocalBoxToWorld(new float3(-outer, floor, b), new float3(-WalkHalfWidth, outer, f)), default, visible: false, blocksShots: false);
            level.AddStatic(portal.LocalBoxToWorld(new float3(WalkHalfWidth, floor, b), new float3(outer, outer, f)), default, visible: false, blocksShots: false);
            level.AddStatic(portal.LocalBoxToWorld(new float3(-WalkHalfWidth, WalkTop, b), new float3(WalkHalfWidth, outer, f)), default, visible: false, blocksShots: false);
            // ...and the ring's front half: its sides and its top.
            var ringFront = style.RingFront + 0.08f;
            level.AddStatic(portal.LocalBoxToWorld(new float3(-outer, floor, f), new float3(-r, r * 0.6f, ringFront)), default, visible: false, blocksShots: false);
            level.AddStatic(portal.LocalBoxToWorld(new float3(r, floor, f), new float3(outer, r * 0.6f, ringFront)), default, visible: false, blocksShots: false);
            level.AddStatic(portal.LocalBoxToWorld(new float3(-r, r * 0.85f, f), new float3(r, outer, ringFront)), default, visible: false, blocksShots: false);
            return;
        }

        level.CarveOpening(portal.LocalBoxToWorld(new float3(-r, floor, -style.WallDepth - 0.02f), new float3(r, r, 0.3f)));

        // Behind the water, around the walkable gap (starts just behind the surface so shots hit the water first).
        var back = -style.WallDepth;
        const float front = -0.03f;
        level.AddStatic(portal.LocalBoxToWorld(new float3(-r, floor, back), new float3(-WalkHalfWidth, r, front)), default, visible: false);
        level.AddStatic(portal.LocalBoxToWorld(new float3(WalkHalfWidth, floor, back), new float3(r, r, front)), default, visible: false);
        level.AddStatic(portal.LocalBoxToWorld(new float3(-WalkHalfWidth, WalkTop, back), new float3(WalkHalfWidth, r, front)), default, visible: false);

        // A heavy ring's lower part sticks out into the room; don't let the player walk into it.
        if (style.ChunkyRing)
        {
            var depth = style.RingFront + 0.08f;
            level.AddStatic(portal.LocalBoxToWorld(new float3(-style.RingOuterRadius, floor, 0), new float3(-0.6f, floor + 0.6f, depth)), default, visible: false, blocksShots: false);
            level.AddStatic(portal.LocalBoxToWorld(new float3(0.6f, floor, 0), new float3(style.RingOuterRadius, floor + 0.6f, depth)), default, visible: false, blocksShots: false);
        }
    }

    /// <summary>
    /// Adds each portal's plate and ring to the physics world, so the frames take bullet holes like everything else.
    /// The water itself isn't in it: shots that reach it are stopped by <see cref="Raycast(float3, float3, float, out float, out Portal?)"/>.
    /// </summary>
    public void AddPhysics(PhysicsWorld physics, SurfaceTable surfaces)
    {
        foreach (var portal in _portals)
        {
            var style = portal.Style;
            var meshes = _meshes[portal];
            void Add(Mesh mesh, Material material, float3 tint)
            {
                var surface = SurfaceInfo.ForMaterial(material, tint);
                var (restitution, friction) = surface.Contact;
                physics.AddStaticMesh(mesh, portal.LocalToWorld, surfaces.Index(surface), restitution, friction);
            }

            if (meshes.Plate is not null)
                Add(meshes.Plate, style.PlateMaterial, style.PlateTint);
            Add(meshes.Body, Material.RustyMetal, RingColor);
            if (meshes.Band is not null)
                Add(meshes.Band, Material.Steel, BandColor);
            Add(meshes.Trim, Material.PaintedMetal, portal.Color * 0.9f);
            if (meshes.BackBand is not null)
                Add(meshes.BackBand, Material.Steel, BandColor);
            if (meshes.BackTrim is not null)
                Add(meshes.BackTrim, Material.PaintedMetal, portal.Color * 0.9f);
        }
    }

    public void Update(float time)
    {
        Time = time;
        foreach (var portal in _portals)
            portal.RemoveExpiredRipples(time);
    }

    /// <summary>Nearest portal water surface along a ray (used to stop shots and aim rays at portals).</summary>
    public bool Raycast(float3 origin, float3 direction, float maxDistance, out float distance) =>
        Raycast(origin, direction, maxDistance, out distance, out _);

    public bool Raycast(float3 origin, float3 direction, float maxDistance, out float distance, out Portal? portal)
    {
        distance = maxDistance;
        portal = null;
        foreach (var candidate in _portals)
        {
            if (candidate.Raycast(origin, direction, distance, out var d))
            {
                distance = d;
                portal = candidate;
            }
        }

        return portal is not null;
    }

    /// <summary>Nearest ring frame along a ray (shots hitting the frame stop on its face).</summary>
    public bool RaycastFrames(float3 origin, float3 direction, float maxDistance, out float distance)
    {
        distance = maxDistance;
        var hit = false;
        foreach (var portal in _portals)
        {
            if (portal.RaycastRing(origin, direction, distance, out var d))
            {
                distance = d;
                hit = true;
            }
        }

        return hit;
    }

    /// <summary>
    /// A shot hit the water of <paramref name="portal"/>. Both portals are one body of water, so the ripple
    /// also appears on the linked portal at the matching (mirrored) spot.
    /// </summary>
    public void Splash(Portal portal, float3 worldPoint)
    {
        SplashCount++;
        portal.AddRipple(worldPoint, Time);
        var local = float3.Transform(worldPoint, portal.WorldToLocal);
        if (portal.Linked is not { } linked)
        {
            Log.Write($"[SPLASH] {portal.Name} (offline) hit at ({local.X:0.00}, {local.Y:0.00})");
            return;
        }
        var linkedPoint = float3.Transform(worldPoint, portal.ToLinked);
        linked.AddRipple(linkedPoint, Time);
        var linkedLocal = float3.Transform(linkedPoint, linked.WorldToLocal);
        Log.Write($"[SPLASH] {portal.Name} hit at ({local.X:0.00}, {local.Y:0.00}); ripple mirrored on {linked.Name} at ({linkedLocal.X:0.00}, {linkedLocal.Y:0.00})");
    }

    /// <summary>
    /// Teleports the player if their eye moved through a portal this frame. Position, look direction and
    /// velocity are all carried across, so you keep your momentum and come out facing the right way.
    /// </summary>
    public bool TryTeleport(PlayerController player, float3 previousEye)
    {
        foreach (var portal in _portals)
        {
            if (!portal.Online || !portal.IsCrossedBy(previousEye, player.EyePosition))
                continue;

            var toLinked = portal.ToLinked;
            var eye = float3.Transform(player.EyePosition, toLinked);
            var forward = float3.TransformNormal(Camera.DirectionFromAngles(player.Yaw, player.Pitch), toLinked);

            var eyeOffset = player.EyePosition - player.Position; // not assumed constant (crouch, bob)
            player.Position = eye - eyeOffset;
            player.Velocity = float3.TransformNormal(player.Velocity, toLinked);
            player.Yaw = float.Atan2(forward.X, -forward.Z);
            player.Pitch = float.Asin(float.Clamp(forward.Y, -1f, 1f));
            player.LeftThroughPortal();

            Log.Write($"[PORTAL] Entered {portal.Name}, exited {portal.Linked!.Name}");
            PlayerCrossed?.Invoke(portal);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Renders what each visible portal looks through. The virtual camera is the main camera moved through the
    /// portal pair; a clip plane at the exit portal hides the wall and anything else behind it. Portals visible
    /// through a portal get a view of their own first (a second level, from the virtual camera moved on through
    /// them), so a portal looks the same whether you see it directly or through another one; deeper than that,
    /// portals show a flat glow. Each view is only drawn inside its portal's outline on screen.
    /// </summary>
    /// <param name="drawPortalViewScene">Draws the scene for a view: the exit portal it looks out of, and its level
    /// (1 = seen directly through a portal, 2 = through two).</param>
    public void RenderViews(Renderer renderer, in Camera camera, int2 size, Action<Portal, int> drawPortalViewScene)
    {
        var aspect = (float)size.X / size.Y;
        var projection = camera.Projection(aspect);
        var cameraWorld = camera.World;
        var viewProjection = camera.View * projection;

        foreach (var portal in _portals)
        {
            if (portal.Dark)
            {
                portal.View.EnsureSize(size);
                renderer.ClearToBlack(portal.View);
                continue;
            }

            // Can't see into a portal from behind it (unless it stands on its own), and don't bother rendering portals that are far away or off-screen
            // (or offline: they show nothing).
            if (!portal.Online || !Visible(portal, camera.Position, viewProjection))
                continue;
            var rect = ScreenRect(viewProjection, portal, size);
            if (rect.Z <= 0 || rect.W <= 0)
                continue;

            var virtualWorld = cameraWorld * portal.ViewThrough(camera.Position, out var exit);   // (through the back of a freestanding one: a window)
            float4x4.Invert(virtualWorld, out var virtualView);
            var virtualPosition = virtualWorld.W.XYZ;
            var virtualViewProjection = virtualView * projection;

            // Second level: the portals on the far side that show through this one.
            foreach (var inner in _portals)
            {
                inner.InnerViewReady = false;
                if (inner == exit || inner == portal || !inner.Online || exit.SignedDistance(inner.Center) <= 0f)
                    continue;
                if (!Visible(inner, virtualPosition, virtualViewProjection))
                    continue;
                var innerRect = Intersect(rect, ScreenRect(virtualViewProjection, inner, size));
                if (innerRect.Z <= 0 || innerRect.W <= 0)
                    continue;

                var innerWorld = virtualWorld * inner.ViewThrough(virtualPosition, out var innerExit);
                float4x4.Invert(innerWorld, out var innerView);
                inner.InnerView.EnsureSize(size);
                renderer.BeginView(inner.InnerView, size, innerView * projection, innerWorld.W.XYZ, ClipPlaneAt(innerExit), innerRect);
                drawPortalViewScene(innerExit, 2);
                inner.InnerViewReady = true;
            }

            portal.View.EnsureSize(size);
            renderer.BeginView(portal.View, size, virtualViewProjection, virtualPosition, ClipPlaneAt(exit), rect);
            drawPortalViewScene(exit, 1);
        }
    }

    /// <summary>
    /// Hides everything behind <paramref name="exit"/>'s wall in a view that looks out of it. A freestanding portal's
    /// plane goes a little further out, past its own water, which the window view through its back would otherwise see.
    /// </summary>
    private static float4 ClipPlaneAt(Portal exit) =>
        new(exit.Normal, -float3.Dot(exit.Normal, exit.Center) + (exit.Style.Freestanding ? -0.01f : 0.002f));

    /// <summary>Whether a portal's front (or either face of a freestanding one) can be seen from <paramref name="eye"/>: facing it, within 70 m and on screen.</summary>
    private static bool Visible(Portal portal, float3 eye, in float4x4 viewProjection) =>
        (portal.SignedDistance(eye) >= -0.05f || portal.Style.Freestanding)
        && float3.Distance(eye, portal.Center) <= 70f
        && SphereOnScreen(viewProjection, portal.Center, portal.Style.RingOuterRadius + 0.2f);

    /// <summary>
    /// The pixels the portal's ring covers on screen (x, y, width, height), with a margin for the water's refraction;
    /// the whole screen if the portal reaches behind the camera.
    /// </summary>
    private static int4 ScreenRect(in float4x4 m, Portal portal, int2 size)
    {
        var right = new float3(portal.LocalToWorld.X.X, portal.LocalToWorld.X.Y, portal.LocalToWorld.X.Z);
        var up = new float3(portal.LocalToWorld.Y.X, portal.LocalToWorld.Y.Y, portal.LocalToWorld.Y.Z);
        var r = portal.Style.RingOuterRadius + 0.05f;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (var i = 0; i < 24; i++)
        {
            var a = i * float.Tau / 24;
            var p = portal.Center + (right * float.Cos(a) + up * float.Sin(a)) * (r / float.Cos(float.Pi / 24));
            var w = p.X * m.X.W + p.Y * m.Y.W + p.Z * m.Z.W + m.W.W;
            if (w < 0.05f)
                return new int4(0, 0, size.X, size.Y);
            var x = (p.X * m.X.X + p.Y * m.Y.X + p.Z * m.Z.X + m.W.X) / w;
            var y = (p.X * m.X.Y + p.Y * m.Y.Y + p.Z * m.Z.Y + m.W.Y) / w;
            minX = float.Min(minX, x); maxX = float.Max(maxX, x);
            minY = float.Min(minY, y); maxY = float.Max(maxY, y);
        }

        var margin = 0.03f * size.X;
        var x0 = (int)float.Clamp((minX * 0.5f + 0.5f) * size.X - margin, 0, size.X);
        var x1 = (int)float.Clamp((maxX * 0.5f + 0.5f) * size.X + margin + 1, 0, size.X);
        var y0 = (int)float.Clamp((minY * 0.5f + 0.5f) * size.Y - margin, 0, size.Y);
        var y1 = (int)float.Clamp((maxY * 0.5f + 0.5f) * size.Y + margin + 1, 0, size.Y);
        return new int4(x0, y0, x1 - x0, y1 - y0);
    }

    private static int4 Intersect(int4 a, int4 b)
    {
        var x0 = int.Max(a.X, b.X);
        var y0 = int.Max(a.Y, b.Y);
        var x1 = int.Min(a.X + a.Z, b.X + b.Z);
        var y1 = int.Min(a.Y + a.W, b.Y + b.W);
        return new int4(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>False if the sphere is entirely outside the left, right, top or bottom side of the view.</summary>
    private static bool SphereOnScreen(in float4x4 m, float3 center, float radius)
    {
        var cx = new float4(m.X.X, m.Y.X, m.Z.X, m.W.X);
        var cy = new float4(m.X.Y, m.Y.Y, m.Z.Y, m.W.Y);
        var cw = new float4(m.X.W, m.Y.W, m.Z.W, m.W.W);
        foreach (var p in (ReadOnlySpan<float4>)[cw + cx, cw - cx, cw + cy, cw - cy])
        {
            var n = new float3(p.X, p.Y, p.Z);
            if (float3.Dot(n, center) + p.W < -radius * float.Sqrt(float3.Dot(n, n)))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Draws the frames and the water surfaces. <paramref name="level"/> is how many portals deep the view being
    /// drawn is: 0 is the player's own view, where every portal shows its view; in a portal's view (1) the portals
    /// that got a second-level view show it; deeper, or without one, a portal's water is a flat glow.
    /// </summary>
    public void Draw(Renderer renderer, int level)
    {
        foreach (var portal in _portals)
        {
            var style = portal.Style;
            if (portal.Hidden || !renderer.InView(Aabb.FromCenterSize(portal.Center, new float3(2f * (style.RingOuterRadius + style.WallDepth + 0.3f)))))
                continue;
            var meshes = _meshes[portal];
            var toWorld = portal.LocalToWorld;

            // The plate matches the wall; the ring is old, gritty metal with chipped paint on the coloured trim.
            if (meshes.Plate is not null)
                renderer.DrawMesh(meshes.Plate, toWorld, style.PlateTint, ShadeMode.Lit, style.PlateMaterial);
            renderer.DrawMesh(meshes.Body, toWorld, RingColor, ShadeMode.Lit, Material.RustyMetal);
            if (meshes.Band is not null)
                renderer.DrawMesh(meshes.Band, toWorld, BandColor, ShadeMode.Lit, Material.Steel);
            renderer.DrawMesh(meshes.Trim, toWorld, portal.Color * 0.9f, ShadeMode.Lit, Material.PaintedMetal);
            if (meshes.BackBand is not null)
                renderer.DrawMesh(meshes.BackBand, toWorld, BandColor, ShadeMode.Lit, Material.Steel);
            if (meshes.BackTrim is not null)
                renderer.DrawMesh(meshes.BackTrim, toWorld, portal.Color * 0.9f, ShadeMode.Lit, Material.PaintedMetal);

            // The warning lamps, front and back: flashing red while it's connected (starting lit the moment it connects),
            // a dark red lens while it's offline.
            renderer.DrawMesh(meshes.Lamp, toWorld, LampHousingColor, ShadeMode.Lit, Material.Steel);
            renderer.DrawMesh(meshes.BackLamp, toWorld, LampHousingColor, ShadeMode.Lit, Material.Steel);
            var lensToWorld = float4x4.CreateTranslation(0, LampHeight(style), 0) * toWorld;
            if (portal.Online || portal.Dark)
            {
                var lit = (Time - portal.OnlineSince) % LampFlashPeriod < LampFlashPeriod / 2;
                var lampColor = lit ? LampOnColor : LampDimColor;
                renderer.DrawMesh(meshes.Lens, lensToWorld, lampColor, ShadeMode.Lit, Material.Emissive);
                renderer.DrawMesh(meshes.BackLens, lensToWorld, lampColor, ShadeMode.Lit, Material.Emissive);
            }
            else
            {
                renderer.DrawMesh(meshes.Lens, lensToWorld, LampOffColor, ShadeMode.Lit, Material.Plain, roughMetal: new float2(0.25f, 0f));
                renderer.DrawMesh(meshes.BackLens, lensToWorld, LampOffColor, ShadeMode.Lit, Material.Plain, roughMetal: new float2(0.25f, 0f));
            }

            // A box covering the circle, from just in front of the wall into it. The shader cuts it to a disc;
            // its back face keeps showing the view when the camera is right at the surface.
            var size = PortalRadius * 2;
            var depth = float.Clamp(style.WallDepth, 0.12f, 0.6f);
            var surface = float4x4.CreateScale(size, size, depth)
                * float4x4.CreateTranslation(0, 0, 0.005f - depth / 2)
                * toWorld;

            if (portal.Dark)
                renderer.DrawPortalSurface(surface, portal.View, portal.Color, portal.WorldToLocal, PortalRadius, portal.Ripples, Time);   // (its view is only ever black)
            else if (!portal.Online)
                renderer.DrawPortalGlow(surface, portal.Color * 0.12f, portal.WorldToLocal, PortalRadius);   // offline: dark, a faint tint
            else if (level == 0)
                renderer.DrawPortalSurface(surface, portal.View, portal.Color, portal.WorldToLocal, PortalRadius, portal.Ripples, Time);
            else if (level == 1 && portal.InnerViewReady)
                renderer.DrawPortalSurface(surface, portal.InnerView, portal.Color, portal.WorldToLocal, PortalRadius, portal.Ripples, Time);
            else
                renderer.DrawPortalGlow(surface, portal.Color * 0.6f, portal.WorldToLocal, PortalRadius);
        }
    }

    public void Dispose()
    {
        foreach (var portal in _portals)
        {
            portal.View.Dispose();
            portal.InnerView.Dispose();
        }
    }
}
