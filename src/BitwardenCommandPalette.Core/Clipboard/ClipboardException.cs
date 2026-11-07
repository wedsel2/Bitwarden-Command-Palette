namespace BitwardenCommandPalette.Core.Clipboard;

/// <summary>
/// The clipboard could not be used, usually because another application holds it open. The
/// message never contains the value being copied.
/// </summary>
public sealed class ClipboardException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public ClipboardException()
        : base("The clipboard could not be used.")
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong, without any copied value.</param>
    public ClipboardException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong, without any copied value.</param>
    /// <param name="innerException">The cause.</param>
    public ClipboardException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
