using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace BitwardenCommandPalette.Extension;

/// <summary>The top-level commands the extension adds to Command Palette, and its settings page.</summary>
internal sealed partial class BitwardenCommandsProvider : CommandProvider
{
    private readonly VaultServices services;
    private readonly VaultPage vaultPage;
    private readonly ICommandItem[] commands;

    public BitwardenCommandsProvider()
    {
        var settings = new ExtensionSettings();
        services = new VaultServices(settings);
        DisplayName = "Bitwarden";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Settings = settings.Settings;
        vaultPage = new VaultPage(services);
        commands =
        [
            new CommandItem(vaultPage) { Title = "Bitwarden", Subtitle = "Search your vault" },
            new CommandItem(new SyncCommand(services.Session, vaultPage.Refresh)) { Title = "Sync Bitwarden vault", Subtitle = "Fetch changes made elsewhere" },
            new CommandItem(new LockCommand(services.Session)) { Title = "Lock Bitwarden vault", Subtitle = "Lock now and clear a copied value" },
        ];
    }

    public override ICommandItem[] TopLevelCommands() => commands;

    /// <summary>Locks the vault (ADR 9: the extension shutting down locks) and stops Core's services.</summary>
    public override void Dispose()
    {
        vaultPage.Dispose();
        services.Dispose();
        base.Dispose();
    }
}
