using Brutal.Numerics;
using Brutal.OpenAlApi;

namespace BrutalFps.Engine;

/// <summary>
/// A sound loaded into OpenAL: the clip as recorded (played as-is for 2D sounds such as the player's own gun) and a
/// mono mix of it (for sounds placed in the world, which OpenAL only positions when they're mono).
/// </summary>
public sealed class SoundClip
{
    public required string Name { get; init; }
    public AlBuffer Stereo { get; init; }
    public AlBuffer Mono { get; init; }
    public float Length { get; init; }
    public int Channels { get; init; }
}

/// <summary>
/// A space with its own acoustics: sounds made inside <paramref name="Bounds"/> ring with <paramref name="Reverb"/>
/// (one of OpenAL Soft's presets, see <see cref="ReverbPreset"/>, or hand-made). Rooms may sit inside one another: a
/// point belongs to the smallest room that contains it (a shed inside the outback, the container inside the yard).
/// <paramref name="Wet"/> scales how loud the reverb is (1 = the preset as is).
/// </summary>
public readonly record struct ReverbRoom(string Name, Aabb Bounds, ReverbProperties Reverb, float Wet = 1f);

/// <summary>
/// An opening between two rooms (a container's doors, a shed door): inside <paramref name="Bounds"/> the acoustics
/// cross-fade along <paramref name="Axis"/> (0 = x, 1 = y, 2 = z) from room <paramref name="MinSide"/> (at the
/// box's Min face) to room <paramref name="MaxSide"/> (at its Max face), 50/50 in the middle. So walking out of a
/// door, or firing in it, sounds like both spaces instead of flipping from one to the other at a line. Make the box
/// only as wide as the opening: next to a wall nothing should leak through.
/// </summary>
public readonly record struct ReverbDoorway(string Name, Aabb Bounds, int Axis, string MinSide, string MaxSide);

/// <summary>
/// The game's audio, on OpenAL Soft (Ahwoo.BRUTAL.OpenAl): opens the default output device, loads WAV files from
/// Assets/Sounds (8/16/24/32-bit PCM or 32-bit float, mono or stereo, any sample rate; converted to 16-bit), and plays
/// them on a pool of voices, either "2D" (head-relative, as recorded) or placed in the world (mono, with distance
/// fall-off and air absorption, following the listener = the camera). Sounds can be started after a delay and from
/// an offset, so they can be lined up with animation. Without an audio device the game carries on silently
/// (TryCreate returns null).
/// <para>
/// Environment (OpenAL EFX): each <see cref="ReverbRoom"/> has its own reverb on an auxiliary effect slot. A sound
/// sends to the reverb of the room it's made in (2D sounds: the listener's room), and when that isn't the listener's
/// room, a little to the listener's room's reverb too. Since every room keeps its own slot, a tail rings out where
/// it started when you walk out into another room. Sounds in the world are muffled (a low-pass on the dry signal)
/// while <see cref="Occlusion"/> says something solid is between them and the listener. Without EFX the sounds just
/// play dry.
/// </para>
/// <para>
/// Doorways (<see cref="ReverbDoorway"/>) blend two rooms near an opening: a sound made there (or, for 2D sounds,
/// with the listener there) sends to both rooms' reverbs, weighted by how far through the opening it is
/// (equal-power, so the total reverb stays as loud). The blend is fixed when the sound starts, like the room was.
/// </para>
/// <para>BRUTALFPS_VOLUME=0.5 sets the master volume; BRUTALFPS_MUTE=1 silences everything; BRUTALFPS_REVERB=0 turns
/// the reverb off (to compare).</para>
/// </summary>
public sealed class AudioEngine : IDisposable
{
    public const int Voices = 64;

    // Occlusion: how a sound behind a wall is heard (dry level and treble left at full occlusion), how fast it eases
    // in and out, how often each playing sound is re-checked, and how much of the path must be blocked to count.
    private const float OccludedGain = 0.45f;
    private const float OccludedGainHf = 0.10f;
    private const float OcclusionRate = 10f;        // 1/s
    private const float OcclusionCheckEvery = 0.08f; // s
    private const float OcclusionSlack = 0.35f;     // m: a hit closer than this to the sound doesn't count (the surface it's on)
    // A sound made in another room feeds this much of it into the listener's room's reverb, with the treble down.
    private const float BleedGain = 0.3f;
    private const float BleedGainHf = 0.7f;
    // Auxiliary sends asked for per source: two for the sound's own rooms (one, or two in a doorway) and two for the
    // bleed into the listener's (likewise). With fewer, the strongest ones are kept.
    private const int WantedSends = 4;

    private struct Voice
    {
        public AlSource Source;
        public double Started;
        public string? Tag;
        public bool World;          // placed in the world (else 2D)
        public float3 Position;
        public int Room;            // the main room it's sending to, -1 none
        public float Occlusion;     // 0 = clear, 1 = fully behind something
        public float OcclusionTarget;
        public double NextCheck;
        public LowpassFilter Direct;
        public LowpassFilter[] Sends;   // one per auxiliary send: its level and treble
        public string? RouteText;   // for the debug log
    }

    /// <summary>Where a point is acoustically: one room (B = -1), or between two in a doorway (A + B weights = 1).</summary>
    private readonly record struct Blend(int A, float WeightA, int B, float WeightB)
    {
        public static readonly Blend None = new(-1, 0f, -1, 0f);
        public int Main => B >= 0 && WeightB > WeightA ? B : A;
        public bool Has(int room) => room >= 0 && ((room == A && WeightA > 0f) || (room == B && WeightB > 0f));
    }

    private sealed class Doorway
    {
        public required ReverbDoorway Def;
        public int MinRoom, MaxRoom;
    }

    private struct Pending
    {
        public SoundClip Clip;
        public double At;
        public float Gain, Pitch, Offset, ReferenceDistance;
        public float3? Position;
        public string? Tag;
    }

    private sealed class Room
    {
        public required ReverbRoom Def;
        public float Volume;
        public AlAuxiliaryEffectSlot Slot;
        public AlEffect Effect;
    }

    private readonly Device _device;
    private readonly Context _context;
    private readonly string _folder;
    private readonly Voice[] _voices = new Voice[Voices];
    private readonly List<Pending> _pending = [];
    private readonly Dictionary<string, SoundClip?> _clips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<WavFile>> _made = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Room> _rooms = [];
    private readonly List<Doorway> _doorways = [];
    private readonly Efx? _fx;
    private readonly bool _efx;
    private readonly int _sends;
    private float3 _listener;
    private Blend _listenerBlend = Blend.None;
    private int _listenerRoom = -1;
    private double _time;
    private float _deafness, _appliedDeafness;
    private bool _disposed;

    private AudioEngine(Device device, Context context, string folder)
    {
        _device = device;
        _context = context;
        _folder = folder;
        Al.DistanceModel = AlDistanceModel.InverseDistanceClamped;
        Al.SpeedOfSound = 343f;
        for (var i = 0; i < Voices; i++)
            _voices[i] = new Voice { Source = Al.GenSource(), Room = -1 };
        var volume = Environment.GetEnvironmentVariable("BRUTALFPS_VOLUME");
        Volume = Environment.GetEnvironmentVariable("BRUTALFPS_MUTE") == "1" ? 0f
            : float.TryParse(volume, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? float.Clamp(v, 0f, 4f)
            : 1f;

        // EFX: per voice a low-pass filter on the dry signal (occlusion) and one on each auxiliary send (its level).
        if (Environment.GetEnvironmentVariable("BRUTALFPS_REVERB") == "0")
            return;
        try
        {
            _fx = device.Efx;
            _sends = int.Min(_fx.MaxAuxiliarySends, WantedSends);
            for (var i = 0; i < Voices; i++)
            {
                _voices[i].Direct = _fx.CreateLowpass();
                _voices[i].Direct.Gain = 1f;
                _voices[i].Direct.GainHf = 1f;
                _voices[i].Sends = new LowpassFilter[_sends];
                for (var send = 0; send < _sends; send++)
                    _voices[i].Sends[send] = _fx.CreateLowpass();
            }

            _efx = _sends > 0 && Al.GetError() == AlError.NoError;
        }
        catch (Exception e)
        {
            Log.Write($"[WARN] No reverb or occlusion: EFX unavailable ({e.GetType().Name}: {e.Message}).");
            _efx = false;
        }
    }

    /// <summary>Opens the default audio device, or returns null (with a [WARN]) if there isn't one or OpenAL won't load.</summary>
    public static AudioEngine? TryCreate(string soundFolder)
    {
        try
        {
            Device? device = null;
            try
            {
                device = Alc.OpenDevice(null!);
            }
            catch (Exception) when (Alc.GetDefaultAllDevicesSpecifier() is { Length: > 0 } name)
            {
                device = Alc.OpenDevice(name);
            }

            if (device is null || device.IsNull())
            {
                Log.Write("[WARN] No audio: couldn't open an audio device.");
                return null;
            }

            // Auxiliary sends per source: the reverb of the sound's own room(s), and the bleed into the listener's.
            var context = device.CreateContext(new Context.CreateInfo { Attributes = [(AlcContextAttribute.MaxAuxiliarySends, WantedSends)] });
            context.MakeCurrent();
            var engine = new AudioEngine(device, context, soundFolder);
            Log.Write($"Audio: {device.Specifier} (OpenAL {device.MajorVersion}.{device.MinorVersion}, {Al.GetString(AlGetString.Renderer)}), " +
                      $"{Voices} voices, volume {engine.Volume:0.##}, HRTF {device.HrtfStatus}, " +
                      (engine._efx ? $"EFX {engine._fx!.EfxMajorVersion}.{engine._fx.EfxMinorVersion} with {engine._sends} sends" : "no reverb"));
            return engine;
        }
        catch (Exception e)
        {
            Log.Write($"[WARN] No audio ({e.GetType().Name}: {e.Message}).");
            return null;
        }
    }

    /// <summary>Master volume (the listener's gain): 1 = as recorded.</summary>
    public float Volume
    {
        get => Al.Listener.Gain;
        set => Al.Listener.Gain = value;
    }

    /// <summary>
    /// How deafened the listener is (0 = not at all, 1 = ears ringing from a bang next to them): every sound is quieter
    /// and loses its treble, except those played with the tag <see cref="RingingTag"/> (the ringing itself). Needs EFX.
    /// </summary>
    public float Deafness
    {
        get => _deafness;
        set => _deafness = float.Clamp(value, 0f, 1f);
    }

    /// <summary>The tag of sounds <see cref="Deafness"/> doesn't muffle.</summary>
    public const string RingingTag = "ringing";

    /// <summary>True when reverb rooms and occlusion are working (EFX is there and not turned off).</summary>
    public bool Environmental => _efx;

    /// <summary>
    /// How much of the straight line from the first point to the second is clear (1 = nothing in the way, 0.3 = blocked
    /// 30% of the way along): the listener to each sound in the world. Null = nothing is ever muffled.
    /// </summary>
    public Func<float3, float3, float>? Occlusion { get; set; }

    /// <summary>The name of the room the listener is in (null outside every room or without EFX).</summary>
    public string? ListenerRoom => _listenerRoom >= 0 ? _rooms[_listenerRoom].Def.Name : null;

    /// <summary>Replaces the reverb rooms (one effect slot each) and the doorways that blend them.</summary>
    public void SetRooms(IEnumerable<ReverbRoom> rooms, IEnumerable<ReverbDoorway>? doorways = null)
    {
        if (!_efx || _fx is null)
            return;
        ClearRooms();
        foreach (var def in rooms)
        {
            try
            {
                var props = def.Reverb;
                AlEffect effect;
                try
                {
                    var eax = _fx.CreateEaxReverb();
                    eax.Set(in props);
                    effect = eax;
                }
                catch (Exception)
                {
                    // Only the standard reverb (no echo or modulation); EAX reverb is always there in OpenAL Soft.
                    var std = _fx.CreateReverb();
                    std.Set(in props);
                    effect = std;
                }

                var slot = _fx.CreateAuxiliaryEffectSlot();
                slot.Load(effect);
                slot.Gain = float.Clamp(def.Wet, 0f, 1f);
                slot.SendAuto = true;   // the send fades with distance, as the room's reverb would
                var error = Al.GetError();
                if (error != AlError.NoError)
                    throw new InvalidOperationException($"AL {error}");
                var size = def.Bounds.Size;
                _rooms.Add(new Room { Def = def, Volume = size.X * size.Y * size.Z, Slot = slot, Effect = effect });
            }
            catch (Exception e)
            {
                Log.Write($"[WARN] Reverb room '{def.Name}' not set up ({e.GetType().Name}: {e.Message}).");
            }
        }

        foreach (var def in doorways ?? [])
        {
            var min = _rooms.FindIndex(r => r.Def.Name == def.MinSide);
            var max = _rooms.FindIndex(r => r.Def.Name == def.MaxSide);
            if (min < 0 || max < 0 || min == max || def.Axis is < 0 or > 2 || def.Bounds.Size[def.Axis] <= 0f)
            {
                Log.Write($"[WARN] Doorway '{def.Name}' skipped: needs two different rooms that exist ('{def.MinSide}', '{def.MaxSide}') and some depth along axis {def.Axis}.");
                continue;
            }

            _doorways.Add(new Doorway { Def = def, MinRoom = min, MaxRoom = max });
        }

        _listenerBlend = BlendAt(_listener);
        _listenerRoom = _listenerBlend.Main;
        Log.Write($"Audio: {_rooms.Count} reverb rooms ({string.Join(", ", _rooms.Select(r => r.Def.Name))}), " +
                  $"{_doorways.Count} doorways{(_doorways.Count > 0 ? $" ({string.Join(", ", _doorways.Select(d => d.Def.Name))})" : "")}, {_sends} sends per sound");
    }

    /// <summary>
    /// The acoustics at <paramref name="p"/>: in a doorway, its two rooms cross-faded (smoothly, 50/50 halfway
    /// through); anywhere else the smallest room containing it.
    /// </summary>
    private Blend BlendAt(float3 p)
    {
        foreach (var d in _doorways)
        {
            var b = d.Def.Bounds;
            if (p.X < b.Min.X || p.Y < b.Min.Y || p.Z < b.Min.Z || p.X > b.Max.X || p.Y > b.Max.Y || p.Z > b.Max.Z)
                continue;
            var axis = d.Def.Axis;
            var t = float.Clamp((p[axis] - b.Min[axis]) / (b.Max[axis] - b.Min[axis]), 0f, 1f);
            var toMax = t * t * (3f - 2f * t);
            return new Blend(d.MinRoom, 1f - toMax, d.MaxRoom, toMax);
        }

        var room = RoomAt(p);
        return new Blend(room, room >= 0 ? 1f : 0f, -1, 0f);
    }

    private string Describe(in Blend blend) =>
        blend.A < 0 ? "-"
        : blend.B < 0 || blend.WeightB <= 0f ? _rooms[blend.A].Def.Name
        : blend.WeightA <= 0f ? _rooms[blend.B].Def.Name
        : $"{_rooms[blend.A].Def.Name} {blend.WeightA * 100:0}% / {_rooms[blend.B].Def.Name} {blend.WeightB * 100:0}%";

    /// <summary>The smallest room containing <paramref name="p"/>, or -1.</summary>
    private int RoomAt(float3 p)
    {
        var best = -1;
        var smallest = float.MaxValue;
        for (var i = 0; i < _rooms.Count; i++)
        {
            var b = _rooms[i].Def.Bounds;
            if (p.X < b.Min.X || p.Y < b.Min.Y || p.Z < b.Min.Z || p.X > b.Max.X || p.Y > b.Max.Y || p.Z > b.Max.Z)
                continue;
            if (_rooms[i].Volume < smallest)
            {
                smallest = _rooms[i].Volume;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Says how to make the clip <paramref name="name"/> in code (<see cref="SoundSynth"/>) if there's no WAV file for it:
    /// a recording dropped into Assets/Sounds under that name takes its place.
    /// </summary>
    public void Provide(string name, Func<WavFile> make) => _made[name] = make;

    /// <summary>Whether there's a recording for that name under Assets/Sounds (for sounds that are only played if someone has made one).</summary>
    public bool HasRecording(string name) => _clips.TryGetValue(name, out var clip) ? clip is not null
        : File.Exists(Path.Combine(_folder, name.Replace('/', Path.DirectorySeparatorChar) + ".wav"));

    /// <summary>
    /// A clip by its path under Assets/Sounds without the extension ("Weapons/pistol_shot"), loaded the first time it's
    /// asked for (or made, if there's no file but one was <see cref="Provide"/>d). Null (with one [WARN]) if the file is
    /// missing or can't be read.
    /// </summary>
    public SoundClip? Get(string name)
    {
        if (_clips.TryGetValue(name, out var cached))
            return cached;
        using var timer = Perf.AudioLoad.Time(name);
        SoundClip? clip = null;
        var path = Path.Combine(_folder, name.Replace('/', Path.DirectorySeparatorChar) + ".wav");
        try
        {
            var made = !File.Exists(path) && _made.ContainsKey(name);
            if (!made && !File.Exists(path))
                throw new FileNotFoundException("no such file", path);
            var wav = made ? _made[name]() : WavFile.Load(path);
            var stereo = Al.GenBuffer();
            stereo.SetData(wav.Channels == 2 ? AlFormat.Stereo16 : AlFormat.Mono16, wav.Bytes(), wav.SampleRate);
            var mono = stereo;
            if (wav.Channels == 2)
            {
                mono = Al.GenBuffer();
                mono.SetData(AlFormat.Mono16, wav.MonoBytes(), wav.SampleRate);
            }

            clip = new SoundClip { Name = name, Stereo = stereo, Mono = mono, Length = wav.Length, Channels = wav.Channels };
            if (Debug || !made)
                Log.Write($"Sound: {name} ({wav.Length:0.00} s, {wav.SampleRate} Hz, {wav.Channels} ch, {(made ? "made in code" : $"from {wav.SourceBits}-bit")})");
        }
        catch (Exception e)
        {
            Log.Write($"[WARN] Sound '{name}' not loaded ({e.GetType().Name}: {e.Message}).");
        }

        _clips[name] = clip;
        return clip;
    }

    /// <summary>Plays a clip as recorded, at the listener (the player's own sounds). <paramref name="delay"/> seconds from now, starting <paramref name="offset"/> seconds in.</summary>
    public void Play(SoundClip? clip, float gain = 1f, float pitch = 1f, float delay = 0f, float offset = 0f, string? tag = null)
    {
        if (clip is null)
            return;
        Queue(new Pending { Clip = clip, Gain = gain, Pitch = pitch, Offset = offset, Tag = tag, At = _time + delay });
    }

    /// <summary>Plays the mono mix of a clip at a point in the world; it falls off beyond <paramref name="referenceDistance"/> metres.</summary>
    public void PlayAt(SoundClip? clip, float3 position, float gain = 1f, float pitch = 1f, float delay = 0f, float referenceDistance = 3f, string? tag = null)
    {
        if (clip is null)
            return;
        Queue(new Pending { Clip = clip, Position = position, Gain = gain, Pitch = pitch, ReferenceDistance = referenceDistance, Tag = tag, At = _time + delay });
    }

    /// <summary>Stops sounds started with <paramref name="tag"/>, and drops any still waiting to start (a reload cut short).</summary>
    /// <summary>How many of the voices are sounding (asks OpenAL about each: for the logs, not every frame).</summary>
    public int VoicesPlaying
    {
        get
        {
            var playing = 0;
            for (var i = 0; i < Voices; i++)
            {
                if (_voices[i].Source.State == AlSourceState.Playing)
                    playing++;
            }

            return playing;
        }
    }

    public void Cancel(string tag)
    {
        _pending.RemoveAll(p => p.Tag == tag);
        for (var i = 0; i < Voices; i++)
        {
            if (_voices[i].Tag == tag)
            {
                _voices[i].Source.Stop();
                _voices[i].Tag = null;
            }
        }
    }

    public void StopAll()
    {
        _pending.Clear();
        foreach (var v in _voices)
            v.Source.Stop();
    }

    /// <summary>Each frame: the listener follows the camera, delayed sounds whose time has come start, and sounds in the world are re-checked for walls in the way.</summary>
    public void Update(float dt, float3 position, float3 forward, float3 velocity)
    {
        _time += dt;
        _listener = position;
        Al.Listener.Position = position;
        Al.Listener.Velocity = velocity;
        var right = float3.Cross(forward, float3.UnitY);
        var up = float3.Dot(right, right) > 1e-6f ? float3.Normalize(float3.Cross(right, forward)) : new float3(0, 0, 1);
        Al.Listener.Orientation = (forward, up);

        if (_efx)
        {
            _listenerBlend = BlendAt(position);
            var room = _listenerBlend.Main;
            if (room != _listenerRoom)
            {
                if (Debug)
                    Log.Write($"[AUDIO] listener: {(_listenerRoom >= 0 ? _rooms[_listenerRoom].Def.Name : "-")} -> {(room >= 0 ? _rooms[room].Def.Name : "-")} ({Describe(_listenerBlend)})");
                _listenerRoom = room;
            }

            UpdateOcclusion(dt);
            if (float.Abs(_deafness - _appliedDeafness) > 0.01f || (_deafness == 0f && _appliedDeafness != 0f))
            {
                _appliedDeafness = _deafness;
                for (var i = 0; i < Voices; i++)
                    if (_voices[i].Source.State == AlSourceState.Playing)
                        ApplyDirect(ref _voices[i]);
            }
        }

        for (var i = 0; i < _pending.Count; i++)
        {
            if (_pending[i].At > _time)
                continue;
            Start(_pending[i]);
            _pending.RemoveAt(i--);
        }
    }

    private void UpdateOcclusion(float dt)
    {
        var ease = float.Min(1f, dt * OcclusionRate);
        for (var i = 0; i < Voices; i++)
        {
            ref var v = ref _voices[i];
            if (!v.World || v.Source.State != AlSourceState.Playing)
                continue;
            if (_time >= v.NextCheck)
            {
                v.OcclusionTarget = Occluded(v.Position);
                v.NextCheck = _time + OcclusionCheckEvery;
            }

            if (float.Abs(v.OcclusionTarget - v.Occlusion) < 0.002f)
                continue;
            v.Occlusion += (v.OcclusionTarget - v.Occlusion) * ease;
            ApplyDirect(ref v);
        }
    }

    /// <summary>1 if something solid is between the listener and <paramref name="at"/> (not counting the surface the sound is on), else 0.</summary>
    private float Occluded(float3 at)
    {
        if (Occlusion is null)
            return 0f;
        var d = at - _listener;
        var length = float.Sqrt(float3.Dot(d, d));
        if (length <= OcclusionSlack)
            return 0f;
        var clear = Occlusion(_listener, at);
        return length * (1f - clear) > OcclusionSlack ? 1f : 0f;
    }

    private void ApplyDirect(ref Voice v)
    {
        var f = v.Direct;
        var deaf = v.Tag == RingingTag ? 0f : _deafness;
        f.Gain = (1f + (OccludedGain - 1f) * v.Occlusion) * (1f - 0.85f * deaf);
        f.GainHf = (1f + (OccludedGainHf - 1f) * v.Occlusion) * (1f - 0.97f * deaf);
        // OpenAL copies a filter's settings when it's attached, so attach it again after every change.
        v.Source.DirectFilter = f.Filter;
    }

    private void Queue(in Pending p)
    {
        if (p.At <= _time)
            Start(p);
        else
            _pending.Add(p);
    }

    private void Start(in Pending p)
    {
        // A free voice, or else the one that's been playing longest.
        var pick = 0;
        var oldest = double.MaxValue;
        for (var i = 0; i < Voices; i++)
        {
            if (_voices[i].Source.State != AlSourceState.Playing)
            {
                pick = i;
                break;
            }

            if (_voices[i].Started < oldest)
            {
                oldest = _voices[i].Started;
                pick = i;
            }
        }

        ref var voice = ref _voices[pick];
        var s = voice.Source;
        s.Stop();
        voice.Started = _time;
        voice.Tag = p.Tag;
        voice.World = p.Position is not null;
        s.Gain = p.Gain;
        s.Pitch = p.Pitch;
        if (p.Position is { } at)
        {
            s.Buffer = p.Clip.Mono;
            s.SourceRelative = false;
            s.Position = at;
            s.ReferenceDistance = p.ReferenceDistance;
            s.RolloffFactor = 1f;
            s.MaxDistance = 500f;
            voice.Position = at;
        }
        else
        {
            s.Buffer = p.Clip.Stereo;
            s.SourceRelative = true;
            s.Position = float3.Zero;
            s.RolloffFactor = 0f;
        }

        s.Velocity = float3.Zero;
        if (_efx)
            Route(ref voice);

        // A late start (the frame came after the sound was due) begins that much further in, to stay in sync.
        var late = (float)(_time - p.At);
        s.SecOffset = float.Clamp(p.Offset + float.Max(late, 0f), 0f, float.Max(p.Clip.Length - 0.01f, 0f));
        s.Play();
        if (Debug)
            Log.Write($"[AUDIO] {p.Clip.Name} on voice {pick} at {_time:0.000} s ({(p.Position is null ? "2D" : "3D")}, {late * 1000:0} ms late, " +
                      $"{(_efx ? voice.RouteText : "dry")}{(voice.World && voice.Occlusion > 0.5f ? ", occluded" : "")}): {s.State}, AL {Al.GetError()}");
    }

    /// <summary>
    /// Sends a starting voice to the reverb of where it's made (2D sounds: where the listener is), both rooms in a
    /// doorway; a world sound also bleeds a little into the listener's room(s) it isn't already in. Sets its occlusion
    /// straight away.
    /// </summary>
    private void Route(ref Voice voice)
    {
        var s = voice.Source;
        var own = voice.World ? BlendAt(voice.Position) : _listenerBlend;
        voice.Room = own.Main;

        // Up to four sends wanted: (room, level, treble). Equal-power: a 50/50 doorway sends 0.71 to each room.
        Span<(int Room, float Gain, float GainHf)> wanted = stackalloc (int, float, float)[4];
        var count = 0;
        if (own.A >= 0 && own.WeightA > 0f)
            wanted[count++] = (own.A, float.Sqrt(own.WeightA), 1f);
        if (own.B >= 0 && own.WeightB > 0f)
            wanted[count++] = (own.B, float.Sqrt(own.WeightB), 1f);
        if (voice.World)
        {
            var l = _listenerBlend;
            if (l.A >= 0 && l.WeightA > 0f && !own.Has(l.A))
                wanted[count++] = (l.A, BleedGain * float.Sqrt(l.WeightA), BleedGainHf);
            if (l.B >= 0 && l.WeightB > 0f && !own.Has(l.B))
                wanted[count++] = (l.B, BleedGain * float.Sqrt(l.WeightB), BleedGainHf);
        }

        // Fewer sends than wanted (an OpenAL without 4): keep the loudest.
        wanted[..count].Sort((x, y) => y.Gain.CompareTo(x.Gain));
        var text = Debug ? new System.Text.StringBuilder("rooms ") : null;
        for (var send = 0; send < _sends; send++)
        {
            if (send < count)
            {
                var (room, gain, gainHf) = wanted[send];
                var f = voice.Sends[send];
                f.Gain = gain;
                f.GainHf = gainHf;
                // The filter's settings are copied when it's attached.
                s.SetAuxiliarySend(_rooms[room].Slot, send, f.Filter);
                text?.Append(send == 0 ? "" : ", ").Append(_rooms[room].Def.Name).Append(gainHf < 1f ? " (bleed) " : " ").Append(gain.ToString("0.00"));
            }
            else
            {
                s.SetAuxiliarySend(AlAuxiliaryEffectSlot.Null, send, AlFilter.Null);
            }
        }

        voice.RouteText = count == 0 ? "no room" : text?.ToString();

        // Distant sounds lose their treble; a sound behind a wall starts out muffled (no fade-in).
        s.AirAbsorptionFactor = voice.World ? 1f : 0f;
        voice.Occlusion = voice.OcclusionTarget = voice.World ? Occluded(voice.Position) : 0f;
        voice.NextCheck = _time + OcclusionCheckEvery;
        if (voice.World || _deafness > 0f)
            ApplyDirect(ref voice);
        else
            s.DirectFilter = AlFilter.Null;
    }

    private void ClearRooms()
    {
        // Detach every source from the slots first: a slot that's still in use can't be deleted.
        for (var i = 0; i < Voices; i++)
        {
            _voices[i].Source.Stop();
            _voices[i].Room = -1;
            for (var send = 0; send < _sends; send++)
                _voices[i].Source.SetAuxiliarySend(AlAuxiliaryEffectSlot.Null, send, AlFilter.Null);
        }

        foreach (var room in _rooms)
        {
            room.Slot.Load(AlEffect.Null);
            room.Slot.Delete();
            room.Effect.Delete();
        }

        _rooms.Clear();
        _doorways.Clear();
        _listenerBlend = Blend.None;
        _listenerRoom = -1;
    }

    private static readonly bool Debug = Environment.GetEnvironmentVariable("BRUTALFPS_DEBUG") == "1";

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopAll();
        if (_efx)
        {
            ClearRooms();
            for (var i = 0; i < Voices; i++)
            {
                _voices[i].Source.DirectFilter = AlFilter.Null;
                _voices[i].Direct.Delete();
                foreach (var f in _voices[i].Sends)
                    f.Delete();
            }
        }

        foreach (var v in _voices)
        {
            v.Source.Buffer = AlBuffer.Null;
            v.Source.Delete();
        }

        foreach (var clip in _clips.Values)
        {
            if (clip is null)
                continue;
            if (clip.Mono.Name != clip.Stereo.Name)
                clip.Mono.Delete();
            clip.Stereo.Delete();
        }

        _clips.Clear();
        _context.Dispose();
        _device.Dispose();
    }
}

/// <summary>A WAV file's samples as 16-bit (interleaved), from 8/16/24/32-bit PCM or 32-bit float, mono or stereo (more channels: the first two).</summary>
public sealed class WavFile
{
    public required short[] Samples { get; init; }
    public int Channels { get; init; }
    public int SampleRate { get; init; }
    public int SourceBits { get; init; }
    public float Length => Samples.Length / (float)Channels / SampleRate;

    public ReadOnlySpan<byte> Bytes() => System.Runtime.InteropServices.MemoryMarshal.AsBytes(Samples.AsSpan());

    public ReadOnlySpan<byte> MonoBytes()
    {
        if (Channels == 1)
            return Bytes();
        var mono = new short[Samples.Length / 2];
        for (var i = 0; i < mono.Length; i++)
            mono[i] = (short)((Samples[2 * i] + Samples[2 * i + 1]) / 2);
        return System.Runtime.InteropServices.MemoryMarshal.AsBytes(mono.AsSpan());
    }

    public static WavFile Load(string path)
    {
        var d = File.ReadAllBytes(path);
        if (d.Length < 12 || d[0] != 'R' || d[1] != 'I' || d[2] != 'F' || d[3] != 'F' || d[8] != 'W' || d[9] != 'A' || d[10] != 'V' || d[11] != 'E')
            throw new FormatException("not a RIFF/WAVE file");
        int format = 0, channels = 0, rate = 0, bits = 0, dataStart = -1, dataLength = 0;
        var pos = 12;
        while (pos + 8 <= d.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(d, pos, 4);
            var size = BitConverter.ToInt32(d, pos + 4);
            var body = pos + 8;
            if (id == "fmt ")
            {
                format = BitConverter.ToUInt16(d, body);
                channels = BitConverter.ToUInt16(d, body + 2);
                rate = BitConverter.ToInt32(d, body + 4);
                bits = BitConverter.ToUInt16(d, body + 14);
                if (format == 0xFFFE && size >= 26)
                    format = BitConverter.ToUInt16(d, body + 24);   // WAVE_FORMAT_EXTENSIBLE: the sub-format
            }
            else if (id == "data")
            {
                dataStart = body;
                dataLength = int.Min(size, d.Length - body);
            }

            pos = body + size + (size & 1);
        }

        if (dataStart < 0 || channels <= 0 || rate <= 0)
            throw new FormatException("no fmt or data chunk");
        if (!(format == 1 && bits is 8 or 16 or 24 or 32) && !(format == 3 && bits == 32))
            throw new FormatException($"unsupported WAV encoding (format {format}, {bits}-bit): use PCM or 32-bit float");

        var outChannels = int.Min(channels, 2);
        var bytesPer = bits / 8;
        var frames = dataLength / (bytesPer * channels);
        var samples = new short[frames * outChannels];
        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < outChannels; c++)
            {
                var o = dataStart + (f * channels + c) * bytesPer;
                float v = format == 3 ? BitConverter.ToSingle(d, o)
                    : bits switch
                    {
                        8 => (d[o] - 128) / 128f,
                        16 => BitConverter.ToInt16(d, o) / 32768f,
                        24 => ((d[o] | (d[o + 1] << 8) | (d[o + 2] << 16)) << 8 >> 8) / 8388608f,
                        _ => BitConverter.ToInt32(d, o) / 2147483648f,
                    };
                samples[f * outChannels + c] = (short)float.Clamp(float.Round(v * 32767f), -32768f, 32767f);
            }
        }

        return new WavFile { Samples = samples, Channels = outChannels, SampleRate = rate, SourceBits = bits };
    }
}
