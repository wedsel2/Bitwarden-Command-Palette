namespace BitwardenCommandPalette.Core.Bw;

/// <summary>Where <see cref="BwLocator"/> looks, normally read from the environment.</summary>
public sealed record BwSearchLocations
{
    /// <summary>The bw path from the extension's settings, if the user set one.</summary>
    public string? ConfiguredPath { get; init; }

    /// <summary>The PATH variable.</summary>
    public string? PathVariable { get; init; }

    /// <summary>%LOCALAPPDATA%, for winget.</summary>
    public string? LocalAppData { get; init; }

    /// <summary>%USERPROFILE%, for Scoop.</summary>
    public string? UserProfile { get; init; }

    /// <summary>%ProgramData%, for Chocolatey.</summary>
    public string? ProgramData { get; init; }

    /// <summary>Reads the locations from this process's environment.</summary>
    /// <param name="configuredPath">The bw path from the settings, if any.</param>
    /// <returns>The locations to search.</returns>
    public static BwSearchLocations FromEnvironment(string? configuredPath) => new()
    {
        ConfiguredPath = configuredPath,
        PathVariable = Environment.GetEnvironmentVariable("PATH"),
        LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    };
}

/// <summary>The outcome of a search for bw.</summary>
public enum BwLocationOutcome
{
    /// <summary>A signed bw.exe was found.</summary>
    Found,

    /// <summary>No bw.exe was found anywhere.</summary>
    NotFound,

    /// <summary>Only bw.exe files that are not signed by Bitwarden Inc. were found.</summary>
    OnlyUntrustedFound,

    /// <summary>The configured path is not a fully qualified path to an existing file.</summary>
    ConfiguredPathMissing,

    /// <summary>The configured file is not signed by Bitwarden Inc.</summary>
    ConfiguredPathUntrusted,
}

/// <summary>The result of a search for bw.</summary>
/// <param name="Outcome">What the search found.</param>
/// <param name="ExecutablePath">The signed bw.exe, when <see cref="Outcome"/> is <see cref="BwLocationOutcome.Found"/>.</param>
public sealed record BwLocation(BwLocationOutcome Outcome, string? ExecutablePath = null);

/// <summary>
/// Finds a <c>bw.exe</c> signed by Bitwarden Inc. (ADR 7, ADR 15). A configured path is used
/// alone, without falling back. Otherwise the known install locations, then the fully
/// qualified PATH entries, are tried in order, links are followed to their target, and the
/// first signed file wins. Package-manager shims are not signed by Bitwarden and are skipped.
/// </summary>
public sealed class BwLocator
{
    private const string FileName = "bw.exe";

    private readonly IExecutableVerifier verifier;

    /// <summary>Creates a locator that accepts only files signed by Bitwarden Inc.</summary>
    public BwLocator()
        : this(new AuthenticodeVerifier())
    {
    }

    /// <summary>Creates a locator with the given verifier.</summary>
    /// <param name="verifier">Decides which files may be run.</param>
    internal BwLocator(IExecutableVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        this.verifier = verifier;
    }

    /// <summary>Searches for bw.</summary>
    /// <param name="locations">Where to look.</param>
    /// <returns>The signed bw.exe, or why none was found.</returns>
    public BwLocation Locate(BwSearchLocations locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        if (!string.IsNullOrWhiteSpace(locations.ConfiguredPath))
        {
            var configured = ExecutableFile.Resolve(locations.ConfiguredPath);
            return configured is null
                ? new BwLocation(BwLocationOutcome.ConfiguredPathMissing)
                : verifier.IsTrusted(configured)
                    ? new BwLocation(BwLocationOutcome.Found, configured)
                    : new BwLocation(BwLocationOutcome.ConfiguredPathUntrusted);
        }

        var sawUntrusted = false;
        foreach (var candidate in Candidates(locations))
        {
            if (ExecutableFile.Resolve(candidate) is not { } path)
            {
                continue;
            }

            if (verifier.IsTrusted(path))
            {
                return new BwLocation(BwLocationOutcome.Found, path);
            }

            sawUntrusted = true;
        }

        return new BwLocation(sawUntrusted ? BwLocationOutcome.OnlyUntrustedFound : BwLocationOutcome.NotFound);
    }

    private static IEnumerable<string> Candidates(BwSearchLocations locations)
    {
        if (FullyQualified(locations.LocalAppData) is { } localAppData)
        {
            var winget = Path.Combine(localAppData, "Microsoft", "WinGet");
            yield return Path.Combine(winget, "Links", FileName);
            var packages = new DirectoryInfo(Path.Combine(winget, "Packages"));
            if (packages.Exists)
            {
                foreach (var package in packages.EnumerateDirectories("Bitwarden.CLI_*").OrderBy(directory => directory.Name, StringComparer.Ordinal))
                {
                    yield return Path.Combine(package.FullName, FileName);
                }
            }
        }

        if (FullyQualified(locations.UserProfile) is { } userProfile)
        {
            yield return Path.Combine(userProfile, "scoop", "apps", "bitwarden-cli", "current", FileName);
        }

        if (FullyQualified(locations.ProgramData) is { } programData)
        {
            yield return Path.Combine(programData, "chocolatey", "lib", "bitwarden-cli", "tools", FileName);
        }

        // A relative PATH entry would resolve against the working directory; it is ignored.
        foreach (var entry in (locations.PathVariable ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (FullyQualified(entry.Trim('"')) is { } directory)
            {
                yield return Path.Combine(directory, FileName);
            }
        }
    }

    private static string? FullyQualified(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) ? path : null;

}
