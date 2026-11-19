using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Antwerp's portal PC: the grey container on the ground just south of the orange portal's is hollow
/// (<see cref="BuildHollowContainer"/>), and at its closed end stands a steel desk with the PC that drives the orange
/// portal (<see cref="YardPcZone"/>, a <see cref="PcTerminal"/>), its cable run along the wall and through it into the
/// portal's container next door. A ladder up the door end of the next container along (<see cref="YardLadderDz"/>)
/// gets the player onto the top of the west block.
/// </summary>
public sealed partial class Level
{
    /// <summary>Where the PC's container stands in the west row (z from the yard's origin): the one south of the portal's.</summary>
    private const float YardPcContainerDz = -2.6f;

    /// <summary>The container whose door end the ladder goes up (z from the yard's origin): the next one south.</summary>
    private const float YardLadderDz = -5.2f;

    private static float YardPcZ => YardOrigin.Z + YardPcContainerDz;

    private static readonly float3 YardPcGlow = new(1.0f, 0.55f, 0.1f);   // the orange portal's colour

    /// <summary>Where the player stands to use the yard's PC: in front of its desk at the container's closed end.</summary>
    public static Aabb YardPcZone => new(
        new float3(ContainerWestX + 0.6f, -0.5f, YardPcZ - 1.1f),
        new float3(ContainerWestX + 2.2f, 3f, YardPcZ + 1.1f));

    /// <summary>The PC container's inside: dim, lit from its open door.</summary>
    private static Aabb YardPcInterior => new(
        new float3(ContainerWestX + EndWallThickness, 0, YardPcZ - InnerHalfWidth),
        new float3(ContainerEastX, InnerHeight, YardPcZ + InnerHalfWidth));

    /// <summary>The sky's light dying away from the PC container's open door (the south half of its east end).</summary>
    private static ShadeVolume YardPcShade => new(
        new Aabb(YardPcInterior.Min - new float3(0.02f, 0.05f, 0.02f), YardPcInterior.Max + new float3(-0.04f, 0.02f, 0.02f)),
        new float3(ContainerEastX + 0.6f, 1.1f, YardPcZ - InnerHalfWidth / 2), 7f, 0.35f, NoSun: true);   // (the floor: the strip light over the desk)

    /// <summary>A steel desk at the closed end: the monitor, a keyboard, the tower, the cable through to the portal, a chair, a strip light.</summary>
    private void BuildYardPc()
    {
        // x right (south, -z, as you face the end wall), y up, z out from the end wall into the container (east).
        var desk = WallFrame(new float3(ContainerWestX + EndWallThickness, 0, YardPcZ), new float3(1, 0, 0));
        var steel = JB(Material.PaintedMetal, new float3(0.32f, 0.34f, 0.33f));
        var top = JB(Material.Steel, new float3(0.22f, 0.22f, 0.22f));
        OBox(top, desk, new float3(0, 0.735f, 0.36f), new float3(1.5f, 0.04f, 0.7f));
        foreach (var x in (ReadOnlySpan<float>)[-0.72f, 0.72f])
            foreach (var z in (ReadOnlySpan<float>)[0.04f, 0.68f])
                OBox(steel, desk, new float3(x, 0.357f, z), new float3(0.05f, 0.715f, 0.05f));
        OBox(steel, desk, new float3(0, 0.66f, 0.04f), new float3(1.4f, 0.08f, 0.02f));
        FrameCollider(desk, new float3(0, 0.38f, 0.36f), new float3(1.5f, 0.76f, 0.7f));

        // The monitor, showing the portal: its ring, a link trace and a column of readouts.
        var plastic = JB(Material.Polymer, new float3(0.06f, 0.06f, 0.065f));
        var monitor = float4x4.CreateTranslation(0.05f, 0.755f, 0.24f) * desk;
        OBox(plastic, monitor, new float3(0, 0.012f, 0), new float3(0.28f, 0.024f, 0.2f));
        OBox(plastic, monitor, new float3(0, 0.15f, -0.065f), new float3(0.06f, 0.28f, 0.03f));
        OBox(plastic, monitor, new float3(0, 0.36f, -0.03f), new float3(0.7f, 0.42f, 0.04f));
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            OBox(plastic, monitor, new float3(side * 0.3375f, 0.36f, 0.0125f), new float3(0.025f, 0.42f, 0.045f));
        OBox(plastic, monitor, new float3(0, 0.56f, 0.0125f), new float3(0.65f, 0.02f, 0.045f));
        OBox(plastic, monitor, new float3(0, 0.165f, 0.0125f), new float3(0.65f, 0.03f, 0.045f));
        OBox(JB(Material.Emissive, new float3(0.015f, 0.03f, 0.02f)), monitor, new float3(0, 0.365f, 0f), new float3(0.65f, 0.37f, 0.02f));
        var ring = JB(Material.Emissive, YardPcGlow);
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
            var bar = 0.012f + 0.035f * float.Abs(float.Sin(i * 0.9f));
            OBox(ring, monitor, new float3(0.04f + i * 0.016f, 0.205f + bar / 2, 0.04f), new float3(0.01f, bar, 0.002f));
        }

        // Keyboard, mouse, a mug; the tower on the floor under the desk, on the portal's side, its light at the front.
        OBox(plastic, desk, new float3(0.05f, 0.767f, 0.56f), new float3(0.44f, 0.024f, 0.15f));
        OBox(plastic, desk, new float3(0.37f, 0.77f, 0.56f), new float3(0.06f, 0.03f, 0.1f));
        OCyl(JB(Material.Plain, new float3(0.62f, 0.6f, 0.52f)), desk, new float3(-0.55f, 0.755f, 0.45f), new float3(-0.55f, 0.85f, 0.45f), 0.04f, 0.04f, 12);
        var tower = new float3(-0.5f, 0, 0.36f);
        OBox(plastic, desk, tower + new float3(0, 0.23f, 0), new float3(0.2f, 0.46f, 0.46f));
        OBox(JB(Material.Emissive, new float3(0.3f, 1.0f, 0.4f)), desk, tower + new float3(-0.05f, 0.41f, 0.24f), new float3(0.014f, 0.014f, 0.01f));

        // The monitor's lead down behind the desk to the tower.
        var wire = JB(Material.Polymer, new float3(0.03f, 0.03f, 0.03f));
        OCyl(wire, desk, new float3(0.05f, 0.85f, 0.18f), new float3(-0.3f, 0.77f, 0.01f), 0.006f, 0.006f, 6);
        OCyl(wire, desk, new float3(-0.3f, 0.77f, 0.01f), tower + new float3(0, 0.3f, -0.23f), 0.006f, 0.006f, 6);

        // The cable to the portal: from the back of the tower into the corner, up the end wall, along the side wall a little
        // and through it, into the orange portal's container next door.
        const float wireY = 1.1f, holeZ = 0.55f, r = 0.011f;
        var wall = -InnerHalfWidth + 0.015f;   // (local -x is north, towards the portal's container)
        (float3 From, float3 To)[] runs =
        [
            (tower + new float3(-0.05f, 0.25f, -0.23f), new float3(wall, 0.2f, 0.014f)),
            (new float3(wall, 0.2f, 0.014f), new float3(wall, wireY, 0.014f)),
            (new float3(wall, wireY, 0.014f), new float3(wall, wireY, holeZ)),
            (new float3(wall, wireY, holeZ), new float3(-InnerHalfWidth - 0.03f, wireY, holeZ)),
        ];
        foreach (var (from, to) in runs)
            OCyl(wire, desk, from, to, r, r, 8);
        var fitting = JB(Material.Steel, new float3(0.22f, 0.22f, 0.23f));
        OCyl(fitting, desk, new float3(-InnerHalfWidth + 0.012f, wireY, holeZ), new float3(-InnerHalfWidth, wireY, holeZ), 0.026f, 0.026f, 10);
        foreach (var at in (ReadOnlySpan<float3>)[new(wall, 0.55f, 0.012f), new(wall, 0.9f, 0.012f), new(wall, wireY, 0.3f)])
            OBox(fitting, desk, at, new float3(0.03f, 0.035f, 0.035f));

        // An office chair, pushed back from the desk.
        var chair = float4x4.CreateTranslation(0.15f, 0, 1.05f) * desk;
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

        // A strip light screwed to the roof over the desk.
        var lamp = float4x4.CreateTranslation(0, InnerHeight - 0.03f, 0.9f) * desk;
        OBox(steel, lamp, float3.Zero, new float3(1.2f, 0.05f, 0.12f));
        OBox(JB(Material.Emissive, new float3(0.85f, 0.9f, 0.85f)), lamp, new float3(0, -0.03f, 0), new float3(1.1f, 0.02f, 0.06f));

        FlushPoliceBatches();
    }
}
