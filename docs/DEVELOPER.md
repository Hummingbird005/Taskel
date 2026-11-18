# Taskel for developers

How the code is organised, how a request travels through it, and where to make the common kinds of
change. For what Taskel does and how to configure it, see the [README](../README.md); for sample accounts, see
[DEMO-ACCOUNTS.md](DEMO-ACCOUNTS.md).

## The pieces

```
Browser ──► React app (frontend/)
              │  fetch /api/…  (cookie session)
              ▼
Outlook ──► ASP.NET Core API (backend/src/Taskel.Api) ──► PostgreSQL (EF Core)
 macro        │  Bearer tsk_… (API token)                ├─► disk: attachment files
              │                                          ├─► SMTP: replies, notifications
              │                                          ├─► Active Directory (LDAP sign-in)
              │                                          └─► Keycloak (OpenID Connect sign-in, user import)
```

- **Backend**: one ASP.NET Core 10 project with controllers, EF Core 10 on PostgreSQL. No separate service
  layer for most features: controllers read and write through the `DbContext` or `ITaskRepository`.
- **Frontend**: React 19 + TypeScript, built with Vite. No router and no state library: one `Workspace`
  component holds the signed-in state and switches views.
- **Outlook macro**: `integrations/outlook/TaskelOutlook.bas`, VBA that calls the same API with a personal
  API token, and `TaskelSendForm.txt`, the code of an optional UserForm that asks who gets the emails. VBA cannot
  import a form without its binary `.frx`, so the user creates an empty form and pastes the code; its controls
  are built at run time, and the macro opens it late bound (`VBA.UserForms.Add`), falling back to the old
  behaviour when it is missing. Both files stay ASCII: Romanian text is written as `\uXXXX` and decoded by `U()`.

## Running and testing

See *Running locally* in the README. In short: `docker compose up -d`, then `dotnet run --project
src/Taskel.Api` in `backend/` and `npm run dev` in `frontend/`. In Development the API migrates the
database and adds the [demo data](DEMO-ACCOUNTS.md) on an empty database.

- `cd backend && dotnet test`: integration tests against a real PostgreSQL database `taskel_test`, which
  they drop and recreate. Set `TASKEL_TEST_CONNECTION` to point elsewhere.
- `cd frontend && npm run lint && npm run build`: oxlint and the TypeScript build. There are no frontend unit
  tests; the interface was checked in a browser.
- CI (`.github/workflows/ci.yml`) runs both on every push and pull request.
- CI also checks for vulnerable packages: `dotnet list package --vulnerable` for NuGet, and
  `node scripts/audit.mjs` in `frontend/` for npm (fails on high or critical advisories). The script lists the
  temporary exceptions, for advisories with no fixed release that do not reach the served application, and says
  when one is no longer needed. Remove an exception as soon as a fix exists.

## Backend

### Layout (`backend/src/Taskel.Api`)

| Folder | What is in it |
|---|---|
| `Program.cs` | All wiring: options, services, authentication, rate limiting, CORS, migrations and demo data on startup |
| `Controllers/` | One controller per area. `CurrentUser` reads the caller's account and role from the database on each request |
| `Models/` | EF entities (`TaskItem`, `User`, `Group`, `TaskForward`, `TaskAttachment`, …) and the request/response records next to them |
| `Data/` | `TaskelDbContext` (table and column mapping), `Migrations/`, and `DemoData` |
| `Services/` | `ITaskRepository` / `EfTaskRepository` (which tasks a user can reach), `EmailTaskFactory` (email → task) |
| `Auth/` | The API token authentication handler |
| `Directory/` | Active Directory sign-in over LDAP and the mapping of directory accounts, roles and groups |
| `Keycloak/` | Keycloak sign-in: the OpenID Connect handler's setup and events, reading the tokens, linking accounts, the admin API client for the test and the import, the sign-out address |
| `Integrations/` | The integration settings an administrator saves (`IntegrationSettings`), what Active Directory and Keycloak accounts share (`ExternalAccounts`: roles and groups), and the public address of the interface |
| `Email/` | SMTP sending, the notification queue and its background worker |
| `Attachments/` | Storing files on disk, the size limit, and download headers |
| `Validation/` | Attributes shared by request records (`SafeText`, `StrictEmail`, `DueDateRange`) |

### A request, start to finish

Take `POST /api/tasks` (create a task):

1. **Authentication** (`Program.cs`). A policy scheme picks the cookie handler, or the API token handler when
   the request has `Authorization: Bearer …`. Both produce a principal whose `NameIdentifier` claim is the
   user id; `User.GetUserId()` reads it.
2. **Binding and validation**. The body binds to `CreateTaskRequest` (`Models/TaskItem.cs`). JSON options in
   `Program.cs` refuse unknown properties and numbers for enums. Data annotations and
   `IValidatableObject.Validate` run; any error becomes a `400` with a problem-details body before the action
   runs.
3. **The action** (`TasksController.Create`) resolves who the task goes to, saves it through
   `ITaskRepository`, and queues a notification email (`TaskNotifier`) when someone else receives it.
4. **The response** is a `TaskResponse`, which includes the caller's `access` to the task.

### Who can do what with a task

This is the rule most features depend on, so it lives in two places only:

- `EfTaskRepository.VisibleTo(userId)`: the tasks a user can reach at all. Their own, assigned to them,
  waiting on one of their groups, or passed on by them earlier. Anything else is a `404`, never a `403`, so
  the API does not reveal that a task exists.
- `TasksController.AccessOf(task, userId, groups)`: what they may do with a task they can reach.

| `TaskAccess` | Who | May |
|---|---|---|
| `Creator` | The person who made it | Everything: edit, complete, assign to anyone or any group, delete |
| `Assignee` | The person it is assigned to now | Edit, complete, forward, hand back to its group |
| `Group` | A member of the group it waits on | Read it and take it (`claim`) |
| `Viewer` | Someone who forwarded it earlier | Read it |

Managers read their department's tasks through `TeamController`, which does its own department check and
returns them as `Viewer`. Their own tasks follow the table above like anyone else's.

**Delegated vs forwarded**, as the interface lists them:

- **Delegate** (delegated): `Creator` tasks given to someone else or to a group. The creator still owns them
  and keeps every right.
- **Direcționate** (forwarded): `Viewer` tasks, which the user received and passed on with
  `POST /api/tasks/{id}/forward`. They are no longer the user's; each step is kept as a `TaskForward` row and
  shown as the task's path.

**Returns.** A forward made with `returnToMe` has `ReturnPending`. `TaskItem.PendingReturn()` is the latest such
step not yet answered; when the assignee completes, `TasksController.Complete` hands the task back to that step's
sender instead (`ReturnAsync`): it adds a step with `IsReturn` and the message as its note, clears the pending
flag and notifies with `TaskNotificationReason.Returned`. Nested forwards unwind one step per completion.

**Who a completion reply goes to**: `TaskItem.ReplyAddressFor(access)`: the email's sender when the creator
completes, the creator's own address otherwise (an assignee's answer goes to whoever brought the email in).
`TaskResponse` carries it as `replyTo`, and `returnsTo` when completing sends the task back instead.

### Data model

- `users`: local and directory accounts. Local accounts have a password hash; directory accounts have a
  `DirectoryId` (the objectGUID) and no password.
- `tasks`: `UserId` is the creator and never changes. `IsCompleted`, and `WaitingReason` / `WaitingSince`
  while on hold, give the task's state (`TaskItem.State`). `AssigneeId` and `GroupId` say who has it: neither (the
  creator), a user, a group (waiting to be taken), or both (taken from a group, and can be handed back).
- `groups`, `group_members`: application groups, and Active Directory groups recorded at sign-in.
- `task_forwards`: each time a task was passed on (`ReturnPending` when its sender wants it back), and each
  return (`IsReturn`, with the resolution as the note).
- `task_attachments`: file metadata; the content is on disk under a generated name.
- `api_tokens`: hashed personal tokens for the Outlook macro and other clients.
- `app_settings`: single values an administrator changes from the interface (the attachment size limit).
- `integration_settings`: one row per integration (`email`, `directory`, `keycloak`) saved in **Setări sistem**:
  the settings as JSON, and the secret apart, encrypted with Data Protection.
- `data_protection_keys`: the Data Protection key ring (session cookies, those secrets).
- `users.KeycloakSubject`: Keycloak's id for the account (`sub`). An account is local, directory
  (`DirectoryId`) or Keycloak, never two at once; `User.IsExternal` tells the last two from a local one.

### Integration settings

The mail server, Active Directory and Keycloak are configured in `appsettings` / environment variables, and an
administrator can replace that configuration from the interface. `Integrations/IntegrationSettings` holds the
result in memory, because the sign-in handlers read it synchronously:

- `IntegrationDefaults` wraps the configured options; `IntegrationSettings.Smtp`, `.Notifications`,
  `.Directory` and `.Keycloak` are what is in effect (saved row, else configuration).
- Saving goes through `SaveEmailAsync` / `SaveDirectoryAsync` / `SaveKeycloakAsync`, which reload at once.
  `IntegrationSettingsRefresher` reloads every minute for the other servers. Whoever needs a value reads it
  from `IntegrationSettings` at the moment of use, never from `IOptions<…>`.
- A secret in a request means: null keeps the one in effect, empty drops the saved one, anything else replaces
  it (`ResolveSecret`). Responses never contain secrets, only whether one is saved or configured.
- The Keycloak OpenID Connect handler caches its options. `KeycloakOidcSetup` builds them from the current
  settings, and `ReloadAsync` drops the cache when the Keycloak settings change.

The admin endpoints are three small controllers (`EmailSettingsController`, `DirectorySettingsController`,
`KeycloakSettingsController`) on `IntegrationSettingsControllerBase`, which carries the route prefix, the
`[AdminOnly]` filter and the status every response includes.

### Keycloak sign-in, step by step

1. The button links to `GET /api/auth/keycloak/login`, which challenges the `Keycloak` scheme.
2. `KeycloakOidcEvents.RedirectToIdentityProvider` sets the callback from the public address and sends the
   browser to Keycloak (code flow, PKCE, `response_mode=query`).
3. Back on `/api/auth/keycloak/callback`, the handler redeems the code and validates the ID token.
   `TokenValidated` reads the account (`KeycloakClaims.Read`: ID token claims, roles from the access token),
   lets `KeycloakAccounts.SignInAsync` create or link it, then replaces the principal with Taskel's own (user id,
   email, `taskel:provider=Keycloak`) and keeps the ID token in the cookie's properties.
4. The cookie handler signs in; the browser goes to the interface. A refusal redirects to
   `/?loginError=<code>`, which `AuthForm` turns into a Romanian sentence.
5. `POST /api/auth/logout` on such a session answers with Keycloak's end-session address (`KeycloakLogout`),
   and the frontend goes there.

Tests drive this whole flow against `FakeKeycloak`, an `HttpMessageHandler` that serves discovery, signing keys,
the token endpoint and the admin user list (`IntegrationSettingsTests`, on a database of their own).

### Adding a migration

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Name> --project src/Taskel.Api -o Data/Migrations
```

Check the generated `Up` method, especially default values for new non-nullable columns on tables that already
have rows. The API applies migrations at startup in Development; elsewhere, set
`Database:MigrateOnStartup=true` or apply them as part of deployment.

### Adding an endpoint

1. Add the request and response records next to the entity in `Models/`, with validation attributes on the
   constructor parameters (`[property: …]` does not validate records). Use `SafeText` on free text.
2. Add the action to the controller of that area. Load tasks through `ITaskRepository.GetAsync` so the
   visibility rule applies, then check `AccessOf` for what the action needs.
3. Return `Problem(statusCode: …, title: …)` for refusals; the frontend shows `title`.
4. Add integration tests in `backend/tests/Taskel.Api.Tests`. Use `factory.CreateUserAsync()` for a signed-in
   client and the `ApiJson` helpers (`PostApiAsync`, `GetApiAsync`, …) so enums travel as strings. Join the
   shared collection with `[Collection(ApiCollection.Name)]`: all those tests share one API and one database,
   so make every test create its own users and data.
5. Document the route in the README's API table.

### Tests

- `TaskelApiFactory` starts the API in memory against `taskel_test`, with Active Directory replaced by
  `FakeDirectory` and SMTP by `FakeEmailSender`, and attachments in a temporary folder.
- `RealDirectoryTests` run only when `TASKEL_TEST_AD_HOST` names a real directory (they were written against a
  Samba AD domain controller).
- `DemoDataTests` use a database of their own, `taskel_test_demo`, so the seeded accounts do not mix with the
  other tests.

## Frontend

### Layout (`frontend/src`)

| Path | What is in it |
|---|---|
| `main.tsx`, `App.tsx` | Entry point; `App` checks the session and shows the sign-in screen or `Workspace` |
| `api/` | One module per API area. `client.ts` has `request()`, which sends the cookie and turns errors into `ApiError` with the server's message. Types here mirror the backend's responses |
| `components/Workspace.tsx` | The signed-in app: loads tasks, people, groups and settings, holds the current view, and owns every change to the task list |
| `components/` | Screens and pieces: `TaskRow`, `TaskForm`, `NewTaskDialog`, `TargetPicker`, `Team`, `Groups`, … |
| `components/ui/` | Shared building blocks: `Button`, `Banner`, `Panel`, `DataTable`, `EmptyState`, skeletons |
| `components/PageView.tsx` | The views that are pages rather than task lists: Integrări, Echipă, Grupuri, Utilizatori, Setări sistem |
| `components/settings/` | **Setări sistem**: one panel per integration, the shared fields (`SettingsFields`) and the form hook (`useSettingsForm`) |
| `lib/taskActions.ts` | Every change to a task from the workspace: the API call, the list update and the Romanian refusal |
| `lib/settingsValidation.ts` | Checks for the settings forms (server names, addresses, identifiers) |
| `lib/taskViews.ts` | Which tasks each view lists, their grouping, titles and empty messages |
| `lib/validation.ts` | Form checks that mirror the API's rules, with Romanian messages |
| `lib/theme.ts` | The theme list and how a theme is applied and remembered |
| `index.css` | All styles: tokens first, then components, then the themes |

### State and data flow

`Workspace` loads everything once, then keeps `tasks` in state. Every change goes through a function in
`lib/taskActions.ts` (`addTask`, `claimTask`, and the `taskActions(context, task)` given to the dialog) that calls
the API and replaces the changed task with the server's answer; the `context` carries the workspace's setters. Views are computed from `tasks` with `lib/taskViews.ts`; nothing else caches tasks. The list reloads
when the window regains focus, to pick up tasks added from Outlook.

Which list a task appears in follows its `access`:

- *Own work* (Toate task-urile, Azi, …): `access === 'assignee'`, or `creator` with nobody else on it.
- **Delegate**: `creator`, given to someone else or to a group.
- **De preluat**: `group`.
- **Direcționate**: `viewer`.

### Lists and the task dialog

A list renders each task as a `TaskRow`: a state mark (`StateMark`), the title and a line of facts. The title is
a button whose `::after` covers the row, so the whole row opens the task while staying one keyboard stop.

Opening a task sets `openId` in `Workspace`, which renders `TaskDetail` for the task with that id, looked up in
`tasks` on every render so the dialog always shows the latest version. `TaskDetail` gets a `TaskActions` object
from `taskActions(context, task)`: each action calls the API, replaces the task in `tasks` with the answer and
resolves to a Romanian error message or null. Without `actions` the dialog is read only, which is how forwarded
tasks and a manager's view of their people's tasks are shown.

The state buttons at the bottom of the dialog share one message field: it is the reason for **În așteptare**
(required), and the resolution for **Finalizat** (required, and emailed, for tasks from an email). The server's
rule for states is in `TaskItem.State` and the `wait`, `resume` and `complete` actions of `TasksController`.

### Adding a task, in the interface

The **Task nou** button in the header opens `NewTaskDialog`, a native `<dialog>` around `TaskForm`. On submit,
`addTask` creates the task, then uploads the picked files one by one. The same `TaskForm` edits a task from its
dialog (the pencil button).

Who a task goes to is chosen with `TargetPicker`: a search box (an ARIA combobox) over the people from
`GET /api/users` and the groups from `GET /api/groups`, matched without regard to case or diacritics. It lists
nothing until something is typed and shows at most eight matches. Its value is a user id, a group id with the
`group:` prefix, or empty for "Eu".

### Styles and themes

`index.css` defines every colour, size and space as a custom property on `:root`; components use only those
tokens. A theme is one more set of the same tokens:

- `:root[data-theme='dark']` (and the system dark setting when no theme is chosen),
- `:root[data-theme='frost']` and `:root[data-theme='amber']`, the glass themes. Their shared structure
  (translucent surfaces, blur, background shapes) is in the rules keyed on `[data-glass]`, which read their
  colours from tokens such as `--glass`, `--glass-field` and `--blob-1`.

`--menu-bg` and `--menu-shadow` colour everything that pops up: the native options of a `<select>`, the
search suggestions, autofilled fields. `color-scheme` is set per theme so date pickers and scrollbars follow
light or dark.

To add a theme: add its id and label to `THEMES` in `lib/theme.ts` (and to `GLASS_THEMES` if it is a glass
theme), add it to the list in the inline script in `index.html`, and define its tokens in `index.css`. Check
it on the dashboard, a task list, the new-task dialog with the search open, the team calendar and the users
screen.

### Conventions

- Text in the interface is Romanian; code, comments, API messages and documentation are English.
- Forms use `noValidate` and check with `lib/validation.ts` (or `lib/settingsValidation.ts`), so the messages are
  in Romanian; the API checks again.
- Status messages use `<output>`, errors `role="alert"`; groups of buttons a `<fieldset>` with a hidden legend.
- Keep functions small enough for SonarQube's cognitive complexity limit (15): pull pure helpers out of
  components, prefer `if`/`switch` in a helper to nested ternaries, and split long JSX into components.
- Anything clickable is a `<button>` or a link, with a visible label or an `aria-label`. Keyboard focus is
  always visible.
- No new runtime dependency without a reason that outweighs its weight; the app has three (React, React DOM,
  the Inter font).

## Outlook macro

`integrations/outlook/TaskelOutlook.bas` is plain ASCII with CRLF line endings, because the VBA editor imports
`.bas` files in the system code page (`.gitattributes` keeps git from changing the endings). It posts each
selected email to `POST /api/tasks/from-email`, then uploads its attachments to
`POST /api/tasks/{id}/attachments`. It cannot be run in CI; test changes in classic Outlook for Windows.
