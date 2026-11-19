using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// A small dark room behind a door off an alley that anyone, hostile or civilian, can run into and hide in. It has a few
/// <see cref="Spots"/> to crouch in, each taken by one person at a time (<see cref="Claim"/>, <see cref="Release"/>).
/// </summary>
public sealed class HidingPlace(string name, float3 doorway, float3 outward, float3[] spots, Aabb room)
{
    private readonly Npc?[] _taken = new Npc?[spots.Length];

    public string Name { get; } = name;

    /// <summary>The middle of the doorway, on the floor.</summary>
    public float3 Doorway { get; } = doorway;

    /// <summary>Out of the door, into the alley (level, unit length).</summary>
    public float3 Outward { get; } = outward;

    /// <summary>Where to crouch, on the floor, well inside.</summary>
    public IReadOnlyList<float3> Spots { get; } = spots;

    /// <summary>The room itself (its inside).</summary>
    public Aabb Room { get; } = room;

    private bool Holds(int i) => _taken[i] is { Alive: true } npc && ReferenceEquals(npc.HidingIn, this);

    /// <summary>How many spots nobody holds.</summary>
    public int Free
    {
        get
        {
            var free = 0;
            for (var i = 0; i < _taken.Length; i++)
                if (!Holds(i))
                    free++;
            return free;
        }
    }

    /// <summary>How many are in it, or on their way.</summary>
    public int Occupants => _taken.Length - Free;

    /// <summary>Takes a free spot for <paramref name="npc"/>: its index, or -1 if they're all taken.</summary>
    public int Claim(Npc npc)
    {
        for (var i = 0; i < _taken.Length; i++)
        {
            if (Holds(i) && !ReferenceEquals(_taken[i], npc))
                continue;
            _taken[i] = npc;
            return i;
        }

        return -1;
    }

    public void Release(Npc npc)
    {
        for (var i = 0; i < _taken.Length; i++)
            if (ReferenceEquals(_taken[i], npc))
                _taken[i] = null;
    }

    /// <summary>Everyone out (a respawn).</summary>
    public void Clear() => Array.Clear(_taken);
}

/// <summary>
/// Rio's hiding places: in three houses on every alley, the front door stands open on a little dark room (2 x 2.6 m, 2.4 m
/// high) cut into the house, whitewashed inside, with a cement floor and a bare bulb. No sun gets in, and the alley's
/// light dies away from the door (<see cref="RioShadeVolumes"/>), so whoever crouches at the back is hard to see. People
/// run into them when the shooting starts (<see cref="Npc"/>'s hiding: Game/NpcHiding.cs).
/// </summary>
public sealed partial class Level
{
    private const float RioHideW = 2.0f;          // inside, across the alley
    private const float RioHideD = 2.6f;          // from the front of the house, back into it
    private const float RioHideH = 2.4f;
    private const float RioHideWall = 0.15f;      // the front wall, with the door in it
    private const float RioHideDoorHalf = 0.47f;  // a 0.94 m doorway (people need 0.9 m at least)
    private const float RioHideDoorTop = 2.05f;

    // Where along each alley the three rooms are wanted (the house whose door is nearest is used): away from the main stairway and the portal.
    private static readonly float[][] RioHideTargets = [[-9f, 16f, 24f], [-19f, 6f, 24f]];

    private readonly Dictionary<(int Block, float X0), float> _rioHideDoors = [];
    private readonly List<HidingPlace> _rioHidingPlaces = [];
    private static ShadeVolume[] _rioShade = [];

    /// <summary>The favela's hiding places (empty until the level is built).</summary>
    public IReadOnlyList<HidingPlace> RioHidingPlaces => _rioHidingPlaces;

    /// <summary>The front door's slot on a house's front face, as <see cref="BuildRioFacade"/> spaces them: the middle one.</summary>
    private static float RioMiddleDoor(float x0, float x1)
    {
        var length = x1 - x0;
        var slots = int.Max(1, (int)((length - 0.8f) / 2.1f));
        var spacing = length / slots;
        return x0 + spacing * (slots / 2 + 0.5f);
    }

    /// <summary>Picks the houses with a hiding place: on each alley (blocks 0-6, their fronts), the two whose middle door is nearest the targets.</summary>
    private void PickRioHideouts()
    {
        _rioHideDoors.Clear();
        for (var block = 0; block <= 6; block++)
        {
            foreach (var target in RioHideTargets[block % 2])
            {
                RioHouse? best = null;
                var bestDoor = 0f;
                foreach (var h in _rioHouses)
                {
                    if (h.Block != block || h.Garden || (block == 5 && h.X0 == LajeX0) || _rioHideDoors.ContainsKey((h.Block, h.X0)))
                        continue;
                    var door = RioMiddleDoor(h.X0, h.X1);
                    if (door - RioHideW / 2 < h.X0 + 0.25f || door + RioHideW / 2 > h.X1 - 0.25f || h.Top - h.Base < RioHideH + 1f)
                        continue;
                    if (best is null || float.Abs(door - target) < float.Abs(bestDoor - target))
                        (best, bestDoor) = (h, door);
                }

                if (best is { } chosen)
                    _rioHideDoors[(chosen.Block, chosen.X0)] = bestDoor;
            }
        }
    }

    /// <summary>The door of <paramref name="h"/>'s hiding place (NaN: it has none).</summary>
    private float RioHideDoorOf(in RioHouse h) => _rioHideDoors.TryGetValue((h.Block, h.X0), out var c) ? c : float.NaN;

    /// <summary><paramref name="x"/>, moved along the alley if it's in front of one of <paramref name="block"/>'s hiding places' doors (a pole there would block it).</summary>
    private float RioAsideOfHideDoors(int block, float x)
    {
        foreach (var ((b, _), c) in _rioHideDoors)
        {
            if (b == block && float.Abs(x - c) < 1.0f)
                return c + (x < c ? -1.0f : 1.0f);
        }
        foreach (var ((b, _), (c, _)) in _rioStairHouses)
        {
            if (b == block && float.Abs(x - c) < 1.0f)
                return c + (x < c ? -1.0f : 1.0f);
        }

        return x;
    }

    /// <summary>
    /// A house with a hiding place: the solid block built round a room behind its front door (left, right, under, over
    /// and behind it, and a front wall with the doorway), the room lined, its shade volume and its <see cref="HidingPlace"/>.
    /// </summary>
    private void BuildRioHideoutHouse(in RioHouse h, float c, float bottom, float3 wall, Material material)
    {
        var b = h.Base;
        var (rx0, rx1) = (c - RioHideW / 2, c + RioHideW / 2);
        var (zf, zb, top) = (h.Z0, h.Z0 + RioHideD, b + RioHideH);
        var (d0, d1) = (c - RioHideDoorHalf, c + RioHideDoorHalf);

        RioSolid(new float3(h.X0, bottom, h.Z0), new float3(rx0, h.Top, h.Z1), wall, material);
        RioSolid(new float3(rx1, bottom, h.Z0), new float3(h.X1, h.Top, h.Z1), wall, material);
        RioSolid(new float3(rx0, bottom, h.Z0), new float3(rx1, b, h.Z1), wall, material);       // under the floor
        RioSolid(new float3(rx0, top, h.Z0), new float3(rx1, h.Top, h.Z1), wall, material);      // over the ceiling
        RioSolid(new float3(rx0, b, zb), new float3(rx1, top, h.Z1), wall, material);            // behind it
        RioSolid(new float3(rx0, b, zf), new float3(d0, top, zf + RioHideWall), wall, material); // the front wall, either side of the door
        RioSolid(new float3(d1, b, zf), new float3(rx1, top, zf + RioHideWall), wall, material);
        RioSolid(new float3(d0, b + RioHideDoorTop, zf), new float3(d1, top, zf + RioHideWall), wall, material);

        // Inside: whitewash gone grey, a cement floor, a bare bulb on its flex.
        var whitewash = new float3(0.80f, 0.79f, 0.74f);
        var cement = new float3(0.42f, 0.41f, 0.38f);
        var zi = zf + RioHideWall;
        RioBox(new float3(rx0, b, zi), new float3(rx1, b + 0.008f, zb), cement, Material.Floor);
        RioBox(new float3(rx0, b, zi), new float3(rx0 + 0.008f, top, zb), whitewash, Material.Blockwork);
        RioBox(new float3(rx1 - 0.008f, b, zi), new float3(rx1, top, zb), whitewash, Material.Blockwork);
        RioBox(new float3(rx0, b, zb - 0.008f), new float3(rx1, top, zb), whitewash, Material.Blockwork);
        RioBox(new float3(rx0, top - 0.008f, zi), new float3(rx1, top, zb), whitewash * 0.9f, Material.Blockwork);
        RioBox(new float3(rx0, b, zi), new float3(d0, top, zi + 0.008f), whitewash, Material.Blockwork);
        RioBox(new float3(d1, b, zi), new float3(rx1, top, zi + 0.008f), whitewash, Material.Blockwork);
        RioBox(new float3(rx0 + 0.01f, b + 0.008f, zi), new float3(rx1 - 0.01f, b + 0.12f, zi + 0.012f), cement * 0.8f, Material.Floor);   // skirting
        var bulb = new float3(c - 0.3f, top, (zi + zb) / 2);
        RioBeam(bulb, bulb - new float3(0, 0.35f, 0), 0.012f, RioWireTint, Material.Plain);
        RioCylinder(bulb - new float3(0, 0.35f, 0), bulb - new float3(0, 0.45f, 0), 0.03f, new float3(0.9f, 0.88f, 0.8f), Material.Glass, 8);
        // A couple of old crates stacked in the back corner by the hinge side (low: something to crouch behind, not to stand in the way).
        RioSolid(new float3(rx1 - 0.5f, b, zb - 0.45f), new float3(rx1 - 0.02f, b + 0.4f, zb - 0.02f), new float3(0.45f, 0.33f, 0.2f), Material.Wood);

        var name = $"alley {h.Block} hideout at x {c:0.0}";
        var spots = new[] { RioAt(c - 0.62f, b, zb - 0.45f), RioAt(c + 0.05f, b, zb - 0.45f), RioAt(c - 0.62f, b, zi + 0.95f) };
        _rioHidingPlaces.Add(new HidingPlace(name, RioAt(c, b, zf), new float3(0, 0, -1), spots,
            BoxBetween(RioAt(rx0, b, zi), RioAt(rx1, top, zb))));
        _rioShade = [.. _rioShade, new ShadeVolume(BoxBetween(RioAt(rx0 - 0.02f, b - 0.05f, zf + 0.02f), RioAt(rx1 + 0.02f, top + 0.02f, zb + 0.02f)),
            RioAt(c, b + 1.1f, zf - 0.35f), 1.9f, 0.1f, NoSun: true)];
    }

    /// <summary>The hiding place's door: a frame round the open doorway, and the leaf swung right back inside against the wall.</summary>
    /// <param name="hinge">Which jamb the leaf hangs on, along the face's u: +1 the right (u = c + 0.47), -1 the left.</param>
    private void BuildRioHideDoor(in RioFace f, float c, float floor, float3 paint, float hinge = 1f)
    {
        var (d0, d1) = (c - RioHideDoorHalf, c + RioHideDoorHalf);
        RioFaceBox(f, d0 - 0.05f, d0, floor, floor + RioHideDoorTop + 0.05f, 0, 0.035f, paint * 0.75f, Material.PaintedMetal);   // frame
        RioFaceBox(f, d1, d1 + 0.05f, floor, floor + RioHideDoorTop + 0.05f, 0, 0.035f, paint * 0.75f, Material.PaintedMetal);
        RioFaceBox(f, d0 - 0.05f, d1 + 0.05f, floor + RioHideDoorTop, floor + RioHideDoorTop + 0.05f, 0, 0.035f, paint * 0.75f, Material.PaintedMetal);
        RioFaceBox(f, d0, d1, floor - 0.002f, floor + 0.02f, -RioHideWall, 0.02f, RioConcrete * 0.8f, Material.Floor);   // the step
        // The leaf: swung in flat against the inside of its jamb (u = jamb - 0.045 .. jamb - 0.005 on the right, into the house).
        var jamb = hinge > 0 ? d1 : d0;
        var lo = RioOnFace(f, jamb - hinge * 0.045f, floor + 0.02f, -RioHideWall - 0.01f);
        var hi = RioOnFace(f, jamb - hinge * 0.005f, floor + RioHideDoorTop - 0.02f, -RioHideWall - 0.01f - 2 * RioHideDoorHalf + 0.06f);
        RioSolid(float3.Min(lo, hi), float3.Max(lo, hi), paint, Material.PaintedMetal);
        RioFaceBox(f, jamb - hinge * 0.1f, jamb - hinge * 0.045f, floor + 0.98f, floor + 1.06f, -RioHideWall - 0.7f, -RioHideWall - 0.64f, RioBarTint, Material.Steel);   // handle
    }
}
