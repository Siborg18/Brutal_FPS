using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The big decisions in a fight, made by weighing them up (a utility score each, a few times a second, with a lean
/// towards carrying on with whatever they're doing so they don't flip between two):
/// <list type="bullet">
/// <item><see cref="FightPlan.Engage"/>: the usual fight (Game/ArmedNpc.cs): aiming, closing in by their strategy, cover,
/// searching and holding when they lose sight. The other tactics take over from it for a while, then hand back.</item>
/// <item><see cref="FightPlan.Flank"/>: with someone else of their side keeping the player busy, one of them (one at a time
/// on a side: <see cref="SquadIntel.Flanker"/>) goes round to the player's side, by a way out of the player's sight
/// where there is one, to a spot they can shoot from. Likelier the bolder they are, and the longer the player has stayed
/// put.</item>
/// <item><see cref="FightPlan.Rush"/>: the player is reloading (they heard or saw it): the bold close in at a run, firing as
/// they come.</item>
/// <item><see cref="FightPlan.Grenade"/>: the player has stayed behind the same cover a while, or has just gone from sight
/// close by: a frag (for those who carry one), thrown where the player was last known to be, never near their own side.
/// "Frag out!".</item>
/// <item><see cref="FightPlan.Retreat"/>: broken (no courage left: Game/ArmedNpcAlert.cs): they fall back.</item>
/// </list>
/// </summary>
public sealed partial class ArmedNpc
{
    private enum FightPlan { Engage, Flank, Rush, Grenade, Retreat }

    private const float ThinkEvery = 0.35f;        // s between weighing things up (plus up to 0.25 s)
    private const float StickWith = 0.2f;          // how much more the tactic they're on scores (so they don't flip-flop)

    private FightPlan _tactic;
    private float _tacticThink;
    private float _tacticFor;                      // s on the current tactic

    // Flanking.
    private const float FlankMin = 7f, FlankMax = 40f;   // m from the player they'll flank from
    private const float FlankTime = 14f;           // s at most getting there
    private const float FlankAgain = 8f;           // s before anyone on their side flanks again
    private float3 _flankSpot;
    private float3? _flankVia;                     // a way round, out of the player's sight, first (null: straight there)
    private float _flankLeft;

    // Rushing.
    private const float RushRange = 22f;           // m: further than this, the reload's over before they'd get there
    private float _rushLeft;

    // Grenades.
    private const float GrenadeMin = 7f, GrenadeMax = 24f;  // m to where they'd throw it
    private const float GrenadeSpeed = 15f;        // m/s at most, by hand
    private const float GrenadeFriendClear = 5.5f; // m: nobody of their side this near where it'll go off (a frag's fragments reach 5)
    private const float GrenadeAgain = 12f;        // s before anyone on their side throws another
    private const float GrenadeOwnAgain = 25f;     // s before they throw another themself
    private static readonly string? FragsOverride = Environment.GetEnvironmentVariable("BRUTALFPS_NPCFRAGS")?.Trim().ToLowerInvariant();
    private int _frags = -1;                       // how many they carry (-1: not rolled yet)
    private float _nextGrenadeAt;
    private float _throwWind;                      // s until it leaves the hand (above 0: at it now)
    private float3 _throwAt, _throwVelocity, _throwFrom;

    /// <summary>How many frags they have left: hostiles carry them now and then (a commando, two); police don't. BRUTALFPS_NPCFRAGS=all or none says otherwise.</summary>
    public int FragsLeft
    {
        get
        {
            if (_frags < 0)
            {
                var roll = new Random(StableSeed(Name) ^ 0x6a7e).NextDouble();
                _frags = FragsOverride switch
                {
                    "all" => 2,
                    "none" => 0,
                    _ => Faction != Faction.Hostile ? 0 : Profile.IsCommando ? 2 : roll < 0.45 ? 1 : 0,
                };
            }

            return _frags;
        }
    }

    /// <summary>
    /// One frame of the tactics: weighs them up now and then (<see cref="ChooseTactic"/>), then carries out any but
    /// Engage. False: Engage, the usual fight goes on; <paramref name="moved"/> is whether they moved.
    /// </summary>
    private bool UpdateTactics(float dt, float walkSpeed, float runSpeed, NpcSenses s, float3 eye, out bool moved)
    {
        moved = false;
        _tacticFor += dt;
        _tacticThink -= dt;
        if (_tacticThink <= 0f && _throwWind <= 0f)
        {
            _tacticThink = ThinkEvery + 0.25f * (float)_random.NextDouble();
            ChooseTactic(s, eye);
        }

        switch (_tactic)
        {
            case FightPlan.Retreat:
                Retreat(s);
                return true;
            case FightPlan.Grenade:
                return UpdateGrenade(dt, s, eye);
            case FightPlan.Flank:
                return UpdateFlank(dt, runSpeed, s, out moved);
            case FightPlan.Rush:
                return UpdateRush(dt, runSpeed, s, eye, out moved);
            default:
                return false;
        }
    }

    /// <summary>Scores each tactic for how things stand, and goes with the best.</summary>
    private void ChooseTactic(NpcSenses s, float3 eye)
    {
        var intel = s.Squads.For(Faction, Zone);
        var supp = SuppressionAt(s.Time);
        var (allies, engaged, enemies) = Odds(s);
        var courage = Courage(allies, enemies);
        var free = !HoldsPost && !OnGun && !GunTaken && !Climbing && _gunState == OnGunState.No;
        var onPlayer = _targetIsPlayer && _everSeen && EnemyOfPlayer(s) && s.PlayerAlive;
        var known = onPlayer && (intel.Known(s.Time, 5f) || _targetVisible);
        var target = _targetVisible ? _lastSeen : intel.Known(s.Time, 5f) ? intel.LastKnown : _lastSeen;
        var dist = Distance(Position with { Y = 0 }, target with { Y = 0 });
        var bold = Aggression == Aggression.Aggressive;
        var camped = intel.CampedFor(s.Time);
        float Stick(FightPlan t) => _tactic == t ? StickWith : 0f;

        var engage = 0.5f + Stick(FightPlan.Engage);

        var retreat = -1f;
        if (free && courage < 0.2f && !Profile.IsCommando)   // (a commando never falls back)
            retreat = 0.6f + (0.2f - courage) * 3f + (Health < 50f ? 0.15f : 0f);

        var flank = -1f;
        var flanker = intel.Flanker;
        var flankFree = flanker is null || ReferenceEquals(flanker, this) || !flanker.Alive || flanker._tactic != FightPlan.Flank;
        if (free && known && engaged >= 1 && flankFree && Aggression != Aggression.Defensive && Strategy != NpcStrategy.HoldBack
            && (s.Time >= intel.NextFlankAt || _tactic == FightPlan.Flank) && dist is >= FlankMin and <= FlankMax && courage >= 0.35f
            && _cf == CoverFight.None)
        {
            flank = 0.3f + (bold ? 0.12f : 0f) + (Strategy == NpcStrategy.Push ? 0.08f : 0f)
                    + 0.3f * float.Min(camped / 8f, 1f) + (_targetVisible ? 0f : 0.1f) - 0.3f * supp + 2f * Stick(FightPlan.Flank);
        }

        var rush = -1f;
        if (free && onPlayer && s.Time < _playerReloadUntil && dist < RushRange && courage >= 0.35f && Aggression != Aggression.Defensive)
            rush = (bold || Strategy == NpcStrategy.Push ? 0.95f : 0.7f) - 0.2f * supp;
        else if (_tactic == FightPlan.Rush && _rushLeft > 0f)
            rush = 0.9f;

        var grenade = -1f;
        if (free && known && FragsLeft > 0 && s.PlanFrag is not null && s.ThrowFrag is not null && s.Time >= intel.NextGrenadeAt
            && s.Time >= _nextGrenadeAt && dist is >= GrenadeMin and <= GrenadeMax && _reloadLeft <= 0f)
        {
            var hiding = !_targetVisible && _lostFor is > 0.8f and < 10f;
            if (camped > 3.5f || hiding)
                grenade = 0.55f + 0.35f * float.Min(camped / 8f, 1f) + (hiding ? 0.1f : 0f) + (courage < 0.5f ? 0.05f : 0f);
        }

        var best = FightPlan.Engage;
        var score = engage;
        if (retreat > score) (best, score) = (FightPlan.Retreat, retreat);
        if (rush > score) (best, score) = (FightPlan.Rush, rush);
        if (grenade > score) (best, score) = (FightPlan.Grenade, grenade);
        if (flank > score) (best, score) = (FightPlan.Flank, flank);
        if (best == _tactic)
            return;
        if (!StartTactic(best, s, eye, intel, target))
            return;
        Log.Write($"[NPC] {Name}: tactic {_tactic} -> {best} (score {score:0.00}; courage {courage:0.00}, morale {Morale:0.00}, " +
                  $"{allies} of their side fighting near, {engaged} in contact, suppression {supp:0.0}, player camped {camped:0.0} s)");
        EndTactic(s, intel);
        _tactic = best;
        _tacticFor = 0f;
    }

    /// <summary>Getting a tactic going (the flank spot, the throw). False: it can't be done after all.</summary>
    private bool StartTactic(FightPlan tactic, NpcSenses s, float3 eye, SquadIntel intel, float3 target)
    {
        switch (tactic)
        {
            case FightPlan.Flank:
            {
                if (s.CoverSearchesLeft <= 0)
                {
                    _tacticThink = 0f;   // (next frame)
                    return false;
                }

                s.CoverSearchesLeft--;
                if (!PickFlankSpot(s, target, out var side))
                {
                    intel.NextFlankAt = s.Time + 4f;
                    return false;
                }

                intel.Flanker = this;
                _flankLeft = FlankTime;
                _blocked = 0f;
                Say(Bark.Flanking, s, side);
                // Someone keeping the player busy says so.
                foreach (var other in s.People)
                {
                    if (other is ArmedNpc { InContact: true, Alive: true } ally && !ReferenceEquals(ally, this) && ally.Faction == Faction
                        && Distance(ally.Position, Position) < ShoutRange)
                    {
                        ally.Say(Bark.Covering, s);
                        break;
                    }
                }

                return true;
            }

            case FightPlan.Rush:
                _rushLeft = 2.6f;
                _lineUp = 0f;
                Say(Bark.Moving, s);
                return true;

            case FightPlan.Grenade:
                return StartGrenade(s, eye, intel, target);

            case FightPlan.Retreat:
                Say(Bark.FallBack, s);
                return true;
        }

        return true;
    }

    /// <summary>Done with the tactic they were on.</summary>
    private void EndTactic(NpcSenses s, SquadIntel intel)
    {
        if (_tactic == FightPlan.Flank && ReferenceEquals(intel.Flanker, this))
        {
            intel.Flanker = null;
            intel.NextFlankAt = s.Time + FlankAgain;
        }

        if (_tactic == FightPlan.Grenade)
            DropGrenadeThrow(0.1f);
        _running = false;
    }

    /// <summary>Back to the usual fight (<paramref name="why"/>: logged).</summary>
    private void BackToEngage(NpcSenses s, string why)
    {
        if (_tactic == FightPlan.Engage)
            return;
        Log.Write($"[NPC] {Name}: {_tactic.ToString().ToLowerInvariant()} over ({why}) after {_tacticFor:0.0} s");
        EndTactic(s, s.Squads.For(Faction, Zone));
        _tactic = FightPlan.Engage;
        _tacticFor = 0f;
        _tacticThink = float.Max(_tacticThink, 1f);
        _blocked = 0f;
    }

    /// <summary>Out of the fight altogether: no tactic carries over.</summary>
    private void ResetTactics(NpcSenses? s)
    {
        if (s is not null)
            EndTactic(s, s.Squads.For(Faction, Zone));
        else
            DropGrenadeThrow(0.1f);
        _tactic = FightPlan.Engage;
        _tacticFor = 0f;
        _tacticThink = 0f;
        _rushLeft = 0f;
        _calledContact = false;
    }

    // ------------------------------------------------------------------------------------------------ retreating

    private void Retreat(NpcSenses s)
    {
        Log.Write($"[NPC] {Name}: broken (morale {Morale:0.00}, {Health:0} HP): falling back");
        var away = Position - Threat;
        away.Y = 0;
        var len = float.Sqrt(float3.Dot(away, away));
        (_fleeDir, _fleeClear) = Steer(len > 1e-3f ? away / len : -Ahead, s);
        ResetTactics(s);
        SetState(NpcState.Fleeing);
        // (A rest out of it does them good: they come back a little steadier.)
        _morale = float.Max(Morale, 0.3f);
    }

    // ------------------------------------------------------------------------------------------------ flanking

    /// <summary>
    /// A spot off to the player's side (as seen from the rest of their side fighting them), in sight of the player, that
    /// they can get to; and if the straight way there is in the player's sight, a way round first that isn't.
    /// <paramref name="side"/>: 0 left, 1 right (for the callout).
    /// </summary>
    private bool PickFlankSpot(NpcSenses s, float3 target, out int side)
    {
        side = 0;
        // Where the rest of their side are, as the player sees it.
        var centre = float3.Zero;
        var n = 0;
        foreach (var other in s.People)
        {
            if (other is ArmedNpc { InContact: true, Alive: true } ally && !ReferenceEquals(ally, this) && ally.Faction == Faction && ally.Zone == Zone)
            {
                centre += ally.Position;
                n++;
            }
        }

        centre = n > 0 ? centre / n : Position;
        var axis = new float3(target.X - centre.X, 0, target.Z - centre.Z);
        var axisLength = Length(axis);
        if (axisLength < 1e-3f)
            return false;
        axis /= axisLength;
        var across = float3.Cross(float3.UnitY, axis);
        var mine = float3.Dot(Position - target, across) >= 0f ? 1f : -1f;
        var feetY = s.Nav?.FloorHeight(target, Position.Y) ?? Position.Y;
        var r = float.Clamp(Distance(Position, target) * 0.7f, 7f, 15f);
        var chest = new float3(0, 1.3f, 0);
        var eyes = new float3(0, EyeHeight, 0);

        var found = false;
        var bestScore = float.MinValue;
        foreach (var sign in (ReadOnlySpan<float>)[mine, -mine])
        {
            foreach (var degrees in (ReadOnlySpan<float>)[85f, 110f])
            {
                var (sin, cos) = float.SinCos(float.DegreesToRadians(degrees));
                var dir = -axis * cos + across * (sign * sin);
                var p = new float3(target.X, feetY, target.Z) + dir * r;
                if (s.Nav is { } nav)
                    p.Y = nav.FloorHeight(p, feetY);
                if (Level.ZoneAt(p + new float3(0, 1f, 0)) != Zone || s.Nav?.Walkable(p) == false || Distance(Position, p) > 35f)
                    continue;
                if (s.Clear(p + eyes, target) < 0.99f)
                    continue;   // (no shot from there)
                var mid = (Position + p) * 0.5f;
                var exposed = s.Clear(target, mid + chest) > 0.99f;
                var score = -0.1f * Distance(Position, p) - (exposed ? 3f : 0f) + (sign == mine ? 0.5f : 0f);
                if (score <= bestScore)
                    continue;
                bestScore = score;
                _flankSpot = p;
                _flankVia = null;
                // Left or right of the way they face the player now (heading 0 faces +Z, and +X is then on their left).
                var (fx, fz, sx, sz) = (target.X - Position.X, target.Z - Position.Z, p.X - Position.X, p.Z - Position.Z);
                side = fx * sz - fz * sx > 0f ? 1 : 0;
                found = true;
                if (exposed)
                {
                    // A way round: out from the middle of the way, away from the player, where they can't see.
                    var awayFromTarget = mid - target;
                    awayFromTarget.Y = 0;
                    var len = Length(awayFromTarget);
                    if (len > 1e-3f)
                    {
                        awayFromTarget /= len;
                        foreach (var out_ in (ReadOnlySpan<float>)[3f, 5.5f])
                        {
                            var via = mid + awayFromTarget * out_;
                            if (s.Nav?.Walkable(via) != false && Level.ZoneAt(via + new float3(0, 1f, 0)) == Zone && s.Clear(target, via + chest) < 0.99f)
                            {
                                _flankVia = via;
                                break;
                            }
                        }
                    }
                }
            }
        }

        if (found)
            Log.Write($"[NPC] {Name}: flanking: to ({_flankSpot.X:0.0}, {_flankSpot.Z:0.0}), {Distance(Position, _flankSpot):0.0} m{(_flankVia is null ? "" : ", round out of sight")}");
        return found;
    }

    /// <summary>One frame of going round to the flank spot. False: done (back to the usual fight, from there).</summary>
    private bool UpdateFlank(float dt, float runSpeed, NpcSenses s, out bool moved)
    {
        moved = false;
        _flankLeft -= dt;
        if (_flankLeft <= 0f)
        {
            BackToEngage(s, "took too long getting round");
            return false;
        }

        // Right on top of the player, and they can see them: no need to go round any more.
        if (_targetVisible && Distance(Position, _lastSeen) < 8f)
        {
            BackToEngage(s, "the player's right there");
            return false;
        }

        var goal = _flankVia ?? _flankSpot;
        var way = goal - Position;
        way.Y = 0;
        if (Length(way) < 0.8f || _blocked > 2f)
        {
            if (_flankVia is not null && _blocked <= 2f)
            {
                _flankVia = null;
                _blocked = 0f;
                return true;
            }

            BackToEngage(s, _blocked > 2f ? "held up on the way" : "in position");
            return false;
        }

        if (GoTowards(goal, runSpeed * 0.85f * (1f - _limp), dt, TurnRate * 1.6f, s))
        {
            moved = true;
            _blocked = 0f;
            AddTrail();
        }
        else
        {
            _blocked += dt;
        }

        return true;
    }

    // ------------------------------------------------------------------------------------------------ rushing

    /// <summary>One frame of closing in at a run while the player reloads, firing when they're square on. False: close enough, or it's over.</summary>
    private bool UpdateRush(float dt, float runSpeed, NpcSenses s, float3 eye, out bool moved)
    {
        moved = false;
        _rushLeft -= dt;
        var intel = s.Squads.For(Faction, Zone);
        if (_rushLeft <= 0f || !(_targetVisible || intel.Known(s.Time, 4f)))
        {
            BackToEngage(s, "the moment's gone");
            return false;
        }

        var target = _targetVisible ? _lastSeen : intel.LastKnown;
        var to = target - eye;
        var flat = new float3(to.X, 0, to.Z);
        var dist = Length(flat);
        if (dist < PushRange)
            return false;   // (on them: the usual fight shoots from here)
        _running = true;
        if (GoTowards(target, runSpeed * 0.9f * (1f - _limp), dt, TurnRate * 1.8f, s))
        {
            moved = true;
            AddTrail();
        }

        var facing = dist > 1e-3f ? float3.Dot(flat / dist, Ahead) : 1f;
        _lineUp = facing > ShootCos ? _lineUp + dt : 0f;
        if (_targetVisible && _reloadLeft <= 0f && _lineUp > 0.2f && _cooldown <= 0f && dist < Gun.Range)
            Shoot(s);
        return true;
    }

    // ------------------------------------------------------------------------------------------------ grenades

    /// <summary>Works out the throw at where the player was last known to be, if it can be made and is safe for their side.</summary>
    private bool StartGrenade(NpcSenses s, float3 eye, SquadIntel intel, float3 target)
    {
        var at = intel.Known(s.Time, 5f) ? intel.LastKnown : target;
        foreach (var other in s.People)
        {
            if (other.Alive && other.Faction == Faction && !ReferenceEquals(other, this)
                && Distance(other.Position, at - new float3(0, 1.3f, 0)) < GrenadeFriendClear)
            {
                _nextGrenadeAt = s.Time + 4f;
                Log.Write($"[NPC] {Name}: would throw a frag, but {other.Name} is too near where the player is ({Distance(other.Position, at - new float3(0, 1.3f, 0)):0.0} m)");
                return false;
            }
        }

        // Nobody lobs one onto the spot: off by up to a metre or so for a good shot, three for a poor one.
        var off = (0.8f + 2.2f * (1f - Profile.Accuracy)) * float.Sqrt((float)_random.NextDouble());
        var offAngle = (float)_random.NextDouble() * float.Tau;
        at += new float3(float.Sin(offAngle), 0, float.Cos(offAngle)) * off;
        var from = eye + float3.Normalize((at - eye) with { Y = 0 } + new float3(0, 1e-3f, 0)) * 0.35f;
        if (s.PlanFrag!(from, at - new float3(0, 1.3f, 0), GrenadeSpeed) is not { } plan)
        {
            _nextGrenadeAt = s.Time + 3f;
            Log.Write($"[NPC] {Name}: would throw a frag, but couldn't work out a throw to there");
            return false;
        }

        // Where it'll really go off: not by themself, nor by anyone of their side.
        if (Distance(plan.Landing, Position) < GrenadeFriendClear)
        {
            _nextGrenadeAt = s.Time + 5f;
            Log.Write($"[NPC] {Name}: would throw a frag, but it'd come down too near them");
            return false;
        }

        foreach (var other in s.People)
        {
            if (other.Alive && other.Faction == Faction && Distance(other.Position, plan.Landing) < GrenadeFriendClear)
            {
                _nextGrenadeAt = s.Time + 4f;
                Log.Write($"[NPC] {Name}: would throw a frag, but it'd come down by {other.Name}");
                return false;
            }
        }

        _throwAt = plan.Landing;
        _throwVelocity = plan.Velocity;
        _throwFrom = from;
        _throwWind = FlareThrowTime;
        _burstLeft = 0;
        intel.NextGrenadeAt = s.Time + GrenadeAgain;   // (claimed: nobody else throws one meanwhile)
        _animator.PlayOverlay("ThrowFlare", _upperBody, 0.1f, 0f);
        Log.Write($"[NPC] {Name}: throwing a frag at where the player was ({Distance(Position, plan.Landing):0.0} m; camped {intel.CampedFor(s.Time):0.0} s, out of sight {_lostFor:0.0} s)");
        return true;
    }

    /// <summary>One frame of the throw. False: done.</summary>
    private bool UpdateGrenade(float dt, NpcSenses s, float3 eye)
    {
        if (_throwWind <= 0f)
        {
            BackToEngage(s, "thrown");
            return false;
        }

        FaceTowards(_throwAt, dt, 2.2f);
        _throwWind -= dt;
        if (_throwWind > 0f)
            return true;

        // (From where it was worked out from: from the hand, the arc would be another, and could come back off a rail.)
        var from = _throwFrom;
        s.ThrowFrag!(this, from, _throwVelocity);
        _frags = FragsLeft - 1;
        _nextGrenadeAt = s.Time + GrenadeOwnAgain;
        _reAim = FlareAfter;
        Say(Bark.FragOut, s);
        BackToEngage(s, "thrown");
        return false;
    }

    /// <summary>Thrown off in the middle of a throw (hit, a grenade of the enemy's, out of the fight): no frag.</summary>
    private void DropGrenadeThrow(float fade)
    {
        if (_throwWind <= 0f)
            return;
        _throwWind = 0f;
        _animator.Stop("ThrowFlare", fade);
        if (_tactic == FightPlan.Grenade)
            _tactic = FightPlan.Engage;
    }
}
