using BitwardenCommandPalette.Core;
using BitwardenCommandPalette.Core.Bw;
using BitwardenCommandPalette.Core.Session;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace BitwardenCommandPalette.Extension;

/// <summary>
/// The extension's main page. Locked, it offers the unlock form; unlocked, it searches the vault
/// index as the user types (in memory, no bw call) and lists each login with its copy actions.
/// </summary>
internal sealed partial class VaultPage : DynamicListPage, IDisposable
{
    private readonly VaultServices services;
    private readonly UnlockPage unlockPage;
    private readonly LockCommand lockCommand;

    public VaultPage(VaultServices services)
    {
        this.services = services;
        unlockPage = new UnlockPage(services.Session, () => RaiseItemsChanged());
        lockCommand = new LockCommand(services.Session);
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "Bitwarden";
        Name = "Open";
        PlaceholderText = "Search logins by name, website or username";
        services.Session.Locked += OnLocked;
    }

    public override void UpdateSearchText(string oldSearch, string newSearch) => RaiseItemsChanged();

    public override IListItem[] GetItems()
    {
        if (!services.Session.IsUnlocked)
        {
            return
            [
                new ListItem(unlockPage)
                {
                    Title = "Unlock vault",
                    Subtitle = "Enter your master password",
                    Icon = Icons.Lock,
                },
            ];
        }

        var logins = services.Session.Search(SearchText);
        if (logins.Count == 0)
        {
            return
            [
                // Enter does nothing here; locking stays one deliberate step away in the context menu.
                new ListItem(new NoOpCommand())
                {
                    Title = "No matching logins",
                    Icon = Icons.Key,
                    MoreCommands = [new CommandContextItem(lockCommand)],
                },
            ];
        }

        return [.. logins.Select(Item)];
    }

    public void Dispose() => services.Session.Locked -= OnLocked;

    // Enter opens the login's own page; the context menu keeps the copy actions one step closer.
    private ListItem Item(BwLogin login)
    {
        var more = new List<IContextItem>
        {
            new CommandContextItem(CopyFieldCommand.For(services, login, CopyField.Password, this)),
        };
        if (!string.IsNullOrEmpty(login.Username))
        {
            more.Add(new CommandContextItem(CopyFieldCommand.For(services, login, CopyField.Username, this)));
        }

        if (login.HasTotp)
        {
            more.Add(new CommandContextItem(CopyFieldCommand.For(services, login, CopyField.Totp, this)));
        }

        more.Add(new CommandContextItem(lockCommand));
        return new ListItem(new LoginPage(services, login, lockCommand))
        {
            Title = login.Name,
            Subtitle = Subtitle(login),
            Icon = Icons.Key,
            MoreCommands = [.. more],
        };
    }

    // "alice · example.com": the username and the first website, whichever the login has.
    private static string Subtitle(BwLogin login) =>
        string.Join(" · ", new[] { login.Username, VaultIndex.FirstHost(login) }.Where(part => !string.IsNullOrEmpty(part)));

    // Raised on whichever thread locked; Command Palette asks for the items again.
    private void OnLocked(object? sender, LockEventArgs e) => RaiseItemsChanged();
}
