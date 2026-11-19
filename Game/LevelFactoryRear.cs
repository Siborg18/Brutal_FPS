using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The back way into the factory's hall, in its south end, east of the office: a steel staircase up the outside of the wall to
/// a landing, a steel door through the wall at the height of the concrete band, a walkway along the inside of the wall over the
/// hall, and a ladder down to the floor.
///
///                    south wall (outside is down the page)
///     +-----[office]-----+--------------[door]---------------+
///                        |  walkway  ...........  [ladder]   |     inside
///     ===================+===================================+===
///                 foot of the steps ->->->  [ landing ]            outside
///
/// The door's cut through the lower ribbon window of the bay between the pilasters at x = 9 and 15, so the window carries on
/// either side of it. The steps are the only ones on this level the player steps up (see <see cref="HasStairs"/>).
/// </summary>
public sealed partial class Level
{
    /// <summary>The height of the rear door's landing and the walkway inside it.</summary>
    public const float FactoryRearDeckY = RearDeckY;
    private const float RearDeckY = BandTop;                                 // the landing, the walkway and the door's sill: the top of the concrete band
    private const float RearDoorX0 = 12.56f, RearDoorX1 = 13.67f, RearDoorHeight = 2.1f;   // (x: between two of the window's mullions)
    private const float RearWalkFront = 28.6f, RearWalkX0 = 9.4f, RearWalkX1 = 17.6f, RearLadderX = 16.2f;
    private const float RearLandX0 = 11.4f, RearLandX1 = 14.6f, RearLandZ0 = 31.75f, RearLandZ1 = 33.4f;
    private const float RearFlightZ0 = 31.8f, RearFlightZ1 = 33f;
    private const int RearRisers = 22;
    private const float RearTread = 0.27f;
    private const float RearRise = RearDeckY / RearRisers;
    private const float RearFootX = RearLandX0 - (RearRisers - 1) * RearTread;

    private const float RearDoorHingeZ = HalfLength + WallThickness + SkinIn + 0.15f;   // (just outside the cladding)

    private static readonly Aabb RearStepsArea = new(new float3(RearFootX - 0.8f, -0.3f, RearFlightZ0 - 0.1f), new float3(RearLandX1 + 0.1f, RearDeckY + 0.5f, RearLandZ1 + 0.1f));


    private PrisonDoor? _factoryRearDoor;

    /// <summary>"[E] Open door" / "[E] Close door" while the player is by the hall's rear door, otherwise null.</summary>
    public string? FactoryRearDoorPrompt { get; private set; }

    /// <summary>Call once the hall's skin is up: the door's cut through it.</summary>
    private void BuildFactoryRearEntrance()
    {
        BuildRearDoor();
        BuildRearSteps();
        BuildRearWalkway();
    }

    // ------------------------------------------------------------------------------------------------ the door

    private void BuildRearDoor()
    {
        var top = RearDeckY + RearDoorHeight;
        var wallFace = HalfLength;
        var skinOut = HalfLength + WallThickness + SkinIn + 0.12f;
        // (From the top of the band exactly, so the band and the wall under the opening are left as its floor.)
        CarveOpening(new Aabb(new float3(RearDoorX0, RearDeckY, wallFace - 0.05f), new float3(RearDoorX1, top, skinOut + 0.05f)));

        // Steel round the opening on both faces, angle up its sides and head, a worn plate over the sill.
        var steel = OldSteel * 0.85f;
        foreach (var x in (ReadOnlySpan<float>)[RearDoorX0 - 0.03f, RearDoorX1 + 0.03f])
        {
            FBox(new float3(x - 0.05f, RearDeckY, skinOut - 0.02f), new float3(x + 0.05f, top + 0.12f, skinOut + 0.06f), steel, Material.RustyMetal, collides: false);
            FBox(new float3(x - 0.05f, RearDeckY, wallFace - 0.1f), new float3(x + 0.05f, top + 0.12f, wallFace + 0.02f), steel, Material.RustyMetal, collides: false);
            FBox(new float3(x - 0.03f, RearDeckY, wallFace), new float3(x + 0.03f, top, skinOut), steel, Material.RustyMetal, collides: false);
        }

        foreach (var (z0, z1) in (ReadOnlySpan<(float, float)>)[(skinOut - 0.02f, skinOut + 0.06f), (wallFace - 0.1f, wallFace + 0.02f)])
            FBox(new float3(RearDoorX0 - 0.08f, top, z0), new float3(RearDoorX1 + 0.08f, top + 0.12f, z1), steel, Material.RustyMetal, collides: false);
        FBox(new float3(RearDoorX0, top - 0.03f, wallFace), new float3(RearDoorX1, top, skinOut), steel, Material.RustyMetal, collides: false);
        FBox(new float3(RearDoorX0, RearDeckY, wallFace - 0.02f), new float3(RearDoorX1, RearDeckY + 0.006f, RearLandZ0 + 0.02f), OldSteel, Material.Steel, collides: false, blocksShots: false);

        // The leaf: flush steel in old green paint, hinged on its east edge and swinging out over the landing, away from the steps.
        // Built in the hinge's frame: it runs west (-x) from the hinge.
        const float w = RearDoorX1 - RearDoorX0 - 0.02f, h = RearDoorHeight - 0.04f, thick = 0.05f;
        var id = float4x4.Identity;
        var c = new float3(-w / 2 - 0.01f, h / 2, 0);
        var leaf = HingedLeaf(new float3(RearDoorX1, RearDeckY + 0.02f, RearDoorHingeZ), float.DegreesToRadians(100f), c, new float3(w, h, thick));
        var paint = new float3(0.20f, 0.25f, 0.21f);
        var worn = new float3(0.13f, 0.15f, 0.13f);
        OBox(leaf.B(Material.Plain, paint), id, c, new float3(w, h, thick));
        foreach (var face in (ReadOnlySpan<float>)[-1f, 1f])
        {
            OBox(leaf.B(Material.Plain, worn), id, c with { Y = 0.18f, Z = face * (thick / 2 + 0.004f) }, new float3(w - 0.08f, 0.3f, 0.008f));   // the kick plate
            foreach (var y in (ReadOnlySpan<float>)[0.72f, 1.42f])
                OBox(leaf.B(Material.Plain, paint * 0.86f), id, c with { Y = y, Z = face * (thick / 2 + 0.004f) }, new float3(w - 0.12f, 0.03f, 0.008f));
            OBox(leaf.B(Material.Plain, new float3(0.46f, 0.46f, 0.47f)), id, new float3(-w + 0.11f, 1.02f, face * (thick / 2 + 0.05f)), new float3(0.03f, 0.3f, 0.03f));
            OBox(leaf.B(Material.Plain, new float3(0.46f, 0.46f, 0.47f)), id, new float3(-w + 0.11f, 1.14f, face * (thick / 2 + 0.025f)), new float3(0.024f, 0.024f, 0.05f));
            OBox(leaf.B(Material.Plain, new float3(0.46f, 0.46f, 0.47f)), id, new float3(-w + 0.11f, 0.9f, face * (thick / 2 + 0.025f)), new float3(0.024f, 0.024f, 0.05f));
        }

        _factoryRearDoor = NewDoor("factory rear", 0.9f, leaf);
        MoveDoor(_factoryRearDoor, false, instant: true);
    }

    /// <summary>On the landing or the walkway beside it, E opens or shuts the door. It won't shut on the player standing in the doorway.</summary>
    public void UpdateFactoryRearDoor(float dt, bool usePressed, float3 player)
    {
        if (_factoryRearDoor is not { } door)
            return;
        var near = player.X > RearDoorX0 - 1.4f && player.X < RearDoorX1 + 1.4f && player.Z > HalfLength - 1.4f && player.Z < HalfLength + WallThickness + 2.2f
                   && player.Y > RearDeckY - 0.5f && player.Y < RearDeckY + 1f;
        var inDoorway = player.X > RearDoorX0 - 0.4f && player.X < RearDoorX1 + 0.4f && player.Z > HalfLength - 0.4f && player.Z < RearDoorHingeZ + 0.4f;
        var open = door.Target > 0.5f;
        FactoryRearDoorPrompt = near ? (open ? "[E] Close door" : "[E] Open door") : null;
        if (near && usePressed && !(inDoorway && open) && MoveDoor(door, !open))
            Log.Write(open ? "[DOOR] Shutting the hall's rear door" : "[DOOR] Opening the hall's rear door");
        StepDoor(door, dt);
    }

    // ------------------------------------------------------------------------------------------------ the outside: the steps and the landing

    /// <summary>A handrail with a middle rail on posts, from <paramref name="from"/> to <paramref name="to"/> (where it stands). Just to look at.</summary>
    private void RearRailing(float3 from, float3 to)
    {
        var rail = PB(Material.Steel, SteelTint, occludes: false);
        var id = float4x4.Identity;
        foreach (var h in (ReadOnlySpan<float>)[1.05f, 0.55f])
            OCyl(rail, id, from + new float3(0, h, 0), to + new float3(0, h, 0), 0.022f, 0.022f, 8);
        var posts = int.Max(1, (int)float.Round(float3.Distance(from, to) / 1.1f));
        for (var i = 0; i <= posts; i++)
            OBox(rail, id, from + (to - from) * (i / (float)posts) + new float3(0, 0.525f, 0), new float3(0.05f, 1.05f, 0.05f));
    }

    private void BuildRearSteps()
    {
        // The landing, on four legs, with X bracing across the far side.
        FBox(new float3(RearLandX0, RearDeckY - 0.12f, RearLandZ0), new float3(RearLandX1, RearDeckY, RearLandZ1), RustTint, Material.RustyMetal);
        var legs = PB(Material.RustyMetal, RustTint * 0.9f, occludes: false);
        foreach (var x in (ReadOnlySpan<float>)[RearLandX0 + 0.08f, RearLandX1 - 0.08f])
            foreach (var z in (ReadOnlySpan<float>)[RearLandZ0 + 0.1f, RearLandZ1 - 0.08f])
                FBox(new float3(x - 0.07f, 0, z - 0.07f), new float3(x + 0.07f, RearDeckY - 0.12f, z + 0.07f), RustTint * 0.9f, Material.RustyMetal);
        var braceH = RearDeckY - 0.12f;
        var braceW = RearLandX1 - RearLandX0 - 0.16f;
        var braceLength = float.Sqrt(braceH * braceH + braceW * braceW);
        var braceAngle = float.Atan2(braceH, braceW);
        foreach (var turn in (ReadOnlySpan<float>)[braceAngle, -braceAngle])
            OBox(legs, float4x4.Identity, new float3((RearLandX0 + RearLandX1) / 2, braceH / 2, RearLandZ1 - 0.08f), new float3(braceLength, 0.06f, 0.04f), float4x4.CreateRotationZ(turn));

        // The flight runs up the wall from the west. Each step's a thin tread to look at over a box that's what you walk on.
        var treads = PB(Material.RustyMetal, RustTint * 1.1f, occludes: false);
        var stringers = PB(Material.RustyMetal, OldSteel * 0.9f, occludes: false);
        var id = float4x4.Identity;
        for (var i = 1; i < RearRisers; i++)
        {
            var x0 = RearFootX + (i - 1) * RearTread;
            var x1 = x0 + RearTread;
            var y = i * RearRise;
            OBox(treads, id, new float3((x0 + x1) / 2, y - 0.02f, (RearFlightZ0 + RearFlightZ1) / 2), new float3(RearTread - 0.015f, 0.04f, RearFlightZ1 - RearFlightZ0 - 0.1f));
            AddStatic(new Aabb(new float3(x0, y - 0.2f, RearFlightZ0), new float3(x1, y, RearFlightZ1)), default, visible: false, blocksShots: false);
            AddStatic(new Aabb(new float3(x0, y, RearFlightZ1), new float3(x1, y + 1.1f, RearFlightZ1 + 0.06f)), default, visible: false, blocksShots: false);   // (the handrail)
        }

        var run = RearLandX0 - RearFootX;
        var length = float.Sqrt(run * run + RearDeckY * RearDeckY);
        var slope = float.Atan2(RearDeckY, run);
        var middle = new float3((RearFootX + RearLandX0) / 2, RearDeckY / 2 - 0.1f, 0);
        foreach (var z in (ReadOnlySpan<float>)[RearFlightZ0 + 0.04f, RearFlightZ1 - 0.04f])
            OBox(stringers, id, middle with { Z = z }, new float3(length, 0.22f, 0.05f), float4x4.CreateRotationZ(slope));

        // Halfway up it stands on a pair of legs.
        const float propX = 8.5f;
        var propY = (propX - RearFootX) / run * RearDeckY - 0.2f;
        foreach (var z in (ReadOnlySpan<float>)[RearFlightZ0 + 0.04f, RearFlightZ1 - 0.04f])
            FBox(new float3(propX - 0.05f, 0, z - 0.05f), new float3(propX + 0.05f, propY, z + 0.05f), RustTint * 0.9f, Material.RustyMetal);

        // Railings up both sides of the flight and round the landing, with nothing across the door or the head of the steps.
        RearRailing(new float3(RearFootX + 0.1f, RearRise, RearFlightZ1 + 0.03f), new float3(RearLandX0, RearDeckY, RearFlightZ1 + 0.03f));
        RearRailing(new float3(RearFootX + 0.1f, RearRise, RearFlightZ0 + 0.04f), new float3(RearLandX0, RearDeckY, RearFlightZ0 + 0.04f));
        RearRailing(new float3(RearLandX0, RearDeckY, RearFlightZ1 + 0.03f), new float3(RearLandX0, RearDeckY, RearLandZ1 - 0.03f));
        RearRailing(new float3(RearLandX0, RearDeckY, RearLandZ1 - 0.03f), new float3(RearLandX1 - 0.03f, RearDeckY, RearLandZ1 - 0.03f));
        RearRailing(new float3(RearLandX1 - 0.03f, RearDeckY, RearLandZ1 - 0.03f), new float3(RearLandX1 - 0.03f, RearDeckY, RearLandZ0));
        foreach (var (a, b) in ((float3, float3)[])
                 [(new float3(RearLandX0 - 0.06f, RearDeckY, RearFlightZ1), new float3(RearLandX0, RearDeckY + 1.1f, RearLandZ1)),
                  (new float3(RearLandX0, RearDeckY, RearLandZ1 - 0.06f), new float3(RearLandX1, RearDeckY + 1.1f, RearLandZ1)),
                  (new float3(RearLandX1 - 0.06f, RearDeckY, RearLandZ0), new float3(RearLandX1, RearDeckY + 1.1f, RearLandZ1))])
            AddStatic(new Aabb(a, b), default, visible: false, blocksShots: false);

        // (The head of the flight, for people, is out in front of the door: the searches can't always find their way through it from the landing's west end.)
        const float lane = (RearFlightZ0 + RearFlightZ1) / 2;
        AddStairFlight("factory rear steps", new float3(RearFootX - 0.4f, 0, lane), new float3(RearFootX, 0, lane), new float3(RearLandX0, RearDeckY, lane), new float3(RearLandX0 + 1.5f, RearDeckY, lane),
            new Aabb(new float3(RearFootX, 0, RearFlightZ0), new float3(RearLandX0, RearDeckY, RearFlightZ1)));
    }

    // ------------------------------------------------------------------------------------------------ the inside: the walkway and the ladder

    private void BuildRearWalkway()
    {
        const float top = RearDeckY, front = RearWalkFront, wall = HalfLength, gap = 0.42f;
        FBox(new float3(RearWalkX0, top - 0.12f, front), new float3(RearWalkX1, top, wall), RustTint, Material.RustyMetal);

        // Steel joists back into the wall, on a leg at the front of each of three of them.
        var steel = PB(Material.RustyMetal, SteelTint, occludes: false);
        var id = float4x4.Identity;
        for (var x = RearWalkX0 + 0.3f; x < RearWalkX1; x += 1.3f)
            OBox(steel, id, new float3(x, top - 0.2f, (front + wall) / 2), new float3(0.08f, 0.16f, wall - front));
        foreach (var x in (ReadOnlySpan<float>)[RearWalkX0 + 0.3f, RearDoorX0 - 0.1f, RearWalkX1 - 0.3f])
            FBox(new float3(x - 0.08f, 0, front + 0.05f), new float3(x + 0.08f, top - 0.12f, front + 0.21f), SteelTint, Material.Steel);

        // Railings along the front (but for the head of the ladder) and across both ends. You can't walk through them.
        foreach (var (x0, x1) in (ReadOnlySpan<(float, float)>)[(RearWalkX0, RearLadderX - gap), (RearLadderX + gap, RearWalkX1)])
        {
            RearRailing(new float3(x0, top, front + 0.03f), new float3(x1, top, front + 0.03f));
            AddStatic(new Aabb(new float3(x0, top, front), new float3(x1, top + 1.1f, front + 0.06f)), default, visible: false, blocksShots: false);
        }

        foreach (var x in (ReadOnlySpan<float>)[RearWalkX0 + 0.03f, RearWalkX1 - 0.03f])
        {
            RearRailing(new float3(x, top, front + 0.03f), new float3(x, top, wall));
            AddStatic(new Aabb(new float3(x - 0.03f, top, front), new float3(x + 0.03f, top + 1.1f, wall)), default, visible: false, blocksShots: false);
        }

        // The ladder down to the hall's floor, from the front edge, with a hoop over its head as on the catwalk's: that's what
        // stops you walking straight out over the hall, and drops you onto the rungs.
        var hoopZ = front - 0.95f;
        AddLadder(new float3(RearLadderX, 0, front), new float3(0, 0, 1), top, RustTint, width: 0.6f);
        foreach (var x in (ReadOnlySpan<float>)[RearLadderX - 0.3f, RearLadderX + 0.3f])
            Add(new float3(x, top + 0.97f, (front + hoopZ) / 2 - 0.08f), new float3(0.05f, 0.05f, front - hoopZ - 0.16f), RustTint, Material.Steel, collides: false);
        Add(new float3(RearLadderX, top + 0.97f, hoopZ), new float3(0.65f, 0.05f, 0.05f), RustTint, Material.Steel, collides: false);
        AddStatic(Aabb.FromCenterSize(new float3(RearLadderX, top + 1.1f, hoopZ), new float3(1.1f, 2.2f, 0.1f)), default, visible: false, blocksShots: false);
    }
}
