using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>The levels that come in a day and a night version (the prison and the flats don't: it's always day there; the firing range is out on the outback's plain, under its sky).</summary>
public enum DayNightLevel { Factory, Yard, Outback, Bunker, Arctic, Ship, Jungle, Police, Rio }

/// <summary>
/// Whether it's day or night in each level, chosen before the world is built (<see cref="StartMenu"/>, or
/// BRUTALFPS_TIME): the skies, shadow maps and light lists are all set up once at startup. Each level has its own, so a
/// portal can look out of the night into the day.
/// </summary>
public static class TimeOfDay
{
    public static readonly (DayNightLevel Level, string Name)[] Levels =
    [
        (DayNightLevel.Factory, "THE FACTORY"),
        (DayNightLevel.Yard, "CONTAINER YARD"),
        (DayNightLevel.Outback, "THE OUTBACK"),
        (DayNightLevel.Bunker, "THE BUNKER"),
        (DayNightLevel.Arctic, "THE ARCTIC"),
        (DayNightLevel.Ship, "CONTAINER SHIP"),
        (DayNightLevel.Jungle, "THE JUNGLE LAB"),
        (DayNightLevel.Police, "POLICE STATION"),
        (DayNightLevel.Rio, "RIO FAVELA"),
    ];

    // As the game has always been: the container yard at night, everything else by day.
    private static readonly HashSet<DayNightLevel> Night = [DayNightLevel.Yard];

    /// <summary>What daylit glass and skylights turn into at night: a little moonlight.</summary>
    private static readonly float3 MoonlitGlass = new(0.07f, 0.09f, 0.14f);

    public static bool IsNight(DayNightLevel level) => Night.Contains(level);

    public static void Set(DayNightLevel level, bool night)
    {
        if (night)
            Night.Add(level);
        else
            Night.Remove(level);
    }

    /// <summary>A window or skylight of <paramref name="level"/>: as tinted by day, dark at night.</summary>
    public static float3 Daylight(DayNightLevel level, float3 tint) => IsNight(level) ? tint * MoonlitGlass : tint;

    /// <summary>
    /// The shader's areas graded to moonlight (a bit for each: see Renderer.SetNightAreas). The rest see to their own night:
    /// the yard has a night sky, the Arctic a darker whiteout, and the bunker and the police station are lit from inside.
    /// </summary>
    public static int ShaderAreas =>
        (IsNight(DayNightLevel.Factory) ? 1 << 0 | 1 << 8 : 0)
        | (IsNight(DayNightLevel.Outback) ? 1 << 2 : 0)
        | (IsNight(DayNightLevel.Ship) ? 1 << 5 : 0)
        | (IsNight(DayNightLevel.Jungle) ? 1 << 6 : 0)
        | (IsNight(DayNightLevel.Rio) ? 1 << 13 : 0);

    /// <summary>
    /// Debug: BRUTALFPS_TIME="night" (or "day") sets every level, "outback=night,yard=day" just those. True if it was set;
    /// then there's no start menu. (Nor is there for a test script or screenshot run.)
    /// </summary>
    public static bool ReadEnvironment()
    {
        if (Environment.GetEnvironmentVariable("BRUTALFPS_TIME") is not { Length: > 0 } spec)
            return false;
        Apply(spec, "BRUTALFPS_TIME");
        return true;
    }

    // The start menu's last choices, next to the exe (like the log and the texture cache), in BRUTALFPS_TIME's format.
    private static string SavedPath => Path.Combine(AppContext.BaseDirectory, "timeofday.txt");

    /// <summary>Brings back the choices last started with, if there are any (the defaults otherwise).</summary>
    public static void LoadSaved()
    {
        try
        {
            if (File.Exists(SavedPath))
                Apply(File.ReadAllText(SavedPath), Path.GetFileName(SavedPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"[WARN] Couldn't read the saved day and night choices ({e.Message}).");
        }
    }

    /// <summary>Remembers the current choices for the next run.</summary>
    public static void Save()
    {
        try
        {
            File.WriteAllText(SavedPath, string.Join(',', Levels.Select(l => $"{l.Level.ToString().ToLowerInvariant()}={(IsNight(l.Level) ? "night" : "day")}")));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"[WARN] Couldn't save the day and night choices ({e.Message}).");
        }
    }

    private static void Apply(string spec, string source)
    {
        foreach (var item in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = item.Split('=');
            var time = parts[^1].ToLowerInvariant();
            if (time is not ("day" or "night"))
                Log.Write($"[WARN] {source}: '{item}' isn't day or night.");
            else if (parts.Length == 1)
                foreach (var (level, _) in Levels)
                    Set(level, time == "night");
            else if (Enum.TryParse<DayNightLevel>(parts[0], ignoreCase: true, out var named))
                Set(named, time == "night");
            else
                Log.Write($"[WARN] {source}: there's no level called '{parts[0]}'.");
        }
    }
}
