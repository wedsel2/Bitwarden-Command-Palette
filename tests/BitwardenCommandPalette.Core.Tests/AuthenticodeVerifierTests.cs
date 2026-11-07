using BitwardenCommandPalette.Core.Bw;

namespace BitwardenCommandPalette.Core.Tests;

/// <summary>
/// Runs the real Windows signature check. The .NET runtime's own System.Private.CoreLib.dll
/// stands in for a signed file: it carries an embedded Authenticode signature from Microsoft.
/// </summary>
public sealed class AuthenticodeVerifierTests : IDisposable
{
    // How Microsoft signs the .NET runtime.
    private const string RuntimeSigner = ".NET";
    private const string Microsoft = "Microsoft Corporation";

    private static readonly string SignedFile = typeof(object).Assembly.Location;

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("authenticode-");

    public void Dispose() => folder.Delete(recursive: true);

    [Fact]
    public void AFileSignedByTheExpectedPublisherIsTrusted()
    {
        Assert.True(new AuthenticodeVerifier(RuntimeSigner, Microsoft).IsTrusted(SignedFile));
    }

    [Fact]
    public void AFileSignedBySomeoneElseIsRefused()
    {
        Assert.False(new AuthenticodeVerifier().IsTrusted(SignedFile));
    }

    [Theory]
    [InlineData(Microsoft, Microsoft)]
    [InlineData(RuntimeSigner, RuntimeSigner)]
    [InlineData(".net", Microsoft)]
    public void BothNamesMustMatchExactly(string commonName, string organisation)
    {
        Assert.False(new AuthenticodeVerifier(commonName, organisation).IsTrusted(SignedFile));
    }

    [Fact]
    public void AnUnsignedFileIsRefused()
    {
        Assert.False(new AuthenticodeVerifier(RuntimeSigner, Microsoft).IsTrusted(FakeBwHost.ExecutablePath));
    }

    [Theory]
    [InlineData("bw.exe")]
    [InlineData(@"C:\does-not-exist\bw.exe")]
    [InlineData("C:\\a\0b.exe")]
    public void AMissingOrRelativePathIsRefused(string path)
    {
        Assert.False(new AuthenticodeVerifier(RuntimeSigner, Microsoft).IsTrusted(path));
    }

    [Fact]
    public void ATamperedFileIsRefusedEvenAfterItWasTrusted()
    {
        var copy = Path.Combine(folder.FullName, "signed.dll");
        File.Copy(SignedFile, copy);
        var verifier = new AuthenticodeVerifier(RuntimeSigner, Microsoft);
        Assert.True(verifier.IsTrusted(copy));

        var bytes = File.ReadAllBytes(copy);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(copy, bytes);
        File.SetLastWriteTimeUtc(copy, DateTime.UtcNow.AddMinutes(1));

        Assert.False(verifier.IsTrusted(copy));

        // A failed check is not remembered: the restored file passes again.
        File.Copy(SignedFile, copy, overwrite: true);
        File.SetLastWriteTimeUtc(copy, DateTime.UtcNow.AddMinutes(2));
        Assert.True(verifier.IsTrusted(copy));
    }

    [Fact]
    public void ALinkIsRefusedSoItsTargetMustBePassed()
    {
        var link = Path.Combine(folder.FullName, "link.dll");
        try
        {
            File.CreateSymbolicLink(link, SignedFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Creating a symbolic link needs Developer Mode or administrator rights here.");
        }

        Assert.False(new AuthenticodeVerifier(RuntimeSigner, Microsoft).IsTrusted(link));
    }

    [Fact]
    public void ThePublisherIsRequired()
    {
        Assert.Throws<ArgumentException>(() => new AuthenticodeVerifier(string.Empty, Microsoft));
        Assert.Throws<ArgumentException>(() => new AuthenticodeVerifier(RuntimeSigner, string.Empty));
        Assert.Throws<ArgumentNullException>(() => new AuthenticodeVerifier().IsTrusted(null!));
    }
}
