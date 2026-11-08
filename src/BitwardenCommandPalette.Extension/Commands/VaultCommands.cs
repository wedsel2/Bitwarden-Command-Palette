using System.Globalization;
using BitwardenCommandPalette.Core.Bw;
using BitwardenCommandPalette.Core.Clipboard;
using BitwardenCommandPalette.Core.Session;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace BitwardenCommandPalette.Extension;

/// <summary>
/// Copies one field of a login. The value is fetched and copied inside Core; this command only
/// ever sees the outcome (ADR 10, ADR 11).
/// </summary>
internal sealed partial class CopyFieldCommand : InvokableCommand
{
    private readonly VaultServices services;
    private readonly string itemId;
    private readonly CopyField field;
    private readonly Page owner;

    // `owner` is the page the action runs from; it shows its loading indicator while bw works.
    public CopyFieldCommand(VaultServices services, string itemId, CopyField field, Page owner)
    {
        this.services = services;
        this.owner = owner;
        this.itemId = itemId;
        this.field = field;
        (Name, Icon) = field switch
        {
            CopyField.Username => ("Copy username", Icons.Person),
            CopyField.Totp => ("Copy TOTP code", Icons.Clock),
            _ => ("Copy password", Icons.Key),
        };
    }

    public override ICommandResult Invoke()
    {
        owner.IsLoading = true;
        try
        {
            // Command Palette calls this off its UI thread; bw takes a few seconds.
            var outcome = services.Session.CopyAsync(itemId, field).GetAwaiter().GetResult();
            return outcome switch
            {
                CopyOutcome.Copied => Toasts.Show(CopyMessages.Copied(field, services.ClearDelay), CommandResult.Dismiss()),
                CopyOutcome.Locked => Toasts.Show("The vault is locked. Open Bitwarden and unlock it.", CommandResult.KeepOpen()),
                CopyOutcome.RepromptRequired => Toasts.Show("This login asks for your master password. Open it to copy.", CommandResult.KeepOpen()),
                _ => Toasts.Show("This login has no such field.", CommandResult.KeepOpen()),
            };
        }
        catch (BwCliException exception)
        {
            return Toasts.Show(exception.Message, CommandResult.KeepOpen());
        }
        catch (ClipboardException exception)
        {
            return Toasts.Show(exception.Message, CommandResult.KeepOpen());
        }
        finally
        {
            owner.IsLoading = false;
        }
    }

    /// <summary>
    /// The action for one field of a login: the copy itself, or for the password or TOTP code of a
    /// login marked for reprompt, the page that asks for the master password first (ADR 18).
    /// </summary>
    public static ICommand For(VaultServices services, BwLogin login, CopyField field, Page owner) =>
        login.RequiresReprompt && field != CopyField.Username
            ? new RepromptPage(services, login, field)
            : new CopyFieldCommand(services, login.Id, field, owner);
}

/// <summary>The texts the copy actions show.</summary>
internal static class CopyMessages
{
    public static string Copied(CopyField field, TimeSpan clearDelay)
    {
        var what = field switch
        {
            CopyField.Username => "Username",
            CopyField.Totp => "TOTP code",
            _ => "Password",
        };
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{what} copied. It is cleared from the clipboard in {clearDelay.TotalSeconds:0} seconds.");
    }
}

/// <summary>Locks the vault now: drops the key, the index and our clipboard value, then runs <c>bw lock</c>.</summary>
internal sealed partial class LockCommand : InvokableCommand
{
    private readonly SessionManager session;

    public LockCommand(SessionManager session)
    {
        this.session = session;
        Name = "Lock vault";
        Icon = Icons.Lock;
    }

    public override ICommandResult Invoke()
    {
        session.LockAsync(LockReason.Command).GetAwaiter().GetResult();
        return Toasts.Show("Bitwarden vault locked.", CommandResult.KeepOpen());
    }
}

/// <summary>Runs <c>bw sync</c> and reloads the list, so a change made elsewhere shows up.</summary>
internal sealed partial class SyncCommand : InvokableCommand
{
    private readonly SessionManager session;
    private readonly Action onSynced;
    private readonly Page? owner;

    // `owner`, when given, shows its loading indicator while bw works.
    public SyncCommand(SessionManager session, Action onSynced, Page? owner = null)
    {
        this.session = session;
        this.onSynced = onSynced;
        this.owner = owner;
        Name = "Sync vault";
        Icon = Icons.Sync;
    }

    public override ICommandResult Invoke()
    {
        if (owner is not null)
        {
            owner.IsLoading = true;
        }

        try
        {
            // Command Palette calls this off its UI thread; bw sync takes a few seconds.
            var outcome = session.SyncAsync().GetAwaiter().GetResult();
            if (outcome == SyncOutcome.Synced)
            {
                onSynced();
                return Toasts.Show("Vault synced.", CommandResult.KeepOpen());
            }

            return Toasts.Show("The vault is locked. Open Bitwarden and unlock it.", CommandResult.KeepOpen());
        }
        catch (BwCliException exception)
        {
            return Toasts.Show(exception.Message, CommandResult.KeepOpen());
        }
        finally
        {
            if (owner is not null)
            {
                owner.IsLoading = false;
            }
        }
    }
}

/// <summary>Shows a message, then does what <c>then</c> says.</summary>
internal static class Toasts
{
    public static CommandResult Show(string message, CommandResult then) =>
        CommandResult.ShowToast(new ToastArgs { Message = message, Result = then });
}

/// <summary>Segoe Fluent Icons glyphs used by the pages.</summary>
internal static class Icons
{
    /// <summary>The extension's own icon, on its top-level commands: a glyph, not Bitwarden's logo.</summary>
    public static IconInfo Shield { get; } = new("");

    public static IconInfo Lock { get; } = new("");

    public static IconInfo Key { get; } = new("");

    public static IconInfo Person { get; } = new("");

    public static IconInfo Clock { get; } = new("");

    public static IconInfo Globe { get; } = new("");

    public static IconInfo Sync { get; } = new("");
}
