# Taskel

Task management app with an **ASP.NET Core (.NET 10)** backend and a **React + Vite + TypeScript** frontend (Node.js).
The interface is in Romanian; code, API and documentation are in English.


![Taskel dashboard: at-a-glance counts, overdue, due today, upcoming, priority and weekly completion widgets](docs/screenshots/dashboard.png)

## Screenshots

| | |
|---|---|
| **Team workload** for managers: each person in the department<br>![Team screen with totals and a table of people with open, overdue and completed counts](docs/screenshots/team.png) | **A team member's tasks**, in full, read only<br>![A team member's open and recently completed tasks, with notes](docs/screenshots/team-member.png) |
| **Team calendar**: open tasks per person and day, four weeks at a time<br>![Heat map of tasks per person and day, with the tasks of the selected day listed below](docs/screenshots/team-calendar.png) | **One person's calendar**, with their tasks on each day<br>![Four-week calendar of one person's tasks](docs/screenshots/team-calendar-person.png) |
| **New task**, in a dialog opened from **Task nou**: title, message, dates, priority, tags, who it is for, files<br>![New task dialog with a title, a message, an interval, priority, tags, an assignee chosen by search and an attached file](docs/screenshots/new-task.png) | **Delegated tasks**: what you assigned to others<br>![Delegate view listing tasks assigned to other people](docs/screenshots/delegated.png) |
| **Tasks for a group**: every member sees them until one takes them<br>![De preluat view with two group tasks and a Preia button on each](docs/screenshots/claimable.png) | **Choosing who a task is for**, by searching names, emails and groups<br>![The assignee field with two people matching the letters typed](docs/screenshots/assign-search.png) |
| **Forwarding** a received task to someone else, with a message<br>![Forward form with the recipient, a message and a workload warning](docs/screenshots/forward.png) | **Forwarded tasks** stay readable, with the path they took<br>![Direcționate view showing a completed task, who has it now and its path](docs/screenshots/forwarded.png) |
| **Groups**, managed by administrators and managers<br>![Groups screen listing Active Directory groups and a form for a new group with members](docs/screenshots/groups.png) | **Sign in** with Keycloak, a domain or a local account<br>![Sign-in screen with a Keycloak button above the tabs for domain account and local account](docs/screenshots/signin.png) |
| **System settings**, for administrators: mail server, Active Directory and Keycloak, each with a test<br>![The mail server panel with its fields, the status line and the result of a test message](docs/screenshots/settings-email.png) | **Active Directory test**: what Taskel reads for an account, before saving<br>![The Active Directory panel with a successful test sign-in showing the name, department and groups](docs/screenshots/settings-directory.png) |
| **Roles and departments**, for administrators<br>![Users screen with a role and a department for each account](docs/screenshots/users.png) | **Reminder on opening the app**, when tasks are overdue or due today<br>![Reminder dialog listing overdue and due-today tasks](docs/screenshots/reminder.png) |
| **Task list**: titles grouped by due date, with a state mark and the key facts; a row opens the task<br>![Task list grouped under Overdue, Today, Tomorrow, Later and No due date, one task on hold with a clock](docs/screenshots/tasks.png) | **Reply on completion** for tasks created from emails<br>![Response form shown when completing an email task](docs/screenshots/reply.png) |
| **Customizable dashboard**: add, remove and reorder widgets<br>![Dashboard in customize mode with move and remove controls](docs/screenshots/customize.png) | **Dark theme**<br>![Dashboard in the dark theme](docs/screenshots/dashboard-dark.png) |
| **Mobile**<br><img src="docs/screenshots/mobile-dashboard.png" alt="Dashboard on a phone" width="48%"> <img src="docs/screenshots/mobile-tasks.png" alt="Task list on a phone" width="48%"> | **Email notifications** when a task reaches you, with a switch for each user<br>![Integrări screen with the email notification checkbox](docs/screenshots/notifications.png) |
| **Opening a task**: its message, details and files, and the buttons that change its state<br>![Task dialog with the title, the message, due date and priority, files, a resolution field and the Deschis, În așteptare and Finalizat buttons](docs/screenshots/task-detail.png) | **On hold**, with the reason it waits<br>![Task dialog showing an amber note: on hold since a date, waiting for the legal department](docs/screenshots/task-waiting.png) |
| **Completed**, with the resolution written when it was done<br>![Task dialog with a green note: completed on a date, with the resolution](docs/screenshots/task-done.png) | **New task on a phone**, full screen<br><img src="docs/screenshots/mobile-new-task.png" alt="New task dialog on a phone, with the assignee search open" width="48%"> <img src="docs/screenshots/mobile-task.png" alt="A task opened on a phone, on hold" width="48%"> |
| **Arctic Frost** theme: frosted glass over soft pastel shapes<br>![Dashboard in the Arctic Frost theme, with translucent cards over a pale blue and lavender background](docs/screenshots/frost-dashboard.png) | **Arctic Frost**, task list<br>![Task list in the Arctic Frost theme](docs/screenshots/frost-tasks.png) |
| **Amber Glass** theme: smoked glass in a warm, lamp-lit room<br>![Dashboard in the Amber Glass theme, dark translucent cards over amber and orange light](docs/screenshots/amber-dashboard.png) | **Amber Glass**, task list<br>![Task list in the Amber Glass theme](docs/screenshots/amber-tasks.png) |
| **Loading**: skeletons in the shape of the content<br>![Dashboard with grey placeholder cards while it loads](docs/screenshots/loading.png) | **Empty state**: what is missing and why<br>![Empty Direcționate view with an icon, a title and an explanation](docs/screenshots/empty.png) |
| **Tablet**: the sidebar becomes a rail that opens on hover<br><img src="docs/screenshots/tablet.png" alt="Dashboard at tablet width with an icon-only sidebar" width="70%"> | **Tables on a phone** become cards<br><img src="docs/screenshots/mobile-team.png" alt="Team workload on a phone, one card per person" width="48%"> |

Screenshots use the [demo accounts and tasks](docs/DEMO-ACCOUNTS.md).

**Manuals (PDF, in Romanian):** [user](docs/manuale/1-manual-utilizator.pdf), [manager](docs/manuale/2-manual-manager.pdf),
[administrator](docs/manuale/3-manual-administrator.pdf), and the installation guides for [Linux](docs/manuale/4-ghid-instalare.pdf)
and [Windows with IIS](docs/manuale/5-ghid-instalare-windows.pdf).
Their sources are in `docs/manuale/src`; rebuild them with `node docs/manuale/build.mjs` after changing the text or the
screenshots.

**For developers:** how the code is organised and how to extend it is in [docs/DEVELOPER.md](docs/DEVELOPER.md). The last security scan (SonarQube, Semgrep, dependency audits), its findings and how to run it again are in
[docs/SECURITY-SCAN.md](docs/SECURITY-SCAN.md).

## Interface design

The interface is minimal and high-contrast: near-black text on white (and the reverse in the dark theme, which
follows the system setting), one blue accent for the active item and focus, and colour otherwise only where it
means something: red for overdue, amber for warnings, green for done. Text is set in Inter, bundled with the
app, on a 13/15/17/22/28 px scale and a 4 px spacing grid. The tokens are at the top of
`frontend/src/index.css`.

### Themes

The theme is chosen at the bottom of the sidebar and remembered in the browser (so it also applies to the
sign-in screen, and is separate on each device):

- **Automată** (default) follows the system: light or dark.
- **Luminoasă** and **Întunecată** fix the light or the dark theme whatever the system says.
- **Arctic Frost** is a light glass theme in the spirit of macOS and visionOS: a pale slate background with
  three out-of-focus pastel shapes (pale blue, lavender, ice), and cards, sidebar, tables and forms as frosted
  glass — translucent white, a strong backdrop blur, a white edge with a highlight along the top and a very
  faint shadow. Text stays dark slate and primary buttons stay solid dark, so legibility never depends on what
  is behind the glass. Drawers and input fields are nearly opaque for the same reason.

- **Amber Glass** is its dark counterpart: a near-black, warm background with out-of-focus amber and orange
  light, surfaces of smoked glass with a faint light edge, warm white text, and amber for everything that acts
  or measures — primary buttons, ticks, progress bars and the busiest days in the calendar.

Where the browser cannot blur what is behind an element, and for people who ask their system for less
transparency or more contrast, the glass themes fall back to solid surfaces without the shapes.

A theme is a set of tokens under `:root[data-theme='…']` in `frontend/src/index.css`; the list offered in the
picker is in `frontend/src/lib/theme.ts`. The glass themes share their structure (the `[data-glass]` rules), so
another one needs only its tokens and whatever it wants to colour differently.

### States and layout

- **Loading** shows skeletons shaped like the content; buttons show a spinner and cannot be pressed twice.
- **Empty** lists say what is missing and, where it makes sense, offer the action that fills them.
- **Hover and focus**: rows, buttons and fields respond to the pointer, and everything interactive has a
  visible keyboard focus ring. Row actions appear on hover or focus, and are always visible on touch screens.
- **Responsive**: above 1024 px the sidebar is fixed; from 641 to 1024 px it is a rail of icons that opens on
  hover or keyboard focus (a drawer on touch tablets); up to 640 px it is a drawer, the new-task dialog fills the screen, tables
  become cards and touch targets are at least 44 px. The team calendar's heat map still scrolls sideways on a
  phone.
- Animations are switched off for people who ask their system to reduce motion.

Shared pieces live in `frontend/src/components/ui`: `Button`, `Banner`, `Panel`, `DataTable`, `EmptyState` and
the skeletons.

## Project layout

```
Taskel/
├── backend/                     # ASP.NET Core Web API (.NET 10)
│   ├── Taskel.slnx
│   ├── src/Taskel.Api/          # API: controllers, models, services
│   └── tests/Taskel.Api.Tests/  # xUnit integration tests
├── frontend/                    # React + Vite + TypeScript
├── integrations/outlook/        # Outlook macro: turn emails into tasks
├── deploy/keycloak/             # Development Keycloak realm (docker compose profile "keycloak")
├── offline/                     # Every NuGet and npm package, to build without the internet; IIS web.config
├── docs/                        # Developer guide, demo accounts, Keycloak setup, screenshots
└── .github/workflows/ci.yml     # Build + test both projects
```

## Prerequisites

Without internet access, restore from the packages in `offline/` instead: see [offline/README.md](offline/README.md).

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js](https://nodejs.org/) 22+
- PostgreSQL 16+ — easiest via Docker: `docker compose up -d` (uses `docker-compose.yml`)

## Running locally

**Database**:

```bash
docker compose up -d          # PostgreSQL on localhost:5432 (user/password/db: taskel)
docker compose --profile keycloak up -d   # also a Keycloak on localhost:8080, to try single sign-on
```

The development Keycloak, its accounts and how to connect Taskel to it are in [docs/KEYCLOAK.md](docs/KEYCLOAK.md).

**Backend** (http://localhost:5000):

```bash
cd backend
dotnet run --project src/Taskel.Api
```

In Development the API applies EF Core migrations automatically on startup.

**Frontend** (http://localhost:5173) — in a second terminal:

```bash
cd frontend
npm install
npm run dev
```

The Vite dev server proxies `/api/*` to the backend, so no CORS setup is needed in development.

**Try it**: on an empty database, the API in Development adds sample accounts for every role (including an
initial administrator) and sample tasks. The emails and passwords are in
[docs/DEMO-ACCOUNTS.md](docs/DEMO-ACCOUNTS.md). They are public: the sample data is added only when
`Seed:DemoData` is true, which it is only in `appsettings.Development.json`.
For other setups, see `frontend/.env.example` and `Cors:AllowedOrigins` in `backend/src/Taskel.Api/appsettings.json`.

## API

| Method | Route                | Auth | Description                                   |
|--------|----------------------|------|-----------------------------------------------|
| GET    | `/api/health`        | –    | Health check                                  |
| POST   | `/api/auth/register` | –    | Create an account and sign in                 |
| GET    | `/api/auth/options`  | –    | What the sign-in screen should offer          |
| POST   | `/api/auth/login`    | –    | Sign in with a local account                  |
| POST   | `/api/auth/login/directory` | – | Sign in with an Active Directory account   |
| POST   | `/api/auth/logout`   | –    | Sign out. For a Keycloak session, answers with the address that ends it there too |
| GET    | `/api/auth/me`       | ✓    | Current user                                  |
| GET    | `/api/auth/tokens`   | session | List your API tokens                       |
| POST   | `/api/auth/tokens`   | session | Create an API token (shown once)           |
| DELETE | `/api/auth/tokens/{id}` | session | Revoke an API token                     |
| GET    | `/api/dashboard`     | ✓    | Your dashboard preferences                    |
| PUT    | `/api/dashboard`     | ✓    | Save your dashboard preferences               |
| GET    | `/api/notifications` | ✓    | Your email notification preference            |
| PUT    | `/api/notifications` | ✓    | Turn emails for received tasks on or off      |
| GET    | `/api/users`         | ✓    | People a task can be assigned to              |
| GET    | `/api/users/{id}/workload` | ✓ | How many open tasks a user has in a period  |
| GET    | `/api/team`          | manager | Workload of the people in a department     |
| GET    | `/api/team/calendar` | manager | A department's dated open tasks, per person, for a period |
| GET    | `/api/team/members/{id}/tasks` | manager | A team member's tasks, read only |
| GET    | `/api/team/tasks`    | manager | Every task of the department's people: open, and completed in the last 14 days |
| PUT    | `/api/team/tasks/{taskId}/due-date` | manager | Move the due date of a team member's task |
| GET    | `/api/admin/users`   | admin | Accounts with their role and department      |
| PUT    | `/api/admin/users/{id}` | admin | Set an account's role and department      |
| GET    | `/api/attachments/limits` | ✓ | Largest file accepted and files per task     |
| POST   | `/api/tasks/{id}/attachments` | ✓ | Attach a file (multipart/form-data, part `file`) |
| GET    | `/api/tasks/{id}/attachments/{fileId}` | ✓ | Download a file; with `?view=true`, show a PDF or a picture |
| DELETE | `/api/tasks/{id}/attachments/{fileId}` | ✓ | Remove a file                        |
| GET    | `/api/team/members/{id}/tasks/{taskId}/attachments/{fileId}` | manager | Download a file of a team member's task (`?view=true` as above) |
| GET    | `/api/admin/settings/attachments` | admin | The attachment size limit              |
| PUT    | `/api/admin/settings/attachments` | admin | Change the attachment size limit       |
| GET/PUT/DELETE | `/api/admin/integrations/email` | admin | Mail server and notifications: read, save, back to the server configuration |
| POST   | `/api/admin/integrations/email/test` | admin | Send a test message, with the settings in the form, to yourself |
| GET/PUT/DELETE | `/api/admin/integrations/directory` | admin | Active Directory (LDAP) settings     |
| POST   | `/api/admin/integrations/directory/test` | admin | Sign in with a domain account using the settings in the form; creates nothing |
| GET/PUT/DELETE | `/api/admin/integrations/keycloak` | admin | Keycloak settings                     |
| POST   | `/api/admin/integrations/keycloak/test` | admin | Check the realm and the client secret |
| POST   | `/api/admin/integrations/keycloak/import` | admin | Create or update accounts for the realm's users |
| GET    | `/api/auth/keycloak/login` | – | Start a Keycloak sign-in (the browser goes there and comes back) |
| GET    | `/api/auth/keycloak/callback` | – | Where Keycloak sends the browser back             |
| GET    | `/api/groups`        | ✓    | Groups a task can be given to                 |
| GET    | `/api/groups/{id}`   | manager | A group with its members                   |
| GET    | `/api/groups/candidates` | manager | People you may put in a group          |
| POST   | `/api/groups`        | manager | Create an application group                |
| PUT    | `/api/groups/{id}`   | manager | Rename a group and set its members         |
| DELETE | `/api/groups/{id}`   | manager | Delete an application group                |
| GET    | `/api/tasks`         | ✓    | List the tasks you can see                    |
| GET    | `/api/tasks/{id}`    | ✓    | Get a task                                    |
| POST   | `/api/tasks`         | ✓    | Create a task                                 |
| POST   | `/api/tasks/from-email` | ✓ | Create a task from an email                   |
| POST   | `/api/tasks/from-email/distribute` | ✓ | Give an email to people or groups: one task each, with instructions |
| GET    | `/api/outlook/recipients` | ✓ | People and groups an email can go to, as tab-separated lines (for the Outlook macro) |
| GET    | `/api/outlook/manifest.xml` | – | The Outlook add-in's manifest, pointing at this Taskel |
| PUT    | `/api/tasks/{id}`    | ✓    | Update a task                                 |
| PUT    | `/api/tasks/{id}/assignee` | ✓ | Assign a task to a user or a group (creator only) |
| POST   | `/api/tasks/{id}/forward` | ✓ | Pass a received task on to a user or a group (assignee only) |
| POST   | `/api/tasks/{id}/distribute` | ✓ | Give a copy of the task to several people or groups; the task waits for their answers |
| POST   | `/api/tasks/{id}/distributions/{copyId}/send-back` | ✓ | Reopen one answered copy, with a message |
| GET    | `/api/tasks/{id}/conversation` | ✓ | The task's conversation: distribution, answers, messages |
| POST   | `/api/tasks/{id}/messages` | ✓ | Write in the conversation (`relatedTaskId`: to one copy's exchange) |
| POST   | `/api/tasks/{id}/claim` | ✓ | Take a task that waits on one of your groups   |
| POST   | `/api/tasks/{id}/release` | ✓ | Hand a task taken from a group back to it    |
| POST   | `/api/tasks/{id}/complete` | ✓ | Complete a task, with a response (required for email tasks) |
| POST   | `/api/tasks/{id}/wait` | ✓ | Put a task on hold, with a required reason |
| POST   | `/api/tasks/{id}/resume` | ✓ | Take a task off hold |
| DELETE | `/api/tasks/{id}`    | ✓    | Delete a task                                 |

✓ = browser session or API token. "session" = browser session only.

### Task fields

| Field         | Type                          | Notes                                                              |
|---------------|-------------------------------|--------------------------------------------------------------------|
| `title`       | string, 1–200 chars           | Required                                                           |
| `description` | string, up to 2000 chars      | Optional                                                           |
| `isCompleted` | boolean                       | Set through `PUT`                                                  |
| `startDate`   | `"YYYY-MM-DD"` or `null`      | Optional first day of the task's interval; needs a `dueDate` on or after it |
| `dueDate`     | `"YYYY-MM-DD"` or `null`      | A calendar date, no time or time zone; the last day of the interval |
| `priority`    | `"low"`, `"medium"`, `"high"` | Defaults to `"medium"`                                             |
| `tags`        | array of strings              | Up to 10 tags of up to 30 chars; trimmed, lower-cased, de-duplicated |
| `sourceEmailAddress` | string or `null`       | Read-only. Sender of the email the task came from                  |
| `completionResponse` | string or `null`       | Read-only. The response given when the task was completed          |
| `completedAt` | timestamp or `null`           | Read-only                                                          |
| `createdBy`   | `{ id, email, displayName }`  | Read-only. Who created the task                                    |
| `assignee`    | same shape, or `null`         | Read-only here; set with `assigneeId` on create or through `/assignee` |

`PUT` replaces the whole task: omitted optional fields are reset (no due date, medium priority, no tags).

The OpenAPI document is available at `/openapi/v1.json` in Development.

## Authentication

- Accounts are email + password. Passwords (8+ characters) are stored hashed with ASP.NET Core's `PasswordHasher` (PBKDF2).
- Sessions use an **HttpOnly cookie** (`taskel.auth`, `SameSite=Lax`, 14 days, sliding), so JavaScript never sees a token.
  Outside Development the cookie is `Secure` and is only sent over HTTPS.
- Every task belongs to the user who created it; users only see and change their own tasks.
- Unauthenticated requests to protected endpoints get `401`.
- Deployment notes:
  - Serve the frontend and the API from the same site (as the Vite dev proxy does). A frontend on a different site
    needs `SameSite=None` on the cookie plus its origin in `Cors:AllowedOrigins`.
  - The cookie, and the secrets saved in **Setări sistem**, are encrypted with ASP.NET Core Data Protection keys.
    They are kept in the database (table `data_protection_keys`), so they survive restarts and are shared by every
    server. Set `DataProtection__KeysPath` to keep them in a folder instead, for example a volume that is not part
    of the database backups. Losing the keys signs everyone out and makes the saved secrets unreadable: the
    settings page then asks for them again.
  - Sign-in attempts are limited per client address (`Auth__LoginAttemptsPerMinute`, default 10).

### API tokens

Clients that cannot use the browser session send a personal API token instead:

```
Authorization: Bearer tsk_…
```

- Create and revoke tokens in the app under **Integrări** (Integrations; up to 10 per user). A token is shown once;
  only its SHA-256 hash is stored.
- A token gives access to its owner's tasks. It cannot list, create or revoke tokens.

## Outlook integration

Emails become tasks tagged `email`, with the subject as title and the sender, date and body as notes, in one of
two ways (setup and comparison in [integrations/outlook/README.md](integrations/outlook/README.md)):

- **Outlook add-in** (recommended): a pane next to the email, in Taskel's design, in every Outlook (Windows classic
  and new, web, Mac). It is the page `outlook.html` of the interface; Taskel serves its manifest at
  `/api/outlook/manifest.xml` (button **Descarcă manifestul** in **Integrări**), with the public address set for
  emails, otherwise the address it was downloaded from. Outlook needs Taskel on HTTPS with a trusted certificate.
- **VBA macro** in `integrations/outlook`, for classic Outlook on Windows: sends the selected emails to
  `POST /api/tasks/from-email`.

Both can set the recipients, a due date (`dueDate`) and instructions, and mark each email sent with the Outlook
category "Taskel". The add-in's manifest asks for `ReadWriteItem` for that (version 1.1; an add-in installed from
1.0 must be installed again).

Both call the API with a personal API token.

The add-in, and the macro with its optional `TaskelSendForm` window installed, ask who gets the email and for
instructions, then call `POST /api/tasks/from-email/distribute` with `recipients` (user ids, or `group:` and a
group id; at most 50) and `instructions` (up to 2000 characters):

- Each recipient gets a task of their own, created by the Outlook user, titled with the subject, with the
  instructions shown above its notes and emailed to them. A group gets one task, which a member takes.
  No recipient: the task is the Outlook user's own.
- The answer is `201` with `{ "taskIds": [ … ] }`, one id per recipient; the add-in or the macro uploads the
  attachments to each.
- When a recipient completes their task, their message is emailed to the Outlook user (not to the original
  sender), as a reply to the original email, so it lands in that conversation in Outlook. Each recipient's
  completion sends its own reply. The Outlook user answers the sender from Outlook, or completes a task of
  their own in Taskel, which replies to the sender.

## Input validation

Every request is validated before anything is stored:

- **Shape.** Bodies must be JSON objects of at most 1 MB. Unknown properties are rejected (`400`), so a
  misspelt field is an error rather than silently ignored. Enums are strings; ids are GUIDs.
- **Text.** Titles, notes, tags, token names, responses and user names reject control characters, NUL and
  unpaired UTF-16 surrogates. Notes and responses may contain line breaks and tabs; single-line fields may not.
  Text is trimmed, and blank notes are stored as none.
- **Limits.** Title 1–200 characters, notes 2000, completion response 4000, up to 10 tags of up to 30 characters
  (no commas), due dates between 2000-01-01 and 2100-12-31, passwords 8–128 characters.
- **Email addresses** must be plain `name@host.tld` addresses.
- **Emails turned into tasks** are the exception: they come from outside, so their text is cleaned (unsafe
  characters removed, long values shortened) instead of rejected. Only the overall shape and lengths are checked.

The interface applies the same rules in its forms and shows the reason in Romanian. Validation failures return
`400` with a problem-details body listing the fields.

## Priority level

Next to the priority (low, medium, high), a task can have a **priority level**, 1 to 20: its place in its day, 1
first (`priorityLevel` in the task and in create and update requests; optional, and left out of an update it is
removed).

- **Within one day, one person has one open task per level.** The person is the one the task is with (its assignee,
  or its creator while it is with nobody else); the day is its due date. Tasks without a due date, tasks waiting for
  a group and completed tasks hold no place.
- Choosing a level already taken that day **pushes the others down**: the task at that level goes one lower, and the
  one below it too if that level was taken, until a free level. If that would take a task past 20, the change is
  refused with `409` and nothing moves.
- A task that reaches someone else's day without being asked for (assigned, forwarded, taken from a group, sent
  back, its due date moved by a manager) keeps its level if there is room, and loses it otherwise.
- Lists order each day by level, then the tasks without one by priority. The level shows as a dark square before the
  title.

![A day's tasks ordered by level, 1 first, then those without a level](docs/screenshots/priority-level.png)

## Assigning tasks

A task belongs to the user who created it and can be assigned to one other user.

- Assign on create with `assigneeId`, or later with `PUT /api/tasks/{id}/assignee` and
  `{ "assigneeId": "…" }` (`null` takes the task back). `GET /api/users` lists the people to choose from.
- The creator and the assignee both see the task, edit it and complete it (with the emailed reply, for
  tasks from emails). Only the creator can delete or reassign it; the assignee gets `403`.
- Other users cannot see the task at all (`404`).
- A task can also go to a group, and an assignee can pass it on: see [Groups and forwarding](#groups-and-forwarding).
- In the interface, new tasks are written in a dialog opened with **Task nou**, at the top of the dashboard
  and of every task list. The person or group is chosen by **searching**: type part of a name, an email or a
  group name (case and diacritics do not matter) and pick from the matches. Nothing is listed before typing.

`GET /api/users` shows every account's name and email to every signed-in user, which suits a team
installation. The search runs in the browser over that list, which is fine for hundreds of accounts.

### Working on a task: open, on hold, completed

Lists show each task's **title** with a mark for its state and a line of facts (due date, high priority, who has
it, files). Clicking anywhere on the row opens the task: its title and **message**, the details, the files and
the path it took.

At the bottom, the people working on the task (its creator and its assignee) write a message and press one of
the state buttons:

| Button | Colour | What it does |
|---|---|---|
| **Deschis** / **Reia lucrul** / **Redeschide** | blue | Back to open: takes a task off hold, or reopens a completed one |
| **În așteptare** | amber | Puts the task on hold. **The message is required**: it is the reason, shown to everyone who can see the task. Pressed again, it updates the reason |
| **Finalizat** | green | Completes the task. The message is kept as its resolution; for a task from an email it is required and is emailed as the reply |

In the lists, an open task has an empty ring, a task on hold a clock and an **În așteptare** label, and a
completed task a **green tick**. Completed titles are not struck through.

A task on hold still counts as open work: in the views, the dashboard, the reminder, the team's workload and
calendar. Completing or reopening a task takes it off hold. In the API, each task has `state` (`open`,
`waiting`, `completed`), `waitingReason` and `waitingSince`.

Members of the group a task waits on see it read-only with a **Preia task-ul** button; someone who forwarded a
task and a manager reading their people's tasks see it read-only.

### Delegate vs Direcționate

Both lists hold tasks that are with someone else now. The difference is whose they are:

| | **Delegate** (delegated) | **Direcționate** (forwarded) |
|---|---|---|
| Which tasks | Tasks **you created** and gave to someone else or to a group | Tasks **you received** from someone and passed on |
| Whose they are | Still yours: you are their creator | Not yours any more: the creator is someone else |
| What you can do | Everything: edit, reassign, take back, delete, complete | Only read them, to follow what happens |
| How they got there | Choosing a person or group when adding or editing your task | **Direcționează** on a task assigned to you |

Your own work (tasks assigned to you, and yours that nobody else has) fills the other views, the dashboard and
the reminder. Tasks waiting on one of your groups are under **De preluat** until someone takes them.

## Groups and forwarding

### Tasks for a group

A task can be given to a group instead of a person: `groupId` in place of `assigneeId`, on create or with
`PUT /api/tasks/{id}/assignee`. It is one shared task, not a copy per member.

- Until someone takes it, every member of the group sees it under **De preluat**, read only.
- A member takes it with **Preia** (`POST /api/tasks/{id}/claim`) and becomes its assignee: it moves to their
  own tasks and disappears for the other members. If two people press at once, the second is told it is gone.
- Whoever took it can hand it back to the group (`POST /api/tasks/{id}/release`).
- The creator follows it under **Delegate** and sees who took it.
- A task nobody has taken counts in nobody's workload, calendar or statistics.

There are two kinds of groups, offered together in the assignee picker:

- **Application groups** are made on the **Grupuri** screen. Administrators manage all of them. Managers
  create their own, with people from their department, and manage only those. Deleting a group returns its
  untaken tasks to the people who created them.
- **Active Directory groups** appear by themselves: at each domain sign-in Taskel reads the user's `memberOf`
  and updates their memberships. They cannot be edited in Taskel. See the limits under
  [Active Directory sign-in](#active-directory-sign-in).

### Forwarding

Someone who received a task can pass it on to another user or to a group (`POST /api/tasks/{id}/forward` with
`assigneeId` or `groupId`, an optional `note` of up to 500 characters, and `returnToMe`).

- The new assignee gets every right the previous one had, including forwarding it again.
- The person who forwarded keeps the task under **Direcționate**, read only, and sees how it ends.
- Every step is kept and returned in the task's `forwards` (from, to, note, time); the interface shows it as
  **Traseu** to everyone who can see the task.
- Only the current assignee can forward. The creator reassigns instead.
- With `returnToMe: true` (the interface's **Revine la mine după finalizare**, checked by default), the task comes
  back when the new assignee completes it: their message (required) is kept as a return step in the path, the
  task is assigned to the forwarder again (or back to the creator, unassigned, when the creator forwarded it)
  and stays open, and the forwarder is emailed. The forwarder then closes it, or forwards it again. Returns
  unwind a chain one step at a time. `PUT` with `isCompleted: true` is refused while a return is pending, and
  the creator reassigning the task cancels every pending return. Tasks expose it as `returnsTo` (who completing
  sends it to) and each forward step as `returnPending` / `isReturn`.

### Distributing

The person responsible for a task (its assignee, or its creator while it is with nobody else) can **distribute**
it: **Distribuie**, next to the state buttons. Unlike forwarding, the task stays theirs.

- `POST /api/tasks/{id}/distribute` with `recipients` (user ids, or `group:` and a group id; at most 50), an
  optional `message` (up to 2000 characters) and `dueDate` (the task's own by default). Each recipient gets a
  **copy**: a task of their own, open, created by the distributor, linked to the original (`distributedFrom`), with
  its message, its files (shared, not copied on disk) and the message as instructions. Nobody can be sent a
  second copy while their first is open, and the distributor cannot distribute to themselves.
- The task goes **on hold** ("Distribuit către …: aștept răspunsurile"). Its `distributions` list every copy: who
  has it, its state, due date and answer.
- A recipient completes their copy with a **required answer** (`answersTo` says who gets it). The answer joins the
  task's conversation and the distributor is emailed. Copies never email anyone outside Taskel.
- When **every** copy is answered, the task opens again by itself, and the distributor is told. A reason the
  user wrote by hand is never replaced or cleared.
- **Revino** (`.../distributions/{copyId}/send-back` with a `message`) reopens one answered copy; the task
  waits again.
- The distributor **closes** the task with their own message; only then does the email reply leave, by the usual
  rule (to the sender for whoever brought the email from Outlook, otherwise to that person). Closing with copies
  still open needs `closeDistributions: true` (the interface asks first): their tasks are closed, and their
  holders told.
- Reopening a copy, or deleting an open one, keeps the task's state in step. A file is removed from disk only
  when no task lists it any more.

**Conversation.** `GET /api/tasks/{id}/conversation` returns the task's messages, oldest first: distribution,
answers, sending back, closing, and notes (`POST /api/tasks/{id}/messages`). The distributor reads every
exchange; each recipient reads their own (on their copy). Notes are written by those who work on the task; the
other side of an exchange is emailed. The interface shows the conversation beside the task, in a wider dialog,
together with the task's own history (created, forwarded, sent back, completed).

| | |
|---|---|
| ![Distributed task: on hold, its copies and the conversation beside it](docs/screenshots/distributed.png) | ![A copy: where it came from, the exchange with the distributor, and the answer to write](docs/screenshots/distributed-copy.png) |

Each task in the API carries `access`, what the caller may do with it: `creator` (everything), `assignee`
(edit, complete, forward), `group` (read and take) or `viewer` (read).

## Task intervals, the workload warning and the team calendar

A task can have an **interval**: a `startDate` together with its `dueDate` says on which days it is worked on.
The start date is optional, must not be after the due date, and cannot be set without one. A task without a
start date occupies its due date alone; a task without a due date occupies no day.

**Workload warning.** Before a task is saved, the form asks how loaded the person it is for already is:
`GET /api/users/{id}/workload?from=YYYY-MM-DD&to=YYYY-MM-DD` returns `{ "count": n }`, the number of open tasks
that person is responsible for whose days overlap the period (`excludeTaskId` leaves out the task being edited).
When the count is above zero the interface shows, for example, "Maria Ionescu mai are 5 task-uri în perioada
14–16 nov.". It is a warning, not a block: the task can still be saved. Any signed-in user can ask for anyone's
count; only the number is returned, never the tasks. The period can cover at most 366 days.

**Team calendar.** In **Echipă**, the **Calendar** tab shows four weeks at a time:

- *Toată echipa* (everyone): a heat map with one row per person and one column per day. Each cell holds the
  number of open tasks occupying that day, shaded in fixed steps (1, 2, 3–4, 5+) so a number looks the same for
  every person. Selecting a cell lists that person's tasks for the day.
- One person: a calendar with their tasks named on each day.

It is drawn from `GET /api/team/calendar?from=…&to=…` (at most 92 days; managers and administrators, with the
same department rules as the workload screen), which returns each person's open tasks that have a due date and
touch the period, plus how many open tasks have no date and so cannot be placed. Completed tasks are not shown.

## Roles, departments and team workload

Every account has a **role** and, optionally, a **department**.

| Role      | Can do, on top of the role before it                                                    |
|-----------|-----------------------------------------------------------------------------------------|
| `user`    | Work with their own tasks and the tasks assigned to them                                |
| `manager` | Open **Echipă** (Team): the workload of the people who share their department           |
| `admin`   | Open **Utilizatori** (Users) to set roles and departments, and look at any department   |

**Team workload** (`GET /api/team`) lists each person in the department with their open tasks, overdue tasks,
tasks due today and in the next 7 days, open high-priority tasks, and tasks completed in the last 7 and 30 days,
plus department totals. A person is counted for the tasks they are responsible for: those assigned to them, and
their own that they have not handed to someone else. Pass `today=YYYY-MM-DD` so "overdue" follows the viewer's
date; a value more than a day away from the server's is ignored.

`GET /api/team/members/{id}/tasks` returns a member's tasks in full, notes and replies included, for reading.

**Task-urile echipei** (`GET /api/team/tasks`, optional `department` for administrators) lists every task the
department's people answer for: the open ones and those completed in the last two weeks. The interface groups them
by day (overdue, each day with tasks, no due date, recently completed) with a filter by person. A manager can **move
the due date** of such a task (`PUT /api/team/tasks/{taskId}/due-date` with `dueDate`, or `null` for none), from that
list or from **Echipă**: nothing else about the task changes. Not before the task's start date, not on a completed
task, and only within the manager's department (administrators: any). The person responsible is emailed ("Termen
schimbat"), and the change is kept in the task's conversation.
It does not give the manager access to the tasks themselves: they cannot edit, complete or delete them.

A manager can only name their own department (`403` otherwise) and a manager without a department sees nobody.
Roles and departments are read from the database on every request, so a change applies at once, without
signing in again.

Where roles and departments come from:

- **Administrators** set them in the interface (`PUT /api/admin/users/{id}`). An administrator cannot change
  their own role.
- **The first administrator** is named in configuration: `Auth__AdminEmails__0=you@example.com` (more with
  `__1`, `__2`, …). Those accounts are always administrators.
- **Active Directory**, for domain accounts, at every sign-in: the department is read from the `department`
  attribute (when the user has one), and members of `ManagerGroupDn` / `AdminGroupDn` become managers /
  administrators. A configured group also takes the role away from people who are not in it. What the directory
  does not speak for is left as set in Taskel.

## System settings (administrators)

Administrators set up the integrations in the interface, under **Organizare → Setări sistem**, without editing
configuration files or restarting the server:

| Panel | What it sets | Test button |
|---|---|---|
| **Server de email (SMTP)** | Server, port, user, password, sender, TLS; whether notifications are sent; the address of the interface | Sends a test message to your own address |
| **Active Directory (LDAP)** | Everything in [Active Directory sign-in](#active-directory-sign-in) | Signs in with a domain account you type and shows what Taskel would read (email, name, department, role, groups); creates nothing |
| **Keycloak** | Everything in [Keycloak sign-in](#keycloak-sign-in) | Reads the realm's discovery document and tries the client secret and the user list; a separate button imports the users |
| **Fișiere atașate** | The largest file accepted | – |

![Setări sistem: the Keycloak panel with the realm address, client, roles, the addresses to register in Keycloak and the result of an import](docs/screenshots/system-settings.png)

How the settings work:

- **Starting values** come from the server configuration (appsettings, environment variables), as documented in
  each section. Saving a panel replaces that integration's configuration as a whole, at once and for everyone.
  **Revino la configurația serverului** forgets what was saved.
- **Secrets** (the SMTP password, the Keycloak client secret) are never sent back to the browser. Left empty they
  keep the current one; **Șterge** drops the saved one, so the configured one (e.g. from an environment variable)
  applies again. They are stored encrypted with Data Protection, apart from the other values
  (table `integration_settings`).
- **Tests use the form**, not what is saved, so a change can be checked before it is saved.
- **Several servers** sharing a database pick up a change within a minute; the one that saved it, at once.
- Only administrators can read or change these settings (`403` otherwise). The tests reach the servers the form
  names, so they are rate limited like sign-ins.

## Active Directory sign-in

Users can sign in with their domain account. Taskel checks the password by binding to Active Directory over
LDAP as that user, so no service account is needed. Local (email + password) accounts keep working.

| Setting           | Environment variable                 | Notes                                                    |
|-------------------|--------------------------------------|----------------------------------------------------------|
| `Enabled`         | `ActiveDirectory__Enabled`           | `true` to turn it on                                     |
| `Host`            | `ActiveDirectory__Host`              | A domain controller, e.g. `dc1.corp.example.com`         |
| `Port`            | `ActiveDirectory__Port`              | `636` (default)                                          |
| `Security`        | `ActiveDirectory__Security`          | `Ldaps` (default), `StartTls` (port 389) or `None`       |
| `Domain`          | `ActiveDirectory__Domain`            | DNS name of the domain, e.g. `corp.example.com`          |
| `BaseDn`          | `ActiveDirectory__BaseDn`            | Where users live, e.g. `DC=corp,DC=example,DC=com`       |
| `RequiredGroupDn` | `ActiveDirectory__RequiredGroupDn`   | Optional. Only direct members of this group may sign in  |
| `DepartmentAttribute` | `ActiveDirectory__DepartmentAttribute` | Default `department`                               |
| `ManagerGroupDn`  | `ActiveDirectory__ManagerGroupDn`    | Optional. Direct members are managers in Taskel          |
| `AdminGroupDn`    | `ActiveDirectory__AdminGroupDn`      | Optional. Direct members are administrators in Taskel    |
| `SyncGroups`      | `ActiveDirectory__SyncGroups`        | Default `true`. Users' groups become groups in Taskel    |
| `GroupsBaseDn`    | `ActiveDirectory__GroupsBaseDn`      | Optional. Only groups under this container are brought in, e.g. `OU=Teams,DC=corp,DC=example,DC=com` |
| `TimeoutSeconds`  | `ActiveDirectory__TimeoutSeconds`    | Default 10                                               |

All of these can also be set by an administrator in **Setări sistem → Active Directory**, which replaces the
configuration (see [System settings](#system-settings-administrators)).

Related settings in the `Auth` section: `Auth__AllowLocalRegistration` (`false` stops new local accounts) and
`Auth__LoginAttemptsPerMinute` (default 10 per client address; further attempts get `429`).

How it behaves:

- Users type `ion.popescu`, `CORP\ion.popescu` or `ion.popescu@corp.example.com`.
- The first sign-in creates the Taskel account from the directory entry: email (`mail`, falling back to the
  logon address), display name, and `objectGUID`, which links the account from then on. Later sign-ins follow
  changes of name or email.
- A local account with the same email is taken over by the directory account: it keeps its tasks and its local
  password stops working. Directory accounts cannot sign in with the local form.
- Disabled or locked accounts, wrong passwords and empty passwords are refused (`401`). If the directory
  cannot be reached the answer is `503`.

Things to know:

- **TLS.** The domain controller's certificate must be trusted by the machine running Taskel and must match
  `Host`. Use `None` only in a lab: it sends passwords in clear text.
- **Lockout.** Failed sign-ins count against the account in Active Directory. The rate limit is per client
  address; behind a reverse proxy configure forwarded headers, or every user shares one address.
- **Groups.** `RequiredGroupDn`, `ManagerGroupDn` and `AdminGroupDn` check direct membership (`memberOf`), not
  nested groups.
- **Groups for tasks.** Taskel has no service account, so it cannot list the directory. It learns a group
  when one of its members signs in, and shows as members only the people who have signed in to Taskel. A
  change of membership in the directory takes effect at that person's next sign-in. Only direct membership
  counts, and the primary group (usually Domain Users) is not included. Without `GroupsBaseDn` every group a
  user belongs to is brought in, so set it to keep the list to the groups that make sense for tasks.
- **Leaving.** An account removed from the directory can no longer sign in, but an open session lasts until
  its cookie expires, and its Taskel account and tasks remain.

## Keycloak sign-in

Taskel can sign people in through [Keycloak](https://www.keycloak.org/) with OpenID Connect: the sign-in screen
shows a **Conectează-te cu …** button, Keycloak asks for the password (and any second factor it is set up for),
and the browser comes back signed in to Taskel. Local and domain accounts keep working alongside.

The step-by-step setup of the Keycloak client, and a ready-made development realm, are in
[docs/KEYCLOAK.md](docs/KEYCLOAK.md).

| Setting           | Environment variable              | Notes                                                       |
|-------------------|-----------------------------------|-------------------------------------------------------------|
| `Enabled`         | `Keycloak__Enabled`               | `true` to show the button                                   |
| `Authority`       | `Keycloak__Authority`             | The realm, e.g. `https://sso.example.com/realms/company`    |
| `ClientId`        | `Keycloak__ClientId`              | e.g. `taskel`                                               |
| `ClientSecret`    | `Keycloak__ClientSecret`          | For a confidential client (needed for the user import). Never in appsettings.json |
| `DisplayName`     | `Keycloak__DisplayName`           | Text on the button. Default `Keycloak`                      |
| `RequireHttpsMetadata` | `Keycloak__RequireHttpsMetadata` | Default `true`. `false` only for a test Keycloak on http:// |
| `RequiredRole`    | `Keycloak__RequiredRole`          | Optional. Only users with this realm or client role may sign in |
| `AdminRole`       | `Keycloak__AdminRole`             | Optional. Users with this role are administrators in Taskel, others are not |
| `ManagerRole`     | `Keycloak__ManagerRole`           | Optional. Users with this role are managers in Taskel, others are not |
| `DepartmentClaim` | `Keycloak__DepartmentClaim`       | Default `department`. The token claim (and, for the import, the user attribute) with the department |
| `SyncGroups`      | `Keycloak__SyncGroups`            | Default `false`. The user's Keycloak groups become groups in Taskel |
| `GroupsClaim`     | `Keycloak__GroupsClaim`           | Default `groups`                                            |

The same values can be set in **Setări sistem → Keycloak**, which also shows the two addresses to register on
the Keycloak client: `…/api/auth/keycloak/callback` (valid redirect URI) and the interface's home page (valid post
logout redirect URI). They are built from the interface's address set in the email panel
(`Email__Notifications__PublicUrl`), or from the address the request came to.

How it behaves:

- **Code flow with PKCE**, the code returned in the query string. Taskel keeps its own session cookie afterwards;
  Keycloak's tokens are not kept, except the ID token, to end the Keycloak session at sign-out.
- **First sign-in** creates the Taskel account (linked by Keycloak's user id, `sub`), with the email, the name and
  the department. Later sign-ins follow changes, including a changed email when Keycloak has verified it.
- **An existing local account** with the same email is taken over (it keeps its tasks; its password stops
  working) only when Keycloak says the address is **verified**. Otherwise anyone able to set their own email in
  Keycloak could take over any local account; the sign-in is refused with a message instead. Accounts of Active
  Directory or of another Keycloak user are never taken over, and a Keycloak account is never taken over by an
  Active Directory sign-in.
- **Roles** are read from the realm roles and from the client's roles in the access token. A configured role both
  grants and withdraws its Taskel role at every sign-in; without one, roles are set in Taskel as usual.
- **Groups**, when switched on, come from the groups claim (Keycloak's *Group Membership* mapper); `/Sales/Offers`
  becomes a Taskel group named *Offers*. As with Active Directory, members appear as they sign in.
- **Signing out** of Taskel also ends the Keycloak session (the browser passes through Keycloak and comes back),
  so the next sign-in asks for the password again.
- **Import.** **Importă utilizatorii** in the settings creates or updates an account for every enabled user of
  the realm, so tasks can be given to people before their first sign-in. It uses the client's service account,
  which needs the `view-users` role of the `realm-management` client. Roles and groups are applied at each
  person's first sign-in. Users without an email, and local accounts whose address is not verified in Keycloak,
  are skipped and listed.
- A refused sign-in comes back to the sign-in screen with the reason in Romanian (no email, address in use,
  address not verified, missing role, cancelled).

`RealDirectoryTests` run the LDAP code against a real directory when `TASKEL_TEST_AD_HOST` is set (see the
class comment for the accounts it expects); they were developed against a Samba AD domain controller and are
skipped otherwise, including in CI.

## Dashboard and reminders

The app opens on **Acasă** (Home), a dashboard built from widgets: at-a-glance counts, overdue, due today, next 7 days,
email tasks waiting for a reply, tasks delegated to others, open tasks by priority, tasks completed over the last 7 days, tags, and recently
completed.

- **Personalizează** (Customize, top right) lets each user remove, add and reorder widgets, or reset to the default layout.
  Changes are saved to the user's account as they are made (`PUT /api/dashboard`), so they follow the user
  across browsers.
- When the app is opened and there are tasks that are overdue or due today, a reminder dialog lists them, with
  shortcuts to the overdue and today views. It is shown once per visit and can be turned off under Personalizează.
- Preferences are `{ "widgets": ["stats", "overdue", …] | null, "showDueAlert": true }`. `null` means the default
  layout. Widget ids are defined by the frontend (`frontend/src/lib/dashboard.ts`); the API only checks their shape.
- "Completed, last 7 days" counts by completion time, which is recorded since the completion-reply feature; tasks
  completed before that are not counted.

## Completing email tasks with a reply

A task created from an email remembers who sent that email. Such a task can only be completed by writing a
response:

- `POST /api/tasks/{id}/complete` with `{ "response": "…" }` (up to 4000 characters). In the app, open the task,
  write the response at the bottom and press **Trimite și finalizează**.
- Taskel emails the response as a reply to the original email (`Re:` subject, `In-Reply-To` and
  `References` headers), so it shows up in the same conversation. The task's creator replies to the email's
  sender; anyone else who completes it (an assignee) replies to the creator, who brought the email into Taskel.
  Tasks expose the address as `replyTo`.
- A task that returns to whoever forwarded it (see [Forwarding](#forwarding)) sends no email on the way back;
  the reply goes out when it is finally completed.
- The task is marked as completed only after the SMTP server has accepted the reply. If sending fails
  (`502`) or email is not configured (`503`), the task stays open.
- Setting `isCompleted: true` through `PUT` is rejected for these tasks. Reopening through `PUT` is allowed;
  completing again needs a new response and sends a new reply.
- Tasks added by hand, and email tasks without a usable sender address, complete without a response.

**What the reply contains.** It is sent as text and HTML, like a mail thread:

- **To a colleague** (the person who brought the email from Outlook, when someone else closes the task): who closed
  it, the answer, the files sent with answers, then **the history in Taskel** (who brought the email in and to whom,
  instructions, forwards and returns, distribution, questions, answers, sending back, in order, in Romanian time),
  and the original email quoted.
- **To the email's sender** (when the person who brought it in closes it): the answer, the files attached to this
  answer only, and their own email quoted. Nothing internal (history, colleagues' answers or files) goes outside.

**Files with the answer.** Whoever answers can attach files to the answer: **Atașează la răspuns**, next to the
answer field (`attachmentIds` in `complete`, files of the task). They are marked **la răspuns** in the task's files.
On a distributed copy they go, with the answer, to the distributor's task and show under the answer in the
conversation. The reply carries them, up to 20 MB in all; larger files are named in the reply and stay in Taskel.

| | |
|---|---|
| ![Answer field with a file attached to the answer](docs/screenshots/answer-files.png) | ![The reply in a mail program: the answer, the history in Taskel and the original email](docs/screenshots/reply-history.png) |

### Email (SMTP) configuration

Replies and notifications are sent from one mailbox, configured on the server in the `Email:Smtp` section, or by
an administrator in **Setări sistem → Server de email**, which replaces it (see
[System settings](#system-settings-administrators)):

| Setting       | Environment variable         | Notes                                                    |
|---------------|------------------------------|----------------------------------------------------------|
| `Host`        | `Email__Smtp__Host`          | e.g. `smtp.gmail.com`                                    |
| `Port`        | `Email__Smtp__Port`          | `587` (STARTTLS, default) or `465` (TLS)                 |
| `Username`    | `Email__Smtp__Username`      | Usually the full email address                           |
| `Password`    | `Email__Smtp__Password`      | For Gmail, an [app password](https://support.google.com/accounts/answer/185833), not the account password |
| `FromAddress` | `Email__Smtp__FromAddress`   | Optional; defaults to `Username`                         |
| `FromName`    | `Email__Smtp__FromName`      | Optional display name                                    |
| `RequireTls`  | `Email__Smtp__RequireTls`    | Default `true`. `false` only for a local test server without TLS |

Keep the password out of `appsettings.json`. For local development use user secrets:

```bash
cd backend/src/Taskel.Api
dotnet user-secrets init
dotnet user-secrets set "Email:Smtp:Host" "smtp.gmail.com"
dotnet user-secrets set "Email:Smtp:Username" "you@gmail.com"
dotnet user-secrets set "Email:Smtp:Password" "your-app-password"
```

Things to know:

- All users' replies are sent from this one mailbox. A recipient who answers the reply writes to that mailbox.
- Sent replies are not saved to a "Sent" folder by Taskel (Gmail keeps its own copy of mail sent through its SMTP server).
- Any signed-in user can make the server send one email per completed email task, to the address stored on that
  task. Do not expose a server with open registration and SMTP configured to the internet.

## File attachments

Tasks can carry files, whether they were made in Taskel or from an email.

- **In the interface:** pick files in the new-task dialog (**Atașează fișiere**), or, on an existing task, open
  **Fișiere** in its dialog. Uploads show their progress.
- **From Outlook:** the macro sends the files attached to each email along with it. Pictures shown inside the
  message, such as signature logos, are left out.
- **Who can do what:** everyone who can see a task can download its files, including the members of a group it
  waits on, someone who forwarded it, and the manager of the person responsible. Its creator and its assignee
  add and remove files.
- Deleting a task deletes its files.

### Viewing PDF files and pictures

PDF files and pictures (PNG, JPEG, GIF, WEBP) open in a viewer over the task, without being downloaded: click the
file's name. The viewer goes to the previous and next file (also ← →), zooms pictures (+, −, 0 to fit, or a click on
the picture), and has a download button. PDF files open in the browser's own viewer. Every other file is
downloaded when clicked, as before.

The API shows a file only when its first bytes prove it is one of those types (`?view=true`), whatever its name or
the type its uploader declared; anything else, SVG included (it can carry scripts), gets `415` and stays
download-only. Shown files are sent inline with `X-Content-Type-Options: nosniff`, `Cross-Origin-Resource-Policy:
same-origin` and a `Content-Security-Policy` that keeps them from being framed by other sites; pictures are also
sandboxed. The browser's PDF viewer does not start in a sandbox, so PDF files are not: they are shown by the
browser, never run as a page of Taskel. Large PDF files are read in parts (HTTP range requests).

| | |
|---|---|
| ![Files of a task: an eye before files that can be viewed, and a download button](docs/screenshots/attachments-list.png) | ![A picture in the viewer, with zoom, download, previous and next](docs/screenshots/attachment-viewer.png) |

### Limits and settings

| Setting           | Environment variable             | Notes                                                    |
|-------------------|----------------------------------|----------------------------------------------------------|
| `MaxFileSizeMb`   | `Attachments__MaxFileSizeMb`     | Default `100`. Largest file accepted, in megabytes       |
| `MaxFilesPerTask` | `Attachments__MaxFilesPerTask`   | Default `20`                                             |
| `Path`            | `Attachments__Path`              | Default `data/attachments`, under the application folder |

An administrator can change the size limit from the interface, under **Setări sistem → Fișiere atașate** (1 to
2048 MB). A value set there is stored in the database, applies at once and wins over `MaxFileSizeMb`.

Things to know:

- **Where files live.** On the server's disk, in `Path`, under generated names; the original name is kept only
  in the database. Back this folder up together with the database, and put it on a volume that survives a
  redeploy (in a container, mount it).
- **Disk use is not capped overall.** The limits are per file and per task. Any signed-in user can fill the
  disk given enough tasks, so watch free space on servers open to many people.
- **Reverse proxies have their own limit.** nginx, for example, refuses bodies over 1 MB unless
  `client_max_body_size` is raised. Set it at least as high as the limit here.
- **Downloads are never displayed.** Every file is sent as a download of unspecified type, with
  `X-Content-Type-Options: nosniff`, whatever was uploaded, so an uploaded HTML page cannot run as part of
  Taskel. Files are not scanned for viruses.
- The limit is checked while the file arrives: an oversized upload is cut off and nothing is kept. Files are
  streamed to disk, not held in memory.
- An upload is one request. A file that takes longer than the proxy's or browser's patience fails and has to
  be sent again from the start.

## Email notifications for received tasks

When a task reaches someone, Taskel emails them at their account's address. It uses the same mailbox as the
replies ([Email (SMTP) configuration](#email-smtp-configuration)); without SMTP configured nothing is sent.

An email goes out when:

- a task is created for, or reassigned to, another user: to that user;
- a task is forwarded: to the new assignee, with the forwarder's message;
- a forwarded task comes back: to the forwarder, with the resolution;
- an email is given to people from Outlook: to each of them, with the instructions;
- a task is given or forwarded to a group: to every member of the group.

The person who did it is never emailed, and neither is anyone for a task they keep for themselves. Taking a
group task, handing it back, editing and completing are not announced.

The email is plain text, in Romanian, with the task's title, due date or interval, priority (when not medium)
and, if set, a link to Taskel. The task's notes are not included.

Each user can turn the emails off under **Integrări → Notificări pe email** (`GET`/`PUT /api/notifications`).
They are on by default, including for existing accounts.

| Setting     | Environment variable                 | Notes                                                    |
|-------------|--------------------------------------|----------------------------------------------------------|
| `Enabled`   | `Email__Notifications__Enabled`      | Default `true`. `false` stops notifications for everyone |
| `PublicUrl` | `Email__Notifications__PublicUrl`    | Optional. Address of the Taskel interface, added to the emails as a link |

Things to know:

- Notifications are sent in the background after the action has succeeded. If the mail server refuses one, it
  is logged and dropped: the task is still assigned, and nothing is retried.
- Emails waiting to be sent are held in memory (up to 2000) and are lost if the server restarts.
- Local accounts' addresses are not verified. With open registration, someone could register with another
  person's address and have tasks assigned to it, making Taskel email that person. On a server reachable from
  the internet, set `Auth__AllowLocalRegistration=false` or `Email__Notifications__Enabled=false`.

## Database (EF Core + PostgreSQL)

- `TaskelDbContext` lives in `backend/src/Taskel.Api/Data/`, migrations in `Data/Migrations/`.
- Connection string: `ConnectionStrings:Taskel` (Development value in `appsettings.Development.json`).
  In other environments set it via the `ConnectionStrings__Taskel` environment variable.
- Migrations run on startup in Development. Elsewhere set `Database__MigrateOnStartup=true`, or apply them explicitly:

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Name> -p src/Taskel.Api -o Data/Migrations   # after changing the model
dotnet ef database update -p src/Taskel.Api                             # apply to the database
```

## Tests

Backend tests run against a real PostgreSQL database `taskel_test` (recreated on each run).
Override with the `TASKEL_TEST_CONNECTION` environment variable.

```bash
cd backend && dotnet test
cd frontend && npm run lint && npm run build
```
