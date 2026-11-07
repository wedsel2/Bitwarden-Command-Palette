using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BitwardenCommandPalette.Core;

/// <summary>
/// Base of every secret type (ADR 13). The value cannot be printed: <see cref="ToString"/>,
/// the debugger and JSON serialisation all show <see cref="Redacted.Placeholder"/>. Each
/// derived type exposes the raw value only through a method whose name says where it goes.
/// </summary>
/// <remarks>
/// .NET strings cannot be wiped from memory; this type limits where the value can travel,
/// it does not claim erasure (ADR 6).
/// </remarks>
[DebuggerDisplay(Redacted.Placeholder)]
public abstract class SecretValue
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly string value;

    /// <summary>Wraps a secret value.</summary>
    /// <param name="value">The secret. It must not be null or empty.</param>
    private protected SecretValue(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        this.value = value;
    }

    /// <summary>Always <see cref="Redacted.Placeholder"/>, never the value.</summary>
    /// <returns><see cref="Redacted.Placeholder"/>.</returns>
    public sealed override string ToString() => Redacted.Placeholder;

    /// <summary>The raw value, for the derived type's named reveal method only.</summary>
    /// <returns>The secret.</returns>
    private protected string Reveal() => value;
}

/// <summary>
/// Writes a secret as <see cref="Redacted.Placeholder"/> and refuses to read one, so a secret
/// can never be serialised into a log, a settings file or a crash report.
/// </summary>
/// <typeparam name="T">The secret type.</typeparam>
internal sealed class RedactingJsonConverter<T> : JsonConverter<T>
    where T : SecretValue
{
    /// <inheritdoc />
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Secrets are never read from JSON.");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(Redacted.Placeholder);
    }
}
