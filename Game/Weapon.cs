using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// A semi-automatic 9 mm pistol held in the player's right hand: 17 rounds in the magazine plus one in the chamber,
/// one shot per click, R to reload.
/// <para>
/// Each shot is animated: the trigger finger squeezes and the trigger moves back, the shot goes off, the slide slams
/// back while the barrel tilts down to unlock, a spent case flips out of the ejection port (a Bullet body that
/// bounces and rolls on the floor), and everything springs back. On the last round the slide locks back.
/// </para>
/// <para>
/// Reloading tips the gun in and up; the magazine release drops the old magazine out of the grip (it falls and lies
/// on the floor), a fresh one comes up into the grip and is slapped home, and after an empty reload the slide is
/// released and runs forward to chamber a round. A reload with a round still chambered is quicker.
/// </para>
/// <para>
/// It's held in both hands, thumbs forward: the left hand wraps round the right hand's fingers on the grip, its thumb
/// along the frame under the right thumb, both arms out in front in their sleeves. Hold the right mouse button to aim:
/// the gun comes up to the eye with the sights on the crosshair (and the view narrows a little). Each shot kicks: the
/// muzzle flips up and rolls a little to the left about the wrists, the gun comes back into the hands, then it settles
/// (the sleeves stay with the arms; their cuffs follow the wrists); the view jolts a fraction. Burning gas flashes out
/// of the ejection port as the slide comes back, with a small flash at the muzzle, and the flash lights the hands.
/// </para>
/// <para>
/// The gun also moves with the player: it lags behind the mouse, bobs with your stride, dips when you land, drops
/// and turns aside while sprinting, breathes a little when still, and is raised into view after a respawn. On a
/// reload the left hand lets go and drops away, comes back up holding a fresh magazine, pushes it up into the grip and
/// takes hold again.
/// </para>
/// Shots leave the muzzle but are aimed (allowing for the drop) at whatever is under the crosshair. Recoil moves that
/// aim: each shot walks the point of aim up and off to one side (in a wandering line, the way it does on a real
/// pistol), the view following it, and the hands pull it back towards where you were aiming once you stop firing.
/// Every shot also has a little scatter of its own (more from the hip), so even slow, careful shots don't land in
/// the same hole. The view model (gun, hand and sleeve) is modelled in Blender (source: Assets/pistol.blend) and
/// loaded from Assets/Models/pistol.glb (or the older Assets/pistol.obj); if neither is there, a simple box gun is
/// drawn instead.
/// </summary>
public sealed class Weapon(
    Level level,
    PortalSystem portals,
    ProjectileSystem projectiles,
    ImpactEffects effects,
    DebrisSystem debris,
    PhysicsWorld? physics,
    Model? pistol)
    : GunBase(level, portals, projectiles, effects, debris, physics, PistolFeel,
        // Each shot's cone (radians): its own scatter, opened up by shots in quick succession (see ShotSpread). Careful
        // aimed shots land within 25 cm at 25 m, so they hit a man there, and within 50 cm at 50 m, so more than half miss
        // his body; rapid fire spreads them out, and it closes up again within a second of stopping.
        new ShotSpread(hip: 0.022f, aimed: 0.0095f, hipBloom: 0.018f, aimedBloom: 0.010f, perShot: 0.2f, coolDelay: 0.2f, coolRate: 1.4f),
        capacity: 17, seed: 1234)
{
    public override string Name => "Pistol";
    public float FireInterval = 0.11f;   // fastest you can fire by clicking
    public float ProjectileSpeed = 360f; // m/s (9 mm, 115 grain, from a pistol barrel)
    public float MaxAimDistance = 300f;
    public float ZeroDistance = 25f;     // m: where the shot crosses the line of sight (and falls below it beyond)

    public override bool Reloading => _reloading;

    /// <summary>0..1 through the current reload.</summary>
    public override float ReloadProgress => Reloading ? float.Clamp(_reloadT / ReloadLength, 0f, 1f) : 0f;

    public override string FireMode => "SEMI";    // one shot per click, and nothing else

    // Sounds (Assets/Sounds/Weapons, cut from the recordings in Assets/Sounds: see its README). The reload clips are
    // started so that their loudest click lands on the matching moment of the reload: the magazine release and the new
    // magazine seating. These are the times of those clicks within the clips.
    private const string MagOutSound = "Weapons/pistol_mag_out", MagInSound = "Weapons/pistol_mag_in";
    private const float MagOutClick = 0.10f, MagInClick = 0.45f;

    // --- Pistol model (model space: metres, +X out of the muzzle, +Y up, -Z = the gun's left side) ---
    private static readonly float3 MuzzlePoint = new(0.093f, 0.0108f, 0f);
    protected override float3? BoreMuzzle => MuzzlePoint;
    private const float SlideTravel = 0.024f;                                  // how far the slide kicks back
    private static readonly HashSet<string> SlideParts = ["Slide", "FrontSight", "RearSight"];
    private static readonly HashSet<string> BarrelParts = ["Barrel", "BarrelHood"];
    private static readonly HashSet<string> MagazineParts = ["Magazine", "MagazineBase", "MagazineFollower", "MagazineRound"];
    private static readonly float3 TriggerPivot = new(-0.0025f, -0.0225f, 0f);     // top of the trigger blade
    private static readonly float3 FingerKnuckle = new(-0.0612f, -0.0363f, 0.0236f); // trigger finger's knuckle (from Tools/previews/pistol_pivots.json)
    private static readonly float3 BarrelPivot = new(0.085f, 0.0108f, 0f);         // barrel tilts about its muzzle end
    private static readonly float3 EjectionPort = new(0.004f, 0.025f, 0.005f);

    // The magazine (Tools/blender/build_pistol.py: MAG_*): it slides out down the grip's slope.
    private static readonly float3 MagAxis = float3.Normalize(new float3(-0.2932f, -0.9560f, 0f));
    private static readonly float3 MagCenter = new(-0.0605f, -0.053f, 0f);
    private static readonly float3 MagHalfExtents = new(0.0175f, 0.054f, 0.0135f);   // its body's box (depth, length, width)
    private const float MagLean = -0.2977f;     // the body frame's turn about Z (its Y axis up the magazine)
    private const float MagClear = 0.11f;       // how far down the axis it has to go to clear the grip

    // Shot timing (seconds from the click).
    private const float PullTime = 0.03f;      // finger squeezes the trigger; the shot fires at the end of it
    private const float ReleaseStart = 0.08f;
    private const float ReleaseEnd = 0.18f;
    private const float SlideBackTime = 0.018f;
    private const float SlideReturnTime = 0.09f;
    private const float SlideSlamTime = 0.05f; // released from lock-back

    // Reload timing (seconds from pressing R).
    private const float MagRelease = 0.22f;    // the old magazine starts to fall out
    private const float MagDrop = 0.36f;       // ...and is clear of the grip (it becomes a physics body)
    private const float NewMagSeat = 0.90f;    // pushed home (the left hand brings it up: see SupportHandTransform)
    private const float SlideRelease = 1.06f;  // empty reload: slide released
    private float ReloadLength => _reloadEmpty ? 1.55f : 1.3f;

    private static readonly float3 SkinTint = new(0.66f, 0.47f, 0.37f);

    // The flash of burning gas out of the ejection port as the slide comes back (model space, over the port); the
    // shot's light comes from there too.
    private static readonly float3 PortFlashPoint = new(-0.004f, 0.031f, 0.005f);
    private const float PortFlashTime = 0.035f;

    private static readonly GunFeel PistolFeel = new()
    {
        // Where the gun is held, in camera space (camera looks down -Z, +X right, +Y up): the point on the line of the
        // sights just over the rear sight, and how far the muzzle is turned in towards the centre of the screen. Aiming
        // puts the sights straight down the middle of the view. (Tools/blender/build_pistol.py's GAME_VIEWS renders the
        // previews from these.)
        SightPoint = new(-0.071f, 0.0325f, 0f),     // model space: rear sight, line of sight
        GripPivot = new(-0.0696f, -0.0505f, 0f),    // where the hand holds it
        HipPosition = new(0.025f, -0.048f, -0.270f),
        HipYaw = 0.04f,
        AimPosition = new(0f, 0f, -0.265f),
        AimTime = 0.16f,
        DrawTime = 0.45f,                            // raising the gun after a respawn

        // Recoil: the muzzle flips up (rolling a little to the left) about the wrists and the gun comes back into the
        // hands; springs, so it snaps up in a few hundredths of a second, overshoots a touch on the way back and settles.
        // The arms are pushed back with it, but don't flip with it.
        RecoilPivot = new(-0.133f, -0.095f, -0.012f),  // between the wrists (model space)
        FlipKick = 10.5f, FlipKickAiming = 7.5f,       // rad/s given to the flip by a shot
        FlipStiffness = 576f, FlipDamping = 28.8f,     // ~24 rad/s, damping ratio 0.6
        BackKick = 1.2f,                               // m/s back into the hands
        ViewKickPitch = 0.35f,                         // rad/s given to the view's jolt
        BackStiffness = 500f, BackDamping = 36f,
        RollRange = new(0.25f, 0.45f),
        FlipScatter = new(0.9f, 1.1f),
        ViewKickYawShare = 0.35f,
        RecoilYawShare = 0.08f, RecoilLift = 0.15f,
        ArmsBack = 0.8f, ArmsLift = 0.12f, ArmsFlipLift = 0.02f,

        // The walk of the aim: big steps, wandering widely and turning sharply now and then; recovered quickly.
        Walk = new AimWalk(RiseStepHip: 0.018f, RiseStepAiming: 0.014f, RecoverDelay: 0.09f, RecoverRate: 3.5f, RiseFollow: 30f,
            FreshHeading: 0.9f, TurnChance: 0.3, TurnRange: 2.2f, Drift: new(-0.5f, 0.5f), HeadingKeep: 0.85f, Lift: 0.15f),

        // Steadiness: held out at arm's length it wanders more, but it's light and settles quickly.
        AimSway = 0.0016f, AimSwayUnsteady = 0.005f,
        UnsteadySpreadHip = 0.020f, UnsteadySpreadAimed = 0.007f,
        SettleRate = 3.6f,

        // A light gun: it lags the mouse further and springs back quicker.
        SwayGain = 0.0016f, SwayLimit = new(0.07f, 0.06f),
        SwayStiffness = 140f, SwayDamping = 18f,
        DipStiffness = 120f, DipDamping = 14f,
        SprintIn = 6f, SprintOut = 12f,

        // Reloading: the magazine well turned towards the left hand, raised and brought in, so you see the magazine
        // change. Sprinting drops it further (but doesn't turn it aside).
        Steady = 0.6f,
        ReloadRoll = 0.26f, SprintRoll = 0.25f,
        ReloadPitch = 0.26f, SprintPitch = 0.45f, DrawPitch = 0.7f,
        SprintYaw = 0f,
        ReloadShift = new(-0.035f, 0.055f, 0.035f),
        SprintShift = new(-0.03f, -0.045f, 0.02f),
        DrawShift = new(0, -0.25f, 0.05f),
        StrideBob = new(0.008f, 0.01f),
        Breath = new(0.0008f, 0.0014f),

        FlashPoint = PortFlashPoint,
        FlashLightTime = 0.04f, FlashLightStrength = 0.06f,
    };

    // The arms (Tools/previews/models/pistol_pivots.json, in model space): each wrist and the way its forearm runs back.
    // The sleeves' cuffs follow the hands, the rest of them the arms (they're skinned between the two, so the hands can
    // flip in recoil without the arms swinging). The left hand and its sleeve let go of the gun on a reload.
    private static readonly ArmRig Arms = new(
        new float3(-0.139f, -0.083f, 0.015f), float3.Normalize(new float3(-0.780f, -0.550f, 0.300f)),
        new float3(-0.128f, -0.108f, -0.040f), float3.Normalize(new float3(-0.699f, -0.619f, -0.359f)));
    private static readonly HashSet<string> SupportParts = ["HandLeft", "SleeveLeft"];

    private static readonly float3 GunmetalTint = new(0.22f, 0.22f, 0.23f);
    private static readonly float3 PolymerTint = new(0.19f, 0.19f, 0.2f);
    private static readonly float3 BarrelTint = new(0.40f, 0.40f, 0.42f);

    // Box-gun fallback layout.
    private const float ViewModelScale = 0.75f;
    private static readonly float3 GunOffset = new(0.17f, -0.16f, -0.36f);
    private static readonly float3 MuzzleLocal = new(0f, 0.02f, -0.36f); // relative to GunOffset

    private static readonly float3 BodyColor = new(0.18f, 0.19f, 0.21f);
    private static readonly float3 AccentColor = new(0.85f, 0.45f, 0.12f);
    private static readonly float3 FlashColor = new(1.0f, 0.95f, 0.6f);

    private float _cooldown;
    private float _recoil;           // 1 right after a shot, decays to 0 (the box gun)
    private float _sinceClick = 10f; // time since the trigger squeeze started
    private float _sinceSlideRelease = 10f;
    private bool _shotPending;
    private bool _slideLocked;

    // Reload state.
    private bool _reloading;
    private float _reloadT;
    private bool _reloadEmpty;
    private bool _oldMagDropped;
    private int _oldMagRounds;
    private bool _newMagSeated;
    private bool _slideReleased;

    // (An older pistol.glb has the sleeves in "cloth"; they're fleece now. The UVs are in 5 cm repeats either way.)
    private readonly Dictionary<string, ModelPart> _skinnedSleeves = SkinSleeves(pistol, Arms, material =>
        material.Material == Material.Cloth && TextureLibrary.Current?.TryGet(Material.Fleece, out _) == true
            ? material with { Material = Material.Fleece }
            : material);
    protected override IReadOnlyDictionary<string, ModelPart> SkinnedSleeves => _skinnedSleeves;

    protected override void AbandonReload()
    {
        // Whatever magazine is in the gun stays; one already dropped is gone. A fresh magazine seated after an empty
        // one has the slide released on the way down (otherwise the gun would come back up full, slide locked and
        // unchambered, and only another reload, dropping a full magazine, would chamber it).
        _reloading = false;
        if (_oldMagDropped && !_newMagSeated)
            AmmoInMag = 0;
        else if (_reloadEmpty && _newMagSeated && !_slideReleased)
        {
            _slideReleased = true;
            _slideLocked = false;
            AmmoInMag--;
            Chambered = true;
        }
    }

    protected override void Respawned()
    {
        _reloading = false;
        _slideLocked = false;
    }

    /// <summary>The light a shot gives off this frame (xyz world position, w strength; 0 when there's none).</summary>
    public override float4 FlashLight(in Camera camera) => pistol is null ? float4.Zero : base.FlashLight(camera);

    public override void Update(float dt, Input input, in Camera camera, PlayerController player, bool allowFire)
    {
        Tick(dt, player);
        _cooldown = float.Max(0, _cooldown - dt);
        _recoil = float.Max(0, _recoil - dt * 8f);
        _sinceClick += dt;
        _sinceSlideRelease += dt;

        if (allowFire && !_holstering && input.WasPressed(GlfwKey.R))
            StartReload();

        // Semi-automatic: one shot per click. With the pistol model, the shot goes off once the finger has
        // squeezed the trigger through (a few hundredths of a second); the box gun fires straight away.
        var ready = !Reloading && !_holstering && _drawT >= Feel.DrawTime * 0.8f;
        if (allowFire && ready && input.WasPressed(GlfwMouseButton.Left) && _cooldown <= 0 && !_shotPending)
        {
            _cooldown = FireInterval;
            _sinceClick = 0;
            _shotPending = true;
        }

        if (_shotPending && (pistol is null || _sinceClick >= PullTime))
        {
            _shotPending = false;
            if (Chambered)
                Fire(camera);
            else
                Log.Write(EmptyClick);
        }

        if (Reloading)
            UpdateReload(dt, camera);

        UpdateMotion(dt, input, player);
        RiseOverEdge(dt, camera, player);
        ApplyDebugPose();
    }

    // ------------------------------------------------------------------------------------------------ reloading

    private void StartReload()
    {
        if (!CanStartReload)
            return;
        _reloading = true;
        _reloadT = 0;
        _reloadEmpty = !Chambered;
        _oldMagRounds = AmmoInMag;
        _oldMagDropped = false;
        _newMagSeated = false;
        _slideReleased = false;
        _shotPending = false;
        if (Audio is not null)
        {
            Audio.Play(Audio.Get(MagOutSound), 0.9f, delay: MagRelease - MagOutClick, tag: "reload");
            Audio.Play(Audio.Get(MagInSound), 0.9f, delay: NewMagSeat - MagInClick, tag: "reload");
        }

        Log.Write($"[RELOAD] {(_reloadEmpty ? "Empty" : "Tactical")} reload ({AmmoInMag} left in the magazine)");
    }

    private void UpdateReload(float dt, in Camera camera)
    {
        _reloadT += dt;
        if (!_oldMagDropped && _reloadT >= MagDrop)
        {
            _oldMagDropped = true;
            AmmoInMag = 0;
            DropMagazine(camera);
        }

        if (!_newMagSeated && _reloadT >= NewMagSeat)
        {
            _newMagSeated = true;
            AmmoInMag = Capacity;
            _recoil = float.Max(_recoil, 0.3f);   // slapped home
            _flipVelocity += 1.4f;
            _backVelocity -= 0.25f;
        }

        if (_reloadEmpty && !_slideReleased && _reloadT >= SlideRelease)
        {
            _slideReleased = true;
            _slideLocked = false;
            _sinceSlideRelease = 0;
            AmmoInMag--;
            Chambered = true;
            _recoil = float.Max(_recoil, 0.35f);
            _flipVelocity += 1.8f;
            _backVelocity += 0.3f;
        }

        if (_reloadT >= ReloadLength)
            _reloading = false;
    }

    /// <summary>How far down the grip the old magazine is (0 = seated), or null once it has dropped away.</summary>
    private float? OldMagOffset =>
        !Reloading || _reloadT < MagRelease ? 0f
        : _oldMagDropped ? null
        : MagClear * Square((_reloadT - MagRelease) / (MagDrop - MagRelease));

    /// <summary>The new magazine is in the left hand (from when it's out of sight until it's seated).</summary>
    private bool NewMagInHandNow => Reloading && _reloadT >= NewMagInHand && _reloadT < NewMagSeat;

    /// <summary>The old magazine leaves the grip: it carries on as a body in the world and lands on the floor.</summary>
    private void DropMagazine(in Camera camera)
    {
        if (pistol is null)
            return;
        var gunToWorld = GunToCamera() * camera.World;
        var bodyPose = float4x4.CreateRotationZ(MagLean) * float4x4.CreateTranslation(MagCenter + MagAxis * MagClear) * gunToWorld;
        var renderOffset = float4x4.CreateTranslation(-MagCenter) * float4x4.CreateRotationZ(-MagLean);
        PullInFromWall(camera.Position, ref bodyPose, 0.07f);

        var down = float3.Normalize(float3.TransformNormal(MagAxis, gunToWorld));
        var velocity = down * 1.6f + _playerVelocity;
        var spin = new float3(Range(-3f, 3f), Range(-2f, 2f), Range(-3f, 3f));
        var parts = pistol.Parts.Where(p => MagazineParts.Contains(p.Name)).ToList();
        Debris.Magazine(parts, renderOffset, bodyPose, MagHalfExtents, _oldMagRounds, velocity, spin);
    }

    // ------------------------------------------------------------------------------------------------ firing

    private void Fire(in Camera camera)
    {
        _recoil = 1f;
        var aiming = AimBlend;
        var aimed = Discharge(camera, aiming, new float2(0.97f, 1.03f));

        // Cycle: the chamber empties, the next round (if any) is fed from the magazine; on the last one the slide stays back.
        Chambered = false;
        if (AmmoInMag > 0)
        {
            AmmoInMag--;
            Chambered = true;
        }
        else
        {
            _slideLocked = true;
        }

        // The case: out of the port, up and to the right, a little backwards; spinning end over end.
        var gunToWorld = pistol is not null ? GunToCamera() * camera.World : float4x4.Identity;
        if (pistol is not null)
            EjectCasing(camera, gunToWorld, EjectionPort, new float3(Range(-0.3f, 0f), Range(2.2f, 2.8f), Range(1.0f, 1.5f)),
                new float3(0.2f, 0.3f, 1f), new float2(25f, 40f));

        var eye = camera.Position;
        var forward = aimed.Forward;
        var muzzle = pistol is not null
            ? float3.Transform(MuzzlePoint, gunToWorld)
            : float3.Transform(GunOffset + MuzzleLocal * ViewModelScale, camera.World);
        var direction = AimShot(eye, forward, muzzle, MaxAimDistance, 1.5f, ZeroDistance, ProjectileSpeed, out var spawn, out var aimDistance, out var aimedAt);

        Projectiles.Spawn(spawn, direction * ProjectileSpeed);
        Effects.MuzzleSmoke(muzzle, forward, _playerVelocity);
        LogShot("", aimedAt, aimDistance, spawn == eye, aimed, camera, aiming, right: "right of the crosshair's rest");
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
                _oldMagDropped = t >= MagDrop;
                _oldMagRounds = _reloadEmpty ? 0 : 5;
                _slideLocked = _reloadEmpty && t < SlideRelease;
                _sinceSlideRelease = _reloadEmpty && t >= SlideRelease ? t - SlideRelease : 10f;
                _sinceShot = 10f;
                break;
            case "recoil":
            case "aimrecoil":
                _sinceClick = t + PullTime;
                break;
            case "locked":
                _slideLocked = true;
                _sinceShot = 10f;
                break;
        }
    }

    // ---- Animation curves (all 0..1) ----

    private float TriggerPull =>
        _sinceClick < PullTime ? Smooth(_sinceClick / PullTime)
        : _sinceClick < ReleaseStart ? 1f
        : 1f - Smooth(float.Clamp((_sinceClick - ReleaseStart) / (ReleaseEnd - ReleaseStart), 0f, 1f));

    private float SlideBack =>
        _slideLocked ? float.Min(1f, _sinceShot / SlideBackTime)
        : _sinceSlideRelease < SlideSlamTime ? 1f - Square(_sinceSlideRelease / SlideSlamTime)
        : _sinceShot < SlideBackTime ? _sinceShot / SlideBackTime
        : 1f - Smooth(float.Clamp((_sinceShot - SlideBackTime) / (SlideReturnTime - SlideBackTime), 0f, 1f));

    /// <summary>How far into the reload pose the gun is (tipped in and raised).</summary>
    protected override float ReloadPose
    {
        get
        {
            if (!Reloading)
                return 0f;
            var outStart = ReloadLength - 0.32f;
            return _reloadT < 0.25f ? Smooth(_reloadT / 0.25f)
                : _reloadT < outStart ? 1f
                : 1f - Smooth(float.Clamp((_reloadT - outStart) / 0.32f, 0f, 1f));
        }
    }

    // The left hand's reload (seconds): it lets go and drops away, out of sight; comes back up from below holding the
    // new magazine under the grip; pushes it up into the well; and is round the grip again as it seats.
    private const float LetGo = 0.05f, Gone = 0.30f, NewMagInHand = 0.40f, ComeBack = 0.46f, AtWell = 0.66f, NearlyIn = 0.84f;
    private const float WellGap = 0.11f;     // how far below its seat the magazine is as it reaches the well

    /// <summary>
    /// The left hand's place relative to the gun (identity = holding it). The new magazine travels in it: seated
    /// relative to the hand, so as the hand slides up the grip from below, the magazine goes up into the well.
    /// </summary>
    private float4x4 SupportHandTransform()
    {
        if (!Reloading)
            return float4x4.Identity;
        var t = _reloadT;
        // Away: down the grip and on down, a little to the left and turned a touch (out of sight below the view). It
        // comes back up the same way, so the hand and the magazine in it rise into view together.
        var away = float4x4.CreateTranslation(MagAxis * 0.06f)
            * RotateAbout(Arms.LeftWrist, float4x4.CreateRotationX(-0.2f))
            * float4x4.CreateTranslation(-0.02f, -0.21f, -0.05f);
        var below = float4x4.CreateTranslation(MagAxis * WellGap);
        var nearly = float4x4.CreateTranslation(MagAxis * 0.012f);
        if (t < LetGo)
            return float4x4.Identity;
        if (t < Gone)
            return Blend(float4x4.Identity, away, Smooth((t - LetGo) / (Gone - LetGo)));
        if (t < ComeBack)
            return away;
        if (t < AtWell)
            return Blend(away, below, Smooth((t - ComeBack) / (AtWell - ComeBack)));
        if (t < NearlyIn)
            return Blend(below, nearly, Smooth((t - AtWell) / (NearlyIn - AtWell)));
        if (t < NewMagSeat)
            return Blend(nearly, float4x4.Identity, Square((t - NearlyIn) / (NewMagSeat - NearlyIn)));
        return float4x4.Identity;
    }

    // ------------------------------------------------------------------------------------------------ drawing

    /// <summary>Draws the gun in front of the camera. Call after clearing depth so it never clips into walls.</summary>
    public override void Draw(Renderer renderer, in Camera camera)
    {
        if (pistol is not null)
        {
            DrawPistol(renderer, camera);
            return;
        }

        // Box gun: view bob while walking, kick back + up on recoil, dipped out of view while reloading.
        var bobAmount = float.Min(_horizontalSpeed / 9f, 1f);
        var bob = new float3(
            float.Sin(_strideDistance * 1.6f) * 0.012f * bobAmount,
            -float.Abs(float.Cos(_strideDistance * 1.6f)) * 0.014f * bobAmount,
            0);
        var kick = _recoil * _recoil;
        var offset = GunOffset + bob + new float3(0, 0.01f * kick - 0.2f * ReloadPose, 0.06f * kick);

        var gunToCamera = float4x4.CreateScale(ViewModelScale)
            * float4x4.CreateRotationX(0.12f * kick + 0.6f * ReloadPose)
            * float4x4.CreateTranslation(offset);
        var gunToWorld = gunToCamera * camera.World;

        // Body, barrel, sight, grip and a coloured stripe.
        DrawPart(renderer, gunToWorld, new float3(0, 0, 0), new float3(0.07f, 0.09f, 0.42f), BodyColor);
        DrawPart(renderer, gunToWorld, new float3(0, 0.02f, -0.27f), new float3(0.035f, 0.035f, 0.2f), BodyColor * 0.8f);
        DrawPart(renderer, gunToWorld, new float3(0, 0.06f, -0.05f), new float3(0.02f, 0.03f, 0.08f), BodyColor * 0.7f);
        DrawPart(renderer, gunToWorld, new float3(0, -0.09f, 0.08f), new float3(0.05f, 0.12f, 0.06f), BodyColor * 0.9f);
        DrawPart(renderer, gunToWorld, new float3(0.036f, 0.0f, -0.05f), new float3(0.004f, 0.025f, 0.25f), AccentColor);

        if (_muzzleFlash > 0)
        {
            var model = float4x4.CreateScale(0.09f)
                * float4x4.CreateRotationZ(ShotsFired * 0.7f)
                * float4x4.CreateTranslation(MuzzleLocal + new float3(0, 0, -0.03f))
                * gunToWorld;
            renderer.DrawCube(model, FlashColor, ShadeMode.Unlit);
        }
    }

    private void DrawPistol(Renderer renderer, in Camera camera)
    {
        var gunToWorld = GunToCamera() * camera.World;
        renderer.SetViewModel(true);

        // Procedural surface detail follows the gun rather than the world, so it doesn't crawl as you move.
        float4x4.Invert(gunToWorld, out var worldToGun);
        renderer.SetTextureSpace(worldToGun);

        // Moving parts: the slide slams back and springs forward (or stays locked back on an empty magazine); the
        // barrel rides back a little with it and its rear tilts down; the trigger blade swings back as the finger
        // curls round it; the magazine slides down the grip on a reload.
        var slide = SlideBack;
        var pull = TriggerPull;
        var slideToWorld = float4x4.CreateTranslation(-SlideTravel * slide, 0, 0) * gunToWorld;
        var barrelToWorld = RotateAbout(BarrelPivot, float4x4.CreateRotationZ(0.05f * slide))
            * float4x4.CreateTranslation(-0.004f * float.Min(slide * 3f, 1f), 0, 0) * gunToWorld;
        var triggerToWorld = RotateAbout(TriggerPivot, float4x4.CreateRotationZ(-0.3f * pull)) * gunToWorld;
        var fingerToWorld = RotateAbout(FingerKnuckle, float4x4.CreateRotationY(0.2f * pull) * float4x4.CreateRotationZ(-0.05f * pull)) * gunToWorld;

        var oldMag = OldMagOffset;
        var oldRounds = Reloading ? _oldMagRounds : AmmoInMag;
        var support = SupportHandTransform();
        var supportToWorld = support * gunToWorld;
        var hands = new HandsPose(gunToWorld, fingerToWorld, support, supportToWorld, ArmsToCamera() * camera.World);
        var newMagInHand = NewMagInHandNow;

        foreach (var part in pistol!.Parts)
        {
            var name = part.Name;
            if (MagazineParts.Contains(name))
            {
                // The old magazine slides out (until it drops away as a body); the new one comes up in the left hand.
                if (oldMag is { } d && (name != "MagazineRound" || oldRounds > 0))
                    renderer.DrawPart(part, float4x4.CreateTranslation(MagAxis * d) * gunToWorld, SleeveTint);
                if (newMagInHand)
                    renderer.DrawPart(part, supportToWorld, SleeveTint);
                else if (oldMag is null && (!Reloading || _reloadT >= NewMagSeat))
                    renderer.DrawPart(part, gunToWorld, SleeveTint);
                continue;
            }

            if (DrawHandPart(renderer, part, hands))
                continue;

            var model = name switch
            {
                _ when SlideParts.Contains(name) => slideToWorld,
                _ when BarrelParts.Contains(name) => barrelToWorld,
                _ when SupportParts.Contains(name) => supportToWorld,
                "Trigger" => triggerToWorld,
                _ => gunToWorld,
            };
            renderer.DrawPart(part, model, SleeveTint); // the sleeve's cloth takes the jacket colour
        }
        DrawOptic(renderer, slideToWorld, SleeveTint);   // (a mini red dot on the slide moves with it)

        renderer.SetTextureSpace(float4x4.Identity);
        renderer.SetViewModel(false);

        // Glowing stars drawn after the gun (on top), additively: a small, short flash at the muzzle, and burning gas
        // puffing out of the ejection port as the slide comes back (the bright orange flash you see from behind a
        // pistol).
        if (_muzzleFlash > 0 || _sinceShot < PortFlashTime)
        {
            var muzzle = float3.Transform(MuzzlePoint + new float3(0.01f, 0, 0), gunToWorld);
            var port = float3.Transform(PortFlashPoint, gunToWorld);
            BeginFlash();
            if (_muzzleFlash > 0)
            {
                var life = _muzzleFlash / 0.05f;
                FlashStar(muzzle, 0.03f * (0.7f + 0.5f * life), _flashRotation, new float3(3.6f, 1.5f, 0.4f), life);
                FlashStar(muzzle, 0.012f, _flashRotation + 0.6f, new float3(6f, 4f, 1.8f), life);
            }

            if (_sinceShot < PortFlashTime)
            {
                // Swells as the port opens, then dies away.
                var t = _sinceShot / PortFlashTime;
                var swell = float.Min(1f, t * 3f);
                var fade = 1f - Smooth(float.Max(0f, (t - 0.3f) / 0.7f));
                // Deep orange (not so bright that it tone-maps to white), with a hotter yellow core.
                FlashStar(port, 0.018f + 0.012f * swell, _flashRotation * 1.7f, new float3(3.2f, 1.05f, 0.22f), fade);
                FlashStar(port, 0.009f * swell + 0.003f, _flashRotation, new float3(4.5f, 2.6f, 0.8f), fade);
            }
            EndFlash(renderer);
        }
    }

    /// <summary>Materials for the parts of the older OBJ pistol (it has no materials of its own).</summary>
    public static PartMaterial ObjPartMaterial(string name)
    {
        var (tint, material) = name switch
        {
            "Slide" or "FrontSight" or "RearSight" or "Trigger" or "SlideStop" or "Takedown" => (GunmetalTint, Material.Gunmetal),
            "Barrel" or "BarrelHood" => (BarrelTint, Material.Steel),
            "Hand" or "TriggerFinger" => (SkinTint, Material.Skin),
            "Sleeve" => (SleeveTint, Material.Cloth),
            _ => (PolymerTint, Material.Polymer),
        };
        return new PartMaterial(material, tint, false, 0.5f, 0f);
    }

    private static void DrawPart(Renderer renderer, in float4x4 gunToWorld, float3 center, float3 size, float3 color) =>
        renderer.DrawCube(float4x4.CreateScale(size) * float4x4.CreateTranslation(center) * gunToWorld, color * 1.8f);
}
