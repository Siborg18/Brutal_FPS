using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The tech-room PC next to the navy portal. Standing in front of its desk (<see cref="Level.PolicePcZone"/>) shows a
/// "[E] Use PC" prompt; E opens a terminal window over the view (in the style of Windows Terminal: a dark tab bar,
/// a black console in a pale monospaced pixel font, a blinking cursor) and E again closes it. Walk away from the desk
/// and it closes by itself. While it's open the gun doesn't fire. BRUTALFPS_TERMINAL=open keeps it open anywhere (for
/// screenshots; BRUTALFPS_TERMINAL=vault, the vault's). The vault's PC (<see cref="Level.PoliceVaultPcZone"/>) is another of
/// these, wired into the gold portal.
/// </summary>
public sealed class PcTerminal
{
    /// <summary>The console's opening text, a line per entry (the index listing follows it).</summary>
    public static readonly string[] Lines = ["Portal connection protocol:"];

    private const string Prompt = "C:\\PORTAL>";
    private const string Command = " portal --list";
    private const float TypeDelay = 0.5f, TypeRate = 16f, LineRate = 30f;   // s before typing; characters / s; listing lines / s

    private readonly PortalSystem _system;
    private readonly IReadOnlyList<Portal> _portals;
    private string[] _listing = [];
    private float _session;   // seconds since the terminal was opened (drives the typing and the listing)
    private int _selected = -1;   // the portal picked as the HQ portal's destination (index into _portals)
    private readonly List<string> _log = [];   // what connecting printed

    private const int HeaderRows = 5;   // listing lines before the first portal's row

    /// <param name="system">The game's portals: the index lists them all (with their addresses, locations and links),
    /// and the HQ portal can be connected to any of them from here.</param>
    public PcTerminal(PortalSystem system) : this(system, system.Hq, Level.PolicePcZone, "HQ")
    {
    }

    /// <param name="system">The game's portals (all listed).</param>
    /// <param name="home">The portal this PC is wired into: the one it connects.</param>
    /// <param name="zone">Where the player stands to use it.</param>
    /// <param name="label">What the listing and its messages call the home portal ("HQ", "VAULT").</param>
    /// <param name="autoConnect">If set, there's no choosing: once the listing is up the player types this portal's address and it connects, and the window closes.</param>
    public PcTerminal(PortalSystem system, Portal home, Aabb zone, string label, Portal? autoConnect = null)
    {
        _system = system;
        _portals = [.. system.Portals.Where(static p => !p.Dark)];
        _home = home;
        _zone = zone;
        _label = label;
        _auto = autoConnect;
        _autoCommand = autoConnect is null ? "" : $" portal --connect {PortalIndex.Prefix}{PortalIndex.For(autoConnect.Name).Shown}";
        _forced = Environment.GetEnvironmentVariable("BRUTALFPS_TERMINAL") is { } forced
                  && (forced == "open" && label == "HQ" || forced.Equals(label, StringComparison.OrdinalIgnoreCase));
    }

    private readonly Portal _home;
    private readonly Aabb _zone;
    private readonly string _label;
    private readonly Portal? _auto;
    private readonly string _autoCommand;
    private bool _autoDone;
    private float _closeAt;

    private const float AutoPause = 0.6f, AutoSettle = 0.25f, AutoCloseAfter = 1.8f;   // s: before typing it, before it goes through, reading the result

    /// <summary>Seconds into a session when the address starts being typed.</summary>
    private float AutoStart => ListingDone + AutoPause;

    /// <summary>What's been typed of the address so far, at the prompt.</summary>
    private string AutoTyped => _auto is null || _autoDone
        ? ""
        : _autoCommand[..(int)float.Clamp((_session - AutoStart) * TypeRate, 0, _autoCommand.Length)];

    /// <summary>
    /// The index listing: a header, a row per portal (number, colour, address, the number it's linked to or "--" if
    /// it's offline, location), a summary and how to use it. Rebuilt every frame, so it shows the links as they are.
    /// </summary>
    private string[] BuildListing()
    {
        var rows = new List<string>
        {
            "",
            "PORTAL INDEX - portal network, prefix " + PortalIndex.Prefix,
            "",
            "ID  PORTAL   ADDRESS                    LINK  LOCATION",
            "--  -------  -------------------------  ----  ----------------------------",
        };
        var hidden = 0;
        var offline = 0;
        for (var i = 0; i < _portals.Count; i++)
        {
            var p = _portals[i];
            var entry = PortalIndex.For(p.Name);
            if (PortalIndex.IsHidden(entry))
                hidden++;
            if (!p.Online)
                offline++;
            var link = p.Linked is { } l ? $"{IndexOf(l) + 1:00}" : "--";
            var here = p == _home ? $"  < {_label}" : "";
            rows.Add($"{i + 1:00}  {PortalIndex.Short(p.Name),-7}  {PortalIndex.Prefix + entry.Shown,-25}  {link}    {entry.Location}{here}");
        }
        rows.Add("");
        rows.Add($"{_portals.Count} portals, {(_portals.Count - offline) / 2} links, {offline} offline. {hidden} relayed via Tor, addresses hidden.");
        rows.Add(_auto is null ? "UP and DOWN (or the mouse) choose a destination. ENTER (or a click) connects the " + _label + " portal to it." : "Emergency line: 112.");
        return [.. rows];
    }

    private int IndexOf(Portal p)
    {
        for (var j = 0; j < _portals.Count; j++)
            if (_portals[j] == p)
                return j;
        return -1;
    }

    /// <summary>Seconds into a session when the listing has finished printing (and the keys start working).</summary>
    private float ListingDone => TypeDelay + Command.Length / TypeRate + 0.3f + _listing.Length / LineRate;

    /// <summary>Moves the selection by <paramref name="step"/>, skipping the HQ portal itself.</summary>
    private void MoveSelection(int step)
    {
        var hq = IndexOf(_home);
        var i = _selected;
        do
            i = (i + step + _portals.Count) % _portals.Count;
        while (i == hq);
        _selected = i;
    }

    /// <summary>Points the HQ portal at the selected portal, cutting off whatever either was linked to.</summary>
    private void ConnectSelected()
    {
        var hq = _home;
        var target = _portals[_selected];
        var id = $"{_selected + 1:00}";
        _log.Clear();
        _log.Add(_auto is null ? $"{Prompt} portal --connect {PortalIndex.Prefix}{PortalIndex.For(hq.Name).Shown} {id}" : Prompt + _autoCommand);
        if (hq.Linked == target)
        {
            _log.Add($"Already connected to {id} {PortalIndex.Short(target.Name)}.");
            return;
        }
        var previous = hq.Linked;
        var dropped = _system.Connect(hq, target);
        _log.Add($"Linking {_label} to {id} {PortalIndex.Short(target.Name)}, {PortalIndex.For(target.Name).Location} ... connected.");
        foreach (var d in dropped)
        {
            var why = d == previous ? $"released by {_label}" : $"overridden: its link to {id} {PortalIndex.Short(target.Name)} was taken";
            _log.Add($"{IndexOf(d) + 1:00} {PortalIndex.Short(d.Name)} offline - {why}.");
        }
    }

    private const float OpenTime = 0.14f;

    private static readonly float3 Backdrop = new(0.02f, 0.02f, 0.025f);
    private static readonly float3 TitleBar = new(0.13f, 0.13f, 0.15f);
    private static readonly float3 Tab = new(0.05f, 0.05f, 0.055f);
    private static readonly float3 Edge = new(0.32f, 0.36f, 0.45f);
    private static readonly float3 Console = new(0.047f, 0.047f, 0.05f);
    private static readonly float3 Ink = new(0.80f, 0.80f, 0.80f);
    private static readonly float3 Dim = new(0.55f, 0.56f, 0.6f);
    private static readonly float3 Accent = new(0.35f, 0.55f, 1.0f);

    private readonly bool _forced;   // BRUTALFPS_TERMINAL: "open" holds the HQ's open, "vault" the vault's
    private float _open;      // 0 closed .. 1 fully open (the window scales up as it opens)
    private float _time;

    public bool IsOpen { get; private set; }

    /// <summary>True while the player stands where they can use the PC.</summary>
    public bool InReach { get; private set; }

    /// <summary>Closes the terminal (Esc while it's open: the game doesn't pause). False if it wasn't open (or is held open).</summary>
    public bool Close()
    {
        if (!IsOpen || _forced)
            return false;
        IsOpen = false;
        Log.Write("[PC] Terminal closed");
        return true;
    }

    public void Update(float dt, Input input, float3 playerPosition, bool playing)
    {
        _time += dt;
        var zone = _zone;
        InReach = playerPosition.X >= zone.Min.X && playerPosition.X <= zone.Max.X && playerPosition.Y >= zone.Min.Y
                  && playerPosition.Y <= zone.Max.Y && playerPosition.Z >= zone.Min.Z && playerPosition.Z <= zone.Max.Z;
        var wasOpen = IsOpen;
        if (_forced)
            IsOpen = true;
        else if (!InReach)
            IsOpen = false;          // walked away: the screen closes
        else if (playing && input.WasPressed(GlfwKey.E))
        {
            IsOpen = !IsOpen;
            Log.Write(IsOpen ? "[PC] Terminal opened" : "[PC] Terminal closed");
        }

        _open = IsOpen ? float.Min(1f, _open + dt / OpenTime) : 0f;
        if (IsOpen && !wasOpen)
        {
            _session = 0f;
            _log.Clear();
            _listing = BuildListing();
            _autoDone = false;
            _selected = _auto is not null ? IndexOf(_auto) : _home.Linked is { } partner ? IndexOf(partner) : -1;
            if (_selected < 0)
                MoveSelection(1);
        }
        if (!IsOpen)
            return;
        _session += dt;
        _listing = BuildListing();
        if (_auto is not null)
        {
            if (playing && !_autoDone && _session >= AutoStart + _autoCommand.Length / TypeRate + AutoSettle)
            {
                ConnectSelected();
                _autoDone = true;
                _closeAt = _session + AutoCloseAfter;
                Log.Write($"[PC] Typed{_autoCommand}");
            }
            if (_autoDone && _session >= _closeAt)
                Close();
            return;
        }
        if (playing && input.UiCursor && input.WasPressed(GlfwMouseButton.Left) && Inside(input.CursorSquare, _closeBox))
        {
            Close();   // (the window's cross)
            return;
        }
        if (_session >= ListingDone && playing)
        {
            if (input.WasPressed(GlfwKey.Up))
                MoveSelection(-1);
            if (input.WasPressed(GlfwKey.Down))
                MoveSelection(1);
            if (input.WasPressed(GlfwKey.Enter) || input.WasPressed(GlfwKey.KpEnter))
                ConnectSelected();

            // The mouse: hovering a portal's row picks it, clicking it connects to it.
            if (input.UiCursor)
            {
                var cursor = input.CursorSquare;
                var hovered = -1;
                foreach (var (row, box) in _rowBoxes)
                    if (Inside(cursor, box) && row != IndexOf(_home))
                        hovered = row;
                if (hovered >= 0 && cursor != _lastCursor)
                    _selected = hovered;
                _lastCursor = cursor;
                if (hovered >= 0 && input.WasPressed(GlfwMouseButton.Left))
                {
                    _selected = hovered;
                    ConnectSelected();
                }
            }
        }
    }

    // Where the last frame drew each portal's row and the window's cross (centre and size), for the mouse.
    private readonly List<(int Row, float4 Box)> _rowBoxes = [];
    private float4 _closeBox;
    private float2 _lastCursor;

    private static bool Inside(float2 p, float4 box) => float.Abs(p.X - box.X) <= box.Z / 2 && float.Abs(p.Y - box.Y) <= box.W / 2;

    /// <summary>Draws the prompt or the terminal over the view (call after the HUD, in its overlay pass).</summary>
    public void Draw(Renderer renderer, int2 framebufferSize)
    {
        if (!InReach && !IsOpen)
            return;
        renderer.BeginOverlay();
        var aspect = framebufferSize.Y > 0 ? (float)framebufferSize.X / framebufferSize.Y : 1f;
        var px = 2f / float.Max(framebufferSize.Y, 1);   // one screen pixel, in NDC height units

        if (!IsOpen)
        {
            DrawPrompt(renderer, framebufferSize, "[E] Use PC");
            return;
        }

        // The window: scales up from the middle as it opens.
        var s = 0.6f + 0.4f * (1f - (1f - _open) * (1f - _open));
        var pixel = float.Max(2f, float.Round(framebufferSize.Y / 300f)) * px;   // the console font's pixel
        var winW = float.Min(1.85f, aspect * 1.9f) * s;
        var winH = 1.75f * s;
        var center = new float2(0, 0.04f);
        var top = center.Y + winH / 2;
        var left = center.X - winW / 2;
        var titleH = 0.075f * s;

        Rect(renderer, aspect, center, new float2(winW + 2 * px, winH + 2 * px), Edge);
        Rect(renderer, aspect, new float2(center.X, top - titleH / 2), new float2(winW, titleH), TitleBar);
        Rect(renderer, aspect, new float2(center.X, center.Y - titleH / 2), new float2(winW, winH - titleH), Console);

        // The tab: an icon, its title, a close cross; a "+" for a new tab.
        var tabW = 0.62f * s;
        var tabH = titleH * 0.8f;
        var tabCenter = new float2(left + 0.015f * s + tabW / 2, top - titleH + tabH / 2);
        Rect(renderer, aspect, tabCenter, new float2(tabW, tabH), Tab);
        Rect(renderer, aspect, new float2(left + 0.04f * s, tabCenter.Y), new float2(0.026f * s, 0.022f * s), Dim);
        Text(renderer, aspect, ">_", new float2(left + 0.031f * s, tabCenter.Y + 3.5f * pixel * 0.5f), pixel * 0.5f, Backdrop);
        Text(renderer, aspect, "Portal Terminal", new float2(left + 0.07f * s, tabCenter.Y + 3.5f * pixel * 0.75f), pixel * 0.75f, Ink);
        Cross(renderer, aspect, new float2(tabCenter.X + tabW / 2 - 0.03f * s, tabCenter.Y), 0.012f * s, Dim);
        var plus = new float2(tabCenter.X + tabW / 2 + 0.04f * s, tabCenter.Y);
        Rect(renderer, aspect, plus, new float2(0.024f * s, 2 * px), Dim);
        Rect(renderer, aspect, plus, new float2(2 * px, 0.024f * s), Dim);

        // Window buttons: minimise, maximise, close.
        var buttonY = top - titleH / 2;
        var right = left + winW;
        Rect(renderer, aspect, new float2(right - 0.17f * s, buttonY), new float2(0.022f * s, 2 * px), Ink);
        Outline(renderer, aspect, new float2(right - 0.105f * s, buttonY), new float2(0.022f * s, 0.022f * s), px, Ink);
        Cross(renderer, aspect, new float2(right - 0.04f * s, buttonY), 0.012f * s, Ink);
        _closeBox = new float4(right - 0.04f * s, buttonY, 0.07f * s, titleH);
        _rowBoxes.Clear();

        // The console: the opening text, the command typed at the prompt, the index listed line by line, then the
        // prompt again with a blinking block cursor. Its font is sized to fit the widest line and every line.
        var margin = 0.035f * s;
        var consoleW = winW - 2 * margin;
        var consoleH = winH - titleH - 2 * margin;
        var widest = _listing.Append(Prompt + Command).Concat(Lines).Max(l => l.Length);
        var lineCount = Lines.Length + 2 + _listing.Length + 7;   // (room for what connecting prints)
        var cpx = float.Min(consoleW / (widest * BlockFont.Advance), consoleH / (lineCount * (BlockFont.Height + 4)));
        if (cpx > 2 * px)
            cpx = float.Floor(cpx / px) * px;   // whole screen pixels when there's room
        var lineH = (BlockFont.Height + 4) * cpx;
        var x0 = left + margin;
        var y = top - titleH - margin;
        foreach (var line in Lines)
        {
            Text(renderer, aspect, line, new float2(x0, y), cpx, Ink);
            y -= lineH;
        }
        y -= lineH;
        var typed = (int)float.Clamp((_session - TypeDelay) * TypeRate, 0, Command.Length);
        var commandLine = Prompt + Command[..typed];
        Text(renderer, aspect, commandLine, new float2(x0, y), cpx, Ink);
        var cursorAt = commandLine.Length;
        var listingStart = TypeDelay + Command.Length / TypeRate + 0.3f;
        if (_session >= listingStart)
        {
            var shown = (int)float.Min(_listing.Length, (_session - listingStart) * LineRate);
            for (var i = 0; i < shown; i++)
            {
                y -= lineH;
                var line = _listing[i];
                var row = i - HeaderRows;
                var selected = shown == _listing.Length && row == _selected;
                if (row >= 0 && row < _portals.Count)
                    _rowBoxes.Add((row, new float4(x0 + consoleW / 2 - cpx, y - BlockFont.Height * cpx / 2, consoleW, lineH)));
                if (selected)
                {
                    // The chosen destination: highlighted right across, like a selected line in a console menu.
                    Rect(renderer, aspect, new float2(x0 + consoleW / 2 - cpx, y - BlockFont.Height * cpx / 2), new float2(consoleW, lineH), Accent * 0.55f);
                    Text(renderer, aspect, ">", new float2(x0 - 7 * cpx, y), cpx, Ink);
                }
                var offline = row >= 0 && row < _portals.Count && !_portals[row].Online;
                Text(renderer, aspect, line, new float2(x0, y), cpx, selected ? new float3(1f) : i < HeaderRows || i >= _listing.Length - 2 || offline ? Dim : Ink);
            }
            if (shown == _listing.Length)
            {
                y -= lineH;
                foreach (var line in _log)
                {
                    y -= lineH;
                    Text(renderer, aspect, line, new float2(x0, y), cpx, line.StartsWith(Prompt) ? Ink : Accent * 0.6f + new float3(0.35f));
                }
                y -= 2 * lineH;
                var atPrompt = Prompt + AutoTyped;
                Text(renderer, aspect, atPrompt, new float2(x0, y), cpx, Ink);
                cursorAt = atPrompt.Length;
            }
            else
            {
                cursorAt = -1;   // no cursor while it's listing
            }
        }
        if (cursorAt >= 0 && ((int)(_time * 1.9f) % 2 == 0 || typed < Command.Length && _session > TypeDelay))
        {
            var cx = x0 + (cursorAt * BlockFont.Advance) * cpx;
            Rect(renderer, aspect, new float2(cx + 2.5f * cpx, y - BlockFont.Height * cpx / 2), new float2(5 * cpx, BlockFont.Height * cpx), Ink);
        }
        // A thin accent line under the tab bar, as the focused tab has.
        Rect(renderer, aspect, new float2(tabCenter.X, top - titleH + px), new float2(tabW, 2 * px), Accent);
    }

    /// <summary>A prompt such as "[E] Use PC" under the crosshair (on a dark strip), for anything the player can use.</summary>
    public static void DrawPrompt(Renderer renderer, int2 framebufferSize, string hint)
    {
        renderer.BeginOverlay();
        var aspect = framebufferSize.Y > 0 ? (float)framebufferSize.X / framebufferSize.Y : 1f;
        var size = 3f * 2f / float.Max(framebufferSize.Y, 1);
        var width = (hint.Length * BlockFont.Advance - 1) * size;
        var pos = new float2(-width / 2, -0.32f);
        Rect(renderer, aspect, new float2(0, pos.Y - 3.5f * size), new float2(width + 10 * size, 12 * size), Backdrop);
        Text(renderer, aspect, hint, pos, size, Ink);
    }

    /// <summary>Text with its top-left corner at <paramref name="topLeft"/>, <paramref name="pixel"/> per font pixel.</summary>
    internal static void Text(Renderer renderer, float aspect, string text, float2 topLeft, float pixel, float3 color)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (!BlockFont.TryGet(text[i], out var glyph))
                continue;
            var x0 = topLeft.X + i * BlockFont.Advance * pixel;
            for (var row = 0; row < glyph.Length; row++)
            {
                var line = glyph[row];
                var col = 0;
                while (col < BlockFont.Width)
                {
                    if (line[col] != '#')
                    {
                        col++;
                        continue;
                    }
                    var start = col;
                    while (col < BlockFont.Width && line[col] == '#')
                        col++;
                    var run = col - start;
                    Rect(renderer, aspect, new float2(x0 + (start + run / 2f) * pixel, topLeft.Y - (row + 0.5f) * pixel), new float2(run * pixel, pixel), color);
                }
            }
        }
    }

    private static void Outline(Renderer renderer, float aspect, float2 center, float2 size, float px, float3 color)
    {
        Rect(renderer, aspect, center + new float2(0, size.Y / 2), new float2(size.X, 2 * px), color);
        Rect(renderer, aspect, center - new float2(0, size.Y / 2), new float2(size.X, 2 * px), color);
        Rect(renderer, aspect, center + new float2(size.X / 2, 0), new float2(2 * px, size.Y), color);
        Rect(renderer, aspect, center - new float2(size.X / 2, 0), new float2(2 * px, size.Y), color);
    }

    private static void Cross(Renderer renderer, float aspect, float2 center, float half, float3 color)
    {
        foreach (var angle in (ReadOnlySpan<float>)[float.Pi / 4, -float.Pi / 4])
            Rect(renderer, aspect, center, new float2(half * 2.6f, half * 0.28f), color, angle);
    }

    /// <summary>A filled rectangle in "square" space (y from -1 to 1, x from -aspect to aspect).</summary>
    internal static void Rect(Renderer renderer, float aspect, float2 center, float2 size, float3 color, float angle = 0f)
    {
        var model = float4x4.CreateScale(size.X, size.Y, 0.001f)
            * float4x4.CreateRotationZ(angle)
            * float4x4.CreateTranslation(center.X, center.Y, 0)
            * float4x4.CreateScale(1f / aspect, 1f, 1f);
        renderer.DrawCube(model, color, ShadeMode.Unlit);
    }
}
