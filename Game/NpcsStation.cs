using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The Arctic research station's crew (Game/LevelArcticStation.cs): all seven of the level's hostiles, in the red arctic
/// kit, inside the mess room at the radio, the galley, the table, the lockers, the bunk, the heater and the window. The
/// moment the white portal is connected (a terminal links something to it: <see cref="PortalOpened"/>), four of them come
/// out (the rest stay in): the door is thrown open (<see cref="StationCrewOut"/>) and one after another, guns up, they walk out across the deck, down the steps and across the snow to the portal, 50 m
/// off, and spread out in an arc in front of it (<see cref="StationPosts"/>). There each one's route becomes that post,
/// and they move up on the water as anyone armed does when a portal comes on near them (<see cref="Npc.NoticePortal"/>),
/// then settle back on their posts, covering it.
/// <para>
/// While walking out they're under a script (no senses of their own), so it watches for them: if one is hurt, or the
/// player comes into sight of any of them, the script lets them all go at once and they fight. It happens once a life;
/// a respawn puts them back inside behind the shut door.
/// </para>
/// </summary>
public sealed partial class Npcs
{
    private const float StationWalkSpeed = 1.6f;      // m/s inside and on the steps, guns up
    private const float StationAdvanceSpeed = 2.1f;   // m/s across the snow
    private const float StationStagger = 0.9f;        // s between one leaving and the next
    private const float StationSightRange = 70f;      // m: the player in sight this near, on the way out, and the script lets go

    /// <summary>The station's door is thrown open from inside.</summary>
    public event Action? StationCrewOut;

    private sealed class StationMarcher(ArmedNpc npc, float3[] path, float delay)
    {
        public readonly ArmedNpc Npc = npc;
        public readonly float3[] Path = path;
        public float Delay = delay;
        public int Leg = -1;          // -1: not set off yet; Path.Length: arrived
        public float Health = npc.Health;
    }

    private readonly List<(ArmedNpc Npc, float3 Aisle)> _stationCrew = [];
    private readonly List<StationMarcher> _stationMarch = [];
    private Portal? _stationPortal;
    private bool _stationOut;

    /// <summary>Where each of the crew ends up: an arc 6-8 m out in front of the white portal (it faces north, -z).</summary>
    private static float3[] StationPosts =>
    [
        Level.ArcticPortalFloorPoint + new float3(4.6f, 0, -5.8f),
        Level.ArcticPortalFloorPoint + new float3(1.6f, 0, -7.6f),
        Level.ArcticPortalFloorPoint + new float3(-1.8f, 0, -7.4f),
        Level.ArcticPortalFloorPoint + new float3(-4.8f, 0, -5.6f),
    ];

    /// <summary>The crew, inside the station (called from <see cref="Populate"/>).</summary>
    private void AddStationCrew()
    {
        _stationCrew.Clear();
        _stationMarch.Clear();
        _stationOut = false;
        _stationPortal = null;
        var o = Level.ArcticOrigin;
        var f = Level.StationFloorY;
        float3 S(float x, float z) => o + new float3(x, f, z);
        // (Name, where they stand, facing, a point in the aisle by the front wall to make for first.) Facing: 0 is +z, pi is -z (the back wall).
        (string Name, float3 At, float Face, float3 Aisle)[] crew =
        [
            ("station crew 1", S(31.4f, -42.6f), float.Pi, S(31.6f, -39.4f)),              // at the lockers
            ("station crew 2", S(34.0f, -42.9f), float.Pi, S(33.6f, -39.5f)),              // at the galley counter
            ("station crew 3", S(35.2f, -39.75f), float.Pi, S(34.4f, -39.4f)),             // at the table
            ("station crew 4", S(38.0f, -42.75f), float.Pi, S(38.0f, -39.5f)),             // at the radio
            // (The three commandos who used to stand out on the snow, in here now; they stay behind when the others go out.)
            ("arctic commando 1", S(40.3f, -41.1f), -float.Pi / 2, S(40.0f, -39.5f)),      // by the bunk
            ("arctic commando 2", S(39.2f, -38.95f), 0f, S(39.6f, -39.5f)),                // warming up at the heater
            ("arctic commando 3", S(35.9f, -38.7f), 0f, S(36.6f, -39.4f)),                 // at the window, looking out
        ];
        for (var i = 0; i < crew.Length; i++)
        {
            var (name, at, face, aisle) = crew[i];
            _kitFor[name] = Kit.Arctic;
            var profile = i >= 4
                ? NpcProfile.Commando(NpcProfile.CommandoGun((i - 4) * 2 + 1)) with { NoHelmet = true }
                : i % 2 == 0
                ? NpcProfile.Commando(NpcProfile.CommandoGun(i * 3 + 2)) with { NoHelmet = true }
                : NpcProfile.Rifleman(0.6f);
            Add(name, SkinTones[(i * 3 + 2) % SkinTones.Length], face, profile, null, new Waypoint(at, float.PositiveInfinity, face));
            if (_people[^1] is ArmedNpc armed)
                _stationCrew.Add((armed, aisle));
        }
    }

    /// <summary>The white portal has just been connected: the station's crew come out and make for it (if they're not busy already).</summary>
    private void StationPortalOpened(Portal portal)
    {
        if (_stationOut || _stationCrew.Count == 0 || PortalIndex.Short(portal.Name) != "White")
            return;
        _stationOut = true;
        _stationPortal = portal;
        var posts = StationPosts;
        var wayOut = Level.StationWayOut;
        var delay = 0.5f;   // (the door goes first)
        var leaving = 0;
        foreach (var (npc, aisle) in _stationCrew)
        {
            if (leaving == posts.Length)
                break;   // (four go and look; the rest stay in the station)
            if (!npc.Alive || npc.State is not (NpcState.Calm or NpcState.Wary or NpcState.Returning))
                continue;
            var post = posts[leaving % posts.Length];
            _stationMarch.Add(new StationMarcher(npc, [aisle, .. wayOut, post], delay));
            npc.ScriptTake(npc.Position, npc.Heading);
            npc.ScriptSling(false);   // (guns up)
            delay += StationStagger;
            leaving++;
        }

        StationCrewOut?.Invoke();
        Log.Write($"[STATION] The {portal.Name} came on: {leaving} of the station's crew coming out to it");
    }

    /// <summary>Walks the crew out, leg by leg, and lets them go at their posts, or all at once if there's trouble on the way.</summary>
    private void UpdateStationMarch(float dt, PlayerController player)
    {
        if (_stationMarch.Count == 0)
            return;

        // Trouble on the way: one of them hurt, or the player in sight of any of them.
        string? trouble = null;
        foreach (var m in _stationMarch)
        {
            if (m.Leg >= m.Path.Length || !m.Npc.Alive)
                continue;
            if (m.Npc.Health < m.Health)
            {
                trouble = $"{m.Npc.Name} is hit";
                break;
            }
            if (player.Alive && m.Npc.SensesEnabled && m.Npc.Zone == Level.ZoneAt(Senses.PlayerEye))
            {
                // (Ahead of them, in a wide cone, or right beside them: they're looking where they're going, guns up.)
                var eye = m.Npc.Position + new float3(0, Npc.EyeHeight, 0);
                var to = Senses.PlayerEye - eye;
                var dist = float.Sqrt(float3.Dot(to, to));
                var ahead = new float3(float.Sin(m.Npc.Heading), 0, float.Cos(m.Npc.Heading));
                if (dist < StationSightRange && (dist < 4f || float3.Dot(to / dist, ahead) > 0.35f) && Senses.Visibility(eye, Senses.PlayerEye) > 0f)
                {
                    trouble = $"{m.Npc.Name} sees the player";
                    break;
                }
            }
        }
        if (trouble is not null)
        {
            Log.Write($"[STATION] {trouble}: the crew break off and fight");
            foreach (var m in _stationMarch)
            {
                if (m.Leg < m.Path.Length && m.Npc.Alive && m.Npc.Scripted)
                {
                    m.Npc.Repost(m.Path[^1], FaceOf(m.Path[^1], _stationPortal));
                    m.Npc.ScriptEngage(player.Position with { Y = m.Npc.Position.Y }, 0.3f);
                }
            }
            _stationMarch.Clear();
            return;
        }

        var left = 0;
        foreach (var m in _stationMarch)
        {
            if (m.Leg >= m.Path.Length)
                continue;
            if (!m.Npc.Alive)
            {
                m.Leg = m.Path.Length;
                continue;
            }
            left++;
            if (m.Leg < 0)
            {
                m.Delay -= dt;
                if (m.Delay > 0)
                    continue;
                m.Leg = 0;
                Walk(m);
                continue;
            }
            if (!m.Npc.ScriptArrived)
                continue;
            m.Leg++;
            if (m.Leg < m.Path.Length)
            {
                Walk(m);
                continue;
            }

            // At their post: it's their route now. They go and look the portal over, then stand on it, gun on the water.
            var post = m.Path[^1];
            m.Npc.Repost(post, FaceOf(post, _stationPortal));
            m.Npc.ScriptRelease();
            if (_stationPortal is { Online: true } portal)
                m.Npc.NoticePortal(portal, 0.2f + 0.3f * (float)_random.NextDouble());
            Log.Write($"[STATION] {m.Npc.Name} is at the white portal");
            left--;
        }

        if (left == 0)
            _stationMarch.Clear();

        static void Walk(StationMarcher m)
        {
            var to = m.Path[m.Leg];
            var outside = m.Leg >= m.Path.Length - 2;   // (from the foot of the steps on)
            m.Npc.ScriptFace(to + new float3(0, Npc.EyeHeight, 0));
            m.Npc.ScriptWalk(to, outside ? StationAdvanceSpeed : StationWalkSpeed);
        }
    }

    /// <summary>The heading (0 = +z) from <paramref name="at"/> to the portal's water.</summary>
    private static float FaceOf(float3 at, Portal? portal)
    {
        var to = (portal?.Center ?? Level.ArcticPortalFloorPoint) - at;
        return float.Atan2(to.X, to.Z);
    }
}
