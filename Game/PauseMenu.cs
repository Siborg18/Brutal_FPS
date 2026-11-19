using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The pause screen. Esc in play releases the mouse and opens it: the view dims and holds still behind a small panel
/// with RESUME and QUIT. (The level editor is its own build: Editor/BrutalFpsEditor.csproj.) Click one, or choose with Up / Down (or W / S) and Enter;
/// Esc again resumes.
/// </summary>
public sealed class PauseMenu
{
    private readonly string[] Items;
    private const int Resume = 0;
    private int Quit => Items.Length - 1;
    private int NpcConfig => _npcConfig is null ? -1 : 1;

    /// <summary>The NPC test build's NPC CONFIG (Game/NpcTestMenu.cs), a button between RESUME and QUIT: null in any other build.</summary>
    private readonly NpcTestMenu? _npcConfig;

    public PauseMenu(NpcTestMenu? npcConfig = null)
    {
        _npcConfig = npcConfig;
        Items = npcConfig is null ? ["RESUME", "QUIT"] : ["RESUME", "NPC CONFIG", "QUIT"];
    }

    private static readonly float3 Panel = new(0.03f, 0.03f, 0.035f);
    private static readonly float3 Edge = new(0.32f, 0.36f, 0.45f);
    private static readonly float3 Button = new(0.09f, 0.09f, 0.1f);
    private static readonly float3 Accent = new(0.35f, 0.55f, 1.0f);
    private static readonly float3 Ink = new(0.80f, 0.80f, 0.80f);
    private static readonly float3 Dim = new(0.55f, 0.56f, 0.6f);

    private const float ButtonWidth = 0.56f, ButtonHeight = 0.12f, ButtonGap = 0.05f;

    private int _selected;

    public bool IsOpen { get; private set; }

    public void Open()
    {
        IsOpen = true;
        _selected = Resume;
        Log.Write("[PAUSE] Paused");
    }

    /// <summary>Paused, straight into NPC CONFIG (the NPC test build: BRUTALFPS_PAUSE=npc, for screenshots).</summary>
    public void OpenNpcConfig()
    {
        Open();
        _npcConfig?.Open();
    }

    public void Close()
    {
        IsOpen = false;
        _npcConfig?.Close();
    }

    /// <summary>What the player picked this frame (after <see cref="Update"/>).</summary>
    public enum Choice { None, Resume, Quit }

    /// <summary>Handles the keys and the mouse while the menu is open (call it after Esc has been checked for opening).</summary>
    public Choice Update(Input input)
    {
        if (!IsOpen)
            return Choice.None;
        if (_npcConfig is { IsOpen: true })
        {
            _npcConfig.Update(input);   // (Esc or BACK there comes back here)
            _lastCursor = input.CursorSquare;
            return Choice.None;
        }
        if (input.WasPressed(GlfwKey.Escape))
            return Choice.Resume;
        if (input.WasPressed(GlfwKey.Up) || input.WasPressed(GlfwKey.W))
            _selected = (_selected + Items.Length - 1) % Items.Length;
        if (input.WasPressed(GlfwKey.Down) || input.WasPressed(GlfwKey.S))
            _selected = (_selected + 1) % Items.Length;

        // The mouse: hovering a button selects it, clicking it chooses it.
        var hovered = -1;
        var cursor = input.CursorSquare;
        for (var i = 0; i < Items.Length; i++)
        {
            var c = ButtonCenter(i);
            if (float.Abs(cursor.X - c.X) <= ButtonWidth / 2 && float.Abs(cursor.Y - c.Y) <= ButtonHeight / 2)
                hovered = i;
        }
        if (hovered >= 0 && cursor != _lastCursor)
            _selected = hovered;
        _lastCursor = cursor;

        var chosen = -1;
        if (input.WasPressed(GlfwKey.Enter) || input.WasPressed(GlfwKey.KpEnter) || input.WasPressed(GlfwKey.Space))
            chosen = _selected;
        else if (input.WasPressed(GlfwMouseButton.Left) && hovered >= 0)
            chosen = hovered;
        if (chosen >= 0 && chosen == NpcConfig)
        {
            _npcConfig!.Open();
            return Choice.None;
        }
        if (chosen == Resume)
            return Choice.Resume;
        return chosen >= 0 && chosen == Quit ? Choice.Quit : Choice.None;
    }

    private float2 _lastCursor;

    private static float2 ButtonCenter(int i) =>
        new(0, -0.06f - i * (ButtonHeight + ButtonGap));

    /// <summary>Draws the dimmed view and the panel over everything (call last, after the HUD).</summary>
    public void Draw(Renderer renderer, int2 framebufferSize)
    {
        if (!IsOpen)
            return;
        renderer.DrawFade(0.55f);
        if (_npcConfig is { IsOpen: true })
        {
            _npcConfig.Draw(renderer, framebufferSize);
            return;
        }
        renderer.BeginOverlay();
        var aspect = framebufferSize.Y > 0 ? (float)framebufferSize.X / framebufferSize.Y : 1f;
        var px = 2f / float.Max(framebufferSize.Y, 1);   // one screen pixel, in NDC height units

        // The panel: a title over the buttons.
        var extra = (Items.Length - 2) * (ButtonHeight + ButtonGap);   // (the NPC test build's third button)
        var panelSize = new float2(ButtonWidth + 0.3f, 0.64f + extra);
        var panelCenter = new float2(0, -0.04f - extra / 2);
        PcTerminal.Rect(renderer, aspect, panelCenter, panelSize + new float2(4 * px, 4 * px), Edge);
        PcTerminal.Rect(renderer, aspect, panelCenter, panelSize, Panel);

        var titlePixel = float.Max(4f, float.Round(framebufferSize.Y / 120f)) * px;
        CenteredText(renderer, aspect, "PAUSED", 0.2f, titlePixel, Ink);

        var pixel = float.Max(3f, float.Round(framebufferSize.Y / 200f)) * px;
        for (var i = 0; i < Items.Length; i++)
        {
            var c = ButtonCenter(i);
            var selected = i == _selected;
            if (selected)
                PcTerminal.Rect(renderer, aspect, c, new float2(ButtonWidth + 4 * px, ButtonHeight + 4 * px), Accent);
            PcTerminal.Rect(renderer, aspect, c, new float2(ButtonWidth, ButtonHeight), selected ? Accent * 0.45f : Button);
            CenteredText(renderer, aspect, Items[i], c.Y + BlockFont.Height * pixel / 2, pixel, selected ? new float3(1f) : Dim);
        }
    }

    private static void CenteredText(Renderer renderer, float aspect, string text, float top, float pixel, float3 color)
    {
        var width = (text.Length * BlockFont.Advance - 1) * pixel;
        PcTerminal.Text(renderer, aspect, text, new float2(-width / 2, top), pixel, color);
    }
}
