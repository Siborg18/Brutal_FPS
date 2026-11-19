using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The yard's shipping container as something that can move, so the intro can fly it in (<see cref="PrisonDelivery"/>):
/// everything built by <c>BuildPrisonContainer</c> and everything else standing in its space (its boxes, with the
/// portal's doorway cut in them) is drawn apart from the level, through <see cref="PrisonContainerPose"/>. Unless the
/// intro moves it, it stands in the yard as it always has. Its colliders stay where it lands: nothing can reach them
/// while it's in the air, as the intro has the player on rails.
/// </summary>
public sealed partial class Level
{
    private readonly Dictionary<(Material Material, float3 Tint), MeshBuilder> _containerBatches = [];
    private readonly List<(Mesh Mesh, float3 Tint, Material Material)> _containerMeshes = [];
    private readonly List<LevelBox> _containerBoxes = [];
    private bool _buildingContainer;

    /// <summary>Whether it's drawn: false until the helicopter brings it.</summary>
    public bool PrisonContainerShown { get; set; } = true;

    /// <summary>From where it stands in the yard to where it is now.</summary>
    public float4x4 PrisonContainerPose { get; set; } = float4x4.Identity;

    /// <summary>Where the container's own origin is when it stands in the yard (bottom centre).</summary>
    public static float3 PrisonContainerOrigin => JAt(0, 0, 0);

    /// <summary>Height of the container's roof above its origin.</summary>
    public const float PrisonContainerHeight = PcHeight;

    /// <summary>Half its length and width, for dust round its foot.</summary>
    public static float2 PrisonContainerHalfSize => new(PcLength / 2, PcWidth / 2);

    private static bool InPrisonContainer(in Aabb box)
    {
        var c = box.Center - PrisonOrigin;
        return c.X > PcWestX - 0.05f && c.X < PcEastX + 0.05f
            && c.Z > -PcWidth / 2 - 0.05f && c.Z < PcWidth / 2 + 0.05f
            && c.Y > 0f && c.Y < PcHeight + 0.05f;
    }

    /// <summary>
    /// A small prime-focus satellite dish on the roof at the closed (rear) end, on a post with three splayed legs, its
    /// lead going down through the roof: 0.9 m across, aimed up to the south-east. It stands on the centre line, between
    /// the two rear sling legs, and inside the V they make when the sling falls flat on the roof, so it's clear of them
    /// both in the air and on the ground. (The legs run from the roof's corners to a ring over x = -3.9, 5.8 m up.)
    /// </summary>
    private void AddPrisonContainerDish()
    {
        const float radius = 0.45f, focus = 0.34f, shell = 0.025f;
        const int rings = 10, around = 48;
        var id = float4x4.Identity;
        var white = new float3(0.78f, 0.79f, 0.77f);
        var paint = JB(Material.PaintedMetal, white);
        var steel = JB(Material.Steel, new float3(0.42f, 0.43f, 0.44f));
        var black = JB(Material.Plain, new float3(0.035f, 0.035f, 0.04f));

        // The dish's frame: origin at the bottom of the bowl, z along its aim, x level.
        var aim = float3.Normalize(new float3(0.663f, 0.643f, 0.383f));
        var right = float3.Normalize(float3.Cross(float3.UnitY, aim));
        var up = float3.Cross(aim, right);
        var vertex = JAt(PcWestX + 0.75f, PcHeight + 0.72f, 0);
        var dish = new float4x4(
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            aim.X, aim.Y, aim.Z, 0,
            vertex.X, vertex.Y, vertex.Z, 1);

        // The bowl, a paraboloid: its inside, its back, and the rim between them.
        static float Sag(float r) => r * r / (4f * focus);
        float3 Surface(float r, float a, float back) =>
            float3.Transform(new float3(r * float.Cos(a), r * float.Sin(a), Sag(r) - back), dish);
        float3 Facing(float r, float a) =>
            float3.TransformNormal(float3.Normalize(new float3(-r * float.Cos(a) / (2f * focus), -r * float.Sin(a) / (2f * focus), 1f)), dish);
        float3 Outward(float a) => float3.TransformNormal(new float3(float.Cos(a), float.Sin(a), 0), dish);
        void Patch(float3 p0, float3 n0, float3 p1, float3 n1, float3 p2, float3 n2, float3 p3, float3 n3)
        {
            paint.Triangle(p0, n0, p1, n1, p2, n2);
            paint.Triangle(p0, n0, p2, n2, p3, n3);
        }
        for (var k = 0; k < around; k++)
        {
            var a0 = float.Tau * k / around;
            var a1 = float.Tau * (k + 1) / around;
            for (var i = 0; i < rings; i++)
            {
                var r0 = radius * i / rings;
                var r1 = radius * (i + 1) / rings;
                Patch(Surface(r0, a0, 0), Facing(r0, a0), Surface(r1, a0, 0), Facing(r1, a0), Surface(r1, a1, 0), Facing(r1, a1), Surface(r0, a1, 0), Facing(r0, a1));
                Patch(Surface(r0, a0, shell), -Facing(r0, a0), Surface(r1, a0, shell), -Facing(r1, a0), Surface(r1, a1, shell), -Facing(r1, a1), Surface(r0, a1, shell), -Facing(r0, a1));
            }
            Patch(Surface(radius, a0, 0), Outward(a0), Surface(radius, a0, shell), Outward(a0), Surface(radius, a1, shell), Outward(a1), Surface(radius, a1, 0), Outward(a1));
        }

        // The feed at the focus, facing the bowl, held by three arms from the rim.
        OCyl(paint, dish, new float3(0, 0, focus - 0.07f), new float3(0, 0, focus + 0.05f), 0.032f, 0.024f, 12);
        OCyl(paint, dish, new float3(0, 0, focus - 0.02f), new float3(0, 0, focus + 0.01f), 0.04f, 0.04f, 12);
        ODisc(black, dish, new float3(0, 0, focus - 0.074f), 0.026f, 0.005f, 12);
        ReadOnlySpan<float> arms = [float.Pi / 2, float.Pi * 7f / 6f, float.Pi * 11f / 6f];
        foreach (var a in arms)
        {
            var r = radius * 0.96f;
            OCyl(paint, dish, new float3(r * float.Cos(a), r * float.Sin(a), Sag(r)), new float3(0, 0, focus - 0.02f), 0.009f, 0.009f, 6);
        }

        // The mount: a hub on the bowl's back, a post down to the roof, three splayed legs on foot plates, an adjuster.
        OCyl(steel, dish, new float3(0, 0, -shell), new float3(0, 0, -0.12f), 0.06f, 0.045f, 12);
        var head = float3.Transform(new float3(0, 0, -0.12f), dish);
        var roof = PrisonOrigin.Y + PcHeight;
        var foot = new float3(head.X, roof, head.Z);
        OCyl(paint, id, foot, head + new float3(0, 0.03f, 0), 0.035f, 0.035f, 12);
        OBox(steel, id, foot + new float3(0, 0.008f, 0), new float3(0.13f, 0.016f, 0.13f));
        foreach (var a in (ReadOnlySpan<float>)[float.Pi / 3, float.Pi, float.Pi * 5f / 3f])
        {
            var plant = foot + new float3(0.28f * float.Cos(a), 0, 0.28f * float.Sin(a));
            OCyl(paint, id, plant + new float3(0, 0.012f, 0), foot + new float3(0, 0.27f, 0), 0.014f, 0.014f, 8);
            OBox(steel, id, plant + new float3(0, 0.006f, 0), new float3(0.08f, 0.012f, 0.08f));
        }
        OCyl(steel, id, foot + new float3(0, 0.36f, 0), float3.Transform(new float3(0, -0.28f, Sag(0.28f) - shell), dish), 0.011f, 0.011f, 8);

        // Its lead: from the feed along an arm, round the rim, to the post and down through a gland in the roof.
        var armAngle = float.Pi * 7f / 6f;
        var rimBack = new float3((radius + 0.008f) * float.Cos(armAngle), (radius + 0.008f) * float.Sin(armAngle), Sag(radius) - shell - 0.01f);
        ReadOnlySpan<float3> lead =
        [
            float3.Transform(new float3(0, 0, focus + 0.04f), dish),
            float3.Transform(rimBack + new float3(0, 0, 0.03f), dish),
            float3.Transform(rimBack, dish),
            foot + new float3(0.04f, 0.3f, 0),
            foot + new float3(0.1f, 0.02f, 0.08f),
        ];
        for (var i = 0; i < lead.Length - 1; i++)
            OCyl(black, id, lead[i], lead[i + 1], 0.005f, 0.005f, 5);
        OCyl(steel, id, foot + new float3(0.1f, 0, 0.08f), foot + new float3(0.1f, 0.03f, 0.08f), 0.016f, 0.012f, 8);
    }

    private void FinishContainerMeshes()
    {
        foreach (var ((material, tint), builder) in _containerBatches)
            _containerMeshes.Add((builder.Build(), tint, material));
        _containerBatches.Clear();
    }

    private void DrawPrisonContainer(Renderer renderer)
    {
        if (!PrisonContainerShown)
            return;
        var pose = PrisonContainerPose;

        // Its paint and rust are mapped where it will stand in the yard, so they ride with it and don't crawl as it moves.
        float4x4.Invert(pose, out var toYard);
        renderer.SetTextureSpace(toYard);
        foreach (var (mesh, tint, material) in _containerMeshes)
            renderer.DrawMesh(mesh, pose, tint, ShadeMode.Lit, material);
        foreach (var box in _containerBoxes)
            box.Draw(renderer, pose);
        if (_prisonDoors.TryGetValue("container", out var doors))
            DrawDoor(renderer, doors, pose);   // (its doors go where it goes)
        renderer.SetTextureSpace(float4x4.Identity);
    }
}
