using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// How a view-model gun sits in the hands and moves: the hold, the recoil, the walk of the aim, and how it follows the
/// player (sway, bob, dip, sprint). <see cref="GunBase"/> runs all of it; each gun only says how much. The defaults are
/// the long guns' (the AK-47, the UMP40 and the shotgun share them); the pistol sets its own.
/// <para>
/// Model space: metres, +X out of the muzzle, +Y up, -Z the gun's left side. Camera space: x right, y up, z back.
/// </para>
/// </summary>
public sealed record GunFeel
{
    // --- The hold: where the line of sight sits in camera space (at the hip and aiming), the muzzle's turn in at the
    // hip, and how quickly the gun comes up to the eye / into view. Reloading, sprinting and the draw turn it about the
    // grip. ---
    public required float3 SightPoint { get; init; }
    public required float3 GripPivot { get; init; }
    public required float3 HipPosition { get; init; }
    public float HipYaw { get; init; } = 0.10f;
    public float3 AimPosition { get; init; } = new(0f, 0f, -0.400f);
    public required float AimTime { get; init; }
    public required float DrawTime { get; init; }

    // --- Recoil: the muzzle flips up about RecoilPivot and the gun comes straight back (springs, stepped finely: the
    // flip is stiff); the view takes a jolt of its own. Each shot's kick is scaled by a random factor in these ranges.
    // The arms take the push back, and a little of the flip. ---
    public required float3 RecoilPivot { get; init; }
    public required float FlipKick { get; init; }          // rad/s per shot, at the hip
    public required float FlipKickAiming { get; init; }    // ...and aiming
    public required float FlipStiffness { get; init; }
    public required float FlipDamping { get; init; }
    public required float BackKick { get; init; }          // m/s back into the shoulder (hands)
    public required float ViewKickPitch { get; init; }     // rad/s given to the view's jolt
    public float BackStiffness { get; init; } = 600f;
    public float BackDamping { get; init; } = 45f;
    public float ViewKickStiffness { get; init; } = 300f;
    public float ViewKickDamping { get; init; } = 30f;
    public float2 RollRange { get; init; } = new(0.1f, 0.3f);          // how far the top rolls left as it flips
    public float2 FlipScatter { get; init; } = new(0.85f, 1.15f);
    public float2 BackScatter { get; init; } = new(0.9f, 1.1f);
    public float2 ViewKickScatter { get; init; } = new(0.8f, 1.2f);
    public float ViewKickYawShare { get; init; } = 0.5f;               // the jolt's sideways part, of its pitch
    public float RecoilYawShare { get; init; } = 0.15f;                // the muzzle's swing aside, of its flip
    public float RecoilLift { get; init; } = 0.05f;                    // the gun rising as it comes back
    public float ArmsBack { get; init; } = 0.85f;
    public float ArmsLift { get; init; } = 0.05f;
    public float ArmsFlipLift { get; init; } = 0.08f;

    /// <summary>The walk of the aim (see <see cref="AimWalk"/>); null: the gun has none.</summary>
    public AimWalk? Walk { get; init; }

    // --- Following the player: lag behind the mouse, a dip on landing, lowered and turned aside while sprinting. ---
    public float SwayGain { get; init; } = 0.0013f;
    public float2 SwayLimit { get; init; } = new(0.06f, 0.05f);
    public float SwayStiffness { get; init; } = 110f;
    public float SwayDamping { get; init; } = 17f;
    public float DipStiffness { get; init; } = 110f;
    public float DipDamping { get; init; } = 13f;
    public float SprintIn { get; init; } = 5f;     // 1/s
    public float SprintOut { get; init; } = 11f;

    // --- Steadiness: moving, swinging the aim, bringing the gun up or landing unsettles the hands, and they settle again
    // at SettleRate once that stops. The aim always wanders a little (AimSway, radians: the view and the shot follow it),
    // further while unsettled, and an unsettled shot's cone is wider (fully unsettled, by these radians). ---
    public float AimSway { get; init; } = 0.0012f;
    public float AimSwayUnsteady { get; init; } = 0.004f;
    public float UnsteadySpreadHip { get; init; } = 0.024f;
    public float UnsteadySpreadAimed { get; init; } = 0.008f;
    public float SettleRate { get; init; } = 2.8f;     // 1/s

    // --- The pose: turns about the grip (X rolls it, Z tips the muzzle up) and shifts in camera space, for the reload
    // (see GunBase.ReloadPose), the sprint and the draw; the stride's bob and breathing when still; how much steadier
    // aiming holds it. ---
    public float Steady { get; init; } = 0.65f;
    public float ReloadRoll { get; init; } = 0.40f;
    public float SprintRoll { get; init; } = 0.35f;
    public float ReloadPitch { get; init; } = 0.32f;
    public float SprintPitch { get; init; } = 0.32f;
    public float DrawPitch { get; init; } = 0.8f;
    public float SprintYaw { get; init; } = 0.25f;
    public float3 ReloadShift { get; init; } = new(-0.06f, 0.035f, -0.01f);
    public float3 SprintShift { get; init; } = new(-0.01f, -0.02f, 0.02f);
    public float3 DrawShift { get; init; } = new(0, -0.30f, 0.06f);
    public float2 StrideBob { get; init; } = new(0.009f, 0.011f);
    public float2 Breath { get; init; } = new(0.0009f, 0.0016f);

    // --- The muzzle flash (how long it shows) and the light the shot gives off (from FlashPoint, model space). ---
    public float FlashTime { get; init; } = 0.05f;
    public required float3 FlashPoint { get; init; }
    public required float FlashLightTime { get; init; }
    public required float FlashLightStrength { get; init; }
}

/// <summary>
/// Where the gun points after recoil (and so where the next shot goes): each shot walks the aim on by a step (radians)
/// in a direction that wanders from shot to shot but mostly climbs; once the shooting stops (after RecoverDelay) the
/// hands bring it back at RecoverRate per second, and the view follows it at RiseFollow. Shots fired faster than that
/// pile up and climb away; slow ones come back near the start.
/// <para>
/// The heading carries on from the last shot, turning by up to Drift each shot and now and then (TurnChance) swinging
/// round by up to TurnRange, and is drawn back towards climbing by HeadingKeep; a fresh string (0.6 s after the last
/// shot) starts off within FreshHeading of straight up. Lift is the climb every step adds on top (the flip itself). MaxRise
/// is as far as it can climb (and half that to either side); by default there's no limit, as a magazine runs out first.
/// </para>
/// </summary>
public sealed record AimWalk(
    float RiseStepHip, float RiseStepAiming,
    float RecoverDelay, float RecoverRate, float RiseFollow,
    float FreshHeading, double TurnChance, float TurnRange, float2 Drift, float HeadingKeep, float Lift, float MaxRise = float.MaxValue);

/// <summary>Where the arms are (model space): each wrist and the way its forearm runs back, for skinning the sleeves.</summary>
public readonly record struct ArmRig(float3 RightWrist, float3 RightArm, float3 LeftWrist, float3 LeftArm)
{
    /// <summary>The AK-47's (ak47_points.json); the UMP40 and the shotgun borrow its hands.</summary>
    public static readonly ArmRig Ak47 = new(
        new float3(-0.1238f, -0.099f, 0.015f), float3.Normalize(new float3(-0.7797f, -0.5498f, 0.2999f)),
        new float3(0.244f, -0.093f, -0.052f), float3.Normalize(new float3(-0.2996f, -0.8989f, -0.3196f)));
}

/// <summary>Where the hands are drawn this frame (all to world space): see <see cref="GunBase.DrawHandPart"/>.</summary>
public readonly record struct HandsPose(float4x4 Gun, float4x4 Finger, float4x4 Support, float4x4 SupportToWorld, float4x4 Arms);

/// <summary>
/// What the player's guns have in common: the view model's motion (the hold, recoil springs, the walk of the aim,
/// sway, bob, dip, sprint and the draw), drawing and putting away, a respawn's fresh gun, the shot itself (kick,
/// scatter, flash, sound, the muzzle-or-eye spawn point and the hold-over for the drop), spent cases, the skinned
/// sleeves and the muzzle flash. Each gun adds its own action (how it fires, cycles and reloads) and model.
/// </summary>
public abstract class GunBase(
    Level level,
    PortalSystem portals,
    ProjectileSystem projectiles,
    ImpactEffects effects,
    DebrisSystem debris,
    PhysicsWorld? physics,
    GunFeel feel,
    ShotSpread spread,
    int capacity,
    int seed) : IGun
{
    protected const string ShotSound = "Weapons/pistol_shot";

    /// <summary>How loud the shot is: a suppressor brings it down.</summary>
    protected virtual float ShotVolume => 1f;

    /// <summary>Every gun's cloth takes the jacket colour (a light blue fleece).</summary>
    protected static readonly float3 SleeveTint = new(0.50f, 0.61f, 0.76f);

    // The sleeves: the cuff (along the arm from the wrist) follows the hand, the rest the arm, blending over CuffBlend.
    private const float SleeveStart = -0.006f, CuffBlend = 0.10f;

    // What unsettles the hands: speed (fully at a sprint), the aim's turn rate (rad/s: slow tracking doesn't count) and
    // bringing the gun up or down. How fast that takes hold (1/s), and how much further the gun itself shows the aim's
    // wander at the hip than the view does.
    private const float UnsteadySpeed = 9.5f, TurnFree = 0.4f, TurnFull = 4f, UnsteadyRaising = 0.6f, UnsteadyAttack = 12f, HipSwayShow = 5f;

    // Debug: BRUTALFPS_WEAPONPOSE="reload:0.5" (or "empty:1.0", "draw:0.2", "sprint", ...) holds the gun in that pose,
    // for screenshots (see ApplyDebugPose).
    private static readonly string? DebugPose = Environment.GetEnvironmentVariable("BRUTALFPS_WEAPONPOSE");

    /// <summary>How it sits and kicks: its own, or as an optic fitted to it changes it (see <see cref="Fit"/>).</summary>
    protected GunFeel Feel { get; private set; } = feel;
    private readonly GunFeel _ownFeel = feel;
    protected readonly ShotSpread _spread = spread;
    protected readonly Random _random = new(seed);
    protected PhysicsWorld? Physics => physics;
    protected DebrisSystem Debris => debris;
    protected ProjectileSystem Projectiles => projectiles;
    protected ImpactEffects Effects => effects;

    public abstract string Name { get; }
    public int Capacity { get; } = capacity;
    public abstract string FireMode { get; }
    public abstract bool Reloading { get; }
    public abstract float ReloadProgress { get; }
    public int AmmoInMag { get; protected set; } = capacity;
    public bool Chambered { get; protected set; } = true;
    public int ShotsFired { get; protected set; }

    /// <summary>How far the shot carries to people's ears (<see cref="IGun.Loudness"/>): as loud as it sounds.</summary>
    public float Loudness => ShotVolume;
    public AudioEngine? Audio { get; set; }

    protected float _sinceShot = 10f;          // time since the last shot went off
    protected float _pull;                     // trigger finger, 0..1
    protected bool _triggerHeld;
    protected float _flip, _flipVelocity;      // recoil: muzzle flip (radians)
    protected float _back, _backVelocity;      // recoil: back into the hands (metres)
    protected float _recoilRoll, _recoilYaw;
    protected float2 _viewKick, _viewKickVelocity;
    protected float2 _riseTarget, _rise;       // recoil's walk of the aim (pitch, yaw radians): where it's headed, where it is
    private float _riseHeading;                // direction of the walk (radians from straight up, positive to the right)
    protected float _aim;                      // 0 at the hip, 1 aiming down the sights
    protected float _muzzleFlash, _flashRotation;

    protected float _time, _drawT, _sprint;
    private float _air;
    protected float _strideDistance, _horizontalSpeed;
    private float2 _sway, _swayVelocity;
    private float _dip, _dipVelocity;
    private float _unsteady;                   // 0 settled, 1 fully unsettled (see GunFeel's steadiness)
    private float2 _aimSway;                   // the aim's wander (pitch, yaw radians)
    private float _brace;                      // 0..1: lying prone, the gun rested: a tighter cone, less wander and a gentler walk of the aim
    protected float3 _playerVelocity;
    private int _respawns = -1;
    private bool _skipDraw;
    protected bool _holstering;
    private DynamicMesh? _flashMesh;

    /// <summary>0 at the hip, 1 aiming down the sights (the view narrows with it).</summary>
    public float AimBlend => Smooth(_aim);

    // ------------------------------------------------------------------------------------------------ sights and optics

    /// <summary>The optic fitted at a gun locker (null: its own sights).</summary>
    public FittedOptic? Optic { get; private set; }

    /// <summary>Its own sight, if that's a scope or a reflex sight (null: open sights).</summary>
    protected virtual OpticView? OwnSight => null;

    /// <summary>The sight in use: the fitted optic's, or its own.</summary>
    private OpticView? Sight => Optic?.View ?? OwnSight;

    private float? _scopeZoom;     // (stays where it's set, through reloads and changes of gun; a new optic starts at its own)
    private ScopeVision _vision;   // (likewise)
    private const float ScopeWheelBlend = 0.05f;       // aimed this far up, the mouse wheel no longer changes guns

    /// <summary>Puts an optic on it (null: back to its own sights): its hold changes to look through it.</summary>
    public void Fit(FittedOptic? optic)
    {
        Optic = optic;
        Feel = optic?.Adjust(_ownFeel) ?? _ownFeel;
        _scopeZoom = null;
    }

    /// <summary>Up at the eye (not reloading or putting it away): looking through the sight.</summary>
    private bool AtEye => AimBlend > 0.97f && !Reloading && !_holstering;

    /// <summary>
    /// Fully up to the eye on a magnifying sight (see <see cref="IGun.Scoped"/>): the HUD draws the scope's picture. A
    /// reflex sight at 1x isn't: the gun stays in view, and the HUD lights its dot (<see cref="Reticle"/>).
    /// </summary>
    public virtual bool Scoped => Sight is { } s && AtEye && (s.Kind is ScopeKind.Tube or ScopeKind.Acog || ScopeZoom > 1.01f);
    public virtual float ScopeZoom => _scopeZoom ?? Sight?.Zoom ?? 1f;

    /// <summary>How it's seen through now: a holographic sight with its magnifier flipped in is seen as a prism sight.</summary>
    public virtual ScopeKind ScopeKind => Sight is { } s ? s.Kind is ScopeKind.Holo or ScopeKind.RedDot && ScopeZoom > 1.01f ? ScopeKind.Acog : s.Kind : ScopeKind.Tube;

    /// <summary>A reflex sight looked through at 1x: its reticle (a red dot or a holographic ring), null otherwise.</summary>
    /// <summary>A red dot or holographic sight at 1x: aimed, the view closes in a little more than over open sights (<see cref="IGun.AimZoom"/>).</summary>
    public float3? SightDirection => Reticle is null ? null : float3.Normalize(float3.TransformNormal(new float3(1f, 0f, 0f), GunToCamera()));

    public float AimZoom => Sight is { Kind: ScopeKind.RedDot or ScopeKind.Holo } && ScopeZoom <= 1.01f ? ReflexZoom : 1f;
    private const float ReflexZoom = 1.35f;

    public ScopeKind? Reticle => Sight is { Kind: ScopeKind.RedDot or ScopeKind.Holo } s && !Scoped && !Reloading && !_holstering ? s.Kind : null;

    public virtual bool TurnScope(int steps)
    {
        // At the eye, or on the way there or back: the wheel belongs to the sight, even one with nothing to turn.
        if (Sight is not { } sight || Reloading || _holstering || AimBlend <= ScopeWheelBlend)
            return false;
        if (!AtEye || sight.Steps is not { } powers)
            return true;
        var now = ScopeZoom;
        var turned = powers[int.Clamp(Array.IndexOf(powers, now) + steps, 0, powers.Length - 1)];
        if (turned != now)
            Log.Write($"[GUN] {Name} scope: {turned:0}x");
        _scopeZoom = turned;
        return true;
    }

    public virtual ScopeVision Vision => _vision;

    public virtual void TurnVision()
    {
        if (!Scoped)
            return;
        _vision = _vision switch { ScopeVision.Off => ScopeVision.Night, ScopeVision.Night => ScopeVision.Thermal, _ => ScopeVision.Off };
        Log.Write($"[GUN] {Name} scope vision: {_vision}");
    }

    /// <summary>Draws the fitted optic (and the riser under it on a gun with no rail), <paramref name="gunToWorld"/> being where the gun's frame is.</summary>
    protected void DrawOptic(Renderer renderer, in float4x4 gunToWorld, float3 tint)
    {
        if (Optic is not { } optic)
            return;
        var magnified = ScopeZoom > 1.01f;
        foreach (var part in optic.Parts)
            renderer.DrawPart(part, optic.PartPlacement(part, magnified) * gunToWorld, tint);
        if (optic.Block is { } block)
            foreach (var part in optic.BlockParts)
                renderer.DrawPart(part, block * gunToWorld, tint);
    }

    /// <summary>How fast the player moves with it in hand (see <see cref="IGun.MoveScale"/>); the usual speed by default.</summary>
    public virtual float MoveScale => 1f;

    /// <summary>How far below the point of aim (radians) a round lands at this range (see <see cref="IGun.HoldUnder"/>).</summary>
    public virtual float HoldUnder(float range) => 0f;

    /// <summary>
    /// What recoil and the hands do to the view (pitch, yaw radians): the quick jolt of each shot, the walk of the aim
    /// and its wander. The walk and the wander are real: the next shot goes where the view now points (the crosshair),
    /// the jolt is only for show.
    /// </summary>
    public float2 ViewKick => _viewKick + _rise + _aimSway;

    /// <summary>Radius (radians) of the cone the next shot can land in.</summary>
    public virtual float Spread => ShotCone(AimBlend);

    /// <summary>The cone at this much aiming: the gun's own, opened up by rapid fire, and by hands that haven't settled.</summary>
    protected float ShotCone(float aiming) =>
        (_spread.Radius(aiming) + float.Lerp(Feel.UnsteadySpreadHip, Feel.UnsteadySpreadAimed, aiming) * _unsteady) * (1f - ProneSpreadCut * _brace);

    /// <summary>Prone (the gun rested on the ground, the body braced): how much tighter the cone, the aim's wander and the walk of the aim get.</summary>
    private const float ProneSpreadCut = 0.5f, ProneWanderCut = 0.65f, ProneWalkCut = 0.45f, ProneFlipCut = 0.3f;

    /// <summary>Put away (after Holster), so the other gun can come up.</summary>
    /// <summary>Whether a reload can start: the gun's up, it isn't reloading already, and it isn't full.</summary>
    protected bool CanStartReload => !Reloading && _drawT >= Feel.DrawTime && (AmmoInMag < Capacity || !Chambered);

    protected const string EmptyClick = "[SHOT] Click - empty. Press R to reload.";

    /// <summary>Logs a shot: what the crosshair was on, and where recoil and sway had the aim.</summary>
    protected void LogShot(string tag, string aimedAt, float aimDistance, bool fromEye, in Camera aimed, in Camera camera, float aiming, string right = "right") =>
        Log.Write($"[SHOT] {tag}#{ShotsFired} crosshair on: {aimedAt} ({aimDistance:0.0} m){(fromEye ? ", fired from eye" : "")}; " +
                  $"{AmmoInMag}{(Chambered ? "+1" : "")} left; aim {float.RadiansToDegrees(aimed.Pitch - camera.Pitch):0.00} deg up, " +
                  $"{float.RadiansToDegrees(aimed.Yaw - camera.Yaw):0.00} deg {right}; spread {float.RadiansToDegrees(ShotCone(aiming)):0.00} deg");

    public bool Holstered => _holstering && _drawT <= 0f;

    /// <summary>Starts with the gun already raised (screenshot mode).</summary>
    public void SkipDraw() => _skipDraw = true;

    /// <summary>Lowers the gun out of view; a reload in progress is abandoned.</summary>
    public void Holster()
    {
        if (_holstering)
            return;
        _holstering = true;
        _drawT = float.Min(_drawT, Feel.DrawTime);
        if (Reloading)
        {
            Audio?.Cancel("reload");
            AbandonReload();
        }
    }

    public void HolsterNow()
    {
        Holster();
        _drawT = 0f;
    }

    public void Raise() => _holstering = false;

    // A gun taken from someone's hands (the intro): it comes round from the left into the hold, over this long.
    private const float HandoverTime = 0.3f, HandoverYaw = 0.95f, HandoverTip = 0.3f;
    private float _handover;

    /// <summary>
    /// It's in your hands now, already up, with this many rounds: with <paramref name="swing"/> it comes round from the
    /// left, as if just wrenched away; otherwise it's just there.
    /// </summary>
    public void TakeFrom(bool swing, int rounds, PlayerController player)
    {
        _respawns = player.Respawns;   // (not a respawn's fresh gun, the first time it's updated)
        _holstering = false;
        _drawT = Feel.DrawTime;
        AmmoInMag = int.Clamp(rounds, 0, Capacity);
        Chambered = true;
        _handover = swing ? 1f : 0f;
    }

    /// <summary>A reload cut short by putting the gun away.</summary>
    protected abstract void AbandonReload();

    /// <summary>A respawn: anything of the gun's own to reset (a reload in progress is over).</summary>
    protected abstract void Respawned();

    public abstract void Update(float dt, Input input, in Camera camera, PlayerController player, bool allowFire);
    public abstract void Draw(Renderer renderer, in Camera camera);

    /// <summary>The light a shot gives off this frame (xyz world position, w strength; 0 when there's none).</summary>
    public virtual float4 FlashLight(in Camera camera)
    {
        if (_sinceShot >= Feel.FlashLightTime)
            return float4.Zero;
        var t = _sinceShot / Feel.FlashLightTime;
        var at = float3.Transform(Feel.FlashPoint + new float3(0.03f, 0.01f, 0f), GunToCamera() * camera.World);
        return new float4(at, Feel.FlashLightStrength * (1f - t) * (1f - t));
    }

    // ------------------------------------------------------------------------------------------------ update

    /// <summary>
    /// The start of every frame: clocks, the draw (or putting away), the spread cooling, what the player is doing; and
    /// after a respawn, a fresh, full gun, raised into view.
    /// </summary>
    protected void Tick(float dt, PlayerController player)
    {
        _time += dt;
        _handover = float.Max(0f, _handover - dt / HandoverTime);
        _muzzleFlash = float.Max(0, _muzzleFlash - dt);
        _sinceShot += dt;
        _drawT = _holstering ? float.Max(0f, float.Min(_drawT, Feel.DrawTime) - dt * 1.6f) : _drawT + dt;
        _spread.Update(dt);
        _playerVelocity = player.Velocity;
        _strideDistance = player.StrideDistance;
        var speed = player.Grounded || player.Climbing >= 0 ? float.Sqrt(player.Velocity.X * player.Velocity.X + player.Velocity.Z * player.Velocity.Z) : 0f;
        _horizontalSpeed += (speed - _horizontalSpeed) * float.Min(1f, dt * 10f);
        _brace += (player.ProneAmount - _brace) * float.Min(1f, dt * 4f);

        if (player.Respawns != _respawns)
        {
            _respawns = player.Respawns;
            _drawT = _skipDraw ? Feel.DrawTime : 0;
            _skipDraw = false;
            if (Reloading)
                Audio?.Cancel("reload");
            Respawned();
            AmmoInMag = Capacity;
            Chambered = true;
            _rise = _riseTarget = float2.Zero;
            _unsteady = 0f;
            _spread.Reset();
        }
    }

    /// <summary>The trigger finger follows the trigger (quick to pull, a touch slower to let out).</summary>
    protected void FollowTrigger(float dt) =>
        _pull += ((_triggerHeld ? 1f : 0f) - _pull) * float.Min(1f, dt * (_triggerHeld ? 40f : 18f));

    /// <summary>Aiming, the recoil springs, the walk of the aim recovering, sway, the hands' steadiness, the landing dip, sprinting and the air.</summary>
    protected void UpdateMotion(float dt, Input input, PlayerController player)
    {
        // Aiming (right mouse): not while reloading, putting away or drawing.
        var wantAim = ((input.MouseCaptured && input.IsDown(GlfwMouseButton.Right)) || input.ScriptedAim) && !Reloading && !_holstering && _drawT >= Feel.DrawTime
                      && !player.GettingUp;   // (heaving up off the ground in heavy kit: the gun's down, see below)
        _aim = float.Clamp(_aim + (wantAim ? dt : -dt) / Feel.AimTime, 0f, 1f);

        // Recoil springs (a little under-damped: the gun overshoots a hair and settles).
        StepRecoil(dt);
        Spring(ref _viewKick.X, ref _viewKickVelocity.X, 0f, Feel.ViewKickStiffness, Feel.ViewKickDamping, dt);
        Spring(ref _viewKick.Y, ref _viewKickVelocity.Y, 0f, Feel.ViewKickStiffness, Feel.ViewKickDamping, dt);

        // The walk of the aim: the hands bring it back once you stop firing; the view follows it closely.
        if (Feel.Walk is { } walk)
        {
            if (_sinceShot > walk.RecoverDelay)
            {
                var keep = MathF.Exp(-walk.RecoverRate * dt);
                _riseTarget.X *= keep;
                _riseTarget.Y *= keep;
            }
            var follow = float.Min(1f, dt * walk.RiseFollow);
            _rise.X += (_riseTarget.X - _rise.X) * follow;
            _rise.Y += (_riseTarget.Y - _rise.Y) * follow;
        }

        // Lag behind the mouse: a spring pulled towards the opposite of the look movement.
        var mouse = input.MouseDelta;
        var target = new float2(float.Clamp(-mouse.X * Feel.SwayGain, -Feel.SwayLimit.X, Feel.SwayLimit.X),
            float.Clamp(mouse.Y * Feel.SwayGain, -Feel.SwayLimit.Y, Feel.SwayLimit.Y));
        Spring(ref _sway.X, ref _swayVelocity.X, target.X, Feel.SwayStiffness, Feel.SwayDamping, dt);
        Spring(ref _sway.Y, ref _swayVelocity.Y, target.Y, Feel.SwayStiffness, Feel.SwayDamping, dt);

        // Steadiness: whatever is unsettling the hands most takes hold quickly, and they settle slowly once it stops.
        var moving = player.Grounded && player.Climbing < 0
            ? float.Sqrt(player.Velocity.X * player.Velocity.X + player.Velocity.Z * player.Velocity.Z) / UnsteadySpeed
            : 1f;
        var turnRate = dt > 0f ? float.Sqrt(mouse.X * mouse.X + mouse.Y * mouse.Y) * player.MouseSensitivity * player.LookScale / dt : 0f;
        var turning = (turnRate - TurnFree) / (TurnFull - TurnFree);
        var raising = _aim is > 0f and < 1f ? UnsteadyRaising : 0f;
        var unsettled = float.Clamp(float.Max(float.Max(moving, turning), raising), 0f, 1f);
        if (player.LandingSpeed > 2f)
            _unsteady = 1f;
        _unsteady += (unsettled - _unsteady) * (1f - MathF.Exp(-(unsettled > _unsteady ? UnsteadyAttack : Feel.SettleRate) * dt));

        // The aim's wander: a slow, never-repeating drift, further while unsettled.
        var wander = (Feel.AimSway + Feel.AimSwayUnsteady * _unsteady) * (1f - ProneWanderCut * _brace);
        _aimSway = new float2(
            (float.Sin(_time * 1.1f) * 0.65f + float.Sin(_time * 2.7f + 1f) * 0.35f) * wander,
            (float.Sin(_time * 0.8f + 2f) * 0.65f + float.Sin(_time * 2.1f) * 0.35f) * wander);

        // Landing: a dip that springs back.
        if (player.LandingSpeed > 2f)
            _dipVelocity -= float.Min(player.LandingSpeed, 16f) * 0.02f;
        Spring(ref _dip, ref _dipVelocity, 0f, Feel.DipStiffness, Feel.DipDamping, dt);

        // Sprinting: lowered and turned aside (straight back up when you fire).
        // Heaving up off the ground in heavy kit holds it down the same way, a hand on the knee, until you're up.
        var sprintTarget = (player.Sprinting && !Reloading && _sinceShot > 0.35f && _aim <= 0f) || player.GettingUp ? 1f : 0f;
        _sprint += (sprintTarget - _sprint) * float.Min(1f, dt * (sprintTarget > _sprint ? Feel.SprintIn : Feel.SprintOut));

        // In the air: the gun floats up a little as you fall, and sinks as you rise.
        var airTarget = player.Grounded || player.Climbing >= 0 ? 0f : float.Clamp(-player.Velocity.Y * 0.0012f, -0.012f, 0.015f);
        _air += (airTarget - _air) * float.Min(1f, dt * 8f);
    }

    /// <summary>The recoil springs, in small steps (the flip spring is stiff).</summary>
    protected void StepRecoil(float dt)
    {
        for (var left = dt; left > 1e-6f; left -= 1f / 480f)
        {
            var h = float.Min(left, 1f / 480f);
            Spring(ref _flip, ref _flipVelocity, 0f, Feel.FlipStiffness, Feel.FlipDamping, h);
            Spring(ref _back, ref _backVelocity, 0f, Feel.BackStiffness, Feel.BackDamping, h);
        }
    }

    /// <summary>
    /// BRUTALFPS_WEAPONPOSE: holds the gun in one pose for screenshots. The draw, sprint, aim and recoil poses are
    /// the same for every gun; anything else (the reloads) goes to <see cref="ApplyDebugPose(string, float)"/>.
    /// </summary>
    protected void ApplyDebugPose()
    {
        if (DebugPose is null)
            return;
        var parts = DebugPose.Split(':');
        var t = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0f;
        _drawT = 10f;
        switch (parts[0])
        {
            case "draw":
                _drawT = t;
                break;
            case "sprint":
                _sprint = 1f;
                break;
            case "aim":
                _aim = 1f;
                break;
            case "recoil":
            case "aimrecoil":
                // A shot fired t seconds ago (the average kick), at the hip or aiming.
                _aim = parts[0] == "aimrecoil" ? 1f : 0f;
                _flip = _back = 0f;
                _flipVelocity = float.Lerp(Feel.FlipKick, Feel.FlipKickAiming, AimBlend);
                _backVelocity = Feel.BackKick;
                _recoilRoll = (Feel.RollRange.X + Feel.RollRange.Y) / 2;
                _recoilYaw = 0f;
                StepRecoil(t);
                _sinceShot = t;
                _muzzleFlash = float.Max(0f, Feel.FlashTime - t);
                _flashRotation = 1.3f;
                break;
        }
        ApplyDebugPose(parts[0], t);
    }

    /// <summary>The gun's own part of a debug pose (<paramref name="pose"/> at <paramref name="t"/> seconds).</summary>
    protected virtual void ApplyDebugPose(string pose, float t)
    {
    }

    // ------------------------------------------------------------------------------------------------ firing

    /// <summary>
    /// A shot goes off: the kick (flip, push back, the view's jolt), the flash and the sound; and where it goes: where
    /// the gun points now (the crosshair, after the recoil so far and the aim's wander), give or take its scatter. Then its kick walks
    /// the aim on for the next one and opens the spread. Returns the camera turned onto the shot's line.
    /// </summary>
    protected Camera Discharge(in Camera camera, float aiming, float2 soundPitch)
    {
        _recoilYaw = Range(-1f, 1f);
        _recoilRoll = Range(Feel.RollRange.X, Feel.RollRange.Y);
        _flipVelocity += float.Lerp(Feel.FlipKick, Feel.FlipKickAiming, aiming) * Range(Feel.FlipScatter.X, Feel.FlipScatter.Y) * (1f - ProneFlipCut * _brace);
        _backVelocity += Feel.BackKick * Range(Feel.BackScatter.X, Feel.BackScatter.Y);
        _viewKickVelocity += new float2(Feel.ViewKickPitch * Range(Feel.ViewKickScatter.X, Feel.ViewKickScatter.Y),
            Feel.ViewKickPitch * Feel.ViewKickYawShare * Range(-1f, 1f));

        var aimed = camera;
        var angle = Range(0f, float.Tau);
        var scatter = ShotCone(aiming) * float.Sqrt(Range(0f, 1f));
        aimed.Pitch += _rise.X + _aimSway.X + scatter * float.Cos(angle);
        aimed.Yaw += _rise.Y + _aimSway.Y + scatter * float.Sin(angle);
        if (Feel.Walk is { } walk)
            WalkAim(walk, aiming);
        _spread.Shot();

        _muzzleFlash = Feel.FlashTime;
        _flashRotation = Range(0, float.Tau);
        _sinceShot = 0;
        ShotsFired++;
        Audio?.Play(Audio.Get(ShotSound), ShotVolume, Range(soundPitch.X, soundPitch.Y));
        return aimed;
    }

    /// <summary>
    /// One shot's kick to the aim: a step in a direction that carries on from the last one, turning a little each
    /// shot and now and then swinging round sharply, always drawn back towards climbing. A string of shots walks
    /// up and across in a wandering line, doubling back now and then, rather than going the same way every time.
    /// </summary>
    private void WalkAim(AimWalk walk, float aiming)
    {
        if (_sinceShot > 0.6f)
            _riseHeading = Range(-walk.FreshHeading, walk.FreshHeading);   // a fresh string: off up and to either side
        else if (_random.NextDouble() < walk.TurnChance)
            _riseHeading += Range(-walk.TurnRange, walk.TurnRange);         // a sharp turn
        else
            _riseHeading += Range(walk.Drift.X, walk.Drift.Y);
        _riseHeading *= walk.HeadingKeep;                                   // muzzle flip keeps pulling it back up
        var step = float.Lerp(walk.RiseStepHip, walk.RiseStepAiming, aiming) * Range(0.6f, 1.4f) * (1f - ProneWalkCut * _brace);
        _riseTarget.X += float.Cos(_riseHeading) * step + step * walk.Lift; // (the flip itself always lifts it a bit too)
        _riseTarget.Y += float.Sin(_riseHeading) * step;
        _riseTarget.X = float.Min(_riseTarget.X, walk.MaxRise);
        _riseTarget.Y = float.Clamp(_riseTarget.Y, -walk.MaxRise / 2, walk.MaxRise / 2);
    }

    /// <summary>
    /// Where a shot leaves from and which way. It's aimed at whatever is under the line of <paramref name="forward"/>
    /// (out to <paramref name="maxAimDistance"/>), then raised by the sights' fixed angle, the one that brings a round at
    /// <paramref name="speed"/> back onto the line of sight at <paramref name="zeroDistance"/>: closer than that it flies
    /// a little high, and beyond it it falls away below the crosshair, further the further out. It leaves from the
    /// muzzle, unless the muzzle is poking through a wall, or the aim point is closer than <paramref name="closeRange"/>
    /// (firing from the muzzle would visibly miss): then it's fired straight from the eye. With a <paramref name="round"/>,
    /// the angle allows for its drag too.
    /// </summary>
    protected float3 AimShot(float3 eye, float3 forward, float3 muzzle, float maxAimDistance, float closeRange, float zeroDistance, float speed,
        out float3 spawn, out float aimDistance, out string aimedAt, Cartridge? round = null)
    {
        aimDistance = projectiles.AimDistance(eye, forward, maxAimDistance, out aimedAt);
        var aimPoint = eye + forward * aimDistance;
        spawn = muzzle;
        var toMuzzle = muzzle - eye;
        var muzzleDistance = float.Sqrt(float3.Dot(toMuzzle, toMuzzle));
        var muzzleBlocked = level.Raycast(eye, toMuzzle / muzzleDistance, muzzleDistance, 0.01f, out _, out _)
            || portals.Raycast(eye, toMuzzle / muzzleDistance, muzzleDistance, out _)
            || (physics is not null && physics.Raycast(eye, muzzle, out _, includeBodies: false));
        if (muzzleBlocked || aimDistance < closeRange || MuzzleLineBlocked(muzzle, aimPoint))
            spawn = eye;

        var toAim = aimPoint - spawn;
        var distance = float.Sqrt(float3.Dot(toAim, toAim));
        toAim.Y += distance * projectiles.Drop(zeroDistance, speed, round) / zeroDistance;
        // Guard against a zero-length aim (crosshair right on the eye, e.g. pressed into a wall): Normalize would
        // give NaN, and NaN fails every comparison, so test for "not at least 0.5" rather than "less than 0.5".
        var direction = distance > 1e-4f ? float3.Normalize(toAim) : forward;
        if (!(float3.Dot(direction, forward) >= 0.5f))
            direction = forward;
        return direction;
    }

    /// <summary>Where the barrel ends (model space), for rising over an edge prone; null for something that doesn't shoot.</summary>
    protected virtual float3? BoreMuzzle => Feel.FlashPoint;

    private float _liftHold;                          // s the elbows stay up once the edge is no longer in the way
    private const float MaxProneLift = 0.15f, ProneLiftStep = 0.025f, ProneLiftMargin = 0.02f, ProneLiftHold = 0.5f;

    /// <summary>
    /// Prone, the sights sit above the barrel, so the eye can see over an edge (a ledge, a crest, a low wall) that the
    /// barrel's line would plough into. Then the player comes up on the elbows, just far enough for the barrel to clear
    /// it (up to <see cref="MaxProneLift"/>), so the shot leaves the muzzle and goes where the crosshair is. Measured
    /// from the flat pose, so it doesn't drop the moment it's high enough; it holds a moment once the need passes, so
    /// recoil's kick of the muzzle doesn't bob the view.
    /// </summary>
    protected void RiseOverEdge(float dt, in Camera camera, PlayerController player)
    {
        var need = 0f;
        if (BoreMuzzle is { } point && !_holstering && player.Alive && player.ProneAmount > 0.9f)
        {
            var up = new float3(0f, 1f, 0f);
            var lifted = up * player.ProneLift;
            var forward = camera.Forward;
            var aimPoint = camera.Position + forward * projectiles.AimDistance(camera.Position, forward, 300f, out _);
            var muzzle = float3.Transform(point, GunToCamera() * camera.World) - lifted;
            if (MuzzleLineBlocked(muzzle, aimPoint))
            {
                need = float.NaN;
                for (var h = ProneLiftStep; h <= MaxProneLift + 1e-4f; h += ProneLiftStep)
                    if (!MuzzleLineBlocked(muzzle + up * h, aimPoint))
                    {
                        need = h + ProneLiftMargin;
                        break;
                    }
                if (float.IsNaN(need))
                    need = 0f;   // too high to reach: stay down (the shot goes from the eye)
            }
        }

        if (need >= player.ProneLiftTarget)
        {
            player.ProneLiftTarget = need;
            _liftHold = ProneLiftHold;
        }
        else if ((_liftHold -= dt) <= 0f)
            player.ProneLiftTarget = need;
    }

    /// <summary>
    /// Whether something solid stands between the muzzle and the aim point, short of the aim point itself. The eye sits
    /// above the muzzle, so it can see over an edge (lying prone behind a ledge or a crest, a low wall at the hip) that
    /// the barrel's own line would plough into: the crosshair is clear, so the shot is fired from the eye instead.
    /// </summary>
    private bool MuzzleLineBlocked(float3 muzzle, float3 aimPoint)
    {
        var toAim = aimPoint - muzzle;
        var distance = float.Sqrt(float3.Dot(toAim, toAim));
        // Stop short of the aim point: it's on the surface the crosshair is on, and that one doesn't count.
        var reach = distance - float.Max(0.15f, distance * 0.02f);
        if (reach <= 0.05f)
            return false;
        var direction = toAim / distance;
        return level.Raycast(muzzle, direction, reach, 0.01f, out _, out _)
            || portals.Raycast(muzzle, direction, reach, out _)
            || (physics is not null && physics.Raycast(muzzle, muzzle + direction * reach, out _, includeBodies: false));
    }

    /// <summary>
    /// A spent case (or hull) out of the port at <paramref name="port"/> (model space): <paramref name="localVelocity"/>
    /// in the gun's frame, on top of the player's, spinning about <paramref name="spinAxis"/>. With a
    /// <paramref name="round"/>, that calibre's case.
    /// </summary>
    protected void EjectCasing(in Camera camera, in float4x4 gunToWorld, float3 port, float3 localVelocity, float3 spinAxis, float2 spinRate, Cartridge? round = null)
    {
        var pose = float4x4.CreateRotationY(float.Pi / 2) * float4x4.CreateTranslation(port) * gunToWorld;
        PullInFromWall(camera.Position, ref pose, 0.03f);
        var velocity = float3.TransformNormal(localVelocity, gunToWorld) + _playerVelocity;
        var spin = float3.Normalize(float3.TransformNormal(spinAxis, gunToWorld)) * Range(spinRate.X, spinRate.Y);
        debris.Casing(pose, velocity, spin, round);
    }

    /// <summary>
    /// The view model is drawn over the world, so near a wall it can be "inside" it: a body leaving it (a case, a
    /// magazine) starts on this side, <paramref name="gap"/> short of the wall.
    /// </summary>
    protected void PullInFromWall(float3 eye, ref float4x4 pose, float gap)
    {
        var position = pose.W.XYZ;
        if (physics is not null && physics.Raycast(eye, position, out var hit, includeBodies: false))
            pose.W = new float4(hit.Point + float3.Normalize(eye - position) * gap, 1f);
    }

    // ------------------------------------------------------------------------------------------------ poses

    /// <summary>0..1: how far into the gun's reload pose it is (rolled and tipped towards the left hand).</summary>
    protected abstract float ReloadPose { get; }

    /// <summary>Gun model space -> camera space: the gun and the hands holding it.</summary>
    protected float4x4 GunToCamera()
    {
        var pose = GunRecoil() * BasePose();
        if (_handover <= 0f)
            return pose;
        var left = Smooth(_handover);
        return RotateAbout(Feel.GripPivot, float4x4.CreateRotationY(HandoverYaw * left) * float4x4.CreateRotationZ(HandoverTip * left)) * pose;
    }

    /// <summary>Model space -> camera space for the arms: they're pushed back with the gun, but take little of its flip.</summary>
    protected float4x4 ArmsToCamera() =>
        float4x4.CreateTranslation(-Feel.ArmsBack * _back, Feel.ArmsLift * _back + Feel.ArmsFlipLift * _flip, 0) * BasePose();

    /// <summary>Recoil (model space): the muzzle flips up about the pivot, rolling a little to the left and swinging a
    /// little aside, and the gun comes back.</summary>
    private float4x4 GunRecoil() =>
        RotateAbout(Feel.RecoilPivot,
            float4x4.CreateRotationZ(_flip)
            * float4x4.CreateRotationX(-_recoilRoll * _flip)
            * float4x4.CreateRotationY(Feel.RecoilYawShare * _recoilYaw * _flip))
        * float4x4.CreateTranslation(-_back, Feel.RecoilLift * _back, 0);

    /// <summary>
    /// The hold without recoil: the reload tilt, sprinting and the draw turn the gun about the grip; then the line of
    /// its sights is put at the hip or aim position (turned in a little at the hip), with sway, bob, dip and breathing.
    /// </summary>
    private float4x4 BasePose()
    {
        var f = Feel;
        var reload = ReloadPose;
        var draw = 1f - Smooth(_drawT / f.DrawTime);
        var sprint = _sprint;
        var aim = AimBlend;
        var steady = 1f - f.Steady * aim;   // aiming holds the gun steadier

        // Turns about the grip, in the gun's own frame: X rolls it about the barrel, Z tips the muzzle up.
        var roll = f.ReloadRoll * reload + f.SprintRoll * sprint;
        var pitch = f.ReloadPitch * reload - f.SprintPitch * sprint - f.DrawPitch * draw;

        // Walk bob (stronger when running), breathing when still.
        var bobAmount = float.Min(_horizontalSpeed / 9f, 1f) * (1f + 0.6f * sprint) * steady;
        var bob = new float3(
            float.Sin(_strideDistance * 1.6f) * f.StrideBob.X * bobAmount,
            -float.Abs(float.Cos(_strideDistance * 1.6f)) * f.StrideBob.Y * bobAmount,
            0);
        var still = 1f - float.Min(_horizontalSpeed / 2f, 1f);
        bob += new float3(float.Sin(_time * 0.9f) * f.Breath.X, float.Sin(_time * 1.6f) * f.Breath.Y, 0) * still * steady;

        var position = float3.Lerp(f.HipPosition, f.AimPosition, aim) + bob
            + f.ReloadShift * reload
            + f.SprintShift * sprint
            + f.DrawShift * draw
            + new float3(_sway.X * 0.35f * steady, -_sway.Y * 0.3f * steady + _dip + _air, 0);

        return RotateAbout(f.GripPivot, float4x4.CreateRotationX(roll) * float4x4.CreateRotationZ(pitch))
            * float4x4.CreateTranslation(-f.SightPoint)
            * float4x4.CreateRotationY(float.Pi / 2 + f.HipYaw * (1f - aim) + f.SprintYaw * sprint)   // muzzle down -Z
            * float4x4.CreateRotationX((_sway.Y - _dip * 2f) * steady + _aimSway.X * HipSwayShow * (1f - aim))
            * float4x4.CreateRotationY(_sway.X * steady + _aimSway.Y * HipSwayShow * (1f - aim))
            * float4x4.CreateTranslation(position);
    }

    // ------------------------------------------------------------------------------------------------ drawing

    /// <summary>
    /// The sleeves of <paramref name="model"/> (if it has them), with each vertex's weight (1 at the cuff, 0 from
    /// CuffBlend up the arm) stored in its tangent's w, so the cuffs can follow the hands and the rest the arms (the
    /// hands can flip in recoil, or go off to the magazine, without the arms swinging). <paramref name="material"/>,
    /// if given, can swap a sleeve's material.
    /// </summary>
    protected static Dictionary<string, ModelPart> SkinSleeves(Model? model, in ArmRig arms, Func<PartMaterial, PartMaterial>? material = null)
    {
        var result = new Dictionary<string, ModelPart>();
        if (model is null)
            return result;
        foreach (var part in model.Parts)
        {
            if (part.Name is not ("Sleeve" or "SleeveLeft") || part.Vertices.Length == 0)
                continue;
            var (wrist, arm) = part.Name == "Sleeve" ? (arms.RightWrist, arms.RightArm) : (arms.LeftWrist, arms.LeftArm);
            var v = (float[])part.Vertices.Clone();
            for (var i = 0; i < v.Length; i += Mesh.FullStride)
            {
                var along = float3.Dot(new float3(v[i], v[i + 1], v[i + 2]) - wrist, arm) - SleeveStart;
                var w = 1f - Smooth(along / CuffBlend);
                v[i + 11] = (v[i + 11] < 0 ? -1f : 1f) * (1f + w);
            }
            result[part.Name] = new ModelPart(part.Name, new Mesh(v, Mesh.FullStride), material?.Invoke(part.Material) ?? part.Material, v);
        }
        return result;
    }

    /// <summary>The skinned sleeves (see <see cref="SkinSleeves"/>), by part name.</summary>
    protected abstract IReadOnlyDictionary<string, ModelPart> SkinnedSleeves { get; }

    /// <summary>
    /// Draws <paramref name="part"/> if it's one of the hands: the right hand on the grip, its trigger finger, the left
    /// hand where the reload has it, and the sleeves (the cuff follows the hand, the rest of the sleeve the arm).
    /// False if it isn't one (or is a sleeve that couldn't be skinned).
    /// </summary>
    protected bool DrawHandPart(Renderer renderer, ModelPart part, in HandsPose pose)
    {
        switch (part.Name)
        {
            case "Sleeve" or "SleeveLeft":
                if (!SkinnedSleeves.TryGetValue(part.Name, out var sleeve))
                    return false;
                var left = part.Name == "SleeveLeft";
                renderer.DrawSkinnedPart(sleeve, left ? pose.Support * pose.Arms : pose.Arms, left ? pose.SupportToWorld : pose.Gun, SleeveTint);
                return true;
            case "Hand":
                renderer.DrawPart(part, pose.Gun, SleeveTint);
                return true;
            case "TriggerFinger":
                renderer.DrawPart(part, pose.Finger, SleeveTint);
                return true;
            case "HandLeft":
                renderer.DrawPart(part, pose.SupportToWorld, SleeveTint);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The muzzle flash: two glowing stars (drawn after the gun, on top, additively), a big orange one of
    /// <paramref name="size"/> and a hot core of <paramref name="coreSize"/>, both dying away over the flash.
    /// </summary>
    protected void DrawMuzzleFlash(Renderer renderer, float3 muzzle, float size, float coreSize)
    {
        BeginFlash();
        var life = _muzzleFlash / Feel.FlashTime;
        FlashStar(muzzle, size * (0.7f + 0.5f * life), _flashRotation, new float3(3.4f, 1.3f, 0.35f), life);
        FlashStar(muzzle, coreSize, _flashRotation + 0.6f, new float3(6f, 4f, 1.8f), life);
        EndFlash(renderer);
    }

    /// <summary>Starts a frame's flash (stars added with <see cref="FlashStar"/>, drawn by <see cref="EndFlash"/>).</summary>
    protected void BeginFlash()
    {
        _flashMesh ??= new DynamicMesh();
        _flashMesh.Clear();
    }

    /// <summary>One glowing star (a camera-facing quad, its colour HDR, faded by <paramref name="alpha"/>).</summary>
    protected void FlashStar(float3 position, float halfSize, float rotation, float3 color, float alpha)
    {
        var packed = new float4(color, 5 + 0.05f + 0.9f * float.Clamp(alpha, 0f, 1f));
        var n = new float3(rotation, 0, 0);
        _flashMesh!.Vertex(position, n, new float2(-halfSize, -halfSize), packed);
        _flashMesh.Vertex(position, n, new float2(halfSize, -halfSize), packed);
        _flashMesh.Vertex(position, n, new float2(halfSize, halfSize), packed);
        _flashMesh.Vertex(position, n, new float2(-halfSize, -halfSize), packed);
        _flashMesh.Vertex(position, n, new float2(halfSize, halfSize), packed);
        _flashMesh.Vertex(position, n, new float2(-halfSize, halfSize), packed);
    }

    protected void EndFlash(Renderer renderer) => renderer.DrawParticles(null, _flashMesh!);

    // ------------------------------------------------------------------------------------------------ helpers

    protected static void Spring(ref float x, ref float v, float target, float stiffness, float damping, float dt)
    {
        v += ((target - x) * stiffness - v * damping) * dt;
        x += v * dt;
    }

    protected static float Smooth(float t)
    {
        t = float.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    protected static float Square(float t)
    {
        t = float.Clamp(t, 0f, 1f);
        return t * t;
    }

    protected float Range(float min, float max) => min + (float)_random.NextDouble() * (max - min);

    protected static float4x4 RotateAbout(float3 pivot, in float4x4 rotation) =>
        float4x4.CreateTranslation(-pivot) * rotation * float4x4.CreateTranslation(pivot);

    /// <summary>Between two rigid transforms (rotation slerped, translation lerped).</summary>
    protected static float4x4 Blend(in float4x4 a, in float4x4 b, float t)
    {
        var qa = floatQuat.CreateFromRotationMatrix(a);
        var qb = floatQuat.CreateFromRotationMatrix(b);
        var m = float4x4.CreateFromQuaternion(floatQuat.Slerp(qa, qb, t));
        m.Translation = float3.Lerp(a.Translation, b.Translation, t);
        return m;
    }
}
