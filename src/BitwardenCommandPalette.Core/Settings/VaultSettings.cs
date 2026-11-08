using System.Globalization;
using BitwardenCommandPalette.Core.Clipboard;
using BitwardenCommandPalette.Core.Session;

namespace BitwardenCommandPalette.Core.Settings;

/// <summary>One value a choice setting offers: what the settings file stores, and what the user sees.</summary>
/// <param name="Value">The stored value: whole minutes or seconds, as text.</param>
/// <param name="Title">The label shown in the settings page.</param>
public sealed record SettingChoice(string Value, string Title);

/// <summary>
/// The values the settings page offers and how a stored value is read back. The settings file
/// holds no secret (ADR 6, item 2); a value that is missing, unknown or out of range falls back
/// to the default, so a hand-edited file can never move a timeout past Core's limits.
/// </summary>
public static class VaultSettings
{
    /// <summary>The idle timeouts offered, in minutes.</summary>
    public static IReadOnlyList<SettingChoice> IdleTimeoutChoices { get; } = Minutes(1, 2, 5, 10, 15);

    /// <summary>The absolute timeouts offered, in minutes.</summary>
    public static IReadOnlyList<SettingChoice> AbsoluteTimeoutChoices { get; } = Minutes(15, 30, 60, 120, 240);

    /// <summary>The clipboard clear delays offered, in seconds.</summary>
    public static IReadOnlyList<SettingChoice> ClearDelayChoices { get; } = Seconds(10, 20, 30, 60, 120);

    /// <summary>The sync intervals offered, in minutes; zero syncs at every unlock.</summary>
    public static IReadOnlyList<SettingChoice> SyncIntervalChoices { get; } =
        [new("0", "At every unlock"), .. Minutes(15, 30, 60, 240)];

    /// <summary>The stored value of a duration in whole minutes.</summary>
    /// <param name="value">The duration.</param>
    /// <returns>The value as the settings file stores it.</returns>
    public static string ToMinutes(TimeSpan value) => ((int)value.TotalMinutes).ToString(CultureInfo.InvariantCulture);

    /// <summary>The stored value of a duration in whole seconds.</summary>
    /// <param name="value">The duration.</param>
    /// <returns>The value as the settings file stores it.</returns>
    public static string ToSeconds(TimeSpan value) => ((int)value.TotalSeconds).ToString(CultureInfo.InvariantCulture);

    /// <summary>The idle timeout a stored value means, within Core's limits.</summary>
    /// <param name="stored">The stored value.</param>
    /// <returns>The idle timeout.</returns>
    public static TimeSpan IdleTimeout(string? stored) =>
        Read(stored, TimeSpan.FromMinutes, SessionManager.DefaultIdleTimeout, SessionManager.MinimumIdleTimeout, SessionManager.MaximumIdleTimeout);

    /// <summary>The absolute timeout a stored value means, within Core's limits.</summary>
    /// <param name="stored">The stored value.</param>
    /// <returns>The absolute timeout.</returns>
    public static TimeSpan AbsoluteTimeout(string? stored) =>
        Read(stored, TimeSpan.FromMinutes, SessionManager.DefaultAbsoluteTimeout, SessionManager.MinimumAbsoluteTimeout, SessionManager.MaximumAbsoluteTimeout);

    /// <summary>The clipboard clear delay a stored value means, within Core's limits.</summary>
    /// <param name="stored">The stored value.</param>
    /// <returns>The clear delay.</returns>
    public static TimeSpan ClearDelay(string? stored) =>
        Read(stored, TimeSpan.FromSeconds, ClipboardService.DefaultClearDelay, ClipboardService.MinimumClearDelay, ClipboardService.MaximumClearDelay);

    /// <summary>The sync interval a stored value means, at most a day.</summary>
    /// <param name="stored">The stored value.</param>
    /// <returns>The sync interval.</returns>
    public static TimeSpan SyncInterval(string? stored) =>
        Read(stored, TimeSpan.FromMinutes, SessionManager.DefaultSyncInterval, TimeSpan.Zero, TimeSpan.FromDays(1));

    /// <summary>The bw path a stored value means: trimmed, without quotes, or null to search for bw.</summary>
    /// <param name="stored">The stored value.</param>
    /// <returns>The path, or null.</returns>
    public static string? BwPath(string? stored)
    {
        var path = stored?.Trim().Trim('"').Trim();
        return string.IsNullOrEmpty(path) ? null : path;
    }

    private static TimeSpan Read(string? stored, Func<double, TimeSpan> unit, TimeSpan fallback, TimeSpan minimum, TimeSpan maximum)
    {
        if (!int.TryParse(stored, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return fallback;
        }

        var value = unit(number);
        return value < minimum || value > maximum ? fallback : value;
    }

    private static SettingChoice[] Minutes(params int[] values) =>
        [.. values.Select(value => new SettingChoice(
            value.ToString(CultureInfo.InvariantCulture),
            value switch
            {
                1 => "1 minute",
                < 60 => string.Create(CultureInfo.InvariantCulture, $"{value} minutes"),
                60 => "1 hour",
                _ => string.Create(CultureInfo.InvariantCulture, $"{value / 60} hours"),
            }))];

    private static SettingChoice[] Seconds(params int[] values) =>
        [.. values.Select(value => new SettingChoice(
            value.ToString(CultureInfo.InvariantCulture),
            value < 60
                ? string.Create(CultureInfo.InvariantCulture, $"{value} seconds")
                : value == 60 ? "1 minute" : string.Create(CultureInfo.InvariantCulture, $"{value / 60} minutes")))];
}
