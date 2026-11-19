using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The Chinook bringing the container into the prison yard, for the intro (<see cref="PrisonIntro"/>). It comes in out of
/// the haze from the north-west, over the corner of the wings, slowing and turning until it's lined up over the court;
/// it hovers, comes straight down until the container touches, and lets go of the load (the sling drops onto the roof,
/// and stays there). Lightened, it bobs up, then climbs away east, nose down.
///
/// All of it is a function of the time since it started (<see cref="HeliAt"/>): where it is, and from how that changes
/// how it leans (nose up to slow down, nose down to speed up, into its turns). The container hangs rigidly from it, so it
/// swings forward as the helicopter slows, as a load would; it's level by the time it lands, and from there it's the
/// container in the yard (see LevelPrisonContainer.cs).
/// </summary>
public sealed class PrisonDelivery
{
    // The timetable, in seconds.
    private const float ApproachSeconds = 14f, HoverSeconds = 1.4f, DescentSeconds = 7f, ReleaseAfter = 1.5f, BobSeconds = 1.6f, DepartSeconds = 15f;
    private const float LandsAt = ApproachSeconds + HoverSeconds + DescentSeconds;
    private const float ReleaseAt = LandsAt + ReleaseAfter;
    private const float DepartsAt = ReleaseAt + 0.4f;
    private const float GoneAt = DepartsAt + BobSeconds + DepartSeconds;

    // The way in, in the prison's coordinates: a curve from far out, to the hover. (The curve's third point keeps the load
    // clear of the roofs: at the corner of the wings, where the ridges are about 13 m up, its foot is about 19 m up.)
    private static readonly float3 Start = new(-130f, 62f, -122f), Control1 = new(-80f, 46f, -75f), Control2 = new(-16f, 32f, -15f);

    /// <summary>How high the hover is above where it lands.</summary>
    private const float HoverHeight = 8.1f;
    private static readonly float3 Landed = new(0, -Chinook.ContainerOrigin.Y, 0);
    private static readonly float3 Hover = Landed + new float3(0, HoverHeight, 0);
    private const float BobHeight = 0.5f;

    /// <summary>Lined up with the container: nose along +X (east).</summary>
    private static readonly float FinalYaw = -float.Pi / 2;

    /// <summary>The container in the helicopter's frame, from its own (along +X) to hanging lengthways under it.</summary>
    private static readonly float4x4 Hanging =
        float4x4.CreateRotationY(float.Pi / 2) * float4x4.CreateTranslation(Chinook.ContainerOrigin);

    private readonly Level _level;
    private readonly Portal? _portal;
    private readonly Particles _particles;
    private readonly Random _random = new(47);
    private Chinook? _chinook;
    private float _time;
    private float _emit;
    private bool _flying, _landed, _released;
    private bool _slingOnRoof;
    private float4x4 _slingWorld;

    /// <summary>The container has touched down.</summary>
    public event Action? ContainerLanded;

    /// <summary>The container is in the yard (false from <see cref="Prepare"/> until it lands), which the lighting needs to know.</summary>
    public event Action<bool>? ContainerInPlace;

    /// <summary>Its shadow moves: the prison's shadow map has to be drawn again before this frame.</summary>
    public bool ShadowsChanging { get; private set; }

    /// <summary>The prison's shadow map is out of date once more (clears when read).</summary>
    public bool ConsumeShadowsChanged()
    {
        var changed = _shadowsChanged;
        _shadowsChanged = false;
        return changed;
    }

    private bool _shadowsChanged;

    public PrisonDelivery(Level level, Portal? portal, Particles particles)
    {
        _level = level;
        _portal = portal;
        _particles = particles;
    }

    /// <summary>Clears the yard: no container, no portal, until the helicopter brings them.</summary>
    public void Prepare()
    {
        _flying = _landed = _released = _slingOnRoof = false;
        _time = 0;
        _level.PrisonContainerShown = false;
        _level.PrisonContainerPose = float4x4.Identity;
        if (_portal is not null)
            _portal.Hidden = true;
        _shadowsChanged = true;
        ContainerInPlace?.Invoke(false);
    }

    /// <summary>The helicopter sets off.</summary>
    public void Fly()
    {
        _chinook ??= new Chinook();
        _chinook.SlingAttached = true;
        _flying = true;
        _time = 0;
        _level.PrisonContainerShown = true;
        ShadowsChanging = true;
        Place();
    }

    /// <summary>As if it had all happened: the container in the yard, the sling on it, the helicopter gone.</summary>
    public void Finish()
    {
        _chinook ??= new Chinook();
        if (!_landed)
        {
            _time = LandsAt;
            Place();
            Land(burst: false);
        }
        if (!_released)
        {
            _time = ReleaseAt;
            Place();
            Release();
        }
        _slingWorld = FlatOnTheRoof(1e3f);
        _slingOnRoof = true;
        _flying = false;
        ShadowsChanging = false;
        _shadowsChanged = true;
    }

    /// <summary>Where the intro should look: the helicopter and its load, or what it's left in the yard.</summary>
    public float3 LookPoint
    {
        get
        {
            if (_chinook is null)
                return Level.PrisonAt(Start.X, Start.Y, Start.Z);
            var load = Level.PrisonContainerOrigin + new float3(0, Level.PrisonContainerHeight, 0);
            if (!_landed)
                load = float3.Transform(load, _level.PrisonContainerPose);
            return float3.Lerp(_chinook.Position, load, 0.36f);
        }
    }

    public void Update(float dt)
    {
        if (!_flying)
            return;
        _time += dt;
        if (_time >= GoneAt)
        {
            _flying = false;
            ShadowsChanging = false;
            _shadowsChanged = true;
            Log.Write("[DELIVERY] The helicopter has gone");
            return;
        }

        Place();
        if (!_landed && _time >= LandsAt)
            Land(burst: true);
        if (!_released && _time >= ReleaseAt)
            Release();
        if (_released)
        {
            var fall = _time - ReleaseAt;
            _slingWorld = FlatOnTheRoof(fall);
            _slingOnRoof = true;
        }
        _chinook!.Update(dt);
        Downwash(dt);
    }

    public void Draw(Renderer renderer, Zones zones)
    {
        if ((zones & Zones.Prison) == 0)
            return;
        if (_flying)
            _chinook!.Draw(renderer, zones);
        if (_slingOnRoof)
            _chinook!.DrawSling(renderer, _slingWorld);
    }

    public void DrawBlur(Renderer renderer, Zones zones)
    {
        if (_flying)
            _chinook!.DrawBlur(renderer, zones);
    }

    private void Place()
    {
        var chinook = _chinook!;
        (chinook.Position, chinook.Yaw, chinook.Pitch, chinook.Roll) = PoseAt(_time);
        if (!_landed && _time < LandsAt)
        {
            // Lined up on the yard's own container: when it lands, this is exactly the identity.
            _level.PrisonContainerPose = float4x4.CreateTranslation(-Level.PrisonContainerOrigin) * Hanging * chinook.World;
        }
    }

    private void Land(bool burst)
    {
        _landed = true;
        _level.PrisonContainerPose = float4x4.Identity;
        _level.PrisonContainerShown = true;
        if (_portal is not null)
            _portal.Hidden = false;
        ContainerInPlace?.Invoke(true);
        ContainerLanded?.Invoke();
        Log.Write("[DELIVERY] The container is down");
        if (burst)
            Dust(70, 3f, 9f);
    }

    private void Release()
    {
        _released = true;
        _chinook!.SlingAttached = false;
        Log.Write("[DELIVERY] Sling released");
    }

    /// <summary>The sling as it falls from the hooks and settles flat on the container's roof, by the time since it let go.</summary>
    private float4x4 FlatOnTheRoof(float fall)
    {
        var length = -Chinook.ContainerOrigin.Y - Level.PrisonContainerHeight;
        var height = float.Max(0.006f, 1f - 0.5f * 9.8f * fall * fall / length);
        var roof = Level.PrisonContainerOrigin.Y + Level.PrisonContainerHeight + 0.02f;
        var hung = float4x4.CreateRotationY(FinalYaw) * float4x4.CreateTranslation(Level.PrisonAt(Landed.X, Landed.Y, Landed.Z));
        return hung * float4x4.CreateTranslation(0, -roof, 0) * float4x4.CreateScale(1f, height, 1f) * float4x4.CreateTranslation(0, roof, 0);
    }

    // ------------------------------------------------------------------------------------------------
    // The flight.
    // ------------------------------------------------------------------------------------------------

    private static float Smooth(float t)
    {
        t = float.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Its path alone: where it is at time t, in the prison's coordinates.</summary>
    private static float3 HeliAt(float t)
    {
        if (t < ApproachSeconds)
        {
            // Along the curve, slowing all the way to the hover.
            var s = t / ApproachSeconds;
            var u = 1f - (1f - s) * (1f - s);
            var v = 1f - u;
            return Start * (v * v * v) + Control1 * (3f * v * v * u) + Control2 * (3f * v * u * u) + Hover * (u * u * u);
        }
        if (t < ApproachSeconds + HoverSeconds)
            return Hover;
        if (t < LandsAt)
            return float3.Lerp(Hover, Landed, Smooth((t - ApproachSeconds - HoverSeconds) / DescentSeconds));
        if (t < DepartsAt)
            return Landed;
        // Lightened, it rises a little; then climbs, gathering speed along its nose.
        var away = t - DepartsAt;
        var bob = BobHeight * Smooth(away / BobSeconds);
        var fly = float.Max(0f, away - BobSeconds * 0.5f);
        return Landed + new float3(0.5f * 3.6f * fly * fly, bob + 5.5f * fly * Smooth(fly / 2f), 0);
    }

    /// <summary>How much it sways in the air: none as the load touches down, for the load to land square.</summary>
    private static float SwayAt(float t)
    {
        if (t < ApproachSeconds + HoverSeconds + DescentSeconds * 0.45f)
            return 1f;
        if (t < LandsAt)
            return 1f - Smooth((t - ApproachSeconds - HoverSeconds - DescentSeconds * 0.45f) / (DescentSeconds * 0.5f));
        if (t < DepartsAt)
            return 0f;
        return Smooth((t - DepartsAt) / BobSeconds);
    }

    private static (float3 Position, float Yaw, float Pitch, float Roll) PoseAt(float t)
    {
        const float e = 0.25f;
        var at = HeliAt(t);
        var velocity = (HeliAt(t + e) - HeliAt(t - e)) / (2f * e);
        var acceleration = (HeliAt(t + e) - at * 2f + HeliAt(t - e)) / (e * e);

        // It flies nose first, and turns to line up with the yard as it slows.
        var travel = float.Atan2(-(Control2.X - Start.X), -(Control2.Z - Start.Z));
        var yaw = t < LandsAt
            ? travel + (FinalYaw - travel) * Smooth((t - ApproachSeconds * 0.45f) / (ApproachSeconds * 0.5f))
            : FinalYaw;

        var forward = new float3(-float.Sin(yaw), 0, -float.Cos(yaw));
        var right = new float3(float.Cos(yaw), 0, -float.Sin(yaw));
        var sway = SwayAt(t);

        // Nose up to slow down, nose down to speed up, and a little nose down for the speed itself; into its turns.
        var alongSpeed = float3.Dot(velocity, forward);
        var pitch = -float.Atan(float3.Dot(acceleration, forward) / 9.8f) * 1.4f - 0.0035f * alongSpeed;
        var roll = float.Atan(float3.Dot(acceleration, right) / 9.8f) * -1.4f;
        pitch = float.Clamp(pitch, -0.2f, 0.2f) * sway + 0.012f * sway * float.Sin(t * 0.83f);
        roll = float.Clamp(roll, -0.2f, 0.2f) * sway + 0.016f * sway * float.Sin(t * 0.61f + 1f);

        // It never holds quite still.
        var drift = new float3(0.22f * float.Sin(t * 0.71f), 0.10f * float.Sin(t * 1.13f + 2f), 0.18f * float.Sin(t * 0.53f + 4f)) * sway;
        return (Level.PrisonAt(at.X, at.Y, at.Z) + drift, yaw, pitch, roll);
    }

    // ------------------------------------------------------------------------------------------------
    // Its downwash on the ground.
    // ------------------------------------------------------------------------------------------------

    private void Downwash(float dt)
    {
        var height = _chinook!.Position.Y - Level.PrisonOrigin.Y;
        if (height > 22f)
            return;
        _emit += dt * 30f * float.Clamp((22f - height) / 8f, 0f, 1f);
        var n = (int)_emit;
        _emit -= n;
        Dust(n, 2.5f, 6f);
    }

    /// <summary>Puffs raised from the ground round the container's foot, blown outward.</summary>
    private void Dust(int count, float minSpeed, float maxSpeed)
    {
        var half = Level.PrisonContainerHalfSize;
        var origin = Level.PrisonContainerOrigin;
        for (var i = 0; i < count; i++)
        {
            // Somewhere round the box: along one of its sides, a little way out.
            var along = (float)_random.NextDouble() * 2f - 1f;
            float2 p, away;
            if (_random.Next(5) < 3)
            {
                var side = _random.Next(2) * 2f - 1f;
                p = new float2(along * half.X, side * (half.Y + 0.3f));
                away = new float2(along * 0.35f, side);
            }
            else
            {
                var end = _random.Next(2) * 2f - 1f;
                p = new float2(end * (half.X + 0.3f), along * half.Y);
                away = new float2(end, along * 0.35f);
            }
            away /= float.Sqrt(away.X * away.X + away.Y * away.Y);
            var speed = minSpeed + (float)_random.NextDouble() * (maxSpeed - minSpeed);
            _particles.Add(new Particles.Particle
            {
                Position = origin + new float3(p.X, 0.15f + (float)_random.NextDouble() * 0.3f, p.Y),
                Velocity = new float3(away.X * speed, 0.3f + (float)_random.NextDouble() * 0.7f, away.Y * speed),
                Color = new float3(0.68f, 0.65f, 0.57f),
                Kind = ParticleKind.Dust,
                Size = 0.45f + (float)_random.NextDouble() * 0.35f,
                Growth = 1.1f,
                Life = 1.4f + (float)_random.NextDouble() * 1.1f,
                Alpha = 0.34f,
                Gravity = 0f,
                Drag = 1.3f,
                Rotation = (float)_random.NextDouble() * float.Tau,
                Spin = 0.5f,
            });
        }
    }
}
