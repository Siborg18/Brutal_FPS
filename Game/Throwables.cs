using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The player's other grenades, thrown as flashbangs are (hold the key and the throw's arc is plotted, let go to throw;
/// the same <see cref="Flight"/>):
/// <list type="bullet">
/// <item><b>Fragmentation (Q).</b> After <see cref="FragFuse"/> s it explodes: full force within
/// <see cref="BlastRadius"/> m, the fragments carrying on, weakening, to <see cref="FragmentReach"/> m. How hard it hits
/// depends on where it goes off: <see cref="Confinement"/> feels out the walls and ceiling round it, and in a room or a
/// corridor the blast has nowhere to go: it does up to <see cref="ConfinedBoost"/> times the damage, and reaches round
/// corners (the pressure wave filling the room). It hurts the player as anyone else, throws bodies and loose debris, and
/// leaves survivors near it senseless and deaf.</item>
/// <item><b>Smoke (X).</b> It does no harm: after <see cref="SmokeFuse"/> s it pops and pours out a thick cloud for
/// <see cref="SmokePour"/> s that hangs for a while after. Nobody sees through it, the player or anyone else
/// (<see cref="SmokeBetween"/> feeds <see cref="NpcSenses.Smoke"/>); but it's cool, so a thermal scope sees straight through
/// (the shader drops <see cref="ParticleKind.Smoke"/> in thermal).</item>
/// </list>
/// A respawn brings a fresh set (or, with <see cref="Infinite"/>, they never run out).
/// </summary>
public sealed class Throwables
{
    public enum Kind { Frag, Smoke }

    public const int FragCapacity = 3;
    public const int SmokeCapacity = 2;
    public const float FragFuse = 3.2f;          // s from the throw
    public const float SmokeFuse = 1.2f;         // s from the throw to the pop
    public const float SmokePour = 16f;          // s of smoke pouring out
    public const float SmokeLinger = 14f;        // s more for it to thin away once it's done
    public const float SmokeRadius = 4.5f;       // m: how far the cloud fills out round the can, in the open

    public const float BlastRadius = 2f;         // m: the full force of the blast
    public const float FragmentReach = 5f;       // m: fragments still hurt this far out (in the open)
    public const float BlastDamage = 130f;       // HP within the blast radius, in the open
    public const float ConfinedBoost = 2.25f;    // damage multiple, fully confined
    private const float ProbeReach = 6f;         // m: walls further than this don't confine it

    private const float ThrowSpeed = 13f;
    private const float ThrowLift = 0.2f;
    private const float CarryOver = 0.6f;
    private const float Step = Flight.Step;
    private const float LightTime = 0.2f;
    private const float DeafFade = 3f;

    private static readonly float3 FragArcColor = new(1.6f, 0.75f, 0.3f);
    private static readonly float3 SmokeArcColor = new(0.9f, 0.9f, 0.9f);
    private static readonly float3 FragTint = new(0.2f, 0.23f, 0.15f);
    private static readonly float3 SmokeTint = new(0.24f, 0.26f, 0.22f);
    private static readonly float3 SmokeBand = new(0.75f, 0.75f, 0.72f);
    private static readonly float3 FuseTint = new(0.45f, 0.45f, 0.43f);
    private static readonly float3 SmokeColor = new(0.68f, 0.69f, 0.7f);

    private sealed class Grenade
    {
        public Kind Kind;
        public float3 Position, Previous, Velocity;
        public bool Resting;
        public float Age;
        public float3 SpinAxis;
        public float Spin, Angle;
        public float Heading;
        public bool ByPlayer;
        public bool Popped;   // (smoke: it's pouring)
    }

    /// <summary>A smoke screen: where it pours from, how long it's been going, and how big it's grown (m).</summary>
    private sealed class Cloud
    {
        public float3 Source;
        public float Age;
        public float Radius;
        public float MaxRadius;
        public float Emit;     // puffs owed
        public float HissIn;
        public string Tag = "";
        public float Heading;

        public float Density => Age < SmokePour ? float.Min(1f, Age / 2.5f) : float.Max(0f, 1f - (Age - SmokePour) / SmokeLinger);
        public float3 Center => Source + new float3(0, float.Min(Radius * 0.45f, 1.4f), 0);

        /// <summary>
        /// How thick it looks (0..1), for the grey of standing in it: as its puffs fade (<see cref="Particles.Build"/>: thick
        /// until near the end), not as <see cref="Density"/> thins. The puffs right round the eye aren't drawn (the shader
        /// fades them out), so that grey is all there is to hide the world from inside, and must last as long as the cloud looks solid.
        /// </summary>
        public float Thickness
        {
            get
            {
                if (Age < SmokePour)
                    return float.Min(1f, Age / 2.5f);
                var u = float.Min(1f, (Age - SmokePour) / (SmokeLinger * 0.85f));
                return 1f - u * u * u;
            }
        }
    }

    private readonly Flight _flight;
    private readonly PhysicsWorld? _physics;
    private readonly Particles _particles;
    private readonly Particles _smoke;   // (its own pool: a cloud's hundreds of long-lived puffs mustn't crowd out the sparks and dust)
    private readonly Npcs _npcs;
    private readonly GameSounds _sounds;
    private readonly DebrisSystem _debris;
    private readonly List<Grenade> _live = [];
    private readonly List<Cloud> _clouds = [];
    private readonly List<(float3 Point, bool Break)> _arc = [];
    private readonly Random _random = new(919);
    private readonly Mesh _fragBody, _fuse, _smokeBody, _smokeBand;
    private readonly float3[] _probes;
    private float _accumulator;
    private Kind? _held;
    private int _respawns = int.MinValue;
    private int _cloudsMade;
    private float4 _light;
    private float _lightLeft, _deaf, _deafHold, _sinceBang = float.PositiveInfinity;

    public Throwables(Level level, PortalSystem portals, PhysicsWorld? physics, SurfaceTable surfaces, Particles particles, Npcs npcs, GameSounds sounds, DebrisSystem debris)
    {
        _flight = new Flight(level, portals, physics, surfaces);
        _physics = physics;
        _particles = particles;
        _smoke = new Particles(null, 900);
        _npcs = npcs;
        _sounds = sounds;
        _debris = debris;
        // An M67-ish ball (6.4 cm across) with its fuse and spoon; a canister for the smoke (along Z, the fuse towards +Z).
        _fragBody = new MeshBuilder().Blob(float3.Zero, new float3(0.032f, 0.032f, 0.035f), new Random(3), 12, 8).Build();
        _fuse = new MeshBuilder().Cylinder(0.009f, 0.028f, 0.05f, 10).Box(new float3(0.014f, 0, 0.03f), new float3(0.006f, 0.01f, 0.05f)).Build();
        _smokeBody = new MeshBuilder().Cylinder(0.03f, -0.07f, 0.045f, 16).Build();
        _smokeBand = new MeshBuilder().Cylinder(0.0305f, 0.0f, 0.02f, 16).Build();

        // The directions the blast feels for walls with: round the horizon and up (the ground is always there, so it doesn't count).
        var probes = new List<float3>();
        for (var ring = 0; ring < 4; ring++)
        {
            var elevation = ring * 0.42f + 0.06f;   // just above level (so open ground doesn't count), to near straight up
            var count = ring == 3 ? 4 : 10;
            for (var k = 0; k < count; k++)
            {
                var a = (k + 0.5f * ring) * float.Tau / count;
                probes.Add(new float3(float.Cos(a) * float.Cos(elevation), float.Sin(elevation), float.Sin(a) * float.Cos(elevation)));
            }
        }

        probes.Add(float3.UnitY);
        _probes = [.. probes];
    }

    public int Frags { get; private set; } = FragCapacity;
    public int Smokes { get; private set; } = SmokeCapacity;

    /// <summary>How many of each a respawn brings (a raid's loadout sets them): setting them fills up to them.</summary>
    public void SetStock(int frags, int smokes)
    {
        (Frags, _fragStock) = (frags, frags);
        (Smokes, _smokeStock) = (smokes, smokes);
    }
    private int _fragStock = FragCapacity, _smokeStock = SmokeCapacity;

    /// <summary>What the player's armour leaves of a frag's blast (Game/RaidLoadout.cs).</summary>
    public float PlayerBlastScale { get; set; } = 1f;

    /// <summary>Testing (BRUTALFPS_GRENADES=infinite): throwing one doesn't use it up.</summary>
    public bool Infinite { get; set; }

    /// <summary>Q or X is held: a throw's arc is plotted.</summary>
    public bool Aiming => _held is not null;

    /// <summary>An explosion lighting its surroundings, as <see cref="Renderer.SetFlash"/> takes it (w 0: none).</summary>
    public float4 Light => _lightLeft > 0 ? _light with { W = _light.W * (_lightLeft / LightTime) * (_lightLeft / LightTime) } : float4.Zero;

    /// <summary>How deaf an explosion has left the player (0..1), for <see cref="AudioEngine.Deafness"/>.</summary>
    public float Deafness => _deaf * float.Clamp(_sinceBang / 0.25f, 0f, 1f);

    // ------------------------------------------------------------------------------------------------ each frame

    /// <param name="canThrow">The player is free to (alive, playing, not at a terminal, not already aiming a flashbang).</param>
    public void Update(float dt, Input input, PlayerController player, in Camera camera, bool canThrow)
    {
        _npcPlansLeft = NpcPlansPerFrame;
        if (player.Respawns != _respawns)
        {
            if (_respawns != int.MinValue)
            {
                _live.Clear();
                foreach (var c in _clouds)
                    _sounds.FlareOut(c.Tag);
                _clouds.Clear();
                _smoke.Clear();   // (the clouds' puffs too, or the screen would hang there for another half a minute)
                _deaf = _deafHold = 0;
                _lightLeft = 0;
            }

            _respawns = player.Respawns;
            Frags = _fragStock;
            Smokes = _smokeStock;
        }

        // Holding Q (or X): plot the throw. Letting go: throw it. (One at a time: whichever was picked up first.)
        Kind? held = null;
        if (canThrow)
        {
            var frag = Frags > 0 && input.IsDown(GlfwKey.Q);
            var smoke = Smokes > 0 && input.IsDown(GlfwKey.X);
            held = _held switch
            {
                Kind.Frag when frag => Kind.Frag,
                Kind.Smoke when smoke => Kind.Smoke,
                _ => frag ? Kind.Frag : smoke ? Kind.Smoke : null,
            };
        }

        if (held is not null && _held != held)
            _sounds.PinPulled();
        if (_held is { } was && held != was && canThrow && (was == Kind.Frag ? Frags : Smokes) > 0)
            Throw(was, player, camera);
        _held = held;
        if (held is { } aiming)
        {
            var (from, velocity) = ThrowFrom(player, camera);
            PlotArc(from, velocity, aiming == Kind.Frag ? FragFuse : SmokeFuse + 2.5f);
            DrawArc(aiming == Kind.Frag ? FragArcColor : SmokeArcColor);
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
                if (g.Kind == Kind.Frag && g.Age >= FragFuse)
                {
                    _live.RemoveAt(i);
                    Explode(g, player, camera);
                    continue;
                }

                // Smoke pops once the fuse is burnt and it's come to rest (or soon after, if it's still rolling).
                if (g.Kind == Kind.Smoke && !g.Popped && g.Age >= SmokeFuse && (g.Resting || g.Age >= SmokeFuse + 2.5f))
                {
                    g.Popped = true;
                    StartSmoke(g);
                }

                var wasResting = g.Resting;
                if (_flight.Advance(ref g.Position, ref g.Velocity, ref g.Resting, out var impact, out var surface, out var jumped))
                {
                    _sounds.GrenadeBounce(g.Position, impact, surface);
                    g.Spin *= 0.5f;
                }

                if (jumped)
                    g.Previous = g.Position;
                if (g.Resting && !wasResting)
                    g.Heading = (float)_random.NextDouble() * float.Tau;
                g.Angle += g.Spin * Step;
                if (g.Position.Y < Level.KillHeightAt(g.Position) || (g.Kind == Kind.Smoke && g.Age > SmokeFuse + SmokePour + SmokeLinger))
                    _live.RemoveAt(i);
            }
        }

        UpdateClouds(dt);
        _smoke.Update(dt);

        _sinceBang += dt;
        _lightLeft -= dt;
        if (_deafHold > 0)
            _deafHold -= dt;
        else
            _deaf = float.Max(0f, _deaf - dt / DeafFade);
    }

    /// <summary>Rebuilds the smoke's batch (once per frame, after <see cref="Update"/>).</summary>
    public void BuildSmoke() => _smoke.Build();

    private (float3 From, float3 Velocity) ThrowFrom(PlayerController player, in Camera camera)
    {
        var forward = camera.Forward;
        var right = float3.Cross(forward, float3.UnitY);
        right = float3.Dot(right, right) > 1e-6f ? float3.Normalize(right) : float3.UnitX;
        var eye = camera.Position;
        var hand = eye + forward * 0.3f + right * 0.2f - new float3(0, 0.12f, 0);
        if (_physics is not null && _physics.Raycast(eye, hand, out var hit, includeBodies: false))
            hand = eye + (hand - eye) * float.Max(0f, hit.Fraction - 0.1f);
        var direction = float3.Normalize(forward + new float3(0, ThrowLift, 0));
        return (hand, direction * ThrowSpeed + player.Velocity * CarryOver);
    }

    private void Throw(Kind kind, PlayerController player, in Camera camera)
    {
        var (from, velocity) = ThrowFrom(player, camera);
        var axis = float3.Cross(velocity, float3.UnitY);
        axis = float3.Dot(axis, axis) > 1e-6f ? float3.Normalize(axis) : float3.UnitX;
        _live.Add(new Grenade
        {
            Kind = kind, Position = from, Previous = from, Velocity = velocity, SpinAxis = axis,
            Spin = 9f + 6f * (float)_random.NextDouble(), ByPlayer = true,
        });
        if (!Infinite)
        {
            if (kind == Kind.Frag)
                Frags--;
            else
                Smokes--;
        }

        _sounds.Thrown();
        Log.Write($"[{(kind == Kind.Frag ? "FRAG" : "SMOKE")}] Thrown from {from.X:0.0},{from.Y:0.0},{from.Z:0.0} ({(kind == Kind.Frag ? Frags : Smokes)} left)");
    }

    // ------------------------------------------------------------------------------------------------ NPCs' frags

    private const int NpcPlansPerFrame = 2;       // throws worked out a frame (each flies the arc a few times)
    private const float NpcPlanError = 2.5f;      // m: as near as it has to come down to where they meant
    private int _npcPlansLeft = NpcPlansPerFrame;

    /// <summary>
    /// How an NPC at <paramref name="from"/> would throw a frag to go off at <paramref name="target"/>, at no more than
    /// <paramref name="maxSpeed"/> m/s: tries a few angles, flying each (bounces and all) for as long as the fuse burns.
    /// Null: it can't be done from there (or there's been enough working out this frame).
    /// </summary>
    public (float3 Velocity, float3 Landing)? PlanNpcThrow(float3 from, float3 target, float maxSpeed)
    {
        if (_npcPlansLeft <= 0)
            return null;
        _npcPlansLeft--;
        if (_physics is not null && _physics.Raycast(target, target - new float3(0, 3f, 0), out var ground, includeBodies: false))
            target = ground.Point;
        var flat = new float3(target.X - from.X, 0, target.Z - from.Z);
        var distance = float.Sqrt(float3.Dot(flat, flat));
        if (distance < 3f)
            return null;
        flat /= distance;
        var rise = target.Y - from.Y;
        (float3 Velocity, float3 Landing)? best = null;
        var bestMiss = float.MaxValue;
        // Lobbed, mostly: a high one rolls less once it's down. Each angle is tried twice: the second time aimed short by as
        // far as the first rolled on after it came down.
        ReadOnlySpan<float> angles = [40f, 55f, 25f, 68f];
        foreach (var degrees in angles)
        {
            var (sin, cos) = float.SinCos(float.DegreesToRadians(degrees));
            var aim = distance;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var below = 2f * cos * cos * (aim * sin / cos - rise);
                if (below <= 0f || aim < 1f)
                    break;
                var speed = float.Sqrt(9.81f * aim * aim / below);
                if (speed > maxSpeed)
                    break;
                var velocity = (flat * cos + float3.UnitY * sin) * speed;
                var position = from;
                var v = velocity;
                var resting = false;
                float3? down = null;   // where it first came down
                for (var t = 0f; t < FragFuse && !resting; t += Step)
                {
                    if (_flight.Advance(ref position, ref v, ref resting, out _, out _, out _) && down is null)
                        down = position;
                    if (position.Y < Level.KillHeightAt(position))
                        break;
                }

                var miss = float3.Distance(position, target);
                if (miss < bestMiss)
                {
                    bestMiss = miss;
                    best = (velocity, position);
                }

                if (miss < 1.2f || down is not { } landed)
                    break;
                // It rolled (or bounced) on from where it came down: aim that much shorter.
                var rolled = float3.Dot(position - landed, flat);
                if (float.Abs(rolled) < 0.5f)
                    break;
                aim -= rolled;
            }

            if (bestMiss < 1.2f)
                break;
        }

        if (bestMiss > NpcPlanError)
        {
            Log.Write($"[FRAG] No throw from {from.X:0.0},{from.Y:0.0},{from.Z:0.0} to {target.X:0.0},{target.Y:0.0},{target.Z:0.0} ({distance:0.0} m, rise {rise:0.0}): the nearest comes {(bestMiss == float.MaxValue ? "nowhere" : $"{bestMiss:0.0} m")} off");
            return null;
        }

        return best;
    }

    /// <summary>An NPC's frag, thrown from <paramref name="from"/> at <paramref name="velocity"/> (its fuse already burning).</summary>
    public void ThrowFromNpc(Npc thrower, float3 from, float3 velocity)
    {
        var axis = float3.Cross(velocity, float3.UnitY);
        axis = float3.Dot(axis, axis) > 1e-6f ? float3.Normalize(axis) : float3.UnitX;
        _live.Add(new Grenade
        {
            Kind = Kind.Frag, Position = from, Previous = from, Velocity = velocity, SpinAxis = axis,
            Spin = 9f + 6f * (float)_random.NextDouble(), ByPlayer = false,
        });
        Log.Write($"[FRAG] {thrower.Name} threw a frag from {from.X:0.0},{from.Y:0.0},{from.Z:0.0}");
    }

    // ------------------------------------------------------------------------------------------------ the arc

    private void PlotArc(float3 from, float3 velocity, float time)
    {
        _arc.Clear();
        _arc.Add((from, false));
        var position = from;
        var resting = false;
        var steps = (int)(time / Step);
        for (var i = 0; i < steps && !resting; i++)
        {
            _flight.Advance(ref position, ref velocity, ref resting, out _, out _, out var jumped);
            _arc.Add((position, jumped));
            if (position.Y < Level.KillHeightAt(position))
                break;
        }
    }

    private void DrawArc(float3 color)
    {
        var dash = 0;
        var travelled = 0f;
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
            for (var t = 0f; t < length; t += 0.125f, dash++)
            {
                if ((dash & 1) == 1)
                    continue;
                var away = travelled + t;
                if (away < 0.8f)
                    continue;
                var head = a + ab * float.Min(1f, (t + 0.125f) / length);
                var tail = a + ab * (t / length);
                _particles.Streak(head, tail, color, 0.006f + 0.002f * away / 10f, float.Min(0.7f, (away - 0.8f) * 1.5f));
            }

            travelled += length;
        }

        // Where it'll go off: a ring the size of the blast's full force for a frag, a small one for smoke.
        var end = _arc[^1].Point;
        var radius = _held == Kind.Frag ? BlastRadius : 0.3f;
        var segments = _held == Kind.Frag ? 40 : 16;
        for (var k = 0; k < segments; k++)
        {
            var a0 = k * float.Tau / segments;
            var a1 = (k + 1) * float.Tau / segments;
            var p0 = end + new float3(float.Cos(a0), 0.02f, float.Sin(a0)) * radius;
            var p1 = end + new float3(float.Cos(a1), 0.02f, float.Sin(a1)) * radius;
            _particles.Streak(p1, p0, color * 1.4f, 0.01f, 0.8f);
        }
    }

    // ------------------------------------------------------------------------------------------------ the frag

    /// <summary>
    /// How shut in a blast at <paramref name="at"/> is (0 = in the open, 1 = a small room): rays out round the horizon
    /// and up, each counting for more the nearer the wall or ceiling it meets.
    /// </summary>
    public float Confinement(float3 at)
    {
        if (_physics is null)
            return 0f;
        var from = at + new float3(0, 0.5f, 0);   // (the heart of the blast)
        var sum = 0f;
        foreach (var d in _probes)
            if (_physics.Raycast(from, from + d * ProbeReach, out var hit, includeBodies: false))
                sum += 1f - hit.Fraction;
        // A 4 m square room with a 2.6 m ceiling comes to about 0.6 of the most there could be: call that fully confined.
        return float.Clamp(sum / _probes.Length / 0.6f, 0f, 1f);
    }

    /// <summary>
    /// The health a blast takes from someone <paramref name="distance"/> m away: all of it within <see cref="BlastRadius"/>,
    /// then the fragments' share, thinning out to nothing at <see cref="FragmentReach"/>. Confined, it's all harder and
    /// reaches further, and gets round corners (where, in the open, a wall would stop it).
    /// </summary>
    public static float Damage(float distance, bool inSight, float confinement)
    {
        var boost = 1f + (ConfinedBoost - 1f) * confinement;
        var reach = FragmentReach * (1f + 0.3f * confinement);
        float share;
        if (distance <= BlastRadius)
            share = 1f;
        else if (distance >= reach)
            return 0f;
        else
        {
            var t = 1f - (distance - BlastRadius) / (reach - BlastRadius);
            share = 0.6f * t * t;
        }

        // Behind cover: in the open nothing gets to you; shut in, the pressure fills the room and finds you anyway.
        var cover = inSight ? 1f : 0.55f * confinement * confinement;
        return BlastDamage * boost * share * cover;
    }

    private void Explode(Grenade g, PlayerController player, in Camera camera)
    {
        var at = g.Position + new float3(0, 0.05f, 0);
        var confinement = Confinement(at);
        Log.Write($"[FRAG] Boom at {at.X:0.0},{at.Y:0.0},{at.Z:0.0} ({Level.ZoneAt(at)}), confinement {confinement:0.00} (x{1f + (ConfinedBoost - 1f) * confinement:0.00} damage)");
        _sounds.Explosion(at);
        _light = new float4(at, 10f + 6f * confinement);
        _lightLeft = LightTime;

        // The fireball, the fragments flying, the dirt kicked up and a dirty cloud.
        _particles.Add(new Particles.Particle
        {
            Position = at + new float3(0, 0.2f, 0), Kind = ParticleKind.Flash, Color = new float3(28f, 14f, 5f), Size = 2.2f, Life = 0.12f, Alpha = 1f,
            Rotation = (float)_random.NextDouble() * float.Tau,
        });
        for (var i = 0; i < 50; i++)
        {
            var d = Particles.RandomDirection();
            d.Y = float.Abs(d.Y) * 0.7f;
            _particles.Add(new Particles.Particle
            {
                Position = at, Velocity = d * Particles.Range(12f, 30f), Kind = ParticleKind.Spark,
                Color = new float3(8f, 4.5f, 1.8f), Size = 0.005f, Life = Particles.Range(0.15f, 0.4f), Alpha = 1f,
                Gravity = 0.6f, Drag = 1f, StreakTime = 0.015f, Bounce = true,
            });
        }

        for (var i = 0; i < 18; i++)
        {
            var d = Particles.RandomDirection();
            d.Y = float.Abs(d.Y);
            _particles.Add(new Particles.Particle
            {
                Position = at + d * 0.2f, Velocity = d * Particles.Range(1.5f, 5f) + new float3(0, 1f, 0),
                Kind = ParticleKind.Dust, Color = new float3(0.24f, 0.22f, 0.2f), Size = Particles.Range(0.25f, 0.5f), Growth = Particles.Range(0.5f, 1.2f),
                Life = Particles.Range(3f, 6f), Alpha = 0.75f, Gravity = -0.03f, Drag = 2.2f, Spin = Particles.Range(-0.5f, 0.5f),
            });
        }

        _debris.Chunks(at, float3.UnitY, -float3.UnitY, new float3(0.35f, 0.32f, 0.28f), 10);
        _debris.Blast(at, FragmentReach, 9f + 6f * confinement);

        // People: hurt (or thrown, if dead already), and those near enough who live are knocked senseless.
        _npcs.Blast(at, (distance, inSight) => Damage(distance, inSight, confinement), stunReach: 4f + 2f * confinement, byPlayer: g.ByPlayer);

        // The player.
        if (!player.Alive || (Level.ZonesSeenFrom(camera.Position) & Level.ZoneAt(at)) == 0)
            return;
        var chest = player.Position + new float3(0, float.Clamp(at.Y - player.Position.Y, 0.25f, 1.4f), 0);
        var to = chest - at;
        var distance = float.Sqrt(float3.Dot(to, to));
        var inSight = _npcs.Senses.Clear?.Invoke(at + new float3(0, 0.1f, 0), chest) is not { } clear || clear > 0.95f;
        var damage = Damage(distance, inSight, confinement) * PlayerBlastScale;
        if (damage > 0.5f)
        {
            player.TakeDamage(damage, at);
            _sounds.PlayerHurt(damage);
            // Thrown off your feet (a shove, at least).
            var push = distance > 1e-3f ? to / distance : float3.UnitY;
            player.Velocity += new float3(push.X, 0.35f, push.Z) * float.Min(8f, damage * 0.06f);
        }

        var deaf = float.Clamp(1f - (distance - 1f) / (9f + 5f * confinement), 0f, 1f) * (inSight ? 1f : 0.6f);
        if (deaf > 0.05f)
        {
            if (deaf > _deaf)
            {
                _deaf = deaf;
                _deafHold = 0.5f + 3f * deaf;
            }

            _sinceBang = 0;
            _sounds.Ringing(deaf);
        }

        Log.Write($"[FRAG] The player: {distance:0.0} m away, {(inSight ? "in sight" : "behind cover")}: -{damage:0} HP, deaf {deaf:0.00}");
    }

    // ------------------------------------------------------------------------------------------------ the smoke

    private void StartSmoke(Grenade g)
    {
        // In a room the cloud fills it rather than spreading on through the walls: as far as the walls round it allow.
        var reach = SmokeRadius;
        if (_physics is not null)
        {
            var from = g.Position + new float3(0, 0.3f, 0);
            var sum = 0f;
            var n = 0;
            foreach (var d in _probes)
            {
                if (d.Y > 0.5f)
                    continue;
                sum += _physics.Raycast(from, from + d * SmokeRadius * 1.5f, out var hit, includeBodies: false) ? hit.Fraction * SmokeRadius * 1.5f : SmokeRadius * 1.5f;
                n++;
            }

            reach = float.Clamp(sum / float.Max(n, 1) * 0.9f, 1.8f, SmokeRadius);
        }

        var cloud = new Cloud { Source = g.Position, MaxRadius = reach, Radius = 0.5f, Tag = $"smoke{_cloudsMade++}", Heading = (float)_random.NextDouble() * float.Tau };
        _clouds.Add(cloud);
        _sounds.SmokePop(g.Position);
        Log.Write($"[SMOKE] Popped at {g.Position.X:0.0},{g.Position.Y:0.0},{g.Position.Z:0.0} ({Level.ZoneAt(g.Position)}): a cloud {reach:0.0} m round");
    }

    private void UpdateClouds(float dt)
    {
        for (var i = _clouds.Count - 1; i >= 0; i--)
        {
            var c = _clouds[i];
            c.Age += dt;
            if (c.Age >= SmokePour + SmokeLinger)
            {
                _sounds.FlareOut(c.Tag);
                _clouds.RemoveAt(i);
                continue;
            }

            // It billows out fast at first, then fills in; once it stops pouring it spreads a little more as it thins.
            var target = c.Age < SmokePour ? c.MaxRadius * float.Min(1f, 0.35f + c.Age / 6f) : c.MaxRadius * (1f + 0.15f * (c.Age - SmokePour) / SmokeLinger);
            c.Radius += (target - c.Radius) * float.Min(1f, dt * 1.5f);

            if (c.Age >= SmokePour)
            {
                _sounds.FlareOut(c.Tag);
                continue;
            }

            c.HissIn -= dt;
            if (c.HissIn <= 0)
            {
                _sounds.SmokeHiss(c.Source, c.Tag);
                c.HissIn = 1.8f;
            }

            // Puffs: thick and fast to begin with. (About 180 of them in all, a little bigger, where there were 270: every
            // one is drawn over the others, and the cloud was far thicker to the eye than it needed to be to hide anything.)
            c.Emit += dt * (c.Age < 3f ? 20f : 9f);
            while (c.Emit >= 1f)
            {
                c.Emit -= 1f;
                Puff(c);
            }
        }
    }

    private void Puff(Cloud c)
    {
        var d = Particles.RandomDirection();
        d.Y = float.Abs(d.Y) * 0.55f + 0.05f;
        d = float3.Normalize(d);
        var from = c.Source + new float3(0, 0.08f, 0);
        // Out to somewhere in the cloud, but no further than the first wall that way (drag brings it to a stop: v / drag m).
        var want = Particles.Range(0.2f, 1f) * c.MaxRadius;
        if (_physics is not null && _physics.Raycast(from, from + d * want, out var hit, includeBodies: false))
            want = float.Max(0.1f, hit.Fraction * want - 0.6f);
        const float drag = 0.9f;
        _smoke.Add(new Particles.Particle
        {
            Position = from, Velocity = d * want * drag, Kind = ParticleKind.Smoke,
            Color = SmokeColor * Particles.Range(0.9f, 1.05f), Size = Particles.Range(0.36f, 0.58f), Growth = Particles.Range(0.05f, 0.09f),
            Life = SmokePour - c.Age + SmokeLinger * Particles.Range(0.7f, 1f), Alpha = Particles.Range(0.55f, 0.8f),
            Gravity = -0.004f, Drag = drag, Rotation = Particles.Range(0f, float.Tau), Spin = Particles.Range(-0.15f, 0.15f),
        });
    }

    /// <summary>
    /// How much smoke there is from <paramref name="a"/> to <paramref name="b"/> (for <see cref="NpcSenses.Smoke"/>): the
    /// length of the line inside each cloud, as a share of that cloud's radius, by how thick it is. 0 = none.
    /// </summary>
    public float SmokeBetween(float3 a, float3 b)
    {
        var total = 0f;
        foreach (var c in _clouds)
            total += Chord(a, b, c.Center, c.Radius) / c.Radius * c.Density;
        return total;
    }

    /// <summary>How thick the smoke is right at <paramref name="eye"/> (0..1): the player inside a cloud sees grey.</summary>
    public float SmokeAt(float3 eye)
    {
        var most = 0f;
        foreach (var c in _clouds)
        {
            var dist = float3.Distance(eye, c.Center);
            most = float.Max(most, float.Clamp((c.Radius * 0.85f - dist) / 1.2f, 0f, 1f) * c.Thickness);
        }

        return most;
    }

    /// <summary>The length of the segment a-b inside the sphere.</summary>
    private static float Chord(float3 a, float3 b, float3 center, float radius)
    {
        var ab = b - a;
        var len = float.Sqrt(float3.Dot(ab, ab));
        if (len < 1e-4f)
            return 0f;
        var dir = ab / len;
        var m = a - center;
        var bq = float3.Dot(m, dir);
        var cq = float3.Dot(m, m) - radius * radius;
        var disc = bq * bq - cq;
        if (disc <= 0f)
            return 0f;
        var root = float.Sqrt(disc);
        var t0 = float.Max(0f, -bq - root);
        var t1 = float.Min(len, -bq + root);
        return float.Max(0f, t1 - t0);
    }

    /// <summary>The frags in flight or lying about, for people to get clear of (<see cref="NpcSenses.FindGrenades"/>). (Smoke does no harm.)</summary>
    public void ReportGrenades(List<LiveGrenade> into)
    {
        foreach (var g in _live)
        {
            if (g.Kind == Kind.Frag)
                into.Add(new LiveGrenade(g.Position, g.Resting ? float3.Zero : g.Velocity, g.Resting, FragFuse - g.Age, FragmentReach, Flash: false));
        }
    }

    // ------------------------------------------------------------------------------------------------ drawing

    /// <summary>The ones in flight or lying about.</summary>
    public void Draw(Renderer renderer, Zones zones)
    {
        var t = _accumulator / Step;
        foreach (var g in _live)
        {
            var at = float3.Lerp(g.Previous, g.Position, t);
            if ((zones & Level.ZoneAt(at)) == 0)
                continue;
            var lying = g.Kind == Kind.Smoke ? 0.03f : 0.032f;
            var turn = g.Resting
                ? float4x4.CreateRotationY(g.Heading)
                : float4x4.CreateFromAxisAngle(g.SpinAxis, g.Angle) * float4x4.CreateRotationX(0.4f);
            var model = turn * float4x4.CreateTranslation(at + (g.Resting ? new float3(0, lying - Flight.Radius, 0) : float3.Zero));
            if (g.Kind == Kind.Frag)
                DrawFrag(renderer, model);
            else
                DrawSmokeCan(renderer, model);
        }
    }

    /// <summary>The smoke screens (see-through: draw them with the other particles, last).</summary>
    public void DrawSmoke(Renderer renderer) => _smoke.Draw(renderer, smokeScreen: true);

    public void DrawFrag(Renderer renderer, in float4x4 model, float brightness = 1f)
    {
        renderer.DrawMesh(_fragBody, model, FragTint * brightness, ShadeMode.Lit, Material.Plain, new float2(0.7f, 0.3f));
        renderer.DrawMesh(_fuse, model, FuseTint, ShadeMode.Lit, Material.Plain, new float2(0.35f, 0.8f));
    }

    public void DrawSmokeCan(Renderer renderer, in float4x4 model, float brightness = 1f)
    {
        renderer.DrawMesh(_smokeBody, model, SmokeTint * brightness, ShadeMode.Lit, Material.Plain, new float2(0.6f, 0.4f));
        renderer.DrawMesh(_smokeBand, model, SmokeBand, ShadeMode.Lit, Material.Plain, new float2(0.6f, 0.2f));
        renderer.DrawMesh(_fuse, float4x4.CreateTranslation(0, 0, 0.017f) * model, FuseTint, ShadeMode.Lit, Material.Plain, new float2(0.35f, 0.8f));
    }
}
