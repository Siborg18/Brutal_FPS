using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <param name="Offset">Where on the board, from its centre (metres: x to the shooter's right, y up).</param>
public readonly record struct TargetHit(Target Target, string Zone, int Points, float3 WorldPoint, float2 Offset)
{
    // How the bullet got there (the target doesn't know: see ProjectileSystem).
    public Cartridge? Round { get; init; }
    public float Range { get; init; }    // metres from where it was fired
    public float Speed { get; init; }    // m/s
    public float Energy { get; init; }   // joules
    public float Flight { get; init; }   // seconds
}

/// <summary>
/// A square shooting target hanging from a frame. The board swings when hit (unless it's fixed, so its bullet holes stay
/// where the shots landed), flashes, and scores the hit by ring (bullseye / inner / middle / outer).
/// </summary>
public sealed class Target
{
    public const float BoardSize = 0.5f;   // (about as wide as a man's body)
    public const float BoardThickness = 0.12f;
    private const float FrameGap = 0.15f;  // space between the crossbar and the top of the board
    private const float PostSize = 0.15f;

    // Rings, outermost first: (half-extent as a fraction of the board, colour, zone name, points)
    internal static readonly (float Extent, float3 Color, string Zone, int Points)[] Rings =
    [
        (1.00f, new float3(0.95f, 0.95f, 0.92f), "Outer", 5),
        (0.75f, new float3(0.85f, 0.15f, 0.12f), "Middle", 10),
        (0.48f, new float3(0.95f, 0.95f, 0.92f), "Inner", 25),
        (0.22f, new float3(0.85f, 0.15f, 0.12f), "BULLSEYE", 50),
    ];

    private static readonly float3 FlashColor = new(1.0f, 0.85f, 0.2f);

    private readonly float3 _pivot; // top-centre of the board; the board swings about the X axis through here
    private readonly bool _swings;

    private float _swingAngle;
    private float _swingVelocity;
    private float _flash;

    public string Name { get; }
    public int Hits { get; private set; }
    public int Score { get; private set; }

    private readonly List<float2> _hitMarks = [];

    /// <summary>Where the hits since the last reset landed, oldest first (metres from the centre: x to the shooter's right, y up).</summary>
    public IReadOnlyList<float2> HitMarks => _hitMarks;

    /// <param name="name">Label used in log messages.</param>
    /// <param name="groundPosition">Point on the ground directly below the board centre.</param>
    /// <param name="boardCenterHeight">Height of the board centre above the ground.</param>
    /// <param name="level">The frame (two posts + crossbar) is added to the level as solid geometry.</param>
    /// <param name="swings">False: the board stays still when hit.</param>
    public Target(string name, float3 groundPosition, float boardCenterHeight, Level level, bool swings = true)
    {
        Name = name;
        _swings = swings;
        _pivot = groundPosition + new float3(0, boardCenterHeight + BoardSize / 2, 0);

        var barY = _pivot.Y + FrameGap;
        var postX = BoardSize / 2 + 0.35f;
        var postHeight = barY + PostSize / 2 - groundPosition.Y;

        level.AddStatic(Aabb.FromCenterSize(
            groundPosition + new float3(-postX, postHeight / 2, 0), new float3(PostSize, postHeight, PostSize)), Level.StandColor, Material.Steel);
        level.AddStatic(Aabb.FromCenterSize(
            groundPosition + new float3(postX, postHeight / 2, 0), new float3(PostSize, postHeight, PostSize)), Level.StandColor, Material.Steel);
        level.AddStatic(Aabb.FromCenterSize(
            new float3(_pivot.X, barY, _pivot.Z), new float3(postX * 2 + PostSize, PostSize, PostSize)), Level.StandColor, Material.Steel);
    }

    /// <summary>Board-local space -> world. Board local origin is its centre; it faces +Z.</summary>
    private float4x4 BoardToWorld =>
        float4x4.CreateTranslation(0, -BoardSize / 2, 0)
        * float4x4.CreateRotationX(_swingAngle)
        * float4x4.CreateTranslation(_pivot);

    private static Aabb LocalBounds => Aabb.FromCenterSize(float3.Zero, new float3(BoardSize, BoardSize, BoardThickness));

    /// <summary>The middle of the board, in the world.</summary>
    public float3 Center => BoardToWorld.Translation;

    /// <summary>The way the board's face looks (+Z, as it hangs now).</summary>
    public float3 Normal => float3.TransformNormal(float3.UnitZ, BoardToWorld);

    /// <summary>Whether a point in the world is on the board (a bullet hole lying on its face, say).</summary>
    public bool Covers(float3 point)
    {
        float4x4.Invert(BoardToWorld, out var worldToBoard);
        var local = float3.Transform(point, worldToBoard);
        return float.Abs(local.X) <= BoardSize / 2 + 0.05f && float.Abs(local.Y) <= BoardSize / 2 + 0.05f && float.Abs(local.Z) <= BoardThickness / 2 + 0.05f;
    }

    /// <summary>As new: no hits, no score, hanging still. (Its bullet holes aren't its own: see <see cref="BulletHoles.RemoveWhere"/>.)</summary>
    public void Reset()
    {
        Hits = 0;
        Score = 0;
        _hitMarks.Clear();
        _swingAngle = 0f;
        _swingVelocity = 0f;
        _flash = 0f;
    }

    /// <summary>
    /// Sweeps a sphere along a ray against the (possibly swinging) board.
    /// Done in board-local space so the hit point maps straight onto the rings.
    /// </summary>
    public bool Raycast(float3 origin, float3 direction, float maxDistance, float radius, out float distance, out float3 localPoint)
    {
        localPoint = default;
        float4x4.Invert(BoardToWorld, out var worldToBoard);
        var localOrigin = float3.Transform(origin, worldToBoard);
        var localDirection = float3.TransformNormal(direction, worldToBoard); // rigid transform: stays unit length

        if (!LocalBounds.Expand(radius).Raycast(localOrigin, localDirection, maxDistance, out distance, out _))
            return false;

        localPoint = localOrigin + localDirection * distance;
        return true;
    }

    public TargetHit RegisterHit(float3 localPoint, float3 incomingDirection)
    {
        // Square rings, so score by the larger of |x| and |y| (Chebyshev distance from centre).
        var extent = float.Max(float.Abs(localPoint.X), float.Abs(localPoint.Y)) / (BoardSize / 2);

        var zone = Rings[0];
        foreach (var ring in Rings)
        {
            if (extent <= ring.Extent)
                zone = ring;
        }

        Hits++;
        Score += zone.Points;
        _hitMarks.Add(new float2(localPoint.X, localPoint.Y));
        _flash = 1f;

        // Push the board away from the shot: a hit travelling towards -Z swings the bottom back (+angle).
        float4x4.Invert(BoardToWorld, out var worldToBoard);
        var push = -float3.TransformNormal(incomingDirection, worldToBoard).Z;
        if (_swings)
            _swingVelocity += 5f * float.Clamp(push, -1f, 1f);

        var world = float3.Transform(localPoint, BoardToWorld);
        Log.Write($"[HIT] {Name}: {zone.Zone} (+{zone.Points})  |  hits on this target: {Hits}, score: {Score}");
        return new TargetHit(this, zone.Zone, zone.Points, world, new float2(localPoint.X, localPoint.Y));
    }

    public void Update(float dt)
    {
        // Damped spring back to rest.
        const float stiffness = 40f;
        const float damping = 3.5f;
        _swingVelocity += (-stiffness * _swingAngle - damping * _swingVelocity) * dt;
        _swingAngle = float.Clamp(_swingAngle + _swingVelocity * dt, -1.2f, 1.2f);

        _flash = float.Max(0, _flash - dt * 5f);
    }

    public void Draw(Renderer renderer)
    {
        var boardToWorld = BoardToWorld;

        // Each ring is four bars round the next one in, as thick as the board: nothing overlaps, so nothing flickers from
        // a long way off (layers a few millimetres apart would, with the depth buffer as coarse as it is out there).
        for (var i = 0; i < Rings.Length; i++)
        {
            var (extent, color, _, _) = Rings[i];
            var outer = BoardSize * extent / 2;
            var inner = i + 1 < Rings.Length ? BoardSize * Rings[i + 1].Extent / 2 : 0f;
            var side = outer - inner;
            void Slab(float x, float y, float width, float height) => renderer.DrawCube(
                float4x4.CreateScale(width, height, BoardThickness) * float4x4.CreateTranslation(x, y, 0) * boardToWorld,
                Tint(color), Shade);

            if (inner <= 0f)
            {
                Slab(0, 0, outer * 2, outer * 2);
                continue;
            }
            Slab(0, (outer + inner) / 2, outer * 2, side);
            Slab(0, -(outer + inner) / 2, outer * 2, side);
            Slab((outer + inner) / 2, 0, side, inner * 2);
            Slab(-(outer + inner) / 2, 0, side, inner * 2);
        }
    }

    private float3 Tint(float3 color) => color + (FlashColor - color) * _flash;

    // Glow (ignore lighting) for the first part of the flash so hits pop even on shaded faces.
    private ShadeMode Shade => _flash > 0.4f ? ShadeMode.Unlit : ShadeMode.Lit;
}
