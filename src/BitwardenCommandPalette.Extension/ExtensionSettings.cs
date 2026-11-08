using BitwardenCommandPalette.Core.Settings;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace BitwardenCommandPalette.Extension;

/// <summary>
/// The settings page and its file. Nothing in it is a secret (ADR 6, item 2): a path, durations
/// and one switch. Core reads every stored value back within its own limits (<see cref="VaultSettings"/>).
/// </summary>
internal sealed partial class ExtensionSettings : JsonSettingsManager
{
    private readonly TextSetting bwPath = new(
        "bwPath",
        "Path to bw.exe",
        "Leave empty to find bw in the usual install locations and on PATH. It must be signed by Bitwarden Inc.",
        string.Empty)
    {
        Placeholder = @"C:\path\to\bw.exe",
    };

    private readonly ChoiceSetSetting idleTimeout = Choices(
        "idleTimeoutMinutes",
        "Lock after inactivity",
        "Lock the vault when it has not been searched or copied from for this long.",
        VaultSettings.IdleTimeoutChoices);

    private readonly ChoiceSetSetting absoluteTimeout = Choices(
        "absoluteTimeoutMinutes",
        "Always lock after",
        "Lock the vault this long after unlocking, however much it is used.",
        VaultSettings.AbsoluteTimeoutChoices);

    private readonly ChoiceSetSetting clearDelay = Choices(
        "clipboardClearSeconds",
        "Clear the clipboard after",
        "A copied value is cleared after this long, if it is still on the clipboard.",
        VaultSettings.ClearDelayChoices);

    private readonly ChoiceSetSetting syncInterval = Choices(
        "syncIntervalMinutes",
        "Sync at unlock when older than",
        "Run bw sync when unlocking if the last sync is older than this. 'Sync vault' syncs any time.",
        VaultSettings.SyncIntervalChoices);

    private readonly ToggleSetting showIcons = new(
        "showWebsiteIcons",
        "Show website icons",
        "Fetch each login's website icon from your Bitwarden server's icon service, as Bitwarden's apps do. Bitwarden then sees which websites you have logins for, but not your usernames or passwords. Icons are kept in memory only.",
        true);

    public ExtensionSettings()
    {
        FilePath = SettingsFilePath();
        idleTimeout.Value = VaultSettings.ToMinutes(Core.Session.SessionManager.DefaultIdleTimeout);
        absoluteTimeout.Value = VaultSettings.ToMinutes(Core.Session.SessionManager.DefaultAbsoluteTimeout);
        clearDelay.Value = VaultSettings.ToSeconds(Core.Clipboard.ClipboardService.DefaultClearDelay);
        syncInterval.Value = VaultSettings.ToMinutes(Core.Session.SessionManager.DefaultSyncInterval);

        Settings.Add(bwPath);
        Settings.Add(idleTimeout);
        Settings.Add(absoluteTimeout);
        Settings.Add(clearDelay);
        Settings.Add(syncInterval);
        Settings.Add(showIcons);

        LoadSettings();
        Settings.SettingsChanged += (_, _) =>
        {
            SaveSettings();
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Raised after the user saves the settings page.</summary>
    public event EventHandler? Changed;

    public string? BwPath => VaultSettings.BwPath(bwPath.Value);

    public TimeSpan IdleTimeout => VaultSettings.IdleTimeout(idleTimeout.Value);

    public TimeSpan AbsoluteTimeout => VaultSettings.AbsoluteTimeout(absoluteTimeout.Value);

    public TimeSpan ClearDelay => VaultSettings.ClearDelay(clearDelay.Value);

    public TimeSpan SyncInterval => VaultSettings.SyncInterval(syncInterval.Value);

    public bool ShowWebsiteIcons => showIcons.Value;

    private static ChoiceSetSetting Choices(string key, string label, string description, IReadOnlyList<SettingChoice> choices) =>
        new(key, label, description, [.. choices.Select(choice => new ChoiceSetSetting.Choice(choice.Title, choice.Value))]);

    // The toolkit's per-extension folder; a packaged app's writes there stay in its own package data.
    private static string SettingsFilePath()
    {
        var directory = Utilities.BaseSettingsPath("BitwardenCommandPalette");
        _ = Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }
}
