using BitwardenCommandPalette.Core.Bw;
using BitwardenCommandPalette.Core.Clipboard;
using BitwardenCommandPalette.Core.Icons;
using BitwardenCommandPalette.Core.Session;

namespace BitwardenCommandPalette.Extension;

/// <summary>
/// Creates Core's services once for the life of the extension, applies the settings to them, and
/// shuts them down in the right order: the session locks first, while the clipboard service can
/// still clear our value.
/// </summary>
internal sealed partial class VaultServices : IDisposable
{
    private readonly SystemLockEvents lockEvents;
    private readonly ClipboardService clipboard;
    private string? appliedBwPath;
    private int disposed;

    public VaultServices(ExtensionSettings settings)
    {
        Settings = settings;
        Cli = new LocatingBwCli(() => settings.BwPath);
        clipboard = new ClipboardService();
        lockEvents = new SystemLockEvents();
        Session = new SessionManager(Cli, clipboard, lockEvents);
        Icons = new WebsiteIcons();

        // Icons are vault metadata: dropped at every lock, and fetched from the vault's own server (ADR 19).
        Session.Locked += (_, _) => Icons.Clear();
        Session.Unlocked += (_, _) => Icons.UseVaultServer(Session.VaultServer);

        appliedBwPath = settings.BwPath;
        Apply();
        settings.Changed += (_, _) => Apply();

        // Close any session a crash left behind (ADR 9), off the start-up path.
        _ = Task.Run(() => Session.StartAsync());
    }

    public ExtensionSettings Settings { get; }

    public LocatingBwCli Cli { get; }

    public SessionManager Session { get; }

    public WebsiteIcons Icons { get; }

    public TimeSpan ClearDelay => clipboard.ClearDelay;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        // Runs `bw lock` and clears our clipboard value before the process exits.
        Session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Icons.Dispose();
        lockEvents.Dispose();
        clipboard.Dispose();
    }

    // Every value is already within Core's limits (VaultSettings); a timeout change applies now.
    private void Apply()
    {
        Session.IdleTimeout = Settings.IdleTimeout;
        Session.AbsoluteTimeout = Settings.AbsoluteTimeout;
        Session.SyncInterval = Settings.SyncInterval;
        clipboard.ClearDelay = Settings.ClearDelay;
        Icons.Enabled = Settings.ShowWebsiteIcons;

        // A different bw path is searched for and its signature checked again on the next call.
        var path = Settings.BwPath;
        if (!string.Equals(path, appliedBwPath, StringComparison.OrdinalIgnoreCase))
        {
            appliedBwPath = path;
            Cli.Reset();
        }
    }
}
