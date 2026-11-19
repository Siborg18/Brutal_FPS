using Brutal.OpenGlApi;

namespace BrutalFps.Engine;

/// <summary>
/// How long the graphics card really took over each frame, by OpenGL timer queries. The answer turns up a few frames later,
/// and is only asked for once it's there, so this never waits for the card. It's what tells a frame the card was busy with from
/// one where "swap" only took long because the driver or Windows held it up.
/// </summary>
internal sealed class GpuTimer
{
    private const uint TimeElapsed = 0x88BF, QueryResult = 0x8866, QueryResultAvailable = 0x8867;
    private const int Slots = 12;

    private readonly GlQuery[] _queries = new GlQuery[Slots];
    private readonly long[] _frame = new long[Slots];
    private readonly bool[] _waiting = new bool[Slots];
    private int _next, _oldest;
    private bool _open;

    public GpuTimer() => OpenGl.GenQueries(new ReadOnlySpan<GlQuery>(_queries));

    public void Begin(long frame)
    {
        if (_open || _waiting[_next])
            return;   // (all the slots still waiting: this frame goes unmeasured)
        OpenGl.BeginQuery(TimeElapsed, _queries[_next]);
        _frame[_next] = frame;
        _open = true;
    }

    public void End()
    {
        if (!_open)
            return;
        OpenGl.EndQuery(TimeElapsed);
        _waiting[_next] = true;
        _next = (_next + 1) % Slots;
        _open = false;
    }

    /// <summary>Hands over the results that have come in: (frame number, milliseconds).</summary>
    public void Collect(Action<long, float> result)
    {
        Span<int> available = [0];
        Span<ulong> nanoseconds = [0];
        while (_waiting[_oldest])
        {
            var id = _queries[_oldest].Id;
            OpenGl.GetQueryObjectiv(id, QueryResultAvailable, available);
            if (available[0] == 0)
                return;
            OpenGl.GetQueryObjectui64v(id, QueryResult, nanoseconds);
            result(_frame[_oldest], nanoseconds[0] / 1e6f);
            _waiting[_oldest] = false;
            _oldest = (_oldest + 1) % Slots;
        }
    }
}
