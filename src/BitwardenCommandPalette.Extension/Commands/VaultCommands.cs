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
                CopyOutcome.Copied => Toasts.Show(Copied(), CommandResult.Dismiss()),
                CopyOutcome.Locked => Toasts.Show("The vault is locked. Open Bitwarden and unlock it.", CommandResult.KeepOpen()),
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

    private string Copied()
    {
        var what = field switch
        {
            CopyField.Username => "Username",
            CopyField.Totp => "TOTP code",
            _ => "Password",
        };
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{what} copied. It is cleared from the clipboard in {services.ClearDelay.TotalSeconds:0} seconds.");
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

/// <summary>Shows a message, then does what <c>then</c> says.</summary>
internal static class Toasts
{
    public static CommandResult Show(string message, CommandResult then) =>
        CommandResult.ShowToast(new ToastArgs { Message = message, Result = then });
}

/// <summary>Segoe Fluent Icons glyphs used by the pages.</summary>
internal static class Icons
{
    public static IconInfo Lock { get; } = new("");

    public static IconInfo Key { get; } = new("");

    public static IconInfo Person { get; } = new("");

    public static IconInfo Clock { get; } = new("");

    public static IconInfo Globe { get; } = new("");
}
