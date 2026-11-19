using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Crosshair + hit marker drawn in normalised device coordinates, and a text HUD in the window title
/// (BRUTAL has no built-in text rendering; swap this for Brutal.ImGui later).
/// </summary>
public sealed class Hud
{
    private static readonly float3 CrosshairColor = new(0.95f, 0.95f, 0.95f);
    private static readonly float3 HitMarkerColor = new(1.0f, 0.25f, 0.2f);
    private static readonly float3 BullseyeColor = new(1.0f, 0.85f, 0.2f);

    private float _hitMarker;
    private bool _lastWasBullseye;
    private string _lastHit = "-";
    private int _totalHits;
    private int _totalScore;

    private float _titleTimer;
    private int _frames;
    private float _fps;

    public void OnTargetHit(TargetHit hit)
    {
        _hitMarker = 0.18f;
        _lastWasBullseye = hit.Points >= 50;
        _totalHits++;
        _totalScore += hit.Points;
        _lastHit = $"{hit.Target.Name}: {hit.Zone} (+{hit.Points})";
    }

    /// <summary>Whether <see cref="Update"/> will set the window's title this frame (so its caller needn't build the texts for it otherwise).</summary>
    public bool TitleDue(float realDt) => _titleTimer + realDt >= 0.25f;

    /// <param name="dt">Time passed, for the hit marker.</param>
    /// <param name="realDt">The frame as it really was (dt is cut short on a long frame, which would hide a low frame rate).</param>
    /// <summary>What the player can hear people calling out (<see cref="NpcBarks"/>), shown at the bottom of the screen.</summary>
    public readonly Subtitles Subtitles = new();

    public void Update(float dt, float realDt, GlfwWindow window, IGun weapon, PortalSystem portals, bool mouseCaptured, PlayerController player, bool wanted, string flashbangs, string grenades = "")
    {
        _hitMarker = float.Max(0, _hitMarker - dt);
        Subtitles.Update(dt);

        _frames++;
        _titleTimer += realDt;
        if (_titleTimer < 0.25f)
            return;

        _fps = _frames / _titleTimer;
        _frames = 0;
        _titleTimer = 0;

        var status = mouseCaptured ? "" : "   [PAUSED]";
        var ammo = weapon.Reloading ? "reloading..." : $"{weapon.AmmoInMag}{(weapon.Chambered ? "+1" : "")} / {weapon.Capacity}";
        var hint = !weapon.Reloading && !weapon.Chambered ? "   [R] reload" : "";
        var health = player.Alive ? $"HP {player.Health:0}" : $"DEAD (back in {float.Max(PlayerController.RespawnDelay - player.DeadFor, 0f):0.0} s)";
        var police = wanted ? "   WANTED" : "";
        window.Title = $"BRUTAL FPS   |   {health}{police}   |   {weapon.Name} [1 pistol, 2 AK, 3 shotgun, 4 UMP, 5 AR, 6 G3, 7 M249]   {weapon.FireMode} [B]   Ammo: {ammo}{hint}   Flashbangs: {flashbangs} [G]{grenades}   Shots: {weapon.ShotsFired}   Splashes: {portals.SplashCount}   |   {_fps:0} fps   [+] respawn{status}";
    }

    private float _blink;

    public void Draw(Renderer renderer, int2 framebufferSize, IGun weapon, float dt, PlayerController player, in Camera view)
    {
        renderer.BeginOverlay();
        _blink += dt;

        var aspect = framebufferSize.Y > 0 ? (float)framebufferSize.X / framebufferSize.Y : 1f;
        var px = 2f / float.Max(framebufferSize.Y, 1); // one pixel in NDC (vertical); horizontal is divided by aspect

        // Classic 4-line crosshair with a gap. Aiming down the sights, the sights take over: it opens out and goes.
        // The gap also shows the spread: it opens up as shots follow each other quickly and closes as it cools.
        const float length = 9f, thickness = 2f;
        var fov = float.DegreesToRadians(weapon.FieldOfView(Camera.Default.FieldOfViewDegrees));
        if (weapon.Scoped && weapon.ScopeKind == ScopeKind.Acog)
            DrawAcog(renderer, aspect, px, weapon);
        else if (weapon.Scoped)
            DrawScope(renderer, aspect, px, weapon);
        else if (weapon.Reticle is { } reticle && weapon.AimBlend > 0.85f)
            DrawReflex(renderer, aspect, px, reticle, (weapon.AimBlend - 0.85f) / 0.15f, fov, weapon.SightDirection);
        var spreadPixels = float.Tan(weapon.Spread) / float.Tan(fov / 2f) * (framebufferSize.Y / 2f);
        var gap = 5f + 14f * weapon.AimBlend + float.Min(spreadPixels, 120f);
        var color = _hitMarker > 0 ? (_lastWasBullseye ? BullseyeColor : HitMarkerColor) : CrosshairColor;
        if (weapon.AimBlend < 0.6f)
        {
            Bar(renderer, aspect, new float2(0, gap + length / 2) * px, new float2(thickness, length) * px, 0, color);
            Bar(renderer, aspect, new float2(0, -(gap + length / 2)) * px, new float2(thickness, length) * px, 0, color);
            Bar(renderer, aspect, new float2(gap + length / 2, 0) * px, new float2(length, thickness) * px, 0, color);
            Bar(renderer, aspect, new float2(-(gap + length / 2), 0) * px, new float2(length, thickness) * px, 0, color);
        }

        // Hit marker: an X that pops when a target is hit.
        if (_hitMarker > 0)
        {
            var grow = 1f + (0.18f - _hitMarker) * 2f;
            var d = 13f * grow;
            for (var i = 0; i < 4; i++)
            {
                var angle = float.Pi / 4 + i * float.Pi / 2;
                var center = new float2(float.Cos(angle), float.Sin(angle)) * d * px;
                Bar(renderer, aspect, center, new float2(10f, 2.5f) * px, angle, color);
            }
        }

        if (weapon.Capacity > 0)   // (the baton has none)
            DrawAmmo(renderer, aspect, px, weapon);
        DrawHealth(renderer, aspect, px, player, view);
        DrawSubtitles(renderer, aspect, px);
    }

    private static readonly float3 SubtitleBackdrop = new(0.02f, 0.02f, 0.025f);

    /// <summary>People's callouts, newest at the bottom, a little above the bottom of the screen, middle.</summary>
    private void DrawSubtitles(Renderer renderer, float aspect, float px)
    {
        var lines = Subtitles.Lines;
        if (lines.Count == 0)
            return;
        var letter = 2f * px;
        var lineHeight = (BlockFont.Height + 4) * letter;
        var y = -0.62f + (lines.Count - 1) * lineHeight;
        foreach (var (text, colour, left) in lines)
        {
            // ("Square" space: y from -1 to 1, x from -aspect to aspect.)
            var width = (text.Length * BlockFont.Advance - 1) * letter;
            var fade = float.Clamp(left / 0.4f, 0f, 1f);
            PcTerminal.Rect(renderer, aspect, new float2(0, y - BlockFont.Height * letter / 2), new float2(width + 8 * letter, lineHeight - letter), SubtitleBackdrop * fade);
            PcTerminal.Text(renderer, aspect, text, new float2(-width / 2, y), letter, colour * fade);
            y -= lineHeight;
        }
    }

    private static readonly float3 ScopeTubeColor = new(0.05f, 0.05f, 0.055f);
    private static readonly float3 ReticleColor = new(0.02f, 0.02f, 0.02f);
    private static readonly float3 ReticleDotColor = new(1.0f, 0.12f, 0.06f);

    /// <summary>The reticle's fine lines: black, but lit in night vision and thermal, where black is lost in the dark of the picture.</summary>
    private static float3 ReticleLines(IGun weapon) => weapon.Vision switch
    {
        ScopeVision.Night => new float3(1.0f, 0.16f, 0.06f),
        ScopeVision.Thermal => new float3(0.25f, 1.0f, 1.0f),
        _ => ReticleColor,
    };

    /// <summary>
    /// Looking through a scope: black all round its circle (the tube's dark rim just inside it) and a duplex reticle:
    /// thick posts from the edge that thin to fine cross-hairs in the middle, with a lit red dot at the centre, and the
    /// scope's power in the black beside the circle.
    /// </summary>
    private static void DrawScope(Renderer renderer, float aspect, float px, IGun weapon)
    {
        const int segments = 96;
        const float radius = 0.94f, rim = 0.035f, outside = 3f;
        // Each segment is a bar tangent to the circle reaching far past the screen's corners; together they leave a
        // 96-sided hole.
        var step = float.Tau / segments;
        for (var i = 0; i < segments; i++)
        {
            var a = i * step;
            var dir = new float2(float.Cos(a), float.Sin(a));
            var wide = 2.1f * (radius + outside) * float.Tan(step / 2);
            Bar(renderer, aspect, dir * (radius + outside / 2), new float2(wide, outside), a - float.Pi / 2, new float3(0, 0, 0));
            var rimWide = 2.1f * radius * float.Tan(step / 2);
            Bar(renderer, aspect, dir * (radius - rim / 2), new float2(rimWide, rim), a - float.Pi / 2, ScopeTubeColor);
        }

        // The duplex: posts from the rim to a third of the way in, then fine lines to just short of the dot.
        var postIn = radius * 0.34f;
        var postLength = radius - rim - postIn;
        var fineLength = postIn - 6f * px;
        var lines = ReticleLines(weapon);
        for (var k = 0; k < 4; k++)
        {
            var a = k * float.Pi / 2;
            var dir = new float2(float.Cos(a), float.Sin(a));
            var horizontal = k % 2 == 0;
            Bar(renderer, aspect, dir * (postIn + postLength / 2),
                horizontal ? new float2(postLength, 6f * px) : new float2(6f * px, postLength), 0, lines);
            Bar(renderer, aspect, dir * (6f * px + fineLength / 2),
                horizontal ? new float2(fineLength, 1.5f * px) : new float2(1.5f * px, fineLength), 0, lines);
        }

        Bar(renderer, aspect, float2.Zero, new float2(4f, 4f) * px, float.Pi / 4, ReticleDotColor);

        DrawScopePower(renderer, aspect, px, weapon, new float2(radius * 0.74f, -radius * 0.74f));
    }

    /// <summary>The scope's power (8x, say) and any vision it's set to, its top middle at <paramref name="at"/>.</summary>
    private static void DrawScopePower(Renderer renderer, float aspect, float px, IGun weapon, float2 at)
    {
        var power = $"{weapon.ScopeZoom:0}x{weapon.Vision switch { ScopeVision.Night => " NV", ScopeVision.Thermal => " THM", _ => "" }}";
        var letter = 3f * px;
        PcTerminal.Text(renderer, aspect, power, at - new float2((power.Length * BlockFont.Advance - 1) * letter / 2, 0), letter, ChevronColor);
    }

    /// <summary>The radius of an ACOG's magnified circle, in half screen heights (and its eyepiece ring's outer edge).</summary>
    public const float AcogRadius = 0.55f, AcogRingOuter = 0.86f;

    private static readonly float3 ChevronColor = new(1.0f, 0.10f, 0.04f);

    /// <summary>
    /// The pixels round an ACOG's magnified circle (x, y, width, height): the scene is drawn again there, zoomed (see
    /// Program). The square's corners are under the eyepiece ring.
    /// </summary>
    public static int4 AcogScissor(int2 framebufferSize)
    {
        var half = (int)(AcogRadius * framebufferSize.Y / 2f) + 4;
        return new int4(framebufferSize.X / 2 - half, framebufferSize.Y / 2 - half, 2 * half, 2 * half);
    }

    /// <summary>
    /// Looking through an ACOG (its magnified picture is already drawn in the circle): the black eyepiece ring round
    /// it, the world seen past that, and the reticle: a lit red chevron, its tip the point of aim, over a fine black
    /// post with the bullet-drop marks across it (one for each hundred metres from 400 to 800, narrower the further, each
    /// where a round at that range really lands), a fine stadia line each side with its range ticks, and the scope's power
    /// on the ring under the circle.
    /// </summary>
    private static void DrawAcog(Renderer renderer, float aspect, float px, IGun weapon)
    {
        renderer.DrawScopeRing(AcogRadius, AcogRingOuter, 0.08f);
        const float r = AcogRadius;
        var lines = ReticleLines(weapon);

        // The chevron: two arms down from its tip. (Small enough to sit above the first mark.)
        const float arm = 0.016f;
        var a = arm / 2 * float.Sqrt(0.5f);
        Bar(renderer, aspect, new float2(a, -a - 1f * px), new float2(arm, 2.5f * px), -float.Pi / 4, ChevronColor);
        Bar(renderer, aspect, new float2(-a, -a - 1f * px), new float2(arm, 2.5f * px), float.Pi / 4, ChevronColor);

        // The bullet-drop post under it, and its marks: an angle below the aim, as far down the magnified picture.
        var perUnit = 1f / float.Tan(float.DegreesToRadians(weapon.ScopeFieldOfView(Camera.Default.FieldOfViewDegrees)) / 2f);
        const int marks = 5;
        var top = -2f * a - 2f * px;
        var bottom = -float.Tan(weapon.HoldUnder(800f)) * perUnit - 0.012f;
        Bar(renderer, aspect, new float2(0, (top + bottom) / 2), new float2(1.6f * px, top - bottom), 0, lines);
        for (var i = 0; i < marks; i++)
        {
            var width = (0.11f - 0.014f * i) * r;
            var y = float.Tan(weapon.HoldUnder(400f + 100f * i)) * perUnit;
            Bar(renderer, aspect, new float2(0, -y), new float2(width, 1.6f * px), 0, lines);
        }

        // The stadia lines either side, with their ticks.
        const float from = 0.24f * r, to = 0.74f * r;
        for (var s = -1; s <= 1; s += 2)
        {
            Bar(renderer, aspect, new float2(s * (from + to) / 2, 0), new float2(to - from, 1.6f * px), 0, lines);
            for (var k = 0; k < 4; k++)
                Bar(renderer, aspect, new float2(s * (from + (to - from) * k / 3f), 0), new float2(1.6f * px, (k == 0 ? 14f : 9f) * px), 0, lines);
        }

        DrawScopePower(renderer, aspect, px, weapon, new float2(0, -r - 0.07f));
    }

    /// <summary>
    /// Looking through a reflex sight at 1x: its lit reticle (a red dot, or a holographic ring round a dot), seen at infinity
    /// along the sight's own axis (<paramref name="axis"/>, camera space): in the middle of its window (the gun's model) even
    /// while the gun lags the mouse or kicks, and in the middle of the view once it settles. It brightens as the gun comes up.
    /// </summary>
    private static void DrawReflex(Renderer renderer, float aspect, float px, ScopeKind reticle, float shown, float fov, float3? axis)
    {
        var at = axis is { Z: < -0.1f } d ? new float2(d.X, d.Y) / -d.Z / float.Tan(fov / 2f) : float2.Zero;
        var glow = ReticleDotColor * (0.35f * shown);
        var dot = ReticleDotColor * shown;
        // A round dot: two squares turned against each other, in a fainter, larger halo.
        for (var k = 0; k < 2; k++)
        {
            Bar(renderer, aspect, at, new float2(7f, 7f) * px, k * float.Pi / 4, glow);
            Bar(renderer, aspect, at, new float2(4f, 4f) * px, k * float.Pi / 4, dot);
        }
        if (reticle != ScopeKind.Holo)
            return;
        // The holographic ring: 68 MOA across, in short arcs.
        const int segments = 48;
        var radius = float.Tan(68f / 60f * float.Pi / 180f / 2f) / float.Tan(fov / 2f);
        for (var i = 0; i < segments; i++)
        {
            var a = i * float.Tau / segments;
            var wide = 2.1f * radius * float.Tan(float.Pi / segments);
            Bar(renderer, aspect, at + new float2(float.Cos(a), float.Sin(a)) * radius, new float2(wide, 2f * px), a - float.Pi / 2, dot);
        }
        // ...and a short tick across it at the top, bottom and either side.
        for (var k = 0; k < 4; k++)
        {
            var dir = new float2(float.Cos(k * float.Pi / 2), float.Sin(k * float.Pi / 2));
            Bar(renderer, aspect, at + dir * radius, k % 2 == 0 ? new float2(radius * 0.3f, 2f * px) : new float2(2f * px, radius * 0.3f), 0, dot);
        }
    }

    private static readonly float3 HealthColor = new(0.85f, 0.88f, 0.86f);
    private static readonly float3 HealthLowColor = new(0.95f, 0.2f, 0.12f);
    private static readonly float3 HurtColor = new(0.75f, 0.03f, 0.02f);

    /// <summary>
    /// Bottom left: a health bar (red when low). Being hit flashes a red band on the edge of the screen the shot came
    /// from; dead, the whole screen gets a heavy red frame.
    /// </summary>
    private void DrawHealth(Renderer renderer, float aspect, float px, PlayerController player, in Camera view)
    {
        const float margin = 28f, width = 180f, height = 9f;
        var left = -aspect + margin * px;
        var y = -1f + (margin + height / 2) * px;
        var fraction = float.Clamp(player.Health / PlayerController.MaxHealth, 0f, 1f);
        var low = fraction < 0.35f;
        var color = low && (int)(_blink * 3f) % 2 == 0 ? HealthLowColor : (low ? HealthLowColor * 0.75f : HealthColor);
        Bar(renderer, aspect, new float2(left + width / 2 * px, y), new float2(width + 4, height + 4) * px, 0, SpentColor);
        if (fraction > 0)
            Bar(renderer, aspect, new float2(left + width * fraction / 2 * px, y), new float2(width * fraction, height) * px, 0, color);

        if (!player.Alive)
        {
            var t = float.Min(player.DeadFor / 0.6f, 1f) * 110f;
            Edge(renderer, aspect, px, 0, t, HurtColor * 0.8f);
            Edge(renderer, aspect, px, 1, t, HurtColor * 0.8f);
            Edge(renderer, aspect, px, 2, t * 0.7f, HurtColor * 0.8f);
            Edge(renderer, aspect, px, 3, t * 0.7f, HurtColor * 0.8f);
            return;
        }

        if (player.HurtFlash <= 0)
            return;
        // Which side it came from, in the view: right / left / ahead / behind.
        var to = player.HurtFrom - view.Position;
        var forward = view.Forward;
        var right = float3.Normalize(float3.Cross(forward, float3.UnitY));
        var sideways = float3.Dot(to, right);
        var ahead = float3.Dot(to, new float3(forward.X, 0, forward.Z));
        var thickness = 10f + 50f * player.HurtFlash;
        if (float.Abs(sideways) > float.Abs(ahead))
            Edge(renderer, aspect, px, sideways > 0 ? 1 : 0, thickness, HurtColor);
        else
            Edge(renderer, aspect, px, ahead > 0 ? 2 : 3, thickness, HurtColor);
    }

    /// <summary>A band along one edge of the screen (0 left, 1 right, 2 top, 3 bottom), <paramref name="thickness"/> pixels deep.</summary>
    private static void Edge(Renderer renderer, float aspect, float px, int side, float thickness, float3 color)
    {
        var t = thickness * px;
        switch (side)
        {
            case 0: Bar(renderer, aspect, new float2(-aspect + t / 2, 0), new float2(t, 2f), 0, color); break;
            case 1: Bar(renderer, aspect, new float2(aspect - t / 2, 0), new float2(t, 2f), 0, color); break;
            case 2: Bar(renderer, aspect, new float2(0, 1f - t / 2), new float2(2f * aspect, t), 0, color); break;
            default: Bar(renderer, aspect, new float2(0, -1f + t / 2), new float2(2f * aspect, t), 0, color); break;
        }
    }

    private static readonly float3 RoundColor = new(0.86f, 0.68f, 0.32f);
    private static readonly float3 LowColor = new(1.0f, 0.45f, 0.15f);
    private static readonly float3 SpentColor = new(0.16f, 0.16f, 0.17f);
    private static readonly float3 EmptyColor = new(0.95f, 0.15f, 0.12f);

    /// <summary>
    /// Bottom right: a pip for each round in the magazine and a bigger one for the chamber, a bar while reloading,
    /// and the pips flashing red when the gun is empty (press R).
    /// </summary>
    private void DrawAmmo(Renderer renderer, float aspect, float px, IGun weapon)
    {
        const float margin = 28f;
        var big = weapon.Capacity > 20;     // a rifle magazine: slimmer pips so they fit
        var perPip = weapon.Capacity > 60 ? 5 : 1;   // a belt: a pip for every five rounds
        var pipWidth = big ? 3f : 4f;
        var pipHeight = big ? 13f : 15f;
        var gap = big ? 2f : 3f;
        var right = aspect - margin * px;
        var y = -1f + (margin + pipHeight / 2) * px;
        var empty = !weapon.Chambered && weapon.AmmoInMag == 0 && !weapon.Reloading;
        var flash = empty && (int)(_blink * 3f) % 2 == 0;
        var low = weapon.AmmoInMag <= (perPip > 1 ? 25 : big ? 8 : 5);

        // The chamber, rightmost: one round on semi-automatic, three on fully automatic.
        var chamber = weapon.Chambered ? RoundColor : (flash ? EmptyColor : SpentColor);
        var chamberPips = weapon.FireMode == "AUTO" ? 3 : 1;
        var x = right;
        for (var i = 0; i < chamberPips; i++)
        {
            Bar(renderer, aspect, new float2(x - pipWidth * px, y), new float2(pipWidth * 2, pipHeight + 4) * px, 0, chamber);
            x -= (pipWidth * 2 + gap) * px;
        }

        x -= gap * 2 * px;
        for (var i = 0; i < weapon.Capacity / perPip; i++)
        {
            var filled = !weapon.Reloading && i * perPip < weapon.AmmoInMag;
            var color = filled ? (low ? LowColor : RoundColor) : (flash ? EmptyColor : SpentColor);
            Bar(renderer, aspect, new float2(x - pipWidth / 2 * px, y), new float2(pipWidth, pipHeight) * px, 0, color);
            x -= (pipWidth + gap) * px;
        }

        if (weapon.Reloading)
        {
            var width = right - x;
            var progress = weapon.ReloadProgress;
            var barY = y - (pipHeight / 2 + 7f) * px;
            Bar(renderer, aspect, new float2(x + width / 2, barY), new float2(width / px, 3f) * px, 0, SpentColor);
            Bar(renderer, aspect, new float2(x + width * progress / 2, barY), new float2(width * progress / px, 3f) * px, 0, RoundColor);
        }
    }

    private static void Bar(Renderer renderer, float aspect, float2 center, float2 size, float angle, float3 color)
    {
        // Build in "square" space (y units), rotate, then squash X by the aspect ratio.
        var model = float4x4.CreateScale(size.X, size.Y, 0.001f)
            * float4x4.CreateRotationZ(angle)
            * float4x4.CreateTranslation(center.X, center.Y, 0)
            * float4x4.CreateScale(1f / aspect, 1f, 1f);
        renderer.DrawCube(model, color, ShadeMode.Unlit);
    }
}
