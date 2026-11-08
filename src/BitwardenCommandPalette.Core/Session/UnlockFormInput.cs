using System.Text.Json;

namespace BitwardenCommandPalette.Core.Session;

/// <summary>
/// Reads what the unlock form submits: a JSON object whose <see cref="PasswordField"/> holds the
/// master password. The value goes straight into a <see cref="MasterPassword"/>; it is never
/// returned as a string or put in an exception.
/// </summary>
public static class UnlockFormInput
{
    /// <summary>The id of the password input in the unlock form.</summary>
    public const string PasswordField = "password";

    /// <summary>Takes the master password out of the form's submitted inputs.</summary>
    /// <param name="inputs">The JSON the form submitted.</param>
    /// <param name="password">The password, when the form held a non-empty one.</param>
    /// <returns>False for malformed input or an empty or missing password.</returns>
    public static bool TryReadPassword(string? inputs, out MasterPassword? password)
    {
        password = null;
        if (string.IsNullOrEmpty(inputs))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(inputs);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(PasswordField, out var field)
                || field.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var value = field.GetString();
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            password = new MasterPassword(value);
            return true;
        }
        catch (JsonException)
        {
            // Never rethrown: the exception text could quote the input.
            return false;
        }
    }
}
