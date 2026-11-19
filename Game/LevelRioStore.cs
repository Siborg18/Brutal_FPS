using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Rio's storage room: behind the red door just along the bottom alley from the magenta portal (the first house of block 0
/// past the portal's wall, <see cref="RioStoreX"/>), a whitewashed room full of the Arctic's red kit: parkas hung up by their
/// hoods and insulated trousers by their waistbands, on walnut rails of double coat hooks round three walls
/// (Tools/blender/build_hanging_clothes.py makes the hanging clothes). The door is the one that was always there; E opens it
/// (inwards) and shuts it again, as the jungle shack's does. A bare bulb hangs from the ceiling; no sun gets in.
/// </summary>
public sealed partial class Level
{
    /// <summary>The house with the storage room: block 0's whose front takes in this x (local).</summary>
    private const float RioStoreX = -24.1f;

    private const float RioStoreDepth = 3.4f;     // from the front of the house, back into it
    private const float RioStoreH = 2.5f;
    private const float RioStoreSide = 0.25f;     // the house's walls either side of the room
    private const float RioStoreRail = 1.72f;     // the middle of the hook rails
    private static readonly float3 RioStoreRed = new(0.62f, 0.025f, 0.025f);   // (Npcs.ArcticKit's)

    private (RioHouse House, float Bottom, float3 Wall, Material Material)? _rioStore;
    private PrisonDoor? _rioStoreDoor;
    private float3 _rioStoreDoorAt;   // the middle of the doorway, on the floor (world)

    /// <summary>"[E] Open door" / "[E] Close door" by the storage room's door, otherwise null.</summary>
    public string? RioStoreDoorPrompt { get; private set; }

    private static bool IsRioStore(in RioHouse h) => h.Block == 0 && !h.Garden && h.X0 < RioStoreX && h.X1 > RioStoreX;

    /// <summary>
    /// The storage room, its door at <paramref name="c"/> along the house's front <paramref name="f"/> (where the facade put
    /// it): the house's solid round the room, the room lined, the hooks and the clothes, the door in its frame.
    /// <paramref name="lower"/>: the bottom storey's paint, if the house has one, drawn round the doorway.
    /// </summary>
    private void BuildRioStore(in RioFace f, float c, float floor, float3 paint, float3? lower)
    {
        if (_rioStore is not { } store)
            return;
        var (h, bottom, wall, material) = store;
        var b = h.Base;
        var (rx0, rx1) = (h.X0 + RioStoreSide, h.X1 - RioStoreSide);
        var (zf, zb, top) = (h.Z0, h.Z0 + RioStoreDepth, b + RioStoreH);
        var (d0, d1) = (c - RioHideDoorHalf, c + RioHideDoorHalf);

        // The house, solid round the room (as a hiding place's is).
        RioSolid(new float3(h.X0, bottom, h.Z0), new float3(rx0, h.Top, h.Z1), wall, material);
        RioSolid(new float3(rx1, bottom, h.Z0), new float3(h.X1, h.Top, h.Z1), wall, material);
        RioSolid(new float3(rx0, bottom, h.Z0), new float3(rx1, b, h.Z1), wall, material);       // under the floor
        RioSolid(new float3(rx0, top, h.Z0), new float3(rx1, h.Top, h.Z1), wall, material);      // over the ceiling
        RioSolid(new float3(rx0, b, zb), new float3(rx1, top, h.Z1), wall, material);            // behind it
        RioSolid(new float3(rx0, b, zf), new float3(d0, top, zf + RioHideWall), wall, material); // the front wall, either side of the door
        RioSolid(new float3(d1, b, zf), new float3(rx1, top, zf + RioHideWall), wall, material);
        RioSolid(new float3(d0, b + RioHideDoorTop, zf), new float3(d1, top, zf + RioHideWall), wall, material);
        if (lower is { } paintBelow)
        {
            RioFaceBox(f, f.U0, d0, f.Ground - 0.3f, f.Ground + RioStorey, 0, 0.012f, paintBelow, Material.Blockwork);
            RioFaceBox(f, d1, f.U1, f.Ground - 0.3f, f.Ground + RioStorey, 0, 0.012f, paintBelow, Material.Blockwork);
            RioFaceBox(f, d0, d1, f.Ground + RioHideDoorTop, f.Ground + RioStorey, 0, 0.012f, paintBelow, Material.Blockwork);
        }

        // Inside: whitewash, a cement floor and skirting, a bare bulb on its flex.
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
        RioBox(new float3(rx0 + 0.01f, b + 0.008f, zb - 0.012f), new float3(rx1 - 0.01f, b + 0.12f, zb - 0.008f), cement * 0.8f, Material.Floor);
        RioBox(new float3(rx0 + 0.008f, b + 0.008f, zi), new float3(rx0 + 0.012f, b + 0.12f, zb), cement * 0.8f, Material.Floor);
        RioBox(new float3(rx1 - 0.012f, b + 0.008f, zi), new float3(rx1 - 0.008f, b + 0.12f, zb), cement * 0.8f, Material.Floor);
        var bulb = new float3((rx0 + rx1) / 2, top, (zi + zb) / 2);
        RioBeam(bulb, bulb - new float3(0, 0.35f, 0), 0.012f, RioWireTint, Material.Plain);
        RioBox(bulb - new float3(0.035f, 0.45f, 0.035f), bulb - new float3(-0.035f, 0.36f, -0.035f), new float3(1.0f, 0.9f, 0.7f), Material.Emissive);

        // The hooks and what hangs on them: the back wall, then down each side (clear of the door's swing on its hinge side).
        var parka = TryModel("hanging_parka");
        var trousers = TryModel("hanging_snow_trousers");
        var random = new Random(1607);
        var n = 0;
        void Rail(float3 from, float3 to, float3 outward, int hooks)
        {
            // A walnut board on the wall, its hooks evenly along it.
            var along = float3.Normalize(to - from);
            var lo = from + new float3(0, RioStoreRail - 0.05f, 0);
            var hi = to + new float3(0, RioStoreRail + 0.05f, 0) + outward * 0.025f;
            RioBox(float3.Min(lo, hi), float3.Max(lo, hi), new float3(0.36f, 0.22f, 0.13f), Material.Wood);
            var length = float3.Distance(from, to);
            for (var i = 0; i < hooks; i++)
            {
                var at = from + along * (length * (i + 0.5f) / hooks) + new float3(0, RioStoreRail, 0) + outward * 0.025f;
                Hook(at, outward);
                var model = n % 2 == 0 ? parka : trousers;
                if (model is not null)
                {
                    // Hung on the hook's lower arm, its back against the wall, a little crooked.
                    var yaw = float.Atan2(outward.X, outward.Z) + RioRandom(random, -0.06f, 0.06f);
                    var hang = at + outward * 0.03f - new float3(0, 0.03f, 0);
                    var shade = RioRandom(random, 0.88f, 1.08f);
                    AddModel(model, float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(RioOrigin + hang - outward * 0.02f), RioStoreRed * shade);
                }
                n++;
            }
        }
        void Hook(float3 at, float3 outward)
        {
            // A double coat hook in dark bronze: a stem out from the board, a lower arm curling up and a longer upper one.
            var bronze = new float3(0.24f, 0.21f, 0.17f);
            RioBeam(at + new float3(0, 0.03f, 0), at + outward * 0.05f + new float3(0, 0.01f, 0), 0.014f, bronze, Material.Steel);
            RioBeam(at + outward * 0.05f + new float3(0, 0.01f, 0), at + outward * 0.075f - new float3(0, 0.025f, 0), 0.012f, bronze, Material.Steel);
            RioBeam(at + outward * 0.075f - new float3(0, 0.025f, 0), at + outward * 0.09f + new float3(0, 0.015f, 0), 0.011f, bronze, Material.Steel);
            RioBeam(at + outward * 0.05f + new float3(0, 0.01f, 0), at + outward * 0.065f + new float3(0, 0.09f, 0), 0.011f, bronze, Material.Steel);
        }
        Rail(new float3(rx0 + 0.15f, b, zb), new float3(rx1 - 0.15f, b, zb), new float3(0, 0, -1), 5);
        Rail(new float3(rx0, b, zi + 0.35f), new float3(rx0, b, zb - 0.35f), new float3(1, 0, 0), 4);
        Rail(new float3(rx1, b, zi + 0.35f), new float3(rx1, b, zb - 0.35f), new float3(-1, 0, 0), 4);

        // The door: its frame and step outside, and the leaf, hinged on the right jamb (seen from the alley), opening in.
        RioFaceBox(f, d0 - 0.05f, d0, floor, floor + RioHideDoorTop + 0.05f, 0, 0.035f, paint * 0.75f, Material.PaintedMetal);
        RioFaceBox(f, d1, d1 + 0.05f, floor, floor + RioHideDoorTop + 0.05f, 0, 0.035f, paint * 0.75f, Material.PaintedMetal);
        RioFaceBox(f, d0 - 0.05f, d1 + 0.05f, floor + RioHideDoorTop, floor + RioHideDoorTop + 0.05f, 0, 0.035f, paint * 0.75f, Material.PaintedMetal);
        RioFaceBox(f, d0, d1, floor - 0.002f, floor + 0.02f, -RioHideWall, 0.02f, RioConcrete * 0.8f, Material.Floor);

        // Built in the hinge's frame: the leaf runs west (-x) from the hinge across the doorway, its thickness out towards the alley.
        const float thick = 0.05f;
        var w = 2 * RioHideDoorHalf - 0.03f;
        var dh = RioHideDoorTop - 0.04f;
        var id = float4x4.Identity;
        var lc = new float3(-w / 2, dh / 2, -thick / 2);
        var leaf = HingedLeaf(RioOrigin + new float3(d1 - 0.01f, floor + 0.02f, zi - 0.01f), float.DegreesToRadians(95f), lc, new float3(w, dh, thick));
        OBox(leaf.B(Material.PaintedMetal, paint), id, lc, new float3(w, dh, thick));
        var trim = leaf.B(Material.PaintedMetal, paint * 0.8f);
        foreach (var (y, ph) in (ReadOnlySpan<(float, float)>)[(0.55f, 0.7f), (1.45f, 0.75f)])
            OBox(trim, id, new float3(-w / 2, y, -thick - 0.004f), new float3(w - 0.16f, ph, 0.008f));   // pressed panels on the alley side
        var steel = leaf.B(Material.Steel, RioBarTint);
        foreach (var (z, side) in (ReadOnlySpan<(float, float)>)[(-thick, -1f), (0f, 1f)])
        {
            OBox(steel, id, new float3(-w + 0.1f, 1.02f, z + side * 0.02f), new float3(0.02f, 0.02f, 0.04f));   // the handle's neck
            OBox(steel, id, new float3(-w + 0.15f, 1.02f, z + side * 0.04f), new float3(0.12f, 0.022f, 0.022f)); // and its lever
        }
        OBox(steel, id, new float3(-w + 0.1f, 0.9f, -thick - 0.004f), new float3(0.04f, 0.08f, 0.008f));      // the lock's escutcheon
        _rioStoreDoor = NewDoor("rio store", 0.7f, leaf);
        MoveDoor(_rioStoreDoor, false, instant: true);
        _rioStoreDoorAt = RioAt(c, floor, zf);

        // No sun inside; the alley's light dies away from the door, but not to nothing (the bulb's on).
        _rioShade = [.. _rioShade, new ShadeVolume(BoxBetween(RioAt(rx0 - 0.02f, b - 0.05f, zf + 0.02f), RioAt(rx1 + 0.02f, top + 0.02f, zb + 0.02f)),
            RioAt(c, b + 1.1f, zf - 0.35f), 2.6f, 0.32f, NoSun: true)];
        Log.Write($"[RIO] Storage room behind the door at x {c:0.00}: {n} hooks, {(n + 1) / 2} parkas and {n / 2} pairs of trousers");
    }

    /// <summary>The storage room's house, if its door never got built (the facade put none on it): solid, as any other.</summary>
    private void BuildRioStoreFallback()
    {
        if (_rioStore is not { } store || _rioStoreDoor is not null)
            return;
        var (h, bottom, wall, material) = store;
        RioSolid(new float3(h.X0, bottom, h.Z0), new float3(h.X1, h.Top, h.Z1), wall, material);
        Log.Write("[WARN] Rio's storage room has no door on its house's front: built solid.");
    }

    /// <summary>Near the storage room's door, E opens or shuts it. It won't shut on the player standing in the doorway.</summary>
    public void UpdateRioStoreDoor(float dt, bool usePressed, float3 player)
    {
        RioStoreDoorPrompt = null;
        if (_rioStoreDoor is not { } door)
            return;
        var at = _rioStoreDoorAt;
        var dx = player.X - at.X;
        var dz = player.Z - at.Z;
        var near = dx * dx + dz * dz < 1.7f * 1.7f && float.Abs(player.Y - at.Y) < 1.5f;
        var inDoorway = float.Abs(dx) < RioHideDoorHalf + 0.4f && dz > -0.4f && dz < RioHideWall + 1.0f && float.Abs(player.Y - at.Y) < 1.5f;
        var open = door.Target > 0.5f;
        if (near)
        {
            RioStoreDoorPrompt = open ? "[E] Close door" : "[E] Open door";
            if (usePressed && !(inDoorway && open) && MoveDoor(door, !open))
                Log.Write(open ? "[DOOR] Shutting the storage room's door (Rio)" : "[DOOR] Opening the storage room's door (Rio)");
        }
        StepDoor(door, dt);
    }
}
