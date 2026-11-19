namespace BrutalFps.Game;

/// <summary>
/// A calibre: what the bullet weighs and how it flies, what it does to what it hits, and the case it leaves behind. The
/// models are Assets/Models/round_*.glb (Tools/blender/build_rounds.py). How fast a round leaves is the gun's (its barrel:
/// <see cref="RifleSpec.ProjectileSpeed"/>). The pistol and the shotgun have no cartridge of their own: a shot without
/// one is the plain 9 mm ball of <see cref="ProjectileSystem"/>, which the numbers here are measured against.
/// </summary>
public sealed class Cartridge
{
    /// <summary>A 115 grain 9 mm bullet at 360 m/s: what a <see cref="Punch"/> of 1 is.</summary>
    private const float ReferenceEnergy = 486f;

    private const float AirDensity = 1.2f;   // kg/m3

    public required string Name { get; init; }
    public required string Model { get; init; }           // Assets/Models/{Model}.glb
    public required float BulletMass { get; init; }       // kg
    public required float BulletDiameter { get; init; }   // m

    /// <summary>The bullet's drag coefficient, taken as constant over the speeds it flies at in the game.</summary>
    public required float DragCoefficient { get; init; }

    /// <summary>How readily it goes through someone, against 9 mm's 1: scales the depth, the chance and the speed kept.</summary>
    public required float Penetration { get; init; }

    /// <summary>
    /// How much wall it goes through at close range, in metres of brick (a level box's material weighs it: wood is
    /// easier, steel far harder; see <see cref="Level.ShootThrough"/>). 0: it stops in whatever it hits.
    /// </summary>
    public float WallPenetration { get; init; }

    /// <summary>Out to how far (m from the muzzle) it still goes through walls and body armour (with a <see cref="WallPenetration"/>).</summary>
    public float CloseRange { get; init; }

    public required double RicochetChance { get; init; }  // of a shallow hit on something hard (9 mm: 0.75)
    public required double SpentChance { get; init; }     // of the bullet staying in one piece on something hard, and dropping
    public required float CaseMass { get; init; }         // kg
    public bool SteelCase { get; init; }

    public float Radius => BulletDiameter / 2;

    /// <summary>Speed lost to the air per metre, per m/s of speed: dv/ds = -k v, so v = v0 e^(-k s).</summary>
    public float DragPerMetre => 0.5f * AirDensity * DragCoefficient * float.Pi * Radius * Radius / BulletMass;

    /// <summary>How hard it hits at this speed: the square root of its energy against 9 mm's, so it falls off with range.</summary>
    public float Punch(float speed) => float.Sqrt(0.5f * BulletMass * speed * speed / ReferenceEnergy);

    /// <summary>Muzzle energy (J) at this speed (what the gun locker shows).</summary>
    public float Energy(float speed) => 0.5f * BulletMass * speed * speed;

    /// <summary>
    /// How far a shot fired level at <paramref name="speed"/> has dropped after <paramref name="distance"/> metres, with
    /// this drag: y'' = g e^(2 k x) / v^2, as the air slows it and gravity gets longer to work. What the sights' zero and a drop ladder are worked out from.
    /// </summary>
    public float Drop(float distance, float speed, float gravity)
    {
        double k = DragPerMetre, x = distance, v = speed;
        return (float)(gravity / (2 * k * v * v) * ((Math.Exp(2 * k * x) - 1) / (2 * k) - x));
    }

    // Masses and diameters are the published ones; the case masses agree with what build_rounds.py works out from the metal.

    /// <summary>7.62 x 51 mm NATO, M80 ball (the G3): the heaviest and the hardest hitting, it carries furthest and goes through.</summary>
    public static readonly Cartridge Nato762 = new()
    {
        Name = "7.62x51mm NATO",
        Model = "round_762x51",
        BulletMass = 0.0095f,         // 147 grain
        BulletDiameter = 0.00782f,
        DragCoefficient = 0.29f,
        Penetration = 1.8f,
        RicochetChance = 0.7,
        SpentChance = 0.55,
        CaseMass = 0.0120f,
    };

    /// <summary>7.62 x 39 mm, M43 (the AK-47): nearly as heavy as the NATO round but slower and blunter, so it runs out of steam sooner.</summary>
    public static readonly Cartridge Soviet762 = new()
    {
        Name = "7.62x39mm",
        Model = "round_762x39",
        BulletMass = 0.0079f,         // 123 grain, steel core
        BulletDiameter = 0.00792f,
        DragCoefficient = 0.36f,
        Penetration = 1.5f,
        RicochetChance = 0.75,
        SpentChance = 0.6,
        CaseMass = 0.0065f,           // lacquered steel
        SteelCase = true,
    };

    /// <summary>5.56 x 45 mm NATO, M855 (the AR-15): light and very fast, it tends to break up rather than go through.</summary>
    public static readonly Cartridge Nato556 = new()
    {
        Name = "5.56x45mm NATO",
        Model = "round_556x45",
        BulletMass = 0.0040f,         // 62 grain
        BulletDiameter = 0.0057f,
        DragCoefficient = 0.30f,
        Penetration = 1.1f,
        RicochetChance = 0.55,
        SpentChance = 0.3,
        CaseMass = 0.0060f,
    };

    /// <summary>.40 S&amp;W, 165 grain (the UMP40): big, heavy and slow; the air slows it soon, and it mostly stays in whatever it hits.</summary>
    public static readonly Cartridge SW40 = new()
    {
        Name = ".40 S&W",
        Model = "round_40sw",
        BulletMass = 0.0107f,
        BulletDiameter = 0.01016f,
        DragCoefficient = 0.45f,
        Penetration = 0.95f,
        RicochetChance = 0.8,
        SpentChance = 0.9,
        CaseMass = 0.0045f,
    };

    /// <summary>9 x 19 mm Parabellum, 115 grain (the MP5s): light and slow, it stays in whatever it hits.</summary>
    public static readonly Cartridge Para9 = new()
    {
        Name = "9x19mm",
        Model = "round_9x19",
        BulletMass = 0.00745f,
        BulletDiameter = 0.00901f,
        DragCoefficient = 0.42f,
        Penetration = 0.85f,
        RicochetChance = 0.8,
        SpentChance = 0.9,
        CaseMass = 0.0040f,
    };

    /// <summary>12.7 x 99 mm (.50 BMG), 647 grain ball (the L135A1): it carries for kilometres and goes through almost anything.</summary>
    public static readonly Cartridge Bmg50 = new()
    {
        Name = ".50 BMG",
        Model = "round_50bmg",
        BulletMass = 0.0419f,
        BulletDiameter = 0.01298f,
        DragCoefficient = 0.27f,
        Penetration = 4.0f,
        WallPenetration = 0.6f,       // a single-skin brick wall (at an angle too), a steel door, a few centimetres of plate
        CloseRange = 50f,
        RicochetChance = 0.5,
        SpentChance = 0.2,
        CaseMass = 0.116f,
    };

    public static readonly Cartridge[] All = [Nato762, Soviet762, Nato556, SW40, Para9, Bmg50];
}
