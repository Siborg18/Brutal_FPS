using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The three armed officers who ride in the back of the delivery van with the player on a factory raid (<see cref="FactoryRaid"/>).
/// On the way they stand in the load space, rifles up, facing the back doors. When the doors open they go first, one after
/// the other: down the middle of the van, a hop off the sill, and they wait either side of the doors while the player climbs
/// out. Then they take cover at the back of the van, one at each back corner watching down its flanks and one by the hall's
/// wall, and the script lets go of them: they hold there (<see cref="ArmedNpc.HoldsPost"/>) and shoot any hostile who shows.
/// <para>
/// Everything here is in the frame of the van where it parks (<see cref="DeliveryVan.ParkedAt"/>): nose towards -Z, back doors
/// towards +Z, its back 2.99 m behind its middle (it parks side on to the hall, its back to the west, and drives off once
/// everyone's out: <see cref="FactoryRaid"/>).
/// </para>
/// </summary>
public sealed class RaidOfficers
{
    private const float F = DeliveryVan.FloorHeight;
    private const float AisleX = 0.05f;          // down the middle, between the load on the left and the box by the doors on the right
    private const float SillZ = 2.95f;           // at the open doors, still on the floor
    private const float LandZ = 3.65f;           // on the ground, having hopped down
    private const float InsideSpeed = 1.8f, HopSpeed = 2.6f, OutSpeed = 2.0f, CoverSpeed = 2.4f;
    private const float Stagger = 0.45f;         // s between one setting off and the next

    // Where each rides, nearest the doors first (so first out). The player sits at (0.34, -0.32), against the bulkhead.
    private static readonly float3[] Rides = [new(AisleX, F, 2.1f), new(0.47f, F, 1.25f), new(0.47f, F, 0.45f)];

    // Where they wait while the player climbs out: either side of the doors, clear of the way down.
    private static readonly float3[] Waits = [new(-1.0f, 0, 4.15f), new(1.0f, 0, 4.15f), new(1.05f, 0, 5.0f)];

    // Their cover: the van's two back corners, and against the hall's wall just west of its big door (the van drives off, but
    // the wall stays), watching out across the yard to the north-east; and what each watches.
    private static readonly float3[] Covers = [new(-1.45f, 0, 2.7f), new(1.45f, 0, 2.7f), new(5.98f, 0, -1.99f)];
    private static readonly float3[] Watches = [new(-9f, 0, -1.5f), new(9f, 0, -1.5f), new(-6.02f, 0, -16.99f)];

    /// <summary>Their cover in the world, and which way each faces there: where the people put them back after a respawn.</summary>
    public static (float3 At, float Heading)[] Posts =>
        [.. Covers.Select((c, i) => (DeliveryVan.ParkedAt(c), FacingFrom(DeliveryVan.ParkedAt(c), DeliveryVan.ParkedAt(Watches[i]))))];

    private readonly Npcs _npcs;
    private readonly DeliveryVan _van;
    private ArmedNpc[]? _officers;
    private readonly List<(float3 To, float Speed)>[] _legs = [[], [], []];
    private readonly float[] _delay = new float[3];
    private readonly bool[] _faced = new bool[3];
    private enum Stage { Riding, GettingOut, Waiting, TakingCover, Released }
    private Stage _stage = Stage.Riding;
    private int _respawns = int.MinValue;

    public RaidOfficers(Npcs npcs, DeliveryVan van)
    {
        _npcs = npcs;
        _van = van;
        _officers = npcs.AddRaidOfficers(Posts);
    }

    /// <summary>
    /// A respawn has made new officers (at their cover: <see cref="Npcs.ApplyRespawn"/>): back in the van with the player, the
    /// van parked and its doors about to open (<see cref="FactoryRaid.RestartNearEnd"/>).
    /// </summary>
    public void Restart(PlayerController player)
    {
        _officers = _npcs.RaidOfficersNow is { Length: > 0 } now ? now[..int.Min(now.Length, Rides.Length)] : null;
        Start(player);
    }

    /// <summary>Into the van with the player, rifles up, before it sets off.</summary>
    public void Start(PlayerController player)
    {
        _respawns = player.Respawns;
        _stage = Stage.Riding;
        if (_officers is null)
            return;
        foreach (var officer in _officers)
        {
            officer.ScriptTake(officer.Position, officer.Heading);
            officer.ScriptSling(false);
        }
        Ride();
        Log.Write("[RAID] Three officers in the back of the van");
    }

    /// <summary>Gone (a respawn has made new ones), dead, or not there at all: nothing to move.</summary>
    private bool Gone(PlayerController player) => _officers is null || player.Respawns != _respawns;

    public void Update(float dt, PlayerController player)
    {
        if (Gone(player) || _stage == Stage.Released)
            return;
        if (_stage == Stage.Riding)
        {
            Ride();
            return;
        }

        for (var i = 0; i < _officers!.Length; i++)
        {
            var officer = _officers[i];
            if (!officer.Alive)
                continue;
            if (_delay[i] > 0f)
            {
                _delay[i] -= dt;
                if (_delay[i] > 0f)
                    continue;
            }
            if (!officer.ScriptArrived)
                continue;
            if (_legs[i].Count > 0)
            {
                var (to, speed) = _legs[i][0];
                _legs[i].RemoveAt(0);
                officer.ScriptFace(to);
                officer.ScriptWalk(to, speed);
            }
            else if (!_faced[i])
            {
                // Where this leg of it ends: looking out the way they cover, waiting or in cover.
                _faced[i] = true;
                officer.ScriptFace(World(Watches[i]));
            }
        }
    }

    /// <summary>Standing in the load space wherever the van is, facing the back doors.</summary>
    private void Ride()
    {
        for (var i = 0; i < _officers!.Length; i++)
        {
            var at = _van.WorldPoint(Rides[i]);
            _officers[i].ScriptTake(at, FacingFrom(at, _van.WorldPoint(Rides[i] + new float3(0, 0, 1f))));
        }
    }

    /// <summary>The doors are open: out they go, the one nearest the doors first.</summary>
    public void GetOut(bool instant)
    {
        if (_officers is null || _stage != Stage.Riding)
            return;
        _stage = Stage.GettingOut;
        for (var i = 0; i < _officers.Length; i++)
        {
            _faced[i] = false;
            _delay[i] = i * Stagger;
            _legs[i].Clear();
            if (instant)
            {
                Place(i, Waits[i], World(Watches[i]));
                continue;
            }
            var ride = Rides[i];
            if (float.Abs(ride.X - AisleX) > 0.01f)
                _legs[i].Add((World(new float3(AisleX, F, ride.Z)), InsideSpeed));
            _legs[i].Add((World(new float3(AisleX, F, SillZ)), InsideSpeed));
            _legs[i].Add((World(new float3(AisleX, 0, LandZ)), HopSpeed));
            _legs[i].Add((World(Waits[i]), OutSpeed));
        }
        Log.Write("[RAID] The officers get out of the van");
    }

    /// <summary>The player's out too: to their cover at the back of the van.</summary>
    public void TakeCover(bool instant)
    {
        if (_officers is null || _stage is Stage.TakingCover or Stage.Released)
            return;
        if (_stage == Stage.Riding)
            GetOut(instant: true);
        _stage = Stage.TakingCover;
        for (var i = 0; i < _officers.Length; i++)
        {
            _faced[i] = false;
            _delay[i] = (2 - i) * 0.2f;   // (the last out goes first: they're all clear of each other's way)
            _legs[i].Clear();
            if (instant)
                Place(i, Covers[i], World(Watches[i]));
            else
                _legs[i].Add((World(Covers[i]), CoverSpeed));
        }
        Log.Write("[RAID] The officers take cover behind the van");
    }

    /// <summary>The script's over: they're at their cover (put there now if they aren't), and on their own from here.</summary>
    public void Release(PlayerController player)
    {
        if (Gone(player) || _stage == Stage.Released)
            return;
        TakeCover(instant: true);
        _stage = Stage.Released;
        foreach (var officer in _officers!)
        {
            if (!officer.Alive)
                continue;
            officer.HoldsPost = true;
            officer.ScriptRelease();
        }
        Log.Write("[RAID] The officers hold the back of the van");
    }

    private void Place(int i, float3 local, float3 lookAt)
    {
        var officer = _officers![i];
        if (!officer.Alive)
            return;
        var at = World(local);
        officer.ScriptPlace(at, FacingFrom(at, lookAt));
        officer.ScriptFace(lookAt);
        _faced[i] = true;
        _delay[i] = 0f;
    }

    private static float3 World(float3 local) => DeliveryVan.ParkedAt(local);

    private static float FacingFrom(float3 from, float3 to) => float.Atan2(to.X - from.X, to.Z - from.Z);
}
