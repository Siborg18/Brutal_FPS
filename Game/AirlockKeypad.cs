using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The police vault airlock's keypads, close up. At a keypad whose door is shut, E brings the keypad up over the view:
/// type the code (<see cref="Level.AirlockCode"/>) on the number keys (top row or numpad; Backspace rubs out a digit)
/// and after the fourth digit it checks it: ACCESS GRANTED and the door opens (or the airlock cycles to it), or DENIED,
/// a red flash, and the entry clears. E, Esc or walking away puts it down. At an open door's keypad E just shuts the
/// door (no code). While it's up the gun doesn't fire or change.
/// </summary>
public sealed class AirlockKeypad
{
    private const float ResultTime = 0.9f;   // seconds GRANTED / DENIED shows

    private static readonly GlfwKey[] Digits =
    [
        GlfwKey.Number0, GlfwKey.Number1, GlfwKey.Number2, GlfwKey.Number3, GlfwKey.Number4,
        GlfwKey.Number5, GlfwKey.Number6, GlfwKey.Number7, GlfwKey.Number8, GlfwKey.Number9,
    ];

    private static readonly GlfwKey[] PadDigits =
    [
        GlfwKey.Kp0, GlfwKey.Kp1, GlfwKey.Kp2, GlfwKey.Kp3, GlfwKey.Kp4,
        GlfwKey.Kp5, GlfwKey.Kp6, GlfwKey.Kp7, GlfwKey.Kp8, GlfwKey.Kp9,
    ];

    private string _entry = "";
    private float _result;        // seconds left of the GRANTED / DENIED message
    private bool _granted;
    private float _keyFlash;      // the last key pressed lights up briefly
    private char _lastKey;
    private float _time;
    private string _label = "AIRLOCK";   // the name on the display (the keypad's door)

    public bool IsOpen { get; private set; }

    /// <summary>Puts the keypad down (Esc). False if it wasn't up.</summary>
    public bool Close()
    {
        if (!IsOpen)
            return false;
        IsOpen = false;
        Log.Write("[DOOR] Keypad put down");
        return true;
    }

    public void Update(float dt, Input input, Level level, float3 player, bool playing)
    {
        _time += dt;
        _keyFlash = float.Max(0f, _keyFlash - dt);
        if (!IsOpen)
        {
            if (playing && input.WasPressed(GlfwKey.E) && level.AirlockKeypadInReach && !level.AirlockBusy)
            {
                if (level.AirlockNeedsCode)
                {
                    IsOpen = true;
                    _entry = "";
                    _result = 0f;
                    _label = level.KeypadLabel;
                    Log.Write("[DOOR] Keypad up");
                }
                else
                {
                    level.UseAirlockKeypad(player);
                }
            }
            return;
        }

        if (!level.AirlockKeypadInReach)
        {
            Close();   // walked away
            return;
        }
        if (_result > 0f)
        {
            _result -= dt;
            if (_result > 0f)
                return;
            _entry = "";
            if (_granted)
            {
                IsOpen = false;
                level.UseAirlockKeypad(player);
            }
            return;
        }
        if (!playing)
            return;
        // The mouse: a click on a key presses it (* rubs out a digit, # puts the keypad down).
        var clicked = '\0';
        if (input.UiCursor && input.WasPressed(GlfwMouseButton.Left))
        {
            var cursor = input.CursorSquare;
            foreach (var (label, center) in KeyCenters())
                if (float.Abs(cursor.X - center.X) <= KeySize / 2 && float.Abs(cursor.Y - center.Y) <= KeySize / 2)
                    clicked = label;
            if (clicked != '\0')
            {
                _lastKey = clicked;
                _keyFlash = 0.15f;
            }
        }
        if (input.WasPressed(GlfwKey.E) || clicked == '#')
        {
            Close();
            return;
        }
        if ((input.WasPressed(GlfwKey.Backspace) || clicked == '*') && _entry.Length > 0)
            _entry = _entry[..^1];
        for (var d = 0; d < 10 && _entry.Length < Level.AirlockCode.Length; d++)
        {
            if (!input.WasPressed(Digits[d]) && !input.WasPressed(PadDigits[d]) && clicked != (char)('0' + d))
                continue;
            _lastKey = (char)('0' + d);
            _keyFlash = 0.15f;
            _entry += _lastKey;
        }
        if (_entry.Length == Level.AirlockCode.Length)
        {
            _granted = _entry == Level.AirlockCode;
            _result = ResultTime;
            if (_granted)
                Log.Write("[DOOR] Keypad: access granted");
            else
                level.DenyAirlockKeypad("access denied");
        }
    }

    private static readonly float3 Backdrop = new(0.02f, 0.02f, 0.025f);
    private static readonly float3 Body = new(0.36f, 0.37f, 0.39f);
    private static readonly float3 BodyEdge = new(0.55f, 0.56f, 0.58f);
    private static readonly float3 Lcd = new(0.10f, 0.22f, 0.15f);
    private static readonly float3 LcdInk = new(0.55f, 1.0f, 0.65f);
    private static readonly float3 Key = new(0.11f, 0.11f, 0.12f);
    private static readonly float3 KeyLit = new(0.35f, 0.36f, 0.4f);
    private static readonly float3 Ink = new(0.85f, 0.85f, 0.85f);
    private static readonly float3 Green = new(0.15f, 1.0f, 0.3f);
    private static readonly float3 Red = new(1.0f, 0.12f, 0.06f);

    private const float KeySize = 0.15f;
    private static readonly float2 PadCenter = new(0, 0.06f);
    private static readonly float2 PadSize = new(0.62f, 1.13f);

    /// <summary>The twelve keys in four rows (1 2 3 / 4 5 6 / 7 8 9 / * 0 #), each with where its middle is.</summary>
    private static IEnumerable<(char Label, float2 Center)> KeyCenters()
    {
        const string labels = "123456789*0#";
        var top = PadCenter.Y + PadSize.Y / 2;
        for (var i = 0; i < labels.Length; i++)
            yield return (labels[i], new float2((i % 3 - 1) * (KeySize + 0.04f), top - 0.4f - (i / 3) * (KeySize + 0.04f)));
    }

    /// <summary>Draws the keypad over the view (in the overlay pass, after the HUD).</summary>
    public void Draw(Renderer renderer, int2 framebufferSize)
    {
        if (!IsOpen)
            return;
        renderer.BeginOverlay();
        var aspect = framebufferSize.Y > 0 ? (float)framebufferSize.X / framebufferSize.Y : 1f;
        var px = 2f / float.Max(framebufferSize.Y, 1);
        var font = float.Max(2f, float.Round(framebufferSize.Y / 220f)) * px;

        // The keypad's brushed steel body, its LCD, and twelve keys in four rows (1 2 3 / 4 5 6 / 7 8 9 / * 0 #).
        var center = PadCenter;
        var size = PadSize;
        PcTerminal.Rect(renderer, aspect, center, size + new float2(8 * px, 8 * px), BodyEdge);
        PcTerminal.Rect(renderer, aspect, center, size, Body);
        var top = center.Y + size.Y / 2;

        var lcdCenter = new float2(0, top - 0.17f);
        PcTerminal.Rect(renderer, aspect, lcdCenter, new float2(0.5f, 0.17f), Backdrop);
        PcTerminal.Rect(renderer, aspect, lcdCenter, new float2(0.47f, 0.14f), Lcd);
        string text;
        float3 ink;
        if (_result > 0f)
        {
            text = _granted ? "GRANTED" : "DENIED";
            ink = _granted ? Green : Red;
        }
        else
        {
            text = string.Concat(Enumerable.Range(0, Level.AirlockCode.Length).Select(i => i < _entry.Length ? "* " : (i == _entry.Length && (int)(_time * 2.5f) % 2 == 0 ? "  " : "_ "))).TrimEnd();
            ink = LcdInk;
        }
        var textW = (text.Length * BlockFont.Advance - 1) * font;
        PcTerminal.Text(renderer, aspect, text, new float2(-textW / 2, lcdCenter.Y + BlockFont.Height * font / 2), font, ink);

        // Status LED over the LCD.
        var led = _result > 0f ? (_granted ? Green : Red) : new float3(1.0f, 0.55f, 0.08f);
        PcTerminal.Rect(renderer, aspect, new float2(0.21f, top - 0.05f), new float2(0.025f, 0.025f), led);
        PcTerminal.Text(renderer, aspect, _label, new float2(-0.27f, top - 0.035f), font * 0.6f, Ink * 0.8f);

        foreach (var (label, c) in KeyCenters())
        {
            var lit = _keyFlash > 0f && label == _lastKey;
            PcTerminal.Rect(renderer, aspect, c, new float2(KeySize, KeySize), lit ? KeyLit : Key);
            var lw = (BlockFont.Advance - 1) * font * 1.4f;
            PcTerminal.Text(renderer, aspect, label.ToString(), new float2(c.X - lw / 2, c.Y + BlockFont.Height * font * 1.4f / 2), font * 1.4f, Ink);
        }

        // How to use it, under the keypad.
        const string hint = "0-9/CLICK CODE   BACKSPACE/* CLEAR   E/# CLOSE";
        var hintPx = font * 0.75f;
        var hintW = (hint.Length * BlockFont.Advance - 1) * hintPx;
        var hintY = center.Y - size.Y / 2 - 0.06f;
        PcTerminal.Rect(renderer, aspect, new float2(0, hintY - BlockFont.Height * hintPx / 2), new float2(hintW + 0.06f, BlockFont.Height * hintPx + 0.04f), Backdrop);
        PcTerminal.Text(renderer, aspect, hint, new float2(-hintW / 2, hintY), hintPx, Ink);
    }
}
