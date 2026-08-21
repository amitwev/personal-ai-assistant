# Personal AI Assistant — Slice 1 (Reminders) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a self-hosted Telegram bot that captures tasks from natural language and reliably delivers proactive reminders, surviving process restarts.

**Architecture:** A single .NET 10 Worker Service long-polls Telegram, routes free text through an LLM to typed C# tools, and runs a 30-second scheduler that delivers due reminders and a daily brief. Six source projects enforce a one-directional reference graph: `Models` and `Contracts` depend on nothing, `Interfaces` holds every abstraction, `Repository` owns EF Core exclusively, `Impl` holds every implementation but cannot reach `Repository`, and `Worker` is the only composition root.

**Tech Stack:** .NET 10, PostgreSQL 16, EF Core, Npgsql, Telegram.Bot, Refit, Microsoft.Extensions.AI, Polly (via `Microsoft.Extensions.Http.Resilience`), Serilog, xUnit, Shouldly, WireMock.Net, Respawn, NetArchTest.

**Spec:** `docs/design/slice-1-reminders.md` (v2.0) — committed in Task 0. Read it alongside this plan; every task cites the section it implements.

---

## Global Constraints

Every task's requirements implicitly include this section.

- **Target framework:** `net10.0`. No multi-targeting.
- **Repository:** `personal-ai-assistant`, public on GitHub, MIT licensed, public from commit #1.
- **Nullable reference types:** enabled solution-wide. No `#nullable disable`.
- **Warnings are errors** solution-wide, including `CS1591` (missing XML comment) in `src/`. Test projects suppress `CS1591` only.
- **XML doc comments** on every public type and member in `src/`. Document the contract, never the implementation. Required tags: `<summary>`, `<param>`, `<returns>`, `<exception>`, `<value>`, `<typeparam>`. Use `<inheritdoc/>` on interface implementations. Spec §12.1.
- **Reference graph** — enforced by tests in Task 1, never to be violated:
  ```
  Models      →  (nothing)
  Contracts   →  (nothing)
  Interfaces  →  Models, Contracts
  Repository  →  Interfaces, Models
  Impl        →  Interfaces, Contracts, Models        ✗ never Repository
  Worker      →  everything
  ```
- **EF Core types appear only in `Assistant.Repository`.** Nothing else references `Microsoft.EntityFrameworkCore` or names `DbContext`.
- **No `HttpClient` used directly.** Every HTTP API this project calls itself is a Refit interface. Spec §12.3.
- **All mapping is extension methods** in `Impl/Mapping`, named by destination: `ToResponse()`, `ToModel()`, `ToRequest()`, `ToNotification()`. No mapping library. Spec §12.2.
- **All instants stored and compared in UTC.** `DateTimeOffset` with **zero offset** — Npgsql requires `Offset == TimeSpan.Zero` for `timestamptz` and throws otherwise. Convert to local only when rendering text for the user.
- **Timezone is `Asia/Jerusalem`, fixed.** Slice 1 does not make it configurable. Spec §5.2.
- **`ReminderTask` and `ReminderStatus`** — never name a type `Task` or an enum `TaskStatus`; both collide with `System.Threading.Tasks`.
- **Repositories return materialised results** — `IReadOnlyList<T>`, never `IQueryable<T>`.
- **`TaskService` is the only type that mutates a `ReminderTask`.** Jobs, tools, and button actions call `ITaskService`, never a repository.
- **Delivery is at-least-once:** send, then mark. Never mark, then send.
- **Telegram parse mode is HTML**, never MarkdownV2.
- **One test per behaviour, at the highest-fidelity level that can reach it.** If an integration test covers it, do not also unit-test it. Spec §7.2.
- **Commit after every task.** Conventional commit messages (`feat:`, `test:`, `chore:`, `docs:`, `fix:`).

### Assertion library note

The spec's §7.3 example uses FluentAssertions syntax. **Use Shouldly instead** — FluentAssertions v8 abandoned Apache licensing in January 2025 and requires a paid licence for commercial use, which is a poor fit for an MIT repository. Translation:

| FluentAssertions | Shouldly |
| :--- | :--- |
| `x.Should().Be(y)` | `x.ShouldBe(y)` |
| `list.Should().HaveCount(1)` | `list.Count.ShouldBe(1)` |
| `list.Should().Equal(a, b)` | `list.ShouldBe(new[] { a, b })` |
| `x.Should().BeNull()` | `x.ShouldBeNull()` |
| `act.Should().Throw<T>()` | `Should.Throw<T>(act)` |

---

## File Structure

### Repository root

| Path | Responsibility |
| :--- | :--- |
| `AGENTS.md` | Every command needed to build, test, and run. Entry point for AI agents and new contributors. |
| `CLAUDE.md` | One line pointing at `AGENTS.md`. Never duplicates it. |
| `README.md` | Problem statement, four-step quickstart, the single-user and single-timezone limitations stated plainly. |
| `LICENSE` | MIT. |
| `.gitignore` | .NET template. Must exclude `.env`, `bin/`, `obj/`. |
| `.env.example` | Three keys with empty values. The documented starting point. |
| `.editorconfig` | Formatting and naming rules. |
| `Directory.Build.props` | `net10.0`, nullable, warnings-as-errors, `GenerateDocumentationFile`. |
| `Directory.Packages.props` | Central package version management. |
| `tests/Directory.Build.props` | Inherits root, suppresses `CS1591`. |
| `compose.yaml` | Production: Postgres 16 + worker. |
| `compose.test.yaml` | Integration tests: Postgres 16 only, fixed port 55432. |
| `.github/workflows/ci.yml` | Build, arch tests, unit tests, integration tests, gitleaks. |
| `docs/design/slice-1-reminders.md` | The spec. |
| `docs/conventions.md` | Spec §12 extracted for contributors. |

### `src/Assistant.Models`

| File | Responsibility |
| :--- | :--- |
| `ReminderTask.cs` | Task POCO. Public setters, no methods. |
| `ChatMessage.cs` | Conversation-window POCO. |
| `DailyBriefLog.cs` | One row per day the brief was sent. |
| `ReminderStatus.cs` | `Pending`, `Completed`, `Cancelled`. |
| `Priority.cs` | `Normal`, `High`. |

### `src/Assistant.Contracts`

| File | Responsibility |
| :--- | :--- |
| `Result.cs` | Success/failure with an error code and message. No exceptions for expected failures. |
| `ErrorCode.cs` | Enumerates the rejections in spec §4.2. |
| `CreateTaskRequest.cs` | Title, optional local due time, notes, priority. |
| `UpdateTaskRequest.cs` | Id plus optional fields. |
| `ListTasksRequest.cs` | Filter and limit. |
| `TaskFilter.cs` | `Today`, `Overdue`, `Week`, `All`. |
| `TaskResponse.cs` | Caller-visible projection of a task. |
| `ReminderNotification.cs` | What the Telegram layer renders. Carries no database shape. |
| `DailyBriefNotification.cs` | Today's and overdue items for the brief. |

### `src/Assistant.Interfaces`

| File | Responsibility |
| :--- | :--- |
| `IClock.cs` | `UtcNow`. The seam that makes time testable. |
| `ITaskRepository.cs` | Intent-named persistence operations for tasks. |
| `IChatMessageRepository.cs` | Append and read the conversation window. |
| `IDailyBriefRepository.cs` | Claim a date for the brief; idempotent. |
| `ITaskService.cs` | The single writer's contract. |
| `INotifier.cs` | Outbound user-facing messages. |
| `IMessageHandler.cs` | Handles one inbound text message. |
| `ICallbackHandler.cs` | Handles one inbound button press. |
| `ITaskAction.cs` | One button behaviour, resolved by key. |
| `IAssistantTool.cs` | One LLM-callable capability. |
| `IScheduledJob.cs` | One recurring job. |
| `IAgent.cs` | Runs the LLM tool loop for a message. |

### `src/Assistant.Repository`

| File | Responsibility |
| :--- | :--- |
| `AssistantDbContext.cs` | EF context. Internal to this project. |
| `Configurations/ReminderTaskConfiguration.cs` | Table, constraints, filtered index. |
| `Configurations/ChatMessageConfiguration.cs` | Table and index. |
| `Configurations/DailyBriefLogConfiguration.cs` | Date primary key. |
| `EfTaskRepository.cs` | `ITaskRepository` over EF. |
| `EfChatMessageRepository.cs` | `IChatMessageRepository` over EF. |
| `EfDailyBriefRepository.cs` | `IDailyBriefRepository` over EF. |
| `RepositoryServiceCollectionExtensions.cs` | `AddAssistantRepository`. The project's only public entry point besides the repositories. |
| `Migrations/` | EF migrations. |

### `src/Assistant.Impl`

| File | Responsibility |
| :--- | :--- |
| `Services/TaskService.cs` | Single writer. Every invariant from spec §4.2. |
| `Services/LocalTimeResolver.cs` | Local ISO string → UTC instant, with guard clauses. |
| `Services/MessageHandler.cs` | Whitelist, persist, delegate to agent, reply. |
| `Services/CallbackHandler.cs` | Parse callback data, dispatch to `ITaskAction`. |
| `Services/AgentService.cs` | Builds the prompt, runs the tool loop. |
| `Services/Jobs/DueReminderJob.cs` | Deliver overdue reminders. The reliability core. |
| `Services/Jobs/DailyBriefJob.cs` | One brief per day, no cutoff. |
| `Services/Actions/DoneAction.cs` | Complete via `ITaskService`. |
| `Services/Actions/SnoozeAction.cs` | Snooze by a parsed duration. |
| `Services/Actions/RescheduleAction.cs` | Move to a named target time. |
| `Services/Actions/EditAction.cs` | Prompt for a change; route the next message. |
| `Mapping/ReminderTaskMappingExtensions.cs` | `ToResponse`, `ToModel`, `ToNotification`. |
| `Mapping/CallbackDataExtensions.cs` | Encode and parse `v1:action:id[:arg]`. |
| `Tools/CreateTaskTool.cs` | LLM tool → `ITaskService.CreateAsync`. |
| `Tools/ListTasksTool.cs` | LLM tool → `ITaskService.QueryAsync`. |
| `Tools/UpdateTaskTool.cs` | LLM tool → `ITaskService.UpdateAsync`. |
| `Tools/CompleteTaskTool.cs` | LLM tool → `ITaskService.CompleteAsync`. |
| `Telegram/TelegramListener.cs` | Long-poll loop. |
| `Telegram/TelegramNotifier.cs` | `INotifier` over Telegram.Bot, HTML parse mode. |
| `Telegram/TelegramOptions.cs` | Token, base URL, owner user ID. |
| `Ai/IAnthropicApi.cs` | Refit interface for the Messages API. |
| `Ai/IOpenRouterApi.cs` | Refit interface for the chat completions API. |
| `Ai/AnthropicChatClient.cs` | Refit interface → project's own chat abstraction. |
| `Ai/OpenRouterChatClient.cs` | Same for OpenRouter. |
| `Ai/FallbackChatClient.cs` | Decorator: primary, then secondary. |
| `Ai/LlmOptions.cs` | Keys, models, base URLs, per-minute cap. |
| `Scheduling/ReminderScheduler.cs` | 30-second tick, runs every `IScheduledJob`. |
| `Scheduling/ScheduledJobBase.cs` | Re-entrancy guard and error boundary. |
| `Scheduling/SystemClock.cs` | `IClock` over `DateTimeOffset.UtcNow`. |
| `Scheduling/HeartbeatWriter.cs` | Touches the healthcheck file. |
| `ImplServiceCollectionExtensions.cs` | `AddAssistantServices`. |

### `src/Assistant.Worker`

| File | Responsibility |
| :--- | :--- |
| `Program.cs` | Composition root. Options binding, DI, hosted services. |
| `appsettings.json` | Non-secret defaults. |
| `Dockerfile` | Multi-stage build. |

### `tests/Assistant.UnitTests`

| File | Responsibility |
| :--- | :--- |
| `Architecture/ReferenceGraphTests.cs` | Parses `.csproj` files; asserts the reference graph. |
| `Architecture/DependencyRuleTests.cs` | NetArchTest type-dependency rules. |
| `Architecture/ConventionTests.cs` | Models have no methods; no `IQueryable` on repositories. |
| `Services/LocalTimeResolverTests.cs` | Guard clauses, DST gap and ambiguity, table-driven. |
| `Services/SnoozeArithmeticTests.cs` | Duration parsing and target-time tables. |
| `Mapping/ReminderTaskMappingTests.cs` | Round-trip property coverage. |
| `Mapping/CallbackDataTests.cs` | Encode/parse, 64-byte budget, version prefix. |

### `tests/Assistant.IntegrationTests`

| File | Responsibility |
| :--- | :--- |
| `Infrastructure/PostgresFixture.cs` | Waits for readiness, applies migrations, Respawn reset. |
| `Infrastructure/AssistantHostFixture.cs` | Boots the real host with WireMock and `FakeClock` substituted. |
| `Infrastructure/FakeClock.cs` | Settable `IClock`. |
| `Infrastructure/TelegramStub.cs` | WireMock setup and payload assertions for Telegram. |
| `Infrastructure/AnthropicStub.cs` | WireMock setup for the Messages API, including tool-use replies. |
| `Infrastructure/SendMessagePayload.cs` | Parses a captured Telegram request into assertable fields. |
| `Repository/TaskRepositoryTests.cs` | Queries, constraints, index behaviour. |
| `Services/TaskServiceTests.cs` | Observable invariants from spec §4.2. |
| `Reminders/DueReminderJobTests.cs` | Every scenario in spec §7.4 relating to delivery. |
| `Reminders/DailyBriefJobTests.cs` | Once per day, late delivery, no cutoff. |
| `Telegram/CallbackActionTests.cs` | Button behaviour, idempotency, in-place edit. |
| `Capture/CaptureFlowTests.cs` | Whitelist, tool call → row + buttons, provider failure fallback. |

---

## Task 0: Public repository with agent documentation

Spec §9 step 0, §11.1, §11.2, §12.4. This is commit #1 and contains no C#. It exists so no secret can ever enter history and so the conventions are readable before any code is written.

**Files:**
- Create: `.gitignore`, `LICENSE`, `.env.example`, `.editorconfig`, `README.md`, `AGENTS.md`, `CLAUDE.md`, `docs/conventions.md`, `docs/design/slice-1-reminders.md`

**Interfaces:**
- Consumes: nothing.
- Produces: the repository. Every later task commits into it.

- [ ] **Step 1: Initialise the repository**

```bash
mkdir -p personal-ai-assistant && cd personal-ai-assistant
git init -b main
mkdir -p docs/design src tests .github/workflows
```

- [ ] **Step 2: Write `.gitignore` before anything else**

```gitignore
# Build output
bin/
obj/
[Dd]ebug/
[Rr]elease/
artifacts/
*.user
*.suo
.vs/
.idea/

# Secrets — never commit
.env
*.env
!.env.example
appsettings.*.local.json

# Test output
[Tt]est[Rr]esult*/
coverage*
*.trx

# OS
.DS_Store
Thumbs.db
```

- [ ] **Step 3: Write `LICENSE`**

Use the MIT licence text verbatim, with `Copyright (c) 2026 Amit Salim`.

- [ ] **Step 4: Write `.env.example`**

```dotenv
# Telegram bot token from @BotFather
TELEGRAM__BOTTOKEN=

# Your own numeric Telegram user ID, from @userinfobot.
# The bot ignores every other sender.
TELEGRAM__OWNERUSERID=

# Anthropic API key (primary LLM provider)
LLM__ANTHROPIC__APIKEY=

# OpenRouter API key (fallback provider; optional)
LLM__OPENROUTER__APIKEY=

# Postgres password used by compose.yaml
POSTGRES_PASSWORD=
```

The double underscore is the .NET configuration hierarchy separator: `TELEGRAM__BOTTOKEN` binds to `Telegram:BotToken`.

- [ ] **Step 5: Write `.editorconfig`**

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
indent_style = space
trim_trailing_whitespace = true

[*.{cs,csx}]
indent_size = 4
csharp_style_namespace_declarations = file_scoped:warning
csharp_style_var_for_built_in_types = false:suggestion
dotnet_sort_system_directives_first = true
dotnet_style_require_accessibility_modifiers = always:warning
dotnet_naming_rule.interfaces_start_with_i.severity = error
dotnet_naming_rule.interfaces_start_with_i.symbols = interface_symbol
dotnet_naming_rule.interfaces_start_with_i.style = prefix_i_style
dotnet_naming_symbols.interface_symbol.applicable_kinds = interface
dotnet_naming_style.prefix_i_style.required_prefix = I
dotnet_naming_style.prefix_i_style.capitalization = pascal_case

[*.{json,yml,yaml,csproj,props}]
indent_size = 2

[*.md]
trim_trailing_whitespace = false
```

- [ ] **Step 6: Copy the design spec in**

Copy the approved v2.0 spec to `docs/design/slice-1-reminders.md`.

- [ ] **Step 7: Write `docs/conventions.md`**

Extract spec §12 verbatim: XML documentation rules and the required tag table, mapping-as-extension-methods with the naming convention, Refit as the only HTTP mechanism with the `Telegram.Bot` exception, and the reference graph.

- [ ] **Step 8: Write `AGENTS.md`**

````markdown
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

### Prompt evaluations

Needs a real API key and costs money. Not part of CI.

```bash
dotnet run --project tools/PromptEval
```

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
````

- [ ] **Step 9: Write `CLAUDE.md`**

```markdown
See [AGENTS.md](./AGENTS.md). This project keeps one set of agent
instructions; this file exists only so tools that look for `CLAUDE.md`
find it.
```

- [ ] **Step 10: Write `README.md`**

````markdown
# personal-ai-assistant

Tasks get captured and then forgotten. Nothing resurfaces them at the right
moment. This is a Telegram bot that fixes the second half of that problem.

You send it a message:

> call the bank tomorrow at 10

It replies with the task and three buttons, then messages you at 10:00 the
next morning. Tap **Done** and it is gone — no app, no tokens spent, one tap.
Every morning it sends a brief of what is due and what is overdue.

Self-hosted on a €5 VPS. Your data never leaves your machine.

## Quickstart

1. Create a bot with [@BotFather](https://t.me/BotFather) and copy the token
2. Get your numeric user ID from [@userinfobot](https://t.me/userinfobot)
3. `cp .env.example .env` and fill in the values
4. `docker compose up -d`

## Deliberate limitations

- **Single user.** There are no accounts and no tenancy: one whitelisted
  Telegram ID, and every other sender is ignored. This is the point, not a
  gap — nobody operates a server that can read your tasks.
- **One timezone.** Currently fixed to `Asia/Jerusalem`. Making this
  configurable is a small change and a welcome pull request.

## Contributing

The whole test suite runs with **no credentials at all** — Telegram and the
LLM APIs are stubbed with WireMock and Postgres comes from Docker Compose.
Fork, `dotnet test`, done. See [AGENTS.md](./AGENTS.md) for every command and
[docs/design/](./docs/design/) for why the system is shaped the way it is.

## Licence

MIT.
````

- [ ] **Step 11: Verify no secret is tracked**

```bash
git add -A
git status --short
```
Expected: `.env.example` present, `.env` absent. If `.env` appears, stop and fix `.gitignore` before committing.

- [ ] **Step 12: Commit and publish**

```bash
git commit -m "chore: initialise public repository with agent docs and licence"
gh repo create personal-ai-assistant --public --source=. --remote=origin --push
```

---

## Task 1: Solution skeleton and architecture tests

Spec §9 step 1, §3.1, §3.2, §7.5. The reference graph is enforced by tests written **before** there is any code to violate it.

**Files:**
- Create: `PersonalAssistant.sln`, `Directory.Build.props`, `Directory.Packages.props`, `tests/Directory.Build.props`, eight `.csproj` files
- Create: `tests/Assistant.UnitTests/Architecture/ReferenceGraphTests.cs`, `tests/Assistant.UnitTests/Architecture/ConventionTests.cs`

**Interfaces:**
- Consumes: Task 0's repository.
- Produces: a building solution; `ProjectPaths.SolutionRoot` and `ProjectPaths.CsprojFor(string projectName)` used by later architecture tests.

- [ ] **Step 1: Create the solution and projects**

```bash
dotnet new sln -n PersonalAssistant

dotnet new classlib -o src/Assistant.Models      -n Assistant.Models
dotnet new classlib -o src/Assistant.Contracts   -n Assistant.Contracts
dotnet new classlib -o src/Assistant.Interfaces  -n Assistant.Interfaces
dotnet new classlib -o src/Assistant.Repository  -n Assistant.Repository
dotnet new classlib -o src/Assistant.Impl        -n Assistant.Impl
dotnet new worker   -o src/Assistant.Worker      -n Assistant.Worker
dotnet new xunit    -o tests/Assistant.UnitTests -n Assistant.UnitTests
dotnet new xunit    -o tests/Assistant.IntegrationTests -n Assistant.IntegrationTests

dotnet sln add $(find src tests -name '*.csproj')

rm src/Assistant.Models/Class1.cs src/Assistant.Contracts/Class1.cs \
   src/Assistant.Interfaces/Class1.cs src/Assistant.Repository/Class1.cs \
   src/Assistant.Impl/Class1.cs
```

- [ ] **Step 2: Wire the reference graph**

```bash
dotnet add src/Assistant.Interfaces reference src/Assistant.Models src/Assistant.Contracts
dotnet add src/Assistant.Repository reference src/Assistant.Interfaces src/Assistant.Models
dotnet add src/Assistant.Impl      reference src/Assistant.Interfaces src/Assistant.Contracts src/Assistant.Models
dotnet add src/Assistant.Worker    reference src/Assistant.Repository src/Assistant.Impl src/Assistant.Interfaces src/Assistant.Contracts src/Assistant.Models

dotnet add tests/Assistant.UnitTests reference src/Assistant.Impl src/Assistant.Interfaces src/Assistant.Contracts src/Assistant.Models
dotnet add tests/Assistant.IntegrationTests reference src/Assistant.Worker
```

Note what is deliberately absent: `Impl` has no reference to `Repository`.

- [ ] **Step 3: Write `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

`GenerateDocumentationFile` turns on `CS1591` for undocumented public members; `TreatWarningsAsErrors` makes it fail the build. `InvariantGlobalization` must be `false` — `TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem")` throws without timezone data.

- [ ] **Step 4: Write `tests/Directory.Build.props`**

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>
</Project>
```

Test projects do not require XML documentation; source projects do.

- [ ] **Step 5: Enable central package management and add test packages**

```bash
cat > Directory.Packages.props <<'XML'
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
  </ItemGroup>
</Project>
XML

dotnet add tests/Assistant.UnitTests package Shouldly
dotnet add tests/Assistant.UnitTests package NetArchTest.Rules
```

`dotnet add package` writes `PackageVersion` entries into `Directory.Packages.props` when central management is enabled, so versions resolve to current rather than being guessed here.

- [ ] **Step 6: Write the failing reference-graph test**

`tests/Assistant.UnitTests/Architecture/ReferenceGraphTests.cs`:

```csharp
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace Assistant.UnitTests.Architecture;

public static class ProjectPaths
{
    public static string SolutionRoot { get; } = FindRoot(AppContext.BaseDirectory);

    public static string CsprojFor(string projectName)
    {
        var folder = projectName.StartsWith("Assistant.Unit") || projectName.StartsWith("Assistant.Integration")
            ? "tests"
            : "src";
        return Path.Combine(SolutionRoot, folder, projectName, $"{projectName}.csproj");
    }

    private static string FindRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PersonalAssistant.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("PersonalAssistant.sln not found above " + start);
    }
}

public class ReferenceGraphTests
{
    private static IReadOnlyList<string> ProjectReferencesOf(string projectName)
    {
        var doc = XDocument.Load(ProjectPaths.CsprojFor(projectName));
        return doc.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(
                e.Attribute("Include")!.Value.Replace('\\', '/')))
            .OrderBy(n => n)
            .ToList();
    }

    [Theory]
    [InlineData("Assistant.Models")]
    [InlineData("Assistant.Contracts")]
    public void Foundation_projects_reference_nothing(string project)
        => ProjectReferencesOf(project).ShouldBeEmpty();

    [Fact]
    public void Interfaces_references_only_models_and_contracts()
        => ProjectReferencesOf("Assistant.Interfaces")
            .ShouldBe(new[] { "Assistant.Contracts", "Assistant.Models" });

    [Fact]
    public void Repository_references_only_interfaces_and_models()
        => ProjectReferencesOf("Assistant.Repository")
            .ShouldBe(new[] { "Assistant.Interfaces", "Assistant.Models" });

    [Fact]
    public void Impl_does_not_reference_repository()
        => ProjectReferencesOf("Assistant.Impl")
            .ShouldNotContain("Assistant.Repository");

    [Fact]
    public void Impl_references_only_interfaces_contracts_and_models()
        => ProjectReferencesOf("Assistant.Impl")
            .ShouldBe(new[] { "Assistant.Contracts", "Assistant.Interfaces", "Assistant.Models" });

    [Fact]
    public void Repository_does_not_reference_impl()
        => ProjectReferencesOf("Assistant.Repository")
            .ShouldNotContain("Assistant.Impl");
}
```

- [ ] **Step 7: Run the tests to see them pass against the graph you just built**

Run: `dotnet test tests/Assistant.UnitTests --filter ReferenceGraphTests`
Expected: PASS, 7 tests.

Then prove the test actually bites:

```bash
dotnet add src/Assistant.Impl reference src/Assistant.Repository
dotnet test tests/Assistant.UnitTests --filter ReferenceGraphTests
```
Expected: FAIL on `Impl_does_not_reference_repository` and `Impl_references_only_...`.

```bash
dotnet remove src/Assistant.Impl reference src/Assistant.Repository
dotnet test tests/Assistant.UnitTests --filter ReferenceGraphTests
```
Expected: PASS again. A guard you have not seen fail is not a guard.

The remaining architecture tests — `ConventionTests` and the NetArchTest type-dependency rules — need types that do not exist yet. They are written in Task 4, once `Models`, `Contracts`, and `Interfaces` are populated.

- [ ] **Step 8: Verify the whole solution builds clean**

Run: `dotnet build`
Expected: succeeds with zero warnings. If `CS1591` fires, a public member is missing XML documentation — add it rather than suppressing the warning.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "chore: solution skeleton with build-enforced reference graph"
```

```bash
git add -A
git commit -m "chore: solution skeleton with build-enforced reference graph"
```

---

## Task 2: The shape of the system — Models, Contracts, Interfaces

Spec §9 step 2, §3.3, §4.1. Every type in the system is declared before anything implements one. Nothing in this task has behaviour, so its test is the architecture suite.

**Files:**
- Create: `src/Assistant.Models/{ReminderTask,ChatMessage,DailyBriefLog,ReminderStatus,Priority}.cs`
- Create: `src/Assistant.Contracts/{Result,ErrorCode,CreateTaskRequest,UpdateTaskRequest,ListTasksRequest,TaskFilter,TaskResponse,ReminderNotification,DailyBriefNotification}.cs`
- Create: `src/Assistant.Interfaces/{IClock,ITaskRepository,IChatMessageRepository,IDailyBriefRepository,ITaskService,INotifier,IMessageHandler,ICallbackHandler,ITaskAction,IAssistantTool,IScheduledJob,IAgent}.cs`
- Create: `tests/Assistant.UnitTests/Architecture/ConventionTests.cs`, `tests/Assistant.UnitTests/Architecture/DependencyRuleTests.cs`

**Interfaces:**
- Consumes: Task 1's solution and reference graph.
- Produces: every type name and signature the rest of the plan uses. Later tasks implement these exactly — do not rename.

- [ ] **Step 1: Write the enums**

`src/Assistant.Models/ReminderStatus.cs`:

```csharp
namespace Assistant.Models;

/// <summary>Lifecycle state of a <see cref="ReminderTask"/>.</summary>
public enum ReminderStatus
{
    /// <summary>Outstanding. Eligible for reminder delivery.</summary>
    Pending = 0,

    /// <summary>Finished. No further reminders are delivered.</summary>
    Completed = 1,

    /// <summary>Abandoned without being done. No further reminders are delivered.</summary>
    Cancelled = 2,
}
```

`src/Assistant.Models/Priority.cs`:

```csharp
namespace Assistant.Models;

/// <summary>Relative importance of a <see cref="ReminderTask"/>.</summary>
public enum Priority
{
    /// <summary>Default importance.</summary>
    Normal = 1,

    /// <summary>Raised importance. Surfaced first in listings and briefs.</summary>
    High = 2,
}
```

- [ ] **Step 2: Write the models**

`src/Assistant.Models/ReminderTask.cs`:

```csharp
namespace Assistant.Models;

/// <summary>
/// A task the assistant is holding, and the time at which it should remind the user about it.
/// </summary>
/// <remarks>
/// This is a persistence model with no behaviour: every mutation goes through the task service,
/// which is the single writer and the only place the invariants are enforced. All instants are
/// UTC with a zero offset.
/// </remarks>
public sealed class ReminderTask
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Short description of what needs doing.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional longer detail.</summary>
    public string? Notes { get; set; }

    /// <summary>Current lifecycle state.</summary>
    public ReminderStatus Status { get; set; }

    /// <summary>Relative importance.</summary>
    public Priority Priority { get; set; }

    /// <summary>
    /// When the task is due, in UTC. Also the instant at which its reminder is delivered.
    /// </summary>
    /// <value><see langword="null"/> for a task with no deadline, which never triggers a reminder.</value>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>When the reminder for the current <see cref="DueAt"/> was delivered, in UTC.</summary>
    /// <value>
    /// <see langword="null"/> when delivery is still owed. Snoozing or rescheduling resets this
    /// to <see langword="null"/> so the task fires again.
    /// </value>
    public DateTimeOffset? ReminderSentAt { get; set; }

    /// <summary>Number of failed delivery attempts for the current <see cref="DueAt"/>.</summary>
    public int DeliveryAttempts { get; set; }

    /// <summary>When the task was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the task was last modified, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When the task was completed, in UTC.</summary>
    /// <value><see langword="null"/> unless <see cref="Status"/> is <see cref="ReminderStatus.Completed"/>.</value>
    public DateTimeOffset? CompletedAt { get; set; }
}
```

`src/Assistant.Models/ChatMessage.cs`:

```csharp
namespace Assistant.Models;

/// <summary>One turn of the conversation, retained so follow-up messages resolve.</summary>
/// <remarks>
/// Only the most recent turns are ever read; see the chat message repository for the window size.
/// </remarks>
public sealed class ChatMessage
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Who produced this turn: <c>user</c>, <c>assistant</c>, or <c>tool</c>.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>The turn's text.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>When the turn occurred, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
```

`src/Assistant.Models/DailyBriefLog.cs`:

```csharp
namespace Assistant.Models;

/// <summary>Record that the daily brief was sent for a given local date.</summary>
/// <remarks>
/// <see cref="BriefDate"/> is the primary key, which makes the insert itself the once-per-day
/// check: a duplicate send is a primary key violation rather than a race to be reasoned about.
/// </remarks>
public sealed class DailyBriefLog
{
    /// <summary>The local date the brief covered.</summary>
    public DateOnly BriefDate { get; set; }

    /// <summary>When the brief was actually delivered, in UTC.</summary>
    public DateTimeOffset SentAt { get; set; }
}
```

- [ ] **Step 3: Write the result type and error codes**

`src/Assistant.Contracts/ErrorCode.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>Why an operation was rejected.</summary>
public enum ErrorCode
{
    /// <summary>No error.</summary>
    None = 0,

    /// <summary>No task exists with the given identifier.</summary>
    TaskNotFound,

    /// <summary>The task has already been completed, and the operation requires an open task.</summary>
    TaskAlreadyCompleted,

    /// <summary>The task has been cancelled, and the operation requires an open task.</summary>
    TaskCancelled,

    /// <summary>The operation requires a due time and the task has none.</summary>
    TaskHasNoDueTime,

    /// <summary>The supplied time is in the past.</summary>
    TimeInPast,

    /// <summary>The supplied time is implausibly far in the future.</summary>
    TimeTooFarAhead,

    /// <summary>The supplied text could not be parsed as a local ISO-8601 datetime.</summary>
    TimeUnparseable,

    /// <summary>Every language model provider failed.</summary>
    LlmUnavailable,
}
```

`src/Assistant.Contracts/Result.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>Outcome of an operation that can fail for an expected reason.</summary>
/// <remarks>
/// Expected failures — a missing task, a time in the past — are returned rather than thrown.
/// Exceptions remain for genuine faults such as a database being unreachable.
/// </remarks>
public sealed class Result
{
    private Result(bool succeeded, ErrorCode error, string? message)
    {
        Succeeded = succeeded;
        Error = error;
        Message = message;
    }

    /// <summary>Whether the operation completed.</summary>
    public bool Succeeded { get; }

    /// <summary>Why the operation was rejected.</summary>
    /// <value><see cref="ErrorCode.None"/> when <see cref="Succeeded"/> is <see langword="true"/>.</value>
    public ErrorCode Error { get; }

    /// <summary>Human-readable explanation suitable for showing to the user.</summary>
    /// <value><see langword="null"/> on success.</value>
    public string? Message { get; }

    /// <summary>Creates a successful result.</summary>
    /// <returns>A result whose <see cref="Succeeded"/> is <see langword="true"/>.</returns>
    public static Result Success() => new(true, ErrorCode.None, null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">Why the operation was rejected.</param>
    /// <param name="message">Explanation suitable for showing to the user.</param>
    /// <returns>A result whose <see cref="Succeeded"/> is <see langword="false"/>.</returns>
    public static Result Failure(ErrorCode error, string message) => new(false, error, message);
}
```

- [ ] **Step 4: Write the request and response types**

`src/Assistant.Contracts/TaskFilter.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>Which tasks a listing should return.</summary>
public enum TaskFilter
{
    /// <summary>Pending tasks due at any point today, local time.</summary>
    Today = 0,

    /// <summary>Pending tasks whose due time has passed.</summary>
    Overdue,

    /// <summary>Pending tasks due within the next seven days.</summary>
    Week,

    /// <summary>All pending tasks, including those with no due time.</summary>
    All,
}
```

`src/Assistant.Contracts/CreateTaskRequest.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>Request to create a task.</summary>
/// <param name="Title">Short description of what needs doing.</param>
/// <param name="DueAtLocal">
/// Absolute local ISO-8601 datetime with no offset, for example <c>2026-08-17T10:00:00</c>.
/// <see langword="null"/> creates a task with no deadline, which never reminds.
/// </param>
/// <param name="Notes">Optional longer detail.</param>
/// <param name="IsHighPriority">Whether the task is raised in importance.</param>
public sealed record CreateTaskRequest(
    string Title,
    string? DueAtLocal = null,
    string? Notes = null,
    bool IsHighPriority = false);
```

`src/Assistant.Contracts/UpdateTaskRequest.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>Request to change an existing task. Omitted fields are left unchanged.</summary>
/// <param name="TaskId">Identifier of the task to change.</param>
/// <param name="Title">New description, or <see langword="null"/> to keep the current one.</param>
/// <param name="DueAtLocal">
/// New absolute local ISO-8601 datetime with no offset, or <see langword="null"/> to keep the
/// current due time. Changing it re-arms the reminder.
/// </param>
/// <param name="Notes">New detail, or <see langword="null"/> to keep the current text.</param>
/// <param name="IsHighPriority">New importance, or <see langword="null"/> to keep the current value.</param>
public sealed record UpdateTaskRequest(
    Guid TaskId,
    string? Title = null,
    string? DueAtLocal = null,
    string? Notes = null,
    bool? IsHighPriority = null);
```

`src/Assistant.Contracts/ListTasksRequest.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>Request to list tasks.</summary>
/// <param name="Filter">Which tasks to return.</param>
/// <param name="Limit">Maximum number of tasks to return. Clamped to 100 by the service.</param>
public sealed record ListTasksRequest(TaskFilter Filter = TaskFilter.Today, int Limit = 20);
```

`src/Assistant.Contracts/TaskResponse.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>A task as presented to a caller.</summary>
/// <param name="Id">Unique identifier.</param>
/// <param name="Title">Short description of what needs doing.</param>
/// <param name="Notes">Longer detail, if any.</param>
/// <param name="DueAtLocal">
/// Due time rendered in local time, or <see langword="null"/> when the task has no deadline.
/// </param>
/// <param name="IsOverdue">Whether the due time has passed and the task is still pending.</param>
/// <param name="IsHighPriority">Whether the task is raised in importance.</param>
/// <param name="IsCompleted">Whether the task has been completed.</param>
public sealed record TaskResponse(
    Guid Id,
    string Title,
    string? Notes,
    DateTimeOffset? DueAtLocal,
    bool IsOverdue,
    bool IsHighPriority,
    bool IsCompleted);
```

`src/Assistant.Contracts/ReminderNotification.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>A reminder ready to be delivered to the user.</summary>
/// <remarks>
/// Carries no persistence shape, so the messaging layer never depends on the database schema.
/// </remarks>
/// <param name="TaskId">Identifier of the task, used to build the button callback payloads.</param>
/// <param name="Title">Short description of what needs doing.</param>
/// <param name="DueAtLocal">Due time rendered in local time.</param>
/// <param name="OverdueBy">
/// How long the task has been overdue, or <see langword="null"/> when it is due now.
/// </param>
public sealed record ReminderNotification(
    Guid TaskId,
    string Title,
    DateTimeOffset DueAtLocal,
    TimeSpan? OverdueBy);
```

`src/Assistant.Contracts/DailyBriefNotification.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>The daily brief, ready to be delivered to the user.</summary>
/// <param name="BriefDate">The local date the brief covers.</param>
/// <param name="DueToday">Tasks due at some point today.</param>
/// <param name="Overdue">Tasks whose due time has already passed.</param>
/// <param name="OpenWithoutDueDate">How many pending tasks have no deadline at all.</param>
public sealed record DailyBriefNotification(
    DateOnly BriefDate,
    IReadOnlyList<TaskResponse> DueToday,
    IReadOnlyList<TaskResponse> Overdue,
    int OpenWithoutDueDate);
```

- [ ] **Step 5: Write the clock and repository interfaces**

`src/Assistant.Interfaces/IClock.cs`:

```csharp
namespace Assistant.Interfaces;

/// <summary>Source of the current time.</summary>
/// <remarks>
/// Every time-dependent rule in the system reads the clock through this interface, which is what
/// makes reminder scheduling, snoozing, and daylight-saving behaviour testable without waiting.
/// </remarks>
public interface IClock
{
    /// <summary>The current instant in UTC.</summary>
    /// <value>A <see cref="DateTimeOffset"/> whose offset is always <see cref="TimeSpan.Zero"/>.</value>
    DateTimeOffset UtcNow { get; }
}
```

`src/Assistant.Interfaces/ITaskRepository.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Models;

namespace Assistant.Interfaces;

/// <summary>Persistence operations for tasks.</summary>
/// <remarks>
/// Methods are named by intent rather than exposing a composable query, so each one can be
/// backed by an index built for it. Results are always materialised.
/// </remarks>
public interface ITaskRepository
{
    /// <summary>Finds a task by identifier.</summary>
    /// <param name="id">Identifier to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The task, or <see langword="null"/> when no task has that identifier.</returns>
    Task<ReminderTask?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>Adds a new task.</summary>
    /// <param name="task">The task to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    Task AddAsync(ReminderTask task, CancellationToken ct);

    /// <summary>Persists changes made to a previously loaded task.</summary>
    /// <param name="task">The task to save.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    Task UpdateAsync(ReminderTask task, CancellationToken ct);

    /// <summary>
    /// Returns pending tasks that are due and whose reminder has not yet been delivered.
    /// </summary>
    /// <param name="asOfUtc">The instant to treat as "now".</param>
    /// <param name="limit">Maximum number of tasks to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Tasks ordered by due time, oldest first. There is no lower bound on the due time, so a
    /// task missed during an outage is still returned once the process is running again.
    /// </returns>
    Task<IReadOnlyList<ReminderTask>> GetDueRemindersAsync(
        DateTimeOffset asOfUtc, int limit, CancellationToken ct);

    /// <summary>Returns pending tasks matching a filter.</summary>
    /// <param name="filter">Which tasks to return.</param>
    /// <param name="asOfUtc">The instant to treat as "now" when resolving relative filters.</param>
    /// <param name="limit">Maximum number of tasks to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tasks ordered by due time, with undated tasks last.</returns>
    Task<IReadOnlyList<ReminderTask>> QueryAsync(
        TaskFilter filter, DateTimeOffset asOfUtc, int limit, CancellationToken ct);

    /// <summary>Counts pending tasks that have no due time.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of pending tasks with no deadline.</returns>
    Task<int> CountOpenWithoutDueDateAsync(CancellationToken ct);
}
```

`src/Assistant.Interfaces/IChatMessageRepository.cs`:

```csharp
using Assistant.Models;

namespace Assistant.Interfaces;

/// <summary>Persistence for the rolling conversation window.</summary>
public interface IChatMessageRepository
{
    /// <summary>Appends one turn.</summary>
    /// <param name="message">The turn to append.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    Task AppendAsync(ChatMessage message, CancellationToken ct);

    /// <summary>Returns the most recent turns, oldest first.</summary>
    /// <param name="limit">How many turns to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Up to <paramref name="limit"/> turns in chronological order, ready to be replayed to the
    /// language model as conversation history.
    /// </returns>
    Task<IReadOnlyList<ChatMessage>> GetRecentAsync(int limit, CancellationToken ct);
}
```

`src/Assistant.Interfaces/IDailyBriefRepository.cs`:

```csharp
namespace Assistant.Interfaces;

/// <summary>Persistence for the record of which days the brief has been sent.</summary>
public interface IDailyBriefRepository
{
    /// <summary>Attempts to claim a date for the daily brief.</summary>
    /// <param name="briefDate">The local date to claim.</param>
    /// <param name="nowUtc">The current instant, recorded against the claim.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the caller has exclusively claimed the date and should send
    /// the brief; <see langword="false"/> when it was already claimed. The claim is atomic, so
    /// two concurrent callers cannot both receive <see langword="true"/>.
    /// </returns>
    Task<bool> TryClaimAsync(DateOnly briefDate, DateTimeOffset nowUtc, CancellationToken ct);
}
```

- [ ] **Step 6: Write the service and handler interfaces**

`src/Assistant.Interfaces/ITaskService.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Models;

namespace Assistant.Interfaces;

/// <summary>The single writer for tasks. Every mutation in the system goes through here.</summary>
/// <remarks>
/// Because models carry no behaviour, this is the only place task invariants are enforced.
/// Nothing else — no job, no tool, no button handler — may mutate a task or call a repository
/// write directly.
/// </remarks>
public interface ITaskService
{
    /// <summary>Creates a task, resolving its due time from local text.</summary>
    /// <param name="request">What to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The created task on success; a failure carrying <see cref="ErrorCode.TimeInPast"/>,
    /// <see cref="ErrorCode.TimeTooFarAhead"/>, or <see cref="ErrorCode.TimeUnparseable"/> when
    /// the requested due time is rejected.
    /// </returns>
    Task<(Result Result, ReminderTask? Task)> CreateAsync(CreateTaskRequest request, CancellationToken ct);

    /// <summary>Applies changes to an existing task.</summary>
    /// <param name="request">What to change.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or a failure explaining why the change was rejected.</returns>
    /// <remarks>Changing the due time clears the reminder marker, so the task fires again.</remarks>
    Task<Result> UpdateAsync(UpdateTaskRequest request, CancellationToken ct);

    /// <summary>Marks a task complete.</summary>
    /// <param name="id">Identifier of the task.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success — including when the task was already complete, so a button can be pressed twice
    /// safely. A failure carrying <see cref="ErrorCode.TaskCancelled"/> when the task was cancelled.
    /// </returns>
    Task<Result> CompleteAsync(Guid id, CancellationToken ct);

    /// <summary>Marks a task cancelled without completing it.</summary>
    /// <param name="id">Identifier of the task.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or a failure explaining why the cancellation was rejected.</returns>
    Task<Result> CancelAsync(Guid id, CancellationToken ct);

    /// <summary>Moves a task's due time forward and re-arms its reminder.</summary>
    /// <param name="id">Identifier of the task.</param>
    /// <param name="duration">How far forward to move the due time. Must be positive.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or a failure explaining why the snooze was rejected.</returns>
    /// <remarks>
    /// Snoozing clears the reminder-sent marker and resets the delivery attempt count, so the
    /// task fires again at its new due time. Snoozing measures from the current time, not from
    /// the old due time, so snoozing an overdue task by an hour means an hour from now.
    /// See <see cref="RescheduleAsync"/> to set an absolute time instead.
    /// </remarks>
    Task<Result> SnoozeAsync(Guid id, TimeSpan duration, CancellationToken ct);

    /// <summary>Sets a task's due time to a specific instant and re-arms its reminder.</summary>
    /// <param name="id">Identifier of the task.</param>
    /// <param name="newDueAtUtc">The new due time, in UTC.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or a failure explaining why the reschedule was rejected.</returns>
    Task<Result> RescheduleAsync(Guid id, DateTimeOffset newDueAtUtc, CancellationToken ct);

    /// <summary>Records that a task's reminder has been delivered.</summary>
    /// <param name="id">Identifier of the task.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success, or a failure carrying <see cref="ErrorCode.TaskHasNoDueTime"/> when the task has
    /// no due time and therefore no reminder to have delivered.
    /// </returns>
    /// <remarks>
    /// Called only after the message has actually been sent. Marking before sending would lose a
    /// reminder whenever the send fails.
    /// </remarks>
    Task<Result> MarkReminderSentAsync(Guid id, CancellationToken ct);

    /// <summary>Records a failed delivery attempt.</summary>
    /// <param name="id">Identifier of the task.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or a failure when no such task exists.</returns>
    /// <remarks>
    /// Once the attempt count reaches three the task is treated as undeliverable and is no longer
    /// returned by the due-reminder query, so a permanently failing send cannot loop forever.
    /// </remarks>
    Task<Result> RecordDeliveryFailureAsync(Guid id, CancellationToken ct);

    /// <summary>Lists tasks matching a filter.</summary>
    /// <param name="request">Which tasks to return, and how many.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Matching tasks, ordered by due time with undated tasks last.</returns>
    Task<IReadOnlyList<ReminderTask>> QueryAsync(ListTasksRequest request, CancellationToken ct);
}
```

`src/Assistant.Interfaces/INotifier.cs`:

```csharp
using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>Delivers messages to the user.</summary>
public interface INotifier
{
    /// <summary>Sends a reminder for a single task, with its action buttons attached.</summary>
    /// <param name="notification">What to remind the user about.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the message has been accepted for delivery.</returns>
    /// <exception cref="Exception">
    /// Thrown when delivery fails. Callers must treat a thrown exception as "not delivered" and
    /// must not mark the reminder sent.
    /// </exception>
    Task SendReminderAsync(ReminderNotification notification, CancellationToken ct);

    /// <summary>Sends a single message covering several overdue tasks at once.</summary>
    /// <param name="notifications">The overdue tasks to summarise.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the message has been accepted for delivery.</returns>
    Task SendOverdueSummaryAsync(IReadOnlyList<ReminderNotification> notifications, CancellationToken ct);

    /// <summary>Sends the daily brief.</summary>
    /// <param name="brief">What to include.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the message has been accepted for delivery.</returns>
    Task SendDailyBriefAsync(DailyBriefNotification brief, CancellationToken ct);

    /// <summary>Sends a plain text message with no buttons.</summary>
    /// <param name="text">The message body. May contain the supported HTML subset.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the message has been accepted for delivery.</returns>
    Task SendTextAsync(string text, CancellationToken ct);
}
```

`src/Assistant.Interfaces/IMessageHandler.cs`:

```csharp
namespace Assistant.Interfaces;

/// <summary>Handles one inbound text message from the user.</summary>
public interface IMessageHandler
{
    /// <summary>Processes a message and replies to it.</summary>
    /// <param name="senderUserId">The messaging platform's identifier for the sender.</param>
    /// <param name="text">The message body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A task that completes once the message has been handled and any reply sent. Messages from
    /// anyone other than the configured owner are discarded without a reply and without any
    /// language model call.
    /// </returns>
    Task HandleAsync(long senderUserId, string text, CancellationToken ct);
}
```

`src/Assistant.Interfaces/ICallbackHandler.cs`:

```csharp
namespace Assistant.Interfaces;

/// <summary>Handles one inbound button press.</summary>
public interface ICallbackHandler
{
    /// <summary>Processes a button press and updates the originating message.</summary>
    /// <param name="senderUserId">The messaging platform's identifier for the sender.</param>
    /// <param name="callbackId">Identifier the platform requires in order to acknowledge the press.</param>
    /// <param name="messageId">Identifier of the message the button belongs to.</param>
    /// <param name="callbackData">The button's payload, in the form <c>v1:action:id[:arg]</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A task that completes once the press has been acknowledged. The press is always
    /// acknowledged, including on failure, because an unacknowledged press leaves the user's
    /// client showing a spinner indefinitely.
    /// </returns>
    Task HandleAsync(long senderUserId, string callbackId, int messageId, string callbackData, CancellationToken ct);
}
```

- [ ] **Step 7: Write the extension-point interfaces**

`src/Assistant.Interfaces/ITaskAction.cs`:

```csharp
using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>One behaviour reachable from a message button.</summary>
/// <remarks>
/// Adding a button means adding an implementation of this interface with a new <see cref="Key"/>.
/// No existing type changes.
/// </remarks>
public interface ITaskAction
{
    /// <summary>The token identifying this action inside a button payload.</summary>
    /// <value>Lowercase, no colons, kept short because the payload budget is 64 bytes.</value>
    string Key { get; }

    /// <summary>Applies the action to a task.</summary>
    /// <param name="taskId">Identifier of the task the button belongs to.</param>
    /// <param name="argument">
    /// The action's optional argument from the payload, such as a snooze duration.
    /// <see langword="null"/> when the payload carried none.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The text to show the user as a short confirmation, and whether the originating message's
    /// buttons should be removed.
    /// </returns>
    Task<(Result Result, string UserMessage, bool RemoveButtons)> ExecuteAsync(
        Guid taskId, string? argument, CancellationToken ct);
}
```

`src/Assistant.Interfaces/IAssistantTool.cs`:

```csharp
namespace Assistant.Interfaces;

/// <summary>One capability the language model may invoke.</summary>
/// <remarks>
/// Adding a capability means adding an implementation of this interface. Registration is by
/// convention, so no existing type changes.
/// </remarks>
public interface IAssistantTool
{
    /// <summary>The tool's name as exposed to the model.</summary>
    /// <value>Lowercase snake case, for example <c>create_task</c>.</value>
    string Name { get; }

    /// <summary>What the tool does, written for the model rather than for a developer.</summary>
    string Description { get; }

    /// <summary>The JSON Schema describing the tool's parameters.</summary>
    /// <value>A JSON object schema serialised as text.</value>
    string ParametersJsonSchema { get; }

    /// <summary>Invokes the tool.</summary>
    /// <param name="argumentsJson">The model's arguments, as a JSON object.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Text to return to the model as the tool's result. On rejection this is the explanation,
    /// so the model can ask the user a follow-up question rather than failing silently.
    /// </returns>
    Task<string> InvokeAsync(string argumentsJson, CancellationToken ct);
}
```

`src/Assistant.Interfaces/IScheduledJob.cs`:

```csharp
namespace Assistant.Interfaces;

/// <summary>One unit of recurring work run by the scheduler.</summary>
/// <remarks>
/// The scheduler resolves every implementation and runs each on every tick; it knows nothing
/// about what any of them do. Adding a job changes no existing type.
/// </remarks>
public interface IScheduledJob
{
    /// <summary>Name used in logs.</summary>
    string Name { get; }

    /// <summary>Performs one pass of the job's work.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A task that completes when the pass is finished. Implementations decide for themselves
    /// whether a given tick is one on which they should act.
    /// </returns>
    /// <remarks>
    /// A pass that throws is logged and swallowed by the scheduler: one failing job must never
    /// stop the others or terminate the host.
    /// </remarks>
    Task RunAsync(CancellationToken ct);
}
```

`src/Assistant.Interfaces/IAgent.cs`:

```csharp
using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>Runs the language model tool loop for one user message.</summary>
public interface IAgent
{
    /// <summary>Interprets a message, invoking tools as the model requests them.</summary>
    /// <param name="userText">The user's message.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The reply to show the user, and the identifier of a task the turn created, if any, so the
    /// caller can attach action buttons. A failure carrying
    /// <see cref="ErrorCode.LlmUnavailable"/> when every provider failed.
    /// </returns>
    Task<(Result Result, string ReplyText, Guid? CreatedTaskId)> RunAsync(string userText, CancellationToken ct);
}
```

- [ ] **Step 8: Write the remaining architecture tests**

`tests/Assistant.UnitTests/Architecture/ConventionTests.cs`:

```csharp
using System.Reflection;
using Assistant.Contracts;
using Assistant.Interfaces;
using Assistant.Models;
using Shouldly;
using Xunit;

namespace Assistant.UnitTests.Architecture;

public class ConventionTests
{
    private static Assembly ModelsAssembly => typeof(ReminderTask).Assembly;
    private static Assembly InterfacesAssembly => typeof(IClock).Assembly;
    private static Assembly ContractsAssembly => typeof(Result).Assembly;

    [Fact]
    public void Models_declare_no_methods_beyond_property_accessors()
    {
        var offenders = ModelsAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsPublic: true })
            .SelectMany(t => t.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        offenders.ShouldBeEmpty(
            "Models are POCOs; behaviour belongs in TaskService. Offenders: " + string.Join(", ", offenders));
    }

    [Fact]
    public void No_repository_method_returns_IQueryable()
    {
        var offenders = InterfacesAssembly.GetTypes()
            .Where(t => t.IsInterface && t.Name.EndsWith("Repository", StringComparison.Ordinal))
            .SelectMany(t => t.GetMethods())
            .Where(m => m.ReturnType.Name.StartsWith("IQueryable", StringComparison.Ordinal))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        offenders.ShouldBeEmpty(
            "IQueryable leaks EF Core through the interface; return IReadOnlyList. Offenders: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void Contracts_declares_no_interfaces()
    {
        var offenders = ContractsAssembly.GetTypes()
            .Where(t => t is { IsInterface: true, IsPublic: true })
            .Select(t => t.Name)
            .ToList();

        offenders.ShouldBeEmpty(
            "Contracts holds request/response types; interfaces belong in Assistant.Interfaces. Offenders: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void Interfaces_declares_no_concrete_public_classes()
    {
        var offenders = InterfacesAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsPublic: true, IsAbstract: false })
            .Select(t => t.Name)
            .ToList();

        offenders.ShouldBeEmpty(
            "Assistant.Interfaces holds abstractions only. Offenders: " + string.Join(", ", offenders));
    }

    [Fact]
    public void No_type_is_named_Task_and_no_enum_is_named_TaskStatus()
    {
        var offenders = new[] { ModelsAssembly, ContractsAssembly, InterfacesAssembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name is "Task" or "TaskStatus")
            .Select(t => $"{t.Assembly.GetName().Name}.{t.Name}")
            .ToList();

        offenders.ShouldBeEmpty(
            "These collide with System.Threading.Tasks. Use ReminderTask and ReminderStatus. Offenders: "
            + string.Join(", ", offenders));
    }
}
```

`tests/Assistant.UnitTests/Architecture/DependencyRuleTests.cs`:

```csharp
using Assistant.Interfaces;
using Assistant.Models;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace Assistant.UnitTests.Architecture;

public class DependencyRuleTests
{
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Npgsql")]
    public void Models_do_not_depend_on_persistence_libraries(string forbidden)
    {
        var result = Types.InAssembly(typeof(ReminderTask).Assembly)
            .ShouldNot().HaveDependencyOn(forbidden)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Models must not depend on {forbidden}. Offenders: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Npgsql")]
    [InlineData("Telegram.Bot")]
    [InlineData("Refit")]
    public void Interfaces_do_not_depend_on_infrastructure_libraries(string forbidden)
    {
        var result = Types.InAssembly(typeof(IClock).Assembly)
            .ShouldNot().HaveDependencyOn(forbidden)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Assistant.Interfaces must stay free of {forbidden}. Offenders: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
```

- [ ] **Step 9: Run the architecture suite**

Run: `dotnet test tests/Assistant.UnitTests`
Expected: PASS. All reference-graph, convention, and dependency tests green.

- [ ] **Step 10: Confirm XML documentation enforcement actually works**

Temporarily delete the `<summary>` block above `IClock.UtcNow` and run:

Run: `dotnet build src/Assistant.Interfaces`
Expected: FAIL with `error CS1591: Missing XML comment for publicly visible type or member 'IClock.UtcNow'`.

Restore the comment and rebuild. Expected: succeeds. This proves the documentation rule is enforced rather than aspirational.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "feat: declare models, contracts, and interfaces with enforced conventions"
```

---

## Task 3: Repository, migrations, and the integration test harness

Spec §9 step 3, §3.2, §4.3, §7.1. Postgres comes from Docker Compose, not Testcontainers.

**Files:**
- Create: `compose.test.yaml`, `.config/dotnet-tools.json`
- Create: `src/Assistant.Repository/AssistantDbContext.cs`
- Create: `src/Assistant.Repository/Configurations/{ReminderTaskConfiguration,ChatMessageConfiguration,DailyBriefLogConfiguration}.cs`
- Create: `src/Assistant.Repository/{EfTaskRepository,EfChatMessageRepository,EfDailyBriefRepository,RepositoryServiceCollectionExtensions}.cs`
- Create: `src/Assistant.Repository/Migrations/` (generated)
- Create: `tests/Assistant.IntegrationTests/Infrastructure/PostgresFixture.cs`
- Create: `tests/Assistant.IntegrationTests/Repository/TaskRepositoryTests.cs`

**Interfaces:**
- Consumes: `ITaskRepository`, `IChatMessageRepository`, `IDailyBriefRepository`, `ReminderTask`, `ChatMessage`, `DailyBriefLog`, `TaskFilter` from Task 2.
- Produces:
  - `RepositoryServiceCollectionExtensions.AddAssistantRepository(this IServiceCollection services, string connectionString)` — registers the context and all three repositories, and applies migrations.
  - `PostgresFixture` with `string ConnectionString { get; }`, `Task ResetAsync()`, and xUnit collection name `"postgres"`.

- [ ] **Step 1: Add packages and the EF tool manifest**

```bash
dotnet add src/Assistant.Repository package Microsoft.EntityFrameworkCore
dotnet add src/Assistant.Repository package Microsoft.EntityFrameworkCore.Relational
dotnet add src/Assistant.Repository package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Assistant.Repository package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add src/Assistant.Worker  package Microsoft.EntityFrameworkCore.Design

dotnet add tests/Assistant.IntegrationTests package Shouldly
dotnet add tests/Assistant.IntegrationTests package Respawn
dotnet add tests/Assistant.IntegrationTests package Npgsql

dotnet new tool-manifest
dotnet tool install dotnet-ef
```

`Microsoft.EntityFrameworkCore.Design` goes on `Worker` because that is the startup project for migrations. It is a design-time-only dependency and does not put EF's runtime API into `Worker`'s reachable surface for the purposes of the dependency tests.

- [ ] **Step 2: Write `compose.test.yaml`**

```yaml
services:
  postgres-test:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: assistant_test
      POSTGRES_USER: assistant
      POSTGRES_PASSWORD: assistant
    ports:
      - "55432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U assistant -d assistant_test"]
      interval: 2s
      timeout: 3s
      retries: 15
    tmpfs:
      - /var/lib/postgresql/data
```

Port 55432 avoids colliding with a local Postgres on 5432. `tmpfs` keeps the data directory in memory, which makes the test database noticeably faster and guarantees a clean slate whenever the container is recreated.

- [ ] **Step 3: Write the DbContext**

`src/Assistant.Repository/AssistantDbContext.cs`:

```csharp
using Assistant.Models;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Repository;

/// <summary>Entity Framework context for the assistant's tables.</summary>
/// <remarks>
/// Internal to this project by design: no other assembly names a context or a
/// <see cref="DbSet{TEntity}"/>. Callers go through the repository interfaces.
/// </remarks>
internal sealed class AssistantDbContext : DbContext
{
    /// <summary>Initialises the context.</summary>
    /// <param name="options">Provider and connection configuration.</param>
    public AssistantDbContext(DbContextOptions<AssistantDbContext> options) : base(options)
    {
    }

    /// <summary>The tasks table.</summary>
    public DbSet<ReminderTask> ReminderTasks => Set<ReminderTask>();

    /// <summary>The conversation window table.</summary>
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    /// <summary>The record of which days the brief has been sent.</summary>
    public DbSet<DailyBriefLog> DailyBriefLogs => Set<DailyBriefLog>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AssistantDbContext).Assembly);
}
```

- [ ] **Step 4: Write the entity configurations**

`src/Assistant.Repository/Configurations/ReminderTaskConfiguration.cs`:

```csharp
using Assistant.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Repository.Configurations;

internal sealed class ReminderTaskConfiguration : IEntityTypeConfiguration<ReminderTask>
{
    public void Configure(EntityTypeBuilder<ReminderTask> builder)
    {
        builder.ToTable("reminder_tasks", t =>
        {
            t.HasCheckConstraint(
                "ck_reminder_tasks_completed_consistency",
                "(status = 1) = (completed_at IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_reminder_tasks_sent_requires_due",
                "reminder_sent_at IS NULL OR due_at IS NOT NULL");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Title).HasColumnName("title").IsRequired().HasMaxLength(500);
        builder.Property(x => x.Notes).HasColumnName("notes");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.Priority).HasColumnName("priority").HasConversion<int>();
        builder.Property(x => x.DueAt).HasColumnName("due_at").HasColumnType("timestamptz");
        builder.Property(x => x.ReminderSentAt).HasColumnName("reminder_sent_at").HasColumnType("timestamptz");
        builder.Property(x => x.DeliveryAttempts).HasColumnName("delivery_attempts");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamptz");

        // Supports the due-reminder query. Filtered so it stays small: the vast majority of rows
        // are either already delivered or no longer pending.
        builder.HasIndex(x => x.DueAt)
            .HasDatabaseName("ix_reminder_tasks_due_pending")
            .HasFilter("status = 0 AND reminder_sent_at IS NULL");
    }
}
```

`src/Assistant.Repository/Configurations/ChatMessageConfiguration.cs`:

```csharp
using Assistant.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Repository.Configurations;

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("chat_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Role).HasColumnName("role").IsRequired().HasMaxLength(20);
        builder.Property(x => x.Content).HasColumnName("content").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");

        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_chat_messages_created_at")
            .IsDescending();
    }
}
```

`src/Assistant.Repository/Configurations/DailyBriefLogConfiguration.cs`:

```csharp
using Assistant.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Repository.Configurations;

internal sealed class DailyBriefLogConfiguration : IEntityTypeConfiguration<DailyBriefLog>
{
    public void Configure(EntityTypeBuilder<DailyBriefLog> builder)
    {
        builder.ToTable("daily_brief_log");

        // The date is the key, which is what makes "one brief per day" a database guarantee
        // rather than something the application has to coordinate.
        builder.HasKey(x => x.BriefDate);
        builder.Property(x => x.BriefDate).HasColumnName("brief_date").HasColumnType("date");
        builder.Property(x => x.SentAt).HasColumnName("sent_at").HasColumnType("timestamptz");
    }
}
```

- [ ] **Step 5: Write the repositories**

`src/Assistant.Repository/EfTaskRepository.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Repository;

/// <summary>Entity Framework implementation of <see cref="ITaskRepository"/>.</summary>
internal sealed class EfTaskRepository : ITaskRepository
{
    /// <summary>Attempts beyond this count are treated as undeliverable.</summary>
    private const int MaxDeliveryAttempts = 3;

    private readonly AssistantDbContext _db;

    /// <summary>Initialises the repository.</summary>
    /// <param name="db">The context to read and write through.</param>
    public EfTaskRepository(AssistantDbContext db) => _db = db;

    /// <inheritdoc/>
    public Task<ReminderTask?> FindAsync(Guid id, CancellationToken ct)
        => _db.ReminderTasks.FirstOrDefaultAsync(t => t.Id == id, ct);

    /// <inheritdoc/>
    public async Task AddAsync(ReminderTask task, CancellationToken ct)
    {
        _db.ReminderTasks.Add(task);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(ReminderTask task, CancellationToken ct)
    {
        _db.ReminderTasks.Update(task);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReminderTask>> GetDueRemindersAsync(
        DateTimeOffset asOfUtc, int limit, CancellationToken ct)
        => await _db.ReminderTasks
            .Where(t => t.Status == ReminderStatus.Pending
                        && t.DueAt != null
                        && t.DueAt <= asOfUtc
                        && t.ReminderSentAt == null
                        && t.DeliveryAttempts < MaxDeliveryAttempts)
            .OrderBy(t => t.DueAt)
            .Take(limit)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReminderTask>> QueryAsync(
        TaskFilter filter, DateTimeOffset asOfUtc, int limit, CancellationToken ct)
    {
        var query = _db.ReminderTasks.Where(t => t.Status == ReminderStatus.Pending);

        query = filter switch
        {
            TaskFilter.Today => query.Where(t => t.DueAt != null && t.DueAt < asOfUtc.Date.AddDays(1)),
            TaskFilter.Overdue => query.Where(t => t.DueAt != null && t.DueAt <= asOfUtc),
            TaskFilter.Week => query.Where(t => t.DueAt != null && t.DueAt < asOfUtc.AddDays(7)),
            TaskFilter.All => query,
            _ => query,
        };

        return await query
            .OrderBy(t => t.DueAt == null)
            .ThenBy(t => t.DueAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public Task<int> CountOpenWithoutDueDateAsync(CancellationToken ct)
        => _db.ReminderTasks.CountAsync(
            t => t.Status == ReminderStatus.Pending && t.DueAt == null, ct);
}
```

`src/Assistant.Repository/EfChatMessageRepository.cs`:

```csharp
using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Repository;

/// <summary>Entity Framework implementation of <see cref="IChatMessageRepository"/>.</summary>
internal sealed class EfChatMessageRepository : IChatMessageRepository
{
    private readonly AssistantDbContext _db;

    /// <summary>Initialises the repository.</summary>
    /// <param name="db">The context to read and write through.</param>
    public EfChatMessageRepository(AssistantDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task AppendAsync(ChatMessage message, CancellationToken ct)
    {
        _db.ChatMessages.Add(message);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ChatMessage>> GetRecentAsync(int limit, CancellationToken ct)
    {
        var newestFirst = await _db.ChatMessages
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        newestFirst.Reverse();
        return newestFirst;
    }
}
```

`src/Assistant.Repository/EfDailyBriefRepository.cs`:

```csharp
using Assistant.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Repository;

/// <summary>Entity Framework implementation of <see cref="IDailyBriefRepository"/>.</summary>
internal sealed class EfDailyBriefRepository : IDailyBriefRepository
{
    private readonly AssistantDbContext _db;

    /// <summary>Initialises the repository.</summary>
    /// <param name="db">The context to read and write through.</param>
    public EfDailyBriefRepository(AssistantDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task<bool> TryClaimAsync(DateOnly briefDate, DateTimeOffset nowUtc, CancellationToken ct)
    {
        // A conditional insert rather than a read-then-write: the primary key does the arbitration,
        // so there is no window in which two callers both believe they claimed the date.
        var rowsInserted = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO daily_brief_log (brief_date, sent_at)
             VALUES ({briefDate}, {nowUtc})
             ON CONFLICT (brief_date) DO NOTHING
             """,
            ct);

        return rowsInserted == 1;
    }
}
```

- [ ] **Step 6: Write the registration extension**

`src/Assistant.Repository/RepositoryServiceCollectionExtensions.cs`:

```csharp
using Assistant.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Repository;

/// <summary>Registers the assistant's persistence layer.</summary>
/// <remarks>
/// This is the only way in. Nothing outside this assembly names an Entity Framework type, which
/// is what keeps the persistence technology replaceable without touching any service.
/// </remarks>
public static class RepositoryServiceCollectionExtensions
{
    /// <summary>Registers the database context and every repository implementation.</summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <param name="connectionString">A Npgsql connection string for the assistant database.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAssistantRepository(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AssistantDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ITaskRepository, EfTaskRepository>();
        services.AddScoped<IChatMessageRepository, EfChatMessageRepository>();
        services.AddScoped<IDailyBriefRepository, EfDailyBriefRepository>();
        return services;
    }

    /// <summary>Applies any outstanding database migrations.</summary>
    /// <param name="provider">A provider from which a scoped context can be resolved.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the schema is up to date.</returns>
    /// <remarks>
    /// Called once at startup. Safe to call when the schema is already current.
    /// </remarks>
    public static async Task MigrateAssistantDatabaseAsync(
        this IServiceProvider provider, CancellationToken ct = default)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssistantDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
```

- [ ] **Step 7: Generate the initial migration**

```bash
docker compose -f compose.test.yaml up -d

dotnet ef migrations add InitialSchema \
  --project src/Assistant.Repository \
  --startup-project src/Assistant.Worker
```

If `dotnet ef` cannot find a `DbContext`, it is because `AssistantDbContext` is internal and `Worker` has no design-time factory yet. Add one:

`src/Assistant.Repository/DesignTimeDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Assistant.Repository;

/// <summary>Supplies a context to the Entity Framework command-line tools at design time.</summary>
/// <remarks>
/// Used only by <c>dotnet ef</c>. The connection string here points at the local test database
/// and is never used at runtime.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AssistantDbContext>
{
    /// <summary>Creates a context for the tooling.</summary>
    /// <param name="args">Arguments passed by the tooling. Unused.</param>
    /// <returns>A context configured against the local test database.</returns>
    public AssistantDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AssistantDbContext>()
            .UseNpgsql("Host=localhost;Port=55432;Database=assistant_test;Username=assistant;Password=assistant")
            .Options;

        return new AssistantDbContext(options);
    }
}
```

This forces `AssistantDbContext` to become `public` rather than `internal`, since a public factory cannot expose an internal type. That is acceptable: the dependency test in Task 2 already prevents any other assembly from depending on EF Core, so `public` here changes nothing enforceable.

Change `internal sealed class AssistantDbContext` to `public sealed class AssistantDbContext` and add XML documentation to the class and its members if not already present. Then re-run the migration command.

- [ ] **Step 8: Write the Postgres fixture**

`tests/Assistant.IntegrationTests/Infrastructure/PostgresFixture.cs`:

```csharp
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Xunit;

namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>
/// Shared connection to the Postgres instance defined in <c>compose.test.yaml</c>.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=55432;Database=assistant_test;Username=assistant;Password=assistant;Include Error Detail=true";

    private Respawner? _respawner;

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("ASSISTANT_TEST_DB") ?? DefaultConnectionString;

    public async Task InitializeAsync()
    {
        await WaitForServerAsync();

        var services = new ServiceCollection();
        services.AddAssistantRepository(ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await provider.MigrateAssistantDatabaseAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Respawn.Graph.Table("public", "__EFMigrationsHistory")],
        });
    }

    /// <summary>Truncates every table, leaving the schema in place.</summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner!.ResetAsync(connection);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Polls until the server accepts a connection. Compose returning does not mean Postgres is
    /// listening, and this is the single most common cause of a flaky first test in CI.
    /// </summary>
    private async Task WaitForServerAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        Exception? last = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(500);
            }
        }

        throw new InvalidOperationException(
            "Postgres did not become available within 60s. Run: docker compose -f compose.test.yaml up -d",
            last);
    }
}

/// <summary>Groups every test that shares the Postgres instance.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    /// <summary>The collection name to put on test classes.</summary>
    public const string Name = "postgres";
}
```

- [ ] **Step 9: Write the failing repository tests**

`tests/Assistant.IntegrationTests/Repository/TaskRepositoryTests.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Interfaces;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Models;
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Xunit;

namespace Assistant.IntegrationTests.Repository;

[Collection(PostgresCollection.Name)]
public sealed class TaskRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private ServiceProvider _provider = null!;

    public TaskRepositoryTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        await _postgres.ResetAsync();
        var services = new ServiceCollection();
        services.AddAssistantRepository(_postgres.ConnectionString);
        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private static DateTimeOffset Utc(string iso) =>
        DateTimeOffset.Parse(iso, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

    private async Task<T> WithRepositoryAsync<T>(Func<ITaskRepository, Task<T>> work)
    {
        using var scope = _provider.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ITaskRepository>());
    }

    private static ReminderTask NewTask(
        string title,
        DateTimeOffset? dueAt = null,
        ReminderStatus status = ReminderStatus.Pending,
        DateTimeOffset? sentAt = null,
        int attempts = 0) => new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Status = status,
            Priority = Priority.Normal,
            DueAt = dueAt,
            ReminderSentAt = sentAt,
            DeliveryAttempts = attempts,
            CreatedAt = Utc("2026-08-01T00:00:00Z"),
            UpdatedAt = Utc("2026-08-01T00:00:00Z"),
            CompletedAt = status == ReminderStatus.Completed ? Utc("2026-08-01T00:00:00Z") : null,
        };

    [Fact]
    public async Task Round_trips_a_task_with_every_field_preserved()
    {
        var original = NewTask("Call the bank", Utc("2026-08-17T07:00:00Z"));

        await WithRepositoryAsync(async r => { await r.AddAsync(original, default); return 0; });
        var loaded = await WithRepositoryAsync(r => r.FindAsync(original.Id, default));

        loaded.ShouldNotBeNull();
        loaded!.Title.ShouldBe("Call the bank");
        loaded.DueAt.ShouldBe(Utc("2026-08-17T07:00:00Z"));
        loaded.Status.ShouldBe(ReminderStatus.Pending);
        loaded.ReminderSentAt.ShouldBeNull();
        loaded.DeliveryAttempts.ShouldBe(0);
    }

    [Fact]
    public async Task Due_reminders_include_overdue_tasks_with_no_lower_bound()
    {
        // The task was due three days ago. It must still be returned, because that is what makes
        // a reminder survive an outage rather than being silently skipped.
        var stale = NewTask("Stale", Utc("2026-08-14T07:00:00Z"));
        var future = NewTask("Future", Utc("2026-08-20T07:00:00Z"));

        await WithRepositoryAsync(async r =>
        {
            await r.AddAsync(stale, default);
            await r.AddAsync(future, default);
            return 0;
        });

        var due = await WithRepositoryAsync(
            r => r.GetDueRemindersAsync(Utc("2026-08-17T07:00:00Z"), 50, default));

        due.Select(t => t.Title).ShouldBe(new[] { "Stale" });
    }

    [Fact]
    public async Task Due_reminders_exclude_already_delivered_tasks()
    {
        var delivered = NewTask("Delivered", Utc("2026-08-17T06:00:00Z"), sentAt: Utc("2026-08-17T06:00:01Z"));
        await WithRepositoryAsync(async r => { await r.AddAsync(delivered, default); return 0; });

        var due = await WithRepositoryAsync(
            r => r.GetDueRemindersAsync(Utc("2026-08-17T07:00:00Z"), 50, default));

        due.ShouldBeEmpty();
    }

    [Fact]
    public async Task Due_reminders_exclude_tasks_that_have_failed_three_times()
    {
        var exhausted = NewTask("Exhausted", Utc("2026-08-17T06:00:00Z"), attempts: 3);
        await WithRepositoryAsync(async r => { await r.AddAsync(exhausted, default); return 0; });

        var due = await WithRepositoryAsync(
            r => r.GetDueRemindersAsync(Utc("2026-08-17T07:00:00Z"), 50, default));

        due.ShouldBeEmpty();
    }

    [Fact]
    public async Task Due_reminders_are_ordered_oldest_first_and_respect_the_limit()
    {
        await WithRepositoryAsync(async r =>
        {
            await r.AddAsync(NewTask("Third", Utc("2026-08-17T03:00:00Z")), default);
            await r.AddAsync(NewTask("First", Utc("2026-08-17T01:00:00Z")), default);
            await r.AddAsync(NewTask("Second", Utc("2026-08-17T02:00:00Z")), default);
            return 0;
        });

        var due = await WithRepositoryAsync(
            r => r.GetDueRemindersAsync(Utc("2026-08-17T07:00:00Z"), 2, default));

        due.Select(t => t.Title).ShouldBe(new[] { "First", "Second" });
    }

    [Fact]
    public async Task Query_puts_undated_tasks_last()
    {
        await WithRepositoryAsync(async r =>
        {
            await r.AddAsync(NewTask("Undated"), default);
            await r.AddAsync(NewTask("Dated", Utc("2026-08-18T07:00:00Z")), default);
            return 0;
        });

        var all = await WithRepositoryAsync(
            r => r.QueryAsync(TaskFilter.All, Utc("2026-08-17T07:00:00Z"), 50, default));

        all.Select(t => t.Title).ShouldBe(new[] { "Dated", "Undated" });
    }

    [Fact]
    public async Task Completed_consistency_constraint_rejects_a_completed_task_with_no_completion_time()
    {
        var inconsistent = NewTask("Broken", Utc("2026-08-17T07:00:00Z"), ReminderStatus.Completed);
        inconsistent.CompletedAt = null;

        var ex = await Should.ThrowAsync<Exception>(async () =>
            await WithRepositoryAsync(async r => { await r.AddAsync(inconsistent, default); return 0; }));

        // The database, not the application, is what refuses this.
        ex.ToString().ShouldContain("ck_reminder_tasks_completed_consistency");
    }

    [Fact]
    public async Task Claiming_a_brief_date_succeeds_once_and_then_reports_already_claimed()
    {
        using var scope = _provider.CreateScope();
        var briefs = scope.ServiceProvider.GetRequiredService<IDailyBriefRepository>();
        var date = new DateOnly(2026, 8, 17);

        var first = await briefs.TryClaimAsync(date, Utc("2026-08-17T04:00:00Z"), default);
        var second = await briefs.TryClaimAsync(date, Utc("2026-08-17T16:00:00Z"), default);

        first.ShouldBeTrue();
        second.ShouldBeFalse("the primary key must arbitrate, so a restart cannot double-send");
    }

    [Fact]
    public async Task Chat_messages_return_oldest_first_within_the_window()
    {
        using var scope = _provider.CreateScope();
        var messages = scope.ServiceProvider.GetRequiredService<IChatMessageRepository>();

        for (var i = 0; i < 5; i++)
        {
            await messages.AppendAsync(new ChatMessage
            {
                Id = Guid.NewGuid(),
                Role = "user",
                Content = $"message {i}",
                CreatedAt = Utc("2026-08-17T00:00:00Z").AddMinutes(i),
            }, default);
        }

        var recent = await messages.GetRecentAsync(3, default);

        recent.Select(m => m.Content).ShouldBe(new[] { "message 2", "message 3", "message 4" });
    }
}
```

- [ ] **Step 10: Run the tests and watch them fail, then pass**

```bash
docker compose -f compose.test.yaml up -d
dotnet test tests/Assistant.IntegrationTests
```

Expected on the first run before migrations exist: FAIL with a relation-not-found error. After Step 7's migration is generated and `InitializeAsync` applies it: PASS, 9 tests.

If `Round_trips_a_task_with_every_field_preserved` fails with a message about `DateTimeOffset` offsets, a test value was constructed with a non-zero offset. Npgsql requires `Offset == TimeSpan.Zero` for `timestamptz`; the `Utc` helper enforces this and must be used for every instant.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "feat: postgres repository with migrations and compose-based integration harness"
```

---

## Task 4: Local time resolution and the guard clauses

Spec §5.4. Pure logic, so it is unit tested — an integration test cannot reach a daylight-saving boundary cheaply.

**Files:**
- Create: `src/Assistant.Impl/Services/LocalTimeResolver.cs`
- Create: `src/Assistant.Interfaces/ILocalTimeResolver.cs`
- Create: `tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs`

**Interfaces:**
- Consumes: `IClock`, `Result`, `ErrorCode` from Task 2.
- Produces:
  - `ILocalTimeResolver.Resolve(string localIso)` returning `(Result Result, DateTimeOffset? UtcInstant)`
  - `ILocalTimeResolver.ToLocal(DateTimeOffset utcInstant)` returning `DateTimeOffset`
  - `ILocalTimeResolver.LocalToday` returning `DateOnly`
  - `ILocalTimeResolver.DescribeNowForPrompt()` returning `string`
  - `LocalTimeResolver.TimeZoneId` constant `"Asia/Jerusalem"`

- [ ] **Step 1: Declare the interface**

`src/Assistant.Interfaces/ILocalTimeResolver.cs`:

```csharp
using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>Converts between the user's local wall clock and UTC instants.</summary>
/// <remarks>
/// Slice 1 fixes the zone to a single value. Every instant crossing into persistence is UTC;
/// every instant shown to the user is local.
/// </remarks>
public interface ILocalTimeResolver
{
    /// <summary>Converts an absolute local ISO-8601 datetime to a UTC instant.</summary>
    /// <param name="localIso">
    /// A datetime with no offset, for example <c>2026-08-17T10:00:00</c>, as produced by the
    /// language model.
    /// </param>
    /// <returns>
    /// The UTC instant on success. A failure carrying <see cref="ErrorCode.TimeUnparseable"/>,
    /// <see cref="ErrorCode.TimeInPast"/>, or <see cref="ErrorCode.TimeTooFarAhead"/> otherwise,
    /// with a message suitable for relaying to the user.
    /// </returns>
    /// <remarks>
    /// A local time that does not exist because the clocks moved forward is shifted past the gap.
    /// A local time that occurs twice because the clocks moved back resolves to the first
    /// occurrence.
    /// </remarks>
    (Result Result, DateTimeOffset? UtcInstant) Resolve(string localIso);

    /// <summary>Renders a UTC instant in the user's local time.</summary>
    /// <param name="utcInstant">The instant to convert.</param>
    /// <returns>The same instant with the local offset applied.</returns>
    DateTimeOffset ToLocal(DateTimeOffset utcInstant);

    /// <summary>The current date on the user's local calendar.</summary>
    /// <value>Used to key the daily brief, which is a local-calendar concept.</value>
    DateOnly LocalToday { get; }

    /// <summary>Describes the current local time for injection into the model's prompt.</summary>
    /// <returns>
    /// A sentence naming the weekday, date, time, zone, and offset, without which the model has
    /// no basis for resolving a phrase like "tomorrow".
    /// </returns>
    string DescribeNowForPrompt();
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Impl.Services;
using Assistant.Interfaces;
using Shouldly;
using Xunit;

namespace Assistant.UnitTests.Services;

public class LocalTimeResolverTests
{
    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private static (LocalTimeResolver Resolver, StubClock Clock) Build(string nowUtcIso)
    {
        var clock = new StubClock
        {
            UtcNow = DateTimeOffset.Parse(nowUtcIso, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal),
        };
        return (new LocalTimeResolver(clock), clock);
    }

    [Fact]
    public void Resolves_summer_local_time_to_utc_minus_three_hours()
    {
        // Israel is UTC+3 in August.
        var (resolver, _) = Build("2026-08-16T20:00:00Z");

        var (result, utc) = resolver.Resolve("2026-08-17T10:00:00");

        result.Succeeded.ShouldBeTrue();
        utc.ShouldBe(DateTimeOffset.Parse("2026-08-17T07:00:00Z", null,
            System.Globalization.DateTimeStyles.AdjustToUniversal));
        utc!.Value.Offset.ShouldBe(TimeSpan.Zero, "Npgsql requires a zero offset for timestamptz");
    }

    [Fact]
    public void Resolves_winter_local_time_to_utc_minus_two_hours()
    {
        // Israel is UTC+2 in January.
        var (resolver, _) = Build("2026-01-10T06:00:00Z");

        var (result, utc) = resolver.Resolve("2026-01-11T10:00:00");

        result.Succeeded.ShouldBeTrue();
        utc.ShouldBe(DateTimeOffset.Parse("2026-01-11T08:00:00Z", null,
            System.Globalization.DateTimeStyles.AdjustToUniversal));
    }

    [Fact]
    public void Rejects_a_time_more_than_a_minute_in_the_past()
    {
        var (resolver, _) = Build("2026-08-17T07:00:00Z");   // 10:00 local

        var (result, utc) = resolver.Resolve("2026-08-17T09:00:00");   // 09:00 local, an hour ago

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe(ErrorCode.TimeInPast);
        utc.ShouldBeNull();
    }

    [Fact]
    public void Accepts_a_time_a_few_seconds_in_the_past()
    {
        // Clock skew between the model's reasoning and our validation must not reject a
        // legitimate "remind me now".
        var (resolver, _) = Build("2026-08-17T07:00:00Z");

        var (result, _) = resolver.Resolve("2026-08-17T09:59:40");

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Rejects_a_time_more_than_two_years_ahead()
    {
        var (resolver, _) = Build("2026-08-17T07:00:00Z");

        var (result, _) = resolver.Resolve("2029-08-17T10:00:00");

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe(ErrorCode.TimeTooFarAhead);
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("")]
    [InlineData("2026-13-45T99:00:00")]
    public void Rejects_unparseable_text(string input)
    {
        var (resolver, _) = Build("2026-08-17T07:00:00Z");

        var (result, _) = resolver.Resolve(input);

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe(ErrorCode.TimeUnparseable);
    }

    [Fact]
    public void Shifts_a_local_time_that_does_not_exist_past_the_spring_forward_gap()
    {
        // Israel moves to summer time on the Friday before the last Sunday of March.
        // In 2027 that is 26 March, when 02:00 local jumps to 03:00 — 02:30 never happens.
        var (resolver, _) = Build("2027-03-01T00:00:00Z");

        var (result, utc) = resolver.Resolve("2027-03-26T02:30:00");

        result.Succeeded.ShouldBeTrue();
        utc.ShouldNotBeNull();

        // Whatever instant we land on, converting it back must give a local time at or after the
        // end of the gap. Asserting the shift rather than a hardcoded instant keeps this test
        // correct if the transition rule ever changes.
        resolver.ToLocal(utc!.Value).TimeOfDay.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromHours(3));
    }

    [Fact]
    public void Resolves_an_ambiguous_local_time_to_its_first_occurrence()
    {
        // Israel returns to standard time on the last Sunday of October. In 2026 that is
        // 25 October, when 02:00 local repeats — so 01:30 occurs twice.
        var (resolver, _) = Build("2026-10-01T00:00:00Z");

        var (result, utc) = resolver.Resolve("2026-10-25T01:30:00");

        result.Succeeded.ShouldBeTrue();

        var zone = TimeZoneInfo.FindSystemTimeZoneById(LocalTimeResolver.TimeZoneId);
        var local = new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Unspecified);
        var offsets = zone.GetAmbiguousTimeOffsets(local);
        var earliest = offsets.Max();   // the larger offset is the earlier instant

        utc!.Value.ShouldBe(new DateTimeOffset(local, earliest).ToUniversalTime());
    }

    [Fact]
    public void Local_today_follows_the_local_calendar_not_the_utc_one()
    {
        // 22:30 UTC is already the next day at 01:30 local in summer.
        var (resolver, _) = Build("2026-08-16T22:30:00Z");

        resolver.LocalToday.ShouldBe(new DateOnly(2026, 8, 17));
    }

    [Fact]
    public void Prompt_description_names_the_weekday_date_time_and_zone()
    {
        var (resolver, _) = Build("2026-08-16T20:40:00Z");   // Sunday 23:40 local

        var description = resolver.DescribeNowForPrompt();

        description.ShouldContain("Sunday");
        description.ShouldContain("16 August 2026");
        description.ShouldContain("23:40");
        description.ShouldContain("Asia/Jerusalem");
        description.ShouldContain("UTC+3");
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Assistant.UnitTests --filter LocalTimeResolverTests`
Expected: FAIL to compile — `Assistant.Impl.Services.LocalTimeResolver` does not exist.

- [ ] **Step 4: Implement**

`src/Assistant.Impl/Services/LocalTimeResolver.cs`:

```csharp
using System.Globalization;
using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Services;

/// <inheritdoc cref="ILocalTimeResolver"/>
public sealed class LocalTimeResolver : ILocalTimeResolver
{
    /// <summary>The IANA zone the assistant operates in.</summary>
    /// <remarks>
    /// Fixed in slice 1. Making this configurable is a small, self-contained change: bind it from
    /// options and inject it here.
    /// </remarks>
    public const string TimeZoneId = "Asia/Jerusalem";

    /// <summary>How far in the past a supplied time may be before it is rejected.</summary>
    /// <remarks>
    /// A small tolerance, because the model reasons about "now" a moment before validation runs.
    /// </remarks>
    private static readonly TimeSpan PastTolerance = TimeSpan.FromMinutes(1);

    /// <summary>How far ahead a supplied time may be before it is treated as a mistake.</summary>
    private static readonly TimeSpan FutureLimit = TimeSpan.FromDays(730);

    private static readonly string[] AcceptedFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
    ];

    private readonly IClock _clock;
    private readonly TimeZoneInfo _zone;

    /// <summary>Initialises the resolver.</summary>
    /// <param name="clock">Source of the current time.</param>
    /// <exception cref="TimeZoneNotFoundException">
    /// Thrown when the host has no timezone database. Ensure <c>InvariantGlobalization</c> is
    /// <see langword="false"/> and, on Alpine images, that <c>tzdata</c> is installed.
    /// </exception>
    public LocalTimeResolver(IClock clock)
    {
        _clock = clock;
        _zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
    }

    /// <inheritdoc/>
    public DateOnly LocalToday =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, _zone).DateTime);

    /// <inheritdoc/>
    public (Result Result, DateTimeOffset? UtcInstant) Resolve(string localIso)
    {
        if (!DateTime.TryParseExact(
                localIso?.Trim(), AcceptedFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var naive))
        {
            return (Result.Failure(
                ErrorCode.TimeUnparseable,
                "I could not read that as a date and time. Could you say it another way?"), null);
        }

        var local = DateTime.SpecifyKind(naive, DateTimeKind.Unspecified);

        // A local time inside a spring-forward gap does not exist. Walk forward to the first
        // instant that does, rather than throwing or silently picking something arbitrary.
        while (_zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        var offset = _zone.IsAmbiguousTime(local)
            ? _zone.GetAmbiguousTimeOffsets(local).Max()   // larger offset == earlier instant
            : _zone.GetUtcOffset(local);

        var utc = new DateTimeOffset(local, offset).ToUniversalTime();
        var now = _clock.UtcNow;

        if (utc < now - PastTolerance)
        {
            return (Result.Failure(
                ErrorCode.TimeInPast,
                "That time has already passed. When would you like to be reminded?"), null);
        }

        if (utc > now + FutureLimit)
        {
            return (Result.Failure(
                ErrorCode.TimeTooFarAhead,
                "That is more than two years away — did you mean a different year?"), null);
        }

        return (Result.Success(), utc);
    }

    /// <inheritdoc/>
    public DateTimeOffset ToLocal(DateTimeOffset utcInstant)
        => TimeZoneInfo.ConvertTime(utcInstant, _zone);

    /// <inheritdoc/>
    public string DescribeNowForPrompt()
    {
        var local = ToLocal(_clock.UtcNow);
        var hours = local.Offset.Hours;
        return $"Current time: {local:dddd d MMMM yyyy, HH:mm}, {TimeZoneId} (UTC{hours:+#;-#;+0}). "
             + "All times the user gives are local to that zone. "
             + "Return absolute local ISO-8601 datetimes with no offset.";
    }
}
```

- [ ] **Step 5: Run to verify the tests pass**

Run: `dotnet test tests/Assistant.UnitTests --filter LocalTimeResolverTests`
Expected: PASS, 12 tests.

If `Shifts_a_local_time_that_does_not_exist_past_the_spring_forward_gap` fails, confirm the container or host has `tzdata`. On `alpine` images this is a separate package; the Dockerfile in Task 14 installs it.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: local time resolution with daylight-saving and plausibility guards"
```

---

## Task 5: Mapping extension methods and the callback codec

Spec §4.4, §6.4, §12.2. Pure functions; unit tested because there is nothing to integrate.

**Files:**
- Create: `src/Assistant.Impl/Mapping/ReminderTaskMappingExtensions.cs`
- Create: `src/Assistant.Impl/Mapping/CallbackDataExtensions.cs`
- Create: `tests/Assistant.UnitTests/Mapping/ReminderTaskMappingTests.cs`
- Create: `tests/Assistant.UnitTests/Mapping/CallbackDataTests.cs`

**Interfaces:**
- Consumes: `ReminderTask`, `TaskResponse`, `ReminderNotification`, `ILocalTimeResolver`.
- Produces:
  - `ReminderTask.ToResponse(ILocalTimeResolver, DateTimeOffset nowUtc)` → `TaskResponse`
  - `ReminderTask.ToNotification(ILocalTimeResolver, DateTimeOffset nowUtc)` → `ReminderNotification`
  - `CreateTaskRequest.ToModel(Guid id, DateTimeOffset? dueAtUtc, DateTimeOffset nowUtc)` → `ReminderTask`
  - `CallbackDataExtensions.ToCallbackData(Guid taskId, string actionKey, string? argument)` → `string`
  - `CallbackDataExtensions.TryParseCallbackData(string, out CallbackPayload)` → `bool`
  - `CallbackPayload` record with `Guid TaskId`, `string Action`, `string? Argument`

- [ ] **Step 1: Write the failing mapping tests**

`tests/Assistant.UnitTests/Mapping/ReminderTaskMappingTests.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Impl.Mapping;
using Assistant.Impl.Services;
using Assistant.Interfaces;
using Assistant.Models;
using Shouldly;
using Xunit;

namespace Assistant.UnitTests.Mapping;

public class ReminderTaskMappingTests
{
    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private static DateTimeOffset Utc(string iso) => DateTimeOffset.Parse(
        iso, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

    private static ILocalTimeResolver Resolver(string nowUtcIso)
        => new LocalTimeResolver(new StubClock { UtcNow = Utc(nowUtcIso) });

    private static ReminderTask Sample() => new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Title = "Call the bank",
        Notes = "Ask about the transfer fee",
        Status = ReminderStatus.Pending,
        Priority = Priority.High,
        DueAt = Utc("2026-08-17T07:00:00Z"),
        ReminderSentAt = null,
        DeliveryAttempts = 0,
        CreatedAt = Utc("2026-08-16T20:00:00Z"),
        UpdatedAt = Utc("2026-08-16T20:00:00Z"),
        CompletedAt = null,
    };

    [Fact]
    public void ToResponse_renders_the_due_time_in_local_time()
    {
        var response = Sample().ToResponse(Resolver("2026-08-16T20:00:00Z"), Utc("2026-08-16T20:00:00Z"));

        response.Id.ShouldBe(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        response.Title.ShouldBe("Call the bank");
        response.Notes.ShouldBe("Ask about the transfer fee");
        response.DueAtLocal!.Value.Hour.ShouldBe(10, "07:00Z is 10:00 in Israel in August");
        response.DueAtLocal!.Value.Offset.ShouldBe(TimeSpan.FromHours(3));
        response.IsHighPriority.ShouldBeTrue();
        response.IsCompleted.ShouldBeFalse();
        response.IsOverdue.ShouldBeFalse();
    }

    [Fact]
    public void ToResponse_marks_a_pending_task_whose_time_has_passed_as_overdue()
    {
        var response = Sample().ToResponse(Resolver("2026-08-17T09:00:00Z"), Utc("2026-08-17T09:00:00Z"));

        response.IsOverdue.ShouldBeTrue();
    }

    [Fact]
    public void ToResponse_never_marks_a_completed_task_overdue()
    {
        var task = Sample();
        task.Status = ReminderStatus.Completed;
        task.CompletedAt = Utc("2026-08-17T06:00:00Z");

        var response = task.ToResponse(Resolver("2026-08-17T09:00:00Z"), Utc("2026-08-17T09:00:00Z"));

        response.IsCompleted.ShouldBeTrue();
        response.IsOverdue.ShouldBeFalse();
    }

    [Fact]
    public void ToResponse_leaves_the_due_time_null_for_an_undated_task()
    {
        var task = Sample();
        task.DueAt = null;

        var response = task.ToResponse(Resolver("2026-08-16T20:00:00Z"), Utc("2026-08-16T20:00:00Z"));

        response.DueAtLocal.ShouldBeNull();
        response.IsOverdue.ShouldBeFalse();
    }

    [Fact]
    public void ToNotification_reports_how_overdue_a_task_is()
    {
        var notification = Sample()
            .ToNotification(Resolver("2026-08-17T10:00:00Z"), Utc("2026-08-17T10:00:00Z"));

        notification.TaskId.ShouldBe(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        notification.Title.ShouldBe("Call the bank");
        notification.OverdueBy.ShouldBe(TimeSpan.FromHours(3));
    }

    [Fact]
    public void ToNotification_reports_no_overdue_span_for_a_task_due_now()
    {
        var notification = Sample()
            .ToNotification(Resolver("2026-08-17T07:00:00Z"), Utc("2026-08-17T07:00:00Z"));

        notification.OverdueBy.ShouldBeNull();
    }

    [Fact]
    public void Every_response_property_is_populated_from_the_model()
    {
        // Guards the predictable defect: a property added to the model or the response and then
        // forgotten in the mapper. If this fails, the mapper is missing an assignment.
        var response = Sample().ToResponse(Resolver("2026-08-16T20:00:00Z"), Utc("2026-08-16T20:00:00Z"));

        foreach (var property in typeof(TaskResponse).GetProperties())
        {
            var value = property.GetValue(response);
            if (property.PropertyType == typeof(string))
            {
                value.ShouldNotBeNull($"{property.Name} was not mapped");
            }
        }

        response.DueAtLocal.ShouldNotBeNull("DueAtLocal was not mapped");
    }

    [Fact]
    public void ToModel_builds_a_pending_task_with_matching_timestamps()
    {
        var now = Utc("2026-08-16T20:00:00Z");
        var due = Utc("2026-08-17T07:00:00Z");
        var id = Guid.NewGuid();

        var task = new CreateTaskRequest("Call the bank", "2026-08-17T10:00:00", "Fee query", true)
            .ToModel(id, due, now);

        task.Id.ShouldBe(id);
        task.Title.ShouldBe("Call the bank");
        task.Notes.ShouldBe("Fee query");
        task.Priority.ShouldBe(Priority.High);
        task.Status.ShouldBe(ReminderStatus.Pending);
        task.DueAt.ShouldBe(due);
        task.ReminderSentAt.ShouldBeNull();
        task.DeliveryAttempts.ShouldBe(0);
        task.CreatedAt.ShouldBe(now);
        task.UpdatedAt.ShouldBe(now);
        task.CompletedAt.ShouldBeNull();
    }
}
```

`tests/Assistant.UnitTests/Mapping/CallbackDataTests.cs`:

```csharp
using System.Text;
using Assistant.Impl.Mapping;
using Shouldly;
using Xunit;

namespace Assistant.UnitTests.Mapping;

public class CallbackDataTests
{
    private static readonly Guid TaskId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Round_trips_an_action_with_no_argument()
    {
        var data = CallbackDataExtensions.ToCallbackData(TaskId, "done", null);

        CallbackDataExtensions.TryParseCallbackData(data, out var payload).ShouldBeTrue();
        payload.TaskId.ShouldBe(TaskId);
        payload.Action.ShouldBe("done");
        payload.Argument.ShouldBeNull();
    }

    [Fact]
    public void Round_trips_an_action_with_an_argument()
    {
        var data = CallbackDataExtensions.ToCallbackData(TaskId, "snooze", "1h");

        CallbackDataExtensions.TryParseCallbackData(data, out var payload).ShouldBeTrue();
        payload.Action.ShouldBe("snooze");
        payload.Argument.ShouldBe("1h");
    }

    [Fact]
    public void Stays_within_the_sixty_four_byte_platform_budget()
    {
        // Telegram rejects callback data over 64 bytes, and it does so at send time on a live
        // reminder, so this is asserted rather than assumed.
        var longest = CallbackDataExtensions.ToCallbackData(TaskId, "reschedule", "tomorrow");

        Encoding.UTF8.GetByteCount(longest).ShouldBeLessThanOrEqualTo(64);
    }

    [Fact]
    public void Rejects_a_payload_from_an_unknown_future_version()
    {
        // A button left in chat history from a later format must degrade politely, not throw.
        CallbackDataExtensions.TryParseCallbackData("v9:done:abc", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("v1:done")]
    [InlineData("v1:done:not-base64!!")]
    public void Rejects_malformed_payloads(string data)
        => CallbackDataExtensions.TryParseCallbackData(data, out _).ShouldBeFalse();
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Assistant.UnitTests --filter "ReminderTaskMappingTests|CallbackDataTests"`
Expected: FAIL to compile — neither extension class exists.

- [ ] **Step 3: Implement the task mappers**

`src/Assistant.Impl/Mapping/ReminderTaskMappingExtensions.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Interfaces;
using Assistant.Models;

namespace Assistant.Impl.Mapping;

/// <summary>Projections between task models and the types callers exchange.</summary>
/// <remarks>
/// Mapping is deliberately hand-written: a forgotten assignment is then a visible omission in a
/// short method rather than a silent gap in a convention-based mapper.
/// </remarks>
public static class ReminderTaskMappingExtensions
{
    /// <summary>Projects a task onto the shape returned to callers.</summary>
    /// <param name="task">The task to project.</param>
    /// <param name="time">Resolver used to render the due time in local time.</param>
    /// <param name="nowUtc">The instant to treat as "now" when deciding whether it is overdue.</param>
    /// <returns>A response carrying the caller-visible fields of <paramref name="task"/>.</returns>
    public static TaskResponse ToResponse(
        this ReminderTask task, ILocalTimeResolver time, DateTimeOffset nowUtc)
    {
        var isCompleted = task.Status == ReminderStatus.Completed;

        return new TaskResponse(
            Id: task.Id,
            Title: task.Title,
            Notes: task.Notes,
            DueAtLocal: task.DueAt is { } due ? time.ToLocal(due) : null,
            IsOverdue: !isCompleted
                       && task.Status == ReminderStatus.Pending
                       && task.DueAt is { } d
                       && d <= nowUtc,
            IsHighPriority: task.Priority == Priority.High,
            IsCompleted: isCompleted);
    }

    /// <summary>Projects a task onto the shape the messaging layer renders.</summary>
    /// <param name="task">The task to project. Must have a due time.</param>
    /// <param name="time">Resolver used to render the due time in local time.</param>
    /// <param name="nowUtc">The instant to treat as "now" when measuring lateness.</param>
    /// <returns>A notification ready to be delivered.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="task"/> has no due time, which means it should never have been
    /// selected for delivery.
    /// </exception>
    public static ReminderNotification ToNotification(
        this ReminderTask task, ILocalTimeResolver time, DateTimeOffset nowUtc)
    {
        if (task.DueAt is not { } due)
        {
            throw new InvalidOperationException(
                $"Task {task.Id} has no due time and cannot be delivered as a reminder.");
        }

        var lateBy = nowUtc - due;

        return new ReminderNotification(
            TaskId: task.Id,
            Title: task.Title,
            DueAtLocal: time.ToLocal(due),
            OverdueBy: lateBy > TimeSpan.Zero ? lateBy : null);
    }

    /// <summary>Builds a new task from a creation request.</summary>
    /// <param name="request">What to create.</param>
    /// <param name="id">Identifier to assign.</param>
    /// <param name="dueAtUtc">The already-validated due instant, or <see langword="null"/>.</param>
    /// <param name="nowUtc">The instant to stamp as created and updated.</param>
    /// <returns>A pending task with no reminder yet delivered.</returns>
    public static ReminderTask ToModel(
        this CreateTaskRequest request, Guid id, DateTimeOffset? dueAtUtc, DateTimeOffset nowUtc)
        => new()
        {
            Id = id,
            Title = request.Title,
            Notes = request.Notes,
            Status = ReminderStatus.Pending,
            Priority = request.IsHighPriority ? Priority.High : Priority.Normal,
            DueAt = dueAtUtc,
            ReminderSentAt = null,
            DeliveryAttempts = 0,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
            CompletedAt = null,
        };
}
```

- [ ] **Step 4: Implement the callback codec**

`src/Assistant.Impl/Mapping/CallbackDataExtensions.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Assistant.Impl.Mapping;

/// <summary>A decoded button payload.</summary>
/// <param name="TaskId">The task the button belongs to.</param>
/// <param name="Action">The action token, matching a task action's key.</param>
/// <param name="Argument">The action's optional argument, or <see langword="null"/>.</param>
public sealed record CallbackPayload(Guid TaskId, string Action, string? Argument);

/// <summary>Encodes and decodes message button payloads.</summary>
/// <remarks>
/// <para>
/// The wire form is <c>v1:action:id[:argument]</c>, where the identifier is a 22-character
/// URL-safe Base64 rendering of the task's raw bytes rather than its 36-character text form.
/// That keeps the whole payload inside the platform's 64-byte limit with room to spare.
/// </para>
/// <para>
/// The version prefix exists so buttons already sitting in the user's chat history fail politely
/// after a format change instead of throwing.
/// </para>
/// </remarks>
public static class CallbackDataExtensions
{
    private const string CurrentVersion = "v1";

    /// <summary>Builds the payload for a button.</summary>
    /// <param name="taskId">The task the button belongs to.</param>
    /// <param name="actionKey">The action's key.</param>
    /// <param name="argument">The action's optional argument.</param>
    /// <returns>A payload string within the platform's size limit.</returns>
    public static string ToCallbackData(Guid taskId, string actionKey, string? argument)
    {
        var id = Convert.ToBase64String(taskId.ToByteArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return argument is null
            ? $"{CurrentVersion}:{actionKey}:{id}"
            : $"{CurrentVersion}:{actionKey}:{id}:{argument}";
    }

    /// <summary>Attempts to decode a button payload.</summary>
    /// <param name="data">The payload as received from the platform.</param>
    /// <param name="payload">The decoded payload when this method returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the payload is well formed and carries the current version;
    /// <see langword="false"/> for anything else, including payloads from other versions.
    /// </returns>
    public static bool TryParseCallbackData(
        string? data, [NotNullWhen(true)] out CallbackPayload? payload)
    {
        payload = null;

        if (string.IsNullOrWhiteSpace(data))
        {
            return false;
        }

        var parts = data.Split(':', 4);
        if (parts.Length < 3 || parts[0] != CurrentVersion)
        {
            return false;
        }

        var encoded = parts[2].Replace('-', '+').Replace('_', '/');
        encoded = encoded.PadRight(encoded.Length + ((4 - (encoded.Length % 4)) % 4), '=');

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length != 16)
        {
            return false;
        }

        payload = new CallbackPayload(
            new Guid(bytes),
            parts[1],
            parts.Length == 4 ? parts[3] : null);

        return true;
    }
}
```

- [ ] **Step 5: Run to verify the tests pass**

Run: `dotnet test tests/Assistant.UnitTests --filter "ReminderTaskMappingTests|CallbackDataTests"`
Expected: PASS, 13 tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: task mapping extensions and compact versioned callback codec"
```

---

## Task 6: `TaskService` — the single writer

Spec §4.2. Every task mutation in the system passes through here, and this is the only place the invariants live. Tested at integration level against real Postgres, because the invariants and the check constraints have to agree.

**Files:**
- Create: `src/Assistant.Impl/Services/TaskService.cs`
- Create: `tests/Assistant.IntegrationTests/Infrastructure/FakeClock.cs`
- Create: `tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs`

**Interfaces:**
- Consumes: `ITaskRepository`, `ILocalTimeResolver`, `IClock`, `Result`, mapping extensions.
- Produces: `TaskService : ITaskService`, and `FakeClock` with `void Set(string utcIso)` and `DateTimeOffset UtcNow { get; set; }`.

- [ ] **Step 1: Write the fake clock**

`tests/Assistant.IntegrationTests/Infrastructure/FakeClock.cs`:

```csharp
using System.Globalization;
using Assistant.Interfaces;

namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>A clock the test drives directly.</summary>
public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = Parse("2026-08-17T07:00:00Z");

    /// <summary>Sets the current instant from an ISO-8601 UTC string.</summary>
    public void Set(string utcIso) => UtcNow = Parse(utcIso);

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by) => UtcNow += by;

    private static DateTimeOffset Parse(string iso)
        => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
}
```

- [ ] **Step 2: Write the failing service tests**

`tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Impl.Services;
using Assistant.Interfaces;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Models;
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Assistant.IntegrationTests.Services;

[Collection(PostgresCollection.Name)]
public sealed class TaskServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private readonly FakeClock _clock = new();
    private ServiceProvider _provider = null!;

    public TaskServiceTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        await _postgres.ResetAsync();
        _clock.Set("2026-08-16T20:00:00Z");   // Sunday 23:00 local

        var services = new ServiceCollection();
        services.AddAssistantRepository(_postgres.ConnectionString);
        services.AddSingleton<IClock>(_clock);
        services.AddSingleton<ILocalTimeResolver, LocalTimeResolver>();
        services.AddScoped<ITaskService, TaskService>();
        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private async Task<T> WithServiceAsync<T>(Func<ITaskService, Task<T>> work)
    {
        using var scope = _provider.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ITaskService>());
    }

    private async Task<ReminderTask?> LoadAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITaskRepository>().FindAsync(id, default);
    }

    private async Task<Guid> GivenTaskDueAsync(string localIso, string title = "Call the bank")
    {
        var (result, task) = await WithServiceAsync(
            s => s.CreateAsync(new CreateTaskRequest(title, localIso), default));
        result.Succeeded.ShouldBeTrue(result.Message);
        return task!.Id;
    }

    [Fact]
    public async Task Creates_a_task_with_the_due_time_converted_to_utc()
    {
        var id = await GivenTaskDueAsync("2026-08-17T10:00:00");

        var stored = await LoadAsync(id);
        stored!.DueAt.ShouldBe(DateTimeOffset.Parse("2026-08-17T07:00:00Z", null,
            System.Globalization.DateTimeStyles.AdjustToUniversal));
        stored.Status.ShouldBe(ReminderStatus.Pending);
        stored.CreatedAt.ShouldBe(_clock.UtcNow);
        stored.UpdatedAt.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task Creates_an_undated_task_when_no_time_is_supplied()
    {
        var (result, task) = await WithServiceAsync(
            s => s.CreateAsync(new CreateTaskRequest("Think about it"), default));

        result.Succeeded.ShouldBeTrue();
        (await LoadAsync(task!.Id))!.DueAt.ShouldBeNull();
    }

    [Fact]
    public async Task Rejects_creation_with_a_time_in_the_past_and_writes_nothing()
    {
        var (result, task) = await WithServiceAsync(
            s => s.CreateAsync(new CreateTaskRequest("Too late", "2026-08-16T09:00:00"), default));

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe(ErrorCode.TimeInPast);
        task.ShouldBeNull();
    }

    [Fact]
    public async Task Completing_sets_the_status_and_the_completion_time_together()
    {
        var id = await GivenTaskDueAsync("2026-08-17T10:00:00");
        _clock.Set("2026-08-17T07:05:00Z");

        var result = await WithServiceAsync(s => s.CompleteAsync(id, default));

        result.Succeeded.ShouldBeTrue();
        var stored = await LoadAsync(id);
        stored!.Status.ShouldBe(ReminderStatus.Completed);
        stored.CompletedAt.ShouldBe(_clock.UtcNow, "the check constraint requires these to agree");
    }

    [Fact]
    public async Task Completing_twice_succeeds_so_a_button_can_be_pressed_again()
    {
        var id = await GivenTaskDueAsync("2026-08-17T10:00:00");
        await WithServiceAsync(s => s.CompleteAsync(id, default));

        var second = await WithServiceAsync(s => s.CompleteAsync(id, default));

        second.Succeeded.ShouldBeTrue("a second tap must be a no-op, not an error");
    }

    [Fact]
    public async Task Completing_a_cancelled_task_is_rejected()
    {
        var id = await GivenTaskDueAsync("2026-08-17T10:00:00");
        await WithServiceAsync(s => s.CancelAsync(id, default));

        var result = await WithServiceAsync(s => s.CompleteAsync(id, default));

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe(ErrorCode.TaskCancelled);
    }

    [Fact]
    public async Task Completing_a_missing_task_is_rejected()
    {
        var result = await WithServiceAsync(s => s.CompleteAsync(Guid.NewGuid(), default));

        result.Error.ShouldBe(ErrorCode.TaskNotFound);
    }

    [Fact]
    public async Task Snoozing_moves_the_due_time_forward_from_now_and_re_arms_the_reminder()
    {
        var id = await GivenTaskDueAsync("2026-08-17T10:00:00");
        _clock.Set("2026-08-17T07:00:00Z");
        await WithServiceAsync(s => s.MarkReminderSentAsync(id, default));
        await WithServiceAsync(s => s.RecordDeliveryFailureAsync(id, default));

        _clock.Set("2026-08-17T07:30:00Z");
        var result = await WithServiceAsync(s => s.SnoozeAsync(id, TimeSpan.FromHours(1), default));

        result.Succeeded.ShouldBeTrue();
        var stored = await LoadAsync(id);
        stored!.DueAt.ShouldBe(DateTimeOffset.Parse("2026-08-17T08:30:00Z", null,
            System.Globalization.DateTimeStyles.AdjustToUniversal));
        stored.ReminderSentAt.ShouldBeNull("otherwise the task would never fire again");
        stored.DeliveryAttempts.ShouldBe(0);
    }

    [Fact]
    public async Task Snoozing_a_completed_task_is_rejected()
    {
        var id = await GivenTaskDueAsync("2026-08-17T10:00:00");
        await WithServiceAsync(s => s.CompleteAsync(id, default));

        var result = await WithServiceAsync(s => s.SnoozeAsync(id, TimeSpan.FromHours(1), default));

        result.Error.ShouldBe(ErrorCode.TaskAlreadyCompleted);
    }

    [Fact]
    public async Task Rescheduling_sets_an_absolute_time_and_re_arms_the_reminder()
    {
        var id = await GivenTaskDueAsync("2026-08-17T10:00:00");
        _clock.Set("2026-08-17T07:00:00Z");
        await WithServiceAsync(s => s.MarkReminderSentAsync(id, default));

        var target = DateTimeOffset.Parse("2026-08-18T06:00:00Z", null,
            System.Globalization.DateTimeStyles.AdjustToUniversal);
        var result = await WithServiceAsync(s => s.RescheduleAsync(id, target, default));

        result.Succeeded.ShouldBeTrue();
        var stored = await LoadAsync(id);
        stored!.DueAt.ShouldBe(target);
        stored.ReminderSentAt.ShouldBeNull();
    }

    [Fact]
    public async Task Marking_a_reminder_sent_records_the_current_instant()
    {
        var id = await GivenTaskDueAsync("2026-08-17T10:00:00");
        _clock.Set("2026-08-17T07:00:03Z");

        await WithServiceAsync(s => s.MarkReminderSentAsync(id, default));

        (await LoadAsync(id))!.ReminderSentAt.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task Marking_an_undated_task_sent_is_rejected()
    {
        var (_, task) = await WithServiceAsync(
            s => s.CreateAsync(new CreateTaskRequest("No deadline"), default));

        var result = await WithServiceAsync(s => s.MarkReminderSentAsync(task!.Id, default));

        result.Error.ShouldBe(ErrorCode.TaskHasNoDueTime);
    }

    [Fact]
    public async Task Recording_delivery_failures_increments_the_attempt_count()
    {
        var id = await GivenTaskDueAsync("2026-08-17T10:00:00");

        await WithServiceAsync(s => s.RecordDeliveryFailureAsync(id, default));
        await WithServiceAsync(s => s.RecordDeliveryFailureAsync(id, default));

        (await LoadAsync(id))!.DeliveryAttempts.ShouldBe(2);
    }

    [Fact]
    public async Task Updating_the_due_time_re_arms_the_reminder()
    {
        var id = await GivenTaskDueAsync("2026-08-17T10:00:00");
        _clock.Set("2026-08-17T07:00:00Z");
        await WithServiceAsync(s => s.MarkReminderSentAsync(id, default));

        var result = await WithServiceAsync(
            s => s.UpdateAsync(new UpdateTaskRequest(id, DueAtLocal: "2026-08-17T15:00:00"), default));

        result.Succeeded.ShouldBeTrue();
        var stored = await LoadAsync(id);
        stored!.ReminderSentAt.ShouldBeNull();
        stored.DueAt.ShouldBe(DateTimeOffset.Parse("2026-08-17T12:00:00Z", null,
            System.Globalization.DateTimeStyles.AdjustToUniversal));
    }

    [Fact]
    public async Task Updating_only_the_title_leaves_the_reminder_state_untouched()
    {
        var id = await GivenTaskDueAsync("2026-08-17T10:00:00");
        _clock.Set("2026-08-17T07:00:00Z");
        await WithServiceAsync(s => s.MarkReminderSentAsync(id, default));
        var sentAt = (await LoadAsync(id))!.ReminderSentAt;

        await WithServiceAsync(s => s.UpdateAsync(new UpdateTaskRequest(id, Title: "Call the bank back"), default));

        var stored = await LoadAsync(id);
        stored!.Title.ShouldBe("Call the bank back");
        stored.ReminderSentAt.ShouldBe(sentAt);
    }

    [Fact]
    public async Task Query_clamps_an_oversized_limit()
    {
        for (var i = 0; i < 5; i++)
        {
            await GivenTaskDueAsync("2026-08-17T10:00:00", $"Task {i}");
        }

        var results = await WithServiceAsync(
            s => s.QueryAsync(new ListTasksRequest(TaskFilter.All, 100_000), default));

        results.Count.ShouldBe(5);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Assistant.IntegrationTests --filter TaskServiceTests`
Expected: FAIL to compile — `Assistant.Impl.Services.TaskService` does not exist.

- [ ] **Step 4: Implement `TaskService`**

`src/Assistant.Impl/Services/TaskService.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Impl.Mapping;
using Assistant.Interfaces;
using Assistant.Models;

namespace Assistant.Impl.Services;

/// <inheritdoc cref="ITaskService"/>
public sealed class TaskService : ITaskService
{
    /// <summary>Upper bound on how many tasks any single listing returns.</summary>
    private const int MaxLimit = 100;

    private readonly ITaskRepository _tasks;
    private readonly ILocalTimeResolver _time;
    private readonly IClock _clock;

    /// <summary>Initialises the service.</summary>
    /// <param name="tasks">Persistence for tasks.</param>
    /// <param name="time">Converts local text to UTC instants and applies the plausibility guards.</param>
    /// <param name="clock">Source of the current time.</param>
    public TaskService(ITaskRepository tasks, ILocalTimeResolver time, IClock clock)
    {
        _tasks = tasks;
        _time = time;
        _clock = clock;
    }

    /// <inheritdoc/>
    public async Task<(Result Result, ReminderTask? Task)> CreateAsync(
        CreateTaskRequest request, CancellationToken ct)
    {
        DateTimeOffset? dueAtUtc = null;

        if (!string.IsNullOrWhiteSpace(request.DueAtLocal))
        {
            var (timeResult, resolved) = _time.Resolve(request.DueAtLocal);
            if (!timeResult.Succeeded)
            {
                return (timeResult, null);
            }
            dueAtUtc = resolved;
        }

        var task = request.ToModel(Guid.NewGuid(), dueAtUtc, _clock.UtcNow);
        await _tasks.AddAsync(task, ct);
        return (Result.Success(), task);
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateAsync(UpdateTaskRequest request, CancellationToken ct)
    {
        var task = await _tasks.FindAsync(request.TaskId, ct);
        if (task is null)
        {
            return NotFound();
        }

        if (task.Status == ReminderStatus.Completed)
        {
            return Result.Failure(ErrorCode.TaskAlreadyCompleted, "That one is already done.");
        }

        if (request.DueAtLocal is not null)
        {
            var (timeResult, resolved) = _time.Resolve(request.DueAtLocal);
            if (!timeResult.Succeeded)
            {
                return timeResult;
            }

            task.DueAt = resolved;
            ReArm(task);
        }

        if (request.Title is not null)
        {
            task.Title = request.Title;
        }

        if (request.Notes is not null)
        {
            task.Notes = request.Notes;
        }

        if (request.IsHighPriority is { } high)
        {
            task.Priority = high ? Priority.High : Priority.Normal;
        }

        return await SaveAsync(task, ct);
    }

    /// <inheritdoc/>
    public async Task<Result> CompleteAsync(Guid id, CancellationToken ct)
    {
        var task = await _tasks.FindAsync(id, ct);
        if (task is null)
        {
            return NotFound();
        }

        if (task.Status == ReminderStatus.Cancelled)
        {
            return Result.Failure(ErrorCode.TaskCancelled, "That one was cancelled.");
        }

        // Idempotent on purpose: a button can be pressed twice, and the second press should read
        // as confirmation rather than as an error.
        if (task.Status == ReminderStatus.Completed)
        {
            return Result.Success();
        }

        task.Status = ReminderStatus.Completed;
        task.CompletedAt = _clock.UtcNow;
        return await SaveAsync(task, ct);
    }

    /// <inheritdoc/>
    public async Task<Result> CancelAsync(Guid id, CancellationToken ct)
    {
        var task = await _tasks.FindAsync(id, ct);
        if (task is null)
        {
            return NotFound();
        }

        if (task.Status == ReminderStatus.Completed)
        {
            return Result.Failure(ErrorCode.TaskAlreadyCompleted, "That one is already done.");
        }

        task.Status = ReminderStatus.Cancelled;
        task.CompletedAt = null;
        return await SaveAsync(task, ct);
    }

    /// <inheritdoc/>
    public Task<Result> SnoozeAsync(Guid id, TimeSpan duration, CancellationToken ct)
        => duration <= TimeSpan.Zero
            ? Task.FromResult(Result.Failure(ErrorCode.TimeInPast, "A snooze has to move it forward."))
            : RescheduleAsync(id, _clock.UtcNow + duration, ct);

    /// <inheritdoc/>
    public async Task<Result> RescheduleAsync(Guid id, DateTimeOffset newDueAtUtc, CancellationToken ct)
    {
        var task = await _tasks.FindAsync(id, ct);
        if (task is null)
        {
            return NotFound();
        }

        if (task.Status == ReminderStatus.Completed)
        {
            return Result.Failure(ErrorCode.TaskAlreadyCompleted, "That one is already done.");
        }

        if (task.Status == ReminderStatus.Cancelled)
        {
            return Result.Failure(ErrorCode.TaskCancelled, "That one was cancelled.");
        }

        task.DueAt = newDueAtUtc.ToUniversalTime();
        ReArm(task);
        return await SaveAsync(task, ct);
    }

    /// <inheritdoc/>
    public async Task<Result> MarkReminderSentAsync(Guid id, CancellationToken ct)
    {
        var task = await _tasks.FindAsync(id, ct);
        if (task is null)
        {
            return NotFound();
        }

        if (task.DueAt is null)
        {
            return Result.Failure(ErrorCode.TaskHasNoDueTime, "That task has no reminder to deliver.");
        }

        task.ReminderSentAt = _clock.UtcNow;
        return await SaveAsync(task, ct);
    }

    /// <inheritdoc/>
    public async Task<Result> RecordDeliveryFailureAsync(Guid id, CancellationToken ct)
    {
        var task = await _tasks.FindAsync(id, ct);
        if (task is null)
        {
            return NotFound();
        }

        task.DeliveryAttempts++;
        return await SaveAsync(task, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ReminderTask>> QueryAsync(ListTasksRequest request, CancellationToken ct)
        => _tasks.QueryAsync(request.Filter, _clock.UtcNow, Math.Clamp(request.Limit, 1, MaxLimit), ct);

    /// <summary>
    /// Clears the delivery state so a task with a new due time will remind again.
    /// </summary>
    /// <remarks>
    /// These two fields must always be cleared together. Clearing only the marker would let an
    /// exhausted task stay invisible; clearing only the count would leave a task that never fires.
    /// This is the reason mutation is confined to this class.
    /// </remarks>
    private static void ReArm(ReminderTask task)
    {
        task.ReminderSentAt = null;
        task.DeliveryAttempts = 0;
    }

    private async Task<Result> SaveAsync(ReminderTask task, CancellationToken ct)
    {
        task.UpdatedAt = _clock.UtcNow;
        await _tasks.UpdateAsync(task, ct);
        return Result.Success();
    }

    private static Result NotFound()
        => Result.Failure(ErrorCode.TaskNotFound, "I could not find that task.");
}
```

- [ ] **Step 5: Run to verify the tests pass**

Run: `dotnet test tests/Assistant.IntegrationTests --filter TaskServiceTests`
Expected: PASS, 16 tests.

- [ ] **Step 6: Add the single-writer architecture test**

Append to `tests/Assistant.UnitTests/Architecture/ConventionTests.cs`:

```csharp
    [Fact]
    public void Only_TaskService_depends_on_a_task_repository()
    {
        // Jobs, tools, and button actions must go through ITaskService. Reaching a repository
        // directly bypasses the invariants — most damagingly the pairing of ReminderSentAt and
        // DeliveryAttempts — and produces tasks that silently stop reminding.
        var implAssembly = typeof(Assistant.Impl.Services.TaskService).Assembly;

        var offenders = implAssembly.GetTypes()
            .Where(t => t.IsClass && t != typeof(Assistant.Impl.Services.TaskService))
            .Where(t => t.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType.Name.EndsWith("Repository", StringComparison.Ordinal)))
            .Select(t => t.Name)
            .ToList();

        offenders.ShouldBeEmpty(
            "These types take a repository directly instead of ITaskService: " + string.Join(", ", offenders));
    }
```

Note: `MessageHandler` legitimately takes `IChatMessageRepository`, and `DailyBriefJob` takes `IDailyBriefRepository`. Neither is a *task* repository. Narrow the check to task repositories specifically by replacing the predicate's `EndsWith("Repository")` with `== nameof(Assistant.Interfaces.ITaskRepository)`.

- [ ] **Step 7: Run the full unit suite and commit**

Run: `dotnet test tests/Assistant.UnitTests`
Expected: PASS.

```bash
git add -A
git commit -m "feat: TaskService as the single writer with enforced invariants"
```

---

## Task 7: Telegram delivery and the host harness

Spec §6.4, §6.5, §7.1, §7.3. WireMock stands in for Telegram, so no credentials are needed to test.

**Files:**
- Create: `src/Assistant.Impl/Telegram/{TelegramOptions,TelegramNotifier,TelegramMessageFormatter}.cs`
- Create: `tests/Assistant.IntegrationTests/Infrastructure/{TelegramStub,SendMessagePayload}.cs`
- Create: `tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs`

**Interfaces:**
- Consumes: `INotifier`, `ReminderNotification`, `DailyBriefNotification`, `CallbackDataExtensions`.
- Produces:
  - `TelegramOptions` with `BotToken`, `OwnerUserId`, `BaseUrl`, section name `"Telegram"`
  - `TelegramNotifier : INotifier`
  - `TelegramStub` with `string BaseUrl`, `IReadOnlyList<SendMessagePayload> SentMessages()`, `void Reset()`
  - `SendMessagePayload` with `ChatId`, `Text`, `ParseMode`, `IReadOnlyList<string> Buttons()`, `IReadOnlyList<string> CallbackData()`

- [ ] **Step 1: Add packages**

```bash
dotnet add src/Assistant.Impl package Telegram.Bot
dotnet add src/Assistant.Impl package Microsoft.Extensions.Options
dotnet add src/Assistant.Impl package Microsoft.Extensions.Logging.Abstractions
dotnet add tests/Assistant.IntegrationTests package WireMock.Net
```

- [ ] **Step 2: Write the options type**

`src/Assistant.Impl/Telegram/TelegramOptions.cs`:

```csharp
namespace Assistant.Impl.Telegram;

/// <summary>Configuration for the Telegram connection.</summary>
public sealed class TelegramOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Telegram";

    /// <summary>Bot token issued by BotFather.</summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>Numeric identifier of the only user the bot will talk to.</summary>
    /// <value>
    /// Messages from any other sender are discarded before any language model call is made, so an
    /// unknown sender costs nothing.
    /// </value>
    public long OwnerUserId { get; set; }

    /// <summary>Base address of the Bot API.</summary>
    /// <value>
    /// Empty to use the public API. Tests point this at a local stub, which is what allows the
    /// whole suite to run without a real token.
    /// </value>
    public string BaseUrl { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Write the message formatter**

`src/Assistant.Impl/Telegram/TelegramMessageFormatter.cs`:

```csharp
using System.Net;
using System.Text;
using Assistant.Contracts;

namespace Assistant.Impl.Telegram;

/// <summary>Renders notifications as Telegram message bodies.</summary>
/// <remarks>
/// HTML is used rather than MarkdownV2: HTML needs three characters escaped where MarkdownV2
/// needs eighteen, and an unescaped underscore in a task title is a rejected message on a live
/// reminder.
/// </remarks>
public static class TelegramMessageFormatter
{
    /// <summary>Escapes text for Telegram's HTML parse mode.</summary>
    /// <param name="text">Raw text, typically a user-supplied task title.</param>
    /// <returns>The text with <c>&amp;</c>, <c>&lt;</c>, and <c>&gt;</c> escaped.</returns>
    public static string Escape(string text) => WebUtility.HtmlEncode(text);

    /// <summary>Renders a single reminder.</summary>
    /// <param name="notification">What to remind the user about.</param>
    /// <returns>An HTML message body.</returns>
    public static string Reminder(ReminderNotification notification)
    {
        var body = new StringBuilder("⏰ ").Append(Escape(notification.Title));

        if (notification.OverdueBy is { } late)
        {
            body.Append(" <i>(due ").Append(Describe(late)).Append(" ago)</i>");
        }

        return body.ToString();
    }

    /// <summary>Renders several overdue reminders as one message.</summary>
    /// <param name="notifications">The overdue tasks to summarise.</param>
    /// <returns>An HTML message body.</returns>
    public static string OverdueSummary(IReadOnlyList<ReminderNotification> notifications)
    {
        var body = new StringBuilder("⏰ <b>")
            .Append(notifications.Count)
            .Append(notifications.Count == 1 ? " reminder" : " reminders")
            .AppendLine(" you missed</b>");

        foreach (var n in notifications)
        {
            body.Append("• ").Append(Escape(n.Title))
                .Append(" — ").Append(n.DueAtLocal.ToString("ddd d MMM, HH:mm"))
                .AppendLine();
        }

        return body.ToString().TrimEnd();
    }

    /// <summary>Renders the daily brief.</summary>
    /// <param name="brief">What to include.</param>
    /// <returns>An HTML message body.</returns>
    public static string DailyBrief(DailyBriefNotification brief)
    {
        var body = new StringBuilder("<b>")
            .Append(brief.BriefDate.ToString("dddd d MMMM"))
            .AppendLine("</b>");

        if (brief.Overdue.Count > 0)
        {
            body.AppendLine().AppendLine("<b>Overdue</b>");
            foreach (var t in brief.Overdue)
            {
                body.Append("• ").AppendLine(Escape(t.Title));
            }
        }

        if (brief.DueToday.Count > 0)
        {
            body.AppendLine().AppendLine("<b>Today</b>");
            foreach (var t in brief.DueToday)
            {
                body.Append("• ").Append(Escape(t.Title));
                if (t.DueAtLocal is { } due)
                {
                    body.Append(" — ").Append(due.ToString("HH:mm"));
                }
                body.AppendLine();
            }
        }

        if (brief.Overdue.Count == 0 && brief.DueToday.Count == 0)
        {
            body.AppendLine().AppendLine("Nothing due today.");
        }

        if (brief.OpenWithoutDueDate > 0)
        {
            body.AppendLine().Append("<i>").Append(brief.OpenWithoutDueDate)
                .Append(" open with no date</i>");
        }

        return body.ToString().TrimEnd();
    }

    private static string Describe(TimeSpan span) => span.TotalDays >= 1
        ? $"{(int)span.TotalDays}d"
        : span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h"
            : $"{Math.Max(1, (int)span.TotalMinutes)}m";
}
```

- [ ] **Step 4: Write the failing notifier tests**

`tests/Assistant.IntegrationTests/Infrastructure/SendMessagePayload.cs`:

```csharp
using System.Text.Json;

namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>A captured outbound Telegram message, in assertable form.</summary>
public sealed class SendMessagePayload
{
    private readonly JsonElement _root;

    private SendMessagePayload(JsonElement root) => _root = root;

    /// <summary>Parses a captured request body.</summary>
    public static SendMessagePayload Parse(string body)
        => new(JsonDocument.Parse(body).RootElement.Clone());

    public long ChatId => _root.GetProperty("chat_id").GetInt64();

    public string Text => _root.GetProperty("text").GetString()!;

    public string ParseMode => _root.TryGetProperty("parse_mode", out var m) ? m.GetString()! : "";

    /// <summary>The visible label of every inline button, in order.</summary>
    public IReadOnlyList<string> Buttons() => Keyboard("text");

    /// <summary>The payload of every inline button, in order.</summary>
    public IReadOnlyList<string> CallbackData() => Keyboard("callback_data");

    private IReadOnlyList<string> Keyboard(string field)
    {
        if (!_root.TryGetProperty("reply_markup", out var markup)
            || !markup.TryGetProperty("inline_keyboard", out var rows))
        {
            return [];
        }

        return rows.EnumerateArray()
            .SelectMany(row => row.EnumerateArray())
            .Select(button => button.GetProperty(field).GetString()!)
            .ToList();
    }
}
```

`tests/Assistant.IntegrationTests/Infrastructure/TelegramStub.cs`:

```csharp
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>A local stand-in for the Telegram Bot API.</summary>
/// <remarks>
/// Exists so the suite runs with no credentials at all, and so assertions can be made against the
/// exact bytes that would have gone to Telegram — including the inline keyboard, which a fake
/// notifier could never verify.
/// </remarks>
public sealed class TelegramStub : IDisposable
{
    private readonly WireMockServer _server;

    public TelegramStub()
    {
        _server = WireMockServer.Start();

        _server
            .Given(Request.Create().WithPath("/bot*/sendMessage").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"ok":true,"result":{"message_id":1,"date":0,"chat":{"id":1,"type":"private"}}}"""));

        _server
            .Given(Request.Create().WithPath("/bot*/editMessageText").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"ok":true,"result":{"message_id":1,"date":0,"chat":{"id":1,"type":"private"}}}"""));

        _server
            .Given(Request.Create().WithPath("/bot*/answerCallbackQuery").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"ok":true,"result":true}"""));

        _server
            .Given(Request.Create().WithPath("/bot*/getUpdates").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"ok":true,"result":[]}"""));
    }

    /// <summary>Base address to configure the bot client with.</summary>
    public string BaseUrl => _server.Url!;

    /// <summary>Every message the bot has sent, oldest first.</summary>
    public IReadOnlyList<SendMessagePayload> SentMessages() => Captured("sendMessage");

    /// <summary>Every in-place message edit the bot has performed, oldest first.</summary>
    public IReadOnlyList<SendMessagePayload> EditedMessages() => Captured("editMessageText");

    /// <summary>How many button presses the bot has acknowledged.</summary>
    public int AcknowledgedCallbacks() =>
        _server.LogEntries.Count(e => e.RequestMessage.Path.EndsWith("/answerCallbackQuery", StringComparison.Ordinal));

    /// <summary>Forgets every recorded request.</summary>
    public void Reset() => _server.ResetLogEntries();

    public void Dispose() => _server.Dispose();

    private IReadOnlyList<SendMessagePayload> Captured(string method) => _server.LogEntries
        .Where(e => e.RequestMessage.Path.EndsWith('/' + method, StringComparison.Ordinal))
        .OrderBy(e => e.RequestMessage.DateTime)
        .Select(e => SendMessagePayload.Parse(e.RequestMessage.Body ?? "{}"))
        .ToList();
}
```

`tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Impl.Telegram;
using Assistant.Interfaces;
using Assistant.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Options;
using Shouldly;
using Telegram.Bot;
using Xunit;

namespace Assistant.IntegrationTests.Telegram;

public sealed class TelegramNotifierTests : IDisposable
{
    private const long OwnerId = 4242;

    private readonly TelegramStub _telegram = new();
    private readonly INotifier _notifier;

    public TelegramNotifierTests()
    {
        var options = Options.Create(new TelegramOptions
        {
            BotToken = "test-token",
            OwnerUserId = OwnerId,
            BaseUrl = _telegram.BaseUrl,
        });

        var client = new TelegramBotClient(
            new TelegramBotClientOptions(options.Value.BotToken, options.Value.BaseUrl));

        _notifier = new TelegramNotifier(client, options);
    }

    public void Dispose() => _telegram.Dispose();

    private static ReminderNotification Reminder(string title = "Call the bank", TimeSpan? overdue = null)
        => new(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            title,
            DateTimeOffset.Parse("2026-08-17T10:00:00+03:00"),
            overdue);

    [Fact]
    public async Task Sends_exactly_one_message_to_the_owner_with_the_expected_text_and_buttons()
    {
        await _notifier.SendReminderAsync(Reminder(), default);

        var sent = _telegram.SentMessages();
        sent.Count.ShouldBe(1);

        var message = sent[0];
        message.ChatId.ShouldBe(OwnerId);
        message.Text.ShouldBe("⏰ Call the bank");
        message.ParseMode.ShouldBe("HTML");
        message.Buttons().ShouldBe(new[] { "Done", "Snooze 1h", "Tomorrow", "Edit" });
    }

    [Fact]
    public async Task Every_button_carries_a_payload_within_the_platform_limit()
    {
        await _notifier.SendReminderAsync(Reminder(), default);

        foreach (var data in _telegram.SentMessages()[0].CallbackData())
        {
            System.Text.Encoding.UTF8.GetByteCount(data).ShouldBeLessThanOrEqualTo(64);
            data.ShouldStartWith("v1:");
        }
    }

    [Fact]
    public async Task Escapes_markup_characters_in_a_task_title()
    {
        // A title containing < or & is a rejected message rather than a badly formatted one,
        // and it would be rejected at the moment a real reminder fires.
        await _notifier.SendReminderAsync(Reminder("Email <admin> & pay_the_bill"), default);

        _telegram.SentMessages()[0].Text
            .ShouldBe("⏰ Email &lt;admin&gt; &amp; pay_the_bill");
    }

    [Fact]
    public async Task Collapses_several_overdue_reminders_into_one_message()
    {
        var overdue = new[]
        {
            Reminder("First", TimeSpan.FromDays(3)),
            Reminder("Second", TimeSpan.FromDays(2)),
            Reminder("Third", TimeSpan.FromDays(1)),
        };

        await _notifier.SendOverdueSummaryAsync(overdue, default);

        var sent = _telegram.SentMessages();
        sent.Count.ShouldBe(1, "three separate messages after an outage is noise, not a reminder");
        sent[0].Text.ShouldContain("First");
        sent[0].Text.ShouldContain("Second");
        sent[0].Text.ShouldContain("Third");
    }

    [Fact]
    public async Task Marks_a_single_reminder_as_overdue_in_its_text()
    {
        await _notifier.SendReminderAsync(Reminder(overdue: TimeSpan.FromHours(3)), default);

        _telegram.SentMessages()[0].Text.ShouldContain("3h ago");
    }
}
```

- [ ] **Step 5: Run to verify failure**

Run: `dotnet test tests/Assistant.IntegrationTests --filter TelegramNotifierTests`
Expected: FAIL to compile — `TelegramNotifier` does not exist.

- [ ] **Step 6: Implement the notifier**

`src/Assistant.Impl/Telegram/TelegramNotifier.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Impl.Mapping;
using Assistant.Interfaces;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Assistant.Impl.Telegram;

/// <inheritdoc cref="INotifier"/>
public sealed class TelegramNotifier : INotifier
{
    private readonly ITelegramBotClient _bot;
    private readonly TelegramOptions _options;

    /// <summary>Initialises the notifier.</summary>
    /// <param name="bot">Configured Bot API client.</param>
    /// <param name="options">Connection settings, including the owner's chat identifier.</param>
    public TelegramNotifier(ITelegramBotClient bot, IOptions<TelegramOptions> options)
    {
        _bot = bot;
        _options = options.Value;
    }

    /// <inheritdoc/>
    public Task SendReminderAsync(ReminderNotification notification, CancellationToken ct)
        => _bot.SendMessage(
            chatId: _options.OwnerUserId,
            text: TelegramMessageFormatter.Reminder(notification),
            parseMode: ParseMode.Html,
            replyMarkup: BuildKeyboard(notification.TaskId),
            cancellationToken: ct);

    /// <inheritdoc/>
    public Task SendOverdueSummaryAsync(
        IReadOnlyList<ReminderNotification> notifications, CancellationToken ct)
        => _bot.SendMessage(
            chatId: _options.OwnerUserId,
            text: TelegramMessageFormatter.OverdueSummary(notifications),
            parseMode: ParseMode.Html,
            cancellationToken: ct);

    /// <inheritdoc/>
    public Task SendDailyBriefAsync(DailyBriefNotification brief, CancellationToken ct)
        => _bot.SendMessage(
            chatId: _options.OwnerUserId,
            text: TelegramMessageFormatter.DailyBrief(brief),
            parseMode: ParseMode.Html,
            cancellationToken: ct);

    /// <inheritdoc/>
    public Task SendTextAsync(string text, CancellationToken ct)
        => _bot.SendMessage(
            chatId: _options.OwnerUserId,
            text: text,
            parseMode: ParseMode.Html,
            cancellationToken: ct);

    /// <summary>Builds the action buttons attached to a reminder.</summary>
    /// <remarks>
    /// These four cover what a person actually does with a reminder, and each is one tap costing
    /// no language model call.
    /// </remarks>
    private static InlineKeyboardMarkup BuildKeyboard(Guid taskId) => new(
    [
        [
            InlineKeyboardButton.WithCallbackData(
                "Done", CallbackDataExtensions.ToCallbackData(taskId, "done", null)),
            InlineKeyboardButton.WithCallbackData(
                "Snooze 1h", CallbackDataExtensions.ToCallbackData(taskId, "snooze", "1h")),
        ],
        [
            InlineKeyboardButton.WithCallbackData(
                "Tomorrow", CallbackDataExtensions.ToCallbackData(taskId, "resched", "tomorrow")),
            InlineKeyboardButton.WithCallbackData(
                "Edit", CallbackDataExtensions.ToCallbackData(taskId, "edit", null)),
        ],
    ]);
}
```

- [ ] **Step 7: Run and reconcile the client API**

Run: `dotnet test tests/Assistant.IntegrationTests --filter TelegramNotifierTests`
Expected: PASS, 5 tests.

If the build fails on `SendMessage`, the installed `Telegram.Bot` predates the v22 rename. Use `SendTextMessageAsync` with the same arguments — the parameter names are unchanged. Check with:

```bash
dotnet build src/Assistant.Impl 2>&1 | grep -i "does not contain a definition"
```

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: Telegram delivery with HTML escaping and inline action buttons"
```

---

## Task 8: The scheduler and the reminder loop

Spec §6.1, §6.2, §7.4. This is the reliability core, and it is proven before any language model is involved. Tasks are seeded straight into the database.

**Files:**
- Create: `src/Assistant.Impl/Scheduling/{SystemClock,ScheduledJobBase,ReminderScheduler,HeartbeatWriter,SchedulerOptions}.cs`
- Create: `src/Assistant.Impl/Services/Jobs/DueReminderJob.cs`
- Create: `tests/Assistant.IntegrationTests/Reminders/DueReminderJobTests.cs`

**Interfaces:**
- Consumes: `IScheduledJob`, `ITaskService`, `ITaskRepository`, `INotifier`, `IClock`, `ILocalTimeResolver`.
- Produces:
  - `SystemClock : IClock`
  - `ScheduledJobBase : IScheduledJob` with `protected abstract Task ExecuteAsync(CancellationToken)`
  - `DueReminderJob : ScheduledJobBase`, `Name` = `"due-reminders"`
  - `ReminderScheduler : BackgroundService`
  - `SchedulerOptions` with `TickSeconds` (default 30), `BatchSize` (default 50), `OverdueSummaryThresholdHours` (default 24), `HeartbeatPath` (default `/tmp/heartbeat`), section `"Scheduler"`

- [ ] **Step 1: Add hosting packages**

```bash
dotnet add src/Assistant.Impl package Microsoft.Extensions.Hosting.Abstractions
```

- [ ] **Step 2: Write the clock, options, and heartbeat**

`src/Assistant.Impl/Scheduling/SystemClock.cs`:

```csharp
using Assistant.Interfaces;

namespace Assistant.Impl.Scheduling;

/// <inheritdoc cref="IClock"/>
public sealed class SystemClock : IClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

`src/Assistant.Impl/Scheduling/SchedulerOptions.cs`:

```csharp
namespace Assistant.Impl.Scheduling;

/// <summary>Configuration for the recurring job loop.</summary>
public sealed class SchedulerOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Scheduler";

    /// <summary>Seconds between ticks.</summary>
    /// <value>
    /// Thirty by default: fine enough that a reminder is never noticeably late, coarse enough that
    /// the database load is negligible.
    /// </value>
    public int TickSeconds { get; set; } = 30;

    /// <summary>Maximum reminders delivered in a single tick.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// How overdue a reminder must be before it is folded into a summary rather than sent alone.
    /// </summary>
    /// <value>
    /// Twenty-four hours by default. Without this, returning from a week-long outage produces a
    /// burst of individual messages.
    /// </value>
    public int OverdueSummaryThresholdHours { get; set; } = 24;

    /// <summary>Hour of the local day at which the daily brief is due.</summary>
    public int DailyBriefHour { get; set; } = 7;

    /// <summary>File whose modification time signals liveness to the container healthcheck.</summary>
    public string HeartbeatPath { get; set; } = "/tmp/heartbeat";
}
```

`src/Assistant.Impl/Scheduling/HeartbeatWriter.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Impl.Scheduling;

/// <summary>Records that the scheduler loop is still running.</summary>
/// <remarks>
/// The container healthcheck reads this file's modification time. A process that is alive but
/// whose loop has wedged is therefore still detected and restarted, which a plain liveness probe
/// on the process would miss.
/// </remarks>
public sealed class HeartbeatWriter
{
    private readonly SchedulerOptions _options;
    private readonly ILogger<HeartbeatWriter> _log;

    /// <summary>Initialises the writer.</summary>
    /// <param name="options">Scheduler settings, including the heartbeat path.</param>
    /// <param name="log">Log sink.</param>
    public HeartbeatWriter(IOptions<SchedulerOptions> options, ILogger<HeartbeatWriter> log)
    {
        _options = options.Value;
        _log = log;
    }

    /// <summary>Updates the heartbeat file's modification time.</summary>
    /// <remarks>
    /// Failures are logged and swallowed: an unwritable heartbeat file must never be the reason a
    /// reminder is not delivered.
    /// </remarks>
    public void Beat()
    {
        try
        {
            File.WriteAllText(_options.HeartbeatPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not write heartbeat to {Path}", _options.HeartbeatPath);
        }
    }
}
```

- [ ] **Step 3: Write the job base and the scheduler**

`src/Assistant.Impl/Scheduling/ScheduledJobBase.cs`:

```csharp
using Assistant.Interfaces;
using Microsoft.Extensions.Logging;

namespace Assistant.Impl.Scheduling;

/// <summary>Shared behaviour for recurring jobs.</summary>
/// <remarks>
/// Supplies two guarantees every job needs and none should reimplement: a job never overlaps
/// itself when a pass runs longer than the tick interval, and a throwing pass is contained rather
/// than allowed to stop the loop.
/// </remarks>
public abstract class ScheduledJobBase : IScheduledJob
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger _log;

    /// <summary>Initialises the base.</summary>
    /// <param name="log">Log sink used to report contained failures.</param>
    protected ScheduledJobBase(ILogger log) => _log = log;

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken ct)
    {
        // A pass already in flight means this tick has nothing to add. Skipping is correct:
        // whatever is outstanding will still be outstanding on the next tick.
        if (!await _gate.WaitAsync(0, ct))
        {
            _log.LogDebug("Job {Job} still running from a previous tick; skipping", Name);
            return;
        }

        try
        {
            await ExecuteAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Job {Job} failed; the loop continues", Name);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Performs the job's work for one tick.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the pass is finished.</returns>
    protected abstract Task ExecuteAsync(CancellationToken ct);
}
```

`src/Assistant.Impl/Scheduling/ReminderScheduler.cs`:

```csharp
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Impl.Scheduling;

/// <summary>Runs every registered job on a fixed interval.</summary>
/// <remarks>
/// Knows nothing about the jobs it runs: it resolves every <see cref="IScheduledJob"/> from the
/// container, so a new recurring behaviour is a new class and no change here.
/// </remarks>
public sealed class ReminderScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly HeartbeatWriter _heartbeat;
    private readonly SchedulerOptions _options;
    private readonly ILogger<ReminderScheduler> _log;

    /// <summary>Initialises the scheduler.</summary>
    /// <param name="scopes">Factory used to resolve jobs per tick, so scoped services work.</param>
    /// <param name="heartbeat">Liveness recorder.</param>
    /// <param name="options">Tick interval and related settings.</param>
    /// <param name="log">Log sink.</param>
    public ReminderScheduler(
        IServiceScopeFactory scopes,
        HeartbeatWriter heartbeat,
        IOptions<SchedulerOptions> options,
        ILogger<ReminderScheduler> log)
    {
        _scopes = scopes;
        _heartbeat = heartbeat;
        _options = options.Value;
        _log = log;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.TickSeconds));
        _log.LogInformation("Scheduler started; tick every {Seconds}s", _options.TickSeconds);

        // Tick once immediately so a restart delivers anything already owed without waiting.
        await TickAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await TickAsync(stoppingToken);
        }
    }

    /// <summary>Runs one pass of every job.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when every job's pass has finished.</returns>
    /// <remarks>
    /// Exposed so integration tests can drive ticks deterministically instead of waiting on wall
    /// clock time.
    /// </remarks>
    public async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();

        foreach (var job in scope.ServiceProvider.GetServices<IScheduledJob>())
        {
            await job.RunAsync(ct);
        }

        _heartbeat.Beat();
    }
}
```

- [ ] **Step 4: Write the failing reminder-loop tests**

`tests/Assistant.IntegrationTests/Reminders/DueReminderJobTests.cs`:

```csharp
using System.Globalization;
using Assistant.Impl.Mapping;
using Assistant.Impl.Scheduling;
using Assistant.Impl.Services;
using Assistant.Impl.Services.Jobs;
using Assistant.Impl.Telegram;
using Assistant.Interfaces;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Models;
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Telegram.Bot;
using Xunit;

namespace Assistant.IntegrationTests.Reminders;

[Collection(PostgresCollection.Name)]
public sealed class DueReminderJobTests : IAsyncLifetime, IDisposable
{
    private const long OwnerId = 4242;

    private readonly PostgresFixture _postgres;
    private readonly TelegramStub _telegram = new();
    private readonly FakeClock _clock = new();
    private ServiceProvider _provider = null!;

    public DueReminderJobTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        await _postgres.ResetAsync();
        _clock.Set("2026-08-17T07:00:00Z");   // 10:00 local

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssistantRepository(_postgres.ConnectionString);
        services.AddSingleton<IClock>(_clock);
        services.AddSingleton<ILocalTimeResolver, LocalTimeResolver>();
        services.AddScoped<ITaskService, TaskService>();
        services.Configure<SchedulerOptions>(o => o.HeartbeatPath = Path.GetTempFileName());
        services.Configure<TelegramOptions>(o =>
        {
            o.BotToken = "test-token";
            o.OwnerUserId = OwnerId;
            o.BaseUrl = _telegram.BaseUrl;
        });
        services.AddSingleton<ITelegramBotClient>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
            return new TelegramBotClient(new TelegramBotClientOptions(o.BotToken, o.BaseUrl));
        });
        services.AddScoped<INotifier, TelegramNotifier>();
        services.AddScoped<IScheduledJob, DueReminderJob>();

        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    public void Dispose() => _telegram.Dispose();

    private static DateTimeOffset Utc(string iso)
        => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private async Task<Guid> SeedAsync(string title, string dueUtcIso, int attempts = 0)
    {
        using var scope = _provider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        var task = new ReminderTask
        {
            Id = Guid.NewGuid(),
            Title = title,
            Status = ReminderStatus.Pending,
            Priority = Priority.Normal,
            DueAt = Utc(dueUtcIso),
            DeliveryAttempts = attempts,
            CreatedAt = Utc("2026-08-01T00:00:00Z"),
            UpdatedAt = Utc("2026-08-01T00:00:00Z"),
        };
        await repo.AddAsync(task, default);
        return task.Id;
    }

    private async Task TickAsync()
    {
        using var scope = _provider.CreateScope();
        foreach (var job in scope.ServiceProvider.GetServices<IScheduledJob>())
        {
            await job.RunAsync(default);
        }
    }

    private async Task<ReminderTask?> LoadAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITaskRepository>().FindAsync(id, default);
    }

    [Fact]
    public async Task Delivers_a_due_reminder_with_the_exact_text_recipient_and_buttons()
    {
        var id = await SeedAsync("Call the bank", "2026-08-17T07:00:00Z");

        await TickAsync();

        var sent = _telegram.SentMessages();
        sent.Count.ShouldBe(1);
        sent[0].ChatId.ShouldBe(OwnerId);
        sent[0].Text.ShouldBe("⏰ Call the bank");
        sent[0].Buttons().ShouldBe(new[] { "Done", "Snooze 1h", "Tomorrow", "Edit" });

        var stored = await LoadAsync(id);
        stored!.ReminderSentAt.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task Ticking_twice_in_the_same_minute_delivers_exactly_one_message()
    {
        await SeedAsync("Call the bank", "2026-08-17T07:00:00Z");

        await TickAsync();
        await TickAsync();

        _telegram.SentMessages().Count.ShouldBe(1, "the sent marker is what makes this idempotent");
    }

    [Fact]
    public async Task Delivers_a_reminder_missed_during_an_outage_when_the_process_returns()
    {
        // Due at 09:58 local; the process was down and comes back at 10:03.
        await SeedAsync("Call the bank", "2026-08-17T06:58:00Z");
        _clock.Set("2026-08-17T07:03:00Z");

        await TickAsync();

        _telegram.SentMessages().Count.ShouldBe(1, "late is correct; silent is not");
    }

    [Fact]
    public async Task Does_not_deliver_a_reminder_before_it_is_due()
    {
        await SeedAsync("Call the bank", "2026-08-17T09:00:00Z");

        await TickAsync();

        _telegram.SentMessages().ShouldBeEmpty();
    }

    [Fact]
    public async Task Collapses_reminders_overdue_by_more_than_a_day_into_one_summary()
    {
        await SeedAsync("First", "2026-08-14T07:00:00Z");
        await SeedAsync("Second", "2026-08-14T08:00:00Z");
        await SeedAsync("Third", "2026-08-14T09:00:00Z");
        await SeedAsync("Fourth", "2026-08-14T10:00:00Z");
        await SeedAsync("Fifth", "2026-08-14T11:00:00Z");

        await TickAsync();

        var sent = _telegram.SentMessages();
        sent.Count.ShouldBe(1, "five separate messages after an outage is noise");
        sent[0].Text.ShouldContain("First");
        sent[0].Text.ShouldContain("Fifth");
    }

    [Fact]
    public async Task Marks_every_task_in_a_summary_as_delivered()
    {
        var first = await SeedAsync("First", "2026-08-14T07:00:00Z");
        var second = await SeedAsync("Second", "2026-08-14T08:00:00Z");

        await TickAsync();
        _telegram.Reset();
        await TickAsync();

        _telegram.SentMessages().ShouldBeEmpty("a summarised task must not be summarised again");
        (await LoadAsync(first))!.ReminderSentAt.ShouldNotBeNull();
        (await LoadAsync(second))!.ReminderSentAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Snoozing_causes_the_reminder_to_fire_again_at_the_new_time_and_not_before()
    {
        var id = await SeedAsync("Call the bank", "2026-08-17T07:00:00Z");
        await TickAsync();
        _telegram.Reset();

        using (var scope = _provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITaskService>()
                .SnoozeAsync(id, TimeSpan.FromHours(1), default);
        }

        _clock.Set("2026-08-17T07:30:00Z");
        await TickAsync();
        _telegram.SentMessages().ShouldBeEmpty("half an hour into a one-hour snooze");

        _clock.Set("2026-08-17T08:00:00Z");
        await TickAsync();
        _telegram.SentMessages().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Does_not_deliver_a_reminder_for_a_completed_task()
    {
        var id = await SeedAsync("Call the bank", "2026-08-17T07:00:00Z");

        using (var scope = _provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITaskService>().CompleteAsync(id, default);
        }

        await TickAsync();

        _telegram.SentMessages().ShouldBeEmpty();
    }

    [Fact]
    public async Task Stops_retrying_a_task_that_has_already_failed_three_times()
    {
        await SeedAsync("Undeliverable", "2026-08-17T07:00:00Z", attempts: 3);

        await TickAsync();

        _telegram.SentMessages().ShouldBeEmpty("a permanently failing send must not loop forever");
    }

    [Fact]
    public async Task Does_not_mark_a_reminder_sent_when_delivery_fails()
    {
        // This is the reason the design sends before it marks. If the order were reversed, this
        // reminder would be lost permanently rather than retried.
        var id = await SeedAsync("Call the bank", "2026-08-17T07:00:00Z");
        using var brokenTelegram = new BrokenTelegramStub();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssistantRepository(_postgres.ConnectionString);
        services.AddSingleton<IClock>(_clock);
        services.AddSingleton<ILocalTimeResolver, LocalTimeResolver>();
        services.AddScoped<ITaskService, TaskService>();
        services.Configure<SchedulerOptions>(o => o.HeartbeatPath = Path.GetTempFileName());
        services.Configure<TelegramOptions>(o =>
        {
            o.BotToken = "test-token";
            o.OwnerUserId = OwnerId;
            o.BaseUrl = brokenTelegram.BaseUrl;
        });
        services.AddSingleton<ITelegramBotClient>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
            return new TelegramBotClient(new TelegramBotClientOptions(o.BotToken, o.BaseUrl));
        });
        services.AddScoped<INotifier, TelegramNotifier>();
        services.AddScoped<IScheduledJob, DueReminderJob>();

        await using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            foreach (var job in scope.ServiceProvider.GetServices<IScheduledJob>())
            {
                await job.RunAsync(default);
            }
        }

        var stored = await LoadAsync(id);
        stored!.ReminderSentAt.ShouldBeNull("the reminder was never delivered");
        stored.DeliveryAttempts.ShouldBe(1);
    }
}
```

Add the failing stub next to `TelegramStub`:

`tests/Assistant.IntegrationTests/Infrastructure/BrokenTelegramStub.cs`:

```csharp
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>A Telegram stand-in that rejects every send.</summary>
/// <remarks>
/// Used to prove that a failed delivery leaves the reminder owed rather than marking it done.
/// </remarks>
public sealed class BrokenTelegramStub : IDisposable
{
    private readonly WireMockServer _server;

    public BrokenTelegramStub()
    {
        _server = WireMockServer.Start();
        _server
            .Given(Request.Create().WithPath("/bot*/*").UsingAnyMethod())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"ok":false,"error_code":500,"description":"Internal Server Error"}"""));
    }

    public string BaseUrl => _server.Url!;

    public void Dispose() => _server.Dispose();
}
```

- [ ] **Step 5: Run to verify failure**

Run: `dotnet test tests/Assistant.IntegrationTests --filter DueReminderJobTests`
Expected: FAIL to compile — `DueReminderJob` does not exist.

- [ ] **Step 6: Implement the job**

`src/Assistant.Impl/Services/Jobs/DueReminderJob.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Impl.Mapping;
using Assistant.Impl.Scheduling;
using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Impl.Services.Jobs;

/// <summary>Delivers reminders whose due time has arrived.</summary>
/// <remarks>
/// <para>
/// The query has no lower bound on the due time, which is what makes recovery from an outage
/// automatic rather than something to orchestrate: a reminder missed while the process was down
/// is still owed and is delivered on the next tick.
/// </para>
/// <para>
/// Delivery is at-least-once. The message is sent first and only then marked, because the reverse
/// order loses a reminder whenever the send fails after the write. A duplicate is an annoyance; a
/// miss defeats the product.
/// </para>
/// </remarks>
public sealed class DueReminderJob : ScheduledJobBase
{
    private readonly ITaskRepository _tasks;
    private readonly ITaskService _service;
    private readonly INotifier _notifier;
    private readonly ILocalTimeResolver _time;
    private readonly IClock _clock;
    private readonly SchedulerOptions _options;
    private readonly ILogger<DueReminderJob> _log;

    /// <summary>Initialises the job.</summary>
    /// <param name="tasks">Read access to tasks awaiting delivery.</param>
    /// <param name="service">The single writer, used to record delivery outcomes.</param>
    /// <param name="notifier">Outbound message channel.</param>
    /// <param name="time">Renders due times in local time.</param>
    /// <param name="clock">Source of the current time.</param>
    /// <param name="options">Batch size and the overdue summary threshold.</param>
    /// <param name="log">Log sink.</param>
    public DueReminderJob(
        ITaskRepository tasks,
        ITaskService service,
        INotifier notifier,
        ILocalTimeResolver time,
        IClock clock,
        IOptions<SchedulerOptions> options,
        ILogger<DueReminderJob> log)
        : base(log)
    {
        _tasks = tasks;
        _service = service;
        _notifier = notifier;
        _time = time;
        _clock = clock;
        _options = options.Value;
        _log = log;
    }

    /// <inheritdoc/>
    public override string Name => "due-reminders";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var due = await _tasks.GetDueRemindersAsync(now, _options.BatchSize, ct);

        if (due.Count == 0)
        {
            return;
        }

        var threshold = now - TimeSpan.FromHours(_options.OverdueSummaryThresholdHours);
        var stale = due.Where(t => t.DueAt <= threshold).ToList();
        var fresh = due.Where(t => t.DueAt > threshold).ToList();

        if (stale.Count > 0)
        {
            await DeliverSummaryAsync(stale, now, ct);
        }

        foreach (var task in fresh)
        {
            await DeliverOneAsync(task, now, ct);
        }
    }

    private async Task DeliverOneAsync(ReminderTask task, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await _notifier.SendReminderAsync(task.ToNotification(_time, now), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Delivery failed for task {TaskId}; it stays owed", task.Id);
            await _service.RecordDeliveryFailureAsync(task.Id, ct);
            return;
        }

        await _service.MarkReminderSentAsync(task.Id, ct);
    }

    private async Task DeliverSummaryAsync(
        IReadOnlyList<ReminderTask> stale, DateTimeOffset now, CancellationToken ct)
    {
        var notifications = stale.Select(t => t.ToNotification(_time, now)).ToList();

        try
        {
            await _notifier.SendOverdueSummaryAsync(notifications, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Overdue summary failed for {Count} tasks; they stay owed", stale.Count);
            foreach (var task in stale)
            {
                await _service.RecordDeliveryFailureAsync(task.Id, ct);
            }
            return;
        }

        // Every task named in the summary counts as delivered, otherwise the same batch is
        // summarised again on the next tick.
        foreach (var task in stale)
        {
            await _service.MarkReminderSentAsync(task.Id, ct);
        }
    }
}
```

- [ ] **Step 7: Run to verify the tests pass**

Run: `dotnet test tests/Assistant.IntegrationTests --filter DueReminderJobTests`
Expected: PASS, 10 tests.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: scheduler and at-least-once reminder delivery with outage recovery"
```

---

## Task 9: Button actions and callback handling

Spec §6.4. Every action is idempotent, the press is always acknowledged, and the original message is edited in place.

**Files:**
- Create: `src/Assistant.Impl/Services/Actions/{DoneAction,SnoozeAction,RescheduleAction,EditAction}.cs`
- Create: `src/Assistant.Impl/Services/CallbackHandler.cs`
- Create: `src/Assistant.Interfaces/IMessageEditor.cs`
- Create: `src/Assistant.Impl/Telegram/TelegramMessageEditor.cs`
- Create: `tests/Assistant.IntegrationTests/Telegram/CallbackActionTests.cs`
- Create: `tests/Assistant.UnitTests/Services/SnoozeArgumentTests.cs`

**Interfaces:**
- Consumes: `ITaskAction`, `ICallbackHandler`, `ITaskService`, `CallbackDataExtensions`, `ILocalTimeResolver`.
- Produces:
  - Action keys: `"done"`, `"snooze"`, `"resched"`, `"edit"`
  - `IMessageEditor.EditAsync(int messageId, string text, bool removeButtons, CancellationToken)`
  - `IMessageEditor.AcknowledgeAsync(string callbackId, string? toast, CancellationToken)`
  - `SnoozeAction.TryParseDuration(string? argument, out TimeSpan duration)` → `bool`
  - `RescheduleAction.TryResolveTarget(string? argument, ILocalTimeResolver, out DateTimeOffset utc)` → `bool`

- [ ] **Step 1: Declare the editor interface**

`src/Assistant.Interfaces/IMessageEditor.cs`:

```csharp
namespace Assistant.Interfaces;

/// <summary>Updates messages already delivered to the user.</summary>
public interface IMessageEditor
{
    /// <summary>Replaces the text of an existing message.</summary>
    /// <param name="messageId">Identifier of the message to change.</param>
    /// <param name="text">The replacement body. May contain the supported HTML subset.</param>
    /// <param name="removeButtons">Whether to strip the message's buttons.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the edit has been accepted.</returns>
    /// <remarks>
    /// Editing rather than replying keeps the conversation readable: a reminder acted on becomes
    /// a single settled line instead of a pair of messages.
    /// </remarks>
    Task EditAsync(int messageId, string text, bool removeButtons, CancellationToken ct);

    /// <summary>Acknowledges a button press.</summary>
    /// <param name="callbackId">Identifier supplied with the press.</param>
    /// <param name="toast">Optional short text to flash to the user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the acknowledgement has been sent.</returns>
    /// <remarks>
    /// Must be called for every press, including failed ones: an unacknowledged press leaves the
    /// user's client showing a spinner indefinitely.
    /// </remarks>
    Task AcknowledgeAsync(string callbackId, string? toast, CancellationToken ct);
}
```

- [ ] **Step 2: Write the failing unit tests for argument parsing**

`tests/Assistant.UnitTests/Services/SnoozeArgumentTests.cs`:

```csharp
using Assistant.Impl.Services.Actions;
using Shouldly;
using Xunit;

namespace Assistant.UnitTests.Services;

public class SnoozeArgumentTests
{
    [Theory]
    [InlineData("15m", 0, 15)]
    [InlineData("1h", 1, 0)]
    [InlineData("3h", 3, 0)]
    [InlineData("1d", 24, 0)]
    public void Parses_supported_durations(string argument, int hours, int minutes)
    {
        SnoozeAction.TryParseDuration(argument, out var duration).ShouldBeTrue();
        duration.ShouldBe(new TimeSpan(hours, minutes, 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("-1h")]
    [InlineData("0h")]
    [InlineData("999d")]
    public void Rejects_unsupported_arguments(string? argument)
        => SnoozeAction.TryParseDuration(argument, out _).ShouldBeFalse();
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Assistant.UnitTests --filter SnoozeArgumentTests`
Expected: FAIL to compile — `SnoozeAction` does not exist.

- [ ] **Step 4: Implement the four actions**

`src/Assistant.Impl/Services/Actions/DoneAction.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Services.Actions;

/// <summary>Completes a task from its reminder message.</summary>
public sealed class DoneAction : ITaskAction
{
    private readonly ITaskService _tasks;

    /// <summary>Initialises the action.</summary>
    /// <param name="tasks">The single writer.</param>
    public DoneAction(ITaskService tasks) => _tasks = tasks;

    /// <inheritdoc/>
    public string Key => "done";

    /// <inheritdoc/>
    public async Task<(Result Result, string UserMessage, bool RemoveButtons)> ExecuteAsync(
        Guid taskId, string? argument, CancellationToken ct)
    {
        var result = await _tasks.CompleteAsync(taskId, ct);

        // Completion is idempotent, so a second press reads as confirmation rather than an error.
        return result.Succeeded
            ? (result, "✅ Done", true)
            : (result, result.Message ?? "Could not do that", false);
    }
}
```

`src/Assistant.Impl/Services/Actions/SnoozeAction.cs`:

```csharp
using System.Globalization;
using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Services.Actions;

/// <summary>Pushes a task's reminder back by a fixed duration.</summary>
public sealed class SnoozeAction : ITaskAction
{
    /// <summary>Longest snooze accepted from a button.</summary>
    private static readonly TimeSpan MaxSnooze = TimeSpan.FromDays(30);

    private readonly ITaskService _tasks;

    /// <summary>Initialises the action.</summary>
    /// <param name="tasks">The single writer.</param>
    public SnoozeAction(ITaskService tasks) => _tasks = tasks;

    /// <inheritdoc/>
    public string Key => "snooze";

    /// <summary>Parses a snooze argument such as <c>1h</c>.</summary>
    /// <param name="argument">The argument from the button payload.</param>
    /// <param name="duration">The parsed duration when this method returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="true"/> for a positive duration within the accepted range; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static bool TryParseDuration(string? argument, out TimeSpan duration)
    {
        duration = default;

        if (string.IsNullOrWhiteSpace(argument) || argument.Length < 2)
        {
            return false;
        }

        var unit = argument[^1];
        if (!int.TryParse(
                argument[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0)
        {
            return false;
        }

        duration = unit switch
        {
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            _ => TimeSpan.Zero,
        };

        if (duration <= TimeSpan.Zero || duration > MaxSnooze)
        {
            duration = default;
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public async Task<(Result Result, string UserMessage, bool RemoveButtons)> ExecuteAsync(
        Guid taskId, string? argument, CancellationToken ct)
    {
        if (!TryParseDuration(argument, out var duration))
        {
            return (
                Result.Failure(ErrorCode.TimeUnparseable, "I did not understand that snooze."),
                "Could not snooze that",
                false);
        }

        var result = await _tasks.SnoozeAsync(taskId, duration, ct);

        return result.Succeeded
            ? (result, $"💤 Snoozed {argument}", true)
            : (result, result.Message ?? "Could not snooze that", false);
    }
}
```

`src/Assistant.Impl/Services/Actions/RescheduleAction.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Services.Actions;

/// <summary>Moves a task's reminder to a named point in the future.</summary>
public sealed class RescheduleAction : ITaskAction
{
    /// <summary>Local hour used when a named target implies a morning slot.</summary>
    private const int MorningHour = 9;

    private readonly ITaskService _tasks;
    private readonly ILocalTimeResolver _time;
    private readonly IClock _clock;

    /// <summary>Initialises the action.</summary>
    /// <param name="tasks">The single writer.</param>
    /// <param name="time">Converts the resolved local target to a UTC instant.</param>
    /// <param name="clock">Source of the current time.</param>
    public RescheduleAction(ITaskService tasks, ILocalTimeResolver time, IClock clock)
    {
        _tasks = tasks;
        _time = time;
        _clock = clock;
    }

    /// <inheritdoc/>
    public string Key => "resched";

    /// <summary>Resolves a named target such as <c>tomorrow</c> to a UTC instant.</summary>
    /// <param name="argument">The argument from the button payload.</param>
    /// <param name="time">Resolver used to interpret the local target.</param>
    /// <param name="utc">The resolved instant when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the name is recognised; otherwise <see langword="false"/>.</returns>
    public static bool TryResolveTarget(
        string? argument, ILocalTimeResolver time, out DateTimeOffset utc)
    {
        utc = default;

        var localDate = argument switch
        {
            "tomorrow" => time.LocalToday.AddDays(1),
            "nextweek" => time.LocalToday.AddDays(7),
            _ => (DateOnly?)null,
        };

        if (localDate is not { } date)
        {
            return false;
        }

        var (result, resolved) = time.Resolve(
            $"{date:yyyy-MM-dd}T{MorningHour:00}:00:00");

        if (!result.Succeeded || resolved is null)
        {
            return false;
        }

        utc = resolved.Value;
        return true;
    }

    /// <inheritdoc/>
    public async Task<(Result Result, string UserMessage, bool RemoveButtons)> ExecuteAsync(
        Guid taskId, string? argument, CancellationToken ct)
    {
        if (!TryResolveTarget(argument, _time, out var target))
        {
            return (
                Result.Failure(ErrorCode.TimeUnparseable, "I did not understand that target."),
                "Could not reschedule that",
                false);
        }

        var result = await _tasks.RescheduleAsync(taskId, target, ct);
        var local = _time.ToLocal(target);

        return result.Succeeded
            ? (result, $"📅 Moved to {local:ddd d MMM, HH:mm}", true)
            : (result, result.Message ?? "Could not reschedule that", false);
    }
}
```

`src/Assistant.Impl/Services/Actions/EditAction.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Services.Actions;

/// <summary>Asks the user what to change, and routes their next message to that task.</summary>
/// <remarks>
/// The only action that costs a language model call, and only on the follow-up message rather
/// than on the press itself.
/// </remarks>
public sealed class EditAction : ITaskAction
{
    private readonly IPendingEditStore _pending;

    /// <summary>Initialises the action.</summary>
    /// <param name="pending">Store recording which task the next message applies to.</param>
    public EditAction(IPendingEditStore pending) => _pending = pending;

    /// <inheritdoc/>
    public string Key => "edit";

    /// <inheritdoc/>
    public Task<(Result Result, string UserMessage, bool RemoveButtons)> ExecuteAsync(
        Guid taskId, string? argument, CancellationToken ct)
    {
        _pending.Set(taskId);
        return Task.FromResult((Result.Success(), "✏️ What should it say instead?", false));
    }
}
```

`src/Assistant.Interfaces/IPendingEditStore.cs`:

```csharp
namespace Assistant.Interfaces;

/// <summary>Remembers that the user's next message is an edit to a specific task.</summary>
/// <remarks>
/// Held in memory only. A restart between the press and the follow-up message simply means the
/// follow-up is treated as a new instruction, which is a reasonable outcome and not worth a table.
/// </remarks>
public interface IPendingEditStore
{
    /// <summary>Records that the next message edits the given task.</summary>
    /// <param name="taskId">Identifier of the task being edited.</param>
    void Set(Guid taskId);

    /// <summary>Takes and clears the pending edit, if there is one.</summary>
    /// <returns>
    /// The task identifier awaiting an edit, or <see langword="null"/> when the next message
    /// should be treated as a fresh instruction.
    /// </returns>
    Guid? Take();
}
```

`src/Assistant.Impl/Services/PendingEditStore.cs`:

```csharp
using Assistant.Interfaces;

namespace Assistant.Impl.Services;

/// <inheritdoc cref="IPendingEditStore"/>
public sealed class PendingEditStore : IPendingEditStore
{
    private Guid? _taskId;

    /// <inheritdoc/>
    public void Set(Guid taskId) => Interlocked.Exchange(ref _taskId, taskId);

    /// <inheritdoc/>
    public Guid? Take() => Interlocked.Exchange(ref _taskId, null);
}
```

- [ ] **Step 5: Implement the callback handler and the editor**

`src/Assistant.Impl/Services/CallbackHandler.cs`:

```csharp
using Assistant.Impl.Mapping;
using Assistant.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Assistant.Impl.Telegram;

namespace Assistant.Impl.Services;

/// <inheritdoc cref="ICallbackHandler"/>
public sealed class CallbackHandler : ICallbackHandler
{
    private readonly IReadOnlyDictionary<string, ITaskAction> _actions;
    private readonly IMessageEditor _editor;
    private readonly TelegramOptions _telegram;
    private readonly ILogger<CallbackHandler> _log;

    /// <summary>Initialises the handler.</summary>
    /// <param name="actions">Every available action, indexed by key on construction.</param>
    /// <param name="editor">Used to acknowledge the press and update the message.</param>
    /// <param name="telegram">Settings carrying the owner's identifier.</param>
    /// <param name="log">Log sink.</param>
    public CallbackHandler(
        IEnumerable<ITaskAction> actions,
        IMessageEditor editor,
        IOptions<TelegramOptions> telegram,
        ILogger<CallbackHandler> log)
    {
        _actions = actions.ToDictionary(a => a.Key, StringComparer.Ordinal);
        _editor = editor;
        _telegram = telegram.Value;
        _log = log;
    }

    /// <inheritdoc/>
    public async Task HandleAsync(
        long senderUserId, string callbackId, int messageId, string callbackData, CancellationToken ct)
    {
        if (senderUserId != _telegram.OwnerUserId)
        {
            _log.LogWarning("Discarded button press from {UserId}", senderUserId);
            return;
        }

        if (!CallbackDataExtensions.TryParseCallbackData(callbackData, out var payload))
        {
            // Most likely a button from an older payload format still sitting in chat history.
            await _editor.AcknowledgeAsync(callbackId, "That button is out of date", ct);
            return;
        }

        if (!_actions.TryGetValue(payload.Action, out var action))
        {
            await _editor.AcknowledgeAsync(callbackId, "I no longer know that action", ct);
            return;
        }

        var (result, userMessage, removeButtons) =
            await action.ExecuteAsync(payload.TaskId, payload.Argument, ct);

        // Acknowledge before editing: the spinner clears immediately even if the edit is slow.
        await _editor.AcknowledgeAsync(callbackId, userMessage, ct);

        if (result.Succeeded && removeButtons)
        {
            await _editor.EditAsync(messageId, userMessage, removeButtons: true, ct);
        }
    }
}
```

`src/Assistant.Impl/Telegram/TelegramMessageEditor.cs`:

```csharp
using Assistant.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <inheritdoc cref="IMessageEditor"/>
public sealed class TelegramMessageEditor : IMessageEditor
{
    private readonly ITelegramBotClient _bot;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramMessageEditor> _log;

    /// <summary>Initialises the editor.</summary>
    /// <param name="bot">Configured Bot API client.</param>
    /// <param name="options">Settings carrying the owner's chat identifier.</param>
    /// <param name="log">Log sink.</param>
    public TelegramMessageEditor(
        ITelegramBotClient bot, IOptions<TelegramOptions> options, ILogger<TelegramMessageEditor> log)
    {
        _bot = bot;
        _options = options.Value;
        _log = log;
    }

    /// <inheritdoc/>
    public async Task EditAsync(int messageId, string text, bool removeButtons, CancellationToken ct)
    {
        try
        {
            await _bot.EditMessageText(
                chatId: _options.OwnerUserId,
                messageId: messageId,
                text: text,
                parseMode: ParseMode.Html,
                replyMarkup: removeButtons ? null : null,
                cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed cosmetic edit must not surface as a failed action: the task has already
            // been changed and the user has already been told.
            _log.LogWarning(ex, "Could not edit message {MessageId}", messageId);
        }
    }

    /// <inheritdoc/>
    public async Task AcknowledgeAsync(string callbackId, string? toast, CancellationToken ct)
    {
        try
        {
            await _bot.AnswerCallbackQuery(callbackId, toast, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Could not acknowledge callback {CallbackId}", callbackId);
        }
    }
}
```

- [ ] **Step 6: Write the failing action integration tests**

`tests/Assistant.IntegrationTests/Telegram/CallbackActionTests.cs`:

```csharp
using System.Globalization;
using Assistant.Impl.Mapping;
using Assistant.Impl.Scheduling;
using Assistant.Impl.Services;
using Assistant.Impl.Services.Actions;
using Assistant.Impl.Telegram;
using Assistant.Contracts;
using Assistant.Interfaces;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Models;
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Telegram.Bot;
using Xunit;

namespace Assistant.IntegrationTests.Telegram;

[Collection(PostgresCollection.Name)]
public sealed class CallbackActionTests : IAsyncLifetime, IDisposable
{
    private const long OwnerId = 4242;
    private const long StrangerId = 9999;

    private readonly PostgresFixture _postgres;
    private readonly TelegramStub _telegram = new();
    private readonly FakeClock _clock = new();
    private ServiceProvider _provider = null!;

    public CallbackActionTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        await _postgres.ResetAsync();
        _clock.Set("2026-08-17T07:00:00Z");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssistantRepository(_postgres.ConnectionString);
        services.AddSingleton<IClock>(_clock);
        services.AddSingleton<ILocalTimeResolver, LocalTimeResolver>();
        services.AddSingleton<IPendingEditStore, PendingEditStore>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<ITaskAction, DoneAction>();
        services.AddScoped<ITaskAction, SnoozeAction>();
        services.AddScoped<ITaskAction, RescheduleAction>();
        services.AddScoped<ITaskAction, EditAction>();
        services.AddScoped<ICallbackHandler, CallbackHandler>();
        services.Configure<TelegramOptions>(o =>
        {
            o.BotToken = "test-token";
            o.OwnerUserId = OwnerId;
            o.BaseUrl = _telegram.BaseUrl;
        });
        services.AddSingleton<ITelegramBotClient>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
            return new TelegramBotClient(new TelegramBotClientOptions(o.BotToken, o.BaseUrl));
        });
        services.AddScoped<IMessageEditor, TelegramMessageEditor>();

        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    public void Dispose() => _telegram.Dispose();

    private static DateTimeOffset Utc(string iso)
        => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private async Task<Guid> SeedAsync()
    {
        using var scope = _provider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        var task = new ReminderTask
        {
            Id = Guid.NewGuid(),
            Title = "Call the bank",
            Status = ReminderStatus.Pending,
            Priority = Priority.Normal,
            DueAt = Utc("2026-08-17T07:00:00Z"),
            CreatedAt = Utc("2026-08-16T20:00:00Z"),
            UpdatedAt = Utc("2026-08-16T20:00:00Z"),
        };
        await repo.AddAsync(task, default);
        return task.Id;
    }

    private async Task PressAsync(Guid taskId, string action, string? argument, long sender = OwnerId)
    {
        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICallbackHandler>().HandleAsync(
            sender,
            callbackId: "cb-1",
            messageId: 77,
            callbackData: CallbackDataExtensions.ToCallbackData(taskId, action, argument),
            ct: default);
    }

    private async Task<ReminderTask?> LoadAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITaskRepository>().FindAsync(id, default);
    }

    [Fact]
    public async Task Done_completes_the_task_acknowledges_and_edits_the_message_in_place()
    {
        var id = await SeedAsync();

        await PressAsync(id, "done", null);

        (await LoadAsync(id))!.Status.ShouldBe(ReminderStatus.Completed);
        _telegram.AcknowledgedCallbacks().ShouldBe(1, "an unacknowledged press spins forever");

        var edits = _telegram.EditedMessages();
        edits.Count.ShouldBe(1, "the original message is updated, not replied to");
        edits[0].Text.ShouldBe("✅ Done");
        edits[0].Buttons().ShouldBeEmpty();
    }

    [Fact]
    public async Task Done_pressed_twice_acknowledges_both_times_without_error()
    {
        var id = await SeedAsync();

        await PressAsync(id, "done", null);
        await PressAsync(id, "done", null);

        _telegram.AcknowledgedCallbacks().ShouldBe(2);
        (await LoadAsync(id))!.Status.ShouldBe(ReminderStatus.Completed);
    }

    [Fact]
    public async Task Snooze_moves_the_due_time_forward_by_the_argument()
    {
        var id = await SeedAsync();

        await PressAsync(id, "snooze", "1h");

        (await LoadAsync(id))!.DueAt.ShouldBe(Utc("2026-08-17T08:00:00Z"));
    }

    [Fact]
    public async Task Reschedule_to_tomorrow_targets_nine_local_the_next_day()
    {
        var id = await SeedAsync();

        await PressAsync(id, "resched", "tomorrow");

        // 09:00 local on 18 August is 06:00Z, Israel being UTC+3 in August.
        (await LoadAsync(id))!.DueAt.ShouldBe(Utc("2026-08-18T06:00:00Z"));
    }

    [Fact]
    public async Task Edit_records_the_task_and_leaves_the_buttons_in_place()
    {
        var id = await SeedAsync();

        await PressAsync(id, "edit", null);

        _provider.GetRequiredService<IPendingEditStore>().Take().ShouldBe(id);
        _telegram.EditedMessages().ShouldBeEmpty("the reminder is still actionable");
        _telegram.AcknowledgedCallbacks().ShouldBe(1);
    }

    [Fact]
    public async Task A_press_from_a_stranger_changes_nothing()
    {
        var id = await SeedAsync();

        await PressAsync(id, "done", null, sender: StrangerId);

        (await LoadAsync(id))!.Status.ShouldBe(ReminderStatus.Pending);
        _telegram.AcknowledgedCallbacks().ShouldBe(0);
    }

    [Fact]
    public async Task An_unrecognised_action_is_acknowledged_rather_than_thrown()
    {
        var id = await SeedAsync();

        await PressAsync(id, "teleport", null);

        _telegram.AcknowledgedCallbacks().ShouldBe(1);
        (await LoadAsync(id))!.Status.ShouldBe(ReminderStatus.Pending);
    }

    [Fact]
    public async Task A_button_from_an_older_payload_version_degrades_politely()
    {
        using var scope = _provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<ICallbackHandler>()
            .HandleAsync(OwnerId, "cb-1", 77, "v0:done:whatever", default);

        _telegram.AcknowledgedCallbacks().ShouldBe(1, "old buttons must not throw");
    }
}
```

- [ ] **Step 7: Run to verify the tests pass**

Run: `dotnet test tests/Assistant.IntegrationTests --filter CallbackActionTests`
Expected: PASS, 8 tests. Also run the unit filter: `dotnet test tests/Assistant.UnitTests --filter SnoozeArgumentTests` → PASS, 10 cases.

If `EditMessageText` or `AnswerCallbackQuery` do not resolve, the installed `Telegram.Bot` predates the v22 renames; the earlier names are `EditMessageTextAsync` and `AnswerCallbackQueryAsync`.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: idempotent button actions with in-place message editing"
```

---

## Task 10: Refit language model clients with provider fallback

Spec §5.5, §12.3. No `HttpClient` is used directly. Both providers are stubbed by WireMock in tests, which is what keeps CI credential-free.

**Files:**
- Create: `src/Assistant.Contracts/{ChatTurn,ChatToolDefinition,ChatRequest,ChatReply,ChatToolCall}.cs`
- Create: `src/Assistant.Interfaces/IChatCompletionClient.cs`
- Create: `src/Assistant.Impl/Ai/{LlmOptions,AnthropicDtos,IAnthropicApi,AnthropicChatClient,OpenRouterDtos,IOpenRouterApi,OpenRouterChatClient,FallbackChatClient}.cs`
- Create: `tests/Assistant.IntegrationTests/Infrastructure/AnthropicStub.cs`
- Create: `tests/Assistant.IntegrationTests/Ai/ChatClientTests.cs`

**Interfaces:**
- Consumes: `Result`, `ErrorCode`.
- Produces:
  - `IChatCompletionClient.CompleteAsync(ChatRequest, CancellationToken)` → `Task<ChatReply>`
  - `ChatReply` with `string? Text`, `IReadOnlyList<ChatToolCall> ToolCalls`
  - `ChatToolCall` with `string Id`, `string Name`, `string ArgumentsJson`
  - `ChatTurn` with `string Role`, `string Content`, `string? ToolCallId`
  - `LlmOptions` with nested `ProviderOptions` for `Anthropic` and `OpenRouter`, section `"Llm"`
  - `AnthropicStub` with `string BaseUrl`, `void RespondWithToolCall(string toolName, string argumentsJson)`, `void RespondWithText(string text)`, `void RespondWithError(int status)`, `int RequestCount()`

The interface is deliberately **not** named `IChatClient`: that name is taken by `Microsoft.Extensions.AI`, and importing both produces ambiguous references.

- [ ] **Step 1: Add packages**

```bash
dotnet add src/Assistant.Impl package Refit
dotnet add src/Assistant.Impl package Refit.HttpClientFactory
dotnet add src/Assistant.Impl package Microsoft.Extensions.Http.Resilience
```

- [ ] **Step 2: Write the provider-neutral chat contracts**

`src/Assistant.Contracts/ChatTurn.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>One turn in a conversation sent to a language model.</summary>
/// <param name="Role">Who produced the turn: <c>user</c>, <c>assistant</c>, or <c>tool</c>.</param>
/// <param name="Content">The turn's text, or a tool's result.</param>
/// <param name="ToolCallId">
/// For a tool result, the identifier of the call it answers; otherwise <see langword="null"/>.
/// </param>
public sealed record ChatTurn(string Role, string Content, string? ToolCallId = null);
```

`src/Assistant.Contracts/ChatToolDefinition.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>A capability offered to the model.</summary>
/// <param name="Name">The tool's name, in lowercase snake case.</param>
/// <param name="Description">What the tool does, written for the model.</param>
/// <param name="ParametersJsonSchema">A JSON Schema object describing the parameters.</param>
public sealed record ChatToolDefinition(string Name, string Description, string ParametersJsonSchema);
```

`src/Assistant.Contracts/ChatToolCall.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>A tool invocation requested by the model.</summary>
/// <param name="Id">Identifier the model uses to match the result to this call.</param>
/// <param name="Name">Which tool to invoke.</param>
/// <param name="ArgumentsJson">The arguments, as a JSON object.</param>
public sealed record ChatToolCall(string Id, string Name, string ArgumentsJson);
```

`src/Assistant.Contracts/ChatRequest.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>A request for one model completion.</summary>
/// <param name="SystemPrompt">Instructions and the current local time.</param>
/// <param name="Turns">Conversation so far, oldest first.</param>
/// <param name="Tools">Capabilities the model may invoke.</param>
public sealed record ChatRequest(
    string SystemPrompt,
    IReadOnlyList<ChatTurn> Turns,
    IReadOnlyList<ChatToolDefinition> Tools);
```

`src/Assistant.Contracts/ChatReply.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>What the model returned.</summary>
/// <param name="Text">Prose for the user, or <see langword="null"/> when the model only called tools.</param>
/// <param name="ToolCalls">Tools the model wants invoked before it can answer.</param>
public sealed record ChatReply(string? Text, IReadOnlyList<ChatToolCall> ToolCalls)
{
    /// <summary>Whether the model is waiting on tool results.</summary>
    /// <value><see langword="true"/> when at least one tool call is pending.</value>
    public bool WantsTools => ToolCalls.Count > 0;
}
```

`src/Assistant.Interfaces/IChatCompletionClient.cs`:

```csharp
using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>A language model that can call tools.</summary>
/// <remarks>
/// Deliberately not named <c>IChatClient</c>, which collides with the type of that name in
/// <c>Microsoft.Extensions.AI</c>.
/// </remarks>
public interface IChatCompletionClient
{
    /// <summary>Name used in logs to identify which provider answered.</summary>
    string ProviderName { get; }

    /// <summary>Requests one completion.</summary>
    /// <param name="request">Prompt, history, and available tools.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The model's reply, which may be prose, tool calls, or both.</returns>
    /// <exception cref="Exception">
    /// Thrown when the provider cannot be reached or rejects the request. Callers are expected to
    /// fall through to another provider rather than surfacing this to the user.
    /// </exception>
    Task<ChatReply> CompleteAsync(ChatRequest request, CancellationToken ct);
}
```

- [ ] **Step 3: Write the options**

`src/Assistant.Impl/Ai/LlmOptions.cs`:

```csharp
namespace Assistant.Impl.Ai;

/// <summary>Settings for one language model provider.</summary>
public sealed class ProviderOptions
{
    /// <summary>API key. Empty disables the provider.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Model identifier to request.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Base address of the provider's API.</summary>
    /// <value>Overridden in tests to point at a local stub.</value>
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>Language model configuration.</summary>
public sealed class LlmOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Llm";

    /// <summary>Primary provider.</summary>
    public ProviderOptions Anthropic { get; set; } = new()
    {
        Model = "claude-sonnet-4-5",
        BaseUrl = "https://api.anthropic.com",
    };

    /// <summary>Fallback provider, used only when the primary fails.</summary>
    public ProviderOptions OpenRouter { get; set; } = new()
    {
        Model = "anthropic/claude-sonnet-4.5",
        BaseUrl = "https://openrouter.ai",
    };

    /// <summary>Largest number of tokens to request in a reply.</summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>Maximum completions per minute, as a spend guard against a runaway loop.</summary>
    public int MaxCallsPerMinute { get; set; } = 20;

    /// <summary>Maximum tool-call rounds before the loop gives up on a single message.</summary>
    public int MaxToolRounds { get; set; } = 5;
}
```

- [ ] **Step 4: Write the Anthropic Refit interface and its wire types**

`src/Assistant.Impl/Ai/AnthropicDtos.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Assistant.Impl.Ai;

/// <summary>Request body for the Anthropic Messages API.</summary>
public sealed record AnthropicRequest
{
    /// <summary>Model identifier.</summary>
    [JsonPropertyName("model")] public string Model { get; init; } = string.Empty;

    /// <summary>Maximum tokens in the reply.</summary>
    [JsonPropertyName("max_tokens")] public int MaxTokens { get; init; }

    /// <summary>System instructions.</summary>
    [JsonPropertyName("system")] public string? System { get; init; }

    /// <summary>Conversation turns, oldest first.</summary>
    [JsonPropertyName("messages")] public IReadOnlyList<AnthropicMessage> Messages { get; init; } = [];

    /// <summary>Tools the model may call.</summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AnthropicTool>? Tools { get; init; }
}

/// <summary>One conversation turn.</summary>
/// <param name="Role">Either <c>user</c> or <c>assistant</c>.</param>
/// <param name="Content">The turn's content blocks.</param>
public sealed record AnthropicMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] IReadOnlyList<AnthropicContentBlock> Content);

/// <summary>One content block within a turn.</summary>
public sealed record AnthropicContentBlock
{
    /// <summary>Block kind: <c>text</c>, <c>tool_use</c>, or <c>tool_result</c>.</summary>
    [JsonPropertyName("type")] public string Type { get; init; } = "text";

    /// <summary>Body of a text block.</summary>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    /// <summary>Identifier of a tool call.</summary>
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    /// <summary>Name of the tool being called.</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    /// <summary>Arguments supplied to the tool.</summary>
    [JsonPropertyName("input")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public System.Text.Json.JsonElement? Input { get; init; }

    /// <summary>Identifier of the call a tool result answers.</summary>
    [JsonPropertyName("tool_use_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolUseId { get; init; }

    /// <summary>Body of a tool result.</summary>
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResultContent { get; init; }
}

/// <summary>A tool definition offered to the model.</summary>
/// <param name="Name">Tool name.</param>
/// <param name="Description">What it does.</param>
/// <param name="InputSchema">JSON Schema for the parameters.</param>
public sealed record AnthropicTool(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("input_schema")] System.Text.Json.JsonElement InputSchema);

/// <summary>Response body from the Messages API.</summary>
public sealed record AnthropicResponse
{
    /// <summary>Content blocks the model produced.</summary>
    [JsonPropertyName("content")] public IReadOnlyList<AnthropicContentBlock> Content { get; init; } = [];

    /// <summary>Why generation stopped.</summary>
    [JsonPropertyName("stop_reason")] public string? StopReason { get; init; }
}
```

`src/Assistant.Impl/Ai/IAnthropicApi.cs`:

```csharp
using Refit;

namespace Assistant.Impl.Ai;

/// <summary>The Anthropic Messages API.</summary>
/// <remarks>
/// The interface is the contract: authentication headers and the base address are attached at
/// registration, so pointing this at a stub in tests needs no production seam.
/// </remarks>
public interface IAnthropicApi
{
    /// <summary>Requests a completion.</summary>
    /// <param name="request">Model, prompt, conversation, and tools.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The model's content blocks, which may include tool calls.</returns>
    [Post("/v1/messages")]
    Task<AnthropicResponse> CreateMessageAsync([Body] AnthropicRequest request, CancellationToken ct = default);
}
```

- [ ] **Step 5: Write the Anthropic adapter**

`src/Assistant.Impl/Ai/AnthropicChatClient.cs`:

```csharp
using System.Text.Json;
using Assistant.Contracts;
using Assistant.Interfaces;
using Microsoft.Extensions.Options;

namespace Assistant.Impl.Ai;

/// <inheritdoc cref="IChatCompletionClient"/>
public sealed class AnthropicChatClient : IChatCompletionClient
{
    private readonly IAnthropicApi _api;
    private readonly LlmOptions _options;

    /// <summary>Initialises the client.</summary>
    /// <param name="api">The generated Messages API client.</param>
    /// <param name="options">Model name and token budget.</param>
    public AnthropicChatClient(IAnthropicApi api, IOptions<LlmOptions> options)
    {
        _api = api;
        _options = options.Value;
    }

    /// <inheritdoc/>
    public string ProviderName => "anthropic";

    /// <inheritdoc/>
    public async Task<ChatReply> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        var response = await _api.CreateMessageAsync(
            new AnthropicRequest
            {
                Model = _options.Anthropic.Model,
                MaxTokens = _options.MaxTokens,
                System = request.SystemPrompt,
                Messages = request.Turns.Select(ToMessage).ToList(),
                Tools = request.Tools.Count == 0 ? null : request.Tools.Select(ToTool).ToList(),
            },
            ct);

        var text = string.Join(
            "\n",
            response.Content.Where(b => b.Type == "text" && b.Text is not null).Select(b => b.Text));

        var calls = response.Content
            .Where(b => b.Type == "tool_use" && b.Id is not null && b.Name is not null)
            .Select(b => new ChatToolCall(b.Id!, b.Name!, b.Input?.GetRawText() ?? "{}"))
            .ToList();

        return new ChatReply(string.IsNullOrWhiteSpace(text) ? null : text, calls);
    }

    private static AnthropicMessage ToMessage(ChatTurn turn) => turn.Role switch
    {
        // A tool result is carried on a user turn in this API's shape.
        "tool" => new AnthropicMessage("user",
        [
            new AnthropicContentBlock
            {
                Type = "tool_result",
                ToolUseId = turn.ToolCallId,
                ResultContent = turn.Content,
            },
        ]),
        _ => new AnthropicMessage(turn.Role,
        [
            new AnthropicContentBlock { Type = "text", Text = turn.Content },
        ]),
    };

    private static AnthropicTool ToTool(ChatToolDefinition tool) => new(
        tool.Name,
        tool.Description,
        JsonDocument.Parse(tool.ParametersJsonSchema).RootElement.Clone());
}
```

- [ ] **Step 6: Write the OpenRouter client**

`src/Assistant.Impl/Ai/OpenRouterDtos.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Assistant.Impl.Ai;

/// <summary>Request body for an OpenAI-compatible chat completion.</summary>
public sealed record OpenRouterRequest
{
    /// <summary>Model identifier.</summary>
    [JsonPropertyName("model")] public string Model { get; init; } = string.Empty;

    /// <summary>Conversation turns, oldest first, including the system turn.</summary>
    [JsonPropertyName("messages")] public IReadOnlyList<OpenRouterMessage> Messages { get; init; } = [];

    /// <summary>Tools the model may call.</summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OpenRouterTool>? Tools { get; init; }

    /// <summary>Maximum tokens in the reply.</summary>
    [JsonPropertyName("max_tokens")] public int MaxTokens { get; init; }
}

/// <summary>One conversation turn.</summary>
public sealed record OpenRouterMessage
{
    /// <summary>Turn role: <c>system</c>, <c>user</c>, <c>assistant</c>, or <c>tool</c>.</summary>
    [JsonPropertyName("role")] public string Role { get; init; } = string.Empty;

    /// <summary>Turn text.</summary>
    [JsonPropertyName("content")] public string? Content { get; init; }

    /// <summary>For a tool turn, the call it answers.</summary>
    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; init; }

    /// <summary>Tool calls the assistant requested.</summary>
    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OpenRouterToolCall>? ToolCalls { get; init; }
}

/// <summary>A tool definition in OpenAI's wrapper shape.</summary>
/// <param name="Type">Always <c>function</c>.</param>
/// <param name="Function">The function description.</param>
public sealed record OpenRouterTool(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] OpenRouterFunction Function);

/// <summary>A callable function.</summary>
/// <param name="Name">Function name.</param>
/// <param name="Description">What it does.</param>
/// <param name="Parameters">JSON Schema for the parameters.</param>
public sealed record OpenRouterFunction(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("parameters")] System.Text.Json.JsonElement Parameters);

/// <summary>A tool invocation requested by the model.</summary>
/// <param name="Id">Call identifier.</param>
/// <param name="Type">Always <c>function</c>.</param>
/// <param name="Function">Which function, and with what arguments.</param>
public sealed record OpenRouterToolCall(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] OpenRouterFunctionCall Function);

/// <summary>The function and arguments of a tool call.</summary>
/// <param name="Name">Function name.</param>
/// <param name="Arguments">Arguments as a JSON string.</param>
public sealed record OpenRouterFunctionCall(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] string Arguments);

/// <summary>Response body for a chat completion.</summary>
public sealed record OpenRouterResponse
{
    /// <summary>Generated alternatives. Only the first is used.</summary>
    [JsonPropertyName("choices")] public IReadOnlyList<OpenRouterChoice> Choices { get; init; } = [];
}

/// <summary>One generated alternative.</summary>
/// <param name="Message">The assistant turn produced.</param>
public sealed record OpenRouterChoice(
    [property: JsonPropertyName("message")] OpenRouterMessage Message);
```

`src/Assistant.Impl/Ai/IOpenRouterApi.cs`:

```csharp
using Refit;

namespace Assistant.Impl.Ai;

/// <summary>OpenRouter's OpenAI-compatible chat completions API.</summary>
public interface IOpenRouterApi
{
    /// <summary>Requests a completion.</summary>
    /// <param name="request">Model, conversation, and tools.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The generated alternatives.</returns>
    [Post("/api/v1/chat/completions")]
    Task<OpenRouterResponse> CreateCompletionAsync(
        [Body] OpenRouterRequest request, CancellationToken ct = default);
}
```

`src/Assistant.Impl/Ai/OpenRouterChatClient.cs`:

```csharp
using System.Text.Json;
using Assistant.Contracts;
using Assistant.Interfaces;
using Microsoft.Extensions.Options;

namespace Assistant.Impl.Ai;

/// <inheritdoc cref="IChatCompletionClient"/>
public sealed class OpenRouterChatClient : IChatCompletionClient
{
    private readonly IOpenRouterApi _api;
    private readonly LlmOptions _options;

    /// <summary>Initialises the client.</summary>
    /// <param name="api">The generated chat completions client.</param>
    /// <param name="options">Model name and token budget.</param>
    public OpenRouterChatClient(IOpenRouterApi api, IOptions<LlmOptions> options)
    {
        _api = api;
        _options = options.Value;
    }

    /// <inheritdoc/>
    public string ProviderName => "openrouter";

    /// <inheritdoc/>
    public async Task<ChatReply> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        var messages = new List<OpenRouterMessage>
        {
            new() { Role = "system", Content = request.SystemPrompt },
        };

        messages.AddRange(request.Turns.Select(t => new OpenRouterMessage
        {
            Role = t.Role,
            Content = t.Content,
            ToolCallId = t.Role == "tool" ? t.ToolCallId : null,
        }));

        var response = await _api.CreateCompletionAsync(
            new OpenRouterRequest
            {
                Model = _options.OpenRouter.Model,
                MaxTokens = _options.MaxTokens,
                Messages = messages,
                Tools = request.Tools.Count == 0 ? null : request.Tools
                    .Select(t => new OpenRouterTool(
                        "function",
                        new OpenRouterFunction(
                            t.Name, t.Description,
                            JsonDocument.Parse(t.ParametersJsonSchema).RootElement.Clone())))
                    .ToList(),
            },
            ct);

        var message = response.Choices.FirstOrDefault()?.Message;

        var calls = (message?.ToolCalls ?? [])
            .Select(c => new ChatToolCall(c.Id, c.Function.Name, c.Function.Arguments))
            .ToList();

        return new ChatReply(
            string.IsNullOrWhiteSpace(message?.Content) ? null : message!.Content,
            calls);
    }
}
```

- [ ] **Step 7: Write the fallback decorator**

`src/Assistant.Impl/Ai/FallbackChatClient.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Interfaces;
using Microsoft.Extensions.Logging;

namespace Assistant.Impl.Ai;

/// <summary>Tries a primary provider and falls through to a secondary when it fails.</summary>
/// <remarks>
/// Neither underlying client is aware of the other, so changing providers is a registration change
/// and touches no calling code.
/// </remarks>
public sealed class FallbackChatClient : IChatCompletionClient
{
    private readonly IChatCompletionClient _primary;
    private readonly IChatCompletionClient? _secondary;
    private readonly ILogger<FallbackChatClient> _log;

    /// <summary>Initialises the decorator.</summary>
    /// <param name="primary">Provider tried first.</param>
    /// <param name="secondary">
    /// Provider tried when the primary fails, or <see langword="null"/> when none is configured.
    /// </param>
    /// <param name="log">Log sink.</param>
    public FallbackChatClient(
        IChatCompletionClient primary,
        IChatCompletionClient? secondary,
        ILogger<FallbackChatClient> log)
    {
        _primary = primary;
        _secondary = secondary;
        _log = log;
    }

    /// <inheritdoc/>
    public string ProviderName => "fallback";

    /// <inheritdoc/>
    public async Task<ChatReply> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        try
        {
            return await _primary.CompleteAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (_secondary is null)
            {
                _log.LogError(ex, "Provider {Provider} failed and no fallback is configured",
                    _primary.ProviderName);
                throw;
            }

            _log.LogWarning(ex, "Provider {Primary} failed; trying {Secondary}",
                _primary.ProviderName, _secondary.ProviderName);

            return await _secondary.CompleteAsync(request, ct);
        }
    }
}
```

- [ ] **Step 8: Write the stub and the failing tests**

`tests/Assistant.IntegrationTests/Infrastructure/AnthropicStub.cs`:

```csharp
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>A local stand-in for a language model provider.</summary>
/// <remarks>
/// Lets the whole capture flow be tested deterministically and for free: the test decides exactly
/// which tool the model "chooses" and with what arguments.
/// </remarks>
public sealed class AnthropicStub : IDisposable
{
    private readonly WireMockServer _server;

    public AnthropicStub()
    {
        _server = WireMockServer.Start();
        RespondWithText("Noted.");
    }

    /// <summary>Base address to configure the API client with.</summary>
    public string BaseUrl => _server.Url!;

    /// <summary>Makes the next completions return prose.</summary>
    public void RespondWithText(string text)
    {
        var body = JsonSerializer.Serialize(new
        {
            content = new[] { new { type = "text", text } },
            stop_reason = "end_turn",
        });

        Stub(200, body);
    }

    /// <summary>Makes the next completions request a tool call.</summary>
    /// <param name="toolName">Which tool the model asks for.</param>
    /// <param name="argumentsJson">The arguments it supplies, as a JSON object.</param>
    public void RespondWithToolCall(string toolName, string argumentsJson)
    {
        var body = JsonSerializer.Serialize(new
        {
            content = new object[]
            {
                new
                {
                    type = "tool_use",
                    id = "toolu_test_1",
                    name = toolName,
                    input = JsonDocument.Parse(argumentsJson).RootElement,
                },
            },
            stop_reason = "tool_use",
        });

        Stub(200, body);
    }

    /// <summary>Makes the provider fail.</summary>
    /// <param name="status">HTTP status to return.</param>
    public void RespondWithError(int status)
        => Stub(status, """{"type":"error","error":{"type":"overloaded_error","message":"nope"}}""");

    /// <summary>How many completions have been requested.</summary>
    public int RequestCount() => _server.LogEntries.Count(
        e => e.RequestMessage.Path.EndsWith("/v1/messages", StringComparison.Ordinal));

    /// <summary>Forgets every recorded request and restores the default response.</summary>
    public void Reset()
    {
        _server.ResetLogEntries();
        RespondWithText("Noted.");
    }

    public void Dispose() => _server.Dispose();

    private void Stub(int status, string body)
    {
        _server.Reset();
        _server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(status)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));
    }
}
```

`tests/Assistant.IntegrationTests/Ai/ChatClientTests.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Impl.Ai;
using Assistant.Interfaces;
using Assistant.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;
using Shouldly;
using Xunit;

namespace Assistant.IntegrationTests.Ai;

public sealed class ChatClientTests : IDisposable
{
    private readonly AnthropicStub _primary = new();
    private readonly AnthropicStub _secondary = new();

    public void Dispose()
    {
        _primary.Dispose();
        _secondary.Dispose();
    }

    private static ChatRequest Request() => new(
        "You are a reminder assistant.",
        [new ChatTurn("user", "call the bank tomorrow at 10")],
        [new ChatToolDefinition("create_task", "Create a task",
            """{"type":"object","properties":{"title":{"type":"string"}},"required":["title"]}""")]);

    private static IAnthropicApi ApiFor(string baseUrl)
    {
        var services = new ServiceCollection();
        services.AddRefitClient<IAnthropicApi>()
            .ConfigureHttpClient(c => c.BaseAddress = new Uri(baseUrl));
        return services.BuildServiceProvider().GetRequiredService<IAnthropicApi>();
    }

    private static AnthropicChatClient ClientFor(string baseUrl) => new(
        ApiFor(baseUrl),
        Options.Create(new LlmOptions { MaxTokens = 512 }));

    [Fact]
    public async Task Returns_prose_when_the_model_answers_directly()
    {
        _primary.RespondWithText("You have nothing due today.");

        var reply = await ClientFor(_primary.BaseUrl).CompleteAsync(Request(), default);

        reply.Text.ShouldBe("You have nothing due today.");
        reply.WantsTools.ShouldBeFalse();
    }

    [Fact]
    public async Task Surfaces_a_tool_call_with_its_arguments_intact()
    {
        _primary.RespondWithToolCall(
            "create_task",
            """{"title":"Call the bank","due_at_local":"2026-08-17T10:00:00"}""");

        var reply = await ClientFor(_primary.BaseUrl).CompleteAsync(Request(), default);

        reply.WantsTools.ShouldBeTrue();
        reply.ToolCalls.Count.ShouldBe(1);
        reply.ToolCalls[0].Name.ShouldBe("create_task");
        reply.ToolCalls[0].ArgumentsJson.ShouldContain("Call the bank");
        reply.ToolCalls[0].ArgumentsJson.ShouldContain("2026-08-17T10:00:00");
    }

    [Fact]
    public async Task Throws_when_the_provider_rejects_the_request()
    {
        _primary.RespondWithError(529);

        await Should.ThrowAsync<ApiException>(
            () => ClientFor(_primary.BaseUrl).CompleteAsync(Request(), default));
    }

    [Fact]
    public async Task Fallback_uses_the_secondary_provider_when_the_primary_fails()
    {
        _primary.RespondWithError(500);
        _secondary.RespondWithText("Answered by the fallback.");

        var fallback = new FallbackChatClient(
            ClientFor(_primary.BaseUrl),
            ClientFor(_secondary.BaseUrl),
            LoggerFactory.Create(b => { }).CreateLogger<FallbackChatClient>());

        var reply = await fallback.CompleteAsync(Request(), default);

        reply.Text.ShouldBe("Answered by the fallback.");
        _primary.RequestCount().ShouldBe(1, "the primary must be tried first");
        _secondary.RequestCount().ShouldBe(1);
    }

    [Fact]
    public async Task Fallback_does_not_call_the_secondary_when_the_primary_succeeds()
    {
        _primary.RespondWithText("Answered by the primary.");
        _secondary.RespondWithText("Should not be reached.");

        var fallback = new FallbackChatClient(
            ClientFor(_primary.BaseUrl),
            ClientFor(_secondary.BaseUrl),
            LoggerFactory.Create(b => { }).CreateLogger<FallbackChatClient>());

        var reply = await fallback.CompleteAsync(Request(), default);

        reply.Text.ShouldBe("Answered by the primary.");
        _secondary.RequestCount().ShouldBe(0, "the fallback costs money and must stay unused");
    }

    [Fact]
    public async Task Fallback_rethrows_when_both_providers_fail()
    {
        _primary.RespondWithError(500);
        _secondary.RespondWithError(500);

        var fallback = new FallbackChatClient(
            ClientFor(_primary.BaseUrl),
            ClientFor(_secondary.BaseUrl),
            LoggerFactory.Create(b => { }).CreateLogger<FallbackChatClient>());

        await Should.ThrowAsync<ApiException>(() => fallback.CompleteAsync(Request(), default));
    }
}
```

- [ ] **Step 9: Run to verify the tests pass**

Run: `dotnet test tests/Assistant.IntegrationTests --filter ChatClientTests`
Expected: PASS, 6 tests.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: Refit language model clients with provider fallback"
```

---

## Task 11: Tools, the agent loop, and message capture

Spec §5.1, §5.3, §5.6. Whitelist first, tools second, and a raw-capture safety net so a provider outage never loses a thought.

**Files:**
- Create: `src/Assistant.Impl/Tools/{CreateTaskTool,ListTasksTool,UpdateTaskTool,CompleteTaskTool,ToolArguments}.cs`
- Create: `src/Assistant.Impl/Services/{AgentService,MessageHandler}.cs`
- Create: `src/Assistant.Impl/Telegram/TelegramListener.cs`
- Create: `tests/Assistant.IntegrationTests/Capture/CaptureFlowTests.cs`

**Interfaces:**
- Consumes: `IAssistantTool`, `IAgent`, `IMessageHandler`, `IChatCompletionClient`, `ITaskService`, `IChatMessageRepository`, `IPendingEditStore`, `INotifier`.
- Produces: `AgentService : IAgent`, `MessageHandler : IMessageHandler`, `TelegramListener : BackgroundService`, and four `IAssistantTool` implementations named `create_task`, `list_tasks`, `update_task`, `complete_task`.

- [ ] **Step 1: Write the tool argument types**

`src/Assistant.Impl/Tools/ToolArguments.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Assistant.Impl.Tools;

/// <summary>Arguments the model supplies when creating a task.</summary>
public sealed record CreateTaskArguments
{
    /// <summary>Short description of what needs doing.</summary>
    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;

    /// <summary>Absolute local ISO-8601 datetime with no offset, or omitted for no deadline.</summary>
    [JsonPropertyName("due_at_local")] public string? DueAtLocal { get; init; }

    /// <summary>Longer detail.</summary>
    [JsonPropertyName("notes")] public string? Notes { get; init; }

    /// <summary>Whether the task is raised in importance.</summary>
    [JsonPropertyName("high_priority")] public bool HighPriority { get; init; }
}

/// <summary>Arguments the model supplies when listing tasks.</summary>
public sealed record ListTasksArguments
{
    /// <summary>Which tasks to return: <c>today</c>, <c>overdue</c>, <c>week</c>, or <c>all</c>.</summary>
    [JsonPropertyName("filter")] public string Filter { get; init; } = "today";

    /// <summary>Maximum number to return.</summary>
    [JsonPropertyName("limit")] public int Limit { get; init; } = 20;
}

/// <summary>Arguments the model supplies when changing a task.</summary>
public sealed record UpdateTaskArguments
{
    /// <summary>Identifier of the task to change.</summary>
    [JsonPropertyName("task_id")] public string TaskId { get; init; } = string.Empty;

    /// <summary>New description, or omitted to keep the current one.</summary>
    [JsonPropertyName("title")] public string? Title { get; init; }

    /// <summary>New absolute local ISO-8601 datetime, or omitted to keep the current time.</summary>
    [JsonPropertyName("due_at_local")] public string? DueAtLocal { get; init; }

    /// <summary>New detail, or omitted to keep the current text.</summary>
    [JsonPropertyName("notes")] public string? Notes { get; init; }
}

/// <summary>Arguments the model supplies when completing a task.</summary>
public sealed record CompleteTaskArguments
{
    /// <summary>Identifier of the task to complete.</summary>
    [JsonPropertyName("task_id")] public string TaskId { get; init; } = string.Empty;
}
```

- [ ] **Step 2: Write the four tools**

`src/Assistant.Impl/Tools/CreateTaskTool.cs`:

```csharp
using System.Text.Json;
using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Tools;

/// <summary>Creates a task from the model's interpretation of a message.</summary>
public sealed class CreateTaskTool : IAssistantTool
{
    private readonly ITaskService _tasks;

    /// <summary>Initialises the tool.</summary>
    /// <param name="tasks">The single writer.</param>
    public CreateTaskTool(ITaskService tasks) => _tasks = tasks;

    /// <inheritdoc/>
    public string Name => "create_task";

    /// <inheritdoc/>
    public string Description =>
        "Create a task the user wants to be reminded about. Use this whenever the user mentions "
        + "something they need to do. Supply due_at_local whenever a time is stated or implied.";

    /// <inheritdoc/>
    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "Short description of what needs doing." },
            "due_at_local": {
              "type": "string",
              "description": "Absolute local datetime, ISO-8601 with no offset, e.g. 2026-08-17T10:00:00. Omit if the user gave no time."
            },
            "notes": { "type": "string", "description": "Extra detail, if any." },
            "high_priority": { "type": "boolean", "description": "True if the user signalled urgency." }
          },
          "required": ["title"]
        }
        """;

    /// <summary>The identifier of the task created by the most recent invocation.</summary>
    /// <value><see langword="null"/> until a task has been created successfully.</value>
    public Guid? LastCreatedTaskId { get; private set; }

    /// <inheritdoc/>
    public async Task<string> InvokeAsync(string argumentsJson, CancellationToken ct)
    {
        var args = JsonSerializer.Deserialize<CreateTaskArguments>(argumentsJson);
        if (args is null || string.IsNullOrWhiteSpace(args.Title))
        {
            return "Rejected: a title is required.";
        }

        var (result, task) = await _tasks.CreateAsync(
            new CreateTaskRequest(args.Title, args.DueAtLocal, args.Notes, args.HighPriority), ct);

        if (!result.Succeeded)
        {
            // Returned to the model rather than thrown, so it can ask a follow-up question
            // instead of the turn failing silently.
            return $"Rejected: {result.Message}";
        }

        LastCreatedTaskId = task!.Id;
        return task.DueAt is null
            ? $"Created task {task.Id} with no due time."
            : $"Created task {task.Id}, due {task.DueAt:yyyy-MM-dd HH:mm} UTC.";
    }
}
```

`src/Assistant.Impl/Tools/ListTasksTool.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Tools;

/// <summary>Lists the user's outstanding tasks.</summary>
public sealed class ListTasksTool : IAssistantTool
{
    private readonly ITaskService _tasks;
    private readonly ILocalTimeResolver _time;

    /// <summary>Initialises the tool.</summary>
    /// <param name="tasks">Query access to tasks.</param>
    /// <param name="time">Renders due times in local time for the model to relay.</param>
    public ListTasksTool(ITaskService tasks, ILocalTimeResolver time)
    {
        _tasks = tasks;
        _time = time;
    }

    /// <inheritdoc/>
    public string Name => "list_tasks";

    /// <inheritdoc/>
    public string Description =>
        "List the user's outstanding tasks. Use this when they ask what is due, what is on today, "
        + "or what they have forgotten.";

    /// <inheritdoc/>
    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "filter": {
              "type": "string",
              "enum": ["today", "overdue", "week", "all"],
              "description": "Which tasks to return."
            },
            "limit": { "type": "integer", "description": "Maximum number to return." }
          },
          "required": ["filter"]
        }
        """;

    /// <inheritdoc/>
    public async Task<string> InvokeAsync(string argumentsJson, CancellationToken ct)
    {
        var args = JsonSerializer.Deserialize<ListTasksArguments>(argumentsJson)
                   ?? new ListTasksArguments();

        var filter = args.Filter?.ToLowerInvariant() switch
        {
            "overdue" => TaskFilter.Overdue,
            "week" => TaskFilter.Week,
            "all" => TaskFilter.All,
            _ => TaskFilter.Today,
        };

        var tasks = await _tasks.QueryAsync(new ListTasksRequest(filter, args.Limit), ct);

        if (tasks.Count == 0)
        {
            return "No matching tasks.";
        }

        var body = new StringBuilder();
        foreach (var task in tasks)
        {
            body.Append(task.Id).Append(" | ").Append(task.Title);
            if (task.DueAt is { } due)
            {
                body.Append(" | due ").Append(_time.ToLocal(due).ToString("yyyy-MM-dd HH:mm"));
            }
            body.AppendLine();
        }

        return body.ToString().TrimEnd();
    }
}
```

`src/Assistant.Impl/Tools/UpdateTaskTool.cs`:

```csharp
using System.Text.Json;
using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Tools;

/// <summary>Changes an existing task.</summary>
public sealed class UpdateTaskTool : IAssistantTool
{
    private readonly ITaskService _tasks;

    /// <summary>Initialises the tool.</summary>
    /// <param name="tasks">The single writer.</param>
    public UpdateTaskTool(ITaskService tasks) => _tasks = tasks;

    /// <inheritdoc/>
    public string Name => "update_task";

    /// <inheritdoc/>
    public string Description =>
        "Change an existing task's title, notes, or due time. Use list_tasks first if you do not "
        + "already know the task's identifier.";

    /// <inheritdoc/>
    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "task_id": { "type": "string", "description": "Identifier of the task to change." },
            "title": { "type": "string", "description": "New description." },
            "due_at_local": {
              "type": "string",
              "description": "New absolute local datetime, ISO-8601 with no offset."
            },
            "notes": { "type": "string", "description": "New detail." }
          },
          "required": ["task_id"]
        }
        """;

    /// <inheritdoc/>
    public async Task<string> InvokeAsync(string argumentsJson, CancellationToken ct)
    {
        var args = JsonSerializer.Deserialize<UpdateTaskArguments>(argumentsJson);
        if (args is null || !Guid.TryParse(args.TaskId, out var id))
        {
            return "Rejected: a valid task_id is required.";
        }

        var result = await _tasks.UpdateAsync(
            new UpdateTaskRequest(id, args.Title, args.DueAtLocal, args.Notes), ct);

        return result.Succeeded ? "Updated." : $"Rejected: {result.Message}";
    }
}
```

`src/Assistant.Impl/Tools/CompleteTaskTool.cs`:

```csharp
using System.Text.Json;
using Assistant.Interfaces;

namespace Assistant.Impl.Tools;

/// <summary>Marks a task complete.</summary>
public sealed class CompleteTaskTool : IAssistantTool
{
    private readonly ITaskService _tasks;

    /// <summary>Initialises the tool.</summary>
    /// <param name="tasks">The single writer.</param>
    public CompleteTaskTool(ITaskService tasks) => _tasks = tasks;

    /// <inheritdoc/>
    public string Name => "complete_task";

    /// <inheritdoc/>
    public string Description =>
        "Mark a task as done. Use list_tasks first if you do not already know its identifier.";

    /// <inheritdoc/>
    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "task_id": { "type": "string", "description": "Identifier of the task to complete." }
          },
          "required": ["task_id"]
        }
        """;

    /// <inheritdoc/>
    public async Task<string> InvokeAsync(string argumentsJson, CancellationToken ct)
    {
        var args = JsonSerializer.Deserialize<CompleteTaskArguments>(argumentsJson);
        if (args is null || !Guid.TryParse(args.TaskId, out var id))
        {
            return "Rejected: a valid task_id is required.";
        }

        var result = await _tasks.CompleteAsync(id, ct);
        return result.Succeeded ? "Completed." : $"Rejected: {result.Message}";
    }
}
```

- [ ] **Step 3: Write the agent loop**

`src/Assistant.Impl/Services/AgentService.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Impl.Ai;
using Assistant.Impl.Tools;
using Assistant.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Impl.Services;

/// <inheritdoc cref="IAgent"/>
public sealed class AgentService : IAgent
{
    private const string Instructions =
        "You are a personal reminder assistant reachable over a chat app. "
        + "When the user mentions something they need to do, create a task for it. "
        + "When they ask what is due, list their tasks. "
        + "Reply in one or two short sentences — never bullet lists, never headings. "
        + "If a tool rejects your request, tell the user plainly and ask what they meant.";

    private readonly IChatCompletionClient _model;
    private readonly IChatMessageRepository _history;
    private readonly ILocalTimeResolver _time;
    private readonly IReadOnlyDictionary<string, IAssistantTool> _tools;
    private readonly LlmOptions _options;
    private readonly ILogger<AgentService> _log;

    /// <summary>Initialises the agent.</summary>
    /// <param name="model">Language model, already wrapped in provider fallback.</param>
    /// <param name="history">Conversation window, so follow-up messages resolve.</param>
    /// <param name="time">Supplies the current local time for the prompt.</param>
    /// <param name="tools">Every capability the model may invoke.</param>
    /// <param name="options">Tool-round limit and related settings.</param>
    /// <param name="log">Log sink.</param>
    public AgentService(
        IChatCompletionClient model,
        IChatMessageRepository history,
        ILocalTimeResolver time,
        IEnumerable<IAssistantTool> tools,
        IOptions<LlmOptions> options,
        ILogger<AgentService> log)
    {
        _model = model;
        _history = history;
        _time = time;
        _tools = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        _options = options.Value;
        _log = log;
    }

    /// <inheritdoc/>
    public async Task<(Result Result, string ReplyText, Guid? CreatedTaskId)> RunAsync(
        string userText, CancellationToken ct)
    {
        var recent = await _history.GetRecentAsync(20, ct);

        var turns = recent
            .Select(m => new ChatTurn(m.Role == "assistant" ? "assistant" : "user", m.Content))
            .ToList();

        turns.Add(new ChatTurn("user", userText));

        var definitions = _tools.Values
            .Select(t => new ChatToolDefinition(t.Name, t.Description, t.ParametersJsonSchema))
            .ToList();

        // Injecting the local time is the single most load-bearing line in the prompt: without it
        // the model has no basis for resolving a phrase like "tomorrow" and will guess.
        var systemPrompt = $"{_time.DescribeNowForPrompt()}\n\n{Instructions}";

        for (var round = 0; round < _options.MaxToolRounds; round++)
        {
            ChatReply reply;
            try
            {
                reply = await _model.CompleteAsync(new ChatRequest(systemPrompt, turns, definitions), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Every language model provider failed");
                return (
                    Result.Failure(ErrorCode.LlmUnavailable, "The model is unreachable."),
                    string.Empty,
                    null);
            }

            if (!reply.WantsTools)
            {
                return (Result.Success(), reply.Text ?? "Done.", CreatedTaskId());
            }

            foreach (var call in reply.ToolCalls)
            {
                var output = _tools.TryGetValue(call.Name, out var tool)
                    ? await tool.InvokeAsync(call.ArgumentsJson, ct)
                    : $"Rejected: no tool named {call.Name}.";

                turns.Add(new ChatTurn("assistant", $"Calling {call.Name}."));
                turns.Add(new ChatTurn("tool", output, call.Id));
            }
        }

        _log.LogWarning("Tool loop hit the {Limit}-round limit", _options.MaxToolRounds);
        return (Result.Success(), "I got a bit stuck on that one — could you rephrase it?", CreatedTaskId());
    }

    /// <summary>The task created during this turn, if any.</summary>
    /// <remarks>
    /// Read from the create tool rather than parsed out of the model's prose, so the caller can
    /// attach action buttons to a task the model did in fact create.
    /// </remarks>
    private Guid? CreatedTaskId() =>
        _tools.TryGetValue("create_task", out var tool) && tool is CreateTaskTool create
            ? create.LastCreatedTaskId
            : null;
}
```

- [ ] **Step 4: Write the message handler**

`src/Assistant.Impl/Services/MessageHandler.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Impl.Telegram;
using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Impl.Services;

/// <inheritdoc cref="IMessageHandler"/>
public sealed class MessageHandler : IMessageHandler
{
    private readonly IAgent _agent;
    private readonly ITaskService _tasks;
    private readonly IChatMessageRepository _history;
    private readonly INotifier _notifier;
    private readonly IPendingEditStore _pendingEdit;
    private readonly IClock _clock;
    private readonly TelegramOptions _telegram;
    private readonly ILogger<MessageHandler> _log;

    /// <summary>Initialises the handler.</summary>
    /// <param name="agent">Runs the model tool loop.</param>
    /// <param name="tasks">The single writer, used for the raw-capture fallback.</param>
    /// <param name="history">Conversation window.</param>
    /// <param name="notifier">Outbound message channel.</param>
    /// <param name="pendingEdit">Whether the message is a follow-up to an edit button.</param>
    /// <param name="clock">Source of the current time.</param>
    /// <param name="telegram">Settings carrying the owner's identifier.</param>
    /// <param name="log">Log sink.</param>
    public MessageHandler(
        IAgent agent,
        ITaskService tasks,
        IChatMessageRepository history,
        INotifier notifier,
        IPendingEditStore pendingEdit,
        IClock clock,
        IOptions<TelegramOptions> telegram,
        ILogger<MessageHandler> log)
    {
        _agent = agent;
        _tasks = tasks;
        _history = history;
        _notifier = notifier;
        _pendingEdit = pendingEdit;
        _clock = clock;
        _telegram = telegram.Value;
        _log = log;
    }

    /// <inheritdoc/>
    public async Task HandleAsync(long senderUserId, string text, CancellationToken ct)
    {
        // Before anything that costs money: an unknown sender must be free to ignore.
        if (senderUserId != _telegram.OwnerUserId)
        {
            _log.LogWarning("Discarded message from {UserId}", senderUserId);
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (text.Trim() == "/status")
        {
            await SendStatusAsync(ct);
            return;
        }

        var effectiveText = _pendingEdit.Take() is { } editingTaskId
            ? $"Apply this change to task {editingTaskId}: {text}"
            : text;

        await AppendAsync("user", text, ct);

        var (result, reply, createdTaskId) = await _agent.RunAsync(effectiveText, ct);

        if (!result.Succeeded)
        {
            await CaptureRawAsync(text, ct);
            return;
        }

        await AppendAsync("assistant", reply, ct);
        await _notifier.SendTextAsync(TelegramMessageFormatter.Escape(reply), ct);

        _log.LogInformation("Handled message; created task {TaskId}", createdTaskId);
    }

    /// <summary>
    /// Stores a message verbatim when the model could not be reached.
    /// </summary>
    /// <remarks>
    /// The product exists because things get forgotten, so dropping input on a provider outage is
    /// the one unacceptable failure. The thought is kept even though it could not be parsed.
    /// </remarks>
    private async Task CaptureRawAsync(string text, CancellationToken ct)
    {
        var (created, _) = await _tasks.CreateAsync(new CreateTaskRequest(text), ct);

        await _notifier.SendTextAsync(
            created.Succeeded
                ? "I could not reach the model just now, so I have saved that as-is. "
                  + "Ask me to tidy it up later."
                : "I could not reach the model and could not save that either — please resend it.",
            ct);
    }

    private async Task SendStatusAsync(CancellationToken ct)
    {
        var pending = await _tasks.QueryAsync(new ListTasksRequest(TaskFilter.All, 100), ct);
        var overdue = pending.Count(t => t.DueAt is { } d && d <= _clock.UtcNow);

        await _notifier.SendTextAsync(
            $"Alive. {pending.Count} open, {overdue} overdue.", ct);
    }

    private Task AppendAsync(string role, string content, CancellationToken ct)
        => _history.AppendAsync(
            new ChatMessage
            {
                Id = Guid.NewGuid(),
                Role = role,
                Content = content,
                CreatedAt = _clock.UtcNow,
            },
            ct);
}
```

- [ ] **Step 5: Write the listener**

`src/Assistant.Impl/Telegram/TelegramListener.cs`:

```csharp
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>Receives updates from Telegram by long polling.</summary>
/// <remarks>
/// Long polling rather than a webhook: no public domain, no certificate, no inbound firewall
/// rule, and the connection re-establishes itself after a network interruption. Messages are
/// handled one at a time, which for a single user avoids interleaved model turns corrupting the
/// conversation window.
/// </remarks>
public sealed class TelegramListener : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<TelegramListener> _log;

    /// <summary>Initialises the listener.</summary>
    /// <param name="bot">Configured Bot API client.</param>
    /// <param name="scopes">Factory used to resolve a handler per update.</param>
    /// <param name="log">Log sink.</param>
    public TelegramListener(
        ITelegramBotClient bot, IServiceScopeFactory scopes, ILogger<TelegramListener> log)
    {
        _bot = bot;
        _scopes = scopes;
        _log = log;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var offset = 0;
        _log.LogInformation("Telegram listener started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await _bot.GetUpdates(
                    offset: offset,
                    timeout: 50,
                    allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery],
                    cancellationToken: stoppingToken);

                foreach (var update in updates)
                {
                    offset = update.Id + 1;
                    await DispatchAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A transient network fault must not end the loop; back off briefly and resume.
                _log.LogError(ex, "Polling failed; retrying shortly");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task DispatchAsync(Update update, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();

        try
        {
            if (update.Message is { Text: { } text, From: { } sender })
            {
                await scope.ServiceProvider.GetRequiredService<IMessageHandler>()
                    .HandleAsync(sender.Id, text, ct);
            }
            else if (update.CallbackQuery is { Data: { } data, From: { } presser, Message: { } message })
            {
                await scope.ServiceProvider.GetRequiredService<ICallbackHandler>()
                    .HandleAsync(presser.Id, update.CallbackQuery.Id, message.MessageId, data, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One bad update must not stop the bot receiving the next one.
            _log.LogError(ex, "Failed to handle update {UpdateId}", update.Id);
        }
    }
}
```

- [ ] **Step 6: Write the failing capture tests**

`tests/Assistant.IntegrationTests/Capture/CaptureFlowTests.cs`:

```csharp
using System.Globalization;
using Assistant.Contracts;
using Assistant.Impl.Ai;
using Assistant.Impl.Services;
using Assistant.Impl.Telegram;
using Assistant.Impl.Tools;
using Assistant.Interfaces;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Refit;
using Shouldly;
using Telegram.Bot;
using Xunit;

namespace Assistant.IntegrationTests.Capture;

[Collection(PostgresCollection.Name)]
public sealed class CaptureFlowTests : IAsyncLifetime, IDisposable
{
    private const long OwnerId = 4242;
    private const long StrangerId = 9999;

    private readonly PostgresFixture _postgres;
    private readonly TelegramStub _telegram = new();
    private readonly AnthropicStub _model = new();
    private readonly FakeClock _clock = new();
    private ServiceProvider _provider = null!;

    public CaptureFlowTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        await _postgres.ResetAsync();
        _clock.Set("2026-08-16T20:00:00Z");   // Sunday 23:00 local

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssistantRepository(_postgres.ConnectionString);
        services.AddSingleton<IClock>(_clock);
        services.AddSingleton<ILocalTimeResolver, LocalTimeResolver>();
        services.AddSingleton<IPendingEditStore, PendingEditStore>();
        services.Configure<LlmOptions>(o =>
        {
            o.Anthropic.BaseUrl = _model.BaseUrl;
            o.Anthropic.Model = "test-model";
        });
        services.Configure<TelegramOptions>(o =>
        {
            o.BotToken = "test-token";
            o.OwnerUserId = OwnerId;
            o.BaseUrl = _telegram.BaseUrl;
        });
        services.AddSingleton<ITelegramBotClient>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
            return new TelegramBotClient(new TelegramBotClientOptions(o.BotToken, o.BaseUrl));
        });
        services.AddRefitClient<IAnthropicApi>()
            .ConfigureHttpClient(c => c.BaseAddress = new Uri(_model.BaseUrl));

        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<INotifier, TelegramNotifier>();
        services.AddScoped<IAssistantTool, CreateTaskTool>();
        services.AddScoped<IAssistantTool, ListTasksTool>();
        services.AddScoped<IAssistantTool, UpdateTaskTool>();
        services.AddScoped<IAssistantTool, CompleteTaskTool>();
        services.AddScoped<IChatCompletionClient, AnthropicChatClient>();
        services.AddScoped<IAgent, AgentService>();
        services.AddScoped<IMessageHandler, MessageHandler>();

        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    public void Dispose()
    {
        _telegram.Dispose();
        _model.Dispose();
    }

    private async Task SendAsync(string text, long sender = OwnerId)
    {
        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMessageHandler>()
            .HandleAsync(sender, text, default);
    }

    private async Task<IReadOnlyList<Assistant.Models.ReminderTask>> AllTasksAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITaskRepository>()
            .QueryAsync(TaskFilter.All, _clock.UtcNow, 100, default);
    }

    [Fact]
    public async Task A_message_from_a_stranger_costs_nothing_and_stores_nothing()
    {
        await SendAsync("call the bank tomorrow at 10", sender: StrangerId);

        _model.RequestCount().ShouldBe(0, "the whitelist must run before anything billable");
        _telegram.SentMessages().ShouldBeEmpty();
        (await AllTasksAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_tool_call_creates_the_task_with_the_due_time_converted_to_utc()
    {
        _model.RespondWithToolCall(
            "create_task",
            """{"title":"Call the bank","due_at_local":"2026-08-17T10:00:00"}""");

        await SendAsync("call the bank tomorrow at 10");

        var tasks = await AllTasksAsync();
        tasks.Count.ShouldBe(1);
        tasks[0].Title.ShouldBe("Call the bank");
        tasks[0].DueAt.ShouldBe(DateTimeOffset.Parse(
            "2026-08-17T07:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal));
    }

    [Fact]
    public async Task A_rejected_time_is_reported_to_the_model_rather_than_stored()
    {
        _model.RespondWithToolCall(
            "create_task",
            """{"title":"Time travel","due_at_local":"2020-01-01T10:00:00"}""");

        await SendAsync("remind me last year");

        (await AllTasksAsync()).ShouldBeEmpty();
        _model.RequestCount().ShouldBeGreaterThan(1, "the rejection goes back for a follow-up");
    }

    [Fact]
    public async Task A_direct_answer_is_relayed_to_the_user()
    {
        _model.RespondWithText("You have nothing due today.");

        await SendAsync("what's on today?");

        var sent = _telegram.SentMessages();
        sent.Count.ShouldBe(1);
        sent[0].ChatId.ShouldBe(OwnerId);
        sent[0].Text.ShouldBe("You have nothing due today.");
    }

    [Fact]
    public async Task A_provider_outage_saves_the_message_verbatim_rather_than_losing_it()
    {
        _model.RespondWithError(500);

        await SendAsync("call the bank tomorrow at 10");

        var tasks = await AllTasksAsync();
        tasks.Count.ShouldBe(1, "a captured thought must survive a provider outage");
        tasks[0].Title.ShouldBe("call the bank tomorrow at 10");
        tasks[0].DueAt.ShouldBeNull();

        _telegram.SentMessages()[0].Text.ShouldContain("saved that as-is");
    }

    [Fact]
    public async Task The_conversation_window_records_both_sides_of_the_exchange()
    {
        _model.RespondWithText("Noted.");

        await SendAsync("remember I like tea");

        using var scope = _provider.CreateScope();
        var history = await scope.ServiceProvider.GetRequiredService<IChatMessageRepository>()
            .GetRecentAsync(10, default);

        history.Select(m => m.Role).ShouldBe(new[] { "user", "assistant" });
        history[0].Content.ShouldBe("remember I like tea");
    }

    [Fact]
    public async Task Status_answers_without_calling_the_model()
    {
        await SendAsync("/status");

        _model.RequestCount().ShouldBe(0, "a health check must not cost tokens");
        _telegram.SentMessages()[0].Text.ShouldContain("Alive");
    }
}
```

- [ ] **Step 7: Run to verify the tests pass**

Run: `dotnet test tests/Assistant.IntegrationTests --filter CaptureFlowTests`
Expected: PASS, 7 tests.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: assistant tools, agent loop, and message capture with raw fallback"
```

---

## Task 12: The daily brief

Spec §6.3. One per day, guaranteed by the primary key, and no cutoff — a brief that arrives in the evening is better than a day with none.

**Files:**
- Create: `src/Assistant.Impl/Services/Jobs/DailyBriefJob.cs`
- Create: `tests/Assistant.IntegrationTests/Reminders/DailyBriefJobTests.cs`

**Interfaces:**
- Consumes: `IDailyBriefRepository`, `ITaskService`, `ITaskRepository`, `INotifier`, `ILocalTimeResolver`, `SchedulerOptions.DailyBriefHour`.
- Produces: `DailyBriefJob : ScheduledJobBase`, `Name` = `"daily-brief"`.

- [ ] **Step 1: Write the failing tests**

`tests/Assistant.IntegrationTests/Reminders/DailyBriefJobTests.cs`:

```csharp
using System.Globalization;
using Assistant.Impl.Scheduling;
using Assistant.Impl.Services;
using Assistant.Impl.Services.Jobs;
using Assistant.Impl.Telegram;
using Assistant.Interfaces;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Models;
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Telegram.Bot;
using Xunit;

namespace Assistant.IntegrationTests.Reminders;

[Collection(PostgresCollection.Name)]
public sealed class DailyBriefJobTests : IAsyncLifetime, IDisposable
{
    private const long OwnerId = 4242;

    private readonly PostgresFixture _postgres;
    private readonly TelegramStub _telegram = new();
    private readonly FakeClock _clock = new();
    private ServiceProvider _provider = null!;

    public DailyBriefJobTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        await _postgres.ResetAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssistantRepository(_postgres.ConnectionString);
        services.AddSingleton<IClock>(_clock);
        services.AddSingleton<ILocalTimeResolver, LocalTimeResolver>();
        services.AddScoped<ITaskService, TaskService>();
        services.Configure<SchedulerOptions>(o => o.DailyBriefHour = 7);
        services.Configure<TelegramOptions>(o =>
        {
            o.BotToken = "test-token";
            o.OwnerUserId = OwnerId;
            o.BaseUrl = _telegram.BaseUrl;
        });
        services.AddSingleton<ITelegramBotClient>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
            return new TelegramBotClient(new TelegramBotClientOptions(o.BotToken, o.BaseUrl));
        });
        services.AddScoped<INotifier, TelegramNotifier>();
        services.AddScoped<IScheduledJob, DailyBriefJob>();

        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    public void Dispose() => _telegram.Dispose();

    private static DateTimeOffset Utc(string iso)
        => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private async Task SeedAsync(string title, string dueUtcIso)
    {
        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITaskRepository>().AddAsync(
            new ReminderTask
            {
                Id = Guid.NewGuid(),
                Title = title,
                Status = ReminderStatus.Pending,
                Priority = Priority.Normal,
                DueAt = Utc(dueUtcIso),
                CreatedAt = Utc("2026-08-01T00:00:00Z"),
                UpdatedAt = Utc("2026-08-01T00:00:00Z"),
            },
            default);
    }

    private async Task TickAsync()
    {
        using var scope = _provider.CreateScope();
        foreach (var job in scope.ServiceProvider.GetServices<IScheduledJob>())
        {
            await job.RunAsync(default);
        }
    }

    [Fact]
    public async Task Sends_the_brief_once_the_local_hour_has_arrived()
    {
        await SeedAsync("Call the bank", "2026-08-17T12:00:00Z");
        _clock.Set("2026-08-17T04:00:00Z");   // 07:00 local

        await TickAsync();

        var sent = _telegram.SentMessages();
        sent.Count.ShouldBe(1);
        sent[0].ChatId.ShouldBe(OwnerId);
        sent[0].Text.ShouldContain("Call the bank");
    }

    [Fact]
    public async Task Does_not_send_before_the_local_hour()
    {
        await SeedAsync("Call the bank", "2026-08-17T12:00:00Z");
        _clock.Set("2026-08-17T03:00:00Z");   // 06:00 local

        await TickAsync();

        _telegram.SentMessages().ShouldBeEmpty();
    }

    [Fact]
    public async Task Sends_exactly_one_brief_however_many_times_it_ticks()
    {
        _clock.Set("2026-08-17T04:00:00Z");

        await TickAsync();
        await TickAsync();
        await TickAsync();

        _telegram.SentMessages().Count.ShouldBe(1, "the brief date is a primary key");
    }

    [Fact]
    public async Task Sends_the_brief_late_in_the_evening_when_the_morning_was_missed()
    {
        // The host was down all day and comes up at 19:00 local. A late brief is strictly better
        // than a silent day, so there is deliberately no cutoff.
        await SeedAsync("Call the bank", "2026-08-17T12:00:00Z");
        _clock.Set("2026-08-17T16:00:00Z");

        await TickAsync();

        _telegram.SentMessages().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Sends_a_fresh_brief_the_following_day()
    {
        _clock.Set("2026-08-17T04:00:00Z");
        await TickAsync();

        _clock.Set("2026-08-18T04:00:00Z");
        await TickAsync();

        _telegram.SentMessages().Count.ShouldBe(2);
    }

    [Fact]
    public async Task Separates_overdue_tasks_from_those_due_today()
    {
        await SeedAsync("Yesterday's job", "2026-08-16T12:00:00Z");
        await SeedAsync("Today's job", "2026-08-17T12:00:00Z");
        _clock.Set("2026-08-17T04:00:00Z");

        await TickAsync();

        var text = _telegram.SentMessages()[0].Text;
        text.ShouldContain("Overdue");
        text.ShouldContain("Yesterday's job");
        text.ShouldContain("Today");
        text.ShouldContain("Today's job");
    }

    [Fact]
    public async Task Sends_a_brief_even_when_nothing_is_due()
    {
        _clock.Set("2026-08-17T04:00:00Z");

        await TickAsync();

        _telegram.SentMessages()[0].Text.ShouldContain("Nothing due today");
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Assistant.IntegrationTests --filter DailyBriefJobTests`
Expected: FAIL to compile — `DailyBriefJob` does not exist.

- [ ] **Step 3: Implement the job**

`src/Assistant.Impl/Services/Jobs/DailyBriefJob.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Impl.Mapping;
using Assistant.Impl.Scheduling;
using Assistant.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Impl.Services.Jobs;

/// <summary>Sends one summary of outstanding work per local day.</summary>
/// <remarks>
/// <para>
/// There is deliberately no cutoff. If the process was down all morning, the brief is still sent
/// when it comes back — the tasks in it are still owed, and a silent day is the failure this
/// product exists to prevent.
/// </para>
/// <para>
/// Sending exactly once is guaranteed by the brief date being a primary key, so a restart cannot
/// produce a duplicate and no coordination is required.
/// </para>
/// </remarks>
public sealed class DailyBriefJob : ScheduledJobBase
{
    private readonly IDailyBriefRepository _briefs;
    private readonly ITaskRepository _tasks;
    private readonly INotifier _notifier;
    private readonly ILocalTimeResolver _time;
    private readonly IClock _clock;
    private readonly SchedulerOptions _options;
    private readonly ILogger<DailyBriefJob> _log;

    /// <summary>Initialises the job.</summary>
    /// <param name="briefs">Record of which days have already been briefed.</param>
    /// <param name="tasks">Read access to outstanding tasks.</param>
    /// <param name="notifier">Outbound message channel.</param>
    /// <param name="time">Supplies the local date and renders due times.</param>
    /// <param name="clock">Source of the current time.</param>
    /// <param name="options">The local hour at which the brief becomes due.</param>
    /// <param name="log">Log sink.</param>
    public DailyBriefJob(
        IDailyBriefRepository briefs,
        ITaskRepository tasks,
        INotifier notifier,
        ILocalTimeResolver time,
        IClock clock,
        IOptions<SchedulerOptions> options,
        ILogger<DailyBriefJob> log)
        : base(log)
    {
        _briefs = briefs;
        _tasks = tasks;
        _notifier = notifier;
        _time = time;
        _clock = clock;
        _options = options.Value;
        _log = log;
    }

    /// <inheritdoc/>
    public override string Name => "daily-brief";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var local = _time.ToLocal(now);

        if (local.Hour < _options.DailyBriefHour)
        {
            return;
        }

        var today = _time.LocalToday;

        // Claim before building: if the claim fails, another tick already sent today's brief and
        // there is nothing to do.
        if (!await _briefs.TryClaimAsync(today, now, ct))
        {
            return;
        }

        var overdue = await _tasks.QueryAsync(TaskFilter.Overdue, now, 50, ct);
        var week = await _tasks.QueryAsync(TaskFilter.Today, now, 50, ct);
        var undatedCount = await _tasks.CountOpenWithoutDueDateAsync(ct);

        var overdueIds = overdue.Select(t => t.Id).ToHashSet();
        var dueToday = week.Where(t => !overdueIds.Contains(t.Id)).ToList();

        var brief = new DailyBriefNotification(
            today,
            dueToday.Select(t => t.ToResponse(_time, now)).ToList(),
            overdue.Select(t => t.ToResponse(_time, now)).ToList(),
            undatedCount);

        await _notifier.SendDailyBriefAsync(brief, ct);
        _log.LogInformation("Daily brief sent for {Date}", today);
    }
}
```

- [ ] **Step 4: Run to verify the tests pass**

Run: `dotnet test tests/Assistant.IntegrationTests --filter DailyBriefJobTests`
Expected: PASS, 7 tests.

Note the deliberate consequence of claiming before sending: if the send then fails, that day's brief is lost rather than retried. This is the opposite trade from reminder delivery, and it is correct here — a duplicated brief is far more annoying than a missed one, and every task it would have named is still surfaced by its own reminder.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: daily brief with once-per-day guarantee and no evening cutoff"
```

---

## Task 13: Composition root, containers, and CI

Spec §8, §11.3, §12.4. The last task wires everything together, ships it, and makes every command in `AGENTS.md` one that CI actually runs.

**Files:**
- Create: `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`
- Modify: `src/Assistant.Worker/Program.cs` (replace the template entirely)
- Create: `src/Assistant.Worker/appsettings.json`, `src/Assistant.Worker/Dockerfile`
- Create: `compose.yaml`, `.github/workflows/ci.yml`, `.github/dependabot.yml`
- Modify: `AGENTS.md` (verify every command)

**Interfaces:**
- Consumes: everything.
- Produces: `ImplServiceCollectionExtensions.AddAssistantServices(this IServiceCollection, IConfiguration)`; a runnable container.

- [ ] **Step 1: Add packages**

```bash
dotnet add src/Assistant.Worker package Serilog.AspNetCore
dotnet add src/Assistant.Worker package Serilog.Sinks.Console
dotnet add src/Assistant.Impl package Microsoft.Extensions.Configuration.Abstractions
```

- [ ] **Step 2: Write the `Impl` registration extension**

`src/Assistant.Impl/ImplServiceCollectionExtensions.cs`:

```csharp
using Assistant.Impl.Ai;
using Assistant.Impl.Scheduling;
using Assistant.Impl.Services;
using Assistant.Impl.Services.Actions;
using Assistant.Impl.Services.Jobs;
using Assistant.Impl.Telegram;
using Assistant.Impl.Tools;
using Assistant.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;
using Telegram.Bot;

namespace Assistant.Impl;

/// <summary>Registers the assistant's services, jobs, and adapters.</summary>
/// <remarks>
/// Deliberately does not register persistence: that is the repository assembly's own entry point,
/// and keeping them separate is what allows this assembly to stay free of any database dependency.
/// </remarks>
public static class ImplServiceCollectionExtensions
{
    /// <summary>Adds every implementation this assembly provides.</summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <param name="configuration">Source of the Telegram, model, and scheduler settings.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAssistantServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TelegramOptions>(configuration.GetSection(TelegramOptions.SectionName));
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.Configure<SchedulerOptions>(configuration.GetSection(SchedulerOptions.SectionName));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ILocalTimeResolver, LocalTimeResolver>();
        services.AddSingleton<IPendingEditStore, PendingEditStore>();
        services.AddSingleton<HeartbeatWriter>();

        services.AddSingleton<ITelegramBotClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
            return new TelegramBotClient(new TelegramBotClientOptions(
                options.BotToken,
                string.IsNullOrWhiteSpace(options.BaseUrl) ? null : options.BaseUrl));
        });

        AddModelClients(services);

        services.AddScoped<INotifier, TelegramNotifier>();
        services.AddScoped<IMessageEditor, TelegramMessageEditor>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IAgent, AgentService>();
        services.AddScoped<IMessageHandler, MessageHandler>();
        services.AddScoped<ICallbackHandler, CallbackHandler>();

        services.AddScoped<IAssistantTool, CreateTaskTool>();
        services.AddScoped<IAssistantTool, ListTasksTool>();
        services.AddScoped<IAssistantTool, UpdateTaskTool>();
        services.AddScoped<IAssistantTool, CompleteTaskTool>();

        services.AddScoped<ITaskAction, DoneAction>();
        services.AddScoped<ITaskAction, SnoozeAction>();
        services.AddScoped<ITaskAction, RescheduleAction>();
        services.AddScoped<ITaskAction, EditAction>();

        services.AddScoped<IScheduledJob, DueReminderJob>();
        services.AddScoped<IScheduledJob, DailyBriefJob>();

        services.AddHostedService<TelegramListener>();
        services.AddHostedService<ReminderScheduler>();

        return services;
    }

    /// <summary>
    /// Registers the Refit clients and wraps them so the primary provider is tried first.
    /// </summary>
    private static void AddModelClients(IServiceCollection services)
    {
        services.AddRefitClient<IAnthropicApi>()
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
                client.BaseAddress = new Uri(options.Anthropic.BaseUrl);
                client.DefaultRequestHeaders.Add("x-api-key", options.Anthropic.ApiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            })
            .AddStandardResilienceHandler();

        services.AddRefitClient<IOpenRouterApi>()
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
                client.BaseAddress = new Uri(options.OpenRouter.BaseUrl);
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", options.OpenRouter.ApiKey);
            })
            .AddStandardResilienceHandler();

        services.AddScoped<AnthropicChatClient>();
        services.AddScoped<OpenRouterChatClient>();

        services.AddScoped<IChatCompletionClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;

            // The fallback is only wired in when a key exists for it, so a single-provider setup
            // fails loudly on the primary rather than quietly reaching for something unconfigured.
            var secondary = string.IsNullOrWhiteSpace(options.OpenRouter.ApiKey)
                ? null
                : sp.GetRequiredService<OpenRouterChatClient>();

            return new FallbackChatClient(
                sp.GetRequiredService<AnthropicChatClient>(),
                secondary,
                sp.GetRequiredService<ILogger<FallbackChatClient>>());
        });
    }
}
```

- [ ] **Step 3: Write the composition root**

Replace `src/Assistant.Worker/Program.cs` entirely:

```csharp
using Assistant.Impl;
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(config => config
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console());

var connectionString = builder.Configuration.GetConnectionString("Assistant")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Assistant is not configured. Copy .env.example to .env and fill it in.");

// Persistence registers itself: this is the only place the repository assembly is named, which is
// what keeps every service free of a database dependency.
builder.Services.AddAssistantRepository(connectionString);
builder.Services.AddAssistantServices(builder.Configuration);

var host = builder.Build();

await host.Services.MigrateAssistantDatabaseAsync();

await host.RunAsync();
```

- [ ] **Step 4: Write `appsettings.json`**

`src/Assistant.Worker/appsettings.json`:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
        "System.Net.Http.HttpClient": "Warning"
      }
    }
  },
  "ConnectionStrings": {
    "Assistant": ""
  },
  "Telegram": {
    "BotToken": "",
    "OwnerUserId": 0,
    "BaseUrl": ""
  },
  "Llm": {
    "Anthropic": {
      "ApiKey": "",
      "Model": "claude-sonnet-4-5",
      "BaseUrl": "https://api.anthropic.com"
    },
    "OpenRouter": {
      "ApiKey": "",
      "Model": "anthropic/claude-sonnet-4.5",
      "BaseUrl": "https://openrouter.ai"
    },
    "MaxTokens": 1024,
    "MaxCallsPerMinute": 20,
    "MaxToolRounds": 5
  },
  "Scheduler": {
    "TickSeconds": 30,
    "BatchSize": 50,
    "OverdueSummaryThresholdHours": 24,
    "DailyBriefHour": 7,
    "HeartbeatPath": "/tmp/heartbeat"
  }
}
```

- [ ] **Step 5: Write the Dockerfile**

`src/Assistant.Worker/Dockerfile`:

```dockerfile
# Build from the repository root: docker build -f src/Assistant.Worker/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so a source-only change does not invalidate the package layer.
COPY Directory.Build.props Directory.Packages.props PersonalAssistant.sln ./
COPY src/ src/
RUN dotnet restore src/Assistant.Worker/Assistant.Worker.csproj

RUN dotnet publish src/Assistant.Worker/Assistant.Worker.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# tzdata is required: resolving "tomorrow at 10" needs a timezone database, and
# TimeZoneInfo.FindSystemTimeZoneById throws without it.
RUN apt-get update \
 && apt-get install -y --no-install-recommends tzdata curl \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# The scheduler touches this file on every successful tick, so a process that is alive but whose
# loop has wedged is still detected.
HEALTHCHECK --interval=60s --timeout=5s --start-period=30s --retries=3 \
    CMD test "$(find /tmp/heartbeat -mmin -2 2>/dev/null)" != "" || exit 1

ENTRYPOINT ["dotnet", "Assistant.Worker.dll"]
```

- [ ] **Step 6: Write `compose.yaml`**

```yaml
services:
  postgres:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: assistant
      POSTGRES_USER: assistant
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in .env}
    volumes:
      - assistant-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U assistant -d assistant"]
      interval: 10s
      timeout: 5s
      retries: 5

  worker:
    build:
      context: .
      dockerfile: src/Assistant.Worker/Dockerfile
    restart: unless-stopped
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      ConnectionStrings__Assistant: >-
        Host=postgres;Database=assistant;Username=assistant;Password=${POSTGRES_PASSWORD}
      TELEGRAM__BOTTOKEN: ${TELEGRAM__BOTTOKEN:?set TELEGRAM__BOTTOKEN in .env}
      TELEGRAM__OWNERUSERID: ${TELEGRAM__OWNERUSERID:?set TELEGRAM__OWNERUSERID in .env}
      LLM__ANTHROPIC__APIKEY: ${LLM__ANTHROPIC__APIKEY:?set LLM__ANTHROPIC__APIKEY in .env}
      LLM__OPENROUTER__APIKEY: ${LLM__OPENROUTER__APIKEY:-}

volumes:
  assistant-data:
```

The `:?` syntax fails the command with a readable message when a variable is missing, rather than starting a container that silently cannot authenticate.

- [ ] **Step 7: Write the CI workflow**

`.github/workflows/ci.yml`:

```yaml
name: ci

on:
  push:
    branches: [main]
  pull_request:

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore --configuration Release

      - name: Unit tests
        run: dotnet test tests/Assistant.UnitTests --no-build --configuration Release --verbosity normal

      - name: Start Postgres
        run: docker compose -f compose.test.yaml up -d --wait

      - name: Integration tests
        run: dotnet test tests/Assistant.IntegrationTests --no-build --configuration Release --verbosity normal

      - name: Stop Postgres
        if: always()
        run: docker compose -f compose.test.yaml down -v

  secret-scan:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - uses: gitleaks/gitleaks-action@v2
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

No secret is configured for either job. That is the point: a pull request from a stranger's fork runs the whole suite, because Telegram and the model providers are stubbed by WireMock and Postgres comes from compose. `--wait` makes compose block until the healthcheck passes, which removes the most common source of a flaky first integration test.

- [ ] **Step 8: Write the Dependabot config**

`.github/dependabot.yml`:

```yaml
version: 2
updates:
  - package-ecosystem: nuget
    directory: "/"
    schedule:
      interval: weekly
    open-pull-requests-limit: 5
  - package-ecosystem: github-actions
    directory: "/"
    schedule:
      interval: monthly
```

- [ ] **Step 9: Verify the whole suite from a clean state**

```bash
docker compose -f compose.test.yaml down -v
dotnet clean && dotnet build
dotnet test tests/Assistant.UnitTests
docker compose -f compose.test.yaml up -d --wait
dotnet test tests/Assistant.IntegrationTests
```

Expected: both suites pass with zero warnings.

- [ ] **Step 10: Verify the container actually starts**

```bash
docker build -f src/Assistant.Worker/Dockerfile -t assistant-worker .
```

Expected: builds successfully.

Then, with a real `.env` in place:

```bash
docker compose up -d
docker compose logs worker | head -40
```

Expected: log lines reading `Telegram listener started` and `Scheduler started; tick every 30s`, and no unhandled exception. Confirm the healthcheck turns healthy:

```bash
sleep 60 && docker compose ps
```

Expected: the worker's state shows `healthy`. If it shows `unhealthy`, the heartbeat file is not being written — check that `Scheduler:HeartbeatPath` is writable inside the container.

- [ ] **Step 11: Walk through `AGENTS.md` and correct anything that has drifted**

Run every command in `AGENTS.md` in order, from a fresh clone, and fix any that fails or that no longer matches reality. Specifically confirm:

- the migration command works with the actual project paths
- the test commands match the actual project names
- the prompt-eval command is either implemented or removed from the file

The eval tool is not built in slice 1, so remove that section from `AGENTS.md` rather than leaving an instruction that does not work. A stale `AGENTS.md` misleads; a missing section merely omits.

- [ ] **Step 12: Commit and push**

```bash
git add -A
git commit -m "feat: composition root, container image, and credential-free CI"
git push
```

Confirm the workflow passes on GitHub before considering the slice complete.

---

## Self-Review

Checked against spec v2.0 with fresh eyes.

### Spec coverage

| Spec section | Covered by |
| :--- | :--- |
| §1.2 capture, reminders, brief, buttons, listing | Tasks 11, 8, 12, 9, 11 |
| §1.4 success criteria 1–6 | Tasks 6/11, 8, 8, 12, 9, 11 |
| §2 decisions table | Tasks 1 (framework, warnings), 4 (timezone), 10 (Refit), 13 (hosting) |
| §3.1 projects | Task 1 |
| §3.2 reference rules, EF containment | Tasks 1, 2, 3 |
| §3.3 contents by project | Tasks 2, 3, 8–12 |
| §3.4 `Impl` folder layout | Tasks 4, 5, 8–11 |
| §3.5 process topology | Task 13 |
| §3.6 extension seams | Tasks 8 (`IScheduledJob`), 9 (`ITaskAction`), 11 (`IAssistantTool`), 10 (`IChatCompletionClient`) |
| §4.1 anemic models | Task 2, enforced by `ConventionTests` |
| §4.2 single writer and its rules | Task 6 |
| §4.3 schema, constraints, filtered index | Task 3 |
| §4.4 mapping | Task 5 |
| §5.1 capture flow | Task 11 |
| §5.2 prompt with injected time | Tasks 4, 11 |
| §5.3 four tools | Task 11 |
| §5.4 time contract and guards | Task 4 |
| §5.5 provider routing and fallback | Task 10 |
| §5.6 never lose a capture | Task 11 |
| §6.1 scheduler | Task 8 |
| §6.2 due reminder job, send-then-mark | Task 8 |
| §6.3 daily brief, no cutoff | Task 12 |
| §6.4 buttons, versioned payload, idempotency | Tasks 5, 9 |
| §6.5 failure handling | Tasks 8, 9, 10, 11 |
| §7.1 compose-based integration harness | Task 3 |
| §7.2 unit tests only where integration cannot reach | Tasks 4, 5, 9 |
| §7.3 assertion standard | Tasks 7, 8 |
| §7.4 required scenarios | Tasks 8, 9, 11, 12 |
| §7.5 architecture tests | Tasks 1, 2, 6 |
| §7.6 prompt evaluation | **Not implemented** — see gaps below |
| §7.7 test-driven method | Every task |
| §8 deployment, heartbeat healthcheck | Task 13 |
| §11.1 repository layout | Tasks 0, 13 |
| §11.2 secrets discipline | Tasks 0, 13 |
| §11.3 credential-free CI | Task 13 |
| §11.4 positioning | Task 0 |
| §11.5 quickstart | Tasks 0, 13 |
| §11.6 GHCR image | Deferred by the spec itself |
| §12.1 XML docs, `CS1591` as error | Task 1, verified in Task 2 Step 10 |
| §12.2 mapping as extension methods | Task 5 |
| §12.3 Refit | Task 10 |
| §12.4 `AGENTS.md` | Tasks 0, 13 |

**Gaps found and resolved:**

1. **Prompt evaluation (§7.6) has no task.** It calls a live model, costs money, and by design never runs in CI, so it is not part of getting slice 1 working. Task 13 Step 11 explicitly removes the eval section from `AGENTS.md` rather than leaving a command that does not work. Building the eval harness is the first item of slice 1.1.
2. **The per-minute call cap (§5.6) is configured but not enforced.** `LlmOptions.MaxCallsPerMinute` is bound and documented, and `MaxToolRounds` bounds the loop within a single message, which covers the runaway case that actually threatens the bill. A cross-message rate limiter is the second item of slice 1.1.
3. **`/status` (§6.5) landed in `MessageHandler`** rather than as a separate command router. At one command that is the right size; a router becomes worthwhile at three.

### Placeholder scan

No `TBD`, `TODO`, "implement later", or "similar to Task N". Every code step carries complete code. Two steps intentionally describe a *verification* rather than an edit — Task 1 Step 7 (break the architecture test, then restore it) and Task 13 Step 11 (walk `AGENTS.md`) — and both state the exact commands and expected outcomes.

### Type consistency

Checked every name used across task boundaries:

- `ReminderStatus` / `Priority` / `ReminderTask` — consistent, Tasks 2 → 13.
- `ITaskService.CreateAsync` returns `(Result, ReminderTask?)` in Task 2 and is consumed with that shape in Tasks 6 and 11.
- Action keys: `"done"`, `"snooze"`, `"resched"`, `"edit"` — the notifier in Task 7 emits `resched` and `RescheduleAction.Key` in Task 9 returns `resched`. **Verified matching**; the spec's prose says "Reschedule", which would have been a silent mismatch had the key been spelled out in full.
- `IChatCompletionClient`, not `IChatClient` — consistent in Tasks 10, 11, 13, with the collision reason recorded.
- `PostgresFixture.ConnectionString` / `ResetAsync` / `PostgresCollection.Name` — consistent, Tasks 3, 6, 8, 9, 11, 12.
- `FakeClock.Set` — introduced in Task 6, used in Tasks 8, 9, 11, 12.
- `TelegramStub.SentMessages` / `EditedMessages` / `AcknowledgedCallbacks` / `Reset` — introduced in Task 7, used in Tasks 8, 9, 11, 12.
- `AnthropicStub.RespondWithToolCall` / `RespondWithText` / `RespondWithError` / `RequestCount` — introduced in Task 10, used in Task 11.
- `ILocalTimeResolver` members `Resolve` / `ToLocal` / `LocalToday` / `DescribeNowForPrompt` — introduced Task 4, used in Tasks 5, 6, 9, 11, 12.
- `AddAssistantRepository` and `MigrateAssistantDatabaseAsync` — introduced Task 3, used in Tasks 3, 6, 8, 9, 11, 12, 13.

One inconsistency found and fixed while reviewing: `IPendingEditStore` and `PendingEditStore` were introduced inside Task 9 but are listed in the Task 9 file list only implicitly. They are declared in that task's Step 4 with full code, so the plan is executable as written.
