# Taskel for Outlook

Turn emails into Taskel tasks from **classic Outlook for Windows**. Select one or more emails,
click **Send to Taskel**, and each email becomes a task:

| Email            | Task                                                        |
|------------------|-------------------------------------------------------------|
| Subject          | Title (shortened to 200 characters)                         |
| Sender, received | First lines of the notes (`De la:`, `Primit:`)              |
| Body             | Notes (plain text; the whole description is cut at 2000 characters) |
| Importance       | Priority (low / normal / high → low / medium / high)        |
| –                | Tag `email`                                                 |
| Sender address, Message-ID | Kept so Taskel can reply to the sender (see below) |

It is a VBA macro, so it works with any account type (Gmail, IMAP, POP, Exchange). It does **not**
work in the new Outlook for Windows, Outlook on the web or Outlook for Mac, which cannot run VBA.

## Install

1. **Create an API token.** In Taskel, open **Integrări**, click **Token nou** and copy the
   token (it starts with `tsk_`). It is shown only once.
2. **Allow macros.** In Outlook: **File → Options → Trust Center → Trust Center Settings → Macro
   Settings**, choose **Notifications for all macros**, then restart Outlook.
3. **Import the macro.** Press **Alt+F11** to open the VBA editor, then **File → Import File…** and
   pick `TaskelOutlook.bas` from this folder. Press **Ctrl+S** and close the editor.
4. **Connect it.** Press **Alt+F8**, run **ConfigureTaskel**, enter the address of your Taskel API
   (for a local setup: `http://localhost:5000`) and paste the token. You should see
   "Connected to Taskel."
5. **Add the recipients window (optional, recommended).** It lets you choose who gets the emails. In the VBA
   editor (**Alt+F11**): **Insert → UserForm**. In the **Properties** window (**F4**), set **(Name)** to
   `TaskelSendForm`. Press **F7** to open the form's code, select everything there and delete it, then paste the
   whole content of `TaskelSendForm.txt` from this folder. Leave the form empty in the designer: its fields are
   created when it opens. Press **Ctrl+S**.
   Without this form the macro still works: each email becomes a task of your own.
6. **Add a button.** Right-click the ribbon → **Customize the Ribbon…**. On the right, select the
   **Home (Mail)** tab and click **New Group**. In **Choose commands from**, pick **Macros**, select
   `Project1.TaskelOutlook.SendToTaskel` and click **Add >>**. Use **Rename…** to call it
   "Send to Taskel" and pick an icon.

When Outlook asks about macros at startup, choose **Enable Macros**.

## Use

Select one or more emails in the list (or open one) and click **Send to Taskel**. With the recipients window
installed, it opens first (once for all the selected emails):

- **Caută** narrows the list of people and groups; tick one or more of them. Ticks are kept while you search.
- **Indicații** (optional, up to 2000 characters) is shown to each recipient above the email and sent to them in
  the notification.
- **Trimite** creates one task for each recipient, per email, with the subject as the title and the attachments
  on each. With nobody ticked, the task is yours. **Renunță** sends nothing.

A message confirms how many tasks were added. Switch to Taskel in the browser; the list refreshes when the
window gets focus.

Sending the same email twice creates two tasks.

## Reply on completion

A task created from an email can only be completed in Taskel by writing a response. Taskel emails that
response as a reply to the original email, then marks the task as completed:

- When you complete a task of your own, the reply goes to the email's sender.
- When someone you sent the email to completes their task, the reply comes to you, in the same Outlook
  conversation, with their name and message. Each recipient's completion sends its own reply.
- A recipient can forward the task in Taskel and tick **Revine la mine după finalizare**: when the next person
  completes it, it comes back to the recipient with that person's message, and the recipient closes it, which
  sends the reply to you. This needs an SMTP
mailbox configured on the Taskel server; see "Email (SMTP) configuration" in the main README.

If you installed the macro before this feature existed, import `TaskelOutlook.bas` again (remove the old
`TaskelOutlook` module first: right-click it in the VBA editor → **Remove**, answer **No** to exporting), and add
the recipients window (step 5).
Tasks sent by the old macro still get a reply, but it may not be threaded with the original email.

## Troubleshooting

| Message                               | What to do                                                                 |
|---------------------------------------|----------------------------------------------------------------------------|
| Could not reach Taskel                | Check that the Taskel API is running and the address is right; run **ConfigureTaskel** again. |
| Taskel rejected the API token         | The token was revoked or mistyped. Create a new one and run **ConfigureTaskel**. |
| Nothing happens when clicking the button | Macros are disabled. Repeat step 2 and restart Outlook.                  |
| The recipients window does not open   | The form is missing or not named `TaskelSendForm` (step 5); each email then becomes your own task. |
| Compile error in the form             | Only part of `TaskelSendForm.txt` was pasted, or the form's old code was not deleted first. Paste the whole file again. |

## Notes

- The address and token are stored for your Windows user in the registry, under
  `HKEY_CURRENT_USER\Software\VB and VBA Program Settings\Taskel\Outlook`, in plain text. Revoke
  the token in Taskel (**Integrări → Revocă**) if the computer is shared or lost.
- A token can read and change your tasks, but cannot create or revoke tokens.
- Only the first 4000 characters of the body are sent, as plain text.
- The files attached to an email are added to its task. Pictures shown inside the message (signature logos and
  the like) are skipped. A file larger than Taskel's limit (100 MB unless an administrator changed it) is
  skipped too. The task is created either way, and the final message lists any attachment that was not added.
- Each attachment is saved to your temporary folder, sent, then deleted from there. Large files are read into
  memory while they are sent, so very large attachments can make Outlook pause.
- Use an `https://` address when Taskel is not on the same computer, so the token and email text
  are encrypted in transit.
