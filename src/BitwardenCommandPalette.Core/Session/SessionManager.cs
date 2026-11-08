using BitwardenCommandPalette.Core.Bw;
using BitwardenCommandPalette.Core.Clipboard;

namespace BitwardenCommandPalette.Core.Session;

/// <summary>How an unlock ended, when it did not throw.</summary>
public enum UnlockOutcome
{
    /// <summary>The vault is unlocked and the index is loaded.</summary>
    Unlocked,

    /// <summary>The CLI is not logged in; the user runs <c>bw login</c> in a terminal (ADR 12).</summary>
    NotLoggedIn,

    /// <summary>The master password was wrong.</summary>
    WrongPassword,

    /// <summary>The vault was locked again (a timeout or Windows event) before loading finished.</summary>
    Interrupted,
}

/// <summary>What a copy action copies.</summary>
public enum CopyField
{
    /// <summary>The login's username, from the index.</summary>
    Username,

    /// <summary>The login's password, fetched with <c>bw get password</c>.</summary>
    Password,

    /// <summary>The login's current TOTP code, fetched with <c>bw get totp</c>.</summary>
    Totp,
}

/// <summary>How a copy action ended, when it did not throw.</summary>
public enum CopyOutcome
{
    /// <summary>The value is on the clipboard and its clear is scheduled.</summary>
    Copied,

    /// <summary>The vault is locked; show the unlock form and try again after it.</summary>
    Locked,

    /// <summary>The login, or the requested field of it, does not exist.</summary>
    NotFound,

    /// <summary>
    /// The login is marked for master password reprompt: ask for the master password and copy
    /// again with it (ADR 18). Nothing was fetched.
    /// </summary>
    RepromptRequired,

    /// <summary>The master password given for a reprompt was wrong. Nothing was copied.</summary>
    WrongPassword,
}

/// <summary>How a sync ended, when it did not throw.</summary>
public enum SyncOutcome
{
    /// <summary>The vault was synced and the index reloaded.</summary>
    Synced,

    /// <summary>The vault is locked; nothing was synced.</summary>
    Locked,
}

/// <summary>
/// Owns the session key and the vault index, decides when to lock, and runs every action that
/// needs the vault (ADR 9, ADR 10). The extension never holds the key, the index or a fetched
/// secret: it searches and copies through this class.
/// </summary>
/// <remarks>
/// <para>
/// Both timeouts are measured on the wall clock and checked again before every action, so a
/// timer delayed by sleep cannot keep the vault open; a clock that moves back more than
/// <see cref="ClockTolerance"/> locks it.
/// </para>
/// <para>
/// A lock drops the key, the index and our clipboard value at once, then runs <c>bw lock</c>.
/// An action that was waiting for <c>bw</c> when the lock came is discarded rather than copied.
/// </para>
/// </remarks>
public sealed class SessionManager : IAsyncDisposable
{
    /// <summary>The idle timeout unless the settings say otherwise.</summary>
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(10);

    /// <summary>The shortest idle timeout allowed.</summary>
    public static readonly TimeSpan MinimumIdleTimeout = TimeSpan.FromMinutes(1);

    /// <summary>The longest idle timeout allowed.</summary>
    public static readonly TimeSpan MaximumIdleTimeout = TimeSpan.FromMinutes(15);

    /// <summary>The absolute timeout unless the settings say otherwise.</summary>
    public static readonly TimeSpan DefaultAbsoluteTimeout = TimeSpan.FromMinutes(60);

    /// <summary>The shortest absolute timeout allowed.</summary>
    public static readonly TimeSpan MinimumAbsoluteTimeout = TimeSpan.FromMinutes(15);

    /// <summary>The longest absolute timeout allowed.</summary>
    public static readonly TimeSpan MaximumAbsoluteTimeout = TimeSpan.FromMinutes(240);

    /// <summary>How old the last sync may be before an unlock runs <c>bw sync</c>, by default.</summary>
    public static readonly TimeSpan DefaultSyncInterval = TimeSpan.FromMinutes(30);

    /// <summary>How far the clock may step back (a time sync correction) before it locks the vault.</summary>
    internal static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(5);

    private readonly IBwCli cli;
    private readonly IClipboardService clipboard;
    private readonly ILockEvents lockEvents;
    private readonly TimeProvider time;
    private readonly VaultIndex index = new();
    private readonly Lock gate = new();
    private readonly SemaphoreSlim unlocking = new(1, 1);

    private TimeSpan idleTimeout = DefaultIdleTimeout;
    private TimeSpan absoluteTimeout = DefaultAbsoluteTimeout;
    private TimeSpan syncInterval = DefaultSyncInterval;
    private SessionKey? key;
    private Uri? vaultServer;
    private DateTimeOffset unlockedAt;
    private DateTimeOffset lastActivity;
    private ITimer? timer;

    // Changes at every unlock and lock, so work that started under one session cannot finish under another.
    private long generation;
    private bool disposed;

    /// <summary>Creates the session manager.</summary>
    /// <param name="cli">The Bitwarden CLI.</param>
    /// <param name="clipboard">The clipboard service every copy goes through.</param>
    /// <param name="lockEvents">The Windows events that lock the vault; the caller disposes them.</param>
    public SessionManager(IBwCli cli, IClipboardService clipboard, ILockEvents lockEvents)
        : this(cli, clipboard, lockEvents, TimeProvider.System)
    {
    }

    /// <summary>Creates the session manager on the given clock.</summary>
    /// <param name="cli">The Bitwarden CLI.</param>
    /// <param name="clipboard">The clipboard service every copy goes through.</param>
    /// <param name="lockEvents">The Windows events that lock the vault; the caller disposes them.</param>
    /// <param name="time">The clock for both timeouts and the sync interval.</param>
    internal SessionManager(IBwCli cli, IClipboardService clipboard, ILockEvents lockEvents, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(cli);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(lockEvents);
        ArgumentNullException.ThrowIfNull(time);
        this.cli = cli;
        this.clipboard = clipboard;
        this.lockEvents = lockEvents;
        this.time = time;
        lockEvents.LockRequested += OnLockRequested;
    }

    /// <summary>
    /// Raised once for every lock of an unlocked vault, before <c>bw lock</c> runs, on the
    /// thread that caused it.
    /// </summary>
    public event EventHandler<LockEventArgs>? Locked;

    /// <summary>Raised after an unlock has loaded the index, on the thread that unlocked.</summary>
    public event EventHandler? Unlocked;

    /// <summary>
    /// The server the CLI was logged in to at the last unlock, as <c>bw status</c> reports it;
    /// null for the US cloud or before the first unlock. Picks the icon server (ADR 19).
    /// </summary>
    public Uri? VaultServer
    {
        get
        {
            lock (gate)
            {
                return vaultServer;
            }
        }
    }

    /// <summary>
    /// Whether the vault is unlocked, its index is loaded and neither timeout has passed. False
    /// while an unlock is still syncing or listing.
    /// </summary>
    public bool IsUnlocked
    {
        get
        {
            lock (gate)
            {
                return key is not null && index.IsLoaded && Expired() is null;
            }
        }
    }

    /// <summary>
    /// How long without a search or copy before the vault locks, from
    /// <see cref="MinimumIdleTimeout"/> to <see cref="MaximumIdleTimeout"/>. A change applies now.
    /// </summary>
    public TimeSpan IdleTimeout
    {
        get
        {
            lock (gate)
            {
                return idleTimeout;
            }
        }

        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinimumIdleTimeout);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaximumIdleTimeout);
            lock (gate)
            {
                idleTimeout = value;
                Reschedule();
            }
        }
    }

    /// <summary>
    /// How long after unlocking the vault locks regardless of activity, from
    /// <see cref="MinimumAbsoluteTimeout"/> to <see cref="MaximumAbsoluteTimeout"/>. A change
    /// applies now.
    /// </summary>
    public TimeSpan AbsoluteTimeout
    {
        get
        {
            lock (gate)
            {
                return absoluteTimeout;
            }
        }

        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinimumAbsoluteTimeout);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaximumAbsoluteTimeout);
            lock (gate)
            {
                absoluteTimeout = value;
                Reschedule();
            }
        }
    }

    /// <summary>How old the last sync may be before an unlock runs <c>bw sync</c>; zero syncs every time.</summary>
    public TimeSpan SyncInterval
    {
        get
        {
            lock (gate)
            {
                return syncInterval;
            }
        }

        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            lock (gate)
            {
                syncInterval = value;
            }
        }
    }

    /// <summary>
    /// Runs <c>bw lock</c> once at start-up, closing any session a crash left behind (ADR 9).
    /// A CLI that is missing, untrusted or not logged in has nothing to close and is ignored.
    /// It takes turns with <see cref="UnlockAsync"/>, and is skipped once a session has been
    /// unlocked: <c>bw unlock</c> already made every earlier key worthless, and a late
    /// <c>bw lock</c> would end the new session.
    /// </summary>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>A task that completes when the CLI is locked.</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await unlocking.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                if (generation != 0)
                {
                    return;
                }
            }

            await cli.LockAsync(null, cancellationToken).ConfigureAwait(false);
        }
        catch (BwCliException)
        {
            // Nothing to close; unlocking reports a CLI problem when it matters.
        }
        finally
        {
            _ = unlocking.Release();
        }
    }

    /// <summary>
    /// Unlocks the vault, starts both timeouts and loads the index. Only one unlock runs at a time.
    /// </summary>
    /// <param name="password">The master password; it goes to <c>bw unlock</c> only (ADR 7).</param>
    /// <param name="cancellationToken">Stops the unlock; a session already issued is locked again.</param>
    /// <returns>How the unlock ended.</returns>
    /// <exception cref="BwCliException">The CLI failed for another reason; the vault stays locked.</exception>
    public async Task<UnlockOutcome> UnlockAsync(MasterPassword password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        ThrowIfDisposed();
        await unlocking.WaitAsync(cancellationToken).ConfigureAwait(false);
        UnlockOutcome outcome;
        try
        {
            outcome = await UnlockOnceAsync(password, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = unlocking.Release();
        }

        if (outcome == UnlockOutcome.Unlocked)
        {
            Unlocked?.Invoke(this, EventArgs.Empty);
        }

        return outcome;
    }

    /// <summary>
    /// Runs <c>bw sync</c> and reloads the index, so changes made elsewhere show up. Counts as
    /// activity. Takes turns with unlocks and reprompts, so the session key stays the same while
    /// it runs.
    /// </summary>
    /// <param name="cancellationToken">Stops the <c>bw</c> calls.</param>
    /// <returns>How the sync ended.</returns>
    /// <exception cref="BwCliException">The CLI failed, for example because the server cannot be reached.</exception>
    public async Task<SyncOutcome> SyncAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await unlocking.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SessionKey? session;
            long expected;
            LockReason? expired;
            lock (gate)
            {
                expired = Expired();
                session = expired is null && index.IsLoaded ? key : null;
                expected = generation;
                if (session is not null)
                {
                    lastActivity = time.GetUtcNow();
                }
            }

            if (session is null)
            {
                LockInBackground(expired);
                return SyncOutcome.Locked;
            }

            IReadOnlyList<BwLogin> logins;
            try
            {
                await cli.SyncAsync(session, cancellationToken).ConfigureAwait(false);
                logins = await cli.ListLoginsAsync(session, cancellationToken).ConfigureAwait(false);
            }
            catch (BwCliException exception) when (exception.Kind is BwErrorKind.VaultLocked or BwErrorKind.NotLoggedIn)
            {
                await LockAsync(LockReason.CliLocked, expected).ConfigureAwait(false);
                return SyncOutcome.Locked;
            }

            lock (gate)
            {
                // Locked, or a timeout passed, while bw was running: the list is not loaded.
                expired = Expired();
                if (expected != generation || expired is not null)
                {
                    session = null;
                }
                else
                {
                    index.Load(logins);
                }
            }

            if (session is null)
            {
                LockInBackground(expired);
                return SyncOutcome.Locked;
            }

            return SyncOutcome.Synced;
        }
        finally
        {
            _ = unlocking.Release();
        }
    }

    /// <summary>
    /// Searches the index (see <see cref="VaultIndex.Search"/>) and counts as activity. Returns
    /// nothing while the vault is locked.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="limit">The most results to return.</param>
    /// <returns>The matching logins, best first, without secrets.</returns>
    public IReadOnlyList<BwLogin> Search(string? query, int limit = VaultIndex.DefaultLimit)
    {
        LockReason? expired;
        lock (gate)
        {
            expired = Expired();
            if (key is not null && expired is null)
            {
                lastActivity = time.GetUtcNow();
                return index.Search(query, limit);
            }
        }

        LockInBackground(expired);
        return [];
    }

    /// <summary>
    /// Copies one field of a login through <see cref="IClipboardService"/>, after checking both
    /// timeouts (ADR 9). A password or TOTP code is fetched now, never earlier (ADR 10), and never
    /// returned to the caller; <c>bw get</c> itself tells a session that is no longer valid (ADR 17).
    /// </summary>
    /// <param name="itemId">The login's ID, from a search result.</param>
    /// <param name="field">What to copy.</param>
    /// <param name="reprompt">
    /// The master password the user typed for this copy, needed for the password or TOTP code of a
    /// login marked for reprompt (ADR 18); checked with <c>bw unlock</c>, then dropped.
    /// </param>
    /// <param name="cancellationToken">Stops the <c>bw</c> calls.</param>
    /// <returns>How the copy ended.</returns>
    /// <exception cref="BwCliException">The CLI failed, for example because TOTP needs Premium.</exception>
    /// <exception cref="ClipboardException">The clipboard could not be used.</exception>
    public async Task<CopyOutcome> CopyAsync(string itemId, CopyField field, MasterPassword? reprompt = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(itemId);
        if (!Enum.IsDefined(field))
        {
            throw new ArgumentOutOfRangeException(nameof(field));
        }
        SessionKey? session;
        BwLogin? login;
        long expected;
        LockReason? expired;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            expired = Expired();
            session = expired is null ? key : null;
            login = session is null ? null : index.Find(itemId);
            expected = generation;
        }

        if (session is null)
        {
            LockInBackground(expired);
            return CopyOutcome.Locked;
        }

        if (login is null || !Has(login, field))
        {
            return CopyOutcome.NotFound;
        }

        if (login.RequiresReprompt && field != CopyField.Username)
        {
            if (reprompt is null)
            {
                return CopyOutcome.RepromptRequired;
            }

            var (failed, confirmed) = await RepromptAsync(reprompt, expected, cancellationToken).ConfigureAwait(false);
            if (failed is { } outcome)
            {
                return outcome;
            }

            session = confirmed!;
        }

        Secret? secret;
        try
        {
            // No separate bw status (ADR 17): bw get fails with VaultLocked for a key that is no
            // longer valid, and a username needs no bw call at all.
            secret = field switch
            {
                CopyField.Password => await cli.GetPasswordAsync(session, login.Id, cancellationToken).ConfigureAwait(false),
                CopyField.Totp => await cli.GetTotpAsync(session, login.Id, cancellationToken).ConfigureAwait(false),
                _ => null,
            };
        }
        catch (BwCliException exception) when (exception.Kind is BwErrorKind.VaultLocked or BwErrorKind.NotLoggedIn)
        {
            await LockAsync(LockReason.CliLocked, expected).ConfigureAwait(false);
            return CopyOutcome.Locked;
        }
        catch (BwCliException exception) when (exception.Kind == BwErrorKind.NotFound)
        {
            return CopyOutcome.NotFound;
        }

        lock (gate)
        {
            // Locked, or a timeout passed, while bw was running: the value is not copied.
            expired = Expired();
            if (expected != generation || expired is not null)
            {
                session = null;
            }
            else
            {
                lastActivity = time.GetUtcNow();
                if (secret is null)
                {
                    clipboard.CopyUsername(login.Username!);
                }
                else
                {
                    clipboard.Copy(secret);
                }
            }
        }

        if (session is null)
        {
            LockInBackground(expired);
            return CopyOutcome.Locked;
        }

        return CopyOutcome.Copied;
    }

    /// <summary>
    /// Locks: drops the key, the index and our clipboard value at once, raises <see cref="Locked"/>,
    /// then runs <c>bw lock</c>. Safe to call when already locked; never throws for a CLI failure,
    /// because the only copy of the key is already gone.
    /// </summary>
    /// <param name="reason">Why the vault is locked.</param>
    /// <returns>A task that completes when <c>bw lock</c> has run.</returns>
    public Task LockAsync(LockReason reason) => LockAsync(reason, expected: null);

    /// <summary>Locks the vault for <see cref="LockReason.Shutdown"/> and stops listening for events.</summary>
    /// <returns>A task that completes when <c>bw lock</c> has run.</returns>
    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        lockEvents.LockRequested -= OnLockRequested;
        await LockAsync(LockReason.Shutdown).ConfigureAwait(false);

        // An unlock still running sees `disposed` and locks the key it was issued; wait for it.
        await unlocking.WaitAsync().ConfigureAwait(false);
        unlocking.Dispose();
    }

    private static bool Has(BwLogin login, CopyField field) => field switch
    {
        CopyField.Username => !string.IsNullOrEmpty(login.Username),
        CopyField.Totp => login.HasTotp,
        _ => true,
    };

    private async Task<UnlockOutcome> UnlockOnceAsync(MasterPassword password, CancellationToken cancellationToken)
    {
        var status = await cli.GetStatusAsync(null, cancellationToken).ConfigureAwait(false);
        if (status.State == BwVaultState.Unauthenticated)
        {
            return UnlockOutcome.NotLoggedIn;
        }

        SessionKey issued;
        try
        {
            issued = await cli.UnlockAsync(password, cancellationToken).ConfigureAwait(false);
        }
        catch (BwCliException exception) when (exception.Kind == BwErrorKind.InvalidPassword)
        {
            return UnlockOutcome.WrongPassword;
        }
        catch (BwCliException exception) when (exception.Kind == BwErrorKind.NotLoggedIn)
        {
            return UnlockOutcome.NotLoggedIn;
        }

        long expected = 0;
        bool sync = false;
        bool wasDisposed;
        lock (gate)
        {
            wasDisposed = disposed;
            if (!wasDisposed)
            {
                // bw unlock has already made any earlier key worthless.
                _ = Drop(clearClipboard: false);
                key = issued;
                vaultServer = status.ServerUrl;
                unlockedAt = lastActivity = time.GetUtcNow();
                expected = generation;
                sync = NeedsSync(status.LastSync);
                Reschedule();
            }
        }

        if (wasDisposed)
        {
            await LockQuietlyAsync(issued).ConfigureAwait(false);
            throw new ObjectDisposedException(nameof(SessionManager));
        }

        try
        {
            if (sync)
            {
                await SyncIfReachableAsync(issued, cancellationToken).ConfigureAwait(false);
            }

            var logins = await cli.ListLoginsAsync(issued, cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                if (expected != generation)
                {
                    return UnlockOutcome.Interrupted;
                }

                index.Load(logins);
                return UnlockOutcome.Unlocked;
            }
        }
        catch
        {
            await LockAsync(LockReason.UnlockFailed, expected).ConfigureAwait(false);
            throw;
        }
    }

    // ADR 18: checks the master password with bw unlock and, when it is right, swaps in the new
    // key. The absolute timeout keeps running from the first unlock. Returns the failed outcome,
    // or the key to fetch with.
    private async Task<(CopyOutcome? Failed, SessionKey? Key)> RepromptAsync(MasterPassword password, long expected, CancellationToken cancellationToken)
    {
        await unlocking.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SessionKey issued;
            try
            {
                issued = await cli.UnlockAsync(password, cancellationToken).ConfigureAwait(false);
            }
            catch (BwCliException exception) when (exception.Kind == BwErrorKind.InvalidPassword)
            {
                return (CopyOutcome.WrongPassword, null);
            }
            catch (BwCliException exception) when (exception.Kind == BwErrorKind.NotLoggedIn)
            {
                await LockAsync(LockReason.CliLocked, expected).ConfigureAwait(false);
                return (CopyOutcome.Locked, null);
            }

            LockReason? expired;
            bool current;
            lock (gate)
            {
                expired = Expired();
                current = expected == generation && key is not null && expired is null;
                if (current)
                {
                    // bw unlock has made the old key worthless; this one replaces it.
                    key = issued;
                    lastActivity = time.GetUtcNow();
                }
            }

            if (!current)
            {
                // Locked, or a timeout passed, while bw was running: end both sessions.
                if (expired is { } reason)
                {
                    await LockAsync(reason, expected).ConfigureAwait(false);
                }

                await LockQuietlyAsync(issued).ConfigureAwait(false);
                return (CopyOutcome.Locked, null);
            }

            return (null, issued);
        }
        finally
        {
            _ = unlocking.Release();
        }
    }

    // Locks only if the session is still the one numbered `expected`, when given.
    private async Task LockAsync(LockReason reason, long? expected)
    {
        SessionKey? dropped;
        lock (gate)
        {
            if (expected is { } value && value != generation)
            {
                return;
            }

            dropped = Drop(clearClipboard: true);
        }

        if (dropped is null)
        {
            return;
        }

        try
        {
            Locked?.Invoke(this, new LockEventArgs(reason));
        }
        finally
        {
            // A throwing handler must not keep bw's session alive.
            await LockQuietlyAsync(dropped).ConfigureAwait(false);
        }
    }

    // Offline, or the server unreachable: the local vault is still usable, so the unlock goes on.
    // A sync that says the session is gone is a real failure.
    private async Task SyncIfReachableAsync(SessionKey session, CancellationToken cancellationToken)
    {
        try
        {
            await cli.SyncAsync(session, cancellationToken).ConfigureAwait(false);
        }
        catch (BwCliException exception) when (exception.Kind is not (BwErrorKind.VaultLocked or BwErrorKind.NotLoggedIn or BwErrorKind.UntrustedExecutable))
        {
            // The list below reads the local copy; the next unlock tries to sync again.
        }
    }

    private async Task LockQuietlyAsync(SessionKey session)
    {
        try
        {
            await cli.LockAsync(session, CancellationToken.None).ConfigureAwait(false);
        }
        catch (BwCliException)
        {
            // The key existed only in our memory and is gone; bw's data stays encrypted without it.
        }
    }

    private void LockInBackground(LockReason? reason)
    {
        if (reason is { } value)
        {
            _ = LockAsync(value);
        }
    }

    private void OnLockRequested(object? sender, LockEventArgs e) => LockInBackground(e.Reason);

    private void OnTimer(long expected)
    {
        LockReason? expired;
        lock (gate)
        {
            if (expected != generation || key is null)
            {
                return;
            }

            expired = Expired();
            if (expired is null)
            {
                // Activity moved the idle deadline; wait for the new one.
                Reschedule();
                return;
            }
        }

        _ = LockAsync(expired.Value, expected);
    }

    // Caller holds the gate. Null while unlocked and within both timeouts, or while locked.
    private LockReason? Expired()
    {
        if (key is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (now < unlockedAt - ClockTolerance || now < lastActivity - ClockTolerance)
        {
            return LockReason.ClockChanged;
        }

        if (now - unlockedAt >= absoluteTimeout)
        {
            return LockReason.AbsoluteTimeout;
        }

        return now - lastActivity >= idleTimeout ? LockReason.IdleTimeout : null;
    }

    // Caller holds the gate.
    private bool NeedsSync(DateTimeOffset? lastSync)
    {
        var now = time.GetUtcNow();
        return lastSync is not { } synced || synced > now || now - synced >= syncInterval;
    }

    // Caller holds the gate. Arms the timer for the nearer of the two deadlines.
    private void Reschedule()
    {
        timer?.Dispose();
        timer = null;
        if (key is null)
        {
            return;
        }

        var deadline = lastActivity + idleTimeout < unlockedAt + absoluteTimeout
            ? lastActivity + idleTimeout
            : unlockedAt + absoluteTimeout;
        var due = deadline - time.GetUtcNow();
        var expected = generation;
        timer = time.CreateTimer(_ => OnTimer(expected), null, due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    // Caller holds the gate. Forgets the session and everything loaded with it; returns its key.
    // A lock also clears our clipboard value; replacing the session at unlock does not.
    private SessionKey? Drop(bool clearClipboard)
    {
        var dropped = key;
        key = null;
        generation++;
        timer?.Dispose();
        timer = null;
        index.Clear();
        if (clearClipboard)
        {
            clipboard.ClearIfOurs();
        }

        return dropped;
    }

    private void ThrowIfDisposed()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }
    }
}
