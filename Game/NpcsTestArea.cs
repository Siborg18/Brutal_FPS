using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>The kinds of people the NPC test area has one of each of.</summary>
public enum TestNpcType { Civilian, Woman, PoliceOfficer, PrisonOfficer, Gunman, Rifleman, Commando }

/// <summary>
/// One of the NPC test area's people as the pause menu's NPC CONFIG sets them up (<see cref="NpcTestMenu"/>): whether
/// they're there, their gun and their personality. They stand at <see cref="Spot"/> in the factory's outbuilding.
/// </summary>
public sealed class TestNpcSlot(TestNpcType type, string label, float3 spot, NpcProfile profile)
{
    public TestNpcType Type { get; } = type;
    public string Label { get; } = label;
    public float3 Spot { get; } = spot;
    public Faction Faction { get; } = profile.Faction;
    public bool Woman { get; } = profile.Woman;
    public bool IsCommando { get; } = profile.IsCommando;

    public bool Spawned = true;
    public Equipment Equipment = profile.Equipment;
    public Aggression Aggression = profile.Aggression;
    public NpcStrategy Strategy = profile.Strategy;
    public Temperament Temperament = profile.Temperament;
    public LostSight OnLostSight = profile.OnLostSight;
    public float Accuracy = profile.Accuracy;
    public bool Armoured = profile.Armoured;

    /// <summary>What AUTO came out as, the last time they were spawned aggressive with no plan set.</summary>
    public NpcStrategy RolledStrategy = NpcStrategy.Auto;

    /// <summary>How many of them stand in the test area (1 .. <see cref="MaxCount"/>), in a little grid round <see cref="Spot"/>.</summary>
    public int Count = 1;
    public const int MaxCount = 9;

    /// <summary>The first one's name in the world (fixed, so they keep their clothes from one spawn to the next).</summary>
    public string Name => $"{Npcs.TestPrefix}{Label.ToLowerInvariant()}";

    /// <summary>The <paramref name="i"/>-th one's name (0 is <see cref="Name"/>; the rest "#2", "#3", ...).</summary>
    public string NameOf(int i) => i == 0 ? Name : $"{Name} #{i + 1}";

    /// <summary>Whether someone is one of this slot's people.</summary>
    public bool Owns(string name) => name == Name || name.StartsWith(Name + " #", StringComparison.Ordinal);

    /// <summary>Where the <paramref name="i"/>-th one stands: three across (north-south, 1.2 m apart), rows going back west from the spot.</summary>
    public float3 SpotOf(int i) => Spot + new float3(-1.2f * (i / 3), 0, (i % 3) switch { 0 => 0f, 1 => 1.2f, _ => -1.2f });
}

/// <summary>
/// The NPC test build (on in the NpcTest build, or with BRUTALFPS_NPCTEST=1: the "NPC testing" launch profile): nobody in
/// the world but one of each kind of person, inside the factory's outbuilding, and the player starts (and respawns) just
/// outside its doors. They take hits, bleed, flinch and fight as usual but never die (<see cref="Npc.Undying"/>), and nor
/// does the player. The pause menu's NPC CONFIG (<see cref="NpcTestMenu"/>) spawns and despawns them and sets their guns
/// and personalities; a respawn (+) brings them all back as configured.
/// </summary>
public sealed partial class Npcs
{
    /// <summary>Whether this is the NPC test build: always with NPC_TEST, otherwise with BRUTALFPS_NPCTEST=1.</summary>
    public static readonly bool NpcTest =
#if NPC_TEST
        true;
#else
        Environment.GetEnvironmentVariable("BRUTALFPS_NPCTEST") == "1";
#endif

    public const string TestPrefix = "test ";

    /// <summary>
    /// Where the player starts and respawns: outside the outbuilding, behind the blue Transit van parked across its north
    /// doorway (x -35.5 .. -33.5, z -13.5 .. -7.5: Level.BuildVans), facing it (west). The van hides you from that doorway, and
    /// the south one only looks out further south, so nobody inside sees you until you step out.
    /// </summary>
    public static readonly float3 TestPlayerSpawn = new(-32f, 0, -11.5f);
    public const float TestPlayerYaw = -float.Pi / 2;

    /// <summary>The outbuilding's floor plan (inside its walls).</summary>
    public static readonly Aabb TestArea = new(new float3(-63.62f, -1f, -17.62f), new float3(-38.38f, 20f, 3.62f));

    /// <summary>Everyone the test area can have, as configured. (Kept across respawns: they come back as they're set.)</summary>
    public IReadOnlyList<TestNpcSlot> TestSlots => _testSlots;

    private readonly List<TestNpcSlot> _testSlots =
    [
        // Spots inside the outbuilding, clear of its crates (at (-48, -9), (-53, -8), (-56, 0) and (-43, -15)).
        new(TestNpcType.Civilian, "CIVILIAN", new(-59f, 0, -14f), NpcProfile.Civilian(Temperament.Bolter)),
        new(TestNpcType.Woman, "WOMAN", new(-59f, 0, -8f), NpcProfile.Civilian(Temperament.Freezer) with { Woman = true }),
        new(TestNpcType.PoliceOfficer, "POLICE", new(-59f, 0, -2f), NpcProfile.Officer()),
        new(TestNpcType.PrisonOfficer, "PRISON OFFICER", new(-52f, 0, -14f), NpcProfile.PrisonOfficer()),
        new(TestNpcType.Gunman, "GUNMAN", new(-52f, 0, -3f), NpcProfile.Gunman() with { Strategy = NpcStrategy.Push }),
        new(TestNpcType.Rifleman, "RIFLEMAN", new(-46f, 0, -5f), NpcProfile.Rifleman() with { Strategy = NpcStrategy.Cover }),
        new(TestNpcType.Commando, "COMMANDO", new(-46f, 0, 1f), NpcProfile.Commando() with { Strategy = NpcStrategy.Push }),
    ];

    private bool _exactProfile;   // (Add takes the profile as it is: no rolled temper, commando chance or mixed gun)
    private readonly Random _testRandom = new();

    /// <summary>Who's standing in for a slot now (empty: despawned).</summary>
    public List<Npc> TestNpcs(TestNpcSlot slot) => _people.FindAll(p => slot.Owns(p.Name));

    private bool _countsRead;

    private void PopulateTestArea()
    {
        // BRUTALFPS_NPCCOUNT=n: start with n of every kind (1 .. 9), as if set in NPC CONFIG.
        if (!_countsRead && int.TryParse(Environment.GetEnvironmentVariable("BRUTALFPS_NPCCOUNT"), out var count))
            foreach (var slot in _testSlots)
                slot.Count = int.Clamp(count, 1, TestNpcSlot.MaxCount);
        _countsRead = true;
        foreach (var slot in _testSlots)
            SpawnTest(slot);
        Log.Write($"[NPCTEST] Test area: {_people.Count} people ({_testSlots.Count(s => s.Spawned)} of {_testSlots.Count} kinds) in the factory's outbuilding; nobody else in the world");
    }

    /// <summary>Puts a slot's person back as it's now configured: the old one goes, a new one stands at the spot (if it's spawned).</summary>
    public void ApplyTestSlot(TestNpcSlot slot)
    {
        if (_model is null)
            return;
        _people.RemoveAll(p => slot.Owns(p.Name));
        SpawnTest(slot);
    }

    /// <summary>Everyone back at their spots, unhurt (as configured).</summary>
    public void RespawnTestArea()
    {
        foreach (var slot in _testSlots)
            ApplyTestSlot(slot);
        // ...and nothing they knew carries over, as on a respawn (+): the alerts passed round, the hiding places, the wanted player.
        Senses.ClearAlerts();
        ClearHidingPlaces();
        _crossings.Clear();
        PlayerWanted = false;
        Senses.PlayerWanted = false;
        Log.Write("[NPCTEST] Everyone in the test area put back, calm");
    }

    /// <summary>The police forget the player shot one of them.</summary>
    public void ForgiveTestPlayer()
    {
        PlayerWanted = false;
        Senses.PlayerWanted = false;
        Log.Write("[NPCTEST] The police aren't after the player any more");
    }

    private void SpawnTest(TestNpcSlot slot)
    {
        if (!slot.Spawned || _model is null)
            return;
        var strategy = slot.Strategy;
        slot.RolledStrategy = NpcStrategy.Auto;
        if (strategy == NpcStrategy.Auto && slot.Aggression == Aggression.Aggressive && slot.Equipment is not (Equipment.None or Equipment.Baton))
        {
            // As NpcProfile.Rolled picks it: the armoured mostly push.
            var (push, cover) = slot.Armoured ? (0.6, 0.3) : (0.35, 0.4);
            var roll = _testRandom.NextDouble();
            strategy = slot.RolledStrategy = roll < push ? NpcStrategy.Push : roll < push + cover ? NpcStrategy.Cover : NpcStrategy.HoldBack;
        }

        var armed = slot.Equipment is not (Equipment.None or Equipment.Baton);
        var profile = new NpcProfile(slot.Faction, slot.Equipment, slot.Aggression, slot.Temperament, slot.Accuracy, slot.OnLostSight,
            Armoured: slot.Armoured, Strategy: strategy, TwoHanded: armed && slot.Equipment != Equipment.Pistol && (slot.IsCommando || slot.Type == TestNpcType.Rifleman),
            FixedGun: true, Woman: slot.Woman, IsCommando: slot.IsCommando);
        string[]? garments = slot.Type switch
        {
            TestNpcType.Woman => ["hair_long", "longsleeve", "jeans", "sneakers"],
            TestNpcType.PoliceOfficer => ["longsleeve", "trousers_work", "boots_work", "cap"],
            TestNpcType.PrisonOfficer => ["longsleeve", "trousers_work", "boots_work"],
            TestNpcType.Gunman => ["hoodie", "jeans", "sneakers", "beanie"],
            TestNpcType.Rifleman => ["longsleeve", "trousers_work", "boots_work", "balaclava"],
            _ => null,
        };
        var facing = float.Pi / 2;   // (east: towards the doorways and the player)
        _exactProfile = true;
        try
        {
            for (var i = 0; i < slot.Count; i++)
            {
                Add(slot.NameOf(i), SkinTones[((int)slot.Type + i) % SkinTones.Length], facing, profile, garments,
                    new Waypoint(slot.SpotOf(i), float.PositiveInfinity, facing));
                _people[^1].Undying = true;
            }
        }
        finally
        {
            _exactProfile = false;
        }

        Log.Write($"[NPCTEST] {slot.Label}{(slot.Count > 1 ? $" x{slot.Count}" : "")}: {DescribeTest(slot)}");
    }

    private static string DescribeTest(TestNpcSlot slot) =>
        $"{slot.Equipment.ToString().ToLowerInvariant()}, {slot.Aggression.ToString().ToLowerInvariant()}, " +
        $"{(slot.RolledStrategy != NpcStrategy.Auto ? $"auto ({slot.RolledStrategy.ToString().ToLowerInvariant()})" : slot.Strategy.ToString().ToLowerInvariant())}, " +
        $"{slot.Temperament.ToString().ToLowerInvariant()}, lost sight: {slot.OnLostSight.ToString().ToLowerInvariant()}, accuracy {slot.Accuracy:0.0}{(slot.Armoured ? ", armoured" : "")}";
}
