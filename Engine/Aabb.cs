using Brutal.Numerics;

namespace BrutalFps.Engine;

/// <summary>Axis-aligned bounding box with the queries the game needs.</summary>
public readonly record struct Aabb(float3 Min, float3 Max)
{
    public static Aabb FromCenterSize(float3 center, float3 size) => new(center - size * 0.5f, center + size * 0.5f);

    public float3 Center => (Min + Max) * 0.5f;
    public float3 Size => Max - Min;

    public bool Overlaps(in Aabb other) =>
        Min.X < other.Max.X && Max.X > other.Min.X &&
        Min.Y < other.Max.Y && Max.Y > other.Min.Y &&
        Min.Z < other.Max.Z && Max.Z > other.Min.Z;

    public bool Contains(float3 p) =>
        p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;

    public Aabb Expand(float amount) => new(Min - new float3(amount), Max + new float3(amount));

    /// <summary>The box round this one after <paramref name="m"/> (v * m).</summary>
    public Aabb Transformed(in float4x4 m)
    {
        var c = float3.Transform(Center, m);
        var e = Size * 0.5f;
        var r = new float3(
            float.Abs(m.X.X) * e.X + float.Abs(m.Y.X) * e.Y + float.Abs(m.Z.X) * e.Z,
            float.Abs(m.X.Y) * e.X + float.Abs(m.Y.Y) * e.Y + float.Abs(m.Z.Y) * e.Z,
            float.Abs(m.X.Z) * e.X + float.Abs(m.Y.Z) * e.Y + float.Abs(m.Z.Z) * e.Z);
        return new Aabb(c - r, c + r);
    }

    /// <summary>
    /// Slab ray test. <paramref name="direction"/> must be normalised.
    /// Returns the entry distance (0 if the origin starts inside) and the face normal that was hit.
    /// </summary>
    public bool Raycast(float3 origin, float3 direction, float maxDistance, out float distance, out float3 normal)
    {
        var tEnter = float.NegativeInfinity;
        var tExit = float.PositiveInfinity;
        var enterAxis = -1;
        var enterSign = 0f;

        for (var axis = 0; axis < 3; axis++)
        {
            var o = origin[axis];
            var d = direction[axis];
            var min = Min[axis];
            var max = Max[axis];

            if (float.Abs(d) < 1e-8f)
            {
                if (o < min || o > max)
                {
                    distance = 0;
                    normal = default;
                    return false;
                }

                continue;
            }

            var inv = 1f / d;
            var t1 = (min - o) * inv;
            var t2 = (max - o) * inv;
            var sign = -1f; // entering through the min face -> normal points to -axis
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
                sign = 1f;
            }

            if (t1 > tEnter)
            {
                tEnter = t1;
                enterAxis = axis;
                enterSign = sign;
            }

            tExit = float.Min(tExit, t2);
            if (tEnter > tExit)
            {
                distance = 0;
                normal = default;
                return false;
            }
        }

        if (tExit < 0 || tEnter > maxDistance)
        {
            distance = 0;
            normal = default;
            return false;
        }

        if (tEnter < 0 || enterAxis < 0)
        {
            // Origin is inside the box.
            distance = 0;
            normal = -direction;
            return true;
        }

        distance = tEnter;
        normal = default;
        normal[enterAxis] = enterSign;
        return true;
    }
}
