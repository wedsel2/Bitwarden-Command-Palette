using System.Diagnostics;
using System.Text.Json.Serialization;

namespace BitwardenCommandPalette.Core;

/// <summary>
/// The master password the user typed into the unlock form. It goes to <c>bw unlock</c>
/// through an environment variable of the child process only (ADR 7) and is dropped after.
/// </summary>
[DebuggerDisplay(Redacted.Placeholder)]
[JsonConverter(typeof(RedactingJsonConverter<MasterPassword>))]
public sealed class MasterPassword : SecretValue
{
    /// <summary>Wraps the master password.</summary>
    /// <param name="value">The password as typed. It must not be null or empty.</param>
    public MasterPassword(string value)
        : base(value)
    {
    }

    /// <summary>The raw password, for the <c>bw unlock</c> child's environment only.</summary>
    /// <returns>The password.</returns>
    internal string RevealForChildEnvironment() => Reveal();
}

/// <summary>
/// The key <c>bw unlock</c> returns. It lives in memory only (ADR 9) and reaches every other
/// <c>bw</c> command as <c>BW_SESSION</c> on the child's environment.
/// </summary>
[DebuggerDisplay(Redacted.Placeholder)]
[JsonConverter(typeof(RedactingJsonConverter<SessionKey>))]
public sealed class SessionKey : SecretValue
{
    /// <summary>Wraps a session key returned by <c>bw unlock</c>.</summary>
    /// <param name="value">The key. It must not be null or empty.</param>
    internal SessionKey(string value)
        : base(value)
    {
    }

    /// <summary>The raw key, for a <c>bw</c> child's environment only.</summary>
    /// <returns>The key.</returns>
    internal string RevealForChildEnvironment() => Reveal();
}

/// <summary>
/// A password or TOTP code fetched for one copy action (ADR 10), on its way to the clipboard.
/// </summary>
[DebuggerDisplay(Redacted.Placeholder)]
[JsonConverter(typeof(RedactingJsonConverter<Secret>))]
public sealed class Secret : SecretValue
{
    /// <summary>Wraps a value fetched from <c>bw</c>.</summary>
    /// <param name="value">The value. It must not be null or empty.</param>
    internal Secret(string value)
        : base(value)
    {
    }

    /// <summary>The raw value, for <c>ClipboardService</c> only.</summary>
    /// <returns>The value.</returns>
    internal string RevealForClipboard() => Reveal();
}
