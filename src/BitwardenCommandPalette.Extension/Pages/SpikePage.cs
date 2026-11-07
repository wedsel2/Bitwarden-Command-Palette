using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace BitwardenCommandPalette.Extension;

/// <summary>
/// Phase 2 placeholder: proves the extension loads and shows the spike log
/// (process start, Windows session and power events). Replaced by the vault
/// pages in phase 4.
/// </summary>
internal sealed partial class SpikePage : ListPage
{
    public SpikePage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "Bitwarden";
        Name = "Open";
    }

    public override IListItem[] GetItems()
    {
        // Logged off the render path so opening the page never waits on disk I/O.
        _ = Task.Run(() => SpikeLog.Write("page opened"));
        return
        [
            new ListItem(new NoOpCommand())
            {
                Title = "The extension is running",
                Subtitle = string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Process {Environment.ProcessId}. Newest log entries below."),
            },
            .. SpikeLog.Recent.Select(entry => new ListItem(new NoOpCommand()) { Title = entry }),
        ];
    }
}
