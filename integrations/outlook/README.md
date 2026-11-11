# Taskel for Outlook

Turn emails into Taskel tasks from **classic Outlook for Windows**. Select one or more emails,
click **Send to Taskel**, and each email becomes a task:

| Email            | Task                                                        |
|------------------|-------------------------------------------------------------|
| Subject          | Title (shortened to 200 characters)                         |
| Sender, received | First lines of the notes                                    |
| Body             | Notes (plain text; the whole description is cut at 2000 characters) |
| Importance       | Priority (low / normal / high → low / medium / high)        |
| –                | Tag `email`                                                 |

It is a VBA macro, so it works with any account type (Gmail, IMAP, POP, Exchange). It does **not**
work in the new Outlook for Windows, Outlook on the web or Outlook for Mac, which cannot run VBA.

## Install

1. **Create an API token.** In Taskel, open **Integrations**, click **New token** and copy the
   token (it starts with `tsk_`). It is shown only once.
2. **Allow macros.** In Outlook: **File → Options → Trust Center → Trust Center Settings → Macro
   Settings**, choose **Notifications for all macros**, then restart Outlook.
3. **Import the macro.** Press **Alt+F11** to open the VBA editor, then **File → Import File…** and
   pick `TaskelOutlook.bas` from this folder. Press **Ctrl+S** and close the editor.
4. **Connect it.** Press **Alt+F8**, run **ConfigureTaskel**, enter the address of your Taskel API
   (for a local setup: `http://localhost:5000`) and paste the token. You should see
   "Connected to Taskel."
5. **Add a button.** Right-click the ribbon → **Customize the Ribbon…**. On the right, select the
   **Home (Mail)** tab and click **New Group**. In **Choose commands from**, pick **Macros**, select
   `Project1.TaskelOutlook.SendToTaskel` and click **Add >>**. Use **Rename…** to call it
   "Send to Taskel" and pick an icon.

When Outlook asks about macros at startup, choose **Enable Macros**.

## Use

Select one or more emails in the list (or open one) and click **Send to Taskel**. A message
confirms how many tasks were added. Switch to Taskel in the browser; the list refreshes when the
window gets focus.

Sending the same email twice creates two tasks.

## Troubleshooting

| Message                               | What to do                                                                 |
|---------------------------------------|----------------------------------------------------------------------------|
| Could not reach Taskel                | Check that the Taskel API is running and the address is right; run **ConfigureTaskel** again. |
| Taskel rejected the API token         | The token was revoked or mistyped. Create a new one and run **ConfigureTaskel**. |
| Nothing happens when clicking the button | Macros are disabled. Repeat step 2 and restart Outlook.                  |

## Notes

- The address and token are stored for your Windows user in the registry, under
  `HKEY_CURRENT_USER\Software\VB and VBA Program Settings\Taskel\Outlook`, in plain text. Revoke
  the token in Taskel (**Integrations → Revoke**) if the computer is shared or lost.
- A token can read and change your tasks, but cannot create or revoke tokens.
- Only the first 4000 characters of the body are sent, as plain text. Attachments are not sent.
- Use an `https://` address when Taskel is not on the same computer, so the token and email text
  are encrypted in transit.
