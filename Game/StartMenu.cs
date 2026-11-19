using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The screen the game opens on, before anything is loaded: DAY or NIGHT for each level, then START (or QUIT). Click
/// them, or choose with Up / Down (or W / S), Left / Right (or A / D) picks day or night and Enter or Space toggles a
/// level or presses START / QUIT; Esc quits. A factory raid (BRUTALFPS_START=raid: the "Factory raid" launch profile) has a
/// screen of its own instead: over the fence, or in the back of a van (<see cref="ChooseFactoryEntry"/>, <see cref="FactoryRaid"/>).
/// </summary>
public sealed class StartMenu
{
    private static readonly float3 Backdrop = new(0.015f, 0.015f, 0.02f);
    private static readonly float3 Panel = new(0.03f, 0.03f, 0.035f);
    private static readonly float3 Edge = new(0.32f, 0.36f, 0.45f);
    private static readonly float3 Button = new(0.09f, 0.09f, 0.1f);
    private static readonly float3 Accent = new(0.35f, 0.55f, 1.0f);
    private static readonly float3 DayColor = new(0.95f, 0.68f, 0.2f);
    private static readonly float3 NightColor = new(0.28f, 0.42f, 0.95f);
    private static readonly float3 Ink = new(0.80f, 0.80f, 0.80f);
    private static readonly float3 Dim = new(0.55f, 0.56f, 0.6f);

    private const float RowHeight = 0.09f, RowStep = 0.11f, FirstRowY = 0.6f;
    private const float ChoiceWidth = 0.34f, DayX = 0.28f, NightX = 0.66f, NameX = -0.84f;
    private const float ActionWidth = 0.56f, ActionHeight = 0.11f;

    private static int LevelCount => TimeOfDay.Levels.Length;
    private static int StartRow => LevelCount;
    private static int QuitRow => LevelCount + 1;
    private static int RowCount => LevelCount + 2;

    private int _selected = LevelCount;   // (START: the usual choice is to change nothing)
    private float2 _lastCursor;

    // A factory raid's screen: how you get in.
    private const float CardWidth = 0.84f, CardHeight = 0.86f, CardX = 0.46f, CardY = 0.06f;
    private const int FenceCard = 0, VanCard = 1;   // (the left card and the right: on the Antwerp raid's screen, the boat and the rope)
    private bool _choosingEntry;
    private int _entrySelected = FenceCard;
    private int _entryChosen = -1;
    private bool _entryTime;   // (the Antwerp raid's screen: DAY or NIGHT in the yard as well, under the cards)
    private const float TimeY = -0.56f, TimeX = 0.2f;
    private (string Title, string[] Lines)[] _entryCards =
    [
        ("OVER THE FENCE", ["Go in on foot, up", "over the west wall.", "", "You start at the", "edge of the grounds,", "a long way from", "anyone."]),
        ("IN THE VAN", ["Hide in the back of", "a delivery van and", "ride in by the gate.", "", "You get out at the", "hall's big door."]),
    ];

    /// <summary>Shows the menu until it's left: with START, or with the window told to close. <paramref name="eachFrame"/> runs after the input's update (Alt+Enter).</summary>
    public void Run(GlfwWindow window, Renderer renderer, Input input, Action eachFrame)
    {
        TimeOfDay.LoadSaved();
        if (Loop(window, renderer, input, eachFrame))
            TimeOfDay.Save();
        foreach (var (level, _) in TimeOfDay.Levels)
            Log.Write($"[TIME] {level}: {(TimeOfDay.IsNight(level) ? "night" : "day")}");
    }

    /// <summary>A factory raid's only screen: over the fence or in the van. <see cref="FactoryEntry.None"/> if the window was closed instead (Esc).</summary>
    public FactoryEntry ChooseFactoryEntry(GlfwWindow window, Renderer renderer, Input input, Action eachFrame)
    {
        _choosingEntry = true;
        Loop(window, renderer, input, eachFrame);
        return _entryChosen switch { FenceCard => FactoryEntry.Fence, VanCard => FactoryEntry.Van, _ => FactoryEntry.None };
    }

    /// <summary>
    /// The Antwerp raid's only screen (BRUTALFPS_START=antwerp: the "Antwerp raid" launch profile): by boat or down the rope.
    /// <see cref="AntwerpEntry.None"/> if the window was closed instead (Esc).
    /// </summary>
    public AntwerpEntry ChooseAntwerpEntry(GlfwWindow window, Renderer renderer, Input input, Action eachFrame)
    {
        _choosingEntry = true;
        _entryCards =
        [
            ("BY BOAT", ["Cross the dock in a", "fast boat with three", "of your own, then", "up the quay ladder.", "", "They've seen you", "coming: fight your", "way into the stacks."]),
            ("DOWN THE ROPE", ["Go in alone, from", "the top of the", "east stacks.", "", "Abseil into a dead", "end between them", "and nobody knows", "you're there."]),
        ];
        _entryTime = true;   // (starting as the yard was last played: TimeOfDay.LoadSaved has run)
        Loop(window, renderer, input, eachFrame);
        return _entryChosen switch { FenceCard => AntwerpEntry.Boat, VanCard => AntwerpEntry.Abseil, _ => AntwerpEntry.None };
    }

    /// <summary>Runs the screen until it's left: true if that was by starting the game.</summary>
    private bool Loop(GlfwWindow window, Renderer renderer, Input input, Action eachFrame)
    {
        var started = false;
        while (!started && !window.ShouldClose)
        {
            Glfw.PollEvents();
            input.Update();
            eachFrame();
            switch (Update(input))
            {
                case Choice.Start:
                    started = true;
                    break;
                case Choice.Quit:
                    window.ShouldClose = true;
                    break;
            }

            var size = window.FramebufferSize;
            if (size.X > 0 && size.Y > 0)
                Draw(renderer, size);
            // Debug: BRUTALFPS_MENUSHOT=file.png saves the menu as it first appears, and quits.
            if (Environment.GetEnvironmentVariable("BRUTALFPS_MENUSHOT") is { Length: > 0 } shot)
            {
                Renderer.SaveScreenshot(Path.Combine(AppContext.BaseDirectory, shot), size);
                window.ShouldClose = true;
            }
            window.SwapBuffers();
            if (size.X <= 0 || size.Y <= 0)
                Thread.Sleep(16);   // (minimized)
        }

        return started;
    }

    private enum Choice { None, Start, Quit }

    private Choice Update(Input input)
    {
        if (_choosingEntry)
            return UpdateEntry(input);
        if (input.WasPressed(GlfwKey.Escape))
            return Choice.Quit;
        if (input.WasPressed(GlfwKey.Up) || input.WasPressed(GlfwKey.W))
            _selected = (_selected + RowCount - 1) % RowCount;
        if (input.WasPressed(GlfwKey.Down) || input.WasPressed(GlfwKey.S))
            _selected = (_selected + 1) % RowCount;

        if (_selected < LevelCount)
        {
            var level = TimeOfDay.Levels[_selected].Level;
            if (input.WasPressed(GlfwKey.Left) || input.WasPressed(GlfwKey.A))
                TimeOfDay.Set(level, false);
            if (input.WasPressed(GlfwKey.Right) || input.WasPressed(GlfwKey.D))
                TimeOfDay.Set(level, true);
        }

        // The mouse: hovering a row selects it, clicking DAY or NIGHT sets it, clicking START or QUIT presses it.
        var cursor = input.CursorSquare;
        var hovered = -1;
        var hoveredNight = false;
        for (var i = 0; i < LevelCount; i++)
        {
            if (float.Abs(cursor.Y - RowY(i)) > RowHeight / 2)
                continue;
            if (float.Abs(cursor.X - DayX) <= ChoiceWidth / 2)
                hovered = i;
            else if (float.Abs(cursor.X - NightX) <= ChoiceWidth / 2)
                (hovered, hoveredNight) = (i, true);
        }
        for (var i = StartRow; i < RowCount; i++)
            if (float.Abs(cursor.X) <= ActionWidth / 2 && float.Abs(cursor.Y - RowY(i)) <= ActionHeight / 2)
                hovered = i;
        if (hovered >= 0 && cursor != _lastCursor)
            _selected = hovered;
        _lastCursor = cursor;

        var pressed = -1;
        if (input.WasPressed(GlfwKey.Enter) || input.WasPressed(GlfwKey.KpEnter) || input.WasPressed(GlfwKey.Space))
        {
            pressed = _selected;
            if (pressed < LevelCount)
                TimeOfDay.Set(TimeOfDay.Levels[pressed].Level, !TimeOfDay.IsNight(TimeOfDay.Levels[pressed].Level));
        }
        else if (input.WasPressed(GlfwMouseButton.Left) && hovered >= 0)
        {
            pressed = hovered;
            if (pressed < LevelCount)
                TimeOfDay.Set(TimeOfDay.Levels[pressed].Level, hoveredNight);
        }
        return pressed == StartRow ? Choice.Start : pressed == QuitRow ? Choice.Quit : Choice.None;
    }

    /// <summary>A factory raid's screen: left and right between the two ways in, Enter or a click to go, Esc to quit.</summary>
    private Choice UpdateEntry(Input input)
    {
        if (input.WasPressed(GlfwKey.Escape))
            return Choice.Quit;
        if (input.WasPressed(GlfwKey.Left) || input.WasPressed(GlfwKey.A))
            _entrySelected = FenceCard;
        if (input.WasPressed(GlfwKey.Right) || input.WasPressed(GlfwKey.D))
            _entrySelected = VanCard;

        // DAY or NIGHT (the Antwerp raid): up or down (W / S, or N) swaps it, a click on either button sets it.
        var cursor = input.CursorSquare;
        if (_entryTime)
        {
            if (input.WasPressed(GlfwKey.Up) || input.WasPressed(GlfwKey.Down) || input.WasPressed(GlfwKey.W) || input.WasPressed(GlfwKey.S) || input.WasPressed(GlfwKey.N))
                TimeOfDay.Set(DayNightLevel.Yard, !TimeOfDay.IsNight(DayNightLevel.Yard));
            if (input.WasPressed(GlfwMouseButton.Left) && float.Abs(cursor.Y - TimeY) <= RowHeight / 2)
            {
                if (float.Abs(cursor.X + TimeX) <= ChoiceWidth / 2)
                    TimeOfDay.Set(DayNightLevel.Yard, false);
                else if (float.Abs(cursor.X - TimeX) <= ChoiceWidth / 2)
                    TimeOfDay.Set(DayNightLevel.Yard, true);
            }
        }

        var hovered = -1;
        if (float.Abs(cursor.Y - CardY) <= CardHeight / 2 && float.Abs(float.Abs(cursor.X) - CardX) <= CardWidth / 2)
            hovered = cursor.X < 0 ? FenceCard : VanCard;
        if (hovered >= 0 && cursor != _lastCursor)
            _entrySelected = hovered;
        _lastCursor = cursor;

        var pressed = input.WasPressed(GlfwKey.Enter) || input.WasPressed(GlfwKey.KpEnter) || input.WasPressed(GlfwKey.Space)
            ? _entrySelected
            : input.WasPressed(GlfwMouseButton.Left) ? hovered : -1;
        if (pressed is FenceCard or VanCard)
        {
            _entryChosen = pressed;
            return Choice.Start;
        }
        return Choice.None;
    }

    private static float RowY(int row) =>
        row < LevelCount ? FirstRowY - row * RowStep
                         : FirstRowY - LevelCount * RowStep - 0.07f - (row - LevelCount) * (ActionHeight + 0.04f);

    private void Draw(Renderer renderer, int2 size)
    {
        renderer.ClearWindow(size);
        renderer.BeginOverlay();
        var aspect = (float)size.X / size.Y;
        var px = 2f / size.Y;   // one screen pixel, in NDC height units

        var panelSize = new float2(1.86f, 1.68f);
        var panelCenter = new float2(0, 0.1f);
        PcTerminal.Rect(renderer, aspect, new float2(0, 0), new float2(2f * aspect, 2f), Backdrop);
        PcTerminal.Rect(renderer, aspect, panelCenter, panelSize + new float2(4 * px, 4 * px), Edge);
        PcTerminal.Rect(renderer, aspect, panelCenter, panelSize, Panel);

        var titlePixel = float.Max(4f, float.Round(size.Y / 120f)) * px;
        CenteredText(renderer, aspect, "BRUTAL FPS", 0.87f, titlePixel, Ink);

        var pixel = float.Max(2f, float.Round(size.Y / 220f)) * px;
        if (_choosingEntry)
        {
            DrawEntry(renderer, aspect, size, pixel, px);
            return;
        }
        for (var i = 0; i < LevelCount; i++)
        {
            var (level, name) = TimeOfDay.Levels[i];
            var y = RowY(i);
            var selected = i == _selected;
            if (selected)
                PcTerminal.Rect(renderer, aspect, new float2(0, y), new float2(1.76f, RowHeight + 0.018f), Accent * 0.22f);
            PcTerminal.Text(renderer, aspect, name, new float2(NameX, y + BlockFont.Height * pixel / 2), pixel, selected ? new float3(1f) : Ink);
            var night = TimeOfDay.IsNight(level);
            ChoiceButton(renderer, aspect, "DAY", DayX, y, !night, DayColor, pixel);
            ChoiceButton(renderer, aspect, "NIGHT", NightX, y, night, NightColor, pixel);
        }

        ActionButton(renderer, aspect, "START", StartRow, pixel, px);
        ActionButton(renderer, aspect, "QUIT", QuitRow, pixel, px);
    }

    private void DrawEntry(Renderer renderer, float aspect, int2 size, float pixel, float px)
    {
        CenteredText(renderer, aspect, "HOW DO YOU GET IN", 0.68f, pixel, Ink);
        var small = float.Max(1f, float.Round(size.Y / 300f)) * px;
        Card(renderer, aspect, -CardX, FenceCard, _entryCards[FenceCard].Title, _entryCards[FenceCard].Lines, pixel, small, px);
        Card(renderer, aspect, CardX, VanCard, _entryCards[VanCard].Title, _entryCards[VanCard].Lines, pixel, small, px);
        if (_entryTime)
        {
            var night = TimeOfDay.IsNight(DayNightLevel.Yard);
            ChoiceButton(renderer, aspect, "DAY", -TimeX, TimeY, !night, DayColor, pixel);
            ChoiceButton(renderer, aspect, "NIGHT", TimeX, TimeY, night, NightColor, pixel);
        }
    }

    private void Card(Renderer renderer, float aspect, float x, int index, string title, string[] lines, float pixel, float small, float px)
    {
        var center = new float2(x, CardY);
        var selected = _entrySelected == index;
        PcTerminal.Rect(renderer, aspect, center, new float2(CardWidth + 4 * px, CardHeight + 4 * px), selected ? Accent : Edge * 0.6f);
        PcTerminal.Rect(renderer, aspect, center, new float2(CardWidth, CardHeight), selected ? Accent * 0.2f : Button);
        var titleWidth = (title.Length * BlockFont.Advance - 1) * pixel;
        var top = CardY + CardHeight / 2 - 0.09f;
        PcTerminal.Text(renderer, aspect, title, new float2(x - titleWidth / 2, top), pixel, selected ? new float3(1f) : Ink);
        for (var i = 0; i < lines.Length; i++)
        {
            var width = (lines[i].Length * BlockFont.Advance - 1) * small;
            PcTerminal.Text(renderer, aspect, lines[i], new float2(x - width / 2, top - 0.14f - i * (BlockFont.Height + 4) * small), small, selected ? Ink : Dim);
        }
    }

    private static void ChoiceButton(Renderer renderer, float aspect, string text, float x, float y, bool on, float3 color, float pixel)
    {
        PcTerminal.Rect(renderer, aspect, new float2(x, y), new float2(ChoiceWidth, RowHeight), on ? color * 0.75f : Button);
        var width = (text.Length * BlockFont.Advance - 1) * pixel;
        PcTerminal.Text(renderer, aspect, text, new float2(x - width / 2, y + BlockFont.Height * pixel / 2), pixel, on ? new float3(1f) : Dim);
    }

    private void ActionButton(Renderer renderer, float aspect, string text, int row, float pixel, float px)
    {
        var c = new float2(0, RowY(row));
        var selected = row == _selected;
        if (selected)
            PcTerminal.Rect(renderer, aspect, c, new float2(ActionWidth + 4 * px, ActionHeight + 4 * px), Accent);
        PcTerminal.Rect(renderer, aspect, c, new float2(ActionWidth, ActionHeight), selected ? Accent * 0.45f : Button);
        CenteredText(renderer, aspect, text, c.Y + BlockFont.Height * pixel / 2, pixel, selected ? new float3(1f) : Dim);
    }

    /// <summary>A last frame while the world loads (it takes a few seconds, and the window would just sit there).</summary>
    public static void DrawLoading(GlfwWindow window, Renderer renderer)
    {
        var size = window.FramebufferSize;
        if (size.X <= 0 || size.Y <= 0)
            return;
        renderer.ClearWindow(size);
        renderer.BeginOverlay();
        var aspect = (float)size.X / size.Y;
        var px = 2f / size.Y;
        PcTerminal.Rect(renderer, aspect, new float2(0, 0), new float2(2f * aspect, 2f), Backdrop);
        var pixel = float.Max(3f, float.Round(size.Y / 200f)) * px;
        CenteredText(renderer, aspect, "LOADING", BlockFont.Height * pixel / 2, pixel, Ink);
        window.SwapBuffers();
    }

    private static void CenteredText(Renderer renderer, float aspect, string text, float top, float pixel, float3 color)
    {
        var width = (text.Length * BlockFont.Advance - 1) * pixel;
        PcTerminal.Text(renderer, aspect, text, new float2(-width / 2, top), pixel, color);
    }
}
