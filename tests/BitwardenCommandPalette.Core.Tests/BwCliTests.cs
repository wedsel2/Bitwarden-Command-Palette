using BitwardenCommandPalette.Core.Bw;

namespace BitwardenCommandPalette.Core.Tests;

[Collection(FakeBwGroup.Name)]
public sealed class BwCliTests : IDisposable
{
    private const string FullLogin = "00000000-0000-4000-8000-000000000005";
    private const string NoPassword = "00000000-0000-4000-8000-000000000007";
    private const string MasterPasswordCanary = "canary-master-password-51c7";

    private readonly FakeBwHost bw = new();
    private readonly BwCli cli = FakeBwHost.CreateCli();
    private readonly SessionKey session = new(FakeBwHost.RecordedSessionKey);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => bw.Dispose();

    [Theory]
    [InlineData("status-unauthenticated", BwVaultState.Unauthenticated)]
    [InlineData("status-locked", BwVaultState.Locked)]
    [InlineData("status-unlocked", BwVaultState.Unlocked)]
    public async Task StatusReadsTheState(string recording, BwVaultState expected)
    {
        bw.Reply(recording, "status");

        var status = await cli.GetStatusAsync(cancellationToken: Token);

        Assert.Equal(expected, status.State);
    }

    [Fact]
    public async Task StatusReadsTheServerAndLastSync()
    {
        bw.Reply("status-unlocked", "status");

        var status = await cli.GetStatusAsync(session, Token);

        Assert.Equal(new Uri("https://vault.bitwarden.eu"), status.ServerUrl);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), status.LastSync);
        Assert.Equal(FakeBwHost.RecordedSessionKey, Assert.Single(bw.Calls).Session);
    }

    [Fact]
    public async Task StatusWhenLoggedOutHasNoServerOrSync()
    {
        bw.Reply("status-unauthenticated", "status");

        var status = await cli.GetStatusAsync(cancellationToken: Token);

        Assert.Null(status.ServerUrl);
        Assert.Null(status.LastSync);
    }

    [Fact]
    public async Task UnlockPassesThePasswordOnlyThroughTheChildEnvironment()
    {
        bw.Reply("unlock", "unlock", "--passwordenv", "BWCP_PW", "--raw");

        var key = await cli.UnlockAsync(new MasterPassword(MasterPasswordCanary), Token);

        Assert.Equal(FakeBwHost.RecordedSessionKey, key.RevealForChildEnvironment());
        var call = Assert.Single(bw.Calls);
        Assert.Equal(["unlock", "--passwordenv", "BWCP_PW", "--raw", "--nointeraction"], call.Args);
        Assert.Equal("BWCP_PW", call.PasswordVariable);
        Assert.Equal(MasterPasswordCanary, call.Password);
        Assert.Null(call.Session);
        Assert.Null(Environment.GetEnvironmentVariable("BWCP_PW"));
    }

    [Theory]
    [InlineData("unlock-wrong-password", BwErrorKind.InvalidPassword)]
    [InlineData("unlock-missing-password-variable", BwErrorKind.Failed)]
    [InlineData("list-items-unauthenticated", BwErrorKind.NotLoggedIn)]
    public async Task UnlockFailuresAreClassified(string recording, BwErrorKind expected)
    {
        bw.Reply(recording, "unlock", "--passwordenv", "BWCP_PW", "--raw");

        var error = await Assert.ThrowsAsync<BwCliException>(() => cli.UnlockAsync(new MasterPassword(MasterPasswordCanary), Token));

        Assert.Equal(expected, error.Kind);
    }

    [Fact]
    public async Task UnlockWithEmptyOutputIsUnexpected()
    {
        bw.Respond(0, "  ", string.Empty, ["unlock", "--passwordenv", "BWCP_PW", "--raw"]);

        var error = await Assert.ThrowsAsync<BwCliException>(() => cli.UnlockAsync(new MasterPassword(MasterPasswordCanary), Token));

        Assert.Equal(BwErrorKind.UnexpectedOutput, error.Kind);
    }

    [Fact]
    public async Task ListLoginsKeepsOnlyLoginMetadata()
    {
        bw.Reply("list-items", "list", "items");

        var logins = await cli.ListLoginsAsync(session, Token);

        Assert.Equal(9, logins.Count);
        Assert.DoesNotContain(logins, login => login.Name.EndsWith("Card", StringComparison.Ordinal));
        Assert.DoesNotContain(logins, login => login.Name.EndsWith("Secure note", StringComparison.Ordinal));
        var full = Assert.Single(logins, login => login.Id == FullLogin);
        Assert.Equal("BWCP Fixture - Full login", full.Name);
        Assert.Equal("fixture-user@example.com", full.Username);
        Assert.Equal(["https://login.example.com", "https://example.org/sign-in"], full.Uris);
        Assert.NotNull(full.FolderId);
        Assert.True(full.HasTotp);
        Assert.False(full.RequiresReprompt);
        Assert.Equal(FakeBwHost.RecordedSessionKey, Assert.Single(bw.Calls).Session);
    }

    [Fact]
    public async Task ListLoginsReadsMissingFieldsAsAbsent()
    {
        bw.Reply("list-items", "list", "items");

        var logins = await cli.ListLoginsAsync(session, Token);

        var noUsername = Assert.Single(logins, login => login.Name.EndsWith("No username", StringComparison.Ordinal));
        Assert.Null(noUsername.Username);
        Assert.Empty(noUsername.Uris);
        Assert.Null(noUsername.FolderId);
        Assert.False(Assert.Single(logins, login => login.Name.EndsWith("No TOTP", StringComparison.Ordinal)).HasTotp);
        Assert.True(Assert.Single(logins, login => login.Name.EndsWith("TOTP URI", StringComparison.Ordinal)).HasTotp);
        Assert.True(Assert.Single(logins, login => login.Name.EndsWith("Reprompt", StringComparison.Ordinal)).RequiresReprompt);
        Assert.Contains(logins, login => login.Name.Contains("名前", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[{\"type\":1}]")]
    [InlineData("[{\"type\":1,\"id\":null,\"name\":\"x\"}]")]
    public async Task ListLoginsWithUnreadableOutputIsUnexpected(string stdout)
    {
        bw.Respond(0, stdout, string.Empty, ["list", "items"]);

        var error = await Assert.ThrowsAsync<BwCliException>(() => cli.ListLoginsAsync(session, Token));

        Assert.Equal(BwErrorKind.UnexpectedOutput, error.Kind);
    }

    [Fact]
    public async Task GetPasswordReturnsTheSecretAndPassesTheSession()
    {
        bw.Reply("get-password-full-login", "get", "password", FullLogin);

        var password = await cli.GetPasswordAsync(session, FullLogin, Token);

        Assert.Equal("fixture-password-full", password.RevealForClipboard());
        Assert.Equal(FakeBwHost.RecordedSessionKey, Assert.Single(bw.Calls).Session);
    }

    [Fact]
    public async Task GetPasswordKeepsWhitespace()
    {
        bw.Respond(0, " spaced password ", string.Empty, ["get", "password", FullLogin]);

        var password = await cli.GetPasswordAsync(session, FullLogin, Token);

        Assert.Equal(" spaced password ", password.RevealForClipboard());
    }

    [Theory]
    [InlineData("get-password-no-password", BwErrorKind.NotFound)]
    [InlineData("get-password-unknown-id", BwErrorKind.NotFound)]
    [InlineData("get-password-after-lock", BwErrorKind.VaultLocked)]
    [InlineData("get-password-by-duplicate-name", BwErrorKind.AmbiguousItem)]
    public async Task GetPasswordFailuresAreClassified(string recording, BwErrorKind expected)
    {
        bw.Reply(recording, "get", "password", NoPassword);

        var error = await Assert.ThrowsAsync<BwCliException>(() => cli.GetPasswordAsync(session, NoPassword, Token));

        Assert.Equal(expected, error.Kind);
    }

    [Fact]
    public async Task GetTotpWithoutPremiumSaysSo()
    {
        bw.Reply("get-totp-full-login", "get", "totp", FullLogin);

        var error = await Assert.ThrowsAsync<BwCliException>(() => cli.GetTotpAsync(session, FullLogin, Token));

        Assert.Equal(BwErrorKind.PremiumRequired, error.Kind);
    }

    [Fact]
    public async Task GetTotpReturnsTheCode()
    {
        // Not recorded: the test account has no Premium. Bitwarden documents a six-digit code.
        bw.Respond(0, "123456\n", string.Empty, ["get", "totp", FullLogin]);

        var code = await cli.GetTotpAsync(session, FullLogin, Token);

        Assert.Equal("123456", code.RevealForClipboard());
    }

    [Fact]
    public async Task GetWithEmptyOutputIsUnexpected()
    {
        bw.Respond(0, string.Empty, string.Empty, ["get", "password", FullLogin]);
        bw.Respond(0, "\n", string.Empty, ["get", "totp", FullLogin]);

        Assert.Equal(BwErrorKind.UnexpectedOutput, (await Assert.ThrowsAsync<BwCliException>(() => cli.GetPasswordAsync(session, FullLogin, Token))).Kind);
        Assert.Equal(BwErrorKind.UnexpectedOutput, (await Assert.ThrowsAsync<BwCliException>(() => cli.GetTotpAsync(session, FullLogin, Token))).Kind);
    }

    [Theory]
    [InlineData("BWCP Fixture - Full login")]
    [InlineData("")]
    public async Task GetRefusesAnythingButAnId(string itemId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => cli.GetPasswordAsync(session, itemId, Token));

        Assert.Empty(bw.Calls);
    }

    [Fact]
    public async Task LockAndSyncSucceed()
    {
        bw.Reply("lock", "lock").Reply("sync", "sync");

        await cli.LockAsync(cancellationToken: Token);
        await cli.LockAsync(session, Token);
        await cli.SyncAsync(session, Token);

        Assert.Null(bw.Calls[0].Session);
        Assert.Equal(FakeBwHost.RecordedSessionKey, bw.Calls[1].Session);
        Assert.Equal(FakeBwHost.RecordedSessionKey, bw.Calls[2].Session);
    }

    [Fact]
    public async Task NotFoundIsRecognisedAfterANotice()
    {
        bw.Respond(1, string.Empty, "Could not find data file; creating it instead.\r\nNot found.\r\n", ["get", "totp", FullLogin]);

        var error = await Assert.ThrowsAsync<BwCliException>(() => cli.GetTotpAsync(session, FullLogin, Token));

        Assert.Equal(BwErrorKind.NotFound, error.Kind);
    }

    [Theory]
    [InlineData("{00000000-0000-4000-8000-000000000005}")]
    [InlineData("00000000000040008000000000000005")]
    [InlineData(" 00000000-0000-4000-8000-000000000005")]
    [InlineData("00000000-0000-4000-8000-000000000005\n")]
    public async Task GetRefusesOtherGuidFormats(string itemId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => cli.GetTotpAsync(session, itemId, Token));

        Assert.Empty(bw.Calls);
    }

    [Fact]
    public async Task AnOddUriEntryDoesNotFailTheList()
    {
        const string items = """
            [{"type":1,"id":"00000000-0000-4000-8000-000000000005","name":"odd","folderId":null,
              "login":{"username":"u","totp":null,"uris":[null,"text",{"uri":"https://ok.example"},{"uri":""}]}}]
            """;
        bw.Respond(0, items, string.Empty, ["list", "items"]);

        var login = Assert.Single(await cli.ListLoginsAsync(session, Token));

        Assert.Equal(["https://ok.example"], login.Uris);
        Assert.False(login.HasTotp);
    }

    [Fact]
    public async Task SyncWhenLockedSaysSo()
    {
        bw.Reply("list-items-locked", "sync");

        var error = await Assert.ThrowsAsync<BwCliException>(() => cli.SyncAsync(session, Token));

        Assert.Equal(BwErrorKind.VaultLocked, error.Kind);
    }

    [Fact]
    public async Task AnUnknownErrorGetsOurOwnMessage()
    {
        bw.Respond(1, string.Empty, "Something about item canary-name-4410", ["lock"]);

        var error = await Assert.ThrowsAsync<BwCliException>(() => cli.LockAsync(cancellationToken: Token));

        Assert.Equal(BwErrorKind.Failed, error.Kind);
        Assert.DoesNotContain("canary", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecretsInheritedFromOurEnvironmentNeverReachTheChild()
    {
        bw.Reply("status-locked", "status");
        Environment.SetEnvironmentVariable("BW_SESSION", "inherited-session-canary");
        Environment.SetEnvironmentVariable("BWCP_PW", "inherited-password-canary");
        try
        {
            await cli.GetStatusAsync(cancellationToken: Token);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BW_SESSION", null);
            Environment.SetEnvironmentVariable("BWCP_PW", null);
        }

        var call = Assert.Single(bw.Calls);
        Assert.Null(call.Session);
        Assert.Null(call.BwcpPw);
    }

    [Fact]
    public async Task ASlowCallIsStopped()
    {
        bw.Respond(0, "{}", string.Empty, ["status"], delayMs: 10_000);
        var impatient = FakeBwHost.CreateCli(TimeSpan.FromMilliseconds(500));

        var error = await Assert.ThrowsAsync<BwCliException>(() => impatient.GetStatusAsync(cancellationToken: Token));

        Assert.Equal(BwErrorKind.TimedOut, error.Kind);
    }

    [Fact]
    public async Task ACancelledCallIsStopped()
    {
        bw.Respond(0, "{}", string.Empty, ["status"], delayMs: 10_000);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cli.GetStatusAsync(cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task AMissingExecutableIsReported()
    {
        var missing = new BwCli(new BwCliOptions(Path.Combine(AppContext.BaseDirectory, "missing", "bw.exe")), new TrustEverything());

        var error = await Assert.ThrowsAsync<BwCliException>(() => missing.LockAsync(cancellationToken: Token));

        Assert.Equal(BwErrorKind.UntrustedExecutable, error.Kind);
    }

    [Fact]
    public async Task AFileThatIsNotAProgramCannotStart()
    {
        var folder = Directory.CreateTempSubdirectory("notaprogram-");
        try
        {
            var text = Path.Combine(folder.FullName, "bw.exe");
            await File.WriteAllTextAsync(text, "not a program", Token);
            var cli = new BwCli(new BwCliOptions(text), new TrustEverything());

            var error = await Assert.ThrowsAsync<BwCliException>(() => cli.LockAsync(cancellationToken: Token));

            Assert.Equal(BwErrorKind.CouldNotStart, error.Kind);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ASlowSignatureCheckIsBoundByTheTimeout()
    {
        bw.Reply("status-locked", "status");
        var cli = new BwCli(new BwCliOptions(FakeBwHost.ExecutablePath) { Timeout = TimeSpan.FromMilliseconds(300) }, new SlowVerifier());

        var error = await Assert.ThrowsAsync<BwCliException>(() => cli.GetStatusAsync(cancellationToken: Token));

        Assert.Equal(BwErrorKind.TimedOut, error.Kind);
        Assert.Empty(bw.Calls);
    }

    [Fact]
    public async Task ASlowSignatureCheckCanBeCancelled()
    {
        var cli = new BwCli(new BwCliOptions(FakeBwHost.ExecutablePath), new SlowVerifier());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cli.GetStatusAsync(cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task ALinkIsCheckedAndStartedAsItsTarget()
    {
        bw.Reply("status-locked", "status");
        var folder = Directory.CreateTempSubdirectory("bwlink-");
        try
        {
            var link = Path.Combine(folder.FullName, "bw.exe");
            try
            {
                File.CreateSymbolicLink(link, FakeBwHost.ExecutablePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Assert.Skip("Creating a symbolic link needs Developer Mode or administrator rights here.");
            }

            var recorder = new RecordingVerifier();
            await new BwCli(new BwCliOptions(link), recorder).GetStatusAsync(cancellationToken: Token);

            Assert.Equal(Path.GetFullPath(FakeBwHost.ExecutablePath), Assert.Single(recorder.Checked));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AnUnsignedExecutableIsNeverStarted()
    {
        bw.Reply("status-locked", "status");
        var real = new BwCli(new BwCliOptions(FakeBwHost.ExecutablePath));

        var error = await Assert.ThrowsAsync<BwCliException>(() => real.GetStatusAsync(cancellationToken: Token));

        Assert.Equal(BwErrorKind.UntrustedExecutable, error.Kind);
        Assert.Empty(bw.Calls);
    }

    [Fact]
    public void OptionsAreValidated()
    {
        Assert.Throws<ArgumentNullException>(() => new BwCli(null!));
        Assert.Throws<ArgumentException>(() => new BwCli(new BwCliOptions(string.Empty)));
        Assert.Throws<ArgumentException>(() => new BwCli(new BwCliOptions("bw.exe")));
        Assert.Throws<ArgumentException>(() => new BwCli(new BwCliOptions(@"tools\bw.exe")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BwCli(new BwCliOptions(FakeBwHost.ExecutablePath) { Timeout = TimeSpan.Zero }));
    }

    [Fact]
    public void ALoginPrintsOnlyItsId()
    {
        var login = new BwLogin(FullLogin, "canary-name", "canary-user", [], null, HasTotp: false, RequiresReprompt: false);

        Assert.Equal($"Login {FullLogin}", login.ToString());
    }

    [Fact]
    public void EveryErrorKindHasItsOwnMessage()
    {
        var messages = Enum.GetValues<BwErrorKind>().Select(kind => new BwCliException(kind).Message).ToList();

        Assert.Equal(messages.Count, messages.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(BwErrorKind.Failed, new BwCliException().Kind);
        Assert.Equal(BwErrorKind.Failed, new BwCliException("ours").Kind);
        Assert.Equal(BwErrorKind.Failed, new BwCliException("ours", new InvalidOperationException()).Kind);
    }
}

/// <summary>Takes far longer than any test timeout, like a revocation fetch on a dead network.</summary>
internal sealed class SlowVerifier : IExecutableVerifier
{
    public bool IsTrusted(string path)
    {
        Thread.Sleep(TimeSpan.FromSeconds(5));
        return true;
    }
}

/// <summary>Trusts everything and remembers which paths it was asked about.</summary>
internal sealed class RecordingVerifier : IExecutableVerifier
{
    public List<string> Checked { get; } = [];

    public bool IsTrusted(string path)
    {
        Checked.Add(path);
        return true;
    }
}
