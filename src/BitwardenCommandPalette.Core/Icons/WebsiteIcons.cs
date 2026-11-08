using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;

namespace BitwardenCommandPalette.Core.Icons;

/// <summary>
/// Fetches website icons from the vault's Bitwarden icon server and keeps them in memory only
/// (ADR 19). Each host is fetched at most once until <see cref="Clear"/>; a failed fetch is
/// remembered as no icon.
/// </summary>
public sealed class WebsiteIcons : IDisposable
{
    /// <summary>The largest icon kept.</summary>
    public const int MaximumBytes = 64 * 1024;

    /// <summary>How long one icon request may take.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private const int MaximumConcurrentRequests = 4;
    private const int MaximumHostLength = 253;

    // Domains that only make sense on a private network, or are reserved (RFC 2606, 6761, 8375).
    private static readonly string[] PrivateDomains = ["localhost", "local", "internal", "lan", "home.arpa", "test", "example", "invalid"];

    private static readonly Uri UsCloudIcons = new("https://icons.bitwarden.net/");
    private static readonly Uri EuCloudIcons = new("https://icons.bitwarden.eu/");

    private readonly HttpClient http;
    private readonly SemaphoreSlim requests = new(MaximumConcurrentRequests, MaximumConcurrentRequests);
    private readonly Lock gate = new();
    private ConcurrentDictionary<string, Lazy<Task<byte[]?>>> cache = new(StringComparer.Ordinal);
    private CancellationTokenSource generation = new();
    private Uri? server;
    private bool enabled = true;
    private bool disposed;

    /// <summary>Creates the icon fetcher with its own HTTP handler: no redirects, no cookies.</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The HttpClient owns the handler and disposes it.")]
    public WebsiteIcons()
        : this(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = RequestTimeout,
        })
    {
    }

    /// <summary>Creates the icon fetcher on the given handler, which it disposes.</summary>
    /// <param name="handler">Sends the requests; tests pass a fake.</param>
    internal WebsiteIcons(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = RequestTimeout,
            MaxResponseContentBufferSize = MaximumBytes,
        };
    }

    /// <summary>Whether icons are fetched at all. Switching it off drops every cached icon.</summary>
    public bool Enabled
    {
        get
        {
            lock (gate)
            {
                return enabled;
            }
        }

        set
        {
            lock (gate)
            {
                enabled = value;
            }

            if (!value)
            {
                Clear();
            }
        }
    }

    /// <summary>The icon server set by <see cref="UseVaultServer"/>, or null when there is none to use.</summary>
    public Uri? Server
    {
        get
        {
            lock (gate)
            {
                return server;
            }
        }
    }

    /// <summary>Picks the icon server for the vault's server (ADR 19); drops the cache if it changed.</summary>
    /// <param name="vaultServer">The server <c>bw status</c> reports; null means the US cloud.</param>
    public void UseVaultServer(Uri? vaultServer)
    {
        var next = IconServerFor(vaultServer);
        bool changed;
        lock (gate)
        {
            changed = next != server;
            server = next;
        }

        if (changed)
        {
            Clear();
        }
    }

    /// <summary>
    /// The icon of a host, fetched once and then kept until <see cref="Clear"/>. Null when icons
    /// are off, the host may not be sent, or the fetch failed or returned no usable image.
    /// </summary>
    /// <param name="host">A login's host, as <see cref="VaultIndex.FirstHost"/> returns it.</param>
    /// <returns>The image bytes, or null. The caller must not change them.</returns>
    public Task<byte[]?> GetAsync(string? host)
    {
        Uri? iconServer;
        CancellationToken token;
        ConcurrentDictionary<string, Lazy<Task<byte[]?>>> current;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!enabled || server is null)
            {
                return Task.FromResult<byte[]?>(null);
            }

            iconServer = server;
            token = generation.Token;
            current = cache;
        }

        if (NormalizeHost(host) is not { } name)
        {
            return Task.FromResult<byte[]?>(null);
        }

        return current.GetOrAdd(name, key => new Lazy<Task<byte[]?>>(() => FetchAsync(iconServer, key, token))).Value;
    }

    /// <summary>Drops every cached icon and abandons fetches still running; called at every lock.</summary>
    public void Clear()
    {
        CancellationTokenSource old;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            old = generation;
            generation = new CancellationTokenSource();
            cache = new ConcurrentDictionary<string, Lazy<Task<byte[]?>>>(StringComparer.Ordinal);
        }

        old.Cancel();
        old.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cache = new ConcurrentDictionary<string, Lazy<Task<byte[]?>>>(StringComparer.Ordinal);
        }

        generation.Cancel();
        generation.Dispose();
        http.Dispose();
        requests.Dispose();
    }

    /// <summary>The icon server for a vault server (ADR 19), or null when icons cannot be fetched safely.</summary>
    /// <param name="vaultServer">The server <c>bw status</c> reports; null means the US cloud.</param>
    /// <returns>The base address icons are fetched under.</returns>
    internal static Uri? IconServerFor(Uri? vaultServer)
    {
        if (vaultServer is null)
        {
            return UsCloudIcons;
        }

        var host = vaultServer.Host;
        if (IsUnder(host, "bitwarden.com"))
        {
            return UsCloudIcons;
        }

        if (IsUnder(host, "bitwarden.eu"))
        {
            return EuCloudIcons;
        }

        // Self-hosted: the icons service sits under /icons/ of the server. Never over plain HTTP,
        // which would show every host to the network.
        if (vaultServer.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(vaultServer.UserInfo))
        {
            return null;
        }

        var path = vaultServer.AbsolutePath.TrimEnd('/');
        return new UriBuilder(Uri.UriSchemeHttps, vaultServer.Host, vaultServer.Port, path + "/icons/").Uri;
    }

    /// <summary>
    /// The host as it is sent to the icon server: lower case ASCII (punycode), or null when it
    /// must not be sent: an IP address, a name without a dot, a private or reserved domain, or
    /// anything but a plain DNS name.
    /// </summary>
    /// <param name="host">A login's host.</param>
    /// <returns>The host to send, or null.</returns>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Host names are sent in lower case, as DNS names are written.")]
    internal static string? NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > MaximumHostLength)
        {
            return null;
        }

        string ascii;
        try
        {
            ascii = new IdnMapping { AllowUnassigned = false, UseStd3AsciiRules = true }
                .GetAscii(host.TrimEnd('.'))
                .ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (ascii.Length > MaximumHostLength || !ascii.Contains('.', StringComparison.Ordinal) || IPAddress.TryParse(ascii, out _))
        {
            return null;
        }

        // A dotted name of digits only ("10.0.0") is an IP address to some parsers.
        if (ascii.All(c => char.IsAsciiDigit(c) || c == '.'))
        {
            return null;
        }

        return PrivateDomains.Any(domain => IsUnder(ascii, domain)) ? null : ascii;
    }

    /// <summary>Whether the bytes start like an image format Command Palette can show.</summary>
    /// <param name="data">The response body.</param>
    /// <returns>True for PNG, JPEG, GIF, ICO, BMP or WebP.</returns>
    internal static bool LooksLikeImage(ReadOnlySpan<byte> data) =>
        data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
        || data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])
        || data.StartsWith("GIF87a"u8)
        || data.StartsWith("GIF89a"u8)
        || data.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0x01, 0x00])
        || (data.Length > 14 && data.StartsWith("BM"u8))
        || (data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8));

    private static bool IsUnder(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    private async Task<byte[]?> FetchAsync(Uri iconServer, string host, CancellationToken token)
    {
        try
        {
            await requests.WaitAsync(token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(iconServer, host + "/icon.png"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            var data = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            return data.Length is > 0 and <= MaximumBytes && LooksLikeImage(data) ? data : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or ObjectDisposedException)
        {
            // Offline, too large, timed out or abandoned at a lock: show the default icon.
            return null;
        }
        finally
        {
            try
            {
                _ = requests.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed while this request ran.
            }
        }
    }
}
