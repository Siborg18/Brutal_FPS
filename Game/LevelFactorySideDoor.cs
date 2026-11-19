using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// A plain steel door at floor level through the hall's south wall, in the bay between the office and the west wall: the way
/// in nearest the outbuilding. It opens into the hall, hinged on its east side. Outside, two of the crew stand guard either
/// side of it (<see cref="Npcs"/>: the door guards), and at least one of them always stays there.
///
///     +--[side door]--+-----[office]-----+--------------[rear door]-------+     south wall (outside is down the page)
///       guard  guard
/// </summary>
public sealed partial class Level
{
    /// <summary>The side door's opening (x along the south wall) and height.</summary>
    public const float SideDoorX0 = -14.1f, SideDoorX1 = -12.9f, SideDoorHeight = 2.2f;

    /// <summary>The middle of the side door's opening, on the floor, in the wall's thickness.</summary>
    public static readonly float3 SideDoorCenter = new((SideDoorX0 + SideDoorX1) / 2, 0, HalfLength + WallThickness / 2);

    private const float SideDoorHingeZ = HalfLength - 0.04f;   // (just inside the old wall's inner face)

    private PrisonDoor? _factorySideDoor;

    /// <summary>"[E] Open door" / "[E] Close door" while the player is at the hall's side door, otherwise null.</summary>
    public string? FactorySideDoorPrompt { get; private set; }

    /// <summary>The hall's side door (by the outbuilding) is open or opening.</summary>
    public bool FactorySideDoorOpening => _factorySideDoor is { Target: > 0.5f };

    /// <summary>Call once the hall's skin is up: the door's cut through it.</summary>
    private void BuildFactorySideDoor()
    {
        var top = SideDoorHeight;
        var wallFace = HalfLength;
        var skinOut = HalfLength + WallThickness + SkinIn + SkinT;
        // (From y = 0 exactly, as the big door's: Overlaps is strict, so the floor slab and the grass are left alone.)
        CarveOpening(new Aabb(new float3(SideDoorX0, 0f, wallFace - 0.05f), new float3(SideDoorX1, top, skinOut + 0.05f)));

        // A concrete surround on the outside (over the brick plinth), steel angle round the opening inside, a worn steel sill.
        foreach (var x in (ReadOnlySpan<float>)[SideDoorX0 - 0.12f, SideDoorX1 + 0.12f])
            FBox(new float3(x - 0.12f, 0, skinOut - 0.02f), new float3(x + 0.12f, top + 0.25f, skinOut + 0.1f), FrameConcrete, Material.Concrete);
        FBox(new float3(SideDoorX0 - 0.24f, top, skinOut - 0.02f), new float3(SideDoorX1 + 0.24f, top + 0.25f, skinOut + 0.12f), FrameConcrete, Material.Concrete);
        var steel = OldSteel * 0.85f;
        foreach (var x in (ReadOnlySpan<float>)[SideDoorX0 - 0.03f, SideDoorX1 + 0.03f])
        {
            FBox(new float3(x - 0.05f, 0, wallFace - 0.1f), new float3(x + 0.05f, top + 0.1f, wallFace + 0.02f), steel, Material.RustyMetal, collides: false);
            FBox(new float3(x - 0.03f, 0, wallFace), new float3(x + 0.03f, top, skinOut), steel, Material.RustyMetal, collides: false);
        }
        FBox(new float3(SideDoorX0 - 0.08f, top, wallFace - 0.1f), new float3(SideDoorX1 + 0.08f, top + 0.1f, wallFace + 0.02f), steel, Material.RustyMetal, collides: false);
        FBox(new float3(SideDoorX0, top - 0.03f, wallFace), new float3(SideDoorX1, top, skinOut), steel, Material.RustyMetal, collides: false);
        FBox(new float3(SideDoorX0, 0, wallFace - 0.02f), new float3(SideDoorX1, 0.006f, skinOut + 0.1f), OldSteel, Material.Steel, collides: false, blocksShots: false);
        // A bulkhead lamp over it, outside.
        FBox(new float3(-13.62f, top + 0.35f, skinOut + 0.1f), new float3(-13.38f, top + 0.55f, skinOut + 0.22f), new float3(0.2f, 0.2f, 0.19f), Material.Steel, collides: false);
        FBox(new float3(-13.58f, top + 0.37f, skinOut + 0.22f), new float3(-13.42f, top + 0.53f, skinOut + 0.23f), new float3(0.75f, 0.72f, 0.6f), Material.Plain, collides: false, blocksShots: false);

        // The leaf: flush steel in faded blue-grey paint, hinged on its east edge, swinging into the hall. Built in the hinge's
        // frame: it runs west (-x) from the hinge, its outside face towards +z.
        const float w = SideDoorX1 - SideDoorX0 - 0.02f, h = SideDoorHeight - 0.03f, thick = 0.05f;
        var id = float4x4.Identity;
        var c = new float3(-w / 2 - 0.01f, h / 2, thick / 2);
        var leaf = HingedLeaf(new float3(SideDoorX1, 0.015f, SideDoorHingeZ), float.DegreesToRadians(-100f), c, new float3(w, h, thick));
        var paint = new float3(0.27f, 0.31f, 0.34f);
        var worn = new float3(0.16f, 0.17f, 0.18f);
        OBox(leaf.B(Material.Plain, paint), id, c, new float3(w, h, thick));
        foreach (var face in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var z = thick / 2 + face * (thick / 2 + 0.004f);
            OBox(leaf.B(Material.Plain, worn), id, c with { Y = 0.18f, Z = z }, new float3(w - 0.08f, 0.3f, 0.008f));   // the kick plate
            foreach (var y in (ReadOnlySpan<float>)[0.75f, 1.5f])
                OBox(leaf.B(Material.Plain, paint * 0.86f), id, c with { Y = y, Z = z }, new float3(w - 0.12f, 0.03f, 0.008f));
            var handle = thick / 2 + face * (thick / 2 + 0.05f);
            OBox(leaf.B(Material.Plain, new float3(0.46f, 0.46f, 0.47f)), id, new float3(-w + 0.11f, 1.02f, handle), new float3(0.03f, 0.3f, 0.03f));
            foreach (var y in (ReadOnlySpan<float>)[1.14f, 0.9f])
                OBox(leaf.B(Material.Plain, new float3(0.46f, 0.46f, 0.47f)), id, new float3(-w + 0.11f, y, thick / 2 + face * (thick / 2 + 0.025f)), new float3(0.024f, 0.024f, 0.05f));
        }

        _factorySideDoor = NewDoor("factory side", 0.9f, leaf);
        MoveDoor(_factorySideDoor, false, instant: true);
    }

    /// <summary>Either side of it, E opens or shuts the door. It won't shut on the player standing in the doorway.</summary>
    public void UpdateFactorySideDoor(float dt, bool usePressed, float3 player)
    {
        if (_factorySideDoor is not { } door)
            return;
        var near = player.X > SideDoorX0 - 1.4f && player.X < SideDoorX1 + 1.4f && player.Z > HalfLength - 1.8f && player.Z < HalfLength + WallThickness + 2.2f
                   && player.Y > -0.5f && player.Y < 1f;
        var inDoorway = player.X > SideDoorX0 - 0.4f && player.X < SideDoorX1 + 0.4f && player.Z > HalfLength - 1.3f && player.Z < HalfLength + WallThickness + 0.6f;
        var open = door.Target > 0.5f;
        FactorySideDoorPrompt = near ? (open ? "[E] Close door" : "[E] Open door") : null;
        if (near && usePressed && !(inDoorway && open) && MoveDoor(door, !open))
            Log.Write(open ? "[DOOR] Shutting the hall's side door" : "[DOOR] Opening the hall's side door");
        StepDoor(door, dt);
    }
}
