using System.Text.Json;

namespace BitwardenCommandPalette.Core.Bw;

/// <summary>
/// Parses bw's JSON. Failures become <see cref="BwErrorKind.UnexpectedOutput"/> without the
/// parser's own exception, so no fragment of the output can reach a message (ADR 13).
/// </summary>
internal static class BwOutput
{
    private const int LoginType = 1;

    /// <summary>Parses the output of <c>bw status</c>.</summary>
    /// <param name="json">bw's standard output.</param>
    /// <returns>The status, without the account email.</returns>
    public static BwStatus ParseStatus(string json) => Parse(json, root =>
    {
        var state = root.GetProperty("status").GetString() switch
        {
            "unauthenticated" => BwVaultState.Unauthenticated,
            "locked" => BwVaultState.Locked,
            "unlocked" => BwVaultState.Unlocked,
            _ => throw new BwCliException(BwErrorKind.UnexpectedOutput),
        };
        DateTimeOffset? lastSync = root.TryGetProperty("lastSync", out var sync) && sync.ValueKind == JsonValueKind.String ? sync.GetDateTimeOffset() : null;
        var server = OptionalString(root, "serverUrl") is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
        return new BwStatus(state, lastSync, server);
    });

    /// <summary>
    /// Parses the output of <c>bw list items</c>, keeping only login metadata. Passwords and
    /// TOTP secrets are read from the UTF-8 document only to check that a secret exists; they
    /// never become strings of their own.
    /// </summary>
    /// <param name="json">bw's standard output.</param>
    /// <returns>The logins; cards, notes and identities are left out.</returns>
    public static IReadOnlyList<BwLogin> ParseLogins(string json) => Parse(json, root =>
    {
        var logins = new List<BwLogin>();
        foreach (var item in root.EnumerateArray())
        {
            if (item.GetProperty("type").GetInt32() != LoginType)
            {
                continue;
            }

            var login = item.TryGetProperty("login", out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
            var hasLogin = login.ValueKind == JsonValueKind.Object;
            logins.Add(new BwLogin(
                Id: item.GetProperty("id").GetString() ?? throw new BwCliException(BwErrorKind.UnexpectedOutput),
                Name: item.GetProperty("name").GetString() ?? throw new BwCliException(BwErrorKind.UnexpectedOutput),
                Username: hasLogin ? OptionalString(login, "username") : null,
                Uris: hasLogin ? ParseUris(login) : [],
                FolderId: OptionalString(item, "folderId"),
                HasTotp: hasLogin && IsNonEmptyString(login, "totp"),
                RequiresReprompt: item.TryGetProperty("reprompt", out var reprompt) && reprompt.ValueKind == JsonValueKind.Number && reprompt.GetInt32() != 0));
        }

        return logins;
    });

    private static T Parse<T>(string json, Func<JsonElement, T> read)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return read(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new BwCliException(BwErrorKind.UnexpectedOutput);
        }
    }

    private static List<string> ParseUris(JsonElement login)
    {
        var uris = new List<string>();
        if (login.TryGetProperty("uris", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in array.EnumerateArray())
            {
                if (OptionalString(entry, "uri") is { } uri)
                {
                    uris.Add(uri);
                }
            }
        }

        return uris;
    }

    // A missing, null or empty string counts as absent; bw writes "" for "no folder". An entry
    // that is not an object (an odd URI entry, say) also counts as absent instead of failing
    // the whole list.
    private static string? OptionalString(JsonElement element, string name) =>
        IsNonEmptyString(element, name) ? element.GetProperty(name).GetString() : null;

    private static bool IsNonEmptyString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.ValueEquals(ReadOnlySpan<byte>.Empty) is false;
}
