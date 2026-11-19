using Brutal.Numerics;

namespace BrutalFps.Game;

/// <summary>The army that comes for the factory four minutes after a raid goes loud (Game/FactoryArmy.cs): who it is, and what counts as loud.</summary>
public sealed partial class Npcs
{
    public const string ArmyPrefix = "army ";
    private static readonly string[] ArmyKit = ["longsleeve", "trousers_work", "boots_work", "helmet"];
    private int _armyCount;

    /// <summary>
    /// Why the factory's crew know the player's there, if they do: one of them down, hit, or onto him, or shots fired
    /// (more than <paramref name="shotsBefore"/>: the count when this life began).
    /// </summary>
    public string? FactoryDetection(int shotsBefore = 0)
    {
        foreach (var (npc, _) in _factoryArea)
        {
            if (!npc.Alive)
                return $"{npc.Name} is down";
            if (npc.Health < Npc.MaxHealth)
                return $"{npc.Name} is hit";
            if (npc.State is NpcState.Fighting or NpcState.Startled)
                return $"{npc.Name} has spotted the player ({npc.State})";
        }

        return FactoryGunshots > shotsBefore ? "shots fired" : null;
    }

    /// <summary>
    /// One of the army, out of the back of a truck at <paramref name="at"/> and making for <paramref name="goTo"/> (a
    /// gunner stays where he's put, up in his armoured car's hatch). Null without people.
    /// </summary>
    public ArmedNpc? AddArmySoldier(float3 at, float heading, float3 goTo, bool gunner)
    {
        if (_model is null)
            return null;
        var i = _armyCount++;
        var profile = NpcProfile.Rifleman(gunner ? 0.75f : 0.55f) with { Armoured = true };
        var name = $"{ArmyPrefix}{(gunner ? "gunner" : "soldier")} {i + 1}";
        Waypoint[] route = gunner
            ? [new Waypoint(at, float.PositiveInfinity, heading)]
            : [new Waypoint(at, 0.4f + (i % 15) * 0.12f, heading), new Waypoint(goTo, float.PositiveInfinity, heading)];
        Add(name, SkinTones[(i * 5 + 1) % SkinTones.Length], heading, profile, ArmyKit, route);
        var npc = _people[^1] as ArmedNpc;
        if (gunner && npc is not null)
            (npc.HoldsPost, npc.TurretCrew) = (true, true);   // (on its turret's M249, if it has one: Game/NpcGunner.cs)
        return npc;
    }
}
