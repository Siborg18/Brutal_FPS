using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Hostiles in the container yard at Antwerp (ten) and on the container ship (fifty). Some walk beats on the ground or the
/// deck; some keep watch from the tops of the container stacks. In the yard there's one ladder, up the west block's door end; on the ship
/// some go up and down the ladders up the stacks (<see cref="Level.ShipStacks"/>: the nav takes them by their links).
/// </summary>
public sealed partial class Npcs
{
    private static readonly string[][] DockKit =
    [
        ["hivis_vest", "trousers_work", "boots_work", "cap"], ["work_jacket", "jeans", "boots_work", "beanie"],
        ["hoodie", "trousers_work", "sneakers", "beanie"], ["longsleeve", "jeans", "boots_work"],
    ];

    /// <summary>One of the dock's hostiles: AKs mostly, a pistol now and then, every fourth in body armour.</summary>
    private void AddDockHand(string name, int i, float facing, params Waypoint[] route) => AddDockHand(name, i, facing, false, route);

    /// <summary>As above; <paramref name="commando"/>: one of the rare commandos instead (<see cref="NpcProfile.Commando"/>).</summary>
    private void AddDockHand(string name, int i, float facing, bool commando, params Waypoint[] route)
    {
        var accuracy = 0.3f + 0.03f * (i % 5);
        var profile = commando ? NpcProfile.Commando(NpcProfile.CommandoGun(i))
            : (i % 4 == 3 ? NpcProfile.Gunman(accuracy) : NpcProfile.Rifleman(accuracy)) with { Armoured = i % 4 == 0 };
        Add(name, SkinTones[i % SkinTones.Length], facing, profile, DockKit[i % DockKit.Length], route);
    }

    /// <summary>The yard at Antwerp: ten of them, four about the yard and six up on the stacks round it, and three more at cards.</summary>
    private void AddYardCrew()
    {
        var o = Level.YardOrigin;
        const float two = 5.18f, three = 7.77f;   // the tops of the stacks, two and three high
        Waypoint Y(float x, float y, float z, float pause, float face) => new(o + new float3(x, y, z), pause, face);
        const float east = float.Pi / 2, west = -float.Pi / 2, north = 0f, south = float.Pi;   // (headings: 0 looks along +Z)
        (float Face, Waypoint[] Route)[] beats =
        [
            // On the west block, over the open container, watching the yard.
            (east, [Y(4.6f, two, -8.0f, 7, east), Y(4.6f, two, -1.5f, 5, east)]),
            (east, [Y(4.6f, two, 1.5f, 6, east), Y(4.6f, two, 8.0f, 6, east)]),
            // Along the grey containers on the inner rows (three high), looking down into the yard.
            (south, [Y(8.5f, three, Level.YardInnerRowZ, 6, south), Y(16.0f, three, Level.YardInnerRowZ, 5, south)]),
            (north, [Y(16.0f, three, -Level.YardInnerRowZ, 6, north), Y(8.5f, three, -Level.YardInnerRowZ, 5, north)]),
            // On the east stacks.
            (west, [Y(25.3f, two, -8.5f, 6, west), Y(25.3f, two, -1.0f, 5, west)]),
            // On the north end of the west block, where it meets the north row, looking across the yard.
            (east, [Y(2.0f, two, 9.5f, 5, east), Y(-4.0f, two, 10.3f, 6, south)]),
            // On the ground: by the drums, either side of the lone container, and over towards the east stacks.
            (west, [Y(9.5f, 0, -5.8f, 5, west), Y(12.0f, 0, -1.0f, 5, west)]),
            (east, [Y(20.5f, 0, -6.5f, 4, east), Y(22.0f, 0, 1.0f, 5, west)]),
            (west, [Y(13.0f, 0, 5.6f, 5, west), Y(13.0f, 0, -1.5f, 4, south)]),
            (east, [Y(11.5f, 0, -5.8f, 4, east), Y(19.0f, 0, 5.5f, 4, west)]),
        ];
        for (var i = 0; i < beats.Length; i++)
            AddDockHand($"yard guard {i + 1}", i, beats[i].Face, i == 7, beats[i].Route);   // (the eighth, on the ground: a commando)

        // Three more, off duty: sat playing cards at the garden table up the lane past the pallets, their guns on the
        // ground by their chairs, until something alerts them (Game/LevelYardCards.cs, Game/NpcSeat.cs).
        var seats = Level.YardCardSeats;
        for (var k = 0; k < seats.Count; k++)
        {
            var (seat, heading) = seats[k];
            AddDockHand($"yard card player {k + 1}", 12 + k, heading, new Waypoint(seat, float.PositiveInfinity, heading));
            _people[^1].SeatAt(heading);
        }
    }

    /// <summary>
    /// The container ship: fifty of them. On each stack with a ladder up it, one walks its top and one goes up and down; the
    /// rest of the stacks' watchers stand on the tallest tops (no way down: they hold up there); the others walk the deck,
    /// the walkways either side of the hatches, the stern, the forecastle, and the deck under the bridge.
    /// </summary>
    private void AddShipCrew()
    {
        const int Total = 50;
        var stacks = Level.ShipStacks;
        var bays = Level.ShipBays;
        if (stacks.Count == 0 || bays.Count < 8)
            return;
        var s0 = Level.ShipOrigin;
        Waypoint S(float x, float y, float z, float pause, float face) => new(s0 + new float3(x, y, z), pause, face);
        const float fwd = float.Pi / 2, aft = -float.Pi / 2;
        var beats = new List<(float Face, Waypoint[] Route)>();

        // The deck: the walkways either side of the hatches, two bays at a time.
        float Walkway(int bay, float side)
        {
            var b = bays[bay];
            var hb = float.Min(Level.ShipHalfBreadth(b.X0), Level.ShipHalfBreadth(b.X1)) - 0.12f;
            return side * (b.CoverHalf + hb) / 2;
        }
        foreach (var j in (ReadOnlySpan<int>)[0, 1, 3, 4, 6, 7])
        {
            foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            {
                var (a, b) = (bays[j], bays[j + 1]);
                var look = side > 0 ? 0f : float.Pi;   // out over the side
                beats.Add((fwd, [S((a.X0 + a.X1) / 2, 0, Walkway(j, side), 5, look), S((b.X0 + b.X1) / 2, 0, Walkway(j + 1, side), 4, aft)]));
            }
        }

        // The stern: beside the engine casing and beside the accommodation block.
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            beats.Add((fwd, [S(-88f, 0, side * 10.5f, 5, fwd), S(-80.5f, 0, side * 10.5f, 4, side > 0 ? 0f : float.Pi)]));
            beats.Add((fwd, [S(-76f, 0, side * 13.5f, 4, side > 0 ? 0f : float.Pi), S(-68f, 0, side * 13.5f, 6, fwd)]));
        }
        // The forecastle, looking back down the ship.
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            beats.Add((aft, [S(81f, 2.6f, side * 6.5f, 6, aft), S(88.5f, 2.6f, side * 1f, 4, side > 0 ? 0f : float.Pi)]));
        // The deck under the bridge front, either side of the ladder.
        beats.Add((fwd, [S(-64.3f, 0, -9f, 5, fwd), S(-64.3f, 0, -1.5f, 5, fwd)]));
        beats.Add((fwd, [S(-64.3f, 0, 1.5f, 5, fwd), S(-64.3f, 0, 7.5f, 5, fwd)]));

        // The stacks with ladders: one on top, one going up and down.
        var top = new List<(float Face, Waypoint[] Route)>();
        foreach (var run in stacks.Where(r => r.LadderFoot is not null))
        {
            var box = run.Box;
            var (y, zc) = (box.Max.Y - s0.Y, box.Center.Z - s0.Z);
            var (xa, xb) = (box.Min.X - s0.X + 1.5f, box.Max.X - s0.X - 1.5f);
            var look = zc >= 0 ? 0f : float.Pi;
            top.Add((fwd, [S(xa, y, zc, 6, aft), S(xb, y, zc, 6, look)]));
            var foot = run.LadderFoot!.Value - s0;
            var far = float.Abs(foot.X - xa) > float.Abs(foot.X - xb) ? xa : xb;
            top.Add((fwd, [S(foot.X, 0, foot.Z, 5, look), S(far, y, zc, 7, aft)]));
        }

        // The rest on the tallest of the other tops, watching aft towards the bridge or out to the side.
        var watchers = Total - beats.Count - top.Count;
        foreach (var run in stacks.Where(r => r.LadderFoot is null).OrderByDescending(r => r.Box.Max.Y).ThenByDescending(r => r.Box.Size.Z).Take(int.Max(watchers, 0)))
        {
            var box = run.Box;
            var (y, zc) = (box.Max.Y - s0.Y, box.Center.Z - s0.Z);
            var look = run.Bay % 2 == 0 ? aft : zc >= 0 ? 0f : float.Pi;
            top.Add((aft, [S(box.Min.X - s0.X + 2f, y, zc, 8, look), S(box.Max.X - s0.X - 2f, y, zc, 6, aft)]));
        }
        beats.AddRange(top);

        for (var i = 0; i < int.Min(beats.Count, Total); i++)
        {
            _kitFor[$"deckhand {i + 1}"] = Kit.HiVis;   // (the crew in hi-vis waterproofs: Npcs.HiVisKit)
            AddDockHand($"deckhand {i + 1}", i, beats[i].Face, i == 0, beats[i].Route);   // (the first, on the deck: a commando)
        }
        if (beats.Count < Total)
            Log.Write($"[WARN] Only {beats.Count} places for the ship's crew of {Total}");
    }
}
