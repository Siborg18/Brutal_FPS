using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The M249 SAW, a belt-fed light machine gun (5.56 x 45 mm NATO, a 200-round belt in a soft pouch under the receiver,
/// fully automatic at 750 rounds a minute). You don't start with it: it sits on the tripod of the jungle's
/// gun nest (<see cref="GunNest"/>) and you pick it up with E, and then it's on 7. It weighs as much as it looks, so with it in your hands you
/// move at <see cref="MoveScale"/> of your speed, it comes up slowly, lags the mouse and takes its time to the eye; a long
/// gun and a small round, it kicks little.
/// <para>
/// Each shot pulls the belt on by a link, and a case flies out of the port on the right. The last few rounds can be seen
/// going: the belt draws up out of the pouch, from its tail, until the tray is empty.
/// </para>
/// <para>
/// Reloading (R) is the long business it is on the real gun. The left hand leaves the handguard and lifts the feed cover,
/// takes the pouch and pulls it off with what's left of its belt (which falls); it goes down for a fresh one, hangs it
/// under the receiver with its belt laid into the tray, and pushes the cover down until it latches. After an empty belt
/// it then goes to the charging handle on the right and pulls it back.
/// </para>
/// The view model is Assets/Models/m249.glb (Tools/blender/build_m249.py, points: m249_points.json). Its grip, trigger and
/// guard are the AK's and its handguard's lower half under the left hand the AK's section, so it borrows the AK's hands
/// and sleeves, both as they are.
/// </summary>
public sealed class Lmg(
    Level level,
    PortalSystem portals,
    ProjectileSystem projectiles,
    ImpactEffects effects,
    DebrisSystem debris,
    PhysicsWorld? physics,
    Model model,
    Model hands)
    : GunBase(level, portals, projectiles, effects, debris, physics, M249Feel,
        // Spread: no bipod, so about 14 MOA aimed (a 40 cm group at 100 m), very wide from the hip; a second of fire opens it
        // right up (the widest of the guns), and it's slow to close again.
        new ShotSpread(hip: 0.022f, aimed: 0.002f, hipBloom: 0.045f, aimedBloom: 0.020f, perShot: 0.09f, coolDelay: 0.25f, coolRate: 1.0f),
        capacity: 200, seed: 2490)
{
    public override string Name => "M249";
    public override string FireMode => "AUTO";
    public override bool Reloading => _reloading;
    public override float ReloadProgress => Reloading ? float.Clamp(_reloadT / ReloadLength, 0f, 1f) : 0f;

    /// <summary>How fast you move with it in your hands, of your usual speed: walking, sprinting, crouching and on ladders.</summary>
    public override float MoveScale => 0.68f;

    public const float AutoFireInterval = 0.08f;    // 750 rounds a minute
    public const float ProjectileSpeed = 915f;      // 5.56 x 45 mm from a 521 mm barrel
    private const float MaxAimDistance = 500f;
    private const float ZeroDistance = 100f;

    public override float HoldUnder(float range) =>
        Projectiles.Drop(range, ProjectileSpeed, Cartridge.Nato556) / range
        - Projectiles.Drop(ZeroDistance, ProjectileSpeed, Cartridge.Nato556) / ZeroDistance;

    // Sounds: the pistol's clips, pitched down for heavier metal.
    private const string MagOutSound = "Weapons/pistol_mag_out", MagInSound = "Weapons/pistol_mag_in";
    private const float MagOutClick = 0.10f, MagInClick = 0.45f;

    // --- Points on the model (m249_points.json, in model space) ---
    private static readonly float3 MuzzlePoint = new(0.684f, 0.022f, 0f);
    private static readonly float3 EjectionPort = new(0.055f, 0.020f, 0.027f);
    private static readonly float3 TriggerPivot = new(0.001f, -0.036f, 0f);
    private static readonly float3 FingerKnuckle = new(-0.05045f, -0.05628f, 0.0231f);
    private static readonly float3 CoverHinge = new(-0.097f, 0.060f, 0f);
    private const float BoltTravel = 0.080f, HandleTravel = 0.070f;
    private static readonly float2 BoltCycle = new(0.024f, 0.04f);   // its times back, then forward again

    // The belt: Belt0 in the tray to the last in the pouch's mouth, a link apart; each shot it jumps up a link.
    private const int BeltParts = 11;
    private const float BeltPitch = 0.0119f;

    // The pouch: it comes away down and to the left, with its belt.
    private static readonly float3 PouchAxis = float3.Normalize(new float3(0f, -0.92f, -0.38f));
    private static readonly float3 PouchCenter = new(0.108f, -0.110f, 0f);
    private static readonly float3 PouchHalfExtents = new(0.058f, 0.066f, 0.036f);
    private const float PouchPull = 0.16f;       // how far before it's clear and falls
    private const float PouchHook = 0.035f;      // the new one, before it's pushed up onto its hanger

    private const float CoverOpen = 0.85f;       // radians: how far the feed cover swings up

    // The left hand's holds, from its hold on the handguard (origin and two axes of that hold, as Rifle's): up round the
    // feed cover from below and to the left, knuckle row along it, fingers over the lid and the arm well down out of the
    // view; on the pouch's left side, palm flat on it; and under the receiver at the charging handle, as on the AK.
    private static readonly float4x4 CoverHold = Frame(new float3(0.262f, -0.006f, 0f), new float3(1, 0, 0), new float3(0, -1, 0),
        new float3(0.095f, 0.030f, -0.012f), new float3(1, 0, 0), float3.Normalize(new float3(0, -0.94f, -0.34f)));
    private static readonly float4x4 PouchHold = Frame(new float3(0.262f, -0.006f, 0f), new float3(1, 0, 0), new float3(0, -1, 0),
        new float3(0.108f, -0.105f, -0.030f), new float3(0, 1, 0), new float3(0, 0, -1));
    private static readonly float4x4 RackHold = float4x4.CreateTranslation(-0.132f, 0.040f, 0.008f);

    // The reload's timeline (seconds from pressing R).
    private const float LetGo = 0.08f, AtCover = 0.42f, CoverUpStart = 0.44f, CoverUpEnd = 0.68f;  // lifts the cover
    private const float AtPouch = 0.94f, PullEnd = 1.12f, DropOld = 1.15f;                         // takes the pouch off
    private const float HandAway = 1.40f, NewShows = 1.50f, AtHanger = 1.92f, Seated = 2.08f;      // hangs a fresh one
    private const float BackAtCover = 2.34f, CoverDownEnd = 2.58f;                                 // shuts the cover
    private const float ToHandle = 2.86f, RackBack = 3.00f, RackRelease = 3.06f;                   // empty: racks it
    private const float BackOnGuardTime = 0.38f;
    private float ReloadLength => _reloadEmpty ? 3.52f : CoverDownEnd + BackOnGuardTime + 0.04f;
    private float BackOnGuardStart => _reloadEmpty ? RackRelease + 0.08f : CoverDownEnd + 0.02f;

    private static readonly GunFeel M249Feel = new()
    {
        // SightPoint is on the line through the ring and the front post, where the AK's rear sight is. At the hip it's held
        // a little lower than the sight line's height would put the hands, so the feed cover isn't in the middle of the view.
        SightPoint = new(0.1612f, 0.084f, 0f),
        GripPivot = new(-0.060f, -0.082f, 0f),
        HipPosition = new(0.112f, -0.088f, -0.340f),
        HipYaw = 0.09f,
        AimPosition = new(0f, 0f, -0.450f),          // cheek on the stock: the eye well behind the ring
        AimTime = 0.42f,
        DrawTime = 0.95f,
        // Recoil: a small round in a heavy gun with its butt in the shoulder: a steady shove, hardly any flip.
        RecoilPivot = new(-0.34f, 0f, 0f),
        FlipKick = 1.5f, FlipKickAiming = 0.95f,
        FlipStiffness = 380f, FlipDamping = 30f,
        BackKick = 0.40f,
        ViewKickPitch = 0.05f,
        // The walk of the aim: bigger steps than the AR-15's, wandering more (nothing holds it up but your arms), a little
        // slower to come back, and it only climbs so far: on a 200-round belt the hands soon settle against the kick, and a
        // long burst doesn't end up in the sky.
        Walk = new AimWalk(RiseStepHip: 0.0052f, RiseStepAiming: 0.0032f, RecoverDelay: 0.14f, RecoverRate: 3.0f, RiseFollow: 30f,
            FreshHeading: 0.6f, TurnChance: 0.25, TurnRange: 2.0f, Drift: new(-0.35f, 0.45f), HeadingKeep: 0.88f, Lift: 0.2f, MaxRise: 0.10f),
        // Steadiness: hard to hold still, and slow to settle after you've moved it.
        AimSway = 0.0024f, AimSwayUnsteady = 0.008f,
        UnsteadySpreadHip = 0.036f, UnsteadySpreadAimed = 0.018f,
        SettleRate = 1.6f,
        // The weight: it lags the mouse further and swings back slowly, sinks further on landing, and bobs with your stride.
        SwayGain = 0.0019f, SwayLimit = new(0.09f, 0.07f),
        SwayStiffness = 58f, SwayDamping = 13f,
        DipStiffness = 70f, DipDamping = 9f,
        SprintIn = 3.5f, SprintOut = 7f,
        Steady = 0.55f,
        // Reloading: rolled to show the top and the left side, where the belt goes in. Sprinting: carried low across the body.
        ReloadRoll = 0.30f, ReloadPitch = 0.22f,
        ReloadShift = new(-0.05f, -0.085f, -0.07f),
        SprintRoll = 0.30f, SprintPitch = 0.42f, SprintYaw = 0.3f,
        SprintShift = new(-0.02f, -0.07f, 0.05f),
        DrawPitch = 1.0f, DrawShift = new(0f, -0.38f, 0.08f),
        StrideBob = new(0.015f, 0.018f),
        Breath = new(0.0014f, 0.0022f),
        FlashPoint = new(0.684f, 0.022f, 0f),
        FlashLightTime = 0.05f, FlashLightStrength = 0.09f,
    };

    private readonly float4x4 _pouchHoldInverse = Inverted(PouchHold);
    private readonly Dictionary<string, ModelPart> _skinnedSleeves = SkinSleeves(hands, ArmRig.Ak47);
    protected override IReadOnlyDictionary<string, ModelPart> SkinnedSleeves => _skinnedSleeves;

    private float _cooldown;
    private bool _reloading;
    private float _reloadT;
    private bool _reloadEmpty, _oldPouchDropped, _newPouchSeated, _racked;
    private int _oldPouchRounds;

    protected override void AbandonReload()
    {
        // As Rifle's: whatever is on the gun stays, a pouch already dropped is gone, and a fresh belt after an empty one is
        // charged on the way down (otherwise the only way to chamber it would be to reload again).
        _reloading = false;
        if (_oldPouchDropped && !_newPouchSeated)
            AmmoInMag = 0;
        else if (_reloadEmpty && _newPouchSeated && !_racked)
        {
            _racked = true;
            AmmoInMag--;
            Chambered = true;
        }
    }

    protected override void Respawned() => _reloading = false;

    // ------------------------------------------------------------------------------------------------ update

    public override void Update(float dt, Input input, in Camera camera, PlayerController player, bool allowFire)
    {
        Tick(dt, player);
        _cooldown = float.Max(0, _cooldown - dt);

        if (allowFire && !_holstering && input.WasPressed(GlfwKey.R))
            StartReload();

        var ready = !Reloading && !_holstering && _drawT >= Feel.DrawTime * 0.8f;
        var held = (input.IsDown(GlfwMouseButton.Left) && input.MouseCaptured) || input.ScriptedFire || input.WasPressed(GlfwMouseButton.Left);
        _triggerHeld = allowFire && ready && held;
        if (_triggerHeld && _cooldown <= 0)
        {
            if (Chambered)
            {
                _cooldown = AutoFireInterval;
                Fire(camera);
            }
            else if (input.WasPressed(GlfwMouseButton.Left))
            {
                _cooldown = 0.2f;
                Log.Write(EmptyClick);
            }
        }

        FollowTrigger(dt);

        if (Reloading)
            UpdateReload(dt, camera);

        UpdateMotion(dt, input, player);
        RiseOverEdge(dt, camera, player);
        ApplyDebugPose();
    }

    private void StartReload()
    {
        if (!CanStartReload)
            return;
        _reloading = true;
        _reloadT = 0;
        _reloadEmpty = !Chambered;
        _oldPouchRounds = AmmoInMag;
        _oldPouchDropped = _newPouchSeated = _racked = false;
        if (Audio is not null)
        {
            Audio.Play(Audio.Get(MagOutSound), 0.9f, 0.74f, delay: CoverUpStart - MagOutClick, tag: "reload");   // the cover's latch
            Audio.Play(Audio.Get(MagOutSound), 0.8f, 0.86f, delay: PullEnd - MagOutClick, tag: "reload");        // the pouch off
            Audio.Play(Audio.Get(MagInSound), 0.9f, 0.82f, delay: Seated - MagInClick, tag: "reload");           // the new one on
            Audio.Play(Audio.Get(MagInSound), 1.0f, 0.68f, delay: CoverDownEnd - MagInClick, tag: "reload");     // the cover latching
            if (_reloadEmpty)
            {
                Audio.Play(Audio.Get(MagOutSound), 0.8f, 0.70f, delay: RackBack - MagOutClick, tag: "reload");
                Audio.Play(Audio.Get(MagInSound), 1.0f, 0.66f, delay: RackRelease - MagInClick + 0.02f, tag: "reload");
            }
        }
        Log.Write($"[RELOAD] {Name} {(_reloadEmpty ? "empty" : "tactical")} reload ({AmmoInMag} left on the belt)");
    }

    private void UpdateReload(float dt, in Camera camera)
    {
        _reloadT += dt;
        if (!_oldPouchDropped && _reloadT >= DropOld)
        {
            _oldPouchDropped = true;
            DropPouch(camera);
            AmmoInMag = 0;
        }

        if (!_newPouchSeated && _reloadT >= Seated)
        {
            _newPouchSeated = true;
            AmmoInMag = Capacity;
            _flipVelocity += 0.3f;      // hung on with a shove
            _backVelocity -= 0.08f;
        }

        if (_reloadEmpty && !_racked && _reloadT >= RackRelease)
        {
            _racked = true;
            AmmoInMag--;
            Chambered = true;
            _flipVelocity += 0.4f;
            _backVelocity += 0.12f;
        }

        if (_reloadT >= ReloadLength)
            _reloading = false;
    }

    /// <summary>How far the feed cover is up, in radians.</summary>
    private float CoverAngle
    {
        get
        {
            if (!Reloading)
                return 0f;
            var t = _reloadT;
            return t < CoverUpStart ? 0f
                : t < CoverUpEnd ? CoverOpen * Smooth((t - CoverUpStart) / (CoverUpEnd - CoverUpStart))
                : t < BackAtCover ? CoverOpen
                : t < CoverDownEnd ? CoverOpen * (1f - Square((t - BackAtCover) / (CoverDownEnd - BackAtCover)))
                : 0f;
        }
    }

    /// <summary>The muzzle on the model, for the gun on its tripod.</summary>
    public static float3 MuzzleOnModel => MuzzlePoint;

    /// <summary>A gunner on the tripod fires: the next round off the belt. False: it's out.</summary>
    public bool TakeRoundOnTripod()
    {
        if (!Chambered)
            return false;
        Chambered = false;
        if (AmmoInMag > 0)
        {
            AmmoInMag--;
            Chambered = true;
        }

        return true;
    }

    /// <summary>A full belt, charged: a gunner's, from the crate, or a respawn's.</summary>
    public void NewBelt()
    {
        AmmoInMag = Capacity;
        Chambered = true;
    }

    /// <summary>A gunner's round has gone: smoke at the muzzle and a case out of the port on the right.</summary>
    public void ShotOnTripod(float3 muzzle, float3 velocity, in float4x4 gunToWorld)
    {
        Effects.MuzzleSmoke(muzzle, float3.Normalize(velocity), float3.Zero);
        var casePose = float4x4.CreateRotationY(float.Pi / 2) * float4x4.CreateTranslation(EjectionPort) * gunToWorld;
        var caseVelocity = float3.TransformNormal(new float3(Range(0.2f, 0.7f), Range(1.0f, 1.6f), Range(2.6f, 3.4f)), gunToWorld);
        var spin = float3.Normalize(float3.TransformNormal(new float3(0.3f, 1f, 0.2f), gunToWorld)) * Range(20f, 35f);
        Debris.Casing(casePose, caseVelocity, spin, Cartridge.Nato556);
    }

    private float4x4 CoverMotion(float angle) => RotateAbout(CoverHinge, float4x4.CreateRotationZ(angle));

    /// <summary>Where the pouch and its belt are, relative to their seat, during a reload; null while there's none at the gun.</summary>
    private float4x4? PouchLocal()
    {
        if (!Reloading)
            return float4x4.Identity;
        var t = _reloadT;
        if (t < AtPouch)
            return float4x4.Identity;
        if (t < DropOld)
            return float4x4.CreateTranslation(PouchAxis * (PouchPull * Smooth((t - AtPouch) / (PullEnd - AtPouch))));
        if (t < NewShows)
            return null;
        if (t < Seated)
            return _pouchHoldInverse * SupportHandTransform();
        return float4x4.Identity;
    }

    /// <summary>The left hand's place relative to its hold on the handguard (identity = on the handguard).</summary>
    private float4x4 SupportHandTransform()
    {
        if (!Reloading)
            return float4x4.Identity;
        var t = _reloadT;
        var onCover = CoverHold * CoverMotion(CoverOpen);
        var outPose = PouchHold * float4x4.CreateTranslation(PouchAxis * PouchPull);
        var away = outPose * float4x4.CreateTranslation(-0.05f, -0.24f, -0.07f);
        var hook = PouchHold * float4x4.CreateTranslation(PouchAxis * PouchHook);
        var racked = RackHold * float4x4.CreateTranslation(-HandleTravel, 0, 0);
        if (t < LetGo)
            return float4x4.Identity;
        if (t < AtCover)
            return Blend(float4x4.Identity, CoverHold, Smooth((t - LetGo) / (AtCover - LetGo)));
        if (t < CoverUpEnd)
            return CoverHold * CoverMotion(CoverAngle);
        if (t < AtPouch)
            return Blend(onCover, PouchHold, Smooth((t - CoverUpEnd) / (AtPouch - CoverUpEnd)));
        if (t < DropOld)
            return PouchHold * float4x4.CreateTranslation(PouchAxis * (PouchPull * Smooth((t - AtPouch) / (PullEnd - AtPouch))));
        if (t < HandAway)
            return Blend(outPose, away, Smooth((t - DropOld) / (HandAway - DropOld)));
        if (t < NewShows + 0.04f)
            return away;
        if (t < AtHanger)
            return Blend(away, hook, Smooth((t - NewShows - 0.04f) / (AtHanger - NewShows - 0.04f)));
        if (t < Seated)
            return Blend(hook, PouchHold, Smooth((t - AtHanger) / (Seated - AtHanger)));
        if (t < BackAtCover)
            return Blend(PouchHold, onCover, Smooth((t - Seated) / (BackAtCover - Seated)));
        if (t < CoverDownEnd)
            return CoverHold * CoverMotion(CoverAngle);

        var back = BackOnGuardStart;
        if (_reloadEmpty && t < back)
        {
            if (t < ToHandle)
                return Blend(CoverHold, RackHold, Smooth((t - CoverDownEnd) / (ToHandle - CoverDownEnd)));
            if (t < RackBack)
                return Blend(RackHold, racked, Smooth((t - ToHandle) / (RackBack - ToHandle)));
            return racked;
        }

        var from = _reloadEmpty ? racked : CoverHold;
        return t < back + BackOnGuardTime ? Blend(from, float4x4.Identity, Smooth((t - back) / BackOnGuardTime)) : float4x4.Identity;
    }

    /// <summary>The charging handle, racked by the hand after an empty belt: 0 forward, 1 back.</summary>
    private float HandleBack
    {
        get
        {
            if (!Reloading || !_reloadEmpty)
                return 0f;
            var t = _reloadT;
            return t < ToHandle ? 0f
                : t < RackBack ? Smooth((t - ToHandle) / (RackBack - ToHandle))
                : t < RackRelease ? 1f
                : 1f - Square((t - RackRelease) / 0.04f);
        }
    }

    private float BoltBack
    {
        get
        {
            var cycle = _sinceShot < BoltCycle.X ? _sinceShot / BoltCycle.X
                : 1f - Smooth(float.Clamp((_sinceShot - BoltCycle.X) / BoltCycle.Y, 0f, 1f));
            return float.Max(cycle, HandleBack);
        }
    }

    /// <summary>The old pouch, with what's left of its belt, comes away and falls.</summary>
    private void DropPouch(in Camera camera)
    {
        var gunToWorld = GunToCamera() * camera.World;
        var motion = float4x4.CreateTranslation(PouchAxis * PouchPull);
        var bodyPose = float4x4.CreateTranslation(PouchCenter) * motion * gunToWorld;
        var renderOffset = float4x4.CreateTranslation(-PouchCenter);
        PullInFromWall(camera.Position, ref bodyPose, 0.08f);
        var down = float3.Normalize(float3.TransformNormal(PouchAxis, gunToWorld));
        var velocity = down * 1.0f + _playerVelocity;
        var spin = new float3(Range(-2f, 2f), Range(-1f, 1f), Range(-2f, 2f));
        var parts = model.Parts.Where(p => p.Name == "Pouch" || BeltShows(p.Name, _oldPouchRounds)).ToList();
        Debris.Magazine(parts, renderOffset, bodyPose, PouchHalfExtents, _oldPouchRounds, velocity, spin);
    }

    /// <summary>Is this one of the belt's parts, with this many rounds left to fill it? (Belt0 is the last to go.)</summary>
    private static bool BeltShows(string name, int rounds) =>
        name.StartsWith("Belt", StringComparison.Ordinal) && int.TryParse(name.AsSpan(4), out var index) && index < rounds && index < BeltParts;

    private static bool IsBelt(string name) => name.StartsWith("Belt", StringComparison.Ordinal);

    // ------------------------------------------------------------------------------------------------ firing

    private void Fire(in Camera camera)
    {
        var aiming = AimBlend;
        var aimed = Discharge(camera, aiming, new float2(0.86f, 0.92f));

        Chambered = false;
        if (AmmoInMag > 0)
        {
            AmmoInMag--;
            Chambered = true;
        }

        // The case: out to the right, a little forward and up, tumbling.
        var gunToWorld = GunToCamera() * camera.World;
        var caseVelocity = new float3(Range(0.2f, 0.7f), Range(1.0f, 1.6f), Range(2.6f, 3.4f));
        EjectCasing(camera, gunToWorld, EjectionPort, caseVelocity, new float3(0.3f, 1f, 0.2f), new float2(20f, 35f), Cartridge.Nato556);

        var eye = camera.Position;
        var forward = aimed.Forward;
        var muzzle = float3.Transform(MuzzlePoint, gunToWorld);
        var direction = AimShot(eye, forward, muzzle, MaxAimDistance, 2f, ZeroDistance, ProjectileSpeed, out var spawn, out var aimDistance, out var aimedAt, Cartridge.Nato556);

        Projectiles.Spawn(spawn, direction * ProjectileSpeed, round: Cartridge.Nato556);
        Effects.MuzzleSmoke(muzzle, forward, _playerVelocity);
        LogShot("M249 ", aimedAt, aimDistance, spawn == eye, aimed, camera, aiming);
    }

    protected override void ApplyDebugPose(string pose, float t)
    {
        switch (pose)
        {
            case "reload":
            case "empty":
                _reloading = true;
                _reloadEmpty = pose == "empty";
                _reloadT = t;
                _oldPouchDropped = t >= DropOld;
                _oldPouchRounds = _reloadEmpty ? 0 : 60;
                _sinceShot = 10f;
                break;
            case "recoil":
            case "aimrecoil":
                _pull = 1f;
                break;
        }
    }

    // ------------------------------------------------------------------------------------------------ poses

    protected override float ReloadPose
    {
        get
        {
            if (!Reloading)
                return 0f;
            var outStart = ReloadLength - 0.4f;
            return _reloadT < 0.34f ? Smooth(_reloadT / 0.34f)
                : _reloadT < outStart ? 1f
                : 1f - Smooth(float.Clamp((_reloadT - outStart) / 0.4f, 0f, 1f));
        }
    }

    // ------------------------------------------------------------------------------------------------ drawing

    public override void Draw(Renderer renderer, in Camera camera)
    {
        var gunToWorld = GunToCamera() * camera.World;
        renderer.SetViewModel(true);
        float4x4.Invert(gunToWorld, out var worldToGun);
        renderer.SetTextureSpace(worldToGun);

        var boltToWorld = float4x4.CreateTranslation(-BoltTravel * BoltBack, 0, 0) * gunToWorld;
        var handleToWorld = float4x4.CreateTranslation(-HandleTravel * HandleBack, 0, 0) * gunToWorld;
        var triggerToWorld = RotateAbout(TriggerPivot, float4x4.CreateRotationZ(-0.25f * _pull)) * gunToWorld;
        var fingerToWorld = RotateAbout(FingerKnuckle, float4x4.CreateRotationY(0.16f * _pull) * float4x4.CreateRotationZ(-0.04f * _pull)) * gunToWorld;
        var coverToWorld = CoverMotion(CoverAngle) * gunToWorld;
        var support = SupportHandTransform();
        var handsPose = new HandsPose(gunToWorld, fingerToWorld, support, support * gunToWorld, ArmsToCamera() * camera.World);

        // The belt jumps up a link with each shot: it's a link down as the bolt goes back, and comes up with it.
        var feed = _sinceShot < AutoFireInterval ? BeltPitch * (1f - _sinceShot / AutoFireInterval) : 0f;
        var pouch = PouchLocal();
        var rounds = !Reloading ? AmmoInMag : _reloadT < DropOld ? _oldPouchRounds : Capacity;

        foreach (var part in model.Parts)
        {
            var name = part.Name;
            if (name == "Pouch")
            {
                if (pouch is { } p)
                    renderer.DrawPart(part, p * gunToWorld, SleeveTint);
                continue;
            }

            if (IsBelt(name))
            {
                if (pouch is { } p && BeltShows(name, rounds))
                    renderer.DrawPart(part, float4x4.CreateTranslation(0, -feed, 0) * p * gunToWorld, SleeveTint);
                continue;
            }

            var pose = name switch
            {
                "FeedCover" => coverToWorld,
                "BoltCarrier" => boltToWorld,
                "ChargingHandle" => handleToWorld,
                "Trigger" => triggerToWorld,
                _ => gunToWorld,
            };
            renderer.DrawPart(part, pose, SleeveTint);
        }
        DrawOptic(renderer, coverToWorld, SleeveTint);   // (on the feed cover's rail: it lifts with it)

        foreach (var part in hands.Parts)
            DrawHandPart(renderer, part, handsPose);

        renderer.SetTextureSpace(float4x4.Identity);
        renderer.SetViewModel(false);

        if (_muzzleFlash > 0)
            DrawMuzzleFlash(renderer, float3.Transform(MuzzlePoint + new float3(0.02f, 0, 0), gunToWorld), 0.05f, 0.022f);
    }

    /// <summary>The gun with nobody holding it, out in the world (on its tripod): as many rounds on its belt as it has.</summary>
    public void DrawInWorld(Renderer renderer, in float4x4 pose, float coverAngle = 0f, float sinceShot = 10f)
    {
        // Fired on the tripod, the bolt cycles and the belt jumps; a new belt has the cover up.
        var bolt = sinceShot < BoltCycle.X ? sinceShot / BoltCycle.X : 1f - Smooth(float.Clamp((sinceShot - BoltCycle.X) / BoltCycle.Y, 0f, 1f));
        var feed = sinceShot < AutoFireInterval ? BeltPitch * (1f - sinceShot / AutoFireInterval) : 0f;
        foreach (var part in model.Parts)
        {
            if (IsBelt(part.Name))
            {
                if (BeltShows(part.Name, AmmoInMag))
                    renderer.DrawPart(part, float4x4.CreateTranslation(0, -feed, 0) * pose, float3.One);
                continue;
            }

            var at = part.Name switch
            {
                "BoltCarrier" => float4x4.CreateTranslation(-BoltTravel * bolt, 0, 0) * pose,
                "FeedCover" when coverAngle > 0f => CoverMotion(coverAngle) * pose,
                _ => pose,
            };
            renderer.DrawPart(part, at, float3.One);
        }
    }

    private static float4x4 Inverted(in float4x4 m)
    {
        float4x4.Invert(m, out var inv);
        return inv;
    }

    /// <summary>The rigid transform taking frame A (origin, axes a1, a2) onto frame B (see <see cref="RifleSpec"/>).</summary>
    private static float4x4 Frame(float3 oa, float3 a1, float3 a2, float3 ob, float3 b1, float3 b2)
    {
        var a3 = float3.Cross(a1, a2);
        var b3 = float3.Cross(b1, b2);
        var ma = new float4x4(a1.X, a1.Y, a1.Z, 0, a2.X, a2.Y, a2.Z, 0, a3.X, a3.Y, a3.Z, 0, 0, 0, 0, 1);
        var mb = new float4x4(b1.X, b1.Y, b1.Z, 0, b2.X, b2.Y, b2.Z, 0, b3.X, b3.Y, b3.Z, 0, 0, 0, 0, 1);
        var rot = float4x4.Transpose(ma) * mb;
        return float4x4.CreateTranslation(-oa) * rot * float4x4.CreateTranslation(ob);
    }
}
