using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>How the player gets into the factory on a raid (<see cref="StartMenu"/>, or BRUTALFPS_START=fence / van).</summary>
public enum FactoryEntry { None, Fence, Van }

/// <summary>
/// A raid on the factory: the world as ever, but the game starts outside the factory's wall, and you've chosen how you get in.
/// Over the fence, you walk up to the west wall from the scrub outside, climb it and drop down at the edge of the grounds. In
/// the van, you ride in the back of a delivery van up to the north gate, wait while it's unchained, go in past the sentry and
/// hear it shut behind you (there's no seeing out of the back); the van backs in side on to the hall beside its big door,
/// you all climb out of the back, and it drives off with its doors open, back out through the gate.
///
/// Like <see cref="PrisonIntro"/> this builds a <see cref="Cinematic"/> and answers its cues. Nobody can spot you until it's
/// over (<see cref="NpcSenses.PlayerConcealed"/>), and a death from then on brings you back where you got in, not at the HQ.
/// The van parked at the door is also the way to the police HQ and back (E at its back doors, E at the HQ's exit door): the
/// portal on the loading dock can't be, as it only opens onto blackness and you can't go in (<see cref="FactoryEvacuation"/>).
/// </summary>
public sealed class FactoryRaid
{
    private readonly Level _level;
    private readonly AudioEngine? _audio;
    private readonly DeliveryVan? _van;
    private readonly ClimbingHands? _hands;
    private readonly RaidOfficers? _officers;
    private PlayerController? _player;
    private bool _riding;
    private bool _shadowsChanged;

    public Cinematic Cinematic { get; }

    public bool Active => Cinematic.Active || _ride is { Active: true };

    /// <summary>The van's dropped everyone off and is driving away (or gone): the raid's loud (Game/FactoryArmy.cs).</summary>
    public bool VanLeaving => _van is { Leaving: true } or { Gone: true };

    // --- The way to the police HQ that isn't the portal: E at the back of the parked van, and E at the HQ's exit door to come back. ---
    private const float RideReach = 1.8f;   // m from the spot, on the flat
    private static readonly float3 VanBack = DeliveryVan.ParkedAt(new float3(0, 0, 3.6f));
    private static readonly float3 VanStandAt = DeliveryVan.ParkedAt(new float3(1.8f, 0, 4.6f));   // (off to the hall's side, to see past the van)
    private static readonly float3 VanDoors = DeliveryVan.ParkedAt(new float3(0, 0, 2.99f));
    private static readonly float3 VanLooksAt = new(0f, 2.2f, -Level.HalfLength);   // the hall's big door, along past the van's nose
    private static readonly float3 HqStandAt = Level.PoliceExitDoor + new float3(0, 0, 1.1f);
    private static readonly float3 HqLooksAt = Level.PoliceExitDoor + new float3(0, 1.6f, 7f);   // into the bullpen
    private Cinematic? _ride;

    /// <summary>What E does here, if it does anything: get in the van, or leave the HQ for it.</summary>
    public string? Prompt { get; private set; }

    /// <summary>The bulb in the van, for this frame: xyz where, w how strong (0: none).</summary>
    public float4 Light => _van?.Lamp ?? float4.Zero;

    /// <summary>Where the van's driving, while it is: people on foot wait rather than walk into it (<see cref="DeliveryVan.KeepClear"/>).</summary>
    public IReadOnlyList<float3> VanInTheWay => _van is not null && (_riding || _van.Leaving) ? _van.KeepClear : Array.Empty<float3>();

    /// <summary>The factory's shadow map is out of date (clears when read): the van has arrived.</summary>
    public bool ConsumeShadowsChanged()
    {
        var changed = _shadowsChanged;
        _shadowsChanged = false;
        return changed;
    }

    // --- Over the fence: the wall runs north-south at x = -86, and you come at it from the west. ---
    private static float3 Fence(float x, float y) => new(x, y, Level.FenceClimbZ);
    private static readonly float3 FenceStart = Fence(-93f, 0);
    private static readonly float3 FenceLanding = Fence(-85.1f, 0);
    private static readonly float3 FenceLooksAt = new(-22f, 4f, -25f);   // the hall's west side

    public FactoryRaid(Level level, AudioEngine? audio, FactoryEntry entry, Npcs? npcs = null)
    {
        _level = level;
        _audio = audio;
        if (entry == FactoryEntry.Van)
        {
            _van = new DeliveryVan();
            _van.ReachedGate += OnReachedGate;
            _van.PassedGate += () => { _level.SetFactoryGate(false); PlayAtGate(); if (_van.Leaving) Log.Write("[RAID] The van's out of the gate: it shuts"); };
            _van.Parked += () => Cinematic?.Signal("van:parked");
            _van.DroveAway += () => { _audio?.Cancel("van"); Log.Write("[RAID] The van's gone, out of the gate"); };
            if (npcs is not null)
                _officers = new RaidOfficers(npcs, _van);   // (three armed officers ride in with you, and get out first)
            Cinematic = BuildVan();
        }
        else
        {
            _hands = new ClimbingHands();
            Cinematic = BuildFence();
        }

        Cinematic.Cued += OnCue;
        Cinematic.Finished += OnFinished;
    }

    private Cinematic BuildFence()
    {
        var wallTop = Fence(Level.GroundsMin.X, Level.PerimeterHeight);
        return new Cinematic("Factory raid: over the fence")
            .Letterbox(true, 0).FadeOut(0)
            .Caption("The west wall  -  nobody watches this side", 4.5f)
            .Wait(0.7f).FadeIn(1.8f)
            .Walk(1.8f, Fence(-86.7f, 0))
            .Look(wallTop + new float3(0, -0.3f, 0), 0.5f)
            .Carried(true)
            // Knees bend, a jump, and the hands come down on the top of the wall.
            .Glide(0.3f, Fence(-86.7f, -0.38f), wallTop)
            .Cue("climb:jump")
            .Glide(0.3f, Fence(-86.6f, 0.72f), wallTop + new float3(0, 0.3f, 0), easeOut: true)
            .Cue("climb:grip")
            .Wait(0.2f)
            // Hauls up until the head's over it, a look down the far side, a leg over.
            .Glide(0.8f, Fence(-86.48f, 1.32f), Fence(-75f, 1.2f))
            .Cue("climb:over")
            .Glide(0.6f, Fence(-86.02f, 1.62f), FenceLanding + new float3(3.5f, 0.5f, 0))
            .Glide(0.45f, Fence(-85.6f, 1.3f))
            .Cue("climb:release")
            .Glide(0.32f, Fence(-85.4f, 0.75f))
            // Lets go. The legs take it, and up, looking at the factory.
            .Glide(0.3f, Fence(-85.2f, -0.6f), easeOut: false)
            .Cue("climb:land")
            .Glide(0.65f, FenceLanding, FenceLooksAt)
            .Wait(0.3f)
            .Letterbox(false, 0.8f).Wait(0.6f);
    }

    private Cinematic BuildVan()
    {
        var van = _van!;
        var outside = DeliveryVan.ParkedAt(new float3(0, 1.4f, 12f));
        return new Cinematic("Factory raid: in the van")
            .Letterbox(true, 0).FadeOut(0).Carried(true)
            .Cue("van:go")
            .Caption("In the back of the delivery", 4f)
            .Track(() => van.WorldPoint(DeliveryVan.RearDoors), 0.4f)
            .Wait(1.0f).FadeIn(2f)
            .WaitFor("van:gate", 25f)
            .Caption("The north gate  -  stay down", 3.5f)
            .WaitFor("van:parked", 90f)
            .Cue("van:ride over")
            .Wait(0.8f)
            .Cue("van:doors open")
            .Wait(0.5f)
            // The officers riding with you go first: out, a hop off the sill, and they wait either side of the doors.
            .Cue("van:officers out")
            .Wait(2.5f)
            // Up off the floor, down the load space, over the sill and down onto the grass.
            .Glide(0.8f, DeliveryVan.ParkedAt(new float3(0, DeliveryVan.FloorHeight, 0.3f)), outside)
            .Glide(1.15f, DeliveryVan.ParkedAt(new float3(0, DeliveryVan.FloorHeight, 2.3f)))
            .Glide(0.4f, DeliveryVan.ParkedAt(new float3(0, DeliveryVan.FloorHeight + 0.02f, 3.05f)))
            .Glide(0.26f, DeliveryVan.ParkedAt(new float3(0, -0.28f, 3.75f)), easeOut: false)
            .Cue("van:out")
            .Glide(0.5f, VanStandAt, VanLooksAt)
            // You're out: they take cover at the back of the van.
            .Cue("van:officers cover")
            .Wait(0.7f)
            .Letterbox(false, 0.8f).Wait(0.6f);
    }

    /// <summary>Everything as it was, and off you go.</summary>
    public void Start(PlayerController player)
    {
        _player = player;
        _level.SetFactoryGate(false, instant: true);
        if (_van is not null)
        {
            _level.ArrivingVanHidden = true;
            _level.ArrivingVanCastsShadow = false;
            _level.SetArrivingVanThere(false);   // (it's never more than stopped there for a moment: the raid draws it, and it drives off again)
            var exit = VanStandAt;
            _level.SetSpawn(exit, YawTowards(exit, VanLooksAt));
            player.Position = _van.PlayerFeet;
            player.Yaw = _van.Yaw + float.Pi;   // (facing the back doors)
            _officers?.Start(player);
        }
        else
        {
            _level.SetSpawn(FenceLanding, YawTowards(FenceLanding, FenceLooksAt));
            player.Position = FenceStart;
            player.Yaw = float.Pi / 2;          // east, towards the wall
        }
        player.Velocity = float3.Zero;
        player.Pitch = -0.06f;
        player.LeftThroughPortal();
        Cinematic.Start(player);
    }

    /// <summary>
    /// Killed (or respawned) on a raid: back just before the way in ends, with the people already brought back
    /// (<see cref="Npcs.ApplyRespawn"/> first). In the van: parked at the hall's door, the doors about to open, the officers
    /// in the back with you again. Over the fence: on top of the wall, about to drop down.
    /// </summary>
    public void RestartNearEnd(PlayerController player)
    {
        _player = player;
        _ride = null;   // (on the way to or from the HQ: that's off)
        _audio?.Cancel("van");
        _level.SetFactoryGate(false, instant: true);
        _level.SetFactoryDoor(false);   // (the hall's big door, shut as it was the first time)
        player.Velocity = float3.Zero;
        player.Pitch = -0.06f;
        player.LeftThroughPortal();
        if (_van is not null)
        {
            _riding = false;
            _van.Restore();
            _level.ArrivingVanHidden = true;
            _level.ArrivingVanCastsShadow = true;   // (parked)
            _shadowsChanged = true;
            player.Position = _van.PlayerFeet;
            player.Yaw = _van.Yaw + float.Pi;
            _officers?.Restart(player);
            Cinematic.Start(player);
            Cinematic.JumpTo(player, Cinematic.StepOfCue("van:doors open"));
        }
        else
        {
            player.Position = FenceStart;
            player.Yaw = float.Pi / 2;
            Cinematic.Start(player);
            Cinematic.JumpTo(player, Cinematic.StepOfCue("climb:over"));
            if (_hands is not null)
                _hands.Gripping = true;   // (they've hold of the top of the wall: the jump skipped putting them there)
        }
        Log.Write($"[RAID] The player's back near the end of the way in ({(_van is null ? "on the wall" : "in the van")})");
    }

    private static float YawTowards(float3 from, float3 to) => float.Atan2(to.X - from.X, -(to.Z - from.Z));

    public void Update(float dt, PlayerController player, Input input)
    {
        if (_van is not null && _riding)
        {
            _van.UpdateDoors(dt);
            _van.Update(dt);
            player.Position = _van.PlayerFeet;
            PlayEngine(dt, _van.Speed);
        }
        else
            UpdateVan(dt);
        Cinematic.Update(dt, player, input);
        _officers?.Update(dt, player);
        _ride?.Update(dt, player, input);
        _hands?.Follow(player);
    }

    /// <summary>Call once a frame when <see cref="Update"/> isn't called (the way in's over): the van's doors, and the van driving off.</summary>
    public void UpdateVan(float dt)
    {
        if (_van is null)
            return;
        _van.UpdateDoors(dt);
        if (!_van.Leaving)
            return;
        _van.Update(dt);
        PlayEngine(dt, _van.Speed, _van.WorldPoint(new float3(0, 0.8f, -2f)));   // (heard from outside now: where it is)
    }

    public void Skip(PlayerController player)
    {
        Cinematic.Skip(player);
        _ride?.Skip(player);
    }

    public void Draw(Renderer renderer, int2 framebufferSize)
    {
        Cinematic.Draw(renderer, framebufferSize);
        _ride?.Draw(renderer, framebufferSize);
    }

    /// <summary>Call once a frame: offers the ride when the player's at the van or the HQ's exit door, and sets off if they take it.</summary>
    public void UpdateWayToHq(bool usePressed, PlayerController player)
    {
        Prompt = null;
        if (Active || !player.Alive)
            return;
        var atVan = AtVan(player);
        if (!atVan && !Within(player.Position, Level.PoliceExitDoor))
            return;
        // (On a raid in the van it's driven off: it's radioed back for, the ride itself going by in the dark as ever.)
        var vanGone = _van is { Leaving: true } or { Gone: true };
        Prompt = atVan ? (vanGone ? "[E] Radio for the van - to the police HQ" : "[E] Get in the van - to the police HQ") : "[E] Out to the van - back to the factory";
        if (usePressed)
            SetOff(player, atVan ? HqStandAt : VanStandAt, atVan ? HqLooksAt : VanLooksAt, atVan ? "The police HQ" : "Back at the factory");
    }

    /// <summary>
    /// At the back of the parked van and turned towards it, or (once it's driven off on a raid in it) just where it was.
    /// </summary>
    public bool AtVan(PlayerController player)
    {
        if (Active || !player.Alive || !Within(player.Position, VanBack))
            return false;
        if (_van is { Leaving: true } or { Gone: true })
            return true;
        var (x, z) = (VanDoors.X - player.Position.X, VanDoors.Z - player.Position.Z);
        var away = float.Sqrt(x * x + z * z);
        return away < 0.3f || (x * float.Sin(player.Yaw) - z * float.Cos(player.Yaw)) / away > 0.35f;
    }

    private static bool Within(float3 at, float3 spot)
    {
        var (x, z) = (at.X - spot.X, at.Z - spot.Z);
        return float.Abs(at.Y - spot.Y) < 2.5f && x * x + z * z < RideReach * RideReach;
    }

    /// <summary>The screen goes black, the journey goes by on the soundtrack, and the player's standing at the other end.</summary>
    private void SetOff(PlayerController player, float3 to, float3 lookAt, string caption)
    {
        var respawns = player.Respawns;
        Sound("Vehicles/van_door", 0.8f);
        _ride = new Cinematic($"Factory raid: to {caption}")
            .FadeOut(0.55f)
            .Cue("ride:arrive")
            .Look(lookAt, 0f)
            .Caption(caption, 2.2f)
            .Wait(1.6f)
            .FadeIn(0.8f, hold: true);
        _ride.Cued += _ =>
        {
            if (!player.Alive || player.Respawns != respawns)
                return;   // (a stray bullet on the way to the van)
            player.Position = to;
            player.Velocity = float3.Zero;
            player.LeftThroughPortal();
            Log.Write($"[RAID] By van: {caption}");
            _audio?.Play(_audio.Get("Vehicles/van_engine"), 0.5f, 0.85f, tag: "van");
        };
        _ride.Finished += () => _audio?.Cancel("van");
        Prompt = null;
        _ride.Start(player);
    }

    /// <summary>What's only there for the raid: the van while it's on the move, the hands on the wall.</summary>
    public void DrawWorld(Renderer renderer, Zones zones)
    {
        if ((zones & Zones.Warehouse) == 0)
            return;
        _van?.Draw(renderer);
        _hands?.Draw(renderer);
    }

    private void OnReachedGate()
    {
        Cinematic.Signal("van:gate");
        _level.SetFactoryGate(true);
        PlayAtGate();
    }

    private void OnCue(string cue)
    {
        var instant = Cinematic.FastForwarding;
        switch (cue)
        {
            case "van:go":
                _riding = true;
                break;
            case "van:ride over":
                if (instant)
                    _level.SetFactoryGate(false, instant: true);
                _van?.Finish();
                _riding = false;
                _level.ArrivingVanCastsShadow = true;   // (stopped: the level's parked one casts its shadow, the raid draws it)
                _shadowsChanged = true;
                break;
            case "van:doors open":
                _van?.SetDoors(true, instant);
                Sound("Vehicles/van_door", 0.8f);
                break;
            case "van:officers out":
                _officers?.GetOut(instant);
                break;
            case "van:officers cover":
                _officers?.TakeCover(instant);
                break;
            case "van:out":
                Sound("Body/land", 0.55f);
                if (_van is not null)
                {
                    // Everyone's out: it drives off, its back doors still open, round onto the road and out of the gate.
                    _van.PlayerOut = true;
                    _van.Leave();
                    _level.ArrivingVanCastsShadow = false;
                    _shadowsChanged = true;
                    _engineIn = 0f;
                    Log.Write("[RAID] The van drives off");
                }
                break;
            case "climb:jump":
                Sound("Body/scrape", 0.5f);
                break;
            case "climb:grip":
                if (_hands is not null && !instant)
                    _hands.Gripping = true;
                Sound("Body/land", 0.45f, pitch: 1.5f);
                break;
            case "climb:over":
                Sound("Body/scrape", 0.65f);
                break;
            case "climb:release":
                if (_hands is not null)
                    _hands.Gripping = false;
                break;
            case "climb:land":
                Sound("Body/land", 0.7f);
                break;
        }
    }

    private void OnFinished()
    {
        if (_van is not { Leaving: true })
            _audio?.Cancel("van");   // (not while it's driving off: that's its engine)
        if (_hands is not null)
            _hands.Gripping = false;
        if (_van is not null && _player is not null)
            _officers?.Release(_player);
    }

    // ------------------------------------------------------------------------------------------------ sound

    private float _engineIn;

    /// <summary>The engine: the one clip over and over, higher with the speed.</summary>
    private void PlayEngine(float dt, float speed, float3? at = null)
    {
        _engineIn -= dt;
        if (_engineIn > 0 || _audio is null)
            return;
        _engineIn = 1.75f;
        if (at is { } where)
            _audio.PlayAt(_audio.Get("Vehicles/van_engine"), where, 0.9f, 0.72f + speed * 0.05f, referenceDistance: 6f, tag: "van");
        else
            _audio.Play(_audio.Get("Vehicles/van_engine"), 0.55f, 0.72f + speed * 0.05f, tag: "van");
    }

    private void PlayAtGate() => _audio?.PlayAt(_audio.Get("Vehicles/gate_chain"), new float3(0, 1.2f, Level.GroundsMin.Z), 1f, referenceDistance: 9f);

    private void Sound(string name, float gain, float pitch = 1f, float delay = 0f)
    {
        if (!Cinematic.FastForwarding)
            _audio?.Play(_audio.Get(name), gain, pitch, delay);
    }
}

/// <summary>
/// The delivery van, on the move: the Transit from outside, a lined load space round the player inside it (its own shell, so
/// the van's body doesn't have to have one), and the road it follows, from outside the north gate round to where it parks.
/// In the van's own frame the nose is towards -Z and the back doors are towards +Z, as in the model.
/// </summary>
public sealed class DeliveryVan
{
    public const float FloorHeight = 0.62f;
    private const float RoofHeight = 2.45f, HalfWidth = 0.88f, BulkheadZ = -0.78f, DoorsZ = 2.72f;

    /// <summary>In the van's frame: what the player keeps an eye on, where the back doors meet (they've no windows: there's no seeing out).</summary>
    public static readonly float3 RearDoors = new(0, 1.6f, DoorsZ);

    private static readonly float3 SeatFeet = new(0.34f, FloorHeight + 0.95f - PlayerController.EyeHeight, -0.32f);

    // The roads, in the world (x, z). In: through the gate and past the sentry just east of the middle (the parked vans on
    // the right), straight down the road towards the hall, and at the end of it round to the left, heading east along in
    // front of the hall's door until it's ReverseRun past where it parks; from there it backs straight in, its back to the
    // west (see Route). Out: once everyone's out, forwards to the east, round to the left onto the road, up it and out
    // through the gate, which opens for it and shuts behind it. The bushes keep off both (NearRoad, in
    // Level.BuildVegetation), nobody's beat stops on them, and whoever's walking waits for it to go by (KeepClear).
    private const float GateStopZ = -112f;       // coming in: outside the gate
    private const float LeaveGateStopZ = -97f;   // going out: inside it
    private const float PastGateZ = -92f, LeftGateZ = -118f;
    private const float GateWait = 4.2f;
    private const float ReverseRun = 11f, ReverseSpeed = 3.5f, ReverseWait = 0.5f;

    private static float2 ParkXZ => new(Level.ArrivingVanPark.X, Level.ArrivingVanPark.Z);

    /// <summary>A point from where it parks: <paramref name="across"/> to its right, <paramref name="ahead"/> towards its nose.</summary>
    private static float2 FromPark(float across, float ahead)
    {
        var yaw = float.DegreesToRadians(Level.ArrivingVanYawDegrees);
        var nose = new float2(float.Sin(yaw), -float.Cos(yaw));
        var right = new float2(float.Cos(yaw), float.Sin(yaw));
        return ParkXZ + right * across + nose * ahead;
    }

    private static readonly float2[] ArriveWaypoints =
    [
        new(0.8f, -150f), new(0.8f, -130f), new(0.8f, GateStopZ), new(0.8f, -96f), new(0.6f, -86f), new(0.8f, -76f), new(0.8f, -62f),
        new(0.6f, -50f), new(0.4f, -45.5f), new(1.2f, -41.6f), new(2.6f, -39.4f),   // round to the left, in line with where it parks
        FromPark(0f, ReverseRun),
    ];

    private static readonly float2[] LeaveWaypoints =
    [
        ParkXZ, FromPark(0f, 3.5f), new(-0.8f, -39.6f), new(0.6f, -42.2f), new(1.1f, -46.5f),   // round to the left, onto the road
        new(1.1f, -56f), new(1f, -70f), new(1f, -86f), new(1f, LeaveGateStopZ), new(1f, -112f), new(1f, -130f), new(1f, -165f),
    ];

    /// <summary>A line through the waypoints (Catmull-Rom), fine enough to measure distances along.</summary>
    private static float2[] Spline(float2[] waypoints)
    {
        var fine = new List<float2>();
        for (var i = 0; i < waypoints.Length - 1; i++)
        {
            var p0 = waypoints[int.Max(i - 1, 0)];
            var p1 = waypoints[i];
            var p2 = waypoints[i + 1];
            var p3 = waypoints[int.Min(i + 2, waypoints.Length - 1)];
            for (var s = 0; s < 40; s++)
                fine.Add(CatmullRom(p0, p1, p2, p3, s / 40f));
        }
        fine.Add(waypoints[^1]);
        return [.. fine];
    }

    private static readonly float2[][] Roads = [[.. Spline(ArriveWaypoints), ParkXZ], Spline(LeaveWaypoints)];

    /// <summary>True if (x, z) is within <paramref name="within"/> of the line the van's middle drives along, in or out (it's about 1 m to either side of that).</summary>
    public static bool NearRoad(float x, float z, float within)
    {
        var p = new float2(x, z);
        foreach (var road in Roads)
        {
            for (var i = 0; i < road.Length - 1; i++)
            {
                var (a, ab) = (road[i], road[i + 1] - road[i]);
                var along = float.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / float.Max(ab.X * ab.X + ab.Y * ab.Y, 1e-6f), 0f, 1f);
                if (Length(p - (a + ab * along)) < within)
                    return true;
            }
        }
        return false;
    }

    private const float SampleStep = 0.25f;
    private const float Cruise = 11f, Accelerate = 3.2f, Brake = 3.2f, Cornering = 3.4f;

    /// <summary>
    /// One drive: the road sampled evenly by distance, the speed it can take at each sample (the bends and the stops), where it
    /// stops at the gate, and (coming in) where it stops and backs straight up to where it parks.
    /// </summary>
    private sealed class Route
    {
        public readonly float2[] Samples;
        public readonly float[] Limit;
        public readonly int GateSample;
        public readonly int ReverseSample;   // -1: it doesn't back up
        public readonly bool Parks;          // it stops at the end (coming in), rather than driving on out of sight

        public Route(float2[] waypoints, bool comingIn)
        {
            var fine = Spline(waypoints);
            var even = new List<float2> { fine[0] };
            var travelled = 0f;
            for (var i = 1; i < fine.Length; i++)
            {
                var step = Length(fine[i] - fine[i - 1]);
                travelled += step;
                while (travelled >= SampleStep)
                {
                    var over = travelled - SampleStep;
                    var t = step > 1e-5f ? over / step : 0f;
                    even.Add(fine[i] + (fine[i - 1] - fine[i]) * t);
                    travelled = over;
                }
            }
            even.Add(fine[^1]);
            ReverseSample = -1;
            if (comingIn)
            {
                // ...and from the end of that, straight back to where it parks.
                ReverseSample = even.Count - 1;
                for (var d = SampleStep; d < ReverseRun; d += SampleStep)
                    even.Add(fine[^1] + (ParkXZ - fine[^1]) * (d / ReverseRun));
                even.Add(ParkXZ);
            }
            Samples = [.. even];
            Parks = comingIn;
            GateSample = comingIn ? Array.FindIndex(Samples, p => p.Y >= GateStopZ) : Array.FindIndex(Samples, p => p.Y <= LeaveGateStopZ);

            // How fast round each bend, then backwards from each stop so it brakes in time for them.
            Limit = new float[Samples.Length];
            for (var i = 0; i < Samples.Length; i++)
            {
                var a = Samples[int.Max(i - 8, 0)];
                var b = Samples[i];
                var c = Samples[int.Min(i + 8, Samples.Length - 1)];
                var turn = float.Abs(WrapAngle(Heading(b, c) - Heading(a, b)));
                var radius = 2f * float.Max(Length(c - b), 0.5f) / float.Max(turn, 1e-3f);
                Limit[i] = float.Min(Cruise, float.Sqrt(Cornering * radius));
            }
            if (ReverseSample >= 0)
            {
                for (var i = ReverseSample + 1; i < Samples.Length; i++)
                    Limit[i] = ReverseSpeed;
                Limit[ReverseSample] = 0f;
            }
            if (GateSample >= 0)
                Limit[GateSample] = 0f;
            if (Parks)
                Limit[^1] = 0f;
            for (var i = Samples.Length - 2; i >= 0; i--)
                Limit[i] = float.Min(Limit[i], float.Sqrt(Limit[i + 1] * Limit[i + 1] + 2f * Brake * SampleStep));
        }

        public float Length => (Samples.Length - 1) * SampleStep;
        public float GateAt => GateSample * SampleStep;
        public float ReverseAt => ReverseSample < 0 ? float.MaxValue : ReverseSample * SampleStep;

        public float2 Sample(float distance)
        {
            var at = float.Clamp(distance / SampleStep, 0f, Samples.Length - 1.001f);
            var i = (int)at;
            return Samples[i] + (Samples[i + 1] - Samples[i]) * (at - i);
        }

        /// <summary>It's been stopped for at the gate: no braking for it any more.</summary>
        public void ClearGate()
        {
            Limit[GateSample] = Cruise;
            for (var j = GateSample - 1; j >= 0 && Limit[j] < Cruise; j--)
                Limit[j] = Cruise;
        }
    }

    private Route _route = new(ArriveWaypoints, comingIn: true);
    private float _distance;
    private float _speed;
    private float _standing = -1f;         // standing: at the gate, changing into reverse, or pulling away
    private bool _pastGateTold;
    private bool _parked;
    private bool _leaving;
    private float _time;

    private readonly (Mesh Mesh, float3 Tint)[] _shell;
    private readonly (Mesh Mesh, float3 Tint, float Side)[] _doors;
    private readonly Engine.Model? _model, _plates;
    private readonly Engine.Model? _openModel;   // the body with the barn doors' skin cut out, for when they're open
    private float _doorsOpen, _doorsTarget;
    private bool _gone;

    /// <summary>The player has climbed out: the van's body is drawn from now on.</summary>
    public bool PlayerOut { get; set; }

    /// <summary>Driving off, out of the grounds (<see cref="Leave"/>).</summary>
    public bool Leaving => _leaving;

    /// <summary>Gone: it's driven off out of the gate and away.</summary>
    public bool Gone => _gone;

    public event Action? ReachedGate;
    public event Action? PassedGate;
    public event Action? Parked;
    public event Action? DroveAway;

    public float Speed => _speed;

    // The dim bulb on the roof of the load space (a raid's always at night): lights what's round it as a shot's flash does.
    private const float LampStrength = 3.2f;
    private static readonly float3 LampAt = new(0, RoofHeight - 0.08f, 0.9f);
    private static readonly float3 LampGlow = new(0.75f, 0.5f, 0.26f);
    private readonly Mesh _lamp = new MeshBuilder().Box(LampAt, new float3(0.1f, 0.03f, 0.2f)).Build();

    /// <summary>The bulb's light for this frame: xyz where, w how strong (0 once the player's out).</summary>
    public float4 Lamp => _gone || PlayerOut ? float4.Zero : new float4(WorldPoint(LampAt), LampStrength);

    public DeliveryVan()
    {
        (_model, _plates) = Level.ArrivingVanModels;
        _openModel = _model is null ? null : CutDoorway(_model);
        (_shell, _doors) = BuildInterior();
    }

    private static float Length(float2 v) => float.Sqrt(v.X * v.X + v.Y * v.Y);

    private static float Heading(float2 from, float2 to) => float.Atan2(to.X - from.X, -(to.Y - from.Y));

    private static float WrapAngle(float a)
    {
        while (a > float.Pi)
            a -= float.Tau;
        while (a < -float.Pi)
            a += float.Tau;
        return a;
    }

    private static float2 CatmullRom(float2 p0, float2 p1, float2 p2, float2 p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return ((p1 * 2f) + (p2 - p0) * t + (p0 * 2f - p1 * 5f + p2 * 4f - p3) * t2 + (p1 * 3f - p0 - p2 * 3f + p3) * t3) * 0.5f;
    }

    private static float ParkYaw => float.DegreesToRadians(Level.ArrivingVanYawDegrees);

    /// <summary>Which way its nose is pointing (the player's yaw).</summary>
    public float Yaw => _parked || _distance >= _route.ReverseAt
        ? ParkYaw   // (backing up: still pointing the way it parks)
        : _leaving && _distance < 0.5f
            ? ParkYaw   // (just pulling away)
            : Heading(_route.Sample(_distance - 2.2f), _route.Sample(float.Min(_distance + 1.4f, float.Min(_route.ReverseAt, _route.Length))));

    private float3 Ground => _parked ? Level.ArrivingVanPark : new float3(_route.Sample(_distance).X, 0, _route.Sample(_distance).Y);

    /// <summary>The body's shaking on its springs, more with the speed (and its engine running even when it stands).</summary>
    private float Bounce => _parked ? 0f : (0.006f + 0.0016f * _speed) * (float.Sin(_time * 21f) + 0.6f * float.Sin(_time * 33.7f));

    private float4x4 World => float4x4.CreateRotationY(-Yaw) * float4x4.CreateTranslation(Ground + new float3(0, Bounce, 0));

    /// <summary>A point in the van's frame, in the world.</summary>
    public float3 WorldPoint(float3 local) => float3.Transform(local, World);

    /// <summary>Where the player's feet go, seated on the floor with their back to the bulkhead.</summary>
    public float3 PlayerFeet => WorldPoint(SeatFeet);

    /// <summary>A point in the frame of the van where it ends up parked.</summary>
    public static float3 ParkedAt(float3 local) =>
        float3.Transform(local, float4x4.CreateRotationY(-ParkYaw) * float4x4.CreateTranslation(Level.ArrivingVanPark));

    private readonly List<float3> _keepClear = [];

    /// <summary>
    /// Points a metre apart along the road, from its back bumper to as far ahead as it'll be in the next few seconds: people
    /// on foot keep <see cref="NpcSenses.VehicleClearance"/> off them (<see cref="NpcSenses.Vehicle"/>). None while it's parked.
    /// </summary>
    public IReadOnlyList<float3> KeepClear => _keepClear;

    private void UpdateKeepClear()
    {
        _keepClear.Clear();
        if (_parked || _gone)
            return;
        var to = float.Min(_distance + 7f + _speed * 1.5f, _route.Length);
        for (var d = float.Max(_distance - 3f, 0f); d <= to; d += 1f)
            _keepClear.Add(new float3(_route.Sample(d).X, 0, _route.Sample(d).Y));
    }

    public void Update(float dt)
    {
        _time += dt;
        UpdateKeepClear();
        if (_parked || _gone)
            return;

        if (_standing > 0f)
        {
            _standing -= dt;
            return;
        }

        var limits = _route.Limit;
        var i = (int)float.Clamp(_distance / SampleStep, 0f, limits.Length - 1f);
        var limit = float.Max(limits[i], 0.35f);   // (creeps in to the mark, rather than stopping short of it)
        _speed = _speed < limit ? float.Min(_speed + Accelerate * dt, limit) : float.Max(_speed - Brake * 2f * dt, limit);
        var next = _distance + _speed * dt;

        var gateAt = _route.GateAt;
        if (_route.GateSample >= 0 && _distance < gateAt && next >= gateAt)
        {
            _distance = gateAt;
            _speed = 0f;
            _standing = GateWait;
            _route.ClearGate();   // (it's been stopped for: on from here)
            ReachedGate?.Invoke();
            return;
        }

        var reverseAt = _route.ReverseAt;
        if (_distance < reverseAt && next >= reverseAt)
        {
            _distance = reverseAt;
            _speed = 0f;
            _standing = ReverseWait;
            limits[_route.ReverseSample] = ReverseSpeed;   // (and back from here)
            return;
        }

        if (next >= _route.Length)
        {
            if (_leaving)
            {
                _gone = true;
                _leaving = false;
                _keepClear.Clear();
                DroveAway?.Invoke();
            }
            else
                Finish();
            return;
        }

        _distance = next;
        var z = _route.Sample(_distance).Y;
        if (!_pastGateTold && (_leaving ? z < LeftGateZ : z > PastGateZ))
        {
            _pastGateTold = true;
            PassedGate?.Invoke();
        }
    }

    /// <summary>At the end of the road in, parked.</summary>
    public void Finish()
    {
        if (_parked || _leaving || _gone)
            return;
        _parked = true;
        _keepClear.Clear();
        _distance = _route.Length;
        _speed = 0f;
        Parked?.Invoke();
    }

    /// <summary>Everyone's out: it pulls away, round onto the road and out through the gate (<see cref="DroveAway"/> once it's gone).</summary>
    public void Leave()
    {
        if (!_parked)
            return;
        _route = new Route(LeaveWaypoints, comingIn: false);
        _parked = false;
        _leaving = true;
        _pastGateTold = false;
        _distance = 0f;
        _speed = 0f;
        _standing = 0.9f;   // (into gear)
    }

    /// <summary>The van's stopped being this and is just the one parked at the hall's door (<see cref="Level.ArrivingVanHidden"/>).</summary>
    public void Retire() => _gone = true;

    /// <summary>Back as it is the moment it's parked, with the player still inside and its doors shut (a restart: <see cref="FactoryRaid.RestartNearEnd"/>).</summary>
    public void Restore()
    {
        _route = new Route(ArriveWaypoints, comingIn: true);
        _leaving = false;
        _gone = false;
        _parked = true;   // (as Finish, but without telling anyone: it's not just arrived)
        _pastGateTold = true;
        _keepClear.Clear();
        _distance = _route.Length;
        _speed = 0f;
        PlayerOut = false;
        SetDoors(false, instant: true);
    }

    /// <summary>The back doors swing in about 0.6 s.</summary>
    public void UpdateDoors(float dt)
    {
        var step = dt * 1.6f;
        _doorsOpen = _doorsOpen < _doorsTarget ? float.Min(_doorsOpen + step, _doorsTarget) : float.Max(_doorsOpen - step, _doorsTarget);
    }

    public void SetDoors(bool open, bool instant)
    {
        _doorsTarget = open ? 1f : 0f;
        if (instant)
            _doorsOpen = _doorsTarget;
    }

    public void Draw(Renderer renderer)
    {
        if (_gone)
            return;
        var world = World;
        var paint = Level.ArrivingVanPaint;
        // (From inside there's only the shell to see: the body's trim would show through the open doors.)
        var body = _doorsOpen > 0.01f ? _openModel : _model;
        if (PlayerOut && body is not null)
            renderer.DrawModel(body, world, paint);
        if (PlayerOut && _plates is not null)
            renderer.DrawModel(_plates, world, paint);
        // (Once you're out its bulb's off and the roof's over it: dim inside, not lit like the yard.)
        var inside = PlayerOut ? 0.22f : 1f;
        foreach (var (mesh, tint) in _shell)
            renderer.DrawMesh(mesh, world, tint * inside);
        if (!PlayerOut)
            renderer.DrawMesh(_lamp, world, LampGlow, ShadeMode.Unlit);

        var open = _doorsOpen * _doorsOpen * (3f - 2f * _doorsOpen);
        foreach (var (mesh, tint, side) in _doors)
        {
            var hinge = float4x4.CreateRotationY(side * 2.75f * open) * float4x4.CreateTranslation(side * HalfWidth, 0, DoorsZ) * world;
            renderer.DrawMesh(mesh, hinge, tint);
        }
    }

    /// <summary>
    /// The van's body without what covers the doorway at the back (the model's barn doors are part of its skin): the
    /// triangles whose middles are in it go, so the load space shows through with the doors (<see cref="BuildInterior"/>) open.
    /// </summary>
    private static Engine.Model CutDoorway(Engine.Model model)
    {
        static bool InDoorway(float3 p) =>
            float.Abs(p.X) < HalfWidth + 0.02f && p.Y > FloorHeight - 0.03f && p.Y < RoofHeight + 0.02f && p.Z > DoorsZ - 0.1f;
        var parts = new List<(string, Mesh, PartMaterial)>();
        foreach (var part in model.Parts)
        {
            var v = part.SharedVertices;
            var stride = Mesh.FullStride;
            var indices = part.Indices ?? Enumerable.Range(0, v.Length / stride).Select(i => (uint)i).ToArray();
            float3 At(uint i) => new(v[i * stride], v[i * stride + 1], v[i * stride + 2]);
            var kept = new List<uint>(indices.Length);
            for (var t = 0; t + 2 < indices.Length; t += 3)
            {
                if (InDoorway((At(indices[t]) + At(indices[t + 1]) + At(indices[t + 2])) / 3f))
                    continue;
                kept.Add(indices[t]);
                kept.Add(indices[t + 1]);
                kept.Add(indices[t + 2]);
            }
            if (kept.Count > 0)
                parts.Add((part.Name, kept.Count == indices.Length ? part.Mesh : new Mesh(v, stride, [.. kept]), part.Material));
        }
        return Engine.Model.FromMeshes(parts);
    }

    /// <summary>
    /// The lining, the racking, a load strapped down, and the back doors (solid: no windows). All plain colour: the textured
    /// materials are mapped in world space, so they'd crawl across the walls as the van drove along.
    /// </summary>
    private static ((Mesh, float3)[], (Mesh, float3, float)[]) BuildInterior()
    {
        var ply = new MeshBuilder();
        var ribs = new MeshBuilder();
        var floor = new MeshBuilder();
        var card = new MeshBuilder();
        var cardDark = new MeshBuilder();
        var strap = new MeshBuilder();
        var y0 = FloorHeight;
        var y1 = RoofHeight;
        var midY = (y0 + y1) / 2;
        var midZ = (BulkheadZ + DoorsZ) / 2;
        var length = DoorsZ - BulkheadZ;

        floor.Box(new float3(0, y0 - 0.04f, midZ), new float3(2f * HalfWidth + 0.1f, 0.08f, length + 0.04f));
        foreach (var x in (ReadOnlySpan<float>)[-0.6f, -0.3f, 0f, 0.3f, 0.6f])
            ribs.Box(new float3(x, y0 + 0.006f, midZ), new float3(0.03f, 0.012f, length - 0.04f));   // chequer strips along the floor

        ply.Box(new float3(0, y1 + 0.03f, midZ), new float3(2f * HalfWidth + 0.1f, 0.06f, length + 0.04f));          // the roof lining
        ply.Box(new float3(0, midY, BulkheadZ - 0.03f), new float3(2f * HalfWidth + 0.1f, y1 - y0 + 0.1f, 0.06f));   // the bulkhead behind the cab
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            ply.Box(new float3(side * (HalfWidth + 0.03f), midY, midZ), new float3(0.06f, y1 - y0 + 0.1f, length + 0.04f));
            // Steel ribs up the walls and over the roof, a rail along each side.
            for (var z = BulkheadZ + 0.42f; z < DoorsZ - 0.1f; z += 0.72f)
            {
                ribs.Box(new float3(side * (HalfWidth - 0.012f), midY, z), new float3(0.024f, y1 - y0, 0.07f));
                ribs.Box(new float3(0, y1 - 0.012f, z), new float3(2f * HalfWidth, 0.024f, 0.07f));
            }
            ribs.Box(new float3(side * (HalfWidth - 0.02f), y0 + 0.62f, midZ), new float3(0.04f, 0.05f, length - 0.1f));
        }

        // The load, strapped against the left wall and stacked by the bulkhead, leaving a way down the middle.
        card.Box(new float3(-0.56f, y0 + 0.3f, 1.3f), new float3(0.6f, 0.6f, 0.7f));
        cardDark.Box(new float3(-0.58f, y0 + 0.82f, 1.25f), new float3(0.52f, 0.44f, 0.58f));
        card.Box(new float3(-0.55f, y0 + 0.26f, 0.5f), new float3(0.62f, 0.52f, 0.62f));
        cardDark.Box(new float3(-0.58f, y0 + 0.28f, -0.4f), new float3(0.56f, 0.56f, 0.66f));
        card.Box(new float3(-0.56f, y0 + 0.82f, -0.42f), new float3(0.5f, 0.48f, 0.6f));
        cardDark.Box(new float3(0.62f, y0 + 0.2f, 2.1f), new float3(0.46f, 0.4f, 0.8f));
        strap.Box(new float3(-0.27f, y0 + 0.55f, 0.9f), new float3(0.02f, 1.1f, 0.05f));
        strap.Box(new float3(-0.56f, y0 + 1.06f, 0.9f), new float3(0.6f, 0.02f, 0.05f));
        foreach (var b in (ReadOnlySpan<float>)[0.5f, 1.3f])
            strap.Box(new float3(-0.56f, y0 + 0.62f, b), new float3(0.65f, 0.025f, 0.04f));

        (Mesh, float3)[] shell =
        [
            (ply.Build(), new float3(0.16f, 0.14f, 0.11f)),
            (ribs.Build(), new float3(0.10f, 0.10f, 0.10f)),
            (floor.Build(), new float3(0.09f, 0.085f, 0.08f)),
            (card.Build(), new float3(0.25f, 0.18f, 0.11f)),
            (cardDark.Build(), new float3(0.17f, 0.125f, 0.08f)),
            (strap.Build(), new float3(0.28f, 0.11f, 0.05f)),
        ];

        // A door each side, built out from its hinge: a solid steel panel (no windows: you can't see out of the back), with
        // stiffening rails across its inside and a locking rod up its meeting edge. The right-hand door carries a strip
        // that laps over the join, so there's no chink of the outside between them either.
        var doors = new List<(Mesh, float3, float)>();
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var skin = new MeshBuilder();
            var rails = new MeshBuilder();
            skin.Box(new float3(-side * HalfWidth / 2, midY, 0), new float3(HalfWidth, y1 - y0, 0.05f));
            if (side > 0)
                skin.Box(new float3(-side * (HalfWidth - 0.005f), midY, -0.03f), new float3(0.07f, y1 - y0, 0.012f));
            foreach (var v in (ReadOnlySpan<float>)[y0 + 0.12f, midY, y1 - 0.12f])
                rails.Box(new float3(-side * 0.42f, v, -0.032f), new float3(0.7f, 0.07f, 0.016f));
            rails.Box(new float3(-side * 0.74f, midY, -0.045f), new float3(0.022f, y1 - y0 - 0.06f, 0.022f));
            rails.Box(new float3(-side * 0.7f, y0 + 0.78f, -0.05f), new float3(0.14f, 0.03f, 0.03f));   // its handle
            doors.Add((skin.Build(), new float3(0.18f, 0.18f, 0.17f), side));
            doors.Add((rails.Build(), new float3(0.11f, 0.11f, 0.11f), side));
        }

        return (shell, [.. doors]);
    }
}

/// <summary>Two gloved hands on top of the perimeter wall, and the arms going from them to the climber's shoulders.</summary>
public sealed class ClimbingHands
{
    private static readonly float3[] Wrists = [new(-86.12f, Level.PerimeterHeight + 0.04f, Level.FenceClimbZ - 0.26f), new(-86.12f, Level.PerimeterHeight + 0.04f, Level.FenceClimbZ + 0.26f)];
    private static readonly float3 Glove = new(0.055f, 0.05f, 0.045f);
    private static readonly float3 Sleeve = new(0.11f, 0.12f, 0.09f);

    private readonly Mesh _gloves;
    private readonly Mesh _arm;
    private float3 _eye, _right;

    public bool Gripping { get; set; }

    public ClimbingHands()
    {
        var b = new MeshBuilder();
        const float top = Level.PerimeterHeight, near = -86.075f, far = -85.925f;
        foreach (var (wrist, inward) in ((float3, float)[])[(Wrists[0], 1f), (Wrists[1], -1f)])
        {
            var z = wrist.Z;
            b.Box(new float3(near + 0.01f, top + 0.032f, z), new float3(0.13f, 0.062f, 0.098f));       // the back of the hand
            for (var i = 0; i < 4; i++)
            {
                var fingerZ = z + (i - 1.5f) * 0.0245f;
                b.Box(new float3(near + 0.1f, top + 0.024f, fingerZ), new float3(0.1f, 0.03f, 0.021f));
                b.Box(new float3(far - 0.005f, top + 0.008f, fingerZ), new float3(0.03f, 0.04f, 0.02f));
                b.Box(new float3(far + 0.012f, top - 0.04f, fingerZ), new float3(0.022f, 0.075f, 0.02f));   // hooked down over the far edge
            }
            b.Box(new float3(near + 0.03f, top + 0.022f, z + inward * 0.062f), new float3(0.09f, 0.03f, 0.026f));   // the thumb
            b.Box(wrist + new float3(0.03f, -0.005f, 0), new float3(0.08f, 0.075f, 0.082f));             // the cuff
        }
        _gloves = b.Build();
        _arm = new MeshBuilder().Cylinder(1f, 0f, 1f, 12).Build();
    }

    public void Follow(PlayerController player)
    {
        _eye = player.EyePosition;
        _right = new float3(float.Cos(player.Yaw), 0, float.Sin(player.Yaw));
    }

    public void Draw(Renderer renderer)
    {
        if (!Gripping)
            return;
        renderer.DrawMesh(_gloves, float4x4.Identity, Glove);
        // From each wrist towards that shoulder, stopping short of the eye.
        for (var i = 0; i < 2; i++)
        {
            var side = float3.Dot(Wrists[i] - _eye, _right) >= 0 ? 1f : -1f;
            var shoulder = _eye + _right * (side * 0.3f) - new float3(0, 0.34f, 0);
            var d = shoulder - Wrists[i];
            var length = float.Sqrt(float3.Dot(d, d));
            if (length < 1e-3f)
                continue;
            var dir = d / length;
            var model = float4x4.CreateScale(0.04f, 0.04f, length)
                        * float4x4.CreateRotationX(-float.Asin(float.Clamp(dir.Y, -1f, 1f)))
                        * float4x4.CreateRotationY(float.Atan2(dir.X, dir.Z))
                        * float4x4.CreateTranslation(Wrists[i]);
            renderer.DrawMesh(_arm, model, Sleeve);
        }
    }
}
