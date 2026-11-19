using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// A way between two floors that isn't a floor: a ladder or a flight of stairs. <see cref="Way"/> runs from its bottom to
/// its top; the first and last points are on the floors at each end, somewhere a body can stand. People go along it point by
/// point, not by <see cref="NpcNav.Slide"/>. <see cref="ClimbFrom"/>: the leg that's up the rungs (-1: none), facing
/// <see cref="Facing"/>. <see cref="KeepOff"/>: the floors either end treat this as solid, so nobody walks out level over the steps.
/// </summary>
public sealed class NavLink(string name, float3[] way, int climbFrom, float3 facing, Aabb? keepOff)
{
    public string Name { get; } = name;
    public float3[] Way { get; } = way;
    public int ClimbFrom { get; } = climbFrom;
    public float3 Facing { get; } = facing;
    public Aabb? KeepOff { get; } = keepOff;
    public bool IsLadder => ClimbFrom >= 0;
    public float3 Bottom => Way[0];
    public float3 Top => Way[^1];

    /// <summary>What going along it costs, against walking on the flat (m): a ladder is slow, and you can't shoot from it.</summary>
    public float Cost
    {
        get
        {
            var length = 0f;
            for (var i = 1; i < Way.Length; i++)
                length += float3.Distance(Way[i - 1], Way[i]);
            return length * (IsLadder ? 3f : 1.3f);
        }
    }
}

/// <summary>
/// A door people can open themselves. The search goes through it as if it were open (<see cref="Level.NavPlanBoxes"/>); the
/// body stops at it until it is. Asking it to open does nothing if it's already opening.
/// </summary>
public sealed class NpcDoor(string name, bool hinged, Aabb[] shut, Aabb[] open, Func<float> openness, Func<IEnumerable<Aabb>> leaves, Func<bool> allowed, Func<bool> request)
{
    public string Name { get; } = name;

    /// <summary>It swings (it can knock someone standing in its way), rather than sliding.</summary>
    public bool Hinged { get; } = hinged;

    /// <summary>Where its leaves are, shut.</summary>
    public Aabb[] Shut { get; } = shut;

    /// <summary>Where its leaves are, open.</summary>
    public Aabb[] Open { get; } = open;

    /// <summary>0 shut .. 1 open.</summary>
    public float Openness => openness();

    /// <summary>Where its leaves stop people now (a swinging leaf stops nobody till it's there).</summary>
    public IEnumerable<Aabb> LeavesNow => leaves();

    /// <summary>Whether people may open it at the moment (not while a script is keeping it shut).</summary>
    public bool Allowed => allowed();

    /// <summary>Starts it opening: true if this is what set it off.</summary>
    public bool RequestOpen() => request();
}

public sealed partial class Level
{
    private readonly List<NavLink> _stairLinks = [];
    private List<NpcDoor>? _npcDoors;
    private readonly HashSet<string> _npcDoorKeys = [];

    /// <summary>The flights of stairs. (Ladders become links too: <see cref="NpcNav"/> works out where each one's ends are.)</summary>
    public IReadOnlyList<NavLink> StairLinks => _stairLinks;

    /// <summary>The prison's doors stay as the intro's script has them while it runs: nobody else opens them.</summary>
    public bool PrisonDoorsLocked { get; set; }

    /// <summary>The doors people can open (not the door on the flats' top landing, nor a locker or a shipping container's).</summary>
    public IReadOnlyList<NpcDoor> NpcDoors => _npcDoors ??= BuildNpcDoors();

    /// <summary>
    /// A flight of stairs from <paramref name="foot"/> (on the floor at the bottom) by way of the first and last steps to
    /// <paramref name="head"/> (on the floor at the top), <paramref name="keepOff"/> being the steps themselves.
    /// </summary>
    private void AddStairFlight(string name, float3 foot, float3 first, float3 last, float3 head, Aabb keepOff) =>
        _stairLinks.Add(new NavLink(name, [foot, first, last, head], -1, default, keepOff));

    private List<NpcDoor> BuildNpcDoors()
    {
        var doors = new List<NpcDoor>
        {
            new("factory", false, [DoorCollider(0f).Bounds], [DoorCollider(1f).Bounds], () => _doorOpen, () => [_movingBoxes[0].Bounds], () => true, () =>
            {
                if (_doorTarget > 0.5f)
                    return false;
                _doorT = _doorT is > 0f and < 1f ? 1f - _doorT : 0f;
                _doorTarget = 1f;
                Log.Write("[DOOR] Someone's opening the factory door");
                return true;
            }),
        };

        void AddLeaves(PrisonDoor door, Func<bool> allowed)
        {
            for (var i = 0; i < door.Leaves.Count; i++)
                _npcDoorKeys.Add($"door {door.Name} {i}");
            var hinged = door.Leaves.Exists(l => l.Collider(0.5f) is null);
            doors.Add(new NpcDoor(door.Name, hinged,
                [.. door.Leaves.Select(l => l.Collider(0f)!.Value)],
                [.. door.Leaves.Select(l => l.Collider(1f)!.Value)],
                () => door.Open,
                () => door.Leaves.Select(l => l.Collider(door.Open)).Where(b => b is not null).Select(b => b!.Value),
                allowed,
                () =>
                {
                    if (!MoveDoor(door, true))
                        return false;
                    Log.Write($"[DOOR] Someone's opening the {door.Name} door");
                    return true;
                }));
        }

        foreach (var name in (ReadOnlySpan<string>)["entrance", "sally", "grille", "yard"])
        {
            if (_prisonDoors.TryGetValue(name, out var door))
                AddLeaves(door, () => !PrisonDoorsLocked);
        }

        if (_factoryRearDoor is { } factoryRear)
            AddLeaves(factoryRear, () => true);
        if (_factorySideDoor is { } factorySide)
            AddLeaves(factorySide, () => true);
        if (_bodegaDoor is { } shack)
            AddLeaves(shack, () => true);
        if (_stationDoor is { } station)
            AddLeaves(station, () => true);
        if (_flatsStairDoor is { } stairs)
            AddLeaves(stairs, () => true);
        foreach (var motel in _motelDoors)
            AddLeaves(motel, () => true);
        return doors;
    }

    /// <summary>
    /// What a search for a way should take to be in the way: everything that moves or comes and goes (an offline portal's
    /// blocker, a door nobody may open as it is now), but a door people can open stands open, and is in the way only there.
    /// </summary>
    public void NavPlanBoxes(List<Aabb> into)
    {
        into.Clear();
        var factory = NpcDoors[0];
        foreach (var box in _movingBoxes)
        {
            if (box.Collides && !(factory.Allowed && box.Bounds == _movingBoxes[0].Bounds))
                into.Add(box.Bounds);
        }

        foreach (var door in NpcDoors)
        {
            if (door.Allowed)
                into.AddRange(door.Open);
        }

        var locked = _prisonDoors.Count > 0 && PrisonDoorsLocked;
        foreach (var (key, box) in _blockers)
        {
            if (!box.Collides)
                continue;
            if (_npcDoorKeys.Contains(key) && !(locked && IsPrisonDoorKey(key)))
                continue;
            into.Add(box.Bounds);
        }
    }

    private static bool IsPrisonDoorKey(string key) => key.StartsWith("door entrance", StringComparison.Ordinal) || key.StartsWith("door sally", StringComparison.Ordinal)
        || key.StartsWith("door grille", StringComparison.Ordinal) || key.StartsWith("door yard", StringComparison.Ordinal);
}
