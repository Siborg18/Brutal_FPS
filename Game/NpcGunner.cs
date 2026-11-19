using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// An <see cref="ArmedNpc"/> on the M249 on the jungle nest's tripod (<see cref="GunNest"/>). The nest's own man goes to it the
/// moment there's trouble in its arc (<see cref="PostedAtGun"/>); with it standing empty, another guard close by who can see
/// the enemy runs for it. On it, their AK goes on their back and the fight is run from <see cref="UpdateOnGun"/> rather than
/// the usual moves: they swing round the tripod with the gun, inside its limits, fire bursts off its belt, and hold it on where
/// the enemy went for a while if they drop out of sight. They leave it (and get their AK back) when the enemy has got round
/// the side, is too close to depress it on, or has been gone a long time, or when they're hurt or dazzled.
/// </summary>
public sealed partial class ArmedNpc
{
    private const float GunCallRange = 18f;     // m: a guard this near the gun runs for it, if it's empty and they can see the enemy
    private const float GunPostRange = 30f;     // m: the man posted on it comes back to it from this far
    private const float GunMinRange = 7f;       // m: an enemy nearer than this: no time to get on it
    private const float GunTooClose = 4f;       // m: on it, with the enemy this close: off it
    private const float GunRange = 70f;         // m: they'll fire it this far
    private const float GunFlankRange = 25f;    // m: out of the arc and this near (or anywhere behind them): flanked
    private const float GunEye = 1.3f;          // m: their eyes, crouched behind it
    private const float GunHurt = 45f;          // health below which they let go of it
    private const float GunRetryAfter = 12f;    // s after leaving it before they'd go back to it
    private const float GunOutOfArcSeen = 0.7f; // s with the enemy in view outside its limits before they let it go
    private const float GunOutOfArcHeard = 3f;  // s with the threat only thought to be out there
    private const float GunHoldMin = 18f;       // s holding it on where the enemy went, plus up to 10 s
    private const float GunSuppress = 2.5f;     // s of firing on where the enemy went, once they're gone
    private const float GunLineUpCos = 0.035f;  // rad off the target the barrel can be and fire
    private const float GunBagsHold = 3.2f;     // m: the line to the target stops this near the muzzle: the bags are in the way
    private const float GunBlockedLeave = 2.5f; // s of that with the enemy in view before they give it up
    private const float GunReloadTime = 5.5f;   // s: as long as the ManGunReload clip
    private const float GunBeltOn = 3.2f;       // s into it when the new belt is on
    private const float DrawAkTime = 1.6f;      // s: as long as DrawRifleBack
    private const int BurstMin = 5, BurstMax = 11;

    // A turret's gunner: long bursts with hardly a pause, on where the enemy went for a good while after; down behind the
    // turret's armour when they're hit, when rounds crack past, while they change belts, and now and then between bursts.
    private const int CrewBurstMin = 14, CrewBurstMax = 28;
    private const float CrewSuppress = 7f;      // s firing on where the enemy went, once they're gone
    private const float CrewDuckDepth = 0.8f;   // m down into the hatch: the head under the turret's rim
    private const float CrewDuckLevel = 0.3f;   // ducked further than this: no firing

    /// <summary>What a hit does to a turret's gunner, of what it would do to anyone else (Npcs.Hit).</summary>
    public const float CrewDamage = 0.45f;

    private float _crewDuck;                    // 0 up on the gun .. 1 down in the hatch (as shown)
    private float _crewDuckLeft;                // s more to stay down
    private float _crewHealthSeen = float.NaN;
    private float _crewSuppressionSeen = float.NaN;

    private enum OnGunState { No, Going, On }

    /// <summary>Debug: BRUTALFPS_NESTMAN=1 starts the nest's man on the gun; =hold,yawDeg,pitchDeg also keeps him there, with it laid that far off its line, not firing.</summary>
    private static readonly string[] NestManTest = (Environment.GetEnvironmentVariable("BRUTALFPS_NESTMAN") ?? "").ToLowerInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries);

    private OnGunState _gunState;
    private GunNest? _nest;
    private float _gunLook;             // s until they next think about going for it
    private float _gunNotBefore;        // Senses.Time before which they won't go back to it
    private float _gunBlocked;          // s a searcher has been stopped on the way to it / the bags have been in the way
    private float _seatEase;            // s left of settling into place behind it
    private float _outOfArc;            // s the enemy has been outside its limits
    private float _gunHoldLeft;         // s left holding it on where they went
    private float _flashOnGun;          // s left firing on a muzzle flash
    private float _walkIn;              // rad: how far off the first rounds of this burst land, walking onto the target
    private float3 _walkDir;
    private int _burstShots;
    private bool _gunEngaged;           // has fired on this target already (later bursts need less walking in)
    private bool _friendInWay;
    private bool _beltOn;
    private bool _testHold, _testSeated;
    private float _unslingLeft = -1f;   // s until the hand finds the AK on their back
    private bool _drawingAk;
    private int[] _chestNodes = [];

    /// <summary>The man whose post is the nest's gun: he goes to it first, from further off, even for a sound.</summary>
    public bool PostedAtGun { get; set; }

    /// <summary>On the tripod's gun now.</summary>
    public bool OnGun => _gunState == OnGunState.On;

    /// <summary>The gun they're firing: the M249 while they're on it, otherwise their own.</summary>
    public NpcGun GunInUse => OnGun ? NpcGun.M249 : Gun;

    private protected override bool HandsOnMount => OnGun;

    /// <summary>The mount they're on or going to (null: none).</summary>
    public GunNest? Mount => _nest;

    /// <summary>A vehicle turret's gunner (Game/LevelArmouredCar.cs): they stand in its hatch on its gun and never leave it.</summary>
    public bool TurretCrew { get; set; }

    private GunNest? _crewMount;

    /// <summary>On a vehicle's turret: none of the reasons to get off it apply (there's nowhere to go).</summary>
    private bool Crew => _crewMount is not null && ReferenceEquals(_nest, _crewMount);

    /// <summary>The nearest mount a guard could run to (not a vehicle's: only its crew use those).</summary>
    private GunNest? NearestFreeNest(NpcSenses s)
    {
        GunNest? best = null;
        var bestDist = float.MaxValue;
        foreach (var n in s.Nests)
        {
            if (n.Crewed || !n.Free)
                continue;
            var d = Distance(Position, n.Cradle);
            if (d < bestDist)
                (best, bestDist) = (n, d);
        }

        return best;
    }

    /// <summary>A turret's gunner: every frame from here (Npc.Update). Up into the hatch first, then they fight or watch from it.</summary>
    private protected override bool CrewHold(float dt, NpcSenses s, out bool moving)
    {
        moving = false;
        if (!TurretCrew)
            return false;
        if (_crewMount is null)
        {
            // The nearest vehicle's turret: the one they were put in.
            var bestDist = float.MaxValue;
            foreach (var n in s.Nests)
                if (n.Crewed && Distance(Position, n.Pivot) is var d && d < bestDist)
                    (_crewMount, bestDist) = (n, d);
            if (_crewMount is null)
                return false;   // (no turret here: they're just a guard)
        }

        if (!OnGun || !ReferenceEquals(_crewMount.Gunner, this))
        {
            _crewMount.TakeCrew(this);
            _nest = _crewMount;
            _gunState = OnGunState.No;
            GetOnGun(s);
            _seatEase = 0f;
            Position = _crewMount.Seat(_crewMount.Yaw, Position.Y);
            Heading = _crewMount.Yaw;
        }

        moving = CrewStates(dt, s);
        return true;
    }

    /// <summary>On watch in the hatch: the turret swung slowly to and fro over its arc, or onto something they've heard.</summary>
    private protected override void CrewWatch(float dt, NpcSenses s)
    {
        var nest = _crewMount!;
        float3 at;
        if (State is NpcState.Wary or NpcState.Startled)
            at = Threat + new float3(0, 1.3f, 0);
        else
        {
            var yaw = nest.Facing + 1.1f * float.Sin(s.Time * 0.13f + 1.7f);
            at = nest.Pivot + new float3(float.Sin(yaw), 0, float.Cos(yaw)) * 40f;
        }

        nest.TraverseTo(at, dt, 0.5f);
        UpdateCrewDuck(dt);
        Settle(nest, dt);
    }

    /// <summary>
    /// A turret's gunner ducking down into the hatch and coming back up: down when they're hit (for a second or two), maybe when
    /// a round cracks past, while a new belt goes on, while they're dazzled; up again to fire.
    /// </summary>
    private void UpdateCrewDuck(float dt)
    {
        if (!Crew)
            return;
        if (float.IsNaN(_crewHealthSeen))
            (_crewHealthSeen, _crewSuppressionSeen) = (Health, _suppressedAt);
        if (Health < _crewHealthSeen - 0.5f)
        {
            _crewDuckLeft = float.Max(_crewDuckLeft, 1.3f + 0.8f * (float)_random.NextDouble());
            Log.Write($"[NPC] {Name}: hit ({Health:0} left): down behind the turret");
        }
        else if (_suppressedAt != _crewSuppressionSeen && _random.NextDouble() < 0.3)
            _crewDuckLeft = float.Max(_crewDuckLeft, 0.6f + 0.5f * (float)_random.NextDouble());   // (a near miss: a moment's ducking)

        (_crewHealthSeen, _crewSuppressionSeen) = (Health, _suppressedAt);
        _crewDuckLeft -= dt;
        var down = _crewDuckLeft > 0f || _reloadLeft > 0f || Blinded;
        _crewDuck = down ? float.Min(1f, _crewDuck + 5f * dt) : float.Max(0f, _crewDuck - 2.5f * dt);
    }

    /// <summary>A turret's gunner down in the hatch, too far to fire.</summary>
    private bool CrewDucked => Crew && _crewDuck > CrewDuckLevel;

    /// <summary>Whether a test wants anyone on the gun from the start (<see cref="TestSeat"/>).</summary>
    public static bool NestManWanted => NestManTest.Length > 0;

    /// <summary>BRUTALFPS_NESTMAN: the nest's man is put on the gun, once.</summary>
    public void TestSeat(NpcSenses s)
    {
        if (!PostedAtGun || !Alive || _testSeated || s.Nests.Find(static n => !n.Crewed) is not { Free: true } nest)
            return;
        if (State != NpcState.Fighting)
            SetState(NpcState.Fighting);
        _testSeated = true;
        Threat = nest.Cradle with { Y = Position.Y } + new float3(float.Sin(nest.Facing), 0, float.Cos(nest.Facing)) * 20f;
        _lastSeen = Threat + new float3(0, 1.3f, 0);
        if (!nest.Claim(this))
            return;
        _nest = nest;
        GetOnGun(s);
        if (NestManTest[0] != "hold")
        {
            // (=1,rounds: with that few left on the belt.)
            if (NestManTest.Length > 1 && int.TryParse(NestManTest[1], out var rounds))
                while (nest.Rounds > rounds && nest.TakeRound()) { }
            return;
        }

        float Degrees(int i) => NestManTest.Length > i && float.TryParse(NestManTest[i], System.Globalization.CultureInfo.InvariantCulture, out var v) ? float.DegreesToRadians(v) : 0f;
        _testHold = true;
        nest.Lay(nest.Facing + Degrees(1), Degrees(2));
        _seatEase = 0f;
        Position = nest.Seat(nest.Yaw, Position.Y);
        Heading = nest.Yaw;
    }

    // ------------------------------------------------------------------------------------------------ getting to it

    /// <summary>Whether they're on their way to it now, having just thought of it if not.</summary>
    private bool WantsGun(float dt, NpcSenses s)
    {
        if (_gunState == OnGunState.Going)
            return true;
        _gunLook -= dt;
        if (_gunLook > 0f)
            return false;
        _gunLook = 0.4f;
        if (TurretCrew || NearestFreeNest(s) is not { } nest || s.Time < _gunNotBefore || Climbing || Blinded || _slung || Faction != Faction.Hostile
            || Equipment == Equipment.Lmg || Zone != Level.ZoneAt(nest.Cradle) || Health < (PostedAtGun ? GunHurt : GunHurt + 15f) || (!PostedAtGun && !_targetVisible))
            return false;
        var threat = _targetVisible ? _lastSeen : ThreatAt;
        var seat = nest.Seat(nest.Yaw, Position.Y);
        var way = Distance(Position, seat);
        var fromGun = Distance(threat with { Y = nest.Cradle.Y }, nest.Cradle);
        // (A defensive one only if it's right by them: they hold their ground.)
        if (way > (PostedAtGun ? GunPostRange : float.Min(GunCallRange, CoverReachFor)) || fromGun < GunMinRange || fromGun > GunRange
            || !nest.InArc(threat) || Distance(threat with { Y = Position.Y }, seat) < way + 3f || !nest.Claim(this))
            return false;
        _nest = nest;
        _gunState = OnGunState.Going;
        _gunBlocked = 0;
        _burstLeft = 0;
        Log.Write($"[NPC] {Name}: going for the M249, {way:0.0} m ({TargetName} {fromGun:0.0} m from it, {float.RadiansToDegrees(float.Abs(nest.OffLine(threat))):0} deg off its line)");
        return true;
    }

    /// <summary>A frame of going to the gun, at a run, not firing. Returns whether they moved.</summary>
    private bool GoToGun(float dt, float runSpeed, NpcSenses s)
    {
        var nest = _nest!;
        if (!nest.ClaimedBy(this))
        {
            Log.Write($"[NPC] {Name}: {(nest.Carried ? "the player's got the M249" : "someone else is on the M249")}: back to the fight");
            GunGoingOff(s, 4f);
            return false;
        }

        var seat = nest.Seat(nest.Yaw, Position.Y);
        var threat = _targetVisible ? _lastSeen : ThreatAt;
        if (Distance(threat with { Y = nest.Cradle.Y }, nest.Cradle) < GunTooClose + 1f)
        {
            Log.Write($"[NPC] {Name}: {TargetName} is at the M249: back to the fight");
            GunGoingOff(s, GunRetryAfter);
            return false;
        }

        var left = Distance(Position, seat);
        if (left < 0.5f || (_gunBlocked > 1f && left < 1.3f))
        {
            GetOnGun(s);
            return false;
        }

        if (_gunBlocked > 5f)
        {
            Log.Write($"[NPC] {Name}: can't get to the M249 ({left:0.0} m away): back to the fight");
            GunGoingOff(s, GunRetryAfter * 2f);
            return false;
        }

        if (GoTowards(seat, runSpeed * (1f - _limp), dt, TurnRate * 2f, s))
        {
            _gunBlocked = 0;
            AddTrail();
            return true;
        }

        _gunBlocked += dt;
        return false;
    }

    private void GunGoingOff(NpcSenses s, float wait)
    {
        _nest?.Release(this);
        _nest = null;
        _gunState = OnGunState.No;
        _gunNotBefore = s.Time + wait;
    }

    /// <summary>Behind it: the AK on their back, both hands on it.</summary>
    private void GetOnGun(NpcSenses s)
    {
        var nest = _nest!;
        if (!nest.Man(this))
        {
            GunGoingOff(s, 4f);
            return;
        }

        _gunState = OnGunState.On;
        if (_chest < 0)
            ScriptSlingReady();
        if (_chestNodes.Length == 0)
            _chestNodes = _skeleton.Subtree(_chest);
        _slung = true;
        _unslingLeft = -1f;
        _drawingAk = false;
        _animator.Stop(AimClip, 0.15f);
        _animator.Stop(FireClip, 0.1f);
        _animator.Stop(ReloadClip, 0.15f);
        if (_reloadLeft > 0)
            Ammo = Gun.Magazine;
        _reloadLeft = 0;
        _reAim = -1f;
        _burstLeft = 0;
        _sidestepLeft = 0;
        _fallBackLeft = 0;
        _lineUp = 0;
        _armTuck = 0;
        _outOfArc = 0;
        _gunBlocked = 0;
        _flashOnGun = 0;
        _gunEngaged = false;
        _shotOrder = ShotOrder.None;
        DropFlareThrow(0.1f);
        _seatEase = 0.5f;
        _gunHoldLeft = GunHoldMin + 10f * (float)_random.NextDouble();
        _animator.PlayFullBody("ManGun", 0.35f, 0.3f);
        Log.Write($"[NPC] {Name}: on the M249, {nest.Rounds} rounds on the belt ({TargetName}, {Distance(Position, _lastSeen):0.0} m)");
    }

    /// <summary>Off it, with why: their hand goes over their shoulder for the AK (unless they've <paramref name="fallen"/> or been dazzled), and it's free for anyone else.</summary>
    private void LeaveGun(string why, NpcSenses? s, bool fallen = false)
    {
        var nest = _nest;
        var wasOn = _gunState == OnGunState.On;
        _gunState = OnGunState.No;
        _nest = null;
        nest?.Release(this);
        _gunNotBefore = (s?.Time ?? _gunNotBefore) + GunRetryAfter;
        if (!wasOn)
            return;
        _animator.Stop("ManGun", 0.3f);
        _animator.Stop("ManGunReload", 0.2f);
        _reloadLeft = 0;
        _burstLeft = 0;
        _lineUp = 0;
        _hunt = Hunt.Engage;
        _unslingLeft = -1f;
        _drawingAk = false;
        Log.Write($"[NPC] {Name}: {why}: {(nest?.Rounds ?? 0)} rounds left on the M249");
        if (fallen)
        {
            _slung = false;
            return;
        }

        // Hand over the shoulder for the AK, and up with it (as if reloading: no firing till it's there).
        _animator.PlayOverlay("DrawRifleBack", _upperBody, 0.15f, 0f);
        _unslingLeft = DrawGrabTime;
        _reloadLeft = DrawAkTime;
        _drawingAk = true;
    }

    private void UpdateUnsling(float dt)
    {
        if (_unslingLeft < 0f)
            return;
        _unslingLeft -= dt;
        if (_unslingLeft < 0f)
            _slung = false;
    }

    // ------------------------------------------------------------------------------------------------ on it

    /// <summary>A frame of the fight from behind the gun. They don't walk: they go round the tripod with it.</summary>
    private bool UpdateOnGun(float dt, NpcSenses s)
    {
        var nest = _nest!;
        if (_testHold)
        {
            Settle(nest, dt);
            return false;
        }

        UpdateCrewDuck(dt);
        if (Blinded)
        {
            // (In a turret, they duck down behind the shield till they can see again: there's nowhere else to go.)
            if (Crew)
            {
                _burstLeft = 0;
                _lineUp = 0;
                Settle(nest, dt);
            }
            else
                LeaveGun("dazzled: let go of the M249", s, fallen: true);
            return false;
        }

        if (!Crew && Health < GunHurt)
        {
            LeaveGun($"hurt ({Health:0}): leaving the M249", s);
            return false;
        }

        _retarget -= dt;
        if (_retarget <= 0)
        {
            _retarget = RetargetEvery;
            PickTarget(s);
        }

        if (_shotOrder == ShotOrder.FireAtFlash && !_targetVisible)
        {
            _flashOnGun = FlashFireTime;
            Threat = new float3(_shotAt.X, Position.Y, _shotAt.Z);
            Log.Write($"[NPC] {Name}: saw a muzzle flash {Distance(Position, _shotAt):0.0} m away: swinging the M249 onto it");
        }

        _shotOrder = ShotOrder.None;
        _flashOnGun -= dt;
        if (_targetVisible)
        {
            _everSeen = true;
            _lostFor = 0;
            _calmLeft = CalmTime;
            _gunHoldLeft = GunHoldMin + 10f * (float)_random.NextDouble();
            Threat = new float3(_lastSeen.X, Position.Y, _lastSeen.Z);
        }
        else
        {
            _lostFor += dt;
            _gunHoldLeft -= dt;
        }

        var at = _targetVisible ? _lastSeen : _flashOnGun > 0f ? _shotAt : _everSeen && Distance(Threat, _lastSeen with { Y = Position.Y }) < 2.5f ? _lastSeen : Threat + new float3(0, 1.3f, 0);
        var flatDist = Distance(at with { Y = nest.Cradle.Y }, nest.Cradle);

        // Time to go? Round the side, or too close to get it down on them, or there's been nothing to fire at for a long time.
        if (!Crew && _targetVisible && flatDist < GunTooClose)
        {
            LeaveGun($"{TargetName} too close ({flatDist:0.0} m): leaving the M249", s);
            return false;
        }

        var off = nest.OffLine(at);
        _outOfArc = float.Abs(off) > nest.ArcHalf + 0.05f ? _outOfArc + dt : 0f;
        if (!Crew && _outOfArc > (_targetVisible ? GunOutOfArcSeen : GunOutOfArcHeard))
        {
            var flanked = _targetVisible && (flatDist < GunFlankRange || float.Abs(off) > float.Pi / 2);
            var seen = _targetVisible ? TargetName : "the threat";
            LeaveGun($"{(flanked ? "flanked" : "out of its arc")}: {seen} {flatDist:0.0} m away, {float.RadiansToDegrees(float.Abs(off)):0} deg off its line: leaving the M249", s);
            return false;
        }

        if (_gunHoldLeft <= 0f)
        {
            if (Crew)
            {
                // Back on watch, still in the hatch (Npc.CrewStates).
                Log.Write($"[NPC] {Name}: nothing to fire at for {_lostFor:0} s: back on watch in the turret");
                SetState(NpcState.Calm);
                return false;
            }

            LeaveGun($"nothing to fire at for {_lostFor:0} s: leaving the M249", s);
            return false;
        }

        // Swing it round, and themselves with it.
        var error = nest.TraverseTo(at, dt);
        Settle(nest, dt);
        _gunNotBefore = s.Time + GunRetryAfter;

        // The belt.
        if (_reloadLeft > 0)
        {
            _reloadLeft -= dt;
            if (!_beltOn && _reloadLeft < GunReloadTime - GunBeltOn)
            {
                _beltOn = true;
                nest.NewBelt();
            }

            if (_reloadLeft <= 0)
                Log.Write($"[NPC] {Name}: new belt on the M249");
            return false;
        }

        if (_staggerLeft > 0 && !Crew)
            return false;

        // Down in the hatch: the turret still comes round (they crank it from down there), and they're up and firing the
        // moment they come back up, lined up already.
        if (CrewDucked)
        {
            _burstLeft = 0;
            _lineUp = error < GunLineUpCos ? _lineUp + dt : 0f;
            return false;
        }

        // Bursts: lined up, on someone they can see, or on where they went for a moment after (a turret's gunner: for a good
        // while after, hosing it down), or on a flash.
        _lineUp = error < GunLineUpCos ? _lineUp + dt : _burstLeft > 0 ? _lineUp : 0f;
        var firing = _targetVisible || _flashOnGun > 0f || (_everSeen && _lostFor < (Crew ? CrewSuppress : GunSuppress));
        var reaction = (Crew ? 0.5f : 0.9f) - 0.4f * Profile.Accuracy;
        if (firing && _lineUp > reaction && _cooldown <= 0 && float.Abs(off) <= nest.ArcHalf + 0.02f && flatDist < GunRange)
            ShootGun(nest, at, s);
        else if (!firing)
        {
            _burstLeft = 0;
            _gunBlocked = 0;
        }

        return false;
    }

    /// <summary>Into place behind the gun as it stands, and facing along it.</summary>
    private void Settle(GunNest nest, float dt)
    {
        var seat = nest.Seat(nest.Yaw, Position.Y) - new float3(0, Crew ? CrewDuckDepth * _crewDuck : 0f, 0);   // (a turret's gunner ducked down in the hatch)
        if (_seatEase > 0f)
        {
            _seatEase -= dt;
            Position += (seat - Position) * (1f - float.Exp(-11f * dt));
            Heading = Wrap(Heading + Wrap(nest.Yaw - Heading) * (1f - float.Exp(-11f * dt)));
        }
        else
        {
            Position = seat;
            Heading = nest.Yaw;
        }
    }

    private void ShootGun(GunNest nest, float3 at, NpcSenses s)
    {
        if (s.Fire is null)
            return;
        var muzzle = nest.Muzzle;
        var to = at - muzzle;
        var dist = Length(to);
        if (dist < 0.3f)
            return;
        var dir = to / dist;

        // The bags right there in the way: they can't get it down far enough. Anything further off, they fire into.
        if (s.Clear(muzzle, at) * dist < GunBagsHold)
        {
            _cooldown = 0.15f;
            _burstLeft = 0;
            _gunBlocked += 0.15f;
            if (_gunBlocked > GunBlockedLeave && _targetVisible && !Crew)
                LeaveGun($"the bags are in the way of {TargetName} ({dist:0.0} m): leaving the M249", s);
            return;
        }

        _gunBlocked = 0;

        // The start of a burst: how many, and where the first rounds go. A tripod holds it steady: most of the trouble is
        // the first rounds walking onto them, wider the first time they fire on someone.
        var starting = _burstLeft <= 0;
        if (starting)
        {
            _burstLeft = Crew ? _random.Next(CrewBurstMin, CrewBurstMax + 1) : _random.Next(BurstMin, BurstMax + 1);
            _burstShots = 0;
            _walkIn = (_gunEngaged ? 0.05f : 0.09f) * (1.4f - Profile.Accuracy);
            var a = (float)_random.NextDouble() * float.Pi;
            var (u, _) = Basis(dir);
            _walkDir = u * float.Cos(a) - float3.UnitY * float.Sin(a);
            _gunEngaged = true;
        }

        var cone = (0.045f + 0.05f * (1f - Profile.Accuracy) + 0.0015f * _burstShots) * (1f + _limp) * (_targetVisible ? 1f : FlashCone);

        // Not through a friend (one in front of the target: they fire on past those beyond it, or they'd never fire at all).
        if (FriendInLine(muzzle, dir, dist, cone, s, beyond: 1f))
        {
            if (!_friendInWay)
                Log.Write($"[NPC] {Name}: holding the M249's fire: a friend in the way of {TargetName}");
            _friendInWay = true;
            _burstLeft = 0;
            _cooldown = 0.25f;
            return;
        }

        _friendInWay = false;

        if (!nest.TakeRound())
        {
            _burstLeft = 0;
            _reloadLeft = GunReloadTime;
            _beltOn = false;
            nest.LiftCover(GunReloadTime - 1.2f);
            _animator.PlayOverlay("ManGunReload", _upperBody, 0.2f, 0.25f);
            Log.Write($"[NPC] {Name}: the M249's belt is out: reloading");
            return;
        }

        if (starting)
            Log.Write($"[NPC] {Name}: M249 burst of {_burstLeft} at {(_targetVisible ? TargetName : "where they went")} ({dist:0.0} m), {nest.Rounds} on the belt");

        dir = Scatter(float3.Normalize(dir + _walkDir * _walkIn), cone);
        _walkIn *= 0.72f;
        var drop = new float3(0, 0.5f * 9.81f * dist / NpcGun.M249.BulletSpeed, 0);
        s.Fire(this, muzzle, [dir * NpcGun.M249.BulletSpeed + drop]);
        _flash = 0.05f;
        nest.Kick();
        _burstShots++;
        _burstLeft--;
        _cooldown = _burstLeft > 0 ? NpcGun.M249.Interval
            : Crew ? 0.15f + 0.25f * (float)_random.NextDouble()
            : 0.55f + 0.7f * (float)_random.NextDouble() + 0.3f * (1f - Profile.Accuracy);
        if (_burstLeft <= 0 && Crew && _random.NextDouble() < 0.3)
            _crewDuckLeft = 0.5f + 0.5f * (float)_random.NextDouble();   // (a moment down behind the armour between bursts)
    }

    /// <summary>The upper body goes with the gun as it's laid up or down and kicks, about the tripod's head: the clip only has it level.</summary>
    private protected override void PoseOnMount()
    {
        if (_nest is not { } nest || _chestNodes.Length == 0 || _seatEase > 0.2f)
            return;
        var pivot = GunNest.PintleOnFigure;
        var turn = float4x4.CreateTranslation(-pivot) * float4x4.CreateRotationX(-nest.ShownPitch) * float4x4.CreateTranslation(pivot + new float3(0, 0, -nest.ShownBack));
        foreach (var n in _chestNodes)
            _world[n] = _world[n] * turn;
    }
}
