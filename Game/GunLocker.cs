using System.Diagnostics;
using System.Globalization;
using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>What the gun locker shows of a gun, besides its picture.</summary>
public sealed record GunStats
{
    /// <summary>What it is ("SA80 bullpup assault rifle").</summary>
    public required string Kind { get; init; }

    public required string Calibre { get; init; }

    /// <summary>"30-round magazine", "200-round belt".</summary>
    public required string Feed { get; init; }

    /// <summary>"Select fire", "Semi-auto", "Pump", "Belt-fed".</summary>
    public required string Action { get; init; }

    public string Optic { get; init; } = "Iron sights";

    /// <summary>Muzzle energy (J) of one round, or of each pellet.</summary>
    public required float Energy { get; init; }

    public int Pellets { get; init; } = 1;

    /// <summary>Cyclic rate, or as fast as you can click (or pump) a gun that fires one at a time.</summary>
    public required float RoundsPerMinute { get; init; }

    /// <summary>Muzzle velocity, m/s.</summary>
    public required float Velocity { get; init; }

    /// <summary>The radius (radians) of the cone a first, aimed shot lands in (the pellets' pattern, for a shotgun).</summary>
    public required float AimedSpread { get; init; }

    /// <summary>How fast you move with it, of your usual speed.</summary>
    public float Mobility { get; init; } = 1f;

    public bool Suppressed { get; init; }

    public bool FullAuto => Action is "Select fire" or "Belt-fed" or "Full auto";

    public static GunStats Of(RifleSpec s, string? kind = null, string? optic = null) => new()
    {
        Kind = kind ?? s.Kind,
        Optic = optic ?? s.Optic,
        Calibre = s.Cartridge.Name,
        Feed = $"{s.Capacity}-round magazine",
        Action = s.SemiOnly ? "Semi-auto" : "Select fire",
        Energy = s.Cartridge.Energy(s.ProjectileSpeed),
        RoundsPerMinute = 60f / (s.SemiOnly ? s.FireInterval : s.AutoFireInterval),
        Velocity = s.ProjectileSpeed,
        AimedSpread = s.NewSpread().Radius(1f),
        Mobility = s.MoveScale,
        Suppressed = s.ShotVolume < 0.5f,
    };
}

/// <summary>A gun in the locker: the gun, its model (for its picture; null: no picture) and what's shown of it.</summary>
public sealed record LockerEntry(IGun Gun, Model? Model, GunStats Stats);

/// <summary>
/// The gun lockers, at the police armoury's bench and at the firing range: E brings up the locker with your loadout on the
/// left (the ten number keys, 1 to 9 then 0, each with the gun on it) and every gun in the game on the right, each on a
/// card with its picture (its own model, drawn live); under the loadout, the one picked with its numbers.
/// The mouse: click a card to pick it, drag a gun from the right onto a number to put it there (in place of whatever was
/// there), drag between numbers to swap them, drag one off the loadout back to the right to take it off, and double-click
/// to take it in your hands. The keys: the arrows (or the mouse wheel) pick one; a number key puts it on that number;
/// Backspace takes it off; Enter takes it in your hands (on the first free number if it has none). E, Esc or walking away
/// shuts it. What's on which number is kept in loadout.txt next to the exe.
/// The SCOPE button on the picked gun's picture opens the optics it can take (<see cref="Optics"/>, <see cref="OpticPicker"/>).
/// </summary>
public sealed class GunLocker(IReadOnlyList<LockerEntry> catalog, IGun?[] slots)
{
    private const float Reach = 2.2f;
    private const string SaveFile = "loadout.txt";
    private const int Columns = 4;

    private static readonly GlfwKey[] Digits =
    [
        GlfwKey.Number0, GlfwKey.Number1, GlfwKey.Number2, GlfwKey.Number3, GlfwKey.Number4,
        GlfwKey.Number5, GlfwKey.Number6, GlfwKey.Number7, GlfwKey.Number8, GlfwKey.Number9,
    ];

    // The look: gunmetal panels, an amber accent.
    private static readonly float3 Frame = new(0.20f, 0.21f, 0.22f);
    internal static readonly float3 Panel = new(0.045f, 0.05f, 0.055f);
    private static readonly float3 Header = new(0.075f, 0.08f, 0.085f);
    internal static readonly float3 Card = new(0.085f, 0.09f, 0.1f);
    internal static readonly float3 CardEdge = new(0.16f, 0.17f, 0.18f);
    private static readonly float3 Label = new(0.06f, 0.065f, 0.07f);
    private static readonly float3 Stage = new(0.15f, 0.16f, 0.17f);
    private static readonly float3 Floor = new(0.07f, 0.075f, 0.08f);
    internal static readonly float3 Track = new(0.13f, 0.14f, 0.15f);
    internal static readonly float3 Amber = new(1.0f, 0.68f, 0.18f);
    internal static readonly float3 AmberDim = new(0.45f, 0.32f, 0.1f);
    internal static readonly float3 Ink = new(0.9f, 0.9f, 0.86f);
    internal static readonly float3 Dim = new(0.5f, 0.53f, 0.54f);
    internal static readonly float3 Green = new(0.45f, 0.85f, 0.45f);

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private int _selected;
    private bool _forced;

    public bool IsOpen { get; private set; }

    /// <summary>"[E] Gun locker" while you're at one, otherwise null.</summary>
    public string? Prompt { get; private set; }

    /// <summary>The guns you've put on a number here (the M249 stays on its number even when it isn't off the tripod).</summary>
    public bool Chosen(IGun gun) => Array.IndexOf(slots, gun) >= 0 && _chosen.Contains(gun);
    private readonly HashSet<IGun> _chosen = [];

    /// <summary>Debug: BRUTALFPS_LOCKER=open (or open:L85A2, with that gun picked), for screenshots: up from the start, wherever you are.</summary>
    public void ForceOpen(string? pick = null)
    {
        IsOpen = true;
        _forced = true;
        var index = catalog.ToList().FindIndex(e => e.Gun.Name.Equals(pick, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            _selected = index;
    }

    public bool Close()
    {
        if (!IsOpen)
            return false;
        IsOpen = false;
        _forced = false;
        _optics.Close();
        Log.Write("[LOCKER] Shut");
        return true;
    }

    private static bool Near(float3 player, float3 at)
    {
        var offset = at - player;
        return offset.X * offset.X + offset.Z * offset.Z < Reach * Reach && float.Abs(offset.Y) < 2.5f;
    }

    /// <summary>The gun to bring up, if Enter was pressed on one.</summary>
    public IGun? Update(Input input, float3 player, bool playing, bool otherScreenUp)
    {
        var near = Near(player, Level.RangeLockerPoint) || Near(player, Level.ArmouryLockerPoint);
        Prompt = near && !IsOpen && !otherScreenUp ? "[E] Gun locker" : null;
        if (!IsOpen)
        {
            if (near && playing && !otherScreenUp && input.WasPressed(GlfwKey.E))
            {
                IsOpen = true;
                Log.Write("[LOCKER] Open");
            }
            return null;
        }
        if (!near && !_forced)
        {
            Close();
            return null;
        }
        if (!playing)
            return null;
        if (input.WasPressed(GlfwKey.E))
        {
            Close();
            return null;
        }

        _selected = Pick(input, _selected, catalog.Count);

        // The SCOPE button on the picked gun's picture, and its list: a click there isn't a click on a card.
        var opticClick = _optics.Update(input);

        // The mouse: boxes 0 to 9 are the numbers, then the guns available, in the catalog's order.
        var (what, from, to) = _mouse.Update(input, i => GunAt(i) is not null);
        if (opticClick)
            what = CardMouse.Event.None;
        switch (what)
        {
            case CardMouse.Event.Click when GunAt(from) is { } clicked:
                _selected = IndexOf(clicked);
                break;
            case CardMouse.Event.DoubleClick when GunAt(from) is { } clicked:
                return TakeIt(clicked);
            case CardMouse.Event.Drop when GunAt(from) is { } dragged:
                if (to is >= 0 and < 10)
                {
                    if (from < 10)
                        Move(from, to);
                    else
                    {
                        Assign(dragged, to);
                        Log.Write($"[LOCKER] {dragged.Name} on {to}");
                        Save();
                    }
                }
                else if (from < 10 && (to >= 10 || CardMouse.Inside(_mouse.Cursor, _availableBox)))
                {
                    TakeOff(dragged);   // (dragged back to the locker)
                }
                _selected = IndexOf(dragged);
                break;
        }

        var gun = catalog[_selected].Gun;

        for (var d = 0; d < 10; d++)
        {
            if (!input.WasPressed(Digits[d]))
                continue;
            Assign(gun, d);
            Log.Write($"[LOCKER] {gun.Name} on {d}");
            Save();
        }
        if (input.WasPressed(GlfwKey.Backspace) || input.WasPressed(GlfwKey.Delete))
            TakeOff(gun);
        if (input.WasPressed(GlfwKey.Enter))
            return TakeIt(gun);
        return null;
    }

    private readonly CardMouse _mouse = new();
    private readonly OpticPicker _optics = new();
    private float4 _availableBox;   // the right half, where the guns available are (a gun dropped there comes off its number)

    /// <summary>Mouse box <paramref name="i"/>'s gun: 0 to 9 the numbers (null if nothing's on it), then the catalog's.</summary>
    private IGun? GunAt(int i) => i < 0 ? null : i < 10 ? slots[i] : i - 10 < catalog.Count ? catalog[i - 10].Gun : null;

    private int IndexOf(IGun gun)
    {
        for (var i = 0; i < catalog.Count; i++)
            if (catalog[i].Gun == gun)
                return i;
        return _selected;
    }

    /// <summary>Takes it in your hands, putting it on the first free number if it's on none, and shuts the locker.</summary>
    private IGun TakeIt(IGun gun)
    {
        if (Array.IndexOf(slots, gun) < 0)
        {
            var free = Array.FindIndex(slots, 1, s => s is null);
            Assign(gun, free >= 0 ? free : slots[0] is null ? 0 : 1);
            Save();
        }
        Close();
        return gun;
    }

    private void TakeOff(IGun gun)
    {
        for (var d = 0; d < slots.Length; d++)
            if (slots[d] == gun)
                slots[d] = null;
        _chosen.Remove(gun);
        Log.Write($"[LOCKER] {gun.Name} put away");
        Save();
    }

    /// <summary>Swaps what's on two numbers (the inventory's drag and drop too), and keeps it.</summary>
    internal void Move(int from, int to)
    {
        if (from == to || from is < 0 or > 9 || to is < 0 or > 9)
            return;
        (slots[from], slots[to]) = (slots[to], slots[from]);
        foreach (var gun in (ReadOnlySpan<IGun?>)[slots[from], slots[to]])
            if (gun is not null)
                _chosen.Add(gun);
        Log.Write($"[LOCKER] {slots[to]?.Name} moved from {from} to {to}{(slots[from] is { } other ? $", {other.Name} to {from}" : "")}");
        Save();
    }

    /// <summary>Puts a gun on a number (off any it was on, and in place of what was there), and keeps it.</summary>
    internal void Put(IGun gun, int number)
    {
        Assign(gun, number);
        Log.Write($"[LOCKER] {gun.Name} on {number}");
        Save();
    }

    /// <summary>The card picked after this frame's keys: left / right (and the wheel) step through them; up / down a row at a time.</summary>
    internal static int Pick(Input input, int selected, int count, int columns = Columns)
    {
        var step = input.WasPressed(GlfwKey.Left) ? -1
            : input.WasPressed(GlfwKey.Right) ? 1
            : input.WasPressed(GlfwKey.Up) ? -columns
            : input.WasPressed(GlfwKey.Down) ? columns
            : -input.ScrollSteps;
        if (int.Abs(step) == columns && (selected + step < 0 || selected + step >= count))
            step = 0;   // (a row off the top or bottom: stay)
        return ((selected + step) % count + count) % count;
    }

    private void Assign(IGun gun, int number)
    {
        for (var d = 0; d < slots.Length; d++)
            if (slots[d] == gun)
                slots[d] = null;
        slots[number] = gun;
        _chosen.Add(gun);
    }

    /// <summary>What was on which number last time, if there's a record of it. False: leave the defaults.</summary>
    public bool Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, SaveFile);
        if (!File.Exists(path))
            return false;
        Array.Clear(slots);
        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2 && int.TryParse(parts[0], out var d) && d is >= 0 and <= 9 && catalog.FirstOrDefault(e => e.Gun.Name == parts[1].Trim()) is { } entry)
            {
                slots[d] = entry.Gun;
                _chosen.Add(entry.Gun);
            }
        }
        return true;
    }

    private void Save()
    {
        try
        {
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, SaveFile),
                Enumerable.Range(0, slots.Length).Where(d => slots[d] is not null && (slots[d] is not Lmg || _chosen.Contains(slots[d]!))).Select(d => $"{d}={slots[d]!.Name}"));
        }
        catch (Exception e)
        {
            Log.Write($"[WARN] Couldn't save the loadout ({e.Message})");
        }
    }

    // ------------------------------------------------------------------------------------------------ drawing

    /// <summary>A model's gun alone (no hands or sleeves), and its bounds: what a picture of it frames.</summary>
    private readonly Dictionary<(Model, bool), (ModelPart[] Parts, Aabb Bounds)> _pictures = [];

    private (ModelPart[] Parts, Aabb Bounds) Picture(Model model, bool ownOptic = true)
    {
        var key = (model, ownOptic);
        if (_pictures.TryGetValue(key, out var picture))
            return picture;
        var parts = model.Parts.Where(p => !IsHand(p.Name) && (ownOptic || p.Name != "Optic")).ToArray();
        var bounds = parts.Length > 0 ? Optics.Bounds(parts) : model.Bounds;
        return _pictures[key] = (parts, bounds);
    }

    /// <summary>What's shown of a gun in the locker: its sights as the optic fitted to it makes them.</summary>
    internal static GunStats Fitted(LockerEntry entry) => entry.Stats with { Optic = Optics.NameOf(Optics.FittedTo(entry.Gun), entry.Stats.Optic) };

    private static bool IsHand(string name) =>
        name is "Hand" or "HandLeft" or "TriggerFinger" or "Sleeve" or "SleeveLeft"
        || name.StartsWith("Hand", StringComparison.Ordinal) || name.StartsWith("Sleeve", StringComparison.Ordinal)
        || name.StartsWith("Arm", StringComparison.Ordinal) || name.StartsWith("Finger", StringComparison.Ordinal);

    /// <param name="eye">Where the player's eye is: the pictures are lit as the gun in their hands is.</param>
    /// <param name="inHand">The gun they're holding (marked on its card).</param>
    public void Draw(Renderer renderer, int2 framebufferSize, float3 eye, IGun? inHand)
    {
        if (!IsOpen || catalog.Count == 0)
            return;
        var menu = DrawCase(renderer, framebufferSize, "GUN LOCKER", $"{catalog.Count} GUNS",
            [("DRAG", "onto a number, or back to take off"), ("DOUBLE-CLICK/ENTER", "take it"), ("ARROWS", "pick"), ("0-9", "put on number"), ("BACKSPACE", "take off"), ("E", "shut")]);
        var (aspect, px, unit, _, _, _) = menu;
        var margin = MenuCase.Margin;
        var half = (menu.Width - 3 * margin) / 2;
        var leftX = menu.Left + margin;
        var rightX = leftX + half + margin;
        var headingHeight = (BlockFont.Height + 9) * unit;
        var pictures = new List<(Model? Model, float2 Center, float2 Size, bool Large)>();
        var dragged = _mouse.Dragging ? GunAt(_mouse.Held) : null;
        var selectedGun = catalog[_selected].Gun;

        // Mouse boxes 0 to 9: the numbers; then the guns available.
        _mouse.Boxes.Clear();
        for (var d = 0; d < 10; d++)
            _mouse.Boxes.Add(default);

        // On the left, the loadout: the ten numbers in two rows, 1 to 9 then 0 as along the keyboard.
        DrawSectionHeading(renderer, menu, leftX, half, "LOADOUT", "DRAG GUNS ONTO A NUMBER");
        var slotTop = menu.BodyTop - headingHeight;
        var slotMenu = menu with { GridLeft = leftX, GridWidth = half, BodyTop = slotTop, BodyBottom = slotTop - menu.BodyHeight * 0.44f };
        for (var k = 0; k < 10; k++)
        {
            var number = (k + 1) % 10;
            var (corner, size) = CardAt(slotMenu, k, 2, 5);
            _mouse.Boxes[number] = CardMouse.Box(corner, size);
            var hovered = _mouse.Hovered == number;
            var target = dragged is not null && hovered;
            var label = number.ToString(CultureInfo.InvariantCulture);
            if (slots[number] is not { } gun)
            {
                DrawEmptySlot(renderer, slotMenu, corner, size, label, false, target);
                continue;
            }
            var (center, pictureSize) = DrawCard(renderer, slotMenu, corner, size, gun.Name, label, null, gun == selectedGun, gun == inHand, false, target);
            pictures.Add((ModelOf(gun), center, pictureSize, false));
        }

        // On the right, every gun in the locker.
        DrawSectionHeading(renderer, menu, rightX, half, "AVAILABLE", dragged is not null && _mouse.Held < 10 ? "DROP HERE TO TAKE OFF" : "DOUBLE-CLICK TO TAKE IT");
        var gridMenu = menu with { GridLeft = rightX, GridWidth = half, BodyTop = slotTop };
        _availableBox = new float4(rightX + half / 2, (gridMenu.BodyTop + gridMenu.BodyBottom) / 2, half + margin, gridMenu.BodyHeight + headingHeight);
        var rows = (catalog.Count + Columns - 1) / Columns;
        for (var i = 0; i < catalog.Count; i++)
        {
            var entry = catalog[i];
            var (corner, size) = CardAt(gridMenu, i, rows);
            _mouse.Boxes.Add(CardMouse.Box(corner, size));
            var number = Array.IndexOf(slots, entry.Gun);
            var (center, pictureSize) = DrawCard(renderer, gridMenu, corner, size, entry.Gun.Name, number >= 0 ? number.ToString(CultureInfo.InvariantCulture) : null, null,
                i == _selected, entry.Gun == inHand, false, dragged is not null && _mouse.Held < 10 && CardMouse.Inside(_mouse.Cursor, _availableBox));
            pictures.Add((entry.Model, center, pictureSize, false));
        }

        // Under the loadout, the one picked: its picture on a lit stage, then its name, what it is and its numbers.
        var selected = catalog[_selected];
        var detailTop = slotMenu.BodyBottom - margin;
        var stageSize = new float2(half * 0.36f, detailTop - menu.BodyBottom);
        var stageCenter = new float2(leftX + stageSize.X / 2, detailTop - stageSize.Y / 2);
        Box(renderer, aspect, stageCenter, stageSize + new float2(4 * px), CardEdge);
        Gradient(renderer, aspect, stageCenter, stageSize, Stage, Floor, 12);
        pictures.Add((selected.Model, stageCenter, stageSize, true));
        var detail = menu with { BodyTop = detailTop, DetailLeft = leftX + stageSize.X + 2 * margin, DetailRight = leftX + half, StageFraction = 0f };
        var slot = Array.IndexOf(slots, selected.Gun);
        var status = selected.Gun == inHand ? "IN HAND" : slot >= 0 ? $"ON {slot}" : "IN THE LOCKER";
        var y = DrawDetailHeading(renderer, detail, selected.Gun.Name, selected.Stats.Kind, status, selected.Gun == inHand ? Green * 0.5f : slot >= 0 ? AmberDim : CardEdge);
        DrawNumbers(renderer, detail, Fitted(selected), y);
        _optics.DrawButton(renderer, menu, stageCenter, stageSize, selected.Gun, selected.Stats.Optic);

        // The pictures last: each is drawn into its own patch of the screen, over its card.
        var time = (float)Clock.Elapsed.TotalSeconds;
        foreach (var (model, center, size, large) in pictures)
            if (model is not null)
                DrawPicture(renderer, framebufferSize, aspect, eye, model, center, size,
                    large ? -0.32f + 0.55f * float.Sin(time * 0.6f) : -0.3f, large ? 0.16f : 0.12f, large ? 0.82f : 0.86f);

        // Over them, the list of optics if it's open.
        _optics.DrawList(renderer, framebufferSize, menu, eye);

        // Over everything, the gun being dragged, under the pointer.
        if (dragged is not null)
            DrawDragged(renderer, framebufferSize, menu, eye, dragged.Name, ModelOf(dragged), _mouse.Cursor, CardAt(slotMenu, 0, 2, 5).Size);
    }

    private Model? ModelOf(IGun gun)
    {
        foreach (var entry in catalog)
            if (entry.Gun == gun)
                return entry.Model;
        return null;
    }

    /// <summary>A card being dragged: a copy of it, a little smaller, its top left corner under the pointer.</summary>
    internal void DrawDragged(Renderer renderer, int2 framebufferSize, in MenuCase menu, float3 eye, string name, Model? model, float2 cursor, float2 size)
    {
        size *= 0.85f;
        var corner = cursor + new float2(-size.X * 0.15f, size.Y * 0.15f);
        var (center, pictureSize) = DrawCard(renderer, menu, corner, size, name, null, null, true, false);
        if (model is not null)
            DrawPicture(renderer, framebufferSize, menu.Aspect, eye, model, center, pictureSize, -0.3f, 0.12f, 0.86f);
    }

    /// <summary>A section's name in amber along the top of its column, a note at the other end, a rule under them.</summary>
    internal static void DrawSectionHeading(Renderer renderer, in MenuCase menu, float left, float width, string title, string note)
    {
        var (aspect, px, unit, _, _, _) = menu;
        Text(renderer, aspect, title, new float2(left, menu.BodyTop), unit, Amber);
        Text(renderer, aspect, note, new float2(left + width - TextWidth(note, unit), menu.BodyTop), unit, Dim);
        Box(renderer, aspect, new float2(left + width / 2, menu.BodyTop - (BlockFont.Height + 4) * unit), new float2(width, 2 * px), CardEdge);
    }

    /// <summary>A number with nothing on it: a dim, empty card with the number large in its middle.</summary>
    internal static void DrawEmptySlot(Renderer renderer, in MenuCase menu, float2 topLeft, float2 size, string number, bool hovered, bool target)
    {
        var (aspect, px, unit, _, _, _) = menu;
        var center = topLeft + new float2(size.X / 2, -size.Y / 2);
        Box(renderer, aspect, center, size + new float2((target ? 8 : 4) * px), target ? Amber : hovered ? CardEdge * 1.8f : CardEdge * 0.7f);
        Box(renderer, aspect, center, size, target ? AmberDim * 0.35f : Card * 0.6f);
        var big = unit * 4f;
        Text(renderer, aspect, number, new float2(center.X - TextWidth(number, big) / 2, center.Y + BlockFont.Height * big / 2 + 3 * unit), big, target ? Amber : Dim * 0.7f);
        Text(renderer, aspect, "EMPTY", new float2(center.X - TextWidth("EMPTY", unit) / 2, center.Y - BlockFont.Height * big / 2 + 1 * unit), unit, Dim * 0.7f);
    }

    /// <summary>Where a menu's parts are, once <see cref="DrawCase"/> has drawn it (and, with <c>with</c>, a part of it).</summary>
    internal readonly record struct MenuCase(float Aspect, float Px, float Unit, float Width, float BodyTop, float BodyBottom)
    {
        public const float Margin = 0.035f;
        public const float Gap = 0.018f;
        public float Left => -Width / 2;
        public float Right => Width / 2;
        public float BodyHeight => BodyTop - BodyBottom;

        /// <summary>Where the cards' grid starts, and how wide it is.</summary>
        public float GridLeft { get; init; } = -Width / 2 + Margin;
        public float GridWidth { get; init; } = (Width - 3 * Margin) * 0.6f;

        /// <summary>Where the details of the one picked go.</summary>
        public float DetailLeft { get; init; } = -Width / 2 + 2 * Margin + (Width - 3 * Margin) * 0.6f;
        public float DetailRight { get; init; } = Width / 2 - Margin;
        public float DetailWidth => DetailRight - DetailLeft;

        /// <summary>How much of the details' height the large picture takes, at the top.</summary>
        public float StageFraction { get; init; } = 0.42f;
    }

    /// <summary>The world dimmed and the menu's case over it: a frame, the panel, the header with an amber rule under it, and the keys along the bottom.</summary>
    internal static MenuCase DrawCase(Renderer renderer, int2 framebufferSize, string title, string count, ReadOnlySpan<(string Key, string What)> keys)
    {
        renderer.DrawFade(0.6f);   // (the world dimmed behind it)
        renderer.BeginOverlay();
        var aspect = framebufferSize.Y > 0 ? (float)framebufferSize.X / framebufferSize.Y : 1f;
        var px = 2f / float.Max(framebufferSize.Y, 1);
        var unit = float.Max(1f, float.Round(framebufferSize.Y / 540f)) * px;   // one pixel of the font, for body text
        var big = unit * 2f;

        var width = float.Min(aspect * 2f - 0.12f, 3.5f);
        var height = 1.84f;
        var left = -width / 2;
        var top = height / 2;
        var margin = MenuCase.Margin;

        Box(renderer, aspect, new float2(0, 0), new float2(width + 6 * px, height + 6 * px), Frame);
        Box(renderer, aspect, new float2(0, 0), new float2(width, height), Panel);
        var headerHeight = BlockFont.Height * big + 2 * margin;
        Box(renderer, aspect, new float2(0, top - headerHeight / 2), new float2(width, headerHeight), Header);
        Box(renderer, aspect, new float2(0, top - headerHeight), new float2(width, 2 * px), Amber);
        Box(renderer, aspect, new float2(left + margin + 2 * unit, top - headerHeight / 2), new float2(4 * unit, BlockFont.Height * big), Amber);
        Text(renderer, aspect, title, new float2(left + margin + 12 * unit, top - margin), big, Ink);
        Text(renderer, aspect, count, new float2(-left - margin - TextWidth(count, unit), top - headerHeight / 2 + BlockFont.Height * unit / 2), unit, Dim);

        var footerHeight = BlockFont.Height * unit + 2 * margin;
        var bottom = -height / 2;
        Box(renderer, aspect, new float2(0, bottom + footerHeight / 2), new float2(width, footerHeight), Header);
        var x = left + margin;
        var keysY = bottom + footerHeight / 2 + BlockFont.Height * unit / 2;
        foreach (var (key, what) in keys)
        {
            var keyWidth = TextWidth(key, unit) + 6 * unit;
            Box(renderer, aspect, new float2(x + keyWidth / 2, keysY - BlockFont.Height * unit / 2), new float2(keyWidth, (BlockFont.Height + 5) * unit), CardEdge);
            Text(renderer, aspect, key, new float2(x + 3 * unit, keysY), unit, Ink);
            x += keyWidth + 3 * unit;
            Text(renderer, aspect, what, new float2(x, keysY), unit, Dim);
            x += TextWidth(what, unit) + 9 * unit;
        }

        return new MenuCase(aspect, px, unit, width, top - headerHeight - margin, bottom + footerHeight + margin);
    }

    /// <summary>The top left corner and size of card <paramref name="index"/> in a grid of <paramref name="columns"/> by <paramref name="rows"/>.</summary>
    internal static (float2 Corner, float2 Size) CardAt(in MenuCase menu, int index, int rows, int columns = Columns)
    {
        var size = new float2((menu.GridWidth - MenuCase.Gap * (columns - 1)) / columns, (menu.BodyHeight - MenuCase.Gap * (rows - 1)) / rows);
        return (new float2(menu.GridLeft + (index % columns) * (size.X + MenuCase.Gap), menu.BodyTop - (index / columns) * (size.Y + MenuCase.Gap)), size);
    }

    /// <inheritdoc cref="DrawCard(Renderer, in MenuCase, float2, float2, string, string?, string?, bool, bool, bool, bool)"/>
    internal static (float2 Center, float2 Size) DrawCard(Renderer renderer, in MenuCase menu, int index, int rows, string name, string? badge, string? corner, bool picked, bool inHand,
        bool hovered = false, bool target = false)
    {
        var (topLeft, cardSize) = CardAt(menu, index, rows);
        return DrawCard(renderer, menu, topLeft, cardSize, name, badge, corner, picked, inHand, hovered, target);
    }

    /// <summary>
    /// A card: its name strip along the bottom, with <paramref name="badge"/> (the key it's on, if any) at the end of it,
    /// <paramref name="corner"/> (if any) in the top right and a green mark if it's in the player's hands. Its edge
    /// lights up under the pointer (<paramref name="hovered"/>), and thickens in amber where a dragged card would go (<paramref name="target"/>).
    /// </summary>
    /// <returns>The box its picture goes in.</returns>
    internal static (float2 Center, float2 Size) DrawCard(Renderer renderer, in MenuCase menu, float2 topLeft, float2 cardSize, string name, string? badge, string? corner, bool picked, bool inHand,
        bool hovered = false, bool target = false)
    {
        var (aspect, px, unit, _, _, _) = menu;
        var labelHeight = (BlockFont.Height + 6) * unit;
        var center = topLeft + new float2(cardSize.X / 2, -cardSize.Y / 2);
        Box(renderer, aspect, center, cardSize + new float2((target ? 8 : 4) * px), picked || target ? Amber : hovered ? CardEdge * 2.2f : CardEdge);
        Box(renderer, aspect, center, cardSize, picked ? Card * 1.35f : hovered ? Card * 1.2f : Card);

        var labelCenter = new float2(center.X, topLeft.Y - cardSize.Y + labelHeight / 2);
        Box(renderer, aspect, labelCenter, new float2(cardSize.X, labelHeight), picked ? AmberDim * 0.5f : Label);
        var textY = labelCenter.Y + BlockFont.Height * unit / 2;
        Text(renderer, aspect, name.ToUpperInvariant(), new float2(topLeft.X + 4 * unit, textY), unit, picked ? Ink : Ink * 0.8f);
        if (badge is not null)
        {
            var at = new float2(topLeft.X + cardSize.X - 7 * unit, labelCenter.Y);
            Box(renderer, aspect, at, new float2(10 * unit, labelHeight - 3 * unit), Amber);
            Text(renderer, aspect, badge, new float2(at.X - 2.5f * unit, textY), unit, Panel);
        }

        var stageSize = new float2(cardSize.X, cardSize.Y - labelHeight);
        var stageCenter = new float2(center.X, topLeft.Y - stageSize.Y / 2);
        Gradient(renderer, aspect, stageCenter, stageSize, picked ? Stage * 1.15f : Stage * 0.8f, Card, 6);
        if (inHand)
            Box(renderer, aspect, new float2(topLeft.X + 4 * unit, topLeft.Y - 4 * unit), new float2(4 * unit, 4 * unit), Green);
        if (corner is not null)
            Text(renderer, aspect, corner, new float2(topLeft.X + cardSize.X - 3 * unit - TextWidth(corner, unit), topLeft.Y - 3 * unit), unit, Ink);
        return (stageCenter, stageSize);
    }

    /// <summary>The lit stage at the top of the details, where the large picture of what's picked goes.</summary>
    internal static (float2 Center, float2 Size) DrawDetailStage(Renderer renderer, in MenuCase menu)
    {
        var size = new float2(menu.DetailWidth, menu.BodyHeight * menu.StageFraction);
        var center = new float2(menu.DetailLeft + size.X / 2, menu.BodyTop - size.Y / 2);
        Box(renderer, menu.Aspect, center, size + new float2(4 * menu.Px), CardEdge);
        Gradient(renderer, menu.Aspect, center, size, Stage, Floor, 12);
        return (center, size);
    }

    /// <summary>Under the stage: its name, large, with its status at the end of the line, and what it is.</summary>
    /// <returns>The height to carry on writing at.</returns>
    internal static float DrawDetailHeading(Renderer renderer, in MenuCase menu, string name, string kind, string status, float3 statusColor)
    {
        var (aspect, _, unit, _, _, _) = menu;
        var detailLeft = menu.DetailLeft;
        var y = menu.BodyTop - menu.BodyHeight * menu.StageFraction - MenuCase.Margin * (menu.StageFraction > 0f ? 1.2f : 0.2f);
        var nameSize = unit * 3f;
        Text(renderer, aspect, name.ToUpperInvariant(), new float2(detailLeft, y), nameSize, Ink);
        var statusWidth = TextWidth(status, unit) + 8 * unit;
        var statusCenter = new float2(menu.DetailRight - statusWidth / 2, y - BlockFont.Height * nameSize / 2);
        Box(renderer, aspect, statusCenter, new float2(statusWidth, (BlockFont.Height + 6) * unit), statusColor);
        Text(renderer, aspect, status, new float2(statusCenter.X - statusWidth / 2 + 4 * unit, statusCenter.Y + BlockFont.Height * unit / 2), unit, Ink);
        y -= BlockFont.Height * nameSize + 4 * unit;
        Text(renderer, aspect, kind, new float2(detailLeft, y), unit, Amber);
        return y - (BlockFont.Height + 8) * unit;
    }

    /// <summary>Where the values start, in the details' rows of a label and a value.</summary>
    internal static float DetailValueLeft(in MenuCase menu) => menu.DetailLeft + TextWidth("ACCURACY", menu.Unit) + 8 * menu.Unit;

    /// <summary>A gun's bars and specifications, from <paramref name="y"/> down as far as the details go.</summary>
    internal static void DrawNumbers(Renderer renderer, in MenuCase menu, GunStats stats, float y)
    {
        var (aspect, px, unit, _, _, bodyBottom) = menu;
        var (detailLeft, detailWidth) = (menu.DetailLeft, menu.DetailWidth);
        var labelWidth = TextWidth("ACCURACY", unit) + 8 * unit;
        var valueWidth = TextWidth("8 X 9,999 J", unit);
        var barLeft = detailLeft + labelWidth;
        var barWidth = detailWidth - labelWidth - valueWidth - 6 * unit;
        var barHeight = 5 * unit;
        var energy = stats.Energy * stats.Pellets;
        var moa = 2f * stats.AimedSpread * 3437.75f;
        var rate = stats.FullAuto ? $"{stats.RoundsPerMinute:0} RPM" : stats.Action.ToUpperInvariant();
        foreach (var (name, fill, value) in (ReadOnlySpan<(string, float, string)>)
                 [
                     ("POWER", float.Sqrt(energy / 15300f), stats.Pellets > 1 ? $"{stats.Pellets} X {stats.Energy:0} J" : string.Format(CultureInfo.InvariantCulture, "{0:#,0} J", stats.Energy)),
                     ("FIRE RATE", stats.RoundsPerMinute / 1000f * (stats.FullAuto ? 1f : 0.5f), rate),
                     ("ACCURACY", 1f - float.Log(stats.AimedSpread / 0.0002f) / float.Log(0.035f / 0.0002f), moa < 10f ? $"{moa:0.0} MOA" : $"{moa:0} MOA"),
                     ("VELOCITY", stats.Velocity / 950f, $"{stats.Velocity:0} M/S"),
                     ("MOBILITY", (stats.Mobility - 0.5f) / 0.5f, $"{stats.Mobility * 100f:0}%"),
                 ])
        {
            Text(renderer, aspect, name, new float2(detailLeft, y), unit, Dim);
            var barY = y - BlockFont.Height * unit / 2;
            Box(renderer, aspect, new float2(barLeft + barWidth / 2, barY), new float2(barWidth, barHeight), Track);
            var f = float.Clamp(fill, 0.04f, 1f);
            Box(renderer, aspect, new float2(barLeft + barWidth * f / 2, barY), new float2(barWidth * f, barHeight), float3.Lerp(AmberDim, Amber, f));
            for (var tick = 1; tick < 5; tick++)   // quarter marks
                Box(renderer, aspect, new float2(barLeft + barWidth * tick / 5f, barY), new float2(2 * px, barHeight), Panel);
            Text(renderer, aspect, value, new float2(menu.DetailRight - TextWidth(value, unit), y), unit, Ink);
            y -= (BlockFont.Height + 7) * unit;
        }

        // What it takes, how it feeds, how it fires, how it's aimed.
        y -= 3 * unit;
        Box(renderer, aspect, new float2(detailLeft + detailWidth / 2, y + 4 * unit), new float2(detailWidth, px * 2), CardEdge);
        y -= 4 * unit;
        foreach (var (name, value) in (ReadOnlySpan<(string, string)>)
                 [("CALIBRE", stats.Calibre), ("FEED", stats.Feed), ("ACTION", stats.Action), ("SIGHTS", stats.Optic + (stats.Suppressed ? "  -  suppressed" : ""))])
        {
            if (y - BlockFont.Height * unit < bodyBottom)
                break;
            Text(renderer, aspect, name, new float2(detailLeft, y), unit, Dim);
            Text(renderer, aspect, value, new float2(barLeft, y), unit, Ink);
            y -= (BlockFont.Height + 6) * unit;
        }
    }

    /// <summary>
    /// The gun, side on (muzzle to the right), turned by <paramref name="yaw"/> and tipped towards you by
    /// <paramref name="tilt"/>, filling <paramref name="fill"/> of the box at <paramref name="center"/>.
    /// </summary>
    internal void DrawPicture(Renderer renderer, int2 framebufferSize, float aspect, float3 eye, Model model, float2 center, float2 size,
        float yaw, float tilt, float fill)
    {
        // With an optic fitted, that in place of its own (and the riser under it, on a gun with no rail).
        var optic = catalog.FirstOrDefault(e => e.Model == model)?.Gun is GunBase { Optic: { } fitted } ? fitted : null;
        var (parts, bounds) = Picture(model, optic is null);
        if (optic is not null)
        {
            var on = Optics.Bounds(optic.Parts).Transformed(optic.Placement);
            bounds = new Aabb(float3.Min(bounds.Min, on.Min), float3.Max(bounds.Max, on.Max));
        }
        DrawPicture(renderer, framebufferSize, aspect, eye, bounds, world =>
        {
            var tint = new float3(0.50f, 0.61f, 0.76f);
            foreach (var part in parts)
                renderer.DrawPart(part, world, tint);
            if (optic is null)
                return;
            foreach (var part in optic.Parts)
                renderer.DrawPart(part, optic.PartPlacement(part, magnified: false) * world, tint);
            if (optic.Block is { } block)
                foreach (var part in optic.BlockParts)
                    renderer.DrawPart(part, block * world, tint);
        }, center, size, yaw, tilt, fill);
    }

    /// <summary>A picture of anything <paramref name="draw"/> draws with the matrix it's given, framed on <paramref name="bounds"/>.</summary>
    internal static void DrawPicture(Renderer renderer, int2 framebufferSize, float aspect, float3 eye, Aabb bounds, Action<float4x4> draw, float2 center, float2 size,
        float yaw, float tilt, float fill)
    {
        // The box in pixels (from the bottom left).
        var x0 = ((center.X - size.X / 2) / aspect + 1f) / 2f * framebufferSize.X;
        var x1 = ((center.X + size.X / 2) / aspect + 1f) / 2f * framebufferSize.X;
        var y0 = (center.Y - size.Y / 2 + 1f) / 2f * framebufferSize.Y;
        var y1 = (center.Y + size.Y / 2 + 1f) / 2f * framebufferSize.Y;
        var rect = new int4((int)float.Ceiling(x0), (int)float.Ceiling(y0), (int)(x1 - float.Ceiling(x0)), (int)(y1 - float.Ceiling(y0)));
        if (rect.Z < 4 || rect.W < 4)
            return;

        var extent = bounds.Size;
        var camera = Camera.Default with { Position = float3.Zero, FieldOfViewDegrees = 20f, NearPlane = 0.02f, FarPlane = 50f };
        var boxAspect = (float)rect.Z / rect.W;
        var halfTan = float.Tan(float.DegreesToRadians(camera.FieldOfViewDegrees) / 2f);
        // Far enough back that its length fits across and its height up and down (allowing for the turn).
        var across = extent.X * float.Abs(float.Cos(yaw)) + extent.Z * float.Abs(float.Sin(yaw));
        var upDown = extent.Y + extent.Z * float.Abs(float.Sin(tilt));
        var distance = float.Max(across / (2f * halfTan * boxAspect), upDown / (2f * halfTan)) / fill + extent.Z;

        var world = float4x4.CreateTranslation(-bounds.Center)
            * float4x4.CreateRotationY(yaw)
            * float4x4.CreateRotationX(tilt)
            * float4x4.CreateTranslation(0f, 0f, -distance);
        renderer.BeginInset(rect, camera.View * camera.Projection(boxAspect), eye);
        float4x4.Invert(world, out var toModel);
        renderer.SetTextureSpace(toModel);
        draw(world);
        renderer.EndInset(framebufferSize);
    }

    /// <summary>A stage for a picture: <paramref name="light"/> at the top fading down to <paramref name="floor"/>, in bands.</summary>
    private static void Gradient(Renderer renderer, float aspect, float2 center, float2 size, float3 light, float3 floor, int bands)
    {
        var bandHeight = size.Y / bands;
        var top = center.Y + size.Y / 2;
        for (var b = 0; b < bands; b++)
        {
            var t = (b + 0.5f) / bands;
            Box(renderer, aspect, new float2(center.X, top - (b + 0.5f) * bandHeight), new float2(size.X, bandHeight * 1.02f), float3.Lerp(light, floor, t * t));
        }
    }

    internal static float TextWidth(string text, float pixel) => (text.Length * BlockFont.Advance - 1) * pixel;

    internal static void Text(Renderer renderer, float aspect, string text, float2 topLeft, float pixel, float3 color) =>
        PcTerminal.Text(renderer, aspect, text, topLeft, pixel, color);

    internal static void Box(Renderer renderer, float aspect, float2 center, float2 size, float3 color) =>
        PcTerminal.Rect(renderer, aspect, center, size, color);
}

/// <summary>
/// The mouse on a screen of cards (a gun locker, the inventory): which card is under the pointer, clicks and
/// double-clicks on one, and dragging one onto another. The screen fills <see cref="Boxes"/> as it draws them (each
/// a centre and a size), and the next frame's <see cref="Update"/> tests the pointer against them.
/// </summary>
internal sealed class CardMouse
{
    private const float DragStart = 0.02f;        // how far the pointer moves, held down on a card, before it's a drag
    private const double DoubleClickTime = 0.35;  // seconds

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public enum Event { None, Click, DoubleClick, Drop }

    /// <summary>The cards where they were last drawn: centre (X, Y) and size (Z, W), in the overlay's square space.</summary>
    public List<float4> Boxes { get; } = [];

    /// <summary>The card under the pointer (an index into <see cref="Boxes"/>), or -1.</summary>
    public int Hovered { get; private set; } = -1;

    /// <summary>The card the button went down on (while it's held), or -1.</summary>
    public int Held { get; private set; } = -1;

    /// <summary>The held card has been pulled away: it's being dragged.</summary>
    public bool Dragging { get; private set; }

    public float2 Cursor { get; private set; }

    private float2 _pressAt;
    private int _lastClick = -1;
    private double _lastClickTime;

    public static float4 Box(float2 topLeft, float2 size) => new(topLeft.X + size.X / 2, topLeft.Y - size.Y / 2, size.X, size.Y);

    public static bool Inside(float2 p, float4 box) => float.Abs(p.X - box.X) <= box.Z / 2 && float.Abs(p.Y - box.Y) <= box.W / 2;

    /// <summary>
    /// This frame's mouse. A click (on the button going down) and a double-click name the card in <c>From</c>; a drop,
    /// the card dragged (<c>From</c>) and the one it was let go over (<c>To</c>, -1 for none).
    /// </summary>
    /// <param name="canDrag">Whether a card can be picked up (an empty one can't).</param>
    public (Event What, int From, int To) Update(Input input, Func<int, bool> canDrag)
    {
        if (!input.UiCursor)
        {
            Hovered = Held = -1;
            Dragging = false;
            return (Event.None, -1, -1);
        }

        Cursor = input.CursorSquare;
        Hovered = -1;
        for (var i = 0; i < Boxes.Count; i++)
            if (Boxes[i].Z > 0 && Inside(Cursor, Boxes[i]))
                Hovered = i;

        var result = (Event.None, -1, -1);
        if (input.WasPressed(GlfwMouseButton.Left) && Hovered >= 0)
        {
            var now = Clock.Elapsed.TotalSeconds;
            var twice = Hovered == _lastClick && now - _lastClickTime < DoubleClickTime;
            _lastClick = twice ? -1 : Hovered;
            _lastClickTime = now;
            if (twice)
            {
                Held = -1;
                Dragging = false;
                return (Event.DoubleClick, Hovered, Hovered);
            }
            result = (Event.Click, Hovered, Hovered);
            if (canDrag(Hovered))
            {
                Held = Hovered;
                _pressAt = Cursor;
                Dragging = false;
            }
        }

        var down = input.IsDown(GlfwMouseButton.Left);
        if (Held >= 0 && !Dragging && down)
        {
            var moved = Cursor - _pressAt;
            if (moved.X * moved.X + moved.Y * moved.Y > DragStart * DragStart)
            {
                Dragging = true;
                _lastClick = -1;   // (a drag isn't half of a double-click)
            }
        }
        if (Held >= 0 && !down)
        {
            if (Dragging)
                result = (Event.Drop, Held, Hovered);
            Held = -1;
            Dragging = false;
        }
        return result;
    }
}
