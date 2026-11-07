namespace BitwardenCommandPalette.Core;

/// <summary>
/// The text that stands in for a secret wherever one would otherwise be printed:
/// logs, exception messages and debugger displays.
/// </summary>
public static class Redacted
{
    /// <summary>The placeholder shown instead of a secret value.</summary>
    public const string Placeholder = "***";

    /// <summary>
    /// Returns <see cref="Placeholder"/> for any value, so a caller can never
    /// accidentally pass the value itself through.
    /// </summary>
    /// <typeparam name="T">The type of the value being hidden.</typeparam>
    /// <param name="value">The value to hide. It is not read.</param>
    /// <returns>Always <see cref="Placeholder"/>.</returns>
    public static string Hide<T>(T value)
    {
        _ = value;
        return Placeholder;
    }
}
