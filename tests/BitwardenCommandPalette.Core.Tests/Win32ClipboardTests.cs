using BitwardenCommandPalette.Core.Clipboard;

namespace BitwardenCommandPalette.Core.Tests;

/// <summary>
/// Runs the real Windows clipboard, so it overwrites and then empties whatever is on it. All
/// clipboard tests live in this one class, which xUnit never runs in parallel with itself.
/// </summary>
public sealed class Win32ClipboardTests : IDisposable
{
    private const string Text = "canary-win32-clipboard-5a9d";

    private readonly Win32Clipboard clipboard = new();

    public void Dispose() => clipboard.Clear();

    [Fact]
    public void TextIsSetWithEveryExclusionMark()
    {
        clipboard.SetText(Text);

        Assert.True(clipboard.HoldsText(Text));
        Assert.True(Win32Clipboard.HasFormat(Win32Clipboard.HistoryFormatName));
        Assert.True(Win32Clipboard.HasFormat(Win32Clipboard.CloudFormatName));
        Assert.True(Win32Clipboard.HasFormat(Win32Clipboard.MonitorFormatName));
    }

    [Theory]
    [InlineData("canary-win32-clipboard-5a9")]
    [InlineData("canary-win32-clipboard-5a9d ")]
    [InlineData("CANARY-WIN32-CLIPBOARD-5A9D")]
    [InlineData("")]
    public void OnlyTheExactTextMatches(string other)
    {
        clipboard.SetText(Text);

        Assert.False(clipboard.HoldsText(other));
    }

    [Fact]
    public void TextWithUnicodeAndSpacesRoundTrips()
    {
        const string text = " pässwörd 🔑 ";

        clipboard.SetText(text);

        Assert.True(clipboard.HoldsText(text));
    }

    [Fact]
    public void ClearEmptiesTheClipboard()
    {
        clipboard.SetText(Text);

        clipboard.Clear();

        Assert.False(clipboard.HoldsText(Text));
        Assert.False(Win32Clipboard.HasFormat(Win32Clipboard.HistoryFormatName));
    }

    [Fact]
    public void ANewCopyReplacesThePreviousText()
    {
        clipboard.SetText(Text);

        clipboard.SetText("second");

        Assert.False(clipboard.HoldsText(Text));
        Assert.True(clipboard.HoldsText("second"));
    }
}
