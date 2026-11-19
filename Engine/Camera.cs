using Brutal.Numerics;

namespace BrutalFps.Engine;

/// <summary>
/// A first-person camera described by a position plus yaw/pitch.
/// Right-handed, +Y up; yaw 0 looks down -Z, positive yaw turns right.
/// Kept separate from the player so later features (e.g. portal views) can render the scene from other cameras.
/// </summary>
public struct Camera
{
    public float3 Position;
    public float Yaw;
    public float Pitch;
    public float FieldOfViewDegrees;
    public float NearPlane;
    public float FarPlane;

    public static Camera Default => new()
    {
        FieldOfViewDegrees = 80f,
        NearPlane = 0.05f,
        FarPlane = 3000f, // far enough for the outback's sky box, from anywhere on its plain
    };

    public readonly float3 Forward => DirectionFromAngles(Yaw, Pitch);

    public static float3 DirectionFromAngles(float yaw, float pitch)
    {
        var cosPitch = float.Cos(pitch);
        return new float3(float.Sin(yaw) * cosPitch, float.Sin(pitch), -float.Cos(yaw) * cosPitch);
    }

    public readonly float4x4 View => float4x4.CreateLookTo(Position, Forward, float3.UnitY);

    /// <summary>Camera-to-world transform (inverse of the view matrix).</summary>
    public readonly float4x4 World
    {
        get
        {
            float4x4.Invert(View, out var world);
            return world;
        }
    }

    public readonly float4x4 Projection(float aspect) =>
        float4x4.CreatePerspectiveFieldOfView(
            float.DegreesToRadians(FieldOfViewDegrees),
            aspect,
            NearPlane,
            FarPlane);
}
