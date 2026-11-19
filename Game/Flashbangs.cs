using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Flashbangs (stun grenades). Hold G and the throw's path is plotted ahead of you: a dashed arc through the air,
/// bouncing off whatever it meets and through any portal it flies into, to a ring where it will go off. Let go of G
/// and it's thrown (the fuse is lit as it leaves the hand). After <see cref="Fuse"/> seconds it goes off: a blinding
/// flash and a bang.
/// <para>
/// In flight it's simulated here, not by Bullet: the same fixed steps (gravity, a ray through the physics world for
/// each step, a bounce that loses most of the speed into the surface and some along it) as the plotted arc, so it
/// follows the arc exactly unless something moves into its way. Portal water carries it through to the linked portal
/// (an offline portal's water is just something to bounce off).
/// </para>
/// <para>
/// The bang dazzles whoever can see it, by how near they are and whether they were looking its way
/// (<see cref="Effect"/>), and deafens whoever is near. The player's view whites out and their ears ring (the world
/// muffled: <see cref="AudioEngine.Deafness"/>); people double over, unable to see, hear, run or shoot for a few
/// seconds, then react as to a gunshot next to them (<see cref="Npcs.Flashbang"/>). It hurts no one.
/// </para>
/// You carry <see cref="Capacity"/>; a respawn brings you a fresh set (or, with <see cref="Infinite"/>, they never run out).
/// </summary>
public sealed class Flashbangs
{
    public const int Capacity = 3;
    public const float Fuse = 1.6f;              // s from the throw
    public const float BlindReach = 16f;         // m: nobody is dazzled from further away
    public const float DeafReach = 12f;          // m: nobody is deafened from further away
    private const float ThrowSpeed = 13f;        // m/s
    private const float ThrowLift = 0.2f;        // thrown a little above the line of sight (added to the look direction's Y)
    private const float CarryOver = 0.6f;        // how much of the player's own speed it takes with it
    private const float Radius = Flight.Radius;
    private const float Step = Flight.Step;      // (the arc and the real thing alike)
    private const float GlareFade = 2.5f;        // s: the white fading out after the hold
    private const float DeafFade = 3f;
    private const float LightTime = 0.15f;       // s: the flash lighting the room

    private static readonly float3 ArcColor = new(0.55f, 1.1f, 1.5f);
    private static readonly float3 BodyTint = new(0.17f, 0.2f, 0.14f);
    private static readonly float3 CapTint = new(0.45f, 0.45f, 0.43f);

    private sealed class Grenade
    {
        public float3 Position, Previous, Velocity;
        public bool Resting;
        public float Age;
        public float3 SpinAxis;
        public float Spin, Angle;   // rad/s, rad
        public float Heading;       // lying at rest
        public bool ByPlayer;
    }

    private readonly Flight _flight;
    private readonly PhysicsWorld? _physics;
    private readonly Particles _particles;
    private readonly Npcs _npcs;
    private readonly GameSounds _sounds;
    private readonly List<Grenade> _live = [];
    private readonly List<(float3 Point, bool Break)> _arc = [];
    private readonly Random _random = new(808);
    private readonly Mesh _body, _cap;
    private float _accumulator;
    private bool _wasHeld;
    private int _respawns = int.MinValue;

    // The player's eyes and ears: how white the view is and how deaf they are, held for a while then fading.
    private float _glare, _glareHold, _deaf, _deafHold, _sinceBang = float.PositiveInfinity;
    private float4 _light;
    private float _lightLeft;

    public Flashbangs(Level level, PortalSystem portals, PhysicsWorld? physics, SurfaceTable surfaces, Particles particles, Npcs npcs, GameSounds sounds)
    {
        _flight = new Flight(level, portals, physics, surfaces);
        _physics = physics;
        _particles = particles;
        _npcs = npcs;
        _sounds = sounds;
        // An M84-ish can (along Z): a perforated steel body 4.4 cm across, the fuse and its spoon on top.
        _body = new MeshBuilder().Cylinder(0.022f, -0.055f, 0.045f, 16).Build();
        _cap = new MeshBuilder().Cylinder(0.013f, 0.045f, 0.068f, 12).Box(new float3(0.016f, 0, 0.02f), new float3(0.006f, 0.01f, 0.07f)).Build();
    }

    /// <summary>How many the player has left.</summary>
    public int Carried { get; private set; } = Capacity;

    /// <summary>How many a respawn brings (a raid's loadout sets it): <see cref="Capacity"/> unless it says otherwise. Setting it fills up to it.</summary>
    public int Stock
    {
        get => _stock;
        set => Carried = _stock = int.Max(0, value);
    }
    private int _stock = Capacity;

    /// <summary>Testing (BRUTALFPS_FLASHBANGS=infinite): throwing one doesn't use it up.</summary>
    public bool Infinite { get; set; }

    /// <summary>G is held: the throw's arc is plotted.</summary>
    public bool Aiming { get; private set; }

    /// <summary>How white the player's view is (0..1): draw it over everything (<see cref="Renderer.DrawWhiteout"/>).</summary>
    public float Glare => 1f - (1f - _glare) * (1f - _glare);   // (eased: a strong flash is all but white, and the last of it lingers)

    /// <summary>How deaf the player is (0..1): for <see cref="AudioEngine.Deafness"/>. (It sets in just after the bang, so the bang itself is heard.)</summary>
    public float Deafness => _deaf * float.Clamp(_sinceBang / 0.25f, 0f, 1f);

    /// <summary>The flash lighting its surroundings: xyz where, w how bright (0 = none), as <see cref="Renderer.SetFlash"/> takes.</summary>
    public float4 Light => _lightLeft > 0 ? _light with { W = _light.W * (_lightLeft / LightTime) * (_lightLeft / LightTime) } : float4.Zero;

    /// <summary>
    /// How a flashbang going off <paramref name="distance"/> metres away affects someone: how white their view goes
    /// (0..1: fully within 2 m, nothing past 16 m; less if they were looking away, <paramref name="facing"/> being the
    /// cosine between where they look and the bang; only the glow off the walls if they can't see it), and how deaf
    /// they are (0..1, by distance: less through a wall).
    /// </summary>
    public static (float White, float Deaf) Effect(float distance, float facing, bool inSight)
    {
        var near = float.Clamp(1f - (distance - 2f) / (BlindReach - 2f), 0f, 1f);
        var look = 0.2f + 0.8f * float.Clamp((facing + 0.3f) / 1.1f, 0f, 1f);
        var white = inSight ? near * look : 0.3f * float.Clamp(1f - distance / 6f, 0f, 1f);
        var deaf = float.Clamp(1f - (distance - 1f) / (DeafReach - 1f), 0f, 1f) * (inSight ? 1f : 0.6f);
        return (white, deaf);
    }

    // ------------------------------------------------------------------------------------------------ each frame

    /// <summary>
    /// The throw (G held: the arc; let go: thrown), the ones in flight (and going off), and the player's glare and
    /// deafness wearing off. <paramref name="canThrow"/>: the player is free to (alive, playing, not at a terminal).
    /// </summary>
    public void Update(float dt, Input input, PlayerController player, in Camera camera, bool canThrow)
    {
        if (player.Respawns != _respawns)
        {
            if (_respawns != int.MinValue)
            {
                _live.Clear();
                _glare = _deaf = _glareHold = _deafHold = 0;
                _lightLeft = 0;
            }

            _respawns = player.Respawns;
            Carried = Stock;
        }

        // Holding G: plot the throw. Letting go: throw it.
        var held = canThrow && Carried > 0 && input.IsDown(GlfwKey.G);
        if (held && !_wasHeld)
            _sounds.PinPulled();
        if (!held && _wasHeld && canThrow && Carried > 0)
            Throw(player, camera);
        _wasHeld = held;
        Aiming = held;
        if (held)
        {
            var (from, velocity) = ThrowFrom(player, camera);
            PlotArc(from, velocity);
            DrawArc();
        }

        // In flight: fixed steps.
        _accumulator += dt;
        while (_accumulator >= Step)
        {
            _accumulator -= Step;
            for (var i = _live.Count - 1; i >= 0; i--)
            {
                var g = _live[i];
                g.Previous = g.Position;
                g.Age += Step;
                if (g.Age >= Fuse)
                {
                    _live.RemoveAt(i);
                    Bang(g, player, camera);
                    continue;
                }

                var wasResting = g.Resting;
                if (_flight.Advance(ref g.Position, ref g.Velocity, ref g.Resting, out var impact, out var surface, out var jumped))
                {
                    _sounds.GrenadeBounce(g.Position, impact, surface);
                    g.Spin *= 0.5f;
                }

                if (jumped)
                    g.Previous = g.Position;   // (don't draw it sliding across the map between the two portals)
                if (g.Resting && !wasResting)
                    g.Heading = (float)_random.NextDouble() * float.Tau;
                g.Angle += g.Spin * Step;
                if (g.Position.Y < Level.KillHeightAt(g.Position))
                    _live.RemoveAt(i);
            }
        }

        // The player's eyes and ears coming back.
        _sinceBang += dt;
        _lightLeft -= dt;
        if (_glareHold > 0)
            _glareHold -= dt;
        else
            _glare = float.Max(0f, _glare - dt / GlareFade);
        if (_deafHold > 0)
            _deafHold -= dt;
        else
            _deaf = float.Max(0f, _deaf - dt / DeafFade);
    }

    /// <summary>Where a throw leaves the hand (low on the right, in front of the eye) and how fast.</summary>
    private (float3 From, float3 Velocity) ThrowFrom(PlayerController player, in Camera camera)
    {
        var forward = camera.Forward;
        var right = float3.Cross(forward, float3.UnitY);
        right = float3.Dot(right, right) > 1e-6f ? float3.Normalize(right) : float3.UnitX;
        var eye = camera.Position;
        var hand = eye + forward * 0.3f + right * 0.2f - new float3(0, 0.12f, 0);
        // Not out through a wall you're standing against.
        if (_physics is not null && _physics.Raycast(eye, hand, out var hit, includeBodies: false))
            hand = eye + (hand - eye) * float.Max(0f, hit.Fraction - 0.1f);
        var direction = float3.Normalize(forward + new float3(0, ThrowLift, 0));
        return (hand, direction * ThrowSpeed + player.Velocity * CarryOver);
    }

    private void Throw(PlayerController player, in Camera camera)
    {
        var (from, velocity) = ThrowFrom(player, camera);
        var axis = float3.Cross(velocity, float3.UnitY);
        axis = float3.Dot(axis, axis) > 1e-6f ? float3.Normalize(axis) : float3.UnitX;
        _live.Add(new Grenade
        {
            Position = from, Previous = from, Velocity = velocity, SpinAxis = axis,
            Spin = 9f + 6f * (float)_random.NextDouble(), ByPlayer = true,
        });
        if (!Infinite)
            Carried--;
        _sounds.Thrown();
        Log.Write($"[FLASHBANG] Thrown from {from.X:0.0},{from.Y:0.0},{from.Z:0.0} at {float.Sqrt(float3.Dot(velocity, velocity)):0.0} m/s ({Carried} left)");
    }

    // ------------------------------------------------------------------------------------------------ flight

    /// <summary>The path a throw from <paramref name="from"/> would take until it goes off (with breaks where it goes through a portal).</summary>
    private void PlotArc(float3 from, float3 velocity)
    {
        _arc.Clear();
        _arc.Add((from, false));
        var position = from;
        var resting = false;
        var steps = (int)(Fuse / Step);
        for (var i = 0; i < steps && !resting; i++)
        {
            _flight.Advance(ref position, ref velocity, ref resting, out _, out _, out var jumped);
            _arc.Add((position, jumped));
            if (position.Y < Level.KillHeightAt(position))
                break;
        }
    }

    /// <summary>The plotted arc as glowing dashes (not the first bit, right in front of the eye), and a ring on the ground where it will go off.</summary>
    private void DrawArc()
    {
        var dash = 0;
        var travelled = 0f;   // along the path (straight-line distance from the eye means nothing past a portal)
        for (var i = 1; i < _arc.Count; i++)
        {
            var (a, _) = _arc[i - 1];
            var (b, jumped) = _arc[i];
            if (jumped)
                continue;
            var ab = b - a;
            var length = float.Sqrt(float3.Dot(ab, ab));
            if (length < 1e-4f)
                continue;
            // Dashes of 0.25 m with gaps between, along the whole path.
            for (var t = 0f; t < length; t += 0.125f, dash++)
            {
                if ((dash & 1) == 1)
                    continue;
                var head = a + ab * float.Min(1f, (t + 0.125f) / length);
                var tail = a + ab * (t / length);
                var away = travelled + t;
                if (away < 0.8f)
                    continue;
                _particles.Streak(head, tail, ArcColor, 0.006f + 0.002f * away / 10f, float.Min(0.7f, (away - 0.8f) * 1.5f));
            }

            travelled += length;
        }

        // Where it goes off: a ring round it, lying on the ground (or in the air, if the fuse runs out first).
        var end = _arc[^1].Point;
        const int segments = 16;
        const float radius = 0.3f;
        for (var k = 0; k < segments; k++)
        {
            var a0 = k * float.Tau / segments;
            var a1 = (k + 1) * float.Tau / segments;
            var p0 = end + new float3(float.Cos(a0), 0.02f, float.Sin(a0)) * radius;
            var p1 = end + new float3(float.Cos(a1), 0.02f, float.Sin(a1)) * radius;
            _particles.Streak(p1, p0, ArcColor * 1.4f, 0.01f, 0.8f);
        }
    }

    // ------------------------------------------------------------------------------------------------ the bang

    private void Bang(Grenade g, PlayerController player, in Camera camera)
    {
        var at = g.Position + new float3(0, 0.05f, 0);
        Log.Write($"[FLASHBANG] Bang at {at.X:0.0},{at.Y:0.0},{at.Z:0.0} ({Level.ZoneAt(at)})");
        _sounds.Flashbang(at);
        _light = new float4(at, 14f);
        _lightLeft = LightTime;

        // The flash itself, sparks off the can, and a cloud of smoke.
        _particles.Add(new Particles.Particle
        {
            Position = at, Kind = ParticleKind.Flash, Color = new float3(30f, 30f, 28f), Size = 1.6f, Life = 0.1f, Alpha = 1f,
            Rotation = (float)_random.NextDouble() * float.Tau,
        });
        for (var i = 0; i < 24; i++)
        {
            _particles.Add(new Particles.Particle
            {
                Position = at, Velocity = Particles.RandomDirection() * Particles.Range(3f, 9f), Kind = ParticleKind.Spark,
                Color = new float3(7f, 6f, 4.5f), Size = 0.004f, Life = Particles.Range(0.2f, 0.5f), Alpha = 1f,
                Gravity = 1f, Drag = 1.5f, StreakTime = 0.02f, Bounce = true,
            });
        }

        for (var i = 0; i < 10; i++)
        {
            _particles.Add(new Particles.Particle
            {
                Position = at + Particles.RandomDirection() * 0.15f, Velocity = Particles.RandomDirection() * Particles.Range(0.5f, 2f) + new float3(0, 0.4f, 0),
                Kind = ParticleKind.Dust, Color = new float3(0.62f, 0.62f, 0.6f), Size = Particles.Range(0.15f, 0.3f), Growth = Particles.Range(0.4f, 0.8f),
                Life = Particles.Range(3f, 5f), Alpha = 0.5f, Gravity = -0.02f, Drag = 2f, Spin = Particles.Range(-0.5f, 0.5f),
            });
        }

        // People.
        _npcs.Flashbang(at, g.ByPlayer);

        // The player (who may be standing right by it, or round the corner, or in another part of the map).
        if (!player.Alive || (Level.ZonesSeenFrom(camera.Position) & Level.ZoneAt(at)) == 0)
            return;
        var to = at - camera.Position;
        var distance = float.Sqrt(float3.Dot(to, to));
        var facing = distance > 1e-3f ? float3.Dot(to / distance, camera.Forward) : 1f;
        var inSight = _npcs.Senses.Clear?.Invoke(camera.Position, at) is not { } clear || clear > 0.99f;
        var (white, deaf) = Effect(distance, facing, inSight);
        if (white > _glare)
        {
            _glare = white;
            _glareHold = 0.4f + 3.6f * white;
        }

        if (deaf > 0.05f)
        {
            if (deaf > _deaf)
            {
                _deaf = deaf;
                _deafHold = 1f + 4f * deaf;
            }

            _sinceBang = 0;
            _sounds.Ringing(deaf);
        }

        Log.Write($"[FLASHBANG] The player: {distance:0.0} m away, {(inSight ? "in sight" : "out of sight")}, facing {facing:0.00}: " +
                  $"white {white:0.00} ({_glareHold:0.0} s), deaf {deaf:0.00}");
    }

    /// <summary>The ones in flight or lying about, for people to get clear of (<see cref="NpcSenses.FindGrenades"/>): within 7 m it dazzles anyone facing it.</summary>
    public void ReportGrenades(List<LiveGrenade> into)
    {
        foreach (var g in _live)
            into.Add(new LiveGrenade(g.Position, g.Resting ? float3.Zero : g.Velocity, g.Resting, Fuse - g.Age, 7f, Flash: true));
    }

    // ------------------------------------------------------------------------------------------------ drawing

    /// <summary>The ones in flight or lying about (between steps: where they'd be by now).</summary>
    public void Draw(Renderer renderer, Zones zones)
    {
        var t = _accumulator / Step;
        foreach (var g in _live)
        {
            var at = float3.Lerp(g.Previous, g.Position, t);
            if ((zones & Level.ZoneAt(at)) == 0)
                continue;
            // Tumbling end over end in flight; lying on its side once it stops.
            var turn = g.Resting
                ? float4x4.CreateRotationY(g.Heading)
                : float4x4.CreateFromAxisAngle(g.SpinAxis, g.Angle) * float4x4.CreateRotationX(0.4f);
            var model = turn * float4x4.CreateTranslation(at + (g.Resting ? new float3(0, 0.022f - Radius, 0) : float3.Zero));
            DrawCan(renderer, model);
        }
    }

    /// <summary>
    /// One flashbang (its length along Z, the fuse end towards +Z), wherever <paramref name="model"/> puts it.
    /// <paramref name="bodyBrightness"/> lifts its dark paint (for a picture of it, on a dark card).
    /// </summary>
    public void DrawCan(Renderer renderer, in float4x4 model, float bodyBrightness = 1f)
    {
        renderer.DrawMesh(_body, model, BodyTint * bodyBrightness, ShadeMode.Lit, Material.Plain, new float2(0.6f, 0.4f));
        renderer.DrawMesh(_cap, model, CapTint, ShadeMode.Lit, Material.Plain, new float2(0.35f, 0.8f));
    }
}
