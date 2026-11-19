using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Hiding places (<see cref="HidingPlace"/>, <see cref="NpcSenses.HidingPlaces"/>: Rio's little rooms behind open doors).
/// <list type="bullet">
/// <item>Someone unarmed who's frightened (a shot, the gun, someone panicking) runs for the nearest one with a free spot
/// on their own alley, within <see cref="HideReach"/> m and not on the far side of the danger, instead of running off or
/// cowering where they stand (a freezer too, unless the gun's right on them). In it they crouch at their spot (Cower) for
/// twice as long as usual before they calm down and walk back out.</item>
/// <item>A hostile hurt below <see cref="RegroupHealth"/> with one within <see cref="RegroupReach"/> m falls back into it to
/// regroup, and so does one badly hurt who'd back off anyway; after <see cref="RegroupFor"/> s in there they come out
/// fighting (once: after that they back off the ordinary way).</item>
/// <item>Nobody can get there (stuck for 3 s, 8 s within 3 m of the door while others go in, or 30 s gone): they give it up and run or cower as usual.</item>
/// </list>
/// The log says who makes for which place, who gets there and who leaves.
/// </summary>
public partial class Npc
{
    private const float HideReach = 30f;        // m to the door, straight
    private const float HideLevel = 0.6f;       // m: the door on their own floor (alley)
    private const float HideGiveUp = 30f;       // s on the way
    private const float HideStall = 3f;         // s without getting nearer
    private const float HideQueue = 8f;         // s without getting nearer, close to the door (waiting for someone in the doorway)
    private protected const float RegroupHealth = 45f;
    private protected const float RegroupReach = 14f;
    private const float RegroupFor = 8f;        // s in there before they come out again

    private HidingPlace? _hide;
    private int _hideSpot = -1;
    private bool _hideTried, _hiddenIn;
    private protected bool _regrouped;
    private float _hideBest, _hideStall;

    /// <summary>The hiding place they've taken a spot in, or are running for (null: none).</summary>
    public HidingPlace? HidingIn => _hide;

    /// <summary>In a hiding place, crouched at their spot.</summary>
    public bool InHiding => _hide is not null && _hiddenIn;

    /// <summary>The best hiding place for them now, and how far its door is (null: none will do).</summary>
    private HidingPlace? PickHidingPlace(NpcSenses s, float reach, out float distance)
    {
        distance = 0f;
        if (s.HidingPlaces.Count == 0 || Scripted || (this is ArmedNpc && _regrouped))
            return null;
        var toThreat = Threat - Position;
        toThreat.Y = 0;
        var threatDist = float.Sqrt(float3.Dot(toThreat, toThreat));
        HidingPlace? best = null;
        var bestScore = float.MaxValue;
        foreach (var place in s.HidingPlaces)
        {
            if (float.Abs(place.Doorway.Y - Position.Y) > HideLevel)
                continue;
            if (place.Free == 0 && !ReferenceEquals(_hide, place))
                continue;
            var to = place.Doorway - Position;
            to.Y = 0;
            var d = float.Sqrt(float3.Dot(to, to));
            if (d > reach)
                continue;
            // Not past the danger: the door the same way as the threat and the threat nearer it than they are.
            if (d > 1.5f && threatDist > 0.5f && float3.Dot(to / d, toThreat / threatDist) > 0.4f && Distance(place.Doorway, Threat) < d + 1f)
                continue;
            var score = place.Room.Contains(Position + new float3(0, 0.5f, 0)) ? 0f : d + place.Occupants * 2f;
            if (score < bestScore)
                (best, bestScore, distance) = (place, score, d);
        }

        return best;
    }

    /// <summary>Takes a spot in the best hiding place for them, if there is one: true if they're off to it.</summary>
    private protected bool TryHide(NpcSenses s)
    {
        _hideTried = true;
        if (PickHidingPlace(s, HideReach, out var distance) is not { } place)
            return false;
        var spot = place.Claim(this);
        if (spot < 0)
            return false;
        if (_hide is not null && !ReferenceEquals(_hide, place))
            _hide.Release(this);
        _hide = place;
        _hideSpot = spot;
        _hiddenIn = false;
        _hideBest = float.MaxValue;
        _hideStall = 0f;
        Log.Write($"[NPC] {Name}: making for the {place.Name} ({distance:0.0} m off, {place.Occupants} of {place.Spots.Count} spots taken)");
        return true;
    }

    /// <summary>A hostile hurt enough, with a hiding place close by, falls back into it to regroup (once).</summary>
    private protected bool WantsToRegroup(NpcSenses s) =>
        !_regrouped && Health < RegroupHealth && s.HidingPlaces.Count > 0 && PickHidingPlace(s, RegroupReach, out _) is not null;

    /// <summary>Running for their hiding place. Returns whether they moved.</summary>
    private bool UpdateHideRun(float dt, float runSpeed, NpcSenses s)
    {
        var place = _hide!;
        var spot = place.Spots[_hideSpot];
        var to = spot - Position;
        to.Y = 0;
        var dist = float.Sqrt(float3.Dot(to, to));
        if (dist < 0.35f)
        {
            _hiddenIn = true;
            SetState(NpcState.Cowering);
            _calmLeft = this is ArmedNpc ? RegroupFor : CalmTime * 2f;
            Log.Write($"[NPC] {Name}: reached the {place.Name}, hiding ({place.Occupants} of {place.Spots.Count} spots taken)");
            return false;
        }

        if (dist < _hideBest - 0.2f)
        {
            _hideBest = dist;
            _hideStall = 0f;
        }
        else
        {
            _hideStall += dt;
        }

        if (_hideStall > (dist < 3f ? HideQueue : HideStall) || _stateTime > HideGiveUp)
        {
            Log.Write($"[NPC] {Name}: can't get into the {place.Name} ({dist:0.0} m short): running on");
            ReleaseHide();
            _stateTime = 0f;
            return false;
        }

        var speed = runSpeed * (1f - _limp) * float.Max(_walkWeight, 0.2f);
        if (dist < 1.2f)
            speed = float.Min(speed, 1.4f);   // (in through the door, not into the back wall)
        var moved = GoTowards(spot, speed, dt, FleeTurnRate, s);
        if (_trail.Count == 0 || Distance2(Position, _trail[^1]) > 1f)
        {
            if (_trail.Count < 400)
                _trail.Add(Position);
        }

        return moved;
    }

    /// <summary>Gives up their spot (out of it, or not going after all).</summary>
    private protected void ReleaseHide()
    {
        if (_hide is null)
            return;
        if (_hiddenIn && Alive)
            Log.Write($"[NPC] {Name}: leaving the {_hide.Name}");
        _hide.Release(this);
        _hide = null;
        _hideSpot = -1;
        _hiddenIn = false;
    }

    /// <summary>From <see cref="SetState"/>: a spot is only kept while running for it or crouched in it.</summary>
    private void HideStateChanged(NpcState state)
    {
        if (state == NpcState.Fleeing)
            _hideTried = _hide is not null;
        else if (state != NpcState.Cowering)
            ReleaseHide();
    }

    /// <summary>Crouched in a hiding place long enough: a hostile who still has a fight on comes out fighting.</summary>
    private bool RegroupDone(NpcSenses s)
    {
        if (this is not ArmedNpc || !InHiding || !WantsToFight(s))
            return false;
        _regrouped = true;
        Log.Write($"[NPC] {Name}: regrouped in the {_hide!.Name}: back into the fight");
        SetState(NpcState.Fighting);
        return true;
    }
}
