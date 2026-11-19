using System.Diagnostics;
using System.Globalization;
using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// What you're carrying: I brings it up anywhere, drawn like a gun locker, each thing on a card with its picture (its own
/// model, drawn live): your ten number keys (1 to 9, then 0) with the gun on each, then your flashbangs (and the gun in
/// your hands, if it's on no number). Click a card (or use the arrow keys or the mouse wheel) to pick it and it's shown
/// large on the right with its numbers. Drag a gun onto another number to move it there (swapping with whatever was
/// there), or pick it and press a number key; double-click or Enter takes a gun in your hands. I or Esc shuts it. The
/// world carries on behind it, and your hands are off your gun while it's up.
/// </summary>
public sealed class Inventory(GunLocker locker, IReadOnlyList<LockerEntry> catalog, IGun?[] slots, Flashbangs flashbangs)
{
    private const int Rows = 4;
    private const int Cells = 16;   // (four to a row: GunLocker's grid, so the cards are the size of the locker's)

    private static readonly GlfwKey[] Digits =
    [
        GlfwKey.Number0, GlfwKey.Number1, GlfwKey.Number2, GlfwKey.Number3, GlfwKey.Number4,
        GlfwKey.Number5, GlfwKey.Number6, GlfwKey.Number7, GlfwKey.Number8, GlfwKey.Number9,
    ];

    /// <summary>A flashbang as its picture has it, stood up on its base (its length along Y, the fuse on top).</summary>
    private static readonly Aabb CanBounds = new(new float3(-0.022f, -0.055f, -0.022f), new float3(0.022f, 0.07f, 0.022f));

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    /// <summary>A card: a number key (<paramref name="Number"/>, with the gun on it or none), the flashbangs, or the gun in hand that's on no number.</summary>
    private readonly record struct Cell(int Number, LockerEntry? Entry, bool Flashbang = false);

    private readonly List<Cell> _cells = [];
    private readonly CardMouse _mouse = new();
    private int _selected;
    private bool _forced;
    private bool _gunsLocked;

    public bool IsOpen { get; private set; }

    private Cell Picked => _cells[_selected];

    /// <summary>Debug: BRUTALFPS_INVENTORY=open, for screenshots: up from the start, whatever you're doing.</summary>
    public void ForceOpen()
    {
        IsOpen = true;
        _forced = true;
    }

    public bool Close()
    {
        if (!IsOpen)
            return false;
        IsOpen = false;
        _forced = false;
        Log.Write("[INVENTORY] Shut");
        return true;
    }

    /// <summary>The gun to bring up, if Enter was pressed on one (or it was double-clicked).</summary>
    /// <param name="canOpen">The player is free to look (playing, alive, not in the intro). It shuts if they stop being.</param>
    /// <param name="otherScreenUp">A PC, keypad or gun locker is up. It gives way to one that's brought up over it.</param>
    /// <param name="gunsLocked">No guns yet (the start of the intro): the number keys don't work, so they aren't carried.</param>
    public IGun? Update(Input input, bool canOpen, bool otherScreenUp, IGun inHand, bool gunsLocked)
    {
        if (!IsOpen)
        {
            if (!canOpen || otherScreenUp || !input.WasPressed(GlfwKey.I))
                return null;
            IsOpen = true;
            Log.Write("[INVENTORY] Open");
            Refresh(inHand, gunsLocked);
            _selected = _cells.FindIndex(c => c.Entry?.Gun == inHand) is var held and >= 0 ? held : 0;
            return null;
        }

        if ((!canOpen || otherScreenUp) && !_forced)
        {
            Close();
            return null;
        }

        if (input.WasPressed(GlfwKey.I))
        {
            Close();
            return null;
        }

        Refresh(inHand, gunsLocked);
        _selected = GunLocker.Pick(input, _selected, _cells.Count);

        // The mouse: click to pick, drag a gun onto another number to move it, double-click to take it.
        var (what, from, to) = _mouse.Update(input, i => !gunsLocked && i < _cells.Count && _cells[i].Entry is not null);
        switch (what)
        {
            case CardMouse.Event.Click when from < _cells.Count:
                _selected = from;
                break;
            case CardMouse.Event.DoubleClick when from < _cells.Count && _cells[from].Entry is { } entry && !gunsLocked:
                Close();
                return entry.Gun;
            case CardMouse.Event.Drop when to >= 0 && to < _cells.Count && _cells[to].Number >= 0 && _cells[from].Entry is { } dragged:
                MoveTo(_cells[from], _cells[to].Number, dragged.Gun);
                Refresh(inHand, gunsLocked);
                _selected = to;
                break;
        }

        // A number key puts the gun picked on that number (swapping with whatever was there).
        if (!gunsLocked && Picked.Entry is { } pickedEntry)
        {
            for (var d = 0; d < Digits.Length; d++)
            {
                if (!input.WasPressed(Digits[d]))
                    continue;
                MoveTo(Picked, d, pickedEntry.Gun);
                Refresh(inHand, gunsLocked);
                _selected = _cells.FindIndex(c => c.Number == d);
                break;
            }
        }

        if (input.WasPressed(GlfwKey.Enter) && Picked.Entry is { } chosen && !gunsLocked)
        {
            Close();
            return chosen.Gun;
        }

        return null;
    }

    /// <summary>Puts the gun on <paramref name="cell"/> on <paramref name="number"/>: from another number, a swap; the gun in hand that's on none, in place of what's there.</summary>
    private void MoveTo(Cell cell, int number, IGun gun)
    {
        if (cell.Number >= 0)
            locker.Move(cell.Number, number);
        else
            locker.Put(gun, number);
    }

    /// <summary>
    /// The cards: the ten numbers in the order of the keys (1 to 9, then 0), the flashbangs, and the gun in hand if it's on
    /// none of them. With no guns yet, just the gun in hand (if it's a gun) and the flashbangs.
    /// </summary>
    private void Refresh(IGun inHand, bool gunsLocked)
    {
        _gunsLocked = gunsLocked;
        _cells.Clear();
        if (!gunsLocked)
        {
            for (var k = 1; k <= slots.Length; k++)
            {
                var number = k % slots.Length;
                _cells.Add(new Cell(number, slots[number] is { } gun ? EntryOf(gun) : null));
            }
        }
        _cells.Add(new Cell(-1, null, Flashbang: true));
        if (Array.IndexOf(slots, inHand) < 0 || gunsLocked)
            if (EntryOf(inHand) is { } held)
                _cells.Insert(gunsLocked ? 0 : _cells.Count, new Cell(-1, held));
        _selected = int.Clamp(_selected, 0, _cells.Count - 1);
    }

    private LockerEntry? EntryOf(IGun gun)
    {
        foreach (var entry in catalog)
            if (entry.Gun == gun)
                return entry;
        return null;
    }

    // ------------------------------------------------------------------------------------------------ drawing

    /// <param name="eye">Where the player's eye is: the pictures are lit as the gun in their hands is.</param>
    /// <param name="inHand">The gun they're holding (marked on its card).</param>
    public void Draw(Renderer renderer, int2 framebufferSize, float3 eye, IGun inHand)
    {
        if (!IsOpen || _cells.Count == 0)
            return;
        var count = _cells.Count(c => c.Entry is not null || c.Flashbang);
        var items = count == 1 ? "1 ITEM" : $"{count} ITEMS";
        var picked = Picked;
        var menu = GunLocker.DrawCase(renderer, framebufferSize, "INVENTORY", items, picked.Entry is null
            ? [("CLICK/ARROWS", "pick"), ("I", "shut")]
            : _gunsLocked
                ? [("CLICK/ARROWS", "pick"), ("DOUBLE-CLICK/ENTER", "take it"), ("I", "shut")]
                : [("CLICK/ARROWS", "pick"), ("DRAG/0-9", "move to a number"), ("DOUBLE-CLICK/ENTER", "take it"), ("I", "shut")]);

        var dragged = _mouse.Dragging && _mouse.Held < _cells.Count ? _cells[_mouse.Held].Entry : null;
        var pictures = new List<(LockerEntry? Entry, float2 Center, float2 Size, bool Large)>();
        _mouse.Boxes.Clear();
        for (var cell = 0; cell < Cells; cell++)
        {
            var (corner, size) = GunLocker.CardAt(menu, cell, Rows);
            if (cell >= _cells.Count)
            {
                var middle = corner + new float2(size.X / 2, -size.Y / 2);
                GunLocker.Box(renderer, menu.Aspect, middle, size + new float2(4 * menu.Px), GunLocker.CardEdge * 0.6f);
                GunLocker.Box(renderer, menu.Aspect, middle, size, GunLocker.Card * 0.6f);
                continue;
            }

            _mouse.Boxes.Add(CardMouse.Box(corner, size));
            var c = _cells[cell];
            var isPicked = cell == _selected;
            var hovered = _mouse.Hovered == cell;
            var target = dragged is not null && hovered && c.Number >= 0;
            var number = c.Number >= 0 ? c.Number.ToString(CultureInfo.InvariantCulture) : null;
            if (c.Flashbang)
            {
                var (center, pictureSize) = GunLocker.DrawCard(renderer, menu, corner, size, "Flashbang", "G", FlashbangsLeft, isPicked, false);
                pictures.Add((null, center, pictureSize, false));
            }
            else if (c.Entry is { } entry)
            {
                var (center, pictureSize) = GunLocker.DrawCard(renderer, menu, corner, size, entry.Gun.Name, number, Rounds(entry.Gun), isPicked, entry.Gun == inHand, false, target);
                pictures.Add((entry, center, pictureSize, false));
            }
            else
            {
                GunLocker.DrawEmptySlot(renderer, menu, corner, size, number!, isPicked, target);
            }
        }

        // The one picked, large on the right.
        var (stageCenter, stageSize) = GunLocker.DrawDetailStage(renderer, menu);
        var valueLeft = GunLocker.DetailValueLeft(menu);
        if (picked.Flashbang)
        {
            pictures.Add((null, stageCenter, stageSize, true));
            DrawFlashbangDetails(renderer, menu, valueLeft);
        }
        else if (picked.Entry is { } entry)
        {
            pictures.Add((entry, stageCenter, stageSize, true));
            var status = entry.Gun == inHand ? "IN HAND" : $"ON {picked.Number}";
            var y = GunLocker.DrawDetailHeading(renderer, menu, entry.Gun.Name, entry.Stats.Kind, status, entry.Gun == inHand ? GunLocker.Green * 0.5f : GunLocker.AmberDim);
            GunLocker.Text(renderer, menu.Aspect, "LOADED", new float2(menu.DetailLeft, y), menu.Unit, GunLocker.Dim);
            GunLocker.Text(renderer, menu.Aspect, $"{Rounds(entry.Gun)} rounds", new float2(valueLeft, y), menu.Unit, GunLocker.Ink);
            GunLocker.DrawNumbers(renderer, menu, GunLocker.Fitted(entry), y - (BlockFont.Height + 7) * menu.Unit);
        }
        else
        {
            var y = GunLocker.DrawDetailHeading(renderer, menu, "Empty", $"Nothing on {picked.Number}", $"KEY {picked.Number}", GunLocker.CardEdge);
            GunLocker.Text(renderer, menu.Aspect, "Drag a gun here to put it on this number.", new float2(menu.DetailLeft, y), menu.Unit, GunLocker.Dim);
            GunLocker.Text(renderer, menu.Aspect, "More guns are at a gun locker.", new float2(menu.DetailLeft, y - (BlockFont.Height + 6) * menu.Unit), menu.Unit, GunLocker.Dim);
        }

        // The pictures last: each is drawn into its own patch of the screen, over its card.
        var time = (float)Clock.Elapsed.TotalSeconds;
        foreach (var (entry, center, size, large) in pictures)
        {
            var yaw = large ? -0.32f + 0.55f * float.Sin(time * 0.6f) : -0.3f;
            if (entry is null)
            {
                GunLocker.DrawPicture(renderer, framebufferSize, menu.Aspect, eye, CanBounds,
                    world => flashbangs.DrawCan(renderer, float4x4.CreateRotationX(-float.Pi / 2f) * world, bodyBrightness: 3f),
                    center, size, yaw, 0.3f, large ? 0.72f : 0.8f);
            }
            else if (entry.Model is { } model)
            {
                locker.DrawPicture(renderer, framebufferSize, menu.Aspect, eye, model, center, size, yaw, large ? 0.16f : 0.12f, large ? 0.82f : 0.86f);
            }
        }

        // Over everything, the gun being dragged, under the pointer.
        if (dragged is not null)
            locker.DrawDragged(renderer, framebufferSize, menu, eye, dragged.Gun.Name, dragged.Model, _mouse.Cursor, GunLocker.CardAt(menu, 0, Rows).Size);
    }

    private void DrawFlashbangDetails(Renderer renderer, in GunLocker.MenuCase menu, float valueLeft)
    {
        var (aspect, _, unit, _, _, _) = menu;
        var left = flashbangs.Infinite || flashbangs.Carried > 0;
        var y = GunLocker.DrawDetailHeading(renderer, menu, "Flashbang", "Stun grenade", left ? "READY" : "NONE LEFT", left ? GunLocker.AmberDim : GunLocker.CardEdge);

        // One pip for each you can carry, lit for each you've got.
        GunLocker.Text(renderer, aspect, "CARRIED", new float2(menu.DetailLeft, y), unit, GunLocker.Dim);
        if (flashbangs.Infinite)
        {
            GunLocker.Text(renderer, aspect, "Unlimited", new float2(valueLeft, y), unit, GunLocker.Ink);
        }
        else
        {
            var pip = new float2(12 * unit, 6 * unit);
            for (var i = 0; i < flashbangs.Stock; i++)
                GunLocker.Box(renderer, aspect, new float2(valueLeft + pip.X / 2 + i * (pip.X + 3 * unit), y - BlockFont.Height * unit / 2), pip,
                    i < flashbangs.Carried ? GunLocker.Amber : GunLocker.Track);
            var text = $"{flashbangs.Carried} / {flashbangs.Stock}";
            GunLocker.Text(renderer, aspect, text, new float2(menu.DetailRight - GunLocker.TextWidth(text, unit), y), unit, GunLocker.Ink);
        }
        y -= (BlockFont.Height + 9) * unit;

        foreach (var (name, value) in (ReadOnlySpan<(string, string)>)
                 [
                     ("FUSE", string.Format(CultureInfo.InvariantCulture, "{0:0.0} seconds from the throw", Flashbangs.Fuse)),
                     ("BLINDS", $"Whoever can see it, out to {Flashbangs.BlindReach:0} m"),
                     ("DEAFENS", $"Whoever is near, out to {Flashbangs.DeafReach:0} m"),
                     ("DAMAGE", "None"),
                     ("THROW", "Hold G to plot the arc, let go"),
                     ("RESTOCK", "A fresh set each respawn"),
                 ])
        {
            if (y - BlockFont.Height * unit < menu.BodyBottom)
                break;
            GunLocker.Text(renderer, aspect, name, new float2(menu.DetailLeft, y), unit, GunLocker.Dim);
            GunLocker.Text(renderer, aspect, value, new float2(valueLeft, y), unit, GunLocker.Ink);
            y -= (BlockFont.Height + 6) * unit;
        }
    }

    private static string Rounds(IGun gun) => $"{gun.AmmoInMag}{(gun.Chambered ? "+1" : "")}/{gun.Capacity}";

    private string FlashbangsLeft => flashbangs.Infinite ? "INF" : $"X{flashbangs.Carried}";
}
