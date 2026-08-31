# Design Spec — Personal AI Assistant, Slice 1: Reminders

**Version:** 2.0.0
**Date:** 2026-08-16
**Status:** Approved for planning
**Stack:** C# / .NET 10 (LTS), PostgreSQL 16, Telegram Bot API, Refit
**Repository:** `personal-ai-assistant` — public on GitHub from the first commit, MIT licensed
**Supersedes for slice 1:** `personal_ai_assistant_spec.md` (that document remains the long-term vision)

---

## 1. Purpose and scope

### 1.1 The problem being solved

Tasks get captured and then forgotten. Nothing resurfaces them at the right moment. Storage is not the failure; proactive delivery is.

This inverts the roadmap in the original spec, where proactive reminders were Phase 4. Here they are the product. Task CRUD exists to serve the reminder loop, not the other way round.

### 1.2 What slice 1 does

- Capture a task from free-form Telegram text, including natural-language times ("call the bank tomorrow at 10")
- Fire a reminder at the due time, reliably, surviving process restarts
- Send a daily brief each morning with today's and overdue tasks
- Complete, snooze, and reschedule via inline buttons — one tap, no LLM call
- Answer "what's on today" in natural language

### 1.3 What slice 1 explicitly excludes

| Excluded | Reason |
| :--- | :--- |
| Notes, embeddings, `pgvector`, semantic search | A separate product surface. Own slice, own spec. Additive migration later. |
| Multi-user support | Single user, whitelisted by Telegram ID. Adding `user_id` later is a trivial migration. |
| Webhooks and any HTTP surface | Long polling needs no domain, no TLS certificate, no inbound firewall rule. No `Api` project in slice 1. |
| `System.Threading.Channels` ingestion queue | It exists to prevent webhook timeouts. With long polling there is no HTTP request to time out. |
| Quartz.NET | A persistent job store solving a problem we do not have. The requirement is a query, not a job. |
| Separate due date and reminder time | Not needed until "due Friday, remind me Wednesday". Additive when it is. |
| Calendar integration, voice, context-aware timing | Later slices. |

### 1.4 Success criteria

1. A task captured in one message fires a reminder at the correct Jerusalem local time.
2. A process restart between capture and due time does not lose the reminder.
3. No reminder is delivered twice under normal operation.
4. The daily brief arrives once per day, even across restarts.
5. Completing a task from a reminder takes one tap and costs zero tokens.
6. Failure of the LLM provider never causes a captured message to be lost.

---

## 2. Constraints and decisions taken

| Decision | Choice | Rationale |
| :--- | :--- | :--- |
| Runtime | .NET 10 (LTS) | Current long-term-support release |
| Hosting | Cheap VPS (Hetzner/DO), Docker Compose | Proactive push requires genuine 24/7 uptime |
| Host type | Worker Service (Generic Host), no web server | No inbound HTTP in slice 1 |
| Telegram transport | Long polling | No public domain, cert, or open port; self-reconnecting |
| Timezone | Configured IANA identifier | Bound at startup so a typo fails fast. Default is `Asia/Jerusalem`. |
| HTTP clients | Refit for every HTTP API this project calls itself | Typed interfaces, declarative, WireMock-friendly (§12.3) |
| Documentation | XML doc comments on every public member, enforced by the build | §12.1 |
| Mapping | Extension methods only | §12.2 |
| Integration test infrastructure | Docker Compose, not Testcontainers | §7.1 |
| Licence and visibility | MIT, public from commit #1 | §11 |
| Storage of instants | UTC in the database, converted at the edges | DST fall-back repeats a local hour; UTC makes double or missed fires impossible |
| Interaction model | LLM parses free text in; inline buttons out | Messy language needs the model; done/snooze/reschedule is a fixed set |
| Model shape | Anemic persistence models; behaviour in services | §4 |
| `Contracts` | Request/response types only — the app's external surface. References nothing. | It is what callers speak, not where interfaces live |
| `Interfaces` | All interfaces, referencing `Models` | Internal calls pass models directly; DTOs are needed only at real edges |
| `Impl` → `Repository` | No reference. Services bind to `ITaskRepository` from `Interfaces`; `Worker` wires the implementation | Makes EF unreachable from services by construction rather than by rule |
| Delivery guarantee | At-least-once (send, then mark) | A duplicate is annoying; a miss defeats the product |
| Parse mode | HTML | MarkdownV2 has 18 escape-sensitive characters; an underscore in a title causes a 400 on a live reminder |

---

## 3. Solution structure

### 3.1 Projects

```
PersonalAssistant.slnx
├─ Directory.Build.props        net10.0, nullable enable, warnings-as-errors
├─ Directory.Packages.props     central package version management
├─ src/
│  ├─ Assistant.Models          POCOs mapped to tables. No behaviour.
│  ├─ Assistant.Contracts       Request/response types. The external surface.
│  ├─ Assistant.Interfaces      Every interface in the system.
│  ├─ Assistant.Repository      EF Core, DbContext, migrations, repository impls.
│  ├─ Assistant.Impl            Every other implementation: services, jobs, adapters.
│  └─ Assistant.Worker          Host and composition root.
└─ tests/
   ├─ Assistant.UnitTests
   └─ Assistant.IntegrationTests
```

### 3.2 Reference rules

```
Models      →  (nothing)
Contracts   →  (nothing — BCL only)
Interfaces  →  Models, Contracts
Repository  →  Interfaces, Models
Impl        →  Interfaces, Contracts, Models        never Repository
Worker      →  everything
UnitTests   →  Impl, Interfaces, Contracts, Models
IntegrationTests → Worker (boots the real host)
```

Three rules carry the weight of this layout:

**`Contracts` holds request/response types, not interfaces.** It is what a caller speaks to the application: `CreateTaskRequest`, `TaskResponse`, `ListTasksRequest`. In slice 1 those callers are the LLM tool invocations and the button callbacks; when an HTTP API arrives in a later slice it consumes the same types unchanged. `Contracts` references nothing, so it stays a pure vocabulary.

**`Interfaces` holds every interface and may reference `Models`.** This is what keeps the design simple: internal calls pass `ReminderTask` directly, so there is no obligatory mapping layer between every collaborator. Mapping survives only where something genuinely external is on the other side — an LLM tool parameter, a Telegram payload.

**`Impl` never references `Repository`.** Services live in `Impl` and call the repository — but they call it through `ITaskRepository`, which lives in `Interfaces`. C# requires a reference only for types a project *names*, and `TaskService(ITaskRepository repo)` names nothing from `Repository`. The reference is therefore unnecessary, and omitting it makes EF Core structurally unreachable from every service and adapter: not by convention, not by a package trick, but because there is nothing for the compiler to bind to.

Registration happens where registration belongs — the composition root:

```csharp
builder.Services.AddAssistantRepository(connectionString);  // from Repository
builder.Services.AddAssistantServices();                     // from Impl
```

`AddAssistantServices()` registers `TaskService`, the jobs, the actions, and the adapters, all binding to interfaces. At runtime the `Repository` assembly is present regardless, because `Worker` references it and .NET copies transitive dependencies to the output folder.

Two constraints preserve the boundary:

1. `Repository`'s public surface is exactly its repository implementations plus `AddAssistantRepository(this IServiceCollection, string connectionString)`, which registers `AppDbContext` and applies migrations internally. `Worker` never names an EF type; `Repository`'s EF package references are marked `PrivateAssets="compile"` so their compile-time assets do not flow outward while their runtime assets still do. **Not `"all"`** — that withholds the runtime assets too, so `Npgsql.EntityFrameworkCore.PostgreSQL.dll` is never copied to `Worker`'s output and `UseNpgsql` throws at startup. Verified empirically in F1: with `"all"` the provider assembly is absent from `Worker`'s output; with `"compile"` it is present and naming a `DbContext` in `Worker` source still fails to compile (CS0234), which is the property this rule exists to protect.
2. Repository methods return **materialised** results — `IReadOnlyList<ReminderTask>`, never `IQueryable<T>`. A queryable would leak EF back out through the interface and move query composition into the services.

The one case that would genuinely force a reference is a transaction spanning multiple repository calls. Slice 1 has none — capture is a single write, reminder delivery is send-then-single-update, the daily brief is one insert. Should one arise later, the answer is an `IUnitOfWork` in `Interfaces`, not a project reference.

Consequence for design: repository methods are named by intent (`GetDueRemindersAsync(DateTimeOffset now, int limit)`) rather than being generic composable queries. This is a better boundary anyway — the query lives next to the index built for it.

### 3.3 Contents by project

| Project | Contents |
| :--- | :--- |
| `Models` | `ReminderTask`, `ChatMessage`, `DailyBriefLog`, `ReminderStatus`, `Priority` — plain POCOs |
| `Contracts` | `CreateTaskRequest`, `UpdateTaskRequest`, `ListTasksRequest`, `TaskResponse`, `TaskListResponse`, `ReminderNotification`, `TaskFilter`; `Result`/`Error` types |
| `Interfaces` | `ITaskService`, `IMessageHandler`, `ITaskRepository`, `IChatMessageRepository`, `IDailyBriefRepository`, `INotifier`, `IAssistantTool`, `IScheduledJob`, `ITaskAction`, `IChatClient` wrapper |
| `Repository` | `AppDbContext`, EF configurations, migrations, `EfTaskRepository` and siblings, `AddAssistantRepository` |
| `Impl` | Everything else implemented — see the folder layout below |
| `Worker` | `Program.cs`, DI registration, options binding, hosted-service registration |

### 3.4 `Impl` internal layout

With services and adapters in one assembly, folders and namespaces carry the separation that project references used to. This layout is enforced by architecture tests (§7.5), not convention alone.

```
Assistant.Impl/
├─ Services/       TaskService, MessageHandler, AgentService, LocalTimeResolver
│  ├─ Jobs/        DueReminderJob, DailyBriefJob
│  └─ Actions/     DoneAction, SnoozeAction, RescheduleAction, EditAction
├─ Mapping/        ReminderTaskMappingExtensions, NotificationMappingExtensions
├─ Tools/          CreateTaskTool, ListTasksTool, UpdateTaskTool, CompleteTaskTool
├─ Telegram/       TelegramListener, TelegramNotifier, CallbackRouter
├─ Ai/             IAiApi (Refit), AiClient (the IAiClient adapter), SystemPrompt.
│                  FallbackChatClient is undecided, see §5.5.
└─ Scheduling/     ReminderScheduler, ScheduledJobBase, HeartbeatWriter
```

### 3.5 Process topology

A single .NET 10 Worker Service process. Two hosted services:

```
Assistant.Worker (one process)
├─ TelegramListener   IHostedService — long-poll loop, whitelist, dispatch
└─ ReminderScheduler  IHostedService — 30s tick, runs IScheduledJob implementations
```

### 3.6 Extension seams (Open/Closed)

Each interface is a point where behaviour is added by writing a new class, never by editing an existing one.

| Interface | Implementations in slice 1 | Extension means |
| :--- | :--- | :--- |
| `ITaskAction` | `DoneAction`, `SnoozeAction`, `RescheduleAction`, `EditAction` | New button → new class, resolved by callback key |
| `IAssistantTool` | `CreateTask`, `ListTasks`, `UpdateTask`, `CompleteTask` | New capability → new class, auto-registered |
| `IScheduledJob` | `DueReminderJob`, `DailyBriefJob` | New recurring job → new class; scheduler untouched |
| `INotifier` | `TelegramNotifier` | Additional channel without touching job logic |
| `TimeProvider` | `TimeProvider.System`, `FakeTimeProvider` | Makes every time-based rule testable |

`ReminderScheduler` injects `IEnumerable<IScheduledJob>` and knows nothing about the jobs it runs. Adding a third job requires no change to the scheduler.

Inheritance is used narrowly and only for genuinely shared behaviour: `ScheduledJobBase` holds the re-entrancy guard and the try/catch boundary. Everything else is composition behind interfaces.

---

## 4. Data model and where behaviour lives

### 4.1 Models carry no behaviour

`Assistant.Models` holds plain POCOs with public setters, mapped directly to tables. No methods, no invariant enforcement, no domain logic.

```csharp
public sealed class ReminderTask
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public ReminderStatus Status { get; set; }
    public Priority Priority { get; set; }
    public DateTimeOffset? DueAt { get; set; }           // UTC
    public DateTimeOffset? ReminderSentAt { get; set; }   // null = delivery still owed
    public int DeliveryAttempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
```

**Naming:** the model must not be called `Task` and the enum must not be `TaskStatus` — both collide with `System.Threading.Tasks` and produce ambiguous references in every async file.

### 4.2 `TaskService` is the single writer

Anemic models remove the entity as an enforcement point, so the rules need one owner instead. `TaskService` (in `Impl/Services`) is the only type permitted to mutate a `ReminderTask`. Jobs, tool handlers, and button actions all call it; none of them touch a repository directly.

`ITaskService` lives in `Interfaces`, so it takes request types from `Contracts` and returns models directly — no mapping on internal calls. The interface grows a method per feature that needs one, arriving with its caller rather than all at once; slice 1 ships one method so far:

```csharp
public interface ITaskService
{
    Task<Result> MarkReminderSentAsync(Guid id, CancellationToken ct);
}
```

Rules enforced inside it, in one place:

- Completing a cancelled task is rejected.
- Completing an already-completed task is a no-op, so a button can be tapped twice safely.
- Snooze and reschedule will clear `ReminderSentAt` and reset `DeliveryAttempts`, so the task fires again — the shape this pairing takes once `DeliveryAttempts` returns (F11); today only `MarkReminderSentAsync` sets `ReminderSentAt`, and there is no `DeliveryAttempts` column yet. **This pairing is the reason a single writer is mandatory** — setting one without the other silently stops a task from ever reminding again.
- Snooze or reschedule on a completed task is rejected.
- `MarkReminderSent` on a task with no `DueAt` is rejected.
- `UpdatedAt` is stamped on every mutation.

Three defences keep this from eroding:

1. An architecture test asserting no type under `Impl.Services.Jobs` or `Impl.Services.Actions` references a repository interface.
2. Database check constraints for the hard invariants (§4.3).
3. Integration tests targeting `TaskService` directly, covering every rule above — the highest level able to reach them; `AGENTS.md` rules out a unit test for behaviour an integration test already covers.

### 4.3 Schema

```sql
CREATE TABLE reminder_tasks (
    id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    title              TEXT NOT NULL,
    notes              TEXT,
    status             INT NOT NULL DEFAULT 1,   -- 0 Unknown (never persisted), 1 Pending, 2 Completed, 3 Cancelled
    priority           INT NOT NULL DEFAULT 1,   -- 1 Normal, 2 High
    due_at             TIMESTAMPTZ,              -- UTC; also the reminder time
    reminder_sent_at   TIMESTAMPTZ,              -- NULL = delivery still owed
    delivery_attempts  INT NOT NULL DEFAULT 0,
    created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at       TIMESTAMPTZ,

    CONSTRAINT ck_completed_consistency
        CHECK ((status = 2) = (completed_at IS NOT NULL)),
    CONSTRAINT ck_sent_requires_due
        CHECK (reminder_sent_at IS NULL OR due_at IS NOT NULL),
    CONSTRAINT ck_status_known
        CHECK (status <> 0)
);

CREATE INDEX idx_tasks_due_pending
    ON reminder_tasks (due_at)
    WHERE status = 1 AND reminder_sent_at IS NULL;

CREATE TABLE chat_messages (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    role        VARCHAR(20) NOT NULL,   -- user | assistant | tool
    content     TEXT NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_chat_messages_recent ON chat_messages (created_at DESC);

CREATE TABLE daily_brief_log (
    brief_date  DATE PRIMARY KEY,       -- Jerusalem local date
    sent_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

Three points worth stating explicitly:

- **`due_at` doubles as the reminder time.** One field. Splitting it is additive later.
- **`reminder_sent_at` is the idempotency key.** The scheduler selects only rows where it is NULL, which makes restart catch-up and duplicate suppression the same mechanism.
- **`daily_brief_log.brief_date` as primary key** makes the insert itself the once-per-day check. No race condition is possible.

`chat_messages` exists only so follow-ups resolve ("actually make it 11"). The most recent 20 rows are loaded per request. Slice 1 has no pruning job — at single-user volume the table stays small and old rows are never read.

### 4.4 Mapping

Models flow freely across internal boundaries because `Interfaces` references `Models`. Mapping is therefore needed in exactly two places, both genuinely external:

1. **Inbound** — an LLM tool call or button callback arrives as a `Contracts` request type, which the service turns into model changes.
2. **Outbound** — a `TaskResponse` or `ReminderNotification` handed to `INotifier` for rendering, so the Telegram layer never sees a database shape.

Hand-written mappers in `Impl/Services`, not a mapping library — a handful of properties does not justify a dependency, and an explicit mapper fails visibly when a field is added.

The predictable defect is a new column the mapper forgets. Covered by a round-trip unit test asserting every property survives model → response → model.

---

## 5. Capture path

### 5.1 Flow

```
Telegram update arrives (long poll)
  → whitelist: reject any sender other than the configured owner ID, silently
  → persist inbound message to chat_messages
  → start "typing…" indicator, refreshed every 4s until the reply is sent
  → build request: system prompt + last ~20 turns + user text
  → IAiClient → tool call
  → IAssistantTool → ITaskService → repository → Postgres
  → reply rendered with inline keyboard
```

The whitelist check happens before any LLM call, so an unknown sender costs nothing.

**Deferred:** F7 shipped the listener without the `persist inbound message to chat_messages`
step. `ChatMessage` and its table arrive at F13; F7 writes nothing to the database, and the reply
above is built from the update Telegram just delivered, not from anything read back.

**Deferred:** F7 shipped without the `start "typing…" indicator` step. There is nothing to
compose until F9 makes a model call — F7's reply is the user's own text echoed back, which is
instant, so there is no wait for an indicator to cover.

### 5.2 System prompt

The current local time is injected on every call:

> `Current time: Sunday 16 August 2026, 23:40, Asia/Jerusalem (UTC+3). All times the user gives are Jerusalem local. Return absolute local ISO-8601 datetimes with no offset.`

Without this the model has no basis for resolving "tomorrow" and will guess.

**The zone comes from configuration**, default `Asia/Jerusalem`, injected into both the prompt and `LocalTimeResolver`. This was originally deferred (§12.7) but brought forward during F8 because a hardcoded zone would block any contributor outside Israel in their first five minutes (spec §11).

### 5.3 Tools

| Tool | Parameters |
| :--- | :--- |
| `create_task` | `title`, `due_at_local?`, `notes?`, `priority?` |
| `list_tasks` | `filter` (today \| overdue \| week \| all), `limit?` |
| `update_task` | `task_id`, `title?`, `due_at_local?`, `priority?` |
| `complete_task` | `task_id` |

Each is one `IAssistantTool` implementation with a single responsibility and a self-describing schema, calling `ITaskService`.

### 5.4 Time contract

The model returns an absolute local ISO string (`2026-08-17T10:00:00`, no offset). `LocalTimeResolver` — which takes the configured IANA zone, never a hardcoded one — converts local → UTC, and `Resolve` applies the guard clauses itself, returning a `Result<DateTimeOffset>` whose caller turns a failure into a question to the user before anything is persisted:

| Condition | Behaviour |
| :--- | :--- |
| More than one minute in the past | Do not store silently; ask the user for clarification |
| More than two years in the future | Almost certainly a hallucinated year; ask |
| Non-existent local time (spring-forward gap) | Resolves to the instant just past the gap — in Jerusalem's one-hour gap, 02:30 becomes 03:30 — needing no branch (`GetUtcOffset` returns the pre-gap offset for a time inside it) |
| Ambiguous local time (fall-back hour) | `TimeZoneInfo.IsAmbiguousTime` → take the first occurrence (`GetUtcOffset` defaults to the second) |

Both `TimeZoneInfo.ConvertTimeToUtc` and `TimeZoneInfo.GetUtcOffset` resolve an ambiguous local time to its second occurrence, standard time, so the first occurrence this spec requires has to be selected by hand — `GetAmbiguousTimeOffsets(...).Max()` is always the first.

Absolute-from-model rather than relative is deliberate: resolving relative expressions in C# would mean writing the natural-language date parser the LLM was chosen to replace. The guard clauses provide safety without the parser.

### 5.5 Provider routing and fallback

**Corrected at F9a:** every provider this project reaches is exercised through **one** Refit
interface, `IAiApi` (§12.3), named for the OpenAI-compatible chat API that OpenRouter, OpenAI,
Groq and a local Ollama all serve — not `IAnthropicApi`/`IOpenRouterApi` as this section
originally named them. Anthropic is ruled out of slice 1 entirely; OpenRouter is the provider
`AiSettings` ships a default for, and switching to any other OpenAI-compatible endpoint is a
configuration change (`AiSettings.BaseUrl`, `AiSettings.Model`), not a new type. `AiClient` is
the one `IAiClient` adapter, translating between the wire format and the project's own request
and response types.

`FallbackChatClient` is a decorator wrapping a primary and a secondary `IAiClient`, with Polly
for timeout and circuit-breaking. Neither concrete client is aware of the other; only the
composition root changes when providers change.

**Open question, raised at F9a, not resolved here:** OpenRouter is itself a router — a request
against one upstream model can already fail over to another model before OpenRouter ever answers
the caller. Going OpenRouter-first for the primary (and, plausibly, the fallback too) may make
`FallbackChatClient` redundant with routing OpenRouter already does. This section owes an answer
before F13 builds `FallbackChatClient`; F9a does not decide it.

### 5.6 Never lose a capture

If every provider fails, the message is **not** dropped. The raw text is stored as a task with no due date and the bot replies that it could not parse it but has saved it. Given the product exists because things get forgotten, silently discarding input is the one unacceptable failure mode.

A per-minute cap on LLM calls bounds cost if anything loops.

---

## 6. Reminder loop and interaction

### 6.1 Scheduler

A `PeriodicTimer` on a 30-second tick, injected with `IEnumerable<IScheduledJob>`. `ScheduledJobBase` holds a re-entrancy guard so a slow job cannot overlap itself, and every job runs inside try/catch: a throwing job must never terminate the loop or the host. Each successful tick touches the heartbeat file used by the container healthcheck (§8). **Deferred:** F5b shipped the scheduler without this. There is no heartbeat file and no container healthcheck yet — §8 describes the intent for when a container exists, not current behaviour.

### 6.2 `DueReminderJob`

Backed by `ITaskRepository.GetDueRemindersAsync(now, limit)`:

```sql
SELECT * FROM reminder_tasks
WHERE status = 1 AND due_at <= @now AND reminder_sent_at IS NULL
ORDER BY due_at
LIMIT @limit;
```

There is deliberately no lower bound on `due_at` — that is what makes restart catch-up automatic. Because a long outage would otherwise produce a burst of individual messages, anything overdue by more than 24 hours is collapsed into a single summary message. **Deferred:** not built at F5b, which sends one message per overdue task no matter how overdue it is. §7.4's "overdue by 3 days across 5 tasks → one summary message, not five" scenario belongs to this collapse and is therefore deferred with it; F5b's third job test deliberately arranges one overdue task, not five, so it asserts nothing that the deferred behaviour would later contradict.

**Delivery ordering: send, then mark.** The reverse (mark, then send) loses a reminder when the send fails after the write. At-least-once is the correct trade for this product. `delivery_attempts` caps retries at 3 so a persistent failure cannot loop indefinitely. **Deferred:** there is no `delivery_attempts` column yet and no retry cap — a persistent failure today retries forever, once per tick, rather than giving up after three.

### 6.3 `DailyBriefJob`

Fires at 07:00 Jerusalem (configurable). Inserts today's Jerusalem local date into `daily_brief_log`; the primary key makes this the idempotency check. Content: tasks due today, overdue items, and counts.

**There is no cutoff.** If the host was down all morning and comes up at 19:00, the brief is still sent that evening. A late brief is strictly better than a silent day — the tasks in it are still owed, and the product's whole premise is that nothing gets dropped. `daily_brief_log` still guarantees exactly one per day, so a late send cannot become a duplicate.

### 6.4 Inline buttons

Callback data format: `v1:<action>:<base64-id>[:<arg>]` — roughly 33 bytes against Telegram's 64-byte limit.

The `v1:` prefix means buttons left in chat history degrade gracefully when the format changes, rather than throwing. Actions are `ITaskAction` implementations resolved by key; an unrecognised key produces a polite message.

| Button | Action | Effect |
| :--- | :--- | :--- |
| `Done` | `DoneAction` | `CompleteAsync`; message edited to show it struck through, buttons removed |
| `Snooze 1h` | `SnoozeAction` (arg `1h`) | `SnoozeAsync(1h)`; clears `ReminderSentAt` so it fires again |
| `Tomorrow` | `RescheduleAction` (arg `tomorrow`) | Moves `DueAt` to 09:00 Jerusalem the next day |
| `Edit` | `EditAction` | Replies asking what to change; the next free-text message is routed to `update_task` for that task ID |

`EditAction` is the only one that costs an LLM call, and only on the follow-up message.

Three required behaviours:

1. **Always answer the callback query**, or Telegram shows a spinner indefinitely.
2. **Edit the original message in place** rather than sending a new one, so the chat stays clean.
3. **Every action is idempotent** — a second tap on Done yields "already done", not an error.

### 6.5 Failure handling

| Failure | Response |
| :--- | :--- |
| Telegram 429 | Polly retry honouring `retry_after` |
| Telegram 400 | Never retry — it is a formatting defect. Log with the payload. |
| LLM provider error | Fall through to secondary; then the raw-capture path (§5.6) |
| Database unavailable | Nothing is lost; reminders are rows, the next tick retries |
| Unhandled exception in a job | Caught, logged, loop continues |

Serilog structured logging throughout. A deterministic `/status` command reports uptime, pending task count, and last brief time — no LLM involved, and the way to confirm the system is alive without SSH.

---

## 7. Testing strategy

Testability is a day-one requirement, and the driver behind the reference rules in §3.2.

### 7.1 Default level: full-stack in Docker

`Assistant.IntegrationTests` exercises the real host and DI container, a real PostgreSQL, and WireMock.NET standing in for the Telegram and LLM HTTP APIs. `FakeTimeProvider` replaces `TimeProvider.System`.

**Postgres comes from Docker Compose, not Testcontainers.** `compose.test.yaml` at the repository root defines a Postgres service on a fixed port with a healthcheck. It is brought up once — by the developer locally, or by a CI step before `dotnet test` — and the suite connects to it.

This means the test fixture owns two responsibilities Testcontainers would have handled:

1. **Readiness.** The fixture polls the connection until it succeeds, with a bounded timeout, rather than assuming the container is accepting connections the moment compose returns.
2. **Isolation.** Respawn resets tables between tests. Where a test needs harder isolation, it creates its own database on the shared server rather than sharing the default one.

The trade is honest: one more command in the developer loop and in CI, against one fewer dependency, and infrastructure defined in the same compose format the project already ships for production.

Today, each test builds its own `ServiceCollection` and registers only the services it exercises — `Telegram.Bot` and the LLM clients pointed at WireMock base addresses, connection string pointed at the compose service — rather than composing `Assistant.Worker`'s full `IHost`. A `HostApplicationFactory` helper that boots the whole host with test overrides arrives with the first feature that needs the whole host composed.

WireMock runs as its own container, defined alongside Postgres in `compose.test.yaml`. Tests verify against it through its admin API (`GET /__admin/requests`), and the request log is cleared between tests the same way Respawn clears tables.

This level is the refactor safety net: it asserts what the system does rather than how it is wired, so the interior can be restructured freely.

### 7.2 `Assistant.UnitTests` — only what integration does not already cover

**No duplication.** If a behaviour is already asserted by an integration test, it does not get a unit test as well. One test per behaviour, at the highest-fidelity level that can reach it. Two suites asserting the same rule means two places to update and no extra confidence.

That leaves unit tests for the cases integration cannot reach cheaply:

1. **Combinatorial tables** — dozens of permutations of status × due × sent, and the DST and snooze arithmetic. Integration could assert each one, but at a database reset per row; a table-driven unit test covers forty cases in milliseconds.
2. **Mapper round-trips** (§4.4, §12.2) — pure functions, nothing to integrate.
3. **`TaskService` rules that have no observable side effect** — a rejection that produces no message and no row change is invisible from the outside, so it is asserted directly. Rules that *do* produce an observable effect are covered by integration and are not repeated here.

No `FakeNotifier`, no contract-test suite. The integration level covers adapters against real wire formats, so there is no fake to drift.

### 7.3 Assertion standard

Every delivery assertion pins four things — **count, recipient, exact text, exact buttons** — and every time assertion is an absolute instant. "A message was captured" is not acceptable; it passes when the wrong message is sent at the wrong time.

```csharp
// 10:00 Jerusalem == 07:00Z in August (UTC+3)
clock.Set("2026-08-17T07:00:00Z");

await scheduler.Tick();

var sent = wireMock.FindLogEntries(Request.Create().WithPath("/bot*/sendMessage"));
sent.Should().HaveCount(1);                        // exactly one, not "at least one"

var body = SendMessagePayload.Parse(sent.Single());
body.ChatId.Should().Be(KnownChatId);
body.Text.Should().Be("Call the bank");             // exact string, not Contains
body.Buttons().Should().Equal("Done", "Snooze 1h", "Tomorrow", "Edit");

task.DueAt.Should().Be(DateTimeOffset.Parse("2026-08-17T07:00:00Z"));
task.ReminderSentAt.Should().Be(clock.UtcNow);

await scheduler.Tick();                            // second tick, same minute
sent.Should().HaveCount(1);                        // still one — no duplicate
```

### 7.4 Required scenarios

| Scenario | Expected |
| :--- | :--- |
| Tick twice within the same minute | Exactly one message |
| Process down 09:58, restarts 10:03 | One message, delivered late, not lost |
| Snooze 1h at 10:00 | Fires at 11:00 exactly; not immediately |
| Snooze clears the sent marker | `ReminderSentAt` null and `DeliveryAttempts` zero after snooze |
| DST fall-back, task at 02:30 local | Fires once |
| DST spring-forward, task at 02:30 local | Stored shifted past the gap; fires once |
| Overdue by 3 days across 5 tasks | One summary message, not five |
| Daily brief, restart at 07:05 | One brief that day |
| Both LLM providers failing | Raw task created; user informed; nothing lost |
| Task title containing `_` and `*` | Delivered successfully (HTML parse mode) |
| Done tapped twice | Second tap acknowledged as already done, no error |
| Complete a cancelled task | Rejected |
| Message from a non-whitelisted sender | Ignored; no LLM call recorded |

### 7.5 Architecture tests

With services and adapters sharing one assembly, these are load-bearing rather than decorative. NetArchTest assertions that fail the build on:

**Project-level**

- `Models` or `Contracts` referencing any other project
- `Contracts` containing an interface, or `Interfaces` containing a concrete class
- `Impl` referencing `Repository`, or `Repository` referencing `Impl`
- EF Core types appearing anywhere outside `Repository`
- `Models` containing methods other than property accessors
- Any repository method returning `IQueryable<T>`

**Namespace-level, inside `Impl`**

- `Impl.Services` referencing `Telegram.Bot`, the OpenAI-compatible wire types, or `Refit` types
- `Impl.Telegram` or `Impl.Ai` referencing repository interfaces
- `Impl.Services.Jobs` or `Impl.Services.Actions` referencing repository interfaces — they go through `ITaskService` (§4.2)

Without these, this layout drifts into a single tangled project. The `PrivateAssets="compile"` hiding in §3.2 covers the EF rules at compile time; the rest are tests.

### 7.6 Prompt evaluation

A separate, on-demand eval file of real phrasings ("next thursday", "in a couple weeks", "בעוד שעה") with expected parses, run against the live model. Kept out of CI so the suite stays deterministic and free.

### 7.7 Method

Test-driven: write the failing test, watch it fail, then implement.

---

## 8. Deployment

Docker Compose on the VPS:

- `postgres:16` with a named volume for data
- Worker image on `mcr.microsoft.com/dotnet/runtime:10.0`, `restart: unless-stopped`
- Secrets via `.env` (bot token, API keys, owner Telegram ID)
- EF Core migrations applied on startup, inside `AddAssistantRepository`
- **Healthcheck without an HTTP endpoint:** the scheduler touches `/tmp/heartbeat` on every successful tick; `HEALTHCHECK` fails if its mtime is older than two minutes. A wedged loop is then caught and restarted even though the process is still alive.

---

## 9. Implementation order

Each step ends with a working, tested system.

0. **Repo hygiene and agent docs, before any code** — `git init`, `.gitignore` (dotnet template), `.env.example`, `LICENSE` (MIT), stub `README.md`, `AGENTS.md` + `CLAUDE.md`, `docs/conventions.md`, and this design doc at `docs/design/slice-1-reminders.md`. Push public as `personal-ai-assistant`. Commit #1 specifically so no real secret can ever exist in history (§11.2), and so the conventions in §12 are readable by an agent before the first line of C# is written.
1. **Skeleton** — solution with the six src projects and two test projects, `Directory.Build.props` and `Directory.Packages.props`, all architecture tests from §7.5 written and green against empty projects, GitHub Actions workflow running them. The rules exist before there is code to break them.
2. **Models, contracts, interfaces** — POCOs, request/response types, every interface. No implementations. This is the shape of the system before anything does work.
3. **Repository** — EF configuration, migrations, `AddAssistantRepository`, implementations of the intent-named repository methods, the Docker Compose Postgres fixture (§7.1) running green.
4. **`TaskService` and its rules** — the single writer, mappers, every invariant from §4.2, full unit coverage.
5. **Telegram round-trip** — long-poll listener, whitelist, echo reply, WireMock-based integration test. No LLM yet.
6. **Reminder loop** — scheduler, `DueReminderJob`, delivery marking, restart catch-up and duplicate-suppression tests. Tasks seeded directly into the database. This is the reliability core, proven before any AI is involved.
7. **Buttons** — `ITaskAction` implementations, callback routing, in-place message editing, idempotency tests.
8. **LLM capture** — `IChatClient`, the four tools, the time contract and guard clauses, fallback decorator, raw-capture safety net.
9. **Daily brief** — `DailyBriefJob`, `daily_brief_log`, cutoff behaviour.
10. **Operations** — `/status`, Serilog, heartbeat healthcheck, Docker Compose, deploy to the VPS.

Steps 1–7 deliver a reliable reminder system with no AI at all. If step 8 were dropped entirely, the product would still work. That ordering is intentional: the risky, expensive, non-deterministic component sits on top of a foundation already proven correct.

Within each step, a type, interface member, model property, or table is introduced by the task that first exercises it with a test, not by an earlier step anticipating it (see the YAGNI reset, `docs/plans/2026-08-21-yagni-reset-plan.md`).

---

## 10. Deferred to later slices

| Item | Slice |
| :--- | :--- |
| Notes, embeddings, `pgvector`, semantic search | 2 |
| Recurring tasks ("every Monday") | 2 |
| Separate due date and reminder time | 2 |
| `Assistant.Api` project — HTTP surface, webhooks, admin UI | 2 or later |
| Splitting `Impl` once it outgrows one project | When it hurts |
| `Contracts` consumed by an HTTP API rather than only LLM tools | With `Assistant.Api` |
| Notion as the datastore | Evaluated and rejected — see below |

**Notion as datastore — considered and rejected.** Volume is not the obstacle: Notion allows roughly three requests per second per connection, and a 30-second tick uses two per minute. The obstacles are availability and enforceability. This product's single guarantee is proactive delivery, and Postgres on the same VPS is available if and only if the process is; Notion puts a third party in that critical path. It also has no unique or check constraints, so "one brief per day" and the consistency rules in §4.3 would move from the database to application discipline. The compensating design — a prefetch cache plus a durable local operation queue — reintroduces local state to replace the database that was removed.

Should this be wanted later, it needs no rework: `ITaskRepository` lives in `Interfaces`, so a `NotionTaskRepository` is a sibling of the EF implementation, selected by configuration, with no service changes. A one-way mirror into Notion purely for its UI is the cheaper version of the same idea.
| Surfacing stale, undated tasks | 3 |
| Calendar integration and context-aware timing | 3 |
| Voice message transcription | 3 |
| Multi-user support | Never — see §11.4 |

---

## 11. Open source

The repository is `personal-ai-assistant`, public on GitHub from the first commit, MIT licensed.

MIT because it is the shortest and most familiar licence, imposes the least friction on anyone who wants to run or fork this, and matches the nature of the project — a self-hosted tool, not a service anyone would be tempted to resell.

**Note on timezone references in this document.** Every mention of Jerusalem below and above means *the configured zone, which defaults to `Asia/Jerusalem`*. No code names a zone literal; it is bound from configuration and injected into `LocalTimeResolver`. This is a direct consequence of publishing: a hardcoded zone blocks every user who is not in Israel, in their first five minutes.

### 11.1 Repository layout

```
personal-ai-assistant/
├─ .github/
│  ├─ workflows/ci.yml           build, unit tests, integration tests, gitleaks
│  ├─ workflows/eval.yml         prompt evals — scheduled, never on fork PRs
│  ├─ dependabot.yml             NuGet + Actions updates
│  └─ ISSUE_TEMPLATE/
├─ AGENTS.md                     entry point for AI coding agents (§12.4)
├─ CLAUDE.md                     → symlink or one-line pointer to AGENTS.md
├─ docs/
│  ├─ design/slice-1-reminders.md   this document
│  └─ conventions.md             XML docs, mapping, Refit — the rules in §12
├─ src/                          six projects (§3.1)
├─ tests/                        two projects (§3.1)
├─ compose.yaml
├─ compose.test.yaml             Postgres for integration tests (§7.1)
├─ .env.example
├─ .gitignore
├─ LICENSE                       MIT
├─ CONTRIBUTING.md
└─ README.md
```

### 11.2 Secrets discipline

Three secrets exist: the Telegram bot token, the LLM API key, and the owner's Telegram user ID. A leaked bot token hands control of the bot to a stranger; a leaked API key spends the owner's credits.

Git history is permanent, which drives the ordering in §9: `.gitignore` and `.env.example` are commit #1, before a real token has ever been written to disk in the working tree.

- `.env` is gitignored. `.env.example` holds keys with empty or placeholder values and is the documented starting point.
- `gitleaks` runs in CI on every push and pull request, so a future accidental paste fails the build rather than being discovered later by GitHub's scanner.
- No secret is ever needed to build or test (§11.3), which removes the main reason anyone would be tempted to commit one.

### 11.3 CI without secrets

The test design in §7 makes this possible, and it is worth stating in the README as a feature.

WireMock.NET stands in for both the Telegram and LLM HTTP APIs, and PostgreSQL comes from Docker Compose (§7.1), not Testcontainers. GitHub's Ubuntu runners have Docker available, so the **entire suite — unit and integration — runs on a fork's pull request with zero credentials configured.** A contributor can validate a change end to end without an Anthropic account or a Telegram bot.

The one exception is the prompt eval suite (§7.6), which calls a live model. It runs on a schedule from the primary repository using a repository secret, and never on pull requests from forks — where a secret would be exposed to untrusted code.

`ci.yml` stages: restore → build with warnings as errors → architecture tests → unit tests → integration tests → gitleaks.

### 11.4 Positioning

Two design decisions read as unfinished features unless the README frames them deliberately:

**Single user by design.** There are no `user_id` columns and there is a hard whitelist on one Telegram ID. This is the point, not a gap: the pitch is a bot you run on your own €5 VPS, where your data never leaves your machine and there is no account, no tenant, and no operator with database access. Multi-user is moved from "later" to "never" in §10 on that basis.

**Reminder-first, not another todo app.** The README leads with the problem from §1.1 — tasks get captured and then forgotten — because that is what distinguishes this from every other Telegram todo bot, most of which are storage with no proactive loop.

### 11.5 README quickstart

Adoption is decided by how long the path from landing on the repo to a working bot is. The target is under five minutes and four steps:

1. Create a bot via `@BotFather`, copy the token
2. Get your own Telegram user ID (`@userinfobot`)
3. `cp .env.example .env`, fill in three values, optionally set your timezone
4. `docker compose up -d`

Nothing to compile, no database to provision. The README opens with a recording of the actual Telegram exchange — capture, reminder firing, one-tap Done — because that conveys what the thing does faster than any description.

### 11.6 Publishing the image

A GitHub Actions job publishes to GHCR (`ghcr.io/<owner>/personal-ai-assistant`) on tagged releases, and `compose.yaml` references the published image with a build override for local development. This means step 4 above pulls rather than compiles, which removes the .NET SDK from the list of things a user needs installed.

Deferred until the bot works; it is packaging, not product.

---

## 12. Code conventions

These are build-enforced where possible, because a convention that only lives in a document is a convention that erodes. They live in `docs/conventions.md` in the repository, referenced from `AGENTS.md`.

### 12.1 XML documentation comments

Every public type and member carries XML documentation, following the [recommended tags](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/recommended-tags).

**The rule that matters: document the contract, never the implementation.** A comment describes what a caller can rely on — what it does, what it returns, what it throws, what the valid inputs are. It does not narrate how the body works. Implementation detail in a doc comment is worse than no comment, because it goes stale silently while the summary still reads as authoritative.

Required:

| Tag | Applies to | Purpose |
| :--- | :--- | :--- |
| `<summary>` | every public type and member | What it is. Shown in IntelliSense. |
| `<param>` | every parameter | What the caller must supply |
| `<returns>` | every non-void method | What comes back, including what a null or empty result means |
| `<exception>` | every exception thrown deliberately | What causes it |
| `<value>` | properties | What the value represents |
| `<typeparam>` | generic types and methods | Constraints and intent |

Used where they add something:

- `<remarks>` — context that does not belong in a one-line summary: ordering guarantees, idempotency, thread safety, why a decision was made
- `<inheritdoc/>` — on implementations of an interface, so the contract is documented once. This is the default for everything in `Impl` implementing something from `Interfaces`.
- `<see cref="..."/>` and `<seealso>` — link related types instead of naming them in prose
- `<paramref>`, `<typeparamref>` — reference parameters inside prose
- `<c>` for inline code, `<code>` for blocks, `<example>` for non-obvious usage
- `<para>`, `<list>` for structure in longer remarks

**Enforcement.** `Directory.Build.props` sets `GenerateDocumentationFile=true`, and `CS1591` (missing XML comment for publicly visible type or member) is escalated to an error alongside the existing warnings-as-errors. Undocumented public API therefore fails the build rather than fails review.

Illustration of the distinction:

```csharp
/// <summary>
/// Moves a task's due time forward by <paramref name="duration"/> and re-arms its reminder.
/// </summary>
/// <param name="id">Identifier of the task to snooze.</param>
/// <param name="duration">How far forward to move the due time. Must be positive.</param>
/// <param name="ct">Cancellation token.</param>
/// <returns>
/// A successful <see cref="Result"/>, or a failure when the task does not exist
/// or has already been completed or cancelled.
/// </returns>
/// <remarks>
/// Snoozing clears the reminder-sent marker, so the task will fire again at its new
/// due time. Snoozing a task whose reminder has not yet fired is valid and simply
/// moves it. See <see cref="ITaskService.RescheduleAsync"/> for setting an absolute time.
/// </remarks>
Task<Result> SnoozeAsync(Guid id, TimeSpan duration, CancellationToken ct);
```

The `<remarks>` states a guarantee a caller depends on. It does not say "loads the entity, adds the timespan, and calls SaveChangesAsync" — that is implementation, and it is what the code already says.

### 12.2 Mapping is extension methods

All mapping between models, requests, and responses is written as extension methods in `Impl/Mapping`, grouped in `static class`es by the type being mapped.

```csharp
public static class ReminderTaskMappingExtensions
{
    /// <summary>Projects a task onto the response shape returned to callers.</summary>
    /// <param name="task">The task to project.</param>
    /// <returns>A response carrying the caller-visible fields of <paramref name="task"/>.</returns>
    public static TaskResponse ToResponse(this ReminderTask task) => ...;

    /// <summary>Builds a new task from a creation request.</summary>
    public static ReminderTask ToModel(this CreateTaskRequest request, DateTimeOffset dueAtUtc) => ...;
}
```

Naming is by destination: `ToResponse()`, `ToModel()`, `ToRequest()`, `ToNotification()`. No mapping library — explicit methods fail visibly when a property is added, and the round-trip tests in §7.2 cover the case where one is forgotten.

### 12.3 Refit for HTTP clients

Every HTTP API this project calls itself is expressed as a Refit interface. No `HttpClient` is used directly, and no request is composed by hand.

```csharp
/// <summary>Anthropic Messages API.</summary>
public interface IAnthropicApi
{
    /// <summary>Sends a message request and returns the model's reply.</summary>
    /// <param name="request">The message request, including any tool definitions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The model response, which may contain tool-use blocks.</returns>
    [Post("/v1/messages")]
    Task<AnthropicMessageResponse> CreateMessageAsync(
        [Body] AnthropicMessageRequest request,
        CancellationToken ct = default);
}
```

Registered via `AddRefitGeneratedClient<T>()` — Refit 15's source-generator path; `AddRefitClient<T>()` is the reflection path and needs a `Refit.Reflection` package this project does not take — with the base address, auth headers, and the Polly resilience handler attached at the `HttpClient` level, so retry and circuit-breaking are configuration rather than code inside the adapter.

Why this suits the project: a Refit interface *is* the contract, so it is legible and reviewable; and because the base address is a registration concern, pointing it at WireMock in tests requires no production seam.

**Exception:** where a maintained typed SDK already exists, it is used rather than reimplemented. `Telegram.Bot` is an SDK, not a raw HTTP client, and stays as-is — its base address is likewise configurable, so it remains WireMock-testable.

### 12.4 `AGENTS.md`

The repository root carries `AGENTS.md` so that an AI coding agent — or a new human contributor — can build, test, and run everything without reverse-engineering the project. `CLAUDE.md` points at it rather than duplicating it, so there is one file to keep true.

It contains, and nothing more than:

1. **What this is**, in three sentences.
2. **Every command, copy-pasteable and verified to work**: restore, build, unit tests, bring up `compose.test.yaml`, integration tests, run locally, apply migrations, run the prompt evals. Each with what it requires — Docker running, `.env` populated.
3. **Project map** — the six projects, one line each, and the reference rules from §3.2 with a pointer to the architecture tests that enforce them.
4. **The conventions from this section**, or a pointer to `docs/conventions.md`.
5. **What not to do** — do not add a project reference from `Impl` to `Repository` (§3.2), do not put behaviour on models (§4.1), do not mutate a task outside `TaskService` (§4.2), do not write a unit test for something an integration test covers (§7.2), do not use `HttpClient` directly (§12.3).
6. **Where the design lives** — `docs/design/`, and the instruction to read the relevant spec before making a structural change.

**It must stay honest.** A stale `AGENTS.md` actively misleads, whereas a missing one merely slows people down. Every command in it is one CI already runs, so drift shows up as a failing build rather than as a contributor's wasted afternoon.

### 12.5 Primary constructors

**Every class that takes constructor arguments declares them as a primary constructor.** No class
declares a separate constructor.

```csharp
internal sealed class EfTaskRepository(AssistantDbContext db) : ITaskRepository
{
    public async Task AddAsync(ReminderTask task, CancellationToken ct)
    {
        db.ReminderTasks.Add(task);
        await db.SaveChangesAsync(ct);
    }
}
```

The parameter is in scope for every member, so the assign-to-a-readonly-field ceremony disappears
along with the field itself. Base calls come along too:
`internal sealed class AssistantDbContext(DbContextOptions<AssistantDbContext> options) : DbContext(options)`.

Two consequences worth knowing before you hit them:

- **Parameters are documented on the class.** A primary constructor has no doc comment of its own,
  so its `<param name="...">` tags belong on the class-level block next to `<summary>`. Omitting
  one is `CS1573`, which is an error in `src/`.
- **A field initializer cannot reference another field.** Where one dependency is derived from
  another, the derived one becomes an expression-bodied property rather than a field:

  ```csharp
  private readonly ServiceProvider _provider = postgres.CreateProvider();

  private ITaskRepository Sut => _provider.GetRequiredService<ITaskRepository>();
  ```

**This rule is not build-enforced, and that is deliberate.** The compiler emits an ordinary
constructor either way, so no reflection test can tell the two apart. The analyzer that can
(`IDE0290`) needs `.editorconfig`, which this project does not use (§12.7). It is a review rule,
checked by reading.

### 12.6 No emoji

**No file in this repository contains an emoji.** Not source, not tests, not documentation, not
commit messages, and not the text the bot sends. A friendly tone is not an exception clause;
nothing here needs decoration to read as approachable, and the rule does not bend for a message
that only the assistant will ever read.

The case against them is concrete, not aesthetic. Emoji render at inconsistent widths across
fonts and terminals, and this project's documents are full of ASCII diagrams and reference
tables — §3.2, the directory tree in §11.1 — whose alignment depends on every character being
one column wide; a pictogram silently breaks that for whoever's renderer disagrees with the
author's. In a diff, an emoji is one opaque glyph: `git diff` shows that the line changed, not
what changed, and a reviewer cannot tell which pictogram replaced which without opening a
codepoint table. They are not greppable without already knowing the codepoint — you cannot
search a codebase for a character you cannot type. And inside a message body an emoji is one
more character that has to survive `ParseMode.Html` escaping intact, on top of the escaping debt
the feature backlog already owes to F7 — one more way for a reminder to fail for a reason that
has nothing to do with what it says. None of that buys anything a word would not.

So: use the word, or use nothing. The due-reminder message is the task title alone, with no
prefix — it arrives from the assistant, in a chat only the assistant writes to, so there is no
reader for a pictogram to orient and nothing left for it to disambiguate.

**This rule is not build-enforced, and that is deliberate — the same way §12.5 is honest about
`IDE0290`.** Catching it would need a Unicode-range scan wired into CI, and nothing here runs
one. It is a review rule, checked by reading.

### 12.7 Deferred conventions

| Item | Trigger |
| :--- | :--- |
| Per-user timezone, in the prompt and the resolver | A second user, or a trip that makes it personally annoying |
| Localisation of bot messages | Same |
| Analyzer package beyond the built-in rules | When style debates start costing review time |
