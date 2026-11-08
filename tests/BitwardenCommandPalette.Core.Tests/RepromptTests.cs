using BitwardenCommandPalette.Core.Bw;
using BitwardenCommandPalette.Core.Session;
using Microsoft.Extensions.Time.Testing;

namespace BitwardenCommandPalette.Core.Tests;

/// <summary>ADR 18: logins marked for master password reprompt.</summary>
public sealed class RepromptTests : IAsyncDisposable
{
    private const string Id = "00000000-0000-4000-8000-000000000009";
    private const string Canary = "canary-reprompt-password-5e2a";

    private readonly FakeCli cli = new() { CorrectPassword = Canary };
    private readonly FakeClipboardService clipboard = new();
    private readonly FakeLockEvents events = new();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly SessionManager session;
    private readonly List<LockReason> locks = [];

    public RepromptTests()
    {
        cli.Logins.Add(new BwLogin(Id, "Bank", "bob", ["https://bank.example"], null, HasTotp: true, RequiresReprompt: true));
        session = new SessionManager(cli, clipboard, events, time);
        session.Locked += (_, e) => locks.Add(e.Reason);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await session.DisposeAsync().ConfigureAwait(true);
        events.Dispose();
    }

    [Theory]
    [InlineData(CopyField.Password)]
    [InlineData(CopyField.Totp)]
    public async Task ASecretNeedsTheMasterPasswordAndNothingIsFetchedWithoutIt(CopyField field)
    {
        await UnlockAsync();
        cli.Calls.Clear();

        Assert.Equal(CopyOutcome.RepromptRequired, await session.CopyAsync(Id, field, cancellationToken: Token));

        Assert.Empty(cli.Calls);
        Assert.Empty(clipboard.Copied);
    }

    [Fact]
    public async Task TheUsernameNeedsNoReprompt()
    {
        await UnlockAsync();

        Assert.Equal(CopyOutcome.Copied, await session.CopyAsync(Id, CopyField.Username, cancellationToken: Token));

        Assert.Equal(["bob"], clipboard.Copied);
    }

    [Fact]
    public async Task TheRightPasswordReplacesTheKeyAndCopies()
    {
        await UnlockAsync();
        var first = cli.Issued;
        cli.Calls.Clear();

        Assert.Equal(CopyOutcome.Copied, await session.CopyAsync(Id, CopyField.Password, new MasterPassword(Canary), Token));

        // The fake bw get only accepts the newest key, so this also proves the swap.
        Assert.Equal(["unlock", $"get password {Id}"], cli.Calls);
        Assert.NotSame(first, cli.Issued);
        Assert.Equal([cli.PasswordValue], clipboard.Copied);
        Assert.True(session.IsUnlocked);
        Assert.Empty(locks);
    }

    [Fact]
    public async Task AWrongPasswordCopiesNothingAndKeepsTheSession()
    {
        await UnlockAsync();
        cli.Calls.Clear();

        Assert.Equal(CopyOutcome.WrongPassword, await session.CopyAsync(Id, CopyField.Password, new MasterPassword("wrong"), Token));

        Assert.Equal(["unlock"], cli.Calls);
        Assert.Empty(clipboard.Copied);
        Assert.True(session.IsUnlocked);
    }

    [Fact]
    public async Task EveryCopyAsksAgain()
    {
        await UnlockAsync();
        await session.CopyAsync(Id, CopyField.Password, new MasterPassword(Canary), Token);

        Assert.Equal(CopyOutcome.RepromptRequired, await session.CopyAsync(Id, CopyField.Password, cancellationToken: Token));
    }

    [Fact]
    public async Task ARepromptDoesNotExtendTheAbsoluteTimeout()
    {
        await UnlockAsync();
        for (var minutes = 5; minutes < 60; minutes += 5)
        {
            time.Advance(TimeSpan.FromMinutes(5));
            Assert.Equal(CopyOutcome.Copied, await session.CopyAsync(Id, CopyField.Password, new MasterPassword(Canary), Token));
        }

        time.Advance(TimeSpan.FromMinutes(5));

        Assert.False(session.IsUnlocked);
        Assert.Equal([LockReason.AbsoluteTimeout], locks);
    }

    [Fact]
    public async Task ALockDuringTheRepromptCopiesNothingAndEndsTheNewSession()
    {
        await UnlockAsync();
        cli.DuringUnlock = () => events.Raise(LockReason.WindowsLocked);

        Assert.Equal(CopyOutcome.Locked, await session.CopyAsync(Id, CopyField.Password, new MasterPassword(Canary), Token));

        Assert.Empty(clipboard.Copied);
        Assert.Same(cli.Issued, cli.LockedWith[^1]);
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public async Task ATimeoutDuringTheRepromptLocks()
    {
        await UnlockAsync();
        cli.DuringUnlock = () => time.AdjustTime(time.GetUtcNow() + SessionManager.DefaultIdleTimeout);

        Assert.Equal(CopyOutcome.Locked, await session.CopyAsync(Id, CopyField.Password, new MasterPassword(Canary), Token));

        Assert.Empty(clipboard.Copied);
        Assert.Equal([LockReason.IdleTimeout], locks);
        Assert.Same(cli.Issued, cli.LockedWith[^1]);
    }

    [Fact]
    public async Task ACliLoggedOutDuringTheRepromptLocks()
    {
        await UnlockAsync();
        cli.LoggedIn = false;

        Assert.Equal(CopyOutcome.Locked, await session.CopyAsync(Id, CopyField.Password, new MasterPassword(Canary), Token));

        Assert.Equal([LockReason.CliLocked], locks);
    }

    [Fact]
    public async Task TheRepromptPasswordNeverAppearsInAnOutcomeOrException()
    {
        await UnlockAsync();
        cli.FailGet = BwErrorKind.PremiumRequired;

        var error = await Assert.ThrowsAsync<BwCliException>(() => session.CopyAsync(Id, CopyField.Totp, new MasterPassword(Canary), Token));
        var wrong = await session.CopyAsync(Id, CopyField.Password, new MasterPassword("wrong-" + Canary), Token);

        Assert.DoesNotContain(Canary, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Canary, wrong.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            Environment.GetEnvironmentVariables().Values.Cast<object>(),
            value => value.ToString()!.Contains(Canary, StringComparison.Ordinal));
    }

    private async Task UnlockAsync() =>
        Assert.Equal(UnlockOutcome.Unlocked, await session.UnlockAsync(new MasterPassword(Canary), Token).ConfigureAwait(true));
}
