using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The stress test (on in the StressTest build, or with BRUTALFPS_STRESS=1: see <see cref="StressTest"/>): the moment the player steps into Rio,
/// <see cref="StressCount"/> armed hostiles appear at once, spread along the favela's alleys, and
/// <see cref="StressVolleyAfter"/> seconds later every one of them fires in the same frame (at the player, who takes no
/// harm while it's on). After that they fight on as usual. It happens again after a respawn, the next time the player
/// comes into Rio.
/// </summary>
public sealed partial class Npcs
{
    /// <summary>
    /// Whether the stress test is on: always in the StressTest build (STRESS_TEST), otherwise with BRUTALFPS_STRESS=1 (the
    /// "Stress test" launch profile in Visual Studio).
    /// </summary>
    public static readonly bool StressTest =
#if STRESS_TEST
        true;
#else
        Environment.GetEnvironmentVariable("BRUTALFPS_STRESS") == "1";
#endif

    public const int StressCount = 100;
    public const float StressVolleyAfter = 1f;

    private int _stressLife = int.MinValue;     // the life (PlayerController.Respawns) they last appeared in
    private long _stressVolleyAt = -1;           // when the volley goes (a Stopwatch timestamp: real time, since game time is held back in slow frames), -1 once it has
    private readonly List<ArmedNpc> _stressGunmen = [];

    private void UpdateStressTest(PlayerController player)
    {
        if (_stressLife != player.Respawns && player.Alive && Level.ZoneAt(player.Position) == Zones.Rio)
        {
            _stressLife = player.Respawns;
            SpawnStressGunmen(player.Position);
            _stressVolleyAt = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(StressVolleyAfter * System.Diagnostics.Stopwatch.Frequency);
        }

        if (_stressVolleyAt >= 0 && System.Diagnostics.Stopwatch.GetTimestamp() >= _stressVolleyAt)
        {
            _stressVolleyAt = -1;
            var target = player.Position + new float3(0, 1.2f, 0);
            var fired = 0;
            foreach (var gunman in _stressGunmen)
            {
                if (!gunman.Alive)
                    continue;
                gunman.StressVolley(Senses, target);
                fired++;
            }
            Log.Write($"[STRESS] Volley: {fired} gunmen fired at once");
        }
    }

    /// <summary>The gunmen: along the middle of each of Rio's seven alleys, every 3.5 m, none right on top of the player.</summary>
    private void SpawnStressGunmen(float3 player)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        _stressGunmen.Clear();
        for (var k = 0; k < 7 && _stressGunmen.Count < StressCount; k++)
        {
            for (var x = -24.5f; x <= 25f && _stressGunmen.Count < StressCount; x += 3.5f)
            {
                var at = Level.RioAt(x, k * 2.7f, k * 9f + 1f);
                if (float3.Distance(at, player) < 4f)
                    continue;
                var n = _stressGunmen.Count;
                var profile = n % 2 == 0 ? NpcProfile.Rifleman(0.5f) : NpcProfile.Gunman(0.5f);
                Add($"stress {n + 1}", SkinTones[(n * 5 + 2) % SkinTones.Length], 0f, profile, MotelKit[n % MotelKit.Length],
                    new Waypoint(at, float.PositiveInfinity));
                if (_people[^1] is not ArmedNpc gunman)
                    continue;
                gunman.ScriptEngage(player, StressVolleyAfter + 0.3f);   // (game time: the volley first, then the fight's own fire)
                _stressGunmen.Add(gunman);
            }
        }
        Log.Write($"[STRESS] The player's in Rio: {_stressGunmen.Count} gunmen spawned in {System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:0} ms; they all fire in {StressVolleyAfter:0.0} s");
    }

    private long _profLast;

    /// <summary>The stress test, once a second in Rio: how many are up, down and in a ragdoll, what the physics holds, and (BRUTALFPS_PERF=1) where the NPCs' time went.</summary>
    public void LogStressStatus(int bodies, int debris, int bullets)
    {
        int alive = 0, ragdolls = 0, fighting = 0;
        Span<int> tiers = stackalloc int[5];
        foreach (var npc in _people)
        {
            tiers[(int)npc.Tier]++;
            if (npc.Alive)
                alive++;
            if (npc.Ragdolled)
                ragdolls++;
            if (npc.Alive && npc.State == NpcState.Fighting)
                fighting++;
        }

        var prof = "";
        if (FrameStats.Enabled)
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var span = _profLast == 0 ? 1.0 : (now - _profLast) / (double)System.Diagnostics.Stopwatch.Frequency;
            _profLast = now;
            double Ms(ref long ticks) { var ms = ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / span; ticks = 0; return ms; }
            prof = $"; poses {Npc.PosesDone} done, {Npc.PosesSkipped} skipped, {DormantSkipped} dormant updates skipped, {FrozenSkipped} frozen; per second: senses {Ms(ref Npc.ProfSense):0} ms, behaviour {Ms(ref Npc.ProfBehave):0} ms, animation {Ms(ref Npc.ProfAnimate):0} ms, look+arms {Ms(ref Npc.ProfArms):0} ms";
            Npc.PosesDone = Npc.PosesSkipped = DormantSkipped = FrozenSkipped = 0;
        }

        Log.Write($"[STRESS] {alive} alive ({fighting} fighting), {ragdolls} ragdolls; tiers: {tiers[0]} attacking, {tiers[1]} searching, {tiers[2]} idle, {tiers[3]} far, {tiers[4]} dormant; {bodies} physics bodies, {debris} debris, {bullets} bullets in flight{prof}");
    }
}
