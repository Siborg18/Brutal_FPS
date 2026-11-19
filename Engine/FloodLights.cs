using Brutal.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace BrutalFps.Engine;

/// <summary>
/// A high-mast floodlight: a spot light aimed at <paramref name="Target"/>, with its own shadow map.
/// <paramref name="Intensity"/> is the irradiance (game units) it gives 1 m away on its axis; it falls off with
/// distance squared. <paramref name="Color"/> is linear (sodium lamps are deep orange).
/// </summary>
public readonly record struct FloodLight(float3 Position, float3 Target, float3 Color, float Intensity,
    float OuterDegrees = 62f, float InnerDegrees = 34f)
{
    public float3 Direction => float3.Normalize(Target - Position);

    /// <summary>World -> the light's shadow-map clip space (a perspective view down its axis, a little wider than its cone).</summary>
    public float4x4 ViewProjection
    {
        get
        {
            var dir = Direction;
            var up = float.Abs(dir.Y) > 0.99f ? float3.UnitX : float3.UnitY;
            var view = float4x4.CreateLookAt(Position, Position + dir, up);
            var fov = float.DegreesToRadians(float.Min(2 * OuterDegrees + 8f, 170f));
            var projection = float4x4.CreatePerspectiveFieldOfView(fov, 1f, 1.5f, 160f);
            return view * projection;
        }
    }
}

/// <summary>
/// A light that glows in the night haze (its in-scattering along every view ray): lamp heads, crane lights. With a
/// <paramref name="Range"/> above zero it also lights surfaces within that range, without shadows.
/// </summary>
public readonly record struct GlowPoint(float3 Position, float3 Color, float Intensity, float Range = 0f);

/// <summary>
/// Shadow maps for up to four floodlights in one depth texture (2 x 2 tiles), sampled with hardware depth
/// comparison. The level is static, so the tiles are rendered once at startup.
/// </summary>
public sealed unsafe class FloodShadowAtlas : IDisposable
{
    public const int MaxLights = 4;

    private GlFramebuffer _framebuffer;
    private GlTexture _depth;
    private GlRenderbuffer _color; // keeps the framebuffer complete without glDrawBuffer(GL_NONE)

    public int Size { get; }
    public GlTexture DepthTexture => _depth;
    public GlFramebuffer Framebuffer => _framebuffer;

    public FloodShadowAtlas(int size)
    {
        Size = size;
        Span<GlTexture> textures = stackalloc GlTexture[1];
        GenTextures(textures);
        _depth = textures[0];
        BindTexture(GL_TEXTURE_2D, _depth.Id);
        TexImage2D(GL_TEXTURE_2D, 0, (int)GL_DEPTH_COMPONENT24, size, size, 0, GL_DEPTH_COMPONENT, GL_FLOAT, null);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_COMPARE_MODE, (int)GL_COMPARE_REF_TO_TEXTURE);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_COMPARE_FUNC, (int)GL_LEQUAL);

        (_color, _framebuffer) = ShadowMap.CreateFramebuffer(_depth, size, "Flood shadow atlas");
    }

    /// <summary>Pixel origin of tile <paramref name="index"/> (0..3) in the atlas.</summary>
    public int2 TileOrigin(int index) => new((index % 2) * (Size / 2), (index / 2) * (Size / 2));

    public void Dispose()
    {
        DeleteFramebuffers([_framebuffer]);
        DeleteTextures([_depth]);
        DeleteRenderbuffers([_color]);
    }
}

/// <summary>
/// Shadow maps for burning flares (Game/Flares.cs): each is a point light with six faces, like a cube map's, as a row of
/// tiles in one depth texture (one row per flare), sampled with hardware depth comparison. A flare's row is rendered once,
/// as it settles. The shader's flareShadow picks the face and works out the tile: its face order and axes must match
/// <see cref="ViewProjection"/>.
/// </summary>
public sealed unsafe class FlareShadowAtlas : IDisposable
{
    public const int MaxFlares = 4;    // must match MAX_FLARES in the fragment shader
    public const int Faces = 6;
    public const float Near = 0.04f, Far = 24f;
    public const float SkyNear = 2f, SkyFar = 420f;   // m: one hanging high in the sky sees the ground hundreds of metres off
    private const float FaceDegrees = 94f;   // a little wider than a cube's face, so filtering at its edges has something to read

    private GlFramebuffer _framebuffer;
    private GlTexture _depth;
    private GlRenderbuffer _color;

    public int TileSize { get; }
    public GlTexture DepthTexture => _depth;
    public GlFramebuffer Framebuffer => _framebuffer;

    /// <summary>The tangent of half a face's field of view: a point's offset across the face, over its distance along the axis, at the face's edge.</summary>
    public static float FaceTangent => float.Tan(float.DegreesToRadians(FaceDegrees * 0.5f));

    public FlareShadowAtlas(int tileSize)
    {
        TileSize = tileSize;
        int width = tileSize * Faces, height = tileSize * MaxFlares;
        Span<GlTexture> textures = stackalloc GlTexture[1];
        GenTextures(textures);
        _depth = textures[0];
        BindTexture(GL_TEXTURE_2D, _depth.Id);
        TexImage2D(GL_TEXTURE_2D, 0, (int)GL_DEPTH_COMPONENT24, width, height, 0, GL_DEPTH_COMPONENT, GL_FLOAT, null);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_COMPARE_MODE, (int)GL_COMPARE_REF_TO_TEXTURE);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_COMPARE_FUNC, (int)GL_LEQUAL);

        (_color, _framebuffer) = ShadowMap.CreateFramebuffer(_depth, width, height, "Flare shadow atlas");
    }

    /// <summary>Pixel origin of a flare's face in the atlas.</summary>
    public int2 TileOrigin(int flare, int face) => new(face * TileSize, flare * TileSize);

    /// <summary>World -> a face's clip space, from a light at <paramref name="at"/>: faces +X, -X, +Y, -Y, +Z, -Z. <paramref name="sky"/>: one hanging high in the sky (<see cref="SkyNear"/>, <see cref="SkyFar"/>).</summary>
    public static float4x4 ViewProjection(float3 at, int face, bool sky = false)
    {
        var (direction, up) = face switch
        {
            0 => (float3.UnitX, float3.UnitY),
            1 => (-float3.UnitX, float3.UnitY),
            2 => (float3.UnitY, float3.UnitZ),
            3 => (-float3.UnitY, float3.UnitZ),
            4 => (float3.UnitZ, float3.UnitY),
            _ => (-float3.UnitZ, float3.UnitY),
        };
        var view = float4x4.CreateLookAt(at, at + direction, up);
        return view * float4x4.CreatePerspectiveFieldOfView(float.DegreesToRadians(FaceDegrees), 1f, sky ? SkyNear : Near, sky ? SkyFar : Far);
    }

    public void Dispose()
    {
        DeleteFramebuffers([_framebuffer]);
        DeleteTextures([_depth]);
        DeleteRenderbuffers([_color]);
    }
}

/// <summary>
/// A light without shadows that lights only what's inside <paramref name="Bounds"/>: a bulb, the daylight down a vent,
/// a portal's glow. <paramref name="Intensity"/> is the irradiance 1 m away; it falls off with distance squared and
/// smoothly to nothing at <paramref name="Range"/>.
/// </summary>
public readonly record struct LocalLight(float3 Position, float3 Color, float Intensity, float Range, Aabb Bounds);

/// <summary>
/// A dim interior in an outdoor area (a shed, a pit): inside <paramref name="Bounds"/> the sky's light fades with
/// distance from <paramref name="Opening"/> (over about <paramref name="Reach"/> metres) down to <paramref name="Floor"/>.
/// </summary>
public readonly record struct ShadeVolume(Aabb Bounds, float3 Opening, float Reach, float Floor, bool NoSun = false);
