using Brutal.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace BrutalFps.Engine;

/// <summary>An off-screen framebuffer (colour texture + depth buffer) that resizes with the window.</summary>
public sealed unsafe class RenderTarget : IDisposable
{
    private GlFramebuffer _framebuffer;
    private GlTexture _color;
    private GlRenderbuffer _depth;

    public int2 Size { get; private set; }
    public GlTexture Texture => _color;
    public GlFramebuffer Framebuffer => _framebuffer;

    /// <summary>(Re)creates the buffers if the requested size differs from the current one.</summary>
    public void EnsureSize(int2 size)
    {
        if (size == Size && _framebuffer.Id != 0)
            return;

        Release();
        Size = size;

        Span<GlTexture> textures = stackalloc GlTexture[1];
        GenTextures(textures);
        _color = textures[0];
        BindTexture(GL_TEXTURE_2D, _color.Id);
        TexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, size.X, size.Y, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);

        Span<GlRenderbuffer> renderbuffers = stackalloc GlRenderbuffer[1];
        GenRenderbuffers(renderbuffers);
        _depth = renderbuffers[0];
        BindRenderbuffer(GL_RENDERBUFFER, _depth);
        RenderbufferStorage(GL_RENDERBUFFER, GL_DEPTH_COMPONENT24, size.X, size.Y);

        Span<GlFramebuffer> framebuffers = stackalloc GlFramebuffer[1];
        GenFramebuffers(framebuffers);
        _framebuffer = framebuffers[0];
        BindFramebuffer(GL_FRAMEBUFFER, _framebuffer);
        FramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, _color.Id, 0);
        FramebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_RENDERBUFFER, _depth);

        var status = CheckFramebufferStatus(GL_FRAMEBUFFER);
        BindFramebuffer(GL_FRAMEBUFFER, default);
        if (status.Id != GL_FRAMEBUFFER_COMPLETE)
            throw new InvalidOperationException($"Portal framebuffer is incomplete (status 0x{status.Id:X}).");
    }

    private void Release()
    {
        if (_framebuffer.Id != 0)
            DeleteFramebuffers([_framebuffer]);
        if (_color.Id != 0)
            DeleteTextures([_color]);
        if (_depth.Id != 0)
            DeleteRenderbuffers([_depth]);

        _framebuffer = default;
        _color = default;
        _depth = default;
    }

    public void Dispose() => Release();
}
