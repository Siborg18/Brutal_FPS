using Brutal.Numerics;

namespace BrutalFps.Game;

/// <summary>The stress test only (Game/NpcsStressTest.cs): a shot fired on cue, whatever the fight would say.</summary>
public sealed partial class ArmedNpc
{
    /// <summary>
    /// Fires one round (or one load of pellets) at <paramref name="at"/> right now: no waiting for a line of sight, a
    /// clear shot past friends or the end of a cooldown, so everyone given the cue fires in the same frame. The usual
    /// fight carries on after it.
    /// </summary>
    public void StressVolley(NpcSenses s, float3 at)
    {
        if (!Alive || s.Fire is null || GunTaken)
            return;
        var muzzle = Muzzle;
        var to = at - muzzle;
        var dist = to.Length();
        if (dist < 0.3f)
            return;
        var dir = Scatter(to / dist, 0.02f);
        var velocities = new float3[Gun.Pellets];
        for (var i = 0; i < velocities.Length; i++)
            velocities[i] = (Gun.Pellets > 1 ? Scatter(dir, Gun.PelletCone) : dir) * Gun.BulletSpeed;
        s.Fire(this, muzzle, velocities);
        Ammo--;
        _flash = 0.05f;
        _animator.PlayOverlay(FireClip, _upperBody, 0.02f, 0f);
        _reAim = 0.28f;
    }
}
