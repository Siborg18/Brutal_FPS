using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The game's cinematic intro, in the prison (<see cref="Level.PrisonOrigin"/>): you arrive at the staff entrance at
/// the start of a shift, go through the gatehouse's screening, change at your locker, walk down the cell corridor and
/// out into the yard, where a helicopter lowers a shipping container in. Its doors swing open on the crimson portal,
/// which comes online; then you have control.
///
/// The script is a <see cref="Cinematic"/>; this class builds it and answers its cues (opening doors, bringing the
/// portal online). <see cref="Delivery"/> flies the helicopter: the script cues <c>"helicopter:approach"</c>, follows it
/// in, and waits for it to signal <c>"container:landed"</c>; the rest of the script (the doors, the portal) follows on
/// from there, while the helicopter leaves.
/// </summary>
public sealed class PrisonIntro
{
    private readonly Level _level;
    private readonly PortalSystem _portals;
    private readonly Npcs _npcs;

    public Cinematic Cinematic { get; }

    /// <summary>The helicopter and the container it brings (update and draw it with the world).</summary>
    public PrisonDelivery Delivery { get; }

    /// <summary>
    /// What the crimson portal connects to when it comes online: a portal's colour (BRUTALFPS_INTRO_LINK, default the cobalt portal
    /// in the undercroft of the block of flats, Rook's way out; it used to be the police HQ's navy portal).
    /// </summary>
    public string PortalLink { get; set; } = Environment.GetEnvironmentVariable("BRUTALFPS_INTRO_LINK") is { Length: > 0 } link ? link : "cobalt";

    /// <summary>Where the intro starts: on the perimeter road, facing the gatehouse up the staff path.</summary>
    public static float3 StartPosition => Level.PrisonAt(21f, 0, 47.2f);
    public const float StartYaw = 0f;

    /// <summary>
    /// Where you watch from, until you charge: on the court by the container's south side, behind where its south door ends up
    /// when it has swung right round (the leaf runs from its hinge at x = 6.1, z = 1.2 back to about x = 5, z = 1.6), and just
    /// clear of its swing (out to z = 2.4).
    /// </summary>
    public static float3 EndPosition => Level.PrisonAt(5.8f, 0, 3.6f);

    public bool Active => Cinematic.Active;

    /// <summary>The script has the player's finger on the trigger of the rifle they've taken.</summary>
    public bool PlayerFiring { get; private set; }

    /// <summary>The baton can still be swung: until the charge, when the script has the player's hands.</summary>
    public bool BatonFree { get; private set; } = true;

    /// <summary>How long the doors stay shut, in seconds, once you're looking at them.</summary>
    private const float DoorsDelay = 5f;

    /// <summary>The crowd in the yard, the man being rescued and the four raiders in the container (update them with the world).</summary>
    public PrisonSquad Squad { get; }

    public PrisonIntro(Level level, PortalSystem portals, Particles particles, Npcs npcs)
    {
        _level = level;
        _portals = portals;
        _npcs = npcs;
        Delivery = new PrisonDelivery(level, portals.Find("crimson"), particles);
        Squad = new PrisonSquad(npcs, () => Delivery.LookPoint, portals.Find("crimson"), level);
        Delivery.ContainerLanded += () => Cinematic?.Signal("container:landed");
        Delivery.ContainerLanded += Squad.Landed;
        Squad.RifleTaken += (_, _) => Cinematic?.Signal("rifle:taken");
        Squad.RescueThrough += () => Cinematic?.Signal("rescue:through");
        static float3 J(float x, float z) => Level.PrisonAt(x, 0, z);
        static float3 Eye(float x, float y, float z) => Level.PrisonAt(x, y, z);
        var doors = Level.PrisonContainerDoors;

        Cinematic = new Cinematic("Prison intro")
            // Black, the bars in, the time of day; fade up on the road outside the gatehouse.
            .Letterbox(true, 0).FadeOut(0)
            .Caption("11:12  -  Staff entrance", 5f)
            .Wait(1.6f).FadeIn(3.2f)
            .Walk(1.35f, J(21f, 43.5f), J(21f, 37.7f))
            .Cue("door:entrance").Wait(1.3f)
            // Into the gatehouse; a look at the control booth, then through the detector arch.
            .Walk(1.3f, J(21f, 35.2f), J(21f, 33.3f))
            .Look(Eye(17.4f, 1.35f, 31.9f), 1.2f).Wait(1.1f)
            .LookAhead()
            .Walk(1.2f, J(21f, 30.3f))
            .Look(Eye(21f, 1.45f, 27.9f), 0.8f)
            .Cue("door:sally").Wait(2.2f)
            .LookAhead()
            // The locker room: to your locker, open it, change (fade), shut it.
            .Walk(1.35f, J(21f, 26.6f), J(21.7f, 24.6f), J(24.55f, 22.2f))
            .Look(Eye(25.5f, 1.3f, Level.PlayerLockerZ), 0.9f)
            .Cue("locker:open").Wait(1.3f)
            .FadeOut(0.9f).Wait(1.4f).Cue("locker:close").FadeIn(1.3f)
            .LookAhead()
            // Through the grille and down the cell corridor.
            .Walk(1.35f, J(24.25f, 19.2f), J(23.95f, 17.5f))
            .Look(Eye(23.95f, 1.3f, 15.9f), 0.6f)
            .Cue("door:grille").Wait(2.0f)
            .LookAhead()
            .Walk(1.45f, J(23.95f, 15.0f), J(23.8f, 4.0f), J(23.8f, -10.9f))
            .Walk(1.2f, J(23.3f, -12.5f))
            .Look(Eye(22.2f, 1.5f, -12.5f), 0.7f)
            .Cue("door:yard").Wait(2.1f)
            .LookAhead()
            // Out into the yard, onto the ring path; the helicopter comes in over the north wing with the container.
            .Walk(1.3f, J(21.2f, -12.5f), J(18.6f, -10.4f), EndPosition)
            .Cue("helicopter:approach")
            .Track(() => Delivery.LookPoint, 2.4f)
            .WaitFor("container:landed", 40f)
            .Wait(2.4f)   // (it lets go of the load: the sling drops onto the roof)
            // Two officers go and stand in front of the doors, and prisoners crowd round. Two raiders push them open from the
            // inside and draw their rifles; two more stand behind them, rifles up. They open fire on the yard, and the man
            // they've come for walks straight through it into the container (see PrisonSquad).
            .Look(doors + new float3(0, 1.35f, 0), 1.8f)
            .Wait(DoorsDelay)   // (shut, for a moment, while the helicopter leaves)
            .Cue("container:doors")
            .Wait(8.0f)
            // You charge the nearer one and grab his barrel. The other fires at you and hits him; his rifle's yours; the other's
            // changing magazines, and you shoot him with it.
            .Cue("player:charge")
            .WalkLooking(PrisonSquad.GrabLook, 4.6f, PrisonSquad.GrabSpot)
            .Cue("player:grab")
            .Cue("portal:online")
            .WaitFor("rifle:taken", 5f)
            .Track(() => Squad.ShooterLookPoint, 0.3f)
            .Wait(0.55f)
            .Cue("player:fire")
            .Wait(0.35f)
            .Cue("player:cease")
            // Your eyes go to the two left inside. You have control once the man they came for has gone through the portal.
            .Look(doors + new float3(0, 1.35f, 0), 0.9f)
            .WaitFor("rescue:through", 8f)
            .Letterbox(false, 1.2f).Wait(1.0f);

        Cinematic.Cued += OnCue;
        Cinematic.Finished += Squad.Engage;
        portals.PlayerCrossed += OnPlayerCrossed;
    }

    /// <summary>How long the link to the prison lasts once you've come out at the flats, in seconds.</summary>
    private const float LinkDropsAfter = 3f;

    private bool _linkWillDrop;       // the link is the one the intro made to the flats, and hasn't dropped yet
    private float _linkDropsIn = -1;  // counting down since you crossed (negative: not counting)

    /// <summary>Through the crimson portal to the flats, the clock starts on the link. Step back through in time and it stops, so you're never cut off in the prison.</summary>
    private void OnPlayerCrossed(Portal entered)
    {
        // Anywhere but along the intro's own link to the flats, and from here on a death is an ordinary one.
        var link = entered.Linked;
        if (!(entered == _portals.Find("crimson") && link == _portals.Find("cobalt")) && !(entered == _portals.Find("cobalt") && link == _portals.Find("crimson")))
            RestartsAtDoors = false;
        if (!_linkWillDrop)
            return;
        if (entered == _portals.Find("crimson"))
            _linkDropsIn = LinkDropsAfter;
        else if (entered == _portals.Find("cobalt"))
            _linkDropsIn = -1;
    }

    /// <summary>Runs with the world, as this outlasts the script: the portal at the flats drops the prison behind you (the PC beside it dials on: see <see cref="Level.FlatsPcZone"/>).</summary>
    public void UpdateLink(float dt)
    {
        if (RestartsAtDoors && _npcs.FlatsDoorGunmenDown)
            RestartsAtDoors = false;   // (past the two on the stairwell's door: the intro's over)
        if (_linkDropsIn < 0)
            return;
        _linkDropsIn -= dt;
        if (_linkDropsIn > 0)
            return;
        _linkDropsIn = -1;
        _linkWillDrop = false;
        if (_portals.Find("crimson") is { } crimson && crimson.Linked == _portals.Find("cobalt"))
            _portals.Disconnect(crimson);
    }

    /// <summary>Shuts the prison up as it is before the shift, puts you on the road outside and starts the intro.</summary>
    public void Start(PlayerController player)
    {
        _level.SetAllPrisonDoors(false);
        _level.SetFlatsDoor(top: false, open: false);   // (Rook's way out, on a restart)
        _level.SetFlatsDoor(top: true, open: false);
        RestartsAtDoors = false;
        BatonFree = true;
        PlayerFiring = false;
        _linkWillDrop = false;
        _linkDropsIn = -1;
        if (_portals.Find("crimson") is { } crimson)
            _portals.Disconnect(crimson);
        Delivery.Prepare();
        Squad.Prepare();
        player.Position = StartPosition;
        player.Velocity = float3.Zero;
        player.Yaw = StartYaw;
        player.Pitch = -0.06f;
        player.LeftThroughPortal();
        Cinematic.Start(player);
    }

    /// <summary>
    /// The player's checkpoint: from the moment the container's doors open, through the portal to the flats, until both gunmen
    /// on the stairwell's door there are dead or the player has gone through any other portal, dying brings them back to that
    /// moment (<see cref="RestartAtDoors"/>) instead of to the spawn point.
    /// </summary>
    public bool RestartsAtDoors { get; private set; }

    private bool _restartingAtDoors;

    /// <summary>Back to the moment the doors open, with everything before it as it was and everyone in the yard alive again. The people must be back first (see <see cref="Npcs.ApplyRespawn"/>).</summary>
    public void RestartAtDoors(PlayerController player)
    {
        Log.Write("[INTRO] The player's back at the container's doors");
        _restartingAtDoors = true;
        Start(player);
        Cinematic.JumpTo(player, Cinematic.StepOfCue("container:doors"));
    }

    public void Update(float dt, PlayerController player, Input input) => Cinematic.Update(dt, player, input);

    public void Skip(PlayerController player)
    {
        Cinematic.Skip(player);
        Delivery.Finish();   // (it may be in the air, past its cue)
    }

    public void Draw(Renderer renderer, int2 framebufferSize) => Cinematic.Draw(renderer, framebufferSize);

    private void OnCue(string cue)
    {
        var instant = Cinematic.FastForwarding;   // (skipped or jumped past: no time to watch them move)
        switch (cue)
        {
            case "door:entrance": _level.SetPrisonDoor("entrance", true, instant); break;
            case "door:sally": _level.SetPrisonDoor("sally", true, instant); break;
            case "locker:open": _level.SetPrisonDoor("locker", true, instant); break;
            case "locker:close": _level.SetPrisonDoor("locker", false, instant); break;
            case "door:grille": _level.SetPrisonDoor("grille", true, instant); break;
            case "door:yard": _level.SetPrisonDoor("yard", true, instant); break;
            case "container:doors":
                RestartsAtDoors = true;
                if (_restartingAtDoors)
                {
                    _restartingAtDoors = false;
                    Squad.AtTheDoors();
                }
                _level.SetPrisonDoor("container", true, instant);
                if (instant)
                    Squad.Finish();
                else
                    Squad.Open();
                break;
            case "player:charge":
                BatonFree = false;
                if (!instant)
                    Squad.Charge();
                break;
            case "player:grab":
                if (instant)
                    Squad.FinishFight();
                else
                    Squad.Grab();
                break;
            case "player:fire":
                PlayerFiring = !instant;
                Squad.ShooterExposed();
                break;
            case "player:cease":
                PlayerFiring = false;
                Squad.ShooterDown();
                break;
            case "portal:online":
                if (_portals.Find("crimson") is { } crimson && _portals.Find(PortalLink) is { } target && target != crimson)
                {
                    _portals.Connect(crimson, target);
                    _linkWillDrop = target == _portals.Find("cobalt");   // (anywhere else has no PC to dial on with)
                }
                else
                    Log.Write($"[WARN] Prison intro: couldn't connect the crimson portal to \"{PortalLink}\".");
                break;
            case "helicopter:approach":
                if (instant)
                {
                    Delivery.Finish();
                }
                else
                {
                    Delivery.Fly();
                    Squad.HelicopterComing();
                }
                break;
        }
    }
}
