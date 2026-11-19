using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Rio's stair houses: in every block between two terraces, one house you can walk through, from the alley below to the
/// alley above. Its front door stands open on a ground-floor room; a concrete stair (15 risers of 18 cm, like the stair
/// alleys') climbs the side wall to an upper floor level with the alley above, and the back door stands open onto it.
/// <code>
///     looking down a stair house (here its stair on the east, +x; some have it on the west)
///
///     z1  ====[back door]=========    &lt;- the alley above (floor b + 2.7)
///         |  upper floor   |    |
///         |  (b + 2.7)     | /\ |     the stair: up the side wall, from the landing inside the front door
///         |----------------| || |
///         |  ground room   | || |     the upper floor is over the ground room too (rail along the stairwell)
///         |  (b)           | || |
///         |                |land|
///     z0  ====[front door]========    &lt;- the alley below (floor b)
/// </code>
/// Both floors are dim (<see cref="RioShadeVolumes"/>, lit only from their doors), and each is a hiding place for the
/// alley outside its door.
/// </summary>
public sealed partial class Level
{
    private const float RioStairW0 = 1.0f;        // inside: from the doors' middle away from the stair
    private const float RioStairW1 = 2.15f;       // inside: from the doors' middle to the far side of the stair
    private const float RioStairWidth = 1.05f;    // the stair, up the side wall
    private const float RioStairHouseWall = 0.15f;
    private const float RioStairLow = 2.5f;       // the ground room's ceiling (a 20 cm slab over it, the upper floor at 2.7)
    private const float RioStairHigh = 2.4f;      // the upper floor's ceiling, above that floor
    private const float RioStairTread = 0.26f;
    private const float RioStairFoot = 0.9f;      // the landing at the foot of the stair, inside the front door

    // Where in each block the stair house is wanted (by block parity; the house whose door is nearest is used): clear of the hiding places.
    private static readonly float[] RioStairHouseTargets = [5f, -9f];

    /// <summary>The stair houses, by block and west end: where their doors are (x), and which way the stair is from them (+1 east, -1 west).</summary>
    private readonly Dictionary<(int Block, float X0), (float Door, float Side)> _rioStairHouses = [];

    /// <summary>Picks a stair house in each block 0-5: the house nearest the target with room for one (beside its middle door, either way).</summary>
    private void PickRioStairHouses()
    {
        _rioStairHouses.Clear();
        for (var block = 0; block < RioStairs.Length; block++)
        {
            var target = RioStairHouseTargets[block % 2];
            RioHouse? best = null;
            var (bestDoor, bestSide) = (0f, 0f);
            foreach (var h in _rioHouses)
            {
                if (h.Block != block || h.Garden || (block == 5 && h.X0 == LajeX0) || _rioHideDoors.ContainsKey((h.Block, h.X0)))
                    continue;
                if (h.Top - h.Base < RioRise + RioStairHigh + 0.3f)
                    continue;
                var door = RioMiddleDoor(h.X0, h.X1);
                var side = 0f;
                foreach (var s in (ReadOnlySpan<float>)[1f, -1f])
                {
                    var (a, b) = (door - s * RioStairW0, door + s * RioStairW1);
                    if (float.Min(a, b) >= h.X0 + 0.25f && float.Max(a, b) <= h.X1 - 0.25f)
                    {
                        side = s;
                        break;
                    }
                }
                if (side == 0f)
                    continue;
                if (best is null || float.Abs(door - target) < float.Abs(bestDoor - target))
                    (best, bestDoor, bestSide) = (h, door, side);
            }

            if (best is { } chosen)
                _rioStairHouses[(chosen.Block, chosen.X0)] = (bestDoor, bestSide);
        }
    }

    /// <summary><paramref name="h"/>'s stair house doors and stair side, if it is one.</summary>
    private (float Door, float Side)? RioStairHouseOf(in RioHouse h) =>
        _rioStairHouses.TryGetValue((h.Block, h.X0), out var s) ? s : null;

    /// <summary>
    /// A stair house: the solid block built round its two floors and stair (Game/LevelRioStairHouses.cs's sketch), the
    /// rooms lined, the stair, a rail round the stairwell, a few things left about, their shade volumes and hiding places.
    /// </summary>
    private void BuildRioStairHouse(RioHouse h, float c, float s, float bottom, float3 wall, Material material)
    {
        var b = h.Base;
        var b2 = b + RioRise;                      // the upper floor, level with the alley above
        var low = b + RioStairLow;
        var high = b2 + RioStairHigh;
        var us = RioStairW1 - RioStairWidth;       // the stair's inner edge (local u: across, +u towards the stair)
        var (zf, zb) = (h.Z0, h.Z1);
        var zi = zf + RioStairHouseWall;           // inside the front wall
        var zw = zb - RioStairHouseWall;           // inside the back wall
        var zs0 = zi + RioStairFoot;               // the stair, from its foot ...
        var zs1 = zs0 + RioRisers * RioStairTread; // ... to the top, where the upper floor runs right across the house
        const float dh = RioHideDoorHalf;
        var rx0 = float.Min(c - s * RioStairW0, c + s * RioStairW1);
        var rx1 = float.Max(c - s * RioStairW0, c + s * RioStairW1);

        float3 P(float u, float y, float z) => new(c + s * u, y, z);
        void Solid(float u0, float y0, float z0, float u1, float y1, float z1) => RioSolid(P(u0, y0, z0), P(u1, y1, z1), wall, material);
        void Line(float u0, float y0, float z0, float u1, float y1, float z1, float3 tint, Material m) => RioBox(P(u0, y0, z0), P(u1, y1, z1), tint, m);

        // The house round it: either side, under, over.
        RioSolid(new float3(h.X0, bottom, zf), new float3(rx0, h.Top, zb), wall, material);
        RioSolid(new float3(rx1, bottom, zf), new float3(h.X1, h.Top, zb), wall, material);
        RioSolid(new float3(rx0, bottom, zf), new float3(rx1, b, zb), wall, material);
        RioSolid(new float3(rx0, high, zf), new float3(rx1, h.Top, zb), wall, material);
        // The front wall, its doorway on the ground floor; the back wall, its doorway on the upper floor.
        Solid(-RioStairW0, b, zf, -dh, high, zi);
        Solid(dh, b, zf, RioStairW1, high, zi);
        Solid(-dh, b + RioHideDoorTop, zf, dh, high, zi);
        Solid(-RioStairW0, b, zw, RioStairW1, b2, zb);
        Solid(-RioStairW0, b2, zw, -dh, high, zb);
        Solid(dh, b2, zw, RioStairW1, high, zb);
        Solid(-dh, b2 + RioHideDoorTop, zw, dh, high, zb);
        // Under the upper floor's back strip (the stair's top lands on it), and the slab over the ground room.
        Solid(-RioStairW0, b, zs1, RioStairW1, b2, zw);
        Solid(-RioStairW0, low, zi, us, b2, zs1);

        // The stair: concrete, a worn nosing on every step.
        const float riser = RioRise / RioRisers;
        var stairTint = new float3(0.47f, 0.46f, 0.43f);
        for (var i = 0; i < RioRisers; i++)
        {
            var top = b + riser * (i + 1);
            var z0 = zs0 + i * RioStairTread;
            RioSolid(P(us, b, z0), P(RioStairW1, top, z0 + RioStairTread), stairTint, Material.Floor);
            Line(us, top - 0.03f, z0 - 0.01f, RioStairW1, top + 0.002f, z0 + 0.02f, stairTint * 0.72f, Material.Floor);
        }

        // Inside: the ground room painted (a colour that goes with nothing), the stairwell and the upper floor whitewashed, cement floors.
        var whitewash = new float3(0.80f, 0.79f, 0.74f);
        var cement = new float3(0.42f, 0.41f, 0.38f);
        var paint = RioPaint[(int)(RioHash(c, b, 7.3f) * RioPaint.Length) % RioPaint.Length] * 0.85f;
        const float t = 0.008f;
        // Floors.
        Line(-RioStairW0, b, zi, us, b + t, zs1, cement, Material.Floor);
        Line(us, b, zi, RioStairW1, b + t, zs0, cement, Material.Floor);
        Line(-RioStairW0, b2, zi, us, b2 + t, zs1, cement * 1.05f, Material.Floor);
        Line(-RioStairW0, b2, zs1, RioStairW1, b2 + t, zw, cement * 1.05f, Material.Floor);
        // The ground room: its walls, its back, its ceiling.
        Line(-RioStairW0, b, zi, -RioStairW0 + t, low, zs1, paint, Material.Blockwork);
        Line(-RioStairW0, b, zi, -dh, low, zi + t, paint, Material.Blockwork);
        Line(dh, b, zi, us, low, zi + t, paint, Material.Blockwork);
        Line(-dh, b + RioHideDoorTop, zi, dh, low, zi + t, paint, Material.Blockwork);
        Line(-RioStairW0, b, zs1 - t, us, low, zs1, paint, Material.Blockwork);
        Line(-RioStairW0, low - t, zi, us, low, zs1, whitewash * 0.9f, Material.Blockwork);
        Line(-RioStairW0 + 0.01f, b + t, zi + t, -RioStairW0 + 0.02f, b + 0.12f, zs1 - 0.01f, cement * 0.8f, Material.Floor);   // skirting
        // The stairwell: the wall the stair climbs, the front wall over the landing, the slab's edge.
        Line(RioStairW1 - t, b, zi, RioStairW1, high, zw, whitewash, Material.Blockwork);
        Line(us, b, zi, RioStairW1, high, zi + t, whitewash, Material.Blockwork);
        Line(us - t, low, zi, us, b2, zs1, whitewash * 0.85f, Material.Blockwork);
        // The upper floor: its walls, the back wall round the doorway, its ceiling.
        Line(-RioStairW0, b2, zi, -RioStairW0 + t, high, zw, whitewash, Material.Blockwork);
        Line(-RioStairW0, b2, zi, us, high, zi + t, whitewash, Material.Blockwork);
        Line(-RioStairW0, b2, zw - t, -dh, high, zw, whitewash, Material.Blockwork);
        Line(dh, b2, zw - t, RioStairW1, high, zw, whitewash, Material.Blockwork);
        Line(-dh, b2 + RioHideDoorTop, zw - t, dh, high, zw, whitewash, Material.Blockwork);
        Line(-RioStairW0, high - t, zi, RioStairW1, high, zw, whitewash * 0.9f, Material.Blockwork);

        // A rail along the stairwell on the upper floor: a steel tube on posts (and a box to stop you walking off).
        var railTint = new float3(0.20f, 0.36f, 0.50f);
        Line(us - 0.05f, b2 + 0.95f, zi, us - 0.01f, b2 + 1.0f, zs1, railTint, Material.PaintedMetal);
        Line(us - 0.05f, b2 + 0.45f, zi, us - 0.01f, b2 + 0.49f, zs1, railTint, Material.PaintedMetal);
        for (var z = zi + 0.3f; z < zs1 - 0.1f; z += 1.1f)
            Line(us - 0.05f, b2, z - 0.02f, us - 0.01f, b2 + 0.95f, z + 0.02f, railTint, Material.PaintedMetal);
        Line(us - 0.05f, b2, zs1 - 0.06f, us - 0.01f, b2 + 0.95f, zs1 - 0.02f, railTint, Material.PaintedMetal);
        AddStatic(BoxBetween(RioOrigin + P(us - 0.06f, b2, zi), RioOrigin + P(us, b2 + 1.0f, zs1)), default, visible: false);

        // Bare bulbs on their flex, one to a floor.
        foreach (var (u, y, z) in (ReadOnlySpan<(float, float, float)>)[(-0.1f, low, zi + 2.4f), (0.1f, high, (zs1 + zw) / 2 - 0.6f)])
        {
            RioBeam(P(u, y, z), P(u, y - 0.35f, z), 0.012f, RioWireTint, Material.Plain);
            RioCylinder(P(u, y - 0.35f, z), P(u, y - 0.45f, z), 0.03f, new float3(0.9f, 0.88f, 0.8f), Material.Glass, 8);
        }

        // Left about: a crate by the stair downstairs; a mattress, a pillow and a blanket on the floor upstairs.
        RioSolid(P(us - 0.45f, b, zi + 2.1f), P(us - 0.03f, b + 0.42f, zi + 2.55f), new float3(0.45f, 0.33f, 0.2f), Material.Wood);
        Line(-RioStairW0 + 0.05f, b2 + t, zi + 0.15f, -0.1f, b2 + 0.18f, zi + 2.05f, new float3(0.55f, 0.52f, 0.45f), Material.Cloth);
        Line(-RioStairW0 + 0.12f, b2 + 0.18f, zi + 0.22f, -0.18f, b2 + 0.26f, zi + 0.62f, new float3(0.86f, 0.85f, 0.80f), Material.Cloth);
        Line(-RioStairW0 + 0.05f, b2 + 0.18f, zi + 1.1f, -0.1f, b2 + 0.21f, zi + 2.05f, RioPaint[(int)(RioHash(b, c, 1.7f) * RioPaint.Length) % RioPaint.Length] * 0.7f, Material.Cloth);

        // The floors as hiding places, each for the alley outside its door, and as dim rooms lit only from their doors.
        _rioHidingPlaces.Add(new HidingPlace($"alley {h.Block} stair house at x {c:0.0}", RioAt(c, b, zf), new float3(0, 0, -1),
            [RioOrigin + P(-0.6f, b, zs1 - 0.45f), RioOrigin + P(0.4f, b, zs1 - 0.45f), RioOrigin + P(-0.6f, b, zi + 1.6f)],
            BoxBetween(RioAt(rx0, b, zi), RioAt(rx1, low, zs1))));
        _rioHidingPlaces.Add(new HidingPlace($"alley {h.Block + 1} stair house at x {c:0.0} (upstairs)", RioAt(c, b2, zb), new float3(0, 0, 1),
            [RioOrigin + P(-0.6f, b2, zs1 - 0.6f), RioOrigin + P(0.4f, b2, zs1 - 0.6f), RioOrigin + P(-0.6f, b2, zs1 - 1.5f)],
            BoxBetween(RioAt(rx0, b2, zi), RioAt(rx1, high, zw))));
        _rioShade =
        [
            .. _rioShade,
            new ShadeVolume(BoxBetween(RioAt(rx0 - 0.02f, b - 0.05f, zf + 0.02f), RioAt(rx1 + 0.02f, low + 0.05f, zs1 + 0.02f)),
                RioAt(c, b + 1.1f, zf - 0.35f), 2.4f, 0.16f, NoSun: true),
            new ShadeVolume(BoxBetween(RioAt(rx0 - 0.02f, low + 0.05f, zf + 0.02f), RioAt(rx1 + 0.02f, high + 0.02f, zw + 0.02f)),
                RioAt(c, b2 + 1.1f, zb + 0.35f), 2.4f, 0.16f, NoSun: true),
        ];
    }
}
