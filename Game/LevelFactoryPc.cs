using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The factory's portal PC: a steel desk on the hall floor, pushed up against the east face of the loading dock just
/// south-east of the blue portal (<see cref="FactoryPcZone"/>, a <see cref="PcTerminal"/>). Its cable runs up the dock's
/// face and across the top to the portal's foot. (On a raid the blue portal is dark, so the PC there does nothing.)
/// </summary>
public sealed partial class Level
{
    /// <summary>The desk's middle along the dock's east face (z), and that face (x).</summary>
    private const float FactoryPcZ = -6.6f, FactoryDockFaceX = -10f;

    private static readonly float3 FactoryPcGlow = new(0.15f, 0.55f, 1.0f);   // the blue portal's colour

    /// <summary>Where the player stands to use the factory's PC: in front of its desk, on the hall floor.</summary>
    public static Aabb FactoryPcZone => new(
        new float3(FactoryDockFaceX + 0.6f, -0.5f, FactoryPcZ - 1.1f),
        new float3(FactoryDockFaceX + 2.2f, 3f, FactoryPcZ + 1.1f));

    /// <summary>The desk against the dock: the monitor, a keyboard, the tower, the cable up to the portal, a chair.</summary>
    private void BuildFactoryPc()
    {
        // x right (north, -z, as you face the dock), y up, z out from the dock's face into the hall (east).
        var desk = WallFrame(new float3(FactoryDockFaceX, 0, FactoryPcZ), new float3(1, 0, 0));
        var steel = JB(Material.PaintedMetal, new float3(0.26f, 0.32f, 0.30f));
        var top = JB(Material.Steel, new float3(0.22f, 0.22f, 0.22f));
        OBox(top, desk, new float3(0, 0.735f, 0.36f), new float3(1.5f, 0.04f, 0.7f));
        foreach (var x in (ReadOnlySpan<float>)[-0.72f, 0.72f])
            foreach (var z in (ReadOnlySpan<float>)[0.04f, 0.68f])
                OBox(steel, desk, new float3(x, 0.357f, z), new float3(0.05f, 0.715f, 0.05f));
        OBox(steel, desk, new float3(0, 0.66f, 0.04f), new float3(1.4f, 0.08f, 0.02f));
        FrameCollider(desk, new float3(0, 0.38f, 0.36f), new float3(1.5f, 0.76f, 0.7f));

        // The monitor, showing the portal: its ring, a link trace and a column of readouts.
        var plastic = JB(Material.Polymer, new float3(0.06f, 0.06f, 0.065f));
        var monitor = float4x4.CreateTranslation(-0.05f, 0.755f, 0.24f) * desk;
        OBox(plastic, monitor, new float3(0, 0.012f, 0), new float3(0.28f, 0.024f, 0.2f));
        OBox(plastic, monitor, new float3(0, 0.15f, -0.065f), new float3(0.06f, 0.28f, 0.03f));
        OBox(plastic, monitor, new float3(0, 0.36f, -0.03f), new float3(0.7f, 0.42f, 0.04f));
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            OBox(plastic, monitor, new float3(side * 0.3375f, 0.36f, 0.0125f), new float3(0.025f, 0.42f, 0.045f));
        OBox(plastic, monitor, new float3(0, 0.56f, 0.0125f), new float3(0.65f, 0.02f, 0.045f));
        OBox(plastic, monitor, new float3(0, 0.165f, 0.0125f), new float3(0.65f, 0.03f, 0.045f));
        OBox(JB(Material.Emissive, new float3(0.015f, 0.02f, 0.03f)), monitor, new float3(0, 0.365f, 0f), new float3(0.65f, 0.37f, 0.02f));
        var ring = JB(Material.Emissive, FactoryPcGlow);
        var trace = JB(Material.Emissive, new float3(0.25f, 0.85f, 0.4f));
        for (var i = 0; i < 28; i++)
        {
            var a = float.Tau * i / 28;
            OBox(ring, monitor, float3.Zero, new float3(0.022f, 0.006f, 0.002f),
                 float4x4.CreateRotationZ(a + float.Pi / 2) * float4x4.CreateTranslation(-0.17f + float.Cos(a) * 0.1f, 0.38f + float.Sin(a) * 0.1f, 0.04f));
        }
        OBox(trace, monitor, new float3(-0.17f, 0.38f, 0.04f), new float3(0.03f, 0.03f, 0.002f));
        for (var i = 0; i < 9; i++)
            OBox(trace, monitor, new float3(0.14f, 0.5f - i * 0.028f, 0.04f), new float3(0.2f - (i * 37 % 5) * 0.03f, 0.01f, 0.002f));
        for (var i = 0; i < 16; i++)
        {
            var bar = 0.012f + 0.035f * float.Abs(float.Sin(i * 1.3f));
            OBox(ring, monitor, new float3(0.04f + i * 0.016f, 0.205f + bar / 2, 0.04f), new float3(0.01f, bar, 0.002f));
        }

        // Keyboard, mouse, a mug; the tower on the floor under the desk, on the portal's side (north), its light at the front.
        OBox(plastic, desk, new float3(-0.05f, 0.767f, 0.56f), new float3(0.44f, 0.024f, 0.15f));
        OBox(plastic, desk, new float3(0.27f, 0.77f, 0.56f), new float3(0.06f, 0.03f, 0.1f));
        OCyl(JB(Material.Plain, new float3(0.62f, 0.6f, 0.52f)), desk, new float3(-0.58f, 0.755f, 0.42f), new float3(-0.58f, 0.85f, 0.42f), 0.04f, 0.04f, 12);
        var tower = new float3(0.5f, 0, 0.36f);
        OBox(plastic, desk, tower + new float3(0, 0.23f, 0), new float3(0.2f, 0.46f, 0.46f));
        OBox(JB(Material.Emissive, new float3(0.3f, 1.0f, 0.4f)), desk, tower + new float3(0.05f, 0.41f, 0.24f), new float3(0.014f, 0.014f, 0.01f));

        // The monitor's lead down behind the desk to the tower.
        var wire = JB(Material.Polymer, new float3(0.03f, 0.03f, 0.03f));
        OCyl(wire, desk, new float3(-0.05f, 0.85f, 0.18f), new float3(0.3f, 0.77f, 0.01f), 0.006f, 0.006f, 6);
        OCyl(wire, desk, new float3(0.3f, 0.77f, 0.01f), tower + new float3(0, 0.3f, -0.23f), 0.006f, 0.006f, 6);

        // The cable to the portal: from the back of the tower up the dock's face, over its edge, and along the top to a
        // junction box at the portal's foot (the portal stands at x -14, z -9: local x 2.4, z -4).
        const float r = 0.011f, rise = 0.95f, dockTop = 2f, face = 0.014f;
        var foot = new float3(2.4f, dockTop, -2.45f);
        (float3 From, float3 To)[] runs =
        [
            (tower + new float3(0.05f, 0.25f, -0.23f), new float3(rise, 0.25f, face)),
            (new float3(rise, 0.25f, face), new float3(rise, dockTop - 0.02f, face)),
            (new float3(rise, dockTop - 0.02f, face), new float3(rise, dockTop + r, -0.03f)),
            (new float3(rise, dockTop + r, -0.03f), new float3(rise, dockTop + r, -1.2f)),
            (new float3(rise, dockTop + r, -1.2f), new float3(foot.X, dockTop + r, -1.2f)),
            (new float3(foot.X, dockTop + r, -1.2f), foot + new float3(0, r, 0.1f)),
        ];
        foreach (var (from, to) in runs)
            OCyl(wire, desk, from, to, r, r, 8);
        var fitting = JB(Material.Steel, new float3(0.22f, 0.22f, 0.23f));
        foreach (var at in (ReadOnlySpan<float3>)[new(rise, 0.7f, 0.012f), new(rise, 1.2f, 0.012f), new(rise, 1.7f, 0.012f)])
            OBox(fitting, desk, at, new float3(0.035f, 0.03f, 0.03f));
        OBox(JB(Material.PaintedMetal, new float3(0.32f, 0.34f, 0.33f)), desk, foot + new float3(0, 0.07f, 0), new float3(0.2f, 0.14f, 0.2f));
        OBox(JB(Material.Emissive, FactoryPcGlow), desk, foot + new float3(0, 0.1f, 0.101f), new float3(0.02f, 0.02f, 0.004f));

        // An office chair, pushed back from the desk and off to the south.
        var chair = float4x4.CreateRotationY(0.5f) * float4x4.CreateTranslation(-0.75f, 0, 1.2f) * desk;
        var black = JB(Material.Polymer, new float3(0.05f, 0.05f, 0.055f));
        OBox(black, chair, new float3(0, 0.46f, 0), new float3(0.46f, 0.07f, 0.44f));
        OBox(black, chair, new float3(0, 0.78f, 0.21f), new float3(0.44f, 0.5f, 0.06f));
        OCyl(steel, chair, new float3(0, 0.08f, 0), new float3(0, 0.43f, 0), 0.025f, 0.025f, 8);
        for (var i = 0; i < 5; i++)
        {
            var a = float.Tau * i / 5;
            OCyl(steel, chair, new float3(0, 0.07f, 0), new float3(float.Cos(a) * 0.27f, 0.04f, float.Sin(a) * 0.27f), 0.018f, 0.018f, 6);
        }
        FrameCollider(chair, new float3(0, 0.45f, 0.05f), new float3(0.5f, 0.9f, 0.5f), blocksShots: false);

        FlushPoliceBatches();
    }
}
