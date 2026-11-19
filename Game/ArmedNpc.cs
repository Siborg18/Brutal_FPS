using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// An <see cref="Npc"/> with a gun who is willing to use it (<see cref="NpcProfile.Fights"/>): a gunman, or an officer.
/// <para>
/// Everything a bystander does (routes, idles, wounds, bleeding, death) is the base class's. What's added:
/// <list type="bullet">
/// <item>Senses: enemies (by <see cref="Factions"/>) in sight set them off; an officer isn't frightened by the player's
/// gun unless the player is wanted; allies fighting or shot nearby bring them in.</item>
/// <item><see cref="NpcState.Fighting"/>: the AimPistol overlay (upper body, so they can aim while walking) on top of
/// the locomotion; turn to the target, close in if it's far, take a moment to line up, then fire at a rate, with a
/// cone that depends on <see cref="NpcProfile.Accuracy"/> and distance. FirePistol plays the recoil. A magazine runs out
/// and they lower the gun to reload.</item>
/// <item>Losing sight (a wall, or the enemy slipping into the leaves, <see cref="NpcSenses.Visibility"/>): by
/// <see cref="NpcProfile.OnLostSight"/>, a searcher goes to where the enemy was last seen (a little on, the way they
/// were going), looks round, sweeps a few spots nearby and gives up after a while; a holder stays put with the gun on
/// the spot where they vanished and fires the moment they show again. A gunshot heard meanwhile is a new lead. Giving
/// up, they walk back the way they came (Returning) and carry on.</item>
/// <item>The player slipping through a portal: a searcher who saw them vanish near it goes after them, through it (and
/// on through any others the player takes from there), and hunts on the far side; giving up, they walk back through
/// the same portals to their route.</item>
/// <item>Looking through portals: they see the player through one (<see cref="NpcSenses.ThroughPortal"/>), but the water
/// stops bullets, so they don't fire: a searcher goes through after them, a holder keeps the gun on the water. Searching,
/// they also go and look into the portals near where the enemy vanished.</item>
/// <item>A <see cref="NpcStrategy"/>: pushing the enemy, covering an ally, or holding back; it sets how they move in a
/// fight and what they do on losing sight.</item>
/// <item>Their <see cref="Aggression"/>: a defensive one holds their ground and shoots whoever comes in range; a cautious
/// one moves up slowly, stopping to fire, and searches carefully; an aggressive one goes by their strategy.</item>
/// </list>
/// The gun is drawn in the right hand (<see cref="GunToWorld"/>, set from the hand bone each frame).
/// </para>
/// </summary>
public sealed partial class ArmedNpc : Npc
{
    // Fighting.
    private const float RetargetEvery = 0.25f;  // s between looks for the best target
    private const float EngageRange = 30f;      // m: they'll shoot this far
    private const float CloseInShare = 0.45f;   // of their gun's range: further than this, they walk in while aiming (a shotgun 9 m, a pistol 18, a rifle 36-43)
    private const float CalmLookOut = 0.5f;     // of their gun's range: how far they make the player out, not on the alert (at least FullSight)
    private const float SearchTime = 28f;       // s of searching without seeing an enemy before they stand down
    private const float HoldTime = 22f;         // s holding aim on where the enemy vanished (plus up to 10 s)
    private const float LookRoundTime = 3.5f;   // s looking round at each spot they search
    private const int SearchSpots = 3;          // spots they sweep round where the enemy vanished, after the first
    private const float PortalSearchRange = 16f; // m: a searcher looks into the portals this near them or where the enemy vanished
    private const float PeerDistance = 2.5f;    // m in front of a portal's water to stand and look in
    private const float ReloadTime = 2.2f;      // as long as the ReloadPistol clip
    private const float ShootCos = 0.985f;      // facing within about 10 degrees of the target to shoot
    private const float FriendInLineRadius = 0.6f; // m: a friend this near the line of a shot is in the way
    private const float FriendBeyond = 12f;     // m: how far behind the target a friend still counts as in the way
    private const float FriendCone = 0.035f;    // rad: the most of a shot's cone of error that counts for a friend in the way (the whole cone, 30 m and more off, was metres wide: a rifleman with anyone of theirs downrange never fired)
    private const float SidestepTime = 0.7f;    // s of stepping aside for a clear shot
    private const float TuckedTooFar = 0.3f;    // rad: the gun swung this far down out of a wall won't fire

    // Strategies.
    private const float PushRange = 5f;         // m: a pusher keeps coming until this close
    private const float PushRunRange = 12f;     // m: further than this, at a run
    private const float PushSearchScale = 1.5f; // how much longer a pusher hunts for

    // Aggression.
    private const float CautiousPace = 0.55f;       // of their walking speed: a cautious one moves up (and searches) slowly
    private const float CautiousCloseIn = 10f;      // m: a cautious one moves up on the enemy until this close (or CautiousCloseInShare of their gun's range)
    private const float CautiousCloseInShare = 0.35f;
    private const float CautiousStopToShoot = 0.35f; // s: a cautious one stands still this long before each burst
    private const float CautiousSearchScale = 1.6f; // how much longer a cautious one searches for
    private const float CautiousLookScale = 1.6f;   // and how much longer they look round at each spot
    private const float DefensiveCoverReach = 4f;   // m: the furthest a defensive one goes for cover (they hold their ground)

    /// <summary>How much longer than usual they search for (or hold, for a holder: SearchTime only applies to searchers).</summary>
    private float SearchScale => Aggression == Aggression.Cautious ? CautiousSearchScale : Strategy == NpcStrategy.Push ? PushSearchScale : 1f;

    /// <summary>The furthest they'll go for cover.</summary>
    private float CoverReachFor => Aggression == Aggression.Defensive ? DefensiveCoverReach : float.MaxValue;
    private const float CoverReach = 25f;       // m: how far off an ally can be and still be covered
    private const float CoverRange = 10f;       // m: a coverer in a fight stays this near the ally
    private const float CoverFollow = 4f;       // m behind the ally, on the hunt
    private const float SuppressTime = 3.5f;    // s of firing at where the enemy vanished, to pin them
    private const float FallBackRange = 12f;    // m: an enemy this close makes a holder-back fall back
    private const float FallBackTime = 2f;      // s of running back
    private const float FallBackEvery = 3.5f;   // s of standing and shooting before they'll fall back again

    private readonly int _handR;
    private int _burstLeft;             // shots left in this burst (automatic guns)
    private Npc? _targetNpc;            // the enemy being fought: another NPC, or (null with _targetIsPlayer) the player
    private bool _targetIsPlayer;
    private bool _targetVisible;
    private float3 _lastSeen;           // where the target was last seen (chest height)
    private float _lostFor;             // s since the target was last seen
    private float _retarget;
    private float _lineUp;              // s spent facing the target, towards the first shot
    private float _cooldown;            // s to the next shot
    private float _reloadLeft;
    private float _reAim = -1f;         // s until the aim overlay takes over again after a shot's recoil
    private float _flash;               // s of muzzle flash left
    private float _sidestepLeft;        // s left stepping aside from a friend in the line of fire
    private float _sidestepSign = 1f;
    private float _lastSeenAt;          // Senses.Time _lastSeen was last set from a sighting
    private float _sightFor;            // s they've had the target in sight without a break (their aim settles)

    // Aiming (see Shoot).
    private const float LeadSkill = 0.45f;      // how much of a moving target's lead even the worst shot allows for (the best: all of it)
    private const float SettleTime = 1.8f;      // s for the aim to settle on someone who's just shown...
    private const float SettleSkill = 1f;       // ...less this much for a perfect shot
    private const float SettleExtra = 1.6f;     // how much wider the very first shot's cone is
    private const float CrossingCone = 0.07f;   // wider by this, per m/s the target crosses their line of fire

    // Losing sight.
    private enum Hunt { Engage, Search, Hold, Cover, FireAtFlash, FireIntoSmoke }

    // The player's gunshots.
    private const float FlashCos = -0.1f;       // a muzzle flash catches the eye anywhere but behind them (95 degrees either side)
    private const float FlashFireRange = 70f;   // m: they don't waste rounds on a flash further off than this
    private const float FlashFireTime = 2.5f;   // s of firing at a flash (each new one they see starts this again)
    private const float FlashLineUp = 0.15f;    // s facing it before the first shot: a snap shot
    private const float FlashCone = 1.6f;       // shots go this much wider than at someone they can see
    private const float ProneTargetCone = 1.3f; // and this much wider at the player lying prone
    private const float CoverHug = 2.5f;        // m: cover means something solid this close, between them and the shot
    private const float CoverTime = 8f;         // s in cover before they come out (plus up to 6 s; a pusher, half that)
    private const float CoverTimeMax = 20f;     // s: more shots keep them down, but only this long
    private const float CoverShotAdds = 2f;     // s another shot adds, while they're there
    private const float CoverShotMoved = 4f;    // m: a shot this far from the one they took cover from may mean finding another spot
    private const float CoverNotNear = 4f;      // m: no use taking cover this near the shooter
    private const float CoverShared = 1f;       // m: one person to a spot
    private static readonly float[] CoverRings = [2f, 4f, 7f, 11f, 16f];    // m: how far they look, nearest first
    private const int CoverDirections = 12;

    private enum ShotOrder { None, TakeCover, FireAtFlash }

    private ShotOrder _shotOrder;       // what the last shot heard calls for, once they're in the fight and can't see anyone
    private float3 _shotAt;             // where it was fired from
    private float3 _coverFrom;          // the shot they took cover from
    private float3 _coverSpot;          // where they're heading, or are, in cover
    private bool _coverWanted;          // has to find a spot (when there's a search free this frame)
    private bool _inCover;
    private float _coverLeft;           // s until they come out
    private float _flashLeft;           // s of firing at the flash left
    private bool _firingBlind;          // this shot's at a flash, not at anyone they can see
    private bool _pushingFlash;         // running at a flash they saw, firing at it on the way (a pusher's answer to it)
    private const float PushFlashPace = 0.85f;   // of their running speed, while pushing at a flash

    /// <summary>
    /// What a muzzle flash they can't see the shooter behind makes them do, by their mentality: a pusher, or an aggressive
    /// one who isn't holding back, goes at it; a defensive or cautious one, or one holding back, stands and fires at it.
    /// </summary>
    private bool PushesOnFlash => !HoldsPost && Strategy != NpcStrategy.HoldBack && Aggression != Aggression.Defensive
                                  && (Strategy == NpcStrategy.Push || (Aggression == Aggression.Aggressive && !_holds));

    /// <summary>Debug: BRUTALFPS_NPCLOST=search or hold makes every armed NPC do that when they lose sight.</summary>
    private static readonly LostSight LostOverride = Environment.GetEnvironmentVariable("BRUTALFPS_NPCLOST")?.ToLowerInvariant() switch
    {
        "search" => LostSight.Search,
        "hold" => LostSight.Hold,
        _ => LostSight.Auto,
    };

    // Flares: lighting up the dark where the player has gone.
    private const float FlareChance = 0.35f;      // who carries them (BRUTALFPS_FLARES=all or none says otherwise)
    private const int FlaresCarried = 2;
    private const float FlareDark = 0.3f;         // the light where the player might be, below which it's worth one (about what the moon gives in the open)
    private const float FlareClear = 11f;         // m: not with one burning, or on its way, this near there already
    private const float FlareThrowSpeed = 16f;    // m/s, by hand
    private const float FlareMinRange = 5f;       // m: any nearer, they'd go and look
    private const float FlareThrowRange = 22f;    // m
    private const float FlareLaunchRange = 150f;  // m: an illumination flare, fired up over the place (Flares.PlanHigh)
    private const float FlareLookEvery = 1.2f;    // s between thoughts of it (plus up to as long again), which take rays
    private const float FlareThrowTime = 14f / 30f;   // s from the start of the ThrowFlare clip to letting go (riflelib.py, THROW_RELEASE)
    private const float FlareLaunchTime = 0.35f;  // s of bringing the launcher to bear
    private const float FlareAfter = 0.4f;        // s from letting go until the gun comes back up

    private static readonly string? FlaresOverride = Environment.GetEnvironmentVariable("BRUTALFPS_FLARES")?.Trim().ToLowerInvariant();

    private readonly int _handL;
    private readonly bool _launches;    // a long gun's underslung launcher, not a throw from the off hand
    private int _flares;                // how many they've got left
    private bool _firedOnTarget;        // whether they've fired on the one they're targeting yet (for the log)
    private float _flareLook;           // s until they next think of it
    private float _flareWind;           // s until it leaves the hand or the launcher (above 0: at it now)
    private float3 _flareTowards;       // where it's going
    private float3 _flareVelocity;

    private readonly bool _holds;       // on losing sight: hold aim where the enemy vanished (else search)
    private bool _squadSearch;          // searching for the player together with their side (SquadIntel's places to look)
    private float _clearLook;           // s to the next look at the places to search they can see from where they are
    private Hunt _hunt;
    private bool _everSeen;             // has seen the enemy since they started fighting (else they're after a sound)
    private float3 _seenVel;            // how the target was moving when last seen (m/s, smoothed)
    private float3 _vanished;           // where they were last seen (chest height): what a holder aims at
    private float3 _lead;               // the threat when the hunt started: a shot heard elsewhere is a new lead
    private float3 _spot;               // where a searcher is heading
    private int _spotsLeft;
    private float _lookLeft;            // s left looking round at the current spot (0: walking to it)
    private float _lookFrom;            // the heading they arrived at the spot with
    private float _huntLeft;            // s before they give up
    private float _blocked;             // s a searcher has been stopped by something in the way
    private Portal? _chasing;           // the portal the player went through, that a searcher is heading for
    private float _followedSince;       // the player's portal crossings after this (Senses.Time) are leads
    private bool _followedThrough;      // has gone through a portal after them in this hunt
    private PortalSight? _glimpse;      // the player, in view through a portal (out of a bullet's reach, so not "visible")
    private Portal? _watching;          // the portal they last saw the player through in this hunt
    private Portal? _peering;           // the portal a searcher is on their way to look into (or looking into)
    private readonly HashSet<Portal> _lookedIn = [];   // the portals they've looked into in this hunt, on this side
    private Portal? _portalOrder;       // a portal that's just come on, to answer once they're in the fight (see PortalCameOn)
    private bool _portalApproach;       // ...by moving up to cover it (else into cover, gun on it)
    private float _portalOrderAt;       // Senses.Time it came in: stale after a few seconds
    private Portal? _guarding;          // the portal they're moving up to, to stand with the gun on its water
    private ArmedNpc? _covering;        // the ally a coverer is sticking with
    private bool _suppressing;          // firing at where the enemy vanished
    private bool _running;              // pushing in at a run: shots go wider
    private float _fallBackLeft;        // s left of falling back
    private float _fallBackWait;        // s before they'll fall back again

    public ArmedNpc(string name, Skeleton skeleton, ClipLibrary clips, Outfit outfit, Waypoint[] route, float3 skinTone, float startHeading,
        float phase, NpcProfile profile)
        : base(name, skeleton, clips, outfit, route, skinTone, startHeading, phase, profile)
    {
        _handR = skeleton.NodeIndex("hand.R");
        Gun = NpcGun.For(profile.Equipment);
        Ammo = Gun.Magazine;
        // A defensive one holds their ground, a cautious one goes and looks; an aggressive one, by their strategy (or a roll).
        var tactic = LostOverride != LostSight.Auto ? LostOverride : profile.Aggression switch
        {
            Aggression.Defensive => LostSight.Hold,
            Aggression.Cautious => LostSight.Search,
            _ => profile.Strategy switch
            {
                NpcStrategy.Push => LostSight.Search,
                NpcStrategy.HoldBack => LostSight.Hold,
                _ => profile.OnLostSight,
            },
        };
        var holdChance = 0.25 + (profile.Temperament == Temperament.Freezer ? 0.2 : 0);
        _holds = tactic == LostSight.Hold || (tactic == LostSight.Auto && _random.NextDouble() < holdChance);
        _handL = skeleton.NodeIndex("hand.L");
        _launches = NpcGun.Fallback(profile.Equipment) is Equipment.Rifle or Equipment.Carbine or Equipment.BattleRifle or Equipment.Smg;
        // (Their own roll, by name, so it doesn't shift the others: the same people carry them every run.)
        var carries = FlaresOverride switch
        {
            "all" => true,
            "none" => false,
            _ => new Random(StableSeed(name) ^ 0x0f1a2e).NextDouble() < FlareChance,
        };
        _flares = carries ? FlaresCarried : 0;
        SelfPreserving = !profile.IsCommando && RollSelfPreservation(name);   // (a commando fights on, hurt or not)
        _ = Morale;   // (sets their morale going, from their temper: Game/ArmedNpcAlert.cs)
    }

    /// <summary>How many flares they have left.</summary>
    public int FlaresLeft => _flares;

    /// <summary>What they do when they lose sight of the enemy: search for them, or hold aim where they vanished.</summary>
    public LostSight Tactic => _holds ? LostSight.Hold : LostSight.Search;

    /// <summary>How they go about a fight.</summary>
    public NpcStrategy Strategy => Profile.Strategy;

    /// <summary>How the gun in hand is held and fires.</summary>
    public NpcGun Gun { get; }

    /// <summary>Rounds left in the magazine.</summary>
    public int Ammo { get; private set; }

    /// <summary>The pistol's model space -> world, this frame (identity until the first pose).</summary>
    public float4x4 GunToWorld { get; private set; } = float4x4.Identity;

    private string AimClip => Profile.TwoHanded ? "AimRifle" : "AimPistol";
    private string FireClip => Profile.TwoHanded ? "FireRifle" : "FirePistol";
    private string ReloadClip => Profile.TwoHanded ? "ReloadRifle" : "ReloadPistol";

    // ------------------------------------------------------------------------------------------------ being scripted

    /// <summary>How far into DrawRifleBack the hand reaches the grip (Tools/blender/humankit/riflelib.py, GRAB_FRAME).</summary>
    public const float DrawGrabTime = 13f / 30f;

    private bool _slung;                // the gun is on their back
    private float4x4 _slungOnChest;     // the gun's model space -> the chest bone, there
    private int _chest = -1;
    private float _sparedUntil;         // Senses.Time before which they leave the player alone

    /// <summary>In a fight they stay where they are and shoot from there, for a script that needs them there.</summary>
    public bool HoldsPost { get; set; }

    /// <summary>Holding a post, but not once the player has got past them and gone upstairs: then they go after them (and back to it afterwards).</summary>
    public bool FollowsUpstairs { get; set; }
    private bool _leftPost;

    /// <summary>Their gun has been taken off them: no longer drawn.</summary>
    public bool GunTaken { get; set; }

    /// <summary>A script takes them back in the middle of a fight, gun up, with at least <paramref name="rounds"/> rounds left.</summary>
    public void ScriptSeize(int rounds)
    {
        Scripted = true;
        Ammo = int.Max(Ammo, rounds);
        _scriptTo = _scriptFace = null;
        _burstLeft = 0;
        _reloadLeft = 0;
        _aside = null;
        _animator.PlayOverlay(AimClip, _upperBody, 0.1f, 0.35f);
    }

    /// <summary>One round at <paramref name="at"/>, exactly where the script says.</summary>
    public void ScriptShoot(NpcSenses s, float3 at)
    {
        if (s.Fire is null || Ammo <= 0 || !Alive)
            return;
        var muzzle = Muzzle;
        var to = at - muzzle;
        var dist = Length(to);
        if (dist < 0.05f)
            return;
        _lastSeen = at;
        var drop = new float3(0, 0.5f * 9.81f * dist / Gun.BulletSpeed, 0);
        s.Fire(this, muzzle, [to / dist * Gun.BulletSpeed + drop]);
        Ammo--;
        _flash = 0.05f;
        _animator.PlayOverlay(FireClip, _upperBody, 0.02f, 0f);
    }

    /// <summary>The magazine out and a new one in, for a script.</summary>
    public void ScriptReload() => _animator.PlayOverlay(ReloadClip, _upperBody, 0.15f, 0.2f);

    private protected override bool UpdateScripted(float dt)
    {
        _flash -= dt;
        return base.UpdateScripted(dt);
    }

    /// <summary>They leave the player alone until <paramref name="time"/> (on <see cref="NpcSenses.Time"/>'s clock).</summary>
    public void SparePlayerUntil(float time) => _sparedUntil = time;

    /// <summary>
    /// A script takes them over, hidden, at <paramref name="at"/>: no senses, no route, nobody notices them. With
    /// <paramref name="slung"/> their gun is on their back; otherwise it's up, in the aim.
    /// </summary>
    public void ScriptStart(float3 at, float heading, bool slung)
    {
        ScriptTake(at, heading);
        Hidden = true;
        ScriptSling(slung);
    }

    /// <summary>They're there now.</summary>
    public void ScriptReveal() => Hidden = false;

    /// <summary>Puts the gun on their back or in their hand. Back: exactly where the hand finds it at <see cref="DrawGrabTime"/>, so taking it doesn't jump.</summary>
    public void ScriptSling(bool slung)
    {
        _slung = slung;
        if (slung && _chest < 0)
            ScriptSlingReady();
        if (!slung)
            _animator.PlayOverlay(AimClip, _upperBody, 0.2f, 0.35f);
    }

    /// <summary>Works out where the gun goes on their back: where the hand finds it at <see cref="DrawGrabTime"/>.</summary>
    private void ScriptSlingReady()
    {
        _chest = _skeleton.NodeIndex("spine_03");
        var animator = new Animator(_skeleton, _clips);
        animator.PlayOverlay("DrawRifleBack", _upperBody, 0.001f, 0f);
        var pose = new NodePose[_skeleton.NodeCount];
        var world = new float4x4[_skeleton.NodeCount];
        animator.Update(DrawGrabTime, pose);   // (the clip has no weight until it has aged: the pose is the second update's)
        animator.Update(1e-4f, pose);
        _skeleton.ComputeWorld(pose, world);
        float4x4.Invert(world[_chest], out var toChest);
        _slungOnChest = GunInHand(world[_handR]) * toChest;
    }

    /// <summary>The gun comes into the hand, which is on it (<see cref="DrawGrabTime"/> into DrawRifleBack); the clip goes on playing.</summary>
    public void ScriptTakeGun() => _slung = false;

    public void ScriptPlay(string clip, float fadeIn, float fadeOut) => _animator.PlayOverlay(clip, _upperBody, fadeIn, fadeOut);

    public void ScriptStop(string clip, float fadeOut) => _animator.Stop(clip, fadeOut);

    /// <summary>Stands them at once where the script leaves them, the gun up.</summary>
    public void ScriptPlace(float3 at, float heading)
    {
        Position = at;
        Heading = heading;
        _scriptTo = null;
        ScriptReveal();
        ScriptSling(false);
    }

    /// <summary>The script's over: they fight, on their own. Nobody fires before <paramref name="firstShotAfter"/> seconds.</summary>
    public void ScriptEngage(float3 enemyAt, float firstShotAfter)
    {
        Scripted = false;
        Hidden = false;
        Threat = enemyAt;
        SetState(NpcState.Fighting);
        _cooldown = firstShotAfter;
    }

    /// <summary>Whether the gun is out (drawn and visible): always, for now (they carry it in hand).</summary>
    public bool GunVisible => _handR >= 0 && Alive && !GunTaken;

    /// <summary>The muzzle flash (0 = none, 1 = just fired) and where it is.</summary>
    public float Flash => float.Clamp(_flash / 0.05f, 0f, 1f);

    public float3 Muzzle => OnGun && _nest is { } nest ? nest.Muzzle : float3.Transform(Gun.MuzzleLocal, GunToWorld);

    // ------------------------------------------------------------------------------------------------ senses

    private protected override bool NoticesPlayerGun(NpcSenses s) => EnemyOfPlayer(s);

    /// <summary>They look out as far as their gun reaches: a rifleman on the alert makes the player out at 80-95 m in full light (less in the dark), someone with a shotgun no further than anyone.</summary>
    private protected override float SightReach(bool alert)
    {
        var gun = (OnGun ? GunRange : Gun.Range) / NpcSenses.FullSight;
        return alert ? float.Max(NpcSenses.AlertSightReach, gun) : float.Max(1f, gun * CalmLookOut);
    }

    private protected override bool AlertForPlayer(NpcSenses s) => EnemyOfPlayer(s) && WantsToFight(s) && !Blinded && (Alerted(s.Time) || InContact);

    private bool EnemyOfPlayer(NpcSenses s) => !s.PlayerConcealed && !Profile.FriendlyToPlayer && s.Time >= _sparedUntil && Factions.EnemyOfPlayer(Faction, s.PlayerWanted);

    /// <summary>Whether they'll shoot at this person: by faction, or (<see cref="NpcProfile.ShootsAnyone"/>) anyone not on their side who isn't spared.</summary>
    private bool EnemyOf(Npc other) => Profile.ShootsAnyone
        ? other.Faction != Faction && !other.Profile.Spared
        : Factions.Enemies(Faction, other.Faction);

    private protected override bool ReactsToPlayerShots => true;

    /// <summary>
    /// The player's gunshot has reached them. If they're after the player, what they do depends on what they can see of
    /// it: seeing the muzzle flash, they fire at it (unless they can see the player, then it's an ordinary fight); hearing
    /// it but not seeing the flash, they don't know what they'd be walking into, and make for cover.
    /// </summary>
    private protected override void HeardPlayerShot(float3 at, Stimulus kind, NpcSenses s)
    {
        var wantsCover = State is NpcState.Calm or NpcState.Returning or NpcState.Wary or NpcState.Startled or NpcState.Fighting;
        if (!wantsCover || !EnemyOfPlayer(s) || !WantsToFight(s) || Blinded)
        {
            Alarm(at, kind);
            return;
        }

        if (State == NpcState.Fighting && _targetVisible)
        {
            Alarm(at, kind);   // busy: they've got someone in their sights
            return;
        }

        _contact = true;
        var flash = SeesFlash(at, s);
        if (flash || !HoldsPost)
        {
            _shotAt = at;
            _shotOrder = flash ? ShotOrder.FireAtFlash : ShotOrder.TakeCover;
        }

        Alarm(at, Stimulus.Gunshot);
    }

    /// <summary>
    /// A portal near them has come on: anything could come out of it. Someone who'd fight goes on guard: by
    /// <see cref="NpcStrategy"/> and temperament, either into cover with the gun on the water (holding back, or one who
    /// holds when they lose sight), or moving up to stand a few metres off it, gun on the water (a pusher, an aggressive
    /// one, or one who'd search). Busy with someone in their sights, or already going to ground, they carry on.
    /// </summary>
    private protected override void PortalCameOn(Portal portal, NpcSenses s)
    {
        if (!WantsToFight(s) || Blinded || OnGun || GunTaken
            || State is not (NpcState.Calm or NpcState.Returning or NpcState.Wary or NpcState.Startled or NpcState.Fighting))
        {
            base.PortalCameOn(portal, s);
            return;
        }

        if (State == NpcState.Fighting && (_targetVisible || _hunt is Hunt.Cover or Hunt.FireAtFlash))
            return;   // busy

        // (Someone a script has posted somewhere stays there: just the gun up, towards it.)
        _portalOrder = HoldsPost ? null : portal;
        _portalOrderAt = s.Time;
        _portalApproach = Aggression != Aggression.Defensive && Strategy != NpcStrategy.HoldBack
            && (Strategy == NpcStrategy.Push || Aggression == Aggression.Aggressive || !_holds);
        Alarm(portal.Center, Stimulus.Noise, notThePlayer: true);   // (into the fight, gun up: UpdateFight picks up the order)
    }

    /// <summary>Answering a portal that's come on (<see cref="PortalCameOn"/>), with no one in sight: into cover, or up to it.</summary>
    private void FollowPortalOrder(NpcSenses s, float3 eye)
    {
        var portal = _portalOrder!;
        _portalOrder = null;
        if (!portal.Online || s.Time - _portalOrderAt > 4f)
            return;
        if (_hunt == Hunt.Engage)
            StartHunt(s, eye, quiet: true);
        _chasing = _peering = null;
        _squadSearch = false;
        _running = false;
        var dist = Distance(Position, portal.Center);

        if (_portalApproach)
        {
            // A few metres out in front of it, off to one side or the other so they don't all stand in one spot (if
            // there's floor there, in sight of the water: a small room may not have it).
            var across = float3.Cross(float3.UnitY, portal.Normal);
            var offset = ((float)_random.NextDouble() * 2f - 1f) * 1.6f;
            var knee = new float3(0, 0.6f, 0);
            var straightOut = PortalApproach(portal, s, PeerDistance);
            _spot = straightOut;
            foreach (var (distance, side) in (ReadOnlySpan<(float, float)>)[(PeerDistance + 0.8f, offset), (PeerDistance + 0.8f, 0f), (PeerDistance, offset * 0.5f)])
            {
                var p = PortalApproach(portal, s, distance) + across * side;
                if (s.Nav?.Walkable(p) != false && s.Clear(straightOut + knee, p + knee) > 0.99f)
                {
                    _spot = p;
                    break;
                }
            }
            _guarding = portal;
            _hunt = Hunt.Search;
            _vanished = _lastSeen = portal.Center;
            Threat = _lead = new float3(portal.Center.X, Position.Y, portal.Center.Z);
            _spotsLeft = SearchSpots;
            _lookLeft = 0;
            _blocked = 0;
            _huntLeft = float.Max(_huntLeft, SearchTime);
            Log.Write($"[NPC] {Name}: the {portal.Name} came on {dist:0.0} m away: moving up to cover it");            return;
        }

        Log.Write($"[NPC] {Name}: the {portal.Name} came on {dist:0.0} m away: taking cover, gun on it");
        _guarding = null;
        _shotAt = portal.Center;
        Threat = _lead = new float3(portal.Center.X, Position.Y, portal.Center.Z);
        _hunt = Hunt.Cover;
        _coverWanted = true;
        _inCover = false;
        _coverSpot = Position;
        _blocked = 0;
        _coverLeft = CoverTime + 6f * (float)_random.NextDouble();
    }

    /// <summary>Where the gun was last pointed: at the enemy they can see, or where they think the enemy is.</summary>
    public float3 AimPoint => _lastSeen;

    /// <summary>
    /// Their own side, known by sight and by the sound of their guns. This stands in for shouting and the radio until
    /// people have voices: a callout passing on a contact would come in at <see cref="HeardFriendlyShot"/>.
    /// </summary>
    public override bool Recognises(Npc other) => other is ArmedNpc && other.Faction == Faction;

    /// <summary>
    /// One of their own has fired, so there's a fight on, and the shooter isn't the enemy. Seeing them, they can see which
    /// way the gun's pointing, and that's where to look. Out of sight, all they know is that there's trouble over there:
    /// they head for their friend, or if they're already in the fight, carry on with it.
    /// </summary>
    private protected override void HeardFriendlyShot(ArmedNpc shooter, Stimulus kind, NpcSenses s)
    {
        var eye = Position + new float3(0, EyeHeight, 0);
        var chest = shooter.Position + new float3(0, 1.3f, 0);
        var seesThem = Distance(eye, chest) <= SightRange && s.Visibility(eye, chest) > 0f;
        if (State == NpcState.Fighting && (_targetVisible || !seesThem))
        {
            _calmLeft = CalmTime;
            return;
        }

        if (State != NpcState.Fighting)
            Log.Write($"[NPC] {Name}: {shooter.Name} is firing: {(seesThem ? "looking where they're aiming" : "out of sight, going by the sound")}");
        Alarm(seesThem ? shooter.AimPoint : shooter.Position, kind);
    }

    /// <summary>Whether they saw the flash of a shot from <paramref name="at"/>: a clear line, not behind them, and not too far for the light.</summary>
    private bool SeesFlash(float3 at, NpcSenses s)
    {
        var eye = Position + new float3(0, EyeHeight, 0);
        var to = at - eye;
        var dist = Length(to);
        if (dist > s.FlashSightRange || (dist > 1e-3f && float3.Dot(to / dist, Ahead) < FlashCos))
            return false;
        // (In a smoke screen the flash still shows: the cloud lights up round it.)
        return s.Visibility(eye, at, 0.995f) > 0f || (s.Clear(eye, at) > 0.995f && s.Smoke(eye, at) > 0.05f);
    }

    private protected override bool WantsToFight(NpcSenses s)
    {
        if (SelfPreserving && Health < SelfPreservingBackOff && !HoldsPost)
            return false;   // (however aggressive, if they value their life)
        return true;        // even with no enemy in sight yet: they go and see what the shooting is about
    }

    /// <summary>
    /// A noise, any noise (a footstep, a bullet striking, a body dropping): someone who'd fight goes on the alert, gun up
    /// (Fighting, hunting "after a sound"): to where it came from, looking round there, then sweeping the spots about it
    /// for whoever made it (<see cref="StartHunt"/>). It doesn't put the rest of their side on the alert: only contact does.
    /// While hunting the player, a noise the player makes is a new lead. Posted by a script, they only turn to look.
    /// </summary>
    private protected override void Noticed(float3 at, bool loud, bool byPlayer, NpcSenses s)
    {
        if (State == NpcState.Fighting)
        {
            if (byPlayer && _targetIsPlayer && !_targetVisible)
                Alarm(at, Stimulus.Noise);
            return;
        }

        if (State is (NpcState.Calm or NpcState.Returning or NpcState.Wary) && !HoldsPost && WantsToFight(s))
        {
            if (State != NpcState.Wary)
                Log.Write($"[NPC] {Name}: heard {(loud ? "something" : "a noise")} {Distance(Position, at):0.0} m away: on the alert");
            Alarm(at, Stimulus.Noise);
            return;
        }

        base.Noticed(at, loud, byPlayer, s);
    }

    // ------------------------------------------------------------------------------------------------ bodies and calling for help

    private const float HelpCallRange = 24f;       // m: a shout for help carries this far in the open
    private const float HelpCallWalled = 0.5f;     // ...and this much of it through a wall
    private const int HelpResponders = 4;          // at most this many (the nearest) come to a call
    private const float BodyCheckReach = 2.2f;     // m: close enough to the body to see what's happened
    private const float BodyCallAfter = 9f;        // s: can't get to the body: they call out from where they are
    private const float FiredOnMemory = 30f;       // s: shot at this lately, they know someone's out there and go looking

    private float3 _bodyAt;                        // a body they're on their way to check
    private bool _bodyCall;                        // ...and haven't yet called for help about
    private float _bodyFoundAt;                    // Senses.Time they saw it
    private float _firedOnAt = float.NegativeInfinity;   // Senses.Time an enemy's round last hit them or cracked past

    /// <summary>They've been shot at lately (<see cref="FiredOnMemory"/>): someone's out there, and they mean to find them.</summary>
    private bool FiredOnLately(NpcSenses s) => s.Time - _firedOnAt < FiredOnMemory;

    /// <summary>
    /// A body. One of the enemy's (someone they'd shoot) is nothing to them. One of their own, or anyone else's: no panic,
    /// they go and see (on the alert, gun up, walking), and once they're over it they call for help: those of their side
    /// close by come and search round it with them (<see cref="CallForHelp"/>). Posted somewhere, they call from there.
    /// </summary>
    private protected override void FoundBody(Npc body, NpcSenses s)
    {
        if (EnemyOf(body) || Blinded)
            return;
        if (!WantsToFight(s))
        {
            base.FoundBody(body, s);
            return;
        }

        var dist = Distance(Position, body.Position);
        if (HoldsPost)
        {
            Log.Write($"[NPC] {Name}: sees {body.Name}'s body {dist:0.0} m away: calling it in from their post");
            CallForHelp(this, body.Position, s, $"{Name} called in {body.Name}'s body");
            Alarm(body.Position, Stimulus.Noise);
            return;
        }

        Log.Write($"[NPC] {Name}: found {body.Name}'s body {dist:0.0} m away: going to check it");
        _bodyAt = body.Position;
        _bodyCall = true;
        _bodyFoundAt = s.Time;
        Alarm(body.Position, Stimulus.Noise);   // (into the fight, gun up, after a sound: a search that starts at the body)
    }

    private const float HelpCallEvery = 12f;       // s: someone who's called for help doesn't shout again for this long
    private float _helpCalledAt = float.NegativeInfinity;

    /// <summary><see cref="CallForHelp"/>, unless they called a moment ago (those who'd come are already coming).</summary>
    private void CallForHelpLimited(float3 at, NpcSenses s, string why)
    {
        if (s.Time - _helpCalledAt < HelpCallEvery)
            return;
        _helpCalledAt = s.Time;
        CallForHelp(this, at, s, why);
    }

    /// <summary>On the way to a body they found: at it (or held up too long), they call for help.</summary>
    private void UpdateBodyCall(NpcSenses s)
    {
        if (!_bodyCall)
            return;
        var at = Distance(Position with { Y = 0 }, _bodyAt with { Y = 0 }) < BodyCheckReach;
        if (!at && !_targetVisible && s.Time - _bodyFoundAt < BodyCallAfter)
            return;
        _bodyCall = false;
        Log.Write($"[NPC] {Name}: {(at ? "checked the body" : "can't get to the body")}: calling for help");
        CallForHelp(this, _bodyAt, s, $"{Name} called for help");
        _huntLeft = float.Max(_huntLeft, SearchTime * SearchScale);   // (and searches round it with them)
    }

    /// <summary>
    /// <paramref name="caller"/> shouts for help about something at <paramref name="at"/>: the armed people near enough to
    /// hear it (<see cref="HelpCallRange"/> in the open, half that through walls), on the caller's side (or anyone not their
    /// enemy, for an unarmed caller), who aren't already in a fight or posted somewhere, come over and search round it.
    /// Only those: the rest of the level carries on as it was. <paramref name="delay"/>: s before the shout goes up.
    /// </summary>
    public static void CallForHelp(Npc caller, float3 at, NpcSenses s, string why, float delay = 0f)
    {
        var from = caller.Position + new float3(0, EyeHeight, 0);
        var heard = new List<(ArmedNpc Ally, float Dist)>();
        foreach (var other in s.People)
        {
            if (other is not ArmedNpc { Alive: true, Scripted: false, HoldsPost: false } ally || ReferenceEquals(ally, caller) || ally.Zone != caller.Zone
                || ally.State is not (NpcState.Calm or NpcState.Wary or NpcState.Returning))
                continue;
            if (caller is ArmedNpc ? ally.Faction != caller.Faction : Factions.Enemies(ally.Faction, caller.Faction) || ally.EnemyOf(caller))
                continue;
            var ear = ally.Position + new float3(0, EyeHeight, 0);
            var dist = Distance(from, ear);
            if (dist > HelpCallRange || (dist > HelpCallRange * HelpCallWalled && s.Clear(from, ear) < 0.99f))
                continue;
            heard.Add((ally, dist));
        }

        // The few nearest come (HelpResponders); the rest leave it to them.
        heard.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        var coming = new List<string>();
        foreach (var (ally, dist) in heard.Take(HelpResponders))
        {
            ally.HearNoise(at, byPlayer: false, loud: true, delay + 0.4f + dist / 343f + 0.4f * (float)ally._random.NextDouble());
            coming.Add(ally.Name);
        }

        Log.Write($"[NPC] {why}: {(coming.Count == 0 ? "nobody near enough to hear" : $"{string.Join(", ", coming)} coming to help")}");
    }

    private protected override void SenseMore(NpcSenses s, float3 eye)
    {
        if (State is NpcState.Fighting or NpcState.Dead)
            return;

        // An enemy in sight (NPCs: anyone of an enemy faction who is armed or fighting; the player is covered by the
        // gun-sighting in the base class, as NoticesPlayerGun says).
        // (An ally in a firefight brings them in by shouting to them: ShoutContact / HearShout, Game/ArmedNpcAlert.cs.)
        var ahead = Ahead;
        foreach (var other in s.People)
        {
            if (ReferenceEquals(other, this) || !other.Alive || other.Scripted || other.Zone != Zone || !EnemyOf(other))
                continue;
            var at = other.Position + new float3(0, other.AimHeight, 0);
            var to = at - eye;
            var dist = float.Sqrt(float3.Dot(to, to));
            if (dist > SightRange || dist < 1e-3f || float3.Dot(to / dist, ahead) < SightCos || s.Visibility(eye, at) <= 0f)
                continue;
            Alarm(other.Position, Stimulus.SawGun);
            return;
        }
    }

    // ------------------------------------------------------------------------------------------------ fighting

    private protected override void WhileClimbing()
    {
        _animator.Stop(AimClip, 0.15f);
        _animator.Stop(FireClip, 0.1f);
    }

    private protected override void StoppedClimbing()
    {
        if (State == NpcState.Fighting && _reloadLeft <= 0)
            _animator.PlayOverlay(AimClip, _upperBody, 0.25f, 0.35f);
    }

    private protected override void PushDoor()
    {
        base.PushDoor();
        if (State == NpcState.Fighting)
            _reAim = 0.7f;   // (then the gun comes back up)
    }

    private protected override void OnStateChanged(NpcState old, NpcState now)
    {
        if (now == NpcState.Dead && old != NpcState.Fighting && _gunState != OnGunState.No)
            LeaveGun("shot off the M249", null, fallen: true);   // (a turret's gunner, on watch)
        if (now == NpcState.Fighting)
        {
            if (_leftPost)
            {
                _leftPost = false;
                HoldsPost = true;
            }

            StopIdleClips();
            _animator.Stop("Talk", 0.15f);
            if (!OnGun)   // (a turret's gunner is on its gun already: Game/NpcGunner.cs)
                _animator.PlayOverlay(AimClip, _upperBody, 0.25f, 0.35f);
            _retarget = 0;
            _lineUp = 0;
            _lostFor = 0;
            _lastSeen = float.Abs(_threatRawY - Position.Y) < 2.2f ? Threat + new float3(0, 1.3f, 0) : ThreatAt;
            _cooldown = 0;
            _hunt = Hunt.Engage;
            _everSeen = false;
            _targetVisible = false;
            _glimpse = null;
            _seenVel = float3.Zero;
            if (_trail.Count == 0)
                _trail.Add(Position);
        }
        else if (old == NpcState.Fighting)
        {
            if (_gunState != OnGunState.No && (now == NpcState.Dead || !Crew))   // (a turret's gunner stays in the hatch)
                LeaveGun(now == NpcState.Dead ? "shot off the M249" : $"off the M249 ({now.ToString().ToLowerInvariant()})", null, fallen: true);
            _animator.Stop(AimClip, 0.35f);
            _animator.Stop(FireClip, 0.2f);
            _animator.Stop(ReloadClip, 0.25f);
            if (_reloadLeft > 0)
                Ammo = Gun.Magazine;   // they finish it as they lower the gun
            _reloadLeft = 0;
            _aside = null;
            _burstLeft = 0;
            _reAim = -1f;
            _targetNpc = null;
            _targetIsPlayer = false;
            _shotOrder = ShotOrder.None;
            _pushingFlash = false;
            _portalOrder = _guarding = null;
            _contact = false;
            _bodyCall = false;
            EndCoverFight(null);
            DropFlareThrow(0.25f);
            ResetTactics(null);
            _sightFor = 0f;
        }
    }

    private protected override void OnBlinded(bool blinded)
    {
        if (State != NpcState.Fighting)
            return;
        if (Crew)
        {
            // In a turret: down behind the shield till they can see again (Game/NpcGunner.cs).
            _burstLeft = 0;
            _lineUp = 0;
            return;
        }

        if (blinded && OnGun)
            LeaveGun("dazzled: let go of the M249", null, fallen: true);
        if (blinded)
        {
            // Dazzled: the gun comes down as the arms go up over the eyes (a reload carries on).
            _animator.Stop(AimClip, 0.1f);
            _animator.Stop(FireClip, 0.1f);
            _burstLeft = 0;
            _lineUp = 0;
            _reAim = -1f;
            DropFlareThrow(0.1f);
        }
        else if (_reloadLeft <= 0)
        {
            _animator.PlayOverlay(AimClip, _upperBody, 0.2f, 0.35f);
        }
    }

    private protected override void OnDodge(NpcSenses s)
    {
        // A grenade coming down by them: off the M249, no flare, and the burst's done; they run with the gun up.
        if (OnGun)
            LeaveGun("a grenade: off the M249", null, fallen: true);
        _burstLeft = 0;
        _lineUp = 0;
        _sidestepLeft = 0;
        _fallBackLeft = 0;
        DropFlareThrow(0.1f);
        DropGrenadeThrow(0.1f);
        if (State == NpcState.Fighting)
            Say(Bark.Grenade, s);
    }

    private protected override void OnHitReact()
    {
        if (OnGun)
        {
            // Hit, on the gun: it stops the burst for a moment, and that's all (unless it's bad: see UpdateOnGun).
            _burstLeft = 0;
            _cooldown = float.Max(_cooldown, 0.45f);
            _lineUp = 0;
            return;
        }

        // The hit knocks the aim off: the flinch shows in the arms too, and the aim comes back once they recover. (A
        // reload carries on.)
        if (State != NpcState.Fighting || _reloadLeft > 0)
            return;
        _animator.Stop(AimClip, 0.08f);
        _animator.Stop(FireClip, 0.08f);
        _reAim = 0.05f;
        _lineUp = 0;
        _sightFor *= 0.5f;   // (the aim's knocked off: it has to settle again)
        DropFlareThrow(0.08f);
        DropGrenadeThrow(0.08f);
    }

    private protected override bool UpdateFight(float dt, float walkSpeed, float runSpeed, NpcSenses s)
    {
        _cooldown -= dt;
        _flash -= dt;
        UpdateUnsling(dt);
        if (OnGun)
            return UpdateOnGun(dt, s);
        if (_hitCall)
        {
            _hitCall = false;
            Say(Bark.Hit, s);
        }

        if (_staggerLeft > 0 || Blinded)
            return false;
        UpdateBodyCall(s);

        // Hurt very badly, and one who values their life: back off. Hurt, with a hiding place close by: fall back into it to
        // regroup (Game/NpcHiding.cs: UpdateFlee takes them there).
        var regroup = !HoldsPost && WantsToRegroup(s);
        if (!HoldsPost && ((SelfPreserving && Health < SelfPreservingBackOff) || regroup))
        {
            if (regroup)
                Log.Write($"[NPC] {Name}: hurt ({Health:0} HP): falling back to regroup");
            var away = Position - Threat;
            away.Y = 0;
            var len = float.Sqrt(float3.Dot(away, away));
            (_fleeDir, _fleeClear) = Steer(len > 1e-3f ? away / len : -Ahead, s);
            SetState(NpcState.Fleeing);
            return false;
        }

        // Back to the aim after a shot's recoil (both are upper-body overlays and share the overlay slot).
        if (_reAim >= 0)
        {
            _reAim -= dt;
            if (_reAim < 0 && _reloadLeft <= 0)
                _animator.PlayOverlay(AimClip, _upperBody, 0.08f, 0.35f);
        }

        // Reloading: gun lowered for a moment.
        if (_reloadLeft > 0)
        {
            _reloadLeft -= dt;
            if (_reloadLeft <= 0)
            {
                Ammo = Gun.Magazine;
                _animator.PlayOverlay(AimClip, _upperBody, 0.12f, 0.35f);   // ReloadPistol ends on the aim
                if (_drawingAk)
                    _drawingAk = false;   // (it was their AK coming up, off their back)
                else
                    Log.Write($"[NPC] {Name} reloaded");
            }
        }

        // Who to fight: the nearest enemy in sight, a few times a second.
        _retarget -= dt;
        if (_retarget <= 0)
        {
            _retarget = RetargetEvery * (0.8f + 0.4f * (float)_random.NextDouble());   // (a little uneven: a crowd that saw the player together doesn't all look again on the same frame)
            PickTarget(s);
        }

        // How long they've had this target in sight, without a break (their aim settles: see Shoot).
        _sightFor = _targetVisible ? _sightFor + dt : 0f;

        // Out of a doorway's way to reload (Game/ArmedNpcDoorway.cs).
        if (UpdateAside(dt, walkSpeed, s, out var asideMoved))
            return asideMoved;

        if (WantsGun(dt, s))
            return GoToGun(dt, runSpeed, s);

        var eye = Position + new float3(0, EyeHeight, 0);
        if (UpdateFlare(dt, s, eye))
            return false;   // (stopped to throw it)

        // The big decisions: flank, rush a reloading player, a grenade, fall back, or the usual fight (Game/ArmedNpcTactics.cs).
        // Sent back to hold a post (Game/ArmedNpcOrders.cs): that comes before any fight, in sight or not.
        if (ReturnToPost(dt, runSpeed, s, out var toPost))
            return toPost;

        RecoverMorale(dt, SuppressionAt(s.Time));
        if (UpdateTactics(dt, walkSpeed, runSpeed, s, eye, out var tacticMoved))
            return tacticMoved;

        // Under heavy fire (or, for one with a sense of self-preservation, hurt or shot at): fight from cover.
        if (UpdateCoverFight(dt, walkSpeed, runSpeed, s, eye, out var fromCover))
            return fromCover;

        if (_targetVisible)
        {
            if (_hunt != Hunt.Engage)
                Log.Write($"[NPC] {Name}: {TargetName} back in sight after {_lostFor:0.0} s ({_hunt switch { Hunt.Hold => "held aim", Hunt.Cover => "from cover", Hunt.FireAtFlash => "firing at the flash", Hunt.FireIntoSmoke => "firing into the smoke", _ => "searching" }})");
            _hunt = Hunt.Engage;
            _shotOrder = ShotOrder.None;
            _pushingFlash = false;
            _everSeen = true;
            _lostFor = 0;
            _calmLeft = CalmTime;
            Threat = new float3(_lastSeen.X, Position.Y, _lastSeen.Z);
        }
        else
        {
            if (_portalOrder is not null)
                FollowPortalOrder(s, eye);
            else if (_shotOrder != ShotOrder.None)
                FollowShotOrder(s, eye);
            else if (_hunt == Hunt.Engage)
                StartHunt(s, eye);
            FollowOrder();   // (sent somewhere by a script: that comes first: Game/ArmedNpcOrders.cs)
            _lostFor += dt;
            if (HoldsPost && FollowsUpstairs && s.PlayerAlive && s.PlayerEye.Y - EyeHeight > Position.Y + 1f)
            {
                // Past them and up the stairs: after them.
                HoldsPost = false;
                _leftPost = true;
                _targetIsPlayer = true;
                _everSeen = true;
                _hunt = Hunt.Search;
                _vanished = _lastSeen = s.PlayerEye;
                _spot = s.PlayerEye;
                _lookLeft = 0;
                _blocked = 0;
                _spotsLeft = SearchSpots;
                _huntLeft = SearchTime;
                Log.Write($"[NPC] {Name}: the player's gone up the stairs: leaving their post");
            }

            if (_hunt is Hunt.Hold or Hunt.Search && SuspectsSmoke(dt, s, eye))
                StartFireIntoSmoke(s, eye);

            if (HoldsPost && _hunt is not (Hunt.FireAtFlash or Hunt.FireIntoSmoke))
                return false;
            return UpdateHunt(dt, walkSpeed, runSpeed, s, eye);
        }

        var to = _lastSeen - eye;
        var flat = new float3(to.X, 0, to.Z);
        var dist = float.Sqrt(float3.Dot(flat, flat));
        var along = dist > 1e-3f ? flat / dist : Ahead;

        // Holding back, with the enemy too close: turn and fall back some way, then stand and shoot again.
        _fallBackWait -= dt;
        if (_fallBackLeft > 0 || (Aggression == Aggression.Aggressive && Strategy == NpcStrategy.HoldBack && dist < FallBackRange && _fallBackWait <= 0))
            return FallBack(along, dist, dt, runSpeed, s);

        var moved = _sidestepLeft > 0 ? Sidestep(along, dt, walkSpeed, s) : Manoeuvre(along, dist, dt, walkSpeed, runSpeed, s);
        if (!moved)
            FaceTowards(_lastSeen, dt, 2.2f);

        var facing = dist > 1e-3f ? float3.Dot(flat / dist, Ahead) : 1f;
        if (facing > ShootCos)
            _lineUp += dt;

        // Line up for a moment (quicker the better the shot, at once if they were holding aim on this spot), then fire at a rate.
        var reaction = 0.9f - 0.5f * Profile.Accuracy;
        if (_reloadLeft <= 0 && facing > ShootCos && _lineUp > reaction && _cooldown <= 0 && dist < Gun.Range)
            Shoot(s);

        if (moved)
            AddTrail();
        return moved;
    }

    /// <summary>
    /// How they use the ground with the enemy in sight, still aiming: a defensive one stands their ground; a cautious one
    /// moves up slowly, only between bursts (they stop to shoot); an aggressive one by their <see cref="NpcStrategy"/>.
    /// Returns whether they moved.
    /// </summary>
    private bool Manoeuvre(float3 along, float dist, float dt, float walkSpeed, float runSpeed, NpcSenses s)
    {
        _running = false;
        if (HoldsPost)
            return false;
        // Shaken (Game/ArmedNpcAlert.cs), even the aggressive only move up the cautious way.
        var aggression = Aggression == Aggression.Aggressive && Morale < 0.4f && !Profile.IsCommando ? Aggression.Cautious : Aggression;
        switch (aggression)
        {
            case Aggression.Defensive:
                return false;
            case Aggression.Cautious:
                return dist > float.Max(CautiousCloseIn, Gun.Range * CautiousCloseInShare) && _burstLeft <= 0 && _cooldown > CautiousStopToShoot
                       && GoTowards(_lastSeen, walkSpeed * CautiousPace * (1f - _limp), dt, TurnRate * 1.2f, s);
        }

        switch (Strategy)
        {
            case NpcStrategy.Push:
                if (dist <= PushRange && !InDoorwaysWay(s))   // (not stopping in a doorway: on into the room, Game/ArmedNpcDoorway.cs)
                    return false;
                _running = dist > PushRunRange;
                return GoTowards(_lastSeen, (_running ? runSpeed * 0.8f : walkSpeed) * (1f - _limp), dt, TurnRate * 1.6f, s);

            case NpcStrategy.HoldBack:
                return false;

            case NpcStrategy.Cover when _covering is { } ally:
                var to = ally.Position - Position;
                to.Y = 0;
                var len = Length(to);
                return len > CoverRange && GoTowards(ally.Position, walkSpeed * (1f - _limp), dt, TurnRate * 1.6f, s);

            default:
                return dist > float.Clamp(Gun.Range * CloseInShare, 9f, 45f) && GoTowards(_lastSeen, walkSpeed * (1f - _limp), dt, TurnRate * 1.6f, s);
        }
    }

    /// <summary>A friend is in the line of fire: a few steps to one side for a clear shot (the other side if that way's blocked).</summary>
    private bool Sidestep(float3 along, float dt, float walkSpeed, NpcSenses s)
    {
        _sidestepLeft -= dt;
        var side = float3.Cross(float3.UnitY, along) * _sidestepSign;
        if (MoveAlong(side, walkSpeed * (1f - _limp), dt, TurnRate * 1.6f, s))
            return true;
        _sidestepSign = -_sidestepSign;
        return false;
    }

    /// <summary>Whether one of their own is where a shot from <paramref name="muzzle"/> along <paramref name="dir"/>, in a <paramref name="cone"/> rad cone, could hit them: in front of the target, or close behind it (a round that misses, or goes through, flies on).</summary>
    private bool FriendInLine(float3 muzzle, float3 dir, float dist, float cone, NpcSenses s, float beyond = FriendBeyond)
    {
        foreach (var other in s.People)
        {
            if (ReferenceEquals(other, this) || !other.Alive || other.Hidden || other.Zone != Zone || !Recognises(other))
                continue;
            var to = other.Position + new float3(0, 1.2f, 0) - muzzle;
            var ahead = float3.Dot(to, dir);
            if (ahead < 0.3f || ahead > dist + beyond)
                continue;
            var off = to - dir * ahead;
            var clearance = FriendInLineRadius + ahead * cone;
            if (float3.Dot(off, off) < clearance * clearance)
                return true;
        }

        return false;
    }

    /// <summary>Falling back from an enemy who's too close: at a run, back turned, so no shooting. Cornered, they stand and fight.</summary>
    private bool FallBack(float3 along, float dist, float dt, float runSpeed, NpcSenses s)
    {
        if (_fallBackLeft <= 0)
        {
            _fallBackLeft = FallBackTime;
            _steerLeft = 0;
            _burstLeft = 0;
            _lineUp = 0;
            Log.Write($"[NPC] {Name}: {TargetName} too close ({dist:0.0} m): falling back");
        }

        _fallBackLeft -= dt;
        _steerLeft -= dt;
        if (_steerLeft <= 0)
        {
            (_fleeDir, _fleeClear) = Steer(-along, s);
            _steerLeft = 0.3f;
            if (_fleeClear < 1f)
                _fallBackLeft = 0;
        }

        if (_fallBackLeft <= 0)
        {
            _fallBackWait = FallBackEvery;
            return false;
        }

        var moved = MoveAlong(_fleeDir, runSpeed * (1f - _limp), dt, TurnRate * 1.6f, s);
        if (moved)
            AddTrail();
        return moved;
    }

    /// <summary>The nearest ally in the fight for a coverer to stick with, a pusher by choice (someone else who's only covering is no use).</summary>
    private ArmedNpc? AllyToCover(NpcSenses s)
    {
        ArmedNpc? best = null;
        var bestScore = float.MaxValue;
        foreach (var other in s.People)
        {
            if (ReferenceEquals(other, this) || other is not ArmedNpc { Alive: true, State: NpcState.Fighting, Strategy: not NpcStrategy.Cover } ally
                || ally.Faction != Faction || ally.Zone != Zone)
                continue;
            var d = Distance(Position, ally.Position);
            if (d > CoverReach)
                continue;
            var score = d - (ally.Strategy == NpcStrategy.Push ? 15f : 0f);
            if (score < bestScore)
            {
                bestScore = score;
                best = ally;
            }
        }

        return best;
    }

    /// <summary>
    /// Covering an ally, with the enemy just gone from sight: keep firing at where they went, for a few seconds, to pin
    /// them while the ally moves up. (Not at someone who's down, nor at water: it'd stop the bullets.) Returns whether they are.
    /// </summary>
    private bool CoveringFire(float dt, NpcSenses s, float3 eye)
    {
        if (_covering is null || !_everSeen || _lostFor > SuppressTime || _watching is not null || _targetNpc is { Alive: false })
            return false;
        if (!_suppressing)
            Log.Write($"[NPC] {Name}: covering {_covering.Name}: firing on where {TargetName} went");
        _suppressing = true;
        FaceTowards(_lastSeen, dt, 2.2f);
        var to = _lastSeen - eye;
        var flat = new float3(to.X, 0, to.Z);
        var len = Length(flat);
        var facing = len > 1e-3f ? float3.Dot(flat / len, Ahead) : 1f;
        if (_reloadLeft <= 0 && facing > ShootCos && _cooldown <= 0 && len < Gun.Range)
            Shoot(s);
        return true;
    }

    private void AddTrail()
    {
        if ((_trail.Count == 0 || Distance2(Position, _trail[^1]) > 1f) && _trail.Count < 400)
            _trail.Add(Position);
    }

    private string TargetName => _targetIsPlayer ? "the player" : _targetNpc?.Name ?? "the shooter";

    // ------------------------------------------------------------------------------------------------ losing sight

    /// <summary>The enemy has just dropped out of sight (or they've come to fight one they haven't seen yet): search or hold.</summary>
    /// <param name="quiet">Something else is about to take over from it (a shot heard): no need to say what they're doing.</param>
    private void StartHunt(NpcSenses s, float3 eye, bool quiet = false)
    {
        // (After a sound, with nobody seen yet, even one who'd hold goes and searches the area: there's nowhere to hold aim on.)
        _hunt = _holds && _everSeen ? Hunt.Hold : Hunt.Search;
        _pushingFlash = false;
        _vanished = _lastSeen;
        _lead = Threat;
        _blocked = 0;
        _lookLeft = 0;
        _spotsLeft = SearchSpots;
        _huntLeft = _hunt == Hunt.Hold ? HoldTime + 10f * (float)_random.NextDouble()
            : SearchTime * SearchScale;
        _running = _suppressing = false;
        _fallBackLeft = 0;
        _chasing = null;
        _smokeVolleys = 0;
        _smokeRest = 0;
        _followedSince = s.Time - 2f;   // (sight is only checked a few times a second: they may have gone a moment ago)
        _followedThrough = false;
        _watching = _peering = null;
        _guarding = null;
        _lookedIn.Clear();
        // A searcher heads for where they were going: up to 3 m on from where they vanished.
        var lead = _seenVel * 1.2f;
        var len = Length(lead);
        if (len > 3f)
            lead *= 3f / len;
        _spot = new float3(_lastSeen.X + lead.X, _lastSeen.Y, _lastSeen.Z + lead.Z);

        // What their side knows (Game/SquadIntel.cs): someone's seen the player since they did, so that's where to start.
        var intel = s.Squads.For(Faction, Zone);
        var afterPlayer = EnemyOfPlayer(s) && (_targetIsPlayer || (!_everSeen && intel.Known(s.Time, 20f)));
        if (afterPlayer && intel.Known(s.Time, 20f) && intel.LastKnownAt > _lastSeenAt + 0.5f)
        {
            _lastSeen = _vanished = intel.LastKnown;
            var known = intel.Velocity * 1.2f;
            var knownLength = Length(known);
            if (knownLength > 3f)
                known *= 3f / knownLength;
            _spot = intel.LastKnown + known with { Y = 0 };
            Threat = _lead = new float3(_spot.X, Position.Y, _spot.Z);
            _targetIsPlayer = true;
        }

        // Searching for the player, they search together: the places round where they went that nobody's looked at yet.
        _squadSearch = _hunt == Hunt.Search && afterPlayer;
        _clearLook = 0f;
        // (While someone of their side still has the player in sight, there's nothing to search: they go where the player is.
        // The search starts when the last of them loses sight.)
        if (_squadSearch && !intel.InSightNow(s.Time) && intel.BeginSearch(_vanished, eye, Position.Y, s, Zone))
            Say(_everSeen ? Bark.LostHim : Bark.Searching, s);
        else if (_squadSearch && !intel.InSightNow(s.Time))
            Say(Bark.Searching, s);

        // Shot at by someone they never saw: they go looking for where it came from, for longer, and get help with it.
        if (!_everSeen && _hunt == Hunt.Search && FiredOnLately(s))
        {
            _spotsLeft += 2;
            _huntLeft *= 1.5f;
            CallForHelpLimited(_lastSeen, s, $"{Name} was shot at by someone they can't see: calling for help finding them");
        }

        if (quiet)
            return;

        string why;
        if (!_everSeen)
            why = "on the alert after a sound";
        else if (_targetNpc is { Alive: false })
            why = $"{_targetNpc.Name} is down";
        else
        {
            var now = _targetIsPlayer ? s.PlayerChest : (_targetNpc?.Position ?? _lastSeen) + new float3(0, 1.3f, 0);
            var hidden = Level.ZoneAt(now) != Zone ? "gone"
                : _targetIsPlayer && s.Visibility(eye, now) > 0f ? "in the dark"
                : s.HiddenBy(eye, now);
            why = $"lost sight of {TargetName} ({hidden}, {Distance(Position, _lastSeen):0.0} m)";
        }

        Log.Write($"[NPC] {Name}: {why}: {(_hunt == Hunt.Hold ? $"holding aim on where they were for {_huntLeft:0} s" : "searching")}");
    }

    // ------------------------------------------------------------------------------------------------ the player's gunshots

    /// <summary>They've heard the player fire and can't see anyone: into cover, or fire at the flash (<see cref="HeardPlayerShot"/>).</summary>
    private void FollowShotOrder(NpcSenses s, float3 eye)
    {
        var order = _shotOrder;
        _shotOrder = ShotOrder.None;
        if (_hunt == Hunt.Engage)
            StartHunt(s, eye, quiet: true);
        _chasing = _peering = _guarding = null;
        _squadSearch = false;   // (the shot says where they are: no need to search round)
        _running = false;
        var dist = Distance(Position, _shotAt);
        if (order == ShotOrder.FireAtFlash && PushesOnFlash)
        {
            // Their kind of fight: straight at it, at a run, firing at it as they come.
            if (!_pushingFlash)
                Log.Write($"[NPC] {Name}: saw the muzzle flash {dist:0.0} m away but can't see the player: pushing towards it");
            _hunt = Hunt.Search;
            _pushingFlash = true;
            _flashLeft = FlashFireTime;
            _vanished = _lastSeen = _shotAt;
            _spot = _shotAt;
            Threat = _lead = _spot;
            _spotsLeft = SearchSpots;
            _lookLeft = 0;
            _blocked = 0;
            _lineUp = 0;
            _huntLeft = float.Max(_huntLeft, SearchTime * SearchScale);
            return;
        }

        if (order == ShotOrder.FireAtFlash)
        {
            if (_hunt != Hunt.FireAtFlash)
            {
                _lineUp = 0;
                Log.Write($"[NPC] {Name}: saw the muzzle flash {dist:0.0} m away but can't see the player (light {s.PlayerLight:0.00}): firing at the flash");
            }

            _hunt = Hunt.FireAtFlash;
            _flashLeft = FlashFireTime;
            return;
        }

        if (Profile.IsCommando && _hunt != Hunt.Cover)
        {
            // A commando doesn't go to ground over a shot they can't place: they go and find whoever fired it, gun up.
            // (Only fire that keeps coming puts them behind cover: ArmedNpcCover.cs, CommandoPinnedAt.)
            Log.Write($"[NPC] {Name}: heard a shot {dist:0.0} m away, no sight of the flash: moving up on it");
            _hunt = Hunt.Search;
            _vanished = _lastSeen = _shotAt;
            _spot = _shotAt;
            Threat = _lead = _spot;
            _spotsLeft = SearchSpots;
            _lookLeft = 0;
            _blocked = 0;
            _lineUp = 0;
            _huntLeft = float.Max(_huntLeft, SearchTime * SearchScale);
            return;
        }

        if (_hunt == Hunt.Cover)
        {
            // Already going to ground: more shots keep them there, and from somewhere else may mean finding another spot.
            _coverLeft = float.Min(_coverLeft + CoverShotAdds, CoverTimeMax);
            if (_inCover && Distance(_shotAt, _coverFrom) > CoverShotMoved && !Sheltered(Position, _shotAt, s))
            {
                _coverWanted = true;
                _inCover = false;
            }

            return;
        }

        Log.Write($"[NPC] {Name}: heard a shot {dist:0.0} m away, no sight of the flash: going for cover");
        _hunt = Hunt.Cover;
        _coverWanted = true;
        _inCover = false;
        _coverSpot = Position;
        _blocked = 0;
        _coverLeft = (CoverTime + 6f * (float)_random.NextDouble()) * (Strategy == NpcStrategy.Push ? 0.5f : 1f);
    }

    /// <summary>Finding a spot, running for it, then staying down with the gun on where the shot came from. Returns whether they moved.</summary>
    private bool UpdateCover(float dt, float runSpeed, NpcSenses s)
    {
        if (_coverWanted)
        {
            if (s.CoverSearchesLeft <= 0)
            {
                FaceTowards(_shotAt, dt, 2.2f);
                return false;   // (everyone heard it at once: wait for a free search)
            }

            s.CoverSearchesLeft--;
            _coverWanted = false;
            if (!FindCover(s))
            {
                Log.Write($"[NPC] {Name}: no cover within reach: {(_holds ? "holding aim on the shot" : "going to look")}");
                LeaveShotReaction(s);
                return false;
            }
        }

        if (!_inCover)
        {
            var way = _coverSpot - Position;
            way.Y = 0;
            var dist = Length(way);
            if (dist < 0.5f || _blocked > 1f)
            {
                _inCover = true;
                _blocked = 0;
                Log.Write($"[NPC] {Name}: in cover {Distance(Position, _shotAt):0.0} m from the shot");
                return false;
            }

            if (GoTowards(_coverSpot, runSpeed * (1f - _limp), dt, TurnRate * 2f, s))
            {
                _blocked = 0;
                AddTrail();
                return true;
            }

            _blocked += dt;
            return false;
        }

        // Down behind it, gun on where the shot came from; primed, in case someone comes round the corner there.
        FaceTowards(_shotAt, dt, 2.2f);
        _coverLeft -= dt;
        if (_coverLeft <= 0)
        {
            Log.Write($"[NPC] {Name}: out of cover after {_lostFor:0} s: {(_holds ? "holding aim on the shot" : "going to look")}");
            LeaveShotReaction(s);
        }

        return false;
    }

    /// <summary>
    /// The nearest spot, in rings going outwards, that's sheltered from a shot at <see cref="_shotAt"/> and that they can
    /// run straight to on solid ground; of a ring's spots, the one furthest from the shot. If they're sheltered where they
    /// stand, they stay. False: there's nowhere.
    /// </summary>
    private bool FindCover(NpcSenses s)
    {
        using var coverTimer = Perf.CoverSearch.Time(Name);
        _coverFrom = _shotAt;
        if (Sheltered(Position, _shotAt, s))
        {
            _coverSpot = Position;
            _inCover = true;
            Log.Write($"[NPC] {Name}: already behind cover, staying down");
            return true;
        }

        var here = Distance(Position, _shotAt);
        foreach (var ring in CoverRings)
        {
            if (ring > CoverReachFor)
                break;   // (a defensive one only ducks behind something close by)
            var best = float.MinValue;
            var found = false;
            for (var i = 0; i < CoverDirections; i++)
            {
                var a = (i + 0.37f) * float.Tau / CoverDirections;
                var dir = new float3(float.Sin(a), 0, float.Cos(a));
                var p = Position + dir * ring;
                var fromShot = Distance(p, _shotAt);
                if (fromShot < CoverNotNear || fromShot <= best || CoverTaken(p, s) || Level.ZoneAt(p + new float3(0, 1f, 0)) != Zone)
                    continue;
                if (!Sheltered(p, _shotAt, s) || !CanRun(p, dir, s) || !OnGround(p, s))
                    continue;
                best = fromShot;
                _coverSpot = p;
                found = true;
            }

            if (found)
            {
                Log.Write($"[NPC] {Name}: running for cover {Distance(Position, _coverSpot):0.0} m away ({here:0.0} -> {best:0.0} m from the shot)");
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether someone standing at <paramref name="p"/> is out of sight of <paramref name="from"/>, head and chest, with something solid right next to them on that side.</summary>
    private static bool Sheltered(float3 p, float3 from, NpcSenses s)
    {
        var chest = p + new float3(0, 1.25f, 0);
        var blocked = s.Clear(chest, from);
        var dist = Length(from - chest);
        return blocked * dist < CoverHug && dist > 0.1f && s.Clear(p + new float3(0, EyeHeight, 0), from) < 0.995f;
    }

    /// <summary>Whether they can run straight to <paramref name="to"/>, going along <paramref name="dir"/>, knee and chest clear, with room to arrive.</summary>
    private bool CanRun(float3 to, float3 dir, NpcSenses s)
    {
        var last = to + dir * (BodyRadius + 0.1f);
        foreach (var h in (ReadOnlySpan<float>)[0.6f, 1.25f])
        {
            var up = new float3(0, h, 0);
            if (s.Clear(Position + up, last + up) < 0.999f)
                return false;
        }

        return true;
    }

    private static bool OnGround(float3 p, NpcSenses s) => s.Clear(p + new float3(0, 0.5f, 0), p - new float3(0, 0.6f, 0)) < 1f;

    /// <summary>Someone else has that spot, or is on their way to it.</summary>
    private bool CoverTaken(float3 p, NpcSenses s)
    {
        foreach (var other in s.People)
        {
            if (ReferenceEquals(other, this) || other is not ArmedNpc { Alive: true } armed)
                continue;
            if (armed._hunt == Hunt.Cover && Distance(armed._coverSpot, p) < CoverShared)
                return true;
            if (armed._cf != CoverFight.None && (Distance(armed._cfCover, p) < CoverShared || Distance(armed._cfPeek, p) < CoverShared))
                return true;
        }

        return false;
    }

    /// <summary>One frame of firing at where the flash was. Returns false: they stand and shoot.</summary>
    private bool UpdateFireAtFlash(float dt, NpcSenses s, float3 eye)
    {
        _flashLeft -= dt;
        FaceTowards(_shotAt, dt, 2.2f);
        var to = _shotAt - eye;
        var flat = new float3(to.X, 0, to.Z);
        var len = Length(flat);
        var facing = len > 1e-3f ? float3.Dot(flat / len, Ahead) : 1f;
        _lineUp = facing > ShootCos ? _lineUp + dt : 0f;
        if (_reloadLeft <= 0 && _lineUp > FlashLineUp && _cooldown <= 0 && len < FlashFireRange)
        {
            _lastSeen = _shotAt;
            _firingBlind = true;
            Shoot(s);
            _firingBlind = false;
        }

        if (_flashLeft <= 0)
        {
            Log.Write($"[NPC] {Name}: stopped firing at the flash, {Ammo} rounds left: {(_holds ? "holding aim on it" : "going to look")}");
            LeaveShotReaction(s);
        }

        return false;
    }

    /// <summary>Pushing at a flash: a round at it whenever they're facing it, lined up and ready (they're turning towards it as they go).</summary>
    private void FireAtFlashOnTheMove(float dt, NpcSenses s, float3 eye)
    {
        var to = _shotAt - eye;
        var flat = new float3(to.X, 0, to.Z);
        var len = Length(flat);
        var facing = len > 1e-3f ? float3.Dot(flat / len, Ahead) : 1f;
        _lineUp = facing > ShootCos ? _lineUp + dt : 0f;
        if (_reloadLeft > 0 || _lineUp <= FlashLineUp || _cooldown > 0 || len >= FlashFireRange || len < 2f)
            return;
        _lastSeen = _shotAt;
        _firingBlind = true;
        Shoot(s);
        _firingBlind = false;
    }

    /// <summary>Done with the shot: on to their usual ways, with where it came from as the place to hold aim on, or to go and look.</summary>
    /// <summary>
    /// Out of cover (or with none to take): hold aim on where the shot came from, or go and look. Someone who's been shot
    /// at themself knows the shooter is out there: unless posted somewhere, they go looking for where it came from, for
    /// longer, and call those of their side close by to help them find them.
    /// </summary>
    private void LeaveShotReaction(NpcSenses s)
    {
        _pushingFlash = false;
        _squadSearch = false;
        var hunting = FiredOnLately(s) && !HoldsPost;
        _hunt = _holds && !hunting ? Hunt.Hold : Hunt.Search;
        _vanished = _lastSeen = _shotAt;
        _spot = _shotAt;
        Threat = _lead = _spot;
        _spotsLeft = SearchSpots + (hunting ? 2 : 0);
        _lookLeft = 0;
        _blocked = 0;
        _huntLeft = _hunt == Hunt.Hold ? HoldTime : SearchTime * SearchScale * (hunting ? 1.5f : 1f);
        if (hunting)
        {
            Log.Write($"[NPC] {Name}: was shot at: going to find the shooter, {Distance(Position, _shotAt):0.0} m off");
            CallForHelpLimited(_shotAt, s, $"{Name} called for help finding the shooter");
        }
    }

    // ------------------------------------------------------------------------------------------------ flares

    /// <summary>
    /// The player has gone from sight somewhere dark: someone carrying flares stops and lights it up, thrown from the off
    /// hand, or fired high over it out of a long gun's launcher, from further off. Returns whether they're busy with it.
    /// </summary>
    private bool UpdateFlare(float dt, NpcSenses s, float3 eye)
    {
        if (_flareWind > 0f)
        {
            if (_targetVisible)
            {
                // There they are: never mind that.
                DropFlareThrow(0.08f);
                _reAim = 0.02f;
                return false;
            }

            FaceTowards(_flareTowards, dt, 2.2f);
            if (!_launches && _handL >= 0)
                s.Flares?.GlowInHand((_world[_handL] * Transform).Translation);
            _flareWind -= dt;
            if (_flareWind <= 0f)
                ReleaseFlare(s, eye);
            return true;
        }

        if (_flares <= 0 || s.Flares is not { } flares || s.LightAt is not { } lightAt || _targetVisible || _reloadLeft > 0 || Climbing)
            return false;
        _flareLook -= dt;
        if (_flareLook > 0f)
            return false;
        _flareLook = FlareLookEvery * (1f + (float)_random.NextDouble());

        // It's the player they're after, and where they think the player is. (Fighting someone else, they aren't hidden by the dark.)
        if (_hunt == Hunt.Engage || _targetNpc is not null || !EnemyOfPlayer(s) || !flares.RoomForOne || (_everSeen && _lostFor < 0.6f))
            return false;
        var lead = _hunt switch { Hunt.Cover or Hunt.FireAtFlash => _shotAt, Hunt.Hold => _vanished, _ => _spot };
        var distance = Distance(Position, lead);
        var reach = _launches ? FlareLaunchRange : FlareThrowRange;
        if (distance < FlareMinRange || distance > reach || Level.ZoneAt(lead) != Zone || flares.Near(lead, FlareClear) || lightAt(lead) >= FlareDark)
            return false;

        var from = _launches ? Muzzle : eye + float3.Normalize(lead - eye) * 0.35f;
        if (_launches && _armTuck > TuckedTooFar)
            return false;   // (the gun's swung down out of a wall)
        var planned = _launches
            ? flares.PlanHigh(from, lead, out _flareVelocity, out var landing)
            : flares.Plan(from, lead, FlareThrowSpeed, low: false, out _flareVelocity, out landing);
        if (!planned)
            return false;

        flares.Claim(landing, sky: _launches);
        _flareTowards = lead;
        _flareWind = _launches ? FlareLaunchTime : FlareThrowTime;
        _burstLeft = 0;
        if (!_launches)
        {
            flares.Strike(eye);
            _animator.PlayOverlay("ThrowFlare", _upperBody, 0.1f, 0f);
        }

        Log.Write($"[NPC] {Name}: lost the player in the dark (light {lightAt(lead):0.00} there): {(_launches ? "firing an illumination flare up over" : "throwing a flare")} {distance:0.0} m off to light it up");
        return true;
    }

    /// <summary>Away it goes: from the hand, or the muzzle.</summary>
    private void ReleaseFlare(NpcSenses s, float3 eye)
    {
        var from = _launches ? Muzzle : _handL >= 0 ? (_world[_handL] * Transform).Translation : eye;
        if (s.Clear(eye, from) < 0.99f)
            from = eye;   // (not out through a wall they're standing against)
        s.Flares?.Launch(from, _flareVelocity, fired: _launches, high: _launches);
        _flares--;
        _reAim = FlareAfter;
        if (_launches)
            _animator.PlayOverlay(FireClip, _upperBody, 0.02f, 0f);
    }

    /// <summary>Thrown off in the middle of it (or finished with the fight): no flare.</summary>
    private void DropFlareThrow(float fade)
    {
        if (_flareWind <= 0f)
            return;
        _flareWind = 0f;
        if (!_launches)
            _animator.Stop("ThrowFlare", fade);
    }

    // ------------------------------------------------------------------------------------------------ hunting

    /// <summary>One frame of searching or holding (no enemy in sight). Returns whether they moved.</summary>
    private bool UpdateHunt(float dt, float walkSpeed, float runSpeed, NpcSenses s, float3 eye)
    {
        if (_hunt == Hunt.FireAtFlash)
            return UpdateFireAtFlash(dt, s, eye);
        if (_hunt == Hunt.FireIntoSmoke)
            return UpdateFireIntoSmoke(dt, s, eye);
        if (_hunt == Hunt.Cover)
            return UpdateCover(dt, runSpeed, s);

        if (_orderTo is null)
            _huntLeft -= dt;   // (on their way somewhere they were sent, they don't give up)

        if (_glimpse is { } glimpse)
            WatchThroughPortal(glimpse, s);

        // A shot heard somewhere else (Alarm moves Threat): a new lead (unless it was one of their own side firing). A
        // holder swings the gun onto it; a searcher goes.
        var heard = _orderTo is null && Distance(Threat, _lead) > 2.5f;
        if (heard)
        {
            var focus = _hunt == Hunt.Hold ? new float3(_vanished.X, Threat.Y, _vanished.Z) : _spot with { Y = Threat.Y };
            heard = Distance(Threat, focus) > 2.5f
                    && !s.People.Any(p => p.Alive && p.Faction == Faction && Distance(p.Position, Threat) < 2f);
            _lead = Threat;
        }

        if (heard)
        {
            _vanished = float.Abs(_threatRawY - Position.Y) < 2.2f ? Threat + new float3(0, 1.3f, 0) : ThreatAt;
            _spot = _vanished;
            _lookLeft = 0;
            _blocked = 0;
            _peering = null;
            _spotsLeft = SearchSpots;
            _huntLeft = float.Max(_huntLeft, _hunt == Hunt.Hold ? HoldTime * 0.6f : SearchTime * 0.6f);
            Log.Write($"[NPC] {Name}: heard something {Distance(Position, Threat):0.0} m away: {(_hunt == Hunt.Hold ? "swinging the aim onto it" : "going to look")}");
        }

        // The player went through a portal: a searcher goes after them.
        if (_chasing is null && _hunt == Hunt.Search && _targetIsPlayer && _everSeen)
            LookForCrossing(s);
        if (_chasing is not null)
            return ChasePortal(dt, runSpeed, s);
        if (CoveringFire(dt, s, eye))
            return false;

        if (_huntLeft <= 0)
        {
            // An aggressive holder gets impatient and goes to look (unless holding back is the plan), as does one who's been
            // shot at (someone out there is after them: they go and find them); anyone else stands down.
            if (_hunt == Hunt.Hold && !HoldsPost && ((Aggression == Aggression.Aggressive && Strategy != NpcStrategy.HoldBack) || FiredOnLately(s)))
            {
                _hunt = Hunt.Search;
                _huntLeft = SearchTime * 0.7f;
                _spot = _vanished;
                _lookLeft = 0;
                _blocked = 0;
                _spotsLeft = SearchSpots;
                Log.Write($"[NPC] {Name}: nothing for {_lostFor:0} s: moving up to search");
            }
            else
            {
                Log.Write($"[NPC] {Name}: gives up {(_hunt == Hunt.Hold ? "waiting" : "the search")} after {_lostFor:0} s");
                if (_everSeen || _squadSearch)
                    Say(Bark.AllClear, s);
                SetState(NpcState.Returning);
                return false;
            }
        }

        if (_hunt == Hunt.Hold)
        {
            // Gun on the spot where they vanished, primed: when they show again, the shot comes quickly.
            _lastSeen = _vanished;
            FaceTowards(_vanished, dt, 2.2f);
            var to = _vanished - eye;
            var flat = new float3(to.X, 0, to.Z);
            var len = Length(flat);
            var facing = len > 1e-3f ? float3.Dot(flat / len, Ahead) : 1f;
            _lineUp = facing > ShootCos ? _lineUp + dt : 0f;
            return false;
        }

        _lineUp = 0;
        if (_lookLeft > 0)
        {
            // At the spot: look round, sweeping either side of the way they came in.
            _lookLeft -= dt;
            var t = LookRoundTime - _lookLeft;
            if (_peering is { } portal)
            {
                // At a portal: looking in, from one side of the doorway to the other.
                var across = float3.Cross(float3.UnitY, portal.Normal);
                FaceTowards(portal.Center + across * (portal.Radius * float.Sin(t * 1.9f)), dt, 1.4f);
            }
            else
            {
                Heading = Wrap(_lookFrom + 1.3f * float.Sin(t * 1.9f));
            }

            if (_lookLeft <= 0)
                NextSpot(s);
            return false;
        }

        // Covering someone on the hunt: a few metres behind them, wherever they go.
        if (_covering is { } ally && _peering is null && _guarding is null)
        {
            var behind = Position - ally.Position;
            behind.Y = 0;
            var apart = Length(behind);
            if (apart > CoverFollow + 1f)
                _spot = ally.Position + behind / apart * CoverFollow;
        }

        var way = _spot - Position;
        way.Y = 0;
        var dist = Length(way);
        // (Moving up on a portal, they keep at it round whatever's in the way for longer, as when chasing through one.)
        var givesUp = _guarding is null && _orderTo is null ? _blocked > 1.5f : _blocked > 6f || (_blocked > 1.5f && dist < 3f);
        if ((dist < 0.8f && float.Abs(_spot.Y - Position.Y) < 2f) || givesUp)
        {
            if (_orderTo is { } ordered && _spot == ordered && ReachedOrder(s))
                return false;

            if (_guarding is { } guarded)
            {
                // Up to the portal that came on (or as near as they can get): stand there, gun on the water, for whatever comes out.
                _guarding = null;
                _hunt = Hunt.Hold;
                _vanished = _lastSeen = guarded.Center;
                _huntLeft = HoldTime + 10f * (float)_random.NextDouble();
                _lineUp = 0;
                _blocked = 0;
                Log.Write($"[NPC] {Name}: covering the {guarded.Name} from {Distance(Position, guarded.Center):0.0} m, gun on the water");
                return false;
            }

            // Arrived (or can't get any closer): look round here.
            if (_pushingFlash)
            {
                _pushingFlash = false;
                _running = false;
                Log.Write($"[NPC] {Name}: pushed up to where the flash was: nobody in sight, searching");
            }
            _lookLeft = LookRoundTime * (Aggression == Aggression.Cautious ? CautiousLookScale : 1f);
            _lookFrom = Heading;
            _blocked = 0;
            if (_squadSearch)
                s.Squads.For(Faction, Zone).ClearFrom(eye, Position, s, 4);   // (here, and what they can see from here, is looked at)
            return false;
        }

        // Searching with their side: what they pass in plain view on the way is looked at too.
        _clearLook -= dt;
        if (_squadSearch && _clearLook <= 0f)
        {
            _clearLook = 0.6f;
            s.Squads.For(Faction, Zone).ClearFrom(eye, Position, s, 2);
        }

        // Walk there, gun up; the first leg a little quicker. A pusher runs that leg. Pushing at a flash, at a run, and
        // while the flash is fresh, firing at it whenever they're square on to it.
        var speed = _orderTo is not null && _spot == _orderTo ? runSpeed * 0.85f * (1f - _limp)
            : _pushingFlash ? runSpeed * PushFlashPace * (1f - _limp)
            : Aggression == Aggression.Cautious ? walkSpeed * CautiousPace * (1f - _limp)
            : (Strategy == NpcStrategy.Push && _spotsLeft == SearchSpots ? runSpeed * 0.8f : walkSpeed * (_spotsLeft == SearchSpots ? 1f : 0.8f)) * (1f - _limp);
        if (_pushingFlash)
        {
            _running = true;
            _flashLeft -= dt;
            if (_flashLeft > 0f)
                FireAtFlashOnTheMove(dt, s, eye);
        }
        if (GoTowards(_spot, speed, dt, TurnRate * 1.4f, s))
        {
            _blocked = 0;
            AddTrail();
            return true;
        }

        _blocked += dt;
        return false;
    }

    /// <summary>
    /// The player is in view through a portal, where a bullet won't reach them (the water stops it). The trail starts at
    /// the water: a holder keeps the gun on it, for when they come back through (an aggressive one loses patience as
    /// usual, and then comes after them); a searcher goes through. From behind a freestanding portal it's only a window,
    /// and stepping through wouldn't get there: they go up to it and keep watching.
    /// </summary>
    private void WatchThroughPortal(in PortalSight seen, NpcSenses s)
    {
        if (_watching != seen.Portal)
            Log.Write($"[NPC] {Name}: sees the player through the {seen.Portal.Name} ({seen.Distance:0.0} m): {(_hunt == Hunt.Hold ? "aiming at the water" : "going after them")}");
        _watching = seen.Portal;
        _targetIsPlayer = true;
        _targetNpc = null;
        _everSeen = true;
        _vanished = seen.Entry;
        _seenVel = float3.Zero;
        Threat = _lead = new float3(seen.Entry.X, Position.Y, seen.Entry.Z);

        if (_hunt == Hunt.Hold)
        {
            if (Aggression != Aggression.Aggressive)
                _huntLeft = float.Max(_huntLeft, HoldTime * 0.6f);
            return;
        }

        _huntLeft = float.Max(_huntLeft, SearchTime * 0.6f);
        if (seen.Window)
        {
            _spot = Threat;
            _lookLeft = 0;
        }
        else if (_chasing is null)
        {
            _chasing = seen.Portal;
            _followedSince = s.Time;
            _blocked = 0;
        }
    }

    /// <summary>
    /// A portal in this part of the level that the player walked into since the hunt began (the first one only if it's
    /// near where they vanished: they saw them go that way; after that, they're on the trail).
    /// </summary>
    private void LookForCrossing(NpcSenses s)
    {
        foreach (var c in s.PlayerCrossings)
        {
            if (c.Time < _followedSince || !c.Entered.Online)
                continue;
            var approach = PortalApproach(c.Entered, s);
            if (Level.ZoneAt(approach + new float3(0, 1f, 0)) != Zone)
                continue;
            if (!_followedThrough && Distance(approach, new float3(_vanished.X, approach.Y, _vanished.Z)) > 10f)
                continue;
            _chasing = c.Entered;
            _followedSince = c.Time + 1e-3f;
            _blocked = 0;
            Log.Write($"[NPC] {Name}: the player went through the {c.Entered.Name}: going after them ({Distance(Position, approach):0.0} m)");
            return;
        }
    }

    /// <summary>Walking to the portal the player took, then through it: hunting on the far side.</summary>
    private bool ChasePortal(float dt, float runSpeed, NpcSenses s)
    {
        var portal = _chasing!;
        if (!portal.Online)
        {
            _chasing = null;
            Log.Write($"[NPC] {Name}: the {portal.Name} went dark: searching here");
            NextSpot(s);
            return false;
        }

        var approach = PortalApproach(portal, s);
        var way = approach - Position;
        way.Y = 0;
        var dist = Length(way);
        if (float.Abs(approach.Y - Position.Y) > 2f)
            dist = float.Max(dist, 3f);   // (on another floor: not there yet, however near it looks)
        if (_blocked > 6f && dist >= 2.5f)
        {
            // There's no way to it from here (a door's been shut, or it's on another floor).
            _chasing = null;
            _blocked = 0;
            Log.Write($"[NPC] {Name}: can't get to the {portal.Name}: searching here");
            NextSpot(s);
            return false;
        }

        if (dist < 0.6f || (_blocked > 1.5f && dist < 2.5f))
        {
            _chasing = null;
            var exit = portal.Linked!;
            GoThrough(portal, s);
            _followedThrough = true;
            _glimpse = null;
            _watching = _peering = null;
            _lookedIn.Clear();
            _lookedIn.Add(exit);   // (they've just come out of it)
            _retarget = 0;
            // Out the other side: the player went on from here somewhere. Look a few metres out first.
            var ahead = PortalApproach(exit, s, 3f);
            _vanished = _lastSeen = ahead + new float3(0, 1.3f, 0);
            _spot = ahead;
            Threat = _lead = ahead;
            _seenVel = float3.Zero;
            _lookLeft = 0;
            _blocked = 0;
            _spotsLeft = SearchSpots;
            _huntLeft = float.Max(_huntLeft, SearchTime * 0.8f);
            return false;
        }

        // At a jog: the trail is going cold.
        if (GoTowards(approach, runSpeed * 0.8f * (1f - _limp), dt, TurnRate * 1.6f, s))
        {
            _blocked = 0;
            AddTrail();
            return true;
        }

        _blocked += dt;
        return false;
    }

    /// <summary>The next place to look: a few metres from where the enemy vanished, somewhere they could have gone.</summary>
    private void NextSpot(NpcSenses s)
    {
        // Any portal near here first: they could be on the far side, and it only takes a look.
        _peering = PortalToLookIn(s);
        if (_peering is not null)
        {
            _lookedIn.Add(_peering);
            _spot = PortalApproach(_peering, s, PeerDistance);
            _blocked = 0;
            Log.Write($"[NPC] {Name}: going to look in the {_peering.Name} ({Distance(Position, _spot):0.0} m)");
            return;
        }

        // Searching with their side: the nearest place nobody's looked at, or is on their way to (Game/SquadIntel.cs).
        if (_squadSearch && s.Squads.For(Faction, Zone) is { Points.Count: > 0 } intel)
        {
            intel.ClearFrom(Position + new float3(0, EyeHeight, 0), Position, s, 6);
            if (intel.Claim(this, Position, s, Zone) is { } point)
            {
                _spot = point.Position;
                _blocked = 0;
                Log.Write($"[NPC] {Name}: searching ({point.Position.X:0.0}, {point.Position.Z:0.0}), {Distance(Position, point.Position):0.0} m off ({intel.Left} places left to look)");
                return;
            }

            Log.Write($"[NPC] {Name}: everywhere round there's been looked at");
            _huntLeft = 0;   // (give up next frame)
            return;
        }

        if (_spotsLeft-- <= 0)
        {
            _huntLeft = 0;   // swept everywhere: give up next frame
            return;
        }

        var centre = _vanished;
        centre.Y = s.Nav?.FloorHeight(centre, Position.Y) ?? Position.Y;
        var best = centre;
        var bestScore = float.MinValue;
        for (var i = 0; i < 8; i++)
        {
            var a = (float)_random.NextDouble() * float.Tau;
            var r = 3f + 4f * (float)_random.NextDouble();
            var p = centre + new float3(float.Sin(a), 0, float.Cos(a)) * r;
            var knee = new float3(0, 0.6f, 0);
            if (Level.ZoneAt(p + new float3(0, 1f, 0)) != Zone || s.Nav?.Walkable(p) == false)
                continue;
            // Somewhere they can walk to from here, and away from where they've just looked.
            var score = s.Clear(Position + knee, p + knee) * 2f + Length(p - Position) * 0.1f;
            if (score > bestScore)
            {
                bestScore = score;
                best = p;
            }
        }

        _spot = best;
        _blocked = 0;
    }

    /// <summary>The nearest working portal they haven't looked into yet, near them or where the enemy vanished, that they can walk straight up to.</summary>
    private Portal? PortalToLookIn(NpcSenses s)
    {
        var knee = new float3(0, 0.6f, 0);
        Portal? nearest = null;
        var best = PortalSearchRange;
        foreach (var portal in s.Portals)
        {
            if (!portal.Online || _lookedIn.Contains(portal))
                continue;
            var near = float.Min(Distance(portal.Center, Position), Distance(portal.Center, _vanished));
            if (near >= best)
                continue;
            var spot = PortalApproach(portal, s, PeerDistance);
            if (Level.ZoneAt(spot + new float3(0, 1f, 0)) != Zone || s.Clear(Position + knee, spot + knee) < 0.99f)
                continue;
            nearest = portal;
            best = near;
        }

        return nearest;
    }

    /// <summary>The nearest enemy in line of sight (anywhere round them: they're alert), or keep the last one.</summary>
    private void PickTarget(NpcSenses s)
    {
        _covering = Strategy == NpcStrategy.Cover ? AllyToCover(s) : null;
        var eye = Position + new float3(0, OnGun ? GunEye : EyeHeight, 0);
        var best = OnGun ? GunRange : float.Max(Gun.Range, float.Max(EngageRange, NpcSenses.FullSight * SightReach(true)));   // (beyond their gun's range they track them and hold fire)
        Npc? bestNpc = null;
        var bestPlayer = false;
        var bestAt = float3.Zero;

        if (s.PlayerAlive && EnemyOfPlayer(s) && Level.ZoneAt(s.PlayerEye) == Zone)
        {
            // The chest, or the head if that's all that shows (over a bush or a wall).
            var at = s.PlayerChest;
            var d = Length(at - eye);
            if (d < best && s.SeePlayer(eye, at, 0.99f, SightReach(true)) <= 0f && s.SeePlayer(eye, s.PlayerEye, 0.995f, SightReach(true)) > 0f)
                at = s.PlayerEye;
            if (d < best && s.SeePlayer(eye, at, 0.99f, SightReach(true)) > 0f)
            {
                best = d;
                bestPlayer = true;
                bestAt = at;
            }
        }

        foreach (var other in s.People)
        {
            if (ReferenceEquals(other, this) || !other.Alive || other.Scripted || other.Zone != Zone || !EnemyOf(other))
                continue;
            var at = other.Position + new float3(0, other.AimHeight, 0);
            var d = Length(at - eye);
            // Stick with the current target unless someone is clearly nearer.
            if (ReferenceEquals(other, _targetNpc))
                d *= 0.8f;
            if (d < best && s.Visibility(eye, at) > 0f)
            {
                best = d;
                bestNpc = other;
                bestPlayer = false;
                bestAt = at;
            }
        }

        var changed = bestPlayer != _targetIsPlayer || !ReferenceEquals(bestNpc, _targetNpc);
        _targetVisible = bestPlayer || bestNpc is not null;
        _glimpse = null;
        if (!_targetVisible)
        {
            // Nobody to shoot at: but the player may be in view through a portal, the chest or just the head.
            if (s.PlayerAlive && EnemyOfPlayer(s)
                && (s.ThroughPortal(eye, s.PlayerChest, EngageRange, out var sight)
                    || s.ThroughPortal(eye, s.PlayerEye, EngageRange, out sight, 0.995f))
                && s.PlayerLit(sight.Distance) > 0f)
                _glimpse = sight;
            return;
        }
        if (changed)
        {
            _lineUp = 0;
            _firedOnTarget = false;
            _seenVel = float3.Zero;
            Log.Write($"[NPC] {Name} targets {(bestPlayer ? "the player" : bestNpc!.Name)} ({best:0.0} m)");
        }
        else if (_hunt == Hunt.Engage)
        {
            // Which way they're going (where a searcher looks first if they vanish, and how far a shot leads them).
            var v = (bestAt - _lastSeen) / float.Clamp(s.Time - _lastSeenAt, 0.1f, 0.5f);
            v.Y = 0;
            _seenVel += (v - _seenVel) * 0.5f;
        }

        _targetIsPlayer = bestPlayer;
        _targetNpc = bestNpc;
        _lastSeen = bestAt;
        _lastSeenAt = s.Time;
        _contact = true;
        if (bestPlayer)
            s.Squads.For(Faction, Zone).Report(bestAt, _seenVel, s.Time);   // (what their side knows: Game/SquadIntel.cs)
    }

    private void Shoot(NpcSenses s)
    {
        if (s.Fire is null)
            return;
        // Too close to a wall, gun swung down out of it: not until they've backed off (a round from there would start beyond the wall).
        if (_armTuck > TuckedTooFar)
        {
            _cooldown = 0.1f;
            return;
        }

        var muzzle = Muzzle;
        var aimAt = _lastSeen;
        var lateral = 0f;
        if (!_firingBlind && _targetVisible)
        {
            // Leading a moving target: where it'll be by the time the round gets there, reckoned from how it's been moving
            // (and from when they last looked: a few times a second). A good shot judges it better.
            var flight0 = Length(_lastSeen - muzzle) / Gun.BulletSpeed;
            var ahead = float.Clamp(s.Time - _lastSeenAt, 0f, 0.4f) + flight0;
            var judged = LeadSkill + (1f - LeadSkill) * Profile.Accuracy;
            aimAt = _lastSeen + _seenVel * (ahead * judged);
            // How fast it crosses their line of fire: the harder to hold a gun on.
            var line = float3.Normalize(_lastSeen - muzzle);
            var across = _seenVel - line * float3.Dot(_seenVel, line);
            lateral = float.Min(Length(across), 7f);
        }

        var to = aimAt - muzzle;
        var dist = Length(to);
        if (dist < 0.3f)
            return;
        var dir = to / dist;

        // A cone of error: tighter for a good shot, wider with distance and when hurt (out to 25 m as it always was; beyond
        // that it widens more slowly for a gun made to reach, so a rifleman at long range is a threat, not just noise).
        // Later shots in a burst climb off the aim, as the player's do. The first shots at someone who's just shown are
        // wild, settling over a second or two of keeping them in sight (quicker for a good shot); someone running across
        // their line is harder to hit than someone standing.
        var settle = float.Clamp(_sightFor / (SettleTime - SettleSkill * Profile.Accuracy), 0f, 1f);
        var unsettled = _firingBlind ? 1f : 1f + SettleExtra * (1f - settle) * (1f - settle);
        var crossing = 1f + CrossingCone * lateral * (1f - 0.5f * Profile.Accuracy);
        var spread = 1f + float.Min(dist, 25f) / 25f + float.Max(0f, dist - 25f) / float.Max(Gun.Range, 25f);
        var cone = (0.03f + 0.08f * (1f - Profile.Accuracy)) * spread * (1f + _limp) * unsettled * crossing
                   * (1f + 0.25f * float.Max(0, Gun.Burst - 1 - _burstLeft))
                   * (_running ? 1.4f : Strategy == NpcStrategy.HoldBack || Aggression == Aggression.Defensive ? 0.85f : 1f)
                   * (_firingBlind ? FlashCone : 1f)
                   * (_targetIsPlayer && s.PlayerProne ? ProneTargetCone : 1f);   // (a low shape flat on the ground is hard to hold a gun on)

        // Not through a friend: hold fire, and (unless they're holding a post) step aside for a clear shot.
        if (FriendInLine(muzzle, dir, dist, float.Min(cone, FriendCone) + (Gun.Pellets > 1 ? Gun.PelletCone : 0f), s))
        {
            _burstLeft = 0;
            _cooldown = 0.2f;
            if (!HoldsPost && _sidestepLeft <= 0)
            {
                _sidestepLeft = SidestepTime;
                _sidestepSign = _random.NextDouble() < 0.5 ? 1f : -1f;
            }

            return;
        }

        if (!_firedOnTarget)
        {
            _firedOnTarget = true;
            Log.Write($"[NPC] {Name} opens fire on {(_targetIsPlayer ? "the player" : _targetNpc?.Name ?? "a flash")} at {dist:0.0} m (gun range {Gun.Range:0} m)");
        }

        dir = Scatter(dir, cone);
        // Hold over for the drop at that distance (they judge the range for themselves).
        var flight = dist / Gun.BulletSpeed;
        var drop = new float3(0, 0.5f * 9.81f * flight, 0);
        var velocities = new float3[Gun.Pellets];
        for (var i = 0; i < velocities.Length; i++)
            velocities[i] = (Gun.Pellets > 1 ? Scatter(dir, Gun.PelletCone) : dir) * Gun.BulletSpeed + drop;

        s.Fire(this, muzzle, velocities);
        Ammo--;
        _flash = 0.05f;
        // Automatic guns fire in bursts: the next round of the burst soon, then the usual pause.
        if (_burstLeft <= 0)
            _burstLeft = Gun.Burst;
        _burstLeft--;
        _cooldown = _burstLeft > 0
            ? Gun.Interval
            : 0.6f + 0.7f * (float)_random.NextDouble() + 0.4f * (1f - Profile.Accuracy);
        _animator.PlayOverlay(FireClip, _upperBody, 0.02f, 0f);   // no fade out: it holds its last (aim) frame
        _reAim = 0.28f;
        if (Ammo <= 0)
        {
            _burstLeft = 0;
            _reloadLeft = ReloadTime;
            _reAim = -1f;
            if (_clips.Get(ReloadClip) is not null)
                _animator.PlayOverlay(ReloadClip, _upperBody, 0.15f, 0.2f);   // gun in, magazine out and in, slide racked
            else
                _animator.Stop(FireClip, 0.3f);
            Log.Write($"[NPC] {Name} reloading");
            Say(Bark.Reloading, s);
            StepAsideToReload(s);   // (out of a doorway's way: Game/ArmedNpcDoorway.cs)
        }
    }

    // ------------------------------------------------------------------------------------------------ the gun

    private protected override float3? HeldTip()
    {
        if (_handR < 0 || _slung || Climbing || GunTaken)
            return null;
        return float3.Transform(Gun.MuzzleLocal, GunInHand(_world[_handR]));
    }

    /// <summary>
    /// In a fight, gun up: bend so the barrel (as the aim animation holds it, level) points at the target's height, up on
    /// a container stack or down off one. Not while reloading, climbing, slung, on the M249, dazzled or staggered.
    /// </summary>
    private protected override (float3 Axis, float Angle)? AimPitch()
    {
        if (State != NpcState.Fighting || _handR < 0 || _slung || Climbing || GunTaken || OnGun || Blinded
            || _reloadLeft > 0 || _staggerLeft > 0 || _drawingAk)
            return null;

        var hand = _world[_handR];
        var barrel = float3.Normalize(new float3(hand.M21, hand.M22, hand.M23));   // (GunInHand: the barrel runs along the knuckles)
        var level = float3.Normalize(barrel with { Y = 0 });
        if (!float.IsFinite(level.X))
            return null;
        float4x4.Invert(Transform, out var toModel);
        var muzzle = float3.Transform(Gun.MuzzleLocal, GunInHand(hand));
        var to = float3.Transform(_lastSeen, toModel) - muzzle;
        var flat = float.Sqrt(to.X * to.X + to.Z * to.Z);
        if (flat < 0.5f)
            return null;   // (right on top of them: no telling which way)
        var wanted = float.Atan2(to.Y, flat);
        var held = float.Asin(float.Clamp(barrel.Y, -1f, 1f));
        return (float3.Cross(level, float3.UnitY), wanted - held);
    }

    private protected override void AfterPose()
    {
        if (_handR < 0)
            return;
        if (Climbing && !_slung && _chest < 0)
            ScriptSlingReady();
        if (GunOnGround is { } lying)
        {
            GunToWorld = lying;   // (sat at the card table: Game/NpcSeat.cs)
            return;
        }
        GunToWorld = _slung || (Climbing && _chest >= 0) ? _slungOnChest * _world[_chest] * Transform : GunInHand(_world[_handR] * Transform);
    }

    /// <summary>The gun's model space, held in a hand with that transform.</summary>
    private float4x4 GunInHand(in float4x4 hand)
    {
        // The hand bone's frame in the world: its Y axis runs from the wrist to the knuckles, its X axis across the
        // back of the hand (pointing down the thumb side for the right hand held as in AimPistol). The pistol's barrel
        // (+X) goes along the knuckles and its top (+Y) opposite the hand's X axis.
        var along = float3.Normalize(new float3(hand.M21, hand.M22, hand.M23));
        var across = float3.Normalize(new float3(hand.M11, hand.M12, hand.M13));
        var up = float3.Normalize(-across - along * float3.Dot(-across, along));
        var side = float3.Cross(along, up);
        var basis = new float4x4(
            along.X, along.Y, along.Z, 0,
            up.X, up.Y, up.Z, 0,
            side.X, side.Y, side.Z, 0,
            0, 0, 0, 1);
        var grip = hand.Translation + along * 0.075f - up * 0.01f;   // the palm, a hand's width from the wrist
        return float4x4.CreateTranslation(-Gun.GripLocal) * basis * float4x4.CreateTranslation(grip);
    }

    private static float Length(float3 v) => float.Sqrt(float3.Dot(v, v));

    /// <summary><paramref name="dir"/> turned off by a random amount within a cone of <paramref name="cone"/> rad.</summary>
    private float3 Scatter(float3 dir, float cone)
    {
        var (u, v) = Basis(dir);
        var a = (float)_random.NextDouble() * float.Tau;
        var r = cone * float.Sqrt((float)_random.NextDouble());
        return float3.Normalize(dir + u * (r * float.Cos(a)) + v * (r * float.Sin(a)));
    }

    private static (float3 U, float3 V) Basis(float3 dir)
    {
        var helper = float.Abs(dir.Y) < 0.9f ? float3.UnitY : float3.UnitX;
        var u = float3.Normalize(float3.Cross(dir, helper));
        return (u, float3.Cross(dir, u));
    }
}
