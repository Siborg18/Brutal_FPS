using Brutal.Numerics;

namespace BrutalFps.Engine;

/// <summary>A weight per skeleton node (0 or 1): which bones an overlay clip is allowed to move.</summary>
public sealed class BoneMask
{
    private readonly float[] _weights;

    private BoneMask(int nodeCount) => _weights = new float[nodeCount];

    /// <summary>Weight 1 on the roots and everything below them, 0 elsewhere. Unknown names are a [WARN] and ignored.</summary>
    public static BoneMask Subtree(Skeleton s, params string[] roots)
    {
        var mask = new BoneMask(s.NodeCount);
        foreach (var root in roots)
        {
            var node = s.NodeIndex(root);
            if (node < 0)
            {
                Log.Write($"[WARN] BoneMask: no bone named '{root}'.");
                continue;
            }

            foreach (var n in s.Subtree(node))
                mask._weights[n] = 1f;
        }

        return mask;
    }

    public float this[int node] => (uint)node < (uint)_weights.Length ? _weights[node] : 0f;
}

/// <summary>
/// Plays a person's clips. The base layer is either locomotion (Idle / Walk / Run blended by ground speed, Walk and Run
/// sampled at one shared phase that advances at the rate the feet need to keep still on the ground) or a full-body
/// one-shot cross-faded over it. An overlay layer applies one clip only to the bones of a <see cref="BoneMask"/>.
/// Nothing is allocated after construction.
/// </summary>
public sealed class Animator
{
    private const float MaxWalkSpeedUp = 1.6f;   // Walk played faster when there's no Run clip

    /// <summary>One cross-fading clip player (the full-body one-shot, or the overlay).</summary>
    private sealed class Slot
    {
        public AnimationClip? Clip;
        public float Time;          // clip time (scaled by Speed)
        public float Age;           // seconds since it started, for the fade in
        public float FadeIn, FadeOut, Speed = 1f;
        public bool Stopping, Hold;
        public float StopAge;
        public BoneMask? Mask;

        public bool Loop => Clip!.Info.Loop;

        public float Weight()
        {
            if (Clip is null)
                return 0f;
            var w = FadeIn > 0 ? Smooth(Age / FadeIn) : 1f;
            if (Stopping)
                w *= FadeOut > 0 ? Smooth(1f - StopAge / FadeOut) : 0f;
            else if (!Loop && !Hold)
                w *= FadeOut > 0 ? Smooth((Clip.Duration - Time) / Speed / FadeOut) : 1f;
            return w;
        }

        public void Start(AnimationClip clip, float fadeIn, float fadeOut, float speed, BoneMask? mask)
        {
            // Carry the current weight over so replacing a playing clip doesn't pop.
            var carried = Weight();
            Clip = clip;
            Time = 0;
            FadeIn = fadeIn;
            FadeOut = fadeOut;
            Speed = float.Max(speed, 0.01f);
            Mask = mask;
            Stopping = false;
            StopAge = 0;
            Hold = !clip.Info.Loop && fadeOut <= 0;   // no fade out asked for: a one-shot stays on its last frame (a death)
            Age = fadeIn > 0 ? carried * fadeIn : 0;
        }

        /// <summary>Advances the clocks; the clip time before and after is returned for event detection.</summary>
        public void Advance(float dt, out float from, out float to)
        {
            from = Time;
            Age += dt;
            if (Stopping)
                StopAge += dt;
            Time += dt * Speed;
            to = Time;
            if (Clip is null)
                return;
            if (Stopping && StopAge >= FadeOut)
                Clip = null;
            else if (!Loop && !Hold && Time >= Clip.Duration)
                Clip = null;
        }

        public float SampleTime => Loop ? Time : float.Min(Time, Clip!.Duration * 0.9999f);

        private static float Smooth(float x)
        {
            x = float.Clamp(x, 0f, 1f);
            return x * x * (3 - 2 * x);
        }
    }

    private readonly Skeleton _skeleton;
    private readonly AnimationClip? _idle, _walk, _run;
    private readonly ClipLibrary _clips;
    private readonly NodePose[] _tmp, _fbPose, _ovPose;
    private readonly NodePose[] _last, _from;   // last output pose, and the snapshot a full-body switch fades from
    private float _fromAge, _fromFade;          // _fromFade 0 = no snapshot fade running
    private bool _hasLast;
    private readonly Slot _full = new(), _overlay = new();
    private readonly List<string> _events = new(16);
    private readonly float _walkStride, _walkSpeed, _runStride, _runSpeed;   // clips.json strides, or the known ones without it
    private float _speed;
    private float _phase;       // walk / run cycle, 0..1
    private float _idleTime;

    public Animator(Skeleton skeleton, ClipLibrary clips)
    {
        _skeleton = skeleton;
        _clips = clips;
        _idle = clips.Get("Idle");
        _walk = clips.Get("Walk");
        _run = clips.Get("Run");
        _walkStride = _walk is null ? 0f : _walk.Info.Stride > 0 ? _walk.Info.Stride : 1.24f;
        _walkSpeed = _walk is null || _walk.Duration <= 0 ? 0f : _walkStride / _walk.Duration;
        _runStride = _run is null ? 0f : _run.Info.Stride > 0 ? _run.Info.Stride : 2.70f;
        _runSpeed = _run is null || _run.Duration <= 0 ? 0f : _runStride / _run.Duration;
        _tmp = new NodePose[skeleton.NodeCount];
        _fbPose = new NodePose[skeleton.NodeCount];
        _ovPose = new NodePose[skeleton.NodeCount];
        _last = new NodePose[skeleton.NodeCount];
        _from = new NodePose[skeleton.NodeCount];
    }

    /// <summary>Starts the locomotion cycles part-way through, so a crowd isn't in step. (An addition to the contract.)</summary>
    public void Desync(float phase01, float idleSeconds)
    {
        _phase = phase01 - float.Floor(phase01);
        _idleTime = idleSeconds;
    }

    public void SetLocomotion(float groundSpeed) => _speed = float.Max(groundSpeed, 0f);

    /// <param name="startAt">s into the clip to start from (a loop: so people playing the same one aren't in step).</param>
    public void PlayFullBody(string clip, float fadeIn = 0.2f, float fadeOut = 0.25f, float speed = 1f, float startAt = 0f)
    {
        if (_clips.Get(clip) is not { } c)
            return;
        // Replacing a full-body clip that's showing (cowering -> a death, say): fade from the pose as it was drawn,
        // not from the locomotion underneath, so the switch doesn't pop.
        var replacing = _hasLast && fadeIn > 0 && _full.Weight() > 0.01f;
        if (replacing)
        {
            _last.CopyTo(_from, 0);
            _fromAge = 0;
            _fromFade = fadeIn;
        }

        _full.Start(c, fadeIn, fadeOut, speed, null);
        _full.Time = startAt;
        if (replacing)
            _full.Age = fadeIn;   // the snapshot does the fading in
    }

    public void PlayOverlay(string clip, BoneMask mask, float fadeIn = 0.3f, float fadeOut = 0.3f)
    {
        if (_clips.Get(clip) is { } c)
            _overlay.Start(c, fadeIn, fadeOut, 1f, mask);
    }

    public void Stop(string clip, float fadeOut = 0.25f)
    {
        foreach (var slot in (ReadOnlySpan<Slot>)[_full, _overlay])
        {
            if (slot.Clip is not null && !slot.Stopping && string.Equals(slot.Clip.Name, clip, StringComparison.OrdinalIgnoreCase))
            {
                slot.Stopping = true;
                slot.StopAge = 0;
                slot.FadeOut = fadeOut;
            }
        }
    }

    public bool IsPlaying(string clip)
    {
        foreach (var slot in (ReadOnlySpan<Slot>)[_full, _overlay])
        {
            if (slot.Clip is not null && !slot.Stopping && string.Equals(slot.Clip.Name, clip, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Remaining seconds of the current full-body one-shot (infinity while a looping clip plays), or 0.</summary>
    public float FullBodyRemaining
    {
        get
        {
            if (_full.Clip is null || _full.Hold)
                return 0f;
            if (_full.Stopping)
                return float.Max(_full.FadeOut - _full.StopAge, 0f);
            return _full.Loop ? float.PositiveInfinity : float.Max((_full.Clip.Duration - _full.Time) / _full.Speed, 0f);
        }
    }

    public IReadOnlyList<string> Events => _events;

    /// <summary>
    /// How the locomotion clips are weighted for a ground speed: idle to walk is linear in speed (so the walk's playback
    /// rate stays constant through the blend), walk to run is a smoothstep.
    /// </summary>
    private void LocomotionWeights(out float wIdle, out float wWalk, out float wRun, out float stride)
    {
        wIdle = wWalk = wRun = 0;
        var walkStride = _walkStride;
        var walkSpeed = _walkSpeed;
        if (_walk is null || walkSpeed <= 1e-4f)
        {
            wIdle = 1;
            stride = 0;
            return;
        }

        if (_run is null || _runSpeed <= walkSpeed + 1e-3f)
        {
            wWalk = float.Min(_speed / walkSpeed, 1f);
            wIdle = 1f - wWalk;
            stride = wWalk * walkStride;
            return;
        }

        if (_speed < walkSpeed)
        {
            wWalk = _speed / walkSpeed;
            wIdle = 1f - wWalk;
        }
        else
        {
            var x = float.Clamp((_speed - walkSpeed) / (_runSpeed - walkSpeed), 0f, 1f);
            var t = x * x * (3 - 2 * x);
            wRun = t;
            wWalk = 1f - t;
        }

        stride = wWalk * walkStride + wRun * _runStride;
    }

    /// <summary>The cycles per second the locomotion phase advances at for the current speed.</summary>
    private float PhaseRate(float stride)
    {
        if (_walk is null || stride <= 1e-4f)
            return 0f;
        var rate = _speed / stride;
        if (_run is null)
            rate = float.Min(rate, MaxWalkSpeedUp / _walk.Duration);
        return rate;
    }

    public void Update(float dt, Span<NodePose> pose)
    {
        _events.Clear();
        _skeleton.RestPose.CopyTo(pose);

        // Locomotion.
        LocomotionWeights(out var wIdle, out var wWalk, out var wRun, out var stride);
        var rate = PhaseRate(stride);
        var phase0 = _phase;
        var phase1 = phase0 + rate * dt;
        _phase = phase1 - float.Floor(phase1);
        _idleTime += dt;

        var fullWeight = _full.Weight();
        if (fullWeight < 0.999f)
        {
            var accumulated = 0f;
            Layer(_idle, _idleTime, wIdle, pose, ref accumulated);
            Layer(_walk, _phase * (_walk?.Duration ?? 0), wWalk, pose, ref accumulated);
            Layer(_run, _phase * (_run?.Duration ?? 0), wRun, pose, ref accumulated);
        }

        if (wRun > 0.5f && _run is not null)
            PhaseEvents(_run, phase0, phase1);
        else if (wWalk >= wIdle && wWalk > 0 && _walk is not null)
            PhaseEvents(_walk, phase0, phase1);

        // Full-body one-shot.
        if (_full.Clip is not null)
        {
            var clip = _full.Clip;
            if (fullWeight > 0.001f)
            {
                _skeleton.RestPose.CopyTo(_fbPose);
                clip.Sample(_full.SampleTime, _fbPose);
                for (var i = 0; i < pose.Length; i++)
                    pose[i] = NodePose.Blend(pose[i], _fbPose[i], fullWeight);
            }

            _full.Advance(dt, out var t0, out var t1);
            ClipEvents(clip, t0, t1);
        }

        // Fading out of the snapshot taken when one full-body clip replaced another.
        if (_fromFade > 0)
        {
            var x = float.Clamp(_fromAge / _fromFade, 0f, 1f);
            var t = x * x * (3 - 2 * x);
            for (var i = 0; i < pose.Length; i++)
                pose[i] = NodePose.Blend(_from[i], pose[i], t);
            _fromAge += dt;
            if (_fromAge >= _fromFade)
                _fromFade = 0;
        }

        // Overlay, bone by bone.
        if (_overlay.Clip is not null)
        {
            var clip = _overlay.Clip;
            var w = _overlay.Weight();
            var mask = _overlay.Mask!;
            if (w > 0.001f)
            {
                _skeleton.RestPose.CopyTo(_ovPose);
                clip.Sample(_overlay.SampleTime, _ovPose);
                for (var i = 0; i < pose.Length; i++)
                {
                    var m = mask[i] * w;
                    if (m > 0.001f)
                        pose[i] = NodePose.Blend(pose[i], _ovPose[i], m);
                }
            }

            _overlay.Advance(dt, out var t0, out var t1);
            ClipEvents(clip, t0, t1);
        }

        pose.CopyTo(_last);
        _hasLast = true;
    }

    /// <summary>Blends one clip into the pose with its share of what's been added so far.</summary>
    private void Layer(AnimationClip? clip, float time, float weight, Span<NodePose> pose, ref float accumulated)
    {
        if (clip is null || weight <= 0.0005f)
            return;
        if (accumulated <= 0f)
        {
            clip.Sample(time, pose);
        }
        else
        {
            _skeleton.RestPose.CopyTo(_tmp);
            clip.Sample(time, _tmp);
            var t = weight / (accumulated + weight);
            for (var i = 0; i < pose.Length; i++)
                pose[i] = NodePose.Blend(pose[i], _tmp[i], t);
        }

        accumulated += weight;
    }

    /// <summary>Events of a looping cycle crossed while the phase went from <paramref name="p0"/> to <paramref name="p1"/> (p1 may pass 1).</summary>
    private void PhaseEvents(AnimationClip clip, float p0, float p1)
    {
        var events = clip.Info.Events;
        if (events.Length == 0 || clip.Duration <= 0)
            return;
        foreach (var (name, time) in events)
        {
            var e = time / clip.Duration;
            if (float.Floor(p1 - e) > float.Floor(p0 - e))
                _events.Add(name);
        }
    }

    private void ClipEvents(AnimationClip clip, float t0, float t1)
    {
        var events = clip.Info.Events;
        if (events.Length == 0 || clip.Duration <= 0)
            return;
        if (clip.Info.Loop)
        {
            PhaseEvents(clip, t0 / clip.Duration - float.Floor(t0 / clip.Duration), t1 / clip.Duration - float.Floor(t0 / clip.Duration));
            return;
        }

        foreach (var (name, time) in events)
        {
            if (time > t0 && time <= t1)
                _events.Add(name);
        }
    }
}
