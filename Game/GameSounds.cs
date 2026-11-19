using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The sounds of the world other than the guns themselves, and what people hear of them: footsteps (the player's, and
/// everyone else's, on what they're walking on), bullets striking (by surface), ricochets, a near miss cracking past the
/// player, a body hitting the floor, the player being hit.
/// <para>
/// Every clip is made in code (<see cref="SoundSynth"/>, a few takes of each so steps don't repeat), unless a WAV of
/// the same name is in Assets/Sounds (Footsteps/concrete_0.wav, Impacts/metal_1.wav, ...): then the recording is used.
/// Sounds in the world are placed in 3D (<see cref="AudioEngine.PlayAt"/>: fall-off, the room's reverb, muffled
/// behind walls); the player's own steps are played at the listener.
/// </para>
/// <para>
/// Noises: a sound people could pick up is also passed to <see cref="Npcs.HearNoise"/> with how far it carries (in
/// the open; through a wall much less). The player's steps carry 2.5 m crouched, 9 m walking, 17 m sprinting (more on
/// metal, less on dirt, snow or carpet), so creeping up on someone works and running past them doesn't. People's own
/// steps aren't noises to each other. Without an audio device the noises still happen: people hear, you don't.
/// </para>
/// </summary>
public sealed class GameSounds
{
    private const int Takes = 4;                 // variations of each footstep and impact
    private const float StepLength = 1.963f;     // m: one of the player's steps (the view bob's period: pi / 1.6)
    private const float NpcHearFootsteps = 35f;  // m: people's steps further than this from the listener aren't played

    /// <summary>How sounds in the world are made louder or quieter relative to the gunshots (1 = as made).</summary>
    public float FootstepGain = 0.6f, ImpactGain = 0.8f;

    private readonly AudioEngine? _audio;
    private readonly Npcs _npcs;
    private readonly Level _level;
    private readonly PhysicsWorld? _physics;
    private readonly SurfaceTable _surfaces;
    private readonly Random _random = new(1234);
    private readonly Dictionary<Npc, Walker> _walkers = new(ReferenceEqualityComparer.Instance);
    private int _playerStep = int.MinValue;
    private int _playerRespawns;

    private sealed class Walker
    {
        public float3 Last;
        public float Travelled;
        public float Speed;
        public bool Left;
        public bool Alive;
    }

    public GameSounds(AudioEngine? audio, Npcs npcs, Level level, PhysicsWorld? physics, SurfaceTable surfaces)
    {
        _audio = audio;
        _npcs = npcs;
        _level = level;
        _physics = physics;
        _surfaces = surfaces;
        if (audio is null)
            return;
        Register(audio);
        // Made now rather than at the first step: a few ms each.
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var names = Recipes.Keys.ToList();
        foreach (var name in names)
            audio.Get(name);
        Log.Write($"Sounds: {names.Count} made in code or loaded ({System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:0} ms)");
    }

    // ------------------------------------------------------------------------------------------------ the player

    /// <summary>Each frame: the player's footsteps (in time with the view bob) and landings.</summary>
    public void UpdatePlayer(PlayerController player)
    {
        if (player.Respawns != _playerRespawns)
        {
            _playerRespawns = player.Respawns;
            _playerStep = int.MinValue;
        }

        if (!player.Alive)
            return;
        var feet = player.Position;
        if (player.LandingSpeed > 3.5f)
        {
            var ground = GroundAt(feet);
            var hard = float.Clamp((player.LandingSpeed - 3.5f) / 8f, 0f, 1f);
            Play2D(Footstep(ground), FootstepGain * (0.7f + 0.5f * hard), Pitch(0.85f, 0.92f));
            Play2D("Body/land", 0.35f + 0.5f * hard, Pitch(0.95f, 1.05f));
            _npcs.HearNoise(feet, (7f + 10f * hard) * Carry(ground), byPlayer: true, loud: hard > 0.3f);
        }

        // A step at the bottom of each bob, while walking on the ground (a ladder's rungs aren't steps).
        var step = (int)float.Floor(player.StrideDistance / StepLength);
        if (step == _playerStep)
            return;
        var first = _playerStep == int.MinValue;
        _playerStep = step;
        if (first || !player.Grounded || player.Climbing >= 0)
            return;
        var surface = GroundAt(feet);
        var (gain, carries) = player.Prone ? (0.18f, 1.5f) : player.Crouching ? (0.3f, 2.5f) : player.Sprinting ? (1f, 17f) : (0.65f, 9f);
        // (A raid's kit: light, quieter; heavy, louder: overloaded, twice as far. Game/RaidLoadout.cs.)
        var noise = player.FootstepNoise;
        Play2D(Footstep(surface), FootstepGain * gain * float.Clamp(0.6f + 0.4f * noise, 0.8f, 1.4f),
            Pitch(0.94f, 1.06f) * (player.Crouching || player.Prone ? 0.92f : 1f) * (noise > 1.5f ? 0.9f : 1f));
        _npcs.HearNoise(feet, carries * noise * Carry(surface), byPlayer: true, loud: false);
    }

    /// <summary>The player has been shot.</summary>
    public void PlayerHurt(float damage) => Play2D("Body/hurt", float.Clamp(0.5f + damage / 40f, 0.5f, 1f), Pitch(0.9f, 1.05f));

    // ------------------------------------------------------------------------------------------------ people

    /// <summary>Each frame, after the people have moved: their footsteps near the listener, and bodies falling.</summary>
    public void UpdateNpcs(float dt, float3 listener)
    {
        var people = _npcs.People;
        foreach (var npc in people)
        {
            if (!_walkers.TryGetValue(npc, out var w))
            {
                _walkers[npc] = new Walker { Last = npc.Position, Alive = npc.Alive };
                continue;
            }

            var moved = npc.Position - w.Last;
            moved.Y = 0;
            var dist = float.Sqrt(float3.Dot(moved, moved));
            w.Last = npc.Position;
            if (w.Alive && !npc.Alive)
                BodyFall(npc.Position);
            w.Alive = npc.Alive;
            if (!npc.Alive || dt <= 0f || dist > 2f)
                continue;

            // One step every half stride: the walk's or the run's, by how fast they're going (eased, so a stumble
            // doesn't flip it).
            w.Speed += (dist / dt - w.Speed) * float.Min(1f, dt * 8f);
            var running = w.Speed > (_npcs.WalkSpeed + _npcs.RunSpeed) * 0.5f;
            var stepLength = (running ? _npcs.RunStride : Npcs.WalkStride) * 0.5f;
            w.Travelled += dist;
            if (w.Travelled < stepLength)
                continue;
            w.Travelled = float.Min(w.Travelled - stepLength, stepLength);
            w.Left = !w.Left;

            var to = npc.Position - listener;
            if (float3.Dot(to, to) > NpcHearFootsteps * NpcHearFootsteps)
                continue;
            // On the foot that's coming down: a little to that side.
            var side = new float3(float.Cos(npc.Heading), 0, -float.Sin(npc.Heading)) * (w.Left ? 0.12f : -0.12f);
            var foot = npc.Position + side + new float3(0, 0.05f, 0);
            var surface = GroundAt(npc.Position);
            _audio?.PlayAt(Clip(Footstep(surface)), foot, FootstepGain * (running ? 0.95f : 0.6f), Pitch(0.93f, 1.07f), referenceDistance: 1.5f);
        }

        // Respawned people are new objects: forget the old ones.
        if (_walkers.Count > people.Count)
        {
            var now = new HashSet<Npc>(people, ReferenceEqualityComparer.Instance);
            foreach (var gone in _walkers.Keys.Where(k => !now.Contains(k)).ToList())
                _walkers.Remove(gone);
        }
    }

    /// <summary>Someone going down: the thud of the body, a moment after the shot (they have to fall first).</summary>
    private void BodyFall(float3 at)
    {
        _audio?.PlayAt(Clip("Body/fall"), at + new float3(0, 0.2f, 0), 0.9f, Pitch(0.9f, 1.05f), delay: 0.45f + 0.2f * (float)_random.NextDouble(), referenceDistance: 2.5f);
        _npcs.HearNoise(at, 12f, byPlayer: false, loud: true);
    }

    // ------------------------------------------------------------------------------------------------ bullets

    // A hundred rounds striking in one frame would each take a voice (stealing it from the last) and cost an occlusion ray:
    // no more than ImpactsPerFrame strike sounds start in a frame, and none further off than ImpactRange (at that range one
    // is a twentieth as loud as close up, under the gunfire). What people hear of them (HearNoise) isn't limited.
    private const int ImpactsPerFrame = 10;
    private const float ImpactRange = 60f;
    private readonly FrameBudget _impacts = new(ImpactsPerFrame);

    private bool ImpactAudible(float3 at)
    {
        var d = at - Relevance.Eye;
        return float3.Dot(d, d) <= ImpactRange * ImpactRange && _impacts.Take();
    }

    /// <summary>A bullet striking <paramref name="surface"/> at <paramref name="at"/> (or something loose made of it).</summary>
    public void Impact(float3 at, SurfaceInfo surface, bool fromPlayer, Npc? firedBy = null)
    {
        var (name, gain) = surface.Kind switch
        {
            SurfaceKind.Concrete or SurfaceKind.Plastic or SurfaceKind.Rubber => ("concrete", 0.9f),
            SurfaceKind.Rock => ("rock", 0.9f),
            SurfaceKind.Metal or SurfaceKind.PaintedMetal => ("metal", 0.8f),
            SurfaceKind.SheetMetal => ("sheet", 0.85f),
            SurfaceKind.Wood => ("wood", 0.9f),
            SurfaceKind.Glass => ("glass", 0.8f),
            SurfaceKind.Dirt => ("dirt", 0.8f),
            SurfaceKind.Snow => ("snow", 0.6f),
            SurfaceKind.Fabric => ("soft", 0.7f),
            SurfaceKind.Water => ("water", 0.8f),
            _ => ("concrete", 0.9f),
        };
        if (ImpactAudible(at))
            _audio?.PlayAt(Clip(Take("Impacts/" + name)), at, ImpactGain * gain, Pitch(0.92f, 1.08f), referenceDistance: 3f);
        // A bullet smacking into something nearby: worth a look, and within a few metres it's as bad as the shot.
        _npcs.HearNoise(at, surface.Hard ? 16f : 10f, fromPlayer, loud: true, frightens: 4f, firedBy: firedBy);
    }

    /// <summary>A bullet glancing off <paramref name="surface"/>: the strike, and the whine as it flies on.</summary>
    public void Ricochet(float3 at, SurfaceInfo surface, bool fromPlayer, Npc? firedBy = null)
    {
        Impact(at, surface, fromPlayer, firedBy);
        if (ImpactAudible(at))
            _audio?.PlayAt(Clip(Take("Impacts/ricochet", 3)), at, ImpactGain * 0.75f, Pitch(0.85f, 1.15f), delay: 0.01f, referenceDistance: 4f);
        _npcs.HearNoise(at, 22f, fromPlayer, loud: true, frightens: 6f, firedBy: firedBy);
    }

    /// <summary>A bullet into someone.</summary>
    public void Flesh(float3 at) =>
        _audio?.PlayAt(Clip(Take("Impacts/flesh")), at, ImpactGain, Pitch(0.9f, 1.1f), referenceDistance: 2.5f);

    /// <summary>Someone else's bullet passing within a few metres of the player's head: the crack as it goes by.</summary>
    public void NearMiss(float3 at, float distance) =>
        _audio?.PlayAt(Clip(Take("Impacts/flyby", 3)), at, float.Clamp(1.1f - distance / 3f, 0.25f, 1f), Pitch(0.9f, 1.1f), referenceDistance: 2f);

    // ------------------------------------------------------------------------------------------------ flashbangs

    /// <summary>The pin pulled (G held to throw): the ring coming out.</summary>
    public void PinPulled() => Play2D("Weapons/pin", 0.6f, Pitch(0.95f, 1.05f));

    private readonly Dictionary<string, bool> _voiceMade = [];

    /// <summary>
    /// Someone calling out at <paramref name="at"/> (<see cref="NpcBarks"/>): played if there's a recording of that line
    /// (Assets/Sounds/Voice/...). Without one nothing is heard (the subtitle still shows). True: it was played.
    /// </summary>
    public bool Voice(string clip, float3 at)
    {
        if (_audio is null)
            return false;
        if (!_voiceMade.TryGetValue(clip, out var has))
            _voiceMade[clip] = has = _audio.HasRecording(clip);
        if (!has)
            return false;
        _audio.PlayAt(Clip(clip), at, 1f, Pitch(0.95f, 1.05f), referenceDistance: 6f);
        return true;
    }

    /// <summary>Thrown: the whoosh of the arm, and the spoon flying off as it leaves the hand.</summary>
    public void Thrown() => Play2D("Weapons/spoon", 0.5f, Pitch(0.95f, 1.05f));

    /// <summary>A flashbang hitting something, <paramref name="speed"/> m/s into it: a tinny clank (a thud on soft ground). People near hear it land.</summary>
    /// <param name="heard">People near take it for the player's doing (a flare someone threw themselves isn't).</param>
    public void GrenadeBounce(float3 at, float speed, in SurfaceInfo surface, bool heard = true)
    {
        var gain = float.Clamp(speed / 7f, 0.12f, 1f);
        var soft = surface.Kind is SurfaceKind.Dirt or SurfaceKind.Snow or SurfaceKind.Fabric or SurfaceKind.Water;
        _audio?.PlayAt(Clip(soft ? Take("Footsteps/soft") : Take("Impacts/grenade", 3)), at, ImpactGain * gain, Pitch(0.94f, 1.08f), referenceDistance: 2f);
        if (heard && speed > 1.5f)
            _npcs.HearNoise(at, (soft ? 4f : 8f) * (0.5f + 0.5f * gain), byPlayer: false, loud: true);   // (whoever threw it: anyone not yet alerted turns to look)
    }

    // ------------------------------------------------------------------------------------------------ flares

    /// <summary>A flare being struck in someone's hand: the scrape and the fizz as it catches.</summary>
    public void FlareStruck(float3 at) => _audio?.PlayAt(Clip("Weapons/flare_strike"), at, 0.7f, Pitch(0.95f, 1.05f), referenceDistance: 5f);

    /// <summary>A flare fired out of a launcher: a hollow pop.</summary>
    public void FlareFired(float3 at) => _audio?.PlayAt(Clip("Weapons/flare_pop"), at, 0.9f, Pitch(0.95f, 1.05f), referenceDistance: 9f);

    /// <summary>A take of a flare's hiss (they overlap, every couple of seconds, for as long as it burns). <paramref name="tag"/> stops it.</summary>
    public void FlareBurning(float3 at, string tag) => _audio?.PlayAt(Clip("Weapons/flare_hiss"), at, 0.55f, Pitch(0.92f, 1.08f), referenceDistance: 4f, tag: tag);

    /// <summary>A flare's gone out: its hiss stops.</summary>
    public void FlareOut(string tag) => _audio?.Cancel(tag);

    /// <summary>A flashbang going off: a deafening crack (people hear it through <see cref="Npcs.Flashbang"/>).</summary>
    public void Flashbang(float3 at) => _audio?.PlayAt(Clip("Weapons/flashbang"), at, 1f, Pitch(0.97f, 1.03f), referenceDistance: 14f);

    /// <summary>A fragmentation grenade going off: a deep, heavy crump (the flashbang's crack, pitched well down, twice over for weight).</summary>
    public void Explosion(float3 at)
    {
        _audio?.PlayAt(Clip("Weapons/flashbang"), at, 1f, Pitch(0.55f, 0.62f), referenceDistance: 18f);
        _audio?.PlayAt(Clip("Weapons/flashbang"), at, 0.8f, Pitch(0.38f, 0.44f), delay: 0.02f, referenceDistance: 22f);
    }

    /// <summary>A smoke grenade's fuse popping: a dull pop before it starts to pour out smoke.</summary>
    public void SmokePop(float3 at) => _audio?.PlayAt(Clip("Weapons/flare_pop"), at, 0.7f, Pitch(0.7f, 0.8f), referenceDistance: 5f);

    /// <summary>A take of a smoke grenade's hiss (overlapping, every couple of seconds, while it pours smoke). <paramref name="tag"/> stops it.</summary>
    public void SmokeHiss(float3 at, string tag) => _audio?.PlayAt(Clip("Weapons/flare_hiss"), at, 0.65f, Pitch(0.7f, 0.82f), referenceDistance: 4f, tag: tag);

    /// <summary>The player's ears ringing after a bang (as loud as they're deafened, and not muffled by it).</summary>
    public void Ringing(float gain)
    {
        _audio?.Cancel(AudioEngine.RingingTag);
        _audio?.Play(Clip("Body/ringing"), 0.25f + 0.55f * gain, Pitch(0.97f, 1.03f), delay: 0.05f, tag: AudioEngine.RingingTag);
    }

    // ------------------------------------------------------------------------------------------------ helpers

    /// <summary>What the ground under <paramref name="feet"/> is made of (concrete if there's nothing there).</summary>
    public SurfaceInfo GroundAt(float3 feet)
    {
        var from = feet + new float3(0, 0.3f, 0);
        if (_physics is not null)
        {
            if (_physics.Raycast(from, feet - new float3(0, 0.6f, 0), out var hit, includeBodies: false))
                return _surfaces[hit.UserIndex];
        }
        else if (_level.RaycastSurface(from, new float3(0, -1, 0), 0.9f, out _, out _, out var surface))
        {
            return surface;
        }

        return new SurfaceInfo(SurfaceKind.Concrete, new float3(0.5f), Material.Concrete);
    }

    /// <summary>How far a step on this carries, relative to concrete.</summary>
    private static float Carry(in SurfaceInfo s) => s.Kind switch
    {
        SurfaceKind.Metal or SurfaceKind.PaintedMetal or SurfaceKind.SheetMetal => 1.4f,
        SurfaceKind.Wood or SurfaceKind.Water => 1.15f,
        SurfaceKind.Dirt => 0.75f,
        SurfaceKind.Snow or SurfaceKind.Fabric or SurfaceKind.Rubber => 0.55f,
        _ => 1f,
    };

    private string Footstep(in SurfaceInfo s) => Take("Footsteps/" + s.Kind switch
    {
        SurfaceKind.Metal or SurfaceKind.PaintedMetal => "metal",
        SurfaceKind.SheetMetal => "sheet",
        SurfaceKind.Wood => "wood",
        SurfaceKind.Dirt => "dirt",
        SurfaceKind.Snow => "snow",
        SurfaceKind.Water => "water",
        SurfaceKind.Fabric or SurfaceKind.Rubber => "soft",
        _ => "concrete",
    });

    /// <summary>One of the takes of a sound, at random ("Footsteps/wood" -> "Footsteps/wood_2").</summary>
    private string Take(string name, int takes = Takes) => $"{name}_{_random.Next(takes)}";

    private float Pitch(float low, float high) => low + (high - low) * (float)_random.NextDouble();

    private SoundClip? Clip(string name) => _audio?.Get(name);

    private void Play2D(string name, float gain, float pitch) => _audio?.Play(Clip(name), gain, pitch);

    // ------------------------------------------------------------------------------------------------ recipes

    /// <summary>Every sound made in code, by name: how to make it from a seeded <see cref="SoundSynth"/>.</summary>
    private static readonly Dictionary<string, Func<int, WavFile>> Recipes = BuildRecipes();

    private static void Register(AudioEngine audio)
    {
        foreach (var (name, make) in Recipes)
        {
            var seed = StableSeed(name);
            audio.Provide(name, () => make(seed));
        }
    }

    private static int StableSeed(string s)
    {
        var h = 2166136261u;
        foreach (var c in s)
            h = (h ^ c) * 16777619u;
        return (int)(h & 0x7fffffff);
    }

    private static Dictionary<string, Func<int, WavFile>> BuildRecipes()
    {
        var r = new Dictionary<string, Func<int, WavFile>>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, int count, Func<int, WavFile> make)
        {
            for (var i = 0; i < count; i++)
                r[$"{name}_{i}"] = make;
        }

        // The factory raid: a van's diesel (a clip made to be played over and over), its back doors, the gate's chain, a scramble up a wall.
        r["Vehicles/van_engine"] = seed => new SoundSynth(2.2f, seed)
            .Tone(0, 44, 6f, 0.8f, attack: 0.04f).Tone(0, 88, 6f, 0.42f, attack: 0.04f).Tone(0, 133, 6f, 0.2f, vibratoHz: 11, vibratoDepth: 0.02f, attack: 0.04f)
            .Noise(0, 6f, 0.55f, 40, 260, attack: 0.04f).ToWav(0.75f);
        r["Vehicles/van_door"] = seed => new SoundSynth(0.5f, seed)
            .Noise(0, 0.03f, 0.9f, 60, 900).Tone(0, 130, 0.07f, 0.6f).Tone(0.01f, 420, 0.05f, 0.2f).Crackle(0.02f, 0.2f, 5, 0.15f, 1000, 5000).ToWav(0.85f);
        r["Vehicles/gate_chain"] = seed => new SoundSynth(2.2f, seed)
            .Tinkle(0, 1.6f, 34, 0.25f, 900, 2600, 0.05f).Noise(0, 0.8f, 0.2f, 200, 1500, attack: 0.1f).Tone(0.1f, 240, 0.6f, 0.18f, endFrequency: 190).ToWav(0.8f);
        // The Antwerp raid: a pair of outboards (played over and over, like the van's diesel), a rope running through a
        // descender, a karabiner snapping shut.
        r["Vehicles/boat_engine"] = seed => new SoundSynth(2.2f, seed)
            .Tone(0, 96, 6f, 0.55f, attack: 0.04f).Tone(0, 192, 6f, 0.4f, vibratoHz: 17, vibratoDepth: 0.03f, attack: 0.04f)
            .Tone(0, 290, 6f, 0.16f, vibratoHz: 9, vibratoDepth: 0.02f, attack: 0.04f)
            .Noise(0, 6f, 0.5f, 300, 2400, attack: 0.04f).Noise(0, 6f, 0.3f, 60, 400, attack: 0.04f).ToWav(0.7f);
        r["Body/rope_zip"] = seed => new SoundSynth(0.7f, seed)
            .Noise(0, 0.5f, 0.6f, 1200, 6000, attack: 0.02f).Tone(0, 900, 0.45f, 0.06f, endFrequency: 700).ToWav(0.6f);
        r["Body/rope_clip"] = seed => new SoundSynth(0.3f, seed)
            .Tone(0, 2900, 0.05f, 0.4f).Tone(0.004f, 4300, 0.04f, 0.25f).Crackle(0, 0.03f, 4, 0.3f, 3000, 9000).ToWav(0.7f);
        r["Body/scrape"] = seed => new SoundSynth(0.7f, seed)
            .Noise(0, 0.2f, 0.7f, 700, 5000, attack: 0.03f).Crackle(0, 0.4f, 10, 0.2f, 2000, 7000).ToWav(0.7f);

        // Footsteps: the heel coming down, then the ball of the foot (a scuff) a few tens of ms later.
        Add("Footsteps/concrete", Takes, seed =>
        {
            var s = new SoundSynth(0.25f, seed);
            var toe = s.Vary(0.05f, 0.3f);
            return s.Noise(0, 0.012f, 0.9f, 300, s.Vary(4000, 0.2f)).Tone(0, s.Vary(105, 0.15f), 0.018f, 0.5f)
                .Noise(toe, 0.018f, 0.45f, 1500, 7500).Crackle(0.005f, 0.09f, 7, 0.18f, 2500, 9000).ToWav(0.8f);
        });
        Add("Footsteps/metal", Takes, seed =>
        {
            var s = new SoundSynth(0.5f, seed);
            var toe = s.Vary(0.05f, 0.3f);
            return s.Noise(0, 0.01f, 0.8f, 400, 6000).Tone(0, s.Vary(140, 0.1f), 0.06f, 0.45f)
                .Tone(0, s.Vary(410, 0.1f), 0.12f, 0.22f).Tone(0, s.Vary(1130, 0.1f), 0.09f, 0.14f).Tone(0, s.Vary(2380, 0.1f), 0.06f, 0.08f)
                .Noise(toe, 0.015f, 0.4f, 1500, 8000).Tone(toe, s.Vary(1650, 0.1f), 0.05f, 0.08f).ToWav(0.8f);
        });
        Add("Footsteps/sheet", Takes, seed =>
        {
            var s = new SoundSynth(0.6f, seed);
            return s.Noise(0, 0.012f, 0.7f, 200, 5000).Tone(0, s.Vary(95, 0.1f), 0.1f, 0.5f).Tone(0, s.Vary(230, 0.1f), 0.15f, 0.3f, vibratoHz: 7, vibratoDepth: 0.02f)
                .Tone(0, s.Vary(560, 0.1f), 0.12f, 0.15f).Crackle(0.01f, 0.2f, 14, 0.12f, 1500, 6000, 0.004f)
                .Noise(s.Vary(0.05f, 0.3f), 0.02f, 0.35f, 800, 6000).ToWav(0.8f);
        });
        Add("Footsteps/wood", Takes, seed =>
        {
            var s = new SoundSynth(0.3f, seed);
            var toe = s.Vary(0.055f, 0.3f);
            return s.Noise(0, 0.014f, 0.6f, 150, 2500).Tone(0, s.Vary(165, 0.12f), 0.05f, 0.55f).Tone(0, s.Vary(340, 0.12f), 0.035f, 0.25f)
                .Noise(toe, 0.02f, 0.3f, 400, 3500).Tone(toe, s.Vary(220, 0.1f), 0.03f, 0.2f).ToWav(0.8f);
        });
        Add("Footsteps/dirt", Takes, seed =>
        {
            var s = new SoundSynth(0.3f, seed);
            return s.Noise(0, 0.028f, 0.6f, 80, 1800).Tone(0, s.Vary(80, 0.15f), 0.03f, 0.35f)
                .Crackle(0, 0.14f, 28, 0.25f, 1500, 6500, 0.003f).Noise(s.Vary(0.06f, 0.3f), 0.03f, 0.25f, 300, 3000).ToWav(0.75f);
        });
        Add("Footsteps/snow", Takes, seed =>
        {
            var s = new SoundSynth(0.35f, seed);
            return s.Noise(0, 0.05f, 0.35f, 300, 2500, attack: 0.02f).Crackle(0, 0.18f, 70, 0.35f, 1200, 7000, 0.004f).ToWav(0.7f);
        });
        Add("Footsteps/water", Takes, seed =>
        {
            var s = new SoundSynth(0.5f, seed);
            s.Noise(0, 0.07f, 0.6f, 200, 3500, attack: 0.006f).Noise(0.08f, 0.1f, 0.25f, 900, 5500);
            for (var i = 0; i < 4; i++)
                s.Tone(0.02f + 0.12f * s.Random01(), s.Vary(500, 0.3f), 0.025f, 0.15f, endFrequency: s.Vary(1100, 0.2f));
            return s.ToWav(0.75f);
        });
        Add("Footsteps/soft", Takes, seed =>
        {
            var s = new SoundSynth(0.2f, seed);
            return s.Noise(0, 0.03f, 0.6f, 60, 700).Tone(0, s.Vary(90, 0.15f), 0.035f, 0.45f).Noise(s.Vary(0.05f, 0.3f), 0.025f, 0.2f, 200, 1500).ToWav(0.65f);
        });

        // Bullets striking: a sharp crack, then what the surface does (chips falling, a ring, splinters, shards).
        Add("Impacts/concrete", Takes, seed =>
        {
            var s = new SoundSynth(0.45f, seed);
            return s.Noise(0, 0.004f, 1f, 900, 12000).Noise(0, 0.025f, 0.5f, 300, 5000).Tone(0, s.Vary(190, 0.2f), 0.02f, 0.3f)
                .Crackle(0.006f, 0.28f, 36, 0.3f, 2000, 9000, 0.003f).ToWav();
        });
        Add("Impacts/rock", Takes, seed =>
        {
            var s = new SoundSynth(0.45f, seed);
            return s.Noise(0, 0.004f, 1f, 700, 10000).Noise(0, 0.03f, 0.55f, 200, 4000).Tone(0, s.Vary(150, 0.2f), 0.025f, 0.35f)
                .Crackle(0.006f, 0.3f, 30, 0.3f, 1500, 7000, 0.004f).ToWav();
        });
        Add("Impacts/metal", Takes, seed =>
        {
            var s = new SoundSynth(1.6f, seed);
            return s.Noise(0, 0.003f, 0.9f, 2000, 14000).Tone(0, s.Vary(1870, 0.15f), 0.22f, 0.35f).Tone(0, s.Vary(3150, 0.15f), 0.16f, 0.25f)
                .Tone(0, s.Vary(4720, 0.15f), 0.11f, 0.18f).Tone(0, s.Vary(6200, 0.1f), 0.07f, 0.1f).Tone(0, s.Vary(760, 0.2f), 0.08f, 0.2f).ToWav();
        });
        Add("Impacts/sheet", Takes, seed =>
        {
            var s = new SoundSynth(1.6f, seed);
            return s.Noise(0, 0.004f, 0.9f, 1000, 12000).Tone(0, s.Vary(320, 0.15f), 0.3f, 0.35f, vibratoHz: 9, vibratoDepth: 0.015f)
                .Tone(0, s.Vary(760, 0.15f), 0.22f, 0.25f).Tone(0, s.Vary(1430, 0.15f), 0.18f, 0.18f).Tone(0, s.Vary(2380, 0.1f), 0.12f, 0.1f)
                .Crackle(0.01f, 0.25f, 16, 0.12f, 1500, 6000, 0.004f).ToWav();
        });
        Add("Impacts/wood", Takes, seed =>
        {
            var s = new SoundSynth(0.4f, seed);
            return s.Noise(0, 0.004f, 0.8f, 800, 8000).Tone(0, s.Vary(240, 0.15f), 0.045f, 0.6f).Tone(0, s.Vary(520, 0.15f), 0.03f, 0.3f)
                .Crackle(0.004f, 0.09f, 12, 0.2f, 2500, 9000, 0.002f).ToWav();
        });
        Add("Impacts/glass", Takes, seed =>
        {
            var s = new SoundSynth(0.8f, seed);
            return s.Noise(0, 0.006f, 0.9f, 2000, 14000).Tinkle(0, 0.4f, 40, 0.15f, 2500, 9000, 0.05f)
                .Crackle(0, 0.45f, 80, 0.18f, 4000, 13000, 0.002f).ToWav();
        });
        Add("Impacts/dirt", Takes, seed =>
        {
            var s = new SoundSynth(0.4f, seed);
            return s.Noise(0, 0.035f, 0.8f, 60, 2500).Tone(0, s.Vary(70, 0.15f), 0.045f, 0.4f)
                .Crackle(0, 0.16f, 30, 0.2f, 1000, 5000, 0.004f).ToWav();
        });
        Add("Impacts/snow", Takes, seed =>
        {
            var s = new SoundSynth(0.35f, seed);
            return s.Noise(0, 0.045f, 0.6f, 200, 3000).Crackle(0, 0.15f, 40, 0.25f, 1200, 6000, 0.003f).ToWav();
        });
        Add("Impacts/water", Takes, seed =>
        {
            var s = new SoundSynth(0.9f, seed);
            s.Tone(0, s.Vary(600, 0.2f), 0.05f, 0.4f, endFrequency: s.Vary(1400, 0.2f)).Noise(0, 0.09f, 0.6f, 300, 6000, attack: 0.003f);
            for (var i = 0; i < 6; i++)
                s.Tone(0.08f + 0.4f * s.Random01(), s.Vary(900, 0.4f), 0.02f, 0.12f, endFrequency: s.Vary(1800, 0.3f));
            return s.ToWav();
        });
        Add("Impacts/soft", Takes, seed =>
        {
            // Carpet, cloth, cushions: a dull thump and a puff of fibres.
            var s = new SoundSynth(0.3f, seed);
            return s.Noise(0, 0.004f, 0.5f, 600, 5000).Noise(0, 0.025f, 0.7f, 70, 1100).Tone(0, s.Vary(120, 0.15f), 0.03f, 0.45f)
                .Noise(0.003f, 0.04f, 0.15f, 1500, 6000, attack: 0.008f).ToWav();
        });
        Add("Impacts/flesh", Takes, seed =>
        {
            var s = new SoundSynth(0.3f, seed);
            return s.Noise(0, 0.03f, 0.8f, 80, 900).Tone(0, s.Vary(95, 0.15f), 0.045f, 0.6f).Noise(0.004f, 0.045f, 0.25f, 600, 2500).ToWav();
        });
        Add("Impacts/ricochet", 3, seed =>
        {
            var s = new SoundSynth(0.9f, seed);
            var from = s.Vary(3800, 0.2f);
            return s.Noise(0, 0.003f, 0.6f, 2000, 12000)
                .Tone(0.004f, from, 0.11f, 0.55f, endFrequency: from * s.Vary(0.35f, 0.2f), vibratoHz: s.Vary(28, 0.3f), vibratoDepth: 0.035f, attack: 0.01f).ToWav();
        });
        Add("Impacts/flyby", 3, seed =>
        {
            // The supersonic crack (a sharp N-wave), then the rush of air after it.
            var s = new SoundSynth(0.3f, seed);
            return s.Noise(0, 0.0015f, 1f, 2500, 16000).Noise(0.002f, 0.035f, 0.35f, 1500, s.Vary(6000, 0.2f), attack: 0.015f).ToWav();
        });

        // Flashbangs: the pin, the spoon, the can clanking as it lands, the bang, and the ringing in your ears after.
        r["Weapons/pin"] = seed =>
        {
            var s = new SoundSynth(0.35f, seed);
            return s.Noise(0, 0.006f, 0.5f, 2500, 11000).Tone(0, 2900, 0.03f, 0.25f).Noise(0.05f, 0.01f, 0.35f, 3000, 12000)
                .Tone(0.05f, 4300, 0.05f, 0.2f).Tone(0.05f, 6100, 0.03f, 0.1f).ToWav(0.6f);
        };
        r["Weapons/spoon"] = seed =>
        {
            var s = new SoundSynth(0.6f, seed);
            return s.Noise(0, 0.08f, 0.35f, 300, 2500, attack: 0.05f).Noise(0.12f, 0.004f, 0.4f, 2500, 12000)
                .Tone(0.12f, 5200, 0.09f, 0.22f, vibratoHz: 30, vibratoDepth: 0.01f).Tone(0.12f, 7900, 0.06f, 0.12f).ToWav(0.55f);
        };
        Add("Impacts/grenade", 3, seed =>
        {
            var s = new SoundSynth(0.5f, seed);
            return s.Noise(0, 0.004f, 0.7f, 1200, 10000).Tone(0, s.Vary(980, 0.12f), 0.035f, 0.35f).Tone(0, s.Vary(2650, 0.12f), 0.05f, 0.3f)
                .Tone(0, s.Vary(4300, 0.1f), 0.035f, 0.18f).Tone(0, s.Vary(160, 0.15f), 0.02f, 0.3f)
                .Noise(s.Vary(0.07f, 0.3f), 0.003f, 0.25f, 1500, 9000).Tone(s.Vary(0.07f, 0.3f), s.Vary(2650, 0.1f), 0.03f, 0.1f).ToWav(0.8f);
        });
        r["Weapons/flashbang"] = seed =>
        {
            // A crack with everything in it, the boom of the air behind it, a low thump, and bits pattering down.
            var s = new SoundSynth(2.2f, seed);
            return s.Noise(0, 0.005f, 1f, 150, 18000).Noise(0, 0.06f, 0.85f, 40, 4000).Tone(0, 62, 0.16f, 0.9f, endFrequency: 34)
                .Tone(0, 140, 0.06f, 0.4f, endFrequency: 70).Noise(0.01f, 0.35f, 0.3f, 60, 900, attack: 0.02f)
                .Crackle(0.02f, 0.6f, 50, 0.12f, 1500, 9000, 0.004f).Noise(0.05f, 0.5f, 0.12f, 1500, 7000, attack: 0.05f).ToWav(1f);
        };
        r["Body/ringing"] = seed =>
        {
            // Tinnitus: a high whine (two tones beating slowly) and a hiss, dying away over several seconds.
            var s = new SoundSynth(7f, seed);
            return s.Tone(0, 3700, 1.6f, 0.6f, vibratoHz: 0.7f, vibratoDepth: 0.004f, attack: 0.25f).Tone(0, 3712, 1.4f, 0.35f, attack: 0.25f)
                .Tone(0, 7400, 0.8f, 0.08f, attack: 0.25f).Noise(0, 1.1f, 0.06f, 3000, 9000, attack: 0.3f).ToWav(0.5f);
        };

        // Flares: struck, fired and burning.
        r["Weapons/flare_strike"] = seed =>
        {
            var s = new SoundSynth(0.9f, seed);
            return s.Noise(0, 0.05f, 0.5f, 1800, 9000, attack: 0.02f).Noise(0.09f, 0.22f, 0.8f, 900, 11000, attack: 0.04f)
                .Crackle(0.09f, 0.6f, 24, 0.2f, 2500, 9000, 0.003f).ToWav(0.7f);
        };
        r["Weapons/flare_pop"] = seed =>
        {
            var s = new SoundSynth(0.9f, seed);
            return s.Noise(0, 0.012f, 0.9f, 200, 7000).Tone(0, 170, 0.07f, 0.8f, endFrequency: 85).Noise(0.015f, 0.12f, 0.3f, 300, 2500, attack: 0.02f)
                .Noise(0.04f, 0.09f, 0.22f, 1200, 8000, attack: 0.04f).ToWav(0.9f);
        };
        r["Weapons/flare_hiss"] = seed =>
        {
            // A steady fierce hiss with spits and crackles in it. It rises and falls so that takes overlap evenly.
            var s = new SoundSynth(3.4f, seed);
            return s.Noise(0, 0.28f, 0.42f, 1500, 11000, attack: 0.7f).Noise(0, 0.28f, 0.2f, 300, 1800, attack: 0.7f)
                .Crackle(0.2f, 2.8f, 45, 0.14f, 2000, 9000, 0.003f).ToWav(0.6f);
        };

        // Bodies.
        r["Body/fall"] = seed =>
        {
            var s = new SoundSynth(0.6f, seed);
            return s.Tone(0, 62, 0.07f, 0.8f).Noise(0, 0.05f, 0.6f, 50, 1200).Tone(0.13f, 78, 0.05f, 0.4f).Noise(0.13f, 0.04f, 0.4f, 80, 1500)
                .Crackle(0.1f, 0.22f, 12, 0.12f, 1500, 6000, 0.003f).ToWav(0.85f);
        };
        r["Body/land"] = seed =>
        {
            var s = new SoundSynth(0.35f, seed);
            return s.Tone(0, 58, 0.06f, 0.8f).Noise(0, 0.04f, 0.5f, 40, 900).ToWav(0.8f);
        };
        r["Body/hurt"] = seed =>
        {
            var s = new SoundSynth(0.35f, seed);
            return s.Tone(0, 70, 0.08f, 0.8f).Noise(0, 0.05f, 0.6f, 60, 700).Noise(0.003f, 0.04f, 0.2f, 500, 2500).ToWav(0.9f);
        };
        return r;
    }
}
