using Brutal.Numerics;
using BrutalFps.Engine;
using BulletSharp;

namespace BrutalFps.Game;

/// <summary>
/// Small things that fly off and lie about, simulated by Bullet: chips of concrete and stone, wood splinters, glass
/// shards, spent cases and bullets of each calibre, dropped magazines and squashed bullets. Every kind has a cap; spawning past it removes the
/// oldest of that kind, and loose bits fade away after a while, so the world never fills up with bodies.
/// Without the physics world (no native Bullet library) nothing is spawned.
/// </summary>
public sealed class DebrisSystem
{
    public enum Kind { Chunk, Splinter, Shard, Casing, Magazine, Slug, Bullet }

    // How many of each may exist at once, and how long they stay (seconds).
    private static readonly Dictionary<Kind, (int Cap, float Life)> Limits = new()
    {
        [Kind.Chunk] = (70, 14f),
        [Kind.Splinter] = (40, 18f),
        [Kind.Shard] = (40, 16f),
        [Kind.Casing] = (40, 45f),
        [Kind.Magazine] = (6, 90f),
        [Kind.Slug] = (30, 45f),
        [Kind.Bullet] = (40, 45f),
    };

    /// <summary>A calibre's models and the convex hulls its bodies collide with (no hulls without the physics world).</summary>
    private sealed record RoundPieces(ModelPart[] Bullet, ModelPart[] Case, ModelPart[] Spent,
        CollisionShape? BulletShape, CollisionShape? CaseShape, CollisionShape? SpentShape);

    private sealed class Piece
    {
        public RigidBody? Body;
        public Kind Kind;
        public Mesh? Mesh;
        public IReadOnlyList<ModelPart>? Parts;
        public bool ShowRound;
        public float4x4 RenderOffset = float4x4.Identity;   // mesh space -> body space
        public float3 Tint;
        public float2 RoughMetal;
        public float Age;
        public float Life;
        // A squashed bullet first flattens against the water for a moment, then drops.
        public float SquishLeft;
        public float4x4 SquishPose;
        public float3 SquishNormal;
        public float3 PendingVelocity;
    }

    private readonly PhysicsWorld? _physics;
    private readonly SurfaceTable _surfaces;
    private readonly List<Piece> _pieces = [];
    private readonly Random _random = new(99);

    // Shapes and meshes, shared by every piece of a kind.
    private readonly Mesh[] _chunkMeshes;
    private readonly ConvexHullShape?[,] _chunkShapes;
    private static readonly float[] ChunkSizes = [0.006f, 0.009f, 0.013f, 0.018f];
    private readonly Mesh _boxMesh;
    private readonly Dictionary<float3, BoxShape> _boxShapes = [];
    private readonly Mesh _casingMesh;
    private readonly CollisionShape? _casingShape;
    private readonly Mesh _slugMesh;
    private readonly CollisionShape? _slugShape;
    private readonly Dictionary<float3, BoxShape> _magazineShapes = [];   // by half extents (each gun's magazine)
    private readonly Dictionary<Cartridge, RoundPieces> _rounds = [];
    private readonly int _brassSurface, _steelCaseSurface, _jacketSurface, _magazineSurface, _stoneSurface, _woodSurface, _glassSurface;

    private static readonly float3 BrassTint = new(0.78f, 0.58f, 0.24f);
    private static readonly float3 SteelCaseTint = new(0.31f, 0.27f, 0.17f);   // lacquered
    private static readonly float3 SlugTint = new(0.86f, 0.6f, 0.42f);   // copper jacket peeled back over grey lead

    public int Count => _pieces.Count;

    public DebrisSystem(PhysicsWorld? physics, SurfaceTable surfaces)
    {
        _physics = physics;
        _surfaces = surfaces;
        _brassSurface = surfaces.Index(new SurfaceInfo(SurfaceKind.Metal, BrassTint, Material.Plain));
        _steelCaseSurface = surfaces.Index(new SurfaceInfo(SurfaceKind.Metal, SteelCaseTint, Material.Plain));
        _jacketSurface = surfaces.Index(new SurfaceInfo(SurfaceKind.Metal, SlugTint, Material.Plain));
        _magazineSurface = surfaces.Index(new SurfaceInfo(SurfaceKind.Plastic, new float3(0.15f), Material.Polymer));
        _stoneSurface = surfaces.Index(new SurfaceInfo(SurfaceKind.Concrete, new float3(0.5f), Material.Concrete));
        _woodSurface = surfaces.Index(new SurfaceInfo(SurfaceKind.Wood, new float3(0.6f, 0.46f, 0.3f), Material.Wood));
        _glassSurface = surfaces.Index(new SurfaceInfo(SurfaceKind.Glass, new float3(0.8f), Material.Glass));

        // Chunks: lumpy low-poly stones (unit size), and a convex hull of each at a few sizes.
        var random = new Random(17);
        _chunkMeshes = new Mesh[5];
        _chunkShapes = new ConvexHullShape?[_chunkMeshes.Length, ChunkSizes.Length];
        for (var v = 0; v < _chunkMeshes.Length; v++)
        {
            var radii = new float3(1f, 0.55f + 0.35f * (float)random.NextDouble(), 0.7f + 0.3f * (float)random.NextDouble());
            _chunkMeshes[v] = new MeshBuilder().Blob(float3.Zero, radii, random, 5, 3).Build();
            if (physics is null)
                continue;
            for (var s = 0; s < ChunkSizes.Length; s++)
            {
                var p = _chunkMeshes[v].Positions;
                var points = new List<BulletSharp.Math.Vector3>(p.Length / 3);
                for (var i = 0; i < p.Length; i += 3)
                    points.Add(new BulletSharp.Math.Vector3(p[i] * ChunkSizes[s], p[i + 1] * ChunkSizes[s], p[i + 2] * ChunkSizes[s]));
                _chunkShapes[v, s] = new ConvexHullShape(points) { Margin = 0.0008 };
            }
        }

        _boxMesh = new MeshBuilder().Box(float3.Zero, float3.One).Build();
        // A 9 mm case: 19 mm long, 9.9 mm across the rim.
        _casingMesh = new MeshBuilder().Cylinder(0.0048f, -0.0095f, 0.0095f, 12).Cylinder(0.0050f, -0.0095f, -0.0082f, 12).Build();
        // A squashed bullet: a flattened, lumpy disc (its jacket peeled back round the lead).
        // (A little bigger than life - about 2 cm across - so you can find them on the floor.)
        _slugMesh = new MeshBuilder().Blob(float3.Zero, new float3(0.0105f, 0.0035f, 0.0105f), new Random(5), 10, 4).Build();
        if (physics is not null)
        {
            _casingShape = new CylinderShapeZ(0.0049, 0.0049, 0.0095) { Margin = 0.0006 };
            _slugShape = new CylinderShape(0.0105, 0.003, 0.0105) { Margin = 0.0006 };
        }
    }

    // ------------------------------------------------------------------------------------------------ spawning

    // Level of detail. Chips are a centimetre or two across: past ChipsRange (times the scope's power) they're under a pixel, so
    // they aren't made at all, and past ChipsFullRange only half of them. A firefight also gets ChipsPerFrame at most a frame
    // (a hundred rounds striking at once would otherwise make three hundred bodies, each one costing every physics step).
    private const float ChipsFullRange = 12f, ChipsRange = 25f;
    private const int ChipsPerFrame = 12;
    private int _chipsLeft = ChipsPerFrame;

    /// <summary>How many of <paramref name="count"/> chips to make at <paramref name="point"/>, by how far off it is and this frame's budget.</summary>
    private int Thin(float3 point, int count)
    {
        var d2 = Relevance.DistanceSquared(point, Level.ZoneAt(point)) / (Relevance.Zoom * Relevance.Zoom);
        if (d2 > ChipsRange * ChipsRange)
            return 0;
        if (d2 > ChipsFullRange * ChipsFullRange)
            count = (count + 1) / 2;
        count = int.Min(count, _chipsLeft);
        _chipsLeft -= count;
        return count;
    }

    /// <summary>Whether something as small as a spent bullet at <paramref name="point"/> could be seen at all (see <see cref="Thin"/>).</summary>
    private static bool Worth(float3 point) =>
        Relevance.DistanceSquared(point, Level.ZoneAt(point)) / (Relevance.Zoom * Relevance.Zoom) <= ChipsRange * ChipsRange;

    /// <summary>Chips of stone or concrete knocked out of a surface.</summary>
    public void Chunks(float3 point, float3 normal, float3 shotDirection, float3 tint, int count, float sizeScale = 1f)
    {
        if (_physics is null)
            return;
        count = Thin(point, count);
        for (var i = 0; i < count; i++)
        {
            var sizeIndex = int.Clamp((int)(_random.NextDouble() * ChunkSizes.Length * sizeScale), 0, ChunkSizes.Length - 1);
            var variant = _random.Next(_chunkMeshes.Length);
            var size = ChunkSizes[sizeIndex];
            var velocity = Ejecta(normal, shotDirection, 1.5f, 4.5f);
            var piece = Take(Kind.Chunk);
            piece.Mesh = _chunkMeshes[variant];
            piece.RenderOffset = float4x4.CreateScale(size);
            piece.Tint = tint * (0.8f + 0.35f * (float)_random.NextDouble());
            piece.RoughMetal = new float2(0.95f, 0f);
            var mass = 2400f * 4f * size * size * size;   // a stone of about this size
            Spawn(piece, _chunkShapes[variant, sizeIndex]!, mass, RandomPose(point + normal * (size + 0.004f)), velocity, Spin(40f), 0.35f, 0.8f, size, _stoneSurface);
        }
    }

    /// <summary>Wood splinters.</summary>
    public void Splinters(float3 point, float3 normal, float3 shotDirection, float3 tint, int count)
    {
        if (_physics is null)
            return;
        count = Thin(point, count);
        for (var i = 0; i < count; i++)
        {
            var length = 0.018f + 0.035f * (float)_random.NextDouble();
            var size = new float3(length, 0.0025f + 0.002f * (float)_random.NextDouble(), 0.004f + 0.004f * (float)_random.NextDouble());
            var piece = Take(Kind.Splinter);
            piece.Mesh = _boxMesh;
            piece.RenderOffset = float4x4.CreateScale(size);
            piece.Tint = tint * (0.9f + 0.25f * (float)_random.NextDouble());
            piece.RoughMetal = new float2(0.9f, 0f);
            var mass = 500f * size.X * size.Y * size.Z;
            Spawn(piece, Box(size), mass, RandomPose(point + normal * 0.02f), Ejecta(normal, shotDirection, 1.5f, 4f), Spin(30f), 0.3f, 0.7f, size.Y, _woodSurface);
        }
    }

    /// <summary>Glass shards.</summary>
    public void Shards(float3 point, float3 normal, float3 shotDirection, int count)
    {
        if (_physics is null)
            return;
        count = Thin(point, count);
        for (var i = 0; i < count; i++)
        {
            var size = new float3(0.006f + 0.012f * (float)_random.NextDouble(), 0.0015f, 0.005f + 0.01f * (float)_random.NextDouble());
            var piece = Take(Kind.Shard);
            piece.Mesh = _boxMesh;
            piece.RenderOffset = float4x4.CreateScale(size);
            piece.Tint = new float3(0.78f, 0.84f, 0.86f);
            piece.RoughMetal = new float2(0.06f, 0f);
            var mass = 2500f * size.X * size.Y * size.Z;
            // Glass falls out of the hole as much as it flies back out.
            Spawn(piece, Box(size), mass, RandomPose(point + normal * 0.01f), Ejecta(normal, shotDirection, 0.5f, 2.5f), Spin(25f), 0.3f, 0.5f, size.Y, _glassSurface);
        }
    }

    /// <summary>
    /// Loads a calibre's model (see <see cref="Cartridge.Model"/>). Throws if it can't be read: its cases then fall back
    /// to the plain 9 mm one and its bullets simply aren't seen.
    /// </summary>
    public void LoadRound(Cartridge round)
    {
        var model = Model.Get(round.Model);
        ModelPart[] Parts(string name) => model.Parts.Where(p => p.Name == name).ToArray();
        _rounds[round] = new RoundPieces(Parts("Bullet"), Parts("Case"), Parts("Spent"), Hull(Parts("BulletHull")), Hull(Parts("CaseHull")), Hull(Parts("SpentHull")));
    }

    /// <summary>A Bullet convex hull of a low-poly outline. Its origin is the piece's centre of mass, as it is on the model.</summary>
    private ConvexHullShape? Hull(ModelPart[] parts)
    {
        if (_physics is null)
            return null;
        var seen = new HashSet<float3>();
        var points = new List<BulletSharp.Math.Vector3>();
        foreach (var part in parts)
        {
            var p = part.Mesh.Positions;
            for (var i = 0; i < p.Length; i += 3)
            {
                if (seen.Add(new float3(p[i], p[i + 1], p[i + 2])))
                    points.Add(new BulletSharp.Math.Vector3(p[i], p[i + 1], p[i + 2]));
            }
        }

        return points.Count >= 4 ? new ConvexHullShape(points) { Margin = 0.0004 } : null;
    }

    /// <summary>What a bullet of this calibre looks like in flight (nothing if its model didn't load).</summary>
    public IReadOnlyList<ModelPart> BulletParts(Cartridge round) => _rounds.TryGetValue(round, out var pieces) ? pieces.Bullet : [];

    /// <summary>A bullet that stayed in one piece on something hard: mushroomed, it bounces back off the surface and drops.</summary>
    public void SpentBullet(Cartridge round, float3 point, float3 normal, float3 shotDirection)
    {
        if (_physics is null || !_rounds.TryGetValue(round, out var pieces) || pieces.SpentShape is null)
            return;
        if (!Worth(point))
            return;
        var piece = Take(Kind.Bullet);
        piece.Parts = pieces.Spent;
        var pose = RandomPose(point + normal * round.BulletDiameter);
        Spawn(piece, pieces.SpentShape, round.BulletMass * 0.9f, pose, Ejecta(normal, shotDirection, 0.6f, 3f), Spin(80f), 0.3f, 0.6f, round.BulletDiameter, _jacketSurface);
    }

    /// <summary>A ricochet that has run out of speed: from here on it's a tumbling body, nose first where it was going.</summary>
    public void LooseBullet(Cartridge round, in float4x4 pose, float3 velocity)
    {
        if (_physics is null || !_rounds.TryGetValue(round, out var pieces) || pieces.BulletShape is null)
            return;
        var piece = Take(Kind.Bullet);
        piece.Parts = pieces.Bullet;
        Spawn(piece, pieces.BulletShape, round.BulletMass, pose, velocity, Spin(200f), 0.3f, 0.6f, round.BulletDiameter, _jacketSurface);
    }

    /// <summary>A spent case thrown out of the ejection port: the calibre's own, or a plain 9 mm one (no <paramref name="round"/>).</summary>
    public void Casing(in float4x4 transform, float3 velocity, float3 spin, Cartridge? round = null)
    {
        if (_physics is null || _casingShape is null)
            return;
        if (round is not null && _rounds.TryGetValue(round, out var pieces) && pieces.CaseShape is not null)
        {
            var surface = round.SteelCase ? _steelCaseSurface : _brassSurface;
            // Steel doesn't ring off the floor as lively as brass does.
            var cased = Take(Kind.Casing);
            cased.Parts = pieces.Case;
            Spawn(cased, pieces.CaseShape, round.CaseMass, transform, velocity, spin,
                round.SteelCase ? 0.38f : 0.45f, 0.5f, round.BulletDiameter, surface);
            return;
        }

        var piece = Take(Kind.Casing);
        piece.Mesh = _casingMesh;
        piece.Tint = BrassTint;
        piece.RoughMetal = new float2(0.3f, 0.6f);
        Spawn(piece, _casingShape, 0.004f, transform, velocity, spin, 0.45f, 0.5f, 0.0098f, _brassSurface);
    }

    /// <summary>
    /// A magazine dropped out of the gun. <paramref name="bodyPose"/> is the body's frame (its box: depth x length x
    /// width), <paramref name="renderOffset"/> takes the model's parts into it.
    /// </summary>
    public void Magazine(IReadOnlyList<ModelPart> parts, in float4x4 renderOffset, in float4x4 bodyPose, float3 halfExtents, int rounds,
        float3 velocity, float3 spin)
    {
        if (_physics is null || parts.Count == 0)
            return;
        if (!_magazineShapes.TryGetValue(halfExtents, out var shape))
            _magazineShapes[halfExtents] = shape = new BoxShape(halfExtents.X, halfExtents.Y, halfExtents.Z) { Margin = 0.001 };
        var piece = Take(Kind.Magazine);
        piece.Parts = parts;
        piece.ShowRound = rounds > 0;
        piece.RenderOffset = renderOffset;
        Spawn(piece, shape, 0.075f + 0.012f * rounds, bodyPose, velocity, spin, 0.25f, 0.6f, halfExtents.Z * 2, _magazineSurface);
    }

    /// <summary>
    /// A bullet stopped dead by a portal's water: it flattens against the surface where it hit, then drops.
    /// </summary>
    public void Slug(float3 point, float3 surfaceNormal, float3 shotDirection)
    {
        if (_physics is null || _slugShape is null)
            return;
        // Lying flat against the water (its disc facing out of the portal), slightly turned.
        var up = surfaceNormal;
        var side = float3.Normalize(float3.Cross(up, float.Abs(up.Y) < 0.9f ? float3.UnitY : float3.UnitX));
        var forward = float3.Cross(side, up);
        var pose = float4x4.CreateRotationY((float)_random.NextDouble() * float.Tau)
            * new float4x4(side.X, side.Y, side.Z, 0, up.X, up.Y, up.Z, 0, forward.X, forward.Y, forward.Z, 0, 0, 0, 0, 1)
            * float4x4.CreateTranslation(point + surfaceNormal * 0.004f);
        var piece = Take(Kind.Slug);
        piece.Mesh = _slugMesh;
        piece.Tint = SlugTint;
        piece.RoughMetal = new float2(0.3f, 0.55f);   // (half metallic, so it still shows in dim interiors)
        piece.SquishLeft = 0.07f;
        piece.SquishPose = pose;
        piece.SquishNormal = surfaceNormal;
        // It peels off the surface and drops; a little sideways scatter from the splash.
        piece.PendingVelocity = surfaceNormal * (0.25f + 0.35f * (float)_random.NextDouble())
            + float3.Normalize(shotDirection - surfaceNormal * float3.Dot(shotDirection, surfaceNormal) + new float3(0.001f)) * 0.15f;
        Limit(Kind.Slug);
        piece.Life = Limits[Kind.Slug].Life;
        _pieces.Add(piece);
    }

    private void Spawn(Piece piece, CollisionShape shape, float mass, in float4x4 pose, float3 velocity, float3 spin,
        float restitution, float friction, float size, int surface)
    {
        Limit(piece.Kind);
        piece.Life = Limits[piece.Kind].Life * (0.85f + 0.3f * (float)_random.NextDouble());
        piece.Body = _physics!.AddBody(shape, mass, pose, velocity, spin, restitution, friction, size, surface);
        _pieces.Add(piece);
    }

    /// <summary>Makes room for one more of <paramref name="kind"/>: removes the oldest if it's at its cap.</summary>
    private void Limit(Kind kind)
    {
        var cap = Limits[kind].Cap;
        var count = 0;
        Piece? oldest = null;
        foreach (var p in _pieces)
        {
            if (p.Kind != kind)
                continue;
            count++;
            if (oldest is null || p.Age > oldest.Age)
                oldest = p;
        }

        if (count >= cap && oldest is not null)
            Remove(oldest);
    }

    private void Remove(Piece piece)
    {
        if (piece.Body is not null)
            _physics?.ReleaseBody(piece.Body);   // (back to the pool, for the next piece)
        piece.Body = null;
        _pieces.Remove(piece);
        _spare.Push(piece);
    }

    // Pieces taken out, kept to be handed out again (object pooling: no garbage per chip in a firefight).
    private readonly Stack<Piece> _spare = new();

    /// <summary>A piece to fill in: a spare one, wiped, or a new one.</summary>
    private Piece Take(Kind kind)
    {
        if (!_spare.TryPop(out var p))
            return new Piece { Kind = kind };
        p.Body = null;
        p.Kind = kind;
        p.Mesh = null;
        p.Parts = null;
        p.ShowRound = false;
        p.RenderOffset = float4x4.Identity;
        p.Tint = default;
        p.RoughMetal = default;
        p.Age = 0;
        p.Life = 0;
        p.SquishLeft = 0;
        p.SquishPose = default;
        p.SquishNormal = default;
        p.PendingVelocity = default;
        return p;
    }

    private BoxShape Box(float3 size)
    {
        var key = new float3(MathF.Round(size.X, 3), MathF.Round(size.Y, 4), MathF.Round(size.Z, 3));
        if (!_boxShapes.TryGetValue(key, out var shape))
        {
            shape = new BoxShape(key.X / 2, key.Y / 2, key.Z / 2) { Margin = float.Min(0.0008f, key.Y * 0.3f) };
            _boxShapes[key] = shape;
        }

        return shape;
    }

    private float3 Ejecta(float3 normal, float3 shotDirection, float minSpeed, float maxSpeed)
    {
        // Mostly back out of the surface, a little along the reflected shot, scattered.
        var reflected = shotDirection - normal * (2f * float3.Dot(shotDirection, normal));
        var dir = float3.Normalize(normal * 1.2f + reflected * 0.5f + Particles.RandomDirection() * 0.8f);
        if (float3.Dot(dir, normal) < 0.1f)
            dir = float3.Normalize(dir + normal * 0.6f);
        return dir * (minSpeed + (float)_random.NextDouble() * (maxSpeed - minSpeed));
    }

    private float3 Spin(float max) => Particles.RandomDirection() * ((float)_random.NextDouble() * max);

    private float4x4 RandomPose(float3 position) =>
        float4x4.CreateFromYawPitchRoll((float)_random.NextDouble() * float.Tau, (float)_random.NextDouble() * float.Tau, (float)_random.NextDouble() * float.Tau)
        * float4x4.CreateTranslation(position);

    // ------------------------------------------------------------------------------------------------ simulation

    /// <summary>A blast at <paramref name="at"/>: every loose piece within <paramref name="reach"/> m flung away from it, harder the nearer it is (up to <paramref name="speed"/> m/s).</summary>
    public void Blast(float3 at, float reach, float speed)
    {
        foreach (var p in _pieces)
        {
            if (p.Body is null || p.Body.InvMass <= 0)
                continue;
            var position = PhysicsWorld.TransformOf(p.Body).W.XYZ;
            var away = position - at;
            var dist = float.Sqrt(float3.Dot(away, away));
            if (dist >= reach)
                continue;
            var direction = dist > 1e-3f ? away / dist : float3.UnitY;
            direction = float3.Normalize(direction + new float3(0, 0.4f, 0));
            var kick = speed * (1f - dist / reach);
            PhysicsWorld.Push(p.Body, direction * (kick / (float)p.Body.InvMass), position);
        }
    }

    public void Update(float dt)
    {
        using var timer = Perf.DebrisUpdate.Time();
        _chipsLeft = ChipsPerFrame;
        for (var i = _pieces.Count - 1; i >= 0; i--)
        {
            var p = _pieces[i];
            p.Age += dt;
            if (p.SquishLeft > 0)
            {
                p.SquishLeft -= dt;
                if (p.SquishLeft <= 0 && _physics is not null && _slugShape is not null)
                    p.Body = _physics.AddBody(_slugShape, 0.008f, p.SquishPose, p.PendingVelocity, Spin(6f), 0.15f, 0.6f, 0.004f, _brassSurface);
                continue;
            }

            if (p.Body is null)
                continue;
            var position = PhysicsWorld.TransformOf(p.Body).W.XYZ;
            var sunk = Level.ZoneAt(position) == Zones.Ship && position.Y < Level.SeaLevel - 0.5f;
            if (p.Age >= p.Life || position.Y < Level.KillHeightAt(position) || sunk)
                Remove(p);
        }
    }

    // ------------------------------------------------------------------------------------------------ drawing

    public void Draw(Renderer renderer, Zones zones)
    {
        using var timer = Perf.DebrisDraw.Time();
        foreach (var p in _pieces)
        {
            float4x4 world;
            var scale = 1f;
            if (p.SquishLeft > 0)
            {
                // Flattening: from a bullet's length to a disc in a few hundredths of a second.
                var t = 1f - p.SquishLeft / 0.07f;
                world = float4x4.CreateScale(float.Lerp(0.55f, 1f, t), float.Lerp(2.6f, 1f, t), float.Lerp(0.55f, 1f, t)) * p.SquishPose;
            }
            else if (p.Body is not null)
            {
                world = PhysicsWorld.TransformOf(p.Body);
                // The last second: shrink away.
                scale = float.Clamp(p.Life - p.Age, 0f, 1f);
            }
            else
            {
                continue;
            }

            var position = world.W.XYZ;
            if ((zones & Level.ZoneAt(position)) == 0)
                continue;
            // Shrink about the body's centre (after the model->body offset), so it doesn't slide off as it shrinks away.
            var model = p.RenderOffset * (scale < 1f ? float4x4.CreateScale(scale) : float4x4.Identity) * world;
            if (p.Parts is { } parts)
            {
                foreach (var part in parts)
                {
                    if (part.Name == "MagazineRound" && !p.ShowRound)
                        continue;
                    renderer.DrawPart(part, model, float3.One);
                }
            }
            else if (p.Mesh is { } mesh)
            {
                renderer.DrawMesh(mesh, model, p.Tint, ShadeMode.Lit, Material.Plain, p.RoughMetal);
            }
        }
    }

    public void Clear()
    {
        foreach (var p in _pieces.ToArray())
            Remove(p);
    }
}
