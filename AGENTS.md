# AGENTS.md

A self-hosted, single-user Telegram reminder bot. You message it in plain
language ("call the bank tomorrow at 10"); it stores the task and messages
you back when it is due. Runs as one .NET 10 process against PostgreSQL.

## Commands

These are the commands to run before opening a pull request. `.github/workflows/ci.yml` runs
the restore, build, and both `dotnet test` commands below — the "Build and test" section — on
every pull request and every push to `main`, so a failing one there is caught by a machine, not
only by a human reading the diff. Nothing under "Run locally" or "Database migrations" runs
automatically.

### Prerequisites
- .NET 10 SDK
- Docker (for integration tests and for running locally)
- `cp .env.example .env` and fill it in (only needed to *run*, not to test)

### Build and test

```bash
dotnet restore
dotnet build --no-restore                       # warnings are errors
dotnet test tests/Assistant.UnitTests           # no Docker needed

docker compose -f compose.test.yaml up -d --build  # Postgres on :55432, WireMock on :58080
dotnet test tests/Assistant.IntegrationTests
docker compose -f compose.test.yaml down -v     # when finished
```

### Run locally

Container packaging for the worker (image build, secret delivery, restart policy) is not
yet in this repository — there is no `compose.yaml` and no worker Dockerfile, only
`compose.test.yaml`, which serves the test suite. To run locally: start a Postgres, then

```bash
DatabaseSettings__ConnectionString="Host=localhost;Port=<port>;Database=<db>;Username=<user>;Password=<password>" \
dotnet run --project src/Assistant.Worker
```

The Telegram bot token goes in user secrets, never in `appsettings.Development.json` (user
secrets live outside the repository tree entirely, a stronger guarantee than a gitignore rule
someone could override with `git add -f`); user secrets only load in `Development`, so commands
that need it require
`DOTNET_ENVIRONMENT=Development dotnet run --project src/Assistant.Worker -- send-test-message`.
The local connection string goes in `appsettings.Development.json`; a fresh clone has none, and
the worker will not start without one.

For a full walkthrough — stub and real Telegram, seeding a due reminder, and verifying it is
delivered at most once — see `docs/e2e-local.md`.

### Database migrations

```bash
dotnet ef migrations add <Name> \
  --project src/Assistant.Repository \
  --startup-project src/Assistant.Worker
```
The Worker applies them at startup by calling `MigrateAssistantDatabaseAsync` explicitly;
`AddAssistantRepository` itself never migrates.

## Project map

| Project | Contents | References |
| :--- | :--- | :--- |
| `Assistant.Models` | Table POCOs, no behaviour | nothing |
| `Assistant.Contracts` | Request/response types | nothing |
| `Assistant.Interfaces` | Every interface | Models, Contracts |
| `Assistant.Repository` | EF Core, DbContext, migrations | Interfaces, Models |
| `Assistant.Impl` | Services, jobs, adapters | Interfaces, Contracts, Models |
| `Assistant.Worker` | Composition root | everything |
| `Assistant.WireMock` | Stub API server (Telegram today) run as the `wiremock` service in `compose.test.yaml`, port 58080 | nothing |

`tests/Assistant.UnitTests/Architecture/` enforces this graph. If you change
a project reference and the build goes red, the graph is the thing that is
right and your change is the thing that is wrong.

## Conventions

See `docs/conventions.md`. In short: XML docs on every public member
(missing ones fail the build), every class with arguments uses a primary
constructor, mapping is extension methods named by destination, HTTP
clients are Refit interfaces, and no emoji anywhere in the repository.

## Do not

- Add a project reference from `Impl` to `Repository`. Services depend on
  `ITaskRepository` in `Interfaces`; `Worker` wires the implementation.
- Put behaviour on a type in `Models`. They are POCOs.
- Mutate a `ReminderTask` anywhere except `TaskService`. It is the single
  writer, and it is the only place the invariants live.
- Write a unit test for behaviour an integration test already covers.
- Use `HttpClient` directly. Write a Refit interface.
- Name a type `Task` or an enum `TaskStatus`.
- Declare a separate constructor. Use a primary constructor.
- Mark a reminder sent before it has actually been sent.
- Put an emoji anywhere: source, tests, docs, commit messages, or bot
  message text. Use a word.

## Design

`docs/design/slice-1-reminders.md` is the approved specification. Read the
relevant section before any structural change, and update it in the same
commit if the change alters a documented decision.
