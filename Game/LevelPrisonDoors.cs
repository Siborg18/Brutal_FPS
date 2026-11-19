using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The prison's moving doors, which the intro opens one after another (<see cref="PrisonIntro"/>):
/// <list type="bullet">
/// <item>"entrance": the gatehouse's sliding glass doors (two leaves parting);</item>
/// <item>"sally": the steel door from the gatehouse through to the locker room (slides along the wall);</item>
/// <item>"grille": the barred gate between the locker room and the cell corridor;</item>
/// <item>"yard": the steel door from the corridor out to the yard;</item>
/// <item>"locker": the player's locker door (hinged);</item>
/// <item>"container": the shipping container's two doors (hinged, swung right round against its sides).</item>
/// </list>
/// Each leaf is a set of meshes posed by how far open the door is; its collider follows it (a sliding leaf's always,
/// a hinged leaf's only when it's shut or fully open). Its meshes are in the physics world and follow it all the way,
/// so bullets, debris and people's eyes meet the door as drawn, as they do the factory's sliding door.
/// The leaves are in plain, clean paint: the textured materials are mapped in world space, so they'd stay put as a door moved.
/// </summary>
public sealed partial class Level
{
    private sealed class DoorLeaf(Func<float, float4x4> pose, float3 boxCenter, float3 boxSize, bool slides)
    {
        public readonly Dictionary<(Material, float3), MeshBuilder> Builders = [];
        public readonly List<(Mesh Mesh, float3 Tint, Material Material)> Meshes = [];
        public readonly List<BulletSharp.CollisionObject> PhysicsParts = [];
        public Func<float, float4x4> Pose { get; } = pose;

        public MeshBuilder B(Material material, float3 tint)
        {
            if (!Builders.TryGetValue((material, tint), out var b))
                Builders[(material, tint)] = b = new MeshBuilder();
            return b;
        }

        public void Build()
        {
            foreach (var ((material, tint), b) in Builders)
                Meshes.Add((b.Build(), tint, material));
            Builders.Clear();
        }

        /// <summary>Where the leaf blocks the player at <paramref name="open"/> (null while a hinged leaf is swinging).</summary>
        public Aabb? Collider(float open)
        {
            if (!slides && open > 0.02f && open < 0.98f)
                return null;
            var xf = Pose(open);
            var h = boxSize * 0.5f;
            var min = new float3(float.MaxValue);
            var max = new float3(float.MinValue);
            for (var i = 0; i < 8; i++)
            {
                var corner = boxCenter + new float3((i & 1) == 0 ? -h.X : h.X, (i & 2) == 0 ? -h.Y : h.Y, (i & 4) == 0 ? -h.Z : h.Z);
                var w = float3.Transform(corner, xf);
                min = float3.Min(min, w);
                max = float3.Max(max, w);
            }
            return new Aabb(min, max);
        }
    }

    private sealed class PrisonDoor(string name, float seconds)
    {
        public string Name { get; } = name;
        public float Seconds { get; } = seconds;
        public readonly List<DoorLeaf> Leaves = [];
        public float Open;     // 0 shut .. 1 open (eased)
        public float Target;
        public float T = 1f;   // 0..1 along the current move
        public bool StartsOpen;  // as it was when the game began (see RememberDoorsAsTheyStart)

        public float From => Target > 0.5f ? 0f : 1f;
    }

    private readonly Dictionary<string, PrisonDoor> _prisonDoors = [];

    /// <summary>Every moving door in the world made with <see cref="NewDoor"/> (the prison's, the factory's gate and rear door, the flats', motel's, shack's, Rio store's, the police station's keypad doors).</summary>
    private readonly List<PrisonDoor> _allDoors = [];
    private bool _factoryDoorStartsOpen;

    /// <summary>Notes how every door stands now, as the game begins, for <see cref="ResetDoors"/> to put back after a death.</summary>
    public void RememberDoorsAsTheyStart()
    {
        foreach (var door in _allDoors)
            door.StartsOpen = door.Target > 0.5f;
        _factoryDoorStartsOpen = _doorTarget > 0.5f;
    }

    /// <summary>A respawn: every door back as it was when the game began, at once (anything that wants otherwise, like the intro's restart, sets its own after).</summary>
    public void ResetDoors()
    {
        foreach (var door in _allDoors)
            MoveDoor(door, door.StartsOpen, instant: true);
        SetFactoryDoor(_factoryDoorStartsOpen);
        _airlockPending = null;
        _keypadDenied = 0;
        _deniedAt = null;
        Log.Write($"[DOOR] Respawn: {_allDoors.Count + 1} doors back as they started");
    }

    /// <summary>The names of the prison's doors (see the class summary).</summary>
    public IReadOnlyCollection<string> PrisonDoorNames => _prisonDoors.Keys;

    /// <summary>Opens or shuts a prison door: over its own time (2 s or so), or at once.</summary>
    public void SetPrisonDoor(string name, bool open, bool instant = false)
    {
        if (!_prisonDoors.TryGetValue(name, out var door))
        {
            Log.Write($"[WARN] No prison door called \"{name}\".");
            return;
        }
        if (MoveDoor(door, open, instant))
            Log.Write($"[DOOR] {(open ? "Opening" : "Shutting")} the prison's {name} door");
    }

    /// <summary>Starts a door opening or shutting (or sets it at once). False if it was already going that way.</summary>
    private bool MoveDoor(PrisonDoor door, bool open, bool instant = false)
    {
        var target = open ? 1f : 0f;
        if (instant)
        {
            door.Open = door.Target = target;
            door.T = 1f;
            UpdateDoorColliders(door);
            return false;
        }
        if (door.Target == target)
            return false;
        // Reversing part way: carry on from where it is.
        door.T = door.T >= 1f ? 0f : 1f - door.T;
        door.Target = target;
        return true;
    }

    /// <summary>True while the door is moving.</summary>
    public bool PrisonDoorMoving(string name) => _prisonDoors.TryGetValue(name, out var d) && d.T < 1f;

    /// <summary>All the prison's doors open (or shut) at once.</summary>
    public void SetAllPrisonDoors(bool open)
    {
        foreach (var name in _prisonDoors.Keys)
            SetPrisonDoor(name, open, instant: true);
    }

    public void UpdatePrisonDoors(float dt)
    {
        foreach (var door in _prisonDoors.Values)
            StepDoor(door, dt);
    }

    /// <summary>Moves a door on by <paramref name="dt"/> (eased in and out), its colliders with it.</summary>
    private void StepDoor(PrisonDoor door, float dt)
    {
        if (door.T >= 1f)
            return;
        door.T = float.Min(1f, door.T + dt / door.Seconds);
        var eased = door.T * door.T * (3f - 2f * door.T);
        door.Open = door.From + (door.Target - door.From) * eased;
        if (door.T >= 1f)
            door.Open = door.Target;
        UpdateDoorColliders(door);
    }

    private void UpdateDoorColliders(PrisonDoor door)
    {
        for (var i = 0; i < door.Leaves.Count; i++)
        {
            var leaf = door.Leaves[i];
            SetBlocker($"door {door.Name} {i}", leaf.Collider(door.Open));
            if (_physics is not null)
            {
                var pose = leaf.Pose(door.Open);
                foreach (var part in leaf.PhysicsParts)
                    _physics.MoveDoorPart(part, pose);
            }
        }
    }

    /// <summary>Every door with leaves (all but the factory's sliding door).</summary>
    private IEnumerable<PrisonDoor> LeafDoors()
    {
        foreach (var door in _prisonDoors.Values)
            yield return door;
        if (_bodegaDoor is { } bodega)
            yield return bodega;
        if (_stationDoor is { } station)
            yield return station;
        if (_airlockOuter is { } outer)
            yield return outer;
        if (_airlockInner is { } inner)
            yield return inner;
        foreach (var door in _obsDoors)
            yield return door;
        if (_flatsStairDoor is { } flatsStairs)
            yield return flatsStairs;
        if (_flatsTopDoor is { } flatsTop)
            yield return flatsTop;
        foreach (var door in _motelDoors)
            yield return door;
        if (_factoryGate is { } factoryGate)
            yield return factoryGate;
        if (_rioStoreDoor is { } rioStore)
            yield return rioStore;
        if (_factoryRearDoor is { } factoryRear)
            yield return factoryRear;
        if (_factorySideDoor is { } factorySide)
            yield return factorySide;
    }

    /// <summary>What a door's mesh is made of, for shots. (Its plain parts aren't plaster: they're painted steel.)</summary>
    private static SurfaceInfo DoorSurface(Material material, float3 tint) =>
        material == Material.Plain ? new SurfaceInfo(SurfaceKind.PaintedMetal, tint, material) : SurfaceInfo.ForMaterial(material, tint);

    /// <summary>The nearest door along the ray, by its colliders (for shots and eyes without the physics world).</summary>
    private bool RaycastDoors(float3 origin, float3 direction, ref float distance, out float3 normal, out SurfaceInfo surface)
    {
        normal = default;
        surface = default;
        var hit = false;
        if (DoorCollider(_doorOpen).Bounds.Raycast(origin, direction, distance, out var factory, out var factoryNormal) && factory <= distance)
        {
            distance = factory;
            normal = factoryNormal;
            surface = DoorSurface(_doorParts[0].Material, _doorParts[0].Tint);
            hit = true;
        }

        foreach (var door in LeafDoors())
        {
            foreach (var leaf in door.Leaves)
            {
                if (leaf.Collider(door.Open) is { } box && box.Raycast(origin, direction, distance, out var t, out var n) && t <= distance)
                {
                    distance = t;
                    normal = n;
                    surface = DoorSurface(leaf.Meshes[0].Material, leaf.Meshes[0].Tint);
                    hit = true;
                }
            }
        }

        return hit;
    }

    private void DrawPrisonDoors(Renderer renderer)
    {
        DrawPrisonContainer(renderer);
        foreach (var door in _prisonDoors.Values)
        {
            if (door.Name != "container")   // (the container draws its own)
                DrawDoor(renderer, door);
        }
    }

    private static void DrawDoor(Renderer renderer, PrisonDoor door) => DrawDoor(renderer, door, float4x4.Identity);

    private static void DrawDoor(Renderer renderer, PrisonDoor door, in float4x4 carriedBy)
    {
        foreach (var leaf in door.Leaves)
        {
            var model = leaf.Pose(door.Open) * carriedBy;
            foreach (var (mesh, tint, material) in leaf.Meshes)
                renderer.DrawMesh(mesh, model, tint, ShadeMode.Lit, material);
        }
    }

    private PrisonDoor AddPrisonDoor(string name, float seconds, params DoorLeaf[] leaves)
    {
        var door = NewDoor(name, seconds, leaves);
        _prisonDoors[name] = door;
        return door;
    }

    private PrisonDoor NewDoor(string name, float seconds, params DoorLeaf[] leaves)
    {
        var door = new PrisonDoor(name, seconds);
        _allDoors.Add(door);
        foreach (var leaf in leaves)
        {
            leaf.Build();
            door.Leaves.Add(leaf);
        }
        return door;
    }

    /// <summary>A leaf that slides by <paramref name="travel"/> (world) when open, built in world coordinates where it's shut.</summary>
    private static DoorLeaf SlidingLeaf(float3 travel, float3 closedCenter, float3 size) =>
        new(open => float4x4.CreateTranslation(travel * open), closedCenter, size, slides: true);

    /// <summary>A leaf hinged at <paramref name="hinge"/> (world), turning by <paramref name="angle"/> about y when open; built in the hinge's frame.</summary>
    private static DoorLeaf HingedLeaf(float3 hinge, float angle, float3 localCenter, float3 size) =>
        new(open => float4x4.CreateRotationY(angle * open) * float4x4.CreateTranslation(hinge), localCenter, size, slides: false);

    private void BuildPrisonDoors()
    {
        var id = float4x4.Identity;
        var alu = new float3(0.62f, 0.63f, 0.64f);
        var tinted = new float3(0.12f, 0.14f, 0.16f);
        var steelGrey = new float3(0.5f, 0.5f, 0.52f);

        // --- The staff entrance: two glass leaves in aluminium frames, inside the gatehouse's south wall, parting. ---
        {
            const float zc = GateZ1 - JT - 0.05f, h = 2.4f, w = 0.94f;
            var leaves = new List<DoorLeaf>();
            foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            {
                var cx = EntranceX + side * (w / 2 - 0.02f);
                var leaf = SlidingLeaf(new float3(side * (w - 0.04f), 0, 0), JAt(cx, h / 2, zc), new float3(w, h, 0.06f));
                var frame = leaf.B(Material.Plain, alu);
                OBox(frame, id, JAt(cx, 0.06f, zc), new float3(w, 0.12f, 0.05f));
                OBox(frame, id, JAt(cx, h - 0.04f, zc), new float3(w, 0.08f, 0.05f));
                OBox(frame, id, JAt(cx - w / 2 + 0.03f, h / 2, zc), new float3(0.06f, h, 0.05f));
                OBox(frame, id, JAt(cx + w / 2 - 0.03f, h / 2, zc), new float3(0.06f, h, 0.05f));
                OBox(leaf.B(Material.Glass, tinted), id, JAt(cx, h / 2, zc), new float3(w - 0.1f, h - 0.18f, 0.012f));
                OBox(leaf.B(Material.Plain, new float3(0.85f, 0.85f, 0.85f)), id, JAt(cx, 1.2f, zc + 0.008f), new float3(w - 0.12f, 0.06f, 0.004f));   // manifestation band
                leaves.Add(leaf);
            }
            // The door head (the motor housing) over the opening, inside.
            OBox(JB(Material.Steel, alu), id, JAt(EntranceX, h + 0.1f, zc), new float3(3.8f, 0.2f, 0.16f));
            AddPrisonDoor("entrance", 1.6f, [.. leaves]);
        }

        // --- The sally door: a heavy steel leaf on the gatehouse side, sliding east along the wall. ---
        {
            const float zc = SouthOutZ + 0.045f, h = 2.24f, w = 1.22f;
            var leaf = SlidingLeaf(new float3(1.25f, 0, 0), JAt(EntranceX, h / 2, zc), new float3(w, h, 0.06f));
            var steel = leaf.B(Material.Plain, PrisonDoorBlue);
            OBox(steel, id, JAt(EntranceX, h / 2, zc), new float3(w, h, 0.06f));
            OBox(leaf.B(Material.Glass, tinted), id, JAt(EntranceX - 0.25f, 1.6f, zc + 0.032f), new float3(0.18f, 0.5f, 0.006f));
            OBox(leaf.B(Material.Plain, steelGrey), id, JAt(EntranceX - 0.5f, 1.05f, zc + 0.05f), new float3(0.04f, 0.4f, 0.04f));
            OBox(leaf.B(Material.Plain, steelGrey), id, JAt(EntranceX, 0.12f, zc + 0.032f), new float3(w - 0.06f, 0.2f, 0.004f));
            AddPrisonDoor("sally", 2.2f, leaf);
            // Its track over the opening and on along the wall, a red/green light and a card reader beside it.
            OBox(JB(Material.Steel, new float3(0.3f, 0.31f, 0.33f)), id, JAt(EntranceX + 0.65f, h + 0.08f, zc), new float3(2.8f, 0.14f, 0.12f));
            OBox(JB(Material.Plain, new float3(0.18f, 0.18f, 0.19f)), id, JAt(EntranceX - 0.85f, 1.35f, SouthOutZ + 0.03f), new float3(0.1f, 0.16f, 0.06f));
            OBox(JB(Material.Emissive, new float3(1.0f, 0.12f, 0.08f)), id, JAt(EntranceX - 0.85f, 2.45f, SouthOutZ + 0.04f), new float3(0.08f, 0.08f, 0.06f));
        }

        // --- The grille between the locker room and the corridor: fixed barred panels, a barred gate leaf sliding west. ---
        {
            const float zFixed = CourtZ + 0.15f, zGate = CourtZ - 0.06f, h = 2.4f;
            const float gx0 = 23.2f, gx1 = 24.4f;
            var bars = JB(Material.Steel, new float3(0.26f, 0.28f, 0.3f));
            foreach (var (x0, x1) in (ReadOnlySpan<(float, float)>)[(CorrX0, gx0), (gx1, CorrX1)])
            {
                for (var x = x0 + 0.07f; x < x1 - 0.03f; x += 0.12f)
                    OCyl(bars, id, JAt(x, 0, zFixed), JAt(x, h, zFixed), 0.016f, 0.016f, 8);
                foreach (var y in (ReadOnlySpan<float>)[0.04f, 1.1f, h - 0.04f])
                    OBox(bars, id, JAt((x0 + x1) / 2, y, zFixed), new float3(x1 - x0, 0.06f, 0.04f));
                OBox(bars, id, JAt(x1 - 0.03f, h / 2, zFixed), new float3(0.06f, h, 0.06f));
                AddStatic(new Aabb(JAt(x0, 0, zFixed - 0.03f), JAt(x1, h, zFixed + 0.03f)), default, visible: false, blocksShots: false);
            }
            var w = gx1 - gx0;
            var cx = (gx0 + gx1) / 2;
            var leaf = SlidingLeaf(new float3(-0.9f, 0, 0), JAt(cx, h / 2, zGate), new float3(w, h, 0.05f));
            var gb = leaf.B(Material.Plain, new float3(0.26f, 0.28f, 0.3f));
            for (var x = gx0 + 0.06f; x < gx1 - 0.03f; x += 0.12f)
                OCyl(gb, id, JAt(x, 0.02f, zGate), JAt(x, h - 0.02f, zGate), 0.016f, 0.016f, 8);
            foreach (var y in (ReadOnlySpan<float>)[0.05f, 1.1f, h - 0.05f])
                OBox(gb, id, JAt(cx, y, zGate), new float3(w, 0.07f, 0.04f));
            foreach (var x in (ReadOnlySpan<float>)[gx0 + 0.03f, gx1 - 0.03f])
                OBox(gb, id, JAt(x, h / 2, zGate), new float3(0.06f, h, 0.05f));
            OBox(leaf.B(Material.Plain, PrisonDoorBlue), id, JAt(gx1 - 0.15f, 1.1f, zGate), new float3(0.2f, 0.3f, 0.1f));   // lock box
            AddPrisonDoor("grille", 2.0f, leaf);
            OBox(JB(Material.Steel, new float3(0.3f, 0.31f, 0.33f)), id, JAt(23.3f, h + 0.07f, zGate), new float3(2.0f, 0.1f, 0.08f));
        }

        // --- The yard door: a steel leaf with a small window, sliding north along the corridor wall. ---
        {
            const float xc = CorrX0 + 0.1f, h = 2.24f, w = 1.3f;
            var leaf = SlidingLeaf(new float3(0, 0, -1.25f), JAt(xc, h / 2, YardDoorZ), new float3(0.06f, h, w));
            OBox(leaf.B(Material.Plain, PrisonDoorBlue), id, JAt(xc, h / 2, YardDoorZ), new float3(0.06f, h, w));
            OBox(leaf.B(Material.Glass, tinted), id, JAt(xc, 1.65f, YardDoorZ), new float3(0.07f, 0.4f, 0.3f));
            OBox(leaf.B(Material.Plain, steelGrey), id, JAt(xc + 0.05f, 1.05f, YardDoorZ + 0.45f), new float3(0.04f, 0.04f, 0.3f));
            AddPrisonDoor("yard", 2.2f, leaf);
            OBox(JB(Material.Steel, new float3(0.3f, 0.31f, 0.33f)), id, JAt(xc, h + 0.08f, YardDoorZ - 0.6f), new float3(0.12f, 0.14f, 2.6f));
        }

        // --- The player's locker: a full-height door hinged on its north edge, swinging out towards you. ---
        {
            var front = EastBankBack - 0.5f;
            const float w = 0.38f, h = 1.86f;
            var hinge = JAt(front, 0, PlayerLockerZ - 0.19f);
            var leaf = HingedLeaf(hinge, -1.95f, new float3(-0.008f, 0.17f + h / 2, w / 2), new float3(0.016f, h, w));
            var door = leaf.B(Material.Plain, LockerSteel * 1.08f);
            OBox(door, id, new float3(-0.008f, 0.17f + h / 2, w / 2), new float3(0.016f, h, w));
            var dark = leaf.B(Material.Plain, new float3(0.1f, 0.1f, 0.11f));
            for (var k = 0; k < 3; k++)
            {
                OBox(dark, id, new float3(-0.017f, 0.29f + k * 0.035f, w / 2), new float3(0.004f, 0.012f, 0.16f));
                OBox(dark, id, new float3(-0.017f, 1.8f + k * 0.035f, w / 2), new float3(0.004f, 0.012f, 0.16f));
            }
            OBox(leaf.B(Material.Plain, new float3(0.62f, 0.63f, 0.64f)), id, new float3(-0.03f, 1.1f, w - 0.06f), new float3(0.03f, 0.14f, 0.045f));
            OBox(leaf.B(Material.Plain, new float3(0.85f, 0.85f, 0.82f)), id, new float3(-0.018f, 1.95f, w / 2), new float3(0.003f, 0.035f, 0.07f));
            // A photo stuck inside the door.
            OBox(leaf.B(Material.Plain, new float3(0.7f, 0.6f, 0.45f)), id, new float3(0.002f, 1.5f, w / 2), new float3(0.004f, 0.14f, 0.1f));
            AddPrisonDoor("locker", 0.9f, leaf);
        }

        // --- The container's doors: two corrugated leaves with locking bars, hinged at its corners, swinging out
        // round against its sides (250 degrees, so the bars on their faces stand clear of the sides). ---
        {
            const float h = 2.5f, w = PcWidth / 2 - 0.02f;
            var leaves = new List<DoorLeaf>();
            foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            {
                // North leaf (side -1): hinged at the north-east corner, running south (+z) from it; turns positive.
                var hinge = JAt(PcEastX, 0.05f, side * PcWidth / 2);
                var dir = -side;                           // along the leaf from its hinge (in local z)
                var leaf = HingedLeaf(hinge, -side * float.DegreesToRadians(250f), new float3(-0.03f, h / 2, dir * w / 2), new float3(0.06f, h, w));
                var skin = leaf.B(Material.Plain, PcBlue);
                OBox(skin, id, new float3(-0.03f, h / 2, dir * w / 2), new float3(0.06f, h, w));
                // Its corrugations are modelled, not textured (a texture wouldn't move with the door).
                for (var t = 0.08f; t < w - 0.03f; t += 0.11f)
                {
                    OBox(skin, id, new float3(0.008f, h / 2, dir * t), new float3(0.016f, h - 0.1f, 0.055f));
                    OBox(skin, id, new float3(-0.068f, h / 2, dir * t), new float3(0.016f, h - 0.1f, 0.055f));   // (their inside face shows when they're open)
                }
                var bar = leaf.B(Material.Plain, PcBlue * 0.6f);
                foreach (var t in (ReadOnlySpan<float>)[0.3f, 0.85f])
                {
                    OCyl(bar, id, new float3(0.03f, 0.05f, dir * t), new float3(0.03f, h - 0.05f, dir * t), 0.022f, 0.022f, 8);
                    foreach (var y in (ReadOnlySpan<float>)[0.25f, h - 0.25f])
                        OBox(bar, id, new float3(0.03f, y, dir * t), new float3(0.05f, 0.1f, 0.1f));
                    OBox(bar, id, new float3(0.05f, 1.2f, dir * (t - 0.12f)), new float3(0.03f, 0.04f, 0.26f));   // cam handle
                }
                OBox(leaf.B(Material.Plain, PcBlue * 0.75f), id, new float3(-0.02f, h / 2, dir * 0.03f), new float3(0.08f, h, 0.06f));   // hinge stile
                leaves.Add(leaf);
            }
            AddPrisonDoor("container", 3.6f, [.. leaves]);
            // Lettering on the shut doors' faces is part of the leaves: a big "CAUTION" stencil.
        }

        // Everything starts open except the locker, so you can walk round the prison freely (the intro shuts them).
        SetAllPrisonDoors(true);
        SetPrisonDoor("locker", false, instant: true);
    }
}
