namespace BitwardenCommandPalette.Core.Bw;

/// <summary>The state <c>bw status</c> reports.</summary>
public enum BwVaultState
{
    /// <summary>The CLI is not logged in; the user runs <c>bw login</c> in a terminal (ADR 12).</summary>
    Unauthenticated,

    /// <summary>Logged in, vault locked.</summary>
    Locked,

    /// <summary>Logged in and unlocked for the session key that was passed.</summary>
    Unlocked,
}

/// <summary>What <c>bw status</c> returned. The account email is deliberately not kept.</summary>
/// <param name="State">Whether the CLI is logged in and unlocked.</param>
/// <param name="LastSync">When the vault was last synced, if ever.</param>
/// <param name="ServerUrl">The server the CLI is logged in to, if any.</param>
public sealed record BwStatus(BwVaultState State, DateTimeOffset? LastSync, Uri? ServerUrl);

/// <summary>
/// The metadata of one login, as kept by the vault index (ADR 10). It holds no password and
/// no TOTP secret, only whether a TOTP secret exists.
/// </summary>
/// <param name="Id">The item's ID, the only way <c>bw get</c> is ever called.</param>
/// <param name="Name">The item's name.</param>
/// <param name="Username">The username, if the login has one.</param>
/// <param name="Uris">The login's URIs, possibly none.</param>
/// <param name="FolderId">The folder's ID, if the item is in a folder.</param>
/// <param name="HasTotp">Whether the login has a TOTP secret.</param>
/// <param name="RequiresReprompt">Whether the item is marked for master password reprompt.</param>
public sealed record BwLogin(
    string Id,
    string Name,
    string? Username,
    IReadOnlyList<string> Uris,
    string? FolderId,
    bool HasTotp,
    bool RequiresReprompt)
{
    /// <summary>Only the ID: names and usernames are kept out of logs and messages.</summary>
    /// <returns>A description that names the item by ID.</returns>
    public override string ToString() => $"Login {Id}";
}

/// <summary>Why a <c>bw</c> call failed. Each kind has a fixed message of our own.</summary>
public enum BwErrorKind
{
    /// <summary>Any failure not listed below.</summary>
    Failed,

    /// <summary>bw could not be started.</summary>
    CouldNotStart,

    /// <summary>bw.exe is missing or not signed by Bitwarden Inc., so it was not started.</summary>
    UntrustedExecutable,

    /// <summary>bw did not finish in time and was stopped.</summary>
    TimedOut,

    /// <summary>bw succeeded but its output could not be read.</summary>
    UnexpectedOutput,

    /// <summary>The CLI is not logged in.</summary>
    NotLoggedIn,

    /// <summary>The vault is locked, or the session key is no longer valid.</summary>
    VaultLocked,

    /// <summary>The master password was wrong.</summary>
    InvalidPassword,

    /// <summary>The item does not exist, or it has no value for the requested field.</summary>
    NotFound,

    /// <summary>More than one item matched.</summary>
    AmbiguousItem,

    /// <summary>The account needs Bitwarden Premium for this, such as TOTP codes.</summary>
    PremiumRequired,
}
