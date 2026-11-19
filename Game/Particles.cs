using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>Particle kinds (must match the shader's mode 6).</summary>
public enum ParticleKind
{
    Dust = 0,       // a lit puff of dust or smoke that grows and fades
    Chip = 1,       // a tiny lit fleck (stone, paint, soil, wood)
    Droplet = 2,    // a lit water droplet
    Spark = 3,      // a glowing streak drawn out along its velocity
    Tracer = 4,     // a glowing streak behind a bullet
    Flash = 5,      // a glowing star (muzzle flash)
    Snow = 6,       // a lit puff of snow
    Trail = 7,      // a glowing streak as bright all along, to join end to end with others
    Smoke = 8,      // a lit, soft puff of a smoke screen: thick to the eye, invisible to a thermal imager
}

/// <summary>
/// Cheap effect particles: dust and smoke puffs, chips, droplets and sparks. They're simulated on the CPU (sparks and
/// chips bounce off the physics world) and drawn in two batches (lit and glowing), as camera-facing quads expanded by
/// the vertex shader, so each portal view sees them face-on too. The number alive is capped: when it's full, new ones
/// replace the ones closest to dying.
/// </summary>
public sealed class Particles(PhysicsWorld? physics, int capacity = 700)
{
    public struct Particle
    {
        public float3 Position;
        public float3 Velocity;
        public float3 Color;      // display-space tint (lit kinds) or linear HDR (glowing kinds)
        public ParticleKind Kind;
        public float Size;        // half size (or half width for streaks), metres
        public float Growth;      // size added per second
        public float Life;
        public float Age;
        public float Alpha;
        public float Gravity;     // multiple of g
        public float Drag;        // per second
        public float Rotation;
        public float Spin;
        public float StreakTime;  // sparks: how many seconds of motion the streak shows
        public bool Bounce;       // collides with the world
        public bool Splat;        // (with Bounce) dies where it lands, raising Landed (blood drops leave a stain)
    }

    /// <summary>A <see cref="Particle.Splat"/> particle landed: where, the surface's normal, how fast it was going, and its size.</summary>
    public event Action<float3, float3, float3, float>? Landed;

    private readonly Particle[] _items = new Particle[capacity];
    private int _count;
    private readonly DynamicMesh _lit = new();
    private readonly DynamicMesh _glowing = new();
    private readonly List<(float3 Head, float3 Tail, float3 Color, float HalfWidth, float Alpha, ParticleKind Kind)> _streaks = [];

    public int Count => _count;

    /// <summary>Every particle gone at once (a respawn: no smoke screen hanging about from the last life).</summary>
    public void Clear()
    {
        _count = 0;
        _streaks.Clear();
    }

    public void Add(in Particle particle)
    {
        if (_count < _items.Length)
        {
            _items[_count++] = particle;
            return;
        }

        // Full: replace whichever is nearest the end of its life.
        var victim = 0;
        var best = float.MaxValue;
        for (var i = 0; i < _count; i++)
        {
            var left = _items[i].Life - _items[i].Age;
            if (left < best)
            {
                best = left;
                victim = i;
            }
        }

        _items[victim] = particle;
    }

    /// <summary>A glowing streak drawn for this frame only (a bullet's motion blur).</summary>
    public void Streak(float3 head, float3 tail, float3 color, float halfWidth, float alpha, ParticleKind kind = ParticleKind.Tracer) =>
        _streaks.Add((head, tail, color, halfWidth, alpha, kind));

    public void Update(float dt)
    {
        for (var i = _count - 1; i >= 0; i--)
        {
            ref var p = ref _items[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _items[i] = _items[--_count];
                continue;
            }

            p.Velocity.Y -= 9.81f * p.Gravity * dt;
            p.Velocity *= float.Max(0f, 1f - p.Drag * dt);
            var step = p.Velocity * dt;
            if (p.Bounce && physics is not null && float3.Dot(step, step) > 1e-8f
                && physics.Raycast(p.Position, p.Position + step, out var hit, includeBodies: false))
            {
                if (p.Splat)
                {
                    Landed?.Invoke(hit.Point, hit.Normal, p.Velocity, p.Size);
                    _items[i] = _items[--_count];
                    continue;
                }

                // Bounce off, losing most of the speed into the surface.
                var vn = float3.Dot(p.Velocity, hit.Normal);
                p.Velocity = (p.Velocity - hit.Normal * vn) * 0.6f - hit.Normal * vn * 0.35f;
                p.Position = hit.Point + hit.Normal * 0.002f;
            }
            else
            {
                p.Position += step;
            }

            p.Size += p.Growth * dt;
            p.Rotation += p.Spin * dt;
        }
    }

    /// <summary>Rebuilds the batches (call once per frame, after <see cref="Update"/>).</summary>
    public void Build()
    {
        _lit.Clear();
        _glowing.Clear();
        for (var i = 0; i < _count; i++)
        {
            ref var p = ref _items[i];
            var t = p.Age / p.Life;
            var alpha = p.Kind switch
            {
                ParticleKind.Dust or ParticleKind.Snow => p.Alpha * (1f - t) * (1f - t) * float.Min(1f, p.Age * 20f),
                ParticleKind.Smoke => p.Alpha * (1f - t * t * t) * float.Min(1f, p.Age * 1.5f),   // (thick until near the end)
                ParticleKind.Spark => p.Alpha * (1f - t * t),
                _ => p.Alpha * (1f - float.Max(0f, t - 0.7f) / 0.3f),
            };
            if (p.Kind == ParticleKind.Spark)
            {
                var tail = -p.Velocity * p.StreakTime;
                var color = p.Color * (1f - 0.6f * t);   // cooling
                AddStreak(_glowing, p.Position, tail, color, p.Size, alpha, ParticleKind.Spark);
            }
            else
            {
                AddBillboard(p.Kind == ParticleKind.Flash ? _glowing : _lit, p.Position, p.Size, p.Rotation, p.Color, alpha, p.Kind, i);
            }
        }

        foreach (var (head, tail, color, halfWidth, alpha, kind) in _streaks)
            AddStreak(_glowing, head, tail - head, color, halfWidth, alpha, kind);
        _streaks.Clear();
    }

    /// <param name="smokeScreen">These are a smoke screen's puffs, and no more of them than <see cref="Renderer.SmokeLightSize"/> squared: lit once each (see <see cref="Renderer.DrawParticles"/>).</param>
    public void Draw(Renderer renderer, bool smokeScreen = false) => renderer.DrawParticles(_lit, _glowing, smokeScreen);

    private static float4 Packed(float3 color, float alpha, ParticleKind kind) =>
        new(color, (int)kind + 0.05f + 0.9f * float.Clamp(alpha, 0f, 1f));

    private static void AddBillboard(DynamicMesh mesh, float3 center, float halfSize, float rotation, float3 color, float alpha, ParticleKind kind, int index)
    {
        var packed = Packed(color, alpha, kind);
        var n = new float3(rotation, index, 0);   // (its index: which texel a smoke screen's puff is lit into)
        mesh.Vertex(center, n, new float2(-halfSize, -halfSize), packed);
        mesh.Vertex(center, n, new float2(halfSize, -halfSize), packed);
        mesh.Vertex(center, n, new float2(halfSize, halfSize), packed);
        mesh.Vertex(center, n, new float2(-halfSize, -halfSize), packed);
        mesh.Vertex(center, n, new float2(halfSize, halfSize), packed);
        mesh.Vertex(center, n, new float2(-halfSize, halfSize), packed);
    }

    private static void AddStreak(DynamicMesh mesh, float3 head, float3 tail, float3 color, float halfWidth, float alpha, ParticleKind kind)
    {
        var packed = Packed(color, alpha, kind);
        mesh.Vertex(head, tail, new float2(-halfWidth, 0), packed);
        mesh.Vertex(head, tail, new float2(halfWidth, 0), packed);
        mesh.Vertex(head, tail, new float2(halfWidth, 1), packed);
        mesh.Vertex(head, tail, new float2(-halfWidth, 0), packed);
        mesh.Vertex(head, tail, new float2(halfWidth, 1), packed);
        mesh.Vertex(head, tail, new float2(-halfWidth, 1), packed);
    }

    // ------------------------------------------------------------------------------------------------ helpers

    private static readonly Random Rng = new(4242);

    public static float Range(float min, float max) => min + (float)Rng.NextDouble() * (max - min);

    /// <summary>A random unit vector.</summary>
    public static float3 RandomDirection()
    {
        while (true)
        {
            var v = new float3(Range(-1, 1), Range(-1, 1), Range(-1, 1));
            var l2 = float3.Dot(v, v);
            if (l2 > 0.01f && l2 <= 1f)
                return v / float.Sqrt(l2);
        }
    }

    /// <summary>A random direction within a cone of half-angle <paramref name="spread"/> (radians-ish) round <paramref name="axis"/>.</summary>
    public static float3 Cone(float3 axis, float spread)
    {
        var d = axis + RandomDirection() * float.Tan(float.Min(spread, 1.4f));
        return float3.Normalize(d);
    }
}
