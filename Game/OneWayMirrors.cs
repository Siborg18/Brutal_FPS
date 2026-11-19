using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The one-way mirrors between the police vault and its observation rooms (<see cref="Level.VaultMirrors"/>). From the
/// vault they're mirrors: each frame one is on screen, the scene is drawn again reflected in its plane (the camera's
/// view-projection with a reflection in front of it, clipped to the vault's side) into a texture the glass shows in
/// screen space, so it lines up exactly, with the player's body in it (the camera's double). From the observation room
/// the glass draws nothing: a clear window onto the vault.
/// </summary>
public sealed class OneWayMirrors(IReadOnlyList<Level.MirrorPane> panes) : IDisposable
{
    private static readonly float3 Tint = new(1f, 1f, 1f);
    private static readonly float3 StandIn = new(0.5f, 0.52f, 0.55f);   // the glass in other views (portals, CCTV)

    private readonly (Level.MirrorPane Pane, RenderTarget View)[] _mirrors = [.. panes.Select(p => (p, new RenderTarget()))];
    private readonly bool[] _ready = new bool[panes.Count];

    /// <summary>Renders the reflection of each mirror that's on screen in front of the camera (before the main view).</summary>
    /// <param name="drawScene">Draws the scene seen from the mirror (the zones it's in).</param>
    public void Render(Renderer renderer, in Camera camera, int2 size, Action<Zones> drawScene)
    {
        var aspect = (float)size.X / size.Y;
        var projection = camera.Projection(aspect);
        var viewProjection = camera.View * projection;
        for (var i = 0; i < _mirrors.Length; i++)
        {
            var (pane, view) = _mirrors[i];
            _ready[i] = false;
            if (float3.Dot(camera.Position - pane.Center, pane.Normal) < 0.02f || float3.Distance(camera.Position, pane.Center) > 40f)
                continue;
            var rect = ScreenRect(viewProjection, pane, size);
            if (rect.Z <= 0 || rect.W <= 0)
                continue;

            var reflect = Reflection(pane.Center, pane.Normal);
            view.EnsureSize(size);
            var clip = new float4(pane.Normal, -float3.Dot(pane.Normal, pane.Center) - 0.001f);
            renderer.BeginView(view, size, reflect * viewProjection, float3.Transform(camera.Position, reflect), clip, rect);
            renderer.SetMirrored(true);
            drawScene(Level.ZonesSeenFrom(pane.Center));
            renderer.SetMirrored(false);
            _ready[i] = true;
        }
    }

    /// <summary>
    /// Draws the glass: in the player's own view a mirror rendered this frame; in other views (through a portal, on
    /// CCTV) plain silvered glass. From behind (the observation rooms), nothing.
    /// </summary>
    public void Draw(Renderer renderer, Zones zones, int depth)
    {
        if ((zones & Zones.Police) == 0)
            return;
        for (var i = 0; i < _mirrors.Length; i++)
        {
            var (pane, view) = _mirrors[i];
            if (float3.Dot(renderer.CameraPosition - pane.Center, pane.Normal) <= 0f)
                continue;
            if (depth == 0 && _ready[i])
                renderer.DrawMirror(Model(pane), view, Tint);
            else
                renderer.DrawCube(Model(pane), StandIn, ShadeMode.Lit, Material.Glass);
        }
    }

    /// <summary>The unit cube onto the glass: a thin slab, its +z face facing out of the mirror.</summary>
    private static float4x4 Model(in Level.MirrorPane pane)
    {
        var n = pane.Normal;
        var right = float3.Normalize(float3.Cross(float3.UnitY, n));
        return float4x4.CreateScale(pane.Size.X, pane.Size.Y, 0.002f)
            * new float4x4(right.X, right.Y, right.Z, 0, 0, 1, 0, 0, n.X, n.Y, n.Z, 0, pane.Center.X, pane.Center.Y, pane.Center.Z, 1);
    }

    /// <summary>Reflects points in the plane through <paramref name="point"/> with normal <paramref name="n"/>.</summary>
    private static float4x4 Reflection(float3 point, float3 n)
    {
        var d = float3.Dot(point, n) * 2f;
        return new float4x4(
            1 - 2 * n.X * n.X, -2 * n.X * n.Y, -2 * n.X * n.Z, 0,
            -2 * n.Y * n.X, 1 - 2 * n.Y * n.Y, -2 * n.Y * n.Z, 0,
            -2 * n.Z * n.X, -2 * n.Z * n.Y, 1 - 2 * n.Z * n.Z, 0,
            d * n.X, d * n.Y, d * n.Z, 1);
    }

    /// <summary>The pixels the glass covers on screen (x, y, width, height), the whole screen if it reaches behind the camera.</summary>
    private static int4 ScreenRect(in float4x4 viewProjection, in Level.MirrorPane pane, int2 size)
    {
        var model = Model(pane);
        float2 min = new(float.MaxValue), max = new(float.MinValue);
        for (var c = 0; c < 4; c++)
        {
            var corner = float4.Transform(new float4((c & 1) - 0.5f, (c >> 1) - 0.5f, 0.5f, 1f), model * viewProjection);
            if (corner.W <= 0.01f)
                return new int4(0, 0, size.X, size.Y);
            var ndc = new float2(corner.X, corner.Y) / corner.W;
            min = float2.Min(min, ndc);
            max = float2.Max(max, ndc);
        }
        min = float2.Max(min, new float2(-1f));
        max = float2.Min(max, new float2(1f));
        if (min.X >= max.X || min.Y >= max.Y)
            return default;
        var x0 = (int)((min.X * 0.5f + 0.5f) * size.X) - 2;
        var y0 = (int)((min.Y * 0.5f + 0.5f) * size.Y) - 2;
        var x1 = (int)((max.X * 0.5f + 0.5f) * size.X) + 2;
        var y1 = (int)((max.Y * 0.5f + 0.5f) * size.Y) + 2;
        x0 = int.Max(x0, 0);
        y0 = int.Max(y0, 0);
        return new int4(x0, y0, int.Min(x1, size.X) - x0, int.Min(y1, size.Y) - y0);
    }

    public void Dispose()
    {
        foreach (var (_, view) in _mirrors)
            view.Dispose();
    }
}
