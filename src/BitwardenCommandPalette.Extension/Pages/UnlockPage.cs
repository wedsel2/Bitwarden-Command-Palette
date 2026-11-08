using BitwardenCommandPalette.Core;
using BitwardenCommandPalette.Core.Bw;
using BitwardenCommandPalette.Core.Session;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace BitwardenCommandPalette.Extension;

/// <summary>The unlock form: one password field (ADR 12: the extension unlocks, never logs in).</summary>
internal sealed partial class UnlockPage : ContentPage
{
    private readonly UnlockForm form;

    public UnlockPage(SessionManager session, Action onUnlocked)
    {
        Icon = Icons.Lock;
        Title = "Unlock Bitwarden";
        Name = "Unlock";
        form = new UnlockForm(session, onUnlocked, loading => IsLoading = loading);
    }

    public override IContent[] GetContent() => [form];
}

/// <summary>
/// The Adaptive Card behind <see cref="UnlockPage"/>. The submitted password goes straight into a
/// <see cref="MasterPassword"/> and from there only to <c>bw unlock</c> (ADR 7).
/// </summary>
internal sealed partial class UnlockForm : FormContent
{
    private readonly SessionManager session;
    private readonly Action onUnlocked;
    private readonly Action<bool> showLoading;

    public UnlockForm(SessionManager session, Action onUnlocked, Action<bool> showLoading)
    {
        this.session = session;
        this.onUnlocked = onUnlocked;
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
                  "text": "Enter your master password, then press the Down arrow and Enter, or click Unlock. Unlocking takes a few seconds.",
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
                { "type": "Action.Submit", "title": "Unlock" }
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
            var outcome = session.UnlockAsync(password).GetAwaiter().GetResult();
            return outcome switch
            {
                UnlockOutcome.Unlocked => Unlocked(),
                UnlockOutcome.WrongPassword => Toasts.Show("The master password is incorrect.", CommandResult.KeepOpen()),
                UnlockOutcome.NotLoggedIn => Toasts.Show("The Bitwarden CLI is not logged in. Run 'bw login' once in a terminal.", CommandResult.KeepOpen()),
                _ => Toasts.Show("The vault was locked again before it finished loading. Try again.", CommandResult.KeepOpen()),
            };
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

    private CommandResult Unlocked()
    {
        // The vault page lists the logins now; it must ask for its items again.
        onUnlocked();
        return Toasts.Show("Vault unlocked.", CommandResult.GoBack());
    }
}
