using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using BitwardenCommandPalette.Core;
using BitwardenCommandPalette.Core.Bw;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Storage.Streams;

namespace BitwardenCommandPalette.Extension;

/// <summary>
/// Puts a login's website icon on its list item once Core has it (ADR 19). The icon goes to
/// Command Palette as an in-memory stream, never as a URL or file, so no list of websites ends up
/// in an image cache on disk.
/// </summary>
internal static class LoginIcons
{
    // One stream per fetched icon; it goes when Core drops the bytes at a lock.
    private static readonly ConditionalWeakTable<byte[], IconInfo> Converted = [];

    /// <summary>Sets the item's icon now if Core already has it, or when the fetch finishes.</summary>
    public static void Attach(ListItem item, VaultServices services, BwLogin login)
    {
        Task<byte[]?> fetch;
        try
        {
            fetch = services.Icons.GetAsync(VaultIndex.FirstHost(login));
        }
        catch (ObjectDisposedException)
        {
            // The extension is shutting down.
            return;
        }

        if (fetch.IsCompletedSuccessfully)
        {
            if (fetch.Result is { } data)
            {
                item.Icon = For(data);
            }

            return;
        }

        _ = SetWhenFetchedAsync(item, fetch);
    }

    private static async Task SetWhenFetchedAsync(ListItem item, Task<byte[]?> fetch)
    {
        // Core never throws from a fetch; a failure is a null icon.
        if (await fetch.ConfigureAwait(false) is { } data)
        {
            item.Icon = For(data);
        }
    }

    private static IconInfo For(byte[] data) => Converted.GetValue(data, Create);

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The icon keeps the stream for Command Palette to read; it goes with the icon.")]
    private static IconInfo Create(byte[] data)
    {
        var stream = new InMemoryRandomAccessStream();
        _ = stream.WriteAsync(data.AsBuffer()).AsTask().GetAwaiter().GetResult();
        stream.Seek(0);
        return IconInfo.FromStream(stream);
    }
}
