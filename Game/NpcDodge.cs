using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// A frag or a flashbang, in flight or lying about, for people to get away from (<see cref="NpcSenses.Grenades"/>):
/// where it is, how it's moving, how long before it goes off and how far off it does harm.
/// <paramref name="Flash"/>: a flashbang (better to have your back to it), else a frag.
/// </summary>
public readonly record struct LiveGrenade(float3 Position, float3 Velocity, bool Resting, float FuseLeft, float DangerRadius, bool Flash);

/// <summary>
/// Getting clear of a grenade. Anyone who sees one coming their way (in front of them, in a clear line, and not through
/// a wall, a door or smoke) works out where it'll come down (<see cref="Landing"/>) and, if
/// that's close enough to hurt, takes a moment to react and then runs from it, whatever they were doing: out of its
/// reach, then they stop and wait for it with their back to it (a flashbang dazzles whoever's looking at it). Once it's
/// gone off they carry on as before. Nobody else does anything while they're getting clear: no shots, no talking.
/// Hearing one clatter down is not enough: someone not yet alerted turns to look at the sound, and only if they can
/// see it from there do they know it for a grenade.
/// </summary>
public partial class Npc
{
    private const float GrenadeSpotRange = 16f;  // m: a grenade further off than this isn't noticed
    private const float GrenadeLookEvery = 0.12f; // s between looks for one (when there are any about)
    private const float DodgeMargin = 1.5f;      // m beyond its reach before they feel safe
    private const float DodgeMaxTime = 4.5f;     // s at most getting clear
    private const float DodgeReactMin = 0.15f;   // s from seeing it to moving (plus up to DodgeReactSpread)
    private const float DodgeReactSpread = 0.35f;

    private float _grenadeLook;          // s to the next look
    private bool _dodging;
    private float _dodgeReact;           // s before they move
    private float _dodgeLeft;            // s until it'll have gone off (and a moment more)
    private float3 _dodgeFrom;           // where it's coming down, or lies
    private float _dodgeRadius;          // how far off it to get (its reach, and a margin)
    private bool _dodgeFlash;
    private float _dodgeSteer;

    /// <summary>Getting clear of a grenade (<see cref="Dodge"/>).</summary>
    public bool Dodging => _dodging;

    /// <summary>They've just seen a grenade coming and are about to get clear of it: drop whatever's in hand.</summary>
    private protected virtual void OnDodge(NpcSenses s) { }

    /// <summary>
    /// One frame of looking out for grenades and getting clear of one. Returns true while they're busy with it (then
    /// nothing else they do runs this frame); <paramref name="moved"/>: whether they moved.
    /// </summary>
    private bool Dodge(float dt, float runSpeed, NpcSenses s, out bool moved)
    {
        moved = false;
        if (!_dodging && s.Grenades.Count == 0)
            return false;

        _grenadeLook -= dt;
        if (_grenadeLook <= 0 && !Blinded && !Hidden)
        {
            _grenadeLook = GrenadeLookEvery * (0.75f + 0.5f * (float)_random.NextDouble());
            LookForGrenades(s);
        }

        if (!_dodging)
            return false;

        _dodgeLeft -= dt;
        if (_dodgeLeft <= 0 || !Alive)
        {
            _dodging = false;
            return false;
        }

        _dodgeReact -= dt;
        var away = Position - _dodgeFrom;
        away.Y = 0;
        var dist = float.Sqrt(float3.Dot(away, away));
        away = dist > 1e-3f ? away / dist : -Ahead;
        if (_dodgeReact > 0)
        {
            FaceTowards(_dodgeFrom, dt, 2.5f);   // (a glance at it)
            return true;
        }

        if (_staggerLeft > 0 || Blinded)
            return true;

        if (dist >= _dodgeRadius)
        {
            // Clear of it: wait for it with their back to it.
            FaceTowards(Position + away, dt, 2f);
            return true;
        }

        _dodgeSteer -= dt;
        if (_dodgeSteer <= 0)
        {
            (_fleeDir, _fleeClear) = Steer(away, s);
            _dodgeSteer = 0.25f;
        }

        moved = MoveAlong(_fleeDir, runSpeed * (1f - _limp), dt, FleeTurnRate * 1.3f, s);
        return true;
    }

    private void LookForGrenades(NpcSenses s)
    {
        var eye = Position + new float3(0, EyeHeight, 0);
        var ahead = Ahead;
        foreach (var g in s.Grenades)
        {
            if (g.FuseLeft <= 0.05f)
                continue;
            var to = g.Position - eye;
            var dist = DodgeLength(to);
            if (dist > GrenadeSpotRange)
                continue;
            // Only a grenade they can actually see counts: in front of them, in a clear line (not through a wall, a
            // shut door or smoke). Hearing one clatter down only turns their head (GameSounds.GrenadeBounce ->
            // Npcs.HearNoise -> Noticed); once they're facing it, this picks it up on a later look.
            if (dist < 1e-3f || float3.Dot(to / dist, ahead) < SightCos || s.Visibility(eye, g.Position, 0.95f) <= 0f)
                continue;

            var landing = Landing(g, Position.Y);
            var off = landing - Position;
            if (float.Abs(off.Y) > 3f)
                continue;   // (another floor)
            off.Y = 0;
            var radius = g.DangerRadius + DodgeMargin;
            var offDist = DodgeLength(off);
            if (offDist >= radius)
                continue;
            if (_dodging && Distance2(landing, Position) >= Distance2(_dodgeFrom, Position))
                continue;   // (already running from a nearer one)

            var first = !_dodging;
            _dodging = true;
            _dodgeFrom = landing;
            _dodgeRadius = radius;
            _dodgeFlash = g.Flash;
            _dodgeLeft = float.Min(g.FuseLeft + 0.3f, DodgeMaxTime);
            _dodgeSteer = 0;
            if (!first)
                continue;
            _dodgeReact = DodgeReactMin + DodgeReactSpread * (float)_random.NextDouble();
            Log.Write($"[NPC] {Name}: saw a {(g.Flash ? "flashbang" : "grenade")} coming down {offDist:0.0} m away, {g.FuseLeft:0.0} s to go: getting clear");
            OnDodge(s);
            if (State is NpcState.Calm or NpcState.Returning or NpcState.Wary)
                Alarm(landing, Stimulus.Panic);
        }
    }

    private static float DodgeLength(float3 v) => float.Sqrt(float3.Dot(v, v));

    /// <summary>Where a grenade will come down: at the height of <paramref name="floor"/>, if it's in the air (walls and bounces aside), else where it lies.</summary>
    private static float3 Landing(in LiveGrenade g, float floor)
    {
        if (g.Resting)
            return g.Position;
        var p = g.Position;
        var v = g.Velocity;
        const float Step = 0.04f;
        for (var t = 0f; t < 3f; t += Step)
        {
            var next = p + v * Step;
            v.Y -= 9.81f * Step;
            if (v.Y < 0 && next.Y <= floor + 0.05f)
                return next;
            p = next;
        }

        return p;
    }
}
