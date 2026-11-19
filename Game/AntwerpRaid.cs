using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>How the player gets into the Antwerp yard on a raid (<see cref="StartMenu"/>, or BRUTALFPS_START=boat / abseil).</summary>
public enum AntwerpEntry { None, Boat, Abseil }

/// <summary>
/// A raid on the container yard at Antwerp: the whole world as ever, but the game starts with the way in you chose.
/// <list type="bullet">
/// <item>By boat (by day): you ride a rigid inflatable across the dock with three of your own, it comes alongside the quay
/// north-west of the yard, you step off onto the landing ledge and the script lets go of you there: they go on up the
/// ladders, you climb up after them, and nobody up top knows you're there until you do (<see cref="Concealing"/>). Up
/// top they take cover by the ladders, and from then on they come with you (<see cref="BoatTeam"/>) and fight. A few more of the yard's crew walk the open quay there
/// (<see cref="Npcs.AddQuayGuards"/>): you have to fight your way in.</item>
/// <item>Down the rope (by night): on your own, on top of the four-high stacks along the yard's east edge. You go back over
/// the edge on a rope and bound down into the dead end of the lane below, between them and the stacks under the crane,
/// and nobody's seen you.</item>
/// </list>
/// Like <see cref="FactoryRaid"/> this builds a <see cref="Cinematic"/> and answers its cues. Nobody can spot you until it's
/// over (<see cref="NpcSenses.PlayerConcealed"/>), and a death from then on brings you back near the end of the way in.
/// </summary>
public sealed class AntwerpRaid
{
    private readonly Level _level;
    private readonly AudioEngine? _audio;
    private readonly RaidBoat? _boat;
    private readonly BoatTeam? _team;
    private readonly AbseilRope? _rope;
    private PlayerController? _player;
    private bool _riding;

    public AntwerpEntry Entry { get; }

    /// <summary>Night in the yard (chosen on the raid's screen, or as last played): only the captions care.</summary>
    private static bool Night => TimeOfDay.IsNight(DayNightLevel.Yard);
    public Cinematic Cinematic { get; }
    public bool Active => Cinematic.Active;

    // --- By boat: it ties up nose to the north against the landing ledge at the foot of the quay, between its two ladders. ---
    private static float3 Yard(float x, float y, float z) => Level.YardOrigin + new float3(x, y, z);
    private static readonly float3 QuayLanding = new(Level.QuayFaceX + 1.6f, 0, Level.YardOrigin.Z + Level.QuayLadderZs[0]);
    private static readonly float3 QuayLooksAt = Yard(-8f, 1.6f, 30f);   // over the open quay towards the crane
    private static float3 LedgeLanding => new(Level.LedgeEdgeX + 0.55f, Level.LedgeTop, Level.YardOrigin.Z + Level.QuayLadderZs[0] - 1.1f);

    /// <summary>
    /// Nobody can know you're there yet: still on the way in, or (by boat) down on the ledge under the quay's edge, out of
    /// sight of the quay.
    /// </summary>
    public bool Concealing => Cinematic.Active
        || (_boat is not null && _player is { Alive: true } p && p.Position.X < Level.QuayFaceX + 0.2f && p.Position.Y < -0.3f);

    // --- Down the rope: from the top of the east stacks into the lane's dead end. ---
    private static float Top => Level.EastStacksTop;
    private static float Face => Level.EastStacksFaceX;
    private static float RopeZ => Level.AbseilZ;
    private static float3 RopeStart => new(Face + 1.7f, Top, RopeZ);
    private static float3 RopeLanding => new(Face - 1.4f, 0, RopeZ - 0.6f);
    private static float3 RopeLooksAt => new(Face - 1.8f, 1.5f, RopeZ - 16f);   // down the lane, south

    public AntwerpRaid(Level level, AudioEngine? audio, AntwerpEntry entry, Npcs? npcs = null)
    {
        _level = level;
        _audio = audio;
        Entry = entry;
        if (entry == AntwerpEntry.Boat)
        {
            _boat = new RaidBoat();
            _boat.Moored += () => Cinematic?.Signal("boat:alongside");
            if (npcs is not null)
            {
                npcs.AddQuayGuards();
                _team = new BoatTeam(npcs, _boat);
            }
            Cinematic = BuildBoat();
        }
        else
        {
            _rope = new AbseilRope();
            Cinematic = BuildAbseil();
        }
        Cinematic.Cued += OnCue;
        Cinematic.Finished += OnFinished;
    }

    private Cinematic BuildBoat()
    {
        var boat = _boat!;
        var ladder = Level.QuayLadderFoot(0);   // (the north one: the player's, with one of the team; the other two take the south one)
        var face = Level.QuayFaceX;
        return new Cinematic("Antwerp raid: by boat")
            .Letterbox(true, 0).FadeOut(0).Carried(true)
            .Cue("boat:go")
            .Caption(Night ? "Antwerp  -  the Scheldt, 02:40" : "Antwerp  -  the Scheldt, first light", 4f)
            .Track(() => boat.WorldPoint(new float3(0, 1.2f, -30f)), 0.4f)
            .Wait(0.8f).FadeIn(2f)
            .Wait(5f)
            .Caption("The yard's quay  -  keep low", 3.5f)
            .WaitFor("boat:alongside", 45f)
            .Cue("boat:moored")
            .Look(new float3(ladder.X, 0.6f, ladder.Z), 0.8f)
            .Wait(0.5f)
            // The team step off onto the ledge: one to your ladder, ahead of you, the other two to the south one.
            .Cue("team:up")
            .Wait(1.0f)
            // Up off the seat and a step across onto the ledge, looking along it to the ladder: from there it's yours (the
            // team go on up the ladders while you get your bearings).
            .Glide(0.6f, LedgeLanding, new float3(face, 0.4f, ladder.Z))
            .Cue("ledge:land")
            .Letterbox(false, 0.6f).Wait(0.3f);
    }

    private Cinematic BuildAbseil()
    {
        float3 At(float x, float y) => new(x, y, RopeZ);
        var c = new Cinematic("Antwerp raid: down the rope")
            .Letterbox(true, 0).FadeOut(0)
            .Caption(Night ? "Antwerp  -  03:10, on top of the east stacks" : "Antwerp  -  14:20, on top of the east stacks", 4.5f)
            .Wait(0.7f).FadeIn(2f)
            // A look down into the lane, then round to the rope.
            .Look(new float3(Face - 1.5f, 0, RopeZ - 7f), 1.3f)
            .Wait(0.9f)
            .Carried(true)
            .Look(new float3(Face + 1.0f, Top - 0.2f, RopeZ), 1.0f)
            .Cue("rope:clip")
            .Wait(0.7f)
            // Backs up to the edge facing the rope, and leans out over it, feet on the end of the container.
            .Glide(1.3f, At(Face + 0.25f, Top), At(Face + 2.6f, Top + 0.6f))
            .Cue("rope:over")
            .Glide(0.9f, At(Face - 0.75f, Top - 1.35f), At(Face + 0.4f, Top + 0.1f));
        // Down in bounds: a drop on the rope, the feet back on the steel, and again.
        foreach (var to in (ReadOnlySpan<float>)[6.8f, 4.3f, 1.9f])
        {
            c.Cue("rope:zip")
             .Glide(0.55f, At(Face - 0.85f, to), At(Face, to + 1.3f), easeOut: false)
             .Glide(0.3f, At(Face - 0.55f, to - 0.1f), At(Face, to + 1.2f))
             .Wait(0.15f);
        }
        return c.Cue("rope:zip")
            .Glide(0.6f, At(Face - 0.6f, 0), At(Face, 1.4f), easeOut: false)
            .Cue("rope:land")
            .Wait(0.35f)
            .Cue("rope:off")
            .Wait(0.5f)
            .Glide(0.8f, RopeLanding, RopeLooksAt)
            .Wait(0.3f)
            .Letterbox(false, 0.8f).Wait(0.6f);
    }

    /// <summary>Everything as it was, and off you go.</summary>
    public void Start(PlayerController player)
    {
        _player = player;
        if (_boat is not null)
        {
            _level.SetSpawn(QuayLanding, YawTowards(QuayLanding, QuayLooksAt));
            player.Position = _boat.PlayerFeet;
            player.Yaw = _boat.Yaw;
            _team?.Start(player);
        }
        else
        {
            _level.SetSpawn(RopeLanding, YawTowards(RopeLanding, RopeLooksAt));
            player.Position = RopeStart;
            player.Yaw = -float.Pi / 2;   // west, over the lane
        }
        player.Velocity = float3.Zero;
        player.Pitch = -0.06f;
        player.LeftThroughPortal();
        Cinematic.Start(player);
    }

    /// <summary>
    /// Killed (or respawned) on the raid: back just before the way in ends, with the people already brought back
    /// (<see cref="Npcs.ApplyRespawn"/> first). By boat: alongside, the team in the boat with you again. Down the rope:
    /// going back over the edge.
    /// </summary>
    public void RestartNearEnd(PlayerController player)
    {
        _player = player;
        player.Velocity = float3.Zero;
        player.Pitch = -0.06f;
        player.LeftThroughPortal();
        if (_boat is not null)
        {
            _riding = false;
            _boat.Finish();
            player.Position = _boat.PlayerFeet;
            player.Yaw = _boat.Yaw;
            _team?.Restart(player);
            Cinematic.Start(player);
            Cinematic.JumpTo(player, Cinematic.StepOfCue("boat:moored"));
        }
        else
        {
            player.Position = RopeStart;
            player.Yaw = -float.Pi / 2;
            Cinematic.Start(player);
            Cinematic.JumpTo(player, Cinematic.StepOfCue("rope:over"));
        }
        Log.Write($"[RAID] The player's back near the end of the way in ({(_boat is null ? "on the rope" : "in the boat")})");
    }

    private static float YawTowards(float3 from, float3 to) => float.Atan2(to.X - from.X, -(to.Z - from.Z));

    public void Update(float dt, PlayerController player, Input input)
    {
        if (_boat is not null)
        {
            _boat.Update(dt);
            if (_riding)
            {
                player.Position = _boat.PlayerFeet;
                PlayEngine(dt, _boat.Speed);
            }
        }
        if (Cinematic.Active)
            Cinematic.Update(dt, player, input);
        _team?.Update(dt, player);
    }

    public void Skip(PlayerController player) => Cinematic.Skip(player);

    public void Draw(Renderer renderer, int2 framebufferSize) => Cinematic.Draw(renderer, framebufferSize);

    /// <summary>What's only there for the raid: the boat, the rope.</summary>
    public void DrawWorld(Renderer renderer, Zones zones)
    {
        if ((zones & Zones.Yard) == 0)
            return;
        _boat?.Draw(renderer);
        _rope?.Draw(renderer);
    }

    private void OnCue(string cue)
    {
        var instant = Cinematic.FastForwarding;
        switch (cue)
        {
            case "boat:go":
                _riding = true;
                break;
            case "boat:moored":
                _boat?.Finish();
                _riding = false;
                if (_boat is not null && _player is not null)
                    _player.Position = _boat.PlayerFeet;   // (skipped or jumped to: in the boat, alongside)
                _audio?.Cancel("boat");
                Sound("Body/land", 0.5f, pitch: 0.7f);   // (the tubes against the quay)
                break;
            case "team:up":
                _team?.GoUp(instant);
                break;
            case "ledge:land":
                Sound("Body/land", 0.5f);
                break;
            case "rope:clip":
                Sound("Body/rope_clip", 0.7f);
                break;
            case "rope:over":
                if (_rope is not null)
                    _rope.Taut = true;
                Sound("Body/scrape", 0.4f);
                break;
            case "rope:zip":
                Sound("Body/rope_zip", 0.55f);
                Sound("Body/land", 0.35f, pitch: 1.3f, delay: 0.55f);   // (the boots back on the steel)
                break;
            case "rope:land":
                Sound("Body/land", 0.6f);
                break;
            case "rope:off":
                if (_rope is not null)
                    _rope.Taut = false;
                Sound("Body/rope_clip", 0.6f, pitch: 0.85f);
                break;
        }
    }

    private void OnFinished()
    {
        _riding = false;
        _audio?.Cancel("boat");
        _boat?.Finish();
        if (_rope is not null)
            _rope.Taut = false;
        if (_player is not null)
            _team?.Release(_player);
    }

    private float _engineIn;

    /// <summary>The outboards: one clip over and over, higher with the speed.</summary>
    private void PlayEngine(float dt, float speed)
    {
        _engineIn -= dt;
        if (_engineIn > 0 || _audio is null)
            return;
        _engineIn = 1.75f;
        _audio.Play(_audio.Get("Vehicles/boat_engine"), 0.5f, 0.75f + speed * 0.05f, tag: "boat");
    }

    private void Sound(string name, float gain, float pitch = 1f, float delay = 0f)
    {
        if (!Cinematic.FastForwarding)
            _audio?.Play(_audio.Get(name), gain, pitch, delay);
    }
}

/// <summary>
/// The rigid inflatable the boat team come in on: grey tubes round a dark hull, a console amidships, twin outboards. Its frame
/// is the van's (<see cref="DeliveryVan"/>): the bow towards -Z, starboard +X, y = 0 at the waterline. It comes in across the
/// dock from the south-west, swings in along the quay and ties up nose to the north, its port side against the quay's face.
/// </summary>
public sealed class RaidBoat
{
    public const float DeckHeight = 0.42f;   // above the waterline
    private const float HalfBeam = 1.0f, TubeRadius = 0.3f, Length = 7.6f;

    /// <summary>Where the player sits (feet: they're low, astride a seat).</summary>
    private static readonly float3 PlayerSeat = new(-0.45f, DeckHeight - 0.4f, -1.25f);   // (port side forward, by the ledge when alongside)

    // Tied up: nose to the north (+Z), its port tube against the fenders on the landing ledge, between the two ladders.
    private const float MooredYaw = float.Pi;
    private static float3 MooredPosition => new(Level.LedgeEdgeX - 0.24f - HalfBeam - TubeRadius - 0.03f, Level.YardWaterY, Level.YardOrigin.Z + Level.QuayLandingZ);

    private static readonly float2[] Waypoints = BuildWaypoints();
    private static float2[] BuildWaypoints()
    {
        var o = Level.YardOrigin;
        float2 Y(float x, float z) => new(o.X + x, o.Z + z);
        var moored = new float2(MooredPosition.X, MooredPosition.Z);
        return [Y(-118f, -55f), Y(-90f, -38f), Y(-62f, -16f), Y(-44f, 0f), Y(-34f, 9f), moored - new float2(0.2f, 6.5f), moored];
    }

    private readonly float2[] _samples;
    private const float SampleStep = 0.25f, Cruise = 8.5f, StartSpeed = 7f, Brake = 1.3f, Accelerate = 1.5f;
    private float _distance, _speed = StartSpeed, _time;
    private bool _moored;

    public event Action? Moored;
    public float Speed => _speed;

    private readonly (Mesh Mesh, float3 Tint)[] _parts;
    private readonly Mesh _unitCylinder = new MeshBuilder().Cylinder(1f, 0f, 1f, 16).Build();
    private readonly (float3 From, float3 To)[] _tubes;

    public RaidBoat()
    {
        // The route, sampled evenly by distance (Catmull-Rom through the waypoints).
        var fine = new List<float2>();
        for (var i = 0; i < Waypoints.Length - 1; i++)
        {
            var p0 = Waypoints[int.Max(i - 1, 0)];
            var p1 = Waypoints[i];
            var p2 = Waypoints[i + 1];
            var p3 = Waypoints[int.Min(i + 2, Waypoints.Length - 1)];
            for (var s = 0; s < 40; s++)
                fine.Add(CatmullRom(p0, p1, p2, p3, s / 40f));
        }
        fine.Add(Waypoints[^1]);
        var even = new List<float2> { fine[0] };
        var travelled = 0f;
        for (var i = 1; i < fine.Count; i++)
        {
            var seg = Len(fine[i] - fine[i - 1]);
            travelled += seg;
            while (travelled >= SampleStep)
            {
                var over = travelled - SampleStep;
                even.Add(fine[i] + (fine[i - 1] - fine[i]) * (seg > 1e-5f ? over / seg : 0f));
                travelled = over;
            }
        }
        even.Add(fine[^1]);
        _samples = [.. even];
        (_parts, _tubes) = BuildBoat();
    }

    private static float Len(float2 v) => float.Sqrt(v.X * v.X + v.Y * v.Y);

    private static float2 CatmullRom(float2 p0, float2 p1, float2 p2, float2 p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return ((p1 * 2f) + (p2 - p0) * t + (p0 * 2f - p1 * 5f + p2 * 4f - p3) * t2 + (p1 * 3f - p0 - p2 * 3f + p3) * t3) * 0.5f;
    }

    private float PathLength => (_samples.Length - 1) * SampleStep;

    private float2 Sample(float d)
    {
        var at = float.Clamp(d / SampleStep, 0f, _samples.Length - 1.001f);
        var i = (int)at;
        return _samples[i] + (_samples[i + 1] - _samples[i]) * (at - i);
    }

    private static float Heading(float2 from, float2 to) => float.Atan2(to.X - from.X, -(to.Y - from.Y));

    /// <summary>Which way the bow points (as the player's yaw).</summary>
    public float Yaw
    {
        get
        {
            if (_moored)
                return MooredYaw;
            var ahead = Heading(Sample(_distance - 1.5f), Sample(float.Min(_distance + 2.5f, PathLength)));
            // The last few metres it straightens up alongside.
            var left = PathLength - _distance;
            if (left > 6f)
                return ahead;
            var t = 1f - left / 6f;
            var d = MooredYaw - ahead;
            while (d > float.Pi) d -= float.Tau;
            while (d < -float.Pi) d += float.Tau;
            return ahead + d * t;
        }
    }

    private float3 Position => _moored ? MooredPosition : new float3(Sample(_distance).X, Level.YardWaterY, Sample(_distance).Y);

    private float4x4 World
    {
        get
        {
            // Bow up with the speed (planing), and a roll and a bob with the swell.
            var pitch = 0.055f * float.Min(_speed / Cruise, 1f) + 0.012f * float.Sin(_time * 1.7f);
            var roll = 0.018f * float.Sin(_time * 1.3f + 0.4f);
            var bob = 0.05f * float.Sin(_time * 1.9f) + (_moored ? 0f : 0.03f * float.Sin(_time * 7.3f));
            return float4x4.CreateRotationX(pitch) * float4x4.CreateRotationZ(roll) * float4x4.CreateRotationY(-Yaw)
                   * float4x4.CreateTranslation(Position + new float3(0, bob, 0));
        }
    }

    /// <summary>A point in the boat's frame, in the world, as it is now.</summary>
    public float3 WorldPoint(float3 local) => float3.Transform(local, World);

    /// <summary>A point in the frame of the boat tied up at the quay (no swell).</summary>
    public static float3 MooredAt(float3 local) =>
        float3.Transform(local, float4x4.CreateRotationY(-MooredYaw) * float4x4.CreateTranslation(MooredPosition));

    /// <summary>The player's feet, sat in the boat.</summary>
    public float3 PlayerFeet => WorldPoint(PlayerSeat);

    public void Update(float dt)
    {
        _time += dt;
        if (_moored)
            return;
        var left = PathLength - _distance;
        var limit = float.Min(Cruise, float.Sqrt(2f * Brake * float.Max(left, 0f)) + 0.25f);
        _speed = _speed < limit ? float.Min(_speed + Accelerate * dt, limit) : float.Max(_speed - Brake * 2f * dt, limit);
        _distance += _speed * dt;
        if (_distance >= PathLength)
            Finish();
    }

    /// <summary>Tied up alongside the quay.</summary>
    public void Finish()
    {
        if (_moored)
            return;
        _moored = true;
        _distance = PathLength;
        _speed = 0f;
        Moored?.Invoke();
    }

    public void Draw(Renderer renderer)
    {
        var world = World;
        foreach (var (mesh, tint) in _parts)
            renderer.DrawMesh(mesh, world, tint);
        foreach (var (from, to) in _tubes)
            renderer.DrawMesh(_unitCylinder, Stretch(from, to, TubeRadius) * world, new float3(0.07f, 0.075f, 0.075f));
    }

    /// <summary>The unit cylinder (radius 1, z 0..1) stretched from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static float4x4 Stretch(float3 a, float3 b, float radius)
    {
        var d = b - a;
        var length = float.Sqrt(float3.Dot(d, d));
        var dir = d / float.Max(length, 1e-5f);
        return float4x4.CreateScale(radius, radius, length)
               * float4x4.CreateRotationX(-float.Asin(float.Clamp(dir.Y, -1f, 1f)))
               * float4x4.CreateRotationY(float.Atan2(dir.X, dir.Z))
               * float4x4.CreateTranslation(a);
    }

    private static ((Mesh, float3)[], (float3, float3)[]) BuildBoat()
    {
        var hull = new MeshBuilder();
        var deck = new MeshBuilder();
        var console = new MeshBuilder();
        var glass = new MeshBuilder();
        var engines = new MeshBuilder();
        var seats = new MeshBuilder();
        var stern = Length / 2;      // +Z
        var bow = -Length / 2 + 0.6f;
        // The hull: a deep V under the waterline, stepping in towards the bow.
        for (var i = 0; i < 6; i++)
        {
            var z0 = bow + i * (stern - bow) / 6f;
            var z1 = z0 + (stern - bow) / 6f;
            var w = 2f * HalfBeam * (i == 0 ? 0.55f : i == 1 ? 0.85f : 1f);
            hull.Box(new float3(0, 0.05f, (z0 + z1) / 2), new float3(w, 0.5f, z1 - z0));
            hull.Box(new float3(0, -0.3f, (z0 + z1) / 2), new float3(w * 0.5f, 0.3f, z1 - z0));
        }
        deck.Box(new float3(0, DeckHeight - 0.03f, 0.3f), new float3(2f * HalfBeam - 0.1f, 0.06f, Length - 1.6f));
        // On forward into the bow, under the tubes where they close in, so there's no gap down to the water.
        var deckFront = 0.3f - (Length - 1.6f) / 2;
        deck.Box(new float3(0, DeckHeight - 0.03f, (deckFront + bow) / 2), new float3(1.9f, 0.06f, deckFront - bow));
        deck.Box(new float3(0, DeckHeight - 0.03f, bow - 0.22f), new float3(1.5f, 0.06f, 0.45f));
        // The console amidships, its screen, and a grab rail round it.
        console.Box(new float3(0, DeckHeight + 0.5f, 0.55f), new float3(0.8f, 1.0f, 0.65f));
        glass.Box(new float3(0, DeckHeight + 1.18f, 0.3f), new float3(0.78f, 0.38f, 0.04f));
        console.Box(new float3(0, DeckHeight + 1.6f, 0.75f), new float3(0.06f, 0.9f, 0.06f));   // the mast for the radar and lights
        console.Box(new float3(0, DeckHeight + 2.08f, 0.75f), new float3(0.5f, 0.1f, 0.2f));
        // Jockey seats, two forward, two aft of the console.
        foreach (var (x, z) in ((float, float)[])[(-0.45f, -1.1f), (0.45f, -1.1f), (0f, 1.35f), (0f, 2.3f)])
        {
            seats.Box(new float3(x, DeckHeight + 0.35f, z), new float3(0.36f, 0.7f, 0.55f));
            seats.Box(new float3(x, DeckHeight + 0.75f, z), new float3(0.42f, 0.12f, 0.6f));
        }
        // Twin outboards on the transom.
        foreach (var x in (ReadOnlySpan<float>)[-0.45f, 0.45f])
        {
            engines.Box(new float3(x, DeckHeight + 0.45f, stern + 0.3f), new float3(0.42f, 0.6f, 0.55f));
            engines.Box(new float3(x, -0.2f, stern + 0.35f), new float3(0.14f, 0.9f, 0.22f));
        }
        (Mesh, float3)[] parts =
        [
            (hull.Build(), new float3(0.09f, 0.1f, 0.11f)),
            (deck.Build(), new float3(0.05f, 0.05f, 0.05f)),
            (console.Build(), new float3(0.11f, 0.12f, 0.12f)),
            (glass.Build(), new float3(0.03f, 0.04f, 0.05f)),
            (engines.Build(), new float3(0.04f, 0.04f, 0.045f)),
            (seats.Build(), new float3(0.06f, 0.06f, 0.06f)),
        ];
        // The tubes: down each side, and round the bow in three pieces.
        var y = DeckHeight + 0.05f;
        (float3, float3)[] tubes =
        [
            (new(-HalfBeam, y, stern + 0.1f), new(-HalfBeam, y, bow + 0.6f)),
            (new(HalfBeam, y, stern + 0.1f), new(HalfBeam, y, bow + 0.6f)),
            (new(-HalfBeam, y, bow + 0.6f), new(-0.55f, y + 0.12f, bow - 0.45f)),
            (new(HalfBeam, y, bow + 0.6f), new(0.55f, y + 0.12f, bow - 0.45f)),
            (new(-0.55f, y + 0.12f, bow - 0.45f), new(0.55f, y + 0.12f, bow - 0.45f)),
        ];
        return (parts, tubes);
    }
}

/// <summary>
/// The three who come ashore with the player on the Antwerp raid by boat. On the way over they kneel in the boat, rifles up;
/// alongside, they go up the ladder one after another and take cover on the quay. Then the script lets go of them and they
/// come with you: each follows a few paces behind you along the way you've walked, at its own distance, and fights whoever
/// shows (they're <see cref="Faction.Police"/>, friendly to you, at war with the yard's crew).
/// </summary>
public sealed class BoatTeam
{
    // Where each rides (the boat's frame: bow -Z, port -X): one beside the player on the starboard seat forward, the coxswain
    // at the console, one aft. (None ahead of the player: the way in is to be seen.)
    private static readonly float3[] Rides = [new(0.5f, RaidBoat.DeckHeight, -1.0f), new(0f, RaidBoat.DeckHeight, 1.35f), new(-0.45f, RaidBoat.DeckHeight, 2.3f)];

    // Their cover on the quay (from the yard's origin), and what each watches from it.
    private static readonly float3[] Covers = [new(-26.4f, 0, 26.6f), new(-25.4f, 0, 18.0f), new(-24.2f, 0, 21.6f)];
    private static readonly float3[] Watches = [new(-12f, 0, 34f), new(-4f, 0, 14.5f), new(-9f, 0, 25f)];
    private const float ClimbTime = 1.6f;

    public static (float3 At, float Heading)[] Posts =>
        [.. Covers.Select((c, i) => (Level.YardOrigin + c, FacingFrom(Level.YardOrigin + c, Level.YardOrigin + Watches[i])))];

    private readonly Npcs _npcs;
    private readonly RaidBoat _boat;
    private ArmedNpc[]? _team;
    private readonly List<(float3 To, float Speed)>[] _legs = [[], [], []];
    private readonly float[] _delay = new float[3];
    private readonly bool[] _faced = new bool[3];
    private enum Stage { Riding, GoingUp, Released }
    private Stage _stage = Stage.Riding;
    private int _respawns = int.MinValue;

    // Following: where the player's been (a point every metre or so), and when each next looks again.
    private readonly List<float3> _trail = [];
    private float _followIn;
    private readonly float3?[] _following = new float3?[3];

    public BoatTeam(Npcs npcs, RaidBoat boat)
    {
        _npcs = npcs;
        _boat = boat;
        _team = npcs.AddBoatTeam(Posts);
    }

    /// <summary>A respawn has made new ones (at their cover): back in the boat with the player, alongside.</summary>
    public void Restart(PlayerController player)
    {
        _team = _npcs.BoatTeamNow is { Length: > 0 } now ? now[..int.Min(now.Length, Rides.Length)] : null;
        Start(player);
    }

    public void Start(PlayerController player)
    {
        _respawns = player.Respawns;
        _stage = Stage.Riding;
        _releaseWhenUp = false;
        _trail.Clear();
        Array.Clear(_following);
        if (_team is null)
            return;
        foreach (var member in _team)
        {
            member.ScriptTake(member.Position, member.Heading);
            member.ScriptSling(false);
        }
        Ride();
        Log.Write("[RAID] Three in the boat with the player");
    }

    private bool Gone(PlayerController player) => _team is null || player.Respawns != _respawns;

    public void Update(float dt, PlayerController player)
    {
        if (Gone(player))
            return;
        switch (_stage)
        {
            case Stage.Riding:
                Ride();
                return;
            case Stage.Released:
                Follow(dt, player);
                return;
        }

        for (var i = 0; i < _team!.Length; i++)
        {
            var member = _team[i];
            if (!member.Alive)
                continue;
            if (_delay[i] > 0f)
            {
                _delay[i] -= dt;
                if (_delay[i] > 0f)
                    continue;
            }
            if (!member.ScriptArrived)
                continue;
            if (_legs[i].Count > 0)
            {
                var (to, speed) = _legs[i][0];
                _legs[i].RemoveAt(0);
                // Facing the way they go (up the ladder: facing the quay).
                var flat = to - member.Position;
                flat.Y = 0;
                member.ScriptFace(to + (float3.Dot(flat, flat) > 0.01f ? flat * 4f : new float3(4f, 0, 0)));
                member.ScriptWalk(to, speed);
            }
            else if (!_faced[i])
            {
                _faced[i] = true;
                member.ScriptFace(Level.YardOrigin + Watches[i]);
            }
        }
        if (_releaseWhenUp && AllInCover())
            LetGo(player);
    }

    /// <summary>Knelt in the boat wherever it is, facing the bow.</summary>
    private void Ride()
    {
        for (var i = 0; i < _team!.Length; i++)
        {
            var at = _boat.WorldPoint(Rides[i]);
            _team[i].ScriptTake(at, FacingFrom(at, _boat.WorldPoint(Rides[i] + new float3(0, 0, -1f))));
        }
    }

    // Which ladder each takes (Level.QuayLadderZs: 0 the north one, the player's), and when each sets off: the one beside the
    // player goes up the player's ladder just ahead of them; the other two take the south one, one after the other.
    private static readonly int[] Ladder = [0, 1, 1];
    private static readonly float[] SetOff = [0f, 0.2f, 2.3f];

    /// <summary>Alongside: off the boat onto the ledge, up the two ladders, and into cover on the quay.</summary>
    public void GoUp(bool instant)
    {
        if (_team is null || _stage != Stage.Riding)
            return;
        _stage = Stage.GoingUp;
        for (var i = 0; i < _team.Length; i++)
        {
            _faced[i] = false;
            _delay[i] = SetOff[i];
            _legs[i].Clear();
            if (instant)
            {
                Place(i);
                continue;
            }
            // A step across onto the ledge, along it to the foot of their ladder; up it (a slow lift up the face); over
            // the kerb; to cover.
            var ladder = Level.QuayLadderFoot(Ladder[i]);
            var from = _team[i].Position;
            _legs[i].Add((new float3(Level.LedgeEdgeX + 0.45f, Level.LedgeTop, float.Lerp(from.Z, ladder.Z, 0.4f)), 1.4f));
            _legs[i].Add((new float3(ladder.X - 0.45f, Level.LedgeTop, ladder.Z), 1.4f));
            _legs[i].Add((new float3(ladder.X - 0.3f, 0.05f, ladder.Z), 0.15f / ClimbTime));
            _legs[i].Add((new float3(ladder.X + 0.35f, 0.22f, ladder.Z), 1.2f));
            _legs[i].Add((new float3(ladder.X + 1.2f, 0, ladder.Z), 1.6f));
            _legs[i].Add((Level.YardOrigin + Covers[i], 2.6f));
        }
        Log.Write("[RAID] The boat team go up the two ladders");
    }

    /// <summary>
    /// The script's over (the player's on the ledge): they go on up and into cover, and once they're all there they're on
    /// their own, coming with the player.
    /// </summary>
    public void Release(PlayerController player)
    {
        if (Gone(player) || _stage == Stage.Released)
            return;
        if (_stage == Stage.Riding)
            GoUp(instant: true);
        _releaseWhenUp = true;
        if (AllInCover())
            LetGo(player);
    }

    private bool _releaseWhenUp;

    private bool AllInCover()
    {
        for (var i = 0; i < _team!.Length; i++)
            if (_team[i].Alive && (_legs[i].Count > 0 || !_team[i].ScriptArrived))
                return false;
        return true;
    }

    private void LetGo(PlayerController player)
    {
        _releaseWhenUp = false;
        _stage = Stage.Released;
        foreach (var member in _team!)
        {
            if (!member.Alive)
                continue;
            member.HoldsPost = false;
            member.ScriptRelease();
        }
        _trail.Clear();
        _trail.Add(player.Position);
        _followIn = 2.5f;   // (a moment in cover before they move off)
        Log.Write("[RAID] The boat team are with the player");
    }

    private void Place(int i)
    {
        var member = _team![i];
        _legs[i].Clear();
        _faced[i] = true;
        _delay[i] = 0f;
        if (!member.Alive)
            return;
        var at = Level.YardOrigin + Covers[i];
        var look = Level.YardOrigin + Watches[i];
        member.ScriptPlace(at, FacingFrom(at, look));
        member.ScriptFace(look);
    }

    // How far back along the player's way each keeps, and how far the player's way has to have moved before they go on.
    private static readonly float[] Behind = [3.5f, 5.5f, 7.5f];
    private const float MoveOn = 2.5f;

    /// <summary>Along behind the player: each to the point on the player's way its distance back, when that's moved on.</summary>
    private void Follow(float dt, PlayerController player)
    {
        if (!player.Alive || Level.ZoneAt(player.Position) != Zones.Yard)
            return;
        var at = player.Position;
        if (player.Grounded && Distance(at, _trail[^1]) > 1f)
        {
            _trail.Add(at);
            if (_trail.Count > 40)
                _trail.RemoveAt(0);
        }
        _followIn -= dt;
        if (_followIn > 0f)
            return;
        _followIn = 0.8f;
        var face = player.Yaw;   // (as an NPC's heading: the way the player's looking)
        var heading = float.Atan2(float.Sin(face), -float.Cos(face));
        for (var i = 0; i < _team!.Length; i++)
        {
            var member = _team[i];
            if (!member.Alive)
                continue;
            var spot = AlongTrail(Behind[i]);
            if (spot is null || Distance(spot.Value, at) < 2.5f)
                continue;
            if (_following[i] is { } was && Distance(was, spot.Value) < MoveOn)
                continue;
            _following[i] = spot;
            member.MovePost(spot.Value, heading);
        }
    }

    /// <summary>The point on the player's way <paramref name="back"/> metres back from where they are; null until they've come that far.</summary>
    private float3? AlongTrail(float back)
    {
        var left = back;
        var from = _trail[^1];
        for (var k = _trail.Count - 2; k >= 0; k--)
        {
            var step = Distance(_trail[k], from);
            if (step >= left)
                return from + (_trail[k] - from) * (left / step);
            left -= step;
            from = _trail[k];
        }
        return null;
    }

    private static float Distance(float3 a, float3 b)
    {
        var d = a - b;
        return float.Sqrt(d.X * d.X + d.Z * d.Z);
    }

    private static float FacingFrom(float3 from, float3 to) => float.Atan2(to.X - from.X, to.Z - from.Z);
}

/// <summary>
/// The rope down the east stacks: tied off round a corner casting on top, over the edge and down the end of the
/// container to the ground, with what's left of it in a heap at the bottom. While the player's on it, it's pulled tight
/// against the edge; otherwise it hangs a hand's breadth off the steel.
/// </summary>
public sealed class AbseilRope
{
    private readonly Mesh _cylinder = new MeshBuilder().Cylinder(1f, 0f, 1f, 8).Build();
    private readonly Mesh _gear;
    private static readonly float3 RopeColor = new(0.06f, 0.07f, 0.08f);
    private static readonly float3 GearColor = new(0.05f, 0.05f, 0.04f);

    public bool Taut { get; set; }

    public AbseilRope()
    {
        var top = Level.EastStacksTop;
        var face = Level.EastStacksFaceX;
        var z = Level.AbseilZ;
        var b = new MeshBuilder();
        b.Box(new float3(face + 2.2f, top + 0.06f, z), new float3(0.12f, 0.12f, 0.2f));            // the anchor: a sling round the casting
        b.Box(new float3(face + 1.1f, top + 0.12f, z + 0.35f), new float3(0.45f, 0.24f, 0.3f));   // the rope bag, emptied
        b.Box(new float3(face + 0.06f, top + 0.015f, z), new float3(0.22f, 0.03f, 0.16f));         // the edge protector
        _gear = b.Build();
    }

    public void Draw(Renderer renderer)
    {
        var top = Level.EastStacksTop;
        var face = Level.EastStacksFaceX;
        var z = Level.AbseilZ;
        var anchor = new float3(face + 2.2f, top + 0.08f, z);
        var edge = new float3(face - 0.02f, top + 0.04f, z);
        var hang = Taut ? -0.04f : -0.1f;
        var bottom = new float3(face + hang, 0.05f, z + (Taut ? 0f : 0.05f));
        Segment(renderer, anchor, edge, 0.017f);
        Segment(renderer, edge, new float3(face + hang, top - 0.4f, z), 0.017f);
        Segment(renderer, new float3(face + hang, top - 0.4f, z), bottom, 0.017f);
        // What's left over, coiled at the foot.
        renderer.DrawMesh(_cylinder, float4x4.CreateScale(0.28f, 0.28f, 0.1f) * float4x4.CreateRotationX(-float.Pi / 2)
            * float4x4.CreateTranslation(new float3(face - 0.35f, 0f, z + 0.2f)), RopeColor);
        renderer.DrawMesh(_gear, float4x4.Identity, GearColor);
    }

    private void Segment(Renderer renderer, float3 a, float3 b, float radius) =>
        renderer.DrawMesh(_cylinder, RaidBoat.Stretch(a, b, radius), RopeColor);
}
