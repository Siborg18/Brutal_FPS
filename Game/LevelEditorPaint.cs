using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The level editor's paint tool: a texture set and a colour for each face of a box, each part of a Blender model (its
/// Blender material slots), or a code-built mesh as a whole. Only what's being painted is drawn, and the view turns round it.
/// <list type="bullet">
/// <item>P with something picked in the level paints that one only (a piece, a desk, all of it).</item>
/// <item>A click on a model in the asset list (let go without dragging it out) paints every copy of that model in
/// every level (<see cref="Model.SetRepaint"/>), shown on one of them.</item>
/// </list>
/// Down the left: the colour (a swatch, or any colour by its red, green and blue sliders) and the texture set to paint
/// with; either can be KEEP (left as it is) or AS BUILT (put back). Click a face (or part) to paint it with both;
/// Shift+click paints all of them, Alt+click picks up what a face is painted with. Right mouse turns the view round it,
/// the wheel goes closer and further; Esc, P or DONE goes back. Each paint is a step to undo, and SAVE CHANGES keeps
/// it (Assets/Levels/level_edits.json: Game/LevelEditing.cs).
/// </summary>
public sealed partial class LevelEditor
{
    private bool _painting;
    private readonly List<EditRef> _paintItems = [];   // painting one object: its items (props, visible boxes)
    private string? _paintModel;                         // painting every copy of this model
    private Model? _paintAsset;
    private float4x4 _paintPose;                          // where the model is shown
    private float3 _paintTint;
    private int _paintCopies;
    private Camera _cameraBeforePaint;
    private float3 _orbitCenter;
    private float _orbitDistance;
    private (EditRef Item, int Slot)? _paintHover;
    private bool _orbiting;

    // The brushes: the texture set (a row of the list: KEEP, AS BUILT, then Level.PaintTextures) and the colour.
    private enum ColorBrush { Keep, AsBuilt, Set }
    private int _texture = KeepRow;
    private float _textureScroll;
    private ColorBrush _colorBrush = ColorBrush.Keep;
    private float3 _brushColor = new(0.6f, 0.6f, 0.6f);
    private int _sliderDrag = -1;                          // the colour slider being dragged (0 red, 1 green, 2 blue)

    private const int KeepRow = 0, AsBuiltRow = 1;

    /// <summary>The swatches: greys, then the colours the levels are painted in, then more.</summary>
    private static readonly float3[] Swatches =
    [
        new(0.04f, 0.04f, 0.04f), new(0.13f, 0.13f, 0.13f), new(0.24f, 0.24f, 0.24f), new(0.36f, 0.36f, 0.36f),
        new(0.5f, 0.5f, 0.5f), new(0.65f, 0.65f, 0.65f), new(0.8f, 0.8f, 0.8f), new(1f, 1f, 1f),
        new(0.55f, 0.18f, 0.12f), new(0.72f, 0.36f, 0.12f), new(0.74f, 0.60f, 0.14f), new(0.20f, 0.42f, 0.24f),
        new(0.16f, 0.28f, 0.50f), new(0.42f, 0.44f, 0.46f), new(0.75f, 0.74f, 0.70f), new(0.46f, 0.34f, 0.22f),
        new(0.80f, 0.12f, 0.10f), new(0.95f, 0.50f, 0.10f), new(0.95f, 0.85f, 0.20f), new(0.45f, 0.75f, 0.20f),
        new(0.10f, 0.55f, 0.55f), new(0.20f, 0.45f, 0.85f), new(0.45f, 0.25f, 0.65f), new(0.85f, 0.40f, 0.60f),
        new(0.30f, 0.08f, 0.06f), new(0.30f, 0.20f, 0.12f), new(0.60f, 0.48f, 0.34f), new(0.86f, 0.80f, 0.66f),
        new(0.18f, 0.24f, 0.12f), new(0.36f, 0.40f, 0.24f), new(0.08f, 0.12f, 0.24f), new(0.62f, 0.72f, 0.80f),
    ];

    private const int SwatchColumns = 8;

    /// <summary>The paint tool is open: draw only <see cref="DrawPaintSubject"/>.</summary>
    public bool Painting => IsOpen && _painting;

    private static int TextureRows => Level.PaintTextures.Length + 2;

    private static string TextureName(int row) => row switch
    {
        KeepRow => "KEEP",
        AsBuiltRow => "AS BUILT",
        _ => Spaced(Level.PaintTextures[row - 2].ToString()),
    };

    /// <summary>"RustyMetal" as "Rusty metal".</summary>
    private static string Spaced(string name)
    {
        var text = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && text.Length > 0)
                text.Append(' ').Append(char.ToLowerInvariant(c));
            else
                text.Append(c);
        }
        return text.ToString();
    }

    /// <summary>The list row of a texture set (AS BUILT for one not in the list).</summary>
    private static int RowOf(Material texture) => Array.IndexOf(Level.PaintTextures, texture) is >= 0 and var i ? i + 2 : AsBuiltRow;

    private static string ColorText(float3 c) => $"{(int)float.Round(c.X * 255)} {(int)float.Round(c.Y * 255)} {(int)float.Round(c.Z * 255)}";

    private string BrushText()
    {
        var texture = _texture == KeepRow ? null : _texture == AsBuiltRow ? "texture as built" : TextureName(_texture);
        var color = _colorBrush switch { ColorBrush.AsBuilt => "colour as built", ColorBrush.Set => $"colour {ColorText(_brushColor)}", _ => null };
        return texture is null && color is null ? "nothing (pick a colour or a texture)" : string.Join(", ", new[] { texture, color }.Where(s => s is not null));
    }

    /// <summary>P: paints what's picked in the level, that one only.</summary>
    private void StartPaint()
    {
        var items = new HashSet<EditRef>();
        foreach (var item in _selection)
            level.CollectGroup(item, items);
        var paintable = items.Where(i => level.SlotCount(i) > 0).OrderBy(i => (i.IsProp ? 0 : 1, i.Index)).ToList();
        if (paintable.Count == 0)
        {
            Say("Nothing picked that can be painted (collision boxes can't).");
            return;
        }
        var min = new float3(float.MaxValue);
        var max = new float3(float.MinValue);
        foreach (var item in paintable)
        {
            var b = level.BoundsOf(item);
            min = float3.Min(min, b.Min);
            max = float3.Max(max, b.Max);
        }
        _paintItems.Clear();
        _paintItems.AddRange(paintable);
        _paintModel = null;
        _paintAsset = null;
        BeginPaint(new Aabb(min, max), $"Painting {Describe()}: this one only.");
    }

    /// <summary>A click on a model in the asset list: paints every copy of it.</summary>
    private void StartModelPaint(PaletteEntry entry)
    {
        if (entry.Model is not { } name)
        {
            Say("Boxes are painted one at a time: pick one in the level and press P.", 6);
            return;
        }
        Model asset;
        try
        {
            asset = Model.Get(name);
        }
        catch (Exception e)
        {
            Say($"Couldn't load {name} ({e.Message})");
            return;
        }
        var copies = level.CopiesOf(name).ToList();
        _paintCopies = copies.Count;
        if (copies.Count > 0)
        {
            // (Shown where one of them stands, lit as it is there.)
            (_paintPose, _paintTint) = level.PlacementOf(copies.MinBy(c => float3.DistanceSquared(level.BoundsOf(c).Center, _camera.Position)));
        }
        else
        {
            var size = float.Max(1f, float.Max(asset.Bounds.Size.X, float.Max(asset.Bounds.Size.Y, asset.Bounds.Size.Z)));
            _paintPose = float4x4.CreateTranslation(_camera.Position + _camera.Forward * (size * 1.5f + 2f) - asset.Bounds.Center);
            _paintTint = entry.Tint;
        }
        _paintItems.Clear();
        _paintModel = name;
        _paintAsset = asset;
        var many = copies.Count == 1 ? "the 1 copy" : $"all {copies.Count} copies";
        BeginPaint(asset.Bounds.Transformed(_paintPose), $"Painting {entry.Label}: {many} in this level, and in every other.");
    }

    private void BeginPaint(in Aabb bounds, string say)
    {
        _painting = true;
        _cameraBeforePaint = _camera;
        _selection.Clear();
        _hover = null;
        _paintHover = null;
        _drag = DragKind.None;
        _sliderDrag = -1;
        _orbitCenter = bounds.Center;
        _orbitDistance = float.Max(1.5f, float3.Distance(bounds.Min, bounds.Max) * 1.1f);
        _camera.Pitch = float.Clamp(_camera.Pitch, -1.2f, 0.2f);
        PlaceOrbitCamera();
        Say(say, 8);
        Log.Write($"[EDITOR] {say}");
    }

    private void EndPaint()
    {
        _painting = false;
        _camera = _cameraBeforePaint;
        if (_paintModel is null)
            _selection.UnionWith(_paintItems.Where(i => !level.PoseOf(i).Deleted));   // (picked again, as it was)
        _paintItems.Clear();
        _paintModel = null;
        _paintAsset = null;
        _paintHover = null;
        _orbiting = false;
        _sliderDrag = -1;
    }

    private void PlaceOrbitCamera() => _camera.Position = _orbitCenter - _camera.Forward * _orbitDistance;

    // ------------------------------------------------------------------------------------------------
    // The panel down the left: COLOUR (KEEP, AS BUILT, the swatch in use; the swatches; red, green and blue), then TEXTURE.
    // ------------------------------------------------------------------------------------------------

    private float PanelLeft => -_aspect + 0.03f;
    private const float PanelInner = 0.6f;
    private float ColorTop => 1 - TopBar - 0.025f;
    private float ModeRowY => ColorTop - BlockFont.Height * _pixel - 0.045f;   // the middle of KEEP / AS BUILT / the swatch in use
    private const float ModeHeight = 0.055f, SwatchHeight = 0.05f, SliderHeight = 0.04f, SliderGap = 0.012f;
    private float SwatchTop => ModeRowY - ModeHeight / 2 - 0.02f;
    private float SlidersTop => SwatchTop - Swatches.Length / SwatchColumns * SwatchHeight - 0.025f;
    private float TextureHeaderTop => SlidersTop - 3 * (SliderHeight + SliderGap) - 0.02f;
    private float TextureListTop => TextureHeaderTop - BlockFont.Height * _pixel - 0.02f;
    private int TextureVisibleRows() => int.Max(1, (int)((TextureListTop - (-1 + BottomBar) - 0.02f) / RowHeight) - 1);

    private Box2 KeepColorBox => new(new float2(PanelLeft + 0.085f, ModeRowY), new float2(0.17f, ModeHeight));
    private Box2 AsBuiltColorBox => new(new float2(PanelLeft + 0.19f + 0.125f, ModeRowY), new float2(0.25f, ModeHeight));
    private Box2 BrushSwatchBox => new(new float2(PanelLeft + PanelInner - 0.075f, ModeRowY), new float2(0.15f, ModeHeight));

    private Box2 SwatchBox(int i)
    {
        var w = PanelInner / SwatchColumns;
        var (col, row) = (i % SwatchColumns, i / SwatchColumns);
        return new Box2(new float2(PanelLeft + (col + 0.5f) * w, SwatchTop - (row + 0.5f) * SwatchHeight), new float2(w - 0.008f, SwatchHeight - 0.008f));
    }

    /// <summary>A colour slider's bar (0 red, 1 green, 2 blue): from just past its letter to the panel's edge.</summary>
    private Box2 SliderBar(int channel)
    {
        var x0 = PanelLeft + 0.05f;
        var x1 = PanelLeft + PanelInner - 0.1f;   // (room for the number after it)
        return new Box2(new float2((x0 + x1) / 2, SlidersTop - channel * (SliderHeight + SliderGap) - SliderHeight / 2), new float2(x1 - x0, SliderHeight));
    }

    private int? TextureRowAt(float2 p)
    {
        if (!OverPalette(p) || p.Y > TextureListTop)
            return null;
        var row = (int)((TextureListTop - p.Y) / RowHeight);
        var index = row + (int)_textureScroll;
        return row < TextureVisibleRows() && index < TextureRows ? index : null;
    }

    private void SetSlider(int channel, float x)
    {
        var bar = SliderBar(channel);
        var v = float.Clamp((x - (bar.Center.X - bar.Size.X / 2)) / bar.Size.X, 0f, 1f);
        var c = _brushColor;
        _brushColor = channel == 0 ? new float3(v, c.Y, c.Z) : channel == 1 ? new float3(c.X, v, c.Z) : new float3(c.X, c.Y, v);
        _colorBrush = ColorBrush.Set;
    }

    /// <summary>A click in the panel: a colour, a slider, or a texture. True if it was one.</summary>
    private bool ClickPanel(float2 cursor)
    {
        if (Hit(cursor, KeepColorBox))
        {
            _colorBrush = ColorBrush.Keep;
            Say($"Colours are left as they are. Painting with: {BrushText()}");
            return true;
        }
        if (Hit(cursor, AsBuiltColorBox))
        {
            _colorBrush = ColorBrush.AsBuilt;
            Say($"Painting with: {BrushText()}");
            return true;
        }
        if (Hit(cursor, BrushSwatchBox))
        {
            _colorBrush = ColorBrush.Set;
            Say($"Painting with: {BrushText()}");
            return true;
        }
        for (var i = 0; i < Swatches.Length; i++)
        {
            if (Hit(cursor, SwatchBox(i)))
            {
                _brushColor = Swatches[i];
                _colorBrush = ColorBrush.Set;
                Say($"Painting with: {BrushText()}");
                return true;
            }
        }
        for (var channel = 0; channel < 3; channel++)
        {
            var bar = SliderBar(channel);
            if (Hit(cursor, bar with { Size = bar.Size + new float2(0.04f, 0.01f) }))
            {
                _sliderDrag = channel;
                SetSlider(channel, cursor.X);
                return true;
            }
        }
        if (TextureRowAt(cursor) is { } row)
        {
            _texture = row;
            Say(row == KeepRow ? $"Textures are left as they are. Painting with: {BrushText()}" : $"Painting with: {BrushText()}");
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------------------------------------
    // Input
    // ------------------------------------------------------------------------------------------------

    private Exit UpdatePaint(float dt, Input input, float2 cursor, bool ctrl, bool shift, bool alt, bool overUi)
    {
        // A colour slider being dragged.
        if (_sliderDrag >= 0)
        {
            if (input.IsDown(GlfwMouseButton.Left))
            {
                SetSlider(_sliderDrag, cursor.X);
                return Exit.None;
            }
            _sliderDrag = -1;
            Say($"Painting with: {BrushText()}");
        }

        // Right mouse: turn round it.
        if (!_orbiting && input.WasPressed(GlfwMouseButton.Right) && !overUi)
        {
            _orbiting = true;
            input.SetMouseCaptured(true);
        }
        if (_orbiting)
        {
            if (!input.IsDown(GlfwMouseButton.Right))
            {
                _orbiting = false;
                input.SetMouseCaptured(false);
            }
            else
            {
                _camera.Yaw += input.MouseDelta.X * 0.005f;
                _camera.Pitch = float.Clamp(_camera.Pitch - input.MouseDelta.Y * 0.005f, -1.5f, 1.5f);
            }
        }
        // The wheel: closer and further, or down the list of textures.
        if (input.ScrollSteps != 0)
        {
            if (OverPalette(cursor))
                _textureScroll = float.Clamp(_textureScroll - input.ScrollSteps * 3, 0, float.Max(0, TextureRows - TextureVisibleRows()));
            else
                _orbitDistance = float.Clamp(_orbitDistance * float.Pow(0.88f, input.ScrollSteps), 0.3f, 400f);
        }
        PlaceOrbitCamera();

        // What face (or part) the pointer is over.
        _paintHover = null;
        if (!overUi && !_orbiting)
        {
            var ray = RayThrough(cursor);
            if (_paintAsset is { } asset)
            {
                var distance = 1e5f;
                if (Level.RaycastModelPart(asset, _paintPose, _camera.Position, ray, ref distance) is >= 0 and var part)
                    _paintHover = (default, part);
            }
            else if (level.PickSlot(_paintItems, _camera.Position, ray, out var item, out var slot, out _))
            {
                _paintHover = (item, slot);
            }
        }

        if (input.WasPressed(GlfwMouseButton.Left) && overUi)
        {
            if (Hit(cursor, KeepButton))
                return Close(keep: true, input);
            if (Hit(cursor, DiscardButton))
                return Close(keep: false, input);
            if (Hit(cursor, DoneButton))
            {
                EndPaint();
                Say("Done painting");
                return Exit.None;
            }
            ClickPanel(cursor);
        }
        else if (input.WasPressed(GlfwMouseButton.Left) && _paintHover is { } target)
        {
            if (alt)
            {
                _texture = RowOf(SlotTexture(target));
                _brushColor = SlotColor(target);
                _colorBrush = ColorBrush.Set;
                Say($"Picked up {BrushText()}");
            }
            else
            {
                Paint(target, all: shift);
            }
        }

        if (ctrl && input.WasPressed(GlfwKey.Z))
        {
            if (shift)
                Redo();
            else
                Undo(quiet: false);
        }
        else if (ctrl && input.WasPressed(GlfwKey.Y))
        {
            Redo();
        }
        else if (input.WasPressed(GlfwKey.Escape) || input.WasPressed(GlfwKey.P))
        {
            EndPaint();
            Say("Done painting");
        }
        return Exit.None;
    }

    private Box2 DoneButton => new(new float2(_aspect - 0.04f - 0.2f, 1 - TopBar / 2), new float2(0.4f, TopBar - 0.03f));

    /// <summary>What the slot is painted with now.</summary>
    private Material SlotTexture((EditRef Item, int Slot) target) =>
        _paintAsset is { } asset ? asset.Parts[target.Slot].Material.Material : level.TextureOf(target.Item, target.Slot);

    /// <summary>The colour the slot is drawn in now.</summary>
    private float3 SlotColor((EditRef Item, int Slot) target) =>
        _paintAsset is { } asset ? Level.PartColor(asset.Parts[target.Slot].Material, _paintTint) : level.ColorOf(target.Item, target.Slot);

    private string SlotName((EditRef Item, int Slot) target) =>
        _paintAsset is { } asset ? Level.PartName(asset, target.Slot) : level.SlotName(target.Item, target.Slot);

    /// <summary>Paints the slot (or all of them) with the brushes (what's KEEP stays as it is), as a step to undo.</summary>
    private void Paint((EditRef Item, int Slot) target, bool all)
    {
        if (_texture == KeepRow && _colorBrush == ColorBrush.Keep)
        {
            Say("Pick a colour or a texture first (both are KEEP).");
            return;
        }
        var texture = _texture == AsBuiltRow ? -1 : _texture == KeepRow ? 0 : (int)Level.PaintTextures[_texture - 2];
        float3? color = _colorBrush == ColorBrush.Set ? _brushColor : null;
        SlotPaint Painted(SlotPaint? before, int slots)
        {
            var after = before?.Copy() ?? SlotPaint.Empty(slots);
            for (var i = 0; i < after.Count; i++)
            {
                if (!all && i != target.Slot)
                    continue;
                if (_texture != KeepRow)
                    after.Textures[i] = texture;
                if (_colorBrush != ColorBrush.Keep)
                    after.Colors[i] = color;
            }
            return after;
        }
        var what = $"{(all ? "all of it" : SlotName(target))}: {BrushText()}";

        if (_paintModel is { } name && _paintAsset is { } asset)
        {
            var before = Model.RepaintOf(name);
            Model.SetRepaint(name, Painted(before, asset.Parts.Count));
            var now = Model.RepaintOf(name);
            if (before is null ? now is null : before.SameAs(now))
                return;
            _undo.Add(new UndoStep($"Paint {name} {what}", [], ModelPaints: [(name, before, now)]));
            _redo.Clear();
            Say($"Painted every {name.Replace('_', ' ')}, {what}");
            return;
        }

        var changes = new List<(EditRef, SlotPaint?, SlotPaint?)>();
        foreach (var item in all ? _paintItems : new List<EditRef> { target.Item })
        {
            var before = level.PaintOf(item);
            level.SetPaint(item, Painted(before, level.SlotCount(item)));
            var now = level.PaintOf(item);
            if (!(before is null ? now is null : before.SameAs(now)))
                changes.Add((item, before, now));
        }
        if (changes.Count == 0)
            return;
        _undo.Add(new UndoStep($"Paint {what}", [], Paints: [.. changes]));
        _redo.Clear();
        Say($"Painted {what}");
    }

    // ------------------------------------------------------------------------------------------------
    // Drawing
    // ------------------------------------------------------------------------------------------------

    /// <summary>Just what's being painted (in the world's view, in place of the level).</summary>
    public void DrawPaintSubject(Renderer renderer)
    {
        if (_paintAsset is { } asset)
            renderer.DrawModel(asset, _paintPose, _paintTint);
        else
            level.DrawItems(renderer, _paintItems);
    }

    /// <summary>An outline round the face or part under the pointer.</summary>
    private void DrawPaintHover(Renderer renderer)
    {
        if (_paintHover is not { } target)
            return;
        var bounds = _paintAsset is { } asset
            ? asset.Parts[target.Slot].Mesh.Bounds.Transformed(_paintPose)
            : level.SlotBounds(target.Item, target.Slot);
        Outline(renderer, bounds, HoverColor);
    }

    private void DrawPaintOverlay(Renderer renderer, float2 cursor)
    {
        var a = _aspect;
        var p = _pixel;
        void Rect(Box2 b, float3 color) => PcTerminal.Rect(renderer, a, b.Center, b.Size, color);
        void Frame(Box2 b, float3 color, float3 edge)
        {
            Rect(b with { Size = b.Size + new float2(4 * p) }, edge);
            Rect(b, color);
        }
        void Text(string text, float x, float top, float3 color) => PcTerminal.Text(renderer, a, text, new float2(x, top), p, color);
        void Centered(string text, Box2 b, float3 color) => Text(text, b.Center.X - TextWidth(text) / 2, b.Center.Y + BlockFont.Height * p / 2, color);
        var left = PanelLeft;
        var dark = new float3(0.09f, 0.09f, 0.1f);

        // Along the top: what's being painted, and DONE.
        Rect(new Box2(new float2(0, 1 - TopBar / 2), new float2(2 * a, TopBar)), PanelBack);
        Rect(new Box2(new float2(0, 1 - TopBar), new float2(2 * a, 2 * p)), PanelEdge);
        var title = _paintModel is { } model
            ? $"PAINT: every {model.Replace('_', ' ')} ({(_paintCopies == 1 ? "1 copy" : $"{_paintCopies} copies")} here, and in every level)"
            : $"PAINT: {Describe(_paintItems)} (this one only)";
        Text(title, left, 1 - TopBar / 2 + BlockFont.Height * p / 2, SelectColor);
        var done = DoneButton;
        var overDone = Hit(cursor, done);
        Rect(done, overDone ? Accent * 0.45f : dark);
        Centered("DONE", done, overDone ? new float3(1f) : PanelInk);

        // Down the left: the panel.
        var panel = PalettePanel;
        Rect(panel, PanelBack);
        Rect(new Box2(new float2(panel.Center.X + panel.Size.X / 2, panel.Center.Y), new float2(2 * p, panel.Size.Y)), PanelEdge);

        // COLOUR: KEEP, AS BUILT and the colour in use (lit up: the one painted with), the swatches, the sliders.
        Text("COLOUR", left, ColorTop, PanelInk);
        var hoveredColor = _paintHover is { } hc ? SlotColor(hc) : (float3?)null;
        if (hoveredColor is { } now)
            Text($"here: {ColorText(now)}", left + 0.2f, ColorTop, PanelDim);
        foreach (var (box, label, brush) in (ReadOnlySpan<(Box2, string, ColorBrush)>)[(KeepColorBox, "KEEP", ColorBrush.Keep), (AsBuiltColorBox, "AS BUILT", ColorBrush.AsBuilt)])
        {
            var on = _colorBrush == brush;
            Frame(box, on ? Accent * 0.55f : Hit(cursor, box) ? Accent * 0.3f : dark, on ? new float3(1f) : PanelEdge);
            Centered(label, box, on || Hit(cursor, box) ? new float3(1f) : PanelInk);
        }
        Frame(BrushSwatchBox, _brushColor, _colorBrush == ColorBrush.Set ? new float3(1f) : PanelEdge);
        for (var i = 0; i < Swatches.Length; i++)
        {
            var box = SwatchBox(i);
            var picked = _colorBrush == ColorBrush.Set && Swatches[i] == _brushColor;
            Rect(box with { Size = box.Size + new float2((picked || Hit(cursor, box) ? 5 : 2) * p) },
                picked ? new float3(1f) : Hit(cursor, box) ? HoverColor : PanelEdge * 0.6f);   // (an edge, so the darkest shows on the panel)
            Rect(box, Swatches[i]);
        }
        ReadOnlySpan<(string Letter, float3 Hue)> channels = [("R", new(1f, 0.25f, 0.2f)), ("G", new(0.3f, 0.9f, 0.3f)), ("B", new(0.35f, 0.5f, 1f))];
        for (var channel = 0; channel < 3; channel++)
        {
            var bar = SliderBar(channel);
            var value = channel == 0 ? _brushColor.X : channel == 1 ? _brushColor.Y : _brushColor.Z;
            Text(channels[channel].Letter, left, bar.Center.Y + BlockFont.Height * p / 2, PanelInk);
            Rect(bar, dark);
            var fill = bar.Size.X * value;
            Rect(new Box2(new float2(bar.Center.X - bar.Size.X / 2 + fill / 2, bar.Center.Y), new float2(fill, bar.Size.Y)), channels[channel].Hue * 0.55f);
            Rect(new Box2(new float2(bar.Center.X - bar.Size.X / 2 + fill, bar.Center.Y), new float2(3 * p, bar.Size.Y + 4 * p)), new float3(1f));
            Text($"{(int)float.Round(value * 255)}", bar.Center.X + bar.Size.X / 2 + 0.02f, bar.Center.Y + BlockFont.Height * p / 2, PanelDim);
        }

        // TEXTURE: KEEP, AS BUILT, then the texture sets.
        Rect(new Box2(new float2(panel.Center.X, TextureHeaderTop + 0.012f), new float2(PaletteWidth - 0.04f, p)), PanelEdge);
        Text("TEXTURE", left, TextureHeaderTop, PanelInk);
        var rows = TextureVisibleRows();
        var first = (int)_textureScroll;
        var hoveredTexture = _paintHover is { } h ? RowOf(SlotTexture(h)) : -1;
        for (var row = 0; row < rows && first + row < TextureRows; row++)
        {
            var index = first + row;
            var top = TextureListTop - row * RowHeight;
            var rowBox = new Box2(new float2(panel.Center.X - p, top - RowHeight / 2), new float2(PaletteWidth - 0.02f, RowHeight - p));
            var over = Hit(cursor, rowBox);
            if (index == _texture)
                Rect(rowBox, Accent * 0.55f);
            else if (over)
                Rect(rowBox, Accent * 0.3f);
            // A marker by the one the face under the pointer is painted with now.
            if (index == hoveredTexture)
                Rect(new Box2(new float2(left + 0.012f, top - RowHeight / 2), new float2(0.022f, BlockFont.Height * p)), HoverColor);
            Text(TextureName(index), left + 0.04f, top - 2.5f * p, index == _texture || over ? new float3(1f) : index <= AsBuiltRow ? SelectColor : PanelInk);
        }
        if (TextureRows > rows)
            Text($"{first + 1}-{int.Min(first + rows, TextureRows)} of {TextureRows}  wheel scrolls", left, -1 + BottomBar + 0.01f + BlockFont.Height * p, PanelDim);

        // Along the bottom: the help, what's under the pointer, and the two ways out.
        Rect(new Box2(new float2(0, -1 + BottomBar / 2), new float2(2 * a, BottomBar)), PanelBack);
        Rect(new Box2(new float2(0, -1 + BottomBar), new float2(2 * a, 2 * p)), PanelEdge);
        var line = (BlockFont.Height + 4) * p;
        var y = -1 + BottomBar - 0.02f;
        Text("Right mouse: turn round it  Wheel: closer / further", left, y, PanelDim);
        Text("Click: paint a face  Shift+click: paint all of it", left, y - line, PanelDim);
        Text("Alt+click: pick up a face's colour and texture", left, y - 2 * line, PanelDim);
        Text("KEEP: leave as it is  Ctrl+Z / Ctrl+Y  Esc, P or DONE: back", left, y - 3 * line, PanelDim);
        var info = Glfw.Time < _statusUntil ? _status
            : _paintHover is { } target ? $"{SlotName(target)}: {TextureName(RowOf(SlotTexture(target)))}, {ColorText(SlotColor(target))}"
            : $"Painting with: {BrushText()}";
        if (info.Length > 74)
            info = info[..72] + "..";
        Text(info, left, y - 4 * line - 0.004f, Glfw.Time < _statusUntil ? SelectColor : PanelInk);

        foreach (var (button, label, color) in (ReadOnlySpan<(Box2, string, float3)>)[(KeepButton, $"SAVE CHANGES ({_undo.Count})", KeepColor), (DiscardButton, "REVERT CHANGES", DiscardColor)])
        {
            var over = Hit(cursor, button);
            Rect(new Box2(button.Center, button.Size + new float2(4 * p)), over ? new float3(1f) : color * 1.6f);
            Rect(button, over ? color * 1.4f : color);
            Centered(label, button, new float3(1f));
        }
    }

    /// <summary>What these items are, as the status line says it.</summary>
    private string Describe(List<EditRef> items)
    {
        if (items.Count == 0)
            return "nothing";
        var others = items.Select(level.ObjectKey).Where(k => k >= 0).Distinct().Count() - 1;
        return level.Describe(items[0]) + (others > 0 ? $" and {others} more" : "");
    }

    // ------------------------------------------------------------------------------------------------
    // BRUTALFPS_EDITOR=paint[:pitch] / =paintmodel:name (+keep saves)
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// BRUTALFPS_EDITOR=paint[:pitch]: opens the paint tool on what's straight ahead (that one only), or with
    /// =paintmodel:name on every copy of that model; paints its first face (or part) brick, then all of it red (the
    /// textures kept), then all of it wood with the colour as built; undoes them and checks it's as it was, then does
    /// them again (logged as [EDITOR] PAINT). Left open, for a screenshot; <paramref name="keep"/> saves it.
    /// </summary>
    public void PaintSelfTest(string? model, bool keep = false)
    {
        void Log(string text) => Engine.Log.Write($"[EDITOR] PAINT {text}");
        if (model is not null)
        {
            if (_palette.FindIndex(e => e.Model == model) is not (>= 0 and var entry))
            {
                Log($"no {model} in the asset list");
                return;
            }
            StartModelPaint(_palette[entry]);
        }
        else
        {
            PickAhead(_camera.Pitch);
            StartPaint();
        }
        if (!_painting)
        {
            Log("nothing to paint ahead");
            return;
        }
        var target = _paintAsset is not null ? (default(EditRef), 0) : (_paintItems[0], 0);
        string State()
        {
            IEnumerable<string> Slots(Func<int, (Material, float3)> slot, int count) =>
                Enumerable.Range(0, count).Select(s => slot(s) is var (t, c) ? $"{t} {ColorText(c)}" : "");
            return _paintAsset is { } asset
                ? string.Join(", ", Slots(s => (asset.Parts[s].Material.Material, Level.PartColor(asset.Parts[s].Material, _paintTint)), asset.Parts.Count))
                : string.Join(" | ", _paintItems.Select(i => string.Join(", ", Slots(s => (level.TextureOf(i, s), level.ColorOf(i, s)), level.SlotCount(i)))));
        }
        var before = State();
        Log($"{(_paintModel ?? Describe(_paintItems))}, {SlotName(target)} first: {before}");
        (_texture, _colorBrush) = (RowOf(Material.Brick), ColorBrush.Keep);
        Paint(target, all: false);
        Log($"first slot brick: {State()}");
        (_texture, _colorBrush, _brushColor) = (KeepRow, ColorBrush.Set, new float3(0.8f, 0.12f, 0.1f));
        Paint(target, all: true);
        Log($"all red, textures kept: {State()}");
        (_texture, _colorBrush) = (RowOf(Material.Wood), ColorBrush.AsBuilt);
        Paint(target, all: true);
        Log($"all wood, colour as built: {State()}");
        for (var i = 0; i < 3; i++)
            Undo(quiet: true);
        Log($"undid all three: {(State() == before ? "as it was" : "NOT RESTORED: " + State())}");
        for (var i = 0; i < 3; i++)
            Redo();
        Log($"redid them: {State()}");
        // (Then red again, so a screenshot shows a colour as well as a texture.)
        (_texture, _colorBrush) = (KeepRow, ColorBrush.Set);
        Paint(target, all: true);
        if (keep)
            level.SaveEdits();
    }
}
