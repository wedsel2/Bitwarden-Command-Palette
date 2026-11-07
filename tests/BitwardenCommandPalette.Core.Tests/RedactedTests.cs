namespace BitwardenCommandPalette.Core.Tests;

public sealed class RedactedTests
{
    [Fact]
    public void HideNeverReturnsTheValue()
    {
        const string canary = "canary-secret-3f9a";

        var shown = Redacted.Hide(canary);

        Assert.Equal(Redacted.Placeholder, shown);
        Assert.DoesNotContain(canary, shown, StringComparison.Ordinal);
    }

    [Fact]
    public void HideAcceptsNull()
    {
        Assert.Equal(Redacted.Placeholder, Redacted.Hide<string?>(null));
    }
}
