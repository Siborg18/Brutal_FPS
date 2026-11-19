using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>Bullet hole looks (must match the shader's decal kinds in <c>shadeDecal</c>).</summary>
public enum HoleKind
{
    Concrete = 0,
    Rock = 1,
    Metal = 2,
    PaintedMetal = 3,
    SheetMetal = 4,
    Wood = 5,
    Glass = 6,
    RedDirt = 7,
    Ground = 8,
    Snow = 9,
    Plastic = 10,
    Fabric = 11,
    ScuffMetal = 12,
    ScuffStone = 13,
    GlassLit = 14,     // a bright, backlit pane: dark cracks
    // (15 is the characters' contact shadow, drawn by Npcs)
    BloodSplash = 16,  // blood thrown onto a surface, pulled out the way it was flying
    BloodDrop = 17,    // a single drop of blood
}

/// <summary>
/// The bullet holes in the world: one small quad each, laid on the surface that was hit, drawn in one batch and
/// shaded procedurally by the shader (crater, dent, splinters, cracks...). There are at most <see cref="Capacity"/>:
/// the oldest ones fade out as new ones arrive and are then reused, so the count (and the cost) never grows.
/// A hole in a door goes with the door.
/// </summary>
public sealed class BulletHoles(PhysicsWorld? physics)
{
    public const int Capacity = 400;
    private const int FadingTail = 40;   // the oldest holes fade out over this many newer ones

    private struct Hole
    {
        public float3 Position;
        public float3 Normal;
        public float3 Tangent;
        public float HalfX;
        public float HalfY;
        public HoleKind Kind;
        public int Seed;
        public BulletSharp.CollisionObject? Door;   // the door's part it's in, and then the above are in that part's space

        /// <summary>The hole where it is now.</summary>
        public readonly Hole InWorld()
        {
            if (Door is null)
                return this;
            var pose = PhysicsWorld.FromBullet(Door.WorldTransform);
            var h = this;
            h.Position = float3.Transform(Position, pose);
            h.Normal = float3.TransformNormal(Normal, pose);
            h.Tangent = float3.TransformNormal(Tangent, pose);
            return h;
        }
    }

    private readonly Hole[] _holes = new Hole[Capacity];
    private int _count;
    private int _next;   // where the next hole goes (the oldest once full)
    private readonly DynamicMesh _mesh = new();
    private bool _dirty;
    private int _seed = 1;
    private int _doorMoves;   // how far the physics world's count had got when the mesh was last built

    public int Count => _count;

    /// <summary>
    /// Adds a hole centred on <paramref name="point"/>, lying in the plane of <paramref name="normal"/>, with its x axis
    /// along <paramref name="tangent"/> (the grain of wood, the direction a ricochet gouge runs).
    /// </summary>
    public void Add(float3 point, float3 normal, float3 tangent, float halfX, float halfY, HoleKind kind)
    {
        tangent -= normal * float3.Dot(normal, tangent);
        var length = float.Sqrt(float3.Dot(tangent, tangent));
        if (length < 1e-4f)
            return;
        _seed = (_seed * 1103515245 + 12345) & 0x7fffffff;
        var position = point + normal * 0.0015f;
        tangent /= length;
        var door = physics?.DoorPartAt(point, normal);
        if (door is not null)
        {
            float4x4.Invert(PhysicsWorld.FromBullet(door.WorldTransform), out var toDoor);
            position = float3.Transform(position, toDoor);
            normal = float3.TransformNormal(normal, toDoor);
            tangent = float3.TransformNormal(tangent, toDoor);
        }

        _holes[_next] = new Hole
        {
            Position = position,
            Normal = normal,
            Tangent = tangent,
            HalfX = halfX,
            HalfY = halfY,
            Kind = kind,
            Seed = _seed,
            Door = door,
        };
        _next = (_next + 1) % Capacity;
        _count = int.Min(_count + 1, Capacity);
        _dirty = true;
    }

    public void Clear()
    {
        _count = 0;
        _next = 0;
        _dirty = true;
    }

    /// <summary>Takes away the holes at the points that match (the rest keep their order); returns how many went.</summary>
    public int RemoveWhere(Predicate<float3> match)
    {
        var all = (Hole[])_holes.Clone();
        var start = _count < Capacity ? 0 : _next;
        var before = _count;
        _count = 0;
        for (var i = 0; i < before; i++)
        {
            var h = all[(start + i) % Capacity];
            if (!match(h.InWorld().Position))
                _holes[_count++] = h;
        }

        _next = _count % Capacity;
        _dirty = true;
        return before - _count;
    }

    public void Draw(Renderer renderer)
    {
        if (_dirty || (physics is not null && physics.DoorMoves != _doorMoves))
            Rebuild();
        renderer.DrawDecals(_mesh);
    }

    private void Rebuild()
    {
        _dirty = false;
        _doorMoves = physics?.DoorMoves ?? 0;
        _mesh.Clear();
        // Oldest first: when full, the oldest is at _next.
        var start = _count < Capacity ? 0 : _next;
        for (var i = 0; i < _count; i++)
        {
            var h = _holes[(start + i) % Capacity].InWorld();
            var opacity = _count < Capacity ? 1f : float.Min(1f, (i + 1f) / FadingTail);
            var b = float3.Cross(h.Normal, h.Tangent);
            var x = h.Tangent * h.HalfX;
            var y = b * h.HalfY;
            // The seed picks a cell of the uv plane (multiples of 4); the shader recovers it.
            var cell = new float2(4 * (h.Seed % 61), 4 * ((h.Seed / 61) % 61));
            var tangent = new float4(h.Tangent, (int)h.Kind + 0.05f + 0.9f * opacity);
            void V(float sx, float sy) => _mesh.Vertex(h.Position + x * sx + y * sy, h.Normal, cell + new float2(sx, sy), tangent);
            V(-1, -1); V(1, -1); V(1, 1);
            V(-1, -1); V(1, 1); V(-1, 1);
        }
    }
}
