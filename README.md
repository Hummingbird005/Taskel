# Taskel

Task management app with an **ASP.NET Core (.NET 10)** backend and a **React + Vite + TypeScript** frontend (Node.js).

```
Taskel/
├── backend/                     # ASP.NET Core Web API (.NET 10)
│   ├── Taskel.slnx
│   ├── src/Taskel.Api/          # API: controllers, models, services
│   └── tests/Taskel.Api.Tests/  # xUnit integration tests
├── frontend/                    # React + Vite + TypeScript
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

| Method | Route             | Description        |
|--------|-------------------|--------------------|
| GET    | `/api/health`     | Health check       |
| GET    | `/api/tasks`      | List tasks         |
| GET    | `/api/tasks/{id}` | Get a task         |
| POST   | `/api/tasks`      | Create a task      |
| PUT    | `/api/tasks/{id}` | Update a task      |
| DELETE | `/api/tasks/{id}` | Delete a task      |

The OpenAPI document is available at `/openapi/v1.json` in Development.

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
