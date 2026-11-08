using BitwardenCommandPalette.Core.Clipboard;
using BitwardenCommandPalette.Core.Session;
using BitwardenCommandPalette.Core.Settings;

namespace BitwardenCommandPalette.Core.Tests;

public sealed class VaultSettingsTests
{
    [Fact]
    public void EveryOfferedChoiceReadsBackAsItself()
    {
        foreach (var choice in VaultSettings.IdleTimeoutChoices)
        {
            Assert.Equal(choice.Value, VaultSettings.ToMinutes(VaultSettings.IdleTimeout(choice.Value)));
        }

        foreach (var choice in VaultSettings.AbsoluteTimeoutChoices)
        {
            Assert.Equal(choice.Value, VaultSettings.ToMinutes(VaultSettings.AbsoluteTimeout(choice.Value)));
        }

        foreach (var choice in VaultSettings.ClearDelayChoices)
        {
            Assert.Equal(choice.Value, VaultSettings.ToSeconds(VaultSettings.ClearDelay(choice.Value)));
        }

        foreach (var choice in VaultSettings.SyncIntervalChoices)
        {
            Assert.Equal(choice.Value, VaultSettings.ToMinutes(VaultSettings.SyncInterval(choice.Value)));
        }
    }

    [Fact]
    public void TheDefaultsAreOffered()
    {
        Assert.Contains(VaultSettings.IdleTimeoutChoices, c => c.Value == VaultSettings.ToMinutes(SessionManager.DefaultIdleTimeout));
        Assert.Contains(VaultSettings.AbsoluteTimeoutChoices, c => c.Value == VaultSettings.ToMinutes(SessionManager.DefaultAbsoluteTimeout));
        Assert.Contains(VaultSettings.ClearDelayChoices, c => c.Value == VaultSettings.ToSeconds(ClipboardService.DefaultClearDelay));
        Assert.Contains(VaultSettings.SyncIntervalChoices, c => c.Value == VaultSettings.ToMinutes(SessionManager.DefaultSyncInterval));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ten")]
    [InlineData("-5")]
    [InlineData(" 5")]
    [InlineData("5.5")]
    [InlineData("0")]
    [InlineData("16")]
    [InlineData("99999999999")]
    public void AnIdleTimeoutOutsideTheLimitsFallsBackToTheDefault(string? stored) =>
        Assert.Equal(SessionManager.DefaultIdleTimeout, VaultSettings.IdleTimeout(stored));

    [Theory]
    [InlineData("14")]
    [InlineData("241")]
    [InlineData("100000")]
    public void AnAbsoluteTimeoutOutsideTheLimitsFallsBackToTheDefault(string stored) =>
        Assert.Equal(SessionManager.DefaultAbsoluteTimeout, VaultSettings.AbsoluteTimeout(stored));

    [Theory]
    [InlineData("4")]
    [InlineData("121")]
    public void AClearDelayOutsideTheLimitsFallsBackToTheDefault(string stored) =>
        Assert.Equal(ClipboardService.DefaultClearDelay, VaultSettings.ClearDelay(stored));

    [Fact]
    public void ASyncIntervalOverADayFallsBackToTheDefault() =>
        Assert.Equal(SessionManager.DefaultSyncInterval, VaultSettings.SyncInterval("1441"));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("\"\"", null)]
    [InlineData(@"C:\bw\bw.exe", @"C:\bw\bw.exe")]
    [InlineData(@" ""C:\Program Files\bw\bw.exe"" ", @"C:\Program Files\bw\bw.exe")]
    public void TheBwPathIsTrimmedAndUnquoted(string? stored, string? expected) =>
        Assert.Equal(expected, VaultSettings.BwPath(stored));

    [Fact]
    public void ChoicesHaveReadableTitles()
    {
        Assert.Equal(["1 minute", "2 minutes", "5 minutes", "10 minutes", "15 minutes"], VaultSettings.IdleTimeoutChoices.Select(c => c.Title));
        Assert.Equal(["15 minutes", "30 minutes", "1 hour", "2 hours", "4 hours"], VaultSettings.AbsoluteTimeoutChoices.Select(c => c.Title));
        Assert.Equal(["10 seconds", "20 seconds", "30 seconds", "1 minute", "2 minutes"], VaultSettings.ClearDelayChoices.Select(c => c.Title));
        Assert.Equal("At every unlock", VaultSettings.SyncIntervalChoices[0].Title);
    }
}
