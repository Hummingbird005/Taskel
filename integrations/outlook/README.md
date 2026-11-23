# Taskel for Outlook

Turn emails into Taskel tasks, for yourself, for colleagues or for a group, with instructions. There are two ways:

| | **Outlook add-in** (recommended) | **VBA macro** (this folder) |
|---|---|---|
| Works in | Classic and new Outlook for Windows, Outlook on the web, Outlook for Mac | Classic Outlook for Windows only |
| Looks like | A pane next to the email, in Taskel's design (light and dark) | A window opened from a ribbon button |
| Emails at once | The one being read (the pane can be pinned and follows the selection) | Every selected email |
| Install | Add one manifest file; an administrator can deploy it to everyone | Import the macro, paste the window's code, allow macros |
| Needs | Taskel on HTTPS with a valid certificate; Outlook must reach Microsoft's Office.js | Nothing beyond Outlook |

Both send the same thing to the same API, with a personal API token, and both work with the reply on completion.
Both let you choose the recipients, a due date and instructions, and both mark each email sent with the Outlook
category **"Taskel"**, so the list shows which emails are already in Taskel (filter or search by that category).

## Outlook add-in

The add-in is part of Taskel itself: its pane is the page `outlook.html` of the interface, and Taskel serves the
manifest that points Outlook at it, at `/api/outlook/manifest.xml` (also the **Descarcă manifestul** button in
**Integrări**). The addresses in the manifest are the public address set for emails, otherwise the address the
manifest was downloaded from, so download it from the address people use.

### Install, for yourself

1. In Taskel, open **Integrări** and click **Descarcă manifestul** (`taskel-outlook.xml`).
2. In Outlook: **Get Add-ins** (in new Outlook and on the web: **Apps** → **Add apps**) → **My add-ins** →
   **Custom add-ins** → **Add a custom add-in** → **Add from file**, and pick the file. In Outlook on the web the
   same page is at `https://aka.ms/olksideload`.
3. Open an email. Click **Trimite în Taskel** on the ribbon (in new Outlook and on the web: in the **…** / **Apps**
   menu of the message). The pane asks for an API token once: create one in **Integrări → Token nou**.

### Install, for everyone (administrator)

- **Microsoft 365**: Microsoft 365 admin center → **Settings → Integrated apps → Upload custom apps** →
  **Office Add-in**, upload the manifest, and choose who gets it. It can take up to a day to appear in Outlook.
- **Exchange Server**: Exchange admin center → **Organization → Add-ins** → **+ → Add from file**, then make it
  available or mandatory for users.

When Taskel's address changes, download the manifest again and replace the add-in.

**Updating from version 1.0.** Version 1.1 marks the email it sent, which needs the permission to change the open
email (`ReadWriteItem` instead of `ReadItem`). Outlook keeps using the old manifest until the add-in is installed
again: remove it (**My add-ins → Taskel → Remove**, or in the admin center) and add the new manifest.

### Use

Open an email and click **Trimite în Taskel**. The pane shows the email (subject, sender, importance, files), then:

- **Cui trimiți**: type part of a name or email; pick people and groups from the suggestions (arrow keys and Enter
  work too). They appear as chips; Backspace removes the last one. With nobody chosen, the task is yours.
- **Termen** (optional): a date, or one click on **Azi**, **Mâine**, **Vineri** or **+1 săpt.**; the day is repeated
  in words under the field, in red when it is in the past. Every task gets that due date.
- **Indicații** (optional, up to 2000 characters): shown to each recipient above the email.
- **Atașează fișierele**: on by default. Files shown inside the message (signature logos) are left out. A file
  shared as a link (OneDrive) cannot be sent and is listed at the end.
- **Trimite**: one task per recipient. The pane confirms how many, marks the email with the category **Taskel**, and
  **Deschide Taskel** opens Taskel in the browser.

An add-in cannot create an Outlook category without access to the whole mailbox, which Taskel does not ask for.
If the category **Taskel** does not exist yet, the pane says so after sending: create it once (right-click an email →
**Categorize → All Categories → New**, name `Taskel`), and every email sent from then on gets it. The macro creates
it by itself, so after using the macro once it exists for the add-in too.

Pin the pane (the pin at its top) to keep it open while moving between emails; it resets for each email.

### Notes

- The token is kept in the pane's storage on that computer or browser. **Deconectează** (top right) forgets it.
  Revoke it in Taskel if the device is lost.
- The add-in reads only the email you open and changes nothing but its categories (permission `ReadWriteItem`); it
  never sends mail and cannot see the rest of the mailbox.
- Importance is read from the email's headers; attachments need Outlook with Mailbox 1.8 (Outlook 2021, Microsoft 365,
  new Outlook, web). On older versions the task is created without the files, and the pane says so.
- Outlook loads add-ins only from HTTPS addresses with a trusted certificate (`http://localhost` is allowed for
  testing). Office.js comes from `appsforoffice.microsoft.com`, so Outlook needs internet access to that address.
- The API is called with the token, not the browser session: in Outlook on the web the pane is a frame of another
  site, where browsers do not send Taskel's cookie.

## VBA macro

Select one or more emails in **classic Outlook for Windows**, click **Send to Taskel**, and each email becomes a task:

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

### Install

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

### Use

Select one or more emails in the list (or open one) and click **Send to Taskel**. With the recipients window
installed, it opens first (once for all the selected emails):

- **Către** works like the To field of an email: type part of a name or address, then pick a suggestion with the
  mouse, or with the arrow keys and Enter. Names are kept as `Andrei Stan; Ofertare; `; Backspace right after a name
  removes all of it. Two people with the same name are written with their address (`Ion Popescu <ion@…>`). The
  line under the field says how many recipients there are, or which name is not recognised; a name that is not
  recognised stops the sending until it is corrected. An empty field keeps the task for you.
- **Termen** (optional): type the date as `dd.mm.yyyy` (also `/` or `-`, and a two-digit year), or click **Azi**,
  **Mâine**, **Vineri** or **+1 săpt.**. The day is repeated in words, in red when it is in the past.
- **Indicații** (optional, up to 2000 characters) is shown to each recipient above the email and sent to them in
  the notification.
- **Trimite** creates one task for each recipient, per email, with the subject as the title, the due date and the
  attachments on each. **Renunță** sends nothing.

Each email whose task was created gets the Outlook category **Taskel** (purple), which the macro creates the first
time. An email Outlook will not change, in a read-only shared mailbox for example, keeps its task; the final message
says how many emails could not be marked.

A window pasted before due dates existed still works, without the date: paste the new `TaskelSendForm.txt` to get
it.

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

## Notes on the macro

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
