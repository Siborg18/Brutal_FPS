using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// What the player wears over their eyes, picked on a wheel: hold T and the wheel comes up (the view stops turning:
/// the mouse moves a pointer on the wheel instead), move it over an option and it lights up, let go of T and it's put
/// on. Nothing (the empty slot, the default), night vision goggles or thermal goggles. Letting go over the one already
/// on, or over the middle, changes nothing.
/// <para>
/// The goggles show the whole view as the scopes' visions do (<see cref="ScopeVision"/>, <see cref="Renderer.SetVision"/>),
/// the hands too; a scope with its own vision switched on shows that instead. They darken the edges of the view a little.
/// </para>
/// </summary>
public sealed class Goggles
{
    private static readonly (ScopeVision Vision, string Name, float Angle)[] Options =
    [
        (ScopeVision.Off, "NONE", float.Pi / 2f),                   // top
        (ScopeVision.Night, "NIGHT VISION", -float.Pi / 6f),        // lower right
        (ScopeVision.Thermal, "THERMAL", float.Pi * 7f / 6f),       // lower left
    ];

    private const float HubRadius = 0.13f;    // square units (half screen heights): the middle, where nothing's picked
    private const float WheelRadius = 0.6f;   // the wheel's outer edge
    private const float PointerReach = 0.52f; // how far out the pointer goes
    private const float Sensitivity = 1.2f;   // the pointer moves this many screen pixels per pixel of mouse

    private static readonly float3 Spoke = new(0.75f, 0.76f, 0.78f);
    private static readonly float3 Rim = new(0.85f, 0.86f, 0.88f);
    private static readonly float3 Label = new(0.92f, 0.92f, 0.9f);
    private static readonly float3 LabelDim = new(0.55f, 0.56f, 0.58f);
    private static readonly float3 Lit = new(1.0f, 0.68f, 0.18f);
    private static readonly float3 NightGreen = new(0.35f, 1.0f, 0.4f);
    private static readonly float3 ThermalOrange = new(1.0f, 0.45f, 0.12f);

    private float2 _pointer;   // square units from the middle
    private int _hover = -1;   // the option under the pointer (-1: the middle)

    /// <summary>What they're wearing.</summary>
    public ScopeVision Vision { get; private set; } = Environment.GetEnvironmentVariable("BRUTALFPS_GOGGLES") switch
    {
        "night" => ScopeVision.Night,      // debug: start with them on (for screenshots)
        "thermal" => ScopeVision.Thermal,
        _ => ScopeVision.Off,
    };

    /// <summary>The wheel is up (T held).</summary>
    public bool IsOpen { get; private set; }

    /// <param name="canOpen">The player is free to (playing, alive, not at a screen or in a cutscene).</param>
    /// <param name="pixel">One screen pixel, in square units (2 / the framebuffer's height).</param>
    /// <summary>Whether they're worn at all: a raid's loadout can leave them behind (Game/RaidLoadout.cs).</summary>
    public bool Available
    {
        get => _available;
        set
        {
            _available = value;
            if (!value)
                (IsOpen, Vision) = (false, ScopeVision.Off);
        }
    }
    private bool _available = true;

    public void Update(Input input, bool canOpen, float pixel)
    {
        canOpen &= _available;
        var held = canOpen && input.IsDown(GlfwKey.T);
        if (!IsOpen)
        {
            if (!held)
                return;
            IsOpen = true;
            _pointer = float2.Zero;
            _hover = -1;
            return;
        }

        if (!held)
        {
            // Let go: put on what's lit (if anything, and it's not what's on already).
            IsOpen = false;
            if (canOpen && _hover >= 0 && Options[_hover].Vision != Vision)
            {
                Vision = Options[_hover].Vision;
                Log.Write($"[GOGGLES] {Options[_hover].Name.ToLowerInvariant()}");
            }

            return;
        }

        // The mouse moves the pointer (screen y is down, square y up).
        _pointer += new float2(input.MouseDelta.X, -input.MouseDelta.Y) * (pixel * Sensitivity);
        var len = float.Sqrt(_pointer.X * _pointer.X + _pointer.Y * _pointer.Y);
        if (len > PointerReach)
            _pointer *= PointerReach / len;
        _hover = len < HubRadius ? -1 : Sector(float.Atan2(_pointer.Y, _pointer.X));
    }

    /// <summary>The option whose slice of the wheel <paramref name="angle"/> (rad, anticlockwise from the right) falls in.</summary>
    private static int Sector(float angle)
    {
        var best = 0;
        var bestOff = float.MaxValue;
        for (var i = 0; i < Options.Length; i++)
        {
            var off = float.Abs(Wrap(angle - Options[i].Angle));
            if (off < bestOff)
            {
                bestOff = off;
                best = i;
            }
        }

        return best;
    }

    private static float Wrap(float a)
    {
        while (a > float.Pi) a -= float.Tau;
        while (a < -float.Pi) a += float.Tau;
        return a;
    }

    // ------------------------------------------------------------------------------------------------ drawing

    /// <summary>The edges of the view darkened a little while they're on (call it after the world, before the HUD; not through a scope).</summary>
    public void DrawVignette(Renderer renderer)
    {
        if (Vision == ScopeVision.Off)
            return;
        renderer.BeginOverlay();
        renderer.DrawScopeRing(1.9f, 3.5f, 0.5f);   // (the sides darkening, the housing just showing in the corners)
    }

    /// <summary>The wheel, while T is held (call it last, over the HUD).</summary>
    public void Draw(Renderer renderer, int2 framebufferSize)
    {
        if (!IsOpen)
            return;
        var aspect = framebufferSize.Y > 0 ? (float)framebufferSize.X / framebufferSize.Y : 1f;
        var px = 2f / float.Max(framebufferSize.Y, 1);
        renderer.DrawFade(0.45f);   // (it ends in the overlay pass)

        // The slice under the pointer, filled in: a fan of bars out from the hub.
        if (_hover >= 0)
        {
            var mid = Options[_hover].Angle;
            const int fan = 40;
            for (var i = 0; i <= fan; i++)
            {
                var a = mid - float.Pi / 3f + float.Tau / 3f * i / fan;
                var dir = new float2(float.Cos(a), float.Sin(a));
                var length = WheelRadius - HubRadius;
                PcTerminal.Rect(renderer, aspect, dir * (HubRadius + length / 2f), new float2(length, (WheelRadius * float.Tau / 3f / fan) * 1.6f), Lit * 0.22f, a);
            }
        }

        // The spokes between the slices, the hub's ring and the outer rim.
        for (var i = 0; i < Options.Length; i++)
        {
            var a = Options[i].Angle + float.Pi / 3f;
            var dir = new float2(float.Cos(a), float.Sin(a));
            var length = WheelRadius - HubRadius - 0.02f;
            PcTerminal.Rect(renderer, aspect, dir * (HubRadius + 0.02f + length / 2f), new float2(length, 2f * px), Spoke, a);
        }

        Ring(renderer, aspect, HubRadius, 3f * px, Rim, 64);
        Ring(renderer, aspect, WheelRadius, 2f * px, Spoke * 0.8f, 96);
        if (_hover >= 0)
            Arc(renderer, aspect, WheelRadius, 5f * px, Lit, Options[_hover].Angle, float.Pi / 3f, 40);

        // Each option: its picture and its name, lit if it's under the pointer, marked if it's on.
        var unit = 3f * px;
        for (var i = 0; i < Options.Length; i++)
        {
            var (vision, name, angle) = Options[i];
            var at = new float2(float.Cos(angle), float.Sin(angle)) * ((HubRadius + WheelRadius) / 2f - 0.01f);
            var lit = i == _hover;
            var worn = vision == Vision;
            var color = lit ? Lit : worn ? Label : LabelDim;
            DrawPicture(renderer, aspect, at + new float2(0, 0.045f), vision, lit, px);
            var width = GunLocker.TextWidth(name, unit);
            PcTerminal.Text(renderer, aspect, name, at + new float2(-width / 2f, -0.025f), unit, color);
            if (worn)
            {
                const string on = "ON";
                var small = 2f * px;
                PcTerminal.Text(renderer, aspect, on, at + new float2(-GunLocker.TextWidth(on, small) / 2f, -0.025f - (BlockFont.Height + 4) * unit), small, Lit * 0.8f);
            }
        }

        // The middle: what's on, and the pointer.
        PcTerminal.Rect(renderer, aspect, float2.Zero, new float2(HubRadius * 1.38f, HubRadius * 1.38f), new float3(0.06f, 0.065f, 0.07f), float.Pi / 4f);
        PcTerminal.Rect(renderer, aspect, float2.Zero, new float2(HubRadius * 1.38f, HubRadius * 1.38f), new float3(0.06f, 0.065f, 0.07f));
        const string title = "GOGGLES";
        var tiny = 2f * px;
        PcTerminal.Text(renderer, aspect, title, new float2(-GunLocker.TextWidth(title, tiny) / 2f, BlockFont.Height * tiny / 2f), tiny, _hover < 0 ? Label : LabelDim);
        PcTerminal.Rect(renderer, aspect, _pointer, new float2(7f * px, 7f * px), Label, float.Pi / 4f);
        PcTerminal.Rect(renderer, aspect, _pointer, new float2(4f * px, 4f * px), Lit, float.Pi / 4f);
    }

    /// <summary>A little picture of the option: an empty dashed slot, or a pair of goggles in their vision's colour.</summary>
    private static void DrawPicture(Renderer renderer, float aspect, float2 at, ScopeVision vision, bool lit, float px)
    {
        if (vision == ScopeVision.Off)
        {
            // An empty slot: a dashed square.
            var half = 0.032f;
            var color = lit ? Lit : LabelDim;
            var dash = half * 0.45f;
            foreach (var edge in (ReadOnlySpan<float>)[-1f, 1f])
            {
                foreach (var along in (ReadOnlySpan<float>)[-0.66f, 0f, 0.66f])
                {
                    PcTerminal.Rect(renderer, aspect, at + new float2(along * half, edge * half), new float2(dash, 3f * px), color);   // top and bottom
                    PcTerminal.Rect(renderer, aspect, at + new float2(edge * half, along * half), new float2(3f * px, dash), color);   // the sides
                }
            }

            return;
        }

        // Goggles: two lenses on a bridge, with a strap.
        var tint = vision == ScopeVision.Night ? NightGreen : ThermalOrange;
        var frame = lit ? Lit : Label * 0.85f;
        const float lens = 0.022f, apart = 0.03f;
        PcTerminal.Rect(renderer, aspect, at, new float2(apart * 3.1f, 3f * px), frame * 0.7f);   // the strap
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var c = at + new float2(side * apart, 0);
            PcTerminal.Rect(renderer, aspect, c, new float2(lens * 1.7f, lens * 1.7f), tint * (lit ? 0.9f : 0.6f), float.Pi / 4f);
            PcTerminal.Rect(renderer, aspect, c, new float2(lens * 1.7f, lens * 1.7f), tint * (lit ? 0.9f : 0.6f));
            Ring(renderer, aspect, lens, 3f * px, frame, 20, c);
        }

        PcTerminal.Rect(renderer, aspect, at, new float2(apart * 0.6f, 4f * px), frame);   // the bridge
    }

    private static void Ring(Renderer renderer, float aspect, float radius, float thickness, float3 color, int segments, float2 center = default) =>
        Arc(renderer, aspect, radius, thickness, color, 0f, float.Pi, segments, center);

    /// <summary>Bars round a circle, from <paramref name="mid"/> - <paramref name="half"/> to <paramref name="mid"/> + <paramref name="half"/> (rad).</summary>
    private static void Arc(Renderer renderer, float aspect, float radius, float thickness, float3 color, float mid, float half, int segments, float2 center = default)
    {
        var step = 2f * half / segments;
        var length = radius * step * 1.08f;
        for (var i = 0; i < segments; i++)
        {
            var a = mid - half + (i + 0.5f) * step;
            var dir = new float2(float.Cos(a), float.Sin(a));
            PcTerminal.Rect(renderer, aspect, center + dir * radius, new float2(thickness, length), color, a);
        }
    }
}
