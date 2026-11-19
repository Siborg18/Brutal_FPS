using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Out of a doorway to reload. Someone whose magazine runs dry in a doorway, or just outside one, steps to the side of it,
/// against the wall on their own side (out of the enemy's sight if one side is), and reloads there. That leaves the opening
/// to the next of their side: to shoot through, or to go on in. Then they come back to it as usual. Without this, the first
/// one to get to a door (the jungle shack's) stood in it, firing and reloading, and the rest queued up behind them.
/// </summary>
public sealed partial class ArmedNpc
{
    private const float DoorwayReach = 1.4f;   // m from a doorway's opening (on either side) that counts as being in its way
    private const float AsideOut = 0.6f;       // m out from the wall's face beside the doorway
    private float3? _aside;                    // where they're reloading, out of the doorway's way

    /// <summary>A doorway: its door, the middle of its opening, across it, through it, half its width, and where they are from it.</summary>
    private readonly record struct Doorway(NpcDoor Door, float3 Centre, float3 Across, float3 Through, float Half, float Along, float Depth);

    /// <summary>The doorway they're in the way of (in its opening, or just either side of it), if any.</summary>
    private Doorway? DoorwayHere(NpcSenses s)
    {
        if (s.Nav is not { } nav)
            return null;
        foreach (var door in nav.Doors)
        {
            if (door.Shut.Length == 0)
                continue;
            var box = door.Shut[0];
            foreach (var leaf in door.Shut.AsSpan(1))
                box = new Aabb(float3.Min(box.Min, leaf.Min), float3.Max(box.Max, leaf.Max));
            if (Position.Y < box.Min.Y - 1f || Position.Y > box.Min.Y + 1f)
                continue;

            // The opening: across it (the long side of its leaves, shut) and through it (their thickness).
            var size = box.Size;
            var acrossX = size.X >= size.Z;
            var across = acrossX ? float3.UnitX : float3.UnitZ;
            var through = acrossX ? float3.UnitZ : float3.UnitX;
            var half = (acrossX ? size.X : size.Z) / 2;
            var c = box.Center with { Y = Position.Y };
            var at = Position - c;
            var (along, depth) = (float3.Dot(at, across), float3.Dot(at, through));
            if (float.Abs(along) <= half + 0.3f && float.Abs(depth) <= DoorwayReach)
                return new Doorway(door, c, across, through, half, along, depth);
        }

        return null;   // (beside one, or too far off to be in anyone's way)
    }

    /// <summary>Standing in a doorway's way: a pusher doesn't stop there to shoot, but comes on into the room.</summary>
    private bool InDoorwaysWay(NpcSenses s) => !HoldsPost && DoorwayHere(s) is not null;

    /// <summary>The magazine's just run dry: if they're in a doorway's way, somewhere beside it to reload.</summary>
    private void StepAsideToReload(NpcSenses s)
    {
        _aside = null;
        if (s.Nav is not { } nav || HoldsPost || OnLink || DoorwayHere(s) is not { } way)
            return;
        {
            var (door, c, across, through, half, depth) = (way.Door, way.Centre, way.Across, way.Through, way.Half, way.Depth);

            // Their own side of it; in the opening itself, the side away from the enemy.
            var side = float.Abs(depth) > 0.25f ? float.Sign(depth) : -float.Sign(float3.Dot(Threat - c, through));
            if (side == 0)
                side = 1;

            // Either side of the opening, nearer the jamb if further out is blocked; out of sight of the enemy if they can be.
            var eye = new float3(0, EyeHeight, 0);
            var best = (Spot: (float3?)null, Score: float.MaxValue);
            foreach (var beyond in (ReadOnlySpan<float>)[0.8f, 0.5f])
            {
                foreach (var sign in (ReadOnlySpan<float>)[-1f, 1f])
                {
                    var spot = c + through * (side * AsideOut) + across * (sign * (half + beyond));
                    if (!nav.LineClear(spot, spot) || Taken(spot, s))
                        continue;
                    var score = float3.Distance(spot, Position) + (s.Clear(spot + eye, _lastSeen) < 0.999f ? 0f : 3f) + (0.8f - beyond) * 2f;
                    if (score < best.Score)
                        best = (spot, score);
                }
            }

            if (best.Spot is { } aside)
            {
                _aside = aside;
                Log.Write($"[NPC] {Name}: reloading in the {door.Name} doorway: stepping aside to ({aside.X:0.0}, {aside.Z:0.0})");
                return;
            }
        }
    }

    /// <summary>Someone else of theirs standing there already.</summary>
    private bool Taken(float3 spot, NpcSenses s)
    {
        foreach (var other in s.People)
        {
            if (!ReferenceEquals(other, this) && other.Alive && float3.DistanceSquared(other.Position with { Y = spot.Y }, spot) < 0.6f * 0.6f)
                return true;
        }

        return false;
    }

    /// <summary>While reloading, off to the side of the doorway, facing where the enemy was. False when they're not doing that.</summary>
    private bool UpdateAside(float dt, float walkSpeed, NpcSenses s, out bool moved)
    {
        moved = false;
        if (_aside is not { } spot)
            return false;
        if (_reloadLeft <= 0)
        {
            _aside = null;
            return false;
        }

        var to = spot - Position;
        to.Y = 0;
        if (float3.Dot(to, to) > 0.15f * 0.15f)
            moved = GoTowards(spot, walkSpeed * 1.3f * (1f - _limp), dt, TurnRate * 1.6f, s);
        if (!moved)
            FaceTowards(_lastSeen, dt, 2.2f);
        else
            AddTrail();
        return true;
    }
}
