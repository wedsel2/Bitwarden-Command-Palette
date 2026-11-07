using System.Text.Json;

namespace BitwardenCommandPalette.Core.Tests;

public sealed class SecretTypeTests
{
    private const string Canary = "canary-secret-8d2b";

    [Theory]
    [InlineData(nameof(MasterPassword))]
    [InlineData(nameof(SessionKey))]
    [InlineData(nameof(Secret))]
    public void ToStringAndInterpolationShowThePlaceholder(string type)
    {
        var secret = Create(type);

        Assert.Equal(Redacted.Placeholder, secret.ToString());
        Assert.Equal($"value {Redacted.Placeholder}", $"value {secret}");
    }

    [Theory]
    [InlineData(nameof(MasterPassword))]
    [InlineData(nameof(SessionKey))]
    [InlineData(nameof(Secret))]
    public void SerialisationShowsThePlaceholder(string type)
    {
        var json = type switch
        {
            nameof(MasterPassword) => JsonSerializer.Serialize(new { value = new MasterPassword(Canary) }),
            nameof(SessionKey) => JsonSerializer.Serialize(new { value = new SessionKey(Canary) }),
            _ => JsonSerializer.Serialize(new { value = new Secret(Canary) }),
        };

        Assert.DoesNotContain(Canary, json, StringComparison.Ordinal);
        Assert.Contains(Redacted.Placeholder, json, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecretIsNeverReadFromJson()
    {
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<MasterPassword>("\"x\""));
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<SessionKey>("\"x\""));
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<Secret>("\"x\""));
    }

    [Fact]
    public void ARecordHoldingASecretPrintsThePlaceholder()
    {
        var holder = new Holder(new Secret(Canary));

        Assert.DoesNotContain(Canary, holder.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheNamedRevealMethodsReturnTheValue()
    {
        Assert.Equal(Canary, new MasterPassword(Canary).RevealForChildEnvironment());
        Assert.Equal(Canary, new SessionKey(Canary).RevealForChildEnvironment());
        Assert.Equal(Canary, new Secret(Canary).RevealForClipboard());
    }

    [Fact]
    public void AnEmptySecretIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new MasterPassword(string.Empty));
        Assert.Throws<ArgumentNullException>(() => new MasterPassword(null!));
    }

    private static SecretValue Create(string type) => type switch
    {
        nameof(MasterPassword) => new MasterPassword(Canary),
        nameof(SessionKey) => new SessionKey(Canary),
        _ => new Secret(Canary),
    };

    private sealed record Holder(Secret Value);
}
