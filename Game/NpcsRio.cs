using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Rio's people (normal play): a hundred in the favela's alleys, fourteen along each of the seven and two out on the laje.
/// Sixty are civilians going about their business (walking a stretch of their alley and stopping to stand about); forty are
/// armed hostiles (a long gun or a sidearm, rolled as usual), watching their stretch of alley. On the bottom alley, where
/// the magenta portal brings you in, the gang are all at the far end, out of sight of the portal. When the shooting starts
/// the civilians make for the hiding places behind the open doors along the alleys (<see cref="Level.RioHidingPlaces"/>,
/// Game/NpcHiding.cs), and hurt hostiles fall back into them to regroup.
/// <para>Not with the stress test on (<see cref="StressTest"/>: it brings its own hundred), nor with BRUTALFPS_NORIOCROWD=1.</para>
/// </summary>
public sealed partial class Npcs
{
    private const string RioCivilianPrefix = "rio local ";
    private const string RioHostilePrefix = "rio gang ";
    private const int RioPerAlley = 14;

    private static readonly string[][] RioGangKit =
    [
        ["tshirt", "jeans", "sneakers", "cap"], ["longsleeve", "jeans", "sneakers"],
        ["tshirt", "trousers_work", "sneakers", "beanie"], ["hoodie", "jeans", "sneakers"],
    ];

    private void AddRioCrowd()
    {
        if (StressTest || Environment.GetEnvironmentVariable("BRUTALFPS_NORIOCROWD") == "1")
            return;
        int civilians = 0, hostiles = 0;
        for (var k = 0; k < 7; k++)
        {
            var y = k * 2.7f;
            var z = k * 9f;
            for (var i = 0; i < RioPerAlley; i++)
            {
                var n = k * RioPerAlley + i;
                var x = -22f + 48f * i / (RioPerAlley - 1) + Jitter(n, 0.8f);
                // Four in ten armed: on the bottom alley only the last four slots (more than 35 m from the portal).
                var armed = k == 0 ? i >= RioPerAlley - 4 : (i * 3 + k) % 7 < 3;
                var lane = z + 0.85f + Jitter(n + 101, 0.15f);   // (clear of the pots and gas bottles by the doors uphill)
                if (armed)
                {
                    var profile = n % 2 == 0 ? NpcProfile.Rifleman(0.35f + 0.03f * (n % 4)) : NpcProfile.Gunman(0.38f + 0.03f * (n % 4));
                    profile = profile with { Armoured = n % 5 == 0 };
                    if (hostiles == 0)
                        profile = NpcProfile.Commando();   // (the first: a commando, so Rio always has one)
                    var face = n % 2 == 0 ? float.Pi / 2 : -float.Pi / 2;   // along the alley, one way or the other
                    var x1 = float.Clamp(x + (n % 2 == 0 ? 2.5f : -2.5f), -26f, 26f);
                    Add($"{RioHostilePrefix}{++hostiles}", SkinTones[(n * 5 + 1) % SkinTones.Length], face, profile, RioGangKit[n % RioGangKit.Length],
                        new Waypoint(Level.RioAt(x, y, lane), 8f + n % 7, face),
                        new Waypoint(Level.RioAt(x1, y, lane), 5f + n % 5, face + float.Pi));
                }
                else
                {
                    // Walk a stretch of the alley and back, stopping at each end; every third just stands about.
                    var mood = n % 3 == 0 ? Temperament.Freezer : Temperament.Bolter;
                    var span = 3f + n % 5;
                    var x1 = float.Clamp(x + (n % 2 == 0 ? span : -span), k == 0 ? -20f : -26f, 26f);
                    var stands = n % 3 == 1;
                    Add($"{RioCivilianPrefix}{++civilians}", SkinTones[(n * 7 + 3) % SkinTones.Length], float.Atan2(x1 - x, 0f),
                        NpcProfile.Civilian(mood) with { Talks = stands },
                        null,
                        new Waypoint(Level.RioAt(float.Max(x, k == 0 ? -20f : -26f), y, lane), stands ? 25f : 3f + n % 6),
                        new Waypoint(Level.RioAt(x1, y, lane), stands ? 20f : 2f + n % 4));
                }
            }
        }

        // Two out on the laje, looking over the roofs.
        var laje = 16.2f - 0.05f;
        Add($"{RioCivilianPrefix}{++civilians}", SkinTones[2], 0f, NpcProfile.Civilian(Temperament.Bolter) with { Talks = true }, null,
            new Waypoint(Level.RioAt(5.2f, laje, 50.5f), 30f, float.Pi), new Waypoint(Level.RioAt(6.5f, laje, 49.5f), 20f, float.Pi));
        Add($"{RioCivilianPrefix}{++civilians}", SkinTones[4], 0f, NpcProfile.Civilian(Temperament.Freezer) with { Talks = true }, null,
            new Waypoint(Level.RioAt(7.2f, laje, 50.8f), 30f, float.Pi), new Waypoint(Level.RioAt(7.8f, laje, 51.6f), 25f, float.Pi));

        Log.Write($"[NPC] Rio: {civilians} civilians and {hostiles} armed hostiles in the favela's alleys");

        static float Jitter(int n, float amount) => ((n * 2654435761u >> 8) % 1000 / 999f * 2f - 1f) * amount;
    }

    /// <summary>
    /// Checks every Rio stop against the level (on the ground, not inside anything, a straight walk to the next) and logs the
    /// hiding places. Call once the level's collision exists.
    /// </summary>
    public void CheckRioCrowd(Level level)
    {
        var crowd = _people.Where(p => p.HomeZone == Zones.Rio && (p.Name.StartsWith(RioCivilianPrefix, StringComparison.Ordinal) || p.Name.StartsWith(RioHostilePrefix, StringComparison.Ordinal))).ToList();
        foreach (var place in level.RioHidingPlaces)
        {
            Log.Write($"[NPC] Rio hiding place: {place.Name}, door at ({place.Doorway.X:0.0}, {place.Doorway.Y - Level.RioOrigin.Y:0.0}, {place.Doorway.Z:0.0}), {place.Spots.Count} spots");
            foreach (var spot in place.Spots)
            {
                if (SpotProblem(level, spot) is { } problem)
                    Log.Write($"[WARN] {place.Name}: a spot at ({spot.X:0.00}, {spot.Z:0.00}) is bad: {problem}");
            }
        }

        if (crowd.Count == 0)
            return;
        var bad = 0;
        foreach (var npc in crowd)
        {
            var route = npc.Route;
            for (var i = 0; i < route.Count; i++)
            {
                var problem = SpotProblem(level, route[i].Position)
                              ?? (route.Count > 1 && !WalkClear(route[i].Position, route[(i + 1) % route.Count].Position) ? "the way to the next stop is blocked" : null);
                if (problem is null)
                    continue;
                bad++;
                Log.Write($"[WARN] {npc.Name} at ({route[i].Position.X:0.0}, {route[i].Position.Z:0.0}): {problem}");
            }
        }

        Log.Write($"[NPC] Rio: {crowd.Count} people, {level.RioHidingPlaces.Count} hiding places; {bad} bad spots on their routes");
    }

    /// <summary>A respawn: nobody holds a spot in a hiding place any more.</summary>
    private void ClearHidingPlaces()
    {
        foreach (var place in Senses.HidingPlaces)
            place.Clear();
    }
}
