# Slice 1 — Feature Backlog

**Date:** 2026-08-22
**Status:** For review
**Derived from:** `docs/design/slice-1-reminders.md` (the approved design spec — binding)

This document decomposes slice 1 into features. It adds no requirements: every feature below is
already designed and agreed in the spec, and each entry cites the section it implements.

**One feature = one pull request.** Each is planned separately, with `superpowers:writing-plans`,
at the time it is built — never all at once.

---

## 1. Rules that govern every feature

**YAGNI.** A feature may only introduce an interface member, contract type, model, model
property, or table that **the same feature exercises with a test**. Everything else waits for
the feature that consumes it.

**Open/Closed, precisely bounded.** Behaviour seams stay extension-only — a new button, tool, or
job is a new class, never an edit to an existing one (spec §3.6). Data-access surfaces grow by
adding methods, because adding a repository method *is* a modification and no design avoids it.
An abstraction with one implementation is a guess, not a seam; interfaces appear when a second
implementation or a real extension point does.

**Definition of done.** A feature is done when: it builds with zero warnings, its tests pass,
every new public member has a three-line `<summary>`, and no code was added that nothing
exercises. Features marked **▶ observable** must also be demonstrable on a real phone.

**No credentials to build or test.** Telegram and both model providers are stubbed with
WireMock; Postgres comes from Docker Compose. Spec §11.3.

---

## 2. Decision needed before F1

**How do you test a write when there is no read yet?**

Splitting "save a task" from "find the due ones" leaves F1 with only `AddAsync`, and a write you
cannot read back is a write you have not tested. Three options:

| Option | Cost |
| :--- | :--- |
| **(a) F1 adds `FindAsync` too** | One extra method. The round-trip test becomes honest and trivial. `FindAsync` is consumed at F4 (loading a task to mark its reminder sent), so it is early by one feature, not speculative. |
| (b) Verify with raw SQL in the test | Interface stays at one method, but the test hand-writes a `SELECT` over seven columns and maps them by hand — duplicating the very mapping it is meant to verify, and rotting the first time a column moves. |
| (c) Expose `AssistantDbContext` to tests via `InternalsVisibleTo` | Keeps the interface minimal but punches a hole in the boundary spec §3.2 exists to defend. |

**Recommendation: (a).** The one caveat, stated plainly: a round-trip test exercises write and
read together, so a fault mirrored in both would pass. That is the standard trade for ORM
round-trip tests and is worth accepting over (b) or (c).

---

## 3. The features

### Reminder path — no AI, no credentials

**F1 · Save a task to the database** — spec §4.1, §4.3, §7.1
Adds `AssistantDbContext`, `ReminderTaskConfiguration`, the `reminder_tasks` table and its first
migration, `AddAssistantRepository`, `compose.test.yaml`, and the `PostgresFixture` (readiness
polling + Respawn reset). `ITaskRepository` gains `AddAsync` — and `FindAsync` if §2(a) is
approved.
*Tests:* round-trip persistence; the check constraints reject an invalid row.

**F2 · Find the tasks that are due** — spec §6.2
`ITaskRepository.GetDueRemindersAsync(asOfUtc, limit, ct)`. No lower bound on `due_at` — that
absence is what makes restart catch-up automatic. Adds the partial index.
*Tests:* eligible vs ineligible by equivalence class, with boundaries on `due_at <= now`;
ordering oldest-first; the limit.

**F3 · Send a Telegram message** — spec §6.5, §12.3
`INotifier` + `TelegramNotifier` over `Telegram.Bot`, HTML parse mode, WireMock in place of the
real API.
*Tests:* exact recipient and exact text; a title containing `_` and `*` still delivers.

**F4 · The scheduler fires due reminders ▶ observable** — spec §6.1, §6.2
`IClock`/`SystemClock`, `IScheduledJob`, `ScheduledJobBase` (re-entrancy guard + try/catch),
`ReminderScheduler` on a 30s `PeriodicTimer`, and `DueReminderJob`. Send **then** mark —
at-least-once is deliberate.
**`TaskService` begins here, not at F5.** Spec §4.2 forbids a job touching a repository, so
marking a reminder sent must go through `ITaskService.MarkReminderSentAsync`. That drags in
`Result` and `ErrorCode` — this is where `Contracts` stops being an empty project.
`ITaskRepository` gains `UpdateAsync`.
*Tests:* a due task produces exactly one message; a second tick produces none; a process
restarted after the due time still delivers.
**Milestone: the product works.** Seed a row, watch your phone.

**F5 · Complete a task from a button ▶ observable** — spec §6.4
`ITaskAction` + `DoneAction`, `ICallbackHandler` + `CallbackRouter`, the `v1:<action>:<id>`
callback codec, in-place message edit, and `ITaskService.CompleteAsync`. `ReminderTask` regains
`CompletedAt`, which also brings back the `ck_completed_consistency` check constraint.
*Tests:* one tap completes; a second tap says "already done" rather than erroring; the callback
query is always answered.

### Capture path — the flow you described

**F6 · Consume inbound messages** — spec §5.1
`TelegramListener` long-poll loop, owner whitelist, and an echo reply. No AI yet.
*Tests:* a whitelisted sender gets a reply; a non-whitelisted sender is ignored silently and
costs nothing.

**F7 · Resolve local time** — spec §5.4
`ILocalTimeResolver` + `LocalTimeResolver` over the configured IANA zone, and the guard clauses:
more than a minute in the past, more than two years ahead, DST spring-forward gap, fall-back
ambiguity.
*Tests:* a table over the DST boundaries and each guard.

**F8 · Send to the model and get a tool call** — spec §5.2, §5.3, §12.3
`IChatClient`, `IAnthropicApi` via Refit, the system prompt carrying current local time, and
`CreateTaskTool` as the first `IAssistantTool`. Adds `CreateTaskRequest` to `Contracts`
(`Result` and `ErrorCode` arrived at F4).
*Tests:* free text produces the expected tool call against a WireMock'd provider.

**F9 · Store the parsed task and reply ▶ observable** — spec §5.1
`ITaskService.CreateAsync`, the mapping extension methods, and the reply rendered with its
inline keyboard. `ReminderTask` regains `Notes`, which the capture path is first to write.
*Tests:* "call the bank tomorrow at 10" ends as a row with the right UTC instant and a reply
carrying the right buttons.
**Milestone: the full loop.** Talk to it, get reminded, tap Done.

### Completing the product

**F10 · Snooze and reschedule** — spec §6.4, §4.2
`SnoozeAction`, `RescheduleAction`, `EditAction`. Both clear `ReminderSentAt` and reset
`DeliveryAttempts` so the task fires again — the pairing that makes `TaskService` the mandatory
single writer. `ReminderTask` regains `DeliveryAttempts`; the retry cap enters the due query here.
*Tests:* snooze 1h fires at exactly +1h, not immediately; the sent marker is cleared.

**F11 · Daily brief ▶ observable** — spec §6.3
`DailyBriefLog` + `daily_brief_log` (its primary key is the once-per-day check),
`IDailyBriefRepository.TryClaimAsync`, `DailyBriefJob` at 07:00 local, no cutoff.
`ReminderTask` regains `Priority` — the brief is the first thing that orders by it (spec §3.3).
*Tests:* one brief per day across a restart; a late brief still sends.

**F12 · Never lose a capture** — spec §5.5, §5.6
`IOpenRouterApi`, `FallbackChatClient` with Polly, the per-minute call cap, and the raw-capture
safety net: if every provider fails the text is still saved as an undated task and the user is
told. `ChatMessage` + `chat_messages` arrive here for the conversation window.
*Tests:* both providers failing still produces a task and a reply.

**F13 · Operations** — spec §6.5, §8, §11.3
`/status`, Serilog, the heartbeat file and container healthcheck, `Dockerfile`, `compose.yaml`,
`.github/workflows/ci.yml` with gitleaks, and the fail-fast options validation
(`ValidateOnStart`) with `appsettings.{Environment}.json`.
*Tests:* the host refuses to start on invalid configuration.

---

## 4. Deferred model properties

The YAGNI reset stripped `ReminderTask` to seven properties. Each returns with the feature that
first needs it, at the cost of one additive migration:

| Property | Returns at |
| :--- | :--- |
| `CompletedAt` | F5 |
| `DeliveryAttempts` | F10 |
| `Priority` | F11 |
| `Notes` | F9 |

## 5. Not in slice 1

Notes with embeddings and semantic search, recurring tasks, separate due date vs reminder time,
an HTTP API, calendar integration, voice transcription, multi-user. Spec §1.3 and §10.

## 6. Order and dependencies

F1 → F2 → F3 → F4 is a chain; nothing in it can be reordered. F5 needs F4. F6 is independent of
F1-F5 and could move earlier if you want to talk to the bot sooner. F7 → F8 → F9 is a chain.
F10-F13 each depend only on F9.

Three milestones are worth pausing on: **F4** (a reminder actually fires), **F9** (the whole
loop works), **F13** (it runs on a VPS).
