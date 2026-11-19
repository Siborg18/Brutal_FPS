using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The firing range, for trying out how bullets fly: a concrete firing line on the open plain beside the outback's road,
/// a couple of hundred metres north of the roadhouse, past the last of the scrub, looking north up an empty lane to an
/// earth berm. A target stands at each distance, fanned out (the furthest in the middle) so that none is in front of
/// another, with its range painted on a sign beside it (bigger the further out, to be read through a scope) and across
/// the ground in front of it. The boards and the reset button are <see cref="FiringRange"/>'s.
/// </summary>
public sealed partial class Level
{
    /// <summary>A target's distance from the firing line, and how far to the side of the lane's centre (east is +).</summary>
    public readonly record struct RangeLane(int Distance, float Side);

    /// <summary>Where you stand to shoot: the middle of the firing line. North (yaw 0) is straight down the range.</summary>
    public static readonly float3 RangeFiringPoint = OutbackOrigin + new float3(22, 0, -340);

    public static readonly RangeLane[] RangeLanes =
    [
        new(25, -6), new(50, 7), new(100, -9), new(150, 9), new(200, -8), new(300, 7), new(400, -7), new(500, 4), new(600, -4), new(800, 0),
    ];

    /// <summary>Height of a target's centre: high enough that a shot a little under it still goes on past, not into the dirt in front.</summary>
    public const float RangeBoardHeight = 2f;

    /// <summary>Whether a point is on the firing range: anywhere from just behind the firing line to the berm.</summary>
    public static bool OnFiringRange(float3 point)
    {
        var fromLine = point - RangeFiringPoint;
        return float.Abs(fromLine.X) < 60f && fromLine.Z < 30f && fromLine.Z > -860f && float.Abs(fromLine.Y) < 10f;
    }

    /// <summary>The top of the reset button's post, to the left of the bench.</summary>
    public static readonly float3 RangeButtonPost = RangeFiringPoint + new float3(-2.6f, 1.05f, -0.2f);

    /// <summary>The range's gun locker (<see cref="GunLocker"/>), the steel cabinet to the right of the bench.</summary>
    public static readonly float3 RangeLockerPoint = RangeFiringPoint + new float3(3.6f, 0, -0.9f);

    private static readonly float3 RangeSignWhite = new(1.5f, 1.48f, 1.4f);   // (bright: their faces are out of the sun, which is to the north)
    private static readonly float3 RangeInk = new(0.04f, 0.04f, 0.04f);
    private static readonly float3 RangePaint = new(0.82f, 0.8f, 0.72f);
    private const float RangeLineWidth = 34f;

    private void BuildFiringRange()
    {
        var o = RangeFiringPoint;

        // The pad, and the firing line painted across it: flat on the ground, so they never flicker against it. (The pad is
        // in two halves either side of the line: a patch laid over another would flicker against that instead.)
        RangeGroundPaint(o + new float3(0, 0, -1.325f), new float2(RangeLineWidth, 2.4f), ForecourtTint);
        RangeGroundPaint(o, new float2(RangeLineWidth, 0.25f), RangePaint);
        RangeGroundPaint(o + new float3(0, 0, 2.125f), new float2(RangeLineWidth, 4f), ForecourtTint);

        // The bench in front of you, and a crate of ammunition to the right of it.
        const float benchTop = 0.92f, benchHalf = 1.5f, benchZ = -1.15f;
        Add(o + new float3(0, benchTop - 0.03f, benchZ), new float3(benchHalf * 2, 0.06f, 0.75f), WoodTint, Material.Wood);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            Add(o + new float3(side * (benchHalf - 0.12f), (benchTop - 0.06f) / 2, benchZ), new float3(0.1f, benchTop - 0.06f, 0.6f), WoodTint * 0.8f, Material.Wood);
        AddCrate(o + new float3(benchHalf + 0.6f, 0, -0.9f), 0.6f, WoodTint * 0.85f);

        // The reset button's post (the button itself moves: see FiringRange), its plate and its label.
        var post = RangeButtonPost;
        Add(post - new float3(0, post.Y / 2, 0), new float3(0.14f, post.Y, 0.14f), new float3(0.72f, 0.58f, 0.08f), Material.PaintedMetal);
        Add(post + new float3(0, 0.015f, 0), new float3(0.3f, 0.03f, 0.3f), SteelTint, Material.Steel);
        AddLettering("RESET", post + new float3(0, -0.2f, 0.07f), new float3(0, 0, 1), 0.06f, Material.Plain, RangeInk);
        AddLettering("TARGETS", post + new float3(0, -0.3f, 0.07f), new float3(0, 0, 1), 0.045f, Material.Plain, RangeInk);

        // The gun locker: a green steel cabinet with its name on the door.
        var lockerGreen = new float3(0.20f, 0.27f, 0.16f);
        Add(RangeLockerPoint + new float3(0, 0.95f, 0), new float3(1.1f, 1.9f, 0.55f), lockerGreen, Material.PaintedMetal);
        Add(RangeLockerPoint + new float3(0, 0.95f, 0.285f), new float3(0.02f, 1.8f, 0.02f), lockerGreen * 0.4f, Material.PaintedMetal, collides: false);
        AddLettering("GUN", RangeLockerPoint + new float3(0, 1.5f, 0.285f), new float3(0, 0, 1), 0.12f, Material.Plain, RangePaint);
        AddLettering("LOCKER", RangeLockerPoint + new float3(0, 1.3f, 0.285f), new float3(0, 0, 1), 0.09f, Material.Plain, RangePaint);

        // The name, for anyone coming up the road.
        var name = o + new float3(-14f, 0, 1f);
        RangeSign("FIRING RANGE", name + new float3(0, 2.2f, 0), new float2(5.2f, 1.1f), 0.5f);

        foreach (var lane in RangeLanes)
        {
            var foot = o + new float3(lane.Side, 0, -lane.Distance);
            RangeGroundPaint(new float3(o.X, 0, foot.Z), new float2(RangeLineWidth, 0.4f), RangePaint);

            // Its sign, on the outside of the target. Far ones are bigger, so they look about the same size from here on.
            var scale = float.Max(1f, lane.Distance / 200f);
            var size = new float2(2.2f, 0.9f) * scale;
            var outward = lane.Side < 0 ? -1f : 1f;
            var x = lane.Side + outward * (Target.BoardSize / 2 + 0.35f + size.X / 2 + 0.3f);
            RangeSign($"{lane.Distance}M", o + new float3(x, 0.8f + size.Y / 2, -lane.Distance), size, 0.5f * scale);
        }

        // The berm behind the last target (the ground runs out not long after).
        var berm = new MeshBuilder().Hill(o + new float3(0, -2.5f, -835), new float3(90, 11, 16), new Random(77));
        _props.Add(new Prop(berm.Build(), float4x4.Identity, RedDirtTint * 0.85f, Material.RedDirt));

        FlushPoliceBatches();   // (the lettering's batches are shared with the police station's builder)
    }

    /// <summary>A flat patch lying on the ground, without a body of its own (bullets strike the ground under it).</summary>
    private void RangeGroundPaint(float3 center, float2 size, float3 tint) =>
        _props.Add(new Prop(UnitBox, float4x4.CreateScale(size.X, 0.02f, size.Y) * float4x4.CreateTranslation(center + new float3(0, 0.01f, 0)), tint, Material.Plain, Decal: true));

    /// <summary>A white board on two steel posts, facing back down the range, with block capitals on it.</summary>
    private void RangeSign(string text, float3 center, float2 size, float letterHeight)
    {
        var depth = 0.04f * float.Max(1f, size.Y);
        Add(center, new float3(size.X, size.Y, depth), RangeSignWhite, Material.Plain);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var postHeight = center.Y + size.Y / 2;
            Add(new float3(center.X + side * size.X * 0.38f, postHeight / 2, center.Z - depth), new float3(0.08f, postHeight, 0.05f), StandColor, Material.Steel);
        }
        AddLettering(text, center + new float3(0, 0, depth / 2), new float3(0, 0, 1), letterHeight, Material.Plain, RangeInk, 0.03f * float.Max(1f, size.Y));
    }
}
