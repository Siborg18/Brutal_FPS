using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// A point on an NPC's route, how long it stops there (seconds, 0 = walks straight on) and, optionally, which way it
/// turns to face while it stands there (an angle about +Y, as <see cref="Npc.Heading"/>; NaN = keeps its heading).
/// </summary>
public readonly record struct Waypoint(float3 Position, float Pause = 0f, float Face = float.NaN);

/// <summary>Which part of a person a bullet hit (from the joint that moves that bit of skin most).</summary>
public enum BodyPart { Torso, Head, Arm, Leg }

/// <summary>A bullet wound, kept in the character's bind pose so it stays on the skin as they move.</summary>
public readonly record struct Wound(float3 BindPosition, float Radius, int Joint, float Seed, float Time, bool Exit);

/// <summary>
/// How much of the frame an NPC gets (<see cref="Npcs.TierOf"/>): the more is going on with them, and the nearer the
/// player, the more often they look round and are posed. See <see cref="Npc.Tier"/>.
/// </summary>
public enum NpcTier
{
    /// <summary>In a firefight, with the enemy in sight: they look round 10 times a second.</summary>
    Attacking,

    /// <summary>In a fight without the enemy in sight, or wary, startled, fleeing, cowering, on their way back: about 7.</summary>
    Searching,

    /// <summary>Calm, going about their business: twice a second.</summary>
    Idle,

    /// <summary>Calm and far from the player (over <see cref="Npcs.FarRange"/>): once every 2 s, and posed 10 times a second.</summary>
    Far,

    /// <summary>
    /// Calm, somewhere nobody can see (not the player's part of the level, nor one in view through a portal): they don't
    /// look round at all, only what they hear can rouse them, and they're updated <see cref="Npcs.DormantRate"/> times a second.
    /// </summary>
    Dormant,
}

/// <summary>What a person is doing about the danger they've noticed (or not).</summary>
public enum NpcState
{
    /// <summary>Walking their route, unaware.</summary>
    Calm,
    /// <summary>Heard something far off: stopped, looking towards it. Another scare and they panic.</summary>
    Wary,
    /// <summary>Just noticed a gun or a shot: frozen for a moment (reaction time), turning towards it.</summary>
    Startled,
    /// <summary>Running away from the threat, steering round whatever's in the way.</summary>
    Fleeing,
    /// <summary>Crouched, arms over the head (the Cower clip), until it's been quiet for a while.</summary>
    Cowering,
    /// <summary>Calmed down: walking back the way they fled, then on with their route.</summary>
    Returning,
    /// <summary>Armed and fighting (<see cref="ArmedNpc"/>): aiming at, shooting at or hunting for an enemy.</summary>
    Fighting,
    Dead,
}

/// <summary>How someone reacts to a gun: runs for it, or freezes and cowers where they are.</summary>
public enum Temperament { Bolter, Freezer }

/// <summary>What set someone off.</summary>
public enum Stimulus { SawGun, Gunshot, DistantShot, Shot, Panic, Noise, Flashbang }

/// <summary>What people can sense of the world each frame (filled in by <see cref="Npcs"/>).</summary>
public sealed class NpcSenses
{
    /// <summary>
    /// How much of the straight line from a to b is clear of the level (walls, props, portal water): 1 = nothing in the
    /// way, 0.3 = blocked 30% of the way along. Used for line of sight, muffled gunshots and steering.
    /// </summary>
    public Func<float3, float3, float> Clear = (_, _) => 1f;

    /// <summary>
    /// How much foliage is in the way from a to b (<see cref="FoliageCover.Depth"/>: density x metres, 0 = none). Leaves
    /// hide people but don't stop bullets, so this only goes into <see cref="Visibility"/>, never into <see cref="Clear"/>.
    /// </summary>
    public Func<float3, float3, float> Foliage = (_, _) => 0f;

    /// <summary>Foliage depth beyond which someone is hidden (about a metre of thick shrub).</summary>
    public const float Concealed = 1f;

    /// <summary>
    /// Whether, and how well, someone at <paramref name="eye"/> can make out <paramref name="at"/>: 0 = can't (a wall, or
    /// enough leaves in the way), 1 = in plain view, in between = glimpsed through thin leaves (slower to notice). Up
    /// close it takes more leaves to hide in: within 3 m the threshold rises. The leaves within arm's reach of the
    /// watcher's own eyes don't count (they peer through them: a guard standing in the undergrowth isn't blind).
    /// </summary>
    public float Visibility(float3 eye, float3 at, float clearNeeded = 0.99f)
    {
        if (Clear(eye, at) < clearNeeded)
            return 0f;
        // A smoke screen hides as leaves do: thick enough, nothing shows through it.
        var smoke = Smoke(eye, at);
        if (smoke >= SmokeConceals)
            return 0f;
        var thinned = 1f - smoke / SmokeConceals;
        var depth = LeavesBetween(eye, at, out var dist);
        if (depth <= 0f)
            return thinned;
        var near = float.Max(0f, 3f - dist);
        return depth >= Concealed + near * 0.6f ? 0f : float.Exp(-1.5f * depth) * thinned;
    }

    /// <summary>
    /// How thick the smoke screens are from a to b (0 = none, 1 = as thick as a fully built cloud's own radius;
    /// <see cref="Throwables.SmokeBetween"/>). It hides people (no one sees through it) but stops nothing.
    /// </summary>
    public Func<float3, float3, float> Smoke = (_, _) => 0f;

    /// <summary>Smoke this thick (<see cref="Smoke"/>) and nobody can make anything out through it.</summary>
    public const float SmokeConceals = 0.6f;

    /// <summary>Fills the list with the frags and flashbangs about (in flight or lying there), each frame (null: there are none).</summary>
    public Action<List<LiveGrenade>>? FindGrenades;

    /// <summary>The frags and flashbangs about this frame, for people to get clear of (Game/NpcDodge.cs).</summary>
    public readonly List<LiveGrenade> Grenades = [];

    /// <summary>The foliage depth from <paramref name="eye"/> to <paramref name="at"/>, but for the first <see cref="PeerThrough"/> m.</summary>
    public float LeavesBetween(float3 eye, float3 at, out float dist)
    {
        var d = at - eye;
        dist = float.Sqrt(float3.Dot(d, d));
        return dist > PeerThrough ? Foliage(eye + d * (PeerThrough / dist), at) : 0f;
    }

    private const float PeerThrough = 1.2f;   // m

    /// <summary>Why <paramref name="at"/> can't be seen from <paramref name="eye"/>: "in the leaves", or "out of sight" (a wall).</summary>
    public string HiddenBy(float3 eye, float3 at) => Clear(eye, at) < 0.99f ? "out of sight" : Smoke(eye, at) >= SmokeConceals ? "in the smoke" : LeavesBetween(eye, at, out _) > 0f ? "in the leaves" : "out of sight";

    /// <summary>How much light falls on a point, 0..1 (<see cref="Level.LightAt"/>; null, BRUTALFPS_NODARK=1: always plain daylight).</summary>
    public Func<float3, float>? LightAt;

    /// <summary>Where people can run in and hide (Rio's rooms behind open doors: <see cref="Level.RioHidingPlaces"/>; Game/NpcHiding.cs).</summary>
    public IReadOnlyList<HidingPlace> HidingPlaces = [];

    /// <summary>The M249s on mounts: the jungle nest's tripod, for the guards there, and the armoured car's turret, for its own gunner (Game/GunNest.cs).</summary>
    public readonly List<GunNest> Nests = [];

    /// <summary>Flares for those who carry them to light up the dark with (null: nobody throws any).</summary>
    public Flares? Flares;

    /// <summary>The light on the player at the last look (a few times a second): how far off they can be made out.</summary>
    public float PlayerLight = 1f;

    /// <summary>How far off the player can be made out in full light (m).</summary>
    public const float FullSight = 32f;

    /// <summary>
    /// Whether someone at the first point is looking from outside into a lit building at the second (<see cref="Level.LooksIntoHall"/>).
    /// Someone standing in the light in there stands out from further off: <see cref="IntoLightReach"/> times as far.
    /// </summary>
    public Func<float3, float3, bool>? IntoLight;

    private const float IntoLightReach = 3f;

    /// <summary>
    /// How much further off someone in a fight, scanning for the enemy they're fighting, makes the player out. It stretches
    /// the lit part of the range only, so the dark still hides the player about as well as ever. (Being put on the alert
    /// by a shout doesn't: they know there's trouble, and start looking, but their eyes are no better.)
    /// </summary>
    public const float AlertSightReach = 2f;

    /// <summary>The furthest the player could be made out from <paramref name="eye"/>, in the best of light (m).</summary>
    public float PlayerSightLimit(float3 eye, float alert = 1f) => (IntoLight?.Invoke(eye, PlayerEye) == true ? FullSight * IntoLightReach : FullSight) * alert;

    private const float SightInPitchDark = 3f;     // m: a shape right in front of you, even with no light on it at all
    private const float SightAlwaysWithin = 2f;    // m

    /// <summary>How far off the player can be made out in the light they're standing in (m): 32 by day, about 13 under the moon, 4 in a dark room at night.</summary>
    public float PlayerSightRange => PlayerSightRangeFor(1f);

    /// <summary><see cref="PlayerSightRange"/> for someone on the alert (<see cref="AlertSightReach"/>), or not (1).</summary>
    public float PlayerSightRangeFor(float alert) =>
        SightInPitchDark + (FullSight * alert - SightInPitchDark) * float.Pow(float.Clamp(PlayerLight, 0f, 1f), 0.75f);

    /// <summary>
    /// How well the player shows up at <paramref name="dist"/> m in the light they're in: 1 well inside
    /// <see cref="PlayerSightRange"/>, fading out over its last 15% (a shape that's hard to be sure of), 0 beyond it.
    /// </summary>
    public float PlayerLit(float dist, float reach = 1f, float alert = 1f)
    {
        if (dist <= SightAlwaysWithin)
            return 1f;
        var range = PlayerSightRangeFor(alert) * reach;
        return float.Clamp((range - dist) / (range * 0.15f), 0f, 1f);
    }

    /// <summary><see cref="Visibility"/> for the player: the walls and leaves, and the darkness too. Only the player is hidden by the dark, for now.</summary>
    public float SeePlayer(float3 eye, float3 at, float clearNeeded = 0.99f, float alert = 1f)
    {
        var seen = Visibility(eye, at, clearNeeded);
        return seen <= 0f ? 0f : seen * PlayerLit(float3.Distance(eye, at), IntoLight?.Invoke(eye, at) == true ? IntoLightReach : 1f, alert);
    }

    /// <summary>
    /// What each side knows together in each part of the level (<see cref="SquadIntel"/>): where the player was last seen,
    /// what's left to search, who's flanking. Going on the alert is no longer zone-wide: it's whoever hears someone in a
    /// firefight shout (<see cref="ArmedNpc.ShoutContact"/>).
    /// </summary>
    public readonly SquadBoard Squads = new();

    /// <summary>Everyone stands down and forgets what they knew (a respawn).</summary>
    public void ClearAlerts() => Squads.Clear();

    /// <summary>Someone calls something out (<see cref="ArmedNpc.Say"/>): who, which bark, and the line (null: nobody listens).</summary>
    public Action<Npc, Bark, string, int>? Spoke;

    /// <summary>
    /// Plans a frag thrown from the first point to come down at the second, at no more than the speed given: the velocity
    /// and where it'll go off (null: no way to get it there). Set by <see cref="Throwables"/>.
    /// </summary>
    public Func<float3, float3, float, (float3 Velocity, float3 Landing)?>? PlanFrag;

    /// <summary>An NPC throws a frag from a point with a velocity (null: nobody can).</summary>
    public Action<Npc, float3, float3>? ThrowFrag;

    /// <summary>How far off a muzzle flash catches the eye (m): further the darker it is around it.</summary>
    public float FlashSightRange => 120f - 80f * float.Clamp(PlayerLight, 0f, 1f);

    /// <summary>
    /// How many cover searches are left this frame (rays are not free, and one shot can send fifty people looking at once:
    /// the rest wait for the next frame). See <see cref="ArmedNpc"/>.
    /// </summary>
    public int CoverSearchesLeft;

    /// <summary>How they get about: paths and walls (null: straight lines and rays only, as in a level without collision).</summary>
    public NpcNav? Nav;

    /// <summary>How many path searches are left this frame (the rest walk on, or wait for the next frame).</summary>
    public int NavSearchesLeft;

    public float3 PlayerEye;

    /// <summary>The player is lying prone: a small, low target (shot at a wider cone, aimed at just under the eye rather than the chest).</summary>
    public bool PlayerProne;

    /// <summary>Where they aim at the player: the chest, or (prone) the head and shoulders, just above the ground.</summary>
    public float3 PlayerChest => PlayerEye - new float3(0, PlayerProne ? 0.1f : 0.35f, 0);
    public float3 PlayerForward = new(0, 0, -1);

    /// <summary>The player is holding the pistol (always, for now): seeing them is seeing a gun.</summary>
    public bool PlayerArmed = true;

    /// <summary>The player is getting into a factory raid (over the wall, in the back of a van): nobody can be an enemy to someone they can't know is there.</summary>
    public bool PlayerConcealed;

    /// <summary>
    /// A vehicle on the move (the van driving in on a factory raid): points along where it is and is about to be. Nobody
    /// walking their route steps within <see cref="VehicleClearance"/> of them; they wait for it to go by. Empty: there isn't one.
    /// </summary>
    public IReadOnlyList<float3> Vehicle = [];

    /// <summary>How far off a <see cref="Vehicle"/>'s line people keep: half its width, half theirs, and room to spare.</summary>
    public const float VehicleClearance = 2.2f;

    /// <summary>True if <paramref name="at"/> is where a <see cref="Vehicle"/> is or is about to be.</summary>
    public bool InVehiclesWay(float3 at)
    {
        foreach (var p in Vehicle)
        {
            var (dx, dz) = (p.X - at.X, p.Z - at.Z);
            if (dx * dx + dz * dz < VehicleClearance * VehicleClearance && float.Abs(p.Y - at.Y) < 2f)
                return true;
        }
        return false;
    }

    /// <summary>The player is alive (a dead player is nobody's target).</summary>
    public bool PlayerAlive = true;

    /// <summary>The player has shot a civilian or an officer: the police treat them as an enemy until they respawn.</summary>
    public bool PlayerWanted;

    /// <summary>An armed NPC pulls the trigger: (shooter, muzzle, bullet velocities: one, or a shotgun's pellets). Set by <see cref="Npcs"/>.</summary>
    public Action<Npc, float3, float3[]>? Fire;

    public List<Npc> People = [];

    /// <summary>The portals the player has walked into lately (oldest first; cleared on a respawn), for pursuers to follow.</summary>
    public List<PortalCrossing> PlayerCrossings = [];

    /// <summary>Seconds since the people were made (the clock <see cref="PortalCrossing.Time"/> is on).</summary>
    public float Time;

    /// <summary>The portals, for looking through (<see cref="ThroughPortal"/>) and searching.</summary>
    public List<Portal> Portals = [];

    /// <summary>
    /// Whether someone at <paramref name="eye"/> can make out <paramref name="at"/> through a portal (one level deep, as
    /// <see cref="Visibility"/> on both sides of the water), within <paramref name="range"/> m in all: the nearest way, if
    /// there are several.
    /// </summary>
    public bool ThroughPortal(float3 eye, float3 at, float range, out PortalSight sight, float clearNeeded = 0.99f)
    {
        sight = default;
        var found = false;
        foreach (var portal in Portals)
        {
            var d = portal.Center - eye;
            if (float3.Dot(d, d) > range * range || !portal.LineThrough(eye, at, out var entry, out var leaving, out var along, out var length) || length > range)
                continue;
            var seen = Visibility(eye, entry, clearNeeded);
            if (seen > 0f)
                seen *= Visibility(leaving + along * 0.05f, at, clearNeeded);   // (off the water: a freestanding portal's stops a line from either side)
            if (seen <= 0f)
                continue;
            sight = new PortalSight(portal, entry, along, length, seen, portal.Style.Freestanding && portal.SignedDistance(eye) < 0);
            range = length;
            found = true;
        }

        return found;
    }
}

/// <summary>The player walked into <paramref name="Entered"/> (and out of whatever it was linked to then) at <paramref name="Time"/>.</summary>
public readonly record struct PortalCrossing(Portal Entered, float Time);

/// <summary>
/// Someone seen through a portal: the line of sight reaches the <paramref name="Portal"/>'s water at <paramref name="Entry"/>
/// and arrives going along <paramref name="Along"/>, <paramref name="Distance"/> m from the eye in all (<paramref name="Seen"/>
/// as <see cref="NpcSenses.Visibility"/>). <paramref name="Window"/>: from behind a freestanding portal, a plain window
/// onto what's really on the far side, so stepping through wouldn't get there.
/// </summary>
public readonly record struct PortalSight(Portal Portal, float3 Entry, float3 Along, float Distance, float Seen, bool Window);

/// <summary>
/// One person walking a looping route: an <see cref="Animator"/> plays idle / walk / run at the speed they cover the
/// ground (so the feet don't slide), now and then an idle variant while they stand about, a shuffle when they turn on
/// the spot, and a smooth turn towards the next point. They wear an <see cref="Outfit"/>.
/// <para>
/// They have <see cref="Health"/>: bullets take a share by body part (a head shot kills) and wounds bleed it away; at
/// zero they fall (DeathFwd if shot from behind or bled out, DeathBack if shot from the front) and stay down. They
/// have senses: they see the player's gun (a field of view, line of sight, quicker the closer they are and the more
/// directly it's aimed at them), hear gunshots (muffled through walls), and see others running or lying dead. Scared,
/// a <see cref="Temperament.Bolter"/> runs away (Run) and a <see cref="Temperament.Freezer"/> cowers (Cower); when it's
/// been quiet for a while they walk back the way they came and carry on.
/// </para>
/// </summary>
public partial class Npc
{
    public const float BodyRadius = 0.26f;     // for bumping into the player
    public const float BodyHeight = 1.8f;
    public const float MaxHealth = 100f;
    public const float EyeHeight = 1.62f;
    private protected const float TurnRate = 2.6f;        // rad/s
    private protected const float FleeTurnRate = 5.5f;
    private protected const float BlendTime = 0.35f;      // s, idle <-> walk
    private protected const float VariantAfter = 4f;      // s standing still before an idle variant may play
    private protected const float VariantEvery = 5f;      // s between rolls
    private protected const float VariantChance = 0.25f;
    private protected const float ShuffleAngle = 0.6f;    // rad: turning on the spot by more than this plays ShuffleTurn
    private protected const float Arrive = 0.35f;         // m

    // Senses.
    private protected const float SenseEvery = 0.15f;     // s between looks round
    private protected const float SightRange = 32f;       // m
    private protected const float SightCos = 0.17f;       // field of view: about 80 degrees either side of straight ahead
    private protected const float FocusCos = 0.5f;        // inside 60 degrees they notice things much faster than at the edges
    private protected const float AimedCos = 0.985f;      // the gun pointed within about 10 degrees of them
    private protected const float PeerSight = 14f;        // m: someone running, or a body, this close sets them off too
    private protected const float CalmTime = 14f;         // s of nothing seen or heard before they calm down
    private protected const float WaryTime = 5f;
    private protected const float TooClose = 3f;          // m: a gun this close and even a bolter freezes
    private protected const float FriendNearby = 4f;      // m: trouble this near the player is the player's, to someone friendly to them

    // Fleeing.
    private protected const float FleeLook = 4.5f;        // m: how far ahead the steering looks
    private protected const float Leash = 24f;            // m from their route: they don't run off the edge of the level
    private protected const float SafeDistance = 26f;
    private protected const float MaxFleeTime = 16f;
    private protected const float BleedFade = 25f;        // s: a wound's bleeding eases off over about this long

    private protected readonly Waypoint[] _route;
    private protected readonly NodePose[] _pose;
    private protected readonly Animator _animator;
    private protected readonly ClipLibrary _clips;
    private protected readonly BoneMask _upperBody;
    private protected readonly Random _random;
    private protected readonly bool _hasVariants;
    private protected readonly float4x4[] _world;
    private protected readonly List<Wound> _wounds = [];
    private protected readonly List<float3> _trail = [];  // where they ran, to walk back along
    private protected int _next;
    private protected float _pauseLeft;
    private protected float _walkWeight;                  // 0 idle .. 1 walking (how far up to speed)
    private protected float _animSpeed;                   // m/s fed to the animator (eases to the real ground speed)
    private protected float _stillFor, _variantTimer;     // s standing about / since the last idle-variant roll
    private protected bool _shuffled;                     // ShuffleTurn already played for the turn in progress
    private protected string? _forcedClip;                // BRUTALFPS_NPCANIM: route frozen, this clip looped
    private protected float _staggerLeft;                 // stopped after being shot
    private protected float _faceShooter = float.NaN;
    private protected float _flinch, _flinchVelocity;     // spring (radians)
    private protected float3 _flinchAxis = float3.UnitX;  // model space
    private protected int[] _flinchNodes = [];
    private protected int _flinchPivot;
    private protected float3[]? _posed;                   // collision vertices posed for _posedFrame
    private protected int _posedFrame = -1;
    private protected readonly Skeleton _skeleton;
    private protected readonly string[] _variants;        // idle variants (clips tagged "variant")

    // Looking at things: the head (and a little of the neck) turns towards the threat, or a player close by.
    private const float LookYawMax = 1.15f;               // rad either way
    private const float LookPitchMax = 0.45f;
    private const float LookNear = 5.5f;                  // m: a calm person glances at a player this close in front of them
    private readonly int _neck, _head;
    private readonly int[] _neckNodes, _headNodes;
    private float _lookYaw, _lookPitch, _lookWeight;
    private bool _playerNear;

    // Aiming up and down: the animations hold the gun level, so the spine bends to bring it onto a target above or below (see AimPitch).
    private const float AimPitchMax = 1.1f;               // rad either way
    private readonly int _spineLow, _spineHigh;
    private readonly int[] _spineLowNodes, _spineHighNodes;
    private float _aimBend;                               // rad, eased
    private float3 _aimAxis = float3.UnitX;

    // Arms and walls (see KeepArmsOutOfWalls).
    private const float ArmCheckRange = 40f;              // m from the player: any further and nobody can tell
    private const float ArmTuckMax = 1.4f;                // rad
    private const float ArmSwingIn = 14f, ArmSwingOut = 4f; // rad/s: out of a wall at once, back up in their own time
    private const float ArmBackOffSpeed = 0.8f;           // m/s
    private readonly int _shoulderL, _shoulderR;
    private readonly int[] _armNodes, _armTips;
    private protected float _armTuck;                     // rad: how far the arms are swung down, out of a wall

    // Getting there (see GoTowards).
    private const float NavGoalMoved = 0.4f;              // m: the goal has moved this far: look at the way again
    private const float NavLookEvery = 0.3f;              // s between those looks, while the straight line is clear
    private const float NavSearchEvery = 1f;              // s between searches for a moving goal
    private const float NavStallAfter = 1f;               // s not getting anywhere before they look for another way
    private const float PersonalSpace = 0.65f;            // m between the middles of two people, walking
    private const float RouteGiveUp = 2.5f;               // s stopped short of a stop on their route before they go on to the next
    private readonly List<float3> _navPath = [];
    private NavLink? _navVia;                             // the ladder or flight of stairs at the end of the path, if any
    private int _navNext;
    private bool _navHave;
    private float3 _navGoal, _navFrom, _navLast;
    private float _navLookLeft, _navSkipLeft, _navStall;
    private float3 _moveCarry;

    // The ragdoll (with physics): made on the first frame after death from the last two drawn poses, so the body keeps
    // the way it was moving, and pushed by the bullet that killed it and any that hit it after.
    private Ragdoll? _ragdoll;
    private bool _ragdollWanted, _ragdollStill;
    private readonly List<(int Node, float3 Impulse, float3 Point)> _pushes = [];
    private readonly float4x4[] _prevWorld;
    private float4x4 _drawnTransform, _prevTransform;
    private float _prevDt;
    public const float KillPush = 26f;                    // N s: the killing shot's shove on the limb it hit
    public const float BodyPush = 14f;                    // N s: a shot into a body already down

    // Damage and fear.
    private protected float _bleed;                       // HP per second
    private protected float _limp;                        // 0..0.6: leg wounds slow them down
    private protected float _stateTime;
    private protected float _awareness;                   // 0..1: how sure they are they've seen a gun
    private protected float _senseTimer;
    private protected bool _seesThreat;
    private float3 _playerEye;            // where the player was at the last look
    private protected float _reactLeft;
    private protected float _calmLeft;
    private protected float _hearDelay = -1;              // a gunshot on its way to their ears
    private protected readonly HashSet<Npc> _seenDead = new(ReferenceEqualityComparer.Instance); // bodies already panicked over
    private protected float3 _heardAt;
    private protected Stimulus _heardKind;
    private protected float _playerShotDelay = -1;        // the player's gunshot on its way to an armed enemy's ears (see HeardPlayerShot)
    private float3 _playerShotAt;
    private Stimulus _playerShotKind;
    private float _friendShotDelay = -1;                  // a friend's gunshot on its way to their ears (see HeardFriendlyShot)
    private ArmedNpc? _friendShooter;
    private Stimulus _friendShotKind;
    private protected float _noiseDelay = -1;            // a noise on its way to their ears (see Noticed)
    private protected float3 _noiseAt;
    private protected bool _noiseByPlayer, _noiseLoud;
    private float _portalDelay = -1;                      // a portal coming on, about to catch their eye (see PortalCameOn)
    private Portal? _portalSeen;
    private protected float _blindLeft, _deafLeft;        // s: dazzled and deafened by a flashbang (see Flashbanged)
    private protected float3 _fleeDir;
    private protected float _fleeClear, _steerLeft, _cornered, _stuck;

    public float3 Position { get; private protected set; }

    /// <summary>The points of their route.</summary>
    public IReadOnlyList<Waypoint> Route => _route;

    /// <summary>Facing direction as an angle about +Y: the model (which faces +Z) is turned by this.</summary>
    public float Heading { get; private protected set; }

    public float3 SkinTone { get; }
    public Outfit Outfit { get; }
    /// <summary>The part of the level they're in: where their route is, unless they've followed someone through a portal.</summary>
    public Zones Zone { get; private set; }

    /// <summary>Where their route is.</summary>
    public Zones HomeZone { get; }

    /// <summary>Away from home through portals: each one they came out of (the way back), what it led from, and the trail they had before.</summary>
    private readonly Stack<(Portal Back, Portal Entered, List<float3> Trail)> _away = new();
    private bool _stranded;   // the way back was cut (a portal relinked): they stay where they are
    public string Name { get; }
    public int Hits { get; private set; }
    public List<Wound> Wounds => _wounds;

    public float Health { get; private set; } = MaxHealth;
    public NpcState State { get; private protected set; } = NpcState.Calm;
    public Temperament Temperament { get; set; }

    /// <summary>What they're made from: faction, equipment, aggression, temperament, accuracy.</summary>
    public NpcProfile Profile { get; }
    public Faction Faction => Profile.Faction;
    public Equipment Equipment => Profile.Equipment;
    public Aggression Aggression => Profile.Aggression;
    public bool Alive => State != NpcState.Dead;

    /// <summary>Where they think the danger is (on the ground).</summary>
    public float3 Threat { get; private protected set; }

    /// <summary>How high the danger is, as they took it in (<see cref="Threat"/> is at their own height, to turn towards).</summary>
    private protected float _threatRawY;

    /// <summary>Where they think the danger is, on whichever floor it's on.</summary>
    private protected float3 ThreatAt => Threat with { Y = _threatRawY };

    /// <summary>Dazzled by a flashbang: they can't see anything (and an armed one can't fight) until it wears off.</summary>
    public bool Blinded => _blindLeft > 0;

    /// <summary>Ears ringing from a flashbang: they hear nothing until it wears off.</summary>
    public bool Deafened => _deafLeft > 0;

    /// <summary>False (BRUTALFPS_NOSENSES=1): they don't notice anything, and only stop and turn when shot, as before.</summary>
    public bool SensesEnabled { get; set; } = true;

    /// <summary>Skinning matrices for the current pose.</summary>
    public float4x4[] Skin { get; }

    /// <summary>Model-space matrices of every node of the skeleton for the current pose.</summary>
    public ReadOnlySpan<float4x4> NodeWorld => _world;

    public float4x4 Transform => float4x4.CreateRotationY(Heading) * float4x4.CreateTranslation(Position);

    private protected float3 Ahead => new(float.Sin(Heading), 0, float.Cos(Heading));

    /// <summary>A script is moving them (see <see cref="ArmedNpc.ScriptStart"/>): no senses, no route, and nobody takes any notice of them.</summary>
    public bool Scripted { get; private protected set; }

    /// <summary>Not there yet: not drawn, can't be hit or bumped into.</summary>
    public bool Hidden { get; private protected set; }

    /// <summary>Night-vision goggles and a headset on their helmet (<see cref="NpcHeadGear"/>), with the pack and ear cups in this colour; null for none.</summary>
    public float3? HeadGear { get; set; }

    /// <summary>Their night-vision goggles are down over their eyes (night), not flipped up (day).</summary>
    public bool GogglesDown { get; set; }

    private protected float3? _scriptTo;
    private protected float _scriptSpeed;
    private protected float3? _scriptFace;

    /// <summary>A script takes them over, standing at <paramref name="at"/>: no senses, no route, nobody notices them.</summary>
    public void ScriptTake(float3 at, float heading)
    {
        Scripted = true;
        Position = at;
        Heading = heading;
        _link = null;
        _climbing = false;
        _scriptTo = _scriptFace = null;
    }

    /// <summary>Walks straight to <paramref name="to"/> at <paramref name="speed"/>, without turning.</summary>
    public void ScriptWalk(float3 to, float speed)
    {
        _scriptTo = to;
        _scriptSpeed = speed;
    }

    /// <summary>Turns towards <paramref name="at"/> and keeps facing it.</summary>
    public void ScriptFace(float3 at) => _scriptFace = at;

    /// <summary>Steps through <paramref name="portal"/> (it must be online), coming out in front of the portal it's linked to.</summary>
    public void ScriptGoThrough(Portal portal, NpcSenses s)
    {
        _scriptTo = _scriptFace = null;
        GoThrough(portal, s);
    }

    /// <summary>Not walking anywhere for the script (any more).</summary>
    public bool ScriptArrived => _scriptTo is null;

    /// <summary>A script needs them: bullets leave wounds but take no health and don't stop them, until it says otherwise.</summary>
    public bool KeepAlive { get; set; }

    /// <summary>
    /// Never dies (the NPC test area: Game/NpcsTestArea.cs): hits take health, bleed, flinch and alarm them as usual, but a
    /// hit (or bleeding) that would kill them puts them back to full health instead, counted in <see cref="Saves"/>.
    /// </summary>
    public bool Undying { get; set; }

    /// <summary>How many times <see cref="Undying"/> has kept them from dying.</summary>
    public int Saves { get; private set; }

    /// <summary>Health ran out on someone <see cref="Undying"/>: back to full, the bleeding stopped.</summary>
    private void Survive(string how)
    {
        Saves++;
        Log.Write($"[NPCTEST] {Name} would have died ({how}): back to full health ({Saves} so far)");
        Health = MaxHealth;
        _bleed = 0;
    }

    /// <summary>Where someone shooting at them aims, above their feet: the chest, lower if they're crouched down.</summary>
    public float AimHeight => State == NpcState.Cowering ? 0.65f : 1.3f;

    /// <summary>The script lets go of them: they carry on by themselves from where they are, and can be seen again.</summary>
    public void ScriptRelease()
    {
        Scripted = false;
        _scriptTo = _scriptFace = null;
    }

    /// <summary>Their route becomes one post: standing at <paramref name="at"/>, facing <paramref name="face"/>, for good (after a fight they come back here).</summary>
    public void Repost(float3 at, float face)
    {
        for (var i = 0; i < _route.Length; i++)
            _route[i] = new Waypoint(at, float.PositiveInfinity, face);
        _next = 0;
    }

    /// <summary>
    /// As <see cref="Repost"/>, and they set off for it now even if they were standing at their last post for good (someone
    /// following the player: <see cref="BoatTeam"/>). Whatever they're doing about a fight carries on first.
    /// </summary>
    public void MovePost(float3 at, float face)
    {
        Repost(at, face);
        _pauseLeft = 0f;
        _navHave = false;
    }

    /// <summary>
    /// For a script that walks them somewhere: whatever fright they were in is over. Up off the floor, no cower, startle or
    /// look-round playing over the walk, and calm (otherwise a cowering civilian is slid along crouched in the Cower clip).
    /// </summary>
    public void ScriptSettle()
    {
        if (!Alive)
            return;
        _animator.Stop("Cower", 0.35f);
        StopIdleClips();
        SetState(NpcState.Calm);
    }

    /// <summary>The script says they're dead, whatever the bullets did.</summary>
    public void ScriptKill(float3 direction)
    {
        if (!Alive)
            return;
        Die(direction, 1f);
        var node = _skeleton.NodeIndex("spine_02");
        Push(node, direction * KillPush, _world[node].Translation + Position);
    }

    /// <summary>One frame of being moved by a script. Returns whether they moved.</summary>
    private protected virtual bool UpdateScripted(float dt)
    {
        if (_scriptFace is { } face)
            FaceTowards(face, dt, 2.6f);
        if (_scriptTo is not { } to)
            return false;
        var way = to - Position;
        way.Y = 0;
        var left = float.Sqrt(float3.Dot(way, way));
        var step = _scriptSpeed * dt;
        if (left <= step)
        {
            Position = to;
            _scriptTo = null;
            return false;
        }
        var along = way / left * step;
        along.Y = (to.Y - Position.Y) * (step / left);   // (up or down a ramp or stairs, as they go)
        Position += along;
        return true;
    }

    /// <summary>Stops what they're doing to stare at something for a few seconds (it isn't a fright, and doesn't bring out anyone's gun).</summary>
    public void Watch(float3 at)
    {
        if (!Alive || Scripted || State is not (NpcState.Calm or NpcState.Wary or NpcState.Returning))
            return;
        at.Y = Position.Y;
        Threat = at;
        if (State == NpcState.Wary)
            _stateTime = 0;
        else
            SetState(NpcState.Wary, Stimulus.Noise);
    }

    private bool _talking;
    private float _talkLeft;

    /// <summary>Someone who <see cref="NpcProfile.Talks"/>: their turn to talk comes and goes while they're calm.</summary>
    private void Chat(float dt)
    {
        if (!Profile.Talks)
            return;
        if (!Alive || State != NpcState.Calm || Scripted || _sitting || HandsOnMount)
        {
            if (_talking)
                _animator.Stop("Talk", 0.2f);
            _talking = false;
            return;
        }

        _talkLeft -= dt;
        if (_talkLeft > 0)
            return;
        _talking = !_talking && _clips.Get("Talk") is not null;
        if (_talking)
            _animator.PlayOverlay("Talk", _upperBody, 0.35f, 0.4f);
        else
            _animator.Stop("Talk", 0.5f);
        _talkLeft = _talking ? 2.5f + 3.5f * (float)_random.NextDouble() : 1.5f + 6f * (float)_random.NextDouble();
    }

    public Npc(string name, Skeleton skeleton, ClipLibrary clips, Outfit outfit, Waypoint[] route, float3 skinTone, float startHeading, float phase,
        NpcProfile profile)
    {
        Name = name;
        Outfit = outfit;
        Profile = profile;
        Temperament = profile.Temperament;
        _clips = clips;
        _skeleton = skeleton;
        _animator = new Animator(skeleton, clips);
        _upperBody = BoneMask.Subtree(skeleton, "spine_02");
        _random = new Random(StableSeed(name));
        _variants = [.. clips.WithTag("variant").Select(c => c.Name).Order(StringComparer.Ordinal)];
        _hasVariants = _variants.Length > 0;
        _neck = skeleton.NodeIndex("neck_01");
        _head = skeleton.NodeIndex("head");
        _neckNodes = _neck >= 0 ? skeleton.Subtree(_neck) : [];
        _headNodes = _head >= 0 ? skeleton.Subtree(_head) : [];
        _spineLow = skeleton.NodeIndex("spine_02");
        _spineHigh = skeleton.NodeIndex("spine_03");
        _spineLowNodes = _spineLow >= 0 ? skeleton.Subtree(_spineLow) : [];
        _spineHighNodes = _spineHigh >= 0 ? skeleton.Subtree(_spineHigh) : [];
        _shoulderL = skeleton.NodeIndex("upperarm.L");
        _shoulderR = skeleton.NodeIndex("upperarm.R");
        _armNodes = _shoulderL >= 0 && _shoulderR >= 0 ? [.. skeleton.Subtree(_shoulderL), .. skeleton.Subtree(_shoulderR)] : [];
        _armTips = [.. new[] { "forearm.L", "forearm.R", "hand.L", "hand.R", "middle_01.L", "middle_01.R" }.Select(skeleton.NodeIndex).Where(n => n >= 0)];
        _prevWorld = new float4x4[skeleton.NodeCount];
        _route = route;
        Position = route[0].Position;
        Heading = startHeading;
        SkinTone = skinTone;
        Zone = HomeZone = Level.ZoneAt(Position);
        _pose = new NodePose[skeleton.NodeCount];
        _world = new float4x4[skeleton.NodeCount];
        Skin = new float4x4[skeleton.JointCount];
        // Start part-way through the first stop (or walking on to the second point if the first has none).
        _pauseLeft = route.Length > 1 ? route[0].Pause * (1f - phase) : float.PositiveInfinity;
        _next = _pauseLeft > 0 ? 0 : 1 % route.Length;
        _walkWeight = _pauseLeft > 0 ? 0 : 1;
        _senseTimer = phase * SenseEvery;       // so everyone doesn't look round on the same frame
        var walkDuration = clips.Get("Walk")?.Duration ?? 1f;
        _animator.Desync(phase * 7.3f / walkDuration, phase * 11.1f);
        _drawnTransform = _prevTransform = Transform;
    }

    private const float WomansHeight = 0.95f;

    // How wide each bone ends up across the body. A bone's scale carries on down to its children, so each is given relative to its parent.
    private static readonly (string Node, string? Parent, float Width)[] WomansWidths =
    [
        ("pelvis", null, 1.12f), ("thigh.L", "pelvis", 1f), ("thigh.R", "pelvis", 1f), ("spine_01", "pelvis", 0.92f),
        ("spine_02", "spine_01", 0.87f), ("spine_03", "spine_02", 0.90f), ("neck_01", "spine_03", 0.94f),
    ];

    private (int Node, float Across)[]? _womansFigure;
    private int _womansPelvis = -1;

    /// <summary>The body and its clothes all follow the skeleton: smaller overall, a narrower waist and shoulders, wider hips.</summary>
    private void ApplyWomansFigure(Skeleton skeleton)
    {
        if (_womansFigure is null)
        {
            float Width(string? node) => node is null ? 1f : WomansWidths.First(w => w.Node == node).Width;
            _womansPelvis = skeleton.NodeIndex("pelvis");
            _womansFigure = [.. WomansWidths.Select(w => (skeleton.NodeIndex(w.Node), w.Width / Width(w.Parent))).Where(f => f.Item1 >= 0)];
        }

        foreach (var (node, across) in _womansFigure)
            _pose[node].S.X *= across;
        if (_womansPelvis >= 0)
        {
            _pose[_womansPelvis].S *= WomansHeight;
            _pose[_womansPelvis].T *= WomansHeight;
        }
    }

    /// <summary>Lying as a ragdoll (rather than in a death clip's last pose).</summary>
    public bool Ragdolled => _ragdoll is not null;

    /// <summary>Takes the ragdoll out of the physics world (before the person is thrown away, as on a respawn).</summary>
    public void ReleaseRagdoll()
    {
        _ragdoll?.Dispose();
        _ragdoll = null;
        _ragdollWanted = false;
    }

    /// <summary>Test override: freeze the route and play this clip, looped, in place (locomotion clips at their own speed).</summary>
    public void ForceClip(string clip) => _forcedClip = clip;

    internal static int StableSeed(string s)
    {
        var h = 2166136261u;
        foreach (var c in s)
            h = (h ^ c) * 16777619u;
        return (int)h;
    }

    /// <param name="blocker">Where the player stands: people wait rather than walk into them.</param>
    /// <param name="ragdolls">The physics world and the body layout, for falling limp when killed (null: death clips instead).</param>
    private bool SearchNav(NpcNav nav, float3 goal)
    {
        bool found;
        using (Perf.NavSearch.Time(Name))
            found = nav.FindPath(Position, goal, _navPath, out _navVia, $"{Name} ({State})");
        return found;
    }

    public void Update(float dt, SkinnedModel model, float walkSpeed, float runSpeed, float3 blocker, NpcSenses senses,
        (PhysicsWorld World, RagdollRig Rig)? ragdolls = null)
    {
        if (!Alive && (_ragdoll is not null || (_ragdollWanted && ragdolls is not null)))
        {
            UpdateRagdoll(model.Skeleton, ragdolls);
            return;
        }

        var before = Position;
        _stateTime += dt;
        _staggerLeft -= dt;
        _deafLeft -= dt;
        if (_blindLeft > 0)
        {
            _blindLeft -= dt;
            if (_blindLeft <= 0 && Alive)
            {
                if (State != NpcState.Cowering)
                    _animator.Stop("Cower", 0.5f);
                OnBlinded(false);
                Log.Write($"[NPC] {Name}: can see again ({State.ToString().ToLowerInvariant()})");
            }
        }

        var t0 = FrameStats.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        if (Alive)
        {
            Bleed(dt);
            if (Alive && SensesEnabled && _forcedClip is null && !Scripted)
                Sense(dt, senses);
        }
        var t1 = FrameStats.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        var moving = false;
        if (Alive && _risingLeft > 0f)
            moving = false;   // (getting up out of their chair: Game/NpcSeat.cs)
        else if (Alive && !Scripted && _link is null && _forcedClip is null && CrewHold(dt, senses, out var crewMoving))
            moving = crewMoving;
        else if (Alive && !Scripted && _link is null && _forcedClip is null && !_sitting && Dodge(dt, runSpeed, senses, out var dodged))
            moving = dodged;
        else if (Scripted && Alive)
            moving = UpdateScripted(dt);
        else if (_link is not null && Alive)
            moving = UpdateLink(dt, senses);
        else switch (State)
        {
            case NpcState.Calm:
                // Through a portal, away from their route: head back first (or, stranded, just stand there).
                if (_away.Count > 0)
                    SetState(NpcState.Returning);
                else if (!_stranded && !_sitting)
                    moving = UpdateRoute(dt, walkSpeed, blocker, senses);
                break;
            case NpcState.Wary:
                if (!_sitting)
                    FaceTowards(Threat, dt, 0.8f);
                if (_stateTime > WaryTime)
                    SetState(NpcState.Calm);
                break;
            case NpcState.Startled:
                FaceTowards(Threat, dt, 1.4f);
                _reactLeft -= dt;
                if (_reactLeft <= 0 && _staggerLeft <= 0)
                    Decide(senses);
                break;
            case NpcState.Fleeing:
                moving = UpdateFlee(dt, runSpeed, senses);
                break;
            case NpcState.Cowering:
                FaceTowards(Threat, dt, 0.4f);
                if (!_seesThreat)
                    _calmLeft -= dt;
                if (_calmLeft <= 0 && !RegroupDone(senses))
                    SetState(NpcState.Returning);
                break;
            case NpcState.Returning:
                moving = UpdateReturn(dt, walkSpeed, senses);
                break;
            case NpcState.Fighting:
                moving = UpdateFight(dt, walkSpeed, runSpeed, senses);
                break;
        }
        UpdateSeat(dt, moving);

        var t2 = FrameStats.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        _walkWeight = float.Clamp(_walkWeight + (moving ? dt : -dt) / BlendTime, 0f, 1f);

        // Animation: the speed actually covered, eased so stopping blends into the idle rather than snapping.
        var moved = Position - before;
        moved.Y = 0;
        var groundSpeed = dt > 0 ? float.Sqrt(float3.Dot(moved, moved)) / dt : 0f;
        var step = runSpeed / BlendTime * dt;
        _animSpeed += float.Clamp(groundSpeed - _animSpeed, -step, step);
        if (_forcedClip is not null)
        {
            PlayForced();
        }
        else
        {
            _animator.SetLocomotion(Alive ? _animSpeed : 0f);
            IdleVariants(dt, State != NpcState.Calm || HandsOnMount || _sitting || moving || _staggerLeft > 0 || _animSpeed > 0.05f || Scripted);
            Chat(dt);
        }

        // Flinch: a damped spring, kicked by each hit.
        _flinchVelocity += (-260f * _flinch - 20f * _flinchVelocity) * dt;
        _flinch += _flinchVelocity * dt;

        // Pose: from the animator, as often as the tier says (AnimInterval: every frame up close and in view, less often
        // further off or out of sight, with the time saved up so the clips keep their pace). Between poses they're drawn
        // in the last one, where they are now. (The last one is kept: a ragdoll takes its velocities from the last two.)
        _poseWait += dt;
        if (_poseWait < AnimInterval && Alive && _posedOnce)
        {
            AfterPose();   // (the gun goes along with them, held as last posed)
            if (FrameStats.Enabled)
            {
                ProfSense += t1 - t0;
                ProfBehave += System.Diagnostics.Stopwatch.GetTimestamp() - t1;
                PosesSkipped++;
            }

            return;
        }

        var poseDt = _poseWait;
        _poseWait = 0f;
        _posedOnce = true;
        var skeleton = model.Skeleton;
        _world.CopyTo(_prevWorld, 0);
        _prevTransform = _drawnTransform;
        _prevDt = poseDt;
        var poseTimer = Perf.Pose.Time();
        _animator.Update(poseDt, _pose);
        if (Profile.Woman)
            ApplyWomansFigure(skeleton);

        skeleton.ComputeWorld(_pose, _world);
        poseTimer.Dispose();
        if (float.Abs(_flinch) > 1e-4f && _flinchNodes.Length > 0)
        {
            // Tip the hit part (and everything it carries) away from the bullet, about its joint.
            var pivot = _world[_flinchPivot].Translation;
            var turn = float4x4.CreateTranslation(-pivot) * float4x4.CreateFromAxisAngle(_flinchAxis, _flinch) * float4x4.CreateTranslation(pivot);
            foreach (var n in _flinchNodes)
                _world[n] = _world[n] * turn;
        }

        var t3 = FrameStats.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        Look(poseDt, senses);
        BendToAim(poseDt);
        if (Alive && !HandsOnMount && !SeatHolds)
            using (Perf.ArmsInWalls.Time()) KeepArmsOutOfWalls(poseDt, moving, senses);
        else if (Alive && HandsOnMount)
            PoseOnMount();
        else
            _armTuck = 0f;   // (at the card table: the arms are over it)
        var t4 = FrameStats.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        using (Perf.Pose.Time()) skeleton.ComputeSkin(_world, Skin);
        _drawnTransform = Transform;
        AfterPose();
        if (FrameStats.Enabled)
        {
            var t5 = System.Diagnostics.Stopwatch.GetTimestamp();
            ProfSense += t1 - t0;
            ProfBehave += t2 - t1;
            ProfAnimate += t3 - t2 + (t5 - t4);
            ProfArms += t4 - t3;
            PosesDone++;
        }
    }

    /// <summary>BRUTALFPS_PERF=1: Stopwatch ticks spent in each part of <see cref="Update"/> (senses, behaviour, animation, arms/look), for the stress test's log.</summary>
    internal static long ProfSense, ProfBehave, ProfAnimate, ProfArms;

    /// <summary>BRUTALFPS_PERF=1: poses worked out, and frames a pose was skipped by the tier (for the stress test's log).</summary>
    internal static int PosesDone, PosesSkipped;

    // ------------------------------------------------------------------------------------------------ update tiers

    /// <summary>
    /// How much attention they get (<see cref="Npcs.TierOf"/>, each frame): how often they look round
    /// (<see cref="SenseInterval"/>) and, with how far off and in view they are, how often they're posed
    /// (<see cref="AnimInterval"/>). A <see cref="NpcTier.Dormant"/> one is only updated a few times a second at all.
    /// </summary>
    public NpcTier Tier { get; private set; } = NpcTier.Searching;

    /// <summary>Seconds between poses (0: every frame). Set with the tier.</summary>
    public float AnimInterval { get; private set; }

    /// <summary>Frame (<see cref="Npcs"/>' count) they were last drawn in, in any view.</summary>
    internal int DrawnFrame = int.MinValue;

    /// <summary>Time saved up while <see cref="NpcTier.Dormant"/>, for their next update (<see cref="Npcs.Update"/>).</summary>
    internal float DormantWait;

    /// <summary>Updated at least once (so posed): until then they're updated wherever they are (<see cref="Npcs.ActiveZones"/>).</summary>
    internal bool UpdatedOnce;

    /// <summary>s left that they're updated though their part of the level is shut down (just shot: they fall, wherever they are).</summary>
    internal float AwakeFor;

    private float _poseWait;     // s since the last pose
    private bool _posedOnce;      // (the first update always poses them)
    private float _sinceLook;     // s since they last looked round (Sense)

    /// <summary>Within <see cref="Npcs.NearRange"/> of the camera: calm, they still look round as often as someone searching.</summary>
    public bool Near { get; private set; }

    public void SetTier(NpcTier tier, float animInterval, bool near = false)
    {
        if (tier != Tier || near != Near)
        {
            // Quicker now: don't wait out the slow tier's look.
            _senseTimer = float.Min(_senseTimer, SenseInterval(tier, near));
            Tier = tier;
            Near = near;
        }

        // Quicker now (in view, or come close): pose at once rather than wait out the old interval.
        if (animInterval < AnimInterval)
            _poseWait = float.Max(_poseWait, animInterval);
        AnimInterval = animInterval;
    }

    /// <summary>
    /// Seconds between looks round for a tier: on the attack, 10 a second; searching, wary or running, about 7 (as it
    /// always was); calm, 2 (but about 7 within <see cref="Npcs.NearRange"/> of the player, so someone close by reacts as quickly
    /// as ever); calm and far off, one every 2 s; dormant, never (only what they hear wakes them: see
    /// <see cref="Sense"/>, which still takes in sounds every update).
    /// </summary>
    private static float SenseInterval(NpcTier tier, bool near) => tier switch
    {
        NpcTier.Attacking => 0.1f,
        NpcTier.Searching => SenseEvery,
        NpcTier.Idle => near ? SenseEvery : 0.5f,
        NpcTier.Far => 2f,
        _ => float.PositiveInfinity,
    };

    // ------------------------------------------------------------------------------------------------ arms and walls

    /// <summary>The far end of what they're holding, in model space (null: nothing in their hands).</summary>
    private protected virtual float3? HeldTip() => null;

    /// <summary>
    /// The animations know nothing of walls: an arm held out (a gun above all) can go through one. Each frame, near the
    /// player, the line out from the chest to each forearm, hand and the gun's muzzle is checked against the level, in the
    /// pose as animated. What goes through a wall in front of them is brought back by swinging both arms down about the
    /// line through the shoulders, which keeps a two-handed grip on the gun (low ready), by just as far as it takes;
    /// and if they aren't walking anywhere, they step back off the wall till there's room. A gun held that low doesn't
    /// fire: see <see cref="ArmedNpc"/>.
    /// </summary>
    private void KeepArmsOutOfWalls(float dt, bool moving, NpcSenses s)
    {
        if (s.Nav is not { } nav || _armNodes.Length == 0)
            return;
        if (_climbing)
        {
            _armTuck = 0f;   // (the hands are on the rungs)
            return;
        }

        var wanted = 0f;
        var pushBy = 0f;
        var pushAlong = float3.Zero;
        var offset = Position - s.PlayerEye;
        if (float3.Dot(offset, offset) < ArmCheckRange * ArmCheckRange && !Hidden && _forcedClip is null)
        {
            var transform = Transform;
            var shoulderL = _world[_shoulderL].Translation;
            var shoulderR = _world[_shoulderR].Translation;
            var chest = float3.Transform((shoulderL + shoulderR) * 0.5f, transform);
            void Check(float3 tip)
            {
                var end = float3.Transform(tip, transform);
                var d = end - chest;
                var length = float.Sqrt(float3.Dot(d, d));
                if (!nav.Reach(chest, end, Position.Y, out var distance, out var normal))
                    return;
                var through = length - distance;
                normal.Y = 0;
                if (through <= 0f)
                    return;
                if (float3.Dot(normal, normal) > 0.25f && through > pushBy)
                {
                    pushBy = through;
                    pushAlong = normal;
                }

                // How far to swing an arm of this reach for its end to come back that far. Only for what's in front of
                // them: swinging an arm held out to the side wouldn't bring it in.
                var shoulder = (shoulderL + shoulderR) * 0.5f;
                var reach = tip - shoulder;
                var reachLength = float.Sqrt(float3.Dot(reach, reach));
                if (reach.Z > reachLength * 0.4f)
                    wanted = float.Max(wanted, float.Acos(float.Clamp(1f - through / reachLength, 0f, 1f)));
            }

            foreach (var node in _armTips)
                Check(_world[node].Translation);
            if (HeldTip() is { } held)
                Check(held);
        }

        var target = float.Min(wanted * 1.1f, ArmTuckMax);
        _armTuck += float.Clamp(target - _armTuck, -ArmSwingOut * dt, ArmSwingIn * dt);
        if (_armTuck > 1e-3f)
        {
            var pivot = _world[_shoulderR].Translation;
            var axis = float3.Normalize(_world[_shoulderL].Translation - pivot);
            var turn = float4x4.CreateFromAxisAngle(axis, _armTuck);
            if (float3.Transform(float3.UnitZ, turn).Y > 0f)
                turn = float4x4.CreateFromAxisAngle(axis, -_armTuck);
            turn = float4x4.CreateTranslation(-pivot) * turn * float4x4.CreateTranslation(pivot);
            foreach (var n in _armNodes)
                _world[n] = _world[n] * turn;
        }

        if (pushBy > (wanted > 0f ? 0f : 0.08f) && !moving && _walkWeight < 0.1f && !Scripted && State != NpcState.Cowering && _staggerLeft <= 0)
            Position = nav.Slide(Position, pushAlong * float.Min(pushBy, ArmBackOffSpeed * dt));

        if (!NavDebug || float3.Dot(offset, offset) >= ArmCheckRange * ArmCheckRange || Hidden)
            return;
        // Debug: how's that working? (Logged by Npcs.Update.)
        DebugPoses++;
        if (wanted > 0f)
            DebugSwung++;
        var after = Transform;
        var from = float3.Transform((_world[_shoulderL].Translation + _world[_shoulderR].Translation) * 0.5f, after);
        var worst = 0f;
        foreach (var node in _armTips)
            worst = float.Max(worst, Through(_world[node].Translation));
        if (HeldTip() is { } tip2)
            worst = float.Max(worst, Through(tip2));
        if (worst > 0.03f)
        {
            DebugThrough++;
            if (worst > 0.1f)
                DebugDeep++;
            if (worst > DebugWorst)
            {
                DebugWorst = worst;
                DebugWorstWhat = $"{Name}, {State}{(moving ? ", moving" : "")}, tuck {_armTuck:0.00} of {wanted:0.00}, at ({Position.X:0.0}, {Position.Z:0.0}) facing {Heading:0.00}";
            }
        }

        float Through(float3 tip)
        {
            var end = float3.Transform(tip, after);
            var d = end - from;
            return nav.Reach(from, end, Position.Y, out var distance, out _) ? float.Sqrt(float3.Dot(d, d)) - distance : 0f;
        }
    }

    /// <summary>Debug: called off, whatever they were doing.</summary>
    public void DebugGoHome()
    {
        SensesEnabled = false;   // (or they'd only see the player again)
        if (Alive && !Scripted && State is not (NpcState.Calm or NpcState.Returning))
            SetState(NpcState.Returning);
    }

    /// <summary>BRUTALFPS_NAVCHECK=1: log what getting about comes to (<see cref="Npcs.CheckNav"/>).</summary>
    public static readonly bool NavDebug = Environment.GetEnvironmentVariable("BRUTALFPS_NAVCHECK") == "1";
    public static int DebugPoses, DebugSwung, DebugThrough, DebugDeep, DebugStalls;
    public static float DebugWorst;
    public static string DebugWorstWhat = "";

    // ------------------------------------------------------------------------------------------------ ragdoll

    /// <summary>Dead with physics: the limbs are bodies in the physics world, and the skeleton follows them.</summary>
    private void UpdateRagdoll(Skeleton skeleton, (PhysicsWorld World, RagdollRig Rig)? ragdolls)
    {
        if (_ragdoll is null)
        {
            var (world, rig) = ragdolls!.Value;
            using (Perf.RagdollStart.Time(Name)) _ragdoll = new Ragdoll(world, rig, _world, _drawnTransform, _prevWorld, _prevTransform, _prevDt);
            _ragdollWanted = false;
            _ragdollStill = false;
            _flinch = _flinchVelocity = 0;
        }

        foreach (var (node, impulse, point) in _pushes)
        {
            _ragdoll.Push(node, impulse, point);
            _ragdollStill = false;
        }

        _pushes.Clear();
        if (_ragdollStill)
            return;   // lying still: the pose hasn't changed

        // They're where the body is now (on the ground they fell to), still facing the way they did.
        var c = _ragdoll.Center;
        Position = new float3(c.X, float.Min(Position.Y, c.Y - 0.12f), c.Z);
        _ragdoll.Pose(_pose, _world, Transform);
        using (Perf.Pose.Time()) skeleton.ComputeSkin(_world, Skin);
        _drawnTransform = Transform;
        _posedFrame = -1;
        _ragdollStill = _ragdoll.Asleep;
        AfterPose();
    }

    // ------------------------------------------------------------------------------------------------ looking

    /// <summary>
    /// Turns the head (60%) and neck (40%) towards what they're paying attention to: the threat while wary, startled or
    /// cowering, a player standing close in front of them while they go about their business. Eased in and out.
    /// </summary>
    private void Look(float dt, NpcSenses s)
    {
        if (_neck < 0 || _head < 0)
            return;
        float3? target = null;
        if (Alive && _forcedClip is null && !Scripted)
        {
            if (State is NpcState.Wary or NpcState.Startled or NpcState.Cowering)
                target = Threat + new float3(0, 1.5f, 0);
            else if (State is NpcState.Calm or NpcState.Returning && _playerNear)
                target = s.PlayerEye;
        }

        var want = 0f;
        if (target is { } t)
        {
            float4x4.Invert(Transform, out var toModel);
            var d = float3.Transform(t, toModel) - _world[_head].Translation;
            var flat = float.Sqrt(d.X * d.X + d.Z * d.Z);
            var yaw = float.Atan2(d.X, d.Z);
            if (float.Abs(yaw) < 1.8f && flat > 0.2f)
            {
                want = 1f;
                var pitch = float.Atan2(d.Y, flat);
                var k = 1f - float.Exp(-7f * dt);
                _lookYaw += (float.Clamp(yaw, -LookYawMax, LookYawMax) - _lookYaw) * k;
                _lookPitch += (float.Clamp(pitch, -LookPitchMax, LookPitchMax) - _lookPitch) * k;
            }
        }

        _lookWeight += float.Clamp(want - _lookWeight, -2.5f * dt, 2.5f * dt);
        if (_lookWeight < 1e-3f)
            return;
        Turn(_neckNodes, _neck, 0.4f * _lookWeight);
        Turn(_headNodes, _head, 0.6f * _lookWeight);

        void Turn(int[] nodes, int pivotNode, float share)
        {
            var pivot = _world[pivotNode].Translation;
            var turn = float4x4.CreateTranslation(-pivot) * float4x4.CreateRotationX(-_lookPitch * share) * float4x4.CreateRotationY(_lookYaw * share) *
                       float4x4.CreateTranslation(pivot);
            foreach (var n in nodes)
                _world[n] = _world[n] * turn;
        }
    }

    /// <summary>
    /// Bends the upper body (half at spine_02, half at spine_03, arms, gun and head with it) by what <see cref="AimPitch"/>
    /// asks for, about a level axis across the way the gun points. Eased in and out.
    /// </summary>
    private void BendToAim(float dt)
    {
        if (_spineLow < 0 || _spineHigh < 0)
            return;
        var want = 0f;
        if (Alive && _forcedClip is null && AimPitch() is { } aim)
        {
            want = float.Clamp(aim.Angle, -AimPitchMax, AimPitchMax);
            _aimAxis = aim.Axis;
        }

        _aimBend += (want - _aimBend) * (1f - float.Exp(-10f * dt));
        if (float.Abs(_aimBend) < 1e-3f)
            return;
        Bend(_spineLowNodes, _spineLow);
        Bend(_spineHighNodes, _spineHigh);

        void Bend(int[] nodes, int pivotNode)
        {
            var pivot = _world[pivotNode].Translation;
            var turn = float4x4.CreateTranslation(-pivot) * float4x4.CreateFromAxisAngle(_aimAxis, _aimBend * 0.5f) * float4x4.CreateTranslation(pivot);
            foreach (var n in nodes)
                _world[n] = _world[n] * turn;
        }
    }

    /// <summary>
    /// How far to bend the upper body (model space, before the arms are kept out of walls) to aim at something above or
    /// below: about <c>Axis</c> (level), by <c>Angle</c> rad, positive raising whatever points along the gun. Null: no bend.
    /// </summary>
    private protected virtual (float3 Axis, float Angle)? AimPitch() => null;

    // ------------------------------------------------------------------------------------------------ hooks for armed NPCs
    // A plain Npc is a bystander: these do nothing. ArmedNpc overrides them to fight.

    /// <summary>One frame of <see cref="NpcState.Fighting"/>. Returns whether they moved.</summary>
    private protected virtual bool UpdateFight(float dt, float walkSpeed, float runSpeed, NpcSenses s) => false;

    /// <summary>After a scare (<see cref="Decide"/>): fight instead of running or cowering?</summary>
    private protected virtual bool WantsToFight(NpcSenses s) => false;

    /// <summary>Whether seeing the player's gun frightens them (an officer isn't scared of a gun; a gunman wants a fight).</summary>
    private protected virtual bool NoticesPlayerGun(NpcSenses s) => true;

    /// <summary>
    /// On the alert for the player (someone of their side in a firefight shouted to them: <see cref="ArmedNpc.HearShout"/>):
    /// they know them at once for the enemy when they see them (but see no further than usual).
    /// </summary>
    private protected virtual bool AlertForPlayer(NpcSenses s) => false;

    /// <summary>
    /// How many times <see cref="NpcSenses.FullSight"/> they can make the player out from, on the alert or not (the light
    /// on the player still decides how much of that counts). Armed people look out as far as their gun reaches.
    /// </summary>
    private protected virtual float SightReach(bool alert) => alert ? NpcSenses.AlertSightReach : 1f;

    /// <summary>Whether they have an answer of their own to the player's gunshots (<see cref="HeardPlayerShot"/>) rather than just being scared by them.</summary>
    private protected virtual bool ReactsToPlayerShots => false;

    /// <summary>The player's gunshot has just reached them.</summary>
    private protected virtual void HeardPlayerShot(float3 at, Stimulus kind, NpcSenses s) => Alarm(at, kind);

    /// <summary>
    /// Whether they know this person for one of their own: their gunfire and stray rounds aren't taken for the enemy's. A
    /// bystander doesn't: anyone's gun is a gun (<see cref="ArmedNpc"/> says otherwise).
    /// </summary>
    public virtual bool Recognises(Npc other) => false;

    /// <summary>A shot from someone they recognise has just reached them (<see cref="Recognises"/>).</summary>
    private protected virtual void HeardFriendlyShot(ArmedNpc shooter, Stimulus kind, NpcSenses s) { }

    /// <summary>Extra looking about on each sense tick (an armed NPC looks for enemies).</summary>
    private protected virtual void SenseMore(NpcSenses s, float3 eye) { }

    /// <summary>The state changed (also on death).</summary>
    private protected virtual void OnStateChanged(NpcState old, NpcState now) { }

    /// <summary>The pose for this frame is ready (<see cref="NodeWorld"/>, <see cref="Transform"/>).</summary>
    private protected virtual void AfterPose() { }

    /// <summary>Their hands are on something fixed in the world (the nest's gun): no swinging the arms out of walls, and <see cref="PoseOnMount"/> instead.</summary>
    private protected virtual bool HandsOnMount => false;

    /// <summary>The pose for this frame is nearly ready: anything to move in it to stay on what they're holding.</summary>
    private protected virtual void PoseOnMount() { }

    /// <summary>
    /// A vehicle's crew (a turret's gunner, Game/NpcGunner.cs) runs every frame from here instead of the usual moves: they stay
    /// in the hatch whatever happens. True: handled (<paramref name="moving"/> as it went).
    /// </summary>
    private protected virtual bool CrewHold(float dt, NpcSenses s, out bool moving)
    {
        moving = false;
        return false;
    }

    /// <summary>Someone in a vehicle's hatch can't run, hide or cower: they fight from it, or watch from it (<see cref="CrewWatch"/>).</summary>
    private protected bool CrewStates(float dt, NpcSenses s)
    {
        switch (State)
        {
            case NpcState.Fighting:
                return UpdateFight(dt, 0f, 0f, s);
            case NpcState.Startled:
                _reactLeft -= dt;
                if (_reactLeft <= 0 && _staggerLeft <= 0)
                    Decide(s);
                break;
            case NpcState.Wary:
                if (_stateTime > WaryTime)
                    SetState(NpcState.Calm);
                break;
            case NpcState.Calm:
                break;
            default:
                SetState(NpcState.Calm);   // (fleeing, cowering, going back: none of that from up there)
                break;
        }

        if (State != NpcState.Fighting)
            CrewWatch(dt, s);
        return false;
    }

    /// <summary>A crew member not fighting: on watch from the hatch.</summary>
    private protected virtual void CrewWatch(float dt, NpcSenses s) { }

    // ------------------------------------------------------------------------------------------------ route

    /// <summary>Calm: walking the route (the original behaviour). Returns whether they moved.</summary>
    private protected bool UpdateRoute(float dt, float walkSpeed, float3 blocker, NpcSenses s)
    {
        var stopped = _staggerLeft > 0 || _forcedClip is not null;
        if (!stopped && _pauseLeft <= 0)
        {
            // Someone in the way, just ahead: wait for them to move.
            var toBlocker = blocker - Position;
            toBlocker.Y = 0;
            var d = float.Sqrt(float3.Dot(toBlocker, toBlocker));
            stopped = d < 1.1f && float3.Dot(toBlocker, Ahead) > d * 0.5f && float.Abs(blocker.Y - Position.Y) < 1.5f;
            // A van coming: not out in front of it. (Already in its way: on, and out of it.)
            if (!stopped && s.Vehicle.Count > 0)
                stopped = !s.InVehiclesWay(Position) && s.InVehiclesWay(Position + Ahead * 1.2f);
        }

        // Turning to face: whoever shot them, or the way the stop says.
        var face = _staggerLeft > 0 ? _faceShooter : _pauseLeft > 0 ? _route[_next].Face : float.NaN;
        var turning = false;
        if (!float.IsNaN(face) && _walkWeight < 0.5f)
        {
            var left = Wrap(face - Heading);
            turning = float.Abs(left) > ShuffleAngle;
            Heading = Wrap(Heading + float.Clamp(left, -TurnRate * 0.7f * dt, TurnRate * 0.7f * dt));
        }

        if (!turning)
            _shuffled = false;
        else if (!_shuffled && _animSpeed < 0.05f && _animator.FullBodyRemaining <= 0f && _forcedClip is null)
        {
            _shuffled = true;
            _animator.PlayFullBody("ShuffleTurn");
        }

        if (_pauseLeft > 0)
        {
            if (!stopped)
            {
                _pauseLeft -= dt;
                if (_pauseLeft <= 0)
                    _next = (_next + 1) % _route.Length;
            }

            return false;
        }

        if (stopped)
            return false;
        var target = _route[_next].Position;
        var to = target - Position;
        to.Y = 0;
        var dist = float.Sqrt(float3.Dot(to, to));
        // There, or as near as they can get (the stop's too close to a wall, or there's a door shut on the way).
        if ((dist < Arrive && float.Abs(target.Y - Position.Y) < 1f) || _navStall > RouteGiveUp || (_navHave && _navNext >= _navPath.Count && _navVia is null))
        {
            _navStall = 0;
            _navHave = false;
            if (_route[_next].Pause > 0)
                _pauseLeft = _route[_next].Pause;
            else
                _next = (_next + 1) % _route.Length;
            return false;
        }

        GoTowards(target, walkSpeed * float.Max(_walkWeight, 0.2f), dt, TurnRate, s, probe: false);
        return true;
    }

    /// <summary>
    /// A step on the way to <paramref name="goal"/>: straight there if a body can walk the straight line, otherwise round
    /// by a path (<see cref="NpcNav"/>), looked for again when the goal moves on or they stop getting anywhere. The goal's
    /// height says which floor it's on: the way there can be up a ladder or a flight of stairs (<see cref="UpdateLink"/>),
    /// and through a door they open as they get to it. Returns whether they're getting on with it; false at the end of the
    /// path, if that's as near as they can get.
    /// </summary>
    private protected bool GoTowards(float3 goal, float speed, float dt, float turnRate, NpcSenses s, bool probe = true)
    {
        goal = Inside(goal);
        if (s.Nav is not { } nav)
        {
            var straight = goal - Position;
            straight.Y = 0;
            var straightLength = float.Sqrt(float3.Dot(straight, straight));
            return straightLength > 1e-3f && MoveAlong(straight / straightLength, speed, dt, turnRate, s, probe);
        }

        // A jump (a portal, a script): any path they had is from somewhere else.
        var jump = Position - _navLast;
        if (float3.Dot(jump, jump) > 2.25f)
            _navHave = false;
        _navLast = Position;

        // Getting anywhere?
        var away = Position - _navFrom;
        if (float3.Dot(away, away) > 0.04f)
        {
            _navFrom = Position;
            _navStall = 0;
        }
        else
        {
            _navStall += dt;
        }

        _navLookLeft -= dt;
        var gone = goal - _navGoal;
        var stalled = _navStall > NavStallAfter && _navLookLeft <= 0 && _navNext < _navPath.Count;   // (not if this is as far as the path goes)
        if (stalled && NavDebug)
        {
            DebugStalls++;
            var knee = Position + new float3(0, 0.6f, 0);
            Log.Write($"[NAV] {Name} ({State}) stuck at ({Position.X:0.00}, {Position.Y:0.0}, {Position.Z:0.00}), going to ({goal.X:0.0}, {goal.Z:0.0}): point {_navNext + 1} of {_navPath.Count}, next ({_navPath[_navNext].X:0.0}, {_navPath[_navNext].Z:0.0}), facing {Heading:0.00}; ray ahead clear {s.Clear(knee, knee + Ahead * 0.5f):0.00}; {nav.Describe(Position)}");
        }

        if (!_navHave || stalled || (float3.Dot(gone, gone) > NavGoalMoved * NavGoalMoved && _navLookLeft <= 0))
        {
            if (!stalled && nav.SameFloor(Position, goal) && nav.LineClear(Position, goal))
            {
                _navPath.Clear();
                _navPath.Add(goal with { Y = Position.Y });
                _navNext = 0;
                _navGoal = goal;
                _navVia = null;
                _navHave = true;
                _navLookLeft = NavLookEvery;
            }
            else if (s.NavSearchesLeft > 0 && nav.CanSearch)
            {
                s.NavSearchesLeft--;
                if (!SearchNav(nav, goal))
                {
                    // Walled in where they stand: all they can do is try the straight line.
                    _navPath.Clear();
                    _navPath.Add(goal with { Y = Position.Y });
                    _navVia = null;
                }

                _navNext = 0;
                _navGoal = goal;
                _navHave = true;
                _navLookLeft = NavSearchEvery;
                _navSkipLeft = 0;
            }
            else if (!_navHave)
            {
                return false;   // (a free search next frame)
            }
        }

        // On to the next point: when they're there, or now and then if they can already see their way to the one after.
        _navSkipLeft -= dt;
        if (_navSkipLeft <= 0 && _navNext < _navPath.Count - 1)
        {
            _navSkipLeft = 0.25f;
            if (nav.LineClear(Position, _navPath[_navNext + 1]))
                _navNext++;
        }

        while (_navNext < _navPath.Count)
        {
            var to = _navPath[_navNext] - Position;
            to.Y = 0;
            var dist = float.Sqrt(float3.Dot(to, to));
            if (dist < (_navNext == _navPath.Count - 1 ? 0.1f : 0.25f))
            {
                _navNext++;
                continue;
            }

            // A shut door in the way: open it, and wait till it's out of the way.
            if (nav.DoorInWay(Position, _navPath[_navNext]) is { } door)
            {
                if (door.RequestOpen())
                    PushDoor();
                _navStall = 0;
                var want = float.Atan2(to.X, to.Z);
                Heading = Wrap(Heading + float.Clamp(Wrap(want - Heading), -turnRate * dt, turnRate * dt));
                return true;
            }

            var moved = MoveAlong(to / dist, speed, dt, turnRate, s, probe);
            return moved && _navStall <= NavStallAfter;
        }

        // At the foot (or the head) of the ladder or flight this path leads to: on to it.
        if (_navVia is { } via)
        {
            var entry = float3.DistanceSquared(via.Bottom, Position) <= float3.DistanceSquared(via.Top, Position) ? via.Bottom : via.Top;
            var short_ = entry - Position;
            short_.Y = 0;
            if (float3.Dot(short_, short_) > 0.8f * 0.8f)
            {
                _navHave = false;   // (the way there fell short: look again)
                return false;
            }

            if (StartLink(via, speed, s))
                return true;
            // Someone's on the ladder: wait for them to get off.
            _navStall = 0;
            return true;
        }

        return false;   // the end of the path, for what it's worth
    }

    /// <summary>Hands on the door they've just set opening.</summary>
    private protected virtual void PushDoor() => _animator.PlayOverlay("PushDoor", _upperBody, 0.12f, 0.25f);

    // ------------------------------------------------------------------------------------------------ ladders and stairs

    private const float ClimbSpeed = 1.6f;   // m/s up or down the rungs (the player: 3.2, but they're in no such hurry with a gun on their back)
    private const float LinkBusy = 1.9f;     // m: the next one up a ladder keeps this far behind the one in front
    private NavLink? _link;
    private bool _linkUp;
    private int _linkAt;                     // the point of its way they're going to next
    private float _linkSpeed;
    private bool _climbing;

    /// <summary>On a ladder or a flight of stairs: the body's going where the link goes, whatever else is happening.</summary>
    public bool OnLink => _link is not null;

    /// <summary>Going up or down the rungs of a ladder.</summary>
    public bool Climbing => _climbing;

    /// <summary>Tries to set off along <paramref name="link"/> from the end they're at. False: someone's on it, coming the other way or too close ahead.</summary>
    private bool StartLink(NavLink link, float speed, NpcSenses s)
    {
        var up = float3.DistanceSquared(link.Bottom, Position) <= float3.DistanceSquared(link.Top, Position);
        if (link.IsLadder)
        {
            var from = up ? link.Bottom : link.Top;
            foreach (var other in s.People)
            {
                if (ReferenceEquals(other, this) || !other.Alive || other._link != link)
                    continue;
                if (other._linkUp != up || float.Abs(other.Position.Y - from.Y) < LinkBusy)
                    return false;
            }
        }

        _link = link;
        _linkUp = up;
        _linkAt = up ? 1 : link.Way.Length - 2;
        _linkSpeed = speed;
        _navHave = false;
        Log.Write($"[NAV] {Name} ({State}): {(up ? "up" : "down")} {link.Name} from ({Position.X:0.0}, {Position.Y:0.0}, {Position.Z:0.0})");
        return true;
    }

    /// <summary>Along the ladder or flight they're on, a point at a time, turning to go that way; up the rungs, facing them. Off the far end, they look for the way on.</summary>
    private bool UpdateLink(float dt, NpcSenses s)
    {
        var link = _link!;
        if (_staggerLeft > 0)
            return false;   // (shot, or dazzled: they hang on)

        var way = link.Way;
        // The leg from the point they were last at to the one they're going to: its lower end is point min, and the rungs are one leg.
        var lower = _linkUp ? _linkAt - 1 : _linkAt;
        var onRungs = link.IsLadder && lower == link.ClimbFrom;
        SetClimbing(onRungs);
        var to = way[_linkAt] - Position;
        var dist = float.Sqrt(float3.Dot(to, to));
        var step = (onRungs ? ClimbSpeed : float.Min(_linkSpeed, 3f)) * dt;
        if (dist <= step)
        {
            Position = way[_linkAt];
            _linkAt += _linkUp ? 1 : -1;
            if (_linkAt < 0 || _linkAt >= way.Length)
            {
                _link = null;
                SetClimbing(false);
                _navHave = false;
                _navLast = _navFrom = Position;
                _navStall = 0;
                Zone = Level.ZoneAt(Position + new float3(0, 1f, 0));
                return true;
            }

            return true;
        }

        var dir = to / dist;
        var want = onRungs ? float.Atan2(link.Facing.X, link.Facing.Z) : float.Abs(dir.X) + float.Abs(dir.Z) > 1e-3f ? float.Atan2(dir.X, dir.Z) : Heading;
        Heading = Wrap(Heading + float.Clamp(Wrap(want - Heading), -FleeTurnRate * dt, FleeTurnRate * dt));
        Position += dir * step;
        Zone = Level.ZoneAt(Position + new float3(0, 1f, 0));
        return true;
    }

    private void SetClimbing(bool climbing)
    {
        if (climbing)
        {
            if (!_animator.IsPlaying("Climb"))
                _animator.PlayFullBody("Climb", 0.2f, 0.25f, ClimbSpeed);
            WhileClimbing();
        }
        else if (_climbing)
        {
            _animator.Stop("Climb", 0.25f);
            StoppedClimbing();
        }

        _climbing = climbing;
    }

    /// <summary>Each frame on the rungs: both hands are on them (an armed NPC's gun is on their back).</summary>
    private protected virtual void WhileClimbing() { }

    /// <summary>Off the rungs (a gun comes back into their hands).</summary>
    private protected virtual void StoppedClimbing() { }

    /// <summary>Back on the route after a scare: on to whichever of its points is nearest.</summary>
    private protected void ResumeRoute()
    {
        var best = float.MaxValue;
        for (var i = 0; i < _route.Length; i++)
        {
            var d = _route[i].Position - Position;
            d.Y = 0;
            var dd = float3.Dot(d, d);
            if (dd < best)
            {
                best = dd;
                _next = i;
            }
        }

        _pauseLeft = 0;
        _trail.Clear();
        _awareness = float.Min(_awareness, 0.4f);   // still jumpy: they notice the gun again sooner
        SetState(NpcState.Calm);
    }

    private protected float DistanceToRoute(float3 p)
    {
        var best = float.MaxValue;
        foreach (var w in _route)
        {
            var d = w.Position - p;
            d.Y = 0;
            best = float.Min(best, float3.Dot(d, d));
        }

        return float.Sqrt(best);
    }

    // ------------------------------------------------------------------------------------------------ senses

    /// <summary>A few times a second: can they see the gun, or someone else panicking? (Gunshots arrive through <see cref="Hear"/>.)</summary>
    private protected void Sense(float dt, NpcSenses s)
    {
        _playerEye = s.PlayerEye;

        // A gunshot heard a moment ago (sound takes time to arrive, and people a moment to react).
        if (_hearDelay >= 0)
        {
            _hearDelay -= dt;
            if (_hearDelay < 0)
                Alarm(_heardAt, _heardKind);
        }

        // The player's gunshot, heard a moment ago: an armed enemy decides what to do about it (ArmedNpc).
        if (_playerShotDelay >= 0)
        {
            _playerShotDelay -= dt;
            if (_playerShotDelay < 0)
                HeardPlayerShot(_playerShotAt, _playerShotKind, s);
        }

        // A friend's gunshot, heard a moment ago: not a threat in itself, but there's a fight on.
        if (_friendShotDelay >= 0)
        {
            _friendShotDelay -= dt;
            if (_friendShotDelay < 0 && _friendShooter is { } friend)
                HeardFriendlyShot(friend, _friendShotKind, s);
        }

        // A noise heard a moment ago. The player's steps only matter to someone the player's gun would worry (an officer
        // doesn't turn round at every footstep of a citizen).
        if (_noiseDelay >= 0)
        {
            _noiseDelay -= dt;
            if (_noiseDelay < 0 && (!_noiseByPlayer || NoticesPlayerGun(s)))
                Noticed(_noiseAt, _noiseLoud, _noiseByPlayer, s);
        }

        // A portal nearby came on a moment ago (its lamps flashing, the water lit up).
        if (_portalDelay >= 0)
        {
            _portalDelay -= dt;
            if (_portalDelay < 0 && _portalSeen is { } opened)
            {
                _portalSeen = null;
                if (opened.Online && Alive)
                    PortalCameOn(opened, s);
            }
        }

        // Looking round: as often as the tier says (SenseInterval); dormant, not at all (what's above, what they hear, still reaches them).
        _sinceLook += dt;
        _senseTimer -= dt;
        if (_senseTimer > 0 || Tier == NpcTier.Dormant)
            return;
        var interval = SenseInterval(Tier, Near);
        _senseTimer = float.Max(_senseTimer + interval, interval * 0.5f);   // (back from dormant: no catching up on missed looks)
        // What they take in grows with the time since the last look (at most one look's worth, or half a second: back from a
        // dormant spell, no sudden leap).
        var tick = float.Min(_sinceLook, float.Max(interval, 0.5f));
        _sinceLook = 0f;
        if (_blindLeft > 0)
        {
            _seesThreat = false;   // dazzled: nothing to see but the glare
            _playerNear = false;
            return;
        }

        var eye = Position + new float3(0, EyeHeight, 0);
        var ahead = Ahead;

        // The player, and so the gun: in plain view, or through a portal.
        _seesThreat = false;
        if (s.PlayerArmed && s.PlayerAlive && NoticesPlayerGun(s))
        {
            var to = s.PlayerEye - eye;
            var dist = float.Sqrt(float3.Dot(to, to));
            var dir = to / float.Max(dist, 1e-3f);
            var cos = float3.Dot(dir, ahead);
            var along = dir;                // the way the line of sight arrives at the player
            var threat = s.PlayerEye;       // where it looks to them: through a portal, its water
            var through = false;
            // On the alert, they know the player for the enemy at a look, but they see no further for it.
            var alert = AlertForPlayer(s);
            var reach = SightReach(false);
            var seen = Level.ZoneAt(s.PlayerEye) == Zone && dist < s.PlayerSightLimit(eye, reach) && dist > 1e-3f && (cos > SightCos || dist < 1.2f)
                ? s.SeePlayer(eye, s.PlayerEye, 0.995f, reach) : 0f;
            if (seen <= 0f && s.ThroughPortal(eye, s.PlayerEye, SightRange, out var sight, 0.995f))
            {
                var water = sight.Entry - eye;
                cos = float3.Dot(float3.Normalize(water), ahead);
                if (cos > SightCos && s.PlayerLit(sight.Distance) > 0f)
                {
                    seen = sight.Seen * s.PlayerLit(sight.Distance);
                    dist = sight.Distance;
                    along = sight.Along;
                    threat = sight.Entry;
                    through = true;
                }
            }

            if (seen > 0f)
            {
                _seesThreat = true;
                var rate = 2.2f / (1f + dist / 5f) * seen; // per second (slower through thin leaves)
                if (cos < FocusCos)
                    rate *= 0.4f;                           // out of the corner of the eye
                if (float3.Dot(s.PlayerForward, -along) > AimedCos)
                    rate *= 3f;                             // it's pointed at them
                if (dist < 2.5f)
                    rate = 10f;
                if (alert)
                    rate = float.Max(rate * 4f, 5f);        // on the alert: no doubt who that is (a look or two)
                _awareness = float.Min(_awareness + rate * tick, 1.5f);
                // (Someone already fighting works out for themself what to do about a player beyond a portal: ArmedNpc.)
                if (_awareness >= 1f && !(through && State == NpcState.Fighting))
                    Alarm(threat, Stimulus.SawGun);
            }
        }

        if (!_seesThreat)
            _awareness = float.Max(0f, _awareness - 0.2f * tick);

        // A player standing close in front of them: something to glance at (not a fright).
        _playerNear = false;
        if (s.PlayerAlive && Level.ZoneAt(s.PlayerEye) == Zone)
        {
            var to = s.PlayerEye - eye;
            var dd = float3.Dot(to, to);
            if (dd < LookNear * LookNear && dd > 0.04f && float3.Dot(to, ahead) > 0.35f * float.Sqrt(dd) && s.SeePlayer(eye, s.PlayerEye) > 0f)
                _playerNear = true;
        }

        // Others running away, or lying dead, set them off too.
        if (State is NpcState.Calm or NpcState.Wary or NpcState.Returning)
        {
            foreach (var other in s.People)
            {
                // Someone fighting is a gunman to a bystander (an armed NPC sorts friend from foe in SenseMore).
                var fighting = other.State == NpcState.Fighting && !Profile.Fights;
                if (ReferenceEquals(other, this) || other.Scripted || other.Zone != Zone || (other.State is not (NpcState.Dead or NpcState.Fleeing) && !fighting))
                    continue;
                if (!other.Alive && _seenDead.Contains(other))
                    continue;   // already reacted to this body: once calmed down, they can go back to their route

                var at = other.Position + new float3(0, other.Alive ? 1.2f : 0.25f, 0);
                var to = at - eye;
                var dist = float.Sqrt(float3.Dot(to, to));
                if (dist > PeerSight || float3.Dot(to / dist, ahead) < SightCos || s.Clear(eye, at) < 0.99f)
                    continue;
                if (!other.Alive)
                {
                    _seenDead.Add(other);
                    FoundBody(other, s);
                    break;
                }

                Alarm(fighting ? other.Position : other.Threat, Stimulus.Panic);
                break;
            }
        }

        if (Alive)
            SenseMore(s, eye);
    }

    /// <summary>
    /// A gunshot reaches them after <paramref name="delay"/> seconds. <paramref name="byPlayer"/>: the player fired it, and
    /// someone who has their own answer to that gets to give it (<see cref="ReactsToPlayerShots"/>). <paramref name="shooter"/>:
    /// someone here fired it; if it's a friend, they don't take it for the enemy (<see cref="HeardFriendlyShot"/>).
    /// </summary>
    public void Hear(float3 at, Stimulus kind, float delay, bool byPlayer = false, Npc? shooter = null)
    {
        if (!Alive || !SensesEnabled || _forcedClip is not null || _deafLeft > 0)
            return;
        if (shooter is ArmedNpc friend && Recognises(friend))
        {
            if (_friendShotDelay < 0 || delay < _friendShotDelay)
            {
                _friendShooter = friend;
                _friendShotKind = kind;
                _friendShotDelay = delay;
            }

            return;
        }

        if (byPlayer && ReactsToPlayerShots)
        {
            if (_playerShotDelay < 0 || delay < _playerShotDelay)
            {
                _playerShotAt = at;
                _playerShotKind = kind;
                _playerShotDelay = delay;
            }

            return;
        }

        if (_hearDelay >= 0 && _hearDelay < delay && _heardKind == Stimulus.Gunshot)
            return;   // a louder one is already on its way
        _heardAt = at;
        _heardKind = kind;
        _hearDelay = delay;
    }

    /// <summary>
    /// Whether a gunshot reaching them after <paramref name="delay"/> s would be taken in by <see cref="Hear"/> at all,
    /// loud or distant: no, if they can't hear, or one that reaches them sooner is already on its way (in a firefight,
    /// most are). Lets <see cref="Npcs.HearGunshot"/> skip the ray that tells loud from distant when it can't matter.
    /// </summary>
    public bool WouldHearShot(float delay, bool byPlayer, Npc? shooter)
    {
        if (!Alive || !SensesEnabled || _forcedClip is not null || _deafLeft > 0)
            return false;
        if (shooter is ArmedNpc friend && Recognises(friend))
            return _friendShotDelay < 0 || delay < _friendShotDelay;
        if (byPlayer && ReactsToPlayerShots)
            return _playerShotDelay < 0 || delay < _playerShotDelay;
        return !(_hearDelay >= 0 && _hearDelay < delay && _heardKind == Stimulus.Gunshot);
    }

    /// <summary>
    /// One of the few gunshots a frame (<see cref="HearingRaysPerFrame"/>) whose way to them is looked at (is there a wall?).
    /// Past that, more shots in the same frame add nothing they could tell apart.
    /// </summary>
    public bool TakeHearingRay(int frame)
    {
        if (_hearingFrame != frame)
        {
            _hearingFrame = frame;
            _hearingRays = 0;
        }

        return _hearingRays++ < HearingRaysPerFrame;
    }

    private const int HearingRaysPerFrame = 2;
    private int _hearingFrame = -1, _hearingRays;

    /// <summary>
    /// They've come across <paramref name="body"/>. Not an instant panic: they stop and stare at it for a moment (Wary),
    /// taking in what they're looking at, then cry out (anyone armed on their side close by comes to see:
    /// <see cref="ArmedNpc.CallForHelp"/>) and run or cower. (<see cref="ArmedNpc"/> goes to check the body instead.)
    /// </summary>
    private protected virtual void FoundBody(Npc body, NpcSenses s)
    {
        Log.Write($"[NPC] {Name}: found {body.Name}'s body {Distance(Position, body.Position):0.0} m away: staring at it");
        Noticed(body.Position, loud: true, byPlayer: false, s);
        // The shock sinks in, then the fright (as a heard alarm, so it comes through Sense on time).
        _heardAt = body.Position;
        _heardKind = Stimulus.Panic;
        _hearDelay = 1.2f + 0.8f * (float)_random.NextDouble();
        ArmedNpc.CallForHelp(this, body.Position, s, why: $"{Name} cried out at {body.Name}'s body", delay: _hearDelay);
    }

    /// <summary>A noise (a footstep, a bullet striking, a body falling) reaches them after <paramref name="delay"/> seconds.</summary>
    public void HearNoise(float3 at, bool byPlayer, bool loud, float delay)
    {
        if (!Alive || !SensesEnabled || _forcedClip is not null || _deafLeft > 0)
            return;
        if (_noiseDelay >= 0 && _noiseDelay <= delay && (_noiseLoud || !loud))
            return;   // one at least as loud is already on its way
        _noiseAt = at;
        _noiseByPlayer = byPlayer;
        _noiseLoud = loud;
        _noiseDelay = delay;
    }

    /// <summary>A portal near them has just come on (<see cref="Npcs.PortalOpened"/>): they react after <paramref name="delay"/> seconds.</summary>
    public void NoticePortal(Portal portal, float delay)
    {
        if (!Alive || !SensesEnabled || _forcedClip is not null || Scripted || _blindLeft > 0)
            return;
        _portalSeen = portal;
        _portalDelay = delay;
    }

    /// <summary>
    /// A portal near them has come on. Someone unarmed close by and able to see it gets out of its way (a fright: they
    /// run or cower); further off, they stop and look at it. (<see cref="ArmedNpc"/> takes cover or moves up on it.)
    /// </summary>
    private protected virtual void PortalCameOn(Portal portal, NpcSenses s)
    {
        var eye = Position + new float3(0, EyeHeight, 0);
        var face = Npcs.PortalFace(portal, eye);
        if (Distance(eye, face) < Npcs.PortalFrightRange && s.Visibility(eye, face) > 0f)
        {
            Log.Write($"[NPC] {Name}: the {portal.Name} came on {Distance(eye, face):0.0} m away: backing off");
            Alarm(portal.Center, Stimulus.Noise, notThePlayer: true);
        }
        else
        {
            Noticed(portal.Center, loud: false, byPlayer: false, s);
        }
    }

    /// <summary>
    /// A noise heard: going about their business (or already wary), they stop and turn to look at it for a few seconds.
    /// Not a fright: noises alone never make a bystander run. (ArmedNpc goes to see.)
    /// </summary>
    private protected virtual void Noticed(float3 at, bool loud, bool byPlayer, NpcSenses s)
    {
        if (State is not (NpcState.Calm or NpcState.Returning or NpcState.Wary))
            return;
        _threatRawY = at.Y;
        at.Y = Position.Y;
        Threat = at;
        if (State == NpcState.Wary)
            _stateTime = 0;   // something else: keep looking
        else
            SetState(NpcState.Wary, Stimulus.Noise);
    }

    /// <summary>
    /// A flashbang went off at <paramref name="at"/>: dazzled for <paramref name="blind"/> seconds (they double over,
    /// arms up over their eyes: the Cower clip; nothing seen, no running, no shooting) and deaf for
    /// <paramref name="deaf"/> seconds. Then it's a fright like a gunshot next to them: a bystander runs or cowers, an
    /// armed one fights (after whoever threw it, if they can find them).
    /// </summary>
    public void Flashbanged(float3 at, float blind, float deaf)
    {
        if (!Alive || !SensesEnabled || _forcedClip is not null || (blind <= 0 && deaf <= 0))
            return;
        if (deaf > 0)
        {
            _deafLeft = float.Max(_deafLeft, deaf);
            _hearDelay = _noiseDelay = _playerShotDelay = _friendShotDelay = -1;   // whatever was on its way, drowned out
        }

        if (blind > 0)
        {
            var already = _blindLeft > 0;
            _blindLeft = float.Max(_blindLeft, blind);
            _staggerLeft = float.Max(_staggerLeft, _blindLeft);
            _seesThreat = false;
            _awareness = 0;
            if (!already)
            {
                if (State != NpcState.Cowering)
                {
                    StopIdleClips();
                    // A commando never cowers: a flinch, then they stand dazzled with the gun down (OnBlinded).
                    _animator.PlayFullBody(Profile.IsCommando ? "Startle" : "Cower", 0.12f, Profile.IsCommando ? 0.3f : 0.5f);
                }

                OnBlinded(true);
            }
        }

        Log.Write($"[NPC] {Name}: flashbanged {Distance(Position, at):0.0} m away (blind {float.Max(blind, 0f):0.0} s, deaf {float.Max(deaf, 0f):0.0} s)");
        Alarm(at, Stimulus.Flashbang);
    }

    /// <summary>Dazzled by a flashbang (true), or seeing again (false): an armed NPC lowers and raises the gun.</summary>
    private protected virtual void OnBlinded(bool blinded) { }

    /// <summary>Something scary at <paramref name="at"/>.</summary>
    /// <param name="notThePlayer">It isn't the player, however near them it is (a portal coming on beside them).</param>
    private protected void Alarm(float3 at, Stimulus kind, bool notThePlayer = false)
    {
        if (!Alive)
            return;
        // It's only the player (firing, or someone running from them): a friend, so nothing to draw a gun on.
        if (Profile.FriendlyToPlayer && !notThePlayer && Distance(at, _playerEye) < FriendNearby)
            return;
        _threatRawY = at.Y;
        at.Y = Position.Y;
        Threat = at;
        switch (State)
        {
            case NpcState.Calm or NpcState.Returning when kind == Stimulus.DistantShot:
                SetState(NpcState.Wary);
                break;
            case NpcState.Calm or NpcState.Returning or NpcState.Wary:
                _calmLeft = CalmTime;
                _awareness = float.Max(_awareness, 1f);
                _reactLeft = kind == Stimulus.Shot ? 0f : 0.2f + 0.35f * (float)_random.NextDouble();
                SetState(NpcState.Startled, kind);
                break;
            case NpcState.Fleeing or NpcState.Cowering or NpcState.Fighting:
                _calmLeft = CalmTime;
                break;
        }
    }

    /// <summary>The moment after a scare: run, or cower?</summary>
    private protected void Decide(NpcSenses s)
    {
        if (WantsToFight(s))
        {
            SetState(NpcState.Fighting);
            return;
        }

        var away = Position - Threat;
        away.Y = 0;
        var dist = float.Sqrt(float3.Dot(away, away));
        away = dist > 1e-3f ? away / dist : -Ahead;
        // A hiding place on their alley: run for it, bolter or freezer, unless the gun's right on them (Game/NpcHiding.cs).
        if ((dist > TooClose || Hits > 0) && TryHide(s))
        {
            SetState(NpcState.Fleeing);
            return;
        }

        var (dir, clear) = Steer(away, s);
        var run = Temperament == Temperament.Bolter && clear > 1.5f && (dist > TooClose || Hits > 0);
        if (run)
        {
            _fleeDir = dir;
            _fleeClear = clear;
            SetState(NpcState.Fleeing);
        }
        else
        {
            SetState(NpcState.Cowering);
        }
    }

    // ------------------------------------------------------------------------------------------------ fleeing

    private protected bool UpdateFlee(float dt, float runSpeed, NpcSenses s)
    {
        if (_staggerLeft > 0)
            return false;
        if (!_hideTried)
            TryHide(s);
        if (_hide is not null)
            return UpdateHideRun(dt, runSpeed, s);
        var away = Position - Threat;
        away.Y = 0;
        var dist = float.Sqrt(float3.Dot(away, away));
        away = dist > 1e-3f ? away / dist : Ahead;

        // Safe (far enough and out of sight), cornered, or run out of breath: get down and hide.
        if ((_stateTime > 4f && dist > SafeDistance && !_seesThreat) || _stateTime > MaxFleeTime || _cornered >= 0.9f)
        {
            SetState(NpcState.Cowering);
            return false;
        }

        _steerLeft -= dt;
        if (_steerLeft <= 0)
        {
            (_fleeDir, _fleeClear) = Steer(away, s);
            _steerLeft = 0.3f;
            _cornered = _fleeClear < 0.7f ? _cornered + 0.3f : 0f;
        }

        var speed = runSpeed * (1f - _limp) * float.Max(_walkWeight, 0.2f);
        var moved = MoveAlong(_fleeDir, speed, dt, FleeTurnRate, s);
        if (_trail.Count == 0 || Distance2(Position, _trail[^1]) > 1f)
        {
            if (_trail.Count < 400)
                _trail.Add(Position);
        }

        return moved;
    }

    /// <summary>Walking back along the trail they ran.</summary>
    private protected bool UpdateReturn(float dt, float walkSpeed, NpcSenses s)
    {
        if (_staggerLeft > 0)
            return false;
        if (_trail.Count == 0 && _away.Count > 0)
            return ReturnThroughPortal(dt, walkSpeed, s);
        if (_trail.Count == 0)
        {
            ResumeRoute();
            return false;
        }

        var to = _trail[^1] - Position;
        to.Y = 0;
        var dist = float.Sqrt(float3.Dot(to, to));
        if ((dist < 0.45f && float.Abs(_trail[^1].Y - Position.Y) < 1f) || _stuck > 3f)
        {
            _trail.RemoveAt(_trail.Count - 1);
            _stuck = 0;
            return false;
        }

        if (GoTowards(_trail[^1], walkSpeed * float.Max(_walkWeight, 0.2f), dt, TurnRate, s))
        {
            _stuck = 0;
            return true;
        }

        _stuck += dt;
        return false;
    }

    // ------------------------------------------------------------------------------------------------ portals

    /// <summary>Where to stand to step into <paramref name="portal"/>: just in front of its water, on the floor.</summary>
    public static float3 PortalApproach(Portal portal, NpcSenses s, float distance = 0.9f)
    {
        var n = new float3(portal.Normal.X, 0, portal.Normal.Z);
        var len = float.Sqrt(float3.Dot(n, n));
        n = len > 1e-3f ? n / len : new float3(0, 0, 1);
        var p = portal.Center + n * distance;
        var drop = s.Clear(p, p - new float3(0, 3f, 0)) * 3f;   // down to the floor
        return p - new float3(0, drop, 0);
    }

    /// <summary>
    /// Steps into <paramref name="entered"/> and out of the portal it's linked to: they're now in front of that one, facing
    /// out of it, in its part of the level. Remembered, so they can come back the same way; going back through the portal
    /// they last came out of retraces that step instead (and picks up the trail they had on that side).
    /// </summary>
    private protected void GoThrough(Portal entered, NpcSenses s)
    {
        var exit = entered.Linked!;
        List<float3>? before = null;
        var goingBack = _away.TryPeek(out var last) && last.Back == entered && last.Entered == exit;
        if (goingBack)
            before = _away.Pop().Trail;
        else
            _away.Push((exit, entered, [.. _trail]));
        Position = PortalApproach(exit, s);
        Heading = float.Atan2(exit.Normal.X, exit.Normal.Z);
        Zone = Level.ZoneAt(Position + new float3(0, 1f, 0));
        _trail.Clear();
        if (before is not null)
            _trail.AddRange(before);
        _posedFrame = -1;
        _drawnTransform = _prevTransform = Transform;   // not a movement (a ragdoll would take it for one)
        Log.Write($"[NPC] {Name}: through the {entered.Name} -> {exit.Name} ({Zone}{(goingBack ? ", heading home" : "")})");
    }

    /// <summary>Walking home through a portal they followed someone through: back to it, and through.</summary>
    private bool ReturnThroughPortal(float dt, float walkSpeed, NpcSenses s)
    {
        var (back, entered, _) = _away.Peek();
        if (back.Linked != entered)
        {
            // Relinked meanwhile: there's no way home from here.
            _away.Clear();
            _stranded = true;
            Log.Write($"[NPC] {Name}: the {back.Name} doesn't lead back any more: staying here");
            ResumeRoute();
            return false;
        }

        var to = PortalApproach(back, s) - Position;
        to.Y = 0;
        var dist = float.Sqrt(float3.Dot(to, to));
        if (dist < 0.6f || (_stuck > 2f && dist < 2.5f))
        {
            GoThrough(back, s);
            _stuck = 0;
            return false;
        }

        if (GoTowards(PortalApproach(back, s), walkSpeed * float.Max(_walkWeight, 0.2f), dt, TurnRate, s))
        {
            _stuck = 0;
            return true;
        }

        _stuck += dt;
        return false;
    }

    /// <summary>
    /// The building they belong in (its floor plan: only X and Z count), or null to go anywhere. Whatever they hear or see
    /// outside, and wherever the fight or a search would take them, they don't step out of it: they go as far as the
    /// doorway and fight or look from there. (The factory's indoor crew: Game/NpcsFactory.cs.)
    /// </summary>
    public Aabb? KeepInside { get; set; }

    /// <summary>Logged once that they're outside <see cref="KeepInside"/> (Npcs.Update's check).</summary>
    public bool OutsideWarned { get; set; }

    private const float KeepInsideMargin = 0.6f;   // m: how far inside its walls a goal outside is brought back to

    /// <summary><paramref name="p"/>, or the nearest point to it inside <see cref="KeepInside"/>.</summary>
    private protected float3 Inside(float3 p)
    {
        if (KeepInside is not { } box)
            return p;
        var m = KeepInsideMargin;
        return new float3(float.Clamp(p.X, box.Min.X + m, float.Max(box.Min.X + m, box.Max.X - m)), p.Y,
            float.Clamp(p.Z, box.Min.Z + m, float.Max(box.Min.Z + m, box.Max.Z - m)));
    }

    /// <summary>A step to <paramref name="to"/> would take them out of <see cref="KeepInside"/> (from in it: someone
    /// outside it, a script having moved them, can always come back in).</summary>
    private bool LeavesInside(float3 to)
    {
        if (KeepInside is not { } box || !In(box, Position) || In(box, to))
            return false;
        if (_keptInLogged <= _time0 - 10f || _keptInLogged > _time0)
        {
            _keptInLogged = _time0;
            Log.Write($"[NPC] {Name}: at the edge of their building ({Position.X:0.0}, {Position.Z:0.0}): staying inside");
        }

        return true;
    }

    private float _keptInLogged = float.NegativeInfinity;
    private float _time0 => (float)(Environment.TickCount64 / 1000.0);

    private static bool In(Aabb box, float3 p) => p.X >= box.Min.X && p.X <= box.Max.X && p.Z >= box.Min.Z && p.Z <= box.Max.Z;

    /// <summary>
    /// Turns towards <paramref name="dir"/> and steps forward, sliding along a wall rather than into it, and not over the
    /// edge of a floor. Stops at what the body can't get past. <paramref name="probe"/>: also feel ahead with a ray, for
    /// what isn't a collision box (a portal's water, a prop with no collider). Returns whether they moved.
    /// </summary>
    private protected bool MoveAlong(float3 dir, float speed, float dt, float turnRate, NpcSenses s, bool probe = true)
    {
        var want = float.Atan2(dir.X, dir.Z);
        var diff = Wrap(want - Heading);
        Heading = Wrap(Heading + float.Clamp(diff, -turnRate * dt, turnRate * dt));
        var ahead = Ahead;
        var step = speed * float.Clamp(1.2f - float.Abs(diff) / 1.6f, 0.25f, 1f) * dt;
        if (step < 1e-5f)
            return true;   // just setting off
        var from = Position + new float3(0, 0.6f, 0);
        var feel = step + BodyRadius + 0.1f;
        var felt = probe ? s.Clear(from, from + ahead * feel) : 1f;
        if (felt < 0.999f)
        {
            s.Nav?.Learn(Position, Position + ahead * (felt * feel + 0.05f));
            _navHave = false;
            _steerLeft = 0;   // pick a new way next frame
            return false;
        }

        if (s.Nav is not { } nav)
        {
            if (LeavesInside(Position + ahead * step))
                return false;
            Position += ahead * step;
            return true;
        }

        // (Far from the origin a float is only good to a couple of mm: a small step at a high frame rate would just vanish. Carry it over.)
        var wish = ahead * step + _moveCarry + MakeRoom(step, s);
        var to = nav.Slide(Position, wish);
        if (LeavesInside(to))
        {
            _moveCarry = float3.Zero;
            _steerLeft = 0;   // (the doorway's as far as they go: pick another way)
            return false;
        }
        var moved = to - Position;
        Position = to;
        _moveCarry = wish - moved;
        _moveCarry.Y = 0;
        var carried = float3.Dot(_moveCarry, _moveCarry);
        if (carried > 1e-4f)
            _moveCarry = float3.Zero;
        if (float3.Dot(moved, moved) >= step * step * 0.04f || (step < 0.003f && carried <= 1e-4f))
            return true;
        _steerLeft = 0;
        return false;
    }

    /// <summary>A little to one side of anyone they're about to walk into, so a crowd going through a doorway doesn't end up all in one place.</summary>
    private float3 MakeRoom(float step, NpcSenses s)
    {
        var aside = float3.Zero;
        foreach (var other in s.People)
        {
            if (ReferenceEquals(other, this) || !other.Alive || other.Hidden)
                continue;
            var off = Position - other.Position;
            if (float.Abs(off.X) > PersonalSpace || float.Abs(off.Z) > PersonalSpace || float.Abs(off.Y) > 1f)
                continue;
            off.Y = 0;
            var dist = float.Sqrt(float3.Dot(off, off));
            if (dist >= PersonalSpace)
                continue;
            var away = dist > 1e-3f ? off / dist : new float3(float.Sin(Heading + 1.57f), 0, float.Cos(Heading + 1.57f));
            aside += away * ((1f - dist / PersonalSpace) * step);
        }

        return aside;
    }

    /// <summary>
    /// The best way to go, of 12 round them: towards <paramref name="goal"/>, with room to run (two rays, knee and chest
    /// height), not turning too sharply, staying in their own part of the level and near their route. Returns the
    /// direction and how far it's clear.
    /// </summary>
    private protected (float3 Dir, float Clear) Steer(float3 goal, NpcSenses s)
    {
        const int N = 12;
        var bestScore = float.MinValue;
        var best = (Dir: goal, Clear: 0f);
        var ahead = Ahead;
        var here = DistanceToRoute(Position);
        for (var i = 0; i < N; i++)
        {
            var a = i * float.Tau / N + 0.13f;
            var dir = new float3(float.Sin(a), 0, float.Cos(a));
            var clear = FleeLook;
            foreach (var h in (ReadOnlySpan<float>)[0.35f, 1.25f])
            {
                var p = Position + new float3(0, h, 0);
                clear = float.Min(clear, s.Clear(p, p + dir * FleeLook) * FleeLook);
            }

            clear = float.Max(clear - NpcNav.WallRadius, 0f);
            var score = float3.Dot(dir, goal) * 2f + clear / FleeLook * 1.6f + float3.Dot(dir, ahead) * 0.5f;
            var end = Position + dir * float.Max(clear, 1f);
            // A ray fits past a door's jamb or off a ledge where a body doesn't.
            if (s.Nav is { } nav && !nav.LineClear(Position, Position + dir * float.Min(clear, 1.2f)))
                score -= 1.5f;
            if (Level.ZoneAt(end + new float3(0, 1f, 0)) != Zone)
                score -= 4f;
            var fromRoute = DistanceToRoute(end);
            if (fromRoute > Leash && fromRoute > here)
                score -= 3f;
            if (score > bestScore)
            {
                bestScore = score;
                best = (dir, clear);
            }
        }

        return best;
    }

    // ------------------------------------------------------------------------------------------------ states

    private protected void SetState(NpcState state, Stimulus why = Stimulus.SawGun)
    {
        if (State == state)
            return;
        if (State == NpcState.Cowering)
            _animator.Stop("Cower", 0.6f);
        var old = State;
        State = state;
        _stateTime = 0;
        HideStateChanged(state);
        switch (state)
        {
            case NpcState.Wary:
                StopIdleClips();
                if (_animator.FullBodyRemaining <= 0f)
                    _animator.PlayFullBody("IdleLook");
                break;
            case NpcState.Startled:
                StopIdleClips();
                // A jump of fright (not when shot: the hit reaction is already playing). Running, cowering or fighting
                // takes over from it when they've decided what to do.
                if (why is not (Stimulus.Shot or Stimulus.Noise or Stimulus.Flashbang))
                    _animator.PlayFullBody("Startle", 0.06f, 0.3f);
                break;
            case NpcState.Fleeing:
                StopIdleClips();
                _steerLeft = 0.3f;
                _cornered = 0;
                if (_trail.Count == 0)
                    _trail.Add(Position);
                break;
            case NpcState.Cowering:
                _calmLeft = CalmTime;
                _animator.PlayFullBody("Cower", 0.35f, 0.6f);
                break;
            case NpcState.Returning:
                _stuck = 0;
                break;
        }

        OnStateChanged(old, state);
        Log.Write($"[NPC] {Name}: {old} -> {state}" + state switch
        {
            NpcState.Startled => $" ({why switch { Stimulus.SawGun => "saw the gun", Stimulus.Gunshot => "heard a shot", Stimulus.Shot => "was shot", Stimulus.Panic => "saw someone panic", Stimulus.Noise => "heard something: going to see", Stimulus.Flashbang => "flashbanged", _ => "heard a distant shot" }})",
            NpcState.Wary when why == Stimulus.Noise => $" (heard something {Distance(Position, Threat):0.0} m away)",
            NpcState.Fleeing => $" (running {(_limp > 0 ? "with a limp " : "")}from {Distance(Position, Threat):0.0} m away)",
            NpcState.Fighting => $" ({Faction.ToString().ToLowerInvariant()}, {Equipment.ToString().ToLowerInvariant()}, {Aggression.ToString().ToLowerInvariant()}, {Profile.Strategy.ToString().ToLowerInvariant()}{(Profile.Armoured ? ", armoured" : "")})",
            _ => "",
        });
    }

    private protected void StopIdleClips()
    {
        foreach (var clip in _variants)
            _animator.Stop(clip, 0.15f);
        _animator.Stop("IdleLook", 0.15f);
        _animator.Stop("ShuffleTurn", 0.15f);
        _animator.Stop("Startle", 0.2f);
        _animator.Stop("HitFront", 0.15f);
        _animator.Stop("HitBack", 0.15f);
        _animator.Stop("HitLeg", 0.15f);
    }

    private protected void FaceTowards(float3 target, float dt, float rateScale)
    {
        var to = target - Position;
        to.Y = 0;
        if (float3.Dot(to, to) < 1e-4f)
            return;
        var left = Wrap(float.Atan2(to.X, to.Z) - Heading);
        var rate = TurnRate * rateScale * dt;
        Heading = Wrap(Heading + float.Clamp(left, -rate, rate));
    }

    /// <summary>Standing about for a while: now and then look around or shift weight.</summary>
    private protected void IdleVariants(float dt, bool active)
    {
        if (active || !_hasVariants)
        {
            _stillFor = 0;
            _variantTimer = 0;
            return;
        }

        _stillFor += dt;
        if (_stillFor < VariantAfter)
            return;
        _variantTimer += dt;
        if (_variantTimer < VariantEvery)
            return;
        _variantTimer = 0;
        if (_animator.FullBodyRemaining > 0f || _random.NextDouble() >= VariantChance)
            return;
        _animator.PlayFullBody(_variants[_random.Next(_variants.Length)]);
    }

    /// <summary>BRUTALFPS_NPCANIM: locomotion clips at their own speed, overlays over the idle, anything else looped as a full-body clip.</summary>
    private protected void PlayForced()
    {
        var clip = _clips.Get(_forcedClip!);
        if (clip is null)
        {
            _animator.SetLocomotion(0);
            return;
        }

        var tags = clip.Info.Tags;
        if (tags.Contains("locomotion"))
        {
            _animator.SetLocomotion(clip.Speed > 0 ? clip.Speed : string.Equals(clip.Name, "Walk", StringComparison.OrdinalIgnoreCase) ? Npcs.WalkStride / clip.Duration : 0f);
        }
        else if (tags.Contains("overlay"))
        {
            _animator.SetLocomotion(0);
            if (!_animator.IsPlaying(clip.Name))
                _animator.PlayOverlay(clip.Name, _upperBody);
        }
        else
        {
            _animator.SetLocomotion(0);
            if (!_animator.IsPlaying(clip.Name) && _animator.FullBodyRemaining <= 0f)
                _animator.PlayFullBody(clip.Name, 0.2f, tags.Contains("death") ? 0f : 0.25f);
        }
    }

    // ------------------------------------------------------------------------------------------------ damage

    /// <summary>The collision mesh posed as drawn this frame (skinned on the CPU the first time it's asked for each frame).</summary>
    public float3[] Posed(SkinnedModel model, int frame)
    {
        _posed ??= new float3[model.Collision.VertexCount];
        if (_posedFrame != frame)
        {
            model.Collision.Pose(Skin, Transform, _posed);
            _posedFrame = frame;
        }

        return _posed;
    }

    /// <summary>
    /// Shot: a wound and a flinch away from the bullet. An entry wound also takes <paramref name="damage"/> health and adds
    /// <paramref name="bleed"/> HP/s of bleeding; at zero they die, otherwise they panic (or, without senses, stop and turn
    /// to face the shooter).
    /// </summary>
    public void TakeHit(Wound wound, BodyPart part, float3 direction, float3 from, int[] flinchNodes, int flinchPivot, float damage = 0, float bleed = 0, Npc? firedBy = null)
    {
        AwakeFor = float.Max(AwakeFor, Npcs.AwakeAfterHit);
        if (!wound.Exit)
            Hits++;
        if (wound.Radius > 0f)   // (a blunt blow leaves no wound to draw)
        {
            if (_wounds.Count >= Renderer.MaxWounds)
                _wounds.RemoveAt(0);
            _wounds.Add(wound);
        }
        if (wound.Exit)
        {
            if (!KeepAlive)
                _bleed += bleed;
            return;
        }

        // The flinch axis in the model's own space (it's applied before the model is placed).
        var local = float3.Transform(direction, float4x4.CreateRotationY(-Heading));
        var axis = float3.Cross(float3.UnitY, new float3(local.X, 0, local.Z));
        var len = float.Sqrt(float3.Dot(axis, axis));
        if (len > 1e-4f)
        {
            _flinchAxis = axis / len;
            _flinchNodes = flinchNodes;
            _flinchPivot = flinchPivot;
            _flinch = 0;
            var kick = part switch { BodyPart.Head => 7.5f, BodyPart.Arm => 9f, BodyPart.Leg => 3f, _ => 5f };
            _flinchVelocity = Alive ? kick : kick * 0.4f;
        }

        var node = _skeleton.JointNode(wound.Joint);
        if (!Alive)
        {
            // A body: the shot shoves the limb it hit (now, or as soon as the ragdoll exists).
            Push(node, direction * BodyPush, WoundPosition(wound));
            return;
        }

        if (KeepAlive)
            return;   // (a script needs them, and on their feet: the wound and the flinch are all that shows)
        Health -= damage;
        _bleed += bleed;
        if (part == BodyPart.Leg)
            _limp = float.Min(_limp + 0.3f, 0.6f);
        var toShooter = from - Position;
        _faceShooter = float.Atan2(toShooter.X, toShooter.Z);
        if (Health <= 0 && Undying)
            Survive($"{part.ToString().ToLowerInvariant()} hit for {damage:0}");
        if (Health <= 0)
        {
            Die(direction, part == BodyPart.Head ? 1.25f : 1f);
            Push(node, direction * KillPush, WoundPosition(wound));
            return;
        }

        if (!SensesEnabled || _forcedClip is not null)
        {
            _staggerLeft = float.Max(_staggerLeft, 1.4f);   // as before: stop and face the shooter
            HitReact(part, direction);
            return;
        }

        _staggerLeft = float.Max(_staggerLeft, 0.4f);
        _awareness = 1.5f;
        // Caught by a friend's round: it hurts, but the enemy is whoever that friend was firing at.
        var threat = from;
        if (firedBy is ArmedNpc friend && Recognises(friend))
        {
            threat = friend.AimPoint;
            Log.Write($"[NPC] {Name}: hit by {friend.Name}'s fire: not the enemy");
        }

        Alarm(threat, Stimulus.Shot);
        HitReact(part, direction);   // after Alarm: the state change stops idle and reaction clips
    }

    /// <summary>
    /// The whole body flinches from a hit that didn't kill: HitLeg if the leg was hit, HitBack if the bullet came from
    /// behind, HitFront otherwise. They stand while it plays (cowering, they stay down and only the spring flinch shows).
    /// </summary>
    private protected void HitReact(BodyPart part, float3 direction)
    {
        if (State == NpcState.Cowering || _forcedClip is not null)
            return;
        var flat = new float3(direction.X, 0, direction.Z);
        var clip = part == BodyPart.Leg ? "HitLeg" : float3.Dot(flat, Ahead) > 0 ? "HitBack" : "HitFront";
        if (HandsOnMount)
        {
            OnHitReact();   // (the spring flinch shows; they don't let go of the gun for it)
            return;
        }

        if (_clips.Get(clip) is not { } c)
            return;
        _animator.PlayFullBody(clip, 0.04f, 0.2f);
        _staggerLeft = float.Max(_staggerLeft, c.Duration * 0.85f);
        OnHitReact();
    }

    /// <summary>A hit reaction has started (an armed NPC drops the aim for it).</summary>
    private protected virtual void OnHitReact() { }

    /// <summary>A shove on the ragdoll (queued: it's applied on the next update, when the ragdoll exists).</summary>
    private void Push(int node, float3 impulse, float3 point)
    {
        if (_pushes.Count < 16)
            _pushes.Add((node, impulse, point));
    }

    /// <summary>Wounds bleed health away (each eases off over half a minute or so).</summary>
    private protected void Bleed(float dt)
    {
        if (_bleed <= 1e-3f)
            return;
        Health -= _bleed * dt;
        _bleed *= float.Exp(-dt / BleedFade);
        if (Health <= 0 && Undying)
            Survive("bled out");
        if (Health <= 0)
        {
            Log.Write($"[NPC] {Name} bled out");
            Die(Ahead, 0.8f);   // a slump forwards
        }
    }

    /// <summary>How many people have died, all told (each death's number is its <see cref="DeathSerial"/>).</summary>
    internal static int Deaths;

    /// <summary>Which death this was (0: alive), for <see cref="Npcs"/> to tell their side once.</summary>
    internal int DeathSerial;

    /// <summary>Falls: forwards if the bullet came from behind (it was going the way they face), backwards if from the front.</summary>
    private protected void Die(float3 direction, float speed)
    {
        var wasCowering = State == NpcState.Cowering;
        var old = State;
        State = NpcState.Dead;
        DeathSerial = ++Deaths;
        _stateTime = 0;
        Health = float.Min(Health, 0f);
        _forcedClip = null;
        _hearDelay = -1;
        _noiseDelay = -1;
        var flat = new float3(direction.X, 0, direction.Z);
        var forwards = float3.Dot(flat, Ahead) > 0;
        var clip = forwards ? "DeathFwd" : "DeathBack";
        if (_clips.Get(clip) is null)
            clip = forwards ? "DeathBack" : "DeathFwd";
        if (wasCowering)
            _animator.Stop("Cower", 0.3f);   // State was set directly, so SetState's cleanup didn't run
        if (_clips.Get(clip) is null)
            Log.Write($"[WARN] {Name} has no death clip (DeathFwd/DeathBack): they'll stay in their last pose");
        _animator.Stop("Talk", 0.2f);
        _animator.PlayFullBody(clip, wasCowering ? 0.3f : 0.12f, 0f, speed);   // without physics, the death clip is what's seen
        _ragdollWanted = true;
        OnStateChanged(old, NpcState.Dead);
        Log.Write($"[NPC] {Name} killed ({old}, {Hits} hits): {clip}");
    }

    /// <summary>A wound's position in the world now (it moves with the joint that carries it).</summary>
    public float3 WoundPosition(in Wound w) => float3.Transform(float3.Transform(w.BindPosition, Skin[w.Joint]), Transform);

    private protected static float Wrap(float a)
    {
        while (a > float.Pi) a -= float.Tau;
        while (a < -float.Pi) a += float.Tau;
        return a;
    }

    private protected static float Distance2(float3 a, float3 b)
    {
        var d = a - b;
        d.Y = 0;
        return float3.Dot(d, d);
    }

    private protected static float Distance(float3 a, float3 b) => float.Sqrt(Distance2(a, b));
}
/// <summary>A bullet's hit on a person.</summary>
public readonly record struct NpcHit(Npc Npc, SkinHit Skin, BodyPart Part);

/// <summary>
/// The people in the level (Assets/Models/human.glb, built by Tools/blender/build_human.py): each walks its route
/// with the clips of a per-person <see cref="Animator"/> (Walk / Idle / Run / variants, from the model and
/// anims/human_anims.glb), dressed from the <see cref="Wardrobe"/> (garments skinned on the same skeleton), skinned on the GPU, at a level of detail picked by distance from the camera, with a soft
/// contact shadow under the body and feet (the sun and floodlight shadow maps are rendered once and don't include
/// anything that moves). Bullets are tested against their actual (posed) skin; the player bumps into them.
/// </summary>
public sealed partial class Npcs
{
    /// <summary>Ground covered by one cycle of the Walk clip (two steps), as animated in Blender.</summary>
    public const float WalkStride = 1.24f;

    /// <summary>Camera distances (m) beyond which levels of detail 1, 2 and 3 are used.</summary>
    public static readonly float[] LodDistances = [7f, 16f, 34f];

    private static readonly float3[] SkinTones =
    [
        new(0.88f, 0.68f, 0.56f), new(0.74f, 0.53f, 0.41f), new(0.62f, 0.45f, 0.32f),
        new(0.47f, 0.32f, 0.22f), new(0.33f, 0.22f, 0.15f), new(0.93f, 0.76f, 0.66f),
    ];

    private readonly SkinnedModel? _model;
    private readonly AnimationClip? _walk, _idle;
    private readonly ClipLibrary? _clips;
    private readonly Wardrobe? _wardrobe;
    private readonly Dictionary<string, Outfit> _outfitOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _animOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Npc> _people = [];
    private readonly DynamicMesh _shadows = new();
    private readonly ImpactEffects _effects;
    private readonly (PhysicsWorld World, RagdollRig Rig)? _ragdolls;   // null without physics: the dead play death clips
    private readonly Random _random = new(5);
    private readonly int _pelvis, _footL, _footR, _ballL, _ballR;
    private readonly int _handR = -1;
    private readonly int _handL = -1;   // (holding a card player's cards: Game/NpcSeat.cs)
    private readonly int _headNode = -1;   // (night-vision goggles and headsets: Game/NpcHeadGear.cs)
    private readonly (int[] Nodes, int Pivot) _flinchTorso, _flinchHead, _flinchArmL, _flinchArmR;
    private readonly float _walkSpeed, _runSpeed, _runStride;
    private readonly Dictionary<string, Temperament> _moodOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _senses = Environment.GetEnvironmentVariable("BRUTALFPS_NOSENSES") != "1";
    internal static readonly HashSet<string> NotOnNpcGun = new(StringComparer.OrdinalIgnoreCase)
        { "Hand", "HandLeft", "Sleeve", "SleeveLeft", "TriggerFinger", "MagazineRound" };   // the view model's hands
    private int _respawns = int.MinValue;
    private readonly float4[] _woundData = new float4[Renderer.MaxWounds];
    private readonly float4[] _woundInfo = new float4[Renderer.MaxWounds];
    private const int OutfitSeed = 1;
    private float _time;
    private int _frame;

    public List<Npc> People => _people;

    private readonly List<PortalCrossing> _crossings = [];

    /// <summary>m: a portal coming on this near someone unarmed who can see it frightens them (<see cref="Npc.PortalCameOn"/>).</summary>
    public const float PortalFrightRange = 8f;
    private const float PortalNoticedInSight = 40f;   // m: how far off people notice a portal come on, with a clear line to it
    private const float PortalNoticedUnseen = 15f;    // m: and round a corner or through a wall (the hum, the light spilling out)

    /// <summary>A point just off <paramref name="portal"/>'s water on <paramref name="eye"/>'s side (the water itself blocks lines of sight).</summary>
    public static float3 PortalFace(Portal portal, float3 eye) =>
        portal.Center + portal.Normal * (portal.SignedDistance(eye) >= 0f ? 0.2f : -0.2f);

    /// <summary>
    /// <paramref name="portal"/> has just been connected: its lamps start flashing and its water lights up. People in the
    /// room it opens into, near enough, notice a moment later (<see cref="Npc.PortalCameOn"/>): an armed one takes cover or
    /// moves up to cover it (<see cref="ArmedNpc"/>); someone unarmed nearby backs off, further off they stop and look.
    /// </summary>
    public void PortalOpened(Portal portal)
    {
        StationPortalOpened(portal);   // (the white portal brings the Arctic station's crew out: Game/NpcsStation.cs)
        var zone = Level.ZoneAt(Npc.PortalApproach(portal, Senses, 1.5f) + new float3(0, 1f, 0));
        var count = 0;
        foreach (var npc in _people)
        {
            if (!npc.Alive || npc.Scripted || npc.Zone != zone)
                continue;
            var eye = npc.Position + new float3(0, Npc.EyeHeight, 0);
            var face = PortalFace(portal, eye);
            var d = eye - face;
            var dist = float.Sqrt(float3.Dot(d, d));
            var inSight = Senses.Clear(eye, face) > 0.99f;
            if (dist > (inSight ? PortalNoticedInSight : PortalNoticedUnseen))
                continue;
            npc.NoticePortal(portal, 0.25f + dist / 60f + 0.5f * (float)_random.NextDouble());
            count++;
        }

        if (count > 0)
            Log.Write($"[NPC] The {portal.Name} came on: {count} {(count == 1 ? "person" : "people")} near it react");
    }

    /// <summary>The player walked into <paramref name="portal"/>: armed people hunting them may follow (<see cref="ArmedNpc"/>).</summary>
    public void PlayerUsedPortal(Portal portal)
    {
        _crossings.Add(new PortalCrossing(portal, _time));
        if (_crossings.Count > 8)
            _crossings.RemoveAt(0);
    }

    /// <summary>How fast people walk and run (m/s), and the ground a cycle of the Run clip covers (two steps).</summary>
    public float WalkSpeed => _walkSpeed;
    public float RunSpeed => _runSpeed;
    public float RunStride => _runStride;

    /// <summary>A body for the player, for the security camera to see (null without the human model). Call after <see cref="GunModels"/> is filled.</summary>
    public PlayerDouble? CreatePlayerDouble()
    {
        if (_model is null || _clips is null)
            return null;
        var outfit = _wardrobe?.FromIds(["hoodie", "jeans", "boots_work", "hair_buzz"], OutfitSeed) ?? Outfit.Empty;
        return new PlayerDouble(_model, _clips, outfit, GunModels.GetValueOrDefault(Equipment.Pistol));
    }

    /// <summary>
    /// The guns drawn in armed NPCs' hands, by what they carry (the player's view models, without their hands: pistol.glb,
    /// ak47.glb, shotgun.glb, ump.glb, ar15.glb, g3.glb).
    /// </summary>
    public Dictionary<Equipment, Model> GunModels { get; } = [];

    /// <summary>
    /// Debug: BRUTALFPS_NPCGUNS gives every armed NPC that gun instead of their own ("pistol", "ak", "shotgun", "ump", "ar", "g3",
    /// "m249", or a British gun by name: "l85a2", "l135a1", ...), or "mixed" hands them all out in turn; "standard" keeps the
    /// level's own guns (otherwise hostiles get a mix: see <see cref="MixedGun"/>).
    /// </summary>
    private static readonly string? GunOverride = Environment.GetEnvironmentVariable("BRUTALFPS_NPCGUNS")?.ToLowerInvariant();
    private int _gunsHandedOut;

    /// <summary>Debug: BRUTALFPS_NPCS=passive makes everyone a bystander: no one fights, they all cower or flee.</summary>
    private static readonly bool AllPassive = Environment.GetEnvironmentVariable("BRUTALFPS_NPCS")?.Trim().ToLowerInvariant() == "passive";

    /// <summary>Debug: BRUTALFPS_NPCPLAN=push, cover or hold gives every armed NPC that <see cref="NpcStrategy"/>.</summary>
    private static readonly NpcStrategy PlanOverride = Environment.GetEnvironmentVariable("BRUTALFPS_NPCPLAN")?.Trim().ToLowerInvariant() switch
    {
        "push" => NpcStrategy.Push,
        "cover" => NpcStrategy.Cover,
        "hold" => NpcStrategy.HoldBack,
        _ => NpcStrategy.Auto,
    };

    /// <summary>Debug: BRUTALFPS_NPCARMOUR=all or none gives every hostile body armour, or none of them.</summary>
    private static readonly bool? ArmourOverride = Environment.GetEnvironmentVariable("BRUTALFPS_NPCARMOUR")?.Trim().ToLowerInvariant() switch
    {
        "all" => true,
        "none" => false,
        _ => null,
    };

    /// <summary>Every shot fired in the factory and its grounds so far, by anyone (<see cref="FactoryEvacuation"/>: the fight has started).</summary>
    public int FactoryGunshots { get; private set; }

    /// <summary>An armed NPC fired: (shooter, muzzle, bullet velocities). Program spawns the bullets and plays the sound.</summary>
    public event Action<Npc, float3, float3[]>? Shot;

    /// <summary>The player has shot a civilian or an officer since they last respawned: the police are after them.</summary>
    public bool PlayerWanted { get; private set; }

    /// <summary>What they can see and hear: set <see cref="NpcSenses.Clear"/> once the level's collision exists.</summary>
    public NpcSenses Senses { get; } = new();

    /// <summary>Health a bullet takes, by where it hits (9 mm): a head shot kills, about three in the body do.</summary>
    public static float BaseDamage(BodyPart part) => part switch
    {
        BodyPart.Head => 100f,
        BodyPart.Torso => 40f,
        BodyPart.Leg => 24f,
        _ => 18f,
    };

    /// <summary>HP per second a wound there bleeds at first (exit wounds half as much again).</summary>
    public static float BleedRate(BodyPart part) => part switch
    {
        BodyPart.Head => 1.5f,
        BodyPart.Torso => 0.8f,
        BodyPart.Leg => 0.5f,
        _ => 0.3f,
    };

    /// <summary>Where the warehouse's test subject stands (1 m in front of the column at (6, 0, 0)).</summary>
    public static readonly float3 TestSubjectSpot = new(6f, 0, 1.75f);

    /// <summary>People drawn in the last frame's main view, per level of detail (for the log).</summary>
    public int[] LodCounts { get; } = new int[4];

    /// <param name="effects">Blood and so on.</param>
    /// <param name="physics">For ragdolls.</param>
    /// <param name="people">False: nobody is loaded (the firing range on its own).</param>
    /// <param name="factoryRaid">Only the people of the factory and the police HQ (a factory raid, where the other levels aren't loaded).</param>
    public Npcs(ImpactEffects effects, PhysicsWorld? physics = null, bool people = true, bool factoryRaid = false)
    {
        _factoryRaid = factoryRaid;
        _effects = effects;
        if (!people)
        {
            _model = null;
            return;
        }

        try
        {
            _model = SkinnedModel.Get("human");
            _clips = ClipLibrary.Load(_model, "anims/human_anims", "anims/human_combat", "anims/human_react", "anims/human_rifle", "anims/human_climb", "anims/human_gunner", "anims/human_seated");
            _walk = _clips.Get("Walk");
            _idle = _clips.Get("Idle");
            if (_walk is null || _idle is null)
                throw new FormatException("human.glb needs the clips Walk and Idle.");
        }
        catch (Exception e)
        {
            Log.Write($"[WARN] No people: couldn't load the human model ({e.GetType().Name}: {e.Message}).");
            _model = null;
            return;
        }

        Senses.Fire = (npc, muzzle, velocities) =>
        {
            Shot?.Invoke(npc, muzzle, velocities);
            HearGunshot(muzzle, shooter: npc, heavy: npc is ArmedNpc { OnGun: true } or ArmedNpc { Equipment: Equipment.Lmg }, volume: (npc as ArmedNpc)?.GunInUse.Volume ?? 1f);
        };
        var s = _model.Skeleton;
        _handR = s.NodeIndex("hand.R");
        _handL = s.NodeIndex("hand.L");
        _headNode = s.NodeIndex("head");
        _pelvis = s.NodeIndex("pelvis");
        _footL = s.NodeIndex("foot.L");
        _footR = s.NodeIndex("foot.R");
        _ballL = s.NodeIndex("ball.L");
        _ballR = s.NodeIndex("ball.R");
        (int[], int) Chain(string name) => (s.Subtree(s.NodeIndex(name)), s.NodeIndex(name));
        _flinchTorso = Chain("spine_02");
        _flinchHead = Chain("neck_01");
        _flinchArmL = Chain("upperarm.L");
        _flinchArmR = Chain("upperarm.R");
        if (physics is not null && Environment.GetEnvironmentVariable("BRUTALFPS_NORAGDOLL") != "1")
        {
            try
            {
                var rig = BuildRagdollRig(s);
                _ragdolls = (physics, rig);
                Log.Write($"Ragdolls: {rig.BodyCount} bodies, {rig.Mass:0} kg");
            }
            catch (ArgumentException e)
            {
                Log.Write($"[WARN] No ragdolls ({e.Message}): the dead play death clips.");
            }
        }

        _walkSpeed = WalkStride / _walk.Duration;
        var run = _clips.Get("Run");
        _runStride = run is not null && run.Info.Stride > 0 ? run.Info.Stride : 2.7f;
        _runSpeed = run is not null && run.Duration > 0 ? _runStride / run.Duration : _walkSpeed * 1.6f;
        ParseMoodOverrides(Environment.GetEnvironmentVariable("BRUTALFPS_NPCMOOD"));

        // Clothes (BRUTALFPS_NOCLOTHES=1 leaves everyone as they were) and the test overrides, parsed once.
        if (Environment.GetEnvironmentVariable("BRUTALFPS_NOCLOTHES") != "1")
        {
            try
            {
                _wardrobe = Wardrobe.Load(_model);
            }
            catch (Exception e)
            {
                Log.Write($"[WARN] Wardrobe failed ({e.GetType().Name}: {e.Message}): people are drawn unclothed.");
            }

            ParseOutfitOverrides(Environment.GetEnvironmentVariable("BRUTALFPS_OUTFIT"));
        }

        ParseAnimOverrides(Environment.GetEnvironmentVariable("BRUTALFPS_NPCANIM"));
        Populate();
        foreach (var name in _outfitOverrides.Keys.Concat(_animOverrides.Keys))
        {
            if (!_people.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                Log.Write($"[WARN] BRUTALFPS_OUTFIT / BRUTALFPS_NPCANIM: nobody is called '{name}'.");
        }

        foreach (var (name, clip) in _animOverrides)
        {
            if (_clips.Get(clip) is null)
                Log.Write($"[WARN] BRUTALFPS_NPCANIM: no clip called '{clip}' (for '{name}').");
        }

        Log.Write($"People: {_people.Count}, walking at {_walkSpeed:0.00} m/s, running at {_runSpeed:0.00} m/s, senses {(_senses ? "on" : "off")}; garments loaded: {_wardrobe?.GarmentCount ?? 0}; " +
                  $"clips: {_clips.All.Count} ({string.Join(", ", _clips.All.Select(c => c.Name))}); " +
                  $"bullets test {_model.Collision.TriangleCount} triangles each");
    }

    /// <summary>
    /// The ragdoll a person falls as: boxes for the pelvis and chest, capsules for the head and each limb (the hand rides
    /// on the forearm, the foot on the shin), about 70 kg. The pelvis is first (it's the ragdoll's centre). Hips and
    /// shoulders swing round a centre tipped forward, since legs and arms go much further forward than back.
    /// </summary>
    private static RagdollRig BuildRagdollRig(Skeleton s)
    {
        var rig = new RagdollRig(s);
        var pelvis = rig.AddBox("pelvis", "spine_02", 0.20f, 0.27f, 11f);
        var chest = rig.AddBox("spine_02", "neck_01", 0.22f, 0.30f, 18f);
        var head = rig.AddCapsule("neck_01", rig.RestPosition("head") + new float3(0, 0.19f, 0), 0.1f, 5f);
        rig.AddCone(pelvis, chest, 0.45f, 0.35f, 0.15f);
        rig.AddCone(chest, head, 0.6f, 0.7f, 0.1f);
        foreach (var side in (ReadOnlySpan<string>)[".L", ".R"])
        {
            var upper = rig.AddCapsule("upperarm" + side, "forearm" + side, 0.05f, 2.2f);
            var lower = rig.AddCapsule("forearm" + side, "middle_01" + side, 0.042f, 1.7f);
            var thigh = rig.AddCapsule("thigh" + side, "calf" + side, 0.07f, 8f);
            var shin = rig.AddCapsule("calf" + side, "ball" + side, 0.052f, 4.2f);
            rig.AddCone(chest, upper, 1.3f, 0.8f, 0.5f);
            rig.AddHinge(upper, lower, new float3(0, 0, 1), 2.5f);    // elbows bend the forearm forwards
            rig.AddCone(pelvis, thigh, 1.1f, 0.4f, 0.6f);
            rig.AddHinge(thigh, shin, new float3(0, 0, -1), 2.4f);    // knees bend the shin backwards
        }

        return rig.Finish();
    }

    /// <summary>BRUTALFPS_OUTFIT="test subject:tshirt,trousers_work,boots_work;patrol:hivis_vest,...".</summary>
    private void ParseOutfitOverrides(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        foreach (var entry in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = entry.IndexOf(':');
            if (colon <= 0)
            {
                Log.Write($"[WARN] BRUTALFPS_OUTFIT: expected 'name:id,id' but got '{entry}'.");
                continue;
            }

            var name = entry[..colon].Trim();
            var ids = entry[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (_wardrobe is null)
            {
                Log.Write($"[WARN] BRUTALFPS_OUTFIT: there is no wardrobe, so '{name}' stays unclothed.");
                continue;
            }

            _outfitOverrides[name] = _wardrobe.FromIds(ids, OutfitSeed);
        }
    }

    /// <summary>BRUTALFPS_NPCANIM="test subject:Run;dock:Talk".</summary>
    private void ParseAnimOverrides(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        foreach (var entry in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = entry.IndexOf(':');
            if (colon <= 0 || colon == entry.Length - 1)
            {
                Log.Write($"[WARN] BRUTALFPS_NPCANIM: expected 'name:Clip' but got '{entry}'.");
                continue;
            }

            _animOverrides[entry[..colon].Trim()] = entry[(colon + 1)..].Trim();
        }
    }

    /// <summary>BRUTALFPS_NPCMOOD="test subject:flee;patrol:cower" (flee / bolter, cower / freezer).</summary>
    private void ParseMoodOverrides(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        foreach (var entry in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = entry.IndexOf(':');
            var mood = colon > 0 ? entry[(colon + 1)..].Trim().ToLowerInvariant() : "";
            Temperament? t = mood switch { "flee" or "bolter" or "run" => Temperament.Bolter, "cower" or "freezer" => Temperament.Freezer, _ => null };
            if (t is null)
            {
                Log.Write($"[WARN] BRUTALFPS_NPCMOOD: expected 'name:flee' or 'name:cower' but got '{entry}'.");
                continue;
            }

            _moodOverrides[entry[..colon].Trim()] = t.Value;
        }
    }

    /// <summary>A bystander (a civilian with no gun).</summary>
    private void Add(string name, float3 skin, float heading, Temperament temperament, params Waypoint[] route) =>
        Add(name, skin, heading, NpcProfile.Civilian(temperament), null, route);

    /// <summary>
    /// Someone made from <paramref name="profile"/>: an <see cref="ArmedNpc"/> if they have a gun and will use it, else a
    /// plain <see cref="Npc"/>. <paramref name="garments"/> dresses them (garment ids) instead of the wardrobe's pick.
    /// </summary>
    private void Add(string name, float3 skin, float heading, NpcProfile profile, string[]? garments, params Waypoint[] route)
    {
        var phase = (_people.Count * 0.37f + 0.13f) % 1f;
        if (profile.Faction == Faction.Hostile && ArmourOverride is { } armoured)
            profile = profile with { Armoured = armoured };
        if (PlanOverride != NpcStrategy.Auto)
            profile = profile with { Strategy = PlanOverride };
        // Now and then an ordinary hostile is a commando instead (rolled by name: the same people every run).
        if (!profile.IsCommando && profile.Armed && profile.Faction == Faction.Hostile && !profile.TwoHanded && !profile.FixedGun && !profile.ShootsAnyone
            && new Random(Npc.StableSeed(name) ^ 0xc0de).NextDouble() < CommandoChance)
            profile = NpcProfile.Commando(NpcProfile.CommandoGun(Npc.StableSeed(name)), strategy: profile.Strategy);
        if (!_exactProfile)   // (the NPC test area sets every trait itself: Game/NpcsTestArea.cs)
            profile = profile.Rolled(new Random(Npc.StableSeed(name) ^ 0x5eed));
        if (SpecOpsTest.Contains(name))
            _kitFor[name] = Kit.SpecOps;
        if (_wardrobe is not null && !_outfitOverrides.ContainsKey(name))
        {
            if (_kitFor.TryGetValue(name, out var kit))
                _outfitOverrides[name] = kit switch { Kit.HiVis => HiVisKit(name, profile), Kit.SpecOps => SpecOpsKit(name), _ => ArcticKit(name) };
            else if (profile.IsCommando)
                _outfitOverrides[name] = CommandoUniform(name);
        }
        if (profile.Armoured && profile.Fights)
            garments = WithCarrier(garments);
        var outfit = _outfitOverrides.TryGetValue(name, out var forced) ? forced
            : garments is not null && _wardrobe is not null ? _wardrobe.FromIds(garments, OutfitSeed)
            : _wardrobe?.For(name, OutfitSeed) ?? Outfit.Empty;
        if (_moodOverrides.TryGetValue(name, out var mood))
            profile = profile with { Temperament = mood };
        if (profile.Armed && GunOverride is { Length: > 0 })
        {
            Equipment? gun = GunOverride switch
            {
                "pistol" => Equipment.Pistol,
                "ak" or "ak47" or "rifle" => Equipment.Rifle,
                "shotgun" => Equipment.Shotgun,
                "ump" or "smg" => Equipment.Smg,
                "ar" or "ar15" or "carbine" => Equipment.Carbine,
                "g3" => Equipment.BattleRifle,
                "m249" or "lmg" => Equipment.Lmg,
                "mixed" => NpcGun.All[_gunsHandedOut++ % NpcGun.All.Length],
                _ => Enum.TryParse<Equipment>(GunOverride, ignoreCase: true, out var named) && named.ToString().StartsWith('L') ? named : null,
            };
            if (gun is { } g)
            {
                profile = profile with { Equipment = g };
                Log.Write($"[NPC] {name} carries a {g.ToString().ToLowerInvariant()} (BRUTALFPS_NPCGUNS)");
            }
        }
        else if (profile.Armed && profile.Faction == Faction.Hostile && !profile.TwoHanded && !profile.FixedGun)
        {
            profile = profile with { Equipment = MixedGun(profile.Equipment, profile.Armoured, name) };
        }

        var npc = profile.Fights && !AllPassive
            ? new ArmedNpc(name, _model!.Skeleton, _clips!, outfit, route, skin, heading, phase, profile) { SensesEnabled = _senses }
            : new Npc(name, _model!.Skeleton, _clips!, outfit, route, skin, heading, phase, profile) { SensesEnabled = _senses };
        if (_kitFor.TryGetValue(name, out var worn) && worn == Kit.SpecOps)
        {
            npc.HeadGear = SpecOpsAccent(name);
            // (Goggles down at night: the boat team by the Antwerp yard's day or night, everyone else by the factory's.)
            npc.GogglesDown = TimeOfDay.IsNight(name.StartsWith(BoatTeamPrefix, StringComparison.Ordinal) ? DayNightLevel.Yard : DayNightLevel.Factory);
        }
        if (_animOverrides.TryGetValue(name, out var clip))
            npc.ForceClip(clip);
        if (npc is ArmedNpc)
            Log.Write($"[NPC] {name}: {profile.Equipment.ToString().ToLowerInvariant()}, {profile.Aggression.ToString().ToLowerInvariant()}{(profile.Aggression == Aggression.Aggressive ? $", {profile.Strategy.ToString().ToLowerInvariant()}" : "")}{(profile.IsCommando ? ", commando" : profile.Armoured ? ", armoured" : "")}{(npc is ArmedNpc { SelfPreserving: true } ? ", self-preserving" : "")}");
        _people.Add(npc);
    }

    // What hostiles carry: the level says a long gun (a rifleman) or a sidearm (a gunman); which one is rolled from these,
    // by weight. Most riflemen still have an AK; the heavy and rare guns go more to those in armour.
    private static readonly (Equipment Gun, float Weight, float ArmouredWeight)[] LongGuns =
    [
        (Equipment.Rifle, 32f, 22f),        // AK-47
        (Equipment.BattleRifle, 9f, 10f),   // G3
        (Equipment.Carbine, 9f, 10f),       // AR-15
        (Equipment.Smg, 8f, 5f),            // UMP40
        (Equipment.Shotgun, 7f, 6f),
        (Equipment.L85A2, 4f, 5f),
        (Equipment.L85A3, 2f, 3f),
        (Equipment.L119A1, 4f, 5f),
        (Equipment.L403A1, 2f, 3f),
        (Equipment.L143A2, 2f, 3f),
        (Equipment.L80A1, 3f, 2f),          // MP5K
        (Equipment.L92A1, 2f, 2f),          // MP5SD
        (Equipment.L129A1, 3f, 5f),         // marksman's rifle
        (Equipment.Lmg, 3f, 7f),            // M249
        (Equipment.L135A1, 0.5f, 1.5f),     // the .50: rare
    ];

    private static readonly (Equipment Gun, float Weight, float ArmouredWeight)[] Sidearms =
    [
        (Equipment.Pistol, 50f, 35f),
        (Equipment.Shotgun, 18f, 20f),
        (Equipment.Smg, 14f, 20f),
        (Equipment.L80A1, 12f, 15f),
        (Equipment.L92A1, 6f, 10f),
    ];

    /// <summary>
    /// A hostile's gun, from the level's choice (<paramref name="given"/>: a pistol for a gunman, a long gun for a
    /// rifleman), rolled from <see cref="LongGuns"/> or <see cref="Sidearms"/> by their name, so they carry the same every
    /// run. BRUTALFPS_NPCGUNS=standard keeps the level's own.
    /// </summary>
    private static Equipment MixedGun(Equipment given, bool armoured, string name)
    {
        var table = given == Equipment.Pistol ? Sidearms : LongGuns;
        var total = 0f;
        foreach (var e in table)
            total += armoured ? e.ArmouredWeight : e.Weight;
        var roll = (float)new Random(Npc.StableSeed(name) ^ 0x6a11).NextDouble() * total;
        foreach (var e in table)
        {
            roll -= armoured ? e.ArmouredWeight : e.Weight;
            if (roll < 0f)
                return e.Gun;
        }

        return given;
    }

    // The prison's raiders, who come out of the container in the intro (Game/PrisonSquad.cs).
    private (float3 At, float Heading, NpcStrategy Plan)[]? _raiderPosts;
    private static readonly string[] RaiderKit = ["longsleeve", "trousers_work", "boots_work", "beanie"];

    private ArmedNpc AddRaider(int i, float3 at, float heading, NpcStrategy plan)
    {
        Add($"raider {i + 1}", SkinTones[(i * 2 + 1) % SkinTones.Length], heading,
            NpcProfile.Rifleman(0.6f) with { Armoured = true, Strategy = plan, TwoHanded = true, ShootsAnyone = true }, RaiderKit,
            new Waypoint(at, float.PositiveInfinity, heading));
        return (ArmedNpc)_people[^1];
    }

    /// <summary>
    /// The four raiders, for the intro to move: null without people. If everyone's brought back after this (a respawn) the
    /// raiders stand guard at <paramref name="posts"/>, where the intro left them.
    /// </summary>
    public ArmedNpc[]? AddRaiders((float3 At, float Heading, NpcStrategy Plan)[] posts)
    {
        if (_model is null)
            return null;
        _people.RemoveAll(p => p.Name.StartsWith("raider ", StringComparison.Ordinal));
        _raiderPosts = posts;
        return [.. posts.Select((p, i) => AddRaider(i, p.At, p.Heading, p.Plan))];
    }

    // The armed officers who ride in the back of the van on a factory raid (Game/RaidOfficers.cs).
    private (float3 At, float Heading)[]? _raidOfficerPosts;
    private static readonly string[] RaidOfficerKit = ["longsleeve", "trousers_work", "boots_work", "cap"];
    public const string RaidOfficerPrefix = "raid officer ";

    private ArmedNpc AddRaidOfficer(int i, float3 at, float heading)
    {
        var profile = new NpcProfile(Faction.Police, Equipment.Carbine, Aggression.Defensive, Temperament.Freezer, Accuracy: 0.7f,
            OnLostSight: LostSight.Hold, FriendlyToPlayer: true, Armoured: true, Strategy: NpcStrategy.HoldBack, TwoHanded: true, FixedGun: true);
        var name = $"{RaidOfficerPrefix}{i + 1}";
        _kitFor[name] = Kit.SpecOps;
        Add(name, SkinTones[(i * 2 + 2) % SkinTones.Length], heading, profile, RaidOfficerKit,
            new Waypoint(at, float.PositiveInfinity, heading));
        var officer = (ArmedNpc)_people[^1];
        officer.HoldsPost = true;
        return officer;
    }

    /// <summary>
    /// The officers who ride in with the player on a factory raid, for <see cref="RaidOfficers"/> to move: null without
    /// people. Their routes end at <paramref name="posts"/> (behind the parked van), and after a respawn they're back there.
    /// </summary>
    public ArmedNpc[]? AddRaidOfficers((float3 At, float Heading)[] posts)
    {
        if (_model is null)
            return null;
        _people.RemoveAll(p => p.Name.StartsWith(RaidOfficerPrefix, StringComparison.Ordinal));
        _raidOfficerPosts = posts;
        return [.. posts.Select((p, i) => AddRaidOfficer(i, p.At, p.Heading))];
    }

    /// <summary>The raid's officers as they are now (new ones after a respawn), in order; empty if there are none.</summary>
    public ArmedNpc[] RaidOfficersNow =>
        [.. _people.OfType<ArmedNpc>().Where(p => p.Name.StartsWith(RaidOfficerPrefix, StringComparison.Ordinal)).OrderBy(p => p.Name, StringComparer.Ordinal)];

    // The boat team who come ashore with the player on an Antwerp raid by boat (Game/AntwerpRaid.cs).
    private (float3 At, float Heading)[]? _boatTeamPosts;
    private static readonly string[] BoatTeamKit = ["longsleeve", "trousers_work", "boots_work", "helmet"];
    public const string BoatTeamPrefix = "boat team ";

    /// <summary>One of the boat team (null if they came out unarmed: BRUTALFPS_NPCS=passive).</summary>
    private ArmedNpc? AddBoatTeamMember(int i, float3 at, float heading)
    {
        var profile = new NpcProfile(Faction.Police, Equipment.Carbine, Aggression.Aggressive, Temperament.Freezer, Accuracy: 0.75f,
            OnLostSight: LostSight.Search, FriendlyToPlayer: true, Armoured: true, Strategy: NpcStrategy.Cover, TwoHanded: true, FixedGun: true);
        var name = $"{BoatTeamPrefix}{i + 1}";
        _kitFor[name] = Kit.SpecOps;
        Add(name, SkinTones[(i * 2 + 1) % SkinTones.Length], heading, profile, BoatTeamKit,
            new Waypoint(at, float.PositiveInfinity, heading));
        return _people[^1] as ArmedNpc;
    }

    /// <summary>
    /// The three who come ashore with the player on an Antwerp raid by boat, for <see cref="BoatTeam"/> to move: null
    /// without people. Their routes end at <paramref name="posts"/> (on the quay by the ladder), and after a respawn they're back there.
    /// </summary>
    public ArmedNpc[]? AddBoatTeam((float3 At, float Heading)[] posts)
    {
        if (_model is null)
            return null;
        _people.RemoveAll(p => p.Name.StartsWith(BoatTeamPrefix, StringComparison.Ordinal));
        _boatTeamPosts = posts;
        ArmedNpc[] team = [.. posts.Select((p, i) => AddBoatTeamMember(i, p.At, p.Heading)).OfType<ArmedNpc>()];
        return team.Length > 0 ? team : null;
    }

    /// <summary>The boat team as they are now (new ones after a respawn), in order; empty if there are none.</summary>
    public ArmedNpc[] BoatTeamNow =>
        [.. _people.OfType<ArmedNpc>().Where(p => p.Name.StartsWith(BoatTeamPrefix, StringComparison.Ordinal)).OrderBy(p => p.Name, StringComparer.Ordinal)];

    // On an Antwerp raid by boat, a few more of the yard's crew walk the open quay where the boat comes in (Game/AntwerpRaid.cs).
    private bool _quayGuards;

    /// <summary>Puts the quay's guards out (an Antwerp raid by boat): five hostiles about the open quay north-west of the yard, and back after a respawn.</summary>
    public void AddQuayGuards()
    {
        _quayGuards = true;
        if (_model is not null && !_people.Any(p => p.Name.StartsWith("quay guard ", StringComparison.Ordinal)))
            PlaceQuayGuards();
    }

    private void PlaceQuayGuards()
    {
        var o = Level.YardOrigin;
        Waypoint Q(float x, float z, float pause, float face) => new(o + new float3(x, 0, z), pause, face);
        const float east = float.Pi / 2, west = -float.Pi / 2, north = 0f, south = float.Pi;
        (float Face, Waypoint[] Route)[] beats =
        [
            (west, [Q(-17f, 27f, 6, west), Q(-17f, 17f, 5, west)]),       // out on the open quay, watching the water
            (south, [Q(-9f, 33f, 5, south), Q(-3f, 22f, 6, west)]),
            (west, [Q(-21f, 37f, 7, west), Q(-12f, 38f, 4, south)]),
            (north, [Q(-4f, 14.5f, 6, west), Q(-14f, 14.5f, 5, north)]),   // the lane behind the yard's north row
            (east, [Q(-20f, 5f, 5, west), Q(-20f, -4f, 6, north)]),        // south along the quay, by the west stacks
        ];
        for (var i = 0; i < beats.Length; i++)
            AddDockHand($"quay guard {i + 1}", 20 + i, beats[i].Face, i == 2, beats[i].Route);   // (the third: a commando)
    }

    /// <summary>The chance an ordinary hostile is a commando (<see cref="NpcProfile.Commando"/>) instead.</summary>
    private const double CommandoChance = 0.03;

    /// <summary>
    /// A commando's kit: helmet, plate carrier, long sleeves, work trousers and boots, in near-black and dark olive
    /// (a shade either way per person), the carrier coyote brown, with cropped hair under the helmet.
    /// </summary>
    private Outfit CommandoUniform(string name)
    {
        var shade = 0.85f + 0.3f * (float)new Random(Npc.StableSeed(name) ^ 0x7a1).NextDouble();
        return _wardrobe!.Dyed(
            ("hair_buzz", new float3(0.03f, 0.02f, 0.015f)),
            ("helmet", new float3(0.12f, 0.12f, 0.09f) * shade),
            ("longsleeve", new float3(0.045f, 0.05f, 0.045f) * shade),
            ("plate_carrier", new float3(0.17f, 0.14f, 0.085f) * shade),   // (coyote brown: it reads against the black)
            ("trousers_work", new float3(0.055f, 0.06f, 0.05f) * shade),
            ("boots_work", new float3(0.03f, 0.028f, 0.025f)));
    }

    /// <summary>What someone wears whatever the level's kit list says: a ship's crew in hi-vis, the Arctic's in red arctic kit.</summary>
    private enum Kit { HiVis, Arctic, SpecOps }

    private readonly Dictionary<string, Kit> _kitFor = [];

    private static readonly float3 HiVisYellow = new(0.85f, 0.80f, 0.02f), HiVisOrange = new(0.95f, 0.35f, 0.02f);

    /// <summary>
    /// Hi-vis waterproofs, yellow mostly (one in four orange): the jacket with its reflective tape and the trousers, black from
    /// the knees down, with work boots and a cap, a beanie or bare cropped hair. Anyone in body armour wears the plate carrier
    /// over a hi-vis long-sleeved top instead of the jacket (a commando with his helmet).
    /// </summary>
    private Outfit HiVisKit(string name, NpcProfile profile)
    {
        var random = new Random(Npc.StableSeed(name) ^ 0x41f1);
        var colour = random.NextDouble() < 0.75 ? HiVisYellow : HiVisOrange;
        var hair = HairColours[random.Next(HairColours.Length)];   // (the prison yard's: Game/NpcsPrisonYard.cs)
        var kit = new List<(string, float3)> { ("hivis_trousers", colour), ("boots_work", new float3(0.04f, 0.035f, 0.03f)) };
        if (profile.IsCommando)
            kit.AddRange([("hair_buzz", hair), ("helmet", new float3(0.12f, 0.12f, 0.09f))]);
        else
            kit.Add(random.Next(3) switch
            {
                0 => ("cap", new float3(0.04f, 0.05f, 0.10f)),
                1 => ("beanie", new float3(0.05f, 0.05f, 0.06f)),
                _ => ("hair_short", hair),
            });
        if (profile.Armoured)
            kit.AddRange([("longsleeve", colour), ("plate_carrier", profile.IsCommando ? new float3(0.17f, 0.14f, 0.085f) : new float3(0.05f, 0.055f, 0.05f))]);
        else
            kit.Add(("hivis_jacket", colour));
        return _wardrobe!.Dyed([.. kit]);
    }

    /// <summary>
    /// Red arctic kit: a red parka (a black yoke, a reflective band round each arm) with its fur-ruffed hood up, red insulated
    /// trousers and black boots. Body armour, if they have it, is under the parka.
    /// </summary>
    private Outfit ArcticKit(string name)
    {
        var shade = 0.9f + 0.2f * (float)new Random(Npc.StableSeed(name) ^ 0xa7c).NextDouble();
        var red = new float3(0.62f, 0.025f, 0.025f) * shade;
        return _wardrobe!.Dyed(("parka_hood", red), ("parka", red), ("snow_trousers", red * 0.92f), ("boots_work", new float3(0.03f, 0.03f, 0.03f)));
    }

    // Special-operations kit (the factory raid's officers, the Antwerp boat team): the khaki and tan of multicam, with
    // ranger green or coyote brown armour. Each person gets one of these looks, rolled by name.
    private static readonly (float3 Helmet, float3 Carrier, float3 Accent)[] SpecOpsLooks =
    [
        (new(0.26f, 0.29f, 0.18f), new(0.29f, 0.31f, 0.2f), new(0.22f, 0.24f, 0.15f)),      // ranger green
        (new(0.33f, 0.26f, 0.16f), new(0.31f, 0.24f, 0.145f), new(0.27f, 0.21f, 0.13f)),       // coyote brown
        (new(0.26f, 0.29f, 0.18f), new(0.31f, 0.24f, 0.145f), new(0.27f, 0.21f, 0.13f)),      // green helmet, coyote carrier
        (new(0.30f, 0.27f, 0.18f), new(0.29f, 0.31f, 0.2f), new(0.22f, 0.24f, 0.15f)),      // tan helmet, green carrier
    ];

    /// <summary>BRUTALFPS_SPECOPS="test subject;patrol": puts these people in special-operations kit too, to look at it.</summary>
    private static readonly HashSet<string> SpecOpsTest =
        [.. (Environment.GetEnvironmentVariable("BRUTALFPS_SPECOPS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static int SpecOpsLook(string name) => new Random(Npc.StableSeed(name) ^ 0x5ec0).Next(SpecOpsLooks.Length);

    /// <summary>The colour of the battery pack and ear cups on a special-operations helmet (<see cref="NpcHeadGear"/>).</summary>
    private static float3 SpecOpsAccent(string name) => SpecOpsLooks[SpecOpsLook(name)].Accent;

    /// <summary>
    /// Special-operations kit: a helmet over cropped hair, a plate carrier over a long-sleeved combat shirt, combat trousers
    /// and coyote boots. The shirt and trousers are multicam's khaki and tan (the trousers a little greener).
    /// </summary>
    private Outfit SpecOpsKit(string name)
    {
        var look = SpecOpsLooks[SpecOpsLook(name)];
        var shade = 0.9f + 0.2f * (float)new Random(Npc.StableSeed(name) ^ 0x5ec1).NextDouble();
        return _wardrobe!.Dyed(
            ("hair_buzz", new float3(0.03f, 0.022f, 0.015f)),
            ("helmet", look.Helmet),
            ("longsleeve", new float3(0.36f, 0.32f, 0.22f) * shade),
            ("plate_carrier", look.Carrier),
            ("trousers_work", new float3(0.30f, 0.29f, 0.19f) * shade),
            ("boots_work", new float3(0.26f, 0.2f, 0.12f)));
    }

    /// <summary>The same clothes with a plate carrier over them, in place of any jacket, and something under it.</summary>
    private static string[] WithCarrier(string[]? garments)
    {
        string[] outer = ["hoodie", "work_jacket", "hivis_vest"];
        var kept = (garments ?? ["trousers_work", "boots_work"]).Where(g => !outer.Contains(g)).ToList();
        if (!kept.Contains("tshirt") && !kept.Contains("longsleeve"))
            kept.Add("longsleeve");
        kept.Add("plate_carrier");
        return [.. kept];
    }

    /// <summary>Where everyone walks.</summary>
    private void Populate()
    {
        // The NPC test build: nobody but the test area's people, in the factory's outbuilding (Game/NpcsTestArea.cs).
        if (NpcTest)
        {
            PopulateTestArea();
            return;
        }

        // Warehouse: a test subject standing still with its back to the middle column (so shots through it splash the
        // column), facing the spawn; one patrolling round the column; another hanging about by the dock.
        Add("test subject", SkinTones[0], 0, Temperament.Freezer, new Waypoint(TestSubjectSpot, float.PositiveInfinity, 0));
        Add("patrol", SkinTones[1], float.Pi / 2, Temperament.Bolter,
            new(new(0.5f, 0, 7.5f), 2.5f), new(new(8.5f, 0, 7.5f)), new(new(9.0f, 0, -3.0f), 3f), new(new(1.0f, 0, -3.5f)));
        Add("dock", SkinTones[4], float.Pi / 2, Temperament.Freezer,
            new(new(-10.8f, 0, 1.2f), 6f, float.Pi / 2), new(new(-7.5f, 0, 5.0f), 4f, float.Pi * 0.8f));

        // (A factory raid has only the factory and the police HQ: everyone else, in these blocks, isn't made at all.)
        if (!_factoryRaid)
        {
            // Outback roadhouse: a few people about the forecourt, the road, the cars and the motel.
            var o = Level.OutbackOrigin;
            Waypoint W(float x, float z, float pause = 0, float face = float.NaN) => new(o + new float3(x, 0, z), pause, face);
            Add("road", SkinTones[0], float.Pi, Temperament.Bolter, W(4.8f, -70, 2), W(4.8f, -112, 2));
            Add("pumps", SkinTones[2], 0, Temperament.Bolter, W(9.6f, -94.5f, 4), W(9.8f, -88.5f, 3), W(13.5f, -87.5f));
            Add("verandah", SkinTones[3], -float.Pi / 2, Temperament.Freezer, W(10.6f, -97.5f, 30, -float.Pi / 2), W(10.6f, -98.5f, 30, -float.Pi / 2));
            Add("car", SkinTones[5], float.Pi / 2, Temperament.Bolter, W(14.6f, -75.8f), W(20.6f, -75.8f, 1.5f), W(20.6f, -84.4f), W(14.6f, -84.4f, 1.5f));
            Add("motel", SkinTones[1], 0, Temperament.Bolter, W(21f, -113, 2), W(27.5f, -96), W(32.5f, -88.5f, 3), W(27.5f, -96));
            Add("billboard", SkinTones[4], float.Pi / 2, Temperament.Freezer, W(29, -69, 2), W(40, -65.5f, 2));
            AddMotelGunmen();   // (one behind each motel room's door: Game/NpcsMotel.cs)

            // The fenced yard behind the shack, where the yellow portal comes out (x 11..21, z -6..6 from the origin; the
            // water tank is in its north-east corner): four commandos with pump shotguns hanging about in it, a few metres
            // off the portal's water, the way in covered from every side. They open fire on sight.
            // Four commandos with pump shotguns (Game/NpcProfile.cs: armour, helmets, accurate, aggressive).
            var shotgunner = NpcProfile.Commando(Equipment.Shotgun);
            const float west = -float.Pi / 2, south = float.Pi;
            Add("yard commando 1", SkinTones[3], west, shotgunner, null, W(14.5f, -2.5f, 6, west), W(14.5f, 2.0f, 5, west));
            Add("yard commando 2", SkinTones[0], south, shotgunner, null, W(17.5f, -4.2f, 12, south), W(16.5f, -4.4f, 8, west));
            Add("yard commando 3", SkinTones[4], west, shotgunner, null, W(19.6f, -1.0f, 15, west), W(19.4f, 0.4f, 9, west));
            Add("yard commando 4", SkinTones[2], west, shotgunner, null, W(16.2f, 4.6f, 10, west - 0.4f), W(15.2f, 4.2f, 7, west));

            // The armoured car parked south of the yard (Game/LevelArmouredCar.cs): a commando standing in its roof hatch on the
            // M249 in the turret, watching over the yard's fence. They never climb out: they swing the turret round onto whatever
            // they see, and fire from it till they're shot (Game/NpcGunner.cs).
            var hatch = Level.ArmouredCarGunnerAt;
            Add("turret gunner", SkinTones[1], Level.ArmouredCarFacing, NpcProfile.Commando(), null,
                new Waypoint(hatch, 60f, Level.ArmouredCarFacing), new Waypoint(hatch + new float3(0, 0, 0.01f), 60f, Level.ArmouredCarFacing));
            if (_people[^1] is ArmedNpc turretGunner)
                turretGunner.TurretCrew = true;

            // The Arctic: everyone is inside the research station, out of the cold (Game/NpcsStation.cs).
            AddStationCrew();

            // The jungle lab: workers at the leaf heap and the drums, and one walking between the shelters and the camp.
            var j = Level.JungleOrigin;
            Waypoint J(float x, float z, float pause = 0, float face = float.NaN) => new(j + new float3(x, 0, z), pause, face);
            Add("leaf heap", SkinTones[2], 0, Temperament.Freezer, J(0.2f, -12.0f, 12, -float.Pi / 2), J(0.4f, -13.4f, 8, -float.Pi / 2));
            Add("drums", SkinTones[3], 0, Temperament.Bolter, J(-1.0f, -14.3f, 6, 0), J(2.8f, -14.0f, 4), J(3.0f, -11.2f, 5));
            Add("camp", SkinTones[1], float.Pi / 2, Temperament.Bolter, J(-9.5f, -2.0f, 4), J(-6.5f, -11.0f, 2), J(-6.0f, -17.5f), J(6.5f, -18.0f, 5), J(5.8f, -6.0f, 3));
            // Debug: BRUTALFPS_SHACKWALK=1 adds someone walking in and out of the storage shack by its door, from the clearing's
            // north-east and from right outside the door, logging where they are (Npcs.Update).
            if (ShackWalk)
                Add("shack walker", SkinTones[4], -float.Pi / 2, Temperament.Bolter,
                    J(5.0f, 2.2f, 1), J(-1.2f, 2.6f, 1), J(0.5f, 1.8f, 1), J(6.0f, -5.0f, 1), J(-1.5f, 2.9f, 1), J(4.0f, 2.5f, 1));

            // The lab's guards: twenty riflemen with AKs (hostile; one in three in body armour) in a ring round the clearing, just
            // under the trees, each walking a short beat along the edge and stopping to watch the clearing. They open fire on sight.
            string[][] guardKit = [["tshirt", "trousers_work", "boots_work", "cap"], ["longsleeve", "jeans", "boots_work", "beanie"],
                                   ["work_jacket", "trousers_work", "boots_work"], ["hoodie", "jeans", "sneakers", "beanie"]];
            var clearing = Level.JungleClearingCenter - j;
            for (var i = 0; i < 20; i++)
            {
                var a = i * float.Tau / 20 + 0.08f;
                float2 At(float angle) => new(clearing.X + float.Sin(angle) * 23f, clearing.Z - float.Cos(angle) * 20f);
                var (from, to) = (At(a - 0.07f), At(a + 0.07f));
                var facing = float.Atan2(clearing.X - from.X, clearing.Z - from.Y);   // towards the middle of the clearing
                Add($"guard {i + 1}", SkinTones[i % SkinTones.Length], facing, NpcProfile.Rifleman(0.35f + 0.03f * (i % 6)) with { Armoured = i % 3 == 0 }, guardKit[i % guardKit.Length],
                    J(from.X, from.Y, 5f + i % 4, facing), J(to.X, to.Y, 4f + i % 3, facing));
            }

            // The gun nest's man (Game/GunNest.cs), standing behind the M249 with his AK and now and then going to the end of the bags.
            var nestAt = Level.GunNestOrigin - j;
            Add("nest guard", SkinTones[2], Level.GunNestFacing, NpcProfile.Commando(), null,   // (a commando: the jungle always has one)
                J(nestAt.X - 0.5f, nestAt.Z - 0.9f, 14f, Level.GunNestFacing), J(nestAt.X + 1.1f, nestAt.Z - 1.2f, 7f, Level.GunNestFacing + 0.5f));
            if (_people[^1] is ArmedNpc nestGuard)
                nestGuard.PostedAtGun = true;   // (trouble, and he's on the gun: Game/NpcGunner.cs)
        }

        // The factory grounds: someone wandering about outside, between the outbuilding and the boiler house.
        Add("yard", SkinTones[5], float.Pi / 2, Temperament.Bolter,
            new(new(-28f, 0, -12f), 6f), new(new(-26f, 0, -40f), 2f), new(new(-12f, 0, -42f)), new(new(10f, 0, -40f), 3f),
            new(new(12f, 0, -38.5f), 5f, float.Pi), new(new(-12f, 0, -43f)), new(new(-26f, 0, -41f)));

        // The police station: someone at the copier, an officer doing the rounds of the bullpen, the armourer at work.
        // The officers here are on the player's side, whatever they do.
        var po = Level.PoliceOrigin;
        Waypoint P(float x, float z, float pause = 0, float face = float.NaN) => new(po + new float3(x, 0, z), pause, face);
        Add("copier", SkinTones[0], -float.Pi / 2, Temperament.Bolter, P(-7.9f, -1.4f, 14, -float.Pi / 2), P(-7.6f, 2.3f, 3), P(-7.6f, -2.6f));
        Add("desk officer", SkinTones[3], float.Pi / 2, NpcProfile.Officer() with { FriendlyToPlayer = true }, null,
            P(-7.0f, -3.5f, 2), P(7.6f, -3.5f, 1), P(7.6f, 2.35f, 6, float.Pi / 2), P(-7.0f, 2.35f, 3), P(-7.0f, -3.0f));
        Add("armourer", SkinTones[2], float.Pi / 2, NpcProfile.Officer(Temperament.Freezer) with { Accuracy = 0.8f, FriendlyToPlayer = true }, null,
            P(13.9f, -3.05f, 20, 0), P(14.6f, -3.6f, 6, float.Pi / 2), P(13.3f, -4.6f, 8, -float.Pi / 2));

        if (!_factoryRaid)
        {
            // The prison: the officer on duty in the gatehouse's control booth, and one keeping an eye on the yard from by
            // the north wing's door.
            Waypoint Pr(float x, float z, float pause = 0, float face = float.NaN) => new(Level.PrisonAt(x, 0, z), pause, face);
            Add("gate officer", SkinTones[4], float.Pi / 2, NpcProfile.PrisonOfficer(Temperament.Freezer), null,
                Pr(16.7f, 31.6f, 25, float.Pi / 2), Pr(16.6f, 32.4f, 9, float.Pi / 2));
            Add("yard officer", SkinTones[0], float.Pi, NpcProfile.PrisonOfficer(), null,
                Pr(-3.0f, -14.2f, 12, float.Pi), Pr(3.0f, -14.2f, 10, float.Pi));
        }

        // Gunmen on the factory grounds (hostile, armed, aggressive): one keeping watch by the boiler house, two walking
        // the yard between the outbuilding and the gate. They shoot at the player (and the police) on sight.
        string[] gang = ["hoodie", "jeans", "sneakers", "beanie"];
        Add("gunman watch", SkinTones[1], float.Pi, NpcProfile.Commando(), null,   // (a commando: the factory always has one)
            // (The west stop is clear of the raid's delivery van, which turns in east of the hall's door and backs up past it.)
            new Waypoint(new(11.5f, 0, -37.5f), 9f, float.Pi), new Waypoint(new(8.5f, 0, -44f), 6f, float.Pi));
        Add("gunman yard", SkinTones[4], float.Pi / 2, NpcProfile.Gunman(0.4f), gang,
            // (The east stop is north of the van parked nose-out at the hall's door, whose bonnet reaches z = -41.97: at z = -41 the
            // straight walk to it went through the bonnet, and CheckFactoryCrew said so on every run.)
            new(new(-24f, 0, -40.5f), 4f), new(new(-12f, 0, -42.5f), 1f), new(new(6f, 0, -43.2f), 3f), new(new(-12f, 0, -42.5f)));
        Add("gunman gate", SkinTones[2], 0, NpcProfile.Gunman(0.35f), ["work_jacket", "jeans", "boots_work", "cap"],
            new Waypoint(new(-27f, 0, -14f), 7f, 0), new Waypoint(new(-27.5f, 0, -24f), 5f, float.Pi));
        AddFactoryCrew();   // ...and the rest of them, in and around the factory (Game/NpcsFactory.cs).
        if (_factoryRaid)
        {
            AddOfficeWomen();   // (only on a raid: Game/FactoryEvacuation.cs)
            for (var i = 0; i < (_raidOfficerPosts?.Length ?? 0); i++)
                AddRaidOfficer(i, _raidOfficerPosts![i].At, _raidOfficerPosts[i].Heading);   // (after a respawn: behind the van)
        }

        if (_factoryRaid)
            return;

        AddRioCrowd();      // Rio: a hundred in the favela's alleys, civilians and hostiles (Game/NpcsRio.cs).

        // Hostiles at the docks: ten in the container yard at Antwerp, fifty on the container ship, about the ground and
        // the deck and up on the stacks (Game/NpcsShipYard.cs). Debug: BRUTALFPS_NODOCKCREW=1 leaves them out.
        if (Environment.GetEnvironmentVariable("BRUTALFPS_NODOCKCREW") != "1")
        {
            AddYardCrew();
            AddShipCrew();
        }

        // The flats: two masked riflemen, one either side of the stairwell's door, facing out across the undercroft. They stay
        // on the door. (Rook, whose way out it is, is spared: they let him through.)
        string[] masked = ["longsleeve", "trousers_work", "boots_work", "balaclava"];
        for (var i = 0; i < Level.FlatsDoorGuards.Length; i++)
        {
            Add($"door gunman {i + 1}", SkinTones[i * 3 % SkinTones.Length], -float.Pi / 2,
                i == 0 ? NpcProfile.Commando() : NpcProfile.Rifleman(0.5f) with { Armoured = true, TwoHanded = true }, i == 0 ? null : masked,   // (the first a commando: the flats always have one)
                new Waypoint(Level.FlatsDoorGuards[i], float.PositiveInfinity, -float.Pi / 2));
            if (_people[^1] is ArmedNpc gunman)
                gunman.HoldsPost = gunman.FollowsUpstairs = true;
        }

        if (_yardPlan is not null)
            BuildYardCrowd(afterRespawn: true);
        for (var i = 0; i < (_raiderPosts?.Length ?? 0); i++)
            AddRaider(i, _raiderPosts![i].At, _raiderPosts[i].Heading, _raiderPosts[i].Plan);
        // An Antwerp raid by boat (after a respawn): the quay's guards, and the boat team on the quay by the ladder.
        if (_quayGuards)
            PlaceQuayGuards();
        for (var i = 0; i < (_boatTeamPosts?.Length ?? 0); i++)
            AddBoatTeamMember(i, _boatTeamPosts![i].At, _boatTeamPosts[i].Heading);
    }

    private readonly bool _factoryRaid;

    private static readonly bool ShackWalk = Environment.GetEnvironmentVariable("BRUTALFPS_SHACKWALK") == "1";

    private (float At, string Name)? _killAt = Environment.GetEnvironmentVariable("BRUTALFPS_KILLAT")?.Split(':', 2) is [var killSeconds, var killName]
        && float.TryParse(killSeconds, System.Globalization.CultureInfo.InvariantCulture, out var killTime) ? (killTime, killName) : null;
    private float? _navHome =float.TryParse(Environment.GetEnvironmentVariable("BRUTALFPS_NAVHOME"), System.Globalization.CultureInfo.InvariantCulture, out var navHome) ? navHome : null;
    private bool _navHomeLogged;

    /// <summary>Both gunmen on the stairwell's door at the flats are dead.</summary>
    public bool FlatsDoorGunmenDown => _model is not null &&!_people.Any(static p => p.Alive && p.Name.StartsWith("door gunman ", StringComparison.Ordinal));

    /// <summary>A respawn (+) brings everyone back as they started: alive, unhurt, on their routes. (Done by <see cref="Update"/>; call it first to have them back sooner.)</summary>
    public void ApplyRespawn(PlayerController player)
    {
        if (_model is null || player.Respawns == _respawns)
            return;
        if (_respawns != int.MinValue)
        {
            ReleaseRagdolls();
            _people.Clear();
            Populate();
            PlayerWanted = false;
            _crossings.Clear();
            Senses.ClearAlerts();
            ClearHidingPlaces();
            Log.Write("[NPC] Respawn: everyone's back, unhurt.");
            MotelReset?.Invoke();
            foreach (var nest in Senses.Nests)
                if (nest.Crewed)
                    nest.Reset();   // (the armoured car's turret: laid at rest, a full belt, for its new gunner)
        }

        _respawns = player.Respawns;
    }

    public void Update(float dt, PlayerController player, in Camera camera)
    {
        if (_model is null)
            return;

        using var updateTimer = Perf.NpcsUpdate.Time();
        ApplyRespawn(player);
        UpdateMotelAmbush(player.Position, player.Alive);
        UpdateStationMarch(dt, player);
        UpdateFactoryDefence(dt, player);   // (Game/NpcsFactoryDefence.cs)
        if (StressTest)
            UpdateStressTest(player);   // (Game/NpcsStressTest.cs)

        _time += dt;
        _frame++;
        Senses.PlayerEye = camera.Position;
        Senses.PlayerProne = player.Prone;
        Senses.PlayerForward = camera.Forward;
        Senses.People = _people;
        Senses.PlayerAlive = player.Alive;
        Senses.PlayerWanted = PlayerWanted;
        Senses.PlayerCrossings = _crossings;
        Senses.Time = _time;
        Senses.CoverSearchesLeft = CoverSearchesPerFrame;
        Senses.NavSearchesLeft = NavSearchesPerFrame;
        Senses.Grenades.Clear();
        Senses.FindGrenades?.Invoke(Senses.Grenades);
        using (Perf.NavFrame.Time()) Senses.Nav?.BeginFrame(_time, dt);
        // Anyone in a firefight shouts it to those of their side near enough to hear (now and then: ArmedNpc.ShoutContact);
        // only they go on the alert, and come looking.
        foreach (var npc in _people)
        {
            if (npc is ArmedNpc { Alive: true, InContact: true, Scripted: false } armed)
                armed.ShoutContact(Senses);
        }

        // Someone's just been killed: their side round them takes it in (morale: ArmedNpc.AllyDown).
        if (Npc.Deaths != _deathsHandled)
        {
            foreach (var npc in _people)
                if (npc.DeathSerial > _deathsHandled)
                    ArmedNpc.AllyDown(npc, Senses);
            _deathsHandled = Npc.Deaths;
        }

        // Anyone kept in a building (Npc.KeepInside) found outside it: say so, once (a gap in the fence: a link, a shove).
        foreach (var npc in _people)
        {
            if (npc is { Alive: true, KeepInside: { } box } && !npc.OutsideWarned
                && (npc.Position.X < box.Min.X || npc.Position.X > box.Max.X || npc.Position.Z < box.Min.Z || npc.Position.Z > box.Max.Z))
            {
                npc.OutsideWarned = true;
                Log.Write($"[WARN] {npc.Name} has got out of their building, at ({npc.Position.X:0.0}, {npc.Position.Z:0.0}) ({npc.State})");
            }
        }

        // Debug: BRUTALFPS_KILLAT="seconds:name" drops that person dead on the spot then, without a sound (to see who finds the body).
        if (_killAt is { } killAt && _time >= killAt.At)
        {
            _killAt = null;
            if (_people.FirstOrDefault(p => p.Name.Equals(killAt.Name, StringComparison.OrdinalIgnoreCase)) is { Alive: true } victim)
            {
                Log.Write($"[NPC] BRUTALFPS_KILLAT: {victim.Name} dropped dead at {victim.Position}");
                victim.ScriptKill(float3.UnitX);
            }
            else
                Log.Write($"[WARN] BRUTALFPS_KILLAT: nobody alive called {killAt.Name}");
        }

        // Debug: BRUTALFPS_NAVHOME=seconds calls everyone off at that time: back the way they came (and log where they end up, 30 s later).
        if (_navHome is { } home && _time >= home)
        {
            _navHome = _navHomeLogged ? null : home + 30f;
            foreach (var npc in _people)
            {
                if (!_navHomeLogged)
                    npc.DebugGoHome();
                else if (npc.Position.Y != npc.Route[0].Position.Y)
                    Log.Write($"[NAV] {npc.Name} is not back on their floor: ({npc.Position.X:0.0}, {npc.Position.Y:0.0}, {npc.Position.Z:0.0}), {npc.State}");
            }

            if (_navHomeLogged)
                Log.Write("[NAV] Home check done");
            _navHomeLogged = true;
        }

        if (ShackWalk && float.Floor(_time * 2f) != float.Floor((_time - dt) * 2f))
        {
            foreach (var npc in _people)
            {
                var door = Level.JungleOrigin + new float3(2.6f, 0, 2.2f);
                if (npc.Alive && (npc.Name == "shack walker" || float3.Distance(npc.Position with { Y = 0 }, door) < 4f))
                {
                    var at = npc.Position - Level.JungleOrigin;
                    Log.Write($"[SHACKWALK] t {_time:0.0} {npc.Name}: ({at.X:0.00}, {at.Z:0.00}) {npc.State}{(Level.BodegaInside(npc.Position) ? ", inside" : "")}");
                }
            }
        }

        UpdatePlayerLight(dt, camera.Position);
        if (ArmedNpc.NestManWanted)
        {
            foreach (var npc in _people)
                (npc as ArmedNpc)?.TestSeat(Senses);
        }

        var active = ActiveZones(camera.Position);
        foreach (var npc in _people)
        {
            // Outside the player's part of the level (and any a portal near them opens onto) nobody does anything at all:
            // they're just someone standing where they were, till the player comes near.
            if (npc.AwakeFor > 0f)
                npc.AwakeFor -= dt;
            else if ((npc.Zone & active) == 0 && npc.UpdatedOnce && !npc.Scripted)
            {
                npc.DormantWait = 0f;
                FrozenSkipped++;
                continue;
            }

            npc.UpdatedOnce = true;
            // How much attention they get this frame (NpcTier). A dormant one saves the time up and is updated now and then.
            var (tier, animInterval, near) = TierOf(npc);
            npc.SetTier(tier, animInterval, near);
            var step = dt;
            if (tier == NpcTier.Dormant)
            {
                npc.DormantWait += dt;
                if (npc.DormantWait < 1f / DormantRate)
                {
                    DormantSkipped++;
                    continue;
                }

                step = npc.DormantWait;
            }

            npc.DormantWait = 0f;
            using (Perf.NpcOne.Time(npc.Name)) npc.Update(step, _model, _walkSpeed, _runSpeed, player.Position, Senses, _ragdolls);
            Bleed(npc, step);
        }

        if (SightDebug && float.Floor(_time) != float.Floor(_time - dt))
            LogSight();
        if (Npc.NavDebug && float.Floor(_time / 5f) != float.Floor((_time - dt) / 5f))
        {
            var ms = Senses.Nav is { Searches: > 0 } nav ? nav.SearchTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / nav.Searches : 0;
            Log.Write($"[NAV] {Npc.DebugPoses} poses near the player: arms swung in {Npc.DebugSwung}, still through a wall in {Npc.DebugThrough} ({Npc.DebugDeep} by over 10 cm, worst {Npc.DebugWorst:0.00} m: {Npc.DebugWorstWhat}); {Npc.DebugStalls} stalls; {Senses.Nav?.Searches} searches at {ms:0.00} ms, worst {Senses.Nav?.WorstSearchMs:0.0} ms");
            Npc.DebugPoses = Npc.DebugSwung = Npc.DebugThrough = Npc.DebugDeep = Npc.DebugStalls = 0;
            Npc.DebugWorst = 0f;
            Npc.DebugWorstWhat = "";
            Senses.Nav?.StartWindow();
        }

        BuildShadows();
    }

    /// <summary>Calm and further than this from the camera (m, less through a scope): <see cref="NpcTier.Far"/>.</summary>
    public const float FarRange = 40f;

    /// <summary>Calm and closer than this to the camera (m, less through a scope): they look round as often as someone searching.</summary>
    public const float NearRange = 15f;

    /// <summary>Updates a second for someone <see cref="NpcTier.Dormant"/> (with the time between saved up for each).</summary>
    public const float DormantRate = 10f;

    /// <summary>m: the player this near a live portal wakes the part of the level it opens onto (<see cref="ActiveZones"/>).</summary>
    public const float PortalWakeRange = 30f;

    /// <summary>s someone just shot is updated for, wherever they are (to fall, or to react).</summary>
    public const float AwakeAfterHit = 10f;

    /// <summary>Updates skipped for people in a shut-down part of the level (<see cref="ActiveZones"/>), for the stress test's log.</summary>
    internal int FrozenSkipped;

    private Zones _activeLogged;

    /// <summary>
    /// The parts of the level where people act this frame: the player's own (with the cellar and outback together, as
    /// they see each other), and the far side of every live portal within <see cref="PortalWakeRange"/> of the player.
    /// Everyone else is frozen where they are.
    /// </summary>
    public Zones ActiveZones(float3 eye)
    {
        var active = Level.ZonesSeenFrom(eye);
        foreach (var portal in Senses.Portals)
        {
            if (portal.Linked is not { } far || float3.DistanceSquared(portal.Center, eye) > PortalWakeRange * PortalWakeRange)
                continue;
            active |= Level.ZoneAt(far.Center + far.Normal + new float3(0, 0.5f, 0));
        }

        if (active != _activeLogged)
        {
            Log.Write($"[NPC] Active zones: {active}");
            _activeLogged = active;
        }

        return active;
    }

    // Posing, for someone in view (over distances through a scope's power): every frame to PoseFullRange, then 30, 15 and
    // 10 times a second. In a part of the level that's in sight but not drawn last frame (behind the camera), 10; in a part
    // nobody can see at all, 4 (kept roughly up to date for stray bullets and for falling as a ragdoll).
    private const float PoseFullRange = 20f, PoseHalfRange = 40f, PoseQuarterRange = 70f;
    private const float PoseOffScreen = 0.1f, PoseOutOfSight = 0.25f;

    /// <summary>BRUTALFPS_PERF=1: updates skipped for dormant people (for the stress test's log).</summary>
    internal int DormantSkipped;

    /// <summary>
    /// Which <see cref="NpcTier"/> someone is in this frame, and how often to pose them: what they're doing (a firefight
    /// with the enemy in sight, something else that isn't calm, or calm), whether their part of the level is in view
    /// (the player's own, or one seen through a portal last frame), whether they were drawn last frame, and how far they
    /// are from the camera. Anyone scripted or on a link (a ladder, a door) is always looked after every frame.
    /// </summary>
    public (NpcTier Tier, float AnimInterval, bool Near) TierOf(Npc npc)
    {
        if (npc.Scripted || npc.OnLink)
            return (NpcTier.Searching, 0f, true);
        var inSight = Relevance.InSight(npc.Zone);
        var zoom = Relevance.Zoom;
        var d2 = Relevance.DistanceSquared(npc.Position, npc.Zone) / (zoom * zoom);
        var drawn = npc.DrawnFrame >= _frame - 1;

        NpcTier tier;
        if (!npc.Alive)
            tier = inSight ? NpcTier.Idle : NpcTier.Dormant;
        else if (npc is ArmedNpc { InContact: true })
            tier = NpcTier.Attacking;
        else if (npc.State != NpcState.Calm)
            tier = NpcTier.Searching;
        else if (!inSight)
            tier = NpcTier.Dormant;
        else if (d2 > FarRange * FarRange)
            tier = NpcTier.Far;
        else
            tier = NpcTier.Idle;

        float anim;
        if (!inSight)
            anim = PoseOutOfSight;
        else if (!drawn)
            anim = PoseOffScreen;
        else if (d2 < PoseFullRange * PoseFullRange)
            anim = 0f;
        else if (d2 < PoseHalfRange * PoseHalfRange)
            anim = 1f / 30f;
        else if (d2 < PoseQuarterRange * PoseQuarterRange)
            anim = 1f / 15f;
        else
            anim = 0.1f;
        if (tier == NpcTier.Far)
            anim = float.Max(anim, 0.1f);
        var near = d2 < NearRange * NearRange;
        return (tier, anim, near);
    }

    // How far a gunshot (at full volume) is loud, and how far it carries at all: in the open, and with a wall in the way.
    private const float OpenLoud = 60f, OpenCarries = 95f, WalledLoud = 22f, WalledCarries = 45f;
    private const float NearMissRadius = 2.5f;  // m: a round passing this close to someone is suppressing fire
    private int _deathsHandled;                 // the last Npc.DeathSerial whose side has taken it in

    // The player's reload: heard this far in the open (through a wall, half), and seen by anyone in a fight looking at them this far.
    private const float ReloadHeard = 9f, ReloadSeen = 30f;

    /// <summary>
    /// The player has started to reload at <paramref name="at"/> (their eye): anyone fighting them who can hear the
    /// magazine come out, or see them at it, knows they've a moment (<see cref="ArmedNpc.PlayerReloading"/>).
    /// </summary>
    public void PlayerReloading(float3 at)
    {
        var zone = Level.ZoneAt(at);
        foreach (var npc in _people)
        {
            if (npc is not ArmedNpc { Alive: true, Scripted: false } armed || npc.Zone != zone || npc.State != NpcState.Fighting)
                continue;
            var ear = npc.Position + new float3(0, Npc.EyeHeight, 0);
            var dist = float3.Distance(ear, at);
            if (dist > ReloadSeen)
                continue;
            var open = Senses.Clear(ear, at) > 0.99f;
            var heard = dist < (open ? ReloadHeard : ReloadHeard * 0.5f);
            var seen = open && armed.SeesPlayerNow;
            if (heard || seen)
                armed.PlayerReloading(Senses, seen ? "saw" : "heard");
        }
    }
    private const int CoverSearchesPerFrame = 2;
    private const int NavSearchesPerFrame = 3;
    private const float LightEvery = 0.2f;      // s between looks at the light on the player
    private float _lightLeft;
    private float _lightLogged = -1f;

    /// <summary>A few times a second: how much light is on the player now? (Logged when it changes much.)</summary>
    private void UpdatePlayerLight(float dt, float3 eye)
    {
        _lightLeft -= dt;
        if (_lightLeft > 0f || Senses.LightAt is not { } lightAt)
            return;
        _lightLeft = LightEvery;
        using (Perf.PlayerLight.Time()) Senses.PlayerLight = lightAt(eye - new float3(0, 0.35f, 0));
        if (float.Abs(Senses.PlayerLight - _lightLogged) < 0.1f)
            return;
        _lightLogged = Senses.PlayerLight;
        Log.Write($"[SIGHT] Light on the player {Senses.PlayerLight:0.00}: they can be made out from {Senses.PlayerSightRange:0} m, a muzzle flash from {Senses.FlashSightRange:0} m");
    }

    /// <summary>Debug: BRUTALFPS_SIGHTDEBUG=1 logs, once a second, what stands between each armed NPC nearby and the player.</summary>
    private static readonly bool SightDebug = Environment.GetEnvironmentVariable("BRUTALFPS_SIGHTDEBUG") == "1";

    private void LogSight()
    {
        var zone = Level.ZoneAt(Senses.PlayerEye);
        foreach (var npc in _people)
        {
            if (npc is not ArmedNpc armed || !npc.Alive || npc.Zone != zone)
                continue;
            var eye = npc.Position + new float3(0, Npc.EyeHeight, 0);
            var d = Senses.PlayerEye - eye;
            var dist = float.Sqrt(float3.Dot(d, d));
            if (dist > Senses.PlayerSightLimit(eye))
                continue;
            var walls = Senses.Clear(eye, Senses.PlayerEye);
            var leaves = Senses.LeavesBetween(eye, Senses.PlayerEye, out _);
            var seen = Senses.SeePlayer(eye, Senses.PlayerEye);
            Log.Write($"[SIGHT] {npc.Name}: {dist:0.0} m, {npc.State.ToString().ToLowerInvariant()} ({armed.Tactic.ToString().ToLowerInvariant()}), " +
                      $"walls {(walls > 0.99f ? "clear" : $"blocked at {walls:0.00}")}, leaves {leaves:0.00}, light {Senses.PlayerLight:0.00} -> {(seen > 0 ? $"visible {seen:0.00}" : "hidden")}");
        }
    }

    /// <summary>Takes every body out of the physics world (on a respawn, and before the world itself goes at exit).</summary>
    public void ReleaseRagdolls()
    {
        foreach (var npc in _people)
            npc.ReleaseRagdoll();
    }

    /// <summary>
    /// A gunshot at <paramref name="at"/>: everyone in that part of the level within earshot hears it, after the time
    /// sound takes to reach them (plus a moment to react). A wall in between muffles it: it carries less far, and from
    /// further off it only makes them wary. <paramref name="byPlayer"/>: the player fired it, so anyone who's after the
    /// player works out what to do about it from what they can see (<see cref="ArmedNpc"/>: into cover, or fire at the flash).
    /// <paramref name="shooter"/>: someone here fired it, so their own side knows it for a friend's gun (<see cref="Npc.Recognises"/>).
    /// </summary>
    /// <param name="heavy">A machine gun (the M249): sustained fire from it makes armed people get their heads down (<see cref="ArmedNpc.HeardHeavyFire"/>).</param>
    /// <param name="volume">How loud the gun is (1: as most; a suppressor brings it down): it carries that much less far.</param>
    public void HearGunshot(float3 at, bool byPlayer = false, Npc? shooter = null, bool heavy = false, float volume = 1f)
    {
        using var timer = Perf.HearGunshot.Time();
        // (The shot's sound is played by Weapon through the AudioEngine.)
        var zone = Level.ZoneAt(at);
        if (zone == Zones.Warehouse)
            FactoryGunshots++;
        foreach (var npc in _people)
        {
            if (!npc.Alive || npc.Zone != zone || ReferenceEquals(npc, shooter))
                continue;
            var ear = npc.Position + new float3(0, Npc.EyeHeight, 0);
            var d = ear - at;
            var dist = float.Sqrt(float3.Dot(d, d));
            if (dist > OpenCarries * volume)
                continue;   // (out of earshot even in the open: no need to look for walls)
            var delay = 0.1f + dist / 343f + 0.3f * (float)_random.NextDouble();
            var hostileHeavy = heavy && npc is ArmedNpc && (shooter is null || !npc.Recognises(shooter));
            // The ray (is there a wall between?) only tells a loud shot from a distant one, and how far it carries: skip it
            // when that can't change anything. Inside the walled-in "loud" distance it's loud either way; and when one that
            // reaches them sooner is already on its way, this one won't count. (A volley of a hundred shots was 10,000 rays.)
            bool open;
            if (dist <= WalledLoud * volume && !hostileHeavy)
                open = false;
            else if (!hostileHeavy && (!npc.WouldHearShot(delay, byPlayer, shooter) || !npc.TakeHearingRay(_frame)))
                continue;   // (a dozen guns going off in the same frame are one wall of sound: each listener makes out the first few)
            else
                open = Senses.Clear(ear, at) > 0.99f;
            var (loud, carries) = open ? (OpenLoud * volume, OpenCarries * volume) : (WalledLoud * volume, WalledCarries * volume);
            if (dist > carries)
                continue;
            npc.Hear(at, dist <= loud ? Stimulus.Gunshot : Stimulus.DistantShot, delay, byPlayer, shooter);
            if (hostileHeavy)
                ((ArmedNpc)npc).HeardHeavyFire(at, dist / loud, byPlayer, _time);
        }
    }

    /// <summary>
    /// A round flew from <paramref name="start"/> to <paramref name="end"/> this frame (fired from <paramref name="origin"/>):
    /// anyone armed it passed close to, or struck beside, feels it (<see cref="ArmedNpc.Suppress"/>). Their own side's
    /// rounds don't count: they know where those are going.
    /// </summary>
    public void BulletPassed(float3 start, float3 end, float3 origin, bool byPlayer, Npc? firedBy)
    {
        var path = end - start;
        var length2 = float3.Dot(path, path);
        if (length2 < 1e-6f)
            return;
        var zone = Level.ZoneAt(start);
        foreach (var npc in _people)
        {
            if (npc is not ArmedNpc { Alive: true } armed || npc.Hidden || npc.Scripted || npc.Zone != zone || ReferenceEquals(npc, firedBy)
                || (firedBy is not null && armed.Recognises(firedBy)))
                continue;
            var chest = npc.Position + new float3(0, 1.3f, 0);
            var t = float.Clamp(float3.Dot(chest - start, path) / length2, 0f, 1f);
            var miss = chest - (start + path * t);
            var d2 = float3.Dot(miss, miss);
            if (d2 > NearMissRadius * NearMissRadius || float3.Dot(chest - origin, chest - origin) < 4f)
                continue;
            armed.NearMiss(float.Sqrt(d2) / NearMissRadius, origin, byPlayer, _time);
        }
    }

    /// <summary>
    /// A noise at <paramref name="at"/> that carries <paramref name="carries"/> m in the open (footsteps, a bullet
    /// striking, a body falling): people in that part of the level within earshot hear it once the sound has reached
    /// them. Through a wall it carries 40% as far. It isn't frightening by itself: they turn to look, and an armed one
    /// may come to see (<see cref="Npc.HearNoise"/>). Closer than <paramref name="frightens"/> m (in the open) it's as
    /// bad as a gunshot. <paramref name="loud"/>: something that can't be anything innocent (not a footstep).
    /// <paramref name="firedBy"/>: it's that person's bullet striking; their own side heard the shot and pays it no mind.
    /// </summary>
    public void HearNoise(float3 at, float carries, bool byPlayer, bool loud, float frightens = 0f, Npc? firedBy = null)
    {
        if (carries <= 0f)
            return;
        var zone = Level.ZoneAt(at);
        foreach (var npc in _people)
        {
            if (!npc.Alive || npc.Zone != zone || (firedBy is not null && npc.Recognises(firedBy)))
                continue;
            var ear = npc.Position + new float3(0, Npc.EyeHeight, 0);
            var d = ear - at;
            var dist = float.Sqrt(float3.Dot(d, d));
            if (dist > carries)
                continue;
            var lift = new float3(0, 0.3f, 0);   // (a step's sound starts at the floor: don't let the floor block it)
            var open = Senses.Clear(ear, at + lift) > 0.99f;
            if (!open && dist > carries * 0.4f)
                continue;
            var delay = 0.1f + dist / 343f + 0.25f * (float)_random.NextDouble();
            if (open && dist < frightens)
                npc.Hear(at, Stimulus.Gunshot, delay);
            else
                npc.HearNoise(at, byPlayer, loud, delay);
        }
    }

    /// <summary>
    /// A flashbang going off at <paramref name="at"/>: everyone near enough in that part of the level is dazzled (by
    /// how close it is, whether they can see it and whether they were looking its way: <see cref="Flashbangs.Effect"/>)
    /// and deafened; further off, it's a bang like a gunshot. Thrown by the player, dazzling a civilian or an officer
    /// makes them wanted, as shooting one does.
    /// </summary>
    public void Flashbang(float3 at, bool byPlayer)
    {
        var zone = Level.ZoneAt(at);
        foreach (var npc in _people)
        {
            if (!npc.Alive || npc.Zone != zone)
                continue;
            var eye = npc.Position + new float3(0, Npc.EyeHeight, 0);
            var to = at - eye;
            var dist = float.Sqrt(float3.Dot(to, to));
            var ahead = new float3(float.Sin(npc.Heading), 0, float.Cos(npc.Heading));
            var facing = dist > 1e-3f ? float3.Dot(to / dist, ahead) : 1f;
            var (white, deaf) = Flashbangs.Effect(dist, facing, Senses.Clear(eye, at) > 0.99f);
            var blindFor = white > 0.05f ? 1f + 5f * white : 0f;
            var deafFor = deaf > 0.05f ? 1f + 5f * deaf : 0f;
            if (blindFor <= 0f && deafFor <= 0f)
                continue;
            if (byPlayer && blindFor > 0f && npc.Faction is Faction.Civilian or Faction.Police && !PlayerWanted)
            {
                PlayerWanted = true;
                Log.Write($"[NPC] The player flashbanged {npc.Name} ({npc.Faction.ToString().ToLowerInvariant()}): the police are after them");
            }

            npc.Flashbanged(at, blindFor, deafFor);
        }

        // Everyone else in earshot hears a bang (those deafened by it don't).
        HearGunshot(at);
    }

    /// <summary>
    /// A fragmentation grenade going off at <paramref name="at"/>: everyone in that part of the level gets
    /// <paramref name="damageAt"/>(distance, in sight) health taken (0 = untouched), with a wound where the blast meets
    /// them and a shove away from it (a body is thrown); those near enough who live through it are knocked senseless and
    /// deafened for a few seconds. Then it's a bang everyone else hears. Thrown by the player, hurting a civilian or an
    /// officer makes them wanted, as shooting one does.
    /// </summary>
    public void Blast(float3 at, Func<float, bool, float> damageAt, float stunReach, bool byPlayer)
    {
        var zone = Level.ZoneAt(at);
        var from = at + new float3(0, 0.1f, 0);
        foreach (var npc in _people)
        {
            if (npc.Hidden || npc.Zone != zone)
                continue;
            // The nearest point of them to the blast: along the body, from the shins to the head.
            var axisY = float.Clamp(from.Y - npc.Position.Y, 0.25f, 1.6f);
            var target = npc.Position + new float3(0, axisY, 0);
            var to = target - from;
            var dist = float.Sqrt(float3.Dot(to, to));
            var inSight = Senses.Clear(from, target) > 0.95f;
            var damage = damageAt(dist, inSight);
            if (damage <= 0.5f)
                continue;
            var direction = dist > 1e-3f ? to / dist : float3.UnitY;
            if (byPlayer && npc.Alive && npc.Faction is Faction.Civilian or Faction.Police && !PlayerWanted)
            {
                PlayerWanted = true;
                Log.Write($"[NPC] The player blew up {npc.Name} ({npc.Faction.ToString().ToLowerInvariant()}): the police are after them");
            }

            var wasAlive = npc.Alive;
            var before = npc.Health;
            // The wound: where a line from the blast first meets their skin (or, failing that, the chest, unmarked).
            var wound = new Wound(float3.Zero, 0f, 0, 0f, _time, false);
            var part = BodyPart.Torso;
            if (_model is not null)
            {
                var posed = npc.Posed(_model, _frame);
                if (_model.Collision.Raycast(posed, from, direction, dist + 1f, out var skin))
                {
                    part = PartOf(skin.Joint);
                    wound = new Wound(skin.BindPosition, 0.009f, skin.Joint, (float)(_random.NextDouble() * 100), _time, false);
                }
            }

            var bleed = BleedRate(part) * float.Clamp(damage / 60f, 0.3f, 2f);
            if (npc.Profile.ArmourCovers(part, wound.BindPosition))
            {
                damage *= float.Max(NpcProfile.ArmourDamage, 0.6f);   // (a plate stops some fragments, not the blast)
                bleed *= NpcProfile.ArmourBleed;
            }

            var flinch = part == BodyPart.Head ? _flinchHead : _flinchTorso;
            npc.TakeHit(wound, part, direction, at, flinch.Nodes, flinch.Pivot, damage, bleed);
            // More fragments: wounds (and, on a body, more of a shove), but no more health than the blast took.
            if (_model is not null && dist < 3.5f)
            {
                var posed = npc.Posed(_model, _frame);
                for (var k = 0; k < 3; k++)
                {
                    var stray = float3.Normalize(direction + Particles.RandomDirection() * 0.25f);
                    if (_model.Collision.Raycast(posed, from, stray, dist + 1f, out var skin))
                        npc.TakeHit(new Wound(skin.BindPosition, 0.006f, skin.Joint, (float)(_random.NextDouble() * 100), _time, false), PartOf(skin.Joint),
                            stray, at, [], 0, bleed: wasAlive ? bleed * 0.3f : 0f);
                }
            }

            if (npc is ArmedNpc { Alive: true } armed)
                armed.WasHit(at, _time);
            if (npc.Alive && dist < stunReach)
            {
                var near = 1f - dist / stunReach;
                npc.Flashbanged(at, 1f + 2.5f * near, 2f + 4f * near);   // knocked senseless, ears ringing
            }

            Log.Write(wasAlive
                ? $"[NPC] {npc.Name} caught by a grenade {dist:0.0} m off{(inSight ? "" : " (round a corner)")}: -{before - float.Max(npc.Health, 0f):0} HP, " +
                  $"{float.Max(npc.Health, 0f):0} left{(npc.Alive ? "" : " (killed)")}"
                : $"[NPC] {npc.Name}'s body thrown by a grenade {dist:0.0} m off");
        }

        // Everyone in earshot hears it (those deafened by it don't).
        HearGunshot(at, byPlayer, heavy: true);
    }

    /// <summary>Fresh wounds drip: now and then a drop falls from each, less often as it gets older.</summary>
    private void Bleed(Npc npc, float dt)
    {
        foreach (var w in npc.Wounds)
        {
            var age = _time - w.Time;
            if (age > 30f)
                continue;
            var rate = (w.Exit ? 1.4f : 0.9f) * (1f - age / 30f);
            if (_random.NextDouble() < rate * dt)
                _effects.BloodDrip(npc.WoundPosition(w), new float3(0, -0.2f, 0));
        }
    }

    /// <summary>
    /// The first person the ray (from <paramref name="origin"/> along unit <paramref name="direction"/>) hits within
    /// <paramref name="maxDistance"/>, tested against their skin as posed this frame.
    /// </summary>
    /// <param name="fromNpc">It's another NPC's bullet: it passes through anyone <see cref="NpcProfile.Spared"/>.</param>
    /// <param name="ignore">Someone the ray can't hit (the shooter, for their own bullet).</param>
    public bool Raycast(float3 origin, float3 direction, float maxDistance, out NpcHit hit, bool fromNpc = false, Npc? ignore = null)
    {
        hit = default;
        if (_model is null)
            return false;
        var best = maxDistance;
        foreach (var npc in _people)
        {
            // Broad phase: a cylinder round them.
            // (Lying dead they can stretch out up to a body's length from where they stood.)
            if (npc.Hidden || ReferenceEquals(npc, ignore) || (fromNpc && npc.Profile.Spared) || !RayHitsCylinder(origin, direction, best, npc.Position, npc.Alive ? 0.75f : 2.1f, 2.0f))
                continue;
            var posed = npc.Posed(_model, _frame);
            if (_model.Collision.Raycast(posed, origin, direction, best, out var skin))
            {
                best = skin.Distance;
                hit = new NpcHit(npc, skin, PartOf(skin.Joint));
            }
        }

        return hit.Npc is not null;
    }

    private BodyPart PartOf(int joint)
    {
        var name = _model!.Skeleton.NodeName(_model.Skeleton.JointNode(joint));
        if (name.StartsWith("head") || name.StartsWith("neck") || name.StartsWith("eye"))
            return BodyPart.Head;
        if (name.StartsWith("thigh") || name.StartsWith("calf") || name.StartsWith("foot") || name.StartsWith("ball"))
            return BodyPart.Leg;
        if (name.StartsWith("pelvis") || name.StartsWith("spine") || name.StartsWith("clavicle") || name.StartsWith("root"))
            return BodyPart.Torso;
        return BodyPart.Arm;
    }

    /// <summary>
    /// A bullet going along <paramref name="direction"/> hit someone: the entry wound and its reaction. If the bullet
    /// went through (<paramref name="exitDistance"/> along the same ray from <paramref name="origin"/>), the exit wound
    /// too. <paramref name="from"/> is roughly where it was fired from. <paramref name="punch"/> and
    /// <paramref name="woundScale"/> are how hard it hit and how wide it is, against a 9 mm round.
    /// </summary>
    /// <param name="firedBy">The NPC whose bullet it was: if that's a friend, the one hit doesn't take them for the enemy.</param>
    /// <param name="pierced">It went through their body armour (a .50 close in): the plate takes nothing off it.</param>
    public void Hit(in NpcHit hit, float3 origin, float3 direction, float3 from, float exitDistance, bool byPlayer = true, float punch = 1f, float woundScale = 1f, Npc? firedBy = null, bool pierced = false)
    {
        if (byPlayer && hit.Npc.Alive && hit.Npc.Faction is Faction.Civilian or Faction.Police && !PlayerWanted)
        {
            PlayerWanted = true;
            Log.Write($"[NPC] The player shot {hit.Npc.Name} ({hit.Npc.Faction.ToString().ToLowerInvariant()}): the police are after them");
        }

        var npc = hit.Npc;
        var flinch = hit.Part switch
        {
            BodyPart.Head => _flinchHead,
            BodyPart.Arm => _model!.Skeleton.NodeName(_model.Skeleton.JointNode(hit.Skin.Joint)).EndsWith(".L") ? _flinchArmL : _flinchArmR,
            _ => _flinchTorso,
        };
        var seed = (float)(_random.NextDouble() * 100);
        var through = float.IsFinite(exitDistance);
        var wasAlive = npc.Alive;
        var before = npc.Health;
        // A round that goes right through leaves some of its energy behind it.
        var damage = BaseDamage(hit.Part) * punch * (0.85f + 0.3f * (float)_random.NextDouble()) * (through ? 0.85f : 1f);
        var bleed = BleedRate(hit.Part);
        if (!pierced && npc.Profile.ArmourCovers(hit.Part, hit.Skin.BindPosition))   // (pierced: the round went through the plate)
        {
            damage *= NpcProfile.ArmourDamage;
            bleed *= NpcProfile.ArmourBleed;
        }

        // A turret's gunner is mostly hull and armour: what does reach them is a glancing hit, as often as not (Game/NpcGunner.cs).
        if (npc is ArmedNpc { TurretCrew: true })
        {
            damage *= ArmedNpc.CrewDamage;
            bleed *= ArmedNpc.CrewDamage;
        }

        npc.TakeHit(new Wound(hit.Skin.BindPosition, 0.0048f * woundScale, hit.Skin.Joint, seed, _time, false), hit.Part, direction, from,
            flinch.Nodes, flinch.Pivot, damage, bleed, firedBy);
        if (npc is ArmedNpc { Alive: true } shotAt && (firedBy is null || !shotAt.Recognises(firedBy)))
            shotAt.WasHit(from, _time);

        if (through)
        {
            // Find the exit's place on the skin: the first surface met coming back from just beyond it.
            var beyond = origin + direction * (exitDistance + 0.05f);
            var posed = npc.Posed(_model!, _frame);
            if (_model!.Collision.Raycast(posed, beyond, -direction, 0.1f, out var back))
                npc.TakeHit(new Wound(back.BindPosition, 0.0075f * woundScale, back.Joint, seed + 13.7f, _time, true), hit.Part, direction, from, [], 0,
                    bleed: wasAlive ? bleed * 0.5f : 0f);
        }

        Log.Write(wasAlive
            ? $"[NPC] {npc.Name} hit in the {hit.Part.ToString().ToLowerInvariant()}{(through ? " (through and through)" : "")}: " +
              $"-{before - float.Max(npc.Health, 0f):0} HP, {float.Max(npc.Health, 0f):0} left, {npc.State.ToString().ToLowerInvariant()}"
            : $"[NPC] {npc.Name}'s body hit in the {hit.Part.ToString().ToLowerInvariant()} ({npc.Hits} hits)");
    }

    /// <summary>Health a blow from a baton takes, by where it lands: two to the head will kill, a body takes several.</summary>
    public static float BluntDamage(BodyPart part) => part switch
    {
        BodyPart.Head => 55f,
        BodyPart.Torso => 22f,
        BodyPart.Leg => 16f,
        _ => 14f,
    };

    /// <summary>A baton struck someone: they flinch and lose health (less through a plate), but there's no wound and no bleeding.</summary>
    public void Strike(in NpcHit hit, float3 direction, float3 from)
    {
        var npc = hit.Npc;
        if (npc.Alive && npc.Faction is Faction.Civilian or Faction.Police && !PlayerWanted)
        {
            PlayerWanted = true;
            Log.Write($"[NPC] The player struck {npc.Name} ({npc.Faction.ToString().ToLowerInvariant()}): the police are after them");
        }

        var flinch = hit.Part switch
        {
            BodyPart.Head => _flinchHead,
            BodyPart.Arm => _model!.Skeleton.NodeName(_model.Skeleton.JointNode(hit.Skin.Joint)).EndsWith(".L") ? _flinchArmL : _flinchArmR,
            _ => _flinchTorso,
        };
        var wasAlive = npc.Alive;
        var damage = BluntDamage(hit.Part) * (0.85f + 0.3f * (float)_random.NextDouble());
        if (npc.Profile.ArmourCovers(hit.Part, hit.Skin.BindPosition))
            damage *= NpcProfile.ArmourDamage;
        npc.TakeHit(new Wound(hit.Skin.BindPosition, 0f, hit.Skin.Joint, 0f, _time, false), hit.Part, direction, from, flinch.Nodes, flinch.Pivot, damage);
        Log.Write(wasAlive
            ? $"[NPC] {npc.Name} struck in the {hit.Part.ToString().ToLowerInvariant()}: -{damage:0} HP, {float.Max(npc.Health, 0f):0} left, {npc.State.ToString().ToLowerInvariant()}"
            : $"[NPC] {npc.Name}'s body struck in the {hit.Part.ToString().ToLowerInvariant()}");
    }

    /// <summary>Keeps the player out of everyone (a cylinder each).</summary>
    public void Collide(PlayerController player)
    {
        foreach (var npc in _people)
        {
            if (!npc.Alive || npc.Hidden)
                continue;   // you can step over the dead
            if (player.Position.Y > npc.Position.Y + Npc.BodyHeight || player.Position.Y + PlayerController.Height < npc.Position.Y)
                continue;
            var d = player.Position - npc.Position;
            d.Y = 0;
            var dist = float.Sqrt(float3.Dot(d, d));
            var min = Npc.BodyRadius + PlayerController.Radius;
            if (dist >= min)
                continue;
            var push = dist > 1e-4f ? d / dist : new float3(0, 0, 1);
            player.Nudge(push * (min - dist)); // not into walls: a shove mustn't put you inside something solid
            var into = float3.Dot(player.Velocity, push);
            if (into < 0)
                player.Velocity -= push * into;
        }
    }

    private static bool RayHitsCylinder(float3 origin, float3 direction, float maxDistance, float3 basePoint, float radius, float height)
    {
        // Closest approach in the horizontal plane, then the height at those points.
        var o = new float2(origin.X - basePoint.X, origin.Z - basePoint.Z);
        var d = new float2(direction.X, direction.Z);
        var a = float2.Dot(d, d);
        float t0 = 0, t1 = maxDistance;
        if (a > 1e-8f)
        {
            var b = float2.Dot(o, d);
            var c = float2.Dot(o, o) - radius * radius;
            var disc = b * b - a * c;
            if (disc < 0)
                return false;
            var sq = float.Sqrt(disc);
            t0 = float.Max(t0, (-b - sq) / a);
            t1 = float.Min(t1, (-b + sq) / a);
            if (t0 > t1)
                return false;
        }
        else if (float2.Dot(o, o) > radius * radius)
        {
            return false;
        }

        var y0 = origin.Y + direction.Y * t0 - basePoint.Y;
        var y1 = origin.Y + direction.Y * t1 - basePoint.Y;
        return float.Max(y0, y1) >= 0 && float.Min(y0, y1) <= height;
    }

    /// <summary>Draws the people in <paramref name="zones"/>, each at the level of detail its distance from the camera calls for.</summary>
    public void Draw(Renderer renderer, Zones zones, bool mainView)
    {
        using var timer = Perf.NpcsDraw.Time();
        Relevance.Drawn(zones);
        if (_model is null)
            return;
        if (mainView)
            Array.Clear(LodCounts);
        var eye = renderer.CameraPosition;
        foreach (var npc in _people)
        {
            if ((zones & npc.Zone) == 0 || npc.Hidden)
                continue;
            // (Lying dead they can stretch out up to a body's length from where they stood.)
            if (!renderer.InView(new Aabb(npc.Position - new float3(2.5f, 1f, 2.5f), npc.Position + new float3(2.5f, 3f, 2.5f))))
                continue;
            npc.DrawnFrame = _frame;
            var d = npc.Position + new float3(0, 1f, 0) - eye;
            var dist = float.Sqrt(float3.Dot(d, d));
            var lod = 0;
            while (lod < LodDistances.Length && dist > LodDistances[lod])
                lod++;
            if (mainView)
                LodCounts[lod]++;
            var count = int.Min(npc.Wounds.Count, Renderer.MaxWounds);
            for (var i = 0; i < count; i++)
            {
                var w = npc.Wounds[i];
                _woundData[i] = new float4(w.BindPosition, w.Radius);
                _woundInfo[i] = new float4(_time - w.Time, w.Seed, w.Exit ? 1 : 0, 0);
            }

            var world = npc.Transform;
            var wounds = _woundData.AsSpan(0, count);
            var info = _woundInfo.AsSpan(0, count);
            var outfit = npc.Outfit;
            var bodyHeat = npc.Alive ? 1f : 0.6f;   // (in a thermal scope, the dead show cooler)
            renderer.DrawSkinned(_model, lod, world, npc.Skin, npc.SkinTone, wounds, info, hideRegions: outfit.HideRegions, bodyHeat: bodyHeat);
            for (var k = 0; k < outfit.Items.Count; k++)
            {
                var item = outfit.Items[k];
                renderer.DrawSkinned(item.Model, lod, world, npc.Skin, item.Tint, wounds, info, hideRegions: item.HideBelow, woundStyle: item.Wound, bodyHeat: bodyHeat);
            }

            if (npc.Equipment == Equipment.Baton && npc.Alive && _handR >= 0 && lod < LodDistances.Length)
            {
                var (grip, shaft) = Baton.Meshes();
                var batonToWorld = Baton.InFist(npc.NodeWorld[_handR] * world);
                renderer.DrawMesh(grip, batonToWorld, new float3(0.035f, 0.035f, 0.038f), ShadeMode.Lit, Material.Polymer);
                renderer.DrawMesh(shaft, batonToWorld, new float3(0.055f, 0.056f, 0.06f), ShadeMode.Lit, Material.Polymer);
            }

            if (npc.HeadGear is { } accent && _headNode >= 0 && lod < LodDistances.Length)
                NpcHeadGear.Draw(renderer, npc.NodeWorld[_headNode] * world, accent, npc.GogglesDown);

            // The player's own side carry an infrared marker on the back of the helmet: unseen by eye, it shows through
            // night vision or thermal (only then is it drawn), so a friend in the dark isn't taken for the enemy.
            if (renderer.Vision != ScopeVision.Off && npc is ArmedNpc && npc.Profile.FriendlyToPlayer && _headNode >= 0)
                NpcHeadGear.DrawMarker(renderer, npc.NodeWorld[_headNode] * world, dist, _time + npc.Name.Length * 0.37f);

            if (npc is { Sitting: true, Alive: true } && _handL >= 0 && lod < LodDistances.Length - 1)
                DrawCardsInHand(renderer, npc.NodeWorld[_handL] * world);

            if (npc is ArmedNpc { GunVisible: true } armed && (GunModels.TryGetValue(armed.Equipment, out var gunModel) || GunModels.TryGetValue(NpcGun.Fallback(armed.Equipment), out gunModel)) && lod < LodDistances.Length)
            {
                var gun = armed.GunToWorld;
                foreach (var part in gunModel.Parts)
                {
                    if (!NotOnNpcGun.Contains(part.Name))
                        renderer.DrawPart(part, gun, float3.One);
                }

                if (armed.Flash > 0)
                {
                    var size = armed.GunInUse.FlashSize * (1f + armed.Flash);
                    renderer.DrawCube(float4x4.CreateScale(size) * float4x4.CreateTranslation(armed.Muzzle), new float3(5f, 3.2f, 1.4f) * armed.Flash,
                        ShadeMode.Unlit);
                }
            }
        }
    }

    public void DrawShadows(Renderer renderer) => renderer.DrawDecals(_shadows);

    /// <summary>A soft pool under each body and a smaller one under each foot, darker the nearer the foot is to the ground.</summary>
    private void BuildShadows()
    {
        using var timer = Perf.NpcShadows.Time();
        _shadows.Clear();
        foreach (var npc in _people)
        {
            if (npc.Hidden)
                continue;
            var m = npc.Transform;
            var world = npc.NodeWorld;
            var ground = npc.Position.Y + 0.012f;
            var fwd = new float3(float.Sin(npc.Heading), 0, float.Cos(npc.Heading));
            var pelvis = float3.Transform(world[_pelvis].Translation, m);
            Quad(new float3(pelvis.X, ground, pelvis.Z), fwd, 0.30f, 0.40f, 0.42f);
            foreach (var (foot, ball) in (ReadOnlySpan<(int, int)>)[(_footL, _ballL), (_footR, _ballR)])
            {
                var a = float3.Transform(world[foot].Translation, m);
                var b = float3.Transform(world[ball].Translation, m);
                var dir = new float3(b.X - a.X, 0, b.Z - a.Z);
                var len = float.Sqrt(float3.Dot(dir, dir));
                dir = len > 1e-4f ? dir / len : fwd;
                var lift = float.Clamp((float.Min(a.Y - 0.08f, b.Y - 0.02f) - ground) / 0.12f, 0f, 1f);
                var mid = (a + b) * 0.5f + dir * 0.03f;
                Quad(new float3(mid.X, ground, mid.Z), dir, 0.17f, 0.075f, 0.75f * (1f - lift));
            }
        }
    }

    /// <summary>A contact-shadow decal (kind 15): centred at <paramref name="c"/>, half length along <paramref name="along"/>, half width across.</summary>
    private void Quad(float3 c, float3 along, float halfLength, float halfWidth, float opacity)
    {
        if (opacity <= 0.01f)
            return;
        var x = along * halfLength;
        var z = new float3(-along.Z, 0, along.X) * halfWidth;
        var n = float3.UnitY;
        var t = new float4(along, 15f + 0.05f + 0.9f * float.Clamp(opacity, 0f, 1f));
        var p00 = c - x - z;
        var p10 = c + x - z;
        var p11 = c + x + z;
        var p01 = c - x + z;
        _shadows.Vertex(p00, n, new float2(-1, -1), t);
        _shadows.Vertex(p11, n, new float2(1, 1), t);
        _shadows.Vertex(p10, n, new float2(1, -1), t);
        _shadows.Vertex(p00, n, new float2(-1, -1), t);
        _shadows.Vertex(p01, n, new float2(-1, 1), t);
        _shadows.Vertex(p11, n, new float2(1, 1), t);
    }
}
