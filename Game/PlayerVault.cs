using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>What the player's body runs into: the level's solid boxes (and its moving ones, the doors).</summary>
public interface ISolids
{
    /// <summary>Whether anything solid overlaps <paramref name="box"/>.</summary>
    bool Blocked(in Aabb box);

    /// <summary>The highest top of anything solid overlapping <paramref name="column"/> (-infinity: nothing).</summary>
    float HighestTop(in Aabb column);
}

/// <summary>The level's boxes, searched one by one (for the player's own queries: a handful a frame at most).</summary>
public sealed class LevelSolids(Level level) : ISolids
{
    public bool Blocked(in Aabb box)
    {
        foreach (var b in level.Boxes)
            if (b.Collides && box.Overlaps(b.Bounds))
                return true;
        foreach (var b in level.MovingBoxes)
            if (b.Collides && box.Overlaps(b.Bounds))
                return true;
        return false;
    }

    public float HighestTop(in Aabb column)
    {
        var top = float.NegativeInfinity;
        foreach (var b in level.Boxes)
            if (b.Collides && b.Bounds.Max.Y > top && column.Overlaps(b.Bounds))
                top = b.Bounds.Max.Y;
        foreach (var b in level.MovingBoxes)
            if (b.Collides && b.Bounds.Max.Y > top && column.Overlaps(b.Bounds))
                top = b.Bounds.Max.Y;
        return top;
    }
}

/// <summary>
/// A vault: from <see cref="From"/> up to <see cref="Top"/> and along to <see cref="To"/> (feet positions). <see cref="Onto"/>:
/// it ends standing on top of the obstacle (a climb up onto it) rather than down on the far side of it.
/// <see cref="Crouched"/>: there's only room to land crouched.
/// </summary>
public readonly record struct VaultPlan(float3 From, float Top, float3 To, bool Onto, bool Crouched, float3 Obstacle)
{
    /// <summary>How long it takes (s): longer for a higher or wider obstacle.</summary>
    public float Duration
    {
        get
        {
            var rise = Top - From.Y;
            var across = float3.Distance(new float3(From.X, 0, From.Z), new float3(To.X, 0, To.Z));
            return 0.28f + 0.32f * float.Clamp(rise / PlayerVault.MaxRise, 0f, 1f) + across * 0.12f;
        }
    }
}

/// <summary>
/// Vaulting (Space, facing a low obstacle): the player can't jump, but can get over (or, where the level allows it, up onto)
/// anything from ankle to chest height that has room above it. See <see cref="Plan"/>.
/// </summary>
public static class PlayerVault
{
    /// <summary>The lowest and highest obstacle (m above the feet) a vault gets you over.</summary>
    public const float MinRise = 0.05f, MaxRise = 1.25f;

    /// <summary>How far ahead of the body (m) the obstacle may be.</summary>
    public const float Reach = 0.6f;

    /// <summary>An obstacle deeper than this (m, along the vault) is climbed up onto rather than vaulted over.</summary>
    public const float MaxOverDepth = 1.0f;

    /// <summary>The furthest (m below where you started) the far side may drop away: further, and there's no vault.</summary>
    public const float MaxDrop = 1.3f;

    private const float Step = 0.05f;
    private const float R = PlayerController.Radius;

    /// <summary>
    /// The vault from <paramref name="feet"/> along <paramref name="dir"/> (horizontal unit vector), or null: nothing in reach,
    /// it's too high (a wall), there's no room over it, or the far side drops away. <paramref name="allowOnto"/> says
    /// whether the top of an obstacle (the box under the landing) may be climbed onto.
    /// </summary>
    public static VaultPlan? Plan(ISolids solids, float3 feet, float3 dir, Func<float3, bool>? allowOnto = null)
    {
        // 1. The obstacle: the first thing the body would run into going that way, and how high it is.
        var near = -1f;
        var top = 0f;
        for (var d = Step; d <= Reach + 1e-4f; d += Step)
        {
            var p = feet + dir * d;
            var column = new Aabb(new float3(p.X - R, feet.Y + 0.02f, p.Z - R), new float3(p.X + R, feet.Y + PlayerController.Height, p.Z + R));
            var hi = solids.HighestTop(column);
            if (float.IsNegativeInfinity(hi))
                continue;
            if (hi > feet.Y + MaxRise)
            {
                // Something too high in the way. (It may be a wall above a low sill: then it's no vault either.)
                return null;
            }
            near = d;
            top = hi;
            break;
        }
        if (near < 0 || top - feet.Y < MinRise)
            return null;

        // Room to start: the body, crouched, lifted to the top where it stands.
        if (solids.Blocked(Body(feet with { Y = top + 0.01f }, PlayerController.CrouchHeight)))
            return null;

        // 2. Along the top: crouched, it has to be clear all the way; past the far face, down to what's there.
        float3? onto = null;
        var ontoCrouched = false;
        var end = near + MaxOverDepth + 2 * R + Step;
        for (var s = near; s <= end + 1e-4f; s += Step)
        {
            var p = feet + dir * s;
            if (solids.Blocked(Body(p with { Y = top + 0.01f }, PlayerController.CrouchHeight)))
                break;   // no room to go on over (a wall, a ceiling): up onto it, if there's room there, or nothing

            var support = solids.HighestTop(new Aabb(
                new float3(p.X - R + 0.01f, feet.Y - MaxDrop - 0.5f, p.Z - R + 0.01f),
                new float3(p.X + R - 0.01f, top + 0.005f, p.Z + R - 0.01f)));

            if (support < top - 0.15f)
            {
                // Clear of the obstacle: the far side. Down onto whatever's there, if it isn't a long drop.
                if (float.IsNegativeInfinity(support) || support < feet.Y - MaxDrop)
                    return onto is { } o && (allowOnto?.Invoke(o) ?? true) ? new VaultPlan(feet, top, o, true, ontoCrouched, feet + dir * near) : null;
                var land = p with { Y = support + 0.0005f };
                if (!solids.Blocked(Body(land, PlayerController.Height)))
                    return new VaultPlan(feet, top, land, false, false, feet + dir * near);
                if (!solids.Blocked(Body(land, PlayerController.CrouchHeight)))
                    return new VaultPlan(feet, top, land, false, true, feet + dir * near);
                return null;
            }

            // Still over it: the first place the whole body stands on top is where a climb up onto it ends.
            if (onto is null && s >= near + 2 * R + 0.05f && support >= top - 0.01f && Footing(solids, p, top))
            {
                var stand = p with { Y = top + 0.0005f };
                if (!solids.Blocked(Body(stand, PlayerController.Height)))
                    onto = stand;
                else if (!solids.Blocked(Body(stand, PlayerController.CrouchHeight)))
                {
                    onto = stand;
                    ontoCrouched = true;
                }
            }
        }

        if (onto is { } up && (allowOnto?.Invoke(up) ?? true))
            return new VaultPlan(feet, top, up, true, ontoCrouched, feet + dir * near);
        return null;
    }

    /// <summary>
    /// Whether the top at <paramref name="top"/> gives real footing round <paramref name="p"/>: under the middle of the body and
    /// at least three of four points 15 cm round it (not the edge of a rail, the top of a post or a sliver of a ledge).
    /// </summary>
    private static bool Footing(ISolids solids, float3 p, float top)
    {
        bool Under(float dx, float dz) => solids.HighestTop(new Aabb(
            new float3(p.X + dx - 0.02f, top - 0.05f, p.Z + dz - 0.02f), new float3(p.X + dx + 0.02f, top + 0.005f, p.Z + dz + 0.02f))) >= top - 0.01f;
        if (!Under(0, 0))
            return false;
        var corners = (Under(0.15f, 0.15f) ? 1 : 0) + (Under(-0.15f, 0.15f) ? 1 : 0) + (Under(0.15f, -0.15f) ? 1 : 0) + (Under(-0.15f, -0.15f) ? 1 : 0);
        return corners >= 3;
    }

    private static Aabb Body(float3 feet, float height) =>
        new(feet - new float3(R, 0, R), feet + new float3(R, height, R));
}
