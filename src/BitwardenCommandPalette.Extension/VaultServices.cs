using BitwardenCommandPalette.Core.Bw;
using BitwardenCommandPalette.Core.Clipboard;
using BitwardenCommandPalette.Core.Session;

namespace BitwardenCommandPalette.Extension;

/// <summary>
/// Creates Core's services once for the life of the extension and shuts them down in the right
/// order: the session locks first, while the clipboard service can still clear our value.
/// </summary>
internal sealed partial class VaultServices : IDisposable
{
    private readonly SystemLockEvents lockEvents;
    private readonly ClipboardService clipboard;
    private int disposed;

    public VaultServices()
    {
        // The settings page sets the bw path in a later version; until then bw is searched for.
        Cli = new LocatingBwCli(() => null);
        clipboard = new ClipboardService();
        lockEvents = new SystemLockEvents();
        Session = new SessionManager(Cli, clipboard, lockEvents);

        // Close any session a crash left behind (ADR 9), off the start-up path.
        _ = Task.Run(() => Session.StartAsync());
    }

    public LocatingBwCli Cli { get; }

    public SessionManager Session { get; }

    public TimeSpan ClearDelay => clipboard.ClearDelay;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        // Runs `bw lock` and clears our clipboard value before the process exits.
        Session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        lockEvents.Dispose();
        clipboard.Dispose();
    }
}
