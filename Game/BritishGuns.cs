using System.Text.Json;
using Brutal.Numerics;

namespace BrutalFps.Game;

/// <summary>
/// The British service guns (Tools/blender/build_british_guns.py), each a <see cref="Rifle"/>. Their points come from
/// Assets/Models/&lt;id&gt;_points.json, written by that script, so the model and the spec can't drift apart; how each one
/// sits and kicks starts from the existing gun nearest to it. You don't start with them: they're chosen at a
/// <see cref="GunLocker"/>.
/// </summary>
public sealed partial record RifleSpec
{
    /// <summary>One shot at a time: no selector, and B does nothing.</summary>
    public bool SemiOnly { get; init; }

    /// <summary>How fast you move with it in your hands, of your usual speed.</summary>
    public float MoveScale { get; init; } = 1f;

    /// <summary>How loud its shot is (a suppressor brings it down).</summary>
    public float ShotVolume { get; init; } = 1f;

    /// <summary>What it is, in the gun locker ("SA80 bullpup assault rifle").</summary>
    public string Kind { get; init; } = "";

    /// <summary>Its sights, in the gun locker ("SUSAT 4x", "Iron sights").</summary>
    public string Optic { get; init; } = "Iron sights";

    /// <summary>The ones whose models are there, with the file each is built from.</summary>
    public static List<(string File, RifleSpec Spec)> LoadBritish(string assets)
    {
        var guns = new List<(string, RifleSpec)>();
        void Add(string file, RifleSpec basis, Func<RifleSpec, RifleSpec> own)
        {
            var path = Path.Combine(assets, "Models", file + "_points.json");
            if (!File.Exists(path) || !File.Exists(Path.Combine(assets, "Models", file + ".glb")))
                return;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            guns.Add((file, own(FromPoints(basis, json.RootElement))));
        }

        // 610-775 rounds a minute, 930-940 m/s, from a 518 mm barrel. The muzzle is close to the shoulder's line in a bullpup.
        var l85Spread = () => new ShotSpread(hip: 0.010f, aimed: 0.0003f, hipBloom: 0.028f, aimedBloom: 0.010f, perShot: 0.10f, coolDelay: 0.14f, coolRate: 1.7f);
        Add("l85a2", Ar15, s => s with
        {
            Name = "L85A2", ShotTag = "L85A2", Cartridge = Cartridge.Nato556, ProjectileSpeed = 940f, AutoFireInterval = 60f / 700f, Seed = 8502,
            ScopeZoom = 4f, ScopeSteps = null, ScopeKind = ScopeKind.Acog, NewSpread = l85Spread,
            Kind = "SA80 bullpup assault rifle", Optic = "SUSAT 4x",
        });
        Add("l85a3", Ar15, s => s with
        {
            Name = "L85A3", ShotTag = "L85A3", Cartridge = Cartridge.Nato556, ProjectileSpeed = 940f, AutoFireInterval = 60f / 700f, Seed = 8503,
            ScopeZoom = 4f, ScopeSteps = null, ScopeKind = ScopeKind.Acog, NewSpread = l85Spread,
            Kind = "SA80 bullpup assault rifle", Optic = "4x sight",
        });
        Add("l403a1", Ar15, s => s with
        {
            Name = "L403A1", ShotTag = "L403", Cartridge = Cartridge.Nato556, ProjectileSpeed = 880f, AutoFireInterval = 0.075f, Seed = 4031,
            ScopeZoom = 4f, ScopeSteps = [2f, 4f, 8f], FlashSize = 0.015f, FlashCoreSize = 0.008f, ShotVolume = 0.45f,
            Kind = "Knight's KS-1 rifle", Optic = "2-8x scope",
        });
        Add("l143a2", Ar15, s => s with
        {
            Name = "L143A2", ShotTag = "L143", Cartridge = Cartridge.Nato556, ProjectileSpeed = 760f, AutoFireInterval = 60f / 850f, Seed = 1432,
            ScopeZoom = 1f, ScopeSteps = [1f, 3f], ScopeKind = ScopeKind.Holo, FlashSize = 0.015f, FlashCoreSize = 0.008f, ShotVolume = 0.45f,
            Kind = "SIG MCX carbine", Optic = "Holo + 3x",
        });
        Add("l119a1", Ar15, s => s with
        {
            Name = "L119A1", ShotTag = "L119", Cartridge = Cartridge.Nato556, ProjectileSpeed = 800f, AutoFireInterval = 0.072f, Seed = 1191,
            ScopeZoom = 1f, ScopeSteps = null, ScopeKind = ScopeKind.RedDot,
            Kind = "Colt C8 SFW carbine", Optic = "Red dot",
        });
        Add("l129a1", G3, s => s with
        {
            Name = "L129A1", ShotTag = "L129", Cartridge = Cartridge.Nato762, ProjectileSpeed = 820f, Capacity = 20, FireInterval = 0.14f, Seed = 1291, SemiOnly = true,
            ScopeZoom = 6f, ScopeSteps = null, ScopeKind = ScopeKind.Acog,
            NewSpread = () => new ShotSpread(hip: 0.011f, aimed: 0.00025f, hipBloom: 0.03f, aimedBloom: 0.012f, perShot: 0.16f, coolDelay: 0.2f, coolRate: 1.5f),
            Kind = "LMT sharpshooter rifle", Optic = "6x ACOG",
        });
        Add("l135a1", G3, s => s with
        {
            Name = "L135A1", ShotTag = "L135", Cartridge = Cartridge.Bmg50, ProjectileSpeed = 853f, Capacity = 10, FireInterval = 0.45f, Seed = 1351, SemiOnly = true,
            ScopeZoom = 8f, ScopeSteps = [4f, 8f, 16f], ScopeKind = default, MaxAimDistance = 1500f, ZeroDistance = 300f, MoveScale = 0.62f,
            Feel = s.Feel with
            {
                FlipKick = 8f, FlipKickAiming = 6f, FlipStiffness = 240f, FlipDamping = 22f, BackKick = 1.6f, ViewKickPitch = 0.6f,
                Walk = s.Feel.Walk! with { RiseStepHip = 0.03f, RiseStepAiming = 0.022f, RecoverRate = 2f },
                DrawTime = 0.9f, AimTime = 0.42f, FlashLightStrength = 0.16f,
            },
            NewSpread = () => new ShotSpread(hip: 0.02f, aimed: 0.0002f, hipBloom: 0.05f, aimedBloom: 0.015f, perShot: 0.5f, coolDelay: 0.5f, coolRate: 1.2f),
            ShotPitch = new(0.42f, 0.47f), FlashSize = 0.14f, FlashCoreSize = 0.06f,
            CaseVelocityMin = new(0.3f, 1.6f, 3f), CaseVelocityMax = new(0.8f, 2.4f, 4f),
            Kind = "Barrett M82A1 anti-materiel rifle", Optic = "4-16x scope",
        });
        Add("l92a1", Ump40, s => s with
        {
            Name = "L92A1", ShotTag = "L92", Cartridge = Cartridge.Para9, ProjectileSpeed = 285f, AutoFireInterval = 60f / 700f, Seed = 9201,
            ShotPitch = new(1.25f, 1.32f), FlashSize = 0.008f, FlashCoreSize = 0.004f, ShotVolume = 0.22f,
            Kind = "MP5SD3 submachine gun", Optic = "Iron sights",
        });
        Add("l80a1", Ump40, s => s with
        {
            Name = "L80A1", ShotTag = "L80", Cartridge = Cartridge.Para9, ProjectileSpeed = 375f, AutoFireInterval = 60f / 900f, Seed = 8001,
            ShotPitch = new(0.98f, 1.04f),
            Kind = "MP5K submachine gun", Optic = "Iron sights",
        });
        return guns;
    }

    /// <summary>Blender (x, y, z) to model space (x, z, -y).</summary>
    private static float3 Point(JsonElement root, string name)
    {
        var p = root.GetProperty(name);
        return new float3(p[0].GetSingle(), p[2].GetSingle(), -p[1].GetSingle());
    }

    private static RifleSpec FromPoints(RifleSpec basis, JsonElement p)
    {
        var muzzle = Point(p, "Muzzle");
        var sightY = p.GetProperty("SightZ"u8).GetSingle();
        var eyepiece = p.GetProperty("EyepieceX"u8).GetSingle();
        var butt = p.GetProperty("Butt"u8).GetSingle();
        var shift = Point(p, "LeftHandShift");
        var axis = float3.Normalize(Point(p, "MagAxis"));
        var along = new float3(-axis.Y, axis.X, 0f);        // forward, across the magazine
        if (along.X < 0f)
            along = -along;
        var hold = new float3(0.262f + shift.X, -0.006f + shift.Y, 0f);
        const float SightX = 0.1612f;                       // where the AK's rear sight is: the gun sits in the hands as the AK does
        var half = p.GetProperty("MagHalfExtents"u8);
        return basis with
        {
            MuzzlePoint = muzzle,
            EjectionPort = Point(p, "EjectionPort"),
            BoltTravel = p.GetProperty("BoltTravel"u8).GetSingle(),
            HandleTravel = p.GetProperty("HandleTravel"u8).GetSingle(),
            LeftHandShift = float4x4.CreateTranslation(shift.X, shift.Y, 0f),
            MagPivot = Point(p, "MagPivot"),
            MagAxis = axis,
            MagCenter = Point(p, "MagCenter"),
            MagLean = p.GetProperty("MagLean"u8).GetSingle(),
            MagHalfExtents = new float3(half[0].GetSingle(), half[1].GetSingle(), half[2].GetSingle()),
            MagRock = 0.02f,
            MagPullOut = 0.13f,
            MagHookGap = 0.035f,
            MagHold = Frame(hold, new float3(1, 0, 0), new float3(0, -1, 0), Point(p, "MagGrab") + along * 0.012f, -axis, new float3(0, 0, -1)),
            RackHold = Frame(hold, new float3(1, 0, 0), new float3(0, -1, 0), Point(p, "ChargingHandle"), new float3(0, 1, 0), new float3(0, 0, -1)),
            Feel = basis.Feel with
            {
                SightPoint = new float3(SightX, sightY, 0f),
                // At the hip it's held lower the higher its sight is, so the hands are where the AK's are.
                HipPosition = new float3(0.105f, -0.080f - (sightY - 0.0931f) * 1.43f, float.Min(-0.345f, eyepiece - SightX - 0.09f)),
                // Aimed, the eye is 60 mm behind the sight's back end.
                AimPosition = new float3(0f, 0f, -(SightX - (eyepiece - 0.06f))),
                RecoilPivot = new float3(butt, 0.02f, 0f),
                FlashPoint = muzzle,
            },
        };
    }
}
