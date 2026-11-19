using System.Globalization;
using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The level editor, built on its own (Editor/BrutalFpsEditor.csproj, LEVEL_EDITOR): it opens straight in, and the game's
/// build doesn't have it. The world holds still while you fly round it
/// and rearrange it: move, turn, copy and delete what the levels are built from, and drag in more from the asset
/// list on the left (the Blender models in Assets/Models, and boxes in the levels' materials). Ctrl+Z undoes, Ctrl+Y
/// redoes. SAVE CHANGES saves the layout (Assets/Levels/level_edits.json in the project: see Game/LevelEditing.cs),
/// which the game loads as it builds the levels; REVERT CHANGES puts back everything done since the last save.
///
/// Controls: right mouse held, the mouse looks round; WASD fly, Q / E down and up, Shift faster, the wheel flies
/// forward and back. Left click picks an item (Shift adds to the pick, Alt reaches the ground and long walls), and
/// dragging moves what's picked across the floor (the wheel, while dragging, lifts it); Ctrl+Shift+drag drags off a copy. R turns it a quarter turn
/// (Shift+R 15 degrees, for models), Delete deletes it, Ctrl+D copies it, Page Up / Page Down raise and lower it, the
/// arrow keys nudge it, F flies to it, N names it (Enter keeps the name, Esc cancels), G turns the 25 cm grid on and off.
/// </summary>
public sealed partial class LevelEditor(Level level, LevelEditor.JumpPoint[] jumps)
{
    /// <summary>A place the bar along the top flies you to.</summary>
    public readonly record struct JumpPoint(string Name, float3 Eye, float Yaw);

    /// <summary>What closed the editor this frame.</summary>
    public enum Exit { None, Keep, Discard }

    /// <summary>Something in the asset list: a model (by name) or a box (its size, standing on the ground).</summary>
    private sealed record PaletteEntry(string Label, string? Model, float3 Tint, float3 BoxSize = default, Material BoxMaterial = Material.Plain);

    private sealed record UndoStep(string What, (EditRef Item, EditPose Before, EditPose After)[] Changes,
        (EditRef Item, string? Before, string? After)[]? Names = null,
        (EditRef Item, SlotPaint? Before, SlotPaint? After)[]? Paints = null,
        (string Model, SlotPaint? Before, SlotPaint? After)[]? ModelPaints = null);

    private enum DragKind { None, Look, Move, Spawn }

    private const float GridStep = 0.25f, FineStep = 0.05f;

    private static readonly float3 PanelInk = new(0.82f, 0.82f, 0.8f);
    private static readonly float3 PanelDim = new(0.5f, 0.52f, 0.56f);
    private static readonly float3 PanelBack = new(0.035f, 0.035f, 0.04f);
    private static readonly float3 PanelEdge = new(0.32f, 0.36f, 0.45f);
    private static readonly float3 Accent = new(0.35f, 0.55f, 1.0f);
    private static readonly float3 SelectColor = new(1.0f, 0.78f, 0.1f);
    private static readonly float3 HoverColor = new(0.45f, 0.8f, 1.0f);
    private static readonly float3 ColliderColor = new(0.9f, 0.4f, 0.15f);
    private static readonly float3 KeepColor = new(0.16f, 0.5f, 0.22f);
    private static readonly float3 DiscardColor = new(0.55f, 0.14f, 0.12f);

    private readonly List<PaletteEntry> _palette = BuildPalette();
    private readonly HashSet<EditRef> _selection = [];
    private readonly List<UndoStep> _undo = [], _redo = [];

    private Camera _camera = Camera.Default;
    private bool _snap = true;
    private string _status = "";
    private double _statusUntil;
    private EditRef? _hover;

    private DragKind _drag;
    private readonly Dictionary<EditRef, EditPose> _dragStart = [];
    private float3 _dragAnchor;
    private float _dragLift;
    private float3 _dragMove;
    private bool _copyDrag;   // the drag is of copies just made (Ctrl+Shift+drag)
    private int _dragEntry = -1;
    private float3 _spawnBase;   // where what's being dragged in from the list stood when it was made (its bottom middle)
    private bool _spawnHidden;
    private string _placedOn = "";
    private float _paletteScroll;
    private string? _renaming;   // the name being typed (N), or null

    // The layout of the screen (square overlay space: y -1 .. 1, x -aspect .. aspect), worked out each frame.
    private float _aspect = 16f / 9f, _pixel = 0.0044f;

    public bool IsOpen { get; private set; }

    /// <summary>The view while editing.</summary>
    public Camera Camera => _camera;

    /// <summary>Opens the editor, looking from <paramref name="from"/>, saying <paramref name="say"/> (else how to start).</summary>
    public void Open(Camera from, string? say = null)
    {
        IsOpen = true;
        _camera = Camera.Default with { Position = from.Position, Yaw = from.Yaw, Pitch = float.Clamp(from.Pitch, -1.5f, 1.5f) };
        _selection.Clear();
        _undo.Clear();
        _redo.Clear();
        _drag = DragKind.None;
        _hover = null;
        _painting = false;
        _paintItems.Clear();
        _paintModel = null;
        Say(say ?? "Drag assets in from the left; click things to move them.", 8);
        Log.Write("[EDITOR] Opened");
    }

    /// <summary>How many changes there are since the last save (or since it opened).</summary>
    public int UnsavedChanges => _undo.Count;

    /// <summary>Puts <paramref name="text"/> on the status line for a while, and in the log.</summary>
    public void Warn(string text)
    {
        Say(text, 8);
        Log.Write($"[EDITOR] {text}");
    }

    /// <summary>True when what was kept changed the layout (so the shadows want drawing again).</summary>
    public bool KeptChanges { get; private set; }

    private void Say(string text, double seconds = 4)
    {
        _status = text;
        _statusUntil = Glfw.Time + seconds;
    }

    // ------------------------------------------------------------------------------------------------
    // Input
    // ------------------------------------------------------------------------------------------------

    public Exit Update(float dt, Input input, int2 framebuffer)
    {
        if (!IsOpen)
            return Exit.None;
        Layout(framebuffer);
        var cursor = input.CursorSquare;
        var ctrl = input.IsDown(GlfwKey.LeftControl) || input.IsDown(GlfwKey.RightControl);
        var shift = input.IsDown(GlfwKey.LeftShift) || input.IsDown(GlfwKey.RightShift);
        var alt = input.IsDown(GlfwKey.LeftAlt) || input.IsDown(GlfwKey.RightAlt);
        var overUi = OverUi(cursor);

        // Typing a name: the keyboard is the text box's until Enter or Esc (a click anywhere also keeps it).
        if (_renaming is not null)
        {
            if (input.WasPressed(GlfwMouseButton.Left))
            {
                FinishRename(keep: true);   // (and on to the click)
            }
            else
            {
                Rename(input);
                return Exit.None;
            }
        }

        // The paint tool (Game/LevelEditorPaint.cs) has the mouse and keys to itself while it's open.
        if (_painting)
            return UpdatePaint(dt, input, cursor, ctrl, shift, alt, overUi);

        // Looking round: the right button held captures the mouse.
        if (_drag == DragKind.None && input.WasPressed(GlfwMouseButton.Right) && !overUi)
        {
            _drag = DragKind.Look;
            input.SetMouseCaptured(true);
        }
        if (_drag == DragKind.Look)
        {
            if (!input.IsDown(GlfwMouseButton.Right))
            {
                _drag = DragKind.None;
                input.SetMouseCaptured(false);
            }
            else
            {
                _camera.Yaw += input.MouseDelta.X * 0.0025f;
                _camera.Pitch = float.Clamp(_camera.Pitch - input.MouseDelta.Y * 0.0025f, -1.5f, 1.5f);
            }
        }

        Fly(dt, input, ctrl, shift);

        // The mouse wheel: lifts what's being dragged, scrolls the asset list, or flies forward and back.
        if (input.ScrollSteps != 0)
        {
            if (_drag is DragKind.Move or DragKind.Spawn)
                _dragLift += input.ScrollSteps * (_snap ? GridStep : FineStep);
            else if (OverPalette(cursor))
                _paletteScroll = float.Clamp(_paletteScroll - input.ScrollSteps * 3, 0, float.Max(0, _palette.Count - VisibleRows()));
            else if (_drag == DragKind.None)
                _camera.Position += _camera.Forward * input.ScrollSteps * (shift ? 8f : 2f);
        }

        // The buttons and the bar along the top.
        if (_drag == DragKind.None && input.WasPressed(GlfwMouseButton.Left) && overUi)
        {
            if (Hit(cursor, KeepButton))
                return Close(keep: true, input);
            if (Hit(cursor, DiscardButton))
                return Close(keep: false, input);
            for (var i = 0; i < jumps.Length; i++)
            {
                if (Hit(cursor, JumpButton(i)))
                {
                    _camera.Position = jumps[i].Eye;
                    _camera.Yaw = jumps[i].Yaw;
                    _camera.Pitch = -0.15f;
                    Say($"Flew to {jumps[i].Name}");
                }
            }
            if (PaletteRowAt(cursor) is { } entry)
            {
                _drag = DragKind.Spawn;
                _dragEntry = entry;
                _dragStart.Clear();
                _dragLift = 0;
                _spawnHidden = false;
            }
        }
        // Picking in the world.
        else if (_drag == DragKind.None && input.WasPressed(GlfwMouseButton.Left))
        {
            var ray = RayThrough(cursor);
            if (level.PickEditable(_camera.Position, ray, 2000f, alt, out var hit, out var distance))
            {
                var group = new HashSet<EditRef>();
                level.CollectGroup(hit, group);
                if (ctrl && shift)
                {
                    // Ctrl+Shift+drag: a copy, dragged away from where it was (what's picked, if it was clicked; else what was clicked).
                    if (!_selection.Contains(hit))
                    {
                        _selection.Clear();
                        _selection.UnionWith(group);
                    }
                    if (Duplicate(float3.Zero))
                    {
                        StartMove(_camera.Position + ray * distance);
                        _copyDrag = true;
                        Say($"Dragging a copy of {Describe()}");
                    }
                }
                else if (shift)
                {
                    if (_selection.Contains(hit))
                        _selection.ExceptWith(group);
                    else
                        _selection.UnionWith(group);
                }
                else
                {
                    if (!_selection.Contains(hit))
                    {
                        _selection.Clear();
                        _selection.UnionWith(group);
                    }
                    StartMove(_camera.Position + ray * distance);
                }
                if (!_copyDrag)
                    Say(Describe());
            }
            else if (!shift)
            {
                _selection.Clear();
            }
        }

        switch (_drag)
        {
            case DragKind.Move:
                UpdateMove(input, cursor);
                break;
            case DragKind.Spawn:
                UpdateSpawn(input, cursor, overUi);
                break;
        }

        Keys(input, ctrl, shift);

        // What the pointer is over (outlined), when nothing's being dragged.
        _hover = null;
        if (_drag == DragKind.None && !overUi && level.PickEditable(_camera.Position, RayThrough(cursor), 2000f, alt, out var over, out _))
            _hover = over;
        return Exit.None;
    }

    private Exit Close(bool keep, Input input)
    {
        if (_painting)
            EndPaint();
        KeptChanges = false;
        if (keep)
        {
            try
            {
                level.SaveEdits();
                KeptChanges = _undo.Count > 0;
            }
            catch (Exception e)
            {
                Log.Write($"[WARN] [EDITOR] Couldn't save the level ({e.GetType().Name}: {e.Message})");
                Say($"Couldn't save: {e.Message}", 8);
                return Exit.None;
            }
            Log.Write($"[EDITOR] Kept {_undo.Count} changes");
        }
        else
        {
            while (_undo.Count > 0)
                Undo(quiet: true);
            Log.Write("[EDITOR] Discarded the changes");
        }
        IsOpen = false;
        _selection.Clear();
        _drag = DragKind.None;
        input.ConsumePresses();
        return keep ? Exit.Keep : Exit.Discard;
    }

    private void Fly(float dt, Input input, bool ctrl, bool shift)
    {
        if (ctrl)
            return;   // (Ctrl+D, Ctrl+Z: not flying)
        var forward = _camera.Forward;
        var right = float3.Normalize(float3.Cross(forward, float3.UnitY));
        var move = float3.Zero;
        if (input.IsDown(GlfwKey.W)) move += forward;
        if (input.IsDown(GlfwKey.S)) move -= forward;
        if (input.IsDown(GlfwKey.D)) move += right;
        if (input.IsDown(GlfwKey.A)) move -= right;
        if (input.IsDown(GlfwKey.E) || input.IsDown(GlfwKey.Space)) move += float3.UnitY;
        if (input.IsDown(GlfwKey.Q)) move -= float3.UnitY;
        if (float3.Dot(move, move) > 0)
            _camera.Position += float3.Normalize(move) * (shift ? 40f : 10f) * dt;
    }

    private void Keys(Input input, bool ctrl, bool shift)
    {
        if (ctrl && input.WasPressed(GlfwKey.Z))
        {
            if (shift)
                Redo();
            else
                Undo(quiet: false);
            return;
        }
        if (ctrl && input.WasPressed(GlfwKey.Y))
        {
            Redo();
            return;
        }
        if (input.WasPressed(GlfwKey.G))
        {
            _snap = !_snap;
            Say(_snap ? "Grid on (25 cm)" : "Grid off");
        }
        if (input.WasPressed(GlfwKey.Escape))
        {
            if (_selection.Count > 0)
                _selection.Clear();
            else
                Say("SAVE CHANGES or REVERT CHANGES (bottom right); close the window to quit.", 5);
        }
        if (_drag != DragKind.None || _selection.Count == 0)
            return;

        if (ctrl && input.WasPressed(GlfwKey.D))
            Duplicate();
        else if (input.WasPressed(GlfwKey.N) && !ctrl)
            StartRename();
        else if (input.WasPressed(GlfwKey.P) && !ctrl)
            StartPaint();
        else if (input.WasPressed(GlfwKey.Delete) || input.WasPressed(GlfwKey.Backspace))
            Delete();
        else if (input.WasPressed(GlfwKey.R))
            Rotate(shift);
        else if (input.WasPressed(GlfwKey.F))
            Focus();
        else
        {
            var step = _snap ? GridStep : FineStep;
            var nudge = float3.Zero;
            if (input.WasPressed(GlfwKey.PageUp)) nudge.Y += step;
            if (input.WasPressed(GlfwKey.PageDown)) nudge.Y -= step;
            // The arrows along whichever of the world's axes are nearest the view's forward and right.
            var (ahead, side) = ViewAxes();
            if (input.WasPressed(GlfwKey.Up)) nudge += ahead * step;
            if (input.WasPressed(GlfwKey.Down)) nudge -= ahead * step;
            if (input.WasPressed(GlfwKey.Right)) nudge += side * step;
            if (input.WasPressed(GlfwKey.Left)) nudge -= side * step;
            if (nudge.X != 0 || nudge.Y != 0 || nudge.Z != 0)
                Change("Nudge", item => level.PoseOf(item).Then(float3.Zero, 0, nudge));
        }
    }

    /// <summary>The world's horizontal axes nearest the view's forward and right.</summary>
    private (float3 Ahead, float3 Side) ViewAxes()
    {
        var f = _camera.Forward;
        var ahead = float.Abs(f.X) > float.Abs(f.Z) ? new float3(float.Sign(f.X), 0, 0) : new float3(0, 0, float.Sign(f.Z));
        if (ahead.X == 0 && ahead.Z == 0)
            ahead = new float3(0, 0, -1);
        var side = float3.Cross(ahead, float3.UnitY);
        return (ahead, side);
    }

    // ------------------------------------------------------------------------------------------------
    // Moving, adding, turning, copying, deleting
    // ------------------------------------------------------------------------------------------------

    private void StartMove(float3 grabbed)
    {
        _drag = DragKind.Move;
        _dragStart.Clear();
        foreach (var item in _selection)
            _dragStart[item] = level.PoseOf(item);
        _dragAnchor = grabbed;
        _dragLift = 0;
        _dragMove = float3.Zero;
    }

    private float Snap(float v) => _snap ? float.Round(v / GridStep) * GridStep : v;

    private void UpdateMove(Input input, float2 cursor)
    {
        var ray = RayThrough(cursor);
        // Across the level (the floor's plane at the height it was grabbed), and up and down with the wheel.
        if (float.Abs(ray.Y) > 1e-4f)
        {
            var t = (_dragAnchor.Y - _camera.Position.Y) / ray.Y;
            if (t > 0 && t < 3000f)
            {
                var p = _camera.Position + ray * t;
                var move = new float3(Snap(p.X - _dragAnchor.X), _dragLift, Snap(p.Z - _dragAnchor.Z));
                if (move != _dragMove)
                {
                    _dragMove = move;
                    foreach (var (item, start) in _dragStart)
                        level.SetPose(item, start.Then(float3.Zero, 0, move));
                }
            }
        }
        if (!input.IsDown(GlfwMouseButton.Left))
            EndMove();
    }

    /// <summary>Let go of what's being moved: the move (or the copy dragged off) is a step to undo.</summary>
    private void EndMove()
    {
        _drag = DragKind.None;
        if (_copyDrag)
        {
            // The copy and the drag are one step to undo; let go where it started, there's no copy after all.
            _copyDrag = false;
            var copied = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            if (_dragMove == float3.Zero)
            {
                foreach (var (item, before, _) in copied.Changes)
                    level.SetPose(item, before);
                _selection.Clear();
                Say("Not copied (drag it away to copy it)");
                return;
            }
            Record("Copy", [.. copied.Changes.Select(c => (c.Item, c.Before, level.PoseOf(c.Item)))]);
            Say($"Copied {Describe()}{LadderNote()}", LadderSeconds());
            return;
        }
        if (_dragMove != float3.Zero)
        {
            Record("Move", [.. _dragStart.Select(kv => (kv.Key, kv.Value, level.PoseOf(kv.Key)))]);
            Say($"Moved {Describe()}{LadderNote()}", LadderSeconds());
        }
    }

    private void UpdateSpawn(Input input, float2 cursor, bool overUi)
    {
        var entry = _palette[_dragEntry];
        if (!overUi)
        {
            var ray = RayThrough(cursor);
            var ignore = _dragStart.Count > 0 ? _dragStart.Keys.ToHashSet() : [];
            var at = level.RaycastForPlacing(_camera.Position, ray, 400f, ignore, out var distance, out _placedOn)
                ? _camera.Position + ray * distance
                : _camera.Position + ray * 8f;
            at = new float3(Snap(at.X), at.Y, Snap(at.Z));
            if (_dragStart.Count == 0)
                Spawn(entry, at);
            if (_dragStart.Count > 0)
            {
                var move = at + new float3(0, _dragLift, 0) - _spawnBase;
                foreach (var item in _dragStart.Keys)
                    level.SetPose(item, new EditPose(0, move));
                _spawnHidden = false;
            }
        }
        else if (_dragStart.Count > 0 && !_spawnHidden)
        {
            // Back over the list: it's not going in after all (unless it's let go of out in the level again).
            foreach (var item in _dragStart.Keys)
                level.SetPose(item, new EditPose(0, float3.Zero, Deleted: true));
            _spawnHidden = true;
        }

        if (input.IsDown(GlfwMouseButton.Left))
            return;
        _drag = DragKind.None;
        if (_dragStart.Count == 0 || _spawnHidden)
        {
            // Clicked (let go on the same row without dragging it out): paint every copy of that model.
            if (_dragStart.Count == 0 && PaletteRowAt(cursor) == _dragEntry)
                StartModelPaint(entry);
            _selection.ExceptWith(_dragStart.Keys);
            return;
        }
        _selection.Clear();
        _selection.UnionWith(_dragStart.Keys);
        Record($"Add {entry.Label}", [.. _dragStart.Keys.Select(item => (item, new EditPose(0, float3.Zero, Deleted: true), level.PoseOf(item)))]);
        Say($"Added {entry.Label}");
        Engine.Log.Write($"[EDITOR] Added {entry.Label} at {level.BoundsOf(_dragStart.Keys.First()).Min}, on {_placedOn}");
    }

    /// <summary>Makes what's being dragged in from the list, standing at <paramref name="at"/>, facing back at the camera.</summary>
    private void Spawn(PaletteEntry entry, float3 at)
    {
        _spawnBase = at;
        if (entry.Model is { } name)
        {
            float4x4 placement;
            try
            {
                var bounds = Engine.Model.Get(name).Bounds;
                // Turned to the nearest quarter turn facing the camera, its footprint's middle on the spot, its base on the floor.
                var yaw = float.Round(-_camera.Yaw / (float.Pi / 2)) * (float.Pi / 2);
                var turn = float4x4.CreateRotationY(yaw);
                var foot = float3.Transform(new float3(bounds.Center.X, bounds.Min.Y, bounds.Center.Z), turn);
                placement = turn * float4x4.CreateTranslation(at - foot);
            }
            catch (Exception e)
            {
                Say($"Couldn't load {name}: {e.Message}");
                _drag = DragKind.None;
                return;
            }
            if (level.AddEditorModel(name, placement, entry.Tint) is not { } prop)
            {
                Say($"Couldn't load {name}");
                _drag = DragKind.None;
                return;
            }
            _dragStart[prop] = default;
            // A Transit van wears one of the Factory's number plates, picked at random: a model of its own, placed with the
            // van and one piece with it.
            if (name == "transit" && Level.TransitPlates is { Count: > 0 } plates)
            {
                var code = plates[Random.Shared.Next(plates.Count)];
                if (level.AddEditorModel($"transit_plate_{code}", placement, entry.Tint) is { } plate)
                {
                    _dragStart[plate] = default;
                    level.JoinPiece([prop, plate], "transit");
                }
            }
            // Something to bump into: an invisible box round it (up to head height for the trees, none for the low scrub).
            var round = level.BoundsOf(prop);
            if (round.Size.Y > 0.25f)
            {
                var top = float.Min(round.Max.Y, round.Min.Y + 2.2f);
                var shrink = float.Clamp(float.Min(round.Size.X, round.Size.Z) * 0.08f, 0f, 0.6f);
                if (entry.Model.StartsWith("jungle_", StringComparison.Ordinal) || entry.Model.Contains("tree") || entry.Model.Contains("palm"))
                {
                    // (A tree: just its trunk.)
                    var c = round.Center;
                    round = new Aabb(new float3(c.X - 0.3f, round.Min.Y, c.Z - 0.3f), new float3(c.X + 0.3f, round.Max.Y, c.Z + 0.3f));
                    shrink = 0;
                }
                var collider = new Aabb(round.Min + new float3(shrink, 0, shrink), new float3(round.Max.X - shrink, top, round.Max.Z - shrink));
                _dragStart[level.AddEditorBox(new LevelBox(collider, default, Material.Plain, Visible: false))] = default;
            }
        }
        else
        {
            var size = entry.BoxSize;
            var box = new LevelBox(new Aabb(at - new float3(size.X / 2, 0, size.Z / 2), at + new float3(size.X / 2, size.Y, size.Z / 2)), entry.Tint, entry.BoxMaterial);
            _dragStart[level.AddEditorBox(box)] = default;
        }
        _selection.Clear();
        _selection.UnionWith(_dragStart.Keys);
    }

    // ------------------------------------------------------------------------------------------------
    // Naming (N): the name goes on everything picked (a whole piece, a model and its collision), is kept in the level's
    // file with the rest, and shows wherever the editor says what something is.
    // ------------------------------------------------------------------------------------------------

    private const int MaxNameLength = 40;

    private void StartRename()
    {
        var named = _selection.Select(level.NameOf).FirstOrDefault(n => n is not null);
        _renaming = named ?? "";
    }

    private void Rename(Input input)
    {
        foreach (var c in input.Typed)
            if (_renaming!.Length < MaxNameLength && (c == ' ' || BlockFont.TryGet(c, out _)))
                _renaming += c;
        if (input.WasPressedOrRepeated(GlfwKey.Backspace) && _renaming!.Length > 0)
            _renaming = _renaming[..^1];
        if (input.WasPressed(GlfwKey.Enter) || input.WasPressed(GlfwKey.KpEnter))
            FinishRename(keep: true);
        else if (input.WasPressed(GlfwKey.Escape))
            FinishRename(keep: false);
    }

    private void FinishRename(bool keep)
    {
        var name = _renaming?.Trim();
        _renaming = null;
        if (!keep || _selection.Count == 0)
            return;
        if (name is { Length: 0 })
            name = null;
        var changes = _selection.Where(i => level.NameOf(i) != name).Select(i => (i, level.NameOf(i), name)).ToArray();
        if (changes.Length == 0)
            return;
        foreach (var (item, _, after) in changes)
            level.SetName(item, after);
        _undo.Add(new UndoStep("Rename", [], changes));
        _redo.Clear();
        Say(name is null ? "Name cleared" : $"Named \"{name}\"");
        Engine.Log.Write($"[EDITOR] Named {changes.Length} items \"{name}\"");
    }

    private void Rotate(bool fine)
    {
        // A box can only turn by quarter turns (it stays lined up with the world's axes); models by 15 degrees too.
        var boxes = _selection.Any(level.IsVisibleBox);
        var angle = fine && !boxes ? float.Pi / 12 : float.Pi / 2;
        if (fine && boxes)
            Say("Boxes only turn by quarter turns", 3);
        var pivot = SelectionBounds().Center;
        pivot = new float3(Snap(pivot.X), 0, Snap(pivot.Z));
        Change("Turn", item => level.PoseOf(item).Then(pivot, angle, float3.Zero));
        if (LadderNote() is { Length: > 0 } note)
            Say($"Turned {Describe()}{note}", LadderSeconds());
    }

    /// <summary>A warning to add when a ladder that's picked can't be climbed where it is now (else ""), and logs it.</summary>
    private string LadderNote()
    {
        foreach (var item in _selection)
        {
            if (level.LadderTrouble(item) is { } trouble)
            {
                Engine.Log.Write($"[WARN] [EDITOR] Ladder can't be climbed where it's been put: {trouble}");
                return $" - but the ladder can't be climbed there: {trouble}";
            }
        }
        return "";
    }

    /// <summary>How long to show the status line: longer when there's a ladder warning in it.</summary>
    private double LadderSeconds() => _selection.Any(item => level.LadderTrouble(item) is not null) ? 7 : 4;

    private void Delete()
    {
        var what = Describe();
        Change("Delete", item => level.PoseOf(item) with { Deleted = true });
        _selection.Clear();
        Say($"Deleted {what} (Ctrl+Z brings it back)");
    }

    private void Duplicate()
    {
        var bounds = SelectionBounds();
        var (_, side) = ViewAxes();
        // Next to it, on the right as you look at it: as far over as it's wide.
        var width = float.Abs(side.X) > 0 ? bounds.Size.X : bounds.Size.Z;
        Duplicate(side * float.Max(_snap ? float.Ceiling(width / GridStep) * GridStep : width, GridStep));
    }

    /// <summary>Copies what's picked, <paramref name="offset"/> from it, and picks the copies (one step that undoes); false if nothing could be.</summary>
    private bool Duplicate(float3 offset)
    {
        var copies = new List<(EditRef, EditPose, EditPose)>();
        var skipped = 0;
        var pieces = new Dictionary<int, int>();   // (a copied piece is a new piece)
        foreach (var item in _selection.OrderBy(i => i.IsProp).ThenBy(i => i.Index))
        {
            if (level.Duplicate(item, pieces) is not { } copy)
            {
                skipped++;
                continue;
            }
            var pose = new EditPose(0, offset);
            level.SetPose(copy, pose);
            copies.Add((copy, new EditPose(0, float3.Zero, Deleted: true), pose));
        }
        if (copies.Count == 0)
        {
            Say("That can't be copied");
            return false;
        }
        Record("Copy", [.. copies]);
        _selection.Clear();
        foreach (var (copy, _, _) in copies)
            _selection.Add(copy);
        Say($"Copied {Describe()}" + (skipped > 0 ? $" ({skipped} parts couldn't be)" : "") + LadderNote(), LadderSeconds());
        return true;
    }

    private void Focus()
    {
        var bounds = SelectionBounds();
        var reach = float.Max(3f, float.Max(bounds.Size.X, float.Max(bounds.Size.Y, bounds.Size.Z)) * 1.4f);
        _camera.Position = bounds.Center - _camera.Forward * reach;
    }

    private Aabb SelectionBounds()
    {
        var min = new float3(float.MaxValue);
        var max = new float3(float.MinValue);
        foreach (var item in _selection)
        {
            var b = level.BoundsOf(item);
            min = float3.Min(min, b.Min);
            max = float3.Max(max, b.Max);
        }
        return _selection.Count == 0 ? default : new Aabb(min, max);
    }

    /// <summary>Changes every picked item to <paramref name="to"/>(its pose), as one step that undoes.</summary>
    private void Change(string what, Func<EditRef, EditPose> to)
    {
        var changes = new List<(EditRef, EditPose, EditPose)>();
        foreach (var item in _selection)
        {
            var before = level.PoseOf(item);
            var after = to(item);
            if (after == before)
                continue;
            level.SetPose(item, after);
            changes.Add((item, before, after));
        }
        if (changes.Count > 0)
            Record(what, [.. changes]);
    }

    private void Record(string what, (EditRef, EditPose, EditPose)[] changes)
    {
        _undo.Add(new UndoStep(what, changes));
        _redo.Clear();
    }

    private void Undo(bool quiet)
    {
        if (_undo.Count == 0)
        {
            if (!quiet)
                Say("Nothing to undo");
            return;
        }
        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        for (var i = step.Changes.Length - 1; i >= 0; i--)
            level.SetPose(step.Changes[i].Item, step.Changes[i].Before);
        foreach (var (item, before, _) in step.Names ?? [])
            level.SetName(item, before);
        foreach (var (item, before, _) in step.Paints ?? [])
            level.SetPaint(item, before);
        foreach (var (model, before, _) in step.ModelPaints ?? [])
            Model.SetRepaint(model, before);
        _redo.Add(step);
        _selection.RemoveWhere(item => level.PoseOf(item).Deleted);
        if (!quiet)
            Say($"Undid: {step.What} ({_undo.Count} left)");
    }

    private void Redo()
    {
        if (_redo.Count == 0)
        {
            Say("Nothing to redo");
            return;
        }
        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        foreach (var (item, _, after) in step.Changes)
            level.SetPose(item, after);
        foreach (var (item, _, after) in step.Names ?? [])
            level.SetName(item, after);
        foreach (var (item, _, after) in step.Paints ?? [])
            level.SetPaint(item, after);
        foreach (var (model, _, after) in step.ModelPaints ?? [])
            Model.SetRepaint(model, after);
        _undo.Add(step);
        _selection.RemoveWhere(item => level.PoseOf(item).Deleted);
        Say($"Redid: {step.What}");
    }

    private string Describe()
    {
        if (_selection.Count == 0)
            return "nothing";
        // The drawn things first: a model's invisible collision boxes come with it but aren't what you picked; a piece
        // (a desk: a prop for each material in it) counts once.
        var shown = _selection.Where(i => i.IsProp || level.IsVisibleBox(i)).ToList();
        var first = shown.Count > 0 ? shown.MinBy(i => (i.IsProp ? 0 : 1, i.Index)) : _selection.First();
        var others = (shown.Count > 0 ? shown.Select(level.ObjectKey).Distinct().Count() : _selection.Count) - 1;
        return level.Describe(first) + (others > 0 ? $" and {others} more" : "");
    }

    // ------------------------------------------------------------------------------------------------
    // The view's rays
    // ------------------------------------------------------------------------------------------------

    /// <summary>The ray from the eye through the overlay point <paramref name="cursor"/>.</summary>
    private float3 RayThrough(float2 cursor)
    {
        var t = float.Tan(float.DegreesToRadians(_camera.FieldOfViewDegrees) / 2);
        var forward = _camera.Forward;
        var right = float3.Normalize(float3.Cross(forward, float3.UnitY));
        var up = float3.Cross(right, forward);
        return float3.Normalize(forward + right * (cursor.X * t) + up * (cursor.Y * t));
    }

    // ------------------------------------------------------------------------------------------------
    // Layout: the bar of places along the top, the asset list down the left, the help and buttons along the bottom.
    // ------------------------------------------------------------------------------------------------

    private readonly record struct Box2(float2 Center, float2 Size);

    private static bool Hit(float2 p, Box2 b) => float.Abs(p.X - b.Center.X) <= b.Size.X / 2 && float.Abs(p.Y - b.Center.Y) <= b.Size.Y / 2;

    private const float TopBar = 0.11f, BottomBar = 0.28f, PaletteWidth = 0.66f;

    private void Layout(int2 framebuffer)
    {
        _aspect = framebuffer.Y > 0 ? (float)framebuffer.X / framebuffer.Y : 16f / 9f;
        var px = 2f / float.Max(framebuffer.Y, 1);
        _pixel = float.Max(2f, float.Round(framebuffer.Y / 450f)) * px;
    }

    private float TextWidth(string text) => (text.Length * BlockFont.Advance - 1) * _pixel;
    private float RowHeight => (BlockFont.Height + 5) * _pixel;

    private Box2 PalettePanel => new(new float2(-_aspect + PaletteWidth / 2, (1 - TopBar - (-1 + BottomBar)) / 2 - 1 + BottomBar),
        new float2(PaletteWidth, 2 - TopBar - BottomBar));

    private float PaletteListTop => 1 - TopBar - 0.1f;

    private int VisibleRows() => int.Max(1, (int)((PaletteListTop - (-1 + BottomBar) - 0.02f) / RowHeight) - 1);   // (room for the line under it)

    private bool OverPalette(float2 p) => Hit(p, PalettePanel);

    private bool OverUi(float2 p) => p.Y > 1 - TopBar || p.Y < -1 + BottomBar || OverPalette(p);

    private int? PaletteRowAt(float2 p)
    {
        if (!OverPalette(p) || p.Y > PaletteListTop)
            return null;
        var row = (int)((PaletteListTop - p.Y) / RowHeight);
        var index = row + (int)_paletteScroll;
        return row < VisibleRows() && index < _palette.Count ? index : null;
    }

    private Box2 JumpButton(int i)
    {
        var x = -_aspect + 0.02f;
        for (var j = 0; j < i; j++)
            x += TextWidth(jumps[j].Name) + 0.05f;
        var w = TextWidth(jumps[i].Name) + 0.035f;
        return new Box2(new float2(x + w / 2, 1 - TopBar / 2), new float2(w, TopBar - 0.03f));
    }

    private Box2 KeepButton => new(new float2(_aspect - 0.04f - 0.66f - 0.04f - 0.33f, -1 + BottomBar / 2), new float2(0.66f, 0.14f));
    private Box2 DiscardButton => new(new float2(_aspect - 0.04f - 0.33f, -1 + BottomBar / 2), new float2(0.66f, 0.14f));

    // ------------------------------------------------------------------------------------------------
    // Drawing
    // ------------------------------------------------------------------------------------------------

    /// <summary>The outlines round what's picked and what the pointer is over (in the world's view, after the scene, over it).</summary>
    public void DrawWorld(Renderer renderer)
    {
        if (!IsOpen)
            return;
        if (_painting)
        {
            DrawPaintHover(renderer);
            return;
        }
        // One outline per object (a piece's props together), and the collision boxes that come with them.
        var drawn = new HashSet<long>();
        foreach (var item in _selection)
        {
            var key = level.ObjectKey(item);
            if (key < 0)
                Outline(renderer, level.BoundsOf(item), ColliderColor);
            else if (drawn.Add(key))
                Outline(renderer, level.OutlineOf(item), SelectColor);
        }
        if (_hover is { } hover && !_selection.Contains(hover))
            Outline(renderer, level.OutlineOf(hover), HoverColor);
    }

    private void Outline(Renderer renderer, in Aabb box, float3 color)
    {
        var d = float3.Distance(_camera.Position, float3.Clamp(_camera.Position, box.Min, box.Max));
        var t = float.Max(0.012f, d * 0.0022f);
        var (a, b) = (box.Min, box.Max);
        foreach (var y in (ReadOnlySpan<float>)[a.Y, b.Y])
        {
            foreach (var z in (ReadOnlySpan<float>)[a.Z, b.Z])
                renderer.DrawBox(new Aabb(new float3(a.X - t, y - t, z - t), new float3(b.X + t, y + t, z + t)), color, ShadeMode.Unlit);
            foreach (var x in (ReadOnlySpan<float>)[a.X, b.X])
                renderer.DrawBox(new Aabb(new float3(x - t, y - t, a.Z - t), new float3(x + t, y + t, b.Z + t)), color, ShadeMode.Unlit);
        }
        foreach (var x in (ReadOnlySpan<float>)[a.X, b.X])
            foreach (var z in (ReadOnlySpan<float>)[a.Z, b.Z])
                renderer.DrawBox(new Aabb(new float3(x - t, a.Y - t, z - t), new float3(x + t, b.Y + t, z + t)), color, ShadeMode.Unlit);
    }

    /// <summary>The editor's panels (call last, after the world).</summary>
    public void DrawOverlay(Renderer renderer, int2 framebuffer, float2 cursor)
    {
        if (!IsOpen)
            return;
        Layout(framebuffer);
        renderer.BeginOverlay();
        if (_painting)
        {
            DrawPaintOverlay(renderer, cursor);
            return;
        }
        var a = _aspect;
        var p = _pixel;
        void Rect(Box2 b, float3 color) => PcTerminal.Rect(renderer, a, b.Center, b.Size, color);
        void Text(string text, float x, float top, float3 color) => PcTerminal.Text(renderer, a, text, new float2(x, top), p, color);
        void Centered(string text, Box2 b, float3 color) => Text(text, b.Center.X - TextWidth(text) / 2, b.Center.Y + BlockFont.Height * p / 2, color);

        // Along the top: where to fly to.
        Rect(new Box2(new float2(0, 1 - TopBar / 2), new float2(2 * a, TopBar)), PanelBack);
        Rect(new Box2(new float2(0, 1 - TopBar), new float2(2 * a, 2 * p)), PanelEdge);
        for (var i = 0; i < jumps.Length; i++)
        {
            var b = JumpButton(i);
            var over = Hit(cursor, b) && _drag == DragKind.None;
            Rect(b, over ? Accent * 0.45f : new float3(0.09f, 0.09f, 0.1f));
            Centered(jumps[i].Name, b, over ? new float3(1f) : PanelInk);
        }

        // Down the left: the assets.
        var panel = PalettePanel;
        Rect(panel, PanelBack);
        Rect(new Box2(new float2(panel.Center.X + panel.Size.X / 2, panel.Center.Y), new float2(2 * p, panel.Size.Y)), PanelEdge);
        var left = -a + 0.03f;
        Text("ASSETS", left, 1 - TopBar - 0.025f, PanelInk);
        Text("drag into the level", left, 1 - TopBar - 0.025f - (BlockFont.Height + 3) * p, PanelDim);
        var rows = VisibleRows();
        var first = (int)_paletteScroll;
        for (var row = 0; row < rows && first + row < _palette.Count; row++)
        {
            var entry = _palette[first + row];
            var top = PaletteListTop - row * RowHeight;
            var rowBox = new Box2(new float2(panel.Center.X - p, top - RowHeight / 2), new float2(PaletteWidth - 0.02f, RowHeight - p));
            var active = (_drag == DragKind.Spawn && _dragEntry == first + row) || (_drag == DragKind.None && Hit(cursor, rowBox));
            if (active)
                Rect(rowBox, Accent * 0.4f);
            // A swatch of its colour, then its name.
            Rect(new Box2(new float2(left + 0.012f, top - RowHeight / 2), new float2(0.022f, BlockFont.Height * p)), entry.Model is null ? entry.Tint : entry.Tint * 0.8f + new float3(0.1f));
            Text(entry.Label, left + 0.04f, top - 2.5f * p, active ? new float3(1f) : entry.Model is null ? PanelDim * 1.3f : PanelInk);
        }
        if (_palette.Count > rows)
            Text($"{first + 1}-{int.Min(first + rows, _palette.Count)} of {_palette.Count}  wheel scrolls", left, -1 + BottomBar + 0.01f + BlockFont.Height * p, PanelDim);

        // Along the bottom: the help, what's picked, and the two ways out.
        Rect(new Box2(new float2(0, -1 + BottomBar / 2), new float2(2 * a, BottomBar)), PanelBack);
        Rect(new Box2(new float2(0, -1 + BottomBar), new float2(2 * a, 2 * p)), PanelEdge);
        var line = (BlockFont.Height + 4) * p;
        var y = -1 + BottomBar - 0.02f;
        Text("Right mouse: look  WASD: fly  Q/E: down/up  Shift: fast  Wheel: zoom", left, y, PanelDim);
        Text("Click: pick+drag  Shift: add  Ctrl+Shift: drag a copy  Alt: floors  P: paint", left, y - line, PanelDim);
        Text("R: turn (Shift+R 15)  Del: delete  Ctrl+D: copy  F: fly to it  N: name", left, y - 2 * line, PanelDim);
        Text($"Ctrl+Z: undo  Ctrl+Y: redo  PgUp/PgDn, arrows: nudge  G: grid {(_snap ? "on" : "off")}", left, y - 3 * line, PanelDim);
        if (_renaming is not null)
        {
            // The name being typed, in a box, with a blinking caret.
            var caret = Glfw.Time % 1.0 < 0.55 ? "_" : " ";
            var field = $"Name: {_renaming}{caret}";
            var fieldTop = y - 4 * line - 0.004f;
            Rect(new Box2(new float2(left + (TextWidth(field) + 0.02f) / 2 - 0.01f, fieldTop - BlockFont.Height * p / 2), new float2(TextWidth(field) + 0.02f, (BlockFont.Height + 4) * p)), Accent * 0.35f);
            Text(field, left, fieldTop, new float3(1f));
            Text("Enter: OK  Esc: cancel", left + TextWidth(field) + 0.04f, fieldTop, PanelDim);
        }
        else
        {
            var info = Glfw.Time < _statusUntil ? _status : _selection.Count > 0 ? $"Picked: {Describe()}" : "";
            if (info.Length > 74)
                info = info[..72] + "..";   // (clear of the buttons)
            Text(info, left, y - 4 * line - 0.004f, Glfw.Time < _statusUntil ? SelectColor : PanelInk);
        }

        foreach (var (button, label, color) in (ReadOnlySpan<(Box2, string, float3)>)[(KeepButton, $"SAVE CHANGES ({_undo.Count})", KeepColor), (DiscardButton, "REVERT CHANGES", DiscardColor)])
        {
            var over = Hit(cursor, button) && _drag == DragKind.None;
            Rect(new Box2(button.Center, button.Size + new float2(4 * p)), over ? new float3(1f) : color * 1.6f);
            Rect(button, over ? color * 1.4f : color);
            Centered(label, button, new float3(1f));
        }
    }

    // ------------------------------------------------------------------------------------------------
    // BRUTALFPS_EDITOR=test: runs through the editor's moves on what's straight ahead, logging each ([EDITOR] TEST),
    // undoes them all and checks everything is back as it was, then does them again and leaves them (to be kept or not).
    // ------------------------------------------------------------------------------------------------

    /// <summary>BRUTALFPS_EDITOR=pick: looks <paramref name="pitch"/> up (down, negative) and picks what's straight ahead, as a click would.</summary>
    /// <summary>Looks <paramref name="pitch"/> up (down, negative), for the scripted checks.</summary>
    public void Look(float pitch) => _camera.Pitch = pitch;

    public void PickAhead(float pitch)
    {
        _camera.Pitch = pitch;
        if (!level.PickEditable(_camera.Position, RayThrough(float2.Zero), 500f, includeBig: false, out var hit, out var distance))
        {
            Engine.Log.Write("[EDITOR] PICK nothing straight ahead");
            return;
        }
        var group = new HashSet<EditRef>();
        level.CollectGroup(hit, group);
        _selection.UnionWith(group);
        Engine.Log.Write($"[EDITOR] PICK {Describe()} at {distance:0.00} m: {group.Count} items, outline {level.OutlineOf(hit)}");
    }

    public void SelfTest(bool keep)
    {
        void Log(string text) => Engine.Log.Write($"[EDITOR] TEST {text}");
        var center = float2.Zero;
        if (!level.PickEditable(_camera.Position, RayThrough(center), 500f, includeBig: false, out var hit, out var distance))
        {
            Log("nothing straight ahead to pick");
            return;
        }
        var group = new HashSet<EditRef>();
        level.CollectGroup(hit, group);
        _selection.UnionWith(group);
        Log($"picked {Describe()} at {distance:0.0} m ({group.Count} items in its group), bounds {level.BoundsOf(hit)}");
        var before = group.ToDictionary(i => i, level.BoundsOf);

        Change("Move", item => level.PoseOf(item).Then(float3.Zero, 0, new float3(1.5f, 0, 0)));
        Log($"moved: now {level.BoundsOf(hit)}");
        Rotate(fine: false);
        Log($"turned: now {level.BoundsOf(hit)}");
        Duplicate();
        Log($"copied: {_selection.Count} copies, the first at {level.BoundsOf(_selection.First())}");
        Delete();
        Log("deleted the copies");
        _selection.UnionWith(group);
        _renaming = "Test name";
        FinishRename(keep: true);
        Log($"named it: {Describe()}");
        // Ctrl+Shift+drag: a copy made where it is, dragged 2 m along x, let go.
        _selection.Clear();
        _selection.UnionWith(group);
        var undoBefore = _undo.Count;
        if (Duplicate(float3.Zero))
        {
            StartMove(level.BoundsOf(hit).Center);
            _copyDrag = true;
            _dragMove = new float3(2f, 0, 0);
            foreach (var (item, start) in _dragStart)
                level.SetPose(item, start.Then(float3.Zero, 0, _dragMove));
            EndMove();
            Log($"dragged off a copy: {_selection.Count} items, {_undo.Count - undoBefore} undo step, the first at {level.BoundsOf(_selection.First())}");
            Delete();
            Log("deleted the dragged copy");
        }
        _selection.Clear();
        var crate = _palette.FindIndex(e => e.Model == "crate");
        if (crate >= 0)
        {
            _dragStart.Clear();
            var ahead = _camera.Position + _camera.Forward * 4f;
            Spawn(_palette[crate], new float3(ahead.X, _camera.Position.Y - 1.7f, ahead.Z));
            Record("Add crate", [.. _dragStart.Keys.Select(item => (item, new EditPose(0, float3.Zero, Deleted: true), level.PoseOf(item)))]);
            Log($"added a crate ({_dragStart.Count} items with its collision) at {level.BoundsOf(_dragStart.Keys.First())}");
        }
        var steps = _undo.Count;
        while (_undo.Count > 0)
            Undo(quiet: true);
        var restored = before.All(kv => level.BoundsOf(kv.Key) == kv.Value && level.PoseOf(kv.Key).IsNone);
        Log($"undid {steps} steps: {(restored ? "everything is back where it was" : "NOT RESTORED")}");
        while (_redo.Count > 0)
            Redo();
        Log($"redid {steps} steps: {level.Describe(hit)} is at {level.BoundsOf(hit)}");
        _dragStart.Clear();
        _status = "";
        if (keep)
            level.SaveEdits();
    }

    // ------------------------------------------------------------------------------------------------
    // The asset list
    // ------------------------------------------------------------------------------------------------

    private static List<PaletteEntry> BuildPalette()
    {
        var wood = new float3(0.46f, 0.34f, 0.22f);
        var concrete = new float3(0.50f, 0.48f, 0.44f);
        var steel = new float3(0.24f, 0.24f, 0.25f);
        (string Label, string Model, float3 Tint)[] models =
        [
            ("Container 40ft", "container", new(0.55f, 0.18f, 0.12f)),
            ("Container 20ft", "container_20", new(0.20f, 0.42f, 0.24f)),
            ("Crate", "crate", wood),
            ("Pallet", "pallet", wood * 0.85f),
            ("Oil drum", "drum", new(0.14f, 0.24f, 0.45f)),
            ("Wheelie bin", "bin", new(0.20f, 0.30f, 0.22f)),
            ("Water tank", "tank", new(0.62f, 0.62f, 0.6f)),
            ("Air con unit", "ac_unit", new(0.7f, 0.7f, 0.68f)),
            ("CCTV dome", "cctv_dome", float3.One),
            ("Hanging lamp", "lamp", steel),
            ("Light pole", "light_pole", steel * 1.6f),
            ("Flood mast", "flood_mast", new(0.6f, 0.6f, 0.58f)),
            ("Fuel pump", "fuel_pump", new(0.75f, 0.2f, 0.15f)),
            ("Garden chair", "garden_chair", new(0.85f, 0.85f, 0.82f)),
            ("Garden table", "garden_table", new(0.85f, 0.85f, 0.82f)),
            ("Sedan", "sedan", new(0.48f, 0.5f, 0.54f)),
            ("Ute", "ute", new(0.42f, 0.06f, 0.05f)),
            ("4WD wagon", "wagon", new(0.86f, 0.86f, 0.83f)),
            ("Transit van", "transit", new(0.9f, 0.9f, 0.9f)),
            ("Gun nest", "gun_nest", new(0.4f, 0.38f, 0.3f)),
            ("Hanging parka", "hanging_parka", new(0.7f, 0.3f, 0.1f)),
            ("Snow trousers", "hanging_snow_trousers", new(0.2f, 0.2f, 0.25f)),
            ("Gum tree A", "gum_tree_a", float3.One),
            ("Gum tree B", "gum_tree_b", float3.One),
            ("Gum tree C", "gum_tree_c", float3.One),
            ("Saltbush A", "saltbush_a", float3.One),
            ("Saltbush B", "saltbush_b", float3.One),
            ("Spinifex", "spinifex", float3.One),
            ("Jungle tree A", "jungle_tree_a", float3.One),
            ("Jungle tree B", "jungle_tree_b", float3.One),
            ("Jungle giant", "jungle_giant_a", float3.One),
            ("Banana plant", "jungle_banana_a", float3.One),
            ("Fern", "jungle_fern_a", float3.One),
            ("Shrub", "jungle_shrub_a", float3.One),
            ("Sapling", "jungle_sapling_a", float3.One),
            ("Ground palm", "jungle_ground_palm", float3.One),
            ("Walking palm", "walking_palm_a", float3.One),
            ("Ship crane", "ship_crane", new(0.86f, 0.64f, 0.06f)),
            ("Gantry crane", "gantry_crane", new(0.86f, 0.64f, 0.06f)),
        ];
        var list = new List<PaletteEntry>
        {
            // Boxes in the levels' own materials.
            new("Concrete block 1m", null, concrete, new(1, 1, 1), Material.Concrete),
            new("Concrete wall 4x3m", null, concrete, new(4, 3, 0.3f), Material.Concrete),
            new("Brick wall 4x3m", null, new(0.62f, 0.42f, 0.34f), new(4, 3, 0.3f), Material.Brick),
            new("Painted wall 4x3m", null, new(0.78f, 0.76f, 0.7f), new(4, 3, 0.15f), Material.WallPaint),
            new("Floor slab 4x4m", null, new(0.40f, 0.37f, 0.33f), new(4, 0.2f, 4), Material.Floor),
            new("Plank wall 3x2.4m", null, wood, new(3, 2.4f, 0.1f), Material.Wood),
            new("Tin sheet 3x2.5m", null, new(0.6f, 0.6f, 0.58f), new(3, 2.5f, 0.08f), Material.Galvanized),
            new("Rusty plate 2x2m", null, new(0.30f, 0.27f, 0.25f), new(2, 0.06f, 2), Material.RustyMetal),
            new("Steel beam 4m", null, steel, new(4, 0.3f, 0.3f), Material.Steel),
            new("Steel post 3m", null, steel, new(0.2f, 3, 0.2f), Material.Steel),
            new("Wooden box 1m", null, wood, new(1, 1, 1), Material.Wood),
            new("Sandbag wall 2m", null, new(0.55f, 0.5f, 0.38f), new(2, 1, 0.6f), Material.Canvas),
        };
        var models3d = Path.Combine(AppContext.BaseDirectory, "Assets", "Models");
        foreach (var (label, model, tint) in models)
            if (File.Exists(Path.Combine(models3d, model + ".glb")))
                list.Insert(list.Count, new PaletteEntry(label, model, tint));
        // In alphabetical order, the models and the boxes together.
        return [.. list.OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The places to fly to: each level's way in (only those loaded, on the firing range or a factory raid).</summary>
    public static JumpPoint[] JumpPoints(bool rangeOnly, bool factoryRaid)
    {
        var eye = new float3(0, 1.7f, 0);
        if (rangeOnly)
            return [new("Firing range", Level.RangeFiringPoint + eye, 0f)];
        var all = new List<JumpPoint>
        {
            new("Warehouse", new float3(0, 1.7f, 22f), 0f),
            new("Factory", new float3(-4f, 1.7f, -46f), float.Pi * 0.95f),
            new("Police", Level.PolicePortalFloorPoint + new float3(2.5f, 0, 0) + eye, float.Pi / 2),
        };
        if (factoryRaid)
            return [.. all];
        all.AddRange(
        [
            new("Yard", Level.YardPortalFloorPoint + new float3(0, 0, -5) + eye, float.Pi),
            new("Outback", Level.OutbackPortalFloorPoint + new float3(5, 0, 0) + eye, -float.Pi / 2),
            new("Roadhouse", Level.OutbackOrigin + new float3(1.5f, 0, -62) + eye, 0.25f),
            new("Range", Level.RangeFiringPoint + eye, 0f),
            new("Cellar", Level.CellarPortalFloorPoint + new float3(0, 0, -3.5f) + eye, float.Pi),
            new("Bunker", Level.BunkerOrigin + new float3(-4f, 0, 0) + eye, float.Pi / 2),
            new("Arctic", Level.ArcticPortalFloorPoint + new float3(0, 0, -3f) + eye, 0f),
            new("Station", Level.ArcticOrigin + new float3(24f, 0, -28f) + eye, 0.7f),
            new("Ship", Level.ShipPortalFloorPoint + new float3(1.2f, 0, 0) + eye, float.Pi / 2),
            new("Jungle", Level.JunglePortalFloorPoint + new float3(0, 0, -3f) + eye, 0f),
            new("Prison", PrisonIntro.StartPosition + eye, PrisonIntro.StartYaw),
            new("Flats", Level.FlatsStart + eye, float.Pi / 2),
            new("Rio", Level.RioStart + eye, Level.RioStartYaw),
        ]);
        return [.. all];
    }
}
