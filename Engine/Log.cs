using System.Collections.Concurrent;

namespace BrutalFps.Engine;

/// <summary>
/// Writes messages to the console, to brutalfps.log next to the executable (handy when the console window is hidden behind
/// the game or not shown at all; it starts afresh each run) and to a file of this run's own in Logs\, which is kept
/// (<see cref="KeptSessions"/> of them). A thread of its own does the writing: a console can take milliseconds over a line,
/// and the game logs every shot.
/// </summary>
public static class Log
{
    /// <summary>How many runs' files stay in Logs\.</summary>
    public const int KeptSessions = 30;

    private static readonly string Stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Logs");
    private static readonly StreamWriter? File = Open(Path.Combine(AppContext.BaseDirectory, "brutalfps.log"));
    private static readonly StreamWriter? SessionFile = Open(Path.Combine(Folder, Stamp + ".log"));
    private static readonly StreamWriter? CsvFile = Open(Path.Combine(Folder, Stamp + ".frames.csv"));
    private static readonly BlockingCollection<(string Text, bool Csv)> Lines = [];
    private static readonly Thread Writer = StartWriter();

    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "brutalfps.log");

    /// <summary>This run's own log, kept after the game has gone.</summary>
    public static string SessionPath { get; } = Path.Combine(Folder, Stamp + ".log");

    /// <summary>This run's second-by-second figures (<see cref="FrameStats"/>).</summary>
    public static string CsvPath { get; } = Path.Combine(Folder, Stamp + ".frames.csv");

    /// <summary>Told each message as it's logged, on the thread logging it (<see cref="FrameStats"/>: what happened this frame).</summary>
    public static Action<string>? LineObserver;

    /// <summary>The game's going down with an exception: last words, before the log closes.</summary>
    public static Action? Crashing;

    private static StreamWriter? Open(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return new StreamWriter(path, append: false) { AutoFlush = true };
        }
        catch (IOException)
        {
            return null; // e.g. a second copy of the game is running; console logging still works
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Thread StartWriter()
    {
        var thread = new Thread(() =>
        {
            PruneOldSessions();
            foreach (var (text, csv) in Lines.GetConsumingEnumerable())
            {
                if (csv)
                {
                    CsvFile?.WriteLine(text);
                    continue;
                }

                Console.WriteLine(text);
                File?.WriteLine(text);
                SessionFile?.WriteLine(text);
            }
        })
        { IsBackground = true, Name = "Log" };
        thread.Start();
        // Whatever's still queued goes out with the process, even when it's going down with an exception.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Finish();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Write($"[CRASH] {e.ExceptionObject}");
            Crashing?.Invoke();
            Finish();
        };
        return thread;
    }

    private static void PruneOldSessions()
    {
        try
        {
            foreach (var old in new DirectoryInfo(Folder).GetFiles("*.log").OrderByDescending(f => f.Name).Skip(KeptSessions))
            {
                old.Delete();
                System.IO.File.Delete(Path.ChangeExtension(old.FullName, ".frames.csv"));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // (Old logs left where they are: no harm done.)
        }
    }

    private static void Finish()
    {
        Lines.CompleteAdding();
        Writer.Join(2000);
    }

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        LineObserver?.Invoke(message);
        if (!Lines.IsAddingCompleted)
            Lines.Add((line, false));
    }

    /// <summary>A row for this run's .frames.csv.</summary>
    public static void WriteCsv(string row)
    {
        if (!Lines.IsAddingCompleted)
            Lines.Add((row, true));
    }
}
