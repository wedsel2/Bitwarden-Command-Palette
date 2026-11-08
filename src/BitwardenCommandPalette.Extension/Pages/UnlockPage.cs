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
        form = new UnlockForm(session, onUnlocked);
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

    public UnlockForm(SessionManager session, Action onUnlocked)
    {
        this.session = session;
        this.onUnlocked = onUnlocked;
        TemplateJson = $$"""
            {
              "type": "AdaptiveCard",
              "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
              "version": "1.5",
              "body": [
                {
                  "type": "TextBlock",
                  "text": "Enter your master password to unlock the vault.",
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
    }

    private CommandResult Unlocked()
    {
        // The vault page lists the logins now; it must ask for its items again.
        onUnlocked();
        return CommandResult.GoBack();
    }
}
