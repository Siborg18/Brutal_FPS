using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;
using BrutalFps.Game;
using static Brutal.GlfwApi.Glfw.Constants;

// ---------------------------------------------------------------------------------------------
//  BRUTAL FPS - a minimal first-person shooter on the BRUTAL framework (GLFW + OpenGL 4.1).
//
//  An old warehouse; the blue portal on its loading dock leads through to the orange portal inside a shipping
//  container, which opens onto a small yard walled in by stacked containers.
//
//  Controls:  Mouse look  |  WASD move  |  Shift sprint  |  Space jump  |  Left mouse fire  |  Right mouse aim  |  R reload
//             Middle mouse, looking through a scope: night vision, then thermal, then off
//             1 pistol  |  2 AK-47 (fully automatic: hold to fire)
//             G (hold) plot a flashbang's throw, let go to throw it
//             Q (hold) a frag grenade, X (hold) a smoke grenade: likewise
//             I inventory (what you're carrying)
//             T (hold) the goggles wheel: point at none, night vision or thermal, let go to put them on
//             + respawn   |  E use (the factory's sliding door, the police station's PC)   |  Esc pause menu (resume / quit)
//             Enter skips the prison intro (BRUTALFPS_START=intro) or the way into a factory raid (=fence, =van) or an Antwerp raid (=boat, =abseil)   |  Alt+Enter (or Alt+Shift+Enter) fullscreen / windowed
// ---------------------------------------------------------------------------------------------

Precompile.Start();

if (!Glfw.Init())
    throw new InvalidOperationException("Failed to initialise GLFW.");

Glfw.WindowHint(GlfwWindowHint.ClientApi, GLFW_OPENGL_API);
Glfw.WindowHint(GlfwWindowHint.ContextVersionMajor, 4);
Glfw.WindowHint(GlfwWindowHint.ContextVersionMinor, 1);
Glfw.WindowHint(GlfwWindowHint.OpenGlProfile, GLFW_OPENGL_CORE_PROFILE);
Glfw.WindowHint(GlfwWindowHint.OpenGlForwardCompat, GLFW_TRUE);
Glfw.WindowHint(GlfwWindowHint.Samples, Profile.Has("nomsaa") ? 0 : 4);

// A 1600x900 window, unless BRUTALFPS_FULLSCREEN=1: then fullscreen on the primary monitor at its desktop resolution (but a
// test script or screenshot run keeps the window). Alt+Enter, with or without Shift, switches between the two.
var windowedSize = new int2(1600, 900);
var monitor = Glfw.PrimaryMonitor;
var hasMonitor = !monitor.Equals(GlfwMonitor.Null);
var startWindowed = Environment.GetEnvironmentVariable("BRUTALFPS_FULLSCREEN") != "1"
    || Environment.GetEnvironmentVariable("BRUTALFPS_SCRIPT") is { Length: > 0 }
    || Environment.GetEnvironmentVariable("BRUTALFPS_SHOTS") is { Length: > 0 }
    || !hasMonitor;
var desktopMode = startWindowed ? default : monitor.VideoMode;
if (!startWindowed)
    Glfw.WindowHint(GlfwWindowHint.RefreshRate, desktopMode.RefreshRate);
var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo
{
#if LEVEL_EDITOR
    Title = "BRUTAL FPS - Level Editor",
#else
    Title = "BRUTAL FPS",
#endif
    Size = startWindowed ? windowedSize : new int2(desktopMode.Width, desktopMode.Height),
    Monitor = startWindowed ? GlfwMonitor.Null : monitor,
});

window.MakeContextCurrent();
Glfw.SwapInterval(Environment.GetEnvironmentVariable("BRUTALFPS_NOVSYNC") == "1" ? 0 : 1);
GlLoader.Load();

var renderer = new Renderer();
Log.Write($"Log file: {Log.FilePath}");
Log.Write($"GPU: {Renderer.DriverDescription}");
var input = new Input(window);

// Day or night in each level (Game/TimeOfDay.cs), chosen before anything is loaded: the skies, shadow maps and lights
// below are set up once for those choices. BRUTALFPS_TIME, a test script or a screenshot run skip the menu, and so does
// starting on the firing range (BRUTALFPS_START=range): it's there to try the guns, in the daylight unless BRUTALFPS_TIME says not.
// The prison intro (BRUTALFPS_START=intro) skips it too: it's always day in the prison, so the levels beyond it just keep the
// choices last started with.
// The level editor's own build (Editor/BrutalFpsEditor.csproj) opens straight into the editor.
#if LEVEL_EDITOR
const bool EditorBuild = true;
#else
const bool EditorBuild = false;
#endif
var rangeStart = Environment.GetEnvironmentVariable("BRUTALFPS_START") == "range";
var introStart = Environment.GetEnvironmentVariable("BRUTALFPS_START") == "intro";
// A factory raid (Game/FactoryRaid.cs): BRUTALFPS_START=raid (the "Factory raid" launch profile) opens on its choice of how to get in,
// with no day and night menu; =fence or =van goes straight to one of them.
var raidStart = Environment.GetEnvironmentVariable("BRUTALFPS_START") == "raid";
var factoryEntry =Environment.GetEnvironmentVariable("BRUTALFPS_START") switch
{
    "fence" => FactoryEntry.Fence,
    "van" => FactoryEntry.Van,
    _ => FactoryEntry.None,
};
if (!TimeOfDay.ReadEnvironment()
    && !rangeStart
    && Environment.GetEnvironmentVariable("BRUTALFPS_SCRIPT") is not { Length: > 0 }
    && Environment.GetEnvironmentVariable("BRUTALFPS_SHOTS") is not { Length: > 0 })
{
    if (introStart || raidStart || factoryEntry != FactoryEntry.None
        || Environment.GetEnvironmentVariable("BRUTALFPS_START") is "antwerp" or "boat" or "abseil"
        || EditorBuild    // (the level editor opens straight in: at the time of day the game was last started with)
        || Npcs.NpcTest)  // (so does the NPC test build: straight to the test area, Game/NpcsTestArea.cs)
        TimeOfDay.LoadSaved();
    else
        new StartMenu().Run(window, renderer, input, FullscreenKey);
}
if (raidStart)
    factoryEntry = new StartMenu().ChooseFactoryEntry(window, renderer, input, FullscreenKey);
// An Antwerp raid (Game/AntwerpRaid.cs): BRUTALFPS_START=antwerp (the "Antwerp raid" launch profile) opens on its choice of how to
// get in and DAY or NIGHT in the yard (not saved; it starts as the yard was last played), with no other menu; =boat or =abseil goes
// straight to one, at the saved time (or BRUTALFPS_TIME). It loads the whole world.
var antwerpEntry = Environment.GetEnvironmentVariable("BRUTALFPS_START") switch
{
    "boat" => AntwerpEntry.Boat,
    "abseil" => AntwerpEntry.Abseil,
    _ => AntwerpEntry.None,
};
if (Environment.GetEnvironmentVariable("BRUTALFPS_START") == "antwerp" && !window.ShouldClose)
    antwerpEntry = new StartMenu().ChooseAntwerpEntry(window, renderer, input, FullscreenKey);
if (window.ShouldClose)
{
    renderer.Dispose();
    window.Dispose();
    Glfw.Terminate();
    return;
}
// A factory raid loads the factory and the police HQ, and nothing else: no other levels, no other portals, nobody from anywhere else. It's always at night.
var factoryRaid = factoryEntry != FactoryEntry.None;
var partialWorld = rangeStart || factoryRaid;
if (factoryRaid)
    TimeOfDay.Set(DayNightLevel.Factory, true);   // (not saved: it's the full game's menu that keeps its choices)
// The loadout before a raid (Game/RaidLoadout.cs; not the prison intro): up as soon as the way in is chosen, with the raid
// loading behind it (LoadingPump, between the steps of loading, gives it a frame). Test scripts and screenshot runs skip it
// (with the kit last chosen) unless BRUTALFPS_LOADOUT says otherwise: =open shows it, =go goes straight through,
// =shot:file.png saves a picture of it once loaded and quits, =skip leaves it out (and the raid as the game always was).
var loadoutDebug = Environment.GetEnvironmentVariable("BRUTALFPS_LOADOUT") ?? "";
var testRun = Environment.GetEnvironmentVariable("BRUTALFPS_SCRIPT") is { Length: > 0 } || Environment.GetEnvironmentVariable("BRUTALFPS_SHOTS") is { Length: > 0 };
var raidKit = (factoryEntry != FactoryEntry.None || antwerpEntry != AntwerpEntry.None) && loadoutDebug != "skip" ? RaidKit.Load() : null;
var loadout = raidKit is not null && (!testRun || loadoutDebug.Length > 0) ? new RaidLoadout(raidKit) : null;
if (loadoutDebug == "go")
    loadout?.Accept();
var loadoutEye = float3.Zero;
var lastLoadoutFrame = double.MinValue;
void LoadingPump()
{
    if (loadout is not { IsOpen: true } || window.ShouldClose || Glfw.Time - lastLoadoutFrame < 0.03)
        return;
    loadout.Frame(window, renderer, input, FullscreenKey, loadoutEye);
    lastLoadoutFrame = Glfw.Time;
}
if (loadout is null)
    StartMenu.DrawLoading(window, renderer);
else
    LoadingPump();
renderer.SetNightAreas(TimeOfDay.ShaderAreas);

// --- Textures and skies (baked and rendered in Blender: see Tools/blender) ---
var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
var textures = new TextureLibrary();
textures.Load(Path.Combine(assets, "Textures"), factoryRaid ? TextureLibrary.NotOnARaid : null);
LoadingPump();
// The container yard: by night its floodlights light it (they only come on under a night sky), by day an overcast. Either falls back on the other.
// (The firing range on its own is out on the outback's plain, under its sky: BRUTALFPS_START=range loads only that.)
var yardSky = partialWorld ? null : TimeOfDay.IsNight(DayNightLevel.Yard)
    ? SkyEnvironment.Load(Path.Combine(assets, "Sky"), "yard_night") ?? SkyEnvironment.Load(Path.Combine(assets, "Sky"), "yard")
    : SkyEnvironment.Load(Path.Combine(assets, "Sky"), "yard") ?? SkyEnvironment.Load(Path.Combine(assets, "Sky"), "yard_night");
var outbackSky = SkyEnvironment.Load(Path.Combine(assets, "Sky"), "outback");
renderer.SetEnvironment(textures, yardSky, outbackSky);
// The container ship is out at sea under a heavy grey overcast (or the yard's daytime overcast if that's missing).
var shipSky = partialWorld ? null : SkyEnvironment.Load(Path.Combine(assets, "Sky"), "sea") ?? SkyEnvironment.Load(Path.Combine(assets, "Sky"), "yard");
renderer.SetWhiteout(TimeOfDay.IsNight(DayNightLevel.Arctic) ? Level.WhiteoutNightColor : Level.WhiteoutColor);
LoadingPump();

// --- World ---
var levelStart = System.Diagnostics.Stopwatch.GetTimestamp();
// Texture sets the level editor gave every copy of a model (Game/LevelEditing.cs): before the level, so its merged copies have them too.
Level.LoadModelPaints();
var level = new Level(firingRangeOnly: rangeStart, factoryRaid: factoryRaid);
LoadingPump();
level.ArrivingVanHidden = factoryEntry == FactoryEntry.Van;   // (before the shadow maps: it isn't there yet)
FrameStats.LoadStage("level", levelStart);
renderer.SetLights(level.Lamps.ToArray());
renderer.SetShadeBox(Level.ContainerInterior);
var portals = new PortalSystem(level, none: rangeStart, factoryRaid: factoryRaid); // cuts the doorways into the walls, so create it before anything else is placed
// The layout saved from the level editor (Game/LevelEditing.cs): put back now, before the shadows, physics and people's map are made from it.
level.PrepareEditing();
LoadingPump();
renderer.SetLocalLights(Level.LocalLights(portals.BunkerPortals));
// (Each view is only given the volumes of the parts of the level it can see: see Renderer.SetShadeVolumes. BRUTALFPS_PROFILE=allshade gives every view them all, as it used to be.)
Func<float3, int>? shadeZones = Profile.Has("allshade") ? null : static p => (int)Level.ZonesSeenFrom(p);
renderer.SetShadeVolumes([.. Level.ShadeVolumes, .. Level.JungleShadeVolumes, .. Level.PrisonShadeVolumes, .. Level.FlatsShadeVolumes, Level.ArcticContainerShade, Level.ArcticStationShade, .. Level.RioShadeVolumes], shadeZones);
// The police station: its ceiling panels, and a depth map from straight above of its furniture for the soft shade
// under desks and chairs (rendered once, like the suns' shadow maps).
// The prison's rooms (two kilometres down) are lit by the same kind of ceiling fittings.
// (Rio's bulbs, at night, share their buffer: Rio lights them itself, in the open.)
renderer.SetOfficeLights([.. Level.PoliceLights, .. Level.PrisonLights, .. Level.FlatsLights, .. Level.RioLights], Level.PoliceAmbient);
// (The shadow maps of the areas that aren't there, on the firing range or a factory raid, are only as big as they have to be to exist.)
int ShadowSize(int size) => partialWorld ? 16 : size;
var policeOcclusion = new ShadowMap(rangeStart ? 16 : 2048, Level.PoliceOcclusionCenter, Level.PoliceOcclusionHalfExtent, Level.PoliceOcclusionDirection);
renderer.BeginShadowPass(policeOcclusion);
level.DrawPoliceOccluders(renderer);
renderer.EndShadowPass();
renderer.SetOfficeOcclusion(policeOcclusion, Level.PoliceOcclusionHalfExtent, 0.45f);
LoadingPump();
// The only shooting targets are on the firing range. (More: new Target("Near target", new float3(0, 0, 8), 1.5f, level).)
var targets = new List<Target>();
var firingRange = new FiringRange(level, targets);

// Sun shadows for the outdoor areas. The level doesn't move, so they're rendered once here.
var yardShadow = new ShadowMap(ShadowSize(2048), Level.YardShadowCenter, Level.YardShadowHalfExtent, Level.YardSunDirection);
var outbackShadow = new ShadowMap(factoryRaid ? 16 : 4096, Level.OutbackShadowCenter, Level.OutbackShadowHalfExtent, Level.OutbackSunDirection);
var shipShadow = new ShadowMap(ShadowSize(4096), Level.ShipShadowCenter, Level.ShipShadowHalfExtent, Level.ShipSunDirection);
// The jungle: the sun through the canopy (the tree crowns' shadows dapple the forest floor).
var jungleShadow = new ShadowMap(ShadowSize(4096), Level.JungleShadowCenter, Level.JungleShadowHalfExtent, Level.JungleSunDirection);
foreach (var (shadowMap, zones) in (ReadOnlySpan<(ShadowMap, Zones)>)[(yardShadow, Zones.Yard), (outbackShadow, Zones.Outback | Zones.Cellar), (shipShadow, Zones.Ship), (jungleShadow, Zones.Jungle)])
{
    renderer.BeginShadowPass(shadowMap);
    level.Draw(renderer, zones, shadowPass: true);
    renderer.EndShadowPass();
}
renderer.SetShadowMaps(yardShadow, outbackShadow);
renderer.SetShipEnvironment(shipSky, shipShadow);
renderer.SetJungleEnvironment(jungleShadow, Level.JungleClearingCenter, Level.JungleClearingRadii);
LoadingPump();
// The factory grounds round the warehouse: the outback's sun, with a shadow map of their own.
var factoryShadow = new ShadowMap(rangeStart ? 16 : 4096, Level.FactoryShadowCenter, Level.FactoryShadowHalfExtent, Level.FactorySunDirection);
renderer.BeginShadowPass(factoryShadow);
level.Draw(renderer, Zones.Warehouse, shadowPass: true);
renderer.EndShadowPass();
renderer.SetFactoryShadow(factoryShadow);
LoadingPump();
// The prison: a soft sun through thin overcast over its yard and grounds (its own shadow map), office-style light in
// the rooms you walk through.
var prisonShadow = new ShadowMap(ShadowSize(4096), Level.PrisonShadowCenter, Level.PrisonShadowHalfExtent, Level.PrisonSunDirection);
renderer.BeginShadowPass(prisonShadow);
level.Draw(renderer, Zones.Prison, shadowPass: true);
renderer.EndShadowPass();
renderer.SetPrisonEnvironment(Level.PrisonRooms, Level.PrisonSunDirection, Level.PrisonSunColor, prisonShadow);
LoadingPump();
// The block of flats: the prison's sun and sky (the same country, the same morning), a shadow map of its own.
var flatsShadow = new ShadowMap(ShadowSize(4096), Level.FlatsShadowCenter, Level.FlatsShadowHalfExtent, Level.PrisonSunDirection);
renderer.BeginShadowPass(flatsShadow);
level.Draw(renderer, Zones.Flats, shadowPass: true);
renderer.EndShadowPass();
renderer.SetFlatsEnvironment(Level.FlatsStairwell, flatsShadow);
LoadingPump();
// Rio: the outback's sky and sun turned to stand where Rio's sun does, a shadow map of its own.
var rioShadow = new ShadowMap(ShadowSize(4096), Level.RioShadowCenter, Level.RioShadowHalfExtent, Level.RioSunDirection);
renderer.BeginShadowPass(rioShadow);
level.Draw(renderer, Zones.Rio, shadowPass: true);
renderer.EndShadowPass();
renderer.SetRioEnvironment(Level.RioSunDirection, Level.OutbackSunDirection, rioShadow);
LoadingPump();
if (Environment.GetEnvironmentVariable("BRUTALFPS_DOOR") == "open")
    level.SetFactoryDoor(true);
// BRUTALFPS_DOOR=motel starts with the motel rooms' doors open (to look at them in a screenshot run).
if (Environment.GetEnvironmentVariable("BRUTALFPS_DOOR") == "motel")
    level.OpenMotelDoors();
// BRUTALFPS_AIRLOCK=outer (or inner) starts with that door of the police vault's airlock open.
if (Environment.GetEnvironmentVariable("BRUTALFPS_AIRLOCK") is "outer" or "inner")
    level.SetAirlockDoor(Environment.GetEnvironmentVariable("BRUTALFPS_AIRLOCK")!, true);
// How every door stands now is how a death puts them back (ResetLevelForRespawn).
level.RememberDoorsAsTheyStart();

// The yard's floodlights: a shadow map each (rendered once, like the sun's), and the lamps' glow in the night air.
var floods = Level.YardFloods;
var floodShadows = new FloodShadowAtlas(ShadowSize(4096));
for (var i = 0; i < int.Min(floods.Length, FloodShadowAtlas.MaxLights); i++)
{
    renderer.BeginShadowTile(floodShadows, i, floods[i].ViewProjection);
    level.Draw(renderer, Zones.Yard, shadowPass: true);
    renderer.EndShadowPass();
}
renderer.SetFloodLights(yardSky?.IsNight == true ? floods : [], floodShadows);
renderer.SetGlows(yardSky?.IsNight == true ? Level.YardGlows : []);
LoadingPump();

// Physics (Bullet): everything drawn becomes static collision for bullets and debris.
var physicsStart = System.Diagnostics.Stopwatch.GetTimestamp();
var physics = PhysicsWorld.TryCreate();
var surfaces = new SurfaceTable();
if (physics is not null)
{
    level.BuildPhysics(physics, surfaces);
    portals.AddPhysics(physics, surfaces);
    Log.Write($"Physics world: {physics.StaticObjects} static objects, {physics.StaticTriangles / 1000}k triangles, {surfaces.Count} surfaces " +
              $"({System.Diagnostics.Stopwatch.GetElapsedTime(physicsStart).TotalMilliseconds:0} ms)");
}
LoadingPump();

// BRUTALFPS_CLIMBSCAN=file: list every place a vault could climb up onto something (Game/ClimbScan.cs), then quit.
if (Environment.GetEnvironmentVariable("BRUTALFPS_CLIMBSCAN") is { Length: > 0 } climbScan)
{
    // (Walked from: the spawn, the start points, in front of every portal, both ends of every ladder.)
    var climbSeeds = new List<float3>
    {
        level.SpawnPoint, Level.RioStart, Level.FlatsStart, PrisonIntro.StartPosition, PrisonIntro.EndPosition, Level.RangeFiringPoint,
        Level.ArcticOrigin + new float3(24f, 0, -28f), Level.OutbackOrigin + new float3(1.5f, 0, -62), new float3(-4f, 0, -46f),
        Level.YardPortalFloorPoint + new float3(0, 0, -5), Level.ArcticContainerPortalFloorPoint + new float3(0, 0, 9f),
    };
    foreach (var p in portals.Portals)
        climbSeeds.Add(p.Center + p.Normal * 1.2f);
    climbSeeds.Add(Level.PoliceVaultPortalFloorPoint + new float3(1.5f, 0, 7f));
    // (Both sides of every door people open: a door that's shut now opens later.)
    foreach (var door in level.NpcDoors)
        foreach (var leaf in door.Shut)
            foreach (var side in (ReadOnlySpan<float3>)[new(1.1f, 0, 0), new(-1.1f, 0, 0), new(0, 0, 1.1f), new(0, 0, -1.1f)])
                climbSeeds.Add(leaf.Center with { Y = leaf.Min.Y + 0.25f } + side);
    foreach (var l in level.Ladders)
    {
        climbSeeds.Add(l.Volume.Center with { Y = l.Volume.Min.Y + 0.1f } - l.Facing * 0.4f);
        climbSeeds.Add(l.Volume.Center with { Y = l.Top + 0.1f } + l.Exit * 0.7f);
    }
    ClimbScan.Run(level, climbSeeds, climbScan);
    return;
}

// The NPC test build (Game/NpcsTestArea.cs): you start, and respawn, just outside the factory's outbuilding, where one of
// each kind of person waits inside; nobody (you included) can die.
if (Npcs.NpcTest && !rangeStart && !factoryRaid)
    level.SetSpawn(Npcs.TestPlayerSpawn, Npcs.TestPlayerYaw);
var player = new PlayerController(level) { Undying = Npcs.NpcTest, RespawnKeyRespawns = !Npcs.NpcTest };
var particles = new Particles(physics);
var holes = new BulletHoles(physics);
var debris = new DebrisSystem(physics, surfaces);
// The long guns' rounds (Assets/Models/round_*.glb: Tools/blender/build_rounds.py).
foreach (var round in Cartridge.All)
{
    try
    {
        debris.LoadRound(round);
    }
    catch (Exception e)
    {
        Log.Write($"[WARN] Couldn't load the {round.Name} round's model ({e.Message}).");
    }
}
var effects = new ImpactEffects(physics, holes, particles, debris);
particles.Landed += effects.BloodLanded;   // blood drops stain what they land on
// People walking about (Assets/Models/human.glb: skinned and animated, with levels of detail); bullets hit them.
// The British guns' specs (read from their models' points: Game/BritishGuns.cs), registered before the people are made so
// that NPCs can carry them too, firing as yours do.
var britishSpecs = RifleSpec.LoadBritish(assets);
foreach (var (_, spec) in britishSpecs)
{
    if (Enum.TryParse<Equipment>(spec.Name, ignoreCase: true, out var npcEquipment))
    {
        var (burst, range) = NpcGun.Handling(npcEquipment);
        NpcGun.Register(npcEquipment, NpcGun.FromSpec(spec, burst, range));
    }
}

var npcs = new Npcs(effects, physics, people: !rangeStart, factoryRaid: factoryRaid);
LoadingPump();
// What people can see past: the level (Bullet's static geometry, or the collision boxes without it) and portal water.
npcs.Senses.Clear = (from, to) =>
{
    var d = to - from;
    var length = float.Sqrt(float3.Dot(d, d));
    if (length < 1e-4f)
        return 1f;
    var dir = d / length;
    var clear = 1f;
    if (physics is not null)
    {
        if (physics.Raycast(from, to, out var hit, includeBodies: false))
            clear = hit.Fraction;
    }
    else if (level.Raycast(from, dir, length, 0f, out var distance, out _))
    {
        clear = distance / length;
    }

    if (portals.Raycast(from, dir, length * clear, out var portalDistance, out _))
        clear = float.Min(clear, portalDistance / length);
    return clear;
};
// ...and through that water, what's beyond it (NpcSenses.ThroughPortal).
npcs.Senses.Portals = portals.Portals;
// ...and what they can't see through but bullets go through: the jungle's leaves (BRUTALFPS_NOCONCEAL=1: see through them).
if (Environment.GetEnvironmentVariable("BRUTALFPS_NOCONCEAL") != "1")
    npcs.Senses.Foliage = level.Foliage.Depth;
// ...and how much light is on the player (BRUTALFPS_NODARK=1: they see as well at night as by day).
if (Environment.GetEnvironmentVariable("BRUTALFPS_NODARK") != "1")
    npcs.Senses.LightAt = p => level.LightAt(p, npcs.Senses.Clear);
npcs.Senses.IntoLight = Level.LooksIntoHall;
// ...and how they get about: a way round walls, through doorways, and their bodies kept off the walls.
npcs.Senses.Nav = new NpcNav(level);
// ...and where they can run in and hide (Rio's rooms behind open doors).
npcs.Senses.HidingPlaces = level.RioHidingPlaces;
npcs.CheckFactoryCrew(level);
npcs.CheckRioCrowd(level);
npcs.CheckNav();
LoadingPump();
var projectiles =new ProjectileSystem(level, targets, portals, physics, surfaces, effects, particles, npcs);
// The pistol view model, modelled in Blender (Assets/pistol.blend) and exported as Assets/Models/pistol.glb
// (the older Assets/pistol.obj still works as a fallback).
Model? pistol = null;
var pistolGlb = Path.Combine(assets, "Models", "pistol.glb");
var pistolObj = Path.Combine(assets, "pistol.obj");
try
{
    pistol = File.Exists(pistolGlb)
        ? Model.Load(pistolGlb)
        : Model.FromMeshes(ObjModel.Load(pistolObj).Select(kv => (kv.Key, kv.Value, Weapon.ObjPartMaterial(kv.Key))));
    Log.Write($"Loaded pistol model ({pistol.Parts.Count} parts)");
}
catch (Exception e)
{
    Log.Write($"[WARN] Couldn't load the pistol model ({e.Message}); using the box gun.");
}

// Sound (OpenAL Soft): the clips are in Assets/Sounds; without an audio device the game just runs silently.
var audio = AudioEngine.TryCreate(Path.Combine(assets, "Sounds"));
// Each part of the world has its own reverb (Game/LevelAcoustics.cs), blended across doorways, and sounds in the world are muffled behind
// walls (the same line-of-sight test people use).
audio?.SetRooms(Level.ReverbRooms(), Level.ReverbDoorways());
if (audio is not null)
    audio.Occlusion = npcs.Senses.Clear;
// The world's other sounds (Game/GameSounds.cs): footsteps by surface, bullets striking, ricochets, near misses, bodies
// falling; made in code unless there's a recording. People hear them too (Npcs.HearNoise), with or without a sound device.
var sounds = new GameSounds(audio, npcs, level, physics, surfaces);
LoadingPump();
projectiles.Sounds = sounds;
projectiles.Debris = debris;
// Flashbangs (Game/Flashbangs.cs): hold G for the throw's arc, let go to throw. They blind and deafen the player and people alike.
var flashbangs = new Flashbangs(level, portals, physics, surfaces, particles, npcs, sounds);
// BRUTALFPS_FLASHBANGS=infinite (or inf, 1, true): they never run out (for testing).
// (Always in the NPC test build.)
flashbangs.Infinite = Npcs.NpcTest || Environment.GetEnvironmentVariable("BRUTALFPS_FLASHBANGS")?.Trim().ToLowerInvariant() is "infinite" or "inf" or "1" or "true";
if (flashbangs.Infinite)
    Log.Write($"Flashbangs: infinite ({(Npcs.NpcTest ? "NPC test build" : "BRUTALFPS_FLASHBANGS")})");
// Frag and smoke grenades (Game/Throwables.cs): hold Q (frag) or X (smoke) for the arc, let go to throw. A frag's blast is
// worse shut in a room; smoke hides everyone from everyone, but a thermal scope sees through it.
var throwables = new Throwables(level, portals, physics, surfaces, particles, npcs, sounds, debris);
LoadingPump();
// BRUTALFPS_GRENADES=infinite (or inf, 1, true): they never run out (for testing).
throwables.Infinite = Npcs.NpcTest || Environment.GetEnvironmentVariable("BRUTALFPS_GRENADES")?.Trim().ToLowerInvariant() is "infinite" or "inf" or "1" or "true";
if (throwables.Infinite)
    Log.Write($"Grenades: infinite ({(Npcs.NpcTest ? "NPC test build" : "BRUTALFPS_GRENADES")})");
npcs.Senses.Smoke = throwables.SmokeBetween;
// Goggles (Game/Goggles.cs): hold T for the wheel, point at none, night vision or thermal, let go to put them on.
var goggles = new Goggles();
// People see grenades coming and get clear of them (Game/NpcDodge.cs).
npcs.Senses.FindGrenades = list =>
{
    throwables.ReportGrenades(list);
    flashbangs.ReportGrenades(list);
};
// Flares (Game/Flares.cs): some armed people light up the dark with them when they lose the player in it. Their light
// shows, with its own shadows, and adds to the light on the player that people's eyes go by.
var flares = new Flares(level, portals, physics, surfaces, particles, sounds);
var flareShadows = new FlareShadowAtlas(rangeStart ? 16 : 512);
renderer.SetFlareShadows(flareShadows);
npcs.Senses.Flares = flares;
if (npcs.Senses.LightAt is { } ambientLight)
    npcs.Senses.LightAt = p => float.Min(1f, ambientLight(p) + flares.LightOn(p, npcs.Senses.Clear));
// BRUTALFPS_FLARETEST="x,y,z": one dropped there at the start (to look at its light and shadows).
if (Environment.GetEnvironmentVariable("BRUTALFPS_FLARETEST")?.Split(',') is [var flareX, var flareY, var flareZ])
    flares.Launch(new float3(float.Parse(flareX, System.Globalization.CultureInfo.InvariantCulture), float.Parse(flareY, System.Globalization.CultureInfo.InvariantCulture), float.Parse(flareZ, System.Globalization.CultureInfo.InvariantCulture)), float3.Zero, fired: false);
// BRUTALFPS_SKYFLARETEST="x,y,z": an illumination flare lit and hanging there at the start (Flares.SkyHeight up over the ground, say).
if (Environment.GetEnvironmentVariable("BRUTALFPS_SKYFLARETEST")?.Split(',') is [var skyX, var skyY, var skyZ])
    flares.Hang(new float3(float.Parse(skyX, System.Globalization.CultureInfo.InvariantCulture), float.Parse(skyY, System.Globalization.CultureInfo.InvariantCulture), float.Parse(skyZ, System.Globalization.CultureInfo.InvariantCulture)));
// Armed people hunting the player follow them through the portals they take (Game/ArmedNpc.cs).
portals.PlayerCrossed += npcs.PlayerUsedPortal;

// Armed NPCs (Game/ArmedNpc.cs): their shots are real bullets in the same system as the player's, which can hit the
// player (Game/PlayerController.cs has the health), and they carry the pistol model in hand.
if (pistol is not null)
    npcs.GunModels[Equipment.Pistol] = pistol;
projectiles.Player = player;
// BRUTALFPS_GOD=1: bullets still hit (and are logged) but do no harm, for test scripts among armed NPCs.
var godMode = Environment.GetEnvironmentVariable("BRUTALFPS_GOD") == "1";
if (Npcs.StressTest)
{
    godMode = true;   // (the stress test: a hundred guns fire at you in Rio, and you stay up to watch)
    Log.Write("[STRESS] Stress test: 100 gunmen appear when you step into Rio and all fire 1 s later; you take no harm.");
}
var armourRandom = new Random(4417);
projectiles.PlayerHit += (damage, from, at) =>
{
    damage = player.ArmouredDamage(damage, at, armourRandom);   // (a raid's armour and helmet take their share)
    if (player.RailWish is not null)
        return;   // (on rails in the intro, with the raiders firing round you: a stray round mustn't end it)
    if (godMode)
        Log.Write($"[PLAYER] hit for {damage:0} (BRUTALFPS_GOD: no harm done)");
    else
        player.TakeDamage(damage, from);
    sounds.PlayerHurt(damage);
};
var npcShotSound = audio?.Get("Weapons/pistol_shot");
var npcShotRandom = new Random(77);
// The motel's gunmen throw their doors open as the player comes near (Game/NpcsMotel.cs); a respawn shuts them all again.
npcs.MotelDoorSprung += level.SpringMotelDoor;
npcs.MotelReset += level.ShutMotelDoors;
// The Arctic station's crew throw its door open on their way out to the white portal (Game/NpcsStation.cs).
npcs.StationCrewOut += level.OpenStationDoor;
var npcShotSounds = new FrameBudget(12);
npcs.Shot += (npc, muzzle, velocities) =>
{
    var npcGun = npc is ArmedNpc armed ? armed.GunInUse : NpcGun.For(npc.Equipment);
    // (The gun on the nest's tripod throws its cases and smoke, and every fourth round on its belt is a tracer.)
    var tracer = npc is ArmedNpc { OnGun: true, Mount: { } mount } && mount.Fired(muzzle, velocities[0]);
    foreach (var velocity in velocities)
        projectiles.Spawn(muzzle, velocity, fromPlayer: false, round: npcGun.Round, firedBy: npc, tracer: tracer);
    // The pistol's shot, pitched for the gun (as the player's guns do). No more than a dozen start in one frame (each takes one
    // of the 64 voices and an occlusion ray: a hundred at once only stole voices from each other).
    if (!npcShotSounds.Take())
        return;
    var pitch = npcGun.ShotPitch;
    audio?.PlayAt(npcShotSound, muzzle, npcGun.Volume, pitch.X + (pitch.Y - pitch.X) * (float)npcShotRandom.NextDouble(), referenceDistance: 6f);
};
var pistolGun = new Weapon(level, portals, projectiles, effects, debris, physics, pistol) { Audio = audio };
// All you carry in the prison intro (it's in the pistol's hand and sleeve).
var baton = new Baton(level, portals, projectiles, effects, debris, physics, pistol) { Audio = audio };
// A gun and its model (Assets/Models/<file>.glb, from Tools/blender/build_<file>.py); nothing if it isn't there or won't load.
(Model? Model, T? Gun) LoadGun<T>(string file, string name, Func<Model, T> make) where T : class
{
    try
    {
        var path = Path.Combine(assets, "Models", file + ".glb");
        if (!File.Exists(path))
            return (null, null);
        var model = Model.Load(path);
        var gun = make(model);
        Log.Write($"Loaded {name} model ({model.Parts.Count} parts)");
        return (model, gun);
    }
    catch (Exception e)
    {
        Log.Write($"[WARN] Couldn't load the {name} model ({e.Message}).");
        return (null, null);
    }
}

// The AK-47 (also build_ak47_hands.py).
var (akModel, rifle) = LoadGun("ak47", "AK-47", m => new Rifle(level, portals, projectiles, effects, debris, physics, m, hands: null, RifleSpec.Ak47) { Audio = audio });
LoadingPump();
// The pump shotgun, held in the AK's hands. 3 selects it.
var (shotgunModel, shotgun) = LoadGun("shotgun", "shotgun", m => new Shotgun(level, portals, projectiles, effects, debris, physics, m, akModel) { Audio = audio });
// The UMP40 submachine gun, held in the AK's hands. 4 selects it.
var (umpModel, smg) = LoadGun("ump", "UMP40", m => new Rifle(level, portals, projectiles, effects, debris, physics, m, akModel, RifleSpec.Ump40) { Audio = audio });
LoadingPump();
// The scoped AR-15 carbine, held in the AK's hands. 5 selects it.
var (arModel, carbine) = LoadGun("ar15", "AR-15", m => new Rifle(level, portals, projectiles, effects, debris, physics, m, akModel, RifleSpec.Ar15) { Audio = audio });
// The G3 battle rifle, held in the AK's hands. 6 selects it.
var (g3Model, battleRifle) = LoadGun("g3", "G3", m => new Rifle(level, portals, projectiles, effects, debris, physics, m, akModel, RifleSpec.G3) { Audio = audio });
LoadingPump();
// The M249, held in the AK's hands: it sits on the tripod of the jungle's gun nest (Game/GunNest.cs) until you pick it up
// with E. Then 7 selects it.
var (m249Model, lmg) = akModel is null ? (null, null) : LoadGun("m249", "M249", m => new Lmg(level, portals, projectiles, effects, debris, physics, m, akModel) { Audio = audio });
LoadingPump();
var gunNest = lmg is null ? null : GunNest.Jungle(lmg);
if (gunNest is not null)
    npcs.Senses.Nests.Add(gunNest);   // (the guards can use it too: Game/NpcGunner.cs)
// The armoured car's turret in the outback (Game/LevelArmouredCar.cs): its own M249 and belt, for the gunner in its hatch,
// turned as they aim. Nobody else can use it.
GunNest? carTurret = null;
if (level.HasArmouredCar && m249Model is not null && akModel is not null)
{
    try
    {
        carTurret = GunNest.ArmouredCar(new Lmg(level, portals, projectiles, effects, debris, physics, m249Model, akModel) { Audio = audio },
            Model.Get("armoured_car_turret"));
    }
    catch (Exception e)
    {
        Log.Write($"[WARN] Couldn't load the armoured car's turret ({e.Message}): no gun on it.");
        carTurret = GunNest.ArmouredCar(new Lmg(level, portals, projectiles, effects, debris, physics, m249Model, akModel) { Audio = audio }, null);
    }

    carTurret.Reset();
    if (physics is not null)
        carTurret.AddPhysics(physics, surfaces);   // its shield stops bullets, and hides the gunner behind it
    npcs.Senses.Nests.Add(carTurret);
}
// Debug: BRUTALFPS_CHINOOK="x,y,z,yawDeg" hovers a Chinook there, in the prison's coordinates, rotors turning, for looking at
// it. (The one in the intro is Game/PrisonDelivery.cs.)
var chinook = Environment.GetEnvironmentVariable("BRUTALFPS_CHINOOK") is { Length: > 0 } chinookAt
    ? chinookAt.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray() is var at
        ? new Chinook { Position = Level.PrisonAt(at[0], at[1], at[2]), Yaw = float.DegreesToRadians(at[3]) }
        : null
    : null;
// The far bank of the dock across from the Antwerp yard: its cranes at work, a tug (Game/Harbour.cs).
var harbour = new Harbour();
// The same models in armed NPCs' hands (BRUTALFPS_NPCGUNS hands these out: see Game/Npcs.cs).
foreach (var (equipment, model) in (ReadOnlySpan<(Equipment, Model?)>)[(Equipment.Rifle, akModel), (Equipment.Shotgun, shotgunModel),
             (Equipment.Smg, umpModel), (Equipment.Carbine, arModel), (Equipment.BattleRifle, g3Model), (Equipment.Lmg, m249Model)])
    if (model is not null)
        npcs.GunModels[equipment] = model;

// The gun in hand, and the one coming up once it's put away. BRUTALFPS_WEAPON=ak starts with the AK.
IGun weapon = Environment.GetEnvironmentVariable("BRUTALFPS_WEAPON") switch
{
    "ak" or "ak47" or "2" when rifle is not null => rifle,
    "shotgun" or "3" when shotgun is not null => shotgun,
    "ump" or "smg" or "4" when smg is not null => smg,
    "ar" or "ar15" or "5" when carbine is not null => carbine,
    "g3" or "6" when battleRifle is not null => battleRifle,
    "m249" or "lmg" when lmg is not null => lmg,
    "baton" => baton,
    _ => pistolGun,
};
// (Starting on the firing range, the M249 is on 7 from the first, with no trip to the jungle for it.)
if (weapon == lmg || rangeStart)
    gunNest?.StartCarried();
IGun? nextGun = null;
// A gun picked during a raid's cutscene stays down (the script raises it at the end): its name shows for a moment instead.
string? cutsceneGunName = null;
var cutsceneGunShown = 0f;
IGun gunBeforeLmg = pistolGun;    // the gun you put away to bring up the M249 (it comes back when you put the M249 on its tripod)
float? raidSpeed = null;          // a raid's kit: how fast its weight lets you move (Game/RaidLoadout.cs)
var raidStanceLoad = 0f;          // and how much it slows getting down and up (PlayerController.KitLoad)
var lmgFromLoadout = false;     // the M249 is a raid loadout's primary: a respawn gives it back rather than putting it on its tripod
var nestRespawns = player.Respawns;
// The British guns (Game/BritishGuns.cs), in the AK's hands: you pick them up from a gun locker.
// Each with its model (the locker draws its picture) and its numbers.
var catalog = new List<LockerEntry>();
void Catalog(IGun? gun, Model? model, Func<GunStats> stats)
{
    if (gun is not null)
        catalog.Add(new LockerEntry(gun, model, stats()));
}
// (The pistol's and the shotgun's shot is the plain 9 mm ball: Game/Cartridge.cs. Their aimed cones are in Weapon.cs and Shotgun.cs.)
Catalog(pistolGun, pistol, () => new GunStats
{
    Kind = "9 mm service pistol", Calibre = Cartridge.Para9.Name, Feed = "17 + 1", Action = "Semi-auto",
    Energy = Cartridge.Para9.Energy(pistolGun.ProjectileSpeed), RoundsPerMinute = 60f / pistolGun.FireInterval,
    Velocity = pistolGun.ProjectileSpeed, AimedSpread = 0.0095f,
});
Catalog(rifle, akModel, () => GunStats.Of(((Rifle)rifle!).Spec, "Kalashnikov assault rifle"));
Catalog(shotgun, shotgunModel, () => new GunStats
{
    Kind = "Pump-action shotgun", Calibre = "12 gauge buckshot", Feed = "7 + 1 tube", Action = "Pump",
    Energy = Cartridge.Para9.Energy(((Shotgun)shotgun!).ProjectileSpeed), Pellets = ((Shotgun)shotgun!).Pellets,
    RoundsPerMinute = 60f / 0.53f,   // shot, then the pump back and forward
    Velocity = ((Shotgun)shotgun!).ProjectileSpeed, AimedSpread = 0.002f + ((Shotgun)shotgun!).PelletConeAiming,
});
Catalog(smg, umpModel, () => GunStats.Of(((Rifle)smg!).Spec, "UMP submachine gun"));
Catalog(carbine, arModel, () => GunStats.Of(((Rifle)carbine!).Spec, "AR-15 carbine", "2-16x scope"));
Catalog(battleRifle, g3Model, () => GunStats.Of(((Rifle)battleRifle!).Spec, "G3 battle rifle", "4x ACOG"));
Catalog(lmg, m249Model, () => new GunStats
{
    Kind = "M249 light machine gun", Calibre = Cartridge.Nato556.Name, Feed = "200-round belt", Action = "Belt-fed",
    Energy = Cartridge.Nato556.Energy(Lmg.ProjectileSpeed), RoundsPerMinute = 60f / Lmg.AutoFireInterval,
    Velocity = Lmg.ProjectileSpeed, AimedSpread = 0.002f, Mobility = lmg!.MoveScale,
});
if (akModel is not null)
{
    foreach (var (file, spec) in britishSpecs)
    {
        var (britishModel, britishGun) = LoadGun(file, spec.Name, m => new Rifle(level, portals, projectiles, effects, debris, physics, m, akModel, spec) { Audio = audio });
        LoadingPump();
        Catalog(britishGun, britishModel, () => GunStats.Of(spec));
        // (In NPCs' hands too; without the model, they're drawn with the nearest of the other guns: NpcGun.Fallback.)
        if (britishModel is not null && Enum.TryParse<Equipment>(spec.Name, ignoreCase: true, out var npcEquipment))
            npcs.GunModels[npcEquipment] = britishModel;
    }
}
// The optics a gun locker can fit (Game/Optics.cs, Tools/blender/build_optics.py), and what was fitted to which gun last time.
{
    var (opticsModel, _) = LoadGun("optics", "optics", m => m);
    Optics.Load(opticsModel, Path.Combine(assets, "Models", "optics_points.json"));
    Optics.Load(catalog.Select(e => e.Gun));
}
// Debug: BRUTALFPS_WEAPON can also name any gun in the locker (l85a2, ...).
if (Environment.GetEnvironmentVariable("BRUTALFPS_WEAPON") is { Length: > 0 } startGun && catalog.FirstOrDefault(e => e.Gun.Name.Equals(startGun, StringComparison.OrdinalIgnoreCase)) is { Gun: { } namedGun })
    weapon = namedGun;
// What's on the number keys (index = the key): chosen at a gun locker; to start with, the guns you've always had.
var slots = new IGun?[10];
(slots[1], slots[2], slots[3], slots[4], slots[5], slots[6]) = (pistolGun, rifle, shotgun, smg, carbine, battleRifle);
var locker = new GunLocker(catalog, slots);
locker.Load();
if (Environment.GetEnvironmentVariable("BRUTALFPS_LOCKER") is { } lockerDebug && lockerDebug.StartsWith("open", StringComparison.Ordinal))
    locker.ForceOpen(lockerDebug.Split(':', 2) is [_, var lockerPick] ? lockerPick : null);
// The inventory (Game/Inventory.cs): I brings up what you're carrying. BRUTALFPS_INVENTORY=open starts with it up (for screenshots).
var inventory = new Inventory(locker, catalog, slots, flashbangs);
loadout?.Bind(locker, catalog, flashbangs, throwables);   // (the guns are loaded: their pictures and numbers on its cards)
if (Environment.GetEnvironmentVariable("BRUTALFPS_INVENTORY") == "open")
    inventory.ForceOpen();
// The M249 off the tripod goes on 7, or the first free number, unless you've already given it a number at a locker.
void PutLmgOnANumber()
{
    if (lmg is null || Array.IndexOf(slots, lmg) >= 0)
        return;
    var free = slots[7] is null ? 7 : Array.FindIndex(slots, 1, s => s is null);
    if (free >= 0)
        slots[free] = lmg;
}
void TakeLmgOffItsNumber()
{
    if (lmg is not null && !locker.Chosen(lmg) && Array.IndexOf(slots, lmg) is >= 0 and var number)
        slots[number] = null;
}
if (gunNest is { Carried: true })
    PutLmgOnANumber();
var hud = new Hud();
// NPCs' frags (Game/ArmedNpcTactics.cs): planned and thrown through the same grenades as the player's.
npcs.Senses.PlanFrag = throwables.PlanNpcThrow;
npcs.Senses.ThrowFrag = throwables.ThrowFromNpc;
// People's callouts (Game/NpcBarks.cs): a subtitle when the player is near enough to hear it, and the recording if there is one.
const float BarkHeard = 32f;   // m (half that through a wall)
npcs.Senses.Spoke = (npc, bark, line, index) =>
{
    var ear = npcs.Senses.PlayerEye;
    var mouth = npc.Position + new float3(0, Npc.EyeHeight, 0);
    if ((Level.ZonesSeenFrom(ear) & npc.Zone) == 0)
        return;
    var dist = float3.Distance(ear, mouth);
    if (dist > BarkHeard || (dist > BarkHeard * 0.5f && npcs.Senses.Clear(ear, mouth) < 0.99f))
        return;
    sounds.Voice(NpcBarks.Clip(bark, index), mouth);
    var colour = npc.Faction == Faction.Police ? new float3(0.55f, 0.75f, 1f) : new float3(1f, 0.62f, 0.45f);
    hud.Subtitles.Add(line, colour);
};
// The pause screen (Game/PauseMenu.cs): Esc opens it. BRUTALFPS_PAUSE=open starts with it open (for screenshots).
var pause = new PauseMenu(Npcs.NpcTest ? new NpcTestMenu(npcs) : null);
if (Npcs.NpcTest)
{
    Factions.Truce = true;   // (the test area's police and hostiles leave each other alone until NPC CONFIG says otherwise)
    player.SavedFromDeath += () => hud.Subtitles.Add("NPC TEST: THAT WOULD HAVE KILLED YOU - BACK TO FULL HEALTH", new float3(1f, 0.4f, 0.35f));
    Log.Write("[NPCTEST] NPC test build: one of each NPC in the factory's outbuilding, nobody dies; Esc > NPC CONFIG sets them up.");
}
if (Environment.GetEnvironmentVariable("BRUTALFPS_PAUSE") == "open")
    pause.Open();
// BRUTALFPS_PAUSE=npc starts in the NPC test build's NPC CONFIG.
if (Environment.GetEnvironmentVariable("BRUTALFPS_PAUSE") == "npc")
    pause.OpenNpcConfig();
#if LEVEL_EDITOR
// The level editor (Game/LevelEditor.cs) is its own build (Editor/BrutalFpsEditor.csproj, LEVEL_EDITOR): it opens
// straight into the editor and stays there. The game's build has no editor; it loads what the editor saves.
// BRUTALFPS_EDITOR=shot:file.png saves a screenshot of it (next to the exe) a second in and quits; =test runs through its
// moves (logged as [EDITOR]) first.
var editor = new LevelEditor(level, LevelEditor.JumpPoints(rangeStart, factoryRaid));
var editorDebug = Environment.GetEnvironmentVariable("BRUTALFPS_EDITOR") is { Length: > 0 } editorVariable ? editorVariable : "open";
var editorFrames = 0;
var editorQuitWarned = false;
#endif
// The cinematic intro in the prison (Game/PrisonIntro.cs): BRUTALFPS_START=intro plays it. While it runs the player is
// walked through it (no HUD, nothing in hand until the raiders open the container: then a baton, which left click swings); Enter skips it.
var intro = new PrisonIntro(level, portals, particles, npcs);
intro.Cinematic.Finished += () => weapon.Raise();
// A run that starts with the intro has no guns: only the baton, drawn as the raiders open the container, until you first go through a portal.
var gunsLocked = introStart;
intro.Cinematic.Cued += cue =>
{
    if (cue == "container:doors")
        weapon.Raise();
};
portals.PlayerCrossed += _ => gunsLocked = false;
// (The inside of the container is dim. While it isn't there, that would be a dark patch on the yard's court.)
intro.Delivery.ContainerInPlace += inPlace => renderer.SetShadeVolumes(inPlace
    ? [.. Level.ShadeVolumes, .. Level.JungleShadeVolumes, .. Level.PrisonShadeVolumes, .. Level.FlatsShadeVolumes, Level.ArcticContainerShade, Level.ArcticStationShade, .. Level.RioShadeVolumes]
    : [.. Level.ShadeVolumes, .. Level.JungleShadeVolumes, .. Level.FlatsShadeVolumes, Level.ArcticContainerShade, Level.ArcticStationShade, .. Level.RioShadeVolumes], shadeZones);
// A raid on the factory (Game/FactoryRaid.cs): over the fence or in the back of a van, then you have control at the edge of the grounds.
var raid = factoryRaid && !rangeStart ? new FactoryRaid(level, audio, factoryEntry, npcs) : null;
if (raid is not null)
    raid.Cinematic.Finished += () => weapon.Raise();
// A raid on the container yard at Antwerp (Game/AntwerpRaid.cs): by boat with three of your own, or alone down a rope by night.
var antwerp = antwerpEntry != AntwerpEntry.None && !partialWorld ? new AntwerpRaid(level, audio, antwerpEntry, npcs) : null;
if (antwerp is not null)
    antwerp.Cinematic.Finished += () => weapon.Raise();
// ...where the crew get the women in the office out through the portal once they know you're there (Game/FactoryEvacuation.cs).
var evacuation = raid is null ? null : new FactoryEvacuation(npcs, level, factoryEntry);
// ...and four minutes after it goes loud, the army comes in through the north gate (Game/FactoryArmy.cs).
// Each armoured car's turret is a working one, as the outback car's: its own M249 and belt, its armour stopping bullets.
Func<float3, float, GunNest?>? armyTurret = m249Model is null || akModel is null ? null : (ring, facing) =>
{
    Model? turretModel = null;
    try { turretModel = Model.Get("armoured_car_turret"); }
    catch (Exception e) { Log.Write($"[WARN] Couldn't load the army car's turret ({e.Message}): no armour on it."); }
    var nest = GunNest.Vehicle(new Lmg(level, portals, projectiles, effects, debris, physics, m249Model, akModel) { Audio = audio }, turretModel, ring, facing);
    nest.Reset();
    if (physics is not null)
        nest.AddPhysics(physics, surfaces);
    return nest;
};
var army = raid is null ? null : new FactoryArmy(npcs, level, raid, physics, surfaces, armyTurret);
// The tech-room PC by the navy portal: stand at its desk and press E for its terminal.
var terminal = new PcTerminal(portals);
// The vault's PC (behind the airlock off the bullpen), wired into the gold portal: the same terminal, connecting that one.
var vaultTerminal = new PcTerminal(portals, portals.Vault, Level.PoliceVaultPcZone, "VAULT");
// The PC in the jungle shack, behind the olive portal and wired into it.
var jungleTerminal = new PcTerminal(portals, portals.Jungle, Level.JunglePcZone, "CAMP");
// The PC in the grey container next to the orange portal in the Antwerp yard, wired into it.
var yardTerminal = new PcTerminal(portals, portals.Yard, Level.YardPcZone, "YARD");
// The PC against the loading dock beside the factory's blue portal, wired into it. (On a raid the portal is dark: no PC can dial it, so this one is out of reach.)
var factoryTerminal = new PcTerminal(portals, portals.Factory, factoryRaid ? new Aabb(new float3(float.MaxValue), new float3(float.MaxValue)) : Level.FactoryPcZone, "FACTORY");
// The PC by the cobalt portal in the undercroft: no choosing, the player just dials 112, the police HQ's vault portal.
var flatsTerminal = new PcTerminal(portals, portals.Flats, Level.FlatsPcZone, "FLATS", autoConnect: portals.Vault);
// The vault airlock's keypads, close up: E at one whose door is shut brings it up for the code.
var keypad = new AirlockKeypad();
// The dome cameras (Game/SecurityCamera.cs): the bunker's and the police vault's, each following the player in its room;
// their feeds are on the two CCTV monitors over the tech room's PC desk.
SecurityCamera[] securityCameras = rangeStart ? [] : factoryRaid ? [SecurityCamera.Vault()] : [SecurityCamera.Bunker(), SecurityCamera.Vault()];
var mirrors = new OneWayMirrors(rangeStart ? [] : Level.VaultMirrors);   // the vault's observation rooms' one-way glass

var playerDouble = npcs.CreatePlayerDouble();   // (the camera sees a body where the player is)
// Debug: BRUTALFPS_HQLINK=orange starts with the HQ portal connected to that portal (as if chosen at the terminal).
if (Environment.GetEnvironmentVariable("BRUTALFPS_HQLINK") is { Length: > 0 } hqLink
    && portals.Portals.FirstOrDefault(p => PortalIndex.Short(p.Name).Equals(hqLink, StringComparison.OrdinalIgnoreCase)) is { } hqTarget)
    portals.Connect(portals.Hq, hqTarget);
// A portal coming on (a PC connecting it) puts the people near it on guard (Npcs.PortalOpened). Not the prison intro's
// (its yard is scripted), nor the debug link above (hooked up after it).
portals.PortalOpened += p =>
{
    if (!intro.Cinematic.Active)
        npcs.PortalOpened(p);
};
var linkAt = Environment.GetEnvironmentVariable("BRUTALFPS_LINKAT")?.Split(':') is [var linkSeconds, var linkFrom, var linkTo]
    && double.TryParse(linkSeconds, System.Globalization.CultureInfo.InvariantCulture, out var linkTime) ? (At: linkTime, A: linkFrom, B: linkTo) : ((double At, string A, string B)?)null;
var runStart = Glfw.Time;
projectiles.TargetHit += hud.OnTargetHit;
projectiles.TargetHit += firingRange.OnTargetHit;

var camera = Camera.Default;

// The intro's fight at the doors: the free hand on the raider's barrel, then his rifle in your hands instead of the baton.
baton.SetGrabHands(akModel);
intro.Cinematic.Cued += cue =>
{
    if (cue == "player:grab" && !intro.Cinematic.FastForwarding)
        baton.Grab(() => intro.Squad.GrabbedRifle);
};
intro.Squad.RifleTaken += (rounds, instant) =>
{
    baton.Grab(null);
    if (rifle is null)
        return;
    rifle.TakeFrom(swing: !instant, rounds, player);
    weapon = rifle;
    nextGun = null;
    Log.Write("[GUN] The AK-47 is in your hands");
};

// Killed (or respawned): the level goes back to how it began. Everyone's brought back alive at their posts (npcs.ApplyRespawn),
// every door stands as it did at the start, and the smoke, bullet holes, blood, casings and debris of the last life are gone.
// (Grenades, flares and flashbangs in flight clear themselves on the same respawn count.) The intro's and the raid's restarts
// come after this and set their own doors.
void ResetLevelForRespawn()
{
    level.ResetDoors();
    holes.Clear();
    debris.Clear();
    particles.Clear();
    npcs.ApplyRespawn(player);
}

// Killed once the container's doors have opened, in the prison or by the two gunmen at the flats (see PrisonIntro.RestartsAtDoors),
// you come back at that moment instead of at the spawn point: baton in hand, no guns, the yard as it was.
void RestartIntroAtTheDoors()
{
    if (!intro.RestartsAtDoors)
        return;
    if (weapon != baton)
        weapon.Holster();   // (a reload in progress is over: the rifle's taken again later)
    weapon = baton;
    nextGun = null;
    gunsLocked = true;
    baton.Grab(null);
    holes.Clear();
    debris.Clear();
    npcs.ApplyRespawn(player);
    intro.RestartAtDoors(player);
}

// Killed (or respawned) on a factory raid, you come back just before the way in ends: in the parked van with its doors about
// to open (the officers with you), or on top of the wall about to drop down. Nothing in hand until it's over, as the first time.
void RestartRaidNearEnd()
{
    if (antwerp is not null)
    {
        weapon.Holster();
        npcs.ApplyRespawn(player);
        antwerp.RestartNearEnd(player);
    }
    if (raid is null)
        return;
    weapon.Holster();
    npcs.ApplyRespawn(player);
    raid.RestartNearEnd(player);
}

// Test scripts: BRUTALFPS_SCRIPT="seconds:keys:pitchDeg[:yawDeg];..." holds those keys (W, A, S, D, J = jump) with the
// view at that pitch (and yaw) for that long, step by step, logging where the player is, then quits. M holds aim. At the
// start of a step, F fires, N presses the middle mouse button, T holds G (a flashbang: let go in the next step to throw it), O holds T (the goggles wheel) and [ or ] move the mouse down-left or down-right, R reloads, E uses (the PC), I presses I (the inventory), 0-9 press those number keys, < and > press up and down, # presses Enter, + respawns and P saves a screenshot (script_<step>.png next to the exe).
// BRUTALFPS_START can then also be "x,y,z,yawDeg".
var script = (Environment.GetEnvironmentVariable("BRUTALFPS_SCRIPT") ?? "")
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(step =>
    {
        var parts = step.Split(':');
        return (Seconds: double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), Keys: parts[1],
                Pitch: float.DegreesToRadians(float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)),
                Yaw: parts.Length > 3 ? float.DegreesToRadians(float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)) : (float?)null);
    })
    .ToList();
var scriptScreenshot = false;
var scriptStep = -1;
var scriptStepStart = 0.0;
var scriptLog = 0.0;
var shotsHeard = 0;
var playerWasReloading = false;   // (for the start of a reload: npcs.PlayerReloading)

// Debug: BRUTALFPS_START=yard or =outback starts you at the back of the container yard / in front of the yellow portal; =roadhouse on the
// road just south of the outback roadhouse.
switch (Environment.GetEnvironmentVariable("BRUTALFPS_START"))
{
    case "yard":
        player.Position = Level.YardPortalFloorPoint + new float3(0, 0, -5);
        player.Yaw = float.Pi; // facing where the purple portal goes (+Z; it's out of the game for now)
        break;
    case "roadhouse":
        player.Position = Level.OutbackOrigin + new float3(1.5f, 0, -62);
        player.Yaw = 0.25f; // walking north up the road, the roadhouse ahead on the right
        break;
    case "range":
        player.Position = Level.RangeFiringPoint;
        player.Yaw = 0f; // looking down the firing range
        break;
    case "outback":
        player.Position = Level.OutbackPortalFloorPoint + new float3(5, 0, 0);
        player.Yaw = -float.Pi / 2; // facing the yellow portal (-X), inside the fenced yard
        break;
    case "shed":
        player.Position = Level.OutbackOrigin + new float3(32.4f, 0, -136f);
        player.Yaw = float.Pi; // walking south up to the shed door
        break;
    case "cellar":
        player.Position = Level.CellarPortalFloorPoint + new float3(0, 0, -3.5f);
        player.Yaw = float.Pi; // facing the green portal (+Z)
        break;
    case "bunker":
        player.Position = Level.BunkerOrigin + new float3(-4f, 0, 0);
        player.Yaw = float.Pi / 2; // facing east, the red portal ahead, the ice portal on the left and the lime one on the right
        break;
    case "arctic":
        player.Position = Level.ArcticPortalFloorPoint + new float3(0, 0, -3f);
        player.Yaw = 0; // walking out north into the whiteout
        break;
    case "ship":
        player.Position = Level.ShipPortalFloorPoint + new float3(1.2f, 0, 0);
        player.Yaw = float.Pi / 2; // on the bridge front, looking forward over the deck
        break;
    case "jungle":
        player.Position = Level.JunglePortalFloorPoint + new float3(0, 0, -3f);
        player.Yaw = 0; // stepping out of the shack into the clearing, the lab shelter ahead
        break;
    case "factory":
        player.Position = new float3(-4f, 0, -46f);
        player.Yaw = float.Pi * 0.95f; // outside, looking back at the hall's north end and its sliding door
        break;
    case "police":
        player.Position = Level.PolicePortalFloorPoint + new float3(2.5f, 0, 0);
        player.Yaw = float.Pi / 2; // in the tech room by the portal, looking east through the doorway down the bullpen
        break;
    case "vault":
        player.Position = Level.PoliceVaultPortalFloorPoint + new float3(1.5f, 0, 7f);
        player.Yaw = 0; // in the vault, looking north at the gold portal
        break;
    case "intro":
        intro.Start(player);   // the prison intro: walked through it from the staff entrance to the yard
        weapon = baton;
        weapon.Holster();
        // BRUTALFPS_INTRO_STEP=n starts it at its n-th step (as if the steps before had played), for working on it.
        if (int.TryParse(Environment.GetEnvironmentVariable("BRUTALFPS_INTRO_STEP"), out var introStep))
            intro.Cinematic.JumpTo(player, introStep);
        break;
    case "fence" or "van" or "raid":
        break;   // (a factory raid: set off below, once everything's ready)
    case "antwerp" or "boat" or "abseil":
        break;   // (an Antwerp raid: likewise)
    case "rio":
        player.Position = Level.RioStart;
        player.Yaw = Level.RioStartYaw; // out of the magenta portal at the foot of the favela, looking along the bottom alley
        break;
    case "station":
        player.Position = Level.ArcticOrigin + new float3(24f, 0, -28f);
        player.Yaw = 0.7f; // on the snow south-west of the Arctic research station, looking at its door and window
        break;
    case "arcticcontainer":
        player.Position = Level.ArcticContainerPortalFloorPoint + new float3(0, 0, 9f);
        player.Yaw = 0; // inside the second container by its doors, looking back at the slate portal
        break;
    case "flats":
        player.Position = Level.FlatsStart;
        player.Yaw = float.Pi / 2; // in the undercroft by the cobalt portal, looking across at the stairwell's door
        break;
    case "prison":
        player.Position = PrisonIntro.StartPosition;
        player.Yaw = PrisonIntro.StartYaw; // on the road outside the prison's staff entrance (every door open)
        break;
    case "prisonyard":
        player.Position = PrisonIntro.EndPosition;
        player.Yaw = 0; // in the yard, behind the container's open south door, looking north past it
        break;
    case { } custom when custom.Contains(','):
        var v = custom.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        player.Position = new float3(v[0], v[1], v[2]);
        if (v.Length > 3)
            player.Yaw = float.DegreesToRadians(v[3]);
        break;
}

if (raid is not null)
{
    raid.Start(player);
    weapon.Holster();
    // BRUTALFPS_INTRO_STEP=n starts it at its n-th step, as it does the prison intro.
    if (int.TryParse(Environment.GetEnvironmentVariable("BRUTALFPS_INTRO_STEP"), out var raidStep))
        raid.Cinematic.JumpTo(player, raidStep);
}
if (antwerp is not null)
{
    antwerp.Start(player);
    weapon.Holster();
    if (int.TryParse(Environment.GetEnvironmentVariable("BRUTALFPS_INTRO_STEP"), out var antwerpStep))
        antwerp.Cinematic.JumpTo(player, antwerpStep);
}

// Screenshot mode (for checking the graphics without playing): BRUTALFPS_SHOTS="file.png@x,y,z,yawDeg,pitchDeg;..."
// renders each view, saves it next to the exe and quits.
var shots = (Environment.GetEnvironmentVariable("BRUTALFPS_SHOTS") ?? "")
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(spec =>
    {
        var at = spec.Split('@');
        var v = at[1].Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return (File: Path.Combine(AppContext.BaseDirectory, at[0]), Eye: new float3(v[0], v[1], v[2]), Yaw: float.DegreesToRadians(v[3]), Pitch: float.DegreesToRadians(v[4]));
    })
    .ToList();
if (shots.Count > 0)
    weapon.SkipDraw();
var shotIndex = 0;
var shotFrames = 0;
var shotTime = 0.0;
var shotCpu = 0.0;

// Loaded: the raid's loadout screen stays up until the player's done with it (or shuts now, if they already are), then
// they're given what they chose.
if (loadout is not null)
{
    loadoutEye = player.EyePosition;
    loadout.FinishLoading();
    while (loadout.IsOpen && !window.ShouldClose)
        loadout.Frame(window, renderer, input, FullscreenKey, loadoutEye);
    TakeRaidKit(loadout.Kit);
}

// A raid's loadout: on 1 the primary, on 2 the secondary (and nothing else on the numbers), the armour and helmet worn, the
// goggles if they were taken, and the grenades (as many again after each respawn).
void TakeRaidKit(RaidKit kit)
{
    IGun? Find(string name) => catalog.FirstOrDefault(e => e.Gun.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Gun;
    var primary = Find(kit.Primary);
    var secondary = Find(kit.Secondary);
    Array.Clear(slots);
    slots[1] = primary;
    slots[2] = secondary;
    weapon.HolsterNow();
    weapon = primary ?? secondary ?? pistolGun;
    nextGun = null;
    if (InCutscene())
        weapon.HolsterNow();   // (the way in's script raises it when it's over)
    else
        weapon.Raise();
    gunBeforeLmg = secondary ?? pistolGun;
    lmgFromLoadout = primary is not null && primary == lmg;
    if (lmgFromLoadout)
        gunNest?.StartCarried();
    player.Armour = RaidArmour.Of(kit.Armour, kit.Helmet);
    raidSpeed = kit.SpeedScale;
    raidStanceLoad = kit.StanceLoad;
    player.CanSprint = !kit.Overloaded;
    player.FootstepNoise = kit.NoiseScale;
    throwables.PlayerBlastScale = player.Armour.Blast;
    goggles.Available = kit.Goggles;
    flashbangs.Stock = kit.Flashbangs;
    throwables.SetStock(kit.Frags, kit.Smokes);
}

Log.Write("BRUTAL FPS - click in the window to capture the mouse.");
input.SetMouseCaptured(shots.Count == 0 && script.Count == 0);

// The diagnostics (Engine/FrameStats.cs): what a report on a bad frame says about the world, and the recording starting.
FrameStats.World = () =>
{
    var (fighting, ragdolls, alive) = (0, 0, 0);
    foreach (var person in npcs.People)
    {
        if (person.Alive)
            alive++;
        if (person is ArmedNpc { InContact: true })
            fighting++;
        if (person.Ragdolled)
            ragdolls++;
    }

    var mode = pause.IsOpen ? "paused" : InCutscene() ? "cutscene" : !player.Alive ? "dead" : "playing";
    return new WorldCounts(Level.ZoneAt(player.Position).ToString(), mode, player.Position.X, player.Position.Y, player.Position.Z, alive, fighting, ragdolls,
        physics?.Bodies ?? 0, debris.Count, particles.Count, projectiles.Count, holes.Count, flares.Burning, audio?.VoicesPlaying ?? 0);
};
window.OnFocus += (_, focus) => Log.Write($"[WINDOW] {(focus == GlfwFocusEvent.LostFocus ? "Lost focus" : "Got focus")}");
// The first frame's one-off costs, paid here while it's still loading: the NPCs' map (box index and links) and Bullet's first step.
npcs.Senses.Nav?.BeginFrame(0f, 0f);
physics?.WarmUp();
FrameStats.BeginPlay($"vsync {(Environment.GetEnvironmentVariable("BRUTALFPS_NOVSYNC") == "1" ? "off" : "on")}, {Renderer.DriverDescription}");
var lastZone = Level.ZoneAt(player.Position);

var debugPosition = Environment.GetEnvironmentVariable("BRUTALFPS_DEBUG") == "1";
var lastTime = Glfw.Time;
var worldStarted = false;
while (!window.ShouldClose)
{
    var now = Glfw.Time;
    var realDt = (float)(now - lastTime);
    var dt = float.Min(realDt, 0.05f); // clamp long frames (window drag, breakpoints)
    lastTime = now;

    using (Perf.Poll.Time()) Glfw.PollEvents();
    FrameStats.Mark("poll events");
    input.Update();

    FullscreenKey();

#if LEVEL_EDITOR
    // --- The level editor: the world holds still while you fly round it and rearrange it ---
    // Closing the window with changes not saved: the first time, it stays open and says so.
    if (window.ShouldClose && editor.UnsavedChanges > 0 && !editorQuitWarned && editorDebug == "open")
    {
        window.ShouldClose = false;
        editorQuitWarned = true;
        editor.Warn($"{editor.UnsavedChanges} changes not saved: SAVE CHANGES keeps them. Close again to quit without them.");
    }
    if (window.ShouldClose)
        break;
    if (editorDebug.Length > 0 && !editor.IsOpen && editorFrames == 0)
    {
        editor.Open(player.ApplyTo(camera));
        input.SetMouseCaptured(false);
        // =test:pitchDeg looks that far up or down first; =pick[:pitchDeg] picks what's straight ahead, as a click would;
        // =audit logs the items whose boxes are big ([EDITOR] AUDIT). Any of them +shot:file.png saves a picture after.
        var debugCommand = editorDebug.Split('+')[0];
        var debugPitch = !debugCommand.StartsWith("shot:", StringComparison.Ordinal) && !debugCommand.StartsWith("paintmodel:", StringComparison.Ordinal) && debugCommand.Split(':') is [_, var deg, ..] ?float.DegreesToRadians(float.Parse(deg, System.Globalization.CultureInfo.InvariantCulture)) : 0f;
        if (debugCommand.Split(':')[0] is "test" or "testkeep")
        {
            editor.Look(debugPitch);
            editor.SelfTest(keep: debugCommand.StartsWith("testkeep", StringComparison.Ordinal));
        }
        if (debugCommand == "audit")
            level.AuditBounds();
        if (debugCommand.StartsWith("pick", StringComparison.Ordinal))
            editor.PickAhead(debugPitch);
        // =paint[:pitchDeg] paints what's straight ahead; =paintmodel:name every copy of a model (Game/LevelEditorPaint.cs).
        if (debugCommand.StartsWith("paintmodel:", StringComparison.Ordinal))
            editor.PaintSelfTest(debugCommand["paintmodel:".Length..], keep: editorDebug.Contains("+keep"));
        else if (debugCommand.StartsWith("paint", StringComparison.Ordinal))
        {
            editor.Look(debugPitch);
            editor.PaintSelfTest(null, keep: editorDebug.Contains("+keep"));
        }
        if (editorDebug.Contains("+shot:"))
            editorDebug = editorDebug[(editorDebug.IndexOf("+shot:") + 1)..];
    }
    if (editor.IsOpen)
    {
        var exit = editor.Update(dt, input, window.FramebufferSize);
        if (exit == LevelEditor.Exit.None)
        {
            DrawEditorFrame();
            if (editorDebug.Length > 0 && ++editorFrames == 60 && editorDebug != "open")
            {
                if (editorDebug.StartsWith("shot:", StringComparison.Ordinal))
                {
                    Renderer.SaveScreenshot(Path.Combine(AppContext.BaseDirectory, editorDebug[5..]), window.FramebufferSize);
                    Log.Write($"[EDITOR] Saved {editorDebug[5..]}");
                }
                window.ShouldClose = true;
            }
            continue;
        }
        // Saved (or reverted to the last save): the shadows drawn once at the start are drawn again round what's moved,
        // and editing carries on from the same view. The game picks the saved layout up the next time it starts.
        if (editor.KeptChanges)
            RenderStaticShadows();
        editorQuitWarned = false;
        editor.Open(editor.Camera, exit == LevelEditor.Exit.Keep
            ? "Saved (Assets/Levels/level_edits.json): the game loads it from its next start."
            : "Put back as last saved.");
        continue;
    }
#endif

    // --- Pause menu (Esc): resume or quit ---
    var justCaptured = false;
    if (!pause.IsOpen && input.WasPressed(GlfwKey.Escape) && (terminal.Close() || vaultTerminal.Close() || jungleTerminal.Close() || flatsTerminal.Close() || yardTerminal.Close() || factoryTerminal.Close() || keypad.Close() || locker.Close() || inventory.Close()))
    {
        input.ConsumePresses();   // Esc at the PC leaves the terminal rather than pausing
    }
    else if (!pause.IsOpen && input.WasPressed(GlfwKey.Escape))
    {
        input.SetMouseCaptured(false);
        pause.Open();
        input.ConsumePresses();
    }
    else if (pause.IsOpen)
    {
        switch (pause.Update(input))
        {
            case PauseMenu.Choice.Resume:
                pause.Close();
                input.SetMouseCaptured(true);
                justCaptured = true; // don't fire on the click that resumes the game
                break;
            case PauseMenu.Choice.Quit:
                window.ShouldClose = true;
                break;
        }
        input.ConsumePresses();   // (Enter would skip the intro or connect the PC, Space jump, ...)
    }
    else if (!input.MouseCaptured && input.WasPressed(GlfwMouseButton.Left))
    {
        input.SetMouseCaptured(true);
        justCaptured = true;
    }

    // --- Test script ---
    if (script.Count > 0)
    {
        if (scriptStep < 0 || now - scriptStepStart >= script[scriptStep].Seconds)
        {
            scriptStep++;
            scriptStepStart = now;
            if (scriptStep >= script.Count)
            {
                window.ShouldClose = true;
                script.Clear();
            }
        }

        input.ScriptedKeys.Clear();
        input.ScriptedAim = false;
        input.ScriptedFire = false;
        if (script.Count > 0)
        {
            var stepStarted = scriptStepStart == now;
            foreach (var c in script[scriptStep].Keys)
            {
                switch (c)
                {
                    case 'F' when stepStarted: input.InjectPress(GlfwMouseButton.Left); break;
                    case 'N' when stepStarted: input.InjectPress(GlfwMouseButton.Middle); break;
                    case 'R' when stepStarted: input.InjectPress(GlfwKey.R); break;
                    case 'E' when stepStarted: input.InjectPress(GlfwKey.E); break;
                    case 'I' when stepStarted: input.InjectPress(GlfwKey.I); break;
                    case '+' when stepStarted: input.InjectRespawn(); break;
                    case 'P' when stepStarted: scriptScreenshot = true; break;
                    case 'W': input.ScriptedKeys.Add(GlfwKey.W); break;
                    case 'A': input.ScriptedKeys.Add(GlfwKey.A); break;
                    case 'S': input.ScriptedKeys.Add(GlfwKey.S); break;
                    case 'D': input.ScriptedKeys.Add(GlfwKey.D); break;
                    case 'J': input.InjectPress(GlfwKey.Space); break;   // (a vault: pressed every frame of the step)
                    case 'L' when stepStarted: input.InjectPress(GlfwKey.C); break;   // (prone, or up from it)
                    case 'H': input.ScriptedKeys.Add(GlfwKey.LeftShift); break;
                    case 'C': input.ScriptedKeys.Add(GlfwKey.LeftControl); break;
                    case 'M': input.ScriptedAim = true; break;
                    case 'G': input.ScriptedFire = true; break;
                    case 'T': input.ScriptedKeys.Add(GlfwKey.G); break;
                    case 'Q': input.ScriptedKeys.Add(GlfwKey.Q); break;   // (a frag: let go in the next step to throw it)
                    case 'X': input.ScriptedKeys.Add(GlfwKey.X); break;   // (a smoke grenade, likewise)
                    case 'O': input.ScriptedKeys.Add(GlfwKey.T); break;   // (the goggles wheel)
                    case ']': input.InjectMouse(new float2(30f, 20f)); break;    // (the mouse, down and right)
                    case '[': input.InjectMouse(new float2(-30f, 20f)); break;   // (down and left)
                    case >= '3' and <= '9' or '0' when stepStarted: input.InjectPress(GlfwKey.Number0 + (c - '0')); break;
                    case '1' when stepStarted: input.InjectPress(GlfwKey.Number1); break;
                    case '2' when stepStarted: input.InjectPress(GlfwKey.Number2); break;
                    case 'E' when stepStarted: input.InjectPress(GlfwKey.E); break;
                    case '<' when stepStarted: input.InjectPress(GlfwKey.Up); break;
                    case '>' when stepStarted: input.InjectPress(GlfwKey.Down); break;
                    case '#' when stepStarted: input.InjectPress(GlfwKey.Enter); break;
                    case 'V' when stepStarted: input.InjectScroll(-1); break;
                    case 'Y' when stepStarted: input.InjectScroll(1); break;
                }
            }
            player.Pitch = script[scriptStep].Pitch;
            if (script[scriptStep].Yaw is { } yaw)
                player.Yaw = yaw;
            if (now - scriptLog >= 0.1)
            {
                scriptLog = now;
                Log.Write($"[SCRIPT] step {scriptStep} pos {player.Position.X:0.00},{player.Position.Y:0.00},{player.Position.Z:0.00} climbing {player.Climbing} grounded {player.Grounded} frame {dt * 1000:0.0} ms");
            }
        }
    }

    // --- Simulate ---
    if (input.MouseCaptured || script.Count > 0)
    {
        // Health first: a dead player lies still (the view drops) and comes back at the spawn point after a few seconds.
        var livesBefore = player.Respawns;
        player.UpdateHealth(dt);
        if (player.Respawns != livesBefore)
        {
            ResetLevelForRespawn();
            RestartIntroAtTheDoors();
            RestartRaidNearEnd();
        }
        var previousEye = player.EyePosition;
        var respawnsBefore = player.Respawns;
        if (intro.Active)
        {
            if (input.WasPressed(GlfwKey.Enter))
                intro.Skip(player);
            else
                intro.Update(dt, player, input);
        }
        if (raid is { Active: true })
        {
            if (input.WasPressed(GlfwKey.Enter))
                raid.Skip(player);
            else
                raid.Update(dt, player, input);
        }
        else
            raid?.UpdateVan(dt);   // (the van driving off once you're out)
        if (antwerp is not null)
        {
            if (antwerp.Active && input.WasPressed(GlfwKey.Enter))
                antwerp.Skip(player);
            else
                antwerp.Update(dt, player, input);   // (after the way in too: the boat bobs at the quay, the team follow you)
        }
        // The goggles wheel (T held): while it's up the mouse moves its pointer, not the view.
        goggles.Update(input, (input.MouseCaptured || script.Count > 0) && !intro.Active && !AtAScreen() && player.Alive, 2f / float.Max(window.FramebufferSize.Y, 1));
        if (player.Alive)
        {
            player.LookScale = weapon.Scoped ? 1f / weapon.ScopeZoom : 1f;   // through a scope the view turns as slowly as it's zoomed
            if (goggles.IsOpen)
                player.LookScale = 0f;
            // On a raid, what the whole kit weighs sets the pace (Game/RaidLoadout.cs), and the gun in hand only takes
            // half its usual toll on top (its weight is in the kit's already: this is how it handles).
            player.LoadScale = raidSpeed is { } kitSpeed ? kitSpeed * (1f + (weapon.MoveScale - 1f) * 0.5f) : weapon.MoveScale;
            // Getting down and up: the kit's weight, or a heavy gun in hand on its own (the M249 or the big rifle: about a half).
            player.KitLoad = float.Max(raidSpeed is null ? 0f : raidStanceLoad, (1f - weapon.MoveScale) * 1.5f);
            // The NPC test build: + resets the test area's people (back at their spots, unhurt and calm) and leaves you where you are.
            if (Npcs.NpcTest && input.RespawnPressed)
                npcs.RespawnTestArea();
            player.Update(dt, input);
            npcs.Collide(player);
            sounds.UpdatePlayer(player);   // footsteps and landings (heard by people nearby too)
        }
        // A respawn is a jump across the map, not a walk: don't test that segment against the portal planes.
        if (player.Respawns == respawnsBefore)
            portals.TryTeleport(player, previousEye);
        else
        {
            ResetLevelForRespawn();
            RestartIntroAtTheDoors();   // (the respawn key, or falling out of the world)
            RestartRaidNearEnd();
        }
    }

    FrameStats.Mark("input");
    if (debugPosition && (int)(now * 4) != (int)((now - dt) * 4))
        Log.Write($"[DEBUG] pos {player.Position.X:0.00},{player.Position.Y:0.00},{player.Position.Z:0.00} yaw {player.Yaw:0.00} grounded {player.Grounded}");

    camera = player.ApplyTo(camera);
    if (shotIndex < shots.Count)
    {
        camera.Position = shots[shotIndex].Eye;
        camera.Yaw = shots[shotIndex].Yaw;
        camera.Pitch = shots[shotIndex].Pitch;
    }

    // Switching guns: the number keys, as set at a gun locker (slots); or the mouse wheel, towards you for the next gun and away for the last,
    // going round, on from the one already coming up if you keep scrolling. The one in hand is lowered out of view first,
    // then the other comes up. (Using a scope, the wheel only turns that, away for more, and never changes guns, even if the scope has one power.)
    var playing = input.MouseCaptured || script.Count > 0;
    if (playing && !InCutscene() && !AtAScreen() && input.WasPressed(GlfwMouseButton.Middle))
        weapon.TurnVision();   // (looking through a scope: night vision, then thermal, then off)
    // (A raid's cutscene lets you pick your gun and goggles on the way in; the prison intro doesn't: its hands are the script's.)
    if (playing && !intro.Active && !keypad.IsOpen && !locker.IsOpen && !inventory.IsOpen && !gunsLocked)
    {
        var wheel = input.ScrollSteps != 0 ? slots.OfType<IGun>().ToArray() : [];   // (only made on a frame the wheel turned)
        IGun? wanted = null;
        for (var number = 0; number < slots.Length; number++)
            if (input.WasPressed(GlfwKey.Number0 + number))
                wanted = slots[number];
        if (wanted is null && input.ScrollSteps != 0 && !weapon.TurnScope(input.ScrollSteps) && wheel.Length > 0)
            wanted = wheel[((Array.IndexOf(wheel, nextGun ?? weapon) - input.ScrollSteps) % wheel.Length + wheel.Length) % wheel.Length];
        if (wanted is not null)
        {
            if (wanted == weapon)
            {
                nextGun = null;        // changed your mind: bring it back up
                if (InCutscene())
                    ShowCutsceneGun(weapon);
                else
                    weapon.Raise();
            }
            else
            {
                nextGun = wanted;
                weapon.Holster();
                Log.Write($"[GUN] Switching to the {wanted.Name}");
            }
        }
    }
    // The armoured car's turret: its kick and its belt's cover (its gunner lays it: Game/NpcGunner.cs).
    carTurret?.Update(dt, false, player.Position, false, out _);
    // The M249 (Game/GunNest.cs): E takes it up, or at the nest puts it back on the tripod; a respawn sends it back there.
    if (lmg is not null && gunNest is not null)
    {
        if (player.Respawns != nestRespawns)
        {
            nestRespawns = player.Respawns;
            if (weapon == lmg && !locker.Chosen(lmg) && !lmgFromLoadout)
                weapon = gunBeforeLmg;
            if (nextGun is null || nextGun == lmg)
            {
                nextGun = null;
                if (!InCutscene())
                    weapon.Raise();   // (a restart into a cutscene keeps it down: the script raises it when it's over)
            }
            gunNest.Reset();
            if (!lmgFromLoadout)
                TakeLmgOffItsNumber();
            if (rangeStart || lmgFromLoadout)   // (a raid's loadout's M249 comes back with you, as the range's does)
            {
                gunNest.StartCarried();
                PutLmgOnANumber();
            }
        }

        var usePressed = playing && !InCutscene() && !AtAScreen() && locker.Prompt is null && input.WasPressed(GlfwKey.E);
        if (gunNest.Update(dt, usePressed, player.Position, weapon == lmg && nextGun is null, out var putBack))
        {
            PutLmgOnANumber();
            nextGun = lmg;
            weapon.Holster();
        }
        else if (putBack)
        {
            TakeLmgOffItsNumber();
            nextGun = gunBeforeLmg;
            weapon.Holster();
        }
    }
    // A gun locker (Game/GunLocker.cs): Enter on a gun brings it up.
    if (locker.Update(input, player.Position, playing && !InCutscene() && !gunsLocked, terminal.IsOpen || vaultTerminal.IsOpen || jungleTerminal.IsOpen || flatsTerminal.IsOpen || yardTerminal.IsOpen || factoryTerminal.IsOpen || keypad.IsOpen) is { } fromLocker)
        BringUp(fromLocker);
    if (nextGun is not null && weapon.Holstered)
    {
        if (nextGun == lmg && weapon != lmg)
            gunBeforeLmg = weapon;
        weapon = nextGun;
        nextGun = null;
        if (InCutscene())
        {
            weapon.HolsterNow();   // (the script raises it when it's over)
            ShowCutsceneGun(weapon);
        }
        else
            weapon.Raise();
    }
    cutsceneGunShown = float.Max(0f, cutsceneGunShown - dt);

    terminal.Update(dt, input, player.Position, playing);
    vaultTerminal.Update(dt, input, player.Position, playing);
    jungleTerminal.Update(dt, input, player.Position, playing);
    flatsTerminal.Update(dt, input, player.Position, playing);
    yardTerminal.Update(dt, input, player.Position, playing);
    factoryTerminal.Update(dt, input, player.Position, playing);
    var atRaidVan = raid?.AtVan(player) ?? false;   // (turned to the van at the hall's door: E is the van's, not the door's)
    level.UpdateFactoryDoor(dt, playing && !InCutscene() && !atRaidVan && input.WasPressed(GlfwKey.E), player.Position);
    level.UpdateFactoryRearDoor(dt, playing && !InCutscene() && input.WasPressed(GlfwKey.E), player.Position);
    level.UpdateFactorySideDoor(dt, playing && !InCutscene() && input.WasPressed(GlfwKey.E), player.Position);
    raid?.UpdateWayToHq(playing && !AtAScreen() && input.WasPressed(GlfwKey.E), player);
    level.UpdateBodegaDoor(dt, playing && input.WasPressed(GlfwKey.E), player.Position);
    level.UpdateStationDoor(dt, playing && input.WasPressed(GlfwKey.E), player.Position);
    level.UpdateFlatsDoors(dt, playing && !intro.Active && input.WasPressed(GlfwKey.E), player.Position);
    level.UpdateMotelDoors(dt, playing && input.WasPressed(GlfwKey.E), player.Position);
    level.UpdateRioStoreDoor(dt, playing && input.WasPressed(GlfwKey.E), player.Position);
    level.UpdatePoliceMedkit(playing && !InCutscene() && !AtAScreen() && input.WasPressed(GlfwKey.E), player);
    firingRange.Update(dt, playing && input.WasPressed(GlfwKey.E), player.Position, holes, audio);
    level.UpdatePrisonDoors(dt);
    level.PrisonDoorsLocked = intro.Active;   // (the intro opens them as it needs them)
    portals.UpdateBackBlockers(player.Position);
    level.UpdateAirlock(dt, player.Position, camera.Forward);
    keypad.Update(dt, input, level, player.Position, playing);
    // The inventory (Game/Inventory.cs): after the other screens, as it gives way to any that came up this frame.
    if (inventory.Update(input, playing && !InCutscene() && player.Alive,
            terminal.IsOpen || vaultTerminal.IsOpen || jungleTerminal.IsOpen || flatsTerminal.IsOpen || yardTerminal.IsOpen || factoryTerminal.IsOpen || keypad.IsOpen || locker.IsOpen, weapon, gunsLocked) is { } fromInventory)
        BringUp(fromInventory);
    // A screen that's up has the mouse pointer (the view holds still); shut, the mouse turns the view again.
    input.SetUiCursor(AtAScreen());

    // Paused (mouse released): the world holds still with the player: no shooting at a frozen player, no reload
    // finishing in the background. It still runs for screenshots, and for one frame at the start (with no time passing,
    // so nobody notices anything) so everyone is posed.
    var worldDt = playing || shots.Count > 0 ? dt : 0f;
    var simulate = worldDt > 0 || !worldStarted;
    worldStarted = true;
    var canUseHands = playing && !justCaptured && !AtAScreen() && player.Alive && !InCutscene() && !goggles.IsOpen;
    FrameStats.Mark("player+doors");
    if (Level.ZoneAt(player.Position) is var zoneNow && zoneNow != lastZone)
    {
        Log.Write($"[ZONE] {lastZone} -> {zoneNow}");
        lastZone = zoneNow;
    }

    // The intro has the player's hands from the charge on (the rifle comes up to the eye, and its trigger is the script's).
    var introHasRifle = intro.Active && weapon != baton;
    if (introHasRifle)
    {
        input.ScriptedAim = true;
        input.ScriptedFire = intro.PlayerFiring;
    }
    else if (script.Count == 0)
    {
        input.ScriptedAim = false;
        input.ScriptedFire = false;
    }
    if (simulate)
        weapon.Update(worldDt, input, camera, player, allowFire: canUseHands && !player.GettingUp
            || (playing && intro.Active && player.Alive && (weapon == baton ? intro.BatonFree : intro.PlayerFiring)));
    // Every gun the player can hold (the locker's British guns too: they used to be left out, so nobody heard them).
    var shotsNow = 0;
    foreach (var entry in catalog)
        shotsNow += entry.Gun.ShotsFired;
    if (shotsNow != shotsHeard)
    {
        // Bang (the sound itself is played by the weapon): people within earshot hear it, as far as the gun's loudness carries.
        shotsHeard = shotsNow;
        npcs.HearGunshot(camera.Position, byPlayer: true, heavy: lmg is not null && ReferenceEquals(weapon, lmg), volume: weapon.Loudness);
    }

    // The player starting a reload: anyone fighting them near enough to hear the magazine come out, or seeing it, knows
    // they've a moment (Npcs.PlayerReloading).
    if (weapon.Reloading && !playerWasReloading && player.Alive)
        npcs.PlayerReloading(camera.Position);
    playerWasReloading = weapon.Reloading;

    portals.Update((float)now);
    // Debug: BRUTALFPS_LINKAT="seconds:a:b" connects portal a to b that long into the run, as a terminal would (people near either react).
    if (linkAt is { } pendingLink && now - runStart >= pendingLink.At)
    {
        linkAt = null;
        if (portals.Find(pendingLink.A) is { } linkA && portals.Find(pendingLink.B) is { } linkB)
            portals.Connect(linkA, linkB);
        else
            Log.Write($"[WARN] BRUTALFPS_LINKAT: no portal called {pendingLink.A} or {pendingLink.B}");
    }
    if (simulate)
    {
        FrameStats.Mark("weapon");
        Relevance.BeginFrame(camera.Position, weapon.Scoped ? weapon.ScopeZoom : 80f / float.Max(camera.FieldOfViewDegrees, 1f));
        projectiles.Update(worldDt);
        FrameStats.Mark("projectiles");
        flashbangs.Update(worldDt, input, player, camera,
            canThrow: canUseHands && !throwables.Aiming);
        throwables.Update(worldDt, input, player, camera,
            canThrow: canUseHands && !flashbangs.Aiming);
        FrameStats.Mark("flashbangs");
        flares.Update(worldDt, player);
        FrameStats.Mark("flares");
        physics?.Step(worldDt);
        FrameStats.RunTestHitches();
        FrameStats.Mark("physics");
        debris.Update(worldDt);
        npcs.Senses.PlayerArmed = !InCutscene();   // (holstered on the way through the prison: nobody's frightened of you)
        npcs.Senses.PlayerConcealed = raid is { Active: true } || antwerp is { Concealing: true };   // (still getting in: nobody knows you're there)
        npcs.Senses.Vehicle = raid?.VanInTheWay ?? [];
        npcs.Update(worldDt, player, camera);
        using (Perf.Evacuation.Time()) evacuation?.Update(worldDt, player);
        army?.Update(worldDt, player);
        FrameStats.Mark("npcs");
        if (Npcs.StressTest && float.Floor((float)now) != float.Floor((float)(now - dt)) && Level.ZoneAt(player.Position) == Zones.Rio)
        {
            npcs.LogStressStatus(physics?.Bodies ?? 0, debris.Count, projectiles.Count);
            if (physics is not null)
            {
                Log.Write($"[STRESS] physics: {physics.AwakeBodies()} bodies awake, {physics.SubSteps} steps in the last second, {physics.Reused} bodies reused from the pool so far ({physics.Pooled} spare)");
                physics.SubSteps = 0;
            }
        }
        sounds.UpdateNpcs(worldDt, camera.Position);
        foreach (var cam in securityCameras)
            cam.Update(worldDt, player.Position + new float3(0, player.BodyHeight * 0.7f, 0), player.Alive);
        playerDouble?.Update(worldDt, player);
        particles.Update(worldDt);
        chinook?.Update(worldDt);
        harbour.Update(worldDt);
        intro.Delivery.Update(worldDt);
        intro.Squad.Update(worldDt);
        intro.UpdateLink(worldDt);
        foreach (var target in targets)
            target.Update(worldDt);
    }

    if (audio is not null)
        audio.Deafness = float.Max(flashbangs.Deafness, throwables.Deafness);
    FrameStats.Mark("rest of sim");
    using (Perf.AudioUpdate.Time()) audio?.Update(dt, camera.Position, camera.Forward, player.Velocity);
    FrameStats.Mark("audio");
    particles.Build();
    throwables.BuildSmoke();
    FrameStats.Mark("particles");
    var titleDue = hud.TitleDue(realDt);   // (the title's text is only put together on the frames that set it: four a second)
    hud.Update(dt, realDt, window, weapon, portals, input.MouseCaptured, player, npcs.PlayerWanted, !titleDue ? "" : flashbangs.Infinite ? "∞" : flashbangs.Carried.ToString(),
        !titleDue ? "" : throwables.Infinite ? "   Frags: ∞ [Q]   Smoke: ∞ [X]" : $"   Frags: {throwables.Frags} [Q]   Smoke: {throwables.Smokes} [X]");

    FrameStats.Mark("hud title");

    // --- Render ---
    var cpuStart = System.Diagnostics.Stopwatch.GetTimestamp();
    FrameStats.BeginGpu();
    renderer.SetTime((float)now);
    var framebuffer = window.FramebufferSize;
    if (framebuffer.X > 0 && framebuffer.Y > 0)
    {
        var aspect = (float)framebuffer.X / framebuffer.Y;
        // The prison's sun shadows are rendered once, like the others', but the helicopter and its load move through them.
        if (intro.Delivery.ConsumeShadowsChanged() | intro.Delivery.ShadowsChanging)
        {
            using var shadowTimer = Perf.PrisonShadows.Time();
            renderer.BeginShadowPass(prisonShadow);
            level.Draw(renderer, Zones.Prison, shadowPass: true);
            intro.Delivery.Draw(renderer, Zones.Prison);
            renderer.EndShadowPass();
        }
        // ...and the factory's, drawn once more with the van that's now parked there.
        if (raid?.ConsumeShadowsChanged() == true)
        {
            using var shadowTimer = Perf.FactoryShadows.Time();
            renderer.BeginShadowPass(factoryShadow);
            level.Draw(renderer, Zones.Warehouse, shadowPass: true);
            renderer.EndShadowPass();
        }
        // What's drawn: aiming narrows the view a little (a scope zooms it), and recoil kicks it (a jolt, plus the walk of the aim that the
        // next shot follows).
        var view = camera;
        view.FieldOfViewDegrees = weapon.FieldOfView(Camera.Default.FieldOfViewDegrees);
        // Clamp again after the kick: past +-90 degrees the look-at flips the view (and the portal views) upside down.
        view.Pitch = float.Clamp(view.Pitch + weapon.ViewKick.X, -1.55f, 1.55f);
        view.Yaw += weapon.ViewKick.Y;

        // A shot's flash of light (lights the hands and whatever is near), then the bunker's security camera (its monitor
        // can show in the portal views too) and the world as seen through each portal, into off-screen textures...
        var flash = weapon.FlashLight(view);
        if (flashbangs.Light is { W: > 0f } bang && bang.W > flash.W)
            flash = bang;   // (a flashbang going off lights everything round it)
        if (throwables.Light is { W: > 0f } boom && boom.W > flash.W)
            flash = boom;   // (and so does a frag's fireball)
        if (raid is { Light: { W: > 0f } bulb } && bulb.W > flash.W)
            flash = bulb;   // (the dim bulb in the back of the van)
        renderer.SetFlash(flash.XYZ, flash.W);
        using (Perf.FlareShadows.Time()) flares.RenderShadows(renderer, level, flareShadows);
        flares.Apply(renderer);
        if (!Profile.Has("noportals"))
        {
            // (The monitors are in the police station: no feeds unless you're there, or a portal there is open and might show them.)
            var monitorsInSight = Level.ZoneAt(camera.Position) == Zones.Police || portals.Hq.Online || portals.Vault.Online;
            using var feedsTimer = Perf.CameraFeeds.Time();
            foreach (var cam in monitorsInSight ? securityCameras : [])
            {
                cam.RenderFeed(renderer, zones =>
                {
                    DrawScene(zones, depth: 2);
                    if ((Level.ZoneAt(player.Position) & zones) != 0)
                        playerDouble?.Draw(renderer);
                });
            }
        }
        // Through a tube scope all of the view is the scope's picture, so it's all seen in the scope's vision (a prism sight's
        // is only its magnified circle, below). The camera feeds above are what's on the monitors, whatever you look at them through.
        // (Goggles show all of it in theirs, unless a scope's own vision is on.)
        var scopeVision = weapon.Vision != ScopeVision.Off ? weapon.Vision : goggles.Vision;
        var mainVision = weapon.Scoped && weapon.ScopeKind == ScopeKind.Tube ? scopeVision : goggles.Vision;
        renderer.SetVision(mainVision);
        if (!Profile.Has("noportals"))
        {
            using (Perf.PortalViews.Time()) portals.RenderViews(renderer, view, framebuffer, DrawPortalScene);
            using (Perf.MirrorViews.Time()) mirrors.Render(renderer, view, framebuffer, DrawMirrorScene);
        }
        FrameStats.Mark("portal views");

        // ...then the world from the player's eyes, with the portal surfaces showing those textures.
        var atOrigin = view with { Position = float3.Zero };
        renderer.BeginView(null, framebuffer, atOrigin.View * atOrigin.Projection(aspect), view.Position, relativeTo: true);
        if (!Profile.Has("noscene"))
            DrawScene(Level.ZonesSeenFrom(camera.Position), depth: 0);

        // Through an ACOG: its magnified picture, the world drawn again zoomed in the square round its circle in the
        // middle of the view (the HUD's eyepiece ring covers the square's corners). The portals and mirrors in it need
        // views of their own, for its projection.
        if (weapon.Scoped && weapon.ScopeKind == ScopeKind.Acog && !Profile.Has("noscene"))
        {
            var scope = view with { FieldOfViewDegrees = weapon.ScopeFieldOfView(Camera.Default.FieldOfViewDegrees) };
            renderer.SetVision(scopeVision);
            if (!Profile.Has("noportals"))
            {
                portals.RenderViews(renderer, scope, framebuffer, DrawPortalScene);
                mirrors.Render(renderer, scope, framebuffer, DrawMirrorScene);
            }
            var scopeAtOrigin = scope with { Position = float3.Zero };
            renderer.BeginView(null, framebuffer, scopeAtOrigin.View * scopeAtOrigin.Projection(aspect), scope.Position, scissor: Hud.AcogScissor(framebuffer), relativeTo: true);
            DrawScene(Level.ZonesSeenFrom(camera.Position), depth: 0);
        }

        renderer.SetVision(ScopeVision.Off);   // (the hands and the HUD are on this side of the scope)
        // Standing in a smoke screen: grey all round (but a thermal scope sees straight through it).
        if (!(weapon.Scoped && weapon.Vision == ScopeVision.Thermal) && mainVision != ScopeVision.Thermal && throwables.SmokeAt(view.Position) is var inSmoke and > 0.001f)
            renderer.DrawWhiteout(inSmoke * 0.995f, new float3(0.55f, 0.56f, 0.57f));   // (all but opaque: the puffs right round the eye aren't drawn, so nothing else hides the world)
        FrameStats.Mark("main view");
        // Weapon view model on top of the world.
        renderer.ClearDepth();
        renderer.SetVision(weapon.Scoped ? ScopeVision.Off : goggles.Vision);   // (the hands, through the goggles too)
        renderer.BeginViewModel(view, aspect);   // (drawn relative to the camera: see BeginViewModel)
        weapon.Draw(renderer, view with { Position = float3.Zero });
        renderer.EndViewModel();
        renderer.SetVision(ScopeVision.Off);
        if (!weapon.Scoped)
            goggles.DrawVignette(renderer);

        if (intro.Active)
        {
            intro.Draw(renderer, framebuffer);   // (no HUD while it plays: the bars, fades and captions instead)
        }
        else if (raid is { Active: true })
        {
            raid.Draw(renderer, framebuffer);
            DrawCutsceneChoices(framebuffer);
        }
        else if (antwerp is { Active: true })
        {
            antwerp.Draw(renderer, framebuffer);
            DrawCutsceneChoices(framebuffer);
        }
        else
        {
            hud.Draw(renderer, framebuffer, weapon, dt, player, view);
            terminal.Draw(renderer, framebuffer);
            vaultTerminal.Draw(renderer, framebuffer);
            jungleTerminal.Draw(renderer, framebuffer);
            flatsTerminal.Draw(renderer, framebuffer);
            yardTerminal.Draw(renderer, framebuffer);
            factoryTerminal.Draw(renderer, framebuffer);
            if (level.FactoryDoorPrompt is { } doorPrompt && raid?.Prompt is null)
                PcTerminal.DrawPrompt(renderer, framebuffer, doorPrompt);
            if (level.FactoryRearDoorPrompt is { } rearDoorPrompt)
                PcTerminal.DrawPrompt(renderer, framebuffer, rearDoorPrompt);
            if (level.FactorySideDoorPrompt is { } sideDoorPrompt)
                PcTerminal.DrawPrompt(renderer, framebuffer, sideDoorPrompt);
            if (raid?.Prompt is { } ridePrompt && !AtAScreen())
                PcTerminal.DrawPrompt(renderer, framebuffer, ridePrompt);
            if (level.BodegaDoorPrompt is { } shackPrompt)
                PcTerminal.DrawPrompt(renderer, framebuffer, shackPrompt);
            if (level.StationDoorPrompt is { } stationPrompt)
                PcTerminal.DrawPrompt(renderer, framebuffer, stationPrompt);
            if (level.FlatsDoorPrompt is { } flatsPrompt)
                PcTerminal.DrawPrompt(renderer, framebuffer, flatsPrompt);
            if (level.RioStoreDoorPrompt is { } rioStorePrompt)
                PcTerminal.DrawPrompt(renderer, framebuffer, rioStorePrompt);
            if (level.MotelDoorPrompt is { } motelPrompt)
                PcTerminal.DrawPrompt(renderer, framebuffer, motelPrompt);
            if (level.PoliceMedkitPrompt is { } medkitPrompt && !AtAScreen())
                PcTerminal.DrawPrompt(renderer, framebuffer, medkitPrompt);
            if (gunNest?.Prompt is { } nestPrompt)
                PcTerminal.DrawPrompt(renderer, framebuffer, nestPrompt);
            if (firingRange.Prompt is { } rangePrompt)
                PcTerminal.DrawPrompt(renderer, framebuffer, rangePrompt);
            if (locker.Prompt is { } lockerPrompt)
                PcTerminal.DrawPrompt(renderer, framebuffer, lockerPrompt);
            locker.Draw(renderer, framebuffer, view.Position, weapon);
            firingRange.DrawReadout(renderer, framebuffer);
            keypad.Draw(renderer, framebuffer);
            if (!keypad.IsOpen && level.AirlockPrompt is { } airlockPrompt)
                PcTerminal.DrawPrompt(renderer, framebuffer, airlockPrompt);
            inventory.Draw(renderer, framebuffer, view.Position, weapon);
            goggles.Draw(renderer, framebuffer);
        }
        FrameStats.Mark("hands+hud");
        renderer.DrawWhiteout(flashbangs.Glare, new float3(1f, 0.98f, 0.95f));   // dazzled by a flashbang
        pause.Draw(renderer, framebuffer);
        if (scriptScreenshot)
        {
            scriptScreenshot = false;
            var file = Path.Combine(AppContext.BaseDirectory, $"script_{scriptStep}.png");
            Renderer.SaveScreenshot(file, framebuffer);
            if (Environment.GetEnvironmentVariable("BRUTALFPS_DUMPVIEWS") == "1")
                for (var c = 0; c < securityCameras.Length; c++)
                    if (securityCameras[c].Feed.Size.X > 0)
                        Renderer.SaveScreenshot(Path.ChangeExtension(file, null) + (c == 0 ? "_cctv.png" : $"_cctv{c + 1}.png"), securityCameras[c].Feed.Size, securityCameras[c].Feed);
            Log.Write($"[SCRIPT] Saved {file}: {holes.Count} holes, {debris.Count} debris, {particles.Count} particles, {physics?.Bodies ?? 0} bodies");
        }
        if (shotIndex < shots.Count)
            Brutal.OpenGlApi.OpenGl.Finish(); // so the shot log's timing covers the GPU's work too

        if (shotIndex < shots.Count && shotFrames == 1)
        {
            shotTime = Glfw.Time;
            shotCpu = 0;
        }
        shotCpu += System.Diagnostics.Stopwatch.GetElapsedTime(cpuStart).TotalMilliseconds;
        if (shotIndex < shots.Count && ++shotFrames == 12)
        {
            var frameMs = (Glfw.Time - shotTime) / 11 * 1000;
            Renderer.SaveScreenshot(shots[shotIndex].File, framebuffer);
            if (Environment.GetEnvironmentVariable("BRUTALFPS_DUMPVIEWS") == "1")
            {
                // Debug: what the security cameras see, and what each portal's views hold (its own, and its second-level one).
                for (var c = 0; c < securityCameras.Length; c++)
                    if (securityCameras[c].Feed.Size.X > 0)
                        Renderer.SaveScreenshot(Path.ChangeExtension(shots[shotIndex].File, null) + (c == 0 ? "_cctv.png" : $"_cctv{c + 1}.png"), securityCameras[c].Feed.Size, securityCameras[c].Feed);
                foreach (var portal in portals.Portals)
                {
                    var name = portal.Name.Replace(' ', '_');
                    if (portal.View.Size.X > 0)
                        Renderer.SaveScreenshot(Path.ChangeExtension(shots[shotIndex].File, null) + $"_{name}.png", portal.View.Size, portal.View);
                    if (portal.InnerView.Size.X > 0)
                        Renderer.SaveScreenshot(Path.ChangeExtension(shots[shotIndex].File, null) + $"_{name}_inner.png", portal.InnerView.Size, portal.InnerView);
                }
            }
            Log.Write($"[SHOT] Saved {shots[shotIndex].File} ({frameMs:0.0} ms per frame, {shotCpu / 11:0.0} ms of it rendering; people drawn per LOD {string.Join("/", npcs.LodCounts)})");
            shotFrames = 0;
            if (++shotIndex == shots.Count)
                window.ShouldClose = true;
        }
    }

    FrameStats.Mark("other");
    FrameStats.EndGpu();
    using (Perf.Swap.Time()) window.SwapBuffers();
    FrameStats.Mark("swap (gpu)");
    FrameStats.EndFrame();
    // Minimized (0x0 framebuffer): nothing was drawn and vsync may not block, so don't spin a core at 100%.
    if (framebuffer.X <= 0 || framebuffer.Y <= 0)
        Thread.Sleep(16);
}

FrameStats.Report();

// GL objects must go before the context, and the window before GLFW shuts down.
debris.Clear();
npcs.ReleaseRagdolls();   // bodies out of the physics world before it goes
audio?.Dispose();
physics?.Dispose();
portals.Dispose();
mirrors.Dispose();
foreach (var cam in securityCameras)
    cam.Dispose();
textures.Dispose();
yardSky?.Dispose();
outbackSky?.Dispose();
yardShadow.Dispose();
outbackShadow.Dispose();
shipShadow.Dispose();
jungleShadow.Dispose();
factoryShadow.Dispose();
policeOcclusion.Dispose();
prisonShadow.Dispose();
flatsShadow.Dispose();
rioShadow.Dispose();
shipSky?.Dispose();
floodShadows.Dispose();
flareShadows.Dispose();
renderer.Dispose();
window.Dispose();
Glfw.Terminate();

// A gun picked during a raid's cutscene: its name for a moment (the gun itself stays down).
void ShowCutsceneGun(IGun gun)
{
    cutsceneGunName = gun.Name.ToUpperInvariant();
    cutsceneGunShown = 1.5f;
    Log.Write($"[GUN] {gun.Name} picked during the cutscene");
}

// Over a raid's cutscene: the goggles wheel while T is held, and the gun just picked.
void DrawCutsceneChoices(int2 framebuffer)
{
    if (cutsceneGunShown > 0f && cutsceneGunName is not null)
        PcTerminal.DrawPrompt(renderer, framebuffer, cutsceneGunName);
    goggles.Draw(renderer, framebuffer);
}

// A script has the player: the prison intro, or the way into a factory raid.
bool InCutscene() => intro.Active || raid is { Active: true } || antwerp is { Active: true };

// Using a PC or a keypad: the hands are on that, not the gun.
bool AtAScreen() => terminal.IsOpen || vaultTerminal.IsOpen || jungleTerminal.IsOpen || flatsTerminal.IsOpen || yardTerminal.IsOpen || factoryTerminal.IsOpen || keypad.IsOpen || locker.IsOpen || inventory.IsOpen;

// A gun picked at a gun locker or in the inventory: the one in hand is lowered and this one comes up.
void BringUp(IGun gun)
{
    if (gun == weapon)
    {
        nextGun = null;
        weapon.Raise();
    }
    else
    {
        nextGun = gun;
        weapon.Holster();
        Log.Write($"[GUN] Switching to the {gun.Name}");
    }
}

// Alt+Enter (Shift or not): fullscreen <-> windowed (centred on the monitor).
void FullscreenKey()
{
    if (input.WasPressed(GlfwKey.Enter) && (input.IsDown(GlfwKey.LeftAlt) || input.IsDown(GlfwKey.RightAlt)) && hasMonitor)
    {
        var mode = monitor.VideoMode;
        if (window.IsFullscreen)
            window.SetWindowed(monitor.Position + (new int2(mode.Width, mode.Height) - windowedSize) / 2, windowedSize);
        else
            window.SetFullscreen(monitor, new int2(mode.Width, mode.Height), mode.RefreshRate);
        Log.Write($"[WINDOW] Now {(window.IsFullscreen ? "fullscreen" : "windowed")}");
        input.ConsumePresses();   // (Enter would skip the intro or connect the PC)
    }
}

void DrawScene(Zones zones, int depth)
{
    level.Draw(renderer, zones);
    foreach (var target in targets)
        if ((zones & Level.ZoneAt(target.Center)) != 0)
            target.Draw(renderer);
    firingRange.Draw(renderer, zones);
    gunNest?.Draw(renderer, zones);
    carTurret?.Draw(renderer, zones);
    chinook?.Draw(renderer, zones);
    harbour.Draw(renderer, zones);
    intro.Delivery.Draw(renderer, zones);
    raid?.DrawWorld(renderer, zones);
    army?.Draw(renderer, zones);
    antwerp?.DrawWorld(renderer, zones);
    npcs.Draw(renderer, zones, mainView: depth == 0);
    debris.Draw(renderer, zones);
    projectiles.Draw(renderer, zones);
    flashbangs.Draw(renderer, zones);
    throwables.Draw(renderer, zones);
    flares.Draw(renderer, zones);
    foreach (var cam in securityCameras)
        cam.Draw(renderer, zones);
    portals.Draw(renderer, depth);
    mirrors.Draw(renderer, zones, depth);
    npcs.DrawShadows(renderer);
    // See-through things last: window glass, bullet holes over the surfaces, then dust, sparks and tracers.
    level.DrawGlass(renderer, zones);
    chinook?.DrawBlur(renderer, zones);
    intro.Delivery.DrawBlur(renderer, zones);
    holes.Draw(renderer);
    particles.Draw(renderer);
    throwables.DrawSmoke(renderer);
}

// The scene seen through a portal: out of its exit, at its level (1 = seen directly through a portal, 2 = through two).
void DrawPortalScene(Portal exit, int depth) => DrawScene(Level.ZonesSeenFrom(exit.Center), depth);

// The scene in a mirror.
void DrawMirrorScene(Zones zones)
{
    DrawScene(zones, depth: 2);   // (the portals in it show a glow)
    if ((Level.ZoneAt(player.Position) & zones) != 0)
        playerDouble?.Draw(renderer);   // (you see yourself)
}

#if LEVEL_EDITOR
// A frame of the level editor (Game/LevelEditor.cs): the world from its camera, the outlines round what's picked, its panels.
void DrawEditorFrame()
{
    FrameStats.BeginGpu();
    renderer.SetTime((float)Glfw.Time);
    var framebuffer = window.FramebufferSize;
    if (framebuffer.X > 0 && framebuffer.Y > 0)
    {
        var view = editor.Camera;
        var aspect = (float)framebuffer.X / framebuffer.Y;
        renderer.SetFlash(float3.Zero, 0f);
        renderer.SetVision(ScopeVision.Off);
        if (!Profile.Has("noportals") && !editor.Painting)
        {
            portals.RenderViews(renderer, view, framebuffer, DrawPortalScene);
            mirrors.Render(renderer, view, framebuffer, DrawMirrorScene);
        }
        var atOrigin = view with { Position = float3.Zero };
        renderer.BeginView(null, framebuffer, atOrigin.View * atOrigin.Projection(aspect), view.Position, relativeTo: true);
        if (editor.Painting)
            editor.DrawPaintSubject(renderer);   // (the paint tool: just what's being painted)
        else
            DrawScene(Level.ZonesSeenFrom(view.Position), depth: 0);
        renderer.ClearDepth();
        editor.DrawWorld(renderer);
        editor.DrawOverlay(renderer, framebuffer, input.CursorSquare);
    }
    FrameStats.EndGpu();
    window.SwapBuffers();
    FrameStats.EndFrame();
    if (framebuffer.X <= 0 || framebuffer.Y <= 0)
        Thread.Sleep(16);
}

// The shadow maps drawn once at the start (the suns', the yard's floodlights', the police station's furniture), drawn
// again after the level editor has moved things.
void RenderStaticShadows()
{
    var start = System.Diagnostics.Stopwatch.GetTimestamp();
    renderer.BeginShadowPass(policeOcclusion);
    level.DrawPoliceOccluders(renderer);
    renderer.EndShadowPass();
    foreach (var (shadowMap, zones) in (ReadOnlySpan<(ShadowMap, Zones)>)[(yardShadow, Zones.Yard), (outbackShadow, Zones.Outback | Zones.Cellar),
                 (shipShadow, Zones.Ship), (jungleShadow, Zones.Jungle), (factoryShadow, Zones.Warehouse), (prisonShadow, Zones.Prison),
                 (flatsShadow, Zones.Flats), (rioShadow, Zones.Rio)])
    {
        renderer.BeginShadowPass(shadowMap);
        level.Draw(renderer, zones, shadowPass: true);
        renderer.EndShadowPass();
    }
    for (var i = 0; i < int.Min(floods.Length, FloodShadowAtlas.MaxLights); i++)
    {
        renderer.BeginShadowTile(floodShadows, i, floods[i].ViewProjection);
        level.Draw(renderer, Zones.Yard, shadowPass: true);
        renderer.EndShadowPass();
    }
    Log.Write($"[EDITOR] Shadows drawn again ({System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:0} ms)");
}
#endif

/// <summary>Debug switches for performance checks: BRUTALFPS_PROFILE="nomsaa,noprops,nobush,flat".</summary>
static class Profile
{
    private static readonly string Flags = Environment.GetEnvironmentVariable("BRUTALFPS_PROFILE") ?? "";
    public static bool Has(string flag) => Flags.Contains(flag, StringComparison.OrdinalIgnoreCase);
}
