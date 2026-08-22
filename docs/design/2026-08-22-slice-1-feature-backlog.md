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

**Size budget.** No pull request exceeds **1000 changed lines**, and smaller is better. Where a
feature would breach that, it is split along a line that leaves both halves independently
testable — never split so that one half is untestable until the other lands.
Generated EF migration files count toward the diff but not toward review effort; PR bodies say
which files are generated so they can be skipped.

**No credentials to build or test.** Telegram and both model providers are stubbed with
WireMock; Postgres comes from Docker Compose. Spec §11.3.

---

## 2. Resolved: how F1 is split

Splitting "save a task" from "find the due ones" left the first feature holding only `AddAsync`,
which cannot be verified without a read. Resolved by splitting one level differently instead:

- **`AddAsync` and `FindAsync` ship together.** Separating them would produce one PR whose write
  is unverifiable and another whose read has nothing to read — each untestable alone, which is
  exactly the split the size budget forbids. `FindAsync` is ~15 lines and is consumed at F5 (the scheduler loads a task to mark it sent).
- **The schema and harness move to their own feature (F1).** That is where the bulk lives: the
  first migration, `compose.test.yaml`, and the Postgres fixture. It is independently testable
  without any repository method, because a malformed row can be rejected by the database using a
  raw `INSERT` in the test — deliberately inserting bad data duplicates no mapping.

Result: F1 ≈ 450 lines (≈300 of it generated migration scaffolding), F2 ≈ 150 lines. Both well
under budget, both meaningful alone.

## 3. The features

### Reminder path — no AI, no credentials

**F1 · Database schema and the integration test harness** — spec §4.3, §7.1
Adds `AssistantDbContext`, `ReminderTaskConfiguration`, the `reminder_tasks` table with its check
constraints and partial index, the first migration, `AddAssistantRepository`, `compose.test.yaml`,
and `PostgresFixture` (readiness polling + Respawn reset). No repository methods.
*Tests:* migrations apply cleanly against a real Postgres; each check constraint rejects a
malformed row inserted by raw SQL. Raw SQL is correct here — the point is to write data the
mapping would never produce.

**F2 · Save a task and read it back** — spec §4.1, §4.4
`ITaskRepository.AddAsync` and `FindAsync`, plus `EfTaskRepository`.
*Tests:* a round-trip preserves every field. This is the test that fails when a column is added
and the mapping forgets it (spec §4.4 names that as the predictable defect).

**F3 · Find the tasks that are due** — spec §6.2
`ITaskRepository.GetDueRemindersAsync(asOfUtc, limit, ct)`. No lower bound on `due_at` — that
absence is what makes restart catch-up automatic.
*Tests:* eligible vs ineligible by equivalence class, with boundaries on `due_at <= now`;
ordering oldest-first; the limit.

**F4 · Send a Telegram message** — spec §6.5, §12.3
`INotifier` + `TelegramNotifier` over `Telegram.Bot`, HTML parse mode, WireMock in place of the
real API.
*Tests:* exact recipient and exact text; a title containing `_` and `*` still delivers.

**F5 · The scheduler fires due reminders ▶ observable** — spec §6.1, §6.2
`IClock`/`SystemClock`, `IScheduledJob`, `ScheduledJobBase` (re-entrancy guard + try/catch),
`ReminderScheduler` on a 30s `PeriodicTimer`, and `DueReminderJob`. Send **then** mark —
at-least-once is deliberate.
**`TaskService` begins here, not at F6.** Spec §4.2 forbids a job touching a repository, so
marking a reminder sent must go through `ITaskService.MarkReminderSentAsync`. That drags in
`Result` and `ErrorCode` — this is where `Contracts` stops being an empty project.
`ITaskRepository` gains `UpdateAsync`.
*Tests:* a due task produces exactly one message; a second tick produces none; a process
restarted after the due time still delivers.
**Milestone: the product works.** Seed a row, watch your phone.

**F6 · Complete a task from a button ▶ observable** — spec §6.4
`ITaskAction` + `DoneAction`, `ICallbackHandler` + `CallbackRouter`, the `v1:<action>:<id>`
callback codec, in-place message edit, and `ITaskService.CompleteAsync`. `ReminderTask` regains
`CompletedAt`, which also brings back the `ck_completed_consistency` check constraint.
*Tests:* one tap completes; a second tap says "already done" rather than erroring; the callback
query is always answered.

### Capture path — the flow you described

**F7 · Consume inbound messages** — spec §5.1
`TelegramListener` long-poll loop, owner whitelist, and an echo reply. No AI yet.
*Tests:* a whitelisted sender gets a reply; a non-whitelisted sender is ignored silently and
costs nothing.

**F8 · Resolve local time** — spec §5.4
`ILocalTimeResolver` + `LocalTimeResolver` over the configured IANA zone, and the guard clauses:
more than a minute in the past, more than two years ahead, DST spring-forward gap, fall-back
ambiguity.
*Tests:* a table over the DST boundaries and each guard.

**F9 · Send to the model and get a tool call** — spec §5.2, §5.3, §12.3
`IChatClient`, `IAnthropicApi` via Refit, the system prompt carrying current local time, and
`CreateTaskTool` as the first `IAssistantTool`. Adds `CreateTaskRequest` to `Contracts`
(`Result` and `ErrorCode` arrived at F5).
*Tests:* free text produces the expected tool call against a WireMock'd provider.

**F10 · Store the parsed task and reply ▶ observable** — spec §5.1
`ITaskService.CreateAsync`, the mapping extension methods, and the reply rendered with its
inline keyboard. `ReminderTask` regains `Notes`, which the capture path is first to write.
*Tests:* "call the bank tomorrow at 10" ends as a row with the right UTC instant and a reply
carrying the right buttons.
**Milestone: the full loop.** Talk to it, get reminded, tap Done.

### Completing the product

**F11 · Snooze and reschedule** — spec §6.4, §4.2
`SnoozeAction`, `RescheduleAction`, `EditAction`. Both clear `ReminderSentAt` and reset
`DeliveryAttempts` so the task fires again — the pairing that makes `TaskService` the mandatory
single writer. `ReminderTask` regains `DeliveryAttempts`; the retry cap enters the due query here.
*Tests:* snooze 1h fires at exactly +1h, not immediately; the sent marker is cleared.

**F12 · Daily brief ▶ observable** — spec §6.3
`DailyBriefLog` + `daily_brief_log` (its primary key is the once-per-day check),
`IDailyBriefRepository.TryClaimAsync`, `DailyBriefJob` at 07:00 local, no cutoff.
`ReminderTask` regains `Priority` — the brief is the first thing that orders by it (spec §3.3).
*Tests:* one brief per day across a restart; a late brief still sends.

**F13 · Never lose a capture** — spec §5.5, §5.6
`IOpenRouterApi`, `FallbackChatClient` with Polly, the per-minute call cap, and the raw-capture
safety net: if every provider fails the text is still saved as an undated task and the user is
told. `ChatMessage` + `chat_messages` arrive here for the conversation window.
*Tests:* both providers failing still produces a task and a reply.

**F14 · Operations** — spec §6.5, §8, §11.3
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
| `CompletedAt` | F6 |
| `DeliveryAttempts` | F11 |
| `Priority` | F12 |
| `Notes` | F10 |

## 5. Not in slice 1

Notes with embeddings and semantic search, recurring tasks, separate due date vs reminder time,
an HTTP API, calendar integration, voice transcription, multi-user. Spec §1.3 and §10.

## 6. Order and dependencies

F1 → F2 → F3 → F4 → F5 is a chain; nothing in it can be reordered. F6 needs F5. F7 is
independent of F1-F6 and could move earlier if you want to talk to the bot sooner.
F8 → F9 → F10 is a chain. F11-F14 each depend only on F10.

Three milestones are worth pausing on: **F5** (a reminder actually fires), **F10** (the whole
loop works), **F14** (it runs on a VPS).
