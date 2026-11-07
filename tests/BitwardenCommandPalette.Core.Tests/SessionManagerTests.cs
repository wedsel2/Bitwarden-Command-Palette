using BitwardenCommandPalette.Core.Bw;
using BitwardenCommandPalette.Core.Session;
using Microsoft.Extensions.Time.Testing;

namespace BitwardenCommandPalette.Core.Tests;

public sealed class SessionManagerTests : IAsyncDisposable
{
    private const string Id = "00000000-0000-4000-8000-000000000001";
    private const string NoTotpId = "00000000-0000-4000-8000-000000000002";

    private readonly FakeCli cli = new();
    private readonly FakeClipboardService clipboard = new();
    private readonly FakeLockEvents events = new();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
    private readonly SessionManager session;
    private readonly List<LockReason> locks = [];

    public SessionManagerTests()
    {
        cli.Logins.Add(new BwLogin(Id, "Example", "alice", ["https://example.com"], null, HasTotp: true, RequiresReprompt: false));
        cli.Logins.Add(new BwLogin(NoTotpId, "Other", null, [], null, HasTotp: false, RequiresReprompt: false));
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
    public async Task StartLocksTheCliWithoutASessionAndIgnoresACliProblem()
    {
        cli.FailLock = BwErrorKind.NotLoggedIn;

        await session.StartAsync(Token);

        Assert.Equal(["lock"], cli.Calls);
        Assert.Equal([null], cli.LockedWith);
    }

    [Fact]
    public async Task UnlockLoadsTheIndexAndSyncsWhenNeverSynced()
    {
        Assert.Equal(UnlockOutcome.Unlocked, await UnlockAsync());

        Assert.True(session.IsUnlocked);
        Assert.Equal(["status", "unlock", "sync", "list"], cli.Calls);
        Assert.Equal(2, session.Search(null).Count);
        Assert.Equal(Id, Assert.Single(session.Search("example")).Id);
    }

    [Fact]
    public async Task ARecentSyncIsNotRepeated()
    {
        cli.LastSync = time.GetUtcNow() - SessionManager.DefaultSyncInterval + TimeSpan.FromMinutes(1);

        await UnlockAsync();

        Assert.DoesNotContain("sync", cli.Calls);
    }

    [Fact]
    public async Task AnOldOrFutureSyncIsRepeated()
    {
        cli.LastSync = time.GetUtcNow() - SessionManager.DefaultSyncInterval;
        await UnlockAsync();
        Assert.Contains("sync", cli.Calls);

        cli.Calls.Clear();
        cli.LastSync = time.GetUtcNow() + TimeSpan.FromMinutes(1);
        await UnlockAsync();
        Assert.Contains("sync", cli.Calls);
    }

    [Fact]
    public async Task ACliThatIsNotLoggedInIsReportedWithoutUnlocking()
    {
        cli.LoggedIn = false;

        Assert.Equal(UnlockOutcome.NotLoggedIn, await UnlockAsync());

        Assert.DoesNotContain("unlock", cli.Calls);
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public async Task AWrongPasswordLeavesTheVaultLocked()
    {
        Assert.Equal(UnlockOutcome.WrongPassword, await session.UnlockAsync(new MasterPassword("wrong"), Token));

        Assert.False(session.IsUnlocked);
        Assert.Empty(session.Search(null));
    }

    [Fact]
    public async Task AFailedLoadLocksAgainAndThrows()
    {
        cli.FailList = BwErrorKind.TimedOut;

        var error = await Assert.ThrowsAsync<BwCliException>(UnlockAsync);

        Assert.Equal(BwErrorKind.TimedOut, error.Kind);
        Assert.False(session.IsUnlocked);
        Assert.Equal([LockReason.UnlockFailed], locks);
        Assert.Same(cli.Issued, Assert.Single(cli.LockedWith));
    }

    [Fact]
    public async Task ACancelledLoadLocksAgain()
    {
        using var cancel = new CancellationTokenSource();
        var gate = new TaskCompletionSource();
        cli.ListGate = gate.Task;

        var unlock = session.UnlockAsync(new MasterPassword(cli.CorrectPassword), cancel.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unlock);
        Assert.False(session.IsUnlocked);
        Assert.Equal([LockReason.UnlockFailed], locks);
    }

    [Fact]
    public async Task ALockDuringTheLoadInterruptsTheUnlock()
    {
        var gate = new TaskCompletionSource();
        cli.ListGate = gate.Task;

        var unlock = UnlockAsync();
        events.Raise(LockReason.WindowsLocked);
        gate.SetResult();

        Assert.Equal(UnlockOutcome.Interrupted, await unlock.ConfigureAwait(true));
        Assert.False(session.IsUnlocked);
        Assert.Empty(session.Search(null));
    }

    [Fact]
    public async Task UnlockingAgainReplacesTheSessionWithoutALockEvent()
    {
        await UnlockAsync();
        var first = cli.Issued;

        await UnlockAsync();

        Assert.NotSame(first, cli.Issued);
        Assert.Empty(locks);
        Assert.Empty(cli.LockedWith);
        Assert.True(session.IsUnlocked);
    }

    [Fact]
    public async Task TheIdleTimeoutLocksAndDropsEverything()
    {
        await UnlockAsync();
        var key = cli.Issued;

        time.Advance(SessionManager.DefaultIdleTimeout - TimeSpan.FromSeconds(1));
        Assert.True(session.IsUnlocked);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(session.IsUnlocked);
        Assert.Equal([LockReason.IdleTimeout], locks);
        Assert.Same(key, Assert.Single(cli.LockedWith));
        Assert.Equal(1, clipboard.Clears);
        Assert.Empty(session.Search(null));
    }

    [Fact]
    public async Task ASearchOrCopyMovesTheIdleDeadline()
    {
        await UnlockAsync();

        time.Advance(TimeSpan.FromMinutes(9));
        _ = session.Search("ex");
        time.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(CopyOutcome.Copied, await session.CopyAsync(Id, CopyField.Username, Token));
        time.Advance(TimeSpan.FromMinutes(9));
        Assert.True(session.IsUnlocked);

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal([LockReason.IdleTimeout], locks);
    }

    [Fact]
    public async Task TheAbsoluteTimeoutLocksDespiteActivity()
    {
        await UnlockAsync();

        for (var minutes = 5; minutes < 60; minutes += 5)
        {
            time.Advance(TimeSpan.FromMinutes(5));
            _ = session.Search(null);
        }

        Assert.True(session.IsUnlocked);
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal([LockReason.AbsoluteTimeout], locks);
    }

    [Fact]
    public async Task AClockThatMovesBackLocksOnTheNextAction()
    {
        await UnlockAsync();

        time.AdjustTime(time.GetUtcNow() - TimeSpan.FromMinutes(1));

        Assert.False(session.IsUnlocked);
        Assert.Empty(session.Search(null));
        Assert.Equal([LockReason.ClockChanged], locks);
    }

    [Fact]
    public async Task ATimeoutPassedWhileTheTimerWasDelayedIsCaughtByTheNextAction()
    {
        await UnlockAsync();

        // A timer delayed by sleep has not fired yet; the action checks the clock itself.
        session.IdleTimeout = SessionManager.MaximumIdleTimeout;
        time.AdjustTime(time.GetUtcNow() + SessionManager.MaximumIdleTimeout);

        Assert.Equal(CopyOutcome.Locked, await session.CopyAsync(Id, CopyField.Password, Token));
        Assert.Empty(clipboard.Copied);
        Assert.DoesNotContain(cli.Calls, call => call.StartsWith("get", StringComparison.Ordinal));
        Assert.Equal([LockReason.IdleTimeout], locks);
    }

    [Theory]
    [InlineData(LockReason.WindowsLocked)]
    [InlineData(LockReason.Sleep)]
    [InlineData(LockReason.SignOut)]
    public async Task AWindowsEventLocksAtOnce(LockReason reason)
    {
        await UnlockAsync();

        events.Raise(reason);

        Assert.False(session.IsUnlocked);
        Assert.Equal([reason], locks);
    }

    [Fact]
    public async Task TheLockCommandLocksOnceAndALockedVaultIsLeftAlone()
    {
        await UnlockAsync();

        await session.LockAsync(LockReason.Command);
        await session.LockAsync(LockReason.Command);

        Assert.Equal([LockReason.Command], locks);
        Assert.Single(cli.LockedWith);
        Assert.Equal(2, clipboard.Clears);
    }

    [Fact]
    public async Task AFailingBwLockStillDropsTheSession()
    {
        await UnlockAsync();
        cli.FailLock = BwErrorKind.TimedOut;

        await session.LockAsync(LockReason.Command);

        Assert.False(session.IsUnlocked);
        Assert.Empty(session.Search(null));
    }

    [Fact]
    public async Task APasswordIsFetchedAtCopyTimeAndGoesOnlyToTheClipboard()
    {
        await UnlockAsync();
        cli.Calls.Clear();

        Assert.Equal(CopyOutcome.Copied, await session.CopyAsync(Id, CopyField.Password, Token));

        Assert.Equal(["status", $"get password {Id}"], cli.Calls);
        Assert.Equal([cli.PasswordValue], clipboard.Copied);
    }

    [Fact]
    public async Task AUsernameComesFromTheIndexAfterAStatusCheck()
    {
        await UnlockAsync();
        cli.Calls.Clear();

        Assert.Equal(CopyOutcome.Copied, await session.CopyAsync(Id, CopyField.Username, Token));

        Assert.Equal(["status"], cli.Calls);
        Assert.Equal(["alice"], clipboard.Copied);
    }

    [Fact]
    public async Task ATotpCodeIsFetchedAtCopyTime()
    {
        await UnlockAsync();

        Assert.Equal(CopyOutcome.Copied, await session.CopyAsync(Id, CopyField.Totp, Token));

        Assert.Contains($"get totp {Id}", cli.Calls);
        Assert.Equal(["123456"], clipboard.Copied);
    }

    [Theory]
    [InlineData("00000000-0000-4000-8000-00000000ffff", CopyField.Password)]
    [InlineData(NoTotpId, CopyField.Totp)]
    [InlineData(NoTotpId, CopyField.Username)]
    public async Task AMissingLoginOrFieldIsNotFoundWithoutCallingBw(string id, CopyField field)
    {
        await UnlockAsync();
        cli.Calls.Clear();

        Assert.Equal(CopyOutcome.NotFound, await session.CopyAsync(id, field, Token));

        Assert.Empty(cli.Calls);
        Assert.Empty(clipboard.Copied);
    }

    [Fact]
    public async Task ACopyWhileLockedCallsNothing()
    {
        Assert.Equal(CopyOutcome.Locked, await session.CopyAsync(Id, CopyField.Password, Token));

        Assert.Empty(cli.Calls);
        Assert.Empty(clipboard.Copied);
    }

    [Fact]
    public async Task ACliThatReportsTheVaultLockedLocksTheSession()
    {
        await UnlockAsync();
        cli.CliUnlocked = false;

        Assert.Equal(CopyOutcome.Locked, await session.CopyAsync(Id, CopyField.Password, Token));

        Assert.False(session.IsUnlocked);
        Assert.Equal([LockReason.CliLocked], locks);
        Assert.Empty(clipboard.Copied);
    }

    [Theory]
    [InlineData(BwErrorKind.VaultLocked, CopyOutcome.Locked)]
    [InlineData(BwErrorKind.NotLoggedIn, CopyOutcome.Locked)]
    [InlineData(BwErrorKind.NotFound, CopyOutcome.NotFound)]
    public async Task AFailedGetIsReported(BwErrorKind kind, CopyOutcome outcome)
    {
        await UnlockAsync();
        cli.FailGet = kind;

        Assert.Equal(outcome, await session.CopyAsync(Id, CopyField.Password, Token));

        Assert.Empty(clipboard.Copied);
        Assert.Equal(outcome == CopyOutcome.Locked, !session.IsUnlocked);
    }

    [Fact]
    public async Task AnyOtherCliFailureIsThrown()
    {
        await UnlockAsync();
        cli.FailGet = BwErrorKind.PremiumRequired;

        var error = await Assert.ThrowsAsync<BwCliException>(() => session.CopyAsync(Id, CopyField.Totp, Token));

        Assert.Equal(BwErrorKind.PremiumRequired, error.Kind);
        Assert.True(session.IsUnlocked);
    }

    [Fact]
    public async Task ALockWhileBwIsFetchingDiscardsTheValue()
    {
        await UnlockAsync();
        cli.DuringGet = () => events.Raise(LockReason.WindowsLocked);

        Assert.Equal(CopyOutcome.Locked, await session.CopyAsync(Id, CopyField.Password, Token));

        Assert.Empty(clipboard.Copied);
        Assert.Equal([LockReason.WindowsLocked], locks);
    }

    [Fact]
    public async Task ATimeoutPassedWhileBwIsFetchingDiscardsTheValue()
    {
        await UnlockAsync();
        cli.DuringGet = () => time.AdjustTime(time.GetUtcNow() + SessionManager.DefaultIdleTimeout);

        Assert.Equal(CopyOutcome.Locked, await session.CopyAsync(Id, CopyField.Password, Token));

        Assert.Empty(clipboard.Copied);
        Assert.Equal([LockReason.IdleTimeout], locks);
    }

    [Fact]
    public async Task AShorterTimeoutAppliesAtOnce()
    {
        await UnlockAsync();
        time.Advance(TimeSpan.FromMinutes(3));

        session.IdleTimeout = SessionManager.MinimumIdleTimeout;
        time.Advance(TimeSpan.Zero);

        Assert.Equal([LockReason.IdleTimeout], locks);
    }

    [Fact]
    public async Task AShorterAbsoluteTimeoutAppliesAtOnce()
    {
        await UnlockAsync();
        for (var minutes = 0; minutes < 20; minutes += 5)
        {
            time.Advance(TimeSpan.FromMinutes(5));
            _ = session.Search(null);
        }

        session.AbsoluteTimeout = SessionManager.MinimumAbsoluteTimeout;
        time.Advance(TimeSpan.Zero);

        Assert.Equal([LockReason.AbsoluteTimeout], locks);
    }

    [Fact]
    public void TimeoutsOutsideTheirRangesAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => session.IdleTimeout = TimeSpan.FromSeconds(59));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.IdleTimeout = TimeSpan.FromMinutes(16));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.AbsoluteTimeout = TimeSpan.FromMinutes(14));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.AbsoluteTimeout = TimeSpan.FromMinutes(241));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SyncInterval = TimeSpan.FromTicks(-1));

        Assert.Equal(SessionManager.DefaultIdleTimeout, session.IdleTimeout);
        Assert.Equal(SessionManager.DefaultAbsoluteTimeout, session.AbsoluteTimeout);
        Assert.Equal(SessionManager.DefaultSyncInterval, session.SyncInterval);
    }

    [Fact]
    public async Task ASyncIntervalOfZeroSyncsEveryUnlock()
    {
        session.SyncInterval = TimeSpan.Zero;
        cli.LastSync = time.GetUtcNow();

        await UnlockAsync();

        Assert.Contains("sync", cli.Calls);
    }

    [Fact]
    public async Task DisposeLocksForShutdownAndStopsListening()
    {
        await UnlockAsync();

        await session.DisposeAsync().ConfigureAwait(true);
        await session.DisposeAsync().ConfigureAwait(true);

        Assert.Equal([LockReason.Shutdown], locks);
        Assert.False(events.HasSubscribers);
        await Assert.ThrowsAsync<ObjectDisposedException>(UnlockAsync);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.CopyAsync(Id, CopyField.Password, Token));
    }

    [Fact]
    public async Task DisposeDuringAnUnlockLocksTheIssuedSession()
    {
        var gate = new TaskCompletionSource();
        cli.ListGate = gate.Task;
        var unlock = UnlockAsync();

        var dispose = session.DisposeAsync().AsTask();
        gate.SetResult();
        await dispose.ConfigureAwait(true);

        Assert.Equal(UnlockOutcome.Interrupted, await unlock.ConfigureAwait(true));
        Assert.Same(cli.Issued, Assert.Single(cli.LockedWith));
    }

    [Fact]
    public void TheConstructorRefusesNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SessionManager(null!, clipboard, events));
        Assert.Throws<ArgumentNullException>(() => new SessionManager(cli, null!, events));
        Assert.Throws<ArgumentNullException>(() => new SessionManager(cli, clipboard, null!));
        Assert.Throws<ArgumentNullException>(() => new SessionManager(cli, clipboard, events, null!));
    }

    [Fact]
    public async Task ArgumentsAreChecked()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => session.UnlockAsync(null!, Token));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => session.CopyAsync(string.Empty, CopyField.Password, Token));
    }

    private Task<UnlockOutcome> UnlockAsync() => session.UnlockAsync(new MasterPassword(cli.CorrectPassword), Token);
}
