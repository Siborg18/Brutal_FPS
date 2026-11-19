using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>Who the intro directs in the prison yard: the prisoner being rescued, the two officers at the container's doors, and everyone else, who only watch the sky.</summary>
public sealed record YardCast(Npc Rescue, Npc[] DoorGuards, Npc[] Crowd, Npc[] FrontCrowd);

/// <summary>
/// The crowd in the prison yard, for the intro (<see cref="PrisonSquad"/>): with the yard officer already on his beat, about
/// fifty people. Prisoners talk in circles round the sports court and walk the west half of the ring path, four more officers
/// walk the edges of the yard, and two go to stand at the container's doors. The last two circles go and crowd round the doors
/// once it's down. They all keep clear of where the container lands, the player's walk in from the yard door and the
/// player's view of the doors. The officers carry batons, not guns.
/// </summary>
public sealed partial class Npcs
{
    /// <summary>The placeholder name of the prisoner the raiders have come for. Nobody shoots him, and NPCs' bullets pass through him.</summary>
    public const string RescueName = "Rook";

    private const string PrisonerPrefix = "prisoner ", YardOfficerPrefix = "yard guard ", DoorOfficerPrefix = "door guard ";

    // Dutch prisoners wear their own clothes. Nothing here is as pale as Rook's top, so you can still pick him out.
    private static readonly float3[] InmateTops =
    [
        new(0.30f, 0.31f, 0.32f), new(0.09f, 0.09f, 0.10f), new(0.25f, 0.03f, 0.04f), new(0.13f, 0.15f, 0.06f),
        new(0.30f, 0.45f, 0.65f), new(0.03f, 0.03f, 0.03f), new(0.45f, 0.40f, 0.30f), new(0.10f, 0.22f, 0.30f),
    ];
    private static readonly float3[] InmateLegs =
    [
        new(0.06f, 0.10f, 0.22f), new(0.12f, 0.18f, 0.32f), new(0.03f, 0.04f, 0.07f), new(0.20f, 0.20f, 0.21f), new(0.30f, 0.25f, 0.15f),
    ];
    private static readonly float3 RescueWhite = new(0.78f, 0.77f, 0.72f);
    private static readonly float3 RescueJeans = new(0.06f, 0.10f, 0.22f);
    private static readonly float3 OfficerNavy = new(0.03f, 0.04f, 0.09f);
    private static readonly float3 BootBlack = new(0.03f, 0.03f, 0.03f);
    private static readonly float3[] HairColours = [new(0.015f, 0.012f, 0.01f), new(0.05f, 0.03f, 0.02f), new(0.12f, 0.07f, 0.035f), new(0.3f, 0.29f, 0.28f)];

    /// <summary>The circles: (middle x, middle z, how many), in the prison's coordinates. 36 people.</summary>
    private static readonly (float X, float Z, int Count)[] YardCircles =
    [
        (-8.5f, -5.2f, 4), (-3.0f, -5.0f, 3), (3.0f, -5.2f, 4),    // the court, north of where the container lands
        (-8.5f, 4.8f, 3), (-2.5f, 5.0f, 5), (3.5f, 5.0f, 3),       // the court, south of it
        (9.3f, -5.9f, 8), (13.4f, 4.6f, 6),                        // the two who'll crowd round the doors (FrontCircles)
    ];

    /// <summary>How many of the last circles go and stand in front of the doors.</summary>
    private const int FrontCircles = 2;

    /// <summary>Those walking the ring path, in pairs: (where along the walk they start, on the outside of the path). 6 people.</summary>
    private static readonly (int Start, bool Outside)[] RingWalkers =
    [
        (2, true), (2, false), (9, true), (9, false), (15, true), (15, false),
    ];

    /// <summary>The officers' beats round the edge of the yard: (x, z) to (x, z).</summary>
    private static readonly (float2 From, float2 To)[] YardBeats =
    [
        (new(-19.8f, -10f), new(-19.8f, 1f)), (new(-17f, -14.5f), new(-12f, -14.5f)),
        (new(-6f, 14.6f), new(6f, 14.6f)), (new(20.8f, 8f), new(20.8f, 0.5f)),
    ];

    private (float3 RescueStart, float3 RescuePost, float RescueHeading, (float3 Start, float3 Post, float Facing)[] DoorGuards)? _yardPlan;

    /// <summary>
    /// Puts the crowd in the yard, for the intro to direct: null without people. If everyone's brought back after this (a
    /// respawn), they're back too, with the rescued man at <paramref name="rescuePost"/> and the door guards at their posts.
    /// </summary>
    public YardCast? AddYardCrowd(float3 rescueStart, float3 rescuePost, float rescueHeading, (float3 Start, float3 Post, float Facing)[] doorGuards)
    {
        if (_model is null)
            return null;
        _people.RemoveAll(p => p.Name == RescueName || p.Name.StartsWith(PrisonerPrefix, StringComparison.Ordinal)
            || p.Name.StartsWith(YardOfficerPrefix, StringComparison.Ordinal) || p.Name.StartsWith(DoorOfficerPrefix, StringComparison.Ordinal));
        _yardPlan = (rescueStart, rescuePost, rescueHeading, doorGuards);
        return BuildYardCrowd(afterRespawn: false);
    }

    /// <summary>The rescued man has got away: after a respawn he's back here, not in the container.</summary>
    public void RescueNowAt(float3 at, float heading)
    {
        if (_yardPlan is { } plan)
            _yardPlan = plan with { RescuePost = at, RescueHeading = heading };
    }

    private YardCast BuildYardCrowd(bool afterRespawn)
    {
        var plan = _yardPlan!.Value;
        var crowd = new List<Npc>();
        var random = new Random(4417);
        var number = 0;

        void AddPrisoner(float heading, bool talks, params Waypoint[] route)
        {
            number++;
            var name = PrisonerPrefix + number;
            DressPrisoner(name, number, random);
            var mood = number % 3 == 0 ? Temperament.Freezer : Temperament.Bolter;
            Add(name, SkinTones[number % SkinTones.Length], heading, NpcProfile.Civilian(mood) with { Talks = talks }, null, route);
            crowd.Add(_people[^1]);
        }

        // Each circle, facing its middle, about a metre between neighbours, none of them quite on the mark.
        var front = new List<Npc>();
        for (var c = 0; c < YardCircles.Length; c++)
        {
            var (cx, cz, count) = YardCircles[c];
            var radius = 0.55f / float.Sin(float.Pi / count);
            var turn = (float)random.NextDouble() * float.Tau;
            for (var i = 0; i < count; i++)
            {
                var a = turn + (i + 0.3f * ((float)random.NextDouble() - 0.5f)) * float.Tau / count;
                var r = radius + 0.12f * (float)random.NextDouble();
                var (x, z) = (cx + float.Sin(a) * r, cz + float.Cos(a) * r);
                var facing = float.Atan2(cx - x, cz - z);
                AddPrisoner(facing, true, new Waypoint(Level.PrisonAt(x, 0, z), float.PositiveInfinity, facing));
                if (c >= YardCircles.Length - FrontCircles)
                    front.Add(_people[^1]);
            }
        }

        // The ring path's west half, there and back (each from a point of their own; the pairs walk side by side).
        foreach (var (start, outside) in RingWalkers)
        {
            var (rx, rz) = outside ? (15.7f, 12.1f) : (14.8f, 11.2f);
            var way = new List<float3>();
            const int legs = 12;
            for (var i = 0; i <= legs; i++)
            {
                var a = float.DegreesToRadians(100f + 160f * i / legs);
                way.Add(Level.PrisonAt(float.Cos(a) * rx, 0, float.Sin(a) * rz));
            }
            for (var i = legs - 1; i > 0; i--)
                way.Add(way[i]);
            var route = way.Select((_, i) => new Waypoint(way[(start + i) % way.Count])).ToArray();
            var ahead = route[1].Position - route[0].Position;
            AddPrisoner(float.Atan2(ahead.X, ahead.Z), true, route);
        }

        // Officers on their beats.
        for (var i = 0; i < YardBeats.Length; i++)
        {
            var (from, to) = YardBeats[i];
            var name = YardOfficerPrefix + (i + 1);
            DressOfficer(name, i);
            var ahead = to - from;
            Add(name, SkinTones[(i * 3 + 2) % SkinTones.Length], float.Atan2(ahead.X, ahead.Y), NpcProfile.PrisonOfficer(i % 2 == 0 ? Temperament.Bolter : Temperament.Freezer), null,
                new Waypoint(Level.PrisonAt(from.X, 0, from.Y), 5f + i), new Waypoint(Level.PrisonAt(to.X, 0, to.Y), 7f - i));
            crowd.Add(_people[^1]);
        }

        // The two for the container's doors. (The intro walks them there; after a respawn they're already at their posts.)
        var doorGuards = new List<Npc>();
        for (var i = 0; i < plan.DoorGuards.Length; i++)
        {
            var (start, post, facing) = plan.DoorGuards[i];
            var name = DoorOfficerPrefix + (i + 1);
            DressOfficer(name, i + YardBeats.Length);
            var at = afterRespawn ? post : start;
            Add(name, SkinTones[(i * 2 + 1) % SkinTones.Length], facing, NpcProfile.PrisonOfficer(), null, new Waypoint(at, float.PositiveInfinity, facing));
            doorGuards.Add(_people[^1]);
            if (!afterRespawn)
                _people[^1].ScriptTake(at, facing);
        }

        // The one they've come for: in white, so you can tell him from the rest. A script has him from start to finish.
        var rescueAt = afterRespawn ? plan.RescuePost : plan.RescueStart;
        if (_wardrobe is not null)
            _outfitOverrides.TryAdd(RescueName, _wardrobe.Dyed(("hair_buzz", HairColours[1]), ("longsleeve", RescueWhite), ("jeans", RescueJeans), ("boots_work", BootBlack)));
        Add(RescueName, SkinTones[1], plan.RescueHeading, NpcProfile.Civilian(Temperament.Freezer) with { Spared = true }, null,
            new Waypoint(rescueAt, float.PositiveInfinity, plan.RescueHeading));
        var rescue = _people[^1];
        rescue.ScriptTake(rescueAt, plan.RescueHeading);
        Log.Write($"[NPC] The prison yard: {_people.Count(p => Level.ZoneAt(p.Position + new float3(0, 1f, 0)) == Zones.Prison && p.Position.Z - Level.PrisonOrigin.Z < 16f && float.Abs(p.Position.X - Level.PrisonOrigin.X) < 22f)} people, {number} of them prisoners, and {RescueName}");

        return new YardCast(rescue, [.. doorGuards], [.. crowd], [.. front]);
    }

    /// <summary>Their own clothes (a BRUTALFPS_OUTFIT override still wins), with the odd shaved head or beanie.</summary>
    private void DressPrisoner(string name, int number, Random random)
    {
        if (_wardrobe is null)
            return;
        var top = InmateTops[(int)(random.NextDouble() * InmateTops.Length)];
        var legs = InmateLegs[(int)(random.NextDouble() * InmateLegs.Length)];
        List<(string, float3)> kit =
        [
            (number % 3 == 1 ? "tshirt" : "longsleeve", top),
            (number % 9 == 4 ? "shorts" : number % 2 == 0 ? "jeans" : "trousers_work", legs),
            (number % 4 == 0 ? "sneakers" : "boots_work", number % 4 == 0 ? new float3(0.55f, 0.55f, 0.55f) : BootBlack),
        ];
        if (number % 5 == 2)
            kit.Add(("hoodie", InmateTops[number % InmateTops.Length]));
        if (number % 7 == 0)
            kit.Add(("beanie", new float3(0.05f, 0.05f, 0.06f)));
        else if (number % 5 != 0)
            kit.Add((number % 2 == 0 ? "hair_buzz" : "hair_short", HairColours[number % HairColours.Length]));
        _outfitOverrides.TryAdd(name, _wardrobe.Dyed([.. kit]));
    }

    private void DressOfficer(string name, int i)
    {
        if (_wardrobe is null)
            return;
        _outfitOverrides.TryAdd(name, _wardrobe.Dyed(("hair_short", HairColours[i % HairColours.Length]), ("cap", OfficerNavy), ("longsleeve", OfficerNavy),
            ("trousers_work", OfficerNavy), ("boots_work", BootBlack)));
    }
}
