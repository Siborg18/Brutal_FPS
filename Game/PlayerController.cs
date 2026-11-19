using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Kinematic first-person controller: mouse look, WASD, sprint, crouch, prone, vaulting, gravity, ladders and
/// axis-separated AABB collision against the level.
/// </summary>
/// <remarks>
/// There's no jumping: Space vaults over a low obstacle in front of you (see <see cref="PlayerVault"/>), or up onto one where
/// the level allows it (<see cref="Level.ClimbAllowed"/>). C goes prone (and C again gets up): a low, small target that
/// crawls, with a steadier aim.
/// Ladders: walk into one (or drop onto it from above) to grab it. W climbs up, S climbs down; looking well down
/// swaps them, so you can walk forwards off a ledge and keep going down. At the top, keep moving forwards to step off;
/// at the bottom, climb down onto the floor and walk away. Space lets go.
/// </remarks>
public sealed class PlayerController
{
    // Body
    public const float Radius = 0.3f;
    public const float Height = 1.8f;
    public const float EyeHeight = 1.62f;
    public const float CrouchHeight = 1.1f;
    public const float CrouchEyeHeight = 0.95f;
    public const float ProneHeight = 0.5f;
    public const float ProneEyeHeight = 0.3f;
    private const float EyeShiftSpeed = 5f; // m/s the view moves between standing and crouched eye heights
    private const float ProneShiftSpeed = 2.4f; // m/s down to the ground or back up from it (slower: a whole body goes down)

    // Movement tuning
    public float WalkSpeed = 6.0f;
    public float CrouchSpeed = 3.0f;
    public float SprintSpeed = 9.5f;
    public float ProneSpeed = 1.1f;
    public float GroundAcceleration = 60f;
    public float AirAcceleration = 12f;
    public float Gravity = 22f;
    public float MouseSensitivity = 0.0022f; // radians per pixel
    public float LookScale = 1f;             // slower through a zoomed scope (Program sets it from the gun)
    public float LoadScale = 1f;             // slower with a heavy gun in hand (Program sets it from the gun)
    public float ClimbSpeed = 3.2f;

    private const float MaxPitch = 1.55f; // just under 90 degrees

    private readonly Level _level;

    /// <summary>
    /// Set while a cinematic walks the player (see <see cref="Cinematic"/>): the way to walk (a horizontal unit vector,
    /// or zero to stand still) at <see cref="RailSpeed"/>, instead of the keys. No jumping or sprinting meanwhile.
    /// </summary>
    public float3? RailWish;
    public float RailSpeed = 1.5f;

    /// <summary>Set while a cinematic steers the view: the mouse doesn't turn it here (the cinematic adds it itself).</summary>
    public bool LookLocked;

    /// <summary>
    /// Set while a cinematic carries the player bodily (over a wall, in a van): the script sets <see cref="Position"/> itself,
    /// and there's no gravity, no collision and nothing from the keys.
    /// </summary>
    public bool Carried;

    public float3 Position;  // feet, centre of the body
    public float3 Velocity;
    public float Yaw;
    public float Pitch;
    public bool Grounded { get; private set; }

    /// <summary>The ladder being climbed (an index into <see cref="Level.Ladders"/>), or -1.</summary>
    public int Climbing { get; private set; } = -1;
    private float _ladderCooldown;

    /// <summary>Distance travelled on the ground; drives view bob.</summary>
    public float StrideDistance { get; private set; }

    /// <summary>Running with Shift held, on the ground.</summary>
    public bool Sprinting { get; private set; }

    /// <summary>Ducking with Ctrl held: a shorter body, a lower view and slower walking. Stays down until there's headroom to stand.</summary>
    public bool Crouching { get; private set; }

    /// <summary>Lying flat (C): the lowest view, a crawl, and a small target. Stays down until there's room to get up.</summary>
    public bool Prone { get; private set; }

    /// <summary>0 standing or crouched, 1 flat on the ground: eases as you go down and get up (the aim steadies with it).</summary>
    public float ProneAmount => Prone ? float.Clamp((CrouchEyeHeight - _eyeHeight) / (CrouchEyeHeight - ProneEyeHeight), 0f, 1f) : 0f;

    /// <summary>
    /// Prone, how far up on the elbows the gun wants you (m): set each frame by the gun held when its barrel would ram
    /// an edge the sights see over. The view eases up to it, and back down when it's 0.
    /// </summary>
    public float ProneLiftTarget { get; set; }

    /// <summary>How far up on the elbows the view is right now (m, on top of the prone eye height).</summary>
    public float ProneLift { get; private set; }
    private const float ProneLiftRate = 9f;   // 1/s: how quickly the view rises onto the elbows and settles back

    /// <summary>The body's current height (shorter while crouching, shortest prone).</summary>
    public float BodyHeight => Prone ? ProneHeight : Crouching || Vaulting ? CrouchHeight : Height;

    // Vaulting: the plan being followed and how far along it is (s).
    private VaultPlan? _vault;
    private float _vaultTime, _vaultBuffer;
    private const float VaultBuffer = 0.2f;
    private readonly LevelSolids _solids;

    /// <summary>Going over (or up onto) something: the body follows the vault's path, not the keys.</summary>
    public bool Vaulting => _vault is not null;

    /// <summary>Counts vaults started (for tests and the log).</summary>
    public int Vaults { get; private set; }

    /// <summary>The eye's height above the feet, easing between standing and crouched.</summary>
    private float _eyeHeight = EyeHeight;

    // Stairs (only where Level.HasStairs says so: the rest of the game is built for a player who can't step up): walking into a
    // low enough box lifts you onto it, and walking off one sets you down on what's just below, not in free fall. The view
    // catches up over a moment so the climb doesn't jerk.
    private const float StepHeight = 0.2f, StepDownHeight = 0.26f, StepLagSpeed = 2.6f;
    private float _stepLag;
    private bool _couldStep;

    /// <summary>How fast the player was falling when they landed this frame (0 on other frames).</summary>
    public float LandingSpeed { get; private set; }

    /// <summary>Counts respawns (the weapon draws again after one).</summary>
    public int Respawns { get; private set; }

    // ------------------------------------------------------------------------------------------------ health
    public const float MaxHealth = 100f;
    public const float RespawnDelay = 3f;   // s lying dead before coming back at the spawn point

    public float Health { get; private set; } = MaxHealth;
    public bool Alive => Health > 0;

    /// <summary>Seconds since the player died (0 while alive).</summary>
    public float DeadFor { get; private set; }

    /// <summary>The last hit: how hard (0..1, fading) and where from (for the HUD's damage edge).</summary>
    public float HurtFlash { get; private set; }
    public float3 HurtFrom { get; private set; }

    /// <summary>Slowly back to full when not hit for a while (a little, so a long fight still wears you down).</summary>
    private float _sinceHurt;
    private const float RegenDelay = 6f, RegenRate = 4f, RegenCap = 60f;

    /// <summary>
    /// Never dies (the NPC test build: Game/NpcsTestArea.cs): hits take health and flash as usual, but one that would kill
    /// puts the player back to full health instead, counted in <see cref="Saves"/>.
    /// </summary>
    public bool Undying { get; set; }

    /// <summary>Whether the respawn key (+) respawns the player: not in the NPC test build, where it only resets the NPCs (Program.cs).</summary>
    public bool RespawnKeyRespawns { get; set; } = true;

    /// <summary>How many times <see cref="Undying"/> has kept the player from dying.</summary>
    public int Saves { get; private set; }

    /// <summary>A hit would have killed the player, but they're <see cref="Undying"/>.</summary>
    public event Action? SavedFromDeath;

    /// <summary>Shift sprints: not when overloaded (a raid's heaviest kit: Game/RaidLoadout.cs).</summary>
    public bool CanSprint { get; set; } = true;

    /// <summary>How far the player's footsteps carry (and how loud they are), against the usual: a raid's kit sets it.</summary>
    public float FootstepNoise { get; set; } = 1f;

    /// <summary>
    /// How weighed down the player is for changing stance: 0 light, 1 the heaviest kit (heavy armour and a machine gun).
    /// Program sets it each frame from the raid's kit and the gun in hand. Going down and getting up take longer with it,
    /// and over <see cref="HeavyGetUpLoad"/> getting up from prone is a heave: a moment rooted, gun down, a full-size target.
    /// </summary>
    public float KitLoad { get; set; }
    private const float HeavyGetUpLoad = 0.2f;

    /// <summary>Heaving up off the ground in heavy kit: can't move, aim or fire until it's done (see <see cref="GettingUpAmount"/>).</summary>
    public bool GettingUp => _getUpLeft > 0f;

    /// <summary>0..1 through the heave up (0 when not getting up).</summary>
    public float GettingUpAmount => GettingUp ? 1f - _getUpLeft / _getUpTime : 0f;

    private float _getUpLeft, _getUpTime, _getUpFrom, _getUpTo, _getUpSide = 1f;

    /// <summary>What's worn on a raid (Game/RaidLoadout.cs): body armour and a helmet. Nothing, outside one.</summary>
    public RaidArmour Armour { get; set; } = RaidArmour.None;

    /// <summary>
    /// A bullet's damage once what's worn has taken its share: <paramref name="at"/> is where it struck, which tells
    /// the head (the top of the body's box) from the body and the legs. At chest height a round can take an arm
    /// instead, and a chest plate doesn't cover that (full armour does).
    /// </summary>
    public float ArmouredDamage(float damage, float3 at, Random random)
    {
        var armour = Armour;
        if (armour == RaidArmour.None)
            return damage;
        var top = Bounds.Max.Y;
        var height = float.Clamp((at.Y - Position.Y) / float.Max(top - Position.Y, 0.2f), 0f, 1f);
        var part = Prone ? "body" : height > 0.86f ? "head" : height > 0.48f ? (random.NextDouble() < 0.25 ? "arm" : "body") : "leg";
        var scale = part switch
        {
            "head" => armour.Head,
            "body" => armour.Body,
            _ => armour.Limbs,
        };
        if (scale < 1f)
            Log.Write($"[ARMOUR] {part} hit: {damage:0} -> {damage * scale:0}");
        return damage * scale;
    }

    /// <summary>Shot (or otherwise hurt) for <paramref name="damage"/> from <paramref name="from"/>.</summary>
    public void TakeDamage(float damage, float3 from)
    {
        if (!Alive || damage <= 0)
            return;
        Health = float.Max(Health - damage, 0f);
        HurtFlash = float.Min(1f, HurtFlash + damage / 40f + 0.25f);
        HurtFrom = from;
        _sinceHurt = 0;
        if (!Alive && Undying)
        {
            Saves++;
            Health = MaxHealth;
            Log.Write($"[PLAYER] would have died (hit for {damage:0}): back to full health ({Saves} so far)");
            SavedFromDeath?.Invoke();
            return;
        }
        Log.Write(Alive ? $"[PLAYER] hit for {damage:0} ({Health:0} left)" : $"[PLAYER] killed (hit for {damage:0})");
        if (!Alive)
        {
            DeadFor = 0;
            Velocity = new float3(0, float.Min(Velocity.Y, 0f), 0);
            Sprinting = false;
            _vault = null;
        }
    }

    /// <summary>Patched up (a first aid kit): back to full health.</summary>
    public void Heal()
    {
        if (!Alive)
            return;
        Health = MaxHealth;
        HurtFlash = 0;
    }

    /// <summary>Per frame, alive or dead: the hurt flash fades, a little health comes back, a dead player falls and then respawns.</summary>
    public void UpdateHealth(float dt)
    {
        HurtFlash = float.Max(0f, HurtFlash - dt * 0.9f);
        if (Alive)
        {
            _sinceHurt += dt;
            if (_sinceHurt > RegenDelay && Health < RegenCap)
                Health = float.Min(Health + RegenRate * dt, RegenCap);
            return;
        }

        // Dead: down to whatever's below (off a ladder, out of a jump) rather than hanging in the air.
        DeadFor += dt;
        Climbing = -1;
        if (!Grounded)
        {
            Velocity.Y -= Gravity * dt;
            MoveAndCollide(dt);
        }

        if (DeadFor >= RespawnDelay || Position.Y < Level.KillHeightAt(Position))
            Respawn();
    }

    /// <summary>How far the view has dropped towards the floor (dead).</summary>
    private float DeathDrop => Alive ? 0f : float.Min(DeadFor / 0.6f, 1f) * (EyeHeight - 0.35f);

    public PlayerController(Level level)
    {
        _level = level;
        _solids = new LevelSolids(level);
        Respawn();
    }

    /// <summary>
    /// The boxes a bullet can hit the player in. Standing or crouched, the body's box; prone, the body lies along the
    /// ground behind the head (three small boxes along it, low), so it's a slim target from the front and a long one from the side.
    /// </summary>
    public int HitBoxes(Span<Aabb> into)
    {
        if (!Prone)
        {
            into[0] = Bounds;
            return 1;
        }
        var back = new float3(-float.Sin(Yaw), 0, float.Cos(Yaw));
        const float half = 0.22f;
        for (var i = 0; i < 3; i++)
        {
            var c = Position + back * (0.1f + i * 0.55f);
            into[i] = new Aabb(new float3(c.X - half, Position.Y, c.Z - half), new float3(c.X + half, Position.Y + (i == 0 ? 0.42f + ProneLift : 0.32f), c.Z + half));
        }
        return 3;
    }

    public float3 EyePosition => Position + new float3(0, float.Max(_eyeHeight - DeathDrop, 0.35f) + ProneLift + _stepLag, 0);

    public Aabb Bounds => new(
        Position - new float3(Radius, 0, Radius),
        Position + new float3(Radius, BodyHeight, Radius));

    public Camera ApplyTo(Camera camera)
    {
        var (lurchPitch, lurchSide) = GetUpLurch();
        camera.Position = EyePosition + new float3(float.Cos(Yaw), 0, float.Sin(Yaw)) * lurchSide;
        camera.Yaw = Yaw + lurchSide * 0.4f;
        camera.Pitch = float.Clamp(Pitch + lurchPitch, -MaxPitch, MaxPitch);
        return camera;
    }

    public void Respawn()
    {
        Position = _level.SpawnPoint;
        Velocity = float3.Zero;
        Yaw = _level.SpawnYaw;
        Pitch = 0;
        Climbing = -1;
        // Clear the movement state too, so nothing from before the respawn (a fall, a ladder) carries over a frame.
        Grounded = false;
        LandingSpeed = 0;
        Sprinting = false;
        Crouching = false;
        Prone = false;
        _vault = null;
        _eyeHeight = EyeHeight;
        ProneLift = ProneLiftTarget = 0f;
        _getUpLeft = 0f;
        _ladderCooldown = 0;
        Health = MaxHealth;
        HurtFlash = 0;
        DeadFor = 0;
        _sinceHurt = 0;
        Respawns++;
    }

    /// <summary>Came out of a portal: whatever ladder or floor you were on is on the other side.</summary>
    public void LeftThroughPortal()
    {
        Climbing = -1;
        Grounded = false;
    }

    public void Update(float dt, Input input)
    {
        // --- Look ---
        if (!LookLocked)
        {
            var mouse = input.MouseDelta;
            Yaw += mouse.X * MouseSensitivity * LookScale;
            Pitch = float.Clamp(Pitch - mouse.Y * MouseSensitivity * LookScale, -MaxPitch, MaxPitch);
        }

        if (Carried)
        {
            Velocity = float3.Zero;
            Grounded = false;
            Sprinting = false;
            Crouching = false;
            Prone = false;
            _vault = null;
            LandingSpeed = 0;
            Climbing = -1;
            _eyeHeight = EyeHeight;
            ProneLift = ProneLiftTarget = 0f;
            _getUpLeft = 0f;
            return;
        }

        if (_vault is { } vault)
        {
            FollowVault(dt, vault);
            return;
        }

        // --- Wish direction (on the horizontal plane) ---
        var forward = new float3(float.Sin(Yaw), 0, -float.Cos(Yaw));
        var right = new float3(float.Cos(Yaw), 0, float.Sin(Yaw));

        var wish = float3.Zero;
        if (input.IsDown(GlfwKey.W)) wish += forward;
        if (input.IsDown(GlfwKey.S)) wish -= forward;
        if (input.IsDown(GlfwKey.D)) wish += right;
        if (input.IsDown(GlfwKey.A)) wish -= right;
        wish = float3.NormalizeOrZero(wish);
        var railed = RailWish is not null;
        if (RailWish is { } rail)
            wish = float3.NormalizeOrZero(new float3(rail.X, 0, rail.Z));

        // --- Crouch and prone ---
        // C goes down flat (on the ground, off a ladder) and C again gets up: to standing if there's room, or crouched if
        // only that fits. Ctrl from prone comes up to a crouch; Space or Shift gets up. Nothing on rails.
        var wasProne = Prone || GettingUp;   // (Space that gets you up from prone isn't also a vault)
        var wantCrouch = !railed && (input.IsDown(GlfwKey.LeftControl) || input.IsDown(GlfwKey.RightControl));
        if (GettingUp)
        {
            // Heaving up in heavy kit: the stance keys wait until you're up.
            _getUpLeft = float.Max(0f, _getUpLeft - dt);
            _eyeHeight = GetUpEyeHeight(GettingUpAmount);
            if (!GettingUp)
            {
                _eyeHeight = _getUpTo;
                Log.Write("[PLAYER] up (heavy kit)");
            }
        }
        else if (railed && Prone && Fits(CrouchHeight))
            Prone = false;
        else if (!railed && input.WasPressed(GlfwKey.C))
        {
            if (!Prone && Grounded && Climbing < 0)
            {
                Prone = true;
                Sprinting = false;
                Log.Write("[PLAYER] prone");
            }
            else if (Prone)
                GetUp(wantCrouch);
        }
        else if (Prone && (wantCrouch || input.WasPressed(GlfwKey.Space) || input.WasPressed(GlfwKey.LeftShift)))
            GetUp(wantCrouch);

        if (!Prone && !GettingUp)
        {
            if (wantCrouch)
                Crouching = true;
            else if (Crouching && CanStand())
                Crouching = false;
        }
        if (!GettingUp)
        {
            // Heavier kit, slower down and up: up to twice as long to the ground and back, 1.7x to a crouch.
            var load = float.Clamp(KitLoad, 0f, 1f);
            var eyeTarget = Prone ? ProneEyeHeight : Crouching ? CrouchEyeHeight : EyeHeight;
            var shift = (Prone || _eyeHeight < CrouchEyeHeight - 0.01f ? ProneShiftSpeed / (1f + load) : EyeShiftSpeed / (1f + 0.7f * load)) * dt;
            _eyeHeight = float.Abs(eyeTarget - _eyeHeight) <= shift
                ? eyeTarget
                : _eyeHeight + float.Sign(eyeTarget - _eyeHeight) * shift;
        }
        ProneLift += ((Prone ? ProneLiftTarget : 0f) - ProneLift) * (1f - MathF.Exp(-ProneLiftRate * dt));

        var sprintHeld = CanSprint && !Crouching && !Prone && !GettingUp && input.IsDown(GlfwKey.LeftShift);
        // (Prone, you only crawl once you're down: not while still getting to the ground. Heaving up, you don't move at all.)
        var speed = railed ? RailSpeed
            : GettingUp ? 0f
            : (Prone ? ProneSpeed * ProneAmount : Crouching ? CrouchSpeed : sprintHeld ? SprintSpeed : WalkSpeed) * LoadScale;
        Sprinting = !railed && sprintHeld && wish != float3.Zero && Grounded;
        LandingSpeed = 0;
        var targetVelocity = wish * speed;

        // --- Ladders ---
        UpdateLadder(dt, wish);
        if (Climbing >= 0)
        {
            // Up at the top with your feet clear of the floor: step off. Still pushing on up the rungs takes you off the
            // ladder's way (over the top, or back onto the walkway); any other way walks off that way.
            var ladder = _level.Ladders[Climbing];
            var off = float3.Dot(wish, ladder.Facing) > 0.5f ? ladder.Exit : wish;
            if (Position.Y >= ladder.Top + 0.1f && off != float3.Zero)
            {
                Climbing = -1;
                _ladderCooldown = 0.4f;
                Velocity = off * WalkSpeed * 0.7f + new float3(0, 1.5f, 0);
            }
            else
            {
                Climb(dt, input, wish);
                return;
            }
        }

        // --- Accelerate horizontal velocity towards the target ---
        var accel = Grounded ? GroundAcceleration : AirAcceleration;
        var horizontal = new float2(Velocity.X, Velocity.Z);
        var target = new float2(targetVelocity.X, targetVelocity.Z);
        horizontal = MoveTowards(horizontal, target, accel * dt);
        Velocity.X = horizontal.X;
        Velocity.Z = horizontal.Y;

        // --- Vault (no jumping): Space at something low in front, the way you're walking (or looking, standing still) ---
        // (A press a moment before reaching it still counts.)
        _vaultBuffer = !railed && !wasProne && input.WasPressed(GlfwKey.Space) ? VaultBuffer : _vaultBuffer - dt;
        if (Grounded && _vaultBuffer > 0 && TryVault(wish != float3.Zero ? wish : forward))
            return;

        Velocity.Y -= Gravity * dt;

        // --- Move & collide ---
        var before = Position;
        var wasGrounded = Grounded;
        var fallSpeed = -Velocity.Y;
        MoveAndCollide(dt);
        if (Grounded && !wasGrounded)
            LandingSpeed = float.Max(0, fallSpeed);

        if (Grounded)
        {
            var moved = Position - before;
            StrideDistance += float.Sqrt(moved.X * moved.X + moved.Z * moved.Z);
        }

        if (Position.Y < Level.KillHeightAt(Position) || (input.RespawnPressed && RespawnKeyRespawns))
            Respawn();
    }

    /// <summary>Up from prone: standing if there's room (or crouched, asked for or if that's all that fits). Stays down if neither does.</summary>
    private void GetUp(bool toCrouch)
    {
        if (!toCrouch && Fits(Height))
            (Prone, Crouching) = (false, false);
        else if (Fits(CrouchHeight))
            (Prone, Crouching) = (false, true);
        else
            return;
        Log.Write("[PLAYER] up from prone");
        if (KitLoad > HeavyGetUpLoad)
        {
            // Heavy kit: a heave up onto a knee, a breath, and up the rest of the way (0.9 s, to 1.6 s in the heaviest).
            _getUpTime = _getUpLeft = 0.7f + 0.9f * float.Min(KitLoad, 1f);
            _getUpFrom = _eyeHeight;
            _getUpTo = Crouching ? CrouchEyeHeight : EyeHeight;
            _getUpSide = -_getUpSide;
            Sprinting = false;
            Log.Write($"[PLAYER] heaving up in heavy kit ({_getUpTime:0.00} s)");
        }
    }

    /// <summary>The eye's height through the heave up: onto a knee, a sag as you brace, then up to standing (or a crouch).</summary>
    private float GetUpEyeHeight(float t)
    {
        var knee = float.Min(_getUpTo, 0.85f);
        if (t < 0.45f)
            return float.Lerp(_getUpFrom, knee, SmoothStep(t / 0.45f));
        if (t < 0.62f)
            return knee - 0.05f * float.Sin((t - 0.45f) / 0.17f * MathF.PI);
        return float.Lerp(knee, _getUpTo, SmoothStep((t - 0.62f) / 0.38f));
    }

    private static float SmoothStep(float x)
    {
        x = float.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    /// <summary>The view's lurch while heaving up: head down over the knee and a lean to one side, settling as you rise.</summary>
    private (float Pitch, float Side) GetUpLurch()
    {
        if (!GettingUp)
            return (0f, 0f);
        var t = GettingUpAmount;
        var dip = float.Sin(float.Min(t / 0.8f, 1f) * MathF.PI);          // head down through the kneel, back up as you stand
        var lean = float.Sin(t * MathF.PI) * (1f - 0.3f * t);
        var wobble = float.Sin(t * 19f) * 0.012f * (1f - t);                // the effort of it
        return (-0.2f * dip + wobble, 0.09f * lean * _getUpSide);
    }

    /// <summary>Whether a body this tall would fit where the player is (nothing solid it isn't already in).</summary>
    private bool Fits(float height)
    {
        var body = new Aabb(Position - new float3(Radius, 0, Radius), Position + new float3(Radius, height, Radius));
        var now = Bounds;
        return !Blocks(_level.Boxes, body, now) && !Blocks(_level.MovingBoxes, body, now);
    }

    /// <summary>Whether a climb up onto the top of something at <paramref name="feet"/> is allowed there (see <see cref="Level.ClimbAllowed"/>).</summary>
    private bool OntoAllowed(float3 feet) => _level.ClimbAllowed(feet);

    /// <summary>Starts a vault that way, if there's one to make.</summary>
    private bool TryVault(float3 dir)
    {
        // (A no-climb area stops a vault over into it too: off a roof over its parapet, over a catwalk's railing.)
        if (PlayerVault.Plan(_solids, Position, dir, OntoAllowed) is not { } plan || !OntoAllowed(plan.To))
            return false;
        _vault = plan;
        _vaultTime = 0;
        _vaultBuffer = 0;
        Crouching = false;
        Sprinting = false;
        Grounded = false;
        Vaults++;
        Log.Write($"[PLAYER] vault {(plan.Onto ? "up onto" : "over")} {plan.Top - plan.From.Y:0.00} m at {plan.Obstacle.X:0.00},{plan.Obstacle.Y:0.00},{plan.Obstacle.Z:0.00}" +
                  $" to {plan.To.X:0.00},{plan.To.Y:0.00},{plan.To.Z:0.00} ({plan.Duration:0.00} s)");
        return true;
    }

    /// <summary>
    /// Along the vault: up to the top while closing on it (the first part), across it at that height, and (over it) down
    /// on the far side. The path was checked clear when it was planned, so nothing collides meanwhile.
    /// </summary>
    private void FollowVault(float dt, VaultPlan v)
    {
        const float rise = 0.4f;                    // the share of the time spent getting up to the top
        var drop = v.Onto ? 0f : 0.22f;              // and coming down on the far side
        _vaultTime += dt;
        var t = float.Min(_vaultTime / v.Duration, 1f);
        var flatFrom = v.From with { Y = 0 };
        var flatTo = v.To with { Y = 0 };
        var dir = float3.NormalizeOrZero(flatTo - flatFrom);
        var near = float3.Distance(v.Obstacle with { Y = 0 }, flatFrom);
        var total = float3.Distance(flatTo, flatFrom);
        var top = v.Top + 0.02f;

        float along, y;
        if (t < rise)
        {
            var u = t / rise;
            along = near * u;
            y = v.From.Y + (top - v.From.Y) * (1f - (1f - u) * (1f - u));
        }
        else if (t < 1f - drop)
        {
            var u = (t - rise) / (1f - drop - rise);
            along = near + (total - near) * (u * u * (3f - 2f * u));
            y = top;
        }
        else
        {
            var u = drop > 0 ? (t - (1f - drop)) / drop : 1f;
            along = total;
            y = top + (v.To.Y - top) * u * u;
        }

        var before = Position;
        Position = new float3(v.From.X + dir.X * along, y, v.From.Z + dir.Z * along);
        Velocity = dt > 0 ? (Position - before) / dt : float3.Zero;
        LandingSpeed = 0;
        _eyeHeight += (1.2f - _eyeHeight) * float.Min(1f, dt * 10f);   // ducked over it

        if (t >= 1f)
        {
            Position = v.To;
            _vault = null;
            Crouching = v.Crouched;
            Grounded = true;
            Velocity = dir * WalkSpeed * 0.4f;
            var fell = top - v.To.Y;
            if (fell > 0.4f)
                LandingSpeed = float.Sqrt(2f * Gravity * fell) * 0.6f;
        }
    }

    /// <summary>Grabs a ladder you're touching (walking into it, or falling past it) and lets go of one you've left.</summary>
    private void UpdateLadder(float dt, float3 wish)
    {
        _ladderCooldown -= dt;
        var body = Bounds;
        var touching = -1;
        if (Climbing >= 0 && body.Overlaps(_level.Ladders[Climbing].Volume))
            touching = Climbing; // overlapping ladder volumes: stay on the one being climbed
        else
        {
            for (var i = 0; i < _level.Ladders.Count; i++)
            {
                if (body.Overlaps(_level.Ladders[i].Volume))
                {
                    touching = i;
                    break;
                }
            }
        }

        if (Climbing >= 0 && touching != Climbing)
            Climbing = -1;
        if (Climbing >= 0 || touching < 0 || _ladderCooldown > 0 || Prone)
            return;

        // Standing at the foot: walk into the rungs to start climbing. In the air: grab it.
        var ladder = _level.Ladders[touching];
        var towards = float3.Dot(wish, ladder.Facing) > 0.5f && Position.Y < ladder.Top - 0.5f;
        if ((!Grounded && Position.Y < ladder.Top - 0.3f) || towards)
        {
            Climbing = touching;
            Velocity = float3.Zero;
        }
    }

    private void Climb(float dt, Input input, float3 wish)
    {
        var ladder = _level.Ladders[Climbing];
        var climb = (input.IsDown(GlfwKey.W) ? 1f : 0f) - (input.IsDown(GlfwKey.S) ? 1f : 0f);
        if (Pitch < -0.5f)
            climb = -climb; // looking down at your feet: forwards takes you down

        if (input.WasPressed(GlfwKey.Space))
        {
            // Let go, pushing off from the rungs.
            Climbing = -1;
            _ladderCooldown = 0.5f;
            Velocity = -ladder.Facing * 2f;
            MoveAndCollide(dt);
            return;
        }

        Velocity.Y = climb * ClimbSpeed * LoadScale;
        if (Position.Y > ladder.Top + 0.25f && Velocity.Y > 0)
            Velocity.Y = 0;

        // On the rungs you can only shuffle sideways (and you're held in against them). Once your feet are clear of
        // the floor at the top, Update lets you walk off.
        var sideways = wish - ladder.Facing * float3.Dot(wish, ladder.Facing);
        var horizontal = sideways * 1.5f + ladder.Facing * 0.6f;
        Velocity.X = horizontal.X;
        Velocity.Z = horizontal.Z;

        MoveAndCollide(dt);

        // Climbed down onto the floor and still going down: let go and walk.
        if (Grounded && climb < 0)
            Climbing = -1;

        if (Position.Y < Level.KillHeightAt(Position) || (input.RespawnPressed && RespawnKeyRespawns))
            Respawn();
    }

    private void MoveAndCollide(float dt)
    {
        _couldStep = Grounded && Level.HasStairs(Position);
        _stepLag = _stepLag > 0 ? float.Max(_stepLag - StepLagSpeed * dt, 0f) : float.Min(_stepLag + StepLagSpeed * dt, 0f);
        MoveAndCollideAxes(dt);
        if (_couldStep && !Grounded && Velocity.Y <= 0)
            StepDown();
        // At high frame rates a frame's fall can be smaller than the collision skin, so the feet don't reach the floor
        // every frame: also count as grounded when standing right on top of something.
        if (!Grounded && Velocity.Y <= 0 && OnFloor())
            Grounded = true;
    }

    private bool OnFloor()
    {
        var feet = new Aabb(
            Position - new float3(Radius - 0.01f, 0.004f, Radius - 0.01f),
            Position + new float3(Radius - 0.01f, 0.0f, Radius - 0.01f));
        foreach (var box in _level.Boxes)
        {
            if (box.Collides && box.Bounds.Max.Y <= Position.Y + 0.001f && feet.Overlaps(box.Bounds))
                return true;
        }
        foreach (var box in _level.MovingBoxes)
        {
            if (box.Collides && box.Bounds.Max.Y <= Position.Y + 0.001f && feet.Overlaps(box.Bounds))
                return true;
        }

        return false;
    }

    private void MoveAndCollideAxes(float dt)
    {
        Grounded = false;

        // Resolve one axis at a time: simple, stable, and lets the player slide along walls.
        // Y first so ground contact is known before horizontal movement.
        ReadOnlySpan<int> axes = [1, 0, 2];
        foreach (var axis in axes)
        {
            var delta = Velocity[axis] * dt;
            if (delta == 0)
                continue;

            var before = Bounds;
            var origin = Position[axis];
            Position[axis] += delta;
            var body = Bounds;

            foreach (var box in _level.Boxes)
                Resolve(box, axis, delta, origin, before, ref body);
            foreach (var box in _level.MovingBoxes)
                Resolve(box, axis, delta, origin, before, ref body);
        }
    }

    private void Resolve(in LevelBox box, int axis, float delta, float origin, in Aabb before, ref Aabb body)
    {
        if (!box.Collides || !body.Overlaps(box.Bounds))
            return;

        if (axis != 1 && _couldStep && TryStepUp(box.Bounds, ref body))
            return;

        var b = box.Bounds;
        if (before.Overlaps(b))
        {
            // Already inside it before this move (just after a teleport, pushed in by someone, a portal blocker that
            // appeared around you, or float rounding far from the origin). Don't snap out to a far face, but only allow
            // moves towards the nearest face on this axis: walking further in would carry you through the wall.
            var outIsNegative = before.Max[axis] - b.Min[axis] < b.Max[axis] - before.Min[axis];
            if ((delta > 0) == outIsNegative)
            {
                Position[axis] = origin;   // cancel this axis's move
                Velocity[axis] = 0;
                if (axis == 1 && delta < 0)
                    Grounded = true;
                body = Bounds;
            }
            return;
        }

        // Push the body back out on this axis only.
        const float skin = 0.0005f;
        if (delta > 0)
        {
            var limit = b.Min[axis] - skin;
            Position[axis] = axis == 1 ? limit - BodyHeight : limit - Radius;
        }
        else
        {
            var limit = b.Max[axis] + skin;
            Position[axis] = axis == 1 ? limit : limit + Radius;
            if (axis == 1)
                Grounded = true;
        }

        // Far from the origin (the jungle is at x = 20000) a float step is bigger than the skin, so the snap can
        // round back into the box. Step out one representable value at a time until clear.
        for (var i = 0; i < 8 && Bounds.Overlaps(b); i++)
            Position[axis] = delta > 0 ? float.BitDecrement(Position[axis]) : float.BitIncrement(Position[axis]);

        Velocity[axis] = 0;
        body = Bounds;
    }

    /// <summary>Walked into something low enough to step onto: up onto it, if there's room for your head.</summary>
    private bool TryStepUp(in Aabb box, ref Aabb body)
    {
        var rise = box.Max.Y - Position.Y;
        if (rise <= 0 || rise > StepHeight)
            return false;
        var was = Position.Y;
        Position.Y = box.Max.Y + 0.0005f;
        var raised = Bounds;
        foreach (var other in _level.Boxes)
        {
            if (other.Collides && raised.Overlaps(other.Bounds))
            {
                Position.Y = was;
                return false;
            }
        }
        foreach (var other in _level.MovingBoxes)
        {
            if (other.Collides && raised.Overlaps(other.Bounds))
            {
                Position.Y = was;
                return false;
            }
        }
        _stepLag = float.Max(_stepLag - rise, -0.35f);
        Velocity.Y = 0;
        Grounded = true;
        body = raised;
        return true;
    }

    /// <summary>Walked off a step: down onto what's just below your feet, if anything is.</summary>
    private void StepDown()
    {
        var feet = new Aabb(
            Position - new float3(Radius - 0.01f, StepDownHeight, Radius - 0.01f),
            Position + new float3(Radius - 0.01f, 0.0f, Radius - 0.01f));
        var floor = float.NegativeInfinity;
        foreach (var box in _level.Boxes)
        {
            if (box.Collides && box.Bounds.Max.Y <= Position.Y + 0.001f && box.Bounds.Max.Y > floor && feet.Overlaps(box.Bounds))
                floor = box.Bounds.Max.Y;
        }
        if (float.IsNegativeInfinity(floor))
            return;
        _stepLag = float.Min(_stepLag + (Position.Y - floor), 0.35f);
        Position.Y = floor + 0.0005f;
        Velocity.Y = 0;
        Grounded = true;
    }

    /// <summary>
    /// Moves the player sideways by <paramref name="offset"/> (being shoved by someone), but not into anything solid:
    /// an axis that would push the body into a box it wasn't already touching is dropped.
    /// </summary>
    public void Nudge(float3 offset)
    {
        ReadOnlySpan<int> axes = [0, 2];
        foreach (var axis in axes)
        {
            if (offset[axis] == 0)
                continue;
            var before = Bounds;
            Position[axis] += offset[axis];
            var body = Bounds;
            if (Blocks(_level.Boxes, body, before) || Blocks(_level.MovingBoxes, body, before))
                Position[axis] -= offset[axis];
        }
    }

    /// <summary>Whether a full-height body would fit here (nothing solid over a crouching player's head).</summary>
    private bool CanStand()
    {
        var standing = new Aabb(
            Position - new float3(Radius, 0, Radius),
            Position + new float3(Radius, Height, Radius));
        var crouched = Bounds;
        return !Blocks(_level.Boxes, standing, crouched) && !Blocks(_level.MovingBoxes, standing, crouched);
    }

    private static bool Blocks(List<LevelBox> boxes, in Aabb body, in Aabb before)
    {
        foreach (var box in boxes)
        {
            if (box.Collides && body.Overlaps(box.Bounds) && !before.Overlaps(box.Bounds))
                return true;
        }
        return false;
    }

    private static float2 MoveTowards(float2 current, float2 target, float maxDelta)
    {
        var diff = target - current;
        var length = float.Sqrt(diff.X * diff.X + diff.Y * diff.Y);
        return length <= maxDelta || length < 1e-6f ? target : current + diff / length * maxDelta;
    }
}
