using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Suppression, self-preservation and fighting from cover.
/// <list type="bullet">
/// <item>Suppression: rounds cracking past them (<see cref="NearMiss"/>), hitting them (<see cref="WasHit"/>) and the
/// hammering of a machine gun close by (<see cref="HeardHeavyFire"/>) build it up; it wears off a moment after the fire
/// stops. A machine gun firing long bursts at them, or near them, soon has anyone's head down.</item>
/// <item>Self-preservation (<see cref="SelfPreserving"/>): some people, not all, value their skin. They get down under
/// much less fire, fight from cover once they're hurt, and back off when badly hurt even if they're the aggressive sort.</item>
/// <item>Fighting from cover: pinned (or hurt, for the self-preserving), they find a spot sheltered from the enemy with a
/// place just beside it to lean out from, run for it, and then take turns: down behind it for a moment (longer the
/// heavier the fire; a reload goes here), then out to the side to fire a burst or two back (at the enemy if they can
/// see them, or at where the fire's coming from if not), then back down. When the fire eases off they carry on as
/// before.</item>
/// </list>
/// </summary>
public sealed partial class ArmedNpc
{
    // Suppression.
    private const float SuppressionCap = 3f;
    private const float SuppressionHold = 0.6f;    // s after the last round before it starts to wear off
    private const float SuppressionFade = 0.45f;   // per s, after that
    private const float PinnedAt = 1.1f;           // anyone gets down under this much
    private const float SelfPreservingPinnedAt = 0.45f;  // one with a sense of self-preservation, under this much
    private const float CommandoPinnedAt = 2.2f;   // a commando only under fire that keeps coming (a few shots, even a hit, won't do it)
    private const float ReleasedAt = 0.35f;        // below this, they come out of it (after CoverFightMin)
    private const float NearMissWeight = 0.3f;     // a round right past the head (less, further off)
    private const float HitWeight = 0.6f;
    private const float HeavyFireWeight = 0.09f;   // each round of a machine gun heard close by (less, further off): ~1 a second of it

    // Self-preservation.
    private const float SelfPreservingChance = 0.4f;     // who has it (BRUTALFPS_SELFPRESERVING=all or none says otherwise)
    private const float SelfPreservingHurt = 70f;        // hurt below this, they fight from cover
    private const float SelfPreservingBackOff = 25f;

    // Fighting from cover.
    private const float CoverFightMin = 5f;        // s at least, once they've gone to ground
    private const float CoverFightRetry = 3f;      // s before looking again when there was nowhere
    private const float CoverFightNotNear = 5f;    // m: an enemy this close is no time to go looking for cover
    private const float CoverFightLost = 10f;      // s without sight (and without fire) before they leave cover to hunt
    private const float CoverCheckEvery = 1f;      // s between checks that the cover still shelters them from where the enemy is
    private const float PeekTime = 2.2f;           // s out at the side, at most
    private const float PeekMuzzle = 1.45f;        // m: about where the gun is, aimed
    private static readonly float[] PeekOffsets = [0.9f, 1.4f];
    private const int CoverCandidates = 4;         // spots per ring tried for a place to lean out from (rays are not free)

    private static readonly string? SelfPreservingOverride = Environment.GetEnvironmentVariable("BRUTALFPS_SELFPRESERVING")?.Trim().ToLowerInvariant();

    private enum CoverFight { None, Moving, Hiding, Peeking }

    private float _suppression;         // as of _suppressedAt
    private float _suppressedAt;        // NpcSenses.Time
    private float3 _suppressedFrom;     // where the last of it came from
    private bool _contact;              // in this fight they've seen the enemy, or been fired on: their side is on the alert
    private bool _pinnedLogged;

    private CoverFight _cf;
    private float3 _cfCover;            // the spot behind cover
    private float3 _cfPeek;             // where they lean out from
    private float3 _cfFrom;             // the enemy, or where the fire comes from
    private float _cfTimer;             // s left of hiding, or of peeking
    private float _cfFor;               // s since they went to ground
    private float _cfRetry;
    private float _cfCheck;
    private int _cfShots;               // rounds fired on this peek

    /// <summary>They value their own skin: get down under less fire, fight from cover once hurt, back off sooner.</summary>
    public bool SelfPreserving { get; }

    /// <summary>In a firefight: they've seen the enemy or been fired on. Their side round them goes on the alert (<see cref="NpcSenses.Alerted"/>).</summary>
    public bool InContact => State == NpcState.Fighting && _contact;

    /// <summary>Fighting from cover (on their way to it, down behind it, or leaning out of it).</summary>
    public bool InCoverFight => _cf != CoverFight.None;

    private static bool RollSelfPreservation(string name) => SelfPreservingOverride switch
    {
        "all" => true,
        "none" => false,
        _ => new Random(StableSeed(name) ^ 0x5afe11).NextDouble() < SelfPreservingChance,
    };

    /// <summary>How suppressed they are at <paramref name="time"/> (on <see cref="NpcSenses.Time"/>'s clock).</summary>
    public float SuppressionAt(float time) =>
        float.Max(0f, _suppression - SuppressionFade * float.Max(0f, time - _suppressedAt - SuppressionHold));

    /// <summary>An enemy's round went by <paramref name="closeness"/> (0: right past, 1: <c>NearMissRadius</c> off) from <paramref name="from"/>.</summary>
    public void NearMiss(float closeness, float3 from, bool byPlayer, float time)
    {
        if (byPlayer && Profile.FriendlyToPlayer)
            return;
        AddSuppression(NearMissWeight * (1.2f - closeness), from, time);
        _contact = true;
        _firedOnAt = time;
    }

    /// <summary>Hit by an enemy's round fired from <paramref name="from"/>.</summary>
    public void WasHit(float3 from, float time)
    {
        AddSuppression(HitWeight, from, time);
        _contact = true;
        _firedOnAt = time;
        ShakeMorale(0.08f, "hit");
        _hitCall = true;
    }

    private bool _hitCall;   // hit since the last fight frame: "I'm hit!" (said there, with the senses to hand)

    /// <summary>A machine gun's round, fired at <paramref name="at"/>, <paramref name="far"/> of the way out to where it stops sounding close.</summary>
    public void HeardHeavyFire(float3 at, float far, bool byPlayer, float time)
    {
        if (far >= 1f || (byPlayer && Profile.FriendlyToPlayer))
            return;
        AddSuppression(HeavyFireWeight * (1f - far), at, time);
    }

    private void AddSuppression(float amount, float3 from, float time)
    {
        // Shaken, they're easier to pin down (up to half as easy again, with no morale left); a long stretch under fire wears them down.
        amount *= 1.5f - 0.5f * Morale;
        _morale = float.Max(0f, Morale - 0.015f * amount);
        _suppression = float.Min(SuppressionAt(time) + amount, SuppressionCap);
        _suppressedAt = time;
        _suppressedFrom = from;
    }

    /// <summary>Whether they should be fighting from cover now (going in: the threshold; staying in: see <see cref="UpdateCoverFight"/>).</summary>
    private bool WantsCoverFight(float suppression)
    {
        if (HoldsPost || OnGun || GunTaken || Climbing)
            return false;
        if (Profile.IsCommando)
            return suppression >= CommandoPinnedAt;
        if (suppression >= PinnedAt)
            return true;
        if (!SelfPreserving)
            return false;
        return suppression >= SelfPreservingPinnedAt || (Health < SelfPreservingHurt && _everSeen && (_targetVisible || _lostFor < 3f));
    }

    /// <summary>
    /// One frame of fighting from cover, if they are (or should start). Returns false when they aren't, and the usual
    /// fight goes on; <paramref name="moved"/> is whether they moved.
    /// </summary>
    private bool UpdateCoverFight(float dt, float walkSpeed, float runSpeed, NpcSenses s, float3 eye, out bool moved)
    {
        moved = false;
        var supp = SuppressionAt(s.Time);
        _cfRetry -= dt;
        if (supp < ReleasedAt)
            _pinnedLogged = false;

        if (_cf == CoverFight.None)
        {
            if (_cfRetry > 0 || !WantsCoverFight(supp))
                return false;
            if (_targetVisible && Distance(Position, _lastSeen with { Y = Position.Y }) < CoverFightNotNear)
                return false;   // right on top of them: just fight
            if (s.CoverSearchesLeft <= 0)
                return false;   // (someone else is looking this frame: fight on, and look next frame)
            s.CoverSearchesLeft--;
            _cfFrom = _targetVisible || supp < ReleasedAt ? _lastSeen : _suppressedFrom;
            if (!FindFiringCover(s, _cfFrom))
            {
                _cfRetry = CoverFightRetry;
                Log.Write($"[NPC] {Name}: {Why(supp)}, but there's no cover within reach: fighting in the open");
                return false;
            }

            _cf = CoverFight.Moving;
            _cfFor = 0;
            _cfShots = 0;
            _cfCheck = CoverCheckEvery;
            _blocked = 0;
            _sidestepLeft = 0;
            _fallBackLeft = 0;
            _burstLeft = 0;
            _running = false;
            Log.Write($"[NPC] {Name}: {Why(supp)}: running for cover {Distance(Position, _cfCover):0.0} m away, to fire back from there");
        }

        _cfFor += dt;
        _shotOrder = ShotOrder.None;   // (cover is the answer to that already)
        _portalOrder = null;
        if (_targetVisible)
        {
            _hunt = Hunt.Engage;
            _everSeen = true;
            _lostFor = 0;
            _calmLeft = CalmTime;
            Threat = new float3(_lastSeen.X, Position.Y, _lastSeen.Z);
            _cfFrom = _lastSeen;
        }
        else
        {
            _lostFor += dt;
            if (supp >= ReleasedAt)
            {
                _calmLeft = CalmTime;
                if (s.Time - _suppressedAt < 1f)
                    _cfFrom = _suppressedFrom;   // still coming in: from there
            }
        }

        if (supp >= PinnedAt && !_pinnedLogged)
        {
            _pinnedLogged = true;
            Log.Write($"[NPC] {Name}: pinned down (suppression {supp:0.0})");
            Say(Bark.Pinned, s);
        }

        // Out of it: the fire's eased off (and, for the self-preserving and hurt, the enemy's long gone), or they're right on top of them.
        var hurtAndWary = SelfPreserving && Health < SelfPreservingHurt;
        if (_cfFor > CoverFightMin && supp < ReleasedAt && (!hurtAndWary || _lostFor > CoverFightLost))
        {
            EndCoverFight(_lostFor > CoverFightLost ? "nothing seen for a while: out of cover to look" : "the fire's eased off: out of cover");
            return false;
        }

        if (!_targetVisible && _lostFor > CoverFightLost * 2f)
        {
            EndCoverFight("lost the enemy: out of cover");
            return false;
        }

        if (_targetVisible && Distance(Position, _lastSeen with { Y = Position.Y }) < CoverFightNotNear * 0.7f)
        {
            EndCoverFight($"{TargetName} is right on them: fighting it out");
            _cfRetry = CoverFightRetry;
            return false;
        }

        // Is it still cover, from where the enemy is now?
        _cfCheck -= dt;
        if (_cf == CoverFight.Hiding && _cfCheck <= 0)
        {
            _cfCheck = CoverCheckEvery;
            if (!Sheltered(_cfCover, _cfFrom, s))
            {
                if (s.CoverSearchesLeft <= 0)
                    _cfCheck = 0;   // (next frame)
                else
                {
                    s.CoverSearchesLeft--;
                    if (FindFiringCover(s, _cfFrom))
                    {
                        _cf = CoverFight.Moving;
                        _blocked = 0;
                        Log.Write($"[NPC] {Name}: flanked: moving to other cover {Distance(Position, _cfCover):0.0} m away");
                    }
                    else
                    {
                        EndCoverFight("flanked, and nowhere else to go: fighting in the open");
                        _cfRetry = CoverFightRetry;
                        return false;
                    }
                }
            }
        }

        switch (_cf)
        {
            case CoverFight.Moving:
            {
                // At a run, head down: no shooting on the way.
                var way = _cfCover - Position;
                way.Y = 0;
                if (Length(way) < 0.5f || _blocked > 1.2f)
                {
                    _cf = CoverFight.Hiding;
                    _cfTimer = HideTime(supp);
                    _blocked = 0;
                    _lineUp = 0;
                    break;
                }

                if (GoTowards(_cfCover, runSpeed * (1f - _limp), dt, TurnRate * 2f, s))
                {
                    _blocked = 0;
                    moved = true;
                    AddTrail();
                }
                else
                {
                    _blocked += dt;
                }

                break;
            }

            case CoverFight.Hiding:
            {
                // Down behind it, gun on where the enemy is; a good time to reload.
                if (Ammo < Gun.Magazine / 2 && _reloadLeft <= 0 && _flareWind <= 0f)
                {
                    StartReload("in cover");
                    Say(Bark.Reloading, s);
                }
                if (_targetVisible)
                    FireFromCover(dt, s, eye, quick: false);   // (they can be seen from here after all: may as well)
                else
                    AimAt(_cfFrom, dt, eye);
                _cfTimer -= dt;
                if (_cfTimer <= 0 && _reloadLeft <= 0)
                {
                    _cf = CoverFight.Peeking;
                    _cfTimer = PeekTime * (0.7f + 0.3f * (float)_random.NextDouble());
                    _cfShots = 0;
                }

                break;
            }

            case CoverFight.Peeking:
            {
                // Out to the side, a burst or two back, then down again.
                var way = _cfPeek - Position;
                way.Y = 0;
                var there = Length(way) < 0.35f;
                if (!there && GoTowards(_cfPeek, walkSpeed * (1f - _limp), dt, TurnRate * 2f, s))
                    moved = true;
                if (_targetVisible)
                    FireFromCover(dt, s, eye, quick: true);
                else if ((there || _cfTimer < PeekTime * 0.5f) && (_everSeen || supp > 0f))
                    FireBack(dt, s, eye);
                else
                    AimAt(_cfFrom, dt, eye);

                _cfTimer -= dt;
                if (_cfTimer <= 0 || _cfShots >= int.Max(Gun.Burst * 2, 3) || _reloadLeft > 0)
                {
                    _cf = CoverFight.Moving;   // back behind it
                    _blocked = 0;
                }

                break;
            }
        }

        return true;
    }

    private string Why(float supp) => supp >= PinnedAt ? $"under heavy fire (suppression {supp:0.0})"
        : supp >= SelfPreservingPinnedAt ? "being shot at, and not about to stand in the open for it"
        : $"hurt ({Health:0} health), and not about to stand in the open";

    /// <summary>How long they stay down: longer the heavier the fire.</summary>
    private float HideTime(float supp) => 1f + 1.2f * (float)_random.NextDouble() + 0.8f * float.Min(supp, 2.5f);

    /// <summary>Out of the cover fight, back to the usual ways (<paramref name="why"/>: logged, unless null).</summary>
    private void EndCoverFight(string? why)
    {
        if (_cf == CoverFight.None)
            return;
        _cf = CoverFight.None;
        _blocked = 0;
        _lineUp = 0;
        if (why is not null)
            Log.Write($"[NPC] {Name}: {why} after {_cfFor:0} s");
    }

    /// <summary>Gun on <paramref name="at"/>, primed: when the enemy shows there, the shot comes quickly.</summary>
    private void AimAt(float3 at, float dt, float3 eye)
    {
        FaceTowards(at, dt, 2.2f);
        var to = at - eye;
        var flat = new float3(to.X, 0, to.Z);
        var len = Length(flat);
        var facing = len > 1e-3f ? float3.Dot(flat / len, Ahead) : 1f;
        _lineUp = facing > ShootCos ? _lineUp + dt : 0f;
    }

    /// <summary>At the enemy they can see. <paramref name="quick"/>: they leaned out with the gun already on them.</summary>
    private void FireFromCover(float dt, NpcSenses s, float3 eye, bool quick)
    {
        AimAt(_lastSeen, dt, eye);
        var to = _lastSeen - eye;
        var dist = Length(new float3(to.X, 0, to.Z));
        var reaction = (0.9f - 0.5f * Profile.Accuracy) * (quick ? 0.5f : 1f);
        if (_reloadLeft <= 0 && _lineUp > reaction && _cooldown <= 0 && dist < Gun.Range)
        {
            var before = Ammo;
            Shoot(s);
            if (Ammo < before)
                _cfShots++;
        }
    }

    /// <summary>Can't see them from the side of the cover: fire back at where the fire's coming from, to keep their heads down too.</summary>
    private void FireBack(float dt, NpcSenses s, float3 eye)
    {
        AimAt(_cfFrom, dt, eye);
        var to = _cfFrom - eye;
        var dist = Length(new float3(to.X, 0, to.Z));
        if (_reloadLeft > 0 || _lineUp <= FlashLineUp || _cooldown > 0 || dist > Gun.Range || (_targetNpc is { Alive: false }))
            return;
        var keep = _lastSeen;
        _lastSeen = _cfFrom;
        _firingBlind = true;
        var before = Ammo;
        Shoot(s);
        _firingBlind = false;
        if (Ammo < before)
            _cfShots++;
        if (Ammo > 0)
            _lastSeen = keep;
    }

    /// <summary>The gun down, a fresh magazine in (it's filled when <c>_reloadLeft</c> runs out, in UpdateFight).</summary>
    private void StartReload(string where)
    {
        _burstLeft = 0;
        _reloadLeft = ReloadTime;
        _reAim = -1f;
        if (_clips.Get(ReloadClip) is not null)
            _animator.PlayOverlay(ReloadClip, _upperBody, 0.15f, 0.2f);
        else
            _animator.Stop(FireClip, 0.3f);
        Log.Write($"[NPC] {Name} reloading {where} ({Ammo} left)");
    }

    /// <summary>
    /// A spot sheltered from <paramref name="from"/> with a place just to one side of it to lean out and fire from, that
    /// they can run straight to on solid ground: where they stand, if it'll do, else the nearest ring out that has one
    /// (of a ring's spots, those furthest from the enemy first; a pusher's, the nearest). With nowhere to lean out from,
    /// the best spot that's sheltered at all, leaning out back the way they came. False: nowhere.
    /// </summary>
    private bool FindFiringCover(NpcSenses s, float3 from)
    {
        using var coverTimer = Perf.CoverSearch.Time(Name);
        if (Sheltered(Position, from, s) && TryPeek(Position, from, s, out var peekHere))
        {
            _cfCover = Position;
            _cfPeek = peekHere;
            return true;
        }

        var here = Distance(Position, from);
        var nearer = Strategy == NpcStrategy.Push;
        var fallback = false;
        Span<(float3 P, float FromShot)> spots = stackalloc (float3, float)[CoverDirections];
        // The spots with somewhere to lean out from are weighed against each other (CoverScore), over the ring they're
        // first found in and the one after, rather than the first that'll do.
        var bestScore = float.MinValue;
        var ringsAfterFound = -1;
        var tried = 0;
        foreach (var ring in CoverRings)
        {
            if (ring > CoverReachFor || ringsAfterFound >= 1 || tried >= CoverScored)
                break;   // (a defensive one only ducks behind something close by)
            if (ringsAfterFound >= 0)
                ringsAfterFound++;
            var count = 0;
            for (var i = 0; i < CoverDirections; i++)
            {
                var a = (i + 0.37f) * float.Tau / CoverDirections;
                var dir = new float3(float.Sin(a), 0, float.Cos(a));
                var p = Position + dir * ring;
                var fromShot = Distance(p, from);
                // Not up close to them, and (unless they're pushing) not much nearer than they are now.
                if (fromShot < CoverNotNear || (!nearer && fromShot < here - 2f) || CoverTaken(p, s) || Level.ZoneAt(p + new float3(0, 1f, 0)) != Zone)
                    continue;
                if (!Sheltered(p, from, s) || !CanRun(p, dir, s) || !OnGround(p, s))
                    continue;
                spots[count++] = (p, fromShot);
            }

            if (count == 0)
                continue;
            var found = spots[..count];
            found.Sort((x, y) => nearer ? x.FromShot.CompareTo(y.FromShot) : y.FromShot.CompareTo(x.FromShot));
            for (var i = 0; i < int.Min(count, CoverCandidates) && tried < CoverScored; i++)
            {
                tried++;
                if (!TryPeek(found[i].P, from, s, out var peek))
                    continue;
                var score = CoverScore(found[i].P, found[i].FromShot, s);
                if (ringsAfterFound < 0)
                    ringsAfterFound = 0;
                if (score > bestScore)
                {
                    bestScore = score;
                    _cfCover = found[i].P;
                    _cfPeek = peek;
                }
            }

            if (bestScore == float.MinValue && !fallback && ring <= 7f)
            {
                // Somewhere to be safe, at least: they'll lean back out the way they came (where they could see the enemy).
                fallback = true;
                _cfCover = found[0].P;
                var back = Position - _cfCover;
                back.Y = 0;
                var len = Length(back);
                _cfPeek = len > 1.4f ? _cfCover + back / len * 1.4f : Position;
            }
        }

        if (bestScore > float.MinValue)
            Log.Write($"[NPC] {Name}: cover {Distance(Position, _cfCover):0.0} m off, {Distance(_cfCover, from):0.0} m from the enemy (score {bestScore:0.0}, of {tried} weighed)");
        return bestScore > float.MinValue || fallback;
    }

    private const int CoverScored = 7;              // spots with a place to lean out at most weighed (each takes rays)

    /// <summary>
    /// How good a firing spot is: near (not a long run in the open to get there), at a good range for their gun (a pusher,
    /// the nearer the better; the shaken, the further the better), and not in sight of another enemy of theirs nearby (the
    /// one cover is from is only one of them).
    /// </summary>
    private float CoverScore(float3 p, float fromEnemy, NpcSenses s)
    {
        var travel = Distance(Position, p);
        var ideal = float.Clamp(Gun.Range * 0.45f, 8f, 30f);
        var score = -0.35f * travel - 0.12f * float.Abs(fromEnemy - ideal);
        if (Strategy == NpcStrategy.Push)
            score -= 0.15f * fromEnemy;
        if (Morale < 0.4f)
            score += 0.15f * fromEnemy;
        // Another enemy who'd have them in the open there (the nearest one or two that aren't the one this cover is from).
        var chest = p + new float3(0, 1.25f, 0);
        var checks = 2;
        foreach (var other in s.People)
        {
            if (checks <= 0)
                break;
            if (ReferenceEquals(other, this) || !other.Alive || other.Zone != Zone || !EnemyOf(other) || ReferenceEquals(other, _targetNpc))
                continue;
            if (Distance(other.Position, p) > 35f)
                continue;
            checks--;
            if (s.Clear(other.Position + new float3(0, EyeHeight, 0), chest) > 0.99f)
                score -= 6f;
        }

        if (!_targetIsPlayer && EnemyOfPlayer(s) && s.PlayerAlive && Distance(s.PlayerEye, p) < 35f && s.Clear(s.PlayerEye, chest) > 0.99f)
            score -= 6f;   // (the player, while they fight someone else)
        return score;
    }

    /// <summary>A place within a step or two to one side of <paramref name="p"/>, along the cover, with the gun and eyes clear to <paramref name="from"/>.</summary>
    private bool TryPeek(float3 p, float3 from, NpcSenses s, out float3 peek)
    {
        peek = p;
        var to = from - p;
        to.Y = 0;
        var len = Length(to);
        if (len < 1e-3f)
            return false;
        var side = float3.Cross(float3.UnitY, to / len);
        var knee = new float3(0, 0.6f, 0);
        var chest = new float3(0, 1.25f, 0);
        var gun = new float3(0, PeekMuzzle, 0);
        var eyes = new float3(0, EyeHeight, 0);
        foreach (var off in PeekOffsets)
        {
            foreach (var sign in (ReadOnlySpan<float>)[1f, -1f])
            {
                var q = p + side * (off * sign);
                if (s.Nav?.Walkable(q) == false || s.Clear(p + knee, q + knee) < 0.999f || s.Clear(p + chest, q + chest) < 0.999f || !OnGround(q, s))
                    continue;
                if (s.Clear(q + gun, from) < 0.99f || s.Clear(q + eyes, from) < 0.99f)
                    continue;
                peek = q;
                return true;
            }
        }

        return false;
    }
}
