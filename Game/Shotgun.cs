using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// A pump-action 12 gauge shotgun (7 + 1 rounds) held in both hands. Click to fire a load of buckshot (a cone of
/// pellets); the left hand then racks the pump back and forward (out flies the hull, in goes the next shell). R loads
/// shells one at a time through the port underneath; firing during a reload stops it after the shell in hand, and an
/// empty chamber is racked when the loading's done.
/// <para>
/// The view model is Assets/Models/shotgun.glb (Tools/blender/build_shotgun.py, points: shotgun_points.json). Its grip,
/// trigger and guard are the AK-47's, and its pump's section the AK's lower handguard's, so it borrows the AK's hands
/// and sleeves (<paramref name="hands"/>): the right hand as it is, the left shifted onto the pump by LeftHandShift.
/// </para>
/// </summary>
public sealed class Shotgun(
    Level level,
    PortalSystem portals,
    ProjectileSystem projectiles,
    ImpactEffects effects,
    DebrisSystem debris,
    PhysicsWorld? physics,
    Model model,
    Model? hands)
    : GunBase(level, portals, projectiles, effects, debris, physics, ShotgunFeel,
        // The aim's own error (the pattern comes on top).
        new ShotSpread(hip: 0.012f, aimed: 0.002f, hipBloom: 0.02f, aimedBloom: 0.008f, perShot: 0.5f, coolDelay: 0.3f, coolRate: 1.2f),
        capacity: 7, seed: 1212)
{
    public override string Name => "Shotgun";
    public override string FireMode => "PUMP";
    public int Pellets = 8;
    public float PelletCone = 0.040f, PelletConeAiming = 0.030f;   // rad, the pattern's radius
    public float ProjectileSpeed = 400f;
    public float MaxAimDistance = 200f;
    public float ZeroDistance = 25f;     // m: where the shot crosses the line of sight (and falls below it beyond)

    public override bool Reloading => _phase != ReloadPhase.None;
    public override float ReloadProgress => Reloading ? float.Clamp((float)AmmoInMag / Capacity, 0f, 1f) : 0f;
    public override float Spread => base.Spread + float.Lerp(PelletCone, PelletConeAiming, AimBlend);

    private const string ClackSound = "Weapons/pistol_mag_out", ClickSound = "Weapons/pistol_mag_in";
    private const float ClackAt = 0.10f, ClickAt = 0.45f;   // where in those clips the noise is

    // --- Model space (metres): +X out of the muzzle, +Y up, -Z the gun's left side; x = 0 at the trigger. ---
    private static readonly float3 MuzzlePoint = new(0.640f, 0f, 0f);
    private static readonly float3 EjectionPort = new(0.062f, 0.003f, 0.016f);
    private static readonly float3 TriggerPivot = new(0.001f, -0.036f, 0f);
    private static readonly float3 FingerKnuckle = new(-0.05045f, -0.05628f, 0.0231f);
    private const float PumpTravel = 0.090f;
    private static readonly float4x4 LeftHandShift = float4x4.CreateTranslation(0.045f, -0.0184f, 0f);

    private static readonly GunFeel ShotgunFeel = new()
    {
        // SightPoint is a little over the rib (the bead just shows over the receiver), where the AK's rear sight is:
        // the gun sits in the hands as the AK does.
        SightPoint = new(0.1612f, 0.030f, 0f),
        GripPivot = new(-0.060f, -0.082f, 0f),
        HipPosition = new(0.092f, -0.060f, -0.315f),
        AimTime = 0.22f,
        DrawTime = 0.55f,
        // Recoil: a heavy shove into the shoulder and a big muzzle climb (and no walk of the aim: one shot, then the pump).
        RecoilPivot = new(-0.30f, -0.06f, 0f),
        FlipKick = 5.5f, FlipKickAiming = 4.2f,
        FlipStiffness = 300f, FlipDamping = 24f,
        BackKick = 0.95f,
        ViewKickPitch = 0.24f,
        BackStiffness = 500f, BackDamping = 40f,
        ViewKickStiffness = 220f, ViewKickDamping = 26f,
        FlipScatter = new(0.9f, 1.1f),
        ViewKickScatter = new(0.85f, 1.15f),
        ViewKickYawShare = 0.4f,
        // The reload: rolled further over than the AK (the loading port faces the left hand), tipped up less.
        ReloadRoll = 0.55f,
        ReloadPitch = 0.15f,
        ReloadShift = new(-0.04f, 0.03f, -0.01f),
        FlashTime = 0.06f,
        FlashPoint = new(0.640f, 0f, 0f),
        FlashLightTime = 0.06f, FlashLightStrength = 0.12f,
    };

    // The pump: back, a beat, forward.
    private const float PumpBack = 0.13f, PumpHold = 0.05f, PumpForward = 0.13f;
    private const float PumpDelayAfterShot = 0.22f, RackDelay = 0.15f;

    // Loading: the left hand goes from the pump to a shell (down by the belt), then up into the loading port, one
    // shell at a time.
    private enum ReloadPhase { None, ToShell, Loading, Back }
    private const float ToShellTime = 0.30f, ShellTime = 0.62f, PushAt = 0.75f, BackTime = 0.30f;
    private static readonly float4x4 LoadHold = float4x4.CreateTranslation(-0.205f, 0.004f, 0.004f);
    private static readonly float4x4 PushHold = float4x4.CreateTranslation(-0.205f, 0.016f, 0.004f);
    private static readonly float4x4 FetchHold = float4x4.CreateTranslation(-0.22f, -0.13f, -0.07f);

    private float _pumpT = -1f;             // time into the pump cycle (negative: waiting; -1 and not pumping: idle)
    private bool _pumping, _pumpEject, _pumpEjected;

    private ReloadPhase _phase;
    private float _phaseT;
    private bool _shellIn, _stopLoading;

    private readonly Dictionary<string, ModelPart> _skinnedSleeves = SkinSleeves(hands, ArmRig.Ak47);
    protected override IReadOnlyDictionary<string, ModelPart> SkinnedSleeves => _skinnedSleeves;

    protected override void AbandonReload() => _phase = ReloadPhase.None;

    protected override void Respawned()
    {
        _phase = ReloadPhase.None;
        _pumping = false;
        Audio?.Cancel("pump");      // a pump queued before the respawn would otherwise still clack
    }

    // ------------------------------------------------------------------------------------------------ update

    public override void Update(float dt, Input input, in Camera camera, PlayerController player, bool allowFire)
    {
        Tick(dt, player);

        var clicked = input.WasPressed(GlfwMouseButton.Left) || input.ScriptedFire;
        if (allowFire && !_holstering && input.WasPressed(GlfwKey.R))
            StartReload();
        if (Reloading && clicked)
            _stopLoading = true;      // finish the shell in hand, then get back on the pump

        var ready = !Reloading && !_pumping && !_holstering && _drawT >= Feel.DrawTime * 0.8f;
        var held = (input.IsDown(GlfwMouseButton.Left) && input.MouseCaptured) || input.ScriptedFire;
        _triggerHeld = allowFire && ready && held;
        if (allowFire && ready && clicked)
        {
            if (Chambered)
                Fire(camera);
            else if (AmmoInMag > 0)
                StartPump(RackDelay, eject: false);
            else
                Log.Write("[SHOT] Click - empty. Press R to load shells.");
        }

        FollowTrigger(dt);

        UpdatePump(dt, camera);
        if (Reloading)
            UpdateReload(dt);

        UpdateMotion(dt, input, player);
        RiseOverEdge(dt, camera, player);
    }

    private void StartPump(float delay, bool eject)
    {
        _pumping = true;
        _pumpT = -delay;
        _pumpEject = eject;
        _pumpEjected = false;
        if (Audio is not null)
        {
            Audio.Play(Audio.Get(ClackSound), 0.9f, 0.62f, delay: float.Max(0f, delay + PumpBack - ClackAt), tag: "pump");
            Audio.Play(Audio.Get(ClickSound), 1.0f, 0.60f, delay: float.Max(0f, delay + PumpBack + PumpHold + PumpForward - ClickAt), tag: "pump");
        }
    }

    private void UpdatePump(float dt, in Camera camera)
    {
        if (!_pumping)
            return;
        _pumpT += dt;
        if (!_pumpEjected && _pumpT >= PumpBack)
        {
            _pumpEjected = true;
            if (_pumpEject)
                EjectHull(camera, GunToCamera() * camera.World);
        }
        if (_pumpT >= PumpBack + PumpHold + PumpForward)
        {
            _pumping = false;
            if (AmmoInMag > 0)
            {
                AmmoInMag--;
                Chambered = true;
            }
            _flipVelocity += 0.4f;     // slammed home
        }
    }

    /// <summary>The pump: 0 forward, 1 fully back.</summary>
    private float PumpPosition
    {
        get
        {
            if (!_pumping || _pumpT < 0)
                return 0f;
            var t = _pumpT;
            return t < PumpBack ? Smooth(t / PumpBack)
                : t < PumpBack + PumpHold ? 1f
                : 1f - Smooth((t - PumpBack - PumpHold) / PumpForward);
        }
    }

    private void StartReload()
    {
        if (Reloading || _pumping || _drawT < Feel.DrawTime || AmmoInMag >= Capacity)
            return;
        _phase = ReloadPhase.ToShell;
        _phaseT = 0;
        _stopLoading = false;
        Log.Write($"[RELOAD] Shotgun: loading shells ({AmmoInMag} in the tube{(Chambered ? ", one chambered" : "")})");
    }

    private void UpdateReload(float dt)
    {
        _phaseT += dt;
        switch (_phase)
        {
            case ReloadPhase.ToShell when _phaseT >= ToShellTime:
                BeginShell();
                break;
            case ReloadPhase.Loading:
                if (!_shellIn && _phaseT >= ShellTime * PushAt)
                {
                    _shellIn = true;
                    AmmoInMag++;
                    _flipVelocity += 0.15f;
                }
                if (_phaseT >= ShellTime)
                {
                    if (AmmoInMag >= Capacity || _stopLoading)
                    {
                        _phase = ReloadPhase.Back;
                        _phaseT = 0;
                    }
                    else
                        BeginShell();
                }
                break;
            case ReloadPhase.Back when _phaseT >= BackTime:
                _phase = ReloadPhase.None;
                if (!Chambered && AmmoInMag > 0)
                    StartPump(RackDelay, eject: false);
                break;
        }
    }

    private void BeginShell()
    {
        _phase = ReloadPhase.Loading;
        _phaseT = 0;
        _shellIn = false;
        Audio?.Play(Audio.Get(ClickSound), 0.65f, 1.15f, delay: float.Max(0f, ShellTime * PushAt - ClickAt), tag: "reload");
    }

    /// <summary>The left hand's place relative to its hold on the pump (identity = on the pump).</summary>
    private float4x4 SupportHandTransform()
    {
        var t = _phaseT;
        switch (_phase)
        {
            case ReloadPhase.ToShell:
                return Blend(float4x4.Identity, FetchHold, Smooth(t / ToShellTime));
            case ReloadPhase.Loading:
            {
                var u = t / ShellTime;
                if (u < 0.55f)
                    return Blend(FetchHold, LoadHold, Smooth(u / 0.55f));
                if (u < PushAt)
                    return Blend(LoadHold, PushHold, Smooth((u - 0.55f) / (PushAt - 0.55f)));
                return Blend(PushHold, FetchHold, Smooth((u - PushAt) / (1f - PushAt)));
            }
            case ReloadPhase.Back:
                return Blend(FetchHold, float4x4.Identity, Smooth(t / BackTime));
            default:
                return float4x4.CreateTranslation(-PumpTravel * PumpPosition, 0, 0);
        }
    }

    // ------------------------------------------------------------------------------------------------ firing

    private void Fire(in Camera camera)
    {
        var aiming = AimBlend;
        var aimed = Discharge(camera, aiming, new float2(0.58f, 0.64f));
        Chambered = false;
        StartPump(PumpDelayAfterShot, eject: true);

        var gunToWorld = GunToCamera() * camera.World;
        var eye = camera.Position;
        var forward = aimed.Forward;
        var muzzle = float3.Transform(MuzzlePoint, gunToWorld);
        var direction = AimShot(eye, forward, muzzle, MaxAimDistance, 2f, ZeroDistance, ProjectileSpeed, out var spawn, out var aimDistance, out var aimedAt);

        // The pattern: each pellet off the aimed line by up to the cone's radius (evenly over the disc).
        var right = float3.Normalize(float3.Cross(forward, float3.UnitY));
        var up = float3.Cross(right, forward);
        var cone = float.Lerp(PelletCone, PelletConeAiming, aiming);
        for (var i = 0; i < Pellets; i++)
        {
            var a = Range(0f, float.Tau);
            var r = cone * float.Sqrt(Range(0f, 1f));
            var pellet = float3.Normalize(direction + (right * float.Cos(a) + up * float.Sin(a)) * r);
            Projectiles.Spawn(spawn, pellet * ProjectileSpeed * Range(0.96f, 1.04f));
        }

        Effects.MuzzleSmoke(muzzle, forward, _playerVelocity);
        Log.Write($"[SHOT] Shotgun #{ShotsFired} crosshair on: {aimedAt} ({aimDistance:0.0} m){(spawn == eye ? ", fired from eye" : "")}; " +
                  $"{AmmoInMag} in the tube; {Pellets} pellets in a {float.RadiansToDegrees(cone):0.0} deg cone");
    }

    private void EjectHull(in Camera camera, in float4x4 gunToWorld) =>
        EjectCasing(camera, gunToWorld, EjectionPort, new float3(Range(-0.2f, 0.3f), Range(1.2f, 1.8f), Range(2.2f, 3.0f)),
            new float3(0.3f, 1f, 0.2f), new float2(12f, 22f));

    // ------------------------------------------------------------------------------------------------ poses

    /// <summary>0..1: the gun rolled over to show its loading port to the left hand.</summary>
    protected override float ReloadPose => _phase switch
    {
        ReloadPhase.ToShell => Smooth(_phaseT / ToShellTime),
        ReloadPhase.Loading => 1f,
        ReloadPhase.Back => 1f - Smooth(_phaseT / BackTime),
        _ => 0f,
    };

    // ------------------------------------------------------------------------------------------------ drawing

    public override void Draw(Renderer renderer, in Camera camera)
    {
        var gunToWorld = GunToCamera() * camera.World;
        renderer.SetViewModel(true);
        float4x4.Invert(gunToWorld, out var worldToGun);
        renderer.SetTextureSpace(worldToGun);

        var pumpToWorld = float4x4.CreateTranslation(-PumpTravel * PumpPosition, 0, 0) * gunToWorld;
        var triggerToWorld = RotateAbout(TriggerPivot, float4x4.CreateRotationZ(-0.25f * _pull)) * gunToWorld;
        foreach (var part in model.Parts)
        {
            var at = part.Name switch
            {
                "Pump" or "Bolt" => pumpToWorld,
                "Trigger" => triggerToWorld,
                _ => gunToWorld,
            };
            renderer.DrawPart(part, at, SleeveTint);
        }

        if (hands is not null)
        {
            var fingerToWorld = RotateAbout(FingerKnuckle, float4x4.CreateRotationY(0.16f * _pull) * float4x4.CreateRotationZ(-0.04f * _pull)) * gunToWorld;
            var support = LeftHandShift * SupportHandTransform();
            var pose = new HandsPose(gunToWorld, fingerToWorld, support, support * gunToWorld, ArmsToCamera() * camera.World);
            foreach (var part in hands.Parts)
                DrawHandPart(renderer, part, pose);
        }

        renderer.SetTextureSpace(float4x4.Identity);
        renderer.SetViewModel(false);

        if (_muzzleFlash > 0)
            DrawMuzzleFlash(renderer, float3.Transform(MuzzlePoint + new float3(0.02f, 0, 0), gunToWorld), 0.08f, 0.035f);
    }
}
