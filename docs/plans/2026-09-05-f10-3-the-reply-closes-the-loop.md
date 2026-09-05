# F10-3 — the reply closes the loop

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** F10-1 shipped `ITaskService.CreateAsync`, the writer half of F10, with no caller.
F10-2 shipped `IAssistantTool.ExecuteAsync` and `CreateTaskTool`'s execution body, with no
caller either. Nothing in production has ever dispatched to a tool. `MessageHandler` still sends
`ToolCallNotActedOnYet` — a placeholder that has been the owner's only reply to every real message
since F9b — and writes nothing. This slice wires the last connection: `MessageHandler` matches the
model's tool call against the registered `IEnumerable<IAssistantTool>`, calls `ExecuteAsync`, and
replies with what was actually persisted, carrying the Done button. This is the slice that makes
F10's own milestone true end to end — talk to the bot, get a row stored, get a reply with a Done
button — and the only one of F10's three slices that touches `MessageHandler`, `ILocalTimeResolver`
(in the UTC-to-local direction), and the backlog document.

**Tech Stack:** `net10.0`, nullable enabled, warnings are errors — the existing stack. This slice
adds **no new NuGet package** — `Microsoft.Extensions.TimeProvider.Testing` already reached
`Assistant.IntegrationTests` at F10-2 — and **no database migration**: every column this slice's
call graph ends up writing was already written by F10-1's `CreateAsync`; this slice only decides
what the owner sees afterward.

**Spec:** `docs/design/slice-1-reminders.md` §5.1 (capture path flow, ending "reply rendered with
inline keyboard" — the step this slice finally builds), §5.4 (the time contract — a resolver
failure is a question to the user before anything is persisted, already enforced one layer down by
F10-2), §6.4 (inline buttons — `Done`'s definition and the `v1:<action>:<base64-id>` codec this
slice's tests decode to verify a button belongs to the right task), §7.2 (unit vs. integration
split), §7.3 (assertion standard — count, recipient, exact text, exact buttons), §7.4 (required
scenarios), §12.1 (XML docs), §12.5 (primary constructors), §12.6 (no emoji).

**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — the F10 entry (line 597, "Store
the parsed task and reply — observable"), the §4 "Deferred model properties" table (line 719), and
line 578's "`Notes` and `Priority` are absent from the schema entirely, deferred to F10 and F12" —
all three wrong about `Notes` since F10-1's Decision 7, corrected by this slice per Decision 9,
below. GitHub issues #27 ("An undated task is stored but can never fire") and #28 ("Nothing handles
the model naming a tool that does not exist") are both named as F10-3's to close or confront;
Decisions 2 and 10 do so.

---

## How this slice fits F10

F10-1's own plan estimated this slice at roughly 200-250 lines, reasoned from `MessageHandler.cs`
(60 lines then) growing two dependencies and a reply-building branch (~65), `TelegramListenerTests.cs`
(162 lines then) gaining the full-loop test plus two more scenarios (~140), and a small addition to
`ILocalTimeResolver.cs`/`LocalTimeResolver.cs`/`LocalTimeResolverTests.cs` for `ToLocal` (~20).

That estimate could not anticipate two things that only became visible once this slice's own
decisions were actually confronted: **issue #28** (Decision 2, below) adds a fourth `ErrorCode`
member and a fourth reply sentence neither F10-1 nor F10-2 accounted for, and **the test-scenario
redesign** (Decision 8) turned out to need six failure-mapping cases, not "two more scenarios," once
F10-2's three new `ErrorCode` members and this slice's own new one all needed a sentence proven at
the listener level. Both are argued in full below.

**The measured total, drafted in full and counted, not estimated from an analogue** — the same
discipline F10-1's own Decision 9 and F10-2's own sizing both used. Every file this slice touches
was written out completely (see "Steps," below) and diffed against the working tree with
`git diff --no-index --numstat`:

| File | + | - |
| :--- | ---: | ---: |
| `src/Assistant.Contracts/ErrorCode.cs` | 5 | 0 |
| `src/Assistant.Interfaces/ILocalTimeResolver.cs` | 13 | 0 |
| `src/Assistant.Impl/Services/LocalTimeResolver.cs` | 3 | 0 |
| `src/Assistant.Impl/Telegram/MessageHandler.cs` | 84 | 13 |
| `src/Assistant.Impl/ImplServiceCollectionExtensions.cs` | 6 | 2 |
| `tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs` | 18 | 0 |
| `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs` | 114 | 12 |
| **Code subtotal** | **243** | **27** |
| `docs/design/2026-08-22-slice-1-feature-backlog.md` | 20 | 5 |
| **Total** | **263** | **32** |

**Total changed lines: 295** (263 insertions + 32 deletions), comfortably under the 1000-line
budget. Measured against F10-1's own 200-250 estimate: this lands **within** the estimate if
counted the way F10-1's own slice was measured — insertions only, 263, since F10-1 had no
deletions to weigh that choice against — and **modestly above** it, at 295, if every changed line
counts, which is the more literal reading of "1000 changed lines." Unlike F10-2, which measured
383 against a 225-275 estimate and said so plainly, this slice's overshoot is small and its
insertions-only figure does not overshoot at all. The backlog correction is included in the total
because, unlike a plan document, it merges inside this feature's own pull request rather than as a
separate docs-only PR (see "Commit," below and F10-1's own Global Constraints for that
distinction).

**Running total across F10:** 120 (F10-1, measured) + 383 (F10-2, measured) + 295 (F10-3, measured)
= 798 lines across three pull requests, against three separate 1000-line budgets, not one — each
PR is independently under budget regardless of the sum.

---

## Global Constraints

Every constraint the project's prior plans carry forward still applies here:

- `net10.0`; nullable enabled; warnings are errors; `CS1591`/`CS1573` are errors everywhere.
- **Every class taking arguments uses a primary constructor.** `MessageHandler` gains three more
  primary-constructor parameters this slice: `IEnumerable<IAssistantTool> tools`,
  `ILocalTimeResolver clock`, `ILogger<MessageHandler> logger` — six in total. No separate
  constructor is declared.
- Every enum's first member is `Unknown`, with no explicit numeric values, new members appended
  never inserted. **This slice appends exactly one `ErrorCode` member**, `ModelNamedUnknownTool`,
  after the existing last member, `DueTimeUnparseable` — confirmed by reading the diff: the new
  line sits inside the enum's closing brace, nothing is inserted between existing members, and
  `Unknown` remains first.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first, matched in every new
  assertion below. No Shouldly, no FluentAssertions.
- Every `<summary>` spans three lines: open tag, text, close tag. Test summaries are Gherkin
  (`When` / `And` / `Then`), one clause per line.
- Central package management; no inline `Version=`. Not exercised this slice — no package changes.
- No emoji anywhere: source, tests, docs, or commit messages.
- **Never run `docker compose down -v`.** Use `docker compose -f compose.test.yaml down` with no
  flags. This slice needs no teardown-and-rebuild step — no schema change, no container image
  change.
- Integration tests need `docker compose -f compose.test.yaml up -d` first — **no `--build`**:
  this slice does not touch `tests/Assistant.WireMock/`.
- PR budget: 1000 changed lines per PR, excluding the plan document (which merges on its own,
  docs-only). This slice measures at 295 lines by the convention above — comfortably under budget.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
  ```

---

## Verified facts this plan rests on

Every fact below was read from the working tree at `6bc52e3` (HEAD of `main`, F10-2's own merge
commit) directly, or produced by a command actually run during this planning session — nothing is
carried forward unverified from F10-1's or F10-2's own "verified facts," and two of F10-1's
inherited statements turned out to be exactly the kind of thing worth re-checking (see the
callouts below).

- **`src/Assistant.Impl/Telegram/MessageHandler.cs` is 59 lines today.** Primary constructor
  `MessageHandler(TelegramSettings settings, INotifier notifier, IAiClient ai)`, three
  `private const string` fields (`Unreachable`, `ToolCallNotActedOnYet`, `NotUnderstoodAsATask`),
  and a single `HandleAsync` body: call `ai.AskAsync`, map the `Result<ToolCall>` through one
  `switch` expression, call `notifier.SendAsync`. It is registered scoped
  (`ImplServiceCollectionExtensions.cs:84`, `services.AddScoped<ITelegramUpdateHandler,
  MessageHandler>();`, inside `AddAssistantListener`), consistent with `docs/tech-debt.md`'s
  "Resolved at F6-2" note: plain constructor injection, no `IServiceScopeFactory`, because
  `TelegramListener.DispatchAsync` already opens the per-update scope. `ToolCallNotActedOnYet` is
  what a real phone shows today for every message that produces a tool call.
- **`src/Assistant.Interfaces/INotifier.cs` declares exactly three members**, all read in full:
  `SendAsync(string text, CancellationToken ct)`; `SendTaskAsync(Guid taskId, string text,
  CancellationToken ct)`, whose own doc comment states "There is no overload that accepts a subset
  of actions, because no caller needs one"; and `MarkCompletedTaskAsync(int messageId, string text,
  CancellationToken ct)`. `TelegramNotifier.SendTaskAsync` (`src/Assistant.Impl/Telegram/TelegramNotifier.cs:55-65`)
  attaches exactly the catalogue's `Done` button, built directly rather than by iterating
  `TaskActions.All`, with callback data `CallbackCodec.Encode(TaskActions.Done.Key, taskId)`.
- **`src/Assistant.Interfaces/ILocalTimeResolver.cs` carries exactly three members today**:
  `CurrentLocalTime`, `ZoneId`, and `Resolve(string local)` — **not** `Resolve(DateTime local)`.
  This is the first of two places F10-1's own inherited text is now stale: F10-1's Decision 10 was
  written against `Resolve(DateTime local)`; F10-2 changed the parameter to `string` so the
  resolver could parse the model's raw text itself (F10-2 Decision 3). Decision 4, below, confirms
  this changes nothing about `ToLocal`'s own shape — the two members solve opposite-direction
  problems that share a zone, not a parsing path — but the fact itself needed re-verifying rather
  than assumed, and it was stale exactly where the task brief predicted it might be.
- **`src/Assistant.Impl/Services/LocalTimeResolver.cs`'s primary constructor is unchanged since
  F8**: `LocalTimeResolver(TimeZoneInfo zone, TimeProvider timeProvider)`. `CurrentLocalTime` is
  still exactly `TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone)`. Both facts matter
  directly to Decision 4.
- **`src/Assistant.Contracts/ErrorCode.cs` has exactly twelve members today, in this order**:
  `Unknown`, `TaskNotFound`, `DueTimeMissing`, `DueTimeInPast`, `DueTimeTooFarAhead`,
  `ModelUnavailable`, `ModelReturnedNoAnswer`, `ModelReturnedNoToolCall`, `TaskAlreadyCompleted`,
  `ToolArgumentsMalformed`, `ToolArgumentMissing`, `DueTimeUnparseable`. The last three are F10-2's;
  none of the nine has a `MessageHandler` branch yet, because `MessageHandler` has never dispatched
  to a tool.
- **`src/Assistant.Models/ReminderTask.cs` has exactly eight properties**: `Id`, `Title`, `Status`,
  `DueAt`, `ReminderSentAt`, `CreatedAt`, `UpdatedAt`, `CompletedAt` (returned at F6-1). **Still no
  `Notes`.** This is the second place a carried-forward assumption needed re-checking: F10-1's own
  Decision 7 already argued `Notes` should not ship in F10, and this reading confirms nothing since
  has added it — the backlog's contradicting claim is exactly as wrong today as F10-1 found it.
- **`src/Assistant.Interfaces/IAssistantTool.cs` has exactly four members**: `Name`, `Description`,
  `ParametersJsonSchema`, and `Task<Result<ReminderTask>> ExecuteAsync(string argumentsJson,
  CancellationToken ct)` — F10-2's addition, exactly as Ruling A specified. `CreateTaskTool`
  (`src/Assistant.Impl/Tools/CreateTaskTool.cs`) is its one registered implementation, primary
  constructor `CreateTaskTool(ITaskService taskService, ILocalTimeResolver clock)`, registered
  scoped at `ImplServiceCollectionExtensions.cs:132` inside `AddAssistantAi`.
- **`src/Assistant.Impl/ImplServiceCollectionExtensions.cs` registers, and this slice needs no new
  registration line anywhere:** `TimeProvider.System` singleton and `ITaskService` scoped
  (`AddAssistantServices`, lines 52-53); `TimeZoneInfo` singleton and `ILocalTimeResolver` singleton
  (`AddAssistantTime`, lines 107-108); `AiSettings`/`SystemPrompt` singleton, `IAssistantTool`/
  `CreateTaskTool` scoped, `IAiApi` via Refit, `IAiClient`/`AiClient` scoped (`AddAssistantAi`,
  lines 130-147); `MessageHandler`/`CallbackRouter`/`DoneAction` scoped, `TelegramListener` hosted
  (`AddAssistantListener`, lines 84-87). `src/Assistant.Worker/Program.cs` already chains
  `AddAssistantTime` and `AddAssistantAi` **before** `AddAssistantListener`, so every dependency
  `MessageHandler` gains this slice (`IEnumerable<IAssistantTool>`, `ILocalTimeResolver`,
  `ILogger<MessageHandler>`, the last auto-registered by `Host.CreateApplicationBuilder`) is
  already resolvable in production with zero DI changes. A scoped `MessageHandler` depending on a
  scoped `ITaskService`-shaped collection and a singleton `ILocalTimeResolver` has no captive-
  dependency direction — the unsafe direction (a singleton capturing a scoped dependency) does not
  occur.
- **A dependency-graph regression was checked for and ruled out, the same audit F10-2 ran when it
  discovered the `AiClientTests` break.** `CallbackRouterTests`
  (`tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs`) also starts the real
  `TelegramListener` and therefore also triggers `TelegramListener.DispatchAsync`'s
  `GetServices<ITelegramUpdateHandler>()` call, which constructs every registered handler —
  including `MessageHandler` — regardless of which update type is actually being dispatched.
  Reading its `InitializeAsync` in full: it already calls `AddAssistantTelegram`, `AddAssistantTime`,
  `AddAssistantAi`, `AddAssistantListener`, and `services.AddLogging();` — identical composition to
  `TelegramListenerTests`. `MessageHandler`'s three new dependencies are therefore already
  satisfiable in that container today, with **no `CallbackRouterTests.cs` change needed**.
  `DueReminderJobTests.cs` and `CreateTaskToolTests.cs` never call `AddAssistantListener()`, so
  `MessageHandler` is never constructed in either and neither is at risk.
- **`tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs` is 162 lines and does
  not reset Postgres.** `InitializeAsync` (lines 35-56) calls `services.AddAssistantServices()`
  (registering the real `TimeProvider.System`, never overridden) and, at line 54, only
  `await wireMock.ResetAsync();` — no `postgres.ResetAsync()` anywhere in the file. `SeedAiToolCallAsync`
  is called once at line 55 with `"""{"title":"call the bank"}"""` — no due time — so every one of
  today's four tests exercises the branch this slice is about to turn into "saved with no
  reminder," not the branch the backlog's own canonical scenario names.
- **`tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs:66-67` and
  `tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs:43-44` both call
  `postgres.ResetAsync()` immediately before `wireMock.ResetAsync()`**, confirmed by direct
  `grep -n`. Both are the analogous suites that write rows through the real `TelegramListener`
  (`CallbackRouterTests`) or a real job (`DueReminderJobTests`), and both reset both fixtures.
  `TelegramListenerTests` is the one member of this family that does not, because until this slice
  it never wrote anything.
- **`tests/Assistant.IntegrationTests/Tools/CreateTaskToolTests.cs` fixes "now" with a
  `FakeTimeProvider`**, registered after `AddAssistantServices()`:
  `services.AddSingleton<TimeProvider>(new FakeTimeProvider(AsOf));` where
  `AsOf = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero)` — the identical convention
  `TaskServiceTests` (F10-1) already established. Its happy-path test resolves
  `"2026-08-26T10:00:00"` local (Jerusalem, `+3` in August) to `2026-08-26T07:00:00Z`. This is the
  exact literal Decision 8's worked example and this slice's own new tests reuse, so the resolved
  instant and the "Wednesday 26 August 2026" wording are independently confirmed, not invented for
  this plan.
- **`src/Assistant.Impl/Telegram/CallbackRouter.cs` is the direct precedent Decision 1 restates**:
  `var action = actions.FirstOrDefault(a => a.Definition.Key == actionKey);` (line 91), an inline
  lookup against `IEnumerable<ITaskAction>` with no dispatcher class, inside the one handler that
  needs it.
- **`src/Assistant.Impl/Services/Jobs/DueReminderJob.cs` sends the bare title**:
  `await notifier.SendTaskAsync(task.Id, task.Title, ct);` (line 26) — confirming Decision 6's
  claim that the reminder-fired message and this slice's capture confirmation intentionally no
  longer share one rendering rule (F10-1 Decision 8, Ruling E).
- **`src/Assistant.Impl/Ai/SystemPrompt.cs:25`** uses the exact format string this slice reuses:
  `clock.CurrentLocalTime.ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture)`.
- **Baseline test counts, run directly, not assumed:** `dotnet build --no-restore` across the
  whole solution — `Build succeeded. 0 Warning(s). 0 Error(s).` `dotnet test
  tests/Assistant.UnitTests --no-build` — **56 passed**, 0 failed. With
  `docker compose -f compose.test.yaml up -d`, `dotnet test tests/Assistant.IntegrationTests
  --no-build` — **61 passed**, 0 failed. Matches F10-2's own reported final state exactly.
  Containers were then stopped with `docker compose -f compose.test.yaml down` (no `-v`, per the
  hard safety rule).
- **GitHub issues #27 and #28, read in full via `gh issue view`.** #27 ("An undated task is stored
  but can never fire") already states it was "Settled while planning F10 (see PR #26, Decision 6)"
  and explicitly assigns the reply-wording half to F10-3. #28 ("Nothing handles the model naming a
  tool that does not exist") states the same three open questions this plan's Decision 2 answers,
  verbatim, and assigns the whole issue to F10-3.
- **`docs/e2e-local.md:265`**: "no agent may run the worker against real Telegram (it needs a real
  bot token and sends a real [message])" — confirmed by direct read. This plan does not run the
  worker against real Telegram at any point, and its Steps do not ask a future implementer to
  either; the owner's own real-phone verification is named explicitly in Decision 9 as a
  precondition this plan cannot itself satisfy.

---

## Inherited context: what this slice reads from earlier features

`ITaskService.CreateAsync` and `ReminderTaskMappingExtensions.ToModel` (F10-1) are consumed only
indirectly, through `IAssistantTool.ExecuteAsync` — this slice never calls either directly and
neither file appears in this slice's diff. `IAssistantTool.ExecuteAsync`, `CreateTaskTool`, and the
three F10-2 `ErrorCode` members (`ToolArgumentsMalformed`, `ToolArgumentMissing`,
`DueTimeUnparseable`) are consumed as-is; none is modified. `INotifier`/`TelegramNotifier`
(F6-3) are consumed as-is — `SendTaskAsync`'s signature already takes an arbitrary `text` body, so
nothing about the interface changes; only the string `MessageHandler` builds and passes to it grows
richer. `CallbackCodec`/`TaskActions` (F6-2/F6-3) are read only by this slice's own new tests, to
decode and verify a button's callback data, never by production code this slice adds.
`TelegramListenerTests`'s own fixture shape (F7, extended at F9a/F9b) is extended in place, not
replaced. F10-1's Decisions 4, 5, 6, 8, 9, and 10 are executed here, not re-litigated — each is
restated with its ruling number where the task brief requires it, and the two places this plan
disagrees with or updates an inherited assumption are called out explicitly in "Verified facts,"
above, and in Decision 4, below.

---

## Decisions

### 1. Tool dispatch inside `MessageHandler` — inherited, Ruling B, not re-litigated

**Decision, exactly as F10-1 recorded it (Ruling B — owner-confirmed):** matching `ToolCall.Name`
to the right `IAssistantTool` happens inline inside `MessageHandler`, which already injects
`IAiClient` and now also injects `IEnumerable<IAssistantTool> tools`:

```csharp
var tool = tools.FirstOrDefault(t => t.Name == toolCall.Name);
```

No new interface, no `IToolDispatcher`, no new class.

**Why, restated rather than re-argued:** this repository already has the identical shape for the
identical kind of problem, and it predates this plan. `CallbackRouter` (F6-2) matches an inbound
key against `IEnumerable<ITaskAction>` the same way — `actions.FirstOrDefault(a =>
a.Definition.Key == actionKey)`, verified at `CallbackRouter.cs:91` — inline, inside the one
handler that needs it. Adding `list_tasks`, `update_task`, `complete_task` at F11 needs no change
to this matching line, only new `IAssistantTool` classes and their registrations. F10-1's own
Decision 4 already rejected a dedicated `ToolDispatcher` class on the grounds that this project has
already tried and reversed the equivalent move once (F7's `OwnerOnlyUpdateHandler`, extracted for
a single caller and deleted). This plan adopts that argument as settled and does not reopen it.

**What F10-1 left open here, and what this plan does with it:** F10-1's Decision 4 named, but
explicitly declined to answer, what happens when `tools.FirstOrDefault` returns `null` — "that is
F10-3's decision to make, once it is the slice actually writing `MessageHandler`'s dispatch
branch." Decision 2, immediately below, is that answer.

### 2. Issue #28 — the model names a tool that does not exist

Three things needed deciding, in the issue's own words.

**A named failure, appended to `ErrorCode`.** A new member, `ModelNamedUnknownTool`, appended
after `DueTimeUnparseable` (the current last member) — thirteenth in the enum, `Unknown` still
first, nothing inserted. It sits apart from every existing member in one structural way worth
naming: it is the first `ErrorCode` value that no `IAssistantTool.ExecuteAsync` implementation
ever returns. It is synthesized by `MessageHandler` itself, before any tool is ever reached, so
that the reply-building logic below can treat "no tool matched" and "the matched tool failed" as
one uniform case rather than two:

```csharp
var outcome = tool is null
    ? Result<ReminderTask>.Failure(ErrorCode.ModelNamedUnknownTool)
    : await tool.ExecuteAsync(toolCall.ArgumentsJson, ct);
```

**Alternative considered and rejected: give `MessageHandler` a second, separate branch for "no
tool matched" instead of folding it into the same `Result<ReminderTask>` shape `ExecuteAsync`
already returns.** This would need its own `if` block ahead of the dispatch, with its own call to
`notifier.SendAsync` and its own `return` — duplicating the "send a plain-text failure, no button"
shape the switch below already handles for five other codes. Synthesizing a `Result<ReminderTask>.Failure`
locally costs one ternary and buys a single reply-building path with no special case.

**What the owner sees.** Silence is wrong — the issue's own words, "the model believed it was
acting," are the right frame: the owner sent a message, the model answered with what it believed
was a real action, and returning nothing would look like the message vanished. The owner sees a
plain sentence with no button (see Decision 3's table), sent through `notifier.SendAsync`, the
same channel every other tool-call failure uses.

**Whether the unrecognised name is worth logging, argued rather than waved through.** Yes, but
narrowly: `MessageHandler` gains `ILogger<MessageHandler> logger` as a sixth primary-constructor
parameter (auto-registered by `Host.CreateApplicationBuilder` in production; already explicitly
registered via `services.AddLogging()` in every integration-test fixture that composes
`AddAssistantListener`, verified above), and logs exactly the tool name, at `LogWarning`:

```csharp
if (tool is null)
{
    logger.LogWarning("The chat model called an unregistered tool {Tool}.", toolCall.Name);
}
```

The case for logging: this is genuinely useful, low-cost diagnostic signal, and becomes more
useful, not less, as F11 adds tools with names similar enough to confuse (the issue's own example:
`update_task` vs. `complete_task`). The case against: the repository is public, and any new log
statement is one more thing a future edit could carelessly widen into logging something it should
not. Weighing them honestly rather than picking the safer-sounding answer and moving on: a tool
*name* carries no risk of the kind the issue is worried about. It is drawn from a small,
code-reviewed vocabulary the model chooses from (`create_task`, and whatever F11 adds) — it cannot
contain a task title, because a title is never part of a tool's *name*, only its *arguments*.
`toolCall.ArgumentsJson` is deliberately **not** logged here, and this branch is the one place in
this call graph that could tempt someone to add it "for context" later; this decision is recorded
so that temptation is refused with an argument already on record, not refused from scratch each
time. Worth stating plainly rather than glossing over: `AiClient.AskAsync`
(`src/Assistant.Impl/Ai/AiClient.cs:59-62`) already logs the full arguments, including whatever
title they carry, at `LogInformation`, on **every** successful tool call today — a pre-existing
F9b behaviour this decision does not touch, does not endorse extending, and does not need to fix
to make its own narrower choice (name only, no arguments) correct on its own terms.

### 3. The reply text — the full mapping table, and one open question

F10-1's Decision 8 (Ruling E) gives four rows verbatim. Reproduced exactly, not reworded:

| Outcome | Reply |
| :--- | :--- |
| Captured, with a due time | `Call the bank -- due Wednesday 26 August 2026, 10:00.` — title, separator, stored due time rendered in the configured local zone using `ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture)`, the identical format string `SystemPrompt.Build()` already uses |
| Captured, no due time | `Buy milk -- saved with no reminder.` |
| `DueTimeInPast` | `"That time has already passed. What time did you mean?"` |
| `DueTimeTooFarAhead` | `"That is more than two years away, which is probably not what you meant. What time did you mean?"` |

Four more codes now reach `MessageHandler` with no sentence yet: F10-2's `ToolArgumentsMalformed`
and `ToolArgumentMissing`, F10-2's `DueTimeUnparseable`, and this slice's own
`ModelNamedUnknownTool` (Decision 2). Each is argued below.

**`ToolArgumentMissing` — `"I did not catch what to call that. What should I call it?"`** This is
the one new-code sentence that behaves like the two Ruling-C sentences above: it invites the owner
to restate the one specific thing that was missing (a title), because `create_task`'s schema names
`title` as its only required field and F10-2's own Decision 1 already collapsed absent, empty, and
whitespace-only title into this one code on the grounds that all three read identically to the
user. The reply honours that same reasoning at the sentence level.

**`ToolArgumentsMalformed` and `ModelNamedUnknownTool` share one sentence —
`"Something went wrong on my end. Send that again in a moment."`** Both are wire-level or
model-level failures the user cannot have caused and cannot usefully be asked to correct: a
malformed arguments payload is the model's own JSON, not the owner's words, and a call to a tool
that does not exist is the model inventing something to do, not the owner mis-stating a task.
F10-2's own Decision 2 already ran an honest test for exactly this question — "would F10-3 say
something genuinely different to the owner for each code?" — and concluded that
`ToolArgumentsMalformed` "might be closer to a generic 'something went wrong, try again' or a
message that never reaches the user's specific words at all." Applying that test to
`ModelNamedUnknownTool` gives the identical answer for the identical reason, so this plan gives
them the identical sentence rather than inventing two near-duplicate "something broke" strings.
The wording deliberately echoes, without repeating verbatim, the existing `Unreachable` constant
("I could not reach the model just now. Send that again in a moment.") — related in tone (not the
owner's fault, try again), distinct in text (so a reader of Serilog output, or a future test
failure, can tell which one fired).

**The open question this plan confronts and does not quietly resolve: does `DueTimeUnparseable`
deserve its own sentence, or should it share the "restate your time" sentence with `DueTimeInPast`
and `DueTimeTooFarAhead`?**

F10-2's own Decision 2 already conceded this is the weak half of its three-way split: "all three
mean 'your time did not take, restate it,'" and reusing `DueTimeInPast`'s existing code "could
plausibly have served F10-3 identically." Arguing both sides honestly:

*For sharing* — fewer near-identical strings, and F10-2's own author would, by their own account,
"have proposed two new codes, not three," letting `DueTimeUnparseable` fold into whichever
due-time sentence F10-3 wrote first.

*For a distinct third sentence* — and this is where this plan lands, but flags rather than
buries the choice — reusing either of the two verbatim Ruling-C sentences would sometimes be
**wrong**, not merely repetitive. `DueTimeInPast`'s sentence, `"That time has already passed,"`
asserts a specific fact: the resolver understood the time and judged it to be behind now. That is
not what happened when text fails to parse at all — F10-2's own Decision 3 found that the single
most likely real-world cause of `DueTimeUnparseable` is the model deviating from its own
instructed format (an embedded offset or a trailing `Z`), which has nothing to do with whether the
intended time was actually in the past. Telling the owner their time "has already passed" when the
real problem is a stray `Z` the model appended is a false diagnosis, not just an inexact one, and
`DueTimeTooFarAhead`'s sentence is equally inapplicable for the same reason. This plan's proposed
text: **`"I could not make sense of that time. What time did you mean?"`** — distinct from both
verbatim sentences, but ending in the identical clause, so all three due-time failures read as one
family without one of them making a claim it cannot back up.

**This is marked OPEN — awaiting the owner's ruling, the same way F10-1's Decisions 3, 5, and 8
were marked before Rulings A, C, and E settled them.** A reviewer can overturn this in one line: if
the owner prefers sharing `DueTimeInPast`'s sentence (accepting the occasional false "already
passed" diagnosis as a cost worth paying for one fewer string), Step 8 below changes one switch arm
and this plan's proposed `DueTimeUnparseableReply` constant is deleted. Nothing else in this slice
depends on which way this is decided.

### 4. `ILocalTimeResolver.ToLocal(DateTimeOffset utc)`

**Confirmed against the code as it stands today, not carried forward unread.** F10-1's Decision 10
specified a new interface member returning a bare `DateTimeOffset` (no `Result<T>` — there is no
guard clause going UTC-to-local), implemented as `TimeZoneInfo.ConvertTime(utc, zone)` reusing the
existing `zone` field. F10-2 changed `Resolve`'s parameter from `DateTime` to `string` between when
Decision 10 was written and today (see "Verified facts," above) — the one place this plan was
explicitly warned an inherited fact might now be stale. Checked directly: `LocalTimeResolver`'s
primary constructor (`TimeZoneInfo zone, TimeProvider timeProvider`) is unchanged, and
`CurrentLocalTime`'s body is still exactly `TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(),
zone)`. **Decision 10 is not stale.** `Resolve`'s new parsing step operates entirely before its own
existing DST/guard-clause logic, which is untouched; `ToLocal` shares the `zone` field with both
`Resolve` and `CurrentLocalTime` but shares no code path with `Resolve`'s new `TryParseExact` call
at all — the two members solve opposite-direction problems (wall-clock text to instant; instant
back to wall-clock reading) that happen to share one zone, not a parsing path. This independently
confirms F10-2's own Decision 9 ("Conclusion: none"), reached by inspecting the actual diff rather
than re-deriving the argument from F10-1's text alone.

**One further point worth stating, since it strengthens "no guard clause" beyond F10-1's original
argument.** F10-1 argued no guard clause is needed because the past/future checks exist to catch a
*misreading of intent*, not to validate an already-resolved instant. There is a second, more
structural reason: converting a `DateTimeOffset` (an unambiguous point in time) to a wall-clock
reading in a target zone is a **function** — exactly one reading and offset exists for any given
instant, by definition. Ambiguity and gaps are exclusively a wall-clock-to-instant problem (a
fall-back hour maps to two instants; a spring-forward gap maps to zero), never an instant-to-
wall-clock one. `ToLocal` cannot fail not merely because nothing calls for a guard, but because
there is no such thing as an instant with more than one correct local reading.

**Test coverage — exactly one unit test earns a place, and no more.** Per spec §7.2's own rule (a
pure function with no side effect is a unit-test candidate; a combinatorial table earns a
`[Theory]`, everything else does not), `ToLocal` gets a single `[Fact]`,
`ToLocal_AnyInstant_ReturnsTheWallClockReadingInTheConfiguredZone`, mirroring the shape of the
existing single-case `CurrentLocalTime_AnyInstant_CarriesTheZonesOffsetAtThatInstant` test — the
only other test in the file covering a one-line `TimeZoneInfo.ConvertTime` wrapper with no
branches. A seasonal or DST-crossing `[Theory]`, the shape `Resolve`'s own tests use, is **not**
warranted: that combinatorial complexity in `Resolve` comes from parsing, from `IsAmbiguousTime`/
`GetAmbiguousTimeOffsets` branching, and from the past/future guards — `ToLocal` has none of the
three, one code path, and `TimeZoneInfo.ConvertTime`'s own DST correctness is already exhaustively
proven, in this exact codebase, by `Resolve`'s spring-forward and fall-back tests exercising the
identical BCL zone data in the opposite direction. A second battery of DST cases for `ToLocal`
would duplicate coverage spec §7.2 already forbids duplicating. The integration level separately
proves `ToLocal` is wired correctly in production — Decision 8's new
`Listener_OwnerSendsAMessageWithADueTime...` test asserts the exact rendered string
`"call the bank -- due Wednesday 26 August 2026, 10:00."`, which cannot pass unless `MessageHandler`
calls `ToLocal` with the right instant and unless `ToLocal` itself returns the right reading. Unit
and integration together cover the pure logic and the wiring, once each, with nothing repeated.

### 5. Where the reply strings live — consts inside `MessageHandler`, not a renderer class

`MessageHandler` grows from three `private const string` fields to seven, plus a formatting call.
Two options were weighed.

**Decision: keep them as `private const string` fields inside `MessageHandler`, plus one
`DueTimeFormat` const for the shared format string.** No `ReplyRenderer` or similar class is
created.

**Argued against extraction, on this project's own recorded terms rather than a generic
preference.** This project has already tried the equivalent extraction once and reversed it: F7's
own "Settled at F7" record describes introducing then deleting `OwnerOnlyUpdateHandler`, built for
a single caller, on the grounds that "an abstraction with one implementation is a guess" — F10-1's
own Decision 4 and F10-2's own Decision 6 both already cited this same precedent for the same
reason. A `ReplyRenderer` here would have exactly one call site (`MessageHandler`) and exactly one
implementation, the identical shape. More directly: this codebase already has a **living
counter-example of the same problem solved the same way**. `CallbackRouter` — dispatch an inbound
key, map a handful of failure codes to fixed sentences, send a reply — is 116 lines
(`src/Assistant.Impl/Telegram/CallbackRouter.cs`) and inlines its own three-constant,
`result switch`-based sentence map with no extracted renderer. `MessageHandler` after this slice is
doing the same shape of work `CallbackRouter` already does, one branch layer deeper (a model call
before the dispatch, not just the dispatch itself), and this project has not asked `CallbackRouter`
to extract its own mapping, for the same YAGNI reason.

**The measured size, either way, stated rather than guessed.** `MessageHandler.cs` grows from 59 to
130 lines with everything inline (measured by drafting the full file, see "How this slice fits
F10"). Extracting a renderer would not shrink the total: the seven constants and the
`ErrorCode -> string` switch would simply move into a new file, which then needs its own
class-level `<summary>`, its own DI registration (or a `static` class, avoiding registration but
then needing no constructor-injected state at all — moot here, since nothing it would hold is
injected), and its own file header — properties `MessageHandler.cs`'s existing consts do not carry
today because they live inside an already-registered class. The extraction would cost lines rather
than save them, for zero testability gain (Decision 4 already established the reply text is proven
by the same integration tests either way) and zero reuse (nothing else in this codebase renders a
capture reply). 130 lines is not large by this project's own standard: `ILocalTimeResolver.cs` is
69 lines of interface alone, and `CallbackRouter.cs`, doing the analogous job, is 116.

### 6. Which `INotifier` method each branch calls

**Confirmed against the actual interface, read in full (see "Verified facts"):** `INotifier` has
`SendAsync(text, ct)`, `SendTaskAsync(Guid taskId, text, ct)` — which attaches whatever buttons its
channel offers, with no overload for a subset — and `MarkCompletedTaskAsync`, untouched by this
slice.

**Decision, exactly as F10-1's Decision 8 (Ruling E) specified:** both captured-task branches —
with a due time and without one — call `SendTaskAsync`, attaching the Done button in both cases.
Every failure branch (the four `ErrorCode`-mapped sentences plus the two upstream model-call
failures) calls plain `SendAsync`.

**Why a failure carries no button, argued rather than assumed.** `SendTaskAsync`'s own contract
requires a real `taskId` — "The task the message announces" — and every failure path in this
slice's design is, by construction, a path on which `ITaskService.CreateAsync` was never reached:
Decision 2's `ModelNamedUnknownTool` branch never calls a tool at all, and every `ErrorCode`
`CreateTaskTool.ExecuteAsync` can return (`ToolArgumentsMalformed`, `ToolArgumentMissing`, the
three due-time codes) is returned specifically **before** `taskService.CreateAsync` is called
(F10-2's own `ExecuteAsync` body, read in full: every guard is an early `return` ahead of the
final `await taskService.CreateAsync(...)` line). There is structurally no task identifier to give
`SendTaskAsync` on any failure path — not a stylistic choice to omit the button, but the absence of
the one argument the method requires.

### 7. The `TelegramListenerTests` database-reset gap — a real defect this slice creates, fixed in scope

**Verified, not assumed.** `TelegramListenerTests.cs:54` calls `wireMock.ResetAsync()` and nothing
else — no `postgres.ResetAsync()` anywhere in the file. Today that is harmless: `MessageHandler`
writes nothing, so there is nothing for a missing reset to leak between tests. After this slice
ships, every test in the class that reaches the success path writes a row, into
`assistant_test`'s `reminder_tasks` table, shared across the whole `integration` xUnit collection —
and with no reset, those rows persist into whatever test runs next in the same collection, which
could see stray rows from a prior test's data (most concretely: this slice's own new
`_repository.GetDueRemindersAsync(AsOf.AddYears(10), NoLimit, ct)` assertion, checking that exactly
one due task exists, would silently start failing or silently start passing for the wrong reason
the moment a second test in the same collection also leaves a row behind).

**Compared against the two analogous suites, which already do this correctly.**
`CallbackRouterTests.cs:66-67` and `DueReminderJobTests.cs:43-44` both call `postgres.ResetAsync()`
immediately before `wireMock.ResetAsync()` — both are suites that write rows through a real
component (the listener's callback path; the due-reminder job) and share the same database and the
same collection `TelegramListenerTests` does.

**Decision: fix it as part of this slice's own scope, not deferred.** Add
`await postgres.ResetAsync();` to `TelegramListenerTests.InitializeAsync`, in the same position
(immediately before `wireMock.ResetAsync()`) the other two suites already use. This is not a
"nice to have while we're in the file" — it is the direct, immediate consequence of this exact
slice giving the listener something to write for the first time, so it belongs to this slice's own
diff rather than a follow-up ticket that would leave the suite silently unreliable in the interim.

### 8. Test scenarios, their placement, and the moving-target hazard

**What each existing test becomes.**

- `Listener_OwnerSendsAMessage_RepliesThatItUnderstoodTheTask` — asserts the
  `ToolCallNotActedOnYet` placeholder this slice deletes outright. **Replaced** by
  `Listener_OwnerSendsAMessageWithADueTime_StoresItAndRepliesWithTheDueTimeAndADoneButton`, which
  asserts the backlog's own canonical F10 scenario for the first time: the stored row's exact UTC
  due instant, the exact rendered reply text, and the exact Done button (label and callback data,
  decoded and checked against the row's real id).
- `Listener_StrangerSendsAMessage_OnlyTheOwnerIsAnswered` — unaffected. It asserts a message
  *count*, never reply content, so it is untouched by the reply-text rewrite. Its own remarks
  already point at whichever test now carries the exact-text assertion; that cross-reference is
  updated to the new test's name.
- `Listener_MessageAlreadyAnswered_DoesNotAnswerItAgain` — unaffected, same reasoning.
- `Listener_ModelRepliesWithProse_TellsTheOwnerItWasNotReadAsATask` — unaffected. It seeds its own
  `SeedAiAnswerAsync` (prose, no tool call), a path this slice's dispatch logic never touches.

**What is added.** One more `[Fact]` for the no-due-time capture branch
(`Listener_OwnerSendsAMessageWithNoDueTime_RepliesThatNoReminderWillFire`, reseeding the default
tool call with no `due_at_local`), and one `[Theory]` with six rows covering every failure
`MessageHandler` can now produce — the three F10-2 due-time codes, `ToolArgumentMissing`,
`ToolArgumentsMalformed`, and this slice's own `ModelNamedUnknownTool` — asserting the exact reply
text and that no button is attached. Six rows in one `[Theory]` rather than six `[Fact]` methods,
matching `CreateTaskToolTests`' own precedent (`ExecuteAsync_TitleMissingEmptyOrBlank_IsRejected`,
`ExecuteAsync_ArgumentsAreNotAUsableObject_IsRejected`) for grouping rejections that share one
assertion shape. **Whether each row's non-persistence is re-proven here: deliberately not, for
five of the six.** `CreateTaskToolTests` already proves, at the tool level, that none of F10-2's
five codes leaves a row behind — repeating that proof through a full Telegram round trip would
duplicate coverage spec §7.2 forbids. The sixth row, `ModelNamedUnknownTool`, has no such proof
anywhere else and needs none by a different argument: `MessageHandler` only calls
`tool.ExecuteAsync` once a tool has actually been found (see the code in Decision 2), so there is
no code path from an unmatched name to a persisted row at all — checked structurally, the same way
F10-1's Decision 5 argued "before anything is persisted" structurally rather than only by test.

**The moving-target hazard, confronted directly.** `TelegramListenerTests` builds a real container
over the real `TimeProvider.System` (`AddAssistantServices` registers it; nothing in the file
overrides it today), and `LocalTimeResolver.Resolve` refuses anything more than a minute in the
past. A hardcoded due-time literal in this slice's new with-due-time test would work today and
silently start failing the moment real calendar time passes it — not a hypothetical: this plan's
own `"2026-08-26T10:00:00"` literal would flip from "captured with a due time" to "refused as
`DueTimeInPast`" the first time this suite runs after that date, with no code change at fault.

**The same fix `CreateTaskToolTests` (F10-2) already uses applies here, at near-zero cost.** Add
`services.AddSingleton<TimeProvider>(new FakeTimeProvider(AsOf));` immediately after
`AddAssistantServices()`, with the identical `AsOf = new DateTimeOffset(2026, 8, 25, 12, 0, 0,
TimeSpan.Zero)` `TaskServiceTests` and `CreateTaskToolTests` already use — last registration wins
for a singleton service, so this cleanly overrides `AddAssistantServices()`'s own
`TimeProvider.System` registration. The cost is genuinely small: `Assistant.IntegrationTests.csproj`
already references `Microsoft.Extensions.TimeProvider.Testing` (added at F10-2), so this is one
`using Microsoft.Extensions.Time.Testing;`, one field, and one registration line — no new package.
Reusing the exact same `AsOf` instant every other integration suite in this project already
standardises on also means this slice's due-time literals are drawn from the same, already-proven
arithmetic `CreateTaskToolTests` exercises, rather than a fourth independent set of hand-checked
dates.

### 9. Documentation corrections this slice owns

F10-1's Decision 7 (owner Ruling F) settled that `Notes` ships nowhere in F10, and named this slice
as the one that corrects the backlog's three resulting wrong claims — because this is the only one
of F10's three slices that touches the backlog document at all, the same posture F6-1 and F6-2 held
toward F6's own entry before F6-3 alone closed it.

**Location 1 — line 578, inside F9b's own "Settled at F9b" bullet list.** Original:

```
against yet. `Notes` and `Priority` are absent from the schema entirely, deferred to F10 and
F12, the features that give `ReminderTask` somewhere to put them.
```

Corrected:

```
against yet. `Notes` and `Priority` are absent from the schema entirely. `Priority` is deferred
to F12, the feature that gives `ReminderTask` somewhere to put it. `Notes` was deferred to F10
here too, but F10 shipped without it (F10-1 Decision 7) -- it remains unscheduled, returning with
whichever future feature first writes a test that needs it. Corrected at F10-3.
```

**Location 2 — lines 597-602, the F10 entry itself.** Original:

```
**F10 · Store the parsed task and reply · observable** — spec §5.1
`ITaskService.CreateAsync`, the mapping extension methods, and the reply rendered with its
inline keyboard. `ReminderTask` regains `Notes`, which the capture path is first to write.
*Tests:* "call the bank tomorrow at 10" ends as a row with the right UTC instant and a reply
carrying the right buttons.
**Milestone: the full loop.** Talk to it, get reminded, tap Done.
```

Corrected — the `**done**` suffix and the closing `*Settled at F10-3:*` bullets are added **only
once the owner has verified the full loop on a real phone** (see the caveat immediately below; the
`observable` tag cannot be honestly marked met by this plan alone):

```
**F10 · Store the parsed task and reply · observable** — spec §5.1 · **done**
`ITaskService.CreateAsync`, the mapping extension methods, and the reply rendered with its
inline keyboard. `ReminderTask` does not regain `Notes` -- despite this entry's own original claim
here and the §4 table's matching row (both corrected by this note), no slice of F10 ever wrote a
test that exercises it.
*Tests:* "call the bank tomorrow at 10" ends as a row with the right UTC instant and a reply
carrying the right buttons.
**Milestone: the full loop.** Talk to it, get reminded, tap Done.
*Settled at F10-3:*
- **`Notes` does not ship.** F10-1's Decision 7 found no test anywhere in F10's three slices that
  exercises it, contradicting this entry's own original claim and the §4 table's matching row --
  both corrected above and below.
- **Split across three pull requests**, not one: F10-1 (the writer), F10-2 (the tool executes),
  F10-3 (the reply closes the loop). F10 stayed open, unmet by the `observable` tag, until F10-3
  landed and the owner verified the full loop on a real phone -- the same posture F6 held across
  its own three slices.
- **F10 is closed.** All three pull requests have landed, and the `observable` tag is met:
  talking to the bot ends in a stored row and a reply carrying the Done button, verified by the
  owner on a real phone; see `docs/e2e-local.md`.
```

**Location 3 — line 729, the §4 "Deferred model properties" table.** Original:

```
| `Notes` | F10 |
```

Corrected:

```
| `Notes` | Unscheduled — F10 shipped without it (F10-1 Decision 7); returns with whichever future feature first writes a test that needs it |
```

**Why the `**done**` marker and the closing bullets are conditioned on a real-phone verification
this plan cannot itself perform.** F6's own history is the precedent this decision follows, not
invents: F6-3's plan text — "F6 is closed... `docs/e2e-local.md`'s own 'Walkthrough against real
Telegram' section carries the owner's own manual proof" — was written *after* that proof already
existed, because F6-3's own author had already built the code and the owner had already tapped a
real button. This plan is prospective — written before any of this slice's code exists — and this
task's own hard safety rules forbid running the worker against real Telegram at all
(`docs/e2e-local.md:265`). Marking `**done**` and writing "F10 is closed" here, now, would be
exactly the kind of claim §1's own "Definition of done" rule warns against for an `observable`
feature: "must also be demonstrable on a real phone." The corrected text above is what the
*implementer* writes once that demonstration has actually happened — mirroring F6-3's own
`docs/e2e-local.md` addition of a manual walkthrough step, which this slice's implementer should
add alongside it, the same way F6-3 did. This plan specifies the exact wording; it does not, and
cannot, certify the event the wording describes.

### 10. Issue #27 — an undated task is stored but can never fire

**Honest answer: this slice partially addresses it, and no more.** Issue #27 names two separate
consequences of F10-1's own Decision 6 (an undated task is a valid, permanently-pending row):

1. "The capture reply must say plainly that no reminder will fire... F10-3 owns the wording." This
   slice closes exactly this half: the `"Buy milk -- saved with no reminder."` sentence (Decision
   3's table) makes the gap visible to the owner at the moment of capture, rather than silently.
2. "Until `update_task` exists, an undated task is only reachable through the Done button on its
   capture message. There is no listing." This slice does **nothing** about this half, and issue
   #27 itself already names who does: F11's `update_task`/listing work, not F10-3.

**Making the gap visible is not the same as fixing it, and this plan does not conflate the two.**
An undated task still sits in `reminder_tasks` forever, still never satisfies
`GetDueRemindersAsync`'s `due_at <= @now` filter, and still has no way to be found again except
scrolling chat history for its capture message and tapping Done directly on it. This slice makes
that fact legible to the owner at capture time; it does not give the owner any new way to act on
it later. The remaining gap is F11's to close, exactly as issue #27 already states — this plan
does not reopen that assignment or narrow the issue's own scope, and does not close the issue
itself (this task's own instructions permit reading issues, not closing them).

### 11. Whether this slice fits one PR

Answered in full in "How this slice fits F10," above, with every file drafted completely and
measured by `git diff --no-index --numstat` rather than estimated from an analogue: **243
insertions / 27 deletions across the seven `src`/`tests` files (270 changed), plus 20 insertions /
5 deletions in the backlog correction (25 changed) — 263 insertions / 32 deletions, 295 changed
lines in total.** Comfortably under the 1000-line budget. Measured against F10-1's own 200-250
estimate: within it on an insertions-only basis (263), modestly above it (295) counting every
changed line — said plainly either way, rather than restating only the more flattering figure.

---

## What this slice does NOT include

- **Any change to `ITaskService.CreateAsync`, `ReminderTaskMappingExtensions.ToModel`,
  `TaskService.cs`, `IAssistantTool.ExecuteAsync`, or `CreateTaskTool.cs`.** F10-1's and F10-2's,
  consumed as a single call chain and otherwise untouched — none of the five files appears in this
  slice's diff.
- **Any `ILocalTimeResolver.Resolve` change.** F10-2's, unchanged; this slice only adds `ToLocal`,
  which shares no code path with `Resolve`'s parsing (Decision 4).
- **`ReminderTask.Notes`, any migration, any `CreateTaskRequest` or `create_task` schema change.**
  F10-1's Decision 7 (Ruling F) stands; this slice corrects the backlog's contradictory prose
  rather than fulfilling it. `Priority` is F12's, untouched here.
- **Any `ITaskRepository`/`EfTaskRepository` change.** `GetDueRemindersAsync` and `FindAsync`
  already exist and are exactly what this slice's new tests need to read a stored row back.
- **Any new production DI registration.** Every dependency `MessageHandler` gains this slice is
  already resolvable from the existing composition order in `Program.cs` (see "Verified facts").
  Only `AddAssistantListener`'s own `<remarks>` are updated, to state the requirement that already
  existed silently.
- **`CallbackRouterTests.cs`, `DueReminderJobTests.cs`, `CreateTaskToolTests.cs`, `TaskServiceTests.cs`,
  or `AiClientTests.cs`.** Checked directly (see "Verified facts") and confirmed unaffected —
  `CallbackRouterTests`' container already satisfies `MessageHandler`'s three new dependencies with
  no fixture change, and the other three never construct `MessageHandler` at all.
- **`docs/e2e-local.md`.** This slice's implementer should add a manual real-Telegram walkthrough
  step once the owner verifies the full loop, mirroring F6-3's own precedent — but drafting that
  step is not named in this plan's own scope, and no agent may perform the verification it would
  describe (`docs/e2e-local.md:265`).
- **A second `ErrorCode` for "the arguments parsed but named a field this tool does not
  recognise."** Not reachable today — `CreateTaskRequest`'s deserializer silently ignores unknown
  JSON properties rather than failing — and inventing a code for a case nothing exercises would be
  exactly the anticipatory work spec §1's own YAGNI rule forbids.

---

## File Structure

```
src/Assistant.Contracts/
    ErrorCode.cs                                       + ModelNamedUnknownTool (appended)

src/Assistant.Interfaces/
    ILocalTimeResolver.cs                               + ToLocal(DateTimeOffset utc)

src/Assistant.Impl/
    Services/LocalTimeResolver.cs                       + ToLocal implementation
    Telegram/MessageHandler.cs                          + tool dispatch, + reply rendering
    ImplServiceCollectionExtensions.cs                  AddAssistantListener remarks only

tests/Assistant.UnitTests/
    Services/LocalTimeResolverTests.cs                  + 1 Fact (ToLocal)

tests/Assistant.IntegrationTests/
    Telegram/TelegramListenerTests.cs                   reset fix, FakeTimeProvider, placeholder
                                                         test replaced, +1 Fact, +1 six-case Theory

docs/design/2026-08-22-slice-1-feature-backlog.md       Notes corrections (x2), F10 entry note
                                                         and done marker (implementer applies once
                                                         verified -- see Decision 9)
```

`docs/e2e-local.md` is absent from this list, deliberately — see "What this slice does NOT
include." No `Assistant.Repository`, `Assistant.Worker`, or migration file is touched; this slice
adds no schema and no new production DI registration.

---

## Validation

**Test count arithmetic.** Baseline, run directly (see "Verified facts"): 56 unit, 61 integration.

- Unit: 56 + 1 = **57**. The one new case is `ToLocal_AnyInstant_ReturnsTheWallClockReadingInTheConfiguredZone`,
  appended to `LocalTimeResolverTests.cs`; every other test in that file is unchanged.
- Integration: `TelegramListenerTests.cs` goes from 4 test methods to 5 `[Fact]`s plus one
  6-row `[Theory]` — 11 test cases total, up from 4, a net **+7**. 61 + 7 = **68**. No other
  integration test file changes: `CallbackRouterTests`, `DueReminderJobTests`, `CreateTaskToolTests`,
  `TaskServiceTests`, and `AiClientTests` are all confirmed unaffected (see "Verified facts").

**Expected final state: 57 unit, 68 integration.** These are targets for the implementer to
confirm by actually running the suite — this plan does not claim they were run, because this
plan's own task explicitly forbids writing the code that would make them runnable (`src/` and
`tests/` are out of scope for this document; see the note at the top of this plan's own
instructions). The commands to run them, unchanged from every prior slice:

```bash
docker compose -f compose.test.yaml up -d
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
docker compose -f compose.test.yaml down
```

**This slice is the first that can be validated against real Telegram, and also the one this
plan cannot itself validate that way.** `docs/e2e-local.md:265` and this task's own hard safety
rules both forbid an agent from running the worker against real Telegram — that verification, and
the backlog's `**done**` marker and closing bullets it unlocks (Decision 9), belong to the owner
and to whoever implements this plan, not to this planning document.

---

## Steps

**Decisions this slice carries:** all eleven, given in full above — 1 and 6 restate owner rulings
without change; 3's four Ruling-E rows are reproduced verbatim; 2, 4, 5, 7, 8, 9, 10, and 11 are
this plan's own arguments, and 3's `DueTimeUnparseable` sentence is explicitly OPEN pending the
owner's ruling.

**Consumes:** `IAssistantTool.ExecuteAsync`, `CreateTaskTool`, three F10-2 `ErrorCode` members
(F10-2); `ITaskService.CreateAsync`, `ReminderTaskMappingExtensions.ToModel` (F10-1, indirectly,
through `ExecuteAsync`); `INotifier`/`TelegramNotifier`, `CallbackCodec`, `TaskActions` (F6-2/F6-3);
`TelegramListenerTests`'s own fixture shape (F7-F9b).
**Produces:** `ErrorCode.ModelNamedUnknownTool`; `ILocalTimeResolver.ToLocal`,
`LocalTimeResolver.ToLocal`; `MessageHandler`'s tool dispatch and reply rendering;
`AddAssistantListener`'s corrected `<remarks>`; `TelegramListenerTests`'s reset fix, fixed clock,
and rewritten test suite; the backlog's three corrected claims.

One commit. `MessageHandler`'s dispatch cannot compile without `ErrorCode.ModelNamedUnknownTool`
and without `ILocalTimeResolver.ToLocal`; `TelegramListenerTests`'s new assertions cannot compile
without `MessageHandler`'s new reply text existing to produce them. There is no smaller
independently-buildable unit inside this slice than all of it together — the same reasoning F10-1
and F10-2 both gave for their own single commits.

### Commit 1: the reply closes the loop

**Files:**
- Modify: `src/Assistant.Contracts/ErrorCode.cs`
- Modify: `src/Assistant.Interfaces/ILocalTimeResolver.cs`
- Modify: `src/Assistant.Impl/Services/LocalTimeResolver.cs`
- Modify: `src/Assistant.Impl/Telegram/MessageHandler.cs`
- Modify: `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`
- Modify: `tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs`
- Modify: `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`
- Modify (once verified — see Decision 9): `docs/design/2026-08-22-slice-1-feature-backlog.md`

- [ ] **Step 1: Append `ModelNamedUnknownTool` to `ErrorCode`**

In `src/Assistant.Contracts/ErrorCode.cs`, after `DueTimeUnparseable`'s closing comma:

```csharp

    /// <summary>
    /// The chat model called a tool that is not among those registered.
    /// </summary>
    ModelNamedUnknownTool,
```

- [ ] **Step 2: Add `ToLocal` to `ILocalTimeResolver`**

In `src/Assistant.Interfaces/ILocalTimeResolver.cs`, after `Resolve`'s closing `;`:

```csharp

    /// <summary>
    /// Converts a UTC instant back to the wall-clock reading it names in the configured zone.
    /// </summary>
    /// <param name="utc">The instant to convert.</param>
    /// <returns>The same instant, expressed as a reading in the configured zone.</returns>
    /// <remarks>
    /// The reverse of <see cref="Resolve"/>. There is no guard clause and no failure case: the
    /// past and future checks on <see cref="Resolve"/> exist to catch a misreading of what the
    /// user meant when typing a time, not to validate an instant that has already been resolved
    /// and persisted, so converting it back can never be refused.
    /// </remarks>
    DateTimeOffset ToLocal(DateTimeOffset utc);
```

- [ ] **Step 3: Build and watch it fail**

```bash
dotnet build --no-restore
```

Expected: `LocalTimeResolver` no longer satisfies `ILocalTimeResolver` (`CS0535`, missing
`ToLocal`).

- [ ] **Step 4: Implement `ToLocal` in `LocalTimeResolver`**

In `src/Assistant.Impl/Services/LocalTimeResolver.cs`, after `Resolve`'s closing brace:

```csharp

    /// <inheritdoc/>
    public DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, zone);
```

- [ ] **Step 5: Add the unit test for `ToLocal`**

In `tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs`, append, immediately before the
private `ResolverIn`/`ResolverAt`/`Instant` helpers:

```csharp

    /// <summary>
    /// When a stored UTC instant is converted back to local text
    /// Then it carries the wall-clock reading and offset in force in the configured zone at
    /// that instant.
    /// </summary>
    [Fact]
    public void ToLocal_AnyInstant_ReturnsTheWallClockReadingInTheConfiguredZone()
    {
        // Arrange
        var resolver = ResolverAt("2026-01-01T00:00:00Z");

        // Act
        var local = resolver.ToLocal(Instant("2026-08-26T07:00:00Z"));

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.FromHours(3)), local);
    }
```

- [ ] **Step 6: Build and confirm the unit-test project compiles and passes**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
```

Expected: **57 passed**, 0 failed (56 baseline + 1 new case).

- [ ] **Step 7: Give `MessageHandler` its three new dependencies, tool dispatch, and reply
      rendering**

Replace `src/Assistant.Impl/Telegram/MessageHandler.cs` in full:

```csharp
using System.Globalization;
using Assistant.Contracts;
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Sends the owner's plain-text message to the chat model, carries out the tool call it names,
/// and replies with what was actually stored.
/// </summary>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <param name="notifier">Where the reply is delivered.</param>
/// <param name="ai">Reaches the configured chat model for an answer.</param>
/// <param name="tools">Every registered tool, matched against the model's tool call by name.</param>
/// <param name="clock">Renders a stored due instant back in the configured local zone.</param>
/// <param name="logger">Where a tool call naming an unregistered tool is recorded.</param>
/// <remarks>
/// The owner check lives inline here, on purpose: the assistant serves exactly one person, so
/// there is nothing to route between. Any future handler must apply the same check itself --
/// nothing in <see cref="ITelegramUpdateHandler"/> or <see cref="TelegramListener"/> enforces it.
/// This handler is registered scoped and resolved fresh, inside a scope
/// <see cref="TelegramListener.DispatchAsync"/> opens per update, so every dependency here is
/// injected directly -- there is no captive-dependency concern the way there was when this
/// handler was a singleton.
/// <para>
/// Tool dispatch is a plain lookup against <paramref name="tools"/>, the same shape
/// <c>CallbackRouter</c> already uses to match an inbound key against
/// <c>IEnumerable&lt;ITaskAction&gt;</c>: an inbound name matched against a registered
/// collection, extended by adding a class and a registration, never by editing this method.
/// </para>
/// </remarks>
internal sealed class MessageHandler(
    TelegramSettings settings,
    INotifier notifier,
    IAiClient ai,
    IEnumerable<IAssistantTool> tools,
    ILocalTimeResolver clock,
    ILogger<MessageHandler> logger)
    : ITelegramUpdateHandler
{
    private const string DueTimeFormat = "dddd d MMMM yyyy, HH:mm";

    private const string Unreachable =
        "I could not reach the model just now. Send that again in a moment.";

    private const string NotUnderstoodAsATask =
        "I did not read that as a task. Try rephrasing it.";

    private const string DueTimeInPastReply =
        "That time has already passed. What time did you mean?";

    private const string DueTimeTooFarAheadReply =
        "That is more than two years away, which is probably not what you meant. "
        + "What time did you mean?";

    private const string DueTimeUnparseableReply =
        "I could not make sense of that time. What time did you mean?";

    private const string TitleMissingReply =
        "I did not catch what to call that. What should I call it?";

    private const string SomethingWentWrongReply =
        "Something went wrong on my end. Send that again in a moment.";

    /// <inheritdoc/>
    public UpdateType Handles => UpdateType.Message;

    /// <inheritdoc/>
    public async Task HandleAsync(Update update, CancellationToken ct)
    {
        if (update.Message is not { Chat.Id: var chatId, Text: { } text } ||
            chatId != settings.OwnerChatId)
        {
            return;
        }

        var answer = await ai.AskAsync(text, ct);

        if (!answer.IsSuccess)
        {
            var failure = answer switch
            {
                { Error: ErrorCode.ModelReturnedNoToolCall } => NotUnderstoodAsATask,
                _ => Unreachable,
            };

            await notifier.SendAsync(failure, ct);
            return;
        }

        var toolCall = answer.Value!;
        var tool = tools.FirstOrDefault(t => t.Name == toolCall.Name);

        if (tool is null)
        {
            logger.LogWarning("The chat model called an unregistered tool {Tool}.", toolCall.Name);
        }

        var outcome = tool is null
            ? Result<ReminderTask>.Failure(ErrorCode.ModelNamedUnknownTool)
            : await tool.ExecuteAsync(toolCall.ArgumentsJson, ct);

        if (!outcome.IsSuccess)
        {
            var failure = outcome switch
            {
                { Error: ErrorCode.DueTimeInPast } => DueTimeInPastReply,
                { Error: ErrorCode.DueTimeTooFarAhead } => DueTimeTooFarAheadReply,
                { Error: ErrorCode.DueTimeUnparseable } => DueTimeUnparseableReply,
                { Error: ErrorCode.ToolArgumentMissing } => TitleMissingReply,
                _ => SomethingWentWrongReply,
            };

            await notifier.SendAsync(failure, ct);
            return;
        }

        var task = outcome.Value!;
        var reply = task.DueAt is { } dueAt
            ? $"{task.Title} -- due {clock.ToLocal(dueAt).ToString(DueTimeFormat, CultureInfo.InvariantCulture)}."
            : $"{task.Title} -- saved with no reminder.";

        await notifier.SendTaskAsync(task.Id, reply, ct);
    }
}
```

Per Decision 3's OPEN question: if the owner rules to share `DueTimeInPast`'s sentence instead of
giving `DueTimeUnparseable` its own, delete the `DueTimeUnparseableReply` constant and change its
switch arm to `{ Error: ErrorCode.DueTimeUnparseable } => DueTimeInPastReply,` (or fold the two
`case` labels together) — no other line in this file changes.

- [ ] **Step 8: Update `AddAssistantListener`'s `<remarks>`**

In `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`, replace `AddAssistantListener`'s
`<remarks>` block:

```csharp
    /// <remarks>
    /// Requires <c>AddAssistantTelegram</c> for the client and the owner's chat id,
    /// <c>AddAssistantServices</c> for the <see cref="TimeProvider"/> the failure backoff uses and
    /// the <see cref="ITaskService"/> <see cref="Telegram.CallbackRouter"/>'s actions reach,
    /// <c>AddAssistantTime</c> for the <see cref="ILocalTimeResolver"/>
    /// <see cref="Telegram.MessageHandler"/> renders a stored due time back through, and
    /// <c>AddAssistantAi</c> for the <see cref="IEnumerable{IAssistantTool}"/>
    /// <see cref="Telegram.MessageHandler"/> dispatches a tool call against.
    /// Handlers and task actions are registered scoped, not singleton, so
    /// <see cref="Telegram.TelegramListener"/> can resolve them from a scope it opens per update;
    /// see docs/tech-debt.md.
    /// </remarks>
```

- [ ] **Step 9: Build and confirm the whole solution still compiles**

```bash
dotnet build --no-restore
```

Expected: `Assistant.Impl` compiles; `Assistant.IntegrationTests` fails to build until Step 10,
because `TelegramListenerTests.cs` still asserts the deleted `ToolCallNotActedOnYet` string.

- [ ] **Step 10: Fix the reset gap, fix the clock, and rewrite `TelegramListenerTests`**

Replace `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs` in full:

```csharp
using Assistant.Contracts;
using Assistant.Impl;
using Assistant.Impl.Settings;
using Assistant.Impl.Telegram;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Assistant.IntegrationTests.Telegram;

/// <summary>
/// Test class for the inbound listener registered via <c>AddAssistantListener</c>.
/// </summary>
/// <param name="postgres">The shared database fixture.</param>
/// <param name="wireMock">The shared stub API fixture.</param>
[Collection(IntegrationCollection.Name)]
public sealed class TelegramListenerTests(PostgresFixture postgres, WireMockFixture wireMock) : IAsyncLifetime
{
    private const string BotToken = "123456:TESTTOKEN";
    private const long OwnerChatId = 100200300L;
    private const long StrangerChatId = 999888777L;
    private const int NoLimit = 100;

    private const string NotUnderstoodAsATask =
        "I did not read that as a task. Try rephrasing it.";

    private const string DueTimeInPastReply =
        "That time has already passed. What time did you mean?";

    private const string DueTimeTooFarAheadReply =
        "That is more than two years away, which is probably not what you meant. "
        + "What time did you mean?";

    private const string DueTimeUnparseableReply =
        "I could not make sense of that time. What time did you mean?";

    private const string TitleMissingReply =
        "I did not catch what to call that. What should I call it?";

    private const string SomethingWentWrongReply =
        "Something went wrong on my end. Send that again in a moment.";

    private static readonly TimeSpan ReplyDeadline = TimeSpan.FromSeconds(10);

    private static readonly DateTimeOffset AsOf = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private ServiceProvider _provider = null!;

    private IHostedService _sut = null!;

    private ITaskRepository _repository = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssistantRepository(postgres.ConnectionString);
        services.AddAssistantServices();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(AsOf));
        services.AddAssistantTelegram(new TelegramSettings
        {
            BotToken = BotToken, OwnerChatId = OwnerChatId, BaseUrl = wireMock.Url,
        });
        services.AddAssistantTime(new TimeSettings { IanaTimeZone = "Asia/Jerusalem" });
        services.AddAssistantAi(new AiSettings
        {
            ApiKey = "test-key", BaseUrl = wireMock.Url, Model = "test-model", MaxTokens = 100,
        });
        services.AddAssistantListener();
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetServices<IHostedService>().Single();
        _repository = _provider.GetRequiredService<ITaskRepository>();

        await postgres.ResetAsync();
        await wireMock.ResetAsync();
        await wireMock.SeedAiToolCallAsync(
            "create_task", """{"title":"call the bank","due_at_local":"2026-08-26T10:00:00"}""");
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await _sut.StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
    }

    /// <summary>
    /// When the owner sends a message
    /// And the model calls create_task with a due time that resolves
    /// Then the task is stored with that due instant
    /// And the owner is told the title and the due time, rendered in the configured zone
    /// And the reply carries a Done button for that exact task.
    /// </summary>
    [Fact]
    public async Task Listener_OwnerSendsAMessageWithADueTime_StoresItAndRepliesWithTheDueTimeAndADoneButton()
    {
        // Arrange
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "call the bank tomorrow at 10"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Equal("call the bank -- due Wednesday 26 August 2026, 10:00.", sent[0].Text);

        var stored = Assert.Single(
            await _repository.GetDueRemindersAsync(AsOf.AddYears(10), NoLimit, CancellationToken.None));
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 7, 0, 0, TimeSpan.Zero), stored.DueAt);

        var row = Assert.Single(sent[0].ReplyMarkup!.InlineKeyboard);
        var button = Assert.Single(row);
        Assert.Equal(TaskActions.Done.Label, button.Text);
        Assert.Equal(CallbackCodec.Encode(TaskActions.Done.Key, stored.Id), button.CallbackData);
    }

    /// <summary>
    /// When the owner sends a message
    /// And the model calls create_task with no due time
    /// Then the owner is told plainly that no reminder will fire
    /// And the reply still carries a Done button.
    /// </summary>
    [Fact]
    public async Task Listener_OwnerSendsAMessageWithNoDueTime_RepliesThatNoReminderWillFire()
    {
        // Arrange
        await wireMock.SeedAiToolCallAsync("create_task", """{"title":"Buy milk"}""");
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "buy milk"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Equal("Buy milk -- saved with no reminder.", sent[0].Text);

        var row = Assert.Single(sent[0].ReplyMarkup!.InlineKeyboard);
        var button = Assert.Single(row);
        Assert.Equal(TaskActions.Done.Label, button.Text);
    }

    /// <summary>
    /// When someone other than the owner sends a message
    /// And the owner sends one in the same batch
    /// Then only the owner is answered.
    /// </summary>
    /// <remarks>
    /// The owner's message is a synchronisation device, not a second assertion. Proving
    /// that nothing was sent to the stranger otherwise means waiting on a clock and
    /// hoping; putting the stranger first in the batch means that by the time the owner's
    /// reply arrives, the stranger's message has already been processed and skipped.
    /// <para>
    /// This test does not check the reply's exact text: that check duplicated
    /// <see cref="Listener_OwnerSendsAMessageWithADueTime_StoresItAndRepliesWithTheDueTimeAndADoneButton"/>,
    /// which spec §7.2 forbids. What this test alone proves is that the stranger's message
    /// produced no second reply.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Listener_StrangerSendsAMessage_OnlyTheOwnerIsAnswered()
    {
        // Arrange
        await wireMock.SeedUpdatesAsync(
            new InboundUpdate(10, StrangerChatId, "let me in"),
            new InboundUpdate(11, OwnerChatId, "call the bank"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Single(sent);
    }

    /// <summary>
    /// When a message has been answered
    /// And the listener keeps polling
    /// Then it is not answered again.
    /// </summary>
    /// <remarks>
    /// The only test in this suite that waits on wall-clock time, and it is worth the
    /// cost: a listener that fails to advance its offset is served the same update on
    /// every poll, and the stub answers an unadvanced poll with no delay at all.
    /// </remarks>
    [Fact]
    public async Task Listener_MessageAlreadyAnswered_DoesNotAnswerItAgain()
    {
        // Arrange
        var settle = TimeSpan.FromSeconds(3);
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "call the bank"));

        // Act
        await _sut.StartAsync(CancellationToken.None);
        await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        await Task.Delay(settle);

        // Assert
        Assert.Single(await wireMock.SentMessagesAsync());
    }

    /// <summary>
    /// When the model replies with prose instead of calling a tool
    /// And the owner sent the message that produced it
    /// Then the owner is told the message was not read as a task.
    /// </summary>
    [Fact]
    public async Task Listener_ModelRepliesWithProse_TellsTheOwnerItWasNotReadAsATask()
    {
        // Arrange
        await wireMock.SeedAiAnswerAsync("Sure, tell me more.");
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "hello"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Equal(NotUnderstoodAsATask, sent[0].Text);
    }

    /// <summary>
    /// When the owner sends a message
    /// And the model's tool call cannot be carried out
    /// Then the owner is told a plain sentence with no button
    /// And nothing else is sent.
    /// </summary>
    /// <param name="toolName">The tool name the stubbed tool call carries.</param>
    /// <param name="argumentsJson">The arguments JSON the stubbed tool call carries.</param>
    /// <param name="expectedReply">The sentence the owner should see.</param>
    /// <remarks>
    /// Whether a row was written for each of these is proven once, at the tool level, by
    /// <c>CreateTaskToolTests</c> -- repeating that proof here through a real Telegram round
    /// trip would duplicate coverage spec §7.2 forbids. The unregistered-tool-name row has no
    /// such proof anywhere else, and needs none: <c>MessageHandler</c> only calls an
    /// <see cref="IAssistantTool"/> once one has actually been found, so there is no code path
    /// from an unmatched name to a persisted row to test in the first place.
    /// </remarks>
    [Theory]
    [InlineData("create_task", """{"title":"Call the bank","due_at_local":"2026-08-25T10:00:00"}""", DueTimeInPastReply)]
    [InlineData("create_task", """{"title":"Call the bank","due_at_local":"2029-06-01T00:00:00"}""", DueTimeTooFarAheadReply)]
    [InlineData("create_task", """{"title":"Call the bank","due_at_local":"not a date"}""", DueTimeUnparseableReply)]
    [InlineData("create_task", """{"due_at_local":"2026-08-26T10:00:00"}""", TitleMissingReply)]
    [InlineData("create_task", "not json at all", SomethingWentWrongReply)]
    [InlineData("update_task", """{"anything":"here"}""", SomethingWentWrongReply)]
    public async Task Listener_ModelsToolCallCannotBeCarriedOut_RepliesWithNoButton(
        string toolName, string argumentsJson, string expectedReply)
    {
        // Arrange
        await wireMock.SeedAiToolCallAsync(toolName, argumentsJson);
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "call the bank"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Equal(expectedReply, sent[0].Text);
        Assert.Null(sent[0].ReplyMarkup);
    }
}
```

Per Decision 3's OPEN question, the `DueTimeUnparseableReply` row above changes to whichever
constant the owner's ruling settles on, mirroring Step 7's own note.

- [ ] **Step 11: Build and run the whole suite**

```bash
docker compose -f compose.test.yaml up -d
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
docker compose -f compose.test.yaml down
```

Expected: build succeeds solution-wide, zero warnings, zero errors. **57 passed** unit (0 failed).
**68 passed** integration (0 failed).

- [ ] **Step 12: Correct the backlog document, once the owner has verified the full loop on a
      real phone**

Apply the three corrections given in full in Decision 9, above, to
`docs/design/2026-08-22-slice-1-feature-backlog.md`: the F9b bullet at line 578, the F10 entry at
lines 597-602 (including the `**done**` suffix and the closing `*Settled at F10-3:*` bullets), and
the §4 table row at line 729. Add a manual real-Telegram walkthrough step to `docs/e2e-local.md`,
mirroring F6-3's own addition, once that walkthrough has actually happened.

- [ ] **Step 13: Commit**

```bash
git add src/Assistant.Contracts/ErrorCode.cs \
        src/Assistant.Interfaces/ILocalTimeResolver.cs \
        src/Assistant.Impl/Services/LocalTimeResolver.cs \
        src/Assistant.Impl/Telegram/MessageHandler.cs \
        src/Assistant.Impl/ImplServiceCollectionExtensions.cs \
        tests/Assistant.UnitTests/Services/LocalTimeResolverTests.cs \
        tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs \
        docs/design/2026-08-22-slice-1-feature-backlog.md \
        docs/e2e-local.md
git commit
```

Message:

```
feat: MessageHandler dispatches, the reply closes the loop

MessageHandler now injects IEnumerable<IAssistantTool>, ILocalTimeResolver,
and ILogger<MessageHandler>, matches the model's tool call by name -- the
same inline lookup CallbackRouter already uses for task actions -- and
calls ExecuteAsync. The ToolCallNotActedOnYet placeholder is gone: a
captured task replies with its title and, when one was given, its due time
rendered back in the configured zone; an undated task replies that no
reminder will fire. Both carry the Done button. Every failure -- an
unrecognised tool name, a malformed or incomplete tool call, or a due time
that could not be resolved -- replies with a plain sentence and no button.

ILocalTimeResolver gains ToLocal(DateTimeOffset utc), the UTC-to-local
counterpart Resolve's own local-to-UTC direction needed once a reply had to
render a stored instant back to the owner. No guard clause: converting an
instant to its wall-clock reading in a zone has exactly one right answer,
unlike the reverse.

ErrorCode gains one member, ModelNamedUnknownTool, for a tool call naming
something this assistant does not have. MessageHandler logs the
unregistered name at warning level -- never the call's arguments, which
could carry a task title.

TelegramListenerTests now resets Postgres before every test, the same as
CallbackRouterTests and DueReminderJobTests already do -- a gap that was
harmless while the listener wrote nothing and is not harmless once it does.
It also pins the clock with a FakeTimeProvider, the fix CreateTaskToolTests
already established, so a due-time literal cannot go stale as real time
passes it.

Tests: 57 unit, 68 integration, up from 56 / 61. Build clean, zero
warnings.

Plan: docs/plans/2026-09-05-f10-3-the-reply-closes-the-loop.md

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
```

---

## Self-review

**This commit:**
- [ ] `MessageHandler`'s primary constructor takes exactly `(TelegramSettings settings, INotifier
      notifier, IAiClient ai, IEnumerable<IAssistantTool> tools, ILocalTimeResolver clock,
      ILogger<MessageHandler> logger)`, all six already resolvable in production with no new DI
      registration (Decision 2, "Verified facts")
- [ ] Tool dispatch is `tools.FirstOrDefault(t => t.Name == toolCall.Name)`, matching
      `CallbackRouter.cs:91`'s own shape exactly, restated not re-argued (Decision 1)
- [ ] Exactly one `ErrorCode` member is appended, `ModelNamedUnknownTool`, after
      `DueTimeUnparseable`, with `Unknown` still first — no member inserted, none renumbered
- [ ] The four Ruling-E rows in Decision 3's table are reproduced verbatim, character for
      character, against F10-1's own plan text
- [ ] `ILocalTimeResolver.ToLocal(DateTimeOffset utc)` returns a bare `DateTimeOffset`, not
      `Result<DateTimeOffset>`, and its implementation is exactly
      `TimeZoneInfo.ConvertTime(utc, zone)`, reusing the field `Resolve` and `CurrentLocalTime`
      already close over (Decision 4)
- [ ] `ToLocal` earns exactly one unit test; no DST `[Theory]` was added for it (Decision 4)
- [ ] Every captured-task branch calls `notifier.SendTaskAsync`; every failure branch calls
      `notifier.SendAsync` — checked against both branches in the code above (Decision 6)
- [ ] `TelegramListenerTests.InitializeAsync` calls `postgres.ResetAsync()` immediately before
      `wireMock.ResetAsync()`, matching `CallbackRouterTests.cs:66-67` and
      `DueReminderJobTests.cs:43-44` (Decision 7)
- [ ] `TelegramListenerTests.InitializeAsync` registers a `FakeTimeProvider` at the fixed `AsOf`
      after `AddAssistantServices()`, matching `CreateTaskToolTests`'s own convention (Decision 8)
- [ ] The placeholder-asserting test is gone; no test anywhere in this diff asserts
      `ToolCallNotActedOnYet`
- [ ] `ReminderTask.Notes` still does not exist; F10-1's Decision 7 (Ruling F) is not silently
      reversed
- [ ] No `ITaskService.CreateAsync`, `ReminderTaskMappingExtensions.ToModel`,
      `IAssistantTool.ExecuteAsync`, `CreateTaskTool.cs`, `ILocalTimeResolver.Resolve`, or
      `ReminderTask.cs` change anywhere in this diff
- [ ] Every new or changed public member carries a three-line-tag `<summary>` plus every
      `<param>`/`<returns>` `CS1591`/`CS1573` requires
- [ ] Test summaries are Gherkin (`When`/`And`/`Then`), one clause per line, in every new test
      above
- [ ] No emoji anywhere, including the commit message
- [ ] **No plan-internal decision citation (`Decision 1`, `(Decision 2)`, or similar) inside any
      C# code block, doc comment, or commit message** — every fenced code block above was
      re-read for this before the plan was committed
- [ ] Plain ASCII `--` used inside every C# doc comment, C# comment, and the commit message body;
      this document's own prose uses real em dashes
- [ ] Decision 3's `DueTimeUnparseable` sentence is marked OPEN in the prose, and Steps 7 and 10
      both note the one-line change needed if the owner rules the other way

**Whole feature (F10), once this lands and the owner verifies it:**
- [ ] Talking to the bot with a due time ends in a stored row with the right UTC instant and a
      reply carrying the Done button — the backlog's own named F10 test, finally exercised
- [ ] An undated task's reply says plainly that no reminder will fire (issue #27, half closed;
      the no-listing half remains F11's, per Decision 10)
- [ ] A tool call naming something unregistered gets a plain sentence, logged by name only, never
      by arguments (issue #28, closed)
- [ ] The backlog's F10 entry is marked done and its `Notes` mentions corrected only after the
      owner's real-phone verification, not by this plan alone (Decision 9)
- [ ] Spec coverage across all three F10 slices: §5.1 (the full flow, completed here), §5.4 (the
      guard clauses and "before anything is persisted," structural since F10-2, reply-mapped
      here), §6.4 (the Done button, decoded and checked against the real task id for the first
      time), §7.2/§7.3 (no duplicated coverage; exact text, exact buttons), §7.7 (failing test
      first, Steps 3 and 9 above)
- [ ] This slice's diff measures 295 lines by the convention argued in "How this slice fits F10";
      the full feature's running total (120 + 383 + 295 = 798) stays comfortably under three
      separate 1000-line budgets
