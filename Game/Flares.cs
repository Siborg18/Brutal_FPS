using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Flares, used by armed people who've lost the player in the dark (<see cref="ArmedNpc"/> decides when, by
/// <see cref="NpcSenses.LightAt"/>). Two kinds:
/// <list type="bullet">
/// <item>Red signal flares, thrown by hand. One flies as a flashbang does (<see cref="Flight"/>), trailing sparks, then lies
/// burning for <see cref="BurnTime"/> seconds, hissing and smoking.</item>
/// <item>Illumination flares, fired out of a long gun's launcher <see cref="SkyHeight"/> m up over where the player is
/// thought to be. At the top it lights, white, and hangs under its little parachute, sinking slowly, for
/// <see cref="SkyBurnTime"/> seconds, putting <see cref="SkyLux"/> lux on the ground under it and lighting the ground
/// <see cref="SkyRadius"/> m round (a 500 m wide circle).</item>
/// </list>
/// <para>
/// Its light is real both ways. On screen, it's a point light with shadows (<see cref="Renderer.SetFlares"/>): once it
/// settles, the level round it is drawn once into the six faces of its tiles in the <see cref="FlareShadowAtlas"/> (so walls
/// stop it and doorways throw shadows; people cast none). One in the sky is drawn again each time it has sunk or drifted
/// <see cref="SkyShadowMove"/> m. To people's eyes, it adds to the light on the player (<see cref="LightOn"/>), so anyone
/// standing near one (or anywhere under one in the sky, in the open) can be made out from much further off.
/// </para>
/// </summary>
public sealed class Flares
{
    public const int MaxBurning = FlareShadowAtlas.MaxFlares;
    public const float BurnTime = 30f;          // s, from the moment it's lit
    private const float DyingTime = 4f;         // s: sputtering out at the end
    private const float Range = 22f;            // m: how far its light reaches once it's lying still
    private const float FlyingRange = 9f;       // m: in flight, with no shadows yet to stop it at the walls
    private const float Brightness = 55f;       // irradiance 1 m away (game units)
    private const float LightHeight = 0.45f;    // m above the flare: what lights the floor round it (the flame's glow in its smoke)
    private const float SeenRange = 18f;        // m: how far its light helps people see someone
    private const float MaxFlightTime = 3f;     // s of a throw plotted ahead
    private const float PlanError = 7f;         // m: a throw must end this near where it's aimed
    private const float NotAtOwnFeet = 3.5f;    // m: and this far from whoever throws it
    private const float ClaimTime = 1.2f;       // s: someone about to throw one keeps others from throwing one there too
    private const float HissEvery = 2f;         // s between the overlapping takes of its hiss
    private static readonly float3 LightColor = new(1f, 0.13f, 0.055f);
    private static readonly float3 BodyTint = new(0.45f, 0.06f, 0.05f);
    private static readonly float3 FlameColor = new(30f, 7f, 3.5f);

    // Illumination flares, in the sky.
    public const float SkyHeight = 100f;        // m above the ground where it's aimed: where it lights (SKY_FLARE_HEIGHT in the shader)
    public const float SkyBurnTime = 60f;      // s: four minutes, from the moment it lights at the top
    public const float SkyRadius = 250f;        // m: the circle of ground it lights (500 m across)
    public const float SkyLux = 5f;             // lux on the ground straight under it, from SkyHeight m up
    private const float SkyDyingTime = 8f;      // s: guttering at the end
    private const float SkyCatchTime = 1f;      // s from the pop at the top to full brightness
    private const float SkyFall = 0.15f;        // m/s it sinks under its parachute (to about 64 m by the end)
    private const float SkyDrift = 0.1f;        // m/s it drifts on the air
    private const float SkyShadowMove = 1.5f;   // m it may sink or drift before its shadows are drawn again
    private const float SkySeenFrom = 0.6f;     // of its radius: no one fires another whose circle would be centred this near
    private const float SkyMaxAcross = 40f;     // m/s: the most a launcher puts across into getting it over the place
    // Lux to the game's units. The night is graded the way the eye adapts to it: full moonlight (about 0.25 lux) is about
    // 0.2 in the game's units (the jungle's moon: Renderer's MOON_LEVEL), and brighter light counts for less than its lux
    // would say: game irradiance goes as lux to the SkyGamma (SKY_FLARE_GAMMA in the shader). So 5 lux, twenty times
    // the moon, is about three times the moon's light on screen, and the edge of its circle about the moon's.
    private const float MoonLux = 0.25f, MoonIrradiance = 0.2f, SkyGamma = 0.37f;
    private static readonly float SkyBrightness = MoonIrradiance * float.Pow(SkyLux / MoonLux, SkyGamma);   // game irradiance SkyHeight m off
    private static readonly float3 SkyLightColor = new(1f, 0.88f, 0.7f);   // magnesium: a hard, faintly warm white
    private static readonly float3 SkyFlameColor = new(40f, 34f, 26f);
    private static readonly float3 CanopyTint = new(0.75f, 0.74f, 0.7f);

    private sealed class Flare
    {
        public float3 Position, Previous, Velocity, LightAt, SpinAxis, ShadowAt, Drift;
        public bool Resting, ShadowsReady;
        public bool Sky, Rising;   // an illumination flare; on its way up, not lit yet
        public float Age, SinceRest, Spin, Angle, Heading, Flicker = 1f, HissLeft, SparkLeft, SmokeLeft, DripLeft;
        public float Life = BurnTime;
        public int Slot;
        public Zones Zone;
        public string Tag = "";
    }

    private readonly Flight _flight;
    private readonly PhysicsWorld? _physics;
    private readonly Particles _particles;
    private readonly GameSounds _sounds;
    private readonly List<Flare> _live = [];
    private readonly List<(float3 At, float Left, bool Sky)> _claims = [];
    private readonly bool[] _slotTaken = new bool[MaxBurning];
    private readonly Random _random = new(1907);
    private readonly Mesh _body, _tip, _canopy;
    private readonly float4[] _lightSpots = new float4[MaxBurning], _lightColors = new float4[MaxBurning];
    private float _accumulator;
    private int _respawns = int.MinValue;
    private int _lit;
    private int _plansLeft;

    public Flares(Level level, PortalSystem portals, PhysicsWorld? physics, SurfaceTable surfaces, Particles particles, GameSounds sounds)
    {
        _flight = new Flight(level, portals, physics, surfaces);
        _physics = physics;
        _particles = particles;
        _sounds = sounds;
        // A road flare's stick (along Z): a waxed red tube, the burning end white-hot.
        _body = new MeshBuilder().Cylinder(0.014f, -0.11f, 0.09f, 10).Build();
        _tip = new MeshBuilder().Cylinder(0.011f, 0.09f, 0.115f, 8).Build();
        // An illumination flare's parachute (along Z, up once it hangs): a shallow drum of white nylon.
        _canopy = new MeshBuilder().Cylinder(0.5f, 0f, 0.14f, 14).Build();
    }

    /// <summary>Whether there's room for another (what the shader and the shadow atlas can light at once).</summary>
    public bool RoomForOne => _live.Count + _claims.Count < MaxBurning;

    /// <summary>How many are alight.</summary>
    public int Burning => _live.Count;

    /// <summary>
    /// Whether one is burning, on its way or about to be thrown within <paramref name="radius"/> m of <paramref name="at"/>;
    /// or one in the sky (or on its way up) is lighting the ground round there.
    /// </summary>
    public bool Near(float3 at, float radius)
    {
        foreach (var flare in _live)
            if (flare.Sky ? Across(flare.Position, at) < SkyRadius * SkySeenFrom : float3.Distance(flare.Position, at) < radius)
                return true;
        foreach (var (claim, _, sky) in _claims)
            if (sky ? Across(claim, at) < SkyRadius * SkySeenFrom : float3.Distance(claim, at) < radius)
                return true;
        return false;
    }

    /// <summary>The distance across the ground (leaving out height) from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static float Across(float3 a, float3 b) => float.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>
    /// How much light the flares put on <paramref name="p"/> for people's eyes (0..1, on top of <see cref="Level.LightAt"/>),
    /// from those with a clear line to it. <paramref name="clear"/> is <see cref="NpcSenses.Clear"/>: one ray per flare in range.
    /// </summary>
    public float LightOn(float3 p, Func<float3, float3, float> clear)
    {
        var light = 0f;
        foreach (var flare in _live)
        {
            if (flare.Sky)
            {
                // Bright enough to see by across most of its circle, fading over the outer part.
                if (flare.Rising || !flare.ShadowsReady)
                    continue;
                var across = Across(flare.ShadowAt, p) / SkyRadius;
                if (across >= 1f || clear(flare.ShadowAt, p) < 0.98f)
                    continue;
                var a2 = across * across;
                light += 1.1f * (1f - a2 * a2) * Strength(flare);
                continue;
            }

            var d = float3.Distance(flare.LightAt, p);
            if (d >= SeenRange || clear(flare.LightAt, p) < 0.98f)
                continue;
            var falloff = 1f - d / SeenRange;
            light += 1.15f * falloff * falloff * Strength(flare);
        }

        return light;
    }

    // ------------------------------------------------------------------------------------------------ throwing one

    /// <summary>
    /// Works out how to get one from <paramref name="from"/> to the ground under <paramref name="target"/> at no more than
    /// <paramref name="maxSpeed"/> m/s, trying a few angles (<paramref name="low"/>: flat, as out of a launcher; else lobbed)
    /// and flying each ahead. False: no way there (a wall, a low ceiling, too far). One attempt a frame among everyone: those
    /// are a lot of rays.
    /// </summary>
    public bool Plan(float3 from, float3 target, float maxSpeed, bool low, out float3 velocity, out float3 landing)
    {
        velocity = landing = float3.Zero;
        if (_plansLeft <= 0)
            return false;
        _plansLeft--;

        if (_physics is not null && _physics.Raycast(target, target - new float3(0, 3f, 0), out var ground, includeBodies: false))
            target = ground.Point;

        var flat = new float3(target.X - from.X, 0, target.Z - from.Z);
        var distance = float.Sqrt(float3.Dot(flat, flat));
        if (distance < 1f)
            return false;
        flat /= distance;
        var rise = target.Y - from.Y;

        var best = float.MaxValue;
        ReadOnlySpan<float> angles = low ? [5f, 14f, 28f] : [22f, 38f, 55f];
        foreach (var degrees in angles)
        {
            var (sin, cos) = float.SinCos(float.DegreesToRadians(degrees));
            // The speed that, at this angle, comes down on the target.
            var below = 2f * cos * cos * (distance * sin / cos - rise);
            if (below <= 0f)
                continue;
            var speed = float.Sqrt(9.81f * distance * distance / below);
            if (speed > maxSpeed)
                continue;
            var v = (flat * cos + float3.UnitY * sin) * speed;
            var end = FlyAhead(from, v);
            var miss = float3.Distance(end, target);
            if (miss < best && float3.Distance(end, from) > NotAtOwnFeet)
            {
                best = miss;
                velocity = v;
                landing = end;
            }

            if (miss < 1.5f)
                break;   // (the flattest that gets there: the first that does)
        }

        return best <= PlanError;
    }

    /// <summary>
    /// Works out how to fire an illumination flare from <paramref name="from"/> so it tops out <see cref="SkyHeight"/> m over
    /// the ground under <paramref name="target"/>, and checks its climb is clear of roofs, branches and walls (a few rays).
    /// <paramref name="over"/>: the point on the ground it will light. False: there's something in the way, or it's too far.
    /// Shares <see cref="Plan"/>'s one attempt a frame.
    /// </summary>
    public bool PlanHigh(float3 from, float3 target, out float3 velocity, out float3 over)
    {
        velocity = float3.Zero;
        over = target;
        if (_plansLeft <= 0)
            return false;
        _plansLeft--;

        if (_physics is not null && _physics.Raycast(target, target - new float3(0, 3f, 0), out var ground, includeBodies: false))
            over = ground.Point;
        var rise = over.Y + SkyHeight - from.Y;
        if (rise < SkyHeight * 0.5f)
            return false;   // (it'd be up on a height above them: no)

        // Straight up as high as that, and across meanwhile.
        var up = float.Sqrt(2f * 9.81f * rise);
        var time = up / 9.81f;
        var across = new float3(over.X - from.X, 0, over.Z - from.Z) / time;
        if (float3.Dot(across, across) > SkyMaxAcross * SkyMaxAcross)
            return false;
        velocity = across + float3.UnitY * up;

        if (_physics is null)
            return true;
        const int Legs = 8;
        var at = from;
        for (var i = 1; i <= Legs; i++)
        {
            var t = time * i / Legs;
            var next = from + velocity * t - new float3(0, 4.905f * t * t, 0);
            if (_physics.Raycast(at, next, out _, includeBodies: false))
                return false;
            at = next;
        }

        return true;
    }

    private float3 FlyAhead(float3 from, float3 velocity)
    {
        var position = from;
        var resting = false;
        var steps = (int)(MaxFlightTime / Flight.Step);
        for (var i = 0; i < steps && !resting; i++)
            _flight.Advance(ref position, ref velocity, ref resting, out _, out _, out _);
        return position;
    }

    /// <summary>Someone is about to throw one there (<paramref name="sky"/>: fire one up over there): nobody else need.</summary>
    public void Claim(float3 at, bool sky = false) => _claims.Add((at, ClaimTime, sky));

    /// <summary>The flame in the hand of someone about to throw one, for this frame.</summary>
    public void GlowInHand(float3 at) => Flame(at, 0.7f);

    /// <summary>The scrape of it being struck.</summary>
    public void Strike(float3 at) => _sounds.FlareStruck(at);

    /// <summary>
    /// It's away, lit. <paramref name="fired"/>: out of a launcher (a pop), not by hand. <paramref name="high"/>: an
    /// illumination flare (<see cref="PlanHigh"/>), lighting at the top of its climb.
    /// </summary>
    public void Launch(float3 from, float3 velocity, bool fired, bool high = false)
    {
        var slot = Array.IndexOf(_slotTaken, false);
        if (slot < 0)
            return;
        _slotTaken[slot] = true;
        if (_claims.Count > 0)
            _claims.RemoveAt(0);
        var axis = float3.Cross(velocity, float3.UnitY);
        axis = float3.Dot(axis, axis) > 1e-6f ? float3.Normalize(axis) : float3.UnitX;
        var drift = (float)_random.NextDouble() * float.Tau;
        _live.Add(new Flare
        {
            Position = from, Previous = from, LightAt = from, Velocity = velocity, SpinAxis = axis, Slot = slot,
            Spin = high ? 2f : 7f + 6f * (float)_random.NextDouble(), Tag = $"flare{++_lit}",
            Sky = high, Rising = high, Life = high ? SkyBurnTime : BurnTime, Zone = Level.ZoneAt(from),
            Drift = new float3(float.Cos(drift), 0f, float.Sin(drift)) * SkyDrift,
        });
        if (fired)
            _sounds.FlareFired(from);
        Log.Write($"[FLARE] {(high ? "Fired up" : fired ? "Fired" : "Thrown")} from {from.X:0.0},{from.Y:0.0},{from.Z:0.0} at {float.Sqrt(float3.Dot(velocity, velocity)):0.0} m/s ({Level.ZoneAt(from)}), {_live.Count} burning");
    }

    /// <summary>An illumination flare lit and hanging at <paramref name="at"/> already (BRUTALFPS_SKYFLARETEST).</summary>
    public void Hang(float3 at)
    {
        Launch(at, float3.Zero, fired: false, high: true);
        if (_live.Count > 0 && _live[^1] is { Sky: true } f)
            Ignite(f);
    }

    // ------------------------------------------------------------------------------------------------ each frame

    public void Update(float dt, PlayerController player)
    {
        _plansLeft = 1;
        if (player.Respawns != _respawns)
        {
            if (_respawns != int.MinValue)
                Clear();
            _respawns = player.Respawns;
        }

        for (var i = _claims.Count - 1; i >= 0; i--)
        {
            var (at, left, sky) = _claims[i];
            if (left <= dt)
                _claims.RemoveAt(i);
            else
                _claims[i] = (at, left - dt, sky);
        }

        _accumulator += dt;
        while (_accumulator >= Flight.Step)
        {
            _accumulator -= Flight.Step;
            foreach (var f in _live)
            {
                if (f.Sky)
                    FlyHigh(f);
                else
                    Fly(f);
            }
        }

        for (var i = _live.Count - 1; i >= 0; i--)
        {
            var f = _live[i];
            if (!f.Rising)
                f.Age += dt;
            if (f.Age >= f.Life || f.Position.Y < Level.KillHeightAt(f.Position))
            {
                Log.Write($"[FLARE] Burned out at {f.Position.X:0.0},{f.Position.Y:0.0},{f.Position.Z:0.0}");
                _sounds.FlareOut(f.Tag);
                _slotTaken[f.Slot] = false;
                _live.RemoveAt(i);
                continue;
            }

            if (f.Sky)
                BurnHigh(f, dt);
            else
                Burn(f, dt);
        }
    }

    private void Clear()
    {
        foreach (var f in _live)
            _sounds.FlareOut(f.Tag);
        _live.Clear();
        _claims.Clear();
        Array.Clear(_slotTaken);
    }

    private void Fly(Flare f)
    {
        f.Previous = f.Position;
        if (f.Resting)
            return;
        if (_flight.Advance(ref f.Position, ref f.Velocity, ref f.Resting, out var impact, out var surface, out var jumped))
        {
            _sounds.GrenadeBounce(f.Position, impact, surface, heard: false);
            f.Spin *= 0.5f;
        }

        if (jumped)
            f.Previous = f.Position;
        f.Angle += f.Spin * Flight.Step;
        f.LightAt = f.Position;
        if (!f.Resting)
            return;

        // Down. The light sits a little above it, under anything low it has rolled beneath.
        f.Heading = (float)_random.NextDouble() * float.Tau;
        var height = LightHeight;
        if (_physics is not null && _physics.Raycast(f.Position, f.Position + float3.UnitY * LightHeight, out var over, includeBodies: false))
            height = float.Max(0.05f, LightHeight * over.Fraction - 0.05f);
        f.LightAt = f.Position + float3.UnitY * height;
        f.ShadowsReady = false;
        Log.Write($"[FLARE] Landed at {f.Position.X:0.0},{f.Position.Y:0.0},{f.Position.Z:0.0} ({Level.ZoneAt(f.Position)})");
    }

    /// <summary>An illumination flare: climbing to the top, then hanging under its parachute, sinking and drifting.</summary>
    private void FlyHigh(Flare f)
    {
        f.Previous = f.Position;
        if (f.Resting)
            return;
        var step = f.Rising ? f.Velocity * Flight.Step : (f.Drift - float3.UnitY * SkyFall) * Flight.Step;
        var next = f.Position + step;
        if (_physics is not null && _physics.Raycast(f.Position, next, out var hit, includeBodies: false))
        {
            if (f.Rising)
            {
                // Something in the way after all (a branch, a ledge): it lights where it is and falls as a thrown one does.
                Log.Write($"[FLARE] Hit something on the way up at {hit.Point.X:0.0},{hit.Point.Y:0.0},{hit.Point.Z:0.0}");
                f.Sky = f.Rising = false;
                f.Life = BurnTime;
                f.Velocity *= -0.2f;
                f.Position = float3.Lerp(f.Position, hit.Point, 0.9f);
                f.LightAt = f.Position;
                return;
            }

            f.Resting = true;   // (sunk onto something: it burns out there)
            return;
        }

        f.Position = next;
        f.Angle += f.Spin * Flight.Step;
        if (!f.Rising)
        {
            f.LightAt = f.Position;
            return;
        }

        f.Velocity.Y -= 9.81f * Flight.Step;
        f.LightAt = f.Position;
        if (f.Velocity.Y <= 0f)
            Ignite(f);
    }

    /// <summary>At the top: the pop, the parachute, and it lights.</summary>
    private void Ignite(Flare f)
    {
        f.Rising = false;
        f.Velocity = float3.Zero;
        f.Age = 0f;
        f.ShadowsReady = false;
        f.Heading = (float)_random.NextDouble() * float.Tau;
        _sounds.FlareFired(f.Position);
        Log.Write($"[FLARE] Lit in the sky at {f.Position.X:0.0},{f.Position.Y:0.0},{f.Position.Z:0.0} ({f.Zone}): {SkyLux:0} lux under it for {SkyBurnTime:0} s");
    }

    /// <summary>The flame: its flicker, sparks, smoke and hiss.</summary>
    private void Burn(Flare f, float dt)
    {
        // Flicker: a wandering waver, and a sputter at the end.
        var wander = 0.86f + 0.14f * ((float)_random.NextDouble() * 2f - 1f);
        f.Flicker += (wander - f.Flicker) * float.Min(1f, dt * 14f);
        if (f.Resting)
            f.SinceRest += dt;

        var at = f.Position;
        var strength = Strength(f);
        Flame(at, 0.4f + 0.6f * strength);

        var dying = f.Age > f.Life - DyingTime;
        f.SparkLeft -= dt;
        while (f.SparkLeft <= 0f)
        {
            f.SparkLeft += (dying ? 0.09f : 0.035f) * (f.Resting ? 1f : 0.5f);
            _particles.Add(new Particles.Particle
            {
                Position = at, Velocity = Particles.RandomDirection() * Particles.Range(0.4f, 2.2f) + new float3(0, 1.4f, 0) - f.Velocity * 0.15f,
                Kind = ParticleKind.Spark, Color = new float3(9f, 3f, 1.2f), Size = 0.004f, Life = Particles.Range(0.2f, 0.6f), Alpha = 1f,
                Gravity = 0.8f, Drag = 1.2f, StreakTime = 0.025f, Bounce = true,
            });
        }

        f.SmokeLeft -= dt;
        if (f.SmokeLeft <= 0f)
        {
            f.SmokeLeft = 0.14f;
            _particles.Add(new Particles.Particle
            {
                Position = at + new float3(0, 0.05f, 0), Velocity = new float3(Particles.Range(-0.1f, 0.1f), Particles.Range(0.5f, 0.9f), Particles.Range(-0.1f, 0.1f)),
                Kind = ParticleKind.Dust, Color = new float3(0.85f, 0.42f, 0.36f), Size = Particles.Range(0.05f, 0.09f), Growth = Particles.Range(0.2f, 0.34f),
                Life = Particles.Range(2.5f, 4f), Alpha = 0.3f * strength, Gravity = -0.01f, Drag = 0.6f, Spin = Particles.Range(-0.5f, 0.5f),
            });
        }

        f.HissLeft -= dt;
        if (f.HissLeft <= 0f && !dying)
        {
            f.HissLeft = HissEvery;
            _sounds.FlareBurning(at, f.Tag);
        }
    }

    /// <summary>An illumination flare: a spark trail on the way up; then the white glare, its smoke, and burning drips.</summary>
    private void BurnHigh(Flare f, float dt)
    {
        var at = Swung(f, f.Position);
        if (f.Rising)
        {
            f.SparkLeft -= dt;
            while (f.SparkLeft <= 0f)
            {
                f.SparkLeft += 0.02f;
                _particles.Add(new Particles.Particle
                {
                    Position = at, Velocity = Particles.RandomDirection() * 0.6f - f.Velocity * 0.05f,
                    Kind = ParticleKind.Spark, Color = new float3(7f, 4f, 1.6f), Size = 0.006f, Life = Particles.Range(0.2f, 0.45f), Alpha = 1f,
                    Gravity = 0.5f, Drag = 1.5f, StreakTime = 0.03f,
                });
            }
            return;
        }

        // Magnesium burns steadier than a road flare: a slight waver.
        var wander = 0.94f + 0.06f * ((float)_random.NextDouble() * 2f - 1f);
        f.Flicker += (wander - f.Flicker) * float.Min(1f, dt * 10f);
        var strength = Strength(f);
        var rotation = (float)_random.NextDouble() * float.Tau;
        _particles.Add(new Particles.Particle
        {
            Position = at, Kind = ParticleKind.Flash, Color = SkyFlameColor * strength, Size = 0.6f, Life = 0.03f, Alpha = 1f, Rotation = rotation,
        });
        _particles.Add(new Particles.Particle
        {
            Position = at, Kind = ParticleKind.Flash, Color = SkyLightColor * (2.2f * strength), Size = 7f, Life = 0.03f, Alpha = 0.6f, Rotation = rotation,
        });

        // A thin trail of white smoke, lit from inside, hanging in the air behind it.
        f.SmokeLeft -= dt;
        if (f.SmokeLeft <= 0f)
        {
            f.SmokeLeft = 0.3f;
            _particles.Add(new Particles.Particle
            {
                Position = at + new float3(0, 0.3f, 0), Velocity = new float3(Particles.Range(-0.08f, 0.08f), Particles.Range(0.15f, 0.3f), Particles.Range(-0.08f, 0.08f)),
                Kind = ParticleKind.Dust, Color = new float3(0.9f, 0.86f, 0.78f), Size = Particles.Range(0.4f, 0.7f), Growth = Particles.Range(0.3f, 0.5f),
                Life = Particles.Range(8f, 12f), Alpha = 0.22f * strength, Gravity = -0.005f, Drag = 0.4f, Spin = Particles.Range(-0.3f, 0.3f),
            });
        }

        // Burning drips falling away under it.
        f.DripLeft -= dt;
        while (f.DripLeft <= 0f)
        {
            f.DripLeft += f.Age > f.Life - SkyDyingTime ? 0.25f : 0.09f;
            _particles.Add(new Particles.Particle
            {
                Position = at, Velocity = Particles.RandomDirection() * Particles.Range(0.3f, 1.5f) - new float3(0, 1f, 0),
                Kind = ParticleKind.Spark, Color = new float3(12f, 10f, 7f), Size = 0.012f, Life = Particles.Range(1f, 2.2f), Alpha = 1f,
                Gravity = 1f, Drag = 0.8f, StreakTime = 0.04f,
            });
        }
    }

    /// <summary>Where it's drawn: one hanging under a parachute swings a little under it.</summary>
    private static float3 Swung(Flare f, float3 at) => f.Sky && !f.Rising
        ? at + new float3(0.25f * float.Sin(f.Age * 1.3f + f.Heading), 0f, 0.2f * float.Cos(f.Age * 0.9f + f.Heading))
        : at;

    /// <summary>The white-hot flame and the red glow round it, for this frame.</summary>
    private void Flame(float3 at, float strength)
    {
        var rotation = (float)_random.NextDouble() * float.Tau;
        _particles.Add(new Particles.Particle
        {
            Position = at, Kind = ParticleKind.Flash, Color = FlameColor * strength, Size = 0.09f, Life = 0.03f, Alpha = 1f, Rotation = rotation,
        });
        _particles.Add(new Particles.Particle
        {
            Position = at, Kind = ParticleKind.Flash, Color = new float3(1.5f, 0.3f, 0.14f) * strength, Size = 0.5f, Life = 0.03f, Alpha = 0.7f, Rotation = rotation,
        });
    }

    /// <summary>How bright it is now, 0..1: catching as it's lit, flickering, then dying.</summary>
    private static float Strength(Flare f)
    {
        if (f.Rising)
            return 0f;
        var catching = float.Clamp(f.Age / (f.Sky ? SkyCatchTime : 0.25f), 0f, 1f);
        var dyingTime = f.Sky ? SkyDyingTime : DyingTime;
        var left = f.Life - f.Age;
        var dying = left < dyingTime ? left / dyingTime * (0.75f + 0.25f * float.Sin(f.Age * 31f)) : 1f;
        return catching * dying * f.Flicker;
    }

    // ------------------------------------------------------------------------------------------------ drawing

    /// <summary>
    /// The level round any that have just settled (or lit in the sky, or sunk far enough since), into their tiles of the
    /// shadow atlas. Call outside any view.
    /// </summary>
    public void RenderShadows(Renderer renderer, Level level, FlareShadowAtlas atlas)
    {
        foreach (var f in _live)
        {
            if (f.Sky)
            {
                if (f.Rising || (f.ShadowsReady && float3.Distance(f.Position, f.ShadowAt) < SkyShadowMove))
                    continue;
                f.ShadowsReady = true;
                f.ShadowAt = f.Position;
                for (var face = 0; face < FlareShadowAtlas.Faces; face++)
                {
                    renderer.BeginFlareShadowTile(atlas, f.Slot, face, FlareShadowAtlas.ViewProjection(f.ShadowAt, face, sky: true));
                    if (face != 2)
                        level.Draw(renderer, f.Zone, shadowPass: true);   // (nothing above it: the +Y face is only cleared)
                    renderer.EndShadowPass();
                }
                continue;
            }

            if (!f.Resting || f.ShadowsReady)
                continue;
            f.ShadowsReady = true;
            var zones = Level.ZoneAt(f.LightAt);
            for (var face = 0; face < FlareShadowAtlas.Faces; face++)
            {
                renderer.BeginFlareShadowTile(atlas, f.Slot, face, FlareShadowAtlas.ViewProjection(f.LightAt, face));
                level.Draw(renderer, zones, shadowPass: true);
                renderer.EndShadowPass();
            }
        }
    }

    /// <summary>Their light, for this frame's views.</summary>
    public void Apply(Renderer renderer)
    {
        var count = 0;
        foreach (var f in _live)
        {
            if (f.Sky)
            {
                // From where its shadows were drawn (within SkyShadowMove of it), over its whole circle of ground.
                if (f.Rising || !f.ShadowsReady)
                    continue;
                _lightSpots[count] = new float4(f.ShadowAt, -SkyRadius);
                _lightColors[count] = new float4(SkyLightColor * (SkyBrightness * Strength(f)), f.Slot);
                count++;
                continue;
            }

            // Spreading from the short, shadowless reach of its flight once it's down and its shadows are drawn.
            var settle = f.ShadowsReady ? float.Clamp(f.SinceRest / 0.5f, 0f, 1f) : 0f;
            var range = FlyingRange + (Range - FlyingRange) * settle;
            var bright = Brightness * Strength(f) * (0.45f + 0.55f * settle);
            _lightSpots[count] = new float4(f.LightAt, range);
            _lightColors[count] = new float4(LightColor * bright, f.ShadowsReady ? f.Slot : -1f);
            count++;
        }

        renderer.SetFlares(_lightSpots.AsSpan(0, count), _lightColors.AsSpan(0, count));
    }

    /// <summary>The sticks themselves (and the parachutes of those in the sky).</summary>
    public void Draw(Renderer renderer, Zones zones)
    {
        var t = _accumulator / Flight.Step;
        foreach (var f in _live)
        {
            var at = Swung(f, float3.Lerp(f.Previous, f.Position, t));
            if ((zones & (f.Sky ? f.Zone : Level.ZoneAt(at))) == 0)
                continue;
            if (f.Sky && !f.Rising)
            {
                // Hanging burning end down, the parachute 1.6 m over it, leaning with the swing.
                var hang = float4x4.CreateRotationX(float.Pi * 0.5f) * float4x4.CreateRotationY(f.Heading);
                var stick = hang * float4x4.CreateTranslation(at);
                renderer.DrawMesh(_body, stick, new float3(0.35f, 0.35f, 0.32f), ShadeMode.Lit, Material.Plain, new float2(0.55f, 0f));
                renderer.DrawMesh(_tip, stick, SkyFlameColor * 0.5f, ShadeMode.Unlit);
                var canopy = float4x4.CreateRotationX(-float.Pi * 0.5f) * float4x4.CreateTranslation(f.Position + new float3(0f, 1.6f, 0f) + (at - f.Position) * -0.3f);
                renderer.DrawMesh(_canopy, canopy, CanopyTint, ShadeMode.Lit, Material.Plain, new float2(0.8f, 0f));
                continue;
            }

            var turn = f.Resting
                ? float4x4.CreateRotationY(f.Heading)
                : float4x4.CreateFromAxisAngle(f.SpinAxis, f.Angle) * float4x4.CreateRotationX(0.4f);
            var model = turn * float4x4.CreateTranslation(at + (f.Resting ? new float3(0, 0.014f - Flight.Radius, 0) : float3.Zero));
            renderer.DrawMesh(_body, model, BodyTint, ShadeMode.Lit, Material.Plain, new float2(0.55f, 0f));
            renderer.DrawMesh(_tip, model, FlameColor * 0.5f, ShadeMode.Unlit);
        }
    }
}
