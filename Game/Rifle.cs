using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// A magazine-fed automatic long gun held in both hands: the right hand round the pistol grip with the index finger
/// on the trigger, the left hand cupping the handguard from below. Hold the left mouse button to fire, the right to aim
/// down the sights, R to reload, B for the selector. What it is (the AK-47, the UMP40, the AR-15 or the G3) is its <see cref="RifleSpec"/>.
/// <para>
/// Each shot: the trigger finger pulls, the bolt slams back and forward, a case flies out to the right, and the gun
/// bucks into the shoulder and its muzzle climbs. Held down, the shots walk the aim up and off to the side (the view
/// follows) and the spread opens up (see <see cref="ShotSpread"/>): short bursts and single aimed shots stay tight,
/// long strings spray.
/// </para>
/// <para>
/// Reloading: the gun is tipped up and rolled so the magazine faces the left hand, which leaves the handguard and
/// takes the magazine; it's rocked forward off its catch and pulled away, and falls. The hand goes down for a fresh
/// one, starts it into the well and rocks / pushes it home until it clicks. After an empty magazine the hand then goes
/// to the charging handle and pulls it back to chamber a round.
/// </para>
/// The hands and sleeves are part of <paramref name="model"/> (the AK's), or come from <paramref name="hands"/> (the
/// UMP40 and the AR-15 borrow the AK's: the right hand as it is, the left moved onto the handguard by RifleSpec.LeftHandShift).
/// </summary>
public sealed class Rifle(
    Level level,
    PortalSystem portals,
    ProjectileSystem projectiles,
    ImpactEffects effects,
    DebrisSystem debris,
    PhysicsWorld? physics,
    Model model,
    Model? hands,
    RifleSpec spec)
    : GunBase(level, portals, projectiles, effects, debris, physics, spec.Feel, spec.NewSpread(), spec.Capacity, spec.Seed)
{
    private readonly RifleSpec _spec = spec;

    /// <summary>What it is (the gun locker reads its numbers).</summary>
    public RifleSpec Spec => _spec;

    public override string Name => _spec.Name;

    /// <summary>The selector: fully automatic (fires while the trigger is held) or semi-automatic (one shot per click). B switches.</summary>
    public bool FullAuto { get; private set; } = !spec.SemiOnly;
    public override string FireMode => FullAuto ? "AUTO" : "SEMI";
    public override float MoveScale => _spec.MoveScale;
    protected override float ShotVolume => _spec.ShotVolume;

    /// <summary>Its own sight, if it comes with a scope or a reflex sight (the gun locker can fit another: see <see cref="Optics"/>).</summary>
    protected override OpticView? OwnSight => _ownSight;
    private readonly OpticView? _ownSight = spec.ScopeZoom is { } zoom ? new OpticView(spec.ScopeKind, zoom, spec.ScopeSteps) : null;

    public override float HoldUnder(float range) =>
        Projectiles.Drop(range, _spec.ProjectileSpeed, _spec.Cartridge) / range
        - Projectiles.Drop(_spec.ZeroDistance, _spec.ProjectileSpeed, _spec.Cartridge) / _spec.ZeroDistance;

    public override bool Reloading => _reloading;
    public override float ReloadProgress => Reloading ? float.Clamp(_reloadT / ReloadLength, 0f, 1f) : 0f;

    // Sounds: the pistol's clips, pitched down (a bigger gun, a bigger magazine). The clicks' times within the clips.
    private const string MagOutSound = "Weapons/pistol_mag_out", MagInSound = "Weapons/pistol_mag_in";
    private const float MagOutClick = 0.10f, MagInClick = 0.45f;

    private float ReloadLength => _reloadEmpty ? _spec.EmptyReloadLength : _spec.ReloadLength;
    private float BackOnGuardStart => _reloadEmpty ? _spec.RackRelease + 0.08f : _spec.SeatNew + 0.05f;
    private const float BackOnGuardTime = 0.36f;

    private readonly float4x4 _magHoldInverse = Inverted(spec.MagHold);
    private readonly Model? _hands = hands;
    private readonly Dictionary<string, ModelPart> _skinnedSleeves = SkinSleeves(hands ?? model, ArmRig.Ak47);
    protected override IReadOnlyDictionary<string, ModelPart> SkinnedSleeves => _skinnedSleeves;

    private float _cooldown;
    private bool _reloading;
    private float _reloadT;
    private bool _reloadEmpty, _oldMagDropped, _newMagSeated, _racked;
    private int _oldMagRounds;

    protected override void AbandonReload()
    {
        // Whatever magazine is in the gun stays; one already dropped is gone. A fresh magazine seated after an empty
        // one is racked on the way down (otherwise the gun would come back up full but unchambered, and the only way
        // to chamber it would be to reload again, dropping a full magazine).
        _reloading = false;
        if (_oldMagDropped && !_newMagSeated)
            AmmoInMag = 0;
        else if (_reloadEmpty && _newMagSeated && !_racked)
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

        if (allowFire && !_holstering && !_spec.SemiOnly && input.WasPressed(GlfwKey.B))
        {
            FullAuto = !FullAuto;
            Audio?.Play(Audio.Get(MagInSound), 0.7f, 0.95f); // the selector lever clicking over
            Log.Write($"[GUN] {Name} selector: {FireMode}");
        }

        // Fully automatic: fires every AutoFireInterval while the trigger is held. Semi-automatic: once per click (no
        // faster than FireInterval). A click on an empty chamber just clicks.
        var ready = !Reloading && !_holstering && _drawT >= Feel.DrawTime * 0.8f;
        var held = (input.IsDown(GlfwMouseButton.Left) && input.MouseCaptured) || input.ScriptedFire || input.WasPressed(GlfwMouseButton.Left);
        _triggerHeld = allowFire && ready && held;
        var pulled = FullAuto || input.WasPressed(GlfwMouseButton.Left) || input.ScriptedFire;
        if (_triggerHeld && pulled && _cooldown <= 0)
        {
            if (Chambered)
            {
                _cooldown = FullAuto ? _spec.AutoFireInterval : _spec.FireInterval;
                Fire(camera);
            }
            else if (input.WasPressed(GlfwMouseButton.Left))
            {
                _cooldown = _spec.FireInterval;
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
        _oldMagRounds = AmmoInMag;
        _oldMagDropped = _newMagSeated = _racked = false;
        if (Audio is not null)
        {
            Audio.Play(Audio.Get(MagOutSound), 0.9f, _spec.MagOutPitch, delay: _spec.RockOutEnd - MagOutClick, tag: "reload");
            Audio.Play(Audio.Get(MagInSound), 1.0f, _spec.MagInPitch, delay: _spec.SeatNew - MagInClick, tag: "reload");
            if (_reloadEmpty)
            {
                Audio.Play(Audio.Get(MagOutSound), 0.8f, _spec.RackBackPitch, delay: _spec.RackBack - MagOutClick, tag: "reload");
                Audio.Play(Audio.Get(MagInSound), 1.0f, _spec.RackReleasePitch, delay: _spec.RackRelease - MagInClick + 0.02f, tag: "reload");
            }
        }
        Log.Write($"[RELOAD] {Name} {(_reloadEmpty ? "empty" : "tactical")} reload ({AmmoInMag} left in the magazine)");
    }

    private void UpdateReload(float dt, in Camera camera)
    {
        _reloadT += dt;
        if (!_oldMagDropped && _reloadT >= _spec.LetGoOld)
        {
            _oldMagDropped = true;
            AmmoInMag = 0;
            DropMagazine(camera);
        }

        if (!_newMagSeated && _reloadT >= _spec.SeatNew)
        {
            _newMagSeated = true;
            AmmoInMag = Capacity;
            _flipVelocity += 0.5f;      // rocked home with a shove
            _backVelocity -= 0.12f;
        }

        if (_reloadEmpty && !_racked && _reloadT >= _spec.RackRelease)
        {
            _racked = true;
            AmmoInMag--;
            Chambered = true;
            _flipVelocity += 0.6f;
            _backVelocity += 0.15f;
        }

        if (_reloadT >= ReloadLength)
            _reloading = false;
    }

    /// <summary>The magazine's motion out of / into the well: rocked forward about its front lug (pivot), then moved
    /// along its (rocked) axis.</summary>
    private float4x4 MagMotion(float rock, float down)
    {
        var rot = float4x4.CreateRotationZ(rock);
        return RotateAbout(_spec.MagPivot, rot) * float4x4.CreateTranslation(float3.TransformNormal(_spec.MagAxis, rot) * down);
    }

    private static float4x4 Inverted(in float4x4 m)
    {
        float4x4.Invert(m, out var inv);
        return inv;
    }

    /// <summary>Where the magazine is (relative to its seat) during a reload: the old one coming out, the new one
    /// going in; null while there's none in or at the gun.</summary>
    private float4x4? MagLocal()
    {
        if (!Reloading)
            return float4x4.Identity;
        var s = _spec;
        var t = _reloadT;
        if (t < s.RockOutStart)
            return float4x4.Identity;
        if (t < s.LetGoOld)
            return MagLocalOut(t);
        if (t < s.NewMagShows)
            return null;
        if (t < s.SeatNew)
            return _magHoldInverse * SupportHandTransform();
        return float4x4.Identity;
    }

    /// <summary>The old magazine coming out: rocked off the catch, then pulled down off the front lug.</summary>
    private float4x4 MagLocalOut(float t)
    {
        var s = _spec;
        return t < s.RockOutEnd
            ? MagMotion(s.MagRock * Smooth((t - s.RockOutStart) / (s.RockOutEnd - s.RockOutStart)), 0f)
            : MagMotion(s.MagRock, s.MagPullOut * Smooth((t - s.RockOutEnd) / (s.PullOutEnd - s.RockOutEnd)));
    }

    /// <summary>The left hand's place relative to its hold on the handguard (identity = on the handguard).</summary>
    private float4x4 SupportHandTransform()
    {
        if (!Reloading)
            return float4x4.Identity;
        var s = _spec;
        var t = _reloadT;
        var onMag = s.MagHold;
        var outPose = s.MagHold * MagMotion(s.MagRock, s.MagPullOut);
        var away = outPose * float4x4.CreateTranslation(-0.05f, -0.24f, -0.07f);
        var hook = s.MagHold * MagMotion(s.MagRock, s.MagHookGap);
        var racked = s.RackHold * float4x4.CreateTranslation(-(s.HandleTravel ?? s.BoltTravel), 0, 0);
        if (t < s.LetGo)
            return float4x4.Identity;
        if (t < s.GrabMag)
            return Blend(float4x4.Identity, onMag, Smooth((t - s.LetGo) / (s.GrabMag - s.LetGo)));
        if (t < s.RockOutStart)
            return onMag;
        if (t < s.LetGoOld)
            return s.MagHold * MagLocalOut(t);
        if (t < s.HandAway)
            return Blend(outPose, away, Smooth((t - s.LetGoOld) / (s.HandAway - s.LetGoOld)));
        if (t < s.NewMagShows + 0.04f)
            return away;
        if (t < s.HookIn)
            return Blend(away, hook, Smooth((t - s.NewMagShows - 0.04f) / (s.HookIn - s.NewMagShows - 0.04f)));
        if (t < s.SeatNew)
        {
            var u = (t - s.HookIn) / (s.SeatNew - s.HookIn);
            var down = s.MagHookGap * (1f - Smooth(u * 2f));
            return s.MagHold * MagMotion(s.MagRock * (1f - Smooth(u)), down);
        }

        var back = BackOnGuardStart;
        if (_reloadEmpty && t < back)
        {
            if (t < s.ToHandle)
                return Blend(onMag, s.RackHold, Smooth((t - s.SeatNew) / (s.ToHandle - s.SeatNew)));
            if (t < s.RackBack)
                return Blend(s.RackHold, racked, Smooth((t - s.ToHandle) / (s.RackBack - s.ToHandle)));
            return racked;
        }

        var from = _reloadEmpty ? racked : onMag;
        return t < back + BackOnGuardTime ? Blend(from, float4x4.Identity, Smooth((t - back) / BackOnGuardTime)) : float4x4.Identity;
    }

    /// <summary>The bolt (carrier): 0 forward, 1 fully back (a shot cycling it, or the hand racking it).</summary>
    private float BoltBack
    {
        get
        {
            var (back, forward) = (_spec.BoltCycle.X, _spec.BoltCycle.Y);
            var cycle = _sinceShot < back ? _sinceShot / back
                : 1f - Smooth(float.Clamp((_sinceShot - back) / forward, 0f, 1f));
            return float.Max(cycle, HandleBack);
        }
    }

    /// <summary>The charging handle, racked by the hand after an empty magazine: 0 forward, 1 back. (On the AK it's
    /// part of the bolt carrier; the UMP's doesn't reciprocate, so only this moves it.)</summary>
    private float HandleBack
    {
        get
        {
            if (!Reloading || !_reloadEmpty)
                return 0f;
            var s = _spec;
            var t = _reloadT;
            return t < s.ToHandle ? 0f
                : t < s.RackBack ? Smooth((t - s.ToHandle) / (s.RackBack - s.ToHandle))
                : t < s.RackRelease ? 1f
                : 1f - Square((t - s.RackRelease) / 0.04f);
        }
    }

    private void DropMagazine(in Camera camera)
    {
        var s = _spec;
        var gunToWorld = GunToCamera() * camera.World;
        var motion = MagMotion(s.MagRock, s.MagPullOut);
        var bodyPose = float4x4.CreateRotationZ(s.MagLean) * float4x4.CreateTranslation(s.MagCenter) * motion * gunToWorld;
        var renderOffset = float4x4.CreateTranslation(-s.MagCenter) * float4x4.CreateRotationZ(-s.MagLean);
        PullInFromWall(camera.Position, ref bodyPose, 0.08f);
        var down = float3.Normalize(float3.TransformNormal(s.MagAxis, motion * gunToWorld));
        var velocity = down * 1.2f + _playerVelocity;
        var spin = new float3(Range(-2f, 2f), Range(-1.5f, 1.5f), Range(-3f, 3f));
        var parts = model.Parts.Where(p => p.Name == "Magazine").ToList();
        Debris.Magazine(parts, renderOffset, bodyPose, s.MagHalfExtents, _oldMagRounds, velocity, spin);
    }

    // ------------------------------------------------------------------------------------------------ firing

    private void Fire(in Camera camera)
    {
        var s = _spec;
        var aiming = AimBlend;
        var aimed = Discharge(camera, aiming, s.ShotPitch);

        Chambered = false;
        if (AmmoInMag > 0)
        {
            AmmoInMag--;
            Chambered = true;
        }

        // The case: out to the right, a little forward and up, tumbling.
        var gunToWorld = GunToCamera() * camera.World;
        var caseVelocity = new float3(Range(s.CaseVelocityMin.X, s.CaseVelocityMax.X), Range(s.CaseVelocityMin.Y, s.CaseVelocityMax.Y),
            Range(s.CaseVelocityMin.Z, s.CaseVelocityMax.Z));
        EjectCasing(camera, gunToWorld, s.EjectionPort, caseVelocity, new float3(0.3f, 1f, 0.2f), new float2(20f, 35f), s.Cartridge);

        var eye = camera.Position;
        var forward = aimed.Forward;
        var muzzle = float3.Transform(s.MuzzlePoint, gunToWorld);
        var direction = AimShot(eye, forward, muzzle, s.MaxAimDistance, 2f, s.ZeroDistance, s.ProjectileSpeed, out var spawn, out var aimDistance, out var aimedAt, s.Cartridge);

        Projectiles.Spawn(spawn, direction * s.ProjectileSpeed, round: s.Cartridge);
        Effects.MuzzleSmoke(muzzle, forward, _playerVelocity);
        LogShot(s.ShotTag + " ", aimedAt, aimDistance, spawn == eye, aimed, camera, aiming);
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
                _oldMagDropped = t >= _spec.LetGoOld;
                _oldMagRounds = _reloadEmpty ? 0 : 12;
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
            var outStart = ReloadLength - 0.35f;
            return _reloadT < 0.28f ? Smooth(_reloadT / 0.28f)
                : _reloadT < outStart ? 1f
                : 1f - Smooth(float.Clamp((_reloadT - outStart) / 0.35f, 0f, 1f));
        }
    }

    // ------------------------------------------------------------------------------------------------ drawing

    public override void Draw(Renderer renderer, in Camera camera)
    {
        if (Scoped)
            return;      // looking through the scope: the HUD draws what you see of it
        var s = _spec;
        var gunToWorld = GunToCamera() * camera.World;
        renderer.SetViewModel(true);
        float4x4.Invert(gunToWorld, out var worldToGun);
        renderer.SetTextureSpace(worldToGun);

        var boltToWorld = float4x4.CreateTranslation(-s.BoltTravel * BoltBack, 0, 0) * gunToWorld;
        var handleToWorld = s.HandleTravel is { } travel ? float4x4.CreateTranslation(-travel * HandleBack, 0, 0) * gunToWorld : gunToWorld;
        var triggerToWorld = RotateAbout(s.TriggerPivot, float4x4.CreateRotationZ(-0.25f * _pull)) * gunToWorld;
        var fingerToWorld = RotateAbout(s.FingerKnuckle, float4x4.CreateRotationY(0.16f * _pull) * float4x4.CreateRotationZ(-0.04f * _pull)) * gunToWorld;
        var support = s.LeftHandShift is { } shift ? shift * SupportHandTransform() : SupportHandTransform();
        var handsPose = new HandsPose(gunToWorld, fingerToWorld, support, support * gunToWorld, ArmsToCamera() * camera.World);
        var mag = MagLocal();

        foreach (var part in model.Parts)
        {
            if (_hands is null && DrawHandPart(renderer, part, handsPose))
                continue;
            if (Optic is not null && part.Name == "Optic")
                continue;   // (another optic is fitted in place of its own)
            switch (part.Name)
            {
                case "Magazine":
                    if (mag is { } m)
                        renderer.DrawPart(part, m * gunToWorld, SleeveTint);
                    break;
                case "BoltCarrier":
                    renderer.DrawPart(part, boltToWorld, SleeveTint);
                    break;
                case "ChargingHandle" when s.HandleTravel is not null:
                    renderer.DrawPart(part, handleToWorld, SleeveTint);
                    break;
                case "Trigger":
                    renderer.DrawPart(part, triggerToWorld, SleeveTint);
                    break;
                default:
                    renderer.DrawPart(part, gunToWorld, SleeveTint);
                    break;
            }
        }

        DrawOptic(renderer, gunToWorld, SleeveTint);

        if (_hands is not null)
            foreach (var part in _hands.Parts)
                DrawHandPart(renderer, part, handsPose);

        renderer.SetTextureSpace(float4x4.Identity);
        renderer.SetViewModel(false);

        if (_muzzleFlash > 0)
            DrawMuzzleFlash(renderer, float3.Transform(s.MuzzlePoint + new float3(0.02f, 0, 0), gunToWorld), s.FlashSize, s.FlashCoreSize);
    }
}

/// <summary>
/// What makes a <see cref="Rifle"/> the gun it is: its name, ballistics, sounds, the points on its model, its magazine
/// and how it comes out and goes in, the reload's timeline, and how it sits and kicks (<see cref="GunFeel"/>).
/// <para>
/// Model space (metres): +X out of the muzzle, +Y up, -Z the gun's left side; x = 0 at the trigger, y = 0 the AK's
/// bore. The grip, trigger and guard are the AK's on every one, so the hands hold them the same way.
/// </para>
/// </summary>
public sealed partial record RifleSpec
{
    public required string Name { get; init; }
    public required string ShotTag { get; init; }          // in the [SHOT] log lines
    public int Capacity { get; init; } = 30;
    public float FireInterval { get; init; } = 0.1f;       // fastest you can fire by clicking (semi-automatic)
    public float AutoFireInterval { get; init; } = 0.1f;   // fully automatic: 600 rounds a minute
    public required Cartridge Cartridge { get; init; }     // what it fires
    public required float ProjectileSpeed { get; init; }   // m/s
    public required float MaxAimDistance { get; init; }
    public required float ZeroDistance { get; init; }      // m: where the shot crosses the line of sight (and falls below it beyond)
    public required int Seed { get; init; }

    public required GunFeel Feel { get; init; }
    public required Func<ShotSpread> NewSpread { get; init; }

    // Sounds: the shot's pitch range; the magazine out / in, and the charging handle back / released.
    public required float2 ShotPitch { get; init; }
    public required float MagOutPitch { get; init; }
    public required float MagInPitch { get; init; }
    public required float RackBackPitch { get; init; }
    public required float RackReleasePitch { get; init; }

    // Points on the model.
    public required float3 MuzzlePoint { get; init; }
    public required float3 EjectionPort { get; init; }
    public float3 TriggerPivot { get; init; } = new(0.001f, -0.036f, 0f);
    public float3 FingerKnuckle { get; init; } = new(-0.05045f, -0.05628f, 0.0231f);
    public required float BoltTravel { get; init; }        // the bolt (carrier), each shot
    public required float2 BoltCycle { get; init; }        // its times back, then forward again (seconds)
    public float? HandleTravel { get; init; }              // a separate charging handle (moved only when racked); null: on the carrier
    public float4x4? LeftHandShift { get; init; }          // borrowed hands: the left one moved onto this gun's handguard
    public float? ScopeZoom { get; init; }                 // a scope's magnification (aimed, the view goes through it); null: open sights
    public float[]? ScopeSteps { get; init; }              // the powers the mouse wheel turns the scope through, lowest first (ScopeZoom among them); null: fixed
    public ScopeKind ScopeKind { get; init; }              // how the scope is seen through: a tube, or a prism sight (an ACOG)
    public required float3 CaseVelocityMin { get; init; }  // the case's speed out of the port (gun frame), between these
    public required float3 CaseVelocityMax { get; init; }
    public required float FlashSize { get; init; }
    public required float FlashCoreSize { get; init; }

    // The magazine: it rocks about MagPivot (its front lug or edge) and comes out along MagAxis; MagCenter, MagLean and
    // MagHalfExtents are the box its dropped body is.
    public required float3 MagPivot { get; init; }
    public required float3 MagAxis { get; init; }          // down out of the well
    public required float3 MagCenter { get; init; }
    public required float MagLean { get; init; }
    public required float3 MagHalfExtents { get; init; }
    public required float MagRock { get; init; }           // how far it's rocked forward to come off (and go on)
    public required float MagPullOut { get; init; }        // and pulled down clear
    public required float MagHookGap { get; init; }        // the new one started into the well, before it's seated
    public required float4x4 MagHold { get; init; }        // the left hand on the (seated) magazine, from its hold on the handguard
    public required float4x4 RackHold { get; init; }       // ...and at the charging handle

    // The reload's timeline (seconds from pressing R).
    public required float LetGo { get; init; }             // left hand leaves the handguard...
    public required float GrabMag { get; init; }           // ...and has the magazine
    public required float RockOutStart { get; init; }      // release pressed...
    public required float RockOutEnd { get; init; }        // ...rocked / tipped off the catch
    public required float PullOutEnd { get; init; }        // pulled out
    public required float LetGoOld { get; init; }          // dropped (a body in the world from here)
    public required float HandAway { get; init; }          // down out of sight, for a fresh one
    public required float NewMagShows { get; init; }       // (in the hand from here)
    public required float HookIn { get; init; }            // started into the well
    public required float SeatNew { get; init; }           // home: click
    public required float ToHandle { get; init; }          // empty: the left hand at the charging handle...
    public required float RackBack { get; init; }          // ...pulled back...
    public required float RackRelease { get; init; }       // ...and let go
    public required float ReloadLength { get; init; }
    public required float EmptyReloadLength { get; init; }

    /// <summary>
    /// The AK-47 (7.62 x 39 mm, 30-round magazine, fully automatic at 600 rounds a minute). The left hand cups the lower
    /// handguard from below, thumb along its left side. Its curved magazine is reloaded the AK way: rocked forward off
    /// its catch, and a fresh one hooked in by its front lug and rocked back until it clicks; after an empty magazine
    /// the hand reaches under to the charging handle on the right (on the bolt carrier) and pulls it back.
    /// The view model (the rifle, both hands and sleeves) is Assets/Models/ak47.glb, built by
    /// Tools/blender/build_ak47.py and build_ak47_hands.py (points: Assets/Models/ak47_points.json).
    /// </summary>
    public static readonly RifleSpec Ak47 = new()
    {
        Name = "AK-47",
        ShotTag = "AK",
        Cartridge = Cartridge.Soviet762,
        ProjectileSpeed = 715f,    // 7.62 x 39 mm from a 415 mm barrel
        MaxAimDistance = 400f,
        ZeroDistance = 100f,
        Seed = 4747,
        Feel = new GunFeel
        {
            SightPoint = new(0.1612f, 0.044f, 0f),      // rear sight notch: the line of sight
            GripPivot = new(-0.060f, -0.082f, 0f),
            HipPosition = new(0.092f, -0.068f, -0.315f),
            AimTime = 0.22f,
            DrawTime = 0.55f,
            // Recoil: the butt is in the shoulder, so the gun comes straight back and its muzzle rises about the shoulder.
            RecoilPivot = new(-0.30f, -0.06f, 0f),
            FlipKick = 3.0f, FlipKickAiming = 2.1f,
            FlipStiffness = 420f, FlipDamping = 30f,
            BackKick = 0.55f,
            ViewKickPitch = 0.12f,
            // The walk of the aim: smaller steps than the pistol's, but 10 a second when held down, climbing and
            // drifting (mostly right, the way an AK pulls for a right-handed shooter).
            Walk = new AimWalk(RiseStepHip: 0.0055f, RiseStepAiming: 0.0042f, RecoverDelay: 0.13f, RecoverRate: 3.2f, RiseFollow: 30f,
                FreshHeading: 0.6f, TurnChance: 0.15, TurnRange: 1.6f, Drift: new(-0.35f, 0.45f), HeadingKeep: 0.88f, Lift: 0.2f),
            FlashPoint = new(0.538f, 0f, 0f),
            FlashLightTime = 0.045f, FlashLightStrength = 0.08f,
        },
        // Spread: about 3.5 MOA aimed (a 9 cm group at 100 m, like the reference target), loose from the hip; opens up
        // over the first ten or so rounds of a burst and closes again a second or so after you let go.
        NewSpread = () => new ShotSpread(hip: 0.010f, aimed: 0.0005f, hipBloom: 0.030f, aimedBloom: 0.013f,
            perShot: 0.11f, coolDelay: 0.15f, coolRate: 1.6f),
        ShotPitch = new(0.80f, 0.86f),
        MagOutPitch = 0.82f, MagInPitch = 0.80f, RackBackPitch = 0.70f, RackReleasePitch = 0.68f,
        MuzzlePoint = new(0.538f, 0f, 0f),
        EjectionPort = new(0.079f, 0.012f, 0.016f),
        BoltTravel = 0.105f,
        BoltCycle = new(0.028f, 0.05f),
        CaseVelocityMin = new(0.2f, 1.0f, 2.6f), CaseVelocityMax = new(0.7f, 1.6f, 3.4f),
        FlashSize = 0.055f, FlashCoreSize = 0.024f,
        MagPivot = new(0.1225f, -0.038f, 0f),
        MagAxis = float3.Normalize(new float3(0.064f, -0.998f, 0f)),
        MagCenter = new(0.1148f, -0.095f, 0f),
        MagLean = 0.450f,
        MagHalfExtents = new(0.032f, 0.09f, 0.0135f),
        MagRock = 0.38f,
        MagPullOut = 0.075f,       // pulled down off the front lug
        MagHookGap = 0.012f,       // the new one's lug just in the well, before it's rocked home
        // The left hand's hold on the magazine: its knuckle row along the magazine (index at the top), its palm on the
        // left side, its fingers round the front and its thumb behind, on the release; the forearm comes in from below
        // and to the left. At the handle: under the receiver, its fingers up round the charging handle on the right.
        MagHold = Frame(new float3(0.262f, -0.006f, 0f), new float3(1, 0, 0), new float3(0, -1, 0),
            new float3(0.1107f, -0.0863f, 0f) + new float3(0.913f, 0.407f, 0f) * 0.012f,
            float3.Normalize(new float3(-0.407f, 0.913f, 0f)), new float3(0, 0, -1)),
        RackHold = float4x4.CreateTranslation(-0.118f, 0.012f, 0.012f),
        LetGo = 0.06f, GrabMag = 0.36f,
        RockOutStart = 0.44f, RockOutEnd = 0.58f, PullOutEnd = 0.72f, LetGoOld = 0.76f,
        HandAway = 1.00f, NewMagShows = 1.10f, HookIn = 1.42f, SeatNew = 1.62f,
        ToHandle = 1.92f, RackBack = 2.06f, RackRelease = 2.12f,
        ReloadLength = 2.20f, EmptyReloadLength = 2.80f,
    };

    /// <summary>
    /// The UMP40 submachine gun (.40 S&amp;W, 30-round straight magazine, fully automatic at 745 rounds a minute), held
    /// like the AK-47. The bolt slams back and forward in the ejection port on the right (the charging handle on the
    /// left doesn't move with it); a lighter round than the AK's, so less kick and climb. Its straight magazine comes
    /// out with the paddle pressed and pulled straight down, and a fresh one is started into the well and pushed home;
    /// after an empty magazine the hand goes up to the charging handle on the left and pulls it back.
    /// The view model is Assets/Models/ump.glb (Tools/blender/build_ump.py, points: ump_points.json). Its grip, trigger
    /// and guard are the AK's and its handguard the AK's lower handguard's section, so it borrows the AK's hands and
    /// sleeves: the right hand as it is, the left moved onto the handguard by LeftHandShift.
    /// </summary>
    public static readonly RifleSpec Ump40 = new()
    {
        Name = "UMP40",
        ShotTag = "UMP",
        AutoFireInterval = 0.0805f, // 745 rounds a minute
        Cartridge = Cartridge.SW40,
        ProjectileSpeed = 360f,    // .40 S&W from a 200 mm barrel
        MaxAimDistance = 250f,
        ZeroDistance = 50f,
        Seed = 4040,
        Feel = new GunFeel
        {
            // SightPoint is on the line through the rear aperture and the front post (the UMP's own bore is 18 mm
            // above the AK's), where the AK's rear sight is: the gun sits in the hands as the AK does. The sight line
            // is 25 mm higher above the grip than the AK's, so the hip position is that much higher to keep the hands
            // where the AK's are.
            SightPoint = new(0.1612f, 0.069f, 0f),
            GripPivot = new(-0.060f, -0.082f, 0f),
            HipPosition = new(0.092f, -0.043f, -0.315f),
            AimTime = 0.18f,
            DrawTime = 0.45f,
            // Recoil: a pistol round in a heavy gun with a straight-line stock: a light shove, little climb.
            RecoilPivot = new(-0.36f, -0.02f, 0f),
            FlipKick = 1.9f, FlipKickAiming = 1.3f,
            FlipStiffness = 460f, FlipDamping = 32f,
            BackKick = 0.38f,
            ViewKickPitch = 0.07f,
            // The walk of the aim: smaller steps than the AK's.
            Walk = new AimWalk(RiseStepHip: 0.0038f, RiseStepAiming: 0.0028f, RecoverDelay: 0.12f, RecoverRate: 3.6f, RiseFollow: 30f,
                FreshHeading: 0.6f, TurnChance: 0.15, TurnRange: 1.6f, Drift: new(-0.35f, 0.45f), HeadingKeep: 0.88f, Lift: 0.2f),
            // Steadiness: short and light, it settles quickly.
            UnsteadySpreadHip = 0.020f, UnsteadySpreadAimed = 0.007f,
            SettleRate = 3.4f,
            FlashPoint = new(0.3173f, 0.0183f, 0f),
            FlashLightTime = 0.04f, FlashLightStrength = 0.06f,
        },
        // Spread: about 5 MOA aimed, looser than the AK's from the hip; opens up over a burst and closes again soon after.
        NewSpread = () => new ShotSpread(hip: 0.012f, aimed: 0.0008f, hipBloom: 0.026f, aimedBloom: 0.010f,
            perShot: 0.10f, coolDelay: 0.12f, coolRate: 1.8f),
        ShotPitch = new(0.90f, 0.96f),
        MagOutPitch = 0.92f, MagInPitch = 0.90f, RackBackPitch = 0.78f, RackReleasePitch = 0.76f,
        MuzzlePoint = new(0.3173f, 0.0183f, 0f),
        EjectionPort = new(0.0658f, 0.0202f, 0.016f),
        BoltTravel = 0.060f,
        BoltCycle = new(0.025f, 0.045f),
        HandleTravel = 0.072f,
        LeftHandShift = float4x4.CreateTranslation(-0.040f, -0.014f, 0f),
        CaseVelocityMin = new(0.1f, 0.9f, 2.2f), CaseVelocityMax = new(0.5f, 1.4f, 3.0f),
        FlashSize = 0.042f, FlashCoreSize = 0.02f,
        MagPivot = new(0.1495f, -0.040f, 0f),
        MagAxis = float3.Normalize(new float3(0.0676f, -0.9977f, 0f)),
        MagCenter = new(0.130f, -0.1165f, 0f),
        MagLean = 0.0677f,
        MagHalfExtents = new(0.0255f, 0.0887f, 0.013f),
        MagRock = 0.04f,           // a slight tip forward as it comes off the catch
        MagPullOut = 0.12f,        // and pulled straight down clear of the well
        MagHookGap = 0.035f,
        // The left hand's hold on the magazine, from its hold on the handguard (after LeftHandShift): as the AK's, its
        // knuckle row along the magazine, its palm on the left side, its fingers round the front and its thumb behind,
        // on the paddle. At the charging handle it's held as the magazine is (knuckle row upright, palm facing the
        // receiver's left side, fingers round the front of the knob), standing off the side so they clear it.
        MagHold = Frame(new float3(0.222f, -0.020f, 0f), new float3(1, 0, 0), new float3(0, -1, 0),
            new float3(0.1279f, -0.086f, 0f) + new float3(0.9977f, 0.0676f, 0f) * 0.012f,
            float3.Normalize(new float3(-0.0676f, 0.9977f, 0f)), new float3(0, 0, -1)),
        RackHold = Frame(new float3(0.222f, -0.020f, 0f), new float3(1, 0, 0), new float3(0, -1, 0),
            new float3(0.1187f, 0.0145f, -0.034f), new float3(0, 1, 0), new float3(0, 0, -1)),
        LetGo = 0.06f, GrabMag = 0.34f,
        RockOutStart = 0.42f, RockOutEnd = 0.50f, PullOutEnd = 0.66f, LetGoOld = 0.70f,
        HandAway = 0.94f, NewMagShows = 1.04f, HookIn = 1.34f, SeatNew = 1.50f,
        ToHandle = 1.80f, RackBack = 1.94f, RackRelease = 2.00f,
        ReloadLength = 2.05f, EmptyReloadLength = 2.65f,
    };

    /// <summary>
    /// The AR-15 carbine (5.56 x 45 mm, 30-round magazine; the selector gives semi-automatic or fully automatic at 750
    /// rounds a minute) with a scope that the mouse wheel turns from 2x to 16x, set at 4x to start with: aimed, the view
    /// goes through it (the HUD draws its tube and duplex reticle). A light, fast round through a gun whose stock is in line with the bore: a sharp, short kick
    /// and little climb. Its magazine drops straight down out of the well and a fresh one is pushed home; after an
    /// empty magazine the hand goes up to the T-handle at the back of the receiver and pulls it back.
    /// The view model is Assets/Models/ar15.glb (Tools/blender/build_ar15.py, points: ar15_points.json). Its grip,
    /// trigger and guard are the AK's and the handguard's lower half under the left hand the AK's section, so it
    /// borrows the AK's hands and sleeves: the right hand as it is, the left moved onto the handguard by LeftHandShift.
    /// </summary>
    public static readonly RifleSpec Ar15 = new()
    {
        Name = "AR-15",
        ShotTag = "AR",
        AutoFireInterval = 0.08f,
        Cartridge = Cartridge.Nato556,
        ProjectileSpeed = 900f,    // 5.56 x 45 mm from a 406 mm barrel
        MaxAimDistance = 500f,
        ZeroDistance = 100f,
        Seed = 1515,
        ScopeZoom = 4f,
        ScopeSteps = [2f, 4f, 8f, 16f],
        Feel = new GunFeel
        {
            // SightPoint is on the scope's axis (49 mm above the AK's sight line), where the AK's rear sight is. At the
            // hip the gun is held lower and further out than the AK (by about the scope's height), so the scope's
            // eyepiece stays out of the middle of the view.
            SightPoint = new(0.1612f, 0.0931f, 0f),
            GripPivot = new(-0.060f, -0.082f, 0f),
            HipPosition = new(0.105f, -0.080f, -0.345f),
            AimTime = 0.24f,
            DrawTime = 0.55f,
            // Recoil: a small, fast round and a straight-line stock: the gun comes straight back, the muzzle hardly rises.
            RecoilPivot = new(-0.34f, 0.02f, 0f),
            FlipKick = 2.2f, FlipKickAiming = 1.5f,
            FlipStiffness = 460f, FlipDamping = 32f,
            BackKick = 0.45f,
            ViewKickPitch = 0.08f,
            // The walk of the aim: smaller steps than the AK's (through a 4x scope it still shows).
            Walk = new AimWalk(RiseStepHip: 0.0042f, RiseStepAiming: 0.0026f, RecoverDelay: 0.12f, RecoverRate: 3.4f, RiseFollow: 30f,
                FreshHeading: 0.6f, TurnChance: 0.15, TurnRange: 1.6f, Drift: new(-0.35f, 0.45f), HeadingKeep: 0.88f, Lift: 0.2f),
            FlashPoint = new(0.551f, 0.020f, 0f),
            FlashLightTime = 0.045f, FlashLightStrength = 0.08f,
        },
        // Spread: about 2 MOA aimed (a 6 cm group at 100 m), loose from the hip; opens up over a burst.
        NewSpread = () => new ShotSpread(hip: 0.010f, aimed: 0.0003f, hipBloom: 0.028f, aimedBloom: 0.010f,
            perShot: 0.10f, coolDelay: 0.14f, coolRate: 1.7f),
        ShotPitch = new(0.88f, 0.94f),
        MagOutPitch = 0.88f, MagInPitch = 0.86f, RackBackPitch = 0.74f, RackReleasePitch = 0.72f,
        MuzzlePoint = new(0.551f, 0.020f, 0f),
        EjectionPort = new(0.047f, 0.020f, 0.0145f),
        BoltTravel = 0.080f,
        BoltCycle = new(0.022f, 0.040f),
        HandleTravel = 0.076f,
        LeftHandShift = float4x4.CreateTranslation(0f, 0.020f, 0f),
        CaseVelocityMin = new(0.2f, 1.0f, 2.6f), CaseVelocityMax = new(0.7f, 1.6f, 3.4f),
        FlashSize = 0.05f, FlashCoreSize = 0.022f,
        MagPivot = new(0.116f, -0.064f, 0f),
        MagAxis = float3.Normalize(new float3(0.0294f, -0.9996f, 0f)),
        MagCenter = new(0.093f, -0.090f, 0f),
        MagLean = 0.1019f,
        MagHalfExtents = new(0.032f, 0.0885f, 0.0115f),
        MagRock = 0.02f,           // hardly tips: it drops straight out
        MagPullOut = 0.13f,        // and down clear of the well
        MagHookGap = 0.035f,
        // The left hand's hold on the magazine, from its hold on the handguard (after LeftHandShift): as the UMP's,
        // its knuckle row along the magazine below the well, its palm on the left side, its fingers round the front.
        // At the charging handle: knuckle row upright, palm to the left of the T-handle, fingers round its left wing.
        MagHold = Frame(new float3(0.262f, 0.014f, 0f), new float3(1, 0, 0), new float3(0, -1, 0),
            new float3(0.088f, -0.100f, 0f) + new float3(0.9996f, 0.0294f, 0f) * 0.012f,
            float3.Normalize(new float3(-0.0294f, 0.9996f, 0f)), new float3(0, 0, -1)),
        RackHold = Frame(new float3(0.262f, 0.014f, 0f), new float3(1, 0, 0), new float3(0, -1, 0),
            new float3(-0.087f, 0.038f, -0.031f), new float3(0, 1, 0), new float3(0, 0, -1)),
        LetGo = 0.06f, GrabMag = 0.34f,
        RockOutStart = 0.42f, RockOutEnd = 0.48f, PullOutEnd = 0.64f, LetGoOld = 0.68f,
        HandAway = 0.94f, NewMagShows = 1.04f, HookIn = 1.34f, SeatNew = 1.50f,
        ToHandle = 1.84f, RackBack = 1.98f, RackRelease = 2.04f,
        ReloadLength = 2.05f, EmptyReloadLength = 2.70f,
    };

    /// <summary>
    /// The Heckler &amp; Koch G3 battle rifle (7.62 x 51 mm, 20-round steel magazine; the selector gives semi-automatic
    /// or fully automatic at 550 rounds a minute) with a 4x ACOG on a claw mount over its drum sight: aimed, the magnified
    /// picture fills a circle in the middle of the view, ringed by the black eyepiece, with the world seen as it is round it
    /// (the HUD draws the ring and the red chevron). A full-power round: a heavy shove into the shoulder and a big climb,
    /// and the roller-delayed bolt flings its cases well clear. Its straight magazine is pulled straight down with the
    /// paddle pressed and a fresh one pushed home; after an empty magazine the hand goes forward to the cocking handle on
    /// the left of the tube over the handguard and pulls it back.
    /// The view model is Assets/Models/g3.glb (Tools/blender/build_g3.py, points: g3_points.json). Its grip, trigger and
    /// guard are the AK's and the handguard's lower half under the left hand the AK's section, so it borrows the AK's hands
    /// and sleeves: the right hand as it is, the left moved onto the handguard by LeftHandShift.
    /// </summary>
    public static readonly RifleSpec G3 = new()
    {
        Name = "G3",
        ShotTag = "G3",
        Capacity = 20,
        AutoFireInterval = 0.109f,
        Cartridge = Cartridge.Nato762,
        ProjectileSpeed = 800f,    // 7.62 x 51 mm from a 450 mm barrel
        MaxAimDistance = 500f,
        ZeroDistance = 100f,
        Seed = 3333,
        ScopeZoom = 4f,
        ScopeSteps = [2f, 4f, 8f, 16f],
        ScopeKind = ScopeKind.Acog,
        Feel = new GunFeel
        {
            // SightPoint is on the ACOG's axis (70 mm above the AK's sight line: the claw mount is tall), where the AK's
            // rear sight is. At the hip it is held that much lower than the AK (so the hands are where the AK's are), and a
            // little further out, so the scope doesn't crowd the corner of the view.
            SightPoint = new(0.1612f, 0.114f, 0f),
            GripPivot = new(-0.060f, -0.082f, 0f),
            HipPosition = new(0.100f, -0.110f, -0.335f),
            // Aimed, the eye is 60 mm behind the eyepiece (its eye relief; the eyepiece's end is at x = -0.080).
            AimPosition = new(0f, 0f, -0.301f),
            AimTime = 0.26f,
            DrawTime = 0.60f,
            // Recoil: a full-power round: a hard shove back and the muzzle rising about the shoulder.
            RecoilPivot = new(-0.39f, -0.04f, 0f),
            FlipKick = 3.8f, FlipKickAiming = 2.7f,
            FlipStiffness = 400f, FlipDamping = 28f,
            BackKick = 0.75f,
            ViewKickPitch = 0.16f,
            // The walk of the aim: bigger steps than the AK's, a little slower to settle.
            Walk = new AimWalk(RiseStepHip: 0.0075f, RiseStepAiming: 0.0058f, RecoverDelay: 0.15f, RecoverRate: 2.8f, RiseFollow: 30f,
                FreshHeading: 0.6f, TurnChance: 0.15, TurnRange: 1.6f, Drift: new(-0.35f, 0.45f), HeadingKeep: 0.88f, Lift: 0.2f),
            // Steadiness: a long, heavy rifle: it wanders more and takes longer to settle.
            AimSway = 0.0014f, AimSwayUnsteady = 0.0045f,
            UnsteadySpreadHip = 0.028f, UnsteadySpreadAimed = 0.009f,
            SettleRate = 2.4f,
            FlashPoint = new(0.6324f, 0.010f, 0f),
            FlashLightTime = 0.05f, FlashLightStrength = 0.09f,
        },
        // Spread: about 2.5 MOA aimed; single aimed shots are accurate, but fully automatic it soon sprays.
        NewSpread = () => new ShotSpread(hip: 0.011f, aimed: 0.0004f, hipBloom: 0.034f, aimedBloom: 0.016f,
            perShot: 0.13f, coolDelay: 0.16f, coolRate: 1.5f),
        ShotPitch = new(0.72f, 0.78f),
        MagOutPitch = 0.80f, MagInPitch = 0.78f, RackBackPitch = 0.66f, RackReleasePitch = 0.64f,
        MuzzlePoint = new(0.6324f, 0.010f, 0f),
        EjectionPort = new(0.097f, 0.024f, 0.016f),
        BoltTravel = 0.080f,
        BoltCycle = new(0.030f, 0.055f),
        HandleTravel = 0.095f,
        LeftHandShift = float4x4.CreateTranslation(0.050f, 0.012f, 0f),
        CaseVelocityMin = new(0.3f, 1.4f, 3.4f), CaseVelocityMax = new(0.9f, 2.2f, 4.4f),
        FlashSize = 0.065f, FlashCoreSize = 0.028f,
        MagPivot = new(0.147f, -0.046f, 0f),
        MagAxis = float3.Normalize(new float3(0.0238f, -0.9997f, 0f)),
        MagCenter = new(0.115f, -0.077f, 0f),
        MagLean = 0.0411f,
        MagHalfExtents = new(0.035f, 0.0731f, 0.0135f),
        MagRock = 0.03f,           // a slight tip forward as it comes off the catch
        MagPullOut = 0.12f,        // and pulled straight down clear of the well
        MagHookGap = 0.035f,
        // The left hand's hold on the magazine, from its hold on the handguard (after LeftHandShift): as the UMP's, its
        // knuckle row along the magazine below the well, its palm on the left side, its fingers round the front. At the
        // cocking handle: knuckle row upright, palm to the left of the knob, fingers round it.
        MagHold = Frame(new float3(0.312f, 0.006f, 0f), new float3(1, 0, 0), new float3(0, -1, 0),
            new float3(0.1147f, -0.082f, 0f) + new float3(0.9997f, 0.0238f, 0f) * 0.012f,
            float3.Normalize(new float3(-0.0238f, 0.9997f, 0f)), new float3(0, 0, -1)),
        RackHold = Frame(new float3(0.312f, 0.006f, 0f), new float3(1, 0, 0), new float3(0, -1, 0),
            new float3(0.4066f, 0.037f, -0.0405f), new float3(0, 1, 0), new float3(0, 0, -1)),
        LetGo = 0.06f, GrabMag = 0.36f,
        RockOutStart = 0.44f, RockOutEnd = 0.52f, PullOutEnd = 0.68f, LetGoOld = 0.72f,
        HandAway = 0.98f, NewMagShows = 1.08f, HookIn = 1.38f, SeatNew = 1.56f,
        ToHandle = 1.86f, RackBack = 2.00f, RackRelease = 2.06f,
        ReloadLength = 2.15f, EmptyReloadLength = 2.75f,
    };

    /// <summary>The rigid transform taking frame A (origin, axes a1, a2) onto frame B.</summary>
    private static float4x4 Frame(float3 oa, float3 a1, float3 a2, float3 ob, float3 b1, float3 b2)
    {
        var a3 = float3.Cross(a1, a2);
        var b3 = float3.Cross(b1, b2);
        // Row-vector matrices: rows are the images of the basis vectors.
        var ma = new float4x4(a1.X, a1.Y, a1.Z, 0, a2.X, a2.Y, a2.Z, 0, a3.X, a3.Y, a3.Z, 0, 0, 0, 0, 1);
        var mb = new float4x4(b1.X, b1.Y, b1.Z, 0, b2.X, b2.Y, b2.Z, 0, b3.X, b3.Y, b3.Z, 0, 0, 0, 0, 1);
        var rot = float4x4.Transpose(ma) * mb;   // world -> A's basis -> B's basis
        return float4x4.CreateTranslation(-oa) * rot * float4x4.CreateTranslation(ob);
    }
}
