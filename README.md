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

## Running locally

**Backend** (http://localhost:5000):

```bash
cd backend
dotnet run --project src/Taskel.Api
```

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

> Storage is currently in-memory (`InMemoryTaskRepository`) — data resets on restart. Swap in a database-backed `ITaskRepository` (e.g. EF Core) when ready.

## Tests

```bash
cd backend && dotnet test
cd frontend && npm run lint && npm run build
```
