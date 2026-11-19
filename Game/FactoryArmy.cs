using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// On a factory raid: a hidden clock starts the moment the raid goes loud (the van drives off having dropped you, or, over
/// the fence, the moment any of the factory's crew spot you, are hit or killed, or a shot's fired). Four minutes later
/// (BRUTALFPS_ARMY_DELAY to change it) the army arrives up the road and in through the north gate: a column of four armoured
/// cars, each with a gunner up in its hatch on the M249 in its turret (<see cref="GunNest.Vehicle"/>), and three canvas-topped
/// army trucks (<see cref="ArmyTruck"/>), fifteen troops in the back of each, who jump down when it stops and push for the
/// hall's big door. The lead car rolls the big door open and drives on in through it, to park inside the hall; the rest
/// park nose to tail up the road behind it, all inside the gate. Nothing on screen counts it down.
/// <para>
/// Parked, each vehicle is solid: to the player and people's paths (a blocker), and to bullets and eyes (its hull in the
/// physics world, a car's with a hole round its hatch, as the outback car's is).
/// </para>
/// </summary>
public sealed class FactoryArmy
{
    public static readonly float Delay = float.TryParse(Environment.GetEnvironmentVariable("BRUTALFPS_ARMY_DELAY"), out var d) ? d : 240f;
    private const int TroopsPerTruck = 15;
    private const float Cruise = 9f, RoadX = -1.6f,   // (west of the road's middle: clear of the van parked by it at x 3.4, z -84)
        StartBehind = 110f;
    private const float CarHalfLength = 2.95f, CarHalfWidth = 1.23f, TruckNose = 3.9f;
    private const float InHall = -21f;                                    // where the lead car parks: inside, 9 m past the door
    private static readonly float DoorWait = -Level.HalfLength - 1f - 0.85f - 0.5f - CarHalfLength;   // its nose short of the shut door

    // Where each stops (its middle), front to back: the car inside, then truck, car, truck, car, truck, car up the road, nose to
    // tail with room behind each truck for its troops to drop off the tailgate. The last is just inside the gate (z -104).
    private static readonly float[] StopAt = [InHall, -43.9f, -56.45f, -66.4f, -78.95f, -88.9f, -100.6f];

    private sealed class Vehicle(int index, bool truck, float stopZ)
    {
        public readonly int Index = index;
        public readonly bool Truck = truck;
        public readonly float StopZ = stopZ;
        public float Z = stopZ - StartBehind;
        public float Speed = Cruise;
        public bool Parked;
        public GunNest? Turret;
        public BulletSharp.CollisionObject? Hull;
        public string Key => $"army vehicle {Index}";
    }

    private readonly Npcs _npcs;
    private readonly Level _level;
    private readonly FactoryRaid _raid;
    private readonly PhysicsWorld? _physics;
    private readonly int _hullSurface;
    private readonly (float Restitution, float Friction) _hullContact;
    private readonly Func<float3, float, GunNest?>? _makeTurret;
    private readonly ArmyTruck _truck = new();
    private readonly Engine.Model? _carHull, _carTurret;
    private readonly Mesh _carSolid = CarSolid(), _truckSolid = TruckSolid();
    private readonly List<Vehicle> _column = [];
    private float _clock = -1f;   // s since the raid went loud; < 0: it hasn't
    private bool _arrived;
    private int _goal;
    private int _respawns = int.MinValue;   // the player's respawn count this life began on
    private int _shotsBefore;               // the factory's gunshot count then: only shots since count as loud

    /// <param name="makeTurret">Makes a car's turret, its ring's middle at the point given, laid at rest that way (null: the cars have no guns that work).</param>
    public FactoryArmy(Npcs npcs, Level level, FactoryRaid raid, PhysicsWorld? physics = null, SurfaceTable? surfaces = null,
        Func<float3, float, GunNest?>? makeTurret = null)
    {
        _npcs = npcs;
        _level = level;
        _raid = raid;
        _physics = physics;
        var paint = SurfaceInfo.ForMaterial(Material.PaintedMetal, new float3(0.3f, 0.32f, 0.22f));
        _hullSurface = surfaces?.Index(paint) ?? 0;
        _hullContact = paint.Contact;
        _makeTurret = makeTurret;
        (_carHull, _carTurret) = Level.ArmyCarModels;
        Reset();
    }

    /// <summary>
    /// Back to before the raid went loud: no clock, no column. On a death the people are all put back as they started
    /// (<see cref="Npcs.ApplyRespawn"/>), which takes the army's troops away with everyone else, so the vehicles go too.
    /// </summary>
    private void Reset()
    {
        foreach (var v in _column)
        {
            _level.SetBlocker(v.Key, null);
            if (v.Hull is not null)
                _physics?.RemoveDoorPart(v.Hull);
            if (v.Turret is { } turret)
            {
                turret.RemovePhysics();
                _npcs.Senses.Nests.Remove(turret);
            }
        }

        _column.Clear();
        _level.FactoryDoorHeld = false;
        _clock = -1f;
        _arrived = false;
        _goal = 0;
        if (Delay < 0f)
        {
            // (BRUTALFPS_ARMY_DELAY=-1: the column's already parked, nobody in it, for looking at the vehicles.)
            Arrive();
            _level.SetFactoryDoor(true);
            foreach (var v in _column)
            {
                (v.Z, v.Parked) = (v.StopZ, true);
                Solidify(v);
            }

            _level.FactoryDoorHeld = false;
        }
    }

    /// <summary>Call once a frame, after the people have moved.</summary>
    public void Update(float dt, PlayerController player)
    {
        if (_respawns != player.Respawns)
        {
            if (_respawns != int.MinValue)
            {
                Reset();
                Log.Write("[ARMY] Respawn: the army's gone, and its clock waits for the raid to go loud again");
            }
            _respawns = player.Respawns;
            _shotsBefore = _npcs.FactoryGunshots;
        }

        foreach (var v in _column)
            v.Turret?.Update(dt, false, player.Position, false, out _);

        if (_clock < 0f)
        {
            if (_raid.Active || Delay < 0f)
                return;
            var why = _raid.VanLeaving ? "the van's driving off" : _npcs.FactoryDetection(_shotsBefore);
            if (why is null)
                return;
            _clock = 0f;
            Log.Write($"[ARMY] The raid's gone loud ({why}): the army's {Delay:0} s out");
            return;
        }

        if (!_arrived)
        {
            _clock += dt;
            if (_clock >= Delay)
                Arrive();
            return;
        }

        foreach (var v in _column.Where(v => !v.Parked))
        {
            // The lead car waits at the big door till it's rolled right open (the army opens it, and holds it till it's in).
            var stop = v.StopZ;
            if (v.Index == 0 && !_level.FactoryDoorFullyOpen && v.Z <= DoorWait + 0.01f)
            {
                _level.OpenFactoryDoor();
                stop = DoorWait;
            }

            var left = stop - v.Z;
            v.Speed = float.Min(Cruise, float.Max(v.Index == 0 && stop == v.StopZ ? 2.5f : 1.2f, left * 0.9f));
            v.Z = float.Min(v.Z + v.Speed * dt, stop);
            if (v.Z < v.StopZ)
                continue;
            v.Z = v.StopZ;
            v.Parked = true;
            if (v.Index == 0)
                _level.FactoryDoorHeld = false;
            Solidify(v);
            Unload(v);
        }
    }

    private void Arrive()
    {
        _arrived = true;
        _level.SetFactoryGate(true);
        _level.OpenFactoryDoor();
        _level.FactoryDoorHeld = true;   // (till the lead car's in: nobody shuts it on it)
        // Nose to tail up the road: car, truck, car, truck, car, truck, car.
        for (var i = 0; i < StopAt.Length; i++)
            _column.Add(new Vehicle(i, truck: i % 2 == 1, StopAt[i]));
        Log.Write($"[ARMY] {Delay:0} s after the raid went loud: the army's at the north gate (4 armoured cars, 3 trucks of {TroopsPerTruck}); the lead car's for the hall");
    }

    private static float3 At(Vehicle v, float3 local) => new(RoadX - local.X, local.Y, v.Z - local.Z);   // (facing +Z: the model turned half round)

    private static float4x4 World(Vehicle v) => float4x4.CreateRotationY(float.Pi) * float4x4.CreateTranslation(RoadX, 0, v.Z);

    /// <summary>Parked: in the way of the player and people's paths, and of bullets and eyes; a car's turret manned.</summary>
    private void Solidify(Vehicle v)
    {
        var (halfBack, halfFront, height, halfWidth) = v.Truck ? (ArmyTruck.TailZ, TruckNose, 3.3f, 1.3f) : (CarHalfLength, CarHalfLength, Level.ArmouredCarRoofHeight, CarHalfWidth);
        _level.SetBlocker(v.Key, new Aabb(new float3(RoadX - halfWidth, 0, v.Z - halfBack), new float3(RoadX + halfWidth, height, v.Z + halfFront)));
        if (_physics is not null)
            v.Hull = _physics.AddDoorPart(v.Truck ? _truckSolid : _carSolid, World(v), _hullSurface, _hullContact.Restitution, _hullContact.Friction);
        if (v.Truck || _makeTurret is null)
            return;
        const float facing = 0f;   // (laid at rest the way the car points: on up the road, at the hall, or up the hall from inside it)
        v.Turret = _makeTurret(At(v, new float3(0, Level.ArmouredCarRoofHeight, 0.30f)), facing);
        if (v.Turret is not null)
            _npcs.Senses.Nests.Add(v.Turret);
    }

    private void Unload(Vehicle v)
    {
        const float north = 0f;   // heading: up the road, into the grounds
        if (!v.Truck)
        {
            // In the hatch: the floor under them is the top of the hull's lower box (as the outback car's gunner's).
            var hatch = At(v, new float3(0, Level.ArmouredCarRoofHeight + Level.ArmouredCarCradleUp - GunNest.PintleHeight, 0.30f));
            _npcs.AddArmySoldier(hatch, v.Turret?.Facing ?? north, float3.Zero, gunner: true);
            Log.Write($"[ARMY] An armoured car's in at z {v.Z:0}{(v.Index == 0 ? " (inside the hall)" : "")}: its gunner's up on the turret");
            return;
        }

        var added = 0;
        for (var k = 0; k < TroopsPerTruck; k++)
        {
            // Down off the tailgate in three files, five deep, behind the truck.
            var drop = At(v, new float3((k % 3 - 1) * 1.1f, 0, ArmyTruck.TailZ + 1.0f + (k / 3) * 1.0f));
            var g = _goal++;
            var goal = new float3(-11.2f + (g % 15) * 1.6f, 0, -34f - (g / 15) * 2.0f);   // spread out in front of the hall's big door
            if (_npcs.AddArmySoldier(drop, north, goal, gunner: false) is not null)
                added++;
        }

        Log.Write($"[ARMY] A truck's in at z {v.Z:0}: {added} troops out of the back");
    }

    /// <summary>A car's hull for the physics world, in its own frame (the nose at -Z): solid up to the gunner's floor, the cabin above it round the hatch.</summary>
    private static Mesh CarSolid()
    {
        var b = new MeshBuilder();
        void Box(float x0, float x1, float y0, float y1, float z0, float z1) =>
            b.Box(new float3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new float3(x1 - x0, y1 - y0, z1 - z0));
        var floor = Level.ArmouredCarRoofHeight + Level.ArmouredCarCradleUp - GunNest.PintleHeight;
        const float half = 1.15f, hole = 0.45f, hz = 0.30f;
        var roof = Level.ArmouredCarRoofHeight;
        Box(-half - 0.08f, half + 0.08f, 0.30f, floor, -CarHalfLength, CarHalfLength);   // lower hull, bumpers, flares
        Box(-half, half, floor, 1.95f, -1.30f, -0.80f);                                    // up the windscreen
        Box(-half, half, floor, roof, -0.80f, hz - hole);                                  // the cab, ahead of the hatch
        Box(-half, half, floor, roof, hz + hole, 2.80f);                                   // the crew's space, behind it
        Box(-half, -hole, floor, roof, hz - hole, hz + hole);                              // either side of it
        Box(hole, half, floor, roof, hz - hole, hz + hole);
        return b.Build();
    }

    /// <summary>A truck for the physics world, in its own frame (the nose at -Z): the cab, and the bed under its tilt.</summary>
    private static Mesh TruckSolid()
    {
        var b = new MeshBuilder();
        b.Box(new float3(0, 1.85f, -2.6f), new float3(2.4f, 2.5f, 2.4f));                                     // the cab
        b.Box(new float3(0, 2.1f, (ArmyTruck.TailZ - 1.4f) / 2), new float3(2.5f, 2.3f, ArmyTruck.TailZ + 1.4f));   // the bed and its tilt
        b.Box(new float3(0, 0.8f, 0.1f), new float3(1.4f, 0.6f, 7.2f));                                       // chassis, under them
        return b.Build();
    }

    public void Draw(Renderer renderer, Zones zones)
    {
        foreach (var v in _column)
        {
            var world = World(v);
            if (v.Truck)
                _truck.Draw(renderer, world, v.Parked ? 0f : v.Z);
            else
            {
                if (_carHull is not null)
                    renderer.DrawModel(_carHull, world, float3.One);
                if (v.Turret is { } turret)
                    turret.Draw(renderer, zones);
                else if (_carTurret is not null)
                    renderer.DrawModel(_carTurret, float4x4.CreateTranslation(0, Level.ArmouredCarRoofHeight, 0.30f) * world, float3.One);
            }
        }
    }
}

/// <summary>
/// A 4x4 army truck, olive drab, with a flat-fronted cab and a canvas tilt over the back (built here from boxes and
/// cylinders). In its own frame the nose is towards -Z, the tailgate towards +Z, as the delivery van's is.
/// </summary>
public sealed class ArmyTruck
{
    public const float TailZ = 3.7f;
    private const float WheelRadius = 0.56f;
    private static readonly float3 Olive = new(0.25f, 0.29f, 0.18f), Canvas = new(0.30f, 0.34f, 0.22f), Dark = new(0.06f, 0.06f, 0.05f),
        Glass = new(0.08f, 0.1f, 0.11f), Tyre = new(0.05f, 0.05f, 0.05f), Hub = new(0.22f, 0.25f, 0.16f), Red = new(0.6f, 0.08f, 0.06f),
        Lamp = new(0.85f, 0.85f, 0.8f);
    private static readonly float3[] WheelsAt = [new(-1.08f, WheelRadius, -2.7f), new(1.08f, WheelRadius, -2.7f), new(-1.08f, WheelRadius, 2.0f), new(1.08f, WheelRadius, 2.0f)];

    private readonly (Mesh Mesh, float3 Tint)[] _parts;
    private readonly Mesh _tyre = new MeshBuilder().Cylinder(WheelRadius, -0.21f, 0.21f, 20).Build();
    private readonly Mesh _hub = new MeshBuilder().Cylinder(0.27f, -0.22f, 0.22f, 12).Build();

    public ArmyTruck()
    {
        var olive = new MeshBuilder();
        var canvas = new MeshBuilder();
        var dark = new MeshBuilder();
        var glass = new MeshBuilder();
        var red = new MeshBuilder();
        var lamp = new MeshBuilder();

        // Chassis rails, fuel tank and the step under the door.
        dark.Box(new float3(0, 0.95f, 0.1f), new float3(1.0f, 0.24f, 7.2f));
        olive.Box(new float3(-1.0f, 1.05f, -0.7f), new float3(0.5f, 0.5f, 1.1f));
        dark.Box(new float3(0, 0.62f, -3.0f), new float3(1.2f, 0.25f, 0.8f));   // (the front axle's diff)
        dark.Box(new float3(0, 0.62f, 2.0f), new float3(1.2f, 0.25f, 0.8f));
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            dark.Box(new float3(side * 1.16f, 0.95f, -1.95f), new float3(0.18f, 0.06f, 0.45f));

        // The cab: flat-fronted, the windscreen raked back a little, the doors with their windows.
        olive.Box(new float3(0, 1.55f, -2.75f), new float3(2.4f, 1.15f, 1.9f));
        olive.Box(new float3(0, 2.55f, -2.55f), new float3(2.3f, 0.95f, 1.6f));
        olive.Box(new float3(0, 3.06f, -2.55f), new float3(2.34f, 0.08f, 1.66f));                  // roof
        glass.Box(new float3(0, 2.6f, -3.36f), new float3(2.0f, 0.72f, 0.04f));                    // windscreen
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            glass.Box(new float3(side * 1.16f, 2.62f, -2.5f), new float3(0.03f, 0.6f, 0.95f));     // door windows
            olive.Box(new float3(side * 1.18f, 1.62f, -2.75f), new float3(0.45f, 0.1f, 1.5f));     // mudguards
            dark.Box(new float3(side * 1.25f, 2.75f, -3.15f), new float3(0.06f, 0.35f, 0.22f));    // mirrors
            lamp.Box(new float3(side * 0.9f, 1.35f, -3.72f), new float3(0.28f, 0.18f, 0.04f));     // headlamps
            olive.Box(new float3(side * 1.25f, 1.05f, 2.0f), new float3(0.4f, 0.1f, 1.3f));        // rear mudguards
            red.Box(new float3(side * 1.05f, 1.15f, TailZ + 0.03f), new float3(0.18f, 0.12f, 0.04f));  // tail lamps
        }

        dark.Box(new float3(0, 1.75f, -3.71f), new float3(1.3f, 0.35f, 0.03f));                    // grille
        olive.Box(new float3(0, 1.0f, -3.78f), new float3(2.5f, 0.3f, 0.22f));                     // bumper
        red.Box(new float3(-0.9f, 1.0f, -3.91f), new float3(0.3f, 0.2f, 0.06f));                   // the red tow hooks
        red.Box(new float3(0.9f, 1.0f, -3.91f), new float3(0.3f, 0.2f, 0.06f));

        // The back: a dropside bed, and the canvas tilt over its hoops.
        olive.Box(new float3(0, 1.32f, 1.15f), new float3(2.5f, 0.14f, 5.1f));
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            olive.Box(new float3(side * 1.22f, 1.72f, 1.15f), new float3(0.06f, 0.66f, 5.1f));
        olive.Box(new float3(0, 1.72f, TailZ - 0.03f), new float3(2.5f, 0.66f, 0.06f));            // tailgate
        olive.Box(new float3(0, 1.72f, -1.38f), new float3(2.5f, 0.66f, 0.06f));
        canvas.Box(new float3(0, 2.62f, 1.15f), new float3(2.56f, 1.2f, 5.14f));
        canvas.Box(new float3(0, 3.24f, 1.15f), new float3(2.3f, 0.1f, 5.1f));                     // the tilt's rounded-off top
        foreach (var z in (ReadOnlySpan<float>)[-0.3f, 1.15f, 2.6f])                                // straps down its sides
        {
            dark.Box(new float3(-1.29f, 2.4f, z), new float3(0.02f, 0.8f, 0.05f));
            dark.Box(new float3(1.29f, 2.4f, z), new float3(0.02f, 0.8f, 0.05f));
        }

        _parts = [(olive.Build(), Olive), (canvas.Build(), Canvas), (dark.Build(), Dark), (glass.Build(), Glass), (red.Build(), Red), (lamp.Build(), Lamp)];
    }

    /// <summary>Draws it at <paramref name="world"/>, its wheels turned <paramref name="travelled"/> metres' worth.</summary>
    public void Draw(Renderer renderer, in float4x4 world, float travelled)
    {
        foreach (var (mesh, tint) in _parts)
            renderer.DrawMesh(mesh, world, tint);
        var spin = float4x4.CreateRotationZ(-travelled / WheelRadius);
        foreach (var at in WheelsAt)
        {
            var wheel = spin * float4x4.CreateRotationY(float.Pi / 2) * float4x4.CreateTranslation(at) * world;
            renderer.DrawMesh(_tyre, wheel, Tyre);
            renderer.DrawMesh(_hub, float4x4.CreateTranslation(0, 0, at.X < 0 ? -0.01f : 0.01f) * wheel, Hub);
        }
    }
}
