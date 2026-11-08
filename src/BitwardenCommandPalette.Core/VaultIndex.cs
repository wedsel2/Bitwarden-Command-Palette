using System.Globalization;
using System.Text;
using BitwardenCommandPalette.Core.Bw;

namespace BitwardenCommandPalette.Core;

/// <summary>
/// The searchable list of logins (ADR 10): metadata only, in memory only, loaded at unlock and
/// dropped at lock. Search never starts bw. Names and usernames count as secret for logs and
/// disk, so nothing here logs, persists or prints them.
/// </summary>
/// <remarks>
/// Loading replaces the whole list at once, so a search running on another thread sees either
/// the old list or the new one, never a mix.
/// </remarks>
public sealed class VaultIndex
{
    /// <summary>The most results <see cref="Search"/> returns unless told otherwise.</summary>
    public const int DefaultLimit = 100;

    // Case, accents, kana type and width are ignored, so "cafe" finds "Café".
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

    // The field decides first (any name match beats any website match beats any username
    // match), then where in the field the word matched.
    private const int NameRank = 30;
    private const int HostRank = 20;
    private const int UsernameRank = 10;
    private const int Exact = 4;
    private const int Prefix = 3;
    private const int WordStart = 2;
    private const int Inside = 1;

    // Added when a query of several words matches the name as a whole phrase.
    private const int PhraseExact = 2 * NameRank;
    private const int PhrasePrefix = NameRank;

    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;

    private volatile Snapshot? snapshot;

    /// <summary>Whether a list is loaded; false while the vault is locked.</summary>
    public bool IsLoaded => snapshot is not null;

    /// <summary>How many logins are loaded.</summary>
    public int Count => snapshot?.Entries.Length ?? 0;

    /// <summary>Replaces the list with the given logins.</summary>
    /// <param name="logins">The logins from <see cref="IBwCli.ListLoginsAsync"/>.</param>
    public void Load(IEnumerable<BwLogin> logins)
    {
        ArgumentNullException.ThrowIfNull(logins);
        Entry[] entries = [.. logins.Select(login => new Entry(login))];
        var byId = new Dictionary<string, BwLogin>(entries.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            _ = byId.TryAdd(entry.Login.Id, entry.Login);
        }

        snapshot = new Snapshot(entries, byId);
    }

    /// <summary>Drops the list, at lock.</summary>
    public void Clear() => snapshot = null;

    /// <summary>Finds a login by its ID.</summary>
    /// <param name="id">The item's ID.</param>
    /// <returns>The login, or null if it is not in the list or nothing is loaded.</returns>
    public BwLogin? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return snapshot is { } current && current.ById.TryGetValue(id, out var login) ? login : null;
    }

    /// <summary>
    /// Searches names, websites and usernames. Every word of the query must match somewhere in
    /// a login; better matches come first: the name over the website over the username, and an
    /// exact match over the start of the field over the start of a word over the middle of one.
    /// An empty query lists every login by name.
    /// </summary>
    /// <param name="query">What the user typed.</param>
    /// <param name="limit">The most results to return.</param>
    /// <returns>The matching logins, best first; empty when nothing is loaded.</returns>
    public IReadOnlyList<BwLogin> Search(string? query, int limit = DefaultLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var current = snapshot;
        if (current is null)
        {
            return [];
        }

        // A word of only characters the comparison ignores (a lone accent, a zero-width joiner)
        // would match everything; it is dropped like whitespace.
        string[] terms = [.. (query ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(term => Compare.Compare(term, string.Empty, Loose) != 0)];
        var phrase = terms.Length > 1 ? string.Join(' ', terms) : null;
        return [.. current.Entries
            .Select(entry => (entry.Login, Score: Score(entry, terms, phrase)))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Login.Name, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(match => match.Login.Id, StringComparer.Ordinal)
            .Take(limit)
            .Select(match => match.Login)];
    }

    private static int Score(Entry entry, string[] terms, string? phrase)
    {
        if (terms.Length == 0)
        {
            return 1;
        }

        var total = 0;
        foreach (var term in terms)
        {
            var best = Position(entry.Login.Name, term) is var name and > 0 ? NameRank + name : 0;
            if (best == 0)
            {
                foreach (var host in entry.Hosts)
                {
                    best = Math.Max(best, Position(host, term) is var position and > 0 ? HostRank + position : 0);
                }
            }

            if (best == 0)
            {
                best = Position(entry.Login.Username, term) is var username and > 0 ? UsernameRank + username : 0;
            }

            if (best == 0)
            {
                return 0;
            }

            total += best;
        }

        if (phrase is not null)
        {
            total += Position(entry.Login.Name, phrase) switch
            {
                Exact => PhraseExact,
                Prefix => PhrasePrefix,
                _ => 0,
            };
        }

        return total;
    }

    // Where in the field the term matched: exact, prefix, word start, inside, or 0 for no match.
    private static int Position(string? field, string term)
    {
        if (string.IsNullOrEmpty(field))
        {
            return 0;
        }

        if (Compare.Compare(field, term, Loose) == 0)
        {
            return Exact;
        }

        var index = Compare.IndexOf(field, term, Loose);
        if (index < 0)
        {
            return 0;
        }

        if (index == 0)
        {
            return Prefix;
        }

        for (; index >= 0; index = index + 1 < field.Length ? Compare.IndexOf(field, term, index + 1, Loose) : -1)
        {
            if (index == 0 || IsWordBoundaryBefore(field, index))
            {
                return WordStart;
            }
        }

        return Inside;
    }

    // Looks at the character before the match, skipping combining marks (a decomposed "é" is
    // "e" plus a mark) and decoding surrogate pairs, so neither counts as a boundary by itself.
    private static bool IsWordBoundaryBefore(string field, int index)
    {
        var before = field.AsSpan(0, index);
        while (!before.IsEmpty && Rune.DecodeLastFromUtf16(before, out var rune, out var length) == System.Buffers.OperationStatus.Done)
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                before = before[..^length];
                continue;
            }

            return !Rune.IsLetterOrDigit(rune);
        }

        return true;
    }

    /// <summary>
    /// The host of a login URI without "www.", as search and the pages show it: never its path
    /// or query, which can hold tokens.
    /// </summary>
    /// <param name="uri">A URI as Bitwarden stores it, with or without a scheme.</param>
    /// <returns>The host, or an empty string when there is none.</returns>
    internal static string HostOf(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        // Bitwarden often stores a URI without a scheme ("example.com/reset?token=..."); it is
        // read as https so that only its host is kept.
        var host = HostIfAbsolute(uri) ?? HostIfAbsolute("https://" + uri) ?? uri.Split('/', '?', '#')[0];
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    /// <summary>The first website of a login, as <see cref="HostOf"/> shows it.</summary>
    /// <param name="login">The login.</param>
    /// <returns>The host of its first URI that has one, or null.</returns>
    public static string? FirstHost(BwLogin login)
    {
        ArgumentNullException.ThrowIfNull(login);
        return login.Uris.Select(HostOf).FirstOrDefault(host => host.Length > 0);
    }

    private static string? HostIfAbsolute(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Host.Length > 0 ? parsed.Host : null;

    /// <summary>One loaded list, replaced as a whole.</summary>
    private sealed record Snapshot(Entry[] Entries, Dictionary<string, BwLogin> ById);

    /// <summary>A login with its URIs reduced to what search looks at.</summary>
    private sealed class Entry(BwLogin login)
    {
        public BwLogin Login { get; } = login;

        public string[] Hosts { get; } = [.. login.Uris.Select(HostOf).Where(host => host.Length > 0)];
    }
}
