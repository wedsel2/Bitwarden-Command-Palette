using BitwardenCommandPalette.Core.Bw;
using BitwardenCommandPalette.Core.Clipboard;
using BitwardenCommandPalette.Core.Session;

namespace BitwardenCommandPalette.Core.Tests;

/// <summary>An in-memory <see cref="IBwCli"/> for <see cref="SessionManager"/> tests.</summary>
internal sealed class FakeCli : IBwCli
{
    private int issuedCount;

    public string CorrectPassword { get; set; } = "correct-password";

    public string PasswordValue { get; set; } = "fetched-password";

    public string SessionKeyPrefix { get; set; } = "session-key-";

    public bool LoggedIn { get; set; } = true;

    public bool CliUnlocked { get; set; }

    public DateTimeOffset? LastSync { get; set; }

    public SessionKey? Issued { get; private set; }

    public List<BwLogin> Logins { get; } = [];

    public List<string> Calls { get; } = [];

    public List<SessionKey?> LockedWith { get; } = [];

    public BwErrorKind? FailGet { get; set; }

    public BwErrorKind? FailList { get; set; }

    public BwErrorKind? FailLock { get; set; }

    /// <summary>Runs inside bw get before it returns, to simulate what happens meanwhile.</summary>
    public Action? DuringGet { get; set; }

    /// <summary>Awaited inside bw list items, to hold an unlock half way.</summary>
    public Task? ListGate { get; set; }

    public Task<BwStatus> GetStatusAsync(SessionKey? session = null, CancellationToken cancellationToken = default)
    {
        Calls.Add("status");
        var state = !LoggedIn ? BwVaultState.Unauthenticated
            : session is not null && ReferenceEquals(session, Issued) && CliUnlocked ? BwVaultState.Unlocked
            : BwVaultState.Locked;
        return Task.FromResult(new BwStatus(state, LastSync, null));
    }

    public Task<SessionKey> UnlockAsync(MasterPassword password, CancellationToken cancellationToken = default)
    {
        Calls.Add("unlock");
        if (!LoggedIn)
        {
            throw new BwCliException(BwErrorKind.NotLoggedIn);
        }

        if (password.RevealForChildEnvironment() != CorrectPassword)
        {
            throw new BwCliException(BwErrorKind.InvalidPassword);
        }

        Issued = new SessionKey(SessionKeyPrefix + ++issuedCount);
        CliUnlocked = true;
        return Task.FromResult(Issued);
    }

    public Task LockAsync(SessionKey? session = null, CancellationToken cancellationToken = default)
    {
        Calls.Add("lock");
        LockedWith.Add(session);
        CliUnlocked = false;
        return FailLock is { } kind ? Task.FromException(new BwCliException(kind)) : Task.CompletedTask;
    }

    public Task SyncAsync(SessionKey session, CancellationToken cancellationToken = default)
    {
        Calls.Add("sync");
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<BwLogin>> ListLoginsAsync(SessionKey session, CancellationToken cancellationToken = default)
    {
        Calls.Add("list");
        if (ListGate is not null)
        {
            await ListGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return FailList is { } kind ? throw new BwCliException(kind) : [.. Logins];
    }

    public Task<Secret> GetPasswordAsync(SessionKey session, string itemId, CancellationToken cancellationToken = default) =>
        Get($"get password {itemId}", PasswordValue);

    public Task<Secret> GetTotpAsync(SessionKey session, string itemId, CancellationToken cancellationToken = default) =>
        Get($"get totp {itemId}", "123456");

    private Task<Secret> Get(string call, string value)
    {
        Calls.Add(call);
        DuringGet?.Invoke();
        return FailGet is { } kind ? Task.FromException<Secret>(new BwCliException(kind)) : Task.FromResult(new Secret(value));
    }
}

/// <summary>Records what <see cref="SessionManager"/> asks of the clipboard.</summary>
internal sealed class FakeClipboardService : IClipboardService
{
    public TimeSpan ClearDelay { get; set; } = ClipboardService.DefaultClearDelay;

    public List<string> Copied { get; } = [];

    public int Clears { get; private set; }

    public void Copy(Secret secret) => Copied.Add(secret.RevealForClipboard());

    public void CopyUsername(string username) => Copied.Add(username);

    public void ClearIfOurs() => Clears++;
}

/// <summary>Windows lock events raised by hand.</summary>
internal sealed class FakeLockEvents : ILockEvents
{
    public event EventHandler<LockEventArgs>? LockRequested;

    public bool HasSubscribers => LockRequested is not null;

    public void Raise(LockReason reason) => LockRequested?.Invoke(this, new LockEventArgs(reason));

    public void Dispose()
    {
    }
}
