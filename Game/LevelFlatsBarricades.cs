using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The undercroft's two open sides, blockaded: rusty cars pushed across them nose to tail, big wheeled refuse bins in the gaps,
/// and pallets, drums, tyres and scrap piled round them. You can see out over and between them, but you can't get out: an
/// invisible wall runs along each side from wall to wall and floor to ceiling, so the undercroft and the stairwell are all
/// that's left to walk in. Bullets stop on what's actually there.
/// </summary>
public sealed partial class Level
{
    private static readonly float3 RustA = new(0.34f, 0.18f, 0.10f), RustB = new(0.27f, 0.16f, 0.10f), RustC = new(0.42f, 0.25f, 0.14f);
    private static readonly float3 BinGrey = new(0.055f, 0.06f, 0.065f), BinBlue = new(0.03f, 0.16f, 0.52f);

    /// <summary>The height of the ground in front of the block, on the ramp.</summary>
    private static float RampHeight(float z) => FlatsStreetY * float.Clamp((z - FlatsHalfZ) / (RampEndZ - FlatsHalfZ), 0f, 1f);

    private void BuildFlatsBarricades(Random random)
    {
        // The walls that actually stop you, just outside the columns' outer faces, under the fascia.
        FCollider(-UnderHalfX, -0.2f, FlatsHalfZ - 0.02f, UnderHalfX, UnderCeiling + 0.2f, FlatsHalfZ + 0.2f);
        FCollider(-UnderHalfX, -0.2f, -FlatsHalfZ - 0.2f, UnderHalfX, UnderCeiling + 0.2f, -FlatsHalfZ + 0.02f);

        BuildFrontBarricade(random);
        BuildRearBarricade(random);
    }

    /// <summary>Across the foot of the ramp, between the planters: two cars broadside with a bin between them, more piled behind.</summary>
    private void BuildFrontBarricade(Random random)
    {
        var slope = float.Atan2(FlatsStreetY, RampEndZ - FlatsHalfZ);
        FlatsWreck("sedan", -3.0f, RampHeight(7.5f), 7.5f, 87f, RustA, -slope);
        FlatsWreck("ute", 2.85f, RampHeight(7.45f), 7.45f, -84f, RustB, -slope);
        FlatsWreck("sedan", 0.6f, RampHeight(9.7f), 9.7f, 74f, RustC, -slope);

        FlatsBin(-0.1f, 0f, 6.78f, 4f, BinBlue, RampHeight(6.78f));
        FlatsBin(-4.75f, 0f, 9.5f, -18f, BinGrey, RampHeight(9.5f));
        FlatsBin(4.7f, 0f, 9.6f, 12f, BinBlue, RampHeight(9.6f));
        // Corners a car doesn't reach, and bins thrown up on the bonnets.
        FlatsBin(-5.05f, 0f, 6.85f, 8f, BinGrey, RampHeight(6.85f));
        FlatsBin(5.05f, 0f, 6.85f, -6f, BinGrey, RampHeight(6.85f));
        FlatsBin(-1.2f, 0.95f, 7.6f, 30f, BinGrey, 0f, tip: 0.22f);
        FlatsBin(3.0f, 0.82f, 7.9f, -24f, BinBlue, 0f, tip: -0.3f);

        AddPallet(FlatsAt(-3.3f, RampHeight(8.9f), 8.9f), 4);
        AddPallet(FlatsAt(3.4f, RampHeight(9.0f), 9.0f), 3);
        AddDrum(FlatsAt(-5.0f, RampHeight(8.15f), 8.15f), RustB, 1.1f, Material.RustyMetal);
        AddDrum(FlatsAt(0.2f, RampHeight(7.9f), 7.9f), RustA, 0.4f, Material.RustyMetal);
        FlatsTyres(-0.75f, RampHeight(6.5f), 6.5f, 3);
        FlatsTyres(0.95f, RampHeight(6.55f), 6.55f, 2);
        FlatsScrap(-2.3f, RampHeight(6.45f), 6.45f, 0.3f, random);
        FlatsScrap(2.4f, RampHeight(6.42f), 6.42f, -0.25f, random);
    }

    /// <summary>Across the back, from wall to wall: three cars end to end, bins in the gaps, and a second row of junk behind them.</summary>
    private void BuildRearBarricade(Random random)
    {
        const float z = -7.6f;
        FlatsWreck("ute", -5.4f, 0f, z, 92f, RustC, 0f);
        FlatsWreck("sedan", 0.05f, 0f, z + 0.1f, -83f, RustB, 0f);
        FlatsWreck("sedan", 5.45f, 0f, z - 0.05f, 95f, RustA, 0f);

        FlatsBin(-8.45f, 0f, -6.85f, 6f, BinGrey, 0f);
        FlatsBin(2.65f, 0f, -6.85f, -5f, BinBlue, 0f);
        FlatsBin(8.45f, 0f, -6.9f, 3f, BinGrey, 0f);
        FlatsBin(-1.3f, 0.92f, -7.35f, 18f, BinGrey, 0f, tip: -0.2f);
        FlatsBin(-7.6f, 0f, -9.3f, 25f, BinBlue, 0f);
        FlatsBin(1.0f, 0.65f, -9.5f, 90f, BinGrey, 0f, tip: 1.45f);   // (on its side, in the second row)
        FlatsBin(7.3f, 0f, -9.4f, -30f, BinGrey, 0f);

        AddPallet(FlatsAt(-2.1f, 0, -9.3f), 5);
        AddPallet(FlatsAt(4.2f, 0, -9.35f), 3);
        AddCrate(FlatsAt(5.6f, 1.55f, z), 0.8f, WoodTint * 0.7f, 1);
        AddDrum(FlatsAt(-8.55f, 0, -8.35f), RustA, 2.2f, Material.RustyMetal);
        AddDrum(FlatsAt(8.5f, 0, -8.25f), RustB, 0.7f, Material.RustyMetal);
        FlatsTyres(-2.0f, 0, -6.45f, 3);
        FlatsTyres(4.3f, 0, -6.42f, 4);
        FlatsTyres(-6.5f, 0, -6.4f, 2);
        FlatsScrap(1.05f, 0, -6.42f, -0.2f, random);
        FlatsScrap(6.5f, 0, -6.42f, 0.35f, random);
    }

    /// <summary>
    /// A rusted-out car (a model, facing -Z, tinted rust), its nose towards <paramref name="yawDegrees"/> as the player's yaw,
    /// tilted by <paramref name="slope"/> about x to sit on the ramp.
    /// </summary>
    private void FlatsWreck(string kind, float x, float y, float z, float yawDegrees, float3 rust, float slope)
    {
        if (TryModel(kind) is not { } car)
            return;
        AddModel(car, float4x4.CreateRotationY(-float.DegreesToRadians(yawDegrees)) * float4x4.CreateRotationX(slope) * float4x4.CreateTranslation(FlatsAt(x, y, z)), rust);
    }

    /// <summary>
    /// A big wheeled refuse bin (1100 litres: 1.37 x 1.07 x 1.33 m): moulded body with ribs, a domed lid, lifting pins at its
    /// sides, a bar to push it by and four castors. <paramref name="tip"/> leans it over, about the bin's own z.
    /// </summary>
    private void FlatsBin(float x, float lift, float z, float yawDegrees, float3 tint, float ground, float tip = 0f)
    {
        const float w = 1.37f, d = 1.07f, bodyY = 0.24f, rim = 1.14f;
        var xf = float4x4.CreateRotationZ(tip) * float4x4.CreateRotationY(-float.DegreesToRadians(yawDegrees)) * float4x4.CreateTranslation(FlatsAt(x, ground + lift, z));
        var plastic = JB(Material.Polymer, tint);
        var darker = JB(Material.Polymer, tint * 0.7f);
        var steel = JB(Material.Steel, new float3(0.3f, 0.3f, 0.3f));
        var rubber = JB(Material.Polymer, new float3(0.03f, 0.03f, 0.03f));
        var fork = JB(Material.PaintedMetal, new float3(0.62f, 0.5f, 0.1f));

        // The body narrows towards the foot, in three steps; ribs down its faces; a heavy rim round its top.
        OBox(plastic, xf, new float3(0, (bodyY + 0.55f) / 2, 0), new float3(w - 0.16f, 0.55f - bodyY, d - 0.14f));
        OBox(plastic, xf, new float3(0, (0.55f + 0.85f) / 2, 0), new float3(w - 0.09f, 0.3f, d - 0.08f));
        OBox(plastic, xf, new float3(0, (0.85f + rim) / 2, 0), new float3(w - 0.03f, rim - 0.85f, d - 0.03f));
        OBox(darker, xf, new float3(0, rim + 0.025f, 0), new float3(w + 0.03f, 0.05f, d + 0.03f));
        for (var rx = -0.5f; rx <= 0.51f; rx += 0.25f)
            foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
                OBox(darker, xf, new float3(rx, 0.68f, side * (d / 2 - 0.035f)), new float3(0.05f, 0.82f, 0.04f));
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            // The lifting pins, and a round moulded boss on each end.
            OCyl(darker, xf, new float3(side * (w / 2 - 0.02f), 0.92f, 0), new float3(side * (w / 2 + 0.03f), 0.92f, 0), 0.13f, 0.13f, 14);
            OCyl(steel, xf, new float3(side * (w / 2 + 0.03f), 0.92f, 0), new float3(side * (w / 2 + 0.1f), 0.92f, 0), 0.028f, 0.028f, 8);
        }
        // The two-part lid, ridged along its middle, its handles; the push bar on the back.
        OBox(plastic, xf, new float3(0, rim + 0.09f, 0), new float3(w - 0.02f, 0.08f, d - 0.03f));
        OBox(plastic, xf, new float3(0, rim + 0.15f, 0), new float3(w - 0.18f, 0.05f, d - 0.3f));
        OBox(darker, xf, new float3(0, rim + 0.135f, 0), new float3(0.03f, 0.1f, d - 0.05f));
        OBox(darker, xf, new float3(-0.34f, rim + 0.185f, 0.1f), new float3(0.32f, 0.03f, 0.14f));
        OBox(darker, xf, new float3(0.34f, rim + 0.185f, 0.1f), new float3(0.32f, 0.03f, 0.14f));
        OCyl(steel, xf, new float3(-w / 2 + 0.12f, 1.0f, -d / 2 - 0.07f), new float3(w / 2 - 0.12f, 1.0f, -d / 2 - 0.07f), 0.016f, 0.016f, 8);

        // Four castors under the corners, each in a yellow fork.
        foreach (var cx in (ReadOnlySpan<float>)[-w / 2 + 0.17f, w / 2 - 0.17f])
        {
            foreach (var cz in (ReadOnlySpan<float>)[-d / 2 + 0.16f, d / 2 - 0.16f])
            {
                OCyl(rubber, xf, new float3(cx - 0.025f, 0.1f, cz), new float3(cx + 0.025f, 0.1f, cz), 0.1f, 0.1f, 14);
                foreach (var dx in (ReadOnlySpan<float>)[-0.035f, 0.035f])
                    OBox(fork, xf, new float3(cx + dx, 0.15f, cz), new float3(0.01f, 0.13f, 0.05f));
                OBox(fork, xf, new float3(cx, 0.225f, cz), new float3(0.11f, 0.025f, 0.11f));
            }
        }
    }

    /// <summary>A heap of old tyres, one on another, each a bit off the one under it.</summary>
    private void FlatsTyres(float x, float ground, float z, int count)
    {
        var id = float4x4.Identity;
        var rubber = JB(Material.Polymer, new float3(0.035f, 0.035f, 0.035f));
        var hub = JB(Material.Plain, new float3(0.012f, 0.012f, 0.012f));
        for (var i = 0; i < count; i++)
        {
            var dx = 0.05f * float.Sin(i * 2.3f + x);
            var dz = 0.05f * float.Cos(i * 1.7f + z);
            var y = ground + i * 0.2f;
            OCyl(rubber, id, FlatsAt(x + dx, y, z + dz), FlatsAt(x + dx, y + 0.2f, z + dz), 0.31f, 0.31f, 16);
            OCyl(hub, id, FlatsAt(x + dx, y + 0.2f, z + dz), FlatsAt(x + dx, y + 0.203f, z + dz), 0.16f, 0.16f, 12);
        }
    }

    /// <summary>A rusted sheet of corrugated iron and a couple of planks, leant against whatever's behind.</summary>
    private void FlatsScrap(float x, float ground, float z, float yaw, Random random)
    {
        var at = FlatsAt(x, ground + 0.72f, z);
        var lean = float4x4.CreateRotationX(z > 0 ? 0.28f : -0.28f) * float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(at);
        OBox(JB(Material.RustyMetal, new float3(0.34f, 0.25f, 0.2f)), lean, float3.Zero, new float3(1.3f, 1.5f, 0.015f));
        var wood = JB(Material.Wood, WoodTint * 0.65f);
        for (var i = 0; i < 2; i++)
        {
            var dx = (i - 0.5f) * 0.9f + 0.2f * ((float)random.NextDouble() - 0.5f);
            var skew = float4x4.CreateRotationZ(0.12f * (i * 2 - 1)) * lean;
            OBox(wood, skew, new float3(dx, 0.05f, z > 0 ? -0.04f : 0.04f), new float3(0.14f, 1.75f, 0.025f));
        }
    }
}
