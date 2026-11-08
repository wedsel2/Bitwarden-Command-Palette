using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace BitwardenCommandPalette.Extension;

/// <summary>
/// Shows that the extension is installed and running until the vault pages replace it in
/// phase 4 (unlock form, search list, per-login actions).
/// </summary>
internal sealed partial class PlaceholderPage : ListPage
{
    public PlaceholderPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "Bitwarden";
        Name = "Open";
    }

    public override IListItem[] GetItems() =>
    [
        new ListItem(new NoOpCommand())
        {
            Title = "The extension is running",
            Subtitle = "Unlocking and searching the vault arrive in the next version.",
        },
    ];
}
