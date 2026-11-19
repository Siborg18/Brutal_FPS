using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The pause menu's NPC CONFIG, in the NPC test build (Game/NpcsTestArea.cs). On the left, the test area's people (who's
/// spawned, their health and state) and buttons for all of them; on the right, the one chosen: spawned or not, the gun,
/// and the personality (aggression, plan, temperament, what they do on losing sight of you, accuracy, armour). Every
/// change puts that person back at their spot as now set up.
/// <para>Keys: Up / Down (or W / S) move, Left / Right (or A / D) change a setting (or switch sides), Tab switches sides,
/// Enter or Space picks, Esc goes back to the pause menu. Mouse: click picks or steps a setting on (right click steps
/// it back, the wheel steps it either way).</para>
/// </summary>
public sealed class NpcTestMenu(Npcs npcs)
{
    private static readonly float3 Panel = new(0.03f, 0.03f, 0.035f);
    private static readonly float3 Edge = new(0.32f, 0.36f, 0.45f);
    private static readonly float3 Button = new(0.09f, 0.09f, 0.1f);
    private static readonly float3 Accent = new(0.35f, 0.55f, 1.0f);
    private static readonly float3 Ink = new(0.80f, 0.80f, 0.80f);
    private static readonly float3 Dim = new(0.55f, 0.56f, 0.6f);
    private static readonly float3 Faint = new(0.36f, 0.37f, 0.4f);
    private static readonly float3 Good = new(0.45f, 0.85f, 0.45f);
    private static readonly float3 Bad = new(0.95f, 0.45f, 0.35f);

    private enum Kind { Slot, Action, Field }

    private readonly record struct Item(Kind Kind, int Index, float2 Center, float2 Size);

    private enum Field { Spawned, Count, Weapon, Aggression, Plan, Temperament, LostSight, Accuracy, Armour }
    private static readonly string[] FieldNames = ["SPAWNED", "COUNT", "WEAPON", "AGGRESSION", "PLAN", "TEMPERAMENT", "LOST SIGHT", "ACCURACY", "ARMOUR"];

    private enum Action { SpawnAll, DespawnAll, ResetAll, Forgive, Fights, Back }

    private static readonly Equipment[] Weapons = [Equipment.None, Equipment.Baton, .. NpcGun.All];
    private static readonly NpcStrategy[] Plans = [NpcStrategy.Auto, NpcStrategy.Push, NpcStrategy.Cover, NpcStrategy.HoldBack];

    private const float SlotWidth = 1.1f, SlotHeight = 0.125f, SlotStep = 0.135f, SlotTop = 0.62f, LeftX = -0.66f;
    private const float FieldWidth = 1.16f, FieldHeight = 0.082f, FieldStep = 0.098f, FieldTop = 0.5f, RightX = 0.62f;
    private const float ActionWidth = 0.53f, ActionHeight = 0.095f;
    private const float ValueOffset = 0.22f;
    private const float CheckboxWidth = 0.17f;   // a person's row: clicking this far in from its left edge (the "[X]") toggles them   // a setting's value is centred this far right of the row's centre

    private readonly List<Item> _items = Layout();
    private int _focus;          // index into _items
    private int _slot;           // the person shown on the right
    private float2 _lastCursor;

    public bool IsOpen { get; private set; }

    public void Open()
    {
        IsOpen = true;
        _focus = _items.FindIndex(i => i.Kind == Kind.Slot && i.Index == _slot);
        Log.Write("[NPCTEST] NPC config open");
    }

    public void Close() => IsOpen = false;

    private static List<Item> Layout()
    {
        var items = new List<Item>();
        for (var i = 0; i < 7; i++)
            items.Add(new Item(Kind.Slot, i, new float2(LeftX, SlotTop - i * SlotStep), new float2(SlotWidth, SlotHeight)));
        for (var a = 0; a < 6; a++)
        {
            var x = LeftX + (a % 2 == 0 ? -1 : 1) * (ActionWidth / 2 + 0.02f);
            items.Add(new Item(Kind.Action, a, new float2(x, -0.36f - a / 2 * 0.12f), new float2(ActionWidth, ActionHeight)));
        }
        for (var f = 0; f < FieldNames.Length; f++)
            items.Add(new Item(Kind.Field, f, new float2(RightX, FieldTop - f * FieldStep), new float2(FieldWidth, FieldHeight)));
        return items;
    }

    /// <summary>Handles the keys and the mouse; false once it's closed (back to the pause menu).</summary>
    public bool Update(Input input)
    {
        if (!IsOpen)
            return false;
        if (input.WasPressed(GlfwKey.Escape))
        {
            Close();
            return false;
        }

        if (input.WasPressedOrRepeated(GlfwKey.Up) || input.WasPressedOrRepeated(GlfwKey.W))
            MoveFocus(-1);
        if (input.WasPressedOrRepeated(GlfwKey.Down) || input.WasPressedOrRepeated(GlfwKey.S))
            MoveFocus(1);
        if (input.WasPressed(GlfwKey.Tab))
            SwitchSide();
        var left = input.WasPressedOrRepeated(GlfwKey.Left) || input.WasPressedOrRepeated(GlfwKey.A);
        var right = input.WasPressedOrRepeated(GlfwKey.Right) || input.WasPressedOrRepeated(GlfwKey.D);
        var focus = _items[_focus];
        if (left || right)
        {
            if (focus.Kind == Kind.Field)
                Change((Field)focus.Index, right ? 1 : -1);
            else if (focus.Kind == Kind.Action && (focus.Index % 2 == 0) == right)
                _focus = _items.FindIndex(i => i.Kind == Kind.Action && i.Index == (focus.Index ^ 1));
            else
                SwitchSide();
        }
        if (input.WasPressed(GlfwKey.Enter) || input.WasPressed(GlfwKey.KpEnter) || input.WasPressed(GlfwKey.Space))
        {
            if (!Pick(_items[_focus], 1))
                return false;
        }

        // The mouse: hovering focuses, clicking picks (a setting: steps on; right click steps back; the wheel either way).
        var cursor = input.CursorSquare;
        var hovered = _items.FindIndex(i => float.Abs(cursor.X - i.Center.X) <= i.Size.X / 2 && float.Abs(cursor.Y - i.Center.Y) <= i.Size.Y / 2);
        if (hovered >= 0 && cursor != _lastCursor)
            Focus(hovered);
        _lastCursor = cursor;
        if (hovered >= 0)
        {
            var item = _items[hovered];
            if (input.WasPressed(GlfwMouseButton.Left))
            {
                Focus(hovered);
                // On a person, their [X] box (the row's left end) spawns or despawns them.
                if (item.Kind == Kind.Slot && cursor.X < item.Center.X - item.Size.X / 2 + CheckboxWidth)
                {
                    var slot = npcs.TestSlots[item.Index];
                    slot.Spawned = !slot.Spawned;
                    npcs.ApplyTestSlot(slot);
                    _lastCursor = cursor;
                    return IsOpen;
                }
                // On a setting, the value's left half (its "<") steps back.
                var back = item.Kind == Kind.Field && cursor.X > item.Center.X - 0.1f && cursor.X < item.Center.X + ValueOffset;
                if (!Pick(item, back ? -1 : 1))
                    return false;
            }
            else if (input.WasPressed(GlfwMouseButton.Right) && item.Kind == Kind.Field)
                Change((Field)item.Index, -1);
            else if (input.ScrollSteps != 0 && item.Kind == Kind.Field)
                Change((Field)item.Index, input.ScrollSteps > 0 ? -1 : 1);
        }

        return IsOpen;
    }

    private void Focus(int index)
    {
        _focus = index;
        if (_items[index].Kind == Kind.Slot)
            _slot = _items[index].Index;
    }

    /// <summary>Up or down within the side the focus is on (the slots and the buttons under them are one side).</summary>
    private void MoveFocus(int step)
    {
        var onRight = _items[_focus].Kind == Kind.Field;
        var side = Enumerable.Range(0, _items.Count).Where(i => (_items[i].Kind == Kind.Field) == onRight).ToList();
        var at = side.IndexOf(_focus);
        if (!onRight && _items[_focus].Kind == Kind.Action)
        {
            // The buttons are two to a row: up and down go a row at a time (from the top row, up to the last slot).
            var action = _items[_focus].Index;
            var next = action + step * 2;
            if (next < 0)
                Focus(_items.FindIndex(i => i.Kind == Kind.Slot && i.Index == 6));
            else if (next >= 6)
                Focus(_items.FindIndex(i => i.Kind == Kind.Slot && i.Index == 0));
            else
                Focus(_items.FindIndex(i => i.Kind == Kind.Action && i.Index == next));
            return;
        }
        Focus(side[(at + step + side.Count) % side.Count]);
    }

    private void SwitchSide()
    {
        if (_items[_focus].Kind == Kind.Field)
            Focus(_items.FindIndex(i => i.Kind == Kind.Slot && i.Index == _slot));
        else
            _focus = _items.FindIndex(i => i.Kind == Kind.Field && i.Index == 0);
    }

    /// <summary>Enter, Space or a click on an item. False if it closed the menu (BACK).</summary>
    private bool Pick(Item item, int step)
    {
        switch (item.Kind)
        {
            case Kind.Slot:
                _slot = item.Index;
                _focus = _items.FindIndex(i => i.Kind == Kind.Field && i.Index == 0);
                return true;
            case Kind.Field:
                Change((Field)item.Index, step);
                return true;
        }

        var slots = npcs.TestSlots;
        switch ((Action)item.Index)
        {
            case Action.SpawnAll:
                foreach (var s in slots)
                    s.Spawned = true;
                npcs.RespawnTestArea();
                break;
            case Action.DespawnAll:
                foreach (var s in slots)
                    s.Spawned = false;
                npcs.RespawnTestArea();
                break;
            case Action.ResetAll:
                npcs.RespawnTestArea();
                break;
            case Action.Forgive:
                npcs.ForgiveTestPlayer();
                break;
            case Action.Fights:
                Factions.Truce = !Factions.Truce;
                Log.Write($"[NPCTEST] NPCs fight each other: {(Factions.Truce ? "no" : "yes")}");
                break;
            case Action.Back:
                Close();
                return false;
        }
        return true;
    }

    private static T Step<T>(T[] values, T current, int step) =>
        values[((Array.IndexOf(values, current) is var i and >= 0 ? i : 0) + step + values.Length) % values.Length];

    /// <summary>Steps a setting of the chosen person on (or back) and puts them back as now set up.</summary>
    private void Change(Field field, int step)
    {
        var slot = npcs.TestSlots[_slot];
        switch (field)
        {
            case Field.Spawned: slot.Spawned = !slot.Spawned; break;
            case Field.Count:
                var count = slot.Count + step;
                slot.Count = count < 1 ? TestNpcSlot.MaxCount : count > TestNpcSlot.MaxCount ? 1 : count;
                slot.Spawned = true;   // (choosing how many means you want them there)
                break;
            case Field.Weapon: slot.Equipment = Step(Weapons, slot.Equipment, step); break;
            case Field.Aggression: slot.Aggression = Step(Enum.GetValues<Aggression>(), slot.Aggression, step); break;
            case Field.Plan: slot.Strategy = Step(Plans, slot.Strategy, step); break;
            case Field.Temperament: slot.Temperament = Step(Enum.GetValues<Temperament>(), slot.Temperament, step); break;
            case Field.LostSight: slot.OnLostSight = Step(Enum.GetValues<LostSight>(), slot.OnLostSight, step); break;
            case Field.Accuracy:
                var tenths = (int)float.Round(slot.Accuracy * 10f) + step;
                slot.Accuracy = (tenths < 1 ? 10 : tenths > 10 ? 1 : tenths) / 10f;
                break;
            case Field.Armour: slot.Armoured = !slot.Armoured; break;
        }
        npcs.ApplyTestSlot(slot);
    }

    private static bool Unarmed(TestNpcSlot slot) => slot.Equipment is Equipment.None or Equipment.Baton;

    public static string WeaponName(Equipment e) => e switch
    {
        Equipment.None => "UNARMED",
        Equipment.Baton => "BATON",
        Equipment.Pistol => "PISTOL",
        Equipment.Rifle => "AK-47",
        Equipment.Shotgun => "SHOTGUN",
        Equipment.Smg => "UMP40",
        Equipment.Carbine => "AR-15",
        Equipment.BattleRifle => "G3",
        Equipment.Lmg => "M249",
        _ => e.ToString().ToUpperInvariant(),
    };

    private static string PlanName(TestNpcSlot slot) => slot.Strategy switch
    {
        NpcStrategy.Auto => slot.RolledStrategy != NpcStrategy.Auto && slot.Spawned ? $"AUTO: {PlanName(slot.RolledStrategy)}" : "AUTO",
        var s => PlanName(s),
    };

    private static string PlanName(NpcStrategy s) => s == NpcStrategy.HoldBack ? "HOLD BACK" : s.ToString().ToUpperInvariant();

    private string Value(TestNpcSlot slot, Field field) => field switch
    {
        Field.Spawned => slot.Spawned ? "YES" : "NO",
        Field.Count => slot.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Field.Weapon => WeaponName(slot.Equipment),
        Field.Aggression => slot.Aggression.ToString().ToUpperInvariant(),
        Field.Plan => PlanName(slot),
        Field.Temperament => slot.Temperament == Temperament.Bolter ? "BOLTER" : "FREEZER",
        Field.LostSight => slot.OnLostSight.ToString().ToUpperInvariant(),
        Field.Accuracy => slot.Accuracy.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
        Field.Armour => slot.Armoured ? "ON" : "OFF",
        _ => "",
    };

    /// <summary>Whether a setting matters for them as they're set up (the fight settings don't, unarmed).</summary>
    private static bool Matters(TestNpcSlot slot, Field field) => field switch
    {
        Field.Aggression or Field.LostSight or Field.Accuracy => !Unarmed(slot),
        Field.Plan => !Unarmed(slot) && slot.Aggression == Aggression.Aggressive,
        _ => true,
    };

    private static string ActionName(Action a) => a switch
    {
        Action.SpawnAll => "SPAWN ALL",
        Action.DespawnAll => "DESPAWN ALL",
        Action.ResetAll => "RESET ALL",
        Action.Forgive => "CALM POLICE",
        Action.Fights => Factions.Truce ? "NPC VS NPC: OFF" : "NPC VS NPC: ON",
        _ => "BACK",
    };

    private string Status(TestNpcSlot slot)
    {
        var people = npcs.TestNpcs(slot);
        if (people.Count == 0)
            return slot.Spawned ? "-" : "DESPAWNED";
        if (people.Count == 1)
            return $"HP {float.Max(people[0].Health, 0f):0}  {people[0].State.ToString().ToUpperInvariant()}  SAVED {people[0].Saves}";
        // Several: how many, the worst hurt of them, and how often they've been saved between them.
        return $"X{people.Count} LOW HP {float.Max(LowestHealth(people), 0f):0} SAVED {people.Sum(p => p.Saves)}";
    }

    private static float LowestHealth(List<Npc> people) => people.Count == 0 ? Npc.MaxHealth : people.Min(p => p.Health);

    /// <summary>Draws the panel (over the pause menu's dimmed view).</summary>
    public void Draw(Renderer renderer, int2 framebufferSize)
    {
        if (!IsOpen)
            return;
        renderer.BeginOverlay();
        var aspect = framebufferSize.Y > 0 ? (float)framebufferSize.X / framebufferSize.Y : 1f;
        var px = 2f / float.Max(framebufferSize.Y, 1);
        var pixel = float.Max(2f, float.Round(framebufferSize.Y / 300f)) * px;
        var lineHeight = BlockFont.Height * pixel;

        var panelSize = new float2(2.56f, 1.86f);
        PcTerminal.Rect(renderer, aspect, float2.Zero, panelSize + new float2(4 * px, 4 * px), Edge);
        PcTerminal.Rect(renderer, aspect, float2.Zero, panelSize, Panel);
        var titlePixel = float.Max(3f, float.Round(framebufferSize.Y / 160f)) * px;
        Centered(renderer, aspect, "NPC CONFIG", 0, 0.88f, titlePixel, Ink);

        var slots = npcs.TestSlots;
        var focused = _items[_focus];
        foreach (var item in _items)
        {
            var isFocus = item == focused;
            var x0 = item.Center.X - item.Size.X / 2 + 4 * pixel;
            switch (item.Kind)
            {
                case Kind.Slot:
                {
                    var slot = slots[item.Index];
                    var chosen = item.Index == _slot;
                    if (chosen || isFocus)
                        PcTerminal.Rect(renderer, aspect, item.Center, item.Size + new float2(4 * px, 4 * px), isFocus ? Accent : Accent * 0.5f);
                    PcTerminal.Rect(renderer, aspect, item.Center, item.Size, isFocus ? Accent * 0.45f : chosen ? Accent * 0.2f : Button);
                    var top = item.Center.Y + (2 * lineHeight + 3 * pixel) / 2;
                    PcTerminal.Text(renderer, aspect, $"{(slot.Spawned ? "[X]" : "[ ]")} {slot.Label}", new float2(x0, top), pixel, slot.Spawned ? new float3(1f) : Dim);
                    var weapon = WeaponName(slot.Equipment);
                    PcTerminal.Text(renderer, aspect, weapon, new float2(item.Center.X + item.Size.X / 2 - 4 * pixel - (weapon.Length * BlockFont.Advance - 1) * pixel, top), pixel, Dim);
                    var status = Status(slot);
                    PcTerminal.Text(renderer, aspect, status, new float2(x0 + BlockFont.Advance * pixel, top - lineHeight - 3 * pixel), pixel,
                        npcs.TestNpcs(slot) is { Count: > 0 } people ? LowestHealth(people) < Npc.MaxHealth * 0.5f ? Bad : Good : Faint);
                    break;
                }
                case Kind.Action:
                {
                    if (isFocus)
                        PcTerminal.Rect(renderer, aspect, item.Center, item.Size + new float2(4 * px, 4 * px), Accent);
                    PcTerminal.Rect(renderer, aspect, item.Center, item.Size, isFocus ? Accent * 0.45f : Button);
                    Centered(renderer, aspect, ActionName((Action)item.Index), item.Center.X, item.Center.Y + lineHeight / 2, pixel, isFocus ? new float3(1f) : Dim, item.Size.X);
                    break;
                }
                case Kind.Field:
                {
                    var slot = slots[_slot];
                    var field = (Field)item.Index;
                    var matters = Matters(slot, field);
                    if (isFocus)
                        PcTerminal.Rect(renderer, aspect, item.Center, item.Size + new float2(4 * px, 4 * px), Accent);
                    PcTerminal.Rect(renderer, aspect, item.Center, item.Size, isFocus ? Accent * 0.45f : Button);
                    var top = item.Center.Y + lineHeight / 2;
                    PcTerminal.Text(renderer, aspect, FieldNames[item.Index], new float2(x0, top), pixel, matters ? Ink : Faint);
                    Centered(renderer, aspect, $"<  {Value(slot, field)}  >", item.Center.X + ValueOffset, top, pixel, isFocus ? new float3(1f) : matters ? Ink : Faint, 0.66f);
                    break;
                }
            }
        }

        // Over the settings: who they are; under them: how they're doing, and the keys.
        var shown = slots[_slot];
        var faction = shown.Faction.ToString().ToUpperInvariant();
        var heading = shown.Label.StartsWith(faction, StringComparison.Ordinal) ? shown.Label : $"{shown.Label} ({faction})";
        Centered(renderer, aspect, heading, RightX, FieldTop + FieldStep * 1.1f + lineHeight / 2, pixel * 1.3f, Accent, FieldWidth);
        var below = FieldTop - (FieldNames.Length - 0.2f) * FieldStep;
        Centered(renderer, aspect, Status(shown), RightX, below, pixel, Ink, FieldWidth);
        var note = Unarmed(shown) ? "UNARMED: RUNS OR COWERS"
            : shown.Aggression != Aggression.Aggressive ? "PLAN: ONLY WHEN AGGRESSIVE" : "";
        if (note.Length > 0)
            Centered(renderer, aspect, note, RightX, below - lineHeight * 1.8f, pixel, Faint, FieldWidth);
        string[] hints = ["UP/DOWN: MOVE   TAB: SWITCH SIDE", "LEFT/RIGHT OR CLICK: CHANGE", "ESC: BACK TO THE PAUSE MENU"];
        for (var i = 0; i < hints.Length; i++)
            Centered(renderer, aspect, hints[i], RightX, -0.57f - i * lineHeight * 1.8f, pixel, Dim, FieldWidth);
        Centered(renderer, aspect, "NOBODY DIES: A KILLING HIT REFILLS HEALTH", 0, -0.84f, pixel, Faint, 2.4f);
    }

    /// <summary>Text centred on <paramref name="x"/>, made smaller if it's wider than <paramref name="fit"/>.</summary>
    private static void Centered(Renderer renderer, float aspect, string text, float x, float top, float pixel, float3 color, float fit = float.PositiveInfinity)
    {
        var width = (text.Length * BlockFont.Advance - 1) * pixel;
        if (width > fit * 0.94f)
        {
            pixel *= fit * 0.94f / width;
            width = fit * 0.94f;
        }
        PcTerminal.Text(renderer, aspect, text, new float2(x - width / 2, top), pixel, color);
    }
}
