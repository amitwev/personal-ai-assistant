# AGENTS.md

A self-hosted, single-user Telegram reminder bot. You message it in plain
language ("call the bank tomorrow at 10"); it stores the task and messages
you back when it is due. Runs as one .NET 10 process against PostgreSQL.

## Commands

Every command below is run by CI, so if one fails here it fails there too.

### Prerequisites
- .NET 10 SDK
- Docker (for integration tests and for running locally)
- `cp .env.example .env` and fill it in (only needed to *run*, not to test)

### Build and test

```bash
dotnet restore
dotnet build --no-restore                       # warnings are errors
dotnet test tests/Assistant.UnitTests           # no Docker needed

docker compose -f compose.test.yaml up -d       # Postgres on :55432
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down -v     # when finished
```

### Run locally

```bash
docker compose up -d          # Postgres + worker; migrations apply on startup
docker compose logs -f worker
```

### Database migrations

```bash
dotnet ef migrations add <Name> \
  --project src/Assistant.Repository \
  --startup-project src/Assistant.Worker
```
Migrations are applied automatically at startup by `AddAssistantRepository`.

## Project map

| Project | Contents | References |
| :--- | :--- | :--- |
| `Assistant.Models` | Table POCOs, no behaviour | nothing |
| `Assistant.Contracts` | Request/response types | nothing |
| `Assistant.Interfaces` | Every interface | Models, Contracts |
| `Assistant.Repository` | EF Core, DbContext, migrations | Interfaces, Models |
| `Assistant.Impl` | Services, jobs, adapters | Interfaces, Contracts, Models |
| `Assistant.Worker` | Composition root | everything |

`tests/Assistant.UnitTests/Architecture/` enforces this graph. If you change
a project reference and the build goes red, the graph is the thing that is
right and your change is the thing that is wrong.

## Conventions

See `docs/conventions.md`. In short: XML docs on every public member
(missing ones fail the build), mapping is extension methods named by
destination, HTTP clients are Refit interfaces.

## Do not

- Add a project reference from `Impl` to `Repository`. Services depend on
  `ITaskRepository` in `Interfaces`; `Worker` wires the implementation.
- Put behaviour on a type in `Models`. They are POCOs.
- Mutate a `ReminderTask` anywhere except `TaskService`. It is the single
  writer, and it is the only place the invariants live.
- Write a unit test for behaviour an integration test already covers.
- Use `HttpClient` directly. Write a Refit interface.
- Name a type `Task` or an enum `TaskStatus`.
- Mark a reminder sent before it has actually been sent.

## Design

`docs/design/slice-1-reminders.md` is the approved specification. Read the
relevant section before any structural change, and update it in the same
commit if the change alters a documented decision.
