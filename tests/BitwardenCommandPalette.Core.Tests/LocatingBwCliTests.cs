using BitwardenCommandPalette.Core.Bw;
using BitwardenCommandPalette.Core.Session;

namespace BitwardenCommandPalette.Core.Tests;

public sealed class LocatingBwCliTests
{
    private const string Found = @"C:\bw\bw.exe";

    private readonly List<string> created = [];
    private int searches;
    private BwLocation location = new(BwLocationOutcome.Found, Found);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BwIsFoundOnceAndReused()
    {
        var cli = Create();

        await cli.GetStatusAsync(cancellationToken: Token);
        await cli.LockAsync(cancellationToken: Token);

        Assert.Equal(1, searches);
        Assert.Equal([Found], created);
    }

    [Theory]
    [InlineData(BwLocationOutcome.NotFound)]
    [InlineData(BwLocationOutcome.OnlyUntrustedFound)]
    [InlineData(BwLocationOutcome.ConfiguredPathMissing)]
    [InlineData(BwLocationOutcome.ConfiguredPathUntrusted)]
    public async Task NoSignedBwIsReportedAndSearchedForAgain(BwLocationOutcome outcome)
    {
        location = new BwLocation(outcome);
        var cli = Create();

        var error = await Assert.ThrowsAsync<BwCliException>(() => cli.GetStatusAsync(cancellationToken: Token));
        Assert.Equal(BwErrorKind.UntrustedExecutable, error.Kind);

        location = new BwLocation(BwLocationOutcome.Found, Found);
        await cli.GetStatusAsync(cancellationToken: Token);

        Assert.Equal(2, searches);
        Assert.Single(created);
    }

    [Fact]
    public async Task ResetSearchesAgain()
    {
        var cli = Create();
        await cli.GetStatusAsync(cancellationToken: Token);

        cli.Reset();
        await cli.GetStatusAsync(cancellationToken: Token);

        Assert.Equal(2, searches);
    }

    [Fact]
    public async Task EveryCallGoesToTheFoundClient()
    {
        var fake = new FakeCli();
        var cli = new LocatingBwCli(() => location, _ => fake);

        var key = await cli.UnlockAsync(new MasterPassword(fake.CorrectPassword), Token);
        await cli.SyncAsync(key, Token);
        await cli.ListLoginsAsync(key, Token);
        await cli.GetPasswordAsync(key, "id", Token);
        await cli.GetTotpAsync(key, "id", Token);
        await cli.LockAsync(key, Token);

        Assert.Equal(["unlock", "sync", "list", "get password id", "get totp id", "lock"], fake.Calls);
    }

    [Fact]
    public void TheConstructorsRefuseNull()
    {
        Assert.Throws<ArgumentNullException>(() => new LocatingBwCli(null!));
        Assert.Throws<ArgumentNullException>(() => new LocatingBwCli(null!, _ => new FakeCli()));
        Assert.Throws<ArgumentNullException>(() => new LocatingBwCli(() => location, null!));
    }

    [Fact]
    public async Task ThePublicConstructorSearchesTheConfiguredPath()
    {
        var cli = new LocatingBwCli(() => @"C:\does-not-exist\bw.exe");

        var error = await Assert.ThrowsAsync<BwCliException>(() => cli.GetStatusAsync(cancellationToken: Token));

        Assert.Equal(BwErrorKind.UntrustedExecutable, error.Kind);
    }

    private LocatingBwCli Create() => new(
        () =>
        {
            searches++;
            return location;
        },
        path =>
        {
            created.Add(path);
            return new FakeCli();
        });
}

public sealed class UnlockFormInputTests
{
    private const string Canary = "canary-form-password-61d3";

    [Theory]
    [InlineData("{\"password\":\"" + Canary + "\"}")]
    [InlineData("{\"other\":1,\"password\":\"" + Canary + "\"}")]
    public void ThePasswordIsRead(string inputs)
    {
        Assert.True(UnlockFormInput.TryReadPassword(inputs, out var password));
        Assert.Equal(Canary, password!.RevealForChildEnvironment());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"password\":\"\"}")]
    [InlineData("{\"password\":null}")]
    [InlineData("{\"password\":42}")]
    [InlineData("[\"password\"]")]
    [InlineData("\"password\"")]
    public void MissingOrEmptyPasswordsAreRefused(string? inputs)
    {
        Assert.False(UnlockFormInput.TryReadPassword(inputs, out var password));
        Assert.Null(password);
    }

    [Fact]
    public void MalformedInputIsRefusedWithoutAnException()
    {
        var error = Record.Exception(() => UnlockFormInput.TryReadPassword("{\"password\":\"" + Canary, out _));

        Assert.Null(error);
        Assert.False(UnlockFormInput.TryReadPassword("{\"password\":\"" + Canary, out _));
    }

    [Fact]
    public void ThePasswordUnicodeAndSpacesSurvive()
    {
        Assert.True(UnlockFormInput.TryReadPassword("{\"password\":\" p\\u00e4ss \\\"quoted\\\" \"}", out var password));

        Assert.Equal(" päss \"quoted\" ", password!.RevealForChildEnvironment());
    }
}
