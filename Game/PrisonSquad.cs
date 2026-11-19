using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Who the intro directs in the yard (<see cref="PrisonIntro"/>): the crowd, the two officers at the doors, the prisoner being
/// rescued (<see cref="Npcs.RescueName"/>) and the four armoured raiders in the container. Two raiders open it from the
/// inside: each with an AK-47 slung on the back, palms on a door, pushing it open as they walk out behind it; then each
/// reaches over the shoulder, draws the rifle and shoulders it. Behind them, inside, two more stand with theirs already up.
/// With their rifles up they open fire from where they stand on everyone but the man they've come for, starting with the
/// officers and the prisoners crowded round the doors. He walks straight through it all, into the container and out through
/// the portal.
/// <para>
/// Then the player's part (the script walks them; the rest is here, on a clock from the grab). The player charges the nearer
/// raider outside and takes hold of his rifle's barrel in the free hand. The other one outside swings round and fires at the
/// player, and hits his friend, who's in the way and goes down; the player has the rifle now. He has emptied his magazine,
/// and while he changes it the player shoots him with it. The two inside are left for the player to fight.
/// </para>
/// The push is timed to the door (<see cref="Level.SetPrisonDoor"/>, 250 degrees over 3.6 s, eased), so the hands stay on
/// it for as long as it's slow enough to be pushed: 74 degrees, by when it's swinging on its own.
/// </summary>
public sealed class PrisonSquad(Npcs npcs, Func<float3> helicopter, Portal? portal, Level level)
{
    private const float DoorSideZ = 0.62f;       // each stands behind the middle of a door
    private const float StartX = 5.60f;          // palms on the shut door (its inside face at x = 6.03)
    private const float PushedX = 5.92f;         // where they've walked to with the door as far as they follow it
    private const float PushSeconds = 1.25f;
    private const float WalkOutSpeed = 0.75f;    // m/s, once they've let go
    private const float DrawAt = 1.35f;          // s: the draw starts, still walking
    private const float TurnAt = 1.7f;
    private const float DrawSeconds = 1.6f;      // as long as the clip
    private const float OpenFireAt = DrawAt + DrawSeconds;

    private static readonly float3 Player = PrisonIntro.EndPosition;

    // Where the script leaves them: the two who opened it spread out just outside the doorway, the two behind them inside,
    // wide of the middle, leaving room for the rescued man to pass and clear lines past the front pair to the crowd
    // (they'd hit each other otherwise: bullets hit anyone).
    private static readonly float3[] Posts =
    [
        Level.PrisonAt(7.0f, 0, -1.5f), Level.PrisonAt(7.0f, 0, 1.4f),
        Level.PrisonAt(4.4f, 0, -0.72f), Level.PrisonAt(4.4f, 0, 0.72f),
    ];

    private const int Shooter = 0, Grabbed = 1;   // the two outside: who fires at the player, and whose rifle the player takes

    /// <summary>They keep straight on through the doorway until the doors have swung past, then spread out.</summary>
    private const float ClearX = 6.3f, SpreadAt = 1.85f;

    /// <summary>Where the player's charge ends: at the raider's rifle, with him between the player and the other raider.</summary>
    public static float3 GrabSpot => Level.PrisonAt(7.2f, 0, 2.35f);

    /// <summary>What the player looks at while charging: the raider's chest.</summary>
    public static float3 GrabLook => Posts[Grabbed] + new float3(0, 1.3f, 0);

    // The player's hand forces the muzzle aside, past the player's left.
    private static readonly float3 PushedAside = Level.PrisonAt(6.2f, 0, 2.5f);

    // On a clock from the grab: the other raider turns; his burst (into his friend); the rifle's free; he changes magazines.
    private const float ShooterTurnsAt = 0.45f, BurstAt = 0.95f, BurstGap = 0.1f, ReloadAt = 1.85f;
    private const int BurstRounds = 7;
    private const int RoundsTaken = 22;           // left in the rifle when the player has it

    // The two officers: they walk over once it's down and stand a stride clear of the doors' swing, one in front of each.
    private const float GuardWalkSpeed = 1.25f;
    private const float GuardsSetOffAfter = 1.5f;     // s after it lands
    private static readonly float3[] GuardStart = [Level.PrisonAt(12.5f, 0, -6.5f), Level.PrisonAt(13.5f, 0, 2.5f)];
    private static readonly float3[] GuardPost = [Level.PrisonAt(8.4f, 0, -0.8f), Level.PrisonAt(8.4f, 0, 0.8f)];
    private static readonly float3[] GuardLooksAt = [Level.PrisonAt(6f, 0, -0.8f), Level.PrisonAt(6f, 0, 0.8f)];

    // The prisoners who come to see what's in it: both sides of the way the rescued man walks, leaving the player's charge clear.
    private const float CrowdSetsOffAfter = 0.8f;     // s after it lands
    private static readonly float3[] FrontPosts =
    [
        Level.PrisonAt(9.6f, 0, -1.9f), Level.PrisonAt(9.0f, 0, -2.7f), Level.PrisonAt(10.4f, 0, -2.6f), Level.PrisonAt(11.3f, 0, -2.2f),
        Level.PrisonAt(10.0f, 0, -3.6f), Level.PrisonAt(12.3f, 0, -3.0f), Level.PrisonAt(11.4f, 0, -3.9f), Level.PrisonAt(13.0f, 0, -3.7f),
        Level.PrisonAt(10.2f, 0, 1.6f), Level.PrisonAt(11.2f, 0, 0.9f), Level.PrisonAt(12.2f, 0, 1.5f),
        Level.PrisonAt(10.9f, 0, 2.7f), Level.PrisonAt(12.6f, 0, 2.9f), Level.PrisonAt(13.6f, 0, 1.2f),
    ];

    // The rescued man: he stands off by himself by the east wall, and once it's down he sets off straight for it, between the
    // two officers, in at the doors, between the two raiders inside, and through the portal as soon as it's online. Out of the
    // far end he runs on for the stairs (RookEscape); anywhere but the flats he walks on a little way, to leave the way clear.
    private const float RescueSpeed = 1.3f;
    private const float RescueRunSpeed = 3.4f;        // the last few metres, for the portal (and on, at the far end: see RookEscape)
    private const float RescueWalksOn = 2.5f;         // m beyond the far portal
    private const float RescueSetsOffAfter = 5f;      // s after it lands
    private const float DoorsOpenAfter = 9.2f;        // s after it lands, by the intro's script
    private static readonly float3 RescueStart = Level.PrisonAt(20f, 0, -4.5f);
    private static readonly float3 RescueGap = Level.PrisonAt(8.6f, 0, 0f);
    private static readonly float3 RescuePost = Level.PrisonAt(2.4f, 0, -0.2f);

    // Everyone in the yard stops to stare at the helicopter as it gets near, a few at a time.
    private const float CrowdLooksAfter = 7f;         // s after it sets off
    private const float WatchEvery = 0.5f;

    private ArmedNpc[]? _raiders;
    private RookEscape? _escape;
    private YardCast? _yard;
    private float _time;
    private bool _spreadDone;
    private bool _opening, _pushDone, _spread, _drawing, _gunTaken, _turned, _aiming, _engaged;
    private bool _firing, _guardsWalking, _guardsInPlace, _crowdWalking, _crowdReleased, _rescueWalking, _rescueInside, _landed, _crowdWatching;
    private bool _rescueAtPortal, _rescueThrough;
    private bool _gripping, _shooterTurned, _rifleFree, _shooterReloading, _killAfterPosing;
    private int _burstFired;
    private float _gripTime;
    private bool[] _crowdFaced = [];
    private float _sinceLanded, _sinceHelicopter, _watchLeft;

    /// <summary>The rifle's come away in the player's hands: the rounds in it, and whether to skip the swing into the hold.</summary>
    public event Action<int, bool>? RifleTaken;

    /// <summary>The rescued man has gone through the portal.</summary>
    public event Action? RescueThrough;

    /// <summary>Where the intro should look at the raider who's reloading: his chest.</summary>
    public float3 ShooterLookPoint => (_raiders?[Shooter].Position ?? Level.PrisonContainerDoors) + new float3(0, 1.3f, 0);

    /// <summary>The rifle the player has hold of, its model space to the world.</summary>
    public float4x4 GrabbedRifle => _raiders?[Grabbed].GunToWorld ?? float4x4.Identity;

    /// <summary>Puts the crowd in the yard, and them in the shut container, out of sight, guns as they should be.</summary>
    public void Prepare()
    {
        var facing = FacingFrom(RescueStart, RescueGap);
        _yard = npcs.AddYardCrowd(RescueStart, RescuePost, facing,
        [
            (GuardStart[0], GuardPost[0], -float.Pi / 2), (GuardStart[1], GuardPost[1], -float.Pi / 2),
        ]);
        _raiders = npcs.AddRaiders(
        [
            (Posts[0], FacingPlayer(Posts[0]), NpcStrategy.Push), (Posts[1], FacingPlayer(Posts[1]), NpcStrategy.Push),
            (Posts[2], FacingPlayer(Posts[2]), NpcStrategy.Cover), (Posts[3], FacingPlayer(Posts[3]), NpcStrategy.Cover),
        ]);
        _opening = _pushDone = _spread = _drawing = _gunTaken = _turned = _aiming = _engaged = false;
        _firing = _guardsWalking = _guardsInPlace = _crowdWalking = _crowdReleased = _rescueWalking = _rescueInside = _rescueAtPortal = _rescueThrough = _landed = _crowdWatching = false;
        _gripping = _shooterTurned = _rifleFree = _shooterReloading = _killAfterPosing = false;
        _escape = null;
        _burstFired = 0;
        _time = _gripTime = _sinceLanded = _sinceHelicopter = _watchLeft = 0;
        _spreadDone = false;
        _crowdFaced = new bool[_yard?.FrontCrowd.Length ?? 0];
        if (_raiders is not { } r)
            return;
        var east = float.Pi / 2;
        r[0].ScriptStart(Level.PrisonAt(StartX, 0, -DoorSideZ), east, slung: true);
        r[1].ScriptStart(Level.PrisonAt(StartX, 0, DoorSideZ), east, slung: true);
        r[2].ScriptStart(Posts[2], east, slung: false);
        r[3].ScriptStart(Posts[3], east, slung: false);
        foreach (var raider in r)
            raider.SparePlayerUntil(float.PositiveInfinity);
    }

    /// <summary>The helicopter has set off.</summary>
    public void HelicopterComing()
    {
        _crowdWatching = true;
        _sinceHelicopter = 0;
    }

    /// <summary>The container is down: the officers, the crowd and the rescued man get moving.</summary>
    public void Landed()
    {
        _landed = true;
        _sinceLanded = 0;
    }

    /// <summary>As it stands the moment before the doors start to move: the officers and the crowd in front of them, the rescued man on his way over.</summary>
    public void AtTheDoors()
    {
        _sinceLanded = DoorsOpenAfter;
        if (_yard is not { } yard)
            return;
        _guardsWalking = _guardsInPlace = _crowdWalking = _rescueWalking = true;
        for (var i = 0; i < yard.DoorGuards.Length; i++)
        {
            yard.DoorGuards[i].ScriptTake(GuardPost[i], FacingFrom(GuardPost[i], GuardLooksAt[i]));
            yard.DoorGuards[i].ScriptFace(GuardLooksAt[i]);
        }
        for (var i = 0; i < yard.FrontCrowd.Length; i++)
        {
            yard.FrontCrowd[i].ScriptTake(FrontPosts[i], FacingFrom(FrontPosts[i], Level.PrisonContainerDoors));
            yard.FrontCrowd[i].ScriptFace(Level.PrisonContainerDoors);
            _crowdFaced[i] = true;
        }
        var walked = RescueSpeed * (DoorsOpenAfter - RescueSetsOffAfter);
        yard.Rescue.ScriptTake(RescueStart + float3.Normalize(RescueGap - RescueStart) * walked, FacingFrom(RescueStart, RescueGap));
        yard.Rescue.ScriptWalk(RescueGap, RescueSpeed);
    }

    /// <summary>The doors start to move.</summary>
    public void Open()
    {
        _crowdWatching = false;
        ReleaseCrowd();
        if (_raiders is not { } r)
            return;
        _opening = true;
        _time = 0;
        foreach (var raider in r)
            raider.ScriptReveal();
        r[0].ScriptPlay("PushDoor", 0.08f, 0.3f);
        r[1].ScriptPlay("PushDoor", 0.08f, 0.3f);
        r[0].ScriptWalk(Level.PrisonAt(PushedX, 0, -DoorSideZ), (PushedX - StartX) / PushSeconds);
        r[1].ScriptWalk(Level.PrisonAt(PushedX, 0, DoorSideZ), (PushedX - StartX) / PushSeconds);
    }

    public void Update(float dt)
    {
        // Skipped: the two outside fall now they've been posed where they stand (a ragdoll starts from the last pose: they may never have had one).
        if (_killAfterPosing && _raiders is { } dead)
        {
            _killAfterPosing = false;
            dead[Grabbed].ScriptKill(float3.Normalize(dead[Grabbed].Position - dead[Shooter].Position));
            ShooterDown();
        }

        UpdateCrowd(dt);
        UpdateGuards();
        UpdateRescue();
        _escape?.Update(dt);
        UpdateRaiders(dt);
        UpdateGrip(dt);
    }

    private void UpdateCrowd(float dt)
    {
        if (_landed)
            _sinceLanded += dt;
        if (_yard is not { } yard)
            return;

        // Those who come to see: they go over to the doors, and stand looking at them.
        if (_landed && !_crowdWalking && !_crowdReleased && _sinceLanded >= CrowdSetsOffAfter)
        {
            _crowdWalking = true;
            for (var i = 0; i < yard.FrontCrowd.Length; i++)
            {
                var npc = yard.FrontCrowd[i];
                if (!npc.Alive)
                    continue;
                npc.ScriptTake(npc.Position, npc.Heading);
                npc.ScriptFace(FrontPosts[i]);
                npc.ScriptWalk(FrontPosts[i], 1.1f + 0.1f * (i % 3));
            }
        }

        if (_crowdWalking && !_crowdReleased)
        {
            for (var i = 0; i < yard.FrontCrowd.Length; i++)
            {
                if (!_crowdFaced[i] && yard.FrontCrowd[i].ScriptArrived)
                {
                    _crowdFaced[i] = true;
                    yard.FrontCrowd[i].ScriptFace(Level.PrisonContainerDoors);
                }
            }
        }

        if (!_crowdWatching)
            return;
        _sinceHelicopter += dt;
        _watchLeft -= dt;
        if (_watchLeft > 0)
            return;
        _watchLeft = WatchEvery;
        var at = helicopter();
        for (var i = 0; i < yard.Crowd.Length; i++)
        {
            if (_sinceHelicopter > CrowdLooksAfter + (i % 12) * 0.35f)
                yard.Crowd[i].Watch(at);
        }
    }

    /// <summary>The script lets go of those at the doors: they stand there until the shooting starts.</summary>
    private void ReleaseCrowd()
    {
        if (_crowdReleased || _yard is not { } yard)
            return;
        _crowdReleased = true;
        foreach (var npc in yard.FrontCrowd)
            npc.ScriptRelease();
    }

    private void UpdateGuards()
    {
        if (_yard is not { } yard || _firing)
            return;
        var guards = yard.DoorGuards;
        if (_landed && !_guardsWalking && _sinceLanded >= GuardsSetOffAfter)
        {
            _guardsWalking = true;
            for (var i = 0; i < guards.Length; i++)
            {
                guards[i].ScriptFace(GuardPost[i]);
                guards[i].ScriptWalk(GuardPost[i], GuardWalkSpeed);
            }
        }

        if (_guardsWalking && !_guardsInPlace && guards.All(g => g.ScriptArrived))
        {
            _guardsInPlace = true;
            for (var i = 0; i < guards.Length; i++)
                guards[i].ScriptFace(GuardLooksAt[i]);
        }
    }

    private void UpdateRescue()
    {
        if (_yard is not { Rescue: { } rescue })
            return;
        if (_landed && !_rescueWalking && _sinceLanded >= RescueSetsOffAfter)
        {
            _rescueWalking = true;
            rescue.ScriptWalk(RescueGap, RescueSpeed);
        }

        if (_rescueWalking && !_rescueInside && rescue.ScriptArrived)
        {
            _rescueInside = true;
            rescue.ScriptFace(RescuePost);
            rescue.ScriptWalk(RescuePost, RescueSpeed);
            Log.Write($"[SQUAD] {Npcs.RescueName} is at the doors");
        }
        else if (_rescueInside && !_rescueAtPortal && rescue.ScriptArrived && portal is not null)
        {
            _rescueAtPortal = true;
            rescue.ScriptFace(portal.Center);
            rescue.ScriptWalk(Npc.PortalApproach(portal, npcs.Senses), RescueRunSpeed);
            Log.Write($"[SQUAD] {Npcs.RescueName} is inside, heading for the portal ({rescue.Health:0} HP)");
        }
        else if (_rescueAtPortal && !_rescueThrough && rescue.ScriptArrived)
        {
            RescueGoesThrough(rescue);
        }
    }

    private void RescueGoesThrough(Npc rescue)
    {
        if (!rescue.Alive || portal is not { Online: true, Linked: { } exit })
            return;
        _rescueAtPortal = _rescueThrough = true;
        rescue.ScriptGoThrough(portal, npcs.Senses);
        // Out in the undercroft he has somewhere to run to (BRUTALFPS_INTRO_LINK can send him anywhere else: there he just walks clear).
        var on = rescue.Position + new float3(exit.Normal.X, 0, exit.Normal.Z) * RescueWalksOn;
        if (Level.ZoneAt(exit.Center) == Zones.Flats)
        {
            on = Level.RookGone;
            _escape = new RookEscape(level, npcs.Senses, rescue);
            _escape.Start();
        }
        else
        {
            rescue.ScriptWalk(on, RescueSpeed);
        }
        npcs.RescueNowAt(on, rescue.Heading);
        Log.Write($"[SQUAD] {Npcs.RescueName} has gone through the {portal.Name} to the {exit.Name}: on to {on} ({rescue.Zone}, {rescue.Health:0} HP)");
        RescueThrough?.Invoke();
    }

    private void UpdateRaiders(float dt)
    {
        if (!_opening || _raiders is not { } r)
            return;
        _time += dt;
        if (!_firing && _time >= OpenFireAt)
            OpenFire(0.25f);
        if (_spreadDone || _firing)
            return;
        if (!_pushDone && _time >= PushSeconds)
        {
            // Their hands come off the door as it swings away, and they walk on out.
            _pushDone = true;
            r[0].ScriptStop("PushDoor", 0.25f);
            r[1].ScriptStop("PushDoor", 0.25f);
            r[0].ScriptWalk(Level.PrisonAt(ClearX, 0, -DoorSideZ), WalkOutSpeed);
            r[1].ScriptWalk(Level.PrisonAt(ClearX, 0, DoorSideZ), WalkOutSpeed);
        }
        if (!_spread && _time >= SpreadAt)
        {
            _spread = true;
            r[0].ScriptWalk(Posts[0], WalkOutSpeed);
            r[1].ScriptWalk(Posts[1], WalkOutSpeed);
        }
        if (!_drawing && _time >= DrawAt)
        {
            _drawing = true;
            r[0].ScriptPlay("DrawRifleBack", 0.12f, 0f);
            r[1].ScriptPlay("DrawRifleBack", 0.12f, 0f);
        }
        if (!_gunTaken && _time >= DrawAt + ArmedNpc.DrawGrabTime)
        {
            _gunTaken = true;
            r[0].ScriptTakeGun();
            r[1].ScriptTakeGun();
        }
        if (!_turned && _time >= TurnAt)
        {
            _turned = true;
            r[0].ScriptFace(GuardPost[0]);
            r[1].ScriptFace(GuardPost[1]);
        }
        if (!_aiming && _time >= DrawAt + DrawSeconds)
        {
            _aiming = true;
            r[0].ScriptPlay("AimRifle", 0.1f, 0.35f);
            r[1].ScriptPlay("AimRifle", 0.1f, 0.35f);
        }
        _spreadDone = _aiming && _spread;
    }

    /// <summary>As if it had all played: everyone where the script leaves them, guns up.</summary>
    public void Finish()
    {
        _crowdWatching = false;
        if (_firing || _engaged)
            return;
        if (_yard is { } yard)
        {
            _landed = _guardsWalking = _guardsInPlace = _crowdWalking = _rescueWalking = _rescueInside = true;
            for (var i = 0; i < yard.DoorGuards.Length; i++)
                yard.DoorGuards[i].ScriptTake(GuardPost[i], -float.Pi / 2);
            for (var i = 0; i < yard.FrontCrowd.Length; i++)
            {
                yard.FrontCrowd[i].ScriptTake(FrontPosts[i], FacingFrom(FrontPosts[i], Level.PrisonContainerDoors));
                _crowdFaced[i] = true;
            }
            ReleaseCrowd();
            yard.Rescue.ScriptTake(RescuePost, -float.Pi / 2);
        }

        if (_raiders is not { } r)
            return;
        _opening = _aiming = _spread = _spreadDone = true;
        for (var i = 0; i < r.Length; i++)
            r[i].ScriptPlace(Posts[i], FacingFrom(Posts[i], GuardPost[i % GuardPost.Length]));
    }

    /// <summary>Rifles up: they open fire on the yard from where they stand, the nearest first: the officers at the doors, then the crowd. The player they leave for now.</summary>
    private void OpenFire(float after)
    {
        if (_firing || _raiders is not { } r)
            return;
        _firing = true;
        var guards = _yard?.DoorGuards ?? [];
        foreach (var guard in guards)
            guard.ScriptRelease();
        r[Shooter].KeepAlive = r[Grabbed].KeepAlive = true;   // (bullets hit anyone, and the script needs these two standing)
        for (var i = 0; i < r.Length; i++)
        {
            r[i].HoldsPost = true;
            r[i].ScriptEngage(guards.Length > 0 ? guards[i % guards.Length].Position : Player, after + 0.2f * i);
        }
        Log.Write("[SQUAD] The raiders open fire on the yard");
    }

    /// <summary>The player sets off for the nearer raider: he takes his eyes off the crowd and swings his rifle onto them.</summary>
    public void Charge()
    {
        if (_raiders is not { } r || !r[Grabbed].Alive)
            return;
        OpenFire(0.25f);
        r[Grabbed].ScriptSeize(1);
        r[Grabbed].ScriptFace(GrabSpot);
        Log.Write("[SQUAD] The player charges");
    }

    /// <summary>The player has hold of the barrel: it's forced aside, and the clock starts on the rest.</summary>
    public void Grab()
    {
        if (_raiders is not { } r)
            return;
        _gripping = true;
        _gripTime = 0f;
        r[Grabbed].ScriptFace(PushedAside);
        Log.Write("[SQUAD] The player has his barrel");
    }

    private void UpdateGrip(float dt)
    {
        if (!_gripping || _raiders is not { } r)
            return;
        _gripTime += dt;
        var shooter = r[Shooter];
        var friend = r[Grabbed];
        if (!_shooterTurned && _gripTime >= ShooterTurnsAt)
        {
            _shooterTurned = true;
            shooter.ScriptSeize(BurstRounds);
            shooter.ScriptFace(GrabSpot);
            Log.Write("[SQUAD] The other one turns on the player");
        }

        // He fires at the player, and his friend's in the way.
        while (_burstFired < BurstRounds && _gripTime >= BurstAt + _burstFired * BurstGap)
        {
            _burstFired++;
            shooter.ScriptShoot(npcs.Senses, friend.Position + new float3(0, 1.15f + 0.04f * _burstFired, 0));
        }

        if (!_rifleFree && _burstFired >= BurstRounds)
            TakeRifle(instant: false);

        if (!_shooterReloading && _gripTime >= ReloadAt)
        {
            _shooterReloading = true;
            shooter.ScriptReload();
            Log.Write("[SQUAD] He's out, and reloading");
        }
    }

    /// <summary>His friend's down, and the rifle's the player's.</summary>
    private void TakeRifle(bool instant)
    {
        if (_rifleFree || _raiders is not { } r)
            return;
        _rifleFree = true;
        var friend = r[Grabbed];
        friend.GunTaken = true;
        friend.KeepAlive = false;
        if (!instant)
            friend.ScriptKill(float3.Normalize(friend.Position - r[Shooter].Position));
        Log.Write("[SQUAD] His friend's down: the player has the rifle");
        RifleTaken?.Invoke(RoundsTaken, instant);
    }

    /// <summary>The player's opening fire on him: from here the bullets count.</summary>
    public void ShooterExposed()
    {
        if (_raiders is { } r)
            r[Shooter].KeepAlive = false;
    }

    /// <summary>The player's shots have gone into him (they hit where the crosshair is: this makes sure of it).</summary>
    public void ShooterDown()
    {
        if (_raiders is not { } r || _killAfterPosing)
            return;
        r[Shooter].ScriptKill(float3.Normalize(r[Shooter].Position - GrabSpot));
        Log.Write("[SQUAD] The player has shot the one reloading");
    }

    /// <summary>Skipped: the fight at the doors is over, as if it had played.</summary>
    public void FinishFight()
    {
        if (_raiders is not { } r)
            return;
        OpenFire(1.2f);
        _gripping = false;
        r[Grabbed].ScriptSeize(1);
        r[Shooter].ScriptSeize(1);
        r[Shooter].ScriptReveal();
        TakeRifle(instant: true);
        ShooterExposed();
        _killAfterPosing = true;
    }

    /// <summary>The intro's over: the two left inside turn on the player, one and then the other, giving them a moment.</summary>
    public void Engage()
    {
        if (_engaged)
            return;
        Finish();
        _engaged = true;
        if (_yard is { Rescue: { } rescue } && !_rescueThrough)
            RescueGoesThrough(rescue);   // (skipped: he's gone)
        if (_raiders is not { } r)
            return;
        OpenFire(1.2f);
        for (var i = 0; i < r.Length; i++)
        {
            r[i].HoldsPost = false;
            r[i].SparePlayerUntil(npcs.Senses.Time + 1.2f + 0.45f * i);
        }
        Log.Write("[SQUAD] The raiders turn on the player");
    }

    private static float FacingPlayer(float3 from) => FacingFrom(from, Player);

    private static float FacingFrom(float3 from, float3 to) => float.Atan2(to.X - from.X, to.Z - from.Z);
}
