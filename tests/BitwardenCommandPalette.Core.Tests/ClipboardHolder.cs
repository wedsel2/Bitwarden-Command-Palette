using System.Runtime.InteropServices;

namespace BitwardenCommandPalette.Core.Tests;

/// <summary>
/// Holds the Windows clipboard open from a thread of its own, as another application would,
/// until disposed or until the given time has passed. Opening and closing must happen on the
/// same thread.
/// </summary>
internal sealed class ClipboardHolder : IDisposable
{
    private readonly ManualResetEventSlim opened = new();
    private readonly ManualResetEventSlim release = new();
    private readonly Thread thread;
    private readonly TimeSpan holdFor;

    /// <summary>Opens the clipboard and holds it.</summary>
    /// <param name="holdFor">How long to hold it at most; infinite by default.</param>
    public ClipboardHolder(TimeSpan? holdFor = null)
    {
        this.holdFor = holdFor ?? Timeout.InfiniteTimeSpan;
        thread = new Thread(Hold) { IsBackground = true };
        thread.Start();
        if (!opened.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException("The test could not open the clipboard.");
        }
    }

    public void Dispose()
    {
        release.Set();
        thread.Join();
        opened.Dispose();
        release.Dispose();
    }

    private void Hold()
    {
        // Opened without a window, the clipboard is not locked against other threads of this
        // process; a message-only window owns it the way another application's window would.
        var window = NativeMethods.CreateWindowExW(0, "STATIC", null, 0, 0, 0, 0, 0, NativeMethods.MessageOnly, 0, 0, 0);
        try
        {
            // Another application may have it for a moment; keep trying like the code under test.
            while (NativeMethods.OpenClipboard(window) == 0)
            {
                Thread.Sleep(10);
            }

            opened.Set();
            _ = release.Wait(holdFor);
            _ = NativeMethods.CloseClipboard();
        }
        finally
        {
            _ = NativeMethods.DestroyWindow(window);
        }
    }

    // DllImport with runtime marshalling, so the test project needs no unsafe code.
    private static class NativeMethods
    {
        public const nint MessageOnly = -3;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern nint CreateWindowExW(
            int exStyle, string className, string? windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int DestroyWindow(nint window);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int OpenClipboard(nint owner);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int CloseClipboard();
    }
}
