using System.Net;
using BitwardenCommandPalette.Core.Icons;

namespace BitwardenCommandPalette.Core.Tests;

public sealed class WebsiteIconsTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];

    private readonly FakeHandler handler = new();
    private readonly WebsiteIcons icons;

    public WebsiteIconsTests()
    {
        icons = new WebsiteIcons(handler);
        icons.UseVaultServer(new Uri("https://vault.bitwarden.eu"));
    }

    public void Dispose()
    {
        icons.Dispose();
        handler.Dispose();
    }

    [Theory]
    [InlineData(null, "https://icons.bitwarden.net/")]
    [InlineData("https://vault.bitwarden.com", "https://icons.bitwarden.net/")]
    [InlineData("https://bitwarden.com", "https://icons.bitwarden.net/")]
    [InlineData("https://vault.bitwarden.eu", "https://icons.bitwarden.eu/")]
    [InlineData("https://vault.example.org", "https://vault.example.org/icons/")]
    [InlineData("https://example.org:8443/bitwarden/", "https://example.org:8443/bitwarden/icons/")]
    [InlineData("http://vault.example.org", null)]
    [InlineData("https://user:pass@vault.example.org", null)]
    [InlineData("https://notbitwarden.com", "https://notbitwarden.com/icons/")]
    public void TheIconServerMatchesTheVaultServer(string? vault, string? expected) =>
        Assert.Equal(expected, WebsiteIcons.IconServerFor(vault is null ? null : new Uri(vault))?.AbsoluteUri);

    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("Login.Example.COM", "login.example.com")]
    [InlineData("example.com.", "example.com")]
    [InlineData("bücher.example", null)]
    [InlineData("bücher.de", "xn--bcher-kva.de")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("intranet", null)]
    [InlineData("localhost", null)]
    [InlineData("nas.local", null)]
    [InlineData("router.lan", null)]
    [InlineData("app.internal", null)]
    [InlineData("printer.home.arpa", null)]
    [InlineData("site.test", null)]
    [InlineData("192.168.1.1", null)]
    [InlineData("10.0.0", null)]
    [InlineData("::1", null)]
    [InlineData("[::1]", null)]
    [InlineData("example.com:8080", null)]
    [InlineData("user@example.com", null)]
    [InlineData("example.com/path", null)]
    [InlineData("exa mple.com", null)]
    [InlineData("example.com?token=secret", null)]
    public void OnlyPublicDnsNamesAreSent(string? host, string? expected) =>
        Assert.Equal(expected, WebsiteIcons.NormalizeHost(host));

    [Fact]
    public void AnOverlongHostIsNotSent() =>
        Assert.Null(WebsiteIcons.NormalizeHost(new string('a', 250) + ".com"));

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, true)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, true)]
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0x00, 0x01, 0x00 }, true)]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, true)]
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E }, false)]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, false)]
    [InlineData(new byte[] { 0x42, 0x4D }, false)]
    [InlineData(new byte[0], false)]
    public void OnlyImageFormatsAreKept(byte[] data, bool expected) =>
        Assert.Equal(expected, WebsiteIcons.LooksLikeImage(data));

    [Fact]
    public async Task AnIconIsFetchedByHostOnlyFromTheVaultsIconServer()
    {
        handler.Respond = _ => Image(Png);

        var data = await icons.GetAsync("example.com");

        Assert.Equal(Png, data);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://icons.bitwarden.eu/example.com/icon.png", request.AbsoluteUri);
    }

    [Fact]
    public async Task EachHostIsFetchedOnce()
    {
        handler.Respond = _ => Image(Png);

        await icons.GetAsync("example.com");
        await icons.GetAsync("EXAMPLE.com");

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AFailedFetchIsNotRetriedUntilCleared()
    {
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        Assert.Null(await icons.GetAsync("example.com"));
        Assert.Null(await icons.GetAsync("example.com"));
        Assert.Single(handler.Requests);

        icons.Clear();
        await icons.GetAsync("example.com");
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ARedirectIsNotFollowed()
    {
        handler.Respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://elsewhere.example.net/icon.png");
            return response;
        };

        Assert.Null(await icons.GetAsync("example.com"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SomethingThatIsNotAnImageIsDropped()
    {
        handler.Respond = _ => Image("<html>not an icon</html>"u8.ToArray());

        Assert.Null(await icons.GetAsync("example.com"));
    }

    [Fact]
    public async Task AnIconOverTheLimitIsDropped()
    {
        var large = new byte[WebsiteIcons.MaximumBytes + 1];
        Png.CopyTo(large, 0);
        handler.Respond = _ => Image(large);

        Assert.Null(await icons.GetAsync("example.com"));
    }

    [Fact]
    public async Task ANetworkFailureIsNoIcon()
    {
        handler.Respond = _ => throw new HttpRequestException("offline");

        Assert.Null(await icons.GetAsync("example.com"));
    }

    [Fact]
    public async Task APrivateHostIsNeverRequested()
    {
        Assert.Null(await icons.GetAsync("nas.local"));
        Assert.Null(await icons.GetAsync("192.168.1.10"));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SwitchedOffNothingIsRequestedAndTheCacheIsDropped()
    {
        handler.Respond = _ => Image(Png);
        await icons.GetAsync("example.com");

        icons.Enabled = false;
        Assert.Null(await icons.GetAsync("example.com"));
        Assert.Single(handler.Requests);

        icons.Enabled = true;
        await icons.GetAsync("example.com");
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ASelfHostedServerWithoutHttpsGetsNoIcons()
    {
        icons.UseVaultServer(new Uri("http://vault.example.org"));

        Assert.Null(icons.Server);
        Assert.Null(await icons.GetAsync("example.com"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AnotherVaultServerDropsTheCache()
    {
        handler.Respond = _ => Image(Png);
        await icons.GetAsync("example.com");

        icons.UseVaultServer(null);
        await icons.GetAsync("example.com");

        Assert.Equal("https://icons.bitwarden.net/example.com/icon.png", handler.Requests[^1].AbsoluteUri);
    }

    [Fact]
    public async Task AFetchAbandonedByClearIsNoIcon()
    {
        var gate = new TaskCompletionSource();
        handler.Gate = gate.Task;
        handler.Respond = _ => Image(Png);

        var fetch = icons.GetAsync("example.com");
        icons.Clear();
        gate.SetResult();

        Assert.Null(await fetch.ConfigureAwait(true));
    }

    [Fact]
    public async Task AfterDisposeGetThrows()
    {
        icons.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => icons.GetAsync("example.com"));
    }

    private static HttpResponseMessage Image(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public Task? Gate { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request.RequestUri!);
            }

            if (Gate is not null)
            {
                await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return Respond(request);
        }
    }
}
