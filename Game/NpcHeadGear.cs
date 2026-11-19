using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The kit a special-operations team member wears on their helmet, drawn as rigid pieces on the head bone (the helmet
/// itself is a garment): a night-vision mount on the brow with binocular goggles flipped down over the eyes, a battery
/// pack on the back as a counterweight, a strobe on top, rails down the sides and the headset's ear cups under the rim.
/// </summary>
/// <remarks>
/// Laid out in the head bone's own space. At rest that bone's axes are the body's (Y up, +Z the way the face looks), its
/// origin at the top of the neck: the eyes are at (±0.032, 0.0775, 0.083) from it and the helmet's front rim at about
/// (0, 0.12, 0.10) (Tools/blender/clothkit/garments_more.py, HELMET_CUT).
/// </remarks>
public static class NpcHeadGear
{
    private enum Shape { Box, Tube }

    private readonly record struct Piece(Shape Shape, float4x4 Local, int Colour, Material Material, bool Flips = false);

    // Flipped up (by day): the arm, bridge and tubes swing up about the mount's hinge, the tubes standing up in front of the helmet.
    private static readonly float3 Hinge = new(0f, 0.15f, 0.122f);
    private const float FlipAngle = -2.15f;   // rad, about X
    private static readonly float4x4 FlipUp =
        float4x4.CreateTranslation(-Hinge) * float4x4.CreateRotationX(FlipAngle) * float4x4.CreateTranslation(Hinge);

    // Colours: 0 black (the goggles and mount), 1 the accent (pack, cups, rails: coyote or ranger green), 2 the lenses.
    private static readonly float3 Black = new(0.025f, 0.026f, 0.028f);
    private static readonly float3 Lens = new(0.015f, 0.04f, 0.03f);

    private static Mesh? _box, _tube;
    private static Piece[]? _pieces;

    private static float4x4 Box(float3 centre, float3 size, float tiltX = 0f) =>
        float4x4.CreateScale(size) * float4x4.CreateRotationX(tiltX) * float4x4.CreateTranslation(centre);

    /// <summary>A tube along Z, from <paramref name="zBack"/> to <paramref name="zFront"/>, centred on (x, y).</summary>
    private static float4x4 TubeZ(float x, float y, float zBack, float zFront, float radius) =>
        float4x4.CreateScale(radius, radius, zFront - zBack) * float4x4.CreateTranslation(x, y, zBack);

    /// <summary>A tube along X, from <paramref name="xFrom"/> outwards by <paramref name="length"/> (negative: the other side).</summary>
    private static float4x4 TubeX(float xFrom, float length, float y, float z, float radius) =>
        float4x4.CreateScale(radius, radius, float.Abs(length)) * float4x4.CreateRotationY(length > 0 ? float.Pi / 2 : -float.Pi / 2)
        * float4x4.CreateTranslation(xFrom, y, z);

    private static Piece[] Build()
    {
        _box = new MeshBuilder().Box(float3.Zero, float3.One).Build();
        _tube = new MeshBuilder().Cylinder(1f, 0f, 1f, 14).Build();
        var pieces = new List<Piece>
        {
            // The mount: a shroud on the helmet's brow, an arm down from it and the bridge the two tubes hang from.
            new(Shape.Box, Box(new float3(0f, 0.142f, 0.122f), new float3(0.052f, 0.036f, 0.016f), -0.35f), 0, Material.Polymer),
            new(Shape.Box, Box(new float3(0f, 0.117f, 0.138f), new float3(0.022f, 0.04f, 0.018f), 0.3f), 0, Material.Gunmetal, Flips: true),
            new(Shape.Box, Box(new float3(0f, 0.093f, 0.152f), new float3(0.036f, 0.026f, 0.05f)), 0, Material.Polymer, Flips: true),
            // The back: battery pack (counterweight) and a strobe on top.
            new(Shape.Box, Box(new float3(0f, 0.125f, -0.118f), new float3(0.074f, 0.044f, 0.028f), 0.5f), 1, Material.Cloth),
            new(Shape.Box, Box(new float3(0f, 0.198f, -0.075f), new float3(0.026f, 0.016f, 0.034f), 0.5f), 0, Material.Polymer),
        };
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            // A goggle tube in front of each eye, wider at the objective, with its dark lens.
            pieces.Add(new(Shape.Tube, TubeZ(side * 0.032f, 0.0775f, 0.112f, 0.185f, 0.0165f), 0, Material.Polymer, Flips: true));
            pieces.Add(new(Shape.Tube, TubeZ(side * 0.032f, 0.0775f, 0.175f, 0.197f, 0.0195f), 0, Material.Polymer, Flips: true));
            pieces.Add(new(Shape.Tube, TubeZ(side * 0.032f, 0.0775f, 0.197f, 0.1985f, 0.0145f), 2, Material.Plain, Flips: true));
            // The headset's ear cup, and a rail along the helmet's side.
            pieces.Add(new(Shape.Tube, TubeX(side * 0.074f, side * 0.032f, 0.045f, 0.0f, 0.034f), 1, Material.Polymer));
            pieces.Add(new(Shape.Box, Box(new float3(side * 0.101f, 0.118f, -0.005f), new float3(0.012f, 0.016f, 0.12f)), 0, Material.Polymer));
        }

        return [.. pieces];
    }

    // The friendly-fire marker: a little infrared light on the back of the helmet (see DrawMarker).
    private static readonly float3 MarkerAt = new(0f, 0.165f, -0.118f);
    private const float MarkerSize = 0.016f;        // m, up close
    private const float MarkerPixels = 0.0035f;     // it never gets smaller than about 3-4 pixels across (rad seen from the eye)
    private const float MarkerPeriod = 1.4f, MarkerFlash = 0.12f;   // s: it glows steadily, and flashes bright once a period

    /// <summary>
    /// The friendly-fire marker on a friend's helmet (<paramref name="headToWorld"/>), <paramref name="distance"/> from the
    /// eye: only drawn when the view is seen through night vision or thermal, where it's a bright dot, glowing and
    /// flashing once every <see cref="MarkerPeriod"/> s (<paramref name="clock"/>: each friend's own, so they don't flash
    /// together). Further off it's drawn bigger, so it never shrinks below a few pixels.
    /// </summary>
    public static void DrawMarker(Renderer renderer, in float4x4 headToWorld, float distance, float clock)
    {
        var flash = clock % MarkerPeriod < MarkerFlash;
        var size = float.Max(MarkerSize, distance * MarkerPixels) * (flash ? 1.6f : 1f);
        var at = float3.Transform(MarkerAt, headToWorld);
        // Drawn past the vision's own colouring, in a colour it never makes: pure white over the phosphor green, and
        // in thermal an icy cyan no heat shows as (taken from how bright it is, it came out the orange of skin).
        var vision = renderer.Vision;
        var colour = vision == ScopeVision.Thermal
            ? (flash ? new float3(0.85f, 1f, 1f) : new float3(0.3f, 0.95f, 1f))
            : (flash ? new float3(1f) : new float3(0.92f, 1f, 0.92f));
        renderer.SetVision(ScopeVision.Off);
        renderer.DrawCube(float4x4.CreateScale(size) * float4x4.CreateTranslation(at), colour, ShadeMode.Unlit);
        renderer.SetVision(vision);
    }

    /// <summary>
    /// Draws it on a head (<paramref name="headToWorld"/>: the head bone's pose in the world), the pack and cups in <paramref name="accent"/>,
    /// the goggles <paramref name="down"/> over the eyes (at night) or flipped up out of the way (by day).
    /// </summary>
    public static void Draw(Renderer renderer, in float4x4 headToWorld, float3 accent, bool down)
    {
        _pieces ??= Build();
        foreach (var p in _pieces)
        {
            var colour = p.Colour switch { 1 => accent, 2 => Lens, _ => Black };
            renderer.DrawMesh(p.Shape == Shape.Box ? _box! : _tube!, (p.Flips && !down ? p.Local * FlipUp : p.Local) * headToWorld, colour, ShadeMode.Lit, p.Material);
        }
    }
}
