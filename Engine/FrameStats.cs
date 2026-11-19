using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Brutal.OpenGlApi;

namespace BrutalFps.Engine;

/// <summary>What's going on in the world, for a report on a bad frame (and once a second for the .frames.csv).</summary>
public readonly record struct WorldCounts(
    string Zone, string Mode, float X, float Y, float Z,
    int People, int Fighting, int Ragdolls, int Bodies, int Debris, int Particles, int Bullets, int Holes, int Flares, int Voices);

/// <summary>
/// Where the frames go. <see cref="Mark"/> ends a section of the frame and <see cref="PerfTimer"/>s time what's inside the
/// sections. Always on, unless BRUTALFPS_DIAG=0 (cheap enough to leave on: no garbage per frame):
/// <list type="bullet">
/// <item>A frame well over what the frames have been taking gets a <c>[HITCH]</c> report in the log: what took the time, whose
/// fault it looks like (ours, the garbage collector's, the JIT's, the graphics card's, or Windows'), what was going on in the
/// world and what was logged that frame. BRUTALFPS_HITCH_MS sets the smallest (default 40).</item>
/// <item><see cref="HitchWatchdog"/> notes a frame that's stuck while it's stuck (<c>[STALL]</c>).</item>
/// <item>Once a second, a row in Logs\*.frames.csv; a second that's slow overall also gets a <c>[SLOW]</c> line.</item>
/// <item>At exit, a <c>[SUMMARY]</c>: how the run went, and the causes of its hitches, worst first.</item>
/// </list>
/// BRUTALFPS_PERF=1 adds the averages per section at exit (<c>[PERF]</c>).
/// </summary>
public static class FrameStats
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("BRUTALFPS_PERF") == "1";

    /// <summary>The recorder, the hitch reports and the timers: off only with BRUTALFPS_DIAG=0.</summary>
    public static readonly bool Active = Environment.GetEnvironmentVariable("BRUTALFPS_DIAG") != "0";

    /// <summary>Draw calls so far this frame.</summary>
    public static int DrawCalls;

    /// <summary>The game's thread: the one that started the game.</summary>
    public static readonly int GameThread = Environment.CurrentManagedThreadId;

    /// <summary>Says what's going on in the world (set by the game once it's all built).</summary>
    public static Func<WorldCounts>? World;

    private static readonly double HitchMs = double.TryParse(Environment.GetEnvironmentVariable("BRUTALFPS_HITCH_MS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var hitch) ? hitch : 40.0;
    private const double HitchFactor = 2.5;      // ...or this many times what the frames have been taking, whichever is more
    private const double SlowSecondMs = 34.0;    // a second averaging worse than this (under 30 fps) gets a line of its own
    private const int WarmUpFrames = 30;         // the JIT warming up: reported as such, and left out of the averages
    private const int ReportDelay = 8;           // frames after a hitch before it's written: the GPU's time has come in, and the frames after it
    private const int ReportsPerWindow = 6;      // reports in full per WindowSeconds; the rest get one line
    private const double WindowSeconds = 5.0;
    private const int RingSize = 256;
    private const int MaxSections = 40, MaxTimers = 48, MaxLines = 6;

    private sealed class Section
    {
        public double Total, Worst, Current, ThisSecond;
        public int Index;
    }

    private sealed class FrameRecord
    {
        public long Number = -1;
        public double Seconds;
        public float Ms, CpuMs = -1f, GpuMs = -1f, GcPauseMs, JitMs, AllocatedKb;
        public int Gen0, Gen1, Gen2, JitMethods, Draws, LineCount, LinesDropped, Exceptions;
        public readonly float[] Sections = new float[MaxSections];
        public readonly float[] TimerMs = new float[MaxTimers];
        public readonly float[] TimerWorstMs = new float[MaxTimers];
        public readonly int[] TimerCalls = new int[MaxTimers];
        public readonly string?[] TimerWho = new string?[MaxTimers];
        public readonly string[] Lines = new string[MaxLines];
    }

    private sealed record Pending(long Frame, long ReportAt, double Baseline, WorldCounts? World, bool WarmingUp);

    private static readonly List<(string Name, Section Times)> Sections = [];
    private static readonly Dictionary<string, Section> ByName = [];
    private static readonly List<PerfTimer> Timers = [];
    private static readonly List<double> Frames = [];
    private static readonly FrameRecord[] Ring = NewRing();
    private static readonly List<Pending> PendingReports = [];
    private static readonly Dictionary<string, (int Count, double Worst, double Total)> Causes = [];
    private static readonly List<(double Ms, double Seconds, string Cause, string Zone)> WorstHitches = [];
    private static readonly int[] Histogram = new int[1000];   // frames by whole milliseconds; the last holds everything over
    private static readonly Dictionary<string, int> ExceptionsSeen = [];
    private static readonly string[] LinesThisFrame = new string[MaxLines];
    private static readonly long[] TimerSecondTicks = new long[MaxTimers];

    private static long _last = Stopwatch.GetTimestamp();
    private static long _frameStart = _last;
    private static long _playStart = _last;
    private static long _drawCalls, _maxDrawCalls;
    private static long _startBytes;
    private static readonly int[] StartCollections = new int[3];
    private static int _skip = WarmUpFrames;
    private static double _worstFrame;
    private static string _worstFrameSections = "";

    private static long _frameNumber;
    private static double _baseline = 16.7;
    private static bool _playing;
    private static bool _reporting;
    private static GpuTimer? _gpu;

    // Per frame, for the differences.
    private static readonly int[] LastCollections = new int[3];
    private static long _lastAllocated;
    private static TimeSpan _lastPause, _lastJit;
    private static long _lastJitMethods;
    private static long _lastCpu = -1;
    private static int _linesThisFrame, _linesDropped, _exceptionsThisFrame;

    // Limiting the reports.
    private static double _windowStart;
    private static int _reportsThisWindow, _shortLinesThisWindow;
    private const int ShortLinesPerWindow = 20;

    // For the watchdog: read from its thread.
    private static long _watchedFrameStart;
    private static long _watchedFrame;
    private static string _afterSection = "start of the frame";
    private static readonly string[] Stack = new string[16];
    private static int _depth;

    // The current second.
    private static long _nextSecond;
    private static int _secFrames, _secHitches, _secGpuFrames, _secDraws;
    private static double _secTotalMs, _secMaxMs, _secGpuMs, _secPauseMs;
    private static long _secAllocated;
    private static readonly int[] SecCollections = new int[3];
    private static long _sysIdle, _sysKernel, _sysUser;
    private static bool _csvHeaderWritten;
    private static string[] _csvSections = [];
    private static int _glErrorsLogged;
    private static int _hitchCount;
    private static long _gcHeapAtStart;

    // A test of all this: BRUTALFPS_TESTHITCH=frame sets off one of each kind of hitch from that frame on.
    private static readonly long TestFrom = long.TryParse(Environment.GetEnvironmentVariable("BRUTALFPS_TESTHITCH"), out var from) ? from : -1;

    private static object[] _testJunk = [];

    private static FrameRecord[] NewRing()
    {
        var ring = new FrameRecord[RingSize];
        for (var i = 0; i < RingSize; i++)
            ring[i] = new FrameRecord();
        return ring;
    }

    private static string I(FormattableString text) => FormattableString.Invariant(text);

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    internal static int Register(PerfTimer timer)
    {
        Timers.Add(timer);
        if (Timers.Count > MaxTimers)
            throw new InvalidOperationException($"More than {MaxTimers} PerfTimers: raise FrameStats.MaxTimers.");
        return Timers.Count - 1;
    }

    /// <summary>Everything's built and the game's about to start: the recording starts here, not during the loading.</summary>
    public static void BeginPlay(string graphics)
    {
        if (!Active)
            return;
        RuntimeHelpers.RunClassConstructor(typeof(Perf).TypeHandle);
        foreach (var timer in Timers)
            (timer.Ticks, timer.WorstTicks, timer.Calls, timer.WorstWho) = (0, 0, 0, null);

        var build =
#if DEBUG
            "Debug";
#elif STRESS_TEST
            "StressTest";
#else
            "Release";
#endif
        Log.Write(I($"[DIAG] Recording this run: {Log.SessionPath}"));
        Log.Write(I($"[DIAG] {build} build, .NET {Environment.Version}, {Environment.OSVersion.VersionString}, {Environment.ProcessorCount} logical CPUs, debugger {(Debugger.IsAttached ? "ATTACHED (expect slower frames)" : "not attached")}"));
        Log.Write(I($"[DIAG] GC: {(GCSettings.IsServerGC ? "server" : "workstation")}, latency {GCSettings.LatencyMode}, tiered PGO {AppContext.TryGetSwitch("System.Runtime.TieredPGO", out var pgo) && pgo}"));
        Log.Write(I($"[DIAG] {graphics}; a hitch is a frame over {HitchMs:0} ms and over {HitchFactor:0.0} x the recent average"));

        try
        {
            _gpu = new GpuTimer();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Write($"[DIAG] No GPU timing ({e.GetType().Name}: {e.Message})");
        }

        for (var g = 0; g < 3; g++)
            SecCollections[g] = LastCollections[g] = GC.CollectionCount(g);
        _lastAllocated = _secAllocated = GC.GetTotalAllocatedBytes();
        _lastPause = GC.GetTotalPauseDuration();
        _lastJit = JitInfo.GetCompilationTime(currentThread: true);
        _lastJitMethods = JitInfo.GetCompiledMethodCount(currentThread: true);
        _lastCpu = ThreadCpuTicks();
        _gcHeapAtStart = GC.GetTotalMemory(false);
        SystemTimes(out _sysIdle, out _sysKernel, out _sysUser);

        Log.LineObserver = NoteLine;
        Log.Crashing = CrashReport;
        AppDomain.CurrentDomain.FirstChanceException += (_, e) => NoteException(e.Exception);

        var now = Stopwatch.GetTimestamp();
        _playStart = _frameStart = _last = now;
        _nextSecond = now + Stopwatch.Frequency;
        Volatile.Write(ref _watchedFrameStart, now);
        _playing = true;
        HitchWatchdog.Start();
    }

    // ------------------------------------------------------------------------------------------------ during the frame

    public static void Mark(string name)
    {
        if (!Active)
            return;
        var now = Stopwatch.GetTimestamp();
        if (!ByName.TryGetValue(name, out var section))
        {
            ByName[name] = section = new Section { Index = Sections.Count };
            Sections.Add((name, section));
        }
        section.Current += Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds;
        _last = now;
        Volatile.Write(ref _afterSection, name);
    }

    internal static void EnterTimer(string name)
    {
        if (_depth < Stack.Length)
            Stack[_depth] = name;
        Volatile.Write(ref _depth, _depth + 1);
    }

    internal static void LeaveTimer() => Volatile.Write(ref _depth, _depth - 1);

    public static void BeginGpu()
    {
        if (Active)
            _gpu?.Begin(_frameNumber);
    }

    public static void EndGpu()
    {
        if (Active)
            _gpu?.End();
    }

    private static void NoteLine(string line)
    {
        if (_reporting || Environment.CurrentManagedThreadId != GameThread)
            return;
        if (_linesThisFrame < MaxLines)
            LinesThisFrame[_linesThisFrame++] = line;
        else
            _linesDropped++;
    }

    [ThreadStatic] private static bool _inExceptionNote;

    /// <summary>Every exception, caught or not: each one thrown costs real time, and one that's being swallowed can be a bug. The first few of each kind are logged.</summary>
    private static void NoteException(Exception exception)
    {
        if (_inExceptionNote)
            return;
        _inExceptionNote = true;
        try
        {
            var type = exception.GetType().Name;
            int seen;
            lock (ExceptionsSeen)
            {
                ExceptionsSeen.TryGetValue(type, out seen);
                ExceptionsSeen[type] = ++seen;
            }

            if (Environment.CurrentManagedThreadId == GameThread)
                _exceptionsThisFrame++;
            if (seen <= 3)
                Log.Write($"[EXCEPTION] {type}: {exception.Message} (at {exception.TargetSite?.DeclaringType?.Name}.{exception.TargetSite?.Name}; thread {Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString()}, {seen} of this kind so far)");
        }
        finally
        {
            _inExceptionNote = false;
        }
    }

    // ------------------------------------------------------------------------------------------------ the end of a frame

    public static void EndFrame()
    {
        if (!Active)
            return;
        var now = Stopwatch.GetTimestamp();
        var ms = Stopwatch.GetElapsedTime(_frameStart, now).TotalMilliseconds;
        _frameStart = now;
        _last = now;

        var record = Ring[_frameNumber % RingSize];
        Fill(record, ms, now);
        _gpu?.Collect(GpuResult);

        // The old per-section averages (BRUTALFPS_PERF=1).
        var warm = _skip > 0;
        if (warm)
        {
            if (--_skip == 0)
            {
                _startBytes = GC.GetTotalAllocatedBytes();
                for (var g = 0; g < 3; g++)
                    StartCollections[g] = GC.CollectionCount(g);
            }
        }
        else
        {
            if (ms > _worstFrame)
            {
                _worstFrame = ms;
                _worstFrameSections = string.Join(", ", Sections.Where(x => x.Times.Current > 1).OrderByDescending(x => x.Times.Current).Select(x => $"{x.Name} {x.Times.Current:0.0}"));
            }

            foreach (var (_, s) in Sections)
            {
                s.Total += s.Current;
                s.Worst = double.Max(s.Worst, s.Current);
            }

            if (Enabled)
                Frames.Add(ms);
            _drawCalls += DrawCalls;
            _maxDrawCalls = long.Max(_maxDrawCalls, DrawCalls);
            Histogram[int.Min((int)ms, Histogram.Length - 1)]++;
        }

        foreach (var (_, s) in Sections)
        {
            s.ThisSecond += s.Current;
            s.Current = 0;
        }

        _secFrames++;
        _secTotalMs += ms;
        _secMaxMs = double.Max(_secMaxMs, ms);
        _secPauseMs += record.GcPauseMs;
        _secDraws += DrawCalls;
        DrawCalls = 0;

        // A hitch? (The yardstick moves with what the frames have been taking, or a long slow patch would be one long report.)
        if (_playing)
        {
            var limit = double.Max(HitchMs, _baseline * HitchFactor);
            if (ms >= limit)
            {
                _hitchCount++;
                _secHitches++;
                PendingReports.Add(new Pending(_frameNumber, _frameNumber + ReportDelay, _baseline, SnapshotWorld(), warm));
            }

            if (!warm)
                _baseline += (double.Min(ms, limit) - _baseline) * 0.03;
        }

        _frameNumber++;
        Volatile.Write(ref _watchedFrame, _frameNumber);
        Volatile.Write(ref _watchedFrameStart, now);
        Volatile.Write(ref _afterSection, "start of the frame");

        while (PendingReports.Count > 0 && PendingReports[0].ReportAt <= _frameNumber)
        {
            var due = PendingReports[0];
            PendingReports.RemoveAt(0);
            ReportHitch(due);
        }

        if (now >= _nextSecond)
            EndSecond(now);
    }

    private static void Fill(FrameRecord r, double ms, long now)
    {
        r.Number = _frameNumber;
        r.Seconds = Ms(now - _playStart) / 1000.0;
        r.Ms = (float)ms;
        r.GpuMs = -1f;
        foreach (var (_, s) in Sections)
            r.Sections[s.Index] = (float)s.Current;
        foreach (var timer in Timers)
        {
            var i = timer.Index;
            r.TimerMs[i] = (float)Ms(timer.Ticks);
            r.TimerWorstMs[i] = (float)Ms(timer.WorstTicks);
            r.TimerCalls[i] = timer.Calls;
            r.TimerWho[i] = timer.WorstWho;
            TimerSecondTicks[i] += timer.Ticks;
            (timer.Ticks, timer.WorstTicks, timer.Calls, timer.WorstWho) = (0, 0, 0, null);
        }

        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        (r.Gen0, r.Gen1, r.Gen2) = (gen0 - LastCollections[0], gen1 - LastCollections[1], gen2 - LastCollections[2]);
        (LastCollections[0], LastCollections[1], LastCollections[2]) = (gen0, gen1, gen2);

        var allocated = GC.GetTotalAllocatedBytes();
        r.AllocatedKb = (allocated - _lastAllocated) / 1024f;
        _lastAllocated = allocated;

        var pause = GC.GetTotalPauseDuration();
        r.GcPauseMs = (float)(pause - _lastPause).TotalMilliseconds;
        _lastPause = pause;

        var jit = JitInfo.GetCompilationTime(currentThread: true);
        r.JitMs = (float)(jit - _lastJit).TotalMilliseconds;
        _lastJit = jit;
        var methods = JitInfo.GetCompiledMethodCount(currentThread: true);
        r.JitMethods = (int)(methods - _lastJitMethods);
        _lastJitMethods = methods;

        var cpu = ThreadCpuTicks();
        r.CpuMs = cpu >= 0 && _lastCpu >= 0 ? (cpu - _lastCpu) / 10000f : -1f;
        _lastCpu = cpu;

        r.Draws = DrawCalls;
        r.LineCount = _linesThisFrame;
        r.LinesDropped = _linesDropped;
        r.Exceptions = _exceptionsThisFrame;
        Array.Copy(LinesThisFrame, r.Lines, _linesThisFrame);
        _linesThisFrame = _linesDropped = _exceptionsThisFrame = 0;
    }

    private static readonly Action<long, float> GpuResult = (frame, ms) =>
    {
        var record = Ring[frame % RingSize];
        if (record.Number == frame)
            record.GpuMs = ms;
        _secGpuMs += ms;
        _secGpuFrames++;
    };

    private static WorldCounts? SnapshotWorld()
    {
        try
        {
            return World?.Invoke();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;   // (a report on a bad frame mustn't be the thing that brings the game down)
        }
    }

    // ------------------------------------------------------------------------------------------------ reporting a hitch

    private static (string Kind, string Detail) WhatCausedIt(FrameRecord r, double baseline)
    {
        var excess = double.Max(r.Ms - baseline, 1.0);
        var bar = excess * 0.4;

        if (r.GcPauseMs >= bar)
        {
            var info = GC.GetGCMemoryInfo(GCKind.Any);
            return ("garbage collection", I($"gen{(r.Gen2 > 0 ? 2 : r.Gen1 > 0 ? 1 : 0)} collection paused the game {r.GcPauseMs:0.0} ms (last one: gen{info.Generation}, {(info.Compacted ? "compacting" : "not compacting")}, {(info.Concurrent ? "background" : "blocking")}, heap {info.HeapSizeBytes / 1048576.0:0} MB)"));
        }

        if (r.JitMs >= bar)
            return ("JIT compiling", I($"{r.JitMethods} methods compiled for the first time on the game's thread, {r.JitMs:0.0} ms"));

        // The biggest timer that's something in its own right, then failing that the biggest that holds others.
        PerfTimer? best = null;
        foreach (var container in (ReadOnlySpan<bool>)[false, true])
        {
            foreach (var timer in Timers)
            {
                if (timer.Container == container && r.TimerMs[timer.Index] >= bar && (best is null || r.TimerMs[timer.Index] > r.TimerMs[best.Index]))
                    best = timer;
            }

            if (best is not null)
                break;
        }

        if (best == Perf.Swap)
        {
            if (r.GpuMs >= 0 && r.GpuMs >= r.TimerMs[best.Index] * 0.5f)
                return ("graphics card", I($"the GPU took {r.GpuMs:0.0} ms over this frame"));
            return ("driver or Windows", I($"the buffer swap took {r.TimerMs[best.Index]:0.0} ms while the GPU only worked {(r.GpuMs >= 0 ? I($"{r.GpuMs:0.0} ms") : "(not measured)")}: the driver or the compositor held it up"));
        }

        if (best == Perf.Poll)
            return ("window or Windows", I($"handling the window's messages took {r.TimerMs[best.Index]:0.0} ms (dragging or resizing the window, losing focus, or an input device stalling)"));

        if (best is not null)
        {
            var i = best.Index;
            var who = r.TimerWho[i] is { } w ? I($", worst single call {r.TimerWorstMs[i]:0.0} ms: {w}") : "";
            return (best.Name, I($"{r.TimerMs[i]:0.0} ms in {r.TimerCalls[i]} call{(r.TimerCalls[i] == 1 ? "" : "s")}{who}"));
        }

        // No timer explains it. Was the thread even running?
        if (r.CpuMs >= 0 && r.CpuMs < r.Ms * 0.3f && r.Ms >= 50f)
            return ("thread held up", I($"the game's thread only ran for {r.CpuMs:0} ms of the {r.Ms:0} ms: waiting on the disk, a lock or the driver, or Windows gave the CPU to something else"));

        var section = Sections.OrderByDescending(x => r.Sections[x.Times.Index]).FirstOrDefault();
        return section.Name is null ? ("unknown", "") : ("section: " + section.Name, I($"{r.Sections[section.Times.Index]:0.0} ms, with nothing timed inside it big enough to blame"));
    }

    private static string Severity(double ms) => ms >= 500 ? "FREEZE" : ms >= 100 ? "SPIKE" : "STUTTER";

    private static void ReportHitch(Pending due)
    {
        var r = Ring[due.Frame % RingSize];
        if (r.Number != due.Frame)
            return;

        var (kind, detail) = WhatCausedIt(r, due.Baseline);
        var warm = due.WarmingUp;
        Causes.TryGetValue(kind, out var tally);
        Causes[kind] = (tally.Count + 1, double.Max(tally.Worst, r.Ms), tally.Total + r.Ms);
        WorstHitches.Add((r.Ms, r.Seconds, kind, due.World?.Zone ?? "?"));
        if (WorstHitches.Count > 10)
        {
            WorstHitches.Sort((a, b) => b.Ms.CompareTo(a.Ms));
            WorstHitches.RemoveAt(10);
        }

        if (r.Seconds - _windowStart >= WindowSeconds)
        {
            if (_shortLinesThisWindow >= ShortLinesPerWindow)
                Log.Write("[HITCH] (more hitches in that patch weren't logged: see the .frames.csv)");
            (_windowStart, _reportsThisWindow, _shortLinesThisWindow) = (r.Seconds, 0, 0);
        }

        _reporting = true;
        try
        {
            if (_reportsThisWindow >= ReportsPerWindow)
            {
                if (_shortLinesThisWindow++ < ShortLinesPerWindow)
                    Log.Write(I($"[HITCH] {Severity(r.Ms)} {r.Ms:0.0} ms, frame {r.Number}: {kind}: {detail}"));
                return;
            }

            _reportsThisWindow++;
            var text = new StringBuilder();
            text.Append(I($"[HITCH] {Severity(r.Ms)}{(warm ? " (warming up)" : "")} {r.Ms:0.0} ms, frame {r.Number}, {r.Seconds:0.00} s into play (the frames had been taking {due.Baseline:0.0} ms)\n"));
            text.Append($"    Cause: {kind}: {detail}\n");

            var gpu = r.GpuMs >= 0 ? I($"{r.GpuMs:0.0} ms") : "n/a";
            var cpu = r.CpuMs >= 0 ? I($"{r.CpuMs:0} ms") : "n/a";
            text.Append(I($"    Game thread CPU {cpu} of {r.Ms:0} ms, GPU {gpu}, GC pause {r.GcPauseMs:0.0} ms (collections {r.Gen0}/{r.Gen1}/{r.Gen2}), JIT {r.JitMs:0.0} ms ({r.JitMethods} methods), allocated {r.AllocatedKb:0.0} KB, {r.Draws} draw calls, {r.Exceptions} exceptions\n"));

            var sections = Sections.Where(x => r.Sections[x.Times.Index] >= 0.5f).OrderByDescending(x => r.Sections[x.Times.Index]).Select(x => I($"{x.Name} {r.Sections[x.Times.Index]:0.0}"));
            text.Append($"    Sections (ms): {string.Join(", ", sections)}\n");

            var timers = Timers.Where(t => r.TimerMs[t.Index] >= 0.5f).OrderByDescending(t => r.TimerMs[t.Index]).Select(t =>
                I($"{t.Name} {r.TimerMs[t.Index]:0.0} x{r.TimerCalls[t.Index]}") + (r.TimerWho[t.Index] is { } who ? $" ({who})" : ""));
            text.Append($"    Timers (ms): {string.Join(", ", timers)}\n");

            if (due.World is { } w)
            {
                text.Append(I($"    World: {w.Mode}, {w.Zone} at ({w.X:0.0}, {w.Y:0.0}, {w.Z:0.0}); people {w.People} ({w.Fighting} fighting, {w.Ragdolls} ragdolls), bodies {w.Bodies}, debris {w.Debris}, particles {w.Particles}, bullets {w.Bullets}, holes {w.Holes}, flares {w.Flares}, voices {w.Voices}\n"));
            }

            if (r.LineCount > 0)
            {
                text.Append("    Logged this frame:\n");
                for (var i = 0; i < r.LineCount; i++)
                    text.Append("      ").Append(r.Lines[i].Length > 140 ? r.Lines[i][..140] + "..." : r.Lines[i]).Append('\n');
                if (r.LinesDropped > 0)
                    text.Append(I($"      (and {r.LinesDropped} more)\n"));
            }

            text.Append("    Frames round it (ms): ");
            for (var n = due.Frame - 8; n <= due.Frame + ReportDelay - 1; n++)
            {
                if (n < 0)
                    continue;
                var other = Ring[n % RingSize];
                if (other.Number != n)
                    continue;
                text.Append(n == due.Frame ? I($"[{other.Ms:0.0}] ") : I($"{other.Ms:0.0} "));
            }

            Log.Write(text.ToString().TrimEnd());
        }
        finally
        {
            _reporting = false;
        }
    }

    // ------------------------------------------------------------------------------------------------ once a second

    private static void EndSecond(long now)
    {
        _nextSecond = now + Stopwatch.Frequency;
        var frames = int.Max(_secFrames, 1);
        var world = SnapshotWorld();
        var averageMs = _secTotalMs / frames;

        var allocated = GC.GetTotalAllocatedBytes();
        var gen0 = GC.CollectionCount(0) - SecCollections[0];
        var gen1 = GC.CollectionCount(1) - SecCollections[1];
        var gen2 = GC.CollectionCount(2) - SecCollections[2];
        SecCollections[0] += gen0;
        SecCollections[1] += gen1;
        SecCollections[2] += gen2;

        var system = -1.0;
        if (SystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel - _sysKernel + (user - _sysUser);   // (kernel time includes the idle time)
            if (total > 0)
                system = 100.0 * (1.0 - (double)(idle - _sysIdle) / total);
            (_sysIdle, _sysKernel, _sysUser) = (idle, kernel, user);
        }

        if (!_csvHeaderWritten)
        {
            _csvHeaderWritten = true;
            _csvSections = Sections.Select(x => x.Name).ToArray();
            Log.WriteCsv("seconds,frames,avg_ms,worst_ms,gpu_ms,hitches,alloc_mb,gen0,gen1,gen2,gc_pause_ms,heap_mb,working_set_mb,system_cpu_pct,draws,"
                         + "mode,zone,people,fighting,ragdolls,bodies,debris,particles,bullets,holes,flares,voices,"
                         + string.Join(",", _csvSections.Select(s => "s:" + s.Replace(',', ' ')))
                         + "," + string.Join(",", Timers.Select(t => "t:" + t.Name)));
        }

        var row = new StringBuilder();
        var w = world ?? default;
        row.Append(I($"{Ms(now - _playStart) / 1000.0:0.0},{_secFrames},{averageMs:0.00},{_secMaxMs:0.0},{(_secGpuFrames > 0 ? _secGpuMs / _secGpuFrames : -1):0.00},{_secHitches},"));
        row.Append(I($"{(allocated - _secAllocated) / 1048576.0:0.00},{gen0},{gen1},{gen2},{_secPauseMs:0.0},{GC.GetTotalMemory(false) / 1048576.0:0},{Environment.WorkingSet / 1048576:0},{system:0},{_secDraws / frames},"));
        row.Append(I($"{w.Mode},{w.Zone?.Replace(',', '+')},{w.People},{w.Fighting},{w.Ragdolls},{w.Bodies},{w.Debris},{w.Particles},{w.Bullets},{w.Holes},{w.Flares},{w.Voices}"));
        foreach (var name in _csvSections)
            row.Append(I($",{ByName[name].ThisSecond / frames:0.000}"));
        foreach (var timer in Timers)
            row.Append(I($",{Ms(TimerSecondTicks[timer.Index]) / frames:0.000}"));
        Log.WriteCsv(row.ToString());

        // A whole second that's slow is a different problem from one bad frame: it wants a line even with no frame standing out.
        if (averageMs > SlowSecondMs && w.Mode != "paused")
        {
            var top = Sections.OrderByDescending(x => x.Times.ThisSecond).Take(4).Select(x => I($"{x.Name} {x.Times.ThisSecond / frames:0.0}"));
            Log.Write(I($"[SLOW] That second averaged {averageMs:0.0} ms ({1000 / averageMs:0} fps), worst {_secMaxMs:0.0}, in {w.Zone} ({w.People} people, {w.Fighting} fighting): {string.Join(", ", top)} ms a frame"));
        }

        foreach (var (_, s) in Sections)
            s.ThisSecond = 0;
        Array.Clear(TimerSecondTicks);
        (_secFrames, _secHitches, _secTotalMs, _secMaxMs, _secGpuMs, _secGpuFrames, _secPauseMs, _secDraws) = (0, 0, 0, 0, 0, 0, 0, 0);
        _secAllocated = allocated;

        CheckGlErrors();
    }

    private static void CheckGlErrors()
    {
        for (var i = 0; i < 8; i++)
        {
            var error = (uint)OpenGl.GetError();
            if (error == 0)
                return;
            if (_glErrorsLogged++ < 40)
                Log.Write(I($"[GL] Error 0x{error:X4} (somewhere in the last second)"));
        }
    }

    // ------------------------------------------------------------------------------------------------ for the watchdog

    /// <summary>How long the game's thread has been on this frame, and what it's doing.</summary>
    internal static (long Frame, long FrameStart, string Where) Watch()
    {
        var frame = Volatile.Read(ref _watchedFrame);
        var start = Volatile.Read(ref _watchedFrameStart);
        var after = Volatile.Read(ref _afterSection);
        var depth = int.Min(Volatile.Read(ref _depth), Stack.Length);
        var where = depth > 0 ? string.Join(" > ", Stack.Take(depth)) : "no timer running";
        return (frame, start, $"after section '{after}', {where}");
    }

    internal static bool Started => _playing;

    // ------------------------------------------------------------------------------------------------ the end of the run

    /// <summary>Sets off one of each kind of hitch, to prove the reports work (BRUTALFPS_TESTHITCH=frame). Call once a frame.</summary>
    public static void RunTestHitches()
    {
        if (TestFrom < 0)
            return;
        var n = _frameNumber - TestFrom;
        if (n is not (0 or 120 or 200 or 240 or 360))
            return;
        using var _ = n == 240 ? default : Perf.TestSpike.Time(n switch { 0 => "sleeping", 120 => "busy", 200 => "making garbage to collect", _ => "long stall" });
        switch (n)
        {
            case 0:
                Thread.Sleep(300);
                break;
            case 120:
                var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3 / 10;
                while (Stopwatch.GetTimestamp() < until) { }
                break;
            case 200:
                // Millions of small objects: what makes a blocking collection slow is how many it has to trace and move.
                _testJunk = new object[5_000_000];
                for (var i = 0; i < _testJunk.Length; i++)
                    _testJunk[i] = new object();
                break;
            case 240:
                for (var i = 0; i < _testJunk.Length; i += 2)
                    _testJunk[i] = null!;
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                _testJunk = [];
                break;
            default:
                Thread.Sleep(1500);
                break;
        }
    }

    private static void CrashReport()
    {
        if (!_playing)
            return;
        var (_, _, where) = Watch();
        Log.Write($"[CRASH] It went down on frame {_frameNumber}, {where}");
        var world = SnapshotWorld();
        if (world is { } w)
            Log.Write(I($"[CRASH] {w.Mode}, {w.Zone} at ({w.X:0.0}, {w.Y:0.0}, {w.Z:0.0}); {w.People} people, {w.Fighting} fighting, {w.Bodies} bodies, {w.Bullets} bullets"));
        var recent = new StringBuilder("[CRASH] The last frames (ms): ");
        for (var n = long.Max(_frameNumber - 30, 0); n < _frameNumber; n++)
            recent.Append(I($"{Ring[n % RingSize].Ms:0.0} "));
        Log.Write(recent.ToString());
    }

    public static void LoadStage(string name, long since)
    {
        if (Enabled)
            Log.Write($"[LOAD] {name}: {Stopwatch.GetElapsedTime(since).TotalMilliseconds:0} ms");
    }

    private static int Percentile(long total, double fraction)
    {
        var want = (long)(total * fraction);
        var seen = 0L;
        for (var i = 0; i < Histogram.Length; i++)
        {
            seen += Histogram[i];
            if (seen > want)
                return i;
        }

        return Histogram.Length;
    }

    public static void Report()
    {
        if (!Active)
            return;
        foreach (var due in PendingReports)
            ReportHitch(due);
        PendingReports.Clear();
        Log.LineObserver = null;
        Summary();
        if (!Enabled || Frames.Count == 0)
            return;
        var n = Frames.Count;
        var sorted = Frames.Order().ToArray();
        var kb = (GC.GetTotalAllocatedBytes() - _startBytes) / 1024.0 / n;
        Log.Write($"[PERF] {n} frames: avg {Frames.Average():0.00} ms ({1000 / Frames.Average():0} fps), median {sorted[n / 2]:0.00}, 95% {sorted[n * 95 / 100]:0.00}, 99% {sorted[n * 99 / 100]:0.00}, worst {sorted[^1]:0.00}");
        Log.Write($"[PERF] draw calls per frame: avg {_drawCalls / n}, most {_maxDrawCalls}; garbage {kb:0.0} KB per frame; collections gen0 {GC.CollectionCount(0) - StartCollections[0]}, gen1 {GC.CollectionCount(1) - StartCollections[1]}, gen2 {GC.CollectionCount(2) - StartCollections[2]}");
        Log.Write($"[PERF] the worst frame: {_worstFrameSections}");
        foreach (var (name, s) in Sections.OrderByDescending(x => x.Times.Total))
            Log.Write($"[PERF]   {name,-12} avg {s.Total / n,7:0.000} ms, worst {s.Worst,8:0.00}");
    }

    private static void Summary()
    {
        var total = Histogram.Sum(x => (long)x);
        if (total == 0)
            return;
        var seconds = Ms(Stopwatch.GetTimestamp() - _playStart) / 1000.0;
        Log.Write(I($"[SUMMARY] {total} frames in {seconds:0} s: median {Percentile(total, 0.5)} ms, 95% {Percentile(total, 0.95)}, 99% {Percentile(total, 0.99)}, 99.9% {Percentile(total, 0.999)}, worst {_worstFrame:0.0}; {_hitchCount} hitches"));
        Log.Write(I($"[SUMMARY] Managed heap {_gcHeapAtStart / 1048576.0:0} -> {GC.GetTotalMemory(false) / 1048576.0:0} MB, working set {Environment.WorkingSet / 1048576} MB, collections gen0 {GC.CollectionCount(0)}, gen1 {GC.CollectionCount(1)}, gen2 {GC.CollectionCount(2)}, total GC pause {GC.GetTotalPauseDuration().TotalMilliseconds:0} ms"));
        foreach (var (cause, tally) in Causes.OrderByDescending(x => x.Value.Total))
            Log.Write(I($"[SUMMARY] Hitch cause: {cause}: {tally.Count} x, worst {tally.Worst:0.0} ms, {tally.Total:0} ms in all"));
        foreach (var hitch in WorstHitches.OrderByDescending(x => x.Ms))
            Log.Write(I($"[SUMMARY] Worst: {hitch.Ms:0.0} ms at {hitch.Seconds:0.0} s, {hitch.Zone}: {hitch.Cause}"));
        lock (ExceptionsSeen)
        {
            if (ExceptionsSeen.Count > 0)
                Log.Write($"[SUMMARY] Exceptions thrown during play: {string.Join(", ", ExceptionsSeen.Select(x => $"{x.Key} x{x.Value}"))}");
        }

        Log.Write($"[SUMMARY] Second-by-second figures: {Log.CsvPath}");
    }

    // ------------------------------------------------------------------------------------------------ Windows

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll")]
    private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    /// <summary>CPU time this thread has had (100 ns units), or -1 where it can't be told. Windows only updates this about every 16 ms.</summary>
    private static long ThreadCpuTicks()
    {
        if (!OperatingSystem.IsWindows() || !GetThreadTimes(GetCurrentThread(), out _, out _, out var kernel, out var user))
            return -1;
        return kernel + user;
    }

    private static bool SystemTimes(out long idle, out long kernel, out long user)
    {
        (idle, kernel, user) = (0, 0, 0);
        return OperatingSystem.IsWindows() && GetSystemTimes(out idle, out kernel, out user);
    }
}

/// <summary>
/// A thread of its own that notices the game's thread stuck on one frame, and says where it is while it's still stuck. So a
/// freeze that ends in a crash or a force-quit still leaves its mark, and a long one is pinned to what it was doing at the time.
/// </summary>
internal static class HitchWatchdog
{
    private static readonly long[] WarnAtMs = [250, 1000, 3000, 10000, 30000, 60000];

    public static void Start()
    {
        if (Debugger.IsAttached)
            return;   // (sitting at a breakpoint isn't a hang)
        new Thread(Watch) { IsBackground = true, Name = "Watchdog", Priority = ThreadPriority.AboveNormal }.Start();
    }

    private static void Watch()
    {
        long lastFrame = -1;
        var warned = 0;
        while (true)
        {
            Thread.Sleep(25);
            var (frame, start, where) = FrameStats.Watch();
            if (frame != lastFrame)
            {
                lastFrame = frame;
                warned = 0;
                continue;
            }

            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (warned < WarnAtMs.Length && ms >= WarnAtMs[warned])
            {
                warned++;
                Log.Write(FormattableString.Invariant($"[STALL] Frame {frame} has been going for {ms:0} ms: {where}"));
            }
        }
    }
}
