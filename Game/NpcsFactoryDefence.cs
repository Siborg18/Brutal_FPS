using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// How the factory's crew defend the hall.
///
/// <b>The side door's guards:</b> two of them stand outside the hall's side door (<see cref="Level.SideDoorCenter"/>), one
/// either side of it. One holds his post whatever happens (<see cref="ArmedNpc.HoldsPost"/>: he fights from there); the
/// other fights as anyone would, going after the player if he must. If the one at the door goes down, the other comes back
/// and takes his place, so there's always one at the door while either is alive.
///
/// <b>Reinforcing the hall:</b> once the player attacks the hall (anyone in it, or either door guard, is hit or killed, or
/// they're fighting with the player in the hall or close outside it), whoever's still standing in each of the outbuildings
/// leaves it and pushes into the hall by the door nearest them: the outbuilding's crew by the side door, the second hall's up
/// the outside steps and in by the rear door, the boiler house's by the big sliding door. Once through, they go after the
/// player. Each life, it happens once.
/// </summary>
public sealed partial class Npcs
{
    private const string DoorGuardPrefix = "factory door guard ";

    /// <summary>The side door's guards: where each stands (either side of the door, a pace out from the wall) and which way he faces (out, a little away from the door).</summary>
    private static readonly (float3 At, float Facing)[] DoorGuardPosts =
    [
        (new(Level.SideDoorX0 - 0.85f, 0, Level.HalfLength + 2.4f), -0.45f),
        (new(Level.SideDoorX1 + 0.85f, 0, Level.HalfLength + 2.4f), 0.45f),
    ];

    /// <summary>Which part of the factory each of its crew belongs to (FactoryBeats' areas), for <see cref="UpdateFactoryDefence"/>.</summary>
    private readonly Dictionary<Npc, string> _factoryArea = [];

    /// <summary>This life, the outbuildings have been sent into the hall.</summary>
    private bool _factoryPushed;

    private float _doorGuardCheck;

    /// <summary>
    /// Each outbuilding, the door into the hall its crew take, and where they make for just through it (spread out, so they
    /// don't all stand on one spot; the second hall's is the walkway inside the rear door, at the top of the outside steps).
    /// </summary>
    private static readonly (string Area, string Door, float3[] Through)[] HallPushes =
    [
        ("outbuilding", "side door",
        [
            new(-13.5f, 0, 28.4f), new(-14.9f, 0, 27.6f), new(-12.1f, 0, 27.6f), new(-14.2f, 0, 26.4f), new(-12.8f, 0, 26.4f),
            new(-15.8f, 0, 25.8f), new(-11.2f, 0, 25.8f), new(-13.5f, 0, 25.2f),
        ]),
        ("second hall", "rear door",
        [
            new(13.1f, Level.FactoryRearDeckY, 29.3f), new(11.6f, Level.FactoryRearDeckY, 29.3f), new(14.6f, Level.FactoryRearDeckY, 29.3f),
            new(10.4f, Level.FactoryRearDeckY, 29.3f), new(15.6f, Level.FactoryRearDeckY, 29.3f), new(12.3f, Level.FactoryRearDeckY, 29.0f),
            new(16.9f, Level.FactoryRearDeckY, 29.3f), new(13.9f, Level.FactoryRearDeckY, 29.0f),
        ]),
        ("boiler house", "big sliding door",
        [
            new(0f, 0, -26.5f), new(-2f, 0, -25.8f), new(2f, 0, -25.8f), new(-1f, 0, -24.4f), new(1f, 0, -24.4f),
            new(-3f, 0, -24f), new(3f, 0, -24f), new(0f, 0, -23f),
        ]),
    ];

    private void AddDoorGuards()
    {
        string[][] kit = [["work_jacket", "jeans", "boots_work", "beanie"], ["hoodie", "trousers_work", "boots_work", "cap"]];
        for (var i = 0; i < DoorGuardPosts.Length; i++)
        {
            var (at, facing) = DoorGuardPosts[i];
            var profile = NpcProfile.Rifleman(0.5f) with { Armoured = true };
            Add($"{DoorGuardPrefix}{i + 1}", SkinTones[(i * 7 + 3) % SkinTones.Length], facing, profile, kit[i % kit.Length],
                new Waypoint(at, float.PositiveInfinity, facing));
            _factoryArea[_people[^1]] = "door guard";
            if (i == 0 && _people[^1] is ArmedNpc anchor)
                anchor.HoldsPost = true;   // (the one who never leaves the door)
        }
    }

    /// <summary>The side door's guards, as they are now.</summary>
    private IEnumerable<ArmedNpc> DoorGuards => _people.OfType<ArmedNpc>().Where(static p => p.Name.StartsWith(DoorGuardPrefix, StringComparison.Ordinal));

    /// <summary>Call once a frame, after the people have moved.</summary>
    private void UpdateFactoryDefence(float dt, PlayerController player)
    {
        if (_factoryArea.Count == 0)
            return;
        UpdateDoorGuards(dt);
        if (!_factoryPushed && player.Alive && !Senses.PlayerConcealed && HallUnderAttack(player) is { } why)
        {
            _factoryPushed = true;
            PushIntoTheHall(why);
        }
    }

    /// <summary>One of the side door's guards always at the door: if the one holding it is down, the other goes back and takes it over.</summary>
    private void UpdateDoorGuards(float dt)
    {
        _doorGuardCheck -= dt;
        if (_doorGuardCheck > 0f)
            return;
        _doorGuardCheck = 0.5f;

        var guards = DoorGuards.ToList();
        if (guards.Any(g => g.Alive && (g.HoldsPost || g.Ordered)))
            return;   // (someone's at the door, or on his way back to it)
        foreach (var guard in guards.Where(g => g.Alive))
        {
            var (post, facing) = DoorGuardPosts[guard.Name.EndsWith('1') ? 0 : 1];
            // (The nearer of the two posts, now that he's on his own.)
            foreach (var (at, face) in DoorGuardPosts)
            {
                if (float3.Distance(guard.Position, at) < float3.Distance(guard.Position, post))
                    (post, facing) = (at, face);
            }

            if (float3.Distance(guard.Position with { Y = 0 }, post) < 1.5f)
            {
                guard.HoldsPost = true;
                guard.Repost(post, facing);
                Log.Write($"[NPC] {guard.Name}: the only one left at the side door: holding it");
                return;
            }

            if (guard.OrderGoTo(post, hold: true, facing, null, "the other door guard is down: back to the side door", Senses))
                return;
        }
    }

    /// <summary>Why the hall's now under attack, if it is: someone of it hit or killed, or fighting with the player in it or close by.</summary>
    private string? HallUnderAttack(PlayerController player)
    {
        var eye = Senses.PlayerEye;
        var close = float.Abs(eye.X) < Level.HalfWidth + 12f && float.Abs(eye.Z) < Level.HalfLength + 12f && eye.Y < Level.WallHeight + 2f;
        foreach (var (npc, area) in _factoryArea)
        {
            if (area is not ("hall" or "office" or "door guard"))
                continue;
            if (!npc.Alive)
                return $"{npc.Name} is down";
            if (npc.Health < Npc.MaxHealth)
                return $"{npc.Name} is hit";
            if (close && npc.State == NpcState.Fighting && npc is ArmedNpc { SeesPlayerNow: true })
                return $"{npc.Name} is fighting the player ({(Level.InHall(eye) ? "in the hall" : "by the hall")})";
        }

        foreach (var guard in OfficeGuards)
        {
            if (!guard.Alive || guard.Health < Npc.MaxHealth)
                return $"{guard.Name} is {(guard.Alive ? "hit" : "down")}";
        }

        return null;
    }

    /// <summary>Everyone left standing in each outbuilding makes for the hall, by the door nearest them.</summary>
    private void PushIntoTheHall(string why)
    {
        var hall = BuildingOf("hall");
        var report = new List<string>();
        foreach (var (area, door, through) in HallPushes)
        {
            var crew = _factoryArea.Where(p => p.Value == area && p.Key.Alive).Select(p => p.Key).OfType<ArmedNpc>()
                .OrderBy(p => float3.Distance(p.Position, through[0])).ToList();
            var going = 0;
            foreach (var npc in crew)
            {
                var to = through[going % through.Length];
                var facing = float.Atan2(-to.X, -to.Z);   // (into the hall, towards its middle)
                if (npc.OrderGoTo(to, hold: false, facing, hall, $"the hall's under attack: in by the {door}", Senses))
                    going++;
            }

            report.Add($"{going} of {crew.Count} from the {area} by the {door}");
        }

        Log.Write($"[FACTORY] The hall's under attack ({why}): {string.Join(", ", report)}");
    }
}
