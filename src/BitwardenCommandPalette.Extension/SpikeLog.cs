using System.Globalization;
using Microsoft.Win32;

namespace BitwardenCommandPalette.Extension;

/// <summary>
/// Phase 2 only: records when the process starts and stops and which Windows
/// session and power events reach it, to answer roadmap questions 3 and 4.
/// Writes timestamps and event names only, never anything from a vault.
/// Removed when the spike is done.
/// </summary>
internal static class SpikeLog
{
    private static readonly Lock Gate = new();
    private static readonly List<string> Entries = [];
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BitwardenCommandPalette",
        "spike.log");

    public static string[] Recent
    {
        get
        {
            lock (Gate)
            {
                return [.. Entries.TakeLast(15).Reverse()];
            }
        }
    }

    public static void Start()
    {
        Write($"process {Environment.ProcessId} started");
        SystemEvents.SessionSwitch += (_, e) => Write($"session switch: {e.Reason}");
        SystemEvents.PowerModeChanged += (_, e) => Write($"power mode: {e.Mode}");
        SystemEvents.SessionEnding += (_, e) => Write($"session ending: {e.Reason}");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Write("process exit");
    }

    public static void Write(string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}");
        lock (Gate)
        {
            Entries.Add(line);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Diagnostics only; the in-memory list still shows the entry.
            }
            catch (UnauthorizedAccessException)
            {
                // Same.
            }
        }
    }
}
