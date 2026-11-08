using BitwardenCommandPalette.Core;
using BitwardenCommandPalette.Core.Bw;
using BitwardenCommandPalette.Core.Clipboard;
using BitwardenCommandPalette.Core.Session;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace BitwardenCommandPalette.Extension;

/// <summary>The unlock form: one password field (ADR 12: the extension unlocks, never logs in).</summary>
internal sealed partial class UnlockPage : ContentPage
{
    private readonly MasterPasswordForm form;
    private readonly SessionManager session;
    private readonly Action onUnlocked;

    public UnlockPage(SessionManager session, Action onUnlocked)
    {
        this.session = session;
        this.onUnlocked = onUnlocked;
        Icon = Icons.Lock;
        Title = "Unlock Bitwarden";
        Name = "Unlock";
        form = new MasterPasswordForm("Enter your master password to unlock the vault.", "Unlock", Unlock, loading => IsLoading = loading);
    }

    public override IContent[] GetContent() => [form];

    private CommandResult Unlock(MasterPassword password)
    {
        var outcome = session.UnlockAsync(password).GetAwaiter().GetResult();
        switch (outcome)
        {
            case UnlockOutcome.Unlocked:
                // The vault page lists the logins now; it must ask for its items again.
                onUnlocked();
                return Toasts.Show("Vault unlocked.", CommandResult.GoBack());
            case UnlockOutcome.WrongPassword:
                return Toasts.Show("The master password is incorrect.", CommandResult.KeepOpen());
            case UnlockOutcome.NotLoggedIn:
                return Toasts.Show("The Bitwarden CLI is not logged in. Run 'bw login' once in a terminal.", CommandResult.KeepOpen());
            default:
                return Toasts.Show("The vault was locked again before it finished loading. Try again.", CommandResult.KeepOpen());
        }
    }
}

/// <summary>
/// Asks for the master password again before copying the password or TOTP code of a login marked
/// for reprompt (ADR 18). Core checks it with <c>bw unlock</c>, every time.
/// </summary>
internal sealed partial class RepromptPage : ContentPage
{
    private readonly MasterPasswordForm form;
    private readonly VaultServices services;
    private readonly string itemId;
    private readonly CopyField field;

    public RepromptPage(VaultServices services, BwLogin login, CopyField field)
    {
        this.services = services;
        itemId = login.Id;
        this.field = field;
        Icon = field == CopyField.Totp ? Icons.Clock : Icons.Key;
        Title = login.Name;
        Name = field == CopyField.Totp ? "Copy TOTP code" : "Copy password";
        form = new MasterPasswordForm(
            "This login asks for your master password before its password or TOTP code is copied.",
            Name,
            Copy,
            loading => IsLoading = loading);
    }

    public override IContent[] GetContent() => [form];

    private CommandResult Copy(MasterPassword password)
    {
        try
        {
            var outcome = services.Session.CopyAsync(itemId, field, password).GetAwaiter().GetResult();
            return outcome switch
            {
                CopyOutcome.Copied => Toasts.Show(CopyMessages.Copied(field, services.ClearDelay), CommandResult.Dismiss()),
                CopyOutcome.WrongPassword => Toasts.Show("The master password is incorrect.", CommandResult.KeepOpen()),
                CopyOutcome.Locked => Toasts.Show("The vault is locked. Open Bitwarden and unlock it.", CommandResult.GoHome()),
                _ => Toasts.Show("This login has no such field.", CommandResult.KeepOpen()),
            };
        }
        catch (ClipboardException exception)
        {
            return Toasts.Show(exception.Message, CommandResult.KeepOpen());
        }
    }
}

/// <summary>
/// One master password field and a submit button. The submitted password goes straight into a
/// <see cref="MasterPassword"/> and from there only to <c>bw unlock</c> (ADR 7).
/// </summary>
internal sealed partial class MasterPasswordForm : FormContent
{
    private readonly Func<MasterPassword, CommandResult> submit;
    private readonly Action<bool> showLoading;

    public MasterPasswordForm(string intro, string button, Func<MasterPassword, CommandResult> submit, Action<bool> showLoading)
    {
        this.submit = submit;
        this.showLoading = showLoading;

        // Command Palette submits a form on Enter only from a single-line TextBox; a password input
        // is a PasswordBox, so Enter does nothing there. The Down arrow then Enter reaches the button.
        TemplateJson = $$"""
            {
              "type": "AdaptiveCard",
              "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
              "version": "1.5",
              "body": [
                {
                  "type": "TextBlock",
                  "text": "{{intro}} Type it, then press the Down arrow and Enter, or click {{button}}. This takes a few seconds.",
                  "wrap": true
                },
                {
                  "type": "Input.Text",
                  "id": "{{UnlockFormInput.PasswordField}}",
                  "style": "Password",
                  "label": "Master password",
                  "isRequired": true,
                  "errorMessage": "Enter your master password."
                }
              ],
              "actions": [
                { "type": "Action.Submit", "title": "{{button}}" }
              ]
            }
            """;
    }

    public override ICommandResult SubmitForm(string inputs)
    {
        if (!UnlockFormInput.TryReadPassword(inputs, out var password) || password is null)
        {
            return Toasts.Show("Enter your master password.", CommandResult.KeepOpen());
        }

        showLoading(true);
        try
        {
            // Command Palette calls this off its UI thread; bw takes a few seconds.
            return submit(password);
        }
        catch (BwCliException exception)
        {
            // Our own fixed message, never bw's output (ADR 13).
            return Toasts.Show(exception.Message, CommandResult.KeepOpen());
        }
        finally
        {
            showLoading(false);
        }
    }
}
