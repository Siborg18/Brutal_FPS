using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The roadhouse motel's gunmen (Game/LevelMotel.cs): <see cref="Level.MotelGunmenPerRoom"/> waiting in each of the seven
/// rooms, out of sight behind the shut door. The moment the player comes within <see cref="MotelAmbushRange"/> of any
/// room's door, the whole motel goes off at once: every door with someone alive behind it is thrown open
/// (<see cref="MotelDoorSprung"/>: Program has the level swing it) and everyone inside starts shooting, the nearest first.
/// It goes off once a life; a respawn puts everyone back behind shut doors (<see cref="MotelReset"/>).
/// <para>
/// Until then they're ordinary hostiles standing at their posts: a gunshot nearby still brings them out (they open the door
/// themselves, as anyone does a door in their way), and a door the player opens first finds them waiting.
/// </para>
/// </summary>
public sealed partial class Npcs
{
    /// <summary>m from a room's door at which the motel's ambush goes off.</summary>
    public const float MotelAmbushRange = 15f;

    private const float MotelFirstShot = 0.55f;   // s after the doors go before the nearest fire (it takes that long to swing)
    private const float MotelStagger = 0.12f;     // s more for each metre further from the player the room is
    private const float MotelStaggerMax = 2.5f;   // s: the far end of the block is in it soon enough

    private readonly List<(ArmedNpc Npc, int Room)> _motelGunmen = [];
    private bool _motelSprung;

    /// <summary>A motel room's door is thrown open from inside (the room, 0 to <see cref="Level.MotelRooms"/> - 1).</summary>
    public event Action<int>? MotelDoorSprung;

    /// <summary>Everyone's back after a respawn: the motel's doors should all be shut again.</summary>
    public event Action? MotelReset;

    private static readonly string[][] MotelKit =
    [
        ["tshirt", "jeans", "boots_work", "cap"], ["longsleeve", "trousers_work", "boots_work", "beanie"],
        ["hoodie", "jeans", "sneakers"], ["work_jacket", "trousers_work", "boots_work"],
    ];

    /// <summary>The gunmen in every room, waiting behind its door (called from <see cref="Populate"/>).</summary>
    private void AddMotelGunmen()
    {
        _motelGunmen.Clear();
        _motelSprung = false;
        for (var room = 0; room < Level.MotelRooms; room++)
        {
            for (var spot = 0; spot < Level.MotelGunmenPerRoom; spot++)
            {
                var n = room * Level.MotelGunmenPerRoom + spot;   // (every gunman has his own number, so his own rolls)
                var name = spot == 0 ? $"motel room {room + 1}" : $"motel room {room + 1}{(char)('a' + spot)}";
                // Alternately a long gun and a sidearm (the actual gun is rolled from those by name), every third in armour.
                var profile = n % 2 == 0 ? NpcProfile.Rifleman(0.42f + 0.03f * (n % 3)) : NpcProfile.Gunman(0.45f + 0.03f * (n % 3));
                profile = profile with { Armoured = n % 3 == 1 };
                if (n == 0)
                    profile = NpcProfile.Commando();   // (room 1's: a commando, waiting with the rest)
                Add(name, SkinTones[(n * 5 + 2) % SkinTones.Length], Level.MotelFacing, profile, MotelKit[n % MotelKit.Length],
                    new Waypoint(Level.MotelHideAt(room, spot), float.PositiveInfinity, Level.MotelFacing));
                if (_people[^1] is ArmedNpc gunman)
                    _motelGunmen.Add((gunman, room));
            }
        }
    }

    /// <summary>The player within range of any room's door: every door goes, and everyone behind them comes out shooting.</summary>
    private void UpdateMotelAmbush(float3 player, bool playerAlive)
    {
        if (_motelSprung || !playerAlive || _motelGunmen.Count == 0)
            return;
        var trigger = -1;
        var nearest = float.MaxValue;
        for (var room = 0; room < Level.MotelRooms; room++)
        {
            var d = DoorDistance(room, player);
            if (d < nearest && float.Abs(player.Y - Level.MotelDoorAt(room).Y) <= 6f)
                (trigger, nearest) = (room, d);
        }
        if (trigger < 0 || nearest > MotelAmbushRange)
            return;

        _motelSprung = true;
        Log.Write($"[MOTEL] The player's {nearest:0.0} m from room {trigger + 1}'s door: the whole motel opens up");
        var opened = new bool[Level.MotelRooms];
        var engaged = 0;
        foreach (var (gunman, room) in _motelGunmen)
        {
            if (!gunman.Alive)
                continue;   // (shot through the wall, or found earlier)
            if (!opened[room])
            {
                opened[room] = true;
                MotelDoorSprung?.Invoke(room);
            }
            if (gunman.State is NpcState.Calm or NpcState.Wary or NpcState.Startled or NpcState.Returning)
            {
                var delay = MotelFirstShot + float.Min(MotelStaggerMax, MotelStagger * (DoorDistance(room, player) - nearest));
                gunman.ScriptEngage(player with { Y = gunman.Position.Y }, delay);
                engaged++;
            }
        }
        Log.Write($"[MOTEL] {opened.Count(o => o)} doors thrown open, {engaged} gunmen out shooting");
    }

    private static float DoorDistance(int room, float3 player)
    {
        var door = Level.MotelDoorAt(room);
        var dx = player.X - door.X;
        var dz = player.Z - door.Z;
        return float.Sqrt(dx * dx + dz * dz);
    }
}
