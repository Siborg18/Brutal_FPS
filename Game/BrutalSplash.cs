using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;
using static Brutal.OpenGlApi.OpenGl;

namespace BrutalFps.Game;

/// <summary>
/// The first thing the game shows: the BRUTAL logo and its copyright notice, before any menu or gameplay. The BRUTAL
/// License v1.0 asks for both (section 2.1: the copyright notice in the credits or equivalent; section 2.2: the logo,
/// unaltered except for scaling, for long enough to be legible, before gameplay starts). The logo is the white one from
/// BRUTAL's branding (Assets/Branding/LOGO_BRUTAL_WHT.png), drawn exactly as it is in the file. It stays up for
/// <see cref="Seconds"/>; after <see cref="MinSeconds"/> a click, Space, Enter or Esc moves on.
/// </summary>
public static class BrutalSplash
{
    public const float Seconds = 3f, MinSeconds = 1.5f;
    public const string Notice = "BRUTAL game development framework is Copyright (c) 2024 Ahwoo Limited.";
    private static readonly float3 Backdrop = new(0.015f, 0.015f, 0.02f);
    private static readonly float3 Ink = new(0.80f, 0.80f, 0.80f);

    public static void Run(GlfwWindow window, Renderer renderer, Input input, Action eachFrame)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Branding", "LOGO_BRUTAL_WHT.png");
        GlTexture logo = default;
        var shown = false;
        try
        {
            logo = Texture.Upload(Texture.Prepare(path, TextureKind.Data), allowCompression: false);
            shown = true;
        }
        catch (Exception e)
        {
            Log.Write($"[WARN] BRUTAL logo not loaded ({e.Message}): the splash shows the notice only.");
        }
        const float logoAspect = 712f / 168f;   // the logo file's width / height

        var start = DateTime.UtcNow;
        while (!window.ShouldClose)
        {
            Glfw.PollEvents();
            input.Update();
            eachFrame();
            var elapsed = (float)(DateTime.UtcNow - start).TotalSeconds;
            if (elapsed >= Seconds)
                break;
            if (elapsed >= MinSeconds
                && (input.WasPressed(GlfwKey.Space) || input.WasPressed(GlfwKey.Enter) || input.WasPressed(GlfwKey.Escape)
                    || input.WasPressed(GlfwMouseButton.Left)))
                break;

            var size = window.FramebufferSize;
            if (size.X > 0 && size.Y > 0)
            {
                renderer.ClearWindow(size);
                renderer.BeginOverlay();
                var aspect = (float)size.X / size.Y;
                var px = 2f / size.Y;
                PcTerminal.Rect(renderer, aspect, new float2(0, 0), new float2(2f * aspect, 2f), Backdrop);

                // The logo: as wide as 60% of the screen's height-units allow, never wider than the window.
                var logoWidth = float.Min(1.2f * aspect, 1.6f);
                var logoHeight = logoWidth / logoAspect;
                if (shown)
                {
                    var model = float4x4.CreateScale(logoWidth, logoHeight, 0.001f)
                        * float4x4.CreateTranslation(0, 0.12f, 0)
                        * float4x4.CreateScale(1f / aspect, 1f, 1f);
                    renderer.DrawImage(model, logo);
                }

                // The copyright notice under it (scaled down to fit narrow windows).
                var pixel = float.Max(1f, float.Round(size.Y / 360f)) * px;
                var width = (Notice.Length * BlockFont.Advance - 1) * pixel;
                if (width > 1.9f * aspect)
                {
                    pixel *= 1.9f * aspect / width;
                    width = (Notice.Length * BlockFont.Advance - 1) * pixel;
                }
                PcTerminal.Text(renderer, aspect, Notice, new float2(-width / 2, 0.12f - logoHeight / 2 - 0.12f), pixel, Ink);
            }
            window.SwapBuffers();
            if (size.X <= 0 || size.Y <= 0)
                Thread.Sleep(16);   // (minimized)
        }

        if (shown)
            DeleteTextures([logo]);
    }
}
