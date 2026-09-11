# Fix #39 — one task, one live message

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** on the live deployment, capturing a task at 23:56 for a 09:15 reminder produced two
live Telegram messages for one task: the capture reply (`title -- due <local time>.` with
Done/Schedule buttons), then a second, bare-title message with its own Done/Schedule buttons when
the reminder fired. Root cause: `MessageHandler.cs:150` and `DueReminderJob.cs:33` each call
`INotifier.SendTaskAsync` independently, and `ReminderTask` carries no message identifier linking
the two sends. This plan makes **one task, one live message** hold everywhere, by giving
`ReminderTask` a `MessageId` column, replacing `SendTaskAsync` with an `AnnounceTaskAsync` that
takes the previous message id and replaces it, and wiring both call sites through the identical
two-line sequence: announce, then record which message now speaks for the task.

After this slice merges, a captured task's reminder fires into the **same** chat position: the
capture message disappears, replaced by the fired one, carrying the identical due-time text the
capture message showed. A task saved before this migration — no `MessageId` to speak of — still
fires and announces normally, with nothing to delete. A previous message that cannot be deleted
(too old, already deleted by the owner) never blocks delivery: the new message still arrives, the
stale one is simply left behind, logged at warning.

**Tech Stack:** `net10.0`, nullable enabled, warnings are errors — the existing stack. This slice
adds **one database migration** (`AddMessageId`, a nullable `integer` column with no check
constraint) and **no new NuGet package**: an `ILogger<TelegramNotifier>` primary-constructor
parameter is new to that one class, but `Microsoft.Extensions.Logging` is already referenced by
`Assistant.Impl` — `MessageHandler` has taken exactly this dependency since F10-4.

**Spec:** `docs/design/slice-1-reminders.md` §4.3 (schema — gains `message_id`) and §6.2
(`DueReminderJob` — gains a paragraph on replacing rather than adding a message), corrected in
this plan's own commits. §6.4 (inline buttons) is read, not touched: `CallbackRouter` already
edits the message a tap came from, which is by definition the live one once this invariant holds
— see Decision 5. §12.1 (XML docs), §12.5 (primary constructors), §12.6 (no emoji).

**Issue:** GitHub #39, filed against the live deployment (not a test failure — this bug shipped).

---

## How this slice fits

This is not a new feature slice off the F-numbered backlog; it is a production bug fix, and one
the backlog already anticipated without scheduling it. `docs/design/slice-1-reminders.md` §5.1,
written at F10-4, already says: "Storing the capture message's own id on `reminder_tasks`, so the
reminder that later fires can delete it too, is F10-5's." The feature backlog
(`docs/design/2026-08-22-slice-1-feature-backlog.md`, F10's "Settled at F10-4" bullet) repeats the
same forward reference verbatim. No F10-5 was ever scheduled — the gap sat unscheduled until a
real 23:56-to-09:15 capture on the live bot turned it from a known gap into issue #39. This plan
is that deferred work, arriving as a bug fix rather than a numbered feature.

The fix has exactly one governing idea, stated once and then applied at both call sites without
variation:

> **One task, one live message. Its identifier is stored on the task.**

This is spec §6.4's existing rule for a button tap — "Edit the original message in place rather
than sending a new one, so the chat stays clean" — finally holding across the whole app, not only
inside `CallbackRouter`. A second-order bug falls out of the same root cause and is fixed by the
same invariant, with no code aimed at it directly: with two live messages carrying two live
keyboards, completing the task from either one leaves the other showing a dead Done button that
answers `ErrorCode.TaskAlreadyCompleted` on a tap. Once there is only ever one live message, there
is only ever one live keyboard — see Decision 5.

The change touches six production files (a model property, one configuration line, one generated
migration, one interface method rename-and-reshape, one interface addition, and the two call
sites that were the bug), plus the test infrastructure needed to prove the fix is real rather than
trivially true against a stub that has always answered every send with the same hardcoded id — see
Decision 8. It lands as one pull request, in three commits ordered schema, seam, proof — argued in
"Steps."

---

## Global Constraints

Every constraint the project's prior plans carry forward still applies here:

- `net10.0`; nullable enabled; warnings are errors; `CS1591`/`CS1573` are errors everywhere.
- **Every class taking arguments uses a primary constructor.** `TelegramNotifier` gains
  `ILogger<TelegramNotifier>` and `DueReminderJob` gains `ILocalTimeResolver`, both as primary
  constructor parameters introduced and used within the same step that adds them — never one
  step ahead of its own use, which is what makes CS9113 (an unread primary-constructor parameter)
  an error here.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first. No Shouldly, no
  FluentAssertions.
- Every `<summary>` spans three lines: open tag, text, close tag. Test summaries are Gherkin
  (`When` / `And` / `Then`), one clause per line.
- No emoji anywhere: source, tests, docs, or commit messages.
- Central package management: no inline `Version=` attributes anywhere in this diff. No new
  package reference is needed.
- Plain ASCII `--` in every C# file, XML doc, and bot-facing string; real em dashes in this
  document's own markdown prose, and in the markdown prose this plan edits inside
  `docs/design/slice-1-reminders.md` and `docs/e2e-local.md` — Markdown prose and code are two
  different alphabets here, and this document does not mix them.
- **This plan was produced by reading the repository, not by building the change.** Every fact
  labelled "Verified" was checked against the working tree or the already-running test containers'
  admin APIs — never against a trial implementation. Where a reference plan could say "captured
  directly" about a compiler error, this one says "expected": the code below is complete and meant
  to compile and pass as written, but each commit's build/test result is the implementer's first
  real checkpoint, not a rerun this planning session already saw.
- PR budget: 1000 changed lines, excluding this plan. Estimated at roughly 585 raw lines, of which
  about 117 are EF-generated migration boilerplate that "counts toward the diff but not toward
  review effort" (feature backlog §1) — roughly 468 lines a reviewer actually reads, close to this
  plan's own commissioning estimate of ~450.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
  ```

---

## Verified facts this plan rests on

Every fact below was checked directly against the working tree at `1c72e2b` (`HEAD` of
`fix/issue-39-one-live-message`, which is `main` unchanged), or against the already-running
`personal-ai-assistant-postgres-test-1` / `personal-ai-assistant-wiremock-1` containers over their
own admin APIs, during this planning session. Several correct a factual claim in this plan's own
commissioning brief — each is called out as such.

- **The two `SendTaskAsync` call sites, confirmed at the brief's own line numbers.**
  `MessageHandler.cs:150` and `DueReminderJob.cs:33`. The job's own line sends `task.Title` — the
  bare title, confirmed by reading it and by `DueReminderJobTests.RunAsync_TaskIsDue_SendsItsTitle`'s
  current assertion, `Assert.Equal(task.Title, sent.Text)`.
- **`CallbackRouter.cs`'s two edit calls, confirmed at the brief's own line numbers**:
  `MarkCompletedTaskAsync` at line 139, `UpdateTaskAsync` at line 144, both inside the branch that
  runs after a registered `ITaskAction` succeeds. Neither is touched here; see Decision 5.
- **`INotifier` has five members today, not four** — `SendAsync`, `SendTaskAsync`,
  `MarkCompletedTaskAsync`, `UpdateTaskAsync`, `ShowKeyboardAsync`. The last two, plus `TaskKeyboard`
  and `TaskNavigations`, shipped at F11-3/F11-4a (PRs #34, #37), after the reference plan this
  document's format follows was written. `TaskNavigations.Schedule` (not `OpenSchedule`) and
  `TaskActions.Reschedule` (not `Schedule`) are this branch's real, post-rename names, confirmed by
  reading `TaskNavigations.cs`/`TaskActions.cs` directly — stale names would not compile.
- **`INotifier` has exactly one implementation, `TelegramNotifier`** (registered at
  `ImplServiceCollectionExtensions.cs:41`), and no test fake — confirmed by
  `grep -rln ": INotifier\b"` across the repository. Same result for `ITaskService`/`TaskService.cs`
  (scoped, `:53`). Neither interface change here risks breaking a hand-written fake; none exists.
- **No existing test calls `SendTaskAsync` by name.** `TelegramNotifierTests.cs` exercises
  `SendAsync`, `MarkCompletedTaskAsync`, `UpdateTaskAsync`, `ShowKeyboardAsync` only — confirmed by
  reading the file in full. The rename to `AnnounceTaskAsync` breaks no assertion there.
- **`Program.cs:16-17` resolves `INotifier` but calls `SendAsync`, not `SendTaskAsync`** —
  confirmed by reading the file. The `send-test-message` diagnostic is unaffected by this plan.
- **`ILocalTimeResolver` and `INotifier` are singletons** (`ImplServiceCollectionExtensions.cs:115`,
  `:41`); **`DueReminderJob` is a singleton** (`IScheduledJob`, `:64`); **`ITaskService` is scoped**
  (`:53`), which is why `DueReminderJob` already resolves it from a per-tick scope rather than
  injecting it. A singleton job may still take a singleton dependency as a plain primary-constructor
  parameter — `ILocalTimeResolver` has no restriction that would prevent it.
- **Existing migrations, in order: `InitialCreate`, `AddDueReminderIndex`, `AddCompletedAt`** —
  `AddMessageId` is the fourth. `AddCompletedAt` (`20260902181436_AddCompletedAt.cs`) is the
  precedent: one `AddColumn`/`DropColumn` pair, no data migration, landed in the same commit as the
  feature reading the column (`bc0c9cc`). **Correction against the brief:** it describes
  `AddCompletedAt` as adding a check constraint, which it does — but that constraint exists because
  `CompletedAt` and `Status` must always agree, an invariant `MessageId` has no analogue for. This
  migration adds none, exactly as the brief also specifies; the two differ in this one respect
  though they share the same three-file shape.
- **Migrations apply at host startup** — `Program.cs:29` calls
  `await host.Services.MigrateAssistantDatabaseAsync();` before `host.Run()`. No manual step on the
  Hetzner deployment.
- **`Telegram.Bot` 22.10.2.1, confirmed in `Directory.Packages.props`.** `Message.Id` is the
  property `CallbackRouter.cs` already binds and passes to `MarkCompletedTaskAsync(int
  messageId, ...)`, confirming the type is `int` — `AnnounceTaskAsync` reads `message.Id`, never
  `message.MessageId` (the same value, a different property name on the same SDK type).
- **`ApiRequestException` is a `RequestException`, confirmed empirically, not assumed from the
  name.** A standalone probe referencing `Telegram.Bot` 22.10.2.1, pointed at the already-running
  `personal-ai-assistant-wiremock-1` stub with `deleteMessage` answering HTTP 400 and
  `{"ok":false,"error_code":400,"description":"Bad Request: message to delete not found"}`, caught
  `ApiRequestException` from `client.DeleteMessage(...)`, and `ex is RequestException` printed
  `True` — the exact shape `MessageHandler.cs:156`'s existing `catch (RequestException)` already
  relies on, and the shape this plan's WireMock helper and `TelegramNotifier`'s new catch clause
  both depend on.
- **WireMock.Net 2.15.0's mapping priority, confirmed empirically, not read from documentation.**
  `TelegramStubs.cs`'s four non-`getUpdates` mappings set no explicit priority. A test mapping for
  the same path at `Priority: 1` lost to that unprioritised base; an otherwise-identical mapping at
  `Priority: -1` won. Unset priority behaves as `0`, so overriding it needs a **negative** priority
  — confirmed by three direct `curl` round-trips against the stub's admin API, cleaned up
  afterward. **Correction against the brief**, which asked for "a higher-priority mapping" with no
  number: `-1` is the number, not obvious from the existing codebase, where every seeded mapping
  competes only against other seeded mappings, never against an unprioritised base for the same
  path the way this plan's new helper must.
- **`TelegramStubs.cs`'s hardcoded `"message_id":1` lives in `SendMessageResponse` at lines 12-15**,
  installed against `/bot*/sendMessage` at lines 46-51. **Correction against the brief**, which
  cites line 8 — one line inside the class's `<summary>`. Nothing today reads this constant; a test
  asserting "the message deleted is the one the capture created" would pass trivially against it.
- **`DueReminderJobTests.cs` and `TelegramNotifierTests.cs` do not call `services.AddLogging()`
  today.** `grep -rln "AddLogging" tests/Assistant.IntegrationTests/` returns only `AiClientTests.cs`,
  `CallbackRouterTests.cs`, `TelegramListenerTests.cs` — the classes that already resolve
  `MessageHandler` (`ILogger<MessageHandler>` since F10-4). Once `TelegramNotifier` also takes an
  `ILogger`, resolving `INotifier` in either missing file throws at `BuildServiceProvider()` — a
  concrete two-file fix commit 2 must carry, not mentioned in the brief.
- **`DueReminderJobTests.cs` does not call `services.AddAssistantTime(...)` today**, in either its
  main `InitializeAsync` or the ad hoc `ServiceCollection` inside
  `RunAsync_DeliveryFails_TaskIsStillDue`. Once `DueReminderJob` takes `ILocalTimeResolver`,
  resolving `IScheduledJob` throws for the same reason — and in the ad hoc block, an unfixed gap
  would make `Assert.ThrowsAnyAsync<Exception>` pass for the wrong reason (a DI failure instead of
  the intended unreachable-host one), silently emptying that test. Both gaps are fixed in commit 2.
- **Measured baseline, run directly against this exact `HEAD`, not assumed.**
  `dotnet build --no-restore` — `Build succeeded. 0 Warning(s). 0 Error(s).`
  `dotnet test tests/Assistant.UnitTests --no-build` — **62 passed**. Against the already-running
  test containers, `dotnet test tests/Assistant.IntegrationTests --no-build` — **82 passed**. These
  are this branch's real numbers, not the F11-4a reference plan's 62/83 from an earlier point in
  this repository's history.
- **No unit test references `INotifier`, `ITaskService`, `SendTaskAsync`, or `MessageId` anywhere**
  — confirmed by grep. Every architecture test in `ConventionTests.cs`/`DependencyRuleTests.cs` was
  read in full: none scans anything this plan's changes would trip. This PR's test diff is entirely
  integration-level, matching the brief's own instruction.
- **`ReminderTask.cs` (`Assistant.Models`) references nothing** — confirmed against the project
  map and by reading the file. A `<see cref>` from here into `Assistant.Interfaces` would not
  resolve and would fail the build (`CS1574`, escalated by this project's warnings-as-errors). This
  plan's `MessageId` doc comment uses `<c>ITaskService.RecordMessageAsync</c>` and
  `<c>INotifier.AnnounceTaskAsync</c>` as plain code text instead — the same fallback `INotifier.cs`
  already uses for `<c>CallbackRouter</c>` and `<c>TelegramNotifier.MarkCompletedTaskAsync</c>`,
  both in `Assistant.Impl`, which `Assistant.Interfaces` does not reference either.

---

## Inherited context: what this slice reads from earlier features

`INotifier.MarkCompletedTaskAsync` and `UpdateTaskAsync` (F11-3) are read and called exactly as
before, from `CallbackRouter`, which this plan does not modify — see Decision 5.
`INotifier.ShowKeyboardAsync` and `TaskKeyboard` (F11-4a) are likewise read, not modified.
`TelegramNotifier.BuildKeyboard`'s existing dispatch — `BuildActionsKeyboard` /
`BuildScheduleMenuKeyboard` by `TaskKeyboard` value — is called unchanged from the new
`AnnounceTaskAsync`, exactly as `SendTaskAsync` already called it. `ITaskService.MarkReminderSentAsync`
(F5b) is read and left exactly as it is; this plan's new `RecordMessageAsync` is a sibling, not a
replacement. `ReminderTaskMappingExtensions.ToMessageText` (F10-3) is called from `DueReminderJob`
for the first time in this plan — previously called only from `MessageHandler` and
`CallbackRouter`. `ScheduledJobBase`'s re-entrancy guard (F5a) is unaffected: this plan changes
what `DueReminderJob.ExecuteAsync` does, not how often it runs.

---

## Decisions

### 1. `MessageId` lives on `ReminderTask`, not a side table or an in-memory map

**Decision:** `ReminderTask` gains one nullable property, `int? MessageId`, mapped by
`ReminderTaskConfiguration` as a nullable `integer` with no check constraint.

**Why the model, not a lookup elsewhere.** `MessageHandler` (scoped per update) and
`DueReminderJob` (a singleton ticking on its own schedule) can be separated by a process restart
between a capture and its own reminder firing hours or days later — only the row itself is
guaranteed to outlive both. A process-local dictionary would lose every entry on restart, silently
regressing to today's bug for exactly the tasks most likely to be affected: ones captured
recently, still pending. A side table would need its own foreign key, migration, and join for one
`int?` with exactly one owner and one reader-pair — the same shape `ReminderSentAt` already has.

**Why nullable, not a sentinel such as `0`.** Telegram assigns message ids; no `int` value is
safely reserved as "unset." `null` already carries this meaning elsewhere on this model —
`DueAt`, `ReminderSentAt`, `CompletedAt` — for the identical reason: a column added after rows
exist needs an honest answer for what an old row means, and "not applicable yet" is that answer
for every task on the day this migration runs.

### 2. `INotifier.SendTaskAsync` is replaced in place by `AnnounceTaskAsync` — no method is added
   beside it

**Decision:**

```csharp
Task<int> AnnounceTaskAsync(int? previousMessageId, Guid taskId, string text, CancellationToken ct);
```

`previousMessageId` comes first, matching `MarkCompletedTaskAsync(int messageId, ...)` and
`UpdateTaskAsync(int messageId, ...)`, which already put the message identifier first. The method
returns the identifier of the message it just created — the one thing no earlier `INotifier`
method has needed to hand back, because no earlier caller needed to remember what it had just sent.

**Why a replacement, not a second method kept alongside the first.** "Verified facts" already
establishes `SendTaskAsync` has exactly two call sites, zero test references by name, and one
implementation with no test fake — nothing this rename could break silently. Every caller that
used to send a task's first announcement now also owns the previous message id (`null`, for a
task never announced), so the old zero-context signature has no honest caller left. Keeping both
would leave two ways to announce a task, one of which reintroduces the bug the moment anything
called it again.

### 3. `TelegramNotifier`: send first, then delete — and the delete is best-effort

**Decision:**

```csharp
var message = await bot.SendMessage(
    settings.OwnerChatId, Escape(text), ParseMode.Html,
    replyMarkup: BuildKeyboard(taskId, TaskKeyboard.Actions), cancellationToken: ct);

if (previousMessageId is { } previous)
{
    try
    {
        await bot.DeleteMessage(settings.OwnerChatId, previous, ct);
    }
    catch (RequestException)
    {
        logger.LogWarning(
            "Could not delete the previous message {MessageId} announcing task {TaskId}.",
            previous, taskId);
    }
}

return message.Id;
```

**Why send first, never the reverse.** `MessageHandler`'s class doc already set this precedent for
the owner's own inbound message: the delete runs only after the thing that matters has been
delivered, so a delete failure can never cost a delivery. Spec §6.2's "send, then mark" is the same
instinct applied one write earlier. Reversing the order here — delete, then send — would mean a
failed send after a successful delete leaves the owner with nothing: no old message, no new one,
no reminder at all. A stray leftover message is a blemish; a swallowed reminder is the product
failing at its one job.

**Why the delete is best-effort.** Exactly `MessageHandler.cs:152-159`'s own pattern: by the time
it runs, the new message is already live, so a failure to remove the stale one — too old to
delete, already removed by the owner, a transient error — changes nothing about whether the task
is announced. Surfacing it to the owner would turn an invisible bookkeeping miss into a visible,
alarming one for a condition that costs nothing.

### 4. Ordering: `RecordMessageAsync` runs before `MarkReminderSentAsync`, never after

**Decision:** at both call sites:

```csharp
var messageId = await notifier.AnnounceTaskAsync(task.MessageId, task.Id, task.ToMessageText(clock), ct);

await taskService.RecordMessageAsync(task.Id, messageId, ct);
await taskService.MarkReminderSentAsync(task.Id, ct);   // DueReminderJob only
```

**Why this ordering is load-bearing.** If the process dies between the two writes, the row has a
fresh `MessageId` but `ReminderSentAt` still `null` — still owed a reminder by every predicate
`GetDueRemindersAsync` checks, so the next tick picks it up and correctly replaces the message it
just sent, using the `MessageId` the crashed write did manage to record. The reverse ordering would
leave a row that looks fully delivered but has no `MessageId` to clean up if the process dies in
the gap: that message is now a permanent orphan, since nothing will ever fire for this task again
to give a future call a `previousMessageId` to replace it with. The same at-least-once posture
spec §6.2 already commits to for delivery, applied one write earlier.

### 5. `CallbackRouter` needs no change at all

**Decision:** zero lines of `src/Assistant.Impl/Telegram/CallbackRouter.cs` change in this plan.

**Why.** `CallbackRouter.HandleAsync` edits the message the tap it is handling arrived on
(`messageId` bound straight from `callbackQuery.Message.Id`). Under the one-live-message
invariant, the message a tap arrives on is — by construction — the only live one. There is no
second message for `CallbackRouter` to know about.

**The stale-keyboard second-order bug is fixed by this invariant, not by any code aimed at it.**
Today, with two live messages both carrying a Done button, completing the task from either one
completes it once; the *other* message's Done button, tapped afterward, correctly answers
`ErrorCode.TaskAlreadyCompleted` — nothing corrupts, the chat just shows a task in two
contradictory states until someone taps the stale button. Once there is only ever one live
message, there is no second button left to grow stale. No test targets this directly for the same
reason no code does: it disappears as a consequence of `AnnounceTaskAsync` always replacing rather
than adding.

### 6. The migration: nullable, no check constraint, generated, not hand-written

**Decision:** `dotnet ef migrations add AddMessageId --project src/Assistant.Repository
--startup-project src/Assistant.Worker`, generated, not typed by hand. One column:

```csharp
migrationBuilder.AddColumn<int>(name: "message_id", table: "reminder_tasks", type: "integer", nullable: true);
```

**Why no check constraint, when `AddCompletedAt` — this plan's own precedent — added one.**
`ck_reminder_tasks_completed_consistency` enforces `(status = 2) = (completed_at IS NOT NULL)`
because those two columns must always agree; a mismatch would be a data-integrity bug. `MessageId`
has no analogous pairing, and every candidate constraint fails immediately against rows that
already exist: a task captured last week, reminder already delivered, has `ReminderSentAt` set and
`MessageId` forever `null`, since it was announced before this column existed. A constraint the
rows already in the table would violate on day one is not an invariant — it is a bug waiting to
reject a legitimate historical row.

**Why generated, and why the `.Designer.cs` and snapshot are committed alongside it.** Hand-typing
risks the `Designer.cs` drifting from what `ModelBuilder` would actually produce — a mismatch that
surfaces confusingly at the *next* migration, not immediately. `AddCompletedAt.Designer.cs` and
`AssistantDbContextModelSnapshot.cs` were both committed alongside `AddCompletedAt.cs` in
`bc0c9cc`; this migration follows the same three-file shape.

### 7. The fired message now renders through `ToMessageText` — a deliberate, approved change

**Decision:** `DueReminderJob` renders `task.ToMessageText(clock)`, the identical call
`MessageHandler` and `CallbackRouter` already make, in place of today's bare `task.Title`, needing
`ILocalTimeResolver` injected into `DueReminderJob` for the first time.

**Why here, not a separate slice.** This falls directly out of `AnnounceTaskAsync` having one
signature for both callers, with `text` supplied by the caller in both cases. A different
rendering rule for `DueReminderJob` would mean its two callers disagree about what "the text this
method renders" means — exactly the caller-specific branching Decision 2 already rejects.

**Why it is the *right* change, not merely a convenient one.** `CallbackRouter` already re-renders
through `ToMessageText` on every successful non-completing action (line 144, unchanged) — so
today, a reminder that fires with the bare title and is then snoozed with `+1h` visibly *grows* a
due-time line it did not have a moment before, purely because of which code path touched the
message last. One rendering function, called everywhere a task's text is ever written, removes
that inconsistency instead of adding a third rule to reconcile later — called out here as
deliberate and owner-approved, per this plan's commissioning brief.

### 8. The test-stub problem: a helper on `WireMockFixture`, not a change to the shared stub

**Decision:** `tests/Assistant.WireMock/TelegramStubs.cs` is not touched. Two new methods on
`WireMockFixture.cs`, built on the existing private `PutMappingAsync`
(`WireMockFixture.cs:434-487`), each with its own mapping GUID so it can be replaced mid-test —
full code in "Steps."

**Why this is the part most likely to be missed.** `TelegramStubs.cs`'s `SendMessageResponse`
answers **every** `sendMessage` with the literal `"message_id":1`, today read by nothing. The
moment `AnnounceTaskAsync` returns that value, a test asserting "the message this reminder deleted
is the one the capture created" would pass by construction — both sends return `1`, so any
delete-id assertion trivially matches regardless of whether the code is correct.
`SeedNextMessageIdAsync(int messageId)` lets a test make the capture and the fire return two
genuinely different ids (`100`, then `200`), so a deleted-message assertion actually exercises the
code. `SeedDeleteMessageFailureAsync()` similarly makes `deleteMessage` fail in the exact shape
"Verified facts" confirmed triggers `ApiRequestException`, for the fourth business test.

**Why `WireMockFixture.cs`, not `TelegramStubs.cs` itself.** `TelegramStubs.cs` installs the
*baseline* every test starts from, matched by path alone, with no test-specific state. Every
test-specific override that already exists (`SeedUpdatesAsync`, `SeedCallbackQueryUpdatesAsync`,
`SeedAiAnswerAsync`) lives on `WireMockFixture` instead, at a priority that beats the baseline for
one test and is cleaned up in `ResetAsync` — the fixture's own established idiom, extended rather
than replaced.

### 9. No new escaping fact in `TelegramNotifierTests.cs` for `AnnounceTaskAsync`

**Decision:** `TelegramNotifierTests.cs` gains exactly one line (`services.AddLogging();`) and no
new `[Fact]`.

**Why, when `MarkCompletedTaskAsync`, `UpdateTaskAsync`, and `ShowKeyboardAsync` each have their
own escaping fact.** Each exists to prove a *new call path* reaches the shared private `Escape`
method. `AnnounceTaskAsync` calls the identical `Escape(text)` through the identical
`bot.SendMessage(...)` shape `SendAsync` already has two escaping facts for
(angle-brackets-and-ampersand, and Hebrew passthrough). A fifth fact differing only in which
method name calls `Escape` would prove `Escape` is called — which four existing facts, across two
send-shaped and two edit-shaped methods, already establish. The four new `DueReminderJobTests`
facts (Decision 8) exercise `AnnounceTaskAsync` end to end through the real `TelegramNotifier`,
which is a stronger proof the method works than a fifth isolated escaping fact would add.

### 10. `docs/e2e-local.md` also needs a same-commit correction — beyond this plan's original doc
    scope

**Decision:** beyond the two `slice-1-reminders.md` edits this plan's brief names,
`docs/e2e-local.md`'s step 5 is also corrected.

**Why this goes beyond the stated scope, and why that is the right call.** `AGENTS.md`'s rule is
unconditional: update a doc in the same commit if the change alters a documented decision.
Step 5 shows the exact request body a reader is told to expect after seeding a due row —
`{"chat_id":<your-chat-id>,"text":"Call the bank","parse_mode":"Html"}` — explained as "the bare
task title, with no prefix." Once Decision 7 lands, that is simply false: the same seeded row now
produces `text` carrying the rendered due-time line. This is the literal string a human is told to
look for while verifying a release by hand — leaving it stale would mean the next person to run
this walkthrough sees a body that does not match what the doc promised.

**Why the fix stays small.** Only the illustrated `text` value and its explanatory sentence
change, using the document's own `<placeholder>` convention (the exact due time depends on when
the reader runs the step). Every other step — the seed `INSERT`, the row check, the at-most-once
check, Done/Schedule/Back/`+1h` — is unaffected: none of them depend on what `text` contains.

---

## What this slice does NOT include

- **A fix for the read-then-send race between `GetDueRemindersAsync` and a Done tap.** If the
  owner taps Done in the seconds between the job reading a row and calling `AnnounceTaskAsync` for
  it, they get a fresh live message for a task they just completed. `GetDueRemindersAsync` filters
  on `status = 1`, so the window is the gap between that read and the send — unchanged by this
  plan, and not new: it exists identically today, one line earlier. Consistent with the
  at-least-once posture spec §6.2 already commits to; named deliberately rather than engineered
  against, since closing it would add a database round trip for a window of single-digit
  milliseconds.
- **`ScheduleAction`, `CallbackRouter`, or any button-facing code** (Decision 5): the invariant
  fixes the stale-keyboard bug for free.
- **`delivery_attempts`, a retry cap, or any change to retry counts.** Spec §6.2 defers this to a
  column that does not exist yet.
- **A new escaping test for `AnnounceTaskAsync`** (Decision 9).
- **A full resync of the feature backlog's F10 entry against this fix.** Its "Settled at F10-4"
  bullet already forward-references "F10-5" as the work this plan performs; reconciling that entry
  to name this fix rather than an unscheduled slice is a small, real cleanup recommended but not
  performed — outside the brief's two-file doc scope, and unlike `e2e-local.md` it describes no
  fact that becomes false without it, only a reference that becomes fulfilled. Left to the reviewer.

---

## File Structure

```
src/Assistant.Models/
    ReminderTask.cs                                    + MessageId

src/Assistant.Repository/
    Configurations/ReminderTaskConfiguration.cs        + one Property line
    Migrations/<timestamp>_AddMessageId.cs              new file, generated
    Migrations/<timestamp>_AddMessageId.Designer.cs     new file, generated
    Migrations/AssistantDbContextModelSnapshot.cs       + MessageId property block

src/Assistant.Interfaces/
    INotifier.cs                                       SendTaskAsync -> AnnounceTaskAsync
    ITaskService.cs                                     + RecordMessageAsync

src/Assistant.Impl/
    Telegram/TelegramNotifier.cs                        + ILogger<TelegramNotifier>;
                                                         SendTaskAsync -> AnnounceTaskAsync
    Telegram/MessageHandler.cs                          + ITaskService; two-line capture sequence
    Services/TaskService.cs                             + RecordMessageAsync
    Services/Jobs/DueReminderJob.cs                     + ILocalTimeResolver; two-line fire sequence

tests/Assistant.IntegrationTests/
    Infrastructure/WireMockFixture.cs                   + SeedNextMessageIdAsync,
                                                         SeedDeleteMessageFailureAsync
    Telegram/TelegramNotifierTests.cs                    + AddLogging() only
    Jobs/DueReminderJobTests.cs                          + AddLogging()/AddAssistantTime() x2;
                                                         1 fact renamed and fixed; + 4 new facts

docs/
    design/slice-1-reminders.md                          §4.3, §6.2 corrected
    e2e-local.md                                         step 5 corrected (Decision 10)
```

No `Assistant.Contracts`, `Assistant.Worker`, `CallbackRouter.cs`, `CallbackCodec.cs`,
`ITaskAction.cs`, `DoneAction.cs`, `ScheduleAction.cs`, `ErrorCode.cs`, `EfTaskRepository.cs`,
`AssistantDbContext.cs`, `ImplServiceCollectionExtensions.cs`, `TelegramListenerTests.cs`,
`CallbackRouterTests.cs`, or any unit test project file is touched.

---

## Validation

**Test count arithmetic.** Baseline, measured directly against this exact `HEAD` (see "Verified
facts"): 62 unit, 82 integration.

- Unit: no unit test file changes. 62 unchanged.
- Integration: `DueReminderJobTests.RunAsync_TaskIsDue_SendsItsTitle` is renamed to
  `RunAsync_TaskIsDue_SendsItsRenderedDueTimeText` and its assertion is fixed to match Decision 7 —
  no count change. Four new `[Fact]`s land in `DueReminderJobTests.cs` in commit 3. No other
  integration test file gains or loses a fact. 82 + 4 = **86**.

**Expected final state: 62 unit, 86 integration.** Stated as an expectation this plan's code is
written to satisfy, not a rerun this planning session captured — see Global Constraints for why
that distinction is drawn explicitly throughout this document.

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
```

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Passed!  - Failed: 0, Passed: 62, Skipped: 0, Total: 62 - Assistant.UnitTests.dll
```

```bash
docker compose -f compose.test.yaml up -d          # Postgres on :55432, WireMock on :58080
dotnet test tests/Assistant.IntegrationTests --no-build
```

```
Passed!  - Failed: 0, Passed: 86, Skipped: 0, Total: 86 - Assistant.IntegrationTests.dll
```

```bash
docker compose -f compose.test.yaml down -v        # when finished
```

**On `--build`.** `compose.test.yaml up -d` needs no `--build` for this plan's own tests: nothing
here changes `tests/Assistant.WireMock/TelegramStubs.cs`, the only file baked into the `wiremock`
image (Decision 8). If the containers are already running from an earlier session, `up -d` with no
flags is a no-op against them.

**Migration application.** `PostgresFixture.InitializeAsync` (used by every integration test)
calls `AddAssistantRepository` then `MigrateAssistantDatabaseAsync()` against the test database on
every test run — the new `AddMessageId` migration is picked up automatically the first time the
integration suite runs after this plan's commit 1 lands, with no separate manual step.

---

## Steps

**Decisions this slice carries:** all ten, above. **Consumes:** `ReminderTaskMappingExtensions.ToMessageText`
(F10-3), `INotifier.ShowKeyboardAsync`/`TaskKeyboard` (F11-4a, read, not modified),
`ITaskService.MarkReminderSentAsync` (F5a/F5b, unmodified). **Produces:** everything in "File
Structure."

**Why three commits, not one.** Every merged PR on this repository's `main` is a single squash
commit — confirmed directly: PR #23 (`bc0c9cc`) carried four commits on its branch and landed as
one; PR #37 (`2917399`) shows the same. The commit boundaries below are a **review aid**, not a
commitment about `main`'s history — a reviewer reads three checkpoints, each buildable and each
leaving the suite green, none of it surviving past the merge. That freedom is what makes a
schema-only first commit reasonable despite the feature backlog's YAGNI rule ("a feature may only
introduce ... a property ... that the same feature exercises with a test"): the rule binds the
finished PR, which does exercise `MessageId` with a test, not a checkpoint two commits early.

- **Commit 1 — the invariant's home**: the model property, its mapping, and the migration. Builds
  clean; no test yet, since nothing reads the column until commit 2.
- **Commit 2 — the seam**: `INotifier`, `TelegramNotifier`, `ITaskService`, `TaskService`, both
  call sites, and the fixture fixes (`AddLogging`/`AddAssistantTime`) the existing test suite needs
  to keep passing once those types change shape. Ends at 62 unit / 82 integration, unchanged from
  today's baseline, since the four new facts are commit 3's — the one existing fact Decision 7's
  own behaviour change breaks is fixed forward here, not left red for commit 3.
- **Commit 3 — proof and documentation**: the two `WireMockFixture` helpers, the four new business
  tests, and both documentation corrections. Ends at 62/86.

### Commit 1: the invariant's home

**Files:**
- Modify: `src/Assistant.Models/ReminderTask.cs`
- Modify: `src/Assistant.Repository/Configurations/ReminderTaskConfiguration.cs`
- Add: `src/Assistant.Repository/Migrations/<timestamp>_AddMessageId.cs`,
  `<timestamp>_AddMessageId.Designer.cs`
- Modify: `src/Assistant.Repository/Migrations/AssistantDbContextModelSnapshot.cs`

- [ ] **Step 1: `ReminderTask.MessageId`**

Insert into `src/Assistant.Models/ReminderTask.cs`, between `DueAt` and `ReminderSentAt` (matching
the column's logical position: it is set around the same time as `DueAt` and read alongside
`ReminderSentAt` at both call sites):

```diff
     /// <summary>
     /// When the task is due, in UTC. Also the instant at which its reminder is delivered.
     /// </summary>
     /// <value><see langword="null"/> for a task with no deadline, which never triggers a reminder.</value>
     public DateTimeOffset? DueAt { get; set; }
 
+    /// <summary>
+    /// Identifier of the message currently announcing this task to the owner.
+    /// </summary>
+    /// <value>
+    /// <see langword="null"/> when no message announces this task yet -- true both before its
+    /// first announcement and, permanently, for every row that existed before this property was
+    /// added. Set by <c>ITaskService.RecordMessageAsync</c> once
+    /// <c>INotifier.AnnounceTaskAsync</c> returns the identifier of the message it just sent.
+    /// </value>
+    public int? MessageId { get; set; }
+
     /// <summary>
     /// When the reminder for the current <see cref="DueAt"/> was delivered, in UTC.
     /// </summary>
```

- [ ] **Step 2: map the column**

```diff
         builder.Property(x => x.DueAt).HasColumnName("due_at").HasColumnType("timestamptz");
+        builder.Property(x => x.MessageId).HasColumnName("message_id");
         builder.Property(x => x.ReminderSentAt)
             .HasColumnName("reminder_sent_at")
             .HasColumnType("timestamptz");
```

No `HasColumnType` call: EF Core's Npgsql provider maps `int?` to `integer` by convention, the same
way `Status`'s underlying `int` needs no explicit type override.

- [ ] **Step 3: generate the migration**

```bash
dotnet ef migrations add AddMessageId \
  --project src/Assistant.Repository \
  --startup-project src/Assistant.Worker
```

This produces `<timestamp>_AddMessageId.cs` and `<timestamp>_AddMessageId.Designer.cs` (the
timestamp is assigned by the tool at the moment this command runs, e.g.
`20260911120000_AddMessageId` — the exact digits are not predictable in advance and are not
significant), and updates `AssistantDbContextModelSnapshot.cs` in place. Expected `Up`/`Down`,
matching `AddCompletedAt.cs`'s own shape with no check constraint (Decision 6):

```csharp
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "message_id",
                table: "reminder_tasks",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "message_id",
                table: "reminder_tasks");
        }
    }
}
```

`<timestamp>_AddMessageId.Designer.cs` reproduces `AssistantDbContextModelSnapshot.cs`'s own
`BuildTargetModel` at the moment of generation, carrying the `[Migration("<timestamp>_AddMessageId")]`
attribute and `partial class AddMessageId`; both it and the snapshot gain the identical new
property block, alphabetically between `DueAt` and `ReminderSentAt`:

```diff
                     b.Property<DateTimeOffset?>("DueAt")
                         .HasColumnType("timestamptz")
                         .HasColumnName("due_at");
 
+                    b.Property<int?>("MessageId")
+                        .HasColumnType("integer")
+                        .HasColumnName("message_id");
+
                     b.Property<DateTimeOffset?>("ReminderSentAt")
                         .HasColumnType("timestamptz")
                         .HasColumnName("reminder_sent_at");
```

Commit both generated files exactly as the tool produces them — do not hand-edit either.

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` **62 passed** unit — unchanged, since
nothing yet reads `MessageId`.

```bash
git add src/Assistant.Models/ReminderTask.cs \
        src/Assistant.Repository/Configurations/ReminderTaskConfiguration.cs \
        src/Assistant.Repository/Migrations/
git commit
```

Message:

```
fix: #39 -- give ReminderTask a MessageId column

Records which message currently announces a task to the owner.
Nullable, no check constraint: every row that predates this migration
has no message to record, and no historical row could ever satisfy a
constraint requiring one.

Nothing reads or writes this column yet -- the seam that does is next.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
```

### Commit 2: the seam

**Files:**
- Modify: `src/Assistant.Interfaces/INotifier.cs`, `ITaskService.cs`
- Modify: `src/Assistant.Impl/Telegram/TelegramNotifier.cs`, `Telegram/MessageHandler.cs`,
  `Services/TaskService.cs`, `Services/Jobs/DueReminderJob.cs`
- Modify: `tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs`,
  `Jobs/DueReminderJobTests.cs`

- [ ] **Step 1: `INotifier.AnnounceTaskAsync` replaces `SendTaskAsync`**

```diff
-    /// <summary>
-    /// Sends a message announcing a task, with the actions its channel can offer attached as
-    /// buttons. The caller supplies no action list; which actions appear is the adapter's own
-    /// decision.
-    /// </summary>
-    /// <param name="taskId">
-    /// The task the message announces. The adapter needs this to build a channel-neutral handle
-    /// for each action it attaches -- it never sees any other part of a database shape.
-    /// </param>
-    /// <param name="text">
-    /// The message body, as plain text. The adapter escapes whatever its channel requires
-    /// before sending, so callers must not pre-escape.
-    /// </param>
-    /// <param name="ct">Cancellation token.</param>
-    /// <returns>A task that completes once the message has been accepted for delivery.</returns>
-    /// <remarks>
-    /// There is no overload that accepts a subset of actions, because no caller needs one -- the
-    /// first caller that needs a subset is the trigger for adding it.
-    /// </remarks>
-    Task SendTaskAsync(Guid taskId, string text, CancellationToken ct);
+    /// <summary>
+    /// Announces a task to the owner, with the actions its channel can offer attached as buttons,
+    /// replacing whichever message is currently announcing the same task.
+    /// </summary>
+    /// <param name="previousMessageId">
+    /// The message currently announcing this task, or <see langword="null"/> when none does --
+    /// true both before this task's first announcement and, permanently, for a task stored before
+    /// this method existed. Whether and how a previous message is actually replaced is the
+    /// adapter's own decision, not the caller's: a channel with no delete affordance is free to
+    /// implement this as a plain send, ignoring this argument beyond returning a value the next
+    /// call can pass back in.
+    /// </param>
+    /// <param name="taskId">
+    /// The task the message announces. The adapter needs this to build a channel-neutral handle
+    /// for each action it attaches -- it never sees any other part of a database shape.
+    /// </param>
+    /// <param name="text">
+    /// The message body, as plain text. The adapter escapes whatever its channel requires
+    /// before sending, so callers must not pre-escape.
+    /// </param>
+    /// <param name="ct">Cancellation token.</param>
+    /// <returns>
+    /// The identifier of the message this call created -- the caller's only source for a value to
+    /// pass as <paramref name="previousMessageId"/> the next time this task is announced.
+    /// </returns>
+    /// <remarks>
+    /// Replaces the former <c>SendTaskAsync</c>: every caller that used to send a task's first
+    /// announcement now also owns the identifier of whichever message is currently announcing it,
+    /// and passes that back in on every later announcement -- one task, one live message, its
+    /// identifier stored on the task. There is no overload that accepts a subset of actions,
+    /// because no caller needs one -- the first caller that needs a subset is the trigger for
+    /// adding it.
+    /// </remarks>
+    Task<int> AnnounceTaskAsync(int? previousMessageId, Guid taskId, string text, CancellationToken ct);
```

- [ ] **Step 2: `ITaskService.RecordMessageAsync`**

Append to `src/Assistant.Interfaces/ITaskService.cs`, after `RescheduleAsync`:

```csharp
    /// <summary>
    /// Records which message currently announces a task to the owner.
    /// </summary>
    /// <param name="id">The task the message announces.</param>
    /// <param name="messageId">The identifier of the message that now announces it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or the reason it was refused: no task carries the identifier.</returns>
    /// <remarks>
    /// Called after <see cref="ReminderTask.MessageId"/>'s new value comes back from
    /// <c>INotifier.AnnounceTaskAsync</c>, both on a task's first capture and on every later
    /// re-announcement. Overwrites whatever identifier was previously recorded without reading it
    /// first: the caller already holds the previous value, having passed it into
    /// <c>AnnounceTaskAsync</c> as its own <paramref name="messageId"/> one line earlier.
    /// </remarks>
    Task<Result> RecordMessageAsync(Guid id, int messageId, CancellationToken ct);
```

```bash
dotnet build --no-restore
```

Expected: fails with two `CS0535` errors -- `TelegramNotifier` and `TaskService` no longer
implement their interfaces.

- [ ] **Step 3: `TelegramNotifier.AnnounceTaskAsync`**

Add `ILogger<TelegramNotifier>` to the primary constructor and the usings it needs:

```diff
+using Microsoft.Extensions.Logging;
 using Telegram.Bot;
+using Telegram.Bot.Exceptions;
 using Telegram.Bot.Types.Enums;
 using Telegram.Bot.Types.ReplyMarkups;
 
 namespace Assistant.Impl.Telegram;
 
 /// <summary>
 /// Delivers messages through the Telegram Bot API.
 /// </summary>
 /// <param name="bot">The Telegram client, already pointed at a base address.</param>
 /// <param name="settings">Validated Telegram configuration.</param>
+/// <param name="logger">
+/// Where a failed best-effort delete of a task's previous announcing message is recorded.
+/// </param>
 /// <remarks>
 ... (unchanged)
 /// </remarks>
-internal sealed class TelegramNotifier(ITelegramBotClient bot, TelegramSettings settings) : INotifier
+internal sealed class TelegramNotifier(
+    ITelegramBotClient bot, TelegramSettings settings, ILogger<TelegramNotifier> logger) : INotifier
```

Replace `SendTaskAsync`:

```diff
-    /// <inheritdoc/>
-    /// <remarks>
-    /// Attaches <see cref="TaskKeyboard.Actions"/>, built by <see cref="BuildKeyboard"/> the same
-    /// way every other keyboard this adapter sends is.
-    /// </remarks>
-    public async Task SendTaskAsync(Guid taskId, string text, CancellationToken ct) =>
-        await bot.SendMessage(
-            settings.OwnerChatId, Escape(text), ParseMode.Html,
-            replyMarkup: BuildKeyboard(taskId, TaskKeyboard.Actions), cancellationToken: ct);
+    /// <inheritdoc/>
+    /// <remarks>
+    /// Attaches <see cref="TaskKeyboard.Actions"/>, built by <see cref="BuildKeyboard"/> the same
+    /// way every other keyboard this adapter sends is. The send runs first so a delete failure
+    /// can never cost the owner a reminder; the delete that follows is therefore best-effort,
+    /// logged at warning and never surfaced.
+    /// </remarks>
+    public async Task<int> AnnounceTaskAsync(
+        int? previousMessageId, Guid taskId, string text, CancellationToken ct)
+    {
+        var message = await bot.SendMessage(
+            settings.OwnerChatId, Escape(text), ParseMode.Html,
+            replyMarkup: BuildKeyboard(taskId, TaskKeyboard.Actions), cancellationToken: ct);
+
+        if (previousMessageId is { } previous)
+        {
+            try
+            {
+                await bot.DeleteMessage(settings.OwnerChatId, previous, ct);
+            }
+            catch (RequestException)
+            {
+                logger.LogWarning(
+                    "Could not delete the previous message {MessageId} announcing task {TaskId}.",
+                    previous, taskId);
+            }
+        }
+
+        return message.Id;
+    }
```

- [ ] **Step 4: `TaskService.RecordMessageAsync`**

Append to `src/Assistant.Impl/Services/TaskService.cs`, after `RescheduleAsync`:

```csharp
    /// <inheritdoc/>
    public async Task<Result> RecordMessageAsync(Guid id, int messageId, CancellationToken ct)
    {
        var task = await repository.FindAsync(id, ct);

        if (task is null)
        {
            return Result.Failure(ErrorCode.TaskNotFound);
        }

        task.MessageId = messageId;
        task.UpdatedAt = timeProvider.GetUtcNow();
        await repository.UpdateAsync(task, ct);

        return Result.Success();
    }
```

```bash
dotnet build --no-restore
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` The interface implementations are complete;
the two call sites still reference the removed `SendTaskAsync` name next.

- [ ] **Step 5: `MessageHandler` -- inject `ITaskService`, rewire the capture sequence**

```diff
 /// <param name="notifier">Where the reply is delivered.</param>
+/// <param name="taskService">Records which message now announces a captured task.</param>
 /// <param name="ai">Reaches the configured chat model for an answer.</param>
 ... (unchanged doc comments)
 internal sealed class MessageHandler(
     TelegramSettings settings,
     ITelegramBotClient bot,
     INotifier notifier,
+    ITaskService taskService,
     IAiClient ai,
     IEnumerable<IAssistantTool> tools,
     ILocalTimeResolver clock,
     ILogger<MessageHandler> logger)
     : ITelegramUpdateHandler
```

```diff
         var task = outcome.Value!;
-        await notifier.SendTaskAsync(task.Id, task.ToMessageText(clock), ct);
+        var announcedMessageId = await notifier.AnnounceTaskAsync(
+            task.MessageId, task.Id, task.ToMessageText(clock), ct);
+
+        await taskService.RecordMessageAsync(task.Id, announcedMessageId, ct);
 
         try
         {
             await bot.DeleteMessage(chatId, messageId, ct);
         }
         catch (RequestException)
         {
             logger.LogWarning("Could not delete the owner's message {MessageId}.", messageId);
         }
```

**Note the local variable name.** `messageId` is already bound in this method (the inbound
`update.Message`'s own id, used two lines below for the owner's-own-message delete) — naming the
announced id `messageId` too would collide (`CS0136`). `ITaskService` is not already injected into
this class, confirmed by reading its current constructor (`settings`, `bot`, `notifier`, `ai`,
`tools`, `clock`, `logger` only).

- [ ] **Step 6: `DueReminderJob` -- inject `ILocalTimeResolver`, rewire the fire sequence**

```diff
+using Assistant.Impl.Mapping;
 using Assistant.Impl.Scheduling;
 using Assistant.Interfaces;
 using Microsoft.Extensions.DependencyInjection;
 
 namespace Assistant.Impl.Services.Jobs;
 
 /// <summary>
 /// Delivers reminders whose due time has passed.
 /// </summary>
 /// <param name="scopeFactory">
 /// Opens the scope <see cref="ITaskService"/> is resolved from, because this job is a singleton
 /// and the service depends on the scoped database context.
 /// </param>
 /// <param name="notifier">Where a due reminder's message is delivered.</param>
+/// <param name="clock">Renders a stored due instant back in the configured local zone.</param>
 /// <remarks>
 /// Registered as a singleton so the re-entrancy guard on <see cref="ScheduledJobBase"/> refers to
 /// a stable instance across ticks.
 /// </remarks>
-internal sealed class DueReminderJob(IServiceScopeFactory scopeFactory, INotifier notifier)
-    : ScheduledJobBase
+internal sealed class DueReminderJob(
+    IServiceScopeFactory scopeFactory, INotifier notifier, ILocalTimeResolver clock)
+    : ScheduledJobBase
```

```diff
         foreach (var task in tasks)
         {
-            await notifier.SendTaskAsync(task.Id, task.Title, ct);
-            await taskService.MarkReminderSentAsync(task.Id, ct);
+            var messageId = await notifier.AnnounceTaskAsync(
+                task.MessageId, task.Id, task.ToMessageText(clock), ct);
+
+            await taskService.RecordMessageAsync(task.Id, messageId, ct);
+            await taskService.MarkReminderSentAsync(task.Id, ct);
         }
```

No naming collision here: the loop's own variable is `task`, and no other local named `messageId`
exists in this scope, unlike `MessageHandler`'s.

```bash
dotnet build --no-restore
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 7: fix the two test-fixture gaps `ILogger<TelegramNotifier>` and
  `ILocalTimeResolver` now expose**

In `tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs`, add one line to
`InitializeAsync`:

```diff
     public Task InitializeAsync()
     {
         var services = new ServiceCollection();
+        services.AddLogging();
         services.AddAssistantTelegram(new TelegramSettings
```

In `tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs`, the same addition plus
`AddAssistantTime`, in both places a `ServiceCollection` is built:

```diff
     public async Task InitializeAsync()
     {
         var services = new ServiceCollection();
+        services.AddLogging();
         services.AddAssistantRepository(postgres.ConnectionString);
         services.AddAssistantServices();
         services.AddAssistantTelegram(new TelegramSettings
         {
             BotToken = BotToken, OwnerChatId = OwnerChatId, BaseUrl = wireMock.Url,
         });
+        services.AddAssistantTime(new TimeSettings { IanaTimeZone = "Asia/Jerusalem" });
         services.AddAssistantScheduler();
         _provider = services.BuildServiceProvider();
         _sut = _provider.GetRequiredService<IScheduledJob>();
+        _taskService = _provider.GetRequiredService<ITaskService>();
+        _notifier = _provider.GetRequiredService<INotifier>();
+        _clock = _provider.GetRequiredService<ILocalTimeResolver>();
 
         await postgres.ResetAsync();
         await wireMock.ResetAsync();
     }
```

and in `RunAsync_DeliveryFails_TaskIsStillDue`'s own ad hoc setup:

```diff
         var services = new ServiceCollection();
+        services.AddLogging();
         services.AddAssistantRepository(postgres.ConnectionString);
         services.AddAssistantServices();
         services.AddAssistantTelegram(new TelegramSettings
         {
             BotToken = BotToken, OwnerChatId = OwnerChatId, BaseUrl = UnreachableBaseUrl,
         });
+        services.AddAssistantTime(new TimeSettings { IanaTimeZone = "Asia/Jerusalem" });
         services.AddAssistantScheduler();
         await using var provider = services.BuildServiceProvider();
         var sut = provider.GetRequiredService<IScheduledJob>();
```

Without this fix, resolving `IScheduledJob` throws during `InitializeAsync` for a DI wiring reason
unrelated to what each test actually checks. In `RunAsync_DeliveryFails_TaskIsStillDue`
specifically, an unfixed gap would make `Assert.ThrowsAnyAsync<Exception>` pass for the wrong
reason — a resolution failure instead of the unreachable-host failure the test exists to prove.

Add the three new fields this step's edits reference, and the `using` they need, near the top of
`DueReminderJobTests.cs`:

```diff
 using Assistant.Contracts;
 using Assistant.Impl;
+using Assistant.Impl.Mapping;
 using Assistant.Impl.Settings;
 using Assistant.Impl.Telegram;
 using Assistant.IntegrationTests.Infrastructure;
 using Assistant.Interfaces;
 using Assistant.Repository;
 using Microsoft.Extensions.DependencyInjection;
 using static Assistant.IntegrationTests.Infrastructure.ReminderTaskBuilder;
```

```diff
     private ServiceProvider _provider = null!;
 
     private IScheduledJob _sut = null!;
 
+    private ITaskService _taskService = null!;
+
+    private INotifier _notifier = null!;
+
+    private ILocalTimeResolver _clock = null!;
+
     /// <inheritdoc/>
     public async Task InitializeAsync()
```

- [ ] **Step 8: fix the one existing fact Decision 7's rendering change breaks**

```diff
     /// <summary>
     /// When a task is due
     /// And the job runs
-    /// Then exactly one message is sent, carrying the task's title.
+    /// Then exactly one message is sent, carrying its rendered due-time text.
     /// </summary>
     [Fact]
-    public async Task RunAsync_TaskIsDue_SendsItsTitle()
+    public async Task RunAsync_TaskIsDue_SendsItsRenderedDueTimeText()
     {
         // Arrange
         var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
         await postgres.SaveAsync(task);
 
         // Act
         await _sut.RunAsync(CancellationToken.None);
 
         // Assert
         var sent = Assert.Single(await wireMock.SentMessagesAsync());
-        Assert.Equal(task.Title, sent.Text);
+        Assert.Equal(task.ToMessageText(_clock), sent.Text);
     }
```

Fixed forward in this same commit, not left red for commit 3, per "Steps"'s opening paragraph.

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` **62 passed** unit. **82 passed**
integration -- unchanged in count from the measured baseline; this commit changes what several
existing facts prove, not how many exist.

```bash
git add src/Assistant.Interfaces/INotifier.cs src/Assistant.Interfaces/ITaskService.cs \
        src/Assistant.Impl/Telegram/TelegramNotifier.cs \
        src/Assistant.Impl/Telegram/MessageHandler.cs \
        src/Assistant.Impl/Services/TaskService.cs \
        src/Assistant.Impl/Services/Jobs/DueReminderJob.cs \
        tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs \
        tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs
git commit
```

Message:

```
fix: #39 -- one task, one live message

INotifier.SendTaskAsync becomes AnnounceTaskAsync: it takes the
message currently announcing a task (or null) and returns the id of
whichever message it sends, deleting the previous one best-effort
after the new one is confirmed sent -- never before, so a delete
failure can never cost a delivery. ITaskService gains
RecordMessageAsync to persist that id onto the task.

Both call sites -- MessageHandler's capture reply and DueReminderJob's
fired reminder -- run the identical sequence: announce, then record,
then (DueReminderJob only) mark the reminder sent. Recording before
marking means a crash between the two writes leaves the task still
due, correctly re-announced and replacing its own half-delivered
message on the next tick rather than stranding it forever.

DueReminderJob now renders through ToMessageText, the same renderer
MessageHandler and CallbackRouter already use, in place of the bare
title it sent before -- CallbackRouter already re-rendered this way on
a +1h tap, so a bare-title reminder silently grew a due line the
moment it was snoozed. One renderer everywhere removes that.

CallbackRouter is unchanged: it already edits the message a tap came
from, which is the only live message once this invariant holds.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
```

### Commit 3: proof and documentation

**Files:**
- Modify: `tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs`,
  `Jobs/DueReminderJobTests.cs`
- Modify: `docs/design/slice-1-reminders.md`, `docs/e2e-local.md`

- [ ] **Step 1: `WireMockFixture` gains two seeding helpers**

Add two mapping GUIDs, alongside the existing three:

```diff
     private static readonly Guid AiMapping =
         new("f9a00000-0000-0000-0000-000000000001");
 
+    private static readonly Guid SendMessageOverrideMapping =
+        new("f7000000-0000-0000-0000-000000000003");
+
+    private static readonly Guid DeleteMessageFailureMapping =
+        new("f7000000-0000-0000-0000-000000000004");
+
     private readonly HttpClient _http = new();
```

Include both in `ResetAsync`'s cleanup:

```diff
     public async Task ResetAsync()
     {
-        foreach (var id in new[] { PendingUpdatesMapping, DrainedUpdatesMapping, AiMapping })
+        foreach (var id in new[]
+        {
+            PendingUpdatesMapping, DrainedUpdatesMapping, AiMapping,
+            SendMessageOverrideMapping, DeleteMessageFailureMapping,
+        })
         {
             (await _http.DeleteAsync($"{Url}/__admin/mappings/{id}")).Dispose();
         }
```

Add the two helpers, after `SeedAiNoAnswerAsync`:

```csharp
    /// <summary>
    /// Makes the stub answer the next sendMessage request with the given message id, in place of
    /// the constant 1 that <c>TelegramStubs</c>'s own base mapping always returns.
    /// </summary>
    /// <param name="messageId">The id to answer with.</param>
    /// <returns>A task that completes once the override mapping is installed.</returns>
    /// <remarks>
    /// Installed at priority -1, confirmed directly against the running stub to beat
    /// <c>TelegramStubs</c>'s own unprioritised <c>/bot*/sendMessage</c> mapping, which behaves as
    /// priority 0: a mapping installed at priority 1 for the same path lost to it, and priority -1
    /// won. Uses its own guid, distinct from every other seeded mapping, so a test can call this
    /// twice -- once before a capture, again before a fire -- to make the two sends
    /// distinguishable by id, which is the whole point: <c>TelegramStubs</c>'s own constant would
    /// make both sends indistinguishable and any delete-id assertion trivially true.
    /// </remarks>
    public Task SeedNextMessageIdAsync(int messageId) =>
        PutMappingAsync(SendMessageOverrideMapping, "/bot*/sendMessage", priority: -1,
            bodyPattern: null, statusCode: 200,
            responseBody: new JsonObject
            {
                ["ok"] = true,
                ["result"] = new JsonObject
                {
                    ["message_id"] = messageId,
                    ["date"] = 1756000000L,
                    ["chat"] = new JsonObject { ["id"] = 1, ["type"] = "private" },
                    ["text"] = "stubbed",
                },
            },
            delayMs: null);

    /// <summary>
    /// Makes the next deleteMessage request fail the way a real too-old-to-delete message would.
    /// </summary>
    /// <returns>A task that completes once the failure mapping is installed.</returns>
    /// <remarks>
    /// Installed at priority -1 for the same reason <see cref="SeedNextMessageIdAsync"/> is. The
    /// status code and body were confirmed directly against a standalone <c>Telegram.Bot</c>
    /// client pointed at this stub: HTTP 400 with <c>{"ok":false,...}</c> is what makes the client
    /// throw <c>Telegram.Bot.Exceptions.ApiRequestException</c>, which derives from
    /// <c>RequestException</c> -- the type <c>TelegramNotifier.AnnounceTaskAsync</c> catches
    /// around its own best-effort delete.
    /// </remarks>
    public Task SeedDeleteMessageFailureAsync() =>
        PutMappingAsync(DeleteMessageFailureMapping, "/bot*/deleteMessage", priority: -1,
            bodyPattern: null, statusCode: 400,
            responseBody: new JsonObject
            {
                ["ok"] = false,
                ["error_code"] = 400,
                ["description"] = "Bad Request: message to delete not found",
            },
            delayMs: null);
```

```bash
dotnet build --no-restore
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2: the four business tests**

Add to `tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs`, after
`RunAsync_TaskIsDue_SendsItsRenderedDueTimeText`:

```csharp
    /// <summary>
    /// When a task was announced before its reminder fires
    /// And the reminder fires
    /// Then the chat holds one live message, not two.
    /// </summary>
    [Fact]
    public async Task RunAsync_TaskWasAnnouncedBeforeItFires_DeletesThePreviousMessage()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);
        await wireMock.SeedNextMessageIdAsync(100);
        var announcedMessageId = await _notifier.AnnounceTaskAsync(
            task.MessageId, task.Id, task.ToMessageText(_clock), CancellationToken.None);
        await _taskService.RecordMessageAsync(task.Id, announcedMessageId, CancellationToken.None);
        await wireMock.SeedNextMessageIdAsync(200);

        // Act
        await _sut.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, (await wireMock.SentMessagesAsync()).Count);
        var deleted = Assert.Single(await wireMock.DeletedMessagesAsync());
        Assert.Equal(100, deleted.MessageId);
    }

    /// <summary>
    /// When a task was announced before its reminder fires
    /// And the reminder fires
    /// Then the fired message shows the same due-time text the announcement showed.
    /// </summary>
    [Fact]
    public async Task RunAsync_TaskWasAnnouncedBeforeItFires_RendersTheSameTextAsTheAnnouncement()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);
        await wireMock.SeedNextMessageIdAsync(100);
        var announcedMessageId = await _notifier.AnnounceTaskAsync(
            task.MessageId, task.Id, task.ToMessageText(_clock), CancellationToken.None);
        await _taskService.RecordMessageAsync(task.Id, announcedMessageId, CancellationToken.None);
        await wireMock.SeedNextMessageIdAsync(200);

        // Act
        await _sut.RunAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.SentMessagesAsync();
        Assert.Equal(2, sent.Count);
        Assert.Equal(task.ToMessageText(_clock), sent[0].Text);
        Assert.Equal(sent[0].Text, sent[1].Text);
    }

    /// <summary>
    /// When a task stored before this change fires
    /// And it carries no MessageId
    /// Then it is announced
    /// And nothing is deleted.
    /// </summary>
    [Fact]
    public async Task RunAsync_TaskHasNoStoredMessageId_AnnouncesItAndDeletesNothing()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);

        // Act
        await _sut.RunAsync(CancellationToken.None);

        // Assert
        Assert.Single(await wireMock.SentMessagesAsync());
        Assert.Empty(await wireMock.DeletedMessagesAsync());
    }

    /// <summary>
    /// When a task was announced before its reminder fires
    /// And its previous message cannot be deleted
    /// Then the reminder still arrives.
    /// </summary>
    [Fact]
    public async Task RunAsync_ThePreviousMessageCannotBeDeleted_TheReminderStillArrives()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);
        await wireMock.SeedNextMessageIdAsync(100);
        var announcedMessageId = await _notifier.AnnounceTaskAsync(
            task.MessageId, task.Id, task.ToMessageText(_clock), CancellationToken.None);
        await _taskService.RecordMessageAsync(task.Id, announcedMessageId, CancellationToken.None);
        await wireMock.SeedNextMessageIdAsync(200);
        await wireMock.SeedDeleteMessageFailureAsync();

        // Act
        await _sut.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, (await wireMock.SentMessagesAsync()).Count);
    }
```

Each test simulates a "capture" the way `MessageHandler` performs one — `AnnounceTaskAsync` then
`RecordMessageAsync` — against the real `TelegramNotifier`/`TaskService` resolved from `_provider`,
without the full AI-and-listener stack `TelegramListenerTests.cs` already exercises for capture
itself. The same economy `DueReminderJobTests.cs` already practises: test the job, not the system.

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` **62 passed** unit. **86 passed**
integration.

- [ ] **Step 3: `docs/design/slice-1-reminders.md` §4.3**

```diff
     due_at             TIMESTAMPTZ,              -- UTC; also the reminder time
     reminder_sent_at   TIMESTAMPTZ,              -- NULL = delivery still owed
+    message_id         INT,                      -- id of the message currently announcing this task; NULL = not yet announced
     delivery_attempts  INT NOT NULL DEFAULT 0,
```

```diff
 - **`due_at` doubles as the reminder time.** One field. Splitting it is additive later.
 - **`reminder_sent_at` is the idempotency key.** The scheduler selects only rows where it is NULL, which makes restart catch-up and duplicate suppression the same mechanism.
+- **`message_id` is how one task keeps one live message.** Every announcement — a capture reply or a fired reminder — reads and replaces it, so the chat never shows two live messages for the same task. NULL for a task that predates this column, or one never yet announced.
 - **`daily_brief_log.brief_date` as primary key** makes the insert itself the once-per-day check. No race condition is possible.
```

- [ ] **Step 4: `docs/design/slice-1-reminders.md` §6.2**

Append after the existing "Deferred: there is no `delivery_attempts` column yet..." paragraph:

```diff
 **Delivery ordering: send, then mark.** The reverse (mark, then send) loses a reminder when the send fails after the write. At-least-once is the correct trade for this product. `delivery_attempts` caps retries at 3 so a persistent failure cannot loop indefinitely. **Deferred:** there is no `delivery_attempts` column yet and no retry cap — a persistent failure today retries forever, once per tick, rather than giving up after three.
+
+**Settled fixing issue #39:** delivery now *replaces* the task's live message rather than adding a second one. `DueReminderJob` reads the task's own `message_id`, passes it to the notifier as the message to replace, and records whatever new id comes back before marking the reminder sent — the same order the send-then-mark rule above already argues for, applied one write earlier. This is §6.4's "edit the original message in place, so the chat stays clean" rule, finally holding for a fired reminder and not only for a button tap.
```

- [ ] **Step 5: `docs/e2e-local.md` step 5 (Decision 10)**

```diff
-You should see the request body printed on its own:
+You should see the request body printed on its own, `text` now carrying the rendered due-time
+line rather than the bare title:
 
 ```
-{"chat_id":<your-chat-id>,"text":"Call the bank","parse_mode":"Html"}
+{"chat_id":<your-chat-id>,"text":"Call the bank -- due <weekday> <day> <month> <year>, <HH:mm>.","parse_mode":"Html"}
 ```
 
-Note that `text` is the bare task title, with no prefix — that is the behaviour conventions
-§12.6 settled: the message arrives from the assistant, in a chat only the assistant writes to,
-so there is nothing for a prefix to disambiguate.
+The exact due time depends on when you run this step, since the seeded row's `due_at` is always
+"one hour ago" relative to the moment you insert it. `text` reads `{title} -- due {local time}.`
+— the same rendering every task message uses, since issue #39's fix. There is still no added
+prefix such as "Reminder:" — that is the behaviour conventions §12.6 settled: the message arrives
+from the assistant, in a chat only the assistant writes to, so there is nothing for a prefix to
+disambiguate.
```

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` **62 passed** unit. **86 passed**
integration — matching "Validation" exactly.

```bash
git add tests/Assistant.IntegrationTests/Infrastructure/WireMockFixture.cs \
        tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs \
        docs/design/slice-1-reminders.md docs/e2e-local.md
git commit
```

Message:

```
test: #39 -- prove one task keeps one live message

Two WireMockFixture helpers, SeedNextMessageIdAsync and
SeedDeleteMessageFailureAsync, close the gap the shared stub's own
hardcoded message_id left open: without them, a test comparing "the
message deleted" against "the message the capture created" would
pass trivially, both being the same constant regardless of whether
the code under test did anything correct.

Four new DueReminderJob facts, each simulating a capture the way
MessageHandler performs one before firing the job: the chat ends with
one live message, not two; the fired text matches the announced text;
a task with no stored MessageId is announced and nothing is deleted;
a previous message that cannot be deleted never blocks delivery.

Spec §4.3/§6.2 gain message_id and the paragraph explaining delivery
now replaces rather than adds -- §6.4's own "edit in place" rule,
finally holding everywhere. e2e-local.md's step 5 is corrected to
match: the seeded reminder no longer shows the bare title.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
```

---

## Self-review

**Commit 1:**
- [ ] `ReminderTask.MessageId` is `int?`, documented with `<c>` refs, not `<see cref>`, into
      `Assistant.Interfaces` (Models references nothing); mapped with no `HasColumnType` override
      and no check constraint (Decision 6)
- [ ] Migration generated with the exact `AGENTS.md:59-63` command, never hand-written; the
      `.Designer.cs` and `AssistantDbContextModelSnapshot.cs` are committed alongside it

**Commit 2:**
- [ ] `AnnounceTaskAsync(int? previousMessageId, Guid taskId, string text, CancellationToken ct)`
      returns `Task<int>`; `previousMessageId` first, matching `MarkCompletedTaskAsync`/
      `UpdateTaskAsync` (Decision 2)
- [ ] `TelegramNotifier` sends before it deletes, never reversed; the delete is
      `try`/`catch (RequestException)`, logged at warning, never surfaced (Decision 3)
- [ ] Both call sites run `AnnounceTaskAsync` then `RecordMessageAsync` before
      `MarkReminderSentAsync` (`DueReminderJob` only), never the reverse (Decision 4)
- [ ] `MessageHandler`'s new local is `announcedMessageId`, not `messageId` — already bound to the
      inbound message id in this method
- [ ] `DueReminderJob` renders `task.ToMessageText(clock)`, not `task.Title` (Decision 7);
      `ILocalTimeResolver` is used in the same step it is added
- [ ] `CallbackRouter.cs` has zero lines changed (Decision 5)
- [ ] `TelegramNotifierTests.cs` and both `DueReminderJobTests.cs` setup blocks call
      `services.AddLogging()`; the latter also calls `services.AddAssistantTime(...)` in both
- [ ] `RunAsync_TaskIsDue_SendsItsTitle` is renamed and fixed to assert
      `task.ToMessageText(_clock)` in this same commit, not left red for commit 3
- [ ] Unit: 62, unchanged. Integration: 82, unchanged in count — only behaviour proven changes

**Commit 3:**
- [ ] `SeedNextMessageIdAsync`/`SeedDeleteMessageFailureAsync` both install at priority `-1`,
      confirmed empirically to beat `TelegramStubs`'s unprioritised base mappings (Decision 8)
- [ ] Each of the four new facts tests one business behaviour with a Gherkin `<summary>`; none
      asserts on which method was called
- [ ] `TelegramNotifierTests.cs` gains no new `[Fact]` (Decision 9)
- [ ] Spec §4.3/§6.2 corrected; `e2e-local.md` step 5 also corrected, beyond the brief's two-file
      doc scope (Decision 10) — flagged for reviewer awareness, not silently expanded
- [ ] Unit: 62. Integration: 86 — matching "Validation" exactly

**Whole PR:**
- [ ] No `git commit`/`push`/`gh pr create` run by this planning session; no `docker compose down
      -v` or unqualified `down`; no `dotnet run --project src/Assistant.Worker`,
      `send-test-message`, or `dotnet ef migrations add` — every measured fact came from reading,
      read-only `dotnet build`/`test`, or admin-API calls against the already-running containers
- [ ] Plain ASCII `--` in every C# file, XML doc, and doc-file edit; real em dashes in this
      document's own prose; no plan-internal decision number inside any code block or commit message
- [ ] Every new or changed public member carries the XML doc tags `CS1591`/`CS1573` require; every
      primary-constructor parameter is introduced in the step that first uses it; no emoji anywhere
