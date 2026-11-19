using System.Diagnostics;
using System.Globalization;
using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>Body armour for a raid: none, a light chest plate, a heavier chest plate, or full cover.</summary>
public enum ArmourKind { None, Light, Medium, Heavy }

/// <summary>
/// What the player's armour leaves of a hit: on the body (chest and back), the arms and legs, the head (a helmet), and a
/// frag's blast. (How fast you can move in it is the whole kit's weight: <see cref="RaidKit.SpeedScale"/>.)
/// </summary>
public sealed record RaidArmour(float Body, float Limbs, float Head, float Blast)
{
    public static readonly RaidArmour None = new(1f, 1f, 1f, 1f);

    public static RaidArmour Of(ArmourKind kind, bool helmet)
    {
        var head = helmet ? HelmetHead : 1f;
        return kind switch
        {
            ArmourKind.Light => new(0.55f, 1f, head, 0.9f),
            ArmourKind.Medium => new(0.3f, 1f, head, 0.8f),
            ArmourKind.Heavy => new(0.3f, 0.5f, head, 0.6f),
            _ => None with { Head = head },
        };
    }

    /// <summary>What a helmet leaves of a hit to the head.</summary>
    public const float HelmetHead = 0.5f;
}

/// <summary>What's taken on a raid: chosen on <see cref="RaidLoadout"/>'s screen, and kept for the next one.</summary>
public sealed class RaidKit
{
    public const int GrenadeSlots = 6;

    public ArmourKind Armour = ArmourKind.Medium;
    public bool Helmet = true;
    public bool Goggles = true;
    public string Primary = "AK-47";
    public string Secondary = "Pistol";
    public int Flashbangs = 2, Frags = 2, Smokes = 2;

    public int Grenades => Flashbangs + Frags + Smokes;

    // ------------------------------------------------------------------------------------------------ weight

    /// <summary>
    /// What each thing weighs (kg), loaded. The M249 with its belts is the heaviest gun by far, so heavy armour and the
    /// M249 is the heaviest kit there is; a submachine gun and the pistol, with no armour, the lightest.
    /// </summary>
    private static readonly Dictionary<string, float> GunWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pistol"] = 1.0f, ["UMP40"] = 2.5f, ["L92A1"] = 3.0f, ["L80A1"] = 2.0f,
        ["AK-47"] = 4.3f, ["Shotgun"] = 3.8f, ["AR-15"] = 3.6f, ["G3"] = 5.0f, ["M249"] = 21f,
        ["L85A2"] = 4.9f, ["L85A3"] = 4.6f, ["L403A1"] = 4.0f, ["L143A2"] = 3.4f, ["L119A1"] = 3.3f, ["L129A1"] = 5.0f, ["L135A1"] = 14f,
    };

    public const float HelmetWeight = 1.5f, GogglesWeight = 0.6f, GrenadeWeight = 0.45f;

    public static float GunWeight(string name) => GunWeights.TryGetValue(name, out var kg) ? kg : 4f;

    public static float ArmourWeight(ArmourKind kind) => kind switch
    {
        ArmourKind.Light => 4f,
        ArmourKind.Medium => 8f,
        ArmourKind.Heavy => 16f,
        _ => 0f,
    };

    /// <summary>The submachine guns: a primary or a secondary, but one as the primary leaves only the pistol for the secondary.</summary>
    public static readonly HashSet<string> Smgs = new(["UMP40", "L92A1", "L80A1"], StringComparer.OrdinalIgnoreCase);

    public static bool IsSmg(string name) => Smgs.Contains(name);

    public static bool IsLmg(string name) => name.Equals("M249", StringComparison.OrdinalIgnoreCase);

    /// <summary>Everything carried, in kg.</summary>
    public float Weight => ArmourWeight(Armour) + (Helmet ? HelmetWeight : 0f) + (Goggles ? GogglesWeight : 0f)
                           + GunWeight(Primary) + GunWeight(Secondary) + Grenades * GrenadeWeight;

    /// <summary>The lightest kit there can be (a submachine gun and the pistol, nothing else): the fastest.</summary>
    public const float LightestWeight = 3f;

    /// <summary>
    /// How fast you move, against how you move with nothing to speak of: 112% for the lightest kit, a point less for every
    /// kg more, down to 72% at the heaviest.
    /// </summary>
    public float SpeedScale => float.Clamp(1.12f - 0.01f * (Weight - LightestWeight), 0.72f, 1.12f);

    /// <summary>Heavy armour and the M249: too much to sprint with, and you clank.</summary>
    public bool Overloaded => Armour == ArmourKind.Heavy && IsLmg(Primary);

    /// <summary>
    /// How much the kit drags on changing stance (<see cref="PlayerController.KitLoad"/>): nothing up to 12 kg, rising to
    /// 1 at 38 kg; overloaded is always 1. Heavy armour with a rifle comes to about a half.
    /// </summary>
    public float StanceLoad => Overloaded ? 1f : float.Clamp((Weight - 12f) / 26f, 0f, 1f);

    /// <summary>How far your footsteps carry, against the usual: quieter light, louder heavy, much louder overloaded.</summary>
    public float NoiseScale => Overloaded ? 2f : float.Clamp(0.8f + 0.017f * (Weight - LightestWeight), 0.8f, 1.5f);

    /// <summary>A submachine gun as the primary: the secondary can only be the pistol.</summary>
    public void KeepRules()
    {
        if (IsSmg(Primary) && !Secondary.Equals("Pistol", StringComparison.OrdinalIgnoreCase))
            Secondary = "Pistol";
    }

    private const string SaveFile = "raid_loadout.txt";

    public static RaidKit Load()
    {
        var kit = new RaidKit();
        var path = Path.Combine(AppContext.BaseDirectory, SaveFile);
        if (!File.Exists(path))
            return kit;
        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.Split('=', 2) is not [var key, var value])
                    continue;
                value = value.Trim();
                _ = int.TryParse(value, CultureInfo.InvariantCulture, out var n);
                switch (key.Trim())
                {
                    case "armour" when Enum.TryParse<ArmourKind>(value, true, out var a): kit.Armour = a; break;
                    case "helmet": kit.Helmet = value == "1"; break;
                    case "goggles": kit.Goggles = value == "1"; break;
                    case "primary": kit.Primary = value; break;
                    case "secondary": kit.Secondary = value; break;
                    case "flashbangs": kit.Flashbangs = int.Clamp(n, 0, GrenadeSlots); break;
                    case "frags": kit.Frags = int.Clamp(n, 0, GrenadeSlots); break;
                    case "smokes": kit.Smokes = int.Clamp(n, 0, GrenadeSlots); break;
                }
            }
        }
        catch (IOException)
        {
            return new RaidKit();
        }

        while (kit.Grenades > GrenadeSlots)
        {
            if (kit.Smokes > 0) kit.Smokes--;
            else if (kit.Frags > 0) kit.Frags--;
            else kit.Flashbangs--;
        }
        kit.KeepRules();
        return kit;
    }

    public void Save()
    {
        try
        {
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, SaveFile),
            [
                $"armour={Armour.ToString().ToLowerInvariant()}", $"helmet={(Helmet ? 1 : 0)}", $"goggles={(Goggles ? 1 : 0)}",
                $"primary={Primary}", $"secondary={Secondary}",
                $"flashbangs={Flashbangs}", $"frags={Frags}", $"smokes={Smokes}",
            ]);
        }
        catch (IOException e)
        {
            Log.Write($"[LOADOUT] Couldn't save the raid loadout: {e.Message}");
        }
    }

    public override string ToString() =>
        $"{Armour.ToString().ToLowerInvariant()} armour, {(Helmet ? "helmet" : "no helmet")}, {(Goggles ? "goggles" : "no goggles")}, " +
        $"{Primary} and {Secondary}, {Flashbangs} flashbangs, {Frags} frags, {Smokes} smoke; " +
        string.Format(CultureInfo.InvariantCulture, "{0:0.0} kg, speed {1:0}%, footsteps x{2:0.00}{3}", Weight, SpeedScale * 100f, NoiseScale, Overloaded ? ", overloaded: no sprinting" : "");

    public RaidKit Copy() => (RaidKit)MemberwiseClone();
}

/// <summary>
/// The screen before a raid (the factory's or Antwerp's, not the prison intro): drawn like the inventory, it has five
/// sections along the top: ARMOUR (none, light, medium, heavy), HEAD (a helmet, goggles, both or neither), PRIMARY (one
/// of the long guns), SECONDARY (the pistol or a submachine gun) and GRENADES (six, shared out among flashbangs, frags
/// and smoke). It comes up as soon as the way in is chosen, and the raid loads behind it (Program.cs calls
/// <see cref="Frame"/> between the steps of loading); the guns' pictures come in once their models have. Space (or
/// START RAID) is done: if it's still loading then, it starts as soon as it's loaded.
/// </summary>
public sealed class RaidLoadout(RaidKit kit)
{
    private enum Section { Armour, Head, Primary, Secondary, Grenades }

    private static readonly string[] SectionNames = ["ARMOUR", "HEAD", "PRIMARY", "SECONDARY", "GRENADES"];
    private const int Columns = 4;

    private readonly record struct Option(string Name, string Kind);

    private static readonly Option[] ArmourOptions =
    [
        new("None", "No armour: quickest on your feet"),
        new("Light", "Light plate carrier: chest only"),
        new("Medium", "Heavier plates: chest only"),
        new("Heavy", "Full body armour"),
    ];

    private static readonly Option[] HeadOptions =
    [
        new("Neither", "Nothing on your head"),
        new("Helmet", "Ballistic helmet"),
        new("Goggles", "Night and thermal goggles"),
        new("Both", "Helmet with goggles"),
    ];

    private static readonly Option[] GrenadeOptions =
    [
        new("Flashbang", "Stun grenade"),
        new("Frag", "Fragmentation grenade"),
        new("Smoke", "Smoke grenade"),
    ];

    // The guns, in the gun locker's order, until the real ones are loaded (Bind): their names and what they are.
    private Option[] _primaries =
    [
        new("AK-47", "Kalashnikov assault rifle"), new("Shotgun", "Pump-action shotgun"), new("UMP40", "UMP submachine gun"), new("AR-15", "AR-15 carbine"),
        new("G3", "G3 battle rifle"), new("M249", "M249 light machine gun"), new("L85A2", "SA80 bullpup assault rifle"),
        new("L85A3", "SA80 bullpup assault rifle"), new("L403A1", "Knight's KS-1 rifle"), new("L143A2", "SIG MCX carbine"),
        new("L119A1", "Colt C8 SFW carbine"), new("L129A1", "LMT sharpshooter rifle"), new("L135A1", "Barrett M82A1 anti-materiel rifle"),
        new("L92A1", "MP5SD3 submachine gun"), new("L80A1", "MP5K submachine gun"),
    ];

    private Option[] _secondaries =
    [
        new("Pistol", "9 mm service pistol"), new("UMP40", "UMP submachine gun"), new("L92A1", "MP5SD3 submachine gun"), new("L80A1", "MP5K submachine gun"),
    ];

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly Aabb CanBounds = new(new float3(-0.03f, -0.06f, -0.03f), new float3(0.03f, 0.07f, 0.03f));

    // (Debug: BRUTALFPS_LOADOUT_SECTION=0 to 4 opens on that section, for screenshots.)
    private Section _section = (Section)int.Clamp(int.TryParse(Environment.GetEnvironmentVariable("BRUTALFPS_LOADOUT_SECTION"), out var s) ? s : 0, 0, 4);
    private int _picked = -1;   // (the one chosen, on the first frame)
    private int _hovered = -1;
    private readonly List<(float4 Box, int Index)> _cards = [];
    private readonly List<(float4 Box, Section Section)> _tabs = [];
    private float4 _startBox;
    private readonly OpticPicker _optics = new();

    // Once the guns are loaded.
    private GunLocker? _locker;
    private IReadOnlyList<LockerEntry> _catalog = [];
    private Flashbangs? _flashbangs;
    private Throwables? _throwables;

    public RaidKit Kit => kit;

    /// <summary>Up until the player is done with it and the raid has loaded.</summary>
    public bool IsOpen { get; private set; } = true;

    /// <summary>The player has pressed START RAID (it may still be loading).</summary>
    public bool Ready { get; private set; }

    /// <summary>Everything's loaded: once <see cref="Ready"/> too, it shuts.</summary>
    public bool Loaded { get; private set; }

    /// <summary>The guns are loaded: from now on the cards have their pictures, and the guns' own numbers.</summary>
    public void Bind(GunLocker locker, IReadOnlyList<LockerEntry> catalog, Flashbangs flashbangs, Throwables throwables)
    {
        (_locker, _catalog, _flashbangs, _throwables) = (locker, catalog, flashbangs, throwables);
        var primaries = new List<Option>();
        var secondaries = new List<Option>();
        foreach (var entry in catalog)
        {
            var option = new Option(entry.Gun.Name, entry.Stats.Kind);
            var pistol = entry.Gun.Name.Equals("Pistol", StringComparison.OrdinalIgnoreCase);
            if (entry.Stats.Kind.Contains("submachine", StringComparison.OrdinalIgnoreCase))
                RaidKit.Smgs.Add(entry.Gun.Name);
            if (pistol || RaidKit.IsSmg(entry.Gun.Name))
                secondaries.Add(option);
            if (!pistol)
                primaries.Add(option);   // (the submachine guns are primaries too)
        }
        if (primaries.Count > 0)
            _primaries = [.. primaries];
        if (secondaries.Count > 0)
            _secondaries = [.. secondaries];
        if (!_primaries.Any(o => o.Name.Equals(kit.Primary, StringComparison.OrdinalIgnoreCase)))
            kit.Primary = _primaries[0].Name;
        if (!_secondaries.Any(o => o.Name.Equals(kit.Secondary, StringComparison.OrdinalIgnoreCase)))
            kit.Secondary = _secondaries[0].Name;
        kit.KeepRules();
        if (_picked >= 0)
            _picked = int.Clamp(_picked, 0, Options.Length - 1);
    }

    /// <summary>A secondary that can't be taken with the primary chosen: anything but the pistol, with a submachine gun as the primary.</summary>
    private bool Barred(Section section, int index) =>
        section == Section.Secondary && RaidKit.IsSmg(kit.Primary) && !_secondaries[index].Name.Equals("Pistol", StringComparison.OrdinalIgnoreCase);

    /// <summary>Loading's done: if START RAID has been pressed, it shuts now.</summary>
    public void FinishLoading()
    {
        Loaded = true;
        if (Ready)
            Close();
    }

    private void Close()
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        kit.Save();
        Log.Write($"[LOADOUT] {kit}");
    }

    /// <summary>Debug (BRUTALFPS_LOADOUT=go): straight through, with the kit as it was saved.</summary>
    public void Accept()
    {
        Ready = true;
        if (Loaded)
            Close();
    }

    private Option[] Options => _section switch
    {
        Section.Armour => ArmourOptions,
        Section.Head => HeadOptions,
        Section.Primary => _primaries,
        Section.Secondary => _secondaries,
        _ => GrenadeOptions,
    };

    /// <summary>The option chosen in <paramref name="section"/>, as an index into its options.</summary>
    private int Chosen(Section section) => section switch
    {
        Section.Armour => (int)kit.Armour,
        Section.Head => (kit.Helmet ? 1 : 0) + (kit.Goggles ? 2 : 0),
        Section.Primary => Array.FindIndex(_primaries, o => o.Name.Equals(kit.Primary, StringComparison.OrdinalIgnoreCase)),
        Section.Secondary => Array.FindIndex(_secondaries, o => o.Name.Equals(kit.Secondary, StringComparison.OrdinalIgnoreCase)),
        _ => -1,
    };

    private string ChosenName(Section section) => section switch
    {
        Section.Armour => ArmourOptions[(int)kit.Armour].Name,
        Section.Head => HeadOptions[Chosen(Section.Head)].Name,
        Section.Primary => kit.Primary,
        Section.Secondary => kit.Secondary,
        _ => $"{kit.Grenades} / {RaidKit.GrenadeSlots}",
    };

    private int Count(int grenade) => grenade switch { 0 => kit.Flashbangs, 1 => kit.Frags, _ => kit.Smokes };

    private void AddGrenade(int grenade, int by)
    {
        if (by > 0 && kit.Grenades >= RaidKit.GrenadeSlots)
            return;
        switch (grenade)
        {
            case 0: kit.Flashbangs = int.Max(0, kit.Flashbangs + by); break;
            case 1: kit.Frags = int.Max(0, kit.Frags + by); break;
            default: kit.Smokes = int.Max(0, kit.Smokes + by); break;
        }
    }

    private void Choose(int index)
    {
        switch (_section)
        {
            case Section.Armour: kit.Armour = (ArmourKind)index; break;
            case Section.Head: (kit.Helmet, kit.Goggles) = ((index & 1) != 0, (index & 2) != 0); break;
            case Section.Primary:
                kit.Primary = _primaries[index].Name;
                kit.KeepRules();   // (a submachine gun: the pistol as the secondary)
                break;
            case Section.Secondary when !Barred(Section.Secondary, index): kit.Secondary = _secondaries[index].Name; break;
            case Section.Secondary: break;
            default: AddGrenade(index, 1); break;
        }
    }

    private void GoTo(Section section)
    {
        if (section == _section)
            return;
        _section = section;
        _picked = int.Max(0, Chosen(section));
    }

    // ------------------------------------------------------------------------------------------------ the screen's own frames

    /// <summary>
    /// One frame of the screen on its own (while the raid loads, and after, until it's left): the window's events, the
    /// keys and mouse, the screen drawn over black. <paramref name="eachFrame"/> is the game's every-frame keys (fullscreen).
    /// </summary>
    public void Frame(GlfwWindow window, Renderer renderer, Input input, Action eachFrame, float3 eye)
    {
        Glfw.PollEvents();
        input.Update();
        eachFrame();
        Update(input);
        var size = window.FramebufferSize;
        if (size.X > 0 && size.Y > 0)
        {
            renderer.ClearWindow(size);
            Draw(renderer, size, eye);
            if (Environment.GetEnvironmentVariable("BRUTALFPS_LOADOUT") is { } debug && debug.StartsWith("shot:", StringComparison.Ordinal) && Loaded)
            {
                Renderer.SaveScreenshot(Path.Combine(AppContext.BaseDirectory, debug[5..]), size);
                window.ShouldClose = true;
            }
        }
        window.SwapBuffers();
    }

    private void Update(Input input)
    {
        // The keys: Tab for the next section (Shift+Tab the last), the arrows (or the wheel) to pick, Enter to choose,
        // Backspace (or -) for a grenade fewer, Space to go.
        if (input.WasPressed(GlfwKey.Tab))
            GoTo((Section)(((int)_section + (input.IsDown(GlfwKey.LeftShift) ? 4 : 1)) % 5));
        if (_picked < 0)
            _picked = int.Max(0, Chosen(_section));
        _picked = GunLocker.Pick(input, int.Clamp(_picked, 0, Options.Length - 1), Options.Length, Columns);
        if (input.WasPressed(GlfwKey.Enter))
            Choose(_picked);
        if (_section == Section.Grenades && (input.WasPressed(GlfwKey.Backspace) || input.WasPressed(GlfwKey.Minus)))
            AddGrenade(_picked, -1);
        if (input.WasPressed(GlfwKey.Space))
            Accept();

        // The mouse: a click on a card picks and chooses it (no highlight just for being under the pointer) (a right click: a grenade fewer); a click on a tab goes there.
        var cursor = input.CursorSquare;
        // The SCOPE button on the picked gun's picture, and its list (its clicks go no further).
        if (_optics.Update(input))
        {
            _hovered = -1;
            return;
        }
        _hovered = -1;
        foreach (var (box, index) in _cards)
            if (CardMouse.Inside(cursor, box))
                _hovered = index;
        if (input.WasPressed(GlfwMouseButton.Left))
        {
            if (_hovered >= 0)
            {
                _picked = _hovered;
                Choose(_hovered);
            }
            foreach (var (box, section) in _tabs)
                if (CardMouse.Inside(cursor, box))
                    GoTo(section);
            if (CardMouse.Inside(cursor, _startBox))
                Accept();
        }
        if (input.WasPressed(GlfwMouseButton.Right) && _section == Section.Grenades && _hovered >= 0)
            AddGrenade(_hovered, -1);
    }

    // ------------------------------------------------------------------------------------------------ drawing

    private void Draw(Renderer renderer, int2 framebufferSize, float3 eye)
    {
        var status = !Loaded ? "LOADING THE RAID..." : "READY TO GO";
        var menu = GunLocker.DrawCase(renderer, framebufferSize, "RAID LOADOUT", status,
        [
            ("TAB", "next section"), ("CLICK/ENTER", "choose"), ("ARROWS", "pick"),
            ("RIGHT-CLICK/BACKSPACE", "one grenade fewer"), ("SPACE", "start the raid"),
        ]);
        var (aspect, px, unit, _, _, _) = menu;
        var pictures = new List<(int Section, int Index, float2 Center, float2 Size, bool Large)>();

        // Along the top, the five sections, each with what's chosen in it.
        _tabs.Clear();
        var tabHeight = (2 * BlockFont.Height + 14) * unit;
        var tabWidth = (menu.GridWidth - MenuGap * 4) / 5;
        for (var s = 0; s < 5; s++)
        {
            var section = (Section)s;
            var center = new float2(menu.GridLeft + tabWidth / 2 + s * (tabWidth + MenuGap), menu.BodyTop - tabHeight / 2);
            var size = new float2(tabWidth, tabHeight);
            _tabs.Add((new float4(center.X, center.Y, size.X, size.Y), section));
            var on = section == _section;
            GunLocker.Box(renderer, aspect, center, size + new float2((on ? 6 : 4) * px), on ? GunLocker.Amber : GunLocker.CardEdge);
            GunLocker.Box(renderer, aspect, center, size, on ? GunLocker.AmberDim * 0.55f : GunLocker.Card);
            var left = center.X - tabWidth / 2 + 5 * unit;
            GunLocker.Text(renderer, aspect, SectionNames[s], new float2(left, center.Y + tabHeight / 2 - 5 * unit), unit, on ? GunLocker.Amber : GunLocker.Dim);
            GunLocker.Text(renderer, aspect, Fit(ChosenName(section).ToUpperInvariant(), tabWidth - 10 * unit, unit),
                new float2(left, center.Y + tabHeight / 2 - (BlockFont.Height + 10) * unit), unit, GunLocker.Ink);
        }

        // At the bottom, START RAID.
        var startHeight = (BlockFont.Height * 2 + 16) * unit;
        var startCenter = new float2(menu.GridLeft + menu.GridWidth / 2, menu.BodyBottom + startHeight / 2);
        var startSize = new float2(menu.GridWidth, startHeight);
        _startBox = new float4(startCenter.X, startCenter.Y, startSize.X, startSize.Y);
        var startText = Ready && !Loaded ? "STARTING AS SOON AS IT'S LOADED" : "START RAID";
        GunLocker.Box(renderer, aspect, startCenter, startSize + new float2(4 * px), GunLocker.Amber);
        GunLocker.Box(renderer, aspect, startCenter, startSize, Ready ? GunLocker.AmberDim : GunLocker.AmberDim * 0.6f);
        var big = unit * 2f;
        GunLocker.Text(renderer, aspect, startText, new float2(startCenter.X - GunLocker.TextWidth(startText, big) / 2, startCenter.Y + BlockFont.Height * big / 2), big, GunLocker.Ink);

        // Over it, what the whole kit weighs and what that does to you: a bar from the lightest kit to the heaviest.
        var weightHeight = (BlockFont.Height * 2 + 12) * unit;
        var weightTop = menu.BodyBottom + startHeight + MenuGap + weightHeight;
        DrawWeight(renderer, menu, weightTop, weightHeight);

        // In between, the section's options.
        var options = Options;
        var rows = int.Max(2, (options.Length + Columns - 1) / Columns);
        var grid = menu with { BodyTop = menu.BodyTop - tabHeight - MenuGap * 1.5f, BodyBottom = weightTop + MenuGap * 1.5f };
        var chosen = Chosen(_section);
        _cards.Clear();
        for (var i = 0; i < options.Length; i++)
        {
            var (corner, size) = GunLocker.CardAt(grid, i, rows, Columns);
            _cards.Add((CardMouse.Box(corner, size), i));
            var count = _section == Section.Grenades ? $"X{Count(i)}" : null;
            var barred = Barred(_section, i);
            var (center, pictureSize) = GunLocker.DrawCard(renderer, grid, corner, size, options[i].Name, null, count,
                i == _picked, i == chosen || (_section == Section.Grenades && Count(i) > 0));
            if (barred)
            {
                // Greyed out, no picture: with a submachine gun as the primary, the pistol's the only secondary.
                GunLocker.Box(renderer, aspect, center, pictureSize, GunLocker.Card * 0.7f);
                foreach (var (line, row) in (ReadOnlySpan<(string, int)>)[("PISTOL ONLY", 0), ("WITH AN SMG", 1), ("PRIMARY", 2)])
                    GunLocker.Text(renderer, aspect, line, new float2(center.X - GunLocker.TextWidth(line, unit) / 2,
                        center.Y + (1.5f - row) * (BlockFont.Height + 4) * unit), unit, GunLocker.Dim);
            }
            else
                pictures.Add(((int)_section, i, center, pictureSize, false));
        }

        // On the right, the one picked: large, with what it is and what it does.
        var (stageCenter, stageSize) = GunLocker.DrawDetailStage(renderer, menu);
        var picked = options[int.Clamp(_picked, 0, options.Length - 1)];
        var pickedEntry = _section is Section.Primary or Section.Secondary ? _catalog.FirstOrDefault(e => e.Gun.Name == picked.Name) : null;
        _optics.DrawButton(renderer, menu, stageCenter, stageSize, pickedEntry?.Gun, pickedEntry?.Stats.Optic ?? "");
        pictures.Add(((int)_section, _picked, stageCenter, stageSize, true));
        var isChosen = _section == Section.Grenades ? Count(_picked) > 0 : _picked == chosen;
        var statusText = _section == Section.Grenades ? $"TAKING {Count(_picked)}" : isChosen ? "CHOSEN"
            : Barred(_section, _picked) ? "NOT WITH AN SMG PRIMARY" : "CLICK TO CHOOSE";
        var y = GunLocker.DrawDetailHeading(renderer, menu, picked.Name, picked.Kind, statusText, isChosen ? GunLocker.Green * 0.5f : GunLocker.CardEdge);
        DrawDetails(renderer, menu, picked, y);

        // The pictures last, each in its own patch of the screen.
        var time = (float)Clock.Elapsed.TotalSeconds;
        foreach (var (section, index, center, size, large) in pictures)
            DrawPicture(renderer, framebufferSize, aspect, eye, (Section)section, index, center, size, large, time);

        // Over them, the list of optics if it's open.
        _optics.DrawList(renderer, framebufferSize, menu, eye);
    }

    private const float MenuGap = GunLocker.MenuCase.Gap;

    private static string Fit(string text, float width, float unit)
    {
        while (text.Length > 1 && GunLocker.TextWidth(text, unit) > width)
            text = text[..^1];
        return text;
    }

    private void DrawDetails(Renderer renderer, GunLocker.MenuCase menu, Option picked, float y)
    {
        var (aspect, _, unit, _, _, _) = menu;
        var valueLeft = GunLocker.DetailValueLeft(menu);
        void Row(string name, string value)
        {
            if (y - BlockFont.Height * unit < menu.BodyBottom)
                return;
            GunLocker.Text(renderer, aspect, name, new float2(menu.DetailLeft, y), unit, GunLocker.Dim);
            GunLocker.Text(renderer, aspect, value, new float2(valueLeft, y), unit, GunLocker.Ink);
            y -= (BlockFont.Height + 6) * unit;
        }

        static string Percent(float left) => left >= 1f ? "None" : $"{(1f - left) * 100f:0}% off";
        switch (_section)
        {
            case Section.Armour:
            {
                var armour = RaidArmour.Of((ArmourKind)_picked, helmet: false);
                Row("COVERS", (ArmourKind)_picked switch
                {
                    ArmourKind.None => "Nothing",
                    ArmourKind.Heavy => "Chest, back, arms and legs",
                    _ => "Chest and back",
                });
                Row("BODY", Percent(armour.Body));
                Row("LIMBS", Percent(armour.Limbs));
                Row("BLAST", Percent(armour.Blast));
                Row("WEIGHT", Kg(RaidKit.ArmourWeight((ArmourKind)_picked)));
                Row("SPEED", SpeedWith());
                break;
            }
            case Section.Head:
                Row("HELMET", (_picked & 1) != 0 ? $"Head hits {Percent(RaidArmour.HelmetHead).ToLowerInvariant()}" : "None");
                Row("GOGGLES", (_picked & 2) != 0 ? "Night vision and thermal (hold T)" : "None");
                Row("WEIGHT", Kg(((_picked & 1) != 0 ? RaidKit.HelmetWeight : 0f) + ((_picked & 2) != 0 ? RaidKit.GogglesWeight : 0f)));
                Row("SPEED", SpeedWith());
                break;
            case Section.Grenades:
                Row("TAKING", $"{Count(_picked)}  ({kit.Grenades} of {RaidKit.GrenadeSlots} carried)");
                Row("KEY", _picked switch { 0 => "Hold G, let go to throw", 1 => "Hold Q, let go to throw", _ => "Hold X, let go to throw" });
                Row("DOES", _picked switch
                {
                    0 => "Blinds and deafens, no damage",
                    1 => "Blast and fragments: worse in a room",
                    _ => "A screen nobody sees through",
                });
                Row("WEIGHT", $"{Kg(RaidKit.GrenadeWeight)} each");
                Row("ADD", "Click or Enter");
                Row("REMOVE", "Right-click or Backspace");
                break;
            default:
                Row("WEIGHT", Kg(RaidKit.GunWeight(picked.Name)) + (RaidKit.IsLmg(picked.Name) ? " with its belts" : ""));
                Row("SPEED", SpeedWith());
                if (_section == Section.Primary && RaidKit.IsSmg(picked.Name))
                    Row("NOTE", "Only the pistol as a secondary");
                if (_catalog.FirstOrDefault(e => e.Gun.Name == picked.Name) is { } entry)
                    GunLocker.DrawNumbers(renderer, menu, GunLocker.Fitted(entry), y - 3 * unit);
                else
                    Row("", "Loading...");
                break;
        }
    }

    private static string Kg(float kg) => string.Format(CultureInfo.InvariantCulture, "{0:0.0} kg", kg);

    /// <summary>The kit as it would be with the option picked chosen instead: how fast you'd be, against now.</summary>
    private string SpeedWith()
    {
        var with = kit.Copy();
        switch (_section)
        {
            case Section.Armour: with.Armour = (ArmourKind)_picked; break;
            case Section.Head: (with.Helmet, with.Goggles) = ((_picked & 1) != 0, (_picked & 2) != 0); break;
            case Section.Primary: with.Primary = _primaries[_picked].Name; with.KeepRules(); break;
            case Section.Secondary when !Barred(Section.Secondary, _picked): with.Secondary = _secondaries[_picked].Name; break;
        }
        var speed = $"{with.SpeedScale * 100f:0}%";
        if (with.Overloaded)
            speed += ", no sprinting";
        return Math.Abs(with.SpeedScale - kit.SpeedScale) < 0.001f ? speed : $"{speed}  (now {kit.SpeedScale * 100f:0}%)";
    }

    /// <summary>The kit's weight: a bar from the lightest kit there can be to the heaviest, with the speed it leaves you and what it costs.</summary>
    private void DrawWeight(Renderer renderer, in GunLocker.MenuCase menu, float top, float height)
    {
        var (aspect, px, unit, _, _, _) = menu;
        const float heaviest = 45f;   // (heavy armour, the M249 and everything else)
        var weight = kit.Weight;
        var overloaded = kit.Overloaded;
        var label = string.Format(CultureInfo.InvariantCulture, "KIT {0:0.0} KG", weight);
        var speed = $"SPEED {kit.SpeedScale * 100f:0}%";
        var note = overloaded ? "OVERLOADED: NO SPRINTING, NOISY"
            : weight <= 8f ? "LIGHT: FAST AND QUIET"
            : kit.NoiseScale >= 1.3f ? "HEAVY: SLOW AND LOUD" : "";
        var warn = new float3(0.95f, 0.3f, 0.2f);
        GunLocker.Text(renderer, aspect, label, new float2(menu.GridLeft, top), unit, GunLocker.Ink);
        GunLocker.Text(renderer, aspect, speed, new float2(menu.GridLeft + menu.GridWidth - GunLocker.TextWidth(speed, unit), top), unit,
            overloaded ? warn : GunLocker.Amber);
        if (note.Length > 0)
            GunLocker.Text(renderer, aspect, note, new float2(menu.GridLeft + menu.GridWidth / 2 - GunLocker.TextWidth(note, unit) / 2, top), unit,
                overloaded ? warn : GunLocker.Dim);
        var barY = top - height + 5 * unit;
        var barWidth = menu.GridWidth;
        GunLocker.Box(renderer, aspect, new float2(menu.GridLeft + barWidth / 2, barY), new float2(barWidth, 6 * unit), GunLocker.Track);
        var f = float.Clamp(weight / heaviest, 0.02f, 1f);
        GunLocker.Box(renderer, aspect, new float2(menu.GridLeft + barWidth * f / 2, barY), new float2(barWidth * f, 6 * unit),
            overloaded ? warn : float3.Lerp(GunLocker.Green * 0.8f, GunLocker.Amber, float.Clamp(weight / 30f, 0f, 1f)));
        for (var tick = 1; tick < 8; tick++)
            GunLocker.Box(renderer, aspect, new float2(menu.GridLeft + barWidth * tick / 8f, barY), new float2(2 * px, 6 * unit), GunLocker.Panel);
    }

    private void DrawPicture(Renderer renderer, int2 framebufferSize, float aspect, float3 eye, Section section, int index, float2 center, float2 size, bool large, float time)
    {
        var yaw = large ? -0.32f + 0.55f * float.Sin(time * 0.6f) : -0.3f;
        switch (section)
        {
            case Section.Armour:
                DrawFigure(renderer, aspect, center, size, (ArmourKind)index, false, false);
                break;
            case Section.Head:
                DrawFigure(renderer, aspect, center, size, ArmourKind.None, (index & 1) != 0, (index & 2) != 0, headOnly: true);
                break;
            case Section.Grenades when _flashbangs is { } flashbangs && _throwables is { } throwables:
                GunLocker.DrawPicture(renderer, framebufferSize, aspect, eye, CanBounds, world =>
                {
                    var stood = float4x4.CreateRotationX(-float.Pi / 2f) * world;
                    switch (index)
                    {
                        case 0: flashbangs.DrawCan(renderer, stood, bodyBrightness: 3f); break;
                        case 1: throwables.DrawFrag(renderer, stood, 3f); break;
                        default: throwables.DrawSmokeCan(renderer, stood, 3f); break;
                    }
                }, center, size, yaw, 0.3f, large ? 0.72f : 0.8f);
                break;
            case Section.Primary or Section.Secondary when _locker is not null:
                var name = (section == Section.Primary ? _primaries : _secondaries)[index].Name;
                if (_catalog.FirstOrDefault(e => e.Gun.Name == name) is { Model: { } model })
                    _locker.DrawPicture(renderer, framebufferSize, aspect, eye, model, center, size, yaw, large ? 0.16f : 0.12f, large ? 0.82f : 0.86f);
                break;
        }
    }

    /// <summary>
    /// A figure, front on, in grey, with what <paramref name="armour"/> covers in amber; a helmet on its head and goggles
    /// over its eyes. <paramref name="headOnly"/>: just the head and shoulders, larger.
    /// </summary>
    private static void DrawFigure(Renderer renderer, float aspect, float2 center, float2 size, ArmourKind armour, bool helmet, bool goggles, bool headOnly = false)
    {
        // Laid out on a 10 x 20 grid (a whole figure) or 10 x 8 (head and shoulders), fitted into the box.
        var gridHeight = headOnly ? 8f : 20f;
        var cell = float.Min(size.X / 10f, size.Y / gridHeight) * 0.85f;
        var top = center.Y + gridHeight * cell / 2;
        void Part(float x, float y, float w, float h, float3 color) =>
            GunLocker.Box(renderer, aspect, new float2(center.X + x * cell, top - (y + h / 2) * cell), new float2(w * cell, h * cell), color);

        var skin = new float3(0.42f, 0.36f, 0.32f);
        var cloth = new float3(0.22f, 0.24f, 0.26f);
        var plate = GunLocker.Amber * 0.85f;
        var chest = armour != ArmourKind.None;
        var limbs = armour == ArmourKind.Heavy;

        // The head (and a helmet over the top of it, goggles across the eyes).
        Part(0, 0.2f, 2.6f, 3f, skin);
        if (helmet)
        {
            Part(0, 0f, 3.2f, 1.4f, plate);
            Part(0, 1.2f, 3.2f, 0.4f, plate * 0.8f);
        }
        if (goggles)
        {
            Part(0, 1.7f, 3.0f, 0.75f, new float3(0.1f, 0.12f, 0.1f));
            Part(-0.65f, 1.82f, 0.9f, 0.5f, GunLocker.Green);
            Part(0.65f, 1.82f, 0.9f, 0.5f, GunLocker.Green);
        }
        Part(0, 3.2f, 1.1f, 0.8f, skin);   // the neck

        // The body: arms either side, then the legs.
        var torsoHeight = headOnly ? 4f : 6.5f;
        Part(0, 4f, 4.4f, torsoHeight, cloth);
        Part(-2.9f, 4.1f, 1.2f, headOnly ? 3.9f : 6f, cloth);
        Part(2.9f, 4.1f, 1.2f, headOnly ? 3.9f : 6f, cloth);
        if (chest)
        {
            // A light carrier is a smaller plate; medium and heavy cover more of the chest, and heavy the shoulders too.
            var (w, h) = armour == ArmourKind.Light ? (3.4f, 3.6f) : (4.2f, 4.6f);
            Part(0, 4.3f, w, float.Min(h, torsoHeight - 0.4f), plate);
            if (armour != ArmourKind.Light)
                Part(0, 4.3f + float.Min(h, torsoHeight - 0.4f) - 0.5f, w, 0.35f, plate * 0.7f);   // (the plates' seam: thicker)
        }
        if (limbs)
        {
            Part(-2.9f, 4.1f, 1.3f, 1.6f, plate);   // shoulder pads
            Part(2.9f, 4.1f, 1.3f, 1.6f, plate);
            if (!headOnly)
            {
                Part(-2.9f, 6.2f, 1.2f, 2.2f, plate * 0.9f);   // upper arms
                Part(2.9f, 6.2f, 1.2f, 2.2f, plate * 0.9f);
            }
        }
        if (headOnly)
            return;
        Part(-1.1f, 10.6f, 1.8f, 8.4f, cloth);
        Part(1.1f, 10.6f, 1.8f, 8.4f, cloth);
        if (limbs)
        {
            Part(-1.1f, 10.6f, 2f, 1.8f, plate);        // the groin and thighs
            Part(1.1f, 10.6f, 2f, 1.8f, plate);
            Part(-1.1f, 13.6f, 1.9f, 1.4f, plate);      // knee pads
            Part(1.1f, 13.6f, 1.9f, 1.4f, plate);
            Part(-1.1f, 15.4f, 1.9f, 2.6f, plate * 0.85f);   // shins
            Part(1.1f, 15.4f, 1.9f, 2.6f, plate * 0.85f);
        }
    }
}
