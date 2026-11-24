# Plan: next changes

Agreed with the product owner on 2026-11-19 and 2026-11-23. Each part is a separate pull request, in this order.
Decisions taken along the way are recorded here so they are not asked again.

## PR 1 · Outlook: mark sent emails, a "To" field, a due date (done: #27)

- **Mark in Outlook.** An email sent to Taskel gets the Outlook category **"Taskel"** (a coloured label, shown in
  the list and usable as a filter). Outlook allows no custom icon.
  - Macro: creates the category once if it is missing, adds it to the email only after its task was created,
    saves the email. An email that cannot be changed (a read-only shared mailbox) keeps its task, unmarked.
  - Add-in: needs the `ReadWriteItem` permission, so the manifest must be installed again. It cannot create the
    category without a much broader permission; when the category is missing the pane says how to add it.
- **"To" field in the macro window**, as in an email: type a name, pick from up to six suggestions, names are
  added as `Andrei Stan; Ofertare; `. Backspace at the end removes the last name. Every name must match a person or
  a group before sending. The add-in already works this way (chips).
- **Due date** in both: the macro has a date field (dd.mm.yyyy) with *Azi · Mâine · Vineri · +1 săpt.*; the
  add-in a date picker with the same shortcuts. The API already accepts `dueDate`.

## PR 2 · View PDF files and pictures without downloading them (done: #28)

- PDF, PNG, JPEG, GIF and WEBP open in a viewer over the task (previous/next, zoom for pictures, download).
- The API serves them inline only for those types, decided from the file's content, not from the type the
  uploader declared. SVG and everything else stay download-only. PDF uses the browser's own viewer: no new package.

## PR 3 · Distribute, conversation, sending back, reply on closing (done: #29)

- **Distribuie**, next to the state buttons on an open task of yours. **Direcționează** stays as it is
  (it hands the task over; distributing keeps you responsible).
- Each chosen person gets a task of their own, **open**, linked to yours (`ParentTaskId`), with the email, its
  files (shared, not copied) and your message, and optionally a due date. Your task goes **on hold**
  ("Distribuit către …: aștept răspunsurile").
- They complete with a required answer, which joins your task's conversation; you are emailed for each.
  Their tasks never email anyone outside.
- When **all** have answered, your task opens again by itself.
- **Revino**: send one person's task back with a message; it reopens, and yours goes on hold again.
- You close your task with your message; only then the **reply goes to the email**: to its sender if you brought
  the email from Outlook, otherwise to the colleague who did. Closing before everyone answered asks for
  confirmation and closes the remaining tasks as "closed by …".
- **Conversation**: a new table of messages per task (original email, instructions, distribution, answers,
  sending back, closing). The distributor sees all of it; each recipient sees the email and their own exchange.
- **Larger task window** (about 1000px, two columns: details on the left, the conversation with a reply box on the
  right); one column on phones.

## PR 3b · The reply carries the history; files with the answer (#30)

Asked on 2026-11-23, after PR 3.

- The reply to the person who brought the email from Outlook is formatted like a mail thread (HTML and text): the
  answer, the files sent with answers, the history in Taskel, and the original email quoted. A reply to the email's
  sender carries no internal history or colleagues' files.
- Whoever answers can attach files to the answer; on a copy they reach the distributor's task and the conversation,
  and they go with the email reply (up to 20 MB; larger ones are named and stay in Taskel).

## PR 4 · Managers: their team's tasks and due dates (branch feature/team-tasks)

- New view **Task-urile echipei** for managers: every task of the people in their department (administrators:
  any department), grouped by day, with a filter by person.
- A manager can **change the due date** of those tasks, from that view and from **Echipă**. Nothing else becomes
  editable. The person responsible is emailed, and the change is kept in the task's history.

## PR 5 · Priority level 1–20 (branch feature/priority-level)

- An optional **level** (1 = first) next to the existing priority (low / medium / high).
- **Within one day, one person cannot have two tasks with the same level**: the rule applies per person
  responsible and per due date.
- Choosing a level already taken that day **pushes the others down** (the one at 1 goes to 2, and so on); if that
  would go past 20, the change is refused.
- Lists order each day by level (1, 2, 3, …), then tasks without a level, by priority as today. Existing tasks
  need no level.

## PR 6 · Kanban board for managers, with a required reason (branch feature/team-board)

- A level stays optional: tasks without one come after those with one, in each day.
- **Task-urile echipei → Kanban**: the manager picks one person and sees their open tasks as cards, in a column per
  day. Dragging a card up or down changes its level; onto another day, its due date.
- **Every such move needs a reason**: it stays in the task's history with what changed, and the person is emailed the
  details. The same applies to moving a task from its details (the former "move the due date").

## PR 7 · The task as a mail thread (branch feature/mail-thread)

- The task dialog reads like a modern mail app instead of a chat:
  - the subject with a line of facts;
  - a toolbar (Răspunde, Distribuie, Direcționează, Pune în așteptare / Redeschide);
  - one thread with the original email as a mail, messages as cards, events as thin lines, older messages folded;
  - the reply at the end.
- The "Deschis" state button is gone: on hold or completed, **Redeschide** reopens.
- **Doar trimite**: an answer sent without completing the task (`POST /api/tasks/{id}/reply`), by email, to the
  distributor, or to whoever gave the task.

## For every PR

API tests, a browser check, READMEs, manuals (PDF) and screenshots updated. The VBA code cannot run here: its
requests are checked with an emulator and the layout with a mock-up; it must be tried in Outlook.
