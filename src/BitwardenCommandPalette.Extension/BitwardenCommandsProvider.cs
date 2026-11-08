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
        Icon = Icons.Shield;
        Settings = settings.Settings;
        vaultPage = new VaultPage(services);

        // Every top-level command carries the extension's icon, so they read as one group in the results.
        commands =
        [
            new CommandItem(vaultPage) { Title = "Bitwarden", Subtitle = "Search your vault", Icon = Icons.Shield },
            new CommandItem(new SyncCommand(services.Session, vaultPage.Refresh)) { Title = "Sync Bitwarden vault", Subtitle = "Fetch changes made elsewhere", Icon = Icons.Shield },
            new CommandItem(new LockCommand(services.Session)) { Title = "Lock Bitwarden vault", Subtitle = "Lock now and clear a copied value", Icon = Icons.Shield },
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
