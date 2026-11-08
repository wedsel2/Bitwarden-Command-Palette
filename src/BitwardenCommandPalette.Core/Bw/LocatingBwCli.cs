namespace BitwardenCommandPalette.Core.Bw;

/// <summary>
/// An <see cref="IBwCli"/> that finds a signed <c>bw.exe</c> on first use (ADR 15), so the
/// extension can start, and show what is wrong, before bw is installed. A failed search is
/// repeated on the next call; a found bw is kept until <see cref="Reset"/>, for example after
/// the configured path changes.
/// </summary>
public sealed class LocatingBwCli : IBwCli
{
    private readonly Func<BwLocation> locate;
    private readonly Func<string, IBwCli> create;
    private readonly Lock gate = new();
    private IBwCli? cli;

    /// <summary>Creates a client that searches the settings path, the install locations and PATH.</summary>
    /// <param name="configuredPath">Returns the bw path from the settings, or null.</param>
    public LocatingBwCli(Func<string?> configuredPath)
        : this(
            () => new BwLocator().Locate(BwSearchLocations.FromEnvironment(configuredPath())),
            path => new BwCli(new BwCliOptions(path)))
    {
        ArgumentNullException.ThrowIfNull(configuredPath);
    }

    /// <summary>Creates a client with the given search and factory.</summary>
    /// <param name="locate">Finds bw.</param>
    /// <param name="create">Creates the client for a found bw.exe.</param>
    internal LocatingBwCli(Func<BwLocation> locate, Func<string, IBwCli> create)
    {
        ArgumentNullException.ThrowIfNull(locate);
        ArgumentNullException.ThrowIfNull(create);
        this.locate = locate;
        this.create = create;
    }

    /// <summary>Forgets the bw that was found, so the next call searches again.</summary>
    public void Reset()
    {
        lock (gate)
        {
            cli = null;
        }
    }

    /// <inheritdoc />
    public Task<BwStatus> GetStatusAsync(SessionKey? session = null, CancellationToken cancellationToken = default) =>
        Cli().GetStatusAsync(session, cancellationToken);

    /// <inheritdoc />
    public Task<SessionKey> UnlockAsync(MasterPassword password, CancellationToken cancellationToken = default) =>
        Cli().UnlockAsync(password, cancellationToken);

    /// <inheritdoc />
    public Task LockAsync(SessionKey? session = null, CancellationToken cancellationToken = default) =>
        Cli().LockAsync(session, cancellationToken);

    /// <inheritdoc />
    public Task SyncAsync(SessionKey session, CancellationToken cancellationToken = default) =>
        Cli().SyncAsync(session, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<BwLogin>> ListLoginsAsync(SessionKey session, CancellationToken cancellationToken = default) =>
        Cli().ListLoginsAsync(session, cancellationToken);

    /// <inheritdoc />
    public Task<Secret> GetPasswordAsync(SessionKey session, string itemId, CancellationToken cancellationToken = default) =>
        Cli().GetPasswordAsync(session, itemId, cancellationToken);

    /// <inheritdoc />
    public Task<Secret> GetTotpAsync(SessionKey session, string itemId, CancellationToken cancellationToken = default) =>
        Cli().GetTotpAsync(session, itemId, cancellationToken);

    // Throws BwCliException(UntrustedExecutable) while no signed bw.exe can be found.
    private IBwCli Cli()
    {
        lock (gate)
        {
            if (cli is not null)
            {
                return cli;
            }

            var location = locate();
            if (location.Outcome != BwLocationOutcome.Found || location.ExecutablePath is null)
            {
                throw new BwCliException(BwErrorKind.UntrustedExecutable);
            }

            cli = create(location.ExecutablePath);
            return cli;
        }
    }
}
