using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The factory's crew: with the three gunmen in <see cref="Populate"/>, fifty hostiles in and around the factory. Each one
/// walks a short beat and stands watching at either end. <see cref="CheckFactoryCrew"/> checks every beat against the
/// level when the game starts.
/// </summary>
public sealed partial class Npcs
{
    private const string FactoryPrefix = "factory ";

    /// <summary>Where each of them walks: a beat between two points on the ground, and which part of the factory it's in.</summary>
    private static readonly (string Area, float X0, float Z0, float X1, float Z1)[] FactoryBeats =
    [
        // The hall.
        ("hall", -6f, -25f, 3f, -26f),
        ("hall", 8f, -24f, 9f, -13f),
        ("hall", -8f, -9f, -7f, 2f),
        ("hall", 3f, -10f, 2f, -3f),
        ("hall", 9f, -2.5f, 9f, 4f),
        ("hall", -6f, 7f, -2f, 5f),
        ("hall", 3f, 9f, 7f, 10f),
        ("hall", 13.5f, -1f, 13.5f, -12f),
        ("hall", -18f, 14f, -18f, 20f),
        ("hall", -6f, 19.5f, -3f, 17f),
        ("hall", 11f, 19f, 6f, 18f),
        // The hall's office.
        ("office", 6f, 26f, 7f, 24f),
        ("office", -6.5f, 24f, -6.5f, 28.5f),
        // The boiler house.
        ("boiler house", 17f, -53f, 17f, -45f),
        ("boiler house", 19f, -50f, 23f, -52f),
        ("boiler house", 25.5f, -53.5f, 25.5f, -46f),
        ("boiler house", 22f, -45f, 19f, -44.5f),
        ("boiler house", 15.5f, -48f, 15.5f, -54f),
        ("boiler house", 23f, -48f, 21f, -47f),
        // The outbuilding.
        ("outbuilding", -45f, -14f, -45f, -6f),
        ("outbuilding", -50f, -12f, -56f, -12f),
        ("outbuilding", -59f, -5f, -52f, -2f),
        ("outbuilding", -42f, -1f, -47f, 1.5f),
        ("outbuilding", -61f, -15f, -61f, -8f),
        ("outbuilding", -50f, -5f, -46f, -3.5f),
        ("outbuilding", -52f, 2f, -60f, 2.5f),
        // The second hall.
        ("second hall", 38f, 28f, 38f, 37f),
        ("second hall", 48f, 12f, 48f, 21f),
        ("second hall", 58f, 13f, 58f, 24f),
        ("second hall", 49f, 30f, 49f, 41f),
        ("second hall", 59f, 31f, 59f, 41f),
        ("second hall", 37f, 11f, 42f, 11f),
        ("second hall", 41f, 23f, 41f, 29f),
        // Round about, outside.
        ("grounds", -3f, -70f, -2f, -55f),
        ("gate", -4f, -96f, -1f, -100f),    // the sentry: watches down the road, into the hall when its door is open
        ("grounds", 11.5f, -43.5f, 12f, -47.5f),   // (east of where the van stops to back in on a raid: DeliveryVan's road)
        ("grounds", -30f, -30f, -30f, -18f),
        ("grounds", 26f, 4f, 26f, 19f),
        ("grounds", 26f, -30f, 27f, -19f),
        ("grounds", -10f, 38f, 8f, 38f),
        ("grounds", -22f, 36f, -15f, 42f),
        ("grounds", -36f, 1f, -36f, 9f),
        ("grounds", 30f, 3f, 31f, 14f),
        ("grounds", 68f, 18f, 68f, 36f),
        ("grounds", -60f, 40f, -50f, 45f),
        ("grounds", 62f, -70f, 70f, -60f),
        ("grounds", -66f, -68f, -58f, -74f),
    ];

    private void AddFactoryCrew()
    {
        // Debug: BRUTALFPS_NOFACTORYCREW=1 leaves just the three gunmen (to compare frame times).
        _factoryArea.Clear();
        _factoryPushed = false;
        if (Environment.GetEnvironmentVariable("BRUTALFPS_NOFACTORYCREW") == "1")
            return;
        string[][] kit = [["hoodie", "jeans", "sneakers", "beanie"], ["work_jacket", "trousers_work", "boots_work", "cap"],
                          ["longsleeve", "jeans", "boots_work", "beanie"], ["tshirt", "trousers_work", "boots_work"]];
        for (var i = 0; i < FactoryBeats.Length; i++)
        {
            var (area, x0, z0, x1, z1) = FactoryBeats[i];
            if (_factoryRaid && area == "office")
                continue;   // (on a raid the office has its own guards, who stay put: AddOfficeWomen)
            var accuracy = 0.3f + 0.04f * (i % 6);
            // Two in three with a pistol, the rest with AKs; every fourth in body armour.
            var profile = (i % 3 == 2 ? NpcProfile.Rifleman(accuracy) : NpcProfile.Gunman(accuracy)) with { Armoured = i % 4 == 0 };
            var heading = float.Atan2(x1 - x0, z1 - z0);
            var (face0, face1) = (heading + 0.6f, heading + float.Pi - 0.6f);
            if (area == "gate")
            {
                // Faces the hall's door from both stops, and what they spot from up here they come down the road after.
                face0 = float.Atan2(-x0, -Level.HalfLength - z0);
                face1 = float.Atan2(-x1, -Level.HalfLength - z1);
                profile = profile with { Strategy = NpcStrategy.Push };
            }

            Add($"{FactoryPrefix}{i + 1}", SkinTones[i % SkinTones.Length], heading, profile, kit[i % kit.Length],
                new Waypoint(new float3(x0, 0, z0), 4f + i % 5, face0),
                new Waypoint(new float3(x1, 0, z1), 3f + i % 4, face1));
            _people[^1].KeepInside = BuildingOf(area);
            _factoryArea[_people[^1]] = area;
        }

        // In each of the outbuildings, a commando with an M249, posted inside with the gun on its door.
        for (var i = 0; i < OutbuildingCommandos.Length; i++)
        {
            var (area, x, z, doorX, doorZ) = OutbuildingCommandos[i];
            var facing = float.Atan2(doorX - x, doorZ - z);
            Add($"{FactoryCommandoPrefix}{i + 1}", SkinTones[(i * 5 + 1) % SkinTones.Length], facing, NpcProfile.Commando(Equipment.Lmg), null,
                new Waypoint(new float3(x, 0, z), float.PositiveInfinity, facing));
            _people[^1].KeepInside = BuildingOf(area);
            _factoryArea[_people[^1]] = area;
        }

        AddDoorGuards();   // (Game/NpcsFactoryDefence.cs)
    }

    private const string FactoryCommandoPrefix = "factory commando ";

    /// <summary>The M249 commandos: which outbuilding, where they stand, and the doorway they watch.</summary>
    private static readonly (string Area, float X, float Z, float DoorX, float DoorZ)[] OutbuildingCommandos =
    [
        ("boiler house", 21f, -51f, 21f, -42.2f),
        ("outbuilding", -53f, -12f, -38.2f, -6.2f),
        ("second hall", 48f, 16.5f, 34f, 23f),
    ];

    /// <summary>
    /// The floor plan of the building a part of the factory is in (just inside its walls), which its crew stay in
    /// (<see cref="Npc.KeepInside"/>): whatever they hear outside, they fight from the doorways. Null: the grounds, the
    /// gate, anywhere outdoors. (The smaller buildings' are their shade volumes: Level.FactoryShadeVolumes.)
    /// </summary>
    private static Aabb? BuildingOf(string area) => area switch
    {
        "hall" or "office" => new Aabb(new float3(-Level.HalfWidth, -1f, -Level.HalfLength), new float3(Level.HalfWidth, 20f, Level.HalfLength)),
        "boiler house" => new Aabb(new float3(14.38f, -1f, -55.62f), new float3(27.62f, 20f, -42.38f)),
        "outbuilding" => new Aabb(new float3(-63.62f, -1f, -17.62f), new float3(-38.38f, 20f, 3.62f)),
        "second hall" => new Aabb(new float3(34.28f, -1f, 8.28f), new float3(63.72f, 20f, 43.72f)),
        _ => null,
    };

    private const string OfficeWomanPrefix = "office woman ";

    // Where each of them stands in the office, which way she's facing, and what she's wearing: (hair, top, trousers).
    private static readonly (float X, float Z, float Facing, string Top, float3 Hair, float3 TopTint, float3 Legs)[] OfficeWomenAt =
    [
        (2.3f, 24.0f, -2.2f, "tshirt", new(0.03f, 0.02f, 0.015f), new(0.55f, 0.08f, 0.12f), new(0.06f, 0.10f, 0.22f)),
        (1.0f, 24.6f, 1.1f, "longsleeve", new(0.26f, 0.13f, 0.05f), new(0.72f, 0.70f, 0.62f), new(0.03f, 0.04f, 0.07f)),
        (6.0f, 28.4f, -0.9f, "longsleeve", new(0.48f, 0.34f, 0.14f), new(0.10f, 0.28f, 0.34f), new(0.12f, 0.18f, 0.32f)),
        (-6.0f, 25.0f, 2.6f, "tshirt", new(0.08f, 0.04f, 0.02f), new(0.30f, 0.45f, 0.65f), new(0.05f, 0.05f, 0.06f)),
    ];

    /// <summary>The women held in the factory's office on a raid (<see cref="FactoryEvacuation"/>).</summary>
    public IEnumerable<Npc> OfficeWomen => _people.Where(static p => p.Name.StartsWith(OfficeWomanPrefix, StringComparison.Ordinal));

    private const string OfficeGuardPrefix = "office guard ";

    // Where each of the women's guards stands, each close to one of them, and which way they're facing.
    private static readonly (float X, float Z, float Facing)[] OfficeGuardsAt = [(3.6f, 24.2f, -1.2f), (6f, 26f, 0.6f), (-6.5f, 24f, 1.0f)];

    /// <summary>The guards who stay in the office with the women until it's time to take them out (<see cref="FactoryEvacuation"/>).</summary>
    public IEnumerable<ArmedNpc> OfficeGuards => _people.OfType<ArmedNpc>().Where(static p => p.Name.StartsWith(OfficeGuardPrefix, StringComparison.Ordinal));

    /// <summary>They've left the world: not drawn or thought about again until a respawn puts everyone back.</summary>
    public void GoneForGood(Npc npc) => _people.Remove(npc);

    private void AddOfficeWomen()
    {
        for (var i = 0; i < OfficeWomenAt.Length; i++)
        {
            var (x, z, facing, top, hair, topTint, legs) = OfficeWomenAt[i];
            var name = $"{OfficeWomanPrefix}{i + 1}";
            if (_wardrobe is not null)
                _outfitOverrides[name] = _wardrobe.Dyed(("hair_long", hair), (top, topTint), ("jeans", legs), ("sneakers", new float3(0.7f, 0.7f, 0.7f)));
            Add(name, SkinTones[(i * 2 + 1) % SkinTones.Length], facing, NpcProfile.Civilian(Temperament.Freezer) with { Woman = true }, null,
                new Waypoint(new float3(x, 0, z), float.PositiveInfinity, facing));
        }

        for (var i = 0; i < OfficeGuardsAt.Length; i++)
        {
            var (x, z, facing) = OfficeGuardsAt[i];
            var profile = NpcProfile.Commando();   // (the women's escort: commandos, all three)
            Add($"{OfficeGuardPrefix}{i + 1}", SkinTones[(i * 3 + 2) % SkinTones.Length], facing, profile, null,
                new Waypoint(new float3(x, 0, z), float.PositiveInfinity, facing));
        }
    }

    /// <summary>
    /// Counts the factory's hostiles for the log, by where they are, and checks every beat against the level: each stop on
    /// the ground, not inside anything, with room to stand; a straight walk between stops. Call once the level's collision exists.
    /// </summary>
    public void CheckFactoryCrew(Level level)
    {
        var crew = _people.Where(p => p.Faction == Faction.Hostile && p.HomeZone == Zones.Warehouse).ToList();
        if (crew.Count == 0)
            return;
        var bad = 0;
        foreach (var npc in crew)
        {
            var route = npc.Route;
            for (var i = 0; i < route.Count; i++)
            {
                var problem = SpotProblem(level, route[i].Position)
                              ?? (route.Count > 1 && !WalkClear(route[i].Position, route[(i + 1) % route.Count].Position) ? "the way to the next stop is blocked" : null);
                if (problem is null)
                    continue;
                bad++;
                Log.Write($"[WARN] {npc.Name} at ({route[i].Position.X:0.0}, {route[i].Position.Z:0.0}): {problem}");
            }
        }

        foreach (var woman in OfficeWomen)
        {
            if (SpotProblem(level, woman.Position) is { } problem)
                Log.Write($"[WARN] {woman.Name} at ({woman.Position.X:0.0}, {woman.Position.Z:0.0}): {problem}");
        }

        var byArea = FactoryBeats.GroupBy(b => b.Area).Select(g => $"{g.Count()} {g.Key}");
        Log.Write($"[NPC] The factory: {crew.Count} hostiles ({string.Join(", ", byArea)}, {OutbuildingCommandos.Length} M249 commandos in the outbuildings, {DoorGuardPosts.Length} guards on the hall's side door, 3 gunmen on the grounds); {bad} bad spots on their beats");
    }

    /// <summary>What's wrong with someone standing at <paramref name="at"/> (null: nothing).</summary>
    private string? SpotProblem(Level level, float3 at)
    {
        foreach (var box in level.Boxes)
        {
            if (box.Collides && box.Bounds.Overlaps(new Aabb(at + new float3(-Npc.BodyRadius, 0.2f, -Npc.BodyRadius), at + new float3(Npc.BodyRadius, Npc.BodyHeight, Npc.BodyRadius))))
                return $"inside {box.Material} at ({box.Bounds.Center.X:0.0}, {box.Bounds.Center.Y:0.0}, {box.Bounds.Center.Z:0.0})";
        }

        if (Senses.Clear(at + new float3(0, 0.4f, 0), at - new float3(0, 0.4f, 0)) >= 1f)
            return "no ground under them";
        for (var i = 0; i < 8; i++)
        {
            var a = i * float.Tau / 8;
            var dir = new float3(float.Sin(a), 0, float.Cos(a)) * (Npc.BodyRadius + 0.05f);
            foreach (var h in (ReadOnlySpan<float>)[0.6f, 1.25f])
            {
                var from = at + new float3(0, h, 0);
                if (Senses.Clear(from, from + dir) < 1f)
                    return "no room to stand";
            }
        }

        return null;
    }

    /// <summary>
    /// Debug: BRUTALFPS_NAVCHECK=1 logs, for everyone's route, each leg whose straight line a body can't walk, whether a
    /// path gets round it, and what the searches cost. Call once the level's collision exists.
    /// </summary>
    public void CheckNav()
    {
        if (Environment.GetEnvironmentVariable("BRUTALFPS_NAVCHECK") != "1" || Senses.Nav is not { } nav)
            return;
        nav.BeginFrame(0f, 0f);
        var path = new List<float3>();
        var (legs, straight, round, short_) = (0, 0, 0, 0);
        foreach (var npc in _people)
        {
            var route = npc.Route;
            for (var i = 0; i < route.Count && route.Count > 1; i++)
            {
                var (a, b) = (route[i].Position, route[(i + 1) % route.Count].Position);
                legs++;
                if (nav.LineClear(a, b))
                {
                    straight++;
                    continue;
                }

                // (Up and down ladders and stairs too: on from the far end of each link the way takes.)
                var (from, links, legsWalked, end) = (a, 0, 0, a);
                while (true)
                {
                    var found = nav.FindPath(from, b, path, out var via);
                    end = found && path.Count > 0 ? path[^1] : from;
                    legsWalked += path.Count;
                    if (via is null || links++ > 20)
                        break;
                    from = NpcNav.Far(via, end);
                }
                var left = float3.Distance(end with { Y = b.Y }, b);
                if (left < 0.6f && float.Abs(end.Y - b.Y) < 0.6f)
                {
                    round++;
                    Log.Write($"[NAV] {npc.Name}: ({a.X:0.0}, {a.Y:0.0}, {a.Z:0.0}) -> ({b.X:0.0}, {b.Y:0.0}, {b.Z:0.0}): round by {legsWalked} legs, {links} links");
                }
                else
                {
                    short_++;
                    Log.Write($"[NAV] {npc.Name}: ({a.X:0.0}, {a.Y:0.0}, {a.Z:0.0}) -> ({b.X:0.0}, {b.Y:0.0}, {b.Z:0.0}): gets no nearer than {left:0.0} m (ends at height {end.Y:0.0})");
                }
            }
        }

        // BRUTALFPS_NAVMAP=x0,z0,x1,z1,y logs that part of the map.
        if (Environment.GetEnvironmentVariable("BRUTALFPS_NAVMAP") is { Length: > 0 } map)
        {
            var m = map.Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            foreach (var line in nav.Map(m[0], m[1], m[2], m[3], m[4]))
                Log.Write("[NAVMAP] " + line);
        }

        // Ways that have to go through a doorway or round a corner, and from floor to floor.
        foreach (var link in nav.Links)
            Log.Write($"[NAV] link {link.Name}: ({link.Bottom.X:0.0}, {link.Bottom.Y:0.0}, {link.Bottom.Z:0.0}) up to ({link.Top.X:0.0}, {link.Top.Y:0.0}, {link.Top.Z:0.0})");
        foreach (var door in nav.Doors)
            Log.Write($"[NAV] door {door.Name}: hinged {door.Hinged}, shut at ({door.Shut[0].Center.X:0.0}, {door.Shut[0].Center.Y:0.0}, {door.Shut[0].Center.Z:0.0}), open {door.Openness:0.0}, allowed {door.Allowed}");
        foreach (var link in nav.Links)
        {
            nav.FindPath(link.Bottom, link.Top, path, out var up);
            nav.FindPath(link.Top, link.Bottom, path, out var down);
            if (up != link || down != link)
                Log.Write($"[WARN] {link.Name}: a search from one end to the other doesn't use it (up: {up?.Name ?? "none"}, down: {down?.Name ?? "none"})");
        }

        var flats = Level.FlatsOrigin;
        var jungle = Level.JungleOrigin;
        foreach (var (what, a, b) in ((string, float3, float3)[])
                 [("jungle clearing into the shack by its door", jungle + new float3(5f, 0, 2.2f), jungle + new float3(-1.2f, 0, 2.6f)),
                  ("jungle shack out of its door", jungle + new float3(-1.2f, 0, 2.6f), jungle + new float3(5f, 0, 2.2f)),
                  ("jungle clearing round into the shack", Level.JungleClearingCenter, jungle + new float3(0.5f, 0, 2.0f)),
                  ("jungle behind the shack round into it", jungle + new float3(-2.9f, 0, 3.9f), jungle + new float3(-1.4f, 0, 2.6f)),
                  ("jungle west of the shack round into it", jungle + new float3(-5f, 0, 1f), jungle + new float3(-1.4f, 0, 2.6f)),
                  ("jungle south of the shack round into it", jungle + new float3(-3f, 0, 9f), jungle + new float3(-1.4f, 0, 2.6f)),("grounds into the hall by its door", new(22.3f, 0, 6.3f), new(4.4f, 0, 0.3f)),
                  ("road into the hall", new(0, 0, -45f), new(0, 0, -20f)),
                  ("hall into its office", new(5f, 0, 15f), new(5f, 0, 24f)),
                  ("hall up to the catwalk", new(0, 0, -20f), new(0, 5.7f, -28f)),
                  ("grounds up the rear steps to the walkway", new(4f, 0, 36f), new(11f, 3.9f, 29.3f)),
                  ("rear walkway down to the hall", new(11f, 3.9f, 29.3f), new(14f, 0, 25f)),
                  ("road up to the catwalk", new(0, 0, -45f), new(0, 5.7f, -28f)),
                  ("catwalk down to the road", new(0, 5.7f, -28f), new(0, 0, -45f)),
                  ("undercroft up the stairs", flats, flats + new float3(11f, 12f, 2.7f)),
                  ("top landing down to the undercroft", flats + new float3(11f, 12f, 2.7f), flats)])
        {
            var (at, floors) = (a, 0);
            var line = new System.Text.StringBuilder();
            while (true)
            {
                var found = nav.FindPath(at, b, path, out var via);
                var end = found && path.Count > 0 ? path[^1] : at;
                line.Append(string.Join(" ", path.Select(p => $"({p.X:0.0}, {p.Y - a.Y:0.0}, {p.Z:0.0})")));
                if (via is null || floors++ > 20)
                {
                    Log.Write($"[NAV] {what}: {(float3.Distance(end, b) < 0.6f ? "gets there" : $"stops {float3.Distance(end, b):0.0} m short")} by {floors} links: {line}");
                    break;
                }

                line.Append($" [{via.Name}] ");
                at = NpcNav.Far(via, end);
            }
        }

        var ms = nav.Searches > 0 ? nav.SearchTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / nav.Searches : 0;
        Log.Write($"[NAV] {_people.Count} people, {nav.BoxCount} boxes, {legs} route legs: {straight} straight, {round} round by a path, {short_} stop short; {nav.Searches} searches at {ms:0.00} ms each, worst {nav.WorstSearchMs:0.0} ms");
    }

    private bool WalkClear(float3 from, float3 to)
    {
        foreach (var h in (ReadOnlySpan<float>)[0.6f, 1.25f])
        {
            var up = new float3(0, h, 0);
            if (Senses.Clear(from + up, to + up) < 0.999f)
                return false;
        }

        return true;
    }
}
