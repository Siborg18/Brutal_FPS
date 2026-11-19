using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// What the Antwerp raid (<see cref="AntwerpRaid"/>) needs of the yard: a landing ledge at the foot of the quay's face, with
/// two ladders up from it, where the boat comes alongside; and the places the two ways in start and end.
/// <list type="bullet">
/// <item>By boat: the boat ties up against the ledge north-west of the yard, between two bollards, a ladder either side of
/// the middle of it. Up them you're on the open quay with the north edge's stacks and a floodlight mast ahead.</item>
/// <item>Down the rope: from the top of the east edge's stacks (four high) into the dead end of the lane between them and the
/// stacks under the crane, at its north end.</item>
/// </list>
/// </summary>
public sealed partial class Level
{
    /// <summary>Where along the quay (z) the boat ties up: the middle of the landing ledge, between its two ladders.</summary>
    public const float QuayLandingZ = 20.5f;

    /// <summary>The two ladders up the quay's face from the ledge (z): the north one (the player's), and the south one.</summary>
    public static readonly float[] QuayLadderZs = [23.5f, 17.5f];

    /// <summary>The landing ledge: a concrete step along the foot of the quay's face, a little above the water.</summary>
    public const float LedgeTop = YardSeaLevel + 0.55f, LedgeWidth = 1.7f, LedgeHalfLength = 8f;

    /// <summary>The quay's concrete face (x), on the water's side of the kerb.</summary>
    public static float QuayFaceX => YardOrigin.X + YardQuayX - 0.15f;

    /// <summary>The ledge's outer edge (x), where the boat lies alongside.</summary>
    public static float LedgeEdgeX => QuayFaceX - LedgeWidth;

    /// <summary>The foot of ladder <paramref name="i"/> (<see cref="QuayLadderZs"/>), on the quay's face, standing on the ledge.</summary>
    public static float3 QuayLadderFoot(int i) => new(QuayFaceX, LedgeTop, YardOrigin.Z + QuayLadderZs[i]);

    /// <summary>The sea, in the world (y).</summary>
    public static float YardWaterY => YardOrigin.Y + YardSeaLevel;

    /// <summary>The top of the east edge's stacks (four high), and the face of them on the lane (x).</summary>
    public static float EastStacksTop => 4 * ContainerHeight;
    public static float EastStacksFaceX => YardOrigin.X + YardEastX - ContainerWidth;

    /// <summary>The far side of that lane: the east ends of the stacks under the crane (x).</summary>
    public static float CraneStacksEastX => YardOrigin.X + 21.4f + ContainerLength / 2;

    /// <summary>Where the rope comes down: against the east stacks, a metre short of the lane's dead end (z).</summary>
    public static float AbseilZ => YardOrigin.Z + 27.9f;

    /// <summary>
    /// The landing at the foot of the quay's face, for a boat alongside: a concrete ledge a little above the water with rubber
    /// fenders along its edge, and two steel ladders from it up the face to the kerb. The raid by boat leaves you on it, and
    /// you climb up yourself; the yard's invisible edge goes out round it (BuildYardBounds), so you can't go off it.
    /// </summary>
    private void BuildQuayLanding()
    {
        var concrete = new float3(0.44f, 0.43f, 0.4f);
        var z0 = QuayLandingZ - LedgeHalfLength;
        var z1 = QuayLandingZ + LedgeHalfLength;
        var face = QuayFaceX;
        // The ledge, and a darker tide line along its foot. Both stop at the water: the sea is drawn behind everything (in
        // the sky pass, like the quay's own face, which stops there too), so anything below it would show through.
        AddMinMax(new float3(face - LedgeWidth, YardSeaLevel, z0), new float3(face, LedgeTop, z1), concrete, Material.Concrete);
        AddMinMax(new float3(face - LedgeWidth - 0.01f, YardSeaLevel, z0 - 0.01f), new float3(face, YardSeaLevel + 0.25f, z1 + 0.01f),
            new float3(0.14f, 0.15f, 0.12f), Material.Concrete, blocksShots: false, collides: false);
        // Fenders: black rubber blocks hung along the edge, and a steel edge strip on top.
        for (var z = z0 + 1.2f; z < z1 - 0.5f; z += 2.4f)
            Add(new float3(face - LedgeWidth - 0.12f, LedgeTop - 0.3f, z), new float3(0.24f, 0.5f, 0.45f), new float3(0.03f, 0.03f, 0.03f), Material.Plain, blocksShots: false, collides: false);
        AddMinMax(new float3(face - LedgeWidth, LedgeTop, z0), new float3(face - LedgeWidth + 0.12f, LedgeTop + 0.03f, z1),
            new float3(0.32f, 0.3f, 0.12f), Material.PaintedMetal, blocksShots: false, collides: false);
        for (var i = 0; i < QuayLadderZs.Length; i++)
            BuildQuayLadder(QuayLadderFoot(i));
    }

    /// <summary>A steel ladder up the quay's face from <paramref name="foot"/> (on the ledge) over the kerb.</summary>
    private void BuildQuayLadder(float3 foot)
    {
        var top = 0.22f;   // the top of the kerb
        var height = top - foot.Y;
        const float width = 0.5f, standoff = 0.14f, rails = 0.9f;
        var b = new MeshBuilder();
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            // The stiles, up the face and on over the kerb as handholds, bent back onto the top of it.
            b.Box(new float3(foot.X - standoff, foot.Y + (height + rails) / 2, foot.Z + side * width / 2), new float3(0.06f, height + rails, 0.06f));
            b.Box(new float3(foot.X + 0.12f, top + rails, foot.Z + side * width / 2), new float3(0.6f - 0.12f, 0.06f, 0.06f));
            b.Box(new float3(foot.X + 0.38f, top + rails / 2, foot.Z + side * width / 2), new float3(0.06f, rails, 0.06f));
            for (var y = foot.Y + 0.4f; y < top; y += 0.8f)
                b.Box(new float3(foot.X - standoff / 2, y, foot.Z + side * width / 2), new float3(standoff, 0.05f, 0.05f));
        }
        for (var y = foot.Y + 0.3f; y < top + 0.1f; y += 0.3f)
            b.Box(new float3(foot.X - standoff, y, foot.Z), new float3(0.035f, 0.035f, width));
        _props.Add(new Prop(b.Build(), float4x4.Identity, new float3(0.32f, 0.3f, 0.12f), Material.PaintedMetal));

        // Climbable (as AddLadder's): the rungs stop you, and the space in front of them is where you hold on. Facing the
        // face (+x), and over the top onto the kerb.
        var facing = new float3(1, 0, 0);
        var near = foot.X - standoff - 0.04f;
        AddStatic(new Aabb(new float3(near, foot.Y, foot.Z - width / 2 - 0.04f), new float3(foot.X, top, foot.Z + width / 2 + 0.04f)),
            default, visible: false, blocksShots: false);
        // (Shallower than AddLadder's: the ledge is narrow, and stepping off the boat onto it mustn't put you on the ladder.)
        var volume = new Aabb(new float3(near - 0.45f, foot.Y, foot.Z - width / 2 - 0.15f), new float3(near, top + 0.9f, foot.Z + width / 2 + 0.15f));
        _ladders.Add(new Ladder(volume, facing, top, facing, _props.Count - 1));
    }
}
