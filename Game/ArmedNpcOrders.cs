using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Orders from a script that leave them fighting on their own: "get over there" (<see cref="OrderGoTo"/>). They go on the
/// alert and make their way there by the usual paths (opening doors on the way), at a run, gun up. Anyone they see on the
/// way they fight as usual; once they lose sight again they carry on to where they were sent. There: either they hold it
/// (a post: <see cref="HoldsPost"/>), or they go after the player from there.
/// </summary>
public sealed partial class ArmedNpc
{
    private float3? _orderTo;           // where a script has sent them (null: nowhere)
    private bool _orderHold;            // ...and once there, they hold it as a post
    private float _orderFace;           // ...facing this way
    private Aabb? _orderKeepInside;     // ...and stay inside this, from there on
    private string _orderWhy = "";

    /// <summary>On their way somewhere a script sent them (<see cref="OrderGoTo"/>), not there yet.</summary>
    public bool Ordered => _orderTo is not null;

    /// <summary>
    /// Sends them to <paramref name="to"/>, on the alert, by the usual paths and at a run, fighting whoever they see on
    /// the way. Once there, with <paramref name="hold"/> they stand there for good as a post (facing <paramref name="face"/>);
    /// without, they go after the player from there and come back to it when it's over. From then on they keep inside
    /// <paramref name="keepInside"/> (null: anywhere). False if they can't (dead, scripted, not fighting men, running scared).
    /// </summary>
    public bool OrderGoTo(float3 to, bool hold, float face, Aabb? keepInside, string why, NpcSenses s)
    {
        if (!Alive || Scripted || OnGun || GunTaken || Blinded || !WantsToFight(s)
            || State is not (NpcState.Calm or NpcState.Returning or NpcState.Wary or NpcState.Startled or NpcState.Fighting))
            return false;
        _orderTo = to;
        _orderHold = hold;
        _orderFace = face;
        _orderKeepInside = keepInside;
        _orderWhy = why;
        KeepInside = null;   // (on their way, wherever the way goes)
        HoldsPost = false;
        _leftPost = false;
        Repost(to, face);    // (should the fight end before they're there: that's where they go back to)
        if (State != NpcState.Fighting)
            Alarm(to, Stimulus.Noise, notThePlayer: true);   // (into the fight, gun up: UpdateFight takes them there)
        Log.Write($"[NPC] {Name}: {why}: on the way to ({to.X:0.0}, {to.Y:0.0}, {to.Z:0.0}){(hold ? " to hold it" : "")}");
        return true;
    }

    /// <summary>
    /// With nobody in sight: on to where they were sent, if they aren't already heading there (a search, a shot heard, cover
    /// wanted: the order comes first).
    /// </summary>
    private void FollowOrder()
    {
        if (_orderTo is not { } to || (_hunt == Hunt.Search && _spot == to && _lookLeft <= 0f))
            return;
        _hunt = Hunt.Search;
        _spot = to;
        _vanished = _lastSeen = to + new float3(0, 1.3f, 0);
        _lead = Threat;
        _shotOrder = ShotOrder.None;
        _portalOrder = null;
        _chasing = _peering = _guarding = null;
        _squadSearch = false;
        _pushingFlash = false;
        _lookLeft = 0;
        _blocked = 0;
        _spotsLeft = SearchSpots;
        _huntLeft = float.Max(_huntLeft, SearchTime);
    }

    /// <summary>
    /// Sent back to hold a post: they go, at a run, whoever's in sight (the post matters more than the fight). True while
    /// that's what they're doing this frame (<paramref name="moved"/>: whether they moved); false once they're there or
    /// if there's no such order, and the fight carries on as usual.
    /// </summary>
    private bool ReturnToPost(float dt, float runSpeed, NpcSenses s, out bool moved)
    {
        moved = false;
        if (!_orderHold || _orderTo is not { } post)
            return false;
        var way = post - Position;
        way.Y = 0;
        if (Length(way) < 0.8f)
        {
            ReachedOrder(s);
            return false;
        }

        _running = true;
        moved = GoTowards(post, runSpeed * 0.85f * (1f - _limp), dt, TurnRate * 1.4f, s);
        if (moved)
            AddTrail();
        return true;
    }

    /// <summary>Where they've got to on the way to an order: true once they're there (or as near as they can get), and it's done.</summary>
    private bool ReachedOrder(NpcSenses s)
    {
        if (_orderTo is not { } to)
            return false;
        _orderTo = null;
        KeepInside = _orderKeepInside;
        var dist = Distance(Position with { Y = 0 }, to with { Y = 0 });
        if (_orderHold)
        {
            HoldsPost = true;
            Heading = _orderFace;
            _hunt = Hunt.Hold;
            _vanished = _lastSeen = Position + new float3(float.Sin(_orderFace), 0, float.Cos(_orderFace)) * 10f + new float3(0, 1.3f, 0);
            _huntLeft = HoldTime;
            Log.Write($"[NPC] {Name}: at their post ({dist:0.0} m off it): holding it");
            return true;
        }

        // On after the player: from where the fight was last heard of (their side's last sighting, or the player as they are now).
        var intel = s.Squads.For(Faction, Zone);
        var after = intel.Known(s.Time, 30f) ? intel.LastKnown : s.PlayerEye;
        _targetIsPlayer = true;
        _spot = after with { Y = after.Y - EyeHeight };
        _vanished = _lastSeen = after;
        Threat = _lead = new float3(after.X, Position.Y, after.Z);
        _lookLeft = 0;
        _blocked = 0;
        _spotsLeft = SearchSpots;
        _huntLeft = SearchTime * 1.5f;
        Log.Write($"[NPC] {Name}: through ({dist:0.0} m from where they were sent): going after the player at ({after.X:0.0}, {after.Z:0.0})");
        return true;
    }
}
