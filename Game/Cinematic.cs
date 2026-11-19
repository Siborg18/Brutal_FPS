using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// A scripted, on-rails sequence for the player: a list of steps run one after another. While it runs the player
/// walks where the script says (through <see cref="PlayerController.RailWish"/>, so collisions, footsteps and head
/// height work as in play) and looks where it says, though the mouse can still look around a little (the view drifts
/// back when you let go). Black bars frame the picture; the screen can fade to and from black; captions show over the
/// lower bar. Press Enter to skip: every cue still to come is fired at once and you're put where the script ends.
///
/// Build one with the fluent methods, e.g.
/// <code>
/// new Cinematic("Intro").Letterbox(true, 0).FadeOut(0).Caption("06:40", 4).FadeIn(2.5f)
///     .Walk(1.4f, a, b, c).Cue("door:entrance").Wait(1.2f).Look(target, 1.5f).WaitFor("container landed", 8f) ...
/// </code>
/// <see cref="Cued"/> is raised for each <c>Cue</c> step (open a door, start the helicopter, bring a portal online);
/// <c>WaitFor</c> holds until <see cref="Signal"/> is called with its name (an animation saying it's done), or until its
/// timeout runs out, so a script can be written before the thing it waits for exists.
/// </summary>
public sealed class Cinematic(string name)
{
    private abstract record Step;
    private sealed record WalkStep(float3[] Path, float Speed, float3? LookAt) : Step;
    private sealed record LookStep(float3 Target, float Seconds) : Step;
    private sealed record TrackStep(Func<float3> Target, float Seconds) : Step;
    private sealed record LookAlongStep : Step;
    private sealed record WaitStep(float Seconds) : Step;
    private sealed record CueStep(string Name) : Step;
    private sealed record WaitForStep(string Signal, float Timeout) : Step;
    private sealed record FadeStep(float To, float Seconds, bool Hold) : Step;
    private sealed record CaptionStep(string Text, float Seconds) : Step;
    private sealed record LetterboxStep(bool On, float Seconds) : Step;
    private sealed record CarriedStep(bool On) : Step;
    private sealed record GlideStep(float3 To, float Seconds, float3? LookAt, bool EaseIn, bool EaseOut) : Step;

    private readonly List<Step> _steps = [];
    private readonly HashSet<string> _signals = [];

    public string Name { get; } = name;

    /// <summary>A <c>Cue</c> step was reached (or skipped past): its name.</summary>
    public event Action<string>? Cued;

    /// <summary>The sequence has ended (played through or skipped): the player has control again.</summary>
    public event Action? Finished;

    // ------------------------------------------------------------------------------------------------
    // Building the script.
    // ------------------------------------------------------------------------------------------------

    /// <summary>Walks through the points (world, feet) at <paramref name="speed"/> m/s, looking the way it walks.</summary>
    public Cinematic Walk(float speed, params float3[] path) => Add(new WalkStep(path, speed, null));

    /// <summary>Walks through the points while looking at <paramref name="lookAt"/>.</summary>
    public Cinematic WalkLooking(float3 lookAt, float speed, params float3[] path) => Add(new WalkStep(path, speed, lookAt));

    /// <summary>Turns to look at <paramref name="target"/> (and keeps looking at it until told otherwise) over about <paramref name="seconds"/>.</summary>
    public Cinematic Look(float3 target, float seconds) => Add(new LookStep(target, seconds));

    /// <summary>Starts following something that moves (and doesn't hold the script), settling on it over about <paramref name="seconds"/>.</summary>
    public Cinematic Track(Func<float3> target, float seconds) => Add(new TrackStep(target, seconds));

    /// <summary>Goes back to looking straight ahead (the way the next walk goes).</summary>
    public Cinematic LookAhead() => Add(new LookAlongStep());

    public Cinematic Wait(float seconds) => Add(new WaitStep(seconds));

    /// <summary>Raises <see cref="Cued"/> with <paramref name="cue"/> and goes straight on.</summary>
    public Cinematic Cue(string cue) => Add(new CueStep(cue));

    /// <summary>Holds until <see cref="Signal"/>(<paramref name="signal"/>) is called, or <paramref name="timeout"/> seconds pass.</summary>
    public Cinematic WaitFor(string signal, float timeout) => Add(new WaitForStep(signal, timeout));

    /// <summary>Fades to black over <paramref name="seconds"/> (holding until it's black, unless <paramref name="hold"/> is false).</summary>
    public Cinematic FadeOut(float seconds, bool hold = true) => Add(new FadeStep(1f, seconds, hold));

    /// <summary>Fades in from black.</summary>
    public Cinematic FadeIn(float seconds, bool hold = false) => Add(new FadeStep(0f, seconds, hold));

    /// <summary>Shows a caption for <paramref name="seconds"/> (doesn't hold the script).</summary>
    public Cinematic Caption(string text, float seconds) => Add(new CaptionStep(text, seconds));

    /// <summary>Slides the black bars in or out over <paramref name="seconds"/> (doesn't hold the script).</summary>
    public Cinematic Letterbox(bool on, float seconds) => Add(new LetterboxStep(on, seconds));

    /// <summary>Takes the player's body out of the physics (<see cref="PlayerController.Carried"/>), or puts it back, so that <c>Glide</c> or the script's owner can place it. It's put back when the script ends.</summary>
    public Cinematic Carried(bool on) => Add(new CarriedStep(on));

    /// <summary>
    /// While <c>Carried</c>: moves the feet straight to <paramref name="to"/> in <paramref name="seconds"/>, through anything in the
    /// way, slow to start and stop unless told otherwise (a drop only eases in), and looking at <paramref name="lookAt"/> if given.
    /// </summary>
    public Cinematic Glide(float seconds, float3 to, float3? lookAt = null, bool easeIn = true, bool easeOut = true) =>
        Add(new GlideStep(to, seconds, lookAt, easeIn, easeOut));

    private Cinematic Add(Step step)
    {
        _steps.Add(step);
        return this;
    }

    // ------------------------------------------------------------------------------------------------
    // Running it.
    // ------------------------------------------------------------------------------------------------

    private int _index = -1;
    private float _stepTime;
    private int _waypoint;
    private float3 _lastPosition;
    private float _stuckFor;
    private float3 _glideFrom;

    // The view: where the script points it (base), plus what the mouse adds (offset).
    private float _baseYaw, _basePitch;
    private float _offsetYaw, _offsetPitch;
    private float3? _lookTarget;
    private Func<float3>? _lookFollow;   // a moving target (see Track): it goes before _lookTarget
    private float _lookRate = 3f;

    private float _fade, _fadeFrom, _fadeTo, _fadeTime, _fadeSeconds = 1f;
    private float _bars, _barsFrom, _barsTo, _barsTime, _barsSeconds = 1f;
    private string? _caption;
    private float _captionLeft;

    /// <summary>True from <see cref="Start"/> until the last step is done.</summary>
    public bool Active => _index >= 0 && _index < _steps.Count;

    /// <summary>0..1: how black the screen is.</summary>
    public float Fade => _fade;

    /// <summary>The script's own name for where it is (for the log and debugging).</summary>
    public string Status => Active ? $"{Name}: step {_index + 1}/{_steps.Count} ({_steps[_index].GetType().Name.Replace("Step", "")})" : $"{Name}: idle";

    /// <summary>Tells a <c>WaitFor</c> step that what it's waiting for has happened (it can come early: it's remembered).</summary>
    public void Signal(string signal) => _signals.Add(signal);

    public void Start(PlayerController player)
    {
        _index = 0;
        _stepTime = 0;
        _signals.Clear();
        _baseYaw = player.Yaw;
        _basePitch = player.Pitch;
        _offsetYaw = _offsetPitch = 0;
        _lookTarget = null;
        _lookFollow = null;
        _fade = _fadeTo = _fadeFrom = 0;
        _bars = _barsTo = _barsFrom = 0;
        _caption = null;
        player.LookLocked = true;
        player.RailWish = float3.Zero;
        Log.Write($"[CINEMATIC] {Name} started ({_steps.Count} steps)");
        BeginStep(player);
    }

    public void Update(float dt, PlayerController player, Input input)
    {
        if (!Active)
            return;

        // Free look: the mouse turns the view a little way off the script's line; it drifts back when you let go.
        var mouse = input.MouseDelta;
        _offsetYaw = float.Clamp(_offsetYaw + mouse.X * player.MouseSensitivity, -0.75f, 0.75f);
        _offsetPitch = float.Clamp(_offsetPitch - mouse.Y * player.MouseSensitivity, -0.45f, 0.45f);
        if (mouse.X == 0 && mouse.Y == 0)
        {
            var back = 1f - float.Exp(-dt * 0.8f);
            _offsetYaw -= _offsetYaw * back;
            _offsetPitch -= _offsetPitch * back;
        }

        // Steps that finish at once (cues, captions, bars...) run back to back in the same frame.
        var frameDt = dt;
        for (var guard = 0; guard < 64 && Active; guard++)
        {
            if (!RunStep(dt, player))
                break;
            _index++;
            _stepTime = 0;
            if (Active)
                BeginStep(player);
            dt = 0;   // (the frame's time has been spent on the step that just ended)
        }

        UpdateLook(frameDt, player);
        UpdateOverlays(frameDt);
        if (!Active)
            End(player);
    }

    /// <summary>Jumps to the end: fires every cue still to come, puts the player where the script leaves them.</summary>
    public void Skip(PlayerController player)
    {
        if (!Active)
            return;
        Log.Write($"[CINEMATIC] {Name} skipped at step {_index + 1}");
        FastForward(player, _steps.Count);
        _fade = 0;
        _bars = 0;
        _caption = null;
        End(player);
    }

    /// <summary>
    /// Carries on from step <paramref name="step"/> (1-based), as if everything before it had already happened: its
    /// cues fired, the player where its walks ended, the fade and bars where they'd be. For working on a long script
    /// (BRUTALFPS_INTRO_STEP).
    /// </summary>
    public void JumpTo(PlayerController player, int step)
    {
        if (!Active)
            Start(player);
        var until = int.Clamp(step - 1, 0, _steps.Count);
        if (until <= _index)
            return;
        Log.Write($"[CINEMATIC] {Name}: jumping to step {until + 1}");
        FastForward(player, until);
        _caption = null;
        if (Active)
        {
            _stepTime = 0;
            BeginStep(player);
        }
        else
            End(player);
    }

    /// <summary>The step (1-based, as <see cref="JumpTo"/> takes it) of the cue called <paramref name="cue"/>; 0 if there's none.</summary>
    public int StepOfCue(string cue) => _steps.FindIndex(s => s is CueStep c && c.Name == cue) + 1;

    /// <summary>True while a skip or jump is firing the cues it passes (so their handlers can finish things at once).</summary>
    public bool FastForwarding { get; private set; }

    /// <summary>Applies steps from the current one up to (not including) <paramref name="until"/> at once.</summary>
    private void FastForward(PlayerController player, int until)
    {
        FastForwarding = true;
        for (; _index < until; _index++)
        {
            switch (_steps[_index])
            {
                case CueStep cue:
                    Cued?.Invoke(cue.Name);
                    break;
                case WalkStep walk:
                {
                    var from = walk.Path.Length > 1 ? walk.Path[^2] : player.Position;
                    player.Position = walk.Path[^1];
                    player.Velocity = float3.Zero;
                    _lookTarget = walk.LookAt;
                    _lookFollow = null;
                    if (walk.LookAt is null && float3.Distance(from, walk.Path[^1]) > 0.01f)
                    {
                        (_baseYaw, _) = Angles(walk.Path[^1] - from);
                        _basePitch = -0.06f;
                    }
                    break;
                }
                case LookStep look:
                    _lookTarget = look.Target;
                    _lookFollow = null;
                    (_baseYaw, _basePitch) = Angles(look.Target - player.EyePosition);
                    break;
                case TrackStep track:
                    _lookFollow = track.Target;
                    break;
                case LookAlongStep:
                    _lookTarget = null;
                    _lookFollow = null;
                    break;
                case FadeStep fade:
                    _fade = _fadeFrom = _fadeTo = fade.To;
                    break;
                case LetterboxStep bars:
                    _bars = _barsFrom = _barsTo = bars.On ? 1f : 0f;
                    break;
                case CarriedStep carried:
                    player.Carried = carried.On;
                    break;
                case GlideStep glide:
                    player.Position = glide.To;
                    player.Velocity = float3.Zero;
                    if (glide.LookAt is { } glideLook)
                    {
                        _lookTarget = glideLook;
                        _lookFollow = null;
                    }
                    break;
            }
        }
        FastForwarding = false;
        if ((_lookFollow?.Invoke() ?? _lookTarget) is { } t)
            (_baseYaw, _basePitch) = Angles(t - player.EyePosition);
        player.Yaw = _baseYaw;
        player.Pitch = _basePitch;
    }

    private void End(PlayerController player)
    {
        _index = _steps.Count;
        player.LookLocked = false;
        player.RailWish = null;
        player.Carried = false;
        _offsetYaw = _offsetPitch = 0;
        Log.Write($"[CINEMATIC] {Name} finished");
        Finished?.Invoke();
    }

    private void BeginStep(PlayerController player)
    {
        var step = _steps[_index];
        Log.Write($"[CINEMATIC] {Name} step {_index + 1}: {step}");
        switch (step)
        {
            case WalkStep walk:
                _waypoint = 0;
                _stuckFor = 0;
                _lastPosition = player.Position;
                _lookTarget = walk.LookAt;
                _lookFollow = null;
                _lookRate = 3f;
                break;
            case LookStep look:
                _lookTarget = look.Target;
                _lookFollow = null;
                _lookRate = 4.5f / float.Max(0.3f, look.Seconds);
                break;
            case TrackStep track:
                _lookFollow = track.Target;
                _lookRate = 4.5f / float.Max(0.3f, track.Seconds);
                break;
            case LookAlongStep:
                _lookTarget = null;
                _lookFollow = null;
                break;
            case FadeStep fade:
                _fadeFrom = _fade;
                _fadeTo = fade.To;
                _fadeTime = 0;
                _fadeSeconds = float.Max(fade.Seconds, 1e-3f);
                if (fade.Seconds <= 0)
                    _fade = fade.To;
                break;
            case LetterboxStep bars:
                _barsFrom = _bars;
                _barsTo = bars.On ? 1f : 0f;
                _barsTime = 0;
                _barsSeconds = float.Max(bars.Seconds, 1e-3f);
                if (bars.Seconds <= 0)
                    _bars = _barsTo;
                break;
            case CaptionStep caption:
                _caption = caption.Text;
                _captionLeft = caption.Seconds;
                break;
            case CarriedStep carried:
                player.Carried = carried.On;
                player.RailWish = float3.Zero;
                break;
            case GlideStep glide:
                _glideFrom = player.Position;
                player.RailWish = float3.Zero;
                if (glide.LookAt is { } glideLook)
                {
                    _lookTarget = glideLook;
                    _lookFollow = null;
                    _lookRate = 4.5f / float.Max(0.3f, glide.Seconds);
                }
                break;
            case CueStep cue:
                Log.Write($"[CINEMATIC] cue \"{cue.Name}\"");
                Cued?.Invoke(cue.Name);
                break;
        }
    }

    /// <summary>Runs the current step for dt; true once it's done.</summary>
    private bool RunStep(float dt, PlayerController player)
    {
        _stepTime += dt;
        switch (_steps[_index])
        {
            case WalkStep walk:
                return Walk(dt, player, walk);
            case LookStep look:
                player.RailWish = float3.Zero;
                return _stepTime >= look.Seconds;
            case WaitStep wait:
                player.RailWish = float3.Zero;
                return _stepTime >= wait.Seconds;
            case WaitForStep waitFor:
                player.RailWish = float3.Zero;
                if (_signals.Remove(waitFor.Signal))
                    return true;
                if (_stepTime >= waitFor.Timeout)
                {
                    Log.Write($"[CINEMATIC] \"{waitFor.Signal}\" didn't come within {waitFor.Timeout:0.#} s: carrying on");
                    return true;
                }
                return false;
            case FadeStep fade:
                return !fade.Hold || _stepTime >= fade.Seconds;
            case GlideStep glide:
            {
                var t = float.Clamp(_stepTime / float.Max(glide.Seconds, 1e-3f), 0f, 1f);
                var eased = (glide.EaseIn, glide.EaseOut) switch
                {
                    (true, true) => t * t * (3f - 2f * t),
                    (true, false) => t * t,
                    (false, true) => t * (2f - t),
                    _ => t,
                };
                player.Position = float3.Lerp(_glideFrom, glide.To, eased);
                player.Velocity = float3.Zero;
                return t >= 1f;
            }
            default:
                return true;   // cues, captions, bars, look-ahead: instant
        }
    }

    private bool Walk(float dt, PlayerController player, WalkStep walk)
    {
        var last = walk.Path.Length - 1;
        while (true)
        {
            var target = walk.Path[_waypoint];
            var to = target - player.Position;
            to.Y = 0;
            var distance = float.Sqrt(to.X * to.X + to.Z * to.Z);
            var final = _waypoint == last;
            if (distance < (final ? 0.08f : 0.45f))
            {
                if (final)
                {
                    player.RailWish = float3.Zero;
                    return true;
                }
                _waypoint++;
                continue;
            }

            // Ease in to the last point; otherwise walk at the script's pace.
            player.RailSpeed = final ? walk.Speed * float.Clamp(distance / 0.9f, 0.2f, 1f) : walk.Speed;
            player.RailWish = to / distance;

            // Blocked (a door that hasn't opened, someone in the way) for a few seconds: hop to the point instead.
            var moved = player.Position - _lastPosition;
            _lastPosition = player.Position;
            _stuckFor = moved.X * moved.X + moved.Z * moved.Z < (walk.Speed * dt * 0.2f) * (walk.Speed * dt * 0.2f) ? _stuckFor + dt : 0f;
            if (_stuckFor > 2.5f)
            {
                Log.Write($"[CINEMATIC] {Name}: stuck on the way to {target}; moving the player on");
                player.Position = target;
                player.Velocity = float3.Zero;
                _stuckFor = 0;
            }
            return false;
        }
    }

    private void UpdateLook(float dt, PlayerController player)
    {
        float wantYaw, wantPitch;
        if ((_lookFollow?.Invoke() ?? _lookTarget) is { } target)
            (wantYaw, wantPitch) = Angles(target - player.EyePosition);
        else
        {
            // Along the way being walked (or straight on, standing still), eyes a little down.
            var heading = Active && _steps[_index] is WalkStep walk && player.RailWish is { } w && w != float3.Zero ? w : (float3?)null;
            wantYaw = heading is { } h ? Angles(h).Yaw : _baseYaw;
            wantPitch = -0.06f;
            if (Active && _steps[_index] is WalkStep { LookAt: null } && heading is null)
                wantYaw = _baseYaw;
        }
        var k = 1f - float.Exp(-dt * _lookRate);
        _baseYaw += WrapAngle(wantYaw - _baseYaw) * k;
        _basePitch += (wantPitch - _basePitch) * k;
        player.Yaw = _baseYaw + _offsetYaw;
        player.Pitch = float.Clamp(_basePitch + _offsetPitch, -1.5f, 1.5f);
    }

    private void UpdateOverlays(float dt)
    {
        if (_fade != _fadeTo)
        {
            _fadeTime += dt;
            var t = float.Clamp(_fadeTime / _fadeSeconds, 0f, 1f);
            _fade = _fadeFrom + (_fadeTo - _fadeFrom) * (t * t * (3f - 2f * t));
            if (t >= 1f)
                _fade = _fadeTo;
        }
        if (_bars != _barsTo)
        {
            _barsTime += dt;
            var t = float.Clamp(_barsTime / _barsSeconds, 0f, 1f);
            _bars = _barsFrom + (_barsTo - _barsFrom) * (t * t * (3f - 2f * t));
            if (t >= 1f)
                _bars = _barsTo;
        }
        if (_caption is not null && (_captionLeft -= dt) <= 0)
            _caption = null;
    }

    /// <summary>Yaw and pitch (radians, the camera's convention) of a direction.</summary>
    private static (float Yaw, float Pitch) Angles(float3 d)
    {
        var flat = float.Sqrt(d.X * d.X + d.Z * d.Z);
        return (float.Atan2(d.X, -d.Z), float.Atan2(d.Y, float.Max(flat, 1e-4f)));
    }

    private static float WrapAngle(float a)
    {
        while (a > float.Pi)
            a -= float.Tau;
        while (a < -float.Pi)
            a += float.Tau;
        return a;
    }

    // ------------------------------------------------------------------------------------------------
    // Drawing: the fade, the bars, the caption, a skip hint.
    // ------------------------------------------------------------------------------------------------

    private static readonly float3 Black = new(0, 0, 0);
    private static readonly float3 CaptionInk = new(0.92f, 0.92f, 0.9f);
    private static readonly float3 HintInk = new(0.45f, 0.45f, 0.45f);

    /// <summary>Draws over the finished frame (call it last).</summary>
    public void Draw(Renderer renderer, int2 framebufferSize)
    {
        if (!Active)
            return;
        renderer.DrawFade(_fade);
        renderer.BeginOverlay();
        var aspect = framebufferSize.Y > 0 ? (float)framebufferSize.X / framebufferSize.Y : 1f;
        var barH = 0.24f * _bars;   // in "square" space, the screen is 2 high
        if (barH > 0.001f)
        {
            PcTerminal.Rect(renderer, aspect, new float2(0, 1f - barH / 2), new float2(aspect * 2.2f, barH), Black);
            PcTerminal.Rect(renderer, aspect, new float2(0, -1f + barH / 2), new float2(aspect * 2.2f, barH), Black);
        }
        var pixel = float.Max(2f, float.Round(framebufferSize.Y / 270f)) * 2f / float.Max(framebufferSize.Y, 1);
        if (_caption is { } text)
        {
            var width = (text.Length * BlockFont.Advance - 1) * pixel;
            PcTerminal.Text(renderer, aspect, text, new float2(-width / 2, -1f + float.Max(barH, 0.12f) * 0.5f + 3.5f * pixel), pixel, CaptionInk);
        }
        const string hint = "ENTER - SKIP";
        var small = pixel * 0.5f;
        var hintW = (hint.Length * BlockFont.Advance - 1) * small;
        PcTerminal.Text(renderer, aspect, hint, new float2(aspect - hintW - 0.04f, 1f - 0.03f), small, HintInk);
    }
}
