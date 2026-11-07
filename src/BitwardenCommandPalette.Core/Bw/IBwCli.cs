namespace BitwardenCommandPalette.Core.Bw;

/// <summary>
/// The Bitwarden CLI, as the rest of the code sees it. Only <see cref="BwCli"/> implements it,
/// and only it starts a process (ADR 7). Every method throws <see cref="BwCliException"/> on
/// failure.
/// </summary>
public interface IBwCli
{
    /// <summary>Runs <c>bw status</c>.</summary>
    /// <param name="session">The session key to check, if any.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>Whether the CLI is logged in and unlocked.</returns>
    Task<BwStatus> GetStatusAsync(SessionKey? session = null, CancellationToken cancellationToken = default);

    /// <summary>Runs <c>bw unlock</c>; the password goes on the child's environment only.</summary>
    /// <param name="password">The master password.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The session key.</returns>
    Task<SessionKey> UnlockAsync(MasterPassword password, CancellationToken cancellationToken = default);

    /// <summary>Runs <c>bw lock</c>, which makes every session key worthless.</summary>
    /// <param name="session">
    /// The current session key, passed when known so bw locks exactly that session; at
    /// start-up there is none.
    /// </param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>A task that completes when the vault is locked.</returns>
    Task LockAsync(SessionKey? session = null, CancellationToken cancellationToken = default);

    /// <summary>Runs <c>bw sync</c>.</summary>
    /// <param name="session">The session key.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>A task that completes when the sync is done.</returns>
    Task SyncAsync(SessionKey session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <c>bw list items</c> and keeps only the metadata of logins; every password and
    /// TOTP secret in the output is discarded while parsing (ADR 10).
    /// </summary>
    /// <param name="session">The session key.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The logins, without secrets.</returns>
    Task<IReadOnlyList<BwLogin>> ListLoginsAsync(SessionKey session, CancellationToken cancellationToken = default);

    /// <summary>Runs <c>bw get password</c> for one item.</summary>
    /// <param name="session">The session key.</param>
    /// <param name="itemId">The item's ID; a search term is refused.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The password.</returns>
    Task<Secret> GetPasswordAsync(SessionKey session, string itemId, CancellationToken cancellationToken = default);

    /// <summary>Runs <c>bw get totp</c> for one item.</summary>
    /// <param name="session">The session key.</param>
    /// <param name="itemId">The item's ID; a search term is refused.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The current TOTP code.</returns>
    Task<Secret> GetTotpAsync(SessionKey session, string itemId, CancellationToken cancellationToken = default);
}
