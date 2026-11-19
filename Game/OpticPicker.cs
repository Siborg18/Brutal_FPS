using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The SCOPE button on a gun's large picture (in the gun locker and on the raid loadout): it says what's fitted, and a
/// click on it opens the list of what the gun can take, each with its picture and what it does. A click on one fits it
/// (and the picture shows it); a click anywhere else shuts the list. Optics are only changed here, not in the field.
/// </summary>
internal sealed class OpticPicker
{
    private float4 _button;                                 // centre and size, in the overlay's square space (zero: none)
    private readonly List<(float4 Box, OpticKind Kind)> _rows = [];
    private IGun? _gun;
    private string _standard = "";
    private float2 _cursor;
    private bool _cursorShown;

    public bool IsOpen { get; private set; }

    // (Debug: BRUTALFPS_OPTICS=open opens the list on the first gun it's drawn for, for screenshots.)
    private bool _debugOpen = Environment.GetEnvironmentVariable("BRUTALFPS_OPTICS") == "open";

    public void Close() => IsOpen = false;

    /// <summary>This frame's mouse. True if the click was the picker's (the screen under it shouldn't act on it too).</summary>
    public bool Update(Input input)
    {
        // The pointer: a screen in play's (the gun locker), or the free one before the game takes the mouse (the raid loadout).
        _cursorShown = input.UiCursor || !input.MouseCaptured;
        if (!_cursorShown)
            return false;
        _cursor = input.CursorSquare;
        if (!input.WasPressed(GlfwMouseButton.Left))
            return IsOpen && Over(_cursor);
        if (IsOpen)
        {
            foreach (var (box, kind) in _rows)
                if (CardMouse.Inside(_cursor, box) && _gun is not null)
                {
                    if (kind != Optics.FittedTo(_gun))
                        Optics.Fit(_gun, kind);
                    break;
                }
            IsOpen = false;
            return true;   // (a click anywhere else just shuts it)
        }
        if (_gun is not null && _button.Z > 0 && CardMouse.Inside(_cursor, _button))
        {
            IsOpen = true;
            return true;
        }
        return false;
    }

    private bool Over(float2 p) => CardMouse.Inside(p, _button) || _rows.Any(r => CardMouse.Inside(p, r.Box));

    /// <summary>The button, along the bottom of the picture's stage: what's fitted, or that the gun takes none.</summary>
    public void DrawButton(Renderer renderer, in GunLocker.MenuCase menu, float2 stageCenter, float2 stageSize, IGun? gun, string standard)
    {
        var (aspect, px, unit, _, _, _) = menu;
        if (gun != _gun)
            IsOpen = false;
        (_gun, _standard) = (gun, standard);
        _button = default;
        if (gun is null)
            return;
        var choices = Optics.Choices(gun);
        var takes = choices.Count > 1;
        var label = takes ? $"SCOPE: {Optics.NameOf(Optics.FittedTo(gun), standard).ToUpperInvariant()}" : "NO OPTICS FIT";
        var hint = takes ? (IsOpen ? "  -" : "  +") : "";
        var size = new float2(GunLocker.TextWidth(label + hint, unit) + 12 * unit, (BlockFont.Height + 8) * unit);
        var center = new float2(stageCenter.X - stageSize.X / 2 + size.X / 2 + 4 * unit, stageCenter.Y - stageSize.Y / 2 + size.Y / 2 + 4 * unit);
        var hovered = takes && _cursorShown && CardMouse.Inside(_cursor, new float4(center, size));
        GunLocker.Box(renderer, aspect, center, size + new float2((IsOpen || hovered ? 6 : 4) * px), takes ? (IsOpen || hovered ? GunLocker.Amber : GunLocker.AmberDim) : GunLocker.CardEdge);
        GunLocker.Box(renderer, aspect, center, size, IsOpen ? GunLocker.AmberDim * 0.6f : GunLocker.Panel);
        GunLocker.Text(renderer, aspect, label + hint, new float2(center.X - size.X / 2 + 6 * unit, center.Y + BlockFont.Height * unit / 2), unit, takes ? GunLocker.Ink : GunLocker.Dim);
        if (takes)
            _button = new float4(center, size);
        if (takes && _debugOpen)
            (IsOpen, _debugOpen) = (true, false);
    }

    /// <summary>The list, if it's open: up from the button, over whatever's there. Call after the screen's pictures.</summary>
    public void DrawList(Renderer renderer, int2 framebufferSize, in GunLocker.MenuCase menu, float3 eye)
    {
        _rows.Clear();
        if (!IsOpen || _gun is not { } gun || _button.Z <= 0)
            return;
        var (aspect, px, unit, _, _, _) = menu;
        var choices = Optics.Choices(gun);
        var fitted = Optics.FittedTo(gun);
        var rowHeight = (2 * BlockFont.Height + 16) * unit;
        var pictureWidth = rowHeight * 2.4f;
        var textWidth = choices.Max(k => float.Max(GunLocker.TextWidth(Optics.NameOf(k, _standard).ToUpperInvariant() + "  FITTED", unit),
            GunLocker.TextWidth(Optics.NoteOf(gun, k), unit)));
        var width = pictureWidth + textWidth + 16 * unit;
        var height = choices.Count * rowHeight + 2 * unit * (choices.Count + 1);
        var left = _button.X - _button.Z / 2;
        var bottom = _button.Y + _button.W / 2 + 4 * unit;
        bottom = float.Min(bottom, menu.BodyTop - height);
        left = float.Min(left, menu.Right - MenuMargin - width);
        var center = new float2(left + width / 2, bottom + height / 2);
        GunLocker.Box(renderer, aspect, center, new float2(width, height) + new float2(6 * px), GunLocker.Amber);
        GunLocker.Box(renderer, aspect, center, new float2(width, height), GunLocker.Panel);

        var pictures = new List<(OpticKind Kind, float2 Center, float2 Size)>();
        var y = bottom + height - 2 * unit;
        foreach (var kind in choices)
        {
            var rowCenter = new float2(center.X, y - rowHeight / 2);
            var rowSize = new float2(width - 4 * unit, rowHeight);
            var box = new float4(rowCenter, rowSize);
            _rows.Add((box, kind));
            var on = kind == fitted;
            var hovered = _cursorShown && CardMouse.Inside(_cursor, box);
            GunLocker.Box(renderer, aspect, rowCenter, rowSize, on ? GunLocker.AmberDim * 0.55f : hovered ? GunLocker.Card * 1.6f : GunLocker.Card);
            var pictureCenter = new float2(left + 2 * unit + pictureWidth / 2, rowCenter.Y);
            GunLocker.Box(renderer, aspect, pictureCenter, new float2(pictureWidth, rowHeight), GunLocker.Card * 0.8f);
            pictures.Add((kind, pictureCenter, new float2(pictureWidth, rowHeight)));
            var textLeft = left + pictureWidth + 10 * unit;
            var name = Optics.NameOf(kind, _standard).ToUpperInvariant();
            GunLocker.Text(renderer, aspect, name, new float2(textLeft, y - 5 * unit), unit, on || hovered ? GunLocker.Amber : GunLocker.Ink);
            if (on)
                GunLocker.Text(renderer, aspect, "FITTED", new float2(textLeft + GunLocker.TextWidth(name + "  ", unit), y - 5 * unit), unit, GunLocker.Green);
            GunLocker.Text(renderer, aspect, Optics.NoteOf(gun, kind), new float2(textLeft, y - (BlockFont.Height + 10) * unit), unit, GunLocker.Dim);
            y -= rowHeight + 2 * unit;
        }

        // Each optic's picture (its own sights: the gun's, as it comes).
        foreach (var (kind, at, size) in pictures)
        {
            if (Optics.Make(gun.Name, kind) is not { Parts.Length: > 0 } optic)
            {
                var text = "AS ISSUED";
                GunLocker.Text(renderer, aspect, text, new float2(at.X - GunLocker.TextWidth(text, unit) / 2, at.Y + BlockFont.Height * unit / 2), unit, GunLocker.Dim);
                continue;
            }
            var bounds = Optics.Bounds(optic.Parts);
            GunLocker.DrawPicture(renderer, framebufferSize, aspect, eye, bounds, world =>
            {
                foreach (var part in optic.Parts)
                    renderer.DrawPart(part, (FittedOptic.IsMagnifier(part) ? FittedOptic.MagnifierAside : float4x4.Identity) * world, new float3(0.50f, 0.61f, 0.76f));
            }, at, size, -0.3f, 0.12f, 0.8f);
        }
    }

    private const float MenuMargin = GunLocker.MenuCase.Margin;
}
