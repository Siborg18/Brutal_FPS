using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// What a bullet does to what it hits: the hole it leaves (<see cref="BulletHoles"/>), the dust, sparks and chips
/// that fly (<see cref="Particles"/>), and the bits that fall and lie about (<see cref="DebrisSystem"/>), all by what
/// the surface is made of. Also ricochet gouges and the squashed bullets that drop from a portal's water.
/// </summary>
public sealed class ImpactEffects(PhysicsWorld? physics, BulletHoles holes, Particles particles, DebrisSystem debris)
{
    private readonly Random _random = new(7);
    private float _holeScale = 1f;   // of the hit in hand (see Hit)

    private float R(float min, float max) => min + (float)_random.NextDouble() * (max - min);

    /// <summary>
    /// A bullet struck <paramref name="surface"/> at <paramref name="point"/> and stopped there;
    /// <paramref name="holeScale"/> is how big its hole is against a 9 mm round's. <paramref name="inPhysics"/> false: what's
    /// hit isn't in the physics world (a target's board), so its hole can't be checked against the surface's edges.
    /// </summary>
    public void Hit(float3 point, float3 normal, float3 direction, SurfaceInfo surface, float holeScale = 1f, bool inPhysics = true)
    {
        _holeScale = holeScale;
        _holeChecked = inPhysics;
        var tint = surface.Tint;
        var lift = point + normal * 0.012f;
        switch (surface.Kind)
        {
            case SurfaceKind.Concrete:
            case SurfaceKind.Rock:
            {
                var stone = surface.Kind == SurfaceKind.Rock;
                Hole(point, normal, R(0.05f, 0.07f), stone ? HoleKind.Rock : HoleKind.Concrete);
                var dust = stone ? new float3(0.32f, 0.32f, 0.33f) : Float3Lerp(tint, new float3(0.62f, 0.6f, 0.56f), 0.6f);
                Puffs(lift, normal, direction, dust, 4, 0.04f, 0.32f, 1.6f);
                Chips(lift, normal, direction, stone ? new float3(0.15f) : new float3(0.55f, 0.54f, 0.5f), 10, 2f, 6f);
                debris.Chunks(point, normal, direction, stone ? new float3(0.16f, 0.16f, 0.17f) : new float3(0.56f, 0.55f, 0.51f), 2 + _random.Next(3));
                break;
            }
            case SurfaceKind.Metal:
            case SurfaceKind.PaintedMetal:
            case SurfaceKind.SheetMetal:
            {
                var kind = surface.Kind switch
                {
                    SurfaceKind.PaintedMetal => HoleKind.PaintedMetal,
                    SurfaceKind.SheetMetal => HoleKind.SheetMetal,
                    _ => HoleKind.Metal,
                };
                Hole(point, normal, kind == HoleKind.SheetMetal ? R(0.045f, 0.055f) : R(0.042f, 0.052f), kind);
                Sparks(lift, normal, direction, 10 + _random.Next(8));
                Puffs(lift, normal, direction, new float3(0.35f, 0.34f, 0.33f), 2, 0.03f, 0.18f, 1.0f);
                if (kind == HoleKind.PaintedMetal)
                    Chips(lift, normal, direction, tint, 6, 1.5f, 4f);   // flakes of paint
                break;
            }
            case SurfaceKind.Wood:
                Hole(point, normal, R(0.04f, 0.05f), HoleKind.Wood, grain: true);
                Puffs(lift, normal, direction, new float3(0.55f, 0.45f, 0.33f), 2, 0.03f, 0.2f, 1.2f);
                Chips(lift, normal, direction, new float3(0.62f, 0.48f, 0.32f), 8, 2f, 5f);
                debris.Splinters(point, normal, direction, new float3(0.66f, 0.52f, 0.36f), 2 + _random.Next(2));
                break;
            case SurfaceKind.Glass:
                // Bright, backlit panes (the warehouse windows, lamps) show dark cracks; dark glass, and a window with the night behind it, white ones.
                Hole(point, normal, R(0.11f, 0.15f), surface.Material is Material.Window or Material.Emissive && float.Max(surface.Tint.X, float.Max(surface.Tint.Y, surface.Tint.Z)) > 0.3f ? HoleKind.GlassLit : HoleKind.Glass);
                Chips(lift, normal, direction, new float3(0.85f, 0.9f, 0.92f), 12, 1f, 4f);
                debris.Shards(point, normal, direction, 2 + _random.Next(3));
                break;
            case SurfaceKind.Dirt:
            {
                var red = surface.Material is Material.RedDirt or Material.DirtRoad;
                Hole(point, normal, R(0.075f, 0.095f), red ? HoleKind.RedDirt : HoleKind.Ground);
                var soil = red ? new float3(0.62f, 0.32f, 0.17f) : new float3(0.42f, 0.4f, 0.37f);
                Puffs(lift, normal, direction, soil, 5, 0.05f, 0.4f, 1.8f);
                Chips(lift, normal, direction, soil * 0.7f, 14, 2f, 5f);
                break;
            }
            case SurfaceKind.Snow:
                Hole(point, normal, R(0.06f, 0.075f), HoleKind.Snow);
                for (var i = 0; i < 6; i++)
                    Puff(lift, Particles.Cone(normal, 0.6f) * R(0.5f, 2.5f), new float3(0.92f, 0.94f, 0.97f), ParticleKind.Snow, R(0.04f, 0.08f), R(0.25f, 0.45f), R(0.9f, 1.6f), 0.75f);
                Chips(lift, normal, direction, new float3(0.95f), 10, 1.5f, 4f);
                break;
            case SurfaceKind.Plastic:
            case SurfaceKind.Rubber:
                Hole(point, normal, R(0.022f, 0.028f), HoleKind.Plastic);
                Chips(lift, normal, direction, tint * 0.6f, 4, 1f, 3f);
                Puffs(lift, normal, direction, new float3(0.3f), 1, 0.02f, 0.12f, 0.8f);
                break;
            case SurfaceKind.Fabric:
                Hole(point, normal, R(0.02f, 0.026f), HoleKind.Fabric);
                Puffs(lift, normal, direction, tint, 1, 0.02f, 0.1f, 0.8f);
                break;
            case SurfaceKind.Water:
                Splash(point, normal, 1f);
                break;
        }

        _holeScale = 1f;
        _holeChecked = true;
    }

    /// <summary>
    /// A shallow shot glanced off: a gouge running the way it was going (bright on steel, grey on stone) and a spray of
    /// sparks or grit.
    /// </summary>
    public void Ricochet(float3 point, float3 normal, float3 direction, SurfaceInfo surface)
    {
        var along = direction - normal * float3.Dot(direction, normal);
        var length = R(0.06f, 0.1f);
        holes.Add(point + float3.Normalize(along + new float3(1e-5f)) * length * 0.6f, normal, along, length, length * 0.28f,
            surface.Metallic ? HoleKind.ScuffMetal : HoleKind.ScuffStone);
        if (DebugLog)
            Log.Write($"[RICOCHET] off {surface.Kind} at {point.X:0.00},{point.Y:0.00},{point.Z:0.00}");
        var lift = point + normal * 0.01f;
        if (surface.Metallic || surface.Kind == SurfaceKind.Glass)
            Sparks(lift, normal, direction, 14);
        else
            Puffs(lift, normal, direction, new float3(0.5f, 0.49f, 0.46f), 2, 0.03f, 0.2f, 1.2f);
    }

    /// <summary>A baton struck something solid: a puff of dust, and no mark.</summary>
    public void Scuff(float3 point, float3 normal, float3 direction) =>
        Puffs(point + normal * 0.01f, normal, direction, new float3(0.4f), 2, 0.02f, 0.14f, 0.8f);

    /// <summary>A bullet hit something loose (a dropped magazine, a casing): it gets knocked about, and flashes.</summary>
    public void HitBody(float3 point, float3 normal, float3 direction, SurfaceInfo surface)
    {
        var lift = point + normal * 0.01f;
        if (surface.Metallic)
            Sparks(lift, normal, direction, 6);
        Puffs(lift, normal, direction, new float3(0.35f), 1, 0.02f, 0.12f, 0.8f);
    }

    /// <summary>
    /// A bullet stopped by a portal's water: a burst of droplets, and the bullet, flattened against the surface,
    /// drops to the ground. (The ripple itself is the portal's.)
    /// </summary>
    public void PortalSquish(Portal portal, float3 point, float3 direction)
    {
        Splash(point, portal.Normal, 0.6f);
        debris.Slug(point, portal.Normal, direction);
    }

    /// <summary>Droplets thrown up off water, and a little mist.</summary>
    public void Splash(float3 point, float3 normal, float strength)
    {
        var count = (int)(18 * strength);
        for (var i = 0; i < count; i++)
        {
            var v = Particles.Cone(float3.Normalize(normal + new float3(0, 0.6f, 0)), 0.7f) * R(1.2f, 4.5f) * strength;
            particles.Add(new Particles.Particle
            {
                Position = point + normal * 0.01f,
                Velocity = v,
                Color = new float3(0.72f, 0.8f, 0.85f),
                Kind = ParticleKind.Droplet,
                Size = R(0.004f, 0.009f),
                Life = R(0.35f, 0.8f),
                Alpha = 0.85f,
                Gravity = 1f,
                Drag = 0.5f,
                Bounce = false,
            });
        }

        Puffs(point + normal * 0.02f, normal, -normal, new float3(0.8f, 0.85f, 0.9f), 2, 0.04f, 0.25f, 0.7f, 0.35f);
    }

    // ------------------------------------------------------------------------------------------------ blood

    private static readonly float3 BloodColor = new(0.40f, 0.025f, 0.025f);   // display space (the drops are lit)
    private static readonly float3 MistColor = new(0.46f, 0.06f, 0.05f);

    /// <summary>A bullet went into someone at <paramref name="point"/>: a puff of red mist and a few drops thrown back out.</summary>
    public void BloodEntry(float3 point, float3 normal, float3 direction)
    {
        var lift = point + normal * 0.01f;
        for (var i = 0; i < 3; i++)
            Puff(lift, Particles.Cone(normal, 0.6f) * R(0.2f, 0.8f), MistColor, ParticleKind.Dust, R(0.012f, 0.02f), R(0.08f, 0.16f), R(0.18f, 0.35f), 0.45f, gravity: 0.1f);
        Drops(lift, float3.Normalize(normal * 1.2f - direction * 0.3f), 0.7f, 6 + _random.Next(5), 0.8f, 2.8f);
    }

    /// <summary>
    /// A bullet came out of someone at <paramref name="point"/>: a cone of blood thrown on ahead (the drops stain what
    /// they land on), and a splash on the wall or floor if one is close behind.
    /// </summary>
    public void BloodExit(float3 point, float3 direction)
    {
        for (var i = 0; i < 4; i++)
            Puff(point, direction * R(0.8f, 2.2f) + Particles.RandomDirection() * 0.3f, MistColor, ParticleKind.Dust, R(0.015f, 0.03f), R(0.15f, 0.3f), R(0.25f, 0.45f), 0.5f, gravity: 0.15f);
        Drops(point, direction, 0.35f, 22 + _random.Next(12), 2.5f, 8f);
        const float reach = 2.2f;
        if (physics is not null && physics.Raycast(point, point + direction * reach, out var hit, includeBodies: false))
        {
            var dist = hit.Fraction * reach;
            var stretch = 1f + 1.5f * (1f - float.Abs(float3.Dot(direction, hit.Normal)));
            var size = float.Clamp(0.08f + dist * 0.09f, 0.08f, 0.26f);
            holes.Add(hit.Point, hit.Normal, Along(direction, hit.Normal), size * stretch, size, HoleKind.BloodSplash);
        }
    }

    /// <summary>A drop of blood falling from a wound.</summary>
    public void BloodDrip(float3 point, float3 velocity) =>
        particles.Add(new Particles.Particle
        {
            Position = point,
            Velocity = velocity,
            Color = BloodColor,
            Kind = ParticleKind.Droplet,
            Size = R(0.0025f, 0.004f),
            Life = 3f,
            Alpha = 1f,
            Gravity = 1f,
            Drag = 0.1f,
            Bounce = true,
            Splat = true,
        });

    /// <summary>A blood drop landed (<see cref="Particles.Landed"/>): a stain, drawn out the more glancing the impact.</summary>
    public void BloodLanded(float3 point, float3 normal, float3 velocity, float size)
    {
        var speed = float.Sqrt(float3.Dot(velocity, velocity));
        var dir = speed > 1e-4f ? velocity / speed : -normal;
        var glance = 1f - float.Abs(float3.Dot(dir, normal));
        var r = float.Clamp(size * (2.5f + speed * 0.45f), 0.006f, 0.03f);
        holes.Add(point, normal, Along(dir, normal), r * (1f + 2f * glance), r, HoleKind.BloodDrop);
    }

    private void Drops(float3 point, float3 axis, float spread, int count, float minSpeed, float maxSpeed)
    {
        for (var i = 0; i < count; i++)
        {
            particles.Add(new Particles.Particle
            {
                Position = point,
                Velocity = Particles.Cone(axis, spread) * R(minSpeed, maxSpeed),
                Color = BloodColor * R(0.8f, 1.1f),
                Kind = ParticleKind.Droplet,
                Size = R(0.0015f, 0.0045f),
                Life = R(1.2f, 2.5f),
                Alpha = 1f,
                Gravity = 1f,
                Drag = 0.3f,
                Bounce = true,
                Splat = true,
            });
        }
    }

    /// <summary>A direction lying in the surface: <paramref name="direction"/> flattened onto it, or any if it's square on.</summary>
    private static float3 Along(float3 direction, float3 normal)
    {
        var a = direction - normal * float3.Dot(direction, normal);
        return float3.Dot(a, a) > 1e-6f ? a : Perpendicular(normal);
    }

    /// <summary>Smoke curling from the muzzle after a shot.</summary>
    public void MuzzleSmoke(float3 point, float3 forward, float3 inheritVelocity)
    {
        for (var i = 0; i < 2; i++)
            Puff(point + forward * R(0.0f, 0.04f), forward * R(0.3f, 1.0f) + inheritVelocity * 0.8f + new float3(0, R(0.05f, 0.25f), 0),
                new float3(0.62f, 0.62f, 0.64f), ParticleKind.Dust, R(0.01f, 0.02f), R(0.08f, 0.16f), R(0.5f, 0.9f), 0.12f, gravity: -0.03f);
    }

    // ------------------------------------------------------------------------------------------------ pieces

    /// <summary>
    /// Lays a hole of radius <paramref name="radius"/> on the surface. Near an edge it shrinks until its corners are on
    /// the surface (so a hole near a corner doesn't hang off it into the air).
    /// </summary>
    private void Hole(float3 point, float3 normal, float radius, HoleKind kind, bool grain = false)
    {
        radius *= _holeScale;
        // The quad's x axis: across the grain for wood (planks run level), otherwise any way round.
        var horizontal = float3.Cross(float3.UnitY, normal);
        var h2 = float3.Dot(horizontal, horizontal);
        var tangent = grain && h2 > 0.01f
            ? horizontal / float.Sqrt(h2)
            : float3.Normalize(Perpendicular(normal) * float.Cos(R(0, float.Tau)) + float3.Cross(normal, Perpendicular(normal)) * float.Sin(R(0, float.Tau)));
        var bitangent = float3.Cross(normal, tangent);
        if (physics is not null && _holeChecked)
        {
            // (The crater's rim fades out, so a little overhang at the very edge doesn't show.)
            for (var attempt = 0; attempt < 4 && radius > 0.006f; attempt++)
            {
                if (Supported(point, normal, tangent, bitangent, radius * 0.6f))
                    break;
                radius *= 0.75f;
            }
        }

        holes.Add(point, normal, tangent, radius, radius, kind);
        if (DebugLog)
            Log.Write($"[HOLE] {kind} r={radius * 100:0.0} cm at {point.X:0.00},{point.Y:0.00},{point.Z:0.00} n={normal.X:0.00},{normal.Y:0.00},{normal.Z:0.00}");
    }

    private static readonly bool DebugLog = Environment.GetEnvironmentVariable("BRUTALFPS_DEBUG") == "1";

    private bool _holeChecked = true;

    private bool Supported(float3 point, float3 normal, float3 tangent, float3 bitangent, float r)
    {
        ReadOnlySpan<float2> corners = [new(1, 1), new(-1, 1), new(1, -1), new(-1, -1)];
        foreach (var c in corners)
        {
            var p = point + (tangent * c.X + bitangent * c.Y) * r;
            if (!physics!.Raycast(p + normal * 0.012f, p - normal * 0.012f, out _, includeBodies: false))
                return false;
        }

        return true;
    }

    private static float3 Perpendicular(float3 n) =>
        float3.Normalize(float3.Cross(n, float.Abs(n.Y) < 0.9f ? float3.UnitY : float3.UnitX));

    private void Sparks(float3 point, float3 normal, float3 direction, int count)
    {
        var reflected = direction - normal * (2f * float3.Dot(direction, normal));
        for (var i = 0; i < count; i++)
        {
            var dir = float3.Normalize(Particles.Cone(float3.Normalize(normal + reflected * 0.8f), 0.9f));
            particles.Add(new Particles.Particle
            {
                Position = point,
                Velocity = dir * R(3f, 11f),
                Color = new float3(6f, 2.6f, 0.7f) * R(0.6f, 1.3f),
                Kind = ParticleKind.Spark,
                Size = R(0.0012f, 0.0022f),
                Life = R(0.12f, 0.45f),
                Alpha = 1f,
                Gravity = 1f,
                Drag = 1.5f,
                StreakTime = 0.022f,
                Bounce = true,
            });
        }

        // The flash where it struck.
        particles.Add(new Particles.Particle
        {
            Position = point,
            Color = new float3(5f, 3f, 1.2f),
            Kind = ParticleKind.Flash,
            Size = R(0.03f, 0.05f),
            Life = 0.05f,
            Alpha = 1f,
            Rotation = R(0, float.Tau),
        });
    }

    private void Chips(float3 point, float3 normal, float3 direction, float3 color, int count, float minSpeed, float maxSpeed)
    {
        var reflected = direction - normal * (2f * float3.Dot(direction, normal));
        for (var i = 0; i < count; i++)
        {
            var dir = float3.Normalize(Particles.Cone(float3.Normalize(normal * 1.3f + reflected * 0.4f), 0.85f));
            particles.Add(new Particles.Particle
            {
                Position = point,
                Velocity = dir * R(minSpeed, maxSpeed),
                Color = color * R(0.75f, 1.15f),
                Kind = ParticleKind.Chip,
                Size = R(0.0015f, 0.0035f),
                Life = R(0.6f, 1.4f),
                Alpha = 1f,
                Gravity = 1f,
                Drag = 0.4f,
                Rotation = R(0, float.Tau),
                Spin = R(-20f, 20f),
                Bounce = true,
            });
        }
    }

    private void Puffs(float3 point, float3 normal, float3 direction, float3 color, int count, float size, float growth, float life, float alpha = 0.55f)
    {
        for (var i = 0; i < count; i++)
        {
            var v = Particles.Cone(normal, 0.5f) * R(0.3f, 1.4f) - direction * R(0.0f, 0.3f);
            Puff(point, v, color, ParticleKind.Dust, size * R(0.7f, 1.3f), growth * R(0.7f, 1.3f), life * R(0.7f, 1.3f), alpha);
        }
    }

    private void Puff(float3 point, float3 velocity, float3 color, ParticleKind kind, float size, float growth, float life, float alpha, float gravity = 0.02f) =>
        particles.Add(new Particles.Particle
        {
            Position = point,
            Velocity = velocity,
            Color = color,
            Kind = kind,
            Size = size,
            Growth = growth,
            Life = life,
            Alpha = alpha,
            Gravity = gravity,
            Drag = 2.5f,
            Rotation = R(0, float.Tau),
            Spin = R(-1.5f, 1.5f),
        });

    private static float3 Float3Lerp(float3 a, float3 b, float t) => a + (b - a) * t;
}
