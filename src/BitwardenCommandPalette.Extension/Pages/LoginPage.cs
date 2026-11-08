using BitwardenCommandPalette.Core;
using BitwardenCommandPalette.Core.Bw;
using BitwardenCommandPalette.Core.Session;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace BitwardenCommandPalette.Extension;

/// <summary>
/// One login's actions, opened with Enter from the vault page: copy the password, the username
/// or the TOTP code, or lock the vault. It shows only metadata; every value is fetched when its
/// action runs (ADR 10).
/// </summary>
internal sealed partial class LoginPage : ListPage
{
    private readonly IListItem[] items;

    public LoginPage(VaultServices services, BwLogin login, LockCommand lockCommand)
    {
        Icon = Icons.Key;
        Title = login.Name;
        Name = "Open";

        var list = new List<IListItem>
        {
            new ListItem(new CopyFieldCommand(services, login.Id, CopyField.Password)) { Title = "Copy password" },
        };

        if (!string.IsNullOrEmpty(login.Username))
        {
            list.Add(new ListItem(new CopyFieldCommand(services, login.Id, CopyField.Username))
            {
                Title = "Copy username",
                Subtitle = login.Username,
            });
        }

        if (login.HasTotp)
        {
            list.Add(new ListItem(new CopyFieldCommand(services, login.Id, CopyField.Totp)) { Title = "Copy TOTP code" });
        }

        if (VaultIndex.FirstHost(login) is { } host)
        {
            // Shown for recognition only: opening a website is a later decision (roadmap phase 6).
            list.Add(new ListItem(new NoOpCommand()) { Title = host, Subtitle = "Website", Icon = Icons.Globe });
        }

        list.Add(new ListItem(lockCommand) { Title = "Lock vault" });
        items = [.. list];
    }

    public override IListItem[] GetItems() => items;
}
