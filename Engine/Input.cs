using Brutal.GlfwApi;
using Brutal.Numerics;
using static Brutal.GlfwApi.Glfw.Constants;

namespace BrutalFps.Engine;

/// <summary>
/// Event-driven keyboard/mouse state with per-frame "pressed" edges and mouse capture.
/// Built on GLFW callbacks rather than polling so a tap shorter than one frame is never lost.
/// </summary>
public sealed class Input
{
    private readonly GlfwWindow _window;

    private readonly HashSet<GlfwKey> _heldKeys = [];
    private readonly HashSet<GlfwMouseButton> _heldButtons = [];

    // Filled by callbacks during Glfw.PollEvents(), published to the frame by Update().
    private readonly HashSet<GlfwKey> _pendingKeyPresses = [];
    private readonly HashSet<GlfwMouseButton> _pendingButtonPresses = [];
    private readonly HashSet<GlfwKey> _keyPresses = [];
    private readonly HashSet<GlfwMouseButton> _buttonPresses = [];
    private readonly HashSet<GlfwMouseButton> _pendingButtonReleases = [];
    private readonly HashSet<GlfwMouseButton> _buttonReleases = [];

    private double2 _lastCursor;
    private bool _resetCursor = true;

    public Input(GlfwWindow window)
    {
        _window = window;

        window.OnKey += (_, key, scancode, action, _) =>
        {
            if (action == GlfwKeyAction.Press)
            {
                _heldKeys.Add(key);
                _pendingKeyPresses.Add(key);
                if (key == GlfwKey.KpAdd || IsPlusKey(key, scancode))
                    _pendingRespawn = true;
            }
            else if (action == GlfwKeyAction.Release)
            {
                _heldKeys.Remove(key);
            }
            else if (action == GlfwKeyAction.Repeat)
            {
                _pendingKeyRepeats.Add(key);
            }
        };

        // '+' however it's typed (Shift and the "=" key on a US layout, its own key on others).
        window.OnChar += (_, codepoint) =>
        {
            if (codepoint == '+')
                _pendingRespawn = true;
            if (codepoint is >= 32 and < 0xD800)
                _pendingTyped.Append((char)codepoint);
        };

        window.OnMouseButton += (_, button, action, _) =>
        {
            if (action == GlfwButtonAction.Press)
            {
                _heldButtons.Add(button);
                _pendingButtonPresses.Add(button);
            }
            else
            {
                _heldButtons.Remove(button);
                _pendingButtonReleases.Add(button);
            }
        };

        window.OnScroll += (_, offset) => _pendingScroll += offset.Y;

        // Losing focus can swallow release events; drop everything so nothing gets stuck down.
        window.OnFocus += (_, focus) =>
        {
            if (focus == GlfwFocusEvent.LostFocus)
            {
                _heldKeys.Clear();
                _heldButtons.Clear();
            }
        };
    }

    private bool _pendingRespawn;
    private double _pendingScroll;
    private readonly System.Text.StringBuilder _pendingTyped = new();
    private readonly HashSet<GlfwKey> _pendingKeyRepeats = [], _keyRepeats = [];

    /// <summary>The text typed this frame (as the keyboard layout makes it), for text boxes.</summary>
    public string Typed { get; private set; } = "";

    /// <summary>Pressed this frame, or held long enough that it repeats (for Backspace in a text box).</summary>
    public bool WasPressedOrRepeated(GlfwKey key) => _keyPresses.Contains(key) || _keyRepeats.Contains(key);

    /// <summary>
    /// Whole notches of the mouse wheel this frame: positive scrolling away from you, negative towards you. A touchpad's
    /// or a free-spinning wheel's fractions add up until they make a notch.
    /// </summary>
    public int ScrollSteps { get; private set; }

    /// <summary>
    /// The respawn key went down this frame: '+', on the keypad or the main keyboard (the key that types '+' or '=' on
    /// this keyboard layout, so on a US layout the "=/+" key works with or without Shift).
    /// </summary>
    public bool RespawnPressed { get; private set; }

    private static bool IsPlusKey(GlfwKey key, int scancode)
    {
        if (key == GlfwKey.Equal)
            return true;
        try
        {
            return Glfw.GetKeyName(key, scancode) is "+" or "=";
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Mouse movement in pixels since last frame (zero when the mouse isn't captured).</summary>
    public float2 MouseDelta { get; private set; }

    public bool MouseCaptured { get; private set; }

    /// <summary>
    /// Where the (released) mouse pointer is, in the overlay's "square" space: y from -1 (bottom) to 1 (top), x from
    /// -aspect to aspect.
    /// </summary>
    public float2 CursorSquare
    {
        get
        {
            var size = _window.Size;
            if (size.X <= 0 || size.Y <= 0)
                return float2.Zero;
            var cursor = _window.CursorPos;
            var aspect = (float)size.X / size.Y;
            return new float2(((float)cursor.X / size.X * 2f - 1f) * aspect, 1f - (float)cursor.Y / size.Y * 2f);
        }
    }

    /// <summary>Call once per frame, straight after Glfw.PollEvents().</summary>
    public void Update()
    {
        _keyPresses.Clear();
        _keyPresses.UnionWith(_pendingKeyPresses);
        _pendingKeyPresses.Clear();
        _keyRepeats.Clear();
        _keyRepeats.UnionWith(_pendingKeyRepeats);
        _pendingKeyRepeats.Clear();
        Typed = _pendingTyped.ToString();
        _pendingTyped.Clear();

        _buttonPresses.Clear();
        _buttonPresses.UnionWith(_pendingButtonPresses);
        _pendingButtonPresses.Clear();
        _buttonReleases.Clear();
        _buttonReleases.UnionWith(_pendingButtonReleases);
        _pendingButtonReleases.Clear();
        if (_swallowLeft && !_heldButtons.Contains(GlfwMouseButton.Left))
            _swallowLeft = false;

        RespawnPressed = _pendingRespawn;
        _pendingRespawn = false;

        ScrollSteps = (int)_pendingScroll;
        _pendingScroll -= ScrollSteps;

        var cursor = _window.CursorPos;
        if (_resetCursor)
        {
            _lastCursor = cursor;
            _resetCursor = false;
        }

        var delta = cursor - _lastCursor;
        _lastCursor = cursor;
        MouseDelta = MouseCaptured && !UiCursor ? new float2((float)delta.X, (float)delta.Y) : float2.Zero;
    }

    /// <summary>Forgets this frame's key and button presses (a menu used them), so the game doesn't act on them too.</summary>
    public void ConsumePresses()
    {
        _keyPresses.Clear();
        _keyRepeats.Clear();
        Typed = "";
        _buttonPresses.Clear();
        _buttonReleases.Clear();
        ScrollSteps = 0;
    }

    /// <summary>Test scripts: notches of the mouse wheel this frame.</summary>
    public void InjectScroll(int steps) => ScrollSteps += steps;

    /// <summary>Moves the mouse by <paramref name="pixels"/> this frame (test scripts).</summary>
    public void InjectMouse(float2 pixels) => MouseDelta += pixels;

    /// <summary>Test scripts: a key press this frame.</summary>
    public void InjectPress(GlfwKey key) => _keyPresses.Add(key);

    /// <summary>Test scripts: a mouse click this frame.</summary>
    public void InjectPress(GlfwMouseButton button) => _buttonPresses.Add(button);

    /// <summary>Test scripts: the respawn key this frame.</summary>
    public void InjectRespawn() => RespawnPressed = true;

    /// <summary>Test scripts: the trigger (left mouse button) held down.</summary>
    public bool ScriptedFire { get; set; }

    /// <summary>Test scripts: aiming (the right mouse button) held.</summary>
    public bool ScriptedAim { get; set; }

    /// <summary>Keys held down by a test script (see BRUTALFPS_SCRIPT in Program.cs).</summary>
    public HashSet<GlfwKey> ScriptedKeys { get; } = [];

    /// <summary>True while the key is held (or if it was tapped at any point during this frame).</summary>
    public bool IsDown(GlfwKey key) => _heldKeys.Contains(key) || _keyPresses.Contains(key) || ScriptedKeys.Contains(key);

    /// <summary>True only on the frame the key went down.</summary>
    public bool WasPressed(GlfwKey key) => _keyPresses.Contains(key);

    public bool IsDown(GlfwMouseButton button) => !Swallowed(button) && (_heldButtons.Contains(button) || _buttonPresses.Contains(button));

    public bool WasPressed(GlfwMouseButton button) => !Swallowed(button) && _buttonPresses.Contains(button);

    /// <summary>True only on the frame the button came up.</summary>
    public bool WasReleased(GlfwMouseButton button) => _buttonReleases.Contains(button);

    // The left button that was down when a screen's pointer went away (a click on its last button) isn't the trigger.
    private bool _swallowLeft;
    private bool Swallowed(GlfwMouseButton button) => _swallowLeft && button == GlfwMouseButton.Left;

    /// <summary>
    /// A screen in play (a gun locker, the inventory, a PC, a keypad) has the pointer: the mouse stays the game's (it's
    /// still "captured", so the world runs on) but the system's pointer shows and the view doesn't turn.
    /// </summary>
    public bool UiCursor { get; private set; }

    /// <summary>Shows the pointer over a screen in play, or takes it away again. Does nothing while the mouse isn't captured.</summary>
    public void SetUiCursor(bool on)
    {
        if (on == UiCursor || (on && !MouseCaptured))
            return;
        UiCursor = on;
        _window.SetInputMode(GlfwInputMode.Cursor, (int)(on ? GlfwCursorMode.Normal : GlfwCursorMode.Disabled));
        if (on)
        {
            var size = _window.Size;
            _window.CursorPos = new double2(size.X / 2.0, size.Y / 2.0);   // (from the middle of the screen, where you were looking)
        }
        else
        {
            _swallowLeft = _heldButtons.Contains(GlfwMouseButton.Left) || _buttonPresses.Contains(GlfwMouseButton.Left);
        }
        _resetCursor = true;
    }

    public void SetMouseCaptured(bool captured)
    {
        MouseCaptured = captured;
        UiCursor = false;
        _window.SetInputMode(GlfwInputMode.Cursor, (int)(captured ? GlfwCursorMode.Disabled : GlfwCursorMode.Normal));

        if (captured && Glfw.RawMotionSupported())
            _window.SetInputMode(GlfwInputMode.RawMouseMotion, GLFW_TRUE);

        _resetCursor = true;
    }
}
