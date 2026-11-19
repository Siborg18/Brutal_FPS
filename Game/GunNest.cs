using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// An M249 on a mount that a gunner stands behind: the tripod of the jungle's gun nest (<see cref="Jungle"/>), or the turret on
/// the roof of the armoured car (<see cref="ArmouredCar"/>, Game/LevelArmouredCar.cs).
/// <para>
/// The jungle's: E within reach takes it off, and Program brings it up as the gun in hand; from then on it's yours, on 7 and
/// the mouse wheel like the other guns. With it in hand at the nest, E puts it back on the tripod, and until it's taken again
/// 7 does nothing. It keeps the rounds it has. A respawn puts it back on the tripod, with a full belt.
/// </para>
/// <para>
/// On the tripod it's also there for the guards (<see cref="ArmedNpc"/>, Game/NpcGunner.cs): one at a time, and only while it's
/// there. Someone on it (<see cref="Gunner"/>) keeps it from you until they leave or you shoot them off it. Someone on their
/// way to it (<see cref="Claim"/>) doesn't: get there first and it's yours, and they go back to their AK. With you carrying it,
/// they can't have it. Their rounds come off the same belt, so what's left on it is what you'll find, and when a gunner dies
/// it stays where it was laid, its butt sagging, for you or the next guard.
/// </para>
/// <para>
/// The armoured car's (<see cref="Crewed"/>): it's bolted to the turret, which goes right round on its ring with the gun, the
/// gunner standing in the hatch under it. Only the car's own gunner uses it (they never climb out), and it can't be taken.
/// </para>
/// </summary>
public sealed class GunNest
{
    private const float Reach = 2.0f;

    /// <summary>The jungle nest's arc: how far it swings either way off the nest's facing (rad), out over the clearing, short of the ends of the bags.</summary>
    public const float NestArcHalf = 1.05f;

    private const float TraverseRate = 1.15f, ElevateRate = 0.6f;   // rad/s
    private const float Sag = 0.09f;                          // rad: its nose comes up this far with nobody holding the butt down
    private const float KickPitch = 0.007f, KickBack = 0.012f;

    // Where someone behind it stands: this far behind the pintle and to the left of the gun's line.
    // (Tools/blender/humankit/gunnerlib.py: TRIPOD_BACK, GUN_RIGHT, built to these.)
    private const float SeatBack = 0.80f, SeatRight = 0.13f;

    /// <summary>How far the cradle is above the gunner's feet (the ManGun clip is built to it).</summary>
    public const float PintleHeight = 1.06f;

    // Where the gun sits on the cradle (model space): the handguard's underside, just ahead of the pouch.
    private static readonly float3 MountPoint = new(0.20f, -0.02f, 0f);

    private readonly Lmg _gun;

    /// <summary>
    /// <paramref name="pivot"/>: what it turns about (the tripod's head; a turret's ring, on the roof). <paramref name="reach"/> and
    /// <paramref name="cradleUp"/>: where the cradle is from there (a turret's gun is out at the front of the ring).
    /// <paramref name="floorY"/>: where the gunner stands (null: on the ground where they are). <paramref name="turret"/>: drawn
    /// turned with the gun, about the pivot (its front along -Z).
    /// </summary>
    private GunNest(Lmg gun, float3 pivot, float facing, float arcHalf, float pitchUp, float pitchDown, float reach = 0f, float cradleUp = 0f,
        float? floorY = null, bool crewed = false, Model? turret = null)
    {
        _gun = gun;
        Pivot = pivot;
        Facing = facing;
        ArcHalf = arcHalf;
        PitchUp = pitchUp;
        PitchDown = pitchDown;
        _reach = reach;
        _cradleUp = cradleUp;
        FloorY = floorY;
        Crewed = crewed;
        _turret = turret;
        Yaw = facing;
    }

    /// <summary>The jungle gun nest's tripod (Level.BuildGunNest).</summary>
    public static GunNest Jungle(Lmg gun) => new(gun, Level.GunNestCradle, Level.GunNestFacing, NestArcHalf, 0.26f, 0.14f);

    /// <summary>The armoured car's roof turret (Level.BuildArmouredCar): right round, and its gunner stays in the hatch.</summary>
    public static GunNest ArmouredCar(Lmg gun, Model? turret) =>
        new(gun, Level.ArmouredCarTurret, Level.ArmouredCarFacing, float.Pi, 0.55f, 0.42f, Level.ArmouredCarCradleAhead, Level.ArmouredCarCradleUp,
            Level.ArmouredCarTurret.Y + Level.ArmouredCarCradleUp - PintleHeight, crewed: true, turret);

    /// <summary>
    /// The turret of an armoured car parked somewhere else (the army's, on a factory raid: Game/FactoryArmy.cs), its ring's
    /// middle at <paramref name="ring"/> on the roof, laid at rest towards <paramref name="facing"/>.
    /// </summary>
    public static GunNest Vehicle(Lmg gun, Model? turret, float3 ring, float facing) =>
        new(gun, ring, facing, float.Pi, 0.55f, 0.42f, Level.ArmouredCarCradleAhead, Level.ArmouredCarCradleUp,
            ring.Y + Level.ArmouredCarCradleUp - PintleHeight, crewed: true, turret);

    /// <summary>Takes its armour out of the physics world again (its car's gone).</summary>
    public void RemovePhysics()
    {
        if (_physics is not null)
            foreach (var obj in _armour)
                _physics.RemoveDoorPart(obj);
        _armour.Clear();
        _physics = null;
    }

    private readonly float _reach, _cradleUp;
    private readonly Model? _turret;

    // The turret's armour in the physics world (AddPhysics), so bullets ring off its shield and people can't see through it;
    // moved with it as it turns (MoveArmour).
    private readonly List<BulletSharp.CollisionObject> _armour = [];
    private PhysicsWorld? _physics;
    private float _armourYaw;

    /// <summary>
    /// Puts the turret (ring, plates, lids) into the physics world as parts that follow it round, as the doors' are. The gun
    /// itself stays out of it: its own rounds start at its muzzle, out in front of the plate, through the window.
    /// </summary>
    public void AddPhysics(PhysicsWorld physics, SurfaceTable surfaces)
    {
        if (_turret is null)
            return;
        _physics = physics;
        _armourYaw = Yaw;
        var pose = TurretPose;
        foreach (var part in _turret.Parts)
        {
            if (SurfaceInfo.ForPart(part.Material, float3.One) is not { } surface || IsHatchHole(part.Mesh))
                continue;
            if (surface.Kind == SurfaceKind.Plastic)
                surface = surface with { Kind = SurfaceKind.PaintedMetal };   // (flat paint reads as plastic: it's armour plate)
            var (restitution, friction) = surface.Contact;
            if (physics.AddDoorPart(part.Mesh, pose, surfaces.Index(surface), restitution, friction) is { } obj)
                _armour.Add(obj);
        }
        Log.Write($"Armoured car turret: {_armour.Count} of {_turret.Parts.Count} parts stop bullets");
    }

    /// <summary>
    /// The dark disc drawn over the hatch, flat on the ring's floor inside it: not a thing, so not in the way (the gunner stands
    /// through it, and falls through it when shot).
    /// </summary>
    private static bool IsHatchHole(Mesh mesh)
    {
        var p = mesh.Positions;
        for (var i = 0; i + 2 < p.Length; i += 3)
        {
            if (float.Abs(p[i + 1]) > 0.03f || p[i] * p[i] + p[i + 2] * p[i + 2] > 0.62f * 0.62f)
                return false;
        }

        return p.Length > 0;
    }

    /// <summary>Brings the turret's armour round to where it's laid now (when it has turned).</summary>
    private void MoveArmour()
    {
        if (_physics is null || _armourYaw == Yaw)
            return;
        _armourYaw = Yaw;
        var pose = TurretPose;
        foreach (var obj in _armour)
            _physics.MoveDoorPart(obj, pose);
    }

    /// <summary>What it turns about: the tripod's head, or the middle of a turret's ring.</summary>
    public float3 Pivot { get; }

    /// <summary>Which way it's laid at rest (radians from south, +Z, towards the east).</summary>
    public float Facing { get; }

    /// <summary>How far it swings either way off <see cref="Facing"/> (rad): pi goes right round.</summary>
    public float ArcHalf { get; }

    public float PitchUp { get; }
    public float PitchDown { get; }

    /// <summary>Where its gunner stands (null: on the ground).</summary>
    public float? FloorY { get; }

    /// <summary>A vehicle's: its own gunner stays on it whatever happens, nobody else goes for it, and you can't take it.</summary>
    public bool Crewed { get; }

    /// <summary>It goes right round.</summary>
    public bool AllRound => ArcHalf >= float.Pi - 1e-3f;

    /// <summary>Taken off the tripod: 7 and the mouse wheel can bring it up.</summary>
    public bool Carried { get; private set; }

    /// <summary>"[E] Take the M249" and so on while there's something to do with it here, otherwise null.</summary>
    public string? Prompt { get; private set; }

    /// <summary>Who's on it (null: nobody).</summary>
    public ArmedNpc? Gunner { get; private set; }

    /// <summary>Which way it's laid (radians from south, +Z, towards the east, as <see cref="Facing"/>).</summary>
    public float Yaw { get; private set; }

    /// <summary>How far it's laid up (rad).</summary>
    public float Pitch { get; private set; }

    private ArmedNpc? _claimant;
    private float _kick;
    private float _sinceShot = 10f;
    private float _coverAngle;
    private float _coverLeft;
    private int _tracerCount;

    /// <summary>Where the gun lies, laid as it is now.</summary>
    public float3 Cradle => CradleAt(Yaw);

    private float3 CradleAt(float yaw)
    {
        var (sin, cos) = float.SinCos(yaw);
        return Pivot + new float3(sin, 0, cos) * _reach + new float3(0, _cradleUp, 0);
    }

    /// <summary>On the tripod, with nobody on it or on their way to it.</summary>
    public bool Free => !Carried && Gunner is null && _claimant is not { Alive: true };

    public int Rounds => _gun.AmmoInMag + (_gun.Chambered ? 1 : 0);

    /// <summary>The pitch to draw it at, and to lean the gunner's upper body by: where it's laid, and its kick.</summary>
    public float ShownPitch => Pitch + KickPitch * _kick;

    /// <summary>How far back the kick has it (m).</summary>
    public float ShownBack => KickBack * _kick;

    /// <summary>The cradle in the space of a figure standing at <see cref="Seat"/>.</summary>
    public static float3 PintleOnFigure => new(-SeatRight, PintleHeight, SeatBack);

    private float4x4 GunPose =>
        float4x4.CreateTranslation(-MountPoint + new float3(-ShownBack, 0, 0))
        * float4x4.CreateRotationZ(ShownPitch)
        * float4x4.CreateRotationY(Yaw - float.Pi / 2)   // +X (the muzzle) along where it's laid
        * float4x4.CreateTranslation(Cradle);

    // The turret model's front is its -Z: turned half round more than the gun's +Z would be.
    private float4x4 TurretPose => float4x4.CreateRotationY(Yaw + float.Pi) * float4x4.CreateTranslation(Pivot);

    public float3 Muzzle => float3.Transform(Lmg.MuzzleOnModel, GunPose);

    /// <summary>Where someone stands to work it, laid at <paramref name="yaw"/>: they go round with it.</summary>
    public float3 Seat(float yaw, float groundY)
    {
        var (sin, cos) = float.SinCos(yaw);
        var ahead = new float3(sin, 0, cos);
        var left = new float3(cos, 0, -sin);
        var at = CradleAt(yaw) - ahead * SeatBack + left * SeatRight;
        return at with { Y = FloorY ?? groundY };
    }

    /// <summary>How far round from its facing a point is (rad, either way).</summary>
    public float OffLine(float3 at)
    {
        var to = at - Pivot;
        return WrapAngle(float.Atan2(to.X, to.Z) - Facing);
    }

    public bool InArc(float3 at) => AllRound || float.Abs(OffLine(at)) < ArcHalf - 0.05f;

    // ------------------------------------------------------------------------------------------------ the guards

    /// <summary>Someone's on their way to it: nobody else need go. False: too late.</summary>
    public bool Claim(ArmedNpc npc)
    {
        if (!Free && !ReferenceEquals(_claimant, npc))
            return false;
        _claimant = npc;
        return true;
    }

    public bool ClaimedBy(ArmedNpc npc) => ReferenceEquals(_claimant, npc) && !Carried && Gunner is null;

    /// <summary>They've arrived. False: it's gone.</summary>
    public bool Man(ArmedNpc npc)
    {
        if (!ClaimedBy(npc))
            return false;
        _claimant = null;
        Gunner = npc;
        return true;
    }

    /// <summary>A vehicle's gunner takes their place on it, whoever was there before (a respawn brings new people).</summary>
    public void TakeCrew(ArmedNpc npc)
    {
        _claimant = npc;
        Gunner = null;
    }

    /// <summary>They're off it, or not coming after all: it stays laid where it is.</summary>
    public void Release(ArmedNpc npc)
    {
        if (ReferenceEquals(_claimant, npc))
            _claimant = null;
        if (ReferenceEquals(Gunner, npc))
            Gunner = null;
    }

    /// <summary>A test lays it there at once.</summary>
    public void Lay(float yaw, float pitch)
    {
        Yaw = yaw;
        Pitch = pitch;
    }

    /// <summary>The gunner swings it towards a point, inside its limits. Returns how far off that point it still is (rad).</summary>
    public float TraverseTo(float3 at, float dt, float rate = TraverseRate)
    {
        var flatTo = at - Pivot;
        var bearing = float.Atan2(flatTo.X, flatTo.Z);
        var wantYaw = AllRound ? bearing : Facing + float.Clamp(WrapAngle(bearing - Facing), -ArcHalf, ArcHalf);
        var to = at - Cradle;
        var flat = float.Sqrt(to.X * to.X + to.Z * to.Z);
        var wantPitch = float.Clamp(float.Atan2(to.Y, flat), -PitchDown, PitchUp);
        var yawError = WrapAngle(wantYaw - Yaw);
        // Eased in and out a little: a heavy gun, and a man walking it round (or cranking the turret round).
        var step = float.Min(rate, float.Abs(yawError) * 6f + 0.05f) * dt;
        Yaw = WrapAngle(Yaw + float.Clamp(yawError, -step, step));
        Pitch += float.Clamp(wantPitch - Pitch, -ElevateRate * dt, ElevateRate * dt);
        var off = WrapAngle(bearing - Yaw);
        return float.Max(float.Abs(off), float.Abs(float.Atan2(to.Y, flat) - Pitch));
    }

    /// <summary>A round off the belt for the gunner. False: the belt's out.</summary>
    public bool TakeRound() => _gun.TakeRoundOnTripod();

    /// <summary>The gunner's new belt, from the tins in the crate (and the cover up meanwhile).</summary>
    public void NewBelt() => _gun.NewBelt();

    /// <summary>The kick of a shot, on the tripod.</summary>
    public void Kick()
    {
        _kick = 1f;
        _sinceShot = 0f;
    }

    /// <summary>
    /// A round has gone: what the gun does about it (smoke, the case out of the port). Returns whether this is one of the belt's
    /// tracers (one in four).
    /// </summary>
    public bool Fired(float3 muzzle, float3 velocity)
    {
        _gun.ShotOnTripod(muzzle, velocity, GunPose);
        return _tracerCount++ % 4 == 0;
    }

    /// <summary>The cover goes up for a while, for a new belt.</summary>
    public void LiftCover(float seconds) => _coverLeft = seconds;

    // ------------------------------------------------------------------------------------------------ the player

    /// <summary>
    /// Near the tripod, E takes the gun (true: Program lowers the gun in hand and brings this one up). With it in hand
    /// there, E puts it back (<paramref name="putBack"/>: Program lowers what's left in your hands and brings up the last gun).
    /// A vehicle's can't be taken: it only moves (and kicks) for its gunner.
    /// </summary>
    public bool Update(float dt, bool usePressed, float3 player, bool inHandReady, out bool putBack)
    {
        putBack = false;
        Prompt = null;
        _sinceShot += dt;
        _kick *= float.Exp(-22f * dt);
        var coverWanted = _coverLeft > 0f ? 0.85f : 0f;
        _coverLeft -= dt;
        _coverAngle += float.Clamp(coverWanted - _coverAngle, -3f * dt, 3f * dt);
        // With nobody holding the butt down, it sags.
        if (Gunner is null && !Carried && !SomeoneComing)
            Pitch += float.Clamp(Sag - Pitch, -0.5f * dt, 0.5f * dt);
        MoveArmour();
        if (Crewed)
            return false;

        var d = Cradle - player;
        if (d.X * d.X + d.Z * d.Z >= Reach * Reach || d.Y <= -1f || d.Y >= 2.5f)
            return false;
        if (Carried)
        {
            if (!inHandReady)
                return false;
            Prompt = "[E] Put the M249 back on its tripod";
            if (usePressed)
            {
                Carried = false;
                putBack = true;
                Yaw = Facing;
                Pitch = 0f;
                Log.Write("[GUN] Put the M249 back on its tripod");
            }
            return false;
        }

        if (Gunner is { Alive: true } manned)
        {
            Prompt = "The M249 is manned";
            if (usePressed)
                Log.Write($"[GUN] Can't take the M249: {manned.Name} is on it");
            return false;
        }

        Prompt = "[E] Take the M249: heavy, it slows you";
        if (!usePressed)
            return false;
        Carried = true;
        Gunner = null;
        if (_claimant is { } coming)
            Log.Write($"[GUN] Took the M249 before {coming.Name} got to it");
        _claimant = null;
        Log.Write($"[GUN] Took up the M249 ({Rounds} rounds)");
        return true;
    }

    private bool SomeoneComing => _claimant is { Alive: true };

    /// <summary>Starts with it taken (BRUTALFPS_WEAPON=m249).</summary>
    public void StartCarried() => Carried = true;

    /// <summary>A respawn: wherever it was, it's back on the tripod, level, with nobody on it and a full belt.</summary>
    public void Reset()
    {
        Carried = false;
        Gunner = _claimant = null;
        Yaw = Facing;
        Pitch = 0f;
        _kick = 0f;
        _coverLeft = _coverAngle = 0f;
        _gun.NewBelt();
        MoveArmour();
    }

    public void Draw(Renderer renderer, Zones zones)
    {
        if ((zones & Level.ZoneAt(Pivot)) == 0)
            return;
        if (_turret is not null)
        {
            var pose = TurretPose;
            foreach (var part in _turret.Parts)
                renderer.DrawPart(part, pose, float3.One);
        }

        if (!Carried)
            _gun.DrawInWorld(renderer, GunPose, _coverAngle, _sinceShot);
    }

    private static float WrapAngle(float a)
    {
        while (a > float.Pi) a -= float.Tau;
        while (a < -float.Pi) a += float.Tau;
        return a;
    }
}
