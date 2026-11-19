using Brutal.Numerics;

namespace BrutalFps.Game;

/// <summary>
/// What the player can see and hear, for things that cost less where nobody's looking: the AI's update rates
/// (<see cref="Npc.Tier"/>) and the physics debris (<see cref="DebrisSystem"/>). <see cref="Eye"/> is the camera; the zones
/// are every part of the level drawn last frame, in the main view or through a portal (<see cref="Npcs.Draw"/> notes them),
/// so somewhere seen through a portal still counts as in view.
/// </summary>
public static class Relevance
{
    /// <summary>The camera, this frame.</summary>
    public static float3 Eye;

    /// <summary>How magnified the view is (1: the naked eye; a scope's power): small things show from that many times as far.</summary>
    public static float Zoom = 1f;

    /// <summary>Counts frames (<see cref="BeginFrame"/>), for budgets kept per frame.</summary>
    public static int Frame;

    /// <summary>The zone the camera is in, this frame.</summary>
    public static Zones EyeZone;

    /// <summary>Every zone drawn last frame (the main view and the portals' views). Before the first frame: all of them.</summary>
    public static Zones Seen { get; private set; } = (Zones)~0;

    private static Zones _drawing;

    /// <summary>At the start of a frame's simulation, with the camera where it is now.</summary>
    public static void BeginFrame(float3 eye, float zoom = 1f)
    {
        Eye = eye;
        Zoom = float.Max(zoom, 1f);
        Frame++;
        EyeZone = Level.ZoneAt(eye);
        if (_drawing != 0)
            Seen = _drawing | EyeZone;
        _drawing = 0;
    }

    /// <summary>A view of these zones is being drawn (any view: the main one or a portal's).</summary>
    public static void Drawn(Zones zones) => _drawing |= zones;

    /// <summary>Whether <paramref name="zone"/> is the camera's own or was in view last frame.</summary>
    public static bool InSight(Zones zone) => (Seen & zone) != 0 || zone == EyeZone;

    /// <summary>Squared distance from the camera (or +infinity in a zone out of sight, where distance means nothing: it's far away).</summary>
    public static float DistanceSquared(float3 at, Zones zone)
    {
        if (!InSight(zone))
            return float.PositiveInfinity;
        if (zone != EyeZone)
            return 0f;   // (seen through a portal: we can't tell how near, so as if it were near)
        var d = at - Eye;
        return float3.Dot(d, d);
    }
}

/// <summary>
/// At most so many of something a frame (<see cref="Relevance.Frame"/>): sounds started, say. When a hundred guns go off
/// together, the first few are all anyone could tell apart anyway.
/// </summary>
public sealed class FrameBudget(int perFrame)
{
    private int _left, _frame = -1;

    /// <summary>Takes one from this frame's allowance: false once it's used up.</summary>
    public bool Take()
    {
        if (_frame != Relevance.Frame)
        {
            _frame = Relevance.Frame;
            _left = perFrame;
        }

        if (_left <= 0)
            return false;
        _left--;
        return true;
    }
}
