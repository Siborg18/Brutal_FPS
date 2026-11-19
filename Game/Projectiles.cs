using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Bullets in flight: with gravity, and for a round with a <see cref="Cartridge"/> the air's drag, its mass and its punch
/// (without one: 9 mm ball, no drag). Each frame a bullet's path is swept through the physics world (exactly what's
/// drawn), the portals' water and the targets; whatever it meets first takes the hit. These are far too fast for a
/// rigid body, so they only become Bullet bodies once they've slowed: a ricochet that has run out of speed tumbles on
/// as one, and one that stayed in one piece on something hard drops as a spent bullet. Shallow hits on hard surfaces can
/// glance off once. A faint streak shows each bullet's path for the frame. Rounds fired on the firing range are tracers: a
/// bright head, and a line along the path they really flew that hangs in the air and fades.
/// Without the physics world, shots fall back to the level's collision boxes.
/// </summary>
public sealed class ProjectileSystem(
    Level level,
    IReadOnlyList<Target> targets,
    PortalSystem portals,
    PhysicsWorld? physics,
    SurfaceTable surfaces,
    ImpactEffects effects,
    Particles particles,
    Npcs npcs)
{
    private struct Bullet
    {
        public float3 Position;
        public float3 Velocity;
        public float Age;
        public int Ricochets;
        public float3 Origin;   // where it was fired from
        public bool FromPlayer; // the player's (else an NPC's: it can hit the player, and doesn't make the player wanted)
        public Npc? FiredBy;    // the NPC who fired it (their own side know it for a friend's)
        public bool Cracked;    // has already cracked past the player's head
        public Cartridge? Round; // what it is (null: plain 9 mm ball)
        public bool Tracer;     // leaves a line behind it (fired on the firing range)
        public float WallLeft;  // metres of brick it can still go through (see Cartridge.WallPenetration)
        public int Walls;       // walls it has gone through
    }

    private static readonly SurfaceInfo ArmourPlate = SurfaceInfo.ForMaterial(Material.Steel, new float3(0.1f, 0.1f, 0.1f));   // what a bullet meets in body armour

    public float Radius = 0.0045f;   // a 9 mm bullet (targets are tested with this)
    public float Gravity = 9.81f;
    public float Lifetime = 3f;
    public float Mass = 0.0075f;     // kg: 115 grain
    public float MaxShove = 7f;      // the most a hit may change a loose object's speed by (m/s)
    public float TumbleDrag = 4f;    // a bullet knocked off its line tumbles, and the air slows it this many times faster
    public float LooseSpeed = 90f;   // a ricochet slower than this (m/s) carries on as a Bullet body

    private static readonly float3 TracerColor = new(1.6f, 1.05f, 0.55f);
    private static readonly float3 RangeTracerColor = new(6f, 1.3f, 0.35f);
    private const float TrailLife = 2f, TrailAlpha = 0.75f;   // seconds a tracer's line hangs in the air; how strong it starts
    private const int MostTrailPieces = 4000;
    private static readonly bool DebugLog = Environment.GetEnvironmentVariable("BRUTALFPS_DEBUG") == "1";

    private readonly List<Bullet> _bullets = [];
    private readonly List<(float3 Head, float3 Tail, float HalfWidth, float Born)> _trail = [];   // oldest first
    private float _clock;
    private readonly Random _random = new(31);

    /// <summary>Raised whenever a projectile hits a target.</summary>
    public event Action<TargetHit>? TargetHit;

    /// <summary>An NPC's bullet hit the player: (damage, where it came from, where it hit).</summary>
    public event Action<float, float3, float3>? PlayerHit;

    /// <summary>The player, for NPCs' bullets to hit (null: they can't be hit).</summary>
    public PlayerController? Player { get; set; }

    /// <summary>What bullets sound like when they strike or fly past, and what people hear of it (null: silent).</summary>
    public GameSounds? Sounds { get; set; }

    /// <summary>What bullets become when they slow down, and their models (null: they just stop, and aren't seen).</summary>
    public DebrisSystem? Debris { get; set; }

    /// <summary>Health a pistol round takes from the player (before the random spread of +-25%).</summary>
    public float PlayerDamage = 20f;

    public int ActiveCount => _bullets.Count;
    public int Count => _bullets.Count;

    /// <summary>How far a shot fired level at <paramref name="speed"/> has dropped after <paramref name="distance"/> metres.</summary>
    public float Drop(float distance, float speed, Cartridge? round)
    {
        var flight = distance / speed;
        return round?.Drop(distance, speed, Gravity) ?? 0.5f * Gravity * flight * flight;
    }

    public void Spawn(float3 position, float3 velocity, bool fromPlayer = true, Cartridge? round = null, Npc? firedBy = null, bool tracer = false) =>
        _bullets.Add(new Bullet { Position = position, Velocity = velocity, Origin = position, FromPlayer = fromPlayer, FiredBy = firedBy, Round = round, Tracer = tracer || Level.OnFiringRange(position),
            WallLeft = round?.WallPenetration ?? 0f });

    public void Update(float dt)
    {
        _clock += dt;
        for (var i = _bullets.Count - 1; i >= 0; i--)
        {
            var b = _bullets[i];
            b.Age += dt;
            var start = b.Position;
            var before = b.Velocity;
            b.Velocity.Y -= Gravity * dt;
            if (b.Round is { } flying)
            {
                // The air, over the frame: dv/dt = -k v^2, whose exact step this is.
                var k = flying.DragPerMetre * (b.Ricochets > 0 ? TumbleDrag : 1f);
                b.Velocity /= 1f + k * Length(b.Velocity) * dt;
            }

            // On the frame's average velocity, so the drop doesn't change with the frame rate (the sights are zeroed on it).
            var end = start + (before + b.Velocity) * 0.5f * dt;
            var alive = Advance(ref b, start, end);
            NearMiss(ref b, start);
            npcs.BulletPassed(start, b.Position, b.Origin, b.FromPlayer, b.FiredBy);   // (people near its path feel it: suppression)
            // Motion blur of the bullet over this frame (to where it stopped), as wide as the bullet is.
            var shown = b.Position - start;
            if (float3.Dot(shown, shown) > 1e-4f)
            {
                if (b.Tracer)
                    LeaveTrail(b, start);
                else
                    particles.Streak(b.Position, start + shown * 0.25f, TracerColor, 0.0035f * (b.Round?.Radius ?? Radius) / Radius, 0.4f);
            }

            if (alive && b.Ricochets > 0 && b.Round is { } spent && Debris is { } debris && Length(b.Velocity) < LooseSpeed)
            {
                debris.LooseBullet(spent, Pose(b), b.Velocity);
                if (DebugLog)
                    Log.Write($"[BULLET] {spent.Name} ricochet ran out of speed at {b.Position.X:0.00},{b.Position.Y:0.00},{b.Position.Z:0.00}: a Bullet body now");
                alive = false;
            }

            if (!alive || b.Age > Lifetime)
                _bullets.RemoveAt(i);
            else
                _bullets[i] = b;
        }
        DrawTrail();
    }

    /// <summary>A tracer's head for this frame, and this frame's piece of its line. It's wider further from the shooter (the one
    /// watching), or it would soon be too fine to see.</summary>
    private void LeaveTrail(in Bullet b, float3 start)
    {
        var halfWidth = 0.004f + 0.0005f * Length(b.Position - b.Origin);
        particles.Streak(b.Position, start, RangeTracerColor, halfWidth * 1.6f, 1f);
        if (_trail.Count == MostTrailPieces)
            _trail.RemoveAt(0);
        _trail.Add((b.Position, start, halfWidth, _clock));
    }

    private void DrawTrail()
    {
        var gone = 0;
        while (gone < _trail.Count && _clock - _trail[gone].Born > TrailLife)
            gone++;
        _trail.RemoveRange(0, gone);
        foreach (var (head, tail, halfWidth, born) in _trail)
        {
            var left = 1f - (_clock - born) / TrailLife;
            particles.Streak(head, tail, RangeTracerColor, halfWidth, TrailAlpha * left * left, ParticleKind.Trail);
        }
    }

    /// <summary>The bullets in flight that have a model (it's a glint at these speeds, but a slow ricochet can be seen).</summary>
    public void Draw(Renderer renderer, Zones zones)
    {
        if (Debris is null)
            return;
        foreach (var b in _bullets)
        {
            if (b.Round is null || (zones & Level.ZoneAt(b.Position)) == 0)
                continue;
            var pose = Pose(b);
            foreach (var part in Debris.BulletParts(b.Round))
                renderer.DrawPart(part, pose, float3.One);
        }
    }

    /// <summary>A bullet's model space to the world: its nose (+Z) along its flight.</summary>
    private static float4x4 Pose(in Bullet b)
    {
        var length = Length(b.Velocity);
        var forward = length > 1e-3f ? b.Velocity / length : float3.UnitZ;
        var side = float3.Normalize(float3.Cross(float.Abs(forward.Y) < 0.99f ? float3.UnitY : float3.UnitX, forward));
        var up = float3.Cross(forward, side);
        return new float4x4(side.X, side.Y, side.Z, 0, up.X, up.Y, up.Z, 0, forward.X, forward.Y, forward.Z, 0, b.Position.X, b.Position.Y, b.Position.Z, 1);
    }

    private static float Length(float3 v) => float.Sqrt(float3.Dot(v, v));

    /// <summary>BRUTALFPS_DEBUG: where a calibre's round got to, how fast, and with how much left in it.</summary>
    private static void LogStrike(in Bullet b, string what, float3 point, float speed)
    {
        if (DebugLog && b.Round is { } round)
            Log.Write($"[BULLET] {round.Name} hit {what} at {point.X:0.00},{point.Y:0.00},{point.Z:0.00}, {Length(point - b.Origin):0.0} m out: " +
                      $"{speed:0} m/s, {0.5f * round.BulletMass * speed * speed:0} J, punch {round.Punch(speed):0.00}");
    }

    /// <summary>Moves a bullet along start-end, resolving what it hits. Returns false once it has stopped.</summary>
    private bool Advance(ref Bullet b, float3 start, float3 end)
    {
        var delta = end - start;
        var length = float.Sqrt(float3.Dot(delta, delta));
        if (length < 1e-6f)
        {
            b.Position = end;
            return true;
        }

        var direction = delta / length;
        var distance = length;
        var speed = Length(b.Velocity);
        var punch = b.Round?.Punch(speed) ?? 1f;
        var holeScale = float.Pow(punch, 0.3f);   // a round twice as hard doesn't leave a hole twice as wide
        var hitWorld = false;
        var hitPoint = end;
        var hitNormal = float3.Zero;
        SurfaceInfo surface = default;
        BulletSharp.RigidBody? body = null;

        if (physics is not null)
        {
            if (physics.Raycast(start, end, out var hit))
            {
                hitWorld = true;
                distance = hit.Fraction * length;
                hitPoint = hit.Point;
                hitNormal = hit.Normal;
                surface = surfaces[hit.UserIndex];
                body = hit.Body;
            }
        }
        else if (level.RaycastSurface(start, direction, length, out var d, out var n, out var s))
        {
            hitWorld = true;
            distance = d;
            hitPoint = start + direction * d;
            hitNormal = n;
            surface = s;
        }

        // How far the world lets the bullet go (an exit wound can't come out beyond a wall).
        var worldDistance = distance;

        // People, tested against their skin as posed this frame (unless something else is in front of them).
        // (Not the one who fired it, on its way out of the muzzle: their own skin is the most expensive miss there is. A ricochet can come back at them.)
        var hitNpc = npcs.Raycast(start, direction, distance, out var npcHit, fromNpc: !b.FromPlayer, ignore: b.Ricochets == 0 ? b.FiredBy : null);
        if (hitNpc)
            distance = npcHit.Skin.Distance;

        // Shooting targets: the nearest one in front of everything else (not just the first in the list).
        Target? hitTarget = null;
        var targetLocal = default(float3);
        foreach (var t in targets)
        {
            if (t.Raycast(start, direction, distance, b.Round?.Radius ?? Radius, out var d, out var local) && d <= distance)
            {
                hitTarget = t;
                targetLocal = local;
                distance = d;
            }
        }

        // The player (NPCs' bullets only), as a box round their body (or, prone, a few low ones along it).
        var hitPlayer = false;
        if (!b.FromPlayer && Player is { Alive: true } player)
        {
            Span<Aabb> boxes = stackalloc Aabb[3];
            var count = player.HitBoxes(boxes);
            for (var i = 0; i < count; i++)
            {
                if (boxes[i].Raycast(start, direction, distance, out var playerDistance, out _) && playerDistance <= distance)
                {
                    hitPlayer = true;
                    hitTarget = null;
                    distance = playerDistance;
                }
            }
        }

        // Portal doorways are holes in the wall; the water in them stops the bullet (unless someone stands in front).
        if (portals.Raycast(start, direction, distance, out var portalDistance, out var portal)
            && ((hitTarget is null && !hitPlayer) || portalDistance < distance))
        {
            var point = start + direction * portalDistance;
            portals.Splash(portal!, point);
            effects.PortalSquish(portal!, point, direction);
            b.Position = point;
            return false;
        }

        if (hitPlayer)
        {
            b.Position = start + direction * distance;
            var damage = PlayerDamage * punch * (0.75f + 0.5f * (float)_random.NextDouble());
            LogStrike(b, "the player", b.Position, speed);
            PlayerHit?.Invoke(damage, b.Origin, b.Position);
            return false;
        }

        if (hitTarget is not null)
        {
            b.Position = start + direction * distance;
            TargetHit?.Invoke(hitTarget.RegisterHit(targetLocal, direction) with
            {
                Round = b.Round,
                Range = Length(b.Position - b.Origin),
                Speed = speed,
                Energy = 0.5f * (b.Round?.BulletMass ?? Mass) * speed * speed,
                Flight = b.Age - (length - distance) / speed,   // (it didn't fly the whole of this frame)
            });
            var board = new SurfaceInfo(SurfaceKind.Wood, new float3(0.7f), Material.Wood);
            // On the board's face (the side the shot came from), not square to the shot: at an angle it would sink into it.
            var face = float3.Dot(hitTarget.Normal, direction) < 0f ? hitTarget.Normal : -hitTarget.Normal;
            effects.Hit(b.Position, face, direction, board, holeScale, inPhysics: false);
            Sounds?.Impact(b.Position, board, b.FromPlayer, b.FiredBy);
            LogStrike(b, hitTarget.Name, b.Position, speed);
            return false;
        }

        if (hitNpc)
        {
            // Into someone: a wound and blood. A 9 mm round often goes right through a limb, and through the body more
            // often than not: then it comes out the far side (with the blood it carries) and flies on, slower. A rifle
            // round goes through further and keeps more of its speed; how hard it hit is the damage.
            var penetration = b.Round?.Penetration ?? 1f;
            var entry = npcHit.Skin.Distance;
            var point = start + direction * entry;
            var through = npcHit.Skin.ExitDistance - entry;
            var (maxThrough, chance) = npcHit.Part switch
            {
                BodyPart.Torso => (0.34f, 0.7),
                BodyPart.Head => (0.24f, 0.5),
                _ => (0.2f, 0.9),
            };
            var covered = npcHit.Npc.Profile.ArmourCovers(npcHit.Part, npcHit.Skin.BindPosition);
            var pierces = covered && CloseIn(b, point);   // a .50 close in goes straight through the plate
            var armour = covered && !pierces;   // the plate stops it: sparks, not blood
            var exits = !armour && float.IsFinite(through) && through < maxThrough * penetration && (!hitWorld || npcHit.Skin.ExitDistance <= worldDistance)
                        && _random.NextDouble() < chance * penetration;
            npcs.Hit(npcHit, start, direction, b.Origin, exits ? npcHit.Skin.ExitDistance : float.PositiveInfinity, b.FromPlayer,
                punch, (b.Round?.Radius ?? Radius) / Radius, b.FiredBy, pierces);
            if (pierces)
            {
                effects.HitBody(point, npcHit.Skin.Normal, direction, ArmourPlate);
                effects.BloodEntry(point, npcHit.Skin.Normal, direction);
                Sounds?.Impact(point, ArmourPlate, b.FromPlayer, b.FiredBy);
                Sounds?.Flesh(point);
            }
            else if (armour)
            {
                effects.HitBody(point, npcHit.Skin.Normal, direction, ArmourPlate);
                Sounds?.Impact(point, ArmourPlate, b.FromPlayer, b.FiredBy);
            }
            else
            {
                effects.BloodEntry(point, npcHit.Skin.Normal, direction);
                Sounds?.Flesh(point);
            }

            LogStrike(b, npcHit.Npc.Name, point, speed);
            if (exits)
            {
                var exitPoint = start + direction * npcHit.Skin.ExitDistance;
                effects.BloodExit(exitPoint, direction);
                b.Position = exitPoint + direction * 0.02f;
                b.Velocity *= float.Min(0.8f, 0.45f * penetration);
                return true;
            }

            b.Position = point;
            return false;
        }

        if (!hitWorld)
        {
            b.Position = end;
            return true;
        }

        b.Position = hitPoint;
        if (body is not null)
        {
            // Something loose: knock it about (the bullet's momentum, but no more than a hard shove) and stop.
            var mass = body.InvMass > 0 ? 1f / (float)body.InvMass : 1f;
            var impulse = direction * float.Min((b.Round?.BulletMass ?? Mass) * speed, mass * MaxShove * punch);
            PhysicsWorld.Push(body, impulse, hitPoint);
            effects.HitBody(hitPoint, hitNormal, direction, surface);
            Sounds?.Impact(hitPoint, surface, b.FromPlayer, b.FiredBy);
            LogStrike(b, "something loose", hitPoint, speed);
            DropSpent(b, hitPoint, hitNormal, direction, surface);
            return false;
        }

        // A heavy round close in goes through a thin wall: a hole each side, out the far side slower, and on.
        if (b.WallLeft > 0f && b.Walls < 3 && CloseIn(b, hitPoint)
            && level.ShootThrough(hitPoint, direction, b.WallLeft, out var exit, out var exitNormal, out var used, out var exitSurface))
        {
            effects.Hit(hitPoint, hitNormal, direction, surface, holeScale);
            Sounds?.Impact(hitPoint, surface, b.FromPlayer, b.FiredBy);
            LogStrike(b, $"{surface.Kind.ToString().ToLowerInvariant()} and went through ({used:0.00} of {b.WallLeft:0.00} m brick's worth)", hitPoint, speed);
            var kept = 1f - 0.5f * used / (b.Round?.WallPenetration ?? 1f);
            b.WallLeft -= used;
            b.Walls++;
            b.Velocity *= kept;
            b.Position = exit + direction * 0.003f;
            // On through the rest of the frame's path (now slower), to whatever's behind the wall; then the exit hole
            // (after, or the bullet would hit the chunks it throws out).
            var rest = (length - distance - Length(exit - hitPoint)) * kept;
            var flying = rest <= 1e-3f || Advance(ref b, b.Position, b.Position + direction * rest);
            effects.Hit(exit, exitNormal, -direction, exitSurface, holeScale * 1.4f);   // (spalling out the back, away from the wall)
            Sounds?.Impact(exit, exitSurface, b.FromPlayer, b.FiredBy);
            return flying;
        }

        // Glancing blows off hard surfaces can ricochet (once), losing much of their speed.
        var cosIncidence = -float3.Dot(direction, hitNormal);
        if (surface.Hard && b.Ricochets == 0 && cosIncidence < 0.26f && _random.NextDouble() < (b.Round?.RicochetChance ?? 0.75))
        {
            effects.Ricochet(hitPoint, hitNormal, direction, surface);
            Sounds?.Ricochet(hitPoint, surface, b.FromPlayer, b.FiredBy);
            var reflected = direction + hitNormal * (2f * cosIncidence);
            var scatter = Particles.RandomDirection() * 0.12f;
            var outDir = float3.Normalize(reflected + scatter + hitNormal * 0.05f);
            b.Velocity = outDir * speed * (0.35f + 0.25f * (float)_random.NextDouble());
            b.Position = hitPoint + hitNormal * 0.003f;
            b.Ricochets++;
            return true;
        }

        effects.Hit(hitPoint, hitNormal, direction, surface, holeScale);
        Sounds?.Impact(hitPoint, surface, b.FromPlayer, b.FiredBy);
        LogStrike(b, surface.Kind.ToString().ToLowerInvariant(), hitPoint, speed);
        DropSpent(b, hitPoint, hitNormal, direction, surface);
        return false;
    }

    /// <summary>A round that goes through walls and armour (<see cref="Cartridge.WallPenetration"/>), still within its close range at <paramref name="point"/>.</summary>
    private static bool CloseIn(in Bullet b, float3 point) =>
        b.Round is { WallPenetration: > 0f, CloseRange: var range } && b.Ricochets == 0 && Length(point - b.Origin) <= range;

    /// <summary>On something hard, a round that stays in one piece bounces back and drops; in anything softer it stays in.</summary>
    private void DropSpent(in Bullet b, float3 point, float3 normal, float3 direction, in SurfaceInfo surface)
    {
        if (b.Round is not { } round || !surface.Hard || Debris is not { } debris || _random.NextDouble() >= round.SpentChance)
            return;
        debris.SpentBullet(round, point, normal, direction);
        if (DebugLog)
            Log.Write($"[BULLET] {round.Name} dropped spent, {debris.Count} pieces of debris");
    }

    /// <summary>Someone else's bullet passing within 3 m of the player's head this frame: it cracks past (once).</summary>
    private void NearMiss(ref Bullet b, float3 start)
    {
        if (b.FromPlayer || b.Cracked || Sounds is null || Player is not { Alive: true } player)
            return;
        var head = player.EyePosition;
        var path = b.Position - start;
        var length2 = float3.Dot(path, path);
        if (length2 < 1e-6f || float3.Dot(head - b.Origin, head - b.Origin) < 4f)
            return;
        var t = float.Clamp(float3.Dot(head - start, path) / length2, 0f, 1f);
        var closest = start + path * t;
        var miss = head - closest;
        var distance = float.Sqrt(float3.Dot(miss, miss));
        if (distance > 3f)
            return;
        b.Cracked = true;
        Sounds.NearMiss(closest, distance);
    }

    /// <summary>
    /// A baton's blow from the eye along <paramref name="forward"/>, out to <paramref name="reach"/>: anyone under the
    /// crosshair or just off it is struck, unless something solid or a portal's water is in the way; otherwise it lands on
    /// whatever is under the crosshair. False if it met nothing.
    /// </summary>
    public bool Melee(float3 eye, float3 forward, float reach)
    {
        // How far a ray along this line gets before the world or a portal's water stops it.
        float Clear(float3 direction)
        {
            var clear = reach;
            if (physics is not null)
            {
                if (physics.Raycast(eye, eye + direction * reach, out var hit))
                    clear = hit.Fraction * reach;
            }
            else if (level.RaycastSurface(eye, direction, reach, out var d, out _, out _))
                clear = d;
            return portals.Raycast(eye, direction, clear, out var water) ? water : clear;
        }

        // People first, the middle of the swing's arc before its edges (a blow is wider than a bullet).
        var side = float3.Normalize(float3.Cross(forward, float3.UnitY));
        var up = float3.Cross(side, forward);
        ReadOnlySpan<float2> arc = [new(0, 0), new(-0.09f, 0), new(0.09f, 0), new(0, -0.09f), new(0, 0.09f), new(-0.18f, 0), new(0.18f, 0)];
        foreach (var offset in arc)
        {
            var direction = float3.Normalize(forward + side * offset.X + up * offset.Y);
            if (!npcs.Raycast(eye, direction, Clear(direction), out var person))
                continue;
            var point = eye + direction * person.Skin.Distance;
            npcs.Strike(person, direction, eye);
            if (person.Npc.Profile.ArmourCovers(person.Part, person.Skin.BindPosition))
                Sounds?.Impact(point, ArmourPlate, true);
            else
                Sounds?.Flesh(point);
            return true;
        }

        var clear = Clear(forward);
        if (portals.Raycast(eye, forward, reach, out var waterDistance, out var portal) && waterDistance <= clear)
        {
            var point = eye + forward * waterDistance;
            portals.Splash(portal!, point);
            effects.Splash(point, portal!.Normal, 0.4f);
            return true;
        }

        foreach (var target in targets)
        {
            if (target.Raycast(eye, forward, clear, 0, out var d, out _))
            {
                Sounds?.Impact(eye + forward * d, new SurfaceInfo(SurfaceKind.Wood, new float3(0.7f), Material.Wood), true);
                return true;
            }
        }

        if (clear >= reach)
            return false;
        SurfaceInfo surface;
        float3 normal;
        BulletSharp.RigidBody? body = null;
        if (physics is not null && physics.Raycast(eye, eye + forward * reach, out var solid))
        {
            surface = surfaces[solid.UserIndex];
            normal = solid.Normal;
            body = solid.Body;
        }
        else if (!level.RaycastSurface(eye, forward, reach, out _, out normal, out surface))
            return false;

        var at = eye + forward * clear;
        if (body is not null)
            PhysicsWorld.Push(body, forward * (body.InvMass > 0 ? 3f / (float)body.InvMass : 3f), at);   // a shove of 3 m/s
        effects.Scuff(at, normal, forward);
        Sounds?.Impact(at, surface, true);
        Log.Write($"[BATON] Struck {surface.Kind.ToString().ToLowerInvariant()}");
        return true;
    }

    /// <summary>
    /// What the crosshair is on: the distance to the first solid thing, portal water or target along the ray.
    /// </summary>
    public float AimDistance(float3 eye, float3 forward, float maxDistance, out string what)
    {
        var distance = maxDistance;
        what = "nothing";
        if (physics is not null)
        {
            if (physics.Raycast(eye, eye + forward * maxDistance, out var hit))
            {
                distance = hit.Fraction * maxDistance;
                what = surfaces[hit.UserIndex].Kind.ToString().ToLowerInvariant();
            }
        }
        else if (level.RaycastSurface(eye, forward, maxDistance, out var d, out _, out var s))
        {
            distance = d;
            what = s.Kind.ToString().ToLowerInvariant();
        }

        if (npcs.Raycast(eye, forward, distance, out var person))
        {
            distance = person.Skin.Distance;
            what = person.Npc.Name;
        }

        foreach (var target in targets)
        {
            if (target.Raycast(eye, forward, distance, 0, out var d, out _) && d < distance)
            {
                distance = d;
                what = target.Name;
            }
        }

        if (portals.Raycast(eye, forward, distance, out var portalDistance, out var portal))
        {
            distance = portalDistance;
            what = portal!.Name;
        }

        return distance;
    }
}
