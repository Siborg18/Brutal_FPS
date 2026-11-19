namespace BrutalFps.Game;

/// <summary>
/// The portal index the police tech room's PC reads (see <see cref="PcTerminal"/>): every portal's address on the portal
/// network and where in the world it is. Addresses are "prt://" plus an IPv4 address from the ranges reserved for
/// documentation (192.0.2.x, 198.51.100.x, 203.0.113.x), so none of them is anyone's real address. A portal can also be
/// shown under a vanity address (<see cref="Entry.Shown"/>): the vault's gold portal is 203.0.113.32 underneath but the
/// terminal shows it as 112.112.112.112. The bunker's four portals are relayed over Tor: their address is hidden.
/// </summary>
public static class PortalIndex
{
    public const string Prefix = "prt://";

    /// <param name="Address">The portal's address, without the prefix (or a mask for a hidden one).</param>
    /// <param name="Location">Where it is, as the index lists it.</param>
    /// <param name="Vanity">What the terminal shows instead of <paramref name="Address"/>, if anything (a vanity address).</param>
    public readonly record struct Entry(string Address, string Location, string? Vanity = null)
    {
        /// <summary>The address as the terminal shows it (without the prefix).</summary>
        public string Shown => Vanity ?? Address;
    }

    private const string Hidden = "xxx.xxx.xxx.xxx";

    // Keyed by the portal's colour (its name without " portal").
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Blue"] = new("203.0.113.12", "Elbasan, Albania"),                 // the factory's west wall
        ["Magenta"] = new("203.0.113.14", "Rio de Janeiro, Brazil"),        // the dead end of an alley in the favela (it was the factory's east wall)
        ["Orange"] = new("198.51.100.40", "Antwerp, Belgium"),              // inside a container in the yard
        ["Purple"] = new("198.51.100.41", "Antwerp, Belgium"),              // the yard's container stack
        ["Yellow"] = new("192.0.2.61", "Western Australia"),                // the outback shack
        ["Green"] = new("192.0.2.62", "Western Australia"),                 // the cellar under the roadhouse shed
        ["Teal"] = new(Hidden, "Unknown - via Tor"),                        // the bunker
        ["Ice"] = new(Hidden, "Unknown - via Tor"),
        ["Red"] = new(Hidden, "Unknown - via Tor"),
        ["Lime"] = new(Hidden, "Unknown - via Tor"),
        ["White"] = new("192.0.2.90", "Antarctica"),                        // the survey hut
        ["Slate"] = new("192.0.2.91", "Antarctica"),                        // the second container by the store
        ["Pink"] = new("198.51.100.77", "Off the coast of North Korea"),    // the container ship's bridge
        ["Olive"] = new("203.0.113.57", "Colombian rainforest"),            // the jungle lab
        ["Navy"] = new("203.0.113.31", "Amsterdam, Netherlands"),           // the police HQ (this PC)
        ["Gold"] = new("203.0.113.32", "Amsterdam, Netherlands - vault", Vanity: "112.112.112.112"),   // the police HQ's vault (its own PC)
        ["Crimson"] =new("198.51.100.112", "Netherlands - prison yard"),   // the container lowered into the prison yard
        ["Cobalt"] = new("198.51.100.113", "Netherlands - block of flats"), // the undercroft
    };

    /// <summary>The colour word of a portal's name ("Blue portal" -> "Blue").</summary>
    public static string Short(string portalName) =>
        portalName.EndsWith(" portal", StringComparison.OrdinalIgnoreCase) ? portalName[..^7] : portalName;

    /// <summary>The index entry for <paramref name="portalName"/> (an unregistered one if it has none yet).</summary>
    public static Entry For(string portalName) =>
        Entries.TryGetValue(Short(portalName), out var e) ? e : new Entry("0.0.0.0", "Unregistered");

    /// <summary>True if the portal's address is hidden behind Tor.</summary>
    public static bool IsHidden(in Entry entry) => entry.Address == Hidden;
}
