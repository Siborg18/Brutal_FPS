using Brutal.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace BrutalFps.Engine;

/// <summary>
/// A sun shadow map for one outdoor area: a depth texture rendered from the sun with an orthographic camera.
/// The level is static, so each map is rendered once at startup and then only sampled.
/// </summary>
public sealed unsafe class ShadowMap : IDisposable
{
    private GlFramebuffer _framebuffer;
    private GlTexture _depth;
    private GlRenderbuffer _color; // unused, but keeps the framebuffer complete without glDrawBuffer(GL_NONE)

    public int Size { get; }
    public GlTexture DepthTexture => _depth;
    public GlFramebuffer Framebuffer => _framebuffer;

    /// <summary>World -> shadow-map clip space.</summary>
    public float4x4 ViewProjection { get; }

    /// <summary>Unit vector pointing towards the sun.</summary>
    public float3 SunDirection { get; }

    /// <param name="size">Texture resolution (square).</param>
    /// <param name="center">Centre of the area to cover, on the ground.</param>
    /// <param name="halfExtent">Half the width of the square area covered, in metres.</param>
    /// <param name="sunDirection">Direction towards the sun.</param>
    public ShadowMap(int size, float3 center, float halfExtent, float3 sunDirection)
    {
        Size = size;
        SunDirection = float3.Normalize(sunDirection);

        const float distance = 250f;
        // A sun straight overhead would make the look-at degenerate (NaN): pick another up vector then.
        var up = float.Abs(float3.Normalize(SunDirection).Y) > 0.99f ? float3.UnitX : float3.UnitY;
        var view = float4x4.CreateLookAt(center + SunDirection * distance, center, up);
        var projection = float4x4.CreateOrthographic(halfExtent * 2, halfExtent * 2, 1f, distance * 2);
        ViewProjection = view * projection;

        Span<GlTexture> textures = stackalloc GlTexture[1];
        GenTextures(textures);
        _depth = textures[0];
        BindTexture(GL_TEXTURE_2D, _depth.Id);
        TexImage2D(GL_TEXTURE_2D, 0, (int)GL_DEPTH_COMPONENT24, size, size, 0, GL_DEPTH_COMPONENT, GL_FLOAT, null);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);

        (_color, _framebuffer) = CreateFramebuffer(_depth, size, "Shadow map");
    }

    /// <summary>A framebuffer that draws depth into <paramref name="depth"/>, with a colour buffer nothing is drawn to (it keeps the framebuffer complete without glDrawBuffer(GL_NONE)).</summary>
    internal static (GlRenderbuffer Color, GlFramebuffer Framebuffer) CreateFramebuffer(GlTexture depth, int size, string name) => CreateFramebuffer(depth, size, size, name);

    /// <inheritdoc cref="CreateFramebuffer(GlTexture, int, string)"/>
    internal static (GlRenderbuffer Color, GlFramebuffer Framebuffer) CreateFramebuffer(GlTexture depth, int width, int height, string name)
    {
        Span<GlRenderbuffer> renderbuffers = stackalloc GlRenderbuffer[1];
        GenRenderbuffers(renderbuffers);
        var color = renderbuffers[0];
        BindRenderbuffer(GL_RENDERBUFFER, color);
        RenderbufferStorage(GL_RENDERBUFFER, GL_R8, width, height);

        Span<GlFramebuffer> framebuffers = stackalloc GlFramebuffer[1];
        GenFramebuffers(framebuffers);
        var framebuffer = framebuffers[0];
        BindFramebuffer(GL_FRAMEBUFFER, framebuffer);
        FramebufferRenderbuffer(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_RENDERBUFFER, color);
        FramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_TEXTURE_2D, depth.Id, 0);

        var status = CheckFramebufferStatus(GL_FRAMEBUFFER);
        BindFramebuffer(GL_FRAMEBUFFER, default);
        if (status.Id != GL_FRAMEBUFFER_COMPLETE)
            throw new InvalidOperationException($"{name} framebuffer is incomplete (status 0x{status.Id:X}).");
        return (color, framebuffer);
    }

    public void Dispose()
    {
        DeleteFramebuffers([_framebuffer]);
        DeleteTextures([_depth]);
        DeleteRenderbuffers([_color]);
    }
}
