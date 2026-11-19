using Brutal.Numerics;

namespace BrutalFps.Game;

/// <summary>Who someone sides with. Hostiles fight the player and the police; the police fight hostiles, and the player
/// too once the player has shot a civilian or an officer ("wanted"); civilians never fight anyone.</summary>
public enum Faction { Civilian, Police, Hostile }

/// <summary>
/// What someone carries. <see cref="None"/> and <see cref="Baton"/> aren't guns; anything else is drawn in the right hand
/// and fires as the player's gun of the same name does (<see cref="NpcGun"/>). The L-numbers are the British guns in the
/// gun locker (Game/BritishGuns.cs): <see cref="NpcGun.Register"/> sets them up from the same specs.
/// </summary>
public enum Equipment
{
    None, Pistol, Rifle, Shotgun, Smg, Carbine, BattleRifle, Baton,
    /// <summary>The M249, carried (not on the nest's tripod).</summary>
    Lmg,
    L85A2, L85A3, L403A1, L143A2, L119A1, L129A1, L135A1, L92A1, L80A1,
}

/// <summary>
/// How an NPC's gun is held and fires (<see cref="ArmedNpc"/>), by <see cref="Equipment"/>: the round, its speed, the
/// magazine, the rate of fire and how loud it is are the player's gun's (<see cref="FromSpec"/>), so the two can't drift
/// apart. Points are in the gun model's space (barrel +X, up +Y). Every gun is held in the right hand as the pistol is, the
/// long guns too, unless the profile says <see cref="NpcProfile.TwoHanded"/> (only the prison's raiders, so far).
/// <paramref name="Burst"/> shots go off <paramref name="Interval"/> apart (the gun's cyclic rate; a semi-automatic's
/// fastest) before the usual pause; a shotgun's shot is <paramref name="Pellets"/> pellets in a <paramref name="PelletCone"/>
/// (rad) cone. <paramref name="Range"/> (m): the furthest they will fire it at someone they can see. <paramref name="Volume"/>:
/// how loud the shot is (a suppressor brings it down, and it carries less far to people's ears).
/// </summary>
public sealed record NpcGun(
    float3 GripLocal, float3 MuzzleLocal, float BulletSpeed, int Magazine, float2 ShotPitch, float FlashSize,
    int Burst = 1, float Interval = 0f, int Pellets = 1, float PelletCone = 0f, Cartridge? Round = null, float Range = 40f, float Volume = 1f)
{
    // The pistol (Game/Weapon.cs: 9 mm ball at 360 m/s, 17 rounds) and the pump shotgun (Game/Shotgun.cs: 8 pellets at 400 m/s, 7 in the tube).
    public static readonly NpcGun Pistol = new(new(-0.072f, -0.046f, 0f), new(0.093f, 0.0108f, 0f), 360f, 17, new(0.94f, 1.02f), 0.05f,
        Interval: 0.11f, Range: 40f);
    public static readonly NpcGun Shotgun = new(new(-0.060f, -0.082f, 0f), new(0.640f, 0f, 0f), 400f, 7, new(0.62f, 0.68f), 0.065f,
        Pellets: 8, PelletCone: 0.04f, Range: 20f);   // (they hold their fire until it'll tell)

    // The rifles: straight from the player's specs (Game/Rifle.cs).
    public static readonly NpcGun Rifle = FromSpec(RifleSpec.Ak47, burst: 4, range: 80f);
    public static readonly NpcGun Smg = FromSpec(RifleSpec.Ump40, burst: 5, range: 50f);
    public static readonly NpcGun Carbine = FromSpec(RifleSpec.Ar15, burst: 3, range: 85f);
    public static readonly NpcGun BattleRifle = FromSpec(RifleSpec.G3, burst: 3, range: 95f);

    /// <summary>The M249 (Game/Lmg.cs): on the jungle nest's tripod in the hands of whoever's on it (Game/NpcGunner.cs; the belt is <see cref="GunNest"/>'s), or carried.</summary>
    public static readonly NpcGun M249 = new(new(-0.060f, -0.082f, 0f), Lmg.MuzzleOnModel, Lmg.ProjectileSpeed, 200, new(0.86f, 0.92f), 0.065f,
        Burst: 8, Interval: Lmg.AutoFireInterval, Round: Cartridge.Nato556, Range: 100f);

    private static readonly Dictionary<Equipment, NpcGun> Registered = [];

    /// <summary>A gun that's set up at startup from its spec (the British guns, whose specs are read from their models' points).</summary>
    public static void Register(Equipment equipment, NpcGun gun) => Registered[equipment] = gun;

    /// <summary>
    /// The player's gun as an NPC fires it: the same round, speed, magazine, cyclic rate (<paramref name="burst"/> rounds at
    /// a time; one, for a semi-automatic) and loudness, held by the same grip.
    /// </summary>
    public static NpcGun FromSpec(RifleSpec spec, int burst, float range) => new(
        spec.Feel.GripPivot, spec.MuzzlePoint, spec.ProjectileSpeed, spec.Capacity, spec.ShotPitch, spec.FlashSize,
        Burst: spec.SemiOnly ? 1 : burst, Interval: spec.SemiOnly ? spec.FireInterval : spec.AutoFireInterval,
        Round: spec.Cartridge, Range: range, Volume: spec.ShotVolume);

    /// <summary>How a British gun is used: rounds to a burst and how far they'll shoot with it (m).</summary>
    public static (int Burst, float Range) Handling(Equipment equipment) => equipment switch
    {
        Equipment.L85A2 or Equipment.L85A3 => (3, 90f),
        Equipment.L403A1 => (3, 100f),
        Equipment.L143A2 => (3, 80f),
        Equipment.L119A1 => (3, 75f),
        Equipment.L129A1 => (1, 130f),     // a marksman's rifle
        Equipment.L135A1 => (1, 220f),     // the .50
        Equipment.L92A1 or Equipment.L80A1 => (5, 45f),
        _ => (3, 80f),
    };

    /// <summary>What stands in for a gun that isn't set up (its model or spec is missing): the nearest of the guns that always are.</summary>
    public static Equipment Fallback(Equipment equipment) => equipment switch
    {
        Equipment.L85A2 or Equipment.L85A3 or Equipment.L403A1 or Equipment.L143A2 or Equipment.L119A1 => Equipment.Carbine,
        Equipment.L129A1 or Equipment.L135A1 => Equipment.BattleRifle,
        Equipment.L92A1 or Equipment.L80A1 => Equipment.Smg,
        _ => equipment,
    };

    public static NpcGun For(Equipment equipment) => Registered.TryGetValue(equipment, out var registered) ? registered : Fallback(equipment) switch
    {
        Equipment.BattleRifle => BattleRifle,
        Equipment.Rifle => Rifle,
        Equipment.Shotgun => Shotgun,
        Equipment.Smg => Smg,
        Equipment.Carbine => Carbine,
        Equipment.Lmg => M249,
        _ => Pistol,
    };

    /// <summary>Every gun an NPC can carry, for BRUTALFPS_NPCGUNS=mixed.</summary>
    public static readonly Equipment[] All =
    [
        Equipment.Pistol, Equipment.Rifle, Equipment.Shotgun, Equipment.Smg, Equipment.Carbine, Equipment.BattleRifle, Equipment.Lmg,
        Equipment.L85A2, Equipment.L85A3, Equipment.L403A1, Equipment.L143A2, Equipment.L119A1, Equipment.L129A1, Equipment.L135A1,
        Equipment.L92A1, Equipment.L80A1,
    ];
}

/// <summary>
/// How an armed NPC goes about a fight (anyone unarmed runs or cowers, by <see cref="Temperament"/>).
/// <list type="bullet">
/// <item><see cref="Defensive"/>: holds their position and shoots any enemy who comes in range; doesn't go after them,
/// keeps the gun on where they vanished, and only ducks into cover close by.</item>
/// <item><see cref="Cautious"/>: moves up on the enemy slowly, gun up, stopping to fire, and searches carefully (slowly,
/// looking round longer at each spot, and for longer) when they lose sight of them.</item>
/// <item><see cref="Aggressive"/>: goes looking for enemies on sight and fights to the end, the way their
/// <see cref="NpcStrategy"/> says (pushing, covering an ally, or holding back).</item>
/// </list>
/// </summary>
public enum Aggression { Defensive, Cautious, Aggressive }

/// <summary>
/// What an armed NPC does when the enemy they were fighting drops out of sight (behind a wall, or into the leaves).
/// <list type="bullet">
/// <item><see cref="Search"/>: go to where they were last seen (a little on, the way they were going), look round,
/// sweep a few spots nearby, then give up and go back to their routine.</item>
/// <item><see cref="Hold"/>: stay put, gun aimed where they vanished, and wait for them to show again (and fire the
/// moment they do).</item>
/// <item><see cref="Auto"/>: picked per person, for the <see cref="Aggression.Aggressive"/>: mostly search (a
/// <see cref="Temperament.Freezer"/> is likelier to hold). A defensive one always holds; a cautious one always searches.</item>
/// </list>
/// </summary>
public enum LostSight { Auto, Search, Hold }

/// <summary>
/// How an armed NPC goes about a fight.
/// <list type="bullet">
/// <item><see cref="Push"/>: keeps coming until they're on top of the enemy, firing as they go (less accurately at a run),
/// and hunts them down, quicker and for longer, if they drop out of sight.</item>
/// <item><see cref="Cover"/>: stays with an ally who's fighting (a pusher, by choice) rather than closing in, keeps firing
/// at where the enemy went for a few seconds after they vanish, to pin them, and follows the ally on the hunt.
/// With no one to cover, they fight the ordinary way.</item>
/// <item><see cref="HoldBack"/>: never closes in; shoots from where they are, a little more accurately, falls back when
/// the enemy gets close, and holds aim (and stays put) if they vanish.</item>
/// <item><see cref="Auto"/>: picked per person (<see cref="NpcProfile.Rolled"/>).</item>
/// </list>
/// </summary>
public enum NpcStrategy { Auto, Push, Cover, HoldBack }

/// <summary>
/// The attributes an NPC is made from. <see cref="Npcs"/> builds a plain <see cref="Npc"/> (a bystander, unarmed or with
/// only a baton) or an <see cref="ArmedNpc"/> (anyone with a gun) from one of these. The gun's magazine, round and rate of
/// fire come from the gun itself (<see cref="NpcGun"/>).
/// </summary>
/// <param name="Accuracy">0 (sprays) .. 1 (marksman): the cone their shots land in, and how quickly they take a shot.</param>
/// <param name="OnLostSight">Search for an enemy who drops out of sight, or hold aim where they vanished (for an <see cref="Aggression.Aggressive"/> one: a defensive one always holds, a cautious one always searches).</param>
/// <param name="FriendlyToPlayer">Never the player's enemy, even once the rest of the police are after them.</param>
/// <param name="TwoHanded">Holds a long gun shouldered in both hands (AimRifle), not out in front like a pistol (AimPistol).</param>
/// <param name="ShootsAnyone">Fights everyone outside their own faction, bystanders too, except anyone <paramref name="Spared"/>.</param>
/// <param name="Spared">Never a target, and other NPCs' bullets pass through them (the prison's rescue, <see cref="Npcs.RescueName"/>).</param>
/// <param name="Talks">Gestures now and then while calm (the Talk overlay), taking turns with whoever they're standing with.</param>
/// <param name="FixedGun">Keeps the gun the level gave them: a hostile's isn't rolled from the mixed tables (<see cref="Npcs"/>).</param>
/// <param name="Woman">The one body reshaped by its skeleton: smaller, narrower in the shoulders and waist, wider in the hips.</param>
/// <param name="NoHelmet">A commando in a hood instead (the Arctic's): nothing on the head stops a round.</param>
/// <param name="IsCommando">An elite hostile (<see cref="NpcProfile.Commando"/>): a helmet on top of the plate carrier (<see cref="HelmetCovers"/>), always aggressive, in a dark uniform.</param>
public sealed record NpcProfile(
    Faction Faction,
    Equipment Equipment,
    Aggression Aggression,
    Temperament Temperament,
    float Accuracy = 0.5f,
    LostSight OnLostSight = LostSight.Auto,
    bool FriendlyToPlayer = false,
    bool Armoured = false,
    NpcStrategy Strategy = NpcStrategy.Auto,
    bool TwoHanded = false,
    bool ShootsAnyone = false,
    bool Spared = false,
    bool Talks = false,
    bool FixedGun = false,
    bool Woman = false,
    bool IsCommando = false,
    bool NoHelmet = false)
{
    /// <summary>What's left of a hit's damage and bleeding when body armour takes it.</summary>
    public const float ArmourDamage = 0.35f, ArmourBleed = 0.25f;

    public bool Armed => Equipment is not (Equipment.None or Equipment.Baton);

    /// <summary>Has a gun, and so fights: built as an <see cref="ArmedNpc"/>.</summary>
    public bool Fights => Armed;

    /// <summary>The gun in hand, and how it fires.</summary>
    public NpcGun Gun => NpcGun.For(Equipment);

    /// <summary>Whether their armour covers that part: the body only (a head, arm or leg hit is as bad as ever).</summary>
    public bool ArmourCovers(BodyPart part) => Armoured && part == BodyPart.Torso;

    /// <summary>
    /// Whether their armour stops a hit there, <paramref name="bind"/> being the point on the skin in the bind pose: the plate
    /// on the body, and a commando's helmet on the top, sides and back of the head (not the face, the jaw or the neck).
    /// </summary>
    public bool ArmourCovers(BodyPart part, float3 bind) => ArmourCovers(part) || (IsCommando && !NoHelmet && part == BodyPart.Head && HelmetCovers(bind));

    /// <summary>
    /// Whether the helmet covers that point of the head (bind pose, y up, the face towards +z): above its rim, which runs just
    /// over the brow at the front and lower round the back. The same plane as the garment's (HELMET_CUT, in
    /// Tools/blender/clothkit/garments_more.py, in Blender's axes: there z is up and the face is towards -y).
    /// </summary>
    public static bool HelmetCovers(float3 bind)
    {
        float blenderY = -bind.Z, blenderZ = bind.Y;
        return blenderZ > 1.705f - 0.35f * (blenderY + 0.09f) - 0.01f;   // (1 cm under the rim: its lip)
    }

    /// <summary>
    /// A commando: accurate, aggressive, in body armour and a helmet, with a heavy gun shouldered (a carbine unless the level
    /// says otherwise). Rare: a few are placed (one at least in each part of the level with hostiles), and now and then a
    /// hostile is one by chance (<see cref="Npcs"/>).
    /// </summary>
    public static NpcProfile Commando(Equipment gun = Equipment.Carbine, float accuracy = 0.85f, NpcStrategy strategy = NpcStrategy.Auto) =>
        new(Faction.Hostile, gun, Aggression.Aggressive, Temperament.Bolter, accuracy, LostSight.Search, Armoured: true, Strategy: strategy,
            TwoHanded: gun is not Equipment.Pistol, FixedGun: true, IsCommando: true);

    /// <summary>The heavy guns a commando carries, picked by <paramref name="pick"/>: AR-15s mostly, a G3 or an M249 now and then.</summary>
    public static Equipment CommandoGun(int pick) => ((pick & 0x7fffffff) % 5) switch
    {
        0 or 1 or 2 => Equipment.Carbine,
        3 => Equipment.BattleRifle,
        _ => Equipment.Lmg,
    };

    /// <summary>
    /// This person's temper, rolled once (from <paramref name="random"/>, seeded by their name, so it's the same every run).
    /// Hostiles without a set plan: armour makes them bolder (70% aggressive, 20% cautious, 10% defensive, against 40%,
    /// 35%, 25%). Then, for the aggressive, a <see cref="NpcStrategy"/>: the armoured mostly push.
    /// </summary>
    public NpcProfile Rolled(Random random)
    {
        if (!Fights)
            return this;
        var aggression = Aggression;
        if (IsCommando)
        {
            // Always aggressive: mostly pushing, the rest covering whoever is.
            var plan = Strategy != NpcStrategy.Auto ? Strategy : random.NextDouble() < 0.7 ? NpcStrategy.Push : NpcStrategy.Cover;
            return this with { Aggression = Aggression.Aggressive, Strategy = plan };
        }

        if (Faction == Faction.Hostile && Strategy == NpcStrategy.Auto)
        {
            var (aggressive, cautious) = Armoured ? (0.7, 0.2) : (0.4, 0.35);
            var roll = random.NextDouble();
            aggression = roll < aggressive ? Aggression.Aggressive : roll < aggressive + cautious ? Aggression.Cautious : Aggression.Defensive;
        }

        var strategy = Strategy;
        if (strategy == NpcStrategy.Auto && aggression == Aggression.Aggressive)
        {
            var (push, cover) = Armoured ? (0.6, 0.3) : (0.35, 0.4);
            var roll = random.NextDouble();
            strategy = roll < push ? NpcStrategy.Push : roll < push + cover ? NpcStrategy.Cover : NpcStrategy.HoldBack;
        }

        return this with { Aggression = aggression, Strategy = strategy };
    }

    /// <summary>A bystander (no gun: their aggression never comes into it).</summary>
    public static NpcProfile Civilian(Temperament temperament) => new(Faction.Civilian, Equipment.None, Aggression.Defensive, temperament);

    public static NpcProfile Officer(Temperament temperament = Temperament.Bolter) =>
        new(Faction.Police, Equipment.Pistol, Aggression.Defensive, temperament, Accuracy: 0.65f);

    /// <summary>A prison officer: no gun, only a baton, so against rifles they run or cower like anyone else.</summary>
    public static NpcProfile PrisonOfficer(Temperament temperament = Temperament.Bolter) =>
        new(Faction.Police, Equipment.Baton, Aggression.Defensive, temperament);

    public static NpcProfile Gunman(float accuracy = 0.45f) =>
        new(Faction.Hostile, Equipment.Pistol, Aggression.Aggressive, Temperament.Bolter, accuracy);

    /// <summary>A hostile with an AK-47 (a full 30-round magazine, fired in bursts).</summary>
    public static NpcProfile Rifleman(float accuracy = 0.45f) =>
        new(Faction.Hostile, Equipment.Rifle, Aggression.Aggressive, Temperament.Bolter, accuracy);
}

/// <summary>Who fights whom.</summary>
public static class Factions
{
    /// <summary>Whether <paramref name="a"/> treats <paramref name="b"/> as an enemy (and so will shoot at them).</summary>
    public static bool Enemies(Faction a, Faction b) =>
        !Truce && ((a == Faction.Hostile && b == Faction.Police) || (a == Faction.Police && b == Faction.Hostile));

    /// <summary>NPCs never fight each other, only the player (the NPC test area's "NPC VS NPC: OFF": Game/NpcsTestArea.cs).</summary>
    public static bool Truce;

    /// <summary>Whether someone of <paramref name="faction"/> treats the player as an enemy.</summary>
    public static bool EnemyOfPlayer(Faction faction, bool playerWanted) =>
        faction == Faction.Hostile || (faction == Faction.Police && playerWanted);
}
