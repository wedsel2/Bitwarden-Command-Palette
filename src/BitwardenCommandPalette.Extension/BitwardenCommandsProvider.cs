using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace BitwardenCommandPalette.Extension;

/// <summary>The top-level commands the extension adds to Command Palette.</summary>
internal sealed partial class BitwardenCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;

    public BitwardenCommandsProvider()
    {
        DisplayName = "Bitwarden";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        _commands =
        [
            new CommandItem(new PlaceholderPage()) { Title = "Bitwarden", Subtitle = "Search your vault" },
        ];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;
}
