namespace BrutalFps.Engine;

/// <summary>
/// Makes a short mono sound in code (48 kHz), for what there's no recording of yet: footsteps, bullets striking things,
/// a ricochet's whine. A sound is built by adding layers into a buffer: bursts of band-limited noise (a scuff, a
/// crack, a thud), struck resonances (decaying sines, optionally sweeping: a clang, a whine, a bubble) and crackle
/// (scattered tiny clicks: grit, splinters, snow crunching, glass tinkling). Seeded, so the same recipe always gives
/// the same sound. <see cref="ToWav"/> normalises it and hands it to the <see cref="AudioEngine"/>.
/// </summary>
public sealed class SoundSynth
{
    public const int Rate = 48000;

    private readonly float[] _buffer;
    private readonly Random _random;

    public SoundSynth(float seconds, int seed)
    {
        _buffer = new float[int.Max(1, (int)(seconds * Rate))];
        _random = new Random(seed);
    }

    /// <summary>A random number in [0, 1), from this sound's seed (to vary a recipe between takes).</summary>
    public float Random01() => (float)_random.NextDouble();

    /// <summary><paramref name="value"/> varied by up to +-<paramref name="spread"/> (a fraction: 0.1 = 10%).</summary>
    public float Vary(float value, float spread) => value * (1f + spread * (2f * Random01() - 1f));

    /// <summary>
    /// White noise filtered to <paramref name="lowCut"/>-<paramref name="highCut"/> Hz, rising over
    /// <paramref name="attack"/> s from <paramref name="start"/> s and then dying away (time constant
    /// <paramref name="decay"/> s).
    /// </summary>
    public SoundSynth Noise(float start, float decay, float gain, float lowCut, float highCut, float attack = 0.0008f)
    {
        var from = (int)(start * Rate);
        var count = int.Min(_buffer.Length - from, (int)((attack + decay * 7f) * Rate));
        if (count <= 0)
            return this;
        var high = Biquad.HighPass(lowCut);
        var low = Biquad.LowPass(highCut);
        var low2 = Biquad.LowPass(highCut);
        for (var i = 0; i < count; i++)
        {
            var t = i / (float)Rate;
            var env = t < attack ? t / attack : float.Exp(-(t - attack) / decay);
            var x = low2.Run(low.Run(high.Run(2f * Random01() - 1f)));
            _buffer[from + i] += x * env * gain;
        }

        return this;
    }

    /// <summary>
    /// A struck resonance: a sine at <paramref name="frequency"/> Hz dying away (time constant <paramref name="decay"/>
    /// s), gliding to <paramref name="endFrequency"/> over its length if given (a whine, a bubble), with an optional
    /// wobble (<paramref name="vibratoHz"/>, <paramref name="vibratoDepth"/> as a fraction of the pitch).
    /// </summary>
    public SoundSynth Tone(float start, float frequency, float decay, float gain, float endFrequency = 0f, float vibratoHz = 0f,
        float vibratoDepth = 0f, float attack = 0.0005f)
    {
        var from = (int)(start * Rate);
        var length = attack + decay * 7f;
        var count = int.Min(_buffer.Length - from, (int)(length * Rate));
        if (count <= 0)
            return this;
        var phase = Random01() * 2f * float.Pi;
        var glide = endFrequency > 0f ? float.Log(endFrequency / frequency) : 0f;
        for (var i = 0; i < count; i++)
        {
            var t = i / (float)Rate;
            var f = frequency * float.Exp(glide * t / length);
            if (vibratoHz > 0f)
                f *= 1f + vibratoDepth * float.Sin(2f * float.Pi * vibratoHz * t);
            phase += 2f * float.Pi * f / Rate;
            var env = t < attack ? t / attack : float.Exp(-(t - attack) / decay);
            _buffer[from + i] += float.Sin(phase) * env * gain;
        }

        return this;
    }

    /// <summary>
    /// <paramref name="count"/> tiny clicks scattered over <paramref name="span"/> s from <paramref name="start"/>
    /// (thinning out towards the end), each a little noise burst in <paramref name="lowCut"/>-<paramref name="highCut"/>
    /// Hz: grit underfoot, chips falling, splinters, snow.
    /// </summary>
    public SoundSynth Crackle(float start, float span, int count, float gain, float lowCut, float highCut, float grain = 0.002f)
    {
        for (var i = 0; i < count; i++)
        {
            var u = Random01();
            var at = start + span * u * u;   // bunched towards the start
            Noise(at, grain, gain * (0.3f + 0.7f * Random01()) * (1f - 0.6f * u), lowCut, highCut, 0.0002f);
        }

        return this;
    }

    /// <summary>
    /// <paramref name="count"/> short bright pings between <paramref name="lowHz"/> and <paramref name="highHz"/>
    /// scattered over <paramref name="span"/> s: shards of glass, a casing's tinkle.
    /// </summary>
    public SoundSynth Tinkle(float start, float span, int count, float gain, float lowHz, float highHz, float decay = 0.04f)
    {
        for (var i = 0; i < count; i++)
        {
            var u = Random01();
            Tone(start + span * u * u, lowHz + (highHz - lowHz) * Random01(), decay * (0.5f + Random01()), gain * (0.3f + 0.7f * Random01()) * (1f - 0.5f * u));
        }

        return this;
    }

    /// <summary>The sound, scaled so its loudest sample is <paramref name="peak"/>, with the last 10 ms faded out.</summary>
    public WavFile ToWav(float peak = 0.9f)
    {
        var max = 1e-6f;
        foreach (var x in _buffer)
            max = float.Max(max, float.Abs(x));
        var scale = peak / max;
        var fade = int.Min(_buffer.Length, Rate / 100);
        var samples = new short[_buffer.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var x = _buffer[i] * scale;
            var left = _buffer.Length - i;
            if (left < fade)
                x *= left / (float)fade;
            samples[i] = (short)float.Clamp(float.Round(x * 32767f), -32768f, 32767f);
        }

        return new WavFile { Samples = samples, Channels = 1, SampleRate = Rate, SourceBits = 0 };
    }

    /// <summary>A two-pole filter (RBJ's cookbook), Butterworth Q.</summary>
    private struct Biquad
    {
        private float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

        public static Biquad LowPass(float hz) => Make(hz, low: true);
        public static Biquad HighPass(float hz) => Make(hz, low: false);

        private static Biquad Make(float hz, bool low)
        {
            var w = 2f * float.Pi * float.Clamp(hz, 10f, Rate * 0.45f) / Rate;
            var alpha = float.Sin(w) / (2f * 0.7071f);
            var cos = float.Cos(w);
            var a0 = 1f + alpha;
            var b1 = low ? 1f - cos : -(1f + cos);
            var b0 = low ? b1 / 2f : (1f + cos) / 2f;
            return new Biquad { _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b0 / a0, _a1 = -2f * cos / a0, _a2 = (1f - alpha) / a0 };
        }

        public float Run(float x)
        {
            var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;
            return y;
        }
    }
}
