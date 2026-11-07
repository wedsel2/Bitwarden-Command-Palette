using System.Globalization;
using System.Runtime.InteropServices;

namespace BitwardenCommandPalette.Core.Clipboard;

/// <summary>
/// The Windows clipboard through the Win32 API. The WinRT <c>Clipboard</c> class needs a UI
/// thread, which the extension does not have; the Win32 formats below are the documented
/// equivalents of its <c>IsAllowedInHistory</c> and <c>IsRoamable</c> options (ADR 11).
/// </summary>
/// <remarks>
/// The text is written character by character from the string into the clipboard's memory,
/// without a managed copy, and compared there the same way; a buffer that could not be handed
/// to the clipboard is zeroed before it is freed.
/// </remarks>
internal sealed partial class Win32Clipboard : IClipboard
{
    /// <summary>A DWORD 0 in this format keeps the content out of clipboard history.</summary>
    internal const string HistoryFormatName = "CanIncludeInClipboardHistory";

    /// <summary>A DWORD 0 in this format keeps the content off cloud clipboard.</summary>
    internal const string CloudFormatName = "CanUploadToCloudClipboard";

    /// <summary>Any data in this format asks clipboard managers to skip the content.</summary>
    internal const string MonitorFormatName = "ExcludeClipboardContentFromMonitorProcessing";

    private const uint UnicodeText = 13;
    private const uint MoveableMemory = 0x0002;

    // Another application may hold the clipboard open for a moment; try for about half a second.
    private const int OpenAttempts = 10;
    private static readonly TimeSpan OpenRetryInterval = TimeSpan.FromMilliseconds(50);

    private static readonly Lazy<(uint History, uint Cloud, uint Monitor)> ExclusionFormats =
        new(
            () => (Register(HistoryFormatName), Register(CloudFormatName), Register(MonitorFormatName)),
            LazyThreadSafetyMode.PublicationOnly); // a failed registration is tried again, not cached

    /// <inheritdoc />
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var (history, cloud, monitor) = ExclusionFormats.Value;
        using var session = Open();
        if (!EmptyClipboard())
        {
            throw Failure("empty the clipboard");
        }

        try
        {
            SetDword(history, 0);
            SetDword(cloud, 0);
            SetDword(monitor, 0);
            SetUnicodeText(text);
        }
        catch (ClipboardException)
        {
            // Never leave the text on the clipboard without all of its marks.
            _ = EmptyClipboard();
            throw;
        }
    }

    /// <inheritdoc />
    public bool ClearIfHolds(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var session = Open();
        if (!ContainsText(text))
        {
            return false;
        }

        return EmptyClipboard() ? true : throw Failure("empty the clipboard");
    }

    /// <summary>Whether the clipboard text is exactly <paramref name="text"/>.</summary>
    /// <param name="text">The text to compare with, ordinally.</param>
    /// <returns>True only for an exact match.</returns>
    internal static bool HoldsText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var session = Open();
        return ContainsText(text);
    }

    /// <summary>Empties the clipboard.</summary>
    internal static void Clear()
    {
        using var session = Open();
        if (!EmptyClipboard())
        {
            throw Failure("empty the clipboard");
        }
    }

    /// <summary>Whether data in the registered format <paramref name="name"/> is on the clipboard.</summary>
    /// <param name="name">A registered clipboard format name.</param>
    /// <returns>True if data in that format is on the clipboard.</returns>
    internal static bool HasFormat(string name) => IsClipboardFormatAvailable(Register(name));

    // The clipboard must be open.
    private static bool ContainsText(string text)
    {
        if (!IsClipboardFormatAvailable(UnicodeText))
        {
            return false;
        }

        var handle = GetClipboardData(UnicodeText);
        if (handle == 0)
        {
            return false;
        }

        var data = GlobalLock(handle);
        if (data == 0)
        {
            return false;
        }

        try
        {
            // The text ends at the first NUL or at the end of the block, whichever comes first.
            var capacity = GlobalSize(handle) / sizeof(char);
            if (capacity < (nuint)text.Length)
            {
                return false;
            }

            for (var i = 0; i < text.Length; i++)
            {
                if ((char)Marshal.ReadInt16(data, i * sizeof(char)) != text[i])
                {
                    return false;
                }
            }

            return capacity == (nuint)text.Length || Marshal.ReadInt16(data, text.Length * sizeof(char)) == 0;
        }
        finally
        {
            _ = GlobalUnlock(handle);
        }
    }

    private static uint Register(string name)
    {
        var format = RegisterClipboardFormatW(name);
        return format != 0 ? format : throw Failure("register a clipboard format");
    }

    private static Session Open()
    {
        for (var attempt = 1; ; attempt++)
        {
            // No owner window: the extension has none, and none is needed to set data.
            if (OpenClipboard(0))
            {
                return default;
            }

            if (attempt == OpenAttempts)
            {
                throw Failure("open the clipboard");
            }

            Thread.Sleep(OpenRetryInterval);
        }
    }

    private static void SetDword(uint format, int value) =>
        SetData(format, sizeof(int), buffer => Marshal.WriteInt32(buffer, value));

    private static void SetUnicodeText(string text)
    {
        var length = checked((text.Length + 1) * sizeof(char));
        SetData(UnicodeText, length, buffer =>
        {
            for (var i = 0; i < text.Length; i++)
            {
                Marshal.WriteInt16(buffer, i * sizeof(char), text[i]);
            }

            Marshal.WriteInt16(buffer, text.Length * sizeof(char), 0);
        });
    }

    private static void SetData(uint format, int length, Action<nint> fill)
    {
        var handle = GlobalAlloc(MoveableMemory, (nuint)length);
        if (handle == 0)
        {
            throw Failure("allocate clipboard memory");
        }

        var buffer = GlobalLock(handle);
        if (buffer == 0)
        {
            _ = GlobalFree(handle);
            throw Failure("lock clipboard memory");
        }

        fill(buffer);
        _ = GlobalUnlock(handle);

        // On success the clipboard owns the memory; otherwise it is still ours to wipe and free.
        if (SetClipboardData(format, handle) == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            buffer = GlobalLock(handle);
            if (buffer != 0)
            {
                for (var offset = 0; offset < length; offset++)
                {
                    Marshal.WriteByte(buffer, offset, 0);
                }
                _ = GlobalUnlock(handle);
            }

            _ = GlobalFree(handle);
            throw Failure("set clipboard data", error);
        }
    }

    private static ClipboardException Failure(string action) => Failure(action, Marshal.GetLastPInvokeError());

    private static ClipboardException Failure(string action, int error) =>
        new(string.Create(CultureInfo.InvariantCulture, $"Could not {action} (Windows error {error})."));

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SetClipboardData(uint format, nint memory);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint RegisterClipboardFormatW(string name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nuint GlobalSize(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GlobalFree(nint memory);

    /// <summary>Closes the clipboard when disposed.</summary>
    private readonly struct Session : IDisposable
    {
        public void Dispose() => _ = CloseClipboard();
    }
}
