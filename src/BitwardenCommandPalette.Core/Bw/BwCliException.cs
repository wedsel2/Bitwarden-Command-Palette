namespace BitwardenCommandPalette.Core.Bw;

/// <summary>
/// A <c>bw</c> call failed. The message is always one of our own fixed texts: never bw's
/// output, which could hold vault data (ADR 13). No inner exception carries bw output either.
/// </summary>
public sealed class BwCliException : Exception
{
    /// <summary>Creates an exception for a general failure.</summary>
    public BwCliException()
        : this(BwErrorKind.Failed)
    {
    }

    /// <summary>Creates an exception with a message of the caller's own.</summary>
    /// <param name="message">A message that holds no secret and no bw output.</param>
    public BwCliException(string message)
        : base(message)
    {
        Kind = BwErrorKind.Failed;
    }

    /// <summary>Creates an exception with a message of the caller's own and its cause.</summary>
    /// <param name="message">A message that holds no secret and no bw output.</param>
    /// <param name="innerException">The cause, which must not hold bw output.</param>
    public BwCliException(string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = BwErrorKind.Failed;
    }

    /// <summary>Creates an exception of the given kind with its fixed message.</summary>
    /// <param name="kind">Why the call failed.</param>
    public BwCliException(BwErrorKind kind)
        : base(MessageFor(kind))
    {
        Kind = kind;
    }

    /// <summary>Creates an exception of the given kind with its fixed message and cause.</summary>
    /// <param name="kind">Why the call failed.</param>
    /// <param name="innerException">The cause, which must not hold bw output.</param>
    public BwCliException(BwErrorKind kind, Exception innerException)
        : base(MessageFor(kind), innerException)
    {
        Kind = kind;
    }

    /// <summary>Why the call failed.</summary>
    public BwErrorKind Kind { get; }

    private static string MessageFor(BwErrorKind kind) => kind switch
    {
        BwErrorKind.CouldNotStart => "The Bitwarden CLI could not be started.",
        BwErrorKind.UntrustedExecutable => "bw.exe is missing or not signed by Bitwarden Inc., so it was not started.",
        BwErrorKind.TimedOut => "The Bitwarden CLI did not respond in time.",
        BwErrorKind.UnexpectedOutput => "The Bitwarden CLI returned output that could not be read.",
        BwErrorKind.NotLoggedIn => "The Bitwarden CLI is not logged in. Run 'bw login' once in a terminal.",
        BwErrorKind.VaultLocked => "The vault is locked.",
        BwErrorKind.InvalidPassword => "The master password is incorrect.",
        BwErrorKind.NotFound => "The item, or this field of it, was not found.",
        BwErrorKind.AmbiguousItem => "More than one item matched.",
        BwErrorKind.PremiumRequired => "This needs Bitwarden Premium.",
        _ => "The Bitwarden CLI reported an error.",
    };
}
