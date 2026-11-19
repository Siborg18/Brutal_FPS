using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Firing into a smoke screen. When the enemy they were fighting has gone from sight and it's smoke that hides where
/// they went (<see cref="NpcSenses.Smoke"/>: no wall in the way, just the cloud), they reckon they're in there: they
/// stop and put a few bursts into it, sweeping either side of where they lost them, then go back to holding aim on it
/// or searching, and a few seconds later, if the smoke's still there, do it again (a few times at most in a hunt). A
/// shot fired from in the smoke lights it up (<see cref="SeesFlash"/>), and that they fire straight at.
/// </summary>
public sealed partial class ArmedNpc
{
    private const float SmokeSuspect = 0.3f;       // smoke this thick (NpcSenses.Smoke) between them and where they lost the enemy: they're in it
    private const float SmokeSuspectTime = 30f;    // s after losing them: later than this, the smoke could be hiding anything
    private const float SmokeFireRange = 45f;      // m: no further than this
    private const float SmokeLookEvery = 0.4f;     // s between looks at it
    private const float SmokeFireTime = 2.5f;      // s of firing into it (plus up to 1.5 s)
    private const float SmokeRestTime = 3f;        // s before another volley (plus up to 3 s)
    private const int SmokeVolleys = 4;            // volleys at most in one hunt
    private const float SmokeSweep = 1.6f;         // m either side of the spot they sweep
    private const float SmokeLineUp = 0.25f;       // s facing it before the first shot

    private float _smokeLook;           // s to the next look at it
    private float _smokeLeft;           // s of firing left
    private float _smokeRest;           // s before they'll fire into it again
    private int _smokeVolleys;          // volleys fired in this hunt
    private float3 _smokeAt;            // where in it they're aiming
    private Hunt _smokeThen;            // what they were doing before (Hold or Search), to go back to

    /// <summary>Whether they reckon the enemy they lost is in the smoke there, and it's time to fire into it.</summary>
    private bool SuspectsSmoke(float dt, NpcSenses s, float3 eye)
    {
        _smokeRest -= dt;
        _smokeLook -= dt;
        if (_smokeLook > 0f || _smokeRest > 0f)
            return false;
        _smokeLook = SmokeLookEvery * (0.75f + 0.5f * (float)_random.NextDouble());
        if (!_everSeen || _lostFor > SmokeSuspectTime || _smokeVolleys >= SmokeVolleys || _chasing is not null || _watching is not null
            || _targetNpc is { Alive: false } || (!_targetIsPlayer && _targetNpc is null) || GunTaken || Ammo + Gun.Magazine <= 0)
            return false;
        var at = _vanished;
        var dist = Distance(eye, at);
        if (dist > SmokeFireRange || dist < 2.5f)
            return false;
        // Only when it's the smoke that hides them: not a wall, not round a corner.
        return s.Clear(eye, at) > 0.95f && s.Smoke(eye, at) >= SmokeSuspect;
    }

    private void StartFireIntoSmoke(NpcSenses s, float3 eye)
    {
        _smokeThen = _hunt;
        _hunt = Hunt.FireIntoSmoke;
        _smokeAt = _vanished;
        _smokeLeft = SmokeFireTime + 1.5f * (float)_random.NextDouble();
        _smokeVolleys++;
        _lineUp = 0;
        _burstLeft = 0;
        Log.Write($"[NPC] {Name}: {TargetName} went into the smoke {Distance(eye, _smokeAt):0.0} m away (smoke {s.Smoke(eye, _smokeAt):0.00}): firing into it ({_smokeVolleys}/{SmokeVolleys})");
    }

    /// <summary>One frame of firing into the smoke: standing, sweeping it. Returns false: they don't move.</summary>
    private bool UpdateFireIntoSmoke(float dt, NpcSenses s, float3 eye)
    {
        _smokeLeft -= dt;
        FaceTowards(_smokeAt, dt, 2.2f);
        var to = _smokeAt - eye;
        var flat = new float3(to.X, 0, to.Z);
        var len = Length(flat);
        var facing = len > 1e-3f ? float3.Dot(flat / len, Ahead) : 1f;
        _lineUp = facing > ShootCos * 0.995f ? _lineUp + dt : 0f;
        if (_reloadLeft <= 0 && _lineUp > SmokeLineUp && _cooldown <= 0 && len < SmokeFireRange)
        {
            // Somewhere about the spot: across it, a little up and down (standing or crouched).
            var across = len > 1e-3f ? new float3(-flat.Z / len, 0, flat.X / len) : float3.UnitX;
            var r = (float)_random.NextDouble() * 2f - 1f;
            var deep = ((float)_random.NextDouble() * 2f - 1f) * 1.2f;
            _lastSeen = _smokeAt + across * (r * SmokeSweep) + (len > 1e-3f ? flat / len : float3.Zero) * deep
                        + new float3(0, -0.5f + 0.7f * (float)_random.NextDouble(), 0);
            _firingBlind = true;
            Shoot(s);
            _firingBlind = false;
        }

        // Done (or the smoke's thinned away): back to holding aim on it, or searching.
        if (_smokeLeft <= 0f || s.Smoke(eye, _smokeAt) < SmokeSuspect * 0.5f)
        {
            _hunt = _smokeThen;
            _lastSeen = _vanished = _smokeAt;
            _smokeRest = SmokeRestTime + 3f * (float)_random.NextDouble();
            _smokeLook = 0f;
            _lineUp = 0;
            Log.Write($"[NPC] {Name}: stopped firing into the smoke, {Ammo} rounds left: {(_hunt == Hunt.Hold ? "holding aim on it" : "searching")}");
        }

        return false;
    }
}
