using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>Things armed NPCs call out in a fight (<see cref="ArmedNpc.Say"/>).</summary>
public enum Bark
{
    Contact, LostHim, Searching, AllClear, Reloading, HeIsReloading, Flanking, CoverMe, Covering, FragOut, Grenade,
    ManDown, FallBack, Pinned, Moving, Hit,
}

/// <summary>
/// The callouts: what's said for each <see cref="Bark"/>. A line is shown as a subtitle when the player is near enough to
/// hear it, and played if there's a recording of it: Assets/Sounds/Voice/&lt;bark&gt;_&lt;n&gt;.wav, n being the line's number
/// below (0, 1, ...), for example Voice/Contact_0.wav. The callouts are how people's coordination shows: someone shouting
/// "Contact!" is also what puts those near them on the alert (<see cref="ArmedNpc.ShoutContact"/>).
/// </summary>
public static class NpcBarks
{
    /// <summary>The lines for each bark. (The block font has no apostrophe or question mark.)</summary>
    public static readonly Dictionary<Bark, string[]> Lines = new()
    {
        [Bark.Contact] = ["Contact!", "There he is!", "Enemy spotted!", "Over there!"],
        [Bark.LostHim] = ["Lost him!", "Where did he go!", "Lost visual!"],
        [Bark.Searching] = ["Spread out, find him!", "Check the corners!", "Search the area!"],
        [Bark.AllClear] = ["All clear.", "Nothing here.", "He is gone."],
        [Bark.Reloading] = ["Reloading!", "Changing mags!", "Cover me, reloading!"],
        [Bark.HeIsReloading] = ["He is reloading, push!", "He is reloading!", "Now, while he reloads!"],
        [Bark.Flanking] = ["Flanking left!", "Flanking right!"],
        [Bark.CoverMe] = ["Cover me!", "Keep him busy!"],
        [Bark.Covering] = ["Covering!", "I got you!", "Keep his head down!"],
        [Bark.FragOut] = ["Frag out!", "Grenade out!", "Fire in the hole!"],
        [Bark.Grenade] = ["Grenade!", "Get down, grenade!", "Move, grenade!"],
        [Bark.ManDown] = ["Man down!", "We lost one!", "He got him!"],
        [Bark.FallBack] = ["Fall back!", "Pull back, pull back!", "I am getting out of here!"],
        [Bark.Pinned] = ["I am pinned!", "Taking fire!", "Heavy fire!"],
        [Bark.Moving] = ["Moving!", "Moving up!", "Pushing!"],
        [Bark.Hit] = ["I am hit!", "Argh!", "Hit!"],
    };

    /// <summary>s before the same person says anything again (an urgent bark, sooner).</summary>
    public const float PersonalCooldown = 3.5f, UrgentCooldown = 1f;

    /// <summary>s before anyone on the same side says the same bark again.</summary>
    public const float SideCooldown = 4f;

    /// <summary>A grenade warning or one being thrown: said whatever else they said a moment ago.</summary>
    public static bool Urgent(Bark bark) => bark is Bark.Grenade or Bark.FragOut or Bark.ManDown;

    /// <summary>BRUTALFPS_BARKS=0: nobody calls anything out (the shouts still alert people, silently).</summary>
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("BRUTALFPS_BARKS") != "0";

    /// <summary>The line for that bark (for <see cref="Bark.Flanking"/>, <paramref name="pick"/> 0 left, 1 right).</summary>
    public static (string Line, int Index) Pick(Bark bark, int pick)
    {
        var lines = Lines[bark];
        var i = ((pick % lines.Length) + lines.Length) % lines.Length;
        return (lines[i], i);
    }

    /// <summary>The recording's name for that line (Voice/Contact_0), if there is one: see the class's summary.</summary>
    public static string Clip(Bark bark, int index) => $"Voice/{bark}_{index}";
}

/// <summary>The subtitles of what the player can hear people calling out (drawn by <see cref="Hud"/>).</summary>
public sealed class Subtitles
{
    private const int MaxShown = 3;
    private const float ShownFor = 2.6f;
    private readonly List<(string Text, float3 Colour, float Left)> _lines = [];

    public IReadOnlyList<(string Text, float3 Colour, float Left)> Lines => _lines;

    public void Add(string text, float3 colour)
    {
        if (_lines.Count > 0 && _lines[^1].Text == text)
            return;
        _lines.Add((text, colour, ShownFor));
        if (_lines.Count > MaxShown)
            _lines.RemoveAt(0);
    }

    public void Update(float dt)
    {
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            var (text, colour, left) = _lines[i];
            left -= dt;
            if (left <= 0f)
                _lines.RemoveAt(i);
            else
                _lines[i] = (text, colour, left);
        }
    }

    public void Clear() => _lines.Clear();
}
