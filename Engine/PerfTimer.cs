using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace BrutalFps.Engine;

/// <summary>
/// A named stopwatch for something a frame might spend its time on: <c>using var _ = Perf.NavSearch.Time(npc.Name);</c>. It adds up
/// the time and the calls per frame and remembers who took longest, for <see cref="FrameStats"/>'s reports. Cheap, and
/// makes no garbage. Calls from any thread but the game's are ignored.
/// </summary>
public sealed class PerfTimer
{
    public string Name { get; }

    /// <summary>It's only there to hold others: a report blames what's inside it, if anything inside is big enough to blame.</summary>
    public bool Container { get; }

    internal int Index { get; }
    internal long Ticks, WorstTicks;
    internal int Calls;
    internal string? WorstWho;

    public PerfTimer(string name, bool container = false)
    {
        Name = name;
        Container = container;
        Index = FrameStats.Register(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Scope Time(string? who = null)
    {
        if (!FrameStats.Active || Environment.CurrentManagedThreadId != FrameStats.GameThread)
            return default;
        FrameStats.EnterTimer(Name);
        return new Scope(this, who, Stopwatch.GetTimestamp());
    }

    internal void Stop(long started, string? who)
    {
        var ticks = Stopwatch.GetTimestamp() - started;
        FrameStats.LeaveTimer();
        Ticks += ticks;
        Calls++;
        if (ticks > WorstTicks)
        {
            WorstTicks = ticks;
            WorstWho = who;
        }
    }

    public readonly struct Scope(PerfTimer? timer, string? who, long started) : IDisposable
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => timer?.Stop(started, who);
    }
}

/// <summary>Every timer in the game, so the second-by-second file has a column for each from the start.</summary>
public static class Perf
{
    // The window and the graphics driver.
    public static readonly PerfTimer Poll = new("os.poll");
    public static readonly PerfTimer Swap = new("gl.swap");
    public static readonly PerfTimer MeshUpload = new("gl.meshUpload");

    // People.
    public static readonly PerfTimer NpcsUpdate = new("npc.update", container: true);
    public static readonly PerfTimer NpcOne = new("npc.one", container: true);
    public static readonly PerfTimer NavFrame = new("npc.navFrame");
    public static readonly PerfTimer NavSearch = new("npc.navSearch", container: true);
    public static readonly PerfTimer NavLinks = new("npc.navLinks", container: true);        // which ladder or flight first (corridor searches inside)
    public static readonly PerfTimer NavCorridor = new("npc.navCorridor", container: true);  // over the chunks
    public static readonly PerfTimer NavCells = new("npc.navCells", container: true);        // A* over the corridor's cells
    public static readonly PerfTimer NavSmooth = new("npc.navSmooth");                      // pulling the path tight
    public static readonly PerfTimer NavRegions = new("npc.navRegions");                    // a chunk's pieces of floor, worked out again
    public static readonly PerfTimer NavRasterize = new("npc.navRasterize");                // a chunk's cells, from the boxes
    public static readonly PerfTimer CoverSearch = new("npc.coverSearch");
    public static readonly PerfTimer Pose = new("npc.pose");
    public static readonly PerfTimer ArmsInWalls = new("npc.armsInWalls");
    public static readonly PerfTimer RagdollStart = new("npc.ragdollStart");
    public static readonly PerfTimer NpcShadows = new("npc.blobShadows");
    public static readonly PerfTimer HearGunshot = new("npc.hearGunshot");
    public static readonly PerfTimer PlayerLight = new("npc.playerLight");
    public static readonly PerfTimer Evacuation = new("npc.evacuation");

    // The rest of the simulation.
    public static readonly PerfTimer PhysicsStep = new("physics.step");
    public static readonly PerfTimer PhysicsRay = new("physics.ray");
    public static readonly PerfTimer DebrisUpdate = new("debris.update");
    public static readonly PerfTimer AudioUpdate = new("audio.update");
    public static readonly PerfTimer AudioLoad = new("audio.loadClip");

    // Drawing.
    public static readonly PerfTimer FlareShadows = new("draw.flareShadows");
    public static readonly PerfTimer PrisonShadows = new("draw.prisonShadows");
    public static readonly PerfTimer FactoryShadows = new("draw.factoryShadows");
    public static readonly PerfTimer CameraFeeds = new("draw.cctvFeeds", container: true);
    public static readonly PerfTimer PortalViews = new("draw.portalViews", container: true);
    public static readonly PerfTimer MirrorViews = new("draw.mirrorViews", container: true);
    public static readonly PerfTimer LevelDraw = new("draw.level");
    public static readonly PerfTimer NpcsDraw = new("draw.npcs");
    public static readonly PerfTimer DebrisDraw = new("draw.debris");
    public static readonly PerfTimer TestSpike = new("test.spike");
}
