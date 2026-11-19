using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Where the player may climb up onto things. There's no jumping: a vault (<see cref="PlayerVault"/>) gets you over anything
/// low and thin, and up onto the top of something low, except where it would end in one of <see cref="NoClimbAreas"/>.
/// The list came from <c>BRUTALFPS_CLIMBSCAN</c> (<see cref="ClimbScan"/>, written up in Docs/climb_points.md) and the
/// choices made on it: furniture (desks, tables, chairs, beds, counters), the bins, the roadhouse's fuel island, the ship's
/// bridge-wing consoles and the jungle's edge are out; the factory dock's steps, crates, drums, the range bench, the ship's
/// forward container stack, Rio's roof parapets and the factory catwalk's railing are in.
/// </summary>
public sealed partial class Level
{
    /// <summary>Places (boxes round where the feet would end up) no vault may end in: not up onto, not over into.</summary>
    public static List<(string Name, Aabb Area)> NoClimbAreas => _noClimbAreas ??= BuildNoClimbAreas();   // (built on first use: the areas' origins are other files' statics)
    private static List<(string Name, Aabb Area)>? _noClimbAreas;

    /// <summary>Whether a vault that ends with the feet at <paramref name="feet"/> is allowed.</summary>
    public bool ClimbAllowed(float3 feet)
    {
        foreach (var (_, area) in NoClimbAreas)
            if (area.Contains(feet))
                return false;
        return true;
    }

    private static List<(string Name, Aabb Area)> BuildNoClimbAreas()
    {
        var areas = new List<(string Name, Aabb Area)>();

        // From x0..x1, z0..z1, at feet heights y0..y1, all relative to the area's origin.
        void Area(string name, float3 origin, float x0, float x1, float y0, float y1, float z0, float z1) =>
            areas.Add((name, new Aabb(origin + new float3(x0, y0, z0), origin + new float3(x1, y1, z1))));

        // Round a landing found by the scan: half-widths hx, hz, and 8 cm either side of its height.
        void Around(string name, float3 origin, float x, float y, float z, float hx, float hz) =>
            Area(name, origin, x - hx, x + hx, y - 0.08f, y + 0.08f, z - hz, z + hz);

        var factory = float3.Zero;
        // Factory hall: the office's desks (0.75 m) and chairs (0.9 m); the open-topped bins, rim and inside.
        Area("factory office furniture", factory, -4.6f, 0.9f, 0.6f, 1.0f, 24.9f, 27.3f);
        Area("factory bin", factory, 14.5f, 16.5f, 0.02f, 1.25f, 5.25f, 6.75f);
        Area("factory bin", factory, 14.3f, 16.1f, 0.02f, 1.15f, 1.85f, 3.35f);
        Area("factory bin", factory, -16.4f, -14.6f, 0.02f, 1.15f, -20.75f, -19.25f);

        // Container yard: the hut's table and chair.
        Area("yard table and chair", YardOrigin, -6.2f, -4.4f, 0.6f, 1.0f, -3.6f, -1.6f);

        // Outback: the roadhouse's fuel island; the motel rooms' beds (0.6 m) and bedside cabinets (0.8 m).
        Area("roadhouse fuel island", OutbackOrigin, 6.2f, 7.8f, 0.1f, 0.3f, -94.3f, -89.7f);
        Area("motel beds and cabinets", OutbackOrigin, 22.3f, 26.5f, 0.5f, 0.9f, -120.3f, -95.8f);

        // Arctic station (its floor 0.9 m up): the galley counter, the table and the bunks.
        Area("station galley, table and bunks", ArcticOrigin, 32.8f, 39.3f, 1.5f, 1.95f, -44.1f, -40.3f);

        // Container ship: the console on each bridge wing.
        Around("bridge-wing console", ShipOrigin, -65.6f, 17.95f, -14.6f, 0.6f, 0.6f);
        Around("bridge-wing console", ShipOrigin, -65.6f, 17.95f, 14.6f, 0.6f, 0.6f);

        // Jungle: the shack's table and counter and the bench outside it, the lab's bench, the lean-to's plank counter.
        Around("shack table", JungleOrigin, -0.1f, 0.70f, 3.1f, 1.0f, 0.6f);
        Around("shack counter", JungleOrigin, 0.0f, 0.76f, 0.5f, 1.1f, 0.5f);
        Around("shack bench", JungleOrigin, 2.0f, 0.50f, -0.4f, 0.6f, 0.4f);
        Around("lab bench", JungleOrigin, 3.0f, 0.85f, -10.2f, 1.1f, 0.6f);
        Area("lean-to counter", JungleOrigin, 6.6f, 11.4f, 0.65f, 0.85f, -22.4f, -21.2f);
        // ... and the vaults over roots, logs, sacks and sandbags round the clearing that lead somewhere new.
        Area("jungle edge", JungleOrigin, -24.9f, -23.4f, 0.3f, 0.61f, 2.1f, 3.4f);
        Around("jungle edge", JungleOrigin, -10.7f, 0.70f, -18.3f, 0.9f, 0.95f);
        Around("jungle edge", JungleOrigin, 3.0f, 0.45f, -9.4f, 0.6f, 0.6f);
        Around("jungle edge", JungleOrigin, 5.5f, 0.66f, -3.3f, 0.6f, 0.6f);
        Around("jungle edge", JungleOrigin, 20.2f, 0.66f, 3.0f, 0.6f, 0.6f);
        Area("jungle edge", JungleOrigin, 21.5f, 22.8f, 0.46f, 0.92f, -12.5f, -10.9f);
        Around("jungle edge", JungleOrigin, 24.8f, 0.81f, -9.2f, 0.6f, 0.6f);
        Around("jungle edge", JungleOrigin, 24.9f, 0.43f, -7.1f, 0.6f, 0.6f);

        // Police station: the bullpen's desks and chairs, the cabinets by the tech room, the tech room's desk and chair,
        // the observation room's chairs and the vault PC's desk.
        Area("bullpen desks and chairs", PoliceOrigin, -7.1f, 7.1f, 0.6f, 1.0f, -2.0f, 6.2f);
        Area("cabinets", PoliceOrigin, -9.3f, -8.1f, 0.9f, 1.2f, -2.1f, 1.3f);
        Area("tech room desk and chair", PoliceOrigin, -14.3f, -12.1f, 0.6f, 1.0f, -7.3f, -5.2f);
        Area("observation chairs and desk", PoliceOrigin, -10.9f, -0.9f, 0.6f, 1.0f, -37.1f, -33.5f);

        // Prison: the cell bunks, and the table, bench and chair.
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(-16.6f, -5.5f), (-16.6f, -4.2f), (-5.5f, 12.8f), (4.5f, 12.8f), (-4.2f, 12.8f), (5.9f, 12.8f), (27.0f, 33.6f)])
            Around("bunk", PrisonOrigin, x, 0.47f, z, 1.1f, 1.1f);
        Area("bunks", PrisonOrigin, 16.5f, 18.7f, 0.39f, 0.55f, 19.6f, 24.8f);
        Area("bunks", PrisonOrigin, 20.5f, 22.7f, 0.39f, 0.55f, 18.8f, 22.8f);
        Area("prison table, bench and chair", PrisonOrigin, 26.5f, 29.7f, 0.37f, 0.86f, 0.1f, 3.9f);

        // The flats: the undercroft's table.
        Area("flats table", FlatsOrigin, -9.2f, -8.2f, 0.68f, 0.84f, -2.5f, -0.7f);
        return areas;
    }
}
