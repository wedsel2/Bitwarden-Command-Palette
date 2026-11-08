using BitwardenCommandPalette.Core.Bw;
using BitwardenCommandPalette.Core.Session;
using Microsoft.Extensions.Time.Testing;

namespace BitwardenCommandPalette.Core.Tests;

/// <summary><see cref="SessionManager.SyncAsync"/>, the vault server and the unlocked event.</summary>
public sealed class SyncTests : IAsyncDisposable
{
    private const string Id = "00000000-0000-4000-8000-000000000001";
    private const string NewId = "00000000-0000-4000-8000-000000000003";

    private readonly FakeCli cli = new();
    private readonly FakeClipboardService clipboard = new();
    private readonly FakeLockEvents events = new();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly SessionManager session;
    private readonly List<LockReason> locks = [];

    public SyncTests()
    {
        cli.Logins.Add(new BwLogin(Id, "Example", "alice", ["https://example.com"], null, HasTotp: false, RequiresReprompt: false));
        session = new SessionManager(cli, clipboard, events, time);
        session.Locked += (_, e) => locks.Add(e.Reason);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await session.DisposeAsync().ConfigureAwait(true);
        events.Dispose();
    }

    [Fact]
    public async Task SyncRunsBwSyncAndReloadsTheList()
    {
        await UnlockAsync();
        cli.Calls.Clear();
        cli.Logins.Add(new BwLogin(NewId, "Added elsewhere", null, [], null, HasTotp: false, RequiresReprompt: false));

        Assert.Equal(SyncOutcome.Synced, await session.SyncAsync(Token));

        Assert.Equal(["sync", "list"], cli.Calls);
        Assert.Equal(NewId, Assert.Single(session.Search("elsewhere")).Id);
    }

    [Fact]
    public async Task SyncCountsAsActivity()
    {
        await UnlockAsync();
        time.Advance(SessionManager.DefaultIdleTimeout - TimeSpan.FromMinutes(1));

        await session.SyncAsync(Token);
        time.Advance(TimeSpan.FromMinutes(2));

        Assert.True(session.IsUnlocked);
    }

    [Fact]
    public async Task SyncWhileLockedDoesNothing()
    {
        Assert.Equal(SyncOutcome.Locked, await session.SyncAsync(Token));

        Assert.Empty(cli.Calls);
    }

    [Fact]
    public async Task SyncAfterATimeoutLocksAndDoesNotCallBw()
    {
        await UnlockAsync();
        cli.Calls.Clear();
        time.Advance(SessionManager.DefaultIdleTimeout);

        Assert.Equal(SyncOutcome.Locked, await session.SyncAsync(Token));

        Assert.DoesNotContain("sync", cli.Calls);
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public async Task ASyncThatFindsTheVaultLockedLocksTheSession()
    {
        await UnlockAsync();
        cli.FailSync = BwErrorKind.VaultLocked;

        Assert.Equal(SyncOutcome.Locked, await session.SyncAsync(Token));

        Assert.False(session.IsUnlocked);
        Assert.Equal([LockReason.CliLocked], locks);
    }

    [Fact]
    public async Task AnUnreachableServerIsReportedAndKeepsTheVaultOpen()
    {
        await UnlockAsync();
        cli.FailSync = BwErrorKind.Failed;

        var exception = await Assert.ThrowsAsync<BwCliException>(() => session.SyncAsync(Token));

        Assert.Equal(BwErrorKind.Failed, exception.Kind);
        Assert.True(session.IsUnlocked);
        Assert.Single(session.Search("example"));
    }

    [Fact]
    public async Task AListLoadedAfterALockIsDiscarded()
    {
        await UnlockAsync();
        var gate = new TaskCompletionSource();
        cli.ListGate = gate.Task;

        var sync = session.SyncAsync(Token);
        await session.LockAsync(LockReason.Command);
        gate.SetResult();

        Assert.Equal(SyncOutcome.Locked, await sync.ConfigureAwait(true));
        Assert.False(session.IsUnlocked);
        Assert.Empty(session.Search(null));
    }

    [Fact]
    public async Task UnlockKeepsTheVaultServerAndRaisesUnlocked()
    {
        cli.ServerUrl = new Uri("https://vault.bitwarden.eu");
        var unlocked = 0;
        session.Unlocked += (_, _) => unlocked++;

        await UnlockAsync();

        Assert.Equal(new Uri("https://vault.bitwarden.eu"), session.VaultServer);
        Assert.Equal(1, unlocked);
    }

    [Fact]
    public async Task AWrongPasswordDoesNotRaiseUnlocked()
    {
        var unlocked = 0;
        session.Unlocked += (_, _) => unlocked++;

        Assert.Equal(UnlockOutcome.WrongPassword, await session.UnlockAsync(new MasterPassword("wrong"), Token));

        Assert.Equal(0, unlocked);
    }

    private async Task UnlockAsync() =>
        Assert.Equal(UnlockOutcome.Unlocked, await session.UnlockAsync(new MasterPassword(cli.CorrectPassword), Token).ConfigureAwait(true));
}
