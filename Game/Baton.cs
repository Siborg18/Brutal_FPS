using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The prison officer's baton, all you carry in the prison intro: a plain straight stick with a ribbed rubber grip, built
/// here as a lathe profile, held in the right hand of the pistol's view model (Assets/Models/pistol.glb: its hand, trigger
/// finger and sleeve, the finger laid along the shaft). Left click swings it: back, then down and across, then back up.
/// Near the bottom of the swing it lands on whatever is in reach (see <see cref="ProjectileSystem.Melee"/>): people flinch and lose health, without bleeding.
/// </summary>
public sealed class Baton(
    Level level,
    PortalSystem portals,
    ProjectileSystem projectiles,
    ImpactEffects effects,
    DebrisSystem debris,
    PhysicsWorld? physics,
    Model? hands)
    : GunBase(level, portals, projectiles, effects, debris, physics, BatonFeel,
        new ShotSpread(hip: 0f, aimed: 0f, hipBloom: 0f, aimedBloom: 0f, perShot: 0f, coolDelay: 1f, coolRate: 1f),
        capacity: 0, seed: 4242)
{
    public override string Name => "Baton";
    public override string FireMode => "SWING";
    public override bool Reloading => false;
    public override float ReloadProgress => 0f;
    protected override float ReloadPose => 0f;

    // Where the hand holds it (the pistol's grip, in its model space), and the grip's axis, pointing up out of the fist.
    private static readonly float3 Grip = new(-0.0605f, -0.053f, 0f);
    protected override float3? BoreMuzzle => null;   // nothing to shoot
    private static readonly float3 Axis = float3.Normalize(new float3(0.2932f, 0.956f, 0f));

    // The pistol's right arm (see Weapon.Arms), and the trigger finger's knuckle, for curling it round the grip.
    private static readonly ArmRig Arms = new(
        new float3(-0.139f, -0.083f, 0.015f), float3.Normalize(new float3(-0.780f, -0.550f, 0.300f)),
        new float3(-0.128f, -0.108f, -0.040f), float3.Normalize(new float3(-0.699f, -0.619f, -0.359f)));
    private static readonly float3 FingerKnuckle = new(-0.0612f, -0.0363f, 0.0236f);
    private const float FingerAlong = 1.15f;   // (the index finger lies up along the shaft: it's rigid, so it can't wrap round)

    /// <summary>The swing turns the arm about the elbow, and snaps the wrist on top of that.</summary>
    private static readonly float3 Elbow = Arms.RightWrist + Arms.RightArm * 0.26f;

    /// <summary>How the baton is carried: leaning forward out of the fist.</summary>
    private const float CarryLean = -0.42f;

    // The swing (seconds from the click): drawn back, the strike, the recovery.
    private const float WindUp = 0.11f, Strike = 0.25f, SwingLength = 0.62f;
    private const float Contact = 0.21f;   // when the blow lands
    private const float Reach = 1.7f;      // metres from the eye

    private static readonly GunFeel BatonFeel = new()
    {
        SightPoint = Grip,
        GripPivot = Grip,
        HipPosition = new(0.17f, -0.215f, -0.40f),
        HipYaw = 0.12f,
        AimPosition = new(0.17f, -0.215f, -0.40f),
        AimTime = 0.2f,
        DrawTime = 0.5f,

        // No recoil, no walk of the aim, no flash: a stick doesn't kick.
        RecoilPivot = Arms.RightWrist,
        FlipKick = 0f, FlipKickAiming = 0f, FlipStiffness = 200f, FlipDamping = 28f,
        BackKick = 0f, ViewKickPitch = 0f,
        AimSway = 0f, AimSwayUnsteady = 0f, UnsteadySpreadHip = 0f, UnsteadySpreadAimed = 0f,
        FlashPoint = Grip, FlashLightTime = 0.001f, FlashLightStrength = 0f,

        SwayGain = 0.0016f, SwayLimit = new(0.07f, 0.06f),
        SprintShift = new(0f, -0.05f, 0.02f), SprintPitch = 0.3f, SprintRoll = 0f, SprintYaw = 0f,
        DrawPitch = 0.7f, DrawShift = new(0, -0.32f, 0.08f),
    };

    private readonly Dictionary<string, ModelPart> _sleeves = SkinSleeves(hands, Arms, material =>
        material.Material == Material.Cloth && TextureLibrary.Current?.TryGet(Material.Fleece, out _) == true
            ? material with { Material = Material.Fleece }
            : material);
    protected override IReadOnlyDictionary<string, ModelPart> SkinnedSleeves => _sleeves;

    private readonly Mesh _grip = Meshes().Grip;
    private readonly Mesh _shaft = Meshes().Shaft;

    private static Mesh? _sharedGrip, _sharedShaft;

    /// <summary>The baton's two meshes (its axis along +Y, 0 at the middle of the fist), also for the officers' hands. Needs the GL context.</summary>
    public static (Mesh Grip, Mesh Shaft) Meshes() =>
        (_sharedGrip ??= Lathe(GripProfile()),
         _sharedShaft ??= Lathe([(0.06f, 0f), (0.06f, 0.0155f), (0.09f, 0.0150f), (0.50f, 0.0120f), (0.517f, 0.0108f), (0.527f, 0.0070f), (0.53f, 0f)]));

    /// <summary>Baton space to the world, in an NPC's fist with that hand bone: out of the thumb side, leaning a little towards the knuckles.</summary>
    public static float4x4 InFist(in float4x4 hand)
    {
        if (hand.M21 == 0 && hand.M22 == 0 && hand.M23 == 0)
            return default;   // (not posed yet: the level editor can draw the first frame before anyone's been updated)
        var along = float3.Normalize(new float3(hand.M21, hand.M22, hand.M23));
        var thumb = -float3.Normalize(new float3(hand.M11, hand.M12, hand.M13));
        var axis = float3.Normalize(thumb + along * 0.3f);
        var x = float3.Normalize(float3.Cross(axis, along));
        var z = float3.Cross(x, axis);
        var at = hand.Translation + along * 0.08f;
        return new float4x4(
            x.X, x.Y, x.Z, 0,
            axis.X, axis.Y, axis.Z, 0,
            z.X, z.Y, z.Z, 0,
            at.X, at.Y, at.Z, 1);
    }

    // The free hand on someone's rifle (the intro): the AK's left hand, which is made to hold that gun, slid along to the barrel.
    private const float GrabSlide = 0.14f, GrabReach = 0.22f;
    private static readonly float3 GrabShoulder = new(-0.22f, -0.34f, 0.05f);   // camera space
    private static readonly float3 GrabPalm = new(0.27f, -0.045f, 0f);          // on the AK's handguard, in its model space
    private ModelPart? _grabHand;
    private ModelPart? _grabSleeve;
    private float _grabSleeveLength = 0.3f;
    private Func<float4x4>? _grabbing;
    private float _grabT;
    private float3 _eye;   // where the view is in the world (the view model's drawn relative to it)

    /// <summary>The left hand and sleeve for grabbing with (the AK model's).</summary>
    public void SetGrabHands(Model? model)
    {
        if (model is null)
            return;
        _grabHand = model.Parts.FirstOrDefault(p => p.Name == "HandLeft");
        var sleeves = SkinSleeves(model, ArmRig.Ak47);
        _grabSleeve = sleeves.GetValueOrDefault("SleeveLeft");
        if (_grabSleeve is null)
            return;
        var longest = 0f;
        var v = _grabSleeve.Vertices;
        for (var i = 0; i < v.Length; i += Mesh.FullStride)
            longest = float.Max(longest, float3.Dot(new float3(v[i], v[i + 1], v[i + 2]) - ArmRig.Ak47.LeftWrist, ArmRig.Ak47.LeftArm));
        _grabSleeveLength = float.Max(longest, 0.1f);
    }

    /// <summary>The free hand shoots out and takes hold of a rifle (that gun's model space to the world, as it moves); null lets go.</summary>
    public void Grab(Func<float4x4>? gunToWorld)
    {
        _grabbing = gunToWorld;
        _grabT = 0f;
    }

    private void DrawGrab(Renderer renderer, in Camera camera, in float4x4 gunInWorld)
    {
        if (_grabHand is null || _grabSleeve is null)
            return;
        var gun = gunInWorld * float4x4.CreateTranslation(camera.Position - _eye);
        var wrist = ArmRig.Ak47.LeftWrist;
        var arm = ArmRig.Ak47.LeftArm;
        var shoulder = float3.Transform(GrabShoulder, camera.World);

        // Round the barrel, so that the forearm comes from the shoulder.
        var slide = float4x4.CreateTranslation(GrabSlide, 0, 0) * gun;
        float4x4.Invert(slide, out var worldToSlide);
        var from = float3.Transform(shoulder, worldToSlide);
        var roll = float4x4.CreateRotationX(float.Atan2(from.Z, from.Y) - float.Atan2(arm.Z, arm.Y));
        var hand = roll * slide;

        // The whole hand turned about the palm, on the barrel, until its wrist lines up with the shoulder: the model's own
        // forearm runs on inside the sleeve, so the two have to go the same way.
        float4x4.Invert(hand, out var worldToRolled);
        var towards = float3.Normalize(float3.Transform(shoulder, worldToRolled) - GrabPalm);
        var axis = float3.Cross(arm, towards);
        var sin = float.Sqrt(float3.Dot(axis, axis));
        if (sin > 1e-4f)
            hand = RotateAbout(GrabPalm, float4x4.CreateFromAxisAngle(axis / sin, float.Atan2(sin, float3.Dot(arm, towards)))) * hand;

        var reach = Smooth(_grabT / GrabReach);
        hand.Translation = float3.Lerp(shoulder, hand.Translation, reach);

        // The forearm: stretched from the wrist to the shoulder.
        float4x4.Invert(hand, out var worldToHand);
        var to = float3.Transform(shoulder, worldToHand) - wrist;
        var length = float.Sqrt(float3.Dot(to, to));
        var arms = float4x4.CreateTranslation(-wrist) * ScaleAlong(arm, float.Max(length / _grabSleeveLength, 0.3f)) * float4x4.CreateTranslation(wrist) * hand;

        renderer.SetTextureSpace(worldToHand);
        renderer.DrawPart(_grabHand, hand, SleeveTint);
        renderer.DrawSkinnedPart(_grabSleeve, arms, hand, SleeveTint);
    }

    private static float4x4 ScaleAlong(float3 a, float k)
    {
        var s = k - 1f;
        return new float4x4(
            1 + s * a.X * a.X, s * a.X * a.Y, s * a.X * a.Z, 0,
            s * a.Y * a.X, 1 + s * a.Y * a.Y, s * a.Y * a.Z, 0,
            s * a.Z * a.X, s * a.Z * a.Y, 1 + s * a.Z * a.Z, 0,
            0, 0, 0, 1);
    }

    private float _swingT = float.MaxValue;
    private bool _landed;

    protected override void AbandonReload()
    {
    }

    protected override void Respawned() => _swingT = float.MaxValue;

    public override void Update(float dt, Input input, in Camera camera, PlayerController player, bool allowFire)
    {
        Tick(dt, player);
        _grabT += dt;
        _eye = camera.Position;
        _swingT = _swingT == float.MaxValue ? _swingT : _swingT + dt;
        if (_swingT >= SwingLength)
            _swingT = float.MaxValue;

        var ready = !_holstering && _drawT >= Feel.DrawTime && _swingT == float.MaxValue;
        if (allowFire && ready && input.WasPressed(Brutal.GlfwApi.GlfwMouseButton.Left))
        {
            _swingT = 0f;
            _landed = false;
            Log.Write("[BATON] Swing");
        }

        if (_swingT != float.MaxValue && !_landed && _swingT >= Contact)
        {
            _landed = true;
            if (Projectiles.Melee(camera.Position, camera.Forward, Reach))
                _viewKickVelocity += new float2(-0.55f, Range(-0.15f, 0.15f));   // the jar of it, up the arm
        }

        UpdateMotion(dt, input, player);
        RiseOverEdge(dt, camera, player);
        _aim = 0f;   // (nothing to aim down)
        ApplyDebugPose();
    }

    protected override void ApplyDebugPose(string pose, float t)
    {
        if (pose == "swing")
            _swingT = t;
    }

    /// <summary>
    /// The arm's and the wrist's turn at this point of the swing (radians; negative: forward and down), and how far it
    /// sweeps across to the left.
    /// </summary>
    private (float Arm, float Wrist, float Across) SwingPose()
    {
        if (_swingT == float.MaxValue)
            return default;
        var t = _swingT;
        if (t < WindUp)
        {
            var k = Smooth(t / WindUp);
            return (0.42f * k, 0.5f * k, -0.1f * k);
        }
        if (t < Strike)
        {
            var k = (t - WindUp) / (Strike - WindUp);
            k *= k;   // gathering speed all the way down
            return (float.Lerp(0.42f, -0.28f, k), float.Lerp(0.5f, -0.38f, k), float.Lerp(-0.1f, 0.62f, k));
        }
        var back = 1f - Smooth((t - Strike) / (SwingLength - Strike));
        return (-0.28f * back, -0.38f * back, 0.62f * back);
    }

    public override void Draw(Renderer renderer, in Camera camera)
    {
        if (Holstered)
            return;   // (it's long: put away, its tip would still show at the bottom of the view)
        var (arm, wrist, across) = SwingPose();
        // In the gun's frame: Z tips the top forward or back, X sweeps it across the body.
        var armSwing = RotateAbout(Elbow, float4x4.CreateRotationZ(arm) * float4x4.CreateRotationX(across));
        var hand = RotateAbout(Arms.RightWrist, float4x4.CreateRotationZ(CarryLean + wrist));
        // (The forearm follows the wrist's turn: the hand's rigid, and bent any further its end shows through the sleeve.)
        var carried = RotateAbout(Arms.RightWrist, float4x4.CreateRotationZ(CarryLean * 0.6f + wrist));
        var view = camera.World;
        var handToWorld = hand * armSwing * GunToCamera() * view;
        var armsToWorld = carried * armSwing * ArmsToCamera() * view;

        renderer.SetViewModel(true);
        float4x4.Invert(handToWorld, out var worldToHand);
        renderer.SetTextureSpace(worldToHand);

        var batonToWorld = BatonFrame() * handToWorld;
        renderer.DrawMesh(_grip, batonToWorld, new float3(0.035f, 0.035f, 0.038f), ShadeMode.Lit, Material.Polymer);
        renderer.DrawMesh(_shaft, batonToWorld, new float3(0.055f, 0.056f, 0.06f), ShadeMode.Lit, Material.Polymer);

        if (hands is not null)
        {
            var finger = RotateAbout(FingerKnuckle, float4x4.CreateRotationZ(FingerAlong)) * handToWorld;
            var pose = new HandsPose(handToWorld, finger, float4x4.Identity, handToWorld, armsToWorld);
            foreach (var part in hands.Parts)
                if (part.Name is "Hand" or "TriggerFinger" or "Sleeve")
                    DrawHandPart(renderer, part, pose);
        }

        if (_grabbing is { } grabbing)
            DrawGrab(renderer, camera, grabbing());

        renderer.SetTextureSpace(float4x4.Identity);
        renderer.SetViewModel(false);
    }

    /// <summary>Baton space (its axis along +Y, 0 at the middle of the fist) to the hand's model space.</summary>
    private static float4x4 BatonFrame()
    {
        var z = float3.UnitZ;
        var x = float3.Cross(Axis, z);
        return new float4x4(
            x.X, x.Y, x.Z, 0,
            Axis.X, Axis.Y, Axis.Z, 0,
            z.X, z.Y, z.Z, 0,
            Grip.X, Grip.Y, Grip.Z, 1);
    }

    /// <summary>The grip: a flared butt below the fist, then rubber ribs up to the shaft. (Along the baton, radius.)</summary>
    private static (float At, float Radius)[] GripProfile()
    {
        var profile = new List<(float, float)> { (-0.095f, 0f), (-0.095f, 0.017f), (-0.088f, 0.021f), (-0.078f, 0.021f), (-0.072f, 0.0165f) };
        for (var a = -0.068f; a < 0.06f; a += 0.008f)
        {
            profile.Add((a, 0.0165f));
            profile.Add((a + 0.002f, 0.0178f));
            profile.Add((a + 0.006f, 0.0178f));
        }
        profile.Add((0.062f, 0.0165f));
        profile.Add((0.066f, 0f));
        return [.. profile];
    }

    /// <summary>A round solid turned about +Y from (height, radius) points.</summary>
    private static Mesh Lathe(ReadOnlySpan<(float At, float Radius)> profile)
    {
        const int sides = 20;
        var b = new MeshBuilder();
        for (var i = 0; i < profile.Length - 1; i++)
        {
            var (y0, r0) = profile[i];
            var (y1, r1) = profile[i + 1];
            // The slope's normal, the same round the ring.
            var dy = y1 - y0;
            var dr = r1 - r0;
            var length = float.Sqrt(dy * dy + dr * dr);
            if (length < 1e-6f)
                continue;
            var out0 = dy / length;
            var up = -dr / length;
            for (var k = 0; k < sides; k++)
            {
                var a0 = float.Tau * k / sides;
                var a1 = float.Tau * (k + 1) / sides;
                var d0 = new float3(float.Cos(a0), 0, float.Sin(a0));
                var d1 = new float3(float.Cos(a1), 0, float.Sin(a1));
                var n0 = d0 * out0 + new float3(0, up, 0);
                var n1 = d1 * out0 + new float3(0, up, 0);
                var p00 = d0 * r0 + new float3(0, y0, 0);
                var p01 = d1 * r0 + new float3(0, y0, 0);
                var p10 = d0 * r1 + new float3(0, y1, 0);
                var p11 = d1 * r1 + new float3(0, y1, 0);
                b.Triangle(p00, n0, p10, n0, p11, n1);
                b.Triangle(p00, n0, p11, n1, p01, n1);
            }
        }
        return b.Build();
    }
}
