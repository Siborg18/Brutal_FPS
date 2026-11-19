using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// What one side knows, together, in one part of the level (<see cref="SquadBoard"/>): where the player was last seen and
/// how they were moving, the places a search still has to look, who's flanking, and when someone last threw a grenade or
/// called something out. Armed NPCs write to it when they see the player; they read from it when they're in the fight
/// (they've had contact, or someone shouted to them: <see cref="ArmedNpc.HearShout"/>), so word only travels as far as a
/// shout does.
/// </summary>
public sealed class SquadIntel
{
    /// <summary>Where the player was last seen by anyone on this side (chest height), and when (<see cref="NpcSenses.Time"/>).</summary>
    public float3 LastKnown;
    public float LastKnownAt = float.NegativeInfinity;

    /// <summary>How the player was moving when last seen (m/s, flat).</summary>
    public float3 Velocity;

    /// <summary>
    /// Where the player has been staying (within <see cref="CampRadius"/> m), and since when: someone who stays put behind
    /// something gets a grenade or a flanker sent at them.
    /// </summary>
    public float3 CampAt;
    public float CampSince = float.PositiveInfinity;
    public const float CampRadius = 3f;

    /// <summary>The one flanking the player now (null: nobody), and when anyone may start again.</summary>
    public ArmedNpc? Flanker;
    public float NextFlankAt;

    /// <summary>Not before then: another grenade (one at a time from a side, and not too often).</summary>
    public float NextGrenadeAt;

    /// <summary>When each callout was last made on this side: two people don't shout the same thing at once.</summary>
    public readonly Dictionary<Bark, float> LastSaid = [];

    /// <summary>The places a search for the player still has to look (<see cref="BeginSearch"/>).</summary>
    public readonly List<SearchPoint> Points = [];
    public float3 SearchCentre;
    public float SearchStarted = float.NegativeInfinity;
    private float3 _searchEye;
    private float _floorNear;   // a floor height near the search (the searcher's own), to find which floor it's on
    private int _rings;

    public bool Known(float now, float within) => now - LastKnownAt < within;

    /// <summary>The player was seen at <paramref name="at"/> (chest) moving at <paramref name="velocity"/>.</summary>
    public void Report(float3 at, float3 velocity, float now)
    {
        LastKnown = at;
        Velocity = velocity;
        LastKnownAt = now;
        if (float.IsPositiveInfinity(CampSince) || Flat(at - CampAt) > CampRadius)
        {
            CampAt = at;
            CampSince = now;
        }

        // Found them somewhere else: whatever was left to search there doesn't matter any more. (Seen where the search is,
        // it carries on: someone who loses them again there picks up where it left off, rather than starting again.)
        if (Points.Count > 0 && Flat(at - SearchCentre) > SearchSame * 1.5f)
            Points.Clear();
    }

    /// <summary>Someone of this side has the player in sight now (or had, a moment ago): no need to search, just go there.</summary>
    public bool InSightNow(float now) => now - LastKnownAt < 1.2f;

    /// <summary>How long the player has been staying round the same spot, as last seen (0: they're moving, or not seen lately).</summary>
    public float CampedFor(float now) => !Known(now, 6f) || float.IsPositiveInfinity(CampSince) ? 0f : LastKnownAt - CampSince;

    // ------------------------------------------------------------------------------------------------ searching

    private static readonly float[] Rings = [3f, 6f, 9.5f, 13.5f, 18f, 23f];
    private const int PerRing = 10;
    private const float SearchFresh = 25f;    // s: a search started this lately round the same place is the same search
    private const float SearchSame = 6f;      // m
    private const float PlayerPace = 3.5f;    // m/s: how far they reckon the player can have got, a second
    private const float ClearReach = 2.5f;    // m: standing this near a point clears it

    /// <summary>
    /// Starts a search round <paramref name="centre"/> (where the player went from sight), unless one's already going on
    /// near there. The points are the floor round it, out to as far as the player could have got by now, that the one who
    /// lost them couldn't see from <paramref name="eye"/>: the corners, doorways and cover where someone could be.
    /// </summary>
    public bool BeginSearch(float3 centre, float3 eye, float floorNear, NpcSenses s, Zones zone)
    {
        if (s.Time - SearchStarted < SearchFresh && Flat(centre - SearchCentre) < SearchSame && Points.Count > 0)
            return false;
        Points.Clear();
        SearchCentre = centre;
        SearchStarted = s.Time;
        _searchEye = eye;
        _floorNear = floorNear;
        _rings = 0;
        AddRings(s, zone, 4);
        Log.Write($"[SQUAD] Search round ({centre.X:0.0}, {centre.Z:0.0}): {Points.Count} places to look");
        return true;
    }

    /// <summary>Adds the next <paramref name="count"/> rings of places (as far out as the player could have got by now).</summary>
    private void AddRings(NpcSenses s, Zones zone, int count)
    {
        var reach = 4f + PlayerPace * float.Max(0f, s.Time - LastKnownAt);
        var floorY = s.Nav?.FloorHeight(SearchCentre, _floorNear) ?? _floorNear;
        var random = new Random(s.Time.GetHashCode());
        for (var n = 0; n < count && _rings < Rings.Length; n++, _rings++)
        {
            var r = Rings[_rings];
            if (r > float.Max(reach, Rings[0]))
                break;
            var turn = (float)random.NextDouble() * float.Tau;
            for (var i = 0; i < PerRing; i++)
            {
                var a = turn + i * float.Tau / PerRing;
                var p = new float3(SearchCentre.X + float.Sin(a) * r, floorY, SearchCentre.Z + float.Cos(a) * r);
                if (Level.ZoneAt(p + new float3(0, 1f, 0)) != zone || s.Nav?.Walkable(p) == false)
                    continue;
                // What the one who lost them could see from where they stood is already looked at.
                var seen = s.Clear(_searchEye, p + new float3(0, 1.2f, 0)) > 0.99f && Flat(p - _searchEye) < 20f;
                Points.Add(new SearchPoint { Position = p, Cleared = seen });
            }
        }
    }

    /// <summary>
    /// The next place for <paramref name="who"/> to look: the nearest that nobody has looked at or is on their way to,
    /// leaning towards the middle of the search. Null: everywhere's been looked at (rings further out are added while
    /// there are any).
    /// </summary>
    public SearchPoint? Claim(ArmedNpc who, float3 from, NpcSenses s, Zones zone)
    {
        foreach (var p in Points)
            if (ReferenceEquals(p.ClaimedBy, who))
                p.ClaimedBy = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            SearchPoint? best = null;
            var bestScore = float.MaxValue;
            foreach (var p in Points)
            {
                if (p.Cleared || p.ClaimedBy is { Alive: true })
                    continue;
                var score = Flat(p.Position - from) + 0.6f * Flat(p.Position - SearchCentre);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }

            if (best is not null)
            {
                best.ClaimedBy = who;
                return best;
            }

            if (_rings >= Rings.Length)
                break;
            AddRings(s, zone, 1);
        }

        return null;
    }

    /// <summary>Someone has looked from <paramref name="eye"/>: the points near them, and up to <paramref name="rays"/> they can see, are done.</summary>
    public int ClearFrom(float3 eye, float3 standing, NpcSenses s, int rays)
    {
        var cleared = 0;
        foreach (var p in Points)
        {
            if (p.Cleared)
                continue;
            if (Flat(p.Position - standing) < ClearReach)
            {
                p.Cleared = true;
                cleared++;
                continue;
            }

            if (rays <= 0 || Flat(p.Position - standing) > 16f)
                continue;
            rays--;
            if (s.Clear(eye, p.Position + new float3(0, 1.2f, 0)) > 0.99f)
            {
                p.Cleared = true;
                cleared++;
            }
        }

        return cleared;
    }

    /// <summary>How many places are left to look.</summary>
    public int Left => Points.Count(p => !p.Cleared);

    private static float Flat(float3 v) => float.Sqrt(v.X * v.X + v.Z * v.Z);
}

/// <summary>A place a search has to look (<see cref="SquadIntel.Points"/>).</summary>
public sealed class SearchPoint
{
    public float3 Position;
    public bool Cleared;
    public ArmedNpc? ClaimedBy;
}

/// <summary>Each side's <see cref="SquadIntel"/> in each part of the level.</summary>
public sealed class SquadBoard
{
    private readonly Dictionary<(Faction, Zones), SquadIntel> _intel = [];

    public SquadIntel For(Faction faction, Zones zone)
    {
        if (!_intel.TryGetValue((faction, zone), out var intel))
            _intel[(faction, zone)] = intel = new SquadIntel();
        return intel;
    }

    /// <summary>Everyone forgets (a respawn).</summary>
    public void Clear() => _intel.Clear();
}
