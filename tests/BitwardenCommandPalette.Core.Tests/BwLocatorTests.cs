using BitwardenCommandPalette.Core.Bw;

namespace BitwardenCommandPalette.Core.Tests;

public sealed class BwLocatorTests : IDisposable
{
    private const string Signed = "signed";
    private const string Unsigned = "unsigned";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("bwlocator-");
    private readonly BwLocator locator = new(new ContentVerifier());

    public void Dispose() => root.Delete(recursive: true);

    [Fact]
    public void AConfiguredSignedFileIsUsed()
    {
        var path = Write(Signed, "custom", "bw.exe");

        Assert.Equal(new BwLocation(BwLocationOutcome.Found, path), locator.Locate(new BwSearchLocations { ConfiguredPath = path }));
    }

    [Fact]
    public void AConfiguredPathNeverFallsBack()
    {
        var configured = Write(Unsigned, "custom", "bw.exe");
        var onPath = Write(Signed, "tools", "bw.exe");

        var location = locator.Locate(new BwSearchLocations { ConfiguredPath = configured, PathVariable = Path.GetDirectoryName(onPath) });

        Assert.Equal(new BwLocation(BwLocationOutcome.ConfiguredPathUntrusted), location);
    }

    [Theory]
    [InlineData("bw.exe")]
    [InlineData(@"C:\does-not-exist\bw.exe")]
    public void AConfiguredPathThatIsMissingOrRelativeIsReported(string path)
    {
        Assert.Equal(BwLocationOutcome.ConfiguredPathMissing, locator.Locate(new BwSearchLocations { ConfiguredPath = path }).Outcome);
    }

    [Fact]
    public void WingetPackagesAreFound()
    {
        var path = Write(Signed, "local", "Microsoft", "WinGet", "Packages", "Bitwarden.CLI_Microsoft.Winget.Source_8wekyb3d8bbwe", "bw.exe");

        Assert.Equal(path, locator.Locate(new BwSearchLocations { LocalAppData = Folder("local") }).ExecutablePath);
    }

    [Fact]
    public void AWingetLinkIsFollowedToItsTarget()
    {
        var target = Write(Signed, "local", "Microsoft", "WinGet", "Packages", "Bitwarden.CLI_x", "bw.exe");
        var link = Path.Combine(Folder("local", "Microsoft", "WinGet", "Links"), "bw.exe");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Creating a symbolic link needs Developer Mode or administrator rights here.");
        }

        Assert.Equal(target, locator.Locate(new BwSearchLocations { LocalAppData = Folder("local") }).ExecutablePath);
    }

    [Fact]
    public void ScoopAndChocolateyPackagesAreFound()
    {
        var scoop = Write(Signed, "profile", "scoop", "apps", "bitwarden-cli", "current", "bw.exe");
        var chocolatey = Write(Signed, "data", "chocolatey", "lib", "bitwarden-cli", "tools", "bw.exe");

        Assert.Equal(scoop, locator.Locate(new BwSearchLocations { UserProfile = Folder("profile") }).ExecutablePath);
        Assert.Equal(chocolatey, locator.Locate(new BwSearchLocations { ProgramData = Folder("data") }).ExecutablePath);
    }

    [Fact]
    public void AnUnsignedShimOnPathIsSkippedForALaterSignedFile()
    {
        var shim = Write(Unsigned, "shims", "bw.exe");
        var real = Write(Signed, "tools", "bw.exe");
        var path = string.Join(Path.PathSeparator, $"\"{Path.GetDirectoryName(shim)}\"", "relative\\folder", " ", Path.GetDirectoryName(real));

        Assert.Equal(real, locator.Locate(new BwSearchLocations { PathVariable = path }).ExecutablePath);
    }

    [Fact]
    public void OnlyUnsignedFilesAreReportedAsSuch()
    {
        var shim = Write(Unsigned, "shims", "bw.exe");

        Assert.Equal(new BwLocation(BwLocationOutcome.OnlyUntrustedFound), locator.Locate(new BwSearchLocations { PathVariable = Path.GetDirectoryName(shim) }));
    }

    [Fact]
    public void NothingFoundIsReportedAsSuch()
    {
        var locations = new BwSearchLocations { PathVariable = root.FullName, LocalAppData = root.FullName, UserProfile = "relative", ProgramData = null };

        Assert.Equal(new BwLocation(BwLocationOutcome.NotFound), locator.Locate(locations));
    }

    [Fact]
    public void TheEnvironmentProvidesTheLocations()
    {
        var locations = BwSearchLocations.FromEnvironment(configuredPath: null);

        Assert.Null(locations.ConfiguredPath);
        Assert.Equal(Environment.GetEnvironmentVariable("PATH"), locations.PathVariable);
        Assert.True(Path.IsPathFullyQualified(locations.LocalAppData!));
        Assert.Throws<ArgumentNullException>(() => locator.Locate(null!));
        Assert.NotNull(new BwLocator());
        Assert.Throws<ArgumentNullException>(() => new BwLocator(null!));
    }

    private string Folder(params string[] parts) => Directory.CreateDirectory(Path.Combine([root.FullName, .. parts])).FullName;

    private string Write(string content, params string[] parts)
    {
        var path = Path.Combine([root.FullName, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Trusts files whose content is "signed", standing in for the signature check.</summary>
    private sealed class ContentVerifier : IExecutableVerifier
    {
        public bool IsTrusted(string path) => File.ReadAllText(path) == Signed;
    }
}
