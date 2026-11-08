using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace BitwardenCommandPalette.Extension;

/// <summary>The top-level commands the extension adds to Command Palette.</summary>
internal sealed partial class BitwardenCommandsProvider : CommandProvider
{
    private readonly VaultServices services = new();
    private readonly VaultPage vaultPage;
    private readonly ICommandItem[] commands;

    public BitwardenCommandsProvider()
    {
        DisplayName = "Bitwarden";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        vaultPage = new VaultPage(services);
        commands =
        [
            new CommandItem(vaultPage) { Title = "Bitwarden", Subtitle = "Search your vault" },
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
