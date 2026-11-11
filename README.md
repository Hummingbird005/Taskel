# Taskel

Task management app with an **ASP.NET Core (.NET 10)** backend and a **React + Vite + TypeScript** frontend (Node.js).
The interface is in Romanian; code, API and documentation are in English.


![Taskel dashboard: at-a-glance counts, overdue, due today, upcoming, priority and weekly completion widgets](docs/screenshots/dashboard.png)

## Screenshots

| | |
|---|---|
| **Reminder on opening the app**, when tasks are overdue or due today<br>![Reminder dialog listing overdue and due-today tasks](docs/screenshots/reminder.png) | **Task list**, grouped by due date, with tags as channels<br>![Task list grouped under Today, Tomorrow, Later and No due date](docs/screenshots/tasks.png) |
| **Customizable dashboard**: add, remove and reorder widgets<br>![Dashboard in customize mode with move and remove controls](docs/screenshots/customize.png) | **Reply on completion** for tasks created from emails<br>![Response form shown when completing an email task](docs/screenshots/reply.png) |
| **Dark theme**<br>![Dashboard in the dark theme](docs/screenshots/dashboard-dark.png) | **Mobile**<br><img src="docs/screenshots/mobile-dashboard.png" alt="Dashboard on a phone" width="48%"> <img src="docs/screenshots/mobile-tasks.png" alt="Task list on a phone" width="48%"> |

Screenshots use sample data.

## Project layout

```
Taskel/
├── backend/                     # ASP.NET Core Web API (.NET 10)
│   ├── Taskel.slnx
│   ├── src/Taskel.Api/          # API: controllers, models, services
│   └── tests/Taskel.Api.Tests/  # xUnit integration tests
├── frontend/                    # React + Vite + TypeScript
├── integrations/outlook/        # Outlook macro: turn emails into tasks
└── .github/workflows/ci.yml     # Build + test both projects
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js](https://nodejs.org/) 22+
- PostgreSQL 16+ — easiest via Docker: `docker compose up -d` (uses `docker-compose.yml`)

## Running locally

**Database**:

```bash
docker compose up -d          # PostgreSQL on localhost:5432 (user/password/db: taskel)
```

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
For other setups, see `frontend/.env.example` and `Cors:AllowedOrigins` in `backend/src/Taskel.Api/appsettings.json`.

## API

| Method | Route                | Auth | Description                                   |
|--------|----------------------|------|-----------------------------------------------|
| GET    | `/api/health`        | –    | Health check                                  |
| POST   | `/api/auth/register` | –    | Create an account and sign in                 |
| POST   | `/api/auth/login`    | –    | Sign in                                       |
| POST   | `/api/auth/logout`   | –    | Sign out                                      |
| GET    | `/api/auth/me`       | ✓    | Current user                                  |
| GET    | `/api/auth/tokens`   | session | List your API tokens                       |
| POST   | `/api/auth/tokens`   | session | Create an API token (shown once)           |
| DELETE | `/api/auth/tokens/{id}` | session | Revoke an API token                     |
| GET    | `/api/dashboard`     | ✓    | Your dashboard preferences                    |
| PUT    | `/api/dashboard`     | ✓    | Save your dashboard preferences               |
| GET    | `/api/tasks`         | ✓    | List your tasks                               |
| GET    | `/api/tasks/{id}`    | ✓    | Get a task                                    |
| POST   | `/api/tasks`         | ✓    | Create a task                                 |
| POST   | `/api/tasks/from-email` | ✓ | Create a task from an email                   |
| PUT    | `/api/tasks/{id}`    | ✓    | Update a task                                 |
| POST   | `/api/tasks/{id}/complete` | ✓ | Complete a task, with a response (required for email tasks) |
| DELETE | `/api/tasks/{id}`    | ✓    | Delete a task                                 |

✓ = browser session or API token. "session" = browser session only.

### Task fields

| Field         | Type                          | Notes                                                              |
|---------------|-------------------------------|--------------------------------------------------------------------|
| `title`       | string, 1–200 chars           | Required                                                           |
| `description` | string, up to 2000 chars      | Optional                                                           |
| `isCompleted` | boolean                       | Set through `PUT`                                                  |
| `dueDate`     | `"YYYY-MM-DD"` or `null`      | A calendar date, no time or time zone                              |
| `priority`    | `"low"`, `"medium"`, `"high"` | Defaults to `"medium"`                                             |
| `tags`        | array of strings              | Up to 10 tags of up to 30 chars; trimmed, lower-cased, de-duplicated |
| `sourceEmailAddress` | string or `null`       | Read-only. Sender of the email the task came from                  |
| `completionResponse` | string or `null`       | Read-only. The response given when the task was completed          |
| `completedAt` | timestamp or `null`           | Read-only                                                          |

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
  - The cookie is encrypted with ASP.NET Core Data Protection keys. Persist them (e.g. a mounted volume) when running
    in containers or on several instances, otherwise users are signed out on every restart.
  - There is no rate limiting on login/register yet.

### API tokens

Clients that cannot use the browser session send a personal API token instead:

```
Authorization: Bearer tsk_…
```

- Create and revoke tokens in the app under **Integrări** (Integrations; up to 10 per user). A token is shown once;
  only its SHA-256 hash is stored.
- A token gives access to its owner's tasks. It cannot list, create or revoke tokens.

## Outlook integration

`integrations/outlook` contains a VBA macro for classic Outlook on Windows that sends the selected emails to
`POST /api/tasks/from-email`; each becomes a task tagged `email`, with the subject as title and the sender,
date and body as notes. See [integrations/outlook/README.md](integrations/outlook/README.md) for setup.

## Dashboard and reminders

The app opens on **Acasă** (Home), a dashboard built from widgets: at-a-glance counts, overdue, due today, next 7 days,
email tasks waiting for a reply, open tasks by priority, tasks completed over the last 7 days, tags, and recently
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

- `POST /api/tasks/{id}/complete` with `{ "response": "…" }` (up to 4000 characters). In the app, ticking the
  task opens the response form.
- Taskel emails the response to the sender as a reply to the original email (`Re:` subject, `In-Reply-To` and
  `References` headers), so it shows up in the same conversation.
- The task is marked as completed only after the SMTP server has accepted the reply. If sending fails
  (`502`) or email is not configured (`503`), the task stays open.
- Setting `isCompleted: true` through `PUT` is rejected for these tasks. Reopening through `PUT` is allowed;
  completing again needs a new response and sends a new reply.
- Tasks added by hand, and email tasks without a usable sender address, complete without a response.

### Email (SMTP) configuration

Replies are sent from one mailbox, configured on the server in the `Email:Smtp` section:

| Setting       | Environment variable         | Notes                                                    |
|---------------|------------------------------|----------------------------------------------------------|
| `Host`        | `Email__Smtp__Host`          | e.g. `smtp.gmail.com`                                    |
| `Port`        | `Email__Smtp__Port`          | `587` (STARTTLS, default) or `465` (TLS)                 |
| `Username`    | `Email__Smtp__Username`      | Usually the full email address                           |
| `Password`    | `Email__Smtp__Password`      | For Gmail, an [app password](https://support.google.com/accounts/answer/185833), not the account password |
| `FromAddress` | `Email__Smtp__FromAddress`   | Optional; defaults to `Username`                         |
| `FromName`    | `Email__Smtp__FromName`      | Optional display name                                    |

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
